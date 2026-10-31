using P1.Weapons;
using UnityEngine;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 武器选择界面纯逻辑辅助类（P8 选枪系统核心）：
    /// 1. 7 种武器的索引、分类、显示名、资源路径映射；
    /// 2. PlayerPrefs 持久化键名与非法值安全钳制；
    /// 3. 面板属性（伤害/射程/射速/耗墨/大招）格式化输出。
    /// 全部为纯逻辑或安全函数，EditMode 测试可直接调用。
    /// </summary>
    public static class WeaponSelectMath
    {
        public const int WeaponCount = 7;
        public const string PrefsKeySelectedWeaponIndex = "P1.Loadout.SelectedWeaponIndex";

        /// <summary>非法索引钳制到 [0, 6]。</summary>
        public static int ClampIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= WeaponCount) return WeaponCount - 1;
            return index;
        }

        /// <summary>读取持久化的武器索引（默认 0 = Shooter）。</summary>
        public static int LoadSelectedWeaponIndex()
        {
            return ClampIndex(PlayerPrefs.GetInt(PrefsKeySelectedWeaponIndex, 0));
        }

        /// <summary>保存武器索引到 PlayerPrefs。</summary>
        public static void SaveSelectedWeaponIndex(int index)
        {
            PlayerPrefs.SetInt(PrefsKeySelectedWeaponIndex, ClampIndex(index));
            PlayerPrefs.Save();
        }

        /// <summary>索引映射为主武器类型。</summary>
        public static WeaponKind IndexToKind(int index)
        {
            int clamped = ClampIndex(index);
            switch (clamped)
            {
                case 0: return WeaponKind.Shooter;
                case 1: return WeaponKind.Roller;
                case 2: return WeaponKind.Charger;
                case 3: return WeaponKind.Dualies;
                case 4: return WeaponKind.Slosher;
                case 5: return WeaponKind.Blaster;
                case 6: return WeaponKind.Splatling;
                default: return WeaponKind.Shooter;
            }
        }

        /// <summary>主武器类型映射为面板索引。</summary>
        public static int KindToIndex(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return 0;
                case WeaponKind.Roller: return 1;
                case WeaponKind.Charger: return 2;
                case WeaponKind.Dualies: return 3;
                case WeaponKind.Slosher: return 4;
                case WeaponKind.Blaster: return 5;
                case WeaponKind.Splatling: return 6;
                default: return 0;
            }
        }

        /// <summary>武器英文标准名称（TitanOne 标题字体渲染）。</summary>
        public static string GetWeaponEnglishName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return "Spritzer";
                case WeaponKind.Roller: return "Swell Roller";
                case WeaponKind.Charger: return "Glint Charger";
                case WeaponKind.Dualies: return "Twinfin Dualies";
                case WeaponKind.Slosher: return "Tidebucket Slosher";
                case WeaponKind.Blaster: return "Popper Blaster";
                case WeaponKind.Splatling: return "Gyre Splatling";
                default: return "Spritzer";
            }
        }

        /// <summary>武器中文对照名称。</summary>
        public static string GetWeaponChineseName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return "喷射小枪";
                case WeaponKind.Roller: return "推地滚筒";
                case WeaponKind.Charger: return "蓄力狙击";
                case WeaponKind.Dualies: return "双枪翻滚";
                case WeaponKind.Slosher: return "泼桶墨浪";
                case WeaponKind.Blaster: return "爆破空爆";
                case WeaponKind.Splatling: return "旋转加特林";
                default: return "喷射小枪";
            }
        }

        /// <summary>武器战术定位标语。</summary>
        public static string GetWeaponRoleTag(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return "均衡连发";
                case WeaponKind.Roller: return "近战碾压";
                case WeaponKind.Charger: return "贯穿狙击";
                case WeaponKind.Dualies: return "翻滚架枪";
                case WeaponKind.Slosher: return "越障泼洒";
                case WeaponKind.Blaster: return "范围空爆";
                case WeaponKind.Splatling: return "弹幕压制";
                default: return "均衡连发";
            }
        }

        /// <summary>主武器 ScriptableObject 资产路径。</summary>
        public static string GetWeaponAssetPath(WeaponKind kind)
        {
            const string runtimeFolder = "Assets/_Project/ScriptableObjects";
            const string weaponsFolder = runtimeFolder + "/Weapons";
            switch (kind)
            {
                case WeaponKind.Shooter: return $"{runtimeFolder}/WeaponConfig.asset";
                case WeaponKind.Roller: return $"{weaponsFolder}/Weapon_SwellRoller.asset";
                case WeaponKind.Charger: return $"{weaponsFolder}/Weapon_GlintCharger.asset";
                case WeaponKind.Dualies: return $"{weaponsFolder}/Weapon_TwinfinDualies.asset";
                case WeaponKind.Slosher: return $"{weaponsFolder}/Weapon_TidebucketSlosher.asset";
                case WeaponKind.Blaster: return $"{weaponsFolder}/Weapon_PopperBlaster.asset";
                case WeaponKind.Splatling: return $"{weaponsFolder}/Weapon_GyreSplatling.asset";
                default: return $"{runtimeFolder}/WeaponConfig.asset";
            }
        }

        /// <summary>武器图标文件名（位于 Art/Sprites/Icons/Weapons/ 下）。</summary>
        public static string GetWeaponIconFileName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return "weapon_shooter.png";
                case WeaponKind.Roller: return "weapon_roller.png";
                case WeaponKind.Charger: return "weapon_charger.png";
                case WeaponKind.Dualies: return "weapon_dualies.png";
                case WeaponKind.Slosher: return "weapon_slosher.png";
                case WeaponKind.Blaster: return "weapon_blaster.png";
                case WeaponKind.Splatling: return "weapon_splatling.png";
                default: return "weapon_shooter.png";
            }
        }

        /// <summary>武器图标完整资产路径。</summary>
        public static string GetWeaponIconAssetPath(WeaponKind kind)
        {
            return "Assets/_Project/Art/Sprites/Icons/Weapons/" + GetWeaponIconFileName(kind);
        }

        /// <summary>格式化伤害数值。</summary>
        public static string FormatDamage(WeaponConfigSO config, WeaponKind kind)
        {
            if (config == null)
            {
                switch (kind)
                {
                    case WeaponKind.Shooter: return "36";
                    case WeaponKind.Roller: return "140 / 125";
                    case WeaponKind.Charger: return "40~160";
                    case WeaponKind.Dualies: return "30";
                    case WeaponKind.Slosher: return "70";
                    case WeaponKind.Blaster: return "125 (爆70)";
                    case WeaponKind.Splatling: return "28";
                    default: return "36";
                }
            }

            switch (kind)
            {
                case WeaponKind.Roller:
                    return $"{Mathf.RoundToInt(config.rollDamage)} / {Mathf.RoundToInt(config.flickDamageNear)}";
                case WeaponKind.Charger:
                    return $"{Mathf.RoundToInt(config.chargeDamageMin)}~{Mathf.RoundToInt(config.chargeDamageMax)}";
                case WeaponKind.Blaster:
                    return $"{Mathf.RoundToInt(config.damage)} (爆{Mathf.RoundToInt(config.blasterSplashDamageMax)})";
                case WeaponKind.Slosher:
                    return $"{Mathf.RoundToInt(config.slosherHeadDamage)}";
                default:
                    return $"{Mathf.RoundToInt(config.damage)}";
            }
        }

        /// <summary>格式化射程数值。</summary>
        public static string FormatRange(WeaponConfigSO config, WeaponKind kind)
        {
            if (config == null)
            {
                switch (kind)
                {
                    case WeaponKind.Shooter: return "12.0m";
                    case WeaponKind.Roller: return "8.5m";
                    case WeaponKind.Charger: return "27.0m";
                    case WeaponKind.Dualies: return "11.0m";
                    case WeaponKind.Slosher: return "9.5m";
                    case WeaponKind.Blaster: return "10.5m";
                    case WeaponKind.Splatling: return "15.0m";
                    default: return "12.0m";
                }
            }

            float range = config.maxRange;
            if (kind == WeaponKind.Roller) range = Mathf.Max(range, config.flickRange);
            if (kind == WeaponKind.Charger) range = Mathf.Max(range, config.chargeRangeMax);
            return $"{range:0.0}m";
        }

        /// <summary>格式化开火频率 / 蓄力耗时。</summary>
        public static string FormatFireRate(WeaponConfigSO config, WeaponKind kind)
        {
            if (config == null)
            {
                switch (kind)
                {
                    case WeaponKind.Shooter: return "10.0/s";
                    case WeaponKind.Roller: return "挥击 1.6/s";
                    case WeaponKind.Charger: return "蓄力 1.0s";
                    case WeaponKind.Dualies: return "12.0/s (架枪15/s)";
                    case WeaponKind.Slosher: return "1.6/s";
                    case WeaponKind.Blaster: return "1.3/s";
                    case WeaponKind.Splatling: return "蓄力0.85s · 15/s";
                    default: return "10.0/s";
                }
            }

            switch (kind)
            {
                case WeaponKind.Charger:
                    return $"蓄力 {config.chargeTime:0.0}s";
                case WeaponKind.Splatling:
                    return $"蓄力 {config.splatlingChargeTime:0.0}s · {1f / Mathf.Max(0.01f, config.fireInterval):0}发/s";
                case WeaponKind.Roller:
                    return $"挥击 {1f / Mathf.Max(0.01f, config.flickInterval):0.0}/s";
                case WeaponKind.Dualies:
                    return $"{1f / Mathf.Max(0.01f, config.fireInterval):0.0}/s";
                case WeaponKind.Slosher:
                    return $"{1f / Mathf.Max(0.01f, config.fireInterval):0.0}/s";
                case WeaponKind.Blaster:
                    return $"{1f / Mathf.Max(0.01f, config.fireInterval):0.0}/s";
                default:
                    return $"{1f / Mathf.Max(0.01f, config.fireInterval):0.0}/s";
            }
        }

        /// <summary>格式化单次攻击耗墨百分比。</summary>
        public static string FormatInkCost(WeaponConfigSO config, WeaponKind kind)
        {
            if (config == null)
            {
                switch (kind)
                {
                    case WeaponKind.Shooter: return "0.95%";
                    case WeaponKind.Roller: return "9.0%";
                    case WeaponKind.Charger: return "18.0%";
                    case WeaponKind.Dualies: return "0.85%";
                    case WeaponKind.Slosher: return "7.5%";
                    case WeaponKind.Blaster: return "9.0%";
                    case WeaponKind.Splatling: return "0.6%";
                    default: return "1.0%";
                }
            }

            switch (kind)
            {
                case WeaponKind.Roller:
                    return $"{config.flickInkCost:0.0}%";
                case WeaponKind.Charger:
                    return $"{config.chargeInkCostFull:0.0}%";
                default:
                    return $"{config.inkCostPerShot:0.0}%";
            }
        }

        /// <summary>格式化匹配的大招名称。</summary>
        public static string FormatSpecial(WeaponConfigSO config, WeaponKind kind)
        {
            SpecialKind special = config != null ? config.specialKind :
                (kind == WeaponKind.Charger || kind == WeaponKind.Blaster || kind == WeaponKind.Splatling ? SpecialKind.Storm : SpecialKind.Slam);
            return special == SpecialKind.Storm ? "风暴云 (Tempest)" : "降落 (Slam)";
        }
    }
}
