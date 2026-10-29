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
    }
}
