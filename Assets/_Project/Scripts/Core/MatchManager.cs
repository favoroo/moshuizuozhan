using System;
using P1.Ink;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 比赛流程状态机：Ready → Countdown(3-2-1 GO) → Playing(120s) → FinishFreeze → Result → Restart。
    /// 负责计时、结算取数、清场重开，并向 UI 广播阶段变化。
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public enum Phase { Ready, Countdown, Playing, Finished, Result }

        [SerializeField] private GameConfigSO gameConfig;
        [SerializeField] private float countdownSeconds = 3f;
        [SerializeField] private float finishFreezeSeconds = 1.6f;
        [SerializeField] private float resultRollSeconds = 1.4f;

        private float _remaining;
        private float _countdown;
        private float _freeze;
        private int _lastCountdownShown = -1;
        private float _finalA;
        private float _finalB;

        public Phase CurrentPhase { get; private set; } = Phase.Ready;
        public float RemainingSeconds => Mathf.Max(0f, _remaining);
        public int CountdownNumber => Mathf.Max(0, Mathf.CeilToInt(_countdown));
        public float FinalCoverageA => _finalA;
        public float FinalCoverageB => _finalB;
        public float ResultRollSeconds => resultRollSeconds;
        public bool PlayerWon => _finalA >= _finalB;

        public event Action<Phase> PhaseChanged;
        public event Action<int> CountdownTicked;

        /// <summary>只有 Playing 阶段允许移动、开火与涂地推进比赛。</summary>
        public static bool CombatAllowed =>
            Instance == null || Instance.CurrentPhase == Phase.Playing;

        public static bool InputAllowed =>
            Instance == null || Instance.CurrentPhase == Phase.Playing;

        private void Awake()
        {
            Instance = this;
            _remaining = gameConfig != null ? gameConfig.matchDurationSeconds : 120f;
        }

        public void Configure(GameConfigSO config)
        {
            gameConfig = config;
            _remaining = gameConfig != null ? gameConfig.matchDurationSeconds : 120f;
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
                    _countdown -= dt;
                    int shown = Mathf.CeilToInt(Mathf.Max(0f, _countdown));
                    if (shown != _lastCountdownShown)
                    {
                        _lastCountdownShown = shown;
                        CountdownTicked?.Invoke(shown);
                        if (AudioManager.Instance != null && shown > 0)
                        {
                            AudioManager.Instance.PlayOne(AudioManager.Instance.countdownTickClip, 0.7f, 0.98f, 1.02f);
                        }
                    }
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
            _remaining = gameConfig != null ? gameConfig.matchDurationSeconds : 120f;
            _countdown = countdownSeconds;
            _lastCountdownShown = -1;
            _finalA = 0f;
            _finalB = 0f;
            SetPhase(Phase.Countdown);

            if (AudioManager.Instance != null && AudioManager.Instance.matchStartClip != null)
            {
                AudioManager.Instance.PlayOne(AudioManager.Instance.matchStartClip, 0.6f, 0.98f, 1.02f);
            }
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

            if (AudioManager.Instance != null && AudioManager.Instance.matchEndClip != null)
            {
                AudioManager.Instance.PlayOne(AudioManager.Instance.matchEndClip, 0.8f, 0.98f, 1.02f);
            }
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
