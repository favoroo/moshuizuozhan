using System.Collections;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 局内差异化调度服务（INKWAVE src/game/match.js setup() 的对位实现）：
    /// 每次进入 Countdown（开局 / 重开一局）统一执行一次「抽卡」——
    /// 1. 武器：同队 4 人类型互不重复，队友池排除玩家当前武器类型，敌方从 7 种里抽 4 种；
    /// 2. 个性：每个 Bot 一套风格倍率（<see cref="BotPersonality"/>），叠在难度之上；
    /// 3. 昵称：每局从 <see cref="BotLoadoutMath.BotNames"/> 洗牌发放，击杀播报与 VS 阵容卡共用。
    ///
    /// 复活不换枪（与 INKWAVE 一致）：只在 Countdown 抽一次；下一局重抽。
    /// 装配模式仿 <see cref="P1.UI.WeaponSelect.WeaponSelectBootstrap"/>：
    /// RuntimeInitializeOnLoad 自建 DontDestroyOnLoad 主机，MatchManager 晚就绪时按帧重试接管。
    /// 为减少对既有合成测试场景的侵入：自动化抽卡要求场上至少有 <see cref="MinBotsForAutoDraft"/>
    /// 个带 <see cref="BotController"/> 的单位（真实 4v4 试玩场景恒满足）；
    /// 单测可直接调用 <see cref="AssignMatchRoster(int)"/> 绕过门槛，或用
    /// <see cref="AutoDraftEnabled"/> 整体关闭。
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class BotLoadoutService : MonoBehaviour
    {
        public static BotLoadoutService Instance { get; private set; }

        /// <summary>自动化抽卡总开关（测试 / 工具可关；关掉的只是后续自动抽卡，已分配的本局武装保留）。</summary>
        public static bool AutoDraftEnabled = true;

        /// <summary>自动抽卡的最小在场 Bot 数：低于它视为合成测试沙盒，不动别人的 Bot。</summary>
        public const int MinBotsForAutoDraft = 4;

        private const string PrefsKeySelectedWeaponIndex = "P1.Loadout.SelectedWeaponIndex";

        /// <summary>本局阵容快照条目：VS 阵容卡与结算/播报按它展示「昵称 + 武器」。</summary>
        public struct RosterEntry
        {
            public Team Team;
            public bool IsLocalPlayer;
            public string DisplayName;
            public WeaponKind WeaponKind;
            public string WeaponDisplayName;
        }

        private static readonly List<RosterEntry> _roster = new List<RosterEntry>(8);

        /// <summary>最近一次抽卡后的全场阵容（含本地玩家；未抽过时为空）。</summary>
        public static IReadOnlyList<RosterEntry> CurrentRoster => _roster;

        private readonly List<BotController> _allyBots = new List<BotController>(4);
        private readonly List<BotController> _enemyBots = new List<BotController>(4);

        private MatchManager _currentMatch;
        private bool _subscribedMatch;
        private int _matchCounter;
        private int _forcedSeed = int.MinValue;
        private Coroutine _pendingDraft;
        private float _setupRetryTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAtRuntime()
        {
            if (!Application.isPlaying) return;
            CheckAndCreate();
        }

        private static void CheckAndCreate()
        {
            if (Instance != null) return;

            // MatchManager 可能与本组件同帧就绪，顺序不保证；先建主机，Update 里再重试接管。
            var hostGo = new GameObject("[BotLoadoutService]");
            DontDestroyOnLoad(hostGo);
            hostGo.AddComponent<BotLoadoutService>();
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            SetupSystem();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            UnsubscribeMatch();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            SetupSystem();
        }

        private void Update()
        {
            if (_currentMatch != null) return;

            _setupRetryTimer += Time.unscaledDeltaTime;
            if (_setupRetryTimer < 0.5f) return;
            _setupRetryTimer = 0f;
            SetupSystem();
        }

        private void SetupSystem()
        {
            MatchManager match = MatchManager.Instance != null
                ? MatchManager.Instance
                : Object.FindAnyObjectByType<MatchManager>();
            if (match == null) return;

            SubscribeMatch(match);
        }

        private void SubscribeMatch(MatchManager match)
        {
            if (_subscribedMatch && _currentMatch == match) return;

            UnsubscribeMatch();
            _currentMatch = match;
            match.PhaseChanged += HandlePhaseChanged;
            _subscribedMatch = true;
        }

        private void UnsubscribeMatch()
        {
            if (!_subscribedMatch) return;
            if (_currentMatch != null)
                _currentMatch.PhaseChanged -= HandlePhaseChanged;
            _currentMatch = null;
            _subscribedMatch = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase != MatchManager.Phase.Countdown) return;

            // 延后一帧：让 WeaponSelectBootstrap 在同批 PhaseChanged 派发里先把玩家选中的枪装上，
            // 本服务再把「玩家实际武器类型」作为己方排除项抽卡，避免时序竞态。
            if (_pendingDraft != null) StopCoroutine(_pendingDraft);
            _pendingDraft = StartCoroutine(DraftNextFrame());
        }

        private IEnumerator DraftNextFrame()
        {
            yield return null;
            _pendingDraft = null;

            if (!AutoDraftEnabled) yield break;

            // 门槛：场上带 BotController 的单位少于 MinBotsForAutoDraft 视为合成测试沙盒，
            // 不动别人手工装配的 Bot（真实 4v4 试玩场景恒有 7 个）。
            if (CountBotsInField() < MinBotsForAutoDraft) yield break;

            AssignMatchRoster(NextSeed());
        }

        /// <summary>场上带 <see cref="BotController"/> 的单位数（自动抽卡门槛判定，测试可直读）。</summary>
        public static int CountBotsInField()
        {
            int count = 0;
            IReadOnlyList<CombatantHealth> all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth health = all[i];
                if (health != null && health.GetComponent<BotController>() != null)
                    count++;
            }

            return count;
        }

        /// <summary>用当前时间种子执行一次完整抽卡（自动路径与外部工具共用）。</summary>
        public void AssignMatchRoster()
        {
            AssignMatchRoster(NextSeed());
        }

        /// <summary>
        /// 用指定种子执行一次完整抽卡（武器 + 个性 + 昵称 + 阵容快照）。
        /// 同种子同结果；在场 Bot 少于 <see cref="MinBotsForAutoDraft"/> 时仅在显式调用下工作
        /// （自动路径在 DraftNextFrame 里检查门槛，本方法不检查，方便单测与调试菜单）。
        /// </summary>
        public void AssignMatchRoster(int seed)
        {
            Team playerTeam = ResolvePlayerTeam();
            CollectBots(playerTeam);

            int botTotal = _allyBots.Count + _enemyBots.Count;
            if (botTotal == 0) return;

            WeaponKind playerKind = ResolvePlayerKind();

            // INKWAVE pickTeam：己方队（玩家 + 队友）类型互不重复且玩家独占一种；
            // 敌方队从全部 7 种里不放回抽，允许与玩家重复。
            WeaponKind[] allyKinds = BotLoadoutMath.DraftWeaponKinds(_allyBots.Count, playerKind,
                unchecked(seed + 101));
            WeaponKind[] enemyKinds = BotLoadoutMath.DraftWeaponKinds(_enemyBots.Count, null,
                unchecked(seed + 7919));

            string[] names = BotLoadoutMath.DraftNames(botTotal, unchecked(seed + 33));

            _roster.Clear();
            CombatantHealth localHealth = ResolveLocalPlayerHealth();
            if (localHealth != null)
                AddRosterEntry(playerKind, localHealth, playerTeam, "你", true);

            int nameCursor = 0;
            AssignGroup(_allyBots, allyKinds, names, ref nameCursor, unchecked(seed + 211));
            AssignGroup(_enemyBots, enemyKinds, names, ref nameCursor, unchecked(seed + 577));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogRosterSummary(seed);
#endif
        }

        /// <summary>测试钩子：强制下一局自动抽卡使用指定种子。</summary>
        public void ForceNextSeed(int seed)
        {
            _forcedSeed = seed;
        }

        private int NextSeed()
        {
            _matchCounter++;
            if (_forcedSeed != int.MinValue)
            {
                int forced = _forcedSeed;
                _forcedSeed = int.MinValue;
                return forced;
            }

            // 必须写全 System.Environment：裸 Environment 会被解析成 P1.Environment 命名空间。
            return unchecked(System.Environment.TickCount ^ (_matchCounter * 39916801));
        }

        private void AssignGroup(List<BotController> bots, WeaponKind[] kinds, string[] names,
            ref int nameCursor, int personalitySeedBase)
        {
            for (int i = 0; i < bots.Count; i++)
            {
                BotController bot = bots[i];
                if (bot == null) continue;

                WeaponKind kind = i < kinds.Length ? kinds[i] : WeaponKind.Shooter;
                WeaponConfigSO config = ResolveWeaponConfig(kind);
                if (config == null)
                {
                    // 资源缺失只降级为「不换枪」，不能让一整局起不来（装饰性失败不判死）。
                    Debug.LogWarning("[BotLoadout] 缺少武器配置 " + kind + "，Bot " + bot.name + " 保留原武器。");
                }

                BotPersonality personality = BotPersonalityMath.Create(unchecked(personalitySeedBase + i * 7919));
                string displayName = nameCursor < names.Length ? names[nameCursor] : null;
                nameCursor++;

                bot.AssignMatchLoadout(config, personality, displayName);

                AddRosterEntry(kind, bot.GetComponent<CombatantHealth>(), ResolvePlayerTeam(),
                    displayName, false);
            }
        }

        private void CollectBots(Team playerTeam)
        {
            _allyBots.Clear();
            _enemyBots.Clear();

            IReadOnlyList<CombatantHealth> all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth health = all[i];
                if (health == null) continue;

                var bot = health.GetComponent<BotController>();
                if (bot == null) continue;

                if (health.Team == playerTeam)
                    _allyBots.Add(bot);
                else
                    _enemyBots.Add(bot);
            }

            // 名字升序保证同种子分配稳定（注册表顺序随对象创建顺序浮动）。
            _allyBots.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
            _enemyBots.Sort((a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
        }

        private WeaponKind ResolvePlayerKind()
        {
            var local = PlayerController.LocalPlayer;
            if (local != null && local.Weapon != null && local.Weapon.Config != null)
                return local.Weapon.Config.weaponKind;

            // 玩家角色未就绪（换装重试窗口）时按战前存档回退，与 WeaponSelectBootstrap 的落点一致。
            if (PlayerPrefs.HasKey(PrefsKeySelectedWeaponIndex))
                return BotLoadoutMath.KindFromIndex(PlayerPrefs.GetInt(PrefsKeySelectedWeaponIndex, 0));

            return WeaponKind.Shooter;
        }

        private Team ResolvePlayerTeam()
        {
            var local = PlayerController.LocalPlayer;
            if (local != null && local.Health != null)
                return local.Health.Team;

            return Team.TeamA;
        }

        private CombatantHealth ResolveLocalPlayerHealth()
        {
            var local = PlayerController.LocalPlayer;
            return local != null ? local.Health : null;
        }

        private void AddRosterEntry(WeaponKind kind, CombatantHealth health, Team team,
            string displayName, bool isLocalPlayer)
        {
            Team resolvedTeam = health != null ? health.Team : team;
            string weaponName = WeaponSelectName(kind);

            _roster.Add(new RosterEntry
            {
                Team = resolvedTeam,
                IsLocalPlayer = isLocalPlayer,
                DisplayName = displayName,
                WeaponKind = kind,
                WeaponDisplayName = weaponName,
            });
        }

        private static string WeaponSelectName(WeaponKind kind)
        {
            WeaponConfigSO config = ResolveWeaponConfig(kind);
            if (config != null && !string.IsNullOrEmpty(config.displayName))
                return config.displayName;

            return kind.ToString();
        }

        /// <summary>
        /// 按武器类型取 WeaponConfigSO：独立包走 Resources/P1RuntimeAssets，
        /// 编辑器回退资产路径，最后全对象扫描兜底（与 WeaponSelectBootstrap.LoadAssets 同链路）。
        /// </summary>
        private static WeaponConfigSO ResolveWeaponConfig(WeaponKind kind)
        {
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null)
            {
                WeaponConfigSO fromLibrary = library.GetWeapon(kind);
                if (fromLibrary != null) return fromLibrary;
            }

#if UNITY_EDITOR
            WeaponConfigSO fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(
                BotLoadoutMath.GetWeaponAssetPath(kind));
            if (fromAsset != null) return fromAsset;
#endif

            WeaponConfigSO[] all = Resources.FindObjectsOfTypeAll<WeaponConfigSO>();
            for (int i = 0; i < all.Length; i++)
            {
                WeaponConfigSO candidate = all[i];
                if (candidate != null && candidate.weaponKind == kind && !candidate.name.Contains("(Clone)"))
                    return candidate;
            }

            return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void LogRosterSummary(int seed)
        {
            var sb = new System.Text.StringBuilder(160);
            sb.Append("[BotLoadout] 本局抽卡 seed=").Append(seed).Append(" | ");
            for (int i = 0; i < _roster.Count; i++)
            {
                RosterEntry entry = _roster[i];
                sb.Append(entry.IsLocalPlayer ? "玩家" : (entry.Team == ResolvePlayerTeam() ? "友" : "敌"))
                  .Append(':').Append(entry.DisplayName)
                  .Append('(').Append(entry.WeaponKind).Append(") ");
            }

            Debug.Log(sb.ToString());
        }
#endif
    }
}
