using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    [TestFixture]
    public class ReticleAndTrajectoryTests
    {
        [Test]
        public void Trajectory_GeneratesExact64Points_WithExactEndpoints()
        {
            Vector3 start = new Vector3(0f, 1.2f, 0f);
            Vector3 landing = new Vector3(5f, 0f, 6f);
            float arcHeight = 2.6f;

            Vector3[] points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            Assert.AreEqual(64, points.Length);

            InkBombTrajectoryUtility.GenerateTrajectoryPoints(start, landing, arcHeight, points);

            // 第 1 个点必须与发射点 start 重合
            Assert.AreEqual(start.x, points[0].x, 0.001f);
            Assert.AreEqual(start.y, points[0].y, 0.001f);
            Assert.AreEqual(start.z, points[0].z, 0.001f);

            // 最后一个点（第 64 个点）必须与着陆点 landing 严格重合
            Assert.AreEqual(landing.x, points[63].x, 0.001f);
            Assert.AreEqual(landing.y, points[63].y, 0.001f);
            Assert.AreEqual(landing.z, points[63].z, 0.001f);
        }

        [Test]
        public void Trajectory_ApexReachesExactArcHeight_AtMidpoint()
        {
            Vector3 start = new Vector3(0f, 0f, 0f);
            Vector3 landing = new Vector3(10f, 0f, 0f);
            float arcHeight = 3.2f;

            Vector3 mid = InkBombTrajectoryUtility.EvaluateArcPoint(start, landing, arcHeight, 0.5f);

            // y(0.5) = lerp(0, 0, 0.5) + 4 * 3.2 * 0.5 * 0.5 = 3.2
            Assert.AreEqual(5f, mid.x, 0.001f);
            Assert.AreEqual(arcHeight, mid.y, 0.001f);
            Assert.AreEqual(0f, mid.z, 0.001f);
        }

        [Test]
        public void Trajectory_LandingPosition_ClampsToMaxThrowDistance()
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombMaxThrowDistance = 7.5f;

            Vector3 start = Vector3.zero;
            Vector3 farTarget = new Vector3(0f, 0f, 25f); // 25m 超远距离

            Vector3 resolved = InkBombTrajectoryUtility.ResolveLandingPosition(start, farTarget, cfg);

            Vector3 flatDelta = resolved - start;
            flatDelta.y = 0f;
            Assert.AreEqual(7.5f, flatDelta.magnitude, 0.01f);

            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void ComputeArcParams_ArcHeightGrowsWithDistance_AndClampsToFar()
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombMaxThrowDistance = 12f;
            cfg.bombArcHeightNear = 1.6f;
            cfg.bombArcHeightFar = 3.8f;
            cfg.bombFlightSpeed = 15f;

            Vector3 start = Vector3.zero;
            var near = InkBombTrajectoryUtility.ComputeArcParams(start, start, cfg);
            var mid = InkBombTrajectoryUtility.ComputeArcParams(start, new Vector3(6f, 0f, 0f), cfg);
            var far = InkBombTrajectoryUtility.ComputeArcParams(start, new Vector3(12f, 0f, 0f), cfg);

            Assert.AreEqual(1.6f, near.ArcHeight, 0.05f, "零距离落点应取近距弧高锚点");
            Assert.AreEqual(3.8f, far.ArcHeight, 0.05f, "满射程落点应取远距弧高");
            Assert.Greater(mid.ArcHeight, near.ArcHeight, "弧高应随落点距离单调增加");
            Assert.Less(mid.ArcHeight, far.ArcHeight);
            Assert.AreEqual(3.8f, InkBombTrajectoryUtility.ComputeArcParams(start, new Vector3(30f, 0f, 0f), cfg).ArcHeight,
                0.05f, "超远目标弧高按满射程档钳制");

            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void ComputeArcParams_FlightDurationFollowsSpeed_WithNearFloor()
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombFlightSpeed = 15f;

            var near = InkBombTrajectoryUtility.ComputeArcParams(Vector3.zero, new Vector3(1f, 0f, 0f), cfg);
            var far = InkBombTrajectoryUtility.ComputeArcParams(Vector3.zero, new Vector3(12f, 0f, 0f), cfg);

            Assert.AreEqual(0.28f, near.FlightDuration, 0.001f, "贴脚投掷按下限 0.28s 钳制，防瞬移手感");
            Assert.AreEqual(0.8f, far.FlightDuration, 0.001f, "12m / 15m/s = 0.8s，远投应有明显滞空力度感");

            var fallback = InkBombTrajectoryUtility.ComputeArcParams(Vector3.zero, new Vector3(15f, 0f, 0f), null);
            Assert.AreEqual(1f, fallback.FlightDuration, 0.001f, "无配置时按默认速度 15m/s 兜底");

            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void SubWeaponThrowTrajectory_Component_GeneratesMatchingPoints()
        {
            var go = new GameObject("TestTrajectoryHost");
            var trajectory = go.AddComponent<SubWeaponThrowTrajectory>();

            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombMaxThrowDistance = 8f;
            cfg.bombArcHeight = 2.4f;
            cfg.bombSplatRadius = 2.7f;

            Vector3 start = new Vector3(0f, 1f, 0f);
            Vector3 target = new Vector3(6f, 0f, 0f);

            trajectory.UpdateTrajectory(start, target, cfg, true, Team.TeamA);

            Assert.IsTrue(trajectory.IsShowing);
            Assert.AreEqual(6f, trajectory.CurrentLandingPosition.x, 0.05f);

            var line = go.GetComponentInChildren<LineRenderer>();
            Assert.IsNotNull(line);
            Assert.AreEqual(64, line.positionCount);

            trajectory.Hide();
            Assert.IsFalse(trajectory.IsShowing);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(cfg);
        }

        [Test]
        public void GroundAimReticle_CreatesAndUpdatesWithoutExceptions()
        {
            var go = new GameObject("TestGroundReticle");
            var reticle = go.AddComponent<GroundAimReticle>();

            var playerGo = new GameObject("TestPlayer");
            var motor = playerGo.AddComponent<InkCharacterMotor>();
            var health = playerGo.AddComponent<CombatantHealth>();
            var weapon = playerGo.AddComponent<WeaponController>();

            Vector3 aimPos = new Vector3(4f, 0f, 5f);
            reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);

            // 根节点贴地且与瞄准点坐标对齐
            Assert.AreEqual(aimPos.x, reticle.transform.position.x, 0.001f);
            Assert.AreEqual(aimPos.z, reticle.transform.position.z, 0.001f);
            Assert.AreEqual(0.035f, reticle.transform.position.y, 0.001f);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(playerGo);
        }

        [Test]
        public void WeaponReticleUI_CreatesAndInitializesAllContainers()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var reticleGo = new GameObject("TestReticleUI", typeof(RectTransform));
            reticleGo.transform.SetParent(canvasGo.transform, false);

            var reticleUI = reticleGo.AddComponent<P1.UI.WeaponReticleUI>();
            Assert.IsNotNull(reticleUI);
            reticleUI.EnsureInitialized();

            // 检查三个武器形态的子容器均已创建
            var shooter = reticleGo.transform.Find("ShooterReticle");
            var roller = reticleGo.transform.Find("RollerReticle");
            var charger = reticleGo.transform.Find("ChargerReticle");
            var subAim = reticleGo.transform.Find("SubAimBadge");

            Assert.IsNotNull(shooter, "ShooterReticle container should exist");
            Assert.IsNotNull(roller, "RollerReticle container should exist");
            Assert.IsNotNull(charger, "ChargerReticle container should exist");
            Assert.IsNotNull(subAim, "SubAimBadge container should exist");

            Object.DestroyImmediate(canvasGo);
        }

        [Test]
        public void TopDownCameraFollow_SetSubAimActive_ModifiesLookAheadState()
        {
            var camGo = new GameObject("TestCam");
            var cam = camGo.AddComponent<Camera>();
            var follow = camGo.AddComponent<TopDownCameraFollow>();

            Assert.IsFalse(follow.IsSubAimActive);
            follow.SetSubAimActive(true);
            Assert.IsTrue(follow.IsSubAimActive);

            follow.SetSubAimActive(false);
            Assert.IsFalse(follow.IsSubAimActive);

            Object.DestroyImmediate(camGo);
        }
    }
}
