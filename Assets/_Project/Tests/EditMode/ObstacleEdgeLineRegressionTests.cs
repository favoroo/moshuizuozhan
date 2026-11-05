using System.IO;
using System.Linq;
using NUnit.Framework;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 掩体边界线回归护栏（P17-A）。
    ///
    /// 背景：v0.1.6 统一墙墨/地墨/顶面墨的明暗模型后（同色系，玩家认可），掩体与地面
    /// 完全同色——涂满墨的掩体顶面和地面 RGB 几乎一致，玩家分不清掩体在哪、容易撞上。
    /// 修复为掩体每个面四周一圈深色硬边线。这里用源码断言守住两个不变量：
    /// ① 边线必须压在墨水叠加之后（涂满墨后轮廓仍可辨）；
    /// ② 涂墨立面（InkWall quad 外扩 1cm 会盖住基面边线）也要有同款边线。
    /// </summary>
    public class ObstacleEdgeLineRegressionTests
    {
        private const string ObstacleShaderPath = "Assets/_Project/Shaders/ObstacleDitherLit.shader";
        private const string InkWallShaderPath = "Assets/_Project/Shaders/InkWall.shader";

        [Test]
        public void ObstacleShader_AppliesEdgeLineAfterInkOverlay()
        {
            Assert.IsTrue(File.Exists(ObstacleShaderPath), $"找不到掩体着色器：{ObstacleShaderPath}");
            string source = File.ReadAllText(ObstacleShaderPath);

            int inkOverlay = source.IndexOf("ApplyTopInkOverlay(shaded", System.StringComparison.Ordinal);
            int edgeLine = source.IndexOf("edgeLine * _EdgeLineStrength", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(inkOverlay, 0, "顶面墨水叠加调用缺失");
            Assert.GreaterOrEqual(edgeLine, 0,
                "掩体边界线缺失：墨色统一后掩体与地面同色，没有这圈线掩体就『消失』了");
            Assert.Greater(edgeLine, inkOverlay,
                "边界线必须压在墨水叠加之后，否则涂满墨的掩体轮廓会被墨盖住");
        }

        [Test]
        public void ObstacleShader_EdgeDistanceSurvivesStaticBatching()
        {
            string source = File.ReadAllText(ObstacleShaderPath);

            // 掩体全部标记 BatchingStatic：静态合批把顶点烘焙成世界坐标，positionOS 不再是
            // object space 的 ±0.5 盒、UNITY_MATRIX_M 退化为单位阵，任何
            // (0.5 - abs(positionOS)) * 列向量长度 形式的盒体距离都会恒为负值，
            // 把整张表面涂成 _EdgeLineColor（船甲板/掩体大面积全黑的根因）。
            Assert.IsFalse(source.Contains("edgeDist3"),
                "边线距离不能再用 object-space 盒体距离：静态合批后 positionOS 是世界坐标，会整面涂黑");
            Assert.IsFalse(source.Contains("UNITY_MATRIX_M._11_21_31"),
                "边线距离不能靠 UNITY_MATRIX_M 取世界尺度：静态合批后它是单位阵");

            // 每个盒子的世界 AABB 由 CreateBox 烘进 uv2/uv3/uv4（合批保留 UV 通道）。
            Assert.IsTrue(source.Contains("input.boxMinWS") && source.Contains("input.boxMaxWS"),
                "边线距离必须来自烘焙进 UV 的世界 AABB，否则静态合批后拿不到每个盒子的尺寸");
            Assert.IsTrue(source.Contains("boxValid"),
                "缺少退化保护：AABB 未烘焙（全零）时整面会被判成贴边并涂成 _EdgeLineColor");
        }

        [Test]
        public void ArenaBoxBakesWorldBoundsIntoUV()
        {
            string source = File.ReadAllText("Assets/_Project/Scripts/Environment/ArenaGeometryBuilder.cs");
            Assert.IsTrue(source.Contains("BakeBoxBoundsUV"),
                "CreateBox 必须把盒子的世界 AABB 烘进 uv2/uv3/uv4，否则掩体边界线在静态合批后失效");
            Assert.IsTrue(source.Contains("mesh.uv2") && source.Contains("mesh.uv3") && source.Contains("mesh.uv4"),
                "AABB 需要写满 uv2(minXZ) / uv3(maxXZ) / uv4(minY,maxY) 三个通道");
        }

        [Test]
        public void InkWallShader_KeepsEdgeLineOnPaintedFacade()
        {
            Assert.IsTrue(File.Exists(InkWallShaderPath), $"找不到墙面墨着色器：{InkWallShaderPath}");
            string source = File.ReadAllText(InkWallShaderPath);

            Assert.IsTrue(source.Contains("_EdgeLineStrength"),
                "涂墨立面边界线缺失：InkWall quad 外扩 1cm 会盖住基面边线，涂满墨的立面必须自己保留轮廓");
        }

        [Test]
        public void ObstacleMaterial_KeepsEdgeLineVisible()
        {
            var mat = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>()
                .FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "P1/ObstacleDitherLit");
            if (mat == null)
            {
                Assert.Ignore("当前会话没有加载掩体材质（不影响源码不变量校验）");
            }

            Assert.Greater(mat.GetFloat("_EdgeLineStrength"), 0.05f,
                "边界线强度被归零会退回『墨色统一后掩体消失』的问题");
            Assert.Greater(mat.GetFloat("_EdgeLineWidth"), 0.0f, "边线宽度必须为正");
        }
    }
}
