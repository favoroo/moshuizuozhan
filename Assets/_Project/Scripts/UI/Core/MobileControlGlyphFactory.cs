using System.Collections.Generic;
using UnityEngine;

namespace P1.UI
{
    /// <summary>移动端触控键的功能字形（纯几何剪影，颜色由 Image.color 染色）。</summary>
    public enum MobileControlGlyph
    {
        /// <summary>射击：准星（圆环 + 四向刻度 + 中心点）。</summary>
        Crosshair,
        /// <summary>移动：四向箭头（摇杆把手）。</summary>
        MovePad,
        /// <summary>潜行：墨滴。</summary>
        InkDrop,
        /// <summary>跳跃：上箭头 + 起跳线。</summary>
        Jump,
        /// <summary>手雷：弹体 + 引线火花。</summary>
        Bomb,
        /// <summary>大招：下砸箭头 + 冲击线。</summary>
        Slam
    }

    /// <summary>
    /// 移动端动作键的程序化几何字形工厂。
    ///
    /// 为什么不用 Art/Sprites/Icons 里的图标：那套是规则说明插画（深描边 + 内部细节线），
    /// 缩到 60~80px 后细节互相吃掉，看起来「一个圈 / 一坨」，功能语义读不出来。
    /// 动作键需要的是单一、粗壮、1 秒内能认出的剪影，所以这里用 SDF 直接画：
    /// 128px 出片 + 1px 距离场抗锯齿，纯白 RGB + alpha，运行时由 Image.color 染成功能色。
    ///
    /// 所有坐标都落在 [-1, 1] 的标准方形内（y 向上），字形主体控制在 |p| ≤ 0.86，
    /// 四周留出安全边距，换 DPI / 自定义缩放都不会被裁切。
    /// </summary>
    public static class MobileControlGlyphFactory
    {
        private const int Size = 128;

        /// <summary>一像素在 [-1,1] 坐标系里的宽度，用于 1px 距离场抗锯齿。</summary>
        private const float PixelStep = 2f / Size;

        private static readonly Dictionary<MobileControlGlyph, Sprite> s_cache =
            new Dictionary<MobileControlGlyph, Sprite>();

        /// <summary>取字形精灵（首次调用生成并缓存；纯白可染色）。</summary>
        public static Sprite Get(MobileControlGlyph glyph)
        {
            if (s_cache.TryGetValue(glyph, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite built = Build(glyph);
            s_cache[glyph] = built;
            return built;
        }

        private static Sprite Build(MobileControlGlyph glyph)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "MobileGlyph_" + glyph,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var colors = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(
                        (x + 0.5f) * PixelStep - 1f,
                        (y + 0.5f) * PixelStep - 1f);

                    float distance = Distance(glyph, p);
                    float alpha = Mathf.Clamp01(0.5f - distance / PixelStep);
                    colors[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            tex.SetPixels32(colors);
            tex.Apply(false, false);

            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = "MobileGlyph_" + glyph;
            return sprite;
        }

        private static float Distance(MobileControlGlyph glyph, Vector2 p)
        {
            switch (glyph)
            {
                case MobileControlGlyph.Crosshair:
                    return Crosshair(p);
                case MobileControlGlyph.MovePad:
                    return MovePad(p);
                case MobileControlGlyph.InkDrop:
                    return InkDrop(p);
                case MobileControlGlyph.Jump:
                    return Jump(p);
                case MobileControlGlyph.Bomb:
                    return Bomb(p);
                case MobileControlGlyph.Slam:
                    return Slam(p);
                default:
                    return 1f;
            }
        }

        /// <summary>准星：圆环 + 中心点 + 四向刻度。</summary>
        private static float Crosshair(Vector2 p)
        {
            float dist = Mathf.Abs(p.magnitude - 0.44f) - 0.08f; // 外环
            dist = Mathf.Min(dist, p.magnitude - 0.10f);         // 中心点
            dist = Mathf.Min(dist, Capsule(p, new Vector2(0f, 0.55f), new Vector2(0f, 0.80f), 0.07f));
            dist = Mathf.Min(dist, Capsule(p, new Vector2(0f, -0.55f), new Vector2(0f, -0.80f), 0.07f));
            dist = Mathf.Min(dist, Capsule(p, new Vector2(0.55f, 0f), new Vector2(0.80f, 0f), 0.07f));
            dist = Mathf.Min(dist, Capsule(p, new Vector2(-0.55f, 0f), new Vector2(-0.80f, 0f), 0.07f));
            return dist;
        }

        /// <summary>移动：四向箭头 + 中心点（比手柄图标更直白）。</summary>
        private static float MovePad(Vector2 p)
        {
            float dist = p.magnitude - 0.085f; // 中心点

            dist = Mathf.Min(dist, MoveArm(p, Vector2.up, Vector2.right));
            dist = Mathf.Min(dist, MoveArm(p, Vector2.down, Vector2.left));
            dist = Mathf.Min(dist, MoveArm(p, Vector2.right, Vector2.down));
            dist = Mathf.Min(dist, MoveArm(p, Vector2.left, Vector2.up));
            return dist;
        }

        private static float MoveArm(Vector2 p, Vector2 dir, Vector2 perp)
        {
            // 箭杆 0.10 → 0.50，箭头 0.50 → 0.86
            float dist = Capsule(p, dir * 0.10f, dir * 0.50f, 0.09f);
            Vector2 baseCenter = dir * 0.50f;
            return Mathf.Min(dist, Triangle(
                p,
                dir * 0.86f,
                baseCenter + perp * 0.23f,
                baseCenter - perp * 0.23f));
        }

        /// <summary>潜行：墨滴（圆 + 三角尖，底边与圆在 y=-0.02 处对齐避免接缝）。</summary>
        private static float InkDrop(Vector2 p)
        {
            float body = (p - new Vector2(0f, -0.16f)).magnitude - 0.34f;
            float tip = Triangle(
                p,
                new Vector2(0f, 0.80f),
                new Vector2(-0.30f, -0.02f),
                new Vector2(0.30f, -0.02f));
            return Mathf.Min(body, tip);
        }

        /// <summary>跳跃：上箭头 + 起跳线。</summary>
        private static float Jump(Vector2 p)
        {
            float head = Triangle(
                p,
                new Vector2(0f, 0.80f),
                new Vector2(-0.40f, 0.12f),
                new Vector2(0.40f, 0.12f));
            float shaft = Box(p, new Vector2(0f, -0.16f), new Vector2(0.11f, 0.30f));
            float ground = Box(p, new Vector2(0f, -0.70f), new Vector2(0.34f, 0.06f));
            return Mathf.Min(head, Mathf.Min(shaft, ground));
        }

        /// <summary>手雷：弹体 + 斜引线 + 菱形火花。</summary>
        private static float Bomb(Vector2 p)
        {
            float body = (p - new Vector2(0f, -0.16f)).magnitude - 0.44f;
            float fuse = Capsule(p, new Vector2(0.18f, 0.20f), new Vector2(0.40f, 0.46f), 0.055f);
            Vector2 sparkP = new Vector2(0.46f, 0.52f);
            float spark = Mathf.Abs(p.x - sparkP.x) + Mathf.Abs(p.y - sparkP.y) - 0.11f;
            return Mathf.Min(body, Mathf.Min(fuse, spark));
        }

        /// <summary>大招：下砸箭头 + 冲击线。</summary>
        private static float Slam(Vector2 p)
        {
            float head = Triangle(
                p,
                new Vector2(0f, -0.58f),
                new Vector2(-0.44f, 0.10f),
                new Vector2(0.44f, 0.10f));
            float shaft = Box(p, new Vector2(0f, 0.32f), new Vector2(0.12f, 0.28f));
            float ground = Box(p, new Vector2(0f, -0.76f), new Vector2(0.46f, 0.06f));
            return Mathf.Min(head, Mathf.Min(shaft, ground));
        }

        // ── 2D 距离场基元（负值 = 形状内部）────────────────────────────

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        private static float Box(Vector2 p, Vector2 center, Vector2 halfSize)
        {
            Vector2 q = new Vector2(
                Mathf.Abs(p.x - center.x),
                Mathf.Abs(p.y - center.y)) - halfSize;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 e0 = b - a;
            Vector2 e1 = c - b;
            Vector2 e2 = a - c;
            Vector2 v0 = p - a;
            Vector2 v1 = p - b;
            Vector2 v2 = p - c;

            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));

            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(
                Vector2.Min(
                    new Vector2(Vector2.Dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                    new Vector2(Vector2.Dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                new Vector2(Vector2.Dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));

            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }
    }
}
