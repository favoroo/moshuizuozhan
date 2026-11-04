using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 首页地图选择浮层（运行时装配，不改场景 YAML）：
    /// 两张地图的大选项卡 + 程序化小预览；点另一张 = 写存档并真实加载对应场景。
    /// 静态 <see cref="IsOpen"/> / <see cref="MenuInputConsumedThisFrame"/> 给开赛快捷键、
    /// 暂停菜单与武器抽屉做同帧输入仲裁。
    /// </summary>
    [DisallowMultipleComponent]
    public class MapSelectOverlay : MonoBehaviour
    {
        private static MapSelectOverlay _instance;

        public static bool IsOpen => _instance != null;

        public static bool MenuInputConsumedThisFrame => MenuInputRouter.ConsumedThisFrame;

        private RectTransform _host;
        private UIThemeSO _theme;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Rect _lastSafeArea;

        public static MapSelectOverlay Show(RectTransform host, UIThemeSO theme)
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
            if (_instance == this) _instance = null;
            ClearSelection();
            if (gameObject != null)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
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
            panelShape.Shape = UIInkShape.SharpRect;
            panelShape.CornerRadius = ComicStyle.CornerSharp;
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

            return MapSelectLayoutMath.ResolvePanelSize(mobile, safeWidth, safeHeight);
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

            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(panel, false);
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-ComicStyle.Border * 2f, m.TopBandHeight);
            band.anchoredPosition = new Vector2(0f, -ComicStyle.Border);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.SharpRect;
            bandShape.CornerRadius = ComicStyle.CornerSharp;
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
            float innerWidth = panelSize.x - m.Padding * 2f;
            float cardWidth = (innerWidth - m.CardGap) * 0.5f;
            MapDefinition active = MapCatalogService.ActiveOrSelected;

            for (int i = 0; i < MapCatalogService.Maps.Count; i++)
            {
                MapDefinition map = MapCatalogService.Maps[i];
                var topLeft = new Vector2(m.Padding + i * (cardWidth + m.CardGap), contentTop);
                BuildCard(panel, m, mobile, map, map.Id == active.Id, topLeft,
                    new Vector2(cardWidth, contentHeight));
            }
        }

        private void BuildCard(RectTransform panel, MapSelectMetrics m, bool mobile, MapDefinition map,
            bool selected, Vector2 topLeft, Vector2 size)
        {
            var go = new GameObject("Card_" + map.Id, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(panel, false);
            var card = (RectTransform)go.transform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            card.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = selected ? m.CardBorderWidth : m.CardBorderThinWidth;
            shape.BorderColor = selected ? map.Accent : ComicStyle.Ink;
            shape.color = ComicStyle.PaperAlt;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Panel);

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => OnCardClicked(map));
            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Panel);
            UISoundBinder.EnsureBinder(button);

            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(card, false);
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-m.CardBorderThinWidth * 2f, m.CardBandHeight);
            band.anchoredPosition = new Vector2(0f, -m.CardBorderThinWidth);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.SharpRect;
            bandShape.CornerRadius = ComicStyle.CornerSharp;
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
            preview.anchoredPosition = new Vector2((size.x - previewSize.x) * 0.5f, -previewTop);

            float textTop = previewTop + previewSize.y + pad * 0.45f;
            float textWidth = size.x - pad * 2f;
            float titleFont = UITypography.FontSnapped(m.CardTitleFont);
            CreateText(card, "MapName", map.DisplayName, titleFont, true, ComicStyle.Ink,
                new Vector2(pad, textTop),
                new Vector2(textWidth, titleFont * 1.25f),
                TextAlignmentOptions.MidlineLeft, 1f, FontStyles.Bold);

            float englishFont = UITypography.FontSnapped(m.CardEnglishFont);
            float englishTop = textTop + titleFont * 1.2f;
            CreateText(card, "MapEnglishName", map.EnglishName, englishFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                new Vector2(pad, englishTop),
                new Vector2(textWidth, englishFont * 1.5f),
                TextAlignmentOptions.MidlineLeft, 1.5f, FontStyles.Bold);

            float descriptorFont = UITypography.FontSnapped(m.CardDescriptorFont);
            float descriptorTop = englishTop + englishFont * 1.55f;
            CreateText(card, "MapDescriptor", map.Descriptor, descriptorFont, false, ComicStyle.Ink,
                new Vector2(pad, descriptorTop),
                new Vector2(textWidth, descriptorFont * 1.6f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            string buttonLabel = selected ? "当前地图" : "切换到此图";
            Color buttonFill = selected ? map.Accent : ComicStyle.Ink;
            CreateCardButton(card, m, buttonLabel, buttonFill, map,
                new Vector2(pad, size.y - pad - m.CardButtonHeight),
                new Vector2(textWidth, m.CardButtonHeight));
        }

        private void CreateCardButton(RectTransform card, MapSelectMetrics m, string label, Color fill,
            MapDefinition map, Vector2 topLeft, Vector2 size)
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
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.CardButtonFont);
            TextMeshProUGUI text = CreateText(rect, "Label", label, font, true, ComicStyle.Paper,
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
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Tag);
            UISoundBinder.EnsureBinder(button);
        }

        private void OnCardClicked(MapDefinition map)
        {
            if (map == null) return;

            MapDefinition active = MapCatalogService.ActiveOrSelected;
            if (map.Id == active.Id)
            {
                MapCatalogService.TrySelect(map.Id);
                Close();
                return;
            }

            MapCatalogService.TrySelect(map.Id);
            if (!MapCatalogService.LoadMap(map))
            {
                Close();
            }
        }

        private void BuildFooter(RectTransform panel, MapSelectMetrics m, bool mobile, Vector2 panelSize)
        {
            float noteFont = UITypography.FontSnapped(m.NoteFont);
            float noteTop = panelSize.y - m.FooterHeight + (m.FooterHeight - noteFont * 1.5f) * 0.5f;

            CreateText(panel, "FooterNote", "选择即保存 · 切换会重新载入场地", noteFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, noteTop),
                new Vector2(panelSize.x * 0.62f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            string closeHint = mobile ? "点击空白处关闭" : "ESC / 点击空白处关闭";
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
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
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
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
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
            shape.Shape = UIInkShape.SharpRect;
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
            float spacing = 0f, FontStyles style = FontStyles.Bold)
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
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
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
