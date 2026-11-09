using System;
using System.IO;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 应用内更新弹窗（P14 几何大胆风 Geometric Bold 重构；2026-11 圆角化）：
    /// - 840×640 圆角黑白面板（RoundedRect 自适应半径，6px 粗墨边）；
    /// - 纯色圆角标题条；
    /// - Red 关键 CTA 按钮（立即更新），圆角白底墨字次级按钮（稍后再说），蓝底圆角按钮（浏览器下载）；
    /// - 正文、版本号与发布说明字号 ≥22（移动端 ≥26，走 UITypography.FontSnapped）；
    /// - 去除网点、半调、动作线与硬阴影。
    /// </summary>
    public class UpdateDialog : MonoBehaviour
    {
        private static UpdateDialog _instance;
        public static UpdateDialog Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[UpdateDialog]");
                    _instance = go.AddComponent<UpdateDialog>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private Canvas _dialogCanvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private GameObject _dialogRoot;
        // 面板外层容器：只承载「响应式缩放」（ApplyResponsivePanelScale），
        // 入场缩放动画留给面板自身的 UISpringMotion，两者不再抢同一个 localScale。
        private RectTransform _panelContainer;
        private UISpringMotion _panelMotion;
        private TextMeshProUGUI _verText;
        private TextMeshProUGUI _sizeText;
        private TextMeshProUGUI _notesText;
        private ScrollRect _scrollRect;

        // 进度控件
        private GameObject _progressBox;
        private Image _progressBarFill;
        private RectTransform _progressFillArea;
        private UIInkShapeGraphic _progressBarHead;
        private TextMeshProUGUI _progressPercentText;
        private TextMeshProUGUI _progressSpeedText;

        // 进度条动画状态：下载回调粒度很粗（一次可能跳好几个百分点），
        // 直接写 fillAmount 会一顿一顿；按帧指数逼近目标值，条子才会连续滑动。
        private float _targetProgress;
        private float _displayedProgress;
        private bool _progressRunning;

        /// <summary>进度槽底色：深灰，才能和纸白面板、黄色填充都拉开层次。</summary>
        private static readonly Color ProgressTrackColor = ComicStyle.Darken(ComicStyle.PaperAlt, 0.74f);

        // 按钮
        private Button _actionBtn;
        private TextMeshProUGUI _actionBtnText;
        private Button _browserBtn;
        private TextMeshProUGUI _browserBtnText;
        private Button _cancelBtn;
        private TextMeshProUGUI _cancelBtnText;

        private UpdateInfo _currentInfo;
        private bool _isCompleted;
        private string _completedApkPath;

        private UIThemeSO _theme;
        private UIThemeSO Theme
        {
            get
            {
                if (_theme == null)
                {
                    RuntimeAssetLibrary lib = RuntimeAssetLibrary.Load();
                    if (lib != null && lib.uiTheme != null)
                    {
                        _theme = lib.uiTheme;
                    }
#if UNITY_EDITOR
                    if (_theme == null)
                    {
                        _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>("Assets/_Project/ScriptableObjects/UITheme.asset");
                    }
#endif
                    _theme = UIThemeSO.Resolve(_theme);
                }
                return _theme;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureUI();

            UpdateManager.Instance.OnUpdateInfoRefined += HandleUpdateInfoRefined;
            UpdateManager.Instance.OnDownloadProgress += HandleDownloadProgress;
            UpdateManager.Instance.OnDownloadCompleted += HandleDownloadCompleted;
            UpdateManager.Instance.OnDownloadFailed += HandleDownloadFailed;
        }

        private void OnDestroy()
        {
            if (UpdateManager.Instance != null)
            {
                UpdateManager.Instance.OnUpdateInfoRefined -= HandleUpdateInfoRefined;
                UpdateManager.Instance.OnDownloadProgress -= HandleDownloadProgress;
                UpdateManager.Instance.OnDownloadCompleted -= HandleDownloadCompleted;
                UpdateManager.Instance.OnDownloadFailed -= HandleDownloadFailed;
            }
        }

        private void Update()
        {
            if (_progressBarFill == null) return;

            if (!Mathf.Approximately(_displayedProgress, _targetProgress))
            {
                // 用 unscaled 时间：更新弹窗可能在 Time.timeScale=0（暂停/结算）时出现。
                float k = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
                _displayedProgress = Mathf.Lerp(_displayedProgress, _targetProgress, k);
                if (Mathf.Abs(_targetProgress - _displayedProgress) < 0.0015f)
                {
                    _displayedProgress = _targetProgress;
                }

                _progressBarFill.fillAmount = _displayedProgress;
            }

            UpdateProgressHead();
        }

        /// <summary>
        /// 把目标进度交给动画层；immediate 用于「开始下载 / 已就绪 / 下载完成」这类必须立刻到位的切换。
        /// </summary>
        private void SetProgressTarget(float value, bool immediate = false)
        {
            _targetProgress = Mathf.Clamp01(value);
            if (immediate) _displayedProgress = _targetProgress;

            if (_progressBarFill != null) _progressBarFill.fillAmount = _displayedProgress;
            UpdateProgressHead();
        }

        /// <summary>
        /// 黄色填充条尽头的小方块：跟着动画进度走，让「下载中」有明确的运动参照。
        /// 完成 / 未开始时隐藏（满格时它只会压在右边框上）。
        /// </summary>
        private void UpdateProgressHead()
        {
            if (_progressBarHead == null) return;

            bool visible = _progressRunning && _progressBox != null && _progressBox.activeSelf
                && _displayedProgress > 0.002f && _displayedProgress < 0.998f;
            if (_progressBarHead.gameObject.activeSelf != visible)
            {
                _progressBarHead.gameObject.SetActive(visible);
            }
            if (!visible) return;

            float width = _progressFillArea != null ? _progressFillArea.rect.width : 734f;
            float half = _progressBarHead.rectTransform.rect.width * 0.5f;
            float x = Mathf.Clamp(width * _displayedProgress, half, width - half);
            _progressBarHead.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }

        /// <summary>
        /// 体积等元信息是延迟补齐的：Gitee 的 Release API 不给 asset size，
        /// UpdateManager 会另发一次 HEAD 探测，拿到后回来刷这行「大小」。
        /// </summary>
        private void HandleUpdateInfoRefined(UpdateInfo info)
        {
            if (info == null || !ReferenceEquals(info, _currentInfo)) return;
            RefreshSizeText(info);

            // 体积补齐后发现缓存包与远端大小不符（同版本重传 / 上次的坏包）：
            // 撤销「已就绪」，让主按钮回到下载态，别把一个不完整的包送进安装器。
            if (_isCompleted && !UpdateManager.Instance.HasReadyApk(info))
            {
                _isCompleted = false;
                _completedApkPath = "";
                _progressRunning = false;
                if (_progressBox != null) _progressBox.SetActive(false);
                SetProgressTarget(0f, immediate: true);
                if (_actionBtn != null) _actionBtn.interactable = info.hasApk && UpdateManager.InAppInstallSupported;
                if (_actionBtnText != null)
                    _actionBtnText.text = UpdateManager.InAppInstallSupported
                        ? (info.hasApk ? "立 即 更 新" : "无内置安装包")
                        : "前 往 下 载";
            }
        }

        private void RefreshSizeText(UpdateInfo info)
        {
            if (_sizeText == null) return;
            string text = info != null ? info.fileSizeText : "";
            _sizeText.text = $"大小: {(string.IsNullOrEmpty(text) ? "未知" : text)}";
        }

        public void PopupUpdate(UpdateInfo info)
        {
            _currentInfo = info;
            _isCompleted = false;
            _completedApkPath = "";
            _progressRunning = false;

            EnsureUI();
            ApplyResponsivePanelScale();
            if (_verText != null) _verText.text = $"新版本: {info.tagName}";
            RefreshSizeText(info);
            if (_notesText != null)
            {
                _notesText.text = UpdateNotesMarkdown.ToTmp(info.releaseNotes, _notesText.fontSize);
            }

            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;

            // 没有 APK 附件时应用内下载无从谈起，只剩「浏览器下载」这条逃生通道
            bool canInstallInApp = info.hasApk && UpdateManager.InAppInstallSupported;
            bool canOpenDownload = canInstallInApp
                || !string.IsNullOrEmpty(info.releaseUrl)
                || !string.IsNullOrEmpty(info.downloadUrl);
            if (_actionBtn != null) _actionBtn.interactable = canOpenDownload;
            if (_actionBtnText != null)
                _actionBtnText.text = canInstallInApp ? "立 即 更 新" : "前 往 下 载";

            // 上一次已经下好、且缓存包确实属于「这个待安装版本」：直接给安装入口，不必重下几十 MB。
            // 旧实现只问「有没有 APK 文件」，会把上个版本的残留包当成本次更新（点安装必然失败）。
            if (canInstallInApp && UpdateManager.Instance.HasReadyApk(info))
            {
                _isCompleted = true;
                _completedApkPath = UpdateManager.Instance.GetApkSavePath(info);

                if (_progressBox != null) _progressBox.SetActive(true);
                SetProgressTarget(1f, immediate: true);
                if (_progressPercentText != null) _progressPercentText.text = "安装包已就绪";
                if (_progressSpeedText != null) _progressSpeedText.text = "点「立即安装」开始安装";
                if (_actionBtnText != null) _actionBtnText.text = "立 即 安 装";
            }
            else
            {
                SetProgressTarget(0f, immediate: true);
                if (_progressBox != null) _progressBox.SetActive(false);
            }

            // 弹窗原本是「静态贴上去」的：面板上的 UISpringMotion 建了却从没播过（真 bug）。
            // 这里在真正弹出时补一次入场（0.22s Back-Out + alpha）。已在显示中的弹窗再刷新
            // （体积补齐等）不重播，否则会二次弹出闪烁。
            bool wasVisible = _dialogRoot.activeSelf;
            _dialogRoot.SetActive(true);
            if (!wasVisible && _panelMotion != null)
            {
                _panelMotion.PlayIn();
            }
        }

        public void Hide()
        {
            if (UpdateManager.Instance != null && UpdateManager.Instance.IsDownloading)
            {
                UpdateManager.Instance.CancelDownload();
            }
            if (_dialogRoot != null) _dialogRoot.SetActive(false);
        }

        private void ApplyResponsivePanelScale()
        {
            // 缩放打到外层容器上（不是面板本身）：面板身上的 UISpringMotion 独占 localScale，
            // 否则入场动画播完会把这里的响应式缩放覆盖掉。
            if (_panelContainer == null) return;

            float canvasHeight = 1080f;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 680f;

            if (availableHeight < targetHeight && availableHeight > 100f)
            {
                float s = Mathf.Clamp(availableHeight / targetHeight, 0.5f, 1f);
                _panelContainer.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                _panelContainer.localScale = Vector3.one;
            }
        }

        private void HandleActionClick()
        {
            if (_isCompleted && !string.IsNullOrEmpty(_completedApkPath))
            {
                TryInstall(_completedApkPath);
                return;
            }

            if (_currentInfo == null) return;

            // 未申请安装权限（TapTap 隐私合规）：主按钮直接打开下载页。
            if (!UpdateManager.InAppInstallSupported)
            {
                UpdateManager.Instance.OpenReleasePage(_currentInfo);
                return;
            }

            if (!_currentInfo.hasApk)
            {
                _progressRunning = false;
                if (_progressBox != null) _progressBox.SetActive(true);
                if (_progressPercentText != null) _progressPercentText.text = "该版本未附带安装包";
                if (_progressSpeedText != null) _progressSpeedText.text = "请点「浏览器下载」";
                return;
            }

            _progressRunning = true;
            if (_progressBox != null) _progressBox.SetActive(true);
            SetProgressTarget(0f, immediate: true);
            if (_progressPercentText != null) _progressPercentText.text = "准备下载...";
            if (_progressSpeedText != null) _progressSpeedText.text = "";
            if (_actionBtn != null) _actionBtn.interactable = false;
            if (_actionBtnText != null) _actionBtnText.text = "下 载 中...";

            UpdateManager.Instance.StartDownload(_currentInfo);
        }

        /// <summary>
        /// 调起安装并把失败原因如实显示出来（失败时旧实现是一声不响，玩家只看到"点了没反应"）。
        /// </summary>
        private void TryInstall(string filePath, bool verifyVersion = true)
        {
            // 传入当前弹窗的版本号：安装器启动前再核对一次，避免旧包被误装。
            // 本次刚下载完的包已经过传输校验，清单写入失败时也允许直接安装。
            string expectedVersion = verifyVersion ? _currentInfo?.version : null;
            if (UpdateManager.Instance.InstallApk(filePath, expectedVersion, out string reason))
            {
                if (_progressSpeedText != null) _progressSpeedText.text = "已调起系统安装器";
                return;
            }

            if (_progressSpeedText != null) _progressSpeedText.text = reason;
            UpdateManager.Instance.ShowToast($"安装未完成：{reason}", new Color(1f, 0.35f, 0.35f));
        }

        private void HandleBrowserClick()
        {
            string url = _currentInfo != null && !string.IsNullOrEmpty(_currentInfo.releaseUrl)
                ? _currentInfo.releaseUrl
                : GameVersion.GetReleasePageUrl();
            Application.OpenURL(url);
        }

        private void HandleDownloadProgress(long loaded, long total, float percent, float speed, float etaSec)
        {
            if (_progressBox == null || !_progressBox.activeSelf) return;
            _progressRunning = true;

            SetProgressTarget(percent / 100f);

            string loadedStr = GameVersion.FormatBytes(loaded);
            string totalStr = GameVersion.FormatBytes(total);

            if (_progressPercentText != null)
            {
                _progressPercentText.text = total > 0
                    ? $"{percent:F0}% ({loadedStr} / {totalStr})"
                    // 服务端没给 Content-Length 时如实报"已下载多少"，绝不编一个百分比
                    : $"连接中… 已下载 {loadedStr}";
            }

            if (speed > 0f)
            {
                string speedStr = $"{GameVersion.FormatBytes((long)speed)}/s";
                string etaStr = etaSec > 0f ? $" · 剩余 {etaSec:F0}秒" : "";
                if (_progressSpeedText != null) _progressSpeedText.text = speedStr + etaStr;
            }
            else
            {
                if (_progressSpeedText != null) _progressSpeedText.text = "连接中...";
            }
        }

        private void HandleDownloadCompleted(string filePath)
        {
            _isCompleted = true;
            _completedApkPath = filePath;
            _progressRunning = false;

            if (_progressBox != null) _progressBox.SetActive(true);
            SetProgressTarget(1f, immediate: true);
            if (_progressPercentText != null) _progressPercentText.text = "100% (下载完成)";
            if (_progressSpeedText != null) _progressSpeedText.text = "准备安装";

            if (_actionBtn != null) _actionBtn.interactable = true;
            if (_actionBtnText != null) _actionBtnText.text = "立 即 安 装";

            TryInstall(filePath, verifyVersion: false);
        }

        private void HandleDownloadFailed(string error)
        {
            _progressRunning = false;
            if (_progressBox != null)
            {
                _progressBox.SetActive(true);
                if (_progressPercentText != null) _progressPercentText.text = "下载中断";
                if (_progressSpeedText != null) _progressSpeedText.text = $"{error} · 可尝试「浏览器下载」";
            }

            if (_actionBtn != null)
            {
                _actionBtn.interactable = true;
            }
            if (_actionBtnText != null)
            {
                _actionBtnText.text = "重 试 下 载";
            }

            UpdateManager.Instance.ShowToast($"下载失败: {error}", new Color(1f, 0.35f, 0.35f));
        }

        private void EnsureUI()
        {
            if (_dialogRoot != null) return;

            var canvasGo = new GameObject("[UpdateDialogCanvas]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            _dialogCanvas = canvasGo.GetComponent<Canvas>();
            _dialogCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _dialogCanvas.sortingOrder = 9998;

            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = canvasGo.GetComponent<GraphicRaycaster>();

            _dialogRoot = new GameObject("UpdateDialogRoot", typeof(RectTransform));
            _dialogRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = _dialogRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            // 不挂 SafeAreaFitter：根节点必须保持物理全屏，压暗蒙层才能盖住刘海/挖孔区；
            // 挂上后蒙层会跟着安全区收缩，刘海区会留一条没被压暗的亮竖条。
            // 弹窗面板本身是屏幕居中的 840×640，尺寸远小于安全区，不必再单独收缩。

            // 1. 全屏黑色暗底半透明遮罩 (墨色 70%，无网点)
            var overlayGo = new GameObject("DimOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlayGo.transform.SetParent(_dialogRoot.transform, false);
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlayImg = overlayGo.GetComponent<Image>();
            overlayImg.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.70f);
            overlayImg.raycastTarget = true;

            // 2. 居中主面板 (840 × 640，纸白直角面板 + 6px 墨边 + 弹窗硬影 8,-9)
            //    外面套一层只负责定位/响应式缩放的容器：ApplyResponsivePanelScale 与
            //    UISpringMotion 都会写 localScale，压在同一个节点上会让入场结束后把矮屏的
            //    响应式缩放覆盖回 1（面板溢出屏幕）。容器承载缩放，面板只跑入场动画。
            var panelContainerGo = new GameObject("PanelContainer", typeof(RectTransform));
            panelContainerGo.transform.SetParent(_dialogRoot.transform, false);
            _panelContainer = panelContainerGo.GetComponent<RectTransform>();
            _panelContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _panelContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _panelContainer.pivot = new Vector2(0.5f, 0.5f);
            _panelContainer.anchoredPosition = Vector2.zero;
            _panelContainer.sizeDelta = new Vector2(840f, 640f);

            var panelGo = new GameObject("MainPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_panelContainer, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(840f, 640f);

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.RoundedRect; // 圆角化：弹窗面板自适应圆角，硬影同形状跟随
            panelShape.BorderWidth = ComicStyle.BorderHeavy;
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.Halftone = false;
            panelShape.GlossAlpha = 0f;
            panelShape.DrawGloss = false;
            panelShape.raycastTarget = true;
            Theme.ApplyHardShadow(panelShape, UIHardShadowLevel.Dialog); // t-1 弹窗硬影 (8,-9)

            _panelMotion = panelGo.GetComponent<UISpringMotion>();
            _panelMotion.SetTheme(Theme);

            // 3. 顶部直角标题块
            var titleBoxGo = new GameObject("TitleBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            titleBoxGo.transform.SetParent(panelGo.transform, false);
            var titleBoxRt = titleBoxGo.GetComponent<RectTransform>();
            titleBoxRt.anchorMin = new Vector2(0.5f, 1f);
            titleBoxRt.anchorMax = new Vector2(0.5f, 1f);
            titleBoxRt.anchoredPosition = new Vector2(0f, -48f);
            titleBoxRt.sizeDelta = new Vector2(420f, 64f);

            var titleBoxShape = titleBoxGo.GetComponent<UIInkShapeGraphic>();
            titleBoxShape.Shape = UIInkShape.RoundedRect; // 圆角化：标题块自适应圆角
            titleBoxShape.BorderWidth = 0f;
            titleBoxShape.color = ComicStyle.Ink;
            titleBoxShape.HardShadow = false;
            titleBoxShape.Halftone = false;

            var titleText = CreateTextMesh("TitleText", titleBoxGo.transform, "发 现 新 版 本", UITypography.FontSnapped(32f), ComicStyle.Paper, isDisplay: true);
            var tRt = titleText.rectTransform;
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // 4. 版本与体积元信息 (字号 ≥22，移动端 ≥26)
            var metaGo = new GameObject("MetaRow", typeof(RectTransform));
            metaGo.transform.SetParent(panelGo.transform, false);
            var metaRt = metaGo.GetComponent<RectTransform>();
            metaRt.anchorMin = new Vector2(0.5f, 1f);
            metaRt.anchorMax = new Vector2(0.5f, 1f);
            metaRt.anchoredPosition = new Vector2(0f, -104f);
            metaRt.sizeDelta = new Vector2(740f, 34f);

            float metaFontSize = UITypography.FontSnapped(22f); // 桌面 22，移动端 28

            _verText = CreateTextMesh("VersionText", metaGo.transform, "新版本: v0.0.0", metaFontSize, ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.Left);
            _verText.fontStyle = FontStyles.Bold;
            var verRt = _verText.rectTransform;
            verRt.anchorMin = new Vector2(0f, 0f);
            verRt.anchorMax = new Vector2(0.5f, 1f);
            verRt.offsetMin = Vector2.zero;
            verRt.offsetMax = Vector2.zero;

            _sizeText = CreateTextMesh("SizeText", metaGo.transform, "大小: -- MB", metaFontSize, ComicStyle.WithAlpha(ComicStyle.Ink, 0.70f), isDisplay: false, TextAlignmentOptions.Right);
            _sizeText.fontStyle = FontStyles.Bold;
            var sizeRt = _sizeText.rectTransform;
            sizeRt.anchorMin = new Vector2(0.5f, 0f);
            sizeRt.anchorMax = new Vector2(1f, 1f);
            sizeRt.offsetMin = Vector2.zero;
            sizeRt.offsetMax = Vector2.zero;

            // 5. 更新说明滚动视口 (发布说明正文字号 ≥22，移动端 ≥26)
            var scrollGo = new GameObject("NotesScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(ScrollRect));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.anchoredPosition = new Vector2(0f, 26f);
            scrollRt.sizeDelta = new Vector2(740f, 230f);

            var scrollShape = scrollGo.GetComponent<UIInkShapeGraphic>();
            scrollShape.Shape = UIInkShape.RoundedRect; // 圆角化：更新日志滚动框
            scrollShape.BorderWidth = ComicStyle.BorderThin;
            scrollShape.BorderColor = ComicStyle.Ink;
            scrollShape.color = ComicStyle.PaperAlt;
            scrollShape.HardShadow = false;
            scrollShape.Halftone = false;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(16f, 14f);
            viewportRt.offsetMax = new Vector2(-16f, -14f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            _notesText = CreateTextMesh("NotesText", contentGo.transform, "更新日志...", UITypography.FontSnapped(22f), ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.TopLeft);
            _notesText.lineSpacing = 16f;

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.content = contentRt;
            _scrollRect.viewport = viewportRt;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 25f;

            // 6. 下载进度条区域
            _progressBox = new GameObject("ProgressBox", typeof(RectTransform));
            _progressBox.transform.SetParent(panelGo.transform, false);
            var progRt = _progressBox.GetComponent<RectTransform>();
            progRt.anchorMin = new Vector2(0.5f, 0.5f);
            progRt.anchorMax = new Vector2(0.5f, 0.5f);
            progRt.anchoredPosition = new Vector2(0f, -140f);
            progRt.sizeDelta = new Vector2(740f, 44f);

            var barBgGo = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barBgGo.transform.SetParent(_progressBox.transform, false);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 0f);
            barBgRt.anchorMax = new Vector2(1f, 0f);
            barBgRt.pivot = new Vector2(0.5f, 0f);
            barBgRt.sizeDelta = new Vector2(0f, 20f);
            barBgRt.anchoredPosition = Vector2.zero;

            var barBgShape = barBgGo.GetComponent<UIInkShapeGraphic>();
            barBgShape.Shape = UIInkShape.Pill; // 圆角化：进度槽胶囊化
            barBgShape.BorderWidth = ComicStyle.BorderThin;
            barBgShape.BorderColor = ComicStyle.Ink;
            barBgShape.color = ProgressTrackColor; // 深灰槽底：空槽与黄色填充都有清晰边界
            barBgShape.HardShadow = false;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(barBgGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(3f, 3f);
            fillAreaRt.offsetMax = new Vector2(-3f, -3f);
            _progressFillArea = fillAreaRt;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _progressBarFill = fillGo.GetComponent<Image>();
            // 关键：Image 没有 sprite 时 fillAmount 完全失效（整条恒为满格）——
            // 这就是旧版「进度条一直全绿、像没有动画」的根因。
            UIFlatSprite.MakePill(_progressBarFill, filled: true); // 圆角化：胶囊填充
            _progressBarFill.color = ComicStyle.Yellow; // 主行动黄，替代原来的绿色
            _progressBarFill.fillAmount = 0f;

            // 进度头：纸白小胶囊 + 2px 墨边，沿填充条尽头移动（下载中的运动参照）
            var headGo = new GameObject("ProgressHead", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            headGo.transform.SetParent(fillArea.transform, false);
            var headRt = headGo.GetComponent<RectTransform>();
            headRt.anchorMin = new Vector2(0f, 0.5f);
            headRt.anchorMax = new Vector2(0f, 0.5f);
            headRt.pivot = new Vector2(0.5f, 0.5f);
            headRt.sizeDelta = new Vector2(8f, 14f);
            headRt.anchoredPosition = Vector2.zero;

            _progressBarHead = headGo.GetComponent<UIInkShapeGraphic>();
            _progressBarHead.Shape = UIInkShape.Pill; // 圆角化
            _progressBarHead.BorderWidth = 2f;
            _progressBarHead.BorderColor = ComicStyle.Ink;
            _progressBarHead.color = ComicStyle.Paper;
            _progressBarHead.HardShadow = false;
            _progressBarHead.Halftone = false;
            _progressBarHead.raycastTarget = false;
            headGo.SetActive(false);

            _progressPercentText = CreateTextMesh("PercentLabel", _progressBox.transform, "准备下载...", UITypography.FontSnapped(20f), ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.Left);
            var pRt = _progressPercentText.rectTransform;
            pRt.anchorMin = new Vector2(0f, 1f);
            pRt.anchorMax = new Vector2(0.5f, 1f);
            pRt.pivot = new Vector2(0f, 1f);
            pRt.anchoredPosition = new Vector2(0f, 0f);
            pRt.sizeDelta = new Vector2(360f, 22f);

            _progressSpeedText = CreateTextMesh("SpeedLabel", _progressBox.transform, "", UITypography.FontSnapped(20f), ComicStyle.WithAlpha(ComicStyle.Ink, 0.70f), isDisplay: false, TextAlignmentOptions.Right);
            var sRt = _progressSpeedText.rectTransform;
            sRt.anchorMin = new Vector2(0.5f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(1f, 1f);
            sRt.anchoredPosition = new Vector2(0f, 0f);
            sRt.sizeDelta = new Vector2(360f, 22f);

            _progressBox.SetActive(false);

            // 7. 底部直角按钮栏（按钮 230×72，字号 ≥22）
            var btnRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            btnRowGo.transform.SetParent(panelGo.transform, false);
            var btnRowRt = btnRowGo.GetComponent<RectTransform>();
            btnRowRt.anchorMin = new Vector2(0.5f, 0f);
            btnRowRt.anchorMax = new Vector2(0.5f, 0f);
            btnRowRt.anchoredPosition = new Vector2(0f, 54f);
            btnRowRt.sizeDelta = new Vector2(740f, 72f);

            float btnFontSize = UITypography.FontSnapped(22f); // 桌面 22，移动端 28

            // 立即更新（Red 强调色主按钮 + 白字）
            _actionBtn = CreateGeometricButton(btnRowGo.transform, "ActionBtn", "立 即 更 新",
                new Vector2(-252f, 0f), new Vector2(230f, 72f), ComicStyle.Red, ComicStyle.Paper, btnFontSize, out _actionBtnText);
            _actionBtn.onClick.AddListener(HandleActionClick);

            // 稍后再说（白底墨字直角方块）
            _cancelBtn = CreateGeometricButton(btnRowGo.transform, "CancelBtn", "稍 后 再 说",
                new Vector2(0f, 0f), new Vector2(230f, 72f), ComicStyle.Paper, ComicStyle.Ink, btnFontSize, out _cancelBtnText);
            _cancelBtn.onClick.AddListener(Hide);

            // 浏览器下载（蓝底白字直角方块）
            _browserBtn = CreateGeometricButton(btnRowGo.transform, "BrowserBtn", "浏览器下载",
                new Vector2(252f, 0f), new Vector2(230f, 72f), ComicStyle.Blue, Color.white, btnFontSize, out _browserBtnText);
            _browserBtn.onClick.AddListener(HandleBrowserClick);

            _dialogRoot.SetActive(false);
        }

        private TextMeshProUGUI CreateTextMesh(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            bool isDisplay = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;

            UIThemeSO theme = Theme;
            TMP_FontAsset resolvedFont = theme.ResolveFont(isDisplay, text);
            if (resolvedFont == null)
                resolvedFont = TMP_Settings.defaultFontAsset;
            tmp.font = resolvedFont;

            return tmp;
        }

        private Button CreateGeometricButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPos,
            Vector2 size,
            Color bgColor,
            Color textColor,
            float fontSize,
            out TextMeshProUGUI labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：弹窗按钮自适应圆角
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = bgColor;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.GlossAlpha = 0f;
            shape.DrawGloss = false;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = shape;
            btn.transition = Selectable.Transition.None;

            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(Theme);

            var jelly = go.GetComponent<UIJellyButton>();
            jelly.Configure(Theme, btn, shape);
            jelly.EnableThemeHardShadow(); // t-1 按钮硬影 + 0ms 压印 + 0.1s Cubic-Out 回弹

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            labelText = textGo.GetComponent<TextMeshProUGUI>();
            labelText.fontSize = fontSize;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = textColor;
            labelText.text = label;
            labelText.raycastTarget = false;
            labelText.font = Theme.ResolveFont(true, label);

            return btn;
        }
    }
}
