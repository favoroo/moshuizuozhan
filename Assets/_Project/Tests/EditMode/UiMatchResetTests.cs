using System.IO;
using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 「每局一次 / 离场复位」类 UI 状态用例：
    /// - <see cref="CenterTimerView"/>：第二局必须能再次弹出「最后 1 分钟」横幅（旧代码 _bannerShown 永不复位）；
    /// - <see cref="InkTankView"/>：宿主 HUD 画布不可见时不再告警，复位后不再报低墨；
    /// - <see cref="MobileLayoutEditSession"/>：布局编辑器「点 X 取消」必须回到打开时的快照。
    /// </summary>
    [TestFixture]
    public class UiMatchResetTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("UiMatchResetTestRoot", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        private static GameObject NewChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            return go;
        }

        [Test]
        public void CenterTimer_FinalMinuteBanner_TriggersOncePerMatch_AndAgainAfterReset()
        {
            var go = NewChild("CenterTimer");
            go.transform.SetParent(_root.transform, false);
            var view = go.AddComponent<CenterTimerView>();

            view.SetTime(61f);
            Assert.IsFalse(view.BannerShown, "还没跨过 60s 不应弹横幅");

            view.SetTime(59.5f);
            Assert.IsTrue(view.BannerShown, "跨过 60s 必须弹「最后 1 分钟」横幅");

            // 同一局内不再重复
            view.SetTime(30f);
            Assert.IsTrue(view.BannerShown);

            // 新一局：HudController 在 Countdown 阶段调用
            view.ResetForMatch();
            Assert.IsFalse(view.BannerShown, "ResetForMatch 必须清掉本局已弹标记");

            view.SetTime(120f);
            view.SetTime(59.9f);
            Assert.IsTrue(view.BannerShown, "第二局必须能再次弹出「最后 1 分钟」横幅");
        }

        [Test]
        public void MatchFlowUI_DoesNotTriggerMatchBannerOneMinute()
        {
            // 1:00 决战横幅只归 CenterTimerView 独家负责。海鸥港湾 / 霓虹滑板场两张地图的
            // MatchBannerView 是激活状态（PhaseA_Playbox 才是停用），MatchFlowUI 若再调
            // ShowOneMinute 就会和计时球横幅同时弹出两条「最后 1 分钟」。
            const string path = "Assets/_Project/Scripts/UI/MatchFlowUI.cs";
            Assert.IsTrue(File.Exists(path), $"找不到比赛流程 UI 源码：{path}");

            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("ShowOneMinute()"),
                "MatchFlowUI 不能再触发 MatchBannerView.ShowOneMinute：1:00 横幅只归 CenterTimerView，否则两条提示会同时弹出");
        }

        [Test]
        public void InkTank_ResetAlertState_ClearsLowInkFlag()
        {
            var go = NewChild("InkTank");
            go.transform.SetParent(_root.transform, false);
            var tank = go.AddComponent<InkTankView>();

            tank.SetInk(1f, 100f);
            Assert.IsFalse(tank.IsLowInk);

            tank.ResetAlertState();
            Assert.IsFalse(tank.IsLowInk, "复位后不得处于低墨告警态");
            Assert.AreEqual(0, tank.WarningPlayCount, "复位本身不应触发告警音");
        }

        [Test]
        public void InkTank_HostCanvasHidden_ReportsInvisible()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();

            var go = NewChild("InkTank");
            go.transform.SetParent(canvasGo.transform, false);
            var tank = go.AddComponent<InkTankView>();

            Assert.IsTrue(tank.IsHostCanvasVisible(), "HUD 画布可见时视图可见");

            // HudController 隐藏 HUD 只关画布，不停 GameObject：
            // 视图必须自查根画布，否则 Update 会继续按离场前的低墨值蜂鸣。
            canvas.enabled = false;
            Assert.IsFalse(tank.IsHostCanvasVisible());

            canvas.enabled = true;
            Assert.IsTrue(tank.IsHostCanvasVisible());
        }

        [Test]
        public void InkTank_AttachedToCanvasRoot_StillDetectsHiddenState()
        {
            // 组件恰好挂在画布根节点上：EnsureSubCanvas 会认领这层 Canvas 作为「自己的画布」，
            // 解析必须沿父级链找上级画布，否则会解析到自己并把宿主永远判成不可见。
            var canvasGo = new GameObject("HudCanvasRoot", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();

            var tank = canvasGo.AddComponent<InkTankView>();
            Assert.IsTrue(tank.IsHostCanvasVisible());

            canvas.enabled = false;
            Assert.IsFalse(tank.IsHostCanvasVisible(), "挂在画布根上时同样要能识别宿主不可见");
        }

        [Test]
        public void LayoutEditSession_Cancel_RestoresEntrySnapshot()
        {
            var entry = new MobileLayoutConfig { preset = 0, leftFireVisible = false };
            entry.Set("Fire", new Vector2(-150f, 150f), 1f, 1f);

            var session = new MobileLayoutEditSession();
            session.Begin(entry, entryHadSavedConfig: true);

            // 用户在编辑器里切到 4 指方案（ApplyPreset 会即时写盘，但会话快照必须是打开时的 2 指）
            var presetConfig = new MobileLayoutConfig { preset = 2, leftFireVisible = true };
            presetConfig.Set("Fire", new Vector2(-260f, 164f), 1f, 1f);

            var cancel = session.ResolveCancelSnapshot();
            Assert.IsNotNull(cancel, "打开时已有存档：取消必须返回快照");
            Assert.AreEqual(0, cancel.preset, "取消要回到打开时的 2 指方案，而不是刚应用的 4 指");
            Assert.AreEqual(-150f, cancel.Find("Fire").anchoredPosition.x, 0.01f);
            Assert.IsFalse(cancel.leftFireVisible);

            // 返回的是快照副本：外部改动不得污染会话，否则第二次取消会拿到被改过的值
            cancel.preset = 1;
            var cancelAgain = session.ResolveCancelSnapshot();
            Assert.AreEqual(0, cancelAgain.preset);
        }

        [Test]
        public void LayoutEditSession_Cancel_WithoutSavedConfig_ReturnsNull()
        {
            var current = new MobileLayoutConfig { preset = 0 };
            current.Set("Fire", new Vector2(-150f, 150f), 1f, 1f);

            var session = new MobileLayoutEditSession();
            session.Begin(current, entryHadSavedConfig: false);

            Assert.IsNull(session.ResolveCancelSnapshot(),
                "打开时没有存档 → 取消走 ResetToDefault（删档 + 回设备默认布局）");
        }

        [Test]
        public void LayoutEditSession_End_ClearsSnapshotForNextOpen()
        {
            var entry = new MobileLayoutConfig { preset = 1 };
            entry.Set("Fire", new Vector2(-150f, 150f), 1f, 1f);

            var session = new MobileLayoutEditSession();
            session.Begin(entry, entryHadSavedConfig: true);
            session.End();

            Assert.IsFalse(session.IsOpen);
            Assert.IsNull(session.ResolveCancelSnapshot(), "会话结束后不得残留上一次的快照");
        }
    }
}
