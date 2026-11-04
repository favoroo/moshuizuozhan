using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.UI;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P36 空墨/没子弹反馈纯逻辑回归：
    /// 1. <see cref="ReticleStateMath.ComputeInk"/> 的「够不够下一发」判定与方波闪烁相位；
    /// 2. 各武器 <c>InkRequiredToFire</c> 起射门槛与策略里真实的拒射条件一致
    ///    （准星显示「没子弹」时，扳机必须真的打不出东西，反之亦然）。
    /// </summary>
    public class ReticleInkFeedbackTests
    {
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                    Object.DestroyImmediate(_createdConfigs[i]);
            }
            _createdConfigs.Clear();
        }

        // ------------------------------------------------------------------ 空墨判定

        [Test]
        public void ComputeInk_WithEnoughInk_NotOutOfInk()
        {
            ReticleStateMath.InkState state = ReticleStateMath.ComputeInk(50f, 0.95f, 1.23f);

            Assert.IsFalse(state.OutOfInk);
            Assert.AreEqual(0.95f, state.RequiredInk, 0.0001f);
            Assert.IsTrue(state.BlinkOn, "有墨时恒为亮相位，不准闪烁");
        }

        [Test]
        public void ComputeInk_BelowRequirement_IsOutOfInk()
        {
            Assert.IsTrue(ReticleStateMath.ComputeInk(0.9f, 0.95f, 0f).OutOfInk);
            Assert.IsTrue(ReticleStateMath.ComputeInk(0f, 0.95f, 0f).OutOfInk);
            Assert.IsFalse(ReticleStateMath.ComputeInk(0.95f, 0.95f, 0f).OutOfInk,
                "恰好够一发不算没子弹（消耗判定是 < 而非 <=）");
        }

        [Test]
        public void ComputeInk_ZeroRequirement_NeverOutOfInk()
        {
            Assert.IsFalse(ReticleStateMath.ComputeInk(0f, 0f, 0.5f).OutOfInk,
                "零消耗武器（理论上不该出现）不能常亮红警告");
        }

        /// <summary>
        /// 方波硬切：半个周期亮、半个周期暗，与 HUD 墨水罐的警告节奏同源（几何大胆风不用正弦呼吸）。
        /// </summary>
        [Test]
        public void ComputeInk_Blink_UsesSquareWave()
        {
            const float hz = ReticleStateMath.EmptyBlinkHz;
            float halfPeriod = 0.5f / hz;

            Assert.IsTrue(ReticleStateMath.ComputeInk(0f, 1f, 0f).BlinkOn);
            Assert.IsFalse(ReticleStateMath.ComputeInk(0f, 1f, halfPeriod).BlinkOn,
                "半周期处应切到暗相位");
            Assert.IsTrue(ReticleStateMath.ComputeInk(0f, 1f, halfPeriod * 2f).BlinkOn,
                "一个周期后回到亮相位");
        }

        // ------------------------------------------------------------------ 各武器门槛

        [Test]
        public void InkRequiredToFire_DefaultsToShotCost()
        {
            WeaponConfigSO cfg = CreateConfig(WeaponKind.Shooter);
            cfg.inkCostPerShot = 1.25f;

            Assert.AreEqual(1.25f, new ShooterWeaponStrategy(cfg).InkRequiredToFire, 0.0001f);
            Assert.AreEqual(1.25f, new DualiesWeaponStrategy(cfg).InkRequiredToFire, 0.0001f);
            Assert.AreEqual(1.25f, new SlosherWeaponStrategy(cfg).InkRequiredToFire, 0.0001f);
            Assert.AreEqual(1.25f, new BlasterWeaponStrategy(cfg).InkRequiredToFire, 0.0001f);
        }

        [Test]
        public void InkRequiredToFire_ChargerUsesMinChargeCost()
        {
            WeaponConfigSO cfg = CreateConfig(WeaponKind.Charger);
            cfg.chargeInkCostFull = 20f;
            cfg.chargeMinInkFraction = 0.25f;

            Assert.AreEqual(5f, new ChargerWeaponStrategy(cfg).InkRequiredToFire, 0.0001f,
                "蓄力狙门槛 = 满蓄墨耗 × 最低蓄力比例（与 OnTriggerHeld 的拒射判定同源）");
        }

        [Test]
        public void InkRequiredToFire_SplatlingUsesFiveShotBurst()
        {
            WeaponConfigSO cfg = CreateConfig(WeaponKind.Splatling);
            cfg.inkCostPerShot = 0.6f;

            Assert.AreEqual(3f, new SplatlingWeaponStrategy(cfg).InkRequiredToFire, 0.0001f,
                "加特林起射需要 5 发子弹的墨水");
        }

        [Test]
        public void InkRequiredToFire_RollerUsesRollStartThreshold()
        {
            WeaponConfigSO cfg = CreateConfig(WeaponKind.Roller);
            cfg.rollMinInkToStart = 0.5f;
            cfg.flickInkCost = 9f;

            Assert.AreEqual(0.5f, new RollerWeaponStrategy(cfg).InkRequiredToFire, 0.0001f,
                "滚筒准星表达推地宽度，门槛取推地起步墨水而不是挥击墨耗");
        }

        // ------------------------------------------------------------------ 门槛与实际拒射一致

        /// <summary>
        /// 准星说「没子弹」的门槛必须就是策略真正拒射的门槛：
        /// 门槛以下一定收到 ReportEmptyFeedback，门槛处一定能打出去。
        /// </summary>
        [Test]
        public void InkRequiredToFire_MatchesActualDenyThreshold()
        {
            WeaponConfigSO shooterCfg = CreateConfig(WeaponKind.Shooter);
            shooterCfg.inkCostPerShot = 1f;
            AssertDeniesJustBelowAndFiresAt(new ShooterWeaponStrategy(shooterCfg), shooterCfg);

            WeaponConfigSO chargerCfg = CreateConfig(WeaponKind.Charger);
            chargerCfg.chargeInkCostFull = 20f;
            chargerCfg.chargeMinInkFraction = 0.25f;
            AssertDeniesJustBelowAndFiresAt(new ChargerWeaponStrategy(chargerCfg), chargerCfg);

            WeaponConfigSO splatlingCfg = CreateConfig(WeaponKind.Splatling);
            splatlingCfg.inkCostPerShot = 0.6f;
            AssertDeniesJustBelowAndFiresAt(new SplatlingWeaponStrategy(splatlingCfg), splatlingCfg);
        }

        private static void AssertDeniesJustBelowAndFiresAt(MainWeaponStrategy strategy, WeaponConfigSO cfg)
        {
            float required = strategy.InkRequiredToFire;
            Assert.Greater(required, 0f, "测试前提：武器必须消耗墨水");

            var denied = new TestHost { Config = cfg, CurrentInk = required - 0.001f };
            strategy.OnTriggerHeld(WeaponAim.FromDirection(Vector3.forward), 0.05f, denied);
            Assert.AreEqual(1, denied.EmptyFeedbackCount,
                $"{cfg.weaponKind}：墨水低于门槛时必须走上空墨反馈（准星据此弹红叉 / toast）");
            Assert.AreEqual(0f, denied.ShotCount, $"{cfg.weaponKind}：门槛以下不应真的打出一发");

            strategy.Reset();
            var accepted = new TestHost { Config = cfg, CurrentInk = required + 0.001f };
            strategy.OnTriggerHeld(WeaponAim.FromDirection(Vector3.forward), 0.05f, accepted);
            Assert.AreEqual(0, accepted.EmptyFeedbackCount,
                $"{cfg.weaponKind}：达到门槛就不该再报空墨（准星必须在能开火时退出红警告）");
        }

        private WeaponConfigSO CreateConfig(WeaponKind kind)
        {
            var config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            config.weaponKind = kind;
            _createdConfigs.Add(config);
            return config;
        }

        /// <summary>只关心「打没打出弹丸 / 有没有报空墨」的最小宿主，不建场景、不走物理。</summary>
        private sealed class TestHost : IWeaponRuntimeHost
        {
            public Team Team => Team.TeamA;
            public bool IsAlive => true;
            public bool IsGrounded => true;
            public float CurrentInk { get; set; } = 100f;
            public Vector3 Position => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public Vector3 MuzzlePosition => new Vector3(0f, 0.8f, 0f);
            public WeaponConfigSO Config { get; set; }
            public bool TriggerHeldLastFrame { get; set; }

            public int ShotCount { get; private set; }
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

            public void SpawnShot(WeaponShot shot) => ShotCount++;
            public void FireBeam(WeaponBeam beam) => ShotCount++;
            public void ApplyRoll(RollSweep sweep) { }
            public void PaintGround(Vector3 worldPos, float radius) { }
            public void ReportShotFeedback() { }
            public void ReportEmptyFeedback() => EmptyFeedbackCount++;
        }
    }
}
