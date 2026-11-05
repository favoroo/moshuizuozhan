using System;
using P1.Environment;
using P1.Ink;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 比赛流程状态机：Ready → Countdown(开赛墨幕动画) → Playing(2/3/5 分钟，首页可选) → FinishFreeze → Result → Restart。
    /// 负责计时、结算取数、清场重开，并向 UI 广播阶段变化。
    /// Countdown 已不再做 3-2-1 数字倒数，而是只播一次入场演出（墨幕涌满 → 虹膜撕开露出战场），
    /// 演完立刻进入 Playing；阶段名保留是为了 WeaponSelect / HUD 复位 / 移动端触控等既有订阅方不必改动。
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public enum Phase { Ready, Countdown, Playing, Finished, Result }

        [SerializeField] private GameConfigSO gameConfig;

        [Tooltip("已退役：旧版 3-2-1 数字倒计时时长，仅在 introSeconds < 0 时作为兜底")]
        [SerializeField] private float countdownSeconds = 3f;

        [Tooltip("开赛入场演出时长（秒）：默认 0.86，与 ScreenInkTransitionMath.MatchIntroTotalDuration 对齐。" +
                 "不要改短于 0.86（墨幕动画固定 0.86s，短于它会在动画未结束时解锁操作）；<0 = 回退旧 3-2-1 倒计时")]
        [SerializeField] private float introSeconds = 0.86f;

        [SerializeField] private float finishFreezeSeconds = 1.6f;
        [SerializeField] private float resultRollSeconds = 1.4f;

        private float _remaining;
        private float _countdown;
        private float _freeze;
        private float _finalA;
        private float _finalB;

        public Phase CurrentPhase { get; private set; } = Phase.Ready;
        public float RemainingSeconds => Mathf.Max(0f, _remaining);
        public int CountdownNumber => Mathf.Max(0, Mathf.CeilToInt(_countdown));
        public float FinalCoverageA => _finalA;
        public float FinalCoverageB => _finalB;
        public float ResultRollSeconds => resultRollSeconds;
        public bool PlayerWon => _finalA >= _finalB;
        public GameConfigSO Config => gameConfig;

        private OutOfBoundsGuard _boundsGuard;
        private SpawnZone _spawnZoneA;
        private SpawnZone _spawnZoneB;

        public event Action<Phase> PhaseChanged;

        /// <summary>已退役：3-2-1 数字倒数取消后不再触发；事件保留仅为 AudioManager 等旧订阅方兼容。</summary>
        public event Action<int> CountdownTicked;

        /// <summary>只有 Playing 阶段允许移动、开火与涂地推进比赛。</summary>
        public static bool CombatAllowed =>
            Instance == null || Instance.CurrentPhase == Phase.Playing;

        /// <summary>
        /// 终场冻结门禁：伤害 / 涂地 / 爆炸 / 涂地统计的结算入口统一读它。
        /// 与 <see cref="CombatAllowed"/> 的差别是「只锁终场之后」——
        /// 赛前 Ready / Countdown 仍允许开火（训练场与运行时验收会直接对枪验证弹道，
        /// 此时阶段尚未进入 Playing），一旦吹哨进入 Finished / Result，任何后续墨迹与击杀都会
        /// 污染已取好的结算快照，必须停结算（表现可以继续）。
        /// 无 MatchManager（纯逻辑测试 / 编辑器工具）时恒为 true。
        /// </summary>
        public static bool CombatSettlementAllowed =>
            Instance == null ||
            (Instance.CurrentPhase != Phase.Finished && Instance.CurrentPhase != Phase.Result);

        public static bool InputAllowed =>
            Instance == null || Instance.CurrentPhase == Phase.Playing;

        private void Awake()
        {
            Instance = this;
            // 物理层过滤：场景碰撞体只在启动时分类一次，之后弹丸/角色/Bot 的查询只扫 World|Combatant
            PhysicsLayerBootstrap.EnsureSceneLayers();
            _remaining = ResolveMatchDurationSeconds();
            EnsureRuntimeGuards();
        }

        /// <summary>
        /// 本局时长：优先使用首页「时长」段选（GameSettingsService，2/3/5 分钟，默认 2 分钟）；
        /// 设置服务不可用时回退 GameConfigSO 的设计值，保证纯逻辑测试/编辑器工具仍能开局。
        /// </summary>
        private float ResolveMatchDurationSeconds()
        {
            float selected = GameSettingsService.MatchDurationSeconds;
            if (selected > 0f)
                return selected;
            return gameConfig != null ? gameConfig.matchDurationSeconds : 120f;
        }

        public void Configure(GameConfigSO config)
        {
            gameConfig = config;
            _remaining = ResolveMatchDurationSeconds();
            EnsureRuntimeGuards();
            if (_boundsGuard != null) _boundsGuard.Configure(gameConfig);
            if (_spawnZoneA != null) _spawnZoneA.Configure(Team.TeamA, gameConfig);
            if (_spawnZoneB != null) _spawnZoneB.Configure(Team.TeamB, gameConfig);
        }

        private void EnsureRuntimeGuards()
        {
            if (_boundsGuard == null)
            {
                _boundsGuard = GetComponent<OutOfBoundsGuard>();
                if (_boundsGuard == null)
                {
                    _boundsGuard = gameObject.AddComponent<OutOfBoundsGuard>();
                }
                _boundsGuard.Configure(gameConfig);
            }

            if (_spawnZoneA == null)
            {
                _spawnZoneA = SpawnZone.FindZone(Team.TeamA);
                if (_spawnZoneA == null)
                {
                    _spawnZoneA = SpawnZone.CreateZone(Team.TeamA, transform, gameConfig);
                }
            }

            if (_spawnZoneB == null)
            {
                _spawnZoneB = SpawnZone.FindZone(Team.TeamB);
                if (_spawnZoneB == null)
                {
                    _spawnZoneB = SpawnZone.CreateZone(Team.TeamB, transform, gameConfig);
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            SetPhase(Phase.Ready);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            switch (CurrentPhase)
            {
                case Phase.Countdown:
                    // 开场演出窗口：没有 3-2-1 数字、没有逐秒提示音，
                    // 视觉由 ScreenInkTransitionView 的墨幕撕开承担，时间一到直接开打。
                    _countdown -= dt;
                    if (_countdown <= 0f)
                    {
                        BeginPlaying();
                    }
                    break;

                case Phase.Playing:
                    _remaining -= dt;
                    if (_remaining <= 0f)
                    {
                        _remaining = 0f;
                        FinishMatch();
                    }
                    break;

                case Phase.Finished:
                    _freeze -= dt;
                    if (_freeze <= 0f)
                    {
                        SetPhase(Phase.Result);
                    }
                    break;
            }
        }

        public void StartMatch()
        {
            if (CurrentPhase != Phase.Ready && CurrentPhase != Phase.Result) return;
            RestartMatch();
        }

        public void RestartMatch()
        {
            ResetWorld();
            _remaining = ResolveMatchDurationSeconds();
            // 开赛演出：默认走新的墨幕入场（0.86s，与动画同长）；负值才回退旧的 3-2-1 倒计时。
            _countdown = introSeconds >= 0f ? introSeconds : countdownSeconds;
            _finalA = 0f;
            _finalB = 0f;
            SetPhase(Phase.Countdown);
            // 赛前蓄势音由 AudioManager 在 PhaseChanged(Countdown) 统一播放，流程音只留一个出口。
        }

        /// <summary>
        /// 返回赛前主菜单（Ready 阶段）：清场、复位计时与结算数据。
        /// 供暂停菜单「返回主菜单」入口调用；已在 Ready 时静默忽略。
        /// </summary>
        public void ReturnToMenu()
        {
            if (CurrentPhase == Phase.Ready) return;

            ResetWorld();
            _remaining = ResolveMatchDurationSeconds();
            _countdown = 0f;
            _freeze = 0f;
            _finalA = 0f;
            _finalB = 0f;
            SetPhase(Phase.Ready);
        }

        private void BeginPlaying()
        {
            _countdown = 0f;
            SetPhase(Phase.Playing);
        }

        private void FinishMatch()
        {
            var surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                _finalA = surface.GetCoveragePercent(Team.TeamA);
                _finalB = surface.GetCoveragePercent(Team.TeamB);
            }

            _freeze = finishFreezeSeconds;
            SetPhase(Phase.Finished);
            // 终场定格音由 AudioManager 在 PhaseChanged(Finished) 统一播放（含 BGM 闪避与淡出）。
        }

        private void SetPhase(Phase phase)
        {
            if (CurrentPhase == phase) return;
            CurrentPhase = phase;
            PhaseChanged?.Invoke(phase);
        }

        /// <summary>清空墨水、把 8 名角色送回各自出生点、清掉飞行中的弹丸与手雷。</summary>
        public void ResetWorld()
        {
            if (InkSurfaceController.Instance != null)
            {
                InkSurfaceController.Instance.ResetSurface();
            }

            Weapons.InkProjectilePool.Instance?.ClearAllProjectiles();
            Weapons.InkBombPool.Instance?.ClearAllBombs();

            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;

                var weapon = c.GetComponent<Weapons.WeaponController>();
                weapon?.ResetAllWeaponState();

                var motor = c.GetComponent<Player.InkCharacterMotor>();
                motor?.RefillInkFull();

                c.ResetImmediately();
            }
        }
    }
}
