using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 首页「战绩 / 键位说明」详情浮层的度量（桌面 1920×1080 基准 + 移动端一套更大的数）。
    /// 与 <see cref="MenuLayoutMath"/> 同一定位：只描述盒子与字号，运行时由 HomeInfoOverlay 消费，
    /// EditMode 可直接断言「移动端触控目标更大 / 面板不出安全区」。
    /// </summary>
    public struct HomeInfoMetrics
    {
        // ── 面板 ────────────────────────────────────────────────
        public float PanelMaxWidth;
        public float PanelMaxHeight;
        public float PanelWidthRatio;
        public float PanelHeightRatio;
        public float Padding;
        public float ContentInset;
        public float HeaderHeight;
        public float FooterHeight;
        public float TopBandHeight;
        public float ColumnGap;

        // ── 头部 ────────────────────────────────────────────────
        public Vector2 TagSize;
        public float TagFont;
        public float TitleFont;
        public float SubtitleFont;
        public Vector2 CloseSize;
        public float CloseFont;

        // ── 分区与键位行 ────────────────────────────────────────
        public float SectionFont;
        public float RowHeight;
        public float RowGap;
        public float KeyCapWidth;
        public float KeyFont;
        public float ActionFont;

        // ── 数据格 ──────────────────────────────────────────────
        public float StatValueFont;
        public float StatRankFont;
        public float StatLabelFont;
        public float StatGap;

        // ── 身份卡 / 规则 / 页脚 ────────────────────────────────
        public float IdentityLevelFont;
        public float IdentityRankFont;
        public float IdentityLabelFont;
        public float XpBarHeight;
        public Vector2 IdentityTagSize;
        public float IdentityTagFont;
        public float RuleFont;
        public float NoteFont;
    }

    public static class HomeInfoLayoutMath
    {
        /// <summary>桌面 / 移动端两套浮层度量。</summary>
        public static HomeInfoMetrics Metrics(bool mobile)
        {
            if (!mobile)
            {
                return new HomeInfoMetrics
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
                    ColumnGap = 28f,

                    TagSize = new Vector2(128f, 40f),
                    TagFont = 18f,
                    TitleFont = 44f,
                    SubtitleFont = 20f,
                    CloseSize = new Vector2(72f, 72f),
                    CloseFont = 36f,

                    SectionFont = 26f,
                    RowHeight = 64f,
                    RowGap = 10f,
                    KeyCapWidth = 150f,
                    KeyFont = 20f,
                    ActionFont = 22f,

                    StatValueFont = 48f,
                    StatRankFont = 30f,
                    StatLabelFont = 20f,
                    StatGap = 16f,

                    IdentityLevelFont = 120f,
                    IdentityRankFont = 28f,
                    IdentityLabelFont = 22f,
                    XpBarHeight = 18f,
                    IdentityTagSize = new Vector2(96f, 32f),
                    IdentityTagFont = 18f,
                    RuleFont = 20f,
                    NoteFont = 18f,
                };
            }

            // 移动端：逻辑画布更高但物理尺寸小，面板吃满安全区、触控目标与字号整体上一个台阶。
            return new HomeInfoMetrics
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
                ColumnGap = 28f,

                TagSize = new Vector2(168f, 52f),
                TagFont = 20f,
                TitleFont = 56f,
                SubtitleFont = 24f,
                CloseSize = new Vector2(88f, 88f),
                CloseFont = 44f,

                SectionFont = 32f,
                RowHeight = 82f,
                RowGap = 12f,
                KeyCapWidth = 230f,
                KeyFont = 26f,
                ActionFont = 28f,

                StatValueFont = 60f,
                StatRankFont = 38f,
                StatLabelFont = 24f,
                StatGap = 20f,

                IdentityLevelFont = 132f,
                IdentityRankFont = 34f,
                IdentityLabelFont = 28f,
                XpBarHeight = 22f,
                IdentityTagSize = new Vector2(128f, 46f),
                IdentityTagFont = 20f,
                RuleFont = 25f,
                NoteFont = 23f,
            };
        }

        /// <summary>
        /// 面板尺寸：桌面用固定弹窗上限（不铺满整屏），移动端按安全区比例吃满；
        /// 任何尺寸都保证不超出传入的安全区。
        /// </summary>
        public static Vector2 ResolvePanelSize(bool mobile, float safeWidth, float safeHeight)
        {
            if (safeWidth < 1f) safeWidth = MenuLayoutMath.RefWidth;
            if (safeHeight < 1f) safeHeight = MenuLayoutMath.RefHeight;

            HomeInfoMetrics m = Metrics(mobile);
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
    }
}
