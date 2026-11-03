using System.IO;
using System.Xml;
using P1.Core;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 隐私合规：Unity 的 Android 清单处理器会把 UnityPlayerActivity 的 android:exported
    /// 强制写成 true（即使 Assets/Plugins/Android/AndroidManifest.xml 写的是 false）。
    /// 它虽然没有 MAIN/LAUNCHER 过滤器，但 exported=true 仍允许其它应用显式拉起 Unity，
    /// 绕过原生 PrivacyActivity 的同意页。这里在 Gradle 工程生成后、Gradle 构建前，
    /// 把 UnityPlayerActivity 改回 exported="false"，并清掉可能残留的 LAUNCHER 过滤器。
    ///
    /// 为什么不用 Custom Launcher Manifest：
    /// - 那需要长期打开 PlayerSettings.Android.useCustomLauncherManifest，并把图标/屏幕
    ///   等 launcher 默认字段整份复制过去；本钩子只做一条最小修正，随每次构建自动执行。
    /// </summary>
    public sealed class AndroidPrivacyManifestPostprocessor : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";
        const string UnityActivity = "com.unity3d.player.UnityPlayerActivity";
        const string Tag = "[隐私合规]";

        // 放在 Unity 自己写完清单之后执行。
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                // 旧版接口传入的是 Gradle 工程根目录。
                manifestPath = Path.Combine(path, "unityLibrary", "src", "main", "AndroidManifest.xml");
            }
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"{Tag} 找不到生成的 AndroidManifest，跳过 exported 修正: {path}");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("android", AndroidNs);
            ns.AddNamespace("tools", ToolsNs);

            bool changed = false;

            XmlNode activity = doc.SelectSingleNode(
                $"/manifest/application/activity[@android:name='{UnityActivity}']", ns);
            if (activity == null)
            {
                Debug.LogWarning($"{Tag} 生成清单里没有 {UnityActivity}，跳过 exported 修正。");
            }
            else
            {
                SetAndroidAttribute(doc, activity, "exported", "false");
                RemoveLauncherFilter(activity, ns);
                changed = true;
                Debug.Log($"{Tag} 已把 {UnityActivity}.exported 修正为 false（{manifestPath}）");
            }

            // TapTap 上架变体：Unity 会按「使用了网络 API」自动注入 INTERNET 权限，
            // 自定义主清单里的声明同样会带进来。这里统一摘掉，保证包体零联网能力。
            //
            // 注意：不能写 `#if P1_OFFLINE`。宏只写进 Android 平台的 defines，
            // 而本类是 P1.Editor 程序集（按 Editor 平台编译），条件编译永远为假，
            // 必须在运行时读 Android 的 defines 才能判断当前是不是离线变体。
            if (IsOfflineVariant())
            {
                int removed = RemovePermission(doc, ns, "android.permission.INTERNET");
                if (removed > 0)
                {
                    changed = true;
                    Debug.Log($"{Tag} TapTap 上架变体：已移除 {removed} 条 INTERNET 权限声明（{manifestPath}）");
                }
            }

            if (changed) doc.Save(manifestPath);
        }

        /// <summary>
        /// 当前 Android 平台是否开着离线变体宏。
        /// 只读 Android 的 defines：宏由 <see cref="TapTapBuildSuite"/> 写在该平台，
        /// 而本类按 Editor 平台编译，自身的条件编译看不到它。
        /// </summary>
        static bool IsOfflineVariant()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (string.IsNullOrEmpty(defines)) return false;

            foreach (string d in defines.Split(';'))
            {
                if (d.Trim() == BuildVariants.OfflineDefine) return true;
            }
            return false;
        }

        /// <summary>删掉指定权限的所有 uses-permission 节点，返回删除条数。</summary>
        static int RemovePermission(XmlDocument doc, XmlNamespaceManager ns, string permissionName)
        {
            var nodes = doc.SelectNodes(
                $"/manifest/uses-permission[@android:name='{permissionName}']", ns);
            if (nodes == null) return 0;

            int count = 0;
            foreach (XmlNode node in nodes)
            {
                node.ParentNode?.RemoveChild(node);
                count++;
            }
            return count;
        }

        static void SetAndroidAttribute(XmlDocument doc, XmlNode node, string name, string value)
        {
            XmlAttribute attribute = null;
            foreach (XmlAttribute candidate in node.Attributes)
            {
                if (candidate.LocalName == name && candidate.NamespaceURI == AndroidNs)
                {
                    attribute = candidate;
                    break;
                }
            }

            if (attribute != null)
            {
                attribute.Value = value;
                return;
            }

            attribute = doc.CreateAttribute("android", name, AndroidNs);
            attribute.Value = value;
            node.Attributes.Append(attribute);
        }

        static void RemoveLauncherFilter(XmlNode activity, XmlNamespaceManager ns)
        {
            var filters = activity.SelectNodes("intent-filter", ns);
            if (filters == null) return;

            foreach (XmlNode filter in filters)
            {
                bool hasMain = filter.SelectSingleNode(
                    "action[@android:name='android.intent.action.MAIN']", ns) != null;
                bool hasLauncher = filter.SelectSingleNode(
                    "category[@android:name='android.intent.category.LAUNCHER']", ns) != null;

                if (hasMain && hasLauncher)
                    activity.RemoveChild(filter);
            }
        }
    }
}
