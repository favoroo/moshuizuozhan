using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 满能爆闪（P4-C）：一圈向外炸开的冲击波 + 放射状金色射线。
    ///
    /// 只在 <see cref="Play"/> 后的一小段时间内绘制，播完自动停笔，
    /// 因此常态零开销，不会和光圈、脉冲一起长期抢 UI 网格。
    /// </summary>
    [AddComponentMenu("P1/UI/Burst Flash")]
    public class UIBurstGraphic : MaskableGraphic
    {
        private const int RaySegments = 3;

        [SerializeField, Range(0.1f, 2f)] private float duration = 0.7f;

        [SerializeField, Range(3, 24)] private int rayCount = 10;

        [Tooltip("环形冲击波的起始/结束半径（相对容器短边的一半）")]
        [SerializeField, Range(0.1f, 1f)] private float ringStartRadius = 0.95f;

        [SerializeField, Range(0.5f, 2.5f)] private float ringEndRadius = 1.55f;

        [SerializeField, Range(0.5f, 20f)] private float ringThickness = 5f;

        [Tooltip("射线最远伸出的长度（相对容器短边的一半）")]
        [SerializeField, Range(0f, 1f)] private float rayLength = 0.45f;

        [SerializeField, Range(0.5f, 12f)] private float rayWidth = 3.5f;

        private float _timer;

        public bool IsPlaying => _timer > 0f;

        public void Configure(float seconds, int rays, Color burstColor)
        {
            duration = Mathf.Clamp(seconds, 0.1f, 2f);
            rayCount = Mathf.Clamp(rays, 3, 24);
            color = burstColor;
            SetVerticesDirty();
        }

        public void Play()
        {
            _timer = Mathf.Max(0.1f, duration);
            SetVerticesDirty();
        }

        private void Update()
        {
            if (_timer <= 0f) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _timer -= dt;
            if (_timer < 0f) _timer = 0f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_timer <= 0f) return;

            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 8f || rect.height < 8f) return;

            float maxRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float progress = 1f - Mathf.Clamp01(_timer / Mathf.Max(0.05f, duration));
            float easeOut = 1f - (1f - progress) * (1f - progress);
            float fade = Mathf.Clamp01(1f - progress);

            Vector2 center = rect.center;
            Color tint = new Color(color.r, color.g, color.b, color.a * fade);
            if (tint.a <= 0.002f) return;

            // 1) 冲击波环：向外扩散且变薄，对应 CSS 的 iw-sp-ring
            float ringRadius = Mathf.Lerp(ringStartRadius, ringEndRadius, easeOut) * maxRadius;
            float halfWidth = Mathf.Max(0.6f, ringThickness * (1f - progress * 0.8f)) * 0.5f;
            AddRing(vh, center, Mathf.Max(0.1f, ringRadius - halfWidth), ringRadius + halfWidth, 44, tint);

            // 2) 放射射线：每根依次延迟，形成一圈「刷」地铺开
            int rays = Mathf.Clamp(rayCount, 3, 24);
            for (int i = 0; i < rays; i++)
            {
                float delay = (i / (float)rays) * 0.18f;
                float local = Mathf.Clamp01((progress - delay) / Mathf.Max(0.01f, 1f - delay));
                if (local <= 0f) continue;

                float rayFade = Mathf.Sin(local * Mathf.PI);
                if (rayFade <= 0.01f) continue;

                float angle = (i / (float)rays) * 360f + 12f;
                float innerRadius = maxRadius * 0.85f;
                float outerRadius = innerRadius + maxRadius * rayLength * Mathf.Lerp(0.4f, 1f, local);

                Color rayTint = new Color(tint.r, tint.g, tint.b, tint.a * rayFade);
                AddRay(vh, center, angle, innerRadius, outerRadius, rayWidth * rayFade + 0.4f, rayTint);
            }
        }

        private static void AddRing(VertexHelper vh, Vector2 center, float inner, float outer,
            int segments, Color tint)
        {
            int baseIndex = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Deg2Rad * (i / (float)segments * 360f);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vh.AddVert(new Vector3(center.x + cos * inner, center.y + sin * inner, 0f), tint, Vector2.zero);
                vh.AddVert(new Vector3(center.x + cos * outer, center.y + sin * outer, 0f), tint, Vector2.zero);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = baseIndex + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }

        private static void AddRay(VertexHelper vh, Vector2 center, float angleDegrees,
            float inner, float outer, float width, Color tint)
        {
            float rad = angleDegrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 normal = new Vector2(-dir.y, dir.x);

            // 内窄外宽的锥形射线：外端宽 2 倍，视觉上像「炸开」而不是一根等宽棍子
            Vector2 innerHalf = normal * (width * 0.5f);
            Vector2 outerHalf = normal * width;
            Vector2 a = center + dir * inner;
            Vector2 b = center + dir * outer;

            int index = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x - innerHalf.x, a.y - innerHalf.y, 0f), tint, Vector2.zero);
            vh.AddVert(new Vector3(a.x + innerHalf.x, a.y + innerHalf.y, 0f), tint, Vector2.zero);
            vh.AddVert(new Vector3(b.x + outerHalf.x, b.y + outerHalf.y, 0f), tint, Vector2.zero);
            vh.AddVert(new Vector3(b.x - outerHalf.x, b.y - outerHalf.y, 0f), tint, Vector2.zero);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
