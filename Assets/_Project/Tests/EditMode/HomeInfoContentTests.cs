using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 首页「战绩 / 键位说明」浮层的内容与度量测试：
    /// 键位表是首页浮层与暂停菜单共用的唯一数据源，战绩统计是纯函数，
    /// 这里断言分组、移动端触控文案、胜率/场均涂地与 0 局边界，避免以后再抄一份文案抄漂。
    /// </summary>
    public class HomeInfoContentTests
    {
        [Test]
        public void Controls_BothPlatforms_HaveBothGroupsAndUniqueKeys()
        {
            foreach (bool mobile in new[] { false, true })
            {
                var rows = HomeInfoContent.Controls(mobile);
                Assert.IsNotNull(rows, "键位表不得为空");
                Assert.Greater(rows.Count, 0, "键位表不得为空");

                var seen = new System.Collections.Generic.HashSet<string>();
                int movement = 0, combat = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(rows[i].Key), "键位键名不得为空");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(rows[i].Action), "键位动作不得为空");
                    Assert.IsTrue(seen.Add(rows[i].Key), "键位键名重复：" + rows[i].Key);
                    if (rows[i].Group == ControlGroup.Movement) movement++;
                    else combat++;
                }

                Assert.Greater(movement, 0, "必须有移动组键位");
                Assert.Greater(combat, 0, "必须有战斗组键位");
            }

            Assert.AreEqual(8, HomeInfoContent.Controls(false).Count, "桌面键位表应为 8 行");
            Assert.AreEqual(6, HomeInfoContent.Controls(true).Count, "移动端键位表应为 6 行");
        }

        [Test]
        public void Controls_Mobile_UsesTouchLabels()
        {
            var rows = HomeInfoContent.Controls(true);
            string joined = string.Empty;
            for (int i = 0; i < rows.Count; i++) joined += rows[i].Key + "|" + rows[i].Action + "|";

            StringAssert.Contains("摇杆", joined);
            StringAssert.Contains("射击键", joined);
            StringAssert.Contains("潜墨键", joined);
            StringAssert.DoesNotContain("WASD", joined, "移动端不应出现键盘键位");
        }

        [Test]
        public void DynamicControls_Desktop_ReturnsEightRowsAndReflectsBindings()
        {
            DesktopKeyBindingService.ResetToDefaults();
            var rows = HomeInfoContent.DynamicControls(false);
            Assert.AreEqual(8, rows.Count);
            Assert.AreEqual("移动", rows[0].Action);
            Assert.AreEqual("WASD", rows[0].Key);
            Assert.AreEqual("跳跃", rows[2].Action);
            Assert.AreEqual("空格", rows[2].Key);

            var mobileRows = HomeInfoContent.DynamicControls(true);
            Assert.AreEqual(6, mobileRows.Count);
        }

        [Test]
        public void RowsInGroup_SplitsMovementAndCombat()
        {
            ControlRow[] movement = HomeInfoContent.RowsInGroup(false, ControlGroup.Movement);
            ControlRow[] combat = HomeInfoContent.RowsInGroup(false, ControlGroup.Combat);

            Assert.Greater(movement.Length, 0);
            Assert.Greater(combat.Length, 0);
            Assert.AreEqual(HomeInfoContent.Controls(false).Count, movement.Length + combat.Length,
                "分组后行数之和应等于总行数");

            for (int i = 0; i < movement.Length; i++)
                Assert.AreEqual(ControlGroup.Movement, movement[i].Group);
            for (int i = 0; i < combat.Length; i++)
                Assert.AreEqual(ControlGroup.Combat, combat[i].Group);
        }

        [Test]
        public void Rules_ReturnsFourNonEmptyLines()
        {
            foreach (bool mobile in new[] { false, true })
            {
                string[] rules = HomeInfoContent.Rules(mobile);
                Assert.AreEqual(4, rules.Length, "规则速记应为 4 条");
                for (int i = 0; i < rules.Length; i++)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(rules[i]), "规则第 " + i + " 条为空");
                }
            }
        }

        [Test]
        public void Record_ComputesWinRateAndPerMatchTurf()
        {
            var profile = new PlayerProfile
            {
                Matches = 4,
                Wins = 3,
                TotalTurf = 400f,
            };

            RecordSummary summary = HomeInfoContent.Summarize(profile);

            Assert.IsTrue(summary.HasMatches);
            Assert.AreEqual(4, summary.Matches);
            Assert.AreEqual(3, summary.Wins);
            Assert.AreEqual(0.75f, summary.WinRate01, 0.0001f);
            Assert.AreEqual(100f, summary.TurfPerMatch, 0.0001f);
            Assert.AreEqual("75%", HomeInfoContent.Percent(summary.WinRate01));
        }

        [Test]
        public void Record_ClampsWinsAboveMatches()
        {
            var profile = new PlayerProfile { Matches = 4, Wins = 10, TotalTurf = 40f };
            RecordSummary summary = HomeInfoContent.Summarize(profile);

            Assert.AreEqual(1f, summary.WinRate01, 0.0001f, "脏存档胜场 > 场次时胜率必须钳到 100%");
            Assert.AreEqual(100f, summary.WinRate01 * 100f, 0.0001f);
        }

        [Test]
        public void Record_ZeroMatches_DoesNotDivideByZero()
        {
            var profile = new PlayerProfile { Matches = 0, Wins = 0, TotalTurf = 0f };
            RecordSummary summary = HomeInfoContent.Summarize(profile);

            Assert.IsFalse(summary.HasMatches);
            Assert.AreEqual(0f, summary.WinRate01, 0.0001f);
            Assert.AreEqual(0f, summary.TurfPerMatch, 0.0001f);
            Assert.AreEqual("0%", HomeInfoContent.Percent(summary.WinRate01));
        }

        [Test]
        public void Record_ComputesLevelProgressAndXpToNext()
        {
            var profile = new PlayerProfile { Level = 3, Xp = 100 };

            RecordSummary summary = HomeInfoContent.Summarize(profile);

            // 800 + Level * 350 = 1850
            Assert.AreEqual(1850, summary.XpForNextLevel);
            Assert.AreEqual(1750, summary.XpToNextLevel);
            Assert.AreEqual(100f / 1850f, summary.LevelProgress01, 0.0001f);
            Assert.AreEqual("Fresh Recruit", summary.RankName);
        }

        [Test]
        public void Record_NullProfile_FallsBackToDefaults()
        {
            RecordSummary summary = HomeInfoContent.Summarize(null);

            Assert.AreEqual(PlayerProfile.DefaultName, summary.PlayerName);
            Assert.AreEqual(PlayerProfile.DefaultLevel, summary.Level);
            Assert.AreEqual(0, summary.Matches);
            Assert.IsFalse(summary.HasMatches);
        }

        [Test]
        public void Record_CareerAndAchievementFields_FlowIntoSummary()
        {
            var profile = new PlayerProfile
            {
                TotalKills = 88,
                BestMatchStreak = 5
            };
            profile.MarkAchievementUnlocked("ach_kill_3");
            profile.MarkAchievementUnlocked("ach_cover_50");

            RecordSummary summary = HomeInfoContent.Summarize(profile);

            Assert.AreEqual(88, summary.TotalKills);
            Assert.AreEqual(5, summary.BestStreak);
            Assert.AreEqual(2, summary.AchievementsUnlocked);
            Assert.AreEqual(AchievementCatalog.TotalCount, summary.AchievementsTotal);
        }

        [Test]
        public void Record_NullProfile_ReportsZeroAchievements()
        {
            RecordSummary summary = HomeInfoContent.Summarize(null);

            Assert.AreEqual(0, summary.AchievementsUnlocked);
            Assert.AreEqual(AchievementCatalog.TotalCount, summary.AchievementsTotal);
        }

        [Test]
        public void Layout_MobileTouchTargetsAreBiggerThanDesktop()
        {
            HomeInfoMetrics desktop = HomeInfoLayoutMath.Metrics(false);
            HomeInfoMetrics mobile = HomeInfoLayoutMath.Metrics(true);

            Assert.Greater(mobile.CloseSize.y, desktop.CloseSize.y, "移动端关闭键必须更大，便于拇指点击");
            Assert.GreaterOrEqual(mobile.CloseSize.y, 88f, "移动端关闭键不得小于 88");
            Assert.Greater(mobile.RowHeight, desktop.RowHeight, "移动端键位行高必须更大");
            Assert.Greater(mobile.TitleFont, desktop.TitleFont, "移动端标题字号必须更大");
            Assert.Greater(mobile.StatValueFont, desktop.StatValueFont, "移动端数据字号必须更大");
        }

        [Test]
        public void Layout_PanelSizeNeverExceedsSafeArea()
        {
            foreach (bool mobile in new[] { false, true })
            {
                Vector2 size = HomeInfoLayoutMath.ResolvePanelSize(mobile, 1920f, 1080f);
                Assert.LessOrEqual(size.x, 1920f, "面板宽度不得超出安全区");
                Assert.LessOrEqual(size.y, 1080f, "面板高度不得超出安全区");
                Assert.Greater(size.x, 320f);
                Assert.Greater(size.y, 320f);
            }

            Vector2 small = HomeInfoLayoutMath.ResolvePanelSize(false, 900f, 620f);
            Assert.LessOrEqual(small.x, 900f);
            Assert.LessOrEqual(small.y, 620f);

            Vector2 mobilePanel = HomeInfoLayoutMath.ResolvePanelSize(true, 1920f, 1080f);
            Vector2 desktopPanel = HomeInfoLayoutMath.ResolvePanelSize(false, 1920f, 1080f);
            Assert.Greater(mobilePanel.x, desktopPanel.x, "移动端面板应吃满安全区宽度，而不是沿用桌面弹窗尺寸");
        }

        /// <summary>
        /// 战绩数据格：格子够高时保持「标签在上 / 大数字在下」，放不下时改「标签左 / 数值右」。
        /// 两种模式下标签盒与数值盒都必须落在格子里且互不相交——这是首页战绩页叠字的回归锁。
        /// </summary>
        [Test]
        public void Layout_StatTileNeverOverlapsAtAnyTileHeight()
        {
            HomeInfoMetrics desktop = HomeInfoLayoutMath.Metrics(false);
            float[] labelFonts = { 20f, 30f, 36f };   // 桌面 / 移动端渲染字号（UITypography ×1.28 之后）
            float[] valueFonts = { 48f, 62f, 76f };

            foreach (float labelFont in labelFonts)
            {
                foreach (float valueFont in valueFonts)
                {
                    for (float tileHeight = 40f; tileHeight <= 260f; tileHeight += 2f)
                    {
                        HomeInfoStatTileLayout slot =
                            HomeInfoLayoutMath.ResolveStatTileLayout(tileHeight, labelFont, valueFont);
                        float available = tileHeight - slot.Pad * 2f;

                        Assert.LessOrEqual(slot.LabelFont, labelFont + 0.01f, "字号只能收敛不能放大");
                        Assert.LessOrEqual(slot.ValueFont, valueFont + 0.01f, "字号只能收敛不能放大");

                        if (slot.Horizontal)
                        {
                            // 一行式：标签盒与数值盒都要落在格子内（两者竖中同高，靠左右走廊分开）
                            Assert.LessOrEqual(slot.LabelFont * 1.4f, available + 0.01f,
                                $"标签盒超高：h={tileHeight} label={labelFont} value={valueFont}");
                            Assert.LessOrEqual(slot.ValueFont * 1.15f, available + 0.01f,
                                $"数值盒超高：h={tileHeight} label={labelFont} value={valueFont}");
                        }
                        else
                        {
                            // 两段式：标签字形带与数值字形带之间至少留 6px 空档
                            Assert.GreaterOrEqual(available, slot.LabelFont + slot.ValueFont + 6f - 0.01f,
                                $"两段式放不下却仍走了两段：h={tileHeight} label={labelFont} value={valueFont}");
                        }
                    }
                }
            }

            // 桌面 1920×1080 的 2×4 格阵必须保持两段式（大数字才是设计主体）。
            float desktopTile = (514f - desktop.StatGap * 3f) / 4f;
            Assert.IsFalse(
                HomeInfoLayoutMath.ResolveStatTileLayout(desktopTile,
                    UITypography.FontSnapped(desktop.StatLabelFont),
                    UITypography.FontSnapped(desktop.StatValueFont)).Horizontal,
                "桌面数据格应保持「标签在上 / 大数字在下」");
        }

        /// <summary>
        /// 身份卡：五段自然高装不下时必须等比收敛，且收敛后一定装得下（否则段位会压进 XP 文字）。
        /// </summary>
        [Test]
        public void Layout_IdentityBlockFitsInShortContentBox()
        {
            HomeInfoMetrics desktop = HomeInfoLayoutMath.Metrics(false);
            float desktopNatural = HomeInfoLayoutMath.IdentityContentHeight(desktop,
                UITypography.FontSnapped(desktop.IdentityLevelFont),
                UITypography.FontSnapped(desktop.IdentityRankFont),
                UITypography.FontSnapped(desktop.IdentityLabelFont));
            Assert.AreEqual(1f, HomeInfoLayoutMath.FitScale(514f - 48f, desktopNatural), 0.0001f,
                "桌面内容区（514）放得下身份卡五段，不应收敛");

            // 手机横屏 20:9：内容区只有 437，且移动端度量还要再过一遍 UITypography ×1.28。
            // 旧版固定像素在这里算出「段位 281..340 / XP 文字 214..263」，两段直接叠死。
            HomeInfoMetrics mobile = HomeInfoLayoutMath.Metrics(true);
            const float phoneContentHeight = 437f;
            const float boost = 1.28f;
            float phoneBox = phoneContentHeight - mobile.Padding * 0.6f * 2f;
            float phoneNatural = HomeInfoLayoutMath.IdentityContentHeight(mobile,
                mobile.IdentityLevelFont * boost, mobile.IdentityRankFont * boost,
                mobile.IdentityLabelFont * boost);
            float fit = HomeInfoLayoutMath.FitScale(phoneBox, phoneNatural);

            Assert.Less(fit, 1f, "手机横屏内容区放不下五段原尺寸，必须收敛");
            Assert.GreaterOrEqual(fit, 0.7f, "收敛不能到读不清的程度");
            Assert.LessOrEqual(phoneNatural * fit, phoneBox + 0.01f, "收敛后必须装得下，否则段位会压进 XP 文字");
        }

        [Test]
        public void Layout_FitScaleNeverEnlarges()
        {
            Assert.AreEqual(1f, HomeInfoLayoutMath.FitScale(500f, 400f), 0.0001f, "装得下时不放大");
            Assert.AreEqual(0.75f, HomeInfoLayoutMath.FitScale(300f, 400f), 0.0001f, "装不下时等比收敛");
            Assert.AreEqual(1f, HomeInfoLayoutMath.FitScale(0f, 0f), 0.0001f, "退化输入不得除零");
        }
    }
}
