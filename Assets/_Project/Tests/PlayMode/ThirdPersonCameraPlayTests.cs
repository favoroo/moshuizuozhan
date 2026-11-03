using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.Weapons;
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

            // SafeArea 容器在刘海/挖孔屏上会内缩：准星必须直接挂画布根，否则静止瞄准也会偏。
            Canvas hudCanvas = reticle.GetComponentInParent<Canvas>();
            Assert.IsNotNull(hudCanvas, "准星应位于 HUD 画布内");
            Assert.AreEqual(hudCanvas.transform, reticle.transform.parent,
                "准星必须直接挂在 Canvas 根，不能留在 HudSafeArea 里");

            Vector3 reticleScreen = RectTransformUtility.WorldToScreenPoint(null, reticleRect.position);
            Assert.AreEqual(Screen.width * 0.5f, reticleScreen.x, 1f,
                "准星屏幕 X 必须等于屏幕正中心");
            Assert.AreEqual(Screen.height * 0.5f, reticleScreen.y, 1f,
                "准星屏幕 Y 必须等于屏幕正中心");

            // 甩枪 20°：本帧瞄准点必须仍落在渲染画面的屏幕中心射线上。
            // 旧实现用上一帧相机位置 + 本帧新朝向，20° 甩枪会偏出约 1m。
            follow.AddLookInput(20f, 0f);
            yield return null;

            Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 aimPoint = player.AimPoint;
            Vector3 closestOnRay = centerRay.GetPoint(
                Vector3.Dot(aimPoint - centerRay.origin, centerRay.direction));
            float missDistance = Vector3.Distance(aimPoint, closestOnRay);
            Assert.Less(missDistance, 0.15f,
                $"甩枪后瞄准点必须贴合屏幕中心射线，实际偏差 {missDistance:0.000}m");
        }

        /// <summary>
        /// Slam 镜头演出验收：角色视觉体升空 4.2m 时默认机位会把它整个甩出画面上方
        /// （顶点脚底投影 ≈1.17）。起跳必须把枢轴抬高 + 吊臂外拉 + FOV 加宽，
        /// 保证升空全程角色完整可见；落地后所有演出偏移必须归零回到基线。
        /// </summary>
        [UnityTest]
        public IEnumerator SlamPresentation_KeepsCharacterOnScreen_AndRestoresCamera()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");
            var follow = TopDownCameraFollow.Instance;
            Assert.IsNotNull(follow, "场景中应存在第三人称相机");
            var weapon = player.GetComponent<WeaponController>();
            Assert.IsNotNull(weapon, "本地玩家应挂 WeaponController");
            Assert.IsNotNull(weapon.Config, "本地玩家应有武器配置");

            // 存档可能把本地玩家换成 Storm 武器：克隆一份改成 Slam，保证用例与存档无关
            WeaponConfigSO slamConfig = Object.Instantiate(weapon.Config);
            slamConfig.specialKind = SpecialKind.Slam;
            weapon.EquipWeapon(slamConfig);

            MatchManager match = MatchManager.Instance != null
                ? MatchManager.Instance
                : Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(match, "场景中应存在 MatchManager");
            var setPhase = typeof(MatchManager).GetMethod(
                "SetPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(setPhase, "MatchManager 必须具备 SetPhase 私有方法");
            setPhase.Invoke(match, new object[] { MatchManager.Phase.Playing });
            yield return null;

            Camera cam = follow.CameraComponent;
            float baseDistance = follow.CurrentDistance;
            float baseFov = cam.fieldOfView;

            Transform kidRoot = player.Motor.VisualController.KidRoot.transform;
            Assert.IsNotNull(kidRoot, "角色应有程序化 Kid 视觉根节点");

            weapon.AddSpecialPointsRaw(weapon.SpecialRequired + 1f);
            Assert.IsTrue(weapon.IsSpecialReady, "直充后大招应就绪");
            Assert.IsTrue(weapon.TryActivateSpecial(), "Slam 应能成功释放");

            float maxWeight = 0f;
            float maxDistance = baseDistance;
            float maxFov = baseFov;
            float minAirborneFeetY = 1f;
            float maxHeadY = 0f;
            int frames = 0;

            while (weapon.IsSlamAirborne && frames < 600)
            {
                yield return null;
                frames++;

                maxWeight = Mathf.Max(maxWeight, follow.SlamPresentationWeight);
                maxDistance = Mathf.Max(maxDistance, follow.CurrentDistance);
                maxFov = Mathf.Max(maxFov, cam.fieldOfView);

                Vector3 feet = kidRoot.position;
                Vector3 head = feet + Vector3.up * CharacterMetrics.BodyHeight;
                Vector3 headViewport = cam.WorldToViewportPoint(head);
                if (headViewport.z > 0f)
                    maxHeadY = Mathf.Max(maxHeadY, headViewport.y);

                // 落地帧角色已回到地面，只要求头（上半身）在画面内；
                // 空中阶段要求整只脚也在画面内，否则「人跳到哪去了」问题回归。
                if (weapon.IsSlamAirborne)
                {
                    Vector3 feetViewport = cam.WorldToViewportPoint(feet);
                    if (feetViewport.z > 0f)
                        minAirborneFeetY = Mathf.Min(minAirborneFeetY, feetViewport.y);
                }
            }

            Assert.Greater(frames, 10, "Slam 演出应持续若干帧");
            Assert.Greater(maxWeight, 0.9f, "起跳应把镜头拉到完全展开");
            Assert.Greater(maxDistance, baseDistance + 1f, "起跳应把吊臂明显外拉");
            Assert.Greater(maxFov, baseFov + 1f, "起跳应加宽 FOV");
            Assert.Greater(minAirborneFeetY, 0f,
                $"空中角色脚底不得跑出画面下缘（最低 {minAirborneFeetY:0.00}）");
            Assert.Less(maxHeadY, 1f,
                $"空中角色头顶不得跑出画面上缘（最高 {maxHeadY:0.00}）");

            // 落地后演出权重与机位都要归零（Hitstop 与低帧率留 2s 真实时间余量）
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline && follow.SlamPresentationWeight > 0.02f)
                yield return null;
            yield return new WaitForSecondsRealtime(0.6f);

            Assert.Less(follow.SlamPresentationWeight, 0.02f, "落地演出结束后权重应归零");
            Assert.AreEqual(baseDistance, follow.CurrentDistance, 0.6f, "落地后吊臂应回到基线");
            Assert.AreEqual(baseFov, cam.fieldOfView, 1.5f, "落地后 FOV 应回到基线");

            Object.Destroy(slamConfig);
        }
    }
}
