using NUnit.Framework;
using P1.Core;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 玩家档案与段位成长系统纯逻辑验证（EditMode）：
    /// 验证 INKWAVE XP 曲线、单局跨级累进、胜负经验公式、段位阈值、损坏存档安全兜底及重置清理。
    /// </summary>
    public class PlayerProfileTests
    {
        [SetUp]
        [TearDown]
        public void Cleanup()
        {
            PlayerProfile.Reset();
            PlayerProfile.InvalidateCache();
        }

        [Test]
        public void XpCurve_CalculatesCorrectRequirement()
        {
            // 800 + lvl * 350
            Assert.AreEqual(1150, PlayerProfile.GetXpRequiredForLevel(1));
            Assert.AreEqual(1500, PlayerProfile.GetXpRequiredForLevel(2));
            Assert.AreEqual(1850, PlayerProfile.GetXpRequiredForLevel(3));
            Assert.AreEqual(2200, PlayerProfile.GetXpRequiredForLevel(4));
            Assert.AreEqual(4300, PlayerProfile.GetXpRequiredForLevel(10));
            Assert.AreEqual(11300, PlayerProfile.GetXpRequiredForLevel(30));
        }

        [Test]
        public void RankTiers_MatchesExactThresholds()
        {
            // Tier 0: 1..4 (Fresh Recruit)
            Assert.AreEqual(0, PlayerProfile.GetRankTierIndex(1));
            Assert.AreEqual("Fresh Recruit", PlayerProfile.GetRankName(1));
            Assert.AreEqual(0, PlayerProfile.GetRankTierIndex(4));

            // Tier 1: 5..9 (Turf Scrapper)
            Assert.AreEqual(1, PlayerProfile.GetRankTierIndex(5));
            Assert.AreEqual("Turf Scrapper", PlayerProfile.GetRankName(5));
            Assert.AreEqual(1, PlayerProfile.GetRankTierIndex(9));

            // Tier 2: 10..19 (Ink Slinger)
            Assert.AreEqual(2, PlayerProfile.GetRankTierIndex(10));
            Assert.AreEqual("Ink Slinger", PlayerProfile.GetRankName(10));
            Assert.AreEqual(2, PlayerProfile.GetRankTierIndex(19));

            // Tier 3: 20..29 (Splat Veteran)
            Assert.AreEqual(3, PlayerProfile.GetRankTierIndex(20));
            Assert.AreEqual("Splat Veteran", PlayerProfile.GetRankName(20));
            Assert.AreEqual(3, PlayerProfile.GetRankTierIndex(29));

            // Tier 4: 30+ (Tide Legend)
            Assert.AreEqual(4, PlayerProfile.GetRankTierIndex(30));
            Assert.AreEqual("Tide Legend", PlayerProfile.GetRankName(30));
            Assert.AreEqual(4, PlayerProfile.GetRankTierIndex(99));
        }

        [Test]
        public void CalculateMatchXp_WinAndLossFormulas_ClampedAndRounded()
        {
            // 胜：1200 + 100*1 + 3*40 = 1200 + 100 + 120 = 1420
            var winStats = new CombatantMatchStats { TurfSquareMeters = 100f, Kills = 3 };
            int winXp = PlayerProfile.CalculateMatchXp(win: true, winStats);
            Assert.AreEqual(1420, winXp);

            // 负：500 + 50.4*1 + 1*40 = 500 + 50.4 + 40 = 590.4 -> 取整 590
            var loseStats = new CombatantMatchStats { TurfSquareMeters = 50.4f, Kills = 1 };
            int loseXp = PlayerProfile.CalculateMatchXp(win: false, loseStats);
            Assert.AreEqual(590, loseXp);

            // 负数与非法值安全钳制为 0
            var negativeStats = new CombatantMatchStats { TurfSquareMeters = -20f, Kills = -5 };
            int clampedXp = PlayerProfile.CalculateMatchXp(win: false, negativeStats);
            Assert.AreEqual(500, clampedXp);
        }

        [Test]
        public void ApplyResult_SingleLevelUp_AdvancesLevelAndKeepsRemainderXp()
        {
            var profile = new PlayerProfile { Level = 1, Xp = 0 };
            // Lv1 需要 1150 XP 升级。获得 1200 XP（胜利基础）。
            var stats = new CombatantMatchStats { TurfSquareMeters = 0f, Kills = 0 };
            var result = profile.ApplyResult(win: true, stats);

            Assert.AreEqual(1200, result.XpGained);
            Assert.AreEqual(1, result.LevelsGained);
            Assert.AreEqual(1, result.OldLevel);
            Assert.AreEqual(2, result.NewLevel);
            // 剩余经验：1200 - 1150 = 50
            Assert.AreEqual(50, result.NewXp);
            Assert.AreEqual(50, profile.Xp);
            Assert.AreEqual(2, profile.Level);
            Assert.AreEqual(1, profile.Matches);
            Assert.AreEqual(1, profile.Wins);
        }

        [Test]
        public void ApplyResult_MultiLevelUp_AcrossMultipleTiers()
        {
            var profile = new PlayerProfile { Level = 1, Xp = 0 };
            // Lv1 需要 1150, Lv2 需要 1500 -> 升到 Lv3 共需 2650
            // 注入大量涂地与击杀
            var stats = new CombatantMatchStats { TurfSquareMeters = 1500f, Kills = 10 };
            // 经验值：1200 + 1500*1 + 10*40 = 1200 + 1500 + 400 = 3100 XP
            var result = profile.ApplyResult(win: true, stats);

            Assert.AreEqual(3100, result.XpGained);
            Assert.AreEqual(2, result.LevelsGained);
            Assert.AreEqual(1, result.OldLevel);
            Assert.AreEqual(3, result.NewLevel);
            // 剩余经验：3100 - (1150 + 1500) = 3100 - 2650 = 450
            Assert.AreEqual(450, result.NewXp);
            Assert.AreEqual(3, profile.Level);
            Assert.AreEqual(450, profile.Xp);
        }

        [Test]
        public void Persistence_CorruptedValues_FallbackToSafeDefaults()
        {
            // 模拟 PlayerPrefs 中写入非法/破坏性数据
            PlayerPrefs.SetString(PlayerProfile.KeyName, "   ");
            PlayerPrefs.SetInt(PlayerProfile.KeyLevel, -99);
            PlayerPrefs.SetInt(PlayerProfile.KeyXp, -500);
            PlayerPrefs.SetInt(PlayerProfile.KeyWins, -10);
            PlayerPrefs.SetInt(PlayerProfile.KeyMatches, -2);
            PlayerPrefs.SetFloat(PlayerProfile.KeyTotalTurf, float.NaN);
            PlayerPrefs.Save();

            PlayerProfile.InvalidateCache();
            var loaded = PlayerProfile.Load();

            Assert.AreEqual(PlayerProfile.DefaultName, loaded.Name);
            Assert.AreEqual(1, loaded.Level);
            Assert.AreEqual(0, loaded.Xp);
            Assert.AreEqual(0, loaded.Wins);
            Assert.AreEqual(0, loaded.Matches);
            Assert.AreEqual(0f, loaded.TotalTurf);
        }

        [Test]
        public void Persistence_SaveAndLoad_RoundtripsAccurately()
        {
            var profile = new PlayerProfile
            {
                Name = "OCTO_CHAMP",
                Level = 15,
                Xp = 680,
                Wins = 42,
                Matches = 50,
                TotalTurf = 12345.5f
            };
            profile.Save();

            PlayerProfile.InvalidateCache();
            var loaded = PlayerProfile.Load();

            Assert.AreEqual("OCTO_CHAMP", loaded.Name);
            Assert.AreEqual(15, loaded.Level);
            Assert.AreEqual(680, loaded.Xp);
            Assert.AreEqual(42, loaded.Wins);
            Assert.AreEqual(50, loaded.Matches);
            Assert.AreEqual(12345.5f, loaded.TotalTurf, 1e-4f);
            Assert.AreEqual("Ink Slinger", loaded.RankName);
            Assert.AreEqual(2, loaded.RankTier);
        }

        [Test]
        public void Reset_ClearsAllKeysAndResetsProfile()
        {
            var profile = PlayerProfile.Current;
            profile.Level = 25;
            profile.Xp = 1000;
            profile.Wins = 10;
            profile.Matches = 15;
            profile.Save();

            PlayerProfile.Reset();

            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyLevel));
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyXp));
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyWins));

            var resetProfile = PlayerProfile.Current;
            Assert.AreEqual(1, resetProfile.Level);
            Assert.AreEqual(0, resetProfile.Xp);
            Assert.AreEqual(0, resetProfile.Wins);
        }
    }
}
