using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P2-B 首发三武器纯逻辑回归：射速/伤害/Bloom、滚筒推地宽度与碾压秒杀、挥击 9 滴扇形衰减、
    /// 蓄力狙 1.0s 满蓄力与 160 贯穿直击。全部走策略与结算静态类，不依赖场景、物理或对象池。
    /// </summary>
    public class WeaponStrategyTests
    {
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
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

            Random.InitState(System.Environment.TickCount);
        }

        // ------------------------------------------------------------------ 配置规范

        [Test]
        public void Config_DefaultsMatchP2BSpec()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Shooter);

            Assert.AreEqual(WeaponKind.Shooter, config.weaponKind, "默认配备必须是小枪");
            Assert.AreEqual(0.1f, config.fireInterval, 0.0001f, "小枪 10 发/秒");
            Assert.AreEqual(36f, config.damage, 0.0001f, "小枪单发 36 伤");
            Assert.AreEqual(1.2f, config.spreadMinDegrees, 0.0001f, "首发高精度");
            Assert.AreEqual(5.5f, config.spreadDegrees, 0.0001f, "持续开火最大散布");

            Assert.AreEqual(1.9f, config.rollWidth, 0.0001f, "滚筒 1.9m 墨带");
            Assert.AreEqual(140f, config.rollDamage, 0.0001f, "碾压 140 秒杀");
            Assert.AreEqual(9, config.flickDrops, "挥击 9 滴");

            Assert.AreEqual(1.0f, config.chargeTime, 0.0001f, "蓄力狙 1.0s 满蓄力");
            Assert.AreEqual(160f, config.chargeDamageMax, 0.0001f, "满蓄力直击 160 秒杀");
        }

        // ------------------------------------------------------------------ Spritzer 小枪

        [Test]
        public void Shooter_FiresTenShotsPerSecond_With36Damage()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Shooter);
            config.inkCostPerShot = 0.1f;
            var host = new FakeHost { Config = config, CurrentInk = 1000f };
            var strategy = new ShooterWeaponStrategy(config);
            WeaponAim aim = ForwardAim(12f);

            const float dt = 1f / 60f;
            for (int frame = 0; frame < 60; frame++)
            {
                strategy.OnTriggerHeld(aim, dt, host);
                strategy.Tick(dt, true, aim, host);
                host.TriggerHeldLastFrame = true;
            }

            Assert.That(host.Shots.Count, Is.InRange(9, 11), $"1 秒内应打出约 10 发，实际 {host.Shots.Count} 发");
            Assert.AreEqual(host.Shots.Count, host.ShotFeedbackCount, "每发都应播放一次开火反馈");

            for (int i = 0; i < host.Shots.Count; i++)
            {
                Assert.AreEqual(36f, host.Shots[i].Damage, 0.0001f, "单发伤害必须为 36");
                Assert.AreEqual(12f, host.Shots[i].MaxRange, 0.0001f);
                Assert.AreEqual(0.18f, host.Shots[i].HitRadius, 0.0001f);
            }
        }

        [Test]
        public void Shooter_BloomStartsAccurate_ExpandsWhileFiring_RecoversOnRelease()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Shooter);
            var host = new FakeHost { Config = config, CurrentInk = 1000f };
            var strategy = new ShooterWeaponStrategy(config);
            WeaponAim aim = ForwardAim();

            Assert.AreEqual(config.spreadMinDegrees, strategy.SpreadDegrees, 0.0001f, "未开火时是首发精度");

            var spreads = new float[4];
            for (int i = 0; i < spreads.Length; i++)
            {
                Assert.IsTrue(strategy.OnTriggerHeld(aim, 0.1f, host), $"第 {i + 1} 发应成功射出");
                spreads[i] = strategy.SpreadDegrees;
                strategy.Tick(0.1f, true, aim, host);
                host.TriggerHeldLastFrame = true;
            }

            Assert.Less(spreads[0], spreads[spreads.Length - 1], "持续开火准星应逐步扩散");
            Assert.LessOrEqual(spreads[spreads.Length - 1], config.spreadDegrees + 0.0001f, "散布不超过上限");

            strategy.Tick(0.35f, false, aim, host);
            host.TriggerHeldLastFrame = false;
            Assert.AreEqual(config.spreadMinDegrees, strategy.SpreadDegrees, 0.01f, "松开扳机后精度应完全恢复");
        }

        // ------------------------------------------------------------------ Swell Roller 滚筒

        [Test]
        public void Roller_FlickLaunchesNineDroplets_InFan_WithNearAndFarDamage()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new RollerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(6f);

            Random.InitState(20261029);
            host.TriggerHeldLastFrame = false;
            strategy.OnTriggerHeld(aim, 1f / 60f, host);

            Assert.IsEmpty(host.Shots, "挥击前摇期间不应立即出弹");
            Assert.AreEqual(100f - 9f, host.CurrentInk, 0.01f, "按下瞬间扣除一次挥击墨水");

            strategy.Tick(0.25f, true, aim, host);

            Assert.AreEqual(9, host.Shots.Count, "一次挥击必须抛出 9 滴墨");

            float minYaw = float.MaxValue;
            float maxYaw = float.MinValue;
            for (int i = 0; i < host.Shots.Count; i++)
            {
                WeaponShot shot = host.Shots[i];
                Assert.AreEqual(125f, shot.Damage, 0.0001f, "近距挥击伤害 125");
                Assert.AreEqual(30f, shot.FarDamage, 0.0001f, "远距挥击伤害 30");
                Assert.AreEqual(8.5f, shot.MaxRange, 0.0001f);
                Assert.Greater(shot.Direction.y, 0.15f, "墨滴应带抛物线仰角");
                Assert.AreEqual(1.15f, shot.Origin.y, 0.01f);

                float yaw = Mathf.Atan2(shot.Direction.x, shot.Direction.z) * Mathf.Rad2Deg;
                minYaw = Mathf.Min(minYaw, yaw);
                maxYaw = Mathf.Max(maxYaw, yaw);
            }

            Assert.That(maxYaw - minYaw, Is.InRange(28f, 40f), "9 滴墨应覆盖约 34° 的扇形");
            Assert.AreEqual(1, host.ShotFeedbackCount);
        }

        [Test]
        public void Roller_RollSweep_Is190Wide_AndCostsInkPerMeter()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);
            var host = new FakeHost { Config = config, CurrentInk = 100f, IsGrounded = true };
            var strategy = new RollerWeaponStrategy(config);
            WeaponAim aim = ForwardAim();

            // 已按住扳机（非按下边沿）：不触发挥击，进入推地。
            host.TriggerHeldLastFrame = true;
            Assert.IsFalse(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.IsEmpty(host.Shots, "持续按住不应连续挥击");

            strategy.Tick(1f / 60f, true, aim, host);
            Assert.IsTrue(strategy.IsRolling);

            host.Position = new Vector3(0f, 0f, 0.5f);
            strategy.Tick(1f / 60f, true, aim, host);

            Assert.AreEqual(2, host.Rolls.Count, "每帧推地都应提交一次清扫");
            RollSweep sweep = host.Rolls[1];
            Assert.AreEqual(1.9f, sweep.Width, 0.0001f, "墨带宽度必须为 1.9m");
            Assert.AreEqual(140f, sweep.Damage, 0.0001f, "碾压伤害 140");
            Assert.AreEqual(0.5f, sweep.HitInterval, 0.0001f);
            Assert.AreEqual(0.34f, sweep.PaintRadius, 0.0001f);
            Assert.AreEqual(0f, sweep.From.z, 0.001f);
            Assert.AreEqual(0.5f, sweep.To.z, 0.001f);

            Assert.AreEqual(100f - 1.1f * 0.5f, host.CurrentInk, 0.01f, "推地按 1.1 墨/米计费");

            strategy.Tick(1f / 60f, false, aim, host);
            Assert.IsFalse(strategy.IsRolling, "松开扳机应停止推地");
        }

        [Test]
        public void Roller_RollDamage_KillsWith140_AndRespectsPerTargetInterval()
        {
            var victimGo = CreateCombatant("RollerVictim", Team.TeamB, new Vector3(0f, 0f, 0.6f));
            var victim = victimGo.GetComponent<CombatantHealth>();
            var hitTimes = new Dictionary<CombatantHealth, float>();

            var sweep = new RollSweep
            {
                From = Vector3.zero,
                To = Vector3.zero,
                Forward = Vector3.forward,
                Width = 1.9f,
                ForwardReach = 1.35f,
                Damage = 140f,
                HitInterval = 0.5f,
                PaintRadius = 0.34f
            };

            WeaponRollSystem.Apply(sweep, Team.TeamA, hitTimes, 0f);

            Assert.IsFalse(victim.IsAlive, "140 碾压伤害应一触即杀 100 HP 目标");
            Assert.AreEqual(0f, victim.CurrentHealth, 0.001f);
        }

        [Test]
        public void Roller_RollDamage_IntervalPreventsDoubleHits()
        {
            var victimGo = CreateCombatant("RollerIntervalVictim", Team.TeamB, new Vector3(0f, 0f, 0.6f));
            var victim = victimGo.GetComponent<CombatantHealth>();
            var hitTimes = new Dictionary<CombatantHealth, float>();

            var sweep = new RollSweep
            {
                From = Vector3.zero,
                To = Vector3.zero,
                Forward = Vector3.forward,
                Width = 1.9f,
                ForwardReach = 1.35f,
                Damage = 50f,
                HitInterval = 0.5f,
                PaintRadius = 0.34f
            };

            WeaponRollSystem.Apply(sweep, Team.TeamA, hitTimes, 0f);
            Assert.AreEqual(50f, victim.CurrentHealth, 0.001f);

            WeaponRollSystem.Apply(sweep, Team.TeamA, hitTimes, 0.2f);
            Assert.AreEqual(50f, victim.CurrentHealth, 0.001f, "0.5s 内同一目标不应重复受击");

            WeaponRollSystem.Apply(sweep, Team.TeamA, hitTimes, 0.6f);
            Assert.IsFalse(victim.IsAlive, "超过受击间隔后应再次结算伤害");
        }

        // ------------------------------------------------------------------ Glint Charger 蓄力狙

        [Test]
        public void Charger_ReachesFullChargeInOneSecond_ThenFires160Beam()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Charger);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, 10f));

            Assert.IsFalse(strategy.OnTriggerHeld(aim, 1f / 60f, host), "按住扳机只是开始蓄力，不立即开火");
            Assert.IsTrue(strategy.IsCharging);

            strategy.Tick(0.5f, true, aim, host);
            Assert.AreEqual(0.5f, strategy.Charge01, 0.01f);
            Assert.IsFalse(strategy.IsFullyCharged);

            strategy.Tick(0.5f, true, aim, host);
            Assert.AreEqual(1f, strategy.Charge01, 0.001f, "1.0s 应精确满蓄力");
            Assert.IsTrue(strategy.IsFullyCharged);
            Assert.IsEmpty(host.Beams, "蓄力期间不应发射");

            strategy.Tick(1f / 60f, false, aim, host);

            Assert.AreEqual(1, host.Beams.Count, "松开扳机瞬间发射一束光柱");
            WeaponBeam beam = host.Beams[0];
            Assert.AreEqual(160f, beam.Damage, 0.01f, "满蓄力直击 160");
            Assert.AreEqual(27f, beam.Range, 0.01f, "满蓄力射程 27m");
            Assert.IsTrue(beam.Pierce, "光柱应贯穿");
            Assert.AreEqual(1f, beam.Intensity01, 0.001f);
            Assert.AreEqual(100f - 18f, host.CurrentInk, 0.01f, "满蓄力消耗 18 墨");
            Assert.IsFalse(strategy.IsCharging);
        }

        [Test]
        public void Charger_PartialCharge_ScalesDamageAndRange()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Charger);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, 10f));

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.5f, true, aim, host);

            // 50% 蓄力：射程 19m；伤害 = Lerp(40, 160*0.62=99.2, 0.5) = 69.6
            strategy.Tick(1f / 60f, false, aim, host);

            Assert.AreEqual(1, host.Beams.Count);
            Assert.AreEqual(19f, host.Beams[0].Range, 0.01f);
            Assert.AreEqual(69.6f, host.Beams[0].Damage, 0.05f);
        }

        [Test]
        public void Charger_InkCapLimitsChargeToAvailableInk()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Charger);
            var host = new FakeHost { Config = config, CurrentInk = 9f };
            var strategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, 10f));

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(1f, true, aim, host);

            Assert.AreEqual(0.5f, strategy.Charge01, 0.01f, "只有 9 墨时最多蓄到 50%");

            strategy.Tick(1f / 60f, false, aim, host);
            Assert.AreEqual(1, host.Beams.Count);
            Assert.AreEqual(0f, host.CurrentInk, 0.01f, "发射后墨水应恰好清空");
        }

        [Test]
        public void Charger_BeamKillsBothTargets_WhenPiercing()
        {
            // 放到高空，避免依赖当前编辑器场景里是否存在地面/掩体碰撞体。
            var first = CreateCombatant("BeamVictimA", Team.TeamB, new Vector3(0f, 49f, 4f));
            var second = CreateCombatant("BeamVictimB", Team.TeamB, new Vector3(0f, 49f, 8f));
            var firstHealth = first.GetComponent<CombatantHealth>();
            var secondHealth = second.GetComponent<CombatantHealth>();

            var beam = new WeaponBeam
            {
                Origin = new Vector3(0f, 50f, 0f),
                Direction = Vector3.forward,
                Range = 27f,
                Damage = 160f,
                PaintRadius = 0.55f,
                PaintStep = 1.2f,
                ImpactRadius = 1.2f,
                Pierce = true,
                Intensity01 = 1f
            };

            BeamResult result = WeaponBeamSystem.Fire(beam, Team.TeamA, null, null);

            Assert.IsFalse(firstHealth.IsAlive, "光柱路径上的第一个敌人应被 160 秒杀");
            Assert.IsFalse(secondHealth.IsAlive, "贯穿光柱应继续秒杀后方敌人");
            Assert.AreEqual(27f, result.End.z, 0.01f, "空旷高空里光束应飞到最大射程");
        }

        // ------------------------------------------------------------------ [P3-A] 后续四武器规范与机制

        [Test]
        public void Config_DefaultsMatchP3ASpec()
        {
            // 双枪
            var dualies = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(dualies);
            WeaponConfigSO.ApplyDualiesSpec(dualies);
            Assert.AreEqual(WeaponKind.Dualies, dualies.weaponKind);
            Assert.AreEqual(0.083f, dualies.fireInterval, 0.001f, "双枪 12 发/秒");
            Assert.AreEqual(30f, dualies.damage, 0.001f, "双枪单发 30 伤");
            Assert.AreEqual(7f, dualies.dualiesRollInkCost, 0.001f, "翻滚耗墨 7");
            Assert.AreEqual(2.8f, dualies.dualiesRollDistance, 0.001f, "翻滚距离 2.8m");
            Assert.AreEqual(0.5f, dualies.dualiesTurretDuration, 0.001f, "站桩架枪 0.5s");
            Assert.AreEqual(0.0664f, dualies.dualiesTurretFireInterval, 0.001f, "架枪射速提升 20%");
            Assert.AreEqual(0f, dualies.dualiesTurretSpreadDegrees, 0.001f, "架枪散布清零");

            // 泼桶
            var slosher = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(slosher);
            WeaponConfigSO.ApplySlosherSpec(slosher);
            Assert.AreEqual(WeaponKind.Slosher, slosher.weaponKind);
            Assert.AreEqual(8, slosher.slosherDrops, "一次泼出 8 滴墨");
            Assert.AreEqual(70f, slosher.slosherHeadDamage, 0.001f, "头滴 70 伤稳定 2 桶击杀");
            Assert.AreEqual(0.13f, slosher.slosherWindup, 0.001f, "前摇 0.13s");

            // 爆破枪
            var blaster = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(blaster);
            WeaponConfigSO.ApplyBlasterSpec(blaster);
            Assert.AreEqual(WeaponKind.Blaster, blaster.weaponKind);
            Assert.AreEqual(125f, blaster.damage, 0.001f, "直击 125 伤秒杀");
            Assert.AreEqual(2.6f, blaster.blasterSplashRadius, 0.001f, "范围溅射 2.6m");
            Assert.AreEqual(70f, blaster.blasterSplashDamageMax, 0.001f, "中心溅射 70 伤");
            Assert.AreEqual(30f, blaster.blasterSplashDamageMin, 0.001f, "边缘溅射 30 伤");

            // 加特林
            var splatling = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(splatling);
            WeaponConfigSO.ApplySplatlingSpec(splatling);
            Assert.AreEqual(WeaponKind.Splatling, splatling.weaponKind);
            Assert.AreEqual(0.066f, splatling.fireInterval, 0.001f, "15 发/秒");
            Assert.AreEqual(28f, splatling.damage, 0.001f, "单发 28 伤");
            Assert.AreEqual(0.85f, splatling.splatlingChargeTime, 0.001f, "蓄力时间 0.85s");
            Assert.AreEqual(0.3f, splatling.splatlingBurstMinDuration, 0.001f, "最短爆发 0.3s");
            Assert.AreEqual(1.7f, splatling.splatlingBurstMaxDuration, 0.001f, "满蓄力爆发 1.7s");
        }

        // ------------------------------------------------------------------ Twinfin Dualies 双枪

        [Test]
        public void Dualies_FiresAlternatingHands_At12ShotsPerSec_With30Damage()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplyDualiesSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new DualiesWeaponStrategy(config);
            WeaponAim aim = ForwardAim(11f);

            const float dt = 1f / 60f;
            for (int f = 0; f < 60; f++)
            {
                strategy.OnTriggerHeld(aim, dt, host);
                strategy.Tick(dt, true, aim, host);
                host.TriggerHeldLastFrame = true;
            }

            Assert.That(host.Shots.Count, Is.InRange(11, 13), $"1 秒内应打出约 12 发，实际 {host.Shots.Count}");

            for (int i = 0; i < host.Shots.Count; i++)
            {
                Assert.AreEqual(30f, host.Shots[i].Damage, 0.001f, "双枪单发 30 伤");
                Assert.AreEqual(32f, host.Shots[i].Speed, 0.001f);
                Assert.AreEqual(11f, host.Shots[i].MaxRange, 0.001f);

                // 验证双手交替：X 轴枪口偏移在正负之间交替
                float xOffset = host.Shots[i].Origin.x;
                if (i % 2 == 0)
                {
                    Assert.Greater(xOffset, 0.1f, "偶数发右手开火偏右");
                }
                else
                {
                    Assert.Less(xOffset, -0.1f, "奇数发左手开火偏左");
                }
            }
        }

        [Test]
        public void Dualies_TacticalRoll_ConsumesInk_AndEntersTurretLock()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplyDualiesSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new DualiesWeaponStrategy(config);
            WeaponAim aim = ForwardAim(11f);

            Assert.AreEqual(2, strategy.RollsRemaining, "初始有 2 次翻滚额度");
            bool rolled = strategy.TryPerformDodgeRoll(Vector3.right, host);

            Assert.IsTrue(rolled, "成功触发战术翻滚");
            Assert.AreEqual(1, strategy.RollsRemaining, "翻滚后剩余 1 次");
            Assert.AreEqual(100f - 7f, host.CurrentInk, 0.01f, "翻滚消耗 7 墨水");
            Assert.IsTrue(strategy.IsRollingDodge, "处于翻滚状态");

            // 翻滚中无法开火
            Assert.IsFalse(strategy.OnTriggerHeld(aim, 1f / 60f, host), "翻滚中禁止开火");

            // 推进 0.3s，翻滚结束
            strategy.Tick(0.31f, true, aim, host);
            Assert.IsFalse(strategy.IsRollingDodge, "0.3s 后翻滚结束");
            Assert.IsTrue(strategy.IsTurretLocked, "翻滚后立刻进入架枪站桩模式");
            Assert.AreEqual(0f, strategy.MoveSpeedMultiplier, 0.001f, "架枪期间移速清零（站桩）");
            Assert.AreEqual(0f, strategy.SpreadDegrees, 0.001f, "架枪期间弹道散布清零");

            // 架枪期间开火：射速提升 20%（间隔 0.0664s，即约 15 发/秒）
            int shotsBefore = host.Shots.Count;
            for (int f = 0; f < 30; f++) // 0.5s
            {
                strategy.OnTriggerHeld(aim, 1f / 60f, host);
                strategy.Tick(1f / 60f, true, aim, host);
            }
            int shotsFired = host.Shots.Count - shotsBefore;
            Assert.That(shotsFired, Is.InRange(7, 9), "架枪 0.5s 射击约 7~8 发（15发/秒）");

            // 松开扳机且架枪时间走完，翻滚次数恢复为 2
            strategy.Tick(0.25f, false, aim, host);
            Assert.IsFalse(strategy.IsTurretLocked, "架枪计时走完退出锁定");
            Assert.AreEqual(2, strategy.RollsRemaining, "退出架枪后翻滚额度回复到 2");
            Assert.AreEqual(1f, strategy.MoveSpeedMultiplier, 0.001f, "恢复正常移速");
        }

        [Test]
        public void Dualies_MaxTwoConsecutiveRolls()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplyDualiesSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new DualiesWeaponStrategy(config);
            WeaponAim aim = ForwardAim(11f);

            // 第一次翻滚
            Assert.IsTrue(strategy.TryPerformDodgeRoll(Vector3.forward, host));
            strategy.Tick(0.31f, true, aim, host);

            // 第二次连续翻滚（2 连滚）
            Assert.IsTrue(strategy.TryPerformDodgeRoll(Vector3.left, host));
            strategy.Tick(0.31f, true, aim, host);

            // 翻滚额度用尽
            Assert.AreEqual(0, strategy.RollsRemaining);
            Assert.IsFalse(strategy.TryPerformDodgeRoll(Vector3.right, host), "最多 2 连滚，第 3 次翻滚应被拒绝");
        }

        // ------------------------------------------------------------------ Tidebucket Slosher 泼桶

        [Test]
        public void Slosher_FiresEightDrops_AfterWindup()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplySlosherSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SlosherWeaponStrategy(config);
            WeaponAim aim = ForwardAim(9.5f);

            // 刚按下扳机进入前摇，尚未生成弹丸
            Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.AreEqual(100f - 7.5f, host.CurrentInk, 0.01f, "泼墨扣除 7.5 墨");
            Assert.IsTrue(strategy.IsWindingUp, "进入 0.13s 前摇");
            Assert.IsEmpty(host.Shots, "前摇期间尚未泼出墨滴");

            // 走完 0.13s 前摇
            strategy.Tick(0.14f, true, aim, host);
            Assert.IsFalse(strategy.IsWindingUp, "前摇结束");
            Assert.AreEqual(8, host.Shots.Count, "一次泼出 8 滴墨浪");

            // 头滴直击 70 伤
            Assert.AreEqual(70f, host.Shots[0].Damage, 0.001f, "头滴直击基准 70 伤");
            for (int i = 1; i < 8; i++)
            {
                Assert.AreEqual(34f, host.Shots[i].Damage, 0.001f, "后续尾滴基准 34 伤");
            }

            // 8 滴全部关联同一个 Volley
            SlosherVolley sharedVolley = host.Shots[0].Volley;
            Assert.IsNotNull(sharedVolley, "泼桶墨滴必须携带 Volley 实例");
            for (int i = 1; i < 8; i++)
            {
                Assert.AreSame(sharedVolley, host.Shots[i].Volley, "同一次泼洒的所有墨滴必须共享同一个 Volley 实例");
            }
        }

        [Test]
        public void Slosher_VolleyAggregatesDamage_PreventingMultiHitBurst()
        {
            // 创建一个 100 HP 的假敌人
            var victimGo = CreateCombatant("SlosherVictim", Team.TeamB, new Vector3(0f, 50f, 5f));
            var victimHealth = victimGo.GetComponent<CombatantHealth>();
            Assert.AreEqual(100f, victimHealth.CurrentHealth);

            var volley = new SlosherVolley();

            // 模拟 8 滴墨全部打在目标身上
            float totalDamageApplied = 0f;
            for (int i = 0; i < 8; i++)
            {
                float baseDamage = i == 0 ? 70f : 34f;
                if (volley.TryRegisterHit(victimHealth))
                {
                    victimHealth.TakeDamage(baseDamage, Team.TeamA, victimGo.transform.position);
                    totalDamageApplied += baseDamage;
                }
            }

            Assert.AreEqual(70f, totalDamageApplied, 0.001f, "同一次 Volley 对同目标只判 1 次直击（70 伤）");
            Assert.AreEqual(30f, victimHealth.CurrentHealth, 0.001f, "单桶命中敌人扣至 30 HP，绝不秒杀");
            Assert.IsTrue(victimHealth.IsAlive, "单桶不可秒杀 100 HP 角色");

            // 第二桶泼洒（新 Volley）
            var secondVolley = new SlosherVolley();
            if (secondVolley.TryRegisterHit(victimHealth))
            {
                victimHealth.TakeDamage(70f, Team.TeamA, victimGo.transform.position);
            }

            Assert.IsFalse(victimHealth.IsAlive, "两桶（70 + 70 = 140）稳定击杀");
        }

        // ------------------------------------------------------------------ Popper Blaster 爆破枪

        [Test]
        public void Blaster_FiresDirectHit_125Damage_OneShotSplats()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplyBlasterSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new BlasterWeaponStrategy(config);
            WeaponAim aim = ForwardAim(10.5f);

            bool fired = strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.IsTrue(fired);
            Assert.AreEqual(100f - 9f, host.CurrentInk, 0.01f, "爆破枪消耗 9 墨水");
            Assert.AreEqual(1, host.Shots.Count);

            WeaponShot shot = host.Shots[0];
            Assert.AreEqual(125f, shot.Damage, 0.001f, "直击 125 伤");
            Assert.AreEqual(23f, shot.Speed, 0.001f);
            Assert.AreEqual(0f, shot.Gravity, 0.001f, "重弹水平平直飞行");
            Assert.IsTrue(shot.IsBlaster, "标记为 Blaster 弹丸");
            Assert.AreEqual(2.6f, shot.SplashRadius, 0.001f, "空爆溅射半径 2.6m");

            // 直击直接秒杀 100 HP 角色
            var victimGo = CreateCombatant("BlasterDirectVictim", Team.TeamB, new Vector3(0f, 50f, 5f));
            var victimHealth = victimGo.GetComponent<CombatantHealth>();
            victimHealth.TakeDamage(shot.Damage, Team.TeamA, victimGo.transform.position);
            Assert.IsFalse(victimHealth.IsAlive, "125 直击秒杀 100 HP 目标");
        }

        [Test]
        public void Blaster_SplashDamage_FalloffWithDistance_AndExcludesDirectTarget()
        {
            Vector3 center = new Vector3(0f, 50.75f, 0f);

            // A: 直击目标，已受 125 秒杀，空爆不应再次受到溅射
            var directTargetGo = CreateCombatant("DirectTarget", Team.TeamB, center - Vector3.up * 0.75f);
            var directHealth = directTargetGo.GetComponent<CombatantHealth>();
            directHealth.TakeDamage(125f, Team.TeamA, center);
            Assert.IsFalse(directHealth.IsAlive);

            // B: 近距离溅射 (水平距离 0.5m)
            var nearTargetGo = CreateCombatant("NearTarget", Team.TeamB, center - Vector3.up * 0.75f + new Vector3(0.5f, 0f, 0f));
            var nearHealth = nearTargetGo.GetComponent<CombatantHealth>();

            // C: 远距离溅射 (水平距离 2.0m)
            var farTargetGo = CreateCombatant("FarTarget", Team.TeamB, center - Vector3.up * 0.75f + new Vector3(2.0f, 0f, 0f));
            var farHealth = farTargetGo.GetComponent<CombatantHealth>();

            // D: 超出溅射范围 (水平距离 3.0m > 2.6m)
            var outsideTargetGo = CreateCombatant("OutsideTarget", Team.TeamB, center - Vector3.up * 0.75f + new Vector3(3.0f, 0f, 0f));
            var outsideHealth = outsideTargetGo.GetComponent<CombatantHealth>();

            WeaponBlasterSystem.Explode(
                center,
                splashRadius: 2.6f,
                splashDamageMax: 70f,
                splashDamageMin: 30f,
                groundPaintRadius: 1.5f,
                attackerTeam: Team.TeamA,
                ownerRoot: null,
                directHitTarget: directHealth,
                onCellsPainted: null);

            // 近距离受高伤害：Lerp(70, 30, 0.5/2.6 ≈ 0.192) ≈ 62.3
            float nearDamageTaken = 100f - nearHealth.CurrentHealth;
            Assert.That(nearDamageTaken, Is.InRange(60f, 65f), $"近目标受高额溅射，实际扣血 {nearDamageTaken}");

            // 远距离受轻伤害：Lerp(70, 30, 2.0/2.6 ≈ 0.769) ≈ 39.2
            float farDamageTaken = 100f - farHealth.CurrentHealth;
            Assert.That(farDamageTaken, Is.InRange(37f, 42f), $"远目标受衰减溅射，实际扣血 {farDamageTaken}");
            Assert.Greater(nearDamageTaken, farDamageTaken, "近距离伤害必须高于远距离伤害");

            // 超出范围不扣血
            Assert.AreEqual(100f, outsideHealth.CurrentHealth, "超出 2.6m 范围的目标不受伤害");
        }

        // ------------------------------------------------------------------ Gyre Splatling 加特林

        [Test]
        public void Splatling_HoldToCharge_ReleaseToStreamBurst()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplySplatlingSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(15f);

            // 按住扳机开始蓄力
            Assert.IsFalse(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.IsTrue(strategy.IsCharging);
            Assert.AreEqual(config.splatlingMoveSpeedCharging / 6.0f, strategy.MoveSpeedMultiplier, 0.01f, "蓄力移速减速");

            // 推进 0.85s 到达满蓄力
            strategy.Tick(0.86f, true, aim, host);
            Assert.AreEqual(1f, strategy.Charge01, 0.001f, "0.85s 满蓄力");
            Assert.IsEmpty(host.Shots, "蓄力阶段不发射弹丸");

            // 松开扳机释放 1.7s 高密度暴风雨弹幕
            strategy.Tick(1f / 60f, false, aim, host);
            Assert.IsFalse(strategy.IsCharging);
            Assert.IsTrue(strategy.IsStreaming, "进入扫射状态");
            Assert.AreEqual(config.splatlingMoveSpeedFiring / 6.0f, strategy.MoveSpeedMultiplier, 0.01f, "扫射移速轻微减速");

            // 模拟推进 1.7s 扫射
            const float dt = 1f / 60f;
            int frames = Mathf.CeilToInt(1.72f / dt);
            for (int f = 0; f < frames; f++)
            {
                strategy.Tick(dt, false, aim, host);
            }

            Assert.IsFalse(strategy.IsStreaming, "1.7s 后扫射完毕");
            // 1.7s / 0.066s ≈ 25~26 发
            Assert.That(host.Shots.Count, Is.InRange(24, 27), $"满蓄力释放约 25 发弹丸，实际 {host.Shots.Count}");

            for (int i = 0; i < host.Shots.Count; i++)
            {
                Assert.AreEqual(28f, host.Shots[i].Damage, 0.001f, "单发伤害 28");
                Assert.AreEqual(40f, host.Shots[i].Speed, 0.001f, "初速 40m/s");
                Assert.AreEqual(15f, host.Shots[i].MaxRange, 0.001f, "射程 15m");
            }
        }

        [Test]
        public void Splatling_PartialCharge_ScalesBurstDuration()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplySplatlingSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(15f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            // 蓄力一半时间 0.425s
            strategy.Tick(0.425f, true, aim, host);
            Assert.AreEqual(0.5f, strategy.Charge01, 0.02f);

            // 松手开始扫射：持续时间应为 Lerp(0.3, 1.7, 0.5) = 1.0s
            strategy.Tick(1f / 60f, false, aim, host);
            Assert.IsTrue(strategy.IsStreaming);

            // 推进 0.9s 仍在扫射
            const float dt = 1f / 60f;
            for (int f = 0; f < 54; f++) // 0.9s
            {
                strategy.Tick(dt, false, aim, host);
            }
            Assert.IsTrue(strategy.IsStreaming, "0.9s 时仍在扫射");

            // 推进 0.2s 扫射结束
            for (int f = 0; f < 15; f++) // 0.25s
            {
                strategy.Tick(dt, false, aim, host);
            }
            Assert.IsFalse(strategy.IsStreaming, "1.0s 左右扫射结束");
            // 1.0s / 0.066s ≈ 15 发
            Assert.That(host.Shots.Count, Is.InRange(14, 17), $"半蓄力释放约 15 发，实际 {host.Shots.Count}");
        }

        // ------------------------------------------------------------------ 3D 瞄准（朝天射击）

        [Test]
        public void Shooter_AimingAtSky_FiresUpward()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Shooter);
            var host = new FakeHost { Config = config, CurrentInk = 1000f };
            var strategy = new ShooterWeaponStrategy(config);
            WeaponAim aim = SkyAim(60f);

            Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.AreEqual(1, host.Shots.Count);

            float pitch = PitchDegrees(host.Shots[0].Direction);
            Assert.Greater(pitch, 50f, "准星对着 60° 天空时弹丸必须向上飞，而不是水平平射");
            Assert.LessOrEqual(pitch, InkBallistics.MaxLaunchPitchDegrees + 0.5f);
        }

        [Test]
        public void Blaster_AimingAtSky_FiresUpward()
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplyBlasterSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new BlasterWeaponStrategy(config);
            WeaponAim aim = SkyAim(45f);

            Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.AreEqual(0f, host.Shots[0].Gravity, 0.0001f);
            Assert.Greater(PitchDegrees(host.Shots[0].Direction), 35f, "爆破枪也必须朝准星方向飞出");
        }

        [Test]
        public void Charger_AimingAtSky_FiresBeamUpward()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Charger);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = SkyAim(50f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(1.0f, true, aim, host);
            strategy.Tick(1f / 60f, false, aim, host);

            Assert.AreEqual(1, host.Beams.Count);
            Assert.Greater(PitchDegrees(host.Beams[0].Direction), 40f, "蓄力狙朝天应打出一道朝上的光柱");
        }

        [Test]
        public void Roller_Flick_AimPitchRaisesTheFan()
        {
            WeaponConfigSO config = CreateConfig(WeaponKind.Roller);

            float flatPitch = AverageFlickPitch(config, 0f);
            float upPitch = AverageFlickPitch(config, 25f);

            Assert.Greater(upPitch, flatPitch + 10f, "抬头挥击应把墨浪抬高");
            Assert.LessOrEqual(upPitch, 18f + 28.6f + 1f, "抬升量应被限制在 +28.6° 以内");
        }

        [Test]
        public void Slosher_AimingAtHighTarget_RaisesTheLob()
        {
            WeaponConfigSO config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);
            WeaponConfigSO.ApplySlosherSpec(config);

            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SlosherWeaponStrategy(config);
            WeaponAim aim = WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 1.5f, 7f));

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.2f, true, aim, host);

            Assert.IsNotEmpty(host.Shots, "泼桶应抛出墨滴");
            Assert.Greater(PitchDegrees(host.Shots[0].Direction), 18.1f, "高目标应抬高抛射仰角");
        }

        private float AverageFlickPitch(WeaponConfigSO config, float aimPitchDegrees)
        {
            var host = new FakeHost { Config = config, CurrentInk = 100f, TriggerHeldLastFrame = false };
            var strategy = new RollerWeaponStrategy(config);

            float pitchRad = aimPitchDegrees * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(0f, Mathf.Sin(pitchRad), Mathf.Cos(pitchRad));
            WeaponAim aim = WeaponAim.FromDirection(dir, new Vector3(0f, 0f, 6f));

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(0.25f, true, aim, host);

            Assert.IsNotEmpty(host.Shots, "挥击应产生墨滴");
            float sum = 0f;
            for (int i = 0; i < host.Shots.Count; i++)
                sum += PitchDegrees(host.Shots[i].Direction);
            return sum / host.Shots.Count;
        }

        private static WeaponAim SkyAim(float pitchUpDegrees)
        {
            float rad = pitchUpDegrees * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
            Vector3 muzzle = new Vector3(0f, 0.8f, 0f);
            return WeaponAim.FromDirection(dir, muzzle + dir * 70f);
        }

        private static float PitchDegrees(Vector3 direction)
        {
            return Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ helpers

        private WeaponConfigSO CreateConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = kind;
            _createdConfigs.Add(config);
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
