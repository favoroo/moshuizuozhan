using System;
using System.Collections;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 衣柜（Locker）浮层：左侧 5 个外观槽位页签 + 右侧 3D 预览与外观卡片网格（运行时装配，不改场景 YAML）。
    ///
    /// 为什么是「点一下立刻穿上」而不是「选中 → 再点穿上」：
    /// 外观是低风险、可随时反悔的偏好项（不像开赛 / 删除存档），两步确认只会让人多点一次；
    /// 点已解锁卡片 = 立即写入档案并刷新预览，点未解锁卡片 = 抖一下告诉玩家「这个还没有」。
    ///
    /// 结构照抄 <see cref="HomeInfoOverlay"/>：独立 Overlay Canvas（默认 sortingOrder 65，高于地图选择 60）、
    /// 全屏墨色遮罩、居中纸白面板（4px 墨框 + 弹窗硬影）、Esc 同帧消费标记、屏幕尺寸变化整层重建。
    /// 页签切换只重建右侧网格与预览状态，不重跑 <see cref="Rebuild"/>——整层重建会让面板重新播一次入场，
    /// 玩家看到的就是「切个页签闪一下」。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockerOverlay : MonoBehaviour
    {
        private static LockerOverlay _instance;

        public static bool IsOpen => _instance != null;

        /// <summary>面板宽度/高度与盒子度量（桌面 / 移动端两套；字号仍统一走 UITypography 的 ×1.28）。</summary>
        private struct LockerMetrics
        {
            public float PanelMaxWidth;
            public float PanelMaxHeight;
            public float PanelWidthRatio;
            public float PanelHeightRatio;
            public float Padding;
            public float HeaderHeight;
            public float ContentInset;
            public float FooterHeight;
            public float TopBandHeight;

            public Vector2 TagSize;
            public float TagFont;
            public float TitleFont;
            public float CountFont;
            public Vector2 CloseSize;
            public float CloseFont;

            public float TabWidth;
            public float TabHeight;
            public float TabGap;
            public float TabIcon;
            public float TabFont;
            public float TabBadgeFont;
            public float ColumnGap;

            public float PreviewHeightMax;
            public float CaptionHeight;
            public float CaptionFont;
            public Vector2 TitlePillSize;
            public float TitlePillFont;
            public float InfoTitleFont;
            public float InfoNameFont;
            public float InfoSubFont;
            public float InfoColumnWidth;

            public float CardHeight;
            public float CardGap;
            public int GridColumns;
            public float CardNameFont;
            public float CardSubFont;
            public float CardHintFont;

            public float FooterFont;
            public Vector2 DoneSize;
            public float DoneFont;
        }

        /// <summary>卡片顶部身份色带高度（规范：5px，不承载文字）。</summary>
        private const float CardBandHeight = 5f;

        /// <summary>单张外观卡片的引用集合（换装后只刷这些，不重建整层）。</summary>
        private sealed class CardRefs
        {
            public CosmeticDef Def;
            public bool Unlocked;
            public RectTransform Root;
            public Vector2 Home;
            public UIInkShapeGraphic Shape;
            public RectTransform EquippedTag;
            public UISpringMotion Motion;
        }

        private RectTransform _host;
        private UIThemeSO _theme;
        private CosmeticSlot _slot;

        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Rect _lastSafeArea;

        private RectTransform _gridArea;
        private LockerPreviewStage _previewStage;

        private readonly List<CardRefs> _cards = new List<CardRefs>();
        private readonly List<UIInkShapeGraphic> _tabShapes = new List<UIInkShapeGraphic>();
        private readonly List<TextMeshProUGUI> _tabLabels = new List<TextMeshProUGUI>();
        private readonly List<Image> _tabIcons = new List<Image>();
        private readonly List<TextMeshProUGUI> _tabProgress = new List<TextMeshProUGUI>();

        private TextMeshProUGUI _captionText;
        private TextMeshProUGUI _infoNameText;
        private TextMeshProUGUI _infoSubtitleText;
        private TextMeshProUGUI _infoProgressText;
        private UIInkShapeGraphic _titlePillShape;
        private TextMeshProUGUI _titlePillText;
        private Button _closeButton;

        // 退场转场：面板先 PlayOut，收敛后整层淡出再销毁（与 HomeInfoOverlay 同一套语义）。
        private UISpringMotion _panelMotion;
        private CanvasGroup _rootGroup;
        private bool _closing;
        private float _closingElapsed;

        // ==================== 静态入口 ====================

        public static LockerOverlay Show(RectTransform host, UIThemeSO theme, int sortingOrder = 65)
        {
            if (host == null) return null;

            CloseActive();

            // 独立 Overlay Canvas：默认 65，高于地图选择（60）与首页详情（50），
            // 保证衣柜打开时下面所有菜单层都被遮罩吃掉点击。
            var go = new GameObject("LockerOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var overlay = go.AddComponent<LockerOverlay>();
            overlay._host = host;
            overlay._theme = UIThemeSO.Resolve(theme);
            overlay._slot = CosmeticCatalog.Slots.Length > 0 ? CosmeticCatalog.Slots[0] : CosmeticSlot.Outfit;
            overlay.Rebuild();
            overlay.CacheGeometry();
            _instance = overlay;
            return overlay;
        }

        public static void CloseIfOpen()
        {
            if (_instance != null) _instance.Close();
        }

        public static void CloseActive()
        {
            if (_instance != null) _instance.Close();
        }

        /// <summary>
        /// Esc / 手柄 Start 的统一入口：衣柜打开时关闭并标记本帧已消费；
        /// 衣柜已关闭但本帧刚被别的菜单层消费时返回 true，调用方不得再处理同一个输入。
        /// </summary>
        public static bool TryCloseFromMenuInput()
        {
            if (_instance != null)
            {
                _instance.Close();
                MenuInputRouter.MarkConsumed(MenuEscLayer.Locker);
                return true;
            }

            return MenuInputRouter.ConsumedThisFrame;
        }

        // ==================== 生命周期 ====================

        private void Close()
        {
            // 防重入：连点 × / 遮罩 / Esc 只播一次退场，否则会叠加两次 PlayOut。
            if (_closing) return;
            _closing = true;
            _closingElapsed = 0f;

            ClearSelection();

            // 立刻摘掉「打开态」：逻辑层（输入仲裁）与展示层（淡出）解耦，语义与硬切一致。
            if (_instance == this) _instance = null;

            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            if (_panelMotion != null && gameObject.activeInHierarchy)
            {
                _panelMotion.OnPlayedOut -= HandleClosed;
                _panelMotion.OnPlayedOut += HandleClosed;
                _panelMotion.PlayOut();
            }
            else
            {
                HandleClosed();
            }
        }

        /// <summary>退场收敛：先释放预览台（RT / 相机 / 克隆体），再销毁整层。OnPlayedOut 与超时兜底都会调用，故幂等。</summary>
        private void HandleClosed()
        {
            if (!_closing) return;

            if (_panelMotion != null) _panelMotion.OnPlayedOut -= HandleClosed;
            _closing = false;
            _closingElapsed = 0f;

            DisposePreview();

            if (gameObject != null)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        private void TickClosing()
        {
            _closingElapsed += Time.unscaledDeltaTime;

            if (_rootGroup != null)
            {
                // 与面板自身的 alpha 淡出同节奏，否则遮罩会比面板慢半拍留在屏幕上。
                UIThemeSO settings = UIThemeSO.Resolve(_theme);
                float duration = Mathf.Clamp(settings.entranceFadeDuration, 0.05f, 0.3f);
                _rootGroup.alpha = 1f - Mathf.Clamp01(_closingElapsed / duration);
            }

            // 兜底：面板 motion 被外部失活时 OnPlayedOut 永不到来（正常动画 ≤0.3s，这里给 1s 余量）。
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
            DisposePreview();
        }

        private void Update()
        {
            if (_closing)
            {
                TickClosing();
                return;
            }

            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight &&
                Screen.safeArea == _lastSafeArea)
            {
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
            // 预览台持有相机 / RT / 克隆体，销毁子节点前必须先自己收干净，否则 RT 泄漏。
            DisposePreview();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _cards.Clear();
            _tabShapes.Clear();
            _tabLabels.Clear();
            _tabIcons.Clear();
            _tabProgress.Clear();
            _gridArea = null;
            _captionText = null;
            _infoNameText = null;
            _infoSubtitleText = null;
            _infoProgressText = null;
            _titlePillShape = null;
            _titlePillText = null;

            BuildRoot();
        }

        // ==================== 根布局 ====================

        private void BuildRoot()
        {
            bool mobile = UITypography.IsMobileLike;
            LockerMetrics m = Metrics(mobile);
            Vector2 panelSize = ResolvePanelSize(m);

            // 1) 全屏遮罩：墨黑半透明 + raycastTarget，吃掉下面所有菜单的点击；点空白处关闭。
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            scrimGo.transform.SetParent(transform, false);
            Stretch((RectTransform)scrimGo.transform);

            var scrimImage = scrimGo.GetComponent<Image>();
            scrimImage.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.74f);
            scrimImage.raycastTarget = true;

            var scrimButton = scrimGo.GetComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.onClick.AddListener(Close);

            // 2) 居中面板：纸白 + 4px 墨框 + 弹窗硬影；锚点跟随安全区中心，刘海屏不偏。
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = ResolveSafeCenter();
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = panelSize;

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.RoundedRect;
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

            _rootGroup = GetComponent<CanvasGroup>();
            if (_rootGroup == null) _rootGroup = gameObject.AddComponent<CanvasGroup>();
            _rootGroup.alpha = 1f;

            BuildHeader(panel, m, panelSize);

            float contentTop = m.HeaderHeight + m.ContentInset;
            float contentHeight = Mathf.Max(200f, panelSize.y - contentTop - m.FooterHeight);

            BuildTabs(panel, m, contentTop, contentHeight);
            BuildRightColumn(panel, m, panelSize, contentTop, contentHeight);
            BuildFooter(panel, m, panelSize);

            // 手柄：默认选中关闭键，A 直接退出；鼠标 / 触屏不受影响。
            if (EventSystem.current != null && _closeButton != null)
            {
                EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
            }
        }

        private Vector2 ResolvePanelSize(LockerMetrics m)
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

            return new Vector2(
                Mathf.Min(m.PanelMaxWidth, safeWidth * m.PanelWidthRatio),
                Mathf.Min(m.PanelMaxHeight, safeHeight * m.PanelHeightRatio));
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

        // ==================== 头部 / 页脚 ====================

        private void BuildHeader(RectTransform panel, LockerMetrics m, Vector2 panelSize)
        {
            // 顶部身份色带（不承载文字）：内缩到圆角直线段内，胶囊短划。
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
            bandShape.color = ComicStyle.Yellow;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;

            float tagTop = m.Padding + m.TopBandHeight;
            CreateTag(panel, "TabTag", "外观", m.TagSize, UITypography.FontSnapped(m.TagFont),
                ComicStyle.Ink, ComicStyle.Paper, new Vector2(m.Padding, tagTop));

            float titleFont = UITypography.FontSnapped(m.TitleFont);
            float titleX = m.Padding + m.TagSize.x + 18f;
            float titleTop = tagTop + (m.TagSize.y - titleFont * 1.2f) * 0.5f;
            CreateText(panel, "Title", "衣柜", titleFont, true, ComicStyle.Ink,
                new Vector2(titleX, titleTop), new Vector2(320f, titleFont * 1.2f),
                TextAlignmentOptions.MidlineLeft, -1f, FontStyles.Bold);

            // 已解锁总数：标题右侧的次级读数，玩家一眼知道「还剩多少没拿到」。
            float countFont = UITypography.FontSnapped(m.CountFont);
            CreateText(panel, "UnlockCount", FormatUnlockCount(), countFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(titleX, titleTop + (titleFont * 1.2f - countFont * 1.6f) * 0.5f),
                new Vector2(panelSize.x - titleX - m.Padding - m.CloseSize.x - 24f, countFont * 1.6f),
                TextAlignmentOptions.MidlineRight, 1f, FontStyles.Bold);

            _closeButton = CreateCloseButton(panel, m, new Vector2(panelSize.x - m.Padding - m.CloseSize.x, tagTop));

            CreateSolidRect(panel, "HeaderDivider",
                new Vector2(m.Padding, m.HeaderHeight),
                new Vector2(panelSize.x - m.Padding * 2f, 2f),
                ComicStyle.InkSoft, false);
        }

        private void BuildFooter(RectTransform panel, LockerMetrics m, Vector2 panelSize)
        {
            float dividerY = panelSize.y - m.FooterHeight;
            CreateSolidRect(panel, "FooterDivider",
                new Vector2(m.Padding, dividerY),
                new Vector2(panelSize.x - m.Padding * 2f, 2f),
                ComicStyle.InkSoft, false);

            // 队伍墨色是敌我识别红线，不进外观表：这里写清楚，免得玩家到处找「换墨色」。
            float noteFont = UITypography.FontSnapped(m.FooterFont);
            CreateText(panel, "FooterNote", "队伍墨色不可更改（敌我识别）", noteFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, dividerY + (m.FooterHeight - noteFont * 1.6f) * 0.5f),
                new Vector2(panelSize.x * 0.56f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            var doneGo = new GameObject("DoneButton", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion));
            doneGo.transform.SetParent(panel, false);
            var doneRect = (RectTransform)doneGo.transform;
            doneRect.anchorMin = new Vector2(0f, 1f);
            doneRect.anchorMax = new Vector2(0f, 1f);
            doneRect.pivot = new Vector2(0f, 1f);
            doneRect.anchoredPosition = new Vector2(panelSize.x - m.Padding - m.DoneSize.x,
                -(dividerY + (m.FooterHeight - m.DoneSize.y) * 0.5f));
            doneRect.sizeDelta = m.DoneSize;

            var doneShape = doneGo.GetComponent<UIInkShapeGraphic>();
            doneShape.Shape = UIInkShape.RoundedRect;
            doneShape.BorderWidth = 0f;
            doneShape.color = ComicStyle.Ink;
            doneShape.HardShadow = false;
            doneShape.Halftone = false;
            doneShape.raycastTarget = true;
            _theme.ApplyHardShadow(doneShape, UIHardShadowLevel.Button);

            float doneFont = UITypography.FontSnapped(m.DoneFont);
            TextMeshProUGUI doneLabel = CreateText(doneRect, "Label", "完成", doneFont, true, ComicStyle.Paper,
                Vector2.zero, m.DoneSize, TextAlignmentOptions.Center);
            if (doneLabel != null)
            {
                doneLabel.rectTransform.anchorMin = Vector2.zero;
                doneLabel.rectTransform.anchorMax = Vector2.one;
                doneLabel.rectTransform.offsetMin = Vector2.zero;
                doneLabel.rectTransform.offsetMax = Vector2.zero;
            }

            var doneButton = doneGo.GetComponent<Button>();
            doneButton.targetGraphic = doneShape;
            doneButton.transition = Selectable.Transition.None;
            doneButton.onClick.AddListener(Close);

            var doneMotion = doneGo.GetComponent<UISpringMotion>();
            doneMotion.SetTheme(_theme);

            var jelly = doneGo.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, doneButton, doneShape);
            if (doneLabel != null) jelly.SetPressContent(doneLabel.rectTransform);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Button);

            UISoundBinder.EnsureBinder(doneButton);
        }

        // ==================== 左侧槽位页签 ====================

        private void BuildTabs(RectTransform panel, LockerMetrics m, float contentTop, float contentHeight)
        {
            CosmeticSlot[] slots = CosmeticCatalog.Slots;
            PlayerProfile profile = Profile;

            float tabHeight = (contentHeight - (slots.Length - 1) * m.TabGap) / Mathf.Max(1, slots.Length);
            tabHeight = Mathf.Max(64f, Mathf.Min(m.TabHeight, tabHeight));

            for (int i = 0; i < slots.Length; i++)
            {
                CosmeticSlot slot = slots[i];
                bool active = slot == _slot;
                var topLeft = new Vector2(m.Padding, contentTop + i * (tabHeight + m.TabGap));

                var go = new GameObject("Tab_" + slot, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(UIInkShapeGraphic), typeof(Button));
                go.transform.SetParent(panel, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
                rect.sizeDelta = new Vector2(m.TabWidth, tabHeight);

                var shape = go.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.Pill; // 竖排胶囊页签：与 HomeInfo 的页签条同一套语言
                shape.BorderWidth = active ? 0f : ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.Ink;
                shape.color = active ? ComicStyle.Ink : ComicStyle.Paper;
                shape.HardShadow = false;
                shape.Halftone = false;
                shape.raycastTarget = true;
                if (active) _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);
                _tabShapes.Add(shape);

                float iconSize = Mathf.Min(m.TabIcon, tabHeight - 20f);
                float iconLeft = 16f;
                _tabIcons.Add(BuildTabIcon(rect, CosmeticCatalog.SlotIcon(slot),
                    new Vector2(iconLeft, (tabHeight - iconSize) * 0.5f), iconSize, active));

                float labelFont = UITypography.FontSnapped(m.TabFont);
                float badgeFont = UITypography.FontSnapped(m.TabBadgeFont);
                float textLeft = iconLeft + iconSize + 12f;
                float textWidth = Mathf.Max(40f, m.TabWidth - textLeft - 12f);

                // 上半：槽位名；下半：该槽进度角标。两段各占一行，避免叠字。
                TextMeshProUGUI label = CreateText(rect, "Label", CosmeticCatalog.SlotLabel(slot), labelFont, true,
                    active ? ComicStyle.Paper : ComicStyle.Ink,
                    new Vector2(textLeft, tabHeight * 0.5f - labelFont * 1.35f - 2f),
                    new Vector2(textWidth, labelFont * 1.35f),
                    TextAlignmentOptions.MidlineLeft);
                _tabLabels.Add(label);

                CosmeticUnlockMath.SlotProgress(slot, profile, out int unlocked, out int total);
                _tabProgress.Add(CreateText(rect, "Progress", unlocked + "/" + total, badgeFont, false,
                    active ? ComicStyle.WithAlpha(ComicStyle.Paper, 0.72f) : ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                    new Vector2(textLeft, tabHeight * 0.5f + 2f),
                    new Vector2(textWidth, badgeFont * 1.35f),
                    TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold));

                var button = go.GetComponent<Button>();
                button.targetGraphic = shape;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectSlot(slot));

                var jelly = go.AddComponent<UIJellyButton>();
                jelly.Configure(_theme, button, shape);
                if (label != null) jelly.SetPressContent(label.rectTransform);

                UISoundBinder.EnsureBinder(button);
            }
        }

        /// <summary>页签图标：取不到图标就返回 null（调用方不得留下白块）。</summary>
        private static Image BuildTabIcon(RectTransform parent, string iconName, Vector2 topLeft, float size, bool active)
        {
            if (!UIIconLibrary.TryGet(iconName, out Sprite icon))
            {
                return null; // 取不到图标就不建 Image：绝不留白方块
            }

            var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.sprite = icon;
            image.color = active ? ComicStyle.Paper : ComicStyle.Ink;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>切页签：只重建右侧网格与预览状态，不整层 Rebuild（整层重建会闪一下入场动画）。</summary>
        private void SelectSlot(CosmeticSlot slot)
        {
            if (_slot == slot) return;
            _slot = slot;

            // 页签选中态就地刷新：墨底纸字 ↔ 纸底墨字，阴影硬切（不补间）。
            CosmeticSlot[] slots = CosmeticCatalog.Slots;
            for (int i = 0; i < slots.Length && i < _tabShapes.Count; i++)
            {
                bool active = slots[i] == _slot;
                UIInkShapeGraphic shape = _tabShapes[i];
                if (shape == null) continue;
                shape.color = active ? ComicStyle.Ink : ComicStyle.Paper;
                shape.BorderWidth = active ? 0f : ComicStyle.BorderThin;
                _theme.ApplyHardShadow(shape, active ? UIHardShadowLevel.Tag : UIHardShadowLevel.None);
                shape.Refresh();

                if (i < _tabLabels.Count && _tabLabels[i] != null)
                {
                    _tabLabels[i].color = active ? ComicStyle.Paper : ComicStyle.Ink;
                }

                if (i < _tabProgress.Count && _tabProgress[i] != null)
                {
                    _tabProgress[i].color = active
                        ? ComicStyle.WithAlpha(ComicStyle.Paper, 0.72f)
                        : ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f);
                }

                if (i < _tabIcons.Count && _tabIcons[i] != null)
                {
                    _tabIcons[i].color = active ? ComicStyle.Paper : ComicStyle.Ink;
                }
            }

            RebuildGrid();
            RefreshCaption();
            RefreshInfoPanel();
        }

        // ==================== 右侧：预览 + 网格 ====================

        private void BuildRightColumn(RectTransform panel, LockerMetrics m, Vector2 panelSize,
            float contentTop, float contentHeight)
        {
            float rightX = m.Padding + m.TabWidth + m.ColumnGap;
            float rightWidth = Mathf.Max(240f, panelSize.x - m.Padding * 2f - m.TabWidth - m.ColumnGap);

            var columnGo = new GameObject("RightColumn", typeof(RectTransform));
            columnGo.transform.SetParent(panel, false);
            var column = (RectTransform)columnGo.transform;
            column.anchorMin = new Vector2(0f, 1f);
            column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(rightX, -contentTop);
            column.sizeDelta = new Vector2(rightWidth, contentHeight);

            float previewHeight = Mathf.Clamp(contentHeight * 0.52f, 220f, m.PreviewHeightMax);
            RectTransform previewBlock = CreateBlock(column, "PreviewBlock", Vector2.zero,
                new Vector2(rightWidth, previewHeight), ComicStyle.PaperAlt, ComicStyle.Yellow, 5f,
                UIHardShadowLevel.None);

            // 取景框固定 3:4 竖幅（与 RT 768×1024 同比例，拉伸会把人压扁）。
            // 「取景框 + 当前装备信息列」作为一组在预览块内水平居中：
            // 预览块本身铺满整行（与网格同宽），不然右侧会多出一条没有信息的纸灰边。
            float innerHeight = Mathf.Max(120f, previewHeight - 32f);
            float innerWidth = innerHeight * 0.75f;
            float infoWidth = Mathf.Max(0f, Mathf.Min(m.InfoColumnWidth, rightWidth - innerWidth - 64f));
            float groupWidth = 16f + innerWidth + (infoWidth > 0f ? 16f + infoWidth : 0f) + 16f;
            float groupX = Mathf.Max(16f, (rightWidth - groupWidth) * 0.5f);

            var viewportGo = new GameObject("PreviewViewport", typeof(RectTransform));
            viewportGo.transform.SetParent(previewBlock, false);
            var viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = new Vector2(0f, 0.5f);
            viewport.anchorMax = new Vector2(0f, 0.5f);
            viewport.pivot = new Vector2(0f, 0.5f);
            viewport.anchoredPosition = new Vector2(groupX, 0f);
            viewport.sizeDelta = new Vector2(innerWidth, innerHeight);

            // 3D 预览台：克隆本地玩家角色到场地外单独取景（不在真实出生点拍，避免拍到 Bot / 掩体）。
            _previewStage = LockerPreviewStage.Create(viewport, ResolveLocalTeam());
            RefreshPreviewSet();

            // 称号徽章：2D 小 pill 叠在取景框顶部（3D 里不画文字，文字永远走 UI 层）。
            // 挂 previewBlock 而不是取景框：取景框带 RectMask2D，硬影会被裁掉一半。
            BuildTitlePill(previewBlock, m, groupX, innerWidth);

            // 「当前装备」信息列接在取景框右侧；宽度不够时整列不画（宁可留白也不挤成一竖条）。
            float infoLeft = groupX + innerWidth + 16f;
            BuildInfoColumn(previewBlock, m, infoWidth, infoLeft, previewHeight);

            float captionTop = previewHeight + 8f;
            float captionFont = UITypography.FontSnapped(m.CaptionFont);
            CreateText(column, "CaptionHint", "拖拽旋转", captionFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(4f, captionTop), new Vector2(rightWidth * 0.5f, m.CaptionHeight),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            _captionText = CreateText(column, "CaptionCount", string.Empty, captionFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(0f, captionTop), new Vector2(rightWidth - 4f, m.CaptionHeight),
                TextAlignmentOptions.MidlineRight, 0.5f, FontStyles.Bold);
            RefreshCaption();

            float gridTop = captionTop + m.CaptionHeight + 12f;
            float gridHeight = contentHeight - gridTop;
            if (gridHeight < 96f)
            {
                // 面板被压得很矮（极端窄窗）时宁可不画网格，也不画一排半张卡片。
                return;
            }

            var gridGo = new GameObject("GridArea", typeof(RectTransform));
            gridGo.transform.SetParent(column, false);
            var grid = (RectTransform)gridGo.transform;
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(0f, 1f);
            grid.pivot = new Vector2(0f, 1f);
            grid.anchoredPosition = new Vector2(0f, -gridTop);
            grid.sizeDelta = new Vector2(rightWidth, gridHeight);
            _gridArea = grid;

            BuildGrid(m);
        }

        /// <summary>预览右侧的「当前装备」信息列：换装后立刻回读，玩家不必去网格里找那一张卡。</summary>
        private void BuildInfoColumn(RectTransform previewBlock, LockerMetrics m, float width,
            float left, float previewHeight)
        {
            if (width < 160f) return;

            float top = 24f;

            CreateTag(previewBlock, "CurrentTag", "当前", m.TagSize, UITypography.FontSnapped(m.TagFont),
                ComicStyle.Ink, ComicStyle.Paper, new Vector2(left, top));

            float nameFont = UITypography.FontSnapped(m.InfoNameFont);
            float nameTop = top + m.TagSize.y + 16f;
            _infoNameText = CreateText(previewBlock, "EquippedName", string.Empty, nameFont, true, ComicStyle.Ink,
                new Vector2(left, nameTop), new Vector2(width, nameFont * 2.6f),
                TextAlignmentOptions.TopLeft, -1f, FontStyles.Bold, wrapping: true);

            float subFont = UITypography.FontSnapped(m.InfoSubFont);
            _infoSubtitleText = CreateText(previewBlock, "EquippedSubtitle", string.Empty, subFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.66f),
                new Vector2(left, nameTop + nameFont * 2.7f), new Vector2(width, subFont * 2.6f),
                TextAlignmentOptions.TopLeft, 0.5f, FontStyles.Bold, wrapping: true);

            _infoProgressText = CreateText(previewBlock, "EquippedProgress", string.Empty,
                UITypography.FontSnapped(m.InfoTitleFont), true, ComicStyle.Ink,
                new Vector2(left, previewHeight - 24f - UITypography.FontSnapped(m.InfoTitleFont) * 1.4f),
                new Vector2(width, UITypography.FontSnapped(m.InfoTitleFont) * 1.4f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            RefreshInfoPanel();
        }

        private void BuildTitlePill(RectTransform previewBlock, LockerMetrics m, float left, float width)
        {
            var go = new GameObject("TitlePill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(previewBlock, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -12f);

            float pillWidth = Mathf.Min(m.TitlePillSize.x, Mathf.Max(120f, width));
            rect.sizeDelta = new Vector2(pillWidth, m.TitlePillSize.y);

            _titlePillShape = go.GetComponent<UIInkShapeGraphic>();
            _titlePillShape.Shape = UIInkShape.Pill;
            _titlePillShape.BorderWidth = ComicStyle.BorderThin;
            _titlePillShape.BorderColor = ComicStyle.Ink;
            _titlePillShape.HardShadow = false;
            _titlePillShape.Halftone = false;
            _titlePillShape.raycastTarget = false;
            _theme.ApplyHardShadow(_titlePillShape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.TitlePillFont);
            _titlePillText = CreateText(rect, "Text", string.Empty, font, true, ComicStyle.Ink,
                Vector2.zero, rect.sizeDelta, TextAlignmentOptions.Center, 1f, FontStyles.Bold);
            if (_titlePillText != null)
            {
                _titlePillText.rectTransform.anchorMin = Vector2.zero;
                _titlePillText.rectTransform.anchorMax = Vector2.one;
                _titlePillText.rectTransform.offsetMin = new Vector2(10f, 0f);
                _titlePillText.rectTransform.offsetMax = new Vector2(-10f, 0f);
            }

            RefreshTitlePill();
        }

        private void RefreshTitlePill()
        {
            if (_titlePillShape == null || _titlePillText == null) return;

            CosmeticSet set = CosmeticUnlockMath.ResolveSet(Profile);
            // 称号文案/色阶在 CosmeticDef.Title 载荷里（TitleDef），不是直接挂在 CosmeticDef 上
            string text = string.IsNullOrEmpty(set.Title.Title.Text) ? "无称号" : set.Title.Title.Text;
            Color fill = TitleTierColor(set.Title.Title.Tier);

            _titlePillShape.color = fill;
            _titlePillShape.Refresh();
            _titlePillText.text = text;
            _titlePillText.color = ComicStyle.ReadableTextOn(fill);
            TMP_FontAsset font = _theme.ResolveFont(true, text);
            if (font != null) _titlePillText.font = font;
        }

        private void RefreshCaption()
        {
            if (_captionText == null) return;
            CosmeticUnlockMath.SlotProgress(_slot, Profile, out int unlocked, out int total);
            _captionText.text = "已解锁 " + unlocked + "/" + total;
        }

        /// <summary>「当前」列：回读档案里该槽实际生效的那件（已做解锁回退，不会显示非法 id）。</summary>
        private void RefreshInfoPanel()
        {
            PlayerProfile profile = Profile;
            CosmeticSet set = CosmeticUnlockMath.ResolveSet(profile);
            CosmeticDef def = set.Get(_slot);

            if (_infoNameText != null)
            {
                string name = string.IsNullOrEmpty(def.DisplayName) ? "未设置" : def.DisplayName;
                _infoNameText.text = name;
                TMP_FontAsset font = _theme.ResolveFont(true, name);
                if (font != null) _infoNameText.font = font;
            }

            if (_infoSubtitleText != null)
            {
                string sub = def.Subtitle ?? string.Empty;
                _infoSubtitleText.text = sub;
                TMP_FontAsset font = _theme.ResolveFont(false, sub);
                if (font != null) _infoSubtitleText.font = font;
            }

            if (_infoProgressText != null)
            {
                CosmeticUnlockMath.SlotProgress(_slot, profile, out int unlocked, out int total);
                string text = unlocked + " / " + total;
                _infoProgressText.text = text;
                TMP_FontAsset font = _theme.ResolveFont(true, text);
                if (font != null) _infoProgressText.font = font;
            }
        }

        // ==================== 外观卡片网格 ====================

        private void RebuildGrid()
        {
            if (_gridArea == null) return;

            for (int i = _gridArea.childCount - 1; i >= 0; i--)
            {
                GameObject child = _gridArea.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _cards.Clear();
            BuildGrid(Metrics(UITypography.IsMobileLike));
        }

        private void BuildGrid(LockerMetrics m)
        {
            RectTransform parent = _gridArea;
            if (parent == null) return;

            CosmeticDef[] defs = CosmeticCatalog.ForSlot(_slot);
            bool mobile = UITypography.IsMobileLike;

            // 移动端少一列（桌面 3 → 移动 2）：卡片更宽，中文名 + 副标题不必缩字号。
            int columns = Mathf.Max(1, mobile ? Mathf.Max(2, m.GridColumns - 1) : m.GridColumns);
            float gap = m.CardGap;
            float cardWidth = Mathf.Max(120f, (parent.sizeDelta.x - (columns - 1) * gap) / columns);
            float cardHeight = m.CardHeight;
            int rows = Mathf.Max(1, (defs.Length + columns - 1) / columns);
            float contentHeight = rows * cardHeight + Mathf.Max(0, rows - 1) * gap + 8f;

            // 滚动容器：RectMask2D 裁剪。卡片**不开硬影**——硬影会被视口裁掉，规范也禁止在滚动区内加硬影。
            var scrollGo = new GameObject("CardScroll", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            var scrollRectT = (RectTransform)scrollGo.transform;
            Stretch(scrollRectT);

            var scrollImage = scrollGo.GetComponent<Image>();
            scrollImage.color = Color.clear; // 透明拖拽热区
            scrollImage.raycastTarget = true;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            Stretch(viewportRt);
            scrollRect.viewport = viewportRt;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, contentHeight);
            scrollRect.content = contentRt;

            PlayerProfile profile = Profile;
            string equippedId = CosmeticUnlockMath.ResolveEquipped(_slot,
                profile != null ? profile.GetEquippedId(_slot) : null, profile);

            for (int i = 0; i < defs.Length; i++)
            {
                int col = i % columns;
                int row = i / columns;
                var position = new Vector2(col * (cardWidth + gap), row * (cardHeight + gap) + 2f);

                bool unlocked = CosmeticUnlockMath.IsUnlocked(defs[i], profile);
                bool equipped = string.Equals(defs[i].Id, equippedId, StringComparison.Ordinal);
                _cards.Add(BuildCard(contentRt, m, defs[i], unlocked, equipped, position,
                    new Vector2(cardWidth, cardHeight), i));
            }
        }

        private CardRefs BuildCard(RectTransform parent, LockerMetrics m, in CosmeticDef def, bool unlocked,
            bool equipped, Vector2 topLeft, Vector2 size, int index)
        {
            var card = new CardRefs
            {
                Def = def,
                Unlocked = unlocked,
                // 抖动复位用真实 anchoredPosition（顶左约定下 y 取负），存 topLeft 会让卡片弹到反方向。
                Home = new Vector2(topLeft.x, -topLeft.y),
            };

            var go = new GameObject("Card_" + def.Id, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;
            card.Root = rect;

            Color fill = unlocked ? ComicStyle.Paper : ComicStyle.PaperAlt;
            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.BorderWidth = equipped ? ComicStyle.Border : ComicStyle.BorderThin;
            shape.BorderColor = unlocked ? ComicStyle.Ink : ComicStyle.WithAlpha(ComicStyle.Ink, 0.35f);
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = true;
            card.Shape = shape;

            // 顶部 5px 胶囊身份色带：已解锁用该外观主色，未解锁压成灰带（保留骨架但不抢视线）。
            // 内缩量按最大边框（4px）算：选中态把边框从 3 加到 4 时色带不会戳出圆角轮廓。
            Color accent = unlocked ? ResolveCardAccent(def) : ComicStyle.WithAlpha(ComicStyle.Ink, 0.18f);
            BuildCardBand(rect, size, accent, ComicStyle.Border);

            float pad = 16f;
            float textTop = CardBandHeight + 14f;
            float nameFont = UITypography.FontSnapped(m.CardNameFont);
            float subFont = UITypography.FontSnapped(m.CardSubFont);

            // 未解锁卡的文字仍要 ≥4.5:1：先由 ReadableTextOn 取可读基色，再略微降透明度表达「退后一步」，
            // 绝不直接把文字调成浅灰（那样在纸灰底上会掉到 2:1 以下）。
            Color readable = ComicStyle.ReadableTextOn(fill);
            Color nameColor = unlocked ? ComicStyle.Ink : ComicStyle.WithAlpha(readable, 0.88f);
            Color subColor = unlocked
                ? ComicStyle.WithAlpha(ComicStyle.Ink, 0.66f)
                : ComicStyle.WithAlpha(readable, 0.70f);

            float reserveRight = 0f;
            if (unlocked)
            {
                // 「穿着中」胶囊：每张已解锁卡都建好，靠 SetActive 切换——
                // 只给当前装备的那张建，换装后旧卡就没有角标可显示（换装是常态操作）。
                Vector2 tagSize = new Vector2(96f, 30f);
                card.EquippedTag = CreatePill(rect, "EquippedTag", "穿着中", tagSize,
                    new Vector2(size.x - pad - tagSize.x, textTop - 2f),
                    ComicStyle.Yellow, ComicStyle.Ink, ComicStyle.BorderThin);
                card.EquippedTag.gameObject.SetActive(equipped);
                reserveRight = tagSize.x + 10f;
            }
            else
            {
                BuildLockGlyph(rect, size, pad, textTop);
                reserveRight = 36f;
            }

            CreateText(rect, "Name", def.DisplayName, nameFont, true, nameColor,
                new Vector2(pad, textTop),
                new Vector2(Mathf.Max(40f, size.x - pad * 2f - reserveRight), nameFont * 1.3f),
                TextAlignmentOptions.MidlineLeft);

            CreateText(rect, "Subtitle", def.Subtitle, subFont, false, subColor,
                new Vector2(pad, textTop + nameFont * 1.3f + 4f),
                new Vector2(size.x - pad * 2f, subFont * 1.5f),
                TextAlignmentOptions.MidlineLeft);

            if (!unlocked)
            {
                // 解锁条件（「等级 7 解锁」/「成就：压哨先生」）贴卡片底部，比副标题更靠边。
                float hintFont = UITypography.FontSnapped(m.CardHintFont);
                CreateText(rect, "Hint", CosmeticUnlockMath.UnlockHint(def), hintFont, true,
                    ComicStyle.WithAlpha(readable, 0.78f),
                    new Vector2(pad, size.y - pad - hintFont * 1.35f),
                    new Vector2(size.x - pad * 2f, hintFont * 1.35f),
                    TextAlignmentOptions.MidlineLeft);
            }

            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);
            motion.Build();
            card.Motion = motion;

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            CosmeticDef captured = def;
            bool capturedUnlocked = unlocked;
            button.onClick.AddListener(() => OnCardClicked(card, captured, capturedUnlocked));
            UISoundBinder.EnsureBinder(button);

            // 入场：0.05s/张 错峰 pop-in；带延迟的先把 alpha 压 0，避免「先整张显示、轮到自己再消失」。
            float stagger = Mathf.Min(_theme.entranceStagger, 0.05f);
            float delay = index * stagger;
            if (delay > 0.001f)
            {
                CanvasGroup group = go.GetComponent<CanvasGroup>();
                if (group == null) group = go.AddComponent<CanvasGroup>();
                group.alpha = 0f;
            }
            motion.PlayIn(delay);

            return card;
        }

        private void BuildCardBand(RectTransform card, Vector2 size, Color accent, float borderWidth)
        {
            float radius = ComicStyle.CornerFor(size.x, size.y);
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(card, false);
            bandGo.transform.SetAsFirstSibling();
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-(radius + borderWidth) * 2f, CardBandHeight);
            band.anchoredPosition = new Vector2(0f, -borderWidth);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.Pill;
            bandShape.BorderWidth = 0f;
            bandShape.color = accent;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;
        }

        /// <summary>未解锁卡的锁图标：右上角（与已解锁卡的「穿着中」胶囊同一位置，两种卡片的右上角语言一致）。</summary>
        private void BuildLockGlyph(RectTransform card, Vector2 size, float pad, float top)
        {
            if (!UIIconLibrary.TryGet("glyph_lock", out Sprite lockIcon))
            {
                return; // 取不到图标就什么都不画（禁止白块）
            }

            var go = new GameObject("LockIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(card, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(size.x - pad - 26f, -top);
            rect.sizeDelta = new Vector2(26f, 26f); // ≥24px：再小就糊成噪点

            var image = go.GetComponent<Image>();
            image.sprite = lockIcon;
            image.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f);
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private void OnCardClicked(CardRefs card, in CosmeticDef def, bool unlocked)
        {
            if (card == null) return;

            if (!unlocked)
            {
                // 未解锁：抖一下，明确「现在穿不上」，不写任何存档。
                PlayDeniedFeedback(card);
                return;
            }

            PlayerProfile profile = Profile;
            if (profile == null) return;

            // 一步到位：立即写入档案并落盘，预览与卡片选中态当场刷新（不做「选中 → 再点穿上」两步）。
            profile.SetEquippedId(def.Slot, def.Id);
            profile.Save();

            RefreshPreviewSet();
            RefreshTitlePill();
            RefreshEquippedState();
            RefreshInfoPanel();

            // 场上那个真实角色也要同步换装：否则玩家在衣柜里戴好帽子，回到对局还是旧的
            // （之前只在预览里变，要等下一局重开才生效）。CharacterVisualController 挂在 Player 的子节点
            // VisualRoot 上，所以用 GetComponentInChildren 而不是 GetComponent。
            P1.Player.PlayerController localPlayer = P1.Player.PlayerController.LocalPlayer;
            if (localPlayer != null)
            {
                var visual = localPlayer.GetComponentInChildren<P1.Player.CharacterVisualController>(true);
                if (visual != null) visual.ApplyLocalPlayerCosmetics();
            }

            if (card.Motion != null) card.Motion.Punch(0.06f);
        }

        private void RefreshEquippedState()
        {
            PlayerProfile profile = Profile;
            string equippedId = CosmeticUnlockMath.ResolveEquipped(_slot,
                profile != null ? profile.GetEquippedId(_slot) : null, profile);

            for (int i = 0; i < _cards.Count; i++)
            {
                CardRefs card = _cards[i];
                if (card == null || card.Root == null) continue;

                bool selected = card.Unlocked && string.Equals(card.Def.Id, equippedId, StringComparison.Ordinal);
                if (card.Shape != null)
                {
                    // 选中态 = 4px 墨色厚描边（WeaponCardView 的选中语言），未选中回到 3px。
                    card.Shape.BorderWidth = selected ? ComicStyle.Border : ComicStyle.BorderThin;
                    card.Shape.BorderColor = card.Unlocked
                        ? ComicStyle.Ink
                        : ComicStyle.WithAlpha(ComicStyle.Ink, 0.35f);
                    card.Shape.Refresh();
                }

                if (card.EquippedTag != null)
                {
                    card.EquippedTag.gameObject.SetActive(selected);
                }
            }
        }

        /// <summary>
        /// 未解锁的拒绝反馈：横向抖动 + 一记缩放脉冲。
        /// 规范禁止「摆动」当装饰，但这是功能性告警（操作被拒绝），允许短促、有限次。
        /// </summary>
        private void PlayDeniedFeedback(CardRefs card)
        {
            if (card.Motion != null) card.Motion.Punch(0.05f);
            if (card.Root == null || !isActiveAndEnabled) return;
            StartCoroutine(DeniedShakeRoutine(card.Root, card.Home));
        }

        private IEnumerator DeniedShakeRoutine(RectTransform target, Vector2 home)
        {
            const float duration = 0.18f;
            const float amplitude = 9f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (target == null) yield break;

                float k = 1f - Mathf.Clamp01(elapsed / duration);
                float x = Mathf.Sin(elapsed / duration * Mathf.PI * 6f) * amplitude * k;
                target.anchoredPosition = home + new Vector2(x, 0f);
                yield return null;
            }

            if (target != null) target.anchoredPosition = home;
        }

        // ==================== 预览 / 数据 ====================

        private void RefreshPreviewSet()
        {
            if (_previewStage == null) return;
            _previewStage.SetCosmetics(CosmeticUnlockMath.ResolveSet(Profile));
        }

        private void DisposePreview()
        {
            if (_previewStage == null) return;
            _previewStage.Dispose();
            _previewStage = null;
        }

        private PlayerProfile Profile
        {
            get
            {
                try
                {
                    return PlayerProfile.Current;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[LockerOverlay] 读取玩家档案失败：" + ex.Message);
                    return null;
                }
            }
        }

        private string FormatUnlockCount()
        {
            PlayerProfile profile = Profile;
            int unlocked = profile != null ? CosmeticUnlockMath.UnlockedCount(profile) : 0;
            return "已解锁 " + unlocked + "/" + CosmeticCatalog.Count;
        }

        private static Team ResolveLocalTeam()
        {
            PlayerController player = PlayerController.LocalPlayer;
            if (player == null) return Team.TeamA;
            CombatantHealth health = player.GetComponent<CombatantHealth>();
            return health != null ? health.Team : Team.TeamA;
        }

        /// <summary>卡片身份色：服装=上衣、武器涂装=枪身、头饰=帽体、发型=队伍橙、称号=段位阶色。</summary>
        private Color ResolveCardAccent(in CosmeticDef def)
        {
            switch (def.Slot)
            {
                case CosmeticSlot.Outfit:
                    return def.Outfit.Shirt.a > 0.01f ? def.Outfit.Shirt : ComicStyle.Ink;
                case CosmeticSlot.Hair:
                    return _theme != null ? _theme.teamAColor : ComicStyle.Yellow;
                case CosmeticSlot.Headgear:
                    // 「不戴」的 HeadgearDef 是 default（Main 全透明），没有身份色就退回墨黑。
                    return def.Headgear.Main.a > 0.01f ? def.Headgear.Main : ComicStyle.Ink;
                case CosmeticSlot.WeaponSkin:
                    return def.WeaponSkin.Body.a > 0.01f ? def.WeaponSkin.Body : ComicStyle.Ink;
                case CosmeticSlot.Title:
                    return TitleTierColor(def.Title.Tier);
                default:
                    return ComicStyle.Ink;
            }
        }

        /// <summary>称号阶色（0..4 段位色阶 / 5 成就专属金）。</summary>
        private static Color TitleTierColor(int tier)
        {
            switch (tier)
            {
                case 0: return ComicStyle.Ink;
                case 1: return ComicStyle.Green;
                case 2: return ComicStyle.Blue;
                case 3: return ComicStyle.Red;
                case 4: return ComicStyle.Yellow;
                default: return ComicStyle.Gold;
            }
        }

        // ==================== 基础图元 ====================

        private Button CreateCloseButton(RectTransform panel, LockerMetrics m, Vector2 topLeft)
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
            shape.Shape = UIInkShape.Pill; // 关闭键 = 正圆
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.Ink;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.CloseFont);
            TextMeshProUGUI label = CreateText(rect, "Label", "×", font, true, ComicStyle.Paper,
                Vector2.zero, m.CloseSize, TextAlignmentOptions.Center, 0f, FontStyles.Bold);
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

        private RectTransform CreatePill(Transform parent, string name, string label, Vector2 size,
            Vector2 topLeft, Color fill, Color textColor, float borderWidth)
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
            shape.Shape = UIInkShape.Pill;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            float font = UITypography.FontSnapped(16f);
            TextMeshProUGUI tmp = CreateText(rect, "Text", label, font, true, textColor,
                Vector2.zero, size, TextAlignmentOptions.Center, 1f, FontStyles.Bold);
            if (tmp != null)
            {
                tmp.rectTransform.anchorMin = Vector2.zero;
                tmp.rectTransform.anchorMax = Vector2.one;
                tmp.rectTransform.offsetMin = new Vector2(8f, 0f);
                tmp.rectTransform.offsetMax = new Vector2(-8f, 0f);
            }

            return rect;
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
            shape.Shape = UIInkShape.Pill;
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            CreateText(rect, "Text", label, font, true, textColor,
                new Vector2(8f, 0f), new Vector2(Mathf.Max(20f, size.x - 16f), size.y),
                TextAlignmentOptions.Center, 1.5f, FontStyles.Bold);
            return rect;
        }

        private RectTransform CreateBlock(Transform parent, string name, Vector2 topLeft, Vector2 size,
            Color fill, Color accent, float accentHeight, UIHardShadowLevel shadowLevel)
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
            shape.Shape = UIInkShape.RoundedRect;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = false;
            if (shadowLevel != UIHardShadowLevel.None) _theme.ApplyHardShadow(shape, shadowLevel);

            if (accentHeight > 0f)
            {
                float radius = ComicStyle.CornerFor(size.x, size.y);
                var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bandGo.transform.SetParent(rect, false);
                var band = (RectTransform)bandGo.transform;
                band.anchorMin = new Vector2(0f, 1f);
                band.anchorMax = new Vector2(1f, 1f);
                band.pivot = new Vector2(0.5f, 1f);
                band.sizeDelta = new Vector2(-(radius + ComicStyle.BorderThin) * 2f, accentHeight);
                band.anchoredPosition = new Vector2(0f, -ComicStyle.BorderThin);

                var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
                bandShape.Shape = UIInkShape.Pill;
                bandShape.BorderWidth = 0f;
                bandShape.color = accent;
                bandShape.HardShadow = false;
                bandShape.Halftone = false;
                bandShape.raycastTarget = false;
            }

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
            shape.Shape = UIInkShape.RoundedRect; // 1~2px 细线会被圆角钳成直角，视觉上仍是分隔线
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
            float spacing = 0f, FontStyles style = FontStyles.Bold, bool wrapping = false)
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
            // 紧凑盒子里禁用 Truncate：TMP 会按行高做纵向截断，整段字直接消失。
            tmp.textWrappingMode = wrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
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

        // ==================== 度量表 ====================

        /// <summary>
        /// 桌面 / 移动端两套盒子。字号只给基础值，交给 UITypography 统一 ×1.28；
        /// 只有盒子变大——否则字号被放大两次，长中文名会顶出卡片。
        /// </summary>
        private static LockerMetrics Metrics(bool mobile)
        {
            if (!mobile)
            {
                return new LockerMetrics
                {
                    PanelMaxWidth = 1600f,
                    PanelMaxHeight = 920f,
                    PanelWidthRatio = 0.94f,
                    PanelHeightRatio = 0.90f,
                    Padding = 36f,
                    HeaderHeight = 132f,
                    ContentInset = 16f,
                    FooterHeight = 88f,
                    TopBandHeight = 8f,

                    TagSize = new Vector2(128f, 40f),
                    TagFont = 18f,
                    TitleFont = 44f,
                    CountFont = 20f,
                    CloseSize = new Vector2(72f, 72f),
                    CloseFont = 36f,

                    TabWidth = 224f,
                    TabHeight = 76f,
                    TabGap = 12f,
                    TabIcon = 32f,
                    TabFont = 20f,
                    TabBadgeFont = 18f,
                    ColumnGap = 28f,

                    PreviewHeightMax = 400f,
                    CaptionHeight = 30f,
                    CaptionFont = 18f,
                    TitlePillSize = new Vector2(240f, 40f),
                    TitlePillFont = 20f,
                    InfoTitleFont = 20f,
                    InfoNameFont = 40f,
                    InfoSubFont = 20f,
                    InfoColumnWidth = 300f,

                    CardHeight = 124f,
                    CardGap = 12f,
                    GridColumns = 3,
                    CardNameFont = 22f,
                    CardSubFont = 18f,
                    CardHintFont = 18f,

                    FooterFont = 18f,
                    DoneSize = new Vector2(240f, 60f),
                    DoneFont = 28f,
                };
            }

            return new LockerMetrics
            {
                PanelMaxWidth = 2400f,
                PanelMaxHeight = 1200f,
                PanelWidthRatio = 0.94f,
                PanelHeightRatio = 0.92f,
                Padding = 44f,
                HeaderHeight = 168f,
                ContentInset = 20f,
                FooterHeight = 112f,
                TopBandHeight = 10f,

                TagSize = new Vector2(170f, 52f),
                TagFont = 18f,
                TitleFont = 44f,
                CountFont = 20f,
                CloseSize = new Vector2(92f, 92f),
                CloseFont = 36f,

                TabWidth = 300f,
                TabHeight = 104f,
                TabGap = 14f,
                TabIcon = 44f,
                TabFont = 20f,
                TabBadgeFont = 18f,
                ColumnGap = 32f,

                PreviewHeightMax = 460f,
                CaptionHeight = 40f,
                CaptionFont = 18f,
                TitlePillSize = new Vector2(320f, 52f),
                TitlePillFont = 20f,
                InfoTitleFont = 20f,
                InfoNameFont = 40f,
                InfoSubFont = 20f,
                InfoColumnWidth = 400f,

                CardHeight = 168f,
                CardGap = 16f,
                GridColumns = 2,
                CardNameFont = 22f,
                CardSubFont = 18f,
                CardHintFont = 18f,

                FooterFont = 18f,
                DoneSize = new Vector2(320f, 84f),
                DoneFont = 28f,
            };
        }
    }
}
