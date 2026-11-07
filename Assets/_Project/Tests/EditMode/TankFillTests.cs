using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 储液罐液面动画纯数学回归（INKWAVE character.js _updateTank L3339-3351 / damp L1272 配方移植）：
    /// 平滑收敛、瞬变吸附、扣墨冲量方向与钳制、晃动目标映射、倾斜/挤压钳制、低墨迟滞与呼吸幅度。
    /// </summary>
    public class TankFillTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void ExpDamp_ConvergesTowardTarget_FrameRateIndependent()
        {
            // 同一总时长下，大步长与小步长的终值偏差应在指数平滑的离散误差内
            float big = TankFillMath.ExpDamp(0f, 1f, 8f, 0.25f);
            float small = 0f;
            for (int i = 0; i < 25; i++)
            {
                small = TankFillMath.ExpDamp(small, 1f, 8f, 0.01f);
            }
            Assert.AreEqual(big, small, 0.02f, "指数平滑必须帧率无关（大步长≈小步长累加）");

            // rate=8、125ms 后约走 63%（1−e^-1）
            float halfLife = TankFillMath.ExpDamp(0f, 1f, 8f, 0.125f);
            Assert.AreEqual(1f - Mathf.Exp(-1f), halfLife, 0.01f, "125ms 应收敛约 63%");
        }

        [Test]
        public void ExpDamp_InvalidInputs_NoOp()
        {
            Assert.AreEqual(0.42f, TankFillMath.ExpDamp(0.42f, 1f, 0f, 0.016f), Eps, "rate=0 应原值返回");
            Assert.AreEqual(0.42f, TankFillMath.ExpDamp(0.42f, 1f, 8f, 0f), Eps, "dt=0 应原值返回");
        }

        [Test]
        public void NeedsSnap_OnlyForRespawnSizedJumps()
        {
            Assert.IsTrue(TankFillMath.NeedsSnap(0.1f, 1f), "复活式跳变（0.1→1）必须吸附");
            Assert.IsFalse(TankFillMath.NeedsSnap(0.65f, 0.55f), "正常扣墨（0.1 内）不吸附");
            Assert.IsFalse(TankFillMath.NeedsSnap(0.5f, 0.5f), "零偏差不吸附");
        }

        [Test]
        public void ConsumeKick_OnlyOnInkDrop_ProportionalToDrop()
        {
            const float gain = 100f; // SO 默认 tankConsumeKickGain
            Assert.AreEqual(0f, TankFillMath.ConsumeKick(0.05f, gain), Eps, "回墨（delta>0）不给冲量");
            Assert.AreEqual(0f, TankFillMath.ConsumeKick(0f, gain), Eps, "零消耗不给冲量");
            Assert.AreEqual(-20f, TankFillMath.ConsumeKick(-0.2f, gain), Eps, "扣 20% 墨 → −20 冲量（bob 弹簧峰值 ≈ 7% 液位下沉）");
            Assert.AreEqual(-10f, TankFillMath.ConsumeKick(-0.1f, gain), Eps, "冲量与消耗量成正比");
            Assert.GreaterOrEqual(TankFillMath.ConsumeKick(-5f, gain), -100f, "单帧超量消耗按 100% 钳制");
        }

        [Test]
        public void SloshTarget_MapsAccelWithGainAndClamp()
        {
            Assert.AreEqual(0f, TankFillMath.SloshTarget(0f, 0.004f, 0.1f), Eps, "零加速度零目标");
            Assert.AreEqual(0.08f, TankFillMath.SloshTarget(20f, 0.004f, 0.1f), Eps, "20 m/s² → 0.08 rad");
            Assert.AreEqual(0.1f, TankFillMath.SloshTarget(50f, 0.004f, 0.1f), Eps, "超量程按 ±0.1 钳制");
            Assert.AreEqual(-0.1f, TankFillMath.SloshTarget(-50f, 0.004f, 0.1f), Eps, "负加速度 → 反向目标");
        }

        [Test]
        public void Tilt_ClampsToTankCapClearance()
        {
            Assert.AreEqual(0.05f, TankFillMath.Tilt(0.05f, 0.06f), Eps, "量程内直通");
            Assert.AreEqual(0.06f, TankFillMath.Tilt(0.5f, 0.06f), Eps, "上限钳制（罐盖净空约束）");
            Assert.AreEqual(-0.06f, TankFillMath.Tilt(-0.5f, 0.06f), Eps, "下限钳制");
        }

        [Test]
        public void RadialSqueeze_OneAtRest_SqueezesWithSlosh()
        {
            Assert.AreEqual(1f, TankFillMath.RadialSqueeze(0f, 0f, 0.03f), Eps, "静止不挤压");
            float squeezed = TankFillMath.RadialSqueeze(0.1f, 0f, 0.03f);
            Assert.AreEqual(1f - 0.1f * 0.03f, squeezed, Eps, "晃动量线性换算挤压");
            Assert.GreaterOrEqual(TankFillMath.RadialSqueeze(1f, 1f, 0.03f), 0.9f, "挤压下限 0.9 防负缩放");
        }

        [Test]
        public void LevelBob_GainWithSymmetricClamp()
        {
            Assert.AreEqual(0f, TankFillMath.LevelBob(0f, 0.06f, 0.08f), Eps);
            Assert.AreEqual(0.06f, TankFillMath.LevelBob(1f, 0.06f, 0.08f), Eps);
            Assert.AreEqual(0.08f, TankFillMath.LevelBob(5f, 0.06f, 0.08f), Eps, "bob 上限 ±8%（Inkwave L3348）");
            Assert.AreEqual(-0.08f, TankFillMath.LevelBob(-5f, 0.06f, 0.08f), Eps, "bob 下限 ±8%");
        }

        [Test]
        public void LowInkPulse_BoundedAndPeriodic()
        {
            float amp = 0.03f;
            float max = float.MinValue;
            // 扫描一个完整周期以上（2.4Hz 周期 ≈ 0.417s），否则窗口可能错过峰值
            for (int i = 0; i < 500; i++)
            {
                float v = TankFillMath.LowInkPulse(i * 0.001f, 2.4f, amp);
                Assert.LessOrEqual(Mathf.Abs(v), amp + Eps, "脉动必须约束在 ±amp 内");
                max = Mathf.Max(max, v);
            }
            Assert.Greater(max, amp * 0.9f, "扫描窗口内应触到接近峰值的正脉动");
        }

        [Test]
        public void LowInkHysteresis_EnterBelowExitAbove_NoBoundaryFlicker()
        {
            // 进入：<0.15；退出：>0.20
            Assert.IsTrue(TankFillMath.LowInkHysteresis(false, 0.10f, 0.15f, 0.20f), "低于进入阈值必须告警");
            Assert.IsFalse(TankFillMath.LowInkHysteresis(false, 0.17f, 0.15f, 0.20f), "带内未激活保持安静");
            Assert.IsTrue(TankFillMath.LowInkHysteresis(true, 0.17f, 0.15f, 0.20f), "带内已激活保持告警（迟滞防闪烁）");
            Assert.IsFalse(TankFillMath.LowInkHysteresis(true, 0.25f, 0.15f, 0.20f), "高于退出阈值解除");
            // 参数顺序颠倒自动交换
            Assert.IsTrue(TankFillMath.LowInkHysteresis(false, 0.10f, 0.20f, 0.15f), "enter/exit 颠倒时行为一致");
        }

        [Test]
        public void DisplayLevel_FloorGuardAndFactorComposition()
        {
            Assert.AreEqual(0.5f, TankFillMath.DisplayLevel(0.5f, 0f, 0f, 0.004f), Eps, "无 bob/脉动时等于显示墨量");
            Assert.AreEqual(0.54f, TankFillMath.DisplayLevel(0.5f, 0.08f, 0f, 0.004f), Eps, "bob +8% 生效");
            Assert.AreEqual(0.004f, TankFillMath.DisplayLevel(0.001f, 0f, 0f, 0.004f), Eps, "极低墨量按下限保护（完全隐藏由 visible 开关负责）");
        }
    }
}
