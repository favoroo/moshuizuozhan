using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 素材工具：重新套用导入设置 + 素材体检。
    ///
    /// 体检是本项目「agent 看不见画面」原则在素材侧的落地：把每个资源的导入设置与关键属性
    /// 打到控制台，agent 用 `unity command console.get` 读回，不依赖用户截图转述。
    /// </summary>
    public static class MediaTools
    {
        const string k_LogTag = "[素材体检]";

        [MenuItem("P1/素材/1. 对选中资源重新套用导入设置", false, 1)]
        static void ReapplyToSelection()
        {
            var paths = Selection.assetGUIDs;
            if (paths == null || paths.Length == 0)
            {
                Debug.LogWarning(k_LogTag + " 没有选中任何资源。在 Project 窗口选中 Art/ 或 Audio/ 下的资源再执行。");
                return;
            }

            int done = 0, skipped = 0;
            foreach (var guid in paths)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texKind = MediaImportPolicy.ClassifyTexture(path);
                var audKind = MediaImportPolicy.ClassifyAudio(path);

                if (texKind != MediaImportPolicy.TextureKind.Ignore && AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    MediaImportPolicy.Apply(ti, texKind);
                    ti.SaveAndReimport();   // 这里必须存盘：改的是已导入资源，不重导入不生效
                    done++;
                }
                else if (audKind != MediaImportPolicy.AudioKind.Ignore && AssetImporter.GetAtPath(path) is AudioImporter ai)
                {
                    MediaImportPolicy.Apply(ai, audKind);
                    ai.SaveAndReimport();
                    done++;
                }
                else
                {
                    skipped++;
                }
            }

            Debug.LogFormat("{0} 重新套用导入设置：完成 {1} 个，跳过 {2} 个（跳过的是不在 Art/ Audio/ 策略目录下的资源）。",
                k_LogTag, done, skipped);
        }

        [MenuItem("P1/素材/2. 体检：列出已导入媒体与设置", false, 2)]
        public static void ReportMedia()
        {
            // batchmode 下新拷进来的文件需要先刷新才看得见
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            int assets = 0, problems = 0;

            sb.AppendLine(k_LogTag + " ===== 路径策略 =====");
            sb.AppendLine(k_LogTag + " Sprites → Sprite/alpha/mips off/Clamp | Pixel → 同前但 Point | Textures → mips on/Repeat");
            sb.AppendLine(k_LogTag + " Music → Streaming+Vorbis | SFX → DecompressOnLoad+PCM | LoopSFX → CompressedInMemory+PCM | Voice → CompressedInMemory+Vorbis");

            problems += ReportMissingMeta(sb, MediaImportPolicy.SpritesFolder, ref assets);
            problems += ReportMissingMeta(sb, MediaImportPolicy.TexturesFolder, ref assets);
            problems += ReportMissingMeta(sb, MediaImportPolicy.MusicFolder, ref assets);
            problems += ReportMissingMeta(sb, MediaImportPolicy.SfxFolder, ref assets);
            problems += ReportMissingMeta(sb, MediaImportPolicy.VoiceFolder, ref assets);

            sb.AppendLine(k_LogTag + " ===== 纹理 =====");
            problems += ReportTextures(sb, MediaImportPolicy.SpritesFolder, ref assets);
            problems += ReportTextures(sb, MediaImportPolicy.TexturesFolder, ref assets);

            sb.AppendLine(k_LogTag + " ===== 音频 =====");
            problems += ReportAudio(sb, MediaImportPolicy.MusicFolder, ref assets);
            problems += ReportAudio(sb, MediaImportPolicy.SfxFolder, ref assets);
            problems += ReportAudio(sb, MediaImportPolicy.VoiceFolder, ref assets);

            sb.AppendLine(k_LogTag + " ===== 汇总 =====");
            if (assets == 0)
            {
                sb.AppendLine(k_LogTag + " 资源 0 个：这些目录还是空的，或素材没拷进来、编辑器没导入。");
            }
            sb.AppendLine(string.Format("{0} 资源 {1} 个，异常 {2} 项{3}",
                k_LogTag, assets, problems, problems == 0 ? " ✓" : " ⚠ 见上方 ⚠ 行"));

            Debug.Log(sb.ToString());
        }

        /// <summary>体检每个文件是否存在同名 .meta —— 缺 .meta 就是「图白块 / 音哑的」的直接原因。</summary>
        static int ReportMissingMeta(StringBuilder sb, string folder, ref int assets)
        {
            if (!Directory.Exists(folder)) return 0;

            int bad = 0;
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                // .meta 自身、以及 Unity/系统会忽略的隐藏文件不算资源
                if (name.EndsWith(".meta", System.StringComparison.Ordinal) || name.StartsWith(".", System.StringComparison.Ordinal))
                    continue;

                assets++;
                if (!File.Exists(file + ".meta"))
                {
                    sb.AppendLine(string.Format("{0} ⚠ 缺 .meta（未导入：图会显示白块、音会哑）: {1}",
                        k_LogTag, file.Replace('\\', '/')));
                    bad++;
                }
            }
            return bad;
        }

        static int ReportTextures(StringBuilder sb, string folder, ref int assets)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;

            int bad = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var kind = MediaImportPolicy.ClassifyTexture(path);
                if (kind == MediaImportPolicy.TextureKind.Ignore) continue;

                assets++;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var s = new TextureImporterSettings();
                importer.ReadTextureSettings(s);

                bool wantMips = kind == MediaImportPolicy.TextureKind.WorldTexture;
                bool wantPoint = kind == MediaImportPolicy.TextureKind.PixelSprite;
                bool expectSprite = !wantMips;

                var issues = new List<string>();
                if (expectSprite && s.textureType != TextureImporterType.Sprite) issues.Add("应为 Sprite");
                if (wantMips && s.textureType != TextureImporterType.Default) issues.Add("应为 Default");
                if (expectSprite && !s.alphaIsTransparency) issues.Add("alphaIsTransparency 关");
                if (s.mipmapEnabled != wantMips) issues.Add(wantMips ? "缺 mip" : "多余 mip");
                if (wantPoint && s.filterMode != FilterMode.Point) issues.Add("应为 Point");
                if (!wantPoint && s.filterMode != FilterMode.Bilinear) issues.Add("应为 Bilinear");

                string size = tex != null ? tex.width + "x" + tex.height : "?";
                string line = string.Format(
                    "{0} {1} | {2} | type={3} filter={4} mips={5} alpha={6} max={7}",
                    k_LogTag, path, size, s.textureType, s.filterMode,
                    s.mipmapEnabled ? "On" : "Off", s.alphaIsTransparency ? "T" : "F", importer.maxTextureSize);

                if (issues.Count > 0)
                {
                    sb.AppendLine(line + " ⚠ " + string.Join("；", issues));
                    bad++;
                }
                else
                {
                    sb.AppendLine(line + " OK");
                }
            }
            return bad;
        }

        static int ReportAudio(StringBuilder sb, string folder, ref int assets)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;

            int bad = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var kind = MediaImportPolicy.ClassifyAudio(path);
                if (kind == MediaImportPolicy.AudioKind.Ignore) continue;

                assets++;
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var s = importer.defaultSampleSettings;

                AudioClipLoadType wantLoad;
                AudioCompressionFormat wantFormat;
                if (kind == MediaImportPolicy.AudioKind.Music)
                {
                    wantLoad = AudioClipLoadType.Streaming;
                    wantFormat = AudioCompressionFormat.Vorbis;
                }
                else if (kind == MediaImportPolicy.AudioKind.Sfx)
                {
                    wantLoad = AudioClipLoadType.DecompressOnLoad;
                    wantFormat = AudioCompressionFormat.PCM;
                }
                else if (kind == MediaImportPolicy.AudioKind.LoopSfx)
                {
                    // 循环音效刻意用 CompressedInMemory + PCM：Vorbis 的首尾填充会破坏无缝循环。
                    wantLoad = AudioClipLoadType.CompressedInMemory;
                    wantFormat = AudioCompressionFormat.PCM;
                }
                else
                {
                    wantLoad = AudioClipLoadType.CompressedInMemory;
                    wantFormat = AudioCompressionFormat.Vorbis;
                }

                var issues = new List<string>();
                if (s.loadType != wantLoad) issues.Add("应为 " + wantLoad);
                if (s.compressionFormat != wantFormat) issues.Add("应为 " + wantFormat);

                // Streaming 的 clip 在播放前可能报不出时长/声道，这是正常的，不算异常
                // 注意枚举名：Unity 6 是 AudioDataLoadState（旧版的 AudioClipLoadState 已移除，实测编译报 CS0103）
                string info;
                if (clip == null)
                {
                    info = "clip 加载失败";
                }
                else if (s.loadType == AudioClipLoadType.Streaming && clip.loadState != AudioDataLoadState.Loaded)
                {
                    info = string.Format("{0}ch {1}Hz loadState={2}（Streaming 播前不报时长属正常）",
                        clip.channels, clip.frequency, clip.loadState);
                }
                else
                {
                    info = string.Format("{0:F3}s {1}ch {2}Hz", clip.length, clip.channels, clip.frequency);
                }

                string line = string.Format(
                    "{0} {1} | {2} | load={3} fmt={4} q={5:F2} mono={6} preload={7} | {8}",
                    k_LogTag, path, info, s.loadType, s.compressionFormat, s.quality,
                    importer.forceToMono ? "T" : "F", s.preloadAudioData ? "T" : "F",
                    clip != null ? clip.name : "?");

                if (issues.Count > 0)
                {
                    sb.AppendLine(line + " ⚠ " + string.Join("；", issues));
                    bad++;
                }
                else
                {
                    sb.AppendLine(line + " OK");
                }
            }
            return bad;
        }

        /// <summary>试听入口：音色好不好听只能靠人耳，agent 听不见。</summary>
        [MenuItem("P1/素材/3. 在 Finder 中打开 MediaRaw（试听素材）", false, 3)]
        static void RevealMediaRaw()
        {
            var raw = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MediaRaw");
            if (!Directory.Exists(raw))
            {
                Debug.LogWarning(k_LogTag + " 找不到 MediaRaw 目录: " + raw);
                return;
            }
            EditorUtility.RevealInFinder(raw);
        }
    }
}