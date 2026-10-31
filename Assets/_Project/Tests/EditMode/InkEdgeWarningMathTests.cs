using NUnit.Framework;
using P1.UI;

namespace P1.Tests
{
    /// <summary>
    /// P18-A 踩敌墨全屏边缘墨迹的纯逻辑验收：
    /// 强度指数逼近（淡入快 / 淡出慢）、阈值显隐、样式默认值自洽。
    /// 无场景依赖，EditMode 秒级运行。
    /// </summary>
    public class InkEdgeWarningMathTests
    {
        [Test]
        public void StepIntensity_FadesInFasterThanItFadesOut()
        {
            float value = 0f;
            for (int i = 0; i < 5; i++)
            {
                value = InkEdgeWarningMath.StepIntensity(value, true, 9f, 3f, 0.1f);
            }

            Assert.Greater(value, 0.98f, "9/s 淡入 0.5s 应基本拉满");

            float entered = value;
            value = InkEdgeWarningMath.StepIntensity(value, false, 9f, 3f, 0.2f);
            Assert.Less(value, entered, "离开敌墨后应立即开始衰减");
            Assert.Greater(value, 0.45f, "0.2s 内不应消失（淡出 3/s 比淡入慢）");

            for (int i = 0; i < 20; i++)
            {
                value = InkEdgeWarningMath.StepIntensity(value, false, 9f, 3f, 0.1f);
            }

            Assert.Less(value, 0.01f, "约 2s 后应基本归零");
        }

        [Test]
        public void StepIntensity_ClampsAndIgnoresZeroDelta()
        {
            Assert.AreEqual(0f, InkEdgeWarningMath.StepIntensity(0f, false, 9f, 3f, 0.1f), 0.0001f);
            Assert.AreEqual(1f, InkEdgeWarningMath.StepIntensity(1f, true, 9f, 3f, 0.1f), 0.0001f);
            Assert.AreEqual(1f, InkEdgeWarningMath.StepIntensity(1.4f, true, 9f, 3f, 0.1f), 0.0001f,
                "输入超界应先 Clamp 再逼近");

            float hold = InkEdgeWarningMath.StepIntensity(0.4f, true, 9f, 3f, 0f);
            Assert.AreEqual(0.4f, hold, 0.0001f, "dt=0（暂停/首帧）不应跳变");

            hold = InkEdgeWarningMath.StepIntensity(0.4f, true, 9f, 3f, -1f);
            Assert.AreEqual(0.4f, hold, 0.0001f, "负 dt 不应改变强度");
        }

        [Test]
        public void IsVisible_UsesThreshold()
        {
            Assert.IsFalse(InkEdgeWarningMath.IsVisible(0f, 0.003f));
            Assert.IsFalse(InkEdgeWarningMath.IsVisible(0.003f, 0.003f), "等于阈值时应关闭 Graphic");
            Assert.IsTrue(InkEdgeWarningMath.IsVisible(0.02f, 0.003f));
        }

        [Test]
        public void StyleFallback_HasSaneDefaults()
        {
            InkEdgeWarningStyleSO style = InkEdgeWarningStyleSO.Fallback;

            Assert.IsNotNull(style);
            Assert.Greater(style.fadeInRate, style.fadeOutRate, "淡入必须比淡出快");
            Assert.Greater(style.bottomBand, style.edgeBand, "底边墨带应明显厚于侧边细线");
            Assert.That(style.visibleThreshold, Is.InRange(0f, 0.05f));
            Assert.That(style.maxAlpha, Is.InRange(0.1f, 1f));
        }
    }
}
