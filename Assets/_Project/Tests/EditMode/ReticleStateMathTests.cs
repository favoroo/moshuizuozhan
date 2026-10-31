using NUnit.Framework;
using P1.UI;
using P1.Weapons;

namespace P1.Tests
{
    /// <summary>
    /// 准星状态映射回归：
    /// 1. 射程内实线、超距变暗+虚线；
    /// 2. 锁定敌人放大，超距时抑制锁定；
    /// 3. 滚筒准星表达推地宽度，不参与超距变暗。
    /// </summary>
    public class ReticleStateMathTests
    {
        [Test]
        public void WithinRange_IsSolidAndNotTarget()
        {
            ReticleStateMath.State state = ReticleStateMath.Compute(8f, 12f, false, WeaponKind.Shooter);

            Assert.IsFalse(state.OutOfRange);
            Assert.IsFalse(state.OnTarget);
            Assert.AreEqual(1f, state.Alpha, 0.0001f);
            Assert.AreEqual(1f, state.Scale, 0.0001f);
        }

        [Test]
        public void BeyondRange_DimsAndDashes()
        {
            ReticleStateMath.State state = ReticleStateMath.Compute(13f, 12f, false, WeaponKind.Shooter);

            Assert.IsTrue(state.OutOfRange);
            Assert.IsFalse(state.OnTarget);
            Assert.AreEqual(ReticleStateMath.FarAlpha, state.Alpha, 0.0001f);
            Assert.AreEqual(1f, state.Scale, 0.0001f);
        }

        [Test]
        public void BoundaryPadding_KeepsEdgeStable()
        {
            Assert.IsFalse(ReticleStateMath.Compute(12.4f, 12f, false, WeaponKind.Shooter).OutOfRange,
                "射程 + 0.5m 容差内不应闪成超距");
            Assert.IsTrue(ReticleStateMath.Compute(12.6f, 12f, false, WeaponKind.Shooter).OutOfRange);
        }

        [Test]
        public void Roller_IgnoresRangeState()
        {
            ReticleStateMath.State state = ReticleStateMath.Compute(30f, 6f, false, WeaponKind.Roller);

            Assert.IsFalse(state.OutOfRange, "滚筒准星是推地宽度条，不指向远处落点");
            Assert.AreEqual(1f, state.Alpha, 0.0001f);
        }

        [Test]
        public void OnTarget_ScalesUp()
        {
            ReticleStateMath.State state = ReticleStateMath.Compute(8f, 12f, true, WeaponKind.Shooter);

            Assert.IsTrue(state.OnTarget);
            Assert.IsFalse(state.OutOfRange);
            Assert.AreEqual(ReticleStateMath.TargetScale, state.Scale, 0.0001f);
        }

        [Test]
        public void OnTarget_SuppressedWhenOutOfRange()
        {
            ReticleStateMath.State state = ReticleStateMath.Compute(20f, 12f, true, WeaponKind.Charger);

            Assert.IsTrue(state.OutOfRange);
            Assert.IsFalse(state.OnTarget, "够不到的目标不应显示锁定高亮");
            Assert.AreEqual(1f, state.Scale, 0.0001f);
        }
    }
}
