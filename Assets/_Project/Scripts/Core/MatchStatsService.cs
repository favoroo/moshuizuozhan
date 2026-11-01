using System.Collections.Generic;
using P1.Player;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 单个战斗单位的对局统计数据（击杀、阵亡、涂地面积）。
    /// </summary>
    public struct CombatantMatchStats
    {
        public int Kills;
        public int Deaths;
        public float TurfSquareMeters;
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

        private static bool _damageAppliedSubscribed;
        private static MatchStatsRunner _runner;

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
        }

        /// <summary>
        /// 登记击杀与死亡。
        /// killer != null 时 killer.Kills++；victim != null 时 victim.Deaths++。
        /// </summary>
        public static void RegisterKill(CombatantHealth killer, CombatantHealth victim)
        {
            EnsureSubscribedToDamageApplied();

            if (killer != null)
            {
                if (Stats.TryGetValue(killer, out CombatantMatchStats kStats))
                {
                    kStats.Kills++;
                    Stats[killer] = kStats;
                }
                else
                {
                    Stats[killer] = new CombatantMatchStats { Kills = 1 };
                }
            }

            if (victim != null)
            {
                if (Stats.TryGetValue(victim, out CombatantMatchStats vStats))
                {
                    vStats.Deaths++;
                    Stats[victim] = vStats;
                }
                else
                {
                    Stats[victim] = new CombatantMatchStats { Deaths = 1 };
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
