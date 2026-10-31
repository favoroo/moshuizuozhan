using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 乌贼游动姿态求解器（SwimPoseSolver）纯逻辑测试。
    /// 数值对标 INKWAVE character.js L3550-3560（游动姿态）与 L3559/L3611（触须 wigAmp/wigFreq）。
    /// </summary>
    public class SwimPoseSolverTests
    {
        private static CharacterAnimConfigSO CreateConfig()
        {
            return ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
        }

        [Test]
        public void Speed01_ClampsAndNormalizes()
        {
            Assert.AreEqual(0f, SwimPoseSolver.Speed01(0f, 11.8f), 0.001f);
            Assert.AreEqual(1f, SwimPoseSolver.Speed01(11.8f, 11.8f), 0.001f);
            Assert.AreEqual(1f, SwimPoseSolver.Speed01(30f, 11.8f), 0.001f, "超速应钳到 1");
            Assert.AreEqual(0.5f, SwimPoseSolver.Speed01(5.9f, 11.8f), 0.01f);
        }

        [Test]
        public void UndulationFreq_LerpsBetweenSlowAndFast()
        {
            var cfg = CreateConfig();
            Assert.AreEqual(cfg.swimUndulationFreqSlow, SwimPoseSolver.UndulationFreq(0f, cfg.swimUndulationFreqSlow, cfg.swimUndulationFreqFast), 0.01f);
            Assert.AreEqual(cfg.swimUndulationFreqFast, SwimPoseSolver.UndulationFreq(1f, cfg.swimUndulationFreqSlow, cfg.swimUndulationFreqFast), 0.01f);
            Assert.AreEqual(Mathf.Lerp(5f, 14f, 0.5f), SwimPoseSolver.UndulationFreq(0.5f, 5f, 14f), 0.01f);
        }

        [Test]
        public void Sample_FullSpeed_MatchesInkwaveVolumePreservingStretch()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            SwimPoseSolver.SwimPose pose = SwimPoseSolver.Sample(1f, 0.5f, 0f, cfg);

            Assert.AreEqual(1.2f, pose.StretchZ, 0.01f, "全速纵向拉伸 1+0.2（Inkwave sy = 1+0.2sv）");
            Assert.AreEqual(1f / Mathf.Sqrt(1.2f), pose.StretchXZ, 0.001f, "横向 1/√sy 保体积");
            Assert.LessOrEqual(Mathf.Abs(pose.PitchDeg), cfg.swimUndulationPitchDeg + 0.01f);
            Assert.AreEqual(0f, pose.RollDeg, 0.01f, "直线游动无侧倾");
        }

        [Test]
        public void Sample_Banking_ClampsToConfiguredMax()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            // yawRate 单位为度/秒：5729°/s ≈ 100 rad/s 的极端急转应钳到上限
            SwimPoseSolver.SwimPose pose = SwimPoseSolver.Sample(1f, 0f, yawRateDeg: 5729f, cfg);
            Assert.AreEqual(-cfg.swimBankMaxDeg, pose.RollDeg, 0.01f, "极速急转应钳到侧倾上限（负号 = 内倾压弯）");

            // 57.3°/s ≈ 1 rad/s 的缓转：按 0.08 系数内倾（Inkwave 原值，量纲等价）
            SwimPoseSolver.SwimPose gentle = SwimPoseSolver.Sample(1f, 0f, yawRateDeg: 57.3f, cfg);
            Assert.AreEqual(-57.3f * 0.08f, gentle.RollDeg, 0.1f, "缓转按 0.08 系数内倾（Inkwave 原值）");
        }

        [Test]
        public void TentacleFlap_AmplitudeAndPhaseFollowSpeed()
        {
            CharacterAnimConfigSO cfg = CreateConfig();

            // 选 t 使首根触须 sin(t·freq)=1：flap = amp，验证全速/静止摆幅
            float freqFast = Mathf.Lerp(cfg.tentacleFreqSlow, cfg.tentacleFreqFast, 1f);
            float tPeak = (Mathf.PI * 0.5f) / freqFast;
            SwimPoseSolver.TentacleFlap(1f, tPeak, 0, cfg, out float fastFlap, out _);
            Assert.AreEqual(cfg.tentacleAmpFastDeg, fastFlap, 0.01f, "全速拍水摆幅 = tentacleAmpFastDeg");

            float freqSlow = cfg.tentacleFreqSlow;
            SwimPoseSolver.TentacleFlap(0f, (Mathf.PI * 0.5f) / freqSlow, 0, cfg, out float idleFlap, out _);
            Assert.AreEqual(cfg.tentacleAmpIdleDeg, idleFlap, 0.01f, "近静止摆幅 = tentacleAmpIdleDeg");

            // 相位差传播：同一时刻相邻触须摆角不同（根→尖行波近似）
            SwimPoseSolver.TentacleFlap(1f, tPeak, 1, cfg, out float nextFlap, out _);
            Assert.Greater(fastFlap, nextFlap, "相位差应让相邻触须摆角错开");
            Assert.Greater(nextFlap, 0f, "相位差 0.9rad 时第二根仍应为正摆（cos(0.9)≈0.62）");
        }

        [Test]
        public void TentacleFlap_SideSwayIsProportional()
        {
            CharacterAnimConfigSO cfg = CreateConfig();
            SwimPoseSolver.TentacleFlap(1f, 0.37f, 2, cfg, out float flap, out float side);
            Assert.LessOrEqual(Mathf.Abs(side), cfg.tentacleAmpFastDeg * cfg.tentacleSideSwayFactor + 0.01f);
        }
    }
}
