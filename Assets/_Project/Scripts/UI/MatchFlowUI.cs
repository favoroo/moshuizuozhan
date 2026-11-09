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
    /// 2. 流程横幅系统（MatchBannerView）：开赛墨幕撕开后 GO、1:00 决战与 FINISH 终局定格（不再有 3-2-1 数字倒数）；
    /// 3. 结算界面（ResultPanel · 简化版「大数字即设计」）：
    ///    - 顶部 Hero 级「胜利 / 失败」判定大牌（移动端 ≥120），一句话讲完胜负；
    ///    - 中部两块墨色读数板（我方 / 敌方）：队色描边 + 超大百分比，胜方点亮「胜」签；
    ///    - 一张个人战报卡：涂地 / 击杀 / 阵亡三列大数字 + 等级 / 经验条 / 本局经验；
    ///    - 关键 CTA「再来一局」黄底墨字直角大按钮 + 次级「返回」白底墨字直角按钮；
    ///    4v4 明细表、勋章 chips、胜差贴纸与背景装饰全部退场，只留「谁赢 / 赢多少 / 我这局干了什么」。
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

        /// <summary>
        /// 右下角「检查更新」小标签的文案（t-1 做法：点一下依次变
        /// 检查更新中… / 新版 vX! / 已是最新版 / 检查失败）。
        /// </summary>
        private TextMeshProUGUI _updateTagLabel;
        private Coroutine _updateTagResetRoutine;

        /// <summary>无结果类状态（已是最新版 / 检查失败）停留多久后自己变回「检查更新」。</summary>
        private const float UpdateTagResetSeconds = 2.5f;

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

        /// <summary>读数板底色 #1E1A16：深墨中立色，靠 3px 队色描边识别阵营。</summary>
        public static readonly Color PlateInk = ComicStyle.FromHex(0x1E1A16);

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

        // 首页几何连续稳定的帧数；启动动画（LaunchIntroView）据此决定何时擦除露出首页，
        // 避免把「首帧脏几何排出的错版首页」直接暴露给玩家。项目开了 DisableDomainReload，
        // 静态字段跨 Play 残留，Awake 里显式归零。
        private static int s_homeGeometryStableFrames;

        /// <summary>首页几何是否已连续 ≥2 帧不变；为 false 时启动动画应按住擦除不露出首页。</summary>
        public static bool HasStableHomeGeometry => s_homeGeometryStableFrames >= 2;
        private TextMeshProUGUI _homeSettingsInfo;
        private TextMeshProUGUI _homeRecordInfo;
        private TextMeshProUGUI _homeMapInfo;
        // 衣柜卡（下一级解锁文案）与成就卡（已解锁数）的读数行，回首页时随等级/成就刷新。
        private TextMeshProUGUI _homeLockerInfo;
        private TextMeshProUGUI _homeAchieveInfo;
        // 衣柜卡右上角「NEW」角标：只有「上次打开衣柜之后又解锁了新外观」时才亮。
        private RectTransform _homeLockerPip;

        // 首页 Hero 卡头部的 Bot 难度四档段选（新手/轻松/普通/硬核），选择结果持久化到 GameSettingsService。
        private readonly UIInkShapeGraphic[] _difficultyShapes =
            new UIInkShapeGraphic[MenuLayoutMath.DifficultyTierCount];
        private readonly TextMeshProUGUI[] _difficultyTexts =
            new TextMeshProUGUI[MenuLayoutMath.DifficultyTierCount];

        // 首页 Hero 卡第二行的比赛时长段选（2分/3分/5分），选择结果持久化到 GameSettingsService。
        private readonly UIInkShapeGraphic[] _durationShapes =
            new UIInkShapeGraphic[MenuLayoutMath.MatchDurationOptionCount];
        private readonly TextMeshProUGUI[] _durationTexts =
            new TextMeshProUGUI[MenuLayoutMath.MatchDurationOptionCount];

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

        // 结算 → 首页的退场转场：结算页整页淡出（_resultExitGroup）后再亮出首页，
        // 替代 ApplyPhase 里「resultPanel 一帧硬切掉」的观感断层。
        // _resultExitActive 防重入——ApplyPhase 每帧都会跑，没有标记会反复重置 alpha 从 1 重播，
        // 淡出永远走不完（表现为结算页卡住不消失）。
        private CanvasGroup _resultExitGroup;
        private bool _resultExitActive;
        private float _resultExitElapsed;

        // 结算退场期间，「结算揭示层」（战报卡 / 成长条 / 胜签 / 勋章条…）的收起要延后到淡出收敛时，
        // 否则它们会在淡出开始前抢先消失，只剩基础页面在淡，观感像半张页面硬切。
        private bool _hideResultRevealAfterExit;

        // 结算列纵向拟合缓存：记录上次拟合时的画布高与位移量，画布尺寸变化时按差量重移。
        private float _resultFitCanvasH = -1f;
        private float _resultFitShift;
        private MatchManager.Phase _lastPhase = (MatchManager.Phase)(-1);
        private MatchManager.Phase _phaseBeforeTransition = (MatchManager.Phase)(-1);
        private bool _numbersSettled;

        [Header("结算个人战报与成长（简化版）")]
        private RectTransform _reportPanelRoot;
        private bool _winnerEmphasisApplied;

        // 简化结算页：两块墨色读数板 + 一张个人战报卡（涂地 / 击杀 / 阵亡 + 等级 / 经验）。
        private RectTransform _coverPlateA;
        private RectTransform _coverPlateB;
        private TextMeshProUGUI _percentTmpA;
        private TextMeshProUGUI _percentTmpB;
        private RectTransform _winMarkA;
        private RectTransform _winMarkB;
        private Color _teamColorA = Color.white;
        private Color _teamColorB = Color.white;

        private TextMeshProUGUI _statTurfText;
        private TextMeshProUGUI _statKillText;
        private TextMeshProUGUI _statDeathText;
        private TextMeshProUGUI _statStreakText;
        private CombatantMatchStats _localStats;

        // 新纪录角签（击杀 / 阵亡后扩展的列共用一套）：与四列读数一一对应，结算出纪录才点亮。
        private RectTransform[] _recordTagRoots = new RectTransform[4];
        private static readonly string[] RecordTagLabels = { "纪录!", "纪录!", "纪录!", "纪录!" };

        // 勋章条：金银铜胶囊 chips（最多 4 枚）+ 首枚勋章幽默描述行（溢出并入该行）。
        private RectTransform[] _medalChipRoots = new RectTransform[4];
        private TextMeshProUGUI[] _medalChipTexts = new TextMeshProUGUI[4];
        private TextMeshProUGUI _medalDescText;

        private ResultAchievementsView _achievementsView;
        private AchievementSettleOutcome _achievementOutcome;
        private AwardType _localAwards;

        private TextMeshProUGUI _xpLevelText;
        private TextMeshProUGUI _xpGainText;
        private Image _xpBarFill;
        private RectTransform _levelUpBadgeRoot;
        private TextMeshProUGUI _levelUpText;
        private UISpringMotion _levelUpMotion;

        // 结算「解锁」页签：与升级页签同款胶囊（奖励金），骑在卡片右上边框上。
        // 本局新解锁的外观在结算构建时算一次存进 _unlockCosmetics，动画阶段只负责显示，
        // 避免每帧遍历外观目录。
        private RectTransform _unlockBadgeRoot;
        private TextMeshProUGUI _unlockText;
        private UISpringMotion _unlockMotion;
        private readonly List<CosmeticDef> _unlockCosmetics = new List<CosmeticDef>(4);
        private bool _unlockBadgeShown;
        // 页签的设计宽与「不与左侧升级页签相撞」的可用上限：文案变长时只放大到上限。
        private float _unlockBadgeBaseWidth;
        private float _unlockBadgeMaxWidth;

        private ProfileMatchResult _profileResult;
        private bool _profileSettled;
        private int _lastLevelUpStep;

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
            s_homeGeometryStableFrames = 0;
            EnsureTheme();
            EnsureFullscreenPaperUnderlay();
            EnsureSafeAreaRoot();
            EnsureBannerView();
            EnsureResultVisuals();
            EnsureUpdateUI();
            // 首次构建不播错峰入场：启动层（LaunchIntroView 纯黑遮罩）会盖住前几帧，
            // 揭开时必须是完整首页，否则会看到卡片还在逐张淡入/弹入。
            EnsureReadyVisuals(false);

            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (restartButton != null) restartButton.onClick.AddListener(OnStartClicked);

            // 设置页改音量/震屏时同步首页设置卡读数（事件由 GameSettingsService 静态广播）
            GameSettingsService.SettingsChanged += RefreshHomeInfo;
            GameSettingsService.BotDifficultyChanged += HandleBotDifficultyChanged;
            GameSettingsService.MatchDurationChanged += HandleMatchDurationChanged;

            // 右下角检查更新标签跟随检查结果换文案（更新弹窗仍由 UpdateManager 自己弹）
            UpdateManager updater = UpdateManager.Instance;
            if (updater != null)
            {
                updater.OnUpdateAvailable += HandleUpdateAvailableForTag;
                updater.OnNoUpdateFound += HandleNoUpdateFoundForTag;
                updater.OnCheckFailed += HandleCheckFailedForTag;
            }
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
        /// 3. 右侧 2×3 次级卡：纸白底 + 3px 墨框 + 面板硬影 (4,-5) + 顶部 5px 身份色带
        ///    + 左上角签 + 一行 `·` 密集信息 + `》` 行进箭头（只有可点卡才放箭头）；
        /// 4. 左下 / 右下黑底角签（社区位 / 版本号）落在卡片区之外，不抢主角；
        /// 5. 内容全部放进 SafeArea，入场按 0.05s/张错峰 pop-in（0.22s Back-Out）。
        /// 所有盒子来自 <see cref="MenuLayoutMath.Home"/>，纯数据、EditMode 可断言。
        /// </summary>
        /// <param name="animateEntrance">
        /// 首次构建（Awake）传 true 播错峰入场；几何重建传 false——重建发生在安全区 settle /
        /// 旋转瞬间，重播入场会先整树透明一帧再逐张弹回，真机上读作「首页闪一下」。
        /// </param>
        private void EnsureReadyVisuals(bool animateEntrance = true)
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
            RetireLegacyTopLayoutButton();

            // 4) 卡片：Hero → 2×3 → 角签（入场顺序 = 构建顺序）
            BuildHomeHero(theme, m, mobile);
            BuildHomeTiles(theme, m, mobile);
            BuildHomeCornerTags(theme, m, mobile);

            // 5) 旧场景节点退场：标题 / 副标题 / 提示 / 版本号都由新卡与角签承担
            if (titleText != null) titleText.gameObject.SetActive(false);
            if (subtitleText != null) subtitleText.gameObject.SetActive(false);
            if (versionText != null) versionText.gameObject.SetActive(false);
            Transform legacyHint = FindMenuNode("StartHint");
            if (legacyHint != null) legacyHint.gameObject.SetActive(false);

            if (animateEntrance) PlayHomeEntrance();
            else SnapHomeEntrance();
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
        /// 只销毁本脚本生成的节点；场景遗留按钮（开赛 / 检查更新）原样保留，
        /// 由 EnsureReadyVisuals 重新收编与摆位。
        /// </summary>
        private void RebuildHomeVisuals()
        {
            if (readyPanel == null || _menuSafeArea == null) return;

            // 首页重建会销毁并重建整棵卡片树：先收掉详情浮层，避免它挂在旧几何上。
            HomeInfoOverlay.CloseIfOpen();
            LockerOverlay.CloseIfOpen();

            // 场景遗留按钮（开赛 / 检查更新）是 readyPanel 的持久节点，
            // 重建时必须先搬回 readyPanel 再销毁旧安全区，否则会跟着 MenuSafeArea 一起被 Destroy：
            // StartButton 的 onClick 只在 Awake 绑一次，一旦销毁 startButton 变 null，
            // PlaceHomeCta 直接 return —— 真机表现就是「首页开始游戏按钮凭空消失、整局开不了」。
            // （检查更新按钮在首页里被摆成右下角小标签，同样得先搬出再重建。）
            ReparentInto(startButton != null ? startButton.transform : null, readyPanel.transform);
            ReparentInto(checkUpdateButton != null ? checkUpdateButton.transform : null, readyPanel.transform);
            RetireLegacyTopLayoutButton();

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
                // HomeFooterTag 是已下线的「游戏共创 · 单机 4V4」角签：保留清理分支，
                // 让旧场景 / 旧运行时缓存里残留的那颗节点在重建时被一并收掉，不会挂在安全区当孤儿。
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
            EnsureReadyVisuals(false);
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

        /// <summary>稀疏星点：正圆点（圆角化）；用归一化锚点，任何宽高比都不变形。</summary>
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

                var shape = go.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.Pill; // 圆角化：星点改正圆点，不再旋转方块
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
            bg.Shape = UIInkShape.RoundedRect; // 圆角化：Hero 大卡自适应圆角，硬影同形状跟随
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

            // 1. 顶部 Header：左上「PLAY」硬角签 + 模式微标（不再挤塞难度/时长，清爽通透）
            CreateStageTag(card, "HeroTag", "PLAY", m.TagSize, m.TagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, m.HeroPadding, TextAlignmentOptions.Center);

            float badgeFont = UITypography.FontSnapped(mobile ? 22f : 20f);
            float badgeX = m.HeroPadding.x + m.TagSize.x + 16f;
            float badgeWidth = innerWidth - m.TagSize.x - 16f;
            if (badgeWidth >= 96f)
            {
                CreateBoardText(card, "HeroModeBadge", mobile ? "4V4 · 涂地模式" : "4V4 · 涂地对战 (TURF WAR)", badgeFont, true,
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                    new Vector2(badgeX, m.HeroPadding.y + (m.TagSize.y - badgeFont * 1.4f) * 0.5f),
                    new Vector2(badgeWidth, badgeFont * 1.4f),
                    TextAlignmentOptions.MidlineLeft, 2f, FontStyles.Bold);
            }

            // 2. 流式排版：从上到下 Header → 武器整备卡 → 难度行 → 时长行 → CTA
            // 武器整备卡由 WeaponSelectView 对齐 HeroWeaponSlot 渲染，高度与位置由 ResolveHeroFlow 统一收口。
            HeroFlowLayout flow = MenuLayoutMath.ResolveHeroFlow(m, _homeBoard.Height, mobile);
            BuildHeroDifficulty(card, theme, m, innerWidth, flow.DifficultyTop);
            BuildHeroDuration(card, theme, m, innerWidth, flow.DurationTop);

            // 3. 桌面端操作提示微字：在时长行底与 CTA 之间居中偏下，舒适呼应
            if (!mobile)
            {
                float hintFont = UITypography.FontSnapped(18f);
                float durBottom = flow.DurationTop + flow.DurationHeight;
                float hintFree = flow.CtaTop - durBottom;
                if (hintFree >= hintFont * 1.6f + 8f)
                {
                    float hintY = durBottom + (hintFree - hintFont * 1.6f) * 0.65f;
                    CreateBoardText(card, "HeroKeyHint", "SPACE / ENTER TO BATTLE", hintFont, true,
                        ComicStyle.WithAlpha(ComicStyle.Ink, 0.40f),
                        new Vector2(m.HeroPadding.x, hintY),
                        new Vector2(innerWidth, hintFont * 1.6f),
                        TextAlignmentOptions.Center, 2f, FontStyles.Bold);
                }
            }

            // 4. 主 CTA：复用场景里的 StartButton（保持 MenuSafeArea 直接子级）
            if (startButton != null) PlaceHomeCta(theme, m, mobile);
        }

        /// <summary>
        /// Hero 卡中部对局控制区的 Bot 难度四档段选（新手 / 轻松 / 普通 / 硬核）。
        /// 位置：位于武器整备卡正下方，与时长行同宽同右缘对齐铺满 innerWidth。
        /// 点击只改 <see cref="GameSettingsService.BotDifficulty"/>，Bot 通过事件即时重建缩放配置。
        /// </summary>
        private void BuildHeroDifficulty(RectTransform card, UIThemeSO theme, HomeMenuMetrics m,
            float innerWidth, float rowY)
        {
            DifficultyBarLayout layout = MenuLayoutMath.ResolveHeroDifficulty(m, innerWidth);
            float segHeight = m.DifficultySegmentSize.y;

            var rowGo = new GameObject("HeroDifficultyRow", typeof(RectTransform));
            rowGo.transform.SetParent(card, false);
            var row = (RectTransform)rowGo.transform;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(m.HeroPadding.x, -rowY);
            row.sizeDelta = new Vector2(layout.TotalWidth, segHeight);

            // 左侧「难度」黑角签：与 PLAY 同语言的黑底白字硬角签，语义清晰
            CreateStageTag(row, "HeroDifficultyTag", "难度",
                new Vector2(layout.TagWidth, segHeight), m.DifficultyTagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, Vector2.zero, TextAlignmentOptions.Center,
                layout.TagWidth);

            float fontSize = UITypography.FontSnapped(m.DifficultyFont);
            float x = layout.TagWidth + layout.TagGap;
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
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：难度段选自适应圆角
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

        /// <summary>
        /// Hero 卡中部对局控制区的比赛时长段选（2分 / 3分 / 5分）：
        /// 黑色「时长」角签 + 3 段纸白药丸，与难度行严格对齐铺满 innerWidth。
        /// 点击只改 <see cref="GameSettingsService.MatchDurationMinutes"/>，下一局开局时生效。
        /// </summary>
        private void BuildHeroDuration(RectTransform card, UIThemeSO theme, HomeMenuMetrics m,
            float innerWidth, float rowY)
        {
            MatchDurationBarLayout layout = MenuLayoutMath.ResolveHeroDuration(m, innerWidth);
            float segHeight = m.DurationSegmentSize.y;

            var rowGo = new GameObject("HeroDurationRow", typeof(RectTransform));
            rowGo.transform.SetParent(card, false);
            var row = (RectTransform)rowGo.transform;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(m.HeroPadding.x, -rowY);
            row.sizeDelta = new Vector2(layout.TotalWidth, segHeight);

            // 「时长」角签：与 PLAY 同语言的黑底白字硬角签，给第二行一个明确语义锚点。
            CreateStageTag(row, "HeroDurationTag", "时长",
                new Vector2(layout.TagWidth, segHeight), m.DurationTagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, Vector2.zero, TextAlignmentOptions.Center,
                layout.TagWidth);

            float fontSize = UITypography.FontSnapped(m.DurationFont);
            float x = layout.TagWidth + layout.TagGap;
            int[] options = GameSettingsMath.AvailableMatchDurationMinutes;
            for (int i = 0; i < options.Length && i < _durationShapes.Length; i++)
            {
                int capturedMinutes = options[i];
                string label = GameSettingsMath.GetMatchDurationLabel(capturedMinutes);

                Button button = CreateDifficultySegment(row, "Duration_" + capturedMinutes, label,
                    layout.SegmentWidth, segHeight, theme, fontSize);
                if (button == null) continue;

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(x, 0f);

                _durationShapes[i] = button.GetComponent<UIInkShapeGraphic>();
                _durationTexts[i] = button.GetComponentInChildren<TextMeshProUGUI>();

                button.onClick.AddListener(() => OnMatchDurationSelected(capturedMinutes));
                x += layout.SegmentWidth + layout.Gap;
            }

            RefreshMatchDurationButtons();
        }

        private void OnMatchDurationSelected(int minutes)
        {
            GameSettingsService.MatchDurationMinutes = minutes;
            RefreshMatchDurationButtons();
        }

        private void HandleMatchDurationChanged(int minutes)
        {
            RefreshMatchDurationButtons();
        }

        /// <summary>时长选中态与难度段选一致：墨黑实底 + 金黄字；未选中纸白 + 墨字。</summary>
        private void RefreshMatchDurationButtons()
        {
            int current = GameSettingsService.MatchDurationMinutes;
            int[] options = GameSettingsMath.AvailableMatchDurationMinutes;
            for (int i = 0; i < _durationShapes.Length; i++)
            {
                UIInkShapeGraphic shape = _durationShapes[i];
                if (shape == null) continue;

                bool selected = i < options.Length && options[i] == current;
                shape.color = selected ? ComicStyle.Ink : ComicStyle.Paper;
                shape.BorderColor = ComicStyle.Ink;
                shape.Refresh();

                TextMeshProUGUI text = _durationTexts[i];
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

        /// <summary>右侧 2×3 次级卡：战绩 / 衣柜 / 成就 / 地图选择 / 系统设置 / 修改按钮位置。</summary>
        private void BuildHomeTiles(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            // 每次重建都是新节点，旧的 pip 引用随旧树一起销毁，必须先置空再按需点亮。
            _homeLockerPip = null;
            // 首次进首页时给「已看过水位」落基线（否则新玩家一开局就挂 NEW）。
            EnsureLockerSeenBaseline();

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

            // 衣柜：黄色 = 奖励语义（与主行动黄同源），信息行直接回答「再升一级给什么」。
            BuildHomeTile(new HomeTileSpec
            {
                Index = 1,
                Tag = "LOOK",
                Title = "衣柜",
                Info = BuildLockerInfo(),
                Accent = ComicStyle.Yellow,
                Interactive = true,
                ShowNewPip = true,
                ClickAction = OpenLockerFromHome,
            }, theme, m, mobile, out _homeLockerInfo, out _);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 2,
                Tag = "ACHIEVE",
                Title = "成就",
                Info = BuildAchievementInfo(),
                Accent = ComicStyle.Green,
                Interactive = true,
                ClickAction = () => ShowHomeInfo(HomeInfoTab.Achievements),
            }, theme, m, mobile, out _homeAchieveInfo, out _);

            MapDefinition currentMap = MapCatalogService.Selected;
            MapDefinition otherMap = MapCatalogService.OtherThan(currentMap);
            BuildHomeTile(new HomeTileSpec
            {
                Index = 3,
                Tag = "MAP",
                Title = currentMap.DisplayName,
                Info = BuildMapInfo(currentMap, otherMap),
                Accent = currentMap.Accent,
                Interactive = true,
                MapPreview = currentMap,
                ClickAction = OpenMapSelect,
            }, theme, m, mobile, out _homeMapInfo, out _);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 4,
                Tag = "SETTINGS",
                Title = "系统设置",
                Info = BuildSettingsInfo(),
                Accent = theme.teamAColor,
                Interactive = true,
                ClickAction = OpenHomeSettings,
            }, theme, m, mobile, out _homeSettingsInfo, out _);

            BuildHomeTile(new HomeTileSpec
            {
                Index = 5,
                Tag = "LAYOUT",
                // 标题只放 4 个字：移动端字号 ×1.28 后 6 个字的标题会顶进 `》` 走廊（可用宽 254px）
                Title = "按键位置",
                Info = mobile ? "拖动调整触屏按键位置" : "拖动调整按键位置 · 自定义布局",
                Accent = ComicStyle.Yellow,
                Interactive = true,
                ClickAction = OpenLayoutCustomizerFromHome,
            }, theme, m, mobile, out _, out _);
        }

        /// <summary>
        /// 衣柜卡信息行：「下一级解锁：薄荷汽水」/「外观已全部解锁」。
        /// 判定是纯函数（CosmeticUnlockMath），这里只负责兜异常，装饰失败不能拖垮首页。
        /// </summary>
        private static string BuildLockerInfo()
        {
            try
            {
                PlayerProfile profile = PlayerProfile.Current;
                // 档案为空时 NextUnlockText 会误报「外观已全部解锁」，这里先拦一道给中性文案。
                if (profile == null) return "外观 · 点击查看衣柜";
                return CosmeticUnlockMath.NextUnlockText(profile);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 衣柜卡读取外观解锁失败：" + ex.Message);
            }
            return "外观 · 点击查看衣柜";
        }

        /// <summary>成就卡信息行：已解锁成就数 / 总数（移动端省掉尾注，避免挤爆信息行）。</summary>
        private static string BuildAchievementInfo()
        {
            try
            {
                PlayerProfile profile = PlayerProfile.Current;
                if (profile != null)
                {
                    int unlocked = profile.UnlockedAchievements.Count;
                    int total = AchievementCatalog.TotalCount;
                    return UITypography.IsMobileLike
                        ? $"成就 {unlocked}/{total}"
                        : $"已解锁成就 {unlocked}/{total} · 点击查看";
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 成就卡读取档案失败：" + ex.Message);
            }
            return "已解锁成就 0/0";
        }

        private static string BuildMapInfo(MapDefinition current, MapDefinition other)
        {
            return other == null ? "当前地图" : $"当前 · 还可选「{other.DisplayName}」";
        }

        /// <summary>
        /// 首页地图卡：打开地图选择浮层。浮层里切换只改存档（不再立刻重载场景），
        /// 关窗后若选择变了就重建首页地图卡，让标题/预览/信息立刻跟着选中项走；
        /// 真正的场景载入发生在「开始游戏」。
        /// </summary>
        private void OpenMapSelect()
        {
            if (readyPanel == null) return;

            string selectedBefore = MapCatalogService.Selected.Id;
            MapSelectOverlay.Show(readyPanel.transform as RectTransform, UIThemeSO.Resolve(uiTheme), () =>
            {
                if (MapCatalogService.Selected.Id == selectedBefore) return;
                if (isActiveAndEnabled) RebuildHomeVisuals();
            });
        }

        /// <summary>
        /// 首页「按键位置」卡：打开移动端触屏布局自定义器（首页唯一入口，局内走暂停菜单）。
        /// 桌面端没有常驻触屏 UI，自定义器会按需把触屏画布显示出来供预览与拖动。
        /// </summary>
        private void OpenLayoutCustomizerFromHome()
        {
            var touchUI = FindAnyObjectByType<MobileTouchUI>();
            if (touchUI == null)
            {
                Debug.LogWarning("[MatchFlowUI] 场景里没有 MobileTouchUI，无法打开按钮位置自定义");
                return;
            }
            touchUI.OpenLayoutCustomizer();
        }

        private struct HomeTileSpec
        {
            public int Index;
            public string Tag;
            public string Title;
            public string Info;
            public Color Accent;
            public bool Interactive;
            public MapDefinition MapPreview;
            public System.Action ClickAction;
            /// <summary>
            /// 该卡是否预留右上角「NEW」小角标槽位（当前只有衣柜卡）。节点常驻、显隐由
            /// 「已看过水位」决定，这样结算解锁后回首页只切显隐、不必重建整棵卡片树。
            /// </summary>
            public bool ShowNewPip;
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
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：次级卡自适应圆角，硬影同形状跟随
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

            // 顶部 5px 身份色带（规范：色带不承载文字）。
            // 圆角化：卡片顶角是圆的，色带横向内缩到圆角直线段内并改胶囊短划。
            float tileRadius = ComicStyle.CornerFor(tileSize.x, tileSize.y);
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(root, false);
            var bandRect = (RectTransform)bandGo.transform;
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = new Vector2(1f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.sizeDelta = new Vector2(-(tileRadius + m.TileBorderWidth) * 2f, m.TileBandHeight);
            bandRect.anchoredPosition = new Vector2(0f, -m.TileBorderWidth);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.Pill;
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

            float titleSize = UITypography.FontSnapped(m.TileTitleFont);
            float infoSize = UITypography.FontSnapped(m.TileInfoFont);
            float infoHeight = infoSize * 1.7f;

            // 2×3 后卡片明显变矮（参考桌面 396→256，20:9 真机仅 ≈197）。竖向预算按
            // 「上下内边距 + 角签 + 呼吸位 + 标题 + 信息行 + 4px 行间缝」分配，超支时依次让位：
            // 先收内边距（24/22 → 最低 12），再收角签与标题之间的呼吸位（20/18 → 最低 8），
            // 最后才降标题字号（且不低于 20）。标题是卡片的主读数，内边距只是留白，先牺牲留白。
            float padY = m.TilePadding.y;
            float titleGap = mobile ? 20f : 18f;
            float titleHeight = titleSize * 1.25f;
            const float titleInfoGap = 4f; // 标题行与信息行的最小缝：贴着排会读成一坨

            float fixedOther = m.TileTagSize.y + infoHeight + titleInfoGap;
            float budget = contentHeight - fixedOther;
            if (padY * 2f + titleGap + titleHeight > budget)
            {
                // 先收内边距（最多收到 12），把省下的空间还给标题。
                float overflow = padY * 2f + titleGap + titleHeight - budget;
                float padFloor = 12f;
                float padCut = Mathf.Min(overflow, Mathf.Max(0f, padY - padFloor) * 2f);
                padY -= padCut * 0.5f;
                overflow -= padCut;
                if (overflow > 0f)
                {
                    // 再收呼吸位（最低 8）。
                    float gapCut = Mathf.Min(overflow, Mathf.Max(0f, titleGap - 8f));
                    titleGap -= gapCut;
                    overflow -= gapCut;
                }
                if (overflow > 0f)
                {
                    // 最后降标题字号：只降不升，版面表的值是上限；底线 20（正文可读下限）。
                    float maxTitle = Mathf.Floor((budget - padY * 2f - titleGap) / 1.25f);
                    float fitted = Mathf.Max(20f, Mathf.Floor(maxTitle * 0.5f) * 2f);
                    if (fitted < titleSize) titleSize = fitted;
                    titleHeight = titleSize * 1.25f;
                }
            }

            // 角签宽度按标签文本自然宽度自适应（SETTINGS 比 RECORD 宽得多，固定 140px 会画出黑底）。
            CreateStageTag(content, "TileTag", spec.Tag, m.TileTagSize, m.TileTagFont, theme,
                ComicStyle.Ink, ComicStyle.Paper, new Vector2(m.TilePadding.x, padY),
                TextAlignmentOptions.Center, rowWidth);

            // `》` 独占标题行右侧走廊：双书名号实际约 2em 宽（旧值按 1.2em 预留），
            // 且不能再和信息行共行——移动端字号 ×1.28 后短信息也会顶到箭头下面。
            // 标题字号被压小时箭头跟着收，否则箭头会比标题行还高、下沿伸进信息行。
            float arrowSize = Mathf.Min(UITypography.FontSnapped(m.TileArrowFont), titleSize);
            float arrowHeight = arrowSize * 1.1f;
            float arrowGutter = spec.Interactive ? arrowSize * 2.1f + 12f : 0f;

            float infoTop = contentHeight - padY - infoHeight;
            float titleTop = padY + m.TileTagSize.y + titleGap;

            // 兜底：预算再紧也不许标题压进信息行（降字号失败时的最后一道闸，只压位置不截字）。
            float maxTitleTop = infoTop - titleInfoGap - titleHeight;
            if (titleTop > maxTitleTop) titleTop = Mathf.Max(padY + m.TileTagSize.y, maxTitleTop);

            titleText = CreateBoardText(content, "TileTitle", spec.Title, titleSize, true, ComicStyle.Ink,
                new Vector2(m.TilePadding.x, titleTop),
                new Vector2(rowWidth - arrowGutter, titleHeight),
                TextAlignmentOptions.MidlineLeft, -2f, FontStyles.Bold);

            infoText = CreateBoardText(content, "TileInfo", spec.Info, infoSize, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                new Vector2(m.TilePadding.x, infoTop),
                new Vector2(rowWidth, infoHeight),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            // 地图卡的小预览：2×3 后行高只剩约 190~260（20:9 真机 ≈197），标题与信息行之间
            // 通常只剩 40px 左右（旧 2×2 有 100+）。所以先试中间那条空白带，放不下就退回
            // 「角签行右侧」——宁可画成小缩略图，也不让预览在真机上凭空消失。
            if (spec.MapPreview != null)
            {
                float previewTop = titleTop + titleHeight + (mobile ? 4f : 8f);
                float band = infoTop - previewTop - (mobile ? 2f : 4f);
                float previewHeight;
                float previewY;
                if (band >= 40f)
                {
                    previewHeight = band;
                    previewY = previewTop;
                }
                else
                {
                    previewHeight = m.TileTagSize.y;
                    previewY = padY;
                }

                float previewWidth = previewHeight * 40f / 56f;
                float maxPreviewWidth = rowWidth * 0.3f;
                if (previewWidth > maxPreviewWidth && maxPreviewWidth > 0f)
                {
                    previewWidth = maxPreviewWidth;
                    previewHeight = previewWidth * 56f / 40f;
                }

                if (previewHeight >= 32f && previewWidth >= 24f)
                {
                    RectTransform preview = MapPreviewBuilder.BuildPreview(
                        content, spec.MapPreview, new Vector2(previewWidth, previewHeight), theme, "TileMapPreview");
                    preview.anchorMin = preview.anchorMax = new Vector2(1f, 1f);
                    preview.pivot = new Vector2(1f, 1f);
                    preview.anchoredPosition = new Vector2(-m.TilePadding.x, -previewY);
                }
            }

            if (spec.Interactive)
            {
                // `》` 行进箭头：只有可点卡才放（信息卡不放箭头，避免"假可点"）
                CreateBoardText(content, "TileArrow", "》", arrowSize, true, ComicStyle.Ink,
                    new Vector2(m.TilePadding.x + rowWidth - arrowGutter, titleTop + (titleHeight - arrowHeight) * 0.5f),
                    new Vector2(arrowGutter, arrowHeight),
                    TextAlignmentOptions.MidlineRight, 0f, FontStyles.Bold);

                AttachTileInteraction(go, shape, content, spec.ClickAction);
            }

            // 右上角「NEW」角标：与左上角签同一行（不占标题/信息行），
            // 复用 CreateStageTag 的胶囊 + Tag 硬影，建好后改成右上锚点。
            // 节点常驻、显隐由「已看过水位」决定——结算解锁后回首页只刷新显隐，
            // 不重建整棵树也能让角标亮起来。
            if (spec.ShowNewPip)
            {
                // 宽度按「NEW」三个字符的自然宽 + 20px 内缩给足：角签只放大不缩小，
                // 固定半宽（59/70）在移动端 26 号字下会溢字。
                float pipWidth = Mathf.Ceil(UITypography.FontSnapped(m.TileTagFont) * 2.1f) + 20f;
                TextMeshProUGUI pip = CreateStageTag(content, "TileNewPip", "NEW",
                    new Vector2(pipWidth, m.TileTagSize.y), m.TileTagFont, theme,
                    ComicStyle.Yellow, ComicStyle.Ink, m.TilePadding, TextAlignmentOptions.Center, rowWidth);
                if (pip != null)
                {
                    var pipRect = (RectTransform)pip.transform.parent;
                    pipRect.anchorMin = new Vector2(1f, 1f);
                    pipRect.anchorMax = new Vector2(1f, 1f);
                    pipRect.pivot = new Vector2(1f, 1f);
                    pipRect.anchoredPosition = new Vector2(-m.TilePadding.x, -padY);
                    pipRect.gameObject.SetActive(HasUnseenCosmetics());
                    _homeLockerPip = pipRect;
                }
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

        /// <summary>整卡可点：可点次级卡各自带 clickAction，统一套一套按压物理。</summary>
        private void AttachTileInteraction(GameObject go, UIInkShapeGraphic shape, RectTransform content,
            System.Action clickAction)
        {
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
        /// 首页顶部右侧「自定义按键」小按钮已下线：入口与 2×3 卡阵的「按键位置」卡完全重复，
        /// 同一屏出现两颗一模一样的入口只会让人犹豫点哪个。旧场景/旧截图里遗留的节点一律关掉，
        /// 不再摆位、不再换肤（重开它请改回 <c>BuildHomeTiles</c> 的调用与度量）。
        /// </summary>
        private void RetireLegacyTopLayoutButton()
        {
            Transform legacy = FindMenuNode("CustomLayoutButton");
            if (legacy != null && legacy.gameObject.activeSelf) legacy.gameObject.SetActive(false);
        }

        /// <summary>
        /// 右下角签：可点「检查更新」小按钮，落在卡片区之外。
        ///
        /// 左下角的「游戏共创 · 单机 4V4」只读角签已下线：它只是把项目模式写在角落，
        /// 既不是可点入口、也不承载任何信息（模式/时长/难度都在 Hero 卡里），
        /// 却在一屏最显眼的位置占掉 320×36 的黑底条。留着只会让人误以为那里能点什么。
        /// 版面表里 <see cref="MenuLayoutMath.HomeMenuMetrics.FooterTag"/> 已置空（两平台都空），
        /// 这里不再建节点；要恢复请改回版面表 + 本方法。
        /// </summary>
        /// <param name="mobile">保留给旧调用签名；左下角签下线后本方法已无平台分支。</param>
        private void BuildHomeCornerTags(UIThemeSO theme, HomeMenuMetrics m, bool mobile)
        {
            // 右下角角签不再是只读版本号：改成 t-1 同款的「检查更新」小按钮
            // （版本号仍可在启动页与更新弹窗里看到）。
            // TapTap 上架变体（P1_OFFLINE）没有联网能力：这里退回只读版本号签，
            // 否则玩家点了一颗永远「检查失败」的按钮。
            if (UpdateManager.NetworkFeatureEnabled)
                AttachMenuButtonToUpdateTag(checkUpdateButton, m, theme);
            else
                CreateFooterTag("HomeVersionTag", m.VersionTag,
                    $"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}", theme,
                    ComicStyle.Ink, ComicStyle.Paper, m.FooterTagFont);
        }

        /// <summary>
        /// 右下角「检查更新」小标签（t-1 做法）：复用场景遗留按钮的节点（透明 Image 只负责射线），
        /// 视觉改成一颗黄底墨字直角小按钮，按压/悬停走 UIJellyButton 的硬影物理。
        /// 文案由 <see cref="SetUpdateTagText"/> 驱动：检查中 / 新版 / 已是最新 / 失败。
        /// </summary>
        private void AttachMenuButtonToUpdateTag(Button button, HomeMenuMetrics m, UIThemeSO theme)
        {
            if (button == null || _menuSafeArea == null) return;

            HomeBox box = m.VersionTag;
            if (box.IsEmpty) return;

            ReparentInto(button.transform, _menuSafeArea);
            var rect = button.transform as RectTransform;
            if (rect == null) return;

            // 与 CreateFooterTag 同一套收边位：右下角签锚右边，刘海屏安全区变窄也不会被推出屏幕。
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-(MenuLayoutMath.RefWidth - box.Right),
                MenuLayoutMath.RefHeight - box.Bottom);
            rect.sizeDelta = box.Size;

            var img = button.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
            }

            var legacyLabel = button.GetComponentInChildren<Text>(true);
            if (legacyLabel != null) legacyLabel.gameObject.SetActive(false);

            // 节点上已有 Image（透明射线用），不能再 AddComponent<UIInkShapeGraphic>（Graphic 带
            // DisallowMultipleComponent，会静默返回 null）——背景只能走 CreateChildBackground。
            var shape = button.GetComponentInChildren<UIInkShapeGraphic>(true);
            if (shape == null) shape = UIInkShapeGraphic.CreateChildBackground(rect, "GeometricBg");
            if (shape != null)
            {
                shape.Shape = UIInkShape.Pill; // 圆角化：检查更新小标签改胶囊
                shape.BorderWidth = ComicStyle.BorderThin;
                shape.BorderColor = ComicStyle.Ink;
                shape.color = ComicStyle.Yellow; // 主行动黄：角签里只有这颗可点
                shape.HardShadow = false;
                shape.Halftone = false;
                shape.GlossAlpha = 0f;
                shape.DrawGloss = false;
                shape.raycastTarget = false;
            }

            _updateTagLabel = CreateOrReplaceTmpLabel(rect, "LabelTmp", UpdateTagIdleText,
                UITypography.FontSnapped(m.FooterTagFont), false, ComicStyle.Ink, TextAlignmentOptions.Center);

            button.targetGraphic = shape != null ? shape : button.targetGraphic;
            button.transition = Selectable.Transition.None;

            if (shape != null)
            {
                var jelly = button.GetComponent<UIJellyButton>();
                if (jelly == null) jelly = button.gameObject.AddComponent<UIJellyButton>();
                jelly.Configure(theme, button, shape);
                if (_updateTagLabel != null) jelly.SetPressContent(_updateTagLabel.rectTransform);
                jelly.EnableThemeHardShadow(UIHardShadowLevel.Button);
            }

            button.gameObject.SetActive(true);
        }

        /// <summary>「检查更新」小标签的常态文案（未在检查、也没有待更新版本）。</summary>
        private const string UpdateTagIdleText = "检查更新";

        private void SetUpdateTagText(string text)
        {
            if (_updateTagLabel != null) _updateTagLabel.text = text;
        }

        /// <summary>把标签切到某个状态；autoReset = true 的状态会在 2.5 秒后自己变回「检查更新」。</summary>
        private void SetUpdateTagState(string text, bool autoReset)
        {
            SetUpdateTagText(text);

            if (_updateTagResetRoutine != null)
            {
                StopCoroutine(_updateTagResetRoutine);
                _updateTagResetRoutine = null;
            }
            if (autoReset && isActiveAndEnabled)
            {
                _updateTagResetRoutine = StartCoroutine(ResetUpdateTagAfterDelay());
            }
        }

        /// <summary>
        /// 用 unscaled 等待：更新弹窗可能在 Time.timeScale=0（暂停/结算）时出现，
        /// 走缩放时间会让标签永远停在「已是最新版」。
        /// </summary>
        private System.Collections.IEnumerator ResetUpdateTagAfterDelay()
        {
            yield return new WaitForSecondsRealtime(UpdateTagResetSeconds);
            _updateTagResetRoutine = null;
            SetUpdateTagText(UpdateTagIdleText);
        }

        private void HandleUpdateAvailableForTag(UpdateInfo info)
        {
            // 有新版本时标签不再自动复位：留着「新版 vX!」提示玩家，点它重新打开更新弹窗。
            SetUpdateTagState($"新版 {info?.tagName}!", autoReset: false);
        }

        private void HandleNoUpdateFoundForTag()
        {
            SetUpdateTagState("已是最新版", autoReset: true);
        }

        private void HandleCheckFailedForTag(string _)
        {
            SetUpdateTagState("检查失败", autoReset: true);
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
            shape.Shape = UIInkShape.Pill; // 圆角化：底部角签改胶囊
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
            shape.Shape = UIInkShape.Pill; // 圆角化：角签（PLAY / 角标）改胶囊
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
        /// 首页「衣柜」卡：打开外观衣柜浮层（独立 Overlay Canvas，层级 65，压过 HUD 与武器抽屉）。
        /// 打开即记下「当前已解锁件数」，下次回首页时 NEW 角标只在又有新解锁时才亮。
        /// </summary>
        private void OpenLockerFromHome()
        {
            if (readyPanel == null) return;

            // 先落「已看过」水位再开窗：即使衣柜构建失败，也不会每次回首页都亮角标。
            MarkCosmeticsSeen();
            // 角标立刻灭掉：玩家已经在看衣柜了，再挂一个 NEW 只会显得没响应。
            if (_homeLockerPip != null) _homeLockerPip.gameObject.SetActive(false);

            try
            {
                LockerOverlay.Show(readyPanel.transform as RectTransform, UIThemeSO.Resolve(uiTheme), 65);
            }
            catch (System.Exception ex)
            {
                // 装饰性入口失败不该让首页卡死（也避免 PlayMode 的 log-error 门禁）。
                Debug.LogWarning("[MatchFlowUI] 打开衣柜失败：" + ex.Message);
            }
        }

        /// <summary>衣柜「已看过」水位键：只存件数，不碰 PlayerProfile（并行改动面隔离）。</summary>
        private const string LockerSeenCountKey = "P1.Locker.SeenUnlockedCount";

        /// <summary>当前档案下已解锁的外观件数（异常时按 0 处理，只影响角标不影响衣柜本身）。</summary>
        private static int CurrentUnlockedCosmeticCount()
        {
            try
            {
                return CosmeticUnlockMath.UnlockedCount(PlayerProfile.Current);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 读取外观解锁数失败：" + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 衣柜卡是否该亮 NEW 角标：当前解锁件数 &gt; 上次打开衣柜时的水位。
        /// 用 PlayerPrefs 而不是 PlayerProfile，避免和并行修改的存档类冲突。
        /// </summary>
        private static bool HasUnseenCosmetics()
        {
            int seen = PlayerPrefs.GetInt(LockerSeenCountKey, -1);
            return seen >= 0 && CurrentUnlockedCosmeticCount() > seen;
        }

        /// <summary>
        /// 首次构建首页时给「已看过水位」落一个基线：不落的话默认值 0 会让新玩家一开局
        /// 就挂一个 NEW（指向的其实是他本来就有的默认外观）。只在键不存在时写一次。
        /// </summary>
        private static void EnsureLockerSeenBaseline()
        {
            if (PlayerPrefs.HasKey(LockerSeenCountKey)) return;
            PlayerPrefs.SetInt(LockerSeenCountKey, CurrentUnlockedCosmeticCount());
            PlayerPrefs.Save();
        }

        /// <summary>把「已看过」水位抬到当前解锁件数（只升不降：降级不会让玩家再看一遍旧解锁）。</summary>
        private static void MarkCosmeticsSeen()
        {
            int current = CurrentUnlockedCosmeticCount();
            if (current <= PlayerPrefs.GetInt(LockerSeenCountKey, -1)) return;
            PlayerPrefs.SetInt(LockerSeenCountKey, current);
            PlayerPrefs.Save();
        }

        /// <summary>首页读数（战绩 / 设置 / 当前地图）刷新：回到主菜单或设置项变化时数值会变。</summary>
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

            if (_homeMapInfo != null)
            {
                MapDefinition currentMap = MapCatalogService.Selected;
                _homeMapInfo.text = BuildMapInfo(currentMap, MapCatalogService.OtherThan(currentMap));
                FitTileInfoText(_homeMapInfo);
            }

            // 衣柜「下一级解锁」与成就计数都会随结算变化（升级 / 解锁成就），回首页必须重算。
            if (_homeLockerInfo != null)
            {
                _homeLockerInfo.text = BuildLockerInfo();
                FitTileInfoText(_homeLockerInfo);
            }

            if (_homeAchieveInfo != null)
            {
                _homeAchieveInfo.text = BuildAchievementInfo();
                FitTileInfoText(_homeAchieveInfo);
            }

            // 角标也按当前水位重算：本局结算解锁了新外观时，回首页立刻亮起来。
            if (_homeLockerPip != null)
            {
                _homeLockerPip.gameObject.SetActive(HasUnseenCosmetics());
            }
        }

        private void PlayHomeEntrance()
        {
            PlayStaggered(_homeEntrance);
        }

        /// <summary>
        /// 几何重建后的无缝落位：所有卡片直接吸附到常态（不透明、原尺寸），不重播错峰入场。
        /// 入场动画在延迟期间是「缩小 + 全透明」，重建时重播它会先整树消失一帧再逐张弹回
        /// ——真机安全区 settle 恰落在启动动画擦除之后时，玩家看到的就是「首页闪一下」。
        /// 吸附换装只留下一次干净的版式切换。
        /// </summary>
        private void SnapHomeEntrance()
        {
            for (int i = 0; i < _homeEntrance.Count; i++)
            {
                UISpringMotion motion = _homeEntrance[i];
                if (motion == null) continue;
                motion.SnapHome();
            }
        }

        /// <summary>
        /// 结算入场：判定牌 / 两块读数板 / 战报卡按阅读顺序同一批 pop-in（0.05s/张）。
        /// 掷点（覆盖率滚动）只负责点亮胜方，不再负责把元素「变出来」。
        /// </summary>
        private void PlayResultEntrance()
        {
            _resultEntrance.Clear();

            MatchManager match = MatchManager.Instance;

            // 判定牌内容必须提前写好：它现在一开场就可见，若沿用「掷点后才写胜利 / 失败」，
            // 输局会先亮 1.4s 的「胜利」再被改掉。这里只写内容，入场动画交给 PlayStaggered。
            if (victoryBadgeRoot != null)
            {
                PrepareVictoryBadge(match != null && match.PlayerWon);
                victoryBadgeRoot.gameObject.SetActive(true);
                if (victoryBadgeMotion != null) _resultEntrance.Add(victoryBadgeMotion);
            }

            // 读数板：入场前先归零，避免第二局沿用上一局终值闪一帧。
            UpdateCoverReadout(0f, 0f);
            if (_coverPlateA != null) _resultEntrance.Add(_coverPlateA.GetComponent<UISpringMotion>());
            if (_coverPlateB != null) _resultEntrance.Add(_coverPlateB.GetComponent<UISpringMotion>());

            // 战报卡：与判定牌同批出现，数值在入场前就写好（掷点结束只推进经验计数）。
            if (_reportPanelRoot != null)
            {
                _reportPanelRoot.gameObject.SetActive(true);
                PopulateReportCard(match);
                if (_reportMotion != null) _resultEntrance.Add(_reportMotion);
            }

            // 本局成就条：内容已在结算时写好；本局有新解锁才随同一批 pop-in。
            if (_achievementsView != null)
            {
                bool stripVisible = _achievementsView.Populate(
                    _achievementOutcome != null ? _achievementOutcome.NewlyUnlocked : null);
                if (stripVisible && _achievementsView.Motion != null)
                {
                    _resultEntrance.Add(_achievementsView.Motion);
                }
            }

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

            btnShape.Shape = UIInkShape.RoundedRect; // 圆角化：开赛 CTA 自适应圆角，硬影同形状跟随
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
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：装饰盒自适应圆角（旋转后为圆角菱形）
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
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：装饰方块 → 圆角菱形
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
            GameSettingsService.MatchDurationChanged -= HandleMatchDurationChanged;

            if (_updateTagResetRoutine != null)
            {
                StopCoroutine(_updateTagResetRoutine);
                _updateTagResetRoutine = null;
            }
            UpdateManager updater = UpdateManager.Instance;
            if (updater != null)
            {
                updater.OnUpdateAvailable -= HandleUpdateAvailableForTag;
                updater.OnNoUpdateFound -= HandleNoUpdateFoundForTag;
                updater.OnCheckFailed -= HandleCheckFailedForTag;
            }
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
        /// 结算页（简化版 · 几何大胆风）：
        /// 1. 顶部 Hero 级「胜利 / 失败」判定大牌，一句话讲完胜负；
        /// 2. 中部两块「墨色读数板」（我方 / 敌方）：队色描边 + 超大百分比，胜方点亮黄色「胜」签；
        /// 3. 底部一张个人战报卡 v2：涂地 / 击杀 / 阵亡 / 连杀四列 + 勋章 chips + 等级 / 经验条 / 本局经验；
        /// 4. 战报卡下方一条「本局成就」解锁卡（本局有新解锁才出现，见 <see cref="ResultAchievementsView"/>）；
        /// 5. 关键 CTA「再来一局」黄底墨字直角大按钮 + 次级「返回」纸白墨框。
        /// 4v4 明细表与胜差贴纸保持退场：结算页只回答「谁赢、赢多少、我干了什么、拿了什么」。
        /// 重影修复：百分比改纯平 TMP（display Heavy），不再给 Legacy Text 挂 uGUI Shadow——
        /// Shadow 复制的是不透明字形副本，配上半透明字色就会读成「灰字黑重影」。
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

            // 1. 顶部 Hero 级「胜利 / 失败」判定大牌
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
                victoryBadgeBg.Shape = UIInkShape.RoundedRect; // 圆角化：胜利判定牌
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
                victoryBadgeText.text = "胜利";
                victoryBadgeText.raycastTarget = false;
                victoryBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
                victoryBadgeText.overflowMode = TextOverflowModes.Overflow;
                victoryBadgeText.font = resolved.ResolveFont(true, "胜利");

                // 像素硬影 (4,-4)：双层描边字，墨底黄字也有"套色偏移"的印刷感
                _victoryBadgeChunky = txtGo.AddComponent<UIChunkyText>();
                _victoryBadgeChunky.ApplyTheme(resolved);
                _victoryBadgeChunky.Configure(resolved, "胜利", victoryBadgeText.fontSize,
                    displayFont: true, doubleLayered: true, fill: ComicStyle.Yellow, outlinedText: true);
            }

            victoryBadgeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.pivot = new Vector2(0.5f, 0.5f);
            // 两个汉字比 "VICTORY" 短得多：牌面同步收窄，才不会被读成一块空黑板。
            victoryBadgeRoot.anchoredPosition = new Vector2(0f, mobile ? 340f : 336f);
            victoryBadgeRoot.sizeDelta = new Vector2(mobile ? 540f : 480f, mobile ? 148f : 132f);
            victoryBadgeRoot.gameObject.SetActive(false);

            // 2. 两侧墨色读数板（我方 / 敌方）：深墨底 + 队色描边 + 超大百分比。
            //    拔河条已退场：两个读数并排本身就是一块完整比分牌，中间不再插第三个同义元素。
            _teamColorA = resolved.GetTeamColor(Team.TeamA);
            _teamColorB = resolved.GetTeamColor(Team.TeamB);

            float plateW = mobile ? 390f : 360f;
            float plateH = mobile ? 172f : 158f;
            float plateX = mobile ? 212f : 200f;
            float plateY = mobile ? 156f : 150f;
            float percentBase = mobile ? 74f : 88f;

            _coverPlateA = CreateCoveragePlate(rootRt, "CoverPlateA", resolved, Team.TeamA, "我方",
                new Vector2(-plateX, plateY), new Vector2(plateW, plateH), percentBase,
                out _percentTmpA, out _winMarkA);
            _coverPlateB = CreateCoveragePlate(rootRt, "CoverPlateB", resolved, Team.TeamB, "敌方",
                new Vector2(plateX, plateY), new Vector2(plateW, plateH), percentBase,
                out _percentTmpB, out _winMarkB);

            // 旧版 Legacy 百分比文本必须彻底退场：TMP 新读数一旦叠在它们上面，
            // 灰底字形会从板边漏出来，被误读成「重影」。
            if (resultPercentA != null) resultPercentA.gameObject.SetActive(false);
            if (resultPercentB != null) resultPercentB.gameObject.SetActive(false);
            if (resultBarA != null) resultBarA.gameObject.SetActive(false);
            if (resultBarB != null) resultBarB.gameObject.SetActive(false);

            // 旧场景柱条容器隐藏（其子 Image 已单独 Deactivate，容器也不应参与射线）。
            HideLegacyResultBar(resultPanel.transform, "ResultBarA");
            HideLegacyResultBar(resultPanel.transform, "ResultBarB");

            // 4. 判定文字退场：顶部大牌 + 两个巨大百分比已经把胜负说完，
            //    屏幕中间不再出现「正在计算… / 惜败 · 再接再厉」这类第二句结论。
            if (verdictText != null)
            {
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            // 5. 个人战报卡（涂地 / 击杀 / 阵亡 / 连杀 + 勋章条 + 等级 / 经验条 / 本局经验）
            BuildSummaryCard(rootRt, resolved, mobile);

            // 5.5 本局成就解锁条（装饰层：本局无新解锁时整条隐藏）
            EnsureAchievementsStrip(rootRt, resolved, mobile);

            // 6. 再来一局 CTA（主行动黄 + 墨字 + Button 硬影）+ 次级「返回」（纸白 + 墨框）
            //    成就条的题头带（节标 + 6px 缝）要靠整体下移让出来，按钮跟着下沉；
            //    底缘随之从 -508 / -504 变到 -520 / -530，同步更新 ApplyResultColumnFit 的设计稿包络。
            float btnY = mobile ? -482f : -474f;
            Vector2 restartSize = mobile ? new Vector2(390f, 96f) : new Vector2(340f, 92f);
            Vector2 backSize = mobile ? new Vector2(200f, 96f) : new Vector2(180f, 92f);
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
                    rShape.Shape = UIInkShape.RoundedRect; // 圆角化：再来一局按钮
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
                bShape.Shape = UIInkShape.RoundedRect; // 圆角化：返回按钮
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

            // 9. 纵向拟合：宽屏（画布变矮）时整列上移，CTA 按钮不再溢出屏幕底缘。
            ApplyResultColumnFit();
        }

        /// <summary>
        /// 结算列纵向拟合：整列（判定牌 / 读数板 / 战报卡 / 成就条 / CTA 按钮组）
        /// 按 1080 高设计稿从中心绝对定位，而 HUD 画布是 ScaleWithScreenSize(1920×1080, match 0.5)——
        /// 宽屏下画布纵向单位数收缩（约 2.1:1 时只剩 ~987），按钮底缘（-520）会溢出屏幕底。
        /// 这里按 resultPanel 实际高度把整列等量上移，内部间距一概不动；
        /// 16:9 基准下位移约 12px（移动端 14px），刚好把设计稿留的底部余量（32 / 24）拿回来。
        /// </summary>
        private void ApplyResultColumnFit()
        {
            if (resultPanel == null) return;
            float canvasH = ((RectTransform)resultPanel.transform).rect.height;
            if (canvasH <= 1f) return; // 画布尚未完成布局，等 Update 里重试
            if (_resultFitCanvasH >= 0f && Mathf.Abs(canvasH - _resultFitCanvasH) < 0.5f) return;
            _resultFitCanvasH = canvasH;

            bool mobile = UITypography.IsMobileLike;
            float halfH = canvasH * 0.5f;
            // 设计稿纵向包络：判定牌顶 +402（移动端 414），按钮底缘 -520（移动端 -530）。
            // 底缘为成就条题头带让位后整体下沉过，改按钮位置时必须同步这里，否则窄屏会漏出屏幕。
            float topExtent = mobile ? 414f : 402f;
            float bottomExtent = mobile ? 530f : 520f;
            float bottomMargin = mobile ? 24f : 32f;

            float shift = Mathf.Max(0f, bottomExtent + bottomMargin - halfH);
            // 超宽屏兜底：判定牌离屏顶至少留 16px，移不动就少移（此时底部只能部分保住）。
            shift = Mathf.Min(shift, Mathf.Max(0f, halfH - topExtent - 16f));

            float delta = shift - _resultFitShift;
            if (Mathf.Abs(delta) < 0.01f) return;
            _resultFitShift = shift;

            if (victoryBadgeRoot != null) victoryBadgeRoot.anchoredPosition += new Vector2(0f, delta);

            // GeometricResultRoot 是全屏拉伸：改 offsetMin/Max 整体平移，
            // 读数板 / 战报卡 / 成就条 / 升级页签都挂在它下面，跟着一起走。
            Transform colRoot = resultPanel.transform.Find("GeometricResultRoot");
            if (colRoot != null)
            {
                var colRt = (RectTransform)colRoot;
                colRt.offsetMin += new Vector2(0f, delta);
                colRt.offsetMax += new Vector2(0f, delta);
            }

            if (restartButton != null)
            {
                var rRt = (RectTransform)restartButton.transform;
                rRt.anchoredPosition += new Vector2(0f, delta);
            }

            Transform backTrans = resultPanel.transform.Find("ResultBackButton");
            if (backTrans != null)
            {
                var bRt = (RectTransform)backTrans;
                bRt.anchoredPosition += new Vector2(0f, delta);
            }
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
            out TextMeshProUGUI percent, out RectTransform winMark)
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

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：覆盖率读数板自适应圆角
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
            markShape.Shape = UIInkShape.Pill; // 圆角化：「胜」签改胶囊
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

        /// <summary>
        /// 掷点定格后强调胜方：胜方板角点亮黄「胜」签，败方读数转不透明灰（#9C9C9C）。
        /// 这里绝不使用半透明降灰：半透明字形 + 任何描边/副本都会读成「重影」。
        /// </summary>
        private void ApplyWinnerEmphasis(bool playerWon)
        {
            if (_winnerEmphasisApplied) return;
            _winnerEmphasisApplied = true;

            if (_winMarkA != null) _winMarkA.gameObject.SetActive(playerWon);
            if (_winMarkB != null) _winMarkB.gameObject.SetActive(!playerWon);

            if (_percentTmpA != null) _percentTmpA.color = playerWon ? _teamColorA : PlateDimReadout;
            if (_percentTmpB != null) _percentTmpB.color = playerWon ? PlateDimReadout : _teamColorB;
        }

        /// <summary>
        /// 本局成就解锁条宿主：条内卡片由 <see cref="ResultAchievementsView"/> 自行装配；
        /// 本方法只负责挂载位置（战报卡下方）与一次性构建。
        /// </summary>
        private void EnsureAchievementsStrip(RectTransform parent, UIThemeSO resolved, bool mobile)
        {
            if (_achievementsView != null) return;

            var go = new GameObject("ResultAchievements", typeof(RectTransform), typeof(ResultAchievementsView));
            go.transform.SetParent(parent, false);
            _achievementsView = go.GetComponent<ResultAchievementsView>();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // 挂载点要连「本局成就」节标的题头带一起往下让：节标底边贴卡行上沿 6px、
            // 顶边还要在战报卡下缘（-268 / -266）之外 10px，合起来约 50px（见 ResultAchievementsView.Build）。
            // 旧值 -332 / -330 没算这段，节标整个压在战报卡的下边框上。
            rt.anchoredPosition = new Vector2(0f, mobile ? -370f : -368f);
            rt.sizeDelta = new Vector2(mobile ? 1080f : 1000f, mobile ? 190f : 180f);

            _achievementsView.EnsureBuilt(resolved, mobile);
            go.SetActive(false);
        }

        /// <summary>
        /// 个人战报卡（结算页唯一的信息卡，v2）：
        /// 上半 = 击杀 / 阵亡 / 涂地 / 连杀四列（小标 + 大数字 + 新纪录黄签），
        /// 中段 = 勋章条（金银铜胶囊 chips + 首枚勋章幽默描述），
        /// 下半 = 等级 / 经验条 / 本局经验。
        /// 纸白 + 3px 墨框 + 面板硬影 + 顶部 5px 身份色带：全页唯一的「纸上数据」。
        /// 读数值在掷点揭晓时才写入（见 <see cref="UpdatePostRollVisuals"/>），卡片本身先隐藏。
        /// </summary>
        private void BuildSummaryCard(RectTransform parent, UIThemeSO resolved, bool mobile)
        {
            if (_reportPanelRoot != null) return;

            const float bandH = 5f; // 顶部身份色带（规范：5px，不承载文字）
            float frameW = mobile ? 1080f : 1000f;
            float frameH = mobile ? 304f : 300f;
            float frameY = mobile ? -114f : -118f;
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
            frameShape.Shape = UIInkShape.RoundedRect; // 圆角化：战报卡自适应圆角
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

            // 顶部身份色带（胜利金 / 失败灰蓝，揭晓时才定色）。
            // 圆角化：色带横向内缩到圆角直线段内并改胶囊短划。
            float reportRadius = ComicStyle.CornerFor(frameW, frameH);
            var bandGo = new GameObject("ReportBand", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(frameGo.transform, false);
            _reportBand = (RectTransform)bandGo.transform;
            _reportBand.anchorMin = new Vector2(0f, 1f);
            _reportBand.anchorMax = new Vector2(1f, 1f);
            _reportBand.pivot = new Vector2(0.5f, 1f);
            _reportBand.sizeDelta = new Vector2(-(reportRadius + ComicStyle.BorderThin) * 2f, bandH);
            _reportBand.anchoredPosition = new Vector2(0f, -ComicStyle.BorderThin);

            _reportBandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            _reportBandShape.Shape = UIInkShape.Pill;
            _reportBandShape.BorderWidth = 0f;
            _reportBandShape.color = ComicStyle.Yellow;
            _reportBandShape.HardShadow = false;
            _reportBandShape.Halftone = false;
            _reportBandShape.raycastTarget = false;

            // ---------- 四列个人战绩：涂地 / 击杀 / 阵亡 / 连杀 ----------
            float labelY = mobile ? 112f : 102f;
            float valueY = mobile ? 66f : 56f;

            _statTurfText = CreateStatColumn(frameGo.transform, "StatTurf", "涂地", StatColumnX(0, mobile), labelY, valueY);
            _statKillText = CreateStatColumn(frameGo.transform, "StatKill", "击杀", StatColumnX(1, mobile), labelY, valueY);
            _statDeathText = CreateStatColumn(frameGo.transform, "StatDeath", "阵亡", StatColumnX(2, mobile), labelY, valueY);
            _statStreakText = CreateStatColumn(frameGo.transform, "StatStreak", "连杀", StatColumnX(3, mobile), labelY, valueY);

            // 新纪录角签：结算出「本局新高」才点亮的小黄签（0=涂地 1=击杀 2=阵亡占位 3=连杀）。
            float tagY = mobile ? 12f : 10f;
            for (int i = 0; i < _recordTagRoots.Length; i++)
            {
                _recordTagRoots[i] = CreateRecordTag(frameGo.transform, "RecordTag_" + i,
                    StatColumnX(i, mobile), tagY, mobile);
            }

            // ---------- 勋章条：金银铜胶囊 chips（MatchAwards，最多 4 枚 + 「+N」） ----------
            float chipY = mobile ? -22f : -20f;
            for (int i = 0; i < _medalChipRoots.Length; i++)
            {
                _medalChipRoots[i] = CreateMedalChip(frameGo.transform, "MedalChip_" + i,
                    i, chipY, mobile, out _medalChipTexts[i]);
            }

            // 首枚（优先级最高）勋章的幽默描述：单行小字，让「无人能碰」们不只是个名词。
            _medalDescText = CreateTmpText(frameGo.transform, "MedalDescLine", string.Empty,
                UITypography.FontSnapped(mobile ? 16f : 14f), isDisplay: false,
                new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.6f),
                new Vector2(0f, mobile ? -52f : -48f), new Vector2(frameW - inset * 2f, 22f), 1f,
                TextAlignmentOptions.Center);
            _medalDescText.fontStyle = FontStyles.Bold;
            _medalDescText.textWrappingMode = TextWrappingModes.NoWrap;
            _medalDescText.overflowMode = TextOverflowModes.Overflow;
            _medalDescText.gameObject.SetActive(false);

            // 战绩区与成长区之间的分隔规
            float ruleY = mobile ? -64f : -60f;
            CreateGeometricSquare(frameGo.transform, "SummaryRule", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, ruleY),
                new Vector2(frameW - inset * 2f, 2f), 0f, ComicStyle.PaperAlt);

            // ---------- 成长区：等级 + 经验条 + 本局经验 ----------
            float stripY = mobile ? -108f : -104f;
            float levelW = mobile ? 260f : 220f;
            float gainW = mobile ? 280f : 230f;
            float trackW = Mathf.Max(200f, frameW - inset * 2f - levelW - gainW - 60f);
            float innerLeft = -frameW * 0.5f + inset;
            float innerRight = frameW * 0.5f - inset;

            var levelGo = new GameObject("LevelText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            levelGo.transform.SetParent(frameGo.transform, false);
            var levelRt = (RectTransform)levelGo.transform;
            levelRt.anchorMin = new Vector2(0.5f, 0.5f);
            levelRt.anchorMax = new Vector2(0.5f, 0.5f);
            levelRt.pivot = new Vector2(0f, 0.5f);
            levelRt.anchoredPosition = new Vector2(innerLeft, stripY);
            levelRt.sizeDelta = new Vector2(levelW, mobile ? 60f : 56f);

            _xpLevelText = levelGo.GetComponent<TextMeshProUGUI>();
            _xpLevelText.text = "等级 1";
            _xpLevelText.font = resolved.ResolveFont(true, "等级 1");
            _xpLevelText.fontSize = UITypography.FontSnapped(40f);
            _xpLevelText.fontStyle = FontStyles.Bold;
            _xpLevelText.alignment = TextAlignmentOptions.MidlineLeft;
            _xpLevelText.color = ComicStyle.Ink;
            _xpLevelText.raycastTarget = false;
            _xpLevelText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpLevelText.overflowMode = TextOverflowModes.Overflow;

            var gainGo = new GameObject("XpGainText", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            gainGo.transform.SetParent(frameGo.transform, false);
            var gainRt = (RectTransform)gainGo.transform;
            // 必须用中心锚点 + 中心系坐标：右边缘锚点会把读数再往右推一个 contentRight。
            gainRt.anchorMin = new Vector2(0.5f, 0.5f);
            gainRt.anchorMax = new Vector2(0.5f, 0.5f);
            gainRt.pivot = new Vector2(1f, 0.5f);
            gainRt.anchoredPosition = new Vector2(innerRight, stripY);
            gainRt.sizeDelta = new Vector2(gainW, mobile ? 36f : 32f);

            _xpGainText = gainGo.GetComponent<TextMeshProUGUI>();
            _xpGainText.text = "经验 +0";
            _xpGainText.font = resolved.ResolveFont(true, "经验 +0");
            _xpGainText.fontSize = UITypography.FontSnapped(22f);
            _xpGainText.fontStyle = FontStyles.Bold;
            _xpGainText.alignment = TextAlignmentOptions.MidlineRight;
            _xpGainText.color = ComicStyle.Ink;
            _xpGainText.raycastTarget = false;
            _xpGainText.textWrappingMode = TextWrappingModes.NoWrap;
            _xpGainText.overflowMode = TextOverflowModes.Overflow;

            // 经验条：夹在等级与经验读数之间，纯色直角，无数字标签，长度本身就是进度。
            var trackGo = new GameObject("XpTrack", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            trackGo.transform.SetParent(frameGo.transform, false);
            var trackRt = (RectTransform)trackGo.transform;
            trackRt.anchorMin = new Vector2(0.5f, 0.5f);
            trackRt.anchorMax = new Vector2(0.5f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.anchoredPosition = new Vector2(0f, stripY);
            trackRt.sizeDelta = new Vector2(trackW, mobile ? 14f : 12f);

            var trackShape = trackGo.GetComponent<UIInkShapeGraphic>();
            trackShape.Shape = UIInkShape.Pill; // 圆角化：XP 轨道胶囊化（描边环同形状）
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
            UIFlatSprite.MakePill(_xpBarFill, filled: true); // 圆角化：胶囊填充
            _xpBarFill.color = ComicStyle.Yellow;
            _xpBarFill.fillAmount = 0f;
            _xpBarFill.raycastTarget = false;

            // 升级页签：骑在卡片上边框上。
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
            luShape.Shape = UIInkShape.Pill; // 圆角化：升级页签改胶囊
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
            _levelUpText.text = "升级 · 等级 1";
            _levelUpText.font = resolved.ResolveFont(true, "升级");
            _levelUpText.fontSize = UITypography.FontSnapped(16f);
            _levelUpText.fontStyle = FontStyles.Bold;
            _levelUpText.alignment = TextAlignmentOptions.Center;
            _levelUpText.color = ComicStyle.Ink;
            _levelUpText.raycastTarget = false;
            _levelUpText.textWrappingMode = TextWrappingModes.NoWrap;
            _levelUpText.overflowMode = TextOverflowModes.Overflow;

            _levelUpBadgeRoot.gameObject.SetActive(false);

            // 解锁页签：与升级页签同款胶囊，骑在卡片右上边框上（同一行左右各一枚，不叠）。
            // 文案在结算构建时按「本局新解锁的外观」写好，动画阶段只负责弹出。
            var unlockGo = new GameObject("UnlockBadge", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            unlockGo.transform.SetParent(frameGo.transform, false);
            _unlockBadgeRoot = (RectTransform)unlockGo.transform;
            _unlockBadgeRoot.anchorMin = new Vector2(1f, 1f);
            _unlockBadgeRoot.anchorMax = new Vector2(1f, 1f);
            _unlockBadgeRoot.pivot = new Vector2(1f, 1f);
            _unlockBadgeRoot.anchoredPosition = new Vector2(-inset, 16f);
            _unlockBadgeRoot.sizeDelta = new Vector2(mobile ? 300f : 264f, mobile ? 34f : 30f);
            _unlockBadgeBaseWidth = _unlockBadgeRoot.sizeDelta.x;
            // 同一行左边还有升级页签（左内缩 + 自身宽），中间留 16px 缝：文案撑宽时不许撞上去。
            float levelBadgeWidth = mobile ? 240f : 216f;
            _unlockBadgeMaxWidth = Mathf.Max(_unlockBadgeBaseWidth, frameW - inset * 2f - levelBadgeWidth - 16f);

            var unlockShape = unlockGo.GetComponent<UIInkShapeGraphic>();
            unlockShape.Shape = UIInkShape.Pill; // 圆角化：与升级页签同款胶囊
            unlockShape.BorderWidth = ComicStyle.BorderThin;
            unlockShape.BorderColor = ComicStyle.Ink;
            unlockShape.color = ComicStyle.Yellow; // 金 = 奖励（与主行动黄同源）
            unlockShape.HardShadow = false;
            unlockShape.raycastTarget = false;

            _unlockMotion = unlockGo.GetComponent<UISpringMotion>();
            _unlockMotion.SetTheme(resolved);

            var unlockTxtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            unlockTxtGo.transform.SetParent(_unlockBadgeRoot, false);
            Stretch(unlockTxtGo.GetComponent<RectTransform>());

            _unlockText = unlockTxtGo.GetComponent<TextMeshProUGUI>();
            _unlockText.text = "解锁";
            _unlockText.font = resolved.ResolveFont(true, "解锁");
            // 18（移动端 → 24）：规范禁止 13~16px 的可读文字。左侧升级页签历史值是 16，
            // 已在交付说明里标注为既有偏差，这里不跟着复刻。
            _unlockText.fontSize = UITypography.FontSnapped(18f);
            _unlockText.fontStyle = FontStyles.Bold;
            _unlockText.alignment = TextAlignmentOptions.Center;
            _unlockText.color = ComicStyle.Ink;
            _unlockText.raycastTarget = false;
            _unlockText.textWrappingMode = TextWrappingModes.NoWrap;
            _unlockText.overflowMode = TextOverflowModes.Overflow;

            _unlockBadgeRoot.gameObject.SetActive(false);
            _reportPanelRoot.gameObject.SetActive(false);
        }

        /// <summary>结算卡里的一列读数：中文小标 + 大数字。</summary>
        private TextMeshProUGUI CreateStatColumn(Transform parent, string name, string label,
            float x, float labelY, float valueY)
        {
            var labelTmp = CreateTmpText(parent, name + "Label", label, UITypography.FontSnapped(20f),
                isDisplay: false, new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.55f),
                new Vector2(x, labelY), new Vector2(220f, 26f), 2f, TextAlignmentOptions.Center);
            labelTmp.fontStyle = FontStyles.Bold;

            var value = CreateTmpText(parent, name + "Value", "0", UITypography.FontSnapped(60f),
                isDisplay: true, ComicStyle.Ink, new Vector2(x, valueY),
                new Vector2(240f, 80f), 0f, TextAlignmentOptions.Center);
            value.fontStyle = FontStyles.Bold;
            value.overflowMode = TextOverflowModes.Overflow;
            return value;
        }

        /// <summary>战报卡四列读数的横向位置（0=涂地 1=击杀 2=阵亡 3=连杀）。</summary>
        private static float StatColumnX(int index, bool mobile)
        {
            switch (index)
            {
                case 0: return mobile ? -382f : -356f;
                case 1: return mobile ? -127f : -119f;
                case 2: return mobile ? 127f : 119f;
                default: return mobile ? 382f : 356f;
            }
        }

        /// <summary>「纪录!」小黄签：挂在对应读数列下方，结算出本局新高才点亮。</summary>
        private RectTransform CreateRecordTag(Transform parent, string name, float x, float y, bool mobile)
        {
            Vector2 size = new Vector2(mobile ? 86f : 74f, mobile ? 30f : 26f);
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Yellow;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            var label = CreateTmpText(go.transform, "Text", "纪录!", UITypography.FontSnapped(mobile ? 15f : 13f),
                isDisplay: false, ComicStyle.Ink, Vector2.zero, size, 1f, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;

            go.SetActive(false);
            return rect;
        }

        /// <summary>勋章胶囊 chip：勋章色实底 + 墨字 + 墨框（金银铜由 MatchAwards.GetBadgeColor 决定）。</summary>
        private RectTransform CreateMedalChip(Transform parent, string name, int index, float y, bool mobile,
            out TextMeshProUGUI text)
        {
            float chipW = mobile ? 150f : 124f;
            float chipH = mobile ? 40f : 34f;
            float stride = chipW + (mobile ? 14f : 12f);
            float x = -stride * (_medalChipRoots.Length - 1) * 0.5f + index * stride;

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(chipW, chipH);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Yellow;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;

            text = CreateTmpText(go.transform, "Text", string.Empty, UITypography.FontSnapped(mobile ? 18f : 16f),
                isDisplay: false, ComicStyle.Ink, Vector2.zero, new Vector2(chipW, chipH), 0.5f,
                TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            go.SetActive(false);
            return rect;
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

            // 版本号只作为「数据源」保留（首页右下角签已改成「检查更新」小标签，版本号只在启动页展示）。
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

            // 检查更新按钮：只是数据/监听载体，视觉与摆位由右下角「检查更新」小标签承担
            // （BuildHomeCornerTags → AttachMenuButtonToUpdateTag）。首页没建起来时它没有射线，
            // 避免在 readyPanel 上留一块看不见却吃点击的死区。
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
                img.raycastTarget = false;

                checkUpdateButton = btnGo.GetComponent<Button>();
                checkUpdateButton.targetGraphic = img;
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
            UpdateManager updater = UpdateManager.Instance;
            if (updater == null) return;

            // TapTap 上架变体（P1_OFFLINE）：没有联网能力，也不该出现这颗按钮；
            // 万一被场景遗留节点调到，直接退回只读文案，不发请求。
            if (!UpdateManager.NetworkFeatureEnabled)
            {
                SetUpdateTagText($"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}");
                return;
            }

            // 已经查到待更新版本（标签还停在「新版 vX!」）：再点一下直接重开更新弹窗，
            // 不再白跑一次网络检查（t-1 的 _on_update_pressed 同款分流）。
            if (updater.PendingUpdate != null)
            {
                SetUpdateTagState($"新版 {updater.PendingUpdate.tagName}!", autoReset: false);
                UpdateDialog.Instance.PopupUpdate(updater.PendingUpdate);
                return;
            }

            SetUpdateTagState("检查更新中...", autoReset: false);
            updater.CheckForUpdate(manual: true);

            // 未同意隐私政策（或已有一次检查在跑）时 CheckForUpdate 会静默 return，
            // 标签不能就这么停在「检查更新中...」——没真的跑起来就退回常态文案。
            if (!updater.IsChecking) SetUpdateTagText(UpdateTagIdleText);
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
            if (MapSelectOverlay.IsOpen) return false;
            if (LockerOverlay.IsOpen) return false;
            if (MobileLayoutCustomizer.IsEditingAny) return false;
            return true;
        }

        private void TryStartMatch()
        {
            if (!CanStartMatch()) return;

            var match = MatchManager.Instance;
            if (match == null) return;
            if (match.CurrentPhase != MatchManager.Phase.Ready && match.CurrentPhase != MatchManager.Phase.Result) return;

            // 存档里的选中图与当前场景不一致（例如刚在地图浮层里换了图 / 外部改过存档）：
            // 先真实切图，加载完成后由 MapStartupRouter 自动开赛，玩家不用按第二次。
            // 用 MapForActiveScene 而不是 Scene.path：玩家包里 Scene.path 可能为空，
            // 旧写法会让这里与地图选择浮层的判断不一致，表现为「点了开始游戏什么都不发生」。
            if (MapCatalogService.MapForActiveScene() != null && !MapCatalogService.IsSelectedSceneActive())
            {
                MapDefinition target = MapCatalogService.Selected;
                if (!MapCatalogService.CanLoadMap(target))
                {
                    // 老安装包只打了主场景：必须给出可见提示，不能继续静默失败。
                    ShowHomeMapHint($"缺少「{target.DisplayName}」场地 · 请更新客户端");
                    Debug.LogError($"[MatchFlow] 安装包里没有选中的地图场景，无法开赛：{target.ScenePath}");
                    return;
                }

                MapStartupRouter.RequestAutoStart();
                if (!MapCatalogService.LoadMap(target))
                {
                    // 场景已经在加载（例如启动补切/上一次点击仍在跑）：保留刚挂上的自动开赛，
                    // 不能在这里 Cancel，否则加载完成后会停在首页、玩家以为又没反应。
                    if (MapCatalogService.IsLoading) return;

                    MapStartupRouter.CancelAutoStart();
                    ShowHomeMapHint("场地载入未开始 · 请再点一次开始");
                    Debug.LogWarning("[MatchFlow] 切图请求未被接受；本次开赛已放弃：" + target.Id);
                }
                return;
            }

            match.StartMatch();
        }

        /// <summary>首页地图卡的信息行兼作错误提示：手机端看不到 Debug.Log，失败必须落在屏幕文字上。</summary>
        private void ShowHomeMapHint(string message)
        {
            if (_homeMapInfo == null) return;
            _homeMapInfo.text = message;
            _homeMapInfo.color = ComicStyle.Red;
            FitTileInfoText(_homeMapInfo);
        }

        private void Update()
        {
            // 首页几何守护：Android 启动/旋转瞬间安全区与画布可能尚未 settle，
            // EnsureReadyVisuals 只在 Awake 构建一次，首帧脏几何会把 2×3 卡阵永久推出屏幕
            // （真机 20:9 实测：右列整体出屏、下排与 CTA 被裁）。几何稳定一帧后即用真实值重建。
            if (_homeGeometryKeyValid && readyPanel != null && readyPanel.activeInHierarchy)
            {
                Vector4 key = CurrentHomeGeometryKey();
                bool changed = MaxDimensionDelta(key, _homeGeometryKey) > 0.5f;
                bool settled = MaxDimensionDelta(key, _homeGeometryKeyPrev) <= 0.5f;
                _homeGeometryKeyPrev = key;
                s_homeGeometryStableFrames = settled ? s_homeGeometryStableFrames + 1 : 0;
                if (changed && settled)
                {
                    RebuildHomeVisuals();
                }
            }
            else
            {
                s_homeGeometryStableFrames = 0;
            }

            var match = MatchManager.Instance;
            if (match == null) return;

            // 快捷键开赛与重开：首页信息浮层 / 衣柜 / 移动端按键布局编辑打开时让路，
            // 否则 Space/Enter 会隔着遮罩直接把比赛开了（布局编辑把 timeScale 置 0，倒计时会冻住）。
            if (!Application.isMobilePlatform && !MobileTouchUI.IsTouchSimulationActive &&
                !HomeInfoOverlay.IsOpen && !LockerOverlay.IsOpen && !MobileLayoutCustomizer.IsEditingAny &&
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
                // 记下切换前的相位：退场转场只认「结算 → 首页」这条真实转换，
                // 首帧同步（_lastPhase 初值 -1）或场景里 resultPanel 恰好默认激活都不算。
                _phaseBeforeTransition = _lastPhase;
                _lastPhase = match.CurrentPhase;
                bool firstSync = !_firstPhaseSynced;
                _firstPhaseSynced = true;
                HandlePhaseChanged(match, firstSync);
            }

            // 1:00 决战横幅由 CenterTimerView 独家负责（每局一次、ResetForMatch 复位）。
            // 这里不要再触发 MatchBannerView.ShowOneMinute：海鸥港湾 / 霓虹滑板场两张地图的
            // MatchBannerView 是激活的，会和计时球横幅同时弹出「最后 1 分钟」两条提示。

            if (match.CurrentPhase == MatchManager.Phase.Result && resultPanel != null && resultPanel.activeSelf)
            {
                _resultElapsed += Time.deltaTime;
                ApplyResultColumnFit(); // 窗口比例变化时跟随重排（无变化时是廉价空转）
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
            if (phase != MatchManager.Phase.Ready)
            {
                HomeInfoOverlay.CloseIfOpen();
                LockerOverlay.CloseIfOpen();
            }

            if (phase == MatchManager.Phase.Ready)
            {
                // 从对局/入场演出中返回主菜单：撤掉可能还挂着的横幅
                if (bannerView != null) bannerView.HideImmediate();
                s_SettledThisMatch = false;
                _profileSettled = false;
                _lastLevelUpStep = 0;
                ResetMatchAchievementState();
                // 若本帧会走「结算页淡出」转场，就把揭示层的收起延后到淡出结束一起做，
                // 避免战报卡 / 成长条在淡出开始前就先消失（见 _hideResultRevealAfterExit）。
                if (WillPlayResultExit()) _hideResultRevealAfterExit = true;
                else HideResultRevealVisuals();
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
                _numbersSettled = false;
                s_SettledThisMatch = false;
                _profileSettled = false;
                _lastLevelUpStep = 0;
                ResetMatchAchievementState();
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
                _numbersSettled = false;
                _lastLevelUpStep = 0;
                HideResultRevealVisuals();
                if (bannerView != null) bannerView.HideImmediate();
                PlayResultSound(match);
                _resultEntrancePending = true;

                if (!s_SettledThisMatch)
                {
                    s_SettledThisMatch = true;
                    SettlePlayerProfile(match);
                }
            }
        }

        /// <summary>
        /// 复位「每局一次」的成就 / 勋章结算状态。必须在 Ready 与 Countdown 两个分支都调用：
        /// 项目开了 DisableDomainReload，实例字段随对象存活，但第二局从 Countdown 重进时
        /// 若只清一处就会沿用上一局的新解锁列表（s_SettledThisMatch 同款坑）。
        /// </summary>
        private void ResetMatchAchievementState()
        {
            _achievementOutcome = null;
            _localAwards = AwardType.None;
        }

        /// <summary>
        /// 收起结算页「掷点后才出现」的那一层（战报卡 / 成长条 / 胜签 / 勋章条 / 成就条），
        /// 让下一局的结算从干净的落后状态重新播放。
        /// </summary>
        private void HideResultRevealVisuals()
        {
            if (_reportPanelRoot != null) _reportPanelRoot.gameObject.SetActive(false);
            if (_levelUpBadgeRoot != null) _levelUpBadgeRoot.gameObject.SetActive(false);
            // 解锁页签挂在战报卡上：收起战报卡时必须一起复位，否则第二局会残留上一局的「解锁 · X」。
            if (_unlockBadgeRoot != null) _unlockBadgeRoot.gameObject.SetActive(false);
            _unlockBadgeShown = false;
            _unlockCosmetics.Clear();
            if (_winMarkA != null) _winMarkA.gameObject.SetActive(false);
            if (_winMarkB != null) _winMarkB.gameObject.SetActive(false);
            _winnerEmphasisApplied = false;

            if (_achievementsView != null) _achievementsView.Hide();

            for (int i = 0; i < _recordTagRoots.Length; i++)
            {
                if (_recordTagRoots[i] != null) _recordTagRoots[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _medalChipRoots.Length; i++)
            {
                if (_medalChipRoots[i] != null) _medalChipRoots[i].gameObject.SetActive(false);
            }
            if (_medalDescText != null) _medalDescText.gameObject.SetActive(false);

            // 败方数字的降灰要在下一局恢复，否则第二次结算会沿用上一局的胜负配色。
            if (_percentTmpA != null) _percentTmpA.color = _teamColorA;
            if (_percentTmpB != null) _percentTmpB.color = _teamColorB;

            UpdateCoverReadout(0f, 0f);
        }

        private void ApplyPhase(MatchManager match)
        {
            var phase = match.CurrentPhase;

            if (_fullscreenUnderlay != null)
            {
                bool showUnderlay = phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Result;
                _fullscreenUnderlay.SetActive(showUnderlay);
            }

            // 结算 → 首页：结算页先播一次淡出再切回首页（本文件唯一加退场转场的路径）。
            // 退场进行中不改两个面板的显隐，收敛后由 TickResultExit 自己完成切换。
            bool resultExitRunning = false;
            if (WillPlayResultExit())
            {
                if (!_resultExitActive) BeginResultExit();
                TickResultExit();
                resultExitRunning = _resultExitActive;
            }
            else if (_resultExitActive)
            {
                // 退场途中相位被别的路径改走（例如退场时又按空格「再来一局」）：立刻撤销，
                // 否则 _resultExitActive 会一直挂着，面板显隐被永久跳过。
                CancelResultExit();
            }

            if (!resultExitRunning)
            {
                if (readyPanel != null) readyPanel.SetActive(phase == MatchManager.Phase.Ready);
                if (resultPanel != null) resultPanel.SetActive(phase == MatchManager.Phase.Result);
            }

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

        /// <summary>
        /// 是否处于「结算 → 首页」的退场转场：只有从 Result 真实切过来的那一帧起、且结算页仍在显示时为真。
        /// 必须在 ApplyPhase 之前（HandlePhaseChanged 里）判断——那时 resultPanel 还没被切掉。
        /// 用 _phaseBeforeTransition 而不是首帧就为 Ready 的同步，避免场景里 resultPanel 默认激活时开局莫名淡出。
        /// </summary>
        private bool WillPlayResultExit()
        {
            return _phaseBeforeTransition == MatchManager.Phase.Result &&
                   resultPanel != null && readyPanel != null && resultPanel.activeSelf;
        }

        /// <summary>
        /// 结算页退场起播：给 resultPanel 挂 CanvasGroup 后把整页淡出。
        /// 幂等由 _resultExitActive 保证（见字段注释）；同时关掉射线，退场途中按钮不可再被点到。
        /// </summary>
        private void BeginResultExit()
        {
            if (resultPanel == null) return;

            if (_resultExitGroup == null)
            {
                _resultExitGroup = resultPanel.GetComponent<CanvasGroup>();
                if (_resultExitGroup == null) _resultExitGroup = resultPanel.AddComponent<CanvasGroup>();
            }

            _resultExitGroup.alpha = 1f;
            _resultExitGroup.blocksRaycasts = false;
            _resultExitGroup.interactable = false;
            _resultExitActive = true;
            _resultExitElapsed = 0f;
        }

        /// <summary>推进结算页淡出；收敛后隐藏结算页、亮出首页，并复位整页状态。</summary>
        private void TickResultExit()
        {
            if (!_resultExitActive || resultPanel == null) return;

            // 用 unscaledDeltaTime：暂停菜单 / 布局编辑会把 timeScale 置 0，缩放在 0 时也要能推进。
            UIThemeSO settings = UIThemeSO.Resolve(uiTheme);
            float duration = settings.ClampEntranceDuration(settings.entranceDuration);
            _resultExitElapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_resultExitElapsed / duration);
            if (_resultExitGroup != null) _resultExitGroup.alpha = 1f - k;

            if (k < 1f) return;

            _resultExitActive = false;
            _resultExitElapsed = 0f;
            if (_resultExitGroup != null)
            {
                _resultExitGroup.alpha = 1f;
                _resultExitGroup.blocksRaycasts = true;
                _resultExitGroup.interactable = true;
            }

            // 揭示层延后收起的一并在此收尾（必须先于 readyPanel 激活，避免首页一帧露出残留）。
            FlushDeferredResultRevealHide();

            resultPanel.SetActive(false);
            // 首页入场动画由 Update 的 _homeEntrancePending 分支在 readyPanel 激活后补播，
            // 顺序上就是「结算先退、首页再进」。
            if (readyPanel != null) readyPanel.SetActive(true);
        }

        /// <summary>撤销未完成的结算页退场（相位在退场途中被改走时调用），恢复整页状态。</summary>
        private void CancelResultExit()
        {
            if (!_resultExitActive) return;

            _resultExitActive = false;
            _resultExitElapsed = 0f;
            if (_resultExitGroup != null)
            {
                _resultExitGroup.alpha = 1f;
                _resultExitGroup.blocksRaycasts = true;
                _resultExitGroup.interactable = true;
            }

            // 退场被打断，延后的揭示层收起也要补上，否则会残留到下一局。
            FlushDeferredResultRevealHide();
        }

        /// <summary>执行被延后的结算揭示层收起（幂等；无待处理时为廉价空转）。</summary>
        private void FlushDeferredResultRevealHide()
        {
            if (!_hideResultRevealAfterExit) return;
            _hideResultRevealAfterExit = false;
            HideResultRevealVisuals();
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

            UpdateCoverReadout(a, b);

            if (verdictText != null)
            {
                // 胜负结论已由顶部大牌 + 两块读数板承担，这里不再写第二句解说。
                verdictText.text = string.Empty;
                verdictText.gameObject.SetActive(false);
            }

            if (rollProgress >= 1f)
            {
                _numbersSettled = true;
                UpdateCoverReadout(match.FinalCoverageA, match.FinalCoverageB);

                // 掷点定格后点亮胜方：胜方「胜」签 + 败方读数转不透明灰。
                // 判定牌已在开场入场时与读数板 / 战报卡一起出现（内容也提前写好），这里只做兜底，
                // 绝不再 PlayIn 一次——否则整块牌会在数字停住时「二次弹出」。
                if (victoryBadgeRoot != null && !victoryBadgeRoot.gameObject.activeSelf)
                {
                    TriggerVictoryBadge(match.PlayerWon);
                }
                ApplyWinnerEmphasis(match.PlayerWon);

                if (!s_SettledThisMatch)
                {
                    s_SettledThisMatch = true;
                    SettlePlayerProfile(match);
                }
                UpdatePostRollVisuals(match);
            }
        }

        /// <summary>
        /// 读数：两块板只显示覆盖率百分比，按掷点进度从 0 滚到终场值。
        /// 拔河条退场后，这里不再逐帧改任何 Image 宽度。
        /// </summary>
        private void UpdateCoverReadout(float a, float b)
        {
            if (_percentTmpA != null) _percentTmpA.text = $"{a:F1}%";
            if (_percentTmpB != null) _percentTmpB.text = $"{b:F1}%";
        }

        /// <summary>
        /// 结算本地玩家档案与个人战绩：写 XP / 等级，并把本局涂地 / 击杀 / 阵亡存下来供战报卡读取。
        /// </summary>
        private void SettlePlayerProfile(MatchManager match)
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

                _localStats = localStats;

                bool won = (match != null) && match.PlayerWon;
                _profileResult = PlayerProfile.ApplyMatchResult(won, localStats);
                _profileSettled = true;

                // 勋章评定（全场参战者）+ 成就结算（单局阈值 + 生涯累计 + 新纪录）。
                // 两者都是装饰性增强：失败只记警告，不影响 XP 与战报卡本身。
                _localAwards = EvaluateLocalAwards(localHealth);
                _achievementOutcome = SettleAchievements(localHealth, localStats, won);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 结算个人档案异常: " + ex);
            }
        }

        /// <summary>对全场参战者评定勋章，返回本地玩家的勋章掩码（异常回退 None）。</summary>
        private AwardType EvaluateLocalAwards(CombatantHealth localHealth)
        {
            try
            {
                var all = CombatantHealthRegistry.All;
                if (all == null || all.Count == 0 || localHealth == null) return AwardType.None;

                AwardType[] results = new AwardType[all.Count]; // 结算期一次性分配，非热路径
                MatchAwards.Evaluate(all, ch => MatchStatsService.Get(ch), results);
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == localHealth) return results[i];
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 勋章评定失败: " + ex.Message);
            }
            return AwardType.None;
        }

        /// <summary>组装本局快照并交给成就服务结算（生涯累计 + 解锁判定 + 落盘）。</summary>
        private AchievementSettleOutcome SettleAchievements(CombatantHealth localHealth,
            CombatantMatchStats localStats, bool won)
        {
            try
            {
                float teamCoverage = 0f;
                float enemyCoverage = 0f;
                if (localHealth != null && InkSurfaceController.Instance != null)
                {
                    teamCoverage = InkSurfaceController.Instance.GetCoveragePercent(localHealth.Team);
                    enemyCoverage = InkSurfaceController.Instance.GetCoveragePercent(localHealth.Team.GetOpponent());
                }

                var ctx = new MatchSettleContext
                {
                    Local = localStats,
                    TeamCoveragePercent = teamCoverage,
                    EnemyCoveragePercent = enemyCoverage,
                    Won = won,
                    LocalGotFirstKill = localHealth != null && MatchStatsService.FirstKillKiller == localHealth,
                    MvpAwards = (_localAwards & AwardType.Mvp) != 0 ? 1 : 0,
                };

                return AchievementService.SettleMatch(ctx, ctx.MvpAwards);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MatchFlowUI] 成就结算失败: " + ex.Message);
                return null;
            }
        }

        private void UpdatePostRollVisuals(MatchManager match)
        {
            // 兜底：正常入场时战报卡已在 PlayResultEntrance 里出现；
            // 只有首帧就直接同步进 Result（没走 PhaseChanged）时才会走到这里补显示。
            if (_reportPanelRoot != null && !_reportPanelRoot.gameObject.activeSelf)
            {
                _reportPanelRoot.gameObject.SetActive(true);
                PopulateReportCard(match);

                // 卡片本身也做一记 pop-in（错峰由更新循环里的 0.05s 承接）
                if (_reportMotion != null)
                {
                    _reportMotion.Build();
                    _reportMotion.SnapHome();
                    _reportMotion.PlayIn(0.05f);
                }
            }

            float rollDuration = match != null ? Mathf.Max(0.01f, match.ResultRollSeconds) : 1f;
            float postRoll = _resultElapsed - rollDuration;

            // 1. XP 成长与跨级进度条推进
            float xpT = postRoll - 0.25f;
            if (xpT >= 0f && _profileSettled)
            {
                int levelsGained = _profileResult.LevelsGained;
                float animDuration = 0.8f + levelsGained * 0.6f;
                float norm = Mathf.Clamp01(xpT / animDuration);

                int curGain = Mathf.RoundToInt(_profileResult.XpGained * norm);
                if (_xpGainText != null) _xpGainText.text = $"经验 +{curGain}";

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

                // 2. 解锁页签：经验条走完后再顺延 0.15s 弹出（不打断成长数字的节奏）。
                //    这里只比较时间轴，不重算外观列表——列表已在 PopulateReportCard 里算过一次。
                float unlockAt = 0.25f + animDuration + 0.15f;
                if (postRoll >= unlockAt) ShowUnlockBadge();
            }
        }

        /// <summary>
        /// 战报卡内容：顶部身份色带 + 三列战绩 + 成长区复位到「本局结算前」的读数。
        /// 卡片与判定牌同批入场，所以这些值必须在入场时就写好；掷点结束只负责推进经验计数。
        /// </summary>
        private void PopulateReportCard(MatchManager match)
        {
            // 顶部身份色带：胜利金 / 失败灰蓝（状态色只占一条，信息层保持纸白可读）
            if (_reportBandShape != null)
            {
                _reportBandShape.color = (match != null && match.PlayerWon) ? ComicStyle.Yellow : DefeatSlate;
            }

            // 本局个人战绩：四列数字一次写入（数值固定，不需要滚动）。
            if (_statTurfText != null)
            {
                _statTurfText.text = Mathf.RoundToInt(_localStats.TurfSquareMeters).ToString();
            }
            if (_statKillText != null) _statKillText.text = _localStats.Kills.ToString();
            if (_statDeathText != null) _statDeathText.text = _localStats.Deaths.ToString();
            if (_statStreakText != null) _statStreakText.text = Mathf.Max(0, _localStats.MaxStreak).ToString();

            // 新纪录角签：本局刷出个人新高才点亮（涂地 / 击杀 / 连杀）。
            if (_achievementOutcome != null)
            {
                SetRecordTagVisible(0, _achievementOutcome.NewRecordTurf);
                SetRecordTagVisible(1, _achievementOutcome.NewRecordKills);
                SetRecordTagVisible(2, false);
                SetRecordTagVisible(3, _achievementOutcome.NewRecordStreak);
            }

            PopulateMedalChips();

            // 先回到「本局结算前」的读数，否则第二局会把上一局的等级 / 经验沿用过来。
            if (_profileSettled)
            {
                SetLevelDisplay(_profileResult.OldLevel);
                if (_xpGainText != null) _xpGainText.text = "经验 +0";
                if (_xpBarFill != null)
                {
                    int req = _profileResult.XpRequiredForNextLevel;
                    _xpBarFill.fillAmount = req > 0 ? Mathf.Clamp01((float)_profileResult.OldXp / req) : 0f;
                }
            }

            // 本局新解锁外观：结算构建时算一次存字段（等级跨过的 + 本局新成就绑定的），
            // 动画阶段只读这个列表决定要不要弹「解锁」页签，绝不每帧遍历外观目录。
            RefreshUnlockBadgeContent();
        }

        /// <summary>
        /// 计算并写入「解锁」页签文案（纯数据，构建/入场时调用一次）。
        /// 无新解锁时把页签收起，避免上一局的文案残留到这一局。
        /// </summary>
        private void RefreshUnlockBadgeContent()
        {
            _unlockCosmetics.Clear();
            _unlockBadgeShown = false;
            if (_unlockBadgeRoot != null) _unlockBadgeRoot.gameObject.SetActive(false);

            if (!_profileSettled) return;

            try
            {
                List<CosmeticDef> unlocked = CosmeticUnlockMath.CollectNewlyUnlocked(
                    _profileResult.OldLevel, _profileResult.NewLevel,
                    _achievementOutcome != null ? _achievementOutcome.NewlyUnlocked : null);
                if (unlocked == null || unlocked.Count == 0) return;
                _unlockCosmetics.AddRange(unlocked);
            }
            catch (System.Exception ex)
            {
                // 装饰性提示失败不影响战报卡与经验条。
                Debug.LogWarning("[MatchFlowUI] 结算外观解锁计算失败：" + ex.Message);
                return;
            }

            if (_unlockText == null) return;

            // 一件：解锁 · 薄荷汽水；多件：解锁 · 薄荷汽水 等 N 件（不列全名，页签只有一行）。
            string first = _unlockCosmetics[0].DisplayName;
            _unlockText.text = _unlockCosmetics.Count > 1
                ? $"解锁 · {first} 等 {_unlockCosmetics.Count} 件"
                : $"解锁 · {first}";
        }

        /// <summary>
        /// 页签宽度按文案自然宽度撑开（只放大不缩小，与 CreateStageTag 同一口径）：
        /// 外观名长短差别很大（「薄荷汽水」4 字 vs 更长的称号），固定宽必然溢出胶囊。
        /// 必须在页签**激活后**测量——TMP 在非激活层级上算 preferredWidth 不可靠（可能得 0）。
        /// 上限由构建时算好的「不与左侧升级页签相撞」的可用宽兜住。
        /// </summary>
        private void FitUnlockBadgeWidth()
        {
            if (_unlockBadgeRoot == null || _unlockText == null || _unlockBadgeBaseWidth <= 0f) return;

            float needed = Mathf.Ceil(_unlockText.GetPreferredValues(_unlockText.text).x) + 28f;
            float maxWidth = Mathf.Max(_unlockBadgeBaseWidth, _unlockBadgeMaxWidth);
            float width = Mathf.Clamp(needed, _unlockBadgeBaseWidth, maxWidth);
            _unlockBadgeRoot.sizeDelta = new Vector2(width, _unlockBadgeRoot.sizeDelta.y);
        }

        /// <summary>
        /// 弹出「解锁」页签：经验条走完（或升级徽章弹出）之后 0.15s 顺延，
        /// 不打断经验条动画——它是奖励补充，不该和成长数字抢节奏。
        /// </summary>
        private void ShowUnlockBadge()
        {
            if (_unlockBadgeShown || _unlockBadgeRoot == null || _unlockCosmetics.Count == 0) return;
            _unlockBadgeShown = true;
            _unlockBadgeRoot.gameObject.SetActive(true);
            FitUnlockBadgeWidth(); // 激活后再量文案宽（非激活层级上 TMP 量不出真实宽度）
            if (_unlockMotion != null)
            {
                _unlockMotion.SnapHome();
                _unlockMotion.PlayIn(0.05f);
            }
        }

        private void SetRecordTagVisible(int index, bool visible)
        {
            if (index < 0 || index >= _recordTagRoots.Length) return;
            if (_recordTagRoots[index] != null) _recordTagRoots[index].gameObject.SetActive(visible);
        }

        /// <summary>
        /// 勋章条填充：按 DisplayOrder 抽取本地玩家勋章写入 chips（最多 4 枚），
        /// 溢出进「+N」，并让首枚勋章的幽默描述上屏。
        /// </summary>
        private void PopulateMedalChips()
        {
            AwardType[] extracted = new AwardType[8]; // 结算期一次性分配
            int count = MatchAwards.ExtractAwards(_localAwards, extracted);

            int shown = Mathf.Min(count, _medalChipRoots.Length);
            for (int i = 0; i < shown; i++)
            {
                if (_medalChipRoots[i] == null || _medalChipTexts[i] == null) continue;

                // 不足 4 枚时整组重新居中：剩余空槽不留在右侧。
                float chipW = _medalChipRoots[i].sizeDelta.x;
                float stride = chipW + 12f;
                float groupWidth = shown * chipW + (shown - 1) * 12f;
                float x = -groupWidth * 0.5f + chipW * 0.5f + i * stride;
                var chipRt = _medalChipRoots[i];
                chipRt.anchoredPosition = new Vector2(x, chipRt.anchoredPosition.y);

                _medalChipRoots[i].gameObject.SetActive(true);
                string title = MatchAwards.GetTitle(extracted[i]);
                _medalChipTexts[i].text = title;
                TMP_FontAsset font = UIThemeSO.Resolve(uiTheme).ResolveFont(false, title);
                if (font != null) _medalChipTexts[i].font = font;

                var chipShape = _medalChipRoots[i].GetComponent<UIInkShapeGraphic>();
                if (chipShape != null) chipShape.color = MatchAwards.GetBadgeColor(extracted[i]);
            }
            for (int i = shown; i < _medalChipRoots.Length; i++)
            {
                if (_medalChipRoots[i] != null) _medalChipRoots[i].gameObject.SetActive(false);
            }

            // 首枚勋章的幽默描述（DisplayOrder 已按 MVP → 涂地王 → … 排好优先级）；勋章溢出并入同行。
            if (_medalDescText != null)
            {
                if (count > 0)
                {
                    string desc = MatchAwards.GetDescription(extracted[0]);
                    if (count > shown) desc += $" · 还有 {count - shown} 枚勋章";
                    _medalDescText.gameObject.SetActive(true);
                    _medalDescText.text = desc;
                    TMP_FontAsset font = UIThemeSO.Resolve(uiTheme).ResolveFont(false, desc);
                    if (font != null) _medalDescText.font = font;
                }
                else
                {
                    _medalDescText.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 战报卡等级读数：只写「等级 N」，段位名与经验分数不再上屏。
        /// </summary>
        private void SetLevelDisplay(int level)
        {
            if (_xpLevelText != null) _xpLevelText.text = $"等级 {level}";
        }

        private void TriggerLevelUpBadge(int newLvl)
        {
            if (_levelUpBadgeRoot == null) return;
            _levelUpBadgeRoot.gameObject.SetActive(true);
            if (_levelUpText != null)
            {
                _levelUpText.text = $"升级 · 等级 {newLvl}";
            }
            if (_levelUpMotion != null)
            {
                _levelUpMotion.SnapHome();
                _levelUpMotion.PlayIn(0.05f);
            }
        }

        /// <summary>
        /// 写判定牌内容与语义色（胜利金 / 失败灰蓝）。只写内容、不负责显示：
        /// 正常入场由 PlayResultEntrance 与读数板 / 战报卡同批 pop-in，
        /// 这样一开场就能读到正确的胜负，不会先亮「胜利」再改。
        /// </summary>
        private void PrepareVictoryBadge(bool playerWon)
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
                string text = playerWon ? "胜利" : "失败";
                victoryBadgeText.text = text;
                Color fill = playerWon ? ComicStyle.Yellow : ComicStyle.Paper;
                var font = resolved.ResolveFont(true, text);
                if (font != null) victoryBadgeText.font = font;

                // 双层描边字的填充色只能走 UIChunkyText：直接改 TMP 颜色会把底层的
                // 墨色描边/硬影层也染成填充色，两层黄字错开 4px 就会读成「重影」。
                if (_victoryBadgeChunky != null)
                {
                    _victoryBadgeChunky.SetFillColor(fill);
                }
                else
                {
                    victoryBadgeText.color = fill;
                }
            }
        }

        /// <summary>
        /// 兜底显示：只有首帧就直接处于 Result（没走 PhaseChanged、入场没播）时才调用，
        /// 此时自己补一次 pop-in；正常路径下判定牌已在入场批次里出现过，绝不再重复播放。
        /// </summary>
        private void TriggerVictoryBadge(bool playerWon)
        {
            PrepareVictoryBadge(playerWon);
            if (victoryBadgeRoot == null) return;

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
