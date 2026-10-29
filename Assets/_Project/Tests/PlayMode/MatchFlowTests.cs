using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 比赛闭环回归测试：Ready → Countdown → Playing → Finished → Result → Restart。
    /// </summary>
    public class MatchFlowTests
    {
        const string SceneName = "PhaseA_Playbox";

        private static void SetRemaining(MatchManager match, float seconds)
        {
            var field = typeof(MatchManager).GetField("_remaining", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "MatchManager 应存在 _remaining 字段");
            field.SetValue(match, seconds);
        }

        private IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            Assert.IsNotNull(MatchManager.Instance, "场景应包含 MatchManager");
        }

        [UnityTest]
        public IEnumerator Match_StartsInReady_AndBlocksCombat()
        {
            yield return LoadScene();

            var match = MatchManager.Instance;
            Assert.AreEqual(MatchManager.Phase.Ready, match.CurrentPhase);
            Assert.IsFalse(MatchManager.CombatAllowed, "Ready 阶段不应允许战斗推进");
            Assert.AreEqual(120f, match.RemainingSeconds, 1f, "默认单局应为 120 秒");
        }

        [UnityTest]
        public IEnumerator Match_StartRunsCountdownThenPlaying()
        {
            yield return LoadScene();

            var match = MatchManager.Instance;
            match.StartMatch();

            Assert.AreEqual(MatchManager.Phase.Countdown, match.CurrentPhase);

            yield return new WaitForSeconds(3.6f);
            Assert.AreEqual(MatchManager.Phase.Playing, match.CurrentPhase, "倒计时结束后应进入 Playing");
            Assert.IsTrue(MatchManager.CombatAllowed);
        }

        [UnityTest]
        public IEnumerator Match_TimeUp_FinishesThenShowsResult()
        {
            yield return LoadScene();

            var match = MatchManager.Instance;
            match.StartMatch();
            yield return new WaitForSeconds(3.4f);

            InkSurfaceController.Instance.PaintCircle(new Vector3(0f, 0f, 4f), 4f, Team.TeamA);
            yield return null;

            SetRemaining(match, 0.05f);
            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(MatchManager.Phase.Finished, match.CurrentPhase, "时间到应先进入 FINISH 定格");
            Assert.IsFalse(MatchManager.CombatAllowed);

            yield return new WaitForSeconds(2.0f);
            Assert.AreEqual(MatchManager.Phase.Result, match.CurrentPhase, "定格结束后应进入结算");
            Assert.Greater(match.FinalCoverageA, 0f, "结算应记录橙队覆盖率");
        }

        [UnityTest]
        public IEnumerator Match_Restart_ResetsInkAndTimer()
        {
            yield return LoadScene();

            var match = MatchManager.Instance;
            match.StartMatch();
            yield return new WaitForSeconds(3.4f);

            InkSurfaceController.Instance.PaintCircle(new Vector3(0f, 0f, 4f), 5f, Team.TeamA);
            yield return null;
            Assert.Greater(InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), 0f);

            SetRemaining(match, 0.05f);
            yield return new WaitForSeconds(2.4f);
            Assert.AreEqual(MatchManager.Phase.Result, match.CurrentPhase);

            match.RestartMatch();
            yield return null;

            Assert.AreEqual(MatchManager.Phase.Countdown, match.CurrentPhase, "重开应回到倒计时");
            Assert.AreEqual(120f, match.RemainingSeconds, 1f, "重开应重置计时");
            Assert.AreEqual(0f, InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), 0.01f, "重开应清空墨水");
        }
    }
}
