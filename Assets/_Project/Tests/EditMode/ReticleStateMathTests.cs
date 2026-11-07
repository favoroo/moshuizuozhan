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

        // ------------------------------------------------------------------ P40 武器身份外形

        [Test]
        public void EveryWeaponKind_MapsToAReticleStyle()
        {
            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                Assert.IsTrue(Enum.IsDefined(typeof(ReticleStyle), ReticleStateMath.StyleFor(kind)),
                    $"{kind} 必须映射到一个枪形身份外形，否则它会退回小枪轮廓、玩家认不出武器");
            }
        }

        [Test]
        public void WeaponStyle_IsTheExpectedSilhouette()
        {
            Assert.AreEqual(ReticleStyle.Basic, ReticleStateMath.StyleFor(WeaponKind.Shooter));
            Assert.AreEqual(ReticleStyle.Twin, ReticleStateMath.StyleFor(WeaponKind.Dualies));
            Assert.AreEqual(ReticleStyle.Lob, ReticleStateMath.StyleFor(WeaponKind.Slosher));
            Assert.AreEqual(ReticleStyle.Blast, ReticleStateMath.StyleFor(WeaponKind.Blaster));
            Assert.AreEqual(ReticleStyle.Spin, ReticleStateMath.StyleFor(WeaponKind.Splatling));

            // 五把弹丸武器必须各不相同（旧版 5 把共用一个轮廓，这是 P40 要修的核心问题）
            var seen = new System.Collections.Generic.HashSet<ReticleStyle>();
            foreach (WeaponKind kind in new[]
                     {
                         WeaponKind.Shooter, WeaponKind.Dualies, WeaponKind.Slosher,
                         WeaponKind.Blaster, WeaponKind.Splatling
                     })
            {
                Assert.IsTrue(seen.Add(ReticleStateMath.StyleFor(kind)), $"{kind} 的外形与其他弹丸武器重复");
            }
        }

        [Test]
        public void TickOffset_ClearsCenterDotAtRest()
        {
            // 静止收在主环内侧，但不能盖住中心点（中心点直径 9px → 半径 4.5）
            float offset = ReticleStateMath.TickOffset(ReticleStateMath.MinSpreadDegrees);
            Assert.Greater(offset, 9f, "静止时刻度必须离开中心点");
            Assert.LessOrEqual(offset, ReticleStateMath.ShooterRingDiameter * 0.5f,
                "静止时刻度中心应落在主环内侧，不能一开局就压住环带");
        }

        [Test]
        public void TickOffset_GrowsWithSpreadAndPunch()
        {
            float rest = ReticleStateMath.TickOffset(ReticleStateMath.MinSpreadDegrees);
            float wide = ReticleStateMath.TickOffset(6.5f);
            float punch = ReticleStateMath.TickOffset(ReticleStateMath.MinSpreadDegrees, 1f, 0f);
            float empty = ReticleStateMath.TickOffset(ReticleStateMath.MinSpreadDegrees, 0f, 1f);

            Assert.Greater(wide, rest, "散布越大刻度越外扩");
            Assert.Greater(punch, rest, "开火当帧刻度必须弹出去");
            Assert.Less(empty, rest, "空墨拒射刻度反向收拢");
        }

        [Test]
        public void TickOffset_StaysInComfortableBand()
        {
            // 上限保护：叠满散布 + 开火冲击也不能飞出 1080p 视野的舒适读数区
            float max = ReticleStateMath.TickOffset(90f, 1f, 0f);
            Assert.LessOrEqual(max, 64f, "刻度外扩上限必须钳住，否则高散布武器准星会散成一整屏");
            Assert.GreaterOrEqual(ReticleStateMath.TickOffset(0f, 0f, 1f), 10f,
                "下限保护：空打收拢也不能把刻度收回中心点");
        }
    }
}
