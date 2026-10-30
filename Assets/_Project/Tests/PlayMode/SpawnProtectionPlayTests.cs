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
    /// [出生保护 + 出生台可涂] PlayMode 回归（对齐 INKWAVE）：
    /// 1. 只有死亡复活授予 3s 出生保护，开局站位 / 重开比赛不授予；
    /// 2. 保护期内武器伤害与敌方墨水灼烧都不结算，结束后恢复；
    /// 3. 出生台顶面可涂且计入覆盖率（侧面由 InkWallSurface 负责）。
    /// </summary>
    public class SpawnProtectionPlayTests
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

            player.enabled = false; // 关闭输入驱动，避免测试期间误操作

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
        public IEnumerator Respawn_GrantsThreeSecondSpawnProtection()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var health = player.Health;

            health.IsInvulnerable = false;
            Assert.IsFalse(health.IsSpawnProtected, "开局站位不应白送出生保护（对齐 INKWAVE）");

            health.TakeDamage(200f, Team.TeamB, player.transform.position);
            Assert.IsFalse(health.IsAlive, "200 伤害应击杀玩家");
            Assert.IsFalse(health.IsSpawnProtected, "死亡后不应残留出生保护");

            // 复活计时走 Time.deltaTime：8 倍时间倍率等待 4.3 游戏内秒（断言仍按游戏内秒）
            yield return PlayModeTestKit.WaitGameSecondsAccelerated(4.3f);

            Assert.IsTrue(health.IsAlive, "4s 后应自动复活");
            Assert.IsTrue(health.IsSpawnProtected, "复活后应进入出生保护");
            Assert.AreEqual(3f, health.SpawnProtectionDuration, 0.01f,
                "PlayerConfigSO 默认出生保护时长应为 3s");
            Assert.AreEqual(3f, health.SpawnProtectionRemaining, 0.35f,
                "出生保护应从满 3s 开始倒计时");

            float hp = health.CurrentHealth;
            bool killed = health.TakeDamage(200f, Team.TeamB, player.transform.position);
            Assert.IsFalse(killed, "保护期内不应被击杀");
            Assert.AreEqual(hp, health.CurrentHealth, 0.01f, "保护期内武器伤害不应结算");

            health.ApplyEnemyInkBurn(15f, 60f);
            Assert.AreEqual(hp, health.CurrentHealth, 0.01f, "保护期内敌方墨水灼烧也不应结算");

            yield return PlayModeTestKit.WaitGameSecondsAccelerated(3.2f);

            Assert.IsFalse(health.IsSpawnProtected, "3s 后出生保护应结束");
            Assert.IsFalse(health.IsInvulnerable, "保护结束后应恢复可受伤");

            // 玩家阵亡时击杀爆墨会在出生点留下击杀方墨斑，保护结束后的敌墨 DOT 会干扰精确断言；
            // 先把脚下涂回己方墨（也验证正常涂地/踩墨路径），再测武器伤害。
            InkSurfaceController.Instance.PaintCircle(player.transform.position, 2f, Team.TeamA);
            yield return null;

            hp = health.CurrentHealth;
            killed = health.TakeDamage(30f, Team.TeamB, player.transform.position, null, "Test");
            Assert.IsFalse(killed);
            Assert.AreEqual(hp - 30f, health.CurrentHealth, 0.6f, "保护结束后伤害应正常结算");
        }

        [UnityTest]
        public IEnumerator SpawnPadTop_IsPaintable_AndCountsTowardCoverage()
        {
            yield return LoadPlaybox();

            var ctrl = InkSurfaceController.Instance;
            var padA = GameObject.Find("SafeSpawnPad_TeamA");
            var padB = GameObject.Find("SafeSpawnPad_TeamB");
            Assert.IsNotNull(padA, "场景里应存在 TeamA 出生台");
            Assert.IsNotNull(padB, "场景里应存在 TeamB 出生台");
            Assert.IsNotNull(padA.GetComponent<InkWallSurface>(), "出生台侧沿应可涂");
            Assert.IsNotNull(padB.GetComponent<InkWallSurface>(), "出生台侧沿应可涂");

            Vector3 padCenter = padA.transform.position + Vector3.up * 0.2f;
            float coverageBefore = ctrl.GetCoveragePercent(Team.TeamB);

            int changed = ctrl.PaintSphereWithWalls(padCenter, 2.2f, Team.TeamB);

            Assert.Greater(changed, 0, "出生台顶面必须可以涂墨");
            Assert.AreEqual(Team.TeamB, ctrl.InkAt(padCenter), "出生台中心应归属涂墨队伍");
            Assert.Greater(ctrl.GetCoveragePercent(Team.TeamB), coverageBefore,
                "出生台涂墨必须计入覆盖率（不能进不可涂分母）");
        }
    }
}
