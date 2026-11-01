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
    /// P3-B 双大招与副武器手雷的运行时验收（真实加载 PhaseA_Playbox）：
    /// 1. Tidal Slam：升空/滞空期间无敌 → 落地 5m 墨斑 + 核心秒杀 + 外圈重伤；
    /// 2. Ink Tempest：发生器落点上空生成云层 → 1.1m/s 漂移 → 雨点涂地 + 34 DPS → 6.5s 消散；
    /// 3. Splat Bomb：70 墨耗 → 0.95s 引信 → 核心 180 秒杀 + 2.7m 涂地。
    /// PlayMode 会把任何运行时 log error 判为失败，因此本测试同时覆盖对象池/粒子创建的隐性问题。
    /// </summary>
    public class SpecialWeaponsPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private WeaponConfigSO _tempConfig;

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Object.Destroy(_spawned[i]);
            }
            _spawned.Clear();

            if (_tempConfig != null)
            {
                Object.Destroy(_tempConfig);
                _tempConfig = null;
            }
        }

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");

            var player = PlayerController.LocalPlayer;
            player.enabled = false; // 关闭输入驱动，避免真实输入干扰
            player.Health.IsInvulnerable = false;

            // 关闭 Bot，避免测试期间玩家被击杀或 Bot 涂地干扰覆盖率断言
            var bots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator TidalSlam_IsInvulnerableInAir_ThenKillsCoreAndPaintsFiveMetres()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var weapon = player.Weapon;
            weapon.ResetAllWeaponState();
            weapon.AddSpecialPointsRaw(500);
            Assert.IsTrue(weapon.IsSpecialReady, "充能后大招应就绪");

            Vector3 forward = Vector3.forward;
            var core = CreateCombatant("SlamCore", Team.TeamB, player.transform.position + forward * 2f);
            var outer = CreateCombatant("SlamOuter", Team.TeamB, player.transform.position + forward * 4.5f);
            var coreHealth = core.GetComponent<CombatantHealth>();
            var outerHealth = outer.GetComponent<CombatantHealth>();

            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);

            Assert.IsTrue(weapon.TryActivateSpecial(), "满充能时应能释放超级降落");
            Assert.AreEqual(SpecialKind.Slam, weapon.ActiveSpecialKind);
            Assert.IsTrue(weapon.IsSlamAirborne, "释放后应进入升空状态");
            Assert.IsTrue(player.Health.IsInvulnerable, "升空期间应无敌");

            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(player.Health.IsInvulnerable, "滞空阶段仍然无敌");
            Assert.IsTrue(weapon.IsSlamAirborne, "0.5s 时仍在空中");

            yield return new WaitForSeconds(0.75f);

            Assert.IsFalse(weapon.IsPerformingSpecial, "落地结算后应结束大招状态");
            Assert.IsFalse(weapon.IsSlamAirborne);
            Assert.IsFalse(coreHealth.IsAlive, "核心 3.2m 内 180 伤应秒杀满血目标");
            Assert.IsTrue(outerHealth.IsAlive, "4.5m 外圈不应被秒杀");
            Assert.That(outerHealth.CurrentHealth, Is.InRange(20f, 55f), "4.5m 处应吃到约 68 点外圈伤害");

            float coverageAfter = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);
            Assert.Greater(coverageAfter, coverageBefore + 0.5f, "落地应在 5m 半径内涂出巨大墨斑");
        }

        [UnityTest]
        public IEnumerator Slam_WarningCenter_ResolvesRealGround_NotSelfCapsule()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var weapon = player.Weapon;
            weapon.ResetAllWeaponState();
            weapon.AddSpecialPointsRaw(500);

            // 修复前 ResolveGroundPoint 的射线（脚底+6m 向下、mask 全开）会先命中施法者自己的
            // CharacterController 顶端（脚底+1.25m），预警圈/冲击波/涂地球心全部悬空，
            // 玩家按屏幕读圈与地面判定圆严重不共形。修复后必须落在真实地面。
            // 注意：出生点已抬到 1.2m 高的出生甲板上，这里不能再断言绝对 y≈0，
            // 改为「贴玩家脚下的地面」：允许脚下薄盘/坡面带来的 -0.45m 误差，
            // 但 +1.25m 的自胶囊命中仍会明显越界。
            Assert.IsTrue(weapon.TryActivateSpecial(), "满充能时应能释放超级降落");
            Assert.IsTrue(weapon.IsSlamAirborne, "释放后应进入升空状态");

            float feetY = player.transform.position.y;
            float groundY = weapon.SpecialGroundCenter.y;
            Assert.That(groundY, Is.InRange(feetY - 0.45f, feetY + 0.15f),
                $"预警中心应贴地（玩家脚底 y={feetY:F2}），实际 {groundY:F2} —— 若 ≈+1.25 说明射线又打中了自己的胶囊");
        }

        [UnityTest]
        public IEnumerator Slam_ImmuneTarget_SurvivesFullImpact()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var weapon = player.Weapon;
            weapon.ResetAllWeaponState();
            weapon.AddSpecialPointsRaw(500);

            // 内圈目标带无敌（出生保护 / 对方正在放大招）：伤害应完整豁免，且不再被计入命中
            var target = CreateCombatant("SlamImmune", Team.TeamB,
                player.transform.position + Vector3.forward * 2f);
            var targetHealth = target.GetComponent<CombatantHealth>();
            targetHealth.IsInvulnerable = true;

            Assert.IsTrue(weapon.TryActivateSpecial(), "满充能时应能释放超级降落");
            yield return new WaitForSeconds(1.4f);

            Assert.IsTrue(targetHealth.IsAlive, "无敌目标不应被砸死");
            Assert.AreEqual(targetHealth.MaxHealth, targetHealth.CurrentHealth,
                "无敌目标应毫发无伤（出生保护/大招无敌豁免）");
        }

        [UnityTest]
        public IEnumerator InkTempest_SpawnsDriftingCloud_PaintsAndDamagesOverTime()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var weapon = player.Weapon;

            _tempConfig = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _tempConfig.specialKind = SpecialKind.Storm;
            _tempConfig.specialPointsRequired = 180f;
            // 云层寿命从 6.5s 压到 3.0s：漂移/持续伤害断言都在前 1.75s 内完成，
            // 后面的"寿命结束回收"走同一条代码路径，仅省下纯等待。
            _tempConfig.stormDuration = 3.0f;
            weapon.EquipWeapon(_tempConfig);
            weapon.AddSpecialPointsRaw(500);
            Assert.IsTrue(weapon.IsSpecialReady, "换装风暴云武器并充能后应就绪");

            Vector3 target = player.transform.position + Vector3.forward * 6f;
            target.y = 0f;
            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);

            Assert.IsTrue(weapon.TryActivateSpecial(target), "风暴云大招应能释放");
            Assert.IsTrue(weapon.IsPerformingSpecial, "投掷前摇内应锁定开火");
            Assert.IsNotNull(InkStormCloudPool.Instance, "首次释放应自动创建风暴云对象池");

            yield return new WaitForSeconds(0.65f);
            Assert.AreEqual(SpecialKind.Storm, weapon.ActiveSpecialKind);
            Assert.IsFalse(weapon.IsPerformingSpecial, "0.35s 投掷前摇结束后应恢复操作");

            var pool = InkStormCloudPool.Instance;
            Assert.AreEqual(1, pool.ActiveStormCount, "应有一朵云处于激活状态");
            Assert.IsTrue(pool.TryGetActiveCloudPosition(out Vector3 cloudStart), "云层应已展开");

            // 在云层正下方放置敌人，验证 34 DPS 的持续伤害
            var victim = CreateCombatant("StormVictim", Team.TeamB, new Vector3(target.x, 0f, target.z));
            var victimHealth = victim.GetComponent<CombatantHealth>();

            yield return new WaitForSeconds(1.1f);

            Assert.IsTrue(pool.TryGetActiveCloudPosition(out Vector3 cloudNow), "云层应仍在降雨");
            float drift = Vector2.Distance(new Vector2(cloudStart.x, cloudStart.z), new Vector2(cloudNow.x, cloudNow.z));
            Assert.That(drift, Is.InRange(0.8f, 1.5f), $"云层应约以 1.1m/s 漂移（实际 {drift:F2}m/1.1s）");
            Assert.Less(victimHealth.CurrentHealth, 90f, "云下敌人应持续吃到 34 DPS");

            float coverageAfter = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);
            Assert.Greater(coverageAfter, coverageBefore + 0.3f, "雨点应持续侵蚀地面");

            // 生命周期（临时配置 3.0s）结束后云层应被回收；低帧率下轮询到回收完成，避免掐点抖动
            yield return new WaitForSeconds(1.4f);
            float recycleDeadline = Time.time + 2.5f;
            while (pool.ActiveStormCount > 0 && Time.time < recycleDeadline)
            {
                yield return null;
            }

            Assert.AreEqual(0, pool.ActiveStormCount, "生命周期结束后云层应完全消散并回收");
        }

        [UnityTest]
        public IEnumerator SplatBomb_IndependentOfInk_AndKillsWith95SecondFuse()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var weapon = player.Weapon;
            // 手雷与墨水脱钩（设计变更）：清空墨水也必须能投，投掷不扣墨，只受冷却限制
            player.Motor.TryConsumeInk(player.Motor.CurrentInk);
            float inkBefore = player.Motor.CurrentInk;

            Vector3 target = player.transform.position + Vector3.forward * 4f;
            target.y = 0f;
            float coverageBefore = InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA);

            Assert.IsTrue(weapon.TryThrowSubBomb(target), "手雷已与墨水脱钩：空墨也应能投掷");
            Assert.AreEqual(inkBefore, player.Motor.CurrentInk, 0.5f, "投掷手雷不得消耗墨水");

            var victim = CreateCombatant("BombVictim", Team.TeamB, new Vector3(target.x, 0f, target.z));
            var victimHealth = victim.GetComponent<CombatantHealth>();

            // 飞行 0.38s + 引信 0.95s，多等 0.2s 确保爆炸已结算
            yield return new WaitForSeconds(1.55f);

            Assert.IsFalse(victimHealth.IsAlive, "手雷核心 180 伤应秒杀满血目标");
            Assert.Greater(InkSurfaceController.Instance.GetCoveragePercent(Team.TeamA), coverageBefore + 0.05f,
                "爆炸应在 2.7m 半径内涂地");
        }

        private GameObject CreateCombatant(string name, Team team, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;

            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            go.transform.position = position;

            _spawned.Add(go);
            return go;
        }
    }
}
