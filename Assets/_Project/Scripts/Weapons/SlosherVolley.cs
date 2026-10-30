using System.Collections.Generic;
using P1.Player;

namespace P1.Weapons
{
    /// <summary>
    /// 泼桶（Slosher）一次泼洒的 Volley 聚合上下文：
    /// 一次泼出的多枚墨滴共享同一个 Volley 实例，对同一目标最多只判定 1 次直击命中，
    /// 确保 2 桶稳定击杀，防止近战全中瞬秒。
    /// </summary>
    public class SlosherVolley
    {
        private readonly HashSet<CombatantHealth> _hitTargets = new HashSet<CombatantHealth>();

        /// <summary>
        /// 尝试向该 Volley 注册一次对目标的命中。
        /// </summary>
        /// <param name="target">被命中的敌人</param>
        /// <returns>若目标尚未受过本 Volley 命中则返回 true（生效）；若已命中过则返回 false（忽略后续多段）</returns>
        public bool TryRegisterHit(CombatantHealth target)
        {
            if (target == null)
                return false;

            return _hitTargets.Add(target);
        }

        public bool HasHit(CombatantHealth target)
        {
            return target != null && _hitTargets.Contains(target);
        }

        public int HitCount => _hitTargets.Count;

        /// <summary>清空已命中目标记录，供下一波泼洒复用。</summary>
        public void Clear()
        {
            _hitTargets.Clear();
        }

        public void Reset()
        {
            _hitTargets.Clear();
        }
    }
}
