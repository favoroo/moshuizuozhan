using NUnit.Framework;
using P1.AI;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// Bot 个性倍率纯逻辑测试：区间钳制、恒等语义、ApplyTo 只改传入副本（不碰源资产）。
    /// </summary>
    public class BotPersonalityMathTests
    {
        [Test]
        public void Identity_ReportsIdentity_AndAppliesNoChange()
        {
            Assert.IsTrue(BotPersonality.Identity.IsIdentity());

            BotConfigSO config = CreateConfigCopy();
            float reactionBefore = config.reactionTime;
            float wanderBefore = config.aimWanderDegrees;
            float thresholdBefore = config.retreatHealthThreshold;

            BotPersonalityMath.ApplyTo(config, BotPersonality.Identity);

            Assert.AreEqual(reactionBefore, config.reactionTime, 1e-4f);
            Assert.AreEqual(wanderBefore, config.aimWanderDegrees, 1e-4f);
            Assert.AreEqual(thresholdBefore, config.retreatHealthThreshold, 1e-4f);
        }

        [Test]
        public void Create_SameSeed_IsDeterministic()
        {
            BotPersonality a = BotPersonalityMath.Create(4242);
            BotPersonality b = BotPersonalityMath.Create(4242);

            Assert.AreEqual(a.reactionTime, b.reactionTime, 1e-6f);
            Assert.AreEqual(a.engageDistance, b.engageDistance, 1e-6f);
            Assert.AreEqual(a.retreatHealth, b.retreatHealth, 1e-6f);
            Assert.AreEqual(a.sightRange, b.sightRange, 1e-6f);
        }

        [Test]
        public void Create_DifferentSeeds_VaryBehavior()
        {
            BotPersonality a = BotPersonalityMath.Create(1);
            BotPersonality b = BotPersonalityMath.Create(97);

            bool anyDifferent =
                !Mathf.Approximately(a.reactionTime, b.reactionTime) ||
                !Mathf.Approximately(a.aimError, b.aimError) ||
                !Mathf.Approximately(a.engageDistance, b.engageDistance) ||
                !Mathf.Approximately(a.bombUseChance, b.bombUseChance);
            Assert.IsTrue(anyDifferent, "不同种子必须给出不同个性");
            Assert.IsFalse(a.IsIdentity());
        }

        [Test]
        public void Create_StaysWithinDeclaredStyleRanges()
        {
            for (int seed = 0; seed < 300; seed++)
            {
                BotPersonality p = BotPersonalityMath.Create(seed);
                Assert.GreaterOrEqual(p.reactionTime, 0.75f);
                Assert.LessOrEqual(p.reactionTime, 1.30f);
                Assert.GreaterOrEqual(p.aimError, 0.60f);
                Assert.LessOrEqual(p.aimError, 1.45f);
                Assert.GreaterOrEqual(p.engageDistance, 0.82f);
                Assert.LessOrEqual(p.engageDistance, 1.18f);
                Assert.GreaterOrEqual(p.chargeRelease, 0.95f);
                Assert.LessOrEqual(p.chargeRelease, 1.00f);
            }
        }

        [Test]
        public void ApplyTo_ExtremeMultipliers_ClampIntoLegalRanges()
        {
            // 个性 × 难度双层叠乘的极端情况：所有字段必须落回 BotConfigSO 的合法区间，
            // 否则会出现反应时间 0、索敌半径爆炸这类「机器人才」行为。
            BotConfigSO config = CreateConfigCopy();
            BotPersonality extreme = new BotPersonality
            {
                reactionTime = 99f,
                aimError = 99f,
                aimWander = 99f,
                engageDistance = 0.001f,
                bombUseChance = 0f,
                retreatHealth = 99f,
                refillInk = 99f,
                sightRange = 99f,
                paintSweepSpeed = 0f,
                chargeRelease = 0f,
            };

            BotPersonalityMath.ApplyTo(config, extreme);

            Assert.GreaterOrEqual(config.reactionTime, 0.05f);
            Assert.GreaterOrEqual(config.aimErrorDegrees, 0.1f);
            Assert.GreaterOrEqual(config.shooterEngageFraction, 0.3f);
            Assert.LessOrEqual(config.shooterEngageFraction, 1f);
            Assert.GreaterOrEqual(config.rollerFireRange, 2f);
            Assert.LessOrEqual(config.rollerFireRange, 8f);
            Assert.GreaterOrEqual(config.retreatHealthThreshold, 5f);
            Assert.LessOrEqual(config.retreatHealthThreshold, 59f, "不得低于 60 血硬底线");
            Assert.GreaterOrEqual(config.refillInkThreshold, 5f);
            Assert.LessOrEqual(config.sightRangeScale, 3f);
            Assert.GreaterOrEqual(config.paintSweepSpeed, 0.2f);
            Assert.GreaterOrEqual(config.chargerReleaseCharge, 0.5f);
        }

        [Test]
        public void ApplyTo_PullsWeaponTacticsEngageDistancePerBot()
        {
            // 个性必须真的改变战术推导：同一武器，激进与保守 Bot 的理想交火距离应可区分。
            BotConfigSO baseConfig = CreateConfigCopy();
            BotConfigSO aggressive = CreateConfigCopy();
            BotConfigSO cautious = CreateConfigCopy();

            BotPersonalityMath.ApplyTo(aggressive, new BotPersonality
            {
                reactionTime = 1f, aimError = 1f, aimWander = 1f,
                engageDistance = 1.18f, bombUseChance = 1f, retreatHealth = 1f,
                refillInk = 1f, sightRange = 1f, paintSweepSpeed = 1f, chargeRelease = 1f,
            });
            BotPersonalityMath.ApplyTo(cautious, new BotPersonality
            {
                reactionTime = 1f, aimError = 1f, aimWander = 1f,
                engageDistance = 0.82f, bombUseChance = 1f, retreatHealth = 1f,
                refillInk = 1f, sightRange = 1f, paintSweepSpeed = 1f, chargeRelease = 1f,
            });

            Assert.Greater(aggressive.shooterEngageFraction, baseConfig.shooterEngageFraction);
            Assert.Less(cautious.shooterEngageFraction, baseConfig.shooterEngageFraction);
        }

        private static BotConfigSO CreateConfigCopy()
        {
            BotConfigSO config = ScriptableObject.CreateInstance<BotConfigSO>();
            config.hideFlags = HideFlags.DontSave;
            return config;
        }
    }
}
