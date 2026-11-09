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

    /// <summary>
    /// 单个战绩数据格的内部排版结果（由 <see cref="HomeInfoLayoutMath.ResolveStatTileLayout"/> 给出）。
    /// </summary>
    public struct HomeInfoStatTileLayout
    {
        /// <summary>true = 标签在左 / 数值在右的一行式；false = 标签在上 / 数值在下的两段式。</summary>
        public bool Horizontal;
        public float Pad;
        public float LabelFont;
        public float ValueFont;
    }

    public static class HomeInfoLayoutMath
    {
        /// <summary>
        /// 内容自然高度超过盒子高度时，字号与行距统一的等比收敛系数（≤ 1，永不放大）。
        /// 先整体等比缩、而不是只缩间距：间距先归零会变成「贴在一起但没叠」，观感更糟。
        /// 无下限——宁可变小也绝不叠字；盒高有面板最小高度兜底，实际不会缩到读不了。
        /// </summary>
        public static float FitScale(float boxHeight, float naturalHeight)
        {
            if (naturalHeight <= 1f || boxHeight <= 0f) return 1f;
            return Mathf.Min(1f, boxHeight / naturalHeight);
        }

        /// <summary>
        /// 身份卡纵向五段（LV 角签 / 等级 / 段位 / XP 条 / ID 标签）的自然总高（不含上下内缩）。
        /// 行距常量与 HomeInfoOverlay.BuildIdentityBlock 共用同一组：6+10+4+8+8+8+14 = 58。
        /// </summary>
        public static float IdentityContentHeight(HomeInfoMetrics m, float levelFont, float rankFont,
            float labelFont)
        {
            return m.IdentityTagSize.y * 2f + 58f
                   + levelFont * 1.15f + rankFont * 1.35f + labelFont * 2.85f + m.XpBarHeight;
        }

        /// <summary>
        /// 战绩数据格排版：格子高得下「标签 + 大数字」两段时保持原样；
        /// 手机横屏逻辑高只有桌面一半（20:9 时长边换短边），格子会被压到 90px 上下，
        /// 此时退回「标签在左 / 数值在右」的一行式，并把数值字号收到格高容得下的上限——
        /// 两种模式都不会让标签与数值叠字（旧版两段式在窄屏会让大数字顶出格子压住标签）。
        /// </summary>
        public static HomeInfoStatTileLayout ResolveStatTileLayout(float tileHeight, float labelFont, float valueFont)
        {
            float pad = Mathf.Clamp(tileHeight * 0.14f, 10f, 20f);
            float available = Mathf.Max(1f, tileHeight - pad * 2f);

            // 用「字号」而不是「行盒高」估两段式占用：行盒自带 1.15~1.4 倍行距，
            // 按行盒判会让桌面也误判成放不下（桌面本来就是紧贴但清晰的两段式）。
            if (available >= labelFont + valueFont + 6f)
            {
                return new HomeInfoStatTileLayout
                {
                    Horizontal = false,
                    Pad = pad,
                    LabelFont = labelFont,
                    ValueFont = valueFont,
                };
            }

            return new HomeInfoStatTileLayout
            {
                Horizontal = true,
                Pad = pad,
                // 一行式里标签与数值共用行高：各自都不能超过格子的可用高度。
                LabelFont = Mathf.Min(labelFont, available / 1.4f),
                ValueFont = Mathf.Min(valueFont, available / 1.15f),
            };
        }

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
