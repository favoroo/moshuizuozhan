using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 金色旋转光环（P4-C）：大招充满时套在液体球外围的虚线环 + 柔光底圈。
    ///
    /// 纯网格绘制，无 Sprite；<see cref="Intensity"/> 0 → 完全隐藏，
    /// 视图组件用弹簧把它推上去，就得到「爆发瞬间亮起」的观感。
    /// </summary>
    [AddComponentMenu("P1/UI/Rotating Ring")]
    public class UIRotatingRingGraphic : MaskableGraphic
    {
        private const int DashSamples = 4;

        [Tooltip("环半径占容器短边一半的比例（1 = 正好贴着边缘，>1 略微外扩）")]
        [SerializeField, Range(0.1f, 1.4f)] private float radiusFraction = 1.02f;

        [SerializeField, Range(0.5f, 24f)] private float thickness = 4f;

        [Tooltip("整圈的柔光底圈厚度倍率（0 = 不画柔光）")]
        [SerializeField, Range(0f, 8f)] private float glowThicknessScale = 3.2f;

        [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.16f;

        [SerializeField, Range(1, 32)] private int dashCount = 9;

        [Tooltip("每段虚线占自身扇区的比例")]
        [SerializeField, Range(0.05f, 1f)] private float dashFill = 0.55f;

        [SerializeField] private float spinDegreesPerSecond = 130f;

        [SerializeField, Range(0f, 1f)] private float intensity = 1f;

        private float _angle;

        public float Intensity
        {
            get => intensity;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(intensity, clamped)) return;
                intensity = clamped;
                SetVerticesDirty();
            }
        }

        public void Configure(float radius, float stroke, int dashes, float spin, Color ringColor)
        {
            radiusFraction = Mathf.Clamp(radius, 0.1f, 1.4f);
            thickness = Mathf.Max(0.5f, stroke);
            dashCount = Mathf.Clamp(dashes, 1, 32);
            spinDegreesPerSecond = spin;
            color = ringColor;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!isActiveAndEnabled) return;
            if (intensity <= 0.001f) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _angle += spinDegreesPerSecond * dt;
            if (_angle > 360f) _angle -= 360f;
            if (_angle < -360f) _angle += 360f;

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 8f || rect.height < 8f) return;

            float alpha = Mathf.Clamp01(intensity) * color.a;
            if (alpha <= 0.002f) return;

            float maxRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float outer = maxRadius * radiusFraction;
            float inner = Mathf.Max(0f, outer - thickness);
            if (outer <= 1f || inner >= outer) return;

            Vector2 center = rect.center;

            // 柔光底圈：整圈低透明度，让光环像在发光而不是硬线条
            if (glowThicknessScale > 0.01f && glowAlpha > 0.001f)
            {
                float glowOuter = outer + (thickness * (glowThicknessScale - 1f)) * 0.5f;
                float glowInner = Mathf.Max(0f, inner - (thickness * (glowThicknessScale - 1f)) * 0.5f);
                Color glow = new Color(color.r, color.g, color.b, alpha * glowAlpha);
                AddArc(vh, center, glowInner, glowOuter, 0f, 360f, 40, glow);
            }

            int dashes = Mathf.Clamp(dashCount, 1, 32);
            float sector = 360f / dashes;
            float sweep = sector * Mathf.Clamp01(dashFill);
            Color ringTint = new Color(color.r, color.g, color.b, alpha);

            for (int i = 0; i < dashes; i++)
            {
                float start = _angle + sector * i;
                AddArc(vh, center, inner, outer, start, start + sweep, DashSamples, ringTint);
            }
        }

        private static void AddArc(VertexHelper vh, Vector2 center, float inner, float outer,
            float startDegrees, float endDegrees, int samples, Color tint)
        {
            int baseIndex = vh.currentVertCount;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float angle = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, t);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vh.AddVert(new Vector3(center.x + cos * inner, center.y + sin * inner, 0f), tint, Vector2.zero);
                vh.AddVert(new Vector3(center.x + cos * outer, center.y + sin * outer, 0f), tint, Vector2.zero);
            }

            for (int i = 0; i < samples; i++)
            {
                int a = baseIndex + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;

                vh.AddTriangle(a, b, d);
                vh.AddTriangle(a, d, c);
            }
        }
    }
}
