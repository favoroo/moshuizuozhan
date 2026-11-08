using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 潜入墨水屏幕浸染的纯数学与 Shader 结构验收：
    /// 1. 强度按对称速率指数平滑（INKWAVE damp λ=7），dt≤0 保持原值；
    /// 2. 低于阈值判不可见（空闲关 Graphic）；
    /// 3. SubmergedInk Shader 必须存在且为双 Pass（浸染乘法 + 焦散加法）。
    /// </summary>
    [Category("UI")]
    public class SubmergedInkTests
    {
        [Test]
        public void StepIntensity_ConvergesToTargetSymmetrically()
        {
            float v = 0f;
            for (int i = 0; i < 60; i++)
            {
                v = SubmergedInkMath.StepIntensity(v, 1f, 0.05f);
            }

            Assert.Greater(v, 0.99f, "持续潜入应快速收敛到 1");

            float fadeOut = v;
            for (int i = 0; i < 60; i++)
            {
                fadeOut = SubmergedInkMath.StepIntensity(fadeOut, 0f, 0.05f);
            }

            Assert.Less(fadeOut, 0.01f, "浮出后应对称快速退场");
        }

        [Test]
        public void StepIntensity_NonPositiveDt_HoldsValueAndClamps()
        {
            Assert.AreEqual(0.42f, SubmergedInkMath.StepIntensity(0.42f, 1f, 0f), 1e-5f, "dt=0 应保持原值");
            Assert.AreEqual(0.42f, SubmergedInkMath.StepIntensity(0.42f, 1f, -1f), 1e-5f, "负 dt 应保持原值");
            Assert.AreEqual(1f, SubmergedInkMath.StepIntensity(1.7f, 1f, 0.1f), 1e-5f, "超出 1 的值应被钳制");
            Assert.AreEqual(0f, SubmergedInkMath.StepIntensity(-0.2f, 0f, 0.1f), 1e-5f, "低于 0 的值应被钳制");
        }

        [Test]
        public void IsVisible_UsesThreshold()
        {
            Assert.IsFalse(SubmergedInkMath.IsVisible(0f), "0 强度必须不可见（空闲关 Graphic）");
            Assert.IsFalse(SubmergedInkMath.IsVisible(SubmergedInkMath.VisibleThreshold), "等于阈值仍不可见");
            Assert.IsTrue(SubmergedInkMath.IsVisible(SubmergedInkMath.VisibleThreshold + 0.001f));
            Assert.Greater(SubmergedInkMath.VisibleThreshold, 0f, "阈值必须为正，否则永远关不掉 Graphic");
        }

        [Test]
        public void SubmergedInkShader_ExistsWithTwoPasses()
        {
            Shader shader = Shader.Find("P1/UI/SubmergedInk");
            Assert.IsNotNull(shader, "Resources/SubmergedInk.shader 必须存在（Shader.Find 可查）");
            Assert.AreEqual(2, shader.passCount,
                "必须恰好双 Pass：SubmergedTint（乘法浸染）+ SubmergedCaustics（加法焦散）");
        }
    }
}
