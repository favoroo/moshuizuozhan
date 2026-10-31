using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// Release API 的附件对象。注意：Gitee 的 assets 只给 name / browser_download_url，
    /// **不给 size**，所以这里的 size 在 Gitee 源上会保持 0（真机实测，见 Docs/OptimizationPlan.md）。
    /// </summary>
    [Serializable]
    internal class ReleaseAssetDto
    {
        public string name = "";
        public string browser_download_url = "";
        public long size = 0;
    }

    /// <summary>
    /// Release API 的响应对象。GitHub 与 Gitee 的字段名一致，多余字段（uploader / author 等嵌套对象）
    /// 由 JsonUtility 直接忽略 —— 这正是替换掉旧正则解析的原因：旧正则跨不过嵌套括号。
    /// </summary>
    [Serializable]
    internal class ReleaseDto
    {
        public string tag_name = "";
        public string name = "";
        public string body = "";
        public string html_url = "";
        public bool draft = false;
        public bool prerelease = false;
        public ReleaseAssetDto[] assets = new ReleaseAssetDto[0];
    }

    /// <summary>
    /// 应用内更新管理器（单例）。
    /// 对标 Godot 项目的 UpdateManager.gd：
    /// 1. 国内 Gitee 优先、GitHub 备选、代理镜像兜底；
    /// 2. 异步检测、真实字节流下载进度采样与安装调起；
    /// 3. 全局 Toast 提示与更新弹窗（UpdateDialog）。
    ///
    /// 进度口径对齐 Godot：总量优先取**下载响应头的 Content-Length**（等价于 Godot 的
    /// HTTPRequest.get_body_size()），再退到 Release 元数据里的 size；两者都拿不到时才显示"未知"，
    /// 绝不用「已下载 / 已下载」凑出一个假的百分比。
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
        public event Action<UpdateInfo> OnUpdateInfoRefined; // 体积等元信息延迟补齐（HEAD 探测）
        public event Action OnNoUpdateFound;
        public event Action<string> OnCheckFailed;
        public event Action<long, long, float, float, float> OnDownloadProgress; // loaded, total, percent, speed, etaSec
        public event Action<string> OnDownloadCompleted;
        public event Action<string> OnDownloadFailed;

        public const string ApkFileName = "moshuizuozhan_update.apk";

        /// <summary>Android 上安装包要走 FileProvider 共享，authority 必须与 AndroidManifest 里的声明一致。</summary>
        public const string FileProviderAuthoritySuffix = ".fileprovider";

        private const string ClientUserAgent = "Moshuizuozhan-UnityClient";

        public bool IsChecking { get; private set; }
        public bool IsDownloading { get; private set; }
        public UpdateInfo PendingUpdate { get; private set; }

        private bool _lastCheckManual;
        private Coroutine _checkCoroutine;
        private Coroutine _downloadCoroutine;
        private UnityWebRequest _downloadRequest;

        // 下载代际号：取消/重启时 +1，让在跑的协程安静收尾（不再发事件、不再碰文件），
        // 从而避免「旧协程收尾时把新下载的半截包删掉」这类竞态。
        private int _downloadGeneration;

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
                    req.SetRequestHeader("User-Agent", ClientUserAgent);
                    req.SetRequestHeader("Accept", "application/vnd.github+json, application/json");

                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                    {
                        jsonResult = req.downloadHandler.text;
                        if (!string.IsNullOrEmpty(jsonResult) && (jsonResult.Contains("\"tag_name\"") || jsonResult.Contains("\"name\"")))
                        {
                            break;
                        }
                        jsonResult = null;
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

            // Gitee 的 API 不给体积：后台补一次 HEAD，拿到 Content-Length 后刷新弹窗上的「大小」。
            // 失败不阻断（下载阶段还有响应头兜底），所以这里静默处理。
            if (info.hasApk && info.fileSize <= 0)
            {
                StartCoroutine(ProbeRemoteSizeAsync(info));
            }
        }

        /// <summary>
        /// 从 Release API JSON 中提取更新元数据。
        /// </summary>
        public static UpdateInfo ParseReleaseJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            ReleaseDto dto;
            try
            {
                dto = JsonUtility.FromJson<ReleaseDto>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] Release JSON 解析失败: {ex.Message}");
                return null;
            }

            if (dto == null) return null;
            if (dto.draft || dto.prerelease) return null;

            var info = new UpdateInfo();
            info.tagName = (dto.tag_name ?? "").Trim();
            info.releaseName = (dto.name ?? "").Trim();
            if (string.IsNullOrEmpty(info.tagName)) info.tagName = info.releaseName;
            info.version = info.tagName.TrimStart('v', 'V');
            info.releaseNotes = dto.body ?? "";

            info.releaseUrl = string.IsNullOrEmpty(dto.html_url)
                ? GameVersion.GetReleasePageUrl(info.tagName)
                : dto.html_url.Trim();

            // 取第一个 .apk 附件；Gitee 不带 size，此时 fileSize 保持 0，由 HEAD 探测 / 响应头补齐。
            string rawApkUrl = "";
            long apkSize = 0;
            if (dto.assets != null)
            {
                foreach (var asset in dto.assets)
                {
                    if (asset == null) continue;
                    string assetName = asset.name ?? "";
                    if (!assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) continue;

                    rawApkUrl = (asset.browser_download_url ?? "").Trim();
                    apkSize = asset.size > 0 ? asset.size : 0;
                    break;
                }
            }

            info.hasApk = !string.IsNullOrEmpty(rawApkUrl);
            info.fileSize = apkSize;
            info.fileSizeText = GameVersion.FormatBytes(apkSize);

            // 候选下载源（顺序与旧实现保持一致：Gitee 直连优先，GitHub 走代理，最后直连兜底）
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
                // 没有 APK 附件：URL 只用于「浏览器下载」逃生通道，
                // 绝不能再拿去当下载任务（旧实现会把发布页 HTML 存成 .apk 并报"完成"）。
                info.downloadUrl = info.releaseUrl;
            }

            return info;
        }

        /// <summary>
        /// 解析响应头里的 Content-Length。取不到或非法一律返回 0（表示"未知"）。
        /// </summary>
        public static long ParseContentLength(string rawHeaderValue)
        {
            if (string.IsNullOrEmpty(rawHeaderValue)) return 0;
            return long.TryParse(rawHeaderValue.Trim(), out long v) && v > 0 ? v : 0;
        }

        /// <summary>
        /// 决定下载总字节数：响应头 &gt; Release 元数据 &gt; 0（未知）。
        /// 返回 0 表示"确实不知道"，此时进度条必须走未知态而不是编一个百分比出来。
        /// </summary>
        public static long ResolveDownloadTotal(long headerContentLength, long metadataSize)
        {
            if (headerContentLength > 0) return headerContentLength;
            if (metadataSize > 0) return metadataSize;
            return 0;
        }

        /// <summary>
        /// 下载百分比。总量未知时返回 0（UI 显示"连接中"），已知时按真实比例给 0~100。
        /// 旧实现把总量退化成"已下载量"，于是百分比恒等于 99 —— 这就是"进度一直是 99%"的成因。
        /// </summary>
        public static float ComputeDownloadPercent(long loaded, long total)
        {
            if (total <= 0 || loaded <= 0) return 0f;
            double percent = (double)loaded / total * 100.0;
            return Mathf.Clamp((float)percent, 0f, 100f);
        }

        /// <summary>
        /// 是否是 APK 的 ZIP 魔数（"PK\x03\x04"）。用来识别"下载到的其实是网页/错误页"。
        /// </summary>
        public static bool LooksLikeApkHeader(byte[] head)
        {
            if (head == null || head.Length < 4) return false;
            return head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04;
        }

        /// <summary>
        /// 校验落盘文件确实是 APK（存在、非空、ZIP 魔数）。
        /// </summary>
        public static bool IsApkPayload(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                if (new FileInfo(path).Length <= 0) return false;

                using (var fs = File.OpenRead(path))
                {
                    var head = new byte[4];
                    int read = fs.Read(head, 0, head.Length);
                    return read == head.Length && LooksLikeApkHeader(head);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] 校验安装包失败: {ex.Message}");
                return false;
            }
        }

        public string GetApkSavePath()
        {
            return Path.Combine(Application.persistentDataPath, ApkFileName);
        }

        /// <summary>
        /// 落盘的包是否已经可以直接安装（用于"上次已下载完"的场景）。
        /// </summary>
        public bool HasReadyApk()
        {
            return IsApkPayload(GetApkSavePath());
        }

        /// <summary>
        /// 一次 HEAD 探测：跟随 302 后读响应头的 Content-Length（Gitee 的 302 链后面才给这个头）。
        /// 探测失败一律回调 0，调用方按"未知"处理，绝不阻断下载。
        /// </summary>
        private static IEnumerator ProbeSizeViaHead(string url, Action<long> onResolved)
        {
            long resolved = 0;

            using (var req = UnityWebRequest.Head(url))
            {
                req.timeout = 8;
                req.redirectLimit = 10;
                req.SetRequestHeader("User-Agent", ClientUserAgent);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                {
                    resolved = ParseContentLength(req.GetResponseHeader("Content-Length"));
                }
            }

            onResolved?.Invoke(resolved);
        }

        /// <summary>
        /// 后台补探测安装包体积（HEAD 请求 → 跟随 302 后读 Content-Length）。
        /// Gitee 的 Release API 不返回 size，这是弹窗能显示大小、进度能有真分母的关键一步。
        /// </summary>
        private IEnumerator ProbeRemoteSizeAsync(UpdateInfo info)
        {
            if (info == null || !info.hasApk) yield break;
            if (info.candidateDownloadUrls == null || info.candidateDownloadUrls.Count == 0) yield break;

            long resolved = 0;
            yield return ProbeSizeViaHead(info.candidateDownloadUrls[0], size => resolved = size);

            // 期间用户可能已经重新检查过更新：旧探测结果不许覆盖新一轮的状态
            if (resolved <= 0 || PendingUpdate != info) yield break;

            info.fileSize = resolved;
            info.fileSizeText = GameVersion.FormatBytes(resolved);
            Debug.Log($"[UpdateManager] HEAD 探测到安装包体积: {resolved} 字节 ({info.fileSizeText})");
            OnUpdateInfoRefined?.Invoke(info);
        }

        /// <summary>
        /// 开始下载 APK 附件。
        /// </summary>
        public void StartDownload(UpdateInfo info)
        {
            if (IsDownloading) return;
            if (info == null) return;

            if (_downloadCoroutine != null) StopCoroutine(_downloadCoroutine);
            int generation = ++_downloadGeneration;
            _downloadCoroutine = StartCoroutine(DoDownloadAsync(info, generation));
        }

        private IEnumerator DoDownloadAsync(UpdateInfo info, int generation)
        {
            bool IsStale() => generation != _downloadGeneration;

            IsDownloading = true;

            if (!info.hasApk)
            {
                IsDownloading = false;
                OnDownloadFailed?.Invoke("该版本未附带安装包附件，请使用「浏览器下载」");
                yield break;
            }

            var candidates = new List<string>();
            if (info.candidateDownloadUrls != null) candidates.AddRange(info.candidateDownloadUrls);
            if (candidates.Count == 0 && !string.IsNullOrEmpty(info.downloadUrl)) candidates.Add(info.downloadUrl);
            if (candidates.Count == 0)
            {
                IsDownloading = false;
                OnDownloadFailed?.Invoke("没有可用的下载地址，请使用「浏览器下载」");
                yield break;
            }

            string savePath = GetApkSavePath();
            bool succeeded = false;
            string lastErr = "";

            // 元数据没有体积、检测期那次 HEAD 也没成功时，下载前再补一次 HEAD：
            // 有了真分母，进度条从第一帧起就是真百分比（哪怕下载中途取不到响应头也不至于退化成"未知"）
            if (info.fileSize <= 0)
            {
                long probed = 0;
                yield return ProbeSizeViaHead(candidates[0], size => probed = size);
                if (IsStale()) yield break;

                if (probed > 0)
                {
                    info.fileSize = probed;
                    info.fileSizeText = GameVersion.FormatBytes(probed);
                    Debug.Log($"[UpdateManager] 下载前 HEAD 探测到安装包体积: {probed} 字节 ({info.fileSizeText})");
                    OnUpdateInfoRefined?.Invoke(info);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsStale()) yield break;

                string url = candidates[i];
                Debug.Log($"[UpdateManager] 尝试下载候选源 {i + 1}/{candidates.Count}: {url}");

                TryDeleteFile(savePath);

                using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
                {
                    _downloadRequest = req;
                    req.downloadHandler = new DownloadHandlerFile(savePath);
                    req.timeout = 300;
                    req.redirectLimit = 10;
                    req.SetRequestHeader("User-Agent", ClientUserAgent);
                    req.SetRequestHeader("Accept", "application/octet-stream, */*");

                    var op = req.SendWebRequest();

                    long total = info.fileSize > 0 ? info.fileSize : 0;
                    long headerTotal = 0;
                    long lastSampleBytes = 0;
                    float lastSampleTime = Time.realtimeSinceStartup;
                    float smoothSpeed = 0f;

                    while (!op.isDone)
                    {
                        if (IsStale()) yield break; // 被取消/换人：不碰共享状态、不发事件，交给 using 释放请求

                        long downloaded = (long)req.downloadedBytes;

                        // 响应头到达后 Content-Length 才是权威总量（元数据缺失时全靠它）
                        if (headerTotal <= 0)
                        {
                            headerTotal = ParseContentLength(req.GetResponseHeader("Content-Length"));
                        }
                        total = ResolveDownloadTotal(headerTotal, info.fileSize);

                        float now = Time.realtimeSinceStartup;
                        float dt = now - lastSampleTime;
                        if (dt >= 0.25f && downloaded >= lastSampleBytes)
                        {
                            float instantSpeed = (downloaded - lastSampleBytes) / dt;
                            smoothSpeed = smoothSpeed > 0f ? (smoothSpeed * 0.65f + instantSpeed * 0.35f) : instantSpeed;
                            lastSampleTime = now;
                            lastSampleBytes = downloaded;
                        }

                        float percent = ComputeDownloadPercent(downloaded, total);
                        float etaSec = (smoothSpeed > 0f && total > downloaded) ? (total - downloaded) / smoothSpeed : 0f;

                        OnDownloadProgress?.Invoke(downloaded, total, percent, smoothSpeed, etaSec);
                        yield return null;
                    }

                    if (IsStale()) yield break;
                    _downloadRequest = null;

                    if (req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300)
                    {
                        string reject = ValidateDownloadedApk(savePath, info.fileSize, total);
                        if (reject == null)
                        {
                            succeeded = true;
                            long finalSize = new FileInfo(savePath).Length;
                            Debug.Log($"[UpdateManager] 下载完成并通过校验: {finalSize} 字节 -> {savePath}");

                            // 先落状态再通知：否则 Hide() 会看到 IsDownloading==true 而误取消一次已完成的下载
                            IsDownloading = false;
                            _downloadCoroutine = null;

                            OnDownloadProgress?.Invoke(finalSize, finalSize, 100f, smoothSpeed, 0f);
                            OnDownloadCompleted?.Invoke(savePath);
                            yield break;
                        }

                        lastErr = reject;
                        Debug.LogWarning($"[UpdateManager] 候选源 {i + 1} 下载内容校验失败: {reject}");
                    }
                    else
                    {
                        lastErr = $"HTTP {req.responseCode}: {req.error}";
                        Debug.LogWarning($"[UpdateManager] 候选源 {i + 1} 失败: {lastErr}");
                    }
                }

                TryDeleteFile(savePath);
            }

            if (IsStale()) yield break;

            IsDownloading = false;
            _downloadCoroutine = null;

            if (!succeeded)
            {
                OnDownloadFailed?.Invoke($"全部候选下载源均失败 ({lastErr})");
            }
        }

        /// <summary>
        /// 下载后校验：能挡住"把发布页 HTML 存成 .apk"和"传输被截断"两类会让安装静默失败的问题。
        /// 返回 null 表示通过，否则返回失败原因。
        /// </summary>
        public static string ValidateDownloadedApk(string path, long expectedSize, long resolvedTotal)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "文件未生成";

                long length = new FileInfo(path).Length;
                if (length <= 0) return "文件为空";

                // 优先用本次响应头解析出的总量（它是这条连接实际要传的字节数），再退到 Release 元数据。
                // 两者都是权威值：不相等即为传输被截断。
                long expect = resolvedTotal > 0 ? resolvedTotal : expectedSize;
                if (expect > 0 && length != expect)
                {
                    return $"体积不符（实得 {length} 字节，期望 {expect} 字节）";
                }

                if (!IsApkPayload(path)) return "内容不是有效的 APK（疑似下载到了网页或错误页面）";

                return null;
            }
            catch (Exception ex)
            {
                return $"校验异常: {ex.Message}";
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] 清理临时安装包失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 取消正在进行的下载，并清掉半截安装包（否则下次可能被当成"已下载完"）。
        /// </summary>
        public void CancelDownload()
        {
            if (!IsDownloading) return;

            IsDownloading = false;
            _downloadGeneration++; // 在跑的协程就此失效：只安静收尾，不再发事件、不再碰文件

            if (_downloadRequest != null)
            {
                try
                {
                    _downloadRequest.Abort();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UpdateManager] 取消下载时终止请求异常: {ex.Message}");
                }
            }

            TryDeleteFile(GetApkSavePath());
        }

        /// <summary>
        /// 调起安装下载的 APK。
        /// </summary>
        public bool InstallApk(string filePath)
        {
            return InstallApk(filePath, out _);
        }

        /// <summary>
        /// 调起安装下载的 APK。返回 false 时 <paramref name="reason"/> 给出可直接展示给玩家的原因
        /// （旧实现在异常分支里 return true，等于对 UI 撒谎）。
        /// </summary>
        public bool InstallApk(string filePath, out string reason)
        {
            reason = "";

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                reason = "安装包不存在";
                Debug.LogWarning($"[UpdateManager] 安装失败：{reason} ({filePath})");
                return false;
            }

            if (!IsApkPayload(filePath))
            {
                reason = "安装包内容无效，请重新下载或用「浏览器下载」";
                Debug.LogWarning($"[UpdateManager] 安装失败：{reason} ({filePath})");
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            return InstallApkOnAndroid(filePath, out reason);
#elif UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(filePath);
            reason = "编辑器环境不支持安装，已定位到安装包";
            return true;
#else
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Application.OpenURL("file://" + dir);
            reason = "已打开安装包所在文件夹";
            ShowToast("APK 已下载完毕，已打开所在文件夹", new Color(0.2f, 0.85f, 0.45f));
            return true;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool InstallApkOnAndroid(string filePath, out string reason)
        {
            reason = "";
            AndroidJavaObject currentActivity = null;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                }

                if (currentActivity == null)
                {
                    reason = "无法获取当前 Activity";
                    Debug.LogWarning($"[UpdateManager] 安装失败：{reason}");
                    return false;
                }

                // Android 8.0（API 26）起安装 APK 需要用户显式授予「安装未知应用」权限，
                // 否则 startActivity 会被系统静默丢弃（表现就是"点了没反应"）。
                int sdkInt = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                if (sdkInt >= 26 && !CanRequestPackageInstalls(currentActivity))
                {
                    reason = "请先允许「安装未知应用」，再点一次安装";
                    Debug.LogWarning($"[UpdateManager] 安装受阻：{reason}");
                    OpenUnknownSourcesSettings(currentActivity);
                    return false;
                }

                using (var fileObj = new AndroidJavaObject("java.io.File", filePath))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    AndroidJavaObject uri = BuildContentUri(currentActivity, fileObj);
                    if (uri == null)
                    {
                        reason = "安装包共享失败，请改用「浏览器下载」";
                        Debug.LogWarning($"[UpdateManager] 安装失败：{reason}");
                        return false;
                    }

                    using (uri)
                    {
                        intent.Call<AndroidJavaObject>("setAction", "android.intent.action.VIEW");
                        intent.Call<AndroidJavaObject>("setDataAndType", uri, "application/vnd.android.package-archive");
                        intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                        intent.Call<AndroidJavaObject>("addFlags", 0x00000001); // FLAG_GRANT_READ_URI_PERMISSION

                        // 个别 ROM 的包安装器只认 ACTION_INSTALL_PACKAGE，两个动作都试
                        if (!TryStartActivity(currentActivity, intent))
                        {
                            intent.Call<AndroidJavaObject>("setAction", "android.intent.action.INSTALL_PACKAGE");
                            if (!TryStartActivity(currentActivity, intent))
                            {
                                reason = "系统没有可用的安装器，请改用「浏览器下载」";
                                Debug.LogWarning($"[UpdateManager] 安装失败：{reason}");
                                return false;
                            }
                        }
                    }
                }

                Debug.Log($"[UpdateManager] 已调起系统安装器: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                reason = $"调起系统安装器异常: {ex.Message}";
                Debug.LogWarning($"[UpdateManager] 安装失败：{reason}");
                return false;
            }
            finally
            {
                try { currentActivity?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 生成 content:// URI。authority 必须与 AndroidManifest 里 FileProvider 的声明一致
        /// （Assets/Plugins/Android/AndroidManifest.xml，菜单 P1/构建/12 可自检）。
        /// </summary>
        private static AndroidJavaObject BuildContentUri(AndroidJavaObject activity, AndroidJavaObject fileObj)
        {
            try
            {
                string authority = activity.Call<string>("getPackageName") + FileProviderAuthoritySuffix;
                using (var fileProvider = new AndroidJavaClass("androidx.core.content.FileProvider"))
                {
                    return fileProvider.CallStatic<AndroidJavaObject>("getUriForFile", activity, authority, fileObj);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] FileProvider 生成 content URI 失败" +
                                 $"（请确认 AndroidManifest 已声明 provider 与 res/xml 路径表）: {ex.Message}");
                return null;
            }
        }

        private static bool CanRequestPackageInstalls(AndroidJavaObject activity)
        {
            try
            {
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    return pm.Call<bool>("canRequestPackageInstalls");
                }
            }
            catch (Exception ex)
            {
                // 查不到就别拦着，直接尝试调起
                Debug.LogWarning($"[UpdateManager] 查询「安装未知应用」权限失败，按已授权处理: {ex.Message}");
                return true;
            }
        }

        private static void OpenUnknownSourcesSettings(AndroidJavaObject activity)
        {
            try
            {
                string packageName = activity.Call<string>("getPackageName");
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.MANAGE_UNKNOWN_APP_SOURCES"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                {
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName))
                    {
                        intent.Call<AndroidJavaObject>("setData", uri);
                        intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                        TryStartActivity(activity, intent);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] 打开「安装未知应用」设置页失败: {ex.Message}");
            }
        }

        private static bool TryStartActivity(AndroidJavaObject activity, AndroidJavaObject intent)
        {
            try
            {
                activity.Call("startActivity", intent);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpdateManager] startActivity 失败: {ex.Message}");
                return false;
            }
        }
#endif

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
