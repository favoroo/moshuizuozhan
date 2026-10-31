using NUnit.Framework;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 瞄准点 → 射击目标 / 起点的纯逻辑回归：
    /// 1. 射程内原样命中准星，超距沿准星方向截断到射程，不再反解到够不到的远点；
    /// 2. 长枪管近距离（瞄准点在枪口后方 / 过近）改用胸口起点，避免平行偏移；
    /// 3. 超距截断后，弹丸仍精确穿过「枪口→准星」连线上的截断点。
    /// </summary>
    public class WeaponAimRangeTests
    {
        private static readonly Vector3 Muzzle = new Vector3(0f, 0.8f, 0f);
        private static readonly Vector3 Chest = new Vector3(0f, 0.95f, 0f);

        [Test]
        public void GetRangeClampedTarget_WithinRange_ReturnsExactGroundPoint()
        {
            Vector3 ground = new Vector3(2f, 0f, 6f);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, ground);

            Vector3 target = aim.GetRangeClampedTarget(Muzzle, 12f);

            Assert.AreEqual(ground.x, target.x, 0.0001f);
            Assert.AreEqual(ground.y, target.y, 0.0001f);
            Assert.AreEqual(ground.z, target.z, 0.0001f);
        }

        [Test]
        public void GetRangeClampedTarget_BeyondRange_ClampsHorizontalAndKeepsDirection()
        {
            Vector3 farGround = new Vector3(0f, 0f, 30f);
            Vector3 direction = (farGround - Muzzle).normalized;
            WeaponAim aim = WeaponAim.FromDirection(direction, farGround);

            Vector3 target = aim.GetRangeClampedTarget(Muzzle, 12f);

            // 沿枪口→命中点方向按水平距离截断到 12m。
            Vector3 expected = Muzzle + (farGround - Muzzle) * (12f / 30f);
            Assert.AreEqual(expected.x, target.x, 0.01f);
            Assert.AreEqual(expected.y, target.y, 0.01f);
            Assert.AreEqual(expected.z, target.z, 0.01f);
        }

        [Test]
        public void GetRangeClampedTarget_NoGroundPoint_UsesDirectionTimesRange()
        {
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward);

            Vector3 target = aim.GetRangeClampedTarget(Muzzle, 12f);

            Assert.AreEqual(Muzzle.z + 12f, target.z, 0.0001f);
        }

        [Test]
        public void GetShotOrigin_UsesMuzzle_WhenAimPointIsInFrontAndFarEnough()
        {
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, 6f));

            Vector3 origin = aim.GetShotOrigin(Muzzle, Chest);

            Assert.AreEqual(Muzzle, origin);
        }

        [Test]
        public void GetShotOrigin_FallsBackToChest_WhenAimPointIsBehindMuzzle()
        {
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0.8f, -0.5f));

            Vector3 origin = aim.GetShotOrigin(Muzzle, Chest);

            Assert.AreEqual(Chest, origin, "长枪管越过近距离目标时必须改用胸口起点");
        }

        [Test]
        public void GetShotOrigin_FallsBackToChest_WhenAimPointIsTooClose()
        {
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0.8f, 0.1f));

            Vector3 origin = aim.GetShotOrigin(Muzzle, Chest);

            Assert.AreEqual(Chest, origin);
        }

        [Test]
        public void GetShotDirection_SmallerMinDistance_AllowsCloseOffAxisAim()
        {
            Vector3 ground = new Vector3(0.2f, 0.8f, 0.2f);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, ground);

            Vector3 defaultDir = aim.GetShotDirection(Muzzle);
            Vector3 closeDir = aim.GetShotDirection(Muzzle, 0.2f);

            Assert.AreEqual(Vector3.forward, defaultDir, "默认 0.6m 内仍回退相机方向");
            Assert.Greater(closeDir.x, 0.5f, "长枪管近距离应允许指向实际命中点");
        }

        [Test]
        public void BeyondRange_ProjectilePassesThroughClampedCrosshairPoint()
        {
            const float speed = 34f;
            const float gravity = 28f;
            const float range = 12f;
            const float hitRadius = 0.18f;

            Vector3 farGround = new Vector3(0f, 0f, 30f);
            Vector3 direction = (farGround - Muzzle).normalized;
            WeaponAim aim = WeaponAim.FromDirection(direction, farGround);

            Vector3 clamped = aim.GetRangeClampedTarget(Muzzle, range);
            Vector3 solveTarget = clamped + Vector3.up * hitRadius;
            Vector3 launch = InkBallistics.SolveLaunchDirection(Muzzle, solveTarget, speed, gravity, range);
            float flightTime = InkBallistics.PredictFlightTime(launch, speed, range);
            Vector3 atRange = InkBallistics.PredictPosition(Muzzle, launch * speed, gravity, flightTime);

            Assert.AreEqual(solveTarget.x, atRange.x, 0.02f, "水平 12m 处仍应落在准星方向线上");
            Assert.AreEqual(solveTarget.y, atRange.y, 0.02f);
            Assert.AreEqual(solveTarget.z, atRange.z, 0.02f);
        }
    }
}
