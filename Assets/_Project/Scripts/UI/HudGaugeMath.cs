using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// HUD 液面几何与低墨判定的纯函数集合（P4-C）。
    ///
    /// 单独抽出来是为了能在 EditMode 里直接断言液面高度、圆形/圆角截面与告警迟滞，
    /// 不必启动 Canvas 或渲染。<see cref="UILiquidFillGraphic"/> 与两个视图组件都只调用这里的实现。
    /// </summary>
    public static class HudGaugeMath
    {
        /// <summary>液面的基准 Y：fill01=0 贴容器底，=1 齐容器顶。</summary>
        public static float SurfaceBase(float bottom, float height, float fill01)
        {
            if (height <= 0f) return bottom;
            return bottom + Mathf.Clamp01(fill01) * height;
        }

        /// <summary>
        /// 圆形截面在给定 x 处的半高。返回负值表示该 x 落在圆外（该列不绘制）。
        /// </summary>
        public static float CircleColumnHalfHeight(float x, float centerX, float halfWidth, float halfHeight)
        {
            if (halfWidth <= 1e-4f || halfHeight <= 1e-4f) return -1f;

            float dx = Mathf.Abs(x - centerX) / halfWidth;
            if (dx > 1f) return -1f;

            return halfHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - dx * dx));
        }

        /// <summary>圆角矩形在给定 x 处的底边抬升量（只有四个圆角处大于 0）。</summary>
        public static float RoundedRectBottomInset(float x, float centerX, float halfWidth, float radius)
        {
            if (halfWidth <= 1e-4f) return 0f;

            float r = Mathf.Clamp(radius, 0f, halfWidth);
            if (r <= 1e-4f) return 0f;

            float dx = Mathf.Abs(x - centerX);
            float straight = halfWidth - r;
            if (dx <= straight) return 0f;

            float over = dx - straight;
            if (over >= r) return r;

            return r - Mathf.Sqrt(Mathf.Max(0f, r * r - over * over));
        }

        /// <summary>
        /// 双正弦叠加的液面起伏（像素）。两个分量权重相加为 1，因此结果严格落在
        /// [-amplitude, +amplitude] 内——液面永远不会越过容器边界或让读数失真。
        /// </summary>
        public static float WaveOffset(float x, float centerX, float width, float phase, float cycles, float amplitude)
        {
            if (width <= 1e-4f || amplitude <= 0f) return 0f;

            float u = (x - centerX) / width;
            float angle = u * Mathf.PI * 2f * Mathf.Max(0.01f, cycles);
            float noise = 0.62f * Mathf.Sin(angle + phase)
                        + 0.38f * Mathf.Sin(angle * 1.73f - phase * 1.35f);

            return amplitude * noise;
        }

        /// <summary>
        /// 带迟滞的低墨判定：跌破阈值立刻告警，回到 阈值+迟滞 才解除，
        /// 避免液面在 20% 附近来回抖动时警报与红闪频繁开关。
        /// </summary>
        public static bool IsLowInk(float ink01, float threshold, bool wasLow, float hysteresis = 0.03f)
        {
            return wasLow
                ? ink01 < threshold + Mathf.Max(0f, hysteresis)
                : ink01 < threshold;
        }

        /// <summary>是否在本帧刚刚跨过满能线（用于只播一次爆发光环/提示）。</summary>
        public static bool CrossedReady(float charge01, bool wasReady, float readyThreshold = 0.999f)
        {
            return !wasReady && charge01 >= readyThreshold;
        }

        /// <summary>把 0..1 的液面换算成容器内像素高度（用于刻度线定位）。</summary>
        public static float ThresholdOffset(float innerHeight, float threshold01)
        {
            return Mathf.Clamp01(threshold01) * Mathf.Max(0f, innerHeight);
        }
    }
}
