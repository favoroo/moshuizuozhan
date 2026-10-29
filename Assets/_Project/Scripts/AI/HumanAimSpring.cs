using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// 二阶临界阻尼弹簧（Critically-damped Spring），把瞄准角当作带质量的转轴来解：
    /// <c>v += (ω²·Δ − 2ζω·v)·dt</c>，ζ = 1 时为临界阻尼（不振荡、最快收敛）。
    /// 再叠加最大转速上限，让 Bot 的甩枪快不过人类手柄推杆的极限，彻底消除「瞬间锁头」。
    /// 大 dt（首帧 hitch / 断点）时按 ω·h ≤ 0.2 自动细分，半隐式欧拉不会数值爆炸。
    /// </summary>
    public sealed class HumanAimSpring
    {
        /// <summary>当前角度（度，[-180, 180]）。</summary>
        public float AngleDegrees { get; private set; }

        /// <summary>当前角速度（度/秒）。</summary>
        public float AngularVelocityDegrees { get; private set; }

        public void Reset(float angleDegrees)
        {
            AngleDegrees = Normalize(angleDegrees);
            AngularVelocityDegrees = 0f;
        }

        /// <summary>
        /// 向 <paramref name="targetDegrees"/> 推进一帧，返回新的当前角度。
        /// </summary>
        /// <param name="omega">弹簧固有频率（rad/s）：越大跟枪越紧。</param>
        /// <param name="maxRateDegrees">最大瞄准转速（度/秒）。</param>
        /// <param name="dampingRatio">阻尼比：1 = 临界阻尼，&lt;1 允许微小过冲。</param>
        public float Step(float targetDegrees, float omega, float maxRateDegrees, float dampingRatio, float dt)
        {
            if (dt <= 0f)
                return AngleDegrees;

            omega = Mathf.Max(0.01f, omega);
            dampingRatio = Mathf.Max(0.05f, dampingRatio);
            maxRateDegrees = Mathf.Max(0.01f, maxRateDegrees);

            int steps = Mathf.Clamp(Mathf.CeilToInt(omega * dt / 0.2f), 1, 8);
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                float delta = ShortestDelta(AngleDegrees, targetDegrees);
                AngularVelocityDegrees += (omega * omega * delta
                                           - 2f * dampingRatio * omega * AngularVelocityDegrees) * h;
                AngularVelocityDegrees = Mathf.Clamp(AngularVelocityDegrees, -maxRateDegrees, maxRateDegrees);
                AngleDegrees = Normalize(AngleDegrees + AngularVelocityDegrees * h);
            }

            return AngleDegrees;
        }

        /// <summary>把角度差折算到 [-180, 180]，保证弹簧永远走最短弧。</summary>
        public static float ShortestDelta(float fromDegrees, float toDegrees)
        {
            float d = (toDegrees - fromDegrees) % 360f;
            if (d > 180f) d -= 360f;
            else if (d < -180f) d += 360f;
            return d;
        }

        public static float Normalize(float angleDegrees)
        {
            angleDegrees %= 360f;
            if (angleDegrees > 180f) angleDegrees -= 360f;
            else if (angleDegrees < -180f) angleDegrees += 360f;
            return angleDegrees;
        }

        /// <summary>把 (yaw, 0, 0) 角度转成水平方向向量。</summary>
        public static Vector3 DirectionFromYaw(float yawDegrees)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>水平方向向量转 yaw 角度（度）。</summary>
        public static float YawFromDirection(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}
