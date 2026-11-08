using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 潜入墨水屏幕浸染的纯数学部分（无 GameObject 依赖，EditMode 可直接断言）。
    ///
    /// 语义参考 INKWAVE <c>src/fx/screenfx.js</c> 的 swim 段：
    /// <c>s.swim = damp(s.swim, form === 'swim' ? 1 : 0, 7, dt)</c> —— 进出都用 λ=7 的
    /// 指数平滑，潜入瞬间快速"没入水中"、浮出瞬间快速"破水而出"，不闪不跳。
    /// 视觉强度（浸染系数 / 焦散增益）在 SubmergedInk.shader 内，保持与 INKWAVE 同源可对照。
    /// </summary>
    public static class SubmergedInkMath
    {
        /// <summary>平滑速率（/s）：INKWAVE damp λ=7，进出对称。</summary>
        public const float FadeRate = 7f;

        /// <summary>低于该强度直接关 Graphic，保证空闲零成本。</summary>
        public const float VisibleThreshold = 0.004f;

        /// <summary>
        /// 强度指数逼近（对称速率）。dt ≤ 0 时保持原值，避免暂停 / 首帧跳变。
        /// </summary>
        public static float StepIntensity(float current, float target, float dt)
        {
            float clamped = Mathf.Clamp01(current);
            if (dt <= 0f) return clamped;

            float t = Mathf.Clamp01(target);
            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, FadeRate) * dt);
            return Mathf.Clamp01(clamped + (t - clamped) * k);
        }

        /// <summary>低于阈值时视图应关闭 Graphic。</summary>
        public static bool IsVisible(float intensity)
        {
            return intensity > VisibleThreshold;
        }
    }
}
