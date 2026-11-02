using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 首次启动隐私同意弹窗（TapTap 隐私合规）。
    ///
    /// 设计约束：
    /// - 运行时装配，不改主场景；仅在非编辑器构建中自动弹出，避免打断 PlayMode 测试；
    /// - 未同意前不发起任何网络请求（UpdateManager.CheckForUpdate 也会二次校验）；
    /// - 同意状态按隐私政策版本号存 PlayerPrefs，政策更新后会重新询问；
    /// - 不同意直接退出应用，不进入游戏。
    /// </summary>
    public class PrivacyConsentDialog : MonoBehaviour
    {
        /// <summary>隐私政策版本号：政策内容有实质变化时递增，老玩家会重新看到同意弹窗。</summary>
        public const string PolicyVersion = "2026-11-02";

        public const string PolicyUrl = "https://favoroo.github.io/moshuizuozhan/privacy.html";
        public const string TermsUrl = "https://favoroo.github.io/moshuizuozhan/terms.html";

        private const string AcceptedKey = "P1.PrivacyConsent.Accepted";
        private const string VersionKey = "P1.PrivacyConsent.Version";

        private static PrivacyConsentDialog _instance;

        /// <summary>
        /// 是否已同意当前版本隐私政策。
        /// 编辑器内恒为 true：不自动弹窗，也不阻断编辑器里的检查更新与自动化测试。
        /// </summary>
        public static bool HasValidConsent
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return PlayerPrefs.GetInt(AcceptedKey, 0) == 1
                    && string.Equals(PlayerPrefs.GetString(VersionKey, ""), PolicyVersion, StringComparison.Ordinal);
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoShow()
        {
#if UNITY_EDITOR
            // 编辑器里用 P1/隐私合规/0 预览；自动弹窗会打断 PlayMode 与测试。
            return;
#else
            if (HasValidConsent) return;
            ShowDialog();
#endif
        }

        /// <summary>打开同意弹窗（设备首次启动、或设置里重新查看）。</summary>
        public static void ShowDialog()
        {
            if (_instance == null)
            {
                var go = new GameObject("[PrivacyConsentDialog]");
                _instance = go.AddComponent<PrivacyConsentDialog>();
                DontDestroyOnLoad(go);
            }
            _instance.Show();
        }

        public static void OpenPolicyPage()
        {
            Application.OpenURL(PolicyUrl);
        }

        public static void OpenTermsPage()
        {
            Application.OpenURL(TermsUrl);
        }

        private UIThemeSO _theme;
        private UIThemeSO Theme
        {
            get
            {
                if (_theme == null)
                {
                    P1.Core.RuntimeAssetLibrary lib = P1.Core.RuntimeAssetLibrary.Load();
                    if (lib != null && lib.uiTheme != null) _theme = lib.uiTheme;
#if UNITY_EDITOR
                    if (_theme == null)
                    {
                        _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(
                            "Assets/_Project/ScriptableObjects/UITheme.asset");
                    }
#endif
                    _theme = UIThemeSO.Resolve(_theme);
                }
                return _theme;
            }
        }

        private RectTransform _panelRect;
        private GameObject _root;
        private TextMeshProUGUI _checkMark;
        private UIInkShapeGraphic _checkBoxShape;
        private Button _agreeBtn;
        private UIInkShapeGraphic _agreeShape;
        private TextMeshProUGUI _agreeLabel;

        private bool _built;
        private bool _agreed;
        private float _previousTimeScale = 1f;

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
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_root != null && _root.activeSelf)
            {
                Time.timeScale = _previousTimeScale;
                AudioListener.pause = false;
            }
        }

        private void Show()
        {
            EnsureUI();
            if (_root == null) return;

            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _root.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void CloseAndAccept()
        {
            if (!_agreed) return;

            PlayerPrefs.SetInt(AcceptedKey, 1);
            PlayerPrefs.SetString(VersionKey, PolicyVersion);
            PlayerPrefs.Save();

            Time.timeScale = _previousTimeScale;
            AudioListener.pause = false;
            _root.SetActive(false);
        }

        private void DeclineAndQuit()
        {
            Time.timeScale = _previousTimeScale;
            AudioListener.pause = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ToggleAgreement()
        {
            _agreed = !_agreed;

            if (_checkMark != null) _checkMark.text = _agreed ? "✓" : "";
            if (_checkBoxShape != null) _checkBoxShape.color = _agreed ? ComicStyle.Green : ComicStyle.Paper;
            if (_agreeBtn != null) _agreeBtn.interactable = _agreed;
            if (_agreeShape != null) _agreeShape.color = _agreed ? ComicStyle.Green : ComicStyle.PaperAlt;
            if (_agreeLabel != null)
                _agreeLabel.color = _agreed ? ComicStyle.Paper : ComicStyle.WithAlpha(ComicStyle.Ink, 0.45f);
        }

        private void EnsureUI()
        {
            if (_built) return;
            _built = true;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10000;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _root = new GameObject("PrivacyConsentRoot", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 全屏遮罩：未同意前挡住所有底层交互。
            var dimGo = new GameObject("DimOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dimGo.transform.SetParent(_root.transform, false);
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            var dim = dimGo.GetComponent<Image>();
            dim.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.82f);
            dim.raycastTarget = true;

            // 主面板 1000×780：纸白直角 + 6px 墨边 + 弹窗硬影。
            var panelGo = new GameObject("MainPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            panelGo.transform.SetParent(_root.transform, false);
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(1000f, 780f);

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.SharpRect;
            panelShape.CornerRadius = ComicStyle.CornerSharp;
            panelShape.BorderWidth = ComicStyle.BorderHeavy;
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.Halftone = false;
            panelShape.GlossAlpha = 0f;
            panelShape.DrawGloss = false;
            panelShape.raycastTarget = true;
            Theme.ApplyHardShadow(panelShape, UIHardShadowLevel.Dialog);

            // 顶部黑色标题条。
            var titleBarGo = new GameObject("TitleBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            titleBarGo.transform.SetParent(panelGo.transform, false);
            var titleBarRt = titleBarGo.GetComponent<RectTransform>();
            titleBarRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleBarRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleBarRt.anchoredPosition = new Vector2(0f, 322f);
            titleBarRt.sizeDelta = new Vector2(620f, 76f);
            var titleBarShape = titleBarGo.GetComponent<UIInkShapeGraphic>();
            titleBarShape.Shape = UIInkShape.SharpRect;
            titleBarShape.CornerRadius = ComicStyle.CornerSharp;
            titleBarShape.BorderWidth = 0f;
            titleBarShape.color = ComicStyle.Ink;
            titleBarShape.HardShadow = false;
            titleBarShape.Halftone = false;

            var title = CreateText("TitleText", titleBarGo.transform, "隐私与用户协议",
                UITypography.FontSnapped(32f), ComicStyle.Paper, true);
            Stretch(title.rectTransform);

            // 正文摘要。
            var body = CreateText("BodyText", panelGo.transform, BuildBodyText(),
                UITypography.FontSnapped(22f), ComicStyle.Ink, false, TextAlignmentOptions.TopLeft);
            var bodyRt = body.rectTransform;
            bodyRt.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.anchoredPosition = new Vector2(0f, 138f);
            bodyRt.sizeDelta = new Vector2(880f, 260f);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.raycastTarget = false;

            // 政策 / 协议链接。
            var policyBtn = CreateButton("PolicyBtn", panelGo.transform, "《隐私政策》",
                new Vector2(-220f, -28f), new Vector2(400f, 48f),
                ComicStyle.Paper, ComicStyle.Ink, UITypography.FontSnapped(22f),
                out _);
            policyBtn.onClick.AddListener(OpenPolicyPage);

            var termsBtn = CreateButton("TermsBtn", panelGo.transform, "《用户协议》",
                new Vector2(220f, -28f), new Vector2(400f, 48f),
                ComicStyle.Paper, ComicStyle.Ink, UITypography.FontSnapped(22f),
                out _);
            termsBtn.onClick.AddListener(OpenTermsPage);

            // 勾选行：整行可点，左侧方框 + 说明文字。
            var checkRowGo = new GameObject("AgreeRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            checkRowGo.transform.SetParent(panelGo.transform, false);
            var checkRowRt = checkRowGo.GetComponent<RectTransform>();
            checkRowRt.anchorMin = new Vector2(0.5f, 0.5f);
            checkRowRt.anchorMax = new Vector2(0.5f, 0.5f);
            checkRowRt.anchoredPosition = new Vector2(0f, -88f);
            checkRowRt.sizeDelta = new Vector2(880f, 48f);
            var checkRowImg = checkRowGo.GetComponent<Image>();
            checkRowImg.color = new Color(0f, 0f, 0f, 0f);
            checkRowImg.raycastTarget = true;
            var checkRowBtn = checkRowGo.GetComponent<Button>();
            checkRowBtn.targetGraphic = checkRowImg;
            checkRowBtn.transition = Selectable.Transition.None;
            checkRowBtn.onClick.AddListener(ToggleAgreement);

            var checkBoxGo = new GameObject("CheckBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            checkBoxGo.transform.SetParent(checkRowGo.transform, false);
            var checkBoxRt = checkBoxGo.GetComponent<RectTransform>();
            checkBoxRt.anchorMin = new Vector2(0f, 0.5f);
            checkBoxRt.anchorMax = new Vector2(0f, 0.5f);
            checkBoxRt.pivot = new Vector2(0f, 0.5f);
            checkBoxRt.anchoredPosition = new Vector2(0f, 0f);
            checkBoxRt.sizeDelta = new Vector2(42f, 42f);
            _checkBoxShape = checkBoxGo.GetComponent<UIInkShapeGraphic>();
            _checkBoxShape.Shape = UIInkShape.SharpRect;
            _checkBoxShape.CornerRadius = ComicStyle.CornerSharp;
            _checkBoxShape.BorderWidth = ComicStyle.Border;
            _checkBoxShape.BorderColor = ComicStyle.Ink;
            _checkBoxShape.color = ComicStyle.Paper;
            _checkBoxShape.HardShadow = false;
            _checkBoxShape.raycastTarget = false;

            _checkMark = CreateText("CheckMark", checkBoxGo.transform, "", UITypography.FontSnapped(26f),
                ComicStyle.Paper, true);
            Stretch(_checkMark.rectTransform);

            var checkLabel = CreateText("AgreeLabel", checkRowGo.transform,
                "我已阅读并同意《隐私政策》和《用户协议》",
                UITypography.FontSnapped(22f), ComicStyle.Ink, false, TextAlignmentOptions.Left);
            var checkLabelRt = checkLabel.rectTransform;
            checkLabelRt.anchorMin = new Vector2(0f, 0.5f);
            checkLabelRt.anchorMax = new Vector2(1f, 0.5f);
            checkLabelRt.pivot = new Vector2(0f, 0.5f);
            checkLabelRt.anchoredPosition = new Vector2(58f, 0f);
            checkLabelRt.sizeDelta = new Vector2(-58f, 44f);
            checkLabel.raycastTarget = false;

            // 底部按钮：同意（绿） / 不同意（白）。
            _agreeBtn = CreateButton("AgreeBtn", panelGo.transform, "同意并继续",
                new Vector2(-220f, -168f), new Vector2(400f, 72f),
                ComicStyle.PaperAlt, ComicStyle.WithAlpha(ComicStyle.Ink, 0.45f),
                UITypography.FontSnapped(26f), out _agreeLabel);
            _agreeShape = _agreeBtn.GetComponent<UIInkShapeGraphic>();
            _agreeBtn.interactable = false;
            _agreeBtn.onClick.AddListener(CloseAndAccept);

            var declineBtn = CreateButton("DeclineBtn", panelGo.transform, "不同意并退出",
                new Vector2(220f, -168f), new Vector2(400f, 72f),
                ComicStyle.Paper, ComicStyle.Ink, UITypography.FontSnapped(26f),
                out _);
            declineBtn.onClick.AddListener(DeclineAndQuit);

            ApplyResponsiveScale();
            _root.SetActive(false);
        }

        private static string BuildBodyText()
        {
            return "欢迎游玩《墨水大作战》。\n\n"
                 + "为让你正常游玩并获取版本更新，我们会在你同意后处理必要信息：\n"
                 + "· 游戏设置、触控布局与隐私同意状态仅保存在本机；\n"
                 + "· 仅当你主动点击“检查更新”时，才会访问 Gitee / GitHub 获取版本信息；\n"
                 + "· 本游戏不收集账号、手机号、位置、通讯录、相册、麦克风或摄像头信息；\n"
                 + "· 本游戏不接入广告、统计分析或内购 SDK；\n"
                 + "· 你可以通过系统“清除应用数据”或卸载游戏删除本地信息。\n\n"
                 + "请阅读并同意《隐私政策》和《用户协议》后继续。";
        }

        private void ApplyResponsiveScale()
        {
            if (_panelRect == null) return;
            float safeRatio = Screen.height > 0 ? Screen.safeArea.height / Screen.height : 1f;
            float available = 1080f * safeRatio;
            const float targetHeight = 780f;
            float scale = available < targetHeight ? Mathf.Clamp(available / targetHeight, 0.55f, 1f) : 1f;
            _panelRect.localScale = new Vector3(scale, scale, 1f);
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize,
            Color color, bool display, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;

            TMP_FontAsset font = Theme.ResolveFont(display, text);
            if (font == null) font = TMP_Settings.defaultFontAsset;
            tmp.font = font;
            return tmp;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Color bgColor, Color textColor, float fontSize, out TextMeshProUGUI labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
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
            jelly.EnableThemeHardShadow();

            labelText = CreateText("Label", go.transform, label, fontSize, textColor, true);
            Stretch(labelText.rectTransform);
            return btn;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}