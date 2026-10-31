using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 战前武器选择面板总视图（P14 几何大胆风 Geometric Bold · P14-I 移动端重排）：
    /// - 独立 Canvas，CanvasScaler 1920×1080 / match 0.5 / 支持 Safe Area / sortingOrder 15；
    /// - 抽屉宽度：桌面 620px；移动端 82% 参考宽度（约 1574px），左滑出、能放下双列大卡；
    /// - 移动端卡片走 2 列网格（每张 ~759×240，圆形武器色盘 132px、中文名 38px）；
    /// - 桌面保留单列列表（卡片 592×164）；
    /// - 抽屉收起时显示左下角「武器整备」整备卡：当前武器圆形色盘 + 名称 + 黄色箭头块，
    ///   取代过去那个 176×72 的纯黄方块；
    /// - 保留 7 键切换、Enter 确认、Esc 收放（桌面）；触摸点按卡片选中。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class WeaponSelectView : MonoBehaviour
    {
        public static WeaponSelectView Instance { get; private set; }

        [SerializeField] private UIThemeSO theme;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private RectTransform _safeAreaRoot;
        private GameObject _backdropGo;
        private RectTransform _drawerRoot;
        private UIInkShapeGraphic _drawerBg;

        private RectTransform _contentRoot;
        private bool _mobile;
        private WeaponDrawerMetrics _metrics;
        private LoadoutCardMetrics _loadoutMetrics;

        private RectTransform _toggleButtonRoot;
        private Button _toggleButton;
        private UIInkShapeGraphic _toggleButtonBg;
        private TextMeshProUGUI _toggleButtonText;
        private TextMeshProUGUI _toggleWeaponText;
        private Image _toggleIconImage;
        private UIInkShapeGraphic _toggleIconPlate;
        private UISpringMotion _toggleMotion;

        private TextMeshProUGUI _headerTitle;
        private Button _closeButton;

        private readonly List<WeaponCardView> _cards = new List<WeaponCardView>();
        private WeaponConfigSO[] _configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
        private Sprite[] _icons = new Sprite[WeaponSelectMath.WeaponCount];

        private int _selectedIndex;
        private Team _currentTeam = Team.TeamA;
        private bool _isDrawerOpen = true;
        private float _currentDrawerX = 24f;
        private float _targetDrawerX = 24f;
        private float _drawerWidth = 620f;
        private bool _built;
        private Rect _lastSafeArea;

        public event Action<int, WeaponConfigSO> OnWeaponSelected;

        public int SelectedIndex => _selectedIndex;
        public WeaponConfigSO SelectedConfig => _configs != null && _selectedIndex >= 0 && _selectedIndex < _configs.Length
            ? _configs[_selectedIndex]
            : null;
        public bool IsDrawerOpen => _isDrawerOpen;

        /// <summary>本视图是否按移动端度量排版（供测试/诊断）。</summary>
        public bool IsMobileLayout => _mobile;

        /// <summary>抽屉当前宽度（UI 单位；供测试断言移动端大抽屉）。</summary>
        public float DrawerWidth => _drawerWidth;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            theme = UIThemeSO.Resolve(theme);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Configure(UIThemeSO themeAsset, WeaponConfigSO[] configs, Sprite[] icons, int initialIndex = 0)
        {
            theme = UIThemeSO.Resolve(themeAsset);

            if (configs != null && configs.Length == WeaponSelectMath.WeaponCount)
            {
                _configs = configs;
            }
            if (icons != null && icons.Length == WeaponSelectMath.WeaponCount)
            {
                _icons = icons;
            }

            _mobile = UITypography.IsMobileLike;
            _metrics = MenuLayoutMath.Drawer(_mobile);
            _loadoutMetrics = MenuLayoutMath.LoadoutCard(_mobile);

            BuildCanvasAndHierarchy();
            PopulateCards();

            int clamped = WeaponSelectMath.ClampIndex(initialIndex);
            SelectWeapon(clamped, silent: true);

            SetDrawerOpen(false);
        }

        private void BuildCanvasAndHierarchy()
        {
            if (_built) return;
            _built = true;

            _drawerWidth = _metrics.Width;

            // 1. Canvas 基础配置
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 15;

            _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = GetComponent<GraphicRaycaster>();
            if (_raycaster == null) _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            // 1.5 全屏遮罩（抽屉打开时点外部收起）
            _backdropGo = new GameObject("DrawerBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            _backdropGo.transform.SetParent(transform, false);
            _backdropGo.transform.SetAsFirstSibling();
            var backdropRt = _backdropGo.GetComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;

            var backdropImg = _backdropGo.GetComponent<Image>();
            backdropImg.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.28f);
            backdropImg.raycastTarget = true;

            var backdropBtn = _backdropGo.GetComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(() =>
            {
                if (_isDrawerOpen)
                {
                    SetDrawerOpen(false);
                }
            });
            _backdropGo.SetActive(_isDrawerOpen);

            // 2. Safe Area 容器
            var safeGo = new GameObject("SafeAreaContainer", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);
            _safeAreaRoot = safeGo.GetComponent<RectTransform>();
            _safeAreaRoot.anchorMin = Vector2.zero;
            _safeAreaRoot.anchorMax = Vector2.one;
            _safeAreaRoot.sizeDelta = Vector2.zero;
            ApplySafeArea();

            // 3. 左侧直角抽屉容器
            var drawerGo = new GameObject("DrawerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            drawerGo.transform.SetParent(_safeAreaRoot, false);
            _drawerRoot = drawerGo.GetComponent<RectTransform>();
            _drawerRoot.anchorMin = new Vector2(0f, 0f);
            _drawerRoot.anchorMax = new Vector2(0f, 1f);
            _drawerRoot.pivot = new Vector2(0f, 0.5f);
            _drawerRoot.sizeDelta = new Vector2(_drawerWidth, -32f);
            _drawerRoot.anchoredPosition = new Vector2(24f, 0f);

            _drawerBg = drawerGo.GetComponent<UIInkShapeGraphic>();
            _drawerBg.Shape = UIInkShape.SharpRect;
            _drawerBg.CornerRadius = ComicStyle.CornerSharp;
            _drawerBg.BorderWidth = ComicStyle.BorderHeavy;
            _drawerBg.BorderColor = ComicStyle.Ink;
            _drawerBg.color = ComicStyle.Paper;
            _drawerBg.HardShadow = false;
            _drawerBg.Halftone = false;
            _drawerBg.GlossAlpha = 0f;
            _drawerBg.DrawGloss = false;
            _drawerBg.raycastTarget = true;

            // 4. Header：墨底标题条 + 黄色收起按钮
            BuildHeader();

            // 5. 卡片列表容器
            BuildScrollContent();

            // 6. 底部提示行
            var footerGo = new GameObject("Footer", typeof(RectTransform), typeof(TextMeshProUGUI));
            footerGo.transform.SetParent(_drawerRoot, false);
            var footerRect = footerGo.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 12f);
            footerRect.sizeDelta = new Vector2(-(_metrics.ScrollLeftInset + _metrics.ScrollRightInset), Mathf.Max(26f, _metrics.FooterFont * 1.5f));

            var footerText = footerGo.GetComponent<TextMeshProUGUI>();
            string footerLabel = _mobile
                ? "点卡片选择 · 点「收起」返回首页"
                : "Enter 确认 · 1~7/上下键 切换 · Esc 收起抽屉";
            footerText.font = theme.ResolveFont(false, footerLabel);
            footerText.alignment = TextAlignmentOptions.Center;
            footerText.fontSize = UITypography.FontSnapped(_metrics.FooterFont);
            footerText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            footerText.text = footerLabel;
            footerText.raycastTarget = false;

            // 7. 抽屉开关（左下角整备卡）
            BuildDrawerToggleButton();
        }

        private void BuildHeader()
        {
            float headerH = _metrics.HeaderHeight;
            float inset = _mobile ? 20f : 14f;

            var headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(_drawerRoot, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -inset);
            headerRect.sizeDelta = new Vector2(-inset * 2f, headerH);

            float closeW = _metrics.CloseSize.x;
            float closeH = _metrics.CloseSize.y;
            float bubbleH = _mobile ? 76f : 52f;

            // 4.1 收起按钮（黄色直角块，右上角）
            var closeGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            closeGo.transform.SetParent(headerGo.transform, false);
            var closeRect = closeGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = new Vector2(closeW, closeH);

            var closeBg = closeGo.GetComponent<UIInkShapeGraphic>();
            closeBg.Shape = UIInkShape.SharpRect;
            closeBg.CornerRadius = ComicStyle.CornerSharp;
            closeBg.BorderWidth = ComicStyle.BorderThin;
            closeBg.BorderColor = ComicStyle.Ink;
            closeBg.color = ComicStyle.Yellow;
            closeBg.HardShadow = false;

            var closeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            closeTextGo.transform.SetParent(closeGo.transform, false);
            var ctr = closeTextGo.GetComponent<RectTransform>();
            ctr.anchorMin = Vector2.zero;
            ctr.anchorMax = Vector2.one;
            ctr.sizeDelta = Vector2.zero;
            var closeButtonText = closeTextGo.GetComponent<TextMeshProUGUI>();
            closeButtonText.font = theme.ResolveFont(false, "收起");
            closeButtonText.alignment = TextAlignmentOptions.Center;
            closeButtonText.fontSize = UITypography.FontSnapped(_metrics.CloseFont);
            closeButtonText.fontStyle = FontStyles.Bold;
            closeButtonText.color = ComicStyle.Ink;
            closeButtonText.text = "收起";
            closeButtonText.raycastTarget = false;

            _closeButton = closeGo.GetComponent<Button>();
            _closeButton.targetGraphic = closeBg;
            _closeButton.transition = Selectable.Transition.None;
            _closeButton.onClick.AddListener(() => SetDrawerOpen(false));

            var closeJelly = closeGo.GetComponent<UIJellyButton>();
            closeJelly.Configure(theme, _closeButton, closeBg);

            // 4.2 标题条（墨底白字直角块，保留 TitleBubble/Title 测试契约路径）
            var bubbleGo = new GameObject("TitleBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bubbleGo.transform.SetParent(headerGo.transform, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchorMin = new Vector2(0f, 1f);
            bubbleRt.anchorMax = new Vector2(0f, 1f);
            bubbleRt.pivot = new Vector2(0f, 1f);
            bubbleRt.anchoredPosition = Vector2.zero;
            bubbleRt.sizeDelta = new Vector2(_drawerWidth - inset * 2f - closeW - 12f, bubbleH);

            var bubbleShape = bubbleGo.GetComponent<UIInkShapeGraphic>();
            bubbleShape.Shape = UIInkShape.SharpRect;
            bubbleShape.CornerRadius = ComicStyle.CornerSharp;
            bubbleShape.BorderWidth = 0f;
            bubbleShape.color = ComicStyle.Ink;
            bubbleShape.HardShadow = false;
            bubbleShape.SpeechTail = false;
            bubbleShape.Halftone = false;

            // 左侧黄色小方块作标题锚点（几何构图，不用阴影）
            var accentGo = new GameObject("TitleAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            accentGo.transform.SetParent(bubbleGo.transform, false);
            var accentRect = accentGo.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0.5f);
            accentRect.anchorMax = new Vector2(0f, 0.5f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = new Vector2(_mobile ? 18f : 14f, 0f);
            accentRect.sizeDelta = new Vector2(_mobile ? 14f : 10f, bubbleH * 0.56f);
            var accentShape = accentGo.GetComponent<UIInkShapeGraphic>();
            accentShape.Shape = UIInkShape.SharpRect;
            accentShape.CornerRadius = ComicStyle.CornerSharp;
            accentShape.BorderWidth = 0f;
            accentShape.color = ComicStyle.Yellow;
            accentShape.raycastTarget = false;

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(bubbleGo.transform, false);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(_mobile ? 48f : 36f, 0f);
            titleRect.offsetMax = new Vector2(-16f, 0f);
            _headerTitle = titleGo.GetComponent<TextMeshProUGUI>();
            _headerTitle.font = theme.ResolveFont(true, "武器整备 · ARSENAL");
            _headerTitle.fontSize = UITypography.FontSnapped(_metrics.HeaderTitleFont);
            _headerTitle.fontStyle = FontStyles.Bold;
            _headerTitle.alignment = TextAlignmentOptions.MidlineLeft;
            _headerTitle.color = ComicStyle.Paper;
            _headerTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _headerTitle.overflowMode = TextOverflowModes.Overflow;
            _headerTitle.text = "武器整备 <size=72%><color=#FFD400>·</color> ARSENAL</size>";
            _headerTitle.raycastTarget = false;

            // 4.3 副标题（标题条下方一行）
            var subGo = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            subGo.transform.SetParent(headerGo.transform, false);
            var subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 1f);
            subRect.anchorMax = new Vector2(0f, 1f);
            subRect.pivot = new Vector2(0f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -(bubbleH + 10f));
            subRect.sizeDelta = new Vector2(_drawerWidth - inset * 2f - closeW - 12f, _mobile ? 34f : 24f);
            var headerSubtitle = subGo.GetComponent<TextMeshProUGUI>();
            string subtitleText = _mobile
                ? "选择本局出战武器 · 点卡片切换"
                : "选择本局出战武器 · 按 1~7 快速切换";
            headerSubtitle.font = theme.ResolveFont(false, subtitleText);
            headerSubtitle.fontSize = UITypography.FontSnapped(_metrics.HeaderSubtitleFont);
            headerSubtitle.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f);
            headerSubtitle.alignment = TextAlignmentOptions.MidlineLeft;
            headerSubtitle.textWrappingMode = TextWrappingModes.NoWrap;
            headerSubtitle.text = subtitleText;
            headerSubtitle.raycastTarget = false;
        }

        private void BuildScrollContent()
        {
            var scrollGo = new GameObject("CardScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_drawerRoot, false);
            var scrollRectTransform = scrollGo.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(0f, _metrics.ScrollBottomInset);
            scrollRectTransform.offsetMax = new Vector2(0f, -(_metrics.HeaderHeight + (_mobile ? 18f : 14f) + 8f));

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = _mobile ? 60f : 30f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRect;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);
            scroll.content = contentRect;
            _contentRoot = contentRect;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (_mobile)
            {
                // 移动端：双列大卡网格，7 张卡两屏内看完
                float spacing = 16f;
                float cellW = (_drawerWidth - _metrics.ScrollLeftInset - _metrics.ScrollRightInset - spacing) * 0.5f;
                var grid = contentGo.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(cellW, _metrics.CardHeight);
                grid.spacing = new Vector2(spacing, spacing);
                grid.padding = new RectOffset(
                    (int)_metrics.ScrollLeftInset,
                    (int)_metrics.ScrollRightInset,
                    (int)_metrics.ScrollTopInset,
                    8);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
                grid.childAlignment = TextAnchor.UpperLeft;
            }
            else
            {
                var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(
                    (int)_metrics.ScrollLeftInset,
                    (int)_metrics.ScrollRightInset,
                    (int)_metrics.ScrollTopInset,
                    8);
                vlg.spacing = 10f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
        }

        /// <summary>
        /// 抽屉收起时的入口：左下角整备卡。
        /// 纸白直角卡 + 圆形武器色盘 + 「武器整备 / 当前武器」两行 + 黄色箭头块，取代旧的纯黄方块页签。
        /// </summary>
        private void BuildDrawerToggleButton()
        {
            float margin = MenuLayoutMath.Home(_mobile).Margin;

            var btnGo = new GameObject("DrawerToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            btnGo.transform.SetParent(_safeAreaRoot, false);
            _toggleButtonRoot = btnGo.GetComponent<RectTransform>();
            _toggleButtonRoot.anchorMin = new Vector2(0f, 0f);
            _toggleButtonRoot.anchorMax = new Vector2(0f, 0f);
            _toggleButtonRoot.pivot = new Vector2(0f, 0f);
            _toggleButtonRoot.anchoredPosition = new Vector2(margin, margin);
            _toggleButtonRoot.sizeDelta = _loadoutMetrics.Size;

            _toggleButtonBg = btnGo.GetComponent<UIInkShapeGraphic>();
            _toggleButtonBg.Shape = UIInkShape.SharpRect;
            _toggleButtonBg.CornerRadius = ComicStyle.CornerSharp;
            _toggleButtonBg.BorderWidth = ComicStyle.Border;
            _toggleButtonBg.BorderColor = ComicStyle.Ink;
            _toggleButtonBg.color = ComicStyle.Paper;
            _toggleButtonBg.HardShadow = false;
            _toggleButtonBg.Halftone = false;
            _toggleButtonBg.raycastTarget = true;

            float iconD = _loadoutMetrics.IconDiameter;
            float pad = _mobile ? 22f : 16f;

            // 圆形武器色盘
            var plateGo = new GameObject("LoadoutIconPlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            plateGo.transform.SetParent(btnGo.transform, false);
            var plateRect = plateGo.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0f, 0.5f);
            plateRect.anchorMax = new Vector2(0f, 0.5f);
            plateRect.pivot = new Vector2(0f, 0.5f);
            plateRect.anchoredPosition = new Vector2(pad, 0f);
            plateRect.sizeDelta = new Vector2(iconD, iconD);

            _toggleIconPlate = plateGo.GetComponent<UIInkShapeGraphic>();
            _toggleIconPlate.Shape = UIInkShape.Blob;
            _toggleIconPlate.WobbleAmplitude = 0f;
            _toggleIconPlate.BorderWidth = ComicStyle.Border;
            _toggleIconPlate.BorderColor = ComicStyle.Ink;
            _toggleIconPlate.color = theme.GetTeamColor(_currentTeam);
            _toggleIconPlate.raycastTarget = false;

            var iconGo = new GameObject("LoadoutIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(plateGo.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.16f, 0.16f);
            iconRect.anchorMax = new Vector2(0.84f, 0.84f);
            iconRect.sizeDelta = Vector2.zero;
            _toggleIconImage = iconGo.GetComponent<Image>();
            _toggleIconImage.preserveAspect = true;
            _toggleIconImage.raycastTarget = false;

            // 两行文字：标题 + 当前武器
            float textLeft = pad + iconD + (_mobile ? 20f : 14f);
            float chevron = _loadoutMetrics.ChevronSize;
            float textRight = chevron + (_mobile ? 34f : 26f);

            var titleGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(btnGo.transform, false);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(textLeft, _loadoutMetrics.Size.y * 0.48f);
            titleRect.offsetMax = new Vector2(-textRight, -(_mobile ? 20f : 12f));
            _toggleButtonText = titleGo.GetComponent<TextMeshProUGUI>();
            _toggleButtonText.font = theme.ResolveFont(true, "武器整备");
            _toggleButtonText.fontSize = UITypography.FontSnapped(_loadoutMetrics.TitleFont);
            _toggleButtonText.fontStyle = FontStyles.Bold;
            _toggleButtonText.alignment = TextAlignmentOptions.MidlineLeft;
            _toggleButtonText.color = ComicStyle.Ink;
            _toggleButtonText.textWrappingMode = TextWrappingModes.NoWrap;
            _toggleButtonText.overflowMode = TextOverflowModes.Overflow;
            _toggleButtonText.text = "武器整备";
            _toggleButtonText.raycastTarget = false;

            var nameGo = new GameObject("WeaponName", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(btnGo.transform, false);
            var nameRect = nameGo.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(textLeft, _mobile ? 22f : 14f);
            nameRect.offsetMax = new Vector2(-textRight, -(_loadoutMetrics.Size.y * 0.52f));
            _toggleWeaponText = nameGo.GetComponent<TextMeshProUGUI>();
            _toggleWeaponText.font = theme.ResolveFont(false, "喷射小枪");
            _toggleWeaponText.fontSize = UITypography.FontSnapped(_loadoutMetrics.NameFont);
            _toggleWeaponText.fontStyle = FontStyles.Normal;
            _toggleWeaponText.alignment = TextAlignmentOptions.MidlineLeft;
            _toggleWeaponText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f);
            _toggleWeaponText.textWrappingMode = TextWrappingModes.NoWrap;
            _toggleWeaponText.overflowMode = TextOverflowModes.Overflow;
            _toggleWeaponText.text = "当前武器";
            _toggleWeaponText.raycastTarget = false;

            // 右侧黄色箭头块
            var chevGo = new GameObject("Chevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            chevGo.transform.SetParent(btnGo.transform, false);
            var chevRect = chevGo.GetComponent<RectTransform>();
            chevRect.anchorMin = new Vector2(1f, 0.5f);
            chevRect.anchorMax = new Vector2(1f, 0.5f);
            chevRect.pivot = new Vector2(1f, 0.5f);
            chevRect.anchoredPosition = new Vector2(-(_mobile ? 22f : 16f), 0f);
            chevRect.sizeDelta = new Vector2(chevron, chevron);

            var chevShape = chevGo.GetComponent<UIInkShapeGraphic>();
            chevShape.Shape = UIInkShape.SharpRect;
            chevShape.CornerRadius = ComicStyle.CornerSharp;
            chevShape.BorderWidth = 0f;
            chevShape.color = ComicStyle.Yellow;
            chevShape.raycastTarget = false;

            var chevTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            chevTextGo.transform.SetParent(chevGo.transform, false);
            var chevTextRect = chevTextGo.GetComponent<RectTransform>();
            chevTextRect.anchorMin = Vector2.zero;
            chevTextRect.anchorMax = Vector2.one;
            chevTextRect.sizeDelta = Vector2.zero;
            var chevText = chevTextGo.GetComponent<TextMeshProUGUI>();
            chevText.font = theme.GetFont(true);
            chevText.fontSize = UITypography.FontSnapped(_loadoutMetrics.ChevronFont);
            chevText.fontStyle = FontStyles.Bold;
            chevText.alignment = TextAlignmentOptions.Center;
            chevText.color = ComicStyle.Ink;
            chevText.text = ">";
            chevText.raycastTarget = false;

            _toggleMotion = btnGo.GetComponent<UISpringMotion>();
            _toggleMotion.SetTheme(theme);

            _toggleButton = btnGo.GetComponent<Button>();
            _toggleButton.targetGraphic = _toggleButtonBg;
            _toggleButton.transition = Selectable.Transition.None;
            _toggleButton.onClick.AddListener(() => SetDrawerOpen(!_isDrawerOpen));

            var toggleJelly = btnGo.GetComponent<UIJellyButton>();
            toggleJelly.Configure(theme, _toggleButton, _toggleButtonBg);

            RefreshLoadoutCard();
            UpdateToggleButtonState();
        }

        private void PopulateCards()
        {
            if (_contentRoot == null) return;

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null) Destroy(_cards[i].gameObject);
            }
            _cards.Clear();

            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                WeaponConfigSO cfg = (_configs != null && i < _configs.Length) ? _configs[i] : null;
                Sprite icon = (_icons != null && i < _icons.Length) ? _icons[i] : null;

                var cardGo = new GameObject($"Card_{i}_{kind}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(WeaponCardView));
                cardGo.transform.SetParent(_contentRoot, false);

                var cardView = cardGo.GetComponent<WeaponCardView>();
                cardView.Initialize(i, kind, cfg, icon, theme, _currentTeam, HandleCardClicked);
                _cards.Add(cardView);
            }
        }

        private void HandleCardClicked(int index)
        {
            SelectWeapon(index);
        }

        public void SelectWeapon(int index, bool silent = false)
        {
            int clamped = WeaponSelectMath.ClampIndex(index);
            _selectedIndex = clamped;
            WeaponSelectMath.SaveSelectedWeaponIndex(clamped);

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetSelected(i == clamped, animate: !silent);
                }
            }

            RefreshLoadoutCard();

            if (!silent)
            {
                PlaySelectSound();
                OnWeaponSelected?.Invoke(clamped, SelectedConfig);
            }
        }

        public void SelectRelative(int delta)
        {
            int next = (_selectedIndex + delta) % WeaponSelectMath.WeaponCount;
            if (next < 0) next += WeaponSelectMath.WeaponCount;
            SelectWeapon(next);
            if (!_isDrawerOpen)
            {
                SetDrawerOpen(true);
            }
        }

        public void SetDrawerOpen(bool open)
        {
            _isDrawerOpen = open;
            _targetDrawerX = open ? 24f : ClosedDrawerX();
            if (_backdropGo != null)
            {
                _backdropGo.SetActive(open);
            }
            UpdateToggleButtonState();

            if (open && _toggleMotion != null)
            {
                _toggleMotion.Punch(0.06f);
            }
        }

        /// <summary>
        /// 抽屉收起时的 X：必须整块退出<画布>左缘，而不是只退出 SafeArea 左缘。
        /// 横屏刘海/挖孔设备的 SafeArea 左侧有内缩（实测约 130px），抽屉挂在 SafeArea 下，
        /// 若只退 drawerWidth+40，内缩量会把抽屉右缘顶回屏幕内，留下一条白纸+黑边的竖条压住主菜单。
        /// </summary>
        private float ClosedDrawerX()
        {
            return -(_drawerWidth + 40f + SafeAreaLeftInCanvasUnits());
        }

        /// <summary>SafeArea 左侧内缩换算成画布单位（0 = 屏幕左缘没有内缩）。</summary>
        private float SafeAreaLeftInCanvasUnits()
        {
            if (_safeAreaRoot == null) return 0f;
            var canvasRect = _safeAreaRoot.parent as RectTransform;
            if (canvasRect == null) return 0f;
            float left = canvasRect.rect.width * _safeAreaRoot.anchorMin.x + _safeAreaRoot.offsetMin.x;
            return Mathf.Max(0f, left);
        }

        private void UpdateToggleButtonState()
        {
            if (_toggleButtonRoot == null) return;

            _toggleButtonRoot.gameObject.SetActive(!_isDrawerOpen);
            if (_toggleButtonText != null) _toggleButtonText.text = "武器整备";
            if (!_isDrawerOpen)
            {
                _toggleButtonRoot.anchoredPosition = new Vector2(
                    MenuLayoutMath.Home(_mobile).Margin,
                    MenuLayoutMath.Home(_mobile).Margin);
            }
        }

        /// <summary>刷新整备卡上的当前武器图标与名称（选中变化/换队伍时调用）。</summary>
        private void RefreshLoadoutCard()
        {
            if (_toggleIconImage != null)
            {
                Sprite icon = (_icons != null && _selectedIndex >= 0 && _selectedIndex < _icons.Length)
                    ? _icons[_selectedIndex]
                    : null;
                if (icon != null)
                {
                    _toggleIconImage.sprite = icon;
                    _toggleIconImage.color = Color.white;
                    _toggleIconImage.gameObject.SetActive(true);
                }
                else
                {
                    _toggleIconImage.gameObject.SetActive(false);
                }
            }

            if (_toggleIconPlate != null)
            {
                _toggleIconPlate.color = theme != null ? theme.GetTeamColor(_currentTeam) : ComicStyle.Ink;
                _toggleIconPlate.Refresh();
            }

            if (_toggleWeaponText != null)
            {
                WeaponConfigSO cfg = SelectedConfig;
                string cnName = WeaponSelectMath.GetWeaponChineseName(
                    cfg != null ? cfg.weaponKind : WeaponSelectMath.IndexToKind(_selectedIndex));
                string text = $"{cnName} · 更换";
                _toggleWeaponText.font = theme.ResolveFont(false, text);
                _toggleWeaponText.text = text;
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
            if (!visible && _backdropGo != null)
            {
                _backdropGo.SetActive(false);
            }
        }

        public void SetTeam(Team team)
        {
            _currentTeam = team;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetTeam(team);
                }
            }
            RefreshLoadoutCard();
        }

        private void PlaySelectSound()
        {
            if (AudioManager.Instance != null && AudioManager.Instance.uiClickClip != null)
            {
                AudioManager.Instance.PlayOne(AudioManager.Instance.uiClickClip, 0.75f, 0.96f, 1.05f);
            }
        }

        private void ApplySafeArea()
        {
            if (_safeAreaRoot == null) return;
            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
            Vector2 min = safe.position;
            Vector2 max = min + safe.size;

            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _safeAreaRoot.anchorMin = min;
            _safeAreaRoot.anchorMax = max;
            _safeAreaRoot.offsetMin = Vector2.zero;
            _safeAreaRoot.offsetMax = Vector2.zero;

            // 刘海内缩变了（旋转/进出全屏）时，收起中的抽屉必须跟着重新算退出位置，
            // 否则旧位置会在新的内缩区里露出一截。
            if (!_isDrawerOpen)
            {
                _targetDrawerX = ClosedDrawerX();
                _currentDrawerX = _targetDrawerX;
                if (_drawerRoot != null)
                {
                    _drawerRoot.anchoredPosition = new Vector2(_currentDrawerX, 0f);
                }
            }
        }

        private void Update()
        {
            Rect safe = Screen.safeArea;
            if (safe != _lastSafeArea)
            {
                ApplySafeArea();
            }

            if (!Mathf.Approximately(_currentDrawerX, _targetDrawerX))
            {
                _currentDrawerX = Mathf.Lerp(_currentDrawerX, _targetDrawerX, Time.unscaledDeltaTime * 24f);
                if (Mathf.Abs(_currentDrawerX - _targetDrawerX) < 0.5f)
                {
                    _currentDrawerX = _targetDrawerX;
                }
                if (_drawerRoot != null)
                {
                    _drawerRoot.anchoredPosition = new Vector2(_currentDrawerX, 0f);
                }
            }

            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase != MatchManager.Phase.Ready)
            {
                return;
            }

            HandleInput();
        }

        private void HandleInput()
        {
            if (_mobile) return; // 触屏设备没有键盘，避免无意义读取

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SelectWeapon(0);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SelectWeapon(1);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SelectWeapon(2);
            else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) SelectWeapon(3);
            else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) SelectWeapon(4);
            else if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) SelectWeapon(5);
            else if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) SelectWeapon(6);

            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
            {
                SelectRelative(-1);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                SelectRelative(1);
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                SelectWeapon(_selectedIndex);
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetDrawerOpen(!_isDrawerOpen);
            }
        }
    }
}