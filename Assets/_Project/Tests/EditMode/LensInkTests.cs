using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 镜头墨滴（LensInk）纯数学与模拟逻辑的 EditMode 验收：
    /// 1. Metaball 表面张力势能评估与距离场融合；
    /// 2. 重力下滑与粘滞卡顿（小微滴卡住不动、大滴重力更大）；
    /// 3. 末段寿命挥发收缩；
    /// 4. 边缘着墨点几何投影与主角区域规避。
    /// </summary>
    public class LensInkTests
    {
        [Test]
        public void EvaluateInfluence_AtCenter_ReturnsOne()
        {
            Vector2 pos = new Vector2(0.5f, 0.5f);
            float inf = LensInkMath.EvaluateInfluence(pos, pos, 0.05f, 0.05f, 1.777f);
            Assert.AreEqual(1f, inf, 0.0001f, "液滴中心势能贡献必须为 1.0");
        }

        [Test]
        public void EvaluateInfluence_OutsideRadius_ReturnsZero()
        {
            Vector2 pos = new Vector2(0.5f, 0.5f);
            Vector2 outside = new Vector2(0.8f, 0.5f);
            float inf = LensInkMath.EvaluateInfluence(outside, pos, 0.05f, 0.05f, 1.777f);
            Assert.AreEqual(0f, inf, 0.0001f, "超出势能半径的影响必须严格为 0");
        }

        [Test]
        public void EvaluateInfluence_MetaballAdditiveSuperposition_MergesTwoDrops()
        {
            Vector2 p1 = new Vector2(0.5f, 0.5f);
            Vector2 p2 = new Vector2(0.53f, 0.5f);
            Vector2 mid = new Vector2(0.515f, 0.5f);

            float inf1 = LensInkMath.EvaluateInfluence(mid, p1, 0.05f, 0.05f, 1.777f);
            float inf2 = LensInkMath.EvaluateInfluence(mid, p2, 0.05f, 0.05f, 1.777f);
            float combined = inf1 + inf2;

            Assert.Greater(combined, inf1, "两颗邻近液滴在鞍点处应形成势能叠加融合");
            Assert.Greater(combined, inf2, "两颗邻近液滴在鞍点处应形成势能叠加融合");
        }

        [Test]
        public void CalculateSlideGravity_HeavyDropsFallFasterThanLightDrops()
        {
            float gLarge = LensInkMath.CalculateSlideGravity(0.08f, 1f, 0f);
            float gSmall = LensInkMath.CalculateSlideGravity(0.02f, 1f, 0f);

            Assert.Greater(gLarge, gSmall, "大半径重液滴的下滑重力加速度必须严格大于轻液滴");
        }

        [Test]
        public void CalculateSlideGravity_HoldingDropHasZeroGravity()
        {
            float gHold = LensInkMath.CalculateSlideGravity(0.08f, 1f, 0.15f);
            Assert.AreEqual(0f, gHold, 0.0001f, "处于 stick-slip 卡滞暂停期的液滴重力加速度必须为 0");
        }

        [Test]
        public void ShouldSlide_SmallDropletsStayStuck()
        {
            // 半径小于 0.012 的微滴受毛细表面张力主导，即使时间已过 stick 也不下滑
            bool slideMicro = LensInkMath.ShouldSlide(false, 5.0f, 0.2f, 0.008f);
            Assert.IsFalse(slideMicro, "微小墨滴应牢牢卡在镜头玻璃上，不发生重力下滑");

            bool slideLarge = LensInkMath.ShouldSlide(false, 0.5f, 0.2f, 0.05f);
            Assert.IsTrue(slideLarge, "超过粘附时间的大液滴必须开始滑动");
        }

        [Test]
        public void StepIntensity_EvaporatesAfterLifetime()
        {
            float full = LensInkMath.StepIntensity(0.5f, 1.0f, 1.3f, false);
            Assert.AreEqual(1.3f, full, 0.0001f, "寿命期内应维持初始强度");

            float halfFade = LensInkMath.StepIntensity(1.275f, 1.0f, 1.3f, false);
            Assert.That(halfFade, Is.InRange(0.4f, 0.8f), "寿命结束后的收缩挥发期强度应平滑递减");

            float expired = LensInkMath.StepIntensity(2.0f, 1.0f, 1.3f, false);
            Assert.AreEqual(0f, expired, 0.0001f, "挥发周期结束后强度必须归零");
        }

        [Test]
        public void EdgePoint_AvoidsBottomCenterPlayerModel()
        {
            // 向下正中央 (-90 度)
            float angleDown = -Mathf.PI * 0.5f;
            Vector2 edge = LensInkMath.EdgePoint(angleDown, 0.05f, 1.777f);

            Assert.That(edge.y, Is.LessThan(0.5f), "向下边缘 y 应在下半屏");
            Assert.IsTrue(Mathf.Abs(edge.x - 0.5f) > 0.15f, "边缘着墨点必须偏离底部中央 (0.5)，避免遮挡主角模型");
        }

        [Test]
        public void EdgePoint_ClampedInsideScreenBounds()
        {
            for (int i = 0; i < 16; i++)
            {
                float ang = (i / 16f) * Mathf.PI * 2f;
                Vector2 p = LensInkMath.EdgePoint(ang, 0.04f, 1.777f);
                Assert.That(p.x, Is.InRange(0.019f, 0.981f));
                Assert.That(p.y, Is.InRange(0.029f, 0.971f));
            }
        }
    }
}
