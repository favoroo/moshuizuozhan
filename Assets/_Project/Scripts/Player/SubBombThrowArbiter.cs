namespace P1.Player
{
    /// <summary>
    /// 副武器手雷「按住瞄准 / 松开投掷」输入状态机（纯逻辑，可 EditMode 单测）。
    /// 对齐 inkwave 手感：按下沿进入瞄准并显示弹道预览，松开沿才投掷（短按同样松手投出）。
    /// 旧实现把按下沿当成「立即投掷」：按下瞬间投出第 1 颗并扣墨进入冷却，松开沿投第 2 颗时
    /// 因墨水不足 / 冷中被静默拒绝——表现为「瞄准后松手丢不出来」，故彻底移除按下即投。
    /// 触屏不产生按下 / 松开沿：MobileTouchUI 在 pointer-up 注入 TouchBombTriggered，
    /// 直接作为投掷事件（短按快投 / 长按瞄准后松手投出），与旧语义完全一致。
    /// </summary>
    public class SubBombThrowArbiter
    {
        private bool _aiming;

        /// <summary>当前是否处于「按住副武器键瞄准」状态（驱动弹道预览 / 主武器压制 / 相机 Look-ahead）。</summary>
        public bool IsAiming => _aiming;

        /// <summary>死亡 / 暂停 / 比赛非 Playing 等早退场景下与外部清屏逻辑同步。</summary>
        public void Reset()
        {
            _aiming = false;
        }

        /// <summary>
        /// 每帧推进一次。返回 true 表示本帧应执行一次投掷（executeBombThrow）。
        /// </summary>
        /// <param name="bombPressedEdge">桌面端副武器键按下沿（E / 鼠标中键 / 手柄 RB 的 wasPressedThisFrame）。</param>
        /// <param name="bombHeld">副武器键当前按住（含触屏 TouchBombHeld）。</param>
        /// <param name="bombReleasedEdge">桌面端副武器键松开沿（wasReleasedThisFrame）。</param>
        /// <param name="touchThrowInjected">触屏 pointer-up 注入的一次性投掷事件（TouchBombTriggered）。</param>
        /// <param name="specialActive">大招演出中（期间屏蔽副武器，保持旧版行为）。</param>
        public bool Tick(
            bool bombPressedEdge,
            bool bombHeld,
            bool bombReleasedEdge,
            bool touchThrowInjected,
            bool specialActive)
        {
            if (specialActive)
            {
                // 大招期间屏蔽副武器：取消瞄准且绝不投掷，大招中的松手事件作废
                _aiming = false;
                return false;
            }

            if (touchThrowInjected)
            {
                // 触屏松手即投：无论是否曾进入瞄准态（短按快投 / 长按瞄准投）
                _aiming = false;
                return true;
            }

            if (bombPressedEdge)
            {
                // 按下沿只进入瞄准不投掷，修正「按下即投 + 松开补投被拒」的双投链
                _aiming = true;
                return false;
            }

            if (_aiming && (bombReleasedEdge || !bombHeld))
            {
                // 松开沿投掷（含同帧按下+松开的极短点按：下一帧由 !held 补投）
                _aiming = false;
                return true;
            }

            if (bombHeld)
            {
                // 兜底建立 / 恢复瞄准：暂停、死亡、大招结束等错过按下沿时，仍按着键也能重新瞄准
                _aiming = true;
            }

            return false;
        }
    }
}
