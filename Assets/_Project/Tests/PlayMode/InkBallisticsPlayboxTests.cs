using System.Collections;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
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

        /// <summary>
        /// 抬头验收：把视角抬到天上后，本地玩家的瞄准方向必须是 3D 向上的相机前向，
        /// 手持武器模型也要跟着抬起（而不是被压平成水平平射）。
        /// </summary>
        [UnityTest]
        public IEnumerator LookingUp_MakesAimDirectionAndWeaponPosePointUpward()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            var cam = TopDownCameraFollow.Instance;
            Assert.IsNotNull(cam, "场景里应存在 TopDownCameraFollow");

            // 开局停在 Ready 阶段，测试里手动开局并压缩倒计时（只有 Playing 阶段玩家 Update 才跑瞄准管线）
            MatchManager match = MatchManager.Instance;
            Assert.IsNotNull(match, "场景里应存在 MatchManager");
            PlayModeTestKit.ShortenMatchTimers(0.20f);
            if (match.CurrentPhase == MatchManager.Phase.Ready)
            {
                match.StartMatch();
            }
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 5f);
            Assert.IsTrue(MatchManager.InputAllowed, "进入 Playing 后应允许输入");

            // pitch 正 = 低头，因此传负增量就是抬头
            cam.AddLookInput(0f, -55f);
            yield return null;
            yield return null;

            Assert.Less(cam.PitchDegrees, -40f, "视角应确实抬起来了");
            Assert.Greater(player.AimDirection.y, 0.6f, "抬头后瞄准方向必须朝上（含俯仰的 3D 方向）");
            Assert.Greater(Vector3.Dot(player.AimDirection, cam.LookForward), 0.99f, "瞄准方向应与相机前向一致");

            CharacterVisualController visual = player.Motor != null ? player.Motor.VisualController : null;
            Assert.IsNotNull(visual, "角色应有程序化视觉控制器");
            Assert.IsNotNull(visual.WeaponRoot, "角色应有武器根节点");

            yield return new WaitForSeconds(0.3f);

            Transform activeModel = null;
            Transform weaponRoot = visual.WeaponRoot.transform;
            for (int i = 0; i < weaponRoot.childCount; i++)
            {
                if (weaponRoot.GetChild(i).gameObject.activeSelf)
                {
                    activeModel = weaponRoot.GetChild(i);
                    break;
                }
            }

            Assert.IsNotNull(activeModel, "应能找到当前激活的武器模型");
            float weaponPitch = Mathf.DeltaAngle(0f, activeModel.localEulerAngles.x);
            Assert.Greater(Mathf.Abs(weaponPitch), 15f, "抬头时武器模型应随视角俯仰，而不是保持水平");

            player.Motor.RefillInkFull();
            Assert.IsTrue(player.Weapon.TryFireMainWeapon(player.AimDirection, player.AimPoint),
                "朝天也应能正常开火");
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>
        /// 朝天射击验收：射程（水平距离）耗尽后墨滴必须真正落回地面并在落点留墨，
        /// 而不是在最大射程处凭空消失。关闭散布与沿线滴墨，确保地面墨迹只可能来自落滴本身。
        /// </summary>
        [UnityTest]
        public IEnumerator SkyShot_FallsBackAndPaintsOnTheGround()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景里应存在本地玩家 PlayerController");
            var surface = InkSurfaceController.Instance;
            Assert.IsNotNull(surface, "场景里应存在 InkSurfaceController");

            player.enabled = false;
            foreach (var bot in UnityEngine.Object.FindObjectsByType<BotController>(FindObjectsSortMode.None))
            {
                bot.enabled = false;
            }

            // 临时配置：零散布 + 关闭沿途滴墨，排除其它墨迹来源；
            // 射程/弹速钉死为 7.2m / 21m/s，让落点扇区断言独立于默认小枪的平衡数值。
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.maxRange = 7.2f;
            config.projectileSpeed = 21f;
            config.spreadMinDegrees = 0f;
            config.spreadDegrees = 0f;
            config.trailStartDistance = 999f;
            config.trailDropDistance = 999f;

            var weapon = player.Weapon;
            weapon.EquipWeapon(config);
            weapon.ResetAllWeaponState();
            player.Motor.RefillInkFull();

            Assert.IsTrue(TryFindOpenSkyLane(player, out Vector3 flatDir),
                "应能找到一条 12m 无遮挡且落点区域可涂的朝天射击方向");

            var host = (IWeaponRuntimeHost)weapon;
            Vector3 muzzle = host.MuzzlePosition;
            const float pitchDegrees = 60f;
            Vector3 aimDir = (flatDir * Mathf.Cos(pitchDegrees * Mathf.Deg2Rad)
                              + Vector3.up * Mathf.Sin(pitchDegrees * Mathf.Deg2Rad)).normalized;
            Vector3 skyPoint = muzzle + aimDir * 70f;

            Team team = player.Health.Team;
            Assert.IsTrue(weapon.TryFireMainWeapon(aimDir, skyPoint), "墨水充足时应能朝天开火");

            // 上升 → 水平射程耗尽 → 自由落体 → 落地，留出足够余量
            yield return new WaitForSeconds(2.2f);

            // 落点理论值 ≈ 水平射程 7.2m + 落体前冲；在 6.5~9.5m 扇区内采样
            Vector3 basePoint = new Vector3(muzzle.x, 0f, muzzle.z);
            var grid = surface.Grid;
            int painted = 0;
            for (int i = 0; i < 21; i++)
            {
                float distance = Mathf.Lerp(6.5f, 9.5f, (i % 7) / 6f);
                float angle = (i / 7 - 1) * 18f;
                Vector3 probe = basePoint + Quaternion.Euler(0f, angle, 0f) * flatDir * distance;
                if (grid.IsBlockedAt(new Vector2(probe.x, probe.z)))
                    continue;
                if (surface.InkAt(probe) == team)
                    painted++;
            }

            Assert.GreaterOrEqual(painted, 1,
                "朝天射击的墨滴应落回地面，并在最大水平射程附近留下墨迹");

            Object.Destroy(config);
        }

        /// <summary>
        /// 找一条前方 12m 无遮挡、且 6.5~9.5m 落点带平坦可涂的射击方向（避免弹丸提前撞墙）。
        /// </summary>
        private static bool TryFindOpenSkyLane(PlayerController player, out Vector3 flatDir)
        {
            Vector3 origin = player.transform.position + Vector3.up * 1f;
            var grid = InkSurfaceController.Instance.Grid;

            for (int i = 0; i < 24; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;

                if (IsPathObstructed(player, origin, dir, 12f))
                    continue;

                bool landingClear = true;
                for (int s = 0; s < 4; s++)
                {
                    Vector3 probe = player.transform.position + dir * (6.5f + s);
                    if (grid.IsBlockedAt(new Vector2(probe.x, probe.z))
                        || !Physics.Raycast(probe + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 6f, ~0, QueryTriggerInteraction.Ignore)
                        || ground.normal.y < 0.85f)
                    {
                        landingClear = false;
                        break;
                    }
                }

                if (!landingClear)
                    continue;

                flatDir = dir;
                return true;
            }

            flatDir = Vector3.forward;
            return false;
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

        private static bool IsPathObstructed(PlayerController player, Vector3 origin, Vector3 dir, float distance = AimDistance + 1.5f)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, ~0, QueryTriggerInteraction.Ignore);
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
