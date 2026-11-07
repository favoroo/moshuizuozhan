using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 击杀奖励全链路（把交火接进主循环，PlayMode——依赖 Awake/OnKilled 生命周期）：
    /// 1. 击杀 → 固定能量 70 + 受害者损失转移（阵亡减半前的 50%）+ 一次性回墨 25；
    /// 2. 击杀满能量目标 ≈ 拿到 0.9 个大招；
    /// 3. 环境死亡（无攻击者）不白送奖励；受害者保留减半后的剩余能量。
    /// 纯数值口径见 EditMode 的 KillRewardTests / KillRewardMath。
    /// </summary>
    public class KillRewardPlayTests
    {
        [UnityTest]
        public IEnumerator AwardKill_KillerGainsBonusTransferAndInkRefill()
        {
            WeaponController killerWeapon = CreateCombatant("KillReward_Killer", Team.TeamA, out InkCharacterMotor killerMotor);
            WeaponController victimWeapon = CreateCombatant("KillReward_Victim", Team.TeamB, out _);
            yield return null;

            victimWeapon.AddSpecialPointsRaw(100f);
            Assert.AreEqual(100f, victimWeapon.SpecialPoints, 0.01f, "前置：受害者已充能 100 点");

            Assert.IsTrue(killerMotor.TryConsumeInk(60f), "前置：击杀者能消耗 60 墨");
            Assert.AreEqual(40f, killerMotor.CurrentInk, 0.01f, "前置：击杀者墨水降到 40");

            CombatantHealth victim = victimWeapon.GetComponent<CombatantHealth>();
            bool killed = victim.TakeDamage(
                999f, Team.TeamA, victim.transform.position,
                killerWeapon.GetComponent<CombatantHealth>(), "KillRewardTest");
            yield return null;

            Assert.IsTrue(killed, "999 伤害应击杀受害者");
            Assert.AreEqual(120f, killerWeapon.SpecialPoints, 0.01f,
                "击杀者应获得 70 固定奖励 + 受害者损失转移 50（100 × 0.5）");
            Assert.AreEqual(50f, victimWeapon.SpecialPoints, 0.01f,
                "受害者阵亡后保留减半后的 50 点");
            Assert.AreEqual(65f, killerMotor.CurrentInk, 0.01f,
                "击杀者应一次性回墨 25（40 + 25，未超罐上限）");

            Object.Destroy(killerWeapon.gameObject);
            Object.Destroy(victimWeapon.gameObject);
        }

        [UnityTest]
        public IEnumerator AwardKill_VictimAtFullCharge_TransfersHalfOfFullBar()
        {
            WeaponController killerWeapon = CreateCombatant("KillReward_KillerFull", Team.TeamB, out _);
            WeaponController victimWeapon = CreateCombatant("KillReward_VictimFull", Team.TeamA, out _);
            yield return null;

            victimWeapon.AddSpecialPointsRaw(180f);
            Assert.AreEqual(180f, victimWeapon.SpecialPoints, 0.01f, "前置：受害者满能量");

            bool killed = victimWeapon.GetComponent<CombatantHealth>().TakeDamage(
                999f, Team.TeamB, victimWeapon.transform.position,
                killerWeapon.GetComponent<CombatantHealth>(), "KillRewardTest");
            yield return null;

            Assert.IsTrue(killed);
            Assert.AreEqual(160f, killerWeapon.SpecialPoints, 0.01f,
                "击杀满能量目标 = 70 固定 + 90 转移，接近一个大招");

            Object.Destroy(killerWeapon.gameObject);
            Object.Destroy(victimWeapon.gameObject);
        }

        [UnityTest]
        public IEnumerator AwardKill_EnvironmentalDeathWithoutAttacker_GivesNothing()
        {
            WeaponController killerWeapon = CreateCombatant("KillReward_Bystander", Team.TeamB, out InkCharacterMotor killerMotor);
            WeaponController victimWeapon = CreateCombatant("KillReward_Fallen", Team.TeamA, out _);
            yield return null;

            victimWeapon.AddSpecialPointsRaw(80f);

            // attacker 传 null：跌落/越界等环境伤害的死亡路径
            bool killed = victimWeapon.GetComponent<CombatantHealth>().TakeDamage(
                999f, Team.TeamB, victimWeapon.transform.position, null, null);
            yield return null;

            Assert.IsTrue(killed, "前置：受害者阵亡");
            Assert.AreEqual(0f, killerWeapon.SpecialPoints, 0.01f,
                "无攻击者的环境死亡不得给旁观者充能");
            Assert.AreEqual(100f, killerMotor.CurrentInk, 0.01f,
                "无攻击者的环境死亡不得回墨");

            Object.Destroy(killerWeapon.gameObject);
            Object.Destroy(victimWeapon.gameObject);
        }

        /// <summary>创建「血量 + 机动 + 武器」最小实体栈（PlayMode 下 Awake 自动初始化订阅链）。</summary>
        private static WeaponController CreateCombatant(string name, Team team, out InkCharacterMotor motor)
        {
            var go = new GameObject(name);
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            motor = go.AddComponent<InkCharacterMotor>();
            return go.AddComponent<WeaponController>();
        }
    }
}
