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
                Assert.IsTrue(grid.FitsIn(canvas), tag + " 2×2 卡阵不应超出参考画布");
                Assert.IsFalse(hero.Overlaps(grid), tag + " Hero 卡与 2×2 卡阵不应重叠");
                Assert.IsTrue(cta.FitsIn(canvas), tag + " CTA 不应超出参考画布");
                Assert.IsTrue(hero.Contains(cta.X + cta.Width * 0.5f, cta.Y + cta.Height * 0.5f),
                    tag + " CTA 必须落在 Hero 卡内");

                // 横向铺满：卡片区应 ≥85% 屏宽（消灭中部空场）
                float occupied = hero.Width + m.ColumnGap + grid.Width;
                Assert.GreaterOrEqual(occupied, MenuLayoutMath.RefWidth * 0.85f,
                    tag + " 横向内容总宽应 ≥85% 屏宽");

                // 2×2 四张卡互不重叠，且都在卡阵内
                for (int i = 0; i < 4; i++)
                {
                    HomeBox tile = m.Tile(i);
                    Assert.IsTrue(tile.FitsIn(canvas), tag + " 卡片 " + i + " 超出画布");
                    Assert.IsTrue(grid.Contains(tile.X + tile.Width * 0.5f, tile.Y + tile.Height * 0.5f),
                        tag + " 卡片 " + i + " 应在卡阵内");
                    for (int j = i + 1; j < 4; j++)
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

                // 移动端为「武器整备」整备卡预留的左下角签必须是空的
                Assert.AreEqual(mobile, m.FooterTag.IsEmpty, tag + " 左下角签仅在桌面显示");
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
            }
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
            // 未钳制时 GridX=1605 → 2×2 卡阵右列整列出屏；卡阵高 823 → CTA/下排被推出屏幕底。
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);
            Vector2 canvas = new Vector2(2146f, 966f);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(mobile, 2533f, 1199f, canvas);

            Assert.LessOrEqual(board.GridX + mobile.GridWidth, canvas.x + 0.01f,
                "钳制后 2×2 卡阵右缘不得超出画布（右列必须可见）");
            Assert.LessOrEqual(board.Top + board.Height, canvas.y + 0.01f,
                "钳制后卡片区底缘不得超出画布（CTA / 下排卡必须可见）");
            Assert.Less(board.Height, 700f, "卡片区高度必须按画布高收放，而不是按脏安全区撑爆");
            Assert.AreEqual(966f - mobile.CardTop - mobile.CardBottom, board.Height, 0.5f,
                "高度应等于画布高 - 上下留白");
            Assert.AreEqual(mobile.HeroBox.x, board.HeroWidth, 0.5f, "Hero 卡钳制后仍应为设计宽度");
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
            Assert.AreEqual((board.Height - desktop.TileGap) * 0.5f, board.TileHeight, 0.5f);
        }

        [Test]
        public void ResolveHomeBoard_UnlaidCanvasKeepsRawSafeArea()
        {
            // 画布还没排版（rect=0）时不钳制，保持旧行为（参考分辨率兜底由调用方处理）。
            HomeMenuMetrics mobile = MenuLayoutMath.Home(true);
            HomeRuntimeBoard board = MenuLayoutMath.ResolveHomeBoard(mobile, 2533f, 1199f, Vector2.zero);

            Assert.AreEqual(2533f - mobile.Margin - mobile.GridWidth, board.GridX, 0.5f);
            Assert.AreEqual(1199f - mobile.CardTop - mobile.CardBottom, board.Height, 0.5f);
        }
    }
}