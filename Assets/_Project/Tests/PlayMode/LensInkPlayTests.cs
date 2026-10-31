using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// 镜头墨滴与全屏转场的 PlayMode 验收：
    /// 1. HudController 自动装配到画布根、层级顺序满足要求、不拦截输入；
    /// 2. Shader 缺失时不报错且静默隐藏 Graphic；
    /// 3. 转场与墨滴在无活动对象时自动关闭 Graphic。
    /// </summary>
    [Category("UI")]
    public class LensInkPlayTests
    {
        private static GameObject CreateCanvas()
        {
            var root = new GameObject("ScreenFxCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator HudController_AutoBuildsLensInkAndTransitionInProperSiblingOrder()
        {
            GameObject root = CreateCanvas();
            var hud = root.AddComponent<HudController>();

            yield return null;
            yield return null;

            // 1. 验证踩墨边缘仍然独占 sibling 0
            InkEdgeWarningView edgeView = hud.InkEdgeWarning;
            Assert.IsNotNull(edgeView, "HudController 必须自动装配踩墨边缘");
            Assert.AreEqual(0, edgeView.transform.GetSiblingIndex(),
                "踩墨边缘必须保持独占 Sibling 0（满足既有用例断言）");

            // 2. 验证镜头墨滴挂在画布根并位于 HUD 内容之下
            LensInkView lensView = hud.LensInk;
            Assert.IsNotNull(lensView, "HudController 必须自动装配镜头墨滴视图");
            Assert.AreSame(root.transform, lensView.transform.parent, "镜头墨滴必须挂在画布根");
            Assert.AreEqual(1, lensView.transform.GetSiblingIndex(),
                "镜头墨滴必须位于 Sibling 1（盖在墨边之上、HUD 内容之下）");

            Image lensImg = lensView.GetComponent<Image>();
            Assert.IsNotNull(lensImg);
            Assert.IsFalse(lensImg.raycastTarget, "镜头墨滴层不得拦截点击输入");
            Assert.IsFalse(lensView.IsShowing, "初始无墨滴时必须保持隐藏");

            // 3. 验证转场视图挂在画布根并作为顶层遮罩
            ScreenInkTransitionView transView = hud.ScreenInkTransition;
            Assert.IsNotNull(transView, "HudController 必须自动装配屏幕转场视图");
            Assert.AreSame(root.transform, transView.transform.parent, "转场视图必须挂在画布根");

            Image transImg = transView.GetComponent<Image>();
            Assert.IsNotNull(transImg);
            Assert.IsFalse(transImg.raycastTarget, "转场遮罩层不得拦截点击输入");
            Assert.IsFalse(transView.IsShowing, "初始非转场阶段必须保持隐藏");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LensInk_NoActiveDrops_DisablesGraphic()
        {
            GameObject root = CreateCanvas();
            var lensGo = new GameObject("LensInk",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lensGo.transform.SetParent(root.transform, false);

            var lensView = lensGo.AddComponent<LensInkView>();
            lensView.EnsureBuilt();

            yield return null;

            Assert.IsFalse(lensView.IsShowing, "无墨滴时 Graphic 必须禁用，空闲零开销");

            // 添加一滴
            lensView.AddDroplet(LensInkView.ChannelEnemy, 0.5f, 0.5f, 0.05f, 0.1f, 99f);
            lensView.Tick(0.016f);

            Assert.IsTrue(lensView.IsShowing, "添加墨滴后 Graphic 必须启用");

            // 等待挥发归零
            for (int i = 0; i < 40; i++)
            {
                lensView.Tick(0.05f);
            }

            Assert.IsFalse(lensView.IsShowing, "墨滴全部挥发后 Graphic 必须自动关闭");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScreenInkTransition_RevealClearsAndDisablesGraphic()
        {
            GameObject root = CreateCanvas();
            var transGo = new GameObject("ScreenInkTransition",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            transGo.transform.SetParent(root.transform, false);

            var transView = transGo.AddComponent<ScreenInkTransitionView>();
            transView.EnsureBuilt();

            yield return null;

            Assert.IsFalse(transView.IsShowing, "初始状态下 Graphic 必须关闭");

            transView.TriggerRespawnReveal(Color.blue);
            Assert.IsTrue(transView.IsShowing, "触发复活撕裂转场后必须显示");

            // 推进 0.9s 完成虹膜完全打开
            for (int i = 0; i < 60; i++)
            {
                transView.Tick(0.016f);
            }

            Assert.IsFalse(transView.IsShowing, "复活破口完全张开后 Graphic 必须自动关闭放行画面");

            Object.Destroy(root);
            yield return null;
        }
    }
}
