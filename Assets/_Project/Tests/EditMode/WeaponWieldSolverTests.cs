using NUnit.Framework;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P35 通用武器持握/动作求解层：逐武器验证蓄力后拉、泼桶前摇/甩出、翻滚/架枪、
    /// 爆破后坐、加特林转速与默认参数一致性。纯函数，不依赖场景。
    /// </summary>
    public class WeaponWieldSolverTests
    {
        private static readonly WeaponKind[] NonRollerKinds =
        {
            WeaponKind.Shooter, WeaponKind.Charger, WeaponKind.Dualies,
            WeaponKind.Slosher, WeaponKind.Blaster, WeaponKind.Splatling
        };

        [Test]
        public void Sample_AllNonRollerKinds_AreFiniteAndBounded()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            WeaponWieldInput input = new WeaponWieldInput
            {
                Time = 3.7f,
                Speed01 = 0.4f,
                Charging = true,
                Charge01 = 1f,
                Streaming = true,
                Streaming01 = 1f,
                Dodge01 = 1f,
                DodgeSign = 1f,
                DodgeForwardSign = 1f,
                Turret01 = 1f,
                Windup01 = 1f,
                Throw01 = 1f,
                FireKick01 = 1f,
                FireKickSign = 1f,
                Release01 = 1f
            };

            for (int i = 0; i < NonRollerKinds.Length; i++)
            {
                WeaponWieldPose pose = WeaponWieldSolver.Sample(NonRollerKinds[i], input, p);
                Assert.IsFalse(float.IsNaN(pose.PitchDeg) || float.IsInfinity(pose.PitchDeg), $"{NonRollerKinds[i]} Pitch NaN");
                Assert.IsFalse(float.IsNaN(pose.YawDeg) || float.IsInfinity(pose.YawDeg), $"{NonRollerKinds[i]} Yaw NaN");
                Assert.IsFalse(float.IsNaN(pose.RollDeg) || float.IsInfinity(pose.RollDeg), $"{NonRollerKinds[i]} Roll NaN");
                Assert.That(pose.PitchDeg, Is.InRange(-45f, 45f));
                Assert.That(pose.YawDeg, Is.InRange(-20f, 20f));
                Assert.That(pose.RollDeg, Is.InRange(-25f, 25f));
                Assert.That(pose.DropMeters, Is.InRange(0f, 0.12f));
                Assert.That(pose.ForwardMeters, Is.InRange(-0.12f, 0.12f));
                Assert.That(pose.BodyPitchDeg, Is.InRange(-15f, 22f));
                Assert.That(pose.BodyRollDeg, Is.InRange(-20f, 20f));
                Assert.That(pose.Sink01, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Sample_Roller_ReturnsNeutral()
        {
            WeaponWieldPose pose = WeaponWieldSolver.Sample(
                WeaponKind.Roller,
                new WeaponWieldInput { Time = 1f, Charge01 = 1f, Throw01 = 1f },
                WeaponWieldParams.Default);

            Assert.AreEqual(0f, pose.PitchDeg, 0.0001f);
            Assert.AreEqual(0f, pose.YawDeg, 0.0001f);
            Assert.AreEqual(0f, pose.RollDeg, 0.0001f);
            Assert.AreEqual(0f, pose.DropMeters, 0.0001f);
            Assert.AreEqual(0f, pose.ForwardMeters, 0.0001f);
            Assert.AreEqual(0f, pose.BodyPitchDeg, 0.0001f);
            Assert.AreEqual(0f, pose.BodyRollDeg, 0.0001f);
            Assert.AreEqual(0f, pose.Sink01, 0.0001f);
        }

        [Test]
        public void Charger_ChargePullsBackAndLeansBack_ReleaseSnapsForward()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            WeaponWieldPose charge = WeaponWieldSolver.Sample(
                WeaponKind.Charger,
                new WeaponWieldInput { Charge01 = 1f, Charging = true },
                p);
            Assert.Less(charge.ForwardMeters, -0.04f, "满蓄力应明显后拉");
            Assert.Greater(charge.DropMeters, 0.01f, "满蓄力应略微下压");
            Assert.Less(charge.BodyPitchDeg, -3f, "满蓄力胸口应后仰");

            WeaponWieldPose release = WeaponWieldSolver.Sample(
                WeaponKind.Charger,
                new WeaponWieldInput { Release01 = 1f },
                p);
            Assert.Greater(release.ForwardMeters, 0.03f, "释放瞬间应前推");
            Assert.Greater(release.PitchDeg, 3f, "炮口应向下回正/前压");
        }

        [Test]
        public void Slosher_WindupTipsBack_ThrowTipsForward()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            WeaponWieldPose windup = WeaponWieldSolver.Sample(
                WeaponKind.Slosher,
                new WeaponWieldInput { Windup01 = 1f },
                p);
            Assert.Less(windup.PitchDeg, -12f, "前摇桶身应后仰");
            Assert.Less(windup.BodyPitchDeg, -6f, "前摇胸口应后仰");

            WeaponWieldPose throwPose = WeaponWieldSolver.Sample(
                WeaponKind.Slosher,
                new WeaponWieldInput { Throw01 = 1f },
                p);
            Assert.Greater(throwPose.PitchDeg, 20f, "甩出桶身应前倾");
            Assert.Greater(throwPose.BodyPitchDeg, 10f, "甩出胸口应前压");
            Assert.Greater(throwPose.ForwardMeters, 0.05f, "甩出应前推");
        }

        [Test]
        public void Blaster_FireKickKicksBackAndMuzzleUp()
        {
            WeaponWieldPose pose = WeaponWieldSolver.Sample(
                WeaponKind.Blaster,
                new WeaponWieldInput { FireKick01 = 1f },
                WeaponWieldParams.Default);

            Assert.Less(pose.ForwardMeters, -0.05f, "爆破开火应明显后坐");
            Assert.Less(pose.PitchDeg, -8f, "爆破开火枪口应上抬");
            Assert.Less(pose.BodyPitchDeg, -4f, "胸口应后仰");
        }

        [Test]
        public void Dualies_DodgeRollsAndSinks_TurretBraces()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            WeaponWieldPose dodge = WeaponWieldSolver.Sample(
                WeaponKind.Dualies,
                new WeaponWieldInput { Dodge01 = 1f, DodgeSign = 1f },
                p);
            Assert.Greater(dodge.BodyRollDeg, 10f, "向右翻滚身体应右倾");
            Assert.Greater(dodge.Sink01, 0.04f, "翻滚应下沉");
            Assert.Greater(dodge.RollDeg, 5f, "武器应随身体侧滚");

            WeaponWieldPose turret = WeaponWieldSolver.Sample(
                WeaponKind.Dualies,
                new WeaponWieldInput { Turret01 = 1f },
                p);
            Assert.Greater(turret.Sink01, 0.05f, "架枪应下沉");
            Assert.Greater(turret.BodyPitchDeg, 4f, "架枪胸口应前压");
            Assert.Less(turret.ForwardMeters, 0f, "架枪枪身应收拢");
        }

        [Test]
        public void Splatling_ChargeBraces_StreamShakes()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            WeaponWieldPose charge = WeaponWieldSolver.Sample(
                WeaponKind.Splatling,
                new WeaponWieldInput { Charging = true, Charge01 = 1f },
                p);
            Assert.Greater(charge.DropMeters, 0.02f, "满蓄力应下压");
            Assert.Greater(charge.BodyPitchDeg, 2f, "蓄力胸口应前压");

            WeaponWieldPose stream = WeaponWieldSolver.Sample(
                WeaponKind.Splatling,
                new WeaponWieldInput { Time = 0.1f, Streaming = true, Streaming01 = 1f },
                p);
            Assert.Greater(Mathf.Abs(stream.YawDeg), 0.01f, "扫射应有机身抖动");
            Assert.Greater(stream.BodyPitchDeg, 3f, "扫射胸口应前压");
        }

        [Test]
        public void SplatlingSpin_RampsWithCharge_AndCoasts()
        {
            WeaponWieldParams p = WeaponWieldParams.Default;
            Assert.AreEqual(p.SplatlingSpinMinDegPerSec,
                WeaponWieldSolver.SplatlingSpinDegPerSec(true, false, 0f, p), 0.01f);
            Assert.AreEqual(p.SplatlingSpinMaxDegPerSec,
                WeaponWieldSolver.SplatlingSpinDegPerSec(true, false, 1f, p), 0.01f);
            Assert.AreEqual(p.SplatlingSpinMaxDegPerSec,
                WeaponWieldSolver.SplatlingSpinDegPerSec(false, true, 0f, p), 0.01f);
            Assert.AreEqual(0f,
                WeaponWieldSolver.SplatlingSpinDegPerSec(false, false, 1f, p), 0.01f);

            float coast = WeaponWieldSolver.SplatlingSpinCoastStep(
                p.SplatlingSpinMaxDegPerSec, 0.1f, p);
            Assert.Less(coast, p.SplatlingSpinMaxDegPerSec, "松开后转速应衰减");
            Assert.Greater(coast, 0f);
        }

        [Test]
        public void Shooter_FireKickLeansIntoGun()
        {
            WeaponWieldPose pose = WeaponWieldSolver.Sample(
                WeaponKind.Shooter,
                new WeaponWieldInput { FireKick01 = 1f },
                WeaponWieldParams.Default);

            Assert.Greater(pose.BodyPitchDeg, 1f, "持续射击胸口应前压");
            Assert.Less(pose.ForwardMeters, 0f, "枪身应回拉");
            Assert.Less(pose.PitchDeg, 0f, "枪口应微抬");
        }

        [Test]
        public void AnimConfigDefaults_MatchSolverConstants()
        {
            CharacterAnimConfigSO cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            try
            {
                WeaponWieldParams p = WeaponWieldParams.Default;
                Assert.AreEqual(p.SmoothRate, cfg.weaponWieldSmoothRate, 0.0001f);
                Assert.AreEqual(p.IdleSwayDeg, cfg.weaponIdleSwayDeg, 0.0001f);
                Assert.AreEqual(p.IdleSwayHz, cfg.weaponIdleSwayHz, 0.0001f);
                Assert.AreEqual(p.ChargePullbackMeters, cfg.weaponChargePullback, 0.0001f);
                Assert.AreEqual(p.ChargeDropMeters, cfg.weaponChargeDrop, 0.0001f);
                Assert.AreEqual(p.ChargeBodyPitchDeg, cfg.weaponChargeBodyPitchDeg, 0.0001f);
                Assert.AreEqual(p.ChargeTremorDeg, cfg.weaponChargeTremorDeg, 0.0001f);
                Assert.AreEqual(p.SlosherCoilPitchDeg, cfg.slosherCoilPitchDeg, 0.0001f);
                Assert.AreEqual(p.SlosherThrowPitchDeg, cfg.slosherThrowPitchDeg, 0.0001f);
                Assert.AreEqual(p.SlosherThrowBodyPitchDeg, cfg.slosherThrowBodyPitchDeg, 0.0001f);
                Assert.AreEqual(p.BlasterKickPitchDeg, cfg.blasterKickPitchDeg, 0.0001f);
                Assert.AreEqual(p.BlasterKickBackMeters, cfg.blasterKickBackMeters, 0.0001f);
                Assert.AreEqual(p.DualiesDodgeBodyRollDeg, cfg.dualiesDodgeBodyRollDeg, 0.0001f);
                Assert.AreEqual(p.DualiesDodgeSink, cfg.dualiesDodgeSink, 0.0001f);
                Assert.AreEqual(p.DualiesTurretSink, cfg.dualiesTurretSink, 0.0001f);
                Assert.AreEqual(p.DualiesTurretBodyPitchDeg, cfg.dualiesTurretBodyPitchDeg, 0.0001f);
                Assert.AreEqual(p.SplatlingChargeDropMeters, cfg.splatlingChargeDropMeters, 0.0001f);
                Assert.AreEqual(p.SplatlingChargeBodyPitchDeg, cfg.splatlingChargeBodyPitchDeg, 0.0001f);
                Assert.AreEqual(p.SplatlingSpinMinDegPerSec, cfg.splatlingSpinMinDegPerSec, 0.0001f);
                Assert.AreEqual(p.SplatlingSpinMaxDegPerSec, cfg.splatlingSpinMaxDegPerSec, 0.0001f);
                Assert.AreEqual(p.SplatlingSpinCoastRate, cfg.splatlingSpinCoastRate, 0.0001f);
                Assert.AreEqual(p.SplatlingStreamShakeDeg, cfg.splatlingStreamShakeDeg, 0.0001f);
                Assert.AreEqual(p.ShooterFireBodyPitchDeg, cfg.shooterFireBodyPitchDeg, 0.0001f);
                Assert.AreEqual(p.ShooterFirePullbackMeters, cfg.shooterFirePullbackMeters, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }
    }
}