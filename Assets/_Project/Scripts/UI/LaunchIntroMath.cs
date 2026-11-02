using UnityEngine;

namespace P1.UI
{
    /// <summary>启动动画某一时刻的纯数值快照（不含 UI 引用，可在 EditMode 直接断言）。</summary>
    public readonly struct LaunchIntroPose
    {
        public readonly float SplatScale;
        public readonly float SplatAngle;
        public readonly float MarkScale;
        public readonly float MarkAngle;
        public readonly int TitleChars;
        public readonly float BarFill;
        public readonly float WipeProgress;
        public readonly float WipeOffset;
        public readonly bool Finished;

        public LaunchIntroPose(float splatScale, float splatAngle, float markScale, float markAngle,
            int titleChars, float barFill, float wipeProgress, float wipeOffset, bool finished)
        {
            SplatScale = splatScale;
            SplatAngle = splatAngle;
            MarkScale = markScale;
            MarkAngle = markAngle;
            TitleChars = titleChars;
            BarFill = barFill;
            WipeProgress = wipeProgress;
            WipeOffset = wipeOffset;
            Finished = finished;
        }
    }

    /// <summary>
    /// 启动动画时序（纯函数）：墨点炸开 → 图标贴纸弹入 → 标题逐字 → 载入块填满 → 整屏上滑擦除。
    ///
    /// 为什么单独抽纯函数：
    /// - 动画时长/顺序是最容易被后续 UI 改动碰坏的部分，放在 <see cref="LaunchIntroView"/> 里
    ///   就只能靠肉眼看 Play，回归成本高；这里用 EditMode 钉住「单调递增 / 收尾必完成 / 跳过不早于擦除」。
    /// - 全部按 unscaled 秒计算，暂停（timeScale=0）与 Hitstop 不影响启动流程。
    /// </summary>
    public static class LaunchIntroMath
    {
        // 时间轴（秒，unscaled）。改动这里会同步影响 EditMode 断言。
        public const float SplatStart = 0.04f;
        public const float SplatEnd = 0.52f;
        public const float MarkStart = 0.22f;
        public const float MarkEnd = 0.82f;
        public const float TitleStart = 0.66f;
        public const float TitleEnd = 1.55f;
        public const float BarStart = 1.10f;
        public const float BarEnd = 2.28f;
        public const float WipeStart = 2.46f;
        public const float WipeEnd = 2.96f;

        public const float TotalDuration = WipeEnd;

        /// <summary>擦除位移量（参考画布 1080 高，向上滑出屏幕）。</summary>
        public const float WipeDistance = 1500f;

        public const float SplatStartScale = 0.15f;
        public const float MarkStartScale = 0.55f;

        public static float Normalize(float time, float start, float end)
        {
            if (end <= start) return time >= end ? 1f : 0f;
            return Mathf.Clamp01((time - start) / (end - start));
        }

        public static float EaseInCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        /// <summary>可跳过窗口：擦除开始前都还能按任意键直接收尾。</summary>
        public static bool CanSkip(float time) => time < WipeStart;

        /// <summary>
        /// 评估 <paramref name="time"/> 时刻的姿态。
        /// <paramref name="titleCharCount"/> 由调用方传入（标题文字长度），
        /// 纯函数不依赖 TMP，避免测试为了数几个字去建 Canvas。
        /// </summary>
        public static LaunchIntroPose Evaluate(float time, int titleCharCount)
        {
            time = Mathf.Max(0f, time);
            int charCount = Mathf.Max(0, titleCharCount);

            float splat = UICubicBezier.BackOut.Evaluate(Normalize(time, SplatStart, SplatEnd));
            float mark = UICubicBezier.BackOut.Evaluate(Normalize(time, MarkStart, MarkEnd));
            float title = Normalize(time, TitleStart, TitleEnd);
            float bar = Normalize(time, BarStart, BarEnd);
            float wipe = Normalize(time, WipeStart, WipeEnd);

            int visibleChars = Mathf.Clamp(Mathf.FloorToInt(title * charCount), 0, charCount);
            // 最后一帧必须真的显示完整标题，不让浮点误差卡在 N-1。
            if (title >= 1f) visibleChars = charCount;

            return new LaunchIntroPose(
                splatScale: Mathf.LerpUnclamped(SplatStartScale, 1f, splat),
                splatAngle: Mathf.LerpUnclamped(-26f, -8f, Mathf.Clamp01(splat)),
                markScale: Mathf.LerpUnclamped(MarkStartScale, 1f, mark),
                markAngle: Mathf.LerpUnclamped(9f, 0f, Mathf.Clamp01(mark)),
                titleChars: visibleChars,
                barFill: bar,
                wipeProgress: wipe,
                wipeOffset: EaseInCubic(wipe) * WipeDistance,
                finished: time >= TotalDuration);
        }
    }
}