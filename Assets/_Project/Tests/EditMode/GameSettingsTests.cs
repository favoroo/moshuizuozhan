using NUnit.Framework;
using P1.Core;

namespace P1.Tests
{
    /// <summary>
    /// 系统设置与无障碍纯逻辑验证（EditMode）：
    /// 确保 GameSettingsMath 的音量裁切、震屏裁切、帧率档位规范化及 PlayerPrefs 键名稳定。
    /// </summary>
    public class GameSettingsTests
    {
        [Test]
        public void VolumeClamping_ClampsValidAndHandlesDegenerates()
        {
            Assert.AreEqual(0.5f, GameSettingsMath.ClampVolume(0.5f), 1e-5f);
            Assert.AreEqual(0f, GameSettingsMath.ClampVolume(-0.25f), 1e-5f);
            Assert.AreEqual(1f, GameSettingsMath.ClampVolume(1.85f), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultMasterVolume, GameSettingsMath.ClampVolume(float.NaN), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultMasterVolume, GameSettingsMath.ClampVolume(float.PositiveInfinity), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultMasterVolume, GameSettingsMath.ClampVolume(float.NegativeInfinity), 1e-5f);
        }

        [Test]
        public void ShakeClamping_ClampsValidAndHandlesDegenerates()
        {
            Assert.AreEqual(0.35f, GameSettingsMath.ClampShake(0.35f), 1e-5f);
            Assert.AreEqual(0f, GameSettingsMath.ClampShake(-1.0f), 1e-5f);
            Assert.AreEqual(1f, GameSettingsMath.ClampShake(3.5f), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultShakeScale, GameSettingsMath.ClampShake(float.NaN), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultShakeScale, GameSettingsMath.ClampShake(float.PositiveInfinity), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultShakeScale, GameSettingsMath.ClampShake(float.NegativeInfinity), 1e-5f);
        }

        [Test]
        public void LookSensitivityClamping_ClampsValidAndHandlesDegenerates()
        {
            Assert.AreEqual(1.5f, GameSettingsMath.ClampLookSensitivity(1.5f), 1e-5f);
            Assert.AreEqual(GameSettingsMath.MinLookSensitivity, GameSettingsMath.ClampLookSensitivity(-1f), 1e-5f);
            Assert.AreEqual(GameSettingsMath.MaxLookSensitivity, GameSettingsMath.ClampLookSensitivity(9f), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsMath.ClampLookSensitivity(float.NaN), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsMath.ClampLookSensitivity(float.PositiveInfinity), 1e-5f);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsMath.ClampLookSensitivity(float.NegativeInfinity), 1e-5f);
        }

        [Test]
        public void FrameRateNormalization_MapsToAllowedTiers()
        {
            Assert.AreEqual(GameSettingsMath.FrameRate30, GameSettingsMath.NormalizeFrameRate(30));
            Assert.AreEqual(GameSettingsMath.FrameRate60, GameSettingsMath.NormalizeFrameRate(60));
            Assert.AreEqual(GameSettingsMath.FrameRate120, GameSettingsMath.NormalizeFrameRate(120));
            Assert.AreEqual(GameSettingsMath.FrameRateUnlimited, GameSettingsMath.NormalizeFrameRate(-1));
            Assert.AreEqual(GameSettingsMath.FrameRateUnlimited, GameSettingsMath.NormalizeFrameRate(0));
            Assert.AreEqual(GameSettingsMath.FrameRateUnlimited, GameSettingsMath.NormalizeFrameRate(-60));

            // 任意值最近合法档位映射
            Assert.AreEqual(GameSettingsMath.FrameRate30, GameSettingsMath.NormalizeFrameRate(24));
            Assert.AreEqual(GameSettingsMath.FrameRate30, GameSettingsMath.NormalizeFrameRate(40));
            Assert.AreEqual(GameSettingsMath.FrameRate60, GameSettingsMath.NormalizeFrameRate(50));
            Assert.AreEqual(GameSettingsMath.FrameRate60, GameSettingsMath.NormalizeFrameRate(75));
            Assert.AreEqual(GameSettingsMath.FrameRate120, GameSettingsMath.NormalizeFrameRate(100));
            Assert.AreEqual(GameSettingsMath.FrameRate120, GameSettingsMath.NormalizeFrameRate(144));
        }

        [Test]
        public void FrameRateLabels_AreValidAndDistinct()
        {
            string label30 = GameSettingsMath.GetFrameRateLabel(30);
            string label60 = GameSettingsMath.GetFrameRateLabel(60);
            string label120 = GameSettingsMath.GetFrameRateLabel(120);
            string labelInf = GameSettingsMath.GetFrameRateLabel(-1);

            Assert.IsNotEmpty(label30);
            Assert.IsNotEmpty(label60);
            Assert.IsNotEmpty(label120);
            Assert.IsNotEmpty(labelInf);

            Assert.AreNotEqual(label30, label60);
            Assert.AreNotEqual(label60, label120);
            Assert.AreNotEqual(label120, labelInf);
        }

        [Test]
        public void PlayerPrefsKeys_AreNonEmptyAndUnique()
        {
            string[] keys =
            {
                GameSettingsMath.KeyMasterVolume,
                GameSettingsMath.KeyShakeScale,
                GameSettingsMath.KeyTargetFrameRate,
                GameSettingsMath.KeyMouseLookSensitivity,
                GameSettingsMath.KeyTouchLookSensitivity,
                GameSettingsMath.KeyInvertLookY,
                GameSettingsMath.KeyFixedJoystick,
                GameSettingsMath.KeyFireButtonRotatesCamera,
                GameSettingsMath.KeyAimAssistLevel
            };

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.IsNotEmpty(keys[i], $"设置键 {i} 不得为空");
                for (int j = i + 1; j < keys.Length; j++)
                {
                    Assert.AreNotEqual(keys[i], keys[j], $"设置键 {i} 与 {j} 不得重复");
                }
            }

            Assert.AreEqual("P1_Settings_MouseLookSensitivity", GameSettingsMath.KeyMouseLookSensitivity);
            Assert.AreEqual("P1_Settings_TouchLookSensitivity", GameSettingsMath.KeyTouchLookSensitivity);
            Assert.AreEqual("P1_Settings_InvertLookY", GameSettingsMath.KeyInvertLookY);
            Assert.AreEqual("P1_Settings_FixedJoystick", GameSettingsMath.KeyFixedJoystick);
            Assert.AreEqual("P1_Settings_FireRotatesCamera", GameSettingsMath.KeyFireButtonRotatesCamera);
            Assert.AreEqual("P1_Settings_AimAssistLevel", GameSettingsMath.KeyAimAssistLevel);
        }

        [Test]
        public void AimAssistLevel_NormalizesAndMapsToRadiusAndLabel()
        {
            Assert.AreEqual(GameSettingsMath.MinAimAssistLevel, GameSettingsMath.NormalizeAimAssistLevel(-5));
            Assert.AreEqual(GameSettingsMath.MaxAimAssistLevel, GameSettingsMath.NormalizeAimAssistLevel(99));
            Assert.AreEqual(2, GameSettingsMath.NormalizeAimAssistLevel(2));

            Assert.AreEqual(0f, GameSettingsMath.GetAimAssistRadius(0), 1e-4f);
            Assert.AreEqual(GameSettingsMath.AimAssistRadiusWeak, GameSettingsMath.GetAimAssistRadius(1), 1e-4f);
            Assert.AreEqual(GameSettingsMath.AimAssistRadiusMedium, GameSettingsMath.GetAimAssistRadius(2), 1e-4f);
            Assert.AreEqual(GameSettingsMath.AimAssistRadiusStrong, GameSettingsMath.GetAimAssistRadius(3), 1e-4f);

            Assert.AreEqual("关", GameSettingsMath.GetAimAssistLevelLabel(0));
            Assert.AreEqual("弱", GameSettingsMath.GetAimAssistLevelLabel(1));
            Assert.AreEqual("中", GameSettingsMath.GetAimAssistLevelLabel(2));
            Assert.AreEqual("强", GameSettingsMath.GetAimAssistLevelLabel(3));
        }

        [Test]
        public void GameSettingsService_ResetToDefaults_RestoresExpectedValues()
        {
            GameSettingsService.EnsureInitialized();
            GameSettingsService.MasterVolume = 0.2f;
            GameSettingsService.ShakeScale = 0.4f;
            GameSettingsService.TargetFrameRate = 30;
            GameSettingsService.MouseLookSensitivity = 2.0f;
            GameSettingsService.TouchLookSensitivity = 0.5f;
            GameSettingsService.InvertLookY = true;
            GameSettingsService.FixedJoystick = true;
            GameSettingsService.FireButtonRotatesCamera = false;
            GameSettingsService.AimAssistLevel = 0;

            GameSettingsService.ResetToDefaults();

            Assert.AreEqual(GameSettingsMath.DefaultMasterVolume, GameSettingsService.MasterVolume, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultShakeScale, GameSettingsService.ShakeScale, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultTargetFrameRate, GameSettingsService.TargetFrameRate);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsService.MouseLookSensitivity, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsService.TouchLookSensitivity, 1e-4f);
            Assert.IsFalse(GameSettingsService.InvertLookY);
            Assert.AreEqual(GameSettingsMath.DefaultFixedJoystick, GameSettingsService.FixedJoystick);
            Assert.AreEqual(GameSettingsMath.DefaultFireButtonRotatesCamera, GameSettingsService.FireButtonRotatesCamera);
            Assert.AreEqual(GameSettingsMath.DefaultAimAssistLevel, GameSettingsService.AimAssistLevel);
        }
    }
}
