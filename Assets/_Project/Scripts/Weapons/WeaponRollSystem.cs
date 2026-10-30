using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 滚筒推地结算：把一条 <see cref="RollSweep"/> 落成“1.9m 宽、边缘平整”的墨带，并对滚筒前方
    /// 矩形范围内的敌人造成碾压伤害（同一目标受击间隔由 <see cref="RollSweep.HitInterval"/> 限制）。
    /// 纯静态、无状态，便于 EditMode 直接断言伤害与涂地参数。
    /// </summary>
    public static class WeaponRollSystem
    {
        /// <summary>执行一次清扫，返回本次新涂/改写的格子数。</summary>
        /// <param name="attacker">造成本次碾压的角色（P6-A 打击标记/击杀 Hitstop 归属；旧调用可省略）。</param>
        public static int Apply(
            RollSweep sweep,
            Team team,
            Dictionary<CombatantHealth, float> hitTimes,
            float now,
            CombatantHealth attacker = null)
        {
            int painted = PaintBand(sweep, team);
            DealDamage(sweep, team, hitTimes, now, attacker);
            return painted;
        }

        private static int PaintBand(RollSweep sweep, Team team)
        {
            if (InkSurfaceController.Instance == null)
                return 0;

            Vector3 forward = Flat(sweep.Forward);
            Vector3 from = sweep.From;
            Vector3 to = sweep.To;
            float distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            if (distance < 0.001f)
                return 0;

            float radius = Mathf.Max(0.1f, sweep.PaintRadius);
            float step = Mathf.Max(0.08f, radius * 0.9f);

            // 横向圆心止于 ±(W/2 - r)，这样圆并集的外包络正好等于 rollWidth，推地边缘保持笔直。
            float halfSpan = Mathf.Max(0f, sweep.Width * 0.5f - radius);
            int across = Mathf.Max(1, Mathf.CeilToInt((halfSpan * 2f) / step) + 1);
            int along = Mathf.Max(1, Mathf.CeilToInt(distance / step));

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();

            int changed = 0;
            for (int a = 1; a <= along; a++)
            {
                Vector3 center = Vector3.Lerp(from, to, a / (float)along);
                for (int c = 0; c < across; c++)
                {
                    float offset = across == 1 ? 0f : Mathf.Lerp(-halfSpan, halfSpan, c / (float)(across - 1));
                    // 推地每帧要铺十几枚圆斑：中间印记只画平滑主体、不各自刷涟漪，
                    // 否则 8 槽波纹缓冲会被一条墨带瞬间冲干净（旧实现里地面涟漪等于没有）。
                    changed += InkSurfaceController.Instance.PaintCircle(center + right * offset, radius, team, false, false);
                }
            }

            // 整条墨带只在扫过处发一条合并涟漪
            if (changed > 0)
            {
                InkSurfaceController.Instance.TriggerRipple(to + forward * 0.2f, 0.045f, 0.34f, 3.4f, 0.9f);
            }

            // 滚筒推抵墙面时同时给接触立面下沿涂上墨水
            if (InkWallSurface.All.Count > 0)
            {
                changed += InkWallSurface.PaintAllInSphere(to + forward * 0.35f, radius * 1.5f, team);
            }

            return changed;
        }

        private static void DealDamage(
            RollSweep sweep,
            Team team,
            Dictionary<CombatantHealth, float> hitTimes,
            float now,
            CombatantHealth attacker = null)
        {
            Vector3 forward = Flat(sweep.Forward);
            Vector3 origin = sweep.To;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();

            float halfWidth = sweep.Width * 0.5f;
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth victim = all[i];
                if (victim == null || !victim.IsAlive || !victim.Team.IsOpponentOf(team))
                    continue;

                Vector3 delta = victim.transform.position - origin;
                delta.y = 0f;
                float along = Vector3.Dot(delta, forward);
                float lateral = Mathf.Abs(Vector3.Dot(delta, right));
                float heightDelta = Mathf.Abs(victim.transform.position.y - origin.y);

                // 滚筒本体在身前 0~ForwardReach 的一段矩形内碾压，横向容差额外加半个身位。
                if (along < -0.2f || along > sweep.ForwardReach ||
                    lateral > halfWidth + 0.35f || heightDelta > 1.2f)
                    continue;

                if (hitTimes != null)
                {
                    if (hitTimes.TryGetValue(victim, out float lastHit) && now - lastHit < sweep.HitInterval)
                        continue;
                    hitTimes[victim] = now;
                }

                victim.TakeDamage(sweep.Damage, team, victim.transform.position, attacker);
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
