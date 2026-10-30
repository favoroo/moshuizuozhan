using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Environment;
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

        [UnityTest]
        public IEnumerator DecorProp_SideFace_AcceptsInk()
        {
            yield return LoadPlaybox();

            InkWallSurface propWall = null;
            var walls = Object.FindObjectsByType<InkWallSurface>(FindObjectsSortMode.None);
            for (int i = 0; i < walls.Length; i++)
            {
                var candidate = walls[i];
                if (candidate == null || !candidate.gameObject.name.StartsWith("PropCollider_"))
                {
                    continue;
                }

                // 找一个未旋转的道具，便于用世界 +Z 作为命中法线
                if (Mathf.Abs(Mathf.DeltaAngle(0f, candidate.transform.eulerAngles.y)) > 0.5f)
                {
                    continue;
                }

                propWall = candidate;
                break;
            }

            Assert.IsNotNull(propWall, "场景里应有装饰道具盒代理（PropCollider_*），且至少一个朝向与世界轴对齐");
            Assert.IsTrue(propWall.InitializeIfNeeded(), "运行时应能初始化道具立面的墨格");
            Assert.AreEqual(4, propWall.FaceCount, "实体道具应生成 4 张可涂立面");

            Bounds bounds = propWall.WorldBounds;
            Vector3 point = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
            Assert.AreEqual(Team.Neutral, propWall.InkAtWorld(point, Vector3.forward), "初始应无墨");

            int changed = propWall.PaintAtWorld(point, Vector3.forward, 0.4f, Team.TeamA);
            Assert.Greater(changed, 0, "射击装饰木箱/花坛的侧面应能落墨");
            Assert.AreEqual(Team.TeamA, propWall.InkAtWorld(point, Vector3.forward), "命中面应变为 TeamA 墨");
        }

        /// <summary>
        /// 回归"高台/坡道永远喷不上油漆"：这些表面都挂了 OcclusionFader，
        /// 恢复不透明时曾用 SetPropertyBlock(null) 把墨迹图集绑定整块抹掉，
        /// 于是相机穿过它们一次之后，那片台面就再也显示不出墨迹。
        /// </summary>
        [UnityTest]
        public IEnumerator ElevatedPlatform_KeepsInkBindingAndPaints_AfterOcclusionRestore()
        {
            yield return LoadPlaybox();

            GameObject platform = GameObject.Find("HighGround_West");
            Assert.IsNotNull(platform, "场景里应存在 HighGround_West 高台");

            var renderer = platform.GetComponent<Renderer>();
            Assert.IsNotNull(renderer, "高台应有 Renderer");

            var fader = platform.GetComponent<OcclusionFader>();
            Assert.IsNotNull(fader, "高台应挂载 OcclusionFader（这是本 bug 的触发条件）");

            // 走一遍"被遮挡 → 恢复不透明"的完整路径（历史实现就是在这里清空 MPB）
            fader.MarkOccluded(platform.transform.position);
            for (int i = 0; i < 8; i++)
            {
                fader.MarkOccluded(platform.transform.position);
                fader.EvaluateStep(0.08f);
                yield return null;
            }
            Assert.Less(fader.CurrentAlpha, 0.99f, "应已淡出");

            fader.ResetToOpaque();
            yield return null;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.IsTrue(renderer.HasPropertyBlock(), "恢复不透明后高台应仍持有 MaterialPropertyBlock");
            Assert.IsNotNull(block.GetTexture(Shader.PropertyToID("_InkTex")),
                "恢复不透明不得抹掉墨迹图集绑定，否则该台面永远喷不上油漆");

            // 台面本身必须仍然可涂：玩法网格与图集都要接受落墨
            var ink = InkSurfaceController.Instance;
            Assert.IsNotNull(ink, "场景里应存在 InkSurfaceController");
            Vector3 topCenter = platform.transform.position + Vector3.up * 0.46f;

            int splatsBefore = ink.SplatAtlas != null ? ink.SplatAtlas.TotalSplatCount : 0;
            int changed = ink.PaintCircle(topCenter, 2.0f, Team.TeamA);
            yield return null;

            Assert.Greater(changed, 0, "高台顶面应能落墨（玩法网格）");
            Assert.AreEqual(Team.TeamA, ink.InkAt(topCenter), "高台顶面应记为 TeamA 墨");
            if (ink.SplatAtlas != null)
            {
                Assert.Greater(ink.SplatAtlas.TotalSplatCount, splatsBefore,
                    "落墨应同时进入 GPU 墨迹图集（视觉层）");
            }
        }
    }
}
