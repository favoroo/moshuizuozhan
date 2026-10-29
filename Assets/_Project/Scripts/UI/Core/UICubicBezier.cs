using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// CSS cubic-bezier 缓动曲线。参考 Inkwave 的
    /// <c>--spring: cubic-bezier(.34, 1.56, .64, 1)</c>，用于一次性弹出的「过冲 + 回稳」补间。
    /// 纯数学结构，不依赖任何 UI 组件，可在 EditMode 测试中独立验证。
    /// </summary>
    [System.Serializable]
    public struct UICubicBezier
    {
        [Range(0f, 1f)] public float x1;

        public float y1;

        [Range(0f, 1f)] public float x2;

        public float y2;

        public UICubicBezier(float x1, float y1, float x2, float y2)
        {
            this.x1 = x1;
            this.y1 = y1;
            this.x2 = x2;
            this.y2 = y2;
        }

        /// <summary>INKWAVE 主弹簧：cubic-bezier(.34, 1.56, .64, 1)。</summary>
        public static UICubicBezier Spring => new UICubicBezier(0.34f, 1.56f, 0.64f, 1f);

        /// <summary>更夸张的二次回弹：cubic-bezier(.26, 1.9, .5, 1)。</summary>
        public static UICubicBezier SpringStrong => new UICubicBezier(0.26f, 1.9f, 0.5f, 1f);

        /// <summary>平滑缓出：cubic-bezier(.22, 1, .36, 1)。</summary>
        public static UICubicBezier EaseOut => new UICubicBezier(0.22f, 1f, 0.36f, 1f);

        /// <summary>平滑缓入：cubic-bezier(.6, 0, .8, .25)。</summary>
        public static UICubicBezier EaseIn => new UICubicBezier(0.6f, 0f, 0.8f, 0.25f);

        /// <summary>
        /// 输入 0..1 的归一化时间，返回插值系数。y 方向不做上限钳制，
        /// 因此曲线本身带过冲时返回值会大于 1（这正是果冻弹性的来源）。
        /// </summary>
        public float Evaluate(float time)
        {
            if (time <= 0f) return 0f;
            if (time >= 1f) return 1f;

            float cx1 = Mathf.Clamp01(x1);
            float cx2 = Mathf.Clamp01(x2);
            float t = time;

            // Newton-Raphson 求 x(t) = time 的参数 t（命中率最高）。
            for (int i = 0; i < 8; i++)
            {
                float error = SampleX(t, cx1, cx2) - time;
                if (Mathf.Abs(error) < 1e-5f) break;

                float derivative = SampleDerivativeX(t, cx1, cx2);
                if (Mathf.Abs(derivative) < 1e-6f) break;

                t = Mathf.Clamp01(t - error / derivative);
            }

            // 二分兜底：极端控制点下 Newton 可能振荡，保证 x 单调收敛。
            float lo = 0f;
            float hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float x = SampleX(t, cx1, cx2);
                if (Mathf.Abs(x - time) < 1e-4f) break;

                if (x < time) lo = t;
                else hi = t;

                t = (lo + hi) * 0.5f;
            }

            return SampleY(t, y1, y2);
        }

        private static float SampleX(float t, float cx1, float cx2)
        {
            float u = 1f - t;
            return 3f * u * u * t * cx1 + 3f * u * t * t * cx2 + t * t * t;
        }

        private static float SampleY(float t, float cy1, float cy2)
        {
            float u = 1f - t;
            return 3f * u * u * t * cy1 + 3f * u * t * t * cy2 + t * t * t;
        }

        private static float SampleDerivativeX(float t, float cx1, float cx2)
        {
            float u = 1f - t;
            return 3f * u * u * cx1 + 6f * u * t * (cx2 - cx1) + 3f * t * t * (1f - cx2);
        }
    }
}