using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
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
    ///    - 顶部 Hero 级 VICTORY / DEFEAT 判定大牌（移动端 ≥120），一句话讲完胜负；
    ///    - 两侧队友 / 敌方纯色比分卡：队色标题条 + 超大百分比 + 涂地条，卡面只留「我方 / 敌方」；
    ///    - 一块白底墨框战报单据收拢勋章与成长：勋章 chips + 大号 LV + 经验条，不再堆叠标题标签；
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
        private UIChunkyText _victoryBadgeChunky;

        // ==================== t-1 新野兽派 · 深色舞台（P19 波次3a） ====================
        // 规范「深色舞台底」允许 Ink #141414 / 深墨 #0B0B0B；首页与结算统一用略偏暖的极深墨黑，
        // 让纯黑硬影（#141414）与舞台之间有 1~2 级明度差，硬影与色块才读得出来。
        // 主题资产里暂无「舞台」token（属于整页界面的一次性底色），因此在这里定义并写进规范文档。

        /// <summary>深色舞台底 #12100E（整页界面专用；战斗 HUD 仍保持透明浮层）。</summary>
        public static readonly Color StageInk = ComicStyle.FromHex(0x12100E);

        /// <summary>失败语义的灰蓝 #3D4A63：比红克制，只出现在败方色带与判定牌描边上。</summary>
        public static readonly Color DefeatSlate = ComicStyle.FromHex(0x3D4A63);

        private RectTransform _contentRoot;
        private RectTransform _menuSafeArea;
        private RectTransform _homeHeroCard;
        private HomeRuntimeBoard _homeBoard;
        // 首页几何指纹（安全区宽高 + 画布宽高）：几何变化时重建首页，见 Update / RebuildHomeVisuals。
        private Vector4 _homeGeometryKey;
        private Vector4 _homeGeometryKeyPrev;
        private bool _homeGeometryKeyValid;
        private TextMeshProUGUI _homeAudioTitle;
        private TextMeshProUGUI _homeAudioInfo;
        private TextMeshProUGUI _homeRecordInfo;
        private TextMeshProUGUI _homeKeysInfo;
        private float _audioVolumeBeforeMute = 0.6f;
        private readonly List<UISpringMotion> _homeEntrance = new List<UISpringMotion>();
        private bool _homeEntrancePending;
        private bool _resultEntrancePending;
        private bool _firstPhaseSynced;

        private RectTransform _reportBand;
        private UIInkShapeGraphic _reportBandShape;
        private RectTransform _teamCardA;
        private RectTransform _teamCardB;
        private UISpringMotion _reportMotion;
        private readonly List<UISpringMotion> _resultEntrance = new List<UISpringMotion>();

        private GameObject _fullscreenUnderlay;
        private float _resultElapsed;
        private MatchManager.Phase _lastPhase = (MatchManager.Phase)(-1);
        private int _lastCountdownNumber = -1;
        private bool _oneMinuteTriggered;
        private bool _badgePlayed;
        private bool _numbersSettled;

        [Header("结算勋章与成长系统（INKWAVE 对齐）")]
        private RectTransform _medalsContainer;
        private readonly List<GameObject> _medalBadgeObjects = new List<GameObject>();
        private readonly List<AwardType> _extractedAwards = new List<AwardType>();
        private int _displayedMedalCount;

        private RectTransform _reportPanelRoot;
        private RectTransform _winnerTagA;
        private RectTransform _winnerTagB;
        private bool _winnerEmphasisApplied;

        private TextMeshProUGUI _xpLevelText;
        private TextMeshProUGUI _xpRankNameText;
        private TextMeshProUGUI _xpGainText;
        private Image _xpBarFill;
        private RectTransform _levelUpBadgeRoot;
        private TextMeshProUGUI _levelUpText;
        private UISpringMotion _levelUpMotion;
        private ProfileMatchResult _profileResult;
        private bool _profileSettled;
        private int _lastLevelUpStep;
        private AwardType _localAwards;

        private static bool s_SettledThisMatch;

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
        /// 全屏绝对不透明舞台底板：在 Ready 与 Result 阶段激活，
        /// 100% 覆盖整个物理屏幕（不受 SafeArea 缩进影响），绝对遮蔽 3D 地图，彻底杜绝边缘漏景穿帮。
        /// 颜色 = 深色舞台底（t-1），整页界面不再铺白。
        /// </summary>
        private void EnsureFullscreenPaperUnderlay()
        {
            if (_fullscreenUnderlay != null) return;

            var underlayGo = new GameObject("FullscreenPaperUnderlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            underlayGo.transform.SetParent(transform, false);
            underlayGo.transform.SetAsFirstSibling();

            var rt = (RectTransform)underlayGo.transform;
            Stretch(rt);

            var img = underlayGo.GetComponent<Image>();
            img.color = StageInk;
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
        /// 主菜单（t-1 新野兽派 · 波次3a）：
        /// 1. 深色舞台底（#12100E）+ 低透明几何点缀 + 稀疏星点，整屏不再铺白、也不再留大面积空场；
        /// 2. 左侧 Hero 大卡（主行动黄实底）：4px 墨框 + Hero 硬影 (6,-7)、左上「PLAY」黑底白字角签、
        ///    120 号 display 粗体中文标题、`·` 分隔的密集信息行、桌面版深色规则板、
        ///    底部「开始游戏 》》」墨底金字大 CTA（按钮硬影 + UIJellyButton 按压物理）；
        /// 3. 右侧 2×2 次级卡：纸白底 + 3px 墨框 + 面板硬影 (4,-5) + 顶部 5px 身份色带
        ///    + 左上角签 + 一行 `·` 密集信息 + `》` 行进箭头（只有可点卡才放箭头）；
        /// 4. 左下 / 右下黑底角签（社区位 / 版本号）落在卡片区之外，不抢主角；
        /// 5. 内容全部放进 SafeArea，入场按 0.05s/张错峰 pop-in（0.22s Back-Out）。
        /// 所有盒子来自 <see cref="MenuLayoutMath.Home"/>，纯数据、EditMode 可断言。
        /// </summary>
        private void EnsureReadyVisuals()
        {
            if (readyPanel == null) return;
            EnsureTheme();
            if (readyPanel.transform.Find("GeometricMenuRoot") != null) return;

            UIThemeSO theme = UIThemeSO.Resolve(uiTheme);
            bool mobile = UITypography.IsMobileLike;
            HomeMenuMetrics m = MenuLayoutMath.Home(mobile);

            // 1) 深色舞台底：全屏不透明，遮蔽 3D 场景
            var overlay = readyPanel.GetComponent<Image>();
            if (overlay != null)
            {
                overlay.color = StageInk;
                overlay.raycastTarget = true;
            }

            // 2) 全屏背景装饰层（在内容层之下）：低透明几何块 + 稀疏星点
            var stageGo = new GameObject("GeometricMenuRoot", typeof(RectTransform));
            stageGo.transform.SetParent(readyPanel.transform, false);
            var stageRoot = (RectTransform)stageGo.transform;
            Stretch(stageRoot);
            stageRoot.SetAsFirstSibling();
            BuildStageDecor(stageRoot, m);

            // 3) 安全区内容层（顺序：先 Stretch 再挂 SafeAreaFitter，否则 anchor 会被覆盖回全屏）
            var safeGo = new GameObject("MenuSafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(readyPanel.transform, false);
            var safeRect = (RectTransform)safeGo.transform;
            Stretch(safeRect);
            safeGo.AddComponent<SafeAreaFitter>();
            _menuSafeArea = safeRect;

            // 卡片几何要在 SafeArea 真实 rect 上算（Awake 阶段 Canvas 还没排版时 rect 为 0，
            // 直接读会回落到 1080 设计高度，20:9 手机上就会压到左下整备卡）。
            Canvas.ForceUpdateCanvases();
            _homeBoard = ResolveHomeBoard(m);

            ReparentInto(startButton != null ? startButton.transform : null, safeRect);
            ReparentInto(checkUpdateButton != null ? checkUpdateButton.transform : null, safeRect);
            ReparentInto(FindMenuNode("CustomLayoutButton"), safeRect);

            // 4) 卡片：Hero → 2×2 → 角签（入场顺序 = 构建顺序）
            BuildHomeHero(theme, m, mobile);
            BuildHomeTiles(theme, m, mobile);
            BuildHomeCornerTags(theme, m, mobile);

            // 5) 旧场景节点退场：标题 / 副标题 / 提示 / 版本号都由新卡与角签承担
            if (titleText != null) titleText.gameObject.SetActive(false);
            if (subtitleText != null) subtitleText.gameObject.SetActive(false);
            if (versionText != null) versionText.gameObject.SetActive(false);
            Transform legacyHint = FindMenuNode("StartHint");
            if (legacyHint != null) legacyHint.gameObject.SetActive(false);

            PlayHomeEntrance();

            // 记录本次构建使用的几何指纹：安全区/画布此后一旦变化（启动 settle、旋转、分屏），
            // Update 会检测到并用真实几何重建首页——启动首帧的脏 safeArea 不能永久定格版面。
            _homeGeometryKey = CurrentHomeGeometryKey();
            _homeGeometryKeyPrev = _homeGeometryKey;
            _homeGeometryKeyValid = true;
        }

        // ==================== 首页构建（波次3a） ====================

        /// <summary>安全区可用宽度；rect 还没算出来时退回全屏父级，最后才用参考宽度。</summary>
        private float ResolveSafeWidth()
        {
            float width = _menuSafeArea != null ? _menuSafeArea.rect.width : 0f;
            if (width < 1f && _menuSafeArea != null && _menuSafeArea.parent is RectTransform parent)
            {
                width = parent.rect.width;
            }
            if (width < 1f) width = MenuLayoutMath.RefWidth;
            return width;
        }

        /// <summary>安全区可用高度；rect 还没算出来时退回全屏父级，最后才用参考高度。</summary>
        private float ResolveSafeHeight()
        {
            float height = _menuSafeArea != null ? _menuSafeArea.rect.height : 0f;
            if (height < 1f && _menuSafeArea != null && _menuSafeArea.parent is RectTransform parent)
            {
                height = parent.rect.height;
            }
            if (height < 1f) height = MenuLayoutMath.RefHeight;
            return height;
        }

        /// <summary>MenuSafeArea 全屏父级（ReadyPanel）的尺寸，即画布逻辑尺寸；还没排版时为 0。</summary>
        private Vector2 ResolveCanvasSize()
        {
            return _menuSafeArea != null && _menuSafeArea.parent is RectTransform parent
                ? parent.rect.size
                : Vector2.zero;
        }

        /// <summary>首页几何指纹 = (安全区宽, 安全区高, 画布宽, 画布高)；任一变化都需要重建首页。</summary>
        private Vector4 CurrentHomeGeometryKey()
        {
            if (_menuSafeArea == null) return Vector4.zero;
            Vector2 canvas = ResolveCanvasSize();
            Rect safe = _menuSafeArea.rect;
            return new Vector4(safe.width, safe.height, canvas.x, canvas.y);
        }

        private static float MaxDimensionDelta(Vector4 a, Vector4 b)
        {
            return Mathf.Max(
                Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)),
                Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)));
        }

        /// <summary>
        /// 用当前安全区/画布几何重建首页（启动 settle、旋转、分屏后调用）。
        /// 只销毁本脚本生成的节点；场景遗留按钮（开赛 / 检查更新 / 自定义按键）原样保留，
        /// 由 EnsureReadyVisuals 重新收编与摆位。
        /// </summary>
        private void RebuildHomeVisuals()
        {
            if (readyPanel == null || _menuSafeArea == null) return;

            // 首页重建会销毁并重建整棵卡片树：先收掉详情浮层，避免它挂在旧几何上。
            HomeInfoOverlay.CloseIfOpen();

            // 检查更新按钮已被铺进 HomeTile_UPDATE 的 Content 里，必须先搬出来再销毁卡片。
            if (checkUpdateButton != null) ReparentInto(checkUpdateButton.transform, _menuSafeArea);

            Transform stage = readyPanel.transform.Find("GeometricMenuRoot");
            if (stage != null)
            {
                // 改名让 EnsureReadyVisuals 的「已构建」守卫失效；Destroy 延迟到帧末，不阻塞重建。
                stage.name += "_Old";
                Destroy(stage.gameObject);
            }

            for (int i = _menuSafeArea.childCount - 1; i >= 0; i--)
            {
                Transform child = _menuSafeArea.GetChild(i);
                string childName = child.name;
                bool built = childName.StartsWith("HomeTile_") || childName == "HomeHeroCard" ||
                             childName == "HomeFooterTag" || childName == "HomeVersionTag";
                if (built)
                {
                    child.name += "_Old";
                    Destroy(child.gameObject);
                }
            }

            // 旧 MenuSafeArea 本体也要退役：EnsureReadyVisuals 会新建一个同名节点，
            // 老节点若只清空内容而不销毁，会永久残留为空壳（按名字查找会命中旧空容器，
            // 表现为「首页内容不在安全区」）。场景遗留按钮（开赛 / 自定义按键）由
            // EnsureReadyVisuals 先搬进新容器再销毁本节点。
            _menuSafeArea.name += "_Old";
            Destroy(_menuSafeArea.gameObject);

            _homeEntrance.Clear();
            _homeGeometryKeyValid = false;
            EnsureReadyVisuals();
        }

        /// <summary>
        /// 把 <see cref="MenuLayoutMath.Home"/> 的设计盒换算成运行时盒（纯函数版，含安全区钳制）：
        /// 高度跟着安全区高度走（20:9 手机的逻辑画布只有约 966 高，
        /// 写死 1080 会把卡片压到左下「武器整备」卡上）；宽度按刘海内缩自适应；
        /// 安全区比画布还大（启动/旋转瞬间的脏 safeArea）时钳回画布，防止卡阵出屏。
        /// </summary>
        private HomeRuntimeBoard ResolveHomeBoard(HomeMenuMetrics m)
        {
            return MenuLayoutMath.ResolveHomeBoard(m, ResolveSafeWidth(), ResolveSafeHeight(), ResolveCanvasSize());
        }

        /// <summary>深色舞台上的背景装饰：低透明几何块 + 稀疏星点（纯程序、固定种子、无循环动画）。</summary>
        private void BuildStageDecor(RectTransform parent, HomeMenuMetrics m)
        {
            CreateGeometricSquare(parent, "DecoSquareTL", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-m.DecoSquare * 0.18f, m.DecoSquare * 0.18f),
                new Vector2(m.DecoSquare, m.DecoSquare), 45f,
                ComicStyle.WithAlpha(ComicStyle.Paper, 0.10f));

            CreateGeometricCircle(parent, "DecoCircleBR", new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(m.DecoCircle * 0.28f, -m.DecoCircle * 0.28f),
                new Vector2(m.DecoCircle, m.DecoCircle),
                ComicStyle.WithAlpha(ComicStyle.Red, 0.14f));

            CreateStarField(parent, UITypography.IsMobileLike ? 30 : 44, 0x5EED1204u);
        }

        /// <summary>稀疏星点：小方块 + 少量 45° 斜块；用归一化锚点，任何宽高比都不变形。</summary>
        private static void CreateStarField(RectTransform parent, int count, uint seed)
        {
            uint state = seed == 0u ? 20261101u : seed;
            for (int i = 0; i < count; i++)
            {
                float nx = NextUnit(ref state);
                float ny = NextUnit(ref state);
                float size = 3f + NextUnit(ref state) * 6f;
                float roll = NextUnit(ref state);

                Color color;
                if (roll < 0.70f) color = ComicStyle.WithAlpha(ComicStyle.Paper, 0.10f + NextUnit(ref state) * 0.12f);
                else if (roll < 0.86f) color = ComicStyle.WithAlpha(ComicStyle.Yellow, 0.16f);
                else color = ComicStyle.WithAlpha(ComicStyle.Red, 0.14f);

                var go = new GameObject("Star", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(Mathf.Clamp01(nx), Mathf.Clamp01(ny));
                rect.anchorMax = rect.anchorMin;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(size, size);
                rect.localRotation = Quaternion.Euler(0f, 0f, roll < 0.32f ? 45f : 0f);

                var shape = go.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.SharpRect;
                shape.CornerRadius = ComicStyle.CornerSharp;
                shape.BorderWidth = 0f;
                shape.color = color;
                shape.HardShadow = false;
                shape.Halftone = false;
                shape.raycastTarget = false;
            }
        }

        /// <summary>确定性伪随机（线性同余）：每次进首页星点位置一致，截图可比对。</summary>
        private static float NextUnit(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return (state >> 8) * (1f / 16777216f);
        }

        /// <summary>左侧 Hero 大卡：主行动色实底 + Hero 硬影 + PLAY 角签 + 标题群 + 规则板 + CTA。</summary>
        private void BuildHomeHero(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            HomeBox box = m.HeroCard;
            float heroWidth = _homeBoard.HeroWidth;

            var cardGo = new GameObject("HomeHeroCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            cardGo.transform.SetParent(_menuSafeArea, false);
            var card = (RectTransform)cardGo.transform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = new Vector2(box.X, -_homeBoard.Top);
            card.sizeDelta = new Vector2(heroWidth, _homeBoard.Height);
            _homeHeroCard = card;

            var bg = cardGo.GetComponent<UIInkShapeGraphic>();
            bg.Shape = UIInkShape.SharpRect;
            bg.CornerRadius = ComicStyle.CornerSharp;
            bg.BorderWidth = m.CardBorderWidth;
            bg.BorderColor = ComicStyle.Ink;
            bg.color = ComicStyle.Yellow;
            bg.GlossAlpha = 0f;
            bg.Halftone = false;
            bg.raycastTarget = false;
            theme.ApplyHardShadow(bg, UIHardShadowLevel.Hero);

            var motion = cardGo.GetComponent<UISpringMotion>();
            motion.SetTheme(theme);
            _homeEntrance.Add(motion);

            float innerWidth = heroWidth - m.HeroPadding.x * 2f;

            // 1. 顶部 Header：左上「PLAY」硬角签 + 右侧「4V4 · TURF WAR」潮流机能微标，清爽大气
            CreateStageTag(card, "HeroTag", "PLAY", m.TagSize, m.TagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, m.HeroPadding, TextAlignmentOptions.Center);

            float badgeFont = UITypography.FontSnapped(mobile ? 22f : 20f);
            float badgeX = m.HeroPadding.x + m.TagSize.x + 16f;
            float badgeWidth = Mathf.Max(120f, innerWidth - m.TagSize.x - 16f);
            CreateBoardText(card, "HeroModeBadge", "4V4 · TURF WAR", badgeFont, true,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                new Vector2(badgeX, m.HeroPadding.y + (m.TagSize.y - badgeFont * 1.4f) * 0.5f),
                new Vector2(badgeWidth, badgeFont * 1.4f),
                TextAlignmentOptions.MidlineLeft, 2f, FontStyles.Bold);

            // 2. 中间区域：专为「武器整备卡」预留黄金展示位（由 WeaponSelectView 对齐 HeroWeaponSlot 渲染，白底黑边+硬阴影）
            // 桌面端在 CTA 上方添加极淡极简的操作提示微字，贴心且不喧宾夺主
            if (!mobile)
            {
                float hintFont = UITypography.FontSnapped(16f);
                float ctaTop = _homeBoard.Top + _homeBoard.Height - m.HeroPadding.y - m.CtaSize.y;
                float hintY = (ctaTop - _homeBoard.Top) - hintFont * 1.6f - 8f;
                CreateBoardText(card, "HeroKeyHint", "SPACE / ENTER TO BATTLE", hintFont, true,
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.40f),
                    new Vector2(m.HeroPadding.x, hintY),
                    new Vector2(innerWidth, hintFont * 1.6f),
                    TextAlignmentOptions.Center, 2f, FontStyles.Bold);
            }

            // 3. 主 CTA：复用场景里的 StartButton（保持 MenuSafeArea 直接子级）
            if (startButton != null) PlaceHomeCta(theme, m, mobile);
        }

        /// <summary>把场景里的 StartButton 摆到 Hero 卡底部（尺寸来自版面表，窄屏时收窄但不改写设计值）。</summary>
        private void PlaceHomeCta(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            var btnRect = startButton.transform as RectTransform;
            if (btnRect == null) return;

            ReparentInto(startButton.transform, _menuSafeArea);
            // Hero 卡等运行时元素是 MenuSafeArea 的较新兄弟节点；CTA 必须置顶，否则会被不透明卡面盖住。
            startButton.transform.SetAsLastSibling();

            float heroWidth = _homeHeroCard != null ? _homeHeroCard.sizeDelta.x : m.HeroBox.x;
            float ctaWidth = Mathf.Min(m.CtaSize.x, heroWidth - m.HeroPadding.x * 2f);
            float ctaTop = _homeBoard.Top + _homeBoard.Height - m.HeroPadding.y - m.CtaSize.y;

            btnRect.anchorMin = new Vector2(0f, 1f);
            btnRect.anchorMax = new Vector2(0f, 1f);
            btnRect.pivot = new Vector2(0f, 1f);
            btnRect.anchoredPosition = new Vector2(m.HeroCta.X, -ctaTop);
            btnRect.sizeDelta = new Vector2(ctaWidth, m.CtaSize.y);

            StyleStartButtonGeometric(theme, UITypography.FontSnapped(m.CtaFont), mobile);
        }

        /// <summary>右侧 2×2 次级卡：战绩（信息）/ 音效（可点）/ 操作指南或键位（信息）/ 检查更新（可点）。</summary>
        private void BuildHomeTiles(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            try
            {
                float volume = GameSettingsService.MasterVolume;
                if (volume > 0.001f) _audioVolumeBeforeMute = Mathf.Clamp(volume, 0.05f, 1f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 读取主音量失败：" + ex.Message);
            }

            BuildHomeTile(new HomeTileSpec
            {
                Index = 0,
                Tag = "RECORD",
                Title = "战绩",
                Info = BuildRecordInfo(),
                Accent = ComicStyle.Green,
                Interactive = true,
                ClickAction = () => ShowHomeInfo(HomeInfoTab.Record),
            }, theme, m, mobile, out _homeRecordInfo, out _);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 1,
                Tag = "AUDIO",
                Title = "音效 · " + (IsAudioMuted ? "关" : "开"),
                Info = BuildAudioInfo(),
                Accent = theme.teamAColor,
                Interactive = true,
                ClickAction = ToggleHomeAudio,
            }, theme, m, mobile, out _homeAudioInfo, out _homeAudioTitle);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 2,
                Tag = mobile ? "GUIDE" : "KEYS",
                Title = mobile ? "操作指南" : "键位说明",
                Info = mobile
                    ? "摇杆 移动 · 射击键 开火 · 跳跃键 起跳 · 大招键 释放"
                    : "WASD 移动 · 左键 射击 · 空格 跳跃 · Q 大招",
                Accent = ComicStyle.Blue,
                Interactive = true,
                ClickAction = () => ShowHomeInfo(HomeInfoTab.Controls),
            }, theme, m, mobile, out _homeKeysInfo, out _);

            HomeTileView updateTile = BuildHomeTile(new HomeTileSpec
            {
                Index = 3,
                Tag = "UPDATE",
                Title = "检查更新",
                Info = "联网检查新版本 · 当前 " + GameVersion.APP_VERSION_NAME,
                Accent = ComicStyle.Yellow,
                Interactive = true,
            }, theme, m, mobile, out _, out _);

            // 检查更新按钮铺满整张卡（透明 Image 只负责射线），视觉完全交给卡片
            AttachMenuButtonToTile(checkUpdateButton, updateTile);

            // 移动端顶部右侧「自定义按键」小按钮（纸白底 + 墨框 + Button 硬影）
            if (mobile) StyleTopKeyButton(theme, m);
        }

        private struct HomeTileSpec
        {
            public int Index;
            public string Tag;
            public string Title;
            public string Info;
            public Color Accent;
            public bool Interactive;
            public System.Action ClickAction;
        }

        /// <summary>一张次级卡的运行时引用（复用已有按钮 / 刷新读数时用）。</summary>
        private sealed class HomeTileView
        {
            public RectTransform Root;
            public RectTransform Content;
            public UIInkShapeGraphic Shape;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Info;
        }

        private HomeTileView BuildHomeTile(HomeTileSpec spec, UIThemeSO theme, HomeMenuMetrics m, bool mobile,
            out TextMeshProUGUI infoText, out TextMeshProUGUI titleText)
        {
            infoText = null;
            titleText = null;

            int col = spec.Index % 2;
            int row = spec.Index / 2;
            float tileX = _homeBoard.GridX + col * (_homeBoard.TileWidth + m.TileGap);
            float tileTop = _homeBoard.Top + row * (_homeBoard.TileHeight + m.TileGap);
            Vector2 tileSize = new Vector2(_homeBoard.TileWidth, _homeBoard.TileHeight);

            var go = new GameObject("HomeTile_" + spec.Tag, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            go.transform.SetParent(_menuSafeArea, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(tileX, -tileTop);
            root.sizeDelta = tileSize;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = m.TileBorderWidth;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = spec.Interactive;
            theme.ApplyHardShadow(shape, UIHardShadowLevel.Panel);

            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(theme);
            _homeEntrance.Add(motion);

            // 顶部 5px 身份色带（规范：色带不承载文字）
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(root, false);
            var bandRect = (RectTransform)bandGo.transform;
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = new Vector2(1f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.sizeDelta = new Vector2(-m.TileBorderWidth * 2f, m.TileBandHeight);
            bandRect.anchoredPosition = new Vector2(0f, -m.TileBorderWidth);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.SharpRect;
            bandShape.CornerRadius = ComicStyle.CornerSharp;
            bandShape.BorderWidth = 0f;
            bandShape.color = spec.Accent;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;

            // 内容容器：按下时整体向阴影方向位移（UIJellyButton 压印）
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(root, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(m.TileBorderWidth, m.TileBorderWidth);
            content.offsetMax = new Vector2(-m.TileBorderWidth, -m.TileBorderWidth);

            float contentWidth = tileSize.x - m.TileBorderWidth * 2f;
            float contentHeight = tileSize.y - m.TileBorderWidth * 2f;

            CreateStageTag(content, "TileTag", spec.Tag, m.TileTagSize, m.TileTagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, m.TilePadding, TextAlignmentOptions.Center);

            float titleSize = UITypography.FontSnapped(m.TileTitleFont);
            float titleTop = m.TilePadding.y + m.TileTagSize.y + (mobile ? 20f : 18f);
            titleText = CreateBoardText(content, "TileTitle", spec.Title, titleSize, true, ComicStyle.Ink,
                new Vector2(m.TilePadding.x, titleTop),
                new Vector2(contentWidth - m.TilePadding.x * 2f, titleSize * 1.25f),
                TextAlignmentOptions.MidlineLeft, -2f, FontStyles.Bold);

            float infoSize = UITypography.FontSnapped(m.TileInfoFont);
            float infoHeight = infoSize * 1.7f;
            float arrowSize = UITypography.FontSnapped(m.TileArrowFont);
            float arrowWidth = spec.Interactive ? arrowSize * 1.2f : 0f;
            float infoTop = contentHeight - m.TilePadding.y - infoHeight;

            infoText = CreateBoardText(content, "TileInfo", spec.Info, infoSize, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                new Vector2(m.TilePadding.x, infoTop),
                new Vector2(contentWidth - m.TilePadding.x * 2f - arrowWidth, infoHeight),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            if (spec.Interactive)
            {
                // `》` 行进箭头：只有可点卡才放（信息卡不放箭头，避免"假可点"）
                CreateBoardText(content, "TileArrow", "》", arrowSize, true, ComicStyle.Ink,
                    new Vector2(m.TilePadding.x, infoTop - (arrowSize * 1.1f - infoHeight) * 0.5f),
                    new Vector2(contentWidth - m.TilePadding.x * 2f, arrowSize * 1.1f),
                    TextAlignmentOptions.MidlineRight, 0f, FontStyles.Bold);

                AttachTileInteraction(go, shape, content, spec.ClickAction);
            }

            return new HomeTileView { Root = root, Content = content, Shape = shape, Title = titleText, Info = infoText };
        }

        /// <summary>整卡可点：自带 Button（音效）或复用的旧按钮（检查更新）都套同一套按压物理。</summary>
        private void AttachTileInteraction(GameObject go, UIInkShapeGraphic shape, RectTransform content,
            System.Action clickAction)
        {
            // 复用旧按钮的卡（检查更新）没有自带 clickAction：按压物理由 AttachMenuButtonToTile
            // 挂到真正的 Button 上（uGUI 只把指针事件派给最深的 handler）。
            if (clickAction == null) return;

            var button = go.AddComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => clickAction());

            var jelly = go.GetComponent<UIJellyButton>();
            if (jelly == null) jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(UIThemeSO.Resolve(uiTheme), button, shape);
            jelly.SetPressContent(content);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Panel);
        }

        /// <summary>
        /// 把旧按钮铺满整张卡（透明 Image 只负责射线 + 旧监听），视觉交给卡片。
        /// 按压物理挂在按钮自己身上：uGUI 的 ExecuteHierarchy 只把指针事件派给最深的 handler，
        /// 挂在卡片根节点上的 UIJellyButton 收不到子按钮里按下的事件。
        /// </summary>
        private void AttachMenuButtonToTile(Button button, HomeTileView tile)
        {
            if (button == null || tile == null || tile.Content == null) return;

            button.transform.SetParent(tile.Content, false);
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            var legacyLabel = button.GetComponentInChildren<Text>(true);
            if (legacyLabel != null) legacyLabel.gameObject.SetActive(false);

            var jelly = button.GetComponent<UIJellyButton>();
            if (jelly == null) jelly = button.gameObject.AddComponent<UIJellyButton>();
            jelly.Configure(UIThemeSO.Resolve(uiTheme), button, tile.Shape);
            jelly.SetPressContent(tile.Content);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Panel);

            button.gameObject.SetActive(true);
        }

        /// <summary>移动端顶部右侧「自定义按键」小按钮：纸白底 + 3px 墨框 + Button 硬影。</summary>
        private void StyleTopKeyButton(UIThemeSO theme, HomeMenuMetrics m)
        {
            Transform node = FindMenuNode("CustomLayoutButton");
            if (node == null) return;

            ReparentInto(node, _menuSafeArea);
            var rect = node.GetComponent<RectTransform>();
            if (rect == null) return;

            HomeBox box = m.TopButton;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-m.Margin, -box.Y);
            rect.sizeDelta = box.Size;

            var img = node.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            var shape = node.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (shape == null) shape = UIInkShapeGraphic.CreateChildBackground(rect, "GeometricBg");
            if (shape != null)
            {
                shape.Shape = UIInkShape.SharpRect;
                shape.CornerRadius = ComicStyle.CornerSharp;
                shape.BorderWidth = ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.Ink;
                shape.color = ComicStyle.Paper;
                shape.Halftone = false;
                shape.GlossAlpha = 0f;
                shape.raycastTarget = false;
                theme.ApplyHardShadow(shape, UIHardShadowLevel.Button);
            }

            var legacyLabel = node.GetComponentInChildren<Text>(true);
            if (legacyLabel != null) legacyLabel.gameObject.SetActive(false);

            var label = CreateOrReplaceTmpLabel(rect, "LabelTmp", "自定义按键",
                UITypography.FontSnapped(m.SecondaryFont), false, ComicStyle.Ink, TextAlignmentOptions.Center);

            var btn = node.GetComponent<Button>();
            if (btn != null && shape != null)
            {
                var jelly = node.GetComponent<UIJellyButton>();
                if (jelly == null) jelly = node.gameObject.AddComponent<UIJellyButton>();
                jelly.Configure(theme, btn, shape);
                if (label != null) jelly.SetPressContent(label.rectTransform);
                jelly.EnableThemeHardShadow(UIHardShadowLevel.Button);
            }

            node.gameObject.SetActive(true);
        }

        /// <summary>左下 / 右下黑底角签：版本号与社区位，落在卡片区之外（移动端左下设让给整备卡）。</summary>
        private void BuildHomeCornerTags(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            HomeBox footer = m.FooterTag;
            if (!footer.IsEmpty)
            {
                CreateFooterTag("HomeFooterTag", footer, "游戏共创 · 单机 4V4", theme,
                    ComicStyle.Ink, ComicStyle.Paper, m.FooterTagFont);
            }

            CreateFooterTag("HomeVersionTag", m.VersionTag,
                GameVersion.APP_NAME + " " + GameVersion.APP_VERSION_NAME, theme,
                ComicStyle.Ink, ComicStyle.Yellow, m.FooterTagFont);
        }

        private void CreateFooterTag(string name, HomeBox box, string label, UIThemeSO theme,
            Color fill, Color textColor, float fontBase)
        {
            if (box.IsEmpty || _menuSafeArea == null) return;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(_menuSafeArea, false);
            var rect = (RectTransform)go.transform;

            // 右下角签锚到右边（刘海屏安全区变窄时不会被推出屏幕），左下角签锚到左边。
            bool anchorRight = box.X > MenuLayoutMath.RefWidth * 0.5f;
            Vector2 anchor = anchorRight ? new Vector2(1f, 0f) : Vector2.zero;
            float y = MenuLayoutMath.RefHeight - box.Bottom;
            float x = anchorRight ? -(MenuLayoutMath.RefWidth - box.Right) : box.X;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = box.Size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            var tmp = CreateBoardText(rect, "Text", label, UITypography.FontSnapped(fontBase), false, textColor,
                new Vector2(10f, 0f), new Vector2(box.Width - 20f, box.Height),
                TextAlignmentOptions.MidlineLeft, 1.5f, FontStyles.Bold);
            if (tmp != null) tmp.fontStyle |= FontStyles.UpperCase;
        }

        /// <summary>左上角签（PLAY / RECORD / AUDIO …）：实底 + 全大写 + 字距 2 + Tag 硬影。</summary>
        private TextMeshProUGUI CreateStageTag(RectTransform parent, string name, string label,
            Vector2 size, float fontBase, UIThemeSO theme, Color fill, Color textColor,
            Vector2 topLeft, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            var tmp = CreateBoardText(rect, "Text", label, UITypography.FontSnapped(fontBase), true, textColor,
                new Vector2(10f, 0f), new Vector2(Mathf.Max(20f, size.x - 20f), size.y), align, 2f, FontStyles.Bold);
            if (tmp != null) tmp.fontStyle |= FontStyles.UpperCase;
            return tmp;
        }

        /// <summary>卡片内文字：左上锚点 + 固定盒 + NoWrap（TMP 的 Truncate 会纵向吃字，禁止使用）。</summary>
        private TextMeshProUGUI CreateBoardText(Transform parent, string name, string text, float fontSize,
            bool display, Color color, Vector2 topLeft, Vector2 size, TextAlignmentOptions align,
            float spacing = 0f, FontStyles style = FontStyles.Bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.characterSpacing = spacing;
            tmp.alignment = align;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var font = UIThemeSO.Resolve(uiTheme).ResolveFont(display, text);
            if (font != null) tmp.font = font;
            return tmp;
        }

        /// <summary>建/复用一块铺满的 TMP 标签（用于复用旧按钮时替换 Legacy Text）。</summary>
        private TextMeshProUGUI CreateOrReplaceTmpLabel(RectTransform owner, string name, string text,
            float fontSize, bool display, Color color, TextAlignmentOptions align)
        {
            if (owner == null) return null;

            Transform existing = owner.Find(name);
            TextMeshProUGUI tmp = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(owner, false);
                tmp = go.GetComponent<TextMeshProUGUI>();
            }

            if (tmp == null) return null;

            var rect = tmp.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = align;
            tmp.color = color;
            tmp.characterSpacing = 1.5f;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;

            var font = UIThemeSO.Resolve(uiTheme).ResolveFont(display, text);
            if (font != null) tmp.font = font;
            return tmp;
        }

        /// <summary>音效是否已静音（主音量 0 视为静音）。</summary>
        private static bool IsAudioMuted
        {
            get
            {
                try
                {
                    return GameSettingsService.MasterVolume <= 0.001f;
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        private static string BuildAudioInfo()
        {
            try
            {
                int percent = Mathf.RoundToInt(Mathf.Clamp01(GameSettingsService.MasterVolume) * 100f);
                return percent <= 0 ? "已静音 · 点击恢复" : $"主音量 {percent}% · 点击静音";
            }
            catch (System.Exception)
            {
                return "主音量 — · 点击切换";
            }
        }

        private static string BuildRecordInfo()
        {
            try
            {
                PlayerProfile profile = PlayerProfile.Current;
                if (profile != null)
                {
                    return $"LV.{profile.Level} · 胜场 {profile.Wins} / {profile.Matches} 局 · 总涂地 {profile.TotalTurf:0}㎡";
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 战绩卡读取档案失败：" + ex.Message);
            }

            return "LV.1 · 胜场 0 / 0 局 · 总涂地 0㎡";
        }

        /// <summary>首页读数（战绩 / 音效）刷新：回到主菜单时数值会变。</summary>
        private void RefreshHomeInfo()
        {
            if (_homeRecordInfo != null) _homeRecordInfo.text = BuildRecordInfo();
            if (_homeAudioInfo != null) _homeAudioInfo.text = BuildAudioInfo();
            if (_homeAudioTitle != null) _homeAudioTitle.text = IsAudioMuted ? "音效 · 关" : "音效 · 开";
            if (_homeKeysInfo != null)
            {
                _homeKeysInfo.text = UITypography.IsMobileLike
                    ? "摇杆 移动 · 射击键 开火 · 跳跃键 起跳 · 大招键 释放"
                    : "WASD 移动 · 左键 射击 · 空格 跳跃 · Q 大招";
            }
        }

        /// <summary>音效卡：一键静音 / 恢复（写 GameSettingsService，与设置面板共用同一份存档）。</summary>
        private void ToggleHomeAudio()
        {
            try
            {
                float current = GameSettingsService.MasterVolume;
                if (current > 0.001f)
                {
                    _audioVolumeBeforeMute = Mathf.Clamp(current, 0.05f, 1f);
                    GameSettingsService.MasterVolume = 0f;
                }
                else
                {
                    GameSettingsService.MasterVolume = _audioVolumeBeforeMute;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 音效开关失败：" + ex.Message);
            }

            RefreshHomeInfo();
        }

        private void PlayHomeEntrance()
        {
            PlayStaggered(_homeEntrance);
        }

        /// <summary>结算卡入场：两块比分卡先到，判定牌/战报卡各自在揭晓时再 pop-in。</summary>
        private void PlayResultEntrance()
        {
            _resultEntrance.Clear();
            if (_teamCardA != null) _resultEntrance.Add(_teamCardA.GetComponent<UISpringMotion>());
            if (_teamCardB != null) _resultEntrance.Add(_teamCardB.GetComponent<UISpringMotion>());
            PlayStaggered(_resultEntrance);
        }

        /// <summary>
        /// 入场：0.22s Back-Out + 主题 entranceStagger（0.05s/张）错峰。
        /// PlayIn(delay&gt;0) 在延迟期间不会写 alpha，这里先手动压成「缩小 + 透明」，
        /// 否则后面的卡会先以正常尺寸闪一帧再重新播放入场。
        /// </summary>
        private void PlayStaggered(List<UISpringMotion> motions, float firstDelay = 0f)
        {
            if (motions == null || motions.Count == 0) return;
            float stagger = Mathf.Max(0.01f, UIThemeSO.Resolve(uiTheme).entranceStagger);

            for (int i = 0; i < motions.Count; i++)
            {
                UISpringMotion motion = motions[i];
                if (motion == null || !motion.gameObject.activeInHierarchy) continue;

                motion.Build();
                motion.SnapHome();
                motion.PlayIn(firstDelay + i * stagger);

                var group = motion.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 0f;
                motion.transform.localScale = Vector3.one * 0.9f;
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

        /// <summary>
        /// 开赛 CTA（Hero 卡底部）：墨底 + 金黄粗体「开始游戏 》》」+ 按钮硬影 (3,-4) + 按压物理。
        /// 旧版左侧墨色竖条在墨底上不可见，节点保留但隐藏（避免旧场景残留引用报错）。
        /// </summary>
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
            btnShape.BorderWidth = 0f;
            btnShape.BorderColor = ComicStyle.Ink;
            btnShape.Halftone = false;
            btnShape.GlossAlpha = 0f;
            btnShape.color = ComicStyle.Ink; // 墨底，金黄字
            btnShape.raycastTarget = true;
            resolved.ApplyHardShadow(btnShape, UIHardShadowLevel.Button);

            Transform accent = btnShape.transform.Find("CtaAccent");
            if (accent != null) accent.gameObject.SetActive(false);

            // Legacy Text 的 bold 对中文不生效，且 LiberationSans 没有中文字形（会显示 □）
            var legacyLabel = startButtonLabel != null ? startButtonLabel : startButton.GetComponentInChildren<Text>(true);
            if (legacyLabel != null) legacyLabel.gameObject.SetActive(false);

            var labelTmp = CreateOrReplaceTmpLabel(startButton.transform as RectTransform, "CtaLabel",
                "开始游戏  》》", fontSize, true, ComicStyle.Yellow, TextAlignmentOptions.Center);
            if (labelTmp != null)
            {
                labelTmp.characterSpacing = 3f;
                labelTmp.rectTransform.offsetMin = new Vector2(mobile ? 12f : 8f, 0f);
                labelTmp.rectTransform.offsetMax = new Vector2(mobile ? -12f : -8f, 0f);
            }

            var jelly = startButton.gameObject.GetComponent<UIJellyButton>();
            if (jelly == null) jelly = startButton.gameObject.AddComponent<UIJellyButton>();
            if (jelly != null)
            {
                jelly.Configure(resolved, startButton, btnShape);
                if (labelTmp != null) jelly.SetPressContent(labelTmp.rectTransform);
                jelly.EnableThemeHardShadow();
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
        /// 结算页（几何大胆风 · 大数字即设计）：
        /// 1. 顶部 Hero 级 VICTORY / DEFEAT 判定大牌（移动端 ≥120），不再另起一行解说文字；
        /// 2. 双方覆盖率百分比做成超大数字（桌面 104，移动端 138）分列左右队伍卡片；
        /// 3. 卡面只保留「我方 / 敌方」两个词与队色涂地条；
        /// 4. 战报单据 = 勋章 + LV + 经验条，一块白卡收拢，不再套灰框/子标签；
        /// 5. 关键 CTA「再来一局」Red 底白字直角大按钮 + 次级「返回」白底墨字。
        /// </summary>
        private void EnsureResultVisuals()
        {
            if (resultPanel == null || resultPanel.transform.Find("GeometricResultRoot") != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);
            bool mobile = UITypography.IsMobileLike;

            // 深色舞台底（与首页一致；整页界面不再铺白）
            var panelImg = resultPanel.GetComponent<Image>();
            if (panelImg != null)
            {
                panelImg.color = StageInk;
            }

            // 清理旧漫画网点背景
            Transform legacyHalf = resultPanel.transform.Find("HalftoneBg");
            if (legacyHalf != null) Destroy(legacyHalf.gameObject);

            var rootGo = new GameObject("GeometricResultRoot", typeof(RectTransform));
            rootGo.transform.SetParent(resultPanel.transform, false);
            var rootRt = (RectTransform)rootGo.transform;
            Stretch(rootRt);
            rootRt.SetAsFirstSibling();

            // 深色舞台上的装饰：低透明角标 + 稀疏星点（与首页同一套语言），
            // 过去四角各撒一个色块读起来像散落的控件，现在只做背景噪点。
            float markSize = mobile ? 52f : 48f;
            float markInset = mobile ? 44f : 40f;
            CreateGeometricSquare(rootRt, "ResultMarkTL", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(markInset, -markInset), new Vector2(markSize, markSize), 0f,
                ComicStyle.WithAlpha(ComicStyle.Paper, 0.10f));
            CreateGeometricSquare(rootRt, "ResultMarkTR", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-markInset, -markInset), new Vector2(markSize, markSize), 0f,
                ComicStyle.WithAlpha(ComicStyle.Red, 0.16f));
            CreateStarField(rootRt, mobile ? 24 : 34, 0x2E5A0901u);

            // 判定牌下方的横规：深舞台上用低透明纸白，给 Hero 大牌一个落地基线。
            float ruleY = mobile ? 262f : 264f;
            CreateGeometricSquare(rootRt, "ResultRule", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, ruleY), new Vector2(mobile ? 720f : 660f, ComicStyle.Border), 0f,
                ComicStyle.WithAlpha(ComicStyle.Paper, 0.18f));

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
                victoryBadgeBg.BorderColor = ComicStyle.Yellow;
                victoryBadgeBg.HardShadow = false;
                victoryBadgeBg.Halftone = false;
                victoryBadgeBg.color = ComicStyle.Ink;
                victoryBadgeBg.raycastTarget = false;
                // Hero 硬影 (6,-7)：判定牌是结算页唯一的主角块
                resolved.ApplyHardShadow(victoryBadgeBg, UIHardShadowLevel.Hero);

                var txtGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(victoryBadgeRoot, false);
                Stretch(txtGo.GetComponent<RectTransform>());
                victoryBadgeText = txtGo.GetComponent<TextMeshProUGUI>();
                victoryBadgeText.fontSize = UITypography.FontSnapped(96f); // 桌面 96，移动端 124（≥120）
                victoryBadgeText.fontStyle = FontStyles.Bold;
                victoryBadgeText.characterSpacing = 6f;
                victoryBadgeText.alignment = TextAlignmentOptions.Center;
                victoryBadgeText.color = ComicStyle.Ink;
                victoryBadgeText.text = "VICTORY";
                victoryBadgeText.raycastTarget = false;
                victoryBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
                victoryBadgeText.overflowMode = TextOverflowModes.Overflow;
                victoryBadgeText.font = resolved.ResolveFont(true, "VICTORY");

                // 像素硬影 (4,-4)：双层描边字，墨底黄字也有"套色偏移"的印刷感
                _victoryBadgeChunky = txtGo.AddComponent<UIChunkyText>();
                _victoryBadgeChunky.ApplyTheme(resolved);
                _victoryBadgeChunky.Configure(resolved, "VICTORY", victoryBadgeText.fontSize,
                    displayFont: true, doubleLayered: true, fill: ComicStyle.Yellow, outlinedText: true);
            }

            victoryBadgeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.pivot = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchoredPosition = new Vector2(0f, mobile ? 340f : 344f);
            victoryBadgeRoot.sizeDelta = new Vector2(mobile ? 720f : 660f, mobile ? 132f : 118f);
            victoryBadgeRoot.gameObject.SetActive(false);

            // 2. 左右分列队伍比分卡：队色标题条（谁）→ 超大百分比（多少）→ 队色涂地条（占比）。
            //    卡面只留「我方 / 敌方」两个词，不再写 TEAM A · 我方队伍 这类重复标签。
            float cardOffsetX = mobile ? 316f : 300f;
            float cardCenterY = mobile ? 88f : 86f;
            Vector2 cardSize = mobile ? new Vector2(520f, 250f) : new Vector2(470f, 232f);
            float tagHeight = mobile ? 54f : 50f;
            float barSlotH = mobile ? 22f : 20f;
            float barSlotY = -cardSize.y * 0.5f + barSlotH * 0.5f + (mobile ? 18f : 16f);

            // 2.1 队伍 A 比分卡（我方）
            GameObject teamAGo = CreateTeamScoreCard(rootRt, "TeamACard", resolved, Team.TeamA, "我方",
                new Vector2(-cardOffsetX, cardCenterY), cardSize, tagHeight);

            // 2.2 队伍 B 比分卡（敌方）
            GameObject teamBGo = CreateTeamScoreCard(rootRt, "TeamBCard", resolved, Team.TeamB, "敌方",
                new Vector2(cardOffsetX, cardCenterY), cardSize, tagHeight);

            _teamCardA = (RectTransform)teamAGo.transform;
            _teamCardB = (RectTransform)teamBGo.transform;

            // 2.3 胜方标记：贴在本方标题条右侧的黄底墨字「胜」，掷点结束后才点亮。
            float winnerTagW = mobile ? 56f : 52f;
            float winnerTagH = mobile ? 34f : 30f;
            _winnerTagA = CreateWinnerTag(teamAGo.transform, true, cardSize, tagHeight,
                new Vector2(winnerTagW, winnerTagH));
            _winnerTagB = CreateWinnerTag(teamBGo.transform, false, cardSize, tagHeight,
                new Vector2(winnerTagW, winnerTagH));

            // 3. 双方覆盖率百分比：卡面绝对主角（桌面 104 / 移动端 138）
            float bigNumberSize = UITypography.FontSnapped(mobile ? 108f : 104f);

            if (resultPercentA != null)
            {
                resultPercentA.transform.SetParent(teamAGo.transform, false);
                var pRtA = resultPercentA.rectTransform;
                pRtA.anchorMin = new Vector2(0.5f, 0.5f);
                pRtA.anchorMax = new Vector2(0.5f, 0.5f);
                pRtA.anchoredPosition = new Vector2(0f, -6f);
                pRtA.sizeDelta = new Vector2(cardSize.x - 32f, mobile ? 150f : 136f);
                resultPercentA.fontSize = Mathf.RoundToInt(bigNumberSize);
                resultPercentA.fontStyle = FontStyle.Bold;
                resultPercentA.alignment = TextAnchor.MiddleCenter;
                resultPercentA.color = ComicStyle.Ink;
                resultPercentA.horizontalOverflow = HorizontalWrapMode.Overflow;
                resultPercentA.verticalOverflow = VerticalWrapMode.Overflow;
                // Legacy Text 接不了 UIChunkyText（它只认 TMP_Text），用同形状不透明偏移影子
                // 得到完全等价的「像素硬影 (4,-4)」：零模糊、alpha 恒为 1。
                EnsureHardTextShadow(resultPercentA, resolved);
            }

            if (resultPercentB != null)
            {
                resultPercentB.transform.SetParent(teamBGo.transform, false);
                var pRtB = resultPercentB.rectTransform;
                pRtB.anchorMin = new Vector2(0.5f, 0.5f);
                pRtB.anchorMax = new Vector2(0.5f, 0.5f);
                pRtB.anchoredPosition = new Vector2(0f, -6f);
                pRtB.sizeDelta = new Vector2(cardSize.x - 32f, mobile ? 150f : 136f);
                resultPercentB.fontSize = Mathf.RoundToInt(bigNumberSize);
                resultPercentB.fontStyle = FontStyle.Bold;
                resultPercentB.alignment = TextAnchor.MiddleCenter;
                resultPercentB.color = ComicStyle.Ink;
                resultPercentB.horizontalOverflow = HorizontalWrapMode.Overflow;
                resultPercentB.verticalOverflow = VerticalWrapMode.Overflow;
                // Legacy Text 接不了 UIChunkyText（它只认 TMP_Text），用同形状不透明偏移影子
                // 得到完全等价的「像素硬影 (4,-4)」：零模糊、alpha 恒为 1。
                EnsureHardTextShadow(resultPercentB, resolved);
            }

            // 4. 直角粗墨水柱（放入各自卡片底部）

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

            // 5. 判定文字退场：顶部大牌 + 两个巨大百分比已经把胜负说完，
            //    屏幕中间不再出现「正在计算… / 惜败 · 再接再厉」这类第二句结论。
            if (verdictText != null)
            {
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            // 5b. 战报单据（勋章 + LV + 经验条合为一块白卡）
            BuildReportPanel(rootRt, resolved, mobile);

            // 6. 再来一局 CTA（主行动黄 + 墨字 + Button 硬影）+ 次级「返回」（纸白 + 墨框）
            float btnY = mobile ? -400f : -390f;
            Vector2 restartSize = mobile ? new Vector2(390f, 104f) : new Vector2(340f, 92f);
            Vector2 backSize = mobile ? new Vector2(200f, 104f) : new Vector2(180f, 92f);
            float btnGap = mobile ? 28f : 24f;
            float restartX = -(backSize.x + btnGap) * 0.5f; // 让「重开 + 返回」整组落在屏幕中线上
            float backX = restartX + restartSize.x * 0.5f + btnGap + backSize.x * 0.5f;

            if (restartButton != null)
            {
                var rRt = restartButton.transform as RectTransform;
                if (rRt != null)
                {
                    rRt.anchorMin = new Vector2(0.5f, 0.5f);
                    rRt.anchorMax = new Vector2(0.5f, 0.5f);
                    rRt.anchoredPosition = new Vector2(restartX, btnY);
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
                    rShape.BorderWidth = ComicStyle.Border;
                    rShape.BorderColor = ComicStyle.Ink;
                    rShape.color = ComicStyle.Yellow; // 主行动色（红=危险，不挪用到主 CTA）
                    rShape.Halftone = false;
                    rShape.GlossAlpha = 0f;
                    rShape.raycastTarget = false;
                    resolved.ApplyHardShadow(rShape, UIHardShadowLevel.Button);
                }

                // LiberationSans 没有中文字形（旧版这里就是 □），统一换 TMP
                var legacyRestartLabel = restartButton.GetComponentInChildren<Text>(true);
                if (legacyRestartLabel != null) legacyRestartLabel.gameObject.SetActive(false);

                var rTmp = CreateOrReplaceTmpLabel(rRt, "RestartLabel", "再来一局",
                    UITypography.FontSnapped(30f), false, ComicStyle.Ink, TextAlignmentOptions.Center);

                var jelly = restartButton.GetComponent<UIJellyButton>();
                if (jelly == null) jelly = restartButton.gameObject.AddComponent<UIJellyButton>();
                jelly.Configure(resolved, restartButton, rShape);
                if (rTmp != null) jelly.SetPressContent(rTmp.rectTransform);
                jelly.EnableThemeHardShadow();
            }

            // 7. 次级「返回」按钮（纸白底 + 3px 墨框 + Button 硬影，复用现有重开路径）
            Transform backBtnTrans = resultPanel.transform.Find("ResultBackButton");
            if (backBtnTrans == null)
            {
                var backGo = new GameObject("ResultBackButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                backGo.transform.SetParent(resultPanel.transform, false);
                backBtnTrans = backGo.transform;

                var bImg = backGo.GetComponent<Image>();
                bImg.color = new Color(1f, 1f, 1f, 0f);
                bImg.raycastTarget = true;

                var bShape = UIInkShapeGraphic.CreateChildBackground((RectTransform)backGo.transform, "BackBg");
                bShape.Shape = UIInkShape.SharpRect;
                bShape.CornerRadius = ComicStyle.CornerSharp;
                bShape.BorderWidth = ComicStyle.BorderThin;
                bShape.BorderColor = ComicStyle.Ink;
                bShape.color = ComicStyle.Paper; // 白底墨字
                bShape.HardShadow = false;
                bShape.Halftone = false;
                bShape.raycastTarget = false;
                resolved.ApplyHardShadow(bShape, UIHardShadowLevel.Button);

                var bBtn = backGo.GetComponent<Button>();
                bBtn.targetGraphic = bImg;
                bBtn.onClick.AddListener(OnStartClicked);

                var bLabel = CreateOrReplaceTmpLabel((RectTransform)backGo.transform, "Label", "返回",
                    UITypography.FontSnapped(26f), false, ComicStyle.Ink, TextAlignmentOptions.Center);

                var bJelly = backGo.AddComponent<UIJellyButton>();
                bJelly.Configure(resolved, bBtn, bShape);
                if (bLabel != null) bJelly.SetPressContent(bLabel.rectTransform);
                bJelly.EnableThemeHardShadow();
            }

            // 位置与文案每次结算都重写：保证与「再来一局」同高、同基线、整组居中。
            if (backBtnTrans != null)
            {
                var bRt = backBtnTrans as RectTransform;
                if (bRt != null)
                {
                    bRt.anchorMin = new Vector2(0.5f, 0.5f);
                    bRt.anchorMax = new Vector2(0.5f, 0.5f);
                    bRt.anchoredPosition = new Vector2(backX, btnY);
                    bRt.sizeDelta = backSize;

                    CreateOrReplaceTmpLabel(bRt, "Label", "返回",
                        UITypography.FontSnapped(26f), false, ComicStyle.Ink, TextAlignmentOptions.Center);
                }
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
        /// 几何风扁平化兜底：清掉斜切高光、内沿墨光、网点与非法阴影。
        /// t-1 解禁了「零模糊硬偏移阴影」，因此这里**不再一律清零硬影**：
        /// 只保留档位表里的偏移（色签/按钮/悬停/面板/Hero/弹窗），其它脏值就地清掉，
        /// 保证结算页既有层级，又不会继承旧场景里五颜六色的投影。
        /// </summary>
        private static void FlattenShape(UIInkShapeGraphic shape)
        {
            if (shape == null) return;
            shape.DrawGloss = false;
            shape.GlossAlpha = 0f;
            shape.InnerRimAlpha = 0f;
            shape.Halftone = false;
            shape.HalftoneAlpha = 0f;
            shape.CornerRadius = ComicStyle.CornerSharp;

            if (!shape.HardShadow) return;

            Vector2 offset = shape.ShadowOffset;
            bool sanctioned =
                IsShadowOffset(offset, ComicStyle.ShadowTag) ||
                IsShadowOffset(offset, ComicStyle.ShadowButton) ||
                IsShadowOffset(offset, ComicStyle.ShadowButtonHover) ||
                IsShadowOffset(offset, ComicStyle.ShadowPanel) ||
                IsShadowOffset(offset, ComicStyle.ShadowHero) ||
                IsShadowOffset(offset, ComicStyle.ShadowDialog);

            if (sanctioned)
            {
                // 硬影必须是不透明纯墨黑（规范禁止半透明 / 带色投影）
                shape.ShadowColor = ComicStyle.Ink;
                return;
            }

            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
        }

        private static bool IsShadowOffset(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.01f && Mathf.Abs(a.y - b.y) < 0.01f;
        }

        /// <summary>
        /// Legacy Text 的像素硬影：<see cref="UIChunkyText"/> 只认 TMP_Text，
        /// uGUI 的 <see cref="Shadow"/> 在只有 effectDistance、没有 blur 时就是「同形状不透明偏移副本」，
        /// 与 TMP 的 chunky 像素硬影 (4,-4) 完全等价（零模糊、alpha 恒为 1）。
        /// </summary>
        private static void EnsureHardTextShadow(Text label, UIThemeSO theme)
        {
            if (label == null) return;

            var shadow = label.GetComponent<Shadow>();
            if (shadow == null) shadow = label.gameObject.AddComponent<Shadow>();

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            shadow.effectColor = resolved.HardShadowOpaqueColor;
            shadow.effectDistance = resolved.chunkyShadowOffsetPixels;
            shadow.useGraphicAlpha = false;
        }

        /// <summary>
        /// 比分卡外壳：白底墨框直角卡 + 顶部队色标题条。
        /// 标题条只写「我方 / 敌方」两个词，避免 TEAM A · 我方队伍 这种同义重复。
        /// </summary>
        private GameObject CreateTeamScoreCard(RectTransform parent, string name, UIThemeSO resolved,
            Team team, string label, Vector2 position, Vector2 size, float headerHeight)
        {
            var cardGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            cardGo.transform.SetParent(parent, false);
            var cardRt = (RectTransform)cardGo.transform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = position;
            cardRt.sizeDelta = size;

            var cardShape = cardGo.GetComponent<UIInkShapeGraphic>();
            cardShape.Shape = UIInkShape.SharpRect;
            cardShape.CornerRadius = ComicStyle.CornerSharp;
            cardShape.BorderWidth = ComicStyle.BorderHeavy;
            cardShape.BorderColor = ComicStyle.Ink;
            cardShape.color = ComicStyle.Paper;
            cardShape.HardShadow = false;
            // 纸白卡浮在深舞台上：面板硬影 (4,-5) 才有"离地"的层次
            resolved.ApplyHardShadow(cardShape, UIHardShadowLevel.Panel);

            var cardMotion = cardGo.GetComponent<UISpringMotion>();
            cardMotion.SetTheme(resolved);

            var headerGo = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            headerGo.transform.SetParent(cardGo.transform, false);
            var headerRt = (RectTransform)headerGo.transform;
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = Vector2.zero;
            headerRt.sizeDelta = new Vector2(0f, headerHeight);

            var headerShape = headerGo.GetComponent<UIInkShapeGraphic>();
            headerShape.Shape = UIInkShape.SharpRect;
            headerShape.CornerRadius = ComicStyle.CornerSharp;
            // 标题条自带墨色粗框：卡片边框被子物体盖住时，边框不能在标题处断开。
            headerShape.BorderWidth = ComicStyle.BorderHeavy;
            headerShape.BorderColor = ComicStyle.Ink;
            headerShape.color = resolved.GetTeamColor(team);
            headerShape.HardShadow = false;
            headerShape.raycastTarget = false;

            var labelTmp = CreateTmpText(headerGo.transform, "Label", label,
                UITypography.FontSnapped(24f), isDisplay: true, ComicStyle.Ink, Vector2.zero,
                new Vector2(size.x, headerHeight), 4f, TextAlignmentOptions.Center);
            labelTmp.fontStyle = FontStyles.Bold;

            return cardGo;
        }

        /// <summary>
        /// 结算页「胜方」标记：黄底墨字直角小牌「胜」，贴在本方标题条右侧。
        /// 建好即隐藏，等覆盖率掷点结束由 <see cref="ApplyWinnerEmphasis"/> 点亮。
        /// </summary>
        private RectTransform CreateWinnerTag(Transform card, bool isTeamA,
            Vector2 cardSize, float headerHeight, Vector2 tagSize)
        {
            var go = new GameObject(isTeamA ? "WinnerTagA" : "WinnerTagB",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(card, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                cardSize.x * 0.5f - ComicStyle.BorderHeavy - 10f - tagSize.x * 0.5f,
                cardSize.y * 0.5f - headerHeight * 0.5f);
            rect.sizeDelta = tagSize;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Yellow;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            var label = CreateTmpText(go.transform, "Label", "胜", UITypography.FontSnapped(20f),
                isDisplay: true, ComicStyle.Ink, Vector2.zero, tagSize, 0f, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;

            go.SetActive(false);
            return rect;
        }

        /// <summary>
        /// 掷点结束后强调胜方：胜方挂 WINNER 牌，败方比分数字降为 45% 墨色，
        /// 让观众一眼读出差值，而不是两张完全等价的卡片。
        /// </summary>
        private void ApplyWinnerEmphasis(bool playerWon)
        {
            if (_winnerEmphasisApplied) return;
            _winnerEmphasisApplied = true;

            if (_winnerTagA != null) _winnerTagA.gameObject.SetActive(playerWon);
            if (_winnerTagB != null) _winnerTagB.gameObject.SetActive(!playerWon);

            Color dim = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.6f);
            if (resultPercentA != null) resultPercentA.color = playerWon ? ComicStyle.Ink : dim;
            if (resultPercentB != null) resultPercentB.color = playerWon ? dim : ComicStyle.Ink;
        }

        /// <summary>
        /// 战报单据（结算页下半屏唯一主体）：白底墨框直角卡 + 左侧墨色竖栏，内部三行：
        /// 1) 勋章 chips（左）+ 本局经验（右）；
        /// 2) 大号 LV 等级 + 段位名；
        /// 3) 全宽经验条。
        ///
        /// 去掉了 MATCH REPORT / YOUR MEDALS / 1232 / 1850 XP 这些标签与嵌套灰框：
        /// 勋章色块、巨大 LV 与经验条本身就说明一切，少一层框就少一层"后台表格"感。
        /// 升级提示也不再浮在内容上，而是变成卡片顶边的一枚黄色页签。
        /// </summary>
        private void BuildReportPanel(RectTransform parent, UIThemeSO resolved, bool mobile)
        {
            if (_reportPanelRoot != null) return;

            float frameW = mobile ? 900f : 840f;
            float frameH = mobile ? 216f : 200f;
            float frameY = mobile ? -204f : -196f;
            const float bandH = 5f; // 顶部身份色带（规范：5px，不承载文字）
            float padX = mobile ? 22f : 20f;
            float inset = ComicStyle.BorderThin + padX;
            float contentLeft = -frameW * 0.5f + inset;
            float contentRight = frameW * 0.5f - (mobile ? 26f : 22f);
            float rowY = mobile ? 58f : 54f;
            float levelY = mobile ? -4f : -2f;
            float trackY = -frameH * 0.5f + (mobile ? 40f : 36f);
            float trackW = contentRight - contentLeft;

            var frameGo = new GameObject("ReportPanel", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            frameGo.transform.SetParent(parent, false);
            _reportPanelRoot = (RectTransform)frameGo.transform;
            _reportPanelRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _reportPanelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _reportPanelRoot.pivot = new Vector2(0.5f, 0.5f);
            _reportPanelRoot.anchoredPosition = new Vector2(0f, frameY);
            _reportPanelRoot.sizeDelta = new Vector2(frameW, frameH);

            var frameShape = frameGo.GetComponent<UIInkShapeGraphic>();
            frameShape.Shape = UIInkShape.SharpRect;
            frameShape.CornerRadius = ComicStyle.CornerSharp;
            frameShape.BorderWidth = ComicStyle.BorderThin;
            frameShape.BorderColor = ComicStyle.Ink;
            frameShape.color = ComicStyle.Paper;
            frameShape.HardShadow = false;
            frameShape.Halftone = false;
            frameShape.raycastTarget = false;
            // 信息卡：纸白 + 3px 墨框 + 面板硬影，身份色只占顶部一条
            resolved.ApplyHardShadow(frameShape, UIHardShadowLevel.Panel);

            _reportMotion = frameGo.GetComponent<UISpringMotion>();
            _reportMotion.SetTheme(resolved);

            // 顶部身份色带（胜利金 / 失败灰蓝，揭晓时才定色）
            var bandGo = new GameObject("ReportBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(frameGo.transform, false);
            _reportBand = (RectTransform)bandGo.transform;
            _reportBand.anchorMin = new Vector2(0f, 1f);
            _reportBand.anchorMax = new Vector2(1f, 1f);
            _reportBand.pivot = new Vector2(0.5f, 1f);
            _reportBand.sizeDelta = new Vector2(-ComicStyle.BorderThin * 2f, bandH);
            _reportBand.anchoredPosition = new Vector2(0f, -ComicStyle.BorderThin);

            _reportBandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            _reportBandShape.Shape = UIInkShape.SharpRect;
            _reportBandShape.CornerRadius = ComicStyle.CornerSharp;
            _reportBandShape.BorderWidth = 0f;
            _reportBandShape.color = ComicStyle.Yellow;
            _reportBandShape.HardShadow = false;
            _reportBandShape.Halftone = false;
            _reportBandShape.raycastTarget = false;

            // 1) 勋章行：chips 由 PopulateMedals 在掷点结束后填入；右侧固定本局经验。
            var medalsGo = new GameObject("MedalsRow", typeof(RectTransform));
            medalsGo.transform.SetParent(frameGo.transform, false);
            var medalsRt = (RectTransform)medalsGo.transform;
            medalsRt.anchorMin = new Vector2(0.5f, 0.5f);
            medalsRt.anchorMax = new Vector2(0.5f, 0.5f);
            medalsRt.pivot = new Vector2(0f, 0.5f);
            medalsRt.anchoredPosition = new Vector2(contentLeft, rowY);
            medalsRt.sizeDelta = new Vector2(trackW - (mobile ? 170f : 150f), mobile ? 36f : 32f);
            _medalsContainer = medalsRt;

            var gainGo = new GameObject("XpGainText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gainGo.transform.SetParent(frameGo.transform, false);
            var gainRt = (RectTransform)gainGo.transform;
            // 必须用中心锚点 + 中心系坐标：右边缘锚点会把 +XP 再往右推一个 contentRight。
            gainRt.anchorMin = new Vector2(0.5f, 0.5f);
            gainRt.anchorMax = new Vector2(0.5f, 0.5f);
            gainRt.pivot = new Vector2(1f, 0.5f);
            gainRt.anchoredPosition = new Vector2(contentRight, rowY);
            gainRt.sizeDelta = new Vector2(mobile ? 190f : 170f, 30f);

            _xpGainText = gainGo.GetComponent<TextMeshProUGUI>();
            _xpGainText.text = "+0 XP";
            _xpGainText.font = resolved.ResolveFont(true, "+1200 XP");
            _xpGainText.fontSize = UITypography.FontSnapped(22f);
            _xpGainText.fontStyle = FontStyles.Bold;
            _xpGainText.alignment = TextAlignmentOptions.MidlineRight;
            _xpGainText.color = ComicStyle.Ink;
            _xpGainText.raycastTarget = false;
            _xpGainText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpGainText.overflowMode = TextOverflowModes.Overflow;

            // 2) 等级主角：LV. 3 大字 + 段位名一行小字。
            var levelGo = new GameObject("LevelText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            levelGo.transform.SetParent(frameGo.transform, false);
            var levelRt = (RectTransform)levelGo.transform;
            levelRt.anchorMin = new Vector2(0.5f, 0.5f);
            levelRt.anchorMax = new Vector2(0.5f, 0.5f);
            levelRt.pivot = new Vector2(0f, 0.5f);
            levelRt.anchoredPosition = new Vector2(contentLeft, levelY);
            levelRt.sizeDelta = new Vector2(mobile ? 200f : 180f, mobile ? 62f : 58f);

            _xpLevelText = levelGo.GetComponent<TextMeshProUGUI>();
            _xpLevelText.text = "LV. 1";
            _xpLevelText.font = resolved.ResolveFont(true, "LV. 1");
            _xpLevelText.fontSize = UITypography.FontSnapped(52f);
            _xpLevelText.fontStyle = FontStyles.Bold;
            _xpLevelText.alignment = TextAlignmentOptions.MidlineLeft;
            _xpLevelText.color = ComicStyle.Ink;
            _xpLevelText.raycastTarget = false;
            _xpLevelText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpLevelText.overflowMode = TextOverflowModes.Overflow;

            var rankGo = new GameObject("RankText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            rankGo.transform.SetParent(frameGo.transform, false);
            var rankRt = (RectTransform)rankGo.transform;
            rankRt.anchorMin = new Vector2(0.5f, 0.5f);
            rankRt.anchorMax = new Vector2(0.5f, 0.5f);
            rankRt.pivot = new Vector2(0f, 0.5f);
            rankRt.anchoredPosition = new Vector2(contentLeft + (mobile ? 200f : 190f), levelY - 2f);
            rankRt.sizeDelta = new Vector2(mobile ? 360f : 330f, 26f);

            _xpRankNameText = rankGo.GetComponent<TextMeshProUGUI>();
            _xpRankNameText.text = "FRESH RECRUIT";
            _xpRankNameText.font = resolved.ResolveFont(true, "FRESH RECRUIT");
            _xpRankNameText.fontSize = UITypography.FontSnapped(16f);
            _xpRankNameText.fontStyle = FontStyles.Bold;
            _xpRankNameText.characterSpacing = 1.5f;
            _xpRankNameText.alignment = TextAlignmentOptions.MidlineLeft;
            _xpRankNameText.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.75f);
            _xpRankNameText.raycastTarget = false;
            _xpRankNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpRankNameText.overflowMode = TextOverflowModes.Overflow;

            // 3) 经验条：纯色直角，无数字标签，长度本身就是进度。
            var trackGo = new GameObject("XpTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            trackGo.transform.SetParent(frameGo.transform, false);
            var trackRt = (RectTransform)trackGo.transform;
            trackRt.anchorMin = new Vector2(0.5f, 0.5f);
            trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.pivot = new Vector2(0f, 0.5f);
            trackRt.anchoredPosition = new Vector2(contentLeft, trackY);
            trackRt.sizeDelta = new Vector2(trackW, mobile ? 14f : 12f);

            var trackShape = trackGo.GetComponent<UIInkShapeGraphic>();
            trackShape.Shape = UIInkShape.SharpRect;
            trackShape.CornerRadius = ComicStyle.CornerSharp;
            trackShape.BorderWidth = 1f;
            trackShape.BorderColor = ComicStyle.Ink;
            trackShape.color = ComicStyle.PaperAlt;
            trackShape.HardShadow = false;
            trackShape.raycastTarget = false;

            var fillGo = new GameObject("XpFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(1f, 1f);
            fillRt.offsetMax = new Vector2(-1f, -1f);

            _xpBarFill = fillGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(_xpBarFill, filled: true);
            _xpBarFill.color = ComicStyle.Yellow;
            _xpBarFill.fillAmount = 0f;
            _xpBarFill.raycastTarget = false;

            // 升级页签：骑在卡片上边框上，不再盖住勋章与等级文字。
            var luGo = new GameObject("LevelUpBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            luGo.transform.SetParent(frameGo.transform, false);
            _levelUpBadgeRoot = (RectTransform)luGo.transform;
            _levelUpBadgeRoot.anchorMin = new Vector2(0f, 1f);
            _levelUpBadgeRoot.anchorMax = new Vector2(0f, 1f);
            _levelUpBadgeRoot.pivot = new Vector2(0f, 1f);
            _levelUpBadgeRoot.anchoredPosition = new Vector2(inset, 16f);
            _levelUpBadgeRoot.sizeDelta = new Vector2(mobile ? 240f : 216f, mobile ? 34f : 30f);

            var luShape = luGo.GetComponent<UIInkShapeGraphic>();
            luShape.Shape = UIInkShape.SharpRect;
            luShape.CornerRadius = ComicStyle.CornerSharp;
            luShape.BorderWidth = ComicStyle.BorderThin;
            luShape.BorderColor = ComicStyle.Ink;
            luShape.color = ComicStyle.Yellow;
            luShape.HardShadow = false;
            luShape.raycastTarget = false;

            _levelUpMotion = luGo.GetComponent<UISpringMotion>();
            _levelUpMotion.SetTheme(resolved);

            var luTxtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            luTxtGo.transform.SetParent(_levelUpBadgeRoot, false);
            Stretch(luTxtGo.GetComponent<RectTransform>());

            _levelUpText = luTxtGo.GetComponent<TextMeshProUGUI>();
            _levelUpText.text = "LEVEL UP · LV. 1";
            _levelUpText.font = resolved.ResolveFont(true, "LEVEL UP");
            _levelUpText.fontSize = UITypography.FontSnapped(16f);
            _levelUpText.fontStyle = FontStyles.Bold;
            _levelUpText.alignment = TextAlignmentOptions.Center;
            _levelUpText.color = ComicStyle.Ink;
            _levelUpText.raycastTarget = false;
            _levelUpText.textWrappingMode = TextWrappingModes.NoWrap;
            _levelUpText.overflowMode = TextOverflowModes.Overflow;

            _levelUpBadgeRoot.gameObject.SetActive(false);
            _reportPanelRoot.gameObject.SetActive(false);
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

            // 版本号只作为「数据源」保留（t-1 首页右下角签用 TMP 渲染，见 BuildHomeCornerTags）。
            // 这里不再申请 LegacyRuntime 字体：LiberationSans 没有中文字形，旧版版本号在真机上就是 □□□。
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
                versionText.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(18f));
                versionText.alignment = TextAnchor.MiddleRight;
                versionText.color = ComicStyle.Ink;
                versionText.raycastTarget = false;
            }

            if (versionText != null)
            {
                versionText.text = $"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}";
            }

            // 检查更新按钮：透明 Image 只负责射线与旧监听（视觉由首页 UPDATE 卡承担）
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
                img.color = new Color(1f, 1f, 1f, 0f);

                checkUpdateButton = btnGo.GetComponent<Button>();
                checkUpdateButton.targetGraphic = img;
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
                limg.color = new Color(1f, 1f, 1f, 0f);

                var lbtn = lbtnGo.GetComponent<Button>();
                lbtn.targetGraphic = limg;
                lbtn.onClick.AddListener(() =>
                {
                    var touchUI = FindAnyObjectByType<MobileTouchUI>();
                    if (touchUI != null) touchUI.OpenLayoutCustomizer();
                });

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

        /// <summary>首页信息卡（战绩 / 键位说明）打开详情浮层：浮层自带遮罩与同帧输入仲裁。</summary>
        private void ShowHomeInfo(HomeInfoTab tab)
        {
            if (readyPanel == null) return;
            HomeInfoOverlay.Show(readyPanel.transform as RectTransform, UIThemeSO.Resolve(uiTheme), tab);
        }

        private void OnStartClicked()
        {
            MatchManager.Instance?.StartMatch();
        }

        private void Update()
        {
            // 首页几何守护：Android 启动/旋转瞬间安全区与画布可能尚未 settle，
            // EnsureReadyVisuals 只在 Awake 构建一次，首帧脏几何会把 2×2 卡阵永久推出屏幕
            // （真机 20:9 实测：右列整体出屏、下排与 CTA 被裁）。几何稳定一帧后即用真实值重建。
            if (_homeGeometryKeyValid && readyPanel != null && readyPanel.activeInHierarchy)
            {
                Vector4 key = CurrentHomeGeometryKey();
                bool changed = MaxDimensionDelta(key, _homeGeometryKey) > 0.5f;
                bool settled = MaxDimensionDelta(key, _homeGeometryKeyPrev) <= 0.5f;
                _homeGeometryKeyPrev = key;
                if (changed && settled)
                {
                    RebuildHomeVisuals();
                }
            }

            var match = MatchManager.Instance;
            if (match == null) return;

            // 快捷键开赛与重开：首页信息浮层打开时让路，否则 Space/Enter 会隔着遮罩直接把比赛开了
            if (!Application.isMobilePlatform && !MobileTouchUI.IsTouchSimulationActive &&
                !HomeInfoOverlay.IsOpen &&
                (match.CurrentPhase == MatchManager.Phase.Ready || match.CurrentPhase == MatchManager.Phase.Result))
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame))
                {
                    match.StartMatch();
                }
            }

            // 阶段切换触发（首帧同步不算"回到首页"，否则会和 Awake 的入场动画重播一次）
            if (match.CurrentPhase != _lastPhase)
            {
                _lastPhase = match.CurrentPhase;
                bool firstSync = !_firstPhaseSynced;
                _firstPhaseSynced = true;
                HandlePhaseChanged(match, firstSync);
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

            // 入场动画必须等面板真正激活后再播（面板未激活时 UISpringMotion 的 Update 不跑，
            // 会停在"透明 + 缩到 0.9"的起始帧）。
            if (_homeEntrancePending && readyPanel != null && readyPanel.activeSelf)
            {
                _homeEntrancePending = false;
                PlayHomeEntrance();
            }

            if (_resultEntrancePending && resultPanel != null && resultPanel.activeSelf)
            {
                _resultEntrancePending = false;
                PlayResultEntrance();
            }
        }

        private void HandlePhaseChanged(MatchManager match, bool firstSync = false)
        {
            var phase = match.CurrentPhase;

            // 任何离开首页的阶段切换都必须收掉详情浮层：readyPanel 会被关掉，
            // 静态 IsOpen 若不复位会一直挡住开赛快捷键。
            if (phase != MatchManager.Phase.Ready) HomeInfoOverlay.CloseIfOpen();

            if (phase == MatchManager.Phase.Ready)
            {
                // 从对局/倒计时中返回主菜单：撤掉可能还挂着的倒计时横幅
                if (bannerView != null) bannerView.HideImmediate();
                s_SettledThisMatch = false;
                _profileSettled = false;
                _displayedMedalCount = 0;
                _lastLevelUpStep = 0;
                HideResultRevealVisuals();
                RefreshHomeInfo();
                if (!firstSync) _homeEntrancePending = true;
            }
            else if (phase == MatchManager.Phase.Countdown)
            {
                _lastCountdownNumber = -1;
                _oneMinuteTriggered = false;
                _badgePlayed = false;
                _numbersSettled = false;
                s_SettledThisMatch = false;
                _profileSettled = false;
                _displayedMedalCount = 0;
                _lastLevelUpStep = 0;
                if (victoryBadgeRoot != null) victoryBadgeRoot.gameObject.SetActive(false);
                HideResultRevealVisuals();
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
                _displayedMedalCount = 0;
                _lastLevelUpStep = 0;
                HideResultRevealVisuals();
                if (bannerView != null) bannerView.HideImmediate();
                PlayResultSound(match);
                _resultEntrancePending = true;

                if (!s_SettledThisMatch)
                {
                    s_SettledThisMatch = true;
                    SettlePlayerProfileAndAwards(match);
                }
            }
        }

        /// <summary>
        /// 收起结算页「掷点后才出现」的那一层（战报单据 / 勋章行 / 成长条 / 胜方牌），
        /// 让下一局的结算从干净的落后状态重新播放。
        /// </summary>
        private void HideResultRevealVisuals()
        {
            if (_reportPanelRoot != null) _reportPanelRoot.gameObject.SetActive(false);
            if (_levelUpBadgeRoot != null) _levelUpBadgeRoot.gameObject.SetActive(false);
            if (_winnerTagA != null) _winnerTagA.gameObject.SetActive(false);
            if (_winnerTagB != null) _winnerTagB.gameObject.SetActive(false);
            _winnerEmphasisApplied = false;

            // 败方数字的降灰要在下一局恢复，否则第二次结算会沿用上一局的胜负配色。
            if (resultPercentA != null) resultPercentA.color = ComicStyle.Ink;
            if (resultPercentB != null) resultPercentB.color = ComicStyle.Ink;
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
            if (_numbersSettled)
            {
                UpdatePostRollVisuals(match);
                return;
            }

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
                // 胜负结论已由顶部大牌 + 两个巨大百分比承担，这里不再写第二句解说。
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            if (rollProgress >= 1f)
            {
                if (resultPercentA != null) resultPercentA.text = $"{match.FinalCoverageA:F1}%";
                if (resultPercentB != null) resultPercentB.text = $"{match.FinalCoverageB:F1}%";
                if (resultBarA != null) resultBarA.fillAmount = Mathf.Clamp01(match.FinalCoverageA / 100f);
                if (resultBarB != null) resultBarB.fillAmount = Mathf.Clamp01(match.FinalCoverageB / 100f);
                _numbersSettled = true;

                // 掷点定格后点亮胜方：顶部判定大牌 + 胜方「胜」页签，败方数字降灰。
                if (!_badgePlayed)
                {
                    _badgePlayed = true;
                    TriggerVictoryBadge(match.PlayerWon);
                }
                ApplyWinnerEmphasis(match.PlayerWon);

                if (!s_SettledThisMatch)
                {
                    s_SettledThisMatch = true;
                    SettlePlayerProfileAndAwards(match);
                }
                UpdatePostRollVisuals(match);
            }
        }

        private void SettlePlayerProfileAndAwards(MatchManager match)
        {
            try
            {
                CombatantHealth localHealth = null;
                if (PlayerController.LocalPlayer != null)
                {
                    localHealth = PlayerController.LocalPlayer.Health;
                }

                CombatantMatchStats localStats = default;
                bool hasStats = false;
                if (localHealth != null)
                {
                    try
                    {
                        hasStats = MatchStatsService.TryGet(localHealth, out localStats);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[MatchFlowUI] MatchStatsService 读取失败: " + ex.Message);
                    }
                }

                if (!hasStats && InkSurfaceController.Instance != null && localHealth != null)
                {
                    float coverage = InkSurfaceController.Instance.GetCoveragePercent(localHealth.Team);
                    localStats = new CombatantMatchStats
                    {
                        TurfSquareMeters = coverage * 10f,
                        Kills = 0,
                        Deaths = 0
                    };
                }

                bool won = (match != null) && match.PlayerWon;
                _profileResult = PlayerProfile.ApplyMatchResult(won, localStats);
                _profileSettled = true;

                // 评定全场参战者勋章
                var all = CombatantHealthRegistry.All;
                AwardType localAwards = AwardType.None;
                if (all != null && all.Count > 0 && localHealth != null)
                {
                    var results = new AwardType[all.Count];
                    MatchAwards.Evaluate(all, c =>
                    {
                        if (c != null && MatchStatsService.TryGet(c, out var st)) return st;
                        return default;
                    }, results);

                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] == localHealth)
                        {
                            localAwards = results[i];
                            break;
                        }
                    }
                }
                else
                {
                    var single = new[] { new ContestantAwardInput(localHealth != null ? localHealth.Team : Team.TeamA, localStats) };
                    var singleAward = MatchAwards.Evaluate(single);
                    if (singleAward != null && singleAward.Length > 0) localAwards = singleAward[0];
                }

                _localAwards = localAwards;
                PopulateMedals(_localAwards, UIThemeSO.Resolve(uiTheme), Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 结算个人勋章与档案异常: " + ex);
            }
        }

        private void PopulateMedals(AwardType awards, UIThemeSO resolved, bool mobile)
        {
            if (_medalsContainer == null) return;

            for (int i = 0; i < _medalBadgeObjects.Count; i++)
            {
                if (_medalBadgeObjects[i] != null) Destroy(_medalBadgeObjects[i]);
            }
            _medalBadgeObjects.Clear();
            _extractedAwards.Clear();

            var buffer = new AwardType[7];
            int count = MatchAwards.ExtractAwards(awards, buffer);
            for (int i = 0; i < count; i++)
            {
                _extractedAwards.Add(buffer[i]);
            }

            float badgeW = mobile ? 142f : 128f;
            float badgeH = mobile ? 38f : 34f;
            float spacing = mobile ? 10f : 8f;

            if (_extractedAwards.Count == 0)
            {
                var emptyGo = new GameObject("NoMedalsBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                emptyGo.transform.SetParent(_medalsContainer, false);
                var eRt = (RectTransform)emptyGo.transform;
                eRt.anchorMin = new Vector2(0f, 0.5f);
                eRt.anchorMax = new Vector2(0f, 0.5f);
                eRt.pivot = new Vector2(0f, 0.5f);
                eRt.anchoredPosition = new Vector2(0f, 0f);
                eRt.sizeDelta = new Vector2(260f, badgeH);

                var eTmp = emptyGo.GetComponent<TextMeshProUGUI>();
                eTmp.text = "本局无勋章";
                eTmp.font = resolved.ResolveFont(true, "本局无勋章");
                eTmp.fontSize = UITypography.FontSnapped(18f);
                eTmp.fontStyle = FontStyles.Bold;
                eTmp.alignment = TextAlignmentOptions.MidlineLeft;
                eTmp.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.45f);
                eTmp.textWrappingMode = TextWrappingModes.NoWrap;
                eTmp.overflowMode = TextOverflowModes.Overflow;
                _medalBadgeObjects.Add(emptyGo);
                return;
            }

            // 最多 4 枚：多出来的收成「+N」一枚，避免 chips 挤出白卡边界。
            const int MaxChips = 4;
            int awardCount = _extractedAwards.Count;
            bool overflow = awardCount > MaxChips;
            int shownAwards = overflow ? MaxChips - 1 : awardCount;
            int chipCount = overflow ? MaxChips : awardCount;
            float available = _medalsContainer.rect.width;
            if (available < 1f) available = mobile ? 660f : 620f;
            badgeW = Mathf.Clamp((available - spacing * (chipCount - 1)) / chipCount, 96f, badgeW);

            for (int i = 0; i < chipCount; i++)
            {
                bool plusChip = overflow && i == chipCount - 1;
                AwardType aw = plusChip ? AwardType.None : _extractedAwards[i];
                string title = plusChip
                    ? "+" + (awardCount - shownAwards)
                    : MatchAwards.GetTitle(aw);
                Color chipColor = plusChip ? ComicStyle.PaperAlt : MatchAwards.GetBadgeColor(aw);

                var badgeGo = new GameObject(plusChip ? "Medal_Plus" : $"Medal_{aw}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(UISpringMotion));
                badgeGo.transform.SetParent(_medalsContainer, false);
                var bRt = (RectTransform)badgeGo.transform;
                bRt.anchorMin = new Vector2(0f, 0.5f);
                bRt.anchorMax = new Vector2(0f, 0.5f);
                bRt.pivot = new Vector2(0f, 0.5f);
                bRt.anchoredPosition = new Vector2(i * (badgeW + spacing), 0f);
                bRt.sizeDelta = new Vector2(badgeW, badgeH);

                var shape = badgeGo.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.SharpRect;
                shape.CornerRadius = ComicStyle.CornerSharp;
                shape.BorderWidth = ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.Ink;
                shape.color = chipColor;
                shape.HardShadow = false;
                shape.Halftone = false;
                shape.raycastTarget = false;

                var motion = badgeGo.GetComponent<UISpringMotion>();
                motion.SetTheme(resolved);

                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(badgeGo.transform, false);
                Stretch(txtGo.GetComponent<RectTransform>());

                var tmp = txtGo.GetComponent<TextMeshProUGUI>();
                tmp.text = title;
                tmp.font = resolved.ResolveFont(true, title);
                tmp.fontSize = UITypography.FontSnapped(mobile ? 16f : 15f);
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 0.5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = ComicStyle.Ink;
                tmp.raycastTarget = false;
                // 长勋章名（UNTOUCHABLE / TOP SPLATTER）在窄徽章里不允许换行，
                // 用自动缩放把字号压到可见范围内，避免拆成两行溢出边框。
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 9f;
                tmp.fontSizeMax = tmp.fontSize;
                tmp.margin = new Vector4(4f, 1f, 4f, 1f);

                badgeGo.SetActive(false);
                _medalBadgeObjects.Add(badgeGo);
            }
        }

        private void UpdatePostRollVisuals(MatchManager match)
        {
            if (_reportPanelRoot != null && !_reportPanelRoot.gameObject.activeSelf)
            {
                _reportPanelRoot.gameObject.SetActive(true);

                // 顶部身份色带：胜利金 / 失败灰蓝（状态色只占一条，信息层保持纸白可读）
                if (_reportBandShape != null)
                {
                    _reportBandShape.color = (match != null && match.PlayerWon) ? ComicStyle.Yellow : DefeatSlate;
                }

                // 卡片本身也做一记 pop-in（错峰由更新循环里的 0.05s 承接）
                if (_reportMotion != null)
                {
                    _reportMotion.Build();
                    _reportMotion.SnapHome();
                    _reportMotion.PlayIn(0.05f);
                }

                // 卡片刚露出的第一帧先回到「本局结算前」的读数，
                // 否则第二局会把上一局的等级 / +XP 沿用到 0.25s 后才被动画覆盖。
                if (_profileSettled)
                {
                    SetLevelDisplay(_profileResult.OldLevel);
                    if (_xpGainText != null) _xpGainText.text = "+0 XP";
                    if (_xpBarFill != null)
                    {
                        int req = _profileResult.XpRequiredForNextLevel;
                        _xpBarFill.fillAmount = req > 0 ? Mathf.Clamp01((float)_profileResult.OldXp / req) : 0f;
                    }
                }
            }

            float rollDuration = match != null ? Mathf.Max(0.01f, match.ResultRollSeconds) : 1f;
            float postRoll = _resultElapsed - rollDuration;

            // 1. 勋章逐个弹入
            for (int i = 0; i < _medalBadgeObjects.Count; i++)
            {
                if (i >= _displayedMedalCount)
                {
                    float popTime = 0.1f + i * 0.14f;
                    if (postRoll >= popTime && _medalBadgeObjects[i] != null)
                    {
                        _medalBadgeObjects[i].SetActive(true);
                        var motion = _medalBadgeObjects[i].GetComponent<UISpringMotion>();
                        if (motion != null)
                        {
                            motion.SnapHome();
                            motion.PlayIn(0.05f);
                        }
                        _displayedMedalCount = i + 1;
                    }
                }
            }

            // 2. XP 成长与跨级进度条推进
            float xpT = postRoll - 0.25f;
            if (xpT >= 0f && _profileSettled)
            {
                int levelsGained = _profileResult.LevelsGained;
                float animDuration = 0.8f + levelsGained * 0.6f;
                float norm = Mathf.Clamp01(xpT / animDuration);

                int curGain = Mathf.RoundToInt(_profileResult.XpGained * norm);
                if (_xpGainText != null) _xpGainText.text = $"+{curGain} XP";

                if (levelsGained == 0)
                {
                    int req = _profileResult.XpRequiredForNextLevel;
                    float oldR = req > 0 ? (float)_profileResult.OldXp / req : 0f;
                    float newR = req > 0 ? (float)_profileResult.NewXp / req : 0f;
                    float curR = Mathf.Lerp(oldR, newR, norm);
                    if (_xpBarFill != null) _xpBarFill.fillAmount = curR;
                    SetLevelDisplay(_profileResult.OldLevel);
                }
                else
                {
                    int totalSegments = levelsGained + 1;
                    float segmentSpan = 1f / totalSegments;
                    int currentSegment = Mathf.Min(levelsGained, Mathf.FloorToInt(norm / segmentSpan));
                    float segmentNorm = (norm - currentSegment * segmentSpan) / segmentSpan;
                    if (norm >= 1f)
                    {
                        currentSegment = levelsGained;
                        segmentNorm = 1f;
                    }

                    int curLvl = _profileResult.OldLevel + currentSegment;
                    int curReq = PlayerProfile.GetXpRequiredForLevel(curLvl);

                    float fromR = (currentSegment == 0) ? ((float)_profileResult.OldXp / PlayerProfile.GetXpRequiredForLevel(_profileResult.OldLevel)) : 0f;
                    float toR = (currentSegment == levelsGained) ? ((float)_profileResult.NewXp / curReq) : 1f;
                    float curR = Mathf.Lerp(fromR, toR, segmentNorm);

                    if (_xpBarFill != null) _xpBarFill.fillAmount = curR;
                    SetLevelDisplay(curLvl);

                    if (currentSegment > 0 && currentSegment != _lastLevelUpStep)
                    {
                        _lastLevelUpStep = currentSegment;
                        TriggerLevelUpBadge(curLvl);
                    }
                }
            }
        }

        /// <summary>
        /// 战报卡等级读数：LV. 3 大字 + 段位名小字。
        /// 数字只在这里写，避免出现「LV. 3 · 1232 / 1850 XP」这种一行塞三件事的读数。
        /// </summary>
        private void SetLevelDisplay(int level)
        {
            if (_xpLevelText != null) _xpLevelText.text = $"LV. {level}";
            if (_xpRankNameText != null)
            {
                _xpRankNameText.text = PlayerProfile.GetRankName(level).ToUpper();
            }
        }

        private void TriggerLevelUpBadge(int newLvl)
        {
            if (_levelUpBadgeRoot == null) return;
            _levelUpBadgeRoot.gameObject.SetActive(true);
            if (_levelUpText != null)
            {
                _levelUpText.text = $"LEVEL UP · LV. {newLvl}";
            }
            if (_levelUpMotion != null)
            {
                _levelUpMotion.SnapHome();
                _levelUpMotion.PlayIn(0.05f);
            }
        }

        private void TriggerVictoryBadge(bool playerWon)
        {
            if (victoryBadgeRoot == null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            if (victoryBadgeBg != null)
            {
                // 墨块底 + 语义色描边（v-1：胜利金 / 失败灰蓝），硬影固定 Hero 档。
                victoryBadgeBg.color = ComicStyle.Ink;
                victoryBadgeBg.BorderColor = playerWon ? ComicStyle.Yellow : DefeatSlate;
                victoryBadgeBg.BorderWidth = ComicStyle.BorderHeavy;
                resolved.ApplyHardShadow(victoryBadgeBg, UIHardShadowLevel.Hero);
            }

            if (victoryBadgeText != null)
            {
                string text = playerWon ? "VICTORY" : "DEFEAT";
                victoryBadgeText.text = text;
                Color fill = playerWon ? ComicStyle.Yellow : ComicStyle.Paper;
                victoryBadgeText.color = fill;
                var font = resolved.ResolveFont(true, text);
                if (font != null) victoryBadgeText.font = font;

                // 双层描边字的填充色单独走 UIChunkyText：直接改 TMP 颜色只会把"描边层"染色
                if (_victoryBadgeChunky != null) _victoryBadgeChunky.SetFillColor(fill);
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
