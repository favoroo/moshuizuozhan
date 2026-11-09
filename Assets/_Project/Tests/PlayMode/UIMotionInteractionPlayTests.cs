using System.Collections;
using NUnit.Framework;
using P1.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// [UI] 交互态不允许跨「隐藏 → 重新显示」残留（首页「武器整备」卡贴边 / 压住难度行的根因）：
    /// 点整备卡会立刻 <c>SetActive(false)</c>（抽屉接管画面），触摸抬手时 PointerExit 已经没有接收者；
    /// 若不清交互态，重新显示时上一次被中断的 0.95→1.05 补间会继续跑完，满宽卡片永久停在
    /// hoverScale(1.05) —— 右缘顶到 Hero 卡边框上、下缘压住难度行。
    /// </summary>
    [Category("UI")]
    public class UIMotionInteractionPlayTests
    {
        private GameObject _canvasGo;
        private GameObject _cardGo;
        private GameObject _eventSystemGo;
        private UISpringMotion _motion;

        [SetUp]
        public void SetUp()
        {
            if (EventSystem.current == null)
            {
                _eventSystemGo = new GameObject("MotionTestEventSystem", typeof(EventSystem));
            }

            _canvasGo = new GameObject("MotionTestCanvas", typeof(Canvas), typeof(CanvasRenderer),
                typeof(GraphicRaycaster), typeof(CanvasScaler));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // 与首页整备卡同构：RectTransform + 背景 Graphic + Button + UISpringMotion + UIJellyButton
            _cardGo = new GameObject("MotionProbe", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            _cardGo.transform.SetParent(_canvasGo.transform, false);
            ((RectTransform)_cardGo.transform).sizeDelta = new Vector2(860f, 150f);
            _motion = _cardGo.GetComponent<UISpringMotion>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_cardGo != null) Object.Destroy(_cardGo);
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_eventSystemGo != null) Object.Destroy(_eventSystemGo);
        }

        [UnityTest]
        public IEnumerator HoverScale_DoesNotSurviveHideAndShow()
        {
            _motion.SetHovered(true);
            yield return WaitForSettle();
            Assert.That(_cardGo.transform.localScale.x, Is.GreaterThan(1.01f), "前置条件：悬停应放大到 1.05");

            // 复现「点开抽屉 → 卡片隐藏 → 关闭抽屉 → 卡片重新显示」
            _cardGo.SetActive(false);
            _cardGo.SetActive(true);
            yield return WaitForSettle();

            Assert.That(_cardGo.transform.localScale.x, Is.EqualTo(1f).Within(0.001f),
                "重新显示的卡片必须回到常态缩放，否则满宽卡片会顶出容器内边距");
        }

        [UnityTest]
        public IEnumerator TouchPressThenHide_DoesNotBringScaleBack()
        {
            // 完整触屏路径：enter（悬停）→ down（按下）→ up（点击回调隐藏卡片）→ 重新显示
            var data = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(_cardGo, data, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(_cardGo, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(_cardGo, data, ExecuteEvents.pointerUpHandler);

            _cardGo.SetActive(false);
            yield return null;
            _cardGo.SetActive(true);
            yield return WaitForSettle();

            Assert.That(_cardGo.transform.localScale.x, Is.EqualTo(1f).Within(0.001f),
                "按下即隐藏的按钮不能把悬停/按压缩放带回首页");
        }

        [UnityTest]
        public IEnumerator HoverScaleOverride_KeepsFullWidthCardAtHomeScale()
        {
            _motion.SetHoverScale(1f); // 满宽卡片（铺满容器内宽）的用法
            _motion.SetHovered(true);
            yield return WaitForSettle();

            Assert.That(_cardGo.transform.localScale.x, Is.EqualTo(1f).Within(0.001f),
                "满宽卡片悬停不应放大，否则右缘会压到容器边框上");
        }

        /// <summary>悬停 0.1s / 回弹 0.1s / 入场 0.22s —— 0.3s 足够让缩放收敛。</summary>
        private static IEnumerator WaitForSettle()
        {
            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
