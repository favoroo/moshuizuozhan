using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P42-D 涂地飘字与低血暗角的纯逻辑验收：
    /// 聚合窗口、飘字关键帧曲线（INKWAVE iw-tpop）、文本格式、血量阈值与危急闪烁次数。
    /// 无场景依赖，EditMode 秒级运行。
    /// </summary>
    public class TurfAndLowHealthUiMathTests
    {
        // ── TurfScoreMath：聚合 ────────────────────────────────────────

        [Test]
        public void TurfFlush_WaitsForAccumulateWindow()
        {
            // 未达面积阈值：永远不结算
            Assert.IsFalse(TurfScoreMath.ShouldFlush(0.1f, 999f, 0.4f));

            // 达到阈值但窗口未到：先攒着（避免连发刷屏）
            Assert.IsFalse(TurfScoreMath.ShouldFlush(2f, 0.1f, 0.1f));

            // 窗口到点：结算
            Assert.IsTrue(TurfScoreMath.ShouldFlush(2f, 0.35f, 0.1f));

            // force 立即结算
            Assert.IsTrue(TurfScoreMath.ShouldFlush(2f, 0f, 0f, force: true));
        }

        [Test]
        public void TurfFlush_IgnoresTinySplats()
        {
            Assert.IsFalse(TurfScoreMath.ShouldFlush(TurfScoreMath.MinPopupSquareMeters - 0.01f, 999f, 0f));
            Assert.IsTrue(TurfScoreMath.ShouldFlush(TurfScoreMath.MinPopupSquareMeters, 999f, 0f));
        }

        // ── TurfScoreMath：飘字关键帧（INKWAVE iw-tpop）───────────────

        [Test]
        public void TurfPopup_ScalePunchesThenSettles()
        {
            Assert.AreEqual(TurfScoreMath.PopupStartScale, TurfScoreMath.PopupScale(0f), 0.0001f);
            Assert.AreEqual(TurfScoreMath.PopupPeakScale, TurfScoreMath.PopupScale(TurfScoreMath.KeyPopEnd), 0.0001f, "18% 冲到峰值 1.15");
            Assert.AreEqual(1f, TurfScoreMath.PopupScale(TurfScoreMath.KeySettleEnd), 0.0001f, "30% 回正");
            Assert.AreEqual(1f, TurfScoreMath.PopupScale(TurfScoreMath.KeyFadeStart), 0.0001f, "75% 前保持 1");
            Assert.AreEqual(TurfScoreMath.PopupEndScale, TurfScoreMath.PopupScale(1f), 0.0001f);

            // 峰值必须真的大于起终（有「蹦」的感觉）
            Assert.Greater(TurfScoreMath.PopupPeakScale, TurfScoreMath.PopupStartScale);
            Assert.Greater(TurfScoreMath.PopupPeakScale, TurfScoreMath.PopupEndScale);
        }

        [Test]
        public void TurfPopup_RiseDropsInThenFloatsUp()
        {
            Assert.AreEqual(-TurfScoreMath.PopupDropInPixels, TurfScoreMath.PopupRiseOffset(0f), 0.0001f, "出生在基准线下方");
            Assert.AreEqual(0f, TurfScoreMath.PopupRiseOffset(TurfScoreMath.KeyPopEnd), 0.0001f);
            Assert.AreEqual(TurfScoreMath.PopupOvershootPixels, TurfScoreMath.PopupRiseOffset(TurfScoreMath.KeySettleEnd), 0.0001f, "回正前冲高 2px");
            Assert.AreEqual(TurfScoreMath.PopupRisePixels, TurfScoreMath.PopupRiseOffset(1f), 0.0001f, "终点上飘 26px");

            float previous = -999f;
            for (int i = 0; i <= 10; i++)
            {
                float offset = TurfScoreMath.PopupRiseOffset(i / 10f);
                Assert.GreaterOrEqual(offset, previous, "纵向偏移整体单调不降（下沉→弹出→上飘）");
                previous = offset;
            }
        }

        [Test]
        public void TurfPopup_AlphaPopsInHoldsThenFades()
        {
            Assert.AreEqual(0f, TurfScoreMath.PopupAlpha(0f), 0.0001f, "出生透明（弹出感）");
            Assert.AreEqual(1f, TurfScoreMath.PopupAlpha(TurfScoreMath.KeyPopEnd), 0.0001f, "18% 达到全不透明");
            Assert.AreEqual(1f, TurfScoreMath.PopupAlpha(TurfScoreMath.KeyFadeStart), 0.0001f, "75% 前不淡出");
            Assert.AreEqual(0.5f, TurfScoreMath.PopupAlpha((TurfScoreMath.KeyFadeStart + 1f) * 0.5f), 0.02f, "淡出段线性");
            Assert.AreEqual(0f, TurfScoreMath.PopupAlpha(1f), 0.0001f);
        }

        [Test]
        public void TurfPopup_ProgressClampsAndHandlesZeroLifetime()
        {
            Assert.AreEqual(0f, TurfScoreMath.PopupProgress(0f, 1f), 0.0001f);
            Assert.AreEqual(1f, TurfScoreMath.PopupProgress(5f, 1f), 0.0001f);
            Assert.AreEqual(1f, TurfScoreMath.PopupProgress(0.2f, 0f), 0.0001f);
        }

        // ── TurfScoreMath：文本 ────────────────────────────────────────

        [Test]
        public void TurfText_FormatsRichPopup()
        {
            Assert.AreEqual("+2<size=55%> m²</size>", TurfScoreMath.FormatPopupRich(2.35f), "整数面积 + 富文本缩小单位");
            Assert.AreEqual("+0<size=55%> m²</size>", TurfScoreMath.FormatPopupRich(-1f));
            Assert.IsTrue(TurfScoreMath.FormatPopupRich(TurfScoreMath.BigPopupThresholdSquareMeters)
                .StartsWith("+10"), "大额阈值处应显示 +10 m²");
        }

        [Test]
        public void TurfRegistered_EventCarriesPainterAndDelta()
        {
            var go = new GameObject("TurfStatsPainter");
            var health = go.AddComponent<CombatantHealth>();

            CombatantHealth capturedPainter = null;
            float capturedDelta = 0f;
            System.Action<CombatantHealth, float> handler = (painter, delta) =>
            {
                capturedPainter = painter;
                capturedDelta = delta;
            };

            MatchStatsService.TurfRegistered += handler;
            try
            {
                MatchStatsService.RegisterTurf(health, 3.5f);

                Assert.AreEqual(health, capturedPainter);
                Assert.AreEqual(3.5f, capturedDelta, 0.0001f);

                // 非法增量不触发事件
                capturedPainter = null;
                MatchStatsService.RegisterTurf(health, 0f);
                MatchStatsService.RegisterTurf(health, float.NaN);
                Assert.IsNull(capturedPainter);
            }
            finally
            {
                MatchStatsService.TurfRegistered -= handler;
                MatchStatsService.Clear();
                Object.DestroyImmediate(go);
            }
        }

        // ── LowHealthVignetteMath ──────────────────────────────────────

        [Test]
        public void LowHealth_TargetIntensityIsZeroAboveThreshold()
        {
            Assert.AreEqual(0f, LowHealthVignetteMath.TargetIntensity(1f), 0.0001f);
            Assert.AreEqual(0f, LowHealthVignetteMath.TargetIntensity(0.5f), 0.0001f);
            Assert.AreEqual(0f, LowHealthVignetteMath.TargetIntensity(LowHealthVignetteMath.WarningThreshold01), 0.0001f);
        }

        [Test]
        public void LowHealth_TargetIntensityRampsBelowThreshold()
        {
            float atCritical = LowHealthVignetteMath.TargetIntensity(LowHealthVignetteMath.CriticalThreshold01);
            float atZero = LowHealthVignetteMath.TargetIntensity(0f);

            Assert.Greater(atCritical, 0.4f, "到达危急线时应已有明显暗角");
            Assert.Less(atCritical, atZero, "越接近 0 越强");
            Assert.AreEqual(LowHealthVignetteMath.MaxIntensity, atZero, 0.0001f);

            // 单调性：血量升高时强度单调不增（hp 0 → 1 对应强度 1 → 0）
            float previous = 2f;
            for (int i = 0; i <= 10; i++)
            {
                float hp = i / 10f;
                float intensity = LowHealthVignetteMath.TargetIntensity(hp);
                Assert.LessOrEqual(intensity, previous + 0.0001f, "强度必须随血量升高单调不增");
                previous = intensity;
            }
        }

        [Test]
        public void LowHealth_StepIntensityUsesFadeInFasterThanFadeOut()
        {
            float value = 0f;
            for (int i = 0; i < 5; i++)
            {
                value = LowHealthVignetteMath.StepIntensity(value, 1f, 0.1f);
            }

            Assert.Greater(value, 0.95f, "9/s 淡入 0.5s 应基本到位");

            float entered = value;
            value = LowHealthVignetteMath.StepIntensity(value, 0f, 0.1f);
            Assert.Less(value, entered, "回血后应立即开始退场");
            Assert.Greater(value, 0.5f, "0.1s 内不应消失（淡出 3.2/s 比淡入慢）");

            // dt ≤ 0 保持原值（暂停 / 首帧）
            Assert.AreEqual(value, LowHealthVignetteMath.StepIntensity(value, 0f, 0f), 0.0001f);
        }

        [Test]
        public void LowHealth_CriticalFlashIsFiniteAndSettles()
        {
            // 窗口内存在暗相（0.55）
            bool sawDip = false;
            float total = LowHealthVignetteMath.CriticalFlashCount * LowHealthVignetteMath.CriticalFlashPeriodSeconds;
            for (float t = 0f; t < total; t += 0.01f)
            {
                if (LowHealthVignetteMath.CriticalFlashWeight(t) < 0.99f) sawDip = true;
            }
            Assert.IsTrue(sawDip, "危急窗口内应有闪烁");

            // 窗口结束后恒定为 1（不循环）
            Assert.AreEqual(1f, LowHealthVignetteMath.CriticalFlashWeight(total), 0.0001f);
            Assert.AreEqual(1f, LowHealthVignetteMath.CriticalFlashWeight(total + 30f), 0.0001f);
        }

        [Test]
        public void LowHealth_CriticalStateUsesHysteresis()
        {
            // 未进入时：跌破 20% 才进
            Assert.IsFalse(LowHealthVignetteMath.UpdateCritical(false, 0.25f));
            Assert.IsTrue(LowHealthVignetteMath.UpdateCritical(false, 0.19f));

            // 已进入时：要回到 24% 以上才退出
            Assert.IsTrue(LowHealthVignetteMath.UpdateCritical(true, 0.22f));
            Assert.IsFalse(LowHealthVignetteMath.UpdateCritical(true, 0.25f));
        }
    }
}
