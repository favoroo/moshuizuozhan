using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// P3-B 大招与副武器的纯数值结算：伤害衰减曲线、范围伤害与风暴云 DOT。
    /// 不依赖场景/物理/对象池，EditMode 可直接断言（对齐 Inkwave 的 slam / bomb / storm 数值）。
    /// </summary>
    public static class SpecialCombat
    {
        /// <summary>外圈衰减的「台阶」权重：刚出核心圈时伤害保留 30% 的落差（对齐 Inkwave 0.3）。</summary>
        public const float DefaultOuterFalloffWeight = 0.3f;

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Tidal Slam 冲击波伤害：核心圈（innerRadius）内满伤 180；
        /// 核心圈外先按 outerFalloffWeight 抬一个台阶（默认 92.5），再平滑衰减到外缘 55。
        /// </summary>
        public static float SlamDamage(
            float distance,
            float innerRadius,
            float radius,
            float innerDamage,
            float outerDamage,
            float outerFalloffWeight = DefaultOuterFalloffWeight)
        {
            if (distance <= innerRadius)
                return innerDamage;
            if (distance > radius)
                return 0f;

            float span = Mathf.Max(0.0001f, radius - innerRadius);
            float t = Mathf.Clamp01(1f - (distance - innerRadius) / span);
            float shelf = Mathf.Lerp(outerDamage, innerDamage, Mathf.Clamp01(outerFalloffWeight));
            return Mathf.Lerp(outerDamage, shelf, t);
        }

        /// <summary>
        /// Splat Bomb 爆炸伤害：核心圈满伤 180，圈外按平方曲线衰减到外缘 35（Inkwave 的 k² 手感）。
        /// </summary>
        public static float BombDamage(
            float distance,
            float innerRadius,
            float radius,
            float innerDamage,
            float outerDamage)
        {
            if (distance <= innerRadius)
                return innerDamage;
            if (distance > radius)
                return 0f;

            float span = Mathf.Max(0.0001f, radius - innerRadius);
            float t = Mathf.Clamp01(1f - (distance - innerRadius) / span);
            return Mathf.Lerp(outerDamage, innerDamage, t * t);
        }

        /// <summary>风暴云单次 DOT 结算伤害 = DPS × 结算间隔（默认 34 × 0.5 = 17）。</summary>
        public static float StormTickDamage(float damagePerSecond, float tickInterval)
        {
            return Mathf.Max(0f, damagePerSecond) * Mathf.Max(0f, tickInterval);
        }

        /// <summary>风暴云一帧的漂移位移（水平面内，忽略 y）。</summary>
        public static Vector3 StormDriftStep(Vector3 cloudPos, Vector3 driftDirection, float driftSpeed, float dt)
        {
            Vector3 dir = new Vector3(driftDirection.x, 0f, driftDirection.z);
            if (dir.sqrMagnitude < 0.0001f || driftSpeed <= 0f || dt <= 0f)
                return cloudPos;

            return cloudPos + dir.normalized * (driftSpeed * dt);
        }

        /// <summary>
        /// 对全场敌方施加砸地冲击波伤害，返回命中人数。
        /// 走 <see cref="CombatantHealthRegistry"/>，不依赖碰撞体数量（避免 OverlapSphere 上限截断）。
        /// </summary>
        public static int ApplySlamDamage(
            Vector3 center,
            Team team,
            float radius,
            float innerRadius,
            float innerDamage,
            float outerDamage,
            float outerFalloffWeight = DefaultOuterFalloffWeight,
            CombatantHealth attacker = null)
        {
            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                float distance = HorizontalDistance(center, victim.transform.position);
                float damage = SlamDamage(distance, innerRadius, radius, innerDamage, outerDamage, outerFalloffWeight);
                if (damage <= 0f)
                    continue;

                victim.TakeDamage(damage, team, center, attacker);
                hits++;
            }
            return hits;
        }

        /// <summary>对爆炸范围内的敌方施加手雷伤害，返回命中人数。</summary>
        public static int ApplyBombDamage(
            Vector3 center,
            Team team,
            float radius,
            float innerRadius,
            float innerDamage,
            float outerDamage,
            CombatantHealth attacker = null)
        {
            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                float distance = HorizontalDistance(center, victim.transform.position);
                float damage = BombDamage(distance, innerRadius, radius, innerDamage, outerDamage);
                if (damage <= 0f)
                    continue;

                victim.TakeDamage(damage, team, center, attacker);
                hits++;
            }
            return hits;
        }

        /// <summary>
        /// 风暴云单次 DOT 结算：只命中所属队伍之外、位于云层下方且水平距离在半径内的战斗单位。
        /// </summary>
        public static int ApplyStormTick(
            Vector3 cloudCenter,
            Team team,
            float radius,
            float tickDamage,
            CombatantHealth attacker = null)
        {
            if (tickDamage <= 0f)
                return 0;

            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                Vector3 victimPos = victim.transform.position;
                if (victimPos.y > cloudCenter.y + 0.5f)
                    continue;
                if (HorizontalDistance(cloudCenter, victimPos) > radius)
                    continue;

                victim.TakeDamage(tickDamage, team, victimPos, attacker);
                hits++;
            }
            return hits;
        }
    }
}
