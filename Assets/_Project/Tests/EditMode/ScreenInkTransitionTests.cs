using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 全屏墨水转场与受击冲击的纯数学逻辑验收：
    /// 1. 阵亡敌墨四周漫灌序列（涌入 -> 维持全屏 -> 留挂垂滴）；
    /// 2. 复活虹膜撕裂开屏（破口单调扩张直至完全出屏）；
    /// 3. 受击形变弹簧临界阻尼稳定性；
    /// 4. 径向色差（Chroma）单调指数衰减；
    /// 5. 边缘粘液带底边厚度与垂滴动态伸长。
    /// </summary>
    public class ScreenInkTransitionTests
    {
        [Test]
        public void CalculateFloodCoverage_SurgesInThenHoldsFullCover()
        {
            Assert.AreEqual(0f, ScreenInkTransitionMath.CalculateFloodCoverage(0f), 0.0001f);

            float early = ScreenInkTransitionMath.CalculateFloodCoverage(0.12f);
            Assert.That(early, Is.InRange(0.3f, 0.7f), "漫灌前半程应平滑涌入");

            float full = ScreenInkTransitionMath.CalculateFloodCoverage(0.24f);
            Assert.AreEqual(1f, full, 0.001f, "0.24s 时必须涌满全屏 (1.0)");

            float hold = ScreenInkTransitionMath.CalculateFloodCoverage(0.40f);
            Assert.AreEqual(1f, hold, 0.001f, "0.24s~0.5s 必须保持全屏压制");

            float recede = ScreenInkTransitionMath.CalculateFloodCoverage(1.3f);
            Assert.That(recede, Is.InRange(0.15f, 0.35f), "阵亡观战期回退至边缘带");
        }

        [Test]
        public void CalculateIrisHole_ExpandsMonotonicallyUntilClearingScreen()
        {
            float lastHole = -1f;

            for (float t = 0.10f; t <= 0.85f; t += 0.05f)
            {
                float hole = ScreenInkTransitionMath.CalculateIrisHole(t);
                Assert.GreaterOrEqual(hole, lastHole, "复活虹膜破口孔径必须单调递增，不得发生收缩颠簸");
                lastHole = hole;
            }

            Assert.Greater(lastHole, 1.25f, "展开终点必须超出 1.25，完全覆盖 16:9 屏幕角落以放行 HUD 与画面");
        }

        [Test]
        public void StepPunchSpring_DampsSmoothlyToZero()
        {
            float punch = 0f;
            float punchV = -0.04f;

            for (int i = 0; i < 40; i++)
            {
                ScreenInkTransitionMath.StepPunchSpring(ref punch, ref punchV, 0.016f);
            }

            Assert.That(Mathf.Abs(punch), Is.LessThan(0.005f), "约 0.6s 后受击形变弹簧必须基本归零");
            Assert.That(Mathf.Abs(punchV), Is.LessThan(0.01f), "弹簧速度必须衰减收敛");
        }

        [Test]
        public void StepChroma_DecaysMonotonicallyToZero()
        {
            float chroma = 1.0f;
            bool strictlyDecreased = false;

            for (int i = 0; i < 10; i++)
            {
                float next = ScreenInkTransitionMath.StepChroma(chroma, 0.05f);
                // 允许提前归零后保持 0（非增），但窗口内必须至少严格衰减过一次
                Assert.LessOrEqual(next, chroma, "色相分离度必须单调不增");
                if (next < chroma) strictlyDecreased = true;
                chroma = next;
            }

            Assert.IsTrue(strictlyDecreased, "色相分离度必须发生实际衰减");

            for (int i = 0; i < 20; i++)
            {
                chroma = ScreenInkTransitionMath.StepChroma(chroma, 0.05f);
            }

            Assert.AreEqual(0f, chroma, 0.0001f, "衰减周期结束后 Chroma 必须严格归零");
        }

        [Test]
        public void InkEdgeWarningMath_BottomBandIsThickerThanEdgeBand()
        {
            float bottomWeight = InkEdgeWarningMath.CalculateBottomWeight(0.02f);
            float topWeight = InkEdgeWarningMath.CalculateBottomWeight(0.95f);

            Assert.Greater(bottomWeight, 0.9f, "屏幕底部加权权重应接近 1.0");
            Assert.AreEqual(0f, topWeight, 0.001f, "屏幕顶部底边加权权重必须为 0");

            float bandBottom = InkEdgeWarningMath.CalculateBandWidth(1.0f, 0.0025f, 0.078f, bottomWeight);
            float bandTop = InkEdgeWarningMath.CalculateBandWidth(1.0f, 0.0025f, 0.078f, topWeight);

            Assert.Greater(bandBottom, bandTop * 5f, "底边粘液带厚度必须显著大于侧边与顶边细线");
        }

        [Test]
        public void InkEdgeWarningMath_DripLengthGrowsWithIntensity()
        {
            float dripLow = InkEdgeWarningMath.CalculateDripLength(0.2f, 0.55f, 1.0f);
            float dripHigh = InkEdgeWarningMath.CalculateDripLength(0.9f, 0.55f, 1.0f);

            Assert.Greater(dripHigh, dripLow * 3f, "垂滴长度必须随踩墨强度的增加而伸长");
        }
    }
}
