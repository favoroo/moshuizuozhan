using System.Collections.Generic;
using P1.Player;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 所有存活战斗单位（玩家 / Bot / 木桩）的全局注册表，避免每帧 FindObjectsByType 产生 GC。
    /// </summary>
    public static class CombatantHealthRegistry
    {
        private static readonly List<CombatantHealth> _all = new List<CombatantHealth>(16);

        public static IReadOnlyList<CombatantHealth> All => _all;

        public static void Register(CombatantHealth health)
        {
            if (health != null && !_all.Contains(health))
            {
                _all.Add(health);
            }
        }

        public static void Unregister(CombatantHealth health)
        {
            _all.Remove(health);
        }

        public static void Clear()
        {
            _all.Clear();
        }
    }
}
