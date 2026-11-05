using NUnit.Framework;
using P1.Player;

namespace P1.Tests
{
    /// <summary>
    /// 副武器手雷输入仲裁回归：
    /// inkwave 式「按住瞄准 / 松手投出」状态机（Advance），以及旧「按下即投」接口（Tick）的兼容语义。
    /// </summary>
    [TestFixture]
    public class SubBombThrowArbiterTests
    {
        [Test]
        public void Advance_PressEntersAiming_NoThrow()
        {
            var arbiter = new SubBombThrowArbiter();

            var intent = arbiter.Advance(true, true, false, false, false, false, false);

            Assert.IsTrue(intent.IsAiming, "按下后进入按住瞄准");
            Assert.IsTrue(intent.StartedAiming, "本帧是瞄准起始帧");
            Assert.IsFalse(intent.ExecuteThrow, "按下不投，等松手");
            Assert.IsTrue(arbiter.IsAiming);
        }

        [Test]
        public void Advance_HoldKeepsAiming_NoRepeatedThrow()
        {
            var arbiter = new SubBombThrowArbiter();
            arbiter.Advance(true, true, false, false, false, false, false);

            for (int i = 0; i < 5; i++)
            {
                var intent = arbiter.Advance(false, true, false, false, false, false, false);
                Assert.IsTrue(intent.IsAiming, "按住期间保持瞄准预览");
                Assert.IsFalse(intent.ExecuteThrow, "按住期间不得连投");
            }
        }

        [Test]
        public void Advance_Release_ThrowsExactlyOnce()
        {
            var arbiter = new SubBombThrowArbiter();
            arbiter.Advance(true, true, false, false, false, false, false);

            var release = arbiter.Advance(false, false, true, false, false, false, false);
            Assert.IsTrue(release.ExecuteThrow, "松手必须投出");
            Assert.IsFalse(release.IsAiming, "投出后结束瞄准");

            var after = arbiter.Advance(false, false, true, false, false, false, false);
            Assert.IsFalse(after.ExecuteThrow, "同一次松手只投一颗");
            Assert.IsFalse(after.IsAiming);
        }

        [Test]
        public void Advance_QuickTapSameFrame_ThrowsOnce()
        {
            var arbiter = new SubBombThrowArbiter();

            var intent = arbiter.Advance(true, false, true, false, false, false, false);
            Assert.IsTrue(intent.ExecuteThrow, "按下+松开同帧（快速点按）也必须投出");
            Assert.IsFalse(intent.IsAiming);

            Assert.IsFalse(arbiter.Advance(false, false, true, false, false, false, false).ExecuteThrow,
                "松开沿不得补投第二颗");
        }

        [Test]
        public void Advance_TouchPressThenRelease_ThrowsOnce()
        {
            var arbiter = new SubBombThrowArbiter();

            var press = arbiter.Advance(false, false, false, true, true, false, false);
            Assert.IsTrue(press.IsAiming, "触屏 pointer-down 进入瞄准");
            Assert.IsFalse(press.ExecuteThrow);

            var release = arbiter.Advance(false, false, false, false, false, true, false);
            Assert.IsTrue(release.ExecuteThrow, "触屏 pointer-up 松手投出");
            Assert.IsFalse(release.IsAiming);
        }

        [Test]
        public void Advance_LostHeldState_ThrowsInsteadOfSticking()
        {
            var arbiter = new SubBombThrowArbiter();
            arbiter.Advance(true, true, false, false, false, false, false);

            // 系统手势/失焦导致没收到 release 沿，但 held 已经变 false：按松手收尾
            var intent = arbiter.Advance(false, false, false, false, false, false, false);
            Assert.IsTrue(intent.ExecuteThrow, "按住状态丢失不得让预览永久卡住");
            Assert.IsFalse(arbiter.IsAiming);
        }

        [Test]
        public void Advance_SpecialActive_CancelsAimingWithoutThrow()
        {
            var arbiter = new SubBombThrowArbiter();
            arbiter.Advance(true, true, false, false, false, false, false);

            var intent = arbiter.Advance(false, true, false, false, false, false, true);
            Assert.IsTrue(intent.Canceled, "大招演出必须取消瞄准");
            Assert.IsFalse(intent.ExecuteThrow, "取消时不得投出");
            Assert.IsFalse(arbiter.IsAiming);
        }

        [Test]
        public void Advance_NoInkOnPress_DeniesAimingAndThrow()
        {
            var arbiter = new SubBombThrowArbiter();

            var press = arbiter.Advance(true, true, false, false, false, false, false, hasInkForThrow: false);
            Assert.IsTrue(press.DeniedByInk, "空墨按下必须标记 DeniedByInk（调用方播「弹药耗尽」提示）");
            Assert.IsFalse(press.IsAiming, "空墨按下不得进入瞄准（不出弹道预览）");
            Assert.IsFalse(press.StartedAiming, "空墨按下不得打断潜墨形态");
            Assert.IsFalse(arbiter.IsAiming);

            var release = arbiter.Advance(false, false, true, false, false, false, false, hasInkForThrow: false);
            Assert.IsFalse(release.ExecuteThrow, "空墨被拒后松手不得投出");
            Assert.IsFalse(arbiter.IsAiming);
        }

        [Test]
        public void Advance_InkRecoveredAfterDeniedPress_StillNeedsNewPress()
        {
            var arbiter = new SubBombThrowArbiter();

            arbiter.Advance(true, true, false, false, false, false, false, hasInkForThrow: false);

            // 按住不放、墨水回满：被拒的那次按下不补进入瞄准，必须重新按一次
            var hold = arbiter.Advance(false, true, false, false, false, false, false, hasInkForThrow: true);
            Assert.IsFalse(hold.IsAiming, "被拒的按住不得因为墨水回满而自动进入瞄准");
            Assert.IsFalse(hold.ExecuteThrow);

            var release = arbiter.Advance(false, false, true, false, false, false, false, hasInkForThrow: true);
            Assert.IsFalse(release.ExecuteThrow, "被拒的按住松手也不得投出");

            var repress = arbiter.Advance(true, true, false, false, false, false, false, hasInkForThrow: true);
            Assert.IsTrue(repress.IsAiming, "重新按下（墨水已够）才进入瞄准");
        }

        [Test]
        public void Advance_Reset_ClearsAiming()
        {
            var arbiter = new SubBombThrowArbiter();
            arbiter.Advance(true, true, false, false, false, false, false);

            arbiter.Reset();
            Assert.IsFalse(arbiter.IsAiming, "Reset 后退出瞄准");
            Assert.IsFalse(arbiter.Advance(false, false, true, false, false, false, false).ExecuteThrow,
                "Reset 后残留松手不得补投");
        }

        [Test]
        public void Tick_PressEdge_ThrowsImmediately_LegacyContract()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(true, true, false, false, false), "旧接口按下沿立刻投掷");
            Assert.IsFalse(arbiter.IsAiming, "旧接口不进入瞄准态");
        }

        [Test]
        public void Hold_DoesNotRepeatThrow()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(true, true, false, false, false), "按下沿投第一颗");
            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(arbiter.Tick(false, true, false, false, false), "按住期间不得连投");
            }
        }

        [Test]
        public void Release_DoesNotThrowSecondBomb()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(true, true, false, false, false));
            Assert.IsFalse(arbiter.Tick(false, true, true, false, false), "松开沿不得补投第二颗");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, false), "同一次松手只处理一次");
        }

        [Test]
        public void QuickTap_ThrowsOnPress()
        {
            var arbiter = new SubBombThrowArbiter();

            // 同帧按下 + 松开：按下沿优先，仍然只投一次
            Assert.IsTrue(arbiter.Tick(true, true, true, false, false), "按下沿必须立刻投出");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, false), "松开不得补投");
        }

        [Test]
        public void TouchInjection_ThrowsImmediately()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(false, false, false, true, false), "触屏 pointer-down 注入 = 立即投掷");
        }

        [Test]
        public void TouchInjection_AndDesktopPress_SameFrame_ThrowsOnce()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(true, true, false, true, false), "同一帧只产生一次投掷事件");
        }

        [Test]
        public void SpecialActive_BlocksPressAndTouch()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, true), "大招中按下不得投掷");
            Assert.IsFalse(arbiter.Tick(false, false, false, true, true), "大招中触屏注入不得投掷");
            Assert.IsFalse(arbiter.IsAiming, "大招中也不存在瞄准态");
        }

        [Test]
        public void Reset_DoesNotResurrectThrow()
        {
            var arbiter = new SubBombThrowArbiter();

            arbiter.Reset();
            Assert.IsFalse(arbiter.IsAiming, "Reset 后仍然没有瞄准态");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, false), "Reset 后残留松手不得补投");
        }
    }
}
