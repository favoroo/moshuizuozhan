using System.Collections.Generic;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 结算页「本局成就」解锁条（运行时装配，纯装饰层：任何失败只 LogWarning，不影响结算流程）。
    /// - 最多 3 张解锁卡（移动端 2 张）：图标 + 成就名 + 幽默描述 + NEW 角签；
    /// - 溢出提示「其余 N 项 · 首页战绩浮层可查看全部成就」；
    /// - 对外只暴露 EnsureBuilt / Populate / Hide / Motion，由 MatchFlowUI 负责入场编排。
    /// </summary>
    [DisallowMultipleComponent]
    public class ResultAchievementsView : MonoBehaviour
    {
        private const int MaxCardsDesktop = 3;
        private const int MaxCardsMobile = 2;

        private sealed class CardRefs
        {
            public RectTransform Root;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Desc;
            public RectTransform NewTag;
        }

        private UIThemeSO _theme;
        private bool _mobile;
        private readonly List<CardRefs> _cards = new List<CardRefs>();
        private TextMeshProUGUI _overflowText;
        private RectTransform _row;
        private TextMeshProUGUI _sectionTitle;

        public UISpringMotion Motion { get; private set; }

        /// <summary>构建骨架（只建一次）；节点命名保证重复调用幂等。</summary>
        public void EnsureBuilt(UIThemeSO theme, bool mobile)
        {
            _theme = UIThemeSO.Resolve(theme);
            _mobile = mobile;
            if (transform.Find("AchievementRow") != null) return;

            try
            {
                Build();
            }
            catch (System.Exception ex)
            {
                // 装饰性换肤代码要 try/catch + LogWarning：成就条失败不该让整局结算起不来。
                Debug.LogWarning("[ResultAchievementsView] 成就条构建失败：" + ex.Message);
            }
        }

        /// <summary>写入本局新解锁成就；空列表时整条隐藏。返回是否可见。</summary>
        public bool Populate(IReadOnlyList<string> achievementIds)
        {
            if (_row == null) return false;

            int count = achievementIds != null ? achievementIds.Count : 0;
            if (count <= 0)
            {
                Hide();
                return false;
            }

            int maxCards = _mobile ? MaxCardsMobile : MaxCardsDesktop;
            for (int i = 0; i < _cards.Count; i++)
            {
                CardRefs card = _cards[i];
                bool visible = i < count && i < maxCards;
                card.Root.gameObject.SetActive(visible);
                if (!visible) continue;

                if (AchievementCatalog.TryGet(achievementIds[i], out AchievementDef def))
                {
                    if (card.Name != null)
                    {
                        card.Name.text = def.Name;
                        TMP_FontAsset font = _theme.ResolveFont(true, def.Name);
                        if (font != null) card.Name.font = font;
                    }
                    if (card.Desc != null)
                    {
                        card.Desc.text = def.Description;
                        TMP_FontAsset font = _theme.ResolveFont(false, def.Description);
                        if (font != null) card.Desc.font = font;
                    }
                    if (card.Icon != null)
                    {
                        Sprite icon = UIIconLibrary.TryGet(def.IconName, out Sprite sprite) ? sprite : null;
                        card.Icon.gameObject.SetActive(icon != null);
                        if (icon != null) card.Icon.sprite = icon;
                    }
                }
                else
                {
                    // 未知 id（目录改动/存档残留）：隐藏整卡，绝不上屏空卡。
                    card.Root.gameObject.SetActive(false);
                }
            }

            if (_overflowText != null)
            {
                int overflow = count - maxCards;
                // 移动端底部余量紧：溢出量并进「本局成就」节标（本局成就 +N），不再单独占一行。
                bool showNote = overflow > 0 && !_mobile;
                _overflowText.gameObject.SetActive(showNote);
                if (showNote)
                {
                    string note = $"其余 {overflow} 项新成就 · 首页战绩浮层可查看全部";
                    _overflowText.text = note;
                    TMP_FontAsset font = _theme.ResolveFont(false, note);
                    if (font != null) _overflowText.font = font;
                }

                if (overflow > 0 && _mobile && _sectionTitle != null)
                {
                    string tag = $"本局成就 +{overflow}";
                    _sectionTitle.text = tag;
                    TMP_FontAsset tagFont = _theme.ResolveFont(true, tag);
                    if (tagFont != null) _sectionTitle.font = tagFont;
                }
            }

            gameObject.SetActive(true);
            return true;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        // ==================== 装配 ====================

        private void Build()
        {
            bool mobile = _mobile;

            var rowGo = new GameObject("AchievementRow", typeof(RectTransform));
            rowGo.transform.SetParent(transform, false);
            _row = (RectTransform)rowGo.transform;
            _row.anchorMin = new Vector2(0.5f, 0.5f);
            _row.anchorMax = new Vector2(0.5f, 0.5f);
            _row.pivot = new Vector2(0.5f, 0.5f);
            _row.anchoredPosition = Vector2.zero;
            _row.sizeDelta = new Vector2(mobile ? 1080f : 1000f, 100f);

            var motionGo = gameObject.GetComponent<UISpringMotion>();
            if (motionGo == null) motionGo = gameObject.AddComponent<UISpringMotion>();
            motionGo.SetTheme(_theme);
            Motion = motionGo;

            int cardCount = mobile ? MaxCardsMobile : MaxCardsDesktop;
            float cardW = mobile ? 430f : 320f;
            float cardH = mobile ? 108f : 100f;
            float gap = mobile ? 24f : 16f;
            float rowW = cardCount * cardW + (cardCount - 1) * gap;
            float x = -rowW * 0.5f;

            for (int i = 0; i < cardCount; i++)
            {
                _cards.Add(BuildCard(rowGo.transform, "Card_" + i,
                    new Vector2(x, 0f), new Vector2(cardW, cardH)));
                x += cardW + gap;
            }

            // 节标题：「本局成就」墨底黄字小胶囊，骑在卡行上沿。
            // 字号封顶 20：整条节标（胶囊 + 与卡行的 6px 缝）就是本条向战报卡下方索要的题头带，
            // 宿主的挂载高度按它算（见 MatchFlowUI.EnsureAchievementsStrip）。
            // 移动端不能再走 ×1.28 放大（22 → 28）：胶囊会从 34 胖到 47.6，结算列在 20:9 手机上顶不住。
            float titleFont = Mathf.Min(UITypography.FontSnapped(19f), 20f);
            Vector2 titleSize = new Vector2(mobile ? 190f : 164f, titleFont * 1.7f);
            float titleCenterY = cardH * 0.5f + titleSize.y * 0.5f + 6f; // 节标中心相对卡行中心
            var titleGo = new GameObject("SectionTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            titleGo.transform.SetParent(rowGo.transform, false);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, titleCenterY);
            titleRect.sizeDelta = titleSize;

            var titleShape = titleGo.GetComponent<UIInkShapeGraphic>();
            titleShape.Shape = UIInkShape.Pill;
            titleShape.BorderWidth = 0f;
            titleShape.color = ComicStyle.Ink;
            titleShape.HardShadow = false;
            titleShape.Halftone = false;
            titleShape.raycastTarget = false;
            _theme.ApplyHardShadow(titleShape, UIHardShadowLevel.Tag);

            _sectionTitle = CreateText(titleGo.transform, "Text", "本局成就", titleFont, true,
                ComicStyle.Yellow, Vector2.zero, titleSize, TextAlignmentOptions.Center);

            // 溢出读数：与节标同一行、右对齐贴卡行右缘。
            // 旧版排在卡行下方，CTA 按钮为节标让位下移后就只剩 0 余量，改挂题头带内。
            // CreateText 是顶左锚点约定（y 向下为正），节标中心在行顶之上，故 y 取负值。
            float rowHalfH = _row.sizeDelta.y * 0.5f;
            _overflowText = CreateText(rowGo.transform, "OverflowNote", string.Empty,
                UITypography.FontSnapped(mobile ? 17f : 15f), false,
                ComicStyle.WithAlpha(ComicStyle.Paper, 0.62f),
                new Vector2((_row.sizeDelta.x - rowW) * 0.5f, -(titleCenterY - rowHalfH + 12f)),
                new Vector2(rowW, 24f),
                TextAlignmentOptions.MidlineRight);
            _overflowText.gameObject.SetActive(false);

            gameObject.SetActive(false);
        }

        private CardRefs BuildCard(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var card = new CardRefs();

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            card.Root = (RectTransform)go.transform;
            card.Root.anchorMin = new Vector2(0.5f, 0.5f);
            card.Root.anchorMax = new Vector2(0.5f, 0.5f);
            card.Root.pivot = new Vector2(0.5f, 0.5f);
            card.Root.anchoredPosition = position;
            card.Root.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = ComicStyle.CornerFor(size.x, size.y);
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Button);

            // 左侧图标：墨色圆角方块 + 纸白图标（取不到就隐藏）
            float iconBox = _mobile ? 72f : 62f;
            var iconBgGo = new GameObject("IconBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            iconBgGo.transform.SetParent(go.transform, false);
            var iconBgRect = (RectTransform)iconBgGo.transform;
            iconBgRect.anchorMin = new Vector2(0f, 0.5f);
            iconBgRect.anchorMax = new Vector2(0f, 0.5f);
            iconBgRect.pivot = new Vector2(0f, 0.5f);
            iconBgRect.anchoredPosition = new Vector2(14f, 0f);
            iconBgRect.sizeDelta = new Vector2(iconBox, iconBox);

            var iconBgShape = iconBgGo.GetComponent<UIInkShapeGraphic>();
            iconBgShape.Shape = UIInkShape.RoundedRect;
            iconBgShape.CornerRadius = ComicStyle.CornerFor(iconBox, iconBox);
            iconBgShape.BorderWidth = 0f;
            iconBgShape.color = ComicStyle.Ink;
            iconBgShape.HardShadow = false;
            iconBgShape.Halftone = false;
            iconBgShape.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(iconBgGo.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(12f, 12f);
            iconRect.offsetMax = new Vector2(-12f, -12f);

            card.Icon = iconGo.GetComponent<Image>();
            card.Icon.color = ComicStyle.Paper;
            card.Icon.raycastTarget = false;
            iconGo.SetActive(false); // 图标加载成功前隐藏，避免白块

            // 右侧文案区：成就名 + 幽默描述（最多两行，Wrap 防截断）
            float textX = 14f + iconBox + 12f;
            float textW = size.x - textX - 14f;

            float nameFont = UITypography.FontSnapped(_mobile ? 21f : 19f);
            card.Name = CreateText(go.transform, "Name", string.Empty, nameFont, true, ComicStyle.Ink,
                new Vector2(textX, 14f), new Vector2(textW, nameFont * 1.4f),
                TextAlignmentOptions.MidlineLeft);

            float descFont = UITypography.FontSnapped(_mobile ? 16f : 14f);
            card.Desc = CreateText(go.transform, "Desc", string.Empty, descFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.66f),
                new Vector2(textX, 14f + nameFont * 1.4f + 4f),
                new Vector2(textW, size.y - 14f - (14f + nameFont * 1.4f + 4f)),
                TextAlignmentOptions.MidlineLeft, wrapping: true);

            // NEW 角签：右上角黄底墨字
            var tagGo = new GameObject("NewTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            tagGo.transform.SetParent(go.transform, false);
            Vector2 tagSize = new Vector2(_mobile ? 66f : 56f, _mobile ? 30f : 26f);
            card.NewTag = (RectTransform)tagGo.transform;
            card.NewTag.anchorMin = new Vector2(1f, 1f);
            card.NewTag.anchorMax = new Vector2(1f, 1f);
            card.NewTag.pivot = new Vector2(1f, 1f);
            card.NewTag.anchoredPosition = new Vector2(-8f, -8f);
            card.NewTag.sizeDelta = tagSize;

            var tagShape = tagGo.GetComponent<UIInkShapeGraphic>();
            tagShape.Shape = UIInkShape.Pill;
            tagShape.BorderWidth = ComicStyle.BorderThin;
            tagShape.BorderColor = ComicStyle.Ink;
            tagShape.color = ComicStyle.Yellow;
            tagShape.HardShadow = false;
            tagShape.Halftone = false;
            tagShape.raycastTarget = false;

            CreateText(tagGo.transform, "Text", "NEW", UITypography.FontSnapped(_mobile ? 16f : 14f), true,
                ComicStyle.Ink, Vector2.zero, tagSize, TextAlignmentOptions.Center);

            return card;
        }

        /// <summary>
        /// 顶部锚定文本（topLeft 顶左约定，与 HomeInfoOverlay 同一套）：
        /// position = 相对父节点顶角的偏移；wrapping = true 时允许换行（成就描述两行内）。
        /// </summary>
        private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
            bool display, Color color, Vector2 topLeft, Vector2 size, TextAlignmentOptions alignment,
            bool wrapping = false)
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
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = wrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            return tmp;
        }
    }
}
