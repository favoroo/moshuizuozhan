using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P40 双枪翻滚姿态求解层：转角快出缓停、蜷身窗、转轴方向、跳弧与压缩。纯函数，不依赖场景。
    /// </summary>
    public class DodgeRollPoseSolverTests
    {
        [Test]
        public void Sample_StartAndEnd_AreIdentityRotation()
        {
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;

            DodgeRollPose start = DodgeRollPoseSolver.Sample(0f, 1f, 1f, p);
            Assert.AreEqual(0f, start.TumbleRadians, 0.0001f, "进度 0 转角为 0");
            Assert.AreEqual(0f, start.Tuck01, 0.0001f, "进度 0 未团身");
            Assert.AreEqual(0f, start.LiftMeters, 0.0001f, "进度 0 贴地");

            DodgeRollPose end = DodgeRollPoseSolver.Sample(1f, 1f, 1f, p);
            Assert.AreEqual(Mathf.Deg2Rad * 360f, end.TumbleRadians, 0.001f, "进度 1 恰好转满一圈（回到单位姿态）");
            Assert.AreEqual(0f, end.Tuck01, 0.0001f, "进度 1 展开完毕，直接衔接架枪");
            Assert.AreEqual(0f, end.LiftMeters, 0.0001f, "进度 1 落回地面");
            Assert.AreEqual(1f, end.SquashMul, 0.0001f, "进度 1 无压缩残留");
        }

        [Test]
        public void Sample_Tumble_IsMonotonicAndFastOut()
        {
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;
            float prev = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float u = i / 20f;
                DodgeRollPose pose = DodgeRollPoseSolver.Sample(u, 0f, 1f, p);
                Assert.GreaterOrEqual(pose.TumbleRadians, prev - 0.0001f, $"转角随进度单调不减（u={u:0.00}）");
                prev = pose.TumbleRadians;
            }

            // 快出缓停：前半程转角超过半圈（INKWAVE 指数 2.4 → u=0.5 时 1-0.5^2.4 ≈ 0.81）
            DodgeRollPose mid = DodgeRollPoseSolver.Sample(0.5f, 0f, 1f, p);
            Assert.Greater(mid.TumbleRadians, Mathf.Deg2Rad * 180f, "u=0.5 转角应超过 180°（爆发在前段）");
        }

        [Test]
        public void Sample_ForwardRoll_AxisIsLocalX()
        {
            DodgeRollPose pose = DodgeRollPoseSolver.Sample(0.3f, 0f, 1f, DodgeRollPoseParams.Default);
            Assert.AreEqual(1f, pose.TumbleAxisX, 0.0001f, "正前滚转轴 = 局部 +X（up × forward）");
            Assert.AreEqual(0f, pose.TumbleAxisZ, 0.0001f, "正前滚转轴无 Z 分量");
        }

        [Test]
        public void Sample_SideRoll_AxisIsNegatedLocalZ()
        {
            DodgeRollPose pose = DodgeRollPoseSolver.Sample(0.3f, 1f, 0f, DodgeRollPoseParams.Default);
            Assert.AreEqual(0f, pose.TumbleAxisX, 0.0001f, "右侧滚转轴无 X 分量");
            Assert.AreEqual(-1f, pose.TumbleAxisZ, 0.0001f, "右侧滚转轴 = 局部 -Z（up × right，与 INKWAVE tumbleZ=-dodgeX 同构）");
        }

        [Test]
        public void Sample_DiagonalRoll_AxisInterpolates()
        {
            DodgeRollPose pose = DodgeRollPoseSolver.Sample(0.3f, 1f, 1f, DodgeRollPoseParams.Default);
            // 滚向 (1,0,1) 归一化后 axis = (dir.z, 0, -dir.x) = (0.707, 0, -0.707)
            Assert.AreEqual(0.7071f, pose.TumbleAxisX, 0.001f);
            Assert.AreEqual(-0.7071f, pose.TumbleAxisZ, 0.001f);
        }

        [Test]
        public void Sample_Tuck_RisesEarlyFadesBeforeEnd()
        {
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;

            DodgeRollPose early = DodgeRollPoseSolver.Sample(0.1f, 0f, 1f, p);
            Assert.AreEqual(1f, early.Tuck01, 0.02f, "0.1s 内必须完全团身（赶在身体转横前缩回半径）");

            DodgeRollPose mid = DodgeRollPoseSolver.Sample(0.5f, 0f, 1f, p);
            Assert.AreEqual(1f, mid.Tuck01, 0.0001f, "中段保持团身");

            DodgeRollPose late = DodgeRollPoseSolver.Sample(0.8f, 0f, 1f, p);
            Assert.That(late.Tuck01, Is.InRange(0f, 1f), "0.62s 起展开，0.8s 处于展开中段");
            Assert.Less(late.Tuck01, mid.Tuck01, "展开段蜷身权重必须下降");
        }

        [Test]
        public void Sample_Lift_PeaksMidRoll_ReturnsToGround()
        {
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;
            DodgeRollPose quarter = DodgeRollPoseSolver.Sample(0.25f, 0f, 1f, p);
            DodgeRollPose half = DodgeRollPoseSolver.Sample(0.5f, 0f, 1f, p);
            DodgeRollPose threeQuarter = DodgeRollPoseSolver.Sample(0.75f, 0f, 1f, p);

            Assert.That(half.LiftMeters, Is.EqualTo(p.LiftMeters).Within(0.0001f), "中段抬升达到峰值");
            Assert.Less(quarter.LiftMeters, half.LiftMeters, "起跳段低于峰值");
            Assert.Less(threeQuarter.LiftMeters, half.LiftMeters, "落地段低于峰值");
            Assert.LessOrEqual(half.LiftMeters, 0.6f, "抬升受配置上限钳制");
        }

        [Test]
        public void Sample_Squash_CompressesOnlyWhileTucked()
        {
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;
            DodgeRollPose tucked = DodgeRollPoseSolver.Sample(0.3f, 0f, 1f, p);
            Assert.AreEqual(1f - p.SquashAmount, tucked.SquashMul, 0.001f, "团身时按配置量压缩");

            DodgeRollPose rolled = DodgeRollPoseSolver.Sample(1f, 0f, 1f, p);
            Assert.AreEqual(1f, rolled.SquashMul, 0.0001f, "滚完无压缩残留");
        }

        [Test]
        public void Sample_DegenerateDirection_FallsBackToForward()
        {
            DodgeRollPose pose = DodgeRollPoseSolver.Sample(0.3f, 0f, 0f, DodgeRollPoseParams.Default);
            Assert.AreEqual(1f, pose.TumbleAxisX, 0.0001f, "零向量滚向回退为正前滚");
        }

        [Test]
        public void Sample_NegativeProgress_ReturnsNone()
        {
            DodgeRollPose pose = DodgeRollPoseSolver.Sample(-0.5f, 1f, 1f, DodgeRollPoseParams.Default);
            Assert.AreEqual(0f, pose.TumbleRadians, 0.0001f);
            Assert.AreEqual(0f, pose.Tuck01, 0.0001f);
        }

        [Test]
        public void AnimConfigDefaults_MatchSolverDefault()
        {
            // CharacterAnimConfigSO 的字段初始化器必须与 DodgeRollPoseParams.Default 一致，
            // 否则旧 asset（新字段缺省反序列化为 C# 默认值）与新建 asset 表现不一致。
            var cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            DodgeRollPoseParams p = DodgeRollPoseParams.Default;

            Assert.AreEqual(p.TumbleDegrees, cfg.dualiesDodgeTumbleDegrees, 0.0001f, "整圈转角默认 360°");
            Assert.AreEqual(p.TumbleEaseExponent, cfg.dualiesDodgeTumbleEase, 0.0001f, "快出缓停指数默认 2.4");
            Assert.AreEqual(p.LiftMeters, cfg.dualiesDodgeTumbleLift, 0.0001f, "中段跳弧默认 0.30m");
            Assert.AreEqual(p.SquashAmount, cfg.dualiesDodgeTuckSquash, 0.0001f, "蜷身压缩默认 0.07");
        }
    }
}
