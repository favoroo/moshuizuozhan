using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.AI;
using P1.Environment;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 出生甲板 + 寻路回归：
    /// 1. 本地玩家开局站在 1.2m 高的出生甲板顶面（不是台前地面）；
    /// 2. Bot/玩家共用的 NavGrid 能从出生点找到场内路径，并且路径确实经两侧台阶下到地面。
    /// 如果台阶缺失或被 NavGrid 判成不可行走，Bot 会被困在台面上——这条用例就是防这个。
    /// </summary>
    public class SpawnDeckNavigationPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        [UnityTest]
        public IEnumerator PlayerSpawnsOnDeck_AndNavGridCanDescendToArena()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");

            Assert.Greater(player.transform.position.y, ArenaLayoutV2.SpawnDeckTopY - 0.05f,
                "玩家应站在 1.2m 高的出生甲板顶面上");
            Assert.AreEqual(-ArenaLayoutV2.SpawnZ, player.transform.position.z, 0.2f,
                "玩家出生线应保持在 z=-23.5");

            var nav = ArenaNavigator.Instance;
            Assert.IsNotNull(nav, "场景里应存在 ArenaNavigator");
            Assert.IsNotNull(nav.Grid, "NavGrid 必须已完成采样");

            var path = new List<Vector3>();
            bool found = nav.Grid.TryFindPath(player.transform.position, new Vector3(0f, 0f, -12f), path);
            Assert.IsTrue(found, "出生甲板必须有通往场内的可行走路径（两侧台阶）");
            Assert.Greater(path.Count, 2, "路径不应只有出生点本身");

            bool stayedOnDeck = false;
            bool descendedToGround = false;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].y > ArenaLayoutV2.SpawnDeckTopY - 0.05f) stayedOnDeck = true;
                if (path[i].y < 0.5f) descendedToGround = true;
            }

            Assert.IsTrue(stayedOnDeck, "路径起点应落在台面上");
            Assert.IsTrue(descendedToGround, "路径必须经台阶下到地面（否则 Bot 会被困在台面上）");

            // 人工验收用的一次性截图：只有显式设置 P1_CAPTURE_SPAWN=1 才写文件，常规 CI 不产生截图。
            if (System.Environment.GetEnvironmentVariable("P1_CAPTURE_SPAWN") == "1")
            {
                ScreenCapture.CaptureScreenshot("Logs/spawn_deck_view.png");
                yield return new WaitForSeconds(0.4f);
            }
        }
    }
}