using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P6-B] 全武器库回归契约（7 类主武器）：
    /// 1. 工厂分派与配置档案（射程 / 单发伤害 / 每次攻击耗墨）；
    /// 2. 真实策略耗墨比例（小枪 0.95、双枪 0.85、泼桶 7.5、爆破枪 9、加特林 0.6/发、蓄力狙 18 满蓄）；
    /// 3. 伤害衰减曲线（滚筒挥击 125→30；蓄力狙 40→160；爆破枪 125 直击 + 70/30 溅射）；
    /// 4. 特殊状态（双枪翻滚/架枪、泼桶 Volley 聚合、加特林蓄力释放）；
    /// 5. 空墨罐时 7 类武器一律拒绝开火（不产生幽灵弹丸）。
    /// 全部走策略与纯函数，不依赖场景、物理或对象池。
    /// </summary>
    public class WeaponArsenalContractTests
    {
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

        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] == null)
                    continue;

                var health = _createdObjects[i].GetComponent<P1.Player.CombatantHealth>();
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

        // ------------------------------------------------------------------ 工厂与配置档案

        [Test]
        public void AllSevenWeapons_UseDedicatedStrategy_AndMatchKind()
        {
            foreach (WeaponKind kind in AllKinds)
            {
                WeaponConfigSO config = CreateSpecConfig(kind);
                MainWeaponStrategy strategy = WeaponStrategyFactory.Create(config);

                Assert.IsNotNull(strategy, $"{kind} 应能构造策略");
                Assert.AreEqual(kind, strategy.Kind, $"{kind} 应分派到对应策略");

                MainWeaponStrategy byKind = WeaponStrategyFactory.Create(kind, config);
                Assert.AreEqual(kind, byKind.Kind, $"{kind} 的显式枚举工厂重载也必须一致");
            }
        }

        [Test]
        public void AllSevenWeapons_ProfileContract_RangeDamageAndInkCost()
        {
            // 小枪 Spritzer
            WeaponConfigSO shooter = CreateSpecConfig(WeaponKind.Shooter);
            Assert.AreEqual(12f, shooter.maxRange, 0.0001f);
            Assert.AreEqual(36f, shooter.damage, 0.0001f);
            Assert.AreEqual(0.95f, shooter.inkCostPerShot, 0.0001f);
            Assert.AreEqual(34f, shooter.projectileSpeed, 0.0001f);

            // 滚筒 Swell Roller
            WeaponConfigSO roller = CreateSpecConfig(WeaponKind.Roller);
            Assert.AreEqual(8.5f, roller.flickRange, 0.0001f, "挥击射程 8.5m");
            Assert.AreEqual(125f, roller.flickDamageNear, 0.0001f, "近距 125 伤");
            Assert.AreEqual(30f, roller.flickDamageFar, 0.0001f, "远距 30 伤");
            Assert.AreEqual(9f, roller.flickInkCost, 0.0001f, "挥击耗墨 9");
            Assert.AreEqual(140f, roller.rollDamage, 0.0001f, "碾压 140");
            Assert.AreEqual(1.9f, roller.rollWidth, 0.0001f, "墨带 1.9m");
            Assert.AreEqual(1.1f, roller.rollInkPerMeter, 0.0001f, "推地 1.1 墨/米");

            // 蓄力狙 Glint Charger
            WeaponConfigSO charger = CreateSpecConfig(WeaponKind.Charger);
            Assert.AreEqual(11f, charger.chargeRangeMin, 0.0001f, "最短射程 11m");
            Assert.AreEqual(27f, charger.chargeRangeMax, 0.0001f, "满蓄射程 27m");
            Assert.AreEqual(40f, charger.chargeDamageMin, 0.0001f);
            Assert.AreEqual(160f, charger.chargeDamageMax, 0.0001f);
            Assert.AreEqual(18f, charger.chargeInkCostFull, 0.0001f, "满蓄耗墨 18");

            // 双枪 Twinfin Dualies
            WeaponConfigSO dualies = CreateSpecConfig(WeaponKind.Dualies);
            Assert.AreEqual(11f, dualies.maxRange, 0.0001f);
            Assert.AreEqual(30f, dualies.damage, 0.0001f);
            Assert.AreEqual(0.85f, dualies.inkCostPerShot, 0.0001f);
            Assert.AreEqual(7f, dualies.dualiesRollInkCost, 0.0001f);
            Assert.AreEqual(2.8f, dualies.dualiesRollDistance, 0.0001f);

            // 泼桶 Tidebucket Slosher
            WeaponConfigSO slosher = CreateSpecConfig(WeaponKind.Slosher);
            Assert.AreEqual(9.5f, slosher.maxRange, 0.0001f);
            Assert.AreEqual(70f, slosher.slosherHeadDamage, 0.0001f);
            Assert.AreEqual(34f, slosher.slosherTailDamage, 0.0001f);
            Assert.AreEqual(7.5f, slosher.inkCostPerShot, 0.0001f);
            Assert.AreEqual(8, slosher.slosherDrops);

            // 爆破枪 Popper Blaster
            WeaponConfigSO blaster = CreateSpecConfig(WeaponKind.Blaster);
            Assert.AreEqual(10.5f, blaster.maxRange, 0.0001f);
            Assert.AreEqual(125f, blaster.damage, 0.0001f);
            Assert.AreEqual(9f, blaster.inkCostPerShot, 0.0001f);
            Assert.AreEqual(2.6f, blaster.blasterSplashRadius, 0.0001f);
            Assert.AreEqual(70f, blaster.blasterSplashDamageMax, 0.0001f);
            Assert.AreEqual(30f, blaster.blasterSplashDamageMin, 0.0001f);

            // 加特林 Gyre Splatling
            WeaponConfigSO splatling = CreateSpecConfig(WeaponKind.Splatling);
            Assert.AreEqual(15f, splatling.maxRange, 0.0001f);
            Assert.AreEqual(28f, splatling.damage, 0.0001f);
            Assert.AreEqual(0.6f, splatling.inkCostPerShot, 0.0001f);
            Assert.AreEqual(40f, splatling.projectileSpeed, 0.0001f);
            Assert.AreEqual(0.85f, splatling.splatlingChargeTime, 0.0001f);
        }

        // ------------------------------------------------------------------ 耗墨比例

        [Test]
        public void Shooter_TenShots_ConsumeExactly9Point5Ink()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Shooter);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new ShooterWeaponStrategy(config);
            WeaponAim aim = ForwardAim();

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host), $"第 {i + 1} 发应成功");
                strategy.Tick(config.fireInterval, true, aim, host);
                host.TriggerHeldLastFrame = true;
            }

            Assert.AreEqual(10, host.Shots.Count);
            Assert.AreEqual(100f - 10f * 0.95f, host.CurrentInk, 0.005f, "小枪每发耗墨 0.95");
        }

        [Test]
        public void Charger_FullAndHalfCharge_ConsumeProportionalInk()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Charger);

            var fullHost = new FakeHost { Config = config, CurrentInk = 100f };
            var fullStrategy = new ChargerWeaponStrategy(config);
            WeaponAim aim = ForwardAim(10f);
            fullStrategy.OnTriggerHeld(aim, 1f / 60f, fullHost);
            fullStrategy.Tick(1f, true, aim, fullHost);
            fullStrategy.Tick(1f / 60f, false, aim, fullHost);
            Assert.AreEqual(1, fullHost.Beams.Count);
            Assert.AreEqual(100f - 18f, fullHost.CurrentInk, 0.01f, "满蓄力 100% 耗墨 18");

            var halfHost = new FakeHost { Config = config, CurrentInk = 100f };
            var halfStrategy = new ChargerWeaponStrategy(config);
            halfStrategy.OnTriggerHeld(aim, 1f / 60f, halfHost);
            halfStrategy.Tick(0.5f, true, aim, halfHost);
            halfStrategy.Tick(1f / 60f, false, aim, halfHost);
            Assert.AreEqual(1, halfHost.Beams.Count);
            Assert.AreEqual(100f - 9f, halfHost.CurrentInk, 0.01f, "50% 蓄力按比例耗墨 9");
        }

        [Test]
        public void Dualies_ShotAndRoll_ConsumeExactInk()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Dualies);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new DualiesWeaponStrategy(config);
            WeaponAim aim = ForwardAim(8f);

            Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.AreEqual(100f - 0.85f, host.CurrentInk, 0.005f, "双枪单发耗墨 0.85");

            Assert.IsTrue(strategy.TryPerformDodgeRoll(Vector3.right, host));
            Assert.AreEqual(100f - 0.85f - 7f, host.CurrentInk, 0.005f, "战术翻滚额外耗墨 7");
        }

        [Test]
        public void Slosher_OneVolley_Consumes7Point5Ink_AndKeepsSharedContext()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Slosher);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SlosherWeaponStrategy(config);
            WeaponAim aim = ForwardAim(9f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            Assert.AreEqual(100f - 7.5f, host.CurrentInk, 0.005f, "一次泼洒耗墨 7.5");
            strategy.Tick(config.slosherWindup + 0.01f, true, aim, host);

            Assert.AreEqual(8, host.Shots.Count);
            Assert.AreEqual(9.5f, host.Shots[0].MaxRange, 0.001f);
            Assert.AreEqual(70f, host.Shots[0].Damage, 0.001f);
            Assert.AreEqual(34f, host.Shots[7].Damage, 0.001f);

            SlosherVolley volley = host.Shots[0].Volley;
            for (int i = 1; i < host.Shots.Count; i++)
            {
                Assert.AreSame(volley, host.Shots[i].Volley, "同一次泼洒必须共享 Volley 上下文");
            }
        }

        [Test]
        public void Blaster_OneShot_Consumes9Ink_AndCarriesAirburstEnvelope()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Blaster);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new BlasterWeaponStrategy(config);
            WeaponAim aim = ForwardAim(8f);

            Assert.IsTrue(strategy.OnTriggerHeld(aim, 1f / 60f, host));
            Assert.AreEqual(100f - 9f, host.CurrentInk, 0.005f, "爆破枪单发耗墨 9");
            Assert.AreEqual(1, host.Shots.Count);

            WeaponShot shot = host.Shots[0];
            Assert.AreEqual(125f, shot.Damage, 0.001f, "直击 125 秒杀");
            Assert.AreEqual(10.5f, shot.MaxRange, 0.001f, "满射程 10.5m 自动空爆");
            Assert.AreEqual(23f, shot.Speed, 0.001f);
            Assert.AreEqual(0f, shot.Gravity, 0.001f, "重弹水平平直飞行");
            Assert.IsTrue(shot.IsBlaster);
            Assert.AreEqual(2.6f, shot.SplashRadius, 0.001f);
            Assert.AreEqual(70f, shot.SplashDamageMax, 0.001f);
            Assert.AreEqual(30f, shot.SplashDamageMin, 0.001f);

            float airburstSeconds = shot.MaxRange / shot.Speed;
            Assert.AreEqual(0.4565f, airburstSeconds, 0.001f, "10.5m / 23m/s ≈ 0.46s 后空爆");
        }

        [Test]
        public void Splatling_FullBurst_InkEfficiencyMatches15ShotsPerSecond()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Splatling);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(12f);

            Assert.IsFalse(strategy.OnTriggerHeld(aim, 1f / 60f, host), "按住扳机只蓄力，不耗墨");
            Assert.AreEqual(100f, host.CurrentInk, 0.001f, "蓄力本身不扣墨");
            strategy.Tick(config.splatlingChargeTime + 0.01f, true, aim, host);
            Assert.AreEqual(1f, strategy.Charge01, 0.001f);
            strategy.Tick(1f / 60f, false, aim, host);

            const float dt = 1f / 60f;
            int frames = Mathf.CeilToInt(config.splatlingBurstMaxDuration / dt) + 2;
            for (int f = 0; f < frames; f++)
            {
                strategy.Tick(dt, false, aim, host);
            }

            Assert.That(host.Shots.Count, Is.InRange(24, 27), $"满蓄力约 25 发，实际 {host.Shots.Count}");
            float expectedInk = 100f - host.Shots.Count * 0.6f;
            Assert.AreEqual(expectedInk, host.CurrentInk, 0.05f, "加特林每发耗墨 0.6");
            Assert.Greater(100f - host.CurrentInk, 14f, "一次满蓄力扫射应消耗约 15 墨");
        }

        // ------------------------------------------------------------------ 伤害衰减

        [Test]
        public void Roller_FlickDamage_FallsLinearlyFrom125To30()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Roller);
            var shot = new WeaponShot
            {
                Damage = config.flickDamageNear,
                FarDamage = config.flickDamageFar,
                FalloffStart = config.flickFalloffStart,
                FalloffDistance = config.flickRange
            };

            Assert.AreEqual(125f, shot.DamageAtDistance(0f), 0.001f, "起点仍是近距满伤");
            Assert.AreEqual(125f, shot.DamageAtDistance(config.flickFalloffStart), 0.001f, "衰减起点前不衰减");
            Assert.AreEqual(77.5f, shot.DamageAtDistance(5f), 0.001f, "5m 处线性衰减到 77.5");
            Assert.AreEqual(30f, shot.DamageAtDistance(config.flickRange), 0.001f, "8.5m 末端衰减到 30");
            Assert.AreEqual(30f, shot.DamageAtDistance(99f), 0.001f, "超出末端保持远距伤害");
        }

        [Test]
        public void ComputeDamage_WithoutFalloff_IsConstant()
        {
            Assert.AreEqual(36f, WeaponShot.ComputeDamage(36f, 0f, 0f, 0f, 7f), 0.0001f);
            Assert.AreEqual(36f, WeaponShot.ComputeDamage(36f, 36f, 0f, 0f, 7f), 0.0001f);

            var chargerBeam = new WeaponBeam
            {
                Range = 27f,
                Damage = 160f,
                Pierce = true
            };
            Assert.AreEqual(160f, chargerBeam.Damage, 0.001f, "蓄力狙光束不做距离衰减");
        }

        // ------------------------------------------------------------------ 特殊状态

        [Test]
        public void Dualies_TurretBurst_Keeps20PercentFasterFireRate_AndExactInk()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Dualies);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new DualiesWeaponStrategy(config);
            WeaponAim aim = ForwardAim(8f);

            Assert.IsTrue(strategy.TryPerformDodgeRoll(Vector3.forward, host));
            strategy.Tick(config.dualiesRollDuration + 0.01f, true, aim, host);
            Assert.IsTrue(strategy.IsTurretLocked);
            Assert.AreEqual(0f, strategy.MoveSpeedMultiplier, 0.001f, "架枪站桩，移速归零");
            Assert.AreEqual(0f, strategy.SpreadDegrees, 0.001f, "架枪散布清零");

            int before = host.Shots.Count;
            const float dt = 1f / 60f;
            for (int f = 0; f < 30; f++)
            {
                strategy.OnTriggerHeld(aim, dt, host);
                strategy.Tick(dt, true, aim, host);
                host.TriggerHeldLastFrame = true;
            }

            int fired = host.Shots.Count - before;
            Assert.That(fired, Is.InRange(7, 9), $"0.5s 架枪约 7~9 发（15 发/秒），实际 {fired}");
            Assert.AreEqual(100f - 7f - fired * 0.85f, host.CurrentInk, 0.01f, "架枪每发仍耗墨 0.85");
        }

        [Test]
        public void Slosher_Volley_AggregatesHitsButSecondVolleyKills()
        {
            var victimGo = CreateCombatant("VolleyVictim", new Vector3(0f, 50f, 0f));
            var victim = victimGo.GetComponent<P1.Player.CombatantHealth>();
            var volley = new SlosherVolley();
            float applied = 0f;

            for (int i = 0; i < 8; i++)
            {
                if (!volley.TryRegisterHit(victim))
                    continue;

                float damage = i == 0 ? 70f : 34f;
                victim.TakeDamage(damage, Team.TeamA, victim.transform.position);
                applied += damage;
            }

            Assert.AreEqual(70f, applied, 0.001f, "同一次 Volley 只判 1 次直击");
            Assert.AreEqual(30f, victim.CurrentHealth, 0.001f, "单桶后剩 30 HP");
            Assert.IsTrue(victim.IsAlive);

            var nextVolley = new SlosherVolley();
            Assert.IsTrue(nextVolley.TryRegisterHit(victim));
            victim.TakeDamage(70f, Team.TeamA, victim.transform.position);
            Assert.IsFalse(victim.IsAlive, "第二桶 70+70 稳定击杀");
        }

        [Test]
        public void Splatling_PartialChargeRelease_ScalesBurstDurationAndDps()
        {
            WeaponConfigSO config = CreateSpecConfig(WeaponKind.Splatling);
            var host = new FakeHost { Config = config, CurrentInk = 100f };
            var strategy = new SplatlingWeaponStrategy(config);
            WeaponAim aim = ForwardAim(12f);

            strategy.OnTriggerHeld(aim, 1f / 60f, host);
            strategy.Tick(config.splatlingChargeTime * 0.5f, true, aim, host);
            Assert.AreEqual(0.5f, strategy.Charge01, 0.02f);

            strategy.Tick(1f / 60f, false, aim, host);
            Assert.IsTrue(strategy.IsStreaming);

            const float dt = 1f / 60f;
            int frames = Mathf.CeilToInt(config.splatlingBurstMinDuration / dt) + 45;
            for (int f = 0; f < frames; f++)
            {
                strategy.Tick(dt, false, aim, host);
            }

            Assert.IsFalse(strategy.IsStreaming, "半蓄力扫射应在约 1.0s 结束");
            Assert.That(host.Shots.Count, Is.InRange(13, 18), $"半蓄力约 15 发，实际 {host.Shots.Count}");
        }

        // ------------------------------------------------------------------ 空墨拒绝开火

        [Test]
        public void AllSevenWeapons_WithEmptyTank_RefuseToFire()
        {
            foreach (WeaponKind kind in AllKinds)
            {
                WeaponConfigSO config = CreateSpecConfig(kind);
                var host = new FakeHost { Config = config, CurrentInk = 0f };
                MainWeaponStrategy strategy = WeaponStrategyFactory.Create(config);
                WeaponAim aim = ForwardAim();

                bool fired = strategy.OnTriggerHeld(aim, 1f / 60f, host);
                strategy.Tick(1f, true, aim, host);
                strategy.Tick(1f / 60f, false, aim, host);

                Assert.IsFalse(fired, $"{kind} 空墨时不应报告成功开火");
                Assert.IsEmpty(host.Shots, $"{kind} 空墨时不应产生弹丸");
                Assert.IsEmpty(host.Beams, $"{kind} 空墨时不应产生光束");
                Assert.GreaterOrEqual(host.EmptyFeedbackCount, 1, $"{kind} 空墨应给出反馈");
            }
        }

        // ------------------------------------------------------------------ helpers

        private WeaponConfigSO CreateSpecConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _createdConfigs.Add(config);

            switch (kind)
            {
                // 默认字段即 Spritzer（小枪）规范，滚筒/蓄力狙只覆写类型与射程。
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
                default:
                    break;
            }

            return config;
        }

        private GameObject CreateCombatant(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var health = go.AddComponent<P1.Player.CombatantHealth>();
            health.Configure(Team.TeamB, null);
            go.transform.position = position;
            P1.Core.CombatantHealthRegistry.Register(health);
            _createdObjects.Add(go);
            return go;
        }

        private static WeaponAim ForwardAim(float distance = 6f)
        {
            return WeaponAim.FromDirection(Vector3.forward, new Vector3(0f, 0f, distance));
        }

        private sealed class FakeHost : IWeaponRuntimeHost
        {
            public Team Team => Team.TeamA;
            public bool IsAlive { get; set; } = true;
            public bool IsGrounded { get; set; } = true;
            public float CurrentInk { get; set; } = 100f;
            public Vector3 Position { get; set; } = Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public Vector3 MuzzlePosition => new Vector3(0f, 0.8f, 0f);
            public WeaponConfigSO Config { get; set; }
            public bool TriggerHeldLastFrame { get; set; }

            public readonly List<WeaponShot> Shots = new List<WeaponShot>();
            public readonly List<WeaponBeam> Beams = new List<WeaponBeam>();
            public readonly List<RollSweep> Rolls = new List<RollSweep>();
            public int EmptyFeedbackCount { get; private set; }

            public bool ConsumeInk(float amount)
            {
                if (amount <= 0f) return true;
                if (CurrentInk < amount) return false;
                CurrentInk -= amount;
                return true;
            }

            public void SpawnShot(WeaponShot shot) => Shots.Add(shot);
            public void FireBeam(WeaponBeam beam) => Beams.Add(beam);
            public void ApplyRoll(RollSweep sweep) => Rolls.Add(sweep);
            public void PaintGround(Vector3 worldPos, float radius) { }
            public void ReportShotFeedback() { }
            public void ReportEmptyFeedback() => EmptyFeedbackCount++;
        }
    }
}