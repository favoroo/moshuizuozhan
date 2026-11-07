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

        /// <summary>武器英文标准名称（标题字重 MiSans-Heavy 渲染）。</summary>
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

        // ==================== 选枪卡三条对比横条 ====================
        // 7 把武器的差异是这游戏最值钱的深度，但「伤/距/频/耗」四个裸数字读不出强弱。
        // 三条 0~1 横条把武器配置（WeaponConfigSO）折算成可对比的读数：
        //   伤害 = 持续伤害密度（单位时间输出，蓄力武器按「满蓄伤害 / 整周期」折算）；
        //   涂地 = 每点墨水能铺出的有效新地面积（终点斑 + 沿途滴落 × 自重叠折扣；推地带连续无自重叠）；
        //   机动 = 有效射程（×0.6）与开火/走位机动性（×0.4）的合成。
        // 归一化锚点是设计常量而非跨武器相对值：列表增删武器时单卡读数保持稳定。
        // 锚点按当前 7 武器规范配置（WeaponConfigSO.Generate* / Apply*Spec）校准，保证排序符合手感认知。

        /// <summary>终点爆斑的有效覆盖率折扣：连射时相邻墨斑大量自重叠。</summary>
        private const float EndSplatOverlapFactor = 0.4f;

        /// <summary>沿途滴落的覆盖率折扣：滴距小于墨斑直径、沿弹道重叠更严重。</summary>
        private const float TrailOverlapFactor = 0.12f;

        /// <summary>伤害密度横条的 DPS 归一化上锚点（加特林爆发 424 ≈ 满条）。</summary>
        private const float DamageDpsAnchorHigh = 430f;

        /// <summary>涂地效率横条的「平方米/墨」归一化上锚点（滚筒 3.04 ≈ 满条）。</summary>
        private const float TurfPerInkAnchorHigh = 3.1f;

        /// <summary>机动横条里射程分量的归一化区间：6m 以下视为贴脸，27m（满蓄狙）≈ 满条。</summary>
        private const float RangeAnchorLow = 6f;
        private const float RangeAnchorHigh = 27f;

        /// <summary>选枪卡三条对比横条的取值（全部 0~1，可直接驱动填充条宽度）。</summary>
        public struct WeaponStatBars
        {
            /// <summary>伤害密度：单位时间持续输出（爆破/泼桶/蓄力按单发周期折算）。</summary>
            public float DamageDensity01;

            /// <summary>涂地效率：每点墨水能铺出的有效新地面积。</summary>
            public float TurfEfficiency01;

            /// <summary>射程机动：有效射程与开火/走位机动性的合成。</summary>
            public float RangeMobility01;
        }

        /// <summary>
        /// 计算某把武器的三条对比横条读数（0~1）。config 为空时按规范默认值兜底，
        /// 与 FormatDamage / FormatRange 的 null 分支同一策略，EditMode 测试可不依赖资产。
        /// </summary>
        public static WeaponStatBars ComputeStatBars(WeaponConfigSO config, WeaponKind kind)
        {
            float dps = ResolveDamagePerSecond(config, kind);
            float turfPerInk = ResolveTurfSquareMetersPerInk(config, kind);
            float range = ResolveEffectiveRange(config, kind);
            float mobility = ResolveMobility01(kind);

            return new WeaponStatBars
            {
                DamageDensity01 = InverseLerp01(0f, DamageDpsAnchorHigh, dps),
                TurfEfficiency01 = InverseLerp01(0f, TurfPerInkAnchorHigh, turfPerInk),
                RangeMobility01 = 0.6f * InverseLerp01(RangeAnchorLow, RangeAnchorHigh, range) + 0.4f * mobility,
            };
        }

        /// <summary>持续伤害密度（DPS）：滚筒按挥击、蓄力狙按「满蓄伤害 / 蓄力+硬直」、其余按 直击伤害/开火间隔。</summary>
        private static float ResolveDamagePerSecond(WeaponConfigSO config, WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                    return SafeDiv(config != null ? config.flickDamageNear : 125f,
                        config != null ? config.flickInterval : 0.62f);
                case WeaponKind.Charger:
                    float cycle = (config != null ? config.chargeTime : 1.0f)
                                + (config != null ? config.chargeReleaseCooldown : 0.28f);
                    return SafeDiv(config != null ? config.chargeDamageMax : 160f, Mathf.Max(0.05f, cycle));
                case WeaponKind.Slosher:
                    return SafeDiv(config != null ? config.slosherHeadDamage : 70f,
                        config != null ? config.fireInterval : 0.62f);
                case WeaponKind.Blaster:
                    return SafeDiv(config != null ? config.damage : 125f,
                        config != null ? config.fireInterval : 0.78f);
                default:
                {
                    // 射击系在 null 配置下也要按各武器的规范值区分：
                    // 加特林 28 伤/0.066s（424 DPS，伤害密度第一）、双枪 30/0.083、小枪 36/0.1。
                    float damage = 36f;
                    float interval = 0.1f;
                    if (config != null)
                    {
                        damage = config.damage;
                        interval = config.fireInterval;
                    }
                    else if (kind == WeaponKind.Splatling)
                    {
                        damage = 28f;
                        interval = 0.066f;
                    }
                    else if (kind == WeaponKind.Dualies)
                    {
                        damage = 30f;
                        interval = 0.083f;
                    }
                    return SafeDiv(damage, Mathf.Max(0.01f, interval));
                }
            }
        }

        /// <summary>
        /// 涂地效率（平方米有效新地 / 每点墨水）。射击系（小枪/双枪/加特林）= 终点爆斑 + 沿途滴落，
        /// 分别乘自重叠折扣；滚筒 = 推地实心墨带宽 ÷ 每米耗墨 × 2.0（推地带是连续新墨无自重叠，
        /// 2.0 为与射击系重叠折扣对齐的校准系数，保证「滚筒涂地王」的排序符合实机手感）。
        /// </summary>
        private static float ResolveTurfSquareMetersPerInk(WeaponConfigSO config, WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                {
                    float width = config != null ? config.rollWidth : 1.9f;
                    float inkPerMeter = config != null ? config.rollInkPerMeter : 1.25f;
                    return SafeDiv(width, Mathf.Max(0.1f, inkPerMeter)) * 2.0f;
                }
                case WeaponKind.Charger:
                {
                    float line = SafeDiv(config != null ? config.chargeRangeMax : 27f,
                        Mathf.Max(0.2f, config != null ? config.chargeLineSplatEvery : 1.2f))
                        * Mathf.PI * Sq(config != null ? config.chargeLineRadius : 0.55f) * TrailOverlapFactor;
                    float impact = Mathf.PI * Sq(config != null ? config.chargeImpactRadius : 1.2f) * EndSplatOverlapFactor;
                    return SafeDiv(line + impact, Mathf.Max(1f, config != null ? config.chargeInkCostFull : 21f));
                }
                case WeaponKind.Slosher:
                {
                    float end = Mathf.PI * Sq(config != null ? config.endSplatRadius : 1.05f) * EndSplatOverlapFactor;
                    float trail = SafeDiv(config != null ? config.maxRange : 9.5f,
                        Mathf.Max(0.2f, config != null ? config.trailDropDistance : 1.4f))
                        * Mathf.PI * Sq(config != null ? config.trailSplatRadius : 0.5f) * TrailOverlapFactor;
                    // 头滴落地溅射是泼桶的主要铺地手段，单独按 0.35 覆盖率计入
                    float splash = Mathf.PI * Sq(config != null ? config.slosherSplashRadius : 1.1f) * 0.35f;
                    return SafeDiv(end + trail + splash, Mathf.Max(1f, config != null ? config.inkCostPerShot : 8.6f));
                }
                case WeaponKind.Blaster:
                {
                    float burst = Mathf.PI * Sq(config != null ? config.blasterBurstRadius : 1.9f) * EndSplatOverlapFactor;
                    return SafeDiv(burst, Mathf.Max(1f, config != null ? config.inkCostPerShot : 10.4f));
                }
                default:
                {
                    float endAvg = config != null ? (config.endSplatRadiusMin + config.endSplatRadiusMax) * 0.5f : 1.03f;
                    float end = Mathf.PI * Sq(endAvg) * EndSplatOverlapFactor;
                    float trail = SafeDiv(config != null ? config.maxRange : 12f,
                        Mathf.Max(0.2f, config != null ? config.trailDropDistance : 1.05f))
                        * Mathf.PI * Sq(config != null ? config.trailSplatRadius : 0.44f) * TrailOverlapFactor;
                    return SafeDiv(end + trail, Mathf.Max(1f, config != null ? config.inkCostPerShot : 1.1f));
                }
            }
        }

        /// <summary>有效射程（米）：滚筒取挥击距离、蓄力狙取满蓄射程，其余取 maxRange。</summary>
        private static float ResolveEffectiveRange(WeaponConfigSO config, WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                    return Mathf.Max(0.1f, config != null ? Mathf.Max(config.maxRange, config.flickRange) : 8.5f);
                case WeaponKind.Charger:
                    return Mathf.Max(0.1f, config != null ? config.chargeRangeMax : 27f);
                case WeaponKind.Dualies:
                    return Mathf.Max(0.1f, config != null ? config.maxRange : 11f);
                case WeaponKind.Slosher:
                    return Mathf.Max(0.1f, config != null ? config.maxRange : 9.5f);
                case WeaponKind.Blaster:
                    return Mathf.Max(0.1f, config != null ? config.maxRange : 10.5f);
                case WeaponKind.Splatling:
                    return Mathf.Max(0.1f, config != null ? config.maxRange : 15f);
                default:
                    return Mathf.Max(0.1f, config != null ? config.maxRange : 12f);
            }
        }

        /// <summary>
        /// 开火/走位机动性（0~1）：加特林蓄力 2.4m/s 蜗牛与蓄力狙硬直垫底，
        /// 双枪双段翻滚满档、滚筒推地即机动次之。config 里只有加特林带移速字段，
        /// 其余是行为手感定档（与 GetWeaponRoleTag 同类的稳定映射表）。
        /// </summary>
        private static float ResolveMobility01(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Dualies: return 1.00f;
                case WeaponKind.Roller: return 0.90f;
                case WeaponKind.Shooter: return 0.70f;
                case WeaponKind.Slosher: return 0.55f;
                case WeaponKind.Blaster: return 0.55f;
                case WeaponKind.Charger: return 0.35f;
                case WeaponKind.Splatling: return 0.30f;
                default: return 0.70f;
            }
        }

        private static float InverseLerp01(float low, float high, float value)
        {
            if (high <= low)
                return 0f;
            return Mathf.Clamp01((value - low) / (high - low));
        }

        private static float Sq(float value)
        {
            return value * value;
        }

        private static float SafeDiv(float numerator, float denominator)
        {
            return denominator > 0.0001f ? numerator / denominator : 0f;
        }
    }
}
