using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 素材导入策略：把「哪个目录 = 哪种导入设置」这件事写成可 code review 的代码，
    /// 而不是手工点 Inspector 或手改 .meta（AGENTS.md 规则 1 与 optimize-audio 技能都禁止手改 .meta YAML）。
    /// </summary>
    public static class MediaImportPolicy
    {
        /// <summary>本策略只管自己项目下的素材，不碰 URP 模板或第三方内容。</summary>
        public const string Root = "Assets/_Project/";

        public const string SpritesFolder = Root + "Art/Sprites/";
        public const string TexturesFolder = Root + "Art/Textures/";
        public const string MusicFolder = Root + "Audio/Music/";
        public const string SfxFolder = Root + "Audio/SFX/";
        public const string VoiceFolder = Root + "Audio/Voice/";

        public enum TextureKind
        {
            Ignore,
            Sprite,
            PixelSprite,   // 将来若定像素风，把图放 Art/Sprites/Pixel/ 即自动最近邻
            WorldTexture,
        }

        public enum AudioKind
        {
            Ignore,
            Music,
            Sfx,
            LoopSfx,
            Voice,
        }

        public static bool IsLoopAudioFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (fileName.Contains("_loop")) return true;
            if (fileName.StartsWith("storm_rain", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("splatling_spin", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("climb_", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("enemy_ink_sizzle", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("roll_", System.StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("roller_flick", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith("swim_", System.StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("swim_splash", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static TextureKind ClassifyTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(Root, System.StringComparison.Ordinal))
                return TextureKind.Ignore;

            if (assetPath.StartsWith(TexturesFolder, System.StringComparison.Ordinal))
                return TextureKind.WorldTexture;

            if (assetPath.StartsWith(SpritesFolder, System.StringComparison.Ordinal))
            {
                // 像素风豁免：Art/Sprites/Pixel/... 用最近邻，避免像素被平滑糊掉
                if (assetPath.IndexOf("/Pixel/", System.StringComparison.Ordinal) >= 0)
                    return TextureKind.PixelSprite;
                return TextureKind.Sprite;
            }

            return TextureKind.Ignore;
        }

        public static AudioKind ClassifyAudio(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(Root, System.StringComparison.Ordinal))
                return AudioKind.Ignore;

            if (assetPath.StartsWith(MusicFolder, System.StringComparison.Ordinal)) return AudioKind.Music;
            if (assetPath.StartsWith(SfxFolder, System.StringComparison.Ordinal))
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                if (IsLoopAudioFileName(fileName))
                    return AudioKind.LoopSfx;
                return AudioKind.Sfx;
            }
            if (assetPath.StartsWith(VoiceFolder, System.StringComparison.Ordinal)) return AudioKind.Voice;
            return AudioKind.Ignore;
        }

        /// <summary>
        /// 套用纹理设置。可在 AssetPostprocessor.OnPreprocessTexture 里安全调用
        /// —— 那里只需改 importer，导入过程会带着这些设置完成，不要自己再 SaveAndReimport（会递归）。
        /// </summary>
        public static void Apply(TextureImporter importer, TextureKind kind)
        {
            if (kind == TextureKind.Ignore) return;

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);

            // 读取设置：UI/世界贴图都不需要 CPU 侧像素访问
            s.readable = false;

            if (kind == TextureKind.WorldTexture)
            {
                s.textureType = TextureImporterType.Default;
                s.mipmapEnabled = true;          // 世界贴图要 mip，避免远处闪烁
                s.wrapMode = TextureWrapMode.Repeat;
                s.filterMode = FilterMode.Bilinear;
                s.npotScale = TextureImporterNPOTScale.ToNearest;
                s.sRGBTexture = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else
            {
                s.textureType = TextureImporterType.Sprite;
                s.spriteMode = (int)SpriteImportMode.Single;
                importer.spriteImportMode = SpriteImportMode.Single;
                s.alphaIsTransparency = true;    // keyout 出的透明图标必须开，否则边缘发黑
                s.mipmapEnabled = false;         // UI 不吃 mip
                s.wrapMode = TextureWrapMode.Clamp;
                s.filterMode = kind == TextureKind.PixelSprite ? FilterMode.Point : FilterMode.Bilinear;
                s.npotScale = TextureImporterNPOTScale.None;
                s.sRGBTexture = true;
                s.spritePixelsPerUnit = 100f;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
            }

            importer.SetTextureSettings(s);
            if (kind != TextureKind.WorldTexture)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
        }

        /// <summary>
        /// 套用音频设置。同样可在 OnPreprocessAudio 里直接调用，不要自己 SaveAndReimport。
        /// </summary>
        public static void Apply(AudioImporter importer, AudioKind kind)
        {
            if (kind == AudioKind.Ignore) return;

            var s = importer.defaultSampleSettings;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

            switch (kind)
            {
                case AudioKind.Music:
                    // BGM 约 2 分钟，常驻内存不合算 → 流式读盘
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.7f;
                    s.preloadAudioData = false;
                    importer.forceToMono = false;        // 音乐要立体声
                    importer.loadInBackground = true;
                    break;

                case AudioKind.Sfx:
                    // 短音效：解码零延迟优先。Vorbis 对短音效有解码延迟，别用
                    s.loadType = AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = AudioCompressionFormat.PCM;
                    s.quality = 1f;
                    s.preloadAudioData = true;
                    importer.forceToMono = true;         // 音效多是单声道源，省一半内存
                    importer.loadInBackground = false;
                    break;

                case AudioKind.LoopSfx:
                    // 循环音效：采用 CompressedInMemory 且保证 PCM 格式，确保首尾无缝循环（避免 Vorbis 填充空隙）
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    s.compressionFormat = AudioCompressionFormat.PCM;
                    s.quality = 1f;
                    s.preloadAudioData = true;
                    importer.forceToMono = true;
                    importer.loadInBackground = false;
                    break;

                case AudioKind.Voice:
                    // 台词：压缩省内存，允许轻微延迟
                    s.loadType = AudioClipLoadType.CompressedInMemory;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.8f;
                    s.preloadAudioData = false;
                    importer.forceToMono = true;
                    importer.loadInBackground = true;
                    break;
            }

            importer.defaultSampleSettings = s;
        }
    }

    /// <summary>
    /// 每次导入按路径自动套用设置 —— 这是本项目落地导入设置的**唯一合规路径**。
    ///
    /// 为什么不用 Inspector 手点或手改 .meta：AGENTS.md 规则 1 与 optimize-audio 技能都禁止手改
    /// .meta YAML（易出坏引用、无法 code review）；而 Inspector 手改没有留痕，
    /// 下次重导入也无人保证一致。写成代码则可见、可提交、可 review。
    ///
    /// 副作用是显式的：**Art/ 与 Audio/ 下的导入设置以本文件为准**，
    /// 在 Inspector 里手改会被下次重导入覆盖。要改行为就改这里。
    /// </summary>
    public sealed class MediaImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var kind = MediaImportPolicy.ClassifyTexture(assetPath);
            if (kind == MediaImportPolicy.TextureKind.Ignore) return;
            MediaImportPolicy.Apply((TextureImporter)assetImporter, kind);
        }

        void OnPreprocessAudio()
        {
            var kind = MediaImportPolicy.ClassifyAudio(assetPath);
            if (kind == MediaImportPolicy.AudioKind.Ignore) return;
            MediaImportPolicy.Apply((AudioImporter)assetImporter, kind);
        }
    }
}