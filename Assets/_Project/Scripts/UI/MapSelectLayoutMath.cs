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
                };
            }

            return new MapSelectMetrics
            {
                PanelMaxWidth = 2200f,
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
            };
        }

        public static Vector2 ResolvePanelSize(bool mobile, float safeWidth, float safeHeight)
        {
            if (safeWidth < 1f) safeWidth = MenuLayoutMath.RefWidth;
            if (safeHeight < 1f) safeHeight = MenuLayoutMath.RefHeight;

            MapSelectMetrics m = Metrics(mobile);
            float inset = m.Padding * 2f;
            float width = mobile
                ? Mathf.Min(m.PanelMaxWidth, safeWidth * m.PanelWidthRatio)
                : Mathf.Min(m.PanelMaxWidth, safeWidth - inset);
            float height = mobile
                ? Mathf.Min(m.PanelMaxHeight, safeHeight * m.PanelHeightRatio)
                : Mathf.Min(m.PanelMaxHeight, safeHeight - inset);

            return new Vector2(
                Mathf.Max(320f, Mathf.Min(width, safeWidth)),
                Mathf.Max(320f, Mathf.Min(height, safeHeight)));
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
