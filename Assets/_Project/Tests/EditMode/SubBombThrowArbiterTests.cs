using NUnit.Framework;
using P1.Player;

namespace P1.Tests
{
    /// <summary>
    /// 副武器手雷「按住瞄准 / 松开投掷」仲裁回归：
    /// 核心场景 = 按下沿绝不立即投掷。旧实现按下即投（扣 70 墨 + 0.6s 冷却），
    /// 松开沿投第 2 颗时因墨水不足 / 冷中被静默拒绝，表现为「瞄准后松手丢不出来」。
    /// </summary>
    [TestFixture]
    public class SubBombThrowArbiterTests
    {
        [Test]
        public void PressEdge_DoesNotThrow_OnlyStartsAiming()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false), "按下沿不得立即投掷（旧 bug 回归）");
            Assert.IsTrue(arbiter.IsAiming, "按下沿应进入瞄准态");
        }

        [Test]
        public void HoldThenRelease_ThrowsExactlyOnce()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false), "按下 → 瞄准");
            Assert.IsFalse(arbiter.Tick(false, true, false, false, false), "按住 → 维持瞄准");
            Assert.IsFalse(arbiter.Tick(false, true, false, false, false));
            Assert.IsTrue(arbiter.Tick(false, true, true, false, false), "松开沿应投掷");
            Assert.IsFalse(arbiter.IsAiming, "投掷后应退出瞄准态");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, false), "同一次松手只投一颗");
        }

        [Test]
        public void QuickTap_ThrowsOnRelease()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false), "按下 → 瞄准");
            Assert.IsTrue(arbiter.Tick(false, false, true, false, false), "短按松手也必须投出");
        }

        [Test]
        public void PressAndReleaseAdjacentFrames_ThrowsOnHeldLost()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false), "按下（同帧松开沿被按下沿优先）");
            Assert.IsTrue(arbiter.Tick(false, false, false, false, false), "下一帧 held 已消失应补投，绝不吞键");
        }

        [Test]
        public void TouchInjection_ThrowsWithoutPriorAim()
        {
            var arbiter = new SubBombThrowArbiter();

            // 触屏短按（< 0.22s）：从未建立瞄准态，pointer-up 注入应直接快投
            Assert.IsTrue(arbiter.Tick(false, false, false, true, false), "触屏短按松手 = 快投");
        }

        [Test]
        public void TouchHoldThenInjection_ThrowsOnce()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(false, true, false, false, false), "长按（TouchBombHeld）→ 瞄准");
            Assert.IsTrue(arbiter.IsAiming);
            Assert.IsTrue(arbiter.Tick(false, false, false, true, false), "触屏长按松手 = 投掷");
            Assert.IsFalse(arbiter.IsAiming);
        }

        [Test]
        public void SpecialActive_CancelsAim_AndNeverThrows()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false), "先进入瞄准");
            Assert.IsFalse(arbiter.Tick(false, true, false, false, true), "大招中不得投掷");
            Assert.IsFalse(arbiter.IsAiming, "大招应取消瞄准态");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, true), "大招中松手也不投");
            Assert.IsFalse(arbiter.Tick(false, false, true, false, false), "大招结束后残留松手不补投");
        }

        [Test]
        public void Reset_ClearsAiming_AimRestoresWhileStillHeld()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsFalse(arbiter.Tick(true, true, false, false, false));
            arbiter.Reset();
            Assert.IsFalse(arbiter.IsAiming, "Reset 应清空瞄准态（死亡 / 暂停早退同步）");
            Assert.IsFalse(arbiter.Tick(false, true, false, false, false), "Reset 后仍按住应兜底恢复瞄准");
            Assert.IsTrue(arbiter.IsAiming);
        }
    }
}
