using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 比赛全流程界面控制器（P14 几何大胆风 Geometric Bold 重构）：
    /// 1. 赛前 Ready 界面：
    ///    - 非对称几何构图：左侧 Hero 级大标题 + 拉丁副标题 (uppercase tracking-tight)；
    ///    - 右侧/下方错位纯色块（黑底白字 / 红底黑字 / 白底墨边）；
    ///    - 直角墨底白字超大开赛按钮（移动端自适应放大）；
    ///    - 四角 15~45° 旋转的几何装饰（直角方块、正圆、三角，纯色无网点无阴影）；
    /// 2. 流程横幅系统（MatchBannerView）：3-2-1 倒计时、READY/GO、1:00 决战与 FINISH 终局定格；
    /// 3. 结算界面（ResultPanel · 大数字即设计）：
    ///    - 顶部 Hero 级 VICTORY / DEFEAT 判定徽章（移动端 ≥120）；
    ///    - 双方涂地覆盖率超大百分比数字（桌面 ≥72，移动端 ≥96）分列左右队伍纯色大块；
    ///    - 直角纯色粗墨水柱；
    ///    - 关键 CTA「再来一局」红底白字直角大按钮 + 次级「返回」白底墨字直角按钮；
    /// 4. 彻底杜绝网点、渐变、动作线、气泡尾巴、硬阴影、有机 wobble 与弹簧过度回弹；
    /// 5. 所有字号严格走 UITypography，移动端 ×1.28 保证移动端字号 ≥ 桌面。
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

        [Header("结算精致化徽章与效果（P14 几何风）")]
        [SerializeField] private RectTransform victoryBadgeRoot;
        [SerializeField] private UIInkShapeGraphic victoryBadgeBg;
        [SerializeField] private TMP_Text victoryBadgeText;
        [SerializeField] private UISpringMotion victoryBadgeMotion;
        [SerializeField] private UIBurstGraphic victoryBurst;

        private RectTransform _contentRoot;
        private GameObject _fullscreenUnderlay;
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

            if (readyPanel != null)
            {
                var rt = (RectTransform)readyPanel.transform;
                Stretch(rt);
            }
            if (resultPanel != null)
            {
                var rt = (RectTransform)resultPanel.transform;
                Stretch(rt);
            }

            EnsureBannerView();
        }

        private void Awake()
        {
            EnsureTheme();
            EnsureFullscreenPaperUnderlay();
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
        /// 全屏绝对不透明纸面底板：在 Ready 与 Result 阶段激活，
        /// 100% 覆盖整个物理屏幕（不受 SafeArea 缩进影响），绝对遮蔽 3D 地图，彻底杜绝边缘漏景穿帮。
        /// </summary>
        private void EnsureFullscreenPaperUnderlay()
        {
            if (_fullscreenUnderlay != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);
            var underlayGo = new GameObject("FullscreenPaperUnderlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            underlayGo.transform.SetParent(transform, false);
            underlayGo.transform.SetAsFirstSibling();

            var rt = (RectTransform)underlayGo.transform;
            Stretch(rt);

            var img = underlayGo.GetComponent<Image>();
            img.color = resolved.paperColor;
            img.raycastTarget = false;

            _fullscreenUnderlay = underlayGo;
        }

        /// <summary>
        /// 主菜单/结算安全区容器：
        /// 全屏背景（ReadyPanel / ResultPanel）保持全屏铺满，
        /// 其他悬浮元素与横幅放入 SafeArea 容器，避免被横屏刘海或手势条切角。
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
                if (child != null && child != safeGo.transform && child.gameObject != _fullscreenUnderlay)
                {
                    // 全屏背景面板（ReadyPanel 与 ResultPanel）必须覆盖整个物理屏幕，绝不受安全区收缩影响，避免边缘露出 3D 场景
                    if (readyPanel != null && child.gameObject == readyPanel) continue;
                    if (resultPanel != null && child.gameObject == resultPanel) continue;
                    child.SetParent(safeRt, false);
                }
            }

            _contentRoot = safeRt;

            // 确保 ReadyPanel 与 ResultPanel 保持全屏拉伸
            if (readyPanel != null)
            {
                var rt = (RectTransform)readyPanel.transform;
                Stretch(rt);
            }
            if (resultPanel != null)
            {
                var rt = (RectTransform)resultPanel.transform;
                Stretch(rt);
            }
        }

        /// <summary>
        /// 主菜单（P14 几何大胆风 · P14-I 移动端重排）：
        /// 1. 内容全部放进 SafeArea（刘海/手势条不吃字），背景装饰留在全屏层；
        /// 2. 左上：模式标签；左侧：Hero 大标题 + 红色拉丁副标题 + 规格行；
        /// 3. 右下：黄色超大开赛按钮（移动端 820×200，拇指区）；上方右对齐两个次级按钮；
        /// 4. 左下预留「武器整备」整备卡位置（由 WeaponSelectView 绘制），版本号落在它上方；
        /// 5. 纯色几何装饰：左上 45° 墨方块、右缘红色大圆，无阴影无渐变。
        /// </summary>
        private void EnsureReadyVisuals()
        {
            if (readyPanel == null) return;
            EnsureTheme();
            if (readyPanel.transform.Find("GeometricMenuRoot") != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);
            bool mobile = UITypography.IsMobileLike;
            HomeMenuMetrics m = MenuLayoutMath.Home(mobile);

            // 1) 全屏底：纯白不透明纸面（禁止透出 3D 场景）
            var overlay = readyPanel.GetComponent<Image>();
            if (overlay != null)
            {
                overlay.color = ComicStyle.Paper;
            }

            // 2) 背景几何装饰根容器（全屏，在内容层之下）
            var menuRootGo = new GameObject("GeometricMenuRoot", typeof(RectTransform));
            menuRootGo.transform.SetParent(readyPanel.transform, false);
            var menuRoot = (RectTransform)menuRootGo.transform;
            Stretch(menuRoot);
            menuRoot.SetAsFirstSibling();

            // 左上 45° 墨方块（一半露出屏幕角，作为构图锚点）
            CreateGeometricSquare(menuRoot, "DecoSquareTL", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-m.DecoSquare * 0.12f, m.DecoSquare * 0.12f),
                new Vector2(m.DecoSquare, m.DecoSquare), 45f, ComicStyle.Ink);

            // 右缘大圆（红色强调，露出约一半）
            CreateGeometricCircle(menuRoot, "DecoCircleR", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(m.DecoCircle * 0.26f, 0f), new Vector2(m.DecoCircle, m.DecoCircle), ComicStyle.Red);

            // 3) 安全区内容容器（刘海屏左右不被切）
            //    注意顺序：必须先 Stretch 再挂 SafeAreaFitter。组件在 AddComponent 时就会
            //    OnEnable→Apply 写入 safeArea 的 anchor，若之后再 Stretch 会把 anchor 覆盖回
            //    全屏，菜单内容就会贴在屏幕物理边缘（横屏刘海区里面），SafeArea 等于白做。
            var safeGo = new GameObject("MenuSafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(readyPanel.transform, false);
            var safeRect = (RectTransform)safeGo.transform;
            Stretch(safeRect);
            safeGo.AddComponent<SafeAreaFitter>();

            ReparentInto(startButton != null ? startButton.transform : null, safeRect);
            ReparentInto(checkUpdateButton != null ? checkUpdateButton.transform : null, safeRect);
            ReparentInto(FindMenuNode("CustomLayoutButton"), safeRect);
            ReparentInto(versionText != null ? versionText.transform : null, safeRect);

            // 隐藏旧场景默认标题
            if (titleText != null) titleText.gameObject.SetActive(false);
            if (subtitleText != null) subtitleText.gameObject.SetActive(false);

            // 4) 左上模式标签（墨底白字直角块）
            var tagGo = new GameObject("ModeTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            tagGo.transform.SetParent(safeRect, false);
            var tagRect = tagGo.GetComponent<RectTransform>();
            tagRect.anchorMin = new Vector2(0f, 1f);
            tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = new Vector2(m.Margin, -m.Margin);
            tagRect.sizeDelta = m.TagSize;

            var tagShape = tagGo.GetComponent<UIInkShapeGraphic>();
            tagShape.Shape = UIInkShape.SharpRect;
            tagShape.CornerRadius = ComicStyle.CornerSharp;
            tagShape.BorderWidth = 0f;
            tagShape.color = ComicStyle.Ink;
            tagShape.HardShadow = false;
            tagShape.Halftone = false;
            tagShape.raycastTarget = false;

            var tagTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            tagTextGo.transform.SetParent(tagGo.transform, false);
            Stretch(tagTextGo.GetComponent<RectTransform>());
            var tagText = tagTextGo.GetComponent<TextMeshProUGUI>();
            tagText.font = resolved.ResolveFont(false, "4V4 涂地对战 · TURF WAR");
            tagText.fontSize = UITypography.FontSnapped(m.TagFont);
            tagText.fontStyle = FontStyles.Bold;
            tagText.alignment = TextAlignmentOptions.Center;
            tagText.color = ComicStyle.Paper;
            tagText.text = "4V4 涂地对战 · TURF WAR";
            tagText.textWrappingMode = TextWrappingModes.NoWrap;
            tagText.overflowMode = TextOverflowModes.Overflow;
            tagText.raycastTarget = false;

            // 5) Hero 标题群（左上，标签下方）
            var heroGo = new GameObject("HeroTitleGroup", typeof(RectTransform));
            heroGo.transform.SetParent(safeRect, false);
            var heroRt = (RectTransform)heroGo.transform;
            heroRt.anchorMin = new Vector2(0f, 1f);
            heroRt.anchorMax = new Vector2(0f, 1f);
            heroRt.pivot = new Vector2(0f, 1f);
            heroRt.anchoredPosition = new Vector2(m.Margin, -(m.Margin + m.TagSize.y + (mobile ? 34f : 30f)));
            heroRt.sizeDelta = m.HeroBox;

            float titleSize = UITypography.FontSnapped(m.TitleFont);
            float titleH = titleSize * 1.32f;
            float latinSize = UITypography.FontSnapped(m.LatinFont);
            float latinH = latinSize * 1.6f;

            // 5.1 标题上方一条黄色短杠（几何标点）
            var accentBarGo = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            accentBarGo.transform.SetParent(heroGo.transform, false);
            var accentBarRect = accentBarGo.GetComponent<RectTransform>();
            accentBarRect.anchorMin = new Vector2(0f, 1f);
            accentBarRect.anchorMax = new Vector2(0f, 1f);
            accentBarRect.pivot = new Vector2(0f, 1f);
            accentBarRect.anchoredPosition = Vector2.zero;
            accentBarRect.sizeDelta = new Vector2(mobile ? 104f : 78f, mobile ? 14f : 11f);
            var accentBarShape = accentBarGo.GetComponent<UIInkShapeGraphic>();
            accentBarShape.Shape = UIInkShape.SharpRect;
            accentBarShape.CornerRadius = ComicStyle.CornerSharp;
            accentBarShape.BorderWidth = 0f;
            accentBarShape.color = ComicStyle.Yellow;
            accentBarShape.raycastTarget = false;

            // 5.2 Hero 大标题
            var titleTmp = CreateTmpText(heroGo.transform, "HeroTitle", "墨水大作战",
                titleSize, isDisplay: true, ComicStyle.Ink,
                new Vector2(0f, -(26f + titleH * 0.5f)), new Vector2(m.HeroBox.x, titleH), -2f, TextAlignmentOptions.MidlineLeft);
            // 超大中文标题改用 Semibold 且不再叠 faux bold：MiSans-Heavy 在 100px+ 时
            // 密笔字（如「墨」）的笔画会糊成实心黑块；Synthetic Bold 又会让字面发胀发斜。
            titleTmp.fontStyle = FontStyles.Normal;
            var heroCjkFont = resolved.GetCjkFont(false);
            if (heroCjkFont != null) titleTmp.font = heroCjkFont;
            titleTmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            titleTmp.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleTmp.rectTransform.anchorMax = new Vector2(0f, 1f);
            titleTmp.rectTransform.anchoredPosition = new Vector2(m.HeroBox.x * 0.5f, -(26f + titleH * 0.5f));
            titleTmp.rectTransform.sizeDelta = new Vector2(m.HeroBox.x, titleH);

            // 5.3 拉丁副标题（红）
            var latinTmp = CreateTmpText(heroGo.transform, "LatinSubtitle", "INK TURF WAR",
                latinSize, isDisplay: true, ComicStyle.Red,
                new Vector2(0f, -(26f + titleH + latinH * 0.5f)),
                new Vector2(m.HeroBox.x, latinH), -3f, TextAlignmentOptions.MidlineLeft);
            latinTmp.fontStyle = FontStyles.Bold;
            latinTmp.rectTransform.anchorMin = new Vector2(0f, 1f);
            latinTmp.rectTransform.anchorMax = new Vector2(0f, 1f);
            latinTmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            latinTmp.rectTransform.anchoredPosition = new Vector2(m.HeroBox.x * 0.5f, -(26f + titleH + latinH * 0.5f));
            latinTmp.rectTransform.sizeDelta = new Vector2(m.HeroBox.x, latinH);

            // 5.4 规格行
            var specTmp = CreateTmpText(heroGo.transform, "FeatureDesc", "40×40M 对称竞技场 · 涂地覆盖率决胜",
                UITypography.FontSnapped(m.SpecFont), isDisplay: false, ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                Vector2.zero, new Vector2(m.HeroBox.x, 40f), 0.5f, TextAlignmentOptions.MidlineLeft);
            specTmp.rectTransform.anchorMin = new Vector2(0f, 1f);
            specTmp.rectTransform.anchorMax = new Vector2(0f, 1f);
            specTmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            specTmp.rectTransform.anchoredPosition = new Vector2(m.HeroBox.x * 0.5f, -(26f + titleH + latinH + 8f + 20f));
            specTmp.rectTransform.sizeDelta = new Vector2(m.HeroBox.x, 40f);

            // 6) 开赛按钮（右下，拇指区；黄色块 + 墨字 + 左侧墨色竖条）
            if (startButton != null)
            {
                var btnRect = startButton.transform as RectTransform;
                if (btnRect != null)
                {
                    btnRect.anchorMin = new Vector2(1f, 0f);
                    btnRect.anchorMax = new Vector2(1f, 0f);
                    btnRect.pivot = new Vector2(1f, 0f);
                    btnRect.anchoredPosition = new Vector2(-m.Margin, m.Margin);
                    btnRect.sizeDelta = m.CtaSize;
                }

                try
                {
                    StyleStartButtonGeometric(resolved, UITypography.FontSnapped(m.CtaFont), mobile);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[MatchFlowUI] 开赛按钮几何换肤失败：" + ex);
                }
            }

            // 7) 键盘提示（仅桌面，位于次级按钮上方）
            float secY = m.Margin + m.CtaSize.y + (mobile ? 24f : 20f);
            Transform hintTrans = FindMenuNode("StartHint");
            if (hintTrans == null)
            {
                var hintGo = new GameObject("StartHint", typeof(RectTransform), typeof(Text));
                hintGo.transform.SetParent(safeRect, false);
                var hintText = hintGo.GetComponent<Text>();
                hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                hintText.text = "也可以按 空格 / 回车 / R 开始";
                hintText.alignment = TextAnchor.MiddleRight;
                hintTrans = hintGo.transform;
            }
            ReparentInto(hintTrans, safeRect);
            var hintRect = (RectTransform)hintTrans;
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-m.Margin, secY + m.SecondarySize.y + 10f);
            hintRect.sizeDelta = new Vector2(520f, 30f);
            var hintLabel = hintTrans.GetComponent<Text>();
            if (hintLabel != null)
            {
                hintLabel.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(18f));
                hintLabel.alignment = TextAnchor.MiddleRight;
                hintLabel.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.62f);
            }
            hintTrans.gameObject.SetActive(!mobile);

            // 8) 次级按钮（右对齐，开赛按钮上方）
            StyleSecondaryButtonGeometric("CheckUpdateButton",
                new Vector2(-m.Margin, secY), m.SecondarySize,
                UITypography.FontSnapped(m.SecondaryFont), "检查更新");
            StyleSecondaryButtonGeometric("CustomLayoutButton",
                new Vector2(-m.Margin - m.SecondarySize.x - (mobile ? 18f : 16f), secY), m.SecondarySize,
                UITypography.FontSnapped(m.SecondaryFont), "自定义按键");

            // 9) 版本号（左下，整备卡上方）
            if (versionText != null)
            {
                float loadoutH = MenuLayoutMath.LoadoutCard(mobile).Size.y;
                var verRect = versionText.transform as RectTransform;
                if (verRect != null)
                {
                    verRect.anchorMin = new Vector2(0f, 0f);
                    verRect.anchorMax = new Vector2(0f, 0f);
                    verRect.pivot = new Vector2(0f, 0f);
                    verRect.anchoredPosition = new Vector2(m.Margin, m.Margin + loadoutH + 10f);
                    verRect.sizeDelta = new Vector2(560f, 30f);
                }
                versionText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(m.VersionFont));
                versionText.alignment = TextAnchor.MiddleLeft;
                versionText.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.58f);
            }
        }

        /// <summary>
        /// 主题兜底：场景里的 MatchFlowUI 历史上没有序列化 uiTheme，
        /// 于是首页所有字体都落到 TMP 默认 LiberationSans + 全局 fallback
        /// （超大中文标题会被糊成黑块）。这里按武器栏同一套路径补齐：
        /// 运行时资源库 → 编辑器 AssetDatabase → 保持 null 由 Resolve 兜底。
        /// </summary>
        private void EnsureTheme()
        {
            if (uiTheme != null) return;

            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null && library.uiTheme != null)
            {
                uiTheme = library.uiTheme;
                if (bannerView != null) bannerView.SetTheme(uiTheme);
                return;
            }

#if UNITY_EDITOR
            uiTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(
                "Assets/_Project/ScriptableObjects/UITheme.asset");
#endif
        }

        /// <summary>把节点移动到父节点下（保持世界位置无关的新布局；已是目标父节点则跳过）。</summary>
        private static void ReparentInto(Transform node, Transform parent)
        {
            if (node == null || parent == null) return;
            if (node.parent == parent) return;
            node.SetParent(parent, false);
        }

        /// <summary>在 readyPanel 子树里找节点（内容会被搬进 MenuSafeArea，不能再只看直接子级）。</summary>
        private Transform FindMenuNode(string name)
        {
            if (readyPanel == null) return null;
            Transform direct = readyPanel.transform.Find(name);
            if (direct != null) return direct;

            var all = readyPanel.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name) return all[i];
            }
            return null;
        }

        /// <summary>开赛按钮：黄色直角大块 + 墨色粗体字 + 左侧墨色竖条，纯色硬切，禁止弹簧。</summary>
        private void StyleStartButtonGeometric(UIThemeSO resolved, float fontSize, bool mobile = false)
        {
            var graphic = startButton.targetGraphic as Image;
            if (graphic != null)
            {
                graphic.color = new Color(1f, 1f, 1f, 0f);
                graphic.raycastTarget = true;
                graphic.enabled = true;
            }

            var btnShape = startButton.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (btnShape == null)
            {
                btnShape = UIInkShapeGraphic.CreateChildBackground(
                    startButton.transform as RectTransform, "GeometricButtonBg");
            }

            if (btnShape == null)
            {
                Debug.LogWarning("[MatchFlowUI] 开赛按钮无法挂载 UIInkShapeGraphic");
                return;
            }

            btnShape.Shape = UIInkShape.SharpRect;
            btnShape.CornerRadius = ComicStyle.CornerSharp;
            btnShape.BorderWidth = ComicStyle.Border;
            btnShape.BorderColor = ComicStyle.Ink;
            btnShape.HardShadow = false;
            btnShape.ShadowOffset = Vector2.zero;
            btnShape.Halftone = false;
            btnShape.GlossAlpha = 0f;
            btnShape.color = ComicStyle.Yellow; // 黄色强调块（主 CTA）
            btnShape.raycastTarget = true;

            // 左侧墨色竖条（几何标点，压住按钮左缘）
            Transform accent = btnShape.transform.Find("CtaAccent");
            if (accent == null)
            {
                var accentGo = new GameObject("CtaAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                accentGo.transform.SetParent(btnShape.transform, false);
                accent = accentGo.transform;
                var accentShape = accentGo.GetComponent<UIInkShapeGraphic>();
                accentShape.Shape = UIInkShape.SharpRect;
                accentShape.CornerRadius = ComicStyle.CornerSharp;
                accentShape.BorderWidth = 0f;
                accentShape.color = ComicStyle.Ink;
                accentShape.raycastTarget = false;
            }
            var accentRect = (RectTransform)accent;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.offsetMin = Vector2.zero;
            accentRect.offsetMax = new Vector2(mobile ? 20f : 16f, 0f);

            // 主标签用 TMP：Legacy Text 的 bold 对中文不生效，黄底上的字会显得发虚。
            var legacyLabel = startButtonLabel != null ? startButtonLabel : startButton.GetComponentInChildren<Text>(true);
            if (legacyLabel != null) legacyLabel.gameObject.SetActive(false);

            Transform labelTrans = startButton.transform.Find("CtaLabel");
            TextMeshProUGUI labelTmp;
            if (labelTrans == null)
            {
                var labelGo = new GameObject("CtaLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(startButton.transform, false);
                labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                labelTmp = labelTrans.GetComponent<TextMeshProUGUI>();
            }

            if (labelTmp != null)
            {
                var labelRect = labelTmp.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(mobile ? 30f : 24f, 0f);
                labelRect.offsetMax = Vector2.zero;
                labelTmp.text = "开 始 比 赛";
                labelTmp.font = resolved.GetCjkFont(false) ?? resolved.ResolveFont(false, "开始比赛");
                labelTmp.fontSize = fontSize;
                labelTmp.fontStyle = FontStyles.Bold;
                labelTmp.alignment = TextAlignmentOptions.Center;
                labelTmp.color = ComicStyle.Ink;
                labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
                labelTmp.overflowMode = TextOverflowModes.Overflow;
                labelTmp.raycastTarget = false;
            }

            var jelly = startButton.gameObject.GetComponent<UIJellyButton>();
            if (jelly == null) jelly = startButton.gameObject.AddComponent<UIJellyButton>();
            if (jelly != null) jelly.Configure(resolved, startButton, btnShape);
        }

        /// <summary>次要按钮（检查更新 / 自定义按键）：白底墨字直角方块，3px 实线墨边。</summary>
        private void StyleSecondaryButtonGeometric(string btnName, Vector2 anchoredPos, Vector2 size, float fontSize, string label)
        {
            Transform t = FindMenuNode(btnName);
            if (t == null) return;

            var rect = t as RectTransform;
            if (rect == null) rect = t.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = anchoredPos;
                rect.sizeDelta = size;
            }

            var img = t.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            var shape = t.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (shape == null)
            {
                shape = UIInkShapeGraphic.CreateChildBackground(rect, "GeometricBg");
            }
            if (shape != null)
            {
                shape.Shape = UIInkShape.SharpRect;
                shape.CornerRadius = ComicStyle.CornerSharp;
                shape.BorderWidth = ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.Ink;
                shape.HardShadow = false;
                shape.Halftone = false;
                shape.GlossAlpha = 0f;
                shape.color = ComicStyle.Paper; // 白底
                shape.raycastTarget = false;
            }

            var labelText = t.GetComponentInChildren<Text>(true);
            if (labelText != null)
            {
                labelText.text = label;
                labelText.color = ComicStyle.Ink; // 墨字
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

        private static UIInkShapeGraphic CreateGeometricBox(Transform parent, string name,
            Vector2 pos, Vector2 size, Color fill, float rotDegrees, float borderWidth = 0f, Color? borderColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotDegrees);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = borderColor ?? ComicStyle.Ink;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.GlossAlpha = 0f;
            shape.raycastTarget = false;
            return shape;
        }

        private static void CreateGeometricSquare(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float rotDegrees, Color fill,
            float border = 0f, Color? borderColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotDegrees);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = border;
            shape.BorderColor = borderColor ?? ComicStyle.Ink;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.color = fill;
            shape.raycastTarget = false;
        }

        private static void CreateGeometricCircle(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fill)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Blob;
            shape.WobbleAmplitude = 0f;
            shape.BorderWidth = 0f;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.color = fill;
            shape.raycastTarget = false;
        }

        /// <summary>场景里遗留的旧结算柱条容器：几何结算页不再使用，直接隐藏。</summary>
        private static void HideLegacyResultBar(Transform panel, string name)
        {
            if (panel == null) return;
            Transform legacy = panel.Find(name);
            if (legacy != null && legacy.gameObject.activeSelf) legacy.gameObject.SetActive(false);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI CreateTmpText(Transform parent, string name, string text, float size,
            bool isDisplay, Color color, Vector2 pos, Vector2 box, float charSpacing = 0f,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
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
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = color;
            tmp.raycastTarget = false;

            var font = UIThemeSO.Resolve(uiTheme).ResolveFont(isDisplay, text);
            if (font != null) tmp.font = font;

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

        /// <summary>
        /// 结算页（P14 几何大胆风重构 · 大数字即设计）：
        /// 1. 顶部 Hero 级 VICTORY / DEFEAT 判定徽章（移动端 ≥120）；
        /// 2. 双方覆盖率百分比做成超大数字（桌面 76，移动端 98）分列左右队伍纯色大块；
        /// 3. 柱状条改直角粗墨水槽；
        /// 4. 关键 CTA「再来一局」改 Red 底白字直角大按钮；
        /// 5. 次级「返回」按钮白底墨字直角方块，复用重开路径。
        /// </summary>
        private void EnsureResultVisuals()
        {
            if (resultPanel == null || resultPanel.transform.Find("GeometricResultRoot") != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);
            bool mobile = Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;

            // 纸面白底
            var panelImg = resultPanel.GetComponent<Image>();
            if (panelImg != null)
            {
                panelImg.color = ComicStyle.Paper;
            }

            // 清理旧漫画网点背景
            Transform legacyHalf = resultPanel.transform.Find("HalftoneBg");
            if (legacyHalf != null) Destroy(legacyHalf.gameObject);

            var rootGo = new GameObject("GeometricResultRoot", typeof(RectTransform));
            rootGo.transform.SetParent(resultPanel.transform, false);
            var rootRt = (RectTransform)rootGo.transform;
            Stretch(rootRt);
            rootRt.SetAsFirstSibling();

            // 四角几何纯色装饰：全部 0° 轴对齐实心块（不做菱形/斜方块，避免尖角），
            // 只保留「黑方块 / 红方块 / 队色圆 / 队色方块」四枚扁平色块。
            CreateGeometricSquare(rootRt, "ResultSquareTL", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(mobile ? 50f : 40f, mobile ? -50f : -40f), new Vector2(70f, 70f), 0f, ComicStyle.Ink);
            CreateGeometricSquare(rootRt, "ResultSquareTR", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(mobile ? -60f : -50f, mobile ? -60f : -50f), new Vector2(80f, 80f), 0f, ComicStyle.Red);
            CreateGeometricCircle(rootRt, "ResultCircleBL", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(mobile ? 60f : 50f, mobile ? 60f : 50f), new Vector2(90f, 90f), resolved.GetTeamColor(Team.TeamA));
            CreateGeometricSquare(rootRt, "ResultSquareBR", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(mobile ? -70f : -60f, mobile ? 70f : 60f), new Vector2(75f, 75f), 0f, resolved.GetTeamColor(Team.TeamB));

            // 1. 顶部 Hero 级 VICTORY / DEFEAT 判定大牌
            if (victoryBadgeRoot == null)
            {
                var badgeGo = new GameObject("VictoryBadge", typeof(RectTransform), typeof(UISpringMotion));
                badgeGo.transform.SetParent(resultPanel.transform, false);
                victoryBadgeRoot = badgeGo.GetComponent<RectTransform>();
                victoryBadgeMotion = badgeGo.GetComponent<UISpringMotion>();
                victoryBadgeMotion.SetTheme(resolved);

                var bgGo = new GameObject("BadgeBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgGo.transform.SetParent(victoryBadgeRoot, false);
                Stretch(bgGo.GetComponent<RectTransform>());
                victoryBadgeBg = bgGo.GetComponent<UIInkShapeGraphic>();
                victoryBadgeBg.Shape = UIInkShape.SharpRect;
                victoryBadgeBg.CornerRadius = ComicStyle.CornerSharp;
                victoryBadgeBg.BorderWidth = ComicStyle.BorderHeavy;
                victoryBadgeBg.BorderColor = ComicStyle.Ink;
                victoryBadgeBg.HardShadow = false;
                victoryBadgeBg.Halftone = false;
                victoryBadgeBg.color = ComicStyle.Yellow;
                victoryBadgeBg.raycastTarget = false;

                var txtGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(victoryBadgeRoot, false);
                Stretch(txtGo.GetComponent<RectTransform>());
                victoryBadgeText = txtGo.GetComponent<TextMeshProUGUI>();
                victoryBadgeText.fontSize = UITypography.FontSnapped(96f); // 桌面 96，移动端 124（≥120）
                victoryBadgeText.fontStyle = FontStyles.Bold;
                victoryBadgeText.alignment = TextAlignmentOptions.Center;
                victoryBadgeText.color = ComicStyle.Ink;
                victoryBadgeText.text = "VICTORY!";
                victoryBadgeText.raycastTarget = false;
                victoryBadgeText.font = resolved.ResolveFont(true, "VICTORY!");
            }

            victoryBadgeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.pivot = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchoredPosition = new Vector2(0f, mobile ? 310f : 320f);
            victoryBadgeRoot.sizeDelta = new Vector2(mobile ? 760f : 680f, mobile ? 140f : 120f);
            victoryBadgeRoot.gameObject.SetActive(false);

            // 2. 左右分列队伍色块与超大数字（Team A 橙 vs Team B 蓝）
            float cardOffsetX = mobile ? 360f : 330f;
            float cardCenterY = mobile ? 40f : 50f;
            Vector2 cardSize = mobile ? new Vector2(520f, 320f) : new Vector2(460f, 280f);

            // 2.1 队伍 A 纯色色块（我方）
            var teamAGo = new GameObject("TeamACard", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            teamAGo.transform.SetParent(rootRt, false);
            var teamARt = (RectTransform)teamAGo.transform;
            teamARt.anchorMin = new Vector2(0.5f, 0.5f);
            teamARt.anchorMax = new Vector2(0.5f, 0.5f);
            teamARt.anchoredPosition = new Vector2(-cardOffsetX, cardCenterY);
            teamARt.sizeDelta = cardSize;

            var teamAShape = teamAGo.GetComponent<UIInkShapeGraphic>();
            teamAShape.Shape = UIInkShape.SharpRect;
            teamAShape.CornerRadius = ComicStyle.CornerSharp;
            teamAShape.BorderWidth = ComicStyle.BorderHeavy;
            teamAShape.BorderColor = ComicStyle.Ink;
            teamAShape.color = ComicStyle.Paper;
            teamAShape.HardShadow = false;

            // 队伍 A 顶部色块条
            var barTagAGo = new GameObject("TagA", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barTagAGo.transform.SetParent(teamAGo.transform, false);
            var tagARt = (RectTransform)barTagAGo.transform;
            tagARt.anchorMin = new Vector2(0f, 1f);
            tagARt.anchorMax = new Vector2(1f, 1f);
            tagARt.pivot = new Vector2(0.5f, 1f);
            tagARt.anchoredPosition = Vector2.zero;
            tagARt.sizeDelta = new Vector2(0f, mobile ? 62f : 54f);
            var tagAShape = barTagAGo.GetComponent<UIInkShapeGraphic>();
            tagAShape.Shape = UIInkShape.SharpRect;
            tagAShape.CornerRadius = ComicStyle.CornerSharp;
            tagAShape.BorderWidth = 0f;
            tagAShape.color = resolved.GetTeamColor(Team.TeamA);
            tagAShape.HardShadow = false;

            CreateTmpText(barTagAGo.transform, "Label", "TEAM A · 我方队伍",
                UITypography.FontSnapped(22f), isDisplay: false, ComicStyle.Paper, Vector2.zero,
                new Vector2(cardSize.x, mobile ? 62f : 54f), 0.5f, TextAlignmentOptions.Center);

            // 2.2 队伍 B 纯色色块（敌方）
            var teamBGo = new GameObject("TeamBCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            teamBGo.transform.SetParent(rootRt, false);
            var teamBRt = (RectTransform)teamBGo.transform;
            teamBRt.anchorMin = new Vector2(0.5f, 0.5f);
            teamBRt.anchorMax = new Vector2(0.5f, 0.5f);
            teamBRt.anchoredPosition = new Vector2(cardOffsetX, cardCenterY);
            teamBRt.sizeDelta = cardSize;

            var teamBShape = teamBGo.GetComponent<UIInkShapeGraphic>();
            teamBShape.Shape = UIInkShape.SharpRect;
            teamBShape.CornerRadius = ComicStyle.CornerSharp;
            teamBShape.BorderWidth = ComicStyle.BorderHeavy;
            teamBShape.BorderColor = ComicStyle.Ink;
            teamBShape.color = ComicStyle.Paper;
            teamBShape.HardShadow = false;

            // 队伍 B 顶部色块条
            var barTagBGo = new GameObject("TagB", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            barTagBGo.transform.SetParent(teamBGo.transform, false);
            var tagBRt = (RectTransform)barTagBGo.transform;
            tagBRt.anchorMin = new Vector2(0f, 1f);
            tagBRt.anchorMax = new Vector2(1f, 1f);
            tagBRt.pivot = new Vector2(0.5f, 1f);
            tagBRt.anchoredPosition = Vector2.zero;
            tagBRt.sizeDelta = new Vector2(0f, mobile ? 62f : 54f);
            var tagBShape = barTagBGo.GetComponent<UIInkShapeGraphic>();
            tagBShape.Shape = UIInkShape.SharpRect;
            tagBShape.CornerRadius = ComicStyle.CornerSharp;
            tagBShape.BorderWidth = 0f;
            tagBShape.color = resolved.GetTeamColor(Team.TeamB);
            tagBShape.HardShadow = false;

            CreateTmpText(barTagBGo.transform, "Label", "TEAM B · 敌方队伍",
                UITypography.FontSnapped(22f), isDisplay: false, ComicStyle.Paper, Vector2.zero,
                new Vector2(cardSize.x, mobile ? 62f : 54f), 0.5f, TextAlignmentOptions.Center);

            // 3. 将现有的 resultPercentA / resultPercentB 挂入对应色块并升级超大字号
            float bigNumberSize = UITypography.FontSnapped(96f); // 桌面 96，移动端 124（大数字即设计）

            if (resultPercentA != null)
            {
                resultPercentA.transform.SetParent(teamAGo.transform, false);
                var pRtA = resultPercentA.rectTransform;
                pRtA.anchorMin = new Vector2(0.5f, 0.5f);
                pRtA.anchorMax = new Vector2(0.5f, 0.5f);
                pRtA.anchoredPosition = new Vector2(0f, mobile ? 15f : 10f);
                pRtA.sizeDelta = new Vector2(cardSize.x - 40f, 110f);
                resultPercentA.fontSize = Mathf.RoundToInt(bigNumberSize);
                resultPercentA.fontStyle = FontStyle.Bold;
                resultPercentA.alignment = TextAnchor.MiddleCenter;
                resultPercentA.color = ComicStyle.Ink;
                resultPercentA.horizontalOverflow = HorizontalWrapMode.Overflow;
                resultPercentA.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (resultPercentB != null)
            {
                resultPercentB.transform.SetParent(teamBGo.transform, false);
                var pRtB = resultPercentB.rectTransform;
                pRtB.anchorMin = new Vector2(0.5f, 0.5f);
                pRtB.anchorMax = new Vector2(0.5f, 0.5f);
                pRtB.anchoredPosition = new Vector2(0f, mobile ? 15f : 10f);
                pRtB.sizeDelta = new Vector2(cardSize.x - 40f, 110f);
                resultPercentB.fontSize = Mathf.RoundToInt(bigNumberSize);
                resultPercentB.fontStyle = FontStyle.Bold;
                resultPercentB.alignment = TextAnchor.MiddleCenter;
                resultPercentB.color = ComicStyle.Ink;
                resultPercentB.horizontalOverflow = HorizontalWrapMode.Overflow;
                resultPercentB.verticalOverflow = VerticalWrapMode.Overflow;
            }

            // 4. 直角粗墨水柱（放入各自卡片底部）
            float barSlotH = mobile ? 28f : 24f;
            float barSlotY = mobile ? -90f : -80f;

            if (resultBarA != null)
            {
                var barSlotGo = new GameObject("BarSlotA", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                barSlotGo.transform.SetParent(teamAGo.transform, false);
                var bsRt = (RectTransform)barSlotGo.transform;
                bsRt.anchorMin = new Vector2(0.5f, 0.5f);
                bsRt.anchorMax = new Vector2(0.5f, 0.5f);
                bsRt.anchoredPosition = new Vector2(0f, barSlotY);
                bsRt.sizeDelta = new Vector2(cardSize.x - 48f, barSlotH);

                var bsShape = barSlotGo.GetComponent<UIInkShapeGraphic>();
                bsShape.Shape = UIInkShape.SharpRect;
                bsShape.CornerRadius = ComicStyle.CornerSharp;
                bsShape.BorderWidth = ComicStyle.BorderThin;
                bsShape.BorderColor = ComicStyle.Ink;
                bsShape.color = ComicStyle.PaperAlt;
                bsShape.HardShadow = false;

                resultBarA.transform.SetParent(barSlotGo.transform, false);
                var barRt = resultBarA.rectTransform;
                barRt.anchorMin = Vector2.zero;
                barRt.anchorMax = Vector2.one;
                barRt.offsetMin = new Vector2(2f, 2f);
                barRt.offsetMax = new Vector2(-2f, -2f);
                // 纯平直角填充：内置 UISprite 在 20~30px 高度会露出圆角端帽，
                // 实机就是「两头尖尖 + 边缘发灰的渐变」；换 1×1 纯白精灵才是纯色直条。
                UIFlatSprite.MakeFlat(resultBarA, filled: true);
                resultBarA.raycastTarget = false;
                resultBarA.color = resolved.GetTeamColor(Team.TeamA);
            }

            if (resultBarB != null)
            {
                var barSlotGo = new GameObject("BarSlotB", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                barSlotGo.transform.SetParent(teamBGo.transform, false);
                var bsRt = (RectTransform)barSlotGo.transform;
                bsRt.anchorMin = new Vector2(0.5f, 0.5f);
                bsRt.anchorMax = new Vector2(0.5f, 0.5f);
                bsRt.anchoredPosition = new Vector2(0f, barSlotY);
                bsRt.sizeDelta = new Vector2(cardSize.x - 48f, barSlotH);

                var bsShape = barSlotGo.GetComponent<UIInkShapeGraphic>();
                bsShape.Shape = UIInkShape.SharpRect;
                bsShape.CornerRadius = ComicStyle.CornerSharp;
                bsShape.BorderWidth = ComicStyle.BorderThin;
                bsShape.BorderColor = ComicStyle.Ink;
                bsShape.color = ComicStyle.PaperAlt;
                bsShape.HardShadow = false;

                resultBarB.transform.SetParent(barSlotGo.transform, false);
                var barRt = resultBarB.rectTransform;
                barRt.anchorMin = Vector2.zero;
                barRt.anchorMax = Vector2.one;
                barRt.offsetMin = new Vector2(2f, 2f);
                barRt.offsetMax = new Vector2(-2f, -2f);
                // 同上：直角纯色填充，禁止圆角端帽 / 渐变边缘。
                UIFlatSprite.MakeFlat(resultBarB, filled: true);
                resultBarB.raycastTarget = false;
                resultBarB.color = resolved.GetTeamColor(Team.TeamB);
            }

            // 4b. 隐藏旧场景里的柱条容器（ResultBarA/B 的黑底条）；
            //     真正的 Fill 已被搬进新的 BarSlot，不隐藏会在卡片中央留下两条遮挡条。
            HideLegacyResultBar(resultPanel.transform, "ResultBarA");
            HideLegacyResultBar(resultPanel.transform, "ResultBarB");

            // 5. 判定文字（正在计算 / 胜负结果）
            if (verdictText != null)
            {
                var vRt = verdictText.rectTransform;
                vRt.anchorMin = new Vector2(0.5f, 0.5f);
                vRt.anchorMax = new Vector2(0.5f, 0.5f);
                vRt.anchoredPosition = new Vector2(0f, mobile ? -150f : -135f);
                vRt.sizeDelta = new Vector2(mobile ? 820f : 740f, 44f);
                verdictText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(30f));
                verdictText.fontStyle = FontStyle.Bold;
                verdictText.alignment = TextAnchor.MiddleCenter;
                verdictText.color = ComicStyle.Ink;
            }

            // 6. 再来一局 CTA（Red 底白字直角大按钮）
            float btnY = mobile ? -255f : -235f;
            Vector2 restartSize = mobile ? new Vector2(380f, 92f) : new Vector2(320f, 80f);
            Vector2 backSize = mobile ? new Vector2(240f, 92f) : new Vector2(200f, 80f);
            float btnSpacing = mobile ? 140f : 120f;

            if (restartButton != null)
            {
                var rRt = restartButton.transform as RectTransform;
                if (rRt != null)
                {
                    rRt.anchorMin = new Vector2(0.5f, 0.5f);
                    rRt.anchorMax = new Vector2(0.5f, 0.5f);
                    rRt.anchoredPosition = new Vector2(-btnSpacing, btnY);
                    rRt.sizeDelta = restartSize;
                }

                var rImg = restartButton.GetComponent<Image>();
                if (rImg != null)
                {
                    rImg.color = new Color(1f, 1f, 1f, 0f);
                    rImg.raycastTarget = true;
                }

                var rShape = restartButton.GetComponentInChildren<UIInkShapeGraphic>(true);
                if (rShape == null)
                {
                    rShape = UIInkShapeGraphic.CreateChildBackground(rRt, "RestartBg");
                }
                if (rShape != null)
                {
                    rShape.Shape = UIInkShape.SharpRect;
                    rShape.CornerRadius = ComicStyle.CornerSharp;
                    rShape.BorderWidth = ComicStyle.BorderHeavy;
                    rShape.BorderColor = ComicStyle.Ink;
                    rShape.color = ComicStyle.Red; // Red 关键 CTA
                    rShape.HardShadow = false;
                    rShape.Halftone = false;
                    rShape.raycastTarget = false;
                }

                var rLabel = restartButton.GetComponentInChildren<Text>(true);
                if (rLabel != null)
                {
                    rLabel.text = "再 来 一 局";
                    rLabel.color = ComicStyle.Paper; // 白字
                    rLabel.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(28f));
                    rLabel.fontStyle = FontStyle.Bold;
                    rLabel.alignment = TextAnchor.MiddleCenter;
                }

                var jelly = restartButton.GetComponent<UIJellyButton>();
                if (jelly == null) jelly = restartButton.gameObject.AddComponent<UIJellyButton>();
                jelly.Configure(resolved, restartButton, rShape);
            }

            // 7. 次级「返回」按钮（白底墨字直角方块，复用现有重开路径）
            Transform backBtnTrans = resultPanel.transform.Find("ResultBackButton");
            if (backBtnTrans == null)
            {
                var backGo = new GameObject("ResultBackButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                backGo.transform.SetParent(resultPanel.transform, false);
                var bRt = (RectTransform)backGo.transform;
                bRt.anchorMin = new Vector2(0.5f, 0.5f);
                bRt.anchorMax = new Vector2(0.5f, 0.5f);
                bRt.anchoredPosition = new Vector2(btnSpacing + (restartSize.x - backSize.x) * 0.5f, btnY);
                bRt.sizeDelta = backSize;

                var bImg = backGo.GetComponent<Image>();
                bImg.color = new Color(1f, 1f, 1f, 0f);
                bImg.raycastTarget = true;

                var bShape = UIInkShapeGraphic.CreateChildBackground(bRt, "BackBg");
                bShape.Shape = UIInkShape.SharpRect;
                bShape.CornerRadius = ComicStyle.CornerSharp;
                bShape.BorderWidth = ComicStyle.BorderHeavy;
                bShape.BorderColor = ComicStyle.Ink;
                bShape.color = ComicStyle.Paper; // 白底
                bShape.HardShadow = false;
                bShape.Halftone = false;
                bShape.raycastTarget = false;

                var bLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                bLabelGo.transform.SetParent(backGo.transform, false);
                Stretch(bLabelGo.GetComponent<RectTransform>());
                var bLabel = bLabelGo.GetComponent<Text>();
                bLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                bLabel.text = "返 回";
                bLabel.color = ComicStyle.Ink; // 墨字
                bLabel.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(24f));
                bLabel.fontStyle = FontStyle.Bold;
                bLabel.alignment = TextAnchor.MiddleCenter;

                var bBtn = backGo.GetComponent<Button>();
                bBtn.onClick.AddListener(OnStartClicked);

                var bJelly = backGo.AddComponent<UIJellyButton>();
                bJelly.Configure(resolved, bBtn, bShape);
            }

            // 8. 结算页硬性扁平化：所有形状的斜切高光、内沿墨光、网点、阴影全部归零，
            //    确保无论场景继承了什么旧参数，屏幕上只出现纯色实心块。
            var resultShapes = resultPanel.GetComponentsInChildren<UIInkShapeGraphic>(true);
            for (int i = 0; i < resultShapes.Length; i++)
            {
                FlattenShape(resultShapes[i]);
            }

            // 隐藏旧场景多余标题
            if (resultTitleText != null) resultTitleText.gameObject.SetActive(false);
        }

        /// <summary>
        /// 几何风扁平化兜底：清掉斜切高光、内沿墨光、网点与硬阴影。
        /// 结算页只允许「纯色实心块 + 0 圆角 + 实线边框」。
        /// </summary>
        private static void FlattenShape(UIInkShapeGraphic shape)
        {
            if (shape == null) return;
            shape.DrawGloss = false;
            shape.GlossAlpha = 0f;
            shape.InnerRimAlpha = 0f;
            shape.Halftone = false;
            shape.HalftoneAlpha = 0f;
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.CornerRadius = ComicStyle.CornerSharp;
        }

        public void EnsureUpdateUI()
        {
            if (readyPanel == null) return;

            if (checkUpdateButton == null)
            {
                var existingBtn = FindMenuNode("CheckUpdateButton");
                if (existingBtn != null) checkUpdateButton = existingBtn.GetComponent<Button>();
            }

            if (versionText == null)
            {
                var existingVer = FindMenuNode("VersionText");
                if (existingVer != null) versionText = existingVer.GetComponent<Text>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 动态创建 VersionText
            if (versionText == null)
            {
                var verGo = new GameObject("VersionText", typeof(RectTransform), typeof(Text));
                verGo.transform.SetParent(readyPanel.transform, false);
                var verRt = verGo.GetComponent<RectTransform>();
                verRt.anchorMin = new Vector2(1f, 0f);
                verRt.anchorMax = new Vector2(1f, 0f);
                verRt.pivot = new Vector2(1f, 0f);
                verRt.anchoredPosition = new Vector2(-24f, 18f);
                verRt.sizeDelta = new Vector2(560f, 32f);

                versionText = verGo.GetComponent<Text>();
                versionText.font = font;
                versionText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(18f));
                versionText.alignment = TextAnchor.MiddleRight;
                versionText.color = ComicStyle.Ink;
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
                btnRt.sizeDelta = new Vector2(210f, 58f);

                var img = btnGo.GetComponent<Image>();
                img.color = ComicStyle.Paper;

                checkUpdateButton = btnGo.GetComponent<Button>();
                checkUpdateButton.targetGraphic = img;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(btnGo.transform, false);
                Stretch(labelGo.GetComponent<RectTransform>());

                var labelText = labelGo.GetComponent<Text>();
                labelText.font = font;
                labelText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(20f));
                labelText.fontStyle = FontStyle.Bold;
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.color = ComicStyle.Ink;
                labelText.text = "检查更新";
            }

            Transform layoutBtnTrans = FindMenuNode("CustomLayoutButton");
            if (layoutBtnTrans == null)
            {
                var lbtnGo = new GameObject("CustomLayoutButton", typeof(RectTransform), typeof(Image), typeof(Button));
                lbtnGo.transform.SetParent(readyPanel.transform, false);
                var lbtnRt = lbtnGo.GetComponent<RectTransform>();
                lbtnRt.anchorMin = new Vector2(0.5f, 0f);
                lbtnRt.anchorMax = new Vector2(0.5f, 0f);
                lbtnRt.anchoredPosition = new Vector2(115f, 48f);
                lbtnRt.sizeDelta = new Vector2(210f, 58f);

                var limg = lbtnGo.GetComponent<Image>();
                limg.color = ComicStyle.Paper;

                var lbtn = lbtnGo.GetComponent<Button>();
                lbtn.targetGraphic = limg;
                lbtn.onClick.AddListener(() =>
                {
                    var touchUI = FindAnyObjectByType<MobileTouchUI>();
                    if (touchUI != null) touchUI.OpenLayoutCustomizer();
                });

                var llabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                llabelGo.transform.SetParent(lbtnGo.transform, false);
                Stretch(llabelGo.GetComponent<RectTransform>());

                var llabelText = llabelGo.GetComponent<Text>();
                llabelText.font = font;
                llabelText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(20f));
                llabelText.fontStyle = FontStyle.Bold;
                llabelText.alignment = TextAnchor.MiddleCenter;
                llabelText.color = ComicStyle.Ink;
                llabelText.text = "自定义按键";

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

            // 快捷键开赛与重开
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
            if (phase == MatchManager.Phase.Ready)
            {
                // 从对局/倒计时中返回主菜单：撤掉可能还挂着的倒计时横幅
                if (bannerView != null) bannerView.HideImmediate();
            }
            else if (phase == MatchManager.Phase.Countdown)
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

            if (_fullscreenUnderlay != null)
            {
                bool showUnderlay = phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Result;
                _fullscreenUnderlay.SetActive(showUnderlay);
            }

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
                    verdictText.color = ComicStyle.Ink;
                }
                else
                {
                    bool won = match.PlayerWon;
                    verdictText.text = won ? "【 胜 利 获 胜 】" : "惜 败 · 再 接 再 厉";
                    verdictText.color = won ? ComicStyle.Ink : ComicStyle.Blue;

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
                victoryBadgeText.color = playerWon ? ComicStyle.Ink : Color.white;
                var font = resolved.ResolveFont(true, text);
                if (font != null) victoryBadgeText.font = font;
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
