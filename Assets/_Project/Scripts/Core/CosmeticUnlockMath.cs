using System.Collections.Generic;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 当前装备的一整套外观（每槽一件，已做过「是否解锁」校验）。
    /// 由 <see cref="CosmeticUnlockMath.ResolveSet"/> 从档案解析，供角色应用器与衣柜预览共用。
    /// </summary>
    public readonly struct CosmeticSet
    {
        public readonly CosmeticDef Outfit;
        public readonly CosmeticDef Hair;
        public readonly CosmeticDef Headgear;
        public readonly CosmeticDef WeaponSkin;
        public readonly CosmeticDef Title;

        public CosmeticSet(CosmeticDef outfit, CosmeticDef hair, CosmeticDef headgear, CosmeticDef weaponSkin,
            CosmeticDef title)
        {
            Outfit = outfit;
            Hair = hair;
            Headgear = headgear;
            WeaponSkin = weaponSkin;
            Title = title;
        }

        public CosmeticDef Get(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Outfit: return Outfit;
                case CosmeticSlot.Hair: return Hair;
                case CosmeticSlot.Headgear: return Headgear;
                case CosmeticSlot.WeaponSkin: return WeaponSkin;
                case CosmeticSlot.Title: return Title;
                default: return default;
            }
        }

        /// <summary>变化检测用签名（变装后重新应用 / 重建预览）。</summary>
        public string Signature =>
            (Outfit.Id ?? "-") + "|" + (Hair.Id ?? "-") + "|" + (Headgear.Id ?? "-") + "|" +
            (WeaponSkin.Id ?? "-") + "|" + (Title.Id ?? "-");
    }

    /// <summary>
    /// 解锁判定与装备校验（纯函数，不触 PlayerPrefs、不碰场景 —— EditMode 直接断言）。
    ///
    /// 关键设计：<b>不持久化"已解锁集合"</b>。解锁状态完全由「等级 + 已达成成就」推导，
    /// 因此老存档天然兼容，也不会出现"解锁记录丢了导致外观消失"的坏档。
    /// </summary>
    public static class CosmeticUnlockMath
    {
        /// <summary>该件是否已解锁。成就解锁优先于等级门槛。</summary>
        public static bool IsUnlocked(in CosmeticDef def, PlayerProfile profile)
        {
            if (string.IsNullOrEmpty(def.Id)) return false;
            if (profile == null) return false;

            if (def.IsAchievementReward)
            {
                return profile.IsAchievementUnlocked(def.UnlockAchievementId);
            }
            return profile.Level >= Mathf.Max(0, def.UnlockLevel);
        }

        /// <summary>某槽位的 (已解锁 / 总数)，衣柜页签角标用。</summary>
        public static void SlotProgress(CosmeticSlot slot, PlayerProfile profile, out int unlocked, out int total)
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(slot);
            total = list.Length;
            unlocked = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (IsUnlocked(list[i], profile)) unlocked++;
            }
        }

        public static int UnlockedCount(PlayerProfile profile)
        {
            int n = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                if (IsUnlocked(CosmeticCatalog.All[i], profile)) n++;
            }
            return n;
        }

        /// <summary>
        /// 解析某槽位该实际使用的 id：存档 id 合法且已解锁就用它，否则回退槽位默认件。
        /// 存档被改坏 / 擦了成就 / 降级都不会让角色"裸奔"。
        /// </summary>
        public static string ResolveEquipped(CosmeticSlot slot, string savedId, PlayerProfile profile)
        {
            if (!string.IsNullOrEmpty(savedId)
                && CosmeticCatalog.TryGet(savedId, out CosmeticDef def)
                && def.Slot == slot
                && IsUnlocked(def, profile))
            {
                return def.Id;
            }
            return CosmeticCatalog.DefaultId(slot);
        }

        /// <summary>解析整套外观（每个槽位各一次校验）。</summary>
        public static CosmeticSet ResolveSet(PlayerProfile profile)
        {
            if (profile == null) profile = PlayerProfile.Current;
            return new CosmeticSet(
                ResolveDef(CosmeticSlot.Outfit, profile),
                ResolveDef(CosmeticSlot.Hair, profile),
                ResolveDef(CosmeticSlot.Headgear, profile),
                ResolveDef(CosmeticSlot.WeaponSkin, profile),
                ResolveDef(CosmeticSlot.Title, profile));
        }

        private static CosmeticDef ResolveDef(CosmeticSlot slot, PlayerProfile profile)
        {
            string id = ResolveEquipped(slot, profile.GetEquippedId(slot), profile);
            return CosmeticCatalog.TryGet(id, out CosmeticDef def) ? def : default;
        }

        /// <summary>
        /// 下一个等级解锁的东西（首页「下一级解锁：XXX」的钩子）。只算等级解锁，成就解锁不参与。
        /// 全部解锁完返回 false。
        /// </summary>
        public static bool TryGetNextUnlock(PlayerProfile profile, out CosmeticDef def)
        {
            def = default;
            if (profile == null) return false;

            int level = profile.Level;
            int bestLevel = int.MaxValue;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (c.IsAchievementReward) continue;
                if (c.UnlockLevel <= level) continue;
                if (c.UnlockLevel < bestLevel || (c.UnlockLevel == bestLevel && string.CompareOrdinal(c.Id, def.Id) < 0))
                {
                    bestLevel = c.UnlockLevel;
                    def = c;
                }
            }
            return bestLevel != int.MaxValue;
        }

        /// <summary>首页 tile 文案：「下一级解锁：薄荷汽水」，全解锁后给一句收尾。</summary>
        public static string NextUnlockText(PlayerProfile profile)
        {
            return TryGetNextUnlock(profile, out CosmeticDef def)
                ? "下一级解锁：" + def.DisplayName
                : "外观已全部解锁";
        }

        /// <summary>
        /// 本次结算新解锁的外观：等级跨过的 + 本局新达成成就绑定的。
        /// <paramref name="newAchievementIds"/> 传结算里的「本局新解锁成就」，可为 null。
        /// </summary>
        public static List<CosmeticDef> CollectNewlyUnlocked(int oldLevel, int newLevel,
            IReadOnlyCollection<string> newAchievementIds)
        {
            var result = new List<CosmeticDef>(4);
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (c.IsAchievementReward)
                {
                    if (newAchievementIds != null && ContainsId(newAchievementIds, c.UnlockAchievementId))
                    {
                        result.Add(c);
                    }
                }
                else if (c.UnlockLevel > oldLevel && c.UnlockLevel <= newLevel)
                {
                    result.Add(c);
                }
            }
            return result;
        }

        /// <summary>某件解锁条件的一行说明（衣柜未解锁卡片用）。</summary>
        public static string UnlockHint(in CosmeticDef def)
        {
            if (def.IsAchievementReward)
            {
                return AchievementCatalog.TryGet(def.UnlockAchievementId, out AchievementDef ach)
                    ? "成就：" + ach.Name
                    : "成就解锁";
            }
            return def.UnlockLevel <= 0 ? "默认拥有" : "等级 " + def.UnlockLevel + " 解锁";
        }

        /// <summary>
        /// <see cref="IReadOnlyCollection{T}"/> 没有 <c>Contains</c>（那是 ICollection/List 的成员），
        /// 这里手写一遍：不引 LINQ，避免结算路径多一次枚举器分配。
        /// </summary>
        private static bool ContainsId(IReadOnlyCollection<string> ids, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (string s in ids)
            {
                if (string.Equals(s, id, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
