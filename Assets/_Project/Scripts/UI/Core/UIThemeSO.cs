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
        public Color outlineColor = new Color(0.082f, 0.071f, 0.110f, 1f);

        public Color outlineSoftColor = new Color(0.122f, 0.102f, 0.173f, 1f);

        public Color deepShadowColor = new Color(0f, 0f, 0f, 0.35f);

        public Color panelColor = new Color(0.098f, 0.082f, 0.145f, 0.92f);

        public Color panelGlassColor = new Color(0.149f, 0.122f, 0.227f, 0.90f);

        public Color textPrimary = Color.white;

        public Color textMuted = new Color(0.765f, 0.741f, 0.839f, 1f);

        public Color highlightColor = new Color(1f, 1f, 1f, 0.16f);

        public Color readyGold = new Color(1f, 0.835f, 0.29f, 1f);

        public Color dangerRed = new Color(0.784f, 0.09f, 0.227f, 1f);

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
        [Range(0f, 1f)] public float chunkyOutlineWidth = 0.35f;

        [Range(0f, 1f)] public float chunkyInnerOutlineWidth = 0.05f;

        [Tooltip("填充层相对描边层上移的距离（em），形成硬阴影")]
        [Range(0f, 0.2f)] public float chunkyShadowOffsetY = 0.05f;

        [Header("卡片预设")]
        public UIInkCardStyle teamCard = new UIInkCardStyle
        {
            shape = UIInkShape.Blob,
            fillColor = new Color(1f, 0.44f, 0.06f, 1f),
            fillUsesTeamColor = true,
            borderColor = new Color(0.082f, 0.071f, 0.110f, 1f),
            borderWidth = 8f,
            cornerRadius = 40f,
            wobble = 0.06f,
            glossAlpha = 0.18f,
            glossSlope = 0.30f,
            glossCenter = 0.72f,
            glossThickness = 0.16f,
            innerRimAlpha = 0.12f,
            titleColor = Color.white,
            titleSize = 38f,
            contentPadding = 26f,
        };

        public UIInkCardStyle darkCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(0.098f, 0.082f, 0.145f, 0.92f),
            fillUsesTeamColor = false,
            borderColor = new Color(0.122f, 0.102f, 0.173f, 1f),
            borderWidth = 7f,
            cornerRadius = 30f,
            wobble = 0f,
            glossAlpha = 0.10f,
            glossSlope = 0.30f,
            glossCenter = 0.74f,
            glossThickness = 0.14f,
            innerRimAlpha = 0.08f,
            titleColor = Color.white,
            titleSize = 36f,
            contentPadding = 24f,
        };

        public UIInkCardStyle lightCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(0.945f, 0.933f, 0.98f, 1f),
            fillUsesTeamColor = false,
            borderColor = new Color(0.082f, 0.071f, 0.110f, 1f),
            borderWidth = 7f,
            cornerRadius = 30f,
            wobble = 0f,
            glossAlpha = 0.35f,
            glossSlope = 0.30f,
            glossCenter = 0.74f,
            glossThickness = 0.16f,
            innerRimAlpha = 0f,
            titleColor = new Color(0.082f, 0.071f, 0.110f, 1f),
            titleSize = 36f,
            contentPadding = 24f,
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
    }
}