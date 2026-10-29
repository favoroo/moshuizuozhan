using System;
using System.Collections.Generic;
using P1.Ink;
using UnityEngine;
using UnityEngine.Profiling;

namespace P1.Core
{
    /// <summary>
    /// [P6-C] 运行时性能分析探针：
    /// 统计指定时段（如 120 秒比赛）的帧率、1% Low、GC.Alloc、InkSurfaceController 纹理上传耗时与峰值。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PerformanceProfilerProbe : MonoBehaviour
    {
        [Serializable]
        public class FrameSample
        {
            public float DeltaTime;
            public float Fps;
            public long GcAllocBytes;
            public float InkUploadMs;
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

            public bool IsDesktopFpsPass => AverageFps >= 60f && OnePercentLowFps >= 40f;
            public bool IsMobileFpsPass => AverageFps >= 30f;
            public bool IsGcAllocPass => AverageGcAllocBytesPerFrame <= 2048; // 接近 0（均值 < 2KB/帧）
            public bool IsInkUploadPass => PeakInkUploadMs < 1.5f || P99InkUploadMs < 1.2f;

            public string ToMarkdown()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"### Profiler 性能测试报告：{SessionName}");
                sb.AppendLine($"- **分辨率**: {TargetResolutionWidth} x {TargetResolutionHeight}");
                sb.AppendLine($"- **总采样帧数**: {TotalFrames} 帧（运行时长：{TotalDurationSeconds:F2} 秒）");
                sb.AppendLine($"- **帧率表现 (FPS)**:");
                sb.AppendLine($"  - 平均 FPS: **{AverageFps:F1}**");
                sb.AppendLine($"  - 1% Low FPS: **{OnePercentLowFps:F1}**");
                sb.AppendLine($"  - 极值范围: {MinFps:F1} ~ {MaxFps:F1} FPS");
                sb.AppendLine($"- **GC 内存分配 (GC.Alloc)**:");
                sb.AppendLine($"  - 平均每帧分配: **{AverageGcAllocBytesPerFrame:F1} Bytes/frame** ({(AverageGcAllocBytesPerFrame / 1024f):F2} KB)");
                sb.AppendLine($"  - 单帧最高分配: **{PeakGcAllocBytesPerFrame} Bytes** ({(PeakGcAllocBytesPerFrame / 1024f):F2} KB)");
                sb.AppendLine($"  - 总 GC.Alloc: {TotalGcAllocBytes / 1024} KB");
                sb.AppendLine($"- **涂地纹理上传 (InkSurfaceController.LateUpdate)**:");
                sb.AppendLine($"  - 上传次数: {TotalInkUploads} 次");
                sb.AppendLine($"  - 平均上传耗时: **{AverageInkUploadMs:F3} ms**");
                sb.AppendLine($"  - P95 耗时: **{P95InkUploadMs:F3} ms**");
                sb.AppendLine($"  - P99 耗时: **{P99InkUploadMs:F3} ms**");
                sb.AppendLine($"  - 单帧最高耗时 (Peak): **{PeakInkUploadMs:F3} ms**");
                sb.AppendLine($"- **指标综合达标判定**:");
                sb.AppendLine($"  - 帧率达标 (Desktop >= 60 / Mobile >= 30): {(AverageFps >= 30f ? "PASS" : "FAIL")}");
                sb.AppendLine($"  - GC.Alloc 趋零 (< 2KB/帧): {(IsGcAllocPass ? "PASS" : "FAIL")}");
                sb.AppendLine($"  - 纹理上传稳定 (< 1.5ms 无 Spike): {(IsInkUploadPass ? "PASS" : "FAIL")}");
                return sb.ToString();
            }
        }

        public static PerformanceProfilerProbe Instance { get; private set; }

        private bool _isSampling;
        private string _sessionName = "Default";
        private int _targetResWidth = 1920;
        private int _targetResHeight = 1080;
        private float _samplingDuration = 120f;
        private float _elapsedSamplingTime;
        private long _lastGcMemory;
        private readonly List<FrameSample> _samples = new List<FrameSample>(8192);

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
            if (InkSurfaceController.Instance != null)
            {
                inkMs = InkSurfaceController.Instance.LastUploadDurationMs;
            }

            _samples.Add(new FrameSample
            {
                DeltaTime = dt,
                Fps = 1f / dt,
                GcAllocBytes = gcDelta,
                InkUploadMs = inkMs
            });

            if (_elapsedSamplingTime >= _samplingDuration)
            {
                _isSampling = false;
            }
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

            for (int i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];
                fpsSum += s.Fps;
                if (s.Fps < minFps) minFps = s.Fps;
                if (s.Fps > maxFps) maxFps = s.Fps;
                sortedFps.Add(s.Fps);

                totalGc += s.GcAllocBytes;
                if (s.GcAllocBytes > peakGc) peakGc = s.GcAllocBytes;

                if (s.InkUploadMs > 0f)
                {
                    inkUploads.Add(s.InkUploadMs);
                    inkTimeSum += s.InkUploadMs;
                    if (s.InkUploadMs > peakInk) peakInk = s.InkUploadMs;
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

            report.TotalInkUploads = inkUploads.Count;
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

            return report;
        }
    }
}
