using System.Collections.Generic;
using System.IO;
using System.Text;
using P1.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace P1.Editor
{
    /// <summary>
    /// MiSans 字体管线（全站唯一字体家族）：把 Art/Fonts 下的两款中文字体
    /// （MiSans Semibold 650 / Heavy 900，小米官方免费商用）烧成 TMP 字体资产，
    /// 按用途分档采样、预烘源码字集，并接进 fallback 链。
    ///
    /// 为什么是全站唯一家族（2026-11-01 收口，参照 godot/t-1）：
    /// - 原来拉丁走 TitanOne/Rubik、中文走 MiSans，四个家族的字面度量/行高/字距都不同，
    ///   而 ResolveFont 按「含不含全角标点」整串换字体 → 中英混排笔画粗细与行高都不一致。
    ///   MiSans 自带完整拉丁字形（95/95），一个家族就能覆盖全站。
    /// - 旧资产是 48pt 单档采样，却要覆盖 18~169px：放大 3.5× 时边缘发糊、笔画显胖。
    ///   现在分两档：正文 Semibold 64pt（≤~64px 文字）、标题 Heavy 96pt（72~169px 文字）。
    ///
    /// 伪粗体收口（用户反馈「有些粗得不正常」的根因）：
    /// - 六个旧资产的 m_FontWeightTable 全空，任何 FontStyles.Bold 都落到 shader 的
    ///   _WeightBold（默认 0.75）膨胀，而基础字体本身已是 700/900 → 双重加粗、密笔汉字糊成黑块。
    ///   现在 boldStyle：正文 0.25（≈700 观感，对应 t-1 的 inline emph），标题 0（Heavy 不再叠加）。
    ///
    /// 预烘字集：账本由 `python3 tools/font_charset.py` 生成，读取口径见 <see cref="FontCharsetLedger"/>。
    /// 正文资产预烘整份字集（1268 字 ≈ 8 张 1024² 图集），标题资产只预烘基线（ASCII+符号 ≈ 2 张）：
    /// 96pt 字形框大、每张只装 81 字，把 1200 字全烘进去要多花 14MB 显存，而大字号几乎只有拉丁/数字。
    /// </summary>
    public static class ComicFontAssetBuilder
    {
        const string k_LogTag = "[MiSans 字体]";

        public const string FontsFolder = "Assets/_Project/Art/Fonts/";
        public const string TmpFolder = FontsFolder + "TMP/";

        public const string DisplayCjkTtf = FontsFolder + "MiSans-Heavy.ttf";
        public const string BodyCjkTtf = FontsFolder + "MiSans-Semibold.ttf";
        public const string DisplayCjkAsset = TmpFolder + "MiSans-Heavy SDF.asset";
        public const string BodyCjkAsset = TmpFolder + "MiSans-Semibold SDF.asset";

        /// <summary>已退役的拉丁资产：不再被主题/代码引用，只保留文件让旧场景引用不断链。</summary>
        static readonly string[] LegacyFontAssets =
        {
            TmpFolder + "TitanOne-Regular SDF.asset",
            TmpFolder + "Rubik-Bold SDF.asset",
        };

        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        // 采样分档（改这里要同步 tools/font_charset.py 的 ATLAS_SIZE/预算口径）
        const int BodySamplingPointSize = 64;
        const int BodyAtlasPadding = 6;
        const int DisplaySamplingPointSize = 96;
        const int DisplayAtlasPadding = 8;

        // 伪粗体量：正文"轻加粗"，标题不叠加
        const float BodyBoldStyle = 0.25f;
        const float DisplayBoldStyle = 0f;

        [MenuItem("P1/画风/漫画风/0. 构建中文字体与 Fallback", false, 0)]
        public static void BuildChineseFonts()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(TmpFolder);
            AssetDatabase.Refresh();

            if (!FontCharsetLedger.LedgersExist())
            {
                Debug.LogError(k_LogTag + " 缺字集账本，先跑：python3 tools/font_charset.py");
                return;
            }

            string charset = FontCharsetLedger.LoadCharset();
            string baseline = FontCharsetLedger.LoadBaseline();

            // 标题资产先建（正文资产的 fallback 指向它）
            var display = EnsureFontAsset(DisplayCjkTtf, DisplayCjkAsset,
                DisplaySamplingPointSize, DisplayAtlasPadding, baseline, DisplayBoldStyle);
            var body = EnsureFontAsset(BodyCjkTtf, BodyCjkAsset,
                BodySamplingPointSize, BodyAtlasPadding, charset, BodyBoldStyle);

            if (display == null || body == null)
            {
                Debug.LogError(k_LogTag + " 字体资产创建失败，无法继续配置 fallback。");
                return;
            }

            WireFallbacks(display, body);
            WireThemeFontFields(body, display);
            ClearLegacyFallbacks();

            var report = Verify();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = string.Format("{0} 完成：{1} + {2}；{3}",
                k_LogTag,
                Path.GetFileName(DisplayCjkAsset),
                Path.GetFileName(BodyCjkAsset),
                report.Format());
            if (report.Ok) Debug.Log(summary + " ✓");
            else Debug.LogError(summary + " ⚠ 缺字/漂移见上");
        }

        /// <summary>只做检查，不改任何资产。给 CI / 验收用。</summary>
        [MenuItem("P1/画风/漫画风/0b. 中文字形覆盖率自检", false, 1)]
        public static void VerifyOnly()
        {
            AssetDatabase.Refresh();
            var report = Verify();
            string line = k_LogTag + " 自检：" + report.Format();
            if (report.Ok) Debug.Log(line + " ✓");
            else Debug.LogError(line + " ⚠");
        }

        /// <summary>三段账验收（字集⊆预烘 / 源码⊆账本 / 豁免不上屏），菜单与测试共用。</summary>
        public static FontCharsetLedger.Report Verify()
        {
            var display = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayCjkAsset);
            var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyCjkAsset);
            var displayFont = AssetDatabase.LoadAssetAtPath<Font>(DisplayCjkTtf);
            var bodyFont = AssetDatabase.LoadAssetAtPath<Font>(BodyCjkTtf);

            if (display == null || body == null)
                Debug.LogWarning(k_LogTag + " 缺字体资产，先跑「0. 构建中文字体与 Fallback」再来自检。");

            return FontCharsetLedger.Analyze(body, display, bodyFont, displayFont);
        }

        static TMP_FontAsset EnsureFontAsset(string ttfPath, string assetPath,
            int samplingPointSize, int atlasPadding, string prebake, float boldStyle)
        {
            var importer = AssetImporter.GetAtPath(ttfPath) as TrueTypeFontImporter;
            if (importer == null)
            {
                Debug.LogError(k_LogTag + " 找不到字体文件或导入器: " + ttfPath);
                return null;
            }

            if (!importer.includeFontData)
            {
                // 动态图集要在运行时从源字体栅格化，必须把字体数据打进包体。
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogError(k_LogTag + " 字体加载失败: " + ttfPath);
                return null;
            }

            // 采样点/内边距不是可写属性，改档只能重建资产（GUID 可能变，实测同帧重建会保留原 GUID，但不要依赖）。
            // 重建后必须紧跟 WireFallbacks + WireThemeFontFields（本方法调用方已按序执行）。
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                bool wrongSampling = !Mathf.Approximately(existing.faceInfo.pointSize, samplingPointSize);
                bool wrongPadding = existing.atlasPadding != atlasPadding;
                if (existing.atlasPopulationMode != AtlasPopulationMode.Dynamic || wrongSampling || wrongPadding)
                {
                    Debug.LogWarningFormat("{0} {1} 参数变更（采样 {2}→{3} / padding {4}→{5}），重建资产（GUID 可能变，主题与 fallback 随后重指）。",
                        k_LogTag, assetPath,
                        existing.faceInfo.pointSize, samplingPointSize,
                        existing.atlasPadding, atlasPadding);
                    AssetDatabase.DeleteAsset(assetPath);
                    existing = null;
                }
            }

            if (existing == null)
            {
                existing = TMP_FontAsset.CreateFontAsset(
                    font,
                    samplingPointSize: samplingPointSize,
                    atlasPadding: atlasPadding,
                    renderMode: GlyphRenderMode.SDFAA,
                    atlasWidth: 1024,
                    atlasHeight: 1024,
                    atlasPopulationMode: AtlasPopulationMode.Dynamic,
                    enableMultiAtlasSupport: true);

                if (existing == null)
                {
                    Debug.LogError(k_LogTag + " TMP 字体资产创建失败: " + ttfPath);
                    return null;
                }

                existing.name = Path.GetFileNameWithoutExtension(assetPath);
                existing.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                existing.isMultiAtlasTexturesEnabled = true;

                AssetDatabase.CreateAsset(existing, assetPath);

                if (existing.material != null)
                {
                    existing.material.name = existing.name + " Material";
                    AssetDatabase.AddObjectToAsset(existing.material, existing);
                }
            }

            ApplyBoldStyle(existing, boldStyle);

            int prebakedBefore = existing.characterTable.Count;
            existing.TryAddCharacters(prebake, out string missing);

            for (int i = 0; i < existing.atlasTextures.Length; i++)
            {
                var tex = existing.atlasTextures[i];
                if (tex == null) continue;
                tex.name = existing.name + (i == 0 ? " Atlas" : " Atlas " + i);
                if (!AssetDatabase.Contains(tex)) AssetDatabase.AddObjectToAsset(tex, existing);
            }

            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();

            Debug.LogFormat("{0} {1}：采样 {2}pt/padding {3}，预烘 {4}→{5} 字，图集 {6} 张{7}",
                k_LogTag, Path.GetFileName(assetPath), samplingPointSize, atlasPadding,
                prebakedBefore, existing.characterTable.Count, existing.atlasTextures.Length,
                string.IsNullOrEmpty(missing) ? " ✓" : "，未预烘: " + missing);
            return existing;
        }

        /// <summary>
        /// 伪粗体量：FontStyles.Bold 在 TMP 里全靠这个值膨胀（_WeightBold），
        /// 资产与它的材质两处都要写，否则运行时会掉回 0.75 的默认值。
        /// </summary>
        static void ApplyBoldStyle(TMP_FontAsset fontAsset, float boldStyle)
        {
            fontAsset.normalStyle = 0f;
            fontAsset.boldStyle = boldStyle;

            if (fontAsset.material != null)
            {
                fontAsset.material.SetFloat(ShaderUtilities.ID_WeightNormal, 0f);
                fontAsset.material.SetFloat(ShaderUtilities.ID_WeightBold, boldStyle);
                EditorUtility.SetDirty(fontAsset.material);
            }

            EditorUtility.SetDirty(fontAsset);
        }

        /// <summary>
        /// fallback 链：正文 ↔ 标题互备；全局表兜底两款 MiSans
        /// （覆盖 new TextMeshProUGUI 未指定字体的地方）。
        /// </summary>
        static void WireFallbacks(TMP_FontAsset display, TMP_FontAsset body)
        {
            SetFallbacks(display, new[] { body });
            SetFallbacks(body, new[] { display });

            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning(k_LogTag + " 找不到 " + TmpSettingsPath + "，只配置了字体资产内的 fallback。");
                return;
            }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_fallbackFontAssets");
            if (prop == null)
            {
                Debug.LogWarning(k_LogTag + " TMP Settings 没有 m_fallbackFontAssets 字段，跳过全局 fallback。");
                return;
            }

            var list = new List<Object> { display, body };
            prop.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = list[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            Debug.Log(k_LogTag + " 已写入 TMP Settings 全局 fallback: MiSans Heavy → MiSans Semibold");
        }

        static void SetFallbacks(TMP_FontAsset target, TMP_FontAsset[] fallbacks)
        {
            if (target == null) return;
            target.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            target.fallbackFontAssetTable.Clear();
            foreach (var f in fallbacks)
                if (f != null && f != target) target.fallbackFontAssetTable.Add(f);
            EditorUtility.SetDirty(target);
        }

        /// <summary>把主题的四个字体字段一起指向 MiSans（重建资产后 GUID 可能变，必须重指）。</summary>
        public static bool WireThemeFontFields(TMP_FontAsset body, TMP_FontAsset display)
        {
            var theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            if (theme == null)
            {
                Debug.LogWarning(k_LogTag + " 找不到 " + ThemePath + "，跳过主题字体接线。");
                return false;
            }

            var so = new SerializedObject(theme);
            SetObject(so, "displayFont", display);
            SetObject(so, "bodyFont", body);
            SetObject(so, "displayCjkFont", display);
            SetObject(so, "bodyCjkFont", body);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);

            Debug.Log(k_LogTag + " 主题四字体字段 → MiSans Heavy（标题）/ MiSans Semibold（正文）");
            return true;
        }

        static void SetObject(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning(k_LogTag + " UITheme 缺字段: " + field);
                return;
            }

            prop.objectReferenceValue = value;
        }

        /// <summary>清掉已退役拉丁资产的 fallback 表，避免指向重建后失效的 MiSans GUID。</summary>
        static void ClearLegacyFallbacks()
        {
            foreach (string path in LegacyFontAssets)
            {
                var legacy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (legacy == null) continue;

                legacy.fallbackFontAssetTable?.Clear();
                EditorUtility.SetDirty(legacy);
            }
        }
    }
}
