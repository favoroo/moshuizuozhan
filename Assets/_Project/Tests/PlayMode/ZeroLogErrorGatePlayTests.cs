using System.Collections;
using System.Collections.Generic;
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
    /// [P6-B] PlayMode 零 Log Error 守门员（覆盖所有运行时子系统）：
    /// 1. 真实加载 PhaseA_Playbox 并跑完整比赛流程（Ready → Countdown → Playing）；
    /// 2. 先让 7 名 Bot 自由行动 2s，覆盖 AI 寻路 / 开火 / 涂地 / 音频路径；
    /// 3. 玩家依次装备并开火全部 7 类主武器（弹丸池 / 光束 / 推地 / Volley / 空爆 / 蓄力扫射）；
    /// 4. 投掷手雷、释放 Tidal Slam 与 Ink Tempest，等待风暴云 6.5s 生命周期回收；
    /// 5. 等待 VFX 对象池自然收束，断言环 / 卫星墨珠 / 延迟环数量全部归零；
    /// 6. 通过 Application.logMessageReceived 捕获整段流程的 Error / Exception / Assert，
    ///    任何一条（NullReferenceException、ParticleSystem、Pool、AudioManager）都会让本测试失败。
    /// </summary>
    public class ZeroLogErrorGatePlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private static readonly WeaponKind[] AllKinds =
        {
            WeaponKind.Shooter,
            WeaponKind.Roller,
            WeaponKind.Charger,
            WeaponKind.Dualies,
            WeaponKind.Slosher,
            WeaponKind.Blaster,
            WeaponKind.Splatling
        };

        private readonly List<WeaponConfigSO> _tempConfigs = new List<WeaponConfigSO>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // 先停掉仍引用临时配置的控制器，避免 Destroy 之后下一次 Update 读到 fake-null 配置。
            PlayerController local = PlayerController.LocalPlayer;
            if (local != null)
            {
                local.enabled = false;
                if (local.Weapon != null) local.Weapon.enabled = false;
                if (local.Motor != null) local.Motor.enabled = false;
            }

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            }
            _spawned.Clear();

            for (int i = 0; i < _tempConfigs.Count; i++)
            {
                if (_tempConfigs[i] != null) Object.Destroy(_tempConfigs[i]);
            }
            _tempConfigs.Clear();
        }

        [UnityTest]
        public IEnumerator FullSystemsPlayback_LeavesZeroRuntimeLogErrors()
        {
            var errors = new List<string>();
            Application.LogCallback collector = (condition, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors.Add($"[{type}] {condition}");
                }
            };

            Application.logMessageReceived += collector;
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            // 先关闭测试框架的即时失败，让整段流程跑完后再统一汇报所有运行时错误。
            LogAssert.ignoreFailingMessages = true;

            try
            {
                SceneManager.LoadScene(SceneName);
                yield return null;
                yield return null;

                PlayerController player = PlayerController.LocalPlayer;
                Assert.IsNotNull(player, "场景里应存在本地玩家");
                Assert.IsNotNull(InkSurfaceController.Instance);
                Assert.IsNotNull(MatchManager.Instance);
                Assert.IsNotNull(VfxPool.Instance, "场景里应存在 VfxPool");
                Assert.IsNotNull(InkProjectilePool.Instance, "场景里应存在 InkProjectilePool");
                Assert.IsNotNull(InkBombPool.Instance, "场景里应存在 InkBombPool");

                player.enabled = false;
                player.Health.IsInvulnerable = true;

                var motor = player.Motor;
                var weapon = player.Weapon;

                // 压缩 3s 倒计时（本用例只关心 Playing 之后的子系统日志）
                PlayModeTestKit.ShortenMatchTimers();
                MatchManager.Instance.StartMatch();
                float timeout = 8f;
                while (!MatchManager.CombatAllowed && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
                Assert.IsTrue(MatchManager.CombatAllowed, "倒计时结束后比赛应进入 Playing");

                // 1) Bot 自由行动：覆盖 AI / 寻路 / 开火 / 涂地 / 音频
                yield return new WaitForSeconds(2f);

                var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
                Assert.Greater(bots.Length, 0, "场景应包含 Bot");
                for (int i = 0; i < bots.Length; i++)
                {
                    bots[i].enabled = false;
                    var botMotor = bots[i].GetComponent<InkCharacterMotor>();
                    if (botMotor != null) botMotor.enabled = false;
                    var botWeapon = bots[i].GetComponent<WeaponController>();
                    if (botWeapon != null) botWeapon.enabled = false;
                    var botHealth = bots[i].GetComponent<CombatantHealth>();
                    if (botHealth != null) botHealth.IsInvulnerable = true;
                }

                player.Health.IsInvulnerable = true;

                // 一个可承受伤害的木桩，用于驱动直击 / 溅射 / 击杀播报路径
                CombatantHealth dummy = CreateDummy(
                    player.transform.position + player.transform.forward * 4f, Team.TeamB);
                Vector3 dummyGround = new Vector3(dummy.transform.position.x, 0.1f, dummy.transform.position.z);

                // 2) 7 类主武器全量开火
                for (int i = 0; i < AllKinds.Length; i++)
                {
                    WeaponKind kind = AllKinds[i];
                    WeaponConfigSO config = CreateConfig(kind);

                    weapon.EquipWeapon(config);
                    weapon.ResetAllWeaponState();
                    motor.RefillInkFull();
                    player.Health.IsInvulnerable = true;

                    dummy.ResetImmediately();
                    dummy.IsInvulnerable = false;

                    yield return FireWeaponCycle(kind, weapon, dummyGround);
                    yield return new WaitForSeconds(0.45f);
                }

                // 3) 副武器：手雷（飞行 0.38s + 0.95s 引信 + 爆炸）
                weapon.ResetAllWeaponState();
                motor.RefillInkFull();
                Vector3 bombTarget = new Vector3(
                    player.transform.position.x, 0.1f, player.transform.position.z) + player.transform.forward * 5f;
                Assert.IsTrue(weapon.TryThrowSubBomb(bombTarget), "墨水充足时应能投掷手雷");
                yield return new WaitForSeconds(1.7f);

                // 4) Tidal Slam：升空 0.55 + 滞空 0.25 + 下砸 0.1 + 无敌余韵
                WeaponConfigSO slamConfig = CreateConfig(WeaponKind.Shooter);
                weapon.EquipWeapon(slamConfig);
                weapon.ResetAllWeaponState();
                motor.RefillInkFull();
                weapon.AddSpecialPoints(500);
                Assert.IsTrue(weapon.TryActivateSpecial(), "满充能应能释放超级降落");
                yield return new WaitForSeconds(1.4f);
                Assert.IsFalse(weapon.IsPerformingSpecial, "砸地结算后应结束大招状态");
                player.Health.IsInvulnerable = true;

                // 5) Ink Tempest：发生器 0.45s 飞行 → 云层漂移 + DOT → 回收
                //    生命周期用临时配置压到 2.0s（真实默认 6.5s），回收路径完全一致，只是省 4.5s 真实等待。
                WeaponConfigSO stormConfig = CreateConfig(WeaponKind.Blaster);
                stormConfig.specialKind = SpecialKind.Storm;
                stormConfig.stormDuration = 2.0f;
                weapon.EquipWeapon(stormConfig);
                weapon.ResetAllWeaponState();
                motor.RefillInkFull();
                weapon.AddSpecialPoints(500);
                Assert.IsTrue(weapon.TryActivateSpecial(dummyGround), "风暴云大招应能释放");

                yield return new WaitForSeconds(0.9f);
                Assert.IsNotNull(InkStormCloudPool.Instance, "首次释放应自动创建风暴云对象池");
                Assert.AreEqual(1, InkStormCloudPool.Instance.ActiveStormCount, "应有一朵风暴云处于激活状态");

                // 轮询到回收完成，避免低帧率掐点
                float stormDeadline = Time.time + 5f;
                while (InkStormCloudPool.Instance.ActiveStormCount > 0 && Time.time < stormDeadline)
                {
                    yield return null;
                }
                Assert.AreEqual(0, InkStormCloudPool.Instance.ActiveStormCount,
                    "生命周期结束后风暴云应完全回收");

                // 6) VFX 对象池收束：环 / 卫星墨珠 / 延迟环全部归零
                yield return new WaitForSeconds(1.2f);
                Assert.IsTrue(VfxPool.Instance.IsEffectPoolSettled,
                    $"VFX 池应自然收束（rings={VfxPool.Instance.ActiveRingCount}, " +
                    $"droplets={VfxPool.Instance.ActiveDropletCount}, pending={VfxPool.Instance.PendingRingCount}）");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
                Application.logMessageReceived -= collector;
            }

            CollectionAssert.IsEmpty(errors,
                $"运行时出现 {errors.Count} 条 Log Error：\n" + string.Join("\n", errors));
        }

        /// <summary>按武器机制驱动一段真实开火周期（按住/释放/翻滚/前摇）。</summary>
        private static IEnumerator FireWeaponCycle(WeaponKind kind, WeaponController weapon, Vector3 aimPoint)
        {
            Vector3 aimDir = Vector3.forward;

            switch (kind)
            {
                case WeaponKind.Charger:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 1.1f);
                    yield return new WaitForSeconds(0.25f);
                    break;

                case WeaponKind.Splatling:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.9f);
                    yield return new WaitForSeconds(1.9f);
                    break;

                case WeaponKind.Dualies:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.3f);
                    weapon.TryDodgeRoll(Vector3.forward);
                    yield return new WaitForSeconds(0.4f);
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.3f);
                    yield return new WaitForSeconds(0.3f);
                    break;

                case WeaponKind.Roller:
                    // 前摇 0.22s → 挥击 9 滴；继续按住进入推地
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.5f);
                    yield return new WaitForSeconds(0.3f);
                    break;

                case WeaponKind.Slosher:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.25f);
                    yield return new WaitForSeconds(0.55f);
                    break;

                case WeaponKind.Blaster:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 1.0f);
                    yield return new WaitForSeconds(0.6f);
                    break;

                default:
                    yield return HoldTrigger(weapon, aimDir, aimPoint, 0.5f);
                    yield return new WaitForSeconds(0.5f);
                    break;
            }
        }

        private static IEnumerator HoldTrigger(
            WeaponController weapon, Vector3 aimDir, Vector3 aimPoint, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                weapon.TryFireMainWeapon(aimDir, aimPoint);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private CombatantHealth CreateDummy(Vector3 worldPos, Team team)
        {
            var go = new GameObject("QA_GateDummy");
            go.transform.position = worldPos;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 1.6f;
            capsule.center = new Vector3(0f, 0.8f, 0f);

            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            _spawned.Add(go);
            return health;
        }

        private WeaponConfigSO CreateConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _tempConfigs.Add(config);

#if UNITY_EDITOR
            switch (kind)
            {
                case WeaponKind.Dualies:
                    WeaponConfigSO.ApplyDualiesSpec(config);
                    break;
                case WeaponKind.Slosher:
                    WeaponConfigSO.ApplySlosherSpec(config);
                    break;
                case WeaponKind.Blaster:
                    WeaponConfigSO.ApplyBlasterSpec(config);
                    break;
                case WeaponKind.Splatling:
                    WeaponConfigSO.ApplySplatlingSpec(config);
                    break;
                case WeaponKind.Roller:
                    config.weaponKind = WeaponKind.Roller;
                    config.displayName = "Swell Roller";
                    config.maxRange = 6f;
                    break;
                case WeaponKind.Charger:
                    config.weaponKind = WeaponKind.Charger;
                    config.displayName = "Glint Charger";
                    config.maxRange = config.chargeRangeMax;
                    break;
                default:
                    break;
            }
#else
            config.weaponKind = kind;
#endif
            return config;
        }
    }
}