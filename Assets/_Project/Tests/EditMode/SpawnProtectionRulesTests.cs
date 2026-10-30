using NUnit.Framework;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 出生保护纯逻辑回归：3s 默认时长、伤害/敌墨灼烧免疫、与外部无敌（大招）取并集、
    /// 以及 ResetImmediately（开局站位/重开比赛）不白送保护。
    /// </summary>
    public class SpawnProtectionRulesTests
    {
        private GameObject _go;
        private PlayerConfigSO _config;
        private CombatantHealth _health;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _go = new GameObject("SpawnProtectionProbe");
            _health = _go.AddComponent<CombatantHealth>();
            _health.Configure(Team.TeamA, _config);
        }

        [TearDown]
        public void TearDown()
        {
            if (_health != null)
            {
                CombatantHealthRegistry.Unregister(_health);
            }

            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }

            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        [Test]
        public void Config_DefaultSpawnInvulnerability_IsThreeSeconds()
        {
            Assert.AreEqual(3f, _config.respawnInvulnerabilitySeconds, 0.001f,
                "PlayerConfigSO 默认出生保护应为 3s");
            Assert.AreEqual(3f, _health.SpawnProtectionDuration, 0.001f,
                "CombatantHealth 应直接读取配置时长");
        }

        [Test]
        public void SpawnProtection_BlocksWeaponDamageAndInkBurn()
        {
            _health.GrantSpawnProtection(3f);

            Assert.IsTrue(_health.IsSpawnProtected);
            Assert.IsTrue(_health.IsInvulnerable);

            bool killed = _health.TakeDamage(200f, Team.TeamB, _go.transform.position);
            Assert.IsFalse(killed, "保护期内 200 伤害不应击杀");
            Assert.AreEqual(100f, _health.CurrentHealth, 0.001f, "保护期内武器伤害不应结算");

            _health.ApplyEnemyInkBurn(20f, 60f);
            Assert.AreEqual(100f, _health.CurrentHealth, 0.001f, "保护期内敌墨灼烧不应结算");
        }

        [Test]
        public void ExternalInvulnerability_AndSpawnProtection_AreUnion()
        {
            _health.GrantSpawnProtection(3f);
            _health.IsInvulnerable = true;
            Assert.IsTrue(_health.IsInvulnerable);

            // WeaponController 的大招余韵每帧回写 false，不能把出生保护误清掉
            _health.IsInvulnerable = false;
            Assert.IsTrue(_health.IsInvulnerable, "清除外部无敌不能误清出生保护");
        }

        [Test]
        public void ResetImmediately_ClearsSpawnProtection()
        {
            _health.GrantSpawnProtection(3f);
            _health.ResetImmediately();

            Assert.IsFalse(_health.IsSpawnProtected, "开局站位/重开比赛不应白送出生保护");
            Assert.IsFalse(_health.IsInvulnerable);
        }
    }
}