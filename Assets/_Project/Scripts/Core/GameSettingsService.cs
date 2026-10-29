using System;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 全局游戏设置管理服务（单例静态类 + PlayerPrefs 持久化）：
    /// 包含主音量 (0~1)、无障碍屏幕震动比例 (0~1)、帧率上限 (30/60/120/无限制)、
    /// 第三人称鼠标/触控视角灵敏度 (0.25~2.5) 与 Y 轴反转。
    /// 启动时通过 RuntimeInitializeOnLoadMethod 自动读取并应用；
    /// 支持动态修改、重置为默认值及事件广播。
    /// </summary>
    public static class GameSettingsService
    {
        private static bool _initialized;
        private static float _masterVolume = GameSettingsMath.DefaultMasterVolume;
        private static float _shakeScale = GameSettingsMath.DefaultShakeScale;
        private static int _targetFrameRate = GameSettingsMath.DefaultTargetFrameRate;
        private static float _mouseLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
        private static float _touchLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
        private static bool _invertLookY;

        public static event Action<float> MasterVolumeChanged;
        public static event Action<float> ShakeScaleChanged;
        public static event Action<int> TargetFrameRateChanged;
        public static event Action<float> MouseLookSensitivityChanged;
        public static event Action<float> TouchLookSensitivityChanged;
        public static event Action<bool> InvertLookYChanged;
        public static event Action SettingsChanged;

        /// <summary>
        /// 主音量（0.0 ~ 1.0），直接作用于 <see cref="AudioListener.volume"/>。
        /// </summary>
        public static float MasterVolume
        {
            get
            {
                EnsureInitialized();
                return _masterVolume;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampVolume(value);
                if (Mathf.Approximately(_masterVolume, clamped))
                    return;

                _masterVolume = clamped;
                ApplyVolume();
                SaveVolume();
                MasterVolumeChanged?.Invoke(_masterVolume);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 屏幕震动缩放比例（0.0 ~ 1.0）。1.0 为默认全强度，0.0 为完全关闭（无障碍模式）。
        /// 由 <see cref="Player.TopDownCameraFollow"/> 读取并乘到震屏位移与旋转上。
        /// </summary>
        public static float ShakeScale
        {
            get
            {
                EnsureInitialized();
                return _shakeScale;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampShake(value);
                if (Mathf.Approximately(_shakeScale, clamped))
                    return;

                _shakeScale = clamped;
                SaveShake();
                ShakeScaleChanged?.Invoke(_shakeScale);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 目标帧率档位（30、60、120 或 -1 代表无限制）。
        /// 设置具体数值时关闭垂直同步，无限制时启用垂直同步。
        /// </summary>
        public static int TargetFrameRate
        {
            get
            {
                EnsureInitialized();
                return _targetFrameRate;
            }
            set
            {
                EnsureInitialized();
                int normalized = GameSettingsMath.NormalizeFrameRate(value);
                if (_targetFrameRate == normalized)
                    return;

                _targetFrameRate = normalized;
                ApplyFrameRate();
                SaveFrameRate();
                TargetFrameRateChanged?.Invoke(_targetFrameRate);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>鼠标视角灵敏度（0.25~2.5，默认 1.0）。</summary>
        public static float MouseLookSensitivity
        {
            get
            {
                EnsureInitialized();
                return _mouseLookSensitivity;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampLookSensitivity(value);
                if (Mathf.Approximately(_mouseLookSensitivity, clamped))
                    return;

                _mouseLookSensitivity = clamped;
                SaveMouseLookSensitivity();
                MouseLookSensitivityChanged?.Invoke(_mouseLookSensitivity);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>触屏滑屏视角灵敏度（0.25~2.5，默认 1.0）。</summary>
        public static float TouchLookSensitivity
        {
            get
            {
                EnsureInitialized();
                return _touchLookSensitivity;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampLookSensitivity(value);
                if (Mathf.Approximately(_touchLookSensitivity, clamped))
                    return;

                _touchLookSensitivity = clamped;
                SaveTouchLookSensitivity();
                TouchLookSensitivityChanged?.Invoke(_touchLookSensitivity);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>是否反转视角 Y 轴（默认关闭）。</summary>
        public static bool InvertLookY
        {
            get
            {
                EnsureInitialized();
                return _invertLookY;
            }
            set
            {
                EnsureInitialized();
                if (_invertLookY == value)
                    return;

                _invertLookY = value;
                SaveInvertLookY();
                InvertLookYChanged?.Invoke(_invertLookY);
                SettingsChanged?.Invoke();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 确保设置服务已初始化（从 PlayerPrefs 读取并应用到引擎）。可被测试或代码显式调用。
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            LoadFromPlayerPrefs();
            ApplyAll();
        }

        /// <summary>
        /// 重置所有设置为系统默认值（主音量 1.0、震动 1.0、帧率 60、灵敏度 1.0、不反转 Y）。
        /// </summary>
        public static void ResetToDefaults()
        {
            EnsureInitialized();
            _masterVolume = GameSettingsMath.DefaultMasterVolume;
            _shakeScale = GameSettingsMath.DefaultShakeScale;
            _targetFrameRate = GameSettingsMath.DefaultTargetFrameRate;
            _mouseLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
            _touchLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
            _invertLookY = false;

            SaveAll();
            ApplyAll();

            MasterVolumeChanged?.Invoke(_masterVolume);
            ShakeScaleChanged?.Invoke(_shakeScale);
            TargetFrameRateChanged?.Invoke(_targetFrameRate);
            MouseLookSensitivityChanged?.Invoke(_mouseLookSensitivity);
            TouchLookSensitivityChanged?.Invoke(_touchLookSensitivity);
            InvertLookYChanged?.Invoke(_invertLookY);
            SettingsChanged?.Invoke();
        }

        private static void LoadFromPlayerPrefs()
        {
            float rawVol = PlayerPrefs.GetFloat(GameSettingsMath.KeyMasterVolume, GameSettingsMath.DefaultMasterVolume);
            _masterVolume = GameSettingsMath.ClampVolume(rawVol);

            float rawShake = PlayerPrefs.GetFloat(GameSettingsMath.KeyShakeScale, GameSettingsMath.DefaultShakeScale);
            _shakeScale = GameSettingsMath.ClampShake(rawShake);

            int rawFps = PlayerPrefs.GetInt(GameSettingsMath.KeyTargetFrameRate, GameSettingsMath.DefaultTargetFrameRate);
            _targetFrameRate = GameSettingsMath.NormalizeFrameRate(rawFps);

            _mouseLookSensitivity = GameSettingsMath.ClampLookSensitivity(
                PlayerPrefs.GetFloat(GameSettingsMath.KeyMouseLookSensitivity, GameSettingsMath.DefaultLookSensitivity));
            _touchLookSensitivity = GameSettingsMath.ClampLookSensitivity(
                PlayerPrefs.GetFloat(GameSettingsMath.KeyTouchLookSensitivity, GameSettingsMath.DefaultLookSensitivity));
            _invertLookY = PlayerPrefs.GetInt(GameSettingsMath.KeyInvertLookY, 0) != 0;
        }

        private static void SaveVolume()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMasterVolume, _masterVolume);
            PlayerPrefs.Save();
        }

        private static void SaveShake()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyShakeScale, _shakeScale);
            PlayerPrefs.Save();
        }

        private static void SaveFrameRate()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyTargetFrameRate, _targetFrameRate);
            PlayerPrefs.Save();
        }

        private static void SaveMouseLookSensitivity()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMouseLookSensitivity, _mouseLookSensitivity);
            PlayerPrefs.Save();
        }

        private static void SaveTouchLookSensitivity()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyTouchLookSensitivity, _touchLookSensitivity);
            PlayerPrefs.Save();
        }

        private static void SaveInvertLookY()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyInvertLookY, _invertLookY ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void SaveAll()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMasterVolume, _masterVolume);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyShakeScale, _shakeScale);
            PlayerPrefs.SetInt(GameSettingsMath.KeyTargetFrameRate, _targetFrameRate);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMouseLookSensitivity, _mouseLookSensitivity);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyTouchLookSensitivity, _touchLookSensitivity);
            PlayerPrefs.SetInt(GameSettingsMath.KeyInvertLookY, _invertLookY ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 将当前配置应用到 Unity 引擎底层（AudioListener 与 QualitySettings/Application）。
        /// </summary>
        public static void ApplyAll()
        {
            ApplyVolume();
            ApplyFrameRate();
        }

        private static void ApplyVolume()
        {
            AudioListener.volume = _masterVolume;
        }

        private static void ApplyFrameRate()
        {
            if (_targetFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _targetFrameRate;
            }
            else
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
        }
    }
}
