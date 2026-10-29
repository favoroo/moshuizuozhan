using System.Collections;
using NUnit.Framework;
using P1.Environment;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// P9 第三人称遮挡验收：
    /// 1. 集装箱在玩家背后时，相机球探针必须把吊臂收进，避免镜头穿进掩体；
    /// 2. OcclusionFader 的 Screen-Door Dither 淡入淡出仍能正常驱动（直接组件级验证）。
    /// 旧版「俯视相机隔墙看玩家 → 掩体淡化」的用例前提已被相机防穿墙取代，故按新语义重写。
    /// </summary>
    [TestFixture]
    public class ArenaOcclusionPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        [UnityTest]
        public IEnumerator Arena_CameraBoomPullsInWhenContainerBehindPlayer()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");

            var cameraFollow = TopDownCameraFollow.Instance;
            Assert.IsNotNull(cameraFollow, "场景中应存在第三人称相机");
            Assert.IsNotNull(cameraFollow.CameraComponent, "相机组件不得为空");

            // 固定镜头朝向为世界 +Z（yaw=0），避免出生朝向影响探针方向
            cameraFollow.AddLookInput(-cameraFollow.YawDegrees, 0f);

            // Container_Mid_SW_Mustard 位于 (-9.2, 0.9, -5.5)，尺寸 2.2×1.8×4.2（北面 z≈-3.4）。
            // 把玩家放到其北侧 0.75m 并背对镜头：镜头从玩家背后（南侧）看过来会撞上集装箱顶沿。
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(
                new Vector3(-9.2f, 0.05f, -2.65f), Quaternion.identity);
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();

            float deadline = Time.time + 3f;
            while (Time.time < deadline && cameraFollow.CurrentDistance > 2.5f)
            {
                yield return null;
            }

            Assert.Less(cameraFollow.CurrentDistance, 2.5f,
                "集装箱在玩家背后时，相机吊臂必须收进（不得保持默认 4.5m）");

            Vector3 camPos = cameraFollow.transform.position;
            Assert.Greater(camPos.z, -3.6f,
                "相机必须停在集装箱北面之前，不能穿进集装箱内部");
            Assert.Less(Vector3.Distance(camPos, player.transform.position), 4.2f,
                "相机收进后仍应贴近玩家，而不是飞到远处");
        }

        [UnityTest]
        public IEnumerator OcclusionFader_MarkedOccluded_FadesAndRestores()
        {
            var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.name = "TestOcclusionFaderCube";
            var fader = occluder.AddComponent<OcclusionFader>();
            yield return null;

            Assert.AreEqual(1f, fader.CurrentAlpha, 0.01f, "初始应为不透明");

            // 同一帧内连续标记遮挡并推进，应平滑淡化到 minAlpha（0.35）。
            // 不在循环里 yield：场景里的 TopDownOcclusionManager 会在每帧 LateUpdate 驱动所有 Fader，
            // 若不标记遮挡就会把透明度回拉，干扰纯组件级验证。
            for (int i = 0; i < 16; i++)
            {
                fader.MarkOccluded(Vector3.zero);
                fader.EvaluateStep(0.05f);
            }

            Assert.LessOrEqual(fader.CurrentAlpha, 0.45f, "被遮挡时应淡化到 ≤0.45");

            // 停止标记后应平滑恢复不透明
            for (int i = 0; i < 16; i++)
            {
                fader.EvaluateStep(0.05f);
            }

            Assert.GreaterOrEqual(fader.CurrentAlpha, 0.92f, "离开遮挡后应恢复到 ≥0.92");

            Object.Destroy(occluder);
            yield return null;
        }
    }
}
