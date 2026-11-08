using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// P41 开赛扫场镜头运行时验收（对标 INKWAVE `_intro()`）：
    /// 1. Countdown 期间相机必须离开玩家、升到高空并架在玩家面向的敌方半场一侧（不是原地不动的假镜头）；
    /// 2. Playing 首帧相机必须已经回到跟随机位（玩家身后 2~6m），且交接那一帧没有跳变；
    /// 3. 扫场结束后遮挡淡出恢复工作（管理器的 enabled 归位）。
    /// </summary>
    [Category("UI")]
    public class MatchIntroCameraPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = typeof(MatchManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"MatchManager.{fieldName} 字段缺失");
            field.SetValue(target, value);
        }

        [UnityTest]
        public IEnumerator MatchIntroCamera_SweepsHighOverArena_ThenHandsOffToFollow_WithoutPop()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var match = MatchManager.Instance;
            Assert.IsNotNull(match, "场景中应存在 MatchManager");
            var follow = TopDownCameraFollow.Instance;
            Assert.IsNotNull(follow, "场景中应存在第三人称相机");
            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");

            var director = MatchIntroCameraDirector.Instance;
            Assert.IsNotNull(director, "开赛扫场镜头导演应在运行时自动装配（RuntimeInitializeOnLoadMethod）");

            // 用例只关心扫场本身：终局定格压短、扫场压到 1.5s 以内，其余时序保持真实契约。
            PlayModeTestKit.ShortenMatchTimers(0.20f);
            SetPrivateField(match, "introCameraSeconds", 1.5f);

            Camera cam = follow.CameraComponent;
            // 必须在开赛前取引用：Countdown 首帧 HUD 内容根就会被 SetActive(false)，
            // 而 GameObject.Find 找不到未激活对象。
            GameObject hudSafeArea = GameObject.Find("HudSafeArea");
            Assert.IsNotNull(hudSafeArea, "HUD 应存在 HudSafeArea 内容根（HudController 运行时创建）");

            match.StartMatch();
            Assert.AreEqual(MatchManager.Phase.Countdown, match.CurrentPhase, "开赛应先进入入场演出阶段");

            float maxHeight = float.MinValue;
            float maxPlanarDistance = 0f;
            float maxEnemySideZ = float.MinValue;
            int sweptFrames = 0;
            bool sawSweepActive = false;
            bool sawHudContentHidden = false;
            Vector3 lastCountdownPosition = cam.transform.position;

            // 轮询整个 Countdown：记录最高点、离玩家最远距离、是否掠过敌方半场
            while (match.CurrentPhase == MatchManager.Phase.Countdown && sweptFrames < 600)
            {
                yield return null;
                sweptFrames++;

                Vector3 position = cam.transform.position;
                lastCountdownPosition = position;
                sawSweepActive |= director.IsSweepActive;

                // HUD 内容（SafeArea 容器）在扫场期间必须让位给电影机位
                if (sweptFrames > 3)
                {
                    sawHudContentHidden |= !hudSafeArea.activeSelf;
                }

                maxHeight = Mathf.Max(maxHeight, position.y);

                Vector3 planar = position - player.transform.position;
                planar.y = 0f;
                maxPlanarDistance = Mathf.Max(maxPlanarDistance, planar.magnitude);

                // 玩家朝 +Z（敌方半场在出生点前方）：扫场机位的 z 应明显大于出生点
                maxEnemySideZ = Mathf.Max(maxEnemySideZ, position.z);
            }

            Assert.AreEqual(MatchManager.Phase.Playing, match.CurrentPhase, "开赛演出应按时结束并进入 Playing");
            Assert.Greater(sweptFrames, 5, "扫场应持续若干帧，而不是一帧闪过");

            Assert.IsTrue(sawSweepActive, "Countdown 期间导演必须真的接管过相机");
            Assert.IsTrue(sawHudContentHidden, "扫场期间 HUD 内容（HudSafeArea）必须隐藏，不能盖住电影机位");
            Assert.Greater(maxHeight, 10f, $"扫场机位必须升到高空（实测最高 {maxHeight:0.0}m）");
            Assert.Greater(maxPlanarDistance, 6f, $"扫场机位必须离开玩家（实测最远 {maxPlanarDistance:0.0}m）");
            Assert.Greater(maxEnemySideZ, player.transform.position.z + 8f,
                $"扫场机位必须架在敌方半场一侧（实测最远 z={maxEnemySideZ:0.0}，玩家 z={player.transform.position.z:0.0}）");

            // Playing 首帧：跟随机位已接手（身后 2~6m，视线朝前）
            yield return null;
            yield return null;

            Vector3 toCamera = cam.transform.position - player.transform.position;
            toCamera.y = 0f;
            Assert.Less(toCamera.magnitude, 8f, "Playing 后相机必须回到玩家附近，不能还停在高空");
            Assert.Less(Vector3.Dot(toCamera.normalized, follow.PlanarForward), -0.5f,
                "Playing 后相机应位于玩家身后");

            // HUD 内容必须随 Playing 恢复，否则整局看不到计时器/墨水/小地图
            Assert.IsTrue(hudSafeArea.activeSelf, "Playing 首帧 HUD 内容必须恢复显示");

            // 交接无跳变：Countdown 最后一帧与 Playing 稳定后的机位差应远小于扫场尺度
            float handoffDelta = Vector3.Distance(lastCountdownPosition, cam.transform.position);
            Assert.Less(handoffDelta, 2.5f,
                $"扫场终点必须精确落在跟随机位，交接跳变过大（{handoffDelta:0.00}m）");

            // 扫场结束后遮挡淡出恢复：管理器重新 enabled，掩体不再冻在复位后的不透明态
            var occlusion = Object.FindAnyObjectByType<P1.Environment.TopDownOcclusionManager>();
            if (occlusion != null)
            {
                Assert.IsTrue(occlusion.enabled, "扫场结束后遮挡淡出管理器必须恢复 enabled");
            }
        }
    }
}
