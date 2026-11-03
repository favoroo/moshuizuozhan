namespace P1.Player
{
    /// <summary>
    /// 副武器手雷输入仲裁（纯逻辑，可 EditMode 单测）。
    /// 现行手感对齐 inkwave：按住副武器键进入瞄准（显示弹道预览），松开立刻按准星落点投出；
    /// 按下+松开发生在同一帧（快速点按）也只投一次；大招演出中/死亡/暂停会取消瞄准且不投。
    /// 旧「按下即投」的 <see cref="Tick"/> 签名保留，供旧调用方与既有测试使用。
    /// </summary>
    public class SubBombThrowArbiter
    {
        /// <summary>一帧的副武器意图：预览/是否本帧投出/是否刚开始瞄准/是否被外部取消。</summary>
        public readonly struct Intent
        {
            /// <summary>按住瞄准中（应显示弹道预览）。</summary>
            public readonly bool IsAiming;

            /// <summary>本帧应执行一次投掷（松手沿 / 同帧点按）。</summary>
            public readonly bool ExecuteThrow;

            /// <summary>本帧刚进入瞄准（用于打断潜墨形态）。</summary>
            public readonly bool StartedAiming;

            /// <summary>瞄准被大招/死亡/暂停打断（调用方应收起预览）。</summary>
            public readonly bool Canceled;

            public Intent(bool isAiming, bool executeThrow, bool startedAiming, bool canceled)
            {
                IsAiming = isAiming;
                ExecuteThrow = executeThrow;
                StartedAiming = startedAiming;
                Canceled = canceled;
            }

            public static Intent None => new Intent(false, false, false, false);
        }

        /// <summary>是否正处于按住瞄准态（供准星 / 相机 / 测试读取）。</summary>
        public bool IsAiming { get; private set; }

        /// <summary>死亡 / 暂停 / 比赛非 Playing 等场景下与外部清屏逻辑同步。</summary>
        public void Reset()
        {
            IsAiming = false;
        }

        /// <summary>
        /// inkwave 式按住瞄准 / 松手投掷状态机。
        /// </summary>
        /// <param name="pressedEdge">本帧副武器键按下沿（桌面 E / 鼠标中键 / 手柄 RB）。</param>
        /// <param name="held">副武器键是否按住。</param>
        /// <param name="releasedEdge">本帧松开沿（由调用方对上一帧按住状态求差得到）。</param>
        /// <param name="touchPressed">触屏手雷键 pointer-down 注入。</param>
        /// <param name="touchHeld">触屏手雷键是否按住。</param>
        /// <param name="touchReleased">触屏手雷键 pointer-up 注入。</param>
        /// <param name="specialActive">大招演出中（期间取消瞄准并屏蔽副武器）。</param>
        public Intent Advance(
            bool pressedEdge,
            bool held,
            bool releasedEdge,
            bool touchPressed,
            bool touchHeld,
            bool touchReleased,
            bool specialActive)
        {
            // 大招期间屏蔽副武器：正在瞄准也立刻取消，避免演出中突然丢雷
            if (specialActive)
            {
                bool canceled = IsAiming;
                IsAiming = false;
                return new Intent(false, false, false, canceled);
            }

            bool press = pressedEdge || touchPressed;
            bool hold = held || touchHeld;
            bool release = releasedEdge || touchReleased;

            bool startedAiming = false;
            if (!IsAiming && press)
            {
                IsAiming = true;
                startedAiming = true;
            }

            if (!IsAiming)
                return Intent.None;

            // 松手 = 投出（inkwave 松手投）；按住状态丢失（系统手势/失焦/丢失 pointer-up）也按松手收尾，
            // 避免预览永久卡住、手雷永远丢不出去。
            if (release || !hold)
            {
                IsAiming = false;
                return new Intent(false, true, startedAiming, false);
            }

            return new Intent(true, false, startedAiming, false);
        }

        /// <summary>
        /// 旧接口（按下即投）：返回 true 表示本帧应执行一次投掷。
        /// 保留原语义供旧调用方；玩家链路已改用 <see cref="Advance"/>。
        /// </summary>
        public bool Tick(
            bool bombPressedEdge,
            bool bombHeld,
            bool bombReleasedEdge,
            bool touchThrowInjected,
            bool specialActive)
        {
            if (specialActive)
                return false;

            return bombPressedEdge || touchThrowInjected;
        }
    }
}
