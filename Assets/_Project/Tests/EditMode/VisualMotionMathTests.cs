using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P37] 程序化角色视觉层帧率健壮性回归（2026-11-03）：
    /// 背景 —— 网页试玩反馈「人物的头一直上下跳」且很卡。离线采样 KidGaitSolver 证实：
    /// 4.5~6.5 m/s 时骨盆竖向是 6 步/秒、峰峰 3.5cm 的**削顶方波**（约 40% 相位贴住
    /// kidHipDropMaxMeters 封顶），直接写回 Hips 后整个上半身含头整体 6Hz 上下跳；
    /// WebGL 低帧率下采样点不足，混叠成不规则乱跳；卡顿帧的超大 dt 还会被
    /// _gaitPhase += π·cadence·dt 放大成「姿态瞬间跳到随机相位」。
    /// 本组用例锁定：dt 消毒的边界、帧率归一单调性、低通时间常数只增不减，
    /// 以及「低帧率档对 6Hz 起伏的衰减足够狠」这一设计目标。
    /// </summary>
    public class VisualMotionMathTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void SanitizeFrameDt_PassesNormalFramesAndCapsSpikes()
        {
            Assert.AreEqual(1f / 60f, VisualMotionMath.SanitizeFrameDt(1f / 60f), Tolerance, "正常帧原样透传");
            Assert.AreEqual(1f / 25f, VisualMotionMath.SanitizeFrameDt(1f / 25f), Tolerance,
                "常规掉帧（40ms）不介入：截断会让视觉层时间基准与移动层解耦、脚与位移脱节");
            Assert.AreEqual(VisualMotionMath.MaxVisualDt, VisualMotionMath.SanitizeFrameDt(0.5f), Tolerance,
                "真卡顿（500ms 假帧）按上限截断，不能把步态相位一次推半圈");
            Assert.AreEqual(VisualMotionMath.MaxVisualDt, VisualMotionMath.SanitizeFrameDt(3f), Tolerance);
        }

        [Test]
        public void SanitizeFrameDt_TreatsInvalidInputAsZero()
        {
            Assert.AreEqual(0f, VisualMotionMath.SanitizeFrameDt(0f), Tolerance);
            Assert.AreEqual(0f, VisualMotionMath.SanitizeFrameDt(-0.02f), Tolerance);
            Assert.AreEqual(0f, VisualMotionMath.SanitizeFrameDt(float.NaN), Tolerance, "NaN 不能污染姿态写回");
            Assert.AreEqual(0f, VisualMotionMath.SanitizeFrameDt(float.PositiveInfinity), Tolerance);
        }

        [Test]
        public void FrameRate01_IsMonotonicAndClampedAtBothEnds()
        {
            Assert.AreEqual(0f, VisualMotionMath.FrameRate01(1f / 10f), Tolerance, "10FPS 属于低帧率档");
            Assert.AreEqual(0f, VisualMotionMath.FrameRate01(1f / VisualMotionMath.BobSmoothLowFps), Tolerance,
                "恰好落在低帧率端点时取 0（保守平滑）");
            Assert.AreEqual(1f, VisualMotionMath.FrameRate01(1f / 144f), Tolerance, "144FPS 属于高帧率档");
            Assert.AreEqual(1f, VisualMotionMath.FrameRate01(1f / VisualMotionMath.BobSmoothHighFps), Tolerance);

            float previous = -1f;
            for (float fps = 8f; fps <= 120f; fps += 2f)
            {
                float value = VisualMotionMath.FrameRate01(1f / fps);
                Assert.GreaterOrEqual(value, previous, $"帧率越高归一值不得回退（fps={fps}）");
                Assert.That(value, Is.InRange(0f, 1f));
                previous = value;
            }
        }

        [Test]
        public void BobSmoothTime_GrowsAsFrameRateDrops_AndNeverShrinksBelowFastEnd()
        {
            var cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            try
            {
                float fast = VisualMotionMath.BobSmoothTime(1f / 60f, cfg.hipsBobSmoothTime, cfg.hipsBobSmoothTimeLowFps);
                float mid = VisualMotionMath.BobSmoothTime(1f / 20f, cfg.hipsBobSmoothTime, cfg.hipsBobSmoothTimeLowFps);
                float low = VisualMotionMath.BobSmoothTime(1f / VisualMotionMath.BobSmoothLowFps,
                    cfg.hipsBobSmoothTime, cfg.hipsBobSmoothTimeLowFps);

                Assert.AreEqual(cfg.hipsBobSmoothTime, fast, Tolerance, "60FPS 走正常档");
                Assert.AreEqual(cfg.hipsBobSmoothTimeLowFps, low, Tolerance, "低帧率端点走最平滑档");
                Assert.That(mid, Is.GreaterThan(fast), "20FPS 已在下探区间内，平滑时间必须更长");
                Assert.That(mid, Is.LessThanOrEqualTo(cfg.hipsBobSmoothTimeLowFps));
                Assert.That(cfg.hipsBobSmoothTimeLowFps, Is.GreaterThan(cfg.hipsBobSmoothTime),
                    "配置两端不得相等或颠倒");
                Assert.That(cfg.hipsBobSmoothTime, Is.GreaterThan(0f));

                // 参数颠倒 / 非法值兜底：不得返回 <= 0（SmoothDamp 会退化成直通甚至异常）
                float swapped = VisualMotionMath.BobSmoothTime(1f / 20f, 0.3f, 0.05f);
                Assert.That(swapped, Is.GreaterThanOrEqualTo(0.05f));
                Assert.That(VisualMotionMath.BobSmoothTime(1f / 20f, 0f, 0f), Is.GreaterThan(0f));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void BobSmoothTime_LowFrameRateBranchAttenuatesStepFrequencyBob()
        {
            // 6 步/秒是 4.5m/s 以上 KidGaitSolver.CadenceFromGait 的触地频率（0.433m 腿长 + 42° 摆幅
            // 下峰值匹配律的必然结果）；一阶低通对正弦的稳态增益 = 1/√(1+(2πfτ)²)。
            var cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            try
            {
                float tau = VisualMotionMath.BobSmoothTime(1f / 20f, cfg.hipsBobSmoothTime, cfg.hipsBobSmoothTimeLowFps);
                Assert.Less(Attenuation(6f, tau), 0.25f,
                    "低帧率档要把 6Hz 骨盆起伏压掉 75% 以上，否则仍会读成「头一直上下跳」");
                Assert.Less(Attenuation(6f, tau), Attenuation(6f, cfg.hipsBobSmoothTime),
                    "低帧率档必须比正常档更狠");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(cfg);
            }
        }

        /// <summary>一阶低通（时间常数 τ）对频率 f 正弦的稳态幅值增益。</summary>
        private static float Attenuation(float frequencyHz, float tau)
        {
            float omegaTau = 2f * Mathf.PI * frequencyHz * tau;
            return 1f / Mathf.Sqrt(1f + omegaTau * omegaTau);
        }
    }
}
