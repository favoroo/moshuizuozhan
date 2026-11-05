using System.IO;
using NUnit.Framework;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 「场外不显墨」的回归护栏。
    ///
    /// 历史 bug：墨迹图集只覆盖 40x56 计分场地 + 1.5m 外扩边距，而场外码头基座（74x84m）、
    /// 远景堆场同样使用 PropLit/ObstacleDitherLit 的顶面墨迹采样。世界 XZ 落在图集外时
    /// 被 saturate 钳到边界纹素，整片场外沿边界拉伸成"一条一条"的墨色。
    /// 修复：InkSurfaceController 推送 _GlobalInkPlayableRect，着色器对场地外片元直接判无墨。
    /// 这里用源码断言守住三个不变量，避免有人删掉屏蔽或只改一处。
    /// </summary>
    public class InkMapBoundsPaintTests
    {
        private const string OverlayShaderPath = "Assets/_Project/Shaders/InkSurfaceOverlay.hlsl";
        private const string GroundShaderPath = "Assets/_Project/Shaders/InkGround.shader";
        private const string ControllerPath = "Assets/_Project/Scripts/Ink/InkSurfaceController.cs";

        [Test]
        public void TopInkOverlay_RejectsFragmentsOutsidePlayableMap()
        {
            string source = ReadSource(OverlayShaderPath);

            StringAssert.Contains("_GlobalInkPlayableRect", source,
                "顶面墨迹叠加必须读取计分场地矩形，否则场外码头会把图集边界纹素拉成条纹");
            StringAssert.Contains("positionWS.x < playMin.x", source,
                "顶面墨迹叠加缺少场外判定：场地外片元必须直接不显墨");
        }

        [Test]
        public void GroundShader_RejectsFragmentsOutsidePlayableMap()
        {
            string source = ReadSource(GroundShaderPath);

            StringAssert.Contains("_GlobalInkPlayableRect", source,
                "地面着色器必须读取计分场地矩形，否则地面盒体外侧仍会采到边界墨色");
            StringAssert.Contains("inkInPlayableMap", source,
                "地面着色器缺少场外判定");
            StringAssert.Contains("amtEff = 0.0", source,
                "场外必须在亚纹素修正之后再清一次 amtEff，否则邻域采样会把边界覆盖率带回来");
        }

        [Test]
        public void Controller_PushesPlayableRectGlobal()
        {
            string source = ReadSource(ControllerPath);

            StringAssert.Contains("_GlobalInkPlayableRect", source,
                "InkSurfaceController 必须推送 _GlobalInkPlayableRect（着色器的场外屏蔽依赖它）");
            StringAssert.Contains("GlobalInkPlayableRectProp", source,
                "场地矩形全局属性 ID 缺失：着色器读不到矩形时会退回旧的 clamp 拉伸行为");
        }

        private static string ReadSource(string path)
        {
            Assert.IsTrue(File.Exists(path), $"找不到文件：{path}");
            return File.ReadAllText(path);
        }
    }
}
