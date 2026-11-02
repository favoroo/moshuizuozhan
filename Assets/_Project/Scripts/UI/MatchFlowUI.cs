using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
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
    /// 2. 流程横幅系统（MatchBannerView）：开赛墨幕撕开后 GO、1:00 决战与 FINISH 终局定格（不再有 3-2-1 数字倒数）；
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

        /// <summary>拔河条深槽 #0B0B0B：比舞台再深一级，队色填充才立得起来。</summary>
        public static readonly Color BarSlotInk = ComicStyle.FromHex(0x0B0B0B);

        /// <summary>读数板底色 #1E1A16：深墨中立色，靠 3px 队色描边识别阵营。</summary>
        public static readonly Color PlateInk = ComicStyle.FromHex(0x1E1A16);

        /// <summary>败方读数用的不透明灰 #6F6F6F（禁止半透明降灰，否则与硬影读成重影）。</summary>
        public static readonly Color DimReadout = ComicStyle.FromHex(0x6F6F6F);

        /// <summary>深墨读数板上的败方灰 #9C9C9C：比纸面灰更亮，深底上仍然可读。</summary>
        public static readonly Color PlateDimReadout = ComicStyle.FromHex(0x9C9C9C);

        private RectTransform _contentRoot;
        private RectTransform _menuSafeArea;
        private RectTransform _homeHeroCard;
        private HomeRuntimeBoard _homeBoard;
        // 首页几何指纹（安全区宽高 + 画布宽高）：几何变化时重建首页，见 Update / RebuildHomeVisuals。
        private Vector4 _homeGeometryKey;
        private Vector4 _homeGeometryKeyPrev;
        private bool _homeGeometryKeyValid;
        private TextMeshProUGUI _homeSettingsInfo;
        private TextMeshProUGUI _homeRecordInfo;
        private TextMeshProUGUI _homeKeysInfo;

        // 首页 Hero 卡头部的 Bot 难度四档段选（新手/轻松/普通/硬核），选择结果持久化到 GameSettingsService。
        private readonly UIInkShapeGraphic[] _difficultyShapes =
            new UIInkShapeGraphic[MenuLayoutMath.DifficultyTierCount];
        private readonly TextMeshProUGUI[] _difficultyTexts =
            new TextMeshProUGUI[MenuLayoutMath.DifficultyTierCount];

        private readonly List<UISpringMotion> _homeEntrance = new List<UISpringMotion>();
        private bool _homeEntrancePending;
        private bool _resultEntrancePending;
        private bool _firstPhaseSynced;

        private RectTransform _reportBand;
        private UIInkShapeGraphic _reportBandShape;
        private UISpringMotion _reportMotion;
        private readonly List<UISpringMotion> _resultEntrance = new List<UISpringMotion>();

        private GameObject _fullscreenUnderlay;
        private float _resultElapsed;
        private MatchManager.Phase _lastPhase = (MatchManager.Phase)(-1);
        private bool _oneMinuteTriggered;
        private bool _badgePlayed;
        private bool _numbersSettled;

        [Header("结算勋章与成长系统（INKWAVE 对齐）")]
        private RectTransform _medalsContainer;
        private readonly List<GameObject> _medalBadgeObjects = new List<GameObject>();
        private readonly List<AwardType> _extractedAwards = new List<AwardType>();
        private int _displayedMedalCount;

        private RectTransform _reportPanelRoot;
        private bool _winnerEmphasisApplied;

        // v2 结算「计分场」：两块墨色读数板 + 中间拔河覆盖率条 + 顶部胜差贴纸。
        private RectTransform _coverPlateA;
        private RectTransform _coverPlateB;
        private UIInkShapeGraphic _plateShapeA;
        private UIInkShapeGraphic _plateShapeB;
        private TextMeshProUGUI _percentTmpA;
        private TextMeshProUGUI _percentTmpB;
        private RectTransform _winMarkA;
        private RectTransform _winMarkB;
        private Image _coverFillA;
        private Image _coverFillB;
        private RectTransform _coverDivider;
        private float _coverInnerW;
        private float _coverInnerH;
        private RectTransform _matchTagRoot;
        private TextMeshProUGUI _matchTagText;
        private Color _teamColorA = Color.white;
        private Color _teamColorB = Color.white;

        // 战报台账卡（双队明细 + 勋章 / LV / XP 条）布局缓存：由 BuildLedgerCard 写入，RebuildRosterRows 读取。
        private float _ledgerColLeft;
        private float _ledgerColRight;
        private float _ledgerHeaderY;
        private float _ledgerRowTopY;
        private float _ledgerRowH;
        private float _ledgerNameW;
        private float _ledgerTurfX;
        private float _ledgerKoX;
        private float _ledgerDownX;
        private readonly List<GameObject> _rosterRowObjects = new List<GameObject>();
        private bool _rosterBuilt;

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

            // 设置页改音量/震屏时同步首页设置卡读数（事件由 GameSettingsService 静态广播）
            GameSettingsService.SettingsChanged += RefreshHomeInfo;
            GameSettingsService.BotDifficultyChanged += HandleBotDifficultyChanged;
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
            P1.UI.WeaponSelect.WeaponSelectView.Instance?.SyncHomeLayout();

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

            // 场景遗留按钮（开赛 / 检查更新 / 自定义按键）是 readyPanel 的持久节点，
            // 重建时必须先搬回 readyPanel 再销毁旧安全区，否则会跟着 MenuSafeArea 一起被 Destroy：
            // StartButton 的 onClick 只在 Awake 绑一次，一旦销毁 startButton 变 null，
            // PlaceHomeCta 直接 return —— 真机表现就是「首页开始游戏按钮凭空消失、整局开不了」。
            // （检查更新按钮还被铺进了 HomeTile_UPDATE 的 Content，不先搬出会随卡片销毁。）
            ReparentInto(startButton != null ? startButton.transform : null, readyPanel.transform);
            ReparentInto(checkUpdateButton != null ? checkUpdateButton.transform : null, readyPanel.transform);
            ReparentInto(FindMenuNode("CustomLayoutButton"), readyPanel.transform);

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
            // 表现为「首页内容不在安全区」）。场景遗留按钮已在上面搬回 readyPanel，
            // 由 EnsureReadyVisuals 重新收编进新容器。
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
            float heroWidth = _homeBoard.HeroWidth;

            var cardGo = new GameObject("HomeHeroCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            cardGo.transform.SetParent(_menuSafeArea, false);
            var card = (RectTransform)cardGo.transform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = new Vector2(_homeBoard.HeroX, -_homeBoard.Top);
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

            // 1. 顶部 Header：左上「PLAY」硬角签 + 右侧 Bot 难度四档段选 + 模式微标
            CreateStageTag(card, "HeroTag", "PLAY", m.TagSize, m.TagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, m.HeroPadding, TextAlignmentOptions.Center);

            float difficultyWidth = BuildHeroDifficulty(card, theme, m, innerWidth);

            float badgeFont = UITypography.FontSnapped(mobile ? 22f : 20f);
            float badgeX = m.HeroPadding.x + m.TagSize.x + 16f;
            float badgeWidth = innerWidth - m.TagSize.x - 16f
                               - (difficultyWidth > 0.01f ? difficultyWidth + 16f : 0f);
            if (badgeWidth >= 96f)
            {
                // 移动端只保留「4V4 · TURF」，给难度段选让出头部宽度；桌面保留完整英文微标。
                CreateBoardText(card, "HeroModeBadge", mobile ? "4V4 · TURF" : "4V4 · TURF WAR", badgeFont, true,
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                    new Vector2(badgeX, m.HeroPadding.y + (m.TagSize.y - badgeFont * 1.4f) * 0.5f),
                    new Vector2(badgeWidth, badgeFont * 1.4f),
                    TextAlignmentOptions.MidlineLeft, 2f, FontStyles.Bold);
            }

            // 1.5 标题群：游戏名 Hero 大标题 + 拉丁副标。
            // 版面表一直保留 TitleFont/LatinFont 两档但构建时未落地，
            // 导致 Hero 卡中部整片空场、首页失去品牌锚点。标题群落在武器整备卡与 CTA 之间的
            // 空档，全部左对齐；底部必须让开桌面端 CTA 上方的按键提示区。
            // 规则速记不放在这里：顶部微标、左下角类型签与 KEYS 浮层已覆盖，再放是冗余。
            {
                float slotTopLocal = m.HeroPadding.y + m.TagSize.y + (m.ShowTopButton ? 48f : 68f);
                float slotBottomLocal = slotTopLocal + MenuLayoutMath.LoadoutCard(m.ShowTopButton).Size.y;
                float ctaTopLocal = _homeBoard.Height - m.HeroPadding.y - m.CtaSize.y;
                float hintFont = UITypography.FontSnapped(18f);
                float bottomLimit = mobile
                    ? ctaTopLocal - 12f
                    : ctaTopLocal - hintFont * 1.6f - 8f - 6f;
                float freeTop = slotBottomLocal + 12f;
                float freeH = Mathf.Max(0f, bottomLimit - freeTop);

                // 移动端空档浅，×1.28 后的 154 号会同时撞武器卡与 CTA；
                // 移动端取版面表原值 120（与桌面相等，不违反「移动端不小于桌面」）。
                float titleFont = mobile ? m.TitleFont : UITypography.FontSnapped(m.TitleFont);
                float titleBoxH = Mathf.Min(titleFont * 1.3f, freeH);
                float latinFont = UITypography.FontSnapped(m.LatinFont);
                float latinH = latinFont * 1.5f;
                bool withLatin = !mobile && titleBoxH + 8f + latinH <= freeH;
                float groupH = withLatin ? titleBoxH + 8f + latinH : titleBoxH;
                float titleY = freeTop + Mathf.Max(0f, (freeH - groupH) * 0.35f);
                CreateBoardText(card, "HeroTitle", GameVersion.APP_NAME, titleFont, true, ComicStyle.Ink,
                    new Vector2(m.HeroPadding.x, titleY), new Vector2(innerWidth, titleBoxH),
                    TextAlignmentOptions.MidlineLeft, 2f, FontStyles.Bold);

                if (withLatin)
                {
                    CreateBoardText(card, "HeroLatin", "INK TURF · SPLAT THE DOCK", latinFont, true,
                        ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                        new Vector2(m.HeroPadding.x + 4f, titleY + titleBoxH + 8f),
                        new Vector2(innerWidth - 4f, latinH),
                        TextAlignmentOptions.MidlineLeft, 6f, FontStyles.Bold | FontStyles.UpperCase);
                }
            }

            // 2. 中间区域：专为「武器整备卡」预留黄金展示位（由 WeaponSelectView 对齐 HeroWeaponSlot 渲染，白底黑边+硬阴影）
            // 桌面端在 CTA 上方添加极淡极简的操作提示微字，贴心且不喧宾夺主
            if (!mobile)
            {
                float hintFont = UITypography.FontSnapped(18f);
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

        /// <summary>
        /// Hero 卡头部右侧的 Bot 难度四档段选（新手 / 轻松 / 普通 / 硬核）。
        /// 位置：与 PLAY 角签同一行、右对齐贴 Hero 内边；窄窗先收窄段宽，绝不压住 PLAY。
        /// 点击只改 <see cref="GameSettingsService.BotDifficulty"/>，Bot 通过事件即时重建缩放配置。
        /// 返回整组占用的宽度（供模式微标让位）。
        /// </summary>
        private float BuildHeroDifficulty(RectTransform card, UIThemeSO theme, HomeMenuMetrics m,
            float innerWidth)
        {
            // 给 PLAY 角签 + 右侧模式微标留最小呼吸位，剩余宽度才是段选可用区。
            float available = Mathf.Max(0f, innerWidth - m.TagSize.x - 16f - 16f);
            DifficultyBarLayout layout = MenuLayoutMath.ResolveHeroDifficulty(m, available);

            float segHeight = m.DifficultySegmentSize.y;
            float rowY = m.HeroPadding.y + (m.TagSize.y - segHeight) * 0.5f;

            var rowGo = new GameObject("HeroDifficultyRow", typeof(RectTransform));
            rowGo.transform.SetParent(card, false);
            var row = (RectTransform)rowGo.transform;
            row.anchorMin = new Vector2(1f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(1f, 1f);
            row.anchoredPosition = new Vector2(-m.HeroPadding.x, -rowY);
            row.sizeDelta = new Vector2(layout.TotalWidth, segHeight);

            float fontSize = UITypography.FontSnapped(m.DifficultyFont);
            float x = 0f;
            for (int level = GameSettingsMath.MinBotDifficulty; level <= GameSettingsMath.MaxBotDifficulty; level++)
            {
                int capturedLevel = level;
                int index = level - GameSettingsMath.MinBotDifficulty;
                string label = GameSettingsMath.GetBotDifficultyLabel(level);

                Button button = CreateDifficultySegment(row, "Difficulty_" + level, label,
                    layout.SegmentWidth, segHeight, theme, fontSize);
                if (button == null) continue;

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x, 0f);

                _difficultyShapes[index] = button.GetComponent<UIInkShapeGraphic>();
                _difficultyTexts[index] = button.GetComponentInChildren<TextMeshProUGUI>();

                button.onClick.AddListener(() => OnDifficultySelected(capturedLevel));
                x += layout.SegmentWidth + layout.Gap;
            }

            RefreshDifficultyButtons();
            return layout.TotalWidth;
        }

        /// <summary>单段难度药丸：纸白实底 + 墨框 + 居中粗体字；选中态由 RefreshDifficultyButtons 改色。</summary>
        private static Button CreateDifficultySegment(RectTransform parent, string name, string label,
            float width, float height, UIThemeSO theme, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, height);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.GlossAlpha = 0f;
            shape.DrawGloss = false;

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = ComicStyle.Ink;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var font = UIThemeSO.Resolve(theme).ResolveFont(true, label);
            if (font != null) tmp.font = font;

            return button;
        }

        private void OnDifficultySelected(int level)
        {
            GameSettingsService.BotDifficulty = level;
            RefreshDifficultyButtons();
        }

        private void HandleBotDifficultyChanged(int level)
        {
            RefreshDifficultyButtons();
        }

        /// <summary>选中态：墨黑实底 + 金黄字；未选中：纸白实底 + 墨字。重建首页后引用自动更新。</summary>
        private void RefreshDifficultyButtons()
        {
            int current = GameSettingsService.BotDifficulty;
            for (int i = 0; i < _difficultyShapes.Length; i++)
            {
                UIInkShapeGraphic shape = _difficultyShapes[i];
                if (shape == null) continue;

                bool selected = (i + GameSettingsMath.MinBotDifficulty) == current;
                shape.color = selected ? ComicStyle.Ink : ComicStyle.Paper;
                shape.BorderColor = ComicStyle.Ink;
                shape.Refresh();

                TextMeshProUGUI text = _difficultyTexts[i];
                if (text != null)
                    text.color = selected ? ComicStyle.Yellow : ComicStyle.Ink;
            }
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
            float ctaX = _homeBoard.HeroX + m.HeroPadding.x;

            btnRect.anchorMin = new Vector2(0f, 1f);
            btnRect.anchorMax = new Vector2(0f, 1f);
            btnRect.pivot = new Vector2(0f, 1f);
            btnRect.anchoredPosition = new Vector2(ctaX, -ctaTop);
            btnRect.sizeDelta = new Vector2(ctaWidth, m.CtaSize.y);

            StyleStartButtonGeometric(theme, UITypography.FontSnapped(m.CtaFont), mobile);
        }

        /// <summary>右侧 2×2 次级卡：战绩（信息）/ 系统设置（可点）/ 操作指南或键位（信息）/ 检查更新（可点）。</summary>
        private void BuildHomeTiles(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
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
                Tag = "SETTINGS",
                Title = "系统设置",
                Info = BuildSettingsInfo(),
                Accent = theme.teamAColor,
                Interactive = true,
                ClickAction = OpenHomeSettings,
            }, theme, m, mobile, out _homeSettingsInfo, out _);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 2,
                Tag = mobile ? "GUIDE" : "KEYS",
                Title = mobile ? "操作指南" : "键位说明",
                Info = BuildKeysInfo(),
                Accent = ComicStyle.Blue,
                Interactive = true,
                ClickAction = () => ShowHomeInfo(HomeInfoTab.Controls),
            }, theme, m, mobile, out _homeKeysInfo, out _);

            HomeTileView updateTile = BuildHomeTile(new HomeTileSpec
            {
                Index = 3,
                Tag = "UPDATE",
                Title = "检查更新",
#if UNITY_WEBGL && !UNITY_EDITOR
                Info = "查看最新版本与更新记录",
#else
                Info = "联网检查新版本 · 当前 " + GameVersion.APP_VERSION_NAME,
#endif
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
            float rowWidth = contentWidth - m.TilePadding.x * 2f;

            // 角签宽度按标签文本自然宽度自适应（SETTINGS 比 RECORD 宽得多，固定 140px 会画出黑底）。
            CreateStageTag(content, "TileTag", spec.Tag, m.TileTagSize, m.TileTagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, m.TilePadding, TextAlignmentOptions.Center, rowWidth);

            float titleSize = UITypography.FontSnapped(m.TileTitleFont);
            float titleTop = m.TilePadding.y + m.TileTagSize.y + (mobile ? 20f : 18f);
            float titleHeight = titleSize * 1.25f;

            // `》` 独占标题行右侧走廊：双书名号实际约 2em 宽（旧值按 1.2em 预留），
            // 且不能再和信息行共行——移动端字号 ×1.28 后短信息也会顶到箭头下面。
            float arrowSize = UITypography.FontSnapped(m.TileArrowFont);
            float arrowHeight = arrowSize * 1.1f;
            float arrowGutter = spec.Interactive ? arrowSize * 2.1f + 12f : 0f;

            titleText = CreateBoardText(content, "TileTitle", spec.Title, titleSize, true, ComicStyle.Ink,
                new Vector2(m.TilePadding.x, titleTop),
                new Vector2(rowWidth - arrowGutter, titleHeight),
                TextAlignmentOptions.MidlineLeft, -2f, FontStyles.Bold);

            float infoSize = UITypography.FontSnapped(m.TileInfoFont);
            float infoHeight = infoSize * 1.7f;
            float infoTop = contentHeight - m.TilePadding.y - infoHeight;

            infoText = CreateBoardText(content, "TileInfo", spec.Info, infoSize, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                new Vector2(m.TilePadding.x, infoTop),
                new Vector2(rowWidth, infoHeight),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            if (spec.Interactive)
            {
                // `》` 行进箭头：只有可点卡才放（信息卡不放箭头，避免"假可点"）
                CreateBoardText(content, "TileArrow", "》", arrowSize, true, ComicStyle.Ink,
                    new Vector2(m.TilePadding.x + rowWidth - arrowGutter, titleTop + (titleHeight - arrowHeight) * 0.5f),
                    new Vector2(arrowGutter, arrowHeight),
                    TextAlignmentOptions.MidlineRight, 0f, FontStyles.Bold);

                AttachTileInteraction(go, shape, content, spec.ClickAction);
            }

            // 规范禁止 Truncate：宽度只能靠版面表兜住，超宽在开发期就报出来。
            WarnIfTextOverflows(titleText, rowWidth - arrowGutter, "次级卡标题 " + spec.Tag);
            FitTileInfoText(infoText);

            return new HomeTileView { Root = root, Content = content, Shape = shape, Title = titleText, Info = infoText };
        }

        /// <summary>
        /// 信息行自适应：战绩数字会随存档变大（「胜 128/256 · 涂地 12345㎡」），
        /// 超出可用宽度时先逐级缩字号（桌面底线 18 / 移动端 20），缩到底仍放不下才告警。
        /// 规范禁 Truncate，所以这里绝不截字；刷新读数（<see cref="RefreshHomeInfo"/>）也要重算。
        /// </summary>
        private static void FitTileInfoText(TextMeshProUGUI text)
        {
            if (text == null) return;

            float maxWidth = text.rectTransform.sizeDelta.x;
            float floor = UITypography.IsMobileLike ? 20f : 18f;
            text.fontSize = UITypography.FontSnapped(MenuLayoutMath.Home(UITypography.IsMobileLike).TileInfoFont);

            while (text.fontSize > floor && text.GetPreferredValues(text.text).x > maxWidth + 0.5f)
            {
                text.fontSize = Mathf.Max(floor, text.fontSize - 1f);
            }

            WarnIfTextOverflows(text, maxWidth, "次级卡信息");
        }

        /// <summary>开发期护栏：文字自然宽度超出盒子就告警（规范禁 Truncate，宽度必须由版面表兜住）。</summary>
        private static void WarnIfTextOverflows(TextMeshProUGUI text, float availableWidth, string label)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (text == null || string.IsNullOrEmpty(text.text) || availableWidth <= 0f) return;
            float preferredWidth = text.GetPreferredValues(text.text).x;
            if (preferredWidth > availableWidth + 0.5f)
            {
                Debug.LogWarning($"[MatchFlowUI] {label} 宽 {preferredWidth:0}px 超出可用 {availableWidth:0}px：请压缩文案或调版面表");
            }
#endif
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

#if !UNITY_WEBGL || UNITY_EDITOR
            CreateFooterTag("HomeVersionTag", m.VersionTag,
                GameVersion.APP_NAME + " " + GameVersion.APP_VERSION_NAME, theme,
                ComicStyle.Ink, ComicStyle.Yellow, m.FooterTagFont);
#endif
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
            Vector2 topLeft, TextAlignmentOptions align, float maxWidth = 0f)
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
            if (tmp != null)
            {
                tmp.fontStyle |= FontStyles.UpperCase;

                // 角签只放大不缩小：移动端 FontSnapped 会把 20 号放大到 26 号，SETTINGS 的自然宽度
                // 约 193px，固定 140px 黑底必然溢字。上限 maxWidth 由调用方按内容行宽给出，防止冲出卡片。
                float neededWidth = Mathf.Ceil(tmp.GetPreferredValues(tmp.text).x) + 20f;
                float fittedWidth = Mathf.Max(size.x, neededWidth);
                if (maxWidth > 0.01f) fittedWidth = Mathf.Min(fittedWidth, maxWidth);
                if (Mathf.Abs(fittedWidth - size.x) > 0.01f)
                {
                    rect.sizeDelta = new Vector2(fittedWidth, size.y);
                    tmp.rectTransform.sizeDelta = new Vector2(Mathf.Max(20f, fittedWidth - 20f), size.y);
                }
                WarnIfTextOverflows(tmp, fittedWidth - 20f, "角签 " + label);
            }
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

        /// <summary>设置卡信息行：主音量 + 震屏读数（移动端空间紧，省略尾注）。</summary>
        private static string BuildSettingsInfo()
        {
            int vol = -1;
            int shake = -1;
            try
            {
                vol = Mathf.RoundToInt(Mathf.Clamp01(GameSettingsService.MasterVolume) * 100f);
                shake = Mathf.RoundToInt(Mathf.Clamp01(GameSettingsService.ShakeScale) * 100f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 设置卡读取读数失败：" + ex.Message);
            }

            if (vol < 0 || shake < 0) return "音量 · 震屏 · 灵敏度 · 点击调整";
            return UITypography.IsMobileLike
                ? $"音量 {vol}% · 震屏 {shake}%"
                : $"音量 {vol}% · 震屏 {shake}% · 点击调整";
        }

        /// <summary>
        /// 设置卡：直开系统设置页。复用暂停菜单的设置视图（首页模式），
        /// 不带对局暂停语义——不动 timeScale / AudioListener，也不弹「继续游戏」等对局按钮。
        /// </summary>
        private void OpenHomeSettings()
        {
            var pause = Menus.PauseMenuOverlay.CreateInstance();
            if (pause != null) pause.OpenSettingsFromHome();
        }

        private static string BuildRecordInfo()
        {
            try
            {
                PlayerProfile profile = PlayerProfile.Current;
                if (profile != null)
                {
                    return $"LV.{profile.Level} · 胜场 {profile.Wins}/{profile.Matches} · 涂地 {profile.TotalTurf:0}㎡";
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 战绩卡读取档案失败：" + ex.Message);
            }

            return "LV.1 · 胜场 0/0 · 涂地 0㎡";
        }

        /// <summary>
        /// 键位卡信息行。移动端字号 ×1.28（18→23），信息行可用宽度只有 ~384px，
        /// 长句必须压成「动作 + 按键」四段短词，否则会顶出卡面。
        /// </summary>
        private static string BuildKeysInfo()
        {
            return UITypography.IsMobileLike
                ? "摇杆移动 · 射击 · 跳跃 · 大招"
                : "WASD 移动 · 左键 射击 · 空格 跳跃 · Q 大招";
        }

        /// <summary>首页读数（战绩 / 设置 / 键位）刷新：回到主菜单或设置项变化时数值会变。</summary>
        private void RefreshHomeInfo()
        {
            if (_homeRecordInfo != null)
            {
                _homeRecordInfo.text = BuildRecordInfo();
                FitTileInfoText(_homeRecordInfo);
            }

            if (_homeSettingsInfo != null)
            {
                _homeSettingsInfo.text = BuildSettingsInfo();
                FitTileInfoText(_homeSettingsInfo);
            }

            if (_homeKeysInfo != null)
            {
                _homeKeysInfo.text = BuildKeysInfo();
                FitTileInfoText(_homeKeysInfo);
            }
        }

        private void PlayHomeEntrance()
        {
            PlayStaggered(_homeEntrance);
        }

        /// <summary>结算入场：两块读数板先到，判定牌 / 台账卡在掷点揭晓时各自 pop-in。</summary>
        private void PlayResultEntrance()
        {
            _resultEntrance.Clear();
            if (_coverPlateA != null) _resultEntrance.Add(_coverPlateA.GetComponent<UISpringMotion>());
            if (_coverPlateB != null) _resultEntrance.Add(_coverPlateB.GetComponent<UISpringMotion>());
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
            GameSettingsService.SettingsChanged -= RefreshHomeInfo;
            GameSettingsService.BotDifficultyChanged -= HandleBotDifficultyChanged;
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
        /// 结算页 v2（几何大胆风 · 深色舞台上的计分场，参考 INKWAVE 结算的信息结构）：
        /// 1. 顶部 Hero 级 VICTORY / DEFEAT 判定大牌（移动端 ≥120），桌面右上角一枚金色「胜差」贴纸；
        /// 2. 中部两块「墨色读数板」（我方 / 敌方）：队色描边 + 超大百分比，中间一条双向生长的
        ///    拔河覆盖率条，白色分界线标出占比落点；掷点定格后胜方点亮「胜」签、败方读数转不透明灰；
        /// 3. 底部一张战报台账卡：双队 4v4 明细表（武器图标 / 姓名 / TURF / K.O. / DOWN），
        ///    本地玩家行队色高亮、队内 TURF 王挂 MVP；卡底一条勋章 chips + LV / 段位 + 经验条 + 本局 XP；
        /// 4. 关键 CTA「再来一局」黄底墨字直角大按钮 + 次级「返回」纸白墨框；升级提示是卡片顶边的黄页签。
        ///    重影修复：百分比改纯平 TMP（display Heavy），不再给 Legacy Text 挂 uGUI Shadow——
        ///    Shadow 复制的是不透明字形副本，配上半透明字色就会读成「灰字黑重影」。
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

            // 胜差贴纸（桌面右上角，INKWAVE 的 PHOTO FINISH / LANDSLIDE 同款信息位）：
            // 只做桌面，移动端屏幕更窄，胜差交给「败方读数转灰 + 胜签」表达。
            if (!mobile)
            {
                var tagGo = new GameObject("MatchTag", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(UIInkShapeGraphic));
                tagGo.transform.SetParent(rootRt, false);
                _matchTagRoot = (RectTransform)tagGo.transform;
                _matchTagRoot.anchorMin = new Vector2(0.5f, 0.5f);
                _matchTagRoot.anchorMax = new Vector2(0.5f, 0.5f);
                _matchTagRoot.pivot = new Vector2(0.5f, 0.5f);
                _matchTagRoot.anchoredPosition = new Vector2(486f, 332f);
                _matchTagRoot.sizeDelta = new Vector2(292f, 36f);

                var tagShape = tagGo.GetComponent<UIInkShapeGraphic>();
                tagShape.Shape = UIInkShape.SharpRect;
                tagShape.CornerRadius = ComicStyle.CornerSharp;
                tagShape.BorderWidth = ComicStyle.BorderThin;
                tagShape.BorderColor = ComicStyle.Ink;
                tagShape.color = ComicStyle.Yellow;
                tagShape.HardShadow = false;
                tagShape.Halftone = false;
                tagShape.raycastTarget = false;
                resolved.ApplyHardShadow(tagShape, UIHardShadowLevel.Tag);

                _matchTagText = CreateTmpText(tagGo.transform, "Label", "", UITypography.FontSnapped(18f),
                    isDisplay: true, ComicStyle.Ink, Vector2.zero, new Vector2(292f, 36f), 1.5f,
                    TextAlignmentOptions.Center);

                tagGo.SetActive(false);
            }

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

            // 2. 计分场：两块墨色读数板（我方 / 敌方）+ 中间拔河覆盖率条。
            //    读数板 = 深墨底 + 队色描边，比两张纯白卡更沉、更有层级；
            //    中间双向条把「谁涂得多」变成一条一眼可读的拉扯，胜方挂金「胜」签。
            _teamColorA = resolved.GetTeamColor(Team.TeamA);
            _teamColorB = resolved.GetTeamColor(Team.TeamB);

            float plateW = mobile ? 300f : 290f;
            float plateH = mobile ? 164f : 150f;
            float plateX = mobile ? 452f : 470f;
            float plateY = mobile ? 182f : 190f;
            float percentBase = mobile ? 62f : 76f;

            _coverPlateA = CreateCoveragePlate(rootRt, "CoverPlateA", resolved, Team.TeamA, "我方",
                new Vector2(-plateX, plateY), new Vector2(plateW, plateH), percentBase,
                out _plateShapeA, out _percentTmpA, out _winMarkA);
            _coverPlateB = CreateCoveragePlate(rootRt, "CoverPlateB", resolved, Team.TeamB, "敌方",
                new Vector2(plateX, plateY), new Vector2(plateW, plateH), percentBase,
                out _plateShapeB, out _percentTmpB, out _winMarkB);

            // 旧版 Legacy 百分比文本必须彻底退场：TMP 新读数一旦叠在它们上面，
            // 灰底字形会从板边漏出来，被误读成「重影」。
            if (resultPercentA != null) resultPercentA.gameObject.SetActive(false);
            if (resultPercentB != null) resultPercentB.gameObject.SetActive(false);
            if (resultBarA != null) resultBarA.gameObject.SetActive(false);
            if (resultBarB != null) resultBarB.gameObject.SetActive(false);

            // 3. 拔河覆盖率条：两段队色从左右两端向中间生长，白色分界线预告占比落点。
            float barW = mobile ? 560f : 620f;
            float barH = mobile ? 40f : 36f;
            var slotGo = new GameObject("CoverBarSlot", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            slotGo.transform.SetParent(rootRt, false);
            var slotRt = (RectTransform)slotGo.transform;
            slotRt.anchorMin = new Vector2(0.5f, 0.5f);
            slotRt.anchorMax = new Vector2(0.5f, 0.5f);
            slotRt.pivot = new Vector2(0.5f, 0.5f);
            slotRt.anchoredPosition = new Vector2(0f, plateY);
            slotRt.sizeDelta = new Vector2(barW, barH);

            var slotShape = slotGo.GetComponent<UIInkShapeGraphic>();
            slotShape.Shape = UIInkShape.SharpRect;
            slotShape.CornerRadius = ComicStyle.CornerSharp;
            slotShape.BorderWidth = ComicStyle.BorderThin;
            slotShape.BorderColor = ComicStyle.Paper;
            slotShape.color = BarSlotInk;
            slotShape.HardShadow = false;
            slotShape.Halftone = false;
            slotShape.raycastTarget = false;
            resolved.ApplyHardShadow(slotShape, UIHardShadowLevel.Panel);

            _coverInnerW = barW - 14f; // 3px 白框 + 4px 内边距，左右各 7px
            _coverInnerH = barH - 14f;

            _coverFillA = CreateCoverFill(slotRt, "FillA", _teamColorA, fromLeft: true);
            _coverFillB = CreateCoverFill(slotRt, "FillB", _teamColorB, fromLeft: false);

            var divGo = new GameObject("CoverDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            divGo.transform.SetParent(slotRt, false);
            _coverDivider = (RectTransform)divGo.transform;
            _coverDivider.anchorMin = new Vector2(0.5f, 0.5f);
            _coverDivider.anchorMax = new Vector2(0.5f, 0.5f);
            _coverDivider.pivot = new Vector2(0f, 0.5f);
            _coverDivider.sizeDelta = new Vector2(3f, _coverInnerH);
            _coverDivider.anchoredPosition = Vector2.zero;
            var divImg = divGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(divImg);
            divImg.color = ComicStyle.Paper;
            divImg.raycastTarget = false;
            divGo.SetActive(false);

            // 旧场景柱条容器隐藏（其子 Image 已单独 Deactivate，容器也不应参与射线）。
            HideLegacyResultBar(resultPanel.transform, "ResultBarA");
            HideLegacyResultBar(resultPanel.transform, "ResultBarB");

            // 5. 判定文字退场：顶部大牌 + 两个巨大百分比已经把胜负说完，
            //    屏幕中间不再出现「正在计算… / 惜败 · 再接再厉」这类第二句结论。
            if (verdictText != null)
            {
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            // 5b. 战报台账卡（双队明细 + 勋章 + LV + 经验条合为一块白卡）
            BuildLedgerCard(rootRt, resolved, mobile);

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

            // 7. 次级「返回」按钮（纸白底 + 3px 墨框 + Button 硬影）
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
                bBtn.onClick.AddListener(OnResultBackClicked);

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

                // 每次都重置监听：这个按钮曾被误绑到 OnStartClicked（→ StartMatch → RestartMatch），
                // 于是「返回」实际是「再来一局」。场景里若已存在同名按钮也靠这里补绑/纠正。
                var backBtn = backBtnTrans.GetComponent<Button>();
                if (backBtn != null)
                {
                    backBtn.onClick.RemoveListener(OnStartClicked);
                    backBtn.onClick.RemoveListener(OnResultBackClicked);
                    backBtn.onClick.AddListener(OnResultBackClicked);
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
        /// 覆盖率读数板：深墨底 + 3px 队色描边 + 顶部「我方 / 敌方」小标 + 超大百分比（display Heavy）。
        /// 百分比一律纯平 TMP（不挂 Shadow / Outline）：v1 给 Legacy Text 挂 uGUI Shadow 做硬影，
        /// 再配上半透明降灰，屏幕上就是「灰字 + 不透明黑色副本」的重影。
        /// </summary>
        private RectTransform CreateCoveragePlate(RectTransform parent, string name, UIThemeSO resolved,
            Team team, string label, Vector2 position, Vector2 size, float percentSize,
            out UIInkShapeGraphic shape, out TextMeshProUGUI percent, out RectTransform winMark)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            Color teamColor = resolved.GetTeamColor(team);

            shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = teamColor;
            shape.color = PlateInk;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            resolved.ApplyHardShadow(shape, UIHardShadowLevel.Panel);

            var motion = go.GetComponent<UISpringMotion>();
            motion.SetTheme(resolved);

            var labelTmp = CreateTmpText(go.transform, "Label", label, UITypography.FontSnapped(20f),
                isDisplay: true, ComicStyle.WithAlpha(ComicStyle.Paper, 0.72f),
                new Vector2(0f, size.y * 0.5f - 26f), new Vector2(size.x - 24f, 28f), 3f,
                TextAlignmentOptions.Center);
            labelTmp.fontStyle = FontStyles.Bold;

            percent = CreateTmpText(go.transform, "Percent", "0.0%", UITypography.FontSnapped(percentSize),
                isDisplay: true, teamColor, new Vector2(0f, -6f), new Vector2(size.x - 16f, size.y * 0.62f),
                1f, TextAlignmentOptions.Center);
            percent.fontStyle = FontStyles.Bold;
            percent.overflowMode = TextOverflowModes.Overflow;

            // 胜签：黄底墨字「胜」，挂在板面右上角，掷点结束后才点亮。
            var markGo = new GameObject("WinMark", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            markGo.transform.SetParent(go.transform, false);
            winMark = (RectTransform)markGo.transform;
            winMark.anchorMin = new Vector2(1f, 1f);
            winMark.anchorMax = new Vector2(1f, 1f);
            winMark.pivot = new Vector2(1f, 1f);
            winMark.anchoredPosition = new Vector2(-10f, -10f);
            winMark.sizeDelta = new Vector2(52f, 30f);

            var markShape = markGo.GetComponent<UIInkShapeGraphic>();
            markShape.Shape = UIInkShape.SharpRect;
            markShape.CornerRadius = ComicStyle.CornerSharp;
            markShape.BorderWidth = ComicStyle.BorderThin;
            markShape.BorderColor = ComicStyle.Ink;
            markShape.color = ComicStyle.Yellow;
            markShape.HardShadow = false;
            markShape.Halftone = false;
            markShape.raycastTarget = false;

            var markLabel = CreateTmpText(markGo.transform, "Label", "胜", UITypography.FontSnapped(20f),
                isDisplay: true, ComicStyle.Ink, Vector2.zero, new Vector2(52f, 30f), 0f,
                TextAlignmentOptions.Center);
            markLabel.fontStyle = FontStyles.Bold;

            markGo.SetActive(false);
            return rt;
        }

        /// <summary>拔河条填充：1×1 纯白精灵 + 队色，锚在槽内左/右端，宽度由 UpdateResultNumbers 逐帧写入。</summary>
        private static Image CreateCoverFill(RectTransform slot, string name, Color color, bool fromLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(slot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(fromLeft ? 0f : 1f, 0f);
            rt.anchorMax = new Vector2(fromLeft ? 0f : 1f, 1f);
            rt.pivot = new Vector2(fromLeft ? 0f : 1f, 0.5f);
            rt.anchoredPosition = new Vector2(fromLeft ? 7f : -7f, 0f);
            rt.sizeDelta = new Vector2(0f, -14f);

            var img = go.GetComponent<Image>();
            UIFlatSprite.MakeFlat(img);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>参战者显示名（与 KillFeedEvent.FormatCombatantName 同一套规则）。</summary>
        private static string FormatCombatantLabel(CombatantHealth ch, bool isLocal)
        {
            if (isLocal) return "YOU";
            if (ch == null) return "PLAYER";

            string raw = ch.gameObject.name;
            if (raw.StartsWith("BotA_")) return "ALLY " + raw.Substring(5);
            if (raw.StartsWith("BotB_")) return "ENEMY " + raw.Substring(5);
            if (raw.StartsWith("Bot")) return raw.ToUpperInvariant();
            return "PLAYER";
        }

        /// <summary>武器图标名（UIIconLibrary / P1RuntimeAssets 的 key，与 KillFeedView 一致）。</summary>
        private static string WeaponIconName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller: return "weapon_roller";
                case WeaponKind.Charger: return "weapon_charger";
                case WeaponKind.Dualies: return "weapon_dualies";
                case WeaponKind.Slosher: return "weapon_slosher";
                case WeaponKind.Blaster: return "weapon_blaster";
                case WeaponKind.Splatling: return "weapon_splatling";
                default: return "weapon_shooter";
            }
        }

        /// <summary>
        /// 掷点定格后强调胜方：胜方板角点亮黄「胜」签，败方读数转不透明灰（#6F6F6F）。
        /// 这里绝不使用半透明降灰：半透明字形 + 任何描边/副本都会读成「重影」。
        /// </summary>
        private void ApplyWinnerEmphasis(MatchManager match, bool playerWon)
        {
            if (_winnerEmphasisApplied) return;
            _winnerEmphasisApplied = true;

            if (_winMarkA != null) _winMarkA.gameObject.SetActive(playerWon);
            if (_winMarkB != null) _winMarkB.gameObject.SetActive(!playerWon);

            if (_percentTmpA != null) _percentTmpA.color = playerWon ? _teamColorA : PlateDimReadout;
            if (_percentTmpB != null) _percentTmpB.color = playerWon ? PlateDimReadout : _teamColorB;

            // 桌面胜差贴纸：PHOTO FINISH / LANDSLIDE / MARGIN，数据直接来自终场覆盖率。
            if (_matchTagRoot != null && _matchTagText != null && match != null)
            {
                float margin = Mathf.Abs(match.FinalCoverageA - match.FinalCoverageB);
                _matchTagText.text = BuildMatchTagText(playerWon, margin);
                _matchTagRoot.gameObject.SetActive(true);
            }
        }

        private static string BuildMatchTagText(bool playerWon, float margin)
        {
            if (margin <= 3f)
            {
                return (playerWon ? "PHOTO FINISH · " : "CLOSE LOSS · ") + margin.ToString("F1") + "%";
            }

            if (margin >= 15f)
            {
                return (playerWon ? "LANDSLIDE +" : "BLOWOUT -") + margin.ToString("F1") + "%";
            }

            return (playerWon ? "MARGIN +" : "MARGIN -") + margin.ToString("F1") + "%";
        }

        /// <summary>
        /// 战报台账卡（结算页下半屏唯一主体）：
        /// 上半 = 双队 4v4 明细（武器图标 / 姓名 / TURF / K.O. / DOWN，YOU 行队色高亮、队内涂地王挂金条），
        /// 中间一条 2px 分隔规，下半 = 勋章 chips + LV / 段位 + 经验条 + 本局 XP。
        /// 纸白 + 3px 墨框 + 面板硬影 + 顶部 5px 身份色带：全页唯一的「纸上数据」。
        /// 明细行不在这里建：每局揭晓时由 <see cref="RebuildRosterRows"/> 按实时统计重建。
        /// </summary>
        private void BuildLedgerCard(RectTransform parent, UIThemeSO resolved, bool mobile)
        {
            if (_reportPanelRoot != null) return;

            const float bandH = 5f; // 顶部身份色带（规范：5px，不承载文字）
            float frameW = mobile ? 1180f : 1240f;
            float frameH = mobile ? 404f : 372f;
            float frameY = mobile ? -110f : -79f;
            float padX = mobile ? 27f : 25f;
            float inset = ComicStyle.BorderThin + padX;

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
            var bandGo = new GameObject("ReportBand", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
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

            // ---------- 布局缓存（RebuildRosterRows 用同一套坐标） ----------
            float half = frameH * 0.5f;
            _ledgerColLeft = -frameW * 0.5f + inset;
            _ledgerColRight = frameW * 0.5f - inset;
            _ledgerHeaderY = half - (mobile ? 44f : 40f);
            _ledgerRowTopY = _ledgerHeaderY - (mobile ? 42f : 38f);
            _ledgerRowH = mobile ? 48f : 44f;
            _ledgerNameW = mobile ? 130f : 140f;
            _ledgerTurfX = mobile ? 345f : 345f;
            _ledgerKoX = mobile ? 215f : 215f;
            _ledgerDownX = mobile ? 105f : 105f;

            // 两支队伍的表头
            CreateRosterHeader(frameGo.transform, resolved, Team.TeamA, "我方", -1f, mobile);
            CreateRosterHeader(frameGo.transform, resolved, Team.TeamB, "敌方", 1f, mobile);

            // 中缝竖规：两队明细之间的 2px 硬线
            float rowsBottom = _ledgerRowTopY - _ledgerRowH * 4f;
            CreateGeometricSquare(frameGo.transform, "LedgerDivider", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, (_ledgerHeaderY + rowsBottom) * 0.5f + 4f),
                new Vector2(2f, _ledgerHeaderY - rowsBottom - 4f), 0f, ComicStyle.PaperAlt);

            // 明细区与成长区之间的分隔规
            float ruleY = mobile ? -80f : -72f;
            CreateGeometricSquare(frameGo.transform, "LedgerRule", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, ruleY),
                new Vector2(frameW - inset * 2f, 2f), 0f, ComicStyle.PaperAlt);

            // ---------- 成长区：勋章 chips + LV/段位 + XP ----------
            float stripY = mobile ? -112f : -104f;

            var medalsGo = new GameObject("MedalsRow", typeof(RectTransform));
            medalsGo.transform.SetParent(frameGo.transform, false);
            var medalsRt = (RectTransform)medalsGo.transform;
            medalsRt.anchorMin = new Vector2(0.5f, 0.5f);
            medalsRt.anchorMax = new Vector2(0.5f, 0.5f);
            medalsRt.pivot = new Vector2(0f, 0.5f);
            float gainW = mobile ? 210f : 200f;
            float trackW = mobile ? 330f : 350f;
            float medalsLeft = mobile ? -334f : -370f;
            medalsRt.anchoredPosition = new Vector2(medalsLeft, stripY);
            // 勋章行从 LV 右侧一路铺到经验条左端（留 12px 呼吸位），宽度由两侧锚点相减得到。
            float xpTrackLeft = frameW * 0.5f - inset - trackW;
            float medalsW = xpTrackLeft - 12f - medalsLeft;
            medalsRt.sizeDelta = new Vector2(Mathf.Max(200f, medalsW), mobile ? 38f : 34f);
            _medalsContainer = medalsRt;

            var levelGo = new GameObject("LevelText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            levelGo.transform.SetParent(frameGo.transform, false);
            var levelRt = (RectTransform)levelGo.transform;
            levelRt.anchorMin = new Vector2(0.5f, 0.5f);
            levelRt.anchorMax = new Vector2(0.5f, 0.5f);
            levelRt.pivot = new Vector2(0f, 0.5f);
            levelRt.anchoredPosition = new Vector2(_ledgerColLeft, stripY);
            levelRt.sizeDelta = new Vector2(mobile ? 210f : 200f, mobile ? 60f : 56f);

            _xpLevelText = levelGo.GetComponent<TextMeshProUGUI>();
            _xpLevelText.text = "LV. 1";
            _xpLevelText.font = resolved.ResolveFont(true, "LV. 1");
            _xpLevelText.fontSize = UITypography.FontSnapped(40f);
            _xpLevelText.fontStyle = FontStyles.Bold;
            _xpLevelText.alignment = TextAlignmentOptions.MidlineLeft;
            _xpLevelText.color = ComicStyle.Ink;
            _xpLevelText.raycastTarget = false;
            _xpLevelText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpLevelText.overflowMode = TextOverflowModes.Overflow;

            var rankGo = new GameObject("RankText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            rankGo.transform.SetParent(frameGo.transform, false);
            var rankRt = (RectTransform)rankGo.transform;
            rankRt.anchorMin = new Vector2(0.5f, 0.5f);
            rankRt.anchorMax = new Vector2(0.5f, 0.5f);
            rankRt.pivot = new Vector2(0f, 0.5f);
            rankRt.anchoredPosition = new Vector2(_ledgerColLeft, stripY - (mobile ? 40f : 38f));
            rankRt.sizeDelta = new Vector2(mobile ? 320f : 300f, 24f);

            _xpRankNameText = rankGo.GetComponent<TextMeshProUGUI>();
            _xpRankNameText.text = "FRESH RECRUIT";
            _xpRankNameText.font = resolved.ResolveFont(true, "FRESH RECRUIT");
            _xpRankNameText.fontSize = UITypography.FontSnapped(18f);
            _xpRankNameText.fontStyle = FontStyles.Bold;
            _xpRankNameText.characterSpacing = 1.5f;
            _xpRankNameText.alignment = TextAlignmentOptions.MidlineLeft;
            _xpRankNameText.color = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.75f);
            _xpRankNameText.raycastTarget = false;
            _xpRankNameText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpRankNameText.overflowMode = TextOverflowModes.Overflow;

            var gainGo = new GameObject("XpGainText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            gainGo.transform.SetParent(frameGo.transform, false);
            var gainRt = (RectTransform)gainGo.transform;
            // 必须用中心锚点 + 中心系坐标：右边缘锚点会把 +XP 再往右推一个 contentRight。
            gainRt.anchorMin = new Vector2(0.5f, 0.5f);
            gainRt.anchorMax = new Vector2(0.5f, 0.5f);
            gainRt.pivot = new Vector2(1f, 0.5f);
            gainRt.anchoredPosition = new Vector2(_ledgerColRight, stripY + 14f);
            gainRt.sizeDelta = new Vector2(gainW, 28f);

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

            // 经验条：纯色直角，无数字标签，长度本身就是进度。
            var trackGo = new GameObject("XpTrack", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            trackGo.transform.SetParent(frameGo.transform, false);
            var trackRt = (RectTransform)trackGo.transform;
            trackRt.anchorMin = new Vector2(0.5f, 0.5f);
            trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.pivot = new Vector2(1f, 0.5f);
            trackRt.anchoredPosition = new Vector2(_ledgerColRight, stripY - 16f);
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
            var luGo = new GameObject("LevelUpBadge", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
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

            var luTxtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
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

        /// <summary>
        /// 台账表头：队色小方块 + 「我方 / 敌方」+ TURF / K.O. / DOWN 列头。
        /// sign = -1 左列（数字右对齐到中缝），+1 右列（数字左对齐到中缝）。
        /// </summary>
        private void CreateRosterHeader(Transform parent, UIThemeSO resolved, Team team, string label,
            float sign, bool mobile)
        {
            float colEdge = sign < 0f ? _ledgerColLeft : _ledgerColRight;
            float chipX = colEdge - sign * 14f;

            // 队色小方块紧贴外侧边；队名从方块右侧 6px 起排（中心锚点要按半个盒宽回退）。
            CreateGeometricSquare(parent, "TeamChip" + team, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(chipX, _ledgerHeaderY),
                new Vector2(18f, 18f), 0f, resolved.GetTeamColor(team));

            const float labelW = 90f;
            var labelTmp = CreateTmpText(parent, "TeamLabel" + team, label, UITypography.FontSnapped(20f),
                isDisplay: true, ComicStyle.Ink,
                new Vector2(colEdge - sign * (14f + 9f + 6f + labelW * 0.5f), _ledgerHeaderY),
                new Vector2(labelW, 26f), 2f,
                sign < 0f ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            labelTmp.fontStyle = FontStyles.Bold;

            Color headColor = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.55f);
            CreateTmpText(parent, "HeadTurf" + team, "TURF", UITypography.FontSnapped(18f),
                isDisplay: false, headColor, new Vector2(sign * _ledgerTurfX, _ledgerHeaderY),
                new Vector2(110f, 24f), 1f,
                sign < 0f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            CreateTmpText(parent, "HeadKo" + team, "K.O.", UITypography.FontSnapped(18f),
                isDisplay: false, headColor, new Vector2(sign * _ledgerKoX, _ledgerHeaderY),
                new Vector2(80f, 24f), 1f,
                sign < 0f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            CreateTmpText(parent, "HeadDown" + team, "DOWN", UITypography.FontSnapped(18f),
                isDisplay: false, headColor, new Vector2(sign * _ledgerDownX, _ledgerHeaderY),
                new Vector2(80f, 24f), 1f,
                sign < 0f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        }

        /// <summary>
        /// 按实时统计重建双队明细行：队伍分列、队内按涂地降序；本地玩家行队色高亮，
        /// 队内涂地王（非本地）加金色外条。每局揭晓时调用一次。
        /// </summary>
        private void RebuildRosterRows(UIThemeSO resolved)
        {
            for (int i = 0; i < _rosterRowObjects.Count; i++)
            {
                if (_rosterRowObjects[i] != null) Destroy(_rosterRowObjects[i]);
            }
            _rosterRowObjects.Clear();
            _rosterBuilt = true;

            if (_reportPanelRoot == null) return;

            CombatantHealth localHealth = null;
            if (PlayerController.LocalPlayer != null) localHealth = PlayerController.LocalPlayer.Health;

            var entries = new List<RosterEntry>(8);
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth ch = all[i];
                if (ch == null) continue;

                CombatantMatchStats stats = default;
                MatchStatsService.TryGet(ch, out stats);

                var weapon = ch.GetComponent<WeaponController>();
                entries.Add(new RosterEntry
                {
                    Health = ch,
                    Stats = stats,
                    IsLocal = ch == localHealth,
                    Kind = weapon != null ? weapon.CurrentWeaponKind : WeaponKind.Shooter,
                    Label = FormatCombatantLabel(ch, ch == localHealth),
                    Team = ch.Team
                });
            }

            entries.Sort((x, y) => y.Stats.TurfSquareMeters.CompareTo(x.Stats.TurfSquareMeters));

            int rowA = 0;
            int rowB = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                RosterEntry entry = entries[i];
                bool teamA = entry.Team == Team.TeamA;
                int slot = teamA ? rowA++ : rowB++;
                if (slot >= 4) continue; // 台账只收 4v4，超出不绘制
                CreateRosterRow(resolved, entry, slot, teamA);
            }
        }

        private void CreateRosterRow(UIThemeSO resolved, RosterEntry entry, int slot, bool teamA)
        {
            float sign = teamA ? -1f : 1f;
            float colEdge = teamA ? _ledgerColLeft : _ledgerColRight;
            float rowY = _ledgerRowTopY - _ledgerRowH * slot - _ledgerRowH * 0.5f;
            float colAbs = Mathf.Abs(colEdge);
            float rowW = colAbs - 15f;
            float halfW = rowW * 0.5f;
            float rowCenterX = sign * (colAbs - halfW);

            // 行内坐标：先按「距外侧边」定列，再换算到以行中心为原点的局部坐标（左右镜像同一套公式）。
            float Local(float dist) => sign * (halfW - dist);
            float iconX = Local(24f);
            float nameX = Local(46f + _ledgerNameW * 0.5f);
            float turfX = Local(colAbs - _ledgerTurfX);
            float koX = Local(colAbs - _ledgerKoX);
            float downX = Local(colAbs - _ledgerDownX);

            var rowGo = new GameObject(teamA ? "RowA" : "RowB" + (slot + 1), typeof(RectTransform));
            rowGo.transform.SetParent(_reportPanelRoot, false);
            var rowRt = (RectTransform)rowGo.transform;
            rowRt.anchorMin = new Vector2(0.5f, 0.5f);
            rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(rowCenterX, rowY);
            rowRt.sizeDelta = new Vector2(rowW, _ledgerRowH);
            _rosterRowObjects.Add(rowGo);

            bool mvp = slot == 0 && entry.Stats.TurfSquareMeters > 0.5f;
            if (entry.IsLocal || mvp)
            {
                var plateGo = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(UIInkShapeGraphic));
                plateGo.transform.SetParent(rowGo.transform, false);
                var plateRt = (RectTransform)plateGo.transform;
                Stretch(plateRt);
                plateRt.offsetMin = new Vector2(2f, 2f);
                plateRt.offsetMax = new Vector2(-2f, -2f);

                var plate = plateGo.GetComponent<UIInkShapeGraphic>();
                plate.Shape = UIInkShape.SharpRect;
                plate.CornerRadius = ComicStyle.CornerSharp;
                plate.BorderWidth = 2f;
                plate.BorderColor = entry.IsLocal ? resolved.GetTeamColor(entry.Team) : ComicStyle.Yellow;
                plate.color = entry.IsLocal
                    ? ComicStyle.WithAlpha(resolved.GetTeamColor(entry.Team), 0.12f)
                    : ComicStyle.WithAlpha(ComicStyle.Yellow, 0.16f);
                plate.HardShadow = false;
                plate.Halftone = false;
                plate.raycastTarget = false;
            }

            // 武器图标：取不到就隐藏（规范：绝不画空白 Image）。
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(rowGo.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(iconX, 0f);
            iconRt.sizeDelta = new Vector2(26f, 26f);
            var icon = iconGo.GetComponent<Image>();
            Sprite iconSprite = UIIconLibrary.Get(WeaponIconName(entry.Kind));
            if (iconSprite != null)
            {
                icon.sprite = iconSprite;
                icon.color = ComicStyle.Ink;
                icon.preserveAspect = true;
            }
            else
            {
                iconGo.SetActive(false);
            }
            icon.raycastTarget = false;

            // 姓名：左列左对齐、右列右对齐（镜像）。
            CreateTmpText(rowGo.transform, "Name", entry.Label, UITypography.FontSnapped(20f),
                isDisplay: true, ComicStyle.Ink,
                new Vector2(nameX, 0f),
                new Vector2(_ledgerNameW, _ledgerRowH), 0.5f,
                teamA ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight)
                .fontStyle = FontStyles.Bold;

            // 三项统计：TURF / K.O. / DOWN，数字统一按「距离外侧边」定位，保证左右镜像对齐。
            CreateTmpText(rowGo.transform, "Turf", Mathf.RoundToInt(entry.Stats.TurfSquareMeters).ToString(),
                UITypography.FontSnapped(22f), isDisplay: true, ComicStyle.Ink,
                new Vector2(turfX, 0f), new Vector2(110f, _ledgerRowH), 0f,
                teamA ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);

            CreateTmpText(rowGo.transform, "Ko", entry.Stats.Kills.ToString(),
                UITypography.FontSnapped(22f), isDisplay: true, ComicStyle.Ink,
                new Vector2(koX, 0f), new Vector2(80f, _ledgerRowH), 0f,
                teamA ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);

            CreateTmpText(rowGo.transform, "Down", entry.Stats.Deaths.ToString(),
                UITypography.FontSnapped(22f), isDisplay: true, DimReadout,
                new Vector2(downX, 0f), new Vector2(80f, _ledgerRowH), 0f,
                teamA ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        }

        /// <summary>台账明细行数据（避免每行多次 GetComponent / 名称拼接）。</summary>
        private struct RosterEntry
        {
            public CombatantHealth Health;
            public CombatantMatchStats Stats;
            public bool IsLocal;
            public WeaponKind Kind;
            public string Label;
            public Team Team;
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
#if UNITY_WEBGL && !UNITY_EDITOR
                // 网页试玩页不展示游戏版本号。
                versionText.gameObject.SetActive(false);
#else
                versionText.text = $"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}";
#endif
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
#if UNITY_WEBGL && !UNITY_EDITOR
            // 浏览器里 DownloadHandlerFile 与 Releases API（跨域/安装包落地）都不可用，直接开 Releases 页。
            Application.OpenURL("https://github.com/favoroo/moshuizuozhan/releases/latest");
#else
            UpdateManager.Instance?.CheckForUpdate(manual: true);
#endif
        }

        /// <summary>首页信息卡（战绩 / 键位说明）打开详情浮层：浮层自带遮罩与同帧输入仲裁。</summary>
        private void ShowHomeInfo(HomeInfoTab tab)
        {
            if (readyPanel == null) return;
            HomeInfoOverlay.Show(readyPanel.transform as RectTransform, UIThemeSO.Resolve(uiTheme), tab);
        }

        private void OnStartClicked()
        {
            TryStartMatch();
        }

        /// <summary>
        /// 结算页次级「返回」：回主菜单（Ready）。
        /// 绝不能走 OnStartClicked——那条路是 StartMatch → RestartMatch，会把「返回」变成「再来一局」。
        /// </summary>
        private void OnResultBackClicked()
        {
            var match = MatchManager.Instance;
            if (match != null) match.ReturnToMenu();
        }

        /// <summary>
        /// 开赛统一入口：移动端正在编辑按键布局（<see cref="MobileLayoutCustomizer.IsEditingAny"/>）
        /// 或首页详情浮层打开时不允许开赛。
        /// 布局编辑会把 Time.timeScale 置 0，此时若还能被 Space/Enter/R 开赛，倒计时会冻在编辑层背后。
        /// </summary>
        private bool CanStartMatch()
        {
            if (HomeInfoOverlay.IsOpen) return false;
            if (MobileLayoutCustomizer.IsEditingAny) return false;
            return true;
        }

        private void TryStartMatch()
        {
            if (!CanStartMatch()) return;

            var match = MatchManager.Instance;
            if (match == null) return;
            if (match.CurrentPhase != MatchManager.Phase.Ready && match.CurrentPhase != MatchManager.Phase.Result) return;

            match.StartMatch();
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

            // 快捷键开赛与重开：首页信息浮层 / 移动端按键布局编辑打开时让路，
            // 否则 Space/Enter 会隔着遮罩直接把比赛开了（布局编辑把 timeScale 置 0，倒计时会冻住）。
            if (!Application.isMobilePlatform && !MobileTouchUI.IsTouchSimulationActive &&
                !HomeInfoOverlay.IsOpen && !MobileLayoutCustomizer.IsEditingAny &&
                (match.CurrentPhase == MatchManager.Phase.Ready || match.CurrentPhase == MatchManager.Phase.Result))
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame))
                {
                    TryStartMatch();
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
                // 从对局/入场演出中返回主菜单：撤掉可能还挂着的横幅
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
                // 开赛只播一次墨幕入场（由 ScreenInkTransitionView 接管），不再有 3-2-1 数字。
                // 菜单面板必须在 PhaseChanged 里立刻收掉：它挂在 sortingOrder 10 的画布上，
                // 入场墨幕在 HUD 画布（0），晚一帧关闭就会看到菜单盖在墨幕上。
                if (readyPanel != null) readyPanel.SetActive(false);
                if (resultPanel != null) resultPanel.SetActive(false);
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
                // 场景里的 MatchBannerView 目前是停用状态（m_IsActive=0），调用不会显示；
                // 保留是为了横幅系统重新启用时 GO 仍与墨幕撕开对齐。
                if (bannerView != null && !firstSync) bannerView.ShowGo();
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
        /// 收起结算页「掷点后才出现」的那一层（台账卡 / 勋章行 / 成长条 / 胜签 / 胜差贴纸），
        /// 让下一局的结算从干净的落后状态重新播放。
        /// </summary>
        private void HideResultRevealVisuals()
        {
            if (_reportPanelRoot != null) _reportPanelRoot.gameObject.SetActive(false);
            if (_levelUpBadgeRoot != null) _levelUpBadgeRoot.gameObject.SetActive(false);
            if (_winMarkA != null) _winMarkA.gameObject.SetActive(false);
            if (_winMarkB != null) _winMarkB.gameObject.SetActive(false);
            if (_matchTagRoot != null) _matchTagRoot.gameObject.SetActive(false);
            _winnerEmphasisApplied = false;
            _rosterBuilt = false;

            // 败方数字的降灰要在下一局恢复，否则第二次结算会沿用上一局的胜负配色。
            if (_percentTmpA != null) _percentTmpA.color = _teamColorA;
            if (_percentTmpB != null) _percentTmpB.color = _teamColorB;

            UpdateCoverReadout(0f, 0f, 0f);
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

            // 旧 3-2-1 数字倒数已退役：Countdown 阶段只播一次墨幕入场（ScreenInkTransitionView），
            // 这里不再推进任何倒计时数字，也不再回退到旧 Text。

            // 保持旧 Text 兼容（字段保留给已序列化的场景，运行时永远隐藏）
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
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

            UpdateCoverReadout(a, b, eased);

            if (verdictText != null)
            {
                // 胜负结论已由顶部大牌 + 两块读数板承担，这里不再写第二句解说。
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            if (rollProgress >= 1f)
            {
                _numbersSettled = true;
                UpdateCoverReadout(match.FinalCoverageA, match.FinalCoverageB, 1f);

                // 掷点定格后点亮胜方：顶部判定大牌 + 胜方「胜」签，败方读数转不透明灰。
                if (!_badgePlayed)
                {
                    _badgePlayed = true;
                    TriggerVictoryBadge(match.PlayerWon);
                }
                ApplyWinnerEmphasis(match, match.PlayerWon);

                if (!s_SettledThisMatch)
                {
                    s_SettledThisMatch = true;
                    SettlePlayerProfileAndAwards(match);
                }
                UpdatePostRollVisuals(match);
            }
        }

        /// <summary>
        /// 读数与拔河条同步：百分比按实时值显示；条宽按「A : B 占比 × 掷点进度」从两端向中缝生长，
        /// 白色分界线预告占比落点（A/(A+B)），掷点结束两段正好在中缝汇合。
        /// </summary>
        private void UpdateCoverReadout(float a, float b, float eased)
        {
            if (_percentTmpA != null) _percentTmpA.text = $"{a:F1}%";
            if (_percentTmpB != null) _percentTmpB.text = $"{b:F1}%";

            if (_coverInnerW <= 0f) return;

            float sum = a + b;
            float shareA = sum > 0.001f ? a / sum : 0.5f;

            if (_coverFillA != null)
            {
                _coverFillA.rectTransform.sizeDelta = new Vector2(_coverInnerW * shareA * eased, -14f);
            }

            if (_coverFillB != null)
            {
                _coverFillB.rectTransform.sizeDelta = new Vector2(_coverInnerW * (1f - shareA) * eased, -14f);
            }

            if (_coverDivider != null)
            {
                _coverDivider.gameObject.SetActive(eased > 0.01f);
                _coverDivider.anchoredPosition = new Vector2(-_coverInnerW * 0.5f + _coverInnerW * shareA, 0f);
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
                // 台账明细按本局统计重建（对象数 ≤ 8×5；每局一次，不在逐帧路径上）。
                if (!_rosterBuilt) RebuildRosterRows(UIThemeSO.Resolve(uiTheme));

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
