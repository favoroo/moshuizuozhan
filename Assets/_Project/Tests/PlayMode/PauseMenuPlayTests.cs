using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.UI.Menus;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 暂停菜单、设置与无障碍屏幕震动运行时验证（PlayMode）。
    /// 严格遵循多 Agent 规范：自建独立测试 GameObject 并在 TearDown 中彻底清理，不污染全局环境。
    /// </summary>
    [Category("UI")]
    public class PauseMenuPlayTests
    {
        [TearDown]
        public void Cleanup()
        {
            if (PauseMenuOverlay.Instance != null)
            {
                Object.Destroy(PauseMenuOverlay.Instance.gameObject);
            }

            Time.timeScale = 1f;
            AudioListener.pause = false;
            HitstopService.IsPaused = false;
            HitstopService.Cancel();
            GameSettingsService.ResetToDefaults();
        }

        private static void SetMatchPhase(MatchManager match, MatchManager.Phase phase)
        {
            var method = typeof(MatchManager).GetMethod("SetPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "MatchManager 必须具备 SetPhase 私有方法");
            method.Invoke(match, new object[] { phase });
        }

        [UnityTest]
        public IEnumerator PauseMenu_FreezesTimeAndAudio_AndResumes()
        {
            var matchGo = new GameObject("TestMatch", typeof(MatchManager));
            var match = matchGo.GetComponent<MatchManager>();
            SetMatchPhase(match, MatchManager.Phase.Playing);
            yield return null;

            var overlay = PauseMenuOverlay.CreateInstance();
            yield return null;

            Assert.IsFalse(overlay.IsPaused, "初始状态不应处于暂停");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f);
            Assert.IsFalse(AudioListener.pause);

            // 暂停
            overlay.Pause();
            Assert.IsTrue(overlay.IsPaused, "调用 Pause 后应处于暂停状态");
            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "暂停后 Time.timeScale 必须为 0");
            Assert.IsTrue(AudioListener.pause, "暂停后 AudioListener.pause 必须为 true");

            // 恢复
            overlay.Resume();
            Assert.IsFalse(overlay.IsPaused, "调用 Resume 后不应处于暂停状态");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "恢复后 Time.timeScale 必须为 1");
            Assert.IsFalse(AudioListener.pause, "恢复后 AudioListener.pause 必须为 false");

            Object.Destroy(overlay.gameObject);
            Object.Destroy(matchGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MatchPhaseChange_ExitingPlaying_AutoResumes()
        {
            var matchGo = new GameObject("TestMatch", typeof(MatchManager));
            var match = matchGo.GetComponent<MatchManager>();
            SetMatchPhase(match, MatchManager.Phase.Playing);
            yield return null;

            var overlay = PauseMenuOverlay.CreateInstance();
            yield return null;

            overlay.Pause();
            Assert.IsTrue(overlay.IsPaused);
            Assert.AreEqual(0f, Time.timeScale, 1e-4f);

            // 离开 Playing 阶段（如时间到进入 Finished 定格）
            SetMatchPhase(match, MatchManager.Phase.Finished);
            yield return null;

            Assert.IsFalse(overlay.IsPaused, "离开 Playing 阶段必须自动退出暂停");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "离开 Playing 阶段必须恢复正常时间尺度");
            Assert.IsFalse(AudioListener.pause, "离开 Playing 阶段必须恢复音频播放");

            Object.Destroy(overlay.gameObject);
            Object.Destroy(matchGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseMenu_CancelsHitstop_AndPreventsOverride()
        {
            var matchGo = new GameObject("TestMatch", typeof(MatchManager));
            var match = matchGo.GetComponent<MatchManager>();
            SetMatchPhase(match, MatchManager.Phase.Playing);
            yield return null;

            var overlay = PauseMenuOverlay.CreateInstance();
            yield return null;

            // 触发一次击杀定格
            HitstopService.Pulse(0.1f, 0.2f, 0.1f);
            Assert.IsTrue(HitstopService.IsActive, "Hitstop 应已激活");

            // 暂停游戏
            overlay.Pause();
            Assert.IsFalse(HitstopService.IsActive, "暂停必须取消活跃的 Hitstop 时间线");
            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "暂停后时间尺度应为 0");
            Assert.IsTrue(HitstopService.IsPaused);

            // 暂停期间若有其他逻辑触发 Pulse，不得把 0 覆盖掉
            HitstopService.Pulse(0.1f, 0.2f, 0.1f);
            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "暂停期间 Hitstop 守卫必须阻止覆盖 Time.timeScale");

            // 恢复游戏
            overlay.Resume();
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "恢复后时间尺度必须回归 1.0");
            Assert.IsFalse(HitstopService.IsPaused);

            Object.Destroy(overlay.gameObject);
            Object.Destroy(matchGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScreenShake_ZeroScale_ProducesNoDisplacement()
        {
            GameSettingsService.EnsureInitialized();
            GameSettingsService.ShakeScale = 0f;

            var camGo = new GameObject("TestCam", typeof(Camera), typeof(TopDownCameraFollow));
            var targetGo = new GameObject("TestTarget");
            targetGo.transform.position = Vector3.zero;

            var camFollow = camGo.GetComponent<TopDownCameraFollow>();
            camFollow.Configure(targetGo.transform, null);
            yield return null;

            Vector3 initialPos = camGo.transform.position;
            Quaternion initialRot = camGo.transform.rotation;

            // 施加最大强度创伤
            camFollow.AddTrauma(1.0f);
            yield return null;

            // 当 ShakeScale 为 0 时，震屏完全关闭，不得产生任何震颤偏移与旋转偏差
            Vector3 afterPos = camGo.transform.position;
            Quaternion afterRot = camGo.transform.rotation;

            Assert.AreEqual(initialPos.x, afterPos.x, 0.001f, "ShakeScale 为 0 时不应产生 X 轴位移");
            Assert.AreEqual(initialPos.z, afterPos.z, 0.001f, "ShakeScale 为 0 时不应产生 Z 轴位移");
            Assert.AreEqual(initialRot.z, afterRot.z, 0.001f, "ShakeScale 为 0 时不应产生 Roll 轴镜头旋转");

            Object.Destroy(camGo);
            Object.Destroy(targetGo);
            GameSettingsService.ResetToDefaults();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseMenuOverlay_KeyBindings_OpenCloseAndEscHierarchy()
        {
            var matchGo = new GameObject("TestMatch", typeof(MatchManager));
            var match = matchGo.GetComponent<MatchManager>();
            SetMatchPhase(match, MatchManager.Phase.Playing);
            yield return null;

            var overlay = PauseMenuOverlay.CreateInstance();
            yield return null;

            overlay.Pause();
            Assert.IsTrue(overlay.IsPaused);

            overlay.OpenSettings();
            Assert.IsTrue(overlay.IsMenuExpanded);

            overlay.OpenKeyBindings();
            Assert.IsTrue(overlay.IsMenuExpanded);

            // 第一次 Esc: 退出按键设置，回退到系统设置
            overlay.TogglePause();
            Assert.IsTrue(overlay.IsPaused);
            Assert.IsTrue(overlay.IsMenuExpanded);

            // 第二次 Esc: 退出系统设置，回退到暂停主面板
            overlay.TogglePause();
            Assert.IsTrue(overlay.IsPaused);

            // 第三次 Esc: 继续游戏
            overlay.TogglePause();
            Assert.IsFalse(overlay.IsPaused);

            Object.Destroy(overlay.gameObject);
            Object.Destroy(matchGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseMenu_MainView_ExposesControlsLayoutEntry()
        {
            var overlay = PauseMenuOverlay.CreateInstance();
            yield return null;

            Transform controlsBtn = overlay.transform.Find("ModalRoot/PausePanel/MainView/ControlsBtn");
            Assert.IsNotNull(controlsBtn, "暂停面板主视图必须包含 ControlsBtn（操作按钮调整/按键设置入口）");

            // 文案随平台切换：移动端唤起触控布局拖拽编辑器，桌面端唤起按键设置
            TMP_Text label = controlsBtn.Find("Label") != null ? controlsBtn.Find("Label").GetComponent<TMP_Text>() : null;
            Assert.IsNotNull(label, "ControlsBtn 必须带 Label 文本");
            Assert.AreEqual(UITypography.IsMobileLike ? "操作按钮调整" : "按键设置", label.text,
                "ControlsBtn 文案必须按平台切换");

            // 原有四颗按钮仍在，避免「加了新入口压掉旧入口」
            Assert.IsNotNull(overlay.transform.Find("ModalRoot/PausePanel/MainView/ResumeBtn"), "ResumeBtn 丢失");
            Assert.IsNotNull(overlay.transform.Find("ModalRoot/PausePanel/MainView/SettingsBtn"), "SettingsBtn 丢失");
            Assert.IsNotNull(overlay.transform.Find("ModalRoot/PausePanel/MainView/RestartBtn"), "RestartBtn 丢失");
            Assert.IsNotNull(overlay.transform.Find("ModalRoot/PausePanel/MainView/ReturnMenuBtn"), "ReturnMenuBtn 丢失");

            Object.Destroy(overlay.gameObject);
            yield return null;
        }
    }
}
