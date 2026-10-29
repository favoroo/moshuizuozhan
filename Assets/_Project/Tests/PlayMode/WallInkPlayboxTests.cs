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
    /// 墙面涂地 / 潜行上墙的运行时集成测试：直接加载 PhaseA_Playbox，
    /// 验证掩体墙立面可涂、爆墨能波及墙面、玩家能贴着己方墨墙向上潜行。
    /// </summary>
    public class WallInkPlayboxTests
    {
        const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");
        }

        private static InkWallSurface FindCoverWall()
        {
            GameObject wallGo = GameObject.Find("CoverWall_00");
            Assert.IsNotNull(wallGo, "场景里应存在掩体墙 CoverWall_00");
            var wall = wallGo.GetComponent<InkWallSurface>();
            Assert.IsNotNull(wall, "掩体墙应挂载 InkWallSurface 组件");
            return wall;
        }

        [UnityTest]
        public IEnumerator CoverWall_GeneratesFourPaintableFaces()
        {
            yield return LoadPlaybox();

            InkWallSurface wall = FindCoverWall();
            Assert.IsTrue(wall.InitializeIfNeeded(), "运行时应能初始化墙面");
            Assert.AreEqual(4, wall.FaceCount, "盒子墙应生成 4 张竖直立面");
            Assert.Greater(wall.TopY, 1.2f, "掩体墙顶部应高于地面");
        }

        [UnityTest]
        public IEnumerator PaintingWall_StoresInkOnlyOnHitFace()
        {
            yield return LoadPlaybox();

            InkWallSurface wall = FindCoverWall();
            Vector3 southFace = new Vector3(wall.WorldBounds.center.x, 0.9f, wall.WorldBounds.min.z);

            Assert.AreEqual(Team.Neutral, wall.InkAtWorld(southFace, Vector3.back));

            int changed = wall.PaintAtWorld(southFace, Vector3.back, 0.9f, Team.TeamA);

            Assert.Greater(changed, 0, "墙面命中应涂上墨水");
            Assert.AreEqual(Team.TeamA, wall.InkAtWorld(southFace, Vector3.back));

            Vector3 northFace = new Vector3(southFace.x, 0.9f, wall.WorldBounds.max.z);
            Assert.AreEqual(Team.Neutral, wall.InkAtWorld(northFace, Vector3.forward),
                "只涂一面时，背面不应同时出现墨水");
        }

        [UnityTest]
        public IEnumerator BombSplash_PaintsGroundAndNearbyWall()
        {
            yield return LoadPlaybox();

            InkWallSurface wall = FindCoverWall();
            Vector3 blastCenter = new Vector3(wall.WorldBounds.center.x, 0.15f, wall.WorldBounds.min.z - 0.4f);

            int changed = InkSurfaceController.Instance.PaintSphereWithWalls(blastCenter, 3f, Team.TeamB);

            Assert.Greater(changed, 0, "爆墨应同时涂到地面或墙面");
            Assert.AreEqual(Team.TeamB,
                wall.InkAtWorld(new Vector3(blastCenter.x, 0.7f, wall.WorldBounds.min.z), Vector3.back),
                "靠近爆炸点的墙面应被涂上墨水");
        }

        [UnityTest]
        public IEnumerator ProjectileHit_PaintsWallFace()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false;
            yield return null;

            InkWallSurface wall = FindCoverWall();
            Vector3 target = new Vector3(wall.WorldBounds.center.x, 1.0f, wall.WorldBounds.min.z);
            Assert.AreEqual(Team.Neutral, wall.InkAtWorld(target, Vector3.back));

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(wall.WorldBounds.center.x, 0.08f, wall.WorldBounds.min.z - 2.2f);
            if (cc != null) cc.enabled = true;
            yield return null;

            Vector3 aim = target - player.transform.position;
            aim.y = 0f;
            aim.Normalize();

            Assert.IsTrue(player.Weapon.TryFireMainWeapon(aim), "墨水充足时应能开火");

            yield return new WaitForSeconds(1.0f);

            Assert.AreEqual(Team.TeamA, wall.InkAtWorld(target, Vector3.back),
                "主武器弹丸命中墙面时应在墙面上留下墨水");
        }

        [UnityTest]
        public IEnumerator Player_SwimsUpOwnInkWall_AndGetsHigher()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false; // 关闭输入驱动，改用 SetFrameIntent 注入意图
            yield return null;

            InkWallSurface wall = FindCoverWall();
            Vector3 southFace = new Vector3(wall.WorldBounds.center.x, 0.9f, wall.WorldBounds.min.z);

            // 以较大半径把整面墙涂成本队墨，保证爬升全程都能查到己方墨水
            wall.PaintAtWorld(southFace, Vector3.back, 2.0f, Team.TeamA);
            Assert.AreEqual(Team.TeamA, wall.InkAtWorld(southFace, Vector3.back));

            var motor = player.Motor;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(
                wall.WorldBounds.center.x, 0.08f, wall.WorldBounds.min.z - 0.62f);
            if (cc != null) cc.enabled = true;
            yield return null;
            yield return null;

            float startY = player.transform.position.y;

            // 推向墙面 + 按住潜行 → 应贴着己方墨墙向上潜行
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, true, false, false);

            bool climbed = false;
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (motor.IsWallClimbing) climbed = true;
            }

            Assert.IsTrue(climbed, "贴着己方墨墙按住潜行键时应进入墙面攀爬状态");
            Assert.Greater(player.transform.position.y, startY + 0.5f,
                $"应沿墙面上爬，实际 Y={player.transform.position.y:F2}，起点 Y={startY:F2}");
        }
    }
}