using P1.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 把几何大胆风（Geometric Bold）token 写进 ScriptableObject 资产
    /// （遵守「不手改 .asset YAML」的硬性规则）。
    ///
    /// 用法：菜单 `P1/画风/几何风/1. 应用几何主题`（旧路径 `P1/画风/漫画风/1` 保留为兼容别名）。
    /// 幂等：重复执行只会把值写成同一套，不会产生重复资产或孤儿材质。
    /// </summary>
    public static class ComicStyleInstaller
    {
        const string k_LogTag = "[几何主题]";

        const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        const string GaugePath = "Assets/_Project/ScriptableObjects/HudGaugeStyle.asset";
        const string TmpFolder = "Assets/_Project/Art/Fonts/TMP/";

        [MenuItem("P1/画风/几何风/1. 应用几何主题（UITheme + HUD 样式）", false, 2)]
        [MenuItem("P1/画风/漫画风/1. 应用几何主题（UITheme + HUD 样式）（旧路径）", false, 2)]
        public static void ApplyGeometricTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            if (theme == null)
            {
                Debug.LogError(k_LogTag + " 找不到 " + ThemePath);
                return;
            }

            var so = new SerializedObject(theme);

            // ---------- 调色板（黑白 + 三原色强调） ----------
            SetColor(so, "outlineColor", ComicStyle.Ink);
            SetColor(so, "outlineSoftColor", Color.Lerp(ComicStyle.Ink, Color.white, 0.38f));
            SetColor(so, "deepShadowColor", ComicStyle.Ink);
            SetColor(so, "panelColor", ComicStyle.Paper);
            SetColor(so, "panelGlassColor", ComicStyle.PaperAlt);
            SetColor(so, "textPrimary", ComicStyle.Ink);
            SetColor(so, "textMuted", Color.Lerp(ComicStyle.Ink, Color.white, 0.28f));
            SetColor(so, "highlightColor", ComicStyle.Yellow);
            SetColor(so, "readyGold", ComicStyle.Yellow);
            SetColor(so, "dangerRed", ComicStyle.Red);
            SetColor(so, "neutralColor", new Color(0.45f, 0.45f, 0.45f, 1f));

            SetColor(so, "inkColor", ComicStyle.Ink);
            SetColor(so, "paperColor", ComicStyle.Paper);
            SetColor(so, "paperAltColor", ComicStyle.PaperAlt);
            SetColor(so, "accentRed", ComicStyle.Red);
            SetColor(so, "accentYellow", ComicStyle.Yellow);
            SetColor(so, "accentBlue", ComicStyle.Blue);
            SetColor(so, "accentGreen", ComicStyle.Green);

            // ---------- 实线边框 + t-1 零模糊硬偏移阴影 ----------
            SetFloat(so, "inkBorderWidth", ComicStyle.Border);
            SetFloat(so, "inkBorderHeavyWidth", ComicStyle.BorderHeavy);

            // 硬影令牌（唯一立体感来源：同形状纯色偏移底板、零模糊、不透明纯墨黑）
            SetColor(so, "hardShadowColor", ComicStyle.Ink);
            SetVector2(so, "shadowOffsetTag", ComicStyle.ShadowTag);
            SetVector2(so, "shadowOffsetButton", ComicStyle.ShadowButton);
            SetVector2(so, "shadowOffsetButtonHover", ComicStyle.ShadowButtonHover);
            SetVector2(so, "shadowOffsetButtonPressed", ComicStyle.ShadowButtonPressed);
            SetVector2(so, "shadowOffsetPanel", ComicStyle.ShadowPanel);
            SetVector2(so, "shadowOffsetHero", ComicStyle.ShadowHero);
            SetVector2(so, "shadowOffsetDialog", ComicStyle.ShadowDialog);
            SetBool(so, "pressContentShift", true);

            // 旧字段保留兼容：通用硬影偏移写面板档 (4,-5)
            SetVector2(so, "hardShadowOffset", ComicStyle.ShadowPanel);
            SetBool(so, "halftoneOnMobile", false);

            SetFloat(so, "halftoneSpacing", ComicStyle.HalftoneSpacing);
            SetFloat(so, "halftoneDotRadius", ComicStyle.HalftoneDotRadius);
            SetFloat(so, "halftoneAlpha", 0f);
            SetFloat(so, "halftoneHoverAlpha", 0f);

            // ---------- 动效：按压 0ms 硬切 + 0.1s Cubic-Out 回弹；入场 0.22s Back-Out ----------
            SetFloat(so, "pressDuration", ComicStyle.PressDuration);
            SetFloat(so, "hoverDuration", ComicStyle.HoverDuration);
            SetFloat(so, "entranceDuration", 0.22f);
            SetFloat(so, "hoverScale", ComicStyle.HoverScale);
            SetFloat(so, "hoverTiltDegrees", 0f);
            SetFloat(so, "pressScale", ComicStyle.PressScale);
            SetBool(so, "pressInstant", true);
            SetFloat(so, "pressReboundDuration", 0.1f);
            SetEasing(so, "pressReboundEasing", 0.22f, 1f, 0.36f, 1f); // Cubic-Out
            SetBool(so, "entranceBackOut", true);
            SetFloat(so, "entranceStagger", 0.05f);
            SetFloat(so, "entranceFadeDuration", 0.12f);

            // 弹簧已不被 UISpringMotion 使用，但字段保留；写成临界阻尼防止旧代码回弹。
            SetSpring(so, "popSpring", 16f, 1f);
            SetSpring(so, "pressSpring", 20f, 1f);
            SetSpring(so, "followSpring", 14f, 1f);
            SetSpring(so, "settleSpring", 12f, 1f);

            var easing = so.FindProperty("entranceEasing");
            if (easing != null)
            {
                SetFloat(easing, "x1", 0f);
                SetFloat(easing, "y1", 0f);
                SetFloat(easing, "x2", 1f);
                SetFloat(easing, "y2", 1f);
            }

            // ---------- 文字：t-1 大标题 4px 硬影 ----------
            // em 字段保留（旧档回退用），像素字段才是 t-1 的实际来源：
            // 固定 4px 硬影不会随字号放大成糊块，方向（右下 = 正 x 负 y）。
            SetFloat(so, "chunkyOutlineWidth", 0.04f);
            SetFloat(so, "chunkyInnerOutlineWidth", 0f);
            SetBool(so, "chunkyShadowInPixels", true);
            SetVector2(so, "chunkyShadowOffsetPixels", new Vector2(4f, -4f)); // 大标题 4px 硬影（右下）
            // em 回退档：像素模式关掉时，在 100px 标题上等价于右下 4px 硬影（填充层向反方向让位）。
            SetFloat(so, "chunkyShadowOffsetX", -0.04f);
            SetFloat(so, "chunkyShadowOffsetY", 0.04f);

            // ---------- 字体：全站唯一家族 MiSans（正文 Semibold / 标题 Heavy）----------
            // 拉丁不再单独走 TitanOne/Rubik：四家族混排的中英笔画粗细、行高、字距都不一致。
            SetObject(so, "displayFont", LoadFont("MiSans-Heavy SDF"));
            SetObject(so, "bodyFont", LoadFont("MiSans-Semibold SDF"));
            SetObject(so, "displayCjkFont", LoadFont("MiSans-Heavy SDF"));
            SetObject(so, "bodyCjkFont", LoadFont("MiSans-Semibold SDF"));

            // ---------- 卡片预设 ----------
            ApplyCardPreset(so, "teamCard", ComicStyle.Paper, ComicStyle.Paper, teamTint: true);
            ApplyCardPreset(so, "darkCard", ComicStyle.Ink, ComicStyle.Paper, teamTint: false);
            ApplyCardPreset(so, "lightCard", ComicStyle.Paper, ComicStyle.Ink, teamTint: false);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);

            ApplyGaugeStyle();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReportContrast();
            Debug.Log(k_LogTag + " 已应用：直角 / 实线 " + ComicStyle.Border + "px / 零模糊硬偏移阴影（按钮 3,-4 · 面板 4,-5 · 弹窗 8,-9） / 按压 0ms+0.1s Cubic-Out / 入场 0.22s Back-Out / 中文字体已接。");
        }

        [MenuItem("P1/画风/几何风/1b. 校验几何 token 对比度", false, 3)]
        [MenuItem("P1/画风/漫画风/1b. 校验几何 token 对比度（旧路径）", false, 3)]
        public static void ReportContrast()
        {
            float inkOnPaper = ComicStyle.ContrastRatio(ComicStyle.Ink, ComicStyle.Paper);
            float paperOnInk = ComicStyle.ContrastRatio(ComicStyle.Paper, ComicStyle.Ink);
            float whiteOnRed = ComicStyle.ContrastRatio(Color.white, ComicStyle.Red);
            float inkOnYellow = ComicStyle.ContrastRatio(ComicStyle.Ink, ComicStyle.Yellow);
            float inkOnBlue = ComicStyle.ContrastRatio(ComicStyle.Ink, ComicStyle.Blue);

            string line = string.Format(
                "{0} 对比度：墨/纸 {1:F2}（≥4.5 AA）| 纸/墨 {2:F2} | 白/红 {3:F2} | 墨/黄 {4:F2} | 墨/蓝 {5:F2}",
                k_LogTag, inkOnPaper, paperOnInk, whiteOnRed, inkOnYellow, inkOnBlue);

            if (inkOnPaper < 4.5f) Debug.LogError(line + " ⚠ 纸上正文不达 AA");
            else Debug.Log(line + " ✓");
        }

        static void ApplyGaugeStyle()
        {
            var gauge = AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(GaugePath);
            if (gauge == null)
            {
                Debug.LogWarning(k_LogTag + " 找不到 " + GaugePath + "，跳过 HUD 计量样式。");
                return;
            }

            var so = new SerializedObject(gauge);
            // 旧球形仪表遗留字段（P14-H 起 HUD 改成底部横向条，代码不再读取）：
            // 仍写一遍是为了资产校验/差异对比稳定，不影响实机布局。
            SetVector2(so, "orbAnchoredPosition", new Vector2(-130f, 150f));
            SetFloat(so, "orbSize", 156f);
            SetVector2(so, "tankAnchoredPosition", new Vector2(-250f, 180f));
            SetVector2(so, "tankSize", new Vector2(66f, 214f));
            // 横向仪表条的读数与提示：移动端字号由 UITypography 再放大，
            // 这里给桌面基准（24 与条高 18 匹配，不再出现"数字比条还高"）。
            SetFloat(so, "percentFontSize", 24f);
            SetFloat(so, "hintFontSize", 16f);
            SetVector2(so, "readyHintSize", new Vector2(92f, 26f));
            SetFloat(so, "specialIconSize", 24f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gauge);
        }

        static TMP_FontAsset LoadFont(string assetName)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFolder + assetName + ".asset");
            if (font == null) Debug.LogWarning(k_LogTag + " 缺字体资产: " + assetName + "（先跑菜单 0）");
            return font;
        }

        static void ApplyCardPreset(SerializedObject so, string root, Color fill, Color title, bool teamTint)
        {
            var p = so.FindProperty(root);
            if (p == null)
            {
                Debug.LogWarning(k_LogTag + " UITheme 缺卡片预设字段: " + root);
                return;
            }

            SetInt(p, "shape", (int)UIInkShape.SharpRect);
            SetColor(p, "fillColor", fill);
            SetColor(p, "titleColor", title);
            SetBool(p, "fillUsesTeamColor", teamTint);
            SetColor(p, "borderColor", ComicStyle.Ink);
            SetFloat(p, "borderWidth", ComicStyle.Border);
            SetFloat(p, "cornerRadius", ComicStyle.CornerSharp);
            SetFloat(p, "wobble", 0f);
            SetFloat(p, "glossAlpha", 0f);
            SetFloat(p, "innerRimAlpha", 0f);
            SetBool(p, "comicPanel", false);
            SetBool(p, "halftone", false);
            SetBool(p, "speechTail", false);
            SetVector2(p, "shadowOffset", Vector2.zero);
            SetFloat(p, "shadowPadding", 0f);
            SetFloat(p, "contentPadding", 20f);
        }

        // ---------- SerializedProperty 小工具 ----------

        static void SetColor(SerializedObject so, string path, Color value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.colorValue = value;
            else Debug.LogWarning(k_LogTag + " 缺字段: " + path);
        }

        static void SetColor(SerializedProperty parent, string path, Color value)
        {
            var p = parent?.FindPropertyRelative(path);
            if (p != null) p.colorValue = value;
        }

        static void SetFloat(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning(k_LogTag + " 缺字段: " + path);
        }

        static void SetFloat(SerializedProperty parent, string path, float value)
        {
            var p = parent?.FindPropertyRelative(path);
            if (p != null) p.floatValue = value;
        }

        static void SetInt(SerializedProperty parent, string path, int value)
        {
            var p = parent?.FindPropertyRelative(path);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning(k_LogTag + " 缺字段: " + path);
        }

        static void SetBool(SerializedProperty parent, string path, bool value)
        {
            var p = parent?.FindPropertyRelative(path);
            if (p != null) p.boolValue = value;
        }

        static void SetVector2(SerializedObject so, string path, Vector2 value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.vector2Value = value;
            else Debug.LogWarning(k_LogTag + " 缺字段: " + path);
        }

        static void SetVector2(SerializedProperty parent, string path, Vector2 value)
        {
            var p = parent?.FindPropertyRelative(path);
            if (p != null) p.vector2Value = value;
        }

        static void SetObject(SerializedObject so, string path, Object value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning(k_LogTag + " 缺字段: " + path);
        }

        static void SetSpring(SerializedObject so, string path, float frequency, float damping)
        {
            var p = so.FindProperty(path);
            if (p == null)
            {
                Debug.LogWarning(k_LogTag + " 缺弹簧字段: " + path);
                return;
            }

            var f = p.FindPropertyRelative("frequency");
            var d = p.FindPropertyRelative("damping");
            if (f != null) f.floatValue = frequency;
            if (d != null) d.floatValue = damping;
        }

        static void SetEasing(SerializedObject so, string path, float x1, float y1, float x2, float y2)
        {
            var p = so.FindProperty(path);
            if (p == null)
            {
                Debug.LogWarning(k_LogTag + " 缺缓动字段: " + path);
                return;
            }

            SetFloat(p, "x1", x1);
            SetFloat(p, "y1", y1);
            SetFloat(p, "x2", x2);
            SetFloat(p, "y2", y2);
        }
    }
}
