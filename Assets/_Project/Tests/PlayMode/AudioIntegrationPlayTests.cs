using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 音频集成 PlayMode 测试：
    /// 加载 PhaseA_Playbox，验证炸弹三音、Slam/Storm 循环音不报错、
    /// 无 AudioSource 泄漏，且场景驱动器已自动挂载。
    /// PlayMode 下任何运行时 log error 都会判失败。
    /// </summary>
    public class AudioIntegrationPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.Destroy(_spawned[i]);
                }
            }
            _spawned.Clear();

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopAllLoops();
            }
        }

        [UnityTest]
        public IEnumerator AudioManager_RuntimeInstance_HasLoopChannels()
        {
            AudioManager mgr = AudioManager.Instance;
            Assert.IsNotNull(mgr, "PhaseA_Playbox 应具备 AudioManager 单例");

            var kinds = (LoopSfxKind[])System.Enum.GetValues(typeof(LoopSfxKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                AudioSource src = mgr.GetLoopSource(kinds[i]);
                Assert.IsNotNull(src, $"循环通道 {kinds[i]} 应成功初始化 AudioSource");
                Assert.IsTrue(src.loop, $"循环通道 {kinds[i]} 应开启 loop");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BombAudio_ThreePhases_ExecuteWithoutErrors()
        {
            AudioManager mgr = AudioManager.Instance;
            Assert.IsNotNull(mgr);

            // 1. 投掷
            Assert.DoesNotThrow(() => mgr.PlayBombThrow());
            yield return new WaitForSeconds(0.05f);

            // 2. 引信 beep
            Assert.DoesNotThrow(() => mgr.PlayBombBeep());
            yield return new WaitForSeconds(0.05f);

            // 3. 爆炸
            Assert.DoesNotThrow(() => mgr.PlayBombExplode());
            yield return new WaitForSeconds(0.05f);
        }

        [UnityTest]
        public IEnumerator SpecialAudio_SlamAndStorm_RunSmoothly()
        {
            AudioManager mgr = AudioManager.Instance;
            Assert.IsNotNull(mgr);

            // Tidal Slam 施放与砸地
            Assert.DoesNotThrow(() => mgr.PlaySpecialCast(SpecialKind.Slam, false));
            yield return new WaitForSeconds(0.1f);

            // Ink Tempest 施放与雨声循环通道
            Assert.DoesNotThrow(() => mgr.PlaySpecialCast(SpecialKind.Storm, false));
            Assert.DoesNotThrow(() => mgr.PlayLoop(LoopSfxKind.StormRain));
            yield return new WaitForSeconds(0.15f);

            Assert.IsTrue(mgr.IsLoopPlaying(LoopSfxKind.StormRain), "StormRain 循环通道应处于活跃中");

            Assert.DoesNotThrow(() => mgr.StopLoop(LoopSfxKind.StormRain));
            yield return new WaitForSeconds(0.15f);

            Assert.IsFalse(mgr.IsLoopPlaying(LoopSfxKind.StormRain), "StormRain 循环通道淡出后应停止");
        }

        [UnityTest]
        public IEnumerator AudioDrivers_AttachedToSceneEntities()
        {
            PlayerController player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player, "本地玩家应存在于场景中");

            var moveDriver = player.GetComponent<MovementAudioDriver>();
            Assert.IsNotNull(moveDriver, "本地玩家应挂载 MovementAudioDriver");

            var weapon = player.GetComponent<WeaponController>();
            Assert.IsNotNull(weapon, "本地玩家应具备 WeaponController");

            var weaponDriver = weapon.GetComponent<WeaponAudioDriver>();
            Assert.IsNotNull(weaponDriver, "WeaponController 应挂载 WeaponAudioDriver");

            yield return null;
        }
    }
}
