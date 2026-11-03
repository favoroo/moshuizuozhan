using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.UI;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    [Category("UI")]
    public class UIChunkyTextPlayTests
    {
        private static GameObject CreateCanvas()
        {
            var root = new GameObject("TestCanvas", typeof(Canvas));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return root;
        }

        [UnityTest]
        public IEnumerator Configure_DoubleLayerFalse_DeactivatesFillLayer()
        {
            GameObject canvas = CreateCanvas();
            var go = new GameObject("TestText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI), typeof(UIChunkyText));
            go.transform.SetParent(canvas.transform, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            var chunky = go.GetComponent<UIChunkyText>();

            yield return null;

            // 此时调用 Configure 配置单层模式
            chunky.Configure(null, "150", 36f, displayFont: true, doubleLayered: false,
                fill: ComicStyle.Ink, outlinedText: false);

            yield return null;

            Transform fillChild = go.transform.Find("ChunkyFill");
            Assert.IsNotNull(fillChild, "ChunkyFill 节点在 Awake 时已建立");
            Assert.IsFalse(fillChild.gameObject.activeSelf, "单层模式下 ChunkyFill 必须被禁用，不能出现重影");
            Assert.AreEqual(ComicStyle.Ink, tmp.color, "单层模式下父文本颜色应为 fill 颜色");

            Object.Destroy(canvas);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Configure_DoubleLayerTrue_ActivatesFillLayer()
        {
            GameObject canvas = CreateCanvas();
            var go = new GameObject("TestText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI), typeof(UIChunkyText));
            go.transform.SetParent(canvas.transform, false);

            var chunky = go.GetComponent<UIChunkyText>();
            yield return null;

            chunky.Configure(null, "VICTORY", 48f, displayFont: true, doubleLayered: true,
                fill: ComicStyle.Yellow, outlinedText: true);

            yield return null;

            Transform fillChild = go.transform.Find("ChunkyFill");
            Assert.IsNotNull(fillChild, "双层模式下 ChunkyFill 节点必须存在");
            Assert.IsTrue(fillChild.gameObject.activeSelf, "双层模式下 ChunkyFill 必须保持激活");

            // 底层是墨色描边层：即使外部按旧写法给 TMP 染色，SetFillColor 也必须拉回描边色，
            // 否则上下两层同色 + 4px 硬影偏移会读成「重影」。
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.color = ComicStyle.Yellow;
            chunky.SetFillColor(ComicStyle.Yellow);
            yield return null;
            Assert.AreEqual(UIThemeSO.Fallback.outlineColor, tmp.color,
                "双层模式下底层 TMP 必须保持墨色描边，不能跟随填充色");

            Object.Destroy(canvas);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ToggleDoubleLayer_SwitchesFillLayerActiveState()
        {
            GameObject canvas = CreateCanvas();
            var go = new GameObject("TestText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI), typeof(UIChunkyText));
            go.transform.SetParent(canvas.transform, false);

            var chunky = go.GetComponent<UIChunkyText>();
            yield return null;

            chunky.Configure(null, "100", 36f, displayFont: true, doubleLayered: false);
            yield return null;
            Transform fillChild = go.transform.Find("ChunkyFill");
            Assert.IsNotNull(fillChild);
            Assert.IsFalse(fillChild.gameObject.activeSelf, "配置为单层时禁用");

            chunky.Configure(null, "100", 36f, displayFont: true, doubleLayered: true);
            yield return null;
            Assert.IsTrue(fillChild.gameObject.activeSelf, "切换为双层时激活");

            chunky.Configure(null, "150", 36f, displayFont: true, doubleLayered: false);
            yield return null;
            Assert.IsFalse(fillChild.gameObject.activeSelf, "再次切回单层时再次禁用");

            Object.Destroy(canvas);
            yield return null;
        }
    }
}
