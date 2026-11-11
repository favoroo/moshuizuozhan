using System.Collections.Generic;
using NUnit.Framework;
using P1.Environment;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 涂地覆盖率审计的回归用例：场上每个"有可视表面 + 碰撞体"的对象都必须能落墨，
    /// 这是"所有位置、所有物品都要能喷上油漆"的可执行保证（对齐 INKWAVE 的 paintable 默认全开）。
    /// </summary>
    public class PaintCoverageAuditTests
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

            // 编辑模式下手动改 Transform / 新建对象后碰撞体 bounds 不会立即刷新，
            // 必须先同步，否则断言读到的是对象创建那一刻的默认 1×1×1 包围盒。
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
        public void ArenaGeometry_HasNoUnpaintableSurface()
        {
            var violations = new List<string>();
            var warnings = new List<string>();
            var roots = new List<Transform> { _arena.transform };

            int checkedCount = PaintCoverageAudit.Run(roots, null, violations, warnings);

            Assert.Greater(checkedCount, 20, "竞技场应包含大量需要受墨的可视表面");
            Assert.AreEqual(0, violations.Count,
                "存在既不能顶面受墨、也没有墨面立面的可视表面：\n" + string.Join("\n", violations));
        }

        [Test]
        public void SouthBoundary_VisibleWallColliderMatchesMesh_BlockerIsSeparate()
        {
            Transform south = _arena.transform.Find("Boundary_South");
            Assert.IsNotNull(south, "应存在 Boundary_South 可见矮墙");

            // 编辑模式下新建对象的碰撞体包围盒可能还是创建瞬间的默认 1×1×1，先强制同步
            Physics.SyncTransforms();

            var wallCol = south.GetComponent<BoxCollider>();
            Assert.IsNotNull(wallCol, "可见矮墙应有 BoxCollider");
            Assert.AreEqual(0.55f, wallCol.size.y * south.lossyScale.y, 0.01f,
                "可见矮墙碰撞体必须与 0.55m 网格等高，否则射线会打在隐形墙上、命中点落在墨面之外");

            var wall = south.GetComponent<InkWallSurface>();
            Assert.IsNotNull(wall, "南边界可见矮墙应装配墨面");
            Assert.IsTrue(wall.InitializeIfNeeded(true), "应能初始化南墙墨面");
            Assert.AreEqual(0.55f, wall.WorldBounds.size.y, 0.01f, "墨面高度必须与可见墙等高");

            Transform blocker = _arena.transform.Find("Boundary_South_Blocker");
            Assert.IsNotNull(blocker, "应存在独立的隐形防越界 Blocker");

            var blockerCol = blocker.GetComponent<BoxCollider>();
            Assert.IsNotNull(blockerCol, "Blocker 应有 BoxCollider");
            Assert.Greater(blockerCol.size.y * blocker.lossyScale.y, 2.5f, "Blocker 应足够高以阻止越界");
            Assert.IsNull(blocker.GetComponent<Renderer>(), "Blocker 不应有任何可视表面");
            Assert.IsNull(blocker.GetComponent<InkWallSurface>(), "Blocker 不产生可视表面，不应装配墨面");
        }

        [Test]
        public void SolidDecorProps_WhenBlocked_TopStaysReachableSoNoWarning()
        {
            var propGo = new GameObject("PropCollider_CrateCluster_00");
            var surfaceGo = new GameObject("~TestSurface");
            try
            {
                propGo.transform.SetParent(_arena.transform);
                var col = propGo.AddComponent<BoxCollider>();
                col.size = new Vector3(2f, 1.5f, 2f);

                var mr = propGo.AddComponent<MeshRenderer>();
                Shader propShader = Shader.Find("P1/PropLit");
                if (propShader != null)
                {
                    mr.sharedMaterial = new Material(propShader);
                }

                _blockedColliders.Add(col);

                var surface = surfaceGo.AddComponent<InkSurfaceController>();
                surface.Configure(null, _inkableRenderers, _blockedColliders);

                var violations = new List<string>();
                var warnings = new List<string>();
                PaintCoverageAudit.Run(new List<Transform> { _arena.transform }, surface, violations, warnings);

                Assert.AreEqual(0, violations.Count, "注册进 blockedColliders 的实体道具顶面不应产生违规");

                // 实体道具是「软占位」：占位格仍算占位（小地图/接触遮蔽/窄缝剔除），但顶面
                // 从上方命中即可涂、可计分，因此不再有「顶面不计分」的告警。
                var centerXZ = new Vector2(col.bounds.center.x, col.bounds.center.z);
                Assert.IsTrue(surface.Grid.IsSoftBlockedAt(centerXZ),
                    "实体道具的占地格必须是软占位（顶面从上方可涂）");
                Assert.IsFalse(warnings.Exists(w => w.Contains("blockedColliders")),
                    "软占位的顶面能涂到，不应再提示「箱顶永远喷不上」");
            }
            finally
            {
                if (propGo != null) Object.DestroyImmediate(propGo);
                if (surfaceGo != null) Object.DestroyImmediate(surfaceGo);
            }
        }
    }
}