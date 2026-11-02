using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// Bot 难度运行时接线（PlayMode）：
    /// 1. 首页 Hero 卡真的建出 4 段难度按钮；
    /// 2. 点击段选改 GameSettingsService，已在场景里的 Bot 立刻换成缩放后的配置；
    /// 3. 切回「普通」必须复用原始 BotConfig 资产（不是残留的克隆）。
    /// </summary>
    [Category("AI")]
    public class BotDifficultyPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetDefaultDifficulty()
        {
            GameSettingsService.BotDifficulty = GameSettingsMath.DefaultBotDifficulty;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreDefaultDifficulty()
        {
            GameSettingsService.BotDifficulty = GameSettingsMath.DefaultBotDifficulty;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HomeDifficultySegments_RescaleExistingBots_Live()
        {
            SceneManager.LoadScene("PhaseA_Playbox");
            yield return null;
            yield return null;

            var row = GameObject.Find("HeroDifficultyRow");
            Assert.IsNotNull(row, "首页 Hero 卡应构建难度段选容器");

            var buttons = row.GetComponentsInChildren<Button>(true);
            Assert.AreEqual(4, buttons.Length, "难度段选应有新手 / 轻松 / 普通 / 硬核四档");

            var bot = Object.FindFirstObjectByType<BotController>();
            Assert.IsNotNull(bot, "主场景应存在 Bot");

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "主场景应存在本地玩家");
            Assert.AreEqual(100f, player.Health.MaxHealth, 0.5f, "普通难度本地玩家应为基准 100 血");

            BotConfigSO normalConfig = bot.ActiveConfig;
            Assert.IsNotNull(normalConfig, "普通难度应有生效配置");
            Assert.AreEqual(1f, normalConfig.sightRangeScale, 1e-4f,
                "旧 BotConfig.asset 缺 sightRangeScale 字段时应按默认 1（全额索敌）");
            float normalReaction = normalConfig.reactionTime;
            float normalOmega = normalConfig.aimSpringOmega;

            // 点最低档「新手」：设置落到 PlayerPrefs 服务，Bot 通过事件即时换配置。
            buttons[GameSettingsMath.MinBotDifficulty].onClick.Invoke();
            yield return null;

            Assert.AreEqual(BotDifficultyMath.LevelBeginner, GameSettingsService.BotDifficulty);
            Assert.AreNotSame(normalConfig, bot.ActiveConfig, "新手档应使用运行时缩放副本");
            Assert.Greater(bot.ActiveConfig.reactionTime, normalReaction, "新手档 Bot 反应必须更慢");
            Assert.Less(bot.ActiveConfig.aimSpringOmega, normalOmega, "新手档 Bot 跟枪必须更软");
            Assert.AreEqual(150f, player.Health.MaxHealth, 0.5f, "新手档本地玩家最大生命应提升到 150");

            // 切回「普通」：必须回到原始资产，行为与旧版完全一致。
            buttons[BotDifficultyMath.LevelNormal].onClick.Invoke();
            yield return null;

            Assert.AreEqual(BotDifficultyMath.LevelNormal, GameSettingsService.BotDifficulty);
            Assert.AreSame(normalConfig, bot.ActiveConfig, "普通档必须复用原始 BotConfig 资产");
            Assert.AreEqual(normalReaction, bot.ActiveConfig.reactionTime, 1e-4f);
            Assert.AreEqual(100f, player.Health.MaxHealth, 0.5f, "切回普通应恢复基准 100 血");
        }
    }
}
