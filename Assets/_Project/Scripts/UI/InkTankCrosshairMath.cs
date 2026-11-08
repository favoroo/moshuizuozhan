using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 准星旁墨水罐（P42-E，对标 INKWAVE <c>.iw-tank</c> / <c>_updTank</c>）的纯函数集合：
    /// 液面追值、空闲淡出、低墨有限闪烁、空罐抖动、横向晃动倾斜、上浮气泡。全部无 GameObject 依赖。
    /// </summary>
    public static class InkTankCrosshairMath
    {
        // ── 液面追值（INKWAVE：level += (ink - level) * (1 - exp(-14·dt))）──

        /// <summary>液面追值速率：每秒逼近剩余差值的 14 倍（同 INKWAVE）。</summary>
        public const float LevelChaseRate = 14f;

        /// <summary>液面向目标墨水量指数逼近一步，永不过冲。</summary>
        public static float StepLevel(float level, float target, float dt)
        {
            if (dt <= 0f) return level;
            float t = 1f - Mathf.Exp(-dt * LevelChaseRate);
            return level + (target - level) * t;
        }

        // ── 空闲淡出（INKWAVE is-idle：满罐且不低墨持续 1.4s 后隐去，省一处常驻视觉）──

        /// <summary>满罐且无告警持续该秒数后淡出罐体。</summary>
        public const float IdleFadeSeconds = 1.4f;

        /// <summary>满罐判定阈值（同 INKWAVE 0.995）。</summary>
        public const float FullLevel01 = 0.995f;

        /// <summary>
        /// 推进空闲计时：<paramref name="fullAndCalm"/>（满罐且无低墨）时累计，否则立刻归零。
        /// </summary>
        public static float StepIdle(float idleSeconds, bool fullAndCalm, float dt)
        {
            if (!fullAndCalm) return 0f;
            return idleSeconds + Mathf.Max(0f, dt);
        }

        /// <summary>空闲计时是否已到淡出线。</summary>
        public static bool ShouldFade(float idleSeconds)
        {
            return idleSeconds >= IdleFadeSeconds;
        }

        // ── 低墨有限闪烁：进入低墨瞬间红框硬切闪 3 下，之后保持稳定红框（不循环）──

        /// <summary>进入低墨时的硬切闪烁次数。</summary>
        public const int LowBlinkCount = 3;

        /// <summary>单次闪烁周期（秒）。</summary>
        public const float LowBlinkPeriodSeconds = 0.32f;

        /// <summary>
        /// 低墨闪烁权重：1 = 警示红框，0 = 常态墨框。
        /// 进入低墨后的 <see cref="LowBlinkCount"/> 个周期内按半周期硬切换；窗口外恒 1（稳定红框）。
        /// </summary>
        public static float LowBlinkWeight(float age)
        {
            if (age < 0f) return 1f;
            float total = LowBlinkCount * LowBlinkPeriodSeconds;
            if (age >= total) return 1f;

            float phase = (age % LowBlinkPeriodSeconds) / LowBlinkPeriodSeconds;
            return phase < 0.5f ? 0f : 1f;
        }

        // ── 空罐抖动（INKWAVE is-empty：iw-shake .35s，衰减摆动）──

        /// <summary>空罐抖动时长（秒，同 INKWAVE）。</summary>
        public const float EmptyShakeSeconds = 0.35f;

        /// <summary>空罐抖动起始摆幅（度）。</summary>
        public const float EmptyShakeDegrees = 7f;

        /// <summary>空罐抖动角偏移（度）：衰减正弦，窗口外恒 0。</summary>
        public static float EmptyShakeOffset(float age)
        {
            if (age < 0f || age >= EmptyShakeSeconds) return 0f;

            float t = age / EmptyShakeSeconds;
            float decay = 1f - t;
            return Mathf.Sin(t * Mathf.PI * 6f) * EmptyShakeDegrees * decay;
        }

        // ── 横向晃动（INKWAVE 用相机偏航角速度 + 侧向加速度驱动弹簧；这里取等效输入：侧向速度）──

        /// <summary>侧向速度到液面倾角的换算系数（度 / (m/s)）。</summary>
        public const float TiltPerMeterPerSecond = 2.4f;

        /// <summary>液面倾角上限（度）。</summary>
        public const float TiltMaxDegrees = 10f;

        /// <summary>
        /// 液面倾角（度）：侧向移动时液面滞后倾斜。取负号让液面朝移动反方向翘起。
        /// </summary>
        public static float TiltDegrees(float lateralSpeed)
        {
            return Mathf.Clamp(-lateralSpeed * TiltPerMeterPerSecond, -TiltMaxDegrees, TiltMaxDegrees);
        }

        // ── 回墨气泡（INKWAVE：rising 时冒泡，v 0.5~1.2 罐高/秒）──

        /// <summary>气泡池容量。</summary>
        public const int BubblePoolSize = 4;

        /// <summary>气泡上升速度（罐高比例 / 秒）。</summary>
        public const float BubbleRiseSpeed = 0.9f;

        /// <summary>气泡按寿命归一化的上升进度 0~1。</summary>
        public static float BubbleProgress(float age)
        {
            return Mathf.Clamp01(age * BubbleRiseSpeed);
        }
    }
}
