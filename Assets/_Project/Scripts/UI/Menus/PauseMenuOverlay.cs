using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using P1.UI.WeaponSelect;
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

        /// <summary>设置行高：76 高 + 2px 分隔线；行内控件 64 高，仍满足 ≥64 可触下限。</summary>
        private const float PauseSettingRowHeight = 76f;

        /// <summary>设置行内按钮宽度（恢复默认 / 返回 / 开关等）。</summary>
        private const float PauseSettingControlWidth = 280f;

        /// <summary>设置行内控件高度（≥64 可触下限）。</summary>
        private const float PauseSettingControlHeight = 64f;

        /// <summary>设置行内控件字号（桌面 24，移动端 32）。</summary>
        private const float PauseSettingControlFont = 24f;

        // ── 设置页栅格（2026-11 重排） ────────────────────────────────
        /// <summary>设置表格统一宽度：1100 面板左右各留 100 呼吸区。</summary>
        private const float SettingsContentWidth = 900f;

        /// <summary>左侧标签列宽：移动端 30 号字也能单行放下「屏幕震动（无障碍减弱）」。</summary>
        private const float SettingsLabelWidth = 380f;

        /// <summary>右侧控件列宽：滑条 / 段选 / 开关共用同一条右对齐基准线。</summary>
        private const float SettingsControlWidth = 380f;

        /// <summary>表格内容左右内边距。</summary>
        private const float SettingsRowPadding = 28f;

        /// <summary>分组标题行高（小号眉题 + 英文角标 + 2px 分隔线）。</summary>
        private const float SettingsGroupHeaderHeight = 30f;

        /// <summary>页脚操作条：恢复默认 / 返回 / 自动保存提示。</summary>
        private const float SettingsFooterButtonWidth = 220f;

        /// <summary>滑轨可见高度与圆形手柄直径（26 = 与轨道同高；旧版 30 / 44 显得笨重）。</summary>
        private const float SettingsSliderTrackHeight = 26f;
        private const float SettingsSliderHandleSize = 26f;

        /// <summary>设置页板块数（声音 / 视角 / 画面与操作）：分页避免 20:9 手机横向被挤出屏幕。</summary>
        private const int SettingsTabCount = 3;

        /// <summary>板块内容顶边：标题底边 318 → Tab 行 302~226 → 板块组头从此开始。</summary>
        private const float SettingsPanelContentTop = 226f;

        /// <summary>暂停主面板宽度：左列 460 按钮 + 40 间距 + 右列 500 操作说明卡 + 左右各 50。</summary>
        private const float PausePanelWidth = 1100f;

        /// <summary>暂停主面板高度。设置页改为板块分页后与主视图/按键视图共用同一固定高度，
        /// 不再随设置项数量增高（避免 20:9 手机横屏下面板溢出屏幕、底部按钮点不到）。</summary>
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

        /// <summary>
        /// 响应式缩放容器（面板的父级）。缩放写这里而不是面板本身，
        /// 因为面板上的 UISpringMotion 会独占 localScale 做入场动画。
        /// </summary>
        private RectTransform _panelScaleRect;
        private UISpringMotion _panelMotion;
        private UIInkShapeGraphic _panelShape;

        // 退场转场：整层（遮罩 + 面板）淡出后再隐藏 _modalRoot，替代原先的硬切 SetActive(false)。
        // _modalGroup 盖住全屏遮罩——面板自带 PlayOut 淡出，但遮罩不在它的 CanvasGroup 里，
        // 只淡面板会让黑底突然消失，所以整层再走一次同节奏的淡出。
        private CanvasGroup _modalGroup;
        private bool _closingModal;          // 退场进行中标记
        private float _closingModalElapsed;  // 退场已耗时（兜底超时用）

        // 视图切换容器
        private GameObject _mainViewContainer;
        private GameObject _settingsViewContainer;
        private GameObject _keyBindingsViewContainer;
        private bool _isSettingsOpen;
        private bool _isKeyBindingsOpen;
        private bool _openedKeyBindingsFromMainView;
        private bool _isListeningForKey;
        private DesktopAction _listeningAction;
        private bool _listeningIsSecondary;
        private int _listeningStartFrame;
        private Button _activeListeningButton;
        private UIInkShapeGraphic _activeListeningShape;
        private TextMeshProUGUI _activeListeningText;

        private class KeyBindingRowUI
        {
            public DesktopAction action;
            public Button primaryBtn;
            public UIInkShapeGraphic primaryShape;
            public TextMeshProUGUI primaryText;
            public Button secondaryBtn;
            public UIInkShapeGraphic secondaryShape;
            public TextMeshProUGUI secondaryText;
        }

        private readonly List<KeyBindingRowUI> _keyBindingRows = new List<KeyBindingRowUI>();
        private static readonly Key[] s_AllCandidateKeys = (Key[])Enum.GetValues(typeof(Key));

        // 首页「设置」卡直开模式：只挂出设置视图，不走对局暂停语义（IsPaused 保持 false，
        // timeScale / AudioListener / GameplayInputEnabled / PauseStateChanged 均不动）。
        private bool _homeSettingsMode;
        private Button _returnMenuButton;

        // 设置项控件
        private Slider _volumeSlider;
        private TextMeshProUGUI _volumeValueText;
        private Slider _musicSlider;
        private TextMeshProUGUI _musicValueText;
        private Slider _sfxSlider;
        private TextMeshProUGUI _sfxValueText;
        private Slider _shakeSlider;
        private TextMeshProUGUI _shakeValueText;
        private Slider _mouseSensSlider;
        private TextMeshProUGUI _mouseSensValueText;
        private Slider _touchSensSlider;
        private TextMeshProUGUI _touchSensValueText;
        private Button _invertYButton;
        private UIInkShapeGraphic _invertYShape;
        private TextMeshProUGUI _invertYText;
        private Button _showFpsButton;
        private UIInkShapeGraphic _showFpsShape;
        private TextMeshProUGUI _showFpsText;
        private Button _showMinimapButton;
        private UIInkShapeGraphic _showMinimapShape;
        private TextMeshProUGUI _showMinimapText;
        private Button[] _fpsButtons;
        private UIInkShapeGraphic[] _fpsShapes;
        private TextMeshProUGUI[] _fpsTexts;

        // 设置页板块 Tab（声音 / 视角 / 画面与操作）
        private UIInkShapeGraphic[] _settingsTabShapes;
        private TextMeshProUGUI[] _settingsTabTexts;
        private GameObject[] _settingsPanels;
        private int _activeSettingsTab;

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

            // 兜底复位：对象若在退场途中被失活又重新启用，_closingModal / 整层 alpha 会停在中间态，
            // 且此刻 OnPlayedOut 可能已经错过。这里强制收尾，保证下次打开不会撞见半透明的残留弹窗。
            if (_closingModal) HideModalNow();
        }

        private void OnDisable()
        {
            // 拆除期不做转场：对象马上失活/销毁，PlayOut 的 OnPlayedOut 永远不会来，
            // 走 immediate 分支直接隐藏，避免留下「alpha 0 却仍激活」的残留。
            if (IsPaused)
            {
                Resume(true);
            }

            CloseSettingsFromHome(true);
        }

        private void OnDestroy()
        {
            if (IsPaused)
            {
                Resume(true);
            }

            CloseSettingsFromHome(true);

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

            // 退场推进：整层淡出 + 兜底超时（面板 motion 异常时强制收尾，避免卡在半透明）。
            if (_closingModal)
            {
                TickModalClosing();
            }

            if (_isListeningForKey)
            {
                HandleKeyListening();
            }
            else
            {
                HandleInput();
            }
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
            shape.Shape = UIInkShape.Pill; // 圆角化：88×88 移动端暂停按钮改正圆
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

            // 退场整层淡出的载体（遮罩 + 面板一起淡）；alpha 常态为 1，不影响任何入场逻辑。
            _modalGroup = _modalRoot.GetComponent<CanvasGroup>();
            if (_modalGroup == null) _modalGroup = _modalRoot.AddComponent<CanvasGroup>();
            _modalGroup.alpha = 1f;

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

            // 点面板外空白返回：按键页 → 设置页；设置页 → 暂停主视图（首页模式则关闭弹窗）。
            // 面板内空白被 PausePanel 的 Graphic 接住，不会误触。
            var backdropBtn = backdropGo.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(OnBackdropClicked);

            // 2. 响应式缩放容器：矮屏（20:9 手机横屏）按可用高度整体缩面板。
            //    必须与面板分开两层——面板身上的 UISpringMotion 独占 localScale（入场从 0.9 收到 1），
            //    若响应式缩放也写在面板上，入场结束会把矮屏的缩放覆盖回 1，面板溢出屏幕。
            var panelScaleGo = new GameObject("PausePanelScale", typeof(RectTransform));
            panelScaleGo.transform.SetParent(_modalRoot.transform, false);
            _panelScaleRect = panelScaleGo.GetComponent<RectTransform>();
            _panelScaleRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelScaleRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelScaleRect.pivot = new Vector2(0.5f, 0.5f);
            _panelScaleRect.sizeDelta = new Vector2(PausePanelWidth, PausePanelHeight);

            // 居中直角主面板 (1100x820)：t-1 纸白底 + 3px 墨框 + 弹窗硬影 (8,-9)
            var panelGo = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(panelScaleGo.transform, false);
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = Vector2.zero;
            _panelRect.anchorMax = Vector2.one;
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.offsetMin = Vector2.zero;
            _panelRect.offsetMax = Vector2.zero;

            _panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            _panelShape.Shape = UIInkShape.RoundedRect; // 圆角化：暂停面板自适应圆角
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

            // 3) 桌面按键设置视图容器
            _keyBindingsViewContainer = new GameObject("KeyBindingsView", typeof(RectTransform));
            _keyBindingsViewContainer.transform.SetParent(panelGo.transform, false);
            var keyRt = _keyBindingsViewContainer.GetComponent<RectTransform>();
            keyRt.anchorMin = Vector2.zero;
            keyRt.anchorMax = Vector2.one;
            keyRt.offsetMin = Vector2.zero;
            keyRt.offsetMax = Vector2.zero;
            BuildKeyBindingsViewContent(_keyBindingsViewContainer.transform);

            _keyBindingsViewContainer.SetActive(false);
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
            barShape.Shape = UIInkShape.Pill; // 圆角化：标题竖条改胶囊
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

            // 主菜单直角按钮群：宽 460、高 76（全部按钮档硬影 + 按压物理，匀称呼吸感排版）
            Vector2 buttonSize = new Vector2(460f, 76f);
            float buttonFont = UITypography.FontSnapped(26f);

            // 1. 继续游戏（黄底黑字直角）
            var resumeBtn = CreateStyledButton("ResumeBtn", parent, "继续游戏", ComicStyle.Yellow, ComicStyle.Ink, buttonSize, _theme, buttonFont, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resumeRt = resumeBtn.GetComponent<RectTransform>();
            resumeRt.anchoredPosition = new Vector2(PauseButtonColumnX, 134f);
            resumeBtn.onClick.AddListener(() => Resume());

            // 2. 系统设置（白底黑字直角）
            var settingsBtn = CreateStyledButton("SettingsBtn", parent, "系统设置", ComicStyle.Paper, ComicStyle.Ink, buttonSize, _theme, buttonFont, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var setRt = settingsBtn.GetComponent<RectTransform>();
            setRt.anchoredPosition = new Vector2(PauseButtonColumnX, 42f);
            settingsBtn.onClick.AddListener(OpenSettings);

            // 3. 操作按钮调整（白底黑字直角：移动端触控按键布局自定义 / 桌面端按键设置）
            string controlsBtnText = UITypography.IsMobileLike ? "操作按钮调整" : "按键设置";
            var controlsBtn = CreateStyledButton("ControlsBtn", parent, controlsBtnText, ComicStyle.Paper, ComicStyle.Ink, buttonSize, _theme, buttonFont, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var controlsRt = controlsBtn.GetComponent<RectTransform>();
            controlsRt.anchoredPosition = new Vector2(PauseButtonColumnX, -50f);
            controlsBtn.onClick.AddListener(() =>
            {
                if (UITypography.IsMobileLike)
                {
                    OpenControlSettings();
                }
                else
                {
                    OpenKeyBindings(fromMainView: true);
                }
            });

            // 4. 重新开始比赛（红底白字直角）
            var restartBtn = CreateStyledButton("RestartBtn", parent, "重新开始比赛", ComicStyle.Red, ComicStyle.Paper, buttonSize, _theme, buttonFont, ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var restartRt = restartBtn.GetComponent<RectTransform>();
            restartRt.anchoredPosition = new Vector2(PauseButtonColumnX, -142f);
            restartBtn.onClick.AddListener(RestartMatch);

            // 5. 返回主菜单（白底墨字 + 红边直角：离开本局回到赛前主菜单）
            var returnMenuBtn = CreateStyledButton("ReturnMenuBtn", parent, "返回主菜单", ComicStyle.Paper, ComicStyle.Ink, buttonSize, _theme, buttonFont, ComicStyle.CornerSharp, ComicStyle.Border);
            var returnMenuRt = returnMenuBtn.GetComponent<RectTransform>();
            returnMenuRt.anchoredPosition = new Vector2(PauseButtonColumnX, -234f);
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
            hintRt.anchoredPosition = new Vector2(PauseButtonColumnX, -312f);
            hintRt.sizeDelta = new Vector2(500f, 26f);

            // 隐私政策入口：同意后也能随时查看完整政策（TapTap 隐私合规要求）。
            var privacyBtn = CreateStyledButton("PrivacyBtn", parent, "隐私政策", ComicStyle.Paper, ComicStyle.Ink,
                new Vector2(300f, 44f), _theme, UITypography.FontSnapped(20f),
                ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var privacyRt = privacyBtn.GetComponent<RectTransform>();
            privacyRt.anchoredPosition = new Vector2(PauseButtonColumnX, -360f);
            privacyBtn.onClick.AddListener(PrivacyConsentDialog.OpenPolicyPage);

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

            // 键位表统一来自 HomeInfoContent：首页「键位说明 / 操作指南」浮层与这里共用一份数据源，
            // 桌面端动态展示最新自定义键位。
            IReadOnlyList<ControlRow> keys = HomeInfoContent.DynamicControls(mobile);
            int rowCount = keys.Count;
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
            cardShape.Shape = UIInkShape.RoundedRect; // 圆角化：操作说明卡自适应圆角
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
            accentShape.Shape = UIInkShape.Pill; // 圆角化：标题色标改胶囊
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

                // 键帽：墨底纸白字 + 色签硬影，圆角（自适应半径）
                var capGo = new GameObject("KeyCap_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                capGo.transform.SetParent(cardGo.transform, false);
                var capRt = capGo.GetComponent<RectTransform>();
                capRt.anchorMin = capRt.anchorMax = new Vector2(0.5f, 0.5f);
                capRt.pivot = new Vector2(0f, 0.5f);
                capRt.anchoredPosition = new Vector2(rowLeft, y);
                capRt.sizeDelta = new Vector2(ControlsKeyCapWidth, ControlsRowHeight);

                var capShape = capGo.GetComponent<UIInkShapeGraphic>();
                capShape.Shape = UIInkShape.RoundedRect; // 圆角化：键帽
                capShape.BorderWidth = 0f;
                capShape.color = ComicStyle.Ink;
                capShape.HardShadow = false;
                capShape.Halftone = false;
                capShape.GlossAlpha = 0f;
                capShape.DrawGloss = false;
                _theme.ApplyHardShadow(capShape, UIHardShadowLevel.Tag);

                var capText = CreateTextMesh("KeyLabel", capGo.transform, keys[i].Key, UITypography.FontSnapped(20f),
                    ComicStyle.Paper, null, TextAlignmentOptions.Center, isDisplay: false);
                capText.fontStyle = FontStyles.Bold;
                var capTextRt = capText.rectTransform;
                capTextRt.anchorMin = Vector2.zero;
                capTextRt.anchorMax = Vector2.one;
                capTextRt.offsetMin = Vector2.zero;
                capTextRt.offsetMax = Vector2.zero;

                var action = CreateTextMesh("ActionLabel", cardGo.transform, keys[i].Action, UITypography.FontSnapped(22f),
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
            // ── 标题头（900 宽，与表格左缘对齐；右侧标注「更改即时生效」） ──
            var headerGo = new GameObject("SettingsHeaderBox", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0.5f, 0.5f);
            headerRt.anchorMax = new Vector2(0.5f, 0.5f);
            headerRt.anchoredPosition = new Vector2(0f, 350f);
            headerRt.sizeDelta = new Vector2(SettingsContentWidth, 64f);

            BuildAccentBar(headerGo.transform, new Vector2(0f, 0f), new Vector2(8f, 44f));

            var title = CreateTextMesh("Title", headerGo.transform, "系统设置 / SETTINGS", UITypography.FontSnapped(36f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: true);
            title.fontStyle = FontStyles.Bold; // display 档粗体
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(SettingsRowPadding, 0f);
            titleRt.sizeDelta = new Vector2(560f, 50f);

            var headerHint = CreateTextMesh("HeaderHint", headerGo.transform, "更改即时生效", UITypography.FontSnapped(18f),
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f), null, TextAlignmentOptions.Right, isDisplay: false);
            var headerHintRt = headerHint.rectTransform;
            headerHintRt.anchorMin = new Vector2(1f, 0.5f);
            headerHintRt.anchorMax = new Vector2(1f, 0.5f);
            headerHintRt.pivot = new Vector2(1f, 0.5f);
            headerHintRt.anchoredPosition = new Vector2(-SettingsRowPadding, 0f);
            headerHintRt.sizeDelta = new Vector2(300f, 34f);

            // 从上往下排：标题底边 318 → Tab 行顶边 302 → 板块内容顶边 226 → 固定页脚（divider -316）。
            // 设置项按板块分页，单页最多 4 行，固定 820 面板在任何比例屏幕上都放得下。
            float cursor = 302f;

            // 板块 Tab 段选（声音 / 视角 / 画面与操作）
            BuildSettingsTabRow(parent, ref cursor);

            // 三个板块容器：共用同一固定区域，切换显示
            _settingsPanels = new GameObject[SettingsTabCount];
            _settingsPanels[0] = CreateSettingsPanelContainer(parent, "SettingsPanel_Audio");
            BuildAudioSettingsPanel(_settingsPanels[0].transform);
            _settingsPanels[1] = CreateSettingsPanelContainer(parent, "SettingsPanel_Camera");
            BuildCameraSettingsPanel(_settingsPanels[1].transform);
            _settingsPanels[2] = CreateSettingsPanelContainer(parent, "SettingsPanel_Display");
            BuildDisplaySettingsPanel(_settingsPanels[2].transform);

            // 页脚固定：恢复默认 / 自动保存提示 / 返回（dividerY = -302 - 14 = -316，与旧版 5 行构图一致）
            BuildSettingsFooter(parent, -302f);

            SetSettingsTab(_activeSettingsTab);
        }

        /// <summary>板块 Tab 段选行：3 段等宽按钮，选中态黄底粗体（与帧率段选同一视觉语言）。</summary>
        private void BuildSettingsTabRow(Transform parent, ref float cursor)
        {
            var rowGo = new GameObject("SettingsTabs", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0.5f, 0.5f);
            rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0f, cursor - PauseSettingRowHeight * 0.5f);
            rowRt.sizeDelta = new Vector2(SettingsContentWidth, PauseSettingRowHeight);
            cursor -= PauseSettingRowHeight;

            string[] labels = { "声音", "视角", "画面与操作" };
            float spacing = 8f;
            float btnWidth = (SettingsContentWidth - SettingsRowPadding * 2f - spacing * (SettingsTabCount - 1))
                             / SettingsTabCount;
            float startX = -SettingsContentWidth * 0.5f + SettingsRowPadding + btnWidth * 0.5f;

            _settingsTabShapes = new UIInkShapeGraphic[SettingsTabCount];
            _settingsTabTexts = new TextMeshProUGUI[SettingsTabCount];

            for (int i = 0; i < SettingsTabCount; i++)
            {
                int captured = i;
                var btn = CreateStyledButton(
                    $"SettingsTab_{i}", rowGo.transform, labels[i],
                    ComicStyle.Paper, ComicStyle.Ink, new Vector2(btnWidth, PauseSettingControlHeight),
                    _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
                var btnRt = btn.GetComponent<RectTransform>();
                btnRt.anchoredPosition = new Vector2(startX + i * (btnWidth + spacing), 0f);

                _settingsTabShapes[i] = btn.GetComponent<UIInkShapeGraphic>();
                _settingsTabTexts[i] = btn.GetComponentInChildren<TextMeshProUGUI>();
                btn.onClick.AddListener(() => SetSettingsTab(captured));
            }
        }

        /// <summary>切换设置板块（0 声音 / 1 视角 / 2 画面与操作），并同步 Tab 选中态。</summary>
        private void SetSettingsTab(int index)
        {
            _activeSettingsTab = Mathf.Clamp(index, 0, SettingsTabCount - 1);

            for (int i = 0; i < SettingsTabCount; i++)
            {
                bool active = i == _activeSettingsTab;
                if (_settingsPanels != null && i < _settingsPanels.Length && _settingsPanels[i] != null)
                {
                    _settingsPanels[i].SetActive(active);
                }
                if (_settingsTabShapes != null && _settingsTabShapes[i] != null)
                {
                    _settingsTabShapes[i].color = active ? ComicStyle.Yellow : ComicStyle.Paper;
                }
                if (_settingsTabTexts != null && _settingsTabTexts[i] != null)
                {
                    _settingsTabTexts[i].color = ComicStyle.Ink;
                    _settingsTabTexts[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        /// <summary>板块容器：铺满设置视图，内部行坐标与主设置栅格同基准。</summary>
        private GameObject CreateSettingsPanelContainer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        // ── 板块 0：声音（4 行） ──────────────────────────────────────
        private void BuildAudioSettingsPanel(Transform panel)
        {
            float cursor = SettingsPanelContentTop;
            cursor = BuildSettingsGroupHeader(panel, cursor, "声音", "AUDIO");

            var row0 = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(row0.transform, "主音量");
            _volumeSlider = CreateStyledSlider("VolumeSlider", row0.transform, 0f, 1f, GameSettingsService.MasterVolume, _theme, out _volumeValueText);
            PlaceSettingControl(_volumeSlider.transform.parent);
            _volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
            UpdateVolumeLabel(GameSettingsService.MasterVolume);

            var rowMusic = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowMusic.transform, "音乐音量", "BGM");
            _musicSlider = CreateStyledSlider("MusicSlider", rowMusic.transform, 0f, 1f, GameSettingsService.MusicVolume, _theme, out _musicValueText);
            PlaceSettingControl(_musicSlider.transform.parent);
            _musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
            UpdateMusicVolumeLabel(GameSettingsService.MusicVolume);

            var rowSfx = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowSfx.transform, "音效音量", "含界面音");
            _sfxSlider = CreateStyledSlider("SfxSlider", rowSfx.transform, 0f, 1f, GameSettingsService.SfxVolume, _theme, out _sfxValueText);
            PlaceSettingControl(_sfxSlider.transform.parent);
            _sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
            UpdateSfxVolumeLabel(GameSettingsService.SfxVolume);

            var rowShake = CreateSettingRow(panel, ref cursor, showSeparator: false);
            CreateSettingLabel(rowShake.transform, "屏幕震动", "无障碍减弱");
            _shakeSlider = CreateStyledSlider("ShakeSlider", rowShake.transform, 0f, 1f, GameSettingsService.ShakeScale, _theme, out _shakeValueText);
            PlaceSettingControl(_shakeSlider.transform.parent);
            _shakeSlider.onValueChanged.AddListener(OnShakeSliderChanged);
            UpdateShakeLabel(GameSettingsService.ShakeScale);
        }

        // ── 板块 1：视角（3 行） ──────────────────────────────────────
        private void BuildCameraSettingsPanel(Transform panel)
        {
            float cursor = SettingsPanelContentTop;
            cursor = BuildSettingsGroupHeader(panel, cursor, "视角", "CAMERA");

            var rowMouse = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowMouse.transform, "鼠标视角灵敏度");
            _mouseSensSlider = CreateStyledSlider(
                "MouseSensSlider", rowMouse.transform,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.MouseLookSensitivity, _theme, out _mouseSensValueText);
            PlaceSettingControl(_mouseSensSlider.transform.parent);
            _mouseSensSlider.onValueChanged.AddListener(OnMouseSensChanged);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);

            var rowTouch = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowTouch.transform, "触控视角灵敏度");
            _touchSensSlider = CreateStyledSlider(
                "TouchSensSlider", rowTouch.transform,
                GameSettingsMath.MinLookSensitivity, GameSettingsMath.MaxLookSensitivity,
                GameSettingsService.TouchLookSensitivity, _theme, out _touchSensValueText);
            PlaceSettingControl(_touchSensSlider.transform.parent);
            _touchSensSlider.onValueChanged.AddListener(OnTouchSensChanged);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);

            var rowInvert = CreateSettingRow(panel, ref cursor, showSeparator: false);
            CreateSettingLabel(rowInvert.transform, "视角垂直方向", "反转 Y 轴");
            _invertYButton = CreateStyledButton(
                "InvertYBtn", rowInvert.transform, "关闭",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            PlaceSettingControl(_invertYButton.transform);
            _invertYShape = _invertYButton.GetComponent<UIInkShapeGraphic>();
            _invertYText = _invertYButton.GetComponentInChildren<TextMeshProUGUI>();
            _invertYButton.onClick.AddListener(OnInvertYToggled);
            UpdateInvertYLabel();
        }

        // ── 板块 2：画面与操作（5 行） ────────────────────────────────
        private void BuildDisplaySettingsPanel(Transform panel)
        {
            float cursor = SettingsPanelContentTop;
            cursor = BuildSettingsGroupHeader(panel, cursor, "画面与操作", "DISPLAY & CONTROLS");

            var rowFps = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowFps.transform, "帧率上限", "FPS");
            BuildFrameRateButtons(rowFps.transform);

            var rowShowFps = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowShowFps.transform, "显示帧率", "HUD 左上角");
            _showFpsButton = CreateStyledButton(
                "ShowFpsBtn", rowShowFps.transform, "关闭",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            PlaceSettingControl(_showFpsButton.transform);
            _showFpsShape = _showFpsButton.GetComponent<UIInkShapeGraphic>();
            _showFpsText = _showFpsButton.GetComponentInChildren<TextMeshProUGUI>();
            _showFpsButton.onClick.AddListener(OnShowFpsToggled);
            UpdateShowFpsLabel();

            var rowMinimap = CreateSettingRow(panel, ref cursor, showSeparator: true);
            CreateSettingLabel(rowMinimap.transform, "小地图", "右上角");
            _showMinimapButton = CreateStyledButton(
                "ShowMinimapBtn", rowMinimap.transform, "开启",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            PlaceSettingControl(_showMinimapButton.transform);
            _showMinimapShape = _showMinimapButton.GetComponent<UIInkShapeGraphic>();
            _showMinimapText = _showMinimapButton.GetComponentInChildren<TextMeshProUGUI>();
            _showMinimapButton.onClick.AddListener(OnShowMinimapToggled);
            UpdateShowMinimapLabel();

            // 操作设置入口（移动端打开触控布局自定义，桌面端打开按键自定义）
            BuildControlSettingsRow(panel, ref cursor);
            // 操作指南入口：首页地图卡取代了原「操作指南」卡，这里保证指南仍可达（含首页移动端）。
            BuildOperationGuideRow(panel, ref cursor);
        }

        /// <summary>打开与首页同源的「操作说明」指南；叠在暂停画布（100）之上。</summary>
        private void BuildOperationGuideRow(Transform parent, ref float cursor)
        {
            var row = CreateSettingRow(parent, ref cursor, showSeparator: false);
            CreateSettingLabel(row.transform, "操作说明", "移动 / 战斗 / 规则速记");

            var button = CreateStyledButton("OperationGuideBtn", row.transform, "查看指南",
                ComicStyle.Paper, ComicStyle.Ink,
                new Vector2(PauseSettingControlWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont),
                ComicStyle.CornerSharp, ComicStyle.BorderThin);
            PlaceSettingControl(button.transform);
            button.onClick.AddListener(OpenOperationGuide);
        }

        private void OpenOperationGuide()
        {
            RectTransform host = _safeAreaRect != null ? _safeAreaRect : (RectTransform)transform;
            HomeInfoOverlay.Show(host, _theme, HomeInfoTab.Controls, sortingOrder: 110);
        }

        /// <summary>分组眉题：左侧黄色色标 + 中文组名 + 右侧英文角标 + 2px 分隔线。</summary>
        private float BuildSettingsGroupHeader(Transform parent, float topY, string titleText, string tagText)
        {
            var groupGo = new GameObject("Group_" + titleText, typeof(RectTransform));
            groupGo.transform.SetParent(parent, false);
            var groupRt = groupGo.GetComponent<RectTransform>();
            groupRt.anchorMin = new Vector2(0.5f, 0.5f);
            groupRt.anchorMax = new Vector2(0.5f, 0.5f);
            groupRt.pivot = new Vector2(0.5f, 0.5f);
            groupRt.anchoredPosition = new Vector2(0f, topY - SettingsGroupHeaderHeight * 0.5f);
            groupRt.sizeDelta = new Vector2(SettingsContentWidth, SettingsGroupHeaderHeight);

            BuildAccentBar(groupGo.transform, new Vector2(0f, 0f), new Vector2(6f, 22f));

            var title = CreateTextMesh("GroupTitle", groupGo.transform, titleText, UITypography.FontSnapped(22f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            title.fontStyle = FontStyles.Bold;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(SettingsRowPadding, 0f);
            titleRt.sizeDelta = new Vector2(420f, 30f);

            var tag = CreateTextMesh("GroupTag", groupGo.transform, tagText, UITypography.FontSnapped(18f),
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f), null, TextAlignmentOptions.Right, isDisplay: false);
            var tagRt = tag.rectTransform;
            tagRt.anchorMin = new Vector2(1f, 0.5f);
            tagRt.anchorMax = new Vector2(1f, 0.5f);
            tagRt.pivot = new Vector2(1f, 0.5f);
            tagRt.anchoredPosition = new Vector2(-SettingsRowPadding, 0f);
            tagRt.sizeDelta = new Vector2(300f, 30f);

            var lineGo = new GameObject("GroupDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGo.transform.SetParent(groupGo.transform, false);
            var lineRt = lineGo.GetComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0f, 0f);
            lineRt.anchorMax = new Vector2(1f, 0f);
            lineRt.pivot = new Vector2(0.5f, 0f);
            lineRt.sizeDelta = new Vector2(0f, 2f);
            lineRt.anchoredPosition = Vector2.zero;
            var lineImg = lineGo.GetComponent<Image>();
            lineImg.color = ComicStyle.InkSoft;
            lineImg.raycastTarget = false;

            return topY - SettingsGroupHeaderHeight;
        }

        /// <summary>按游标向下创建一行 900×76 的设置行；游标最终停在下一行顶边。</summary>
        private GameObject CreateSettingRow(Transform parent, ref float cursor, bool showSeparator)
        {
            float rowTop = cursor;
            cursor -= PauseSettingRowHeight;

            var rowGo = new GameObject($"SettingRow_{Mathf.RoundToInt(rowTop)}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            rowGo.transform.SetParent(parent, false);
            var rt = rowGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(SettingsContentWidth, PauseSettingRowHeight);
            rt.anchoredPosition = new Vector2(0f, rowTop - PauseSettingRowHeight * 0.5f);

            var shape = rowGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：设置行自适应圆角
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.PaperAlt;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            if (showSeparator)
            {
                // 设置行是圆角卡片：2px 分隔线必须内缩到圆角直线段内，否则线端会从两侧圆角
                // 探出成「小尾巴」（用户反馈的线条溢出）。内缩量取行圆角半径，与
                // UIInkShapeGraphic 的 CornerFor 自适应半径同源。
                float rowRadius = ComicStyle.CornerFor(SettingsContentWidth, PauseSettingRowHeight);
                var lineGo = new GameObject("Separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineGo.transform.SetParent(rowGo.transform, false);
                var lrt = lineGo.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(-rowRadius * 2f, 2f);
                lrt.anchoredPosition = Vector2.zero;
                var img = lineGo.GetComponent<Image>();
                img.color = ComicStyle.InkSoft;
                img.raycastTarget = false;
            }

            return rowGo;
        }

        /// <summary>左侧统一标签列；可选小号灰色注解（如「无障碍减弱」「FPS」），不参与折行。</summary>
        private TextMeshProUGUI CreateSettingLabel(Transform row, string text, string hint = null)
        {
            var label = CreateTextMesh("Label", row, text, UITypography.FontSnapped(24f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
            label.fontStyle = FontStyles.Bold;
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(SettingsRowPadding, 0f);
            rt.sizeDelta = new Vector2(SettingsLabelWidth, 44f);

            if (!string.IsNullOrEmpty(hint))
            {
                float mainWidth = Mathf.Min(label.GetPreferredValues(text).x, SettingsLabelWidth - 90f);
                var hintLabel = CreateTextMesh("Hint", row, hint, UITypography.FontSnapped(18f),
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f), null, TextAlignmentOptions.Left, isDisplay: false);
                var hintRt = hintLabel.rectTransform;
                hintRt.anchorMin = new Vector2(0f, 0.5f);
                hintRt.anchorMax = new Vector2(0f, 0.5f);
                hintRt.pivot = new Vector2(0f, 0.5f);
                hintRt.anchoredPosition = new Vector2(SettingsRowPadding + mainWidth + 14f, 0f);
                hintRt.sizeDelta = new Vector2(240f, 34f);
            }

            return label;
        }

        /// <summary>控件统一贴右：与页脚「返回」、分组英文角标共用同一条 28px 内边距基准线。</summary>
        private static void PlaceSettingControl(Transform control)
        {
            var rt = control as RectTransform;
            if (rt == null) rt = control.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-SettingsRowPadding, 0f);
        }

        private void BuildControlSettingsRow(Transform parent, ref float cursor)
        {
            bool isMobile = UITypography.IsMobileLike;

            string labelText = isMobile ? "触控操作与按键" : "键盘与鼠标按键";
            string btnText = isMobile ? "打开操作设置" : "自定义按键";

            var row = CreateSettingRow(parent, ref cursor, showSeparator: false);
            CreateSettingLabel(row.transform, labelText);

            var button = CreateStyledButton("ControlSettingsBtn", row.transform, btnText,
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(PauseSettingControlWidth, PauseSettingControlHeight), _theme, UITypography.FontSnapped(PauseSettingControlFont),
                ComicStyle.CornerSharp, ComicStyle.BorderThin);
            PlaceSettingControl(button.transform);

            button.onClick.AddListener(() =>
            {
                if (isMobile)
                {
                    OpenControlSettings();
                }
                else
                {
                    OpenKeyBindings();
                }
            });
        }

        /// <summary>页脚：2px 分隔线 + 左「恢复默认」+ 中「自动保存」提示 + 右「返回」主操作。</summary>
        private void BuildSettingsFooter(Transform parent, float cursor)
        {
            float dividerY = cursor - 14f;

            var lineGo = new GameObject("FooterDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGo.transform.SetParent(parent, false);
            var lineRt = lineGo.GetComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0.5f, 0.5f);
            lineRt.anchorMax = new Vector2(0.5f, 0.5f);
            lineRt.pivot = new Vector2(0.5f, 0.5f);
            lineRt.anchoredPosition = new Vector2(0f, dividerY);
            lineRt.sizeDelta = new Vector2(SettingsContentWidth, 2f);
            var lineImg = lineGo.GetComponent<Image>();
            lineImg.color = ComicStyle.InkSoft;
            lineImg.raycastTarget = false;

            var footerGo = new GameObject("SettingsFooter", typeof(RectTransform));
            footerGo.transform.SetParent(parent, false);
            var footerRt = footerGo.GetComponent<RectTransform>();
            footerRt.anchorMin = new Vector2(0.5f, 0.5f);
            footerRt.anchorMax = new Vector2(0.5f, 0.5f);
            footerRt.pivot = new Vector2(0.5f, 0.5f);
            footerRt.anchoredPosition = new Vector2(0f, dividerY - 8f - PauseSettingControlHeight * 0.5f);
            footerRt.sizeDelta = new Vector2(SettingsContentWidth, PauseSettingControlHeight);

            var resetBtn = CreateStyledButton("ResetBtn", footerGo.transform, "恢复默认",
                ComicStyle.Paper, ComicStyle.Ink, new Vector2(SettingsFooterButtonWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0f, 0.5f);
            resetRt.anchorMax = new Vector2(0f, 0.5f);
            resetRt.pivot = new Vector2(0f, 0.5f);
            resetRt.anchoredPosition = new Vector2(SettingsRowPadding, 0f);
            resetBtn.onClick.AddListener(OnResetDefaultsClicked);

            string footerHintText = Application.isMobilePlatform ? "更改自动保存" : "更改自动保存 · ESC 返回";
            var footerHint = CreateTextMesh("FooterHint", footerGo.transform, footerHintText, UITypography.FontSnapped(18f),
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f), null, TextAlignmentOptions.Center, isDisplay: false);
            var footerHintRt = footerHint.rectTransform;
            footerHintRt.anchorMin = new Vector2(0.5f, 0.5f);
            footerHintRt.anchorMax = new Vector2(0.5f, 0.5f);
            footerHintRt.pivot = new Vector2(0.5f, 0.5f);
            footerHintRt.anchoredPosition = Vector2.zero;
            footerHintRt.sizeDelta = new Vector2(400f, 34f);

            var backBtn = CreateStyledButton("BackBtn", footerGo.transform, "返回",
                ComicStyle.Yellow, ComicStyle.Ink, new Vector2(SettingsFooterButtonWidth, PauseSettingControlHeight),
                _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(1f, 0.5f);
            backRt.anchorMax = new Vector2(1f, 0.5f);
            backRt.pivot = new Vector2(1f, 0.5f);
            backRt.anchoredPosition = new Vector2(-SettingsRowPadding, 0f);
            backBtn.onClick.AddListener(CloseSettings);
        }

        /// <summary>标题 / 分组通用的黄色竖向色标。</summary>
        private static void BuildAccentBar(Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            var barGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barGo.transform.SetParent(parent, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(0f, 0.5f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = anchoredPosition;
            barRt.sizeDelta = size;

            var barShape = barGo.GetComponent<UIInkShapeGraphic>();
            barShape.Shape = UIInkShape.Pill; // 圆角化：分区色标改胶囊
            barShape.BorderWidth = 0f;
            barShape.color = ComicStyle.Yellow;
            barShape.HardShadow = false;
        }

        private void OpenControlSettings()
        {
            Resume();
            var touchUI = FindAnyObjectByType<MobileTouchUI>(FindObjectsInactive.Include);
            if (touchUI != null)
            {
                if (!touchUI.gameObject.activeSelf)
                {
                    touchUI.gameObject.SetActive(true);
                }
                touchUI.OpenLayoutCustomizer();
            }
            else
            {
                Debug.LogWarning("[PauseMenuOverlay] 未找到 MobileTouchUI，无法打开触控按键调整");
            }
        }

        public void OpenKeyBindings(bool fromMainView = false)
        {
            if (_keyBindingsViewContainer == null) return;
            _openedKeyBindingsFromMainView = fromMainView;
            _isKeyBindingsOpen = true;
            if (fromMainView)
            {
                if (_mainViewContainer != null) _mainViewContainer.SetActive(false);
            }
            else
            {
                if (_settingsViewContainer != null) _settingsViewContainer.SetActive(false);
            }
            _keyBindingsViewContainer.SetActive(true);
            RefreshKeyBindingsUI();

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        public void CloseKeyBindings()
        {
            if (_isListeningForKey)
            {
                CancelKeyListening();
            }

            _isKeyBindingsOpen = false;
            if (_keyBindingsViewContainer != null) _keyBindingsViewContainer.SetActive(false);
            if (_openedKeyBindingsFromMainView)
            {
                if (_mainViewContainer != null) _mainViewContainer.SetActive(true);
            }
            else
            {
                if (_settingsViewContainer != null) _settingsViewContainer.SetActive(true);
            }
            _openedKeyBindingsFromMainView = false;

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        private void BuildKeyBindingsViewContent(Transform parent)
        {
            _keyBindingRows.Clear();

            // 1. 标题头 (960x64，锚定在 y = 335)
            var headerGo = new GameObject("KeyHeaderBox", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var headerRt = headerGo.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0.5f, 0.5f);
            headerRt.anchorMax = new Vector2(0.5f, 0.5f);
            headerRt.anchoredPosition = new Vector2(0f, 335f);
            headerRt.sizeDelta = new Vector2(960f, 64f);

            var barGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barGo.transform.SetParent(headerGo.transform, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(0f, 0.5f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = new Vector2(0f, 0f);
            barRt.sizeDelta = new Vector2(8f, 44f);
            var barShape = barGo.GetComponent<UIInkShapeGraphic>();
            barShape.Shape = UIInkShape.Pill; // 圆角化：标题色标改胶囊
            barShape.BorderWidth = 0f;
            barShape.color = ComicStyle.Yellow;
            barShape.HardShadow = false;

            var title = CreateTextMesh("Title", headerGo.transform, "按键设置 / KEY BINDINGS", UITypography.FontSnapped(32f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: true);
            title.fontStyle = FontStyles.Bold;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(1f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(20f, 10f);
            titleRt.sizeDelta = new Vector2(-20f, 36f);

            var tip = CreateTextMesh("Tip", headerGo.transform, "点击按键卡片后按新键修改；按 ESC 取消；Backspace 清空备用键", UITypography.FontSnapped(18f), ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f), null, TextAlignmentOptions.Left, isDisplay: false);
            var tipRt = tip.rectTransform;
            tipRt.anchorMin = new Vector2(0f, 0.5f);
            tipRt.anchorMax = new Vector2(1f, 0.5f);
            tipRt.pivot = new Vector2(0f, 0.5f);
            tipRt.anchoredPosition = new Vector2(20f, -18f);
            tipRt.sizeDelta = new Vector2(-20f, 26f);

            // 2. 表头与按键列表容器
            var tableGo = new GameObject("KeyBindingsTable", typeof(RectTransform));
            tableGo.transform.SetParent(parent, false);
            var tableRt = tableGo.GetComponent<RectTransform>();
            tableRt.anchorMin = new Vector2(0.5f, 0.5f);
            tableRt.anchorMax = new Vector2(0.5f, 0.5f);
            tableRt.pivot = new Vector2(0.5f, 0.5f);
            tableRt.anchoredPosition = new Vector2(0f, 10f);
            tableRt.sizeDelta = new Vector2(960f, 520f);

            DesktopAction[] actions = (DesktopAction[])Enum.GetValues(typeof(DesktopAction));
            float rowHeight = 48f;
            float rowGap = 8f;
            float totalHeight = actions.Length * (rowHeight + rowGap);
            float topY = totalHeight * 0.5f - rowHeight * 0.5f;

            for (int i = 0; i < actions.Length; i++)
            {
                DesktopAction act = actions[i];
                float y = topY - i * (rowHeight + rowGap);

                var rowGo = new GameObject($"KeyRow_{act}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                rowGo.transform.SetParent(tableGo.transform, false);
                var rt = rowGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(960f, rowHeight);
                rt.anchoredPosition = new Vector2(0f, y);

                var shape = rowGo.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.RoundedRect; // 圆角化：键位行自适应圆角
                shape.BorderWidth = ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.InkSoft;
                shape.color = (i % 2 == 0) ? ComicStyle.Paper : ComicStyle.PaperAlt;
                shape.HardShadow = false;

                // 动作名称标签
                string actionName = DesktopKeyBindingService.GetActionDisplayName(act);
                var actLabel = CreateTextMesh("ActLabel", rowGo.transform, actionName, UITypography.FontSnapped(22f), ComicStyle.Ink, null, TextAlignmentOptions.Left, isDisplay: false);
                actLabel.fontStyle = FontStyles.Bold;
                var actRt = actLabel.rectTransform;
                actRt.anchorMin = new Vector2(0f, 0.5f);
                actRt.anchorMax = new Vector2(0f, 0.5f);
                actRt.pivot = new Vector2(0f, 0.5f);
                actRt.anchoredPosition = new Vector2(24f, 0f);
                actRt.sizeDelta = new Vector2(300f, 40f);

                // 主按键按钮 (180x38)
                var priBtn = CreateStyledButton($"PriBtn_{act}", rowGo.transform, "-", ComicStyle.Paper, ComicStyle.Ink, new Vector2(180f, 38f), _theme, UITypography.FontSnapped(20f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
                var priRt = priBtn.GetComponent<RectTransform>();
                priRt.anchorMin = new Vector2(1f, 0.5f);
                priRt.anchorMax = new Vector2(1f, 0.5f);
                priRt.pivot = new Vector2(1f, 0.5f);
                priRt.anchoredPosition = new Vector2(-220f, 0f);
                var priShape = priBtn.GetComponent<UIInkShapeGraphic>();
                var priText = priBtn.GetComponentInChildren<TextMeshProUGUI>();

                priBtn.onClick.AddListener(() =>
                {
                    StartKeyListening(act, false, priBtn, priShape, priText);
                });

                // 备用按键按钮 (180x38)
                var secBtn = CreateStyledButton($"SecBtn_{act}", rowGo.transform, "-", ComicStyle.Paper, ComicStyle.Ink, new Vector2(180f, 38f), _theme, UITypography.FontSnapped(20f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
                var secRt = secBtn.GetComponent<RectTransform>();
                secRt.anchorMin = new Vector2(1f, 0.5f);
                secRt.anchorMax = new Vector2(1f, 0.5f);
                secRt.pivot = new Vector2(1f, 0.5f);
                secRt.anchoredPosition = new Vector2(-24f, 0f);
                var secShape = secBtn.GetComponent<UIInkShapeGraphic>();
                var secText = secBtn.GetComponentInChildren<TextMeshProUGUI>();

                secBtn.onClick.AddListener(() =>
                {
                    StartKeyListening(act, true, secBtn, secShape, secText);
                });

                _keyBindingRows.Add(new KeyBindingRowUI
                {
                    action = act,
                    primaryBtn = priBtn,
                    primaryShape = priShape,
                    primaryText = priText,
                    secondaryBtn = secBtn,
                    secondaryShape = secShape,
                    secondaryText = secText
                });
            }

            // 3. 底部操作栏（恢复默认 + 返回）
            var bottomGo = new GameObject("BottomBar", typeof(RectTransform));
            bottomGo.transform.SetParent(parent, false);
            var bottomRt = bottomGo.GetComponent<RectTransform>();
            bottomRt.anchorMin = new Vector2(0.5f, 0.5f);
            bottomRt.anchorMax = new Vector2(0.5f, 0.5f);
            bottomRt.pivot = new Vector2(0.5f, 0.5f);
            bottomRt.anchoredPosition = new Vector2(0f, -345f);
            bottomRt.sizeDelta = new Vector2(960f, 56f);

            var resetBtn = CreateStyledButton("ResetKeyBtn", bottomGo.transform, "恢复默认", ComicStyle.Paper, ComicStyle.Ink, new Vector2(190f, 48f), _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0.5f, 0.5f);
            resetRt.anchorMax = new Vector2(0.5f, 0.5f);
            resetRt.pivot = new Vector2(0.5f, 0.5f);
            resetRt.anchoredPosition = new Vector2(-110f, 0f);
            resetBtn.onClick.AddListener(OnResetKeyBindingsClicked);

            var backBtn = CreateStyledButton("BackKeyBtn", bottomGo.transform, "返回", ComicStyle.Yellow, ComicStyle.Ink, new Vector2(190f, 48f), _theme, UITypography.FontSnapped(PauseSettingControlFont), ComicStyle.CornerSharp, ComicStyle.BorderThin);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.5f, 0.5f);
            backRt.anchorMax = new Vector2(0.5f, 0.5f);
            backRt.pivot = new Vector2(0.5f, 0.5f);
            backRt.anchoredPosition = new Vector2(110f, 0f);
            backBtn.onClick.AddListener(CloseKeyBindings);
        }

        private void StartKeyListening(DesktopAction action, bool isSecondary, Button btn, UIInkShapeGraphic shape, TextMeshProUGUI text)
        {
            if (_isListeningForKey)
            {
                CancelKeyListening();
            }

            _isListeningForKey = true;
            _listeningAction = action;
            _listeningIsSecondary = isSecondary;
            _listeningStartFrame = Time.frameCount;
            _activeListeningButton = btn;
            _activeListeningShape = shape;
            _activeListeningText = text;

            if (_activeListeningShape != null)
            {
                _activeListeningShape.color = ComicStyle.Yellow;
            }
            if (_activeListeningText != null)
            {
                _activeListeningText.text = "[ 请按键... ]";
                _activeListeningText.fontStyle = FontStyles.Bold;
            }
        }

        private void StopKeyListening()
        {
            _isListeningForKey = false;
            _activeListeningButton = null;
            _activeListeningShape = null;
            _activeListeningText = null;
        }

        private void CancelKeyListening()
        {
            StopKeyListening();
            RefreshKeyBindingsUI();
        }

        private void OnResetKeyBindingsClicked()
        {
            if (_isListeningForKey) CancelKeyListening();
            DesktopKeyBindingService.ResetToDefaults();
            RefreshKeyBindingsUI();
        }

        public void RefreshKeyBindingsUI()
        {
            if (_keyBindingRows == null) return;
            for (int i = 0; i < _keyBindingRows.Count; i++)
            {
                var row = _keyBindingRows[i];
                if (row == null) continue;

                DesktopKeyBindingService.GetBinding(row.action, out var primary, out var secondary);

                bool isPrimaryListening = _isListeningForKey && _listeningAction == row.action && !_listeningIsSecondary;
                bool isSecondaryListening = _isListeningForKey && _listeningAction == row.action && _listeningIsSecondary;

                if (!isPrimaryListening)
                {
                    if (row.primaryText != null)
                    {
                        row.primaryText.text = primary.GetDisplayName();
                        row.primaryText.fontStyle = FontStyles.Normal;
                    }
                    if (row.primaryShape != null)
                    {
                        row.primaryShape.color = ComicStyle.Paper;
                    }
                }

                if (!isSecondaryListening)
                {
                    if (row.secondaryText != null)
                    {
                        row.secondaryText.text = secondary.GetDisplayName();
                        row.secondaryText.fontStyle = FontStyles.Normal;
                    }
                    if (row.secondaryShape != null)
                    {
                        row.secondaryShape.color = ComicStyle.Paper;
                    }
                }
            }
        }

        private void HandleKeyListening()
        {
            if (Time.frameCount <= _listeningStartFrame) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // Esc: 取消改键
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                CancelKeyListening();
                return;
            }

            // Backspace 或 Delete: 清空备用键
            if (_listeningIsSecondary && kb != null && (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame))
            {
                DesktopKeyBindingService.ClearSecondary(_listeningAction);
                StopKeyListening();
                RefreshKeyBindingsUI();
                return;
            }

            // 检测鼠标按键
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    ApplyNewBinding(DesktopBindingSlot.FromMouse(0));
                    return;
                }
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    ApplyNewBinding(DesktopBindingSlot.FromMouse(1));
                    return;
                }
                if (mouse.middleButton.wasPressedThisFrame)
                {
                    ApplyNewBinding(DesktopBindingSlot.FromMouse(2));
                    return;
                }
                if (mouse.forwardButton.wasPressedThisFrame)
                {
                    ApplyNewBinding(DesktopBindingSlot.FromMouse(3));
                    return;
                }
                if (mouse.backButton.wasPressedThisFrame)
                {
                    ApplyNewBinding(DesktopBindingSlot.FromMouse(4));
                    return;
                }
            }

            // 检测键盘按键
            if (kb != null)
            {
                for (int i = 0; i < s_AllCandidateKeys.Length; i++)
                {
                    Key k = s_AllCandidateKeys[i];
                    if (k == Key.None || k == Key.Escape) continue;

                    var control = kb[k];
                    if (control != null && control.wasPressedThisFrame)
                    {
                        ApplyNewBinding(DesktopBindingSlot.FromKey(k));
                        return;
                    }
                }
            }
        }

        private void ApplyNewBinding(DesktopBindingSlot slot)
        {
            DesktopKeyBindingService.TryBind(_listeningAction, _listeningIsSecondary, slot, out _);
            StopKeyListening();
            RefreshKeyBindingsUI();
        }

        private void BuildFrameRateButtons(Transform parent)
        {
            var rowGo = new GameObject("FpsButtonsRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(1f, 0.5f);
            rowRt.anchorMax = new Vector2(1f, 0.5f);
            rowRt.pivot = new Vector2(1f, 0.5f);
            rowRt.anchoredPosition = new Vector2(-SettingsRowPadding, 0f);
            rowRt.sizeDelta = new Vector2(SettingsControlWidth, PauseSettingControlHeight);

            int[] rates = GameSettingsMath.AvailableFrameRates;
            _fpsButtons = new Button[rates.Length];
            _fpsShapes = new UIInkShapeGraphic[rates.Length];
            _fpsTexts = new TextMeshProUGUI[rates.Length];

            // 四段等宽段选：只显示 30 / 60 / 120 / 无限制，FPS 单位由左侧小注解承担，
            // 移动端 24 号字也不会在 89px 段宽里折行（旧版「120 FPS」会断成两行）。
            float spacing = 8f;
            float btnWidth = (SettingsControlWidth - spacing * (rates.Length - 1)) / rates.Length;
            float startX = -SettingsControlWidth * 0.5f + btnWidth * 0.5f;

            for (int i = 0; i < rates.Length; i++)
            {
                int fps = rates[i];
                string label = GetCompactFrameRateLabel(fps);

                var btn = CreateStyledButton(
                    $"FpsBtn_{fps}", rowGo.transform, label,
                    ComicStyle.Paper, ComicStyle.Ink, new Vector2(btnWidth, PauseSettingControlHeight),
                    _theme, UITypography.FontSnapped(18f), ComicStyle.CornerSharp, ComicStyle.BorderThin);
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

        private static string GetCompactFrameRateLabel(int fps)
        {
            switch (GameSettingsMath.NormalizeFrameRate(fps))
            {
                case GameSettingsMath.FrameRate30:
                    return "30";
                case GameSettingsMath.FrameRate60:
                    return "60";
                case GameSettingsMath.FrameRate120:
                    return "120";
                default:
                    return "无限制";
            }
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

        private void OnMusicSliderChanged(float val)
        {
            GameSettingsService.MusicVolume = val;
            UpdateMusicVolumeLabel(val);
        }

        private void UpdateMusicVolumeLabel(float val)
        {
            if (_musicValueText != null)
            {
                _musicValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            }
        }

        private void OnSfxSliderChanged(float val)
        {
            GameSettingsService.SfxVolume = val;
            UpdateSfxVolumeLabel(val);
        }

        private void UpdateSfxVolumeLabel(float val)
        {
            if (_sfxValueText != null)
            {
                _sfxValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
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
                // 数值列只有 ~100 UI px 宽；0 时直接写「关闭」，避免 32 号字把「0% (关闭)」挤成两行。
                _shakeValueText.text = val <= 0.001f ? "关闭" : $"{Mathf.RoundToInt(val * 100f)}%";
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
                // 灵敏度区间是 0.25x~2.5x，默认 1.0x 只落在轨道的 1/3 处；
                // 旧文案写「100%」会让人误以为滑条坏了，直接显示倍率更直观。
                label.text = $"{val:0.0}x";
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
                // 语义由左侧「视角垂直方向 · 反转 Y 轴」承担，按钮只保留 开启 / 关闭 状态。
                _invertYText.text = GameSettingsService.InvertLookY ? "开启" : "关闭";
                _invertYText.color = ComicStyle.Ink; // 纸白/纸黄底上统一墨字
            }
            if (_invertYShape != null)
            {
                _invertYShape.color = GameSettingsService.InvertLookY ? ComicStyle.Yellow : ComicStyle.Paper;
            }
        }

        private void OnShowFpsToggled()
        {
            GameSettingsService.ShowFps = !GameSettingsService.ShowFps;
            UpdateShowFpsLabel();
        }

        private void UpdateShowFpsLabel()
        {
            if (_showFpsText != null)
            {
                // 语义由左侧「显示帧率 · HUD 左上角」承担，按钮只保留 开启 / 关闭 状态。
                _showFpsText.text = GameSettingsService.ShowFps ? "开启" : "关闭";
                _showFpsText.color = ComicStyle.Ink;
            }
            if (_showFpsShape != null)
            {
                _showFpsShape.color = GameSettingsService.ShowFps ? ComicStyle.Yellow : ComicStyle.Paper;
            }
        }

        private void OnShowMinimapToggled()
        {
            GameSettingsService.ShowMinimap = !GameSettingsService.ShowMinimap;
            UpdateShowMinimapLabel();
        }

        private void UpdateShowMinimapLabel()
        {
            if (_showMinimapText != null)
            {
                _showMinimapText.text = GameSettingsService.ShowMinimap ? "开启" : "关闭";
                _showMinimapText.color = ComicStyle.Ink;
            }
            if (_showMinimapShape != null)
            {
                _showMinimapShape.color = GameSettingsService.ShowMinimap ? ComicStyle.Yellow : ComicStyle.Paper;
            }
        }

        private void OnResetDefaultsClicked()
        {
            GameSettingsService.ResetToDefaults();
            if (_volumeSlider != null) _volumeSlider.value = GameSettingsService.MasterVolume;
            if (_musicSlider != null) _musicSlider.value = GameSettingsService.MusicVolume;
            if (_sfxSlider != null) _sfxSlider.value = GameSettingsService.SfxVolume;
            if (_shakeSlider != null) _shakeSlider.value = GameSettingsService.ShakeScale;
            if (_mouseSensSlider != null) _mouseSensSlider.value = GameSettingsService.MouseLookSensitivity;
            if (_touchSensSlider != null) _touchSensSlider.value = GameSettingsService.TouchLookSensitivity;
            UpdateVolumeLabel(GameSettingsService.MasterVolume);
            UpdateMusicVolumeLabel(GameSettingsService.MusicVolume);
            UpdateSfxVolumeLabel(GameSettingsService.SfxVolume);
            UpdateShakeLabel(GameSettingsService.ShakeScale);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);
            UpdateInvertYLabel();
            UpdateShowFpsLabel();
            UpdateShowMinimapLabel();
            UpdateFrameRateButtons();
        }

        /// <summary>
        /// 点面板外空白返回（触屏上比找「返回」按钮更顺手）：
        /// 按键监听中 → 取消监听；按键页 → 回设置页；设置页 → 回暂停主视图（首页模式关闭弹窗）；
        /// 主视图不响应，避免误触关掉暂停菜单。
        /// </summary>
        private void OnBackdropClicked()
        {
            if (_isListeningForKey)
            {
                CancelKeyListening();
                return;
            }

            if (_isKeyBindingsOpen)
            {
                CloseKeyBindings();
                return;
            }

            if (_isSettingsOpen)
            {
                CloseSettings(); // 内部已处理首页设置模式（走 CloseSettingsFromHome）
            }
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
                // 本帧 Esc / Start 的归属由 MenuInputRouter 统一解析（纯函数，与 Update 顺序无关）：
                // ResolveEscOwner 定归属 → CanLayerHandle 判「该不该我处理」（非归属层直接让路，
                // 绝不抢占帧位，否则归属层会收不到这个 Esc）→ TryConsumeEsc 抢帧位 → ActionForOwner 执行。
                MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                    homeInfoOpen: HomeInfoOverlay.IsOpen,
                    layoutEditing: MobileLayoutCustomizer.IsEditingAny,
                    pauseExpanded: IsMenuExpanded,
                    drawerExpanded: WeaponSelectView.Instance != null && WeaponSelectView.Instance.IsDrawerOpen,
                    mapSelectOpen: MapSelectOverlay.IsOpen,
                    lockerOpen: LockerOverlay.IsOpen);

                if (!MenuInputRouter.CanLayerHandle(MenuEscLayer.PauseMenu, owner))
                {
                    return; // 归属不是暂停/设置/浮层：交给归属者（例如已展开的武器抽屉自己收）
                }

                if (!MenuInputRouter.TryConsumeEsc(MenuEscLayer.PauseMenu))
                {
                    return; // 本帧已被别的菜单层消费
                }

                switch (MenuInputRouter.ActionForOwner(owner))
                {
                    case MenuEscAction.CloseHomeInfo:
                        HomeInfoOverlay.TryCloseFromMenuInput();
                        return;
                    case MenuEscAction.CloseLayoutEditor:
                        MobileLayoutCustomizer.ActiveEditor?.Close(false);
                        return;
                    case MenuEscAction.CloseMapSelect:
                        MapSelectOverlay.TryCloseFromMenuInput();
                        return;
                    case MenuEscAction.CloseLocker:
                        LockerOverlay.TryCloseFromMenuInput();
                        return;
                    case MenuEscAction.CloseDrawer:
                        // 归属为抽屉时由这里执行收起（与 WeaponSelectView 共用同一动作映射，
                        // 谁先 Update 都只有一个动作；旧接线先 consume 再 return 会让 Esc 在抽屉上失效）
                        var drawer = WeaponSelectView.Instance;
                        if (drawer != null) drawer.SetDrawerOpen(false);
                        return;
                    default:
                        TogglePause();
                        return;
                }
            }
        }

        /// <summary>
        /// 暂停菜单或设置页是否处于展开态（供 <see cref="MenuInputRouter"/> 仲裁 Esc 归属）。
        /// </summary>
        public bool IsMenuExpanded => IsPaused || _isSettingsOpen || _isKeyBindingsOpen;

        /// <summary>静态快捷入口：暂停菜单 / 设置页是否展开（无实例时为 false）。</summary>
        public static bool IsAnyMenuExpanded => Instance != null && Instance.IsMenuExpanded;

        public void TogglePause()
        {
            if (!IsPaused && MobileLayoutCustomizer.IsEditingAny)
            {
                MobileLayoutCustomizer.ActiveEditor?.Close(false);
                return;
            }

            if (_isKeyBindingsOpen)
            {
                CloseKeyBindings();
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
            else if (_homeSettingsMode)
            {
                // 首页设置模式：Esc / Start 直接收起设置弹窗（IsMenuExpanded 对输入仲裁仍报展开态）
                CloseSettingsFromHome();
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

            // 暂停音必须在 AudioListener.pause = true 之前播，否则它自己也被静音
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayPause(true);

            Time.timeScale = 0f;
            AudioListener.pause = true;

            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = false;
            }

            _modalRoot.SetActive(true);
            ShowMainView();
            ApplyResponsivePanelScale();
            PrepareModalForOpen();
            if (_panelMotion != null)
            {
                _panelMotion.PlayIn();
            }

            UpdateMobileButtonVisibility();
            PauseStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 恢复对局并收起暂停弹窗。<paramref name="immediate"/> = true 时跳过退场转场直接隐藏
        /// （拆除期、重开/返菜单、阶段切换等「紧接着换屏」的路径用它，避免转场与目标界面重叠）。
        /// 默认 false：Esc / 继续游戏这类玩家主动恢复走淡出转场。
        /// </summary>
        public void Resume(bool immediate = false)
        {
            if (!IsPaused)
                return;

            IsPaused = false;

            AudioListener.pause = false;
            HitstopService.IsPaused = false;
            HitstopService.Cancel();

            // 恢复音在解除静音之后播
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayPause(false);

            Time.timeScale = 1f;

            if (PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.GameplayInputEnabled = true;
            }

            if (immediate) HideModalNow();
            else CloseModalWithTransition();
            _isSettingsOpen = false;
            _isKeyBindingsOpen = false;
            if (_isListeningForKey) CancelKeyListening();
            if (_keyBindingsViewContainer != null) _keyBindingsViewContainer.SetActive(false);

            UpdateMobileButtonVisibility();
            PauseStateChanged?.Invoke(false);
        }

        public void RestartMatch()
        {
            // 紧接着开赛（墨幕入场）：直接隐藏弹窗，不与开赛演出抢画面。
            Resume(true);
            MatchManager match = _match != null ? _match : MatchManager.Instance;
            if (match != null)
            {
                match.RestartMatch();
            }
        }

        /// <summary>关闭弹窗并回到赛前主菜单（Ready 阶段）；找不到比赛或已在主菜单时只关闭弹窗。</summary>
        public void ReturnToMainMenu()
        {
            // 紧接着回首页（首页自带入场动画）：直接隐藏弹窗，避免两层动画叠加。
            Resume(true);
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

            // 每次打开都从 GameSettingsService 全量回读：设置可能被别处（重置/其他面板）改过，
            // 旧版只回读了音量/震屏/帧率，灵敏度和反转 Y 会显示陈旧值。
            if (_volumeSlider != null) _volumeSlider.value = GameSettingsService.MasterVolume;
            if (_musicSlider != null) _musicSlider.value = GameSettingsService.MusicVolume;
            if (_sfxSlider != null) _sfxSlider.value = GameSettingsService.SfxVolume;
            if (_shakeSlider != null) _shakeSlider.value = GameSettingsService.ShakeScale;
            if (_mouseSensSlider != null) _mouseSensSlider.value = GameSettingsService.MouseLookSensitivity;
            if (_touchSensSlider != null) _touchSensSlider.value = GameSettingsService.TouchLookSensitivity;
            UpdateVolumeLabel(GameSettingsService.MasterVolume);
            UpdateMusicVolumeLabel(GameSettingsService.MusicVolume);
            UpdateSfxVolumeLabel(GameSettingsService.SfxVolume);
            UpdateShakeLabel(GameSettingsService.ShakeScale);
            UpdateLookSensitivityLabel(_mouseSensValueText, GameSettingsService.MouseLookSensitivity);
            UpdateLookSensitivityLabel(_touchSensValueText, GameSettingsService.TouchLookSensitivity);
            UpdateInvertYLabel();
            UpdateShowFpsLabel();
            UpdateShowMinimapLabel();
            UpdateFrameRateButtons();
            SetSettingsTab(_activeSettingsTab); // 恢复上次浏览的板块，并确保容器显隐与 Tab 选中态一致

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        public void CloseSettings()
        {
            if (_homeSettingsMode)
            {
                // 首页设置模式没有「主视图」（继续游戏等对局按钮），返回键 = 整个弹窗收起
                CloseSettingsFromHome();
                return;
            }

            _isSettingsOpen = false;
            _settingsViewContainer.SetActive(false);
            _mainViewContainer.SetActive(true);
            UpdateReturnMenuButtonVisibility();

            if (_panelMotion != null)
            {
                _panelMotion.Punch(0.04f);
            }
        }

        /// <summary>
        /// 首页「设置」卡入口：直接挂出设置视图，不带对局暂停语义
        /// （IsPaused 保持 false，timeScale / AudioListener / GameplayInputEnabled / PauseStateChanged 均不动，
        /// 首页 BGM 不受影响）。收起走 <see cref="CloseSettingsFromHome"/>；Esc 由
        /// <see cref="TogglePause"/> 的首页分支处理。
        /// </summary>
        public void OpenSettingsFromHome()
        {
            if (_homeSettingsMode || MobileLayoutCustomizer.IsEditingAny)
                return;

            _homeSettingsMode = true;
            _modalRoot.SetActive(true);
            ApplyResponsivePanelScale();
            OpenSettings(); // 复位主视图 → 激活设置视图 → 回读 GameSettingsService 刷滑杆

            PrepareModalForOpen();
            if (_panelMotion != null)
            {
                _panelMotion.PlayIn();
            }
        }

        /// <summary>
        /// 收起首页设置弹窗（对局暂停语义零副作用；未处于首页模式时为空操作）。
        /// <paramref name="immediate"/> = true 用于拆除期（OnDisable/OnDestroy）直接隐藏。
        /// </summary>
        public void CloseSettingsFromHome(bool immediate = false)
        {
            if (!_homeSettingsMode)
                return;

            _homeSettingsMode = false;
            _isSettingsOpen = false;
            _isKeyBindingsOpen = false;
            if (_isListeningForKey) CancelKeyListening();
            if (_keyBindingsViewContainer != null) _keyBindingsViewContainer.SetActive(false);
            // OnDisable/OnDestroy 拆除期调用时容器可能已销毁，做判空
            if (_settingsViewContainer != null) _settingsViewContainer.SetActive(false);
            if (immediate) HideModalNow();
            else CloseModalWithTransition();
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

        // ==================== 退场转场 ====================

        /// <summary>
        /// 打开弹窗前复位退场状态：清防重入标记、退订退场回调、把整层 alpha / 射线恢复。
        /// 必须做——否则上一次没跑完的 PlayOut 会在重开时立刻把弹窗又淡下去（残留 0.9 倍 / alpha 0）。
        /// </summary>
        private void PrepareModalForOpen()
        {
            _closingModal = false;
            _closingModalElapsed = 0f;
            if (_modalGroup != null)
            {
                _modalGroup.alpha = 1f;
                _modalGroup.blocksRaycasts = true;
                _modalGroup.interactable = true;
            }
            // 彻底退订，避免 OnPlayedOut 累积多个处理器。
            if (_panelMotion != null) _panelMotion.OnPlayedOut -= HandleModalClosed;
        }

        /// <summary>
        /// 退场：先播面板 PlayOut，收敛后再隐藏 _modalRoot。
        /// _closingModal 防重入——Resume / 关闭按钮 / Esc / OnDisable 可能在同一帧或连续两帧触发，
        /// 没有标记就会叠加两次 PlayOut 并重复订阅 OnPlayedOut，甚至把刚打开的弹窗又藏回去。
        /// </summary>
        private void CloseModalWithTransition()
        {
            if (_modalRoot == null) return;
            if (_closingModal) return;

            _closingModal = true;
            _closingModalElapsed = 0f;

            // 退场期间不再接收点击：遮罩与按钮都还在画，但不该再被点到（CanvasGroup 直接挡射线）。
            if (_modalGroup != null)
            {
                _modalGroup.blocksRaycasts = false;
                _modalGroup.interactable = false;
            }

            if (_panelMotion != null && _modalRoot.activeInHierarchy)
            {
                _panelMotion.OnPlayedOut -= HandleModalClosed; // 防重复订阅
                _panelMotion.OnPlayedOut += HandleModalClosed;
                _panelMotion.PlayOut();
            }
            else
            {
                // 面板 motion 缺失或已失活：退不了场就退回硬切。
                HandleModalClosed();
            }
        }

        /// <summary>退场推进：整层淡出（遮罩与面板同节奏），并做兜底超时。</summary>
        private void TickModalClosing()
        {
            _closingModalElapsed += Time.unscaledDeltaTime;

            if (_modalGroup != null)
            {
                // 与面板自身的 alpha 淡出用同一时长（UISpringMotion 的透明度走 entranceFadeDuration），
                // 否则遮罩会比面板慢半拍地留在屏幕上。
                UIThemeSO settings = UIThemeSO.Resolve(_theme);
                float duration = Mathf.Clamp(settings.entranceFadeDuration, 0.05f, 0.3f);
                _modalGroup.alpha = 1f - Mathf.Clamp01(_closingModalElapsed / duration);
            }

            // 兜底：面板 motion 若中途被禁用/失活，OnPlayedOut 永远不来，_closingModal 会卡住、
            // 弹窗以「alpha 0 但还激活」的形态留在屏幕上。超时强制收尾（正常动画 ≤0.3s）。
            if (_closingModalElapsed >= 1f)
            {
                HandleModalClosed();
            }
        }

        /// <summary>退场收敛：隐藏弹窗并复位整层状态（可被 OnPlayedOut 与超时兜底重复调用）。</summary>
        private void HandleModalClosed()
        {
            if (!_closingModal) return;

            if (_panelMotion != null) _panelMotion.OnPlayedOut -= HandleModalClosed;
            _closingModal = false;
            _closingModalElapsed = 0f;

            if (_modalGroup != null)
            {
                _modalGroup.alpha = 1f;
                _modalGroup.blocksRaycasts = true;
                _modalGroup.interactable = true;
            }

            if (_modalRoot != null) _modalRoot.SetActive(false);
        }

        /// <summary>跳过转场直接隐藏弹窗（拆除期 / 紧接着换屏的路径）。</summary>
        private void HideModalNow()
        {
            if (_panelMotion != null) _panelMotion.OnPlayedOut -= HandleModalClosed;
            _closingModal = false;
            _closingModalElapsed = 0f;

            if (_modalGroup != null)
            {
                _modalGroup.alpha = 1f;
                _modalGroup.blocksRaycasts = true;
                _modalGroup.interactable = true;
            }

            if (_modalRoot != null) _modalRoot.SetActive(false);
        }

        private void OnMatchPhaseChanged(MatchManager.Phase phase)
        {
            if (phase != MatchManager.Phase.Playing)
            {
                if (IsPaused)
                {
                    // 阶段切换紧接着换屏（结算 / 入场）：直接隐藏，不播退场转场。
                    Resume(true);
                }
            }

            UpdateMobileButtonVisibility();
        }

        private void UpdateMobileButtonVisibility()
        {
            if (_mobilePauseButton == null)
                return;

            bool isPlaying = _match != null && _match.CurrentPhase == MatchManager.Phase.Playing;
            bool isMobile = Application.isMobilePlatform || forceShowMobileButton || MobileTouchUI.IsTouchSimulationActive;
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
            // 缩放写父级容器：面板自身 localScale 归 UISpringMotion 的入场动画独占，
            // 两者同层会互相覆盖（入场结束把矮屏缩放冲回 1，面板底部按钮出屏）。
            if (_panelScaleRect == null) return;

            // 画布高度必须取 CanvasScaler 生效后的实际值：20:9 手机横屏下参考 1080 会被压到 ~966，
            // 硬编码 1080 会算出偏大的可用高度，面板底部按钮被挤出屏幕（"点不到返回"的根因）。
            Canvas.ForceUpdateCanvases();
            float canvasHeight = 1080f;
            if (transform is RectTransform rootRt && rootRt.rect.height > 100f)
            {
                canvasHeight = rootRt.rect.height;
            }

            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 860f;

            if (availableHeight < targetHeight && availableHeight > 100f)
            {
                float s = Mathf.Clamp(availableHeight / targetHeight, 0.5f, 1f);
                _panelScaleRect.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                _panelScaleRect.localScale = Vector3.one;
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
            // 设置页/暂停页所有文案都按版面表留足宽度，禁止 TMP 自动折行（否则移动端字号放大后
            // 「屏幕震动」「120 FPS」会在中途断行，看起来像排版错乱）。
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

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
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：按钮圆角矩形（radius 0 = 自适应，999 = 胶囊）
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
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
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
            containerRt.sizeDelta = new Vector2(SettingsControlWidth, PauseSettingControlHeight); // 可触高度 ≥64

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(container.transform, false);
            var sliderRt = sliderGo.GetComponent<RectTransform>();
            sliderRt.anchorMin = new Vector2(0f, 0.5f);
            sliderRt.anchorMax = new Vector2(0.72f, 0.5f);
            sliderRt.pivot = new Vector2(0f, 0.5f);
            sliderRt.sizeDelta = new Vector2(0f, PauseSettingControlHeight); // 拖动热区比可见轨道高，触控更好抓

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            // 1. 直角滑轨槽（纸面次级色底 + 3px 墨框）：26 高，避免旧版 30 高的笨重感
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(sliderGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, SettingsSliderTrackHeight);
            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.Pill; // 圆角化：滑轨槽胶囊化
            bgShape.BorderWidth = ComicStyle.BorderThin;
            bgShape.BorderColor = ComicStyle.Ink;
            bgShape.color = ComicStyle.PaperAlt;
            bgShape.HardShadow = false;
            bgShape.Halftone = false;

            // 2. 填充区域 Fill（直角黄色纯色块）：四周内缩 3px，给墨框留出可见边线
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            // 填充区必须与 26 高的轨道同高、再从四周内缩 3px；旧代码把填充区钉在 64 高的
            // 拖动热区上（或 y 点锚 + offset=0 压成 0 高），黄色进度要么撑满整行、要么完全不画。
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(-6f, SettingsSliderTrackHeight - 6f);
            fillAreaRt.anchoredPosition = Vector2.zero;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;
            var fillShape = fillGo.GetComponent<UIInkShapeGraphic>();
            fillShape.Shape = UIInkShape.Pill; // 圆角化：滑轨填充胶囊化（宽度随值变化仍保持全圆端头）
            fillShape.BorderWidth = 0f;
            fillShape.color = ComicStyle.Yellow;
            fillShape.HardShadow = false;
            fillShape.Halftone = false;
            slider.fillRect = fillRt;

            // 3. 小圆形手柄：26×26 纸白正圆 + 3px 墨边（旧版 44×44 方块像一块砖，
            //    用户反馈「还是很大」后改为与滑轨同高的小圆点；整个 64 高滑条仍是拖动热区）。
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            float handleInset = SettingsSliderHandleSize * 0.5f;
            handleAreaRt.offsetMin = new Vector2(handleInset, 0f);
            handleAreaRt.offsetMax = new Vector2(-handleInset, 0f);

            // Slider 会把 handleRect 的锚点改成「x=当前值、y 拉满」，直接把手柄给它会被拉成
            // 贯穿整条轨道的竖条（历史问题：44×44 实际渲染成 ~34×64）。这里用零宽槽接管锚点，
            // 真正的圆形手柄作为它的居中子物体，保持 26×26 不被拉伸。
            var slotGo = new GameObject("HandleSlot", typeof(RectTransform));
            slotGo.transform.SetParent(handleArea.transform, false);
            var slotRt = slotGo.GetComponent<RectTransform>();
            slotRt.pivot = new Vector2(0.5f, 0.5f);
            slotRt.sizeDelta = new Vector2(SettingsSliderHandleSize, 0f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            handleGo.transform.SetParent(slotGo.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.anchorMin = handleRt.anchorMax = new Vector2(0.5f, 0.5f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(SettingsSliderHandleSize, SettingsSliderHandleSize);
            var handleShape = handleGo.GetComponent<UIInkShapeGraphic>();
            handleShape.Shape = UIInkShape.Pill; // 等宽等高 = 正圆
            handleShape.CornerRadius = ComicStyle.CornerBubble;
            handleShape.BorderWidth = ComicStyle.BorderThin;
            handleShape.BorderColor = ComicStyle.Ink;
            handleShape.color = ComicStyle.Paper;
            handleShape.Halftone = false;
            UIThemeSO.Resolve(theme).ApplyHardShadow(handleShape, UIHardShadowLevel.Tag);

            slider.handleRect = slotRt;
            slider.targetGraphic = handleShape;

            // 4. 右侧数值标签（24 号粗体墨字，右对齐；单行不折行）
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
            var valGo = new GameObject("ValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
            valGo.transform.SetParent(container.transform, false);
            var valRt = valGo.GetComponent<RectTransform>();
            valRt.anchorMin = new Vector2(0.74f, 0f);
            valRt.anchorMax = new Vector2(1f, 1f);
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;

            valueLabel = valGo.GetComponent<TextMeshProUGUI>();
            valueLabel.fontSize = UITypography.FontSnapped(PauseSettingControlFont);
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.color = ComicStyle.Ink;
            valueLabel.alignment = TextAlignmentOptions.Right;
            valueLabel.raycastTarget = false;
            valueLabel.textWrappingMode = TextWrappingModes.NoWrap;
            valueLabel.overflowMode = TextOverflowModes.Overflow;
            valueLabel.font = resolvedTheme.ResolveFont(false, "100%");

            return slider;
        }
    }
}
