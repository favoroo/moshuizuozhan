using NUnit.Framework;
using P1.Player;

namespace P1.Tests
{
    /// <summary>
    /// 副武器手雷「按下即投」仲裁回归：
    /// 按下沿立刻投掷，按住不会连投，松开不会补投；不再存在瞄准态。
    /// </summary>
    [TestFixture]
    public class SubBombThrowArbiterTests
    {
        [Test]
        public void PressEdge_ThrowsImmediately_OnSameFrame()
        {
            var arbiter = new SubBombThrowArbiter();

            Assert.IsTrue(arbiter.Tick(true, true, false, false, false), "按下沿必须立刻投掷");
            Assert.IsFalse(arbiter.IsAiming, "新版不再有瞄准态");
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
