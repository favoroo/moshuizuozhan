using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>液体容器的截面形状。</summary>
    public enum UILiquidClipShape
    {
        /// <summary>圆形 / 椭圆：大招液体球。</summary>
        Circle = 0,

        /// <summary>圆角矩形：墨水计量管。</summary>
        RoundedRect = 1,
    }

    /// <summary>
    /// 纯网格绘制的「波浪液体」（P4-C）：不需要任何 Sprite / 材质 / Mask。
    ///
    /// 逐列（column strip）生成液体：
    /// - 顶边是双正弦叠加的液面，带一条更亮的后层浪冠；
    /// - 底边按 <see cref="UILiquidClipShape"/> 的截面收边（圆形弧 / 圆角矩形圆角）；
    /// - 表面叠一条白色亮边，液体内部有几颗上升气泡。
    ///
    /// 液面高度用 <see cref="UISpringSettings"/> 追随目标，涨落天然带惯性；
    /// 追随速度越快，浪幅越高（slosh），所以开火耗墨、涂地涨能时液面会真实地晃一下。
    /// </summary>
    [AddComponentMenu("P1/UI/Liquid Fill")]
    public class UILiquidFillGraphic : MaskableGraphic
    {
        private const int MaxColumns = 64;
        private const int MaxBubbles = 6;

        [Header("容器截面")]
        [SerializeField] private UILiquidClipShape clipShape = UILiquidClipShape.Circle;

        [Tooltip("圆角矩形截面的圆角半径（像素）")]
        [SerializeField, Range(0f, 128f)] private float cornerRadius = 16f;

        [Tooltip("液体与容器内沿的留白，露出一圈容器底色更像玻璃罐")]
        [SerializeField, Range(0f, 8f)] private float edgeInset = 3f;

        [Header("液面")]
        [SerializeField, Range(0f, 1f)] private float fill01 = 0.5f;

        [SerializeField] private UISpringSettings fillSpring = UISpringSettings.Follow;

        [Tooltip("横向绘制的列数：越多弧线越顺滑")]
        [SerializeField, Range(12, 64)] private int columns = 40;

        [Tooltip("液面最大起伏（相对容器高度的比例）")]
        [SerializeField, Range(0f, 0.2f)] private float maxWaveAmplitude = 0.035f;

        [Tooltip("横向完整波数")]
        [SerializeField, Range(0.2f, 4f)] private float waveCycles = 1.35f;

        [Tooltip("液面相位推进速度（弧度/秒）")]
        [SerializeField, Range(0f, 8f)] private float waveSpeed = 1.4f;

        [Tooltip("液面追逐速度带来的额外浪高（0 = 关闭晃动加成）")]
        [SerializeField, Range(0f, 1f)] private float sloshGain = 1f;

        [Tooltip("后层浪冠颜色（一般填队伍亮色），alpha 会被整体透明度相乘")]
        [SerializeField] private Color crestColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("后层浪冠高出前层液面的比例（制造双色波峰）")]
        [SerializeField, Range(0f, 0.08f)] private float crestLift = 0.018f;

        [Tooltip("液面白色亮边强度")]
        [SerializeField, Range(0f, 1f)] private float surfaceShine = 0.42f;

        [Header("气泡")]
        [SerializeField, Range(0, 6)] private int bubbleCount = 3;

        [SerializeField, Range(0.004f, 0.08f)] private float bubbleRadius = 0.026f;

        [SerializeField, Range(0.05f, 1.5f)] private float bubbleSpeed = 0.34f;

        [SerializeField, Range(0f, 1f)] private float bubbleAlpha = 0.45f;

        [SerializeField] private bool animate = true;

        private UISpringFloat _display;
        private float _time;
        private float _sloshBoost;
        private float _renderSlosh;

        /// <summary>当前显示液面（含弹簧惯性），0..1。</summary>
        public float DisplayFill => _display.Value;

        /// <summary>目标液面，0..1。</summary>
        public float TargetFill => fill01;

        public UILiquidClipShape ClipShape
        {
            get => clipShape;
            set
            {
                if (clipShape == value) return;
                clipShape = value;
                SetVerticesDirty();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _display.Set(Mathf.Clamp01(fill01));
            _time = 0f;
            SetVerticesDirty();
        }

        /// <summary>设置目标液面。snap=true 时跳过弹簧直接落位（初始化 / 重置用）。</summary>
        public void SetFill(float value, bool snap = false)
        {
            fill01 = Mathf.Clamp01(value);
            if (snap)
            {
                _display.Set(fill01);
                _sloshBoost = 0f;
                _renderSlosh = 0f;
                SetVerticesDirty();
            }
        }

        /// <summary>液面额外晃动一记（涨能、补墨、受击时调用）。</summary>
        public void Splash(float strength = 0.6f)
        {
            _sloshBoost = Mathf.Clamp01(_sloshBoost + Mathf.Clamp01(strength));
        }

        /// <summary>单独设置后层浪冠颜色（队伍切换时由视图组件调用）。</summary>
        public void SetCrestColor(Color value)
        {
            if (crestColor == value) return;
            crestColor = value;
            SetVerticesDirty();
        }

        /// <summary>一次性写入外观参数（程序化装配用；不改液面颜色，颜色走 <see cref="Graphic.color"/>）。</summary>
        public void Configure(UILiquidClipShape shape, float corner, float inset, Color crest,
            float waveAmplitude, float waveSpeedValue, float cycles, int columnCount,
            int bubbles, float bubbleSize, UISpringSettings spring, float startFill)
        {
            clipShape = shape;
            cornerRadius = Mathf.Max(0f, corner);
            edgeInset = Mathf.Max(0f, inset);
            crestColor = crest;
            maxWaveAmplitude = Mathf.Clamp(waveAmplitude, 0f, 0.2f);
            waveSpeed = Mathf.Max(0f, waveSpeedValue);
            waveCycles = Mathf.Clamp(cycles, 0.2f, 4f);
            columns = Mathf.Clamp(columnCount, 12, MaxColumns);
            bubbleCount = Mathf.Clamp(bubbles, 0, MaxBubbles);
            bubbleRadius = Mathf.Clamp(bubbleSize, 0.004f, 0.08f);
            fillSpring = spring;
            fill01 = Mathf.Clamp01(startFill);
            _display.Set(fill01);
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!animate) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _time += dt;

            _display.Step(fill01, fillSpring, dt, 4e-4f, 3e-3f);

            _sloshBoost = Mathf.MoveTowards(_sloshBoost, 0f, dt * 1.8f);
            float speedSlosh = Mathf.Clamp01(Mathf.Abs(_display.Velocity) * 0.30f * sloshGain);
            _renderSlosh = Mathf.Clamp01(Mathf.Max(_sloshBoost, speedSlosh));

            // 空罐时液面在地板以下，没有可见几何，不必每帧重建网格
            if (fill01 > 0.001f || _display.Value > 0.001f || _renderSlosh > 0.001f)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 6f || rect.height < 6f) return;

            float halfW = rect.width * 0.5f - edgeInset;
            float halfH = rect.height * 0.5f - edgeInset;
            if (halfW < 2f || halfH < 2f) return;

            float cx = rect.center.x;
            float cy = rect.center.y;
            float bottom = cy - halfH;
            float top = cy + halfH;

            float fill = Mathf.Clamp01(_display.Value);
            if (fill <= 0.0005f) return;

            int cols = Mathf.Clamp(columns, 12, MaxColumns);
            float amp = Mathf.Max(0.0002f, maxWaveAmplitude * rect.height) * (1f + _renderSlosh * 1.4f);
            float phase = _time * waveSpeed;
            float surface = HudGaugeMath.SurfaceBase(bottom, halfH * 2f, fill);

            // 1) 后层浪冠：更高、更亮、相位错开，从前层液面后露出一道浅色波峰
            AddLiquidBody(vh, cx, cy, halfW, halfH, bottom, top, cols,
                surface + crestLift * rect.height,
                phase + 1.2f, amp * 0.85f, CrestTint());

            // 2) 前层液体主体
            AddLiquidBody(vh, cx, cy, halfW, halfH, bottom, top, cols,
                surface, phase, amp, color);

            // 3) 液面白色亮边（贴在波浪顶缘下方的一条细带）
            AddSurfaceShine(vh, cx, cy, halfW, halfH, bottom, top, cols, surface, phase, amp);

            // 4) 气泡
            AddBubbles(vh, rect, cx, cy, halfW, halfH, bottom, cols, surface, phase, amp);
        }

        private Color CrestTint()
        {
            Color c = crestColor;
            c.a *= color.a;
            return c;
        }

        private void AddLiquidBody(VertexHelper vh, float cx, float cy, float halfW, float halfH,
            float bottom, float top, int cols, float surface, float phase, float amp, Color tint)
        {
            if (tint.a <= 0.002f) return;

            float width = halfW * 2f;
            for (int i = 0; i < cols; i++)
            {
                float x0 = cx - halfW + (i / (float)cols) * width;
                float x1 = cx - halfW + ((i + 1f) / cols) * width;

                if (!TryColumnBounds(x0, cx, cy, halfW, halfH, bottom, top, out float b0, out float t0)) continue;
                if (!TryColumnBounds(x1, cx, cy, halfW, halfH, bottom, top, out float b1, out float t1)) continue;

                float s0 = Mathf.Clamp(surface + HudGaugeMath.WaveOffset(x0, cx, width, phase, waveCycles, amp), b0, t0);
                float s1 = Mathf.Clamp(surface + HudGaugeMath.WaveOffset(x1, cx, width, phase, waveCycles, amp), b1, t1);

                if (s0 - b0 < 0.2f && s1 - b1 < 0.2f) continue;

                AddQuad(vh,
                    new Vector3(x0, b0), new Vector3(x0, s0),
                    new Vector3(x1, s1), new Vector3(x1, b1), tint);
            }
        }

        private void AddSurfaceShine(VertexHelper vh, float cx, float cy, float halfW, float halfH,
            float bottom, float top, int cols, float surface, float phase, float amp)
        {
            float alpha = surfaceShine;
            if (alpha <= 0.002f) return;

            Color tint = new Color(1f, 1f, 1f, alpha * color.a);
            float width = halfW * 2f;
            float thickness = Mathf.Max(1.4f, (top - bottom) * 0.016f);

            for (int i = 0; i < cols; i++)
            {
                float x0 = cx - halfW + (i / (float)cols) * width;
                float x1 = cx - halfW + ((i + 1f) / cols) * width;

                if (!TryColumnBounds(x0, cx, cy, halfW, halfH, bottom, top, out float b0, out float t0)) continue;
                if (!TryColumnBounds(x1, cx, cy, halfW, halfH, bottom, top, out float b1, out float t1)) continue;

                float s0 = Mathf.Clamp(surface + HudGaugeMath.WaveOffset(x0, cx, width, phase, waveCycles, amp), b0, t0);
                float s1 = Mathf.Clamp(surface + HudGaugeMath.WaveOffset(x1, cx, width, phase, waveCycles, amp), b1, t1);
                if (s0 - b0 < thickness && s1 - b1 < thickness) continue;

                float lo0 = Mathf.Max(b0, s0 - thickness);
                float lo1 = Mathf.Max(b1, s1 - thickness);

                AddQuad(vh,
                    new Vector3(x0, lo0), new Vector3(x0, s0),
                    new Vector3(x1, s1), new Vector3(x1, lo1), tint);
            }
        }

        private void AddBubbles(VertexHelper vh, Rect rect, float cx, float cy, float halfW, float halfH,
            float bottom, int cols, float surface, float phase, float amp)
        {
            int count = Mathf.Clamp(bubbleCount, 0, MaxBubbles);
            if (count <= 0 || bubbleAlpha <= 0.002f) return;

            float radius = Mathf.Max(0.6f, bubbleRadius * rect.height);
            float width = halfW * 2f;

            for (int i = 0; i < count; i++)
            {
                float seed = (i + 0.5f) * 0.618034f;
                float cycle = Mathf.Repeat(_time * bubbleSpeed + seed, 1f);
                float bx = cx + Mathf.Sin(seed * Mathf.PI * 2f + _time * 0.6f) * halfW * 0.55f;

                if (!TryColumnBounds(bx, cx, cy, halfW, halfH, bottom, cy + halfH, out float columnBottom, out _)) continue;

                float surfaceAtBubble = Mathf.Clamp(
                    surface + HudGaugeMath.WaveOffset(bx, cx, width, phase, waveCycles, amp),
                    columnBottom, cy + halfH);

                float lowY = columnBottom + radius * 1.6f;
                float highY = surfaceAtBubble - radius * 1.6f;
                if (highY - lowY < radius) continue;

                float y = Mathf.Lerp(lowY, highY, cycle);
                float fade = Mathf.Sin(cycle * Mathf.PI);
                Color tint = new Color(1f, 1f, 1f, bubbleAlpha * fade * color.a);

                AddDisc(vh, new Vector3(bx, y), radius, tint, 8);
            }
        }

        private bool TryColumnBounds(float x, float cx, float cy, float halfW, float halfH,
            float bottom, float top, out float yMin, out float yMax)
        {
            if (clipShape == UILiquidClipShape.Circle)
            {
                float dy = HudGaugeMath.CircleColumnHalfHeight(x, cx, halfW, halfH);
                if (dy < 0.4f)
                {
                    yMin = yMax = 0f;
                    return false;
                }

                yMin = cy - dy;
                yMax = cy + dy;
                return yMax - yMin > 0.8f;
            }

            float inset = HudGaugeMath.RoundedRectBottomInset(x, cx, halfW, Mathf.Max(0f, cornerRadius - edgeInset));
            yMin = bottom + inset;
            yMax = top;
            return yMax - yMin > 0.8f;
        }

        private static void AddQuad(VertexHelper vh, Vector3 bottomLeft, Vector3 topLeft,
            Vector3 topRight, Vector3 bottomRight, Color tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(bottomLeft, tint, Vector2.zero);
            vh.AddVert(topLeft, tint, Vector2.zero);
            vh.AddVert(topRight, tint, Vector2.zero);
            vh.AddVert(bottomRight, tint, Vector2.zero);

            // UI 默认材质是 Cull Off，顶点环绕方向不影响可见性
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private static void AddDisc(VertexHelper vh, Vector3 center, float radius, Color tint, int segments)
        {
            int centerIndex = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vh.AddVert(center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius,
                    tint, Vector2.zero);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = centerIndex + 1 + i;
                int b = centerIndex + 1 + (i + 1) % segments;
                vh.AddTriangle(centerIndex, a, b);
            }
        }
    }
}
