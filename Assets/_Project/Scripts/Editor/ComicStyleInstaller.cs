using P1.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 把漫画风 token 写进 ScriptableObject 资产（遵守「不手改 .asset YAML」的硬性规则）。
    ///
    /// 用法：菜单 `P1/画风/漫画风/1. 应用漫画主题`。
    /// 幂等：重复执行只会把值写成同一套，不会产生重复资产或孤儿材质。
    /// </summary>
    public static class ComicStyleInstaller
    {
        const string k_LogTag = "[漫画主题]";

        const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        const string GaugePath = "Assets/_Project/ScriptableObjects/HudGaugeStyle.asset";
        const string TmpFolder = "Assets/_Project/Art/Fonts/TMP/";

        [MenuItem("P1/画风/漫画风/1. 应用漫画主题（UITheme + HUD 样式）", false, 2)]
        public static void ApplyComicTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            if (theme == null)
            {
                Debug.LogError(k_LogTag + " 找不到 " + ThemePath);
                return;
            }

            var so = new SerializedObject(theme);

            // ---------- 调色板 ----------
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

            // ---------- 墨线与硬阴影 ----------
            SetFloat(so, "inkBorderWidth", ComicStyle.Border);
            SetFloat(so, "inkBorderHeavyWidth", ComicStyle.BorderHeavy);
            SetVector2(so, "hardShadowOffset", new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset));
            SetBool(so, "halftoneOnMobile", true);

            SetFloat(so, "halftoneSpacing", ComicStyle.HalftoneSpacing);
            SetFloat(so, "halftoneDotRadius", ComicStyle.HalftoneDotRadius);
            SetFloat(so, "halftoneAlpha", ComicStyle.HalftoneAlpha);
            SetFloat(so, "halftoneHoverAlpha", ComicStyle.HalftoneHoverAlpha);

            // ---------- 动效：无回弹硬切 ----------
            SetFloat(so, "pressDuration", ComicStyle.PressDuration);
            SetFloat(so, "hoverDuration", ComicStyle.HoverDuration);
            SetFloat(so, "entranceDuration", ComicStyle.EntranceDuration);
            SetFloat(so, "hoverScale", ComicStyle.HoverScale);
            SetFloat(so, "hoverTiltDegrees", ComicStyle.HoverTiltDegrees);
            SetFloat(so, "pressScale", ComicStyle.PressScale);
            SetFloat(so, "entranceFadeDuration", 0.1f);

            // 阻尼比 1.0 = 临界阻尼，数学上不可能过冲；频率 16~20Hz 保证 80ms 内落定。
            SetSpring(so, "popSpring", 16f, 1f);
            SetSpring(so, "pressSpring", 20f, 1f);
            SetSpring(so, "followSpring", 14f, 1f);
            SetSpring(so, "settleSpring", 12f, 1f);

            var easing = so.FindProperty("entranceEasing");
            if (easing != null)
            {
                SetFloat(easing, "x1", 0.22f);
                SetFloat(easing, "y1", 1f);
                SetFloat(easing, "x2", 0.36f);
                SetFloat(easing, "y2", 1f);
            }

            // ---------- 文字：更细的描边 + 右下硬阴影 ----------
            SetFloat(so, "chunkyOutlineWidth", 0.06f);
            SetFloat(so, "chunkyInnerOutlineWidth", 0.05f);
            SetFloat(so, "chunkyShadowOffsetY", 0.045f);
            SetFloat(so, "chunkyShadowOffsetX", 0.03f);

            // ---------- 字体 ----------
            SetObject(so, "displayFont", LoadFont("TitanOne-Regular SDF"));
            SetObject(so, "bodyFont", LoadFont("Rubik-Bold SDF"));
            SetObject(so, "displayCjkFont", LoadFont("MiSans-Heavy SDF"));
            SetObject(so, "bodyCjkFont", LoadFont("MiSans-Semibold SDF"));

            // ---------- 卡片预设 ----------
            ApplyCardPreset(so, "teamCard", teamTint: true, halftone: true);
            ApplyCardPreset(so, "darkCard", teamTint: false, halftone: false);
            ApplyCardPreset(so, "lightCard", teamTint: false, halftone: true);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);

            ApplyGaugeStyle();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReportContrast();
            Debug.Log(k_LogTag + " 已应用：墨线 " + ComicStyle.Border + "px / 硬阴影 " +
                      ComicStyle.ShadowOffset + "px / 动效 ≤100ms / 中文字体已接。");
        }

        [MenuItem("P1/画风/漫画风/1b. 校验漫画 token 对比度", false, 3)]
        public static void ReportContrast()
        {
            float inkOnPaper = ComicStyle.ContrastRatio(ComicStyle.Ink, ComicStyle.Paper);
            float paperOnInk = ComicStyle.ContrastRatio(ComicStyle.Paper, ComicStyle.Ink);
            float whiteOnRed = ComicStyle.ContrastRatio(Color.white, ComicStyle.Red);
            float inkOnYellow = ComicStyle.ContrastRatio(ComicStyle.Ink, ComicStyle.Yellow);

            string line = string.Format(
                "{0} 对比度：墨/纸 {1:F2}（≥4.5 AA）| 纸/墨 {2:F2} | 白/红 {3:F2} | 墨/黄 {4:F2}",
                k_LogTag, inkOnPaper, paperOnInk, whiteOnRed, inkOnYellow);

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
            SetVector2(so, "orbAnchoredPosition", new Vector2(-360f, -156f));
            SetFloat(so, "orbSize", 156f);
            SetVector2(so, "tankAnchoredPosition", new Vector2(-486f, -196f));
            SetVector2(so, "tankSize", new Vector2(66f, 214f));
            SetFloat(so, "percentFontSize", 22f);
            SetFloat(so, "hintFontSize", 20f);
            SetVector2(so, "readyHintSize", new Vector2(176f, 36f));
            SetFloat(so, "specialIconSize", 48f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gauge);
        }

        static TMP_FontAsset LoadFont(string assetName)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFolder + assetName + ".asset");
            if (font == null) Debug.LogWarning(k_LogTag + " 缺字体资产: " + assetName + "（先跑菜单 0）");
            return font;
        }

        static void ApplyCardPreset(SerializedObject so, string root, bool teamTint, bool halftone)
        {
            var p = so.FindProperty(root);
            if (p == null)
            {
                Debug.LogWarning(k_LogTag + " UITheme 缺卡片预设字段: " + root);
                return;
            }

            SetInt(p, "shape", (int)UIInkShape.RoundedRect);
            SetBool(p, "fillUsesTeamColor", teamTint);
            SetColor(p, "borderColor", ComicStyle.Ink);
            SetFloat(p, "borderWidth", ComicStyle.Border);
            SetFloat(p, "cornerRadius", ComicStyle.CornerSharp);
            SetFloat(p, "wobble", 0f);
            SetFloat(p, "glossAlpha", 0f);
            SetFloat(p, "innerRimAlpha", 0f);
            SetBool(p, "comicPanel", true);
            SetBool(p, "halftone", halftone);
            SetBool(p, "speechTail", false);
            SetVector2(p, "shadowOffset", new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset));
            SetFloat(p, "shadowPadding", 8f);
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