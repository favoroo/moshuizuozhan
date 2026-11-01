using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 滚筒贴地回归（P31）：
    /// 真实骨架 + 真实 WeaponController 注入扳机，按住推地后测量滚轴底缘与地面的间隙。
    /// 覆盖默认视角 / 抬头 / 低头 / 挥击砸地四种状态——滚筒挂在胸口下，固定角度会被视角俯仰
    /// 放大成数厘米的悬空或陷地，这里用世界坐标闭环验收运行时俯仰修正。
    /// </summary>
    public class RollerGroundContactPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private static readonly BindingFlags PrivateInstance =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator Rolling_DrumBottomStaysOnGround()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");

            // 关掉外部输入与 Bot，避免线上玩家/Bot 干扰姿态与位置
            player.enabled = false;
            foreach (var bot in UnityEngine.Object.FindObjectsByType<P1.AI.BotController>(FindObjectsInactive.Exclude))
            {
                bot.enabled = false;
            }

            var weapon = player.Weapon;
            var motor = player.Motor;
            var visual = player.GetComponentInChildren<CharacterVisualController>();
            Assert.IsNotNull(weapon);
            Assert.IsNotNull(motor);
            Assert.IsNotNull(visual);

            // 站到主地面，保证脚下与滚筒前方都是同一块平地
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0f, 0.2f, -11f);
            if (cc != null) cc.enabled = true;
            yield return null;

            Transform roller = visual.WeaponRoot.transform.Find("Swell_Roller");
            Assert.IsNotNull(roller, "应能找到 Swell_Roller 模型");
            Transform drum = roller.Find("RollerDrum");
            Assert.IsNotNull(drum, "应能找到 RollerDrum 滚轴");

            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            try
            {
                config.weaponKind = WeaponKind.Roller;
                config.flickWindup = 0.05f;          // 缩短前摇，测试不用等满 0.22s
                config.flickInkCost = 0f;
                config.rollMinInkToStart = 0f;
                config.rollInkPerMeter = 0f;         // 关掉耗墨，保持推地状态稳定
                config.rollWidth = 1.9f;
                config.rollForwardReach = 1.35f;
                config.rollDamage = 140f;
                config.rollDamageInterval = 0.5f;
                config.rollPaintRadius = 0.34f;

                weapon.EquipWeapon(config);
                weapon.ResetAllWeaponState();
                motor.RefillInkFull();
                yield return null;

                // 1) 默认视角按住推地
                yield return HoldAndWait(player, weapon, motor, 150);
                Assert.IsTrue(weapon.IsRolling, "按住扳机且贴地时应处于推地状态");
                Assert.IsTrue(motor.IsGrounded, "测试期间角色应保持贴地");
                AssertContact(player, motor, visual, drum, roller, "默认视角");
                CaptureCamera("roller_roll_pose.png");

                // 侧面视角留底：正面/背面会被蛋形身体挡住滚轴，无法核对贴地
                var follow = TopDownCameraFollow.Instance;
                if (follow != null)
                {
                    follow.AddLookInput(90f, 0f);
                    for (int i = 0; i < 12; i++)
                    {
                        motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                        yield return null;
                    }
                    CaptureCamera("roller_roll_pose_side.png");
                    follow.AddLookInput(-90f, 0f);
                    yield return null;
                }
                yield return null;

                // 2) 抬头 30°：胸口随视角后仰，长杠杆会把滚筒抬起（旧实现的悬空根因）
                visual.SetAimPitch(-30f);
                yield return HoldAndWait(player, weapon, motor, 45);
                AssertContact(player, motor, visual, drum, roller, "抬头30°");
                CaptureCamera("roller_roll_pose_up.png");
                yield return null;

                // 3) 低头 30°：胸口前压，长杠杆会把滚筒压进地面
                visual.SetAimPitch(30f);
                yield return HoldAndWait(player, weapon, motor, 45);
                AssertContact(player, motor, visual, drum, roller, "低头30°");
                CaptureCamera("roller_roll_pose_down.png");
                yield return null;

                // 4) 松开后单击一次挥击：砸地随挥段应真正落到地面
                visual.SetAimPitch(0f);
                for (int i = 0; i < 25; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                Assert.IsFalse(weapon.IsRolling, "松开扳机后应退出推地状态");

                weapon.TryFireMainWeapon(
                    player.transform.forward,
                    player.transform.position + player.transform.forward * 4f);

                float minGap = float.PositiveInfinity;
                for (int i = 0; i < 60; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                    minGap = Mathf.Min(minGap, MeasureGap(player, motor, drum));
                }
                Debug.Log($"[ROLLGAP] flick slam minGap={minGap:F4}m");
                Assert.Less(minGap, 0.026f, $"挥击砸地时滚筒应触地，最近间隙 {minGap:F3}m");
                Assert.Greater(minGap, -0.04f, $"挥击砸地时滚筒不应明显陷地，最近间隙 {minGap:F3}m");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(config);
            }
        }

        private static IEnumerator HoldAndWait(
            PlayerController player, WeaponController weapon, InkCharacterMotor motor, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                weapon.TryFireMainWeapon(
                    player.transform.forward,
                    player.transform.position + player.transform.forward * 4f);
                yield return null;
            }
        }

        private static void AssertContact(
            PlayerController player, InkCharacterMotor motor, CharacterVisualController visual,
            Transform drum, Transform roller, string label)
        {
            float gap = MeasureGap(player, motor, drum);
            float weight = ReadPrivate<float>(visual, "_rollerContactWeight");
            float contact01 = ReadPrivate<float>(visual, "_rollerPoseContact01");
            Transform chestT = visual.KidRoot.transform.Find("Chest") ?? visual.KidRoot.transform.Find("Hips/BodySquash/Spine/Chest");
            float chestPitch = chestT != null ? chestT.localEulerAngles.x : 0f;
            if (chestPitch > 180f) chestPitch -= 360f;

            Debug.Log($"[ROLLGAP] {label} gap={gap:F4}m contact={weight:F3}/{contact01:F3} " +
                      $"chestPitch={chestPitch:F2} drumY={drum.position.y:F4} " +
                      $"modelPitch={roller.localEulerAngles.x:F2}");

            Assert.Less(gap, 0.015f, $"{label}：滚筒底缘不应悬空，实测间隙 {gap:F3}m");
            Assert.Greater(gap, -0.03f, $"{label}：滚筒不应明显陷入地面，实测间隙 {gap:F3}m");
        }

        private static float MeasureGap(
            PlayerController player, InkCharacterMotor motor, Transform drum)
        {
            float skin = motor.Config != null ? motor.Config.controllerSkinWidth : 0.006f;
            float groundY = player.transform.position.y - skin;
            return drum.position.y - RollerWieldSolver.DrumRadiusMeters - groundY;
        }

        private static void CaptureCamera(string fileName)
        {
            try
            {
                var follow = TopDownCameraFollow.Instance;
                Camera cam = follow != null ? follow.GetComponent<Camera>() : Camera.main;
                if (cam == null)
                {
                    Debug.LogWarning("[SHOT] 找不到游戏相机，跳过截图");
                    return;
                }

                const int width = 1280;
                const int height = 720;
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                RenderTexture previous = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = previous;

                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                UnityEngine.Object.Destroy(rt);

                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs", fileName));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, png);
                Debug.Log("[SHOT] " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHOT] 截图失败（不影响贴地断言）: " + e.Message);
            }
        }

        private static T ReadPrivate<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.IsNotNull(field, $"缺少字段 {fieldName}");
            return (T)field.GetValue(target);
        }
    }
}