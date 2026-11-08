using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// 潜入墨水屏幕浸染的运行时验收：
    /// 1. HudController 自动装配到画布根、位于 HUD 内容之下、不拦截输入、默认隐藏；
    /// 2. 装配顺序不得破坏既有断言位（InkEdgeWarning=0 / LensInk=1）；
    /// 3. 潜入推进后 Graphic 启用、材质 Shader 正确；浮出后自动关闭，保证空闲零成本。
    /// </summary>
    [Category("UI")]
    public class SubmergedInkPlayTests
    {
        private static GameObject CreateCanvas()
        {
            var root = new GameObject("SubmergedInkCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator HudController_AutoBuildsSubmergedInkBehindHudContent()
        {
            GameObject root = CreateCanvas();
            var hud = root.AddComponent<HudController>();

            yield return null;
            yield return null;

            SubmergedInkView view = hud.SubmergedInk;
            Assert.IsNotNull(view, "HudController 应自动装配潜入浸染视图");
            Assert.AreSame(root.transform, view.transform.parent, "应挂在画布根，不放进 SafeArea");

            int sibling = view.transform.GetSiblingIndex();
            Assert.GreaterOrEqual(sibling, 2,
                "潜入浸染必须在低血暗角（2）之后插入，不得抢占 InkEdgeWarning=0 / LensInk=1 的断言位");

            Image image = view.GetComponent<Image>();
            Assert.IsNotNull(image, "视图必须自带 Image");
            Assert.IsFalse(image.raycastTarget, "全屏覆盖层不得拦截鼠标/触控");
            Assert.IsFalse(view.IsShowing, "没潜入墨水时不应显示");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SubmergedInk_FadesInThenDisablesGraphic()
        {
            GameObject root = CreateCanvas();
            var submergedGo = new GameObject("SubmergedInk",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            submergedGo.transform.SetParent(root.transform, false);
            var view = submergedGo.AddComponent<SubmergedInkView>();

            yield return null;

            for (int i = 0; i < 8; i++)
            {
                view.Tick(0.1f, true);
            }

            Assert.Greater(view.CurrentIntensity, 0.9f, "持续潜入应快速拉满强度");
            Assert.IsTrue(view.IsShowing, "强度超过阈值后应启用 Image");

            Image image = view.GetComponent<Image>();
            Assert.IsNotNull(image.material, "应创建运行时材质");
            Assert.AreEqual("P1/UI/SubmergedInk", image.material.shader.name,
                "材质必须使用潜入浸染 Shader");

            for (int i = 0; i < 40; i++)
            {
                view.Tick(0.1f, false);
            }

            Assert.Less(view.CurrentIntensity, 0.003f, "浮出约 2s 后强度应基本归零");
            Assert.IsFalse(view.IsShowing, "低于阈值必须关闭 Graphic，空闲零成本");

            Object.Destroy(root);
            yield return null;
        }
    }
}
