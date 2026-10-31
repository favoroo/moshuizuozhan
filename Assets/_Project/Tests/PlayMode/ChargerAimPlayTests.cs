using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 蓄力狙「松手帧必须使用本帧准星」的运行时回归：
    /// 旧实现 _lastAim 只在按住扳机的帧更新，松手帧 PlayerController 不再调用 TryFireMainWeapon，
    /// 光束会慢一帧——甩枪时 27m 外可差米级，表现为「狙击枪瞄这里打那里」。
    /// 本用例在松手帧之前注入 20° 转向，验证光束终点贴近新准星而不是上一帧旧准星。
    /// </summary>
    [Category("UI")]
    public class ChargerAimPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        [UnityTest]
        public IEnumerator ChargerReleaseFrame_UsesCurrentAim_AfterFlick()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            var player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "场景中应存在本地玩家");
            var follow = TopDownCameraFollow.Instance;
            Assert.IsNotNull(follow, "场景中应存在第三人称相机");
            var match = MatchManager.Instance != null
                ? MatchManager.Instance
                : Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(match, "场景中应存在 MatchManager");

            PlayModeTestKit.ShortenMatchTimers(0.1f);
            if (match.CurrentPhase == MatchManager.Phase.Ready)
                match.StartMatch();
            yield return PlayModeTestKit.WaitForPhase(match, MatchManager.Phase.Playing, 5f);
            yield return null;
            yield return null;

            foreach (var bot in Object.FindObjectsByType<BotController>(FindObjectsInactive.Exclude))
            {
                bot.enabled = false;
            }

            Assert.IsTrue(TryFindOpenGroundLane(player, out Vector3 flatDir),
                "出生点附近应能找到一条 12m 无遮挡、8m 处有平地的射击方向");

            // 把镜头转到这条空地上，并向下压 10°：准星落在约 8m 处的地面，满蓄射程内。
            float desiredYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
            follow.AddLookInput(
                Mathf.DeltaAngle(follow.YawDegrees, desiredYaw),
                10f - follow.PitchDegrees);
            yield return null;
            yield return null;

            Assert.Less(Vector3.Distance(player.AimPoint, player.transform.position), 27f,
                "测试准星点应落在蓄力狙满蓄射程内");

            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = WeaponKind.Charger;
            config.displayName = "Test Charger";
            config.chargeTime = 0.2f;
            config.chargeRangeMin = 11f;
            config.chargeRangeMax = 27f;
            config.maxRange = 27f;
            config.chargeInkCostFull = 18f;
            config.chargeDamageMin = 40f;
            config.chargeDamageMax = 160f;
            config.chargeReleaseCooldown = 0.05f;
            config.spreadMinDegrees = 0f;
            config.spreadDegrees = 0f;

            var weapon = player.Weapon;
            weapon.EquipWeapon(config);
            weapon.ResetAllWeaponState();
            player.Motor.RefillInkFull();
            yield return null;

            // 像 PlayerController 一样每帧注入扳机，保持蓄力直到满蓄。
            float holdUntil = Time.time + 0.6f;
            while (Time.time < holdUntil)
            {
                weapon.TryFireMainWeapon(player.AimDirection, player.AimPoint);
                yield return null;
            }

            Assert.IsTrue(weapon.IsCharging, "应处于蓄力中");
            Assert.Greater(weapon.Charge01, 0.95f, "蓄力应接近满值");
            Vector3 oldAim = player.AimPoint;
            int shotsBefore = weapon.ShotsFired;

            // 在松手帧的 WeaponController.LateUpdate 之前，把准星转到 20° 外并立即刷新瞄准点。
            // 等价于玩家在松手这一帧移动了视角：旧实现 _lastAim 仍是上一帧旧准星，光束会打向 oldAim。
            follow.AddLookInput(20f, 0f);
            var updateAim = typeof(PlayerController).GetMethod(
                "UpdateAimPoint", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(updateAim, "PlayerController.UpdateAimPoint 私有方法缺失");
            updateAim.Invoke(player, null);
            Vector3 newAim = player.AimPoint;
            Assert.Greater(Vector3.Distance(oldAim, newAim), 0.5f,
                "20° 甩枪后新旧瞄准点必须明显不同，否则用例无法区分帧差");

            yield return null; // 本帧 WeaponController.LateUpdate 发射
            yield return null; // 等待一帧后读取稳定结果

            Assert.IsFalse(weapon.IsCharging, "松手帧应发射并结束蓄力");
            Assert.AreEqual(shotsBefore + 1, weapon.ShotsFired, "应只发射一束光柱");

            BeamResult beam = weapon.LastBeamResult;
            float missToNew = Vector3.Distance(beam.End, newAim);
            float missToOld = Vector3.Distance(beam.End, oldAim);

            Assert.Less(missToNew, missToOld,
                $"光束终点应贴近松手帧的新准星，而不是上一帧旧准星（新 {missToNew:0.00}m / 旧 {missToOld:0.00}m）");
            Assert.Less(missToNew, 1.5f,
                $"光束终点与新准星偏差应小于 1.5m，实际 {missToNew:0.00}m");

            Object.Destroy(config);
        }

        /// <summary>找一条 12m 无遮挡、8m 处有平地的水平方向（跳过自己与战斗单位）。</summary>
        private static bool TryFindOpenGroundLane(PlayerController player, out Vector3 flatDir)
        {
            Vector3 origin = player.transform.position + Vector3.up * 1.0f;
            const float laneLength = 12f;

            for (int i = 0; i < 24; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;

                bool blocked = false;
                RaycastHit[] hits = Physics.RaycastAll(origin, dir, laneLength, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits.Length; h++)
                {
                    Collider col = hits[h].collider;
                    if (col == null)
                        continue;
                    if (col.transform.IsChildOf(player.transform))
                        continue;
                    if (col.GetComponentInParent<CombatantHealth>() != null)
                        continue;
                    blocked = true;
                    break;
                }

                if (blocked)
                    continue;

                Vector3 probe = player.transform.position + dir * 8f;
                if (!Physics.Raycast(
                        probe + Vector3.up * 3f,
                        Vector3.down,
                        out RaycastHit ground,
                        8f,
                        ~0,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (ground.normal.y < 0.85f)
                    continue;

                flatDir = dir;
                return true;
            }

            flatDir = Vector3.forward;
            return false;
        }
    }
}
