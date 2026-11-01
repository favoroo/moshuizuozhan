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
    /// 全局暂停菜单与系统设置覆盖层（P14 几何大胆风 Geometric Bold 重构）：
    /// 1. 独立 Overlay Canvas，sortingOrder = 100，高于 HUD 与触控层；
    /// 2. 桌面端 Esc / 手柄 Start 键触发，移动端在 Playing 阶段显示 88x88 安全区纯色几何方块暂停按钮；
    /// 3. 左对齐标题 + 1100x820 直角黑色大面板 + 纯色几何色块；
    ///    主视图左右双栏：左列操作按钮，右列「操作说明」卡（键帽 + 动作，接收原 HUD 底部键鼠提示的职责）；
    /// 4. 设置行规范：左侧粗体标签 ≥24（移动端 ≥30） + 右侧纯色方块滑条/开关；
    /// 5. 彻底去除网点、半调底、动作线、气泡尖角与硬阴影；
    /// 6. 暂停时：HitstopService.Cancel() → Time.timeScale = 0 → AudioListener.pause = true；
    /// 7. 恢复时反向执行，确保 Time.timeScale 归位为 1.0；离开 Playing 阶段或销毁时强制关闭；
    /// 8. 整合主音量、屏幕震动无障碍强度、灵敏度、Y 轴反转与帧率上限实时设置。
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuOverlay : MonoBehaviour
    {
        public static PauseMenuOverlay Instance { get; private set; }

        /// <summary>设置行高（t-1 提级：72→84；行内滑条/开关/下拉的可触高度 ≥64）。</summary>
        private const float PauseSettingRowHeight = 84f;

        /// <summary>设置行内控件宽度（按钮 200/260 → 280）。</summary>
        private const float PauseSettingControlWidth = 280f;

        /// <summary>设置行内控件高度（52 → 72，≥64 可触下限）。</summary>
        private const float PauseSettingControlHeight = 72f;

        /// <summary>设置行内控件字号（20 → 24）。</summary>
        private const float PauseSettingControlFont = 24f;

        /// <summary>暂停主面板宽度：左列 460 按钮 + 40 间距 + 右列 500 操作说明卡 + 左右各 50。</summary>
        private const float PausePanelWidth = 1100f;

        /// <summary>暂停主面板高度（保持不变，设置视图的 8 行表格依赖它）。</summary>
        private const float PausePanelHeight = 820f;

        // ── 操作说明卡几何 ──────────────────────────────────────────────
        private const float ControlsCardWidth = 500f;
        private const float ControlsCardRightX = 250f;
        private const float ControlsCardTopY = 200f;
        private const float ControlsKeyCapWidth = 150f;
        private const float ControlsRowHeight = 44f;
        private const float ControlsRowGap = 8f;
        private const float ControlsCardPadding = 20f;

        /// <summary>左列按钮中心 x（宽 460，左侧留 50 边距）。</summary>
        private const float PauseButtonColumnX = -270f;

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

        // 移动端常驻暂停小按钮 (88x88 直角几何方块)
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
        private Button _returnMenuButton;

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
            if (_matches.Count == 0 && Time.unscaledTime - _lastMatchSearchTime >= 0.5f)
            {
                _lastMatchSearchTime = Time.unscaledTime;
                SubscribeAllMatches();
            }

            CheckScreenResolutionChange();
            HandleInput();
            UpdateMobileButtonVisibility();
        }

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
            _canvas.sortingOrder = 100;

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

            // 3. 移动端左上角暂停小按钮 (88x88 直角纯色几何方块)
            BuildMobilePauseButton(_safeAreaRect);

            // 4. 模态暂停与设置全屏蒙层
            //    蒙层必须挂在 Canvas 根（物理全屏）而不是 SafeArea：
            //    SafeArea 在横屏刘海/挖孔设备上左（右）侧有内缩，蒙层跟着缩会在刘海区留下
            //    一条没被压暗的亮竖条。面板本身仍是屏幕居中的 920x820，另有安全区比例缩放。
            BuildModalOverlay(transform);
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
            shape.Shape = UIInkShape.SharpRect; // 直角方块
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.HardShadow = false;
            shape.Halftone = false;

            _mobilePauseButton = btnGo.GetComponent<Button>();
            _mobilePauseButton.onClick.AddListener(Pause);

            var motion = btnGo.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);

            var jelly = btnGo.GetComponent<UIJellyButton>();
            jelly.Configure(_theme, _mobilePauseButton, shape);
            jelly.EnableThemeHardShadow();

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

            _mobileFallbackText = CreateTextMesh("FallbackText", btnGo.transform, "\u2261", UITypography.FontSnapped(42f), ComicStyle.Ink, isDisplay: true);
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

            // 1. 全屏纯色深色遮罩 (墨色 70%，无网点)
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backdropGo.transform.SetParent(_modalRoot.transform, false);
            var backdropRt = backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = backdropGo.GetComponent<Image>();
            backdropImg.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.70f);
            backdropImg.raycastTarget = true;

            // 2. 居中直角主面板 (1100x820)：t-1 纸白底 + 3px 墨框 + 弹窗硬影 (8,-9)
            var panelGo = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_modalRoot.transform, false);
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(PausePanelWidth, PausePanelHeight);

            _panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            _panelShape.Shape = UIInkShape.SharpRect; // 直角面板
            _panelShape.CornerRadius = ComicStyle.CornerSharp;
            _panelShape.BorderWidth = ComicStyle.BorderThin; // 3px 墨框
            _panelShape.BorderColor = ComicStyle.Ink;
            _panelShape.color = ComicStyle.Paper; // 纸白底
            _panelShape.Halftone = false;
            _panelShape.GlossAlpha = 0f;
            _panelShape.DrawGloss = false;
            _panelShape.InnerRimAlpha = 0f;
            _theme.ApplyHardShadow(_panelShape, UIHardShadowLevel.Dialog);

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
            // 左对齐几何标题头
            var headerGo = new GameObject("HeaderBox", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0.5f, 0.5f);
            headerRt.anchorMax = new Vector2(0.5f, 0.5f);
            headerRt.anchoredPosition = new Vector2(0f, 250f);
            headerRt.sizeDelta = new Vector2(1000f, 90f);

            // 左侧黄色几何装饰条
            var barGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barGo.transform.SetParent(headerGo.transform, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(0f, 0.5f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = new Vector2(0f, 0f);
            barRt.sizeDelta = new Vector2(10f, 54f);
            var barShape = barGo.GetComponent<UIInkShapeGraphic>();
            barShape.Shape = UIInkShape.SharpRect;
            barShape.CornerRadius = ComicStyle.CornerSharp;
            barShape.BorderWidth = 0f;
            barShape.color = ComicStyle.Yellow;
            barShape.HardShadow = false;

            var title = CreateTextMesh("TitleText", headerGo.transform, "游戏暂停 / PAUSED", UITypography.FontSnapped(46f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: true);
            title.fontStyle = FontStyles.Bold; // display 档粗体标题
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 18f);
            titleRt.sizeDelta = new Vector2(-24f, 48f);

            var subtitle = CreateTextMesh("Subtitle", headerGo.transform, "比赛已冻结 · TIME SCALE FROZEN", UITypography.FontSnapped(20f), ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f), null, TextAlignmentOptions.Left, isDisplay: false);
            var subRt = subtitle.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(1f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(24f, -26f);
            subRt.sizeDelta = new Vector2(-24f, 30f);

            // 主菜单直角按钮群：宽 460、高 84（全部按钮档硬影 + 按压物理）
            // 1. 继续游戏（黄底黑字直角）
            var resumeBtn = CreateStyledButton("ResumeBtn", parent, "继续游戏", ComicStyle.Yellow, ComicStyle.Ink, new Vector2(460f, 84f), _theme, UITypography.FontSnapped(28f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resumeRt = resumeBtn.GetComponent<RectTransform>();
            resumeRt.anchoredPosition = new Vector2(PauseButtonColumnX, 90f);
            resumeBtn.onClick.AddListener(Resume);

            // 2. 设置（白底黑字直角）
            var settingsBtn = CreateStyledButton("SettingsBtn", parent, "系统设置", ComicStyle.Paper, ComicStyle.Ink, new Vector2(460f, 84f), _theme, UITypography.FontSnapped(28f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var setRt = settingsBtn.GetComponent<RectTransform>();
            setRt.anchoredPosition = new Vector2(PauseButtonColumnX, -10f);
            settingsBtn.onClick.AddListener(OpenSettings);

            // 3. 重新开始比赛（红底白字直角）
            var restartBtn = CreateStyledButton("RestartBtn", parent, "重新开始比赛", ComicStyle.Red, ComicStyle.Paper, new Vector2(460f, 84f), _theme, UITypography.FontSnapped(28f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var restartRt = restartBtn.GetComponent<RectTransform>();
            restartRt.anchoredPosition = new Vector2(PauseButtonColumnX, -110f);
            restartBtn.onClick.AddListener(RestartMatch);

            // 4. 返回主菜单（白底墨字 + 红边直角：离开本局回到赛前主菜单）
            var returnMenuBtn = CreateStyledButton("ReturnMenuBtn", parent, "返回主菜单", ComicStyle.Paper, ComicStyle.Ink, new Vector2(460f, 84f), _theme, UITypography.FontSnapped(28f), ComicStyle.CornerSharp, ComicStyle.Border);
            var returnMenuRt = returnMenuBtn.GetComponent<RectTransform>();
            returnMenuRt.anchoredPosition = new Vector2(PauseButtonColumnX, -210f);
            var returnMenuShape = returnMenuBtn.GetComponent<UIInkShapeGraphic>();
            if (returnMenuShape != null)
            {
                returnMenuShape.BorderColor = ComicStyle.Red; // 红边标记"离开本局"
            }
            returnMenuBtn.onClick.AddListener(ReturnToMainMenu);
            _returnMenuButton = returnMenuBtn;

            // 提示文案（纸白面板上用墨字，字号 ≥18）
            string hintLabel = Application.isMobilePlatform ? "点击上方按钮继续" : "按 [ESC] 键或点击按钮继续";
            var hint = CreateTextMesh("Hint", parent, hintLabel, UITypography.FontSnapped(20f), ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f), null, TextAlignmentOptions.Center, isDisplay: false);
            var hintRt = hint.rectTransform;
            hintRt.anchoredPosition = new Vector2(PauseButtonColumnX, -295f);
            hintRt.sizeDelta = new Vector2(500f, 30f);

            // 右列「操作说明」卡：原 HUD 底部那排小字改由这里承载，键帽 + 动作两栏排布
            BuildControlsCard(parent);
        }

        /// <summary>
        /// 「操作说明」卡（右栏 500×~526）：纸白次级色底 + 3px 墨框 + 面板硬影，
        /// 标题带黄色色标；每行左侧墨底纸白键帽（色签硬影），右侧粗体动作说明。
        /// 桌面列键鼠键位，移动端列触控键，避免战斗 HUD 底部再出现一排拥挤的小字。
        /// </summary>
        private void BuildControlsCard(Transform parent)
        {
            bool mobile = UITypography.IsMobileLike;
            string[] keys;
            string[] labels;
            if (mobile)
            {
                keys = new[] { "摇杆", "滑屏", "射击键", "潜墨键", "跳跃键", "大招键" };
                labels = new[] { "移动", "转视角", "开火涂地", "潜墨 / 爬墙", "跳跃", "释放大招" };
            }
            else
            {
                keys = new[] { "WASD", "鼠标", "左键", "右键/Shift", "空格", "E / 中键", "Q", "Esc" };
                labels = new[] { "移动", "转视角", "开火涂地", "潜墨 / 爬墙", "跳跃", "投掷手雷", "释放大招", "暂停 / 设置" };
            }

            int rowCount = keys.Length;
            float rowsHeight = rowCount * ControlsRowHeight + (rowCount - 1) * ControlsRowGap;
            float cardHeight = 118f + rowsHeight;
            float halfWidth = ControlsCardWidth * 0.5f;
            float halfHeight = cardHeight * 0.5f;

            var cardGo = new GameObject("ControlsCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            cardGo.transform.SetParent(parent, false);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2(ControlsCardRightX, ControlsCardTopY - halfHeight);
            cardRt.sizeDelta = new Vector2(ControlsCardWidth, cardHeight);

            var cardShape = cardGo.GetComponent<UIInkShapeGraphic>();
            cardShape.Shape = UIInkShape.SharpRect;
            cardShape.CornerRadius = ComicStyle.CornerSharp;
            cardShape.BorderWidth = ComicStyle.BorderThin;
            cardShape.BorderColor = ComicStyle.Ink;
            cardShape.color = ComicStyle.PaperAlt;
            cardShape.HardShadow = false;
            cardShape.Halftone = false;
            cardShape.GlossAlpha = 0f;
            cardShape.DrawGloss = false;
            cardShape.InnerRimAlpha = 0f;
            _theme.ApplyHardShadow(cardShape, UIHardShadowLevel.Panel);

            // 标题：左侧 8×30 黄色色标 + display 28 粗体「操作说明」+ 右侧小号英文角标
            var accentGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            accentGo.transform.SetParent(cardGo.transform, false);
            var accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = accentRt.anchorMax = new Vector2(0.5f, 0.5f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = new Vector2(-halfWidth + ControlsCardPadding, halfHeight - 42f);
            accentRt.sizeDelta = new Vector2(8f, 30f);
            var accentShape = accentGo.GetComponent<UIInkShapeGraphic>();
            accentShape.Shape = UIInkShape.SharpRect;
            accentShape.CornerRadius = ComicStyle.CornerSharp;
            accentShape.BorderWidth = 0f;
            accentShape.color = ComicStyle.Yellow;
            accentShape.HardShadow = false;
            accentShape.Halftone = false;

            var title = CreateTextMesh("ControlsTitle", cardGo.transform, "操作说明", UITypography.FontSnapped(28f),
                ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: true);
            title.fontStyle = FontStyles.Bold;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(-halfWidth + ControlsCardPadding + 8f + 14f, halfHeight - 42f);
            titleRt.sizeDelta = new Vector2(280f, 40f);

            var tag = CreateTextMesh("ControlsTag", cardGo.transform, "CONTROLS", UITypography.FontSnapped(18f),
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f), null, TextAlignmentOptions.Right, isDisplay: false);
            var tagRt = tag.rectTransform;
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(0.5f, 0.5f);
            tagRt.pivot = new Vector2(1f, 0.5f);
            tagRt.anchoredPosition = new Vector2(halfWidth - ControlsCardPadding, halfHeight - 42f);
            tagRt.sizeDelta = new Vector2(160f, 30f);

            // 标题下的细分隔线
            var lineGo = new GameObject("ControlsDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGo.transform.SetParent(cardGo.transform, false);
            var lineRt = lineGo.GetComponent<RectTransform>();
            lineRt.anchorMin = lineRt.anchorMax = new Vector2(0.5f, 0.5f);
            lineRt.pivot = new Vector2(0.5f, 0.5f);
            lineRt.anchoredPosition = new Vector2(0f, halfHeight - 78f);
            lineRt.sizeDelta = new Vector2(ControlsCardWidth - ControlsCardPadding * 2f, 2f);
            var lineImg = lineGo.GetComponent<Image>();
            lineImg.color = ComicStyle.InkSoft;
            lineImg.raycastTarget = false;

            float rowLeft = -halfWidth + ControlsCardPadding;
            float labelLeft = rowLeft + ControlsKeyCapWidth + 14f;
            float labelWidth = ControlsCardWidth - ControlsCardPadding * 2f - ControlsKeyCapWidth - 14f;
            float firstRowY = halfHeight - 120f;

            for (int i = 0; i < rowCount; i++)
            {
                float y = firstRowY - i * (ControlsRowHeight + ControlsRowGap);

                // 键帽：墨底纸白字 + 色签硬影，直角
                var capGo = new GameObject("KeyCap_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                capGo.transform.SetParent(cardGo.transform, false);
                var capRt = capGo.GetComponent<RectTransform>();
                capRt.anchorMin = capRt.anchorMax = new Vector2(0.5f, 0.5f);
                capRt.pivot = new Vector2(0f, 0.5f);
                capRt.anchoredPosition = new Vector2(rowLeft, y);
                capRt.sizeDelta = new Vector2(ControlsKeyCapWidth, ControlsRowHeight);

                var capShape = capGo.GetComponent<UIInkShapeGraphic>();
                capShape.Shape = UIInkShape.SharpRect;
                capShape.CornerRadius = ComicStyle.CornerSharp;
                capShape.BorderWidth = 0f;
                capShape.color = ComicStyle.Ink;
                capShape.HardShadow = false;
                capShape.Halftone = false;
                capShape.GlossAlpha = 0f;
                capShape.DrawGloss = false;
                _theme.ApplyHardShadow(capShape, UIHardShadowLevel.Tag);

                var capText = CreateTextMesh("KeyLabel", capGo.transform, keys[i], UITypography.FontSnapped(20f),
                    ComicStyle.Paper, null, TextAlignmentOptions.Center, isDisplay: false);
                capText.fontStyle = FontStyles.Bold;
                var capTextRt = capText.rectTransform;
                capTextRt.anchorMin = Vector2.zero;
                capTextRt.anchorMax = Vector2.one;
                capTextRt.offsetMin = Vector2.zero;
                capTextRt.offsetMax = Vector2.zero;

                var action = CreateTextMesh("ActionLabel", cardGo.transform, labels[i], UITypography.FontSnapped(22f),
                    ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
                action.fontStyle = FontStyles.Bold;
                var actionRt = action.rectTransform;
                actionRt.anchorMin = actionRt.anchorMax = new Vector2(0.5f, 0.5f);
                actionRt.pivot = new Vector2(0f, 0.5f);
                actionRt.anchoredPosition = new Vector2(labelLeft, y);
                actionRt.sizeDelta = new Vector2(labelWidth, ControlsRowHeight);
            }
        }

        private void BuildSettingsViewContent(Transform parent)
        {
            // 左对齐几何标题头
            var headerGo = new GameObject("SettingsHeaderBox", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0.5f, 0.5f);
            headerRt.anchorMax = new Vector2(0.5f, 0.5f);
            headerRt.anchoredPosition = new Vector2(0f, 335f);
            headerRt.sizeDelta = new Vector2(820f, 64f);

            var barGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barGo.transform.SetParent(headerGo.transform, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(0f, 0.5f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = new Vector2(0f, 0f);
            barRt.sizeDelta = new Vector2(8f, 44f);
            var barShape = barGo.GetComponent<UIInkShapeGraphic>();
            barShape.Shape = UIInkShape.SharpRect;
            barShape.CornerRadius = ComicStyle.CornerSharp;
            barShape.BorderWidth = 0f;
            barShape.color = ComicStyle.Yellow;
            barShape.HardShadow = false;

            var title = CreateTextMesh("Title", headerGo.transform, "系统设置 / SETTINGS", UITypography.FontSnapped(36f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: true);
            title.fontStyle = FontStyles.Bold; // display 档粗体
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(20f, 0f);
            titleRt.sizeDelta = new Vector2(-20f, 44f);

            bool showControlRow = Application.isMobilePlatform || Application.isEditor;
            int totalRows = showControlRow ? 8 : 7;

            var tableGo = new GameObject("SettingsTable", typeof(RectTransform));
            tableGo.transform.SetParent(parent, false);
            var tableRt = tableGo.GetComponent<RectTransform>();
            // 行高 72→84 后整表变高 (8×84=672)：从 -25 下移到 -45，
            // 顶边 291 仍在设置标题底边 303 之下，底边 -381 不压面板 3px 墨框。
            tableRt.anchoredPosition = new Vector2(0f, -45f);
            tableRt.sizeDelta = new Vector2(820f, PauseSettingRowHeight * totalRows);

            float labelFont = UITypography.FontSnapped(24f); // 桌面 24，移动端 30（满足标签 粗体 ≥24，移动端 ≥30）

            // Row 0: 主音量
            var row0 = CreateSettingRow(tableGo.transform, 0, totalRows);
            var volLabel = CreateTextMesh("VolLabel", row0.transform, "主音量", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            volLabel.fontStyle = FontStyles.Bold;
            var volLabelRt = volLabel.rectTransform;
            volLabelRt.anchorMin = new Vector2(0f, 0.5f);
            volLabelRt.anchorMax = new Vector2(0f, 0.5f);
            volLabelRt.pivot = new Vector2(0f, 0.5f);
            volLabelRt.anchoredPosition = new Vector2(28f, 0f);
            volLabelRt.sizeDelta = new Vector2(300f, 44f);

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
            var shakeLabel = CreateTextMesh("ShakeLabel", row1.transform, "屏幕震动 (无障碍减弱)", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            shakeLabel.fontStyle = FontStyles.Bold;
            var shakeLabelRt = shakeLabel.rectTransform;
            shakeLabelRt.anchorMin = new Vector2(0f, 0.5f);
            shakeLabelRt.anchorMax = new Vector2(0f, 0.5f);
            shakeLabelRt.pivot = new Vector2(0f, 0.5f);
            shakeLabelRt.anchoredPosition = new Vector2(28f, 0f);
            shakeLabelRt.sizeDelta = new Vector2(320f, 44f);

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
            var mouseLabel = CreateTextMesh("MouseSensLabel", row2.transform, "鼠标视角灵敏度", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            mouseLabel.fontStyle = FontStyles.Bold;
            var mouseLabelRt = mouseLabel.rectTransform;
            mouseLabelRt.anchorMin = new Vector2(0f, 0.5f);
            mouseLabelRt.anchorMax = new Vector2(0f, 0.5f);
            mouseLabelRt.pivot = new Vector2(0f, 0.5f);
            mouseLabelRt.anchoredPosition = new Vector2(28f, 0f);
            mouseLabelRt.sizeDelta = new Vector2(300f, 44f);

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
            var touchLabel = CreateTextMesh("TouchSensLabel", row3.transform, "触控视角灵敏度", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            touchLabel.fontStyle = FontStyles.Bold;
            var touchLabelRt = touchLabel.rectTransform;
            touchLabelRt.anchorMin = new Vector2(0f, 0.5f);
            touchLabelRt.anchorMax = new Vector2(0f, 0.5f);
            touchLabelRt.pivot = new Vector2(0f, 0.5f);
            touchLabelRt.anchoredPosition = new Vector2(28f, 0f);
            touchLabelRt.sizeDelta = new Vector2(300f, 44f);

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

            // Row 4: 视角垂直方向
            var row4 = CreateSettingRow(tableGo.transform, 4, totalRows);
            var invertLabel = CreateTextMesh("InvertYLabel", row4.transform, "视角垂直方向", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            invertLabel.fontStyle = FontStyles.Bold;
            var invertLabelRt = invertLabel.rectTransform;
            invertLabelRt.anchorMin = new Vector2(0f, 0.5f);
            invertLabelRt.anchorMax = new Vector2(0f, 0.5f);
            invertLabelRt.pivot = new Vector2(0f, 0.5f);
            invertLabelRt.anchoredPosition = new Vector2(28f, 0f);
            invertLabelRt.sizeDelta = new Vector2(300f, 44f);

            _invertYButton = CreateStyledButton(
                "InvertYBtn", row4.transform, "视角反转 Y：关",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
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
            var fpsLabel = CreateTextMesh("FpsLabel", row5.transform, "帧率上限", labelFont, ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            fpsLabel.fontStyle = FontStyles.Bold;
            var fpsLabelRt = fpsLabel.rectTransform;
            fpsLabelRt.anchorMin = new Vector2(0f, 0.5f);
            fpsLabelRt.anchorMax = new Vector2(0f, 0.5f);
            fpsLabelRt.pivot = new Vector2(0f, 0.5f);
            fpsLabelRt.anchoredPosition = new Vector2(28f, 0f);
            fpsLabelRt.sizeDelta = new Vector2(200f, 44f);

            BuildFrameRateButtons(row5.transform);

            // Row 6: 快捷操作（恢复默认 + 返回）
            var row6 = CreateSettingRow(tableGo.transform, 6, totalRows);
            var resetBtn = CreateStyledButton("ResetBtn", row6.transform, "恢复默认", ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight), _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0f, 0.5f);
            resetRt.anchorMax = new Vector2(0f, 0.5f);
            resetRt.pivot = new Vector2(0f, 0.5f);
            resetRt.anchoredPosition = new Vector2(28f, 0f);
            resetBtn.onClick.AddListener(OnResetDefaultsClicked);

            var backBtn = CreateStyledButton("BackBtn", row6.transform, "返回", ComicStyle.Yellow, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight), _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(1f, 0.5f);
            backRt.anchorMax = new Vector2(1f, 0.5f);
            backRt.pivot = new Vector2(1f, 0.5f);
            backRt.anchoredPosition = new Vector2(-28f, 0f);
            backBtn.onClick.AddListener(CloseSettings);

            // Row 7: 移动端操作设置入口
            if (showControlRow)
            {
                BuildControlSettingsRow(tableGo.transform, 7, totalRows);
            }
        }

        private void BuildControlSettingsRow(Transform parent, int index, int totalRows)
        {
            var row = CreateSettingRow(parent, index, totalRows);
            var label = CreateTextMesh("ControlLabel", row.transform, "触控操作与按键", UITypography.FontSnapped(24f), ComicStyle.Ink, null,
                TextAlignmentOptions.Left, isDisplay: false);
            label.fontStyle = FontStyles.Bold;
            var labelRt = label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0f, 0.5f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = new Vector2(28f, 0f);
            labelRt.sizeDelta = new Vector2(320f, 44f);

            var button = CreateStyledButton("ControlSettingsBtn", row.transform, "打开操作设置",
                ComicStyle.Yellow, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight), _theme, UITypography.FontSnapped(PauseSettingControlFont),
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
            Resume();
            var touchUI = FindAnyObjectByType<MobileTouchUI>();
            if (touchUI != null)
            {
                touchUI.OpenLayoutCustomizer();
            }
        }

        private GameObject CreateSettingRow(Transform parent, int index, int totalRows)
        {
            float rowHeight = PauseSettingRowHeight;
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
            shape.Shape = UIInkShape.SharpRect; // 直角条目
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            // 纸白面板上的内嵌列表行：统一次级纸面 + 墨色细分隔线（不再用深色交替条）
            shape.color = ComicStyle.PaperAlt;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            // 底部细墨线分隔
            if (index < totalRows - 1)
            {
                var lineGo = new GameObject("Separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineGo.transform.SetParent(rowGo.transform, false);
                var lrt = lineGo.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(0f, 2f);
                lrt.anchoredPosition = Vector2.zero;
                var img = lineGo.GetComponent<Image>();
                img.color = ComicStyle.InkSoft;
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
            rowRt.sizeDelta = new Vector2(460f, 64f);

            int[] rates = GameSettingsMath.AvailableFrameRates;
            _fpsButtons = new Button[rates.Length];
            _fpsShapes = new UIInkShapeGraphic[rates.Length];
            _fpsTexts = new TextMeshProUGUI[rates.Length];

            float btnWidth = 108f;
            float spacing = 8f;
            float startX = -(btnWidth * 2f + spacing * 1.5f) + btnWidth * 0.5f;

            for (int i = 0; i < rates.Length; i++)
            {
                int fps = rates[i];
                string label = GameSettingsMath.GetFrameRateLabel(fps);

                var btn = CreateStyledButton(
                    $"FpsBtn_{fps}", rowGo.transform, label,
                    ComicStyle.PaperAlt, ComicStyle.Ink, new Vector2(btnWidth, 64f),
                    _theme, UITypography.FontSnapped(22f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
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
                    _fpsShapes[i].color = isSelected ? ComicStyle.Yellow : ComicStyle.PaperAlt;
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
                _invertYText.color = ComicStyle.Ink; // 纸白/纸黄底上统一墨字
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
                if (MobileLayoutCustomizer.IsEditingAny)
                {
                    MobileLayoutCustomizer.ActiveEditor?.Close(false);
                    return;
                }

                TogglePause();
            }
        }

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

        public void Pause()
        {
            if (IsPaused || MobileLayoutCustomizer.IsEditingAny)
                return;

            IsPaused = true;

            HitstopService.Cancel();
            HitstopService.IsPaused = true;

            Time.timeScale = 0f;
            AudioListener.pause = true;

            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = false;
            }

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

        public void Resume()
        {
            if (!IsPaused)
                return;

            IsPaused = false;

            AudioListener.pause = false;
            HitstopService.IsPaused = false;
            HitstopService.Cancel();

            Time.timeScale = 1f;

            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = true;
            }

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

        /// <summary>关闭弹窗并回到赛前主菜单（Ready 阶段）；找不到比赛或已在主菜单时只关闭弹窗。</summary>
        public void ReturnToMainMenu()
        {
            Resume();
            MatchManager match = _match != null ? _match : MatchManager.Instance;
            if (match != null && match.CurrentPhase != MatchManager.Phase.Ready)
            {
                match.ReturnToMenu();
            }
        }

        public void OpenSettings()
        {
            _isSettingsOpen = true;
            _mainViewContainer.SetActive(false);
            _settingsViewContainer.SetActive(true);

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
            UpdateReturnMenuButtonVisibility();

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
            UpdateReturnMenuButtonVisibility();
        }

        /// <summary>已经在赛前主菜单（Ready）时隐藏「返回主菜单」，避免同屏出现无意义入口。</summary>
        private void UpdateReturnMenuButtonVisibility()
        {
            if (_returnMenuButton == null)
                return;

            MatchManager match = _match != null ? _match : MatchManager.Instance;
            bool alreadyInMenu = match != null && match.CurrentPhase == MatchManager.Phase.Ready;
            if (_returnMenuButton.gameObject.activeSelf == alreadyInMenu)
            {
                _returnMenuButton.gameObject.SetActive(!alreadyInMenu);
            }
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
            bool shouldShow = !IsPaused && isPlaying && isMobile && !MobileLayoutCustomizer.IsEditingAny;

            if (_mobilePauseButton.gameObject.activeSelf != shouldShow)
            {
                _mobilePauseButton.gameObject.SetActive(shouldShow);
            }
        }

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

        private void ApplyResponsivePanelScale()
        {
            if (_panelRect == null) return;

            float canvasHeight = 1080f;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 860f;

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
            shape.Shape = UIInkShape.SharpRect; // 直角方块
            shape.CornerRadius = cornerRadius;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fillColor;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.GlossAlpha = 0f;
            shape.DrawGloss = false;

            var btn = go.GetComponent<Button>();
            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(theme);
            var jelly = go.GetComponent<UIJellyButton>();
            jelly.Configure(theme, btn, shape);
            jelly.EnableThemeHardShadow(); // t-1 按钮硬影 (3,-4) + 0ms 压印 + 0.1s Cubic-Out 回弹

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
            containerRt.sizeDelta = new Vector2(460f, 64f); // 行内控件可触高度 ≥64

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(container.transform, false);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(0.76f, 0.5f);
            sliderRt.pivot = new Vector2(0f, 0.5f);
            sliderRt.sizeDelta = new Vector2(0f, 64f); // 拖动热区比可见轨道高，触控更好抓

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            // 1. 直角滑轨槽（纸面次级色底 + 3px 墨框）
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 30f);
            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.SharpRect;
            bgShape.CornerRadius = ComicStyle.CornerSharp;
            bgShape.BorderWidth = ComicStyle.BorderThin;
            bgShape.BorderColor = ComicStyle.Ink;
            bgShape.color = ComicStyle.PaperAlt;
            bgShape.HardShadow = false;
            bgShape.Halftone = false;

            // 2. 填充区域 Fill (直角黄色纯色块)
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(0f, 30f);
            fillAreaRt.offsetMin = new Vector2(3f, 0f);
            fillAreaRt.offsetMax = new Vector2(-3f, 0f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            var fillShape = fillGo.GetComponent<UIInkShapeGraphic>();
            fillShape.Shape = UIInkShape.SharpRect;
            fillShape.CornerRadius = ComicStyle.CornerSharp;
            fillShape.BorderWidth = 0f;
            fillShape.color = ComicStyle.Yellow;
            fillShape.HardShadow = false;
            fillShape.Halftone = false;
            slider.fillRect = fillRt;

            // 3. 直角纯色方块手柄 (44x44 纸白直角方块 + 墨边 + 色签硬影 2,-3)
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(22f, 0f);
            handleAreaRt.offsetMax = new Vector2(-22f, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(44f, 44f);
            var handleShape = handleGo.GetComponent<UIInkShapeGraphic>();
            handleShape.Shape = UIInkShape.SharpRect; // 纯色方块
            handleShape.CornerRadius = ComicStyle.CornerSharp;
            handleShape.BorderWidth = ComicStyle.BorderThin;
            handleShape.BorderColor = ComicStyle.Ink;
            handleShape.color = ComicStyle.Paper;
            handleShape.Halftone = false;
            UIThemeSO.Resolve(theme).ApplyHardShadow(handleShape, UIHardShadowLevel.Tag);

            slider.handleRect = handleRt;
            slider.targetGraphic = handleShape;

            // 4. 右侧数值标签（24 号粗体墨字）
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
            var valGo = new GameObject("ValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
            valGo.transform.SetParent(container.transform, false);
            var valRt = valGo.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0.78f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;

            valueLabel = valGo.GetComponent<TextMeshProUGUI>();
            valueLabel.fontSize = UITypography.FontSnapped(PauseSettingControlFont);
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.color = ComicStyle.Ink;
            valueLabel.alignment = TextAlignmentOptions.Right;
            valueLabel.raycastTarget = false;
            valueLabel.font = resolvedTheme.ResolveFont(false, "100%");

            return slider;
        }
    }
}
