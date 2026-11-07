using System.Collections.Generic;
using NUnit.Framework;
using P1.AI;
using P1.UI.WeaponSelect;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// Bot 武器抽卡与昵称纯逻辑测试（INKWAVE pickTeam 规则的不变量）：
    /// 同队互不重复、队友池排除玩家武器、敌方从全部 7 种抽、同种子确定、多种子覆盖全部武器。
    /// </summary>
    public class BotLoadoutMathTests
    {
        [Test]
        public void DraftWeaponKinds_AliveTeam_ExcludesPlayerKind_AndIsUnique()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                WeaponKind playerKind = (WeaponKind)(seed % BotLoadoutMath.WeaponKindCount);
                WeaponKind[] ally = BotLoadoutMath.DraftWeaponKinds(3, playerKind, seed);

                Assert.AreEqual(3, ally.Length);
                var seen = new HashSet<WeaponKind>();
                foreach (WeaponKind kind in ally)
                {
                    Assert.AreNotEqual(playerKind, kind,
                        "队友不得拿与玩家相同的武器类型（seed=" + seed + "）");
                    Assert.IsTrue(seen.Add(kind),
                        "同队武器类型必须互不重复（seed=" + seed + "）");
                }
            }
        }

        [Test]
        public void DraftWeaponKinds_EnemyTeam_DrawsFourUniqueFromAllSeven()
        {
            var reachedKinds = new HashSet<WeaponKind>();

            for (int seed = 0; seed < 200; seed++)
            {
                WeaponKind[] enemy = BotLoadoutMath.DraftWeaponKinds(4, null, seed);
                Assert.AreEqual(4, enemy.Length);

                var seen = new HashSet<WeaponKind>();
                foreach (WeaponKind kind in enemy)
                {
                    Assert.IsTrue(seen.Add(kind),
                        "敌方武器类型必须互不重复（seed=" + seed + "）");
                    reachedKinds.Add(kind);
                }
            }

            Assert.AreEqual(BotLoadoutMath.WeaponKindCount, reachedKinds.Count,
                "多种子下 7 种武器都应至少出现一次");
        }

        [Test]
        public void DraftWeaponKinds_SameSeed_IsDeterministic()
        {
            WeaponKind[] a = BotLoadoutMath.DraftWeaponKinds(4, WeaponKind.Charger, 12345);
            WeaponKind[] b = BotLoadoutMath.DraftWeaponKinds(4, WeaponKind.Charger, 12345);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], "同种子必须给出完全相同的抽卡结果");
        }

        [Test]
        public void DraftWeaponKinds_DifferentSeeds_VaryTheRoster()
        {
            var distinctResults = new HashSet<string>();
            for (int seed = 0; seed < 60; seed++)
            {
                WeaponKind[] enemy = BotLoadoutMath.DraftWeaponKinds(4, null, seed * 7919 + 13);
                distinctResults.Add(string.Join(",", System.Linq.Enumerable.Select(enemy, k => (int)k)));
            }

            Assert.GreaterOrEqual(distinctResults.Count, 20,
                "每局重抽必须给出足够多样的阵容");
        }

        [Test]
        public void DraftNames_AreUniqueAndDeterministic()
        {
            string[] a = BotLoadoutMath.DraftNames(7, 999);
            string[] b = BotLoadoutMath.DraftNames(7, 999);

            Assert.AreEqual(7, a.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i], b[i], "同种子昵称必须一致");

            Assert.AreEqual(7, new HashSet<string>(a).Count, "同一局 7 个 Bot 昵称不得重复");

            foreach (string name in a)
                Assert.IsTrue(ArrayContains(BotLoadoutMath.BotNames, name),
                    "昵称必须来自名单：" + name);
        }

        [Test]
        public void KindFromIndex_ClampsSafely()
        {
            Assert.AreEqual(WeaponKind.Shooter, BotLoadoutMath.KindFromIndex(-99));
            Assert.AreEqual(WeaponKind.Splatling, BotLoadoutMath.KindFromIndex(999));
            Assert.AreEqual(WeaponKind.Dualies, BotLoadoutMath.KindFromIndex(3));
        }

        /// <summary>
        /// 武器资产路径表必须与战前选枪 UI（WeaponSelectMath）完全一致：
        /// 两处如果漂移，独立包里 Bot 会拿不到配置、玩家选枪预览与 Bot 抽卡也会出现两套武器。
        /// </summary>
        [Test]
        public void WeaponAssetPathTable_MatchesWeaponSelectMath()
        {
            for (int i = 0; i < BotLoadoutMath.WeaponKindCount; i++)
            {
                WeaponKind kind = (WeaponKind)i;
                Assert.AreEqual(
                    WeaponSelectMath.GetWeaponAssetPath(kind),
                    BotLoadoutMath.GetWeaponAssetPath(kind),
                    kind + " 的资产路径表与选枪 UI 不一致");
            }
        }

        [Test]
        public void BotNames_Table_UsesNonEmptyUniqueNames()
        {
            Assert.GreaterOrEqual(BotLoadoutMath.BotNames.Length, 20, "昵称池至少 20 个（对齐 INKWAVE）");

            var seen = new HashSet<string>();
            foreach (string name in BotLoadoutMath.BotNames)
            {
                Assert.IsFalse(string.IsNullOrEmpty(name));
                Assert.IsTrue(seen.Add(name), "昵称池内部不得重复：" + name);
            }
        }

        private static bool ArrayContains(string[] array, string value)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == value) return true;
            }

            return false;
        }
    }
}
