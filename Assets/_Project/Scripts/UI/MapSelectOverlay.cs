using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 首页地图选择浮层（运行时装配，不改场景 YAML）：
    /// 每张地图一张大选项卡 + 实景缩略图；点另一张 = 写存档并在浮层内就地刷新选中态，
    /// 不重载场景、也不自动关闭——真正载入发生在「开始游戏」时（先切图再自动开赛）。
    /// 静态 <see cref="IsOpen"/> / <see cref="MenuInputConsumedThisFrame"/> 给开赛快捷键、
    /// 暂停菜单与武器抽屉做同帧输入仲裁。
    ///
    /// 地图数量增长：面板先按「地图数 × 期望卡宽」长宽（桌面 1920 下三张卡仍是原卡宽），
    /// 屏宽实在不够时卡片区转成横向滚动（两侧箭头 / 拖动 / 滚轮 / ← → 键），
    /// 不把文字列压没、也不让卡片溢出面板。
    /// </summary>
    [DisallowMultipleComponent]
    public class MapSelectOverlay : MonoBehaviour
    {
        /// <summary>箭头滑动时长（≤100ms 的 linear 硬切，符合几何风动效规范）。</summary>
        private const float ScrollSlideSeconds = 0.1f;

        private static MapSelectOverlay _instance;

        public static bool IsOpen => _instance != null;

        public static bool MenuInputConsumedThisFrame => MenuInputRouter.ConsumedThisFrame;

        private RectTransform _host;
        private UIThemeSO _theme;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Rect _lastSafeArea;

        private readonly List<GameObject> _cardRoots = new List<GameObject>();
        private RectTransform _cardsPanel;
        private RectTransform _cardsContent;
        private UIHorizontalScrollRect _cardsScroll;
        private Button _prevCardButton;
        private Button _nextCardButton;
        private MapSelectMetrics _cardsMetrics;
        private MapSelectCardLayout _cardsLayout;
        private bool _cardsMobile;
        private bool _cardsScrollNeeded;
        private float _cardsContentWidth;
        private float _cardsContentHeight;
        private float _scrollOffset;
        private float _scrollTarget;
        private bool _scrollAnimating;
        private bool _rebuildCardsQueued;
        private string _statusMessage;
        private Color _statusColor = ComicStyle.Ink;
        private TextMeshProUGUI _footerNote;
        private System.Action _onClosed;

        // 退场转场：面板先播 PlayOut，收敛后再隐藏并销毁根节点。
        // _panelMotion 是面板自身的动效；_rootGroup 盖住全屏遮罩——面板 PlayOut 只淡面板，
        // 遮罩不在它的 CanvasGroup 里，所以整层再走一次同节奏淡出，退场才不会「黑底突然消失」。
        private UISpringMotion _panelMotion;
        private CanvasGroup _rootGroup;
        private bool _closing;          // 退场进行中标记（防重入）
        private float _closingElapsed;  // 退场已耗时（兜底超时用）

        public static MapSelectOverlay Show(RectTransform host, UIThemeSO theme, System.Action onClosed = null)
        {
            if (host == null) return null;

            CloseActive();

            var go = new GameObject("MapSelectOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var overlay = go.AddComponent<MapSelectOverlay>();
            overlay._host = host;
            overlay._theme = UIThemeSO.Resolve(theme);
            overlay._onClosed = onClosed;
            overlay.Rebuild();
            overlay.CacheGeometry();
            _instance = overlay;
            return overlay;
        }

        public static void CloseActive()
        {
            if (_instance != null) _instance.Close();
        }

        /// <summary>Esc / 手柄 Start 的统一入口：浮层打开时关闭并标记本帧已消费。</summary>
        public static bool TryCloseFromMenuInput()
        {
            if (_instance != null)
            {
                _instance.Close();
                MenuInputRouter.MarkConsumed(MenuEscLayer.MapSelect);
                return true;
            }

            return MenuInputRouter.ConsumedThisFrame;
        }

        private void Close()
        {
            // 防重入：连点 × / 空白处 / Esc（含同帧多次）只播一次退场，
            // 否则会叠加两次 PlayOut、重复订阅 OnPlayedOut，甚至重复触发 onClosed。
            if (_closing) return;
            _closing = true;
            _closingElapsed = 0f;

            ClearSelection();

            System.Action onClosed = _onClosed;
            _onClosed = null;
            if (onClosed != null)
            {
                // 关窗回调只做首页换肤/刷新，失败不该把浮层卡在半关状态。
                try
                {
                    onClosed();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[MapSelect] 关闭回调失败：" + ex.Message);
                }
            }

            // 立刻摘掉「打开态」：IsOpen 变 false，输入仲裁视作已关闭（与硬切时的语义完全一致，
            // 一个 Esc 只关一次浮层）；视觉退场另行播放，逻辑层与展示层解耦。
            if (_instance == this) _instance = null;

            // 退场期间不再接收点击：遮罩/卡片还在画，但不能被点到（尤其遮罩的「点空白关闭」）。
            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            if (_panelMotion != null && gameObject.activeInHierarchy)
            {
                _panelMotion.OnPlayedOut -= HandleClosed; // 防重复订阅
                _panelMotion.OnPlayedOut += HandleClosed;
                _panelMotion.PlayOut();
            }
            else
            {
                // 面板 motion 缺失或已失活：退不了场就退回硬切。
                HandleClosed();
            }
        }

        /// <summary>退场收敛：隐藏并销毁浮层根节点（OnPlayedOut 与超时兜底都可能调用，故做幂等）。</summary>
        private void HandleClosed()
        {
            if (!_closing) return;

            if (_panelMotion != null) _panelMotion.OnPlayedOut -= HandleClosed;
            _closing = false;
            _closingElapsed = 0f;

            if (gameObject != null)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        /// <summary>退场推进：整层淡出（遮罩与面板同节奏），并做兜底超时。</summary>
        private void TickClosing()
        {
            _closingElapsed += Time.unscaledDeltaTime;

            if (_rootGroup != null)
            {
                // 与面板自身的 alpha 淡出用同一时长（UISpringMotion 的透明度走 entranceFadeDuration），
                // 否则遮罩会比面板慢半拍地留在屏幕上。
                UIThemeSO settings = UIThemeSO.Resolve(_theme);
                float duration = Mathf.Clamp(settings.entranceFadeDuration, 0.05f, 0.3f);
                _rootGroup.alpha = 1f - Mathf.Clamp01(_closingElapsed / duration);
            }

            // 兜底：面板 motion 若被外部 SetActive(false)/禁用，OnPlayedOut 永远不来，_closing 会卡住、
            // 浮层残留。超时强制收尾（正常动画 ≤0.3s，这里给 1s 余量）。
            if (_closingElapsed >= 1f)
            {
                HandleClosed();
            }
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // 退场期间只推进淡出：不再重建/滚动，否则屏幕尺寸变化触发的 Rebuild 会把面板
            // 连 motion 一起重建并 PlayIn，正在淡出的浮层会被「复活」一帧。
            if (_closing)
            {
                TickClosing();
                return;
            }

            // 选中态切换安排在下一帧重建卡片：按钮的 onClick 还在派发时销毁自身节点不安全。
            if (_rebuildCardsQueued)
            {
                _rebuildCardsQueued = false;
                RebuildCards();
                return;
            }

            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight &&
                Screen.safeArea == _lastSafeArea)
            {
                TickCardScroll();
                TickCarouselInput();
                return;
            }

            CacheGeometry();
            Rebuild();
        }

        private void CacheGeometry()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastSafeArea = Screen.safeArea;
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            BuildRoot();
        }

        private void BuildRoot()
        {
            bool mobile = UITypography.IsMobileLike;
            MapSelectMetrics m = MapSelectLayoutMath.Metrics(mobile);
            Vector2 panelSize = ResolvePanelSize(mobile);

            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            scrimGo.transform.SetParent(transform, false);
            Stretch((RectTransform)scrimGo.transform);

            var scrimImage = scrimGo.GetComponent<Image>();
            scrimImage.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.74f);
            scrimImage.raycastTarget = true;

            var scrimButton = scrimGo.GetComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.onClick.AddListener(Close);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = ResolveSafeCenter();
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = panelSize;

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.RoundedRect; // 圆角化：弹窗面板自适应圆角，硬影同形状跟随
            panelShape.BorderWidth = ComicStyle.Border;
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.GlossAlpha = 0f;
            panelShape.Halftone = false;
            panelShape.raycastTarget = true;
            _theme.ApplyHardShadow(panelShape, UIHardShadowLevel.Dialog);

            var motion = panelGo.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);
            motion.Build();
            motion.SnapHome();
            motion.PlayIn();
            _panelMotion = motion;

            // 退场整层淡出的载体（遮罩 + 面板一起淡）；alpha 常态为 1，不影响入场。
            _rootGroup = GetComponent<CanvasGroup>();
            if (_rootGroup == null) _rootGroup = gameObject.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 1f;

            BuildHeader(panel, m, mobile, panelSize);

            float contentTop = m.HeaderHeight + m.ContentInset;
            float contentHeight = Mathf.Max(160f, panelSize.y - contentTop - m.FooterHeight);
            BuildCards(panel, m, mobile, panelSize, contentTop, contentHeight);

            BuildFooter(panel, m, mobile, panelSize);

            if (EventSystem.current != null && CloseButton != null)
            {
                EventSystem.current.SetSelectedGameObject(CloseButton.gameObject);
            }
        }

        private Button CloseButton { get; set; }

        private Vector2 ResolvePanelSize(bool mobile)
        {
            Rect hostRect = _host != null ? _host.rect : new Rect(0f, 0f, MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            float hostWidth = hostRect.width > 1f ? hostRect.width : MenuLayoutMath.RefWidth;
            float hostHeight = hostRect.height > 1f ? hostRect.height : MenuLayoutMath.RefHeight;

            Rect safe = Screen.safeArea;
            float safeWidth = hostWidth;
            float safeHeight = hostHeight;
            if (Screen.width > 0 && Screen.height > 0)
            {
                safeWidth = hostWidth * Mathf.Clamp01(safe.width / Screen.width);
                safeHeight = hostHeight * Mathf.Clamp01(safe.height / Screen.height);
            }

            return MapSelectLayoutMath.ResolvePanelSize(mobile, safeWidth, safeHeight, MapCatalogService.Maps.Count);
        }

        private Vector2 ResolveSafeCenter()
        {
            Rect safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0)
                return new Vector2(0.5f, 0.5f);

            float x = (safe.x + safe.width * 0.5f) / Screen.width;
            float y = (safe.y + safe.height * 0.5f) / Screen.height;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private void BuildHeader(RectTransform panel, MapSelectMetrics m, bool mobile, Vector2 panelSize)
        {
            float innerWidth = panelSize.x - m.Padding * 2f;

            // 圆角化：顶部身份色带内缩到圆角直线段内并改胶囊短划
            float panelRadius = ComicStyle.CornerFor(panelSize.x, panelSize.y);
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(panel, false);
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-(panelRadius + ComicStyle.Border) * 2f, m.TopBandHeight);
            band.anchoredPosition = new Vector2(0f, -ComicStyle.Border);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.Pill;
            bandShape.BorderWidth = 0f;
            bandShape.color = ComicStyle.Blue;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;

            float top = m.Padding + m.TopBandHeight;
            CreateTag(panel, "HeaderTag", "MAP", m.TagSize, UITypography.FontSnapped(m.TagFont),
                ComicStyle.Ink, ComicStyle.Paper, new Vector2(m.Padding, top));

            float titleTop = top + m.TagSize.y + 12f;
            float titleFont = UITypography.FontSnapped(m.TitleFont);
            CreateText(panel, "Title", "选择地图", titleFont, true, ComicStyle.Ink,
                new Vector2(m.Padding, titleTop),
                new Vector2(innerWidth - m.CloseSize.x - 24f, titleFont * 1.25f),
                TextAlignmentOptions.MidlineLeft, 1f, FontStyles.Bold);

            float subtitleFont = UITypography.FontSnapped(m.SubtitleFont);
            CreateText(panel, "Subtitle", "SELECT MAP", subtitleFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                new Vector2(m.Padding, titleTop + (titleFont - subtitleFont) * 0.5f),
                new Vector2(innerWidth - m.CloseSize.x - 24f, subtitleFont * 1.6f),
                TextAlignmentOptions.MidlineRight, 1.5f, FontStyles.Bold);

            CloseButton = CreateCloseButton(panel, m, new Vector2(panelSize.x - m.Padding - m.CloseSize.x, top));

            CreateSolidRect(panel, "HeaderDivider",
                new Vector2(m.Padding, m.HeaderHeight),
                new Vector2(panelSize.x - m.Padding * 2f, 2f),
                ComicStyle.InkSoft, false);
        }

        private void BuildCards(RectTransform panel, MapSelectMetrics m, bool mobile, Vector2 panelSize,
            float contentTop, float contentHeight)
        {
            _cardsPanel = panel;
            _cardsMetrics = m;
            _cardsMobile = mobile;
            _cardsContentHeight = contentHeight;

            float innerWidth = panelSize.x - m.Padding * 2f;
            MapSelectCardLayout layout = ResolveCardArea(m, mobile, innerWidth, out float gutter);
            // 视口恒存在（同一套结构两副模式）：放得下时关掉 RectMask2D 与 ScrollRect，
            // 卡片保持原有硬影与满宽版式；放不下才开遮罩 + 横向滚动。
            _cardsLayout = layout;
            _cardsScrollNeeded = layout.ScrollNeeded;
            _cardsContentWidth = layout.ContentWidth;

            var viewportGo = new GameObject("CardViewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(panel, false);
            var viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(0f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = new Vector2(m.Padding + gutter, -contentTop);
            viewport.sizeDelta = new Vector2(layout.ViewportWidth, contentHeight);

            var mask = viewportGo.GetComponent<RectMask2D>();
            // 规范：父级有 RectMask2D 时卡片不能带硬影（偏移阴影会被裁掉）。所以只有滚动模式下
            // 才开遮罩，此时卡片也不开硬影（见 BuildCard）。
            mask.enabled = _cardsScrollNeeded;

            // 卡片之间的空隙也要能按住拖动：铺一层全透明命中面垫在卡片下面
            // （ScrollRect 就在视口自身上，命中它可以拿到拖动/滚轮事件）。
            var surfaceGo = new GameObject("DragSurface", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            surfaceGo.transform.SetParent(viewport, false);
            var surface = (RectTransform)surfaceGo.transform;
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = Vector2.zero;
            surface.offsetMax = Vector2.zero;
            var surfaceImage = surfaceGo.GetComponent<Image>();
            surfaceImage.color = new Color(0f, 0f, 0f, 0f);
            surfaceImage.raycastTarget = _cardsScrollNeeded;

            var contentGo = new GameObject("CardContent", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            _cardsContent = (RectTransform)contentGo.transform;
            _cardsContent.anchorMin = new Vector2(0f, 1f);
            _cardsContent.anchorMax = new Vector2(0f, 1f);
            _cardsContent.pivot = new Vector2(0f, 1f);
            _cardsContent.anchoredPosition = Vector2.zero;
            _cardsContent.sizeDelta = new Vector2(layout.ContentWidth, contentHeight);

            _cardsScroll = viewportGo.AddComponent<UIHorizontalScrollRect>();
            _cardsScroll.viewport = viewport;
            _cardsScroll.content = _cardsContent;
            _cardsScroll.horizontal = true;
            _cardsScroll.vertical = false;
            _cardsScroll.movementType = ScrollRect.MovementType.Clamped;
            _cardsScroll.inertia = true;
            // 滚轮不需额外接线：ScrollRect 在「只开横向」时会把竖直滚轮量换到横轴上。
            _cardsScroll.scrollSensitivity = mobile ? 60f : 30f;
            // 放得下时彻底关掉滚动：既不抢拖动，也不会把内容挪走。
            _cardsScroll.enabled = _cardsScrollNeeded;

            _scrollOffset = 0f;
            _scrollTarget = 0f;
            _scrollAnimating = false;
            BuildScrollArrows(panel, m, mobile, gutter, panelSize, contentTop, contentHeight);
            RebuildCards();
        }

        /// <summary>
        /// 卡片区可用宽 → 实际布局。需要滚动时两侧再让出「箭头 + 间距」的 gutter
        /// （箭头不盖卡片，可见张数可预期）；若让出后反而放得下，就退回满宽不显箭头。
        /// </summary>
        private static MapSelectCardLayout ResolveCardArea(MapSelectMetrics m, bool mobile, float innerWidth, out float gutter)
        {
            MapSelectCardLayout full = MapSelectLayoutMath.ResolveCardLayout(mobile, innerWidth, MapCatalogService.Maps.Count);
            gutter = 0f;
            if (!full.ScrollNeeded) return full;

            float candidate = m.ScrollArrowSize.x + m.ScrollArrowGap;
            MapSelectCardLayout inset = MapSelectLayoutMath.ResolveCardLayout(
                mobile, innerWidth - candidate * 2f, MapCatalogService.Maps.Count);
            if (!inset.ScrollNeeded) return full;

            gutter = candidate;
            return inset;
        }

        /// <summary>两侧滑动箭头：只在需要滚动时创建并显示，居中于 gutter。</summary>
        private void BuildScrollArrows(RectTransform panel, MapSelectMetrics m, bool mobile, float gutter,
            Vector2 panelSize, float contentTop, float contentHeight)
        {
            _prevCardButton = null;
            _nextCardButton = null;
            if (!_cardsScrollNeeded || gutter <= 0f) return;

            float offset = (gutter - m.ScrollArrowSize.x) * 0.5f;
            float top = contentTop + (contentHeight - m.ScrollArrowSize.y) * 0.5f;
            // 用 ←/→（不是 ‹/›）：这两个字形已在字体账本里，不用重跑字库生成。
            _prevCardButton = CreateScrollArrow(panel, m, "←", new Vector2(m.Padding + offset, top), -1);
            _nextCardButton = CreateScrollArrow(panel, m, "→",
                new Vector2(panelSize.x - m.Padding - gutter + offset, top), 1);
        }

        private Button CreateScrollArrow(RectTransform panel, MapSelectMetrics m, string glyph, Vector2 topLeft, int direction)
        {
            var go = new GameObject(direction < 0 ? "PrevCardButton" : "NextCardButton",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = m.ScrollArrowSize;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill; // 圆角化：56×56 箭头键改正圆
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.Ink;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.ScrollArrowFont);
            TextMeshProUGUI label = CreateText(rect, "Label", glyph, font, true, ComicStyle.Paper,
                Vector2.zero, m.ScrollArrowSize, TextAlignmentOptions.Center, 0f, FontStyles.Bold);
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => ScrollByCards(direction));

            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (label != null) jelly.SetPressContent(label.rectTransform);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Tag);
            UISoundBinder.EnsureBinder(button);
            return button;
        }

        /// <summary>只重建卡片本体：保留视口、遮罩、箭头与滚动位置，避免整块浮层重播入场动画。</summary>
        private void RebuildCards()
        {
            if (_cardsPanel == null || _cardsContent == null)
            {
                Rebuild();
                return;
            }

            for (int i = 0; i < _cardRoots.Count; i++)
            {
                GameObject root = _cardRoots[i];
                if (root == null) continue;
                root.SetActive(false);
                Destroy(root);
            }
            _cardRoots.Clear();

            float cardHeight = _cardsContentHeight - (_cardsScrollNeeded ? _cardsMetrics.ScrollSlack : 0f);
            float step = _cardsLayout.CardWidth + _cardsMetrics.CardGap;
            // 选中态以「存档里的选择」为准，而不是当前场景：选完图浮层不重载场景，
            // 但玩家必须立刻看到选中态已经换到另一张卡上。
            MapDefinition active = MapCatalogService.Selected;

            for (int i = 0; i < MapCatalogService.Maps.Count; i++)
            {
                MapDefinition map = MapCatalogService.Maps[i];
                var topLeft = new Vector2(_cardsLayout.SideInset + i * step, 0f);
                BuildCard(_cardsContent, _cardsMetrics, _cardsMobile, map, map.Id == active.Id, topLeft,
                    new Vector2(_cardsLayout.CardWidth, cardHeight));
            }

            // 重建可能由「点了另一张卡」触发，滚动位置必须留在原地：玩家点第 3 张时不该被甩回第 1 张。
            ApplyScrollOffset(_scrollOffset);
            RefreshScrollArrows();
        }

        // ==================== 横向滚动 ====================

        private float CardScrollStep => MapSelectLayoutMath.CardStep(_cardsMetrics, _cardsLayout.CardWidth);

        private float MaxScrollOffset => Mathf.Max(0f, _cardsContentWidth - _cardsLayout.ViewportWidth);

        private void ApplyScrollOffset(float offset)
        {
            _scrollOffset = MapSelectLayoutMath.ClampScrollOffset(offset, _cardsContentWidth, _cardsLayout.ViewportWidth);
            if (_cardsScroll != null && _cardsScroll.content != null)
            {
                _cardsScroll.content.anchoredPosition = new Vector2(-_scrollOffset, 0f);
            }
        }

        private void ScrollByCards(int direction)
        {
            if (!_cardsScrollNeeded || _cardsScroll == null) return;

            _scrollTarget = MapSelectLayoutMath.StepScrollOffset(
                _scrollOffset, direction, CardScrollStep, _cardsContentWidth, _cardsLayout.ViewportWidth);
            _scrollAnimating = Mathf.Abs(_scrollTarget - _scrollOffset) > 0.5f;
            if (_scrollAnimating) _cardsScroll.StopMovement();
            else RefreshScrollArrows();
        }

        private void TickCardScroll()
        {
            if (!_cardsScrollNeeded || _cardsScroll == null || _cardsScroll.content == null) return;

            if (_cardsScroll.IsDragging)
            {
                // 手感优先：玩家一按就开始拖，立刻取消箭头动画并跟住 ScrollRect 的真实位置。
                CancelScrollAnimation();
                _scrollOffset = MapSelectLayoutMath.ClampScrollOffset(
                    -_cardsScroll.content.anchoredPosition.x, _cardsContentWidth, _cardsLayout.ViewportWidth);
                RefreshScrollArrows();
                return;
            }

            // 滚轮交给 ScrollRect；这里只负责撤销箭头动画，别和玩家的滚轮抢内容位置。
            if (ScrollWheelThisFrame()) CancelScrollAnimation();

            if (!_scrollAnimating)
            {
                // 拖动/滚轮惯性结束后位置由 ScrollRect 写，同步一次用于箭头可用态。
                _scrollOffset = MapSelectLayoutMath.ClampScrollOffset(
                    -_cardsScroll.content.anchoredPosition.x, _cardsContentWidth, _cardsLayout.ViewportWidth);
                _scrollTarget = _scrollOffset;
                RefreshScrollArrows();
                return;
            }

            float speed = Mathf.Max(1f, Mathf.Abs(_scrollTarget - _scrollOffset) / ScrollSlideSeconds);
            float next = Mathf.MoveTowards(_scrollOffset, _scrollTarget, speed * Time.unscaledDeltaTime);
            ApplyScrollOffset(next);
            if (Mathf.Abs(_scrollOffset - _scrollTarget) <= 0.5f)
            {
                ApplyScrollOffset(_scrollTarget);
                _scrollAnimating = false;
                _cardsScroll.StopMovement();
            }
            RefreshScrollArrows();
        }

        private void CancelScrollAnimation()
        {
            _scrollAnimating = false;
            _scrollTarget = _scrollOffset;
        }

        private static bool ScrollWheelThisFrame()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
        }

        private void TickCarouselInput()
        {
            if (!_cardsScrollNeeded) return;

            bool prev = false;
            bool next = false;

            // 只用方向键（不用 A/D）：首页浮层打开时角色移动仍可能读到 WASD，别和玩法抢键。
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                prev = keyboard.leftArrowKey.wasPressedThisFrame;
                next = keyboard.rightArrowKey.wasPressedThisFrame;
            }

            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null && gamepad.dpad != null)
            {
                prev |= gamepad.dpad.left.wasPressedThisFrame;
                next |= gamepad.dpad.right.wasPressedThisFrame;
            }

            if (prev) ScrollByCards(-1);
            else if (next) ScrollByCards(1);
        }

        private void RefreshScrollArrows()
        {
            if (_prevCardButton != null)
            {
                SetArrowAvailable(_prevCardButton, _scrollOffset > 0.5f);
            }
            if (_nextCardButton != null)
            {
                SetArrowAvailable(_nextCardButton, _scrollOffset < MaxScrollOffset - 0.5f);
            }
        }

        private static void SetArrowAvailable(Button button, bool available)
        {
            button.interactable = available;
            // 过渡是 None，没有可用态视觉：不可用时自己把底色压暗（点不动就看得出来）。
            if (button.targetGraphic is UIInkShapeGraphic shape)
            {
                shape.color = available ? ComicStyle.Ink : ComicStyle.WithAlpha(ComicStyle.Ink, 0.32f);
            }
        }

        private void BuildCard(RectTransform parent, MapSelectMetrics m, bool mobile, MapDefinition map,
            bool selected, Vector2 topLeft, Vector2 size)
        {
            var go = new GameObject("Card_" + map.Id, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(parent, false);
            _cardRoots.Add(go);
            var card = (RectTransform)go.transform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            card.sizeDelta = size;

            // 滚动视口内有 RectMask2D：按规范卡片不开硬影（偏移阴影会被遮罩裁掉），
            // 选中态仍由更粗的强调色描边表达。
            bool cardShadow = !_cardsScrollNeeded;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：地图卡自适应圆角
            shape.BorderWidth = selected ? m.CardBorderWidth : m.CardBorderThinWidth;
            shape.BorderColor = selected ? map.Accent : ComicStyle.Ink;
            shape.color = ComicStyle.PaperAlt;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = true;
            if (cardShadow) _theme.ApplyHardShadow(shape, UIHardShadowLevel.Panel);

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnCardClicked(map));
            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (cardShadow) jelly.EnableThemeHardShadow(UIHardShadowLevel.Panel);
            UISoundBinder.EnsureBinder(button);

            // 圆角化：卡片色带内缩到圆角直线段内并改胶囊短划
            float cardRadius = ComicStyle.CornerFor(size.x, size.y);
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(card, false);
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-(cardRadius + m.CardBorderThinWidth) * 2f, m.CardBandHeight);
            band.anchoredPosition = new Vector2(0f, -m.CardBorderThinWidth);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.Pill;
            bandShape.BorderWidth = 0f;
            bandShape.color = map.Accent;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;

            float pad = m.CardInnerPadding;
            if (selected)
            {
                CreateTag(card, "CurrentBadge", "当前", m.CardBadgeSize,
                    UITypography.FontSnapped(m.CardBadgeFont), map.Accent, ComicStyle.Paper,
                    new Vector2(size.x - pad - m.CardBadgeSize.x, m.CardBandHeight + pad * 0.55f));
            }

            Vector2 previewSize = MapSelectLayoutMath.PreviewSize(mobile, size.x, size.y);
            float previewTop = m.CardBandHeight + pad * 0.55f;
            if (!selected)
            {
                previewTop += 4f;
            }
            RectTransform preview = MapPreviewBuilder.BuildPreview(
                card, map, previewSize, _theme, "Preview_" + map.Id);
            preview.anchorMin = preview.anchorMax = new Vector2(0f, 1f);
            preview.pivot = new Vector2(0f, 1f);
            preview.anchoredPosition = new Vector2(pad, -previewTop);

            float titleFont = UITypography.FontSnapped(m.CardTitleFont);
            float englishFont = UITypography.FontSnapped(m.CardEnglishFont);
            float descriptorFont = UITypography.FontSnapped(m.CardDescriptorFont);

            float textLeft = pad + previewSize.x + 26f;
            float textWidth = Mathf.Max(96f, size.x - textLeft - pad);
            float titleHeight = titleFont * 1.25f;
            float englishHeight = englishFont * 1.55f;
            // 描述在中/英两图都可能超过一列宽，按两行预留高度并允许换行，避免文字压出卡片边框。
            float descriptorHeight = descriptorFont * 1.45f * 2f;
            float textBlock = titleHeight + englishHeight + descriptorHeight;
            // 文字块与左侧预览垂直居中对齐，卡片右半边不再留出空白带。
            float textTop = previewTop + Mathf.Max(0f, (previewSize.y - textBlock) * 0.5f);

            CreateText(card, "MapName", map.DisplayName, titleFont, true, ComicStyle.Ink,
                new Vector2(textLeft, textTop),
                new Vector2(textWidth, titleHeight),
                TextAlignmentOptions.MidlineLeft, 1f, FontStyles.Bold);

            float englishTop = textTop + titleHeight * 0.86f;
            CreateText(card, "MapEnglishName", map.EnglishName, englishFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                new Vector2(textLeft, englishTop),
                new Vector2(textWidth, englishHeight),
                TextAlignmentOptions.MidlineLeft, 1.5f, FontStyles.Bold);

            float descriptorTop = englishTop + englishHeight * 0.92f;
            CreateText(card, "MapDescriptor", map.Descriptor, descriptorFont, false, ComicStyle.Ink,
                new Vector2(textLeft, descriptorTop),
                new Vector2(textWidth, descriptorHeight),
                TextAlignmentOptions.TopLeft, 0.5f, FontStyles.Bold, true);

            string buttonLabel = selected ? "当前地图" : "切换到此图";
            Color buttonFill = selected ? map.Accent : ComicStyle.Paper;
            Color buttonBorder = selected ? map.Accent : ComicStyle.Ink;
            CreateCardButton(card, m, buttonLabel, buttonFill, buttonBorder, selected ? 0f : 2.5f,
                selected ? ComicStyle.Paper : ComicStyle.Ink, map,
                new Vector2(pad, size.y - pad - m.CardButtonHeight),
                new Vector2(size.x - pad * 2f, m.CardButtonHeight));
        }

        private void CreateCardButton(RectTransform card, MapSelectMetrics m, string label, Color fill,
            Color borderColor, float borderWidth, Color labelColor, MapDefinition map, Vector2 topLeft,
            Vector2 size)
        {
            var go = new GameObject("SelectButton", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(card, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：选卡按钮自适应圆角
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = borderColor;
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = true;
            bool cardShadow = !_cardsScrollNeeded;
            if (cardShadow) _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.CardButtonFont);
            TextMeshProUGUI text = CreateText(rect, "Label", label, font, true, labelColor,
                new Vector2(0f, 0f), size, TextAlignmentOptions.Center, 1f, FontStyles.Bold);
            if (text != null)
            {
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = Vector2.zero;
                text.rectTransform.offsetMax = Vector2.zero;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnCardClicked(map));

            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (text != null) jelly.SetPressContent(text.rectTransform);
            if (cardShadow) jelly.EnableThemeHardShadow(UIHardShadowLevel.Tag);
            UISoundBinder.EnsureBinder(button);
        }

        /// <summary>
        /// 卡片 / 「切换到此图」：只保存选择并在浮层内就地换选中态，不重载场景、也不关闭浮层。
        /// 场景载入交给「开始游戏」：选中的图与当前场景不一致时先切图再自动开赛，
        /// 玩家能先看到「已选择」再自己关掉浮层。
        /// </summary>
        private void OnCardClicked(MapDefinition map)
        {
            if (map == null) return;

            if (map.Id == MapCatalogService.Selected.Id)
            {
                SetStatus($"「{map.DisplayName}」已是当前选择", ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f));
                return;
            }

            if (!MapCatalogService.CanLoadMap(map))
            {
                // 老安装包只打了主场景：宁可不改存档，也不能让玩家选到一张打不开的图。
                SetStatus($"安装包缺少「{map.DisplayName}」场地，请更新客户端", ComicStyle.Red);
                Debug.LogError($"[MapSelect] 安装包中找不到地图场景：{map.ScenePath}");
                return;
            }

            MapCatalogService.TrySelect(map.Id);
            SetStatus($"已选择「{map.DisplayName}」· 开始游戏时载入该场地", map.Accent);
            _rebuildCardsQueued = true;
        }

        private void SetStatus(string message, Color color)
        {
            _statusMessage = message;
            _statusColor = color;
            if (_footerNote == null) return;
            _footerNote.text = message;
            _footerNote.color = color;
        }

        private void BuildFooter(RectTransform panel, MapSelectMetrics m, bool mobile, Vector2 panelSize)
        {
            float noteFont = UITypography.FontSnapped(m.NoteFont);
            float noteTop = panelSize.y - m.FooterHeight + (m.FooterHeight - noteFont * 1.5f) * 0.5f;

            bool hasStatus = !string.IsNullOrEmpty(_statusMessage);
            // 需要横向滚动时默认提示改成「怎么看其它地图」，否则玩家不会知道右边还有卡。
            string defaultNote = _cardsScrollNeeded
                ? (mobile ? "左右拖动查看全部地图" : "← → 键 / 拖动 / 滚轮 查看全部地图")
                : "选择即保存 · 开始游戏时载入场地";
            _footerNote = CreateText(panel, "FooterNote",
                hasStatus ? _statusMessage : defaultNote,
                noteFont, false,
                hasStatus ? _statusColor : ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, noteTop),
                new Vector2(panelSize.x * 0.62f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            string closeHint = mobile ? "点 × 或空白处关闭" : "ESC / 点 × / 空白处关闭";
            CreateText(panel, "FooterCloseHint", closeHint, noteFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, noteTop),
                new Vector2(panelSize.x - m.Padding * 2f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineRight, 0.5f, FontStyles.Bold);
        }

        // ==================== 基础图元 ====================

        private Button CreateCloseButton(RectTransform panel, MapSelectMetrics m, Vector2 topLeft)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = m.CloseSize;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill; // 圆角化：72×72 关闭键改正圆
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.Ink;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.CloseFont);
            TextMeshProUGUI label = CreateText(rect, "Label", "×", font, true, ComicStyle.Paper,
                new Vector2(0f, 0f), m.CloseSize, TextAlignmentOptions.Center, 0f, FontStyles.Bold);
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (label != null) jelly.SetPressContent(label.rectTransform);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Tag);

            UISoundBinder.EnsureBinder(button);
            return button;
        }

        private RectTransform CreateTag(Transform parent, string name, string label, Vector2 size, float font,
            Color fill, Color textColor, Vector2 topLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill; // 圆角化：角签改胶囊
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            TextMeshProUGUI tmp = CreateText(rect, "Text", label, font, true, textColor,
                new Vector2(8f, 0f), new Vector2(Mathf.Max(20f, size.x - 16f), size.y),
                TextAlignmentOptions.Center, 1.5f, FontStyles.Bold);
            if (tmp != null) tmp.fontStyle |= FontStyles.UpperCase;
            return rect;
        }

        private static RectTransform CreateSolidRect(Transform parent, string name, Vector2 topLeft, Vector2 size,
            Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：细线/色块自适应（1~2px 线会被钳成直角）
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            shape.color = color;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = raycast;
            return rect;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
            bool display, Color color, Vector2 topLeft, Vector2 size, TextAlignmentOptions align,
            float spacing = 0f, FontStyles style = FontStyles.Bold, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.characterSpacing = spacing;
            tmp.alignment = align;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;

            TMP_FontAsset font = _theme.ResolveFont(display, text);
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void ClearSelection()
        {
            if (EventSystem.current == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
