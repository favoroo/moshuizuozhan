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
            // 验证 5 个主操作按键的扇形人体工学排布均在右下角，且彼此之间保持足够物理间距
            var positions = new (string name, Vector2 pos, float size)[]
            {
                ("Fire", new Vector2(-160f, 160f), 148f),
                ("Swim", new Vector2(-310f, 110f), 118f),
                ("Jump", new Vector2(-110f, 310f), 110f),
                ("Bomb", new Vector2(-245f, 260f), 112f),
                ("Special", new Vector2(-370f, 340f), 124f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                // 所有按键均在屏幕右下角区域（X 为负相对右边距，Y 为正相对底边距）
                Assert.Less(positions[i].pos.x, 0f, $"{positions[i].name} 必须以右下角锚定 (负 X)");
                Assert.Greater(positions[i].pos.y, 0f, $"{positions[i].name} 必须在下半屏 (正 Y)");
                Assert.Less(positions[i].pos.y, 500f, $"{positions[i].name} 绝不能飞到上半屏遮挡战况条与小地图");

                for (int j = i + 1; j < positions.Length; j++)
                {
                    float dist = Vector2.Distance(positions[i].pos, positions[j].pos);
                    float minAllowedDist = (positions[i].size + positions[j].size) * 0.5f - 1f; // 允许微接触，不允许严重重叠
                    Assert.GreaterOrEqual(dist, minAllowedDist,
                        $"{positions[i].name} 与 {positions[j].name} 发生重叠 (距离 {dist} < 最小允许 {minAllowedDist})");
                }
            }
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
            var config = new MobileLayoutConfig();
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

            MobileLayoutStorage.Delete();
            Assert.IsFalse(MobileLayoutStorage.HasCustomLayout, "清理后 HasCustomLayout 应为 false");
        }

        [Test]
        public void MobileLayoutConfig_ClampsScaleAndAlpha_ToSafeLimits()
        {
            var config = new MobileLayoutConfig();
            // 尝试设置极端非法数值
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

            // 启用强制显示开关供测试
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
    }
}
