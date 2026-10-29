using NUnit.Framework;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P4-B 顶部战况条与中央墨滴计时器的纯逻辑单元测试：
    /// 分秒换算、阶段判定、复活环进度、比分分割比、头像三态判定。
    /// 全部不启动 GameObject / Canvas，零 GC 分配，极速执行。
    /// </summary>
    public class TopRosterMathTests
    {
        [Test]
        public void SplitTime_CalculatesMinutesAndSecondsCorrectly()
        {
            var (m1, s1) = TopRosterMath.SplitTime(120f);
            Assert.AreEqual(2, m1);
            Assert.AreEqual(0, s1);

            var (m2, s2) = TopRosterMath.SplitTime(59.2f);
            Assert.AreEqual(1, m2);
            Assert.AreEqual(0, s2); // 59.2 ceil 之后是 60s -> 1分0秒

            var (m3, s3) = TopRosterMath.SplitTime(59.0f);
            Assert.AreEqual(0, m3);
            Assert.AreEqual(59, s3);

            var (m4, s4) = TopRosterMath.SplitTime(0f);
            Assert.AreEqual(0, m4);
            Assert.AreEqual(0, s4);
        }

        [Test]
        public void FormatTimerString_ProducesPaddedTwoDigitFormat()
        {
            Assert.AreEqual("02:00", TopRosterMath.FormatTimerString(120f));
            Assert.AreEqual("01:00", TopRosterMath.FormatTimerString(60f));
            Assert.AreEqual("00:59", TopRosterMath.FormatTimerString(59f));
            Assert.AreEqual("00:07", TopRosterMath.FormatTimerString(7f));
            Assert.AreEqual("00:00", TopRosterMath.FormatTimerString(0f));
        }

        [Test]
        public void GetTimerPhase_DistinguishesNormalLastMinuteAndFinalThirty()
        {
            Assert.AreEqual(TimerPhase.Normal, TopRosterMath.GetTimerPhase(120f));
            Assert.AreEqual(TimerPhase.Normal, TopRosterMath.GetTimerPhase(60.1f));

            Assert.AreEqual(TimerPhase.LastMinute, TopRosterMath.GetTimerPhase(60f));
            Assert.AreEqual(TimerPhase.LastMinute, TopRosterMath.GetTimerPhase(45f));
            Assert.AreEqual(TimerPhase.LastMinute, TopRosterMath.GetTimerPhase(30.1f));

            Assert.AreEqual(TimerPhase.FinalThirty, TopRosterMath.GetTimerPhase(30f));
            Assert.AreEqual(TimerPhase.FinalThirty, TopRosterMath.GetTimerPhase(15f));
            Assert.AreEqual(TimerPhase.FinalThirty, TopRosterMath.GetTimerPhase(0f));
        }

        [Test]
        public void CalculateRespawnProgress_FillsFromZeroToOne()
        {
            const float duration = 4.0f;

            // 刚阵亡，剩余 4s -> 进度 0%
            Assert.AreEqual(0f, TopRosterMath.CalculateRespawnProgress(4.0f, duration), 0.001f);

            // 复活中，剩余 2s -> 进度 50%
            Assert.AreEqual(0.5f, TopRosterMath.CalculateRespawnProgress(2.0f, duration), 0.001f);

            // 即将复活，剩余 0s -> 进度 100%
            Assert.AreEqual(1.0f, TopRosterMath.CalculateRespawnProgress(0f, duration), 0.001f);

            // 极端参数夹紧保护
            Assert.AreEqual(0f, TopRosterMath.CalculateRespawnProgress(10f, duration), 0.001f);
            Assert.AreEqual(1f, TopRosterMath.CalculateRespawnProgress(-1f, duration), 0.001f);
            Assert.AreEqual(1f, TopRosterMath.CalculateRespawnProgress(1f, 0f), 0.001f);
        }

        [Test]
        public void CalculateScoreSplitRatio_SplitsAndClampsEdgeValues()
        {
            // 势均力敌
            Assert.AreEqual(0.5f, TopRosterMath.CalculateScoreSplitRatio(50f, 50f), 0.001f);
            Assert.AreEqual(0.5f, TopRosterMath.CalculateScoreSplitRatio(0f, 0f), 0.001f);

            // 正常比例 40% vs 60% -> ratio = 0.4
            Assert.AreEqual(0.4f, TopRosterMath.CalculateScoreSplitRatio(40f, 60f), 0.001f);

            // 极端单边压制：夹紧在 [0.05, 0.95]
            Assert.AreEqual(0.05f, TopRosterMath.CalculateScoreSplitRatio(0f, 100f), 0.001f);
            Assert.AreEqual(0.95f, TopRosterMath.CalculateScoreSplitRatio(100f, 0f), 0.001f);
        }

        [Test]
        public void DetermineSlotState_ReflectsAliveDeadAndSpecialReady()
        {
            // 存活普通
            Assert.AreEqual(AvatarSlotState.NormalAlive, TopRosterMath.DetermineSlotState(isAlive: true, isSpecialReady: false));

            // 存活大招就绪
            Assert.AreEqual(AvatarSlotState.SpecialReady, TopRosterMath.DetermineSlotState(isAlive: true, isSpecialReady: true));

            // 死亡状态优先（无论大招是否准备好，死后都是阵亡倒计时）
            Assert.AreEqual(AvatarSlotState.DeadRespawning, TopRosterMath.DetermineSlotState(isAlive: false, isSpecialReady: false));
            Assert.AreEqual(AvatarSlotState.DeadRespawning, TopRosterMath.DetermineSlotState(isAlive: false, isSpecialReady: true));
        }

        [Test]
        public void DidCrossSixtySeconds_DetectsCrossingFrameOnly()
        {
            Assert.IsTrue(TopRosterMath.DidCrossSixtySeconds(60.1f, 59.9f));
            Assert.IsTrue(TopRosterMath.DidCrossSixtySeconds(60.5f, 60.0f));

            Assert.IsFalse(TopRosterMath.DidCrossSixtySeconds(59.9f, 59.5f));
            Assert.IsFalse(TopRosterMath.DidCrossSixtySeconds(80f, 75f));
            Assert.IsFalse(TopRosterMath.DidCrossSixtySeconds(30f, 29f));
        }
    }
}
