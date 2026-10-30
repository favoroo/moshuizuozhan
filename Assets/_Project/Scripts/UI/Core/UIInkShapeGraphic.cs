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

        /// <summary>对话气泡：大圆角 + 尖角尾，漫画风信息块专用。</summary>
        SpeechBubble = 2,
    }

    /// <summary>
    /// 纯网格绘制的墨滴 / 圆角面板 / 对话气泡底图（P4-A 基类，P10 扩展漫画风）。
    ///
    /// 一个 UI 元素即可同时画出：硬边偏移阴影、填充色、半调网点、Chunky 黑边、对话气泡尖角。
    /// 不依赖任何 Sprite / 材质资产，因此 HUD / 菜单 / 移动端都能直接复用；
    /// 颜色由 <see cref="Graphic.color"/> 控制，正好交给 InkTeamColorFilter 自动染色。
    ///
    /// 漫画模式（comicPanel=true）会关掉旧 Inkwave 的斜切高光与内沿墨光，
    /// 换成「实心墨色硬阴影 + 网点」这套印刷语言。旧字段全部保留，老调用方无需改签名。
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

        [Header("硬边偏移阴影 · 印刷感（P10）")]
        [Tooltip("漫画风核心：实心墨色阴影，不做模糊。悬停缩到 3px，按下归零")]
        [SerializeField] private bool hardShadow;

        [SerializeField] private Vector2 shadowOffset = new Vector2(6f, -6f);

        [SerializeField] private Color shadowColor = new Color(0.102f, 0.102f, 0.102f, 1f);

        [Tooltip("外扩留白：保证偏移阴影不会被 Rect 裁掉")]
        [SerializeField, Range(0f, 32f)] private float shadowPadding = 8f;

        [Header("半调网点（P10）")]
        [SerializeField] private bool halftone;

        [SerializeField, Range(4f, 24f)] private float halftoneSpacing = 8f;

        [SerializeField, Range(0.5f, 4f)] private float halftoneDotRadius = 1.3f;

        [SerializeField, Range(0f, 1f)] private float halftoneAlpha = 0.14f;

        [SerializeField, Range(0f, 90f)] private float halftoneAngle = 0f;

        [Tooltip("网点顶点上限：超过就不再铺点，避免移动端 UI 顶点爆量")]
        [SerializeField, Range(40, 400)] private int halftoneMaxDots = 220;

        [Header("对话气泡尖角（P10）")]
        [SerializeField] private bool speechTail;

        [SerializeField] private Vector2 tailSize = new Vector2(20f, 26f);

        [Tooltip("尖角方向（度）：270=向下，90=向上，180=向左，0=向右")]
        [SerializeField, Range(-180f, 360f)] private float tailAngle = 270f;

        private readonly Vector3[] _outerRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _innerRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _rimRing = new Vector3[MaxRingPoints];
        private readonly Vector3[] _glossPolygon = new Vector3[64];
        private readonly Vector3[] _shadowRing = new Vector3[MaxRingPoints];

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

            hardShadow = style.comicPanel;
            shadowOffset = style.shadowOffset;
            shadowPadding = style.shadowPadding;
            shadowColor = style.borderColor;
            halftone = style.halftone;
            speechTail = style.speechTail;

            color = fill;
            Refresh();
        }

        // ---------- 漫画风公共 API（P10） ----------

        /// <summary>
        /// 在 owner 下新建一个铺满的漫画底板。
        ///
        /// 为什么需要它：Unity 的 <see cref="Graphic"/> 带 [DisallowMultipleComponent]，
        /// 同一个物体上已经有 Image 时再 AddComponent&lt;UIInkShapeGraphic&gt; 会**静默返回 null**
        /// （实测：按钮换肤后什么都没发生，还不报错）。所以统一改成「子物体铺底」，
        /// 原 Image 只负责射线/过渡，视觉交给这块底板。
        /// </summary>
        public static UIInkShapeGraphic CreateChildBackground(RectTransform owner, string name = "ComicBackground")
        {
            if (owner == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            var rt = (RectTransform)go.transform;
            rt.SetParent(owner, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();

            var graphic = go.GetComponent<UIInkShapeGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }

        /// <summary>实心偏移阴影开关（漫画印刷感的核心）。</summary>
        public bool HardShadow
        {
            get => hardShadow;
            set
            {
                if (hardShadow == value) return;
                hardShadow = value;
                Refresh();
            }
        }

        public Vector2 ShadowOffset
        {
            get => shadowOffset;
            set
            {
                if (shadowOffset == value) return;
                shadowOffset = value;
                Refresh();
            }
        }

        public Color ShadowColor
        {
            get => shadowColor;
            set
            {
                if (shadowColor == value) return;
                shadowColor = value;
                Refresh();
            }
        }

        public bool Halftone
        {
            get => halftone;
            set
            {
                if (halftone == value) return;
                halftone = value;
                Refresh();
            }
        }

        /// <summary>旧 Inkwave 斜切高光透明度。漫画模式必须为 0（规范禁止渐变）。</summary>
        public float GlossAlpha
        {
            get => glossAlpha;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(glossAlpha, clamped)) return;
                glossAlpha = clamped;
                drawGloss = clamped > 0.001f;
                Refresh();
            }
        }

        public bool DrawGloss
        {
            get => drawGloss;
            set
            {
                if (drawGloss == value) return;
                drawGloss = value;
                Refresh();
            }
        }

        /// <summary>内侧白色墨沿透明度。漫画模式必须为 0。</summary>
        public float InnerRimAlpha
        {
            get => innerRimAlpha;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(innerRimAlpha, clamped)) return;
                innerRimAlpha = clamped;
                Refresh();
            }
        }

        /// <summary>网点透明度实时调节：悬停时调用这里做 Halftone Reveal，无需重建层级。</summary>
        public float HalftoneAlpha
        {
            get => halftoneAlpha;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(halftoneAlpha, clamped)) return;
                halftoneAlpha = clamped;
                Refresh();
            }
        }

        public bool SpeechTail
        {
            get => speechTail;
            set
            {
                if (speechTail == value) return;
                speechTail = value;
                Refresh();
            }
        }

        public Vector2 TailSize
        {
            get => tailSize;
            set
            {
                if (tailSize == value) return;
                tailSize = value;
                Refresh();
            }
        }

        public float TailAngle
        {
            get => tailAngle;
            set
            {
                if (Mathf.Approximately(tailAngle, value)) return;
                tailAngle = value;
                Refresh();
            }
        }

        /// <summary>
        /// 一键套漫画观感：粗墨边 + 硬阴影 + 可选网点 + 关掉渐变高光/内沿。
        /// 供主题安装器与各屏快速换肤用；不传参数时取 ComicStyle 的静态 token。
        /// </summary>
        public void ApplyComic(Color fill, bool dots = true, float border = ComicStyle.Border,
            Vector2? offset = null)
        {
            shape = shape == UIInkShape.Blob ? UIInkShape.RoundedRect : shape;
            borderWidth = border;
            borderColor = ComicStyle.Ink;
            cornerRadius = Mathf.Min(cornerRadius, ComicStyle.CornerSharp + 1f);
            wobbleAmplitude = 0f;
            glossAlpha = 0f;
            drawGloss = false;
            innerRimAlpha = 0f;
            hardShadow = true;
            shadowOffset = offset ?? new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            shadowColor = ComicStyle.Ink;
            halftone = dots;
            halftoneSpacing = ComicStyle.HalftoneSpacing;
            halftoneDotRadius = ComicStyle.HalftoneDotRadius;
            halftoneAlpha = ComicStyle.HalftoneAlpha;
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
            bool isBubble = shape == UIInkShape.SpeechBubble;
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

            float effectiveRadius = isBubble
                ? Mathf.Max(cornerRadius, ComicStyle.CornerBubble)
                : cornerRadius;

            float minHalf = Mathf.Min(rect.width, rect.height) * 0.5f;
            float border = Mathf.Clamp(borderWidth, 0f, Mathf.Max(0f, minHalf - 1f));
            float rimWidth = innerRimWidth > 0.01f ? innerRimWidth : 2f;
            float rim = border > 0.01f && innerRimAlpha > 0.001f
                ? Mathf.Clamp(rimWidth, 0f, border)
                : 0f;

            BuildRing(_outerRing, count, rect, 0f, perCorner, effectiveRadius);
            BuildRing(_innerRing, count, rect, border, perCorner, effectiveRadius);
            if (rim > 0.001f)
            {
                BuildRing(_rimRing, count, rect, Mathf.Min(border + rim, minHalf - 1f), perCorner, effectiveRadius);
            }
            else
            {
                System.Array.Copy(_innerRing, _rimRing, count);
            }

            Vector2 appliedShadow = Vector2.zero;
            if (hardShadow && (Mathf.Abs(shadowOffset.x) > 0.01f || Mathf.Abs(shadowOffset.y) > 0.01f))
            {
                // ① 硬边阴影：整块形状平移复制，实心墨色，不模糊。
                appliedShadow = shadowOffset;
                Rect shadowRect = new Rect(
                    rect.x + appliedShadow.x,
                    rect.y + appliedShadow.y,
                    rect.width,
                    rect.height);
                BuildRing(_shadowRing, count, shadowRect, 0f, perCorner, effectiveRadius);
                AddPolygonFan(vh, shadowRect.center, _shadowRing, count, shadowColor);
            }

            bool drawTail = speechTail || isBubble;
            if (drawTail)
            {
                // ② 气泡尖角画在面板之下：底边被面板盖住，只露出外面的三角。
                AddSpeechTail(vh, rect, border, appliedShadow);
            }

            // ③ 填充
            AddPolygonFan(vh, rect.center, _rimRing, count, color);

            // ④ 半调网点（画在填充之上、墨线之下，永远不会压住边框）
            if (halftone && halftoneAlpha > 0.001f)
            {
                AddHalftone(vh, rect, _rimRing, count);
            }

            // ⑤ 粗墨线边框
            if (border > 0.001f)
            {
                AddRingStrip(vh, _outerRing, _innerRing, count, borderColor);
            }

            // ⑥ 旧 Inkwave 内沿墨光（漫画模式下 innerRimAlpha=0，自动跳过）
            if (rim > 0.001f)
            {
                AddRingStrip(vh, _innerRing, _rimRing, count, new Color(1f, 1f, 1f, innerRimAlpha));
            }

            // ⑦ 旧的斜切高光（漫画模式禁止渐变，glossAlpha=0）
            if (drawGloss && glossAlpha > 0.001f)
            {
                AddGloss(vh, rect, Mathf.Clamp01(glossAlpha));
            }
        }

        private void BuildRing(Vector3[] dst, int count, Rect rect, float inset, int perCorner, float radius)
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
                BuildRoundedRing(dst, inner, Mathf.Max(0.01f, radius - inset), perCorner);
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

        /// <summary>
        /// 对话气泡尖角：只画露在面板外的那一段。
        /// 底边收进面板内部，因此面板的填充与墨线会盖住接缝，不会出现「横切一刀」。
        /// </summary>
        private void AddSpeechTail(VertexHelper vh, Rect rect, float border, Vector2 shadow)
        {
            float angle = tailAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float height = Mathf.Max(8f, tailSize.y);
            float halfBase = Mathf.Max(6f, tailSize.x * 0.5f);

            // 沿 dir 方向从中心到矩形边缘的距离
            float extent = Mathf.Abs(dir.x) * rect.width * 0.5f + Mathf.Abs(dir.y) * rect.height * 0.5f;
            if (extent < 4f) return;

            Vector2 center = rect.center;
            Vector2 baseCenter = center + dir * Mathf.Max(0f, extent - border - 6f);
            Vector2 tip = center + dir * (extent + height);

            Vector2 b0 = baseCenter + perp * halfBase;
            Vector2 b1 = baseCenter - perp * halfBase;

            Color fill = color;

            if (shadow.sqrMagnitude > 0.0001f)
            {
                AddTriangle(vh, b0 + shadow, b1 + shadow, tip + shadow, shadowColor);
            }

            AddTriangle(vh, b0, b1, tip, borderColor);

            float inset = Mathf.Max(1.5f, border * 1.35f);
            float innerHalf = Mathf.Max(1f, halfBase - inset);
            Vector2 innerBase = baseCenter + dir * inset * 0.5f;
            Vector2 innerTip = center + dir * Mathf.Max(inset, extent + height - inset);
            AddTriangle(vh, innerBase + perp * innerHalf, innerBase - perp * innerHalf, innerTip, fill);
        }

        private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>
        /// 半调网点：在填充区域内按固定网格铺小圆点，模拟漫画印刷。
        /// 用多边形内外判定裁掉圆角外的点；顶点数有硬上限，移动端不会爆。
        /// </summary>
        private void AddHalftone(VertexHelper vh, Rect rect, Vector3[] fillRing, int ringCount)
        {
            float spacing = Mathf.Max(4f, halftoneSpacing);
            float rad = Mathf.Clamp(halftoneDotRadius, 0.5f, spacing * 0.45f);
            float angle = halftoneAngle * Mathf.Deg2Rad;
            Vector2 axisX = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spacing;
            Vector2 axisY = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * spacing;

            Vector2 center = rect.center;
            int halfSteps = Mathf.CeilToInt((rect.width + rect.height) * 0.5f / spacing) + 1;
            halfSteps = Mathf.Clamp(halfSteps, 1, 28);

            // 网点颜色按底色亮度自动反转：纸面/队色底用墨点，墨黑底用白点，
            // 保证网点永远看得见但不会盖过主体文字。
            Color dotColor = ComicStyle.RelativeLuminance(color) < 0.32f ? Color.white : ComicStyle.Ink;
            Color tint = new Color(dotColor.r, dotColor.g, dotColor.b, halftoneAlpha);

            int drawn = 0;
            int maxDots = Mathf.Clamp(halftoneMaxDots, 1, 400);

            for (int u = -halfSteps; u <= halfSteps && drawn < maxDots; u++)
            {
                for (int v = -halfSteps; v <= halfSteps && drawn < maxDots; v++)
                {
                    Vector2 p = center + axisX * u + axisY * v;
                    if (p.x < rect.xMin || p.x > rect.xMax || p.y < rect.yMin || p.y > rect.yMax) continue;
                    if (!PointInPolygon(p, fillRing, ringCount)) continue;

                    AddDot(vh, p, rad, tint);
                    drawn++;
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

        private static bool PointInPolygon(Vector2 p, Vector3[] ring, int count)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float yi = ring[i].y;
                float yj = ring[j].y;
                if ((yi > p.y) == (yj > p.y)) continue;

                float dy = yj - yi;
                if (Mathf.Abs(dy) < 1e-6f) continue;

                float x = ring[i].x + (p.y - yi) / dy * (ring[j].x - ring[i].x);
                if (p.x < x) inside = !inside;
            }

            return inside;
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