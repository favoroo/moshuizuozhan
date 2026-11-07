using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// Bot 局内差异化（PlayMode）：在主场景 PhaseA_Playbox 的真实 4v4 局内验证
    /// INKWAVE 式抽卡不变量——同队武器互不重复、队友不拿玩家的枪、每局重抽、
    /// 昵称与个性克隆生效、共享 BotConfig.asset 永不被污染、小合成场景不被自动路径越界改动。
    /// </summary>
    [Category("AI")]
    public class BotLoadoutPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private readonly List<Object> _cleanup = new List<Object>();
        private MatchManager _loadedMatch;

        [UnityTearDown]
        public IEnumerator RestoreDefaults()
        {
            BotLoadoutService.AutoDraftEnabled = true;
            _loadedMatch = null;

            for (int i = 0; i < _cleanup.Count; i++)
            {
                if (_cleanup[i] != null) Object.Destroy(_cleanup[i]);
            }

            _cleanup.Clear();
            yield return null;
        }

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            _loadedMatch = MatchManager.Instance;
            Assert.IsNotNull(_loadedMatch, "主场景应存在 MatchManager");
        }

        [UnityTest]
        public IEnumerator MatchStart_DraftsDistinctWeaponsWithinTeams_ExcludingPlayerKindOnAllySide()
        {
            yield return LoadPlaybox();

            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            Assert.AreEqual(7, bots.Length, "主场景应有 7 个 Bot");

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "主场景应存在本地玩家");
            WeaponKind playerKind = player.Weapon.Config.weaponKind;

            BotLoadoutService service = BotLoadoutService.Instance;
            Assert.IsNotNull(service, "PlayMode 应自动挂起 BotLoadoutService");

            // 污染哨兵：未抽卡前普通难度的 Bot 直指共享 BotConfig.asset，记下原值。
            BotConfigSO sharedAsset = bots[0].DebugBaseConfig;
            Assert.IsNotNull(sharedAsset, "Bot 应保留了场景注入的原始 BotConfig");
            float reactionBefore = sharedAsset.reactionTime;
            float wanderBefore = sharedAsset.aimWanderDegrees;

            service.AssignMatchRoster(20261107);

            var allyKinds = new List<WeaponKind> { playerKind };
            var enemyKinds = new List<WeaponKind>();
            var names = new List<string>();

            foreach (BotController bot in bots)
            {
                var health = bot.GetComponent<CombatantHealth>();
                Assert.IsNotNull(health);

                var weapon = bot.GetComponent<WeaponController>();
                Assert.AreEqual(weapon.Config.weaponKind, weapon.CurrentWeaponKind,
                    bot.name + " 装备的 WeaponConfigSO 与策略应一致");

                if (health.Team == player.Health.Team) allyKinds.Add(weapon.Config.weaponKind);
                else enemyKinds.Add(weapon.Config.weaponKind);

                Assert.IsFalse(string.IsNullOrEmpty(bot.DisplayName),
                    bot.name + " 应在本局拿到昵称（击杀播报/VS 卡依赖）");
                names.Add(bot.DisplayName);

                Assert.AreNotSame(sharedAsset, bot.ActiveConfig,
                    bot.name + " 个性倍率必须落在私有克隆上，不能乘在共享资产上");
                Assert.IsFalse(bot.DebugPersonality.IsIdentity(),
                    bot.name + " 本局应拿到非恒等个性");
            }

            Assert.AreEqual(4, allyKinds.Count, "己方队（含玩家）应有 4 种武器");
            Assert.AreEqual(4, new HashSet<WeaponKind>(allyKinds).Count,
                "己方队武器类型必须互不重复");
            Assert.AreEqual(4, enemyKinds.Count, "敌方队应有 4 个 Bot");
            Assert.AreEqual(4, new HashSet<WeaponKind>(enemyKinds).Count,
                "敌方队武器类型必须互不重复");
            Assert.AreEqual(7, new HashSet<string>(names).Count, "同一局 7 个 Bot 昵称互不重复");

            Assert.AreEqual(reactionBefore, sharedAsset.reactionTime, 1e-4f,
                "共享 BotConfig.asset 的反应时间绝不能被个性层污染");
            Assert.AreEqual(wanderBefore, sharedAsset.aimWanderDegrees, 1e-4f,
                "共享 BotConfig.asset 的瞄准漂移绝不能被个性层污染");

            // 阵容快照：8 条（含玩家「你」），供 VS 卡与播报使用。
            var roster = BotLoadoutService.CurrentRoster;
            Assert.AreEqual(8, roster.Count, "CurrentRoster 应包含玩家与 7 名 Bot");
            Assert.IsTrue(roster[0].IsLocalPlayer, "首条应是本地玩家");
            Assert.AreEqual("你", roster[0].DisplayName);
            for (int i = 1; i < roster.Count; i++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(roster[i].DisplayName));
                Assert.IsFalse(string.IsNullOrEmpty(roster[i].WeaponDisplayName),
                    "VS 卡需要武器显示名（第 " + i + " 条）");
            }
        }

        [UnityTest]
        public IEnumerator SecondMatch_ReseedsTheWholeRoster()
        {
            yield return LoadPlaybox();

            BotLoadoutService service = BotLoadoutService.Instance;
            Assert.IsNotNull(service);

            service.AssignMatchRoster(11);
            string first = string.Join(",", CollectNames());

            service.AssignMatchRoster(4242);
            string second = string.Join(",", CollectNames());

            Assert.AreNotEqual(first, second, "换种子重抽后昵称阵容必须重洗（每局不一样）");
        }

        [UnityTest]
        public IEnumerator Countdown_AutoPath_DraftsWithoutManualCall()
        {
            yield return LoadPlaybox();

            BotLoadoutService service = BotLoadoutService.Instance;
            Assert.IsNotNull(service);
            service.ForceNextSeed(777);

            _loadedMatch.StartMatch(); // 进入 Countdown → 自动抽卡排在后一帧（等玩家选枪先落地）
            yield return null;
            yield return null;
            yield return null;

            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            foreach (BotController bot in bots)
                Assert.IsFalse(string.IsNullOrEmpty(bot.DisplayName),
                    "自动路径（Countdown 触发）应给 " + bot.name + " 发昵称");

            Assert.AreEqual(8, BotLoadoutService.CurrentRoster.Count, "自动路径应填充 CurrentRoster");
        }

        [UnityTest]
        public IEnumerator SyntheticSmallCast_IsNotTouchedByAutoDraft()
        {
            // 同一 Play 会话里前一个用例可能把主场景留在内存里（CreateScene 是叠加加载）。
            // 这里显式清掉比赛管理器与全部 Bot，构造一个「无抽卡触发点」的合成场。
            // 陷阱：FindObjectsByType 默认不含未激活对象——上局阵亡待复活的 Bot 会在此刻隐身、
            // 随后被复活计时器重新激活并注册，计数会凭空多出来；必须 Include 未激活对象一起清。
            foreach (MatchManager leftover in Object.FindObjectsByType<MatchManager>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.Destroy(leftover.gameObject);
            foreach (BotController leftoverBot in Object.FindObjectsByType<BotController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.Destroy(leftoverBot.gameObject);
            yield return null;
            yield return null;

            SceneManager.CreateScene("BotLoadoutSyntheticTestScene");
            yield return null;
            yield return null;

            Assert.IsNull(Object.FindAnyObjectByType<MatchManager>(),
                "合成场景应无 MatchManager（没有自动抽卡触发点）");
            Assert.AreEqual(0, BotLoadoutService.CountBotsInField(),
                "合成场起步应无残留 Bot（现场：" + DescribeFieldBots() + "）");
            Assert.GreaterOrEqual(BotLoadoutService.MinBotsForAutoDraft, 3,
                "门槛本身应放行真实对局、拦住小合成场");

            var bots = new List<BotController>();
            for (int i = 0; i < 2; i++)
            {
                var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
                cfg.weaponKind = WeaponKind.Shooter;
                cfg.displayName = "Test Spritzer";
                _cleanup.Add(cfg);

                // 陷阱：InkCharacterMotor 带 [RequireComponent(CombatantHealth)]，一次性把
                // CombatantHealth 也写进构造函数列表会让 Unity 先自动补一个、再挂上显式那个，
                // 同一物体出现两个 health（各自注册一次），Bot 计数凭空翻倍。
                // 这里只传前置组件，CombatantHealth 让 RequireComponent 自己补，再取唯一实例。
                var go = new GameObject("SyntheticBot_" + i,
                    typeof(CharacterController), typeof(InkCharacterMotor), typeof(WeaponController));
                _cleanup.Add(go);

                var health = go.GetComponent<CombatantHealth>();
                Assert.IsNotNull(health, "RequireComponent 应自动补上 CombatantHealth");
                Assert.AreEqual(1, go.GetComponents<CombatantHealth>().Length,
                    "同一合成 Bot 上只允许存在一个 CombatantHealth");
                health.Configure(i == 0 ? Team.TeamA : Team.TeamB, null);
                go.GetComponent<WeaponController>().EquipWeapon(cfg);

                var botConfig = ScriptableObject.CreateInstance<BotConfigSO>();
                botConfig.hideFlags = HideFlags.None;
                _cleanup.Add(botConfig);

                var bot = go.AddComponent<BotController>();
                bot.Configure(botConfig);
                bots.Add(bot);
            }

            yield return null;
            yield return null;

            string fieldDump = DescribeFieldBots();
            Debug.Log("[BotLoadoutPlayTests] 合成场现场: " + fieldDump);

            Assert.AreEqual(2, BotLoadoutService.CountBotsInField(),
                "门槛判定读到的合成场 Bot 数应为 2（现场：" + fieldDump + "）");
            Assert.Less(BotLoadoutService.CountBotsInField(), BotLoadoutService.MinBotsForAutoDraft);

            foreach (BotController bot in bots)
            {
                Assert.IsTrue(string.IsNullOrEmpty(bot.DisplayName),
                    "合成小场景的 Bot 不应被自动抽卡贴昵称");
                Assert.AreEqual(WeaponKind.Shooter,
                    bot.GetComponent<WeaponController>().Config.weaponKind);
                Assert.AreSame(bot.DebugBaseConfig, bot.ActiveConfig,
                    "未抽卡的 Bot 必须仍直指原配置（零克隆零污染）");
            }
        }

        private static List<string> CollectNames()
        {
            var names = new List<string>();
            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            foreach (BotController bot in bots) names.Add(bot.DisplayName);
            names.Sort();
            return names;
        }

        /// <summary>诊断：列出注册表里所有带 BotController 的单位（场景/激活/昵称/实例 ID）。</summary>
        private static string DescribeFieldBots()
        {
            var sb = new System.Text.StringBuilder(256);
            IReadOnlyList<CombatantHealth> all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth health = all[i];
                BotController bot = health != null ? health.GetComponent<BotController>() : null;
                if (bot == null) continue;

                sb.Append(bot.name)
                  .Append("[rid=").Append(health.GetEntityId().ToString())
                  .Append(" go=").Append(bot.gameObject.GetEntityId().ToString())
                  .Append(" scene=").Append(bot.gameObject.scene.name)
                  .Append(" active=").Append(bot.gameObject.activeInHierarchy)
                  .Append(" team=").Append(health.Team)
                  .Append(" name=").Append(bot.DisplayName ?? "-")
                  .Append("] ");
            }

            int liveBots = Object.FindObjectsByType<BotController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            return (sb.Length > 0 ? sb.ToString() : "(empty)") + " liveBotControllers=" + liveBots;
        }
    }
}
