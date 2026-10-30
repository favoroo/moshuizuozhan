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
        private UIThemeSO _theme;
        private readonly WeaponConfigSO[] _configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
        private readonly Sprite[] _icons = new Sprite[WeaponSelectMath.WeaponCount];
        private Coroutine _equipRetryCoroutine;
        private bool _subscribedMatch;
        private MatchManager _currentMatch;
        private bool _hasExplicitSelection;
        private bool _weaponAppliedExternally;

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

            MatchManager match = Object.FindAnyObjectByType<MatchManager>();
            if (match == null) return;

            var hostGo = new GameObject("[WeaponSelectBootstrap]");
            DontDestroyOnLoad(hostGo);
            hostGo.AddComponent<WeaponSelectBootstrap>();
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
        }

        private void SubscribeMatch(MatchManager match)
        {
            if (match == null || _subscribedMatch) return;
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
                _currentMatch = null;
            }
            _subscribedMatch = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
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

        public void ApplyWeaponToLocalPlayer(WeaponConfigSO config)
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
                PlayerController.LocalPlayer.Weapon.EquipWeapon(config);
            }
            else
            {
                _equipRetryCoroutine = StartCoroutine(RetryEquipRoutine(config));
            }
        }

        private IEnumerator RetryEquipRoutine(WeaponConfigSO config)
        {
            const int maxFrames = 30;
            int frames = 0;

            while (frames < maxFrames)
            {
                yield return null;
                frames++;

                if (PlayerController.LocalPlayer != null && PlayerController.LocalPlayer.Weapon != null)
                {
                    PlayerController.LocalPlayer.Weapon.EquipWeapon(config);
                    yield break;
                }
            }
        }
    }
}
