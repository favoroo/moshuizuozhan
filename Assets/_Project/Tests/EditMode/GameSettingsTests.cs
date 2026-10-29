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
            Assert.IsNotEmpty(GameSettingsMath.KeyMasterVolume);
            Assert.IsNotEmpty(GameSettingsMath.KeyShakeScale);
            Assert.IsNotEmpty(GameSettingsMath.KeyTargetFrameRate);
            Assert.IsNotEmpty(GameSettingsMath.KeyMouseLookSensitivity);
            Assert.IsNotEmpty(GameSettingsMath.KeyTouchLookSensitivity);
            Assert.IsNotEmpty(GameSettingsMath.KeyInvertLookY);

            Assert.AreEqual("P1_Settings_MouseLookSensitivity", GameSettingsMath.KeyMouseLookSensitivity);
            Assert.AreEqual("P1_Settings_TouchLookSensitivity", GameSettingsMath.KeyTouchLookSensitivity);
            Assert.AreEqual("P1_Settings_InvertLookY", GameSettingsMath.KeyInvertLookY);

            Assert.AreNotEqual(GameSettingsMath.KeyMasterVolume, GameSettingsMath.KeyShakeScale);
            Assert.AreNotEqual(GameSettingsMath.KeyMasterVolume, GameSettingsMath.KeyTargetFrameRate);
            Assert.AreNotEqual(GameSettingsMath.KeyShakeScale, GameSettingsMath.KeyTargetFrameRate);
            Assert.AreNotEqual(GameSettingsMath.KeyTargetFrameRate, GameSettingsMath.KeyMouseLookSensitivity);
            Assert.AreNotEqual(GameSettingsMath.KeyMouseLookSensitivity, GameSettingsMath.KeyTouchLookSensitivity);
            Assert.AreNotEqual(GameSettingsMath.KeyTouchLookSensitivity, GameSettingsMath.KeyInvertLookY);
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

            GameSettingsService.ResetToDefaults();

            Assert.AreEqual(GameSettingsMath.DefaultMasterVolume, GameSettingsService.MasterVolume, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultShakeScale, GameSettingsService.ShakeScale, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultTargetFrameRate, GameSettingsService.TargetFrameRate);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsService.MouseLookSensitivity, 1e-4f);
            Assert.AreEqual(GameSettingsMath.DefaultLookSensitivity, GameSettingsService.TouchLookSensitivity, 1e-4f);
            Assert.IsFalse(GameSettingsService.InvertLookY);
        }
    }
}
