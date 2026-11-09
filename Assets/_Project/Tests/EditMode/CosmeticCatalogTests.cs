using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;

namespace P1.Tests
{
    /// <summary>
    /// 外观目录静态数据完整性验证（EditMode）：
    /// 1. 目录非空、id 唯一、名称 / 副标题 / 图标键齐备；
    /// 2. 槽位划分（Slot 合法、ForSlot 严格分区、各槽件数之和 == Count）；
    /// 3. 每槽默认件（免费、可查、幂等）；
    /// 4. 各槽载荷有效性（服装四色不透明 / 发型 6 束 7 点 / 头饰形态 / 涂装双色 / 称号文案与阶）；
    /// 5. 解锁门槛自洽（成就 id 必须存在于 AchievementCatalog、等级非负、同槽等级不重复）；
    /// 6. ResetCachesForTests 缓存重建幂等。
    /// </summary>
    public class CosmeticCatalogTests
    {
        // ==================== 目录完整性 ====================

        [Test]
        public void Catalog_All_HasEntriesWithUniqueIdsAndCompleteCopy()
        {
            Assert.IsNotNull(CosmeticCatalog.All, "目录数组不应为 null");
            Assert.IsTrue(CosmeticCatalog.All.Length > 0, "目录不能为空");
            Assert.AreEqual(CosmeticCatalog.All.Length, CosmeticCatalog.Count, "Count 应等于目录长度");

            var seen = new HashSet<string>();
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef def = CosmeticCatalog.All[i];
                Assert.IsFalse(string.IsNullOrEmpty(def.Id), "第 " + i + " 件外观缺 id");
                Assert.IsTrue(seen.Add(def.Id), "重复外观 id: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.DisplayName), "外观缺名称: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.Subtitle), "外观缺副标题: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.IconName), "外观缺图标键: " + def.Id);
            }
        }

        // ==================== 槽位合法性 ====================

        [Test]
        public void Catalog_Slots_PartitionEntriesAndCoverAllEnumValues()
        {
            Assert.IsNotNull(CosmeticCatalog.Slots, "槽位表不应为 null");
            Assert.AreEqual(System.Enum.GetValues(typeof(CosmeticSlot)).Length, CosmeticCatalog.Slots.Length,
                "Slots 应覆盖全部槽位枚举值");

            var slotSet = new HashSet<CosmeticSlot>(CosmeticCatalog.Slots);
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                Assert.IsTrue(slotSet.Contains(CosmeticCatalog.All[i].Slot),
                    "外观槽位不在 Slots 里: " + CosmeticCatalog.All[i].Id);
            }

            int sum = 0;
            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                CosmeticDef[] list = CosmeticCatalog.ForSlot(slot);
                Assert.IsNotNull(list, "ForSlot 不应返回 null: " + slot);
                Assert.IsTrue(list.Length > 0, "槽位没有任何条目: " + slot);

                for (int i = 0; i < list.Length; i++)
                {
                    Assert.AreEqual(slot, list[i].Slot, "ForSlot 混入了其它槽位的条目: " + list[i].Id);
                }

                Assert.IsFalse(string.IsNullOrEmpty(CosmeticCatalog.SlotLabel(slot)), "槽位缺中文名: " + slot);
                Assert.IsFalse(string.IsNullOrEmpty(CosmeticCatalog.SlotIcon(slot)), "槽位缺图标键: " + slot);
                sum += list.Length;
            }

            Assert.AreEqual(CosmeticCatalog.Count, sum, "各槽件数之和应等于目录总数");
        }

        // ==================== 默认件 ====================

        [Test]
        public void Catalog_DefaultId_IsFreeResolvableAndIdempotent()
        {
            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                string id = CosmeticCatalog.DefaultId(slot);

                Assert.IsFalse(string.IsNullOrEmpty(id), "槽位缺默认件 id: " + slot);
                Assert.IsTrue(CosmeticCatalog.TryGet(id, out CosmeticDef def), "默认件不在目录里: " + id);
                Assert.AreEqual(slot, def.Slot, "默认件槽位不符: " + id);
                Assert.LessOrEqual(def.UnlockLevel, 0, "默认件必须开局即有（UnlockLevel <= 0）: " + id);
                Assert.IsFalse(def.IsAchievementReward, "默认件不能是成就奖励: " + id);

                Assert.AreEqual(id, CosmeticCatalog.DefaultId(slot), "DefaultId 应幂等: " + slot);
            }
        }

        // ==================== 载荷有效 ====================

        [Test]
        public void Catalog_OutfitPayload_HasOpaqueFourColors()
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(CosmeticSlot.Outfit);
            for (int i = 0; i < list.Length; i++)
            {
                CosmeticDef def = list[i];
                Assert.Greater(def.Outfit.Shirt.a, 0f, "上衣颜色忘了填 alpha: " + def.Id);
                Assert.Greater(def.Outfit.Sleeve.a, 0f, "短袖颜色忘了填 alpha: " + def.Id);
                Assert.Greater(def.Outfit.Shorts.a, 0f, "短裤颜色忘了填 alpha: " + def.Id);
                Assert.Greater(def.Outfit.Shoe.a, 0f, "鞋面颜色忘了填 alpha: " + def.Id);
            }
        }

        [Test]
        public void Catalog_HairPayload_HasSixStrandsAndSevenPointSegments()
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(CosmeticSlot.Hair);
            for (int i = 0; i < list.Length; i++)
            {
                CosmeticDef def = list[i];
                Assert.AreEqual(6, def.Hair.StrandDirs.Length, "发型必须 6 束采样方向: " + def.Id);
                Assert.AreEqual(6, def.Hair.RadiusScale.Length, "发型必须 6 个粗细倍率: " + def.Id);
                Assert.AreEqual(6, def.Hair.LengthScale.Length, "发型必须 6 个长度倍率: " + def.Id);

                for (int k = 0; k < 6; k++)
                {
                    Assert.Greater(def.Hair.StrandDirs[k].sqrMagnitude, 0f, "发束采样方向不能是零向量: " + def.Id);
                }

                Assert.AreEqual(7, def.Hair.Root.Points.Length, "根段样条必须 7 点: " + def.Id);
                Assert.AreEqual(7, def.Hair.Tip.Points.Length, "梢段样条必须 7 点: " + def.Id);
                Assert.Greater(def.Hair.Root.BaseRadius, 0f, "根段半径必须为正: " + def.Id);
                Assert.Greater(def.Hair.Root.TipRadius, 0f, "根段梢半径必须为正: " + def.Id);
                Assert.Greater(def.Hair.Tip.BaseRadius, 0f, "梢段半径必须为正: " + def.Id);
                Assert.Greater(def.Hair.Tip.TipRadius, 0f, "梢段梢半径必须为正: " + def.Id);
                Assert.Less(def.Hair.MidPivotPosition.y, 0f, "梢段枢轴应在头皮下方（-Y 为下垂方向）: " + def.Id);
            }
        }

        [Test]
        public void Catalog_HeadgearPayload_HasKindExceptNoneEntry()
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(CosmeticSlot.Headgear);
            for (int i = 0; i < list.Length; i++)
            {
                CosmeticDef def = list[i];
                if (def.Headgear.Kind == HeadgearKind.None)
                {
                    // 「不戴」那件允许 Main / Accent 为 default（不渲染任何网格）
                    continue;
                }

                Assert.IsTrue(def.Headgear.Kind != HeadgearKind.None, "头饰形态忘了填: " + def.Id);
                Assert.Greater(def.Headgear.Main.a, 0f, "头饰主体颜色忘了填 alpha: " + def.Id);
                Assert.Greater(def.Headgear.Accent.a, 0f, "头饰点缀颜色忘了填 alpha: " + def.Id);
            }
        }

        [Test]
        public void Catalog_WeaponSkinPayload_HasOpaqueTwoColors()
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(CosmeticSlot.WeaponSkin);
            for (int i = 0; i < list.Length; i++)
            {
                CosmeticDef def = list[i];
                Assert.Greater(def.WeaponSkin.Body.a, 0f, "枪身颜色忘了填 alpha: " + def.Id);
                Assert.Greater(def.WeaponSkin.Metal.a, 0f, "金属件颜色忘了填 alpha: " + def.Id);
            }
        }

        [Test]
        public void Catalog_TitlePayload_HasTextAndTierInRange()
        {
            CosmeticDef[] list = CosmeticCatalog.ForSlot(CosmeticSlot.Title);
            for (int i = 0; i < list.Length; i++)
            {
                CosmeticDef def = list[i];
                Assert.IsFalse(string.IsNullOrEmpty(def.Title.Text), "称号缺文案: " + def.Id);
                Assert.GreaterOrEqual(def.Title.Tier, 0, "称号色阶不能为负: " + def.Id);
                Assert.LessOrEqual(def.Title.Tier, 5, "称号色阶不能超过 5（0..4 段位 + 5 成就金）: " + def.Id);
            }
        }

        // ==================== 解锁门槛自洽 ====================

        [Test]
        public void Catalog_AchievementRewards_ReferenceExistingAchievements()
        {
            int rewardCount = 0;
            for (int i = 0; i < CosmeticCatalog.All.Length; i++)
            {
                CosmeticDef def = CosmeticCatalog.All[i];
                if (!def.IsAchievementReward)
                {
                    Assert.GreaterOrEqual(def.UnlockLevel, 0, "等级解锁件的门槛不能为负: " + def.Id);
                    continue;
                }

                rewardCount++;
                Assert.IsTrue(AchievementCatalog.TryGet(def.UnlockAchievementId, out _),
                    "成就奖励绑定了不存在的成就 id，门槛永远打不开: " + def.Id + " -> " + def.UnlockAchievementId);
            }

            Assert.Greater(rewardCount, 0, "目录里应至少有一件成就奖励");
        }

        [Test]
        public void Catalog_LevelUnlocks_AreUniqueWithinSlotAndNonNegative()
        {
            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                CosmeticDef[] list = CosmeticCatalog.ForSlot(slot);
                var usedLevels = new Dictionary<int, string>();

                for (int i = 0; i < list.Length; i++)
                {
                    CosmeticDef def = list[i];
                    if (def.IsAchievementReward) continue; // 成就奖励不参与等级唯一性

                    Assert.GreaterOrEqual(def.UnlockLevel, 0, "等级解锁件的门槛不能为负: " + def.Id);

                    // 注意：断言消息里的字典取值必须放在 IsFalse 之前判空，否则断言通过时也会抛 KeyNotFound。
                    string clash = usedLevels.TryGetValue(def.UnlockLevel, out string other) ? other : null;
                    Assert.IsNull(clash,
                        "槽位 " + slot + " 的等级 " + def.UnlockLevel + " 被重复占用: " + clash + " / " + def.Id);
                    usedLevels[def.UnlockLevel] = def.Id;
                }
            }
        }

        // ==================== 缓存重建 ====================

        [Test]
        public void Catalog_ResetCachesForTests_RebuildsIdenticalResults()
        {
            // 先热身一次缓存
            string[] before = Snapshot();

            CosmeticCatalog.ResetCachesForTests();
            CollectionAssert.AreEqual(before, Snapshot(), "清缓存后 ForSlot / DefaultId 结果应完全一致");

            CosmeticCatalog.ResetCachesForTests();
            CollectionAssert.AreEqual(before, Snapshot(), "重复清缓存应保持幂等");
        }

        /// <summary>把每个槽位的默认件 + 全部条目 id 拍成扁平字符串数组，用于缓存重建对比。</summary>
        private static string[] Snapshot()
        {
            var ids = new List<string>();
            for (int s = 0; s < CosmeticCatalog.Slots.Length; s++)
            {
                CosmeticSlot slot = CosmeticCatalog.Slots[s];
                ids.Add(slot + "|default|" + CosmeticCatalog.DefaultId(slot));
                CosmeticDef[] list = CosmeticCatalog.ForSlot(slot);
                for (int i = 0; i < list.Length; i++) ids.Add(slot + "|" + list[i].Id);
            }
            return ids.ToArray();
        }
    }
}
