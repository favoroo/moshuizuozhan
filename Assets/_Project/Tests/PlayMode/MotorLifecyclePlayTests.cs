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
    /// [P6-B] 角色机动/潜墨/踩敌墨/复活的 PlayMode 生命周期回归：
    /// 1. 己方墨水中潜行 11.8 m/s、回墨 42/s（空罐约 2.4s 回满）；
    /// 2. 静止潜墨进入隐匿（IsStealthed + OnStealthChanged），移动超过 1.5 m/s 立即解除；
    /// 3. 踩敌墨减速到 1.9 m/s，DOT 20/s 但被 60 HP 保护底线钳制、绝不致死；
    /// 4. 离开敌墨后 0.5s 内完全摆脱惩罚状态并恢复 6.0 m/s；
    /// 5. 阵亡 4s 后复活：满血满墨、退出潜行/隐匿、回到出生点。
    /// </summary>
    public class MotorLifecyclePlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");

            player.enabled = false; // 关闭输入驱动，由测试直接注入运动意图

            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
                var botMotor = bots[i].GetComponent<InkCharacterMotor>();
                if (botMotor != null) botMotor.enabled = false;
                var botWeapon = bots[i].GetComponent<Weapons.WeaponController>();
                if (botWeapon != null) botWeapon.enabled = false;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SwimOnOwnInk_MovesAt11_8_Refills42PerSecond_AndStealthToggles()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;

            var motor = player.Motor;
            var surface = InkSurfaceController.Instance;
            Vector3 spot = new Vector3(0f, 0.1f, -11f);
            surface.PaintCircle(spot, 5.5f, Team.TeamA);

            Teleport(player.gameObject, spot);
            yield return null;
            yield return null;

            Assert.AreEqual(Team.TeamA, motor.FootInkTeam, "应站在己方墨水上");

            motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
            yield return null;

            Assert.IsTrue(motor.IsSwimming, "己方墨水上按住潜行键应进入潜行形态");
            Assert.AreEqual(11.8f, motor.CurrentMoveSpeed, 0.01f, "潜墨移速 11.8 m/s");

            // 消耗到只剩 5 墨，验证 42/s 回墨速度
            Assert.IsTrue(motor.TryConsumeInk(motor.CurrentInk - 5f));
            Assert.AreEqual(5f, motor.CurrentInk, 0.01f);

            // 静止潜墨即进入隐匿（进入发生在订阅之前，这里直接断言状态）
            float startTime = Time.time;
            while (Time.time - startTime < 1f)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }

            float oneSecondInk = motor.CurrentInk;
            Assert.That(oneSecondInk, Is.InRange(44f, 50f), $"潜墨 1s 应回墨约 42（实际 {oneSecondInk:F1}）");
            Assert.IsTrue(motor.IsStealthed, "静止潜墨应进入隐匿");

            // 订阅隐匿事件：先验证「移动退出」，再验证「静止重新进入」
            int stealthEvents = 0;
            bool lastStealthValue = true;
            motor.OnStealthChanged += (m, on) => { stealthEvents++; lastStealthValue = on; };

            // 极慢蠕动（11.8 × 0.1 = 1.18 m/s <= 1.5）仍保持隐匿
            for (int i = 0; i < 3; i++)
            {
                motor.SetFrameIntent(Vector3.forward * 0.1f, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsStealthed, "低于 1.5 m/s 的蠕动仍应保持隐匿");
            Assert.AreEqual(0, stealthEvents, "阈值内移动不应触发隐匿状态变化");

            // 正常潜行速度（11.8 m/s）立即解除隐匿
            for (int i = 0; i < 3; i++)
            {
                motor.SetFrameIntent(Vector3.forward, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsFalse(motor.IsStealthed, "超过隐匿速度阈值应立即解除隐匿");
            Assert.GreaterOrEqual(stealthEvents, 1, "解除隐匿应触发 OnStealthChanged");
            Assert.IsFalse(lastStealthValue, "解除隐匿的事件参数应为 false");

            // 回到静止继续回墨到满罐（5 + 42×2.4 ≈ 100），并重新进入隐匿
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
            float refillStart = Time.time;
            while (Time.time - refillStart < 1.5f)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.AreEqual(100f, motor.CurrentInk, 0.5f, "潜墨约 2.4s 应回满 100 墨");
            Assert.IsTrue(motor.IsStealthed, "再次静止应重新进入隐匿");
            Assert.GreaterOrEqual(stealthEvents, 2, "重新进入隐匿应再次触发 OnStealthChanged");
            Assert.IsTrue(lastStealthValue, "重新进入的事件参数应为 true");

            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            yield return null;
            Assert.IsFalse(motor.IsSwimming, "松开潜行键应恢复人形");
        }

        [UnityTest]
        public IEnumerator EnemyInk_SlowsTo1_9_AndSixtyHealthFloor_NeverKills()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;

            var motor = player.Motor;
            var health = player.Health;
            var surface = InkSurfaceController.Instance;

            health.IsInvulnerable = false;
            Vector3 enemySpot = new Vector3(0f, 0.1f, -6f);
            surface.PaintCircle(enemySpot, 3f, Team.TeamB);

            Teleport(player.gameObject, enemySpot);
            yield return null;
            yield return null;

            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            yield return null;

            Assert.IsTrue(motor.IsInEnemyInk, "应踩在敌方墨水上");
            Assert.AreEqual(1.9f, motor.CurrentMoveSpeed, 0.01f, "踩敌墨移速骤降到 1.9 m/s");
            Assert.AreEqual(1f, motor.EnemyInkGrip01, 0.01f, "踩敌墨瞬间受击槽锁满");

            // 站在敌墨上 3s：DOT 20/s 只能压到 60，绝不致死
            float burnStart = Time.time;
            while (Time.time - burnStart < 3f)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
            }

            Assert.IsTrue(health.IsAlive, "单靠踩敌墨绝不能击杀角色");
            Assert.AreEqual(60f, health.CurrentHealth, 0.6f, "敌墨 DOT 必须停在 60 HP 保护底线");

            // 离开敌墨到中性地面：恢复 6.0 m/s，0.5s 内受击槽清零
            Vector3 neutralSpot = new Vector3(10f, 0.1f, 10f);
            Assert.AreEqual(Team.Neutral, surface.InkAt(neutralSpot), "测试点必须是中性地面");
            Teleport(player.gameObject, neutralSpot);
            yield return null;
            yield return null;

            Assert.IsFalse(motor.IsInEnemyInk);
            Assert.AreEqual(6f, motor.CurrentMoveSpeed, 0.01f, "离开敌墨应恢复 6.0 m/s");

            float recoverStart = Time.time;
            while (Time.time - recoverStart < 0.7f)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
            }
            Assert.AreEqual(0f, motor.EnemyInkGrip01, 0.01f, "离开敌墨 0.5s 后受击槽应完全清零");
        }

        [UnityTest]
        public IEnumerator DeathAndRespawn_ClearsSwimStealth_AndRestoresHealthAndInk()
        {
            yield return LoadPlaybox();
            var player = PlayerController.LocalPlayer;

            var motor = player.Motor;
            var health = player.Health;
            var surface = InkSurfaceController.Instance;

            health.IsInvulnerable = false;
            Vector3 spawnPos = player.transform.position;
            Vector3 inkSpot = new Vector3(0f, 0.1f, -11f);
            surface.PaintCircle(inkSpot, 5.5f, Team.TeamA);

            Teleport(player.gameObject, inkSpot);
            yield return null;
            yield return null;

            motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
            for (int i = 0; i < 4; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsSwimming);
            Assert.IsTrue(motor.IsStealthed);

            health.TakeDamage(200f, Team.TeamB, player.transform.position);
            Assert.IsFalse(health.IsAlive, "200 伤害应击杀玩家");
            Assert.AreEqual(4f, health.RespawnRemaining, 0.2f, "复活倒计时应为 4s");

            yield return new WaitForSeconds(4.3f);

            Assert.IsTrue(health.IsAlive, "4s 后应自动复活");
            Assert.AreEqual(100f, health.CurrentHealth, 0.5f, "复活应满血");
            Assert.AreEqual(100f, motor.CurrentInk, 0.5f, "复活应满墨");
            Assert.IsFalse(motor.IsSwimming, "复活后应回到人形");
            Assert.IsFalse(motor.IsStealthed, "复活后必须清除隐匿状态");
            Assert.Less(Vector3.Distance(player.transform.position, spawnPos), 0.6f, "复活应回到出生点");
        }

        private static void Teleport(GameObject actor, Vector3 position)
        {
            var cc = actor.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            actor.transform.position = position;
            if (cc != null) cc.enabled = true;
        }
    }
}