using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// Callout 播报分类与优先级：数值越大优先级越高。
    /// 优先级链对齐 INKWAVE：
    /// WIPEOUT! (6) > MULTI SPLAT (5) > FIRST SPLAT (4) > REVENGE (3) > SHUTDOWN (2) > STREAK (1) > NONE (0)。
    /// </summary>
    public enum CalloutKind
    {
        None = 0,
        Streak = 1,
        Shutdown = 2,
        Revenge = 3,
        FirstSplat = 4,
        MultiSplat = 5,
        Wipeout = 6
    }

    /// <summary>
    /// Callout 评估结果（纯值类型、零 GC）。
    /// </summary>
    [Serializable]
    public struct CalloutResult
    {
        public CalloutKind Kind;
        public string MainText;
        public string SubText;
        public int Priority;
        public bool IsBig;
        public Team SplattedTeam;

        public bool HasCallout => Kind != CalloutKind.None;

        public static readonly CalloutResult None = new CalloutResult
        {
            Kind = CalloutKind.None,
            MainText = string.Empty,
            SubText = string.Empty,
            Priority = 0,
            IsBig = false,
            SplattedTeam = Team.Neutral
        };
    }

    /// <summary>
    /// 战局大字播报纯逻辑与状态演算中枢（纯逻辑、零 GC）：
    /// 对齐 INKWAVE 的 Callout 播报链（hud.js _onSplatted）：
    /// 维护本地连杀窗口（4.2s）、复仇目标（Revenge）、各战斗员连杀数（Shutdown 阈值 ≥3）、
    /// 首杀标记（FirstSplat 每局一次）以及团灭判定（Wipeout 带复活去抖与冷却）。
    /// </summary>
    public class MatchCalloutMath
    {
        public const string TextWipeout = "WIPEOUT!";
        public const string SubWipeout = "THE WHOLE TEAM IS SPLATTED";

        public const string TextDoubleSplat = "DOUBLE SPLAT!";
        public const string TextTripleSplat = "TRIPLE SPLAT!";
        public const string TextQuadSplat = "QUAD SPLAT!";

        public const string TextFirstSplat = "FIRST SPLAT!";
        public const string TextRevenge = "REVENGE!";
        public const string TextShutdown = "SHUTDOWN!";
        public const string SubShutdown = "ENDED ENEMY'S STREAK";

        private static readonly string[] StreakTexts = InitStreakTexts();

        private static string[] InitStreakTexts()
        {
            var arr = new string[32];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = "SPLAT STREAK ×" + i;
            }
            return arr;
        }

        public static string GetStreakText(int streak)
        {
            if (streak <= 0) return string.Empty;
            if (streak < StreakTexts.Length) return StreakTexts[streak];
            return StreakTexts[StreakTexts.Length - 1];
        }

        /// <summary>本地玩家多杀窗口（秒），默认 4.2s 对齐 INKWAVE。</summary>
        public float MultiSplatWindowSeconds { get; set; } = 4.2f;

        /// <summary>终结连杀阈值：被击杀者当前连杀数 ≥ 此阈值触发 SHUTDOWN。</summary>
        public int ShutdownStreakThreshold { get; set; } = 3;

        /// <summary>团灭播报冷却（秒），避免 4s 复活窗口与残局瞬间重复触发。</summary>
        public float WipeoutCooldownSeconds { get; set; } = 8.0f;

        private readonly float[] _localKillTimestamps = new float[16];
        private int _localKillCountInWindow;

        private int _localStreak;
        private bool _hasPlayedFirstSplat;
        private string _lastKillerName;

        // 全体战斗员连杀计数（预分配容量 16，避免扩容 GC）
        private readonly Dictionary<string, int> _actorStreaks = new Dictionary<string, int>(16, StringComparer.Ordinal);

        // 队伍 Wipeout 冷却时间戳
        private float _lastWipeoutTimeTeamA = -999f;
        private float _lastWipeoutTimeTeamB = -999f;

        public int LocalStreak => _localStreak;
        public bool HasPlayedFirstSplat => _hasPlayedFirstSplat;
        public string LastKillerName => _lastKillerName;
        public int LocalKillCountInWindow => _localKillCountInWindow;

        public int GetActorStreak(string actorName)
        {
            if (string.IsNullOrEmpty(actorName)) return 0;
            _actorStreaks.TryGetValue(actorName, out int s);
            return s;
        }

        /// <summary>
        /// 输入单条击杀事件、当前时间与受害者队伍的人数统计，评估当前播报。
        /// </summary>
        public CalloutResult Evaluate(in KillFeedEvent ev, float currentTime, int victimTeamTotalCount, int victimTeamAliveCount)
        {
            // 1. 各战斗员连杀状态更新（供 Shutdown 判定）
            int victimStreak = 0;
            if (!string.IsNullOrEmpty(ev.VictimName))
            {
                _actorStreaks.TryGetValue(ev.VictimName, out victimStreak);
                _actorStreaks[ev.VictimName] = 0; // 受害者死亡，连杀归零
            }

            if (!string.IsNullOrEmpty(ev.KillerName) && !string.Equals(ev.KillerName, ev.VictimName, StringComparison.Ordinal))
            {
                _actorStreaks.TryGetValue(ev.KillerName, out int kStreak);
                _actorStreaks[ev.KillerName] = kStreak + 1;
            }

            // 2. 本地玩家阵亡处理
            if (ev.IsLocalPlayerVictim)
            {
                _lastKillerName = ev.KillerName;
                _localStreak = 0;
                _localKillCountInWindow = 0;
            }

            // 3. 本地玩家击杀多杀时间窗口与连杀更新
            bool isLocalKill = ev.IsLocalPlayerKiller;
            int multiCount = 0;
            if (isLocalKill)
            {
                _localStreak++;

                int kept = 0;
                float cutoff = currentTime - MultiSplatWindowSeconds;
                for (int i = 0; i < _localKillCountInWindow; i++)
                {
                    if (_localKillTimestamps[i] >= cutoff - 0.0001f)
                    {
                        _localKillTimestamps[kept++] = _localKillTimestamps[i];
                    }
                }

                if (kept < _localKillTimestamps.Length)
                {
                    _localKillTimestamps[kept++] = currentTime;
                }
                _localKillCountInWindow = kept;
                multiCount = _localKillCountInWindow;
            }

            // 4. 判定队伍全灭（WIPEOUT）：受害者队伍总人数 > 0 且当前存活人数 == 0
            bool isWipeout = false;
            if (victimTeamTotalCount > 0 && victimTeamAliveCount == 0)
            {
                ref float lastWipeoutTime = ref (ev.VictimTeam == Team.TeamA ? ref _lastWipeoutTimeTeamA : ref _lastWipeoutTimeTeamB);
                if (currentTime - lastWipeoutTime >= WipeoutCooldownSeconds)
                {
                    isWipeout = true;
                    lastWipeoutTime = currentTime;
                }
            }

            // 5. 按优先级链挑选播报
            // 优先级 6：WIPEOUT
            if (isWipeout)
            {
                if (isLocalKill)
                {
                    if (!string.IsNullOrEmpty(_lastKillerName) && string.Equals(ev.VictimName, _lastKillerName, StringComparison.Ordinal))
                    {
                        _lastKillerName = null;
                    }
                    _hasPlayedFirstSplat = true;
                }

                return new CalloutResult
                {
                    Kind = CalloutKind.Wipeout,
                    MainText = TextWipeout,
                    SubText = SubWipeout,
                    Priority = (int)CalloutKind.Wipeout,
                    IsBig = true,
                    SplattedTeam = ev.VictimTeam
                };
            }

            // 非本地玩家击杀且非团灭，不向本地玩家弹出个人播报
            if (!isLocalKill)
            {
                return CalloutResult.None;
            }

            // 优先级 5：MULTI SPLAT（4.2s 窗口连杀：Double / Triple / Quad）
            if (multiCount >= 2)
            {
                if (!string.IsNullOrEmpty(_lastKillerName) && string.Equals(ev.VictimName, _lastKillerName, StringComparison.Ordinal))
                {
                    _lastKillerName = null;
                }
                _hasPlayedFirstSplat = true;

                string mainText;
                bool isBig;
                if (multiCount == 2)
                {
                    mainText = TextDoubleSplat;
                    isBig = false;
                }
                else if (multiCount == 3)
                {
                    mainText = TextTripleSplat;
                    isBig = true;
                }
                else
                {
                    mainText = TextQuadSplat;
                    isBig = true;
                }

                return new CalloutResult
                {
                    Kind = CalloutKind.MultiSplat,
                    MainText = mainText,
                    SubText = string.Empty,
                    Priority = (int)CalloutKind.MultiSplat,
                    IsBig = isBig,
                    SplattedTeam = ev.VictimTeam
                };
            }

            // 优先级 4：FIRST SPLAT（整局一次）
            if (!_hasPlayedFirstSplat)
            {
                _hasPlayedFirstSplat = true;
                if (!string.IsNullOrEmpty(_lastKillerName) && string.Equals(ev.VictimName, _lastKillerName, StringComparison.Ordinal))
                {
                    _lastKillerName = null;
                }

                return new CalloutResult
                {
                    Kind = CalloutKind.FirstSplat,
                    MainText = TextFirstSplat,
                    SubText = string.Empty,
                    Priority = (int)CalloutKind.FirstSplat,
                    IsBig = false,
                    SplattedTeam = ev.VictimTeam
                };
            }

            // 优先级 3：REVENGE（复仇击杀最近击杀者）
            if (!string.IsNullOrEmpty(_lastKillerName) && string.Equals(ev.VictimName, _lastKillerName, StringComparison.Ordinal))
            {
                _lastKillerName = null;
                return new CalloutResult
                {
                    Kind = CalloutKind.Revenge,
                    MainText = TextRevenge,
                    SubText = string.Empty,
                    Priority = (int)CalloutKind.Revenge,
                    IsBig = false,
                    SplattedTeam = ev.VictimTeam
                };
            }

            // 优先级 2：SHUTDOWN（终结对方 ≥3 连杀）
            if (victimStreak >= ShutdownStreakThreshold)
            {
                return new CalloutResult
                {
                    Kind = CalloutKind.Shutdown,
                    MainText = TextShutdown,
                    SubText = SubShutdown,
                    Priority = (int)CalloutKind.Shutdown,
                    IsBig = false,
                    SplattedTeam = ev.VictimTeam
                };
            }

            // 优先级 1：SPLAT STREAK（自身总连杀 ≥3）
            if (_localStreak >= 3)
            {
                return new CalloutResult
                {
                    Kind = CalloutKind.Streak,
                    MainText = GetStreakText(_localStreak),
                    SubText = string.Empty,
                    Priority = (int)CalloutKind.Streak,
                    IsBig = false,
                    SplattedTeam = ev.VictimTeam
                };
            }

            return CalloutResult.None;
        }

        /// <summary>
        /// 便捷重载：自动从战斗员列表中统计受害者队伍的人数状态。
        /// </summary>
        public CalloutResult Evaluate(in KillFeedEvent ev, float currentTime, IReadOnlyList<CombatantHealth> combatants)
        {
            CountTeamCombatants(combatants, ev.VictimTeam, out int total, out int alive);
            return Evaluate(ev, currentTime, total, alive);
        }

        /// <summary>
        /// 零 GC 统计队伍总人数与存活人数（不使用 LINQ）。
        /// </summary>
        public static void CountTeamCombatants(IReadOnlyList<CombatantHealth> list, Team team, out int total, out int alive)
        {
            total = 0;
            alive = 0;
            if (list == null) return;
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                var ch = list[i];
                if (ch != null && ch.Team == team)
                {
                    total++;
                    if (ch.IsAlive)
                    {
                        alive++;
                    }
                }
            }
        }

        /// <summary>
        /// 同帧多事件冲突仲裁：返回优先级最高者。
        /// </summary>
        public static CalloutResult SelectHighestPriority(CalloutResult a, CalloutResult b)
        {
            return b.Priority > a.Priority ? b : a;
        }

        /// <summary>
        /// 重置所有本地状态（Ready/Result 阶段调用）。
        /// </summary>
        public void Reset()
        {
            _localKillCountInWindow = 0;
            for (int i = 0; i < _localKillTimestamps.Length; i++)
            {
                _localKillTimestamps[i] = 0f;
            }
            _localStreak = 0;
            _hasPlayedFirstSplat = false;
            _lastKillerName = null;
            _actorStreaks.Clear();
            _lastWipeoutTimeTeamA = -999f;
            _lastWipeoutTimeTeamB = -999f;
        }
    }
}
