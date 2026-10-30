using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.Tests
{
    [TestFixture]
    public class MobileTouchUITests
    {
        [Test]
        public void ActionButtons_AreAnchoredToBottomRight_AndHaveNoOverlaps()
        {
            // 验证从 MobileTouchUI.DefaultLayout 读取的 5 个主操作按键扇形排布均在右下角且无重叠
            var layout = MobileTouchUI.DefaultLayout;
            Assert.IsNotNull(layout, "MobileTouchUI.DefaultLayout 不可为空");
            Assert.AreEqual(5, layout.Length, "默认排布表必须包含 5 个主操作按键");

            for (int i = 0; i < layout.Length; i++)
            {
                // 所有按键均在屏幕右下角区域（X 为负相对右边距，Y 为正相对底边距）
                Assert.Less(layout[i].pos.x, 0f, $"{layout[i].id} 必须以右下角锚定 (负 X)");
                Assert.Greater(layout[i].pos.y, 0f, $"{layout[i].id} 必须在下半屏 (正 Y)");
                Assert.Less(layout[i].pos.y, 500f, $"{layout[i].id} 绝不能飞到上半屏遮挡战况条与小地图");

                for (int j = i + 1; j < layout.Length; j++)
                {
                    float dist = Vector2.Distance(layout[i].pos, layout[j].pos);
                    float minAllowedDist = (layout[i].size + layout[j].size) * 0.5f - 1f; // 允许微接触，不允许严重重叠
                    Assert.GreaterOrEqual(dist, minAllowedDist,
                        $"{layout[i].id} 与 {layout[j].id} 发生重叠 (距离 {dist} < 最小允许 {minAllowedDist})");
                }
            }
        }

        [Test]
        public void MobileLayoutCustomizer_Backdrop_DoesNotBlockRaycasts()
        {
            // 验证修复 Bug 1：编辑模式背板 Image 的 raycastTarget 必须为 false，杜绝吞掉 PointerDown
            var go = new GameObject("TestCustomizerHost", typeof(RectTransform));
            var safeArea = go.GetComponent<RectTransform>();
            var customizer = MobileLayoutCustomizer.Create(null, safeArea, null);

            var bgImg = customizer.GetComponent<Image>();
            Assert.IsNotNull(bgImg, "Customizer 根物体必须包含背景 Image");
            Assert.IsFalse(bgImg.raycastTarget, "全屏背板 raycastTarget 必须为 false，避免吞掉按键与代理的 PointerDown");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MobileLayoutCustomizer_Open_EnablesBlocksRaycastsForRegisteredItems_AndRestoresOnClose()
        {
            // 验证修复 Bug 2：摇杆 CanvasGroup 在编辑态下临时开放 blocksRaycasts，退出时精确恢复
            var go = new GameObject("TestHost", typeof(RectTransform));
            var safeArea = go.GetComponent<RectTransform>();

            var itemGo = new GameObject("TestItem", typeof(RectTransform), typeof(CanvasGroup));
            itemGo.transform.SetParent(safeArea, false);
            var itemGroup = itemGo.GetComponent<CanvasGroup>();
            itemGroup.blocksRaycasts = false; // 初始为 false（如摇杆 VisualContainer）

            var customizer = MobileLayoutCustomizer.Create(null, safeArea, null);
            customizer.RegisterItem("Stick", "摇杆", itemGo.GetComponent<RectTransform>(), itemGroup, Vector2.zero, 1f, 1f);

            customizer.Open();
            Assert.IsTrue(itemGroup.blocksRaycasts, "进入编辑模式时必须将 blocksRaycasts 置 true 以接收点击与拖拽");

            customizer.Close(save: false);
            Assert.IsFalse(itemGroup.blocksRaycasts, "退出编辑模式后必须恢复原始 blocksRaycasts 状态 (false)");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MobileUiMetrics_ComputeScale_ClampsWithinExpectedRange()
        {
            // 验证 DPI 物理毫米自适应与 0.85~1.25 钳制
            float standardScale = MobileUiMetrics.ComputeScale(400f, 1.0f);
            Assert.GreaterOrEqual(standardScale, 0.85f);
            Assert.LessOrEqual(standardScale, 1.25f);

            // 极端超低 DPI -> 钳制在 0.85 下限
            float lowScale = MobileUiMetrics.ComputeScale(160f, 1.0f);
            Assert.AreEqual(0.85f, lowScale, 1e-4f);

            // 极端超高 DPI -> 钳制在 1.25 上限
            float highScale = MobileUiMetrics.ComputeScale(600f, 1.0f);
            Assert.AreEqual(1.25f, highScale, 1e-4f);

            // 非法 DPI -> 兜底按 400
            float fallbackScale = MobileUiMetrics.ComputeScale(-5f, 1.0f);
            Assert.AreEqual(standardScale, fallbackScale, 1e-4f);

            // 物理毫米在目标 11~13mm 范围内
            float primaryMm = MobileUiMetrics.UiUnitsToMillimeters(176f * standardScale, 1.0f, 400f);
            Assert.GreaterOrEqual(primaryMm, 11f);
            Assert.LessOrEqual(primaryMm, 13f);
        }

        [Test]
        public void DynamicFloatingJoystick_ValueCalculatesCorrectly_WithDeadZone()
        {
            var go = new GameObject("TestJoystickHost", typeof(RectTransform));
            var safeArea = go.GetComponent<RectTransform>();
            safeArea.sizeDelta = new Vector2(1920f, 1080f);

            var joystick = DynamicFloatingJoystick.Create(
                safeArea,
                new Vector2(200f, 200f),
                100f,
                10f,
                null,
                null,
                null);

            Assert.AreEqual(Vector2.zero, joystick.Value, "未触碰时摇杆初始值必须为零");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RightAimSwipeZone_AccumulatesPerFrameDelta_AndConsumes()
        {
            var go = new GameObject("TestSwipeZoneHost", typeof(RectTransform));
            var safeArea = go.GetComponent<RectTransform>();
            safeArea.sizeDelta = new Vector2(1920f, 1080f);

            var zone = RightAimSwipeZone.Create(safeArea);

            zone.OnPointerDown(new PointerEventData(EventSystem.current));

            var dragA = new PointerEventData(EventSystem.current) { delta = new Vector2(12f, -5f) };
            var dragB = new PointerEventData(EventSystem.current) { delta = new Vector2(3f, 4f) };
            zone.OnDrag(dragA);
            zone.OnDrag(dragB);

            Vector2 delta = zone.ConsumeLookDelta();
            Assert.AreEqual(15f, delta.x, 1e-4f, "同一帧多次拖拽应累加滑屏增量");
            Assert.AreEqual(-1f, delta.y, 1e-4f);
            Assert.AreEqual(Vector2.zero, zone.ConsumeLookDelta(), "取出后必须清零，避免跨帧持续转视角");

            zone.OnPointerUp(new PointerEventData(EventSystem.current));
            Assert.IsFalse(zone.HasAimInput);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MobileActionButton_TapConsume_OnlyTriggersOnce()
        {
            var go = new GameObject("TestButtonHost", typeof(RectTransform));
            var btn = MobileActionButton.Create(
                go.transform, "TestBtn", "测试",
                new Vector2(-100f, 100f), 100f, Color.white,
                null, null, null, MobileButtonMode.Tap);

            Assert.IsFalse(btn.ConsumeTap(), "初始状态不应有未消费的 Tap");

            // 模拟 PointerDown
            btn.OnPointerDown(new PointerEventData(EventSystem.current));
            Assert.IsTrue(btn.IsHeld, "PointerDown 后 IsHeld 必须为 true");
            Assert.IsTrue(btn.ConsumeTap(), "首次调用 ConsumeTap 应返回 true");
            Assert.IsFalse(btn.ConsumeTap(), "连续调用 ConsumeTap 应已被消费，返回 false");

            btn.OnPointerUp(new PointerEventData(EventSystem.current));
            Assert.IsFalse(btn.IsHeld, "PointerUp 后 IsHeld 必须恢复为 false");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SpecialButton_VisualState_DifferentiatesReadyAndCharging()
        {
            var go = new GameObject("TestSpecialHost", typeof(RectTransform));
            var btn = MobileActionButton.Create(
                go.transform, "SpecialBtn", "大招",
                new Vector2(-300f, 300f), 120f, new Color(1f, 0.8f, 0.2f, 1f),
                null, null, null, MobileButtonMode.Special);

            var canvasGroup = btn.GetComponent<CanvasGroup>();

            // 充能 45%：置灰半透明
            btn.UpdateSpecialState(false, 0.45f);
            Assert.Less(canvasGroup.alpha, 0.8f, "大招未就绪时必须半透明置灰");

            // 就绪 100%：高亮发光
            btn.UpdateSpecialState(true, 1.0f);
            Assert.AreEqual(1.0f, canvasGroup.alpha, "大招就绪时必须满不透明度");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void HudController_ApplyMobileLayout_HidesPcHintAndShrinksBottomPanel()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas), typeof(HudController));
            var hud = canvasGo.GetComponent<HudController>();

            var bottomPanel = new GameObject("BottomStatusPanel", typeof(RectTransform), typeof(Image));
            bottomPanel.transform.SetParent(canvasGo.transform, false);
            var panelRt = bottomPanel.GetComponent<RectTransform>();
            panelRt.sizeDelta = new Vector2(760f, 106f);

            var hintGo = new GameObject("ControlsHintText", typeof(RectTransform), typeof(Text));
            hintGo.transform.SetParent(bottomPanel.transform, false);
            var hintText = hintGo.GetComponent<Text>();
            hintText.text = "操作：[WASD]移动";

            var inkBg = new GameObject("InkBarBg", typeof(RectTransform), typeof(Image));
            inkBg.transform.SetParent(bottomPanel.transform, false);

            var specBg = new GameObject("SpecialBarBg", typeof(RectTransform), typeof(Image));
            specBg.transform.SetParent(bottomPanel.transform, false);

            var hpBg = new GameObject("HealthBarBg", typeof(RectTransform), typeof(Image));
            hpBg.transform.SetParent(bottomPanel.transform, false);

            // 应用移动端布局适配
            hud.ApplyMobileLayout();

            Assert.IsFalse(hintGo.activeSelf, "PC WASD 提示文字必须在移动端被隐藏");
            Assert.IsFalse(inkBg.activeSelf, "与右上角重复的旧墨水条必须在移动端被隐藏");
            Assert.IsFalse(specBg.activeSelf, "与右上角重复的旧大招条必须在移动端被隐藏");
            Assert.Less(panelRt.sizeDelta.x, 400f, "底部面板宽度必须收敛到 400px 以内避免遮挡左右触控区");

            Object.DestroyImmediate(canvasGo);
        }

        [Test]
        public void MobileLayoutStorage_SaveAndLoad_PreservesCustomPositionsAndScales()
        {
            var config = new MobileLayoutConfig
            {
                leftFireVisible = true,
                preset = (int)MobileLayoutPreset.ThreeFinger
            };
            config.Set("Fire", new Vector2(-180f, 190f), 1.25f, 0.85f);
            config.Set("Jump", new Vector2(-130f, 330f), 0.90f, 0.70f);

            MobileLayoutStorage.Save(config);

            Assert.IsTrue(MobileLayoutStorage.HasCustomLayout, "保存后 HasCustomLayout 应为 true");
            var loaded = MobileLayoutStorage.Load();
            Assert.IsNotNull(loaded, "读取配置不应为空");

            var fireData = loaded.Find("Fire");
            Assert.IsNotNull(fireData, "必须找到 Fire 按键配置");
            Assert.AreEqual(-180f, fireData.anchoredPosition.x, 0.01f);
            Assert.AreEqual(190f, fireData.anchoredPosition.y, 0.01f);
            Assert.AreEqual(1.25f, fireData.scale, 0.01f);
            Assert.AreEqual(0.85f, fireData.alpha, 0.01f);
            Assert.IsTrue(loaded.leftFireVisible, "左开火键显隐必须随布局一起持久化");
            Assert.AreEqual((int)MobileLayoutPreset.ThreeFinger, loaded.preset, "方案编号必须随布局持久化");

            MobileLayoutStorage.Delete();
            Assert.IsFalse(MobileLayoutStorage.HasCustomLayout, "清理后 HasCustomLayout 应为 false");
        }

        [Test]
        public void MobileLayoutConfig_ClampsScaleAndAlpha_ToSafeLimits()
        {
            var config = new MobileLayoutConfig();
            config.Set("Test", Vector2.zero, 99f, -10f);

            var item = config.Find("Test");
            Assert.IsNotNull(item);
            Assert.LessOrEqual(item.scale, 1.80f, "最大缩放不可超过 1.8 倍避免遮挡全屏");
            Assert.GreaterOrEqual(item.alpha, 0.25f, "最小透明度不可低于 0.25 避免按键彻底隐形");
        }

        [Test]
        public void DynamicFloatingJoystick_And_RightAimSwipeZone_ResetProperlyOnDisable()
        {
            var go = new GameObject("TestResetHost", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();

            var joystick = DynamicFloatingJoystick.Create(rt, new Vector2(200f, 200f), 100f, 10f, null, null, null);
            joystick.OnPointerDown(new PointerEventData(EventSystem.current) { position = new Vector2(250f, 250f) });
            joystick.OnDrag(new PointerEventData(EventSystem.current) { position = new Vector2(280f, 280f) });
            Assert.AreNotEqual(Vector2.zero, joystick.Value, "拖拽后应产生位移输入");

            joystick.ResetJoystick();
            Assert.AreEqual(Vector2.zero, joystick.Value, "ResetJoystick 后输入必须归零");

            var zone = RightAimSwipeZone.Create(rt);
            zone.OnPointerDown(new PointerEventData(EventSystem.current));
            zone.OnDrag(new PointerEventData(EventSystem.current) { delta = new Vector2(10f, 10f) });
            Assert.IsTrue(zone.HasAimInput);

            zone.ResetZone();
            Assert.IsFalse(zone.HasAimInput, "ResetZone 后滑动状态必须清除");
            Assert.AreEqual(Vector2.zero, zone.ConsumeLookDelta(), "增量必须归零");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MobileLayoutCustomizer_Close_FiresOnClosedEvent()
        {
            var go = new GameObject("TestCustomizerHost", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            var customizer = MobileLayoutCustomizer.Create(null, rt, null);

            bool closedFired = false;
            customizer.OnClosed += () => closedFired = true;

            customizer.Open();
            Assert.IsTrue(customizer.IsEditing);

            customizer.Close(save: false);
            Assert.IsFalse(customizer.IsEditing);
            Assert.IsTrue(closedFired, "Close 后必须派发 OnClosed 事件以通知上层恢复阶段显隐");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MobileTouchUI_VisibilityFollowsMatchPhase()
        {
            var matchGo = new GameObject("TestMatchHost", typeof(MatchManager));
            var match = matchGo.GetComponent<MatchManager>();

            var touchGo = new GameObject("TestTouchHost", typeof(MobileTouchUI));
            var touchUI = touchGo.GetComponent<MobileTouchUI>();

            var showField = typeof(MobileTouchUI).GetField("showTouchUiInEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            showField.SetValue(touchUI, true);

            var awakeMethod = typeof(MobileTouchUI).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            awakeMethod.Invoke(touchUI, null);

            var startMethod = typeof(MobileTouchUI).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            startMethod.Invoke(touchUI, null);

            // 1. Ready 阶段必须隐藏
            Assert.IsFalse(touchUI.IsShown, "Ready 首页阶段触控操作层必须隐藏，绝不遮挡开始比赛与选枪按钮");

            // 2. Countdown 与 Playing 阶段必须显示
            var setPhaseMethod = typeof(MatchManager).GetMethod("SetPhase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            setPhaseMethod.Invoke(match, new object[] { MatchManager.Phase.Countdown });
            Assert.IsTrue(touchUI.IsShown, "Countdown 倒计时阶段触控按键必须显示");

            setPhaseMethod.Invoke(match, new object[] { MatchManager.Phase.Playing });
            Assert.IsTrue(touchUI.IsShown, "Playing 游玩阶段触控按键必须显示");

            // 3. Finished 与 Result 阶段必须隐藏
            setPhaseMethod.Invoke(match, new object[] { MatchManager.Phase.Finished });
            Assert.IsFalse(touchUI.IsShown, "Finished 定格阶段触控按键必须隐藏");

            setPhaseMethod.Invoke(match, new object[] { MatchManager.Phase.Result });
            Assert.IsFalse(touchUI.IsShown, "Result 结算阶段触控按键必须隐藏，避免遮挡结算比分");

            // 4. 在 Ready 阶段打开自定义键位后应临时显示，关闭后应恢复隐藏
            setPhaseMethod.Invoke(match, new object[] { MatchManager.Phase.Ready });
            Assert.IsFalse(touchUI.IsShown);

            touchUI.OpenLayoutCustomizer();
            Assert.IsTrue(touchUI.IsShown, "在 Ready 阶段打开自定义键位时应临时展示画布与按键供玩家调整");

            touchUI.Customizer.Close(save: false);
            Assert.IsFalse(touchUI.IsShown, "关闭自定义键位后应根据 Ready 阶段恢复隐藏");

            Object.DestroyImmediate(touchGo);
            Object.DestroyImmediate(matchGo);
        }

        [Test]
        public void MobileLayoutCustomizer_HitProxy_MustStayInactiveOutsideEditing()
        {
            // 回归用例：HitProxy 常驻激活会吞掉按键的 PointerDown。
            // LayoutDragProxy 实现了 IPointerDownHandler，而 EventSystem 的 ExecuteHierarchy
            // 在命中层就会找到处理器并停止冒泡，父节点的 MobileActionButton 永远收不到按下 ——
            // 线上表现为「右下角所有操作按钮一个都点不动」。
            var host = new GameObject("TestHitProxyHost", typeof(RectTransform));
            var btn = MobileActionButton.Create(
                host.transform, "FireButton", "射击",
                new Vector2(-150f, 150f), 176f, Color.red,
                null, null, null, MobileButtonMode.HoldWithAimDrag);

            var customizer = MobileLayoutCustomizer.Create(null, host.GetComponent<RectTransform>(), null);
            customizer.RegisterItem("Fire", "射击大键", btn.RectTransform, btn.CanvasGroup,
                new Vector2(-150f, 150f), 1f, 1f);

            var hitProxy = btn.RectTransform.Find("HitProxy");
            Assert.IsNotNull(hitProxy, "RegisterItem 必须为按键生成透明命中层");
            Assert.IsNotNull(hitProxy.GetComponent<LayoutDragProxy>(), "命中层必须挂载 LayoutDragProxy 才能拖拽");
            Assert.IsFalse(hitProxy.gameObject.activeSelf,
                "游玩期透明命中层必须隐藏，否则它会吞掉按键的 PointerDown");

            // 命中层一旦激活，事件处理器就落在它身上（复现线上故障的机制）
            hitProxy.gameObject.SetActive(true);
            var blockedHandler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hitProxy.gameObject);
            Assert.AreNotSame(btn.gameObject, blockedHandler,
                "命中层激活时新增的 LayoutDragProxy 会接管按下事件，确认这正是需要隐藏它的原因");
            hitProxy.gameObject.SetActive(false);

            // 隐藏后，真实射线命中的 Bg 必须冒泡到按键本体
            var bg = btn.RectTransform.Find("Content/Bg");
            Assert.IsNotNull(bg, "按键必须包含可接收射线的 Bg");
            var handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(bg.gameObject);
            Assert.AreSame(btn.gameObject, handler,
                "命中层隐藏后，按下 Bg 必须由 MobileActionButton 接收，操作按钮才能响应");

            // 编辑模式启用 / 关闭后收回
            customizer.Open();
            Assert.IsTrue(hitProxy.gameObject.activeSelf,
                "进入自定义键位编辑时必须启用命中层，保证整块按键（含透明圆角）可拖拽");

            customizer.Close(save: false);
            Assert.IsFalse(hitProxy.gameObject.activeSelf,
                "退出编辑后必须立即收回命中层，恢复玩法期按键点击");

            Object.DestroyImmediate(host);
        }

        [Test]
        public void TouchSlider_NormalizeLocalX_HandlesBothPivotModes()
        {
            // pivot=(0.5,0.5)：rect.xMin=-85、width=170，中心 0 应映射到 50%
            Assert.AreEqual(0f, TouchSlider.NormalizeLocalX(-85f, -85f, 170f), 1e-4f);
            Assert.AreEqual(0.5f, TouchSlider.NormalizeLocalX(0f, -85f, 170f), 1e-4f);
            Assert.AreEqual(1f, TouchSlider.NormalizeLocalX(85f, -85f, 170f), 1e-4f);

            // pivot=(0,0.5)：rect.xMin=0，左端 0 → 0%、中点 85 → 50%、右端 170 → 100%
            Assert.AreEqual(0f, TouchSlider.NormalizeLocalX(0f, 0f, 170f), 1e-4f);
            Assert.AreEqual(0.5f, TouchSlider.NormalizeLocalX(85f, 0f, 170f), 1e-4f);
            Assert.AreEqual(1f, TouchSlider.NormalizeLocalX(170f, 0f, 170f), 1e-4f);

            // 超界与非法宽度
            Assert.AreEqual(0f, TouchSlider.NormalizeLocalX(-50f, 0f, 170f), 1e-4f);
            Assert.AreEqual(1f, TouchSlider.NormalizeLocalX(250f, 0f, 170f), 1e-4f);
            Assert.AreEqual(0f, TouchSlider.NormalizeLocalX(50f, 0f, 0f), 1e-4f);
        }

        [Test]
        public void MobileLayoutCustomizer_SelectionFrame_UsesBorderOnlyWithoutSolidShadow()
        {
            // 回归用例：透明填充 + HardShadow 会让实心墨色阴影盖住选中按键，表现为“选中就全黑”。
            var host = new GameObject("TestSelectionHost", typeof(RectTransform));
            var customizer = MobileLayoutCustomizer.Create(null, host.GetComponent<RectTransform>(), null);

            var frame = customizer.transform.Find("SelectionFrame");
            Assert.IsNotNull(frame, "编辑器必须包含 SelectionFrame");
            var shape = frame.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(shape, "SelectionFrame 必须挂 UIInkShapeGraphic");
            Assert.IsFalse(shape.HardShadow, "选中框只能画墨边，不能开实心硬阴影");
            Assert.AreEqual(0f, shape.color.a, 1e-4f, "选中框内部必须保持透明");

            Object.DestroyImmediate(host);
        }

        [Test]
        public void MobileActionButton_BaseScaleAndAlpha_SurvivePressAndSpecialState()
        {
            var host = new GameObject("TestButtonHost", typeof(RectTransform));

            var btn = MobileActionButton.Create(
                host.transform, "ScaleBtn", "测试",
                new Vector2(-100f, 100f), 100f, Color.white,
                null, null, null, MobileButtonMode.Hold);

            btn.SetBaseScale(1.35f);
            Assert.AreEqual(1.35f, btn.BaseScale, 1e-4f, "BaseScale 应记录自定义尺寸");
            Assert.AreEqual(1.35f, btn.RectTransform.localScale.x, 1e-4f);

            btn.OnPointerDown(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1.35f * 0.96f, btn.RectTransform.localScale.x, 1e-4f,
                "按下态应在自定义基准上乘 0.96，而不是回到 1.0 基准");
            btn.OnPointerUp(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1.35f, btn.RectTransform.localScale.x, 1e-4f,
                "抬起后必须回到自定义基准，而不是 Vector3.one");
            btn.ResetState();
            Assert.AreEqual(1.35f, btn.RectTransform.localScale.x, 1e-4f,
                "ResetState 不得抹掉自定义尺寸");

            var special = MobileActionButton.Create(
                host.transform, "SpecialBtn", "大招",
                new Vector2(-300f, 300f), 120f, new Color(1f, 0.8f, 0.2f, 1f),
                null, null, null, MobileButtonMode.Special);

            special.SetBaseAlpha(0.6f);
            special.UpdateSpecialState(false, 0.4f);
            Assert.AreEqual(0.6f * 0.55f, special.CanvasGroup.alpha, 1e-4f,
                "大招未就绪的 55% 应乘在自定义透明度上");
            special.UpdateSpecialState(true, 1f);
            Assert.AreEqual(0.6f, special.CanvasGroup.alpha, 1e-4f,
                "大招就绪后应恢复自定义透明度");

            Object.DestroyImmediate(host);
        }

        [Test]
        public void MobileLayoutPresets_AllPresets_ContainRequiredItemsAndNoOverlaps()
        {
            string[] required = { "MoveStick", "Fire", "Swim", "Jump", "Bomb", "Special", "LeftFire" };
            var presets = new[]
            {
                MobileLayoutPreset.TwoFinger,
                MobileLayoutPreset.ThreeFinger,
                MobileLayoutPreset.FourFinger
            };

            for (int p = 0; p < presets.Length; p++)
            {
                var preset = presets[p];
                var config = MobileLayoutPresets.Build(preset, 1f);

                Assert.AreEqual(p, config.preset, $"{preset} 的 preset 编号必须正确");
                Assert.AreEqual(MobileLayoutPresets.UsesLeftFire(preset), config.leftFireVisible,
                    $"{preset} 的左开火键显隐必须正确");

                // 摇杆/左开火锚定左下角，其余动作键锚定右下角；比较前统一换算到 1920 宽的屏幕坐标。
                Vector2 ScreenPos(string id, ButtonLayoutData data)
                {
                    float x = id == "MoveStick" || id == "LeftFire"
                        ? data.anchoredPosition.x
                        : 1920f + data.anchoredPosition.x;
                    return new Vector2(x, data.anchoredPosition.y);
                }

                for (int i = 0; i < required.Length; i++)
                {
                    Assert.IsNotNull(config.Find(required[i]), $"{preset} 缺少布局项 {required[i]}");
                }

                for (int i = 0; i < required.Length; i++)
                {
                    var a = config.Find(required[i]);
                    float ra = MobileLayoutPresets.GetBaseSize(required[i]) * a.scale * 0.5f;

                    for (int j = i + 1; j < required.Length; j++)
                    {
                        var b = config.Find(required[j]);
                        float rb = MobileLayoutPresets.GetBaseSize(required[j]) * b.scale * 0.5f;
                        float dist = Vector2.Distance(ScreenPos(required[i], a), ScreenPos(required[j], b));
                        Assert.GreaterOrEqual(dist, ra + rb - 1f,
                            $"{preset} 的 {required[i]} 与 {required[j]} 重叠 (距离 {dist:F1} < {ra + rb:F1})");
                    }
                }
            }
        }

        [Test]
        public void MobileLayoutCustomizer_IsEditingAny_TracksOpenAndClose()
        {
            var host = new GameObject("TestEditingFlagHost", typeof(RectTransform));
            var customizer = MobileLayoutCustomizer.Create(null, host.GetComponent<RectTransform>(), null);

            Assert.IsFalse(MobileLayoutCustomizer.IsEditingAny, "初始状态不应处于编辑中");
            customizer.Open();
            Assert.IsTrue(MobileLayoutCustomizer.IsEditingAny, "Open 后静态编辑标记必须为 true");
            customizer.Close(save: false);
            Assert.IsFalse(MobileLayoutCustomizer.IsEditingAny, "Close 后静态编辑标记必须为 false");
            Assert.IsNull(MobileLayoutCustomizer.ActiveEditor, "Close 后不得残留 ActiveEditor 引用");

            Object.DestroyImmediate(host);
        }
    }
}
