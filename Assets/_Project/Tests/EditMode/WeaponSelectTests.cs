using NUnit.Framework;
using P1.UI.WeaponSelect;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P8] 战前武器选择系统纯逻辑单元测试（EditMode）：
    /// 1. 索引边界安全钳制（ClampIndex 限制在 0..6）；
    /// 2. PlayerPrefs 持久化存储与脏数据清洗；
    /// 3. 全部 7 种武器分类与中英文名称、图标路径的双向映射；
    /// 4. 数值格式化器在 null 或非空配置下永不抛出异常。
    /// </summary>
    public class WeaponSelectTests
    {
        private const string TestPrefsKey = WeaponSelectMath.PrefsKeySelectedWeaponIndex;
        private int _originalPrefsValue;

        [SetUp]
        public void SetUp()
        {
            _originalPrefsValue = PlayerPrefs.GetInt(TestPrefsKey, 0);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetInt(TestPrefsKey, _originalPrefsValue);
            PlayerPrefs.Save();
        }

        [Test]
        public void ClampIndex_HandlesBoundariesAndInvalidValues()
        {
            Assert.AreEqual(0, WeaponSelectMath.ClampIndex(-10));
            Assert.AreEqual(0, WeaponSelectMath.ClampIndex(-1));
            Assert.AreEqual(0, WeaponSelectMath.ClampIndex(0));
            Assert.AreEqual(1, WeaponSelectMath.ClampIndex(1));
            Assert.AreEqual(6, WeaponSelectMath.ClampIndex(6));
            Assert.AreEqual(6, WeaponSelectMath.ClampIndex(7));
            Assert.AreEqual(6, WeaponSelectMath.ClampIndex(999));
        }

        [Test]
        public void PlayerPrefsPersistence_SavesAndLoadsCorrectly()
        {
            WeaponSelectMath.SaveSelectedWeaponIndex(3);
            Assert.AreEqual(3, WeaponSelectMath.LoadSelectedWeaponIndex());

            WeaponSelectMath.SaveSelectedWeaponIndex(-5);
            Assert.AreEqual(0, WeaponSelectMath.LoadSelectedWeaponIndex());

            WeaponSelectMath.SaveSelectedWeaponIndex(20);
            Assert.AreEqual(6, WeaponSelectMath.LoadSelectedWeaponIndex());
        }

        [Test]
        public void IndexToKind_AndKindToIndex_AreBijectiveForAllSevenWeapons()
        {
            WeaponKind[] expectedKinds =
            {
                WeaponKind.Shooter,
                WeaponKind.Roller,
                WeaponKind.Charger,
                WeaponKind.Dualies,
                WeaponKind.Slosher,
                WeaponKind.Blaster,
                WeaponKind.Splatling
            };

            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                Assert.AreEqual(expectedKinds[i], kind, $"索引 {i} 映射的分类不匹配");
                Assert.AreEqual(i, WeaponSelectMath.KindToIndex(kind), $"分类 {kind} 逆映射的索引不匹配");
            }
        }

        [Test]
        public void WeaponMetadata_AllSevenWeaponsHaveValidNamesIconsAndPaths()
        {
            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);

                string enName = WeaponSelectMath.GetWeaponEnglishName(kind);
                string cnName = WeaponSelectMath.GetWeaponChineseName(kind);
                string role = WeaponSelectMath.GetWeaponRoleTag(kind);
                string iconName = WeaponSelectMath.GetWeaponIconFileName(kind);
                string assetPath = WeaponSelectMath.GetWeaponAssetPath(kind);

                Assert.IsFalse(string.IsNullOrEmpty(enName), $"{kind} 英文名为空");
                Assert.IsFalse(string.IsNullOrEmpty(cnName), $"{kind} 中文名为空");
                Assert.IsFalse(string.IsNullOrEmpty(role), $"{kind} 战术标签为空");
                Assert.IsTrue(iconName.EndsWith(".png"), $"{kind} 图标名应以 .png 结尾: {iconName}");
                Assert.IsTrue(assetPath.EndsWith(".asset"), $"{kind} 资产路径应以 .asset 结尾: {assetPath}");
            }
        }

        [Test]
        public void Formatters_NeverThrowExceptionsOnNullConfig()
        {
            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);

                Assert.DoesNotThrow(() =>
                {
                    string dmg = WeaponSelectMath.FormatDamage(null, kind);
                    string rng = WeaponSelectMath.FormatRange(null, kind);
                    string rate = WeaponSelectMath.FormatFireRate(null, kind);
                    string ink = WeaponSelectMath.FormatInkCost(null, kind);
                    string spec = WeaponSelectMath.FormatSpecial(null, kind);

                    Assert.IsFalse(string.IsNullOrEmpty(dmg));
                    Assert.IsFalse(string.IsNullOrEmpty(rng));
                    Assert.IsFalse(string.IsNullOrEmpty(rate));
                    Assert.IsFalse(string.IsNullOrEmpty(ink));
                    Assert.IsFalse(string.IsNullOrEmpty(spec));
                }, $"武器 {kind} 在 null 配置下格式化抛出了异常");
            }
        }

        [Test]
        public void Formatters_ProduceValidOutputWithInstantiatedConfig()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.damage = 42f;
            config.maxRange = 8.8f;
            config.fireInterval = 0.12f;
            config.inkCostPerShot = 1.2f;
            config.specialKind = SpecialKind.Storm;

            string dmg = WeaponSelectMath.FormatDamage(config, WeaponKind.Shooter);
            string rng = WeaponSelectMath.FormatRange(config, WeaponKind.Shooter);
            string rate = WeaponSelectMath.FormatFireRate(config, WeaponKind.Shooter);
            string ink = WeaponSelectMath.FormatInkCost(config, WeaponKind.Shooter);
            string spec = WeaponSelectMath.FormatSpecial(config, WeaponKind.Shooter);

            Assert.AreEqual("42", dmg);
            Assert.AreEqual("8.8m", rng);
            Assert.IsTrue(rate.Contains("/s"));
            Assert.AreEqual("1.2%", ink);
            Assert.IsTrue(spec.Contains("Tempest"));

            Object.DestroyImmediate(config);
        }

        [Test]
        public void ComputeStatBars_NullConfig_AllBarsInRangeForAllSevenWeapons()
        {
            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                WeaponSelectMath.WeaponStatBars bars = WeaponSelectMath.ComputeStatBars(null, kind);

                Assert.GreaterOrEqual(bars.DamageDensity01, 0f, $"{kind} 伤害条下界");
                Assert.LessOrEqual(bars.DamageDensity01, 1f, $"{kind} 伤害条上界");
                Assert.GreaterOrEqual(bars.TurfEfficiency01, 0f, $"{kind} 涂地块下界");
                Assert.LessOrEqual(bars.TurfEfficiency01, 1f, $"{kind} 涂地块上界");
                Assert.GreaterOrEqual(bars.RangeMobility01, 0f, $"{kind} 机动条下界");
                Assert.LessOrEqual(bars.RangeMobility01, 1f, $"{kind} 机动条上界");
            }
        }

        [Test]
        public void ComputeStatBars_SevenWeapons_FillDifferentBarTops()
        {
            // 每条横条的「第一名」必须符合手感认知，这是横条存在的意义：
            // 伤害密度 → 加特林弹幕；涂地效率 → 推地滚筒；射程机动 → 满蓄狙。
            var dpsByKind = new System.Collections.Generic.Dictionary<WeaponKind, float>();
            var turfByKind = new System.Collections.Generic.Dictionary<WeaponKind, float>();
            var rangeByKind = new System.Collections.Generic.Dictionary<WeaponKind, float>();

            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                WeaponSelectMath.WeaponStatBars bars = WeaponSelectMath.ComputeStatBars(null, kind);
                dpsByKind[kind] = bars.DamageDensity01;
                turfByKind[kind] = bars.TurfEfficiency01;
                rangeByKind[kind] = bars.RangeMobility01;
            }

            Assert.AreEqual(WeaponKind.Splatling, ArgMax(dpsByKind), "持续伤害密度最高应是加特林");
            Assert.AreEqual(WeaponKind.Roller, ArgMax(turfByKind), "涂地效率最高应是推地滚筒");
            Assert.AreEqual(WeaponKind.Charger, ArgMax(rangeByKind), "射程机动最高应是满蓄狙");

            // 佐证排序方向：爆破枪/泼桶/蓄力狙是「重拳但低速」，伤害密度必须低于连发系
            Assert.Less(turfByKind[WeaponKind.Charger], 0.15f, "蓄力狙每点墨铺地最少");
            Assert.Less(dpsByKind[WeaponKind.Blaster], dpsByKind[WeaponKind.Shooter], "爆破枪单发重但持续密度低");
            Assert.Less(dpsByKind[WeaponKind.Slosher], dpsByKind[WeaponKind.Shooter], "泼桶持续密度低于小枪");
        }

        [Test]
        public void ComputeStatBars_InstantiatedConfig_TracksTuningAndClamps()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();

            WeaponSelectMath.WeaponStatBars baseline =
                WeaponSelectMath.ComputeStatBars(config, WeaponKind.Shooter);
            float expectedDps01 = Mathf.Clamp01((config.damage / config.fireInterval) / 430f);
            Assert.AreEqual(expectedDps01, baseline.DamageDensity01, 0.02f, "伤害条应按 直击伤害/开火间隔 换算");

            // 调配置 → 横条跟着动：伤害翻倍条变长，耗墨翻倍涂地块变短
            config.damage *= 2f;
            WeaponSelectMath.WeaponStatBars buffed =
                WeaponSelectMath.ComputeStatBars(config, WeaponKind.Shooter);
            Assert.Greater(buffed.DamageDensity01, baseline.DamageDensity01, "伤害提升应拉长伤害条");

            config.inkCostPerShot *= 4f;
            WeaponSelectMath.WeaponStatBars thirsty =
                WeaponSelectMath.ComputeStatBars(config, WeaponKind.Shooter);
            Assert.Less(thirsty.TurfEfficiency01, baseline.TurfEfficiency01, "单发耗墨大增应压短涂地块");

            // 极端数值钳制到 1
            config.damage = 9999f;
            config.fireInterval = 0.02f;
            WeaponSelectMath.WeaponStatBars capped =
                WeaponSelectMath.ComputeStatBars(config, WeaponKind.Shooter);
            Assert.AreEqual(1f, capped.DamageDensity01, 0.001f, "超锚点数值必须钳到满条");

            Object.DestroyImmediate(config);
        }

        private static WeaponKind ArgMax(System.Collections.Generic.Dictionary<WeaponKind, float> values)
        {
            WeaponKind best = WeaponKind.Shooter;
            float bestValue = float.NegativeInfinity;
            foreach (var kv in values)
            {
                if (kv.Value > bestValue)
                {
                    bestValue = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }
    }
}
