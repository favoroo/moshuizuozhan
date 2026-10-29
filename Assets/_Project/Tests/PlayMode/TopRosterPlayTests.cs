using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// P4-B 顶部战况条与中央墨滴计时器的 PlayMode 运行时验证：
    /// 确保 4v4 头像槽位自动装配、中央墨滴计时球渲染、比分条弹簧平滑与 HudController 完整联动。
    /// </summary>
    public class TopRosterPlayTests
    {
        private static GameObject CreateCanvas()
        {
            GameObject root = new GameObject("P4B_TestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator TopRosterBarView_AutoBuildsEightSlots_AndBindsCorrectly()
        {
            GameObject canvasGo = CreateCanvas();

            var rosterGo = new GameObject("TopRosterBarView", typeof(RectTransform), typeof(TopRosterBarView));
            rosterGo.transform.SetParent(canvasGo.transform, false);

            var roster = rosterGo.GetComponent<TopRosterBarView>();
            yield return null;

            Assert.IsNotNull(roster.TeamASlots, "Team A 槽位数组应已初始化");
            Assert.AreEqual(4, roster.TeamASlots.Count, "Team A 应正好有 4 个头像槽位");
            Assert.IsNotNull(roster.TeamBSlots, "Team B 槽位数组应已初始化");
            Assert.AreEqual(4, roster.TeamBSlots.Count, "Team B 应正好有 4 个头像槽位");

            for (int i = 0; i < 4; i++)
            {
                Assert.IsNotNull(roster.TeamASlots[i], $"Team A 槽位 {i} 必须非空");
                Assert.AreEqual(Team.TeamA, roster.TeamASlots[i].SlotTeam);

                Assert.IsNotNull(roster.TeamBSlots[i], $"Team B 槽位 {i} 必须非空");
                Assert.AreEqual(Team.TeamB, roster.TeamBSlots[i].SlotTeam);
            }

            Object.Destroy(canvasGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CenterTimerView_AutoBuildsTimerAndScoreBar_AndAnimates()
        {
            GameObject canvasGo = CreateCanvas();

            var timerGo = new GameObject("CenterTimerView", typeof(RectTransform), typeof(CenterTimerView));
            timerGo.transform.SetParent(canvasGo.transform, false);

            var timer = timerGo.GetComponent<CenterTimerView>();
            yield return null;

            // 验证时间设置与分秒倒计时
            timer.SetTime(120f, true);
            Assert.AreEqual(120f, timer.DisplayedSeconds, 0.01f);

            // 验证比分设置与平滑推演
            timer.SetCoverage(42f, 38f);
            yield return null;

            float elapsed = 0f;
            while (elapsed < 0.4f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.Greater(timer.SmoothPercentA, 0f, "Team A 占领率弹簧应平滑推演上升");
            Assert.Greater(timer.SmoothPercentB, 0f, "Team B 占领率弹簧应平滑推演上升");

            Object.Destroy(canvasGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HudController_AutoAssemblesRosterAndTimer()
        {
            GameObject canvasGo = CreateCanvas();

            var hudGo = new GameObject("HUD", typeof(RectTransform), typeof(HudController));
            hudGo.transform.SetParent(canvasGo.transform, false);

            var hud = hudGo.GetComponent<HudController>();
            yield return null;

            Assert.IsNotNull(hud.TopRosterBar, "HudController Awake 应自动装配 TopRosterBar");
            Assert.IsNotNull(hud.CenterTimer, "HudController Awake 应自动装配 CenterTimer");

            Object.Destroy(canvasGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureFullRosterAndTimerPlayboxShot()
        {
            GameObject canvasGo = CreateCanvas();

            var hudGo = new GameObject("HUD", typeof(RectTransform), typeof(HudController));
            hudGo.transform.SetParent(canvasGo.transform, false);

            var hud = hudGo.GetComponent<HudController>();
            yield return null;

            // 驱动战况与比分数据
            if (hud.CenterTimer != null)
            {
                hud.CenterTimer.SetCoverage(52.4f, 41.8f);
                hud.CenterTimer.SetTime(85f, true);
            }

            // 让弹簧运动几帧展开
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            ScreenCapture.CaptureScreenshot("Logs/p4b_hud_roster_timer.png");
            yield return new WaitForSeconds(0.2f);

            Object.Destroy(canvasGo);
            yield return null;
        }
    }
}
