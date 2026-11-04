using P1.Weapons;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 准星形状族：屏幕/地面真正渲染的三种容器。
    /// 与 <see cref="WeaponKind"/> 是多对一关系——7 种武器只分三类外观。
    /// </summary>
    public enum ReticleShape
    {
        /// <summary>枪形：中心点 + 环带 + 4 刻度，随 Bloom 扩散。</summary>
        Shooter = 0,

        /// <summary>滚筒：宽幅横向推地指示条。</summary>
        Roller = 1,

        /// <summary>蓄力狙：同心圆 + 收束线 + 蓄力进度环。</summary>
        Charger = 2
    }

    /// <summary>
    /// 准星状态纯逻辑（可 EditMode 测试）：
    /// 1. 瞄准点超出武器有效射程 → 变暗 + 虚线（对齐 INKWAVE 的 is-far）；
    /// 2. 准星压在存活敌人身上且射程内 → 放大 + 染红（对齐 INKWAVE 的 is-target）；
    /// 3. 滚筒的准星表达推地宽度而不是远处落点，不参与超距变暗；
    /// 4. 武器类型 → 准星形状的映射必须覆盖全部 <see cref="WeaponKind"/>。
    /// </summary>
    public static class ReticleStateMath
    {
        /// <summary>射程边界容差（米）：比较时留出半米，避免贴着射程边缘时状态抖动。</summary>
        public const float RangePadding = 0.5f;

        /// <summary>静止散布下限（度）：0 散布武器（泼桶 / 双枪架枪）也保留最小刻度间距。</summary>
        public const float MinSpreadDegrees = 1.2f;

        /// <summary>超距时的整体透明度（INKWAVE 为 0.42）。</summary>
        public const float FarAlpha = 0.42f;

        /// <summary>锁定敌人时的整体缩放（INKWAVE 为 1.14）。</summary>
        public const float TargetScale = 1.14f;

        /// <summary>空墨警告的方波闪烁频率（Hz）：硬切亮/暗，不用正弦呼吸（几何大胆风）。</summary>
        public const float EmptyBlinkHz = 3.5f;

        public readonly struct State
        {
            public readonly bool OutOfRange;
            public readonly bool OnTarget;
            public readonly float Alpha;
            public readonly float Scale;

            public State(bool outOfRange, bool onTarget, float alpha, float scale)
            {
                OutOfRange = outOfRange;
                OnTarget = onTarget;
                Alpha = alpha;
                Scale = scale;
            }
        }

        /// <summary>
        /// 空墨/没子弹状态（纯数据）：准星据此决定是否红闪、是否显示中央禁止叉。
        /// </summary>
        public readonly struct InkState
        {
            /// <summary>当前墨水不足以打出下一发（各武器门槛见 <c>MainWeaponStrategy.InkRequiredToFire</c>）。</summary>
            public readonly bool OutOfInk;

            /// <summary>打出下一发所需墨水（原样回传，调试/HUD 可读）。</summary>
            public readonly float RequiredInk;

            /// <summary>方波闪烁的亮相位：true = 亮，false = 暗；有墨时恒为 true。</summary>
            public readonly bool BlinkOn;

            public InkState(bool outOfInk, float requiredInk, bool blinkOn)
            {
                OutOfInk = outOfInk;
                RequiredInk = requiredInk;
                BlinkOn = blinkOn;
            }
        }

        /// <summary>
        /// 空墨判定：<paramref name="currentInk"/> 低于打出下一发所需墨水即为「没子弹」。
        /// 门槛 ≤ 0 的武器永不判空，避免零消耗武器准星常亮红。
        /// 闪烁相位用 <see cref="Mathf.Repeat"/> 方波（前 50% 亮、后 50% 暗），与 HUD 墨水罐的硬切节奏一致。
        /// </summary>
        /// <param name="currentInk">当前墨水（绝对值，与配置同单位）。</param>
        /// <param name="requiredInk">打出下一发所需最低墨水。</param>
        /// <param name="time">用于闪烁的无缩放时间（秒）；有墨时不影响结果。</param>
        /// <param name="blinkHz">闪烁频率，默认 <see cref="EmptyBlinkHz"/>。</param>
        public static InkState ComputeInk(float currentInk, float requiredInk, float time, float blinkHz = EmptyBlinkHz)
        {
            float required = Mathf.Max(0f, requiredInk);
            bool outOfInk = required > 0.0001f && currentInk < required;
            bool blinkOn = !outOfInk ||
                           Mathf.Repeat(time * Mathf.Max(0.01f, blinkHz), 1f) < 0.5f;
            return new InkState(outOfInk, required, blinkOn);
        }

        /// <param name="aimFlatDistance">瞄准点到玩家的水平距离（米），与弹丸水平射程判定一致。</param>
        /// <param name="effectiveRange">武器当前有效射程（米）。</param>
        /// <param name="hasEnemyTarget">屏幕中心射线是否命中存活敌方（或辅助瞄准锁定的敌方）。</param>
        /// <param name="kind">当前武器形态；滚筒不参与超距状态。</param>
        public static State Compute(float aimFlatDistance, float effectiveRange, bool hasEnemyTarget, WeaponKind kind)
        {
            return Compute(aimFlatDistance, effectiveRange, hasEnemyTarget, ShapeFor(kind));
        }

        /// <param name="shape">已解析的准星形状；形状族不参与超距的只有滚筒。</param>
        public static State Compute(float aimFlatDistance, float effectiveRange, bool hasEnemyTarget, ReticleShape shape)
        {
            bool outOfRange = shape != ReticleShape.Roller &&
                              effectiveRange > 0f &&
                              aimFlatDistance > effectiveRange + RangePadding;
            bool onTarget = hasEnemyTarget && !outOfRange;

            return new State(
                outOfRange,
                onTarget,
                outOfRange ? FarAlpha : 1f,
                onTarget ? TargetScale : 1f);
        }

        /// <summary>
        /// 武器类型 → 准星形状（唯一映射源，屏幕 UI 与地面光斑都走这里）：
        /// 滚筒 → 宽幅条；蓄力狙 → 蓄力环；
        /// 其余（小枪 / 双枪 / 泼桶 / 爆破枪 / 加特林）→ 枪形（中心点 + 环 + 散布刻度）。
        /// 加特林虽然也蓄力，但它蓄满后是持续扫射、散布反馈更重要，所以留在枪形并额外叠一圈蓄力环
        /// （见 <see cref="WeaponReticleUI"/> 的 ChargeRing）。
        /// 新增 <see cref="WeaponKind"/> 时只需在这里补一行，否则准星会整块消失。
        /// </summary>
        public static ReticleShape ShapeFor(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller: return ReticleShape.Roller;
                case WeaponKind.Charger: return ReticleShape.Charger;
                default:
                    // Shooter / Dualies / Slosher / Blaster / Splatling 以及未来新增的弹丸类武器
                    return ReticleShape.Shooter;
            }
        }

        /// <summary>
        /// 准星散布角的下限保护：泼桶等 0 散布武器若直接按 0 计算，4 个刻度会叠在中心点上糊成一团。
        /// </summary>
        public static float ClampSpread(float spreadDegrees)
        {
            return Mathf.Max(MinSpreadDegrees, spreadDegrees);
        }
    }
}
