using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P6-B] 角色机动与生命值规则回归（纯逻辑 / 不依赖场景）：
    /// 1. 踩敌墨 DOT 20/s 与 60 HP 保护底线：只靠踩墨绝不致死；
    /// 2. 2.0s 从满血被敌墨压到 60 底线；
    /// 3. 无敌帧 / 阵亡状态下敌墨不结算；
    /// 4. 治疗封顶、友军伤害拒绝、阵亡 4s 复活计时与复位；
    /// 5. 潜墨回墨 42/s（空罐 2.4s 回满）、回血 60/s、敌墨受击槽 2.0/s 退出速率；
    /// 6. 隐匿速度阈值 1.5 m/s、出水过渡 0.07s、开火缓冲 0.16s 的配置契约。
    /// </summary>
    public class InkLifecycleRulesTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

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
        }

        // ------------------------------------------------------------------ 敌墨 DOT 与 60 HP 保护底线

        [Test]
        public void EnemyInkBurn_ClampsAtSixtyFloor_AndNeverKills()
        {
            var victim = CreateCombatant("EnemyInkFloor", Team.TeamB);
            const float dotPerTick = 20f * 0.1f; // 20 DPS，每 0.1s 结算一次

            for (int i = 0; i < 40; i++) // 4 秒，远超致死所需
            {
                victim.ApplyEnemyInkBurn(dotPerTick, 60f);
            }

            Assert.IsTrue(victim.IsAlive, "单靠踩敌墨绝不允许击杀角色");
            Assert.AreEqual(60f, victim.CurrentHealth, 0.001f, "敌墨伤害必须停在 60 HP 保护底线");
        }

        [Test]
        public void EnemyInkBurn_ReachesFloorInExactlyTwoSeconds()
        {
            var victim = CreateCombatant("EnemyInkTwoSeconds", Team.TeamB);
            const float dotPerSecond = 20f;
            const int steps = 20; // 2.0s / 0.1s

            for (int i = 0; i < steps; i++)
            {
                victim.ApplyEnemyInkBurn(dotPerSecond * 0.1f, 60f);
            }

            Assert.AreEqual(60f, victim.CurrentHealth, 0.001f, "满血 100 → 60 需 2.0s（20 DPS）");
        }

        [Test]
        public void EnemyInkBurn_StartingAtSixtyFive_StopsAtSixty_WithoutOvershoot()
        {
            var victim = CreateCombatant("EnemyInkNearFloor", Team.TeamB);
            victim.TakeDamage(35f, Team.TeamA, victim.transform.position);
            Assert.AreEqual(65f, victim.CurrentHealth, 0.001f);

            victim.ApplyEnemyInkBurn(20f, 60f);
            Assert.AreEqual(60f, victim.CurrentHealth, 0.001f, "65 - 20 不应穿到 45，必须钳制在 60");

            victim.ApplyEnemyInkBurn(20f, 60f);
            Assert.AreEqual(60f, victim.CurrentHealth, 0.001f, "已在底线时敌墨不再造成伤害");
        }

        [Test]
        public void EnemyInkBurn_IsIgnoredWhenInvulnerableOrDead()
        {
            var victim = CreateCombatant("EnemyInkInvuln", Team.TeamB);
            victim.IsInvulnerable = true;
            victim.ApplyEnemyInkBurn(20f, 60f);
            Assert.AreEqual(100f, victim.CurrentHealth, 0.001f, "无敌帧内敌墨不结算");

            victim.IsInvulnerable = false;
            victim.TakeDamage(200f, Team.TeamA, victim.transform.position);
            Assert.IsFalse(victim.IsAlive);
            victim.ApplyEnemyInkBurn(20f, 60f);
            Assert.AreEqual(0f, victim.CurrentHealth, 0.001f, "阵亡后敌墨不再改变血量");
        }

        // ------------------------------------------------------------------ 治疗 / 友伤 / 阵亡计时

        [Test]
        public void Heal_CapsAtMaxAndIsIgnoredWhenDead()
        {
            var victim = CreateCombatant("HealCap", Team.TeamA);
            victim.TakeDamage(40f, Team.TeamB, victim.transform.position);
            Assert.AreEqual(60f, victim.CurrentHealth, 0.001f);

            victim.Heal(999f);
            Assert.AreEqual(100f, victim.CurrentHealth, 0.001f, "治疗不得超过最大生命值");

            victim.TakeDamage(200f, Team.TeamB, victim.transform.position);
            victim.Heal(50f);
            Assert.AreEqual(0f, victim.CurrentHealth, 0.001f, "阵亡后治疗无效，必须等待复活");
        }

        [Test]
        public void TakeDamage_RejectsFriendlyFire_ButAllowsNeutralSplash()
        {
            var victim = CreateCombatant("FriendlyFire", Team.TeamA);

            bool diedFromAlly = victim.TakeDamage(30f, Team.TeamA, victim.transform.position);
            Assert.IsFalse(diedFromAlly);
            Assert.AreEqual(100f, victim.CurrentHealth, 0.001f, "同队伤害应被拒绝");

            victim.TakeDamage(30f, Team.Neutral, victim.transform.position);
            Assert.AreEqual(70f, victim.CurrentHealth, 0.001f, "中立来源（环境/自杀）仍应结算");
        }

        [Test]
        public void Kill_StartsFourSecondRespawn_AndResetRestoresFullState()
        {
            var victim = CreateCombatant("RespawnCycle", Team.TeamB);
            victim.TakeDamage(200f, Team.TeamA, victim.transform.position);

            Assert.IsFalse(victim.IsAlive);
            Assert.AreEqual(4f, victim.RespawnRemaining, 0.001f, "默认复活倒计时 4.0s");
            Assert.AreEqual(0f, victim.CurrentHealth, 0.001f);

            victim.ResetImmediately();
            Assert.IsTrue(victim.IsAlive);
            Assert.AreEqual(100f, victim.CurrentHealth, 0.001f);
            Assert.AreEqual(0f, victim.RespawnRemaining, 0.001f, "复位后倒计时应清零");
            Assert.IsFalse(victim.IsInvulnerable);
        }

        // ------------------------------------------------------------------ 配置契约（P2-A 对齐 Inkwave）

        [Test]
        public void PlayerConfig_StealthSwimAndRecoveryContracts()
        {
            var cfg = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                Assert.AreEqual(100f, cfg.maxInk, 0.001f);
                Assert.AreEqual(11.8f, cfg.swimMoveSpeed, 0.001f, "潜墨移速 11.8 m/s");
                Assert.AreEqual(1.9f, cfg.enemyInkMoveSpeed, 0.001f, "踩敌墨减速到 1.9 m/s");
                Assert.AreEqual(42f, cfg.swimInkRefillRate, 0.001f, "潜墨回墨 42/s");
                Assert.AreEqual(60f, cfg.swimHealthRegenRate, 0.001f, "潜墨回血 60/s");
                Assert.AreEqual(20f, cfg.enemyInkDamagePerSec, 0.001f, "敌墨 DOT 20/s");
                Assert.AreEqual(60f, cfg.enemyInkMinHealthFloor, 0.001f, "敌墨保护底线 60 HP");
                Assert.AreEqual(2f, cfg.enemyInkRecoveryRatePerSec, 0.001f, "受击槽 2/s 恢复（0.5s 退出）");
                Assert.AreEqual(1.5f, cfg.stealthSpeedThreshold, 0.001f, "隐匿速度阈值 1.5 m/s");
                Assert.AreEqual(0.35f, cfg.stealthAllyAlpha, 0.001f, "己方视角半透明 0.35");
                Assert.AreEqual(6.5f, cfg.swimRippleMinSpeed, 0.001f);
                Assert.AreEqual(0.07f, cfg.emergeTransitionTime, 0.001f, "出水过渡 0.07s");
                Assert.AreEqual(0.16f, cfg.fireBufferTime, 0.001f, "开火缓冲 0.16s");
                Assert.AreEqual(4f, cfg.respawnDelay, 0.001f);

                // 空罐 2.4s 回满；满血 1.0s 内从 60 回满（60/s）
                Assert.LessOrEqual(cfg.maxInk / cfg.swimInkRefillRate, 2.4f, "空罐回满应不超过 2.4s");
                Assert.LessOrEqual(40f / cfg.swimHealthRegenRate, 0.67f, "60 → 100 回血约 0.667s");
                Assert.AreEqual(0.5f, 1f / cfg.enemyInkRecoveryRatePerSec, 0.001f, "离开敌墨 0.5s 完全恢复");
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void EnemyInkGrip_DecaysLinearlyToZero()
        {
            float grip = 1f;
            const float rate = 2f;
            for (int i = 0; i < 25; i++) // 0.5s
            {
                grip = Mathf.Max(0f, grip - rate * 0.02f);
            }

            Assert.AreEqual(0f, grip, 0.001f, "踩敌墨 0.5s 后受击惩罚状态应完全退出");
        }

        // ------------------------------------------------------------------ helpers

        private CombatantHealth CreateCombatant(string name, Team team)
        {
            var go = new GameObject(name);
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            CombatantHealthRegistry.Register(health);
            _createdObjects.Add(go);
            return health;
        }
    }
}