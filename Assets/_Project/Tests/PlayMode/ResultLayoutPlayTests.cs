using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using P1.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 结算页纵向排版回归：「本局成就」节标曾被压进战报卡的下边框——成就条按卡行高度挂载，
    /// 没算节标自己占的题头带（胶囊高 + 与卡行的缝），于是节标一半骑在卡上、一半贴着卡行。
    /// 这里用真实世界矩形把「卡下缘 ≥ 节标 ≥ 卡行 ≥ 按钮」三条缝钉死。
    /// </summary>
    [Category("UI")]
    public class ResultLayoutPlayTests
    {
        const string SceneName = "PhaseA_Playbox";

        private static void SetRemaining(MatchManager match, float seconds)
        {
            var field = typeof(MatchManager).GetField("_remaining", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "MatchManager 应存在 _remaining 字段");
            field.SetValue(match, seconds);
        }

        private static Transform FindByName(string objectName)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == objectName) return all[i];
            }
            return null;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static float Top(Transform t)
        {
            ((RectTransform)t).GetWorldCorners(Corners);
            return Mathf.Max(Corners[0].y, Mathf.Max(Corners[1].y, Mathf.Max(Corners[2].y, Corners[3].y)));
        }

        private static float Bottom(Transform t)
        {
            ((RectTransform)t).GetWorldCorners(Corners);
            return Mathf.Min(Corners[0].y, Mathf.Min(Corners[1].y, Mathf.Min(Corners[2].y, Corners[3].y)));
        }

        [UnityTest]
        public IEnumerator Result_AchievementTag_SitsClearOfReportCardAndCards()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            Assert.IsNotNull(MatchManager.Instance, "场景应包含 MatchManager");
            PlayModeTestKit.ShortenMatchTimers();

            var match = MatchManager.Instance;
            match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 6f);

            InkSurfaceController.Instance.PaintCircle(new Vector3(0f, 0f, 4f), 4f, Team.TeamA);
            yield return null;

            SetRemaining(match, 0.05f);
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Result, 8f);
            // 入场 pop-in 期间整卡缩到 0.9，量之前先等它落位（0.22s + 错峰）。
            yield return new WaitForSeconds(1.5f);

            // 本局无新解锁时整条隐藏；显式点亮才量得到节标与卡行。
            var view = Object.FindAnyObjectByType<ResultAchievementsView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view, "结算页应装配 ResultAchievementsView");
            Assert.IsTrue(view.Populate(new List<string> { "ach_kill_3", "ach_revenge" }), "成就条应可点亮");
            yield return null;

            Transform report = FindByName("ReportPanel");
            Transform tag = FindByName("SectionTag");
            Transform card0 = FindByName("Card_0");
            Transform restart = FindByName("RestartButton");
            Assert.IsNotNull(report, "应有战报卡 ReportPanel");
            Assert.IsNotNull(tag, "应有节标 SectionTag");
            Assert.IsNotNull(card0, "应有成就卡 Card_0");
            Assert.IsNotNull(restart, "应有主 CTA RestartButton");

            AssertVerticalOrder(Bottom(report), Top(tag), "战报卡", "节标");
            AssertVerticalOrder(Bottom(tag), Top(card0), "节标", "成就卡");
            AssertVerticalOrder(Bottom(card0), Top(restart), "成就卡", "CTA 按钮");
        }

        /// <summary>上面的元素底边必须不低于下面元素的顶边（两者之间不许有叠压）。</summary>
        private static void AssertVerticalOrder(float topElementBottom, float bottomElementTop,
            string topName, string bottomName)
        {
            Assert.GreaterOrEqual(topElementBottom, bottomElementTop,
                topName + " 与" + bottomName + "重叠（" + topName + "底=" + topElementBottom.ToString("0.0") +
                "，" + bottomName + "顶=" + bottomElementTop.ToString("0.0") + "）");
        }
    }
}
