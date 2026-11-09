using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 首页 / 武器栏版面表测试（P14-I 移动端重排）：
    /// 手机上「一看就很小」的根因是整块 UI 只按 1920 桌面比例排、只把字号 ×1.28。
    /// 这里用纯逻辑断言移动端每个可点/可读盒子都明显大于桌面端，
    /// 并且左右两个底部大件（整备卡 + 开赛按钮）在参考分辨率下不会互相压住。
    /// </summary>
    public class MenuLayoutMathTests
    {
        [Test]
        public void Home_MobileIsSubstantiallyBiggerThanDesktop()
        {
            HomeMenuMetrics desktop = MenuLayoutMath.Home(false);
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);

            // 边距不是「越大越移动端」：手机逻辑画布更窄，边距反而略小才不浪费宽度。
            Assert.GreaterOrEqual(mobile.Margin, 32f, "移动端基础留白不应小于 32");
            // 波次3a：桌面 CTA 已经是「Hero 卡满宽」，移动端靠绝对尺寸（≥820×200）保证拇指可点，
            // 不再用 1.4 倍比例断言（满宽后再翻倍会撑破画布）。
            Assert.GreaterOrEqual(mobile.CtaSize.x, 820f, "移动端开赛 CTA 宽度应 ≥820（规范）");
            Assert.Greater(mobile.CtaSize.x, desktop.CtaSize.x * 1.25f, "移动端 CTA 仍应明显宽于桌面");
            Assert.Greater(mobile.CtaSize.y, 180f, "移动端开赛按钮高度应 ≥180（拇指可点）");
            Assert.Greater(mobile.CtaFont, desktop.CtaFont, "移动端 CTA 字号应更大");
            // Hero 标题基础字号两平台一致（120），移动端由 UITypography ×1.28 放大到 154——
            // 这里只断言"基础字号不小"，放大由 UITypography 保证。
            Assert.GreaterOrEqual(mobile.TitleFont, desktop.TitleFont, "移动端 Hero 标题基础字号不应小于桌面");
            Assert.GreaterOrEqual(mobile.TagFont, desktop.TagFont, "移动端角签基础字号不应小于桌面");
            Assert.Greater(mobile.TagSize.y, desktop.TagSize.y, "移动端角签盒子应更高");
            Assert.Greater(mobile.SecondarySize.y, 84f, "移动端次级按钮高度应 ≥84（可点）");
        }

        [Test]
        public void Home_BoardFillsScreenWithoutOverlap()
        {
            foreach (bool mobile in new[] { false, true })
            {
                HomeMenuMetrics m = MenuLayoutMath.Home(mobile);
                Vector2 canvas = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
                string tag = mobile ? "移动端" : "桌面端";

                HomeBox hero = m.HeroCard;
                HomeBox grid = m.Grid;
                HomeBox cta = m.HeroCta;

                Assert.IsTrue(hero.FitsIn(canvas), tag + " Hero 卡不应超出参考画布");
                Assert.IsTrue(grid.FitsIn(canvas), tag + " 2×3 卡阵不应超出参考画布");
                Assert.IsFalse(hero.Overlaps(grid), tag + " Hero 卡与 2×3 卡阵不应重叠");
                Assert.IsTrue(cta.FitsIn(canvas), tag + " CTA 不应超出参考画布");
                Assert.IsTrue(hero.Contains(cta.X + cta.Width * 0.5f, cta.Y + cta.Height * 0.5f),
                    tag + " CTA 必须落在 Hero 卡内");

                // 横向铺满：卡片区应 ≥85% 屏宽（消灭中部空场）
                float occupied = hero.Width + m.ColumnGap + grid.Width;
                Assert.GreaterOrEqual(occupied, MenuLayoutMath.RefWidth * 0.85f,
                    tag + " 横向内容总宽应 ≥85% 屏宽");

                // 2×3 六张卡互不重叠，且都在卡阵内
                for (int i = 0; i < 6; i++)
                {
                    HomeBox tile = m.Tile(i);
                    Assert.IsTrue(tile.FitsIn(canvas), tag + " 卡片 " + i + " 超出画布");
                    Assert.IsTrue(grid.Contains(tile.X + tile.Width * 0.5f, tile.Y + tile.Height * 0.5f),
                        tag + " 卡片 " + i + " 应在卡阵内");
                    // 行高变矮后仍必须是可读可点的盒子：两平台设计值都 ≥200
                    // （桌面 256 / 移动端 235），矮到这个值以下角签 + 标题 + 信息行就排不下。
                    Assert.GreaterOrEqual(tile.Height, 200f, tag + " 卡片 " + i + " 行高不应矮于 200");
                    for (int j = i + 1; j < 6; j++)
                    {
                        Assert.IsFalse(tile.Overlaps(m.Tile(j)), tag + " 卡片 " + i + " 与 " + j + " 重叠");
                    }
                }

                // 角签不能压住卡片区
                foreach (HomeBox box in new[] { m.FooterTag, m.VersionTag, m.TopButton })
                {
                    if (box.IsEmpty) continue;
                    Assert.IsTrue(box.FitsIn(canvas), tag + " 角签超出画布");
                    Assert.IsFalse(box.Overlaps(hero) || box.Overlaps(grid), tag + " 角签压到卡片");
                }

                // 左下角签两平台都必须为空：移动端让位给「武器整备」整备卡，
                // 桌面端的「游戏共创 · 单机 4V4」角签也已下线。
                Assert.IsTrue(m.FooterTag.IsEmpty, tag + " 左下角签已下线（两平台都不显示）");
            }
        }

        [Test]
        public void Home_BottomActionsDoNotOverlapAtReferenceWidth()
        {
            foreach (bool mobile in new[] { false, true })
            {
                HomeMenuMetrics home = MenuLayoutMath.Home(mobile);
                LoadoutCardMetrics loadout = MenuLayoutMath.LoadoutCard(mobile);

                float occupied = home.Margin * 2f + loadout.Size.x + home.CtaSize.x;
                Assert.Less(occupied, MenuLayoutMath.RefWidth,
                    (mobile ? "移动端" : "桌面端") + "整备卡 + 开赛按钮在 1920 参考宽度下不应重叠");
            }
        }

        [Test]
        public void Home_HeroWeaponSlot_FitsInHeroCardAndDoesNotOverlapCta()
        {
            foreach (bool mobile in new[] { false, true })
            {
                string tag = mobile ? "[移动端]" : "[桌面端]";
                HomeMenuMetrics home = MenuLayoutMath.Home(mobile);
                HomeBox hero = home.HeroCard;
                HomeBox slot = home.HeroWeaponSlot;
                HomeBox cta = home.HeroCta;

                Assert.IsTrue(hero.Contains(slot.Center.x, slot.Center.y), tag + " 武器整备卡槽中心必须落在 Hero 卡内");
                Assert.IsFalse(slot.Overlaps(cta), tag + " 武器整备卡槽与开始游戏按钮不得重叠");
                Assert.GreaterOrEqual(cta.Y, slot.Bottom, tag + " 开始游戏按钮必须在武器整备卡槽下方");
                Assert.GreaterOrEqual(slot.Y, home.HeroTag.Bottom + 20f, tag + " 武器整备卡槽必须在 HeroTag 下方且留出充裕呼吸间距");
                Assert.IsFalse(slot.Overlaps(home.HeroTag), tag + " 武器整备卡槽与 HeroTag 标签不得重叠");
            }
        }

        [Test]
        public void ResolveHeroFlow_StacksHeaderWeaponSettingsCtaTopToBottom()
        {
            foreach (bool mobile in new[] { false, true })
            {
                string tag = mobile ? "[移动端]" : "[桌面端]";
                HomeMenuMetrics home = MenuLayoutMath.Home(mobile);
                float height = home.BoardHeight;
                HeroFlowLayout flow = MenuLayoutMath.ResolveHeroFlow(home, height, mobile);
                float cardH = MenuLayoutMath.LoadoutCard(mobile).Size.y;

                Assert.AreEqual(home.HeroPadding.y, flow.HeaderTop, 0.01f, tag + " Header 必须贴 Hero 内边距顶部");
                Assert.AreEqual(home.TagSize.y, flow.HeaderHeight, 0.01f, tag + " Header 高度 = PLAY 角签高度");
                Assert.AreEqual(cardH, flow.WeaponHeight, 0.01f, tag + " 武器卡高度来自 LoadoutCard");

                // 方案1 的硬约束：Header → 武器卡 → 难度行 → 时长行 → CTA 严格自上而下，不重叠。
                Assert.GreaterOrEqual(flow.WeaponTop, flow.HeaderTop + flow.HeaderHeight + MenuLayoutMath.HeroWeaponSlotGap - 0.01f,
                    tag + " 武器卡必须在 Header 下方并留出呼吸位");
                Assert.GreaterOrEqual(flow.DifficultyTop, flow.WeaponTop + flow.WeaponHeight + MenuLayoutMath.HeroWeaponSlotGap - 0.01f,
                    tag + " 难度行必须在武器卡下方");
                Assert.GreaterOrEqual(flow.DurationTop, flow.DifficultyTop + flow.DifficultyHeight + home.DurationRowGap - 0.01f,
                    tag + " 时长行必须在难度行下方");
                Assert.LessOrEqual(flow.DurationTop + flow.DurationHeight, flow.CtaTop + 0.01f,
                    tag + " 时长行不得压住开始游戏按钮");
                Assert.AreEqual(height - home.HeroPadding.y - home.CtaSize.y, flow.CtaTop, 0.01f,
                    tag + " CTA 必须贴 Hero 底部内边距上方");

                // 极矮卡片区：仍保持「不压 CTA」的最低优先序。
                HeroFlowLayout tight = MenuLayoutMath.ResolveHeroFlow(home, 420f, mobile);
                Assert.GreaterOrEqual(tight.WeaponTop, tight.HeaderTop + tight.HeaderHeight + 12f - 0.01f,
                    tag + " 极矮卡片区时武器卡仍不得压住 Header");
            }
        }

        [Test]
        public void Home_HeroSettingsRows_SitBelowWeaponSlot_AndAlignWithEachOther()
        {
            foreach (bool mobile in new[] { false, true })
            {
                string tag = mobile ? "[移动端]" : "[桌面端]";
                HomeMenuMetrics home = MenuLayoutMath.Home(mobile);
                Vector2 canvas = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
                HomeBox difficulty = home.HeroDifficulty;
                HomeBox duration = home.HeroDuration;
                HomeBox weaponSlot = home.HeroWeaponSlot;
                HomeBox hero = home.HeroCard;
                HomeBox cta = home.HeroCta;

                Assert.IsTrue(difficulty.FitsIn(canvas), tag + " 难度段选不应超出参考画布");
                Assert.IsTrue(duration.FitsIn(canvas), tag + " 时长段选不应超出参考画布");
                Assert.IsTrue(hero.Contains(difficulty.Center.x, difficulty.Center.y), tag + " 难度段选必须落在 Hero 卡内");
                Assert.IsTrue(hero.Contains(duration.Center.x, duration.Center.y), tag + " 时长段选必须落在 Hero 卡内");

                // 方案1：两行设置都在武器卡下方，且顺序为 难度 → 时长。
                Assert.GreaterOrEqual(difficulty.Y, weaponSlot.Bottom, tag + " 难度行必须在武器整备卡下方");
                Assert.GreaterOrEqual(duration.Y, difficulty.Bottom, tag + " 时长行必须在难度行下方");
                Assert.LessOrEqual(duration.Bottom, cta.Y, tag + " 时长行不得压住开始游戏按钮");
                Assert.IsFalse(difficulty.Overlaps(duration), tag + " 难度行与时长行不得重叠");

                // 两行同宽、同左缘、同右缘（都铺满 Hero 内宽）：读作一组对局设置。
                Assert.AreEqual(difficulty.Width, duration.Width, 0.5f, tag + " 难度行与时长行必须同宽");
                Assert.AreEqual(difficulty.X, duration.X, 0.5f, tag + " 难度行与时长行必须同左缘");
                Assert.AreEqual(difficulty.Right, duration.Right, 0.5f, tag + " 难度行与时长行必须同右缘");
                Assert.AreEqual(home.HeroCard.X + home.HeroPadding.x, difficulty.X, 0.5f,
                    tag + " 设置行应贴 Hero 内边距左缘");
                Assert.AreEqual(4, MenuLayoutMath.DifficultyTierCount, tag + " 难度必须是四档（新手/轻松/普通/硬核）");
                Assert.AreEqual(3, MenuLayoutMath.MatchDurationOptionCount, tag + " 时长必须是三档（2/3/5 分）");
                Assert.GreaterOrEqual(home.DifficultyTagSize.x, 72f, tag + " 「难度」角签要放得下两字");
                Assert.GreaterOrEqual(home.DurationTagSize.x, 72f, tag + " 「时长」角签要放得下两字");
                Assert.GreaterOrEqual(home.DifficultySegmentSize.y, 44f, tag + " 难度按钮高度不得低于可点下限");
                Assert.GreaterOrEqual(home.DurationSegmentSize.y, 44f, tag + " 时长按钮高度不得低于可点下限");

                if (mobile)
                {
                    // 由竖向胶囊改为横向长条后，触控高度仍必须守住设置控件 64px 下限。
                    Assert.GreaterOrEqual(home.DifficultySegmentSize.y, 64f, "移动端难度行高度必须 ≥64（触控下限）");
                    Assert.GreaterOrEqual(home.DurationSegmentSize.y, 64f, "移动端时长行高度必须 ≥64（触控下限）");
                    Assert.GreaterOrEqual(home.DifficultyTagSize.x, 96f, "移动端「难度」角签应 ≥96 宽");
                }
            }
        }

        [Test]
        public void ResolveHeroDuration_ShrinksSegmentsWithoutOverflowingAvailableWidth()
        {
            HomeMenuMetrics desktop = MenuLayoutMath.Home(false);

            // 648 = 桌面 Hero 内宽：角签 + 3 段 + 间隙正好铺满，保持设计段宽。
            MatchDurationBarLayout design = MenuLayoutMath.ResolveHeroDuration(desktop, 648f);
            Assert.AreEqual(desktop.DurationSegmentSize.x, design.SegmentWidth, 0.5f,
                "宽度充足时应保持设计段宽");
            Assert.AreEqual(desktop.DurationTagSize.x, design.TagWidth, 0.5f,
                "角签宽度固定，不随可用宽度收窄");
            Assert.AreEqual(648f, design.TotalWidth, 0.5f, "设计宽度下应正好铺满 Hero 内宽");

            // 极窄：Hero 卡被压到最小宽度（728 × 0.62 → 内宽 371）时的可用区
            const float narrowAvailable = 371f;
            MatchDurationBarLayout narrow = MenuLayoutMath.ResolveHeroDuration(desktop, narrowAvailable);
            Assert.Less(narrow.SegmentWidth, desktop.DurationSegmentSize.x, "窄宽必须收窄段宽");
            Assert.GreaterOrEqual(narrow.SegmentWidth, desktop.DurationMinSegmentWidth,
                "段宽不得缩到低于版面表下限");
            Assert.LessOrEqual(narrow.TotalWidth, narrowAvailable + 0.01f,
                "收窄后的总宽必须落在可用宽度内");
        }

        [Test]
        public void ResolveHeroDifficulty_ShrinksSegmentsWithoutOverflowingAvailableWidth()
        {
            HomeMenuMetrics desktop = MenuLayoutMath.Home(false);

            // 648 = 桌面 Hero 内宽：角签 + 4 段 + 间隙正好铺满，保持设计段宽。
            DifficultyBarLayout design = MenuLayoutMath.ResolveHeroDifficulty(desktop, 648f);
            Assert.AreEqual(desktop.DifficultySegmentSize.x, design.SegmentWidth, 0.5f,
                "宽度充足时应保持设计段宽");
            Assert.AreEqual(desktop.DifficultyGap, design.Gap, 0.5f);
            Assert.AreEqual(648f, design.TotalWidth, 0.5f, "设计宽度下应正好铺满 Hero 内宽");

            // 极窄：Hero 卡被压到最小宽度（桌面 0.62 × 728）时的可用区
            const float narrowAvailable = 371f;
            DifficultyBarLayout narrow = MenuLayoutMath.ResolveHeroDifficulty(desktop, narrowAvailable);
            Assert.Less(narrow.SegmentWidth, desktop.DifficultySegmentSize.x, "窄宽必须收窄段宽");
            Assert.GreaterOrEqual(narrow.SegmentWidth, desktop.DifficultyMinSegmentWidth,
                "段宽不得缩到低于版面表下限");
            Assert.LessOrEqual(narrow.TotalWidth, narrowAvailable + 0.01f,
                "收窄后的总宽必须落在可用宽度内，不能压住 Hero 内边");
        }

        [Test]
        public void Drawer_DesktopTwoColumnFitsAllSevenWeaponsWithoutScroll()
        {
            WeaponDrawerMetrics d = MenuLayoutMath.Drawer(false);
            float spacing = 16f;
            float cellW = (d.Width - d.ScrollLeftInset - d.ScrollRightInset - spacing) * 0.5f;

            // 桌面抽屉与移动端同构走双列网格；单元宽必须与 WeaponCardView 的桌面卡宽 580 严格一致
            Assert.AreEqual(1204f, d.Width, 0.5f, "桌面抽屉宽 = 580×2 + 16 列间距 + 14×2 左右内缩");
            Assert.AreEqual(580f, cellW, 0.5f, "桌面网格单元宽必须等于 WeaponCardView 桌面卡宽 580");

            // 7 张卡 4 行，参考画布上一屏放下（无需滚动）
            int rows = Mathf.CeilToInt(7 / 2f);
            float contentH = d.ScrollTopInset + rows * d.CardHeight + (rows - 1) * spacing + 8f;
            float viewportH = MenuLayoutMath.RefHeight - d.HeaderHeight - 14f - 8f - d.ScrollBottomInset;
            Assert.LessOrEqual(contentH, viewportH,
                "桌面双列 7 卡 4 行应在一屏内看完，无需滚动");
        }

        [Test]
        public void Drawer_MobileUsesWideTwoColumnGrid()
        {
            WeaponDrawerMetrics desktop = MenuLayoutMath.Drawer(false);
            WeaponDrawerMetrics mobile = MenuLayoutMath.Drawer(true);

            Assert.GreaterOrEqual(mobile.Width, MenuLayoutMath.RefWidth * 0.8f, "移动端抽屉应 ≥80% 参考宽度");
            Assert.Greater(mobile.CardHeight, desktop.CardHeight * 1.3f, "移动端卡片应明显更高");
            Assert.GreaterOrEqual(mobile.CardHeight, 220f, "移动端卡片高度应 ≥220");

            float spacing = 16f;
            float cellWidth = (mobile.Width - mobile.ScrollLeftInset - mobile.ScrollRightInset - spacing) * 0.5f;
            Assert.GreaterOrEqual(cellWidth, 700f, "移动端双列卡每张应 ≥700 宽");

            // 抽屉内宽必须能容纳两列卡片
            Assert.LessOrEqual(cellWidth * 2f + spacing + mobile.ScrollLeftInset + mobile.ScrollRightInset,
                mobile.Width + 0.5f, "两列卡片 + 间距不得超出抽屉宽度");
        }

        [Test]
        public void Drawer_MobileIconsAndTypeAreBigger()
        {
            WeaponDrawerMetrics desktop = MenuLayoutMath.Drawer(false);
            WeaponDrawerMetrics mobile = MenuLayoutMath.Drawer(true);

            Assert.GreaterOrEqual(mobile.CardIconDiameter, 120f, "移动端武器色盘直径应 ≥120");
            Assert.Greater(mobile.CardIconDiameter, desktop.CardIconDiameter * 1.4f, "移动端武器色盘应明显大于桌面");
            Assert.Greater(mobile.NameFont, desktop.NameFont, "移动端武器名字号应更大");
            Assert.Greater(mobile.StatsFont, desktop.StatsFont, "移动端属性字号应更大");
            Assert.Greater(mobile.StatBlockSize, desktop.StatBlockSize, "移动端属性方块应更大");
            Assert.GreaterOrEqual(mobile.CloseSize.y, 80f, "移动端收起按钮高度应 ≥80（可点）");
        }

        [Test]
        public void LoadoutCard_MobileIsTouchSizedAndShowsRoundPlate()
        {
            LoadoutCardMetrics desktop = MenuLayoutMath.LoadoutCard(false);
            LoadoutCardMetrics mobile = MenuLayoutMath.LoadoutCard(true);

            Assert.GreaterOrEqual(mobile.Size.y, 132f, "移动端整备卡高度应 ≥132（触控目标底线）");
            Assert.Greater(mobile.Size.x, desktop.Size.x * 1.25f, "移动端整备卡应明显更宽（自适应 Hero 卡宽）");
            Assert.GreaterOrEqual(mobile.IconDiameter, 120f, "移动端整备卡色盘应 ≥120");
            Assert.Greater(mobile.ChevronSize, desktop.ChevronSize, "移动端箭头块应更大");
        }

        [Test]
        public void DrawerWidthRatio_StaysBelowFullScreen()
        {
            Assert.Less(MenuLayoutMath.MobileDrawerWidthRatio, 0.95f,
                "移动端抽屉要留出遮罩与边缘，不能顶满整屏");
            Assert.Greater(MenuLayoutMath.MobileDrawerWidthRatio, 0.7f,
                "移动端抽屉又不能退回桌面那种窄条");
        }

        [Test]
        public void ResolveHomeBoard_ClampsInflatedSafeAreaToCanvas()
        {
            // 真机 20:9 复现值：启动首帧 safeArea 与 Screen 短暂不在同一像素空间，
            // 读出的安全区（2533×1199）比画布（2146×966）还大。
            // 未钳制时 GridX=1605 → 2×3 卡阵右列整列出屏；卡阵高 823 → CTA/下排被推出屏幕底。
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);
            Vector2 canvas = new Vector2(2146f, 966f);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(mobile, 2533f, 1199f, canvas);

            Assert.LessOrEqual(board.GridX + mobile.GridWidth, canvas.x + 0.01f,
                "钳制后 2×3 卡阵右缘不得超出画布（右列必须可见）");
            Assert.LessOrEqual(board.Top + board.Height, canvas.y + 0.01f,
                "钳制后卡片区底缘不得超出画布（CTA / 下排卡必须可见）");
            Assert.AreEqual((mobile.CardTop + mobile.CardBottom) * 0.5f, board.Top, 0.5f,
                "移动端卡片区顶部应为上下留白的均分点（垂直居中）");
            Assert.Less(board.Height, 700f, "卡片区高度必须按画布高收放，而不是按脏安全区撑爆");
            Assert.AreEqual(966f - mobile.CardTop - mobile.CardBottom, board.Height, 0.5f,
                "高度应等于画布高 - 上下留白");
            Assert.AreEqual(mobile.HeroBox.x, board.HeroWidth, 0.5f, "Hero 卡钳制后仍应为设计宽度");
        }

        [Test]
        public void ResolveHomeBoard_WideSafeAreaKeepsColumnGapAndCentersBoard()
        {
            // 20:9 真机（2146 宽安全区）比「Hero 940 + 列距 20 + 卡阵 896」宽 290：
            // 旧实现把多出来的宽度全部灌进列间，Hero 与 2×3 之间形成 146px 中部空场。
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);
            Vector2 canvas = new Vector2(2146f, 966f);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(mobile, canvas.x, canvas.y, canvas);

            float gap = board.GridX - (board.HeroX + board.HeroWidth);
            Assert.AreEqual(mobile.ColumnGap, gap, 0.5f,
                "Hero 与 2×3 卡阵的列间距必须等于版面表 ColumnGap，不能随屏幕变宽而膨胀");

            float leftMargin = board.HeroX;
            float rightMargin = canvas.x - (board.GridX + mobile.GridWidth);
            Assert.AreEqual(leftMargin, rightMargin, 0.5f,
                "多余宽度应均分到左右外边距，让整块版面保持水平居中");
            Assert.Greater(leftMargin, mobile.Margin, "宽安全区的外边距应大于设计最小留白");
            Assert.AreEqual(mobile.HeroBox.x, board.HeroWidth, 0.5f, "宽屏仍保持 Hero 设计宽度");
        }

        [Test]
        public void ResolveHomeBoard_NormalSafeAreaMatchesDesign()
        {
            // 桌面 16:9、安全区=画布：运行时盒必须与设计表一致（回归锚点）。
            HomeMenuMetrics desktop = MenuLayoutMath.Home(false);
            Vector2 canvas = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(desktop, canvas.x, canvas.y, canvas);

            Assert.AreEqual(824f, board.GridX, 0.5f, "桌面 GridX = 1920 - 64 - 1032");
            Assert.AreEqual(desktop.BoardHeight, board.Height, 0.5f);
            Assert.AreEqual(desktop.HeroBox.x, board.HeroWidth, 0.5f);
            Assert.AreEqual((desktop.GridWidth - desktop.TileGap) * 0.5f, board.TileWidth, 0.5f);
            // 2×3：三行夹两条 TileGap
            Assert.AreEqual((board.Height - desktop.TileGap * 2f) / 3f, board.TileHeight, 0.5f);

            // 垂直居中：上下留白均分（历史版面压顶，底部积压 200px 空白）
            Assert.AreEqual((desktop.CardTop + desktop.CardBottom) * 0.5f, board.Top, 0.5f,
                "桌面卡片区顶部应为上下留白的均分点（垂直居中）");
            Assert.AreEqual(canvas.y, board.Top * 2f + board.Height, 0.5f,
                "上留白 + 卡片区 + 下留白应恰好占满参考画布高");
        }

        [Test]
        public void ResolveHomeBoard_UnlaidCanvasKeepsRawSafeArea()
        {
            // 画布还没排版（rect=0）时不钳制，保持旧行为（参考分辨率兜底由调用方处理）。
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(mobile, 2533f, 1199f, Vector2.zero);

            float boardWidth = mobile.HeroBox.x + mobile.ColumnGap + mobile.GridWidth;
            float expectedHeroX = (2533f - boardWidth) * 0.5f;
            Assert.AreEqual(expectedHeroX, board.HeroX, 0.5f);
            Assert.AreEqual(expectedHeroX + mobile.HeroBox.x + mobile.ColumnGap, board.GridX, 0.5f);
            Assert.AreEqual(1199f - mobile.CardTop - mobile.CardBottom, board.Height, 0.5f);
        }
    }
}