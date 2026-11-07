using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 胶囊进度条 Image（2026-11 圆角化 #09）：按当前 rect 的纵横比自动选用
    /// <see cref="UIFlatSprite.Pill"/> 胶囊精灵，<see cref="Image.Type.Filled"/> 与
    /// <see cref="Image.fillAmount"/> 逻辑完全照旧——所有持有 <c>Image</c> 字段的
    /// 进度条代码无需改动，只需把组件从 <c>Image</c> 换成本类。
    ///
    /// 条形尺寸变化（安全区、布局表、面板缩放）时自动换对应比例的精灵，
    /// 不会出现「两端圆头被拉扁」；两端半圆半径恒等于高度一半（真胶囊）。
    ///
    /// 陷阱：精灵同步只能在重建循环**之外**做（OnEnable / OnRectTransformDimensionsChange）。
    /// 在 OnPopulateMesh 里赋 sprite 会触发 SetAllDirty →「Trying to add … for graphic rebuild
    /// while we are already inside a graphic rebuild loop」，PlayMode 会判红。
    /// </summary>
    public class UIPillImage : Image
    {
        private int _aspectKey;

        protected override void OnEnable()
        {
            base.OnEnable();
            _aspectKey = 0;
            SyncSprite();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SyncSprite();
        }

        private void SyncSprite()
        {
            Rect rect = rectTransform.rect;
            if (rect.width < 1f || rect.height < 1f)
            {
                _aspectKey = 0; // 尚未布局：等 rect 变化回调再同步
                return;
            }

            float aspect = Mathf.Clamp(rect.width / rect.height, 1f, 64f);
            int key = Mathf.RoundToInt(aspect * 4f);
            if (key == _aspectKey && sprite != null) return;

            _aspectKey = key;
            sprite = UIFlatSprite.Pill(aspect);
            preserveAspect = false;
            maskable = true;
        }
    }
}
