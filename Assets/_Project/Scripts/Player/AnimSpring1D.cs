using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 一维阻尼弹簧（阻尼振子闭式解），帧率无关、零 GC。
    /// 用于驱动「形态手势」等需要过冲-回稳的连续标量：快速翻转目标时状态天然连续，杜绝跳变
    /// （INKWAVE 用双向关键帧时间轴 + 按当前缩放反查时间防 pop，弹簧模型一次配置双向复用）。
    ///
    /// P0 修复（2026-11-01）：旧实现是半隐式欧拉 + 按 ω·dt 子步，ω·dt 接近 1 时特征值模 &gt; 1
    /// （ζ=0.8、ω=30、30FPS 实测 ≈1.13）会指数发散，峰值还随帧率漂移
    /// （60FPS≈0.011 / 120FPS≈0.029 / 240FPS≈0.037，而 ImpulseForPeak 的设计目标是 0.045）。
    /// 现改为对 x'' = -ω²(x-target) - 2ζω·x' 在每个 dt 内求精确解（目标视为常量），
    /// 任意帧率峰值一致、无条件稳定；30/60/120/240FPS 与「屏幕刷新率切换」不再改变手感。
    /// </summary>
    public struct AnimSpring1D
    {
        public float Value;
        public float Velocity;

        /// <summary>|ζ-1| 小于该值时走临界阻尼分支（避免 ω_d→0 的 0/0 与双实根大数相消）。</summary>
        private const float CriticalDampingEpsilon = 0.01f;

        public AnimSpring1D(float initialValue)
        {
            Value = initialValue;
            Velocity = 0f;
        }

        /// <summary>向目标推进一个时间步。omega = 固有频率 (rad/s)，zeta = 阻尼比（&lt;1 过冲）。</summary>
        public void Step(float target, float omega, float zeta, float dt)
        {
            if (dt <= 0f || omega <= 0f) return;

            // 相对目标的位移 d 与速度 v 满足 d'' + 2ζω·d' + ω²·d = 0（dt 内目标视为常量）。
            float displacement = Value - target;
            float velocity = Velocity;
            float decay = zeta * omega;
            // e^{-ζω·dt}：大 dt / 大 ω 时下溢到 0（= 已完全回稳），不会出现 NaN
            float envelope = Mathf.Exp(-decay * dt);

            if (Mathf.Abs(zeta - 1f) <= CriticalDampingEpsilon)
            {
                // 临界阻尼：d(t) = (d0 + (v0 + ω·d0)·t)·e^{-ωt}
                float slope = velocity + omega * displacement;
                Value = target + (displacement + slope * dt) * envelope;
                Velocity = (velocity - omega * slope * dt) * envelope;
            }
            else if (zeta < 1f)
            {
                // 欠阻尼：d(t) = e^{-ζωt}·[d0·cos(ωd·t) + ((v0 + ζω·d0)/ωd)·sin(ωd·t)]
                float dampedOmega = omega * Mathf.Sqrt(Mathf.Max(1e-6f, 1f - zeta * zeta));
                float cos = Mathf.Cos(dampedOmega * dt);
                float sin = Mathf.Sin(dampedOmega * dt);
                float sinCoefficient = (velocity + decay * displacement) / dampedOmega;

                Value = target + envelope * (displacement * cos + sinCoefficient * sin);
                Velocity = envelope * (velocity * cos
                    - ((omega * omega * displacement + decay * velocity) / dampedOmega) * sin);
            }
            else
            {
                // 过阻尼：双实根 r± = -ζω ± ω√(ζ²-1)，d(t) = A·e^{r+·t} + B·e^{r-·t}
                float gamma = omega * Mathf.Sqrt(zeta * zeta - 1f);
                float expRising = Mathf.Exp((-decay + gamma) * dt);
                float expFalling = Mathf.Exp((-decay - gamma) * dt);

                float a = (velocity + (decay + gamma) * displacement) / (2f * gamma);
                float b = displacement - a;

                Value = target + a * expRising + b * expFalling;
                Velocity = a * (-decay + gamma) * expRising + b * (-decay - gamma) * expFalling;
            }

            // 极端参数（NaN/inf 配置）兜底：宁可直接贴住目标，也不要把渲染变换写成 NaN
            if (float.IsNaN(Value) || float.IsInfinity(Value) ||
                float.IsNaN(Velocity) || float.IsInfinity(Velocity))
            {
                Value = target;
                Velocity = 0f;
            }
        }

        /// <summary>瞬时注入速度冲量（表演点缀用，如浮出甩动）。</summary>
        public void Kick(float velocityImpulse)
        {
            Velocity += velocityImpulse;
        }

        /// <summary>清零当前值与速度（换枪/复活/传送时使用）。</summary>
        public void Reset()
        {
            Value = 0f;
            Velocity = 0f;
        }

        /// <summary>
        /// 求「注入后峰值 ≈ peakOffset」所需的速度冲量。
        /// 临界阻尼（ζ≥0.99）峰值 = v/(e·ω)；ζ=0.8 时约 v/(2.36·ω)，用 2.4 近似。
        /// 闭式解下该系数的含义不变（峰值与帧率无关，只由 ω 与 ζ 决定）。
        /// </summary>
        public static float ImpulseForPeak(float peakOffset, float omega, float dampingRatio)
        {
            if (omega <= 0.0001f)
                return 0f;

            float factor = dampingRatio >= 0.99f ? 2.718f : 2.4f;
            return peakOffset * omega * factor;
        }
    }
}
