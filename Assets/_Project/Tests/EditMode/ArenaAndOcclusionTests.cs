using NUnit.Framework;
using P1.Core;
using P1.Environment;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    [TestFixture]
    public class ArenaAndOcclusionTests
    {
        [Test]
        public void OcclusionFader_TransitionsToMinAlphaWhenOccluded()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var fader = go.AddComponent<OcclusionFader>();
                fader.MinAlpha = 0.35f;
                fader.FadeSpeed = 10f;

                Assert.AreEqual(1.0f, fader.CurrentAlpha, 0.001f);
                Assert.IsFalse(fader.IsOccluding);

                // 标记被遮挡
                fader.MarkOccluded(new Vector3(0, 0, 5));
                // 迭代 0.1s
                fader.EvaluateStep(0.1f);

                Assert.Less(fader.CurrentAlpha, 1.0f);
                Assert.IsTrue(fader.IsOccluding);

                // 持续迭代直到完全达到 minAlpha
                for (int i = 0; i < 20; i++)
                {
                    fader.MarkOccluded(new Vector3(0, 0, 5));
                    fader.EvaluateStep(0.05f);
                }

                Assert.AreEqual(0.35f, fader.CurrentAlpha, 0.001f);
                Assert.IsTrue(fader.IsOccluding);

                // 走出掩体后平滑恢复
                for (int i = 0; i < 20; i++)
                {
                    fader.EvaluateStep(0.05f);
                }

                Assert.AreEqual(1.0f, fader.CurrentAlpha, 0.001f);
                Assert.IsFalse(fader.IsOccluding);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OcclusionFader_UpdatesPropertyBlockOnRenderer()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var rend = go.GetComponent<Renderer>();
                var fader = go.AddComponent<OcclusionFader>();
                fader.MinAlpha = 0.35f;
                fader.FadeSpeed = 20f;

                fader.MarkOccluded(new Vector3(1, 0, 2));
                for (int i = 0; i < 10; i++)
                {
                    fader.MarkOccluded(new Vector3(1, 0, 2));
                    fader.EvaluateStep(0.05f);
                }

                var block = new MaterialPropertyBlock();
                rend.GetPropertyBlock(block);
                float ditherAlpha = block.GetFloat(Shader.PropertyToID("_DitherAlpha"));

                Assert.AreEqual(0.35f, ditherAlpha, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private GameObject _testArena;
        private System.Collections.Generic.List<Renderer> _inkableRenderers;
        private System.Collections.Generic.List<Collider> _blockedColliders;

        [SetUp]
        public void SetUp()
        {
            _inkableRenderers = new System.Collections.Generic.List<Renderer>();
            _blockedColliders = new System.Collections.Generic.List<Collider>();
            _testArena = ArenaGeometryBuilder.BuildArenaGeometry(
                null, null, null, null, null, null, null, null, null,
                null, null, _inkableRenderers, _blockedColliders, out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (_testArena != null)
            {
                Object.DestroyImmediate(_testArena);
                _testArena = null;
            }

            _inkableRenderers = null;
            _blockedColliders = null;
        }

        [Test]
        public void ArenaGeometry_CentralPlatform_WithinHeightLimit()
        {
            var centerPlatform = _testArena.transform.Find("HighGround_Center")?.gameObject;
            Assert.IsNotNull(centerPlatform, "场景中必须存在 HighGround_Center 中央争夺高台");

            float height = centerPlatform.transform.localScale.y;
            Assert.LessOrEqual(height, 1.201f, "中央争夺高台高度必须 ≤ 1.2m，避免严重遮挡俯视角");
            Assert.GreaterOrEqual(height, 0.8f, "中央高台需保持合理的高低差战术争夺感");

            // 中心必须位于竞技场正中央 (0,0)
            Assert.AreEqual(0f, centerPlatform.transform.position.x, 0.1f);
            Assert.AreEqual(0f, centerPlatform.transform.position.z, 0.1f);

            var fader = centerPlatform.GetComponent<OcclusionFader>();
            Assert.IsNotNull(fader, "中央高台必须挂载 OcclusionFader 遮挡剔除组件");
        }

        [Test]
        public void ArenaGeometry_FourWayRamps_ExistAndOrientedCorrectly()
        {
            var rampS = _testArena.transform.Find("Ramp_South_Inkable")?.gameObject;
            var rampN = _testArena.transform.Find("Ramp_North_Inkable")?.gameObject;
            var rampW = _testArena.transform.Find("Ramp_West_Inkable")?.gameObject;
            var rampE = _testArena.transform.Find("Ramp_East_Inkable")?.gameObject;

            Assert.IsNotNull(rampS, "南向斜坡必须存在");
            Assert.IsNotNull(rampN, "北向斜坡必须存在");
            Assert.IsNotNull(rampW, "西向斜坡必须存在");
            Assert.IsNotNull(rampE, "东向斜坡必须存在");

            // 坡度角必须平缓（≤ 25°），确保乌贼与人类可顺畅突进
            float pitchS = rampS.transform.rotation.eulerAngles.x;
            if (pitchS > 180f) pitchS -= 360f;
            Assert.Less(pitchS, 0f, "南向斜坡应朝北上升（负俯仰角）");

            float pitchN = rampN.transform.rotation.eulerAngles.x;
            if (pitchN > 180f) pitchN -= 360f;
            Assert.Greater(pitchN, 0f, "北向斜坡应朝南上升（正俯仰角）");

            // 必须全部带有 OcclusionFader
            Assert.IsNotNull(rampS.GetComponent<OcclusionFader>());
            Assert.IsNotNull(rampN.GetComponent<OcclusionFader>());
            Assert.IsNotNull(rampW.GetComponent<OcclusionFader>());
            Assert.IsNotNull(rampE.GetComponent<OcclusionFader>());
        }

        [Test]
        public void ArenaGeometry_FlankPlatformsAndContainers_ExistSymmetrically()
        {
            var westPlatform = _testArena.transform.Find("HighGround_West")?.gameObject;
            var eastPlatform = _testArena.transform.Find("HighGround_East")?.gameObject;

            Assert.IsNotNull(westPlatform, "西侧侧翼高台必须存在");
            Assert.IsNotNull(eastPlatform, "东侧侧翼高台必须存在");

            Assert.AreEqual(-eastPlatform.transform.position.x, westPlatform.transform.position.x, 0.2f, "东西侧翼高台必须保持 X 轴对称");

            // 集装箱群验证
            var containerSW = _testArena.transform.Find("Container_Mid_SW_Mustard")?.gameObject;
            var containerNE = _testArena.transform.Find("Container_Mid_NE_Mustard")?.gameObject;
            Assert.IsNotNull(containerSW, "中场西南集装箱掩体必须存在");
            Assert.IsNotNull(containerNE, "中场东北集装箱掩体必须存在");

            var fader = containerSW.GetComponent<OcclusionFader>();
            Assert.IsNotNull(fader, "集装箱掩体必须搭载 OcclusionFader 遮挡淡化组件");
            Assert.AreEqual(0.35f, fader.MinAlpha, 0.01f, "掩体遮挡目标透明度必须设定为 0.35");
        }

        [Test]
        public void InkSurface_BlockedColliders_PreventPaintingUnderObstacles()
        {
            var grid = new InkGrid(40, 40, new Vector2(40f, 40f), null,
                new Color32(50, 50, 50, 0),
                new Color32(255, 100, 0, 255),
                new Color32(0, 100, 255, 255),
                new Color32(20, 20, 20, 255));

            // 将中央 (0,0) 处 4x4m 矩形设为掩体阻挡
            grid.SetBlockedWorldRect(new Vector2(-2f, -2f), new Vector2(2f, 2f), true);

            // 在原点处涂 3m 圆形墨斑
            grid.PaintCircle(Vector2.zero, 3.0f, Team.TeamA);

            // 位于掩体内部的点绝不能被涂上墨水
            Assert.AreEqual(Team.Neutral, grid.InkAt(Vector2.zero), "掩体阻挡区域内部不可被涂上队伍墨水");
            Assert.IsTrue(grid.IsBlockedAt(Vector2.zero), "掩体内部必须被标记为 Blocked");

            // 位于掩体外部但在圆斑内部的点应当正常着色
            Assert.AreEqual(Team.TeamA, grid.InkAt(new Vector2(2.5f, 0f)), "掩体外部但在泼墨半径内的地面应正常涂色");
        }

        [Test]
        public void ArenaGeometry_AllObstaclesAndProps_HaveInkWallSurface_WithCorrectClimbRules()
        {
            // 验证中央高台花坛与女儿墙均有 InkWallSurface 且可攀爬
            var planter = _testArena.transform.Find("Cover_Center_West_Planter")?.gameObject;
            Assert.IsNotNull(planter, "中央花坛必须存在");
            var planterWall = planter.GetComponent<InkWallSurface>();
            Assert.IsNotNull(planterWall, "中央花坛必须挂载 InkWallSurface 组件");
            Assert.IsTrue(planterWall.CanClimb, "中央花坛应允许攀爬");

            var parapet = _testArena.transform.Find("Parapet_Center_SW")?.gameObject;
            Assert.IsNotNull(parapet, "中央女儿墙必须存在");
            var parapetWall = parapet.GetComponent<InkWallSurface>();
            Assert.IsNotNull(parapetWall, "中央女儿墙必须挂载 InkWallSurface");
            Assert.IsTrue(parapetWall.CanClimb, "女儿墙应允许攀爬");

            // 验证延长区木箱与网关
            var crate = _testArena.transform.Find("Crate_Extension_A")?.gameObject;
            Assert.IsNotNull(crate, "延长区木箱 A 必须存在");
            var crateWall = crate.GetComponent<InkWallSurface>();
            Assert.IsNotNull(crateWall, "木箱必须挂载 InkWallSurface");
            Assert.IsTrue(crateWall.CanClimb, "木箱应允许攀爬");

            var gate = _testArena.transform.Find("SpawnGate_A")?.gameObject;
            Assert.IsNotNull(gate, "网关 A 必须存在");
            var gateWall = gate.GetComponent<InkWallSurface>();
            Assert.IsNotNull(gateWall, "网关必须挂载 InkWallSurface");

            // 验证四周边界墙可涂墨但绝对禁止攀爬越界
            var boundaryS = _testArena.transform.Find("Boundary_South")?.gameObject;
            Assert.IsNotNull(boundaryS, "南向边界围墙必须存在");
            var boundaryWall = boundaryS.GetComponent<InkWallSurface>();
            Assert.IsNotNull(boundaryWall, "边界围墙必须挂载 InkWallSurface 支持泼墨吸附");
            Assert.IsFalse(boundaryWall.CanClimb, "边界围墙严禁攀爬（防翻出场外坠落）");

            var boundaryN = _testArena.transform.Find("Boundary_North")?.gameObject;
            Assert.IsNotNull(boundaryN?.GetComponent<InkWallSurface>(), "北向边界墙必须支持涂墨");
            Assert.IsFalse(boundaryN.GetComponent<InkWallSurface>().CanClimb, "北向边界墙严禁攀爬");
        }

        [Test]
        public void InkWallSurface_LazyRenderer_DisabledByDefault_AndEnabledOnPaint()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var wall = go.AddComponent<InkWallSurface>();
                var cfg = ScriptableObject.CreateInstance<InkGridConfigSO>();
                wall.Configure(go.GetComponent<BoxCollider>(), null, cfg, true);

                // 运行时初始化生成 4 面（EditMode 下传入 force: true 强行构建 Quad 与贴图）
                Assert.IsTrue(wall.InitializeIfNeeded(true));
                Assert.AreEqual(4, wall.FaceCount);

                // 初始状态下所有子 Renderer 均应为 enabled = false，实现 0 DrawCall 空闲开销
                var childRenderers = go.GetComponentsInChildren<MeshRenderer>();
                // 其中 1 个是物体自身 MeshRenderer，4 个是立面 Quad
                int quadRenderers = 0;
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    if (childRenderers[i].gameObject != go)
                    {
                        quadRenderers++;
                        Assert.IsFalse(childRenderers[i].enabled, "初始未涂色的立面 MeshRenderer 必须为 false 节省渲染开销");
                    }
                }
                Assert.AreEqual(4, quadRenderers, "必须生成 4 个立面 Quad");

                // 在南立面上涂墨
                Vector3 southFacePoint = new Vector3(wall.WorldBounds.center.x, wall.WorldBounds.center.y, wall.WorldBounds.min.z);
                int changed = wall.PaintAtWorld(southFacePoint, Vector3.back, 0.5f, Team.TeamA);
                Assert.Greater(changed, 0, "涂墨必须成功写入格子");

                // 命中面被激活
                int activeCount = 0;
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    if (childRenderers[i].gameObject != go && childRenderers[i].enabled)
                    {
                        activeCount++;
                    }
                }
                Assert.AreEqual(1, activeCount, "仅被涂墨的立面 Renderer 被激活开启");

                // 重置后恢复禁用
                wall.ResetPaint();
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    if (childRenderers[i].gameObject != go)
                    {
                        Assert.IsFalse(childRenderers[i].enabled, "ResetPaint 后所有立面 Renderer 必须恢复禁用");
                    }
                }

                Object.DestroyImmediate(cfg);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ArenaGeometry_SpawnPads_ArePaintable_NotBlocked()
        {
            var padA = _testArena.transform.Find("SafeSpawnPad_TeamA")?.gameObject;
            var padB = _testArena.transform.Find("SafeSpawnPad_TeamB")?.gameObject;
            Assert.IsNotNull(padA, "Team A 出生台必须存在");
            Assert.IsNotNull(padB, "Team B 出生台必须存在");

            // 出生台顶面由 SpawnPad.shader 采样墨迹图集，网格层面不能再标记为不可涂
            Assert.IsFalse(_blockedColliders.Contains(padA.GetComponent<Collider>()),
                "出生台不得加入 blockedColliders，否则射击台面永远涂不上");
            Assert.IsFalse(_blockedColliders.Contains(padB.GetComponent<Collider>()),
                "出生台不得加入 blockedColliders");

            Assert.IsNotNull(padA.GetComponent<InkWallSurface>(), "出生台侧沿仍需可涂");
            Assert.IsNotNull(padB.GetComponent<InkWallSurface>(), "出生台侧沿仍需可涂");
        }

        [Test]
        public void ArenaGeometry_AllRamps_HaveOrientedPaintableSideFaces()
        {
            string[] rampNames =
            {
                "Ramp_South_Inkable", "Ramp_North_Inkable", "Ramp_West_Inkable", "Ramp_East_Inkable",
                "Ramp_West_South_Inkable", "Ramp_West_North_Inkable", "Ramp_East_South_Inkable", "Ramp_East_North_Inkable",
            };

            for (int i = 0; i < rampNames.Length; i++)
            {
                var ramp = _testArena.transform.Find(rampNames[i])?.gameObject;
                Assert.IsNotNull(ramp, $"坡道 {rampNames[i]} 必须存在");

                var wall = ramp.GetComponent<InkWallSurface>();
                Assert.IsNotNull(wall, $"坡道 {rampNames[i]} 的侧面必须可涂墨");
                Assert.IsFalse(wall.CanClimb, $"坡道 {rampNames[i]} 的侧面不应触发上墙攀爬");

                Assert.IsTrue(wall.InitializeIfNeeded(true));
                Assert.AreEqual(4, wall.FaceCount, "坡道应生成 4 张侧面墨面");
            }
        }

        [Test]
        public void ArenaGeometry_SouthBoundaryInkFaces_MatchVisibleWallHeight()
        {
            var south = _testArena.transform.Find("Boundary_South")?.gameObject;
            Assert.IsNotNull(south, "南向边界围墙必须存在");

            var wall = south.GetComponent<InkWallSurface>();
            Assert.IsNotNull(wall, "南向边界围墙必须支持涂墨");
            Assert.IsFalse(wall.CanClimb, "南向边界围墙仍严禁攀爬");

            Assert.IsTrue(wall.InitializeIfNeeded(true));
            Assert.AreEqual(0.55f, wall.WorldBounds.size.y, 0.01f,
                "南墙墨面只应覆盖 0.55m 可见矮墙；按 3m 碰撞体建面会让墨飘在空中");
        }
    }
}
