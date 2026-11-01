using NUnit.Framework;
using P1.Core;

namespace P1.Tests
{
    /// <summary>
    /// [P6-C-R] SamplingReport 达标判定纯逻辑回归测试。
    /// 背景：旧版 IsInkUploadPass 使用 `Peak &lt; 1.5 || P99 &lt; 1.2` 的 OR 条件，
    /// 曾让移动端 Peak 18.8ms 的单帧尖刺被判 PASS（伪造报告的核心缺陷之一）。
    /// </summary>
    [TestFixture]
    public class ProfilerReportGatingTests
    {
        private static PerformanceProfilerProbe.SamplingReport MakeReport(
            float avgUpload, float p99Upload, float peakUpload)
        {
            return new PerformanceProfilerProbe.SamplingReport
            {
                SessionName = "GatingTest",
                AverageInkUploadMs = avgUpload,
                P99InkUploadMs = p99Upload,
                PeakInkUploadMs = peakUpload
            };
        }

        [Test]
        public void InkUploadPass_FailsWhenP99SpikeExceedsGate_EvenWithTinyAverage()
        {
            // 复刻旧伪报告场景：均值 0.032ms 但 Peak/P99 有尖刺 → 必须 FAIL（旧 OR 逻辑会误判 PASS）
            var report = MakeReport(avgUpload: 0.032f, p99Upload: 12.5f, peakUpload: 18.841f);
            Assert.IsFalse(report.IsInkUploadPass,
                "均值低但 P99 超标时不得判 PASS（旧 OR 判定掩盖尖刺的回归防线）");
        }

        [Test]
        public void InkUploadPass_FailsWhenAverageExceedsGate()
        {
            var report = MakeReport(avgUpload: 1.6f, p99Upload: 1.0f, peakUpload: 1.2f);
            Assert.IsFalse(report.IsInkUploadPass, "平均上传耗时超过 1.5ms 应 FAIL");
        }

        [Test]
        public void InkUploadPass_SucceedsWhenBothAverageAndP99WithinGates()
        {
            var report = MakeReport(avgUpload: 0.4f, p99Upload: 1.0f, peakUpload: 1.4f);
            Assert.IsTrue(report.IsInkUploadPass, "均值与 P99 双达标才应 PASS");
        }

        [Test]
        public void RecorderGcAllocPass_UsesTrueRecorderAverage()
        {
            var report = new PerformanceProfilerProbe.SamplingReport
            {
                HasRecorderGcAllocSamples = true,
                AverageRecorderGcAllocBytesPerFrame = 4096f
            };
            Assert.IsFalse(report.IsRecorderGcAllocPass, "Recorder 实测 4KB/帧 应 FAIL（红线 2KB/帧）");

            report.AverageRecorderGcAllocBytesPerFrame = 1024f;
            Assert.IsTrue(report.IsRecorderGcAllocPass, "Recorder 实测 1KB/帧 应 PASS");
        }

        [Test]
        public void RecorderGates_AreNotApplicableWhenCounterUnavailable()
        {
            // 采样器不可用 → 不判 FAIL（报告中标记 N/A），避免平台差异造成假阴性
            var report = new PerformanceProfilerProbe.SamplingReport();
            Assert.IsTrue(report.IsRecorderGcAllocPass);
            Assert.IsTrue(report.IsDrawCallsPass);
        }

        [Test]
        public void DrawCallsPass_FailsWhenAverageExceedsDesktopGate()
        {
            var report = new PerformanceProfilerProbe.SamplingReport
            {
                HasDrawCallSamples = true,
                AverageDrawCalls = 135f
            };
            Assert.IsFalse(report.IsDrawCallsPass, "DrawCalls 均值 135 超过桌面红线 120 应 FAIL");

            report.AverageDrawCalls = 85f;
            Assert.IsTrue(report.IsDrawCallsPass);
        }
    }
}
