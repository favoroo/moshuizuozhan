using NUnit.Framework;
using P1.Core;
using P1.UI.WeaponSelect;
using P1.Weapons;

namespace P1.Tests
{
    /// <summary>
    /// [P36] 阵亡整备（复活换枪）纯逻辑单元测试（EditMode）：
    /// 1. 显示门禁只在「对局进行中 + 本地玩家已阵亡」成立；
    /// 2. 挂起选择只在「进行中 + 已复活 + 有挂起」结算，结算/重开必须作废；
    /// 3. 倒计时文案在 0.05s 内改口「复活中…」，负值不出现「-0.0s」；
    /// 4. 索引清洗与 7 槽常量；null 配置下摘要不抛异常。
    /// </summary>
    public class RespawnLoadoutMathTests
    {
        [Test]
        public void SlotCount_MatchesPreMatchWeaponCount()
        {
            Assert.AreEqual(WeaponSelectMath.WeaponCount, RespawnLoadoutMath.SlotCount);
            Assert.AreEqual(7, RespawnLoadoutMath.SlotCount);
        }

        [Test]
        public void ShouldShow_OnlyWhenPlayingAndDead()
        {
            Assert.IsTrue(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Playing, true, false),
                "进行中 + 已阵亡必须弹出");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Playing, true, true),
                "活着不能弹整备条");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Playing, false, false),
                "没有本地玩家不能弹");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Ready, true, false),
                "Ready 阶段由战前武器库负责，不能弹整备条");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Countdown, true, false),
                "倒计时阶段不弹");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Finished, true, false),
                "终场冻结不弹");
            Assert.IsFalse(RespawnLoadoutMath.ShouldShow(MatchManager.Phase.Result, true, false),
                "结算不弹");
        }

        [Test]
        public void ShouldApplyPending_RequiresPlayingAliveAndPending()
        {
            Assert.IsTrue(RespawnLoadoutMath.ShouldApplyPending(MatchManager.Phase.Playing, true, true),
                "进行中 + 刚复活 + 有挂起选择 = 结算");
            Assert.IsFalse(RespawnLoadoutMath.ShouldApplyPending(MatchManager.Phase.Playing, false, true),
                "没有挂起选择不能凭空换枪");
            Assert.IsFalse(RespawnLoadoutMath.ShouldApplyPending(MatchManager.Phase.Playing, true, false),
                "还躺着不能结算");
            Assert.IsFalse(RespawnLoadoutMath.ShouldApplyPending(MatchManager.Phase.Finished, true, true),
                "终场后复活不能把上一局的选择带进结算");
            Assert.IsFalse(RespawnLoadoutMath.ShouldApplyPending(MatchManager.Phase.Ready, true, true),
                "回到 Ready 必须作废挂起选择");
        }

        [Test]
        public void ClampIndex_ClampsToValidRange()
        {
            Assert.AreEqual(0, RespawnLoadoutMath.ClampIndex(-3));
            Assert.AreEqual(0, RespawnLoadoutMath.ClampIndex(0));
            Assert.AreEqual(3, RespawnLoadoutMath.ClampIndex(3));
            Assert.AreEqual(6, RespawnLoadoutMath.ClampIndex(6));
            Assert.AreEqual(6, RespawnLoadoutMath.ClampIndex(7));
            Assert.AreEqual(6, RespawnLoadoutMath.ClampIndex(999));
        }

        [Test]
        public void FormatCountdown_SwitchesToRespawnLabelNearZero()
        {
            StringAssert.Contains("3.2", RespawnLoadoutMath.FormatCountdown(3.24f));
            StringAssert.Contains("12.0", RespawnLoadoutMath.FormatCountdown(12f));
            Assert.AreEqual("复活中…", RespawnLoadoutMath.FormatCountdown(0f));
            Assert.AreEqual("复活中…", RespawnLoadoutMath.FormatCountdown(0.04f));
            Assert.AreEqual("复活中…", RespawnLoadoutMath.FormatCountdown(-1f),
                "负值不能显示成 -1.0s");
            Assert.AreEqual("复活中…", RespawnLoadoutMath.FormatCountdown(RespawnLoadoutMath.ImminentRespawnSeconds));
        }

        [Test]
        public void FormatWeaponTitle_ContainsNameAndRoleForEveryKind()
        {
            for (int i = 0; i < RespawnLoadoutMath.SlotCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                string title = RespawnLoadoutMath.FormatWeaponTitle(kind);

                Assert.IsNotEmpty(title);
                StringAssert.Contains("·", title, $"{kind} 的标题应包含「名称 · 定位」分隔符");
            }
        }

        [Test]
        public void FormatStats_NullConfigNeverThrows()
        {
            for (int i = 0; i < RespawnLoadoutMath.SlotCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                string stats = RespawnLoadoutMath.FormatStats(null, kind);

                StringAssert.Contains("伤害", stats);
                StringAssert.Contains("射程", stats);
                StringAssert.Contains("射速", stats);
                StringAssert.Contains("耗墨", stats);
            }
        }

        [Test]
        public void FormatStats_WithConfig_MatchesPreMatchCardFormat()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<WeaponConfigSO>();
            try
            {
                config.weaponKind = WeaponKind.Shooter;
                string expected = $"伤害 {WeaponSelectMath.FormatDamage(config, WeaponKind.Shooter)} · " +
                                  $"射程 {WeaponSelectMath.FormatRange(config, WeaponKind.Shooter)} · " +
                                  $"射速 {WeaponSelectMath.FormatFireRate(config, WeaponKind.Shooter)} · " +
                                  $"耗墨 {WeaponSelectMath.FormatInkCost(config, WeaponKind.Shooter)}";

                Assert.AreEqual(expected, RespawnLoadoutMath.FormatStats(config, WeaponKind.Shooter));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void SwapCostHint_MentionsSpecialCharge()
        {
            StringAssert.Contains("大招", RespawnLoadoutMath.SwapCostHint);
            StringAssert.Contains("复活", RespawnLoadoutMath.SwapCostHint);
        }
    }
}
