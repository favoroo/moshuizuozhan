using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 菜单 Esc 的归属层。数值越大优先级越高（仅用于诊断与测试断言）。
    /// </summary>
    public enum MenuEscLayer
    {
        None = 0,
        WeaponDrawer = 1,
        PauseMenu = 2,
        LayoutEditor = 3,
        HomeInfo = 4,
        MapSelect = 5,

        /// <summary>衣柜（外观）：最后打开的菜单层，Esc 归属优先级最高。</summary>
        Locker = 6
    }

    /// <summary>归属层对应的唯一动作，两个消费者（暂停菜单 / 武器抽屉）共用同一份映射。</summary>
    public enum MenuEscAction
    {
        None = 0,
        CloseHomeInfo,
        CloseLayoutEditor,
        TogglePause,
        CloseDrawer,
        CloseMapSelect,

        /// <summary>关闭衣柜浮层。</summary>
        CloseLocker
    }

    /// <summary>
    /// 菜单级输入仲裁（P19 UI 修复）：
    /// 历史 bug——首页一个 Esc 同时被 <c>PauseMenuOverlay</c>（开暂停）与 <c>WeaponSelectView</c>（收放抽屉）
    /// 各自读一次 <c>escapeKey.wasPressedThisFrame</c>，只有 HomeInfoOverlay 有同帧消费标记，
    /// 于是同一帧发生两个动作，抽屉状态与暂停菜单互相踩。
    ///
    /// 规则（全局唯一事实来源）：
    /// 1. <see cref="ResolveEscOwner"/> 是纯函数：由「当前谁处于展开态」决定 Esc 归属，
    ///    因为状态在归属者动手之前不会变，所以与 MonoBehaviour 的 Update 顺序无关；
    /// 2. <see cref="CanLayerHandle"/> 是纯函数：**非归属层一律不碰帧位**。
    ///    这是「顺序无关」的关键——旧接线里暂停菜单在归属为抽屉时先 consume 再 return，
    ///    抽屉随后 TryConsumeEsc 失败，若暂停菜单先 Update，一个 Esc 在抽屉上完全没反应；
    /// 3. <see cref="TryConsumeEsc(MenuEscLayer)"/> 帧级消费位：同一帧只有归属层能执行一次动作；
    /// 4. 优先级：衣柜 &gt; 地图选择 &gt; 首页详情浮层 &gt; 布局编辑 &gt; 暂停/设置 &gt; 武器抽屉；
    ///    都没展开时 Esc = 暂停菜单（桌面端暂停唯一入口），因此抽屉只在「已展开」时抢 Esc。
    /// 两个消费者（<c>PauseMenuOverlay</c> / <c>WeaponSelectView</c>）都按
    /// <c>ResolveEscOwner → CanLayerHandle → TryConsumeEsc → ActionForOwner</c> 四步走，
    /// 谁先 Update 结果都一样。
    /// </summary>
    public static class MenuInputRouter
    {
        private static int _consumedFrame = int.MinValue;
        private static MenuEscLayer _consumedLayer = MenuEscLayer.None;

        /// <summary>本帧的菜单输入（Esc / 手柄 Start）是否已被某个层消费。</summary>
        public static bool ConsumedThisFrame => _consumedFrame == Time.frameCount;

        /// <summary>本帧消费者的层（未被消费时为 <see cref="MenuEscLayer.None"/>）。</summary>
        public static MenuEscLayer ConsumedLayer => _consumedFrame == Time.frameCount ? _consumedLayer : MenuEscLayer.None;

        /// <summary>
        /// 纯逻辑：按展开状态解析本帧 Esc 的归属层。
        /// 展开态互斥（界面自身保证不会同时展开多个），因此结果唯一且与调用顺序无关。
        /// 新层的可选参数一律追加在末尾并给默认值：既有调用点零改动。
        /// </summary>
        public static MenuEscLayer ResolveEscOwner(bool homeInfoOpen, bool layoutEditing, bool pauseExpanded, bool drawerExpanded,
            bool mapSelectOpen = false, bool lockerOpen = false)
        {
            // 更晚打开的层在上：衣柜从地图选择 / 首页浮层之上打开，所以它最先拿走 Esc。
            if (lockerOpen) return MenuEscLayer.Locker;
            if (mapSelectOpen) return MenuEscLayer.MapSelect;
            if (homeInfoOpen) return MenuEscLayer.HomeInfo;
            if (layoutEditing) return MenuEscLayer.LayoutEditor;
            if (pauseExpanded) return MenuEscLayer.PauseMenu;
            if (drawerExpanded) return MenuEscLayer.WeaponDrawer;
            return MenuEscLayer.PauseMenu;
        }

        /// <summary>按当前帧号解析归属（运行时代码用这个）。</summary>
        public static MenuEscLayer ResolveEscOwnerThisFrame(bool homeInfoOpen, bool layoutEditing, bool pauseExpanded, bool drawerExpanded,
            bool mapSelectOpen = false, bool lockerOpen = false)
        {
            return ResolveEscOwner(homeInfoOpen, layoutEditing, pauseExpanded, drawerExpanded, mapSelectOpen, lockerOpen);
        }

        /// <summary>
        /// 纯逻辑：<paramref name="layer"/> 是否有权处理归属为 <paramref name="owner"/> 的本帧 Esc。
        /// - 归属为首页详情浮层 / 布局编辑 / 地图选择 / 衣柜：任何消费者都可代劳（谁先 Update 谁关，动作完全一致）；
        /// - 归属为暂停/设置或武器抽屉：**只有自己**能处理，非归属层必须直接 return 且不得占用帧位，
        ///   否则归属层会因消费位被抢而彻底收不到这个 Esc（顺序相关的历史 bug）。
        /// </summary>
        public static bool CanLayerHandle(MenuEscLayer layer, MenuEscLayer owner)
        {
            if (owner == MenuEscLayer.HomeInfo || owner == MenuEscLayer.LayoutEditor ||
                owner == MenuEscLayer.MapSelect || owner == MenuEscLayer.Locker) return true;
            return layer == owner;
        }

        /// <summary>纯逻辑：归属层对应的唯一动作（两个消费者共用，避免两处 switch 漂移）。</summary>
        public static MenuEscAction ActionForOwner(MenuEscLayer owner)
        {
            switch (owner)
            {
                case MenuEscLayer.HomeInfo: return MenuEscAction.CloseHomeInfo;
                case MenuEscLayer.LayoutEditor: return MenuEscAction.CloseLayoutEditor;
                case MenuEscLayer.MapSelect: return MenuEscAction.CloseMapSelect;
                case MenuEscLayer.Locker: return MenuEscAction.CloseLocker;
                case MenuEscLayer.WeaponDrawer: return MenuEscAction.CloseDrawer;
                default: return MenuEscAction.TogglePause;
            }
        }

        /// <summary>纯逻辑：本帧的菜单输入是否已被 <paramref name="frame"/> 帧的消费位占住。</summary>
        public static bool IsConsumed(int frame)
        {
            return _consumedFrame == frame;
        }

        /// <summary>
        /// 纯逻辑：尝试占用 <paramref name="frame"/> 帧的菜单输入。已被占用返回 false
        /// （调用方必须直接 return，绝不能在同一帧执行第二个菜单动作）。
        /// </summary>
        public static bool TryConsume(int frame, MenuEscLayer layer)
        {
            if (_consumedFrame == frame) return false;
            _consumedFrame = frame;
            _consumedLayer = layer;
            return true;
        }

        /// <summary>当前帧的菜单输入归属尝试（运行时代码用这个）。</summary>
        public static bool TryConsumeEsc(MenuEscLayer layer)
        {
            return TryConsume(Time.frameCount, layer);
        }

        /// <summary>
        /// 无条件标记本帧已被消费：给「已经动手、之后才补标记」的旧调用方用
        /// （例如 <see cref="HomeInfoOverlay.TryCloseFromMenuInput"/>）。
        /// </summary>
        public static void MarkConsumed(MenuEscLayer layer)
        {
            _consumedFrame = Time.frameCount;
            _consumedLayer = layer;
        }

        /// <summary>仅 EditMode 测试使用：清掉帧消费位，避免跨用例残留。</summary>
        public static void ResetForTests()
        {
            _consumedFrame = int.MinValue;
            _consumedLayer = MenuEscLayer.None;
        }
    }
}
