using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// [P36] 阵亡整备（复活换枪）纯逻辑：
    /// - 显示门禁：仅「对局进行中 + 本地玩家已阵亡」时弹出，Ready/Countdown/结算都不弹；
    /// - 应用门禁：只有复活那一帧、且仍在进行中时才结算挂起的换枪；
    ///   对局结束/重开时 phase 已离开 Playing，挂起选择必须作废，避免跨局把上一局的选择带进新局；
    /// - 索引清洗：复用 <see cref="WeaponSelectMath.ClampIndex"/>（0..6），UI 与存档只有一套边界；
    /// - 文案：倒计时与换枪代价提示集中在这里，武器摘要格式化仍保留给战前武器卡口径复用。
    /// 全部为静态纯函数，不依赖 MonoBehaviour，EditMode 可直接覆盖边界。
    /// </summary>
    public static class RespawnLoadoutMath
    {
        /// <summary>整备条槽位数（与战前 7 把武器一致）。</summary>
        public const int SlotCount = WeaponSelectMath.WeaponCount;

        /// <summary>倒计时低于该值就不再报小数，直接显示「复活中…」。</summary>
        public const float ImminentRespawnSeconds = 0.05f;

        /// <summary>换枪代价提示（与 <see cref="Weapons.WeaponController.EquipWeapon(WeaponConfigSO, bool)"/> 的清空行为一一对应）。</summary>
        public const string SwapCostHint = "换枪清空大招充能 · 复活时生效";

        /// <summary>本帧是否应该弹出整备条：必须在对局进行中、本地玩家存在且已阵亡。</summary>
        public static bool ShouldShow(MatchManager.Phase phase, bool hasLocalPlayer, bool isAlive)
        {
            return hasLocalPlayer && !isAlive && phase == MatchManager.Phase.Playing;
        }

        /// <summary>
        /// 复活瞬间是否应该结算挂起的换枪：进行中 + 已存活 + 有挂起选择，三个条件同时成立。
        /// 调用方还必须保证「上一帧是死亡的」（本帧刚复活），否则开局就会把残留选择结算掉。
        /// </summary>
        public static bool ShouldApplyPending(MatchManager.Phase phase, bool hasPendingSelection, bool isAlive)
        {
            return hasPendingSelection && isAlive && phase == MatchManager.Phase.Playing;
        }

        /// <summary>索引清洗：负数回落 0，超范围回落 <see cref="SlotCount"/>-1。</summary>
        public static int ClampIndex(int index)
        {
            return WeaponSelectMath.ClampIndex(index);
        }

        /// <summary>复活倒计时文案；进入最后 0.05s 后改口为「复活中…」，避免读出一个假的 0.0s。</summary>
        public static string FormatCountdown(float seconds)
        {
            if (seconds <= ImminentRespawnSeconds)
            {
                return "复活中…";
            }

            return $"复活 {seconds:0.0}s";
        }

        /// <summary>选中武器标题：「喷射小枪 · 均衡连发」。</summary>
        public static string FormatWeaponTitle(WeaponKind kind)
        {
            return $"{WeaponSelectMath.GetWeaponChineseName(kind)} · {WeaponSelectMath.GetWeaponRoleTag(kind)}";
        }

        /// <summary>选中武器四栏读数（伤害 / 射程 / 射速 / 耗墨），与战前武器卡同一口径。</summary>
        public static string FormatStats(WeaponConfigSO config, WeaponKind kind)
        {
            return $"伤害 {WeaponSelectMath.FormatDamage(config, kind)} · " +
                   $"射程 {WeaponSelectMath.FormatRange(config, kind)} · " +
                   $"射速 {WeaponSelectMath.FormatFireRate(config, kind)} · " +
                   $"耗墨 {WeaponSelectMath.FormatInkCost(config, kind)}";
        }
    }
}
