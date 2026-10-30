using UnityEngine;

namespace P1.UI
{
    /// <summary>移动端操作方案：2 指 / 3 指 / 4 指。</summary>
    public enum MobileLayoutPreset
    {
        TwoFinger = 0,
        ThreeFinger = 1,
        FourFinger = 2
    }

    /// <summary>
    /// 触控按键预设布局（以 1920x1080 为基准，应用时再乘设备 DPI 系数）：
    /// 1. 2 指：右拇指负责全部动作键，左拇指移动；
    /// 2. 3 指：在 2 指基础上增加左上角「左开火键」，由左食指负责开火；
    /// 3. 4 指：左开火键留在左上角，潜行键移到右上角交给右食指，射击大键略向左收，
    ///    跳跃/手雷/大招仍由右拇指负责（与实机截图量出的排布一致）。
    /// 数值与 MobileTouchUI.DefaultLayout 共用一份，避免双份排布漂移。
    /// </summary>
    public static class MobileLayoutPresets
    {
        public const float LeftFireSize = 160f;
        public const float MoveStickDiameter = 340f;
        public const float DefaultMoveStickAlpha = 0.55f;

        /// <summary>左上角射击键与右上角潜行键的放大倍数（按实机截图量得约 1.7 倍）。</summary>
        public const float TopActionScale = 1.70f;

        /// <summary>左上角开火键透明度：略微透出战场，与前作实机观感一致。</summary>
        public const float LeftFireAlpha = 0.80f;

        /// <summary>右上角潜行键透明度：比射击键更淡，避免遮挡小地图下沿。</summary>
        public const float SwimAlpha = 0.70f;

        public static readonly Vector2 DefaultMoveStickPos = new Vector2(200f, 200f);

        /// <summary>3/4 指左开火键基准位置：屏幕左上角（左下角锚点坐标系）。</summary>
        public static readonly Vector2 LeftFirePos = new Vector2(271f, 699f);

        /// <summary>4 指潜行键基准位置：屏幕右上角（右下角锚点坐标系）。</summary>
        public static readonly Vector2 FourFingerSwimPos = new Vector2(-360f, 750f);

        /// <summary>4 指射击大键略向左收，给右拇指更顺手的按压角度。</summary>
        public static readonly Vector2 FourFingerFirePos = new Vector2(-260f, 164f);

        // 兼容旧命名的别名：3/4 指共用同一个左上角开火位。
        public static readonly Vector2 ThreeFingerLeftFirePos = LeftFirePos;
        public static readonly Vector2 FourFingerLeftFirePos = LeftFirePos;

        public static string GetLabel(MobileLayoutPreset preset)
        {
            switch (preset)
            {
                case MobileLayoutPreset.ThreeFinger:
                    return "3指";
                case MobileLayoutPreset.FourFinger:
                    return "4指";
                case MobileLayoutPreset.TwoFinger:
                default:
                    return "2指";
            }
        }

        public static bool UsesLeftFire(MobileLayoutPreset preset)
        {
            return preset != MobileLayoutPreset.TwoFinger;
        }

        public static Vector2 GetLeftFirePosition(MobileLayoutPreset preset)
        {
            return LeftFirePos;
        }

        /// <summary>
        /// 生成整套布局配置。scale 为当前设备 DPI 系数（MobileUiMetrics.ComputeScale），
        /// 位置与按键缩放同步乘该系数，保证高/低 DPI 设备上的物理尺寸一致。
        /// </summary>
        public static MobileLayoutConfig Build(MobileLayoutPreset preset, float scale = 1f)
        {
            float s = Mathf.Clamp(scale, 0.5f, 2.0f);
            var config = new MobileLayoutConfig
            {
                preset = (int)preset,
                leftFireVisible = UsesLeftFire(preset)
            };

            // 摇杆不参与 DPI 缩放（与 ApplyDpiScale 的既有行为一致），保持基准位置。
            config.Set("MoveStick", DefaultMoveStickPos, 1.0f, DefaultMoveStickAlpha);

            var layout = MobileTouchUI.DefaultLayout;
            for (int i = 0; i < layout.Length; i++)
            {
                var entry = layout[i];
                Vector2 pos = entry.pos * s;
                float itemScale = s;
                float itemAlpha = 1.0f;

                if (preset == MobileLayoutPreset.FourFinger)
                {
                    if (entry.id == "Fire")
                    {
                        pos = FourFingerFirePos * s;
                    }
                    else if (entry.id == "Swim")
                    {
                        pos = FourFingerSwimPos * s;
                        itemScale = s * TopActionScale;
                        itemAlpha = SwimAlpha;
                    }
                }

                config.Set(entry.id, pos, itemScale, itemAlpha);
            }

            // 左上角开火键在 2/3/4 指下保持同一位置与尺寸；是否显示由 leftFireVisible 控制。
            config.Set("LeftFire", LeftFirePos * s, s * TopActionScale, LeftFireAlpha);
            return config;
        }

        /// <summary>预设相关按键的基准直径（用于防重叠校验与布局计算）。</summary>
        public static float GetBaseSize(string id)
        {
            switch (id)
            {
                case "MoveStick":
                    return MoveStickDiameter;
                case "LeftFire":
                    return LeftFireSize;
                case "Fire":
                    return 176f;
                case "Swim":
                    return 140f;
                case "Jump":
                    return 132f;
                case "Bomb":
                    return 132f;
                case "Special":
                    return 148f;
                default:
                    return 0f;
            }
        }
    }
}