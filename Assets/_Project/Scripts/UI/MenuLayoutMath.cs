using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 首页盒子：以「安全区左上角」为原点，X 向右、Y 向下，单位是 1920×1080 参考画布像素。
    ///
    /// 为什么用左上原点 + Y 向下：首页卡片全部以 anchor/pivot = (0,1) 摆放，
    /// 版面表里读到的数字就是屏幕上「离左边多远、离顶部多远」，调用方不需要在脑内翻符号；
    /// <see cref="AnchoredPosition"/> 已经替调用方取好负号。
    /// 这些盒子是**纯数据**，EditMode 可以直接断言不重叠 / 不出屏（MenuLayoutMathTests）。
    /// </summary>
    public struct HomeBox
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public HomeBox(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Vector2 Size => new Vector2(Width, Height);

        public Vector2 Min => new Vector2(X, Y);

        public Vector2 Center => new Vector2(X + Width * 0.5f, Y + Height * 0.5f);

        public float Right => X + Width;

        public float Bottom => Y + Height;

        /// <summary>宽或高为 0：该元素在当前平台不显示（例如移动端左下的角签让位给整备卡）。</summary>
        public bool IsEmpty => Width <= 0.01f || Height <= 0.01f;

        /// <summary>anchorMin = anchorMax = (0,1)、pivot = (0,1) 时的 anchoredPosition。</summary>
        public Vector2 AnchoredPosition => new Vector2(X, -Y);

        public bool Overlaps(HomeBox other)
        {
            return !IsEmpty && !other.IsEmpty &&
                   X < other.Right - 0.01f && other.X < Right - 0.01f &&
                   Y < other.Bottom - 0.01f && other.Y < Bottom - 0.01f;
        }

        public bool Contains(float px, float py)
        {
            return px >= X - 0.01f && px <= Right + 0.01f && py >= Y - 0.01f && py <= Bottom + 0.01f;
        }

        /// <summary>盒子是否完整落在给定画布内（左上原点坐标系）。</summary>
        public bool FitsIn(Vector2 canvas)
        {
            return X >= -0.01f && Y >= -0.01f &&
                   Right <= canvas.x + 0.01f && Bottom <= canvas.y + 0.01f;
        }

        /// <summary>四边内缩，用于「卡内内容框」。</summary>
        public HomeBox Inset(float left, float top, float right, float bottom)
        {
            return new HomeBox(X + left, Y + top, Mathf.Max(0f, Width - left - right),
                Mathf.Max(0f, Height - top - bottom));
        }

        public override string ToString()
        {
            return "(" + X.ToString("0") + "," + Y.ToString("0") + " " +
                   Width.ToString("0") + "×" + Height.ToString("0") + ")";
        }
    }

    /// <summary>首页（Ready 主菜单）的版面度量：只描述盒子与间距，字号由 UITypography 再放大。</summary>
    public struct HomeMenuMetrics
    {
        /// <summary>距屏幕/安全区边缘的基础留白。</summary>
        public float Margin;

        /// <summary>Hero 卡左上角签（PLAY）的尺寸与字号。</summary>
        public Vector2 TagSize;
        public float TagFont;

        /// <summary>Hero 卡（左侧大色块）的设计尺寸；运行时会按实际安全区宽度自适应。</summary>
        public Vector2 HeroBox;

        public float TitleFont;
        public float LatinFont;
        public float SpecFont;

        /// <summary>Hero 卡底部主 CTA（开始游戏）尺寸与字号。</summary>
        public Vector2 CtaSize;
        public float CtaFont;

        /// <summary>兼容字段：旧版「次级小按钮」尺寸（首页已改为 2×2 卡片，数值保留给旧调用方与测试）。</summary>
        public Vector2 SecondarySize;
        public float SecondaryFont;

        public float VersionFont;

        /// <summary>背景装饰圆与旋转方块直径。</summary>
        public float DecoCircle;
        public float DecoSquare;

        // ==================== t-1 新野兽派首页（波次3a） ====================

        /// <summary>安全区顶部到卡片区顶部的距离（移动端给顶部小按钮留出行李架）。</summary>
        public float CardTop;

        /// <summary>安全区底部到卡片区底部的距离：预留左下「武器整备」整备卡 + 角签。</summary>
        public float CardBottom;

        /// <summary>
        /// 版面垂直居中后的卡片区顶部 = 上下留白的均分点。
        /// 历史版面按 CardTop 压顶排布，而 CardBottom 预留的底部空间（桌面 200 / 移动 228）
        /// 在「武器整备」卡搬进 Hero 卡后基本空置，底部留白远大于顶部。
        /// <see cref="ResolveHomeBoard"/> 与 <see cref="HeroWeaponSlot"/> 必须同源使用此值，
        /// 否则整备卡会脱离 Hero 卡。
        /// </summary>
        public float CenteredBoardTop => (CardTop + CardBottom) * 0.5f;

        /// <summary>Hero 卡与右侧 2×2 卡阵之间的横向间隔。</summary>
        public float ColumnGap;

        /// <summary>右侧 2×2 卡阵总宽度（桌面/移动端两套数）。</summary>
        public float GridWidth;

        /// <summary>2×2 卡片之间的间隔（横竖同值）。</summary>
        public float TileGap;

        /// <summary>卡片顶部身份色带高度（规范：5px，不承载文字）。</summary>
        public float TileBandHeight;

        public float CardBorderWidth;
        public float TileBorderWidth;

        /// <summary>Hero 卡内边距（左, 上）。</summary>
        public Vector2 HeroPadding;

        /// <summary>次级卡内边距（左, 上）。</summary>
        public Vector2 TilePadding;

        public Vector2 TileTagSize;
        public float TileTagFont;
        public float TileTitleFont;
        public float TileInfoFont;
        public float TileArrowFont;

        /// <summary>
        /// Hero 卡中部对局控制区的 Bot 难度段选：黑色「难度」角签 + 4 档药丸（新手/轻松/普通/硬核）。
        /// 位于武器整备卡正下方，与时长行同宽同右缘对齐铺满 innerWidth。
        /// </summary>
        public Vector2 DifficultySegmentSize;
        public float DifficultyGap;
        public float DifficultyMinSegmentWidth;
        public float DifficultyFont;
        public Vector2 DifficultyTagSize;
        public float DifficultyTagFont;
        public float DifficultyTagGap;

        /// <summary>
        /// Hero 卡中部对局控制区的比赛时长段选：黑色「时长」角签 + 3 档药丸（2分/3分/5分）。
        /// 位于难度行正下方，与难度行严格对齐铺满 innerWidth。
        /// </summary>
        public Vector2 DurationSegmentSize;
        public float DurationGap;
        public float DurationMinSegmentWidth;
        public float DurationFont;
        public Vector2 DurationTagSize;
        public float DurationTagFont;
        /// <summary>「时长」角签与 3 档药丸之间的横向间距。</summary>
        public float DurationTagGap;
        public float DurationRowGap;

        /// <summary>左下 / 右下黑底角签（宽或高为 0 表示该平台不显示）。</summary>
        public Vector2 FooterTagSize;
        public float FooterTagFont;
        public Vector2 VersionTagSize;
        public float FooterInset;

        /// <summary>是否显示顶部右侧小按钮（「自定义按键」只在移动端出现）。</summary>
        public bool ShowTopButton;

        public float BoardHeight => MenuLayoutMath.RefHeight - CardTop - CardBottom;

        /// <summary>卡片区（Hero + 2×2 卡阵）外框。</summary>
        public HomeBox Board =>
            new HomeBox(Margin, CardTop, MenuLayoutMath.RefWidth - Margin * 2f, BoardHeight);

        /// <summary>左侧 Hero 大卡：设计宽度 = HeroBox.x，运行时按安全区宽度自适应。</summary>
        public HomeBox HeroCard => new HomeBox(Margin, CardTop, HeroBox.x, BoardHeight);

        public float TileWidth => (GridWidth - TileGap) * 0.5f;

        public float TileHeight => (BoardHeight - TileGap) * 0.5f;

        public HomeBox Grid =>
            new HomeBox(MenuLayoutMath.RefWidth - Margin - GridWidth, CardTop, GridWidth, BoardHeight);

        /// <summary>2×2 卡阵：0=左上 1=右上 2=左下 3=右下（阅读顺序 = 入场错峰顺序）。</summary>
        public HomeBox Tile(int index)
        {
            int col = index % 2;
            int row = index / 2;
            HomeBox grid = Grid;
            return new HomeBox(
                grid.X + col * (TileWidth + TileGap),
                grid.Y + row * (TileHeight + TileGap),
                TileWidth,
                TileHeight);
        }

        public HomeBox HeroTag =>
            new HomeBox(Margin + HeroPadding.x, CardTop + HeroPadding.y, TagSize.x, TagSize.y);

        /// <summary>
        /// Hero 卡内嵌的武器整备卡绝对盒子（以 MenuSafeArea 左上角为原点）。
        /// 位于 Header 正下方，居于顶部模式与中部设置之间。
        /// </summary>
        public HomeBox HeroWeaponSlot
        {
            get
            {
                float innerWidth = HeroBox.x - HeroPadding.x * 2f;
                Vector2 cardSize = MenuLayoutMath.LoadoutCard(ShowTopButton).Size;
                float width = Mathf.Min(innerWidth, cardSize.x);
                float height = cardSize.y;
                float x = Margin + HeroPadding.x;
                float y = CenteredBoardTop + MenuLayoutMath.ResolveHeroFlow(this, BoardHeight, ShowTopButton).WeaponTop;
                return new HomeBox(x, y, width, height);
            }
        }

        /// <summary>
        /// Hero 卡中部对局控制区的 Bot 难度段选（设计盒）：黑色「难度」角签 + 4 档药丸。
        /// 位于武器整备卡正下方，与时长行同宽同右缘对齐。
        /// </summary>
        public HomeBox HeroDifficulty
        {
            get
            {
                float innerWidth = HeroBox.x - HeroPadding.x * 2f;
                HeroFlowLayout flow = MenuLayoutMath.ResolveHeroFlow(this, BoardHeight, ShowTopButton);
                DifficultyBarLayout layout = MenuLayoutMath.ResolveHeroDifficulty(this, innerWidth);
                return new HomeBox(
                    Margin + HeroPadding.x + innerWidth - layout.TotalWidth,
                    CenteredBoardTop + flow.DifficultyTop,
                    layout.TotalWidth,
                    flow.DifficultyHeight);
            }
        }

        /// <summary>
        /// Hero 卡中部对局控制区的比赛时长段选（设计盒）：黑色「时长」角签 + 3 档药丸。
        /// 位于难度行正下方，与难度行严格对齐铺满 innerWidth。
        /// </summary>
        public HomeBox HeroDuration
        {
            get
            {
                float innerWidth = HeroBox.x - HeroPadding.x * 2f;
                HeroFlowLayout flow = MenuLayoutMath.ResolveHeroFlow(this, BoardHeight, ShowTopButton);
                MatchDurationBarLayout layout = MenuLayoutMath.ResolveHeroDuration(this, innerWidth);
                return new HomeBox(
                    Margin + HeroPadding.x + innerWidth - layout.TotalWidth,
                    CenteredBoardTop + flow.DurationTop,
                    layout.TotalWidth,
                    flow.DurationHeight);
            }
        }

        /// <summary>Hero 卡底部主 CTA（「开始游戏 》》」）的绝对盒子。</summary>
        public HomeBox HeroCta => new HomeBox(
            Margin + HeroPadding.x,
            CardTop + BoardHeight - HeroPadding.y - CtaSize.y,
            CtaSize.x,
            CtaSize.y);

        /// <summary>顶部右侧小按钮（移动端「自定义按键」；桌面端返回空盒）。</summary>
        public HomeBox TopButton => !ShowTopButton
            ? default
            : new HomeBox(
                MenuLayoutMath.RefWidth - Margin - SecondarySize.x,
                Margin,
                SecondarySize.x,
                SecondarySize.y);

        /// <summary>左下黑底角签（社区位）；移动端让位给整备卡，返回空盒。</summary>
        public HomeBox FooterTag => new HomeBox(
            Margin,
            MenuLayoutMath.RefHeight - FooterInset - FooterTagSize.y,
            FooterTagSize.x,
            FooterTagSize.y);

        /// <summary>右下黑底角签（版本号）。</summary>
        public HomeBox VersionTag => new HomeBox(
            MenuLayoutMath.RefWidth - Margin - VersionTagSize.x,
            MenuLayoutMath.RefHeight - FooterInset - VersionTagSize.y,
            VersionTagSize.x,
            VersionTagSize.y);
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
    /// 首页卡片区的运行时几何：设计值优先，宽高比不是 16:9 时按实际安全区收放。
    /// 由 <see cref="MenuLayoutMath.ResolveHomeBoard"/> 纯函数产出，EditMode 可直接断言。
    /// </summary>
    public struct HomeRuntimeBoard
    {
        public float Top;
        public float Height;

        /// <summary>Hero 卡左缘（安全区本地坐标）。安全区比设计宽时整块版面居中，不再固定为 Margin。</summary>
        public float HeroX;
        public float HeroWidth;
        public float GridX;
        public float TileWidth;
        public float TileHeight;
    }

    /// <summary>Hero 卡中部 Bot 难度段选（含黑色「难度」角签）在运行时实际采用的几何。</summary>
    public struct DifficultyBarLayout
    {
        public float TagWidth;
        public float TagGap;
        public float SegmentWidth;
        public float Gap;
        public float TotalWidth;
    }

    /// <summary>Hero 卡中部比赛时长段选（含黑色「时长」角签）在运行时实际采用的几何。</summary>
    public struct MatchDurationBarLayout
    {
        public float TagWidth;
        public float SegmentWidth;
        public float Gap;
        public float TagGap;
        public float TotalWidth;
    }

    /// <summary>
    /// Hero 卡纵向流式排版（方案1：从上到下 Header → 武器整备卡 → 难度行 → 时长行 → CTA）。
    /// 所有 Y 坐标均为 Hero 卡本地坐标（以 Hero 卡左上角为原点，向下为正）。
    /// </summary>
    public struct HeroFlowLayout
    {
        public float HeaderTop;
        public float HeaderHeight;
        public float WeaponTop;
        public float WeaponHeight;
        public float DifficultyTop;
        public float DifficultyHeight;
        public float DurationTop;
        public float DurationHeight;
        public float CtaTop;
        public float CtaHeight;
    }

    /// <summary>
    /// 首页 / 武器栏的纯逻辑版面表（桌面 1920×1080 基准，移动端一套更大的数）。
    ///
    /// 为什么单独抽出来：移动端「看起来很小」的根因是整块 UI 只按桌面比例排、只给字号 ×1.28；
    /// 手机屏幕物理尺寸只有显示器的零头，必须显式给出更大的盒子（CTA、卡片、图标、触控目标）。
    /// 这些数字要能被 EditMode 直接断言（移动端一律 &gt; 桌面端、抽屉宽度占比合理、卡片不重叠），
    /// 不再靠人眼看截图猜。
    ///
    /// 波次3a（t-1 新野兽派）：首页从「白底大留白 + 角落按钮」改为「深色舞台 + 左侧 Hero 大卡 +
    /// 右侧 2×2 次级卡」，盒子由 <see cref="HomeMenuMetrics"/> 的 HeroCard / Tile(i) / HeroCta 等给出。
    /// </summary>
    public static class MenuLayoutMath
    {
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        /// <summary>Bot 难度档数（新手 / 轻松 / 普通 / 硬核），与 GameSettingsMath 的档位区间一致。</summary>
        public const int DifficultyTierCount = 4;

        /// <summary>比赛时长档数（2 / 3 / 5 分钟），与 GameSettingsMath.AvailableMatchDurationMinutes 一致。</summary>
        public const int MatchDurationOptionCount = 3;

        /// <summary>移动端抽屉宽度占参考画布宽度的比例（桌面 620/1920≈32%）。</summary>
        public const float MobileDrawerWidthRatio = 0.82f;

        public static HomeMenuMetrics Home(bool mobile)
        {
            if (!mobile)
            {
                // 桌面：整屏铺满（卡片区 1792×816，横向占屏 93%），左下给整备卡留 200px 底带。
                return new HomeMenuMetrics
                {
                    Margin = 64f,
                    TagSize = new Vector2(132f, 44f),
                    TagFont = 20f,
                    HeroBox = new Vector2(728f, 816f),
                    TitleFont = 120f,
                    LatinFont = 32f,
                    SpecFont = 20f,
                    CtaSize = new Vector2(648f, 140f),
                    CtaFont = 46f,
                    SecondarySize = new Vector2(196f, 56f),
                    SecondaryFont = 20f,
                    VersionFont = 18f,
                    DecoCircle = 240f,
                    DecoSquare = 110f,

                    CardTop = 64f,
                    CardBottom = 200f,
                    ColumnGap = 32f,
                    GridWidth = 1032f,
                    TileGap = 24f,
                    TileBandHeight = 5f,
                    CardBorderWidth = 4f,
                    TileBorderWidth = 3f,
                    HeroPadding = new Vector2(40f, 32f),
                    TilePadding = new Vector2(24f, 24f),
                    TileTagSize = new Vector2(118f, 36f),
                    TileTagFont = 18f,
                    TileTitleFont = 44f,
                    TileInfoFont = 18f,
                    TileArrowFont = 40f,
                    DifficultyTagSize = new Vector2(80f, 44f),
                    DifficultyTagFont = 18f,
                    DifficultyTagGap = 12f,
                    DifficultySegmentSize = new Vector2(133f, 44f),
                    DifficultyGap = 8f,
                    DifficultyMinSegmentWidth = 56f,
                    DifficultyFont = 20f,
                    DurationTagSize = new Vector2(80f, 44f),
                    DurationTagFont = 18f,
                    DurationTagGap = 12f,
                    DurationSegmentSize = new Vector2(178f, 44f),
                    DurationGap = 11f,
                    DurationMinSegmentWidth = 60f,
                    DurationFont = 20f,
                    DurationRowGap = 14f,
                    FooterTagSize = new Vector2(320f, 36f),
                    FooterTagFont = 18f,
                    VersionTagSize = new Vector2(300f, 36f),
                    FooterInset = 18f,
                    ShowTopButton = false,
                };
            }

            // 移动端：顶部留出「自定义按键」小按钮；卡片区 1856×704，横向占屏 97%。
            // CTA 860×160、Hero 940 宽：手机逻辑画布不宽，靠"更大更高的盒子"而不是继续放大字号。
            return new HomeMenuMetrics
            {
                Margin = 32f,
                TagSize = new Vector2(168f, 52f),
                TagFont = 20f,
                HeroBox = new Vector2(940f, 704f),
                TitleFont = 120f,
                LatinFont = 34f,
                SpecFont = 22f,
                CtaSize = new Vector2(860f, 190f),
                CtaFont = 48f,
                SecondarySize = new Vector2(300f, 96f),
                SecondaryFont = 22f,
                VersionFont = 20f,
                DecoCircle = 320f,
                DecoSquare = 130f,

                CardTop = 148f,
                // 188（原 208）：方案1 把难度/时长搬到武器卡下方后，Hero 卡需要多 20px 才装得下
                // 「Header + 整备卡 + 两行设置 + 190 高 CTA」并保留呼吸位；底部版本角签仍有余量。
                CardBottom = 188f,
                ColumnGap = 20f,
                GridWidth = 896f,
                TileGap = 20f,
                TileBandHeight = 5f,
                CardBorderWidth = 4f,
                TileBorderWidth = 3f,
                HeroPadding = new Vector2(40f, 32f),
                TilePadding = new Vector2(24f, 22f),
                TileTagSize = new Vector2(140f, 44f),
                TileTagFont = 20f,
                TileTitleFont = 44f,
                TileInfoFont = 18f,
                TileArrowFont = 44f,
                DifficultyTagSize = new Vector2(100f, 64f),
                DifficultyTagFont = 18f,
                DifficultyTagGap = 12f,
                DifficultySegmentSize = new Vector2(181f, 64f),
                DifficultyGap = 8f,
                DifficultyMinSegmentWidth = 64f,
                DifficultyFont = 20f,
                DurationTagSize = new Vector2(100f, 64f),
                DurationTagFont = 18f,
                DurationTagGap = 12f,
                DurationSegmentSize = new Vector2(242f, 64f),
                DurationGap = 11f,
                DurationMinSegmentWidth = 72f,
                DurationFont = 20f,
                DurationRowGap = 10f,
                // 移动端左下是「武器整备」整备卡的地盘，社区角签不显示（返回空盒）。
                FooterTagSize = Vector2.zero,
                FooterTagFont = 18f,
                VersionTagSize = new Vector2(360f, 44f),
                FooterInset = 20f,
                ShowTopButton = true,
            };
        }

        public static LoadoutCardMetrics LoadoutCard(bool mobile)
        {
            if (!mobile)
            {
                return new LoadoutCardMetrics
                {
                    Size = new Vector2(648f, 190f),
                    IconDiameter = 110f,
                    TitleFont = 18f,
                    NameFont = 38f,
                    ChevronSize = 64f,
                    ChevronFont = 26f,
                };
            }

            return new LoadoutCardMetrics
            {
                // 170（原 220→200）：方案1 把难度/时长搬到武器卡下方后，整备卡收薄给两行设置腾位；
                // 图标 112 居中后上下留白 19，仍然可点可读。
                Size = new Vector2(860f, 150f),
                IconDiameter = 120f,
                TitleFont = 22f,
                NameFont = 42f,
                ChevronSize = 76f,
                ChevronFont = 32f,
            };
        }

        public static WeaponDrawerMetrics Drawer(bool mobile)
        {
            if (!mobile)
            {
                return new WeaponDrawerMetrics
                {
                    // 桌面抽屉也走双列网格（与移动端同构）：1204 = 580（WeaponCardView 桌面卡宽）×2
                    // + 16 列间距 + 14×2 左右内缩，单元宽与卡片硬编码宽度严格一致；
                    // 7 张卡 4 行在参考画布一屏放下，无需滚动。
                    Width = 1204f,
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

        /// <summary>
        /// 首页卡片区的运行时几何（纯函数，MatchFlowUI 每次构建/重排时调用）：
        /// 高度跟着安全区高度走（20:9 手机的逻辑画布只有约 966 高，写死 1080 会把卡片压出屏幕）；
        /// 宽度按刘海内缩自适应；整体在安全区内**垂直居中**（上下留白均分，
        /// 见 <see cref="HomeMenuMetrics.CenteredBoardTop"/>——底部不再积压大片空白）。
        ///
        /// 防御：Android 启动/旋转瞬间 safeArea 与 Screen 可能短暂不在同一像素空间，
        /// 读出的「安全区」会比画布还大（真机 20:9 实测约大 18% 宽 / 24% 高），
        /// 直接采用会把 2×2 卡阵整列推出屏幕。画布尺寸有效时一律钳回画布；
        /// 画布未排版（0）时保持原值，由调用方的参考分辨率兜底。
        /// </summary>
        public static HomeRuntimeBoard ResolveHomeBoard(HomeMenuMetrics m, float safeWidth, float safeHeight,
            Vector2 canvasSize)
        {
            float safeW = canvasSize.x > 1f ? Mathf.Min(safeWidth, canvasSize.x) : safeWidth;
            float safeH = canvasSize.y > 1f ? Mathf.Min(safeHeight, canvasSize.y) : safeHeight;

            float height = Mathf.Max(360f, safeH - m.CardTop - m.CardBottom);
            float heroWidth = Mathf.Clamp(
                safeW - m.Margin * 2f - m.ColumnGap - m.GridWidth,
                m.HeroBox.x * 0.62f,
                m.HeroBox.x);

            // 列间距恒为 ColumnGap：安全区比「Hero + 2×2」设计总宽更宽时（20:9 手机、超宽窗口），
            // 多余宽度均分到左右外边距，整块版面水平居中；绝不能全部灌进 Hero 与卡阵之间——
            // 真机 2148 宽安全区曾因此得到 146px 的中部空场（设计值 20）。
            float boardWidth = heroWidth + m.ColumnGap + m.GridWidth;
            float heroX = Mathf.Max(m.Margin, (safeW - boardWidth) * 0.5f);

            return new HomeRuntimeBoard
            {
                Top = m.CenteredBoardTop,
                Height = height,
                HeroX = heroX,
                HeroWidth = heroWidth,
                GridX = heroX + heroWidth + m.ColumnGap,
                TileWidth = (m.GridWidth - m.TileGap) * 0.5f,
                TileHeight = Mathf.Max(140f, (height - m.TileGap) * 0.5f),
            };
        }

        /// <summary>整备卡与「头部设置行 / CTA」的最小呼吸位（Hero 卡本地 px）。</summary>
        public const float HeroWeaponSlotGap = 12f;

        /// <summary>
        /// Hero 卡纵向流式排版（方案1：从上到下 Header → 武器整备卡 → 难度行 → 时长行 → CTA）。
        /// 武器卡与两行设置视为一个「出战配置簇」（内部只留 12/16px），整簇在 Header 与 CTA 之间垂直居中；
        /// CTA 固定贴底，桌面给快捷键提示预留一条底带。窄屏时整簇贴住 Header 并保持顺序，绝不压 CTA。
        /// </summary>
        public static HeroFlowLayout ResolveHeroFlow(HomeMenuMetrics m, float heroHeight, bool mobile)
        {
            float headerTop = m.HeroPadding.y;
            float headerH = m.TagSize.y;
            float headerBottom = headerTop + headerH;

            float ctaH = m.CtaSize.y;
            float ctaTop = heroHeight - m.HeroPadding.y - ctaH;
            // 桌面 CTA 上方要放「SPACE / ENTER」提示，留一条底带；移动端只留呼吸位。
            float limitBottom = mobile ? ctaTop - 12f : ctaTop - 38f;

            float cardH = LoadoutCard(m.ShowTopButton).Size.y;
            float diffH = m.DifficultySegmentSize.y;
            float durH = m.DurationSegmentSize.y;
            float rowGap = m.DurationRowGap;
            float cardToSettingsGap = mobile ? 12f : 16f;

            float clusterH = cardH + cardToSettingsGap + diffH + rowGap + durH;
            float bandTop = headerBottom + HeroWeaponSlotGap;
            float freeH = Mathf.Max(0f, limitBottom - bandTop - clusterH);
            float topGap = freeH * 0.5f;

            float weaponTop = bandTop + topGap;
            float diffTop = weaponTop + cardH + cardToSettingsGap;
            float durTop = diffTop + diffH + rowGap;

            return new HeroFlowLayout
            {
                HeaderTop = headerTop,
                HeaderHeight = headerH,
                WeaponTop = weaponTop,
                WeaponHeight = cardH,
                DifficultyTop = diffTop,
                DifficultyHeight = diffH,
                DurationTop = durTop,
                DurationHeight = durH,
                CtaTop = ctaTop,
                CtaHeight = ctaH,
            };
        }

        /// <summary>向后兼容：读取流式排版中武器整备卡的顶端 Y 坐标。</summary>
        public static float ResolveHeroWeaponSlotTop(HomeMenuMetrics m, float heroHeight)
        {
            return ResolveHeroFlow(m, heroHeight, m.ShowTopButton).WeaponTop;
        }

        /// <summary>
        /// Hero 中部对局控制区难度段选的实际几何：黑色「难度」角签 + 4 档药丸，铺满 innerWidth。
        /// 窄宽时先收窄段宽（兜底 DifficultyMinSegmentWidth），绝不溢出。
        /// </summary>
        public static DifficultyBarLayout ResolveHeroDifficulty(HomeMenuMetrics m, float availableWidth)
        {
            float tagWidth = m.DifficultyTagSize.x;
            float tagGap = m.DifficultyTagGap;
            float gap = m.DifficultyGap;
            float segment = m.DifficultySegmentSize.x;
            float width = Mathf.Max(0f, availableWidth);
            int count = DifficultyTierCount;
            float gapTotal = gap * (count - 1);

            float total = tagWidth + tagGap + segment * count + gapTotal;
            if (total > width)
            {
                float fitted = (width - tagWidth - tagGap - gapTotal) / count;
                segment = Mathf.Clamp(fitted, m.DifficultyMinSegmentWidth, segment);
                total = tagWidth + tagGap + segment * count + gapTotal;
                if (total > width)
                {
                    // 极端窄（Hero 卡被压到设计下限以下）：允许跌破最小段宽兜底，优先保证不溢出。
                    segment = Mathf.Max(24f, fitted);
                    total = tagWidth + tagGap + segment * count + gapTotal;
                }
            }

            return new DifficultyBarLayout
            {
                TagWidth = tagWidth,
                TagGap = tagGap,
                SegmentWidth = segment,
                Gap = gap,
                TotalWidth = total,
            };
        }

        /// <summary>
        /// Hero 中部对局控制区时长段选的实际几何：黑色「时长」角签 + 3 档药丸，与难度行右缘严格对齐。
        /// 窄宽时先收窄段宽（兜底 DurationMinSegmentWidth），绝不溢出。
        /// </summary>
        public static MatchDurationBarLayout ResolveHeroDuration(HomeMenuMetrics m, float availableWidth)
        {
            float tagWidth = m.DurationTagSize.x;
            float tagGap = m.DurationTagGap;
            float gap = m.DurationGap;
            float segment = m.DurationSegmentSize.x;
            int count = MatchDurationOptionCount;
            float gapTotal = gap * (count - 1);
            float width = Mathf.Max(0f, availableWidth);

            float total = tagWidth + tagGap + segment * count + gapTotal;
            if (total > width)
            {
                float fitted = (width - tagWidth - tagGap - gapTotal) / count;
                segment = Mathf.Clamp(fitted, m.DurationMinSegmentWidth, segment);
                total = tagWidth + tagGap + segment * count + gapTotal;
                if (total > width)
                {
                    // 极端窄：允许跌破最小段宽兜底，优先保证不溢出。
                    segment = Mathf.Max(24f, fitted);
                    total = tagWidth + tagGap + segment * count + gapTotal;
                }
            }

            return new MatchDurationBarLayout
            {
                TagWidth = tagWidth,
                SegmentWidth = segment,
                Gap = gap,
                TagGap = tagGap,
                TotalWidth = total,
            };
        }
    }
}
