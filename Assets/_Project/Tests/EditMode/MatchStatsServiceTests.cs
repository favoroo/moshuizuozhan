using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// MatchStatsService 个人对局统计服务单元测试（EditMode，不依赖场景）。
    /// 验证：
    /// 1. RegisterKill / Deaths / Turf 累加与 Get / TryGet 未登记默认值；
    /// 2. ResetForMatch / Clear 跨局清理；
    /// 3. 多队伍 GetTeamTurf 聚合；
    /// 4. null / 0 / 负值 / NaN / Infinity 边界安全防护；
    /// 5. PhaseChanged 状态流转 Ready/Countdown -> Playing 边沿重置与 Result 保留；
    /// 6. 订阅 CombatantHealth.DamageApplied 静态事件的自动击杀/阵亡归属。
    /// </summary>
    public class MatchStatsServiceTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            MatchStatsService.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            MatchStatsService.Clear();

            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] == null)
                    continue;

                var health = _createdObjects[i].GetComponent<CombatantHealth>();
                if (health != null)
                {
                    CombatantHealthRegistry.Unregister(health);
                }

                Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        private CombatantHealth CreateCombatant(string name, Team team)
        {
            var go = new GameObject(name);
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            CombatantHealthRegistry.Register(health);
            _createdObjects.Add(go);
            return health;
        }

        // ------------------------------------------------------------------ 1. 击杀与死亡累加

        [Test]
        public void RegisterKill_SingleKill_IncrementsKillerKillAndVictimDeath()
        {
            var killer = CreateCombatant("Killer", Team.TeamA);
            var victim = CreateCombatant("Victim", Team.TeamB);

            MatchStatsService.RegisterKill(killer, victim);

            CombatantMatchStats killerStats = MatchStatsService.Get(killer);
            CombatantMatchStats victimStats = MatchStatsService.Get(victim);

            Assert.AreEqual(1, killerStats.Kills);
            Assert.AreEqual(0, killerStats.Deaths);
            Assert.AreEqual(0, victimStats.Kills);
            Assert.AreEqual(1, victimStats.Deaths);
        }

        [Test]
        public void RegisterKill_MultipleTimes_AccumulatesCorrectly()
        {
            var p1 = CreateCombatant("Player1", Team.TeamA);
            var p2 = CreateCombatant("Player2", Team.TeamB);

            MatchStatsService.RegisterKill(p1, p2);
            MatchStatsService.RegisterKill(p1, p2);
            MatchStatsService.RegisterKill(p2, p1);

            CombatantMatchStats p1Stats = MatchStatsService.Get(p1);
            CombatantMatchStats p2Stats = MatchStatsService.Get(p2);

            Assert.AreEqual(2, p1Stats.Kills);
            Assert.AreEqual(1, p1Stats.Deaths);
            Assert.AreEqual(1, p2Stats.Kills);
            Assert.AreEqual(2, p2Stats.Deaths);
        }

        [Test]
        public void RegisterKill_NullVictim_OnlyIncrementsKiller()
        {
            var killer = CreateCombatant("KillerOnly", Team.TeamA);

            MatchStatsService.RegisterKill(killer, null);

            CombatantMatchStats stats = MatchStatsService.Get(killer);
            Assert.AreEqual(1, stats.Kills);
            Assert.AreEqual(0, stats.Deaths);
        }

        [Test]
        public void RegisterKill_NullKiller_OnlyIncrementsVictimDeath()
        {
            var victim = CreateCombatant("VictimOnly", Team.TeamB);

            MatchStatsService.RegisterKill(null, victim);

            CombatantMatchStats stats = MatchStatsService.Get(victim);
            Assert.AreEqual(0, stats.Kills);
            Assert.AreEqual(1, stats.Deaths);
        }

        [Test]
        public void RegisterKill_BothNull_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MatchStatsService.RegisterKill(null, null));
        }

        // ------------------------------------------------------------------ 2. 涂地面积累加与安全防护

        [Test]
        public void RegisterTurf_AccumulatesSquareMeters()
        {
            var painter = CreateCombatant("Painter", Team.TeamA);

            MatchStatsService.RegisterTurf(painter, 12.5f);
            MatchStatsService.RegisterTurf(painter, 7.5f);

            CombatantMatchStats stats = MatchStatsService.Get(painter);
            Assert.AreEqual(20.0f, stats.TurfSquareMeters, 0.0001f);
        }

        [Test]
        public void RegisterTurf_IgnoresNull_Zero_Negative_And_NaN()
        {
            var painter = CreateCombatant("PainterGuard", Team.TeamA);

            MatchStatsService.RegisterTurf(null, 10f);
            MatchStatsService.RegisterTurf(painter, 0f);
            MatchStatsService.RegisterTurf(painter, -5f);
            MatchStatsService.RegisterTurf(painter, float.NaN);
            MatchStatsService.RegisterTurf(painter, float.PositiveInfinity);
            MatchStatsService.RegisterTurf(painter, float.NegativeInfinity);

            CombatantMatchStats stats = MatchStatsService.Get(painter);
            Assert.AreEqual(0f, stats.TurfSquareMeters, 0.0001f);
            Assert.IsFalse(MatchStatsService.TryGet(painter, out _), "无效输入不应登记条目");
        }

        // ------------------------------------------------------------------ 3. Get / TryGet 未登记与空防护

        [Test]
        public void Get_Unregistered_ReturnsDefault()
        {
            var unreg = CreateCombatant("Unreg", Team.TeamA);

            CombatantMatchStats stats = MatchStatsService.Get(unreg);
            Assert.AreEqual(0, stats.Kills);
            Assert.AreEqual(0, stats.Deaths);
            Assert.AreEqual(0f, stats.TurfSquareMeters, 0.0001f);

            CombatantMatchStats nullStats = MatchStatsService.Get(null);
            Assert.AreEqual(0, nullStats.Kills);
        }

        [Test]
        public void TryGet_Unregistered_ReturnsFalseAndDefault()
        {
            var unreg = CreateCombatant("UnregTry", Team.TeamB);

            bool found = MatchStatsService.TryGet(unreg, out CombatantMatchStats stats);
            Assert.IsFalse(found);
            Assert.AreEqual(0, stats.Kills);
            Assert.AreEqual(0, stats.Deaths);
            Assert.AreEqual(0f, stats.TurfSquareMeters, 0.0001f);

            Assert.IsFalse(MatchStatsService.TryGet(null, out _));
        }

        [Test]
        public void TryGet_Registered_ReturnsTrueAndAccurateData()
        {
            var reg = CreateCombatant("RegTry", Team.TeamA);
            MatchStatsService.RegisterTurf(reg, 15.2f);
            MatchStatsService.RegisterKill(reg, null);

            bool found = MatchStatsService.TryGet(reg, out CombatantMatchStats stats);
            Assert.IsTrue(found);
            Assert.AreEqual(1, stats.Kills);
            Assert.AreEqual(0, stats.Deaths);
            Assert.AreEqual(15.2f, stats.TurfSquareMeters, 0.0001f);
        }

        // ------------------------------------------------------------------ 4. 多队 GetTeamTurf

        [Test]
        public void GetTeamTurf_SumsCombatantsByTeam()
        {
            var a1 = CreateCombatant("A1", Team.TeamA);
            var a2 = CreateCombatant("A2", Team.TeamA);
            var b1 = CreateCombatant("B1", Team.TeamB);

            MatchStatsService.RegisterTurf(a1, 10.0f);
            MatchStatsService.RegisterTurf(a2, 15.5f);
            MatchStatsService.RegisterTurf(b1, 30.2f);

            Assert.AreEqual(25.5f, MatchStatsService.GetTeamTurf(Team.TeamA), 0.0001f);
            Assert.AreEqual(30.2f, MatchStatsService.GetTeamTurf(Team.TeamB), 0.0001f);
            Assert.AreEqual(0f, MatchStatsService.GetTeamTurf(Team.Neutral), 0.0001f);
        }

        // ------------------------------------------------------------------ 5. ResetForMatch 与 Clear

        [Test]
        public void ResetForMatch_And_Clear_WipeAllStats()
        {
            var p = CreateCombatant("P", Team.TeamA);
            MatchStatsService.RegisterKill(p, null);
            MatchStatsService.RegisterTurf(p, 50f);

            Assert.AreEqual(1, MatchStatsService.Get(p).Kills);
            Assert.AreEqual(50f, MatchStatsService.Get(p).TurfSquareMeters, 0.0001f);

            MatchStatsService.ResetForMatch();
            Assert.IsFalse(MatchStatsService.TryGet(p, out _));
            Assert.AreEqual(0f, MatchStatsService.GetTeamTurf(Team.TeamA), 0.0001f);

            MatchStatsService.RegisterTurf(p, 20f);
            Assert.AreEqual(20f, MatchStatsService.Get(p).TurfSquareMeters, 0.0001f);

            MatchStatsService.Clear();
            Assert.IsFalse(MatchStatsService.TryGet(p, out _));
            Assert.AreEqual(0f, MatchStatsService.GetTeamTurf(Team.TeamA), 0.0001f);
        }

        // ------------------------------------------------------------------ 6. 状态流转边沿重置与结算数据保留

        [Test]
        public void PhaseTransition_ReadyOrCountdownToPlaying_Resets_And_ResultPreserves()
        {
            var p = CreateCombatant("PlayerPhase", Team.TeamA);
            MatchStatsService.RegisterTurf(p, 42f);

            // Ready -> Playing: 清零
            MatchStatsService.HandlePhaseTransition(MatchManager.Phase.Ready, MatchManager.Phase.Playing);
            Assert.IsFalse(MatchStatsService.TryGet(p, out _));

            // 比赛进行中产生数据
            MatchStatsService.RegisterTurf(p, 100f);
            MatchStatsService.RegisterKill(p, null);

            // Playing -> Finished: 保留
            MatchStatsService.HandlePhaseTransition(MatchManager.Phase.Playing, MatchManager.Phase.Finished);
            Assert.AreEqual(1, MatchStatsService.Get(p).Kills);
            Assert.AreEqual(100f, MatchStatsService.Get(p).TurfSquareMeters, 0.0001f);

            // Finished -> Result: 结算阶段继续完整保留
            MatchStatsService.HandlePhaseTransition(MatchManager.Phase.Finished, MatchManager.Phase.Result);
            Assert.AreEqual(1, MatchStatsService.Get(p).Kills);
            Assert.AreEqual(100f, MatchStatsService.Get(p).TurfSquareMeters, 0.0001f);

            // Result -> Countdown -> Playing: 新局开始清零
            MatchStatsService.HandlePhaseTransition(MatchManager.Phase.Result, MatchManager.Phase.Countdown);
            MatchStatsService.HandlePhaseTransition(MatchManager.Phase.Countdown, MatchManager.Phase.Playing);
            Assert.IsFalse(MatchStatsService.TryGet(p, out _));
        }

        // ------------------------------------------------------------------ 7. DamageApplied 静态事件自动归属

        [Test]
        public void DamageApplied_LethalDamage_AutoRegistersKillAndDeath()
        {
            var attacker = CreateCombatant("AttackerAuto", Team.TeamA);
            var victim = CreateCombatant("VictimAuto", Team.TeamB);

            // 通过 CombatantHealth.TakeDamage 走真实致死管线
            bool killed = victim.TakeDamage(100f, Team.TeamA, Vector3.zero, attacker);
            Assert.IsTrue(killed, "100 伤害应致死");

            CombatantMatchStats attackerStats = MatchStatsService.Get(attacker);
            CombatantMatchStats victimStats = MatchStatsService.Get(victim);

            Assert.AreEqual(1, attackerStats.Kills, "攻击者应自动登记击杀");
            Assert.AreEqual(0, attackerStats.Deaths);
            Assert.AreEqual(0, victimStats.Kills);
            Assert.AreEqual(1, victimStats.Deaths, "受害者应自动登记阵亡");
        }

        [Test]
        public void DamageApplied_NonLethalDamage_DoesNotRegisterKillOrDeath()
        {
            var attacker = CreateCombatant("AttackerNonLethal", Team.TeamA);
            var victim = CreateCombatant("VictimNonLethal", Team.TeamB);

            bool killed = victim.TakeDamage(40f, Team.TeamA, Vector3.zero, attacker);
            Assert.IsFalse(killed, "40 伤害不致死");

            Assert.IsFalse(MatchStatsService.TryGet(attacker, out _));
            Assert.IsFalse(MatchStatsService.TryGet(victim, out _));
        }

        [Test]
        public void DamageApplied_LethalWithoutAttacker_OnlyRegistersVictimDeath()
        {
            var victim = CreateCombatant("VictimEnv", Team.TeamB);

            // 无攻击者（例如环境/跌落），attacker 为 null
            bool killed = victim.TakeDamage(100f, Team.Neutral, Vector3.zero, null);
            Assert.IsTrue(killed, "100 伤害应致死");

            CombatantMatchStats victimStats = MatchStatsService.Get(victim);
            Assert.AreEqual(0, victimStats.Kills);
            Assert.AreEqual(1, victimStats.Deaths, "仅受害者记阵亡，无攻击者不记击杀");
        }
    }
}
