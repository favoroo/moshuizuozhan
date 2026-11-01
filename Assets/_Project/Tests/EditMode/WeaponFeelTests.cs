using NUnit.Framework;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P16-A] 射击手感与涂地撞击反馈的纯逻辑回归：
    /// 1. 武器类别预设的强弱顺序（高射速武器后坐力必须低于单发重武器）；
    /// 2. 配置字段覆盖预设、旧资产（字段为 0）回落预设；
    /// 3. 弹丸速度拉伸 / 出生弹跳 / 撞击涟漪振幅的单调性与上下限；
    /// 4. 涟漪合并窗口与半径（防止连发把 8 槽环形缓冲冲干净）。
    /// 全部为纯函数 / 纯结构断言，不依赖场景、物理与对象池。
    /// </summary>
    public class WeaponFeelTests
    {
        private WeaponConfigSO _config;

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
                _config = null;
            }
        }

        [Test]
        public void FeelProfile_HighFireRateWeapons_RecoilBelowHeavyWeapons()
        {
            WeaponFeelParams splatling = WeaponFeelProfile.ForKind(WeaponKind.Splatling);
            WeaponFeelParams shooter = WeaponFeelProfile.ForKind(WeaponKind.Shooter);
            WeaponFeelParams dualies = WeaponFeelProfile.ForKind(WeaponKind.Dualies);
            WeaponFeelParams blaster = WeaponFeelProfile.ForKind(WeaponKind.Blaster);
            WeaponFeelParams charger = WeaponFeelProfile.ForKind(WeaponKind.Charger);

            Assert.Less(splatling.RecoilKickMeters, shooter.RecoilKickMeters,
                "15 发/秒的加特林单发后坐力必须低于 10 发/秒的小枪，否则连发会变成持续抖动");
            Assert.Less(dualies.RecoilKickMeters, shooter.RecoilKickMeters,
                "12 发/秒的双枪单发后坐力也应低于小枪");
            Assert.Less(shooter.RecoilKickMeters, blaster.RecoilKickMeters,
                "单发爆破枪必须比小枪更有「炮」的重量感");
            Assert.Less(blaster.RecoilKickMeters, charger.RecoilKickMeters,
                "蓄力狙是全场最重的一发");

            Assert.Less(splatling.CameraKickFovDegrees, blaster.CameraKickFovDegrees,
                "连发武器的 FOV 冲击必须显著低于重武器");
            Assert.Greater(charger.MuzzleFlashScale, splatling.MuzzleFlashScale,
                "蓄力狙的枪口火光应比加特林更大");
        }

        [Test]
        public void FeelProfile_ConfigOverride_BeatsPreset()
        {
            _config = ScriptableObject.CreateInstance<WeaponConfigSO>();
            _config.weaponKind = WeaponKind.Dualies;
            _config.recoilPitchDegrees = 9f;
            _config.cameraKickMeters = 0f; // 未填写 → 回落预设
            _config.feelSpringOmega = 7.5f;
            _config.cameraKickDecayRate = 12f;

            WeaponFeelParams feel = WeaponFeelProfile.Resolve(_config);
            WeaponFeelParams preset = WeaponFeelProfile.ForKind(WeaponKind.Dualies);

            Assert.AreEqual(9f, feel.RecoilPitchDegrees, 0.0001f, "配置里 > 0 的字段必须覆盖类别预设");
            Assert.AreEqual(preset.CameraKickMeters, feel.CameraKickMeters, 0.0001f,
                "配置里为 0 的字段必须回落到类别预设（旧资产不用迁移）");
            Assert.AreEqual(7.5f, feel.SpringOmega, 0.0001f, "弹簧角频率也必须支持配置覆盖");
            Assert.AreEqual(12f, feel.CameraKickDecayRate, 0.0001f, "镜头热积累衰减率也必须支持配置覆盖");
        }

        [Test]
        public void FeelProfile_NullConfig_FallsBackToShooterPreset()
        {
            WeaponFeelParams feel = WeaponFeelProfile.Resolve(null);
            WeaponFeelParams shooter = WeaponFeelProfile.ForKind(WeaponKind.Shooter);

            Assert.AreEqual(shooter.RecoilKickMeters, feel.RecoilKickMeters, 0.0001f);
            Assert.AreEqual(shooter.MuzzleFlashScale, feel.MuzzleFlashScale, 0.0001f);
            Assert.AreEqual(shooter.ImpactRippleScale, feel.ImpactRippleScale, 0.0001f);
        }

        [Test]
        public void ProjectileStretch_GrowsWithSpeed_AndStaysClamped()
        {
            float lobbed = WeaponFeelMath.ProjectileStretch(15f);   // 泼桶
            float shooter = WeaponFeelMath.ProjectileStretch(21f);  // 小枪
            float splatling = WeaponFeelMath.ProjectileStretch(40f); // 加特林

            Assert.Greater(shooter, lobbed, "弹速越高尾巴应越长");
            Assert.Greater(splatling, shooter, "加特林应比小枪更「快」");
            Assert.GreaterOrEqual(lobbed, WeaponFeelMath.MinStretch, "低速抛物弹不能缩成负拉伸");
            Assert.LessOrEqual(splatling, WeaponFeelMath.MaxStretch, "高速弹不能被拉成一根针");

            Assert.AreEqual(WeaponFeelMath.MaxStretch, WeaponFeelMath.ProjectileStretch(200f), 0.0001f,
                "极端弹速必须被上限钳住");
        }

        [Test]
        public void SpawnPop_StartsAtSixtyFivePercent_AndReachesFullSize()
        {
            const float speed = 20f;

            Assert.AreEqual(0.65f, WeaponFeelMath.SpawnPopScale(0f, speed), 0.002f,
                "出生瞬间应从 65% 尺寸开始，读作「墨滴被挤出来」");
            Assert.AreEqual(1f, WeaponFeelMath.SpawnPopScale(speed * 0.05f, speed), 0.001f,
                "飞行超过出生弹跳窗口后必须回到 100% 尺寸");
            Assert.Less(WeaponFeelMath.SpawnPopScale(speed * 0.015f, speed), 1f,
                "窗口内应处于放大过程中");
            Assert.AreEqual(1f, WeaponFeelMath.SpawnPopScale(0f, 0f), 0.001f,
                "速度为 0 时不做弹跳，直接返回 1");
        }

        [Test]
        public void ImpactRipple_GrowsWithSplatRadius_AndFeelsScale()
        {
            float small = WeaponFeelMath.ImpactRippleAmplitude(0.4f, 1f);
            float big = WeaponFeelMath.ImpactRippleAmplitude(1.2f, 1f);
            float heavy = WeaponFeelMath.ImpactRippleAmplitude(1.2f, 1.4f);

            Assert.Greater(big, small, "大墨斑的涟漪必须比小滴墨更强");
            Assert.Greater(heavy, big, "武器手感倍率必须参与涟漪强度");
            Assert.Greater(small, 0f);
            Assert.Less(WeaponFeelMath.ImpactRippleAmplitude(10f, 1.4f), 0.35f,
                "半径必须钳制，避免极端参数把地面波纹拉爆");
        }

        [Test]
        public void InkRipple_MergesOnlyInsideWindowAndRadius()
        {
            var slot = new Vector4(2f, 0f, 3f, 10f);

            Assert.IsTrue(InkSurfaceController.ShouldMergeRipple(slot, new Vector3(2.3f, 0f, 3.2f), 10.05f),
                "0.09s 内、0.55m 内的重复落墨应合并进同一槽位");
            Assert.IsFalse(InkSurfaceController.ShouldMergeRipple(slot, new Vector3(2.3f, 0f, 3.2f), 10.2f),
                "超过合并窗口后应占用新槽位（波纹需要继续扩散）");
            Assert.IsFalse(InkSurfaceController.ShouldMergeRipple(slot, new Vector3(4f, 0f, 3f), 10.05f),
                "距离过远的落墨不应合并");
            Assert.IsFalse(InkSurfaceController.ShouldMergeRipple(new Vector4(0f, 0f, 0f, -999f), Vector3.zero, 10f),
                "未使用的槽位（birth = -999）永远不参与合并");
        }

        [Test]
        public void FeelProfile_SpringPresets_ArePositive_AndRapidFireIsStiffer()
        {
            WeaponFeelParams splatling = WeaponFeelProfile.ForKind(WeaponKind.Splatling);
            WeaponFeelParams dualies = WeaponFeelProfile.ForKind(WeaponKind.Dualies);
            WeaponFeelParams shooter = WeaponFeelProfile.ForKind(WeaponKind.Shooter);
            WeaponFeelParams charger = WeaponFeelProfile.ForKind(WeaponKind.Charger);
            WeaponFeelParams blaster = WeaponFeelProfile.ForKind(WeaponKind.Blaster);

            Assert.Greater(splatling.SpringOmega, charger.SpringOmega,
                "15 发/秒的加特林弹簧必须比单发蓄力狙更硬，才能读作连发震颤");
            Assert.Greater(dualies.SpringOmega, shooter.SpringOmega,
                "12 发/秒的双枪弹簧应比小枪更快，逐发回稳不糊底");
            Assert.Greater(blaster.SpringOmega, 0f);
            Assert.Greater(splatling.CameraKickDecayRate, charger.CameraKickDecayRate,
                "连发武器的镜头热积累应衰减更慢，形成持续后拉");
            Assert.Greater(charger.CameraKickDecayRate, 0f);
        }

        [Test]
        public void SpringImpulse_PeakMatchesRequestedOffset_AndSettles()
        {
            const float omega = 30f;
            const float peak = 0.045f;
            var spring = new AnimSpring1D(0f);

            spring.Kick(AnimSpring1D.ImpulseForPeak(peak, omega, WeaponFeelMath.RecoilDampingRatio));

            float maxValue = 0f;
            for (int i = 0; i < 180; i++)
            {
                spring.Step(0f, omega, WeaponFeelMath.RecoilDampingRatio, 1f / 120f);
                maxValue = Mathf.Max(maxValue, Mathf.Abs(spring.Value));
            }

            Assert.AreEqual(peak, maxValue, peak * 0.12f,
                "冲量峰值应接近目标后坐距离，不能过冲成 2 倍也不能小到无感");
            Assert.Less(Mathf.Abs(spring.Value), 0.001f, "1.5s 内必须回稳到 0");
        }

        /// <summary>
        /// P0 回归：后坐弹簧峰值必须与帧率无关。
        /// 旧半隐式欧拉在 ω·dt 接近 1 时发散/漂移，30FPS 会指数发散、60FPS 峰值只剩 0.011、
        /// 240FPS 又冲到 0.037，同样一次开火在 240Hz 显示器上后坐强 3 倍。
        /// </summary>
        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(120f)]
        [TestCase(240f)]
        public void SpringImpulse_PeakIsFrameRateIndependent(float framesPerSecond)
        {
            const float omega = 30f;
            const float peak = 0.045f;
            float step = 1f / framesPerSecond;

            var spring = new AnimSpring1D(0f);
            spring.Kick(AnimSpring1D.ImpulseForPeak(peak, omega, WeaponFeelMath.RecoilDampingRatio));

            float maxValue = 0f;
            int steps = Mathf.CeilToInt(1.5f / step);
            for (int i = 0; i < steps; i++)
            {
                spring.Step(0f, omega, WeaponFeelMath.RecoilDampingRatio, step);
                maxValue = Mathf.Max(maxValue, Mathf.Abs(spring.Value));
                Assert.IsFalse(float.IsNaN(spring.Value) || float.IsInfinity(spring.Value),
                    $"{framesPerSecond}FPS 下弹簧不得发散（旧半隐式欧拉在 30FPS 会指数爆炸）");
            }

            Assert.AreEqual(peak, maxValue, peak * 0.12f,
                $"{framesPerSecond}FPS 的冲量峰值必须仍是设计值 {peak}（±12%）");
            Assert.Less(Mathf.Abs(spring.Value), 0.001f, $"{framesPerSecond}FPS 下 1.5s 内必须回稳");
        }

        [Test]
        public void ShotStrengthScale_MapsChargeToFeedback()
        {
            Assert.AreEqual(0.35f, WeaponFeelMath.ShotStrengthScale(0f), 0.0001f,
                "点按蓄力也要保留最低反馈，不能完全无手感");
            Assert.AreEqual(1f, WeaponFeelMath.ShotStrengthScale(1f), 0.0001f);
            Assert.AreEqual(0.35f + 0.65f * 0.12f, WeaponFeelMath.ShotStrengthScale(0.12f), 0.0001f,
                "蓄力狙最低 12% 蓄力约 0.428 倍");
            Assert.AreEqual(1f, WeaponFeelMath.ShotStrengthScale(5f), 0.0001f,
                "超出 0~1 的强度必须钳制");
        }
    }
}
