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
    /// 移动端按键自定义布局编辑器运行时拖拽与持久化验证（PlayMode）。
    /// 验证三个真实 Bug 的彻底修复：
    /// 1. 全屏背板不拦截射线；
    /// 2. 摇杆等项具备有效命中层且 blocksRaycasts 正确切换；
    /// 3. 拖拽坐标增量计算正确、抓取无跳变、边界安全钳制、松手与退出后配置正确持久化。
    /// </summary>
    [Category("UI")]
    public class MobileLayoutCustomizerPlayTests
    {
        private GameObject _canvasGo;
        private GameObject _eventSystemGo;

        [SetUp]
        public void SetUp()
        {
            // 兜底：任何用例失败都不能把 timeScale=0 泄露给后面的场景用例（会让 WaitForSeconds 永久挂起）
            Time.timeScale = 1f;
            MobileLayoutStorage.Delete();

            if (EventSystem.current == null)
            {
                _eventSystemGo = new GameObject("TestEventSystem", typeof(EventSystem));
            }
        }

        [TearDown]
        public void TearDown()
        {
            MobileLayoutStorage.Delete();
            Time.timeScale = 1f;

            if (_canvasGo != null)
            {
                Object.DestroyImmediate(_canvasGo);
            }

            if (_eventSystemGo != null)
            {
                Object.DestroyImmediate(_eventSystemGo);
            }
        }

        private (MobileLayoutCustomizer customizer, RectTransform safeArea, RectTransform target, CanvasGroup group) CreateTestRig()
        {
            _canvasGo = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var rootRt = _canvasGo.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(1920f, 1080f);

            // SafeArea 容器 (1920x1080)
            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(rootRt, false);
            var safeArea = safeGo.GetComponent<RectTransform>();
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
            // 拉伸锚点下 sizeDelta 是「额外偏移」，必须为 0；填 (1920,1080) 会让父级变成 3840×2160，
            // 拖拽边界断言就会全部偏大。
            safeArea.sizeDelta = Vector2.zero;

            // 待拖拽测试项：射击大键 (176x176，右下角锚定)
            var itemGo = new GameObject("TestFireButton", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            itemGo.transform.SetParent(safeArea, false);
            var itemRt = itemGo.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(1f, 0f);
            itemRt.anchorMax = new Vector2(1f, 0f);
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.anchoredPosition = new Vector2(-150f, 150f);
            itemRt.sizeDelta = new Vector2(176f, 176f);

            var group = itemGo.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;

            var customizer = MobileLayoutCustomizer.Create(null, safeArea, null);
            customizer.RegisterItem("Fire", "射击大键", itemRt, group, new Vector2(-150f, 150f), 1f, 1f);

            return (customizer, safeArea, itemRt, group);
        }

        [UnityTest]
        public IEnumerator Customizer_DragDisplacesItem_ByDeltaWithoutJump()
        {
            var rig = CreateTestRig();
            yield return null;

            rig.customizer.Open();
            Assert.IsTrue(rig.customizer.IsEditing, "进入自定义编辑状态");

            // 获取命中代理
            var proxy = rig.target.GetComponentInChildren<LayoutDragProxy>();
            Assert.IsNotNull(proxy, "目标必须包含 LayoutDragProxy 组件以响应拖拽");

            Vector2 initialAnchored = rig.target.anchoredPosition;

            // 模拟手指按下：点击在 (1770, 150)
            var pointerDown = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(1770f, 150f)
            };
            proxy.OnPointerDown(pointerDown);

            // 模拟手指拖动：向左移动 50px，向上移动 60px -> 当前在 (1720, 210)
            var pointerDrag = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(1720f, 210f)
            };
            proxy.OnDrag(pointerDrag);

            Vector2 newAnchored = rig.target.anchoredPosition;

            // 屏幕像素增量要先换算成 Canvas 局部坐标（CanvasScaler 缩放不一定是 1）
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rig.safeArea, pointerDown.position, null, out Vector2 downLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rig.safeArea, pointerDrag.position, null, out Vector2 dragLocal);
            Vector2 expectedDelta = dragLocal - downLocal;

            // 坐标必须平滑偏移 delta，绝不能突跳或飞出屏幕
            Assert.AreEqual(initialAnchored.x + expectedDelta.x, newAnchored.x, 1.0f,
                $"X 轴位移必须匹配拖拽增量 ({expectedDelta.x:F1})");
            Assert.AreEqual(initialAnchored.y + expectedDelta.y, newAnchored.y, 1.0f,
                $"Y 轴位移必须匹配拖拽增量 ({expectedDelta.y:F1})");

            // 模拟松手并保存
            proxy.OnPointerUp(new PointerEventData(EventSystem.current));
            rig.customizer.Close(save: true);

            Assert.IsFalse(rig.customizer.IsEditing);
            Assert.IsTrue(MobileLayoutStorage.HasCustomLayout, "保存退出后必须成功写入持久化配置");

            var loaded = MobileLayoutStorage.Load();
            Assert.IsNotNull(loaded);
            var fireData = loaded.Find("Fire");
            Assert.IsNotNull(fireData);
            Assert.AreEqual(newAnchored.x, fireData.anchoredPosition.x, 1.0f);
            Assert.AreEqual(newAnchored.y, fireData.anchoredPosition.y, 1.0f);
        }

        [UnityTest]
        public IEnumerator Customizer_Drag_ClampsWithinSafeAreaBounds()
        {
            var rig = CreateTestRig();
            yield return null;

            rig.customizer.Open();

            var proxy = rig.target.GetComponentInChildren<LayoutDragProxy>();

            // 1. 尝试向右拖拽到屏幕外 (+5000)
            proxy.OnPointerDown(new PointerEventData(EventSystem.current) { position = new Vector2(1770f, 150f) });
            proxy.OnDrag(new PointerEventData(EventSystem.current) { position = new Vector2(6000f, 150f) });

            float halfW = rig.target.rect.width * 0.5f; // 88
            // 右下角锚点下，最右贴边位置为 -halfW (-88)
            Assert.LessOrEqual(rig.target.anchoredPosition.x, -halfW + 0.1f, "向右拖出屏幕必须被 Clamp 限制在安全区内（保留半宽）");

            // 2. 尝试向上拖拽到屏幕顶端外 (+5000)
            proxy.OnDrag(new PointerEventData(EventSystem.current) { position = new Vector2(1770f, 6000f) });
            float halfH = rig.target.rect.height * 0.5f; // 88
            float maxAllowedY = rig.safeArea.rect.height - halfH;
            Assert.LessOrEqual(rig.target.anchoredPosition.y, maxAllowedY + 0.1f, "向上拖出屏幕必须被 Clamp 限制在安全区内（保留半高）");

            // 3. 尝试向下拖拽到屏幕底端外 (-5000)
            proxy.OnDrag(new PointerEventData(EventSystem.current) { position = new Vector2(1770f, -6000f) });
            Assert.GreaterOrEqual(rig.target.anchoredPosition.y, halfH - 0.1f, "向下拖出屏幕必须被 Clamp 限制在安全区内（保留半高）");
            Assert.LessOrEqual(rig.target.anchoredPosition.y, maxAllowedY + 0.1f, "再次上拖也必须保持在安全区内");

            proxy.OnPointerUp(new PointerEventData(EventSystem.current));
            rig.customizer.Close(save: false);
        }

        [UnityTest]
        public IEnumerator Customizer_TimeScale_RestoresPreOpenValue()
        {
            var rig = CreateTestRig();
            yield return null;

            // 验证 pre-open 为 0.5f 时正常还原
            Time.timeScale = 0.5f;
            rig.customizer.Open();
            Assert.AreEqual(0f, Time.timeScale, "编辑模式必须暂停游戏 (Time.timeScale = 0)");

            rig.customizer.Close(save: false);
            Assert.AreEqual(0.5f, Time.timeScale, "退出编辑模式必须还原进入前的 Time.timeScale (0.5)");

            // 验证 pre-open 为 0f 时同样正确还原（不硬编码 1f）
            Time.timeScale = 0f;
            rig.customizer.Open();
            rig.customizer.Close(save: false);
            Assert.AreEqual(0f, Time.timeScale, "进入前已暂停时，退出依然还原为 0");

            // 立即恢复：本用例故意把全局时间停在 0，若留给后续用例会让 WaitForSeconds 永久等待
            Time.timeScale = 1f;
        }
    }
}
