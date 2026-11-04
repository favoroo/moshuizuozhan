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
        public const string KeyFixedJoystick = "P1_Settings_FixedJoystick";
        public const string KeyFireButtonRotatesCamera = "P1_Settings_FireRotatesCamera";
        public const string KeyAimAssistLevel = "P1_Settings_AimAssistLevel";
        public const string KeyBotDifficulty = "P1_Settings_BotDifficulty";
        public const string KeyBotDifficultyVersion = "P1_Settings_BotDifficultyVersion";
        public const string KeyMusicVolume = "P1_Settings_MusicVolume";
        public const string KeySfxVolume = "P1_Settings_SfxVolume";
        public const string KeyShowFps = "P1_Settings_ShowFps";
        public const string KeyShowMinimap = "P1_Settings_ShowMinimap";

        public const float DefaultMasterVolume = 1.0f;
        /// <summary>音乐分轨默认音量（主音量之下的相对乘数）。</summary>
        public const float DefaultMusicVolume = 1.0f;
        /// <summary>音效分轨默认音量（主音量之下的相对乘数，含 UI 音与播报语音）。</summary>
        public const float DefaultSfxVolume = 1.0f;
        public const float DefaultShakeScale = 1.0f;
        public const int DefaultTargetFrameRate = 60;
        public const float DefaultLookSensitivity = 1.0f;
        public const float MinLookSensitivity = 0.25f;
        public const float MaxLookSensitivity = 2.5f;

        public const bool DefaultFixedJoystick = false;
        public const bool DefaultFireButtonRotatesCamera = true;
        /// <summary>HUD 实时帧率角标默认关闭。</summary>
        public const bool DefaultShowFps = false;
        /// <summary>HUD 小地图默认显示。</summary>
        public const bool DefaultShowMinimap = true;
        public const int DefaultAimAssistLevel = 2;
        public const int MinAimAssistLevel = 0;
        public const int MaxAimAssistLevel = 3;

        /// <summary>
        /// Bot 难度档位：0 新手 / 1 轻松 / 2 普通 / 3 硬核（与 P1.AI.BotDifficultyMath 一一对应）。
        /// 默认普通（恒等档）；新手档是后加的最低档，旧存档通过 <see cref="BotDifficultyVersion"/> 迁移。
        /// </summary>
        public const int DefaultBotDifficulty = 2;
        public const int MinBotDifficulty = 0;
        public const int MaxBotDifficulty = 3;
        public const int BotDifficultyBeginner = 0;
        public const int BotDifficultyEasy = 1;
        public const int BotDifficultyNormal = 2;
        public const int BotDifficultyHard = 3;

        /// <summary>
        /// 难度存档版本：v1 起从三档（0 轻松/1 普通/2 硬核）扩为四档（0 新手/1 轻松/2 普通/3 硬核）。
        /// 旧值整体 +1，避免老玩家选中的「轻松」被静默解释成「新手」。
        /// </summary>
        public const int BotDifficultyVersion = 1;

        public const float AimAssistRadiusWeak = 0.5f;
        public const float AimAssistRadiusMedium = 0.8f;
        public const float AimAssistRadiusStrong = 1.2f;

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
        /// 规范化移动端辅助瞄准档位：0 关 / 1 弱 / 2 中 / 3 强。
        /// </summary>
        public static int NormalizeAimAssistLevel(int level)
        {
            if (level <= MinAimAssistLevel)
                return MinAimAssistLevel;
            if (level >= MaxAimAssistLevel)
                return MaxAimAssistLevel;
            return level;
        }

        /// <summary>辅助瞄准档位对应的吸附半径（米）；0 档返回 0。</summary>
        public static float GetAimAssistRadius(int level)
        {
            switch (NormalizeAimAssistLevel(level))
            {
                case 1:
                    return AimAssistRadiusWeak;
                case 3:
                    return AimAssistRadiusStrong;
                case 2:
                    return AimAssistRadiusMedium;
                case MinAimAssistLevel:
                default:
                    return 0f;
            }
        }

        /// <summary>辅助瞄准档位的中文展示文案。</summary>
        public static string GetAimAssistLevelLabel(int level)
        {
            switch (NormalizeAimAssistLevel(level))
            {
                case 1:
                    return "弱";
                case 2:
                    return "中";
                case 3:
                    return "强";
                case MinAimAssistLevel:
                default:
                    return "关";
            }
        }

        /// <summary>裁切 Bot 难度到 [0, 3]；越界按最近合法档位收口。</summary>
        public static int NormalizeBotDifficulty(int level)
        {
            if (level <= MinBotDifficulty)
                return MinBotDifficulty;
            if (level >= MaxBotDifficulty)
                return MaxBotDifficulty;
            return level;
        }

        /// <summary>
        /// 把旧版三档存档值迁移到当前四档：v0（0 轻松/1 普通/2 硬核）整体 +1 变为
        /// v1（0 新手/1 轻松/2 普通/3 硬核）。无存档或已是当前版本时原样规范化返回。
        /// </summary>
        public static int MigrateBotDifficulty(int storedValue, bool hasStored, int storedVersion)
        {
            if (hasStored && storedVersion < BotDifficultyVersion)
                return NormalizeBotDifficulty(storedValue + 1);
            return NormalizeBotDifficulty(storedValue);
        }

        /// <summary>Bot 难度的中文展示文案（只使用项目字集里已有的字）。</summary>
        public static string GetBotDifficultyLabel(int level)
        {
            switch (NormalizeBotDifficulty(level))
            {
                case BotDifficultyBeginner:
                    return "新手";
                case BotDifficultyEasy:
                    return "轻松";
                case BotDifficultyHard:
                    return "硬核";
                default:
                    return "普通";
            }
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
