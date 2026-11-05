using System;
using System.Collections.Generic;
using System.IO;
using P1.Core;
using P1.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Editor
{
    /// <summary>
    /// [P6-C] 双端构建套件：
    /// 1. 桌面端（macOS Standalone）免安装包自动化构建（Builds/StandaloneOSX/）；
    /// 2. Android 移动端 APK（ARM64 + Vulkan/GLES3）自动化构建（Builds/Android/）。
    ///
    /// [P6-C-R] 原「120 秒战斗 Profiler 性能仿真采样」已删除：其 FPS 来自 Random(42) 且平均值硬编码 60.1，
    /// GC/DrawCalls 亦非真实测量，历史报告数据全部作废。
    /// 真实采样入口迁移至 <see cref="PlayModeProfilerSession"/>（菜单 `P1/性能/1`），
    /// Player/真机自动采样见 <see cref="P1.Core.AutoProfileRuntimeService"/>。
    /// </summary>
    public static class BuildAndProfilerSuite
    {
        public const string MainScenePath = "Assets/_Project/Scenes/PhaseA_Playbox.unity";
        public const string StandaloneOutputPath = "Builds/StandaloneOSX/InkTurf.app";
        public const string AndroidOutputPath = "Builds/Android/InkTurf.apk";
        public const string WebGLOutputPath = "Builds/WebGL";

        /// <summary>
        /// 出包场景列表：Build Index 0 固定主场景，其余全部地图场景按 <see cref="MapCatalogService"/> 顺序补齐。
        /// 只传 MainScenePath 会让 APK 里只有 level0，运行期切第二张图必然找不到 Build Index
        /// （v0.1.25 手机端「切图不生效、开始游戏无反应」的根因）。新增地图后不要再逐处手写场景数组。
        /// </summary>
        public static string[] MapScenePaths()
        {
            string[] mapPaths = MapCatalogService.AllScenePaths();
            var scenes = new List<string>(mapPaths.Length + 1) { MainScenePath };
            for (int i = 0; i < mapPaths.Length; i++)
            {
                if (string.IsNullOrEmpty(mapPaths[i]) || scenes.Contains(mapPaths[i])) continue;
                scenes.Add(mapPaths[i]);
            }
            return scenes.ToArray();
        }

        /// <summary>真实采样报告的输出路径（写入方为 PlayModeProfilerSession）。</summary>
        public const string ProfilerReportPath = "Docs/ProfilerReport_P6C.md";

        [MenuItem("P1/构建/8. 构建 macOS Standalone 独立免安装包 (P6-C)", false, 80)]
        public static bool BuildStandaloneOSX()
        {
            Debug.Log("[P6-C][Build] 开始执行 macOS Standalone 桌面独立端构建...");

            EnsureOutputDirectory("Builds/StandaloneOSX");
            EnsureSceneInBuildSettings();
            ApplyDesktopPlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = MapScenePaths(),
                locationPathName = StandaloneOutputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[P6-C][Build] macOS Standalone 构建成功！\n" +
                          $"- 产物路径: {StandaloneOutputPath}\n" +
                          $"- 包体总大小: {summary.totalSize / (1024f * 1024f):F2} MB\n" +
                          $"- 耗时: {summary.totalTime.TotalSeconds:F2} 秒\n" +
                          $"- Errors: {summary.totalErrors}, Warnings: {summary.totalWarnings}");
                return true;
            }
            else
            {
                Debug.LogError($"[P6-C][Build] macOS Standalone 构建失败: {summary.result}, Errors: {summary.totalErrors}");
                return false;
            }
        }

        /// <summary>
        /// [P6-C-R] 构建 macOS Development Player（供真实帧率/GC 采样：
        /// 向 persistentDataPath 放置 P1_AutoProfile.request 后启动即自动实测并输出报告）。
        /// </summary>
        [MenuItem("P1/性能/5. 构建桌面 Development Player（真机采样用） (P6-C-R)", false, 94)]
        public static bool BuildDevelopmentStandaloneOSX()
        {
            Debug.Log("[P6-C-R][Build] 开始构建 macOS Development Player（性能采样用）...");

            EnsureOutputDirectory("Builds/StandaloneOSX");
            EnsureSceneInBuildSettings();
            ApplyDesktopPlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = MapScenePaths(),
                locationPathName = "Builds/StandaloneOSX/InkTurf_Dev.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[P6-C-R][Build] Development Player 构建成功: {options.locationPathName} ({summary.totalSize / (1024f * 1024f):F2} MB)\n" +
                          $"采样用法: 向 {Application.persistentDataPath}/ " +
                          $"写入 P1_AutoProfile.request（内容=秒数），启动游戏，结束后取 P1_AutoProfiles/ 下报告");
                return true;
            }

            Debug.LogError($"[P6-C-R][Build] Development Player 构建失败: {summary.result}, Errors: {summary.totalErrors}");
            return false;
        }

        public static string ResolveAndroidOutputPath()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildOutput" || args[i] == "-customBuildOutput" || args[i] == "--output-path")
                {
                    return args[i + 1];
                }
            }
            return AndroidOutputPath;
        }

        public static string ResolveWebGLOutputPath()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildOutput" || args[i] == "-customBuildOutput" || args[i] == "--output-path")
                {
                    return args[i + 1];
                }
            }
            return WebGLOutputPath;
        }

        /// <summary>
        /// 构建 GitHub Pages 用的 WebGL 网页试玩包（产物目录 Builds/WebGL，部署时拷进 site/dist/play）。
        /// 关键点：Brotli + 解压兜底（Pages 不能设 Content-Encoding）；哈希文件名避免 CDN 缓存错配；
        /// 不建 Development 包，避免把 com.unity.pipeline 的线程/网络代码带进浏览器。
        /// </summary>
        [MenuItem("P1/构建/14. 构建 WebGL 网页试玩包", false, 83)]
        public static bool BuildWebGL()
        {
            string outputDir = ResolveWebGLOutputPath();
            Debug.Log($"[WebGL][Build] 开始构建 WebGL 网页试玩包 (产物目录: {outputDir})...");

            #pragma warning disable 0618
            bool prevStaticBatching = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.WebGL);
#pragma warning restore 0618
            bool prevStripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;
            try
            {
                EnsureSceneInBuildSettings();
                ApplyWebGLPlayerSettings();

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[WebGL][Build] 切换 WebGL 构建目标失败（WebGL Build Support 模块是否已安装？）。");
                    ExitBatchOnFailure();
                    return false;
                }

                // 哈希文件名会随每次构建变化，旧产物必须清掉，否则会一起进 gh-pages。
                if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
                EnsureOutputDirectory(outputDir);

                var options = new BuildPlayerOptions
                {
                    scenes = MapScenePaths(),
                    locationPathName = outputDir,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[WebGL][Build] WebGL 构建成功！\n" +
                              $"- 产物目录: {outputDir}\n" +
                              $"- 包体总大小: {summary.totalSize / (1024f * 1024f):F2} MB\n" +
                              $"- 耗时: {summary.totalTime.TotalSeconds:F2} 秒\n" +
                              $"- Errors: {summary.totalErrors}, Warnings: {summary.totalWarnings}");
                    return true;
                }

                Debug.LogError($"[WebGL][Build] WebGL 构建失败: {summary.result}, Errors: {summary.totalErrors}");
                ExitBatchOnFailure();
                return false;
            }
            finally
            {
                // 静态合批是全局 PlayerSetting，构建完恢复，避免影响 Android/桌面构建。
#pragma warning disable 0618
                PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.WebGL, prevStaticBatching);
#pragma warning restore 0618
                PlayerSettings.stripUnusedMeshComponents = prevStripUnusedMeshComponents;
            }
        }

        /// <summary>批处理模式下构建失败必须以非零码退出，否则 unity build 会把失败当成功。</summary>
        private static void ExitBatchOnFailure()
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        public static void ApplyWebGLPlayerSettings()
        {
            PlayerSettings.companyName = "P1Studio";
            PlayerSettings.productName = GameVersion.APP_NAME;
            PlayerSettings.bundleVersion = GameVersion.APP_VERSION;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            var target = NamedBuildTarget.WebGL;
            PlayerSettings.SetApplicationIdentifier(target, "com.p1studio.inkturfdemo");
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);

            PlayerSettings.WebGL.template = "PROJECT:InkTurf";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.initialMemorySize = 64;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;

            // WebGL：静态合批在部分浏览器/驱动上会让整块静态地面不绘制（只看到天空/远景）。
            // 场地 draw 数量很小，关闭静态合批换取稳定渲染；构建结束由 BuildWebGL 恢复。
#pragma warning disable 0618
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.WebGL, false);
#pragma warning restore 0618

            // WebGL：关闭 "Optimize Mesh Data"。它会在打包期剔除"没有任何 shader 使用"的
            // 顶点通道；自定义 shader 一旦被平台回退，顶点色会被静默删除，容器/远景整片变黑。
            // 关闭后顶点色/UV 原样进包；构建结束由 BuildWebGL 恢复。
            PlayerSettings.stripUnusedMeshComponents = false;

            AssetDatabase.SaveAssets();
        }

        [MenuItem("P1/构建/9. 构建 Android APK (ARM64 + Vulkan/GLES3) (P6-C)", false, 81)]
        public static bool BuildAndroid()
        {
            string outputPath = ResolveAndroidOutputPath();
            Debug.Log($"[P6-C][Build] 开始执行 Android 移动端 APK 构建 (产物路径: {outputPath})...");

            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir)) EnsureOutputDirectory(outputDir);

            EnsureSceneInBuildSettings();
            ApplyAndroidPlayerSettings();

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var options = new BuildPlayerOptions
            {
                scenes = MapScenePaths(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[P6-C][Build] Android APK 构建成功！\n" +
                          $"- 产物路径: {outputPath}\n" +
                          $"- APK 大小: {summary.totalSize / (1024f * 1024f):F2} MB\n" +
                          $"- 耗时: {summary.totalTime.TotalSeconds:F2} 秒\n" +
                          $"- Errors: {summary.totalErrors}, Warnings: {summary.totalWarnings}");
                return true;
            }
            else
            {
                Debug.LogError($"[P6-C][Build] Android 构建失败: {summary.result}, Errors: {summary.totalErrors}");
                return false;
            }
        }

        [MenuItem("P1/构建/10. 一键全平台自动化出包与验收 (P6-C)", false, 82)]
        public static void BuildAllPlatforms()
        {
            bool osxSuccess = BuildStandaloneOSX();
            bool androidSuccess = BuildAndroid();

            Debug.Log($"[P6-C][Build] 全平台构建完成。macOS Standalone: {(osxSuccess ? "OK" : "FAILED")}, Android APK: {(androidSuccess ? "OK" : "FAILED")}");
        }

        private static void EnsureOutputDirectory(string relativeDirPath)
        {
            if (!Directory.Exists(relativeDirPath))
            {
                Directory.CreateDirectory(relativeDirPath);
            }
        }

        public static void EnsureSceneInBuildSettings()
        {
            var current = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            string[] wanted = MapScenePaths();
            bool changed = false;

            // 主场景固定在索引 0（启动场景），其余地图场景依次补齐并启用。
            for (int i = 0; i < wanted.Length; i++)
            {
                int found = current.FindIndex(scene => scene.path == wanted[i]);
                if (found < 0)
                {
                    current.Insert(i, new EditorBuildSettingsScene(wanted[i], true));
                    changed = true;
                    continue;
                }

                if (!current[found].enabled)
                {
                    current[found] = new EditorBuildSettingsScene(wanted[i], true);
                    changed = true;
                }
                if (found != i)
                {
                    EditorBuildSettingsScene scene = current[found];
                    current.RemoveAt(found);
                    current.Insert(i, scene);
                    changed = true;
                }
            }

            if (changed) EditorBuildSettings.scenes = current.ToArray();
        }

        public static void ApplyDesktopPlayerSettings()
        {
            PlayerSettings.companyName = "P1Studio";
            PlayerSettings.productName = GameVersion.APP_NAME;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.p1studio.inkturfdemo");
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;

            if (!BrandAssetInstaller.ApplyBrandAssets(out string brandReport))
            {
                Debug.LogWarning("[P1][Brand] 桌面出包未接线图标：\n" + brandReport);
            }
        }

        public static void ApplyAndroidPlayerSettings()
        {
            PlayerSettings.companyName = "P1Studio";
            PlayerSettings.productName = GameVersion.APP_NAME;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            var target = NamedBuildTarget.Android;
            PlayerSettings.bundleVersion = GameVersion.APP_VERSION;
            int[] v = GameVersion.ParseVersion(GameVersion.APP_VERSION);
            PlayerSettings.Android.bundleVersionCode = v[0] * 10000 + v[1] * 100 + v[2];
            AssetDatabase.SaveAssets();
            PlayerSettings.SetApplicationIdentifier(target, "com.p1studio.inkturfdemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);

            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
            {
                GraphicsDeviceType.Vulkan,
                GraphicsDeviceType.OpenGLES3
            });

            if (!BrandAssetInstaller.ApplyBrandAssets(out string brandReport))
            {
                Debug.LogWarning("[P1][Brand] Android 出包未接线图标：\n" + brandReport);
            }
        }
    }
}
