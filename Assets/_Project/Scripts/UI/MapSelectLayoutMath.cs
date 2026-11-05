using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 地图选择浮层的度量（桌面 1920×1080 基准 + 移动端一套更大的数）。
    /// 与 <see cref="HomeInfoLayoutMath"/> 同一定位：只描述盒子与字号，
    /// 运行时由 MapSelectOverlay 消费，EditMode 可直接断言安全区与字号下限。
    /// </summary>
    public struct MapSelectMetrics
    {
        public float PanelMaxWidth;
        /// <summary>地图变多时允许的宽面板上限（让每张卡都保住 CardPreferredWidth，不掉到横向滚动）。</summary>
        public float PanelWideMaxWidth;
        public float PanelMaxHeight;
        public float PanelWidthRatio;
        public float PanelHeightRatio;
        public float Padding;
        public float ContentInset;
        public float HeaderHeight;
        public float FooterHeight;
        public float TopBandHeight;

        public Vector2 TagSize;
        public float TagFont;
        public float TitleFont;
        public float SubtitleFont;
        public Vector2 CloseSize;
        public float CloseFont;

        public float CardGap;
        public float CardInnerPadding;
        public float CardBandHeight;
        public float CardBorderWidth;
        public float CardBorderThinWidth;
        public float CardTitleFont;
        public float CardEnglishFont;
        public float CardDescriptorFont;
        public Vector2 CardBadgeSize;
        public float CardBadgeFont;
        public float CardButtonHeight;
        public float CardButtonFont;
        public float NoteFont;

        /// <summary>卡片行放不下时希望的卡宽（面板宽度按它随地图数量增长）。</summary>
        public float CardPreferredWidth;
        /// <summary>卡宽下限：再窄标题列就放不下 5 个汉字，宁可横向滚动。</summary>
        public float CardMinWidth;
        public float CardMaxWidth;

        public Vector2 ScrollArrowSize;
        public float ScrollArrowFont;
        /// <summary>箭头与卡片区之间的留白（两侧箭头各占 ScrollArrowSize.x + 本值）。</summary>
        public float ScrollArrowGap;
        /// <summary>滚动内容右/下给卡片硬影留的余量；视口有 RectMask2D，硬影必须留在遮罩内。</summary>
        public float ScrollSlack;
    }

    /// <summary>
    /// 卡片行布局结果：运行时按它摆卡片、决定是否开滚动。
    /// 纯数据，EditMode 可直接断言「3 张图在桌面 1920 不溢出」这类回归。
    /// </summary>
    public struct MapSelectCardLayout
    {
        public float CardWidth;
        /// <summary>卡片 + 间距的总宽（不含滚动余量）。</summary>
        public float RowWidth;
        /// <summary>滚动内容宽：滚动时 = RowWidth + 右端硬影余量。</summary>
        public float ContentWidth;
        public float ViewportWidth;
        /// <summary>行不满视口时的居中偏移（地图很少时不留半屏空白）。</summary>
        public float SideInset;
        public int CardCount;
        public bool ScrollNeeded;
    }

    public static class MapSelectLayoutMath
    {
        public static MapSelectMetrics Metrics(bool mobile)
        {
            if (!mobile)
            {
                return new MapSelectMetrics
                {
                    PanelMaxWidth = 1120f,
                    PanelWideMaxWidth = 1720f,
                    PanelMaxHeight = 820f,
                    PanelWidthRatio = 0.92f,
                    PanelHeightRatio = 0.90f,
                    Padding = 40f,
                    ContentInset = 20f,
                    HeaderHeight = 168f,
                    FooterHeight = 64f,
                    TopBandHeight = 8f,

                    TagSize = new Vector2(128f, 40f),
                    TagFont = 18f,
                    TitleFont = 44f,
                    SubtitleFont = 20f,
                    CloseSize = new Vector2(72f, 72f),
                    CloseFont = 36f,

                    CardGap = 28f,
                    CardInnerPadding = 24f,
                    CardBandHeight = 6f,
                    CardBorderWidth = 4f,
                    CardBorderThinWidth = 2f,
                    CardTitleFont = 38f,
                    CardEnglishFont = 18f,
                    CardDescriptorFont = 20f,
                    CardBadgeSize = new Vector2(88f, 34f),
                    CardBadgeFont = 18f,
                    CardButtonHeight = 64f,
                    CardButtonFont = 22f,
                    NoteFont = 18f,

                    CardPreferredWidth = 506f,
                    CardMinWidth = 480f,
                    CardMaxWidth = 620f,

                    ScrollArrowSize = new Vector2(56f, 56f),
                    ScrollArrowFont = 30f,
                    ScrollArrowGap = 10f,
                    ScrollSlack = 10f,
                };
            }

            return new MapSelectMetrics
            {
                PanelMaxWidth = 2200f,
                PanelWideMaxWidth = 2400f,
                PanelMaxHeight = 1000f,
                PanelWidthRatio = 0.92f,
                PanelHeightRatio = 0.90f,
                Padding = 36f,
                ContentInset = 22f,
                HeaderHeight = 210f,
                FooterHeight = 76f,
                TopBandHeight = 10f,

                TagSize = new Vector2(168f, 52f),
                TagFont = 20f,
                TitleFont = 56f,
                SubtitleFont = 24f,
                CloseSize = new Vector2(88f, 88f),
                CloseFont = 44f,

                CardGap = 28f,
                CardInnerPadding = 28f,
                CardBandHeight = 10f,
                CardBorderWidth = 5f,
                CardBorderThinWidth = 3f,
                CardTitleFont = 46f,
                CardEnglishFont = 24f,
                CardDescriptorFont = 26f,
                CardBadgeSize = new Vector2(120f, 44f),
                CardBadgeFont = 24f,
                CardButtonHeight = 84f,
                CardButtonFont = 28f,
                NoteFont = 23f,

                CardPreferredWidth = 680f,
                CardMinWidth = 660f,
                CardMaxWidth = 1100f,

                ScrollArrowSize = new Vector2(76f, 76f),
                ScrollArrowFont = 38f,
                ScrollArrowGap = 12f,
                ScrollSlack = 12f,
            };
        }

        public static Vector2 ResolvePanelSize(bool mobile, float safeWidth, float safeHeight, int cardCount = 2)
        {
            if (safeWidth < 1f) safeWidth = MenuLayoutMath.RefWidth;
            if (safeHeight < 1f) safeHeight = MenuLayoutMath.RefHeight;

            MapSelectMetrics m = Metrics(mobile);
            float inset = m.Padding * 2f;
            float width = ResolvePanelWidth(mobile, safeWidth, cardCount);
            float height = mobile
                ? Mathf.Min(m.PanelMaxHeight, safeHeight * m.PanelHeightRatio)
                : Mathf.Min(m.PanelMaxHeight, safeHeight - inset);

            return new Vector2(
                Mathf.Max(320f, Mathf.Min(width, safeWidth)),
                Mathf.Max(320f, Mathf.Min(height, safeHeight)));
        }

        /// <summary>
        /// 面板宽度：先按「地图数 × 期望卡宽」算需求，再与原有档位取大，
        /// 最后夹到当前屏宽能给的份额内。地图从 2 张加到 3 张时面板会长大
        /// （桌面 1920 下三张卡仍是原有卡宽），屏宽不够才交给卡片区的横向滚动。
        /// </summary>
        public static float ResolvePanelWidth(bool mobile, float safeWidth, int cardCount)
        {
            MapSelectMetrics m = Metrics(mobile);
            if (cardCount < 1) cardCount = 1;

            float inset = m.Padding * 2f;
            float baseWidth = Mathf.Min(m.PanelMaxWidth, mobile ? safeWidth * m.PanelWidthRatio : safeWidth - inset);
            float wideCap = Mathf.Min(m.PanelWideMaxWidth, mobile ? safeWidth * m.PanelWidthRatio : safeWidth - inset);
            float needed = inset + cardCount * m.CardPreferredWidth + (cardCount - 1) * m.CardGap;

            float cap = Mathf.Max(wideCap, baseWidth);
            return Mathf.Clamp(Mathf.Max(baseWidth, needed), Mathf.Min(320f, cap), cap);
        }

        /// <summary>
        /// 卡片行布局。<paramref name="viewportWidth"/> 是卡片区可用宽（已扣掉面板内边距与两侧箭头）。
        /// 卡宽先按「平分视口」算，再夹到 [CardMinWidth, CardMaxWidth]：
        /// 夹到下限说明视口真的放不下，返回 ScrollNeeded = true（宁可滚动，不把文字列压没）。
        /// </summary>
        public static MapSelectCardLayout ResolveCardLayout(bool mobile, float viewportWidth, int cardCount)
        {
            MapSelectMetrics m = Metrics(mobile);
            if (cardCount < 1) cardCount = 1;
            if (viewportWidth < 1f) viewportWidth = 1f;

            float gaps = m.CardGap * (cardCount - 1);
            float cardWidth = Mathf.Clamp((viewportWidth - gaps) / cardCount, m.CardMinWidth, m.CardMaxWidth);
            float rowWidth = cardCount * cardWidth + gaps;
            bool scroll = rowWidth > viewportWidth + 0.5f;

            return new MapSelectCardLayout
            {
                CardWidth = cardWidth,
                RowWidth = rowWidth,
                ContentWidth = rowWidth + (scroll ? m.ScrollSlack : 0f),
                ViewportWidth = viewportWidth,
                SideInset = scroll ? 0f : Mathf.Max(0f, (viewportWidth - rowWidth) * 0.5f),
                CardCount = cardCount,
                ScrollNeeded = scroll,
            };
        }

        /// <summary>两次箭头/方向键之间的滑动步长：一整张卡 + 一个间距。</summary>
        public static float CardStep(MapSelectMetrics m, float cardWidth)
        {
            return cardWidth + m.CardGap;
        }

        /// <summary>滚动量（px，向右为正）夹到 [0, 内容宽 − 视口宽]；放得下时恒为 0。</summary>
        public static float ClampScrollOffset(float offset, float contentWidth, float viewportWidth)
        {
            return Mathf.Clamp(offset, 0f, Mathf.Max(0f, contentWidth - viewportWidth));
        }

        /// <summary>按下一次箭头/方向键后的目标滚动量（已夹到两端）。</summary>
        public static float StepScrollOffset(float offset, int direction, float step, float contentWidth, float viewportWidth)
        {
            int sign = direction < 0 ? -1 : 1;
            return ClampScrollOffset(offset + sign * Mathf.Max(1f, step), contentWidth, viewportWidth);
        }

        /// <summary>
        /// 卡片内预览尺寸：卡片是「预览在左、文字在右、按钮通栏」的横版结构，
        /// 预览先扣掉顶部色带与底部按钮，再按 40×56 等比；宽度不超过卡片内宽的一半，
        /// 保证右侧始终留得下标题、英文名与描述。
        /// </summary>
        public static Vector2 PreviewSize(bool mobile, float cardWidth, float cardHeight)
        {
            MapSelectMetrics m = Metrics(mobile);
            float available = Mathf.Max(120f, cardHeight
                - m.CardInnerPadding * 2f
                - m.CardBandHeight
                - m.CardButtonHeight
                - 28f);

            float innerWidth = Mathf.Max(1f, cardWidth - m.CardInnerPadding * 2f);
            float maxWidth = Mathf.Max(80f, innerWidth * 0.46f);
            float height = available;
            float width = height * 40f / 56f;
            if (width > maxWidth)
            {
                width = maxWidth;
                height = width * 56f / 40f;
            }
            return new Vector2(width, Mathf.Min(height, available));
        }
    }
}
