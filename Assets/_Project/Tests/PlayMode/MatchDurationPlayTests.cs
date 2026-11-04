using System.Collections;
using NUnit.Framework;
using P1.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// 首页比赛时长段选（PlayMode）：
    /// 1. Hero 卡第二行真的建出「时长」角签 + 3 段药丸（2分/3分/5分）；
    /// 2. 点击 5 分档写入 GameSettingsService，并持久化到 PlayerPrefs；
    /// 3. 下一局 MatchManager 的倒计时真的变成 300 秒（不是只改 UI 文案）。
    /// </summary>
    [Category("UI")]
    public class MatchDurationPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetDefaultDuration()
        {
            GameSettingsService.MatchDurationMinutes = GameSettingsMath.DefaultMatchDurationMinutes;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreDefaultDuration()
        {
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ReturnToMenu();
            }
            GameSettingsService.MatchDurationMinutes = GameSettingsMath.DefaultMatchDurationMinutes;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeDurationSegments_SelectFiveMinutes_AppliesToNextMatch()
        {
            SceneManager.LoadScene("PhaseA_Playbox");
            yield return null;
            yield return null;

            Transform row = GameObject.Find("HeroDurationRow")?.transform;
            Assert.IsNotNull(row, "首页 Hero 卡应构建时长段选容器");
            Assert.IsNotNull(row.Find("HeroDurationTag"), "时长段选必须有「时长」角签");

            Button[] buttons = row.GetComponentsInChildren<Button>(true);
            Assert.AreEqual(3, buttons.Length, "时长段选应有 2 分 / 3 分 / 5 分三档");

            // 点击最长档：服务值立即变化，段选高亮由事件刷新。
            buttons[2].onClick.Invoke();
            yield return null;
            Assert.AreEqual(GameSettingsMath.MatchDurationLongMinutes, GameSettingsService.MatchDurationMinutes);
            Assert.AreEqual(PlayerPrefs.GetInt(GameSettingsMath.KeyMatchDurationMinutes),
                GameSettingsMath.MatchDurationLongMinutes, "时长选择必须持久化到 PlayerPrefs");

            // 真正开局：MatchManager 的剩余时间应变成 5 分钟，而不是继续读 GameConfig 的 120s。
            MatchManager match = MatchManager.Instance;
            Assert.IsNotNull(match, "主场景应存在 MatchManager");
            match.StartMatch();
            yield return null;
            Assert.AreEqual(GameSettingsMath.GetMatchDurationSeconds(GameSettingsMath.MatchDurationLongMinutes),
                match.RemainingSeconds, 1.0f, "开局剩余时间应为所选 5 分钟");
        }
    }
}