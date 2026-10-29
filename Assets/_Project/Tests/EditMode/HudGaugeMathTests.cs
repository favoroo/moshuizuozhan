using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P4-C 液面几何与低墨判定的纯逻辑验收：液面映射、圆形/圆角截面收边、
    /// 波浪幅度上限与告警迟滞，全部不启动 Canvas。
    /// </summary>
    public class HudGaugeMathTests
    {
        [Test]
        public void SurfaceBase_MapsFillAcrossContainerHeight()
        {
            Assert.AreEqual(-90f, HudGaugeMath.SurfaceBase(-90f, 180f, 0f), 0.001f, "空罐液面应贴底");
            Assert.AreEqual(0f, HudGaugeMath.SurfaceBase(-90f, 180f, 0.5f), 0.001f, "半罐液面应在中部");
            Assert.AreEqual(90f, HudGaugeMath.SurfaceBase(-90f, 180f, 1f), 0.001f, "满罐液面应齐顶");
        }

        [Test]
        public void SurfaceBase_ClampsOutOfRangeFill()
        {
            Assert.AreEqual(-90f, HudGaugeMath.SurfaceBase(-90f, 180f, -3f), 0.001f);
            Assert.AreEqual(90f, HudGaugeMath.SurfaceBase(-90f, 180f, 4f), 0.001f);
        }

        [Test]
        public void CircleColumn_HalfHeightPeaksAtCenterAndVanishesOutside()
        {
            const float halfW = 50f;
            const float halfH = 50f;

            Assert.AreEqual(50f, HudGaugeMath.CircleColumnHalfHeight(100f, 100f, halfW, halfH), 0.001f,
                "圆心处列高应等于半径");
            Assert.AreEqual(0f, HudGaugeMath.CircleColumnHalfHeight(150f, 100f, halfW, halfH), 0.001f,
                "贴边处列高应收敛到 0");
            Assert.Less(HudGaugeMath.CircleColumnHalfHeight(151f, 100f, halfW, halfH), 0f,
                "圆外的列应返回负值被跳过");

            float quarter = HudGaugeMath.CircleColumnHalfHeight(125f, 100f, halfW, halfH);
            Assert.AreEqual(Mathf.Sqrt(50f * 50f - 25f * 25f), quarter, 0.01f, "半高应符合圆弧解析解");
        }

        [Test]
        public void RoundedRectBottomInset_OnlyLiftsInsideCorners()
        {
            const float halfW = 30f;
            const float radius = 10f;

            Assert.AreEqual(0f, HudGaugeMath.RoundedRectBottomInset(100f, 100f, halfW, radius), 0.001f,
                "中央直边区不应抬升");
            Assert.AreEqual(0f, HudGaugeMath.RoundedRectBottomInset(120f, 100f, halfW, radius), 0.001f,
                "圆角起点处仍未抬升");

            float atCorner = HudGaugeMath.RoundedRectBottomInset(120f + 5f, 100f, halfW, radius);
            Assert.Greater(atCorner, 0f, "进入圆角后底边应向上收");
            Assert.Less(atCorner, radius, "圆角内收量不应超过半径");

            Assert.AreEqual(radius, HudGaugeMath.RoundedRectBottomInset(131f, 100f, halfW, radius), 0.001f,
                "超出圆角范围应钳制到半径");
        }

        [Test]
        public void WaveOffset_StaysWithinAmplitude()
        {
            const float amplitude = 5f;
            float maxSeen = 0f;

            for (int i = 0; i < 400; i++)
            {
                float x = -60f + i * 0.3f;
                for (int p = 0; p < 12; p++)
                {
                    float offset = HudGaugeMath.WaveOffset(x, 0f, 120f, p * 0.7f, 1.35f, amplitude);
                    maxSeen = Mathf.Max(maxSeen, Mathf.Abs(offset));
                    Assert.LessOrEqual(Mathf.Abs(offset), amplitude + 0.0001f,
                        "液面起伏必须被幅度钳制，否则会穿过容器边界");
                }
            }

            Assert.Greater(maxSeen, amplitude * 0.6f, "波浪应确实有可见幅度，而不是被压成一条直线");
        }

        [Test]
        public void IsLowInk_UsesHysteresisToAvoidFlapping()
        {
            Assert.IsTrue(HudGaugeMath.IsLowInk(0.18f, 0.2f, false), "跌破阈值应进入告警");

            Assert.IsTrue(HudGaugeMath.IsLowInk(0.21f, 0.2f, true, 0.03f),
                "回升不超过阈值+迟滞时应保持告警，避免红闪抖动");
            Assert.IsFalse(HudGaugeMath.IsLowInk(0.21f, 0.2f, false, 0.03f),
                "首次判定高于阈值不应告警");

            Assert.IsFalse(HudGaugeMath.IsLowInk(0.24f, 0.2f, true, 0.03f), "超过迟滞带应解除告警");
        }

        [Test]
        public void CrossedReady_FiresOnlyOnTheCrossingFrame()
        {
            Assert.IsTrue(HudGaugeMath.CrossedReady(1f, false));
            Assert.IsFalse(HudGaugeMath.CrossedReady(1f, true), "已满能时不应重复爆发");
            Assert.IsFalse(HudGaugeMath.CrossedReady(0.98f, false), "未满能不应触发");
        }

        [Test]
        public void ThresholdOffset_MapsThresholdIntoInnerHeight()
        {
            Assert.AreEqual(0f, HudGaugeMath.ThresholdOffset(172f, 0f), 0.001f);
            Assert.AreEqual(86f, HudGaugeMath.ThresholdOffset(172f, 0.5f), 0.001f);
            Assert.AreEqual(172f, HudGaugeMath.ThresholdOffset(172f, 1f), 0.001f);
            Assert.AreEqual(172f, HudGaugeMath.ThresholdOffset(172f, 3f), 0.001f, "超范围应钳制");
        }
    }
}
