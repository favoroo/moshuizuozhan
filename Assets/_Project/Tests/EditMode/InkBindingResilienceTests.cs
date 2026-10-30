using System.Collections.Generic;
using NUnit.Framework;
using P1.Environment;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 墨迹绑定韧性：遮挡淡出/恢复不得破坏同一渲染器上其它系统写进去的 MaterialPropertyBlock 键。
    ///
    /// 回归的 bug：OcclusionFader 恢复不透明时执行 SetPropertyBlock(null)，
    /// 把 InkSurfaceController 写进去的 _InkTex / _InkMapOriginXZ / _InkTexelSize 一起抹掉，
    /// 于是高台、坡道、出生台（都挂了 OcclusionFader）在"被遮挡过一次"之后就再也喷不上墨，
    /// 同理会抹掉 InkWallSurface 挂在 InkFace_* 子节点上的单面墨迹贴图。
    /// </summary>
    public class InkBindingResilienceTests
    {
        private static readonly int InkTexId = Shader.PropertyToID("_InkTex");
        private static readonly int DitherAlphaId = Shader.PropertyToID("_DitherAlpha");

        [Test]
        public void OcclusionFadeCycle_KeepsInkTextureBinding()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Texture2D inkTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            try
            {
                Renderer rend = go.GetComponent<Renderer>();
                var fader = go.AddComponent<OcclusionFader>();
                fader.MinAlpha = 0.35f;
                fader.FadeSpeed = 20f;

                // 模拟 InkSurfaceController.BindSurfaceMaterials 写入的墨迹绑定
                var block = new MaterialPropertyBlock();
                block.SetTexture(InkTexId, inkTex);
                rend.SetPropertyBlock(block);

                // 被遮挡 → 淡出
                for (int i = 0; i < 10; i++)
                {
                    fader.MarkOccluded(new Vector3(1f, 0f, 2f));
                    fader.EvaluateStep(0.05f);
                }
                Assert.AreEqual(0.35f, fader.CurrentAlpha, 0.01f);

                // 走出遮挡 → 恢复不透明（历史实现就是在这里把 MPB 整个清空）
                for (int i = 0; i < 20; i++)
                {
                    fader.EvaluateStep(0.05f);
                }
                Assert.AreEqual(1f, fader.CurrentAlpha, 0.001f);

                var after = new MaterialPropertyBlock();
                rend.GetPropertyBlock(after);

                Assert.IsTrue(rend.HasPropertyBlock(), "恢复不透明后渲染器应仍持有 MaterialPropertyBlock");
                Assert.AreSame(inkTex, after.GetTexture(InkTexId),
                    "遮挡淡出/恢复不得抹掉墨迹贴图绑定（否则该表面永远喷不上墨）");
                Assert.AreEqual(1f, after.GetFloat(DitherAlphaId), 0.001f,
                    "恢复后 _DitherAlpha 应为 1（完全不透明）");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(inkTex);
            }
        }

        [Test]
        public void OcclusionFadeCycle_KeepsWallFaceTextureOnChildRenderer()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cfg = ScriptableObject.CreateInstance<InkGridConfigSO>();
            try
            {
                go.transform.position = Vector3.zero;
                var box = go.GetComponent<BoxCollider>();
                var wall = go.AddComponent<InkWallSurface>();
                wall.Configure(box, null, cfg, allowClimb: false);

                var fader = go.AddComponent<OcclusionFader>();
                fader.MinAlpha = 0.35f;
                fader.FadeSpeed = 20f;

                Assert.IsTrue(wall.InitializeIfNeeded(force: true), "应能生成立面");
                Assert.Greater(wall.FaceCount, 0);

                // 立面是运行时生成的子节点，必须在生成之后再缓存子渲染器
                fader.CacheRenderers();

                Renderer faceRenderer = FindFaceRenderer(go);
                Assert.IsNotNull(faceRenderer, "应存在 InkFace_* 立面渲染器");
                Assert.IsTrue(faceRenderer.HasPropertyBlock(), "立面渲染器初始应绑定该面的墨迹贴图");

                for (int i = 0; i < 10; i++)
                {
                    fader.MarkOccluded(new Vector3(1f, 0f, 2f));
                    fader.EvaluateStep(0.05f);
                }
                for (int i = 0; i < 20; i++)
                {
                    fader.EvaluateStep(0.05f);
                }
                Assert.AreEqual(1f, fader.CurrentAlpha, 0.001f);

                var block = new MaterialPropertyBlock();
                faceRenderer.GetPropertyBlock(block);

                Assert.IsTrue(faceRenderer.HasPropertyBlock(),
                    "恢复不透明后立面渲染器应仍持有 MaterialPropertyBlock");
                Assert.IsNotNull(block.GetTexture(InkTexId),
                    "遮挡淡出/恢复不得抹掉立面墨迹贴图（否则墙面涂了也看不见）");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(cfg);
            }
        }

        private static Renderer FindFaceRenderer(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.name.StartsWith("InkFace_"))
                {
                    return renderers[i];
                }
            }
            return null;
        }
    }
}