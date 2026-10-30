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
    /// 2. 3 指：在 2 指基础上增加左下角「左开火键」，由左食指负责开火；
    /// 3. 4 指：再把跳跃键移到右上角，由右食指负责跳跃，右拇指持续瞄准/开火。
    /// 数值与 MobileTouchUI.DefaultLayout 共用一份，避免双份排布漂移。
    /// </summary>
    public static class MobileLayoutPresets
    {
        public const float LeftFireSize = 160f;
        public const float MoveStickDiameter = 340f;
        public const float DefaultMoveStickAlpha = 0.55f;

        public static readonly Vector2 DefaultMoveStickPos = new Vector2(200f, 200f);
        public static readonly Vector2 ThreeFingerLeftFirePos = new Vector2(190f, 470f);
        public static readonly Vector2 FourFingerLeftFirePos = new Vector2(190f, 500f);
        public static readonly Vector2 FourFingerJumpPos = new Vector2(-135f, 500f);

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
            return preset == MobileLayoutPreset.FourFinger
                ? FourFingerLeftFirePos
                : ThreeFingerLeftFirePos;
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
                if (preset == MobileLayoutPreset.FourFinger && entry.id == "Jump")
                {
                    pos = FourFingerJumpPos * s;
                }

                config.Set(entry.id, pos, s, 1.0f);
            }

            config.Set("LeftFire", GetLeftFirePosition(preset) * s, s, 1.0f);
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