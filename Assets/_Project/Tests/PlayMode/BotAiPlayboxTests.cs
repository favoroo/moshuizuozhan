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
    /// [P3-C] Bot AI 运行时冒烟：真实加载 PhaseA_Playbox 跑一局，验证
    /// Bot 会平滑移动/涂地、瞄准角不发散，以及残血时进入 Retreat 并向远离威胁的方向撤离。
    /// </summary>
    public class BotAiPlayboxTests
    {
        const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadAndStartMatch()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");
            var match = MatchManager.Instance;
            Assert.IsNotNull(match, "场景里应存在 MatchManager");

            // 压缩 3s 倒计时：Bot 行为断言发生在 Playing 之后，不依赖倒计时真实时长
            PlayModeTestKit.ShortenMatchTimers();

            if (match.CurrentPhase == MatchManager.Phase.Ready ||
                match.CurrentPhase == MatchManager.Phase.Result)
            {
                match.StartMatch();
            }

            // 等 3-2-1 倒计时结束，进入 Playing（已压缩到 0.4s）
            float timeout = 8f;
            while (!MatchManager.CombatAllowed && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(MatchManager.CombatAllowed, "倒计时结束后应允许移动与开火");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bots_MoveSmoothly_AndKeepAimFinite()
        {
            yield return LoadAndStartMatch();

            var bots = Object.FindObjectsByType<BotController>();
            Assert.Greater(bots.Length, 0, "试玩场景应包含 Bot");

            var startPositions = new Vector3[bots.Length];
            for (int i = 0; i < bots.Length; i++) startPositions[i] = bots[i].transform.position;

            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < bots.Length; i++)
                {
                    float yaw = bots[i].DebugAimYaw;
                    Assert.IsFalse(float.IsNaN(yaw) || float.IsInfinity(yaw), "瞄准弹簧角不应发散");
                    Vector3 p = bots[i].transform.position;
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.z), "Bot 位置不应为 NaN");
                }

                yield return null;
            }

            int moved = 0;
            for (int i = 0; i < bots.Length; i++)
            {
                if (Vector3.Distance(bots[i].transform.position, startPositions[i]) > 0.5f) moved++;
            }

            Assert.GreaterOrEqual(moved, Mathf.CeilToInt(bots.Length * 0.5f),
                "2.5s 内至少一半 Bot 应完成有效位移（走位/涂地未卡死）");
        }

        [UnityTest]
        public IEnumerator LowHealthBot_EntersRetreat_AndBacksAwayFromThreat()
        {
            yield return LoadAndStartMatch();

            BotController victim = null;
            BotController threat = null;
            CombatantHealth victimHealth = null;
            CombatantHealth threatHealth = null;

            var bots = Object.FindObjectsByType<BotController>();
            for (int i = 0; i < bots.Length; i++)
            {
                var health = bots[i].GetComponent<CombatantHealth>();
                if (health == null) continue;

                if (victim == null)
                {
                    victim = bots[i];
                    victimHealth = health;
                }
                else if (threat == null && health.Team.IsOpponentOf(victimHealth.Team))
                {
                    threat = bots[i];
                    threatHealth = health;
                }
            }

            Assert.IsNotNull(victimHealth, "应找到 Bot");
            Assert.IsNotNull(threatHealth, "应找到敌对阵营 Bot");

            // 除受害者外全部停摆，排除其他 Bot 击杀/干扰造成的偶发失败
            for (int i = 0; i < bots.Length; i++)
            {
                if (bots[i] == victim) continue;

                bots[i].enabled = false;
                var otherMotor = bots[i].GetComponent<InkCharacterMotor>();
                if (otherMotor != null)
                {
                    otherMotor.SetFrameIntent(Vector3.zero, bots[i].transform.forward, false, false);
                    otherMotor.enabled = false;
                }
            }

            // 让威胁站在受害者 4m 处并停住（只作为可见威胁，不参与对抗）
            Vector3 threatPos = victim.transform.position + victim.transform.forward * 4f;
            threatPos.y = victim.transform.position.y;
            Teleport(threat.gameObject, threatPos);
            yield return null;
            yield return null;

            // 打到 30 HP：低于 40 的撤退阈值
            bool killed = victimHealth.TakeDamage(70f, threatHealth.Team, threatPos);
            Assert.IsFalse(killed, "70 点伤害不应直接击杀满血 Bot");
            Assert.Less(victimHealth.CurrentHealth, 40f);

            float before = FlatDistance(victim.transform.position, threatPos);
            Assert.Less(before, 14f, "威胁应在撤退触发半径内");

            // 等一次感知 tick（0.15s）+ 状态切换
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(BotController.BotState.Retreat, victim.State,
                "HP < 40 且近敌时应进入残血撤退");

            float maxAfter = before;
            bool exitDuringWindow = false;
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForSeconds(0.1f);
                maxAfter = Mathf.Max(maxAfter, FlatDistance(victim.transform.position, threatPos));
                if (victim.State != BotController.BotState.Retreat)
                    exitDuringWindow = true;
            }

            // 撤退途中若踩到己方墨会以 60HP/s 回血、达到 75HP 后正常结束撤退，
            // 因此不能用 1.2s 后的终点距离判定，而应看撤退窗口内是否真的拉开过距离。
            Assert.Greater(maxAfter, before + 1f,
                $"撤退应向远离威胁的方向拉开距离（{before:F2}m → 最大 {maxAfter:F2}m，期间退出撤退={exitDuringWindow}）");
        }

        private static void Teleport(GameObject actor, Vector3 position)
        {
            var cc = actor.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            actor.transform.position = position;
            if (cc != null) cc.enabled = true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
