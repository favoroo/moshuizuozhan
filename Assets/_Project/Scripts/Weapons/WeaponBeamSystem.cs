using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>蓄力狙光束结算结果，供表现层绘制光柱。</summary>
    public struct BeamResult
    {
        public Vector3 Origin;
        public Vector3 End;
        public Vector3 Normal;
        public bool HitWorld;
    }

    /// <summary>
    /// 蓄力狙的瞬时贯穿光束：沿瞄准方向 raycast 到墙面/地面，贯穿伤害路径上的所有敌人，
    /// 并沿光束在地面铺一条笔直墨线、在终点留下撞击墨斑与溅射粒子。
    /// 静态无状态，EditMode 可直接构造敌人做秒杀断言。
    /// </summary>
    public static class WeaponBeamSystem
    {
        private static readonly RaycastHit[] RaycastBuffer = new RaycastHit[16];
        private static readonly RaycastHit[] GroundBuffer = new RaycastHit[8];

        public static BeamResult Fire(WeaponBeam beam, Team team, GameObject ownerRoot, Action<int> onCellsPainted)
        {
            Vector3 direction = beam.Direction.sqrMagnitude > 0.0001f ? beam.Direction.normalized : Vector3.forward;
            Vector3 origin = beam.Origin;
            float maxRange = Mathf.Max(0.1f, beam.Range);

            // 1. 世界遮挡：墙面 / 掩体 / 地面（跳过自己与所有战斗单位，伤害另外按线段判定以支持贯穿）
            bool hitWorld = TryFindWorldHit(origin, direction, maxRange, ownerRoot, out RaycastHit worldHit);
            float worldDistance = hitWorld ? worldHit.distance : maxRange;
            Vector3 worldNormal = hitWorld
                ? (worldHit.normal.sqrMagnitude > 0.0001f ? worldHit.normal : -direction)
                : -direction;
            Collider worldCollider = hitWorld ? worldHit.collider : null;

            Vector3 end = origin + direction * worldDistance;
            int painted = 0;
            CombatantHealth attacker = ownerRoot != null ? ownerRoot.GetComponent<CombatantHealth>() : null;

            // 2. 贯穿伤害：路径上所有敌人（到光束线段的距离在身体容差内）
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                Vector3 chest = victim.transform.position + Vector3.up * 0.75f;
                float along = Vector3.Dot(chest - origin, direction);
                if (along < 0f || along > worldDistance + 0.3f)
                    continue;

                Vector3 closest = origin + direction * along;
                if ((chest - closest).sqrMagnitude > 0.55f * 0.55f)
                    continue;

                victim.TakeDamage(beam.Damage, team, chest, attacker);

                if (!beam.Pierce)
                {
                    worldDistance = Mathf.Max(0.1f, along);
                    end = origin + direction * worldDistance;
                    break;
                }
            }

            // 3. 地面笔直墨线 + 终点撞击墨斑
            Vector3 impactPoint = ProjectToGround(end, ownerRoot, out Vector3 impactNormal);
            if (hitWorld)
            {
                impactPoint = end;
                impactNormal = worldNormal;
            }

            if (InkSurfaceController.Instance != null)
            {
                float step = Mathf.Max(0.2f, beam.PaintStep);
                float lineRadius = Mathf.Max(0.05f, beam.PaintRadius);
                for (float s = 1.0f; s < worldDistance - 0.2f; s += step)
                {
                    Vector3 probe = origin + direction * s;
                    painted += InkSurfaceController.Instance.PaintCircle(
                        ProjectToGround(probe, ownerRoot), lineRadius, team);
                }

                float impactRadius = Mathf.Max(0.05f, beam.ImpactRadius);
                painted += InkSurfaceController.Instance.PaintCircle(impactPoint, impactRadius, team);

                if (hitWorld && worldCollider != null)
                {
                    InkWallSurface wall = InkWallSurface.FindForCollider(worldCollider);
                    if (wall != null && worldNormal.y <= 0.55f)
                    {
                        painted += wall.PaintAtWorld(end, worldNormal, impactRadius, team);
                    }
                }
            }

            SpawnImpactVfx(impactPoint, impactNormal, team, Mathf.Max(0.05f, beam.ImpactRadius));

            if (painted > 0)
                onCellsPainted?.Invoke(painted);

            return new BeamResult
            {
                Origin = origin,
                End = end,
                Normal = worldNormal,
                HitWorld = hitWorld
            };
        }

        /// <summary>
        /// 沿射线找最近的“世界”命中（墙 / 掩体 / 地面），跳过自己与所有战斗单位。
        /// 蓄力狙的激光指示线与实际光束共用同一遮挡判定，保证“看到哪就打到哪”。
        /// </summary>
        public static bool TryFindWorldHit(
            Vector3 origin,
            Vector3 direction,
            float range,
            GameObject ownerRoot,
            out RaycastHit bestHit)
        {
            bestHit = default;
            if (range <= 0.001f || direction.sqrMagnitude < 0.0001f)
                return false;

            int count = Physics.RaycastNonAlloc(
                origin, direction.normalized, RaycastBuffer, range, ~0, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = RaycastBuffer[i];
                if (hit.collider == null || IsOwnCollider(hit.collider, ownerRoot))
                    continue;
                if (hit.collider.GetComponentInParent<CombatantHealth>() != null)
                    continue;
                if (hit.distance >= closest)
                    continue;

                closest = hit.distance;
                bestHit = hit;
                found = true;
            }

            return found;
        }

        private static void SpawnImpactVfx(Vector3 point, Vector3 normal, Team team, float radius)
        {
            Color color = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(team)
                : (team == Team.TeamB ? new Color(0.15f, 0.42f, 1f) : new Color(1f, 0.44f, 0.06f));

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(point, normal, color, radius, 6);
                VfxPool.Instance.SpawnShockwaveRing(point, color, 0.12f, radius * 0.8f, 0.2f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayInkHitGround(1.1f);
            }
        }

        /// <summary>把空间点垂直投影到地面；跳过自己与战斗单位，避免墨线画在敌人头顶。</summary>
        private static Vector3 ProjectToGround(Vector3 worldPos, GameObject ownerRoot)
        {
            return ProjectToGround(worldPos, ownerRoot, out _);
        }

        private static Vector3 ProjectToGround(Vector3 worldPos, GameObject ownerRoot, out Vector3 normal)
        {
            normal = Vector3.up;
            Vector3 rayOrigin = worldPos + Vector3.up * 3f;
            int count = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, GroundBuffer, 8f, ~0, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            Vector3 point = new Vector3(worldPos.x, 0f, worldPos.z);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = GroundBuffer[i];
                if (hit.collider == null || IsOwnCollider(hit.collider, ownerRoot))
                    continue;
                if (hit.collider.GetComponentInParent<CombatantHealth>() != null)
                    continue;
                if (hit.distance < closest)
                {
                    closest = hit.distance;
                    point = hit.point;
                    normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
                }
            }
            return point;
        }

        private static bool IsOwnCollider(Collider collider, GameObject ownerRoot)
        {
            return ownerRoot != null && collider.transform.IsChildOf(ownerRoot.transform);
        }
    }
}
