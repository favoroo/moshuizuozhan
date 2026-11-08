using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 阵亡观战镜头纯数学验收（对标 INKWAVE <c>_spectate</c>）：
    /// 停留段看阵亡点、之后看击杀者、机位站在阵亡点一侧、漂移封顶、FOV 收窄。
    /// 运行时的遮挡/平滑由 TopDownCameraPlayTests 覆盖。
    /// </summary>
    [TestFixture]
    public class DeathCamMathTests
    {
        [Test]
        public void Focus_HoldsOnVictim_ThenSwitchesToKiller()
        {
            Vector3 victim = new Vector3(3f, 0f, -2f);
            Vector3 killer = new Vector3(-8f, 0f, 6f);

            Vector3 duringHold = DeathCamMath.ResolveFocusPoint(0.2f, victim, killer, true);
            Assert.AreEqual(victim.x, duringHold.x, 1e-4f);
            Assert.AreEqual(victim.y + DeathCamMath.LookHeight, duringHold.y, 1e-4f);
            Assert.AreEqual(victim.z, duringHold.z, 1e-4f, "停留段必须看自己的阵亡点（击杀爆墨）");

            Vector3 afterHold = DeathCamMath.ResolveFocusPoint(DeathCamMath.HoldSeconds + 0.01f, victim, killer, true);
            Assert.AreEqual(killer.x, afterHold.x, 1e-4f);
            Assert.AreEqual(killer.y + DeathCamMath.LookHeight, afterHold.y, 1e-4f);
            Assert.AreEqual(killer.z, afterHold.z, 1e-4f, "停留结束后必须转向击杀者");
        }

        [Test]
        public void Focus_WithoutKiller_StaysOnVictim()
        {
            Vector3 victim = new Vector3(0f, 0f, 4f);
            Vector3 staleKiller = new Vector3(20f, 0f, 20f);

            Vector3 focus = DeathCamMath.ResolveFocusPoint(3f, victim, staleKiller, false);
            Assert.AreEqual(victim.x, focus.x, 1e-4f);
            Assert.AreEqual(victim.z, focus.z, 1e-4f, "环境死亡（无凶手）时不能把镜头拉向无关位置");
        }

        [Test]
        public void CameraPosition_SitsOnVictimSideOfKiller_AtHoldThenWatchRadius()
        {
            Vector3 victim = new Vector3(0f, 0f, 0f);
            Vector3 killer = new Vector3(0f, 0f, 10f);

            // 停留段：焦点在阵亡点正上方，方向退化 → 用死亡瞬间的机位方向兜底
            Vector3 holdFocus = DeathCamMath.ResolveFocusPoint(0f, victim, killer, true);
            Vector3 holdPos = DeathCamMath.ResolveCameraPosition(0f, victim, holdFocus, Vector3.back);
            float holdRadius = HorizontalDistance(holdPos, holdFocus);
            Assert.AreEqual(DeathCamMath.StartRadius, holdRadius, 1e-3f, "停留段机位半径应为 4.2m");
            Assert.AreEqual(holdFocus.y + DeathCamMath.Height, holdPos.y, 1e-4f);
            Assert.Less(holdPos.z, victim.z, "方向退化时必须沿用兜底方位，不能甩到 +Z");

            // 转向后：机位在「击杀者 → 阵亡点」连线的阵亡点一侧
            Vector3 watchFocus = DeathCamMath.ResolveFocusPoint(1f, victim, killer, true);
            Vector3 watchPos = DeathCamMath.ResolveCameraPosition(1f, victim, watchFocus, Vector3.back);
            float watchRadius = HorizontalDistance(watchPos, watchFocus);
            Assert.AreEqual(DeathCamMath.Radius, watchRadius, 1e-3f, "观战机位半径应为 5.5m");
            Assert.Less(watchPos.z, killer.z, "机位必须站在阵亡点一侧，不能绕到击杀者背后");
            Assert.AreEqual(watchFocus.y + DeathCamMath.Height, watchPos.y, 1e-4f);
        }

        [Test]
        public void CameraPosition_DriftStopsAtClampTime()
        {
            Vector3 victim = new Vector3(0f, 0f, 0f);
            Vector3 killer = new Vector3(0f, 0f, 10f);
            Vector3 focus = DeathCamMath.ResolveFocusPoint(1f, victim, killer, true);

            Vector3 atClamp = DeathCamMath.ResolveCameraPosition(
                DeathCamMath.DriftClampSeconds, victim, focus, Vector3.back);
            Vector3 afterClamp = DeathCamMath.ResolveCameraPosition(
                DeathCamMath.DriftClampSeconds + 12f, victim, focus, Vector3.back);

            Assert.AreEqual(atClamp.x, afterClamp.x, 1e-3f, "漂移应在 6s 封顶，长时间观战不再转圈");
            Assert.AreEqual(atClamp.z, afterClamp.z, 1e-3f);
        }

        [Test]
        public void FieldOfView_ZoomsInSixDegrees_AndKeepsClamp()
        {
            Assert.AreEqual(52f - DeathCamMath.FovZoomDegrees, DeathCamMath.ResolveFieldOfView(52f), 1e-4f);
            Assert.AreEqual(25f, DeathCamMath.ResolveFieldOfView(28f), 1e-4f, "低 FOV 配置不得跌破 25°");
            Assert.AreEqual(90f, DeathCamMath.ResolveFieldOfView(120f), 1e-4f, "高 FOV 配置不得突破 90°");
        }

        [Test]
        public void LookSmoothRate_IsFasterDuringHold()
        {
            Assert.AreEqual(DeathCamMath.HoldLookSmoothRate, DeathCamMath.ResolveLookSmoothRate(0.1f), 1e-4f);
            Assert.AreEqual(DeathCamMath.LookSmoothRate, DeathCamMath.ResolveLookSmoothRate(1f), 1e-4f);
            Assert.Greater(DeathCamMath.HoldLookSmoothRate, DeathCamMath.LookSmoothRate,
                "停留段要更快咬住阵亡点，转向击杀者后放慢");
        }

        [Test]
        public void ExpSmoothFactor_IsZeroAtZeroAndFrameRateIndependent()
        {
            Assert.AreEqual(0f, DeathCamMath.ExpSmoothFactor(3f, 0f), 1e-6f);

            float oneStep = DeathCamMath.ExpSmoothFactor(3f, 1f / 60f);
            float twoSteps = 1f - (1f - oneStep) * (1f - oneStep);
            float oneBigStep = DeathCamMath.ExpSmoothFactor(3f, 2f / 60f);
            Assert.AreEqual(oneBigStep, twoSteps, 1e-5f, "指数平滑必须与帧率无关");
            Assert.Greater(oneStep, 0f);
            Assert.Less(oneStep, 1f);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }
    }
}
