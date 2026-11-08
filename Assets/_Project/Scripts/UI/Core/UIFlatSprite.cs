using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 几何大胆风专用「纯色块精灵」（P14-H；2026-11 圆角化补胶囊/正圆）。
    ///
    /// 为什么需要它：<see cref="Image"/> 只有在 `sprite != null` 时才走 Filled 分支
    /// （`sprite == null` 会退化成整块 Simple，fillAmount 完全失效）。
    ///
    /// - <see cref="White"/>：1×1 纯白，直角色块/分隔线/全屏特效层用；
    /// - <see cref="Circle"/> / <see cref="Pill"/>：白色正圆/胶囊（两端半圆、半径 = 高度一半），
    ///   进度条、滑轨、圆点用。按纵横比进程内缓存，条形尺寸变化时重新取用即可。
    ///
    /// 环状弧不用精灵：`Image.Filled + Radial360` 需要带 alpha 的圆环贴图，运行时精灵在
    /// 即时 Canvas 重建里会被画成整块矩形（Simple 正常、Radial360 失效），
    /// 所以环状仪表统一走网格绘制的 <see cref="UIArcRingGraphic"/>。
    /// </summary>
    public static class UIFlatSprite
    {
        private static Sprite _white;
        private static readonly Dictionary<int, Sprite> PillCache = new Dictionary<int, Sprite>();

        /// <summary>1×1 纯白 Sprite（进程内唯一，HideAndDontSave）。</summary>
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;

                Texture2D texture = Texture2D.whiteTexture;
                _white = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    1f,
                    0,
                    SpriteMeshType.FullRect);
                _white.name = "UIFlatWhite";
                _white.hideFlags = HideFlags.HideAndDontSave;
                return _white;
            }
        }

        /// <summary>白色正圆 Sprite：小圆点、角标、圆形滑块用（正方形底、圆形 alpha）。</summary>
        public static Sprite Circle => Pill(1f);

        /// <summary>
        /// 白色胶囊 Sprite（两端半圆、半径 = 高度一半）：进度条/滑轨的轨道与填充用。
        /// aspect = 宽/高；按 0.25 步长进程内缓存，同比例的条共享一张纹理。
        /// </summary>
        public static Sprite Pill(float aspect)
        {
            aspect = Mathf.Clamp(aspect, 1f, 64f);
            int key = Mathf.RoundToInt(aspect * 4f);
            if (PillCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            const int height = 64;
            int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), height, 4096);
            float radius = height * 0.5f;
            float straight = Mathf.Max(0f, width - radius * 2f);

            // mipmap + 三线性：条高 12~20px 时贴图要缩 3~5 倍，没有 mip 的 bilinear 采样
            // 会把半圆端头采成台阶（HUD 横条「锯齿感」的主因）。
            var texture = new Texture2D(width, height, TextureFormat.ARGB32, true)
            {
                name = $"UIFlatPill{key}",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float dy = y + 0.5f - radius;
                for (int x = 0; x < width; x++)
                {
                    float dx = x + 0.5f;
                    float dist;
                    if (dx < radius)
                    {
                        float ox = radius - dx;
                        dist = Mathf.Sqrt(ox * ox + dy * dy);
                    }
                    else if (dx > radius + straight)
                    {
                        float ox = dx - radius - straight;
                        dist = Mathf.Sqrt(ox * ox + dy * dy);
                    }
                    else
                    {
                        dist = Mathf.Abs(dy);
                    }

                    // 1px 抗锯齿边，其余纯硬边（零模糊红线只针对阴影/渐变，这里是形状轮廓）。
                    float alpha = dist <= radius - 0.5f ? 1f
                        : dist >= radius + 0.5f ? 0f
                        : (radius + 0.5f - dist);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true, true); // 生成 mip 链后再释放 CPU 侧像素

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = $"UIFlatPillSprite{key}";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            PillCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 把已有 Image 改成纯平色块。
        /// filled = true 时保留横向填充（进度条），否则整块铺满。
        /// </summary>
        public static void MakeFlat(Image image, bool filled = false,
            Image.OriginHorizontal origin = Image.OriginHorizontal.Left)
        {
            if (image == null) return;

            image.sprite = White;
            image.preserveAspect = false;
            image.maskable = true;

            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)origin;
                image.fillClockwise = true;
                image.fillAmount = Mathf.Clamp01(image.fillAmount);
            }
            else
            {
                image.type = Image.Type.Simple;
            }
        }

        /// <summary>
        /// 把已有 Image 改成胶囊色块（进度条/滑轨轨道与填充；两端半圆随高度自适应）。
        /// filled = true 时保留横向填充。条形尺寸后续会变的请改用 <see cref="UIPillImage"/>。
        /// </summary>
        public static void MakePill(Image image, bool filled = false,
            Image.OriginHorizontal origin = Image.OriginHorizontal.Left)
        {
            if (image == null) return;

            Rect rect = image.rectTransform.rect;
            float aspect = rect.width >= 1f && rect.height >= 1f ? rect.width / rect.height : 4f;
            image.sprite = Pill(aspect);
            image.preserveAspect = false;
            image.maskable = true;

            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)origin;
                image.fillClockwise = true;
                image.fillAmount = Mathf.Clamp01(image.fillAmount);
            }
            else
            {
                image.type = Image.Type.Simple;
            }
        }
    }
}
