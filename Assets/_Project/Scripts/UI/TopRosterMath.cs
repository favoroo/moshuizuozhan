using UnityEngine;

namespace P1.UI
{
    public enum TimerPhase
    {
        Normal = 0,
        LastMinute = 1,
        FinalThirty = 2,
    }

    public enum AvatarSlotState
    {
        NormalAlive = 0,
        DeadRespawning = 1,
        SpecialReady = 2,
    }

    /// <summary>
    /// 顶部 4v4 战况条与中央墨滴计时器的纯逻辑与数学计算类（P4-B）。
    /// 纯函数，不依赖 UnityEngine.Object / GameObject / Canvas，零 GC 分配，极易 EditMode 单测。
    /// </summary>
    public static class TopRosterMath
    {
        /// <summary>
        /// 格式化倒计时时间为分和秒。
        /// </summary>
        public static (int minutes, int seconds) SplitTime(float remainingSeconds)
        {
            int totalSec = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            return (totalSec / 60, totalSec % 60);
        }

        /// <summary>
        /// 格式化为 MM:SS 字符串。
        /// </summary>
        public static string FormatTimerString(float remainingSeconds)
        {
            var (mins, secs) = SplitTime(remainingSeconds);
            return $"{mins:00}:{secs:00}";
        }

        /// <summary>
        /// 根据剩余秒数判定当前比赛阶段（常规、最后60秒决战、最后30秒急促警报）。
        /// </summary>
        public static TimerPhase GetTimerPhase(float remainingSeconds)
        {
            if (remainingSeconds <= 30f) return TimerPhase.FinalThirty;
            if (remainingSeconds <= 60f) return TimerPhase.LastMinute;
            return TimerPhase.Normal;
        }

        /// <summary>
        /// 计算复活倒计时环进度（0..1，随时间逐渐由 0 填满至 1）。
        /// </summary>
        public static float CalculateRespawnProgress(float remaining, float duration)
        {
            if (duration <= 0.001f) return 1f;
            float clampedRemaining = Mathf.Clamp(remaining, 0f, duration);
            return Mathf.Clamp01(1f - (clampedRemaining / duration));
        }

        /// <summary>
        /// 计算双方占领百分比在比分条上的宽度分割比（保证极端情况下双方都有保底可视宽度）。
        /// </summary>
        public static float CalculateScoreSplitRatio(float percentA, float percentB, float minRatio = 0.05f, float maxRatio = 0.95f)
        {
            float sum = percentA + percentB;
            if (sum <= 0.001f) return 0.5f;

            float raw = percentA / sum;
            return Mathf.Clamp(raw, minRatio, maxRatio);
        }

        /// <summary>
        /// 确定队员头像槽位的三态反馈。
        /// </summary>
        public static AvatarSlotState DetermineSlotState(bool isAlive, bool isSpecialReady)
        {
            if (!isAlive) return AvatarSlotState.DeadRespawning;
            if (isSpecialReady) return AvatarSlotState.SpecialReady;
            return AvatarSlotState.NormalAlive;
        }

        /// <summary>
        /// 是否跨越了 60 秒触发点（上一帧 > 60，当前帧 <= 60）。
        /// </summary>
        public static bool DidCrossSixtySeconds(float previousTime, float currentTime)
        {
            return previousTime > 60f && currentTime <= 60f;
        }
    }
}
