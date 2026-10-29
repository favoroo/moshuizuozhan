using NUnit.Framework;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 弹道反解回归测试：反解出的发射方向经与运行时一致的梯形积分推进后，
    /// 必须精确穿过瞄准点（含地面、高台、最大射程截断与不可达回退）。
    /// </summary>
    public class InkBallisticsTests
    {
        private const float Speed = 21f;
        private const float Gravity = 28f;
        private const float MaxRange = 7.2f;

        [Test]
        public void SolvedDirection_LandsOnGroundAimPoint_AcrossEffectiveRange()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);

            for (float distance = 1.5f; distance <= MaxRange + 0.001f; distance += 0.3f)
            {
                Vector3 target = new Vector3(distance, 0f, 0f);

                AssertLanding(muzzle, target, distance, "ground aim point");
            }
        }

        [Test]
        public void SolvedDirection_LandsOnRaisedPlatformAimPoint()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);

            for (float distance = 2f; distance <= 6.5f; distance += 0.9f)
            {
                Vector3 target = new Vector3(distance, 1.4f, 0f);

                AssertLanding(muzzle, target, distance, "raised platform");
            }
        }

        [Test]
        public void SolvedDirection_CompensatesSphereRadius_SoCircleBottomTouchesGround()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            const float hitRadius = 0.18f;

            for (float distance = 2f; distance <= MaxRange; distance += 1f)
            {
                Vector3 aimPoint = new Vector3(0f, 0f, distance);
                Vector3 solveTarget = aimPoint + Vector3.up * hitRadius;

                Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, solveTarget, Speed, Gravity, MaxRange);
                float flightTime = InkBallistics.PredictFlightTime(direction, Speed, distance);
                Vector3 centerAtAim = InkBallistics.PredictPosition(muzzle, direction * Speed, Gravity, flightTime);

                Assert.AreEqual(hitRadius, centerAtAim.y, 0.01f,
                    $"弹丸球心在 {distance:0.0}m 处应恰好离地一个碰撞半径，球体底部与地面相切");
                Assert.AreEqual(aimPoint.x, centerAtAim.x, 0.01f);
                Assert.AreEqual(aimPoint.z, centerAtAim.z, 0.01f);
            }
        }

        [Test]
        public void SolvedDirection_ClampsToMaxRange()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            Vector3 farTarget = new Vector3(0f, 0f, 30f);

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, farTarget, Speed, Gravity, MaxRange);
            float flightTime = InkBallistics.PredictFlightTime(direction, Speed, MaxRange);
            Vector3 landing = InkBallistics.PredictPosition(muzzle, direction * Speed, Gravity, flightTime);

            Assert.AreEqual(0f, landing.y, 0.01f);
            Assert.AreEqual(MaxRange, landing.z, 0.02f, "超出射程的瞄准点应被截断到最大射程");
        }

        [Test]
        public void UnreachableTarget_ReturnsNormalizedFallbackDirection()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            Vector3 impossible = new Vector3(5f, 40f, 0f);

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, impossible, Speed, Gravity, MaxRange);

            Assert.IsFalse(float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z));
            Assert.AreEqual(1f, direction.magnitude, 0.0001f);
            Assert.GreaterOrEqual(direction.y, 0f);
        }

        [Test]
        public void ZeroGravity_FallsBackToDirectAimDirection()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            Vector3 target = new Vector3(0f, 0f, 4f);

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, target, Speed, 0f, MaxRange);

            Assert.AreEqual(1f, direction.magnitude, 0.0001f);
            Assert.AreEqual(0f, direction.y, 0.0001f, "无重力时不做仰角抬升");
            Assert.AreEqual(1f, direction.z, 0.0001f);
        }

        private static void AssertLanding(Vector3 muzzle, Vector3 target, float horizontalDistance, string label)
        {
            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, target, Speed, Gravity, MaxRange);
            float flightTime = InkBallistics.PredictFlightTime(direction, Speed, horizontalDistance);
            Vector3 landing = InkBallistics.PredictPosition(muzzle, direction * Speed, Gravity, flightTime);

            Assert.AreEqual(target.x, landing.x, 0.01f, $"{label}: x 落点偏差");
            Assert.AreEqual(target.y, landing.y, 0.01f, $"{label}: y 落点偏差");
            Assert.AreEqual(target.z, landing.z, 0.01f, $"{label}: z 落点偏差");
        }
    }
}
