using System.IO;
using System.Linq;
using NUnit.Framework;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 全屏卡通描边的回归护栏。
    ///
    /// 历史 bug：深度边缘用一阶差分判定（|邻域深度 − 中心深度| / 中心深度），
    /// 在掠射角下平滑表面本身的深度梯度就远超阈值，于是整片表面被判成"轮廓"并填成纯黑 ——
    /// 玩家视角就是"有些物体面一直是黑色的，也喷不了油漆"，而那片表面其实完全正常。
    /// 修复改用二阶差分（拉普拉斯）：线性深度上的平滑平面恒为 0，只有真实遮挡阶跃才会触发。
    /// 这里用源码断言守住该不变量，避免有人把判定改回一阶差分或用"调大阈值"糊过去。
    /// </summary>
    public class ToonOutlineRegressionTests
    {
        private const string OutlineShaderPath = "Assets/_Project/Shaders/ToonOutline.shader";

        [Test]
        public void DepthEdge_UsesSecondDifference_NotFirstDifference()
        {
            Assert.IsTrue(File.Exists(OutlineShaderPath), $"找不到描边着色器：{OutlineShaderPath}");
            string source = File.ReadAllText(OutlineShaderPath);

            Assert.IsTrue(source.Contains("laplacian"),
                "描边深度边缘必须用二阶差分（拉普拉斯）判定，否则掠射角表面会被整片填黑");
            Assert.IsTrue(source.Contains("centerDepth * 2.0 - depthL - depthR"),
                "拉普拉斯判定式缺失：应为 2*center − L − R（平滑平面恒为 0，阶跃显著）");

            Assert.IsFalse(source.Contains("abs(depthL - centerDepth)"),
                "禁止改回一阶差分：掠射角下它会把握手面判成边缘并填成纯黑");
        }

        [Test]
        public void OutlineMaterial_ThresholdsStayAtDefaults()
        {
            var mat = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>()
                .FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "P1/ToonOutline");
            if (mat == null)
            {
                Assert.Ignore("当前会话没有加载描边材质（不影响源码不变量校验）");
            }

            Assert.AreEqual(0.02f, mat.GetFloat("_DepthThreshold"), 0.005f,
                "_DepthThreshold 应保持默认 0.02；把阈值调大只是掩盖一阶差分的缺陷，不是修复");
        }
    }
}