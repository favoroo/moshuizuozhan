using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// [P6-C-R] Player / 真机自动性能采样服务（仅 UNITY_EDITOR 与 DEVELOPMENT_BUILD 生效）。
    ///
    /// 用法：向 `Application.persistentDataPath` 放置请求文件 `P1_AutoProfile.request`（UTF-8，内容=采样秒数，
    /// 缺省/非法则 120s），下次进入场景后自动预热 5s → 挂载 PerformanceProfilerProbe 实测 →
    /// 报告写入 `persistentDataPath/P1_AutoProfiles/profile_<时间戳>.md`（并打日志路径）。
    ///
    /// Android 示例：
    ///   adb shell mkdir -p /sdcard/Android/data/com.p1studio.inkturfdemo/files
    ///   echo 120 > profile_request.txt
    ///   adb push profile_request.txt /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfile.request
    ///   # 启动游戏，等待采样结束后：
    ///   adb pull /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfiles/ Docs/
    ///
    /// 这是唯一能证明「桌面 60 / Android 30+ FPS」红线的方式——编辑器数据不能替代 Player 数据。
    /// </summary>
    public class AutoProfileRuntimeService : MonoBehaviour
    {
        public const string RequestFileName = "P1_AutoProfile.request";
        public const string ReportFolderName = "P1_AutoProfiles";
        private const float WarmupSeconds = 5f;
        private const float DefaultSeconds = 120f;
        private const float MinSeconds = 5f;
        private const float MaxSeconds = 600f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                string requestPath = Path.Combine(Application.persistentDataPath, RequestFileName);
                if (!File.Exists(requestPath)) return;

                float seconds = DefaultSeconds;
                try
                {
                    string raw = File.ReadAllText(requestPath).Trim();
                    File.Delete(requestPath);
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
                        parsed >= MinSeconds && parsed <= MaxSeconds)
                    {
                        seconds = parsed;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AutoProfile] 请求文件解析失败，按默认 {DefaultSeconds}s 采样: {e.Message}");
                }

                var host = new GameObject("[P6-C AutoProfile]");
                GameObject.DontDestroyOnLoad(host);
                host.AddComponent<AutoProfileRuntimeService>().Begin(seconds);
                Debug.Log($"[AutoProfile] 收到采样请求：预热 {WarmupSeconds}s 后实测 {seconds:F0}s");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoProfile] 请求处理失败: {e.Message}");
            }
#endif
        }

        private float _seconds;

        private void Begin(float seconds)
        {
            _seconds = seconds;
            StartCoroutine(RunSession());
        }

        private IEnumerator RunSession()
        {
            yield return new WaitForSecondsRealtime(WarmupSeconds);

            var probe = gameObject.AddComponent<PerformanceProfilerProbe>();
            probe.StartSampling(
                $"Player 实测 · {SystemInfo.deviceModel}",
                _seconds, Screen.width, Screen.height);

            float maxWait = _seconds + 120f;
            float waited = 0f;
            while (probe.IsSampling && waited < maxWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            try
            {
                var report = probe.StopAndGenerateReport();
                report.EnvironmentInfo = PerformanceProfilerProbe.BuildEnvironmentInfo();
                WriteReport(report);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AutoProfile] 采样会话异常结束: {e.Message}");
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private static void WriteReport(PerformanceProfilerProbe.SamplingReport report)
        {
            const string sourceLabel =
                "Player 包内真实战斗逐帧实测（Time.unscaledDeltaTime + ProfilerRecorder + InkSurfaceController 上报）";

            string guidance =
                "## 红线口径\n" +
                "- 帧率：桌面 Player ≥ 60 / Android 真机 ≥ 30（本表为包内实测，可直接用于达标判定）；\n" +
                "- GC.Alloc：< 2KB/帧（Recorder 实测）；纹理上传：平均 < 1.5ms 且 P99 < 1.8ms；\n" +
                "- DrawCalls：桌面 < 120 / 移动端 < 150。";

            string doc = PerformanceProfilerProbe.BuildStandardReport(report, sourceLabel, guidance);

            string dir = Path.Combine(Application.persistentDataPath, ReportFolderName);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"profile_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(path, doc);
            Debug.Log($"[AutoProfile] 真机采样报告已写出: {path}");
        }
    }
}
