using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 一维阻尼弹簧（半隐式欧拉 + 子步进），帧率无关、零 GC。
    /// 用于驱动「形态手势」等需要过冲-回稳的连续标量：快速翻转目标时状态天然连续，杜绝跳变
    /// （INKWAVE 用双向关键帧时间轴 + 按当前缩放反查时间防 pop，弹簧模型一次配置双向复用）。
    /// 数值参考 inkwave-aah-research/src/game/character.js spr()（半隐式欧拉，按 ω·dt 子步）。
    /// </summary>
    public struct AnimSpring1D
    {
        public float Value;
        public float Velocity;

        public AnimSpring1D(float initialValue)
        {
            Value = initialValue;
            Velocity = 0f;
        }

        /// <summary>向目标推进一个时间步。omega = 固有频率 (rad/s)，zeta = 阻尼比（&lt;1 过冲）。</summary>
        public void Step(float target, float omega, float zeta, float dt)
        {
            if (dt <= 0f || omega <= 0f) return;

            // 子步进保证大 dt 下仍稳定（半隐式欧拉稳定界 omega·h &lt; 2，这里按 1.1 倍留裕量）
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * omega * 0.55f));
            float h = dt / steps;
            float acc;
            for (int i = 0; i < steps; i++)
            {
                acc = omega * omega * (target - Value) - 2f * zeta * omega * Velocity;
                Velocity += acc * h;
                Value += Velocity * h;
            }
        }

        /// <summary>瞬时注入速度冲量（表演点缀用，如浮出甩动）。</summary>
        public void Kick(float velocityImpulse)
        {
            Velocity += velocityImpulse;
        }
    }
}
