using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 环状弧（2026-11 底部双环仪表专用）：纯网格绘制的「甜甜圈扇区」，
    /// 顶部起点顺时针填充 <see cref="Fill01"/> × 360°。
    ///
    /// 为什么不用 `Image.Filled + Radial360`：径向填充需要一个带 alpha 的圆环精灵，
    /// 而运行时生成的精灵在即时的 Canvas 重建里会被当成整块不透明矩形画出来
    /// （Simple 正常、Radial360 失效）。项目里的圆环基元本来就是网格绘制的
    /// （<see cref="UIInkShapeGraphic"/> / <see cref="UIRotatingRingGraphic"/>），这里沿用同一做法：
    /// **不依赖任何贴图与采样**，任意尺寸/任意环宽都保持硬边清晰。
    ///
    /// 用法：挂在独立子节点上（Graphic 带 DisallowMultipleComponent，不能和 Image 同物体），
    /// 方形 rect = 正圆；<see cref="Configure"/> 设环宽与外沿留边，<see cref="Fill01"/> 每帧可写。
    /// </summary>
    [AddComponentMenu("P1/UI/Arc Ring")]
    public class UIArcRingGraphic : MaskableGraphic
    {
        [Tooltip("填充比例 0..1（0 = 不画，1 = 整圈）")]
        [SerializeField, Range(0f, 1f)] private float fill01 = 1f;

        [Tooltip("起始角度（度，uGUI 的 0 = 右，90 = 上 = 顶点）")]
        [SerializeField] private float startAngleDegrees = 90f;

        [Tooltip("顺时针填充（从顶点往右上走）；关掉则逆时针")]
        [SerializeField] private bool clockwise = true;

        [Tooltip("环带宽度（本地像素，桌面参考分辨率）")]
        [SerializeField] private float bandWidth = 8f;

        [Tooltip("环带外沿到 rect 边的留边（本地像素）——留给墨底圆，形成自动描边")]
        [SerializeField] private float outerInset = 3f;

        [Tooltip("每段扇区的最大角度：越小越圆滑（默认 3°）")]
        [SerializeField, Range(1f, 45f)] private float degreesPerSegment = 3f;

        /// <summary>填充比例 0..1。</summary>
        public float Fill01
        {
            get => fill01;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Abs(fill01 - clamped) < 0.0005f) return;
                fill01 = clamped;
                SetVerticesDirty();
            }
        }

        /// <summary>环带宽度（本地像素）。</summary>
        public float BandWidth => bandWidth;

        /// <summary>外沿留边（本地像素）。</summary>
        public float OuterInset => outerInset;

        public void Configure(float bandWidthPx, float outerInsetPx, Color arcColor,
            float startAngle = 90f, bool isClockwise = true)
        {
            bandWidth = Mathf.Max(1f, bandWidthPx);
            outerInset = Mathf.Max(0f, outerInsetPx);
            color = arcColor;
            startAngleDegrees = startAngle;
            clockwise = isClockwise;
            SetVerticesDirty();
        }

        /// <summary>只改环带几何（移动端换尺寸时调用，不碰颜色）。</summary>
        public void SetBand(float bandWidthPx, float outerInsetPx)
        {
            float band = Mathf.Max(1f, bandWidthPx);
            float inset = Mathf.Max(0f, outerInsetPx);
            if (Mathf.Approximately(band, bandWidth) && Mathf.Approximately(inset, outerInset)) return;

            bandWidth = band;
            outerInset = inset;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float fill = Mathf.Clamp01(fill01);
            if (fill <= 0.001f) return;

            Rect r = rectTransform.rect;
            float half = Mathf.Min(r.width, r.height) * 0.5f;
            if (half <= 1f) return;

            float outerRadius = Mathf.Max(1f, half - outerInset);
            float innerRadius = Mathf.Max(0f, outerRadius - bandWidth);
            if (outerRadius - innerRadius < 0.5f) return;

            Vector2 center = r.center;
            float sweep = 360f * fill;
            int segments = Mathf.Max(1, Mathf.CeilToInt(sweep / Mathf.Max(1f, degreesPerSegment)));
            float direction = clockwise ? -1f : 1f;
            float startRad = startAngleDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < segments; i++)
            {
                float a0 = startRad + direction * sweep * (i / (float)segments) * Mathf.Deg2Rad;
                float a1 = startRad + direction * sweep * ((i + 1) / (float)segments) * Mathf.Deg2Rad;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                Vector2 inner0 = center + d0 * innerRadius;
                Vector2 outer0 = center + d0 * outerRadius;
                Vector2 outer1 = center + d1 * outerRadius;
                Vector2 inner1 = center + d1 * innerRadius;

                AddQuad(vh, inner0, outer0, outer1, inner1);
            }
        }

        private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            bandWidth = Mathf.Max(1f, bandWidth);
            outerInset = Mathf.Max(0f, outerInset);
            SetVerticesDirty();
        }
#endif
    }
}