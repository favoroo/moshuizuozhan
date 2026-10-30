using P1.Core;
using TMPro;
using UnityEngine;

namespace P1.UI
{
    /// <summary>墨滴卡片的三种预设风格。</summary>
    public enum UIInkCardVariant
    {
        Team = 0,
        Dark = 1,
        Light = 2,
    }

    /// <summary>
    /// 卡片 / 面板的视觉预设。只描述「长什么样」，不参与布局；
    /// 由 <see cref="UIInkCard"/> 复制到 <see cref="UIInkShapeGraphic"/> 上。
    /// </summary>
    [System.Serializable]
    public class UIInkCardStyle
    {
        public UIInkShape shape = UIInkShape.Blob;

        public Color fillColor = Color.white;

        [Tooltip("填充色是否取当前队伍墨色（Team 预设为 true）")]
        public bool fillUsesTeamColor;

        public Color borderColor = new Color(0.082f, 0.071f, 0.110f, 1f);

        [Range(0f, 32f)] public float borderWidth = 7f;

        [Range(0f, 128f)] public float cornerRadius = 28f;

        [Range(0f, 0.25f)] public float wobble = 0.055f;

        [Range(0f, 1f)] public float glossAlpha = 0.18f;

        [Range(-1f, 1f)] public float glossSlope = 0.30f;

        [Range(0f, 1f)] public float glossCenter = 0.72f;

        [Range(0f, 0.6f)] public float glossThickness = 0.16f;

        [Range(0f, 1f)] public float innerRimAlpha = 0.10f;

        public Color titleColor = Color.white;

        public float titleSize = 38f;

        public float contentPadding = 24f;

        [Header("漫画面板（P10）")]
        [Tooltip("开启后：硬边偏移阴影 + 半调网点 + 关闭渐变高光/内沿，是本项目默认观感")]
        public bool comicPanel = true;

        [Tooltip("硬阴影偏移（UI 像素，Y 为负=向下）")]
        public Vector2 shadowOffset = new Vector2(6f, -6f);

        [Range(0f, 32f)] public float shadowPadding = 8f;

        [Tooltip("面板内部的半调网点")]
        public bool halftone;

        [Tooltip("对话气泡尖角（仅圆角矩形有效）")]
        public bool speechTail;

        public UIInkCardStyle Clone()
        {
            return (UIInkCardStyle)MemberwiseClone();
        }
    }

    /// <summary>
    /// UI 潮流视觉主题（P4-A 基类）：集中管理 Chunky 描边配色、墨队配色、TMP 字体、
    /// 弹簧预设与卡片样式。所有可调数值都在这里，组件里不硬编码。
    ///
    /// 用法：把本资产拖给 UI 组件；忘记拖时组件会自动退回 <see cref="Fallback"/>，
    /// 保证代码/测试在没有主题资产时也能跑。
    /// </summary>
    [CreateAssetMenu(fileName = "UITheme", menuName = "P1/UI Theme", order = 30)]
    public class UIThemeSO : ScriptableObject
    {
        [Header("Chunky Outline · 黑边与面板")]
        [Tooltip("墨线黑（漫画风：#1A1A1A）")]
        public Color outlineColor = new Color(0.102f, 0.102f, 0.102f, 1f);

        public Color outlineSoftColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        [Tooltip("硬阴影墨色（无模糊）")]
        public Color deepShadowColor = new Color(0.102f, 0.102f, 0.102f, 1f);

        [Tooltip("纸面白面板底（#FFFEF0）")]
        public Color panelColor = new Color(1f, 0.996f, 0.941f, 0.97f);

        public Color panelGlassColor = new Color(0.949f, 0.937f, 0.847f, 0.96f);

        [Tooltip("纸上文字（#1A1A1A）")]
        public Color textPrimary = new Color(0.102f, 0.102f, 0.102f, 1f);

        public Color textMuted = new Color(0.29f, 0.29f, 0.29f, 1f);

        public Color highlightColor = new Color(1f, 0.8f, 0f, 1f);

        public Color readyGold = new Color(1f, 0.8f, 0f, 1f);

        public Color dangerRed = new Color(1f, 0.2f, 0.2f, 1f);

        [Header("墨队配色（默认值与 InkGridConfig 一致；运行时会优先读 InkSurfaceController）")]
        public Color teamAColor = new Color(1f, 0.44f, 0.06f, 1f);

        public Color teamALight = new Color(1f, 0.769f, 0.541f, 1f);

        public Color teamADark = new Color(0.722f, 0.384f, 0.059f, 1f);

        public Color teamBColor = new Color(0.15f, 0.42f, 1f, 1f);

        public Color teamBLight = new Color(0.596f, 0.690f, 1f, 1f);

        public Color teamBDark = new Color(0.141f, 0.259f, 0.706f, 1f);

        public Color neutralColor = new Color(0.42f, 0.42f, 0.46f, 1f);

        [Header("字体（P1-C 已生成 TMP 资产）")]
        public TMP_FontAsset displayFont;

        public TMP_FontAsset bodyFont;

        [Header("动效 · 弹簧")]
        public UISpringSettings popSpring = UISpringSettings.Pop;

        public UISpringSettings pressSpring = UISpringSettings.Press;

        public UISpringSettings followSpring = UISpringSettings.Follow;

        public UISpringSettings settleSpring = UISpringSettings.Settle;

        public UICubicBezier entranceEasing = new UICubicBezier(0.34f, 1.56f, 0.64f, 1f);

        [Range(0.05f, 1f)] public float entranceFadeDuration = 0.26f;

        [Header("Chunky 文字")]
        [Tooltip("TMP 描边宽度（0..1，受字体 SDF padding 限制）")]
        [Range(0f, 1f)] public float chunkyOutlineWidth = 0.06f;

        [Range(0f, 1f)] public float chunkyInnerOutlineWidth = 0.05f;

        [Tooltip("填充层相对描边层上移的距离（em），形成硬阴影")]
        [Range(0f, 0.2f)] public float chunkyShadowOffsetY = 0.045f;

        [Tooltip("填充层相对描边层右移的距离（em）：漫画硬阴影往右下偏，像印刷套色偏移")]
        [Range(-0.1f, 0.1f)] public float chunkyShadowOffsetX = 0.03f;

        // ==================== 漫画风 token（P10） ====================
        // 只增不改：上面所有旧字段继续有效，老调用方无需改签名即可换肤。

        [Header("漫画 · 调色板")]
        [Tooltip("墨线黑：所有边框、硬阴影、纸上文字")]
        public Color inkColor = new Color(0.102f, 0.102f, 0.102f, 1f);

        [Tooltip("纸面白：面板底色")]
        public Color paperColor = new Color(1f, 0.996f, 0.941f, 1f);

        [Tooltip("次级纸面：列表行/内嵌区块")]
        public Color paperAltColor = new Color(0.949f, 0.937f, 0.847f, 1f);

        public Color accentRed = new Color(1f, 0.2f, 0.2f, 1f);

        public Color accentYellow = new Color(1f, 0.8f, 0f, 1f);

        public Color accentBlue = new Color(0.2f, 0.4f, 1f, 1f);

        public Color accentGreen = new Color(0.2f, 0.8f, 0.2f, 1f);

        [Header("漫画 · 墨线与硬阴影")]
        [Range(0f, 16f)] public float inkBorderWidth = 6f;

        [Range(0f, 20f)] public float inkBorderHeavyWidth = 8f;

        [Tooltip("硬阴影偏移（UI 像素，Y 为负=向下）。悬停时缩到 3px，按下归零")]
        public Vector2 hardShadowOffset = new Vector2(6f, -6f);

        [Tooltip("移动端是否保留半调网点（低端机关掉可省顶点）")]
        public bool halftoneOnMobile = true;

        [Header("漫画 · 半调网点")]
        [Range(4f, 24f)] public float halftoneSpacing = 8f;

        [Range(0.5f, 4f)] public float halftoneDotRadius = 1.3f;

        [Range(0f, 1f)] public float halftoneAlpha = 0.14f;

        [Range(0f, 1f)] public float halftoneHoverAlpha = 0.22f;

        [Header("漫画 · 动效（毫秒级硬切，无回弹）")]
        [Tooltip("按下压印时长（秒），规范上限 0.1")]
        [Range(0.02f, 0.1f)] public float pressDuration = 0.06f;

        [Range(0.02f, 0.1f)] public float hoverDuration = 0.08f;

        [Range(0.02f, 0.1f)] public float entranceDuration = 0.12f;

        [Range(1f, 1.2f)] public float hoverScale = 1.06f;

        [Tooltip("悬停倾斜角度（负值=逆时针，像漫画音效气泡）")]
        [Range(-10f, 0f)] public float hoverTiltDegrees = -3f;

        [Range(0.85f, 1f)] public float pressScale = 0.97f;

        [Header("漫画 · 中文字体（OFL：庆科黄油体 / 快乐体）")]
        [Tooltip("标题/横幅用中文字体；缺字时 TMP 会走各自的 fallback 链")]
        public TMP_FontAsset displayCjkFont;

        [Tooltip("正文/设置项用中文字体")]
        public TMP_FontAsset bodyCjkFont;

        [Header("卡片预设（漫画风）")]
        public UIInkCardStyle teamCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(1f, 0.44f, 0.06f, 1f),
            fillUsesTeamColor = true,
            borderColor = new Color(0.102f, 0.102f, 0.102f, 1f),
            borderWidth = 6f,
            cornerRadius = 5f,
            wobble = 0f,
            glossAlpha = 0f,
            glossSlope = 0f,
            glossCenter = 0.5f,
            glossThickness = 0f,
            innerRimAlpha = 0f,
            titleColor = Color.white,
            titleSize = 40f,
            contentPadding = 20f,
            comicPanel = true,
            halftone = true,
            shadowOffset = new Vector2(6f, -6f),
            shadowPadding = 8f,
        };

        public UIInkCardStyle darkCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(0.102f, 0.102f, 0.102f, 1f),
            fillUsesTeamColor = false,
            borderColor = new Color(0.102f, 0.102f, 0.102f, 1f),
            borderWidth = 6f,
            cornerRadius = 5f,
            wobble = 0f,
            glossAlpha = 0f,
            glossSlope = 0f,
            glossCenter = 0.5f,
            glossThickness = 0f,
            innerRimAlpha = 0f,
            titleColor = new Color(1f, 0.996f, 0.941f, 1f),
            titleSize = 40f,
            contentPadding = 20f,
            comicPanel = true,
            halftone = false,
            shadowOffset = new Vector2(6f, -6f),
            shadowPadding = 8f,
        };

        public UIInkCardStyle lightCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(1f, 0.996f, 0.941f, 1f),
            fillUsesTeamColor = false,
            borderColor = new Color(0.102f, 0.102f, 0.102f, 1f),
            borderWidth = 6f,
            cornerRadius = 5f,
            wobble = 0f,
            glossAlpha = 0f,
            glossSlope = 0f,
            glossCenter = 0.5f,
            glossThickness = 0f,
            innerRimAlpha = 0f,
            titleColor = new Color(0.102f, 0.102f, 0.102f, 1f),
            titleSize = 36f,
            contentPadding = 20f,
            comicPanel = true,
            halftone = true,
            shadowOffset = new Vector2(6f, -6f),
            shadowPadding = 8f,
        };

        private static UIThemeSO _fallback;

        /// <summary>没有显式配置主题时的兜底实例（不落盘，域重载后重建）。</summary>
        public static UIThemeSO Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<UIThemeSO>();
                    _fallback.name = "UITheme (Fallback)";
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallback;
            }
        }

        public static UIThemeSO Resolve(UIThemeSO theme)
        {
            return theme != null ? theme : Fallback;
        }

        public Color GetTeamColor(Team team)
        {
            switch (team)
            {
                case Team.TeamA: return teamAColor;
                case Team.TeamB: return teamBColor;
                default: return neutralColor;
            }
        }

        public Color GetTeamLight(Team team)
        {
            switch (team)
            {
                case Team.TeamA: return teamALight;
                case Team.TeamB: return teamBLight;
                default: return neutralColor;
            }
        }

        public Color GetTeamDark(Team team)
        {
            switch (team)
            {
                case Team.TeamA: return teamADark;
                case Team.TeamB: return teamBDark;
                default: return neutralColor;
            }
        }

        public UIInkCardStyle GetCardStyle(UIInkCardVariant variant)
        {
            switch (variant)
            {
                case UIInkCardVariant.Light: return lightCard;
                case UIInkCardVariant.Dark: return darkCard;
                default: return teamCard;
            }
        }

        public TMP_FontAsset GetFont(bool display)
        {
            if (display && displayFont != null) return displayFont;
            return bodyFont != null ? bodyFont : displayFont;
        }

        /// <summary>中文字体：标题走庆科黄油体，正文走快乐体。</summary>
        public TMP_FontAsset GetCjkFont(bool display)
        {
            if (display && displayCjkFont != null) return displayCjkFont;
            return bodyCjkFont != null ? bodyCjkFont : displayCjkFont;
        }

        /// <summary>
        /// 按文本内容自动选字体：含汉字就用 CJK 字体（保证描边材质对中文同样生效），
        /// 纯拉丁/数字仍走 TitanOne / Rubik，保留漫画数字的味道。
        /// </summary>
        public TMP_FontAsset ResolveFont(bool display, string text)
        {
            TMP_FontAsset cjk = GetCjkFont(display);
            if (cjk != null && ComicStyle.ContainsCjk(text)) return cjk;
            return GetFont(display);
        }

        /// <summary>纸上正文色（漫画风默认观感）；深色面板上请用 <see cref="paperColor"/>。</summary>
        public Color InkText => textPrimary;

        /// <summary>网点透明度：悬停时从 14% 提到 22%，是「Halftone Reveal」的核心。</summary>
        public float HalftoneAlphaFor(bool hovered)
        {
            return hovered ? halftoneHoverAlpha : halftoneAlpha;
        }

        /// <summary>单位：秒；严格夹在规范上限 100ms 内，防止别处传进来长长的动画。</summary>
        public float ClampDuration(float seconds)
        {
            return Mathf.Clamp(seconds, 0.02f, 0.1f);
        }

        public Vector2 HoverShadowOffset => hardShadowOffset * 0.5f;

        public Vector2 CollapsedShadowOffset => Vector2.zero;
    }
}