using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>墨滴形状类型。</summary>
    public enum UIInkShape
    {
        /// <summary>椭圆墨滴：边缘带有机起伏，适合卡片、计时器、大招球背景。</summary>
        Blob = 0,

        /// <summary>圆角矩形：适合面板、按钮、战况条。</summary>
        RoundedRect = 1,
    }

    /// <summary>
    /// 纯网格绘制的墨滴 / 圆角面板底图（P4-A 基类）。
    ///
    /// 一个 UI 元素即可同时画出：填充色、Chunky 黑边、内侧白色墨沿、斜切高光。
    /// 不依赖任何 Sprite / 材质资产，因此 P4-B（战况条）、P4-C（液体球）都能直接复用；
    /// 颜色由 <see cref="Graphic.color"/> 控制，正好交给 InkTeamColorFilter 自动染色。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Shape Graphic")]
    public class UIInkShapeGraphic : MaskableGraphic
    {
        private const int MaxRingPoints = 128;

        [Header("形状")]
        [SerializeField] private UIInkShape shape = UIInkShape.Blob;

        [Tooltip("周向分段数；圆角矩形会均分到 4 个圆角")]
        [SerializeField, Range(12, 128)] private int segments = 64;

        [Tooltip("墨滴边缘起伏幅度（0 = 规整椭圆）")]
        [SerializeField, Range(0f, 0.25f)] private float wobbleAmplitude = 0.055f;

        [Tooltip("起伏相位的随机种子，不同数值得到不同墨形")]
        [SerializeField] private int seed = 7;

        [SerializeField] private Vector2 squash = Vector2.one;

        [SerializeField, Range(0f, 128f)] private float cornerRadius = 28f;

        [Header("描边 · Chunky Outline")]
        [SerializeField, Range(0f, 64f)] private float borderWidth = 6f;

        [SerializeField] private Color borderColor = new Color(0.082f, 0.071f, 0.110f, 1f);

        [Tooltip("内沿白色墨光的透明度（果冻质感的关键）")]
        [SerializeField, Range(0f, 1f)] private float innerRimAlpha = 0.10f;

        [SerializeField, Range(0f, 8f)] private float innerRimWidth = 2f;

        [Header("斜切高光")]
        [SerializeField] private bool drawGloss = true;

        [SerializeField, Range(0f, 1f)] private float glossAlpha = 0.18f;

        [Tooltip("高光斜带的斜率（圆角矩形）/ 倾斜角（墨滴）")]
        [SerializeField, Range(-1f, 1f)] private float glossSlope = 0.30f;

        [SerializeField, Range(0f, 1f)] private float glossCenter = 0.72f;

        [SerializeField, Range(0f, 0.6f)] private float glossThickness = 0.16f;

        private readonly Vector3[] _outerRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _innerRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _rimRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _glossPolygon = new Vector3[64];

        private float _phase1;
        private float _phase2;
        private float _phase3;

        private float _wobbleTimer;
        private float _wobbleDuration;
        private float _wobbleStrength;
        private float _wobblePulse;

        public UIInkShape Shape
        {
            get => shape;
            set
            {
                if (shape == value) return;
                shape = value;
                Refresh();
            }
        }

        public float BorderWidth
        {
            get => borderWidth;
            set
            {
                if (Mathf.Approximately(borderWidth, value)) return;
                borderWidth = value;
                Refresh();
            }
        }

        public Color BorderColor
        {
            get => borderColor;
            set
            {
                if (borderColor == value) return;
                borderColor = value;
                Refresh();
            }
        }

        public float CornerRadius
        {
            get => cornerRadius;
            set
            {
                if (Mathf.Approximately(cornerRadius, value)) return;
                cornerRadius = value;
                Refresh();
            }
        }

        public float WobbleAmplitude
        {
            get => wobbleAmplitude;
            set
            {
                if (Mathf.Approximately(wobbleAmplitude, value)) return;
                wobbleAmplitude = value;
                Refresh();
            }
        }

        public Vector2 Squash
        {
            get => squash;
            set
            {
                if (squash == value) return;
                squash = value;
                Refresh();
            }
        }

        /// <summary>墨形起伏的随机种子；不同卡片用不同值可避免「复制感」。</summary>
        public int Seed
        {
            get => seed;
            set
            {
                if (seed == value) return;
                seed = value;
                Refresh();
            }
        }

        /// <summary>把主题里的卡片预设一次性刷到本图形上。</summary>
        public void ApplyStyle(UIInkCardStyle style, Color fill)
        {
            if (style == null) return;

            shape = style.shape;
            borderWidth = style.borderWidth;
            borderColor = style.borderColor;
            cornerRadius = style.cornerRadius;
            wobbleAmplitude = style.wobble;
            glossAlpha = style.glossAlpha;
            glossSlope = style.glossSlope;
            glossCenter = style.glossCenter;
            glossThickness = style.glossThickness;
            innerRimAlpha = style.innerRimAlpha;
            drawGloss = glossAlpha > 0.001f;
            color = fill;
            Refresh();
        }

        public void Refresh()
        {
            SetVerticesDirty();
        }

        /// <summary>
        /// 墨汁晃动：叠加一段衰减正弦，让边缘「果冻般」抖一下。
        /// 只在动画期间每帧重建网格，静止时零开销。
        /// </summary>
        public void PlayWobble(float amplitude = 0.09f, float duration = 0.42f)
        {
            _wobbleStrength = Mathf.Clamp(amplitude, 0f, 0.25f);
            _wobbleDuration = Mathf.Max(0.05f, duration);
            _wobbleTimer = _wobbleDuration;
            Refresh();
        }

        private void Update()
        {
            if (_wobbleTimer <= 0f) return;

            _wobbleTimer -= Time.unscaledDeltaTime;
            float t = 1f - Mathf.Clamp01(_wobbleTimer / _wobbleDuration);
            _wobblePulse = _wobbleTimer > 0f
                ? Mathf.Sin(t * Mathf.PI * 3.5f) * (1f - t) * _wobbleStrength
                : 0f;
            Refresh();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 1f || rect.height < 1f) return;

            bool isBlob = shape == UIInkShape.Blob;
            int perCorner = 0;
            int count;
            if (isBlob)
            {
                count = Mathf.Clamp(segments, 12, MaxRingPoints);
            }
            else
            {
                perCorner = Mathf.Clamp(Mathf.RoundToInt(segments / 4f), 3, MaxRingPoints / 4);
                count = perCorner * 4;
            }

            _phase1 = Phase(1);
            _phase2 = Phase(2);
            _phase3 = Phase(3);

            float minHalf = Mathf.Min(rect.width, rect.height) * 0.5f;
            float border = Mathf.Clamp(borderWidth, 0f, Mathf.Max(0f, minHalf - 1f));
            float rimWidth = innerRimWidth > 0.01f ? innerRimWidth : 2f;
            float rim = border > 0.01f && innerRimAlpha > 0.001f
                ? Mathf.Clamp(rimWidth, 0f, border)
                : 0f;

            BuildRing(_outerRing, count, rect, 0f, perCorner);
            BuildRing(_innerRing, count, rect, border, perCorner);
            if (rim > 0.001f)
            {
                BuildRing(_rimRing, count, rect, Mathf.Min(border + rim, minHalf - 1f), perCorner);
            }
            else
            {
                System.Array.Copy(_innerRing, _rimRing, count);
            }

            // 1) 填充（画到内沿墨光的内侧，避免盖住高光）
            AddPolygonFan(vh, rect.center, _rimRing, count, color);

            // 2) Chunky 黑边
            if (border > 0.001f)
            {
                AddRingStrip(vh, _outerRing, _innerRing, count, borderColor);
            }

            // 3) 内侧白色墨沿
            if (rim > 0.001f)
            {
                AddRingStrip(vh, _innerRing, _rimRing, count, new Color(1f, 1f, 1f, innerRimAlpha));
            }

            // 4) 斜切高光
            if (drawGloss && glossAlpha > 0.001f)
            {
                AddGloss(vh, rect, Mathf.Clamp01(glossAlpha));
            }
        }

        private void BuildRing(Vector3[] dst, int count, Rect rect, float inset, int perCorner)
        {
            if (shape == UIInkShape.Blob)
            {
                float halfW = rect.width * 0.5f - inset;
                float halfH = rect.height * 0.5f - inset;
                if (halfW < 1f || halfH < 1f)
                {
                    for (int i = 0; i < count; i++) dst[i] = rect.center;
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    float angle = i / (float)count * Mathf.PI * 2f;
                    float wobble = WobbleAt(angle);
                    float x = rect.center.x + Mathf.Cos(angle) * halfW * wobble * squash.x;
                    float y = rect.center.y + Mathf.Sin(angle) * halfH * wobble * squash.y;
                    dst[i] = new Vector3(x, y, 0f);
                }
            }
            else
            {
                Rect inner = new Rect(
                    rect.xMin + inset,
                    rect.yMin + inset,
                    Mathf.Max(2f, rect.width - inset * 2f),
                    Mathf.Max(2f, rect.height - inset * 2f));
                BuildRoundedRing(dst, inner, Mathf.Max(0.01f, cornerRadius - inset), perCorner);
            }
        }

        private static void BuildRoundedRing(Vector3[] dst, Rect rect, float radius, int perCorner)
        {
            float safeRadius = Mathf.Clamp(radius, 0.01f, Mathf.Min(rect.width, rect.height) * 0.5f);
            Vector2 bl = new Vector2(rect.xMin + safeRadius, rect.yMin + safeRadius);
            Vector2 br = new Vector2(rect.xMax - safeRadius, rect.yMin + safeRadius);
            Vector2 tr = new Vector2(rect.xMax - safeRadius, rect.yMax - safeRadius);
            Vector2 tl = new Vector2(rect.xMin + safeRadius, rect.yMax - safeRadius);

            int index = 0;
            AddArc(dst, ref index, bl, safeRadius, 180f, 270f, perCorner);
            AddArc(dst, ref index, br, safeRadius, 270f, 360f, perCorner);
            AddArc(dst, ref index, tr, safeRadius, 0f, 90f, perCorner);
            AddArc(dst, ref index, tl, safeRadius, 90f, 180f, perCorner);
        }

        private static void AddArc(Vector3[] dst, ref int index, Vector2 center, float radius,
            float fromDegrees, float toDegrees, int perCorner)
        {
            for (int i = 0; i < perCorner; i++)
            {
                float t = i / (float)perCorner;
                float angle = Mathf.Lerp(fromDegrees, toDegrees, t) * Mathf.Deg2Rad;
                dst[index++] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    center.y + Mathf.Sin(angle) * radius,
                    0f);
            }
        }

        private float WobbleAt(float angle)
        {
            float amplitude = Mathf.Clamp(wobbleAmplitude + _wobblePulse, 0f, 0.25f);
            if (amplitude <= 0.0001f) return 1f;

            float noise = 0.55f * Mathf.Sin(angle * 3f + _phase1)
                        + 0.30f * Mathf.Sin(angle * 5f + _phase2)
                        + 0.15f * Mathf.Sin(angle * 7f + _phase3);
            return 1f + amplitude * noise;
        }

        private float Phase(int index)
        {
            float value = Mathf.Sin(seed * 12.9898f + index * 78.233f) * 43758.5453f;
            return (value - Mathf.Floor(value)) * Mathf.PI * 2f;
        }

        private static void AddPolygonFan(VertexHelper vh, Vector3 center, Vector3[] ring, int count,
            Color tint)
        {
            int centerIndex = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);

            int start = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.AddVert(ring[i], tint, Vector2.zero);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vh.AddTriangle(centerIndex, start + i, start + next);
            }
        }

        private static void AddRingStrip(VertexHelper vh, Vector3[] outer, Vector3[] inner, int count,
            Color tint)
        {
            int baseIndex = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.AddVert(outer[i], tint, Vector2.zero);
                vh.AddVert(inner[i], tint, Vector2.zero);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int outerA = baseIndex + i * 2;
                int innerA = outerA + 1;
                int outerB = baseIndex + next * 2;
                int innerB = outerB + 1;

                vh.AddTriangle(outerA, innerA, innerB);
                vh.AddTriangle(outerA, innerB, outerB);
            }
        }

        private void AddGloss(VertexHelper vh, Rect rect, float alpha)
        {
            if (shape == UIInkShape.Blob)
            {
                AddBlobSheen(vh, rect, alpha);
            }
            else
            {
                AddSlantedBand(vh, rect, alpha);
            }
        }

        /// <summary>墨滴：沿左上内缘的一条柔光月牙，不干扰中间的文字。</summary>
        private void AddBlobSheen(VertexHelper vh, Rect rect, float alpha)
        {
            float halfMin = Mathf.Min(rect.width, rect.height) * 0.5f;
            float outerInset = borderWidth + Mathf.Max(2f, innerRimWidth) + 2f;
            float band = Mathf.Max(6f, halfMin * 0.06f);
            float innerInset = outerInset + band;
            if (innerInset >= halfMin - 2f) return;

            const int count = 20;
            const float start = 94f * Mathf.Deg2Rad;
            const float end = 176f * Mathf.Deg2Rad;

            int baseIndex = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f);
                float angle = Mathf.Lerp(start, end, t);
                float wobble = WobbleAt(angle);
                float fade = Mathf.Sin(t * Mathf.PI);
                Color tint = new Color(1f, 1f, 1f, alpha * fade);

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                float outerX = rect.center.x + cos * (rect.width * 0.5f - outerInset) * wobble * squash.x;
                float outerY = rect.center.y + sin * (rect.height * 0.5f - outerInset) * wobble * squash.y;
                float innerX = rect.center.x + cos * (rect.width * 0.5f - innerInset) * wobble * squash.x;
                float innerY = rect.center.y + sin * (rect.height * 0.5f - innerInset) * wobble * squash.y;

                vh.AddVert(new Vector3(outerX, outerY, 0f), tint, Vector2.zero);
                vh.AddVert(new Vector3(innerX, innerY, 0f), tint, Vector2.zero);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int outerA = baseIndex + i * 2;
                int innerA = outerA + 1;
                int outerB = outerA + 2;
                int innerB = outerA + 3;

                vh.AddTriangle(outerA, innerA, innerB);
                vh.AddTriangle(outerA, innerB, outerB);
            }
        }

        /// <summary>圆角面板：横贯上部的斜切高光带。</summary>
        private void AddSlantedBand(VertexHelper vh, Rect rect, float alpha)
        {
            float inset = borderWidth + Mathf.Max(6f, Mathf.Min(cornerRadius, rect.height * 0.2f));
            float x0 = rect.xMin + inset;
            float x1 = rect.xMax - inset;
            if (x1 - x0 < 12f) return;

            float yMin = rect.yMin + borderWidth + 2f;
            float yMax = rect.yMax - borderWidth - 2f;
            if (yMax - yMin < 4f) return;

            const int samples = 10;
            const int total = samples * 2;
            Color tint = new Color(1f, 1f, 1f, alpha);

            for (int i = 0; i < samples; i++)
            {
                float t = i / (samples - 1f);
                float x = Mathf.Lerp(x0, x1, t);
                float baseY = rect.yMin + rect.height * (glossCenter + glossSlope * (t - 0.5f));
                float halfBand = rect.height * glossThickness * 0.5f;

                _glossPolygon[i] = new Vector3(x, Mathf.Clamp(baseY + halfBand, yMin, yMax), 0f);
                _glossPolygon[total - 1 - i] = new Vector3(x, Mathf.Clamp(baseY - halfBand, yMin, yMax), 0f);
            }

            AddPolygonFan(vh, _glossPolygon, total, tint);
        }

        private static void AddPolygonFan(VertexHelper vh, Vector3[] points, int count, Color tint)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < count; i++) center += points[i];
            center /= count;

            int centerIndex = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);

            int start = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.AddVert(points[i], tint, Vector2.zero);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vh.AddTriangle(centerIndex, start + i, start + next);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}