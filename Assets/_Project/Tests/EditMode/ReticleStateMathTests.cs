using System;
using NUnit.Framework;
using P1.UI;
using P1.Weapons;

namespace P1.Tests
{
    /// <summary>
    /// 准星状态映射回归：
    /// 1. 射程内实线、超距变暗+虚线；
    /// 2. 锁定敌人放大，超距时抑制锁定；
    /// 3. 滚筒准星表达推地宽度，不参与超距变暗；
    /// 4. 每种武器类型都必须映射到一个准星形状（防止新武器静默无准星）。
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

        /// <summary>
        /// 回归：双枪 / 泼桶 / 爆破枪 / 加特林曾因为屏幕准星只比较三种 kind 而整块消失。
        /// 这里对枚举里的每一个值都断言能拿到形状，新增武器忘了补映射会立刻转红。
        /// </summary>
        [Test]
        public void EveryWeaponKind_MapsToAReticleShape()
        {
            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                Assert.IsTrue(Enum.IsDefined(typeof(ReticleShape), ReticleStateMath.ShapeFor(kind)),
                    $"{kind} 必须映射到一种准星形状，否则该武器在屏幕上没有准星");
            }
        }

        [Test]
        public void WeaponKind_MapsToExpectedShape()
        {
            Assert.AreEqual(ReticleShape.Shooter, ReticleStateMath.ShapeFor(WeaponKind.Shooter));
            Assert.AreEqual(ReticleShape.Shooter, ReticleStateMath.ShapeFor(WeaponKind.Dualies), "双枪复用枪形准星");
            Assert.AreEqual(ReticleShape.Shooter, ReticleStateMath.ShapeFor(WeaponKind.Slosher), "泼桶复用枪形准星");
            Assert.AreEqual(ReticleShape.Shooter, ReticleStateMath.ShapeFor(WeaponKind.Blaster), "爆破枪复用枪形准星");
            Assert.AreEqual(ReticleShape.Shooter, ReticleStateMath.ShapeFor(WeaponKind.Splatling),
                "加特林蓄满后是持续扫射，散布反馈比蓄力环更重要，留在枪形");
            Assert.AreEqual(ReticleShape.Roller, ReticleStateMath.ShapeFor(WeaponKind.Roller));
            Assert.AreEqual(ReticleShape.Charger, ReticleStateMath.ShapeFor(WeaponKind.Charger));
        }

        [Test]
        public void NewlyCoveredWeapons_UseRangeState()
        {
            // 这 4 把是弹丸武器，必须正常参与超距变暗（旧实现里它们根本没有准星）
            Assert.IsTrue(ReticleStateMath.Compute(20f, 12f, false, WeaponKind.Dualies).OutOfRange);
            Assert.IsTrue(ReticleStateMath.Compute(20f, 12f, false, WeaponKind.Slosher).OutOfRange);
            Assert.IsTrue(ReticleStateMath.Compute(20f, 12f, false, WeaponKind.Blaster).OutOfRange);
            Assert.IsTrue(ReticleStateMath.Compute(20f, 12f, false, WeaponKind.Splatling).OutOfRange);
        }

        [Test]
        public void ClampSpread_NeverGoesBelowMinimum()
        {
            // 泼桶 / 双枪架枪的 SpreadDegrees 为 0，若不夹住，4 个刻度会全叠在中心点
            Assert.AreEqual(ReticleStateMath.MinSpreadDegrees, ReticleStateMath.ClampSpread(0f), 0.0001f);
            Assert.AreEqual(ReticleStateMath.MinSpreadDegrees, ReticleStateMath.ClampSpread(-3f), 0.0001f);
            Assert.AreEqual(4f, ReticleStateMath.ClampSpread(4f), 0.0001f);
        }
    }
}
