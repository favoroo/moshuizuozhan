using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P41 开赛扫场镜头纯数学验证：
    /// 1. t=1 必须精确落在跟随机位（否则切回跟随相机时玩家会看到一次跳变）；
    /// 2. 扫场段确实在敌方半场高空且横向划过（不是原地不动的假镜头）；
    /// 3. 俯冲段从高空一路收进玩家身后，且始终位于最终机位上方（不会从地板下钻出来）；
    /// 4. NaN / 越界输入一律安全钳制。
    /// </summary>
    public class MatchIntroCameraMathTests
    {
        // 货柜码头 TeamA 出生点（z=-23.5，面向 +Z）：跟随机位在玩家身后 4.5m、水平看向 +Z。
        private static MatchIntroCameraSetup MakePlayboxSetup()
        {
            return new MatchIntroCameraSetup
            {
                ArenaCenter = Vector3.zero,
                PlayerFeet = new Vector3(-2.4f, 1.27f, -23.5f),
                PlayerYawDegrees = 0f,
                FinalCameraPosition = new Vector3(-2.4f, 3.19f, -27.98f),
                FinalLookTarget = new Vector3(-2.4f, 3.19f, -17.98f),
                BaseFov = 52f,
                OverviewFov = 64f,
                OverviewHeight = MatchIntroCameraMath.DefaultOverviewHeight,
                OverviewBackDistance = MatchIntroCameraMath.DefaultOverviewBackDistance,
                OverviewSideOffset = MatchIntroCameraMath.DefaultOverviewSideOffset,
                SweepDegrees = MatchIntroCameraMath.DefaultSweepDegrees,
                DiveStart01 = MatchIntroCameraMath.DefaultDiveStart01,
                ArcLift = MatchIntroCameraMath.DefaultArcLift
            };
        }

        [Test]
        public void Evaluate_AtEndLandsExactlyOnFollowPose()
        {
            MatchIntroCameraSetup setup = MakePlayboxSetup();
            MatchIntroCameraPose pose = MatchIntroCameraMath.Evaluate(1f, setup);

            Assert.AreEqual(setup.FinalCameraPosition.x, pose.Position.x, 1e-4f, "终点位置 X 必须精确等于跟随机位");
            Assert.AreEqual(setup.FinalCameraPosition.y, pose.Position.y, 1e-4f, "终点位置 Y 必须精确等于跟随机位");
            Assert.AreEqual(setup.FinalCameraPosition.z, pose.Position.z, 1e-4f, "终点位置 Z 必须精确等于跟随机位");
            Assert.AreEqual(setup.FinalLookTarget.x, pose.LookTarget.x, 1e-4f);
            Assert.AreEqual(setup.FinalLookTarget.y, pose.LookTarget.y, 1e-4f);
            Assert.AreEqual(setup.FinalLookTarget.z, pose.LookTarget.z, 1e-4f);
            Assert.AreEqual(setup.BaseFov, pose.Fov, 1e-4f, "终点 FOV 必须回落基准值");
        }

        [Test]
        public void Evaluate_AtStartIsHighOverEnemyHalf_AndSweepMovesSideways()
        {
            MatchIntroCameraSetup setup = MakePlayboxSetup();

            MatchIntroCameraPose start = MatchIntroCameraMath.Evaluate(0f, setup);
            Assert.Greater(start.Position.y, setup.FinalCameraPosition.y + 10f, "开场必须明显高于跟随机位");
            Assert.Less(start.Position.y, setup.OverviewHeight + 1f, "开场高度不应超过配置的高空机位高度");
            Assert.Greater(start.Position.z, setup.PlayerFeet.z + 10f, "扫场机位必须架在敌方半场（玩家面向 +Z 的前方）");
            Assert.AreEqual(setup.OverviewFov, start.Fov, 1e-4f, "扫场段用广角 FOV");

            MatchIntroCameraPose diveStart = MatchIntroCameraMath.Evaluate(setup.DiveStart01, setup);
            float lateralStart = start.Position.x - setup.ArenaCenter.x;
            float lateralEnd = diveStart.Position.x - setup.ArenaCenter.x;
            Assert.Greater(Mathf.Abs(lateralEnd - lateralStart), 5f, "扫场段必须横向划过至少 5m，否则读不出「扫一遍场地」");
            Assert.Less(diveStart.Position.y, start.Position.y, "扫场段应缓慢下降，不是纯粹的水平平移");
            Assert.Greater(diveStart.Position.y, setup.FinalCameraPosition.y + 8f, "俯冲开始前仍在高空");
        }

        [Test]
        public void Evaluate_DiveDescendsMonotonically_AndStaysAboveFinalPose()
        {
            MatchIntroCameraSetup setup = MakePlayboxSetup();
            float previousY = float.MaxValue;

            // 俯冲段（含中间采样点）：高度单调下降，且不会掉到最终机位以下
            for (int i = 0; i <= 20; i++)
            {
                float t = Mathf.Lerp(setup.DiveStart01, 1f, i / 20f);
                MatchIntroCameraPose pose = MatchIntroCameraMath.Evaluate(t, setup);
                Assert.LessOrEqual(pose.Position.y, previousY + 0.01f,
                    $"俯冲段高度必须单调不升（t={t:0.00}）");
                Assert.GreaterOrEqual(pose.Position.y, setup.FinalCameraPosition.y - 0.01f,
                    $"俯冲段不得低于最终机位（t={t:0.00}，否则镜头会钻到玩家脚下）");
                previousY = pose.Position.y;
            }
        }

        [Test]
        public void Evaluate_ClampsOutOfRangeInput_AndHandlesNaN()
        {
            MatchIntroCameraSetup setup = MakePlayboxSetup();

            MatchIntroCameraPose before = MatchIntroCameraMath.Evaluate(-3f, setup);
            MatchIntroCameraPose start = MatchIntroCameraMath.Evaluate(0f, setup);
            Assert.AreEqual(start.Position.x, before.Position.x, 1e-4f, "t<0 应钳到 0");

            MatchIntroCameraPose after = MatchIntroCameraMath.Evaluate(5f, setup);
            Assert.AreEqual(setup.FinalCameraPosition.z, after.Position.z, 1e-4f, "t>1 应钳到 1");

            MatchIntroCameraPose nan = MatchIntroCameraMath.Evaluate(float.NaN, setup);
            Assert.IsFalse(float.IsNaN(nan.Position.x + nan.Position.y + nan.Position.z), "NaN 输入不得产出 NaN 机位");
            Assert.AreEqual(setup.FinalCameraPosition.z, nan.Position.z, 1e-4f, "NaN 输入应安全回落到终点");
        }

        [Test]
        public void Evaluate_HandlesDegenerateSetup_WithoutNaN()
        {
            // 玩家与跟随机位重合（极端配置 / 测试替身）：注视点退化时也不能产出 NaN
            var degenerate = new MatchIntroCameraSetup
            {
                ArenaCenter = Vector3.zero,
                PlayerFeet = Vector3.zero,
                PlayerYawDegrees = 0f,
                FinalCameraPosition = Vector3.zero,
                FinalLookTarget = Vector3.zero,
                BaseFov = 52f,
                OverviewFov = 52f,
                OverviewHeight = 25f,
                OverviewBackDistance = 19f,
                OverviewSideOffset = 0f,
                SweepDegrees = 0f,
                DiveStart01 = 0.5f,
                ArcLift = 0f
            };

            for (int i = 0; i <= 10; i++)
            {
                MatchIntroCameraPose pose = MatchIntroCameraMath.Evaluate(i / 10f, degenerate);
                Assert.IsFalse(float.IsNaN(pose.Position.x + pose.Position.y + pose.Position.z));
                Assert.IsFalse(float.IsNaN(pose.LookTarget.x + pose.LookTarget.y + pose.LookTarget.z));
                Assert.IsFalse(float.IsNaN(pose.Fov));
            }

            Assert.AreEqual(Vector3.zero, degenerate.FinalCameraPosition, "退化 setup 不得被求值器修改");
        }

        [Test]
        public void QuadraticBezier_EndpointsAreExact()
        {
            Vector3 a = new Vector3(1f, 2f, 3f);
            Vector3 b = new Vector3(4f, -5f, 6f);
            Vector3 c = new Vector3(-7f, 8f, 9f);

            Assert.AreEqual(a, MatchIntroCameraMath.QuadraticBezier(a, b, c, 0f));
            Assert.AreEqual(c, MatchIntroCameraMath.QuadraticBezier(a, b, c, 1f));

            Vector3 mid = MatchIntroCameraMath.QuadraticBezier(a, b, c, 0.5f);
            Assert.AreEqual((a + 2f * b + c) * 0.25f, mid, "t=0.5 的二次贝塞尔应为 0.25a+0.5b+0.25c");
        }

        [Test]
        public void LerpPose_BlendsPositionLookAndFov()
        {
            var from = new MatchIntroCameraPose
            {
                Position = Vector3.zero,
                LookTarget = Vector3.forward,
                Fov = 60f
            };
            var to = new MatchIntroCameraPose
            {
                Position = Vector3.one * 10f,
                LookTarget = Vector3.forward * 20f,
                Fov = 50f
            };

            MatchIntroCameraPose end = MatchIntroCameraMath.LerpPose(from, to, 1f);
            Assert.AreEqual(to.Position, end.Position);
            Assert.AreEqual(to.Fov, end.Fov, 1e-4f);

            MatchIntroCameraPose mid = MatchIntroCameraMath.LerpPose(from, to, 0.5f);
            Assert.AreEqual(5f, mid.Position.x, 1e-4f);
            Assert.AreEqual(55f, mid.Fov, 1e-4f);
        }
    }
}
