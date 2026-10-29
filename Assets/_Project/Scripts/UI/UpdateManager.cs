using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using P1.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 应用内更新信息载体。
    /// </summary>
    [Serializable]
    public class UpdateInfo
    {
        public string version = "";
        public string tagName = "";
        public string releaseName = "";
        public string releaseNotes = "";
        public string downloadUrl = "";
        public List<string> candidateDownloadUrls = new List<string>();
        public string releaseUrl = "";
        public long fileSize = 0;
        public string fileSizeText = "";
        public bool hasApk = false;
    }

    /// <summary>
    /// 应用内更新管理器（单例）。
    /// 对标 Godot 项目的 UpdateManager.gd：
    /// 1. 国内 Gitee 优先、GitHub 备选、代理镜像兜底；
    /// 2. 异步检测、真实字节流下载进度采样与安装调起；
    /// 3. 全局 Toast 提示与更新弹窗（UpdateDialog）。
    /// </summary>
    public class UpdateManager : MonoBehaviour
    {
        private static UpdateManager _instance;
        public static UpdateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[UpdateManager]");
                    _instance = go.AddComponent<UpdateManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public event Action<UpdateInfo> OnUpdateAvailable;
        public event Action OnNoUpdateFound;
        public event Action<string> OnCheckFailed;
        public event Action<long, long, float, float, float> OnDownloadProgress; // loaded, total, percent, speed, etaSec
        public event Action<string> OnDownloadCompleted;
        public event Action<string> OnDownloadFailed;

        public const string ApkFileName = "moshuizuozhan_update.apk";

        public bool IsChecking { get; private set; }
        public bool IsDownloading { get; private set; }
        public UpdateInfo PendingUpdate { get; private set; }

        private bool _lastCheckManual;
        private Coroutine _checkCoroutine;
        private Coroutine _downloadCoroutine;
        private UnityWebRequest _downloadRequest;
        private bool _isCancelled;

        // Toast 控件
        private Canvas _toastCanvas;
        private RectTransform _toastPanel;
        private Text _toastText;
        private CanvasGroup _toastGroup;
        private Coroutine _toastCoroutine;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            ConnectDefaultEvents();
        }

        private void ConnectDefaultEvents()
        {
            OnUpdateAvailable += info =>
            {
                _lastCheckManual = false;
                UpdateDialog.Instance.PopupUpdate(info);
            };

            OnNoUpdateFound += () =>
            {
                if (_lastCheckManual)
                {
                    _lastCheckManual = false;
                    ShowToast($"当前已是最新版本 ({GameVersion.APP_VERSION_NAME})", new Color(0.2f, 0.85f, 0.45f));
                }
            };

            OnCheckFailed += errMsg =>
            {
                if (_lastCheckManual)
                {
                    _lastCheckManual = false;
                    ShowToast($"检查更新失败（{errMsg}），请稍后重试", new Color(1f, 0.35f, 0.35f));
                }
            };
        }

        /// <summary>
        /// 发起更新检测（manual = true 表示用户手动点击）。
        /// </summary>
        public void CheckForUpdate(bool manual = false)
        {
            if (IsChecking)
            {
                if (manual) _lastCheckManual = true;
                return;
            }

            _lastCheckManual = manual;
            IsChecking = true;

            if (_checkCoroutine != null) StopCoroutine(_checkCoroutine);
            _checkCoroutine = StartCoroutine(DoCheckAsync());
        }

        private IEnumerator DoCheckAsync()
        {
            string giteeUrl = $"https://gitee.com/api/v5/repos/{GameVersion.GITEE_OWNER}/{GameVersion.GITEE_REPO}/releases/latest";
            string githubUrl = $"https://api.github.com/repos/{GameVersion.GITHUB_OWNER}/{GameVersion.GITHUB_REPO}/releases/latest";

            var endpoints = new List<string> { giteeUrl, githubUrl };
            foreach (var p in GameVersion.GH_PROXIES)
            {
                endpoints.Add(p + githubUrl);
            }

            string jsonResult = null;
            string lastError = "";

            foreach (var ep in endpoints)
            {
                using (var req = UnityWebRequest.Get(ep))
                {
                    req.timeout = 8;
                    req.SetRequestHeader("User-Agent", "Moshuizuozhan-UnityClient");
                    req.SetRequestHeader("Accept", "application/vnd.github+json, application/json");

                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                    {
                        jsonResult = req.downloadHandler.text;
                        if (!string.IsNullOrEmpty(jsonResult) && (jsonResult.Contains("\"tag_name\"") || jsonResult.Contains("\"name\"")))
                        {
                            break;
                        }
                    }
                    else
                    {
                        lastError = $"{req.responseCode}: {req.error}";
                    }
                }
            }

            IsChecking = false;

            if (string.IsNullOrEmpty(jsonResult))
            {
                string msg = !string.IsNullOrEmpty(lastError) ? lastError : "无法连接到更新服务器";
                OnCheckFailed?.Invoke(msg);
                yield break;
            }

            UpdateInfo info = ParseReleaseJson(jsonResult);
            if (info == null || string.IsNullOrEmpty(info.tagName))
            {
                OnCheckFailed?.Invoke("更新数据解析失败");
                yield break;
            }

            if (!GameVersion.IsVersionNewer(info.tagName, GameVersion.APP_VERSION))
            {
                PendingUpdate = null;
                OnNoUpdateFound?.Invoke();
                yield break;
            }

            PendingUpdate = info;
            OnUpdateAvailable?.Invoke(info);
        }

        /// <summary>
        /// 从 Release API JSON 中提取更新元数据。
        /// </summary>
        public static UpdateInfo ParseReleaseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var info = new UpdateInfo();

            // 检查 draft / prerelease
            if (Regex.IsMatch(json, "\"draft\"\\s*:\\s*true", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(json, "\"prerelease\"\\s*:\\s*true", RegexOptions.IgnoreCase))
            {
                return null;
            }

            var tagMatch = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (tagMatch.Success) info.tagName = tagMatch.Groups[1].Value.Trim();

            var nameMatch = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            if (nameMatch.Success) info.releaseName = nameMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(info.tagName)) info.tagName = info.releaseName;

            info.version = info.tagName.TrimStart('v', 'V');

            // 提取 html_url
            var htmlMatch = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
            info.releaseUrl = htmlMatch.Success ? htmlMatch.Groups[1].Value.Trim() : GameVersion.GetReleasePageUrl(info.tagName);

            // 提取 body（更新日志）
            var bodyMatch = Regex.Match(json, "\"body\"\\s*:\\s*\"([\\s\\S]*?)\"\\s*,\\s*\"(?:author|created_at|assets|url)\"");
            if (bodyMatch.Success)
            {
                info.releaseNotes = Regex.Unescape(bodyMatch.Groups[1].Value);
            }
            else
            {
                var bodyFallback = Regex.Match(json, "\"body\"\\s*:\\s*\"([^\"]*)\"");
                info.releaseNotes = bodyFallback.Success ? Regex.Unescape(bodyFallback.Groups[1].Value) : "暂无更新说明";
            }

            // 提取 assets 中的 APK
            var assetBlockMatches = Regex.Matches(json, "\\{[^\\{\\}]*?\"name\"\\s*:\\s*\"([^\"]*?\\.apk)\"[^\\{\\}]*?\\}", RegexOptions.IgnoreCase);
            string rawApkUrl = "";
            long apkSize = 0;

            foreach (Match block in assetBlockMatches)
            {
                string blockText = block.Value;
                var urlMatch = Regex.Match(blockText, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                if (urlMatch.Success)
                {
                    rawApkUrl = urlMatch.Groups[1].Value;
                    var sizeMatch = Regex.Match(blockText, "\"size\"\\s*:\\s*(\\d+)");
                    if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out long s))
                    {
                        apkSize = s;
                    }
                    break;
                }
            }

            info.hasApk = !string.IsNullOrEmpty(rawApkUrl);
            info.fileSize = apkSize;
            info.fileSizeText = GameVersion.FormatBytes(apkSize);

            // 候选下载源
            info.candidateDownloadUrls.Clear();
            if (info.hasApk)
            {
                if (rawApkUrl.Contains("gitee.com"))
                {
                    info.candidateDownloadUrls.Add(rawApkUrl);
                    string fileName = Path.GetFileName(rawApkUrl);
                    string ghDirect = $"https://github.com/{GameVersion.GITHUB_OWNER}/{GameVersion.GITHUB_REPO}/releases/download/{info.tagName}/{fileName}";
                    foreach (var p in GameVersion.GH_PROXIES)
                    {
                        info.candidateDownloadUrls.Add(p + ghDirect);
                    }
                    info.candidateDownloadUrls.Add(ghDirect);
                }
                else if (rawApkUrl.StartsWith("https://github.com/"))
                {
                    foreach (var p in GameVersion.GH_PROXIES)
                    {
                        info.candidateDownloadUrls.Add(p + rawApkUrl);
                    }
                    info.candidateDownloadUrls.Add(rawApkUrl);
                }
                else
                {
                    info.candidateDownloadUrls.Add(rawApkUrl);
                }
                info.downloadUrl = info.candidateDownloadUrls[0];
            }
            else
            {
                info.downloadUrl = info.releaseUrl;
                info.candidateDownloadUrls.Add(info.releaseUrl);
            }

            return info;
        }

        public string GetApkSavePath()
        {
            return Path.Combine(Application.persistentDataPath, ApkFileName);
        }

        /// <summary>
        /// 开始下载 APK 附件。
        /// </summary>
        public void StartDownload(UpdateInfo info)
        {
            if (IsDownloading) return;

            if (_downloadCoroutine != null) StopCoroutine(_downloadCoroutine);
            _downloadCoroutine = StartCoroutine(DoDownloadAsync(info));
        }

        private IEnumerator DoDownloadAsync(UpdateInfo info)
        {
            IsDownloading = true;
            _isCancelled = false;

            var candidates = new List<string>(info.candidateDownloadUrls);
            if (candidates.Count == 0 && !string.IsNullOrEmpty(info.downloadUrl))
            {
                candidates.Add(info.downloadUrl);
            }

            string savePath = GetApkSavePath();
            bool succeeded = false;
            string lastErr = "";

            for (int i = 0; i < candidates.Count; i++)
            {
                if (_isCancelled) break;

                string url = candidates[i];
                Debug.Log($"[UpdateManager] 尝试下载候选源 {i + 1}/{candidates.Count}: {url}");

                // 移除之前未写完的旧文件
                if (File.Exists(savePath))
                {
                    try { File.Delete(savePath); } catch { }
                }

                using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    _downloadRequest = req;
                    req.downloadHandler = new DownloadHandlerFile(savePath);
                    req.timeout = 300;
                    req.SetRequestHeader("User-Agent", "Moshuizuozhan-UnityClient");
                    req.SetRequestHeader("Accept", "application/octet-stream, */*");

                    var op = req.SendWebRequest();

                    long expectedSize = info.fileSize;
                    long lastSampleBytes = 0;
                    float lastSampleTime = Time.realtimeSinceStartup;
                    float smoothSpeed = 0f;

                    while (!op.isDone)
                    {
                        if (_isCancelled)
                        {
                            req.Abort();
                            yield break;
                        }

                        long downloaded = (long)req.downloadedBytes;
                        float now = Time.realtimeSinceStartup;
                        float dt = now - lastSampleTime;

                        if (dt >= 0.25f && downloaded >= lastSampleBytes)
                        {
                            float instantSpeed = (downloaded - lastSampleBytes) / dt;
                            smoothSpeed = smoothSpeed > 0f ? (smoothSpeed * 0.65f + instantSpeed * 0.35f) : instantSpeed;
                            lastSampleTime = now;
                            lastSampleBytes = downloaded;
                        }

                        long total = expectedSize > 0 ? expectedSize : downloaded;
                        float percent = total > 0 ? Mathf.Clamp((float)downloaded / total * 100f, 0f, 99f) : 0f;
                        float etaSec = (smoothSpeed > 0f && total > downloaded) ? (total - downloaded) / smoothSpeed : 0f;

                        OnDownloadProgress?.Invoke(downloaded, total, percent, smoothSpeed, etaSec);
                        yield return null;
                    }

                    _downloadRequest = null;

                    if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                    {
                        succeeded = true;
                        long finalSize = new FileInfo(savePath).Length;
                        OnDownloadProgress?.Invoke(finalSize, finalSize, 100f, smoothSpeed, 0f);
                        OnDownloadCompleted?.Invoke(savePath);
                        break;
                    }
                    else
                    {
                        lastErr = $"HTTP {req.responseCode}: {req.error}";
                        Debug.LogWarning($"[UpdateManager] 候选源 {i + 1} 失败: {lastErr}");
                    }
                }
            }

            IsDownloading = false;

            if (!succeeded && !_isCancelled)
            {
                OnDownloadFailed?.Invoke($"全部候选下载源均失败 ({lastErr})");
            }
        }

        /// <summary>
        /// 取消正在进行的下载。
        /// </summary>
        public void CancelDownload()
        {
            if (!IsDownloading) return;
            _isCancelled = true;
            IsDownloading = false;
            if (_downloadRequest != null)
            {
                _downloadRequest.Abort();
                _downloadRequest = null;
            }
            if (_downloadCoroutine != null)
            {
                StopCoroutine(_downloadCoroutine);
                _downloadCoroutine = null;
            }
        }

        /// <summary>
        /// 调起安装下载的 APK。
        /// </summary>
        public bool InstallApk(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogError($"[UpdateManager] 安装失败：文件不存在 {filePath}");
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var fileObj = new AndroidJavaObject("java.io.File", filePath))
                {
                    AndroidJavaObject uri;
                    int sdkInt = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                    if (sdkInt >= 24)
                    {
                        string authority = currentActivity.Call<string>("getPackageName") + ".fileprovider";
                        using (var fileProvider = new AndroidJavaClass("androidx.core.content.FileProvider"))
                        {
                            uri = fileProvider.CallStatic<AndroidJavaObject>("getUriForFile", currentActivity, authority, fileObj);
                        }
                    }
                    else
                    {
                        uri = uriClass.CallStatic<AndroidJavaObject>("fromFile", fileObj);
                    }

                    intent.Call<AndroidJavaObject>("setDataAndType", uri, "application/vnd.android.package-archive");
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                    intent.Call<AndroidJavaObject>("addFlags", 0x00000001); // FLAG_GRANT_READ_URI_PERMISSION
                    currentActivity.Call("startActivity", intent);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UpdateManager] Android 原生调起安装异常: {ex}");
                Application.OpenURL("file://" + filePath);
                return true;
            }
#else
            // 桌面环境打开所在目录
            string dir = Path.GetDirectoryName(filePath);
            Application.OpenURL("file://" + dir);
            ShowToast("APK 已下载完毕，已打开所在文件夹", new Color(0.2f, 0.85f, 0.45f));
            return true;
#endif
        }

        /// <summary>
        /// 全局轻量 Toast 提示。
        /// </summary>
        public void ShowToast(string message, Color? accentColor = null, float duration = 2.4f)
        {
            EnsureToastUI();
            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastCoroutine = StartCoroutine(AnimateToast(message, accentColor ?? new Color(0.18f, 0.72f, 1f), duration));
        }

        private void EnsureToastUI()
        {
            if (_toastCanvas != null) return;

            var canvasGo = new GameObject("[ToastCanvas]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            _toastCanvas = canvasGo.GetComponent<Canvas>();
            _toastCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _toastCanvas.sortingOrder = 9999;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panelGo = new GameObject("ToastPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panelGo.transform.SetParent(canvasGo.transform, false);

            _toastPanel = panelGo.GetComponent<RectTransform>();
            _toastPanel.anchorMin = new Vector2(0.5f, 1f);
            _toastPanel.anchorMax = new Vector2(0.5f, 1f);
            _toastPanel.pivot = new Vector2(0.5f, 1f);
            _toastPanel.anchoredPosition = new Vector2(0f, -40f);
            _toastPanel.sizeDelta = new Vector2(600f, 60f);

            var bgImg = panelGo.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.15f, 0.95f);

            _toastGroup = panelGo.GetComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;

            var textGo = new GameObject("ToastText", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panelGo.transform, false);

            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(24f, 8f);
            textRt.offsetMax = new Vector2(-24f, -8f);

            _toastText = textGo.GetComponent<Text>();
            _toastText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _toastText.fontSize = 24;
            _toastText.fontStyle = FontStyle.Bold;
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toastText.color = Color.white;
            _toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private IEnumerator AnimateToast(string message, Color accentColor, float duration)
        {
            _toastText.text = message;
            _toastText.color = accentColor;

            // 动态根据文字宽度调整
            float estimatedWidth = Mathf.Clamp(message.Length * 24f + 80f, 320f, 1000f);
            _toastPanel.sizeDelta = new Vector2(estimatedWidth, 64f);

            float elapsed = 0f;
            Vector2 startPos = new Vector2(0f, 20f);
            Vector2 targetPos = new Vector2(0f, -40f);

            // 滑入
            while (elapsed < 0.2f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.2f);
                _toastGroup.alpha = t;
                _toastPanel.anchoredPosition = Vector2.Lerp(startPos, targetPos, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            _toastGroup.alpha = 1f;
            _toastPanel.anchoredPosition = targetPos;

            // 停留
            yield return new WaitForSecondsRealtime(duration);

            // 淡出
            elapsed = 0f;
            while (elapsed < 0.25f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.25f);
                _toastGroup.alpha = 1f - t;
                yield return null;
            }
            _toastGroup.alpha = 0f;
        }
    }
}
