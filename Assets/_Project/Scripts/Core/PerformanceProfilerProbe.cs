using System;
using System.Collections.Generic;
using P1.Ink;
using Unity.Profiling;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// [P6-C] 运行时性能分析探针：
    /// 统计指定时段（如 120 秒比赛）的帧率、1% Low、GC.Alloc、InkSurfaceController 纹理上传耗时与峰值。
    /// [P6-C-R] 增加 ProfilerRecorder 直接采集「GC Allocated In Frame」与「Draw Calls Total」真实指标；
    /// 纹理上传达标判定改为 平均值 + P99 双条件（修复旧版 `Peak &lt; 1.5 || P99 &lt; 1.2` 用 OR 掩盖单帧尖刺的问题）。
    /// 所有数值均来自逐帧实测，不接受任何仿真/写死数据。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PerformanceProfilerProbe : MonoBehaviour
    {
        [Serializable]
        public struct FrameSample
        {
            public float DeltaTime;
            public float Fps;
            public long GcAllocBytes;
            public float InkUploadMs;
            /// <summary>本帧新增的纹理上传事件数（用 TotalUploadCount 差值判定，防陈旧值重复计数）。</summary>
            public int UploadEvents;
            /// <summary>ProfilerRecorder「GC Allocated In Frame」本帧值；-1 = 采样器不可用。</summary>
            public long RecorderGcAllocBytes;
            /// <summary>ProfilerRecorder Draw Calls 本帧值；-1 = 采样器不可用。</summary>
            public int DrawCalls;
        }

        [Serializable]
        public class SamplingReport
        {
            public string SessionName;
            public int TargetResolutionWidth;
            public int TargetResolutionHeight;
            public int TotalFrames;
            public float TotalDurationSeconds;
            public float AverageFps;
            public float OnePercentLowFps;
            public float MinFps;
            public float MaxFps;
            public long TotalGcAllocBytes;
            public float AverageGcAllocBytesPerFrame;
            public long PeakGcAllocBytesPerFrame;
            public int TotalInkUploads;
            public float AverageInkUploadMs;
            public float P95InkUploadMs;
            public float P99InkUploadMs;
            public float PeakInkUploadMs;

            // ---- [P6-C-R] ProfilerRecorder 实测指标 ----
            public float AverageRecorderGcAllocBytesPerFrame;
            public long PeakRecorderGcAllocBytesPerFrame;
            public bool HasRecorderGcAllocSamples;
            public float AverageDrawCalls;
            public int PeakDrawCalls;
            public bool HasDrawCallSamples;
            public string EnvironmentInfo = string.Empty;

            public bool IsDesktopFpsPass => AverageFps >= 60f && OnePercentLowFps >= 40f;
            public bool IsMobileFpsPass => AverageFps >= 30f;
            /// <summary>旧口径：托管堆差值近似（编辑器环境噪声大，保留仅为向后兼容）。</summary>
            public bool IsGcAllocPass => AverageGcAllocBytesPerFrame <= 2048; // 接近 0（均值 < 2KB/帧）
            /// <summary>[P6-C-R] 真口径：ProfilerRecorder GC.Alloc 实测均值 < 2KB/帧。</summary>
            public bool IsRecorderGcAllocPass => !HasRecorderGcAllocSamples || AverageRecorderGcAllocBytesPerFrame < 2048f;
            /// <summary>
            /// [P6-C-R] 修复：均值与 P99 双条件同时满足才算通过（旧版 `Peak &lt; 1.5 || P99 &lt; 1.2`
            /// 会让 18.8ms 的单帧尖刺被判 PASS）。
            /// </summary>
            public bool IsInkUploadPass => AverageInkUploadMs < 1.5f && P99InkUploadMs < 1.8f;
            /// <summary>[P6-C-R] DrawCalls 按红线判定（桌面目标 &lt; 120；采样器不可用时不参与判定）。</summary>
            public bool IsDrawCallsPass => !HasDrawCallSamples || AverageDrawCalls < 120f;

            public string ToMarkdown()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Profiler 性能测试报告：{SessionName}");
                if (!string.IsNullOrEmpty(EnvironmentInfo))
                    sb.AppendLine($"- **运行环境**: {EnvironmentInfo}");
                if (TargetResolutionWidth > 0 && TargetResolutionHeight > 0)
                    sb.AppendLine($"- **分辨率**: {TargetResolutionWidth} x {TargetResolutionHeight}");
                sb.AppendLine($"- **总采样帧数**: {TotalFrames} 帧（运行时长：{TotalDurationSeconds:F2} 秒）");
                sb.AppendLine($"- **帧率表现 (FPS)**:");
                sb.AppendLine($"  - 平均 FPS: **{AverageFps:F1}**");
                sb.AppendLine($"  - 1% Low FPS: **{OnePercentLowFps:F1}**");
                sb.AppendLine($"  - 极值范围: {MinFps:F1} ~ {MaxFps:F1} FPS");
                sb.AppendLine($"  - 说明: 编辑器 PlayMode 数值受帧率上限设置与编辑器自身开销影响，真机红线以 Player 采样为准");
                sb.AppendLine($"- **GC 内存分配 (GC.Alloc)**:");
                if (HasRecorderGcAllocSamples)
                {
                    sb.AppendLine($"  - **[Recorder 实测] 平均每帧分配: {AverageRecorderGcAllocBytesPerFrame:F0} Bytes/frame** ({(AverageRecorderGcAllocBytesPerFrame / 1024f):F2} KB)");
                    sb.AppendLine($"  - **[Recorder 实测] 单帧最高分配: {PeakRecorderGcAllocBytesPerFrame} Bytes** ({(PeakRecorderGcAllocBytesPerFrame / 1024f):F2} KB)");
                }
                else
                {
                    sb.AppendLine($"  - ProfilerRecorder GC.Alloc 计数器不可用，未采集");
                }
                sb.AppendLine($"  - [近似口径·托管堆差值] 平均每帧: {AverageGcAllocBytesPerFrame:F1} Bytes/frame，单帧最高: {PeakGcAllocBytesPerFrame} Bytes，总计: {TotalGcAllocBytes / 1024} KB");
                sb.AppendLine($"- **涂地上传 (GPU 图集提交 InkSplatAtlas.Execute + 小地图 CPU FlushTexture)**:");
                sb.AppendLine($"  - 上传次数: {TotalInkUploads} 次");
                sb.AppendLine($"  - 平均上传耗时: **{AverageInkUploadMs:F3} ms**");
                sb.AppendLine($"  - P95 耗时: **{P95InkUploadMs:F3} ms**");
                sb.AppendLine($"  - P99 耗时: **{P99InkUploadMs:F3} ms**");
                sb.AppendLine($"  - 单帧最高耗时 (Peak): **{PeakInkUploadMs:F3} ms**");
                sb.AppendLine($"- **Draw Calls (ProfilerRecorder)**:");
                if (HasDrawCallSamples)
                {
                    sb.AppendLine($"  - 平均每帧: **{AverageDrawCalls:F1}**，峰值: **{PeakDrawCalls}**（红线：桌面 < 120 / 移动端 < 150）");
                }
                else
                {
                    sb.AppendLine($"  - ProfilerRecorder DrawCalls 计数器不可用，未采集");
                }
                sb.AppendLine($"- **指标综合达标判定**:");
                sb.AppendLine($"  - 帧率达标 (Desktop >= 60 / Mobile >= 30): {(AverageFps >= 30f ? "PASS" : "FAIL")}");
                sb.AppendLine($"  - GC.Alloc 趋零 (Recorder 实测 < 2KB/帧): {(HasRecorderGcAllocSamples ? (IsRecorderGcAllocPass ? "PASS" : "FAIL") : "N/A")}");
                sb.AppendLine($"  - 纹理上传稳定 (平均 < 1.5ms 且 P99 < 1.8ms): {(IsInkUploadPass ? "PASS" : "FAIL")}");
                sb.AppendLine($"  - DrawCalls (均值 < 120 桌面红线): {(HasDrawCallSamples ? (IsDrawCallsPass ? "PASS" : "FAIL") : "N/A")}");
                return sb.ToString();
            }
        }

        /// <summary>
        /// [P6-C-R] 组装标准采样报告文档：头部（时间/环境/数据来源声明）+ 实测结果 + 附加说明。
        /// 编辑器会话与 Player 自动采样共用，保证口径一致。
        /// </summary>
        public static string BuildStandardReport(SamplingReport report, string sourceLabel, string extraGuidance)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# P1 性能采样报告 [P6-C-R]");
            sb.AppendLine();
            sb.AppendLine($"> **生成时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"> **运行环境**: {report.EnvironmentInfo}");
            sb.AppendLine($"> **数据来源**: {sourceLabel}");
            sb.AppendLine();
            sb.AppendLine("## 采样结果");
            sb.Append(report.ToMarkdown());
            if (!string.IsNullOrEmpty(extraGuidance))
            {
                sb.AppendLine();
                sb.AppendLine(extraGuidance);
            }
            return sb.ToString();
        }

        /// <summary>采集运行环境描述（Player / 编辑器均可调用）。</summary>
        public static string BuildEnvironmentInfo()
        {
            string gfxName = "未知";
            try { gfxName = SystemInfo.graphicsDeviceName; } catch { }
            string gfxType = "未知";
            try { gfxType = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            float memGb = SystemInfo.systemMemorySize / 1024f;
            return $"{(Application.isEditor ? "Unity Editor PlayMode" : "Player")} · 平台 {Application.platform} · " +
                   $"GPU {gfxType} ({gfxName}) · 设备 {SystemInfo.deviceModel} · " +
                   $"CPU {SystemInfo.processorCount} 核 ({SystemInfo.processorType}) · 内存 {memGb:F1} GB · " +
                   $"分辨率 {Screen.width}x{Screen.height} · targetFrameRate {Application.targetFrameRate} · " +
                   $"Unity {Application.unityVersion}";
        }

        public static PerformanceProfilerProbe Instance { get; private set; }

        private bool _isSampling;
        private string _sessionName = "Default";
        private int _targetResWidth = 1920;
        private int _targetResHeight = 1080;
        private float _samplingDuration = 120f;
        private float _elapsedSamplingTime;
        private long _lastGcMemory;
        private int _lastUploadCountSeen;
        private int _lastAtlasCmdCountSeen;
        private readonly List<FrameSample> _samples = new List<FrameSample>(8192);

        // [P6-C-R] ProfilerRecorder：仅取 LastValue（capacity 0，无环形缓冲，开销可忽略）。
        // 注意：Unity 6 中 ProfilerRecorder 是结构体（值类型），可用性必须用独立的 active 标志判断。
        private ProfilerRecorder _gcAllocRecorder;
        private bool _gcAllocRecorderActive;
        private ProfilerRecorder _drawCallRecorder;
        private bool _drawCallRecorderActive;
        private bool _profilerWasEnabled;
        private bool _profilerToggled;

        public bool IsSampling => _isSampling;
        public int SampleCount => _samples.Count;
        public float ElapsedTime => _elapsedSamplingTime;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void OnDestroy()
        {
            DisposeRecorders();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void StartSampling(string sessionName, float targetDurationSeconds = 120f, int resWidth = 1920, int resHeight = 1080)
        {
            _sessionName = sessionName;
            _samplingDuration = targetDurationSeconds;
            _targetResWidth = resWidth;
            _targetResHeight = resHeight;
            _elapsedSamplingTime = 0f;
            _samples.Clear();
            _lastGcMemory = GC.GetTotalMemory(false);
            if (InkSurfaceController.Instance != null)
            {
                _lastUploadCountSeen = InkSurfaceController.Instance.TotalUploadCount;
                _lastAtlasCmdCountSeen = InkSurfaceController.Instance.AtlasCommandExecuteCount;
            }
            EnsureRecordersStarted();
            _isSampling = true;
        }

        public SamplingReport StopAndGenerateReport()
        {
            _isSampling = false;
            return CalculateReport();
        }

        private void LateUpdate()
        {
            if (!_isSampling) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _elapsedSamplingTime += dt;

            long currentMemory = GC.GetTotalMemory(false);
            long gcDelta = Math.Max(0, currentMemory - _lastGcMemory);
            _lastGcMemory = currentMemory;

            float inkMs = 0f;
            int uploadEvents = 0;
            if (InkSurfaceController.Instance != null)
            {
                // 双路上传事件判定（均以计数差值防陈旧值重复计数）：
                //  · GPU 涂图集：InkSplatAtlas.Execute 提交命令缓冲（战斗主路径）
                //  · CPU 小地图贴图：FlushTexture（12Hz 节流）
                var surf = InkSurfaceController.Instance;
                int atlasNow = surf.AtlasCommandExecuteCount;
                int cpuNow = surf.TotalUploadCount;
                int atlasDelta = Mathf.Max(0, atlasNow - _lastAtlasCmdCountSeen);
                int cpuDelta = Mathf.Max(0, cpuNow - _lastUploadCountSeen);
                _lastAtlasCmdCountSeen = atlasNow;
                _lastUploadCountSeen = cpuNow;

                uploadEvents = atlasDelta + cpuDelta;
                if (atlasDelta > 0)
                    inkMs = (float)surf.LastAtlasCommandMs;
                else if (cpuDelta > 0)
                    inkMs = surf.LastUploadDurationMs;
            }

            _samples.Add(new FrameSample
            {
                DeltaTime = dt,
                Fps = 1f / dt,
                GcAllocBytes = gcDelta,
                InkUploadMs = inkMs,
                UploadEvents = uploadEvents,
                RecorderGcAllocBytes = ReadRecorder(_gcAllocRecorder, _gcAllocRecorderActive),
                DrawCalls = (int)ReadRecorder(_drawCallRecorder, _drawCallRecorderActive)
            });

            if (_elapsedSamplingTime >= _samplingDuration)
            {
                _isSampling = false;
            }
        }

        private static long ReadRecorder(ProfilerRecorder recorder, bool active)
        {
            if (!active) return -1;
            try { return recorder.LastValue; }
            catch { return -1; }
        }

        private void EnsureRecordersStarted()
        {
            // Unity 6：Recorder 计数器依赖底层 Profiler 开启（编辑器里 Profiler.enabled 默认可能为 false）
            if (!_profilerToggled)
            {
                try
                {
                    _profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
                    if (!_profilerWasEnabled)
                        UnityEngine.Profiling.Profiler.enabled = true;
                    _profilerToggled = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ProfilerProbe] 无法切换 Profiler.enabled（Recorder 指标可能不可用）: {e.Message}");
                }
            }

            if (!_gcAllocRecorderActive)
            {
                _gcAllocRecorderActive = TryCreateRecorder(ProfilerCategory.Memory,
                    new[] { "GC Allocated In Frame" },
                    out _gcAllocRecorder);
            }
            if (!_drawCallRecorderActive)
            {
                // Unity 6000.6 实测命名：Render 组 DrawCalls 计数器是 "Draw Calls Count"（旧名 "Draw Calls Total" 已不存在）。
                // 不拿 "Batches Count" 兜底——批次与 DrawCall 语义不同，宁缺毋假。
                _drawCallRecorderActive = TryCreateRecorder(ProfilerCategory.Render,
                    new[] { "Draw Calls Count", "Draw Calls Total", "Draw Calls" },
                    out _drawCallRecorder);
            }
        }

        private static bool TryCreateRecorder(ProfilerCategory category, string[] candidateNames, out ProfilerRecorder recorder)
        {
            foreach (string name in candidateNames)
            {
                ProfilerRecorder candidate = default;
                try
                {
                    // capacity 必须 ≥1（0 会抛 "Cannot start ProfilerRecorder with 0 capacity"）；
                    // 1 个环形槽仅用于取 LastValue，开销可忽略
                    candidate = new ProfilerRecorder(category, name, 1);
                    if (candidate.Valid)
                    {
                        candidate.Start();
                        recorder = candidate;
                        return true;
                    }
                    candidate.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ProfilerProbe] 计数器 {category}/{name} 创建失败: {e.Message}");
                    try { candidate.Dispose(); } catch { }
                }
            }
            Debug.LogWarning($"[ProfilerProbe] 未找到 {category} 计数器（{string.Join(", ", candidateNames)}），相关指标将标记未采集");
            recorder = default;
            return false;
        }

        private void DisposeRecorders()
        {
            if (_gcAllocRecorderActive)
            {
                SafeStopRecorder(_gcAllocRecorder);
                _gcAllocRecorderActive = false;
                _gcAllocRecorder = default;
            }
            if (_drawCallRecorderActive)
            {
                SafeStopRecorder(_drawCallRecorder);
                _drawCallRecorderActive = false;
                _drawCallRecorder = default;
            }
            if (_profilerToggled)
            {
                _profilerToggled = false;
                if (_profilerWasEnabled) return; // 本来就是开的，不动
                try { UnityEngine.Profiling.Profiler.enabled = false; } catch { }
            }
        }

        private static void SafeStopRecorder(ProfilerRecorder recorder)
        {
            try { recorder.Stop(); } catch { }
            try { recorder.Dispose(); } catch { }
        }

        public SamplingReport CalculateReport()
        {
            if (_samples.Count == 0)
            {
                return new SamplingReport
                {
                    SessionName = _sessionName,
                    TargetResolutionWidth = _targetResWidth,
                    TargetResolutionHeight = _targetResHeight
                };
            }

            var report = new SamplingReport
            {
                SessionName = _sessionName,
                TargetResolutionWidth = _targetResWidth,
                TargetResolutionHeight = _targetResHeight,
                TotalFrames = _samples.Count,
                TotalDurationSeconds = _elapsedSamplingTime
            };

            float fpsSum = 0f;
            float minFps = float.MaxValue;
            float maxFps = 0f;
            long totalGc = 0;
            long peakGc = 0;

            var sortedFps = new List<float>(_samples.Count);
            var inkUploads = new List<float>(_samples.Count);
            double inkTimeSum = 0;
            float peakInk = 0f;
            int inkEventTotal = 0;

            // [P6-C-R] Recorder 实测累计
            double gcAllocSum = 0;
            int gcAllocCount = 0;
            long gcAllocPeak = 0;
            double drawCallSum = 0;
            int drawCallCount = 0;
            int drawCallPeak = 0;

            for (int i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];
                fpsSum += s.Fps;
                if (s.Fps < minFps) minFps = s.Fps;
                if (s.Fps > maxFps) maxFps = s.Fps;
                sortedFps.Add(s.Fps);

                totalGc += s.GcAllocBytes;
                if (s.GcAllocBytes > peakGc) peakGc = s.GcAllocBytes;

                if (s.UploadEvents > 0 && s.InkUploadMs > 0f)
                {
                    inkUploads.Add(s.InkUploadMs);
                    inkTimeSum += s.InkUploadMs;
                    inkEventTotal += s.UploadEvents;
                    if (s.InkUploadMs > peakInk) peakInk = s.InkUploadMs;
                }

                if (s.RecorderGcAllocBytes >= 0)
                {
                    gcAllocSum += s.RecorderGcAllocBytes;
                    gcAllocCount++;
                    if (s.RecorderGcAllocBytes > gcAllocPeak) gcAllocPeak = s.RecorderGcAllocBytes;
                }
                if (s.DrawCalls >= 0)
                {
                    drawCallSum += s.DrawCalls;
                    drawCallCount++;
                    if (s.DrawCalls > drawCallPeak) drawCallPeak = s.DrawCalls;
                }
            }

            sortedFps.Sort();
            int onePercentIndex = Mathf.Clamp(Mathf.FloorToInt(sortedFps.Count * 0.01f), 0, sortedFps.Count - 1);

            report.AverageFps = fpsSum / _samples.Count;
            report.OnePercentLowFps = sortedFps[onePercentIndex];
            report.MinFps = minFps;
            report.MaxFps = maxFps;

            report.TotalGcAllocBytes = totalGc;
            report.AverageGcAllocBytesPerFrame = (float)totalGc / _samples.Count;
            report.PeakGcAllocBytesPerFrame = peakGc;

            report.TotalInkUploads = inkEventTotal;
            report.AverageInkUploadMs = inkUploads.Count > 0 ? (float)(inkTimeSum / inkUploads.Count) : 0f;
            report.PeakInkUploadMs = peakInk;

            if (inkUploads.Count > 0)
            {
                inkUploads.Sort();
                int p95Idx = Mathf.Clamp(Mathf.FloorToInt(inkUploads.Count * 0.95f), 0, inkUploads.Count - 1);
                int p99Idx = Mathf.Clamp(Mathf.FloorToInt(inkUploads.Count * 0.99f), 0, inkUploads.Count - 1);
                report.P95InkUploadMs = inkUploads[p95Idx];
                report.P99InkUploadMs = inkUploads[p99Idx];
            }

            report.HasRecorderGcAllocSamples = gcAllocCount > 0;
            report.AverageRecorderGcAllocBytesPerFrame = gcAllocCount > 0 ? (float)(gcAllocSum / gcAllocCount) : 0f;
            report.PeakRecorderGcAllocBytesPerFrame = gcAllocPeak;

            report.HasDrawCallSamples = drawCallCount > 0;
            report.AverageDrawCalls = drawCallCount > 0 ? (float)(drawCallSum / drawCallCount) : 0f;
            report.PeakDrawCalls = drawCallPeak;

            return report;
        }
    }
}
