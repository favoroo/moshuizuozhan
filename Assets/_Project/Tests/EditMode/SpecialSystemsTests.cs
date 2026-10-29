using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P3-B 双大招与副武器手雷的纯数值回归：
    /// Tidal Slam（5.2m 冲击波 / 核心 3.2m 180 / 外缘 55）、
    /// Ink Tempest（6.5s / 3.4m / 34 DPS / 1.1m/s 漂移）、
    /// Splat Bomb（耗墨 70 / 0.95s 引信 / 核心 180 / 涂地 2.7m）。
    /// 全部走 <see cref="SpecialCombat"/> 与配置默认值，不依赖场景、物理或对象池。
    /// </summary>
    public class SpecialSystemsTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] == null)
                    continue;

                var health = _createdObjects[i].GetComponent<CombatantHealth>();
                if (health != null)
                    CombatantHealthRegistry.Unregister(health);

                Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                    Object.DestroyImmediate(_createdConfigs[i]);
            }
            _createdConfigs.Clear();
        }

        // ------------------------------------------------------------------ 配置规范

        [Test]
        public void Config_SlamDefaults_MatchP3BSpec()
        {
            WeaponConfigSO config = CreateConfig();

            Assert.AreEqual(SpecialKind.Slam, config.specialKind, "默认大招为 Tidal Slam");
            Assert.AreEqual(0.55f, config.specialWindupDuration, 0.0001f, "起跳升空 0.55s");
            Assert.AreEqual(0.25f, config.specialHangDuration, 0.0001f, "顶点滞空 0.25s");
            Assert.AreEqual(5.2f, config.specialSplatRadius, 0.0001f, "冲击波半径 5.2m");
            Assert.AreEqual(5.0f, config.specialPaintRadius, 0.0001f, "巨大墨斑 5m");
            Assert.AreEqual(3.2f, config.specialInnerKillRadius, 0.0001f, "核心秒杀半径 3.2m");
            Assert.AreEqual(180f, config.specialInnerDamage, 0.0001f, "核心 180 伤秒杀");
            Assert.AreEqual(55f, config.specialOuterDamage, 0.0001f, "外缘 55 伤");
            Assert.AreEqual(1f, config.specialImpactTrauma, 0.0001f, "落地强震屏");
        }

        [Test]
        public void Config_StormDefaults_MatchP3BSpec()
        {
            WeaponConfigSO config = CreateConfig();
            config.specialKind = SpecialKind.Storm;

            Assert.AreEqual(SpecialKind.Storm, config.specialKind, "蓄力狙/爆破枪/加特林使用 Ink Tempest");
            Assert.AreEqual(6.5f, config.stormDuration, 0.0001f, "云层持续 6.5s");
            Assert.AreEqual(3.4f, config.stormRadius, 0.0001f, "降雨半径 3.4m");
            Assert.AreEqual(34f, config.stormDps, 0.0001f, "每秒 34 点 DOT");
            Assert.AreEqual(1.1f, config.stormDriftSpeed, 0.0001f, "云层漂移 1.1m/s");
            Assert.AreEqual(0.5f, config.stormDamageTickInterval, 0.0001f, "0.5s 结算一次 DOT");
        }

        [Test]
        public void Config_BombDefaults_MatchP3BSpec()
        {
            WeaponConfigSO config = CreateConfig();

            Assert.AreEqual(70f, config.bombInkCost, 0.0001f, "手雷耗墨 70%");
            Assert.AreEqual(0.95f, config.bombFuseDelay, 0.0001f, "落地 0.95s 引信");
            Assert.AreEqual(2.7f, config.bombSplatRadius, 0.0001f, "涂地半径 2.7m");
            Assert.AreEqual(3.1f, config.bombDamageRadius, 0.0001f, "伤害判定半径 3.1m");
            Assert.AreEqual(180f, config.bombInnerDamage, 0.0001f, "核心 180 秒杀");
            Assert.AreEqual(35f, config.bombOuterDamage, 0.0001f, "外缘 35 伤");
        }

        // ------------------------------------------------------------------ Tidal Slam

        [Test]
        public void SlamDamage_IsFullInsideCore_And55AtOuterEdge()
        {
            Assert.AreEqual(180f, SpecialCombat.SlamDamage(0f, 3.2f, 5.2f, 180f, 55f), 0.0001f);
            Assert.AreEqual(180f, SpecialCombat.SlamDamage(3.2f, 3.2f, 5.2f, 180f, 55f), 0.0001f, "核心圈边缘仍满伤");
            Assert.AreEqual(55f, SpecialCombat.SlamDamage(5.2f, 3.2f, 5.2f, 180f, 55f), 0.0001f, "外缘恰好 55");
            Assert.AreEqual(0f, SpecialCombat.SlamDamage(5.21f, 3.2f, 5.2f, 180f, 55f), 0.0001f, "半径外不吃伤害");

            float justOutsideCore = SpecialCombat.SlamDamage(3.2001f, 3.2f, 5.2f, 180f, 55f);
            Assert.That(justOutsideCore, Is.InRange(55f, 180f), "核心圈外应是平滑衰减而不是断崖");
            Assert.Greater(justOutsideCore, 55f, "刚出核心圈仍有明显重伤");
        }

        [Test]
        public void Slam_RadialDamage_KillsCore_HurtsOuter_AndSparesAllies()
        {
            var core = CreateCombatant("SlamCoreVictim", Team.TeamB, new Vector3(1.5f, 0f, 0f));
            var outer = CreateCombatant("SlamOuterVictim", Team.TeamB, new Vector3(0f, 0f, 5f));
            var ally = CreateCombatant("SlamAlly", Team.TeamA, new Vector3(0.5f, 0f, 0f));

            var coreHealth = core.GetComponent<CombatantHealth>();
            var outerHealth = outer.GetComponent<CombatantHealth>();
            var allyHealth = ally.GetComponent<CombatantHealth>();

            SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f);

            Assert.IsFalse(coreHealth.IsAlive, "核心 3.2m 内 180 伤必须秒杀 100HP");
            Assert.IsTrue(outerHealth.IsAlive, "外圈 55 伤不应秒杀满血目标");
            Assert.That(outerHealth.CurrentHealth, Is.InRange(30f, 50f), "5.0m 处应结算 55~60 伤");
            Assert.AreEqual(100f, allyHealth.CurrentHealth, 0.001f, "同队不受大招伤害");
        }

        [Test]
        public void Slam_IgnoresOpponentsOutsideRadius()
        {
            var far = CreateCombatant("SlamFarVictim", Team.TeamB, new Vector3(6f, 0f, 0f));
            SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f);
            Assert.AreEqual(100f, far.GetComponent<CombatantHealth>().CurrentHealth, 0.001f);
        }

        // ------------------------------------------------------------------ Ink Tempest

        [Test]
        public void Storm_DpsAccumulatesTo34PerSecond()
        {
            Assert.AreEqual(17f, SpecialCombat.StormTickDamage(34f, 0.5f), 0.0001f, "0.5s 结算 17 点");
            Assert.AreEqual(34f, SpecialCombat.StormTickDamage(34f, 0.5f) * 2f, 0.0001f, "两跳合计 34 DPS");
            Assert.AreEqual(34f, SpecialCombat.StormTickDamage(34f, 1f), 0.0001f);
        }

        [Test]
        public void Storm_Drift_Moves11MetresPerSecond()
        {
            Vector3 start = new Vector3(1f, 4f, -2f);
            Vector3 afterOneSecond = SpecialCombat.StormDriftStep(start, Vector3.right, 1.1f, 1f);

            Assert.AreEqual(2.1f, afterOneSecond.x, 0.0001f, "1 秒漂移 1.1m");
            Assert.AreEqual(start.y, afterOneSecond.y, 0.0001f, "漂移只在水平面");
            Assert.AreEqual(start.z, afterOneSecond.z, 0.0001f);

            Vector3 afterLifetime = SpecialCombat.StormDriftStep(start, Vector3.right, 1.1f, 6.5f);
            Assert.AreEqual(1f + 1.1f * 6.5f, afterLifetime.x, 0.001f, "6.5s 生命周期共漂移 7.15m");
        }

        [Test]
        public void Storm_DotTick_HitsOnlyEnemiesUnderTheCloud()
        {
            var inside = CreateCombatant("StormInside", Team.TeamB, new Vector3(0f, 0f, 2f));
            var outside = CreateCombatant("StormOutside", Team.TeamB, new Vector3(0f, 0f, 4f));
            var ally = CreateCombatant("StormAlly", Team.TeamA, new Vector3(0f, 0f, 1f));
            var aboveCloud = CreateCombatant("StormAbove", Team.TeamB, new Vector3(0f, 5f, 0f));

            Vector3 cloudCenter = new Vector3(0f, 4f, 0f);
            SpecialCombat.ApplyStormTick(cloudCenter, Team.TeamA, 3.4f, 17f);

            Assert.AreEqual(83f, inside.GetComponent<CombatantHealth>().CurrentHealth, 0.001f, "半径内每跳 17 伤");
            Assert.AreEqual(100f, outside.GetComponent<CombatantHealth>().CurrentHealth, 0.001f, "3.4m 外不吃雨");
            Assert.AreEqual(100f, ally.GetComponent<CombatantHealth>().CurrentHealth, 0.001f, "同队不吃雨");
            Assert.AreEqual(100f, aboveCloud.GetComponent<CombatantHealth>().CurrentHealth, 0.001f, "云层之上不吃雨");
        }

        // ------------------------------------------------------------------ Splat Bomb

        [Test]
        public void BombDamage_KillsInsideCore_AndFallsTo35AtEdge()
        {
            Assert.AreEqual(180f, SpecialCombat.BombDamage(0f, 1.2f, 3.1f, 180f, 35f), 0.0001f);
            Assert.AreEqual(180f, SpecialCombat.BombDamage(1.2f, 1.2f, 3.1f, 180f, 35f), 0.0001f);
            Assert.AreEqual(35f, SpecialCombat.BombDamage(3.1f, 1.2f, 3.1f, 180f, 35f), 0.0001f);
            Assert.AreEqual(0f, SpecialCombat.BombDamage(3.11f, 1.2f, 3.1f, 180f, 35f), 0.0001f);
        }

        [Test]
        public void Bomb_RadialDamage_KillsCoreTarget_AndOnlyChipsEdgeTarget()
        {
            var core = CreateCombatant("BombCoreVictim", Team.TeamB, new Vector3(0.8f, 0f, 0f));
            var edge = CreateCombatant("BombEdgeVictim", Team.TeamB, new Vector3(0f, 0f, 3f));

            SpecialCombat.ApplyBombDamage(Vector3.zero, Team.TeamA, 3.1f, 1.2f, 180f, 35f);

            Assert.IsFalse(core.GetComponent<CombatantHealth>().IsAlive, "核心 180 秒杀");
            var edgeHealth = edge.GetComponent<CombatantHealth>();
            Assert.IsTrue(edgeHealth.IsAlive, "外圈不应秒杀");
            Assert.Less(edgeHealth.CurrentHealth, 100f, "外圈仍应吃到衰减伤害");
        }

        // ------------------------------------------------------------------ helpers

        private WeaponConfigSO CreateConfig()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            return config;
        }

        private GameObject CreateCombatant(string name, Team team, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            go.transform.position = position;

            // EditMode 下 AddComponent 不一定触发 OnEnable，显式注册保证伤害结算能找到目标。
            CombatantHealthRegistry.Register(health);

            _createdObjects.Add(go);
            return go;
        }
    }
}
