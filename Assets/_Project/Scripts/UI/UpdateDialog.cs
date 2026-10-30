using System;
using System.IO;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 应用内更新弹窗（P10 漫画风重构）：
    /// - 820×620 纸面板 + 8px 墨线粗边框 + 漫画硬阴影 + 全屏网点蒙层；
    /// - 标题对话气泡（庆科黄油体）；
    /// - 220×72 粗边按钮：更新（黄底）、稍后（纸白）、跳转（蓝底）；
    /// - 发布说明正文 20px、行距 1.2；
    /// - 彻底修复中文字体方块字：所有文本统一经 UIThemeSO.ResolveFont 获取 CJK 字体资产。
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
        private RectTransform _panelRect;
        private TextMeshProUGUI _verText;
        private TextMeshProUGUI _sizeText;
        private TextMeshProUGUI _notesText;
        private ScrollRect _scrollRect;

        // 进度控件
        private GameObject _progressBox;
        private Image _progressBarFill;
        private TextMeshProUGUI _progressPercentText;
        private TextMeshProUGUI _progressSpeedText;

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

            UpdateManager.Instance.OnDownloadProgress += HandleDownloadProgress;
            UpdateManager.Instance.OnDownloadCompleted += HandleDownloadCompleted;
            UpdateManager.Instance.OnDownloadFailed += HandleDownloadFailed;
        }

        private void OnDestroy()
        {
            if (UpdateManager.Instance != null)
            {
                UpdateManager.Instance.OnDownloadProgress -= HandleDownloadProgress;
                UpdateManager.Instance.OnDownloadCompleted -= HandleDownloadCompleted;
                UpdateManager.Instance.OnDownloadFailed -= HandleDownloadFailed;
            }
        }

        public void PopupUpdate(UpdateInfo info)
        {
            _currentInfo = info;
            _isCompleted = false;
            _completedApkPath = "";

            EnsureUI();
            ApplyResponsivePanelScale();
            if (_verText != null) _verText.text = $"新版本: {info.tagName}";
            if (_sizeText != null) _sizeText.text = $"大小: {(string.IsNullOrEmpty(info.fileSizeText) ? "未知" : info.fileSizeText)}";
            if (_notesText != null) _notesText.text = string.IsNullOrEmpty(info.releaseNotes) ? "暂无更新说明" : info.releaseNotes;

            // 重置滚动位置到顶部
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;

            // 隐藏进度条，显示默认按钮
            if (_progressBox != null) _progressBox.SetActive(false);
            if (_actionBtnText != null) _actionBtnText.text = "立 即 更 新";
            if (_actionBtn != null) _actionBtn.interactable = true;

            _dialogRoot.SetActive(true);
        }

        public void Hide()
        {
            if (UpdateManager.Instance != null && UpdateManager.Instance.IsDownloading)
            {
                UpdateManager.Instance.CancelDownload();
            }
            if (_dialogRoot != null) _dialogRoot.SetActive(false);
        }

        /// <summary>
        /// 可用高度不足时整体等比缩小面板（与暂停面板同一套规则），
        /// 避免 620px 面板在超宽屏/小窗口下溢出屏幕。
        /// </summary>
        private void ApplyResponsivePanelScale()
        {
            if (_panelRect == null) return;

            float canvasHeight = 1080f;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 660f; // 620px 面板 + 留白

            if (availableHeight < targetHeight && availableHeight > 100f)
            {
                float s = Mathf.Clamp(availableHeight / targetHeight, 0.5f, 1f);
                _panelRect.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                _panelRect.localScale = Vector3.one;
            }
        }

        private void HandleActionClick()
        {
            if (_isCompleted && !string.IsNullOrEmpty(_completedApkPath))
            {
                UpdateManager.Instance.InstallApk(_completedApkPath);
                return;
            }

            if (_currentInfo == null) return;

            // 开始下载
            if (_progressBox != null) _progressBox.SetActive(true);
            if (_progressBarFill != null) _progressBarFill.fillAmount = 0f;
            if (_progressPercentText != null) _progressPercentText.text = "准备下载...";
            if (_progressSpeedText != null) _progressSpeedText.text = "";
            if (_actionBtn != null) _actionBtn.interactable = false;
            if (_actionBtnText != null) _actionBtnText.text = "下 载 中...";

            UpdateManager.Instance.StartDownload(_currentInfo);
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

            if (_progressBarFill != null) _progressBarFill.fillAmount = percent / 100f;
            string loadedStr = GameVersion.FormatBytes(loaded);
            string totalStr = GameVersion.FormatBytes(total);
            if (_progressPercentText != null) _progressPercentText.text = $"{percent:F0}% ({loadedStr} / {totalStr})";

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

            if (_progressBox != null)
            {
                if (_progressBarFill != null) _progressBarFill.fillAmount = 1f;
                if (_progressPercentText != null) _progressPercentText.text = "100% (下载完成)";
                if (_progressSpeedText != null) _progressSpeedText.text = "准备安装";
            }

            if (_actionBtn != null)
            {
                _actionBtn.interactable = true;
            }
            if (_actionBtnText != null)
            {
                _actionBtnText.text = "立 即 安 装";
            }

            // 自动触发一次安装
            UpdateManager.Instance.InstallApk(filePath);
        }

        private void HandleDownloadFailed(string error)
        {
            if (_progressBox != null)
            {
                if (_progressPercentText != null) _progressPercentText.text = "下载中断";
                if (_progressSpeedText != null) _progressSpeedText.text = error;
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

            // 全屏根节点（顺带收进安全区：横屏刘海机上面板与按钮不被切）
            _dialogRoot = new GameObject("UpdateDialogRoot", typeof(RectTransform));
            _dialogRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = _dialogRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            _dialogRoot.AddComponent<SafeAreaFitter>();

            // 1. 全屏黑色暗底半透明遮罩 (墨色 65%)
            var overlayGo = new GameObject("DimOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlayGo.transform.SetParent(_dialogRoot.transform, false);
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlayImg = overlayGo.GetComponent<Image>();
            overlayImg.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            overlayImg.raycastTarget = true;

            // 2. 全屏半调网点底
            var halfGo = new GameObject("HalftoneBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicHalftoneGraphic));
            halfGo.transform.SetParent(_dialogRoot.transform, false);
            var halfRt = halfGo.GetComponent<RectTransform>();
            halfRt.anchorMin = Vector2.zero;
            halfRt.anchorMax = Vector2.one;
            halfRt.offsetMin = Vector2.zero;
            halfRt.offsetMax = Vector2.zero;
            var halfGraphic = halfGo.GetComponent<ComicHalftoneGraphic>();
            halfGraphic.color = ComicStyle.Ink;
            halfGraphic.Alpha = 0.08f;
            halfGraphic.Spacing = 14f;
            halfGraphic.raycastTarget = false;

            // 3. 居中主面板 (820 × 620，纸面 + 8px 墨边 + 硬阴影 + 网点)
            var panelGo = new GameObject("MainPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_dialogRoot.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(820f, 620f);
            _panelRect = panelRt;

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.RoundedRect;
            panelShape.CornerRadius = ComicStyle.CornerSharp;
            panelShape.BorderWidth = ComicStyle.BorderHeavy; // 8px 墨边
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.HardShadow = true;
            panelShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            panelShape.ShadowColor = ComicStyle.Ink;
            panelShape.Halftone = true;
            panelShape.HalftoneAlpha = ComicStyle.HalftoneAlpha;
            panelShape.GlossAlpha = 0f;
            panelShape.DrawGloss = false;
            panelShape.raycastTarget = true;

            var panelMotion = panelGo.GetComponent<UISpringMotion>();
            panelMotion.SetTheme(Theme);

            // 4. 标题对话气泡 (发 现 新 版 本)
            var bubbleGo = new GameObject("TitleBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bubbleGo.transform.SetParent(panelGo.transform, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchorMin = new Vector2(0.5f, 1f);
            bubbleRt.anchorMax = new Vector2(0.5f, 1f);
            bubbleRt.anchoredPosition = new Vector2(0f, -48f);
            bubbleRt.sizeDelta = new Vector2(380f, 64f);

            var bubbleShape = bubbleGo.GetComponent<UIInkShapeGraphic>();
            bubbleShape.Shape = UIInkShape.SpeechBubble;
            bubbleShape.CornerRadius = 26f;
            bubbleShape.BorderWidth = ComicStyle.Border;
            bubbleShape.BorderColor = ComicStyle.Ink;
            bubbleShape.color = ComicStyle.Paper;
            bubbleShape.HardShadow = true;
            bubbleShape.ShadowOffset = new Vector2(3f, -3f);
            bubbleShape.ShadowColor = ComicStyle.Ink;
            bubbleShape.SpeechTail = true;
            bubbleShape.TailSize = new Vector2(22f, 16f);
            bubbleShape.TailAngle = 270f;
            bubbleShape.GlossAlpha = 0f;
            bubbleShape.DrawGloss = false;

            var titleText = CreateTextMesh("TitleText", bubbleGo.transform, "发 现 新 版 本", 34f, ComicStyle.Ink, isDisplay: true);
            var tRt = titleText.rectTransform;
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            // 5. 版本与体积元信息
            var metaGo = new GameObject("MetaRow", typeof(RectTransform));
            metaGo.transform.SetParent(panelGo.transform, false);
            var metaRt = metaGo.GetComponent<RectTransform>();
            metaRt.anchorMin = new Vector2(0.5f, 1f);
            metaRt.anchorMax = new Vector2(0.5f, 1f);
            metaRt.anchoredPosition = new Vector2(0f, -100f);
            metaRt.sizeDelta = new Vector2(720f, 32f);

            _verText = CreateTextMesh("VersionText", metaGo.transform, "新版本: v0.0.0", 22f, ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.Left);
            var verRt = _verText.rectTransform;
            verRt.anchorMin = new Vector2(0f, 0f);
            verRt.anchorMax = new Vector2(0.5f, 1f);
            verRt.offsetMin = Vector2.zero;
            verRt.offsetMax = Vector2.zero;

            _sizeText = CreateTextMesh("SizeText", metaGo.transform, "大小: -- MB", 20f, ComicStyle.WithAlpha(ComicStyle.Ink, 0.7f), isDisplay: false, TextAlignmentOptions.Right);
            var sizeRt = _sizeText.rectTransform;
            sizeRt.anchorMin = new Vector2(0.5f, 0f);
            sizeRt.anchorMax = new Vector2(1f, 1f);
            sizeRt.offsetMin = Vector2.zero;
            sizeRt.offsetMax = Vector2.zero;

            // 6. 更新说明滚动视口 (发布说明正文 20px、行距 1.2)
            var scrollGo = new GameObject("NotesScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(ScrollRect));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.anchoredPosition = new Vector2(0f, 30f);
            scrollRt.sizeDelta = new Vector2(720f, 230f);

            var scrollShape = scrollGo.GetComponent<UIInkShapeGraphic>();
            scrollShape.Shape = UIInkShape.RoundedRect;
            scrollShape.CornerRadius = ComicStyle.CornerSharp;
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

            _notesText = CreateTextMesh("NotesText", contentGo.transform, "更新日志...", 20f, ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.TopLeft);
            _notesText.lineSpacing = 20f; // 相当于 1.2 行距

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.content = contentRt;
            _scrollRect.viewport = viewportRt;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 25f;

            // 7. 下载进度条区域
            _progressBox = new GameObject("ProgressBox", typeof(RectTransform));
            _progressBox.transform.SetParent(panelGo.transform, false);
            var progRt = _progressBox.GetComponent<RectTransform>();
            progRt.anchorMin = new Vector2(0.5f, 0.5f);
            progRt.anchorMax = new Vector2(0.5f, 0.5f);
            progRt.anchoredPosition = new Vector2(0f, -135f);
            progRt.sizeDelta = new Vector2(720f, 44f);

            // 进度条槽
            var barBgGo = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barBgGo.transform.SetParent(_progressBox.transform, false);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 0f);
            barBgRt.anchorMax = new Vector2(1f, 0f);
            barBgRt.pivot = new Vector2(0.5f, 0f);
            barBgRt.sizeDelta = new Vector2(0f, 20f);
            barBgRt.anchoredPosition = Vector2.zero;

            var barBgShape = barBgGo.GetComponent<UIInkShapeGraphic>();
            barBgShape.Shape = UIInkShape.RoundedRect;
            barBgShape.CornerRadius = 4f;
            barBgShape.BorderWidth = ComicStyle.BorderThin;
            barBgShape.BorderColor = ComicStyle.Ink;
            barBgShape.color = ComicStyle.PaperAlt;
            barBgShape.HardShadow = false;

            // 进度填充条
            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(barBgGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(4f, 4f);
            fillAreaRt.offsetMax = new Vector2(-4f, -4f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _progressBarFill = fillGo.GetComponent<Image>();
            _progressBarFill.type = Image.Type.Filled;
            _progressBarFill.fillMethod = Image.FillMethod.Horizontal;
            _progressBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressBarFill.color = ComicStyle.Green;
            _progressBarFill.fillAmount = 0f;

            // 进度百分比与速率
            _progressPercentText = CreateTextMesh("PercentLabel", _progressBox.transform, "准备下载...", 18f, ComicStyle.Ink, isDisplay: false, TextAlignmentOptions.Left);
            var pRt = _progressPercentText.rectTransform;
            pRt.anchorMin = new Vector2(0f, 1f);
            pRt.anchorMax = new Vector2(0.5f, 1f);
            pRt.pivot = new Vector2(0f, 1f);
            pRt.anchoredPosition = new Vector2(0f, 0f);
            pRt.sizeDelta = new Vector2(350f, 22f);

            _progressSpeedText = CreateTextMesh("SpeedLabel", _progressBox.transform, "", 18f, ComicStyle.WithAlpha(ComicStyle.Ink, 0.7f), isDisplay: false, TextAlignmentOptions.Right);
            var sRt = _progressSpeedText.rectTransform;
            sRt.anchorMin = new Vector2(0.5f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.pivot = new Vector2(1f, 1f);
            sRt.anchoredPosition = new Vector2(0f, 0f);
            sRt.sizeDelta = new Vector2(350f, 22f);

            _progressBox.SetActive(false);

            // 8. 底部按钮栏：按钮 220×72（更新=黄，稍后=纸，跳转=蓝）
            var btnRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            btnRowGo.transform.SetParent(panelGo.transform, false);
            var btnRowRt = btnRowGo.GetComponent<RectTransform>();
            btnRowRt.anchorMin = new Vector2(0.5f, 0f);
            btnRowRt.anchorMax = new Vector2(0.5f, 0f);
            btnRowRt.anchoredPosition = new Vector2(0f, 52f);
            btnRowRt.sizeDelta = new Vector2(720f, 72f);

            // 立即更新（黄底黑字）
            _actionBtn = CreateComicButton(btnRowGo.transform, "ActionBtn", "立 即 更 新",
                new Vector2(-248f, 0f), new Vector2(220f, 72f), ComicStyle.Yellow, ComicStyle.Ink, out _actionBtnText);
            _actionBtn.onClick.AddListener(HandleActionClick);

            // 稍后再说（纸白底黑字）
            _cancelBtn = CreateComicButton(btnRowGo.transform, "CancelBtn", "稍 后 再 说",
                new Vector2(0f, 0f), new Vector2(220f, 72f), ComicStyle.Paper, ComicStyle.Ink, out _cancelBtnText);
            _cancelBtn.onClick.AddListener(Hide);

            // 浏览器下载（蓝底白字）
            _browserBtn = CreateComicButton(btnRowGo.transform, "BrowserBtn", "浏览器下载",
                new Vector2(248f, 0f), new Vector2(220f, 72f), ComicStyle.Blue, Color.white, out _browserBtnText);
            _browserBtn.onClick.AddListener(HandleBrowserClick);

            _dialogRoot.SetActive(false);
        }

        /// <summary>
        /// 统一文本构造工厂：先通过 UIThemeSO.ResolveFont 解析对应字体，不设字体会导致方块字 □。
        /// </summary>
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

            // 关键修复：统一通过 UIThemeSO.ResolveFont 解析 CJK 字体资产，不设字体就会出现方块字 □。
            UIThemeSO theme = Theme;
            TMP_FontAsset resolvedFont = theme.ResolveFont(isDisplay, text);
            if (resolvedFont == null)
                resolvedFont = TMP_Settings.defaultFontAsset;
            tmp.font = resolvedFont;

            return tmp;
        }

        private Button CreateComicButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPos,
            Vector2 size,
            Color bgColor,
            Color textColor,
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
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = bgColor;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            shape.ShadowColor = ComicStyle.Ink;
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

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            labelText = textGo.GetComponent<TextMeshProUGUI>();
            labelText.fontSize = 22f;
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
