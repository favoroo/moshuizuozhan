using NUnit.Framework;
using P1.Core;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 击杀奖励（把交火接进主循环）的纯函数口径：
    /// 默认值 / 配置覆盖 / 钳制 / 转移量。全链路（击杀 → 固定能量 + 损失转移 + 回墨）依赖
    /// MonoBehaviour 生命周期（OnKilled 订阅链、Awake 初始化），按项目惯例放
    /// PlayMode（KillRewardPlayTests）。
    /// </summary>
    public class KillRewardTests
    {
        [Test]
        public void ResolveKillBonusPoints_UnconfiguredFallsBackToSpecDefault()
        {
            Assert.AreEqual(70f, KillRewardMath.ResolveKillBonusPoints(-1f), 0.001f, "负值视为未配置");
            Assert.AreEqual(70f, KillRewardMath.ResolveKillBonusPoints(0f), 0.001f, "0 视为未配置");
            Assert.AreEqual(55f, KillRewardMath.ResolveKillBonusPoints(55f), 0.001f, "显式配置优先");
        }

        [Test]
        public void ResolveEnergyTransferFraction_ClampsAndRespectsExplicitZero()
        {
            Assert.AreEqual(0.5f, KillRewardMath.ResolveEnergyTransferFraction(-1f), 0.001f, "未配置回退规范默认");
            Assert.AreEqual(0f, KillRewardMath.ResolveEnergyTransferFraction(0f), 0.001f, "显式 0 = 关闭转移");
            Assert.AreEqual(1f, KillRewardMath.ResolveEnergyTransferFraction(1.5f), 0.001f, "钳制上限");
            Assert.AreEqual(0.25f, KillRewardMath.ResolveEnergyTransferFraction(0.25f), 0.001f, "合法配置原样透传");
        }

        [Test]
        public void ComputeEnergyTransfer_ScalesWithVictimPreDeathPoints()
        {
            Assert.AreEqual(50f, KillRewardMath.ComputeEnergyTransfer(100f, 0.5f), 0.001f);
            Assert.AreEqual(180f, KillRewardMath.ComputeEnergyTransfer(180f, 1f), 0.001f, "满能量阵亡全额转移");
            Assert.AreEqual(0f, KillRewardMath.ComputeEnergyTransfer(100f, 0f), 0.001f, "比例 0 = 不转移");
            Assert.AreEqual(0f, KillRewardMath.ComputeEnergyTransfer(-5f, 0.5f), 0.001f, "负能量按 0 处理");
        }

        [Test]
        public void ResolveKillInkRefill_UnconfiguredFallsBackToSpecDefault()
        {
            Assert.AreEqual(25f, KillRewardMath.ResolveKillInkRefill(-1f), 0.001f);
            Assert.AreEqual(25f, KillRewardMath.ResolveKillInkRefill(0f), 0.001f);
            Assert.AreEqual(30f, KillRewardMath.ResolveKillInkRefill(30f), 0.001f);
        }
    }
}
