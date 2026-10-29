using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 纯网格圆环进度条（P4-B）：用于 4v4 头像死亡倒计时进度环、技能冷却环等。
    /// 零 Sprite 依赖，支持顺时针/逆时针扫掠与底圈柔光。
    /// </summary>
    [AddComponentMenu("P1/UI/Progress Ring Graphic")]
    public class UIProgressRingGraphic : MaskableGraphic
    {
        private const int MaxSamples = 64;

        [Range(0f, 1f)]
        [SerializeField] private float progress = 0f;

        [Tooltip("环半径占容器短边一半的比例（1 = 正好贴边）")]
        [Range(0.2f, 1.5f)]
        [SerializeField] private float radiusFraction = 1.05f;

        [Range(0.5f, 20f)]
        [SerializeField] private float thickness = 3.5f;

        [SerializeField] private float startAngleDegrees = 90f;

        [SerializeField] private bool clockwise = true;

        [SerializeField] private Color ringBackgroundColor = new Color(0f, 0f, 0f, 0.35f);

        [SerializeField] private Color progressColor = Color.white;

        public float Progress
        {
            get => progress;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(progress, clamped)) return;
                progress = clamped;
                SetVerticesDirty();
            }
        }

        public float RadiusFraction
        {
            get => radiusFraction;
            set
            {
                if (Mathf.Approximately(radiusFraction, value)) return;
                radiusFraction = value;
                SetVerticesDirty();
            }
        }

        public float Thickness
        {
            get => thickness;
            set
            {
                if (Mathf.Approximately(thickness, value)) return;
                thickness = value;
                SetVerticesDirty();
            }
        }

        public Color RingBackgroundColor
        {
            get => ringBackgroundColor;
            set
            {
                if (ringBackgroundColor == value) return;
                ringBackgroundColor = value;
                SetVerticesDirty();
            }
        }

        public Color ProgressColor
        {
            get => progressColor;
            set
            {
                if (progressColor == value) return;
                progressColor = value;
                SetVerticesDirty();
            }
        }

        public void Configure(float initialProgress, Color progColor, Color? bgColor = null, float ringThickness = 3.5f)
        {
            progress = Mathf.Clamp01(initialProgress);
            progressColor = progColor;
            if (bgColor.HasValue) ringBackgroundColor = bgColor.Value;
            thickness = ringThickness;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 6f || rect.height < 6f) return;

            float maxRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float outer = maxRadius * radiusFraction;
            float inner = Mathf.Max(0f, outer - thickness);
            if (outer <= 1f || inner >= outer) return;

            Vector2 center = rect.center;

            // 1. 底圈（完整 360 度半透明圈）
            if (ringBackgroundColor.a > 0.005f)
            {
                AddArc(vh, center, inner, outer, 0f, 360f, 36, ringBackgroundColor);
            }

            // 2. 进度弧（0..1 动态填充）
            if (progress > 0.005f && progressColor.a > 0.005f)
            {
                float totalSweep = 360f * progress;
                float startDeg = startAngleDegrees;
                float endDeg = clockwise ? (startDeg - totalSweep) : (startDeg + totalSweep);

                int samples = Mathf.Clamp(Mathf.CeilToInt(MaxSamples * progress), 4, MaxSamples);
                AddArc(vh, center, inner, outer, startDeg, endDeg, samples, progressColor);
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
