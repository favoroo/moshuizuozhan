using P1.Core;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 本地玩家的难度补偿（纯逻辑，EditMode 可断言）。
    ///
    /// Bot 难度只弱化敌人，玩家本身没有任何容错补偿；手机端触屏瞄准吃亏，
    /// 因此低难度额外给本地玩家加最大生命（只影响本地玩家，Bot 与队友保持基准血量）：
    ///   新手  ×1.5（100 → 150，约多挨 2 发）
    ///   轻松  ×1.25（→ 125）
    ///   普通 / 硬核 ×1.0（保持原平衡）
    /// 血量倍率走 <see cref="CombatantHealth.SetMaxHealthScale"/>，按当前血量比例缩放，
    /// 切难度不会出现血条跳变；复活/重开比赛会以新的 MaxHealth 满血。
    /// </summary>
    public static class PlayerDifficultyMath
    {
        public const float BeginnerMaxHealthScale = 1.5f;
        public const float EasyMaxHealthScale = 1.25f;
        public const float MinMaxHealthScale = 0.5f;
        public const float MaxMaxHealthScale = 3f;

        /// <summary>难度档位 → 本地玩家最大生命倍率。</summary>
        public static float GetMaxHealthScale(int difficultyLevel)
        {
            switch (GameSettingsMath.NormalizeBotDifficulty(difficultyLevel))
            {
                case GameSettingsMath.BotDifficultyBeginner:
                    return BeginnerMaxHealthScale;
                case GameSettingsMath.BotDifficultyEasy:
                    return EasyMaxHealthScale;
                default:
                    return 1f;
            }
        }

        /// <summary>把倍率收口到安全区间（避免外部误传 0 / 负数把玩家血量清零）。</summary>
        public static float ClampMaxHealthScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale))
                return 1f;
            return Mathf.Clamp(scale, MinMaxHealthScale, MaxMaxHealthScale);
        }
    }
}
