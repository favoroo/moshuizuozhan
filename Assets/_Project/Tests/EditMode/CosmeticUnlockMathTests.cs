using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 外观解锁判定与装备校验纯逻辑验证（EditMode）：
    /// 1. IsUnlocked 等级 / 成就双门槛与非法输入；
    /// 2. ResolveEquipped 空串 / 合法 / 不存在 / 跨槽位 / 未解锁的回退；
    /// 3. ResolveSet 五槽解析结果均属本槽且已解锁；
    /// 4. SlotProgress / UnlockedCount 与目录真值一致（期望值由目录计算，不硬编码总数）；
    /// 5. TryGetNextUnlock / NextUnlockText 只算等级解锁；
    /// 6. CollectNewlyUnlocked 区间与成就合并、反向区间为空；
    /// 7. UnlockHint 文案；
    /// 8. 装备存档往返（Save / Load / Reset）与 SetEquippedSet 一次性写入。
    /// </summary>
    public class CosmeticUnlockMathTests
    {
        [SetUp]
        [TearDown]
        public void Cleanup()
        {
            PlayerProfile.Reset();
            PlayerProfile.InvalidateCache();
        }

        // ==================== 测试辅助 ====================

        /// <summary>按 id 取一件外观（取不到直接判失败，避免后续断言变成空值报错）。</summary>
        private static CosmeticDef Def(string id)
        {
            Assert.IsTrue(CosmeticCatalog.TryGet(id, out CosmeticDef def), "目录里找不到外观: " + id);
            return def;
        }

        /// <summary>新档：Level 1 + 无成就。</summary>
        private static PlayerProfile NewProfile()
        {
            return new PlayerProfile { Level = 1 };
        }

        /// <summary>目录里等级解锁（非成就）的件数。</summary>
        private static int LevelItemCount()
        {
            int n = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                if (!CosmeticCatalog.All[i].IsAchievementReward) n++;
            }
            return n;
        }

        /// <summary>目录里成就奖励的件数。</summary>
        private static int AchievementItemCount()
        {
            int n = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                if (CosmeticCatalog.All[i].IsAchievementReward) n++;
            }
            return n;
        }

        // ==================== IsUnlocked ====================

        [Test]
        public void IsUnlocked_LevelItem_RequiresLevelAtLeastThreshold()
        {
            CosmeticDef def = Def("outfit_mint"); // 等级 2
            Assert.Greater(def.UnlockLevel, 1, "本用例依赖 outfit_mint 门槛高于 1");

            var profile = NewProfile();
            profile.Level = def.UnlockLevel - 1;
            Assert.IsFalse(CosmeticUnlockMath.IsUnlocked(in def, profile), "等级不足时不应解锁");

            profile.Level = def.UnlockLevel;
            Assert.IsTrue(CosmeticUnlockMath.IsUnlocked(in def, profile), "等级刚好达标时应解锁");

            profile.Level = def.UnlockLevel + 5;
            Assert.IsTrue(CosmeticUnlockMath.IsUnlocked(in def, profile), "等级超过门槛时应解锁");
        }

        [Test]
        public void IsUnlocked_AchievementItem_RequiresAchievementFlag()
        {
            CosmeticDef def = Def("title_untouched");
            Assert.IsTrue(def.IsAchievementReward, "本用例依赖 title_untouched 是成就奖励");

            var profile = NewProfile();
            profile.Level = 999; // 等级再高也不该解锁成就件
            Assert.IsFalse(CosmeticUnlockMath.IsUnlocked(in def, profile), "未达成成就时等级不能顶替成就门槛");

            Assert.IsTrue(profile.MarkAchievementUnlocked(def.UnlockAchievementId));
            Assert.IsTrue(CosmeticUnlockMath.IsUnlocked(in def, profile), "达成成就后应解锁");
        }

        [Test]
        public void IsUnlocked_NullProfileOrEmptyId_IsFalse()
        {
            CosmeticDef def = Def("outfit_cream");
            Assert.IsFalse(CosmeticUnlockMath.IsUnlocked(in def, null), "空档案时任何外观都不算解锁");

            var profile = NewProfile();
            CosmeticDef empty = default;
            Assert.IsFalse(CosmeticUnlockMath.IsUnlocked(in empty, profile), "id 为空的结构体不应判定为已解锁");
        }

        // ==================== ResolveEquipped ====================

        [Test]
        public void ResolveEquipped_EmptyId_FallsBackToSlotDefault()
        {
            var profile = NewProfile();
            string resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit, string.Empty, profile);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), resolved, "空 id 应回退槽位默认件");

            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Hair),
                CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Hair, null, profile), "null id 应回退槽位默认件");
        }

        [Test]
        public void ResolveEquipped_ValidUnlockedId_ReturnsItVerbatim()
        {
            var profile = NewProfile();
            profile.Level = 5; // outfit_mint 需要 2 级

            string resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit, "outfit_mint", profile);
            Assert.AreEqual("outfit_mint", resolved, "合法且已解锁的 id 应原样返回");
        }

        [Test]
        public void ResolveEquipped_UnknownId_FallsBackToSlotDefault()
        {
            var profile = NewProfile();
            profile.Level = 999;

            string resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit, "outfit_does_not_exist", profile);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), resolved, "不存在的 id 应回退槽位默认件");
        }

        [Test]
        public void ResolveEquipped_CrossSlotId_FallsBackToSlotDefault()
        {
            var profile = NewProfile();
            profile.Level = 999; // 发型本身已解锁，但槽位不对

            string resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit, "hair_bob", profile);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), resolved,
                "把发型 id 塞进服装槽必须回退服装默认件");
        }

        [Test]
        public void ResolveEquipped_LockedHighLevelId_FallsBackToSlotDefault()
        {
            var profile = NewProfile();
            profile.Level = 1; // outfit_sakura 需要 16 级

            string resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit, "outfit_sakura", profile);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), resolved, "未解锁的高等级件应回退默认件");
        }

        // ==================== ResolveSet ====================

        [Test]
        public void ResolveSet_NewProfile_ReturnsUnlockedDefaultPerSlot()
        {
            var profile = NewProfile();
            CosmeticSet set = CosmeticUnlockMath.ResolveSet(profile);

            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                CosmeticDef def = set.Get(slot);

                Assert.IsFalse(string.IsNullOrEmpty(def.Id), "解析出的套装缺件: " + slot);
                Assert.AreEqual(slot, def.Slot, "解析出的件不属于该槽位: " + def.Id);
                Assert.IsTrue(CosmeticUnlockMath.IsUnlocked(in def, profile), "解析出的件必须是已解锁的: " + def.Id);
            }

            // 新档没有自定义装备时，五个槽位都应是默认件
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), set.Outfit.Id);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Hair), set.Hair.Id);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Headgear), set.Headgear.Id);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.WeaponSkin), set.WeaponSkin.Id);
            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Title), set.Title.Id);

            Assert.IsFalse(string.IsNullOrEmpty(set.Signature), "套装签名不应为空");
            Assert.IsTrue(set.Signature.Contains(set.Outfit.Id), "套装签名应包含服装 id");
        }

        [Test]
        public void ResolveSet_EquippedIds_AreHonoredWhenUnlocked()
        {
            var profile = NewProfile();
            profile.Level = 20;
            profile.SetEquippedId(CosmeticSlot.Outfit, "outfit_neon");
            profile.SetEquippedId(CosmeticSlot.Hair, "hair_bob");
            profile.SetEquippedId(CosmeticSlot.Headgear, "head_cap");
            profile.SetEquippedId(CosmeticSlot.WeaponSkin, "gun_cherry");
            profile.SetEquippedId(CosmeticSlot.Title, "title_slinger");

            CosmeticSet set = CosmeticUnlockMath.ResolveSet(profile);
            Assert.AreEqual("outfit_neon", set.Outfit.Id);
            Assert.AreEqual("hair_bob", set.Hair.Id);
            Assert.AreEqual("head_cap", set.Headgear.Id);
            Assert.AreEqual("gun_cherry", set.WeaponSkin.Id);
            Assert.AreEqual("title_slinger", set.Title.Id);
        }

        // ==================== SlotProgress / UnlockedCount ====================

        [Test]
        public void SlotProgress_NewProfile_CountsOnlyFreeDefaults()
        {
            var profile = NewProfile();

            int expectedTotal = 0;
            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                CosmeticDef[] list = CosmeticCatalog.ForSlot(slot);

                int expectedUnlocked = 0;
                for (int i = 0; i < list.Length; i++)
                {
                    if (!list[i].IsAchievementReward && list[i].UnlockLevel <= profile.Level) expectedUnlocked++;
                }

                CosmeticUnlockMath.SlotProgress(slot, profile, out int unlocked, out int total);
                Assert.AreEqual(list.Length, total, "槽位总数应等于 ForSlot 长度: " + slot);
                Assert.AreEqual(expectedUnlocked, unlocked, "槽位已解锁数不符: " + slot);
                expectedTotal += expectedUnlocked;
            }

            Assert.AreEqual(expectedTotal, CosmeticUnlockMath.UnlockedCount(profile), "新档总解锁数应等于各槽免费件之和");
        }

        [Test]
        public void UnlockedCount_HighLevel_EqualsLevelItemsPlusAchievedItems()
        {
            var profile = NewProfile();
            profile.Level = 100; // 高于目录里最高等级门槛（32）

            int expected = LevelItemCount(); // 已达成成就数 = 0
            Assert.AreEqual(expected, CosmeticUnlockMath.UnlockedCount(profile),
                "满级无成就时应等于全部等级解锁件数");

            // 解锁两个成就，各绑一件外观（ach_untouched -> title_untouched，ach_kill_30 -> gun_gold）
            profile.MarkAchievementUnlocked("ach_untouched");
            profile.MarkAchievementUnlocked("ach_kill_30");

            // 期望值仍由目录推导：等级件 + 这两个成就实际绑定的件数（不硬编码）
            int achievementBound = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (c.IsAchievementReward
                    && (c.UnlockAchievementId == "ach_untouched" || c.UnlockAchievementId == "ach_kill_30"))
                {
                    achievementBound++;
                }
            }

            expected = LevelItemCount() + achievementBound;
            Assert.AreEqual(expected, CosmeticUnlockMath.UnlockedCount(profile),
                "满级 + 已达成成就应等于等级解锁件数 + 这些成就绑定的件数");
        }

        // ==================== TryGetNextUnlock ====================

        [Test]
        public void TryGetNextUnlock_NewProfile_ReturnsLowestLockedLevelItem()
        {
            var profile = NewProfile();

            Assert.IsTrue(CosmeticUnlockMath.TryGetNextUnlock(profile, out CosmeticDef def), "新档必然还有未解锁件");
            Assert.IsFalse(def.IsAchievementReward, "下一级解锁不应返回成就奖励");

            // 期望值：全表里等级解锁且 UnlockLevel > 当前等级的最小门槛
            int expectedLevel = int.MaxValue;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (c.IsAchievementReward) continue;
                if (c.UnlockLevel > profile.Level && c.UnlockLevel < expectedLevel) expectedLevel = c.UnlockLevel;
            }

            Assert.AreNotEqual(int.MaxValue, expectedLevel, "目录里应存在高于 1 级的等级解锁件");
            Assert.AreEqual(expectedLevel, def.UnlockLevel, "应返回最低的未解锁等级门槛");
            Assert.IsTrue(CosmeticUnlockMath.IsUnlocked(in def, new PlayerProfile { Level = expectedLevel }),
                "返回件在达到该等级后必须解锁");
        }

        [Test]
        public void TryGetNextUnlock_MaxLevel_ReturnsFalse()
        {
            var profile = NewProfile();
            profile.Level = 999;
            Assert.IsFalse(CosmeticUnlockMath.TryGetNextUnlock(profile, out _), "满级后不应再有等级解锁件");
        }

        [Test]
        public void TryGetNextUnlock_NullProfile_ReturnsFalse()
        {
            Assert.IsFalse(CosmeticUnlockMath.TryGetNextUnlock(null, out _), "空档案应安全返回 false");
        }

        // ==================== NextUnlockText ====================

        [Test]
        public void NextUnlockText_NewProfile_PrefixedWithNextUnlock()
        {
            var profile = NewProfile();
            string text = CosmeticUnlockMath.NextUnlockText(profile);
            Assert.IsFalse(string.IsNullOrEmpty(text), "新档文案不应为空");
            Assert.IsTrue(text.StartsWith("下一级解锁："), "新档文案应以「下一级解锁：」开头，实际: " + text);

            Assert.IsTrue(CosmeticUnlockMath.TryGetNextUnlock(profile, out CosmeticDef def));
            Assert.IsTrue(text.Contains(def.DisplayName), "文案应包含下一件外观的名称，实际: " + text);
        }

        [Test]
        public void NextUnlockText_AllUnlocked_ReturnsClosingLine()
        {
            var profile = NewProfile();
            profile.Level = 999;
            Assert.AreEqual("外观已全部解锁", CosmeticUnlockMath.NextUnlockText(profile), "全解锁文案不符");
        }

        // ==================== CollectNewlyUnlocked ====================

        [Test]
        public void CollectNewlyUnlocked_LevelRange_ReturnsOnlyItemsInsideInterval()
        {
            const int oldLevel = 1;
            const int newLevel = 2;
            List<CosmeticDef> list = CosmeticUnlockMath.CollectNewlyUnlocked(oldLevel, newLevel, null);

            var expected = new List<string>();
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (!c.IsAchievementReward && c.UnlockLevel > oldLevel && c.UnlockLevel <= newLevel) expected.Add(c.Id);
            }

            Assert.IsTrue(expected.Count > 0, "1→2 级区间应至少解锁一件");
            CollectionAssert.AreEquivalent(expected, Ids(list), "返回集合应与区间内的等级解锁件一致");

            for (int i = 0; i < list.Count; i++)
            {
                Assert.IsFalse(list[i].IsAchievementReward, "等级区间不应返回成就奖励: " + list[i].Id);
                Assert.Greater(list[i].UnlockLevel, oldLevel, "返回件门槛应大于旧等级: " + list[i].Id);
                Assert.LessOrEqual(list[i].UnlockLevel, newLevel, "返回件门槛应不大于新等级: " + list[i].Id);
            }
        }

        [Test]
        public void CollectNewlyUnlocked_AchievementOnly_ReturnsBoundItems()
        {
            Assert.Greater(AchievementItemCount(), 0, "目录里应至少有一件成就奖励");

            List<CosmeticDef> list = CosmeticUnlockMath.CollectNewlyUnlocked(1, 1, new[] { "ach_kill_30" });

            var expected = new List<string>();
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                if (c.IsAchievementReward && c.UnlockAchievementId == "ach_kill_30") expected.Add(c.Id);
            }

            Assert.IsTrue(expected.Count > 0, "ach_kill_30 应绑定至少一件外观");
            CollectionAssert.AreEquivalent(expected, Ids(list), "只应返回该成就绑定的外观");
            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual("ach_kill_30", list[i].UnlockAchievementId, "不应混入其它成就的奖励: " + list[i].Id);
            }
        }

        [Test]
        public void CollectNewlyUnlocked_MixedRange_NoDuplicateAndNoFreeItems()
        {
            const int oldLevel = 1;
            const int newLevel = 10;
            List<CosmeticDef> list = CosmeticUnlockMath.CollectNewlyUnlocked(oldLevel, newLevel, new[] { "ach_kill_30" });

            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                Assert.IsTrue(seen.Add(list[i].Id), "同一件外观不应重复返回: " + list[i].Id);

                if (list[i].IsAchievementReward)
                {
                    Assert.AreEqual("ach_kill_30", list[i].UnlockAchievementId, "成就奖励只应来自传入的成就: " + list[i].Id);
                }
                else
                {
                    Assert.Greater(list[i].UnlockLevel, oldLevel, "免费件不应出现在新解锁里: " + list[i].Id);
                    Assert.LessOrEqual(list[i].UnlockLevel, newLevel, "超出新区间的件不应返回: " + list[i].Id);
                }
            }

            int expectedCount = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef c = CosmeticCatalog.All[i];
                bool levelHit = !c.IsAchievementReward && c.UnlockLevel > oldLevel && c.UnlockLevel <= newLevel;
                bool achHit = c.IsAchievementReward && c.UnlockAchievementId == "ach_kill_30";
                if (levelHit || achHit) expectedCount++;
            }

            Assert.AreEqual(expectedCount, list.Count, "合并区间与成就后的件数不符");
        }

        [Test]
        public void CollectNewlyUnlocked_ReverseRange_ReturnsEmpty()
        {
            List<CosmeticDef> list = CosmeticUnlockMath.CollectNewlyUnlocked(5, 3, null);
            Assert.AreEqual(0, list.Count, "反向区间（降级）不应返回任何新解锁");
        }

        // ==================== UnlockHint ====================

        [Test]
        public void UnlockHint_LevelItem_ContainsLevelAndNumber()
        {
            CosmeticDef def = Def("outfit_mint");
            string hint = CosmeticUnlockMath.UnlockHint(in def);

            Assert.IsTrue(hint.Contains("等级"), "等级件提示应含「等级」，实际: " + hint);
            Assert.IsTrue(hint.Contains(def.UnlockLevel.ToString()), "等级件提示应含门槛数字，实际: " + hint);
        }

        [Test]
        public void UnlockHint_AchievementItem_ContainsAchievementName()
        {
            CosmeticDef def = Def("title_untouched");
            Assert.IsTrue(AchievementCatalog.TryGet(def.UnlockAchievementId, out AchievementDef ach));

            string hint = CosmeticUnlockMath.UnlockHint(in def);
            Assert.IsTrue(hint.Contains("成就："), "成就件提示应以「成就：」引出，实际: " + hint);
            Assert.IsTrue(hint.Contains(ach.Name), "成就件提示应含成就名「" + ach.Name + "」，实际: " + hint);
        }

        // ==================== 装备存档往返 ====================

        [Test]
        public void EquippedId_SaveAndLoad_Roundtrips()
        {
            PlayerProfile profile = PlayerProfile.Current;
            profile.SetEquippedId(CosmeticSlot.Outfit, "outfit_mint");
            profile.SetEquippedId(CosmeticSlot.Hair, "hair_bob");
            profile.SetEquippedId(CosmeticSlot.Headgear, "head_cap");
            profile.SetEquippedId(CosmeticSlot.WeaponSkin, "gun_cherry");
            profile.SetEquippedId(CosmeticSlot.Title, "title_slinger");
            profile.Save();

            PlayerProfile.InvalidateCache();
            PlayerProfile loaded = PlayerProfile.Load();

            Assert.AreEqual("outfit_mint", loaded.GetEquippedId(CosmeticSlot.Outfit), "服装装备 id 未往返");
            Assert.AreEqual("hair_bob", loaded.GetEquippedId(CosmeticSlot.Hair), "发型装备 id 未往返");
            Assert.AreEqual("head_cap", loaded.GetEquippedId(CosmeticSlot.Headgear), "头饰装备 id 未往返");
            Assert.AreEqual("gun_cherry", loaded.GetEquippedId(CosmeticSlot.WeaponSkin), "涂装装备 id 未往返");
            Assert.AreEqual("title_slinger", loaded.GetEquippedId(CosmeticSlot.Title), "称号装备 id 未往返");
        }

        [Test]
        public void EquippedId_Reset_ClearsFieldsAndPrefsKeys()
        {
            PlayerProfile profile = PlayerProfile.Current;
            profile.SetEquippedId(CosmeticSlot.Outfit, "outfit_mint");
            profile.SetEquippedId(CosmeticSlot.Hair, "hair_bob");
            profile.SetEquippedId(CosmeticSlot.Headgear, "head_cap");
            profile.SetEquippedId(CosmeticSlot.WeaponSkin, "gun_cherry");
            profile.SetEquippedId(CosmeticSlot.Title, "title_slinger");
            profile.Save();
            Assert.IsTrue(PlayerPrefs.HasKey(PlayerProfile.KeyEquipOutfit), "保存后应写入装备键");

            PlayerProfile.Reset();

            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyEquipOutfit), "Reset 后服装键应被删除");
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyEquipHair), "Reset 后发型键应被删除");
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyEquipHeadgear), "Reset 后头饰键应被删除");
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyEquipWeaponSkin), "Reset 后涂装键应被删除");
            Assert.IsFalse(PlayerPrefs.HasKey(PlayerProfile.KeyEquipTitle), "Reset 后称号键应被删除");

            PlayerProfile reset = PlayerProfile.Current;
            Assert.AreEqual(string.Empty, reset.GetEquippedId(CosmeticSlot.Outfit), "Reset 后服装装备应为空串");
            Assert.AreEqual(string.Empty, reset.GetEquippedId(CosmeticSlot.Hair), "Reset 后发型装备应为空串");
            Assert.AreEqual(string.Empty, reset.GetEquippedId(CosmeticSlot.Headgear), "Reset 后头饰装备应为空串");
            Assert.AreEqual(string.Empty, reset.GetEquippedId(CosmeticSlot.WeaponSkin), "Reset 后涂装装备应为空串");
            Assert.AreEqual(string.Empty, reset.GetEquippedId(CosmeticSlot.Title), "Reset 后称号装备应为空串");
        }

        [Test]
        public void EquippedId_GarbageSavedId_ResolvesToDefaultWithoutThrowing()
        {
            PlayerProfile profile = PlayerProfile.Current;
            profile.Level = 999;
            profile.SetEquippedId(CosmeticSlot.Outfit, "garbage_%%_id");
            profile.Save();

            PlayerProfile.InvalidateCache();
            PlayerProfile loaded = PlayerProfile.Load();

            string resolved = string.Empty;
            Assert.DoesNotThrow(() =>
            {
                resolved = CosmeticUnlockMath.ResolveEquipped(CosmeticSlot.Outfit,
                    loaded.GetEquippedId(CosmeticSlot.Outfit), loaded);
            }, "垃圾 id 不应让装备解析抛异常");

            Assert.AreEqual(CosmeticCatalog.DefaultId(CosmeticSlot.Outfit), resolved, "垃圾 id 应回退槽位默认件");
        }

        [Test]
        public void SetEquippedSet_WritesAllFiveSlotsAndPersists()
        {
            var set = new CosmeticSet(
                Def("outfit_neon"),
                Def("hair_bob"),
                Def("head_cap"),
                Def("gun_cherry"),
                Def("title_slinger"));

            PlayerProfile profile = PlayerProfile.Current;
            profile.Level = 999;
            profile.SetEquippedSet(set);

            Assert.AreEqual("outfit_neon", profile.GetEquippedId(CosmeticSlot.Outfit), "服装槽未写入");
            Assert.AreEqual("hair_bob", profile.GetEquippedId(CosmeticSlot.Hair), "发型槽未写入");
            Assert.AreEqual("head_cap", profile.GetEquippedId(CosmeticSlot.Headgear), "头饰槽未写入");
            Assert.AreEqual("gun_cherry", profile.GetEquippedId(CosmeticSlot.WeaponSkin), "涂装槽未写入");
            Assert.AreEqual("title_slinger", profile.GetEquippedId(CosmeticSlot.Title), "称号槽未写入");

            PlayerProfile.InvalidateCache();
            PlayerProfile loaded = PlayerProfile.Load();
            Assert.AreEqual(set.Signature, CosmeticUnlockMath.ResolveSet(loaded).Signature,
                "SetEquippedSet 应一次性落盘，重载后解析出的整套外观一致");
        }

        /// <summary>把外观列表摊平成 id 列表，供 CollectionAssert 使用。</summary>
        private static List<string> Ids(List<CosmeticDef> list)
        {
            var ids = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++) ids.Add(list[i].Id);
            return ids;
        }
    }
}
