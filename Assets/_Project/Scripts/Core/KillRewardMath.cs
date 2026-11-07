using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 击杀奖励纯数学（把交火接进主循环的数值口径）：
    /// 击杀 → 击杀者立刻获得「固定大招能量 + 受害者因阵亡损失的能量全额转移 + 一次性回墨」。
    /// 三个数值都可由 <see cref="GameConfigSO"/> 覆盖；非法/未配置值回退这里的规范默认。
    /// 全部为纯函数，EditMode 可直接断言；运行时由 <see cref="KillRewardService"/> 消费。
    /// </summary>
    public static class KillRewardMath
    {
        /// <summary>击杀固定奖励的大招能量点默认值：70 ≈ 1/3 个大招（各武器门槛 170~195）。</summary>
        public const float DefaultKillBonusPoints = 70f;

        /// <summary>
        /// 受害者阵亡时损失的大招能量比例默认值：0.5（与 WeaponController 阵亡减半同口径）。
        /// 损失部分全额转给击杀者——旧版这笔能量白扣，交火与主循环完全脱钩。
        /// </summary>
        public const float DefaultEnergyTransferFraction = 0.5f;

        /// <summary>击杀一次性回墨默认值：25/罐。空罐连打后仍够反打一轮，不至于杀完人没墨可喷。</summary>
        public const float DefaultKillInkRefill = 25f;

        /// <summary>击杀固定奖励点数：未配置（≤0）时回退规范默认。</summary>
        public static float ResolveKillBonusPoints(float configured)
        {
            return configured > 0f ? configured : DefaultKillBonusPoints;
        }

        /// <summary>受害者损失能量比例：未配置（&lt;0）时回退规范默认，显式 0 = 关闭转移，结果钳制到 [0,1]。</summary>
        public static float ResolveEnergyTransferFraction(float configured)
        {
            float fraction = configured < 0f ? DefaultEnergyTransferFraction : configured;
            return Mathf.Clamp01(fraction);
        }

        /// <summary>受害者转移给击杀者的能量 = 阵亡前能量 × 损失比例（负能量按 0 处理）。</summary>
        public static float ComputeEnergyTransfer(float victimPreDeathSpecialPoints, float transferFraction)
        {
            return Mathf.Max(0f, victimPreDeathSpecialPoints) * Mathf.Clamp01(transferFraction);
        }

        /// <summary>击杀一次性回墨：未配置（≤0）时回退规范默认。</summary>
        public static float ResolveKillInkRefill(float configured)
        {
            return configured > 0f ? configured : DefaultKillInkRefill;
        }
    }
}
