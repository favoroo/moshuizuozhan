using System;
using System.IO;
using P1.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.Editor
{
    /// <summary>
    /// [P6-C-R] 真实 Profiler 采样会话（替代旧 BuildAndProfilerSuite 的「离线仿真采样」）：
    /// 由菜单或 CLI（script.eval 调 StartSession）触发 → 进入 PlayMode →
    /// PerformanceProfilerProbe 对实战逐帧实测（帧率 / Recorder GC.Alloc / DrawCalls / 纹理上传）→
    /// 结束自动写 Docs/ProfilerReport_P6C.md 并退出 PlayMode。
    /// 会话状态全部走 EditorPrefs，可安全穿越进入 PlayMode 时的 Domain Reload；
    /// 报告中不存在任何写死或 Random 生成的数值。
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeProfilerSession
    {
        public const float DefaultDurationSeconds = 120f;
        public const float MinDurationSeconds = 5f;
        public const float MaxDurationSeconds = 600f;

        private const string ActiveKey = "P1.ProfilerSession.Active";
        private const string DurationKey = "P1.ProfilerSession.Duration";
        private const string DeadlineKey = "P1.ProfilerSession.Deadline";
        private const string CompletedKey = "P1.ProfilerSession.CompletedUtcTicks";

        private static GameObject _probeHolder;
        private static PerformanceProfilerProbe _probe;
        private static int _warmTicksLeft;
        private static float _pendingDuration;
        private static double _lastPlayRequestTime;
        private static bool _awaitingPlayingPhase;

        /// <summary>PlayMode 请求可能被编译/域重载尾部静默吞掉，每 5 秒重发一次直到进入。</summary>
        private const double PlayRequestRetryIntervalSeconds = 5.0;

        /// <summary>进入 PlayMode 后先空跑若干帧（场景装载、材质预热、首包 GC 都发生在入场帧），再开始正式采样。</summary>
        private const int WarmupTicksAfterPlayEntry = 30;

        static PlayModeProfilerSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>是否有采样会话处于排程/进行中。</summary>
        public static bool IsActive => EditorPrefs.GetBool(ActiveKey, false);

        /// <summary>最近一次成功完成的会话（UTC），供 CLI 轮询判定；从未完成返回 DateTime.MinValue。</summary>
        public static DateTime LastCompletedUtc
        {
            get
            {
                string raw = EditorPrefs.GetString(CompletedKey, string.Empty);
                return long.TryParse(raw, out long ticks) && ticks > 0
                    ? new DateTime(ticks, DateTimeKind.Utc)
                    : DateTime.MinValue;
            }
        }

        [MenuItem("P1/性能/1. 执行 120 秒战斗真实 Profiler 采样 (P6-C)", false, 90)]
        private static void MenuRunDefault()
        {
            StartSession(DefaultDurationSeconds);
        }

        [MenuItem("P1/性能/1. 执行 120 秒战斗真实 Profiler 采样 (P6-C)", true)]
        private static bool ValidateMenuRunDefault()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !IsActive;
        }

        /// <summary>
        /// 排程一次真实采样会话并进入 PlayMode。CLI 可用
        /// `unity command script.eval --code 'P1.Editor.PlayModeProfilerSession.StartSession(30f);'` 驱动。
        /// </summary>
        public static void StartSession(float durationSeconds)
        {
            durationSeconds = Mathf.Clamp(durationSeconds, MinDurationSeconds, MaxDurationSeconds);

            if (IsActive)
            {
                Debug.LogWarning("[P6-C][Profiler] 已有采样会话在进行中，忽略本次请求");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[P6-C][Profiler] 编辑器正在播放或切换模式（可能有其他工作在跑），请先停止后再发起采样");
                return;
            }
            if (!EnsureMainSceneLoaded()) return;

            EditorPrefs.SetFloat(DurationKey, durationSeconds);
            EditorPrefs.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + durationSeconds + 90.0));
            EditorPrefs.SetBool(ActiveKey, true);
            _lastPlayRequestTime = EditorApplication.timeSinceStartup;
            Debug.Log($"[P6-C][Profiler] 真实采样会话已排程（{durationSeconds:F0}s），进入 PlayMode…");
            EditorApplication.isPlaying = true;
        }

        /// <summary>主动放弃当前会话（不写报告）。</summary>
        public static void AbortSession(string reason)
        {
            if (!EditorPrefs.GetBool(ActiveKey, false)) return;
            EditorPrefs.SetBool(ActiveKey, false);
            Debug.LogWarning($"[P6-C][Profiler] 采样会话已放弃: {reason}");
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        private static bool EnsureMainSceneLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).path == BuildAndProfilerSuite.MainScenePath)
                {
                    return true;
                }
            }

            var active = SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                Debug.LogWarning("[P6-C][Profiler] 主场景未打开，且当前场景有未保存修改；为避免冲刷他人工作已取消采样。请手动打开 PhaseA_Playbox 后重试。");
                return false;
            }
            EditorSceneManager.OpenScene(BuildAndProfilerSuite.MainScenePath, OpenSceneMode.Single);
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                _probeHolder = null;
                _probe = null;
                _warmTicksLeft = 0;
                _awaitingPlayingPhase = false;
                return;
            }

            if (change != PlayModeStateChange.EnteredPlayMode) return;
            if (!EditorPrefs.GetBool(ActiveKey, false) || _probe != null) return;

            float duration = Mathf.Clamp(
                EditorPrefs.GetFloat(DurationKey, DefaultDurationSeconds),
                MinDurationSeconds, MaxDurationSeconds);

            _probeHolder = new GameObject("[P6-C Real Profiler]");
            _probe = _probeHolder.AddComponent<PerformanceProfilerProbe>();
            _pendingDuration = duration;
            _warmTicksLeft = WarmupTicksAfterPlayEntry;
            Debug.Log($"[P6-C][Profiler] PlayMode 已进入，预热 {WarmupTicksAfterPlayEntry} 帧后开始实测 {duration:F0}s…");
        }

        private static void OnEditorUpdate()
        {
            if (!EditorPrefs.GetBool(ActiveKey, false)) return;

            double now = EditorApplication.timeSinceStartup;
            float deadline = EditorPrefs.GetFloat(DeadlineKey, 0f);

            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return; // 正在切换途中

                if (now > deadline)
                {
                    AbortSession("PlayMode 未进入");
                    return;
                }

                // 进入 PlayMode 的请求可能被编译/域重载尾部静默吞掉：限频重发，直到截止时限
                if (now - _lastPlayRequestTime >= PlayRequestRetryIntervalSeconds)
                {
                    _lastPlayRequestTime = now;
                    Debug.Log("[P6-C][Profiler] 尚未进入 PlayMode（可能被域重载吞掉），重发进入请求…");
                    EditorApplication.isPlaying = true;
                }
                return;
            }

            if (_probe == null) return; // 仍在 Domain Reload 过程中

            // 入场预热：空跑固定帧数让场景装载/材质预热的首包分配落进预热期，不污染战斗稳态数据
            if (_warmTicksLeft > 0)
            {
                _warmTicksLeft--;
                if (_warmTicksLeft == 0)
                {
                    if (MatchManager.Instance != null)
                    {
                        // 无人值守会话没人点「开始」：场景默认 Ready 阶段封锁战斗（Bot 不开火、不落墨），
                        // 必须主动 StartMatch 并等进入 Playing 再采样，否则测的是「待机」而不是「战斗」。
                        MatchManager.Instance.StartMatch();
                        _awaitingPlayingPhase = true;
                        Debug.Log("[P6-C][Profiler] 预热完成，已触发 StartMatch，等待 Playing 阶段…");
                    }
                    else
                    {
                        BeginSampling();
                    }
                }
                return;
            }

            if (_awaitingPlayingPhase)
            {
                if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase == MatchManager.Phase.Playing)
                {
                    _awaitingPlayingPhase = false;
                    BeginSampling();
                }
                return;
            }

            bool finished = !_probe.IsSampling && _probe.SampleCount > 0;
            if (finished)
            {
                CompleteSession();
                return;
            }

            if (now > deadline)
            {
                AbortSession($"超过截止时限仍未完成（已采样 {_probe.SampleCount} 帧，Elapsed {_probe.ElapsedTime:F1}s）");
            }
        }

        private static void BeginSampling()
        {
            _probe.StartSampling(
                $"Editor PlayMode 实测 · {SystemInfo.deviceModel}",
                _pendingDuration, 0, 0);
            Debug.Log($"[P6-C][Profiler] 开始逐帧实测 {_pendingDuration:F0}s");
        }

        private static void CompleteSession()
        {
            PerformanceProfilerProbe.SamplingReport report = null;
            try
            {
                report = _probe.StopAndGenerateReport();
                report.EnvironmentInfo = PerformanceProfilerProbe.BuildEnvironmentInfo();
                string doc = BuildReportDocument(report);
                Directory.CreateDirectory(Path.GetDirectoryName(BuildAndProfilerSuite.ProfilerReportPath));
                File.WriteAllText(BuildAndProfilerSuite.ProfilerReportPath, doc);
            }
            catch (Exception e)
            {
                Debug.LogError($"[P6-C][Profiler] 报告生成失败: {e.Message}");
            }

            EditorPrefs.SetBool(ActiveKey, false);
            EditorPrefs.SetString(CompletedKey, DateTime.UtcNow.Ticks.ToString());

            if (report != null)
            {
                Debug.Log($"[P6-C][Profiler][DONE] 真实采样报告已写入 {BuildAndProfilerSuite.ProfilerReportPath}");
            }

            _probe = null;
            if (_probeHolder != null)
            {
                UnityEngine.Object.Destroy(_probeHolder);
                _probeHolder = null;
            }
            EditorApplication.isPlaying = false;
        }

        private static string BuildReportDocument(PerformanceProfilerProbe.SamplingReport report)
        {
            const string sourceLabel =
                "真实 PlayMode 战斗逐帧实测（Time.unscaledDeltaTime + ProfilerRecorder + InkSurfaceController 上报），" +
                "无任何仿真或写死数值";

            string guidance =
                "## 真机数据状态与红线口径\n" +
                "- 本表为 **编辑器 PlayMode** 数据：帧率受编辑器自身开销与「帧率上限」设置影响，\n" +
                "  桌面 60 FPS / Android 30+ FPS 红线 **必须以 Player 包（真机/本机运行 Development Build）实测为准**。\n" +
                "- Player 自动采样流程（`P1.Core.AutoProfileRuntimeService`）：\n" +
                "  1. 构建 Development Build（菜单 `P1/构建/8` 或 9，改传 `BuildOptions.Development`）；\n" +
                "  2. 放置请求文件（内容为采样秒数）：Android 例 " +
                "`adb push profile_request.txt /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfile.request`；\n" +
                "  3. 启动游戏（预热 5s 后自动实测并结束），报告写入同目录 `P1_AutoProfiles/`；\n" +
                "  4. 回收：`adb pull /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfiles/ Docs/`。\n" +
                "- 在获得 Player 实测数据前，本项目的移动端性能红线状态应记为 **未验证**。\n" +
                "\n" +
                "## 历史数据作废说明\n" +
                "旧版 `Docs/ProfilerReport_P6C.md`（≤ 2026-10-30）由 `BuildAndProfilerSuite.SimulateBattleSampling` 的离线仿真生成" +
                "（FPS 来自 `Random(42)`、平均帧率硬编码 60.1、DrawCalls 45~75 为写死文案、GC 0 B/帧测的是仿真循环），全部作废；" +
                "其「验收圆满通过」结论不成立。旧版纹理上传判定使用 `Peak < 1.5 || P99 < 1.2` 的 OR 条件会掩盖单帧尖刺，已改为均值+P99 双条件。";

            string doc = PerformanceProfilerProbe.BuildStandardReport(report, sourceLabel, guidance);
            return doc;
        }
    }
}
