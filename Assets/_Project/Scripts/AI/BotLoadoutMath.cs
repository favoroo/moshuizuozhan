using System.Collections.Generic;
using P1.Weapons;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 局内武器抽卡与昵称纯逻辑（照 INKWAVE src/game/match.js 的 pickTeam / BOT_NAMES 规则）：
    /// - 同队武器类型互不重复（每队 4 人各拿一种）；
    /// - 队友抽取池排除玩家当前武器类型（玩家在己方队内独占一种）；
    /// - 敌方从全部 7 种里不放回抽 4 种，允许出现玩家的武器类型；
    /// - 同种子结果确定，每局换种子即整套重抽；复活不换枪。
    /// 全部为纯静态函数、无场景依赖，EditMode 可直接测试。
    /// </summary>
    public static class BotLoadoutMath
    {
        /// <summary>主武器种类数（与 <see cref="WeaponKind"/> 枚举数量一致）。</summary>
        public const int WeaponKindCount = 7;

        /// <summary>
        /// 昵称池（对标 INKWAVE 的 20 名洗牌名单）。用字全部为项目 MiSans 常见汉字，
        /// 避免真机缺字显示成 □。
        /// </summary>
        public static readonly string[] BotNames =
        {
            "泡泡", "阿墨", "海苔", "珊瑚", "小浪",
            "疾风", "水滴", "塔可", "芥末", "莫西",
            "朱诺", "凯西", "咕噜", "布丁", "麻薯",
            "豆花", "青柠", "橘子", "闪光", "冰块",
        };

        /// <summary>武器类型映射为主武器资产路径（与 WeaponSelectMath 保持一致，EditMode 测试校验）。</summary>
        public static string GetWeaponAssetPath(WeaponKind kind)
        {
            const string runtimeFolder = "Assets/_Project/ScriptableObjects";
            const string weaponsFolder = runtimeFolder + "/Weapons";
            switch (kind)
            {
                case WeaponKind.Roller: return weaponsFolder + "/Weapon_SwellRoller.asset";
                case WeaponKind.Charger: return weaponsFolder + "/Weapon_GlintCharger.asset";
                case WeaponKind.Dualies: return weaponsFolder + "/Weapon_TwinfinDualies.asset";
                case WeaponKind.Slosher: return weaponsFolder + "/Weapon_TidebucketSlosher.asset";
                case WeaponKind.Blaster: return weaponsFolder + "/Weapon_PopperBlaster.asset";
                case WeaponKind.Splatling: return weaponsFolder + "/Weapon_GyreSplatling.asset";
                default: return runtimeFolder + "/WeaponConfig.asset";
            }
        }

        /// <summary>存档索引映射为主武器类型（索引 0..6 与 WeaponKind 枚举值同序）。</summary>
        public static WeaponKind KindFromIndex(int index)
        {
            return (WeaponKind)Mathf.Clamp(index, 0, WeaponKindCount - 1);
        }

        /// <summary>
        /// 从 7 种主武器里不放回抽 <paramref name="count"/> 种互不重复的武器类型；
        /// <paramref name="excludedKind"/> 非空时该类型整局不进池（用于己方队排除玩家武器）。
        /// count 超过池容量时补池继续（防御性，与 INKWAVE pickTeam 的重填逻辑一致）。
        /// </summary>
        public static WeaponKind[] DraftWeaponKinds(int count, WeaponKind? excludedKind, int seed)
        {
            var rng = new System.Random(seed);
            var pool = BuildPool(excludedKind);

            var result = new WeaponKind[Mathf.Max(0, count)];
            for (int slot = 0; slot < result.Length; slot++)
            {
                if (pool.Count == 0)
                    pool = BuildPool(excludedKind);

                int pick = rng.Next(pool.Count);
                result[slot] = pool[pick];
                pool.RemoveAt(pick);
            }

            return result;
        }

        /// <summary>从昵称池不放回抽 <paramref name="count"/> 个互不重复的名字（洗牌消费，同种子同结果）。</summary>
        public static string[] DraftNames(int count, int seed)
        {
            var rng = new System.Random(seed);
            int take = Mathf.Min(Mathf.Max(0, count), BotNames.Length);
            var names = new string[take];
            var pool = new List<string>(BotNames);

            for (int slot = 0; slot < take; slot++)
            {
                if (pool.Count == 0)
                    pool = new List<string>(BotNames);

                int pick = rng.Next(pool.Count);
                names[slot] = pool[pick];
                pool.RemoveAt(pick);
            }

            return names;
        }

        private static List<WeaponKind> BuildPool(WeaponKind? excludedKind)
        {
            var pool = new List<WeaponKind>(WeaponKindCount);
            for (int i = 0; i < WeaponKindCount; i++)
            {
                var kind = (WeaponKind)i;
                if (excludedKind == null || kind != excludedKind.Value)
                    pool.Add(kind);
            }

            return pool;
        }
    }
}
