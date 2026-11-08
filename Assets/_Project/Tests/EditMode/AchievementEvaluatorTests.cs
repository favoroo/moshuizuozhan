using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 成就系统单元测试（EditMode）：
    /// 1. 目录完整性（37 条、id 唯一、文案与图标键非空）；
    /// 2. 单局指标真值表（击杀 / 覆盖率 / 零阵亡 / 阵亡获胜 / 连杀 / 大招 / 首杀 / 完封 / 险胜 / 单命连杀 / 复仇 / 压哨 / 彩蛋）；
    /// 3. 生涯指标判定与进度文案；
    /// 4. AchievementService 结算回环（解锁、不重复、新纪录、生涯累计）；
    /// 5. MatchStatsService 首杀 / 连杀峰值 / 大招 / 单命击杀 / 复仇 / 压哨窗口登记。
    /// </summary>
    public class AchievementEvaluatorTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            MatchStatsService.Clear();
            PlayerProfile.Reset();
            PlayerProfile.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            MatchStatsService.Clear();
            PlayerProfile.Reset();
            PlayerProfile.InvalidateCache();

            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] != null) Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        // ==================== 目录完整性 ====================

        [Test]
        public void Catalog_HasExpectedCount_WithUniqueIdsAndContent()
        {
            Assert.AreEqual(37, AchievementCatalog.All.Length);
            Assert.AreEqual(AchievementCatalog.All.Length, AchievementCatalog.TotalCount);

            var seen = new HashSet<string>();
            for (int i = 0; i < AchievementCatalog.All.Length; i++)
            {
                AchievementDef def = AchievementCatalog.All[i];
                Assert.IsTrue(seen.Add(def.Id), "重复成就 id: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.Name), "成就缺名称: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.Description), "成就缺描述: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.IconName), "成就缺图标键: " + def.Id);
                Assert.Greater(def.Target, 0f, "成就目标值必须为正: " + def.Id);
            }

            Assert.IsTrue(AchievementCatalog.TryGet("ach_kill_10", out AchievementDef kill10));
            Assert.AreEqual("双位收割", kill10.Name);
            Assert.IsFalse(AchievementCatalog.TryGet("ach_not_exist", out _));
        }

        // ==================== 单局指标真值表 ====================

        private static MatchSettleContext Ctx(int kills = 0, int deaths = 0, float turf = 0f,
            int streak = 0, int specials = 0, float teamCoverage = 0f, float enemyCoverage = 100f,
            bool won = false, bool firstKill = false, int lifeKills = 0, int revenge = 0,
            bool clutch = false, int mvp = 0)
        {
            return new MatchSettleContext
            {
                Local = new CombatantMatchStats
                {
                    Kills = kills,
                    Deaths = deaths,
                    TurfSquareMeters = turf,
                    MaxStreak = streak,
                    SpecialCasts = specials,
                    MaxLifeKills = lifeKills,
                    RevengeKills = revenge,
                    ClutchKill = clutch
                },
                TeamCoveragePercent = teamCoverage,
                EnemyCoveragePercent = enemyCoverage,
                Won = won,
                LocalGotFirstKill = firstKill,
                MvpAwards = mvp,
            };
        }

        private static AchievementDef Def(string id)
        {
            Assert.IsTrue(AchievementCatalog.TryGet(id, out AchievementDef def));
            return def;
        }

        [Test]
        public void MatchKills_Thresholds()
        {
            AchievementDef def = Def("ach_kill_10");

            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 9)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 10)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 20)));

            AchievementDef def3 = Def("ach_kill_3");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def3, Ctx(kills: 2)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def3, Ctx(kills: 3)));

            AchievementDef def20 = Def("ach_kill_20");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def20, Ctx(kills: 19)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def20, Ctx(kills: 20)));
        }

        [Test]
        public void TeamCoverage_ThresholdsWithEpsilon()
        {
            AchievementDef def = Def("ach_cover_70");

            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(teamCoverage: 69.9f)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(teamCoverage: 70.0f)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(teamCoverage: 70.04f)));

            AchievementDef def80 = Def("ach_cover_80");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def80, Ctx(teamCoverage: 79.9f)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def80, Ctx(teamCoverage: 80.5f)));
        }

        [Test]
        public void ZeroDeathWithKill_RequiresBothConditions()
        {
            AchievementDef def = Def("ach_untouched");

            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 0, deaths: 0)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 2, deaths: 1)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 1, deaths: 0)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 5, deaths: 0)));
        }

        [Test]
        public void DeathsThenWin_RequiresWin()
        {
            AchievementDef def = Def("ach_undying");

            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(deaths: 5, won: false)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(deaths: 5, won: true)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(deaths: 4, won: true)));
        }

        [Test]
        public void MaxStreak_And_SpecialCasts()
        {
            AchievementDef streak5 = Def("ach_streak_5");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in streak5, Ctx(streak: 4)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in streak5, Ctx(streak: 5)));

            AchievementDef specials3 = Def("ach_special_3");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in specials3, Ctx(specials: 2)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in specials3, Ctx(specials: 3)));
        }

        [Test]
        public void FirstKill_OnlyWhenLocalGotIt()
        {
            AchievementDef def = Def("ach_first_blood");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx()));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(firstKill: true)));
        }

        [Test]
        public void EnemyCoverageBelow_StrictlyBelowAndMustWin()
        {
            AchievementDef def = Def("ach_shutout");

            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(won: true, enemyCoverage: 9.9f)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(won: true, enemyCoverage: 10f)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(won: false, enemyCoverage: 5f)));
        }

        [Test]
        public void NarrowWinMargin_MarginWithinTargetAndWon()
        {
            AchievementDef def = Def("ach_narrow_win");

            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def,
                Ctx(won: true, teamCoverage: 51f, enemyCoverage: 49f))); // 差 2.0
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def,
                Ctx(won: true, teamCoverage: 51.5f, enemyCoverage: 49f))); // 差 2.5
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def,
                Ctx(won: true, teamCoverage: 50f, enemyCoverage: 50f))); // 平局按胜利口径，差 0
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def,
                Ctx(won: false, teamCoverage: 51f, enemyCoverage: 49f)));
        }

        [Test]
        public void MatchMetricDef_DoesNotRespondToCareerDefs()
        {
            AchievementDef career = Def("ach_career_kill_100");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in career, Ctx(kills: 500)));
            Assert.IsFalse(AchievementEvaluator.IsCareerMetric(career.Metric) == false);
        }

        // ==================== 新增单局指标（单命连杀 / 复仇 / 压哨 / 彩蛋） ====================

        [Test]
        public void SingleLifeKills_Thresholds()
        {
            AchievementDef life5 = Def("ach_life_5");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in life5, Ctx(lifeKills: 4)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in life5, Ctx(lifeKills: 5)));

            AchievementDef life10 = Def("ach_life_10");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in life10, Ctx(lifeKills: 9)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in life10, Ctx(lifeKills: 10)));
        }

        [Test]
        public void RevengeAndClutch_RequireTheirEvents()
        {
            AchievementDef revenge = Def("ach_revenge");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in revenge, Ctx(revenge: 0)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in revenge, Ctx(revenge: 1)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in revenge, Ctx(revenge: 2)));

            AchievementDef clutch = Def("ach_clutch");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in clutch, Ctx(clutch: false)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in clutch, Ctx(clutch: true)));
        }

        [Test]
        public void PacifistWin_WonWithAlmostNoKills()
        {
            AchievementDef def = Def("ach_pacifist");

            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 0, won: true)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 1, won: true)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 2, won: true)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(kills: 0, won: false)));
        }

        [Test]
        public void IdleWin_WonWithAlmostNoPersonalTurf()
        {
            AchievementDef def = Def("ach_lazy_win");

            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(turf: 0f, won: true)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(turf: 10f, won: true)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(turf: 10.5f, won: true)));
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(turf: 0f, won: false)));
        }

        [Test]
        public void MatchMvp_RequiresMvpAward()
        {
            AchievementDef def = Def("ach_mvp_single");
            Assert.IsFalse(AchievementEvaluator.IsMatchDefMet(in def, Ctx(mvp: 0)));
            Assert.IsTrue(AchievementEvaluator.IsMatchDefMet(in def, Ctx(mvp: 1)));
        }

        // ==================== 生涯指标 ====================

        [Test]
        public void CareerMetrics_ReadProfileFields()
        {
            PlayerProfile profile = PlayerProfile.Current;
            profile.Matches = 12;
            profile.TotalKills = 120;
            profile.Wins = 15;
            profile.TotalTurf = 1200f;
            profile.TotalSpecials = 30;
            profile.MvpCount = 11;

            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_match_10"), profile));
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_kill_100"), profile));
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_win_10"), profile));
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_turf_1000"), profile));
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_special_25"), profile));
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_mvp_10"), profile));

            profile.TotalDeaths = 100;
            Assert.IsTrue(AchievementEvaluator.IsCareerDefMet(Def("ach_career_death_100"), profile));
            profile.TotalDeaths = 99;
            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_death_100"), profile));

            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_kill_500"), profile));
            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_match_50"), profile));
            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_turf_5000"), profile));
            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_win_50"), profile));
        }

        [Test]
        public void CareerDef_WithNullProfile_IsNeverMet()
        {
            Assert.IsFalse(AchievementEvaluator.IsCareerDefMet(Def("ach_career_match_10"), null));
        }

        [Test]
        public void Progress_UsesCareerBestsForMatchMetrics()
        {
            PlayerProfile profile = PlayerProfile.Current;
            profile.BestMatchKills = 7;
            profile.BestTeamCoverage = 45f;
            profile.BestMatchStreak = 4;
            profile.BestMatchLifeKills = 3;

            Assert.AreEqual(0.7f, AchievementEvaluator.GetProgress01(Def("ach_kill_10"), profile), 0.001f);
            Assert.AreEqual(45f / 50f, AchievementEvaluator.GetProgress01(Def("ach_cover_50"), profile), 0.001f);
            Assert.AreEqual(4f / 5f, AchievementEvaluator.GetProgress01(Def("ach_streak_5"), profile), 0.001f);
            Assert.AreEqual(3f / 5f, AchievementEvaluator.GetProgress01(Def("ach_life_5"), profile), 0.001f);
            Assert.AreEqual("3 / 5", AchievementEvaluator.GetProgressText(Def("ach_life_5"), profile));

            Assert.AreEqual("7 / 10", AchievementEvaluator.GetProgressText(Def("ach_kill_10"), profile));
            Assert.AreEqual("最高覆盖率 45% / 50%", AchievementEvaluator.GetProgressText(Def("ach_cover_50"), profile));

            // 布尔型成就无量化进度
            Assert.AreEqual(string.Empty, AchievementEvaluator.GetProgressText(Def("ach_first_blood"), profile));
            Assert.AreEqual(0f, AchievementEvaluator.GetProgress01(Def("ach_first_blood"), profile), 0.001f);

            // null 档案安全回退
            Assert.AreEqual(0f, AchievementEvaluator.GetProgress01(Def("ach_kill_10"), null), 0.001f);
        }

        // ==================== 结算服务回环 ====================

        [Test]
        public void SettleMatch_UnlocksOnce_AccumulatesCareerAndFlagsRecords()
        {
            var ctx = Ctx(kills: 12, deaths: 2, turf: 40f, streak: 3, specials: 1,
                teamCoverage: 72f, enemyCoverage: 28f, won: true);

            AchievementSettleOutcome first = AchievementService.SettleMatch(ctx, mvpAwards: 1);

            // 新解锁：单局 3 / 10 杀 + 覆盖 50 / 70 + 连杀 3 + MVP（12 杀不给 20/30 杀；72% 不给 80%）
            CollectionAssert.AreEquivalent(
                new[] { "ach_kill_3", "ach_kill_10", "ach_cover_50", "ach_cover_70", "ach_streak_3", "ach_mvp_single" },
                first.NewlyUnlocked);

            PlayerProfile profile = PlayerProfile.Current;
            Assert.AreEqual(12, profile.TotalKills);
            Assert.AreEqual(2, profile.TotalDeaths);
            Assert.AreEqual(1, profile.TotalSpecials);
            Assert.AreEqual(12, profile.BestMatchKills);
            Assert.AreEqual(3, profile.BestMatchStreak);
            Assert.AreEqual(40f, profile.BestMatchTurf, 0.01f);
            Assert.AreEqual(72f, profile.BestTeamCoverage, 0.01f);
            Assert.AreEqual(1, profile.MvpCount);
            Assert.IsTrue(profile.IsAchievementUnlocked("ach_kill_10"));

            // 第二局同样的成绩：不再重复解锁，也不再亮纪录签
            AchievementSettleOutcome second = AchievementService.SettleMatch(ctx, mvpAwards: 0);
            Assert.AreEqual(0, second.NewlyUnlocked.Count);
            Assert.IsFalse(second.NewRecordKills);
            Assert.IsFalse(second.NewRecordStreak);
            Assert.IsFalse(second.NewRecordTurf);
            Assert.IsFalse(second.NewRecordCoverage);
            Assert.AreEqual(24, profile.TotalKills); // 生涯继续累计
            Assert.AreEqual(1, profile.MvpCount);
        }

        [Test]
        public void SettleMatch_CareerAchievementsUnlockAtThreshold()
        {
            // 生涯阈值判定发生在「ApplyMatchResult 之后」的档案终值上。
            // 这里按真实结算顺序模拟：局数 / 胜场由 ApplyResult 递增，击杀由成就服务累计。
            PlayerProfile profile = PlayerProfile.Current;
            profile.Matches = 9;
            profile.TotalKills = 99;
            profile.Save();

            var stats = new CombatantMatchStats { Kills = 1 };
            PlayerProfile.ApplyMatchResult(true, stats); // matches 9→10, wins +1

            var ctx = Ctx(kills: 1, deaths: 1); // 带阵亡避免顺带解锁「毫发无损」
            AchievementSettleOutcome outcome = AchievementService.SettleMatch(ctx, mvpAwards: 0);

            CollectionAssert.AreEquivalent(new[] { "ach_career_match_10", "ach_career_kill_100" }, outcome.NewlyUnlocked);
        }

        [Test]
        public void SettleMatch_RecordFlags_OnlyWhenBestImproved()
        {
            var ctx = Ctx(kills: 5, streak: 2, turf: 30f, teamCoverage: 55f, won: true);
            AchievementService.SettleMatch(ctx, 0);

            var better = Ctx(kills: 8, streak: 4, turf: 50f, teamCoverage: 61f, won: true);
            AchievementSettleOutcome outcome = AchievementService.SettleMatch(better, 0);

            Assert.IsTrue(outcome.NewRecordKills);
            Assert.IsTrue(outcome.NewRecordStreak);
            Assert.IsTrue(outcome.NewRecordTurf);
            Assert.IsTrue(outcome.NewRecordCoverage);
        }

        // ==================== MatchStatsService 新计数 ====================

        private CombatantHealth CreateCombatant(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<CombatantHealth>();
            _createdObjects.Add(go);
            return go.GetComponent<CombatantHealth>();
        }

        [Test]
        public void MatchStatsService_FirstKill_TrackedAndReset()
        {
            CombatantHealth killer = CreateCombatant("Killer");
            CombatantHealth victim = CreateCombatant("Victim");

            Assert.IsFalse(MatchStatsService.HasFirstKill);
            Assert.IsNull(MatchStatsService.FirstKillKiller);

            MatchStatsService.RegisterKill(null, victim); // 环境死亡不算首杀
            Assert.IsFalse(MatchStatsService.HasFirstKill);

            MatchStatsService.RegisterKill(killer, victim);
            Assert.IsTrue(MatchStatsService.HasFirstKill);
            Assert.AreEqual(killer, MatchStatsService.FirstKillKiller);

            MatchStatsService.ResetForMatch();
            Assert.IsFalse(MatchStatsService.HasFirstKill);
            Assert.IsNull(MatchStatsService.FirstKillKiller);
        }

        [Test]
        public void MatchStatsService_StreakAndSpecials_KeepPeaksAndAccumulate()
        {
            CombatantHealth unit = CreateCombatant("Unit");

            MatchStatsService.RegisterStreak(unit, 2);
            MatchStatsService.RegisterStreak(unit, 5);
            MatchStatsService.RegisterStreak(unit, 3); // 峰值保留 5
            MatchStatsService.RegisterSpecialCast(unit);
            MatchStatsService.RegisterSpecialCast(unit);
            MatchStatsService.RegisterSpecialCast(unit);

            CombatantMatchStats stats = MatchStatsService.Get(unit);
            Assert.AreEqual(5, stats.MaxStreak);
            Assert.AreEqual(3, stats.SpecialCasts);

            MatchStatsService.RegisterStreak(unit, 0); // 非法值忽略
            MatchStatsService.RegisterSpecialCast(null);
            Assert.AreEqual(5, MatchStatsService.Get(unit).MaxStreak);
        }

        [Test]
        public void MatchStatsService_LifeKills_AccumulateAndResetOnDeath()
        {
            CombatantHealth killer = CreateCombatant("LifeKiller");
            CombatantHealth victim = CreateCombatant("LifeVictim");

            for (int i = 0; i < 3; i++)
            {
                MatchStatsService.RegisterKill(killer, victim);
            }
            CombatantMatchStats stats = MatchStatsService.Get(killer);
            Assert.AreEqual(3, stats.Kills);
            Assert.AreEqual(3, stats.CurrentLifeKills);
            Assert.AreEqual(3, stats.MaxLifeKills);

            // 阵亡：当前命清零，峰值保留
            MatchStatsService.RegisterKill(null, killer); // 环境死亡
            stats = MatchStatsService.Get(killer);
            Assert.AreEqual(0, stats.CurrentLifeKills);
            Assert.AreEqual(3, stats.MaxLifeKills);

            // 新一条命重新累计
            MatchStatsService.RegisterKill(killer, victim);
            stats = MatchStatsService.Get(killer);
            Assert.AreEqual(1, stats.CurrentLifeKills);
            Assert.AreEqual(3, stats.MaxLifeKills);
        }

        [Test]
        public void MatchStatsService_Revenge_OncePerGrudge()
        {
            CombatantHealth a = CreateCombatant("GrudgeA");
            CombatantHealth b = CreateCombatant("GrudgeB");

            // a 击杀 b → b 记账；b 反杀 a → 复仇 +1，账销掉
            MatchStatsService.RegisterKill(a, b);
            MatchStatsService.RegisterKill(b, a);
            Assert.AreEqual(1, MatchStatsService.Get(b).RevengeKills);
            Assert.AreEqual(0, MatchStatsService.Get(a).RevengeKills);

            // 同一条恩怨不重复记
            MatchStatsService.RegisterKill(b, a);
            Assert.AreEqual(1, MatchStatsService.Get(b).RevengeKills);

            // a 再次击杀 b → 新账；b 再反杀 → 第二次复仇
            MatchStatsService.RegisterKill(a, b);
            MatchStatsService.RegisterKill(b, a);
            Assert.AreEqual(2, MatchStatsService.Get(b).RevengeKills);
        }

        [Test]
        public void MatchStatsService_ClutchWindow_PureFunction()
        {
            Assert.IsTrue(MatchStatsService.IsClutchWindow(0f));
            Assert.IsTrue(MatchStatsService.IsClutchWindow(10f));
            Assert.IsFalse(MatchStatsService.IsClutchWindow(10.5f));
            Assert.IsFalse(MatchStatsService.IsClutchWindow(30f));
        }

        [Test]
        public void MatchStatsService_ClutchKill_SilentWithoutMatchManager()
        {
            // EditMode 无 MatchManager.Instance：登记击杀不抛错、不误记压哨
            CombatantHealth killer = CreateCombatant("ClutchKiller");
            CombatantHealth victim = CreateCombatant("ClutchVictim");

            Assert.DoesNotThrow(() => MatchStatsService.RegisterKill(killer, victim));
            Assert.IsFalse(MatchStatsService.Get(killer).ClutchKill);
        }
    }
}
