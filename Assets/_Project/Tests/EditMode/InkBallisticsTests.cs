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
        public void UnreachableTarget_KeepsAimingDirection_InsteadOfFlattening()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            Vector3 impossible = new Vector3(5f, 40f, 0f);

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, impossible, Speed, Gravity, MaxRange);

            Assert.IsFalse(float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z));
            Assert.AreEqual(1f, direction.magnitude, 0.0001f);

            // 关键回归：不可达时必须继续朝目标方向抬升，而不是被压平成水平平射
            float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            Assert.Greater(direction.y, 0.8f, "朝天目标应保留高仰角");
            Assert.LessOrEqual(pitch, InkBallistics.MaxLaunchPitchDegrees + 0.001f, "仰角不得超过上限");
        }

        [Test]
        public void SkyAim_ReturnsUpwardDirection_WithinPitchLimit()
        {
            Vector3 muzzle = new Vector3(0f, 1.2f, 0f);
            Vector3 forward = new Vector3(0f, Mathf.Sin(60f * Mathf.Deg2Rad), Mathf.Cos(60f * Mathf.Deg2Rad));
            Vector3 skyPoint = muzzle + forward * 70f;

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, skyPoint, Speed, Gravity, MaxRange);

            float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            Assert.AreEqual(60f, pitch, 0.5f, "朝 60° 看时应以 60° 仰角射出");
            Assert.LessOrEqual(pitch, InkBallistics.MaxLaunchPitchDegrees + 0.001f);
        }

        [Test]
        public void ZeroGravity_FallsBackToDirectAimDirection()
        {
            Vector3 muzzle = new Vector3(0f, 0.75f, 0f);
            Vector3 target = new Vector3(0f, 0f, 4f);

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, target, Speed, 0f, MaxRange);

            Assert.AreEqual(1f, direction.magnitude, 0.0001f, "无重力时直接指向准星命中点");
            Assert.AreEqual(0f, direction.x, 0.0001f);
            Assert.Less(direction.y, 0f, "目标低于枪口时方向略向下，而不是被压平成水平");
            Vector3 expected = (target - muzzle).normalized;
            Assert.AreEqual(expected.y, direction.y, 0.0001f);
            Assert.AreEqual(expected.z, direction.z, 0.0001f);
        }

        [Test]
        public void SkyAim_ZeroGravity_KeepsUpwardDirection()
        {
            Vector3 muzzle = new Vector3(0f, 1f, 0f);
            Vector3 sky = muzzle + Vector3.up * 30f;

            Vector3 direction = InkBallistics.SolveLaunchDirection(muzzle, sky, 23f, 0f, 10.5f);

            Assert.Greater(direction.y, 0.99f, "正上方瞄准时应朝上发射");
        }

        [Test]
        public void ApplySpread_StaysInsideCone_AndActuallyDeviates()
        {
            Vector3 aim = new Vector3(0f, Mathf.Sin(45f * Mathf.Deg2Rad), Mathf.Cos(45f * Mathf.Deg2Rad));
            const float spread = 5.5f;

            float maxAngle = 0f;
            for (int i = 0; i < 256; i++)
            {
                Vector3 shot = InkBallistics.ApplySpread(aim, spread);
                Assert.AreEqual(1f, shot.magnitude, 0.0001f, "散布后必须仍是单位向量");

                float angle = Vector3.Angle(aim, shot);
                maxAngle = Mathf.Max(maxAngle, angle);
                Assert.LessOrEqual(angle, spread + 0.01f, "散布不得超出设定半角");
            }

            Assert.Greater(maxAngle, spread * 0.3f, "散布必须真的产生偏离，而不是空实现");
        }

        [Test]
        public void LobSolve_LandsOnTarget_WhenBasePitchIsEnough()
        {
            Vector3 muzzle = new Vector3(0f, 0.8f, 0f);
            Vector3 target = new Vector3(0f, 0f, 4f);

            InkBallistics.SolveLobLaunch(
                muzzle, target, 15f, 22f, 9.5f, 18f * Mathf.Deg2Rad,
                out float pitch, out float speed);

            Assert.AreEqual(18f, pitch * Mathf.Rad2Deg, 0.01f, "近距离应保持 18° 基础仰角");
            AssertLobLanding(muzzle, target, pitch, speed, 22f, "近距离抛桶");
        }

        [Test]
        public void LobSolve_RaisesArc_ToReachHighTarget()
        {
            Vector3 muzzle = new Vector3(0f, 0.8f, 0f);
            Vector3 target = new Vector3(0f, 1.5f, 7f);

            InkBallistics.SolveLobLaunch(
                muzzle, target, 15f, 22f, 9.5f, 18f * Mathf.Deg2Rad,
                out float pitch, out float speed);

            Assert.Greater(pitch * Mathf.Rad2Deg, 18.1f, "高目标应抬高仰角");
            Assert.LessOrEqual(pitch, InkBallistics.MaxLobPitchRadians + 0.0001f);
            AssertLobLanding(muzzle, target, pitch, speed, 22f, "高台抛桶");
        }

        [Test]
        public void LobSolve_AimingSky_ClampsPitchAndStaysUpward()
        {
            Vector3 muzzle = new Vector3(0f, 0.8f, 0f);
            Vector3 sky = muzzle + new Vector3(0f, 40f, 40f);

            InkBallistics.SolveLobLaunch(
                muzzle, sky, 15f, 22f, 9.5f, 18f * Mathf.Deg2Rad,
                out float pitch, out float speed);

            Assert.Greater(pitch * Mathf.Rad2Deg, 40f, "朝天泼洒必须是高抛");
            Assert.LessOrEqual(pitch, InkBallistics.MaxLobPitchRadians + 0.0001f);
            Assert.Greater(speed, 0f);
        }

        private static void AssertLobLanding(
            Vector3 muzzle, Vector3 target, float pitch, float speed, float gravity, string label)
        {
            Vector3 direction = new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch));
            float horizontal = new Vector2(target.x - muzzle.x, target.z - muzzle.z).magnitude;
            float flightTime = InkBallistics.PredictFlightTime(direction, speed, horizontal);
            Vector3 landing = InkBallistics.PredictPosition(muzzle, direction * speed, gravity, flightTime);

            Assert.AreEqual(target.y, landing.y, 0.02f, $"{label}: 落点高度应贴合准星");
            Assert.AreEqual(target.z, landing.z, 0.02f, $"{label}: 落点水平距离应贴合准星");
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
