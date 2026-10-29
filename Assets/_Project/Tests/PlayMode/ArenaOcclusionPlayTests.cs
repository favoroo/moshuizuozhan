using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Environment;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    [TestFixture]
    public class ArenaOcclusionPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        [UnityTest]
        public IEnumerator Arena_ContainersAndHighGround_FadeWhenPlayerOccluded()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");

            var container = GameObject.Find("Container_Mid_SW_Mustard");
            Assert.IsNotNull(container, "场景中应存在 Container_Mid_SW_Mustard 集装箱掩体");

            var fader = container.GetComponent<OcclusionFader>();
            Assert.IsNotNull(fader, "集装箱掩体应挂载 OcclusionFader");

            // 1. 将玩家移至集装箱正后方（遮挡相机视角）
            // 集装箱位于 (-8.0, 0.9, -5.5)，玩家放置在其北侧 (-8.0, 0.0, -2.5)
            player.transform.position = new Vector3(-8.0f, 0.05f, -2.5f);
            Physics.SyncTransforms();

            // 等待若干物理与渲染帧，让 TopDownOcclusionManager 驱动淡化。
            // 相机跟随玩家需要时间收敛，因此按目标透明度轮询（上限 2.5s），
            // 而不是写死 30 帧——不同帧率/加载状态下 30 帧并不等价于“淡出完成”。
            float fadeDeadline = Time.time + 2.5f;
            while (Time.time < fadeDeadline && fader.CurrentAlpha > 0.4501f)
            {
                yield return null;
            }

            Assert.IsTrue(fader.IsOccluding, "玩家在集装箱后方时，掩体必须标记为遮挡");
            Assert.LessOrEqual(fader.CurrentAlpha, 0.45f, "遮挡时透明度必须平滑淡化至 ≤ 0.45（目标 0.35）");

            // 2. 玩家移出掩体后方
            player.transform.position = new Vector3(0f, 0.05f, -12.0f);
            Physics.SyncTransforms();

            float restoreDeadline = Time.time + 2.5f;
            while (Time.time < restoreDeadline && fader.IsOccluding)
            {
                yield return null;
            }

            Assert.IsFalse(fader.IsOccluding, "玩家移出掩体后，遮挡状态必须解除");
            Assert.GreaterOrEqual(fader.CurrentAlpha, 0.92f, "移出掩体后必须平滑恢复不透明");
        }
    }
}
