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
    /// 2. 桌面端 Esc / 手柄 Start 键触发，移动端在 Playing 阶段显示 88x88 安全区暂停按钮（glyph_gear 图标或 ≡ 兜底）；
    /// 3. 漫画风视觉重构：墨色全屏蒙层 + 半调网点、900x820 纸面板 + 6px 墨线粗边框 + 硬阴影；
    /// 4. 彻底解决中文字体方块字：所有文本统一经 UIThemeSO.ResolveFont 获取 CJK 字体资产；
    /// 5. 暂停时：HitstopService.Cancel() → Time.timeScale = 0 → AudioListener.pause = true；
    /// 6. 恢复时反向执行，确保 Time.timeScale 归位为 1.0；离开 Playing 阶段或销毁时强制关闭；
    /// 7. 整合主音量、屏幕震动无障碍强度、灵敏度、Y 轴反转与帧率上限实时设置。
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
        private float _lastMatchSearchTime = -1f;

        // 移动端常驻暂停小按钮 (88x88)
        private Button _mobilePauseButton;
        private RectTransform _mobilePauseButtonRt;
        private Image _mobileGearIcon;
        private TextMeshProUGUI _mobileFallbackText;

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
        private UIInkShapeGraphic _invertYShape;
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
            // 通常 Start 已经订阅；若菜单早于 MatchManager 创建，再做一次惰性挂接（节流至少 0.5s 一次）。
            if (_matches.Count == 0 && Time.unscaledTime - _lastMatchSearchTime >= 0.5f)
            {
                _lastMatchSearchTime = Time.unscaledTime;
                SubscribeAllMatches();
            }

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
            // 独立包入口：从 Resources/P1RuntimeAssets 取 UI 主题与字体
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null && library.uiTheme != null)
            {
                _theme = library.uiTheme;
            }

#if UNITY_EDITOR
            if (_theme == null)
            {
                _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>("Assets/_Project/ScriptableObjects/UITheme.asset");
            }
#endif
            _theme = UIThemeSO.Resolve(_theme);

            // 严禁在此处通过 AssetDatabase 加载 TitanOne / Rubik，真机无法读取会导致所有中文回退为空变为方块字。
            // 统一通过 UIThemeSO.ResolveFont 根据具体中英文字符串动态解析黄油体/快乐体/TitanOne。
            _displayFont = _theme.GetFont(true);
            _bodyFont = _theme.GetFont(false);
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

            // 3. 移动端左上角暂停小按钮 (规范放大到 88x88，加入 glyph_gear / ≡ 兜底)
            BuildMobilePauseButton(_safeAreaRect);

            // 4. 模态暂停与设置全屏蒙层
            BuildModalOverlay(_safeAreaRect);
        }

        private void BuildMobilePauseButton(Transform parent)
        {
            var btnGo = new GameObject("MobilePauseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            btnGo.transform.SetParent(parent, false);

            _mobilePauseButtonRt = btnGo.GetComponent<RectTransform>();
            _mobilePauseButtonRt.anchorMin = new Vector2(0f, 1f);
            _mobilePauseButtonRt.anchorMax = new Vector2(0f, 1f);
            _mobilePauseButtonRt.pivot = new Vector2(0f, 1f);
            _mobilePauseButtonRt.anchoredPosition = new Vector2(24f, -24f);
            _mobilePauseButtonRt.sizeDelta = new Vector2(88f, 88f);

            var shape = btnGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 14f;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(4f, -4f);
            shape.ShadowColor = ComicStyle.Ink;
            shape.Halftone = false;

            _mobilePauseButton = btnGo.GetComponent<Button>();
            _mobilePauseButton.onClick.AddListener(Pause);

            var motion = btnGo.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);

            var jelly = btnGo.GetComponent<UIJellyButton>();
            jelly.Configure(_theme, _mobilePauseButton, shape);

            // 图标：优先 glyph_gear，不存在时隐藏 Image 并使用 ≡ 文字兜底
            Sprite gearSprite = UIIconLibrary.Get("glyph_gear");

            var iconGo = new GameObject("GearIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(btnGo.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(16f, 16f);
            iconRt.offsetMax = new Vector2(-16f, -16f);
            _mobileGearIcon = iconGo.GetComponent<Image>();
            _mobileGearIcon.preserveAspect = true;
            _mobileGearIcon.raycastTarget = false;
            _mobileGearIcon.color = ComicStyle.Ink;

            _mobileFallbackText = CreateTextMesh("FallbackText", btnGo.transform, "\u2261", 42f, ComicStyle.Ink, isDisplay: true);
            var textRt = _mobileFallbackText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            if (gearSprite != null)
            {
                _mobileGearIcon.sprite = gearSprite;
                _mobileGearIcon.gameObject.SetActive(true);
                _mobileFallbackText.gameObject.SetActive(false);
            }
            else
            {
                _mobileGearIcon.gameObject.SetActive(false);
                _mobileFallbackText.gameObject.SetActive(true);
            }

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

            // 1. 全屏背板 = 墨色 65%
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backdropGo.transform.SetParent(_modalRoot.transform, false);
            var backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = backdropGo.GetComponent<Image>();
            backdropImg.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            backdropImg.raycastTarget = true;

            // 2. 全屏网点底 ComicHalftoneGraphic (墨色，alpha 0.08)
            var halftoneGo = new GameObject("HalftoneBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicHalftoneGraphic));
            halftoneGo.transform.SetParent(_modalRoot.transform, false);
            var halfRt = halftoneGo.GetComponent<RectTransform>();
            halfRt.anchorMin = Vector2.zero;
            halfRt.anchorMax = Vector2.one;
            halfRt.offsetMin = Vector2.zero;
            halfRt.offsetMax = Vector2.zero;
            var halfGraphic = halftoneGo.GetComponent<ComicHalftoneGraphic>();
            halfGraphic.color = ComicStyle.Ink;
            halfGraphic.Alpha = 0.08f;
            halfGraphic.Spacing = 14f;
            halfGraphic.raycastTarget = false;

            // 3. 居中弹窗主卡片 (900x820，纸面 + 6px 墨边 + 硬阴影 + 网点)
            var panelGo = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_modalRoot.transform, false);
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(900f, 820f);

            _panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            _panelShape.Shape = UIInkShape.RoundedRect;
            _panelShape.CornerRadius = ComicStyle.CornerSharp;
            _panelShape.BorderWidth = ComicStyle.Border;
            _panelShape.BorderColor = ComicStyle.Ink;
            _panelShape.color = ComicStyle.Paper;
            _panelShape.HardShadow = true;
            _panelShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            _panelShape.ShadowColor = ComicStyle.Ink;
            _panelShape.Halftone = true;
            _panelShape.HalftoneAlpha = ComicStyle.HalftoneAlpha;
            _panelShape.GlossAlpha = 0f;
            _panelShape.DrawGloss = false;
            _panelShape.InnerRimAlpha = 0f;

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

            ApplyResponsivePanelScale();
        }

        private void BuildMainViewContent(Transform parent)
        {
            // 标题气泡（下指尾 + 庆科黄油体 46px）
            var bubbleGo = new GameObject("TitleBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bubbleGo.transform.SetParent(parent, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchoredPosition = new Vector2(0f, 250f);
            bubbleRt.sizeDelta = new Vector2(440f, 80f);

            var bubbleShape = bubbleGo.GetComponent<UIInkShapeGraphic>();
            bubbleShape.Shape = UIInkShape.SpeechBubble;
            bubbleShape.CornerRadius = ComicStyle.CornerBubble;
            bubbleShape.BorderWidth = ComicStyle.Border;
            bubbleShape.BorderColor = ComicStyle.Ink;
            bubbleShape.color = ComicStyle.Paper;
            bubbleShape.HardShadow = true;
            bubbleShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            bubbleShape.ShadowColor = ComicStyle.Ink;
            bubbleShape.SpeechTail = true;
            bubbleShape.TailSize = new Vector2(28f, 20f);
            bubbleShape.TailAngle = 270f;
            bubbleShape.GlossAlpha = 0f;
            bubbleShape.DrawGloss = false;

            var title = CreateTextMesh("TitleText", bubbleGo.transform, "游戏暂停", 46f, ComicStyle.Ink, isDisplay: true);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            var subtitle = CreateTextMesh("Subtitle", parent, "PAUSED · 比赛已冻结", 20f, ComicStyle.WithAlpha(ComicStyle.Ink, 0.8f), isDisplay: false);
            var subRt = subtitle.rectTransform;
            subRt.anchoredPosition = new Vector2(0f, 142f);
            subRt.sizeDelta = new Vector2(400f, 32f);

            // 主菜单按钮：高 88、宽 420，UIJellyButton 漫画墨印反馈
            // 1. 继续游戏（黄底）
            var resumeBtn = CreateStyledButton("ResumeBtn", parent, "继续游戏", ComicStyle.Yellow, ComicStyle.Ink, new Vector2(420f, 88f), _theme, 28f, ComicStyle.CornerSharp, ComicStyle.Border);
            var resumeRt = resumeBtn.GetComponent<RectTransform>();
            resumeRt.anchoredPosition = new Vector2(0f, 65f);
            resumeBtn.onClick.AddListener(Resume);

            // 2. 设置（纸白）
            var settingsBtn = CreateStyledButton("SettingsBtn", parent, "系统设置", ComicStyle.Paper, ComicStyle.Ink, new Vector2(420f, 88f), _theme, 28f, ComicStyle.CornerSharp, ComicStyle.Border);
            var setRt = settingsBtn.GetComponent<RectTransform>();
            setRt.anchoredPosition = new Vector2(0f, -40f);
            settingsBtn.onClick.AddListener(OpenSettings);

            // 3. 重新开始比赛（红底白字）
            var restartBtn = CreateStyledButton("RestartBtn", parent, "重新开始比赛", ComicStyle.Red, Color.white, new Vector2(420f, 88f), _theme, 28f, ComicStyle.CornerSharp, ComicStyle.Border);
            var restartRt = restartBtn.GetComponent<RectTransform>();
            restartRt.anchoredPosition = new Vector2(0f, -145f);
            restartBtn.onClick.AddListener(RestartMatch);

            // 提示文案（触屏设备没有 ESC 键，换成点击说明）
            string hintLabel = Application.isMobilePlatform ? "点击上方按钮继续" : "按 [ESC] 键或点击按钮继续";
            var hint = CreateTextMesh("Hint", parent, hintLabel, 18f, ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f), isDisplay: false);
            var hintRt = hint.rectTransform;
            hintRt.anchoredPosition = new Vector2(0f, -240f);
            hintRt.sizeDelta = new Vector2(500f, 30f);
        }

        private void BuildSettingsViewContent(Transform parent)
        {
            // 标题气泡（系统设置）
            var bubbleGo = new GameObject("SettingsTitleBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bubbleGo.transform.SetParent(parent, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchoredPosition = new Vector2(0f, 335f);
            bubbleRt.sizeDelta = new Vector2(400f, 68f);

            var bubbleShape = bubbleGo.GetComponent<UIInkShapeGraphic>();
            bubbleShape.Shape = UIInkShape.SpeechBubble;
            bubbleShape.CornerRadius = ComicStyle.CornerBubble;
            bubbleShape.BorderWidth = ComicStyle.Border;
            bubbleShape.BorderColor = ComicStyle.Ink;
            bubbleShape.color = ComicStyle.Paper;
            bubbleShape.HardShadow = true;
            bubbleShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            bubbleShape.ShadowColor = ComicStyle.Ink;
            bubbleShape.SpeechTail = true;
            bubbleShape.TailSize = new Vector2(24f, 16f);
            bubbleShape.TailAngle = 270f;

            var title = CreateTextMesh("Title", bubbleGo.transform, "系统设置", 38f, ComicStyle.Ink, isDisplay: true);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            // 设置列表容器（每行高 72，纸白/纸黄交替底色 + 4px 墨线分隔）。
            // 移动端与编辑器多一行「触控操作与按键」入口；正式桌面包保持 7 行，避免出现点不动的死按钮。
            bool showControlRow = Application.isMobilePlatform || Application.isEditor;
            int totalRows = showControlRow ? 8 : 7;

            var tableGo = new GameObject("SettingsTable", typeof(RectTransform));
            tableGo.transform.SetParent(parent, false);
            var tableRt = tableGo.GetComponent<RectTransform>();
            tableRt.anchoredPosition = new Vector2(0f, -25f);
            tableRt.sizeDelta = new Vector2(820f, 72f * totalRows);

            // Row 0: 主音量
            var row0 = CreateSettingRow(tableGo.transform, 0, totalRows);
            var volLabel = CreateTextMesh("VolLabel", row0.transform, "主音量", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var volLabelRt = volLabel.rectTransform;
            volLabelRt.anchorMin = new Vector2(0f, 0.5f);
            volLabelRt.anchorMax = new Vector2(0f, 0.5f);
            volLabelRt.pivot = new Vector2(0f, 0.5f);
            volLabelRt.anchoredPosition = new Vector2(28f, 0f);
            volLabelRt.sizeDelta = new Vector2(300f, 36f);

            _volumeSlider = CreateStyledSlider("VolumeSlider", row0.transform, 0f, 1f, GameSettingsService.MasterVolume, _theme, out _volumeValueText);
            var volSliderRt = _volumeSlider.transform.parent.GetComponent<RectTransform>();
            volSliderRt.anchorMin = new Vector2(1f, 0.5f);
            volSliderRt.anchorMax = new Vector2(1f, 0.5f);
            volSliderRt.pivot = new Vector2(1f, 0.5f);
            volSliderRt.anchoredPosition = new Vector2(-28f, 0f);
            _volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
            UpdateVolumeLabel(GameSettingsService.MasterVolume);

            // Row 1: 屏幕震动
            var row1 = CreateSettingRow(tableGo.transform, 1, totalRows);
            var shakeLabel = CreateTextMesh("ShakeLabel", row1.transform, "屏幕震动 (无障碍减弱)", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var shakeLabelRt = shakeLabel.rectTransform;
            shakeLabelRt.anchorMin = new Vector2(0f, 0.5f);
            shakeLabelRt.anchorMax = new Vector2(0f, 0.5f);
            shakeLabelRt.pivot = new Vector2(0f, 0.5f);
            shakeLabelRt.anchoredPosition = new Vector2(28f, 0f);
            shakeLabelRt.sizeDelta = new Vector2(320f, 36f);

            _shakeSlider = CreateStyledSlider("ShakeSlider", row1.transform, 0f, 1f, GameSettingsService.ShakeScale, _theme, out _shakeValueText);
            var shakeSliderRt = _shakeSlider.transform.parent.GetComponent<RectTransform>();
            shakeSliderRt.anchorMin = new Vector2(1f, 0.5f);
            shakeSliderRt.anchorMax = new Vector2(1f, 0.5f);
            shakeSliderRt.pivot = new Vector2(1f, 0.5f);
            shakeSliderRt.anchoredPosition = new Vector2(-28f, 0f);
            _shakeSlider.onValueChanged.AddListener(OnShakeSliderChanged);
            UpdateShakeLabel(GameSettingsService.ShakeScale);

            // Row 2: 鼠标视角灵敏度
            var row2 = CreateSettingRow(tableGo.transform, 2, totalRows);
            var mouseLabel = CreateTextMesh("MouseSensLabel", row2.transform, "鼠标视角灵敏度", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var mouseLabelRt = mouseLabel.rectTransform;
            mouseLabelRt.anchorMin = new Vector2(0f, 0.5f);
            mouseLabelRt.anchorMax = new Vector2(0f, 0.5f);
            mouseLabelRt.pivot = new Vector2(0f, 0.5f);
            mouseLabelRt.anchoredPosition = new Vector2(28f, 0f);
            mouseLabelRt.sizeDelta = new Vector2(300f, 36f);

            _mouseSensSlider = CreateStyledSlider(
                "MouseSensSlider", row2.transform,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.MouseLookSensitivity, _theme, out _mouseSensValueText);
            var mouseSliderRt = _mouseSensSlider.transform.parent.GetComponent<RectTransform>();
            mouseSliderRt.anchorMin = new Vector2(1f, 0.5f);
            mouseSliderRt.anchorMax = new Vector2(1f, 0.5f);
            mouseSliderRt.pivot = new Vector2(1f, 0.5f);
            mouseSliderRt.anchoredPosition = new Vector2(-28f, 0f);
            _mouseSensSlider.onValueChanged.AddListener(OnMouseSensChanged);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);

            // Row 3: 触控视角灵敏度
            var row3 = CreateSettingRow(tableGo.transform, 3, totalRows);
            var touchLabel = CreateTextMesh("TouchSensLabel", row3.transform, "触控视角灵敏度", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var touchLabelRt = touchLabel.rectTransform;
            touchLabelRt.anchorMin = new Vector2(0f, 0.5f);
            touchLabelRt.anchorMax = new Vector2(0f, 0.5f);
            touchLabelRt.pivot = new Vector2(0f, 0.5f);
            touchLabelRt.anchoredPosition = new Vector2(28f, 0f);
            touchLabelRt.sizeDelta = new Vector2(300f, 36f);

            _touchSensSlider = CreateStyledSlider(
                "TouchSensSlider", row3.transform,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.TouchLookSensitivity, _theme, out _touchSensValueText);
            var touchSliderRt = _touchSensSlider.transform.parent.GetComponent<RectTransform>();
            touchSliderRt.anchorMin = new Vector2(1f, 0.5f);
            touchSliderRt.anchorMax = new Vector2(1f, 0.5f);
            touchSliderRt.pivot = new Vector2(1f, 0.5f);
            touchSliderRt.anchoredPosition = new Vector2(-28f, 0f);
            _touchSensSlider.onValueChanged.AddListener(OnTouchSensChanged);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);

            // Row 4: 视角反转 Y
            var row4 = CreateSettingRow(tableGo.transform, 4, totalRows);
            var invertLabel = CreateTextMesh("InvertYLabel", row4.transform, "视角垂直方向", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var invertLabelRt = invertLabel.rectTransform;
            invertLabelRt.anchorMin = new Vector2(0f, 0.5f);
            invertLabelRt.anchorMax = new Vector2(0f, 0.5f);
            invertLabelRt.pivot = new Vector2(0f, 0.5f);
            invertLabelRt.anchoredPosition = new Vector2(28f, 0f);
            invertLabelRt.sizeDelta = new Vector2(300f, 36f);

            _invertYButton = CreateStyledButton(
                "InvertYBtn", row4.transform, "视角反转 Y：关",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(260f, 52f),
                _theme, 20f, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var invertRt = _invertYButton.GetComponent<RectTransform>();
            invertRt.anchorMin = new Vector2(1f, 0.5f);
            invertRt.anchorMax = new Vector2(1f, 0.5f);
            invertRt.pivot = new Vector2(1f, 0.5f);
            invertRt.anchoredPosition = new Vector2(-28f, 0f);
            _invertYShape = _invertYButton.GetComponent<UIInkShapeGraphic>();
            _invertYText = _invertYButton.GetComponentInChildren<TextMeshProUGUI>();
            _invertYButton.onClick.AddListener(OnInvertYToggled);
            UpdateInvertYLabel();

            // Row 5: 帧率上限
            var row5 = CreateSettingRow(tableGo.transform, 5, totalRows);
            var fpsLabel = CreateTextMesh("FpsLabel", row5.transform, "帧率上限", 24f, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            var fpsLabelRt = fpsLabel.rectTransform;
            fpsLabelRt.anchorMin = new Vector2(0f, 0.5f);
            fpsLabelRt.anchorMax = new Vector2(0f, 0.5f);
            fpsLabelRt.pivot = new Vector2(0f, 0.5f);
            fpsLabelRt.anchoredPosition = new Vector2(28f, 0f);
            fpsLabelRt.sizeDelta = new Vector2(200f, 36f);

            BuildFrameRateButtons(row5.transform);

            // Row 6: 快捷操作（恢复默认 + 返回）
            var row6 = CreateSettingRow(tableGo.transform, 6, totalRows);
            var resetBtn = CreateStyledButton("ResetBtn", row6.transform, "恢复默认", ComicStyle.Paper, ComicStyle.Ink, new Vector2(200f, 52f), _theme, 20f, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0f, 0.5f);
            resetRt.anchorMax = new Vector2(0f, 0.5f);
            resetRt.pivot = new Vector2(0f, 0.5f);
            resetRt.anchoredPosition = new Vector2(28f, 0f);
            resetBtn.onClick.AddListener(OnResetDefaultsClicked);

            var backBtn = CreateStyledButton("BackBtn", row6.transform, "返回", ComicStyle.Yellow, ComicStyle.Ink, new Vector2(200f, 52f), _theme, 20f, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(1f, 0.5f);
            backRt.anchorMax = new Vector2(1f, 0.5f);
            backRt.pivot = new Vector2(1f, 0.5f);
            backRt.anchoredPosition = new Vector2(-28f, 0f);
            backBtn.onClick.AddListener(CloseSettings);

            // Row 7: 移动端操作设置入口（打开 2/3/4 指布局与辅助瞄准面板）
            if (showControlRow)
            {
                BuildControlSettingsRow(tableGo.transform, 7, totalRows);
            }
        }

        private void BuildControlSettingsRow(Transform parent, int index, int totalRows)
        {
            var row = CreateSettingRow(parent, index, totalRows);
            var label = CreateTextMesh("ControlLabel", row.transform, "触控操作与按键", 24f, ComicStyle.Ink, null,
                TextAlignmentOptions.Left, isDisplay: false);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0f, 0.5f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = new Vector2(28f, 0f);
            labelRt.sizeDelta = new Vector2(320f, 36f);

            var button = CreateStyledButton("ControlSettingsBtn", row.transform, "打开操作设置",
                ComicStyle.Yellow, ComicStyle.Ink, new Vector2(260f, 52f), _theme, 20f,
                ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-28f, 0f);
            button.onClick.AddListener(OpenControlSettings);
        }

        private void OpenControlSettings()
        {
            // 关闭暂停（恢复时间/输入），再进入触控布局编辑器；编辑器会自行暂停。
            Resume();
            var touchUI = FindAnyObjectByType<MobileTouchUI>();
            if (touchUI != null)
            {
                touchUI.OpenLayoutCustomizer();
            }
        }

        private GameObject CreateSettingRow(Transform parent, int index, int totalRows)
        {
            float rowHeight = 72f;
            float totalHeight = rowHeight * totalRows;
            float topY = totalHeight * 0.5f - rowHeight * 0.5f;
            float y = topY - index * rowHeight;

            var rowGo = new GameObject($"Row_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            rowGo.transform.SetParent(parent, false);
            var rt = rowGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(820f, rowHeight);
            rt.anchoredPosition = new Vector2(0f, y);

            var shape = rowGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 3f;
            shape.BorderWidth = 0f;
            // 纸白 / 纸黄交替行底
            shape.color = index % 2 == 0 ? ComicStyle.Paper : ComicStyle.PaperAlt;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            // 底部 4px 墨线分隔
            if (index < totalRows - 1)
            {
                var lineGo = new GameObject("Separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineGo.transform.SetParent(rowGo.transform, false);
                var lrt = lineGo.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(0f, ComicStyle.BorderThin);
                lrt.anchoredPosition = Vector2.zero;
                var img = lineGo.GetComponent<Image>();
                img.color = ComicStyle.Ink;
                img.raycastTarget = false;
            }

            return rowGo;
        }

        private void BuildFrameRateButtons(Transform parent)
        {
            var rowGo = new GameObject("FpsButtonsRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(1f, 0.5f);
            rowRt.anchorMax = new Vector2(1f, 0.5f);
            rowRt.pivot = new Vector2(1f, 0.5f);
            rowRt.anchoredPosition = new Vector2(-28f, 0f);
            rowRt.sizeDelta = new Vector2(460f, 50f);

            int[] rates = GameSettingsMath.AvailableFrameRates;
            _fpsButtons = new Button[rates.Length];
            _fpsShapes = new UIInkShapeGraphic[rates.Length];
            _fpsTexts = new TextMeshProUGUI[rates.Length];

            float btnWidth = 104f;
            float spacing = 8f;
            float startX = -(btnWidth * 2f + spacing * 1.5f) + btnWidth * 0.5f;

            for (int i = 0; i < rates.Length; i++)
            {
                int fps = rates[i];
                string label = GameSettingsMath.GetFrameRateLabel(fps);

                var btn = CreateStyledButton(
                    $"FpsBtn_{fps}", rowGo.transform, label,
                    ComicStyle.Paper, ComicStyle.Ink, new Vector2(btnWidth, 46f),
                    _theme, 16f, ComicStyle.CornerSharp, ComicStyle.BorderThin);
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
                    _fpsShapes[i].color = isSelected ? ComicStyle.Yellow : ComicStyle.Paper;
                }
                if (_fpsTexts[i] != null)
                {
                    _fpsTexts[i].color = ComicStyle.Ink;
                    _fpsTexts[i].fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
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
            {
                _invertYText.text = GameSettingsService.InvertLookY ? "视角反转 Y：开" : "视角反转 Y：关";
            }
            if (_invertYShape != null)
            {
                _invertYShape.color = GameSettingsService.InvertLookY ? ComicStyle.Yellow : ComicStyle.Paper;
            }
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
                // 编辑布局时先退出编辑器，绝不再叠一层暂停：暂停恢复会把 timeScale 改成 1，
                // 破坏编辑器“退出时还原进入前时间尺度”的约定。
                if (MobileLayoutCustomizer.IsEditingAny)
                {
                    MobileLayoutCustomizer.ActiveEditor?.Close(false);
                    return;
                }

                TogglePause();
            }
        }

        /// <summary>
        /// 切换暂停状态。若处于设置页，优先返回主暂停菜单；否则在暂停与游戏中切换。
        /// </summary>
        public void TogglePause()
        {
            if (!IsPaused && MobileLayoutCustomizer.IsEditingAny)
            {
                MobileLayoutCustomizer.ActiveEditor?.Close(false);
                return;
            }

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
            if (IsPaused || MobileLayoutCustomizer.IsEditingAny)
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
            ApplyResponsivePanelScale();
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
            // 编辑布局时暂停按钮必须让位：它 sortingOrder=100 压在编辑器之上，会挡住顶栏并误开暂停。
            bool shouldShow = !IsPaused && isPlaying && isMobile && !MobileLayoutCustomizer.IsEditingAny;

            if (_mobilePauseButton.gameObject.activeSelf != shouldShow)
            {
                _mobilePauseButton.gameObject.SetActive(shouldShow);
            }
        }

        /// <summary>供布局编辑器开关时联动刷新移动端暂停按钮显隐。</summary>
        public void RefreshMobileButtonVisibility()
        {
            UpdateMobileButtonVisibility();
        }

        private void CheckScreenResolutionChange()
        {
            if (Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y || Screen.safeArea != _lastSafeArea)
            {
                UpdateSafeArea();
                ApplyResponsivePanelScale();
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

        /// <summary>
        /// 屏幕高度不足时整体按可用高度等比缩放面板，保留安全区，绝不溢出屏幕边界。
        /// </summary>
        private void ApplyResponsivePanelScale()
        {
            if (_panelRect == null) return;

            float canvasHeight = 1080f;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 860f; // 820px 面板 + 留白

            if (availableHeight < targetHeight && availableHeight > 100f)
            {
                float s = Mathf.Clamp(availableHeight / targetHeight, 0.5f, 1f);
                _panelRect.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                _panelRect.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// 统一文本构造工厂：先通过 UIThemeSO.ResolveFont 解析对应字体，不设字体会导致方块字 □。
        /// </summary>
        private TextMeshProUGUI CreateTextMesh(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            TMP_FontAsset font = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            bool isDisplay = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;

            // 核心修复：优先从主题解析含中文字符匹配的 CJK 字体资产，彻底解决真机与设置页文字方块字问题
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(_theme);
            TMP_FontAsset resolvedFont = resolvedTheme.ResolveFont(isDisplay, text);
            if (resolvedFont == null && font != null)
                resolvedFont = font;
            if (resolvedFont == null)
                resolvedFont = TMP_Settings.defaultFontAsset;

            tmp.font = resolvedFont;
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
            float fontSize = 20f,
            float cornerRadius = ComicStyle.CornerSharp,
            float borderWidth = ComicStyle.Border)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = cornerRadius;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fillColor;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            shape.ShadowColor = ComicStyle.Ink;
            shape.Halftone = false;
            shape.GlossAlpha = 0f;
            shape.DrawGloss = false;

            var btn = go.GetComponent<Button>();
            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(theme);
            var jelly = go.GetComponent<UIJellyButton>();
            jelly.Configure(theme, btn, shape);

            // 构造文本（保证按钮中文同样走 CJK 字体）
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = textColor;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.font = resolvedTheme.ResolveFont(true, label);

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
            containerRt.sizeDelta = new Vector2(460f, 48f);

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(container.transform, false);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(0.76f, 0.5f);
            sliderRt.pivot = new Vector2(0f, 0.5f);
            sliderRt.sizeDelta = new Vector2(0f, 28f);

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            // 1. 滑条轨道 Background (28 高，纸灰/底色 + 4px 墨边)
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 28f);
            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.RoundedRect;
            bgShape.CornerRadius = 6f;
            bgShape.BorderWidth = ComicStyle.BorderThin;
            bgShape.BorderColor = ComicStyle.Ink;
            bgShape.color = ComicStyle.PaperAlt;
            bgShape.HardShadow = false;
            bgShape.Halftone = false;

            // 2. 填充区域 Fill
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(0f, 28f);
            fillAreaRt.offsetMin = new Vector2(4f, 0f);
            fillAreaRt.offsetMax = new Vector2(-4f, 0f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            var fillShape = fillGo.GetComponent<UIInkShapeGraphic>();
            fillShape.Shape = UIInkShape.RoundedRect;
            fillShape.CornerRadius = 5f;
            fillShape.BorderWidth = 2f;
            fillShape.BorderColor = ComicStyle.Ink;
            fillShape.color = ComicStyle.Yellow;
            fillShape.HardShadow = false;
            fillShape.Halftone = false;
            slider.fillRect = fillRt;

            // 3. 手柄区域 Handle (规范 48x48 圆块，墨边 + 硬阴影)
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(24f, 0f);
            handleAreaRt.offsetMax = new Vector2(-24f, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(48f, 48f);
            var handleShape = handleGo.GetComponent<UIInkShapeGraphic>();
            handleShape.Shape = UIInkShape.RoundedRect;
            handleShape.CornerRadius = 24f; // 48x48 圆块
            handleShape.BorderWidth = ComicStyle.BorderThin;
            handleShape.BorderColor = ComicStyle.Ink;
            handleShape.color = ComicStyle.Paper;
            handleShape.HardShadow = true;
            handleShape.ShadowOffset = new Vector2(3f, -3f);
            handleShape.ShadowColor = ComicStyle.Ink;
            handleShape.Halftone = false;

            slider.handleRect = handleRt;
            slider.targetGraphic = handleShape;

            // 4. 数值标签 (字号 22，走 CJK/Latin 字体解析)
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
            var valGo = new GameObject("ValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
            valGo.transform.SetParent(container.transform, false);
            var valRt = valGo.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0.78f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;

            valueLabel = valGo.GetComponent<TextMeshProUGUI>();
            valueLabel.fontSize = 22f;
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.color = ComicStyle.Ink;
            valueLabel.alignment = TextAlignmentOptions.Right;
            valueLabel.raycastTarget = false;
            valueLabel.font = resolvedTheme.ResolveFont(false, "100%");

            return slider;
        }
    }
}
