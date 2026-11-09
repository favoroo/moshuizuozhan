using NUnit.Framework;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P1/InkGround 着色器写入 Pass 的编译门禁。
    ///
    /// 背景：地面墨迹图集由 InkGround.shader 的 InkAtlasPaint / InkAtlasStamp 两个 Pass
    /// 通过 CommandBuffer.DrawMesh 写入。这两个 Pass 一旦编译失败（例如在 CBUFFER_START
    /// 之前漏包含 SRP Core.hlsl），DrawMesh 不产生任何像素，图集恒为全透明 ——
    /// 表现为「地面完全不显示墨水、涂地机制视觉上失效」，而 CPU 墨格与覆盖率仍然正常，
    /// 极易被误判成玩法逻辑问题。本用例把「Pass 存在 + 无编译错误」固化成门禁。
    /// </summary>
    [TestFixture]
    public class InkGroundShaderTests
    {
        private const string ShaderName = "P1/InkGround";
        private const string PaintPassName = "InkAtlasPaint";
        private const string StampPassName = "InkAtlasStamp";

        [Test]
        public void InkGroundShader_ContainsBothAtlasWritePasses()
        {
            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, $"必须存在着色器 {ShaderName}（InkSplatAtlas 依赖它写入墨迹图集）");

            var material = new Material(shader);
            try
            {
                Assert.GreaterOrEqual(material.FindPass(PaintPassName), 0,
                    $"{ShaderName} 缺少 {PaintPassName} Pass：墨迹图集将永远是空的（地面没有墨）");
                Assert.GreaterOrEqual(material.FindPass(StampPassName), 0,
                    $"{ShaderName} 缺少 {StampPassName} Pass：湿润度时间戳无法写入");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void InkGroundShader_HasNoCompileErrors()
        {
            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, $"必须存在着色器 {ShaderName}");

#if UNITY_EDITOR
            var messages = UnityEditor.ShaderUtil.GetShaderMessages(shader);
            for (int i = 0; i < messages.Length; i++)
            {
                var m = messages[i];
                if (m.severity.ToString() != "Error")
                    continue; // warning / info 不阻塞（不同平台可选特性差异）

                Assert.Fail(
                    $"{ShaderName} 编译错误：line {m.line} [{m.platform}] {m.message}\n" +
                    "写入 Pass 编译失败时 DrawMesh 不产生像素，地面会完全没有墨迹。");
            }
#endif
        }

        /// <summary>
        /// 顶面墨迹统一走 InkSurfaceOverlay.hlsl 的 P1EvaluateTopInk。
        /// 该文件新增计分掩码采样后，任何一个引用它的着色器编译失败都会让道具/出生台
        /// 完全失去顶面墨迹（且只在真机渲染时可见），这里把三个消费者都固化成门禁。
        /// </summary>
        [TestCase("P1/PropLit")]
        [TestCase("P1/ObstacleDitherLit")]
        [TestCase("P1/SpawnPad")]
        public void TopInkShaders_HaveNoCompileErrors(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, $"必须存在着色器 {shaderName}（顶面墨迹消费者）");

#if UNITY_EDITOR
            var messages = UnityEditor.ShaderUtil.GetShaderMessages(shader);
            for (int i = 0; i < messages.Length; i++)
            {
                var m = messages[i];
                if (m.severity.ToString() != "Error")
                    continue;

                Assert.Fail(
                    $"{shaderName} 编译错误：line {m.line} [{m.platform}] {m.message}\n" +
                    "InkSurfaceOverlay.hlsl 的顶面墨迹/计分掩码采样必须能编译通过。");
            }
#endif
        }
    }
}