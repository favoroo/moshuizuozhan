using System;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 全局游戏设置管理服务（单例静态类 + PlayerPrefs 持久化）：
    /// 包含主音量 (0~1)、音乐/音效分轨音量 (0~1)、无障碍屏幕震动比例 (0~1)、帧率上限 (30/60/120/无限制)、
/// 第三人称鼠标/触控视角灵敏度 (0.25~2.5)、Y 轴反转、HUD 显示开关（FPS 角标、小地图）、
/// 比赛时长（2/3/5 分钟）、Bot 难度（新手/轻松/普通/硬核），
/// 以及移动端辅助操作：固定摇杆、开火键拖动转视角、辅助瞄准档位（关/弱/中/强）。
    /// 启动时通过 RuntimeInitializeOnLoadMethod 自动读取并应用；
    /// 支持动态修改、重置为默认值及事件广播。
    /// </summary>
    public static class GameSettingsService
    {
        private static bool _initialized;
        private static float _masterVolume = GameSettingsMath.DefaultMasterVolume;
        private static float _musicVolume = GameSettingsMath.DefaultMusicVolume;
        private static float _sfxVolume = GameSettingsMath.DefaultSfxVolume;
        private static float _shakeScale = GameSettingsMath.DefaultShakeScale;
        private static int _targetFrameRate = GameSettingsMath.DefaultTargetFrameRate;
        private static float _mouseLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
        private static float _touchLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
        private static bool _invertLookY;
        private static bool _fixedJoystick = GameSettingsMath.DefaultFixedJoystick;
        private static bool _fireButtonRotatesCamera = GameSettingsMath.DefaultFireButtonRotatesCamera;
        private static bool _showFps = GameSettingsMath.DefaultShowFps;
        private static bool _showMinimap = GameSettingsMath.DefaultShowMinimap;
        private static int _aimAssistLevel = GameSettingsMath.DefaultAimAssistLevel;
        private static int _botDifficulty = GameSettingsMath.DefaultBotDifficulty;
        private static int _matchDurationMinutes = GameSettingsMath.DefaultMatchDurationMinutes;

        public static event Action<float> MasterVolumeChanged;
        public static event Action<float> MusicVolumeChanged;
        public static event Action<float> SfxVolumeChanged;
        public static event Action<float> ShakeScaleChanged;
        public static event Action<int> TargetFrameRateChanged;
        public static event Action<float> MouseLookSensitivityChanged;
        public static event Action<float> TouchLookSensitivityChanged;
        public static event Action<bool> InvertLookYChanged;
        public static event Action<bool> FixedJoystickChanged;
        public static event Action<bool> FireButtonRotatesCameraChanged;
        public static event Action<bool> ShowFpsChanged;
        public static event Action<bool> ShowMinimapChanged;
        public static event Action<int> AimAssistLevelChanged;
        public static event Action<int> BotDifficultyChanged;
        public static event Action<int> MatchDurationChanged;
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
        /// 音乐分轨音量（0.0 ~ 1.0，主音量之下的相对乘数）。
        /// 由 <see cref="AudioManager.ApplyMusicVolume"/> 每帧乘到 BGM 源上，改值即时生效。
        /// </summary>
        public static float MusicVolume
        {
            get
            {
                EnsureInitialized();
                return _musicVolume;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampVolume(value);
                if (Mathf.Approximately(_musicVolume, clamped))
                    return;

                _musicVolume = clamped;
                SaveMusicVolume();
                MusicVolumeChanged?.Invoke(_musicVolume);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 音效分轨音量（0.0 ~ 1.0，主音量之下的相对乘数），覆盖一次性音效、UI 音、
        /// 播报语音与循环音；由 <see cref="AudioManager"/> 在各播放收口处读取。
        /// </summary>
        public static float SfxVolume
        {
            get
            {
                EnsureInitialized();
                return _sfxVolume;
            }
            set
            {
                EnsureInitialized();
                float clamped = GameSettingsMath.ClampVolume(value);
                if (Mathf.Approximately(_sfxVolume, clamped))
                    return;

                _sfxVolume = clamped;
                SaveSfxVolume();
                SfxVolumeChanged?.Invoke(_sfxVolume);
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

        /// <summary>移动端摇杆是否固定底座（默认关闭，保持浮动跟手摇杆）。</summary>
        public static bool FixedJoystick
        {
            get
            {
                EnsureInitialized();
                return _fixedJoystick;
            }
            set
            {
                EnsureInitialized();
                if (_fixedJoystick == value)
                    return;

                _fixedJoystick = value;
                SaveFixedJoystick();
                FixedJoystickChanged?.Invoke(_fixedJoystick);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>右开火键拖动时是否同时旋转视角（默认开启，关闭后 3/4 指开火手不会抖视角）。</summary>
        public static bool FireButtonRotatesCamera
        {
            get
            {
                EnsureInitialized();
                return _fireButtonRotatesCamera;
            }
            set
            {
                EnsureInitialized();
                if (_fireButtonRotatesCamera == value)
                    return;

                _fireButtonRotatesCamera = value;
                SaveFireButtonRotatesCamera();
                FireButtonRotatesCameraChanged?.Invoke(_fireButtonRotatesCamera);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>是否在 HUD 左上角显示实时帧率角标（默认关闭）。由 <see cref="UI.HudController"/> 读取。</summary>
        public static bool ShowFps
        {
            get
            {
                EnsureInitialized();
                return _showFps;
            }
            set
            {
                EnsureInitialized();
                if (_showFps == value)
                    return;

                _showFps = value;
                SaveShowFps();
                ShowFpsChanged?.Invoke(_showFps);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>是否显示 HUD 小地图（默认开启）。由 <see cref="UI.HudController"/> 读取。</summary>
        public static bool ShowMinimap
        {
            get
            {
                EnsureInitialized();
                return _showMinimap;
            }
            set
            {
                EnsureInitialized();
                if (_showMinimap == value)
                    return;

                _showMinimap = value;
                SaveShowMinimap();
                ShowMinimapChanged?.Invoke(_showMinimap);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>移动端辅助瞄准档位：0 关 / 1 弱 / 2 中 / 3 强（默认中档 0.8m）。</summary>
        public static int AimAssistLevel
        {
            get
            {
                EnsureInitialized();
                return _aimAssistLevel;
            }
            set
            {
                EnsureInitialized();
                int normalized = GameSettingsMath.NormalizeAimAssistLevel(value);
                if (_aimAssistLevel == normalized)
                    return;

                _aimAssistLevel = normalized;
                SaveAimAssistLevel();
                AimAssistLevelChanged?.Invoke(_aimAssistLevel);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Bot 难度档位：0 新手 / 1 轻松 / 2 普通 / 3 硬核（默认普通）。
        /// BotController 订阅 <see cref="BotDifficultyChanged"/> 后即时重建难度缩放配置。
        /// </summary>
        public static int BotDifficulty
        {
            get
            {
                EnsureInitialized();
                return _botDifficulty;
            }
            set
            {
                EnsureInitialized();
                int normalized = GameSettingsMath.NormalizeBotDifficulty(value);
                if (_botDifficulty == normalized)
                    return;

                _botDifficulty = normalized;
                SaveBotDifficulty();
                BotDifficultyChanged?.Invoke(_botDifficulty);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>
        /// 比赛时长（分钟）：2 / 3 / 5，默认 2 分钟。
        /// 首页 Hero 卡段选写入；MatchManager 在开局 / 重开 / 返回菜单时读取换算成秒。
        /// </summary>
        public static int MatchDurationMinutes
        {
            get
            {
                EnsureInitialized();
                return _matchDurationMinutes;
            }
            set
            {
                EnsureInitialized();
                int normalized = GameSettingsMath.NormalizeMatchDurationMinutes(value);
                if (_matchDurationMinutes == normalized)
                    return;

                _matchDurationMinutes = normalized;
                SaveMatchDuration();
                MatchDurationChanged?.Invoke(_matchDurationMinutes);
                SettingsChanged?.Invoke();
            }
        }

        /// <summary>当前选择的比赛时长（秒），供 MatchManager 计时直接使用。</summary>
        public static float MatchDurationSeconds =>
            GameSettingsMath.GetMatchDurationSeconds(MatchDurationMinutes);

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
        /// 重置所有设置为系统默认值（主音量 1.0、音乐/音效 1.0、震动 1.0、帧率 60、灵敏度 1.0、
        /// 不反转 Y、FPS 角标关、小地图开）。
        /// </summary>
        public static void ResetToDefaults()
        {
            EnsureInitialized();
            _masterVolume = GameSettingsMath.DefaultMasterVolume;
            _musicVolume = GameSettingsMath.DefaultMusicVolume;
            _sfxVolume = GameSettingsMath.DefaultSfxVolume;
            _shakeScale = GameSettingsMath.DefaultShakeScale;
            _targetFrameRate = GameSettingsMath.DefaultTargetFrameRate;
            _mouseLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
            _touchLookSensitivity = GameSettingsMath.DefaultLookSensitivity;
            _invertLookY = false;
            _fixedJoystick = GameSettingsMath.DefaultFixedJoystick;
            _fireButtonRotatesCamera = GameSettingsMath.DefaultFireButtonRotatesCamera;
            _showFps = GameSettingsMath.DefaultShowFps;
            _showMinimap = GameSettingsMath.DefaultShowMinimap;
            _aimAssistLevel = GameSettingsMath.DefaultAimAssistLevel;
            _botDifficulty = GameSettingsMath.DefaultBotDifficulty;
            _matchDurationMinutes = GameSettingsMath.DefaultMatchDurationMinutes;

            SaveAll();
            ApplyAll();

            MasterVolumeChanged?.Invoke(_masterVolume);
            MusicVolumeChanged?.Invoke(_musicVolume);
            SfxVolumeChanged?.Invoke(_sfxVolume);
            ShakeScaleChanged?.Invoke(_shakeScale);
            TargetFrameRateChanged?.Invoke(_targetFrameRate);
            MouseLookSensitivityChanged?.Invoke(_mouseLookSensitivity);
            TouchLookSensitivityChanged?.Invoke(_touchLookSensitivity);
            InvertLookYChanged?.Invoke(_invertLookY);
            FixedJoystickChanged?.Invoke(_fixedJoystick);
            FireButtonRotatesCameraChanged?.Invoke(_fireButtonRotatesCamera);
            ShowFpsChanged?.Invoke(_showFps);
            ShowMinimapChanged?.Invoke(_showMinimap);
            AimAssistLevelChanged?.Invoke(_aimAssistLevel);
            BotDifficultyChanged?.Invoke(_botDifficulty);
            MatchDurationChanged?.Invoke(_matchDurationMinutes);
            SettingsChanged?.Invoke();
        }

        private static void LoadFromPlayerPrefs()
        {
            float rawVol = PlayerPrefs.GetFloat(GameSettingsMath.KeyMasterVolume, GameSettingsMath.DefaultMasterVolume);
            _masterVolume = GameSettingsMath.ClampVolume(rawVol);

            _musicVolume = GameSettingsMath.ClampVolume(
                PlayerPrefs.GetFloat(GameSettingsMath.KeyMusicVolume, GameSettingsMath.DefaultMusicVolume));
            _sfxVolume = GameSettingsMath.ClampVolume(
                PlayerPrefs.GetFloat(GameSettingsMath.KeySfxVolume, GameSettingsMath.DefaultSfxVolume));

            float rawShake = PlayerPrefs.GetFloat(GameSettingsMath.KeyShakeScale, GameSettingsMath.DefaultShakeScale);
            _shakeScale = GameSettingsMath.ClampShake(rawShake);

            int rawFps = PlayerPrefs.GetInt(GameSettingsMath.KeyTargetFrameRate, GameSettingsMath.DefaultTargetFrameRate);
            _targetFrameRate = GameSettingsMath.NormalizeFrameRate(rawFps);

            _mouseLookSensitivity = GameSettingsMath.ClampLookSensitivity(
                PlayerPrefs.GetFloat(GameSettingsMath.KeyMouseLookSensitivity, GameSettingsMath.DefaultLookSensitivity));
            _touchLookSensitivity = GameSettingsMath.ClampLookSensitivity(
                PlayerPrefs.GetFloat(GameSettingsMath.KeyTouchLookSensitivity, GameSettingsMath.DefaultLookSensitivity));
            _invertLookY = PlayerPrefs.GetInt(GameSettingsMath.KeyInvertLookY, 0) != 0;
            _fixedJoystick = PlayerPrefs.GetInt(GameSettingsMath.KeyFixedJoystick, 0) != 0;
            _fireButtonRotatesCamera = PlayerPrefs.GetInt(GameSettingsMath.KeyFireButtonRotatesCamera, 1) != 0;
            _showFps = PlayerPrefs.GetInt(GameSettingsMath.KeyShowFps, GameSettingsMath.DefaultShowFps ? 1 : 0) != 0;
            _showMinimap = PlayerPrefs.GetInt(GameSettingsMath.KeyShowMinimap, GameSettingsMath.DefaultShowMinimap ? 1 : 0) != 0;
            _aimAssistLevel = GameSettingsMath.NormalizeAimAssistLevel(
                PlayerPrefs.GetInt(GameSettingsMath.KeyAimAssistLevel, GameSettingsMath.DefaultAimAssistLevel));
            _botDifficulty = LoadBotDifficultyWithMigration();
            _matchDurationMinutes = GameSettingsMath.NormalizeMatchDurationMinutes(
                PlayerPrefs.GetInt(GameSettingsMath.KeyMatchDurationMinutes,
                    GameSettingsMath.DefaultMatchDurationMinutes));
        }

        /// <summary>
        /// 读取 Bot 难度并做一次存档迁移：v0 是三档（0 轻松 / 1 普通 / 2 硬核），
        /// v1 扩成四档（0 新手 / 1 轻松 / 2 普通 / 3 硬核）。旧值整体 +1，
        /// 避免老玩家选中的「轻松」被静默解释成「新手」；无存档时保持默认「普通」。
        /// </summary>
        private static int LoadBotDifficultyWithMigration()
        {
            bool hasStored = PlayerPrefs.HasKey(GameSettingsMath.KeyBotDifficulty);
            int raw = PlayerPrefs.GetInt(GameSettingsMath.KeyBotDifficulty, GameSettingsMath.DefaultBotDifficulty);
            int storedVersion = PlayerPrefs.GetInt(GameSettingsMath.KeyBotDifficultyVersion, 0);
            int migrated = GameSettingsMath.MigrateBotDifficulty(raw, hasStored, storedVersion);

            if (hasStored && storedVersion < GameSettingsMath.BotDifficultyVersion)
            {
                PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficulty, migrated);
                PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficultyVersion, GameSettingsMath.BotDifficultyVersion);
                PlayerPrefs.Save();
            }

            return migrated;
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

        private static void SaveMusicVolume()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMusicVolume, _musicVolume);
            PlayerPrefs.Save();
        }

        private static void SaveSfxVolume()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeySfxVolume, _sfxVolume);
            PlayerPrefs.Save();
        }

        private static void SaveShowFps()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyShowFps, _showFps ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void SaveShowMinimap()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyShowMinimap, _showMinimap ? 1 : 0);
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

        private static void SaveFixedJoystick()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyFixedJoystick, _fixedJoystick ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void SaveFireButtonRotatesCamera()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyFireButtonRotatesCamera, _fireButtonRotatesCamera ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void SaveAimAssistLevel()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyAimAssistLevel, _aimAssistLevel);
            PlayerPrefs.Save();
        }

        private static void SaveBotDifficulty()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficulty, _botDifficulty);
            PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficultyVersion, GameSettingsMath.BotDifficultyVersion);
            PlayerPrefs.Save();
        }

        private static void SaveMatchDuration()
        {
            PlayerPrefs.SetInt(GameSettingsMath.KeyMatchDurationMinutes, _matchDurationMinutes);
            PlayerPrefs.Save();
        }

        private static void SaveAll()
        {
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMasterVolume, _masterVolume);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMusicVolume, _musicVolume);
            PlayerPrefs.SetFloat(GameSettingsMath.KeySfxVolume, _sfxVolume);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyShakeScale, _shakeScale);
            PlayerPrefs.SetInt(GameSettingsMath.KeyTargetFrameRate, _targetFrameRate);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyMouseLookSensitivity, _mouseLookSensitivity);
            PlayerPrefs.SetFloat(GameSettingsMath.KeyTouchLookSensitivity, _touchLookSensitivity);
            PlayerPrefs.SetInt(GameSettingsMath.KeyInvertLookY, _invertLookY ? 1 : 0);
            PlayerPrefs.SetInt(GameSettingsMath.KeyFixedJoystick, _fixedJoystick ? 1 : 0);
            PlayerPrefs.SetInt(GameSettingsMath.KeyFireButtonRotatesCamera, _fireButtonRotatesCamera ? 1 : 0);
            PlayerPrefs.SetInt(GameSettingsMath.KeyShowFps, _showFps ? 1 : 0);
            PlayerPrefs.SetInt(GameSettingsMath.KeyShowMinimap, _showMinimap ? 1 : 0);
            PlayerPrefs.SetInt(GameSettingsMath.KeyAimAssistLevel, _aimAssistLevel);
            PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficulty, _botDifficulty);
            PlayerPrefs.SetInt(GameSettingsMath.KeyBotDifficultyVersion, GameSettingsMath.BotDifficultyVersion);
            PlayerPrefs.SetInt(GameSettingsMath.KeyMatchDurationMinutes, _matchDurationMinutes);
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
