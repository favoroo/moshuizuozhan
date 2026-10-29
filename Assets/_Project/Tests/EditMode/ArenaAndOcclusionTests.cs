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

        [SetUp]
        public void SetUp()
        {
            var inkable = new System.Collections.Generic.List<Renderer>();
            var blocked = new System.Collections.Generic.List<Collider>();
            _testArena = ArenaGeometryBuilder.BuildArenaGeometry(
                null, null, null, null, null, null, null, null, null,
                null, null, inkable, blocked, out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (_testArena != null)
            {
                Object.DestroyImmediate(_testArena);
                _testArena = null;
            }
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
    }
}
