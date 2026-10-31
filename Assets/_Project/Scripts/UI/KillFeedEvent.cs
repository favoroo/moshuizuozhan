using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 击杀卡片事件数据模型（P4-D）：
    /// 记录击杀者、受害者、所属队伍、使用的武器/技能以及是否涉及本地玩家。
    /// </summary>
    [System.Serializable]
    public struct KillFeedEvent
    {
        public string KillerName;
        public Team KillerTeam;
        public string VictimName;
        public Team VictimTeam;
        public WeaponKind WeaponKind;
        public string WeaponDisplayName;
        public bool IsSubBomb;
        public bool IsSpecial;
        public bool IsLocalPlayerKiller;
        public bool IsLocalPlayerVictim;
        public float Timestamp;

        public bool IsLocalTeamKill => KillerTeam == PlayerLocalTeam;

        public static Team PlayerLocalTeam
        {
            get
            {
                var player = PlayerController.LocalPlayer;
                return player != null && player.Health != null ? player.Health.Team : Team.TeamA;
            }
        }
    }

    /// <summary>
    /// 本地玩家击杀连杀状态（P6-B，纯静态、零 GC）：
    /// CombatantHealth.Die 在击杀瞬间登记，击杀琶音音量与 KillFlashView 边缘闪光
    /// 按窗口内的连杀数逐层加码（对齐 Inkwave「连击加温」的节奏语义）。
    /// </summary>
    public static class KillFeedbackState
    {
        private static float _lastLocalKillUnscaledTime = -999f;
        private static int _streak;

        /// <summary>当前连杀数（连杀窗口内；窗口外重新从 1 计）。</summary>
        public static int CurrentStreak => _streak;

        /// <summary>连杀判定窗口（秒），由 PlayerConfigSO.killStreakWindowSeconds 在每次击杀时写入。</summary>
        public static float StreakWindowSeconds { get; set; } = 4f;

        /// <summary>本地玩家击杀时调用：返回本次击杀的连杀数（1 = 首杀）。</summary>
        public static int RegisterLocalKill()
        {
            _streak = Time.unscaledTime - _lastLocalKillUnscaledTime <= Mathf.Max(0.1f, StreakWindowSeconds)
                ? _streak + 1
                : 1;
            _lastLocalKillUnscaledTime = Time.unscaledTime;
            return _streak;
        }

        /// <summary>跨局清零（KillFlashView 在 Ready/Finished 阶段调用）。</summary>
        public static void Reset()
        {
            _streak = 0;
            _lastLocalKillUnscaledTime = -999f;
        }
    }

    /// <summary>
    /// 全局击杀事件中枢：解耦战斗逻辑与 UI 击杀播报。
    /// 无论来自 CombatantHealth.Die、武器爆炸还是测试调试命令，均在此统一分发。
    /// </summary>
    public static class KillFeedEventHub
    {
        public static event System.Action<KillFeedEvent> OnKillReported;

        public static void ReportKill(KillFeedEvent ev)
        {
            if (ev.Timestamp <= 0f) ev.Timestamp = Time.time;
            OnKillReported?.Invoke(ev);
        }

        /// <summary>
        /// 当某个 CombatantHealth 阵亡时调用，自动提取名字、队伍和武器信息。
        /// </summary>
        public static void ReportCombatantDeath(
            CombatantHealth victim,
            Team killerTeam,
            CombatantHealth lastAttacker = null,
            string weaponCause = null)
        {
            if (victim == null) return;

            var localPlayer = PlayerController.LocalPlayer;
            CombatantHealth localHealth = localPlayer != null ? localPlayer.Health : null;

            bool isLocalVictim = victim == localHealth;
            string victimName = FormatCombatantName(victim, isLocalVictim);

            CombatantHealth killer = lastAttacker;
            if (killer == null)
            {
                killer = FindRepresentativeEnemy(victim, killerTeam);
            }

            bool isLocalKiller = killer != null && killer == localHealth;
            string killerName = killer != null
                ? FormatCombatantName(killer, isLocalKiller)
                : (killerTeam == Team.TeamA ? "ORANGE TEAM" : "BLUE TEAM");

            WeaponKind kind = WeaponKind.Shooter;
            string weaponName = "Spritzer";
            bool isSub = false;
            bool isSpecial = false;

            if (!string.IsNullOrEmpty(weaponCause))
            {
                weaponName = weaponCause;
                if (weaponCause.IndexOf("Bomb", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isSub = true;
                }
                else if (weaponCause.IndexOf("Slam", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         weaponCause.IndexOf("Storm", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isSpecial = true;
                }
            }
            else if (killer != null)
            {
                var weaponCtrl = killer.GetComponent<WeaponController>();
                if (weaponCtrl != null)
                {
                    kind = weaponCtrl.CurrentWeaponKind;
                    if (weaponCtrl.Config != null && !string.IsNullOrEmpty(weaponCtrl.Config.displayName))
                    {
                        weaponName = weaponCtrl.Config.displayName;
                    }
                    else
                    {
                        weaponName = kind.ToString();
                    }
                }
            }

            var ev = new KillFeedEvent
            {
                KillerName = killerName,
                KillerTeam = killerTeam != Team.Neutral ? killerTeam : (victim.Team == Team.TeamA ? Team.TeamB : Team.TeamA),
                VictimName = victimName,
                VictimTeam = victim.Team,
                WeaponKind = kind,
                WeaponDisplayName = weaponName,
                IsSubBomb = isSub,
                IsSpecial = isSpecial,
                IsLocalPlayerKiller = isLocalKiller,
                IsLocalPlayerVictim = isLocalVictim,
                Timestamp = Time.time
            };

            ReportKill(ev);
        }

        private static string FormatCombatantName(CombatantHealth ch, bool isLocal)
        {
            if (isLocal) return "YOU";
            if (ch == null) return "UNKNOWN";

            string raw = ch.gameObject.name;
            if (raw.StartsWith("BotA_")) return "ALLY " + raw.Substring(5);
            if (raw.StartsWith("BotB_")) return "ENEMY " + raw.Substring(5);
            return raw.ToUpperInvariant();
        }

        private static CombatantHealth FindRepresentativeEnemy(CombatantHealth victim, Team enemyTeam)
        {
            var all = CombatantHealthRegistry.All;
            if (all == null) return null;

            Vector3 vPos = victim != null ? victim.transform.position : Vector3.zero;
            CombatantHealth closest = null;
            float closestDistSq = float.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == victim || c.Team != enemyTeam) continue;

                float d = (c.transform.position - vPos).sqrMagnitude;
                if (d < closestDistSq)
                {
                    closestDistSq = d;
                    closest = c;
                }
            }

            return closest;
        }

        /// <summary>单元测试或调试触发测试击杀卡片。</summary>
        public static void TriggerMockKill(Team killerTeam, Team victimTeam, WeaponKind weapon, string killer = null, string victim = null)
        {
            var ev = new KillFeedEvent
            {
                KillerName = killer ?? (killerTeam == Team.TeamA ? "YOU" : "OCTO BOT"),
                KillerTeam = killerTeam,
                VictimName = victim ?? (victimTeam == Team.TeamA ? "INKLING" : "TARGET BOT"),
                VictimTeam = victimTeam,
                WeaponKind = weapon,
                WeaponDisplayName = weapon.ToString(),
                IsLocalPlayerKiller = killerTeam == Team.TeamA && killer == null,
                IsLocalPlayerVictim = victimTeam == Team.TeamA && killer != null,
                Timestamp = Time.time
            };
            ReportKill(ev);
        }
    }
}
