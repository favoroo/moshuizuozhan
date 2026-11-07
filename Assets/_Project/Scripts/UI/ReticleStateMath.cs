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
    /// 枪形（<see cref="ReticleShape.Shooter"/>）内部的武器身份层：
    /// 5 把弹丸武器共用「中心点 + 散布刻度」这套读数语言，但各自换一个外形壳，
    /// 让玩家不读 HUD 也能从准星轮廓认出当前武器（对齐 INKWAVE 的 iw-ret--* 分类）。
    /// 与 <see cref="WeaponKind"/> 是一对一（滚筒/蓄力狙有独立容器，不参与本层）。
    /// </summary>
    public enum ReticleStyle
    {
        /// <summary>小枪：细主环 + 4 向散布刻度。</summary>
        Basic = 0,

        /// <summary>双枪：左右两枚小环 + 4 向刻度，架枪时叠一枚菱形锁定框。</summary>
        Twin = 1,

        /// <summary>泼桶：上方拱形 + 下方落点斗（泼出去的弧线与落点宽度）。</summary>
        Lob = 2,

        /// <summary>爆破枪：外环（爆炸半径）+ 内环（直击核心），不画刻度。</summary>
        Blast = 3,

        /// <summary>加特林：8 段旋转蓄力环 + 上下两枚刻度。</summary>
        Spin = 4
    }

    /// <summary>
    /// 准星状态纯逻辑（可 EditMode 测试）：
    /// 1. 瞄准点超出武器有效射程 → 变暗 + 虚线（对齐 INKWAVE 的 is-far）；
    /// 2. 准星压在存活敌人身上且射程内 → 放大 + 染红（对齐 INKWAVE 的 is-target）；
    /// 3. 滚筒的准星表达推地宽度而不是远处落点，不参与超距变暗；
    /// 4. 武器类型 → 准星形状（容器族）与武器身份（外形壳）的映射必须覆盖全部 <see cref="WeaponKind"/>。
    /// </summary>
    public static class ReticleStateMath
    {
        /// <summary>射程边界容差（米）：比较时留出半米，避免贴着射程边缘时状态抖动。</summary>
        public const float RangePadding = 0.5f;

        /// <summary>静止散布下限（度）：0 散布武器（泼桶 / 双枪架枪）也保留最小刻度间距。</summary>
        public const float MinSpreadDegrees = 1.2f;

        /// <summary>枪形主环直径（参考像素）：比旧版 60 更克制，给刻度留出外扩空间。</summary>
        public const float ShooterRingDiameter = 44f;

        /// <summary>散布刻度中心距的下限（px）：静止时贴在主环内侧，与中心点不粘连。</summary>
        public const float TickBaseOffset = 15f;

        /// <summary>每多 1° 散布，刻度中心额外外扩的像素数。</summary>
        public const float TickPixelsPerDegree = 4f;

        /// <summary>单发开火时刻度额外外扩的像素上限。</summary>
        public const float TickShotPunchPixels = 5f;

        /// <summary>空墨拒射时刻度反向收拢的像素数（与击发外扩形成相反手感）。</summary>
        public const float TickEmptyPunchPixels = 4f;

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
        /// 武器类型 → 枪形内部的身份外形（唯一映射源，屏幕准星据此切换外形壳）。
        /// 滚筒 / 蓄力狙各自有独立容器，这里回落到 <see cref="ReticleStyle.Basic"/> 只作占位。
        /// 新增 <see cref="WeaponKind"/> 时在这里补一行，否则新武器会退回小枪外形（不会消失，但认不出来）。
        /// </summary>
        public static ReticleStyle StyleFor(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Dualies: return ReticleStyle.Twin;
                case WeaponKind.Slosher: return ReticleStyle.Lob;
                case WeaponKind.Blaster: return ReticleStyle.Blast;
                case WeaponKind.Splatling: return ReticleStyle.Spin;
                default:
                    // Shooter 以及滚筒/蓄力狙（它们不使用枪形容器）
                    return ReticleStyle.Basic;
            }
        }

        /// <summary>
        /// 散布刻度中心距（px）：静止贴着主环内侧，随散布/开火外扩、空墨拒射反向收拢。
        /// 抽成纯函数是为了让 EditMode 能钉住「静止时不会压到中心点、也不会盖回主环」这条几何约束。
        /// </summary>
        /// <param name="spreadDegrees">当前散布角（度，已可经 <see cref="ClampSpread"/> 夹住下限）。</param>
        /// <param name="shotPunch01">单发开火冲击 0~1。</param>
        /// <param name="emptyPunch01">空墨拒射冲击 0~1。</param>
        public static float TickOffset(float spreadDegrees, float shotPunch01 = 0f, float emptyPunch01 = 0f)
        {
            float spread = Mathf.Max(MinSpreadDegrees, spreadDegrees);
            float offset = TickBaseOffset
                           + (spread - MinSpreadDegrees) * TickPixelsPerDegree
                           + Mathf.Clamp01(shotPunch01) * TickShotPunchPixels
                           - Mathf.Clamp01(emptyPunch01) * TickEmptyPunchPixels;
            // 下限 10px：再往里就盖住中心点；上限 64px：留在 1080p 视野的舒适读数区内。
            return Mathf.Clamp(offset, 10f, 64f);
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
