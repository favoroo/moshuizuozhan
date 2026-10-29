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
