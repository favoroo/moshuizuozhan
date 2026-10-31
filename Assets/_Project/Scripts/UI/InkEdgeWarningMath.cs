using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 踩敌墨屏幕边缘提示的纯数学部分（无 GameObject 依赖，EditMode 可直接断言）：
    /// 强度指数逼近（淡入快、淡出慢）与显隐阈值。
    /// </summary>
    public static class InkEdgeWarningMath
    {
        /// <summary>
        /// 向目标强度做指数逼近：踩敌墨 target=1（rate 取淡入），离开 target=0（rate 取淡出）。
        /// dt 为 0 或负值时保持原值，避免暂停 / 首帧跳变。
        /// </summary>
        public static float StepIntensity(float current, bool inEnemyInk,
            float fadeInRate, float fadeOutRate, float dt)
        {
            float clamped = Mathf.Clamp01(current);
            if (dt <= 0f) return clamped;

            float target = inEnemyInk ? 1f : 0f;
            float rate = Mathf.Max(0.01f, inEnemyInk ? fadeInRate : fadeOutRate);
            float k = 1f - Mathf.Exp(-rate * dt);
            return Mathf.Clamp01(clamped + (target - clamped) * k);
        }

        /// <summary>低于阈值时视图应关闭 Graphic，保证空闲零成本。</summary>
        public static bool IsVisible(float intensity, float visibleThreshold)
        {
            return intensity > Mathf.Max(0f, visibleThreshold);
        }

        /// <summary>
        /// 底边加权权重计算（屏幕归一化 uv.y）：底部最厚，往上过渡变细。
        /// 对齐 INKWAVE: bottom = smoothstep(0.6, 0.0, uv.y)。
        /// 注意不能用 <see cref="Mathf.SmoothStep"/>（它会把 t 钳制到 [0,1] 再平滑，
        /// 导致 uv.y &gt; 0.6 的所有位置权重恒为 0.4，屏幕顶部漏出 40% 底边厚度）。
        /// </summary>
        public static float CalculateBottomWeight(float uvY)
        {
            float t = Mathf.Clamp01(uvY / 0.6f);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 计算给定位置的动态墨带几何厚度。
        /// </summary>
        public static float CalculateBandWidth(float intensity, float edgeBand, float bottomBand, float bottomWeight)
        {
            return Mathf.Max(0f, intensity) * (edgeBand + bottomBand * (bottomWeight * bottomWeight + 0.15f * bottomWeight));
        }

        /// <summary>
        /// 顶部垂滴随强度的动态生长长度。
        /// </summary>
        public static float CalculateDripLength(float intensity, float dripStrength, float lengthScale)
        {
            return 0.12f * Mathf.Clamp01(intensity) * Mathf.Clamp01(dripStrength) * Mathf.Max(0.1f, lengthScale);
        }
    }
}
