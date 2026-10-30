using System;
using System.IO;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 应用内更新弹窗（P14 几何大胆风 Geometric Bold 重构）：
    /// - 840×640 直角黑白面板（SharpRect，0 圆角，6px 粗墨边）；
    /// - 纯色直角标题条；
    /// - Red 关键 CTA 按钮（立即更新），直角白底墨字次级按钮（稍后再说），蓝底直角按钮（浏览器下载）；
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

            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;

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

        private void ApplyResponsivePanelScale()
        {
            if (_panelRect == null) return;

            float canvasHeight = 1080f;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float availableHeight = canvasHeight * safeRatio;
            const float targetHeight = 680f;

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

            _dialogRoot = new GameObject("UpdateDialogRoot", typeof(RectTransform));
            _dialogRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = _dialogRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            _dialogRoot.AddComponent<SafeAreaFitter>();

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

            // 2. 居中主面板 (840 × 640，直角黑白面板 + 6px 墨边，无阴影无网点)
            var panelGo = new GameObject("MainPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(_dialogRoot.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(840f, 640f);
            _panelRect = panelRt;

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.SharpRect; // 直角面板
            panelShape.CornerRadius = ComicStyle.CornerSharp;
            panelShape.BorderWidth = ComicStyle.BorderHeavy;
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.HardShadow = false;
            panelShape.Halftone = false;
            panelShape.GlossAlpha = 0f;
            panelShape.DrawGloss = false;
            panelShape.raycastTarget = true;

            var panelMotion = panelGo.GetComponent<UISpringMotion>();
            panelMotion.SetTheme(Theme);

            // 3. 顶部直角标题块
            var titleBoxGo = new GameObject("TitleBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            titleBoxGo.transform.SetParent(panelGo.transform, false);
            var titleBoxRt = titleBoxGo.GetComponent<RectTransform>();
            titleBoxRt.anchorMin = new Vector2(0.5f, 1f);
            titleBoxRt.anchorMax = new Vector2(0.5f, 1f);
            titleBoxRt.anchoredPosition = new Vector2(0f, -48f);
            titleBoxRt.sizeDelta = new Vector2(420f, 64f);

            var titleBoxShape = titleBoxGo.GetComponent<UIInkShapeGraphic>();
            titleBoxShape.Shape = UIInkShape.SharpRect;
            titleBoxShape.CornerRadius = ComicStyle.CornerSharp;
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
            scrollShape.Shape = UIInkShape.SharpRect;
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
            barBgShape.Shape = UIInkShape.SharpRect;
            barBgShape.CornerRadius = ComicStyle.CornerSharp;
            barBgShape.BorderWidth = ComicStyle.BorderThin;
            barBgShape.BorderColor = ComicStyle.Ink;
            barBgShape.color = ComicStyle.PaperAlt;
            barBgShape.HardShadow = false;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(barBgGo.transform, false);
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(3f, 3f);
            fillAreaRt.offsetMax = new Vector2(-3f, -3f);

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
            shape.Shape = UIInkShape.SharpRect; // 直角按钮
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
