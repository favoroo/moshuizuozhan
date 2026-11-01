using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// HUD 子视图的可见性判定：Canvas.enabled = false 时 Unity 只跳过渲染，
    /// MonoBehaviour 的 Update/LateUpdate 仍会每帧执行。子视图（小地图、计时器、
    /// 队伍槽位等）在整块 HUD 隐藏期间应直接 early-out，避免隐藏界面的空转开销。
    /// </summary>
    public static class UICanvasVisibility
    {
        /// <summary>
        /// 组件所在 Canvas 链上任一 Canvas 被禁用 → 当前 UI 不渲染，调用方可跳过本帧更新。
        /// 无 Canvas（独立测试 / 不在 UI 树中的视图）时返回 true，不改变原有行为。
        /// </summary>
        /// <param name="component">调用方组件（用于向上查找 Canvas）。</param>
        /// <param name="cache">调用方持有的缓存数组，首次调用时填充。</param>
        public static bool IsVisible(Component component, ref Canvas[] cache)
        {
            if (component == null)
                return false;

            if (cache == null || cache.Length == 0)
            {
                cache = component.GetComponentsInParent<Canvas>(true);
            }

            if (cache.Length == 0)
                return true;

            for (int i = 0; i < cache.Length; i++)
            {
                Canvas canvas = cache[i];
                if (canvas != null && !canvas.enabled)
                    return false;
            }

            return true;
        }
    }
}