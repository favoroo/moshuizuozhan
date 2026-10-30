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
            Assert.Greater(mobile.CtaSize.x, desktop.CtaSize.x * 1.4f, "移动端开赛按钮宽度应至少是桌面 1.4 倍");
            Assert.Greater(mobile.CtaSize.y, 180f, "移动端开赛按钮高度应 ≥180（拇指可点）");
            Assert.Greater(mobile.CtaFont, desktop.CtaFont, "移动端 CTA 字号应更大");
            Assert.Greater(mobile.TitleFont, desktop.TitleFont, "移动端 Hero 标题字号应更大");
            Assert.Greater(mobile.TagFont, desktop.TagFont, "移动端模式标签字号应更大");
            Assert.Greater(mobile.SecondarySize.y, 84f, "移动端次级按钮高度应 ≥84（可点）");
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
            Assert.Greater(mobile.Size.x, desktop.Size.x * 1.4f, "移动端整备卡应明显更宽");
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
    }
}