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

        /// <summary>
        /// 冲击波允许的最大垂直落差（米）：砸地 / 手雷的判定是水平圆，
        /// 旧实现能穿过 3~4m 的集装箱夹层打到楼上一层或底下一层；
        /// 2.5m 覆盖身高与矮台阶，又挡得住「隔一层板」的穿透。需要跨层命中的玩法可显式传更大值。
        /// </summary>
        public const float DefaultMaxVerticalGap = 2.5f;

        /// <summary>风暴云允许的最大「云下」落差（米）：防止雨点穿过地面/集装箱底打到下一层。</summary>
        public const float DefaultStormMaxDropBelow = 8f;

        /// <summary>覆盖检查的射线起点高度（米）：INKWAVE 从胸口（脚底 +1.2m）向云底检查视线。</summary>
        public const float StormCoverChestHeight = 1.2f;

        /// <summary>覆盖检查射线终点相对云心的高度偏移（米）：负值 = 云腹下方一点，对齐 Inkwave cloudY - 0.6。</summary>
        public const float StormCoverBellyOffset = -0.6f;

        /// <summary>覆盖检查 LinecastNonAlloc 缓冲大小。</summary>
        private const int StormCoverHitsCapacity = 8;

        /// <summary>覆盖检查共享缓冲（static 方法共用，单线程调用无竞争）。</summary>
        private static readonly RaycastHit[] StormCoverHits = new RaycastHit[StormCoverHitsCapacity];

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
            CombatantHealth attacker = null,
            float maxVerticalGap = DefaultMaxVerticalGap)
        {
            // 终场哨响后不再结算伤害。
            if (!MatchManager.CombatSettlementAllowed)
                return 0;

            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                // 垂直落差限制：相隔一层高台/集装箱底的目标不吃冲击波（旧实现只有水平圆判定）。
                if (Mathf.Abs(victim.transform.position.y - center.y) > Mathf.Max(0.1f, maxVerticalGap))
                    continue;

                float distance = HorizontalDistance(center, victim.transform.position);
                float damage = SlamDamage(distance, innerRadius, radius, innerDamage, outerDamage, outerFalloffWeight);
                if (damage <= 0f)
                    continue;

                // P20：圈内目标处于无敌（出生保护/对方正在放大招）时伤害完整豁免——
                // 脚下闪白色小环提示「免疫」，否则玩家会以为判定漏了人。
                // 注意不能用 TakeDamage 返回值判断：它返回的是 killed（是否击杀），非致命伤也是 false。
                if (victim.IsInvulnerable)
                {
                    if (VfxPool.Instance != null)
                    {
                        Vector3 at = victim.transform.position;
                        VfxPool.Instance.SpawnShockwaveRing(
                            new Vector3(at.x, at.y + 0.05f, at.z), Color.white, 0.2f, 1.4f, 0.45f);
                    }
                    continue;
                }

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
            CombatantHealth attacker = null,
            float maxVerticalGap = DefaultMaxVerticalGap)
        {
            // 终场哨响后不再结算伤害。
            if (!MatchManager.CombatSettlementAllowed)
                return 0;

            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                // 垂直落差限制：手雷在楼下爆炸不再震到楼上一层（反之亦然）。
                if (Mathf.Abs(victim.transform.position.y - center.y) > Mathf.Max(0.1f, maxVerticalGap))
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
        /// 旧签名保留（不做掩体检查）——EditMode 纯逻辑测试与历史调用方继续走此入口。
        /// </summary>
        public static int ApplyStormTick(
            Vector3 cloudCenter,
            Team team,
            float radius,
            float tickDamage,
            CombatantHealth attacker = null,
            float maxDropBelow = DefaultStormMaxDropBelow)
        {
            return ApplyStormTick(cloudCenter, team, radius, tickDamage, attacker, maxDropBelow, false);
        }

        /// <summary>
        /// 带掩体检查的风暴云 DOT 结算（P41 对齐 Inkwave）：从受害者胸口向云腹做视线检查，
        /// 中途被世界碰撞体（集装箱/屋檐）挡住的目标淋不到雨。战斗单位自身胶囊被过滤——
        /// 雨可以穿过人群，但穿不过屋顶。
        /// </summary>
        public static int ApplyStormTick(
            Vector3 cloudCenter,
            Team team,
            float radius,
            float tickDamage,
            CombatantHealth attacker,
            float maxDropBelow,
            bool checkOverheadCover)
        {
            if (tickDamage <= 0f)
                return 0;

            // 终场哨响后不再结算伤害。
            if (!MatchManager.CombatSettlementAllowed)
                return 0;

            Vector3 bellyPoint = new Vector3(cloudCenter.x, cloudCenter.y + StormCoverBellyOffset, cloudCenter.z);
            int hits = 0;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;
                // 无敌单位不淋雨也不计数（对齐 Slam 的白圈语义；TakeDamage 本来也会拒绝）。
                if (victim.IsInvulnerable)
                    continue;

                Vector3 victimPos = victim.transform.position;
                if (victimPos.y > cloudCenter.y + 0.5f)
                    continue;
                // 雨水不会穿过地面/集装箱底：云下超过 8m 的目标视为在另一层。
                if (cloudCenter.y - victimPos.y > Mathf.Max(0.5f, maxDropBelow))
                    continue;
                if (HorizontalDistance(cloudCenter, victimPos) > radius)
                    continue;

                if (checkOverheadCover && IsStormCovered(victimPos, bellyPoint))
                    continue;

                victim.TakeDamage(tickDamage, team, victimPos, attacker);
                hits++;
            }
            return hits;
        }

        /// <summary>
        /// 胸口→云腹视线是否被头顶掩体（非战斗单位的世界碰撞体）挡住。
        /// 用 RaycastNonAlloc 沿「胸口→云腹」线段查询（Unity 没有 LinecastNonAlloc）：
        /// 起点在受害者自身胶囊内，queriesHitBackfaces=false 不会命中自身；途中胶囊命中按
        /// 「战斗单位」过滤 —— 雨穿过人群，不穿过屋顶。
        /// </summary>
        private static bool IsStormCovered(Vector3 victimPos, Vector3 bellyPoint)
        {
            Vector3 chest = new Vector3(victimPos.x, victimPos.y + StormCoverChestHeight, victimPos.z);
            Vector3 target = new Vector3(victimPos.x, bellyPoint.y, victimPos.z);
            Vector3 delta = target - chest;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return false;

            int count = Physics.RaycastNonAlloc(chest, delta / distance, StormCoverHits, distance, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = StormCoverHits[i].collider;
                // 战斗单位（自己/队友/敌人）的胶囊不算掩体：雨穿过人群，不穿过屋顶。
                if (hit != null && hit.GetComponentInParent<CombatantHealth>() == null)
                    return true;
            }
            return false;
        }
    }
}
