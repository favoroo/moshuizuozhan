using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 潜行/射击按键仲裁的纯逻辑测试：验证射击中按 Shift 可立即潜入、
    /// 潜行中按开火可立即出枪、松开按键后的形态回落，以及 P2-A 新增的
    /// 出水快速过渡（0.07s）+ 开火输入缓冲（0.16s）时间模型。
    /// </summary>
    public class SwimFormInputArbiterTests
    {
        [Test]
        public void PressSwimWhileFiring_EntersSwimAndSuppressesFire()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(false, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsFire, "按住开火应出枪");
            Assert.IsFalse(arbiter.WantsSwim);

            arbiter.Update(true, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "射击中按潜行键应立即潜入");
            Assert.IsFalse(arbiter.WantsFire, "潜行形态应停止射击");
        }

        [Test]
        public void PressFireWhileSwimming_ExitsAndFiresSameFrame()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim);

            arbiter.Update(true, true, false, false, true, false);
            Assert.IsFalse(arbiter.WantsSwim, "潜行中按开火应立刻恢复人形");
            Assert.IsTrue(arbiter.WantsFire, "恢复人形的同一帧就应出枪");
        }

        [Test]
        public void ReleaseFireWhileHoldingSwim_ReturnsToSwim()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            arbiter.Update(true, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsFire);

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "松开开火但仍在按潜行键，应自动回到潜行");
            Assert.IsFalse(arbiter.WantsFire);
        }

        [Test]
        public void ReleaseSwim_ExitsSwim()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim);

            arbiter.Update(false, false, false, false, true, false);
            Assert.IsFalse(arbiter.WantsSwim, "松开潜行键应立即恢复人形");
        }

        [Test]
        public void SwimOnNeutralGround_DoesNotBlockFiring()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, false, false);
            Assert.IsFalse(arbiter.WantsSwim, "脚下没有己方墨水时不能潜行");

            arbiter.Update(true, true, false, false, false, false);
            Assert.IsTrue(arbiter.WantsFire, "不能潜行时应允许开火，避免按住 Shift 造成按键失效");
        }

        [Test]
        public void SteppingOntoOwnInk_WhileHoldingSwim_AutoEnters()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, false, false);
            Assert.IsFalse(arbiter.WantsSwim);

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "一直按着潜行键时，踩到己方墨水应自动潜入");
        }

        [Test]
        public void BombPress_BreaksSwim()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            arbiter.Update(true, false, true, false, true, false);
            Assert.IsFalse(arbiter.WantsSwim, "投掷手雷应立即打断潜行");
        }

        [Test]
        public void ReadySpecial_BreaksSwim_ButUnreadySpecialDoesNot()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "大招未就绪时按 Q 不应打断潜行");

            arbiter.Update(true, false, false, true, true, false);
            Assert.IsFalse(arbiter.WantsSwim, "释放大招应立即打断潜行");
        }

        [Test]
        public void SpecialLock_SuppressesSwimUntilFinished()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            arbiter.Update(true, false, false, false, true, true);
            Assert.IsFalse(arbiter.WantsSwim, "大招动作期间应锁定人形");

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "大招结束后按住潜行键应恢复潜行");
        }

        [Test]
        public void Reset_ClearsHeldStateAndIntent()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(false, true, false, false, true, false);
            arbiter.Update(true, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim);

            arbiter.Reset();
            Assert.IsFalse(arbiter.WantsSwim);
            Assert.IsFalse(arbiter.WantsFire);

            arbiter.Update(true, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim, "Reset 后仍按住的潜行键应视为新按下");
        }

        // ==================== 丝滑进出水仲裁（0.07s 出水过渡 + 0.16s 开火缓冲） ====================

        [Test]
        public void LegacyUpdate_KeepsInstantFireSemantics_NoEmergeGate()
        {
            var arbiter = new SwimFormInputArbiter();

            arbiter.Update(true, false, false, false, true, false);
            Assert.IsTrue(arbiter.WantsSwim);

            arbiter.Update(true, true, false, false, true, false);
            Assert.IsTrue(arbiter.WantsFire, "旧 6 参重载应保留当帧即时开火的向后兼容语义");
            Assert.IsFalse(arbiter.IsEmerging, "旧 6 参重载不推进时间模型，不应产生过渡期");
            Assert.IsFalse(arbiter.TryConsumeFireBuffer());
        }

        [Test]
        public void EmergeFromSwim_FireIntentHeld_FirstShotGatedUntil007s()
        {
            var arbiter = new SwimFormInputArbiter();
            const float dt = 0.02f;

            arbiter.Update(true, false, false, false, true, false, dt);
            Assert.IsTrue(arbiter.WantsSwim);

            // 出水瞬间按下开火：人形恢复立刻生效，但扳机被过渡期暂扣
            arbiter.Update(true, true, false, false, true, false, dt);
            Assert.IsFalse(arbiter.WantsSwim, "潜行中按开火应立刻恢复人形");
            Assert.IsTrue(arbiter.WantsFire, "开火意图当帧即成立");
            Assert.IsTrue(arbiter.IsEmerging, "应进入 0.07s 出水快速过渡");

            arbiter.Update(false, true, false, false, true, false, dt);
            Assert.IsTrue(arbiter.IsEmerging, "过渡未满 0.07s 不开火");
            arbiter.Update(false, true, false, false, true, false, dt);
            Assert.IsTrue(arbiter.IsEmerging);
            arbiter.Update(false, true, false, false, true, false, dt);
            Assert.IsTrue(arbiter.IsEmerging);

            arbiter.Update(false, true, false, false, true, false, dt);
            Assert.IsFalse(arbiter.IsEmerging, "累计 >= 0.07s 后过渡结束");
            Assert.IsTrue(arbiter.WantsFire, "按住开火时过渡结束立刻出枪");
            Assert.IsFalse(arbiter.TryConsumeFireBuffer(), "按住连发已覆盖缓冲消费，不应重复排队");
        }

        [Test]
        public void TapFireDuringEmerge_ReleaseBeforeGateOut_ShotIsBufferedNotLost()
        {
            var arbiter = new SwimFormInputArbiter();
            const float dt = 0.02f;

            arbiter.Update(true, false, false, false, true, false, dt);   // 潜行
            arbiter.Update(true, true, false, false, true, false, dt);    // 点按开火：出水 + 扣扳机 + 入缓冲
            arbiter.Update(false, false, false, false, true, false, dt);  // 立刻双松手
            Assert.IsTrue(arbiter.IsEmerging);
            Assert.IsFalse(arbiter.WantsFire);

            // 过渡（0.07s）必然先于缓冲（0.16s）结束：这次点击应被补射，绝不吞键
            arbiter.Update(false, false, false, false, true, false, dt);
            arbiter.Update(false, false, false, false, true, false, dt);
            arbiter.Update(false, false, false, false, true, false, dt);
            Assert.IsFalse(arbiter.IsEmerging);
            Assert.IsTrue(arbiter.TryConsumeFireBuffer(), "过渡期内按下过的开火键应在过渡结束后补射");
            Assert.IsFalse(arbiter.TryConsumeFireBuffer(), "同一次缓冲只补射一发");
        }

        [Test]
        public void FireBuffer_ExpiresAfter016s_NoGhostShot()
        {
            var arbiter = new SwimFormInputArbiter();
            arbiter.Update(true, false, false, false, true, false, 0.02f);
            arbiter.Update(true, true, false, false, true, false, 0.02f); // 点击一下
            arbiter.Update(false, false, false, false, true, false, 0.02f);

            for (int i = 0; i < 12; i++)
            {
                arbiter.Update(false, false, false, false, false, false, 0.02f); // 快进 0.24s
            }

            Assert.IsFalse(arbiter.IsEmerging);
            Assert.IsFalse(arbiter.TryConsumeFireBuffer(), "超过 0.16s 的缓冲作废，不应产生幽灵补射");
        }

        [Test]
        public void Reset_ClearsEmergeTimerAndFireBuffer()
        {
            var arbiter = new SwimFormInputArbiter();
            arbiter.Update(true, false, false, false, true, false, 0.02f);
            arbiter.Update(true, true, false, false, true, false, 0.02f);
            Assert.IsTrue(arbiter.IsEmerging);

            arbiter.Reset();
            Assert.IsFalse(arbiter.IsEmerging);
            Assert.AreEqual(0f, arbiter.FireBufferRemaining, 0.0001f);
            Assert.IsFalse(arbiter.TryConsumeFireBuffer());
        }

        [Test]
        public void PlayerConfig_InkwaveAlignedDefaults()
        {
            var cfg = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                Assert.AreEqual(6.0f, cfg.normalMoveSpeed, 0.001f, "人形态跑速对齐 Inkwave runSpeed");
                Assert.AreEqual(11.8f, cfg.swimMoveSpeed, 0.001f, "潜墨移速对齐 swimSpeed");
                Assert.AreEqual(1.9f, cfg.enemyInkMoveSpeed, 0.001f, "踩敌墨移速对齐 enemyInkSpeed");
                Assert.AreEqual(42f, cfg.swimInkRefillRate, 0.001f, "潜墨回墨对齐 inkRefillSwim");
                Assert.AreEqual(60f, cfg.swimHealthRegenRate, 0.001f, "潜墨回血对齐 regenRateSwim");
                Assert.AreEqual(20f, cfg.enemyInkDamagePerSec, 0.001f, "敌墨 DOT 对齐 enemyInkDps");
                Assert.AreEqual(60f, cfg.enemyInkMinHealthFloor, 0.001f, "敌墨保护底线 = hp - damageCap");
                Assert.AreEqual(0.07f, cfg.emergeTransitionTime, 0.0001f, "出水过渡对齐 emergeDelay");
                Assert.AreEqual(0.16f, cfg.fireBufferTime, 0.0001f, "开火缓冲对齐 fireBuffer");
                Assert.AreEqual(2.4f, cfg.maxInk / cfg.swimInkRefillRate, 0.05f, "空罐回满约 2.4s");
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }
    }
}