using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 屏幕墨水漫灌转场与受击冲击的纯数学工具类（无 GameObject 依赖，EditMode 可独立断言）。
    /// 包含：死亡敌墨漫灌序列、复活己墨虹膜撕裂扩展、弹簧阻尼形变振荡与径向色相分离衰减。
    /// </summary>
    public static class ScreenInkTransitionMath
    {
        public const float PunchOmega = 16f;
        public const float DefaultChromaDecay = 3.2f;
        public const float MaxIrisHole = 1.32f;

        public static float EaseInOut(float t)
        {
            float c = Mathf.Clamp01(t);
            return c * c * (3f - 2f * c);
        }

        public static float EaseOut(float t)
        {
            float c = Mathf.Clamp01(t);
            return 1f - (1f - c) * (1f - c);
        }

        /// <summary>
        /// 计算阵亡时敌墨从四周向内漫灌的覆盖度 (0~1)。
        /// 对齐 INKWAVE: t < 0.24s 快速涌入至 1.0；0.24s~0.5s 维持全屏覆盖；之后略微回退并保持重滴垂挂。
        /// </summary>
        public static float CalculateFloodCoverage(float t)
        {
            if (t <= 0f) return 0f;
            if (t < 0.24f) return EaseInOut(t / 0.24f);
            if (t < 0.50f) return 1f;
            float outT = Mathf.Clamp01((t - 0.50f) / 0.80f);
            return Mathf.Lerp(1f, 0.17f, EaseOut(outT));
        }

        /// <summary>
        /// 阵亡漫灌期间顶部垂滴的长度因子。
        /// </summary>
        public static float CalculateFloodDrip(float t)
        {
            if (t < 0.50f) return 0.05f;
            return Mathf.Min(0.40f, 0.05f + (t - 0.50f) * 0.08f);
        }

        /// <summary>
        /// 计算复活时从中心向外撕裂展开的虹膜破口孔径 (0~1.32)。
        /// 单调递增，破口边缘带不规则有机波动。
        /// </summary>
        public static float CalculateIrisHole(float t, float delay = 0.10f, float duration = 0.72f)
        {
            if (t <= delay) return 0.0001f;
            float k = Mathf.Clamp01((t - delay) / Mathf.Max(0.01f, duration));
            float curve = Mathf.Pow(k, 1.7f) * 0.75f + EaseOut(k) * 0.25f;
            return curve * MaxIrisHole;
        }

        /// <summary>
        /// 受击屏幕形变弹性振荡（临界阻尼弹簧振子，w=16，无超调颠簸）。
        /// 采用子步进欧拉积分，保证任意帧率下的绝对数值稳定性。
        /// </summary>
        public static void StepPunchSpring(ref float punch, ref float punchV, float dt, float omega = PunchOmega)
        {
            if (dt <= 0f) return;

            int steps = Mathf.Max(1, Mathf.CeilToInt((omega * dt) / 0.12f));
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                punchV += (-omega * omega * punch - 2f * omega * punchV) * h;
                punch += punchV * h;
            }

            if (Mathf.Abs(punch) < 0.0001f && Mathf.Abs(punchV) < 0.0001f)
            {
                punch = 0f;
                punchV = 0f;
            }
        }

        /// <summary>
        /// 径向色相分离（Chroma）指数衰减。
        /// </summary>
        public static float StepChroma(float currentChroma, float dt, float decayRate = DefaultChromaDecay)
        {
            if (dt <= 0f) return currentChroma;
            return Mathf.Max(0f, currentChroma - dt * Mathf.Max(0.1f, decayRate));
        }
    }
}
