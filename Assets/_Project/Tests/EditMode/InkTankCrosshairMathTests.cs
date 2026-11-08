using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P42-E 准星旁墨水罐的纯逻辑验收：液面追值、空闲淡出、低墨有限闪烁、
    /// 空罐抖动、液面倾角与气泡进度。无场景依赖，EditMode 秒级运行。
    /// </summary>
    public class InkTankCrosshairMathTests
    {
        [Test]
        public void Level_ChasesExponentiallyWithoutOvershoot()
        {
            // 半衰节奏：一帧逼近剩余差值的 (1-exp(-14·0.016)) ≈ 20%
            float oneFrame = InkTankCrosshairMath.StepLevel(1f, 0f, 0.016f);
            Assert.Less(oneFrame, 0.85f, "掉墨首帧液面只应走一小段（指数平滑而非瞬跳）");
            Assert.Greater(oneFrame, 0.6f, "但也不能慢到看不见在动");

            // 永不过冲、必然收敛
            float level = 0f;
            for (int i = 0; i < 600; i++)
            {
                level = InkTankCrosshairMath.StepLevel(level, 1f, 0.016f);
                Assert.GreaterOrEqual(level, 0f);
                Assert.LessOrEqual(level, 1f + 0.0001f);
            }
            Assert.AreEqual(1f, level, 0.0001f, "600 帧（10s）内必须收敛到满位");

            // dt=0（暂停/首帧）保持原值
            Assert.AreEqual(0.5f, InkTankCrosshairMath.StepLevel(0.5f, 1f, 0f), 0.0001f);
        }

        [Test]
        public void Idle_FadesOnlyAfterSustainedFullLevel()
        {
            float idle = 0f;
            for (int i = 0; i < 100; i++)
            {
                idle = InkTankCrosshairMath.StepIdle(idle, true, 0.016f);
            }
            Assert.GreaterOrEqual(idle, InkTankCrosshairMath.IdleFadeSeconds);
            Assert.IsTrue(InkTankCrosshairMath.ShouldFade(idle), "满罐 1.6s 后应淡出");

            // 任何消耗 / 低墨立刻打断计时
            Assert.AreEqual(0f, InkTankCrosshairMath.StepIdle(idle, false, 0.016f), 0.0001f);
            Assert.IsFalse(InkTankCrosshairMath.ShouldFade(
                InkTankCrosshairMath.StepIdle(idle, false, 0.016f)));
        }

        [Test]
        public void LowBlink_HardCutFiniteTimesThenSteadyRed()
        {
            float total = InkTankCrosshairMath.LowBlinkCount * InkTankCrosshairMath.LowBlinkPeriodSeconds;

            // 窗口内：半周期硬切（0 = 墨框 / 1 = 红框）
            Assert.AreEqual(0f, InkTankCrosshairMath.LowBlinkWeight(0f), 0.0001f, "起始保持墨框半周期");
            Assert.AreEqual(1f, InkTankCrosshairMath.LowBlinkWeight(0.16f), 0.05f, "半周期后翻红");
            Assert.AreEqual(0f, InkTankCrosshairMath.LowBlinkWeight(0.33f), 0.05f, "第二周期回到墨框");

            // 窗口外：恒为红框（稳定警示，不循环闪烁）
            Assert.AreEqual(1f, InkTankCrosshairMath.LowBlinkWeight(total), 0.0001f);
            Assert.AreEqual(1f, InkTankCrosshairMath.LowBlinkWeight(total + 30f), 0.0001f);
        }

        [Test]
        public void EmptyShake_DecaysAndSettles()
        {
            Assert.AreEqual(0f, InkTankCrosshairMath.EmptyShakeOffset(0f), 0.0001f, "抖动从零开始起摆");
            Assert.AreEqual(0f, InkTankCrosshairMath.EmptyShakeOffset(-1f), 0.0001f);

            bool sawNonZero = false;
            float previousMagnitude = float.MaxValue;
            for (float age = 0.02f; age < InkTankCrosshairMath.EmptyShakeSeconds; age += 0.02f)
            {
                float offset = InkTankCrosshairMath.EmptyShakeOffset(age);
                if (Mathf.Abs(offset) > 0.01f) sawNonZero = true;
                Assert.LessOrEqual(Mathf.Abs(offset), InkTankCrosshairMath.EmptyShakeDegrees + 0.0001f,
                    "摆幅不得超过起始幅度");
                previousMagnitude = Mathf.Abs(offset);
            }
            Assert.IsTrue(sawNonZero, "抖动窗口内应有实际摆动");

            Assert.AreEqual(0f, InkTankCrosshairMath.EmptyShakeOffset(InkTankCrosshairMath.EmptyShakeSeconds), 0.0001f);
            Assert.AreEqual(0f, InkTankCrosshairMath.EmptyShakeOffset(InkTankCrosshairMath.EmptyShakeSeconds + 10f), 0.0001f);
        }

        [Test]
        public void Tilt_FollowsLateralSpeedWithClampAndInvertedSign()
        {
            Assert.AreEqual(0f, InkTankCrosshairMath.TiltDegrees(0f), 0.0001f);
            Assert.Less(InkTankCrosshairMath.TiltDegrees(2f), 0f, "向右移动液面应朝反方向翘起");
            Assert.Greater(InkTankCrosshairMath.TiltDegrees(-2f), 0f);

            Assert.AreEqual(-InkTankCrosshairMath.TiltMaxDegrees,
                InkTankCrosshairMath.TiltDegrees(100f), 0.0001f, "高速时钳在上限");
            Assert.AreEqual(InkTankCrosshairMath.TiltMaxDegrees,
                InkTankCrosshairMath.TiltDegrees(-100f), 0.0001f);
        }

        [Test]
        public void Bubbles_RiseProgressClampsToOne()
        {
            Assert.AreEqual(0f, InkTankCrosshairMath.BubbleProgress(0f), 0.0001f);
            Assert.AreEqual(0.45f, InkTankCrosshairMath.BubbleProgress(0.5f), 0.02f);
            Assert.AreEqual(1f, InkTankCrosshairMath.BubbleProgress(99f), 0.0001f, "寿命尽头钳在罐顶");
        }
    }
}
