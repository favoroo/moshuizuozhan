using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 漫画风动效数学（P10）：只有缓动与时长换算，不持有状态。
    ///
    /// 规范要求：duration ≤ 100ms、ease-out、禁止 bounce/elastic。
    /// 所有自绘动效（徽章弹出、面板入场）都应调用这里，
    /// 不要写 Mathf.Sin 抖动或超过 0.1 秒的插值。
    /// </summary>
    public static class ComicMotion
    {
        /// <summary>标准漫画弹出曲线：起步快、收尾稳，没有回弹。</summary>
        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        /// <summary>
        /// 帧率无关的平滑系数：等价于指数趋近，1 表示瞬间到位。
        /// 用法：value = Mathf.Lerp(value, target, ComicMotion.Smoothing(dt, duration));
        /// </summary>
        public static float Smoothing(float deltaTime, float duration)
        {
            if (duration <= 0.0001f) return 1f;
            return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / duration);
        }

        /// <summary>80ms 弹出：从 scale 0 到 1，角度从 tilt 回到 0。</summary>
        public static void PopScale(Transform target, float elapsed, float duration = ComicStyle.HoverDuration,
            float fromScale = 0f, float tilt = -6f)
        {
            if (target == null) return;
            float t = EaseOutCubic(elapsed / Mathf.Max(0.02f, duration));
            float s = Mathf.LerpUnclamped(fromScale, 1f, t);
            target.localScale = new Vector3(s, s, 1f);
            target.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(tilt, 0f, t));
        }

        public static Vector2 ShadowOffsetFor(Vector2 home, bool hovered, bool pressed)
        {
            if (pressed) return Vector2.zero;
            return hovered ? home * 0.5f : home;
        }

        /// <summary>把任意时长夹到规范允许范围内（0.02~0.1s）。</summary>
        public static float ClampDuration(float seconds)
        {
            return Mathf.Clamp(seconds, 0.02f, 0.1f);
        }
    }
}