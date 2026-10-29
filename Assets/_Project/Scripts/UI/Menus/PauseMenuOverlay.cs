using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace P1.UI.Menus
{
    /// <summary>
    /// 全局暂停菜单与系统设置覆盖层（运行时程序化装配，零场景入侵）：
    /// 1. 独立 Overlay Canvas，sortingOrder = 100，高于 HUD 与触控层；
    /// 2. 桌面端 Esc / 手柄 Start 键触发，移动端在 Playing 阶段显示 44x44 安全区暂停按钮；
    /// 3. 暂停时：HitstopService.Cancel() → Time.timeScale = 0 → AudioListener.pause = true；
    /// 4. 恢复时反向执行，确保 Time.timeScale 归位为 1.0；离开 Playing 阶段或销毁时强制关闭；
    /// 5. 整合主音量、屏幕震动无障碍强度、帧率上限实时设置与持久化。
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuOverlay : MonoBehaviour
    {
        public static PauseMenuOverlay Instance { get; private set; }

        public event Action<bool> PauseStateChanged;

        [Header("调试 / 平台模拟")]
        [SerializeField] private bool forceShowMobileButton;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private RectTransform _safeAreaRect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private MatchManager _match;
        private readonly List<MatchManager> _matches = new List<MatchManager>(2);

        // 移动端常驻暂停小按钮 (44x44)
        private Button _mobilePauseButton;
        private RectTransform _mobilePauseButtonRt;

        // 模态弹窗与背景蒙层
        private GameObject _modalRoot;
        private RectTransform _panelRect;
        private UISpringMotion _panelMotion;
        private UIInkShapeGraphic _panelShape;

        // 视图切换容器
        private GameObject _mainViewContainer;
        private GameObject _settingsViewContainer;
        private bool _isSettingsOpen;

        // 设置项控件
        private Slider _volumeSlider;
        private TextMeshProUGUI _volumeValueText;
        private Slider _shakeSlider;
        private TextMeshProUGUI _shakeValueText;
        private Slider _mouseSensSlider;
        private TextMeshProUGUI _mouseSensValueText;
        private Slider _touchSensSlider;
        private TextMeshProUGUI _touchSensValueText;
        private Button _invertYButton;
        private TextMeshProUGUI _invertYText;
        private Button[] _fpsButtons;
        private UIInkShapeGraphic[] _fpsShapes;
        private TextMeshProUGUI[] _fpsTexts;

        // 主题与字体缓存
        private UIThemeSO _theme;
        private TMP_FontAsset _displayFont;
        private TMP_FontAsset _bodyFont;

        public bool IsPaused { get; private set; }
        public bool ForceShowMobileButton
        {
            get => forceShowMobileButton;
            set
            {
                forceShowMobileButton = value;
                UpdateMobileButtonVisibility();
            }
        }

        public static PauseMenuOverlay CreateInstance()
        {
            if (Instance != null)
            {
                // 场景切换/测试可能新建了 MatchManager，复用已有菜单时重新扫描一次订阅。
                Instance.SubscribeAllMatches();
                return Instance;
            }

            var go = new GameObject("PauseMenuOverlay");
            var overlay = go.AddComponent<PauseMenuOverlay>();
            return overlay;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveThemeAndFonts();
            EnsureEventSystem();
            BuildUI();
        }

        private void Start()
        {
            SubscribeAllMatches();
            UpdateMobileButtonVisibility();
        }

        private void OnEnable()
        {
            UpdateSafeArea();
        }

        private void OnDisable()
        {
            if (IsPaused)
            {
                Resume();
            }
        }

        private void OnDestroy()
        {
            if (IsPaused)
            {
                Resume();
            }

            if (_matches.Count > 0)
            {
                for (int i = 0; i < _matches.Count; i++)
                {
                    if (_matches[i] != null)
                        _matches[i].PhaseChanged -= OnMatchPhaseChanged;
                }
                _matches.Clear();
            }
            _match = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // 通常 Start 已经订阅；若菜单早于 MatchManager 创建，再做一次惰性挂接。
            if (_matches.Count == 0)
                SubscribeAllMatches();

            CheckScreenResolutionChange();
            HandleInput();
            UpdateMobileButtonVisibility();
        }

        /// <summary>
        /// 订阅场景内所有 MatchManager：4v4 主场景只有一个，但 PlayMode 测试会临时创建独立实例，
        /// 只认单例会导致阶段切换无法自动退出暂停。
        /// </summary>
        private void SubscribeAllMatches()
        {
            MatchManager[] found = FindObjectsByType<MatchManager>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                MatchManager match = found[i];
                if (match == null || _matches.Contains(match))
                    continue;

                _matches.Add(match);
                match.PhaseChanged += OnMatchPhaseChanged;
                if (_match == null)
                    _match = match;
            }

            if (_match == null)
                _match = MatchManager.Instance;
        }

        private void ResolveThemeAndFonts()
        {
            // 独立包入口：从 Resources/P1RuntimeAssets 取 UI 主题与字体。
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null && library.uiTheme != null)
            {
                _theme = library.uiTheme;
                if (_displayFont == null) _displayFont = _theme.displayFont;
                if (_bodyFont == null) _bodyFont = _theme.bodyFont;
            }

#if UNITY_EDITOR
            if (_theme == null)
            {
                _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>("Assets/_Project/ScriptableObjects/UITheme.asset");
            }
            if (_theme != null)
            {
                if (_displayFont == null) _displayFont = _theme.displayFont;
                if (_bodyFont == null) _bodyFont = _theme.bodyFont;
            }

            if (_displayFont == null)
            {
                _displayFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/TMP/TitanOne-Regular SDF.asset");
            }
            if (_bodyFont == null)
            {
                _bodyFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/TMP/Rubik-Bold SDF.asset");
            }
#endif
            _theme = UIThemeSO.Resolve(_theme);
            if (_displayFont == null) _displayFont = _theme.GetFont(true);
            if (_bodyFont == null) _bodyFont = _theme.GetFont(false);
            if (_displayFont == null) _displayFont = TMP_Settings.defaultFontAsset;
            if (_bodyFont == null) _bodyFont = TMP_Settings.defaultFontAsset;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem_Auto");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        private void BuildUI()
        {
            // 1. Root Canvas
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100; // 高于 HUD (0) 与 MobileTouchUI (20)

            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            var rootRt = GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 2. Safe Area Container
            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);
            _safeAreaRect = safeGo.GetComponent<RectTransform>();
            UpdateSafeArea();

            // 3. 移动端左上角暂停小按钮 (44x44)
            BuildMobilePauseButton(_safeAreaRect);

            // 4. 模态暂停与设置全屏蒙层
            BuildModalOverlay(_safeAreaRect);
        }

        private void BuildMobilePauseButton(Transform parent)
        {
            var btnGo = new GameObject("MobilePauseButton", typeof(RectTransform), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            btnGo.transform.SetParent(parent, false);

            _mobilePauseButtonRt = btnGo.GetComponent<RectTransform>();
            _mobilePauseButtonRt.anchorMin = new Vector2(0f, 1f);
            _mobilePauseButtonRt.anchorMax = new Vector2(0f, 1f);
            _mobilePauseButtonRt.pivot = new Vector2(0f, 1f);
            _mobilePauseButtonRt.anchoredPosition = new Vector2(16f, -16f);
            _mobilePauseButtonRt.sizeDelta = new Vector2(44f, 44f);

            var shape = btnGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 12f;
            shape.BorderWidth = 3f;
            shape.BorderColor = _theme.outlineColor;
            shape.color = new Color(0.12f, 0.10f, 0.18f, 0.92f);

            _mobilePauseButton = btnGo.GetComponent<Button>();
            _mobilePauseButton.onClick.AddListener(Pause);

            var motion = btnGo.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);

            var jelly = btnGo.GetComponent<UIJellyButton>();
            jelly.Configure(_theme, _mobilePauseButton, shape);

            // 暂停符号 "❚❚"
            var text = CreateTextMesh("PauseIcon", btnGo.transform, "\u275A\u275A", 16f, Color.white, _bodyFont);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            btnGo.SetActive(false);
        }

        private void BuildModalOverlay(Transform parent)
        {
            _modalRoot = new GameObject("ModalRoot", typeof(RectTransform));
            _modalRoot.transform.SetParent(parent, false);
            var modalRt = _modalRoot.GetComponent<RectTransform>();
            modalRt.anchorMin = Vector2.zero;
            modalRt.anchorMax = Vector2.one;
            modalRt.offsetMin = Vector2.zero;
            modalRt.offsetMax = Vector2.zero;

            // 半透明全屏遮罩，阻挡下方游戏内触控与准星射线
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdropGo.transform.SetParent(_modalRoot.transform, false);
            var backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = backdropGo.GetComponent<Image>();
            backdropImg.color = new Color(0.04f, 0.03f, 0.07f, 0.72f);
            backdropImg.raycastTarget = true;

            // 居中弹窗卡片
            var panelGo = new GameObject("PausePanel", typeof(RectTransform), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_modalRoot.transform, false);
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(560f, 660f);

            _panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            _panelShape.Shape = UIInkShape.RoundedRect;
            _panelShape.CornerRadius = 32f;
            _panelShape.BorderWidth = 7f;
            _panelShape.BorderColor = _theme.outlineColor;
            _panelShape.color = _theme.panelColor;

            _panelMotion = panelGo.GetComponent<UISpringMotion>();
            _panelMotion.SetTheme(_theme);

            // 1) 主暂停视图容器
            _mainViewContainer = new GameObject("MainView", typeof(RectTransform));
            _mainViewContainer.transform.SetParent(panelGo.transform, false);
            var mainRt = _mainViewContainer.GetComponent<RectTransform>();
            mainRt.anchorMin = Vector2.zero;
            mainRt.anchorMax = Vector2.one;
            mainRt.offsetMin = Vector2.zero;
            mainRt.offsetMax = Vector2.zero;
            BuildMainViewContent(_mainViewContainer.transform);

            // 2) 设置项视图容器
            _settingsViewContainer = new GameObject("SettingsView", typeof(RectTransform));
            _settingsViewContainer.transform.SetParent(panelGo.transform, false);
            var setRt = _settingsViewContainer.GetComponent<RectTransform>();
            setRt.anchorMin = Vector2.zero;
            setRt.anchorMax = Vector2.one;
            setRt.offsetMin = Vector2.zero;
            setRt.offsetMax = Vector2.zero;
            BuildSettingsViewContent(_settingsViewContainer.transform);

            _settingsViewContainer.SetActive(false);
            _modalRoot.SetActive(false);
        }

        private void BuildMainViewContent(Transform parent)
        {
            // 标题
            var title = CreateTextMesh("Title", parent, "游戏暂停", 40f, _theme.readyGold, _displayFont);
            var titleRt = title.rectTransform;
            titleRt.anchoredPosition = new Vector2(0f, 175f);

            var subtitle = CreateTextMesh("Subtitle", parent, "PAUSED · 比赛已冻结", 18f, _theme.textMuted, _bodyFont);
            var subRt = subtitle.rectTransform;
            subRt.anchoredPosition = new Vector2(0f, 132f);

            // 按钮：继续游戏
            var resumeBtn = CreateStyledButton("ResumeBtn", parent, "继续游戏", _theme.teamAColor, Color.white, new Vector2(360f, 58f), _theme, _bodyFont, 18f, 5f);
            var resumeRt = resumeBtn.GetComponent<RectTransform>();
            resumeRt.anchoredPosition = new Vector2(0f, 45f);
            resumeBtn.onClick.AddListener(Resume);

            // 按钮：设置
            var settingsBtn = CreateStyledButton("SettingsBtn", parent, "设置", _theme.panelGlassColor, Color.white, new Vector2(360f, 56f), _theme, _bodyFont, 18f, 5f);
            var setRt = settingsBtn.GetComponent<RectTransform>();
            setRt.anchoredPosition = new Vector2(0f, -30f);
            settingsBtn.onClick.AddListener(OpenSettings);

            // 按钮：重新开始
            var restartBtn = CreateStyledButton("RestartBtn", parent, "重新开始比赛", _theme.panelGlassColor, _theme.dangerRed, new Vector2(360f, 54f), _theme, _bodyFont, 18f, 5f);
            var restartRt = restartBtn.GetComponent<RectTransform>();
            restartRt.anchoredPosition = new Vector2(0f, -105f);
            restartBtn.onClick.AddListener(RestartMatch);

            // 提示文案
            var hint = CreateTextMesh("Hint", parent, "按 [ESC] 键或点击按钮继续", 15f, _theme.textMuted, _bodyFont);
            var hintRt = hint.rectTransform;
            hintRt.anchoredPosition = new Vector2(0f, -190f);
        }

        private void BuildSettingsViewContent(Transform parent)
        {
            // 标题
            var title = CreateTextMesh("Title", parent, "系统设置", 34f, _theme.textPrimary, _displayFont);
            var titleRt = title.rectTransform;
            titleRt.anchoredPosition = new Vector2(0f, 268f);

            // 1. 主音量
            var volLabel = CreateTextMesh("VolLabel", parent, "主音量", 18f, _theme.textPrimary, _bodyFont, TextAlignmentOptions.Left);
            var volLabelRt = volLabel.rectTransform;
            volLabelRt.anchoredPosition = new Vector2(-155f, 215f);
            volLabelRt.sizeDelta = new Vector2(150f, 30f);

            _volumeSlider = CreateStyledSlider("VolumeSlider", parent, 0f, 1f, GameSettingsService.MasterVolume, _theme, out _volumeValueText);
            var volSliderRt = _volumeSlider.transform.parent.GetComponent<RectTransform>();
            volSliderRt.anchoredPosition = new Vector2(0f, 183f);
            _volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
            UpdateVolumeLabel(GameSettingsService.MasterVolume);

            // 2. 屏幕震动无障碍
            var shakeLabel = CreateTextMesh("ShakeLabel", parent, "屏幕震动 (无障碍减弱)", 18f, _theme.textPrimary, _bodyFont, TextAlignmentOptions.Left);
            var shakeLabelRt = shakeLabel.rectTransform;
            shakeLabelRt.anchoredPosition = new Vector2(-80f, 140f);
            shakeLabelRt.sizeDelta = new Vector2(300f, 30f);

            _shakeSlider = CreateStyledSlider("ShakeSlider", parent, 0f, 1f, GameSettingsService.ShakeScale, _theme, out _shakeValueText);
            var shakeSliderRt = _shakeSlider.transform.parent.GetComponent<RectTransform>();
            shakeSliderRt.anchoredPosition = new Vector2(0f, 108f);
            _shakeSlider.onValueChanged.AddListener(OnShakeSliderChanged);
            UpdateShakeLabel(GameSettingsService.ShakeScale);

            // 3. 鼠标视角灵敏度
            var mouseLabel = CreateTextMesh("MouseSensLabel", parent, "鼠标视角灵敏度", 18f, _theme.textPrimary, _bodyFont, TextAlignmentOptions.Left);
            var mouseLabelRt = mouseLabel.rectTransform;
            mouseLabelRt.anchoredPosition = new Vector2(-125f, 65f);
            mouseLabelRt.sizeDelta = new Vector2(230f, 30f);

            _mouseSensSlider = CreateStyledSlider(
                "MouseSensSlider", parent,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.MouseLookSensitivity, _theme, out _mouseSensValueText);
            var mouseSliderRt = _mouseSensSlider.transform.parent.GetComponent<RectTransform>();
            mouseSliderRt.anchoredPosition = new Vector2(0f, 33f);
            _mouseSensSlider.onValueChanged.AddListener(OnMouseSensChanged);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);

            // 4. 触控视角灵敏度
            var touchLabel = CreateTextMesh("TouchSensLabel", parent, "触控视角灵敏度", 18f, _theme.textPrimary, _bodyFont, TextAlignmentOptions.Left);
            var touchLabelRt = touchLabel.rectTransform;
            touchLabelRt.anchoredPosition = new Vector2(-125f, -10f);
            touchLabelRt.sizeDelta = new Vector2(230f, 30f);

            _touchSensSlider = CreateStyledSlider(
                "TouchSensSlider", parent,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.TouchLookSensitivity, _theme, out _touchSensValueText);
            var touchSliderRt = _touchSensSlider.transform.parent.GetComponent<RectTransform>();
            touchSliderRt.anchoredPosition = new Vector2(0f, -42f);
            _touchSensSlider.onValueChanged.AddListener(OnTouchSensChanged);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);

            // 5. 视角反转 Y
            _invertYButton = CreateStyledButton(
                "InvertYBtn", parent, "视角反转 Y：关",
                _theme.panelGlassColor, Color.white, new Vector2(360f, 42f),
                _theme, _bodyFont, 15f, 4f);
            var invertRt = _invertYButton.GetComponent<RectTransform>();
            invertRt.anchoredPosition = new Vector2(0f, -105f);
            _invertYText = _invertYButton.GetComponentInChildren<TextMeshProUGUI>();
            _invertYButton.onClick.AddListener(OnInvertYToggled);
            UpdateInvertYLabel();

            // 6. 帧率上限
            var fpsLabel = CreateTextMesh("FpsLabel", parent, "帧率上限", 18f, _theme.textPrimary, _bodyFont, TextAlignmentOptions.Left);
            var fpsLabelRt = fpsLabel.rectTransform;
            fpsLabelRt.anchoredPosition = new Vector2(-155f, -160f);
            fpsLabelRt.sizeDelta = new Vector2(150f, 30f);

            BuildFrameRateButtons(parent);

            // 底部控制按钮
            var resetBtn = CreateStyledButton("ResetBtn", parent, "恢复默认", _theme.panelGlassColor, _theme.textMuted, new Vector2(160f, 44f), _theme, _bodyFont, 14f, 3f);
            var resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchoredPosition = new Vector2(-115f, -285f);
            resetBtn.onClick.AddListener(OnResetDefaultsClicked);

            var backBtn = CreateStyledButton("BackBtn", parent, "返回", _theme.readyGold, _theme.outlineColor, new Vector2(160f, 44f), _theme, _bodyFont, 14f, 3f);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchoredPosition = new Vector2(115f, -285f);
            backBtn.onClick.AddListener(CloseSettings);
        }

        private void BuildFrameRateButtons(Transform parent)
        {
            var rowGo = new GameObject("FpsButtonsRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchoredPosition = new Vector2(0f, -200f);
            rowRt.sizeDelta = new Vector2(460f, 40f);

            int[] rates = GameSettingsMath.AvailableFrameRates;
            _fpsButtons = new Button[rates.Length];
            _fpsShapes = new UIInkShapeGraphic[rates.Length];
            _fpsTexts = new TextMeshProUGUI[rates.Length];

            float btnWidth = 106f;
            float spacing = 8f;
            float startX = -(btnWidth * 2f + spacing * 1.5f) + btnWidth * 0.5f;

            for (int i = 0; i < rates.Length; i++)
            {
                int fps = rates[i];
                string label = GameSettingsMath.GetFrameRateLabel(fps);

                var btn = CreateStyledButton($"FpsBtn_{fps}", rowGo.transform, label, _theme.panelGlassColor, Color.white, new Vector2(btnWidth, 38f), _theme, _bodyFont, 12f, 3f);
                var btnRt = btn.GetComponent<RectTransform>();
                btnRt.anchoredPosition = new Vector2(startX + i * (btnWidth + spacing), 0f);

                _fpsButtons[i] = btn;
                _fpsShapes[i] = btn.GetComponent<UIInkShapeGraphic>();
                _fpsTexts[i] = btn.GetComponentInChildren<TextMeshProUGUI>();

                int capturedFps = fps;
                btn.onClick.AddListener(() =>
                {
                    GameSettingsService.TargetFrameRate = capturedFps;
                    UpdateFrameRateButtons();
                });
            }

            UpdateFrameRateButtons();
        }

        private void UpdateFrameRateButtons()
        {
            if (_fpsButtons == null || _fpsShapes == null)
                return;

            int currentFps = GameSettingsService.TargetFrameRate;
            int[] rates = GameSettingsMath.AvailableFrameRates;

            for (int i = 0; i < rates.Length; i++)
            {
                bool isSelected = (rates[i] == currentFps);
                if (_fpsShapes[i] != null)
                {
                    _fpsShapes[i].color = isSelected ? _theme.readyGold : _theme.panelGlassColor;
                }
                if (_fpsTexts[i] != null)
                {
                    _fpsTexts[i].color = isSelected ? _theme.outlineColor : _theme.textPrimary;
                }
            }
        }

        private void OnVolumeSliderChanged(float val)
        {
            GameSettingsService.MasterVolume = val;
            UpdateVolumeLabel(val);
        }

        private void UpdateVolumeLabel(float val)
        {
            if (_volumeValueText != null)
            {
                _volumeValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            }
        }

        private void OnShakeSliderChanged(float val)
        {
            GameSettingsService.ShakeScale = val;
            UpdateShakeLabel(val);
        }

        private void UpdateShakeLabel(float val)
        {
            if (_shakeValueText != null)
            {
                if (val <= 0.001f)
                    _shakeValueText.text = "0% (关闭)";
                else
                    _shakeValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            }
        }

        private void OnMouseSensChanged(float val)
        {
            GameSettingsService.MouseLookSensitivity = val;
            UpdateLookSensitivityLabel(_mouseSensValueText, val);
        }

        private void OnTouchSensChanged(float val)
        {
            GameSettingsService.TouchLookSensitivity = val;
            UpdateLookSensitivityLabel(_touchSensValueText, val);
        }

        private static void UpdateLookSensitivityLabel(TextMeshProUGUI label, float val)
        {
            if (label != null)
                label.text = $"{Mathf.RoundToInt(val * 100f)}%";
        }

        private void OnInvertYToggled()
        {
            GameSettingsService.InvertLookY = !GameSettingsService.InvertLookY;
            UpdateInvertYLabel();
        }

        private void UpdateInvertYLabel()
        {
            if (_invertYText != null)
                _invertYText.text = GameSettingsService.InvertLookY ? "视角反转 Y：开" : "视角反转 Y：关";
        }

        private void OnResetDefaultsClicked()
        {
            GameSettingsService.ResetToDefaults();
            if (_volumeSlider != null) _volumeSlider.value = GameSettingsService.MasterVolume;
            if (_shakeSlider != null) _shakeSlider.value = GameSettingsService.ShakeScale;
            if (_mouseSensSlider != null) _mouseSensSlider.value = GameSettingsService.MouseLookSensitivity;
            if (_touchSensSlider != null) _touchSensSlider.value = GameSettingsService.TouchLookSensitivity;
            UpdateVolumeLabel(GameSettingsService.MasterVolume);
            UpdateShakeLabel(GameSettingsService.ShakeScale);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);
            UpdateInvertYLabel();
            UpdateFrameRateButtons();
        }

        private void HandleInput()
        {
            bool toggleRequested = false;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                toggleRequested = true;
            }

            var gp = Gamepad.current;
            if (gp != null && gp.startButton.wasPressedThisFrame)
            {
                toggleRequested = true;
            }

            if (toggleRequested)
            {
                TogglePause();
            }
        }

        /// <summary>
        /// 切换暂停状态。若处于设置页，优先返回主暂停菜单；否则在暂停与游戏中切换。
        /// </summary>
        public void TogglePause()
        {
            if (IsPaused)
            {
                if (_isSettingsOpen)
                {
                    CloseSettings();
                }
                else
                {
                    Resume();
                }
            }
            else
            {
                Pause();
            }
        }

        /// <summary>
        /// 冻结游戏并展开暂停面板。
        /// 仅在 Playing 阶段且未暂停时生效。
        /// </summary>
        public void Pause()
        {
            if (IsPaused)
                return;

            IsPaused = true;

            // 1. 取消正在执行的 Hitstop，防止恢复时时间尺度错乱
            HitstopService.Cancel();
            HitstopService.IsPaused = true;

            // 2. 冻结游戏时间与声音
            Time.timeScale = 0f;
            AudioListener.pause = true;

            // 3. 禁用本地玩家的操作意图与移动开火
            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = false;
            }

            // 4. 显示弹窗并播放弹簧展开动效
            _modalRoot.SetActive(true);
            ShowMainView();
            if (_panelMotion != null)
            {
                _panelMotion.PlayIn();
            }

            UpdateMobileButtonVisibility();
            PauseStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 恢复游戏运行并收起暂停面板。
        /// </summary>
        public void Resume()
        {
            if (!IsPaused)
                return;

            IsPaused = false;

            // 1. 恢复声音与 Hitstop 守卫
            AudioListener.pause = false;
            HitstopService.IsPaused = false;
            HitstopService.Cancel();

            // 2. 强制恢复标准时间尺度
            Time.timeScale = 1f;

            // 3. 重新启用本地玩家输入
            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = true;
            }

            // 4. 隐藏模态层
            _modalRoot.SetActive(false);
            _isSettingsOpen = false;

            UpdateMobileButtonVisibility();
            PauseStateChanged?.Invoke(false);
        }

        public void RestartMatch()
        {
            Resume();
            MatchManager match = _match != null ? _match : MatchManager.Instance;
            if (match != null)
            {
                match.RestartMatch();
            }
        }

        public void OpenSettings()
        {
            _isSettingsOpen = true;
            _mainViewContainer.SetActive(false);
            _settingsViewContainer.SetActive(true);

            // 同步最新设置值
            if (_volumeSlider != null) _volumeSlider.value = GameSettingsService.MasterVolume;
            if (_shakeSlider != null) _shakeSlider.value = GameSettingsService.ShakeScale;
            UpdateVolumeLabel(GameSettingsService.MasterVolume);
            UpdateShakeLabel(GameSettingsService.ShakeScale);
            UpdateFrameRateButtons();

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        public void CloseSettings()
        {
            _isSettingsOpen = false;
            _settingsViewContainer.SetActive(false);
            _mainViewContainer.SetActive(true);

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        private void ShowMainView()
        {
            _isSettingsOpen = false;
            _settingsViewContainer.SetActive(false);
            _mainViewContainer.SetActive(true);
        }

        private void OnMatchPhaseChanged(MatchManager.Phase phase)
        {
            if (phase != MatchManager.Phase.Playing)
            {
                if (IsPaused)
                {
                    Resume();
                }
            }

            UpdateMobileButtonVisibility();
        }

        private void UpdateMobileButtonVisibility()
        {
            if (_mobilePauseButton == null)
                return;

            bool isPlaying = _match != null && _match.CurrentPhase == MatchManager.Phase.Playing;
            bool isMobile = Application.isMobilePlatform || forceShowMobileButton;
            bool shouldShow = !IsPaused && isPlaying && isMobile;

            if (_mobilePauseButton.gameObject.activeSelf != shouldShow)
            {
                _mobilePauseButton.gameObject.SetActive(shouldShow);
            }
        }

        private void CheckScreenResolutionChange()
        {
            if (Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y || Screen.safeArea != _lastSafeArea)
            {
                UpdateSafeArea();
            }
        }

        private void UpdateSafeArea()
        {
            if (_safeAreaRect == null)
                return;

            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _safeAreaRect.anchorMin = min;
            _safeAreaRect.anchorMax = max;
            _safeAreaRect.offsetMin = Vector2.zero;
            _safeAreaRect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateTextMesh(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            TMP_FontAsset font,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            if (font != null) tmp.font = font;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateStyledButton(
            string name,
            Transform parent,
            string label,
            Color fillColor,
            Color textColor,
            Vector2 size,
            UIThemeSO theme,
            TMP_FontAsset font,
            float cornerRadius = 16f,
            float borderWidth = 4f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = cornerRadius;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = theme.outlineColor;
            shape.color = fillColor;

            var btn = go.GetComponent<Button>();
            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(theme);
            var jelly = go.GetComponent<UIJellyButton>();
            jelly.Configure(theme, btn, shape);

            var text = CreateTextMesh("Label", go.transform, label, 20f, textColor, font);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            return btn;
        }

        private static Slider CreateStyledSlider(
            string name,
            Transform parent,
            float min,
            float max,
            float current,
            UIThemeSO theme,
            out TextMeshProUGUI valueLabel)
        {
            var container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var containerRt = container.GetComponent<RectTransform>();
            containerRt.sizeDelta = new Vector2(460f, 32f);

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(container.transform, false);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(0.78f, 0.5f);
            sliderRt.pivot = new Vector2(0f, 0.5f);
            sliderRt.sizeDelta = new Vector2(0f, 24f);

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            // Background
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.25f);
            bgRt.anchorMax = new Vector2(1f, 0.75f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.RoundedRect;
            bgShape.CornerRadius = 6f;
            bgShape.BorderWidth = 2f;
            bgShape.BorderColor = theme.outlineColor;
            bgShape.color = new Color(0.18f, 0.16f, 0.26f, 1f);

            // Fill Area
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRt.offsetMin = new Vector2(5f, 0f);
            fillAreaRt.offsetMax = new Vector2(-5f, 0f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(UIInkShapeGraphic));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            var fillShape = fillGo.GetComponent<UIInkShapeGraphic>();
            fillShape.Shape = UIInkShape.RoundedRect;
            fillShape.CornerRadius = 6f;
            fillShape.BorderWidth = 1f;
            fillShape.BorderColor = theme.outlineColor;
            fillShape.color = theme.teamAColor;
            slider.fillRect = fillRt;

            // Handle Slide Area
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(12f, 0f);
            handleAreaRt.offsetMax = new Vector2(-12f, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(UIInkShapeGraphic));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24f, 24f);
            var handleShape = handleGo.GetComponent<UIInkShapeGraphic>();
            handleShape.Shape = UIInkShape.RoundedRect;
            handleShape.CornerRadius = 12f;
            handleShape.BorderWidth = 3f;
            handleShape.BorderColor = theme.outlineColor;
            handleShape.color = Color.white;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleShape;

            // Value Text
            valueLabel = CreateTextMesh("ValueText", container.transform, "", 18f, theme.textPrimary, theme.GetFont(false), TextAlignmentOptions.Right);
            var valRt = valueLabel.rectTransform;
            valRt.anchorMin = new Vector2(0.80f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;

            return slider;
        }
    }
}
