using System.Collections.Generic;
using P1.Player;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 单个战斗单位的对局统计数据（击杀、阵亡、涂地面积）。
    /// MaxStreak / SpecialCasts / MaxLifeKills / RevengeKills / ClutchKill 供成就系统使用；
    /// 旧调用方不读这些字段，默认 0/false，向后兼容。CurrentLifeKills 是内部推算用累计，不参与结算。
    /// </summary>
    public struct CombatantMatchStats
    {
        public int Kills;
        public int Deaths;
        public float TurfSquareMeters;
        public int MaxStreak;
        public int SpecialCasts;
        public int MaxLifeKills;      // 单条命（两次阵亡之间）最高击杀数
        public int CurrentLifeKills;  // 当前这条命已击杀数（阵亡即清零）
        public int RevengeKills;      // 复仇击杀：击杀过「本局击杀过自己的人」的次数
        public bool ClutchKill;       // 压哨击杀：比赛剩余 ≤ ClutchWindowSeconds 时完成击杀
    }

    /// <summary>
    /// 个人对局统计服务：追踪各战斗单位（玩家与 Bot）的 Kills / Deaths / Turf。
    /// 字典以 CombatantHealth 为键，steady-state 零 GC；
    /// 自动订阅 CombatantHealth.DamageApplied 判定击杀与阵亡；
    /// 监听 MatchManager.PhaseChanged 在进入 Playing 时清零，并在 Result 保留供结算读取。
    /// </summary>
    public static class MatchStatsService
    {
        private const int DefaultCapacity = 8;
        private static readonly Dictionary<CombatantHealth, CombatantMatchStats> Stats =
            new Dictionary<CombatantHealth, CombatantMatchStats>(DefaultCapacity);

        // 复仇账本：victim → 「本局击杀过 victim 的敌人」集合；victim 反杀后销账（一次恩怨只报一次）。
        private static readonly Dictionary<CombatantHealth, HashSet<CombatantHealth>> Grudges =
            new Dictionary<CombatantHealth, HashSet<CombatantHealth>>(DefaultCapacity);

        // 全场首杀：只认有击杀者的阵亡（环境死亡不算首杀）；Result 阶段保留供结算读取。
        private static CombatantHealth _firstKillKiller;
        private static bool _hasFirstKill;

        /// <summary>压哨击杀判定窗口：比赛剩余时间 ≤ 该值（秒）时完成击杀即算压哨。</summary>
        public const float ClutchWindowSeconds = 10f;

        /// <summary>压哨窗口纯函数（EditMode 可直接断言；remainingSeconds 已被 MatchManager 钳 ≥ 0）。</summary>
        public static bool IsClutchWindow(float remainingSeconds)
        {
            return remainingSeconds >= 0f && remainingSeconds <= ClutchWindowSeconds;
        }

        /// <summary>
        /// 任意单位涂地面积增量事件：(painter, deltaSquareMeters)。
        /// 只在该次涂地有效（delta &gt; 0）时触发，HUD 据此出「+N m²」飘字，无需逐帧轮询统计字典。
        /// </summary>
        public static event System.Action<CombatantHealth, float> TurfRegistered;

        private static bool _damageAppliedSubscribed;
        private static MatchStatsRunner _runner;

        /// <summary>本局是否已产生全场首杀（有击杀者）。</summary>
        public static bool HasFirstKill => _hasFirstKill;

        /// <summary>全场首杀的击杀者（<see cref="HasFirstKill"/> 为 false 时返回 null）。</summary>
        public static CombatantHealth FirstKillKiller => _hasFirstKill ? _firstKillKiller : null;

        static MatchStatsService()
        {
            EnsureSubscribedToDamageApplied();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            if (_damageAppliedSubscribed)
            {
                CombatantHealth.DamageApplied -= OnDamageApplied;
                _damageAppliedSubscribed = false;
            }

            Stats.Clear();
            Grudges.Clear();

            _firstKillKiller = null;
            _hasFirstKill = false;

            if (_runner != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_runner.gameObject);
                }
                _runner = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            EnsureSubscribedToDamageApplied();
            EnsureRunner();
        }

        private static void EnsureSubscribedToDamageApplied()
        {
            if (_damageAppliedSubscribed)
                return;

            CombatantHealth.DamageApplied += OnDamageApplied;
            _damageAppliedSubscribed = true;
        }

        private static void EnsureRunner()
        {
            if (!Application.isPlaying || _runner != null)
                return;

            var go = new GameObject("~MatchStatsRunner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<MatchStatsRunner>();
        }

        private static void OnDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            if (!killed)
                return;

            if (attacker != null)
            {
                RegisterKill(attacker, victim);
            }
            else if (victim != null)
            {
                // 环境/跌落/无攻击者伤害导致阵亡时：只记阵亡，不记击杀
                RegisterKill(null, victim);
            }
        }

        /// <summary>
        /// 每局开始清零统计数据。保留字典内部容量，避免后续重新分配 GC。
        /// </summary>
        public static void ResetForMatch()
        {
            EnsureSubscribedToDamageApplied();
            Stats.Clear();
            Grudges.Clear();
            _firstKillKiller = null;
            _hasFirstKill = false;
        }

        /// <summary>
        /// 登记击杀与死亡。
        /// killer != null 时 killer.Kills++；victim != null 时 victim.Deaths++。
        /// 第一笔有击杀者的击杀同时记为全场首杀（环境死亡不算）。
        /// 同步维护：单条命击杀数（victim 阵亡清零自身）、复仇账本、压哨击杀标记。
        /// </summary>
        public static void RegisterKill(CombatantHealth killer, CombatantHealth victim)
        {
            EnsureSubscribedToDamageApplied();

            if (killer != null)
            {
                if (!_hasFirstKill)
                {
                    _hasFirstKill = true;
                    _firstKillKiller = killer;
                }

                // 单条命击杀数：两次阵亡之间的击杀累计（与 4 秒窗口的 MaxStreak 口径不同）
                Stats.TryGetValue(killer, out CombatantMatchStats kStats);
                kStats.Kills++;
                kStats.CurrentLifeKills++;
                if (kStats.CurrentLifeKills > kStats.MaxLifeKills)
                {
                    kStats.MaxLifeKills = kStats.CurrentLifeKills;
                }

                // 复仇：击杀过「本局击杀过自己的人」，销账后同一条恩怨不再重复记
                if (victim != null && Grudges.TryGetValue(killer, out HashSet<CombatantHealth> debts) && debts.Remove(victim))
                {
                    kStats.RevengeKills++;
                }

                // 压哨击杀：仅在 Playing 阶段、剩余时间进窗口时记（编辑器/无比赛对象时静默跳过）
                MatchManager match = MatchManager.Instance;
                if (match != null && match.CurrentPhase == MatchManager.Phase.Playing
                    && IsClutchWindow(match.RemainingSeconds))
                {
                    kStats.ClutchKill = true;
                }

                Stats[killer] = kStats;
            }

            if (victim != null)
            {
                Stats.TryGetValue(victim, out CombatantMatchStats vStats);
                vStats.Deaths++;
                vStats.CurrentLifeKills = 0; // 阵亡：这条命到此为止
                Stats[victim] = vStats;

                // 被击杀（非自杀）时记下这笔账，供之后的复仇判定
                if (killer != null && killer != victim)
                {
                    if (!Grudges.TryGetValue(victim, out HashSet<CombatantHealth> debts))
                    {
                        debts = new HashSet<CombatantHealth>();
                        Grudges[victim] = debts;
                    }
                    debts.Add(killer);
                }
            }
        }

        /// <summary>
        /// 登记涂地面积（平方米）。忽略 null、<= 0、NaN 与无穷大。
        /// </summary>
        public static void RegisterTurf(CombatantHealth painter, float squareMeters)
        {
            EnsureSubscribedToDamageApplied();

            if (painter == null || float.IsNaN(squareMeters) || float.IsInfinity(squareMeters) || squareMeters <= 0f)
                return;

            if (Stats.TryGetValue(painter, out CombatantMatchStats current))
            {
                current.TurfSquareMeters += squareMeters;
                Stats[painter] = current;
            }
            else
            {
                Stats[painter] = new CombatantMatchStats { TurfSquareMeters = squareMeters };
            }

            TurfRegistered?.Invoke(painter, squareMeters);
        }

        /// <summary>
        /// 登记连杀峰值（只保留最大值）。streak &lt;= 0 或 combatant 为 null 时忽略。
        /// </summary>
        public static void RegisterStreak(CombatantHealth combatant, int streak)
        {
            EnsureSubscribedToDamageApplied();

            if (combatant == null || streak <= 0)
                return;

            Stats.TryGetValue(combatant, out CombatantMatchStats current);
            if (streak > current.MaxStreak)
            {
                current.MaxStreak = streak;
                Stats[combatant] = current;
            }
        }

        /// <summary>
        /// 登记一次大招释放。combatant 为 null 时忽略。
        /// </summary>
        public static void RegisterSpecialCast(CombatantHealth combatant)
        {
            EnsureSubscribedToDamageApplied();

            if (combatant == null)
                return;

            Stats.TryGetValue(combatant, out CombatantMatchStats current);
            current.SpecialCasts++;
            Stats[combatant] = current;
        }

        /// <summary>
        /// 查询指定战斗单位的统计数据。未登记返回 default。
        /// </summary>
        public static CombatantMatchStats Get(CombatantHealth combatant)
        {
            EnsureSubscribedToDamageApplied();

            if (combatant == null)
                return default;

            return Stats.TryGetValue(combatant, out CombatantMatchStats stats) ? stats : default;
        }

        /// <summary>
        /// 尝试查询指定战斗单位的统计数据。未登记返回 false 且 stats = default。
        /// </summary>
        public static bool TryGet(CombatantHealth combatant, out CombatantMatchStats stats)
        {
            EnsureSubscribedToDamageApplied();

            if (combatant == null)
            {
                stats = default;
                return false;
            }

            return Stats.TryGetValue(combatant, out stats);
        }

        /// <summary>
        /// 统计指定队伍所有登记单位的涂地面积总和。
        /// </summary>
        public static float GetTeamTurf(Team team)
        {
            EnsureSubscribedToDamageApplied();

            float total = 0f;
            foreach (KeyValuePair<CombatantHealth, CombatantMatchStats> kvp in Stats)
            {
                if (kvp.Key != null && kvp.Key.Team == team)
                {
                    total += kvp.Value.TurfSquareMeters;
                }
            }
            return total;
        }

        /// <summary>
        /// 测试或跨局清理数据。
        /// </summary>
        public static void Clear()
        {
            EnsureSubscribedToDamageApplied();
            Stats.Clear();
            Grudges.Clear();
            _firstKillKiller = null;
            _hasFirstKill = false;
        }

        /// <summary>
        /// 状态机阶段流转处理：Ready/Countdown -> Playing 边沿自动 Reset；Result 阶段保留数据供结算。
        /// 供内部 runner 及 EditMode 单元测试直接调用。
        /// </summary>
        public static void HandlePhaseTransition(MatchManager.Phase previousPhase, MatchManager.Phase newPhase)
        {
            if ((previousPhase == MatchManager.Phase.Ready || previousPhase == MatchManager.Phase.Countdown)
                && newPhase == MatchManager.Phase.Playing)
            {
                ResetForMatch();
            }
        }

        /// <summary>
        /// 运行时后台挂载组件，负责跨场景生命周期与 MatchManager.PhaseChanged 事件监听。
        /// </summary>
        private sealed class MatchStatsRunner : MonoBehaviour
        {
            private MatchManager _hookedMatch;
            private MatchManager.Phase? _lastPhase;

            private void Update()
            {
                MatchManager current = MatchManager.Instance;
                if (current != _hookedMatch)
                {
                    if (_hookedMatch != null)
                    {
                        _hookedMatch.PhaseChanged -= OnPhaseChanged;
                    }

                    _hookedMatch = current;
                    if (_hookedMatch != null)
                    {
                        _hookedMatch.PhaseChanged += OnPhaseChanged;
                        _lastPhase = _hookedMatch.CurrentPhase;
                    }
                }
            }

            private void OnPhaseChanged(MatchManager.Phase phase)
            {
                MatchManager.Phase previous = _lastPhase ?? phase;
                _lastPhase = phase;
                HandlePhaseTransition(previous, phase);
            }

            private void OnDestroy()
            {
                if (_hookedMatch != null)
                {
                    _hookedMatch.PhaseChanged -= OnPhaseChanged;
                    _hookedMatch = null;
                }
            }
        }
    }
}
