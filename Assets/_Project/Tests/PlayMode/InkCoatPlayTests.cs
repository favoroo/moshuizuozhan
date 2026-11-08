using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P32 敌方墨覆盖] PlayMode 回归（对齐 INKWAVE actor.js hurtFlash / character-mats.js HURT_FRAG）：
    /// 1. 被敌方命中 → 身体覆盖量上升，且取色是攻击方队色；
    /// 2. 覆盖量随时间自然淡去（纯命中墨斑约 1.7s 淡净）；
    /// 3. 踩在敌方墨上会持续累积覆盖量，离开后衰减；
    /// 4. 残血本身就是覆盖读数（1 - 血量百分比），回血后一起消掉；
    /// 5. 复活清零，不会把上一命的墨斑带进新一命。
    /// </summary>
    public class InkCoatPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            PlayerController player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(player.Health, "本地玩家应挂载 CombatantHealth");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");

            player.enabled = false; // 关闭输入驱动，避免测试期间误操作

            BotController[] bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
                InkCharacterMotor botMotor = bots[i].GetComponent<InkCharacterMotor>();
                if (botMotor != null) botMotor.enabled = false;
                Weapons.WeaponController botWeapon = bots[i].GetComponent<Weapons.WeaponController>();
                if (botWeapon != null) botWeapon.enabled = false;
            }

            yield return null;
        }

        /// <summary>把脚下涂成指定队伍并推进一帧，让 motor 采样到新的 FootInkTeam。</summary>
        private IEnumerator PaintUnderPlayer(Team team)
        {
            PlayerController player = PlayerController.LocalPlayer;
            InkSurfaceController.Instance.PaintCircle(player.transform.position, 2f, team);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Hit_RaisesInkCoat_WithAttackerTeamColor()
        {
            yield return LoadPlaybox();

            CombatantHealth health = PlayerController.LocalPlayer.Health;
            Assert.AreEqual(0f, health.InkCoatAmount01, 0.001f, "开局应是干净状态");

            health.TakeDamage(20f, Team.TeamB, health.transform.position, null, "Test");
            yield return null;

            Assert.Greater(health.InkCoatAmount01, 0.1f, "被命中后身体应被泼上墨");
            Color expected = InkSurfaceController.Instance.GetTeamColor(Team.TeamB);
            Assert.AreEqual(expected.r, health.InkCoatColor.r, 0.01f, "墨色应取攻击方队色");
            Assert.AreEqual(expected.g, health.InkCoatColor.g, 0.01f);
            Assert.AreEqual(expected.b, health.InkCoatColor.b, 0.01f);
        }

        [UnityTest]
        public IEnumerator InkCoat_DecaysOverTime()
        {
            yield return LoadPlaybox();

            CombatantHealth health = PlayerController.LocalPlayer.Health;
            health.TakeDamage(20f, Team.TeamB, health.transform.position, null, "Test");
            yield return null;
            float peak = health.InkCoatAmount01;
            Assert.Greater(peak, 0.1f, "先要有墨可淡");

            // 残血项（1 - 血量百分比）不会自己消失，必须先回满血，才能单独观察命中墨斑的淡去
            health.Heal(health.MaxHealth);
            yield return PlayModeTestKit.WaitGameSecondsAccelerated(1.2f);

            Assert.Less(health.InkCoatAmount01, 0.05f, "纯命中墨斑约 1.7s 内应淡净（0.6/s）");
        }

        [UnityTest]
        public IEnumerator StandingInEnemyInk_AccumulatesCoat_BeyondDamageFloor()
        {
            yield return LoadPlaybox();

            CombatantHealth health = PlayerController.LocalPlayer.Health;
            yield return PaintUnderPlayer(Team.TeamB);

            InkCharacterMotor motor = PlayerController.LocalPlayer.GetComponent<InkCharacterMotor>();
            Assert.IsNotNull(motor, "本地玩家应有 InkCharacterMotor");
            Assert.IsTrue(motor.IsInEnemyInk, "脚下应识别为敌方墨");

            // 敌墨 DOT 有 60 血底线（最多掉 40 点），但覆盖量继续按 0.5/s 累积 —— 约 2s 糊满全身
            yield return PlayModeTestKit.WaitGameSecondsAccelerated(1.6f);

            Assert.Greater(health.InkCoatAmount01, 0.6f, "踩敌墨应把身体糊满，即使血量已触底");
        }

        [UnityTest]
        public IEnumerator LowHealth_CountsAsInkCoverage_AndClearsAfterHeal()
        {
            yield return LoadPlaybox();

            CombatantHealth health = PlayerController.LocalPlayer.Health;
            health.TakeDamage(70f, Team.TeamB, health.transform.position, null, "Test");
            yield return null;

            // 残血 30% → 覆盖量至少 0.7（INKWAVE 的 (1 - hp/maxHp) 项）
            Assert.GreaterOrEqual(health.InkCoatAmount01, 0.65f, "残血本身应读作被墨糊住");

            health.Heal(health.MaxHealth);
            yield return PlayModeTestKit.WaitGameSecondsAccelerated(2.5f);

            Assert.Less(health.InkCoatAmount01, 0.1f, "回满血后墨斑应淡净");
        }

        [UnityTest]
        public IEnumerator Respawn_ResetsInkCoat()
        {
            yield return LoadPlaybox();

            CombatantHealth health = PlayerController.LocalPlayer.Health;
            health.TakeDamage(20f, Team.TeamB, health.transform.position, null, "Test");
            yield return null;
            Assert.Greater(health.InkCoatAmount01, 0.1f, "先要有墨");

            health.TakeDamage(500f, Team.TeamB, health.transform.position, null, "Test");
            Assert.IsFalse(health.IsAlive, "500 伤害应击杀");

            // 逐帧等复活（4s 复活延迟 × 8 倍速）。必须精确停在「刚复活、出生保护仍在」的那一帧：
            // 本用例把玩家杀在自己的出生点上，击杀爆墨会在脚下留下敌墨，保护期一过就会重新累积墨斑
            // （这是正确行为，不是回归），所以不能靠固定秒数掐点。
            float previous = Time.timeScale;
            Time.timeScale = 8f;
            float deadline = Time.realtimeSinceStartup + 10f;
            try
            {
                while (!health.IsAlive && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = previous;
            }

            Assert.IsTrue(health.IsAlive, "应已复活");
            Assert.IsTrue(health.IsSpawnProtected, "刚复活应处于出生保护中");
            Assert.AreEqual(0f, health.InkCoatAmount01, 0.001f,
                $"复活后身体必须干净（hp={health.CurrentHealth:F1}/{health.MaxHealth:F1} " +
                $"invuln={health.IsInvulnerable} protected={health.IsSpawnProtected} " +
                $"amount={health.InkCoatAmount01:F3}）");
        }
    }
}
