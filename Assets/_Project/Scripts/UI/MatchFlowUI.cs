using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 比赛全流程界面控制器（P4-D 重构升级）：
    /// 1. 赛前 Ready 界面（潮流圆角大按钮 + 键盘一键开赛）；
    /// 2. 开局 3-2-1 弹性大字与 READY / GO 墨水炸开全屏横幅（MatchBannerView）；
    /// 3. 1:00 决战横幅与 FINISH 终局定格横幅；
    /// 4. 结算界面（Battle Judgement）：
    ///    - 双方墨水柱爆发对决（圆角对撞槽）；
    ///    - 百分比数字 Cubic-easeout 滚动上升；
    ///    - VICTORY / DEFEAT 胜负徽章弹性弹入；
    ///    - 一键再来一局街头果冻按钮。
    /// 保持所有原有 public 签名与 BindReferences 向后兼容。
    /// </summary>
    public class MatchFlowUI : MonoBehaviour
    {
        [Header("赛前面板")]
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Button startButton;
        [SerializeField] private Text startButtonLabel;

        [Header("版本与检查更新")]
        [SerializeField] private Button checkUpdateButton;
        [SerializeField] private Text versionText;

        public Button CheckUpdateButton => checkUpdateButton;
        public Text VersionText => versionText;

        [Header("既有简单文本引用（保留向后兼容）")]
        [SerializeField] private Text countdownText;
        [SerializeField] private Text finishText;

        [Header("全屏流程横幅系统（P4-D）")]
        [SerializeField] private MatchBannerView bannerView;
        [SerializeField] private UIThemeSO uiTheme;

        [Header("结算面板")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitleText;
        [SerializeField] private Image resultBarA;
        [SerializeField] private Image resultBarB;
        [SerializeField] private Text resultPercentA;
        [SerializeField] private Text resultPercentB;
        [SerializeField] private Text verdictText;
        [SerializeField] private Button restartButton;

        [Header("结算精致化徽章与效果（P4-D）")]
        [SerializeField] private RectTransform victoryBadgeRoot;
        [SerializeField] private UIInkShapeGraphic victoryBadgeBg;
        [SerializeField] private TMP_Text victoryBadgeText;
        [SerializeField] private UISpringMotion victoryBadgeMotion;
        [SerializeField] private UIBurstGraphic victoryBurst;

        private RectTransform _contentRoot;
        private float _resultElapsed;
        private MatchManager.Phase _lastPhase = (MatchManager.Phase)(-1);
        private int _lastCountdownNumber = -1;
        private bool _oneMinuteTriggered;
        private bool _badgePlayed;
        private bool _numbersSettled;

        public MatchBannerView BannerView
        {
            get
            {
                EnsureBannerView();
                return bannerView;
            }
        }

        public void BindReferences(
            GameObject ready, Text title, Text subtitle, Button start, Text startLabel,
            Text countdown, Text finish,
            GameObject result, Text resultTitle,
            Image barA, Image barB, Text percentA, Text percentB, Text verdict, Button restart)
        {
            readyPanel = ready;
            titleText = title;
            subtitleText = subtitle;
            startButton = start;
            startButtonLabel = startLabel;
            countdownText = countdown;
            finishText = finish;
            resultPanel = result;
            resultTitleText = resultTitle;
            resultBarA = barA;
            resultBarB = barB;
            resultPercentA = percentA;
            resultPercentB = percentB;
            verdictText = verdict;
            restartButton = restart;

            EnsureBannerView();
        }

        private void Awake()
        {
            EnsureSafeAreaRoot();
            EnsureBannerView();
            EnsureResultVisuals();
            EnsureUpdateUI();
            EnsureReadyVisuals();

            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (restartButton != null) restartButton.onClick.AddListener(OnStartClicked);
        }

        /// <summary>运行时内容根：SafeArea 容器，桌面/编辑器下等价于全屏。</summary>
        private Transform ContentRoot => _contentRoot != null ? (Transform)_contentRoot : transform;

        /// <summary>
        /// 主菜单/结算画布原先完全没有安全区适配：横屏刘海机会切掉
        /// 右上角墨渍装饰、右下角版本号与次级按钮行。
        /// 运行时新建拉伸容器把既有面板整体搬进去，桌面端视觉零变化。
        /// </summary>
        private void EnsureSafeAreaRoot()
        {
            if (_contentRoot != null) return;

            var safeGo = new GameObject("MatchFlowSafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);

            var safeRt = safeGo.GetComponent<RectTransform>();
            safeRt.anchorMin = Vector2.zero;
            safeRt.anchorMax = Vector2.one;
            safeRt.offsetMin = Vector2.zero;
            safeRt.offsetMax = Vector2.zero;
            safeGo.AddComponent<SafeAreaFitter>();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child != safeGo.transform)
                {
                    child.SetParent(safeRt, false);
                }
            }

            _contentRoot = safeRt;
        }

        /// <summary>
        /// 主菜单（P11 重做）：一张不透明的漫画封面，完全遮住 3D 场景，点「开始比赛」才进对局。
        /// 结构：纸面网点底 + 队伍色墨渍 + 放射动作线 + 倾斜标题贴纸（墨水大作战）
        ///       + 大号黄色开赛按钮 + 底部次级按钮行 + 右下角版本号。
        /// 场景里的旧 Text/Image 由 <c>ProjectScaffolder</c> 烘焙，这里只做运行时「换皮」，
        /// 不改场景文件，也不破坏 ReadyPanel 原有的遮罩与按钮引用。
        /// </summary>
        private void EnsureReadyVisuals()
        {
            if (readyPanel == null) return;
            if (readyPanel.transform.Find("ComicMenuRoot") != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            // 移动端整体放大一档：参考分辨率 1920×1080 在 19.5:9 手机上物理尺寸偏小。
            // 判定只看触摸输入（真机或编辑器模拟），不再用 aspect>=1.9 猜——
            // 21:9 带鱼屏桌面曾被误判成手机，16:10 平板反而吃不到放大。
            bool mobile = Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;

            float logoY = mobile ? 150f : 170f;
            float chipY = mobile ? 170f : 185f;
            float stickerW = mobile ? 1100f : 920f;
            float stickerH = mobile ? 300f : 250f;
            float titleSize = mobile ? 158f : 132f;
            float latinY = mobile ? -180f : -155f;
            float latinSize = mobile ? 32f : 28f;
            Vector2 startPos = new Vector2(0f, mobile ? -160f : -150f);
            Vector2 startSize = mobile ? new Vector2(600f, 170f) : new Vector2(560f, 140f);
            float startFont = mobile ? 66f : 58f;
            float hintY = mobile ? -275f : -245f;
            float hintFont = mobile ? 28f : 24f;
            float secX = mobile ? 150f : 130f;
            float secY = mobile ? -370f : -330f;
            Vector2 secSize = mobile ? new Vector2(260f, 80f) : new Vector2(224f, 68f);
            float secFont = mobile ? 28f : 24f;

            // 1) 全屏底：不透明纸面（主菜单不能透出 3D 场景）
            var overlay = readyPanel.GetComponent<Image>();
            if (overlay != null)
            {
                overlay.color = resolved.paperColor;
            }

            // 背景容器：网点 / 墨渍 / Logo 都压在按钮层之下
            var menuRootGo = new GameObject("ComicMenuRoot", typeof(RectTransform));
            menuRootGo.transform.SetParent(readyPanel.transform, false);
            var menuRoot = (RectTransform)menuRootGo.transform;
            menuRoot.anchorMin = Vector2.zero;
            menuRoot.anchorMax = Vector2.one;
            menuRoot.offsetMin = Vector2.zero;
            menuRoot.offsetMax = Vector2.zero;
            menuRoot.SetAsFirstSibling();

            // 1.1) 半调网点印刷底
            var halfGo = new GameObject("ComicHalftone",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicHalftoneGraphic));
            halfGo.transform.SetParent(menuRoot, false);
            Stretch(halfGo.GetComponent<RectTransform>());
            var halftone = halfGo.GetComponent<ComicHalftoneGraphic>();
            halftone.color = ComicStyle.Ink;
            halftone.Alpha = 0.07f;
            halftone.Spacing = 14f;
            halftone.raycastTarget = false;

            // 1.2) 队伍色墨渍：右上橙、左下蓝，给纯纸面一点品牌色
            CreateDecorBlob(menuRoot, "BlobOrange", resolved.GetTeamColor(Team.TeamA),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -70f), new Vector2(520f, 420f));
            CreateDecorBlob(menuRoot, "BlobBlue", resolved.GetTeamColor(Team.TeamB),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(60f, 80f), new Vector2(560f, 460f));

            // 2) Logo 群组：动作线 + 顶部标签 + 倾斜标题贴纸 + 拉丁副题
            var logoGo = new GameObject("LogoRoot", typeof(RectTransform));
            logoGo.transform.SetParent(menuRoot, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = new Vector2(0.5f, 0.5f);
            logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.anchoredPosition = new Vector2(0f, logoY);
            logoRect.sizeDelta = new Vector2(1500f, 700f);

            // 2.1) 放射动作线（漫画速度线，压在贴纸后面）
            var linesGo = new GameObject("ActionLines",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicActionLinesGraphic));
            linesGo.transform.SetParent(logoRect, false);
            Stretch(linesGo.GetComponent<RectTransform>());
            var lines = linesGo.GetComponent<ComicActionLinesGraphic>();
            lines.color = ComicStyle.Ink;
            lines.Alpha = 0.09f;
            lines.RayCount = 28;
            lines.raycastTarget = false;

            // 2.2) 顶部小标签「4v4 涂地对战」
            Vector2 chipSize = new Vector2(mobile ? 380f : 330f, mobile ? 62f : 56f);
            var chipShape = CreateComicPanel(logoRect, "OverlineChip",
                new Vector2(0f, chipY), chipSize, ComicStyle.Ink, 1.5f);
            chipShape.BorderWidth = ComicStyle.BorderThin;
            chipShape.HardShadow = true;
            chipShape.ShadowOffset = new Vector2(4f, -4f);
            CreateComicTmp(chipShape.transform, "ChipText", "4v4 涂地对战", mobile ? 28f : 25f, false,
                ComicStyle.Paper, Vector2.zero, new Vector2(chipSize.x - 24f, chipSize.y - 8f));

            // 2.3) 标题贴纸：纸面圆角块 + 墨线硬阴影，整体 -2° 微倾
            var sticker = CreateComicPanel(logoRect, "TitleSticker",
                Vector2.zero, new Vector2(stickerW, stickerH), ComicStyle.Paper, -2f);
            sticker.BorderWidth = ComicStyle.BorderHeavy;
            sticker.HardShadow = true;
            sticker.ShadowOffset = new Vector2(10f, -10f);
            sticker.Halftone = true;
            sticker.HalftoneAlpha = ComicStyle.HalftoneAlpha;

            CreateComicTmp(sticker.transform, "ComicTitle", "墨水大作战", titleSize, true,
                resolved.GetTeamColor(Team.TeamA), Vector2.zero,
                new Vector2(stickerW - 60f, stickerH - 40f));

            // 2.4) 拉丁副题
            var latin = CreateComicTmp(logoRect, "LatinSub", "INK TURF WAR", latinSize, true,
                ComicStyle.Ink, new Vector2(0f, latinY), new Vector2(700f, 44f), 2f);

            // 3) 隐藏旧场景标题，旧浅色文字统一压成墨色
            if (titleText != null) titleText.gameObject.SetActive(false);
            if (subtitleText != null) subtitleText.gameObject.SetActive(false);

            foreach (var legacy in readyPanel.GetComponentsInChildren<Text>(true))
            {
                if (legacy == null) continue;
                Color c = legacy.color;
                if (c.r + c.g + c.b > 1.5f)
                {
                    legacy.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.8f);
                }
            }

            // 4) 开始按钮：加大下移（旧场景里它被标题气泡盖住，这次明确挪到 Logo 下方独立位置）
            if (startButton != null)
            {
                var btnRect = startButton.transform as RectTransform;
                if (btnRect != null)
                {
                    btnRect.anchorMin = new Vector2(0.5f, 0.5f);
                    btnRect.anchorMax = new Vector2(0.5f, 0.5f);
                    btnRect.anchoredPosition = startPos;
                    btnRect.sizeDelta = startSize;
                }

                try
                {
                    StyleStartButton(resolved, startFont);
                }
                catch (System.Exception ex)
                {
                    // 纯装饰失败不该让整局游戏起不来：记 warning（不触发 PlayMode 的 log-error 门禁）
                    Debug.LogWarning("[MatchFlowUI] 开赛按钮漫画换肤失败：" + ex);
                }
            }

            // 5) 键盘提示行（旧 StartHint 重排到按钮下方；场景缺失时补建）
            var hintTrans = readyPanel.transform.Find("StartHint");
            if (hintTrans == null)
            {
                var hintGo = new GameObject("StartHint", typeof(RectTransform), typeof(Text));
                hintGo.transform.SetParent(readyPanel.transform, false);
                hintTrans = hintGo.transform;
                var hintText = hintGo.GetComponent<Text>();
                hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                hintText.text = "也可以按 空格 / 回车 开始";
                hintText.alignment = TextAnchor.MiddleCenter;
            }
            var hintRect = (RectTransform)hintTrans;
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.anchoredPosition = new Vector2(0f, hintY);
            hintRect.sizeDelta = new Vector2(760f, 44f);
            var hintLabel = hintTrans.GetComponent<Text>();
            if (hintLabel != null)
            {
                hintLabel.fontSize = Mathf.RoundToInt(hintFont);
                hintLabel.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.62f);
            }

            // 触屏设备上没有键盘，「按空格/回车开始」是无意义提示，直接隐藏
            if (mobile) hintTrans.gameObject.SetActive(false);

            // 6) 次要按钮行：检查更新 / 自定义按键，纸面漫画小方块
            StyleSecondaryButton("CheckUpdateButton", new Vector2(-secX, secY), secSize, secFont, "检查更新");
            StyleSecondaryButton("CustomLayoutButton", new Vector2(secX, secY), secSize, secFont, "自定义按键");

            // 7) 版本号：右下角小字
            if (versionText != null)
            {
                var verRect = versionText.transform as RectTransform;
                if (verRect != null)
                {
                    verRect.anchorMin = new Vector2(1f, 0f);
                    verRect.anchorMax = new Vector2(1f, 0f);
                    verRect.pivot = new Vector2(1f, 0f);
                    verRect.anchoredPosition = new Vector2(-20f, 16f);
                    verRect.sizeDelta = new Vector2(560f, 32f);
                }
                versionText.fontSize = mobile ? 22 : 20;
                versionText.alignment = TextAnchor.MiddleRight;
                versionText.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.55f);
            }
        }

        /// <summary>开赛按钮：黄底墨线硬阴影 + 按压反馈；根 Image 保留为透明射线目标。</summary>
        private void StyleStartButton(UIThemeSO resolved, float fontSize)
        {
            var graphic = startButton.targetGraphic as Image;
            if (graphic != null)
            {
                // Image 保留为全透明：它仍是 Button 的射线目标（关掉就点不动了）
                graphic.color = new Color(1f, 1f, 1f, 0f);
                graphic.raycastTarget = true;
                graphic.enabled = true;
            }

            // 同一物体已有 Image（Graphic 不允许重复），漫画面板铺在子物体上
            var btnShape = startButton.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (btnShape == null)
            {
                btnShape = UIInkShapeGraphic.CreateChildBackground(
                    startButton.transform as RectTransform, "ComicButtonBg");
            }

            if (btnShape == null)
            {
                Debug.LogWarning("[MatchFlowUI] 开赛按钮无法挂载 UIInkShapeGraphic（跳过换肤）");
                return;
            }

            btnShape.Shape = UIInkShape.RoundedRect;
            btnShape.CornerRadius = ComicStyle.CornerSharp;
            btnShape.BorderWidth = ComicStyle.BorderHeavy;
            btnShape.BorderColor = ComicStyle.Ink;
            btnShape.HardShadow = true;
            btnShape.ShadowOffset = new Vector2(10f, -10f);
            btnShape.ShadowColor = ComicStyle.Ink;
            btnShape.Halftone = true;
            btnShape.HalftoneAlpha = ComicStyle.HalftoneAlpha;
            btnShape.GlossAlpha = 0f;
            btnShape.color = ComicStyle.Yellow;
            btnShape.raycastTarget = true;

            var label = startButtonLabel != null ? startButtonLabel : startButton.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = ComicStyle.Ink;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = Mathf.RoundToInt(fontSize);
            }

            var jelly = startButton.gameObject.GetComponent<UIJellyButton>();
            if (jelly == null) jelly = startButton.gameObject.AddComponent<UIJellyButton>();
            if (jelly != null) jelly.Configure(resolved, startButton, btnShape);
        }

        /// <summary>
        /// 次要按钮（检查更新 / 自定义按键）：纸面 + 墨线 + 硬阴影的漫画小方块，
        /// 与主按钮拉开层级（黄 = 主行动，纸 = 次行动）。
        /// </summary>
        private void StyleSecondaryButton(string btnName, Vector2 pos, Vector2 size, float fontSize, string label)
        {
            Transform t = readyPanel.transform.Find(btnName);
            if (t == null) return;

            var rect = t as RectTransform;
            if (rect == null) rect = t.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                rect.sizeDelta = size;
            }

            var img = t.GetComponent<Image>();
            if (img != null)
            {
                // 根 Image 留作射线目标，外观交给子物体漫画面板
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            var shape = t.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (shape == null)
            {
                shape = UIInkShapeGraphic.CreateChildBackground(rect, "ComicBg");
            }
            if (shape != null)
            {
                shape.Shape = UIInkShape.RoundedRect;
                shape.CornerRadius = ComicStyle.CornerSharp;
                shape.BorderWidth = ComicStyle.Border;
                shape.BorderColor = ComicStyle.Ink;
                shape.HardShadow = true;
                shape.ShadowOffset = new Vector2(5f, -5f);
                shape.ShadowColor = ComicStyle.Ink;
                shape.Halftone = false;
                shape.GlossAlpha = 0f;
                shape.color = ComicStyle.Paper;
                shape.raycastTarget = false;
            }

            var labelText = t.GetComponentInChildren<Text>(true);
            if (labelText != null)
            {
                labelText.text = label;
                labelText.color = ComicStyle.Ink;
                labelText.fontSize = Mathf.RoundToInt(fontSize);
                labelText.fontStyle = FontStyle.Bold;
                labelText.alignment = TextAnchor.MiddleCenter;
            }

            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                var jelly = t.GetComponent<UIJellyButton>();
                if (jelly == null) jelly = t.gameObject.AddComponent<UIJellyButton>();
                jelly.Configure(UIThemeSO.Resolve(uiTheme), btn, shape);
            }
        }

        /// <summary>程序化漫画面板：圆角块 + 墨线，返回形状组件供调用方继续调边框/阴影。</summary>
        private UIInkShapeGraphic CreateComicPanel(Transform parent, string name,
            Vector2 pos, Vector2 size, Color fill, float tiltDegrees)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, tiltDegrees);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.raycastTarget = false;
            return shape;
        }

        /// <summary>队伍色墨渍装饰：无边的 Blob 形，低透明度平铺在纸面上。</summary>
        private void CreateDecorBlob(Transform parent, string blobName, Color color,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(blobName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var blob = go.GetComponent<UIInkShapeGraphic>();
            blob.Shape = UIInkShape.Blob;
            blob.BorderWidth = 0f;
            blob.HardShadow = false;
            blob.Halftone = false;
            blob.GlossAlpha = 0f;
            blob.color = new Color(color.r, color.g, color.b, 0.14f);
            blob.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI CreateComicTmp(Transform parent, string name, string text, float size,
            bool display, Color color, Vector2 pos, Vector2 box, float charSpacing = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = box;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.characterSpacing = charSpacing;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = color;
            tmp.raycastTarget = false;

            var font = UIThemeSO.Resolve(uiTheme).ResolveFont(display, text);
            if (font != null) tmp.font = font;

            var chunky = go.AddComponent<UIChunkyText>();
            // 大字号中文标题不做描边（会糊），靠双层错位形成印刷阴影
            chunky.Configure(UIThemeSO.Resolve(uiTheme), text, size, display, true, color, false);
            return tmp;
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
            if (restartButton != null) restartButton.onClick.RemoveListener(OnStartClicked);
            if (checkUpdateButton != null) checkUpdateButton.onClick.RemoveListener(OnCheckUpdateClicked);
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null)
            {
                uiTheme = themeAsset;
                if (bannerView != null) bannerView.SetTheme(uiTheme);
            }
        }

        public void EnsureBannerView()
        {
            if (bannerView == null) bannerView = GetComponentInChildren<MatchBannerView>(true);

            if (bannerView == null)
            {
                var bannerGo = new GameObject("MatchBannerView", typeof(RectTransform), typeof(MatchBannerView));
                bannerGo.transform.SetParent(ContentRoot, false);
                bannerView = bannerGo.GetComponent<MatchBannerView>();
                bannerView.SetTheme(uiTheme);
                bannerView.EnsureBuilt();
            }
        }

        private void EnsureResultVisuals()
        {
            if (resultPanel == null || victoryBadgeRoot != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            // 结算页漫画网点底（全屏印刷质感）
            Transform existingHalf = resultPanel.transform.Find("HalftoneBg");
            if (existingHalf == null)
            {
                var hGo = new GameObject("HalftoneBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicHalftoneGraphic));
                hGo.transform.SetParent(resultPanel.transform, false);
                hGo.transform.SetAsFirstSibling();
                var hRt = (RectTransform)hGo.transform;
                hRt.anchorMin = Vector2.zero;
                hRt.anchorMax = Vector2.one;
                hRt.offsetMin = Vector2.zero;
                hRt.offsetMax = Vector2.zero;
                var half = hGo.GetComponent<ComicHalftoneGraphic>();
                half.color = ComicStyle.Ink;
                half.Alpha = 0.10f;
                half.Spacing = 14f;
                half.raycastTarget = false;
            }

            var badgeGo = new GameObject("VictoryBadge", typeof(RectTransform), typeof(UISpringMotion));
            badgeGo.transform.SetParent(resultPanel.transform, false);
            victoryBadgeRoot = badgeGo.GetComponent<RectTransform>();
            victoryBadgeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.pivot = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchoredPosition = new Vector2(0f, -60f);
            victoryBadgeRoot.sizeDelta = new Vector2(480f, 120f);

            victoryBadgeMotion = badgeGo.GetComponent<UISpringMotion>();
            victoryBadgeMotion.SetTheme(resolved);

            var burstGo = new GameObject("Burst", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIBurstGraphic));
            burstGo.transform.SetParent(victoryBadgeRoot, false);
            var burstRect = (RectTransform)burstGo.transform;
            burstRect.anchorMin = Vector2.zero;
            burstRect.anchorMax = Vector2.one;
            burstRect.sizeDelta = Vector2.zero;
            victoryBurst = burstGo.GetComponent<UIBurstGraphic>();
            victoryBurst.Configure(0.9f, 16, ComicStyle.Yellow);
            victoryBurst.raycastTarget = false;

            var bgGo = new GameObject("BadgeBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(victoryBadgeRoot, false);
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            victoryBadgeBg = bgGo.GetComponent<UIInkShapeGraphic>();
            victoryBadgeBg.Shape = UIInkShape.RoundedRect;
            victoryBadgeBg.CornerRadius = ComicStyle.CornerSharp;
            victoryBadgeBg.BorderWidth = ComicStyle.BorderHeavy;
            victoryBadgeBg.BorderColor = ComicStyle.Ink;
            victoryBadgeBg.HardShadow = true;
            victoryBadgeBg.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            victoryBadgeBg.ShadowColor = ComicStyle.Ink;
            victoryBadgeBg.Halftone = true;
            victoryBadgeBg.HalftoneAlpha = 0.14f;
            victoryBadgeBg.color = ComicStyle.Yellow;
            victoryBadgeBg.raycastTarget = false;

            var txtGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(UIChunkyText));
            txtGo.transform.SetParent(victoryBadgeRoot, false);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.sizeDelta = Vector2.zero;

            victoryBadgeText = txtGo.GetComponent<TextMeshProUGUI>();
            victoryBadgeText.fontSize = 58f;
            victoryBadgeText.fontStyle = FontStyles.Bold;
            victoryBadgeText.alignment = TextAlignmentOptions.Center;
            victoryBadgeText.color = ComicStyle.Ink;
            victoryBadgeText.text = "VICTORY!";
            victoryBadgeText.raycastTarget = false;

            var chunky = txtGo.GetComponent<UIChunkyText>();
            chunky.Configure(resolved, "VICTORY!", 58f, displayFont: true, doubleLayered: true, fill: ComicStyle.Yellow, outlinedText: true);

            victoryBadgeRoot.gameObject.SetActive(false);
        }

        public void EnsureUpdateUI()
        {
            if (readyPanel == null) return;

            if (checkUpdateButton == null)
            {
                var existingBtn = readyPanel.transform.Find("CheckUpdateButton");
                if (existingBtn != null) checkUpdateButton = existingBtn.GetComponent<Button>();
            }

            if (versionText == null)
            {
                var existingVer = readyPanel.transform.Find("VersionText");
                if (existingVer != null) versionText = existingVer.GetComponent<Text>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 动态创建 VersionText
            if (versionText == null)
            {
                var verGo = new GameObject("VersionText", typeof(RectTransform), typeof(Text));
                verGo.transform.SetParent(readyPanel.transform, false);
                var verRt = verGo.GetComponent<RectTransform>();
                verRt.anchorMin = new Vector2(0.5f, 0f);
                verRt.anchorMax = new Vector2(0.5f, 0f);
                verRt.anchoredPosition = new Vector2(0f, 95f);
                verRt.sizeDelta = new Vector2(400f, 32f);

                versionText = verGo.GetComponent<Text>();
                versionText.font = font;
                versionText.fontSize = 20;
                versionText.alignment = TextAnchor.MiddleCenter;
                versionText.color = new Color(0.65f, 0.72f, 0.85f, 0.9f);
            }
            if (versionText != null)
            {
                versionText.text = $"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}";
            }

            // 动态创建 CheckUpdateButton 与 CustomLayoutButton
            if (checkUpdateButton == null)
            {
                var btnGo = new GameObject("CheckUpdateButton", typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(readyPanel.transform, false);
                var btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.5f, 0f);
                btnRt.anchorMax = new Vector2(0.5f, 0f);
                btnRt.anchoredPosition = new Vector2(-115f, 48f);
                btnRt.sizeDelta = new Vector2(190f, 44f);

                var img = btnGo.GetComponent<Image>();
                img.color = new Color(0.18f, 0.22f, 0.30f, 0.92f);

                checkUpdateButton = btnGo.GetComponent<Button>();
                checkUpdateButton.targetGraphic = img;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(btnGo.transform, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;

                var labelText = labelGo.GetComponent<Text>();
                labelText.font = font;
                labelText.fontSize = 18;
                labelText.fontStyle = FontStyle.Bold;
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.color = new Color(0.85f, 0.92f, 1f, 1f);
                labelText.text = "检 查 更 新";
            }

            Transform layoutBtnTrans = readyPanel.transform.Find("CustomLayoutButton");
            if (layoutBtnTrans == null)
            {
                var lbtnGo = new GameObject("CustomLayoutButton", typeof(RectTransform), typeof(Image), typeof(Button));
                lbtnGo.transform.SetParent(readyPanel.transform, false);
                var lbtnRt = lbtnGo.GetComponent<RectTransform>();
                lbtnRt.anchorMin = new Vector2(0.5f, 0f);
                lbtnRt.anchorMax = new Vector2(0.5f, 0f);
                lbtnRt.anchoredPosition = new Vector2(115f, 48f);
                lbtnRt.sizeDelta = new Vector2(190f, 44f);

                var limg = lbtnGo.GetComponent<Image>();
                limg.color = new Color(0.24f, 0.28f, 0.38f, 0.92f);

                var lbtn = lbtnGo.GetComponent<Button>();
                lbtn.targetGraphic = limg;
                lbtn.onClick.AddListener(() =>
                {
                    var touchUI = FindAnyObjectByType<MobileTouchUI>();
                    if (touchUI != null) touchUI.OpenLayoutCustomizer();
                });

                var llabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                llabelGo.transform.SetParent(lbtnGo.transform, false);
                var llabelRt = llabelGo.GetComponent<RectTransform>();
                llabelRt.anchorMin = Vector2.zero;
                llabelRt.anchorMax = Vector2.one;
                llabelRt.offsetMin = Vector2.zero;
                llabelRt.offsetMax = Vector2.zero;

                var llabelText = llabelGo.GetComponent<Text>();
                llabelText.font = font;
                llabelText.fontSize = 18;
                llabelText.fontStyle = FontStyle.Bold;
                llabelText.alignment = TextAnchor.MiddleCenter;
                llabelText.color = new Color(1f, 0.85f, 0.28f, 1f);
                llabelText.text = "⚙️ 自定义键位";

                // P9：桌面/非触控模式下隐藏自定义键位入口，避免出现点击无响应的死按钮
                if (!Application.isMobilePlatform && !MobileTouchUI.IsTouchSimulationActive)
                {
                    lbtnGo.SetActive(false);
                }
            }

            if (checkUpdateButton != null)
            {
                checkUpdateButton.onClick.RemoveListener(OnCheckUpdateClicked);
                checkUpdateButton.onClick.AddListener(OnCheckUpdateClicked);
            }
        }

        private void OnCheckUpdateClicked()
        {
            UpdateManager.Instance?.CheckForUpdate(manual: true);
        }

        private void OnStartClicked()
        {
            MatchManager.Instance?.StartMatch();
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            if (match == null) return;

            // 快捷键重开（触屏设备跳过：连外接键盘时避免误触重开）
            if (!Application.isMobilePlatform && !MobileTouchUI.IsTouchSimulationActive &&
                (match.CurrentPhase == MatchManager.Phase.Ready || match.CurrentPhase == MatchManager.Phase.Result))
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame))
                {
                    match.StartMatch();
                }
            }

            // 阶段切换触发
            if (match.CurrentPhase != _lastPhase)
            {
                _lastPhase = match.CurrentPhase;
                HandlePhaseChanged(match);
            }

            if (match.CurrentPhase == MatchManager.Phase.Playing)
            {
                // 1:00 决战时刻横幅
                if (!_oneMinuteTriggered && match.RemainingSeconds <= 60f)
                {
                    _oneMinuteTriggered = true;
                    if (bannerView != null) bannerView.ShowOneMinute();
                }
            }

            if (match.CurrentPhase == MatchManager.Phase.Result && resultPanel != null && resultPanel.activeSelf)
            {
                _resultElapsed += Time.deltaTime;
            }

            ApplyPhase(match);
        }

        private void HandlePhaseChanged(MatchManager match)
        {
            var phase = match.CurrentPhase;
            if (phase == MatchManager.Phase.Countdown)
            {
                _lastCountdownNumber = -1;
                _oneMinuteTriggered = false;
                _badgePlayed = false;
                _numbersSettled = false;
                if (victoryBadgeRoot != null) victoryBadgeRoot.gameObject.SetActive(false);
            }
            else if (phase == MatchManager.Phase.Playing)
            {
                if (bannerView != null && bannerView.CurrentType == MatchBannerType.Countdown)
                {
                    bannerView.ShowGo();
                }
            }
            else if (phase == MatchManager.Phase.Finished)
            {
                if (bannerView != null)
                {
                    bannerView.ShowFinish();
                }
            }
            else if (phase == MatchManager.Phase.Result)
            {
                _resultElapsed = 0f;
                _badgePlayed = false;
                _numbersSettled = false;
                if (bannerView != null) bannerView.HideImmediate();
                PlayResultSound(match);
            }
        }

        private void ApplyPhase(MatchManager match)
        {
            var phase = match.CurrentPhase;

            if (readyPanel != null) readyPanel.SetActive(phase == MatchManager.Phase.Ready);
            if (resultPanel != null) resultPanel.SetActive(phase == MatchManager.Phase.Result);

            // 倒计时处理
            if (phase == MatchManager.Phase.Countdown)
            {
                int n = match.CountdownNumber;
                if (n != _lastCountdownNumber)
                {
                    _lastCountdownNumber = n;
                    if (bannerView != null)
                    {
                        bannerView.ShowCountdown(n);
                    }
                }
            }

            // 保持旧 Text 兼容
            if (countdownText != null)
            {
                bool showCountdown = phase == MatchManager.Phase.Countdown && bannerView == null;
                countdownText.gameObject.SetActive(showCountdown);
                if (showCountdown)
                {
                    int n = match.CountdownNumber;
                    countdownText.text = n > 0 ? n.ToString() : "GO!";
                }
            }

            if (finishText != null)
            {
                bool showFinish = phase == MatchManager.Phase.Finished && bannerView == null;
                finishText.gameObject.SetActive(showFinish);
            }

            if (phase == MatchManager.Phase.Result)
            {
                UpdateResultNumbers(match);
            }
        }

        private void UpdateResultNumbers(MatchManager match)
        {
            if (_numbersSettled) return;

            // Cubic-easeout 滚动算法
            float rollDuration = Mathf.Max(0.01f, match.ResultRollSeconds);
            float rollProgress = Mathf.Clamp01(_resultElapsed / rollDuration);
            float eased = 1f - Mathf.Pow(1f - rollProgress, 3f);

            float a = match.FinalCoverageA * eased;
            float b = match.FinalCoverageB * eased;

            if (resultPercentA != null) resultPercentA.text = $"{a:F1}%";
            if (resultPercentB != null) resultPercentB.text = $"{b:F1}%";
            if (resultBarA != null) resultBarA.fillAmount = Mathf.Clamp01(a / 100f);
            if (resultBarB != null) resultBarB.fillAmount = Mathf.Clamp01(b / 100f);

            if (verdictText != null)
            {
                if (rollProgress < 1f)
                {
                    verdictText.text = "正在计算全图涂地覆盖率...";
                    verdictText.color = ComicStyle.Paper;
                }
                else
                {
                    bool won = match.PlayerWon;
                    verdictText.text = won ? "【 胜 利 获 胜 】" : "惜 败 · 再 接 再 厉";
                    verdictText.color = won ? ComicStyle.Yellow : ComicStyle.Blue;

                    // 胜利徽章弹入
                    if (!_badgePlayed)
                    {
                        _badgePlayed = true;
                        TriggerVictoryBadge(won);
                    }
                }
            }

            if (rollProgress >= 1f)
            {
                if (resultPercentA != null) resultPercentA.text = $"{match.FinalCoverageA:F1}%";
                if (resultPercentB != null) resultPercentB.text = $"{match.FinalCoverageB:F1}%";
                if (resultBarA != null) resultBarA.fillAmount = Mathf.Clamp01(match.FinalCoverageA / 100f);
                if (resultBarB != null) resultBarB.fillAmount = Mathf.Clamp01(match.FinalCoverageB / 100f);
                _numbersSettled = true;
            }
        }

        private void TriggerVictoryBadge(bool playerWon)
        {
            if (victoryBadgeRoot == null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            if (victoryBadgeBg != null)
            {
                victoryBadgeBg.color = playerWon ? ComicStyle.Yellow : ComicStyle.Blue;
                victoryBadgeBg.BorderColor = ComicStyle.Ink;
                victoryBadgeBg.BorderWidth = ComicStyle.BorderHeavy;
            }

            if (victoryBadgeText != null)
            {
                string text = playerWon ? "VICTORY!" : "DEFEAT";
                victoryBadgeText.text = text;

                var chunky = victoryBadgeText.GetComponent<UIChunkyText>();
                if (chunky != null)
                {
                    chunky.Configure(resolved, text, 58f, displayFont: true, doubleLayered: true,
                        fill: playerWon ? ComicStyle.Yellow : ComicStyle.Blue, outlinedText: true);
                }
                else
                {
                    victoryBadgeText.color = playerWon ? ComicStyle.Ink : Color.white;
                }
            }

            if (playerWon && victoryBurst != null)
            {
                victoryBurst.Configure(0.9f, 16, ComicStyle.Yellow);
                victoryBurst.Play();
            }

            victoryBadgeRoot.gameObject.SetActive(true);
            if (victoryBadgeMotion != null)
            {
                victoryBadgeMotion.SnapHome();
                victoryBadgeMotion.PlayIn(0.05f);
            }
        }

        private void PlayResultSound(MatchManager match)
        {
            if (AudioManager.Instance == null) return;
            AudioClip clip = match.PlayerWon ? AudioManager.Instance.resultWinClip : AudioManager.Instance.resultLoseClip;
            if (clip != null)
            {
                AudioManager.Instance.PlayOne(clip, 0.85f, 1f, 1f);
            }
        }
    }
}
