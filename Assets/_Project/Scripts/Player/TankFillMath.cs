using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 储液罐液面动画纯数学（对齐 INKWAVE character.js _updateTank L3339-3351 / damp L1272，
    /// 不依赖 MonoBehaviour 生命周期，EditMode 友好）。配方：
    /// 显示墨量先指数平滑（INKWAVE damp rate 8）→ 液位 = 平滑墨量 ×(1 + bob + 低墨脉动)，
    /// 倾斜/挤压由加速度驱动的双弹簧给出，全部钳制防穿模（罐盖净空 10mm）。
    /// </summary>
    public static class TankFillMath
    {
        /// <summary>显示墨量的瞬变吸附阈值：|显示−真实| 超过它（复活/传送式突变）直接吸附，不播追赶动画。</summary>
        public const float SnapThreshold = 0.4f;

        /// <summary>
        /// 指数平滑（INKWAVE damp(a,b,rate,dt)：帧率无关，rate=8 ≈ 125ms 时间常数）。
        /// rate ≤ 0 或 dt ≤ 0 时原值返回（调用方跳过本帧）。
        /// </summary>
        public static float ExpDamp(float current, float target, float rate, float dt)
        {
            if (rate <= 0f || dt <= 0f) return current;
            float t = 1f - Mathf.Exp(-rate * dt);
            return Mathf.LerpUnclamped(current, target, t);
        }

        /// <summary>是否需要吸附（复活/传送/乌贼浮出后基准失效）：显示值与真实值偏差超阈值。</summary>
        public static bool NeedsSnap(float displayInk, float targetInk)
        {
            return Mathf.Abs(displayInk - targetInk) > SnapThreshold;
        }

        /// <summary>
        /// 扣墨液面下沉冲量：只有墨量下降（delta &lt; 0）才触发，冲量 = −|Δ|×gain（负速度 = 液面先砸下去再回摆）。
        /// 墨量上升（回墨）交给指数平滑缓升，不叠加冲量。gain 非法时按 0 处理。
        /// </summary>
        public static float ConsumeKick(float inkDelta, float gain)
        {
            if (inkDelta >= 0f) return 0f;
            float g = Mathf.Max(0f, gain);
            return -Mathf.Min(-inkDelta, 1f) * g;
        }

        /// <summary>加速度 → 晃动弹簧目标角（INKWAVE kax×0.004 钳 ±0.1，rad）。</summary>
        public static float SloshTarget(float localAccel, float gain, float targetMax)
        {
            float g = Mathf.Max(0f, gain);
            return Mathf.Clamp(localAccel * g, -Mathf.Max(0f, targetMax), Mathf.Max(0f, targetMax));
        }

        /// <summary>液面倾斜渲染钳制（INKWAVE ±0.06rad ≈ 3.4°，罐盖净空约束）。</summary>
        public static float Tilt(float sloshSpringValue, float tiltMaxRad)
        {
            float m = Mathf.Max(0f, tiltMaxRad);
            return Mathf.Clamp(sloshSpringValue, -m, m);
        }

        /// <summary>晃动径向挤压（INKWAVE 1 − hypot(tx,tz)×0.03，保体积感；结果钳到 [0.9, 1]）。</summary>
        public static float RadialSqueeze(float sloshX, float sloshZ, float squeeze)
        {
            float s = Mathf.Max(0f, squeeze);
            return Mathf.Clamp(1f - Mathf.Sqrt(sloshX * sloshX + sloshZ * sloshZ) * s, 0.9f, 1f);
        }

        /// <summary>液面 bob 弹簧值 → 液位百分比因子（INKWAVE tl×0.06 钳 ±0.08）。</summary>
        public static float LevelBob(float bobSpringValue, float gain, float bobMax)
        {
            float g = Mathf.Max(0f, gain);
            float m = Mathf.Max(0f, bobMax);
            return Mathf.Clamp(bobSpringValue * g, -m, m);
        }

        /// <summary>低墨呼吸脉动因子：与 bob 同频的正弦（INKWAVE 无此项，P1 扩展；幅度 ±amp）。</summary>
        public static float LowInkPulse(float time, float pulseHz, float pulseAmp)
        {
            float hz = Mathf.Max(0f, pulseHz);
            return Mathf.Sin(time * hz * Mathf.PI * 2f) * Mathf.Max(0f, pulseAmp);
        }

        /// <summary>
        /// 低墨告警迟滞：低于 <paramref name="enterFraction"/> 进入、高于 <paramref name="exitFraction"/> 退出
        /// （enter 应 ≤ exit，参数颠倒时自动交换），防止阈值附近边界闪烁。
        /// </summary>
        public static bool LowInkHysteresis(bool active, float ink01, float enterFraction, float exitFraction)
        {
            float lo = Mathf.Min(enterFraction, exitFraction);
            float hi = Mathf.Max(enterFraction, exitFraction);
            if (active) return ink01 > hi ? false : true;
            return ink01 < lo;
        }

        /// <summary>
        /// 最终液位（0..1+）：max(floor, inkDisplay×(1+bob+pulse))。INKWAVE L3348 同构，
        /// floor 让极低墨量仍保留发丝级液面（液面完全消失由 visible 开关负责）。
        /// </summary>
        public static float DisplayLevel(float inkDisplay, float bobFactor, float pulseFactor, float floor)
        {
            float f = Mathf.Max(0f, floor);
            return Mathf.Max(f, inkDisplay * (1f + bobFactor + pulseFactor));
        }
    }
}
