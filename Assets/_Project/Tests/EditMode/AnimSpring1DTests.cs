using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P0] AnimSpring1D 阻尼振子闭式解纯逻辑回归（2026-11-01）：
    /// 旧实现（半隐式欧拉 + 子步）在 ω·dt 接近 1 时特征值模 &gt; 1，30FPS（ω=30 时 ω·dt=1.0）
    /// 实测指数发散，60/120/240FPS 的同一冲量峰值分别约 0.011 / 0.029 / 0.037（设计值 0.045）。
    /// 本组用例锁定：峰值与帧率无关、任意 dt 稳定、三种阻尼分支（欠/临界/过）都单调收敛到目标。
    /// </summary>
    public class AnimSpring1DTests
    {
        private const float RecoilOmega = 30f;
        private const float RecoilZeta = 0.8f;   // WeaponFeelMath.RecoilDampingRatio
        private const float RecoilPeak = 0.045f;

        /// <summary>冲量后的解析峰值：d(t*) = (v0/ωd)·e^{-ζω·t*}·sin(ωd·t*)，t* = atan(ωd/(ζω))/ωd。</summary>
        private static float AnalyticImpulsePeak(float peakOffset, float omega, float zeta)
        {
            float v0 = AnimSpring1D.ImpulseForPeak(peakOffset, omega, zeta);
            float dampedOmega = omega * Mathf.Sqrt(1f - zeta * zeta);
            float tPeak = Mathf.Atan(dampedOmega / (zeta * omega)) / dampedOmega;
            return v0 / dampedOmega * Mathf.Exp(-zeta * omega * tPeak) * Mathf.Sin(dampedOmega * tPeak);
        }

        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(120f)]
        [TestCase(240f)]
        public void ImpulsePeak_IsFrameRateIndependent_AndMatchesAnalyticValue(float framesPerSecond)
        {
            float step = 1f / framesPerSecond;
            var spring = new AnimSpring1D(0f);
            spring.Kick(AnimSpring1D.ImpulseForPeak(RecoilPeak, RecoilOmega, RecoilZeta));

            float maxValue = 0f;
            int steps = Mathf.CeilToInt(2f / step);
            for (int i = 0; i < steps; i++)
            {
                spring.Step(0f, RecoilOmega, RecoilZeta, step);
                maxValue = Mathf.Max(maxValue, Mathf.Abs(spring.Value));
            }

            float analytic = AnalyticImpulsePeak(RecoilPeak, RecoilOmega, RecoilZeta);
            Assert.AreEqual(analytic, maxValue, analytic * 0.05f,
                $"{framesPerSecond}FPS 采样峰值必须贴合解析峰值（离散采样只允许 5% 误差）");
            Assert.AreEqual(RecoilPeak, maxValue, RecoilPeak * 0.12f,
                $"{framesPerSecond}FPS 的峰值必须仍是 ImpulseForPeak 的设计值 ±12%");
        }

        [Test]
        public void ThirtyFps_ImpulseDoesNotDiverge()
        {
            // 旧实现在此场景（ω·dt = 1.0、ζ=0.8）特征值模 ≈1.13，会指数发散到 NaN/Inf
            const float step = 1f / 30f;
            var spring = new AnimSpring1D(0f);
            spring.Kick(AnimSpring1D.ImpulseForPeak(RecoilPeak, RecoilOmega, RecoilZeta));

            float maxValue = 0f;
            for (int i = 0; i < 150; i++) // 5 秒
            {
                spring.Step(0f, RecoilOmega, RecoilZeta, step);
                Assert.IsFalse(float.IsNaN(spring.Value) || float.IsInfinity(spring.Value),
                    "30FPS 下弹簧不得发散");
                maxValue = Mathf.Max(maxValue, Mathf.Abs(spring.Value));
            }

            Assert.LessOrEqual(maxValue, RecoilPeak * 2f, "峰值不得被放大成原设计的 2 倍以上");
            Assert.Less(Mathf.Abs(spring.Value), 0.001f, "5s 后必须回稳到 0");
        }

        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(120f)]
        [TestCase(240f)]
        public void StepToTarget_ConvergesWithinTwoSeconds_AtAnyFrameRate(float framesPerSecond)
        {
            float step = 1f / framesPerSecond;
            var spring = new AnimSpring1D(0f); // 0 → 1（形态手势语境的单位阶跃）
            int steps = Mathf.CeilToInt(2f / step);
            for (int i = 0; i < steps; i++)
                spring.Step(1f, 18f, 0.35f, step);

            Assert.AreEqual(1f, spring.Value, 1e-3f, $"{framesPerSecond}FPS 下必须收敛到目标");
            Assert.AreEqual(0f, spring.Velocity, 1e-2f, $"{framesPerSecond}FPS 下速度必须归零");
        }

        [Test]
        public void Underdamped_OvershootMatchesAnalyticFormula()
        {
            const float omega = 18f;
            const float zeta = 0.35f;
            var spring = new AnimSpring1D(0f);

            float maxValue = 0f;
            const float dt = 1f / 1000f; // 细步长：逼近连续峰值
            for (int i = 0; i < 2000; i++)
            {
                spring.Step(1f, omega, zeta, dt);
                maxValue = Mathf.Max(maxValue, spring.Value);
            }

            float expectedOvershoot = 1f + Mathf.Exp(-Mathf.PI * zeta / Mathf.Sqrt(1f - zeta * zeta));
            Assert.AreEqual(expectedOvershoot, maxValue, 0.01f,
                "欠阻尼过冲必须等于解析值 1+exp(-πζ/√(1-ζ²))（形态手势的浮出拉伸幅度来源）");
        }

        [Test]
        public void CriticallyDamped_NeverOvershoots_AndSettles()
        {
            var spring = new AnimSpring1D(0f);
            spring.Kick(4f); // 给一个冲量，临界阻尼下也不允许越过目标

            const float dt = 1f / 120f;
            for (int i = 0; i < 240; i++)
            {
                spring.Step(1f, 20f, 1f, dt);
                Assert.LessOrEqual(spring.Value, 1f + 1e-4f, "临界阻尼不得过冲");
            }

            Assert.AreEqual(1f, spring.Value, 1e-3f, "临界阻尼必须收敛到目标");
        }

        [TestCase(1.5f)]
        [TestCase(3f)]
        public void Overdamped_NeverOvershoots_AndSettles(float zeta)
        {
            var spring = new AnimSpring1D(0f);
            spring.Kick(6f);

            const float dt = 1f / 60f;
            for (int i = 0; i < 300; i++)
            {
                spring.Step(1f, 12f, zeta, dt);
                Assert.IsFalse(float.IsNaN(spring.Value) || float.IsInfinity(spring.Value),
                    $"ζ={zeta} 过阻尼分支不得产生 NaN");
                Assert.LessOrEqual(spring.Value, 1f + 1e-3f, $"ζ={zeta} 过阻尼不得过冲");
            }

            Assert.AreEqual(1f, spring.Value, 5e-3f, $"ζ={zeta} 过阻尼必须收敛到目标");
        }

        [Test]
        public void Step_ZeroOrNegativeDt_AndZeroOmega_AreNoOps()
        {
            var spring = new AnimSpring1D(0f);
            spring.Step(1f, 18f, 0.35f, 0f);
            Assert.AreEqual(0f, spring.Value, "dt=0 不得推进");
            Assert.AreEqual(0f, spring.Velocity, "dt=0 不得改变速度");

            spring.Step(1f, 18f, 0.35f, -1f);
            Assert.AreEqual(0f, spring.Value, "负 dt 不得推进");

            spring.Step(1f, 0f, 0.35f, 1f / 60f);
            Assert.AreEqual(0f, spring.Value, "omega=0 不得推进（无弹簧）");
        }

        [Test]
        public void Step_LargeDt_UnderflowsToTarget_WithoutNaN()
        {
            var spring = new AnimSpring1D(0f);
            spring.Step(1f, 30f, 0.8f, 0.25f); // 编辑器卡顿级 dt（ω·dt = 7.5）
            Assert.IsFalse(float.IsNaN(spring.Value) || float.IsInfinity(spring.Value));
            Assert.AreEqual(1f, spring.Value, 0.05f, "超大 dt 应安全贴近目标，而不是放大成震荡");
            Assert.Less(Mathf.Abs(spring.Velocity), 5f, "超大 dt 下速度不得被放大");

            var far = new AnimSpring1D(0f);
            far.Step(1f, 30f, 0.8f, 1000f);
            Assert.AreEqual(1f, far.Value, 1e-4f, "极端 dt 应直接下溢到目标值");
            Assert.AreEqual(0f, far.Velocity, 1e-4f, "极端 dt 应把速度一并归零");
        }

        [Test]
        public void Step_MidFlightTargetFlip_StaysContinuous()
        {
            var spring = new AnimSpring1D(0f);
            for (int i = 0; i < 12; i++) spring.Step(1f, 18f, 0.35f, 1f / 60f);

            float maxStep = 0f;
            float previous = spring.Value;
            for (int i = 0; i < 120; i++)
            {
                spring.Step(0f, 18f, 0.35f, 1f / 60f);
                maxStep = Mathf.Max(maxStep, Mathf.Abs(spring.Value - previous));
                previous = spring.Value;
            }

            Assert.LessOrEqual(maxStep, 0.35f, "翻转目标后单帧变化必须与弹簧速度×dt 同量级（不得瞬移）");
            Assert.AreEqual(0f, spring.Value, 0.01f, "最终应回稳到新目标");
        }

        [Test]
        public void KickAndReset_BehaveAsDocumented()
        {
            var spring = new AnimSpring1D(0.5f);
            spring.Kick(2f);
            Assert.AreEqual(2f, spring.Velocity, 1e-5f, "Kick 只叠加速度冲量，不改当前值");
            Assert.AreEqual(0.5f, spring.Value, 1e-5f);

            spring.Reset();
            Assert.AreEqual(0f, spring.Value, 1e-5f);
            Assert.AreEqual(0f, spring.Velocity, 1e-5f);
        }
    }
}
