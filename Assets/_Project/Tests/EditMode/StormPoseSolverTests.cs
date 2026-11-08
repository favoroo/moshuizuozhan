using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P41 Ink Tempest 投掷表演的三段权重时间窗（纯逻辑，EditMode 可断言）。
    ///
    /// 回归背景：Storm 释放只有 0.35s 动作锁，没有投掷姿势 —— 观感是「僵在原地把球扔出去」。
    /// 本测试锁死 INKWAVE _poseThrow 的 cock→whip→follow-through 分段时序，
    /// 防止调参把某一段压没（例如鞭甩早于引臂读成「甩空」，收势晚于锁结束读成「僵住」）。
    /// </summary>
    public class StormPoseSolverTests
    {
        [Test]
        public void Throw_StartsWithCock_ThenWhip_ThenFollowThrough()
        {
            // 起手：引臂满权重（In0=-1 保证第 1 帧就是后仰蓄势），鞭甩/随挥还没开始
            StormThrowPerformance early = StormPoseSolver.Sample(0.04f);
            Assert.Greater(early.CockW, 0.9f, "起手立即进入引臂后仰");
            Assert.AreEqual(0f, early.WhipW, 0.01f, "起手鞭甩还没开始");
            Assert.AreEqual(0f, early.FollowW, 0.01f, "起手随挥还没开始");

            // 中段鞭甩：引臂退场，鞭甩满权重（发生器 t=0 已离手，鞭甩是身体跟上）
            StormThrowPerformance whip = StormPoseSolver.Sample(0.16f);
            Assert.Greater(whip.WhipW, 0.9f, "0.16s 应处于鞭甩满权重");
            Assert.Less(whip.CockW, 0.3f, "鞭甩段引臂应已基本退场");
            Assert.AreEqual(0f, whip.FollowW, 0.01f, "随挥还没开始");

            // 尾段随挥：鞭甩退场，随挥保持
            StormThrowPerformance follow = StormPoseSolver.Sample(0.36f);
            Assert.Greater(follow.FollowW, 0.9f, "0.36s 应处于随挥满权重");
            Assert.AreEqual(0f, follow.WhipW, 0.01f, "随挥段鞭甩应已退场");
            Assert.AreEqual(0f, follow.CockW, 0.01f, "随挥段引臂应已退场");
        }

        [Test]
        public void Throw_WhipPeaksInsideWhipWindow()
        {
            // 鞭甩窗 0.10~0.28s：0.12 处于淡入、0.16 满权重、0.30 已淡出 —— 出手爆发感有明确峰
            StormThrowPerformance ramp = StormPoseSolver.Sample(0.12f);
            Assert.That(ramp.WhipW, Is.InRange(0.3f, 0.8f), "0.12s 鞭甩应在淡入中");

            StormThrowPerformance peak = StormPoseSolver.Sample(0.16f);
            Assert.AreEqual(1f, peak.WhipW, 0.01f, "0.14~0.20s 鞭甩满权重");

            StormThrowPerformance done = StormPoseSolver.Sample(0.30f);
            Assert.AreEqual(0f, done.WhipW, 0.01f, "0.28s 后鞭甩结束");
        }

        [Test]
        public void Throw_BlendFadesInFast_AndOutByTotalDuration()
        {
            Assert.AreEqual(0f, StormPoseSolver.Sample(0f).Blend01, 0.001f, "t=0 当帧包络为 0，姿态由权重接管");

            StormThrowPerformance mid = StormPoseSolver.Sample(0.20f);
            Assert.AreEqual(1f, mid.Blend01, 0.01f, "中段演出应完全接管姿态");

            StormThrowPerformance tail = StormPoseSolver.Sample(0.53f);
            Assert.Less(tail.Blend01, 0.5f, "尾段应已开始渐出（0.35s 锁已结束，移动恢复）");

            StormThrowPerformance end = StormPoseSolver.Sample(StormPoseSolver.TotalDuration + 0.01f);
            Assert.AreEqual(0f, end.Blend01, 0.001f, "总时长后交还步态层");
            Assert.AreEqual(0f, end.TotalWeight, 0.001f, "总时长后三段权重全部归零");
        }

        [Test]
        public void Throw_NegativeElapsed_IsClampedToStart()
        {
            StormThrowPerformance pose = StormPoseSolver.Sample(-0.5f);
            Assert.Greater(pose.CockW, 0.9f, "负时间按 0 处理：引臂满权重");
            Assert.AreEqual(0f, pose.WhipW, 0.01f);
        }

        [Test]
        public void Throw_WeightSumNeverFarExceedsOne()
        {
            // 相邻段过渡区允许短暂 >1（与 Slam 同构，调用方归一化），但不应翻倍。
            // 扫描到 0.53s 截止：最后 0.02s 三段窗与包络同时归零，是设计内的静默帧。
            for (float t = 0.01f; t < StormPoseSolver.TotalDuration - 0.02f; t += 0.01f)
            {
                StormThrowPerformance pose = StormPoseSolver.Sample(t);
                Assert.Greater(pose.TotalWeight, 0f, $"{t:0.00}s 应至少有一段在播");
                Assert.LessOrEqual(pose.TotalWeight, 1.6f, $"{t:0.00}s 权重总和不应远超 1");
            }
        }
    }
}
