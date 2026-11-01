using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 干地乌贼（对齐 Inkwave squidDrySpeed）运行时集成测试：
    /// 1. 无墨地面按住潜行键 → 保持乌贼形态（IsInSquidForm）但没有潜墨加成（IsSwimming false、干地慢速）；
    /// 2. 踩敌墨的乌贼同样受敌墨减速与 DOT（敌墨中不可能 submerged）；
    /// 3. 游回己方墨水 → 恢复完整潜墨加成（IsSwimming true、潜墨极速）。
    /// </summary>
    public class DrySquidFormPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";
        private static readonly Vector3 TestSpot = new Vector3(0f, 0.1f, -11f);

        private IEnumerator LoadCleanPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");

            // 清空全场墨水，保证测试起点是纯粹的无墨地面
            InkSurfaceController.Instance.ResetSurface();
            yield return null;
        }

        private IEnumerator TeleportToTestSpot(PlayerController player)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = TestSpot;
            if (cc != null) cc.enabled = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator DryGround_HoldSwimEntersSquidFormWithoutSwimBonus()
        {
            yield return LoadCleanPlaybox();

            PlayerController player = PlayerController.LocalPlayer;
            player.enabled = false; // 禁用外部输入覆盖，直接注入帧意图
            InkCharacterMotor motor = player.Motor;
            Assert.IsNotNull(motor);

            yield return TeleportToTestSpot(player);
            Assert.AreNotEqual(player.Health.Team, motor.FootInkTeam,
                "测试点不应是己方墨水（ResetSurface 后应为无墨地面）");

            // 无墨地面按住潜行键：进入乌贼形态，但没有潜墨加成
            for (int i = 0; i < 4; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsInSquidForm, "无墨地面按住潜行键也应进入乌贼形态（对齐 Inkwave）");
            Assert.IsFalse(motor.IsSwimming, "无墨地面不应判定为潜墨（没有潜墨加成）");
            Assert.AreEqual(motor.Config.squidDryMoveSpeed, motor.CurrentMoveSpeed, 0.01f,
                "干地乌贼目标速度应为 squidDryMoveSpeed（默认 2.9）");

            // 踩敌墨：乌贼形态同样受敌墨减速（敌墨中不可能 submerged）
            InkSurfaceController.Instance.PaintCircle(TestSpot, 3f, player.Health.Team.GetOpponent());
            yield return null;
            yield return null;
            for (int i = 0; i < 2; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsInSquidForm, "敌墨上按住潜行键仍保持乌贼形态");
            Assert.IsFalse(motor.IsSwimming, "敌墨上不应判定为潜墨");
            Assert.AreEqual(motor.Config.enemyInkMoveSpeed, motor.CurrentMoveSpeed, 0.01f,
                "敌墨上的乌贼应被钳制到敌墨移速（默认 1.9）");

            // 游回己方墨水：恢复完整潜墨加成
            InkSurfaceController.Instance.PaintCircle(TestSpot, 3f, player.Health.Team);
            yield return null;
            yield return null;
            for (int i = 0; i < 2; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsSwimming, "己方墨水上的乌贼应进入潜墨（恢复加成）");
            Assert.AreEqual(motor.Config.swimMoveSpeed, motor.CurrentMoveSpeed, 0.01f,
                "潜墨目标速度应为 swimMoveSpeed（默认 11.8）");
        }

        [UnityTest]
        public IEnumerator DryGround_ReleaseSwimReturnsToKidSpeed()
        {
            yield return LoadCleanPlaybox();

            PlayerController player = PlayerController.LocalPlayer;
            player.enabled = false;
            InkCharacterMotor motor = player.Motor;

            yield return TeleportToTestSpot(player);

            for (int i = 0; i < 4; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }
            Assert.IsTrue(motor.IsInSquidForm, "前置：干地乌贼形态应已成立");

            // 松开潜行键：回到人形目标速度
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            yield return null;
            Assert.IsFalse(motor.IsInSquidForm, "松开潜行键应退出乌贼形态");
            Assert.IsFalse(motor.IsSwimming);
            Assert.AreEqual(motor.Config.normalMoveSpeed, motor.CurrentMoveSpeed, 0.01f,
                "人形目标速度应为 normalMoveSpeed（默认 6.0）");
        }
    }
}
