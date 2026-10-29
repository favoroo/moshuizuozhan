using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 战术纯函数集合（不依赖场景对象，便于 EditMode 直接断言）：
    /// 武器→交火距离映射、墨水地块→寻路代价映射、残血撤退判定。
    /// </summary>
    public static class BotTactics
    {
        /// <summary>
        /// 指向敌人的地面落点：沿 Bot「实际枪口方向」投射，而不是直接对着敌人。
        /// 这是拟人化的关键——打偏就是真的打偏，Bot 必须靠弹簧把准心拉回来。
        /// </summary>
        public static Vector3 AimGroundPoint(Vector3 selfPosition, Vector3 aimDirection, float distance, float targetGroundY)
        {
            Vector3 dir = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (dir.sqrMagnitude < 1e-5f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 point = selfPosition + dir * Mathf.Max(0.5f, distance);
            point.y = targetGroundY;
            return point;
        }

        /// <summary>理想交火距离：滚筒贴脸、小枪中距离、蓄力狙远距离。</summary>
        public static float PreferredEngageDistance(WeaponKind kind, WeaponConfigSO weapon, BotConfigSO bot)
        {
            float keep = bot != null ? bot.keepDistance : 4.2f;
            if (weapon == null) return keep;

            switch (kind)
            {
                case WeaponKind.Roller:
                    return bot != null ? Mathf.Max(0.4f, bot.rollerEngageDistance) : 0.9f;

                case WeaponKind.Charger:
                {
                    float fraction = bot != null ? Mathf.Clamp(bot.chargerEngageFraction, 0.3f, 1f) : 0.8f;
                    return Mathf.Max(4f, weapon.chargeRangeMax * fraction);
                }

                default:
                {
                    float fraction = bot != null ? Mathf.Clamp(bot.shooterEngageFraction, 0.3f, 1f) : 0.7f;
                    return Mathf.Max(1.5f, weapon.maxRange * fraction);
                }
            }
        }

        /// <summary>允许扣扳机的最远距离：超出这个距离只会浪费墨水。</summary>
        public static float MaxFireDistance(WeaponKind kind, WeaponConfigSO weapon, BotConfigSO bot)
        {
            if (weapon == null)
                return bot != null ? Mathf.Max(3f, bot.sightRange) : 8.5f;

            switch (kind)
            {
                case WeaponKind.Roller:
                    return bot != null ? Mathf.Max(1.5f, bot.rollerFireRange) : 5.5f;

                case WeaponKind.Charger:
                    // 满蓄力射程的 95%：留一点余量，避免激光正好停在目标身后
                    return Mathf.Max(4f, weapon.chargeRangeMax * 0.95f);

                default:
                    // 小枪允许略微超过标称射程，覆盖伤害衰减段
                    return Mathf.Max(2f, weapon.maxRange * 1.05f);
            }
        }

        /// <summary>索敌半径：至少要覆盖武器射程，否则蓄力狙永远看不到远处目标。</summary>
        public static float SightRange(WeaponKind kind, WeaponConfigSO weapon, BotConfigSO bot)
        {
            float baseSight = bot != null ? bot.sightRange : 8.5f;
            float margin = bot != null ? bot.weaponSightMargin : 2f;
            return Mathf.Max(baseSight, MaxFireDistance(kind, weapon, bot) + margin);
        }

        /// <summary>弹道提前量（秒）：命中移动目标所需的飞行时间；蓄力狙是即时光束，不需要提前量。</summary>
        public static float AimLeadSeconds(WeaponKind kind, WeaponConfigSO weapon, float distance)
        {
            if (weapon == null || kind == WeaponKind.Charger || kind == WeaponKind.Roller)
                return 0f;

            float speed = Mathf.Max(1f, weapon.projectileSpeed);
            return Mathf.Clamp(distance / speed, 0f, 0.35f);
        }

        /// <summary>
        /// 墨水加权：己方墨道 0.5（高速切入）、中性 1.0、敌方墨水 3.0（陷阱绕行）。
        /// </summary>
        public static float ResolveInkPathCost(Team cellTeam, Team agentTeam, float ownCost, float neutralCost, float enemyCost)
        {
            if (cellTeam == Team.Neutral) return neutralCost;
            return cellTeam == agentTeam ? ownCost : enemyCost;
        }

        /// <summary>
        /// 开火角度门槛：目标在屏幕上的张角（0.6m 半身高 / 距离）决定容差，
        /// 已经开火时放宽容差，避免一帧抖动就断火。
        /// </summary>
        public static float AimToleranceDegrees(float distance, BotConfigSO bot, bool alreadyFiring)
        {
            float baseTolerance = bot != null ? Mathf.Max(0.5f, bot.aimToleranceDegrees) : 9f;
            float growing = bot != null ? Mathf.Max(1f, bot.aimToleranceGrowingScale) : 1.6f;

            float angularRadius = Mathf.Atan2(0.6f, Mathf.Max(0.5f, distance)) * Mathf.Rad2Deg;
            float tolerance = Mathf.Max(angularRadius, baseTolerance * 0.35f);
            return alreadyFiring ? tolerance * growing : tolerance;
        }

        /// <summary>残血撤退判定：血量低于阈值、威胁足够近、且不在撤退冷却中。</summary>
        public static bool ShouldRetreat(float health, float threatDistance, BotConfigSO bot, float cooldownRemaining)
        {
            if (bot == null) return health < 40f;
            return cooldownRemaining <= 0f
                   && health < bot.retreatHealthThreshold
                   && threatDistance <= bot.retreatTriggerRange;
        }
    }
}
