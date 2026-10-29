using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 弹道落点运行时验收：真实加载试玩场景，朝一个无遮挡的可涂地面点开火，
    /// 断言墨水最终覆盖准星点，且枪口到落点之间的滴墨没有断层。
    /// </summary>
    public class InkBallisticsPlayboxTests
    {
        const string SceneName = "PhaseA_Playbox";
        const float AimDistance = 4.5f;

        [UnityTest]
        public IEnumerator Projectile_LandsExactlyOnAimPoint_AndLeavesContinuousTrail()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");

            // 关闭输入与 Bot，排除其他子弹/移动对涂地结果的干扰
            player.enabled = false;
            foreach (var bot in UnityEngine.Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            {
                bot.enabled = false;
            }
            player.Motor.RefillInkFull();
            yield return null;

            Assert.IsTrue(TryFindClearAimPoint(player, out Vector3 aimPoint, out Vector3 aimDirection),
                "场景中应能找到一条无遮挡、平坦且可涂的 4.5m 瞄准射线");

            var surface = InkSurfaceController.Instance;
            Team team = player.Health.Team;
            Assert.AreEqual(Team.Neutral, surface.InkAt(aimPoint), "开火前瞄准点应是中性地面");

            Assert.IsTrue(player.Weapon.TryFireMainWeapon(aimDirection, aimPoint), "墨水充足时应能开火");

            // 等待弹丸飞行、地面接触爆斑与沿途滴墨完成
            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(team, surface.InkAt(aimPoint), "弹丸落点必须与准星地面点重合");

            Vector3 muzzle = FindMuzzlePosition(player);
            Vector3 flatMuzzle = new Vector3(muzzle.x, aimPoint.y, muzzle.z);
            float muzzleToAim = Vector3.Distance(flatMuzzle, aimPoint);
            Assert.Greater(muzzleToAim, 2.5f + 0.2f, "枪口到落点应超过首次滴墨距离，便于验证拖尾");

            Vector3 trailDir = (aimPoint - flatMuzzle).normalized;
            const int samples = 8;
            for (int i = 0; i <= samples; i++)
            {
                float distance = Mathf.Lerp(2.6f, muzzleToAim - 0.05f, i / (float)samples);
                Vector3 probe = flatMuzzle + trailDir * distance;
                Assert.AreEqual(team, surface.InkAt(probe),
                    $"距离枪口 {distance:0.00}m 的拖尾应连续无断层");
            }
        }

        private static bool TryFindClearAimPoint(PlayerController player, out Vector3 aimPoint, out Vector3 aimDirection)
        {
            Vector3 origin = player.transform.position + Vector3.up * 0.9f;
            float playerGroundY = player.transform.position.y;
            var grid = InkSurfaceController.Instance.Grid;

            for (int i = 0; i < 24; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;

                if (IsPathObstructed(player, origin, dir))
                    continue;

                Vector3 candidate = player.transform.position + dir * AimDistance;
                if (!Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 12f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (ground.normal.y < 0.85f)
                    continue;
                if (Mathf.Abs(ground.point.y - playerGroundY) > 1.2f)
                    continue;

                candidate.y = ground.point.y;
                if (grid.IsBlockedAt(new Vector2(candidate.x, candidate.z)))
                    continue;

                aimPoint = candidate;
                aimDirection = dir;
                return true;
            }

            aimPoint = default;
            aimDirection = Vector3.forward;
            return false;
        }

        private static bool IsPathObstructed(PlayerController player, Vector3 origin, Vector3 dir)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, AimDistance + 1.5f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform.IsChildOf(player.transform))
                    continue;
                if (hit.collider.GetComponentInParent<CombatantHealth>() != null)
                    continue;
                return true;
            }
            return false;
        }

        private static Vector3 FindMuzzlePosition(PlayerController player)
        {
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Muzzle")
                    return t.position;
            }
            return player.transform.position + Vector3.up * 0.75f;
        }
    }
}
