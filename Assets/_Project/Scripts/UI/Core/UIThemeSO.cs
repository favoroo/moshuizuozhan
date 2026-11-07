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
        public UIInkShape shape = UIInkShape.RoundedRect;

        public Color fillColor = Color.white;

        [Tooltip("填充色是否取当前队伍墨色（Team 预设为 true）")]
        public bool fillUsesTeamColor;

        public Color borderColor = new Color(0.078f, 0.078f, 0.078f, 1f);

        [Range(0f, 32f)] public float borderWidth = 4f;

        [Tooltip("0 = 按短边自适应（ComicStyle.CornerFor）；正圆/胶囊请直接把 shape 设为 Pill")]
        [Range(0f, 128f)] public float cornerRadius = 0f;

        [Range(0f, 0.25f)] public float wobble = 0f;

        [Range(0f, 1f)] public float glossAlpha = 0f;

        [Range(-1f, 1f)] public float glossSlope = 0f;

        [Range(0f, 1f)] public float glossCenter = 0.5f;

        [Range(0f, 0.6f)] public float glossThickness = 0f;

        [Range(0f, 1f)] public float innerRimAlpha = 0f;

        public Color titleColor = Color.white;

        public float titleSize = 38f;

        public float contentPadding = 24f;

        [Header("几何面板（P14）")]
        [Tooltip("兼容字段：几何风固定直角+实线边框+无阴影+无网点，此开关不再影响绘制")]
        public bool comicPanel;

        [Tooltip("阴影偏移（几何风禁用，保持 0）")]
        public Vector2 shadowOffset = Vector2.zero;

        [Range(0f, 32f)] public float shadowPadding = 0f;

        [Tooltip("面板内部网点（几何风禁用）")]
        public bool halftone;

        [Tooltip("对话气泡尖角（几何风禁用）")]
        public bool speechTail;

        public UIInkCardStyle Clone()
        {
            return (UIInkCardStyle)MemberwiseClone();
        }
    }

    /// <summary>
    /// 硬偏移阴影档位（t-1 新野兽派）：全项目只有这几档偏移，禁止在调用方现编数值。
    /// uGUI 的 y 向上，因此「阴影在右下」= 正 x / 负 y。
    /// </summary>
    public enum UIHardShadowLevel
    {
        None = 0,

        /// <summary>色签 / 小徽章 (2,-3)。</summary>
        Tag = 1,

        /// <summary>按钮常态 (3,-4)。</summary>
        Button = 2,

        /// <summary>按钮悬停 (5,-6)：阴影变大、内容不动，硬切不补间。</summary>
        ButtonHover = 3,

        /// <summary>按钮按下 (0,0)：贴回底板，配合内容位移做「压印」。</summary>
        ButtonPressed = 4,

        /// <summary>面板 / 卡片 (4,-5)。</summary>
        Panel = 5,

        /// <summary>Hero 大色块 (6,-7)。</summary>
        Hero = 6,

        /// <summary>弹窗 (8,-9)。</summary>
        Dialog = 7,
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
        [Tooltip("墨线黑（几何风：#141414）")]
        public Color outlineColor = new Color(0.078f, 0.078f, 0.078f, 1f);

        public Color outlineSoftColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        [Tooltip("阴影墨色（几何风禁用阴影，字段保留兼容）")]
        public Color deepShadowColor = new Color(0.078f, 0.078f, 0.078f, 1f);

        [Tooltip("纸面白面板底（#FFFFFF）")]
        public Color panelColor = Color.white;

        public Color panelGlassColor = new Color(0.941f, 0.941f, 0.941f, 0.96f);

        [Tooltip("纸上文字（#141414）")]
        public Color textPrimary = new Color(0.078f, 0.078f, 0.078f, 1f);

        public Color textMuted = new Color(0.29f, 0.29f, 0.29f, 1f);

        public Color highlightColor = new Color(1f, 0.831f, 0f, 1f);

        public Color readyGold = new Color(1f, 0.831f, 0f, 1f);

        public Color dangerRed = new Color(1f, 0.180f, 0.180f, 1f);

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

        public UICubicBezier entranceEasing = new UICubicBezier(0f, 0f, 1f, 1f);

        [Range(0.05f, 0.3f)] public float entranceFadeDuration = 0.12f;

        [Header("Chunky 文字")]
        [Tooltip("TMP 描边宽度（0..1，受字体 SDF padding 限制）")]
        [Range(0f, 1f)] public float chunkyOutlineWidth = 0.06f;

        [Range(0f, 1f)] public float chunkyInnerOutlineWidth = 0.05f;

        [Tooltip("填充层相对描边层上移的距离（em），形成硬阴影")]
        [Range(0f, 0.2f)] public float chunkyShadowOffsetY = 0.045f;

        [Tooltip("填充层相对描边层右移的距离（em）：漫画硬阴影往右下偏，像印刷套色偏移")]
        [Range(-0.1f, 0.1f)] public float chunkyShadowOffsetX = 0.03f;

        [Header("Chunky 标题硬影 · t-1（像素级，固定不随字号缩放）")]
        [Tooltip("用像素偏移代替上面的 em 字段：大标题统一 4px 硬影，小字不会被放大成糊块")]
        public bool chunkyShadowInPixels = true;

        [Tooltip("标题硬影偏移（像素，uGUI y 向上：右下 = 正 x 负 y）")]
        public Vector2 chunkyShadowOffsetPixels = new Vector2(4f, -4f);

        // ==================== 几何大胆风 token（P14） ====================
        // 只增不改：上面所有旧字段继续有效，老调用方无需改签名即可换肤。

        [Header("几何 · 调色板（黑白 + 三原色强调）")]
        [Tooltip("墨线黑：所有边框、色块、白底上的文字")]
        public Color inkColor = new Color(0.078f, 0.078f, 0.078f, 1f);

        [Tooltip("纸面白：面板底色")]
        public Color paperColor = Color.white;

        [Tooltip("次级纸面：列表行/内嵌区块")]
        public Color paperAltColor = new Color(0.941f, 0.941f, 0.941f, 1f);

        public Color accentRed = new Color(1f, 0.180f, 0.180f, 1f);

        public Color accentYellow = new Color(1f, 0.831f, 0f, 1f);

        public Color accentBlue = new Color(0.141f, 0.337f, 1f, 1f);

        public Color accentGreen = new Color(0.122f, 0.788f, 0.310f, 1f);

        [Header("几何 · 实线边框")]
        [Range(0f, 16f)] public float inkBorderWidth = 4f;

        [Range(0f, 20f)] public float inkBorderHeavyWidth = 6f;

        [Tooltip("通用硬影偏移（兼容旧调用，写入面板档 4,-5）；新代码用下面的 Shadow* 档位")]
        public Vector2 hardShadowOffset = new Vector2(4f, -5f);

        [Tooltip("移动端是否保留半调网点（几何风禁用）")]
        public bool halftoneOnMobile;

        [Header("几何 · 半调网点（已禁用，字段保留兼容）")]
        [Range(4f, 24f)] public float halftoneSpacing = 8f;

        [Range(0.5f, 4f)] public float halftoneDotRadius = 1.3f;

        [Range(0f, 1f)] public float halftoneAlpha = 0f;

        [Range(0f, 1f)] public float halftoneHoverAlpha = 0f;

        [Header("几何 · 动效（≤100ms linear 硬切，无回弹）")]
        [Tooltip("按下压印时长（秒），规范上限 0.1")]
        [Range(0.02f, 0.1f)] public float pressDuration = 0.05f;

        [Range(0.02f, 0.1f)] public float hoverDuration = 0.1f;

        [Tooltip("入场时长（秒）：t-1 用 0.22s Back-Out；交互类仍受 ClampDuration 的 0.16s 上限约束")]
        [Range(0.05f, 0.3f)] public float entranceDuration = 0.22f;

        [Range(1f, 1.2f)] public float hoverScale = 1.05f;

        [Tooltip("悬停倾斜角度（几何风禁止倾斜，保持 0）")]
        [Range(-10f, 0f)] public float hoverTiltDegrees = 0f;

        [Range(0.85f, 1f)] public float pressScale = 0.97f;

        // ==================== 硬偏移阴影 · t-1 新野兽派（P19 波次1a） ====================
        // 唯一允许的立体感来源：同形状纯色偏移底板、零模糊、不透明。
        // uGUI 的 y 向上，因此「阴影在右下」= 正 x / 负 y。

        [Header("硬偏移阴影 · t-1（零模糊 / 同形状偏移底板 / 不透明纯墨黑）")]
        [Tooltip("硬影颜色：不透明纯墨黑（绘制时 alpha 强制 1，禁止半透明阴影）")]
        public Color hardShadowColor = new Color(0.078f, 0.078f, 0.078f, 1f);

        [Tooltip("色签 / 小徽章 (2,-3)")]
        public Vector2 shadowOffsetTag = new Vector2(2f, -3f);

        [Tooltip("按钮常态 (3,-4)")]
        public Vector2 shadowOffsetButton = new Vector2(3f, -4f);

        [Tooltip("按钮悬停 (5,-6)：阴影变大，硬切不补间")]
        public Vector2 shadowOffsetButtonHover = new Vector2(5f, -6f);

        [Tooltip("按钮按下 (0,0)：贴回底板，配合内容位移做压印")]
        public Vector2 shadowOffsetButtonPressed = Vector2.zero;

        [Tooltip("面板 / 卡片 (4,-5)")]
        public Vector2 shadowOffsetPanel = new Vector2(4f, -5f);

        [Tooltip("Hero 大色块 (6,-7)")]
        public Vector2 shadowOffsetHero = new Vector2(6f, -7f);

        [Tooltip("弹窗 (8,-9)")]
        public Vector2 shadowOffsetDialog = new Vector2(8f, -9f);

        [Tooltip("按下时内容（非背景层 RectTransform）是否随阴影方向自动位移同样像素")]
        public bool pressContentShift = true;

        [Header("按压物理 · t-1（0ms 硬切 + 0.1s Cubic-Out 回弹）")]
        [Tooltip("按下瞬间硬切到 pressScale（0ms），关掉才用 pressDuration 补间")]
        public bool pressInstant = true;

        [Tooltip("松开回弹时长（秒）：Cubic-Out 回到 1.0")]
        [Range(0.05f, 0.2f)] public float pressReboundDuration = 0.1f;

        [Tooltip("松开回弹曲线（Cubic-Out）")]
        public UICubicBezier pressReboundEasing = UICubicBezier.EaseOut;

        [Header("入场 · t-1（0.22s Back-Out + 0.05s/张错峰）")]
        [Tooltip("入场是否用 Back-Out 轻微过冲")]
        public bool entranceBackOut = true;

        [Tooltip("连续卡片逐张 pop-in 的错峰间隔（秒）")]
        [Range(0f, 0.2f)] public float entranceStagger = 0.05f;

        [Header("几何 · 中文字体（MiSans 免费商用）")]
        [Tooltip("标题/横幅用中文字体；缺字时 TMP 会走各自的 fallback 链")]
        public TMP_FontAsset displayCjkFont;

        [Tooltip("正文/设置项用中文字体")]
        public TMP_FontAsset bodyCjkFont;

        [Header("卡片预设（圆角矩形自适应半径 + 实线边；硬影默认关，需要时把 shadowOffset 设成面板档 4,-5）")]
        public UIInkCardStyle teamCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(1f, 0.44f, 0.06f, 1f),
            fillUsesTeamColor = true,
            borderColor = new Color(0.078f, 0.078f, 0.078f, 1f),
            borderWidth = 4f,
            cornerRadius = 0f,
            wobble = 0f,
            glossAlpha = 0f,
            innerRimAlpha = 0f,
            titleColor = Color.white,
            titleSize = 40f,
            contentPadding = 20f,
            comicPanel = false,
            halftone = false,
            shadowOffset = Vector2.zero,
            shadowPadding = 0f,
        };

        public UIInkCardStyle darkCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = new Color(0.078f, 0.078f, 0.078f, 1f),
            fillUsesTeamColor = false,
            borderColor = new Color(0.078f, 0.078f, 0.078f, 1f),
            borderWidth = 4f,
            cornerRadius = 0f,
            wobble = 0f,
            glossAlpha = 0f,
            innerRimAlpha = 0f,
            titleColor = Color.white,
            titleSize = 40f,
            contentPadding = 20f,
            comicPanel = false,
            halftone = false,
            shadowOffset = Vector2.zero,
            shadowPadding = 0f,
        };

        public UIInkCardStyle lightCard = new UIInkCardStyle
        {
            shape = UIInkShape.RoundedRect,
            fillColor = Color.white,
            fillUsesTeamColor = false,
            borderColor = new Color(0.078f, 0.078f, 0.078f, 1f),
            borderWidth = 4f,
            cornerRadius = 0f,
            wobble = 0f,
            glossAlpha = 0f,
            innerRimAlpha = 0f,
            titleColor = new Color(0.078f, 0.078f, 0.078f, 1f),
            titleSize = 36f,
            contentPadding = 20f,
            comicPanel = false,
            halftone = false,
            shadowOffset = Vector2.zero,
            shadowPadding = 0f,
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
                    _fallback.ApplyFallbackFonts();
                }

                return _fallback;
            }
        }

        // ==================== 字体兜底解析（P19 波次1a） ====================
        // 历史 bug：Fallback 的 4 个字体字段是 null，移动端触控层 / 键位编辑等
        // UIThemeSO.Resolve(null) 的界面全部掉回 TMP 全局默认字体（中文字形/描边全失效）。
        // 这里做惰性解析，保证 ResolveFont() 永不返回 null：
        //   ① 运行时资源库里的主题（Resources/P1RuntimeAssets.uiTheme，独立包唯一可靠来源）
        //   ② Resources.Load（字体若被拷进 Resources 目录时可用）
        //   ③ 编辑器 AssetDatabase 按路径 / 按类型搜索 Art/Fonts/TMP/
        //   ④ TMP 全局默认字体（最后兜底）

        private const string FontFolderEditor = "Assets/_Project/Art/Fonts/TMP/";
        private const string TmpResourceFolder = "Fonts/TMP/";

        private static bool _globalFontsResolved;
        private static TMP_FontAsset _globalDisplayFont;
        private static TMP_FontAsset _globalBodyFont;
        private static TMP_FontAsset _globalDisplayCjkFont;
        private static TMP_FontAsset _globalBodyCjkFont;

        /// <summary>编辑器里字体资产被重新导入/改名后调用，清空静态缓存（运行时无副作用）。</summary>
        public static void ResetFontCache()
        {
            _globalFontsResolved = false;
            _globalDisplayFont = null;
            _globalBodyFont = null;
            _globalDisplayCjkFont = null;
            _globalBodyCjkFont = null;
        }

        /// <summary>
        /// 全局字体兜底：display=true 取标题字重，cjk=true 取中文字体。
        /// 永不返回 null（最后一级是 TMP 全局默认；TMP 自身缺失的极端情况下才可能为 null）。
        /// </summary>
        public static TMP_FontAsset ResolveGlobalFont(bool display, bool cjk)
        {
            EnsureGlobalFonts();
            if (cjk) return display ? _globalDisplayCjkFont : _globalBodyCjkFont;
            return display ? _globalDisplayFont : _globalBodyFont;
        }

        private static void EnsureGlobalFonts()
        {
            if (_globalFontsResolved) return;
            _globalFontsResolved = true;

            // ① 运行时资源库里的主题（有字体就直接抄字段，避免递归 Stub）
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            UIThemeSO libraryTheme = library != null ? library.uiTheme : null;
            if (libraryTheme != null)
            {
                _globalDisplayFont = libraryTheme.displayFont;
                _globalBodyFont = libraryTheme.bodyFont;
                _globalDisplayCjkFont = libraryTheme.displayCjkFont;
                _globalBodyCjkFont = libraryTheme.bodyCjkFont;
            }

            // ② Resources（若字体资产被移入 Resources 目录）
            if (_globalDisplayFont == null) _globalDisplayFont = LoadResourceFont("MiSans-Heavy SDF", "MiSans-Semibold SDF");
            if (_globalBodyFont == null) _globalBodyFont = LoadResourceFont("MiSans-Semibold SDF", "MiSans-Heavy SDF");
            if (_globalDisplayCjkFont == null) _globalDisplayCjkFont = LoadResourceFont("MiSans-Heavy SDF", "MiSans-Semibold SDF");
            if (_globalBodyCjkFont == null) _globalBodyCjkFont = LoadResourceFont("MiSans-Semibold SDF", "MiSans-Heavy SDF");

            // ③ 编辑器 AssetDatabase（工程内的常规路径）
            if (_globalDisplayFont == null) _globalDisplayFont = LoadEditorFont("MiSans-Heavy SDF", "MiSans-Semibold SDF");
            if (_globalBodyFont == null) _globalBodyFont = LoadEditorFont("MiSans-Semibold SDF", "MiSans-Heavy SDF");
            if (_globalDisplayCjkFont == null) _globalDisplayCjkFont = LoadEditorFont("MiSans-Heavy SDF", "MiSans-Semibold SDF");
            if (_globalBodyCjkFont == null) _globalBodyCjkFont = LoadEditorFont("MiSans-Semibold SDF", "MiSans-Heavy SDF");

            // ④ TMP 全局默认
            TMP_FontAsset tmpDefault = null;
            try
            {
                tmpDefault = TMP_Settings.defaultFontAsset;
            }
            catch (System.Exception)
            {
                // TMP Settings 资产缺失时 TMP_Settings 可能抛异常；此处只做兜底，不打断 UI 构建。
            }

            if (_globalDisplayFont == null) _globalDisplayFont = tmpDefault;
            if (_globalBodyFont == null) _globalBodyFont = tmpDefault;
            if (_globalDisplayCjkFont == null) _globalDisplayCjkFont = _globalBodyFont;
            if (_globalBodyCjkFont == null) _globalBodyCjkFont = _globalBodyFont;
        }

        private static TMP_FontAsset LoadResourceFont(string primary, string secondary)
        {
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(TmpResourceFolder + primary);
            if (font == null) font = Resources.Load<TMP_FontAsset>(primary);
            if (font == null && !string.IsNullOrEmpty(secondary))
            {
                font = Resources.Load<TMP_FontAsset>(TmpResourceFolder + secondary);
                if (font == null) font = Resources.Load<TMP_FontAsset>(secondary);
            }

            return font;
        }

        private static TMP_FontAsset LoadEditorFont(string primary, string secondary)
        {
#if UNITY_EDITOR
            TMP_FontAsset font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontFolderEditor + primary + ".asset");
            if (font == null && !string.IsNullOrEmpty(secondary))
            {
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontFolderEditor + secondary + ".asset");
            }

            if (font != null) return font;

            // 按类型全工程搜一次（资产被改名/移动时的最后一条编辑器路径）
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (fileName.IndexOf(primary, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(secondary) &&
                     fileName.IndexOf(secondary, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    TMP_FontAsset found = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (found != null) return found;
                }
            }
#endif
            return null;
        }

        /// <summary>把全局字体兜底写进本实例的空字段（只对内存实例调用，不写资产）。</summary>
        private void ApplyFallbackFonts()
        {
            if (displayFont == null) displayFont = ResolveGlobalFont(true, false);
            if (bodyFont == null) bodyFont = ResolveGlobalFont(false, false);
            if (displayCjkFont == null) displayCjkFont = ResolveGlobalFont(true, true);
            if (bodyCjkFont == null) bodyCjkFont = ResolveGlobalFont(false, true);
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
            if (bodyFont != null) return bodyFont;
            if (displayFont != null) return displayFont;
            // 主题资产本身缺字体（旧档/独立包）时也不能返回 null：走全局兜底。
            return ResolveGlobalFont(display, false);
        }

        /// <summary>中文字重：标题走 MiSans-Heavy，正文走 MiSans-Semibold（缺档时全局兜底）。</summary>
        public TMP_FontAsset GetCjkFont(bool display)
        {
            if (display && displayCjkFont != null) return displayCjkFont;
            if (bodyCjkFont != null) return bodyCjkFont;
            if (displayCjkFont != null) return displayCjkFont;
            return ResolveGlobalFont(display, true);
        }

        /// <summary>
        /// 按字重取字体：display=true 取标题字重（MiSans Heavy），否则正文字重（MiSans Semibold）。
        /// 2026-11-01 起全站只有 MiSans 一族（Latin 与 CJK 同源），所以这里不再按文本内容切家族；
        /// CJK 分支保留是为了兼容旧主题资产（字段被单独改过时仍能取到中文字体）。
        /// 保证永不返回 null（最后一级为 TMP 全局默认字体）。
        /// </summary>
        public TMP_FontAsset ResolveFont(bool display, string text)
        {
            TMP_FontAsset cjk = GetCjkFont(display);
            if (cjk != null && ComicStyle.ContainsCjk(text)) return cjk;
            TMP_FontAsset font = GetFont(display);
            return font != null ? font : cjk;
        }

        /// <summary>纸上正文色（漫画风默认观感）；深色面板上请用 <see cref="paperColor"/>。</summary>
        public Color InkText => textPrimary;

        /// <summary>网点透明度：悬停时从 14% 提到 22%，是「Halftone Reveal」的核心。</summary>
        public float HalftoneAlphaFor(bool hovered)
        {
            return hovered ? halftoneHoverAlpha : halftoneAlpha;
        }

        /// <summary>单位：秒；严格夹在几何风上限（≤160ms）内，防止别处传进来长长的动画。</summary>
        public float ClampDuration(float seconds)
        {
            return Mathf.Clamp(seconds, 0.02f, 0.16f);
        }

        /// <summary>入场时长夹持：t-1 允许 0.22s Back-Out，上限放宽到 0.3s。</summary>
        public float ClampEntranceDuration(float seconds)
        {
            return Mathf.Clamp(seconds, 0.05f, 0.3f);
        }

        /// <summary>兼容旧调用：悬停时的硬影偏移（现在走 t-1 的按钮悬停档 5,-6）。</summary>
        public Vector2 HoverShadowOffset => shadowOffsetButtonHover;

        public Vector2 CollapsedShadowOffset => Vector2.zero;

        /// <summary>硬影颜色（alpha 恒为 1：规范禁止半透明阴影）。</summary>
        public Color HardShadowOpaqueColor
        {
            get
            {
                Color c = hardShadowColor;
                c.a = 1f;
                return c;
            }
        }

        /// <summary>按档位取硬影偏移（uGUI y 向上：阴影在右下 = 正 x / 负 y）。</summary>
        public Vector2 GetHardShadowOffset(UIHardShadowLevel level)
        {
            switch (level)
            {
                case UIHardShadowLevel.Tag: return shadowOffsetTag;
                case UIHardShadowLevel.Button: return shadowOffsetButton;
                case UIHardShadowLevel.ButtonHover: return shadowOffsetButtonHover;
                case UIHardShadowLevel.ButtonPressed: return shadowOffsetButtonPressed;
                case UIHardShadowLevel.Panel: return shadowOffsetPanel;
                case UIHardShadowLevel.Hero: return shadowOffsetHero;
                case UIHardShadowLevel.Dialog: return shadowOffsetDialog;
                default: return Vector2.zero;
            }
        }

        /// <summary>
        /// 把主题硬影档位直接套到一块 <see cref="UIInkShapeGraphic"/> 上（首选入口）：
        /// 同形状纯色偏移底板、零模糊、颜色不透明。<paramref name="level"/> 为 None 时清掉硬影。
        /// </summary>
        public void ApplyHardShadow(UIInkShapeGraphic graphic, UIHardShadowLevel level)
        {
            if (graphic == null) return;

            Vector2 offset = GetHardShadowOffset(level);
            bool visible = level != UIHardShadowLevel.None &&
                           (Mathf.Abs(offset.x) > 0.01f || Mathf.Abs(offset.y) > 0.01f);

            graphic.ShadowOffset = offset;
            graphic.ShadowColor = HardShadowOpaqueColor;
            graphic.HardShadow = visible;
        }

        public bool HasHardShadow(UIHardShadowLevel level)
        {
            Vector2 offset = GetHardShadowOffset(level);
            return level != UIHardShadowLevel.None &&
                   (Mathf.Abs(offset.x) > 0.01f || Mathf.Abs(offset.y) > 0.01f);
        }
    }
}