using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 几何大胆风（Geometric Bold）设计 token：全项目唯一的静态色板与度量来源。
    ///
    /// 为什么既有 UIThemeSO 还要这个类：
    /// - TMP 材质、程序化 Graphic 的默认值、Editor 预览工具在「拿不到主题资产」时也要有统一兜底；
    /// - 运行时要判断「这段文字是不是中文」「这个颜色在纸上够不够对比度」等纯函数，
    ///   不适合塞进 ScriptableObject。
    ///
    /// 规范红线（改 UI 前必读，详见 Docs/UI-GeometricBold.md）：
    /// - 纯色大色块 + 黑白为主 + 1~2 种强调色；禁止渐变、柔光、模糊阴影；
    /// - 唯一允许的立体感来源是「零模糊硬偏移阴影」（ShadowButton/Panel/Hero/Dialog… 档位）；
    /// - 圆角语言（2026-11 #09）：面板/卡片/按钮 = 圆角矩形（CornerFor 按短边自适应半径），
    ///   小方块徽章 = 正圆（Pill）、条签/进度条 = 胶囊（Pill）；CornerSharp=0 真直角仅为兼容保留；
    /// - 交互一律硬切（按下 0ms、松开 0.1s Cubic-Out），禁止弹簧/bounce/摆动；
    /// - 移动端字号 ≥ 桌面，全部走 UITypography.Font()。
    ///
    /// 兼容说明：旧漫画风的 token 名全部保留（Ink/Paper/Border/CornerSharp/ShadowOffset…），
    /// 26 个调用方文件无需改签名即可换肤；ShadowOffset/HoverShadowOffset 两个旧常量保留为 0，
    /// 新代码请改用 Shadow* 向量档位。
    /// </summary>
    public static class ComicStyle
    {
        // ---------- 调色板（黑白 + 三原色强调，单屏最多 4 色） ----------
        public static readonly Color Ink = FromHex(0x141414);
        public static readonly Color Paper = FromHex(0xFFFFFF);
        public static readonly Color PaperAlt = FromHex(0xF0F0F0);

        public static readonly Color Red = FromHex(0xFF2E2E);
        public static readonly Color Yellow = FromHex(0xFFD400);
        public static readonly Color Blue = FromHex(0x2456FF);
        public static readonly Color Green = FromHex(0x1FC94F);

        /// <summary>墨色半透明版本（弱化文字/分隔线用，避免抢主体）。</summary>
        public static readonly Color InkSoft = new Color(Ink.r, Ink.g, Ink.b, 0.14f);

        // ---------- 度量 ----------
        public const float BorderThin = 3f;
        public const float Border = 4f;
        public const float BorderHeavy = 6f;

        /// <summary>兼容常量（=0 真直角）：圆角化后 UI 代码不再使用，仅为旧调用方编译保留。</summary>
        public const float CornerSharp = 0f;

        /// <summary>全圆角：正圆/胶囊（头像、Tag、进度条）。</summary>
        public const float CornerBubble = 999f;

        /// <summary>圆角半径上限：大面板/卡片/弹窗（短边 ≥107px 时封顶到此）。</summary>
        public const float CornerPanel = 32f;

        /// <summary>圆角半径下限：小徽章/键帽（短边 ≤40px 时兜底到此）。</summary>
        public const float CornerChip = 12f;

        /// <summary>圆角随短边的占比：半径 = 短边 × 该值，大小盒子观感统一且绝不吃字。</summary>
        public const float CornerRatio = 0.30f;

        /// <summary>
        /// 按短边自适应的圆角半径（RoundedRect 半径为 0 时构建期自动取用，rect 变化自动跟随）：
        /// 短边 40 → 12、88 → 26.4、≥107 → 32 封顶；正圆/胶囊走 Pill（CornerBubble），不经此函数。
        /// </summary>
        public static float CornerFor(float width, float height)
        {
            return Mathf.Clamp(Mathf.Min(width, height) * CornerRatio, CornerChip, CornerPanel);
        }

        /// <summary>
        /// 兼容常量（旧调用方仍引用）：真正画阴影请用下面的 Shadow* 档位。
        /// t-1 已解禁「零模糊硬偏移阴影」，见 Docs/UI-GeometricBold.md。
        /// </summary>
        public const float ShadowOffset = 0f;
        public const float HoverShadowOffset = 0f;

        // ---------- 硬偏移阴影档位（t-1，uGUI y 向上：右下 = 正 x / 负 y） ----------
        // 唯一允许的立体感来源：同形状纯色偏移底板、零模糊、不透明纯墨黑。

        public static readonly Vector2 ShadowTag = new Vector2(2f, -3f);
        public static readonly Vector2 ShadowButton = new Vector2(3f, -4f);
        public static readonly Vector2 ShadowButtonHover = new Vector2(5f, -6f);
        public static readonly Vector2 ShadowButtonPressed = Vector2.zero;
        public static readonly Vector2 ShadowPanel = new Vector2(4f, -5f);
        public static readonly Vector2 ShadowHero = new Vector2(6f, -7f);
        public static readonly Vector2 ShadowDialog = new Vector2(8f, -9f);

        // 网点/动作线是漫画语言，几何风全局禁用（保留常量名给旧调用方编译）。
        public const float HalftoneSpacing = 8f;
        public const float HalftoneDotRadius = 1.3f;
        public const float HalftoneAlpha = 0f;
        public const float HalftoneHoverAlpha = 0f;

        // ---------- 动效（秒；规范上限 100ms，linear 硬切） ----------
        public const float PressDuration = 0.05f;
        public const float HoverDuration = 0.1f;
        public const float EntranceDuration = 0.12f;

        /// <summary>悬停放大 5%（hover:scale-105）。</summary>
        public const float HoverScale = 1.05f;

        /// <summary>几何风不做倾斜：保持 0。</summary>
        public const float HoverTiltDegrees = 0f;

        /// <summary>按下压缩（active:scale-97；t-1 用更克制的 3%）。</summary>
        public const float PressScale = 0.97f;

        /// <summary>鼠标/触屏按下时的硬位移（UI px）：按下层回落，抬起弹回。</summary>
        public const float PressOffsetY = -3f;

        /// <summary>能吃到几何视觉的最小字号：再小会糊成一团。正文底线。</summary>
        public const float MinReadableFontSize = 18f;

        // ---------- 字号阶梯（桌面 @1920×1080；移动端走 UITypography.Font 放大） ----------
        public const float FontHero = 132f;
        public const float FontH1 = 72f;
        public const float FontH2 = 44f;
        public const float FontH3 = 30f;
        public const float FontBody = 22f;
        public const float FontSmall = 18f;

        /// <summary>移动端字号放大倍率（"大色块+大字"在手机上必须更狠）。</summary>
        public const float MobileFontBoost = 1.28f;

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

        /// <summary>两色直接插值，仅用于 100ms 内的瞬时状态过渡，不用于渐变填充。</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, Mathf.Clamp01(t));
        }

        /// <summary>
        /// WCAG 相对亮度对比度。几何风大量「白底黑字 / 黑底白字」，
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

        /// <summary>字距：大标题收紧（font-black tracking-tight），正文略放开。</summary>
        public static float TrackingFor(float fontSize, bool display)
        {
            if (display) return fontSize >= 48f ? -4f : -1f;
            return 0.5f;
        }

        /// <summary>在给定底色上选可读文字色（白底给墨、深底给纸），对比度优先。</summary>
        public static Color ReadableTextOn(Color background)
        {
            return ContrastRatio(background, Ink) >= ContrastRatio(background, Paper) ? Ink : Paper;
        }
    }
}
