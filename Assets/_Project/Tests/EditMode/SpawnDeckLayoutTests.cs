using System.Collections.Generic;
using NUnit.Framework;
using P1.Environment;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 出生甲板布局回归（对齐 INKWAVE 出生甲板）：
    /// 出生线站在 1.2m 高的台面上，队色出生盘随之上移，台面两侧各一条 5 级台阶负责上下。
    /// 台面/盘面/台阶必须保持可涂且不进 blockedColliders，否则出生区会变成"打不上墨"的死区。
    /// </summary>
    public class SpawnDeckLayoutTests
    {
        private GameObject _arena;
        private List<Renderer> _inkableRenderers;
        private List<Collider> _blockedColliders;

        [SetUp]
        public void SetUp()
        {
            _inkableRenderers = new List<Renderer>();
            _blockedColliders = new List<Collider>();
            _arena = ArenaGeometryBuilder.BuildArenaGeometry(
                null, null, null, null, null, null, null, null, null,
                null, null, _inkableRenderers, _blockedColliders, out _);

            // 编辑模式下新建/改 Transform 后 Collider.bounds 不会立即刷新，
            // 不同步会读到对象创建瞬间的 1×1×1 默认包围盒。
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (_arena != null)
            {
                Object.DestroyImmediate(_arena);
                _arena = null;
            }

            _inkableRenderers = null;
            _blockedColliders = null;
        }

        [Test]
        public void SpawnDecks_AreRaised_CoverSpawnLine_AndStayPaintable()
        {
            foreach (bool teamB in new[] { false, true })
            {
                string name = teamB ? "SpawnTerrace_TeamB" : "SpawnTerrace_TeamA";
                var deck = _arena.transform.Find(name)?.gameObject;
                Assert.IsNotNull(deck, name + " 必须存在");

                var col = deck.GetComponent<BoxCollider>();
                Assert.IsNotNull(col, name + " 必须有台面碰撞体");
                Bounds bounds = col.bounds;

                Assert.AreEqual(ArenaLayoutV2.SpawnDeckTopY, bounds.max.y, 0.01f,
                    "出生台面必须抬到 1.2m（对齐 INKWAVE 出生甲板）");
                Assert.AreEqual(ArenaLayoutV2.SpawnDeckWidth, bounds.size.x, 0.01f);
                Assert.AreEqual(ArenaLayoutV2.SpawnDeckDepth, bounds.size.z, 0.01f);

                float zSign = teamB ? 1f : -1f;
                Assert.AreEqual(zSign * ArenaLayoutV2.SpawnTerraceZ, bounds.center.z, 0.01f);

                // 出生线（|z|=23.5）必须落在台面 z 范围内，不能还悬在台前地面上
                float spawnZ = zSign * ArenaLayoutV2.SpawnZ;
                Assert.Greater(spawnZ, bounds.min.z, name + " 台面必须覆盖出生线后端");
                Assert.Less(spawnZ, bounds.max.z, name + " 台面必须覆盖出生线前端");

                Assert.IsFalse(_blockedColliders.Contains(col),
                    name + " 不得加入 blockedColliders，否则台面永远涂不上");
                Assert.IsNotNull(deck.GetComponent<InkWallSurface>(),
                    name + " 侧壁仍需可涂墨 / 可攀爬");
            }
        }

        [Test]
        public void SpawnPads_SitOnDeckTop_AndRemainPaintable()
        {
            foreach (bool teamB in new[] { false, true })
            {
                string name = teamB ? "SafeSpawnPad_TeamB" : "SafeSpawnPad_TeamA";
                var pad = _arena.transform.Find(name)?.gameObject;
                Assert.IsNotNull(pad, name + " 必须存在");

                var col = pad.GetComponent<BoxCollider>();
                Assert.IsNotNull(col);
                Bounds bounds = col.bounds;

                Assert.AreEqual(ArenaLayoutV2.SpawnDeckTopY + ArenaLayoutV2.SpawnPadThickness,
                    bounds.max.y, 0.01f, name + " 必须贴在台面顶面，而不是留在地面");
                Assert.AreEqual(ArenaLayoutV2.SpawnPadWidth, bounds.size.x, 0.01f);
                Assert.AreEqual(ArenaLayoutV2.SpawnPadDepth, bounds.size.z, 0.01f);

                Assert.IsFalse(_blockedColliders.Contains(col), name + " 不得加入 blockedColliders");
                Assert.IsNotNull(pad.GetComponent<InkWallSurface>(), name + " 侧沿仍需可涂");
            }
        }

        [Test]
        public void SpawnStairs_FiveStepsOnBothSides_ReachDeckHeight()
        {
            string[] stairs =
            {
                "Ramp_SpawnStair_A_East", "Ramp_SpawnStair_A_West",
                "Ramp_SpawnStair_B_East", "Ramp_SpawnStair_B_West",
            };

            int steps = ArenaLayoutV2.SpawnStairSteps;
            float rise = ArenaLayoutV2.SpawnDeckTopY / steps;
            float halfDeck = ArenaLayoutV2.SpawnDeckWidth * 0.5f;

            for (int s = 0; s < stairs.Length; s++)
            {
                bool east = stairs[s].EndsWith("_East", System.StringComparison.Ordinal);
                float sideSign = east ? 1f : -1f;

                for (int i = 0; i < steps; i++)
                {
                    var step = _arena.transform.Find(stairs[s] + "_Step_" + i)?.gameObject;
                    Assert.IsNotNull(step, $"{stairs[s]} 第 {i} 级台阶必须存在");

                    var col = step.GetComponent<BoxCollider>();
                    Assert.IsNotNull(col);
                    Bounds bounds = col.bounds;

                    Assert.AreEqual(rise * (i + 1), bounds.max.y, 0.01f,
                        $"{stairs[s]} 第 {i} 级高度必须逐级 0.24m 上升");
                    Assert.AreEqual(ArenaLayoutV2.SpawnStairWidth, bounds.size.z, 0.01f);
                    Assert.IsFalse(_blockedColliders.Contains(col), "台阶必须可行走且可涂");

                    // 台阶踢面只有 0.24m，按约定不挂 InkWallSurface（顶面走顶视墨迹图集）
                    Assert.IsNull(step.GetComponent<InkWallSurface>(),
                        $"{stairs[s]} 第 {i} 级不应挂 InkWallSurface（20×4 张立面贴图不值得）");
                }

                var topStep = _arena.transform.Find(stairs[s] + "_Step_" + (steps - 1))?.gameObject;
                Assert.IsNotNull(topStep);
                Bounds topBounds = topStep.GetComponent<BoxCollider>().bounds;
                Assert.AreEqual(ArenaLayoutV2.SpawnDeckTopY, topBounds.max.y, 0.01f,
                    "最高一级必须与台面等高");
                if (east)
                {
                    Assert.AreEqual(halfDeck, topBounds.min.x, 0.01f,
                        "东侧最高一级必须紧贴台面东侧壁 (x=+7)");
                }
                else
                {
                    Assert.AreEqual(-halfDeck, topBounds.max.x, 0.01f,
                        "西侧最高一级必须紧贴台面西侧壁 (x=-7)");
                }
            }
        }

        [Test]
        public void NorthBoundaryWall_HighEnoughAboveDeck_PreventsJumpingOut()
        {
            var deckB = _arena.transform.Find("SpawnTerrace_TeamB")?.gameObject;
            Assert.IsNotNull(deckB, "SpawnTerrace_TeamB 必须存在");
            var deckCol = deckB.GetComponent<BoxCollider>();
            Assert.IsNotNull(deckCol);

            var northWall = _arena.transform.Find("Boundary_North")?.gameObject;
            Assert.IsNotNull(northWall, "Boundary_North 必须存在");
            var wallCol = northWall.GetComponent<BoxCollider>();
            Assert.IsNotNull(wallCol);

            // 角色跳跃高度 1.35m，防翻越要求：北墙顶 - 甲板顶 ≥ jumpHeight + 0.3 = 1.65m
            const float jumpHeight = 1.35f;
            float clearance = wallCol.bounds.max.y - deckCol.bounds.max.y;
            Assert.GreaterOrEqual(clearance, jumpHeight + 0.3f,
                $"北边界墙高出甲板高度 ({clearance:F2}m) 必须 ≥ jumpHeight + 0.3 ({jumpHeight + 0.3f:F2}m)，防止角色跳越北墙坠出场地");
        }
    }
}