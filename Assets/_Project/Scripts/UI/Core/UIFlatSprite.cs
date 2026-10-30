using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 几何大胆风专用「纯色块精灵」（P14-H）。
    ///
    /// 为什么需要它：<see cref="Image"/> 只有在 `sprite != null` 时才走 Filled 分支
    /// （`sprite == null` 会退化成整块 Simple，fillAmount 完全失效）；
    /// 而场景里烘焙的进度条用的是内置九宫格 UISprite，两端带圆角，
    /// 拉到 8~18px 高就变成「两头尖尖 + 边缘发灰」的旧观感。
    ///
    /// 这里用内置白图创建一个 1×1 的 Sprite：Filled 模式照常生效，
    /// 同时渲染出来是真正的直角纯色块——没有圆角、没有渐变、没有描边。
    /// </summary>
    public static class UIFlatSprite
    {
        private static Sprite _white;

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
    }
}
