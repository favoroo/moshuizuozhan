using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 游戏设置相关的纯数学函数、区间裁切与常量（纯逻辑，EditMode 友好，不依赖 Unity 运行时生命周期）。
    /// </summary>
    public static class GameSettingsMath
    {
        public const string KeyMasterVolume = "P1_Settings_MasterVolume";
        public const string KeyShakeScale = "P1_Settings_ShakeScale";
        public const string KeyTargetFrameRate = "P1_Settings_TargetFrameRate";
        public const string KeyMouseLookSensitivity = "P1_Settings_MouseLookSensitivity";
        public const string KeyTouchLookSensitivity = "P1_Settings_TouchLookSensitivity";
        public const string KeyInvertLookY = "P1_Settings_InvertLookY";

        public const float DefaultMasterVolume = 1.0f;
        public const float DefaultShakeScale = 1.0f;
        public const int DefaultTargetFrameRate = 60;
        public const float DefaultLookSensitivity = 1.0f;
        public const float MinLookSensitivity = 0.25f;
        public const float MaxLookSensitivity = 2.5f;

        public const int FrameRate30 = 30;
        public const int FrameRate60 = 60;
        public const int FrameRate120 = 120;
        public const int FrameRateUnlimited = -1;

        public static readonly int[] AvailableFrameRates = { FrameRate30, FrameRate60, FrameRate120, FrameRateUnlimited };

        /// <summary>
        /// 裁切主音量到 [0.0, 1.0]。若为非法浮点数（NaN 或 Inf）则返回默认值 1.0。
        /// </summary>
        public static float ClampVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultMasterVolume;
            return Mathf.Clamp01(value);
        }

        /// <summary>
        /// 裁切屏幕震动强度到 [0.0, 1.0]。1.0 为默认全强度，0.0 为完全无障碍关闭震屏。
        /// </summary>
        public static float ClampShake(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultShakeScale;
            return Mathf.Clamp01(value);
        }

        /// <summary>
        /// 裁切视角灵敏度到 [0.25, 2.5]。NaN/Inf 回落到默认 1.0。
        /// </summary>
        public static float ClampLookSensitivity(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultLookSensitivity;
            return Mathf.Clamp(value, MinLookSensitivity, MaxLookSensitivity);
        }

        /// <summary>
        /// 规范化目标帧率：仅允许 30、60、120 与 -1（无限制）。
        /// 非标准输入映射到最接近的合法档位。
        /// </summary>
        public static int NormalizeFrameRate(int fps)
        {
            if (fps <= 0 || fps == FrameRateUnlimited)
                return FrameRateUnlimited;

            if (fps < 45)
                return FrameRate30;
            if (fps < 90)
                return FrameRate60;

            return FrameRate120;
        }

        /// <summary>
        /// 获取帧率档位的展示文案。
        /// </summary>
        public static string GetFrameRateLabel(int fps)
        {
            int normalized = NormalizeFrameRate(fps);
            switch (normalized)
            {
                case FrameRate30:
                    return "30 FPS";
                case FrameRate60:
                    return "60 FPS";
                case FrameRate120:
                    return "120 FPS";
                case FrameRateUnlimited:
                default:
                    return "无限制";
            }
        }
    }
}
