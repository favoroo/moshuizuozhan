using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 踩敌墨全屏边缘墨迹的调参入口（P18-A）。
    ///
    /// 参考 INKWAVE `src/fx/screenfx.js` 的 edgeInk：
    /// 底边最厚、四边包一圈，FBM 噪声抖动边界 + 湿润高光 + 顶部垂滴，颜色取敌方队色。
    /// 与 <see cref="HudGaugeStyleSO"/> 一样提供 <see cref="Fallback"/>：
    /// 资产缺失时用代码默认值，视图不依赖是否生成过 .asset。
    /// </summary>
    [CreateAssetMenu(fileName = "InkEdgeWarningStyle", menuName = "P1/UI Ink Edge Warning Style", order = 32)]
    public class InkEdgeWarningStyleSO : ScriptableObject
    {
        [Header("淡入淡出（对齐 INKWAVE：9/s 淡入、3/s 淡出）")]
        [Tooltip("踩上敌墨时的强度逼近速率 (1/s)：9 ≈ 0.11s 到达 63%")]
        [Range(0.5f, 30f)] public float fadeInRate = 9f;

        [Tooltip("离开敌墨后的强度衰减速率 (1/s)：3 ≈ 0.33s 衰减 63%，比淡入慢，提示不会一闪而过")]
        [Range(0.5f, 30f)] public float fadeOutRate = 3f;

        [Header("墨边几何（屏幕高度比例）")]
        [Tooltip("底边墨带最大厚度：0.078 = 屏高 7.8%（对齐 INKWAVE 0.08）")]
        [Range(0f, 0.2f)] public float bottomBand = 0.078f;

        [Tooltip("左右 / 顶边细墨线厚度：0.0025 = 屏高 0.25%")]
        [Range(0f, 0.05f)] public float edgeBand = 0.0025f;

        [Header("形态")]
        [Tooltip("边界不规则抖动幅度 0~2；0 = 平直硬边")]
        [Range(0f, 2f)] public float wobble = 0.85f;

        [Tooltip("顶部垂滴强度 0~1；0 = 关闭垂滴")]
        [Range(0f, 1f)] public float dripStrength = 0.55f;

        [Tooltip("顶部垂滴长度缩放 0.2~2.0")]
        [Range(0.2f, 2f)] public float dripLengthScale = 1.0f;

        [Header("观感")]
        [Tooltip("湿润双高光强度 0.5~5.0")]
        [Range(0.5f, 5f)] public float specularIntensity = 2.4f;

        [Tooltip("墨边最大不透明度：0.94 保留一点场景透出，避免完全遮死边缘")]
        [Range(0.1f, 1f)] public float maxAlpha = 0.94f;

        [Tooltip("低于该强度直接关闭 Image，保证不踩敌墨时空闲零成本")]
        [Range(0f, 0.05f)] public float visibleThreshold = 0.003f;

        private static InkEdgeWarningStyleSO _fallback;

        /// <summary>资产未生成 / 未挂引用时的默认实例（HideAndDontSave，不进包）。</summary>
        public static InkEdgeWarningStyleSO Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<InkEdgeWarningStyleSO>();
                    _fallback.name = "InkEdgeWarningStyle (Fallback)";
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallback;
            }
        }

        public static InkEdgeWarningStyleSO Resolve(InkEdgeWarningStyleSO style)
        {
            return style != null ? style : Fallback;
        }
    }
}
