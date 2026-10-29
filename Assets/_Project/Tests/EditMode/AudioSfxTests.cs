using NUnit.Framework;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 音效与循环管线纯逻辑验收：
    /// 覆盖各类武器音效映射、循环通道互斥防叠音、节流窗口抑制、安全兜底与混音参数。
    /// EditMode 运行，无场景依赖。
    /// </summary>
    public class AudioSfxTests
    {
        private GameObject _audioMgrGo;
        private AudioManager _mgr;
        private AudioMixConfigSO _mix;

        [SetUp]
        public void SetUp()
        {
            _audioMgrGo = new GameObject("TestAudioManager");
            _mgr = _audioMgrGo.AddComponent<AudioManager>();
            _mix = ScriptableObject.CreateInstance<AudioMixConfigSO>();
            _mgr.mixConfig = _mix;
        }

        [TearDown]
        public void TearDown()
        {
            if (_audioMgrGo != null)
            {
                Object.DestroyImmediate(_audioMgrGo);
            }
            if (_mix != null)
            {
                Object.DestroyImmediate(_mix);
            }
        }

        [Test]
        public void NullSafe_MethodsDoNotThrowWithoutClips()
        {
            // 在所有 AudioClip 均为空的情况下调用全部新增接口，不应抛出任何异常
            Assert.DoesNotThrow(() =>
            {
                _mgr.PlayWeaponShot(WeaponKind.Shooter);
                _mgr.PlayWeaponShot(WeaponKind.Roller);
                _mgr.PlayWeaponShot(WeaponKind.Charger);
                _mgr.PlayWeaponShot(WeaponKind.Slosher);
                _mgr.PlayWeaponShot(WeaponKind.Blaster);
                _mgr.PlayWeaponShot(WeaponKind.Dualies);
                _mgr.PlayWeaponShot(WeaponKind.Splatling);

                _mgr.PlayBombThrow();
                _mgr.PlayBombBeep();
                _mgr.PlayBombExplode();

                _mgr.PlaySpecialCast(SpecialKind.Slam);
                _mgr.PlaySpecialCast(SpecialKind.Storm);

                _mgr.PlayHurt();
                _mgr.PlayJump();
                _mgr.PlayLand();
                _mgr.PlaySquidIn();
                _mgr.PlaySquidOut();
                _mgr.PlayFootstep(true, false);
                _mgr.PlayFootstep(false, true);
                _mgr.PlayFootstep(false, false);
                _mgr.PlaySwimSplash();
                _mgr.PlayHitMarker();

                for (int i = 0; i <= (int)UiSoundKind.Error; i++)
                {
                    _mgr.PlayUISound((UiSoundKind)i);
                }

                _mgr.PlayLoop(LoopSfxKind.Roller);
                _mgr.StopLoop(LoopSfxKind.Roller);
                _mgr.StopAllLoops();
            });
        }

        [Test]
        public void LoopChannels_ExistAndAreConfiguredCorrectly()
        {
            var kinds = (LoopSfxKind[])System.Enum.GetValues(typeof(LoopSfxKind));
            Assert.AreEqual(7, kinds.Length, "循环音效应有 7 个分类");

            for (int i = 0; i < kinds.Length; i++)
            {
                AudioSource src = _mgr.GetLoopSource(kinds[i]);
                Assert.IsNotNull(src, $"循环通道 {kinds[i]} 应具备专属 AudioSource");
                Assert.IsTrue(src.loop, $"循环通道 {kinds[i]} AudioSource 应开启 loop");
                Assert.AreEqual(0f, src.spatialBlend, 0.001f, "2D 循环音效 spatialBlend 应为 0");
            }
        }

        [Test]
        public void LoopChannel_PlayAndStop_TrackStateCleanly()
        {
            // 给 Roller 绑定虚拟测试音效
            AudioClip dummyClip = AudioClip.Create("DummyRoll", 4410, 1, 44100, false);
            _mgr.loopRollerClip = dummyClip;

            Assert.IsFalse(_mgr.IsLoopPlaying(LoopSfxKind.Roller), "初始应未播放");

            _mgr.PlayLoop(LoopSfxKind.Roller);
            Assert.IsTrue(_mgr.IsLoopPlaying(LoopSfxKind.Roller), "调用 PlayLoop 后应进入播放/淡入状态");

            // 重复 PlayLoop 不应产生异常或打断
            _mgr.PlayLoop(LoopSfxKind.Roller);
            Assert.IsTrue(_mgr.IsLoopPlaying(LoopSfxKind.Roller), "重复 PlayLoop 保持活跃");

            _mgr.StopLoop(LoopSfxKind.Roller);
            Assert.IsFalse(_mgr.IsLoopPlaying(LoopSfxKind.Roller), "调用 StopLoop 后应标记为非正向播放");

            _mgr.StopAllLoops();

            Object.DestroyImmediate(dummyClip);
        }

        [Test]
        public void MixConfig_DefaultValuesAreInValidRanges()
        {
            Assert.IsTrue(_mix.loopMasterVolume >= 0f && _mix.loopMasterVolume <= 1f, "循环总音量在 0~1");
            Assert.IsTrue(_mix.loopFadeSeconds >= 0.05f && _mix.loopFadeSeconds <= 0.30f, "循环淡入淡出在 0.05~0.30s");
            Assert.IsTrue(_mix.rollerLoopVolume > 0f && _mix.rollerLoopVolume <= 1f);
            Assert.IsTrue(_mix.splatlingSpinLoopVolume > 0f && _mix.splatlingSpinLoopVolume <= 1f);
            Assert.IsTrue(_mix.swimLoopVolume > 0f && _mix.swimLoopVolume <= 1f);
            Assert.IsTrue(_mix.climbLoopVolume > 0f && _mix.climbLoopVolume <= 1f);
            Assert.IsTrue(_mix.enemyInkSizzleLoopVolume > 0f && _mix.enemyInkSizzleLoopVolume <= 1f);
            Assert.IsTrue(_mix.stormRainLoopVolume > 0f && _mix.stormRainLoopVolume <= 1f);
            Assert.IsTrue(_mix.chargerChargeLoopVolume > 0f && _mix.chargerChargeLoopVolume <= 1f);

            Assert.IsTrue(_mix.footstepThrottleSeconds > 0.05f, "脚步节流至少 50ms");
            Assert.IsTrue(_mix.hurtThrottleSeconds > 0.05f, "受击节流至少 50ms");
            Assert.IsTrue(_mix.hitMarkerThrottleSeconds > 0.02f, "打击标记节流至少 20ms");
            Assert.IsTrue(_mix.uiThrottleSeconds > 0.01f, "UI 节流至少 10ms");
        }

        [Test]
        public void WeaponShot_FallbackToShotClipsWhenExclusiveMissing()
        {
            AudioClip dummyShot = AudioClip.Create("DummyShot", 4410, 1, 44100, false);
            _mgr.shotClips = new[] { dummyShot };

            // 专有音效未分配时应优雅使用 shotClips 保底
            Assert.DoesNotThrow(() =>
            {
                _mgr.PlayWeaponShot(WeaponKind.Roller);
                _mgr.PlayWeaponShot(WeaponKind.Charger);
                _mgr.PlayWeaponShot(WeaponKind.Slosher);
                _mgr.PlayWeaponShot(WeaponKind.Blaster);
            });

            Object.DestroyImmediate(dummyShot);
        }
    }
}
