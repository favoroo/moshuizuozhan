using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P6-A 打击反馈纯逻辑验收：Hitstop 时间线的定格/弹簧恢复、
    /// 连击颜色加温、打击标记尺寸、受击方向角与连击音高。
    /// 全部无场景依赖，EditMode 秒级运行。
    /// </summary>
    public class HitFeedbackTests
    {
        [Test]
        public void Hitstop_FreezesThenRecoversToNormal()
        {
            var timeline = new HitstopTimeline();

            timeline.Pulse(0.05f, 0.2f, 0.1f);
            Assert.IsTrue(timeline.IsActive, "触发后应处于定格状态");
            Assert.AreEqual(0.2f, timeline.CurrentScale, 0.0001f, "定格期间时间尺度应为 0.2");

            timeline.Advance(0.04f);
            Assert.AreEqual(0.2f, timeline.CurrentScale, 0.0001f, "定格未结束时不应开始恢复");

            timeline.Advance(0.02f);
            Assert.IsTrue(timeline.CurrentScale > 0.2f, "定格结束后应开始向 1.0 恢复");
            Assert.IsTrue(timeline.CurrentScale < 1f, "恢复是渐进的，不应瞬间跳回 1.0");

            timeline.Advance(0.5f);
            Assert.AreEqual(1f, timeline.CurrentScale, 0.0001f, "0.1s 弹簧恢复后必须回到 1.0");
            Assert.IsFalse(timeline.IsActive, "恢复完成后时间线应归位");
        }

        [Test]
        public void Hitstop_RepeatedPulsesExtendHoldInsteadOfStacking()
        {
            var timeline = new HitstopTimeline();

            timeline.Pulse(0.05f, 0.3f, 0.1f);
            timeline.Advance(0.03f);
            timeline.Pulse(0.05f, 0.2f, 0.1f);

            Assert.AreEqual(0.2f, timeline.CurrentScale, 0.0001f, "重叠触发应取更低的冻结尺度");
            Assert.Greater(timeline.FreezeRemaining, 0.04f, "重叠触发应延长定格而不是重新计时");

            timeline.Advance(1f);
            Assert.AreEqual(1f, timeline.CurrentScale, 0.0001f);
        }

        [Test]
        public void Hitstop_CancelRestoresImmediately()
        {
            var timeline = new HitstopTimeline();
            timeline.Pulse(0.2f, 0.1f, 0.1f);
            timeline.Advance(0.01f);

            timeline.Cancel();

            Assert.IsFalse(timeline.IsActive);
            Assert.AreEqual(1f, timeline.CurrentScale, 0.0001f);
        }

        [Test]
        public void Hitstop_IgnoresDegenerateParameters()
        {
            var timeline = new HitstopTimeline();

            timeline.Pulse(0f, 0.2f, 0.1f);
            Assert.IsFalse(timeline.IsActive, "0 时长不应触发定格");

            timeline.Pulse(0.05f, 1f, 0.1f);
            Assert.IsFalse(timeline.IsActive, "时间尺度 1.0 不应触发定格");

            timeline.Pulse(0.05f, 0f, 0.1f);
            Assert.IsFalse(timeline.IsActive, "时间尺度 0 会导致永不恢复，必须拒绝");
        }

        [Test]
        public void ComboTint_WarmsTowardTeamColorWithAlphaPreserved()
        {
            Color white = Color.white;
            Color team = new Color(1f, 0.44f, 0.06f, 1f);

            Color noCombo = HitFeedbackMath.ComboTint(white, team, 0);
            Assert.AreEqual(1f, noCombo.r, 0.0001f);
            Assert.AreEqual(1f, noCombo.g, 0.0001f);
            Assert.AreEqual(1f, noCombo.b, 0.0001f);

            Color sixCombo = HitFeedbackMath.ComboTint(white, team, 6);
            Assert.Less(sixCombo.g, 0.9f, "6 连击应明显向队伍色加温");
            Assert.Greater(sixCombo.g, team.g, "加温上限不应完全变成队伍色");

            Color withAlpha = new Color(1f, 1f, 1f, 0.6f);
            Color tinted = HitFeedbackMath.ComboTint(withAlpha, team, 3);
            Assert.AreEqual(0.6f, tinted.a, 0.0001f, "alpha 必须保留");
        }

        [Test]
        public void MarkerScale_GrowsWithDamageAndComboWithinCaps()
        {
            Assert.AreEqual(0.8f, HitFeedbackMath.MarkerScale(0f, 0), 0.0001f, "零伤害取下限");
            Assert.AreEqual(1.6f, HitFeedbackMath.MarkerScale(90f, 0), 0.0001f, "90 伤害到上限");
            Assert.AreEqual(1.6f, HitFeedbackMath.MarkerScale(500f, 0), 0.0001f, "溢出伤害仍被钳制");

            float combo6 = HitFeedbackMath.MarkerScale(90f, 6);
            Assert.AreEqual(1.6f * 1.36f, combo6, 0.001f, "6 连击在基础上放大 36%");
        }

        [Test]
        public void SourceAngle_MatchesCameraBasis()
        {
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            Assert.AreEqual(0f, HitFeedbackMath.SourceAngleDegrees(Vector3.forward, forward, right), 0.001f);
            Assert.AreEqual(90f, HitFeedbackMath.SourceAngleDegrees(Vector3.right, forward, right), 0.001f);
            Assert.AreEqual(-90f, HitFeedbackMath.SourceAngleDegrees(Vector3.left, forward, right), 0.001f);
            Assert.AreEqual(180f, Mathf.Abs(HitFeedbackMath.SourceAngleDegrees(Vector3.back, forward, right)), 0.001f);

            // 相机偏航 90° 后，世界 +X 方向应变成「正前方」= 0°
            Vector3 yawedForward = Vector3.right;
            Vector3 yawedRight = Vector3.back;
            Assert.AreEqual(0f, HitFeedbackMath.SourceAngleDegrees(Vector3.right, yawedForward, yawedRight), 0.001f,
                "受击弧必须跟随相机朝向，而不是固定世界坐标");
        }

        [Test]
        public void ComboPitch_RisesWithComboAndKill()
        {
            // P6-B 对齐 Inkwave hit_marker：每级升 1.5 半音（×2^(1.5/12)），上限 8 级（一个八度内）
            Assert.AreEqual(1f, HitFeedbackMath.ComboPitch(0, false), 0.0001f);
            Assert.AreEqual(Mathf.Pow(2f, 9f / 12f), HitFeedbackMath.ComboPitch(6, false), 0.0001f, "6 连击 = 9 半音");
            Assert.AreEqual(2f, HitFeedbackMath.ComboPitch(12, false), 0.0001f, "8 级封顶 = 刚好一个八度");
            Assert.AreEqual(Mathf.Pow(2f, 9f / 12f) + 0.06f, HitFeedbackMath.ComboPitch(6, true), 0.0001f, "击杀音再高 6%");
        }
    }
}
