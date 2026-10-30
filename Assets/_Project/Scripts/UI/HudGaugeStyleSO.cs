using P1.Weapons;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 右上角「墨水计量管 + 大招液体球」的全部可调参数（P4-C）。
    ///
    /// 与 <see cref="UIThemeSO"/> 的分工：
    /// <see cref="UIThemeSO"/> 管配色（队伍墨色 / readyGold / dangerRed / 字体 / 弹簧），
    /// 本资产只管这一簇 HUD 的几何、阈值、波浪与节拍。组件忘记拖资产时会退回
    /// <see cref="Fallback"/>，因此代码与测试不依赖是否生成过 .asset。
    /// </summary>
    [CreateAssetMenu(fileName = "HudGaugeStyle", menuName = "P1/UI HUD Gauge Style", order = 31)]
    public class HudGaugeStyleSO : ScriptableObject
    {
        [Header("布局（锚点=右上角，单位=1920×1080 参考分辨率像素）")]
        [Tooltip("大招球中心相对右上角的偏移；默认放在小地图左侧，避免与 P4-D 的小地图重叠")]
        public Vector2 orbAnchoredPosition = new Vector2(-330f, -132f);

        public float orbSize = 124f;

        public Vector2 tankAnchoredPosition = new Vector2(-429f, -170f);

        public Vector2 tankSize = new Vector2(54f, 180f);

        [Header("墨水计量管 · 阈值")]
        [Tooltip("低于该墨水比例进入低墨告警（闪烁红光 + 警报音）")]
        [Range(0.02f, 0.5f)] public float lowInkThreshold01 = 0.2f;

        [Tooltip("告警退出迟滞，防止液面在阈值附近抖动时反复报警")]
        [Range(0f, 0.08f)] public float lowInkHysteresis = 0.03f;

        [Tooltip("副武器（手雷）刻度红线位置。运行时会优先用 WeaponConfig.bombInkCost / maxInk 覆盖")]
        [Range(0.05f, 1f)] public float bombThreshold01 = 0.7f;

        [Header("墨水计量管 · 动效")]
        [Tooltip("罐体红光闪烁频率（Hz）")]
        public float warningBlinkHz = 2.4f;

        [Tooltip("持续低墨时警报音重复间隔（秒）")]
        public float warningRepeatInterval = 1.15f;

        [Range(0f, 1f)] public float lowInkWarningVolume = 0.42f;

        [Range(0f, 1f)] public float warningGlowAlpha = 0.42f;

        [Range(0f, 0.12f)] public float tankWaveAmplitude = 0.026f;

        public float tankWaveSpeed = 1.15f;

        public int tankColumns = 16;

        [Tooltip("罐内上升气泡数量（0 = 关闭；窄罐体建议 1~2 颗，点缀即可）")]
        [Range(0, 4)] public int tankBubbleCount = 2;

        [Tooltip("罐内气泡半径（相对罐高的比例）")]
        [Range(0.004f, 0.05f)] public float tankBubbleSize = 0.02f;

        [Header("大招液体球 · 动效")]
        [Range(0f, 0.2f)] public float orbWaveAmplitude = 0.045f;

        public float orbWaveSpeed = 1.5f;

        public int orbColumns = 40;

        [Range(0.2f, 4f)] public float orbWaveCycles = 1.35f;

        [Range(0, 6)] public int orbBubbleCount = 3;

        [Header("大招满能 · 爆发")]
        [Tooltip("金色光环旋转速度（度/秒）")]
        public float haloSpinDegreesPerSecond = 130f;

        [Range(0f, 1f)] public float haloIntensity = 0.95f;

        [Range(1, 32)] public int haloDashCount = 9;

        [Tooltip("满能脉冲频率（Hz）")]
        public float readyPulseHz = 1.15f;

        [Tooltip("满能脉冲的最大缩放增量（0.05 = ±5%）")]
        [Range(0f, 0.2f)] public float readyPulseScale = 0.045f;

        [Tooltip("满能瞬间冲击波 + 射线的持续时间（秒）")]
        public float burstDuration = 0.7f;

        public int burstRayCount = 10;

        [Header("满能按键提示")]
        public string readyHintText = "按 Q 释放";

        public string readyHintKey = "Q";

        [Range(0f, 1f)] public float readyHintDelay = 0.06f;

        public Vector2 readyHintSize = new Vector2(140f, 30f);

        [Tooltip("提示条相对球心的 Y 偏移（负值=挂在球下方）")]
        public float readyHintOffsetY = -76f;

        [Header("大招技能图标（Art/Sprites/Icons/Specials）")]
        [Tooltip("Tidal Slam 白色矢量图标；留空时编辑器会按资源路径兜底加载")]
        public Sprite slamIcon;

        [Tooltip("Ink Tempest 白色矢量图标；留空时编辑器会按资源路径兜底加载")]
        public Sprite stormIcon;

        [Range(16f, 96f)] public float specialIconSize = 44f;

        [Tooltip("图标在球心内的 Y 偏移（正=向上，避开底部百分比读数）")]
        public float specialIconOffsetY = 6f;

        [Header("文字")]
        public float percentFontSize = 20f;

        public float hintFontSize = 18f;

        [Header("弹簧")]
        [Tooltip("墨水管液面追随手感：几乎不过冲，读数稳")]
        public UISpringSettings tankFillSpring = UISpringSettings.Follow;

        [Tooltip("大招球液面追随手感：带一点过冲，涂地涨能时有『涌起来』的弹性")]
        public UISpringSettings orbFillSpring = new UISpringSettings(6.2f, 0.66f, 8);

        private static HudGaugeStyleSO _fallback;

        /// <summary>没有显式配置时的兜底实例（不落盘，域重载后重建）。</summary>
        public static HudGaugeStyleSO Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<HudGaugeStyleSO>();
                    _fallback.name = "HudGaugeStyle (Fallback)";
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallback;
            }
        }

        public static HudGaugeStyleSO Resolve(HudGaugeStyleSO style)
        {
            return style != null ? style : Fallback;
        }

        /// <summary>按当前大招类型取球心图标；未配置时返回 null（视图会自行兜底或隐藏）。</summary>
        public Sprite GetSpecialIcon(SpecialKind kind)
        {
            return kind == SpecialKind.Storm ? stormIcon : slamIcon;
        }
    }
}
