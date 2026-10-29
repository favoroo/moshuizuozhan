using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Popper Blaster（爆破枪）爆裂结算：
    /// 当重弹直击目标、撞击掩体或飞行达到最大射程时在空中/接触面爆裂，
    /// 产生 2.6m 范围溅射（30~70 伤害按距离线性衰减，直击目标不重复受溅射伤害），并在地面涂抹爆斑。
    /// 纯静态逻辑，支持 EditMode 纯逻辑测试。
    /// </summary>
    public static class WeaponBlasterSystem
    {
        public static int Explode(
            Vector3 burstCenter,
            float splashRadius,
            float splashDamageMax,
            float splashDamageMin,
            float groundPaintRadius,
            Team attackerTeam,
            GameObject ownerRoot,
            CombatantHealth directHitTarget,
            Action<int> onCellsPainted)
        {
            splashRadius = Mathf.Max(0.1f, splashRadius);
            splashDamageMax = Mathf.Max(0f, splashDamageMax);
            splashDamageMin = Mathf.Max(0f, splashDamageMin);

            // P6-A：爆裂伤害同样携带归属者，供打击标记与击杀 Hitstop 使用
            CombatantHealth attacker = ownerRoot != null ? ownerRoot.GetComponent<CombatantHealth>() : null;

            // 1. 范围溅射伤害（跳过已受直击的目标）
            IReadOnlyList<CombatantHealth> all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(attackerTeam) || victim == directHitTarget)
                    continue;

                Vector3 chest = victim.transform.position + Vector3.up * 0.75f;
                float dist = Vector3.Distance(burstCenter, chest);
                if (dist > splashRadius)
                    continue;

                // 掩体阻挡检测：若射线被实体掩体阻挡且非目标自身，则免受溅射
                if (Physics.Linecast(burstCenter, chest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null && !hit.collider.transform.IsChildOf(victim.transform) &&
                        (ownerRoot == null || !hit.collider.transform.IsChildOf(ownerRoot.transform)))
                    {
                        continue;
                    }
                }

                float t = Mathf.Clamp01(dist / splashRadius);
                float damage = Mathf.Lerp(splashDamageMax, splashDamageMin, t);
                victim.TakeDamage(damage, attackerTeam, burstCenter, attacker);
            }

            // 2. 地面爆斑喷涂
            int painted = 0;
            Vector3 groundPoint = ProjectToGround(burstCenter, out Vector3 groundNormal);
            float paintRadius = Mathf.Max(0.1f, groundPaintRadius);

            if (InkSurfaceController.Instance != null)
            {
                painted = InkSurfaceController.Instance.PaintCircle(groundPoint, paintRadius, attackerTeam);
                if (painted > 0)
                {
                    onCellsPainted?.Invoke(painted);
                }
            }

            // 3. 视觉与音效反馈
            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(attackerTeam)
                : (attackerTeam == Team.TeamB ? new Color(0.15f, 0.42f, 1f) : new Color(1f, 0.44f, 0.06f));

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(groundPoint, groundNormal, teamColor, paintRadius, 6);
                VfxPool.Instance.SpawnShockwaveRing(burstCenter, teamColor, 0.4f, splashRadius, 0.25f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayInkHitGround(1.1f);
            }

            return painted;
        }

        private static Vector3 ProjectToGround(Vector3 origin, out Vector3 normal)
        {
            if (Physics.Raycast(origin + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                return hit.point;
            }

            normal = Vector3.up;
            return new Vector3(origin.x, 0f, origin.z);
        }
    }
}
