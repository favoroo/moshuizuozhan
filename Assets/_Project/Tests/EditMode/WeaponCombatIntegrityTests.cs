using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 武器系统修复回归（对应「输入被禁用被当成松手 / 泼桶跨波 Volley / 爆破枪二次结算 /
    /// 终场后战斗不冻结 / 滚筒越界墨带 / 滚筒挥击节奏」六个已确证 bug）：
    /// 1. 输入被抑制时不得把「这一帧没收到扳机」当成松手（蓄力狙自动开炮 / 加特林自动扫射 / 滚筒补挥击）；
    /// 2. 泼桶每波独立 Volley：两波交错飞行时同一目标每波最多 1 次直击；
    /// 3. 爆破枪爆裂由 Explode 统一结算，直击目标只由弹丸补一次直伤（不再走通用分支二次涂墨）；
    /// 4. 终场哨响后伤害与涂地结算冻结（对局阶段之外仍允许，保证运行时验收与训练场可用）；
    /// 5. 滚筒越界回收的传送帧不得画出横贯地图的墨带、不得扣墨；
    /// 6. 两次完整挥击的最小间隔遵守 <see cref="WeaponConfigSO.flickInterval"/>。
    /// 全部为纯逻辑用例，不依赖场景、物理或对象池。
    /// </summary>
    public class WeaponCombatIntegrityTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();
        private MatchManager _testMatchManager;

        [TearDown]
        public void TearDown()
        {
            SetMatchManagerInstance(null);
            _testMatchManager = null;

            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] == null)
                    continue;

                var health = _createdObjects[i].GetComponent<CombatantHealth>();
                if (health != null)
                    CombatantHealthRegistry.Unregister(health);

                Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                    Object.DestroyImmediate(_createdConfigs[i]);
            }
            _createdConfigs.Clear();
        }

        // ------------------------------------------------------------------ 1. 输入被抑制 ≠ 松手

        [Test]
        public void Charger_SuppressedInput_CancelsChargeWithoutFiring()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Charger);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(10f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.IsTrue(strategy.IsCharging, "按下扳机应进入蓄力");

            strategy.Tick(0.6f, true, aim, host);
            Assert.Greater(strategy.Charge01, 0.4f, "按住 0.6s 应有明显蓄力");

            // 暂停 / 终场 / 投雷仲裁：本帧输入被抑制
            Assert.IsTrue(strategy.IsTriggerEngaged, "蓄力中的武器应报告「扳机仍被有效按住」");
            strategy.OnTriggerSuppressed(host);
            strategy.Tick(1f / 60f, false, aim, host);

            Assert.AreEqual(0, host.Beams.Count, "输入被抑制不得把蓄力当成松手开炮");
            Assert.AreEqual(0, host.Shots.Count);
            Assert.IsFalse(strategy.IsCharging, "抑制后蓄力必须被取消");
            Assert.AreEqual(0f, strategy.Charge01, 0.0001f);
            Assert.AreEqual(100f, host.CurrentInk, 0.001f, "被抑制的蓄力不得扣墨水");
            Assert.IsFalse(strategy.IsTriggerEngaged);
        }

        [Test]
        public void Splatling_SuppressedInput_CancelsChargeWithoutStreaming()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Splatling);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(10f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.6f, true, aim, host);
            Assert.IsTrue(strategy.IsCharging, "按住扳机应进入旋转蓄力");
            Assert.Greater(strategy.Charge01, 0.3f);

            strategy.OnTriggerSuppressed(host);
            strategy.Tick(0.2f, false, aim, host);

            Assert.IsFalse(strategy.IsStreaming, "抑制不得把蓄力转成一整轮扫射");
            Assert.AreEqual(0, host.Shots.Count, "被抑制的帧不得倾泻弹幕");
            Assert.AreEqual(0f, strategy.Charge01, 0.0001f, "蓄力进度必须清零");
            Assert.AreEqual(100f, host.CurrentInk, 0.001f, "被抑制的扫射不得扣墨");
        }

        [Test]
        public void Splatling_SuppressionMidStream_StopsFiringImmediately()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Splatling);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(10f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.85f, true, aim, host);
            strategy.Tick(1f / 60f, false, aim, host); // 松手 → 进入扫射
            Assert.IsTrue(strategy.IsStreaming);

            strategy.Tick(0.2f, true, aim, host);
            int shotsBeforeSuppression = host.Shots.Count;
            Assert.Greater(shotsBeforeSuppression, 0, "扫射中应已打出弹丸");

            // 暂停（松开扳机的语义必须由显式抑制覆盖）
            strategy.OnTriggerSuppressed(host);
            strategy.Tick(0.5f, false, aim, host);

            Assert.IsFalse(strategy.IsStreaming, "抑制应立即中断扫射");
            Assert.AreEqual(shotsBeforeSuppression, host.Shots.Count, "抑制后不得再产生弹丸");
        }

        [Test]
        public void Roller_SuppressedWindup_DropsThePendingFlick()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);
            var host = new FakeHost { Config = config, CurrentInk = 100f, IsGrounded = true };
            var strategy = new RollerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(6f);

            host.TriggerHeldLastFrame = false;
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.IsTrue(strategy.IsFlicking, "按下应进入挥击前摇");

            strategy.OnTriggerSuppressed(host);
            strategy.Tick(0.5f, false, aim, host);

            Assert.IsFalse(strategy.IsFlicking, "抑制应丢弃尚未落地的挥击前摇");
            Assert.AreEqual(0, host.Shots.Count, "被抑制的挥击不得在暂停/终场中生成墨滴");
        }

        // ------------------------------------------------------------------ 2. 泼桶每波独立 Volley

        [Test]
        public void Slosher_InterleavedWaves_KeepIndependentVolleys()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Slosher);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SlosherWeaponStrategy(config);
            WeaponAim aim = ForwardAim(9f);

            var victimGo = CreateCombatant("SlosherVolleyVictim", Team.TeamB, new Vector3(0f, 50f, 5f));
            var victim = victimGo.GetComponent<CombatantHealth>();

            // 第一桶
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(config.slosherWindup + 0.01f, true, aim, host);
            Assert.AreEqual(8, host.Shots.Count, "一次泼洒 8 滴墨");
            var wave1 = new List<WeaponShot>(host.Shots);
            SlosherVolley volley1 = wave1[0].Volley;
            Assert.IsNotNull(volley1, "泼桶墨滴必须携带 Volley");

            Assert.AreEqual(1, ApplyVolleyHits(wave1, victim), "同一波对同目标最多 1 次直击");
            Assert.AreEqual(30f, victim.CurrentHealth, 0.001f, "单桶 70 伤，不可秒杀 100 HP");

            // 第二桶：第一桶的墨滴仍在飞（模拟交错飞行）
            strategy.Tick(1f, true, aim, host); // 走完泼洒冷却
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(config.slosherWindup + 0.01f, true, aim, host);
            Assert.AreEqual(16, host.Shots.Count);

            var wave2 = host.Shots.GetRange(8, 8);
            SlosherVolley volley2 = wave2[0].Volley;
            Assert.AreNotSame(
                volley1, volley2,
                "每波必须独立 Volley：旧实现共用实例并每波 Clear，在飞墨滴会吃到跨波的多段直击");

            // 关键回归：第二波开火后，第一波的命中记录必须仍然有效
            Assert.IsFalse(
                volley1.TryRegisterHit(victim),
                "第一波的命中记录不得被第二波清空（否则同一目标会被 70+34 连击秒杀）");

            Assert.AreEqual(1, ApplyVolleyHits(wave2, victim), "第二波同样对同目标最多 1 次直击");
            Assert.IsFalse(victim.IsAlive, "两桶各 70 伤（合计 140）稳定击杀");
        }

        [Test]
        public void SlosherVolleyPool_ReusesOnlyExpiredVolleys()
        {
            var pool = new SlosherVolleyPool();

            SlosherVolley first = pool.Rent(0f, 4f);
            SlosherVolley second = pool.Rent(0f, 4f);

            Assert.AreNotSame(first, second, "仍在飞行的 Volley 绝不能被复用");
            Assert.AreEqual(2, pool.Count, "两波同时在飞 → 池内两个实例");

            SlosherVolley reused = pool.Rent(5f, 4f);
            Assert.AreSame(first, reused, "飞行寿命过后应复用池内实例，避免每波 new 的持续 GC");
            Assert.AreEqual(2, pool.Count);
            Assert.AreEqual(0, reused.HitCount, "复用前必须清空命中记录");
        }

        // ------------------------------------------------------------------ 3. 爆破枪直击结算归属

        [Test]
        public void Blaster_Explode_SkipsDirectHitTarget_ButSplashesBystander()
        {
            // 契约：直接命中目标的直伤由弹丸（InkProjectilePool）单独结算，
            // WeaponBlasterSystem.Explode 必须跳过它，避免「爆裂 + 直击」双计。
            // InkProjectilePool.HandleImpact 因此在 Explode 之后直接 return，
            // 不再继续走通用分支（那会二次涂墨、二次涟漪/粒子/音效）。
            var directGo = CreateCombatant("BlasterDirect", Team.TeamB, new Vector3(0.6f, 0f, 0f));
            var bystanderGo = CreateCombatant("BlasterBystander", Team.TeamB, new Vector3(0f, 0f, 1.6f));
            var direct = directGo.GetComponent<CombatantHealth>();
            var bystander = bystanderGo.GetComponent<CombatantHealth>();

            WeaponBlasterSystem.Explode(
                Vector3.zero,
                2.6f,
                70f,
                30f,
                1.9f,
                Team.TeamA,
                null,
                direct,
                null);

            Assert.AreEqual(100f, direct.CurrentHealth, 0.001f, "直击目标不吃溅射（由弹丸单独结算直伤）");
            Assert.Less(bystander.CurrentHealth, 100f, "范围内的其他敌人照常吃溅射");
        }

        // ------------------------------------------------------------------ 4. 终场冻结

        [Test]
        public void FinishedMatch_FreezesDamageAndPaintSettlement()
        {
            var preGo = CreateCombatant("FreezePre", Team.TeamB, new Vector3(0.5f, 0f, 0f));
            var pre = preGo.GetComponent<CombatantHealth>();

            Assert.IsTrue(MatchManager.CombatSettlementAllowed, "无 MatchManager（纯逻辑测试）时门禁必须放行");
            Assert.AreEqual(
                1, SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f),
                "Playing / 无比赛时应正常结算");
            Assert.IsFalse(pre.IsAlive);

            // 终场门禁：另起一个满血目标，验证哨响后一切伤害都不再结算
            var freshGo = CreateCombatant("FreezePost", Team.TeamB, new Vector3(0.5f, 0f, 0f));
            var fresh = freshGo.GetComponent<CombatantHealth>();

            var matchGo = new GameObject("TestMatchManager");
            _createdObjects.Add(matchGo);
            _testMatchManager = matchGo.AddComponent<MatchManager>();
            SetMatchManagerInstance(_testMatchManager);
            SetMatchPhase(_testMatchManager, MatchManager.Phase.Finished);

            Assert.IsFalse(MatchManager.CombatSettlementAllowed, "终场哨响后应冻结结算");

            Assert.AreEqual(0, SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f),
                "终场后砸地冲击波不得再造成伤害");
            Assert.AreEqual(0, SpecialCombat.ApplyBombDamage(Vector3.zero, Team.TeamA, 3.1f, 1.2f, 180f, 35f),
                "终场后手雷爆炸不得再造成伤害");
            Assert.AreEqual(0, SpecialCombat.ApplyStormTick(Vector3.zero, Team.TeamA, 3.4f, 17f),
                "终场后风暴云不得再造成伤害");
            Assert.AreEqual(100f, fresh.CurrentHealth, 0.001f, "终场后不得再产生击杀");

            Assert.AreEqual(
                0,
                WeaponRollSystem.Apply(
                    new RollSweep
                    {
                        From = Vector3.zero,
                        To = new Vector3(0f, 0f, 1f),
                        Forward = Vector3.forward,
                        Width = 1.9f,
                        ForwardReach = 1.35f,
                        Damage = 140f,
                        HitInterval = 0.5f,
                        PaintRadius = 0.34f
                    },
                    Team.TeamA,
                    new Dictionary<CombatantHealth, float>(),
                    0f),
                "终场后推地不再涂地/碾压");
            Assert.AreEqual(100f, fresh.CurrentHealth, 0.001f, "终场后滚筒不得再碾压伤害");

            SetMatchPhase(_testMatchManager, MatchManager.Phase.Playing);
            Assert.IsTrue(MatchManager.CombatSettlementAllowed, "回到 Playing 后结算恢复");
            Assert.AreEqual(1, SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f));
        }

        // ------------------------------------------------------------------ 5. 滚筒越界回收

        [Test]
        public void Roller_TeleportStep_DoesNotPaintOrSpendInk()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);
            var host = new FakeHost
            {
                Config = config,
                CurrentInk = 100f,
                IsGrounded = true,
                Position = Vector3.zero,
                TriggerHeldLastFrame = true // 已按住：直接进入推地
            };
            var strategy = new RollerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(6f);

            strategy.Tick(1f / 60f, true, aim, host);
            Assert.IsTrue(strategy.IsRolling, "贴地按住扳机应进入推地");

            int sweepsBefore = host.Rolls.Count;
            float inkBefore = host.CurrentInk;

            // OutOfBoundsGuard 一帧把人传送回出生点
            host.Position = new Vector3(0f, 0f, 30f);
            strategy.Tick(1f / 60f, true, aim, host);

            Assert.AreEqual(sweepsBefore, host.Rolls.Count, "传送帧不得提交横贯地图的墨带");
            Assert.AreEqual(inkBefore, host.CurrentInk, 0.001f, "传送帧不得按传送距离扣墨");
            Assert.IsFalse(strategy.IsRolling, "传送后退出推地，等待下一帧重新起步");

            // 下一帧重新起步：只记录新起点，不扣墨
            host.Position = new Vector3(0f, 0f, 30.2f);
            strategy.Tick(1f / 60f, true, aim, host);
            Assert.AreEqual(inkBefore, host.CurrentInk, 0.001f, "重新起步帧不应按旧位置扣墨");

            // 再下一帧才按真实位移 0.2m 结算
            host.Position = new Vector3(0f, 0f, 30.4f);
            strategy.Tick(1f / 60f, true, aim, host);

            Assert.AreEqual(sweepsBefore + 2, host.Rolls.Count, "恢复后照常提交推地");
            Assert.AreEqual(inkBefore - 1.1f * 0.2f, host.CurrentInk, 0.01f, "只按 0.2m 真实位移扣墨");
        }

        // ------------------------------------------------------------------ 6. 滚筒挥击节奏

        [Test]
        public void Roller_FlickRespectsConfiguredInterval()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);
            config.flickInterval = 0.62f;
            config.flickWindup = 0.22f;

            var host = new FakeHost { Config = config, CurrentInk = 1000f, IsGrounded = false };
            var strategy = new RollerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(6f);

            // 第一次挥击：按下 → 前摇 → 落地
            host.TriggerHeldLastFrame = false;
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.25f, true, aim, host);
            Assert.AreEqual(9, host.Shots.Count, "第一次挥击应抛出 9 滴墨");

            // 立刻连点：旧实现 cooldown 恒为 0，连点可达 ~4 次/秒
            host.TriggerHeldLastFrame = false;
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.IsFalse(strategy.IsFlicking, "flickInterval 内不得开始第二次挥击");

            strategy.Tick(0.25f, true, aim, host);
            Assert.AreEqual(9, host.Shots.Count, "冷却中的连点不应产生第二波墨滴");

            // 冷却结束后可以再次挥击
            strategy.Tick(0.4f, true, aim, host);
            host.TriggerHeldLastFrame = false;
            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.IsTrue(strategy.IsFlicking, "flickInterval 过后应能再次挥击");
            strategy.Tick(0.25f, true, aim, host);
            Assert.AreEqual(18, host.Shots.Count, "第二次挥击照常生成 9 滴墨");
        }

        // ------------------------------------------------------------------ 7. 大招垂直落差

        [Test]
        public void Slam_IgnoresTargetsOnAnotherFloor()
        {
            var sameFloorGo = CreateCombatant("SlamSameFloor", Team.TeamB, new Vector3(1f, 0f, 0f));
            var upperFloorGo = CreateCombatant("SlamUpperFloor", Team.TeamB, new Vector3(1f, 3.2f, 0f));
            var sameFloor = sameFloorGo.GetComponent<CombatantHealth>();
            var upperFloor = upperFloorGo.GetComponent<CombatantHealth>();

            int hits = SpecialCombat.ApplySlamDamage(Vector3.zero, Team.TeamA, 5.2f, 3.2f, 180f, 55f);

            Assert.AreEqual(1, hits, "只命中同层目标");
            Assert.IsFalse(sameFloor.IsAlive, "同层核心圈照常秒杀");
            Assert.AreEqual(100f, upperFloor.CurrentHealth, 0.001f, "高台（3.2m）上的目标不再被地面砸地穿透");
        }

        [Test]
        public void Storm_DoesNotRainThroughTheFloor()
        {
            var groundGo = CreateCombatant("StormGround", Team.TeamB, new Vector3(0f, 0f, 1f));
            var deepGo = CreateCombatant("StormDeepBelow", Team.TeamB, new Vector3(0f, -12f, 1f));
            var ground = groundGo.GetComponent<CombatantHealth>();
            var deep = deepGo.GetComponent<CombatantHealth>();

            Vector3 cloud = new Vector3(0f, 4f, 0f);
            int hits = SpecialCombat.ApplyStormTick(cloud, Team.TeamA, 3.4f, 17f);

            Assert.AreEqual(1, hits, "只命中云下同层目标");
            Assert.AreEqual(83f, ground.CurrentHealth, 0.001f, "地面目标照常吃每跳 17 伤");
            Assert.AreEqual(100f, deep.CurrentHealth, 0.001f, "云下 16m 的下一层目标不吃雨");
        }

        // ------------------------------------------------------------------ 8. 换装只派发一次

        [Test]
        public void EquipWeapon_RaisesEquippedEventOnce()
        {
            var go = new GameObject("TestEquipWeapon");
            _createdObjects.Add(go);
            go.AddComponent<InkCharacterMotor>();
            go.AddComponent<CombatantHealth>();
            var weapon = go.AddComponent<WeaponController>();

            WeaponConfigSO config = CreateConfig(WeaponKind.Shooter);
            int equippedCount = 0;
            weapon.OnWeaponEquipped += _ => equippedCount++;

            weapon.EquipWeapon(config);

            Assert.AreEqual(1, equippedCount, "换装只能派发一次 OnWeaponEquipped（旧实现 RebuildStrategy 内外各一次）");
        }

        // ------------------------------------------------------------------ 9. 双枪与加特林移速倍率接入 Motor

        [Test]
        public void Dualies_DodgeRollAndTurretLock_DriveMotorSpeeds()
        {
            var go = new GameObject("TestDualiesMotorActor", typeof(CharacterController));
            _createdObjects.Add(go);
            var motor = go.AddComponent<InkCharacterMotor>();
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(Team.TeamA, null);
            var weapon = go.AddComponent<WeaponController>();

            typeof(InkCharacterMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(motor, null);
            typeof(WeaponController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(weapon, null);
            motor.RefillInkFull();

            WeaponConfigSO config = CreateConfig(WeaponKind.Dualies);
            config.dualiesRollDistance = 2.8f;
            config.dualiesRollDuration = 0.3f;
            config.dualiesTurretDuration = 0.5f;
            weapon.EquipWeapon(config);

            // 1) 初始人形待机：速度为正常跑步速度 6.0
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, false);
            // 驱动一帧（反射或等 Update 推进，由于在 EditMode 中，直接调私有方法或通过测试辅助驱动）
            var updateMethod = typeof(InkCharacterMotor).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updateMethod);
            updateMethod.Invoke(motor, null);

            Assert.AreEqual(6.0f, motor.CurrentMoveSpeed, 0.01f, "初始人形移速为 6.0");

            // 2) 触发双枪翻滚：速度提升至翻滚速度 2.8 / 0.3 ≈ 9.33
            bool rolled = weapon.TryDodgeRoll(Vector3.right);
            Assert.IsTrue(rolled, "成功触发战术翻滚");
            Assert.IsTrue(weapon.IsRollingDodge);

            updateMethod.Invoke(motor, null);
            Assert.AreEqual(2.8f / 0.3f, motor.CurrentMoveSpeed, 0.05f, "翻滚期间 Motor 移速应提升为翻滚速度 (~9.33 m/s)");

            // 3) 推进武器时钟至翻滚结束、进入站桩架枪 (TurretLock)
            weapon.LateUpdateForTesting(0.31f);
            Assert.IsFalse(weapon.IsRollingDodge);
            Assert.IsTrue(weapon.IsTurretLocked);

            updateMethod.Invoke(motor, null);
            Assert.AreEqual(0f, motor.CurrentMoveSpeed, 0.001f, "架枪期间 Motor 移速必须锁定为 0（原地站桩）");

            // 4) 推进武器时钟退出架枪
            weapon.LateUpdateForTesting(0.55f);
            Assert.IsFalse(weapon.IsTurretLocked);

            updateMethod.Invoke(motor, null);
            Assert.AreEqual(6.0f, motor.CurrentMoveSpeed, 0.01f, "退出架枪后恢复正常移速");
        }

        [Test]
        public void Splatling_MoveSpeedMultiplier_SlowsMotorFiringSpeed()
        {
            var go = new GameObject("TestSplatlingMotorActor", typeof(CharacterController));
            _createdObjects.Add(go);
            var motor = go.AddComponent<InkCharacterMotor>();
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(Team.TeamA, null);
            var weapon = go.AddComponent<WeaponController>();

            typeof(InkCharacterMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(motor, null);
            typeof(WeaponController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(weapon, null);
            motor.RefillInkFull();

            WeaponConfigSO config = CreateConfig(WeaponKind.Splatling);
            config.splatlingMoveSpeedCharging = 2.4f;
            weapon.EquipWeapon(config);

            var updateMethod = typeof(InkCharacterMotor).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updateMethod);

            // 开始蓄力
            weapon.TryFireMainWeapon(Vector3.forward);
            Assert.IsTrue(weapon.IsCharging);
            Assert.AreEqual(2.4f / 6.0f, weapon.MoveSpeedMultiplier, 0.01f, "加特林蓄力中移速倍率为 0.4");

            // Motor 设置为开火状态
            motor.SetFrameIntent(Vector3.forward, Vector3.forward, false, isFiring: true);
            updateMethod.Invoke(motor, null);

            float expectedSpeed = 3.8f * (2.4f / 6.0f);
            Assert.AreEqual(expectedSpeed, motor.CurrentMoveSpeed, 0.05f,
                $"加特林蓄力时开火移速应为 {expectedSpeed:F2} m/s（3.8 * 0.4），实际为 {motor.CurrentMoveSpeed:F2}");
        }

        // ------------------------------------------------------------------ helpers

        private static int ApplyVolleyHits(List<WeaponShot> drops, CombatantHealth victim)
        {
            int hits = 0;
            for (int i = 0; i < drops.Count; i++)
            {
                WeaponShot shot = drops[i];
                if (shot.Volley == null || !shot.Volley.TryRegisterHit(victim))
                    continue;

                victim.TakeDamage(shot.Damage, Team.TeamA, victim.transform.position);
                hits++;
            }

            return hits;
        }

        private WeaponConfigSO CreateConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = kind;
            _createdConfigs.Add(config);

            switch (kind)
            {
                case WeaponKind.Roller:
                    config.fireInterval = 0.15f;
                    config.flickInterval = 0.62f;
                    config.flickWindup = 0.22f;
                    config.flickInkCost = 9f;
                    config.flickDrops = 9;
                    config.flickDamageNear = 125f;
                    config.flickDamageFar = 30f;
                    config.flickFalloffStart = 1.5f;
                    config.flickRange = 8.5f;
                    config.rollWidth = 1.9f;
                    config.rollForwardReach = 1.35f;
                    config.rollDamage = 140f;
                    config.rollDamageInterval = 0.5f;
                    config.rollInkPerMeter = 1.1f;
                    config.rollMinInkToStart = 0.5f;
                    config.rollPaintRadius = 0.34f;
                    config.rollMaxStepMeters = 1.5f;
                    break;

                case WeaponKind.Charger:
                    config.chargeTime = 1f;
                    config.chargeInkCostFull = 18f;
                    config.chargeMinInkFraction = 0.2f;
                    config.chargeRangeMin = 11f;
                    config.chargeRangeMax = 27f;
                    config.chargeDamageMin = 40f;
                    config.chargeDamageMax = 160f;
                    config.chargeReleaseCooldown = 0.28f;
                    break;

                case WeaponKind.Splatling:
                    config.fireInterval = 0.066f;
                    config.inkCostPerShot = 0.6f;
                    config.damage = 28f;
                    config.projectileSpeed = 40f;
                    config.projectileGravity = 28f;
                    config.maxRange = 15f;
                    config.projectileRadius = 0.18f;
                    config.splatlingChargeTime = 0.85f;
                    config.splatlingBurstMinDuration = 0.3f;
                    config.splatlingBurstMaxDuration = 1.7f;
                    break;

                case WeaponKind.Slosher:
                    WeaponConfigSO.ApplySlosherSpec(config);
                    break;

                default:
                    break;
            }

            return config;
        }

        private GameObject CreateCombatant(string name, Team team, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var health = go.AddComponent<CombatantHealth>();
            health.Configure(team, null);
            go.transform.position = position;

            // EditMode 下 AddComponent 不一定触发 OnEnable，显式注册保证伤害结算能找到目标。
            CombatantHealthRegistry.Register(health);

            _createdObjects.Add(go);
            return go;
        }

        private static WeaponAim ForwardAim(float distance = 6f)
        {
            return WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, distance));
        }

        private static void SetMatchManagerInstance(MatchManager instance)
        {
            typeof(MatchManager)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(true)
                .Invoke(null, new object[] { instance });
        }

        private static void SetMatchPhase(MatchManager match, MatchManager.Phase phase)
        {
            typeof(MatchManager)
                .GetProperty("CurrentPhase", BindingFlags.Public | BindingFlags.Instance)
                .GetSetMethod(true)
                .Invoke(match, new object[] { phase });
        }

        private sealed class FakeHost : IWeaponRuntimeHost
        {
            public Team Team { get; set; } = Team.TeamA;
            public bool IsAlive { get; set; } = true;
            public bool IsGrounded { get; set; } = true;
            public float CurrentInk { get; set; } = 100f;
            public Vector3 Position { get; set; } = Vector3.zero;
            public Vector3 Forward { get; set; } = Vector3.forward;
            public Vector3 MuzzlePosition { get; set; } = new Vector3(0f, 0.8f, 0f);
            public WeaponConfigSO Config { get; set; }
            public bool TriggerHeldLastFrame { get; set; }

            public readonly List<WeaponShot> Shots = new List<WeaponShot>();
            public readonly List<WeaponBeam> Beams = new List<WeaponBeam>();
            public readonly List<RollSweep> Rolls = new List<RollSweep>();
            public int ShotFeedbackCount { get; private set; }
            public int EmptyFeedbackCount { get; private set; }

            public bool ConsumeInk(float amount)
            {
                if (amount <= 0f)
                    return true;
                if (CurrentInk < amount)
                    return false;

                CurrentInk -= amount;
                return true;
            }

            public void SpawnShot(WeaponShot shot) => Shots.Add(shot);

            public void FireBeam(WeaponBeam beam) => Beams.Add(beam);

            public void ApplyRoll(RollSweep sweep) => Rolls.Add(sweep);

            public void PaintGround(Vector3 worldPos, float radius)
            {
            }

            public void ReportShotFeedback() => ShotFeedbackCount++;

            public void ReportEmptyFeedback() => EmptyFeedbackCount++;
        }
    }
}
