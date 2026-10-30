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

            // ---------- 实线边框；阴影与网点全局禁用 ----------
            SetFloat(so, "inkBorderWidth", ComicStyle.Border);
            SetFloat(so, "inkBorderHeavyWidth", ComicStyle.BorderHeavy);
            SetVector2(so, "hardShadowOffset", Vector2.zero);
            SetBool(so, "halftoneOnMobile", false);

            SetFloat(so, "halftoneSpacing", ComicStyle.HalftoneSpacing);
            SetFloat(so, "halftoneDotRadius", ComicStyle.HalftoneDotRadius);
            SetFloat(so, "halftoneAlpha", 0f);
            SetFloat(so, "halftoneHoverAlpha", 0f);

            // ---------- 动效：≤100ms linear 硬切，无回弹 ----------
            SetFloat(so, "pressDuration", ComicStyle.PressDuration);
            SetFloat(so, "hoverDuration", ComicStyle.HoverDuration);
            SetFloat(so, "entranceDuration", ComicStyle.EntranceDuration);
            SetFloat(so, "hoverScale", ComicStyle.HoverScale);
            SetFloat(so, "hoverTiltDegrees", 0f);
            SetFloat(so, "pressScale", ComicStyle.PressScale);
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

            // ---------- 文字：几何风不做硬阴影套色 ----------
            SetFloat(so, "chunkyOutlineWidth", 0.04f);
            SetFloat(so, "chunkyInnerOutlineWidth", 0f);
            SetFloat(so, "chunkyShadowOffsetY", 0f);
            SetFloat(so, "chunkyShadowOffsetX", 0f);

            // ---------- 字体 ----------
            SetObject(so, "displayFont", LoadFont("TitanOne-Regular SDF"));
            SetObject(so, "bodyFont", LoadFont("Rubik-Bold SDF"));
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
            Debug.Log(k_LogTag + " 已应用：直角 / 实线 " + ComicStyle.Border + "px / 无阴影无网点 / 动效 ≤100ms linear / 中文字体已接。");
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
    }
}
