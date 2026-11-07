using System.Collections;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 战前武器选择引导启动器（P8 选枪系统）：
    /// 1. 运行时自动检测 Playbox 场景中是否存在 MatchManager；
    /// 2. 仅在 MatchManager.Phase.Ready 显示选枪界面，其它阶段自动隐藏；
    /// 3. Ready 阶段实时在主角身上预览切换武器视觉与动作；
    /// 4. Countdown 阶段确保把选中的 WeaponConfigSO 应用到 PlayerController.LocalPlayer.Weapon；
    /// 5. 完全不动 Bot，保持对战平衡与多 Agent 隔离。
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class WeaponSelectBootstrap : MonoBehaviour
    {
        public static WeaponSelectBootstrap Instance { get; private set; }

        private const string EditorThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        private WeaponSelectView _view;
        private RespawnLoadoutView _respawnView;
        private UIThemeSO _theme;
        private readonly WeaponConfigSO[] _configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
        private readonly Sprite[] _icons = new Sprite[WeaponSelectMath.WeaponCount];
        private Coroutine _equipRetryCoroutine;
        private bool _subscribedMatch;
        private MatchManager _currentMatch;
        private bool _hasExplicitSelection;
        private bool _weaponAppliedExternally;

        /// <summary>当前绑定的本地玩家生命（换实例时重绑，避免拿上一局的引用判存活）。</summary>
        private CombatantHealth _respawnHealth;

        /// <summary>上一帧本地玩家是否存活：用于识别「本帧刚复活」这一唯一换枪时机。</summary>
        private bool _wasAlive = true;

        /// <summary>阵亡整备挂起的选择下标；-1 = 没有挂起。离开 Playing 时作废。</summary>
        private int _pendingRespawnLoadoutIndex = -1;

        /// <summary>本局是否明确选过武器；没有选过时不覆盖场景默认武器，避免测试/新号被无意义换装。</summary>
        private bool HasSelection =>
            _hasExplicitSelection || PlayerPrefs.HasKey(WeaponSelectMath.PrefsKeySelectedWeaponIndex);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAtRuntime()
        {
            if (!Application.isPlaying) return;
            CheckAndCreate();
        }

        private static void CheckAndCreate()
        {
            if (Instance != null) return;

            // 不要求此刻已经能 Find 到 MatchManager：RuntimeInitializeOnLoadMethod(AfterSceneLoad)
            // 与场景对象的就绪顺序并不保证，实测有进入 Play 后找不到比赛管理器、
            // 于是整个武器栏 + 「武器整备」入口都不出现的空窗。先建主机，Update 里再重试。
            var hostGo = new GameObject("[WeaponSelectBootstrap]");
            DontDestroyOnLoad(hostGo);
            hostGo.AddComponent<WeaponSelectBootstrap>();
        }

        private float _retryTimer;

        private void Update()
        {
            // 比赛管理器晚于本组件就绪时，每 0.5s 重试一次，直到接管为止。
            if (_currentMatch == null)
            {
                _retryTimer += Time.unscaledDeltaTime;
                if (_retryTimer < 0.5f) return;
                _retryTimer = 0f;
                SetupSystem();
                return;
            }

            UpdateRespawnLoadout();
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
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
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            if (_respawnView != null)
            {
                _respawnView.OnWeaponSelected -= HandleRespawnWeaponSelected;
            }

            UnsubscribeMatch();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SetupSystem();
        }

        private void SetupSystem()
        {
            MatchManager match = MatchManager.Instance != null ? MatchManager.Instance : Object.FindAnyObjectByType<MatchManager>();
            if (match == null)
            {
                if (_view != null)
                {
                    _view.SetVisible(false);
                }
                if (_respawnView != null)
                {
                    _respawnView.SetVisible(false);
                }
                return;
            }

            LoadAssets();
            EnsureView();
            SubscribeMatch(match);

            // 初始阶段响应
            HandlePhaseChanged(match.CurrentPhase);
        }

        private void LoadAssets()
        {
            // 独立包入口：从 Resources/P1RuntimeAssets 取主题、武器配置与图标。
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null)
            {
                if (_theme == null)
                    _theme = library.uiTheme;

                for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
                {
                    WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                    if (_configs[i] == null)
                        _configs[i] = library.GetWeapon(kind);
                    if (_icons[i] == null)
                        _icons[i] = library.GetWeaponIcon(kind);
                }
            }

#if UNITY_EDITOR
            if (_theme == null)
            {
                _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
            }

            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                if (_configs[i] == null)
                {
                    _configs[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(WeaponSelectMath.GetWeaponAssetPath(kind));
                }
                if (_icons[i] == null)
                {
                    _icons[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponSelectMath.GetWeaponIconAssetPath(kind));
                }
            }
#endif

            // 非编辑器或兜底查找
            if (_theme == null)
            {
                _theme = UIThemeSO.Fallback;
            }

            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                if (_configs[i] == null)
                {
                    _configs[i] = FindFallbackConfig(kind);
                }
            }
        }

        private WeaponConfigSO FindFallbackConfig(WeaponKind kind)
        {
            WeaponConfigSO[] allConfigs = Resources.FindObjectsOfTypeAll<WeaponConfigSO>();
            for (int i = 0; i < allConfigs.Length; i++)
            {
                if (allConfigs[i] != null && allConfigs[i].weaponKind == kind)
                {
                    return allConfigs[i];
                }
            }

            // 极致兜底：创建临时实例防止空指针
            var fallback = ScriptableObject.CreateInstance<WeaponConfigSO>();
            fallback.weaponKind = kind;
            fallback.displayName = WeaponSelectMath.GetWeaponEnglishName(kind);
            return fallback;
        }

        private void EnsureView()
        {
            if (_view == null)
            {
                _view = Object.FindAnyObjectByType<WeaponSelectView>();
            }

            if (_view == null)
            {
                var canvasGo = new GameObject("WeaponSelect_Canvas");
                DontDestroyOnLoad(canvasGo);
                _view = canvasGo.AddComponent<WeaponSelectView>();
            }

            int savedIndex = WeaponSelectMath.LoadSelectedWeaponIndex();
            _view.Configure(_theme, _configs, _icons, savedIndex);

            _view.OnWeaponSelected -= HandleWeaponSelected;
            _view.OnWeaponSelected += HandleWeaponSelected;

            EnsureRespawnView();
        }

        private void EnsureRespawnView()
        {
            if (_respawnView == null)
            {
                _respawnView = Object.FindAnyObjectByType<RespawnLoadoutView>();
            }

            if (_respawnView == null)
            {
                var canvasGo = new GameObject("RespawnLoadout_Canvas");
                DontDestroyOnLoad(canvasGo);
                _respawnView = canvasGo.AddComponent<RespawnLoadoutView>();
            }

            _respawnView.Configure(_theme, _configs, _icons);
            _respawnView.OnWeaponSelected -= HandleRespawnWeaponSelected;
            _respawnView.OnWeaponSelected += HandleRespawnWeaponSelected;
        }

        private void SubscribeMatch(MatchManager match)
        {
            if (match == null) return;
            // 切换地图会销毁旧 MatchManager 并重建场景；视图是 DDOL 常驻，
            // 因此必须允许「换绑」而不是见到 _subscribedMatch 就退出，否则新局选枪界面不再跟随阶段。
            if (_subscribedMatch && _currentMatch == match) return;
            if (_subscribedMatch) UnsubscribeMatch();

            _currentMatch = match;
            _currentMatch.PhaseChanged += HandlePhaseChanged;
            _subscribedMatch = true;
        }

        private void UnsubscribeMatch()
        {
            if (!_subscribedMatch) return;
            if (_currentMatch != null)
            {
                _currentMatch.PhaseChanged -= HandlePhaseChanged;
            }
            _currentMatch = null;
            _subscribedMatch = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            // 离开对局（结算 / 重开 / 回到 Ready）立即收起整备条并作废挂起选择：
            // 否则上一局阵亡时点的枪会跨局带进新局，玩家会莫名其妙多换一次枪、少一格大招。
            if (phase != MatchManager.Phase.Playing)
            {
                _pendingRespawnLoadoutIndex = -1;
                _wasAlive = true;
                if (_respawnView != null)
                {
                    _respawnView.SetVisible(false);
                }
            }

            if (_view == null) return;

            if (phase == MatchManager.Phase.Ready)
            {
                _view.SetVisible(true);
                int savedIndex = WeaponSelectMath.LoadSelectedWeaponIndex();
                _view.SelectWeapon(savedIndex, silent: true);

                if (PlayerController.LocalPlayer != null)
                {
                    _view.SetTeam(PlayerController.LocalPlayer.Health.Team);
                    if (HasSelection && !_weaponAppliedExternally)
                        ApplyWeaponToLocalPlayer(_view.SelectedConfig);
                }
            }
            else
            {
                _view.SetVisible(false);

                if (phase == MatchManager.Phase.Countdown && HasSelection)
                {
                    // 点开始后、正式开打前应用武器
                    ApplyWeaponToLocalPlayer(_view.SelectedConfig);
                }
            }
        }

        private void HandleWeaponSelected(int index, WeaponConfigSO config)
        {
            _hasExplicitSelection = true;

            // Ready 阶段支持即时换装预览
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase == MatchManager.Phase.Ready)
            {
                ApplyWeaponToLocalPlayer(config);
            }
        }

        private void HandleRespawnWeaponSelected(int index, WeaponConfigSO config)
        {
            SelectRespawnLoadout(index);
        }

        /// <summary>
        /// 阵亡整备选枪：立刻写回存档并挂起，复活那一帧才真正换装（并清空大招充能）。
        /// 为什么不在点击瞬间换装：死亡时武器视觉已隐藏，立刻换只会让「复活后手里是什么枪」依赖
        /// 点击顺序；统一在复活瞬间结算，存档与画面永远一致。
        /// </summary>
        public void SelectRespawnLoadout(int index)
        {
            int clamped = RespawnLoadoutMath.ClampIndex(index);
            _pendingRespawnLoadoutIndex = clamped;
            _hasExplicitSelection = true;
            WeaponSelectMath.SaveSelectedWeaponIndex(clamped);
        }

        /// <summary>
        /// 结算挂起的阵亡整备选择：返回是否真的换了枪（没有挂起 / 资产缺失返回 false）。
        /// 清空大招充能是换枪的代价，见 <see cref="Weapons.WeaponController.EquipWeapon(WeaponConfigSO, bool)"/>。
        /// </summary>
        public bool ApplyPendingRespawnLoadout()
        {
            int index = _pendingRespawnLoadoutIndex;
            _pendingRespawnLoadoutIndex = -1;
            if (index < 0 || index >= _configs.Length)
            {
                return false;
            }

            WeaponConfigSO config = _configs[index];
            if (config == null)
            {
                return false;
            }

            ApplyWeaponToLocalPlayer(config, resetSpecialCharge: true);
            return true;
        }

        /// <summary>
        /// 死亡/复活编排：只在 Playing 且本地玩家阵亡时弹出整备条，复活那一帧结算挂起选择。
        /// 用轮询而不是 OnKilled/OnRespawned 事件，是因为 PlayerController.LocalPlayer 会随场景重建换实例，
        /// 轮询天然跟着新实例走，不需要维护「旧身体的事件订阅」这类跨场景泄漏点。
        /// </summary>
        private void UpdateRespawnLoadout()
        {
            if (_respawnView == null) return;

            PlayerController local = PlayerController.LocalPlayer;
            CombatantHealth health = local != null ? local.Health : null;
            bool playing = _currentMatch.CurrentPhase == MatchManager.Phase.Playing;

            if (health == null)
            {
                _respawnHealth = null;
                _wasAlive = true;
                _respawnView.SetVisible(false);
                if (!playing)
                {
                    _pendingRespawnLoadoutIndex = -1;
                }
                return;
            }

            if (_respawnHealth != health)
            {
                // 换了一具身体（重开 / 换地图）：以新实例的存活状态为基线，不结算上一局的挂起选择。
                _respawnHealth = health;
                _wasAlive = health.IsAlive;
            }

            bool alive = health.IsAlive;
            if (!_wasAlive && RespawnLoadoutMath.ShouldApplyPending(
                    _currentMatch.CurrentPhase, _pendingRespawnLoadoutIndex >= 0, alive))
            {
                ApplyPendingRespawnLoadout();
            }
            _wasAlive = alive;

            bool show = RespawnLoadoutMath.ShouldShow(_currentMatch.CurrentPhase, true, alive);
            if (show && !_respawnView.IsVisible)
            {
                // 第一次弹出时以「死亡瞬间手里的枪」为初始选项，而不是存档里的——
                // 玩家可能是用上一局存档的枪进场的，两者可能不同。
                _respawnView.SetSelectedIndex(ResolveEquippedIndex(local));
            }

            _respawnView.SetVisible(show);
            if (show)
            {
                _respawnView.SetCountdown(health.RespawnRemaining);
            }

            if (!playing)
            {
                _pendingRespawnLoadoutIndex = -1;
            }
        }

        private int ResolveEquippedIndex(PlayerController local)
        {
            WeaponController weapon = local != null ? local.Weapon : null;
            WeaponKind kind = weapon != null && weapon.Config != null ? weapon.Config.weaponKind : WeaponKind.Shooter;
            return RespawnLoadoutMath.ClampIndex(WeaponSelectMath.KindToIndex(kind));
        }

        public void ApplyWeaponToLocalPlayer(WeaponConfigSO config)
        {
            ApplyWeaponToLocalPlayer(config, resetSpecialCharge: false);
        }

        /// <summary>应用武器；<paramref name="resetSpecialCharge"/> 为 true 时清空大招充能（阵亡整备换枪）。</summary>
        public void ApplyWeaponToLocalPlayer(WeaponConfigSO config, bool resetSpecialCharge)
        {
            if (config == null) return;

            // 一旦外部（测试/其他系统）直接指定武器，Ready 阶段不再用存档覆盖它。
            _weaponAppliedExternally = true;

            if (_equipRetryCoroutine != null)
            {
                StopCoroutine(_equipRetryCoroutine);
                _equipRetryCoroutine = null;
            }

            if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.Weapon != null)
            {
                PlayerController.LocalPlayer.Weapon.EquipWeapon(config, resetSpecialCharge);
            }
            else
            {
                _equipRetryCoroutine = StartCoroutine(RetryEquipRoutine(config, resetSpecialCharge));
            }
        }

        private IEnumerator RetryEquipRoutine(WeaponConfigSO config, bool resetSpecialCharge)
        {
            const int maxFrames = 30;
            int frames = 0;

            while (frames < maxFrames)
            {
                yield return null;
                frames++;

                if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.Weapon != null)
                {
                    PlayerController.LocalPlayer.Weapon.EquipWeapon(config, resetSpecialCharge);
                    yield break;
                }
            }
        }
    }
}
