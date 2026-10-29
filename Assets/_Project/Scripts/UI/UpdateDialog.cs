using System;
using System.IO;
using P1.Core;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 应用内更新弹窗（对标 Godot 的 UpdateDialog.gd）。
    /// 支持全屏暗底、版本日志滚动视口、实时字节流下载进度条、
    /// 「立即更新/立即安装」、「浏览器下载」、「稍后再说」交互。
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
        private GameObject _dialogRoot;
        private Text _verText;
        private Text _sizeText;
        private Text _notesText;
        private ScrollRect _scrollRect;

        // 进度控件
        private GameObject _progressBox;
        private Image _progressBarFill;
        private Text _progressPercentText;
        private Text _progressSpeedText;

        // 按钮
        private Button _actionBtn;
        private Text _actionBtnText;
        private Button _browserBtn;
        private Button _cancelBtn;

        private UpdateInfo _currentInfo;
        private bool _isCompleted;
        private string _completedApkPath;

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
            _verText.text = $"新版本: {info.tagName}";
            _sizeText.text = $"大小: {(string.IsNullOrEmpty(info.fileSizeText) ? "未知" : info.fileSizeText)}";
            _notesText.text = string.IsNullOrEmpty(info.releaseNotes) ? "暂无更新说明" : info.releaseNotes;

            // 重置滚动位置到顶部
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;

            // 隐藏进度条，显示默认按钮
            _progressBox.SetActive(false);
            _actionBtnText.text = "立 即 更 新";
            _actionBtn.interactable = true;

            _dialogRoot.SetActive(true);
        }

        public void Hide()
        {
            if (UpdateManager.Instance.IsDownloading)
            {
                UpdateManager.Instance.CancelDownload();
            }
            if (_dialogRoot != null) _dialogRoot.SetActive(false);
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
            _progressBox.SetActive(true);
            _progressBarFill.fillAmount = 0f;
            _progressPercentText.text = "准备下载...";
            _progressSpeedText.text = "";
            _actionBtn.interactable = false;
            _actionBtnText.text = "下 载 中...";

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

            _progressBarFill.fillAmount = percent / 100f;
            string loadedStr = GameVersion.FormatBytes(loaded);
            string totalStr = GameVersion.FormatBytes(total);
            _progressPercentText.text = $"{percent:F0}% ({loadedStr} / {totalStr})";

            if (speed > 0f)
            {
                string speedStr = $"{GameVersion.FormatBytes((long)speed)}/s";
                string etaStr = etaSec > 0f ? $" · 剩余 {etaSec:F0}秒" : "";
                _progressSpeedText.text = speedStr + etaStr;
            }
            else
            {
                _progressSpeedText.text = "连接中...";
            }
        }

        private void HandleDownloadCompleted(string filePath)
        {
            _isCompleted = true;
            _completedApkPath = filePath;

            if (_progressBox != null)
            {
                _progressBarFill.fillAmount = 1f;
                _progressPercentText.text = "100% (下载完成)";
                _progressSpeedText.text = "准备安装";
            }

            if (_actionBtn != null)
            {
                _actionBtn.interactable = true;
                _actionBtnText.text = "立 即 安 装";
            }

            // 自动触发一次安装
            UpdateManager.Instance.InstallApk(filePath);
        }

        private void HandleDownloadFailed(string error)
        {
            if (_progressBox != null)
            {
                _progressPercentText.text = "下载中断";
                _progressSpeedText.text = error;
            }

            if (_actionBtn != null)
            {
                _actionBtn.interactable = true;
                _actionBtnText.text = "重 试 下 载";
            }

            UpdateManager.Instance.ShowToast($"下载失败: {error}", new Color(1f, 0.35f, 0.35f));
        }

        private void EnsureUI()
        {
            if (_dialogRoot != null) return;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("[UpdateDialogCanvas]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasGo);
            _dialogCanvas = canvasGo.GetComponent<Canvas>();
            _dialogCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _dialogCanvas.sortingOrder = 9998;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // 全屏根节点
            _dialogRoot = new GameObject("UpdateDialogRoot", typeof(RectTransform));
            _dialogRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = _dialogRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 1. 全屏黑色暗底半透明遮罩
            var overlayGo = new GameObject("DimOverlay", typeof(RectTransform), typeof(Image));
            overlayGo.transform.SetParent(_dialogRoot.transform, false);
            var overlayRt = overlayGo.GetComponent<RectTransform>();
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var overlayImg = overlayGo.GetComponent<Image>();
            overlayImg.color = new Color(0.02f, 0.03f, 0.05f, 0.88f);

            // 2. 居中面板 (920 x 680)
            var panelGo = new GameObject("MainPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(_dialogRoot.transform, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(920f, 680f);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0.10f, 0.12f, 0.17f, 0.98f);

            // 3. 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -50f);
            titleRt.sizeDelta = new Vector2(800f, 60f);
            var titleText = titleGo.GetComponent<Text>();
            titleText.font = font;
            titleText.fontSize = 42;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(1f, 0.65f, 0.18f, 1f);
            titleText.text = "发  现  新  版  本";

            // 4. 版本与体积元信息
            var metaGo = new GameObject("MetaRow", typeof(RectTransform));
            metaGo.transform.SetParent(panelGo.transform, false);
            var metaRt = metaGo.GetComponent<RectTransform>();
            metaRt.anchorMin = new Vector2(0.5f, 1f);
            metaRt.anchorMax = new Vector2(0.5f, 1f);
            metaRt.anchoredPosition = new Vector2(0f, -100f);
            metaRt.sizeDelta = new Vector2(760f, 40f);

            var verGo = new GameObject("VersionText", typeof(RectTransform), typeof(Text));
            verGo.transform.SetParent(metaGo.transform, false);
            var verRt = verGo.GetComponent<RectTransform>();
            verRt.anchorMin = new Vector2(0f, 0f);
            verRt.anchorMax = new Vector2(0.5f, 1f);
            verRt.offsetMin = Vector2.zero;
            verRt.offsetMax = Vector2.zero;
            _verText = verGo.GetComponent<Text>();
            _verText.font = font;
            _verText.fontSize = 24;
            _verText.alignment = TextAnchor.MiddleLeft;
            _verText.color = new Color(0.35f, 0.8f, 1f, 1f);
            _verText.text = "新版本: v0.0.0";

            var sizeGo = new GameObject("SizeText", typeof(RectTransform), typeof(Text));
            sizeGo.transform.SetParent(metaGo.transform, false);
            var sizeRt = sizeGo.GetComponent<RectTransform>();
            sizeRt.anchorMin = new Vector2(0.5f, 0f);
            sizeRt.anchorMax = new Vector2(1f, 1f);
            sizeRt.offsetMin = Vector2.zero;
            sizeRt.offsetMax = Vector2.zero;
            _sizeText = sizeGo.GetComponent<Text>();
            _sizeText.font = font;
            _sizeText.fontSize = 22;
            _sizeText.alignment = TextAnchor.MiddleRight;
            _sizeText.color = new Color(0.75f, 0.8f, 0.9f, 1f);
            _sizeText.text = "大小: -- MB";

            // 5. 更新说明滚动视口 (ScrollRect)
            var scrollGo = new GameObject("NotesScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.anchoredPosition = new Vector2(0f, 35f);
            scrollRt.sizeDelta = new Vector2(800f, 260f);
            var scrollImg = scrollGo.GetComponent<Image>();
            scrollImg.color = new Color(0.06f, 0.07f, 0.10f, 0.95f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(16f, 16f);
            viewportRt.offsetMax = new Vector2(-16f, -16f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

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

            var notesTextGo = new GameObject("NotesText", typeof(RectTransform), typeof(Text));
            notesTextGo.transform.SetParent(contentGo.transform, false);
            _notesText = notesTextGo.GetComponent<Text>();
            _notesText.font = font;
            _notesText.fontSize = 22;
            _notesText.lineSpacing = 1.35f;
            _notesText.alignment = TextAnchor.UpperLeft;
            _notesText.color = new Color(0.9f, 0.92f, 0.98f, 1f);
            _notesText.text = "更新日志...";

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.content = contentRt;
            _scrollRect.viewport = viewportRt;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // 6. 下载进度条区域
            _progressBox = new GameObject("ProgressBox", typeof(RectTransform));
            _progressBox.transform.SetParent(panelGo.transform, false);
            var progRt = _progressBox.GetComponent<RectTransform>();
            progRt.anchorMin = new Vector2(0.5f, 0.5f);
            progRt.anchorMax = new Vector2(0.5f, 0.5f);
            progRt.anchoredPosition = new Vector2(0f, -130f);
            progRt.sizeDelta = new Vector2(800f, 50f);

            // 进度条槽
            var barBgGo = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(Image));
            barBgGo.transform.SetParent(_progressBox.transform, false);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 0.5f);
            barBgRt.anchorMax = new Vector2(1f, 0.5f);
            barBgRt.sizeDelta = new Vector2(0f, 18f);
            barBgGo.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.30f, 1f);

            // 进度填充条
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBgGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _progressBarFill = fillGo.GetComponent<Image>();
            _progressBarFill.type = Image.Type.Filled;
            _progressBarFill.fillMethod = Image.FillMethod.Horizontal;
            _progressBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressBarFill.color = new Color(0.2f, 0.85f, 0.45f, 1f);
            _progressBarFill.fillAmount = 0f;

            // 进度百分比与速率
            var progPercentGo = new GameObject("PercentLabel", typeof(RectTransform), typeof(Text));
            progPercentGo.transform.SetParent(_progressBox.transform, false);
            var pRt = progPercentGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0f, 1f);
            pRt.anchorMax = new Vector2(0.5f, 1f);
            pRt.anchoredPosition = new Vector2(0f, 16f);
            pRt.sizeDelta = new Vector2(380f, 24f);
            _progressPercentText = progPercentGo.GetComponent<Text>();
            _progressPercentText.font = font;
            _progressPercentText.fontSize = 20;
            _progressPercentText.alignment = TextAnchor.MiddleLeft;
            _progressPercentText.color = Color.white;

            var progSpeedGo = new GameObject("SpeedLabel", typeof(RectTransform), typeof(Text));
            progSpeedGo.transform.SetParent(_progressBox.transform, false);
            var sRt = progSpeedGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0.5f, 1f);
            sRt.anchorMax = new Vector2(1f, 1f);
            sRt.anchoredPosition = new Vector2(0f, 16f);
            sRt.sizeDelta = new Vector2(380f, 24f);
            _progressSpeedText = progSpeedGo.GetComponent<Text>();
            _progressSpeedText.font = font;
            _progressSpeedText.fontSize = 18;
            _progressSpeedText.alignment = TextAnchor.MiddleRight;
            _progressSpeedText.color = new Color(0.75f, 0.85f, 0.95f, 1f);

            _progressBox.SetActive(false);

            // 7. 底部按钮栏
            var btnRowGo = new GameObject("ButtonRow", typeof(RectTransform));
            btnRowGo.transform.SetParent(panelGo.transform, false);
            var btnRowRt = btnRowGo.GetComponent<RectTransform>();
            btnRowRt.anchorMin = new Vector2(0.5f, 0f);
            btnRowRt.anchorMax = new Vector2(0.5f, 0f);
            btnRowRt.anchoredPosition = new Vector2(0f, 55f);
            btnRowRt.sizeDelta = new Vector2(800f, 70f);

            _actionBtn = CreateButton(btnRowGo.transform, "ActionBtn", font, "立 即 更 新",
                new Vector2(-270f, 0f), new Vector2(230f, 62f), new Color(1f, 0.52f, 0.12f, 1f), out _actionBtnText);
            _actionBtn.onClick.AddListener(HandleActionClick);

            _browserBtn = CreateButton(btnRowGo.transform, "BrowserBtn", font, "浏览器下载",
                new Vector2(0f, 0f), new Vector2(230f, 62f), new Color(0.20f, 0.55f, 0.90f, 1f), out _);
            _browserBtn.onClick.AddListener(HandleBrowserClick);

            _cancelBtn = CreateButton(btnRowGo.transform, "CancelBtn", font, "稍 后 再 说",
                new Vector2(270f, 0f), new Vector2(230f, 62f), new Color(0.35f, 0.38f, 0.45f, 1f), out _);
            _cancelBtn.onClick.AddListener(Hide);

            _dialogRoot.SetActive(false);
        }

        private static Button CreateButton(Transform parent, string name, Font font, string label,
            Vector2 anchoredPos, Vector2 size, Color bgColor, out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = bgColor;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            labelText = textGo.GetComponent<Text>();
            labelText.font = font;
            labelText.fontSize = 26;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.text = label;

            return btn;
        }
    }
}
