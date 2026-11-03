using System.IO;
using P1.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// TapTap 上架专用出包（无联网变体）。
    ///
    /// 为什么单独一个入口：TapTap 审核在「安装包检测到联网功能」时会要求补 ICP 核准信息。
    /// 上架包关掉一切联网能力（无 INTERNET 权限、无检查更新入口）即可绕开该前置资质；
    /// 而仓库自托管渠道仍需要完整更新链路，所以用脚本宏 <see cref="BuildVariants.OfflineDefine"/>
    /// 在构建期间临时切变体，构建结束（无论成败）立即还原，避免宏残留影响后续其它渠道出包。
    ///
    /// 发版清单见 Docs/TapTapRelease.md。
    /// </summary>
    public static class TapTapBuildSuite
    {
        public const string TapTapOutputPath = "Builds/Android/TapTap/InkTurf-TapTap.apk";

        const string LogTag = "[TapTap 出包]";

        [MenuItem("P1/构建/13. 构建 TapTap 上架包（无联网变体）", false, 85)]
        public static void BuildTapTapOffline()
        {
            bool ok = BuildTapTapApk(out string outputPath);
            if (!ok)
            {
                Debug.LogError($"{LogTag} 构建失败，见上方日志。");
                ExitBatchOnFailure();
                return;
            }

            Debug.Log($"{LogTag} 构建完成：{outputPath}\n"
                      + "  · 已移除 INTERNET 权限、隐藏检查更新入口\n"
                      + "  · 上传前用 aapt2 dump permissions 复核应为空（仅剩 DYNAMIC_RECEIVER 自用权限）");
        }

        /// <summary>
        /// 批处理入口（<c>-executeMethod</c> 只接受无参方法）。
        /// 用法：<c>Unity -batchmode -quit -projectPath ... -executeMethod
        /// P1.Editor.TapTapBuildSuite.BuildTapTapOfflineBatch -buildOutput &lt;path&gt;</c>
        /// </summary>
        public static void BuildTapTapOfflineBatch()
        {
            bool ok = BuildTapTapApk(out string outputPath);
            if (!ok)
            {
                Debug.LogError($"{LogTag} 批处理构建失败，见上方日志。");
                ExitBatchOnFailure();
                return;
            }

            Debug.Log($"{LogTag} 批处理构建完成：{outputPath}");
        }

        /// <summary>供 release.py / 批处理调用的无 UI 版本。</summary>
        public static bool BuildTapTapApk(out string outputPath)
        {
            outputPath = ResolveOutputPath();

            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // 先把宏写进 PlayerSettings，再让 Unity 重新编译一次，确保打包用的是离线变体的程序集。
            bool previousOffline = HasOfflineDefine();
            if (!previousOffline)
            {
                SetOfflineDefine(true);
                AssetDatabase.Refresh();
            }

            try
            {
                BuildAndProfilerSuite.EnsureSceneInBuildSettings();
                BuildAndProfilerSuite.ApplyAndroidPlayerSettings();

                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { BuildAndProfilerSuite.MainScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"{LogTag} Android 构建失败: {summary.result}, Errors: {summary.totalErrors}");
                    return false;
                }

                Debug.Log($"{LogTag} Android APK 构建成功：{outputPath}"
                          + $"（{summary.totalSize / (1024f * 1024f):F2} MB）");
                return true;
            }
            finally
            {
                // 无论成败都还原宏，否则下一次「P1/构建/9」会误打成无联网包。
                if (!previousOffline) SetOfflineDefine(false);
                AssetDatabase.Refresh();
            }
        }

        static string ResolveOutputPath()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildOutput" || args[i] == "--output-path")
                    return args[i + 1];
            }
            return TapTapOutputPath;
        }

        static bool HasOfflineDefine()
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            return defines != null && defines.Contains(BuildVariants.OfflineDefine);
        }

        static void SetOfflineDefine(bool enabled)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            var list = new System.Collections.Generic.List<string>(
                string.IsNullOrEmpty(defines) ? new string[0] : defines.Split(';'));

            bool removed = list.RemoveAll(d => d == BuildVariants.OfflineDefine) > 0;
            if (enabled) list.Add(BuildVariants.OfflineDefine);

            if (!removed && !enabled) return; // 本来就没有，不用写回

            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android,
                string.Join(";", list));
            Debug.Log($"{LogTag} 脚本定义符号 {BuildVariants.OfflineDefine} → {(enabled ? "开" : "关")}");
        }

        static void ExitBatchOnFailure()
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
