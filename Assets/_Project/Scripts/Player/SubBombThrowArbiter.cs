namespace P1.Player
{
    /// <summary>
    /// 副武器手雷「按下即投」输入仲裁（纯逻辑，可 EditMode 单测）。
    /// 手感对齐主流 FPS：按一次副武器键立刻朝准星方向投出，不进入瞄准态、不显示抛物线预览；
    /// 按住不会连投，松开也不会补投。触屏在 pointer-down 注入 TouchBombTriggered，语义一致。
    /// </summary>
    public class SubBombThrowArbiter
    {
        /// <summary>
        /// 保留旧接口：新版手雷没有「按住瞄准」态，恒为 false。
        /// 外部（准星 / 相机 / 测试）仍可安全读取，不会再出现瞄准态卡死。
        /// </summary>
        public bool IsAiming => false;

        /// <summary>死亡 / 暂停 / 比赛非 Playing 等场景下与外部清屏逻辑同步；新版无内部状态，保留空实现。</summary>
        public void Reset()
        {
        }

        /// <summary>
        /// 每帧推进一次。返回 true 表示本帧应执行一次投掷（executeBombThrow）。
        /// </summary>
        /// <param name="bombPressedEdge">桌面端副武器键按下沿（E / 鼠标中键 / 手柄 RB 的 wasPressedThisFrame）。</param>
        /// <param name="bombHeld">保留参数：新版按下即投，按住不连投。</param>
        /// <param name="bombReleasedEdge">保留参数：新版松开不补投。</param>
        /// <param name="touchThrowInjected">触屏 pointer-down 注入的一次性投掷事件（TouchBombTriggered）。</param>
        /// <param name="specialActive">大招演出中（期间屏蔽副武器）。</param>
        public bool Tick(
            bool bombPressedEdge,
            bool bombHeld,
            bool bombReleasedEdge,
            bool touchThrowInjected,
            bool specialActive)
        {
            // 大招期间屏蔽副武器：按下沿与触屏注入都作废，避免演出中突然丢雷
            if (specialActive)
                return false;

            // 桌面按下沿 / 触屏按下注入 = 投掷；按住与松开都不产生额外事件
            return bombPressedEdge || touchThrowInjected;
        }
    }
}
