using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;   // GlyphRenderMode（TextCore 命名空间，不是 TMPro）

namespace P1.Editor
{
    /// <summary>
    /// UI 资产工具（P1-C Art 域）：把 Art/Fonts 下的静态 TTF 生成为 TMP 字体资产，
    /// 并给图标库/字体做一次可读回控制台的体检。
    ///
    /// 为什么需要编辑器脚本而不是手点：
    /// - TMP 字体资产= 字体图集 + 材质 + 元数据的复合资产，必须用 API 生成并 AddObjectToAsset 存盘；
    /// - 图标库是 66 个按分类归档的精灵，「全部导对了吗」只能靠脚本逐个体检，
    ///   agent 看不见 Inspector（见 AGENTS.md「Agent 看不见画面」一节）。
    ///
    /// 图标本身不在这里生成：源 SVG 的烘焙走 `python3 tools/bake_icons.py`（可复现、零配额）。
    /// </summary>
    public static class UIAssetTools
    {
        const string k_LogTag = "[UI资产]";

        public const string IconsFolder = "Assets/_Project/Art/Sprites/Icons/";
        public const string FontsFolder = "Assets/_Project/Art/Fonts/";
        public const string TmpFolder = FontsFolder + "TMP/";

        /// <summary>
        /// 预烘进图集的字符集：ASCII 可见字符 + Latin-1 + 常见排版符号。
        /// 字体是 Google Fonts 的 latin 子集（不含 CJK），缺字由 TMP 的 fallback 处理。
        /// </summary>
        const string k_PresetCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "¡¢£¤¥¦§¨©ª«¬­®¯°±²³´µ¶·¸¹º»¼½¾¿×÷" +
            "–—‘’“”…•™→←↑↓×÷°±·«»";

        [MenuItem("P1/素材/4. 生成 TMP 字体资产（Art/Fonts）", false, 4)]
        public static void CreateTmpFontAssets()
        {
            AssetDatabase.Refresh();
            if (!EnsureTmpEssentials()) return;

            var fonts = FindFontFiles();
            if (fonts.Count == 0)
            {
                Debug.LogWarning(k_LogTag + " " + FontsFolder + " 下没有找到 .ttf，先跑 python3 tools/bake_fonts.py");
                return;
            }

            Directory.CreateDirectory(TmpFolder);
            AssetDatabase.Refresh();

            int created = 0, failed = 0;
            foreach (var fontPath in fonts)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font == null)
                {
                    Debug.LogError(k_LogTag + " 字体加载失败: " + fontPath);
                    failed++;
                    continue;
                }

                string assetPath = TmpFolder + Path.GetFileNameWithoutExtension(fontPath) + " SDF.asset";
                var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (existing != null)
                {
                    // 重跑时整份替换，避免旧图集/旧材质残留成孤儿子资产
                    AssetDatabase.DeleteAsset(assetPath);
                }

                var fontAsset = TMP_FontAsset.CreateFontAsset(
                    font,
                    samplingPointSize: 90,
                    atlasPadding: 9,
                    renderMode: GlyphRenderMode.SDFAA,
                    atlasWidth: 1024,
                    atlasHeight: 1024,
                    atlasPopulationMode: AtlasPopulationMode.Dynamic,
                    enableMultiAtlasSupport: true);

                if (fontAsset == null)
                {
                    Debug.LogError(k_LogTag + " TMP 字体资产创建失败: " + fontPath +
                                   "（检查 Font Importer 的 Include Font Data 是否勾选）");
                    failed++;
                    continue;
                }

                fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
                fontAsset.TryAddCharacters(k_PresetCharacters, out string missing);
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

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

                Debug.LogFormat("{0} 生成 {1} | 图集字符 {2} 个{3}",
                    k_LogTag, assetPath, fontAsset.characterTable.Count,
                    string.IsNullOrEmpty(missing) ? "" : "，缺字: " + missing);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.LogFormat("{0} TMP 字体资产：完成 {1} 个，失败 {2} 个 → {3}", k_LogTag, created, failed, TmpFolder);
        }

        /// <summary>
        /// 图标 + 字体体检。batchmode 可直接 `-executeMethod P1.Editor.UIAssetTools.ReportUIAssets`。
        /// </summary>
        [MenuItem("P1/素材/5. 图标与字体体检", false, 5)]
        public static void ReportUIAssets()
        {
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            int assets = 0, problems = 0;

            sb.AppendLine(k_LogTag + " ===== 图标精灵（应为纯白 + alpha，可被 Image.color 无损染色）=====");
            problems += ReportIcons(sb, ref assets);

            sb.AppendLine(k_LogTag + " ===== 字体 =====");
            problems += ReportFonts(sb, ref assets);

            sb.AppendLine(k_LogTag + " ===== 汇总 =====");
            sb.AppendLine(string.Format("{0} 资产 {1} 个，异常 {2} 项{3}",
                k_LogTag, assets, problems, problems == 0 ? " ✓" : " ⚠ 见上方 ⚠ 行"));

            // 体检结果始终走 Log：这个报告是给人/agent 读的，不该污染「运行时 0 error」的验收门。
            // 有问题时正文里的 ⚠ 行就是信号。
            Debug.Log(sb.ToString());
        }

        static int ReportIcons(StringBuilder sb, ref int assets)
        {
            if (!AssetDatabase.IsValidFolder(IconsFolder))
            {
                sb.AppendLine(k_LogTag + " ⚠ 图标目录不存在: " + IconsFolder);
                return 1;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { IconsFolder });
            int bad = 0;
            var byFolder = new Dictionary<string, int>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                assets++;

                var folder = Path.GetFileName(Path.GetDirectoryName(path));
                byFolder[folder] = byFolder.TryGetValue(folder, out int n) ? n + 1 : 1;

                var issues = new List<string>();

                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    sb.AppendLine(string.Format("{0} ⚠ {1} 没有 TextureImporter", k_LogTag, path));
                    bad++;
                    continue;
                }

                var s = new TextureImporterSettings();
                importer.ReadTextureSettings(s);

                if (s.textureType != TextureImporterType.Sprite) issues.Add("应为 Sprite(2D and UI)");
                if (!s.alphaIsTransparency) issues.Add("alphaIsTransparency 关");
                if (s.mipmapEnabled) issues.Add("多余 mip");
                if (s.wrapMode != TextureWrapMode.Clamp) issues.Add("wrap 应为 Clamp");

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    // 强刷前一刻刚批量拷入的图，AssetDatabase 可能还在排队导入：
                    // 同步导入一次再判定，避免把「正在导入」误报成「Sprite 为空」。
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
                if (sprite == null) issues.Add("Sprite 加载为空");

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) issues.Add("Texture2D 加载为空");

                // PNG 有 alpha 通道吗：IHDR 第 25 字节是 color type（4/6 带 alpha）
                byte colorType = ReadPngColorType(path);
                if (colorType != 4 && colorType != 6) issues.Add("PNG 无 alpha 通道 (colorType=" + colorType + ")");

                if (issues.Count > 0)
                {
                    sb.AppendLine(string.Format("{0} ⚠ {1} | {2}x{3} type={4} alpha={5} | {6}",
                        k_LogTag, path, tex != null ? tex.width : 0, tex != null ? tex.height : 0,
                        s.textureType, s.alphaIsTransparency ? "T" : "F", string.Join("；", issues)));
                    bad++;
                }
            }

            foreach (var kv in byFolder)
                sb.AppendLine(string.Format("{0}   {1}: {2} 个", k_LogTag, kv.Key, kv.Value));
            sb.AppendLine(string.Format("{0}   图标合计 {1} 个，异常 {2} 个", k_LogTag, guids.Length, bad));
            return bad;
        }

        static int ReportFonts(StringBuilder sb, ref int assets)
        {
            int bad = 0;
            var fonts = FindFontFiles();

            foreach (var path in fonts)
            {
                assets++;
                var importer = AssetImporter.GetAtPath(path) as TrueTypeFontImporter;
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font == null || importer == null)
                {
                    sb.AppendLine(string.Format("{0} ⚠ 字体加载失败: {1}", k_LogTag, path));
                    bad++;
                    continue;
                }

                string tmpPath = TmpFolder + Path.GetFileNameWithoutExtension(path) + " SDF.asset";
                var tmp = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpPath);

                var issues = new List<string>();
                if (!importer.includeFontData) issues.Add("Include Font Data 关（TMP 动态图集需要）");
                if (tmp == null) issues.Add("缺 TMP 字体资产（跑菜单 4）");
                else
                {
                    if (tmp.material == null) issues.Add("TMP 材质为空");
                    if (tmp.atlasTextures == null || tmp.atlasTextures.Length == 0 || tmp.atlasTextures[0] == null)
                        issues.Add("TMP 图集为空");
                    if (tmp.characterTable.Count == 0) issues.Add("TMP 图集未预烘字符");
                }

                string line = string.Format("{0} {1} | '{2}' | TMP={3}{4}",
                    k_LogTag, path, font.name,
                    tmp == null ? "缺" : tmp.characterTable.Count + " 字符/" + tmp.atlasPopulationMode,
                    issues.Count > 0 ? " | ⚠ " + string.Join("；", issues) : " OK");

                sb.AppendLine(line);
                if (issues.Count > 0) bad++;
            }

            if (fonts.Count == 0) sb.AppendLine(k_LogTag + " ⚠ 没有字体（先跑 python3 tools/bake_fonts.py）");
            return bad;
        }

        static List<string> FindFontFiles()
        {
            var result = new List<string>();
            if (!Directory.Exists(FontsFolder)) return result;
            foreach (var file in Directory.GetFiles(FontsFolder, "*.ttf"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith(".", System.StringComparison.Ordinal)) continue;
                result.Add(FontsFolder + name);
            }
            result.Sort(System.StringComparer.Ordinal);
            return result;
        }

        /// <summary>TMP 字体资产依赖 TMP Essential Resources（Settings/Shader/字体）。缺了就静默导入一次。</summary>
        static bool EnsureTmpEssentials()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;

            Debug.Log(k_LogTag + " 首次使用 TMP：导入 Essential Resources（项目内 Assets/TextMesh Pro/）");
            TMP_PackageResourceImporter.ImportResources(true, false, false);

            // 包导入是排队执行的：文件落盘后 AssetDatabase 还要再索引一次，
            // 立刻 Resources.Load 会拿到 null（实测）。这里有界重试，别让菜单被误判成失败。
            for (int i = 0; i < 60; i++)
            {
                AssetDatabase.Refresh();
                if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
                System.Threading.Thread.Sleep(100);
            }

            Debug.LogError(k_LogTag + " TMP Essential Resources 导入失败：Assets/TextMesh Pro/Resources/TMP Settings.asset 未就绪");
            return false;
        }

        static byte ReadPngColorType(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var header = new byte[26];
                    if (stream.Read(header, 0, header.Length) < header.Length) return 0;
                    if (header[0] != 0x89 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G') return 0;
                    return header[25];
                }
            }
            catch (IOException)
            {
                return 0;
            }
        }
    }
}