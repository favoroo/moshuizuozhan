using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P6-B] 双大招与副武器的数值回归（在 P3-B 基础用例上补齐生命周期/边界断言）：
    /// 1. Ink Tempest：6.5s / 13 次 DOT / 合计 221 伤，第 6 跳（3.0s）击杀 100 HP 目标；
    /// 2. Tidal Slam：核心 3.2m 秒杀、核心外台阶 92.5、外缘 55、5.2m 外 0；
    /// 3. Splat Bomb：平方衰减曲线（半程 71.25 / 四分之一程 44.0625）；
    /// 4. 大招配置契约：超级降落 5m 墨斑 + 风暴云 6.5s / 3.4m / 34 DPS / 1.1m/s 漂移。
    /// </summary>
    public class SpecialRuleRegressionTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] == null) continue;
                var health = _createdObjects[i].GetComponent<CombatantHealth>();
                if (health != null) CombatantHealthRegistry.Unregister(health);
                Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null) Object.DestroyImmediate(_createdConfigs[i]);
            }
            _createdConfigs.Clear();
        }

        // ------------------------------------------------------------------ Ink Tempest 生命周期

        [Test]
        public void Storm_SixPointFiveSeconds_ProducesThirteenTicksAnd221Damage()
        {
            const float dps = 34f;
            const float tickInterval = 0.5f;
            const float duration = 6.5f;

            int expectedTicks = Mathf.FloorToInt(duration / tickInterval); // 13
            float perTick = SpecialCombat.StormTickDamage(dps, tickInterval);
            float total = perTick * expectedTicks;

            Assert.AreEqual(13, expectedTicks, "6.5s / 0.5s = 13 次 DOT 结算");
            Assert.AreEqual(17f, perTick, 0.0001f);
            Assert.AreEqual(221f, total, 0.001f, "整场风暴云理论总伤害 34 × 6.5 = 221");
            Assert.Greater(total, 100f, "整场风暴足以击杀满血目标");
        }

        [Test]
        public void Storm_KillsFullHealthTargetOnSixthTick_AtThreeSeconds()
        {
            var victim = CreateCombatant("StormLifecycleVictim", new Vector3(0f, 0f, 0f));
            Vector3 cloud = new Vector3(0f, 4f, 0f);
            const float perTick = 17f;

            for (int tick = 1; tick <= 5; tick++)
            {
                int hits = SpecialCombat.ApplyStormTick(cloud, Team.TeamA, 3.4f, perTick);
                Assert.AreEqual(1, hits, $"第 {tick} 跳应命中云下敌人");
                Assert.IsTrue(victim.IsAlive, $"第 {tick} 跳后（{tick * 0.5f:F1}s，累计 {tick * perTick} 伤）不应死亡");
            }

            Assert.AreEqual(15f, victim.CurrentHealth, 0.001f, "5 跳后剩 15 HP");

            int sixth = SpecialCombat.ApplyStormTick(cloud, Team.TeamA, 3.4f, perTick);
            Assert.AreEqual(1, sixth);
            Assert.IsFalse(victim.IsAlive, "第 6 跳（3.0s，累计 102 伤）应击杀满血目标");
            Assert.AreEqual(0f, victim.CurrentHealth, 0.001f);
        }

        [Test]
        public void Storm_DriftOverLifetime_Is7Point15Metres()
        {
            Vector3 start = new Vector3(2f, 4f, -3f);
            Vector3 end = start;
            const float dt = 1f / 60f;
            int frames = Mathf.RoundToInt(6.5f / dt);

            for (int i = 0; i < frames; i++)
            {
                end = SpecialCombat.StormDriftStep(end, Vector3.forward, 1.1f, dt);
            }

            Assert.AreEqual(1.1f * 6.5f, end.z - start.z, 0.05f, "6.5s 生命周期累计漂移 7.15m");
            Assert.AreEqual(start.y, end.y, 0.001f, "漂移只在水平面");
            Assert.AreEqual(start.x, end.x, 0.001f);
        }

        // ------------------------------------------------------------------ Tidal Slam 衰减曲线

        [Test]
        public void SlamDamage_HasShelfAtCoreEdge_ThenFallsTo55()
        {
            const float inner = 3.2f;
            const float radius = 5.2f;

            Assert.AreEqual(180f, SpecialCombat.SlamDamage(inner, inner, radius, 180f, 55f), 0.001f);
            Assert.AreEqual(92.5f, SpecialCombat.SlamDamage(inner + 0.001f, inner, radius, 180f, 55f), 0.05f,
                "刚出核心圈应保留 30% 台阶（92.5），不是断崖到 55");

            // 核心圈外一半处：t = 1 - (4.2-3.2)/2.0 = 0.5，伤害 = Lerp(55, 92.5, 0.5) = 73.75
            Assert.AreEqual(73.75f, SpecialCombat.SlamDamage(4.2f, inner, radius, 180f, 55f), 0.001f);
            Assert.AreEqual(55f, SpecialCombat.SlamDamage(radius, inner, radius, 180f, 55f), 0.001f);
            Assert.AreEqual(0f, SpecialCombat.SlamDamage(radius + 0.01f, inner, radius, 180f, 55f), 0.001f);
        }

        [Test]
        public void Slam_OuterRing_LeavesTargetAliveButHurt()
        {
            var core = CreateCombatant("SlamCore", new Vector3(1f, 0f, 0f));
            var outer = CreateCombatant("SlamOuter", new Vector3(0f, 0f, 4.2f));
            var far = CreateCombatant("SlamFar", new Vector3(0f, 0f, 5.5f));
            var ally = CreateCombatant("SlamAlly", new Vector3(0f, 0f, 1f), Team.TeamA);

            int hits = SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f);

            Assert.AreEqual(2, hits, "只命中核心与外圈两名敌人");
            Assert.IsFalse(core.IsAlive, "核心圈 180 秒杀");
            Assert.IsTrue(outer.IsAlive, "外圈 73.75 不秒杀满血目标");
            Assert.AreEqual(26.25f, outer.CurrentHealth, 0.001f, "外圈应剩约 26 HP");
            Assert.AreEqual(100f, far.CurrentHealth, 0.001f, "5.2m 外不吃伤害");
            Assert.AreEqual(100f, ally.CurrentHealth, 0.001f, "同队不吃伤害");
        }

        // ------------------------------------------------------------------ Splat Bomb 衰减曲线

        [Test]
        public void BombDamage_UsesQuadraticFalloff()
        {
            const float inner = 1.2f;
            const float radius = 3.1f;

            Assert.AreEqual(180f, SpecialCombat.BombDamage(inner, inner, radius, 180f, 35f), 0.001f);
            Assert.AreEqual(35f, SpecialCombat.BombDamage(radius, inner, radius, 180f, 35f), 0.001f);

            // 半程 t=0.5 → Lerp(35, 180, 0.25) = 71.25
            Assert.AreEqual(71.25f, SpecialCombat.BombDamage(2.15f, inner, radius, 180f, 35f), 0.001f);
            // 四分之一程 t=0.25 → Lerp(35, 180, 0.0625) = 44.0625
            Assert.AreEqual(44.0625f, SpecialCombat.BombDamage(2.625f, inner, radius, 180f, 35f), 0.001f);
            Assert.AreEqual(0f, SpecialCombat.BombDamage(radius + 0.01f, inner, radius, 180f, 35f), 0.001f,
                "3.1m 外不吃爆炸伤害");
        }

        // ------------------------------------------------------------------ 配置契约

        [Test]
        public void SpecialConfigs_MatchP3BSpec_ForBothKinds()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);

            WeaponConfigSO.ApplySlamSpec(config);
            Assert.AreEqual(5.2f, config.specialSplatRadius, 0.001f, "冲击波 5.2m");
            Assert.AreEqual(5.0f, config.specialPaintRadius, 0.001f, "落地墨斑 5m");
            Assert.AreEqual(3.2f, config.specialInnerKillRadius, 0.001f, "核心秒杀 3.2m");
            Assert.AreEqual(180f, config.specialInnerDamage, 0.001f);
            Assert.AreEqual(55f, config.specialOuterDamage, 0.001f);
            Assert.AreEqual(0.55f, config.specialWindupDuration, 0.001f);
            Assert.AreEqual(0.25f, config.specialHangDuration, 0.001f);
            Assert.AreEqual(0.10f, config.specialFallDuration, 0.001f);

            WeaponConfigSO.ApplyStormSpec(config);
            Assert.AreEqual(6.5f, config.stormDuration, 0.001f, "风暴云 6.5s");
            Assert.AreEqual(3.4f, config.stormRadius, 0.001f, "降雨半径 3.4m");
            Assert.AreEqual(34f, config.stormDps, 0.001f, "34 DPS");
            Assert.AreEqual(1.1f, config.stormDriftSpeed, 0.001f, "漂移 1.1m/s");
            Assert.AreEqual(0.5f, config.stormDamageTickInterval, 0.001f);
            Assert.AreEqual(13, Mathf.FloorToInt(config.stormDuration / config.stormDamageTickInterval));
        }

        // ------------------------------------------------------------------ helpers

        private CombatantHealth CreateCombatant(string name, Vector3 pos, Team team = Team.TeamB)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            go.transform.position = pos;
            CombatantHealthRegistry.Register(health);
            _createdObjects.Add(go);
            return health;
        }
    }
}