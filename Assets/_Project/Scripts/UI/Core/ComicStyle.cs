using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 漫画风设计 token（Comic Style）：全项目唯一的静态色板与度量来源。
    ///
    /// 为什么既有 UIThemeSO 还要这个类：
    /// - TMP 材质、程序化 Graphic 的默认值、Editor 预览工具在「拿不到主题资产」时也要有统一兜底；
    /// - 运行时要判断「这段文字是不是中文」「这个颜色在纸上够不够对比度」等纯函数，
    ///   不适合塞进 ScriptableObject。
    ///
    /// 规范红线（改 UI 前必读）：
    /// - 只用粗墨线边框（4~8px）+ 硬边偏移阴影，禁止细边框与柔边阴影；
    /// - 禁止渐变作为主要视觉，原 InkShape 的 gloss 高光带在漫画模式下必须关闭；
    /// - 圆角只用于对话气泡，面板/按钮一律 4~6px 近似直角；
    /// - 动效最长 100ms，ease-out，禁止 bounce/elastic 回弹。
    /// </summary>
    public static class ComicStyle
    {
        // ---------- 调色板 ----------
        public static readonly Color Ink = FromHex(0x1A1A1A);
        public static readonly Color Paper = FromHex(0xFFFEF0);
        public static readonly Color PaperAlt = FromHex(0xF2EFD8);

        public static readonly Color Red = FromHex(0xFF3333);
        public static readonly Color Yellow = FromHex(0xFFCC00);
        public static readonly Color Blue = FromHex(0x3366FF);
        public static readonly Color Green = FromHex(0x33CC33);

        /// <summary>墨线半透明版本（网点/动作线用，避免抢主体）。</summary>
        public static readonly Color InkSoft = new Color(Ink.r, Ink.g, Ink.b, 0.14f);

        // ---------- 度量 ----------
        public const float BorderThin = 4f;
        public const float Border = 6f;
        public const float BorderHeavy = 8f;

        public const float CornerSharp = 5f;
        public const float CornerBubble = 32f;

        public const float ShadowOffset = 6f;
        public const float HoverShadowOffset = 3f;

        public const float HalftoneSpacing = 8f;
        public const float HalftoneDotRadius = 1.3f;
        public const float HalftoneAlpha = 0.14f;
        public const float HalftoneHoverAlpha = 0.22f;

        // ---------- 动效（秒；规范上限 100ms） ----------
        public const float PressDuration = 0.06f;
        public const float HoverDuration = 0.08f;
        public const float EntranceDuration = 0.12f;

        /// <summary>“POW!” 弹出幅度：悬停放大 6% 并向左歪 3 度。</summary>
        public const float HoverScale = 1.06f;
        public const float HoverTiltDegrees = -3f;
        public const float PressScale = 0.97f;

        /// <summary>能吃到 Comic 视觉的最小字号：再小会糊成一团墨。</summary>
        public const float MinReadableFontSize = 16f;

        public static Color FromHex(int rgb, float alpha = 1f)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                alpha);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>往黑里压，用于 hover/active 的底色加深（不做渐变）。</summary>
        public static Color Darken(Color color, float amount)
        {
            float k = Mathf.Clamp01(1f - amount);
            return new Color(color.r * k, color.g * k, color.b * k, color.a);
        }

        /// <summary>两色直接插值，仅用于 80ms 内的瞬时状态过渡，不用于渐变填充。</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, Mathf.Clamp01(t));
        }

        /// <summary>
        /// WCAG 相对亮度对比度。漫画风大量「纸上黑字 / 深色上白字」，
        /// 这个函数给自检用：正文对比度必须 ≥ 4.5。
        /// </summary>
        public static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float hi = Mathf.Max(la, lb);
            float lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        public static float RelativeLuminance(Color c)
        {
            return 0.2126f * ToLinear(c.r) + 0.7152f * ToLinear(c.g) + 0.0722f * ToLinear(c.b);
        }

        static float ToLinear(float c)
        {
            return c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>这段文字里有没有汉字/假名（用于自动切 CJK 字体）。</summary>
        public static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsCjk(text[i])) return true;
            }

            return false;
        }

        public static bool IsCjk(char c)
        {
            return (c >= '\u2E80' && c <= '\u9FFF')      // CJK 部首 + 统一表意
                || (c >= '\uF900' && c <= '\uFAFF')      // 兼容表意
                || (c >= '\uFF00' && c <= '\uFFEF');     // 全角标点
        }

        /// <summary>漫画字距：大标题收紧，正文略放开，比默认值更有印刷感。</summary>
        public static float TrackingFor(float fontSize, bool display)
        {
            if (display) return fontSize >= 48f ? -3f : 0f;
            return 0.5f;
        }
    }
}