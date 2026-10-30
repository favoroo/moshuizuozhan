using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace P1.Editor
{
    /// <summary>
    /// 漫画风中文字体管线：把 Art/Fonts 下的两款中文字体（MiSans Heavy / Semibold，小米官方免费商用）
    /// 烧成 TMP 动态字体资产，并把它们接进 TMP 的 fallback 链。
    ///
    /// 为什么必须有这个脚本：
    /// - 项目原有的 TitanOne / Rubik 都是 Google Fonts 的 latin 子集，一个汉字都没有，
    ///   设置页的中文全部渲染成 □（见 Docs/UI-ComicStyle.md 的根因说明）；
    /// - TMP 字体资产 = 图集 + 材质 + 字符表复合资产，只能 API 生成，不能手改 .asset；
    /// - fallback 链必须同时写进「每个字体资产」和「TMP Settings 全局表」，
    ///   否则任何一处 new TextMeshProUGUI 忘记指定字体就会重新出现方块字。
    ///
    /// 中文用 48pt / padding 5 / 1024² 动态图集：只栅格化实际用到的字，
    /// 300 来个 UI 用字约 1~2 张图集，移动端显存可控；多图集开启保证后续新增文案不炸。
    /// </summary>
    public static class ComicFontAssetBuilder
    {
        const string k_LogTag = "[漫画字体]";

        public const string FontsFolder = "Assets/_Project/Art/Fonts/";
        public const string TmpFolder = FontsFolder + "TMP/";

        public const string DisplayCjkTtf = FontsFolder + "MiSans-Heavy.ttf";
        public const string BodyCjkTtf = FontsFolder + "MiSans-Semibold.ttf";
        public const string DisplayCjkAsset = TmpFolder + "MiSans-Heavy SDF.asset";
        public const string BodyCjkAsset = TmpFolder + "MiSans-Semibold SDF.asset";

        const string LatinDisplayAsset = TmpFolder + "TitanOne-Regular SDF.asset";
        const string LatinBodyAsset = TmpFolder + "Rubik-Bold SDF.asset";

        const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>ASCII + 常用标点：预烘进图集，避免运行时第一帧集中栅格化造成卡顿。</summary>
        const string k_LatinPreset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "·×÷°±—…“”‘’《》【】、。，；：！？（）";

        [MenuItem("P1/画风/漫画风/0. 构建中文字体与 Fallback", false, 0)]
        public static void BuildChineseFonts()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(TmpFolder);
            AssetDatabase.Refresh();

            var display = EnsureFontAsset(DisplayCjkTtf, DisplayCjkAsset, samplingPointSize: 48, atlasPadding: 5);
            var body = EnsureFontAsset(BodyCjkTtf, BodyCjkAsset, samplingPointSize: 48, atlasPadding: 5);

            if (display == null || body == null)
            {
                Debug.LogError(k_LogTag + " 中文字体资产创建失败，无法继续配置 fallback。");
                return;
            }

            WireFallbacks(display, body);

            int missing = VerifyCoverage(display, body);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogFormat("{0} 完成：{1} + {2}，缺字 {3} 个{4}",
                k_LogTag,
                Path.GetFileName(DisplayCjkAsset),
                Path.GetFileName(BodyCjkAsset),
                missing,
                missing == 0 ? " ✓" : " ⚠ 见上方缺字清单");
        }

        /// <summary>只做检查，不改任何资产。给 CI / 验收用。</summary>
        [MenuItem("P1/画风/漫画风/0b. 中文字形覆盖率自检", false, 1)]
        public static void VerifyOnly()
        {
            AssetDatabase.Refresh();
            var display = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayCjkAsset);
            var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyCjkAsset);

            if (display == null || body == null)
            {
                Debug.LogError(k_LogTag + " 缺中文字体资产，先跑「0. 构建中文字体与 Fallback」");
                return;
            }

            int missing = VerifyCoverage(display, body);
            Debug.LogFormat("{0} 自检：缺字 {1} 个{2}", k_LogTag, missing, missing == 0 ? " ✓" : " ⚠");
        }

        static TMP_FontAsset EnsureFontAsset(string ttfPath, string assetPath, int samplingPointSize, int atlasPadding)
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

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                // 幂等：已有资产只补预烘字符与参数，不重建成孤儿图集。
                if (existing.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                {
                    Debug.LogWarning(k_LogTag + " " + assetPath + " 不是 Dynamic，重建。");
                    AssetDatabase.DeleteAsset(assetPath);
                    existing = null;
                }
            }

            if (existing != null)
            {
                existing.TryAddCharacters(k_LatinPreset, out _);
                existing.isMultiAtlasTexturesEnabled = true;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font,
                samplingPointSize: samplingPointSize,
                atlasPadding: atlasPadding,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError(k_LogTag + " TMP 字体资产创建失败: " + ttfPath);
                return null;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            fontAsset.TryAddCharacters(k_LatinPreset, out string missing);
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;

            AssetDatabase.CreateAsset(fontAsset, assetPath);

            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                var tex = fontAsset.atlasTextures[i];
                if (tex == null) continue;
                tex.name = fontAsset.name + (i == 0 ? " Atlas" : " Atlas " + i);
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            Debug.LogFormat("{0} 生成 {1} | 预烘 {2} 字{3}",
                k_LogTag, assetPath, fontAsset.characterTable.Count,
                string.IsNullOrEmpty(missing) ? "" : "，预烘缺字: " + missing);
            return fontAsset;
        }

        /// <summary>
        /// fallback 链：拉丁显示体 → MiSans Heavy；拉丁正文体 → MiSans Semibold；
        /// 全局表兜底两款中文字体（覆盖 new TextMeshProUGUI 未指定字体的地方）。
        /// </summary>
        static void WireFallbacks(TMP_FontAsset display, TMP_FontAsset body)
        {
            var latinDisplay = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinDisplayAsset);
            var latinBody = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LatinBodyAsset);

            SetFallbacks(latinDisplay, new[] { display, body });
            SetFallbacks(latinBody, new[] { body, display });
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

        /// <summary>
        /// 扫描 UI 源码里出现过的所有汉字，逐个问字体「运行时能不能栅格化」。
        /// 返回缺字数；有任何缺字都打 error，避免「设置页还是方块」这种问题漏到真机。
        /// </summary>
        static int VerifyCoverage(TMP_FontAsset display, TMP_FontAsset body)
        {
            var required = CollectCjkFromSources();
            if (required.Count == 0)
            {
                Debug.LogWarning(k_LogTag + " 没在 UI 源码里扫到汉字，跳过覆盖率检查。");
                return 0;
            }

            // 注意：不能用 TMP_FontAsset.HasCharacters 做检查——动态图集只认「已经栅格化进图集」的字，
            // 没显示过的字一律返回 false，会假报上千个缺字。这里查源字体的 cmap 才准。
            var displayFont = AssetDatabase.LoadAssetAtPath<Font>(DisplayCjkTtf);
            var bodyFont = AssetDatabase.LoadAssetAtPath<Font>(BodyCjkTtf);
            if (displayFont == null || bodyFont == null)
            {
                Debug.LogError(k_LogTag + " 源字体加载失败，无法做覆盖率检查。");
                return required.Count;
            }

            var missing = new StringBuilder();
            int missingCount = 0;

            foreach (var ch in required)
            {
                if (displayFont.HasCharacter(ch) || bodyFont.HasCharacter(ch)) continue;
                missingCount++;
                if (missing.Length < 120) missing.Append(ch);
            }

            var seen = required;

            // 装饰符号单独查一遍：★ ▶ ◀ ↑↓ ▲ 这类字符不在中文字体的 cmap 里，
            // 混进中文串会直接渲染成 □（实测 ◀ 被替换成 Unicode 替换符）。UI 里只用这套安全符号。
            const string requiredSymbols = "【】「」『』《》〈〉·—…、。，；：！？（）～＋－×÷％";
            var missingSymbols = new StringBuilder();
            foreach (char ch in requiredSymbols)
            {
                if (displayFont.HasCharacter(ch) || bodyFont.HasCharacter(ch)) continue;
                missingSymbols.Append(ch);
            }

            if (missingSymbols.Length > 0)
            {
                missingCount += missingSymbols.Length;
                Debug.LogErrorFormat("{0} 装饰符号缺字：{1}", k_LogTag, missingSymbols);
            }

            if (missingCount > 0)
            {
                Debug.LogErrorFormat("{0} 缺字 {1} 个：{2}（检查字体文件是否为完整 CJK 版本）",
                    k_LogTag, missingCount, missing);
            }
            else
            {
                Debug.LogFormat("{0} 覆盖率 OK：{1} 个唯一汉字全部可渲染", k_LogTag, seen.Count);
            }

            return missingCount;
        }

        static HashSet<char> CollectCjkFromSources()
        {
            var result = new HashSet<char>();
            var roots = new[]
            {
                "Assets/_Project/Scripts/UI",
                "Assets/_Project/Scripts/Core",
                "Assets/_Project/Scripts/Player",
                "Assets/_Project/Scripts/Weapons",
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (IOException) { continue; }

                    foreach (char ch in text)
                    {
                        if (ch >= '\u4e00' && ch <= '\u9fff') result.Add(ch);
                    }
                }
            }

            return result;
        }
    }
}
