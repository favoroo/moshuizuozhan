using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 低血屏幕边缘暗角的纯数学部分（P42-B，无 GameObject 依赖，EditMode 可直接断言）。
    ///
    /// 语义参考 INKWAVE <c>src/ui/hud.js</c> 的 _updHp：
    /// 血量跌破阈值后屏幕四周压暗，越接近 0 越强；跌破危急线时允许**有限次数**告警闪烁，
    /// 然后停在稳定强度上（Docs/UI-GeometricBold.md 明确禁止常驻循环脉冲，
    /// 但允许功能性告警闪烁）。
    /// </summary>
    public static class LowHealthVignetteMath
    {
        /// <summary>开始压暗的血量比例（35%：与 HUD 血条翻红阈值 30% 错开，暗角先到、翻红后到）。</summary>
        public const float WarningThreshold01 = 0.35f;

        /// <summary>危急线（20%）：跌破后开始有限次告警闪烁。</summary>
        public const float CriticalThreshold01 = 0.20f;

        /// <summary>暗角强度上限（0~1，Shader 再乘 _MaxAlpha）。</summary>
        public const float MaxIntensity = 1f;

        /// <summary>危急告警闪烁次数（有限次，符合规范例外条款）。</summary>
        public const int CriticalFlashCount = 3;

        /// <summary>单次闪烁周期（秒）。</summary>
        public const float CriticalFlashPeriodSeconds = 0.32f;

        /// <summary>超过危急线后停止闪烁、回到稳定暗角的血量比例（去抖，避免边缘反复触发）。</summary>
        public const float CriticalExitHysteresis01 = 0.24f;

        /// <summary>淡入速率（/s）：受伤瞬间暗角快速到位。</summary>
        public const float FadeInRate = 9f;

        /// <summary>淡出速率（/s）：回血/复活后缓慢退场，避免一闪一闪。</summary>
        public const float FadeOutRate = 3.2f;

        /// <summary>低于该强度直接关 Graphic，保证空闲零成本。</summary>
        public const float VisibleThreshold = 0.004f;

        /// <summary>
        /// 血量比例 → 目标强度（0~1）：
        /// 35% 以上为 0；从 35% 线性升到危急线 20% 时约 0.55；20% 以下陡升到 1。
        /// 用两段线性而不是单段，是因为"快死"（&lt;20%）的压迫感需要明显跳变。
        /// </summary>
        public static float TargetIntensity(float health01)
        {
            float hp = Mathf.Clamp01(health01);
            if (hp >= WarningThreshold01) return 0f;

            if (hp >= CriticalThreshold01)
            {
                float t = (WarningThreshold01 - hp) / (WarningThreshold01 - CriticalThreshold01);
                return Mathf.Lerp(0f, 0.55f, Mathf.Clamp01(t));
            }

            float t2 = hp / Mathf.Max(0.0001f, CriticalThreshold01);
            return Mathf.Lerp(0.55f, MaxIntensity, Mathf.Clamp01(1f - t2));
        }

        /// <summary>
        /// 强度指数逼近（淡入快、淡出慢）。dt ≤ 0 时保持原值，避免暂停 / 首帧跳变。
        /// </summary>
        public static float StepIntensity(float current, float target, float dt)
        {
            float clamped = Mathf.Clamp01(current);
            if (dt <= 0f) return clamped;

            float t = Mathf.Clamp01(target);
            float rate = t > clamped ? FadeInRate : FadeOutRate;
            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, rate) * dt);
            return Mathf.Clamp01(clamped + (t - clamped) * k);
        }

        /// <summary>低于阈值时视图应关闭 Graphic。</summary>
        public static bool IsVisible(float intensity)
        {
            return intensity > VisibleThreshold;
        }

        /// <summary>
        /// 危急闪烁权重（0.55~1）：只在 elapsed 落在有限次闪烁窗口内摆动，
        /// 窗口结束后恒为 1（稳定暗角，不循环）。
        /// </summary>
        public static float CriticalFlashWeight(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f) return 1f;

            float total = CriticalFlashCount * CriticalFlashPeriodSeconds;
            if (elapsedSeconds >= total) return 1f;

            float phase = (elapsedSeconds % CriticalFlashPeriodSeconds) / CriticalFlashPeriodSeconds;
            // 硬切方波（不是正弦呼吸）：亮 55% 周期 / 暗 45% 周期
            return phase < 0.55f ? 1f : 0.55f;
        }

        /// <summary>
        /// 是否进入危急态（带迟滞）：跌破 20% 进入；恢复超过 24% 才退出，
        /// 避免血量在阈值附近抖动时反复触发闪烁。
        /// </summary>
        public static bool UpdateCritical(bool wasCritical, float health01)
        {
            float hp = Mathf.Clamp01(health01);
            if (wasCritical) return hp < CriticalExitHysteresis01;
            return hp < CriticalThreshold01;
        }
    }
}
