using P1.UI;
using P1.Weapons;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 运行时资源库：把原先只在编辑器里按路径加载的资源（音效、武器图标、UI 主题与武器配置）
    /// 固化成 Resources 资产，保证 macOS / Android 独立包里也能取到。
    /// 由菜单 P1/构建/11 生成，运行时通过 <see cref="Load"/> 读取并缓存。
    /// </summary>
    [CreateAssetMenu(menuName = "P1/Runtime Asset Library", fileName = "P1RuntimeAssets")]
    public class RuntimeAssetLibrary : ScriptableObject
    {
        public const string ResourcePath = "P1RuntimeAssets";

        [Header("UI 与武器")]
        public UIThemeSO uiTheme;
        public WeaponConfigSO[] weaponConfigs;
        public Sprite[] weaponIcons;

        [Header("音效（按文件名索引，不含扩展名）")]
        public string[] sfxNames;
        public AudioClip[] sfxClips;

        private static RuntimeAssetLibrary _cached;

        /// <summary>读取 Resources/P1RuntimeAssets；不存在时返回 null（编辑器首次生成前）。</summary>
        public static RuntimeAssetLibrary Load()
        {
            if (_cached == null)
                _cached = Resources.Load<RuntimeAssetLibrary>(ResourcePath);
            return _cached;
        }

        /// <summary>按 WAV 文件名（不含扩展名）取音效，找不到返回 null。</summary>
        public AudioClip GetSfx(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || sfxNames == null || sfxClips == null)
                return null;

            int count = Mathf.Min(sfxNames.Length, sfxClips.Length);
            for (int i = 0; i < count; i++)
            {
                if (sfxNames[i] == clipName)
                    return sfxClips[i];
            }

            return null;
        }

        /// <summary>按武器类型取配置，找不到返回 null。</summary>
        public WeaponConfigSO GetWeapon(WeaponKind kind)
        {
            if (weaponConfigs == null)
                return null;

            for (int i = 0; i < weaponConfigs.Length; i++)
            {
                WeaponConfigSO config = weaponConfigs[i];
                if (config != null && config.weaponKind == kind)
                    return config;
            }

            return null;
        }

        /// <summary>按武器类型取图标（数组按 WeaponKind 枚举值排序）。</summary>
        public Sprite GetWeaponIcon(WeaponKind kind)
        {
            int index = (int)kind;
            if (weaponIcons == null || index < 0 || index >= weaponIcons.Length)
                return null;

            return weaponIcons[index];
        }
    }
}