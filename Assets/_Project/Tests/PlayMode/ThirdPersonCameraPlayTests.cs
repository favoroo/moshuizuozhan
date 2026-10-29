using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// P9 第三人称运行时验收：
    /// 1. 场景相机为透视、位于玩家后方、吊臂距离合理；
    /// 2. AddLookInput 立即改变偏航；
    /// 3. 进入 Playing 后瞄准点位于屏幕中心射线前方（相机中心线即真理）；
    /// 4. 屏幕准星根节点锁定在画布中心锚点。
    /// </summary>
    public class ThirdPersonCameraPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        [UnityTest]
        public IEnumerator SceneCamera_IsThirdPerson_AndAimFollowsCameraCenterRay()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");

            var follow = TopDownCameraFollow.Instance;
            Assert.IsNotNull(follow, "场景中应存在第三人称相机");

            Camera cam = follow.CameraComponent;
            Assert.IsNotNull(cam, "相机组件不得为空");
            Assert.IsFalse(cam.orthographic, "P9 起相机必须是透视模式");
            Assert.Greater(cam.fieldOfView, 40f, "第三人称 FOV 应明显大于旧俯视 40°");

            Assert.Greater(follow.CurrentDistance, 2f, "吊臂不得贴脸");
            Assert.Less(follow.CurrentDistance, 6f, "吊臂不得过长");

            Vector3 toCamera = cam.transform.position - player.transform.position;
            toCamera.y = 0f;
            Assert.Less(Vector3.Dot(toCamera.normalized, follow.PlanarForward), -0.5f,
                "相机应位于玩家身后（而不是头顶或身前）");

            float yawBefore = follow.YawDegrees;
            follow.AddLookInput(45f, 0f);
            float expectedYaw = Mathf.Repeat(yawBefore + 45f + 180f, 360f) - 180f;
            Assert.AreEqual(expectedYaw, follow.YawDegrees, 0.01f, "AddLookInput 应即时改变水平朝向");

            // 进入 Playing，让 PlayerController 每帧刷新 AimPoint
            MatchManager match = MatchManager.Instance != null
                ? MatchManager.Instance
                : Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(match, "场景中应存在 MatchManager");

            var setPhase = typeof(MatchManager).GetMethod(
                "SetPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(setPhase, "MatchManager 必须具备 SetPhase 私有方法");
            setPhase.Invoke(match, new object[] { MatchManager.Phase.Playing });

            yield return null;
            yield return null;

            Vector3 toAim = player.AimPoint - player.transform.position;
            Assert.Greater(toAim.magnitude, 0.1f, "瞄准点不应与玩家重合");
            Assert.Greater(Vector3.Dot(toAim.normalized, follow.LookForward), 0.5f,
                "瞄准点必须落在屏幕中心射线前方");

            var reticle = Object.FindAnyObjectByType<WeaponReticleUI>();
            Assert.IsNotNull(reticle, "HUD 应创建屏幕准星");

            var reticleRect = (RectTransform)reticle.transform;
            Assert.AreEqual(0.5f, reticleRect.anchorMin.x, 1e-4f, "准星应锚定屏幕横向中心");
            Assert.AreEqual(0.5f, reticleRect.anchorMin.y, 1e-4f, "准星应锚定屏幕纵向中心");
            Assert.AreEqual(0.5f, reticleRect.anchorMax.x, 1e-4f);
            Assert.AreEqual(0.5f, reticleRect.anchorMax.y, 1e-4f);
            Assert.AreEqual(0f, reticleRect.anchoredPosition.x, 1e-3f);
            Assert.AreEqual(0f, reticleRect.anchoredPosition.y, 1e-3f);
        }
    }
}
