using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// P18-A 踩敌墨全屏边缘墨迹的运行时验收：
    /// 1. HudController 自动装配到画布根、位于 HUD 内容之前、不拦截输入、默认隐藏；
    /// 2. 强度推进后 Graphic 启用、材质 Shader 正确；离开敌墨后自动关闭，保证空闲零成本。
    /// </summary>
    [Category("UI")]
    public class InkEdgeWarningPlayTests
    {
        private static GameObject CreateCanvas()
        {
            var root = new GameObject("InkEdgeWarningCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator HudController_AutoBuildsInkEdgeWarningBehindHudContent()
        {
            GameObject root = CreateCanvas();
            var hud = root.AddComponent<HudController>();

            yield return null;
            yield return null;

            InkEdgeWarningView view = hud.InkEdgeWarning;
            Assert.IsNotNull(view, "HudController 应自动装配踩敌墨边缘提示");
            Assert.AreEqual(0, view.transform.GetSiblingIndex(),
                "边缘提示必须位于 HUD 内容之前绘制（盖住 3D、不挡 HUD）");
            Assert.AreSame(root.transform, view.transform.parent, "边缘提示应挂在画布根，不放进 SafeArea");

            Image image = view.GetComponent<Image>();
            Assert.IsNotNull(image, "视图必须自带 Image");
            Assert.IsFalse(image.raycastTarget, "全屏覆盖层不得拦截鼠标/触控");
            Assert.IsFalse(view.IsShowing, "没有踩敌墨时不应显示");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InkEdgeWarning_FadesInThenDisablesGraphic()
        {
            GameObject root = CreateCanvas();
            var edgeGo = new GameObject("InkEdgeWarning",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            edgeGo.transform.SetParent(root.transform, false);
            var view = edgeGo.AddComponent<InkEdgeWarningView>();

            yield return null;

            for (int i = 0; i < 8; i++)
            {
                view.Tick(0.1f, true);
            }

            Assert.Greater(view.CurrentIntensity, 0.9f, "连续踩敌墨应快速拉满强度");
            Assert.IsTrue(view.IsShowing, "强度超过阈值后应启用 Image");

            Image image = view.GetComponent<Image>();
            Assert.IsNotNull(image.material, "应创建运行时材质");
            Assert.AreEqual("P1/UI/InkEdgeWarning", image.material.shader.name,
                "材质必须使用踩敌墨边缘 Shader");

            for (int i = 0; i < 40; i++)
            {
                view.Tick(0.1f, false);
            }

            Assert.Less(view.CurrentIntensity, 0.003f, "离开敌墨约 2s 后强度应基本归零");
            Assert.IsFalse(view.IsShowing, "低于阈值必须关闭 Graphic，空闲零成本");

            Object.Destroy(root);
            yield return null;
        }
    }
}
