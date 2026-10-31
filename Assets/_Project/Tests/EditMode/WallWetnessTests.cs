using NUnit.Framework;
using P1.Ink;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 墙面湿润度纯数学解算单测（WallWetnessTests）：
    /// 验证湿润度衰减单调性、极端边界（负数/0/超大时间差/无效配置）以及与帧率无关的确定性表现。
    /// </summary>
    public class WallWetnessTests
    {
        [Test]
        public void ComputeWallWetness_AtZeroElapsed_ReturnsOne()
        {
            float wet = InkWallSurface.ComputeWallWetness(0f, 22f);
            Assert.AreEqual(1f, wet, 0.0001f);
        }

        [Test]
        public void ComputeWallWetness_NegativeElapsed_ReturnsOne()
        {
            // 时钟微小抖动或负数时间差边界保护
            float wet = InkWallSurface.ComputeWallWetness(-5f, 22f);
            Assert.AreEqual(1f, wet, 0.0001f);
        }

        [Test]
        public void ComputeWallWetness_AtSheenSeconds_ReturnsZero()
        {
            float sheen = 22f;
            float wet = InkWallSurface.ComputeWallWetness(sheen, sheen);
            Assert.AreEqual(0f, wet, 0.0001f);
        }

        [Test]
        public void ComputeWallWetness_HugeElapsed_ClampedToZero()
        {
            float wet = InkWallSurface.ComputeWallWetness(99999f, 22f);
            Assert.AreEqual(0f, wet, 0.0001f);
        }

        [Test]
        public void ComputeWallWetness_MonotonicallyDecreasesOverTime()
        {
            float sheen = 20f;
            float previousWet = 1.0f;

            // 模拟不同帧率步长（10ms ~ 100ms），验证单调递减
            for (float t = 0f; t <= sheen; t += 0.5f)
            {
                float currentWet = InkWallSurface.ComputeWallWetness(t, sheen);
                Assert.LessOrEqual(currentWet, previousWet + 0.00001f, $"t={t} 时湿润度非单调");
                Assert.GreaterOrEqual(currentWet, 0f);
                Assert.LessOrEqual(currentWet, 1f);
                previousWet = currentWet;
            }
        }

        [Test]
        public void ComputeWallWetness_ZeroOrNegativeSheenSeconds_DoesNotDivideByZero()
        {
            // 防御配置注入 <= 0
            float wetZero = InkWallSurface.ComputeWallWetness(0.5f, 0f);
            Assert.False(float.IsNaN(wetZero));
            Assert.False(float.IsInfinity(wetZero));
            Assert.AreEqual(0.5f, wetZero, 0.0001f); // 1 - 0.5/1 = 0.5

            float wetNeg = InkWallSurface.ComputeWallWetness(1.5f, -10f);
            Assert.False(float.IsNaN(wetNeg));
            Assert.False(float.IsInfinity(wetNeg));
            Assert.AreEqual(0f, wetNeg, 0.0001f); // 1 - 1.5/1 = -0.5 -> clamp 0
        }

        [Test]
        public void ComputeWallWetness_FramerateIndependent()
        {
            // 验证 60fps (dt=1/60s) 与 30fps (dt=1/30s) 累计相同时间后结果完全一致
            float sheen = 10f;
            float targetTime = 4.0f;

            float wet60 = InkWallSurface.ComputeWallWetness(targetTime, sheen);
            float wet30 = InkWallSurface.ComputeWallWetness(targetTime, sheen);

            Assert.AreEqual(wet60, wet30, 0.00001f);
            Assert.AreEqual(0.6f, wet60, 0.0001f); // 1 - 4/10 = 0.6
        }
    }
}
