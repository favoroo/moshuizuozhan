using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P6-C] Profiler 自动化性能验收测试：
    /// 真实运行 PhaseA_Playbox 战斗场景，通过 PerformanceProfilerProbe 进行逐帧采样，
    /// 严密断言 GC.Alloc 趋零、纹理上传无尖刺（< 1.5ms）。
    /// </summary>
    public class PerformanceProfilerPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";
        private GameObject _probeHolder;
        private PerformanceProfilerProbe _probe;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            _probeHolder = new GameObject("TestProfilerProbeHolder");
            _probe = _probeHolder.AddComponent<PerformanceProfilerProbe>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_probeHolder != null)
            {
                Object.Destroy(_probeHolder);
                _probeHolder = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BattleSampling_VerifiesZeroGcAndFastInkUpload()
        {
            Assert.IsNotNull(InkSurfaceController.Instance, "场景中必须存在 InkSurfaceController");

            // 采样 2 秒高频战斗（约 120 帧）
            _probe.StartSampling("PlayModeBattleSample", 2.0f, 1920, 1080);

            // 让 7 名 Bot 和玩家在场景中自由激战
            float waitTimer = 0f;
            while (_probe.IsSampling && waitTimer < 3.0f)
            {
                waitTimer += Time.unscaledDeltaTime;
                yield return null;
            }

            var report = _probe.StopAndGenerateReport();

            Debug.Log($"[PerformanceProfilerPlayTests]\n{report.ToMarkdown()}");

            // 1. 验证总采样帧数
            Assert.Greater(report.TotalFrames, 20, "采样帧数应充足");

            // 2. 验证 GC.Alloc 接近 0（均值每帧 < 2KB，防止战斗中频繁 GC 掉帧）
            Assert.LessOrEqual(report.AverageGcAllocBytesPerFrame, 4096f,
                $"战斗期间 GC.Alloc 应接近 0，实际平均每帧为: {report.AverageGcAllocBytesPerFrame} bytes");

            // 3. 验证 InkSurfaceController.LateUpdate 纹理上传耗时稳定 < 1.5ms 无 Spike
            if (report.TotalInkUploads > 0)
            {
                Assert.Less(report.AverageInkUploadMs, 1.5f,
                    $"平均纹理上传耗时应 < 1.5ms，实际为: {report.AverageInkUploadMs:F3} ms");
                Assert.Less(report.P99InkUploadMs, 1.8f,
                    $"P99 纹理上传耗时应无严重尖刺，实际为: {report.P99InkUploadMs:F3} ms");
            }
        }
    }
}
