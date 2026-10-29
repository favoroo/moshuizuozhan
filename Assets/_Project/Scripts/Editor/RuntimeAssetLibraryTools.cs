using System.Collections.Generic;
using System.IO;
using System.Linq;
using P1.Core;
using P1.UI;
using P1.Weapons;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 生成 Resources/P1RuntimeAssets：把只在编辑器里按路径加载的音效、武器图标、主题与武器配置
    /// 固化进一个可被独立包加载的 ScriptableObject。场景与 .asset 均不手改。
    /// </summary>
    public static class RuntimeAssetLibraryTools
    {
        private const string AssetPath = "Assets/_Project/Resources/P1RuntimeAssets.asset";
        private const string ResourcesFolder = "Assets/_Project/Resources";
        private const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        private const string ShooterConfigPath = "Assets/_Project/ScriptableObjects/WeaponConfig.asset";
        private const string WeaponsFolder = "Assets/_Project/ScriptableObjects/Weapons";
        private const string IconsFolder = "Assets/_Project/Art/Sprites/Icons/Weapons";
        private const string SfxFolder = "Assets/_Project/Audio/SFX";

        private static readonly string[] WeaponIconFileNames =
        {
            "weapon_shooter",
            "weapon_roller",
            "weapon_charger",
            "weapon_dualies",
            "weapon_slosher",
            "weapon_blaster",
            "weapon_splatling"
        };

        [MenuItem("P1/构建/11. 生成运行时资源库 (P1RuntimeAssets)", false, 83)]
        public static void BuildLibrary()
        {
            EnsureFolder(ResourcesFolder);

            RuntimeAssetLibrary library = AssetDatabase.LoadAssetAtPath<RuntimeAssetLibrary>(AssetPath);
            if (library == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(AssetPath) != null)
                {
                    Debug.LogError($"[P1][RuntimeAssets] {AssetPath} 已存在但不是 RuntimeAssetLibrary，请先手动确认/删除。");
                    return;
                }

                library = ScriptableObject.CreateInstance<RuntimeAssetLibrary>();
                AssetDatabase.CreateAsset(library, AssetPath);
            }

            library.uiTheme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            library.weaponConfigs = LoadWeaponConfigs();
            library.weaponIcons = LoadWeaponIcons();
            LoadSfx(library);

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int iconCount = library.weaponIcons != null ? library.weaponIcons.Count(i => i != null) : 0;
            Debug.Log($"[P1][RuntimeAssets] 生成完成：{AssetPath}；武器 {library.weaponConfigs.Length}，图标 {iconCount}，音效 {library.sfxClips.Length}。");
        }

        [MenuItem("P1/素材/6. 校验运行时资源库 (P1RuntimeAssets)", false, 6)]
        public static void ValidateLibrary()
        {
            RuntimeAssetLibrary library = AssetDatabase.LoadAssetAtPath<RuntimeAssetLibrary>(AssetPath);
            if (library == null)
            {
                Debug.LogError($"[P1][RuntimeAssets] 未找到 {AssetPath}，请先运行 P1/构建/11。");
                return;
            }

            int missingWeapons = 0;
            for (int i = 0; i < WeaponIconFileNames.Length; i++)
            {
                if (library.GetWeapon((WeaponKind)i) == null) missingWeapons++;
                if (library.GetWeaponIcon((WeaponKind)i) == null) missingWeapons++;
            }

            int missingSfx = 0;
            if (library.sfxNames != null && library.sfxClips != null)
            {
                for (int i = 0; i < library.sfxNames.Length; i++)
                {
                    if (library.sfxClips[i] == null) missingSfx++;
                }
            }

            Debug.Log($"[P1][RuntimeAssets] 校验：主题={(library.uiTheme != null ? "OK" : "缺失")}；" +
                      $"武器/图标缺失 {missingWeapons}；音效 {library.sfxClips.Length} 条，缺失 {missingSfx}。");
        }

        private static WeaponConfigSO[] LoadWeaponConfigs()
        {
            var configs = new List<WeaponConfigSO>();
            WeaponConfigSO shooter = AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(ShooterConfigPath);
            if (shooter != null)
                configs.Add(shooter);

            string[] guids = AssetDatabase.FindAssets("t:WeaponConfigSO", new[] { WeaponsFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                WeaponConfigSO config = AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(path);
                if (config != null && !configs.Contains(config))
                    configs.Add(config);
            }

            return configs.OrderBy(c => (int)c.weaponKind).ToArray();
        }

        private static Sprite[] LoadWeaponIcons()
        {
            var icons = new Sprite[WeaponIconFileNames.Length];
            for (int i = 0; i < WeaponIconFileNames.Length; i++)
            {
                string path = $"{IconsFolder}/{WeaponIconFileNames[i]}.png";
                icons[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (icons[i] == null)
                    Debug.LogWarning($"[P1][RuntimeAssets] 缺少武器图标：{path}");
            }

            return icons;
        }

        private static void LoadSfx(RuntimeAssetLibrary library)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxFolder });
            var pairs = new List<(string Name, AudioClip Clip)>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null || !path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                pairs.Add((Path.GetFileNameWithoutExtension(path), clip));
            }

            pairs.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.Ordinal));
            library.sfxNames = pairs.Select(p => p.Name).ToArray();
            library.sfxClips = pairs.Select(p => p.Clip).ToArray();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}