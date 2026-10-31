using P1.Weapons;

namespace P1.UI
{
    /// <summary>
    /// 准星状态纯逻辑（可 EditMode 测试）：
    /// 1. 瞄准点超出武器有效射程 → 变暗 + 虚线（对齐 INKWAVE 的 is-far）；
    /// 2. 准星压在存活敌人身上且射程内 → 放大 + 染红（对齐 INKWAVE 的 is-target）；
    /// 3. 滚筒的准星表达推地宽度而不是远处落点，不参与超距变暗。
    /// </summary>
    public static class ReticleStateMath
    {
        /// <summary>射程边界容差（米）：比较时留出半米，避免贴着射程边缘时状态抖动。</summary>
        public const float RangePadding = 0.5f;

        /// <summary>超距时的整体透明度（INKWAVE 为 0.42）。</summary>
        public const float FarAlpha = 0.42f;

        /// <summary>锁定敌人时的整体缩放（INKWAVE 为 1.14）。</summary>
        public const float TargetScale = 1.14f;

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

        /// <param name="aimFlatDistance">瞄准点到玩家的水平距离（米），与弹丸水平射程判定一致。</param>
        /// <param name="effectiveRange">武器当前有效射程（米）。</param>
        /// <param name="hasEnemyTarget">屏幕中心射线是否命中存活敌方（或辅助瞄准锁定的敌方）。</param>
        /// <param name="kind">当前武器形态；滚筒不参与超距状态。</param>
        public static State Compute(float aimFlatDistance, float effectiveRange, bool hasEnemyTarget, WeaponKind kind)
        {
            bool outOfRange = kind != WeaponKind.Roller &&
                              effectiveRange > 0f &&
                              aimFlatDistance > effectiveRange + RangePadding;
            bool onTarget = hasEnemyTarget && !outOfRange;

            return new State(
                outOfRange,
                onTarget,
                outOfRange ? FarAlpha : 1f,
                onTarget ? TargetScale : 1f);
        }
    }
}
