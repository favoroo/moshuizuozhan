using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 放射动作线（漫画「POW!」能量感）：从中心向外发散、内细外粗的锥形射线。
    ///
    /// 用在：大招就绪、结算胜利徽章、开赛横幅、暂停面板背后。
    /// 纯网格绘制，静止时零开销；需要旋转时只改 transform.localRotation（不重建网格）。
    /// </summary>
    [AddComponentMenu("P1/UI/Comic Action Lines")]
    public class ComicActionLinesGraphic : MaskableGraphic
    {
        [SerializeField, Range(4, 48)] private int rayCount = 24;

        [Tooltip("射线起点半径占 Rect 短边的比例（0 = 从中心开始）")]
        [SerializeField, Range(0f, 0.9f)] private float innerRadius = 0.35f;

        [Tooltip("射线末端半径比例：1 = 贴到 Rect 边缘")]
        [SerializeField, Range(0.2f, 1.5f)] private float outerRadius = 1f;

        [Tooltip("内端宽度（像素）")]
        [SerializeField, Range(0.5f, 12f)] private float innerWidth = 1.2f;

        [Tooltip("外端宽度（像素）")]
        [SerializeField, Range(1f, 40f)] private float outerWidth = 7f;

        [SerializeField] private bool alternate = true;

        [SerializeField, Range(0f, 1f)] private float alpha = 0.18f;

        public int RayCount
        {
            get => rayCount;
            set
            {
                int clamped = Mathf.Clamp(value, 4, 48);
                if (rayCount == clamped) return;
                rayCount = clamped;
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
            float halfMin = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (halfMin < 4f) return;

            int count = Mathf.Clamp(rayCount, 4, 48);
            Vector2 center = rect.center;
            float r0 = Mathf.Lerp(0f, halfMin, Mathf.Clamp01(innerRadius));
            float r1 = Mathf.Max(r0 + 2f, halfMin * Mathf.Clamp(outerRadius, 0.2f, 1.5f));

            Color tint = new Color(color.r, color.g, color.b, alpha * color.a);

            for (int i = 0; i < count; i++)
            {
                if (alternate && (i % 2) == 1) continue;

                float a = (i / (float)count + 0.5f / count) * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 perp = new Vector2(-dir.y, dir.x);

                Vector2 p0 = center + dir * r0;
                Vector2 p1 = center + dir * r1;

                Vector2 w0 = perp * (innerWidth * 0.5f);
                Vector2 w1 = perp * (outerWidth * 0.5f);

                AddQuad(vh, p0 - w0, p0 + w0, p1 + w1, p1 - w1, tint);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero);
            vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}