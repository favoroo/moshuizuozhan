using System.Collections.Generic;
using P1.Core;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// UI 图标运行时查找（P10）：统一解决「图标只在编辑器里能加载、真机变白块」的问题。
    ///
    /// 查找顺序：
    /// 1. Resources/P1RuntimeAssets 里的 uiIcons（由菜单 P1/构建/11 打包，独立包/移动端唯一可靠路径）；
    /// 2. Resources/P1Icons/&lt;name&gt;（可选，方便临时放图）；
    /// 3. 编辑器下回退 AssetDatabase（仅开发期，编译后不存在）。
    ///
    /// 调用方拿到 null 时必须隐藏 Image 或画程序化剪影，**绝不允许把白底 Image 留在屏幕上**。
    /// </summary>
    public static class UIIconLibrary
    {
        const string k_ResourcesRoot = "P1Icons/";
        const string k_EditorIconsRoot = "Assets/_Project/Art/Sprites/Icons/";

        static readonly string[] k_EditorFolders = { "Glyphs", "Marks", "Rules", "Specials", "Subs", "Weapons" };

        static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();
        static readonly HashSet<string> s_warned = new HashSet<string>();

#if UNITY_EDITOR
        static readonly Dictionary<string, Sprite> s_editorCache = new Dictionary<string, Sprite>();
#endif

        /// <summary>按文件名（不含扩展名，如 "mark_squid"）取图标；取不到返回 null。</summary>
        public static Sprite Get(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;
            if (s_cache.TryGetValue(iconName, out Sprite cached)) return cached;

            Sprite sprite = Lookup(iconName);
            s_cache[iconName] = sprite;

            if (sprite == null)
            {
                WarnOnce(iconName);
            }

            return sprite;
        }

        public static bool TryGet(string iconName, out Sprite sprite)
        {
            sprite = Get(iconName);
            return sprite != null;
        }

        /// <summary>热重载/重新打包资源库后清缓存。</summary>
        public static void ClearCache()
        {
            s_cache.Clear();
            s_warned.Clear();
#if UNITY_EDITOR
            s_editorCache.Clear();
#endif
        }

        static Sprite Lookup(string iconName)
        {
            // 1) 运行时资源库（独立包的正路）
            var library = RuntimeAssetLibrary.Load();
            if (library != null)
            {
                Sprite fromLibrary = library.GetUiIcon(iconName);
                if (fromLibrary != null) return fromLibrary;
            }

            // 2) Resources 兜底
            Sprite fromResources = Resources.Load<Sprite>(k_ResourcesRoot + iconName);
            if (fromResources != null) return fromResources;

#if UNITY_EDITOR
            // 3) 编辑器兜底：美术刚落地、还没重跑菜单 11 时也能看到图
            if (s_editorCache.TryGetValue(iconName, out Sprite editorCached)) return editorCached;

            for (int i = 0; i < k_EditorFolders.Length; i++)
            {
                string path = k_EditorIconsRoot + k_EditorFolders[i] + "/" + iconName + ".png";
                var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    s_editorCache[iconName] = sprite;
                    return sprite;
                }
            }
#endif

            return null;
        }

        static void WarnOnce(string iconName)
        {
            if (!s_warned.Add(iconName)) return;
            Debug.LogWarning("[UIIconLibrary] 图标缺失: " + iconName +
                             "（检查 P1/构建/11 是否把该图标打进 P1RuntimeAssets）");
        }
    }
}