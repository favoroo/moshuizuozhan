using System.Collections.Generic;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 泼桶（Slosher）一次泼洒的 Volley 聚合上下文：
    /// 一次泼出的多枚墨滴共享同一个 Volley 实例，对同一目标最多只判定 1 次直击命中，
    /// 确保 2 桶稳定击杀，防止近战全中瞬秒。
    /// <para>
    /// 每波泼洒必须独立租用一个实例（见 <see cref="SlosherVolleyPool"/>）：
    /// 旧实现复用同一个实例并每波 <c>Clear()</c>，上一波还在飞的墨滴会被清空命中记录，
    /// 同一目标因此能连吃 70 + 34 + 34…（远距离单桶秒杀）。
    /// </para>
    /// </summary>
    public class SlosherVolley
    {
        private readonly HashSet<CombatantHealth> _hitTargets = new HashSet<CombatantHealth>();

        /// <summary>
        /// 本 Volley 归属那一波墨滴的最晚落地时刻（<see cref="Time.time"/>）。
        /// 到期之后才允许被对象池回收复用，保证在飞墨滴的命中记录不会被提前清空。
        /// </summary>
        public float ExpireAt { get; private set; } = float.NegativeInfinity;

        /// <summary>该 Volley 是否已过飞行寿命（可安全复用）。</summary>
        public bool IsExpired(float now)
        {
            return now >= ExpireAt;
        }

        /// <summary>开始一波新的泼洒：清空命中记录并设置本波寿命。</summary>
        public void BeginWave(float now, float lifetime)
        {
            _hitTargets.Clear();
            ExpireAt = now + Mathf.Max(0f, lifetime);
        }

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

        /// <summary>清空已命中目标记录，并把实例标记为可立即复用。</summary>
        public void Clear()
        {
            Reset();
        }

        public void Reset()
        {
            _hitTargets.Clear();
            ExpireAt = float.NegativeInfinity;
        }
    }

    /// <summary>
    /// 泼桶 Volley 的小型对象池：每波泼洒租用独立实例，按飞行寿命回收后再复用，
    /// 既避免每波 <c>new</c> 的持续 GC，也保证在飞墨滴的 Volley 不会被下一波提前清空。
    /// </summary>
    public sealed class SlosherVolleyPool
    {
        private readonly List<SlosherVolley> _pool = new List<SlosherVolley>(8);
        private int _cursor;

        /// <summary>池内实例总数（调试 / 测试可观测；正常对局稳定在个位数）。</summary>
        public int Count => _pool.Count;

        /// <summary>
        /// 租用一个可用于本波泼洒的 Volley：优先复用已过飞行寿命的实例，
        /// 全部仍在飞行时新建一个（保证在飞墨滴的命中记录绝不被打断）。
        /// </summary>
        public SlosherVolley Rent(float now, float lifetime)
        {
            int count = _pool.Count;
            for (int i = 0; i < count; i++)
            {
                int index = (_cursor + i) % count;
                SlosherVolley candidate = _pool[index];
                if (!candidate.IsExpired(now))
                    continue;

                _cursor = (index + 1) % count;
                candidate.BeginWave(now, lifetime);
                return candidate;
            }

            var created = new SlosherVolley();
            created.BeginWave(now, lifetime);
            _pool.Add(created);
            _cursor = 0;
            return created;
        }

        /// <summary>清空全部实例（重开 / 换武器 / 复活）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                _pool[i].Reset();
            }
        }
    }
}
