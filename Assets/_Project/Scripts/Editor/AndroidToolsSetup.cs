using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// Android 侧的一次性工程配置：
    /// 1. 把外部工具路径写入 EditorPrefs（Preferences → External Tools）；
    /// 2. 打开「Custom Main Manifest」，让 Assets/Plugins/Android/AndroidManifest.xml 生效
    ///    —— 应用内「下载完安装 APK」依赖它声明 FileProvider 与 REQUEST_INSTALL_PACKAGES；
    /// 3. 提供免出包的自检菜单，出包前先跑一次就知道配置齐不齐。
    /// </summary>
    [InitializeOnLoad]
    public static class AndroidToolsSetup
    {
        const string SdkRoot = "/Users/a1/Library/Android/sdk";
        const string NdkRoot = "/Applications/Unity/Hub/Editor/6000.6.7f1/PlaybackEngines/AndroidPlayer/NDK";
        const string JdkRoot = "/Library/Java/JavaVirtualMachines/temurin-17.jdk/Contents/Home";

        public const string UpdateManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        public const string UpdateFilePathsPath =
            "Assets/Plugins/Android/P1UpdateLib.androidlib/src/main/res/xml/p1_update_paths.xml";
        /// <summary>androidx.core 依赖由该库的 build.gradle 携带；缺失 = 启动即崩，所以自检必须查。</summary>
        public const string UpdateLibGradlePath =
            "Assets/Plugins/Android/P1UpdateLib.androidlib/build.gradle";
        /// <summary>根级 gradle 模板：对齐 kotlin-stdlib 家族，消除与注入组件的重复类。</summary>
        public const string BaseGradleTemplatePath = "Assets/Plugins/Android/baseProjectTemplate.gradle";

        const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        const string CustomMainManifestProperty = "useCustomMainManifest";
        const string CustomBaseGradleTemplateProperty = "useCustomBaseGradleTemplate";
        const string ApplicationEntryProperty = "androidApplicationEntry";

        /// <summary>与 UpdateManager.FileProviderAuthoritySuffix 对应的清单写法。</summary>
        const string FileProviderAuthorityToken = "${applicationId}.fileprovider";
        const string FileProviderClassName = "androidx.core.content.FileProvider";
        const string UnknownSourcesPermission = "android.permission.REQUEST_INSTALL_PACKAGES";
        const string UpdatePathsResource = "@xml/p1_update_paths";

        /// <summary>androidApplicationEntry 的取值：1 = Activity（本项目当前配置）。</summary>
        const int ApplicationEntryActivity = 1;

        static AndroidToolsSetup()
        {
            EditorPrefs.SetString("AndroidSdkRoot", SdkRoot);
            EditorPrefs.SetString("AndroidNdkRoot", NdkRoot);
            EditorPrefs.SetString("JdkPath", JdkRoot);

            // 开关只在编辑器完全就绪后再动：实测在 InitializeOnLoad 阶段同步写会和域重载的序列化打架，
            // 会留下「磁盘 = 1 但内存 = False」的不一致状态，随后编辑器任意一次保存就会把它打回 0。
            // （delayCall 是 void 委托，带返回值的方法要包一层 lambda）
            EditorApplication.delayCall += () =>
            {
                EnsureCustomMainManifest();
                EnsureCustomBaseGradleTemplate();
            };
        }

        /// <summary>
        /// 打开 Player Settings 的「Custom Main Manifest」开关。
        /// 只改这一个布尔字段，走 SerializedObject，不手写 ProjectSettings.asset 的 YAML。
        /// <c>PlayerSettings.Android</c> 没有对应公开 API（实测无任何 manifest 相关属性），所以只能这么写。
        /// 写完立即回读确认 —— 不假设写成功。
        /// </summary>
        public static bool EnsureCustomMainManifest()
        {
            if (IsCustomMainManifestEnabled()) return true;

            var so = LoadProjectSettings();
            if (so == null)
            {
                Debug.LogWarning("[AndroidToolsSetup] 读不到 ProjectSettings，无法确认 Custom Main Manifest 开关");
                return false;
            }

            var prop = so.FindProperty(CustomMainManifestProperty);
            if (prop == null)
            {
                Debug.LogWarning($"[AndroidToolsSetup] 找不到 ProjectSettings.{CustomMainManifestProperty}，"
                                 + "请在 Player Settings → Publishing Settings 手动勾选「Custom Main Manifest」");
                return false;
            }

            prop.boolValue = true;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            bool applied = IsCustomMainManifestEnabled();
            if (applied)
            {
                Debug.Log("[AndroidToolsSetup] 已打开 Custom Main Manifest，生效文件: " + UpdateManifestPath);
            }
            else
            {
                Debug.LogWarning("[AndroidToolsSetup] Custom Main Manifest 写入未生效，"
                                 + "请在 Player Settings → Publishing Settings 手动勾选「Custom Main Manifest」");
            }
            return applied;
        }

        public static bool IsCustomMainManifestEnabled()
        {
            var so = LoadProjectSettings();
            if (so == null) return false;
            var prop = so.FindProperty(CustomMainManifestProperty);
            return prop != null && prop.boolValue;
        }

        /// <summary>
        /// 打开「Custom Base Gradle Template」，让 Assets/Plugins/Android/baseProjectTemplate.gradle 生效
        /// —— kotlin-stdlib 家族版本对齐依赖它，不开则注入组件的分体库与 androidx.core 的主库重复，构建必失败。
        /// 写法与 EnsureCustomMainManifest 相同：SerializedObject 单字段，不手写 YAML。
        /// </summary>
        public static bool EnsureCustomBaseGradleTemplate()
        {
            if (IsCustomBaseGradleTemplateEnabled()) return true;

            var so = LoadProjectSettings();
            if (so == null)
            {
                Debug.LogWarning("[AndroidToolsSetup] 读不到 ProjectSettings，无法确认 Custom Base Gradle Template 开关");
                return false;
            }

            var prop = so.FindProperty(CustomBaseGradleTemplateProperty);
            if (prop == null)
            {
                Debug.LogWarning($"[AndroidToolsSetup] 找不到 ProjectSettings.{CustomBaseGradleTemplateProperty}，"
                                 + "请在 Player Settings → Publishing Settings 手动勾选「Custom Base Gradle Template」");
                return false;
            }

            prop.boolValue = true;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            bool applied = IsCustomBaseGradleTemplateEnabled();
            if (applied)
            {
                Debug.Log("[AndroidToolsSetup] 已打开 Custom Base Gradle Template，生效文件: " + BaseGradleTemplatePath);
            }
            else
            {
                Debug.LogWarning("[AndroidToolsSetup] Custom Base Gradle Template 写入未生效，"
                                 + "请在 Player Settings → Publishing Settings 手动勾选");
            }
            return applied;
        }

        public static bool IsCustomBaseGradleTemplateEnabled()
        {
            var so = LoadProjectSettings();
            if (so == null) return false;
            var prop = so.FindProperty(CustomBaseGradleTemplateProperty);
            return prop != null && prop.boolValue;
        }

        private static SerializedObject LoadProjectSettings()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath);
            if (assets == null || assets.Length == 0) return null;
            return new SerializedObject(assets[0]);
        }

        /// <summary>
        /// 免出包自检：确认应用内安装 APK 需要的清单声明都就位了。
        /// 出包前先跑一次，比打完整包再发现「点了安装没反应」便宜得多。
        /// </summary>
        [MenuItem("P1/构建/12. 校验 Android 更新安装清单配置", false, 84)]
        public static void ValidateUpdateManifest()
        {
            // 菜单是手动触发的，不在域重载期间，写入可靠：缺开关就顺手补上再做校验
            EnsureCustomMainManifest();
            EnsureCustomBaseGradleTemplate();

            var problems = new List<string>();

            if (!IsCustomMainManifestEnabled())
            {
                problems.Add($"ProjectSettings 的 {CustomMainManifestProperty} 仍为 0 —— 自定义主清单不会被采用");
            }

            if (!IsCustomBaseGradleTemplateEnabled())
            {
                problems.Add($"ProjectSettings 的 {CustomBaseGradleTemplateProperty} 仍为 0 —— kotlin-stdlib 对齐不生效，构建会因重复类失败");
            }

            string baseGradle = ReadTextOrNull(BaseGradleTemplatePath);
            if (baseGradle == null)
            {
                problems.Add($"缺少根级 gradle 模板: {BaseGradleTemplatePath}");
            }
            else
            {
                RequireContains(baseGradle, "kotlin-stdlib",
                    "根级 gradle 模板缺少 kotlin-stdlib 版本对齐（会被模板覆盖回默认丢失）", problems);
            }

            string manifest = ReadTextOrNull(UpdateManifestPath);
            if (manifest == null)
            {
                problems.Add($"缺少自定义主清单: {UpdateManifestPath}");
            }
            else
            {
                RequireContains(manifest, UnknownSourcesPermission, "主清单缺少 REQUEST_INSTALL_PACKAGES 权限声明", problems);
                RequireContains(manifest, FileProviderClassName, "主清单缺少 FileProvider 声明", problems);
                RequireContains(manifest, FileProviderAuthorityToken,
                    "主清单的 FileProvider authority 不是 " + FileProviderAuthorityToken, problems);
                RequireContains(manifest, UpdatePathsResource,
                    "主清单的 FileProvider 未指向 " + UpdatePathsResource, problems);

                int entry = ReadApplicationEntry();
                if (entry == ApplicationEntryActivity)
                {
                    RequireContains(manifest, "com.unity3d.player.UnityPlayerActivity",
                        "Application Entry 是 Activity，但主清单里没有 UnityPlayerActivity", problems);
                }
                else if (entry >= 0)
                {
                    problems.Add($"Application Entry 不是 Activity（androidApplicationEntry = {entry}），"
                                 + "请确认主清单里的启动 Activity 与之匹配");
                }

                RequireContains(manifest, "android.permission.INTERNET", "主清单缺少 INTERNET 权限声明", problems);
            }

            string filePaths = ReadTextOrNull(UpdateFilePathsPath);
            if (filePaths == null)
            {
                problems.Add($"缺少 FileProvider 路径表: {UpdateFilePathsPath}");
            }
            else
            {
                RequireContains(filePaths, "external-files-path",
                    "路径表缺少 external-files-path（persistentDataPath 在 Android 上属于 external files 目录）", problems);
            }

            string libGradle = ReadTextOrNull(UpdateLibGradlePath);
            if (libGradle == null)
            {
                problems.Add($"缺少更新库构建脚本: {UpdateLibGradlePath}");
            }
            else
            {
                RequireContains(libGradle, "androidx.core:core",
                    "更新库未声明 androidx.core:core 依赖（FileProvider 类缺失会导致应用启动即崩）", problems);
                RequireContains(libGradle, "namespace ", "更新库 build.gradle 缺少 namespace 声明", problems);
            }

            if (problems.Count == 0)
            {
                Debug.Log("[AndroidToolsSetup] ✅ 应用内更新安装清单配置自检通过：\n"
                          + $"  · {UpdateManifestPath}（FileProvider + REQUEST_INSTALL_PACKAGES + INTERNET）\n"
                          + $"  · {UpdateFilePathsPath}（external-files-path）\n"
                          + $"  · {UpdateLibGradlePath}（androidx.core:core 依赖）\n"
                          + $"  · {BaseGradleTemplatePath}（kotlin-stdlib 对齐）\n"
                          + "  · ProjectSettings: useCustomMainManifest = 1, useCustomBaseGradleTemplate = 1\n"
                          + "  出包后可用 aapt2 dump xmltree --file AndroidManifest.xml <apk> 复核合并结果。");
                return;
            }

            Debug.LogError("[AndroidToolsSetup] ❌ 应用内更新安装清单配置自检未通过，共 " + problems.Count + " 项：\n  · "
                           + string.Join("\n  · ", problems));
        }

        private static void RequireContains(string text, string needle, string problem, List<string> problems)
        {
            if (text.IndexOf(needle, System.StringComparison.Ordinal) < 0) problems.Add(problem);
        }

        private static string ReadTextOrNull(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AndroidToolsSetup] 读取 {path} 失败: {ex.Message}");
                return null;
            }
        }

        private static int ReadApplicationEntry()
        {
            var so = LoadProjectSettings();
            if (so == null) return -1;
            var prop = so.FindProperty(ApplicationEntryProperty);
            return prop != null ? prop.intValue : -1;
        }
    }
}
