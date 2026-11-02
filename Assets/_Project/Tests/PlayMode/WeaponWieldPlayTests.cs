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
    /// P35 七武器持握/动作实机验收：
    /// 1. 逐把武器触发蓄力/泼洒/翻滚/架枪/推地，断言 CharacterVisualController 的动作层帧值方向正确；
    /// 2. 从角色正前方 3/4 视角截图到 Logs/weapon_pose/，供人工核对持枪剪影与滚筒贴地。
    /// 只读姿态，不改伤害/射程/耗墨/涂地。
    ///
    /// 注意：PlayMode 测试机的帧率可能远低于 60fps，所有短动作都不能用固定帧数采样；
    /// 这里一律按武器状态（IsCharging/IsWindingUp/IsRollingDodge/IsTurretLocked/IsStreaming/IsRolling）
    /// 等待，并在动作峰值窗口内读取/截图。
    /// </summary>
    public class WeaponWieldPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 720;

        private static readonly BindingFlags PrivateInstance =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator AllSevenWeapons_ActionPoses_ChangeFrameValues_AndCapture()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");

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

            // 站到主地面开阔处；镜头绕到角色正前方 3/4 视角，武器与滚筒不再被身体完全挡住
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0f, 0.2f, -11f);
            if (cc != null) cc.enabled = true;
            yield return null;

            var follow = TopDownCameraFollow.Instance;
            if (follow != null)
            {
                follow.AddLookInput(160f, 0f);
                for (int i = 0; i < 18; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
            }

            WeaponConfigSO shooter = null;
            WeaponConfigSO charger = null;
            WeaponConfigSO dualies = null;
            WeaponConfigSO slosher = null;
            WeaponConfigSO blaster = null;
            WeaponConfigSO splatling = null;
            WeaponConfigSO roller = null;
            try
            {
                // ---- Shooter：持续射击前压 + 回拉 ----
                shooter = MakeConfig(WeaponKind.Shooter);
                yield return EquipAndSettle(player, weapon, motor, shooter);
                float maxBodyPitch = 0f;
                float minForward = 0f;
                for (int i = 0; i < 18; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 6f);
                    yield return null;

                    maxBodyPitch = Mathf.Max(maxBodyPitch, ReadPrivate<float>(visual, "_wieldBodyPitchDeg"));
                    minForward = Mathf.Min(minForward, ReadPrivate<float>(visual, "_wieldForward"));
                }
                Assert.Greater(maxBodyPitch, 0.5f, "小枪持续射击胸口应前压");
                Assert.Less(minForward, -0.001f, "小枪射击枪身应回拉");
                CaptureCamera("shooter_fire.png");
                yield return null;

                // ---- Charger：蓄力后拉/后仰，释放前推 ----
                charger = MakeConfig(WeaponKind.Charger);
                yield return EquipAndSettle(player, weapon, motor, charger);
                int guard = 0;
                while ((!weapon.IsCharging || weapon.Charge01 < 0.45f) && guard++ < 120)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 10f);
                    yield return null;
                }
                Assert.IsTrue(weapon.IsCharging, "蓄力狙按住应进入蓄力");
                Assert.Greater(weapon.Charge01, 0.4f, "蓄力进度应超过 40%");
                Assert.Less(ReadPrivate<float>(visual, "_wieldForward"), -0.015f, "蓄力时枪身后拉");
                // 采样点可能刚好越过 45% 蓄力门槛，幅度断言放宽到"明确后仰"即可；满蓄力幅度由 EditMode 求解层锁定。
                Assert.Less(ReadPrivate<float>(visual, "_wieldBodyPitchDeg"), -1f, "蓄力时胸口后仰");
                CaptureCamera("charger_charge.png");

                // 松手：策略会在武器 LateUpdate 里发射，视觉层释放动作同帧触发
                guard = 0;
                while (weapon.IsCharging && guard++ < 20)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                Assert.IsFalse(weapon.IsCharging, "松手后应退出蓄力");
                yield return null;
                CaptureCamera("charger_release.png");
                yield return null;

                // ---- Slosher：前摇后仰 -> 甩出前倾 ----
                slosher = MakeConfig(WeaponKind.Slosher);
                yield return EquipAndSettle(player, weapon, motor, slosher);
                weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 5f);
                // 前摇/甩出都是短时间线：逐帧采样取最小/最大俯仰。
                // 注意不要在采样循环内截图——PNG 编码会阻塞主线程造成 0.1~0.2s 长帧，
                // 把 0.24s 的甩出动作直接跳过头。
                float slosherMinPitch = float.MaxValue;
                float slosherMaxPitch = float.MinValue;
                for (int i = 0; i < 30; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;

                    float pitch = ReadPrivate<float>(visual, "_wieldPitchDeg");
                    slosherMinPitch = Mathf.Min(slosherMinPitch, pitch);
                    slosherMaxPitch = Mathf.Max(slosherMaxPitch, pitch);
                }
                Assert.Less(slosherMinPitch, -6f, "泼桶前摇桶身后仰");
                Assert.Greater(slosherMaxPitch, 8f, "泼桶甩出桶身前倾");

                // 截图 pass：等冷却结束后，分别停在“前摇中”和“甩出后第一帧”截图。
                guard = 0;
                while (!weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 5f)
                       && guard++ < 90)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                yield return null;
                CaptureCamera("slosher_windup.png");

                guard = 0;
                while (weapon.IsWindingUp && guard++ < 40)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                yield return null;
                CaptureCamera("slosher_throw.png");
                yield return null;

                // ---- Blaster：开火后坐回拉 + 枪口上抬 ----
                blaster = MakeConfig(WeaponKind.Blaster);
                yield return EquipAndSettle(player, weapon, motor, blaster);
                guard = 0;
                while (!weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 6f)
                       && guard++ < 40)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                yield return null;
                Assert.Less(ReadPrivate<float>(visual, "_wieldForward"), -0.03f, "爆破开火应后坐");
                Assert.Less(ReadPrivate<float>(visual, "_wieldPitchDeg"), -6f, "爆破开火枪口应上抬");
                CaptureCamera("blaster_fire.png");
                yield return null;

                // ---- Dualies：翻滚侧倾/下沉 -> 架枪前压/下沉 ----
                dualies = MakeConfig(WeaponKind.Dualies);
                // 测试机帧率可能低到 5~10fps：把翻滚/架枪窗口拉长，避免一帧 dt 直接跨过 0.3s 状态。
                dualies.dualiesRollDuration = 1.2f;
                dualies.dualiesTurretDuration = 1.5f;
                yield return EquipAndSettle(player, weapon, motor, dualies);
                // 必须沿角色自身右方翻滚：世界 +X 未必等于角色右方，Dot 为 0 时求解层不会给侧倾。
                Assert.IsTrue(weapon.TryDodgeRoll(player.transform.right), "双枪应能触发战术翻滚");
                float maxDodgeRoll = 0f;
                float maxDodgeSink = 0f;
                for (int i = 0; i < 20; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;

                    maxDodgeRoll = Mathf.Max(maxDodgeRoll, ReadPrivate<float>(visual, "_wieldBodyRollDeg"));
                    maxDodgeSink = Mathf.Max(maxDodgeSink, ReadPrivate<float>(visual, "_wieldSink01"));
                    if (i == 1)
                        CaptureCamera("dualies_dodge.png");
                }
                Assert.Greater(maxDodgeRoll, 2f, "翻滚应侧倾");
                Assert.Greater(maxDodgeSink, 0.01f, "翻滚应下沉");

                guard = 0;
                while (!weapon.IsTurretLocked && guard++ < 90)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                Assert.IsTrue(weapon.IsTurretLocked, "翻滚结束应进入架枪");
                float maxTurretSink = 0f;
                float maxTurretPitch = 0f;
                for (int i = 0; i < 12; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;

                    maxTurretSink = Mathf.Max(maxTurretSink, ReadPrivate<float>(visual, "_wieldSink01"));
                    maxTurretPitch = Mathf.Max(maxTurretPitch, ReadPrivate<float>(visual, "_wieldBodyPitchDeg"));
                    if (i == 0)
                        CaptureCamera("dualies_turret.png");
                }
                Assert.Greater(maxTurretSink, 0.03f, "架枪应下沉");
                Assert.Greater(maxTurretPitch, 3f, "架枪胸口应前压");
                yield return null;

                // ---- Splatling：蓄力下压/前压 -> 扫射抖动 ----
                splatling = MakeConfig(WeaponKind.Splatling);
                yield return EquipAndSettle(player, weapon, motor, splatling);
                guard = 0;
                while ((!weapon.IsCharging || weapon.Charge01 < 0.4f) && guard++ < 120)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 8f);
                    yield return null;
                }
                Assert.IsTrue(weapon.IsCharging, "加特林按住应进入蓄力");
                Assert.Greater(ReadPrivate<float>(visual, "_wieldDrop"), 0.006f, "加特林蓄力应下压");
                CaptureCamera("splatling_charge.png");

                guard = 0;
                while (!weapon.IsStreaming && guard++ < 20)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    yield return null;
                }
                Assert.IsTrue(weapon.IsStreaming, "松手应进入扫射");
                yield return null;
                Assert.Greater(ReadPrivate<float>(visual, "_splatlingSpinDegPerSec"), 500f, "扫射枪管应高速旋转");
                CaptureCamera("splatling_stream.png");
                yield return null;

                // ---- Roller：按住推地，滚轴贴地 + 推地姿态 ----
                roller = MakeConfig(WeaponKind.Roller);
                roller.flickWindup = 0.05f;
                roller.flickInkCost = 0f;
                roller.rollInkPerMeter = 0f;
                roller.rollMinInkToStart = 0f;
                yield return EquipAndSettle(player, weapon, motor, roller);
                guard = 0;
                while (!weapon.IsRolling && guard++ < 160)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 4f);
                    yield return null;
                }
                Assert.IsTrue(weapon.IsRolling, "按住扳机且贴地应进入推地");
                for (int i = 0; i < 12; i++)
                {
                    motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                    weapon.TryFireMainWeapon(player.transform.forward, player.transform.position + player.transform.forward * 4f);
                    yield return null;
                }
                Assert.Greater(ReadPrivate<float>(visual, "_rollerContactWeight"), 0.9f, "推地时贴地闭环应满权重");
                Assert.Greater(ReadPrivate<float>(visual, "_rollerRollWeight"), 0.8f, "推地姿态权重应到位");
                CaptureCamera("roller_roll_front.png");
                yield return null;
            }
            finally
            {
                DestroyConfig(shooter);
                DestroyConfig(charger);
                DestroyConfig(dualies);
                DestroyConfig(slosher);
                DestroyConfig(blaster);
                DestroyConfig(splatling);
                DestroyConfig(roller);
            }
        }

        private static IEnumerator EquipAndSettle(
            PlayerController player, WeaponController weapon,
            InkCharacterMotor motor, WeaponConfigSO config)
        {
            weapon.EquipWeapon(config);
            weapon.ResetAllWeaponState();
            motor.RefillInkFull();
            for (int i = 0; i < 4; i++)
            {
                motor.SetFrameIntent(Vector3.zero, player.transform.forward, false, false);
                yield return null;
            }
        }

        private static WeaponConfigSO MakeConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = kind;
            config.bombInkCost = 0f;
            config.flickInkCost = 0f;
            config.rollInkPerMeter = 0f;
            config.rollMinInkToStart = 0f;
            return config;
        }

        private static void DestroyConfig(WeaponConfigSO config)
        {
            if (config != null)
                ScriptableObject.DestroyImmediate(config);
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

                var rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
                RenderTexture previous = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = previous;

                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                UnityEngine.Object.Destroy(rt);

                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/weapon_pose"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, fileName);
                File.WriteAllBytes(path, png);
                Debug.Log("[SHOT] " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHOT] 截图失败（不影响姿态断言）: " + e.Message);
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