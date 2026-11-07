using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 击杀奖励服务（把交火接进主循环）：击杀敌方时，击杀者立刻获得
    /// ① 固定大招能量（GameConfigSO.killRewardSpecialPoints），
    /// ② 受害者因阵亡损失的大招能量全额转移（旧版这笔能量白扣），
    /// ③ 一次性回墨（GameConfigSO.killRewardInk）。
    /// 由 <see cref="CombatantHealth"/> 在 <c>Die</c> 里调用；环境/跌落死亡（无攻击者）不奖励，
    /// 同归于尽（击杀者已死）不奖励，非敌对单位之间不奖励。
    /// 玩家与 Bot 一视同仁——Bot 击杀同样充能，敌方大招威胁才有压力来源。
    /// </summary>
    public static class KillRewardService
    {
        /// <summary>
        /// 结算一次击杀奖励。
        /// </summary>
        /// <param name="killer">击杀者（可空：环境死亡传 null，直接跳过）。</param>
        /// <param name="victim">受害者。</param>
        /// <param name="victimPreDeathSpecialPoints">
        /// 受害者阵亡前的大招能量点。必须在 <c>OnKilled</c>（WeaponController 内部把能量减半）<strong>之前</strong>捕获，
        /// 否则转移量会少一半。
        /// </param>
        public static void AwardKill(CombatantHealth killer, CombatantHealth victim, float victimPreDeathSpecialPoints)
        {
            if (killer == null || victim == null || killer == victim)
                return;
            if (!killer.IsAlive || !killer.Team.IsOpponentOf(victim.Team))
                return;

            GameConfigSO config = MatchManager.Instance != null ? MatchManager.Instance.Config : null;

            float bonus = KillRewardMath.ResolveKillBonusPoints(
                config != null ? config.killRewardSpecialPoints : -1f);
            float fraction = KillRewardMath.ResolveEnergyTransferFraction(
                config != null ? config.killRewardEnergyTransferFraction : -1f);
            float transfer = KillRewardMath.ComputeEnergyTransfer(victimPreDeathSpecialPoints, fraction);

            WeaponController killerWeapon = killer.GetComponent<WeaponController>();
            if (killerWeapon != null && bonus + transfer > 0f)
            {
                // AddSpecialPointsRaw 内部钳制到大招门槛、跳过大招演出中，并自动播「大招就绪」音。
                killerWeapon.AddSpecialPointsRaw(bonus + transfer);
            }

            float ink = KillRewardMath.ResolveKillInkRefill(
                config != null ? config.killRewardInk : -1f);
            if (ink > 0f)
            {
                InkCharacterMotor killerMotor = killer.Motor;
                if (killerMotor != null)
                {
                    killerMotor.RefillInk(ink);
                }
            }
        }
    }
}
