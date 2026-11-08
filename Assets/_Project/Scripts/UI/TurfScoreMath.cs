using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 「+N m²」涂地飘字的纯函数集合（P42-D，对齐 INKWAVE <c>iw-tpop</c> 关键帧）：
    /// 聚合窗口防连发刷屏 + 弹入/停留/上飘淡出的寿命曲线，全部纯函数便于 EditMode 断言。
    ///
    /// 飘字打在准星旁（玩家视线焦点处即时反馈）；无常驻读数卡。
    /// </summary>
    public static class TurfScoreMath
    {
        // ── 聚合节奏 ──────────────────────────────────────────────────

        /// <summary>飘字聚合窗口（秒）：窗口内多次涂地合并成一个数字，避免连发时每颗弹丸都飘一个 +0.3。</summary>
        public const float AccumulateWindowSeconds = 0.4f;

        /// <summary>小于该面积（m²）的涂地不弹飘字（单格 0.014 m²，避免碎屑刷屏）。</summary>
        public const float MinPopupSquareMeters = 0.5f;

        /// <summary>飘字池容量：超出后复用最老的一个，稳态零 GC（INKWAVE 同屏最多 4 条）。</summary>
        public const int PopupPoolSize = 4;

        /// <summary>大额涂地阈值（m²）：达到后飘字放大并染队伍墨色（INKWAVE is-big）。</summary>
        public const float BigPopupThresholdSquareMeters = 10f;

        // ── 飘字曲线（INKWAVE iw-tpop 关键帧，t 为归一化寿命 0~1）──

        /// <summary>飘字存活时长（秒，同 INKWAVE 1s 动画）。</summary>
        public const float PopupLifetimeSeconds = 1.0f;

        /// <summary>起始下沉量（像素）：弹出前先沉 8px，制造「蹦出来」的落点感。</summary>
        public const float PopupDropInPixels = 8f;

        /// <summary>弹出过冲量（像素）：scale 回正前先冲高 2px。</summary>
        public const float PopupOvershootPixels = 2f;

        /// <summary>淡出阶段最终上飘量（像素，UI 空间 y 向上）。</summary>
        public const float PopupRisePixels = 26f;

        /// <summary>横向随机抖动幅度（±像素，每条飘字出生时随机一次）。</summary>
        public const float PopupJitterPixels = 8f;

        /// <summary>弹出起始缩放。</summary>
        public const float PopupStartScale = 0.5f;

        /// <summary>弹出峰值缩放（0.18s 处，过冲感）。</summary>
        public const float PopupPeakScale = 1.15f;

        /// <summary>淡出终点缩放。</summary>
        public const float PopupEndScale = 0.95f;

        /// <summary>关键帧：弹出段结束（INKWAVE 18%）。</summary>
        public const float KeyPopEnd = 0.18f;

        /// <summary>关键帧：回正段结束（INKWAVE 30%）。</summary>
        public const float KeySettleEnd = 0.30f;

        /// <summary>关键帧：淡出开始（INKWAVE 75%）。</summary>
        public const float KeyFadeStart = 0.75f;

        /// <summary>
        /// 累计值是否应当在此刻结算成一次飘字：
        /// 累计量达到阈值、且距上次结算超过聚合窗口（或本帧结束时强制冲刷）。
        /// </summary>
        public static bool ShouldFlush(float accumulated, float secondsSinceFlush, float dt, bool force = false)
        {
            if (accumulated < MinPopupSquareMeters) return false;
            if (force) return true;
            return secondsSinceFlush + Mathf.Max(0f, dt) >= AccumulateWindowSeconds;
        }

        /// <summary>飘字寿命归一化进度 0~1（age 超过 lifetime 后钳到 1）。</summary>
        public static float PopupProgress(float age, float lifetime)
        {
            if (lifetime <= 0.0001f) return 1f;
            return Mathf.Clamp01(age / lifetime);
        }

        /// <summary>
        /// 飘字透明度：0 弹入 → 18% 达到全不透明 → 75% 前保持 → 线性淡出（INKWAVE 关键帧）。
        /// </summary>
        public static float PopupAlpha(float progress01)
        {
            float t = Mathf.Clamp01(progress01);
            if (t < KeyPopEnd) return t / KeyPopEnd;
            if (t < KeyFadeStart) return 1f;
            return Mathf.Clamp01(1f - (t - KeyFadeStart) / (1f - KeyFadeStart));
        }

        /// <summary>
        /// 飘字缩放：0.5 弹入 → 18% 冲到 1.15 → 30% 回正 1 → 停留 → 100% 缩到 0.95。
        /// </summary>
        public static float PopupScale(float progress01)
        {
            float t = Mathf.Clamp01(progress01);
            if (t < KeyPopEnd) return Mathf.Lerp(PopupStartScale, PopupPeakScale, t / KeyPopEnd);
            if (t < KeySettleEnd) return Mathf.Lerp(PopupPeakScale, 1f, (t - KeyPopEnd) / (KeySettleEnd - KeyPopEnd));
            if (t < KeyFadeStart) return 1f;
            return Mathf.Lerp(1f, PopupEndScale, (t - KeyFadeStart) / (1f - KeyFadeStart));
        }

        /// <summary>
        /// 飘字纵向偏移（像素，uGUI y 向上）：先沉在基准线下 8px，弹出冲到 0、过冲 +2，
        /// 之后一路飘升到 +26。整体单调不降。
        /// </summary>
        public static float PopupRiseOffset(float progress01)
        {
            float t = Mathf.Clamp01(progress01);
            if (t < KeyPopEnd) return Mathf.Lerp(-PopupDropInPixels, 0f, t / KeyPopEnd);
            if (t < KeySettleEnd) return Mathf.Lerp(0f, PopupOvershootPixels, (t - KeyPopEnd) / (KeySettleEnd - KeyPopEnd));
            return Mathf.Lerp(PopupOvershootPixels, PopupRisePixels, (t - KeySettleEnd) / (1f - KeySettleEnd));
        }

        // ── 文本 ─────────────────────────────────────────────────────

        /// <summary>
        /// 飘字富文本：整数面积 + 缩小一号的单位（INKWAVE「+12p」的节奏；
        /// 单位交给富文本缩小，数字保持大字号）。
        /// </summary>
        public static string FormatPopupRich(float squareMeters)
        {
            int value = Mathf.Max(0, Mathf.RoundToInt(squareMeters));
            return "+" + value + "<size=55%> m²</size>";
        }
    }
}
