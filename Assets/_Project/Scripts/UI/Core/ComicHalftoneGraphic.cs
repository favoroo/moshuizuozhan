using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 全屏半调网点底（漫画印刷质感）：铺在模态面板背后，让「纸」有颗粒。
    ///
    /// 为什么不做成贴图：项目坚持零外部美术资源，且网点需要跟着分辨率自适应；
    /// 纯顶点绘制还能被 Canvas 批次合并，移动端开销远小于一张大 PNG。
    /// 顶点上限 900 点，超出自动加大间距，避免大屏平板爆量。
    /// </summary>
    [AddComponentMenu("P1/UI/Comic Halftone")]
    public class ComicHalftoneGraphic : MaskableGraphic
    {
        [SerializeField, Range(6f, 32f)] private float spacing = 12f;

        [SerializeField, Range(0.8f, 5f)] private float dotRadius = 1.6f;

        [SerializeField, Range(0f, 1f)] private float alpha = 0.10f;

        [SerializeField, Range(0f, 90f)] private float angle = 15f;

        [SerializeField] private int maxDots = 900;

        public float Spacing
        {
            get => spacing;
            set
            {
                float clamped = Mathf.Max(4f, value);
                if (Mathf.Approximately(spacing, clamped)) return;
                spacing = clamped;
                SetVerticesDirty();
            }
        }

        public float Alpha
        {
            get => alpha;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(alpha, clamped)) return;
                alpha = clamped;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 2f || rect.height < 2f) return;

            float step = Mathf.Max(6f, spacing);
            int stepsX = Mathf.CeilToInt(rect.width / step) + 1;
            int stepsY = Mathf.CeilToInt(rect.height / step) + 1;
            int total = stepsX * stepsY;

            int cap = Mathf.Max(50, maxDots);
            if (total > cap)
            {
                // 超上限就等比放大间距，保证大屏也不爆顶点
                step *= Mathf.Sqrt(total / (float)cap);
                stepsX = Mathf.CeilToInt(rect.width / step) + 1;
                stepsY = Mathf.CeilToInt(rect.height / step) + 1;
            }

            float rad = Mathf.Clamp(dotRadius, 0.5f, step * 0.45f);
            float a = angle * Mathf.Deg2Rad;
            Vector2 axisX = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * step;
            Vector2 axisY = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * step;

            Color tint = new Color(color.r, color.g, color.b, alpha * color.a);

            for (int i = 0; i <= stepsX; i++)
            {
                for (int j = 0; j <= stepsY; j++)
                {
                    Vector2 p = new Vector2(
                        rect.xMin + (j - 0.5f) * axisY.x + (i - 0.5f) * axisX.x,
                        rect.yMin + (j - 0.5f) * axisY.y + (i - 0.5f) * axisX.y);

                    if (p.x < rect.xMin || p.x > rect.xMax || p.y < rect.yMin || p.y > rect.yMax) continue;
                    AddDot(vh, p, rad, tint);
                }
            }
        }

        private static void AddDot(VertexHelper vh, Vector2 center, float radius, Color tint)
        {
            const int segs = 6;
            int centerIndex = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);

            int start = vh.currentVertCount;
            for (int i = 0; i < segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                vh.AddVert(new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f),
                    tint, Vector2.zero);
            }

            for (int i = 0; i < segs; i++)
            {
                vh.AddTriangle(centerIndex, start + i, start + (i + 1) % segs);
            }
        }
    }
}