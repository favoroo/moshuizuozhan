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

            // 武器选择是持久化的（PlayerPrefs）：本地玩家可能带着蓄力狙/加特林等武器进场
            //（蓄力狙会在等待帧松弦打出 1.2m 间隔的墨线，污染拖尾断言）。
            // 本用例验收小枪拖尾语义，显式装一把零散布小枪保证确定性。
            var shooterConfig = ScriptableObject.CreateInstance<WeaponConfigSO>();
            shooterConfig.weaponKind = WeaponKind.Shooter;
            shooterConfig.fireInterval = 0.1f;
            shooterConfig.damage = 36f;
            shooterConfig.projectileSpeed = 34f;
            shooterConfig.projectileGravity = 28f;
            shooterConfig.maxRange = 12f;
            shooterConfig.spreadMinDegrees = 0f;
            shooterConfig.spreadDegrees = 0f;
            shooterConfig.trailStartDistance = 2.5f;
            shooterConfig.trailDropDistance = 1.05f;
            shooterConfig.trailSplatRadius = 0.44f;
            var shooterWeapon = player.Weapon;
            shooterWeapon.EquipWeapon(shooterConfig);
            shooterWeapon.ResetAllWeaponState();
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
            // 失败信息带整条拖尾的 0.1m 步距采样（# = 己方墨，. = 中性）与横向 ±0.3m 对照，
            // 拖尾断层一眼定位：是滴墨没画、还是画偏了线
            var map = new System.Text.StringBuilder();
            for (float d = 2.0f; d <= 5.2f; d += 0.1f)
            {
                Vector3 probe = flatMuzzle + trailDir * d;
                map.Append(surface.InkAt(probe) == team ? '#' : '.');
            }
            var lateral = new System.Text.StringBuilder();
            for (float d = 2.4f; d <= 3.6f; d += 0.2f)
            {
                Vector3 probe = flatMuzzle + trailDir * d;
                Vector3 side = Vector3.Cross(Vector3.up, trailDir);
                float near = Mathf.Max(
                    surface.InkAt(probe - side * 0.3f) == team ? 1f : 0f,
                    surface.InkAt(probe + side * 0.3f) == team ? 1f : 0f);
                lateral.Append(near > 0f ? '#' : '.');
            }
            const int samples = 8;
            for (int i = 0; i <= samples; i++)
            {
                float distance = Mathf.Lerp(2.6f, muzzleToAim - 0.05f, i / (float)samples);
                Vector3 probe = flatMuzzle + trailDir * distance;
                Assert.AreEqual(team, surface.InkAt(probe),
                    $"距离枪口 {distance:0.00}m 的拖尾应连续无断层 | m2a={muzzleToAim:0.00} " +
                    $"map[2.0→5.2]={map} lateral±0.3[2.4→3.6]={lateral}");
            }
        }

        /// <summary>
        /// 爆破枪空爆落点验收：8.5m 外的准星点必须被爆裂涂到，且爆点不能提前落在近处地面。
        /// 回归「弹丸半径被拿去扫世界几何」的 bug：0.26~0.38m 的球体下沿会在下压弹道上
        /// 提前「半径 / tan(俯角)」米贴地（旧实现在枪口前约 5m 就爆，8m 外永远打不到）。
        /// </summary>
        [UnityTest]
        public IEnumerator Blaster_BurstsAtAimPoint_InsteadOfPoppingOnNearGround()
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

            // 临时爆破枪配置：零散布 + 关闭沿途滴墨，让墨迹只可能来自爆裂本身
            const float aimDistance = 8.5f;
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = WeaponKind.Blaster;
            config.fireInterval = 0.78f;
            config.damage = 125f;
            config.inkCostPerShot = 9f;
            config.projectileSpeed = 23f;
            config.projectileGravity = 0f;
            config.maxRange = 10.5f;
            config.spreadMinDegrees = 0f;
            config.spreadDegrees = 0f;
            config.blasterSplashRadius = 2.6f;
            config.blasterSplashDamageMax = 70f;
            config.blasterSplashDamageMin = 30f;
            config.blasterBurstRadius = 1.9f;
            config.blasterHitRadius = 0.38f;
            config.endSplatRadius = 1.5f;
            config.endSplatRadiusMin = 1.3f;
            config.endSplatRadiusMax = 1.7f;
            config.trailStartDistance = 999f;
            config.trailDropDistance = 999f;

            var weapon = player.Weapon;
            weapon.EquipWeapon(config);
            weapon.ResetAllWeaponState();
            player.Motor.RefillInkFull();
            yield return null;

            var host = (IWeaponRuntimeHost)weapon;
            Vector3 muzzle = host.MuzzlePosition;
            Assert.IsTrue(TryFindClearShotPoint(player, muzzle, aimDistance, out Vector3 aimPoint),
                $"{aimDistance}m 内应能找到一条无遮挡、平坦且可涂的枪口→落点弹道");

            Team team = player.Health.Team;
            Assert.AreEqual(Team.Neutral, surface.InkAt(aimPoint), "开火前瞄准点应是中性地面");

            Assert.IsTrue(weapon.TryFireMainWeapon(aimPoint - muzzle, aimPoint), "墨水充足时应能开火");
            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(team, surface.InkAt(aimPoint),
                "爆破弹必须在准星落点爆裂：命中球半径只接角色，撞地走细射线，不得提前贴地");
            Vector3 nearProbe = muzzle + (aimPoint - muzzle).normalized * (aimDistance - 3.2f);
            nearProbe.y = aimPoint.y;
            Assert.AreEqual(Team.Neutral, surface.InkAt(nearProbe),
                "枪口前方近处不应出现爆斑（说明爆点在途中提前触发）");

            Object.Destroy(config);
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

            // 武器选择是持久化的：本地玩家当前可能装备蓄力类武器（Splatling 首拉扳机只开始蓄力、
            // 按设计返回 false）。这里显式装一把连发枪，让「朝天能开火」只验证瞄准与弹道，
            // 不依赖上次选中的武器。
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = WeaponKind.Shooter;
            player.Weapon.EquipWeapon(config);
            player.Weapon.ResetAllWeaponState();
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

        /// <summary>
        /// 找一条「枪口 → 落点」真正无遮挡的瞄准点（爆破枪回归用）：
        /// 与平射探测不同，这里按实际弹道高度整段验证，避免低矮掩体造成假阳性。
        /// </summary>
        private static bool TryFindClearShotPoint(PlayerController player, Vector3 muzzle, float distance, out Vector3 aimPoint)
        {
            var grid = InkSurfaceController.Instance.Grid;
            float playerGroundY = player.transform.position.y;

            for (int i = 0; i < 36; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 10f, 0f) * Vector3.forward;
                Vector3 candidate = player.transform.position + dir * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 12f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (ground.normal.y < 0.85f || Mathf.Abs(ground.point.y - playerGroundY) > 1.2f)
                    continue;

                candidate.y = ground.point.y;
                if (grid.IsBlockedAt(new Vector2(candidate.x, candidate.z)))
                    continue;

                Vector3 toPoint = candidate - muzzle;
                float length = toPoint.magnitude;
                if (length < 1f)
                    continue;

                RaycastHit[] hits = Physics.RaycastAll(muzzle, toPoint / length, length - 0.05f, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider == null)
                        continue;
                    if (hit.collider.transform.IsChildOf(player.transform))
                        continue;
                    if (hit.collider.GetComponentInParent<CombatantHealth>() != null)
                        continue;
                    blocked = true;
                    break;
                }

                if (blocked)
                    continue;

                aimPoint = candidate;
                return true;
            }

            aimPoint = default;
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
