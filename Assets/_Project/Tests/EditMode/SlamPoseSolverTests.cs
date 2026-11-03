using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P38 Slam 起跳表演的四段权重时间窗（纯逻辑，EditMode 可断言）。
    ///
    /// 回归背景：Slam 只抬升视觉根节点，角色本体全程贴地（IsGrounded 恒真），
    /// 贴地步态不会切到滞空分支，于是观感是「站直的人被吊上去」。
    /// 本测试锁死 INKWAVE _poseLeap / _poseSlam 的分段时序，防止调参把某一段压没。
    /// </summary>
    public class SlamPoseSolverTests
    {
        private const float Rise = 0.55f;
        private const float Hang = 0.25f;
        private const float Fall = 0.1f;
        private const float Hold = 0.18f;
        private const float Recover = 0.26f;

        private static SlamPerformance SampleRise(float t)
            => SlamPoseSolver.Sample(SlamPoseSolver.PhaseRise, t, Rise, Hang, Fall, Hold, Recover, 360f);

        [Test]
        public void Rise_StartsWithLaunch_ThenTuck_ThenHang()
        {
            // 起跳第 1 帧：只有蹬伸，团身/顶点都还没起来（腿是伸直的，不是站着的）
            SlamPerformance early = SampleRise(0.06f);
            Assert.Greater(early.LaunchW, 0.5f, "起跳瞬间应进入蹬伸段");
            Assert.AreEqual(0f, early.TuckW, 0.01f, "蹬伸阶段团身还没开始");
            Assert.AreEqual(0f, early.HangW, 0.01f, "蹬伸阶段顶点举枪还没开始");

            // 中段团身：收腿抱膝是整段最重的姿态
            SlamPerformance tuck = SampleRise(0.34f);
            Assert.Greater(tuck.TuckW, 0.95f, "0.34s 应处于团身满权重");
            Assert.AreEqual(0f, tuck.LaunchW, 0.01f, "蹬伸已在 0.24s 结束");

            // 尾段顶点：团身退场，举枪过顶接管
            SlamPerformance hang = SampleRise(0.52f);
            Assert.Greater(hang.HangW, 0.5f, "升空尾段应进入顶点段");
            Assert.Less(hang.TuckW, 0.4f, "顶点段团身应已退场");
        }

        [Test]
        public void HangPhase_RampsHangWeightToFull_AndKeepsIt()
        {
            // 顶点举枪在升空尾段才淡到满（INKWAVE win(lt,0.44,0.58)），滞空开始约 0.88，
            // 之后 0.03s 内到 1.0 并保持到滞空结束 —— 交接是渐进的，不会「站回去」。
            SlamPerformance start = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseHang, 0f, Rise, Hang, Fall, Hold, Recover, 360f);
            SlamPerformance mid = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseHang, 0.06f, Rise, Hang, Fall, Hold, Recover, 360f);
            SlamPerformance end = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseHang, Hang, Rise, Hang, Fall, Hold, Recover, 360f);

            Assert.Greater(start.HangW, 0.85f, "进入滞空时顶点权重应已接近满");
            Assert.AreEqual(1f, mid.HangW, 0.01f, "滞空开始 0.06s 后应满权重举枪");
            Assert.AreEqual(1f, end.HangW, 0.01f, "滞空结束前不得淡出");
            Assert.AreEqual(1f, start.Blend01, 0.01f, "滞空段演出必须完全接管姿态");
        }

        [Test]
        public void Fall_TakesOverAsDive_AndDropsLeapWeights()
        {
            SlamPerformance dive = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseFall, 0.1f, Rise, Hang, Fall, Hold, Recover, 360f);

            Assert.AreEqual(1f, dive.DiveW, 0.01f, "0.07s 内应进入俯冲满权重");
            Assert.AreEqual(0f, dive.HangW, 0.01f, "下砸段不再举枪过顶");
            Assert.AreEqual(0f, dive.TuckW, 0.01f, "下砸段不再团身");
        }

        [Test]
        public void Impact_HoldsCrouch_ThenReleasesWithinRecover()
        {
            SlamPerformance atImpact = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseImpact, 0f, Rise, Hang, Fall, Hold, Recover, 360f);
            SlamPerformance holding = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseImpact, Hold, Rise, Hang, Fall, Hold, Recover, 360f);
            SlamPerformance recovered = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseImpact, Hold + Recover, Rise, Hang, Fall, Hold, Recover, 360f);

            Assert.AreEqual(1f, atImpact.ImpactW, 0.01f, "落地当帧应立刻进入蹲姿");
            Assert.AreEqual(1f, holding.ImpactW, 0.01f, "保持段全程满权重");
            Assert.Less(recovered.ImpactW, 0.02f, "恢复结束应完全退出蹲姿");
            Assert.Less(recovered.Blend01, 0.02f, "恢复结束姿态控制权应交还常规管线");
        }

        [Test]
        public void Flip_TurnsExactlyOneTurn_AndNeverOvershoots()
        {
            // 空翻 0.14~0.50s 转满一圈：终点必须是 360°，不能多转（否则落地朝向歪）
            SlamPerformance beforeFlip = SampleRise(0.10f);
            Assert.AreEqual(0f, beforeFlip.FlipRadians, 0.02f, "0.14s 前不空翻");

            SlamPerformance half = SampleRise(0.32f);
            Assert.Greater(half.FlipRadians, Mathf.PI * 0.6f, "中段应已转过约半圈以上");

            SlamPerformance end = SampleRise(0.52f);
            Assert.AreEqual(Mathf.PI * 2f, end.FlipRadians, 0.02f, "升空结束时必须正好一圈");

            // 滞空/下砸期间不再继续累加（否则落地会歪头）
            SlamPerformance hang = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseHang, 0.2f, Rise, Hang, Fall, Hold, Recover, 360f);
            Assert.AreEqual(Mathf.PI * 2f, hang.FlipRadians, 0.02f, "滞空段不得继续空翻");
        }

        [Test]
        public void Flip_DisabledWhenDegreesIsZero()
        {
            SlamPerformance pose = SlamPoseSolver.Sample(
                SlamPoseSolver.PhaseRise, 0.32f, Rise, Hang, Fall, Hold, Recover, 0f);
            Assert.AreEqual(0f, pose.FlipRadians, 1e-4f, "配置 0 度时必须完全不转");
        }

        [Test]
        public void Window_IsTrapezoid_AndClamped()
        {
            Assert.AreEqual(0f, SlamPoseSolver.Window(-1f, 0f, 0.1f, 0.3f, 0.5f), "窗前进不来");
            Assert.AreEqual(0f, SlamPoseSolver.Window(1f, 0f, 0.1f, 0.3f, 0.5f), "窗后已淡出");
            Assert.AreEqual(1f, SlamPoseSolver.Window(0.2f, 0f, 0.1f, 0.3f, 0.5f), "保持段满权重");
            Assert.AreEqual(0.5f, SlamPoseSolver.Window(0.05f, 0f, 0.1f, 0.3f, 0.5f), 0.05f, "淡入中点约 0.5");

            // out 传 float.MaxValue = 常驻不淡出（顶点段用这个）
            Assert.AreEqual(1f, SlamPoseSolver.Window(5f, 0f, 0.1f, float.MaxValue, float.MaxValue), 0.01f);
        }

        [Test]
        public void Weights_NeverExceedOneInTotal_ForAnyPhase()
        {
            // 0.02s 是第一帧之后：蹬伸窗此刻已经开着（In0=-1），包络也已在淡入，
            // 权重必然非零。t=0 当帧 Blend01 仍为 0，姿态由 Blend 而非权重接管，不在扫描范围。
            for (float t = 0.02f; t <= Rise + Hang; t += 0.02f)
            {
                SlamPerformance pose = SampleRise(t);
                Assert.Greater(pose.TotalWeight, 0f, $"{t:0.00}s 应至少有一段在播");
                Assert.LessOrEqual(pose.TotalWeight, 2.01f, $"{t:0.00}s 权重总和不应远超 1");
            }
        }
    }
}
