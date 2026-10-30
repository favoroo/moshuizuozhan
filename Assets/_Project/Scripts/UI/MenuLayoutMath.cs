using UnityEngine;

namespace P1.UI
{
    /// <summary>首页（Ready 主菜单）的版面度量：只描述盒子与间距，字号由 UITypography 再放大。</summary>
    public struct HomeMenuMetrics
    {
        /// <summary>距屏幕/安全区边缘的基础留白。</summary>
        public float Margin;

        public Vector2 TagSize;
        public float TagFont;

        /// <summary>Hero 区（标题 + 拉丁副标题 + 规格行）的可用宽度。</summary>
        public Vector2 HeroBox;

        public float TitleFont;
        public float LatinFont;
        public float SpecFont;

        /// <summary>主 CTA（开始比赛）尺寸与字号。</summary>
        public Vector2 CtaSize;
        public float CtaFont;

        public Vector2 SecondarySize;
        public float SecondaryFont;

        public float VersionFont;

        /// <summary>背景装饰圆与旋转方块直径。</summary>
        public float DecoCircle;
        public float DecoSquare;
    }

    /// <summary>左下角「武器整备」入口卡（取代旧的窄黄条）的度量。</summary>
    public struct LoadoutCardMetrics
    {
        public Vector2 Size;
        public float IconDiameter;
        public float TitleFont;
        public float NameFont;

        /// <summary>右侧箭头色块边长与箭号字号。</summary>
        public float ChevronSize;
        public float ChevronFont;
    }

    /// <summary>战前武器抽屉（左滑出）的度量。</summary>
    public struct WeaponDrawerMetrics
    {
        public float Width;
        public float HeaderHeight;
        public float HeaderTitleFont;
        public float HeaderSubtitleFont;

        public Vector2 CloseSize;
        public float CloseFont;

        public float FooterFont;

        /// <summary>单张武器卡高度（宽度由布局组铺满抽屉）。</summary>
        public float CardHeight;
        public float CardIconDiameter;
        public Vector2 IndexSize;
        public float IndexFont;
        public float NameFont;
        public float EnglishFont;
        public float RoleFont;
        public float StatsFont;
        public float SpecialFont;

        public Vector2 EquippedSize;
        public float EquippedFont;

        public float StatBlockSize;
        public float StatBlockGap;

        public float ScrollLeftInset;
        public float ScrollRightInset;
        public float ScrollTopInset;
        public float ScrollBottomInset;
    }

    /// <summary>
    /// 首页 / 武器栏的纯逻辑版面表（桌面 1920×1080 基准，移动端一套更大的数）。
    ///
    /// 为什么单独抽出来：移动端「看起来很小」的根因是整块 UI 只按桌面比例排、只给字号 ×1.28；
    /// 手机屏幕物理尺寸只有显示器的零头，必须显式给出更大的盒子（CTA、卡片、图标、触控目标）。
    /// 这些数字要能被 EditMode 直接断言（移动端一律 &gt; 桌面端、抽屉宽度占比合理），
    /// 不再靠人眼看截图猜。
    /// </summary>
    public static class MenuLayoutMath
    {
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        /// <summary>移动端抽屉宽度占参考画布宽度的比例（桌面 620/1920≈32%）。</summary>
        public const float MobileDrawerWidthRatio = 0.82f;

        public static HomeMenuMetrics Home(bool mobile)
        {
            if (!mobile)
            {
                return new HomeMenuMetrics
                {
                    Margin = 64f,
                    TagSize = new Vector2(320f, 52f),
                    TagFont = 18f,
                    HeroBox = new Vector2(980f, 400f),
                    TitleFont = 120f,
                    LatinFont = 32f,
                    SpecFont = 22f,
                    CtaSize = new Vector2(520f, 144f),
                    CtaFont = 46f,
                    SecondarySize = new Vector2(224f, 64f),
                    SecondaryFont = 21f,
                    VersionFont = 18f,
                    DecoCircle = 240f,
                    DecoSquare = 110f,
                };
            }

            return new HomeMenuMetrics
            {
                Margin = 40f,
                TagSize = new Vector2(560f, 72f),
                TagFont = 26f,
                HeroBox = new Vector2(1180f, 460f),
                TitleFont = 132f,
                LatinFont = 34f,
                SpecFont = 22f,
                CtaSize = new Vector2(820f, 200f),
                CtaFont = 48f,
                SecondarySize = new Vector2(300f, 96f),
                SecondaryFont = 22f,
                VersionFont = 18f,
                DecoCircle = 320f,
                DecoSquare = 130f,
            };
        }

        public static LoadoutCardMetrics LoadoutCard(bool mobile)
        {
            if (!mobile)
            {
                return new LoadoutCardMetrics
                {
                    Size = new Vector2(380f, 112f),
                    IconDiameter = 80f,
                    TitleFont = 26f,
                    NameFont = 19f,
                    ChevronSize = 60f,
                    ChevronFont = 26f,
                };
            }

            return new LoadoutCardMetrics
            {
                Size = new Vector2(680f, 176f),
                IconDiameter = 128f,
                TitleFont = 30f,
                NameFont = 22f,
                ChevronSize = 96f,
                ChevronFont = 40f,
            };
        }

        public static WeaponDrawerMetrics Drawer(bool mobile)
        {
            if (!mobile)
            {
                return new WeaponDrawerMetrics
                {
                    Width = 620f,
                    HeaderHeight = 88f,
                    HeaderTitleFont = 28f,
                    HeaderSubtitleFont = 18f,
                    CloseSize = new Vector2(88f, 48f),
                    CloseFont = 20f,
                    FooterFont = 18f,
                    CardHeight = 164f,
                    CardIconDiameter = 84f,
                    IndexSize = new Vector2(44f, 40f),
                    IndexFont = 20f,
                    NameFont = 27f,
                    EnglishFont = 18f,
                    RoleFont = 18f,
                    StatsFont = 20f,
                    SpecialFont = 17f,
                    EquippedSize = new Vector2(88f, 38f),
                    EquippedFont = 18f,
                    StatBlockSize = 18f,
                    StatBlockGap = 3f,
                    ScrollLeftInset = 14f,
                    ScrollRightInset = 14f,
                    ScrollTopInset = 14f,
                    ScrollBottomInset = 48f,
                };
            }

            return new WeaponDrawerMetrics
            {
                Width = RefWidth * MobileDrawerWidthRatio,
                HeaderHeight = 132f,
                HeaderTitleFont = 34f,
                HeaderSubtitleFont = 22f,
                CloseSize = new Vector2(150f, 84f),
                CloseFont = 24f,
                FooterFont = 22f,
                CardHeight = 236f,
                CardIconDiameter = 132f,
                IndexSize = new Vector2(68f, 60f),
                IndexFont = 30f,
                NameFont = 30f,
                EnglishFont = 20f,
                RoleFont = 22f,
                StatsFont = 22f,
                SpecialFont = 20f,
                EquippedSize = new Vector2(136f, 60f),
                EquippedFont = 22f,
                StatBlockSize = 28f,
                StatBlockGap = 4f,
                ScrollLeftInset = 20f,
                ScrollRightInset = 20f,
                ScrollTopInset = 20f,
                ScrollBottomInset = 56f,
            };
        }
    }
}