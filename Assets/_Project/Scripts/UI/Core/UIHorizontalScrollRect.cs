using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 横向滚动容器：ScrollRect 的拖动状态（m_Dragging）是私有的，而地图选择的箭头滑动动画
    /// 必须在玩家一按住拖动时就立刻让位——两边同时写 content.anchoredPosition 会互相顶，
    /// 表现为「拖到一半被箭头动画拽回去」。这里只补一个 <see cref="IsDragging"/>，其余照基类走。
    /// </summary>
    [AddComponentMenu("P1/UI/Horizontal Scroll Rect")]
    public sealed class UIHorizontalScrollRect : ScrollRect
    {
        /// <summary>玩家正按住拖动中（惯性阶段为 false：那时位置由 ScrollRect 自己推）。</summary>
        public bool IsDragging { get; private set; }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            IsDragging = true;
            base.OnBeginDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            IsDragging = false;
            base.OnEndDrag(eventData);
        }

        protected override void OnDisable()
        {
            IsDragging = false;
            base.OnDisable();
        }
    }
}
