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
    /// 战斗 HUD 控制器：负责顶部双方覆盖率比拼条、右上角实时涂地小地图、底部墨水槽（含手雷刻度线）、生命条与大招充能提示。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Header("顶部覆盖率与状态栏")]
        [SerializeField] private Image teamABarFill;
        [SerializeField] private Image teamBBarFill;
        [SerializeField] private Text teamAPercentText;
        [SerializeField] private Text teamBPercentText;
        [SerializeField] private Text centerStatusText;

        [Header("底部主角状态与技能槽")]
        [SerializeField] private Image inkBarFill;
        [SerializeField] private Image healthBarFill;
        [SerializeField] private Image specialBarFill;
        [SerializeField] private Text inkStateText;
        [SerializeField] private Text specialStateText;

        [Header("右上角实时涂地小地图")]
        [SerializeField] private RawImage minimapRawImage;
        [SerializeField] private RectTransform minimapContainer;
        [SerializeField] private RectTransform playerMarkerDot;

        [Header("实时浮雕小地图与击杀播报（P4-D）")]
        [SerializeField] private MinimapView minimapView;
        [SerializeField] private KillFeedView killFeedView;
        [SerializeField] private bool autoBuildMinimapAndKillFeed = true;

        public MinimapView Minimap => minimapView;
        public KillFeedView KillFeed => killFeedView;

        [Header("武器专属屏幕准星")]
        [SerializeField] private WeaponReticleUI weaponReticle;

        public WeaponReticleUI WeaponReticle => weaponReticle;

        [Header("右上角墨水计量管与大招液体球（P4-C）")]
        [SerializeField] private UIThemeSO uiTheme;
        [SerializeField] private HudGaugeStyleSO gaugeStyle;
        [SerializeField] private InkTankView inkTankView;
        [SerializeField] private LiquidOrbView liquidOrbView;

        [Tooltip("场景里没有这两个视图时，Awake 自动程序化生成（无需重建场景）")]
        [SerializeField] private bool autoBuildSpecialGauges = true;

        [Header("准星旁墨水罐（P42-E，取代底部墨水卡）")]
        [SerializeField] private InkTankCrosshairView inkTankCrosshairView;
        [SerializeField] private bool autoBuildInkTankCrosshair = true;

        [Header("顶部 4v4 战况条与中央墨滴计时器（P4-B）")]
        [SerializeField] private TopRosterBarView topRosterBar;
        [SerializeField] private CenterTimerView centerTimer;

        [Tooltip("场景里没有战况条或计时器时，Awake 自动程序化生成")]
        [SerializeField] private bool autoBuildTopRosterAndTimer = true;

        public TopRosterBarView TopRosterBar => topRosterBar;
        public CenterTimerView CenterTimer => centerTimer;

        [Header("踩敌墨全屏边缘墨迹提示（P18-A）")]
        [SerializeField] private InkEdgeWarningView inkEdgeWarning;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、HUD 内容之下）")]
        [SerializeField] private bool autoBuildInkEdgeWarning = true;

        public InkEdgeWarningView InkEdgeWarning => inkEdgeWarning;

        [Header("击杀确认屏幕边缘闪光（P6-B）")]
        [SerializeField] private KillFlashView killFlash;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、HUD 内容之下）")]
        [SerializeField] private bool autoBuildKillFlash = true;

        public KillFlashView KillFlash => killFlash;

        [Header("镜头墨滴与水珠特效")]
        [SerializeField] private LensInkView lensInk;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、HUD 内容之下）")]
        [SerializeField] private bool autoBuildLensInk = true;

        public LensInkView LensInk => lensInk;

        [Header("屏幕墨水漫灌与复活转场")]
        [SerializeField] private ScreenInkTransitionView screenInkTransition;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、压在 HUD 之上）")]
        [SerializeField] private bool autoBuildScreenInkTransition = true;

        public ScreenInkTransitionView ScreenInkTransition => screenInkTransition;

        [Header("大字播报 Callout（P18）")]
        [SerializeField] private MatchCalloutView matchCallout;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在 SafeArea 根）")]
        [SerializeField] private bool autoBuildMatchCallout = true;

        public MatchCalloutView MatchCallout => matchCallout;

        [Header("实时帧率角标（系统设置：显示帧率）")]
        [SerializeField] private bool autoBuildFpsBadge = true;

        [Header("个人涂地读数与 +N m² 飘字（P42-B）")]
        [SerializeField] private TurfScoreView turfScoreView;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在 SafeArea 根左上角）")]
        [SerializeField] private bool autoBuildTurfScore = true;

        public TurfScoreView TurfScore => turfScoreView;

        [Header("低血屏幕边缘暗角（P42-B）")]
        [SerializeField] private LowHealthVignetteView lowHealthVignette;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、HUD 内容之下）")]
        [SerializeField] private bool autoBuildLowHealthVignette = true;

        public LowHealthVignetteView LowHealthVignette => lowHealthVignette;

        [Header("潜入墨水屏幕浸染（对齐 INKWAVE swim 段）")]
        [SerializeField] private SubmergedInkView submergedInk;

        [Tooltip("场景里没有该视图时，Awake 自动程序化生成（挂在画布根、HUD 内容之下）")]
        [SerializeField] private bool autoBuildSubmergedInk = true;

        public SubmergedInkView SubmergedInk => submergedInk;

#if UNITY_EDITOR
        private const string EditorThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        private const string EditorGaugeStylePath = "Assets/_Project/ScriptableObjects/HudGaugeStyle.asset";
#endif

        private float _elapsedPlayTime;
        private float _lastCovA = -1f;
        private float _lastCovB = -1f;
        /// <summary>占领百分比文本刷新节拍（10Hz）：TMP 重排是 HUD 里最贵的每帧操作之一。</summary>
        private float _nextCoverageTextTime;
        /// <summary>上一次的领先方：1 = 橙队（本地队伍）领先，-1 = 蓝队领先，0 = 未知。</summary>
        private int _lastLeadSide;
        private int _lastCenterStatusSecs = -1;
        private MatchManager.Phase _lastCenterStatusPhase = (MatchManager.Phase)(-1);

        // Legacy text dirty check caches
        private bool _lastAlive = true;
        private int _lastRespawnTenths = -1;
        private int _lastRespawnTickSeconds = -1;
        private SpecialKind _lastSpecialKind = (SpecialKind)(-1);
        private bool _lastSpecialPerforming;
        private bool _lastSpecialReady;
        private int _lastSpecialChargePct = -1;

        /// <summary>生命横条填充（胶囊 UIPillImage，左起横向填充）。</summary>
        private UIPillImage _hpFill;

        /// <summary>生命条左端身份徽章（绿底墨十字 = 身份标识；告警翻红）。</summary>
        private UIInkShapeGraphic _hpBadgePill;

        /// <summary>生命条整行动效：掉血时一次 Punch（受击反馈）。</summary>
        private UISpringMotion _hpRowMotion;

        /// <summary>低血/踩墨告警闪烁相位（功能性闪烁，脱险即停，与低墨告警同款）。</summary>
        private float _hpBlinkPhase;
        private bool _hpBlinkOn;

        /// <summary>受击 Punch 节流：上一次血量与下一次允许 Punch 的时间。</summary>
        private float _lastHpForPunch = -1f;
        private float _nextHpPunchTime;

        // ── 底部双横条仪表（2026-11 二轮：大招条 + 生命条） ──────────────────
        private const string HealthCardName = "HealthCard";
        private const string KeyHintStripName = "KeyHintStrip";

        /// <summary>大招环根节点（工厂生成；旧场景自带视图时为空）。</summary>
        private RectTransform _specialCard;

        /// <summary>旧场景没有工厂横条时的兜底组根节点（只承载生命条）。</summary>
        private RectTransform _fallbackCardsRoot;

        // ── Legacy Text → TMP 升级（P19 波次2a：消灭 Arial/LegacyRuntime 烘焙文字） ──
        // 场景里烘焙的 Text 用的是 LiberationSans、无 SDF 描边、字号 13~16。
        // 运行时在原物体下补一个同尺寸 TMP 子物体，拷对齐/颜色/字号后关掉旧组件；
        // 原字段与对象名保持不变（不改场景、不改公共签名）。
        private bool _legacyTextUpgraded;
        private TMP_Text _teamAPercentTmp;
        private TMP_Text _teamBPercentTmp;
        private TMP_Text _centerStatusTmp;
        private TMP_Text _inkStateTmp;
        private TMP_Text _specialStateTmp;

        private const string LegacyTmpChildName = "TMP";

        private void Awake()
        {
            _hudCanvas = GetComponent<Canvas>();
            EnsureSafeAreaRoot();
            EnsureSpecialGauges();
            EnsureTopRosterAndTimer();
            EnsureMinimapAndKillFeed();
            // ScreenInkTransition 置于顶层（压在 HUD 之上，转场完成后关闭 Graphic）
            // KillFlash、LensInk 先建，InkEdgeWarning 最后建并独占 sibling 0
            //（保证 InkEdgeWarningPlayTests 断言其位于一切 HUD 内容之前）。
            // 潜入浸染在所有 SetAsFirstSibling 的视图之前建：同样插 sibling 0，
            // 会被低血暗角 / LensInk / InkEdgeWarning 依次顶后，最终排在低血暗角之上、
            // SafeArea 的 HUD 控件之下（不抢 InkEdge=0 / LensInk=1 的既有断言位）。
            EnsureSubmergedInk();
            EnsureScreenInkTransition();
            EnsureKillFlash();
            // 低血暗角在 LensInk 之前建：插到 sibling 0 后会把 LensInk / InkEdgeWarning
            // 依次顶后一位，最终顺序为 InkEdge=0、LensInk=1、Vignette=2、KillFlash=3
            //（InkEdgeWarningPlayTests / LensInkPlayTests 对 0 与 1 有断言）。
            EnsureLowHealthVignette();
            EnsureLensInk();
            EnsureInkEdgeWarning();
            EnsureMatchCallout();
            // 准星墨水罐先建、涂地飘字后建：飘字上飘时会掠过罐体下缘，
            // 后建者渲染在上，保证 +N m² 永远不被墨水罐挡住（用户实测反馈）。
            EnsureInkTankCrosshair();
            EnsureTurfScore();

            // 系统设置消费端：读持久化开关并订阅后续变更
            GameSettingsService.ShowFpsChanged += HandleShowFpsChanged;
            GameSettingsService.ShowMinimapChanged += HandleShowMinimapChanged;
            if (autoBuildFpsBadge)
                EnsureFpsBadge();
            ApplyFpsBadgeVisibility();
            ApplyMinimapVisibility();
        }

        /// <summary>运行时 HUD 内容根：SafeArea 容器，桌面/编辑器下等价于全屏。</summary>
        private Transform ContentRoot => _contentRoot != null ? (Transform)_contentRoot : transform;

        /// <summary>
        /// 刘海/挖孔屏横屏时把 HUD 整体收进安全区。
        /// 场景里烘焙的面板不允许手改结构，因此运行时新建一个拉伸容器，
        /// 把画布现有直接子物体整体搬进去；桌面与编辑器 safeArea = 全屏时视觉零变化。
        /// </summary>
        private void EnsureSafeAreaRoot()
        {
            if (_contentRoot != null) return;

            var safeGo = new GameObject("HudSafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);

            var safeRt = safeGo.GetComponent<RectTransform>();
            safeRt.anchorMin = Vector2.zero;
            safeRt.anchorMax = Vector2.one;
            safeRt.offsetMin = Vector2.zero;
            safeRt.offsetMax = Vector2.zero;
            // 复用触控层的 SafeAreaFitter：每帧对比 safeArea，分辨率/刘海变化后自动跟手
            safeGo.AddComponent<SafeAreaFitter>();

            // 倒序遍历边搬边改 childCount 也不会漏；跳过容器自身。
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
        /// 内容根查找：先安全区、再画布根。
        /// 回退分支用于 EditMode 测试（Awake 不执行，面板直接挂在画布根下）。
        /// </summary>
        private Transform FindHudChild(string path)
        {
            Transform found = _contentRoot != null ? _contentRoot.Find(path) : null;
            return found != null ? found : transform.Find(path);
        }

        /// <summary>
        /// P4-D：确保实时浮雕小地图与击杀播报卡片视图存在。
        /// </summary>
        private void EnsureMinimapAndKillFeed()
        {
            if (killFeedView == null) killFeedView = GetComponentInChildren<KillFeedView>(true);
            if (minimapView == null) minimapView = GetComponentInChildren<MinimapView>(true);

            if (killFeedView != null && minimapView != null) return;
            if (!autoBuildMinimapAndKillFeed) return;

#if UNITY_EDITOR
            if (uiTheme == null)
            {
                uiTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
            }
#endif

            if (killFeedView == null)
            {
                var kfGo = new GameObject("KillFeedView", typeof(RectTransform), typeof(KillFeedView));
                kfGo.transform.SetParent(ContentRoot, false);
                killFeedView = kfGo.GetComponent<KillFeedView>();
                killFeedView.SetTheme(uiTheme);
                killFeedView.EnsureBuilt();
            }

            if (minimapView == null)
            {
                // 如果场景中有旧的 MinimapFrame，挂在其上或替换之
                Transform oldFrame = FindHudChild("MinimapFrame");
                if (oldFrame != null)
                {
                    // 旧的「实时全图涂地小地图」标题与新 MAP 徽章重复（MinimapView 里还会再兜一次）
                    Transform oldTitle = oldFrame.Find("MinimapTitle");
                    if (oldTitle != null) oldTitle.gameObject.SetActive(false);

                    minimapView = oldFrame.gameObject.AddComponent<MinimapView>();
                    minimapView.SetTheme(uiTheme);
                    minimapView.EnsureBuilt();
                }
                else
                {
                    var mapGo = new GameObject("MinimapView", typeof(RectTransform), typeof(MinimapView));
                    mapGo.transform.SetParent(ContentRoot, false);
                    var rt = mapGo.GetComponent<RectTransform>();
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-20f, -20f);
                    minimapView = mapGo.GetComponent<MinimapView>();
                    minimapView.SetTheme(uiTheme);
                    minimapView.EnsureBuilt();
                }
            }
        }

        /// <summary>P4-D 显式绑定入口（用于测试/编辑器装配）。</summary>
        public void BindMinimapAndKillFeed(MinimapView map, KillFeedView feed)
        {
            minimapView = map;
            killFeedView = feed;
            if (minimapView != null && uiTheme != null) minimapView.SetTheme(uiTheme);
            if (killFeedView != null && uiTheme != null) killFeedView.SetTheme(uiTheme);
        }

        /// <summary>
        /// P4-B：确保顶部 4v4 战况条与中央墨滴计时器存在。
        /// 优先复用场景/子节点里已摆放的视图；两者都不存在时才程序化装配。
        /// </summary>
        private void EnsureTopRosterAndTimer()
        {
            if (topRosterBar == null) topRosterBar = GetComponentInChildren<TopRosterBarView>(true);
            if (centerTimer == null) centerTimer = GetComponentInChildren<CenterTimerView>(true);

            if (topRosterBar != null && centerTimer != null) return;
            if (!autoBuildTopRosterAndTimer) return;

#if UNITY_EDITOR
            if (uiTheme == null)
            {
                uiTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
            }
#endif

            if (centerTimer == null)
            {
                var timerGo = new GameObject("CenterTimerView", typeof(RectTransform), typeof(CenterTimerView));
                timerGo.transform.SetParent(ContentRoot, false);
                centerTimer = timerGo.GetComponent<CenterTimerView>();
                centerTimer.SetTheme(uiTheme);
                centerTimer.EnsureTimerBuilt();
            }

            if (topRosterBar == null)
            {
                var rosterGo = new GameObject("TopRosterBarView", typeof(RectTransform), typeof(TopRosterBarView));
                rosterGo.transform.SetParent(ContentRoot, false);
                topRosterBar = rosterGo.GetComponent<TopRosterBarView>();
                topRosterBar.SetTheme(uiTheme);
                topRosterBar.EnsureSlotsBuilt();
            }
        }

        /// <summary>P4-B 显式绑定入口（编辑器工具 / 后续场景装配 / 单元测试）。</summary>
        public void BindTopRosterAndTimer(TopRosterBarView roster, CenterTimerView timer)
        {
            topRosterBar = roster;
            centerTimer = timer;
            if (topRosterBar != null && uiTheme != null) topRosterBar.SetTheme(uiTheme);
            if (centerTimer != null && uiTheme != null) centerTimer.SetTheme(uiTheme);
        }

        /// <summary>
        /// P4-C / P14-H：确保底部横向仪表条（墨水条 + 大招条）存在。
        /// 优先复用场景/子节点里已摆放的视图；两者都不存在时才程序化装配。
        /// </summary>
        private void EnsureSpecialGauges()
        {
            if (inkTankView == null) inkTankView = GetComponentInChildren<InkTankView>(true);
            if (liquidOrbView == null) liquidOrbView = GetComponentInChildren<LiquidOrbView>(true);

            // 场景里已带视图（旧场景/预制体）时不再重复装配；
            // 新结构场景会顺带认领横条组根节点，老结构保持 _gaugeRoot = null、面板布局照常生效。
            if (liquidOrbView != null)
            {
                AdoptCardLayoutFromViews();
                return;
            }
            if (!autoBuildSpecialGauges) return;

#if UNITY_EDITOR
            // 现有主场景是在 P4-C 之前生成的，没有序列化这两个资产引用；
            // 编辑器里按路径补一次，保证运行时用的是主题配色，而不是兜底实例。
            if (uiTheme == null)
            {
                uiTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
            }

            if (gaugeStyle == null)
            {
                gaugeStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(EditorGaugeStylePath);
            }
#endif

            HudSpecialGaugeFactory.GaugeCluster cluster =
                HudSpecialGaugeFactory.BuildCluster(ContentRoot, uiTheme, gaugeStyle);

            liquidOrbView = cluster.Orb;
            inkTankView = cluster.Tank;
            _gaugeRoot = cluster.Root;
            _specialCard = cluster.SpecialCard;

        }

        /// <summary>
        /// 场景序列化了「新三卡结构」的视图（InkCard / SpecialCard）时，认领卡片与根节点，
        /// 让 <see cref="ApplyBottomPanelLayout"/> 能继续做桌面/移动两套几何。老结构不认领，零行为变化。
        /// </summary>
        private void AdoptCardLayoutFromViews()
        {
            if (_gaugeRoot != null) return;
            if (liquidOrbView == null) return;

            // 横条化后底部只有大招条 + 生命条：只需认领「SpecialCard 挂在某个横条组根节点下」
            Transform specialRoot = liquidOrbView.transform;
            if (specialRoot.name != "SpecialCard") return;
            if (!(specialRoot.parent is RectTransform cardsRoot)) return;

            _gaugeRoot = cardsRoot;
            _specialCard = specialRoot as RectTransform;
        }

        /// <summary>P4-C 显式写入主题与仪表样式（场景生成器 / 编辑器工具调用，不重建视图）。</summary>
        public void SetGaugeTheme(UIThemeSO themeAsset, HudGaugeStyleSO styleAsset)
        {
            if (themeAsset != null) uiTheme = themeAsset;
            if (styleAsset != null) gaugeStyle = styleAsset;
        }

        /// <summary>P4-C 显式绑定入口（编辑器工具 / 后续场景装配用，可只传部分参数）。</summary>
        public void BindSpecialGauges(InkTankView tank, LiquidOrbView orb,
            UIThemeSO themeAsset = null, HudGaugeStyleSO styleAsset = null)
        {
            inkTankView = tank;
            liquidOrbView = orb;
            if (themeAsset != null) uiTheme = themeAsset;
            if (styleAsset != null) gaugeStyle = styleAsset;

            if (inkTankView != null)
            {
                if (themeAsset != null) inkTankView.SetTheme(themeAsset);
                if (styleAsset != null) inkTankView.SetStyle(styleAsset);
            }

            if (liquidOrbView != null)
            {
                if (themeAsset != null) liquidOrbView.SetTheme(themeAsset);
                if (styleAsset != null) liquidOrbView.SetStyle(styleAsset);
            }

            AdoptCardLayoutFromViews();
        }

        /// <summary>P4-C 视图引用（供测试与调试面板读取）。</summary>
        public InkTankView InkTank => inkTankView;

        /// <summary>准星旁墨水罐视图（P42-E；底部墨水卡已移除，墨水量看这里）。</summary>
        public InkTankCrosshairView InkTankCrosshair => inkTankCrosshairView;

        public LiquidOrbView LiquidOrb => liquidOrbView;

        /// <summary>底部横向仪表条根节点（与底部信息条同尺寸同心，移动端适配时同步缩放）。</summary>
        public RectTransform GaugeRoot => _gaugeRoot;

        private void Start()
        {
            EnsureWeaponReticle();
            ApplyBottomStatusPanel();
            RecolorLegacyHudTexts();
            UpgradeLegacyHudTexts();
            HideLegacyGauges();
            HideLegacyMinimapTitle();
            AdaptLayoutForInputMode();
            SubscribePhaseVisibility();
        }

        [Header("阶段可见性（P11）：主菜单/结算不再露出战斗 HUD")]
        [SerializeField] private bool hideHudOutsideMatch = true;

        private Canvas _hudCanvas;
        private RectTransform _contentRoot;
        private RectTransform _gaugeRoot;
        private Vector2Int _lastScreenSize;
        private Rect _lastSafeArea;
        private MatchManager _phaseMatch;
        private bool _phaseSubscribed;

        // 系统设置消费端：FPS 角标 + 小地图开关（改动经 GameSettingsService 事件推送）
        private GameObject _fpsBadgeRoot;
        private TextMeshProUGUI _fpsBadgeText;
        private bool _showFpsBadge;
        private bool _showMinimapEnabled = true;
        private float _fpsAccumTime;
        private int _fpsAccumFrames;

        /// <summary>
        /// 战斗 HUD 只在 Countdown/Playing/Finished 显示：Ready（主菜单）与 Result（结算页）
        /// 整张画布关掉，首页不再露出小地图/阵容条/墨罐，也不会挡住菜单按钮的点击。
        /// </summary>
        private void SubscribePhaseVisibility()
        {
            _hudCanvas = GetComponent<Canvas>();
            _phaseMatch = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (_phaseMatch != null)
            {
                _phaseMatch.PhaseChanged += HandlePhaseVisibilityChanged;
                _phaseSubscribed = true;
            }
            ApplyHudPhaseVisibility();
        }

        private void OnDestroy()
        {
            if (_phaseSubscribed && _phaseMatch != null)
            {
                _phaseMatch.PhaseChanged -= HandlePhaseVisibilityChanged;
                _phaseSubscribed = false;
            }

            GameSettingsService.ShowFpsChanged -= HandleShowFpsChanged;
            GameSettingsService.ShowMinimapChanged -= HandleShowMinimapChanged;
        }

        private void HandlePhaseVisibilityChanged(MatchManager.Phase phase)
        {
            ApplyHudPhaseVisibility();

            // 倒计时阶段 = 新一局开打：复位「每局一次」的 UI 状态。
            // 否则第二局的「最后 1 分钟」横幅（CenterTimerView._bannerShown）再也不会弹。
            if (phase == MatchManager.Phase.Countdown)
            {
                if (centerTimer != null) centerTimer.ResetForMatch();
                if (inkTankView != null) inkTankView.ResetAlertState();
                if (inkTankCrosshairView != null) inkTankCrosshairView.ResetAlertState();
            }
        }

        // ---------------------------------------------------------------- 系统设置消费端（FPS 角标 / 小地图）

        private void HandleShowFpsChanged(bool show) => ApplyFpsBadgeVisibility();

        private void HandleShowMinimapChanged(bool show) => ApplyMinimapVisibility();

        private void ApplyFpsBadgeVisibility()
        {
            _showFpsBadge = GameSettingsService.ShowFps;
            if (_fpsBadgeRoot != null)
            {
                _fpsBadgeRoot.SetActive(_showFpsBadge);
                if (_showFpsBadge)
                {
                    // 若处于移动端/触控界面，FPS 避让至左上角暂停齿轮右侧 (124, -24)，避免与暂停键重叠
                    var rt = _fpsBadgeRoot.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        bool mobile = UITypography.IsMobileLike;
                        rt.anchoredPosition = mobile ? new Vector2(124f, -24f) : new Vector2(20f, -16f);
                    }
                }
            }
            else if (_fpsBadgeText != null)
            {
                _fpsBadgeText.gameObject.SetActive(_showFpsBadge);
            }
        }

        /// <summary>小地图开关：整组隐藏（新 MinimapView 与旧 RawImage 兜底路径都盖到）。</summary>
        private void ApplyMinimapVisibility()
        {
            _showMinimapEnabled = GameSettingsService.ShowMinimap;
            if (minimapView != null)
                minimapView.gameObject.SetActive(_showMinimapEnabled);
            if (minimapRawImage != null)
                minimapRawImage.gameObject.SetActive(_showMinimapEnabled);
            if (playerMarkerDot != null)
                playerMarkerDot.gameObject.SetActive(_showMinimapEnabled);
        }

        /// <summary>
        /// 左上角实时帧率角标：墨色直角小底板 + 纸白数字（几何风）。
        /// 装饰性构建，失败只 LogWarning 不阻断整局。
        /// </summary>
        private void EnsureFpsBadge()
        {
            if (_fpsBadgeText != null) return;

            try
            {
                var badgeGo = new GameObject("FpsBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(ContentRoot, false);
                var badgeRt = badgeGo.GetComponent<RectTransform>();
                badgeRt.anchorMin = new Vector2(0f, 1f);
                badgeRt.anchorMax = new Vector2(0f, 1f);
                badgeRt.pivot = new Vector2(0f, 1f);
                badgeRt.anchoredPosition = new Vector2(20f, -16f);
                badgeRt.sizeDelta = new Vector2(110f, 36f);

                var shape = badgeGo.GetComponent<UIInkShapeGraphic>();
                shape.Shape = UIInkShape.Pill; // 圆角化：FPS 徽章改胶囊
                shape.BorderWidth = 0f;
                shape.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.78f);
                shape.raycastTarget = false;

                var textGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(badgeGo.transform, false);
                var textRt = textGo.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(10f, 0f);
                textRt.offsetMax = new Vector2(-10f, 0f);

                var tmp = textGo.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = UITypography.FontSnapped(18f);
                tmp.color = ComicStyle.Paper;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.raycastTarget = false;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;

                UIThemeSO resolvedTheme = UIThemeSO.Resolve(uiTheme);
                TMP_FontAsset font = resolvedTheme != null ? resolvedTheme.ResolveFont(false, "FPS 60") : null;
                if (font != null)
                    tmp.font = font;

                _fpsBadgeRoot = badgeGo;
                _fpsBadgeText = tmp;
                ApplyFpsBadgeVisibility();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HudController] FPS 角标构建失败（不影响游戏）：{ex.Message}");
                _fpsBadgeRoot = null;
                _fpsBadgeText = null;
            }
        }

        /// <summary>每 0.5s 汇总一次 unscaled 帧数，暂停（timeScale=0）时读数依然真实。</summary>
        private void UpdateFpsBadge()
        {
            if (_fpsBadgeText == null || !_showFpsBadge)
                return;

            _fpsAccumFrames++;
            _fpsAccumTime += Time.unscaledDeltaTime;
            if (_fpsAccumTime < 0.5f)
                return;

            float fps = _fpsAccumFrames / Mathf.Max(0.0001f, _fpsAccumTime);
            _fpsAccumFrames = 0;
            _fpsAccumTime = 0f;
            _fpsBadgeText.text = $"{Mathf.RoundToInt(fps)} FPS";
        }

        private void ApplyHudPhaseVisibility()
        {
            if (!hideHudOutsideMatch || _hudCanvas == null) return;

            bool visible = true;
            var match = _phaseMatch != null ? _phaseMatch : MatchManager.Instance;
            if (match != null)
            {
                var phase = match.CurrentPhase;
                visible = phase == MatchManager.Phase.Countdown
                          || phase == MatchManager.Phase.Playing
                          || phase == MatchManager.Phase.Finished;
            }

            if (_hudCanvas.enabled != visible)
            {
                _hudCanvas.enabled = visible;
            }

            // P41 开赛扫场镜头：Countdown 前半段相机在敌方半场高空扫场，
            // HUD 内容（计时器/比分/墨罐/小地图）盖在电影镜头上会把演出毁掉。
            // 只关内容根、不关画布：墨幕转场（ScreenInkTransition）是画布根的直接子物体，必须继续渲染。
            // 进入 Playing 那一帧本方法会再次调用，内容随之恢复。
            bool hideContentForIntro = match != null
                && match.IntroCameraSeconds > 0.01f
                && match.CurrentPhase == MatchManager.Phase.Countdown
                && visible;
            if (_contentRoot != null && _contentRoot.gameObject.activeSelf == hideContentForIntro)
            {
                _contentRoot.gameObject.SetActive(!hideContentForIntro);
            }
        }

        /// <summary>
        /// 场景里烘焙的旧墨水条/大招条已被 InkTankView / LiquidOrbView 取代，
        /// 桌面端也必须隐藏，否则右侧会同时出现一根旧黑条和新墨罐（看起来像坏掉的仪表）。
        /// </summary>
        private void HideLegacyGauges()
        {
            string[] legacy =
            {
                "BottomStatusPanel/InkBarBg",
                "BottomStatusPanel/SpecialBarBg",
                "BottomStatusPanel/InkStateText",
                // 旧的顶部覆盖率面板已被 P4-B 的阵容条 + 中央计时器取代，
                // 不隐藏会与自动生成的 TopRosterBarView / CenterTimerView 三层叠在一起。
                "TopCoveragePanel",
            };

            for (int i = 0; i < legacy.Length; i++)
            {
                Transform t = FindHudChild(legacy[i]);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 场景里烘焙的旧 topPanel 文字是给深色玻璃底配的浅色（白/浅蓝/亮橙），
        /// 换到纸面底上几乎看不见。这里按名字统一压成墨色，不动场景文件。
        /// </summary>
        private void RecolorLegacyHudTexts()
        {
            var texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null) continue;

                switch (t.gameObject.name)
                {
                    case "CenterStatus":
                    case "TeamAPercent":
                    case "TeamBPercent":
                        t.color = ComicStyle.Ink;
                        break;
                    case "MinimapTitle":
                        t.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f);
                        break;
                }
            }
        }

        // ── Legacy Text → TMP 升级（P19 波次2a） ─────────────────────────────
        // 场景烘焙的 Text 是 LiberationSans（无 SDF 描边、字号 13~16），与 t-1 的
        // 「超大粗体 + 硬影」不符。运行时在原物体下补同尺寸 TMP 子物体、拷贝对齐/颜色/字号，
        // 再关掉旧组件的渲染（不禁用 GameObject），原字段、对象名与场景结构全部保留。

        /// <summary>
        /// 把 HUD 上 5 处场景烘焙的 Legacy Text 升级成 TMP：
        /// 比分/计时走 display 档，状态/键位提示走 body 档；字号按波次2a 的新阶梯写入。
        /// </summary>
        private void UpgradeLegacyHudTexts()
        {
            if (_legacyTextUpgraded) return;
            _legacyTextUpgraded = true;

            _teamAPercentTmp = EnsureTmpFromLegacy(teamAPercentText, 44f, displayFont: true);
            _teamBPercentTmp = EnsureTmpFromLegacy(teamBPercentText, 44f, displayFont: true);
            _centerStatusTmp = EnsureTmpFromLegacy(centerStatusText, 76f, displayFont: true);
            _inkStateTmp = EnsureTmpFromLegacy(inkStateText, 18f, displayFont: false);
            _specialStateTmp = EnsureTmpFromLegacy(specialStateText, 20f, displayFont: false);
            // 键鼠提示已迁到暂停菜单（HUD 底部不再显示），旧文字节点保持原生状态并整体隐藏
        }

        /// <summary>
        /// 幂等升级：在 legacy 物体下补一个铺满其 Rect 的 TMP 子物体，拷对齐/颜色/粗体，
        /// 字号走 <see cref="UITypography"/>（移动端 ×1.28），最后把旧 Text 组件 enabled = false。
        /// </summary>
        private TMP_Text EnsureTmpFromLegacy(Text legacy, float fontSize, bool displayFont)
        {
            if (legacy == null) return null;

            Transform tmpTr = legacy.transform.Find(LegacyTmpChildName);
            TextMeshProUGUI tmp = tmpTr != null ? tmpTr.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null)
            {
                var go = new GameObject(LegacyTmpChildName,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(legacy.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                tmp = go.GetComponent<TextMeshProUGUI>();
            }

            UIThemeSO themeResolved = UIThemeSO.Resolve(uiTheme);
            string content = legacy.text;

            // 字体统一走主题解析（CJK 自动切 MiSans），解析失败再落到全局兜底
            TMP_FontAsset font = themeResolved.ResolveFont(displayFont, content);
            if (font == null) font = UIThemeSO.ResolveGlobalFont(displayFont, ComicStyle.ContainsCjk(content));

            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
            tmp.fontSize = UITypography.FontSnapped(fontSize);
            tmp.fontStyle = legacy.fontStyle == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.alignment = ToTmpAlignment(legacy.alignment);
            tmp.color = legacy.color;
            tmp.text = content;
            // HUD 单行读数：不换行、不截断（TMP Truncate 会按行高把整段字符裁掉）
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            legacy.enabled = false;
            return tmp;
        }

        /// <summary>Legacy TextAnchor → TMP TextAlignmentOptions（同语义对齐）。</summary>
        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        /// <summary>同时写 legacy 与升级后的 TMP（旧字段内容保持可读，行为向后兼容）。</summary>
        private static void SetLegacyText(Text legacy, TMP_Text tmp, string text)
        {
            if (legacy != null) legacy.text = text;
            if (tmp != null) tmp.text = text;
        }

        private static void SetLegacyColor(Text legacy, TMP_Text tmp, Color color)
        {
            if (legacy != null) legacy.color = color;
            if (tmp != null) tmp.color = color;
        }

        /// <summary>旧 MinimapFrame 里烘焙的「实时全图涂地小地图」标题与新 MAP 徽章重复，显式关掉。</summary>
        private void HideLegacyMinimapTitle()
        {
            Transform frame = FindHudChild("MinimapFrame");
            if (frame == null) return;

            Transform title = frame.Find("MinimapTitle");
            if (title != null && title.gameObject.activeSelf)
            {
                title.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 底部状态区（2026-11 二轮）：旧「一个墨黑大块」整体退场，改成两条窄横条
        /// （大招条 + 生命条，各 28px 高、整组 278×64），键鼠提示条早已迁到暂停菜单。
        ///
        /// 旧面板节点和旧绑定全部保留（<c>BottomStatusPanel</c> 只当不可见容器、<c>HealthBarFill</c> 降级为
        /// 禁绘制的数据载体），因为硬性规则禁止手改 .unity，且 HudController 的 Bind 路径不能断。
        /// </summary>
        private void ApplyBottomStatusPanel()
        {
            Transform bottomPanel = FindHudChild("BottomStatusPanel");

            if (bottomPanel != null)
            {
                // 旧黑块：场景节点不删，只关绘制与射线（enabled=false + raycastTarget=false）
                DisableLegacyGraphics(bottomPanel.gameObject);
                Transform bubble = bottomPanel.Find("ComicBubble");
                if (bubble != null) DisableLegacyGraphics(bubble.gameObject);

                Transform divider = bottomPanel.Find("PanelDivider");
                if (divider != null) divider.gameObject.SetActive(false);

                // 旧墨水条 / 旧大招条 / 「大招充能 xx%」：已被准星墨罐与两个环取代，整块退场
                // （节点保留，序列化绑定仍在；失活比禁用 Graphic 更彻底，也不会再被每帧写入）
                HideLegacyChild(bottomPanel, "InkBarBg");
                HideLegacyChild(bottomPanel, "SpecialBarBg");
                HideLegacyChild(bottomPanel, "SpecialStateText");
            }

            // 键鼠提示条 / 文字已从战斗 HUD 移除（改由暂停菜单「操作说明」卡承载）：
            // 旧场景节点保留但整体隐藏，避免底部再出现一排小字。
            HideLegacyKeyHints(bottomPanel);

            ApplyBottomPanelLayout(false);
        }

        /// <summary>幂等隐藏一个遗留子节点（节点不删，绑定引用仍有效）。</summary>
        private static void HideLegacyChild(Transform parent, string childName)
        {
            if (parent == null) return;
            Transform child = parent.Find(childName);
            if (child != null && child.gameObject.activeSelf) child.gameObject.SetActive(false);
        }

        /// <summary>隐藏旧场景烘焙的底部键鼠提示（细黑条 + 文字），桌面/移动端都不再显示。</summary>
        private void HideLegacyKeyHints(Transform bottomPanel)
        {
            if (bottomPanel != null)
            {
                Transform strip = bottomPanel.Find(KeyHintStripName);
                if (strip != null && strip.gameObject.activeSelf) strip.gameObject.SetActive(false);
            }

            Transform hint = FindHudChild("BottomStatusPanel/ControlsHintText");
            if (hint != null && hint.gameObject.activeSelf) hint.gameObject.SetActive(false);
        }

        /// <summary>
        /// 底部双横条几何（桌面 / 移动两套）：<see cref="HudSpecialGaugeFactory.ApplyStatusCardsLayout"/>
        /// 是唯一入口，横条组与旧面板容器矩形保持同心；键鼠提示条不再显示。
        /// </summary>
        private void ApplyBottomPanelLayout(bool mobile)
        {
            float height = mobile
                ? HudSpecialGaugeFactory.StripHeightMobile
                : HudSpecialGaugeFactory.StripHeightDesktop;
            float bottomY = mobile
                ? HudSpecialGaugeFactory.StripBottomYMobile
                : HudSpecialGaugeFactory.StripBottomYDesktop;
            float width = mobile
                ? HudSpecialGaugeFactory.StripWidthMobile
                : HudSpecialGaugeFactory.StripWidth;
            var size = new Vector2(width, height);
            var position = new Vector2(0f, bottomY);

            Transform bottomPanel = FindHudChild("BottomStatusPanel");
            if (bottomPanel is RectTransform panelRect)
            {
                // 旧面板只剩「不可见容器」职责：与横条组同矩形，承载旧提示文字与迁移前的血条节点
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.sizeDelta = size;
                panelRect.anchoredPosition = position;
            }

            // 横条组：正常路径用工厂生成的根节点；旧场景自带视图时用兜底根节点。
            // 编辑器非 Play 的预览工具（Awake 不执行、没有视图）不建横条，避免只在预览里冒出一条孤零零的血条。
            RectTransform cardsRoot = _gaugeRoot;
            if (cardsRoot == null && (Application.isPlaying || _specialCard != null))
            {
                cardsRoot = EnsureFallbackCardsRoot();
            }

            RectTransform healthCard = cardsRoot != null ? EnsureHealthCard(cardsRoot) : null;
            if (cardsRoot != null)
            {
                HudSpecialGaugeFactory.ApplyStatusCardsLayout(cardsRoot, null, _specialCard, healthCard, mobile);
            }

            if (bottomPanel == null) return;

            // 战术分割线已被两行之间的留白取代：旧节点存在就隐藏，不再新建
            Transform divider = bottomPanel.Find("PanelDivider");
            if (divider != null) divider.gameObject.SetActive(false);

            // 复活倒计时等遗留文字：贴在横条组上方（条顶再加一行文字高度）
            Transform inkState = bottomPanel.Find("InkStateText");
            if (inkState is RectTransform inkStateRect)
            {
                inkStateRect.anchorMin = inkStateRect.anchorMax = new Vector2(0.5f, 0f);
                inkStateRect.pivot = new Vector2(0.5f, 0f);
                inkStateRect.anchoredPosition = new Vector2(0f, height + 26f);
            }

            // 键鼠提示条已从战斗 HUD 移除：旧节点保留但隐藏，底部只留两条横条
            HideLegacyKeyHints(bottomPanel);
        }

        /// <summary>关掉一块旧图形（不删节点）：停绘制 + 停吃射线。</summary>
        private static void DisableLegacyGraphics(GameObject go)
        {
            if (go == null) return;

            var graphics = go.GetComponents<Graphic>();
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) continue;
                graphics[i].enabled = false;
                graphics[i].raycastTarget = false;
            }
        }

        /// <summary>旧场景自带仪表视图、没有工厂卡片时的兜底三卡区（只承载生命卡）。</summary>
        private RectTransform EnsureFallbackCardsRoot()
        {
            if (_fallbackCardsRoot != null) return _fallbackCardsRoot;

            var go = new GameObject("StatusCardsRoot", typeof(RectTransform));
            go.transform.SetParent(ContentRoot, false);
            _fallbackCardsRoot = (RectTransform)go.transform;
            return _fallbackCardsRoot;
        }

        /// <summary>
        /// 生命横条（整行 344×34，移动端 379×38）：左端绿底墨十字徽章 + 墨黑胶囊轨道（色签硬影）
        /// + 轨道内缩 3px 的绿色胶囊填充，右端不留任何附加小件。
        /// 低血（&lt;30%）/ 踩敌墨：填充与徽章翻红、填充红↔纸白交替闪；掉血时整行一次 Punch。
        /// 场景里的 <c>HealthBarBg/HealthBarFill</c> 会被迁进来当**不可见数据载体**
        /// （<c>fillAmount</c> 的序列化写入路径保持不变），实际绘制由 <c>BarTrack/BarFill</c> 承担。
        /// 幂等：重复调用只重刷几何与外观。
        /// </summary>
        private RectTransform EnsureHealthCard(RectTransform cardsRoot)
        {
            if (cardsRoot == null) return null;

            UIThemeSO themeResolved = UIThemeSO.Resolve(uiTheme);

            RectTransform card = cardsRoot.Find(HealthCardName) as RectTransform;
            if (card == null)
            {
                var cardGo = new GameObject(HealthCardName, typeof(RectTransform));
                cardGo.transform.SetParent(cardsRoot, false);
                card = (RectTransform)cardGo.transform;
            }

            EnsureHealthBadge(card, themeResolved);
            EnsureHealthTrack(card, themeResolved);
            EnsureLegacyHealthCarrier(card);
            EnsureHealthRowMotion(card);
            return card;
        }

        /// <summary>
        /// 生命横条左端身份徽章：绿底正圆 + 墨色十字图标（<c>glyph_plus</c>）+ 3px 墨边 + 色签硬影，
        /// 与大招条左端黄底徽章左右对称——黄圆=大招、绿十字=生命，不看读数也能分清谁是谁。
        /// 幂等：节点存在就复用，只重刷外观。尺寸/移动端缩放由
        /// <see cref="HudSpecialGaugeFactory.ApplyBarRowMetrics"/> 统一处理。
        /// </summary>
        private void EnsureHealthBadge(RectTransform card, UIThemeSO themeResolved)
        {
            RectTransform badge = card.Find(HudSpecialGaugeFactory.BarBadgeName) as RectTransform;
            if (badge == null)
            {
                var badgeGo = new GameObject(HudSpecialGaugeFactory.BarBadgeName,
                    typeof(RectTransform), typeof(CanvasRenderer));
                badgeGo.transform.SetParent(card, false);
                badge = (RectTransform)badgeGo.transform;
            }

            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.sizeDelta = new Vector2(HudSpecialGaugeFactory.BarBadgeSizeDesktop,
                HudSpecialGaugeFactory.BarBadgeSizeDesktop);
            badge.anchoredPosition = new Vector2(HudSpecialGaugeFactory.BarBadgeSizeDesktop * 0.5f, 0f);

            if (_hpBadgePill == null) _hpBadgePill = badge.GetComponent<UIInkShapeGraphic>();
            if (_hpBadgePill == null) _hpBadgePill = badge.gameObject.AddComponent<UIInkShapeGraphic>();
            _hpBadgePill.raycastTarget = false;
            _hpBadgePill.Shape = UIInkShape.Pill; // 方形 rect = 正圆
            _hpBadgePill.SpeechTail = false;
            _hpBadgePill.BorderWidth = ComicStyle.BorderThin;
            _hpBadgePill.BorderColor = ComicStyle.Ink;
            _hpBadgePill.EdgeFeather = HudSpecialGaugeFactory.BarEdgeFeather;
            _hpBadgePill.HardShadow = false;
            _hpBadgePill.ShadowOffset = Vector2.zero;
            _hpBadgePill.Halftone = false;
            _hpBadgePill.GlossAlpha = 0f;
            _hpBadgePill.DrawGloss = false;
            _hpBadgePill.InnerRimAlpha = 0f;
            _hpBadgePill.color = ComicStyle.Green;
            themeResolved.ApplyHardShadow(_hpBadgePill, UIHardShadowLevel.Tag);

            // 十字图标：桌面 28px / 移动端 32px（ApplyBarRowMetrics 统一刷新），徽章变大时内缩同步跟随；取不到就隐藏，绝不留白块
            Transform iconTr = badge.Find(HudSpecialGaugeFactory.HealthBadgeIconName);
            if (iconTr == null)
            {
                var iconGo = new GameObject(HudSpecialGaugeFactory.HealthBadgeIconName,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(badge, false);
                iconTr = iconGo.transform;
            }

            var iconRect = (RectTransform)iconTr;
            float inset = Mathf.Max(0f,
                (HudSpecialGaugeFactory.BarBadgeSizeDesktop - HudSpecialGaugeFactory.BarBadgeIconSize) * 0.5f);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.offsetMin = new Vector2(inset, inset);
            iconRect.offsetMax = new Vector2(-inset, -inset);

            var icon = iconTr.GetComponent<Image>();
            if (icon == null) icon = iconTr.gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.sprite = UIIconLibrary.Get("glyph_plus");
            icon.color = ComicStyle.Ink;
            icon.enabled = icon.sprite != null;

            // 旧生命卡徽章 HpBadge 存在则整块隐藏（节点不删）
            Transform legacyBadge = card.Find("HpBadge");
            if (legacyBadge != null && legacyBadge.gameObject.activeSelf) legacyBadge.gameObject.SetActive(false);
        }

        /// <summary>
        /// 生命横条轨道 + 胶囊填充：墨黑胶囊（色签硬影）当对比底兼 2px 自动描边，
        /// 填充走 <see cref="UIPillImage"/>（按条宽自动换胶囊精灵）+ 左起横向填充。
        /// 幂等：节点存在就复用，只重刷几何。
        /// </summary>
        private void EnsureHealthTrack(RectTransform card, UIThemeSO themeResolved)
        {
            RectTransform track = card.Find(HudSpecialGaugeFactory.BarTrackName) as RectTransform;
            if (track == null)
            {
                var trackGo = new GameObject(HudSpecialGaugeFactory.BarTrackName,
                    typeof(RectTransform), typeof(CanvasRenderer));
                trackGo.transform.SetParent(card, false);
                track = (RectTransform)trackGo.transform;
            }

            track.anchorMin = track.anchorMax = new Vector2(0f, 0.5f);
            track.pivot = new Vector2(0f, 0.5f);
            track.sizeDelta = new Vector2(HudSpecialGaugeFactory.BarTrackWidthDesktop,
                HudSpecialGaugeFactory.BarTrackHeightDesktop);
            track.anchoredPosition = new Vector2(
                HudSpecialGaugeFactory.BarBadgeSizeDesktop + HudSpecialGaugeFactory.BarBadgeGap, 0f);

            var trackGraphic = track.GetComponent<UIInkShapeGraphic>();
            if (trackGraphic == null) trackGraphic = track.gameObject.AddComponent<UIInkShapeGraphic>();
            trackGraphic.raycastTarget = false;
            trackGraphic.Shape = UIInkShape.Pill;
            trackGraphic.SpeechTail = false;
            trackGraphic.BorderWidth = 0f;
            trackGraphic.BorderColor = ComicStyle.Ink;
            trackGraphic.EdgeFeather = HudSpecialGaugeFactory.BarEdgeFeather;
            trackGraphic.HardShadow = false;
            trackGraphic.ShadowOffset = Vector2.zero;
            trackGraphic.Halftone = false;
            trackGraphic.GlossAlpha = 0f;
            trackGraphic.DrawGloss = false;
            trackGraphic.InnerRimAlpha = 0f;
            trackGraphic.color = ComicStyle.Ink;
            themeResolved.ApplyHardShadow(trackGraphic, UIHardShadowLevel.Tag);

            if (_hpFill == null) _hpFill = track.Find(HudSpecialGaugeFactory.BarFillName)?.GetComponent<UIPillImage>();
            if (_hpFill == null) _hpFill = HudSpecialGaugeFactory.CreateBarFill(track, ComicStyle.Green);
            _hpFill.color = ComicStyle.Green;
        }

        /// <summary>
        /// 旧场景血条迁移：<c>HealthBarBg</c> 降级为不可见容器、<c>HealthBarFill</c> 保留为**禁绘制的数据载体**
        /// （<see cref="healthBarFill"/> 的序列化绑定与每帧 fillAmount 写入路径不得断）。
        /// 两者都摆到轨道位置上，节点不删。
        /// </summary>
        private void EnsureLegacyHealthCarrier(RectTransform card)
        {
            Transform track = card.Find("HealthBarBg");
            if (track == null) track = FindHudChild("BottomStatusPanel/HealthBarBg");
            if (track == null)
            {
                // 场景里没有旧血条（新场景/测试）：仍然补一对不可见载体，
                // 保证 healthBarFill 的绑定路径与体检断言在两条路径下完全一致。
                var trackGo = new GameObject("HealthBarBg", typeof(RectTransform), typeof(Image));
                trackGo.transform.SetParent(card, false);
                track = trackGo.transform;
            }

            if (track.parent != card) track.SetParent(card, false);

            var trackRect = (RectTransform)track;
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 0.5f);
            trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.sizeDelta = new Vector2(HudSpecialGaugeFactory.BarTrackWidthDesktop,
                HudSpecialGaugeFactory.BarTrackHeightDesktop);
            trackRect.anchoredPosition = new Vector2(
                HudSpecialGaugeFactory.BarBadgeSizeDesktop + HudSpecialGaugeFactory.BarBadgeGap, 0f);

            var trackImage = track.GetComponent<Image>();
            if (trackImage != null)
            {
                trackImage.enabled = false;
                trackImage.raycastTarget = false;
            }

            // 旧 3px 胶囊描边：轨道自带墨底色，整块退场（节点不删）
            Transform frameTr = track.Find("HpFrame");
            if (frameTr != null && frameTr.gameObject.activeSelf) frameTr.gameObject.SetActive(false);

            Image fill = healthBarFill;
            if (fill == null)
            {
                Transform fillTr = track.Find("HealthBarFill");
                if (fillTr == null)
                {
                    var fillGo = new GameObject("HealthBarFill", typeof(RectTransform), typeof(Image));
                    fillGo.transform.SetParent(track, false);
                    fillTr = fillGo.transform;
                }
                fill = fillTr.GetComponent<Image>();
            }

            if (fill == null) return;

            if (fill.transform.parent != track) fill.transform.SetParent(track, false);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;

            // 旧横条填充 Image 只当数据载体：横条化后由 BarFill（UIPillImage）绘制，节点与绑定保留（禁绘制）
            fill.enabled = false;
            fill.raycastTarget = false;
            // 迁移场景血条时重新绑定，Update 的填充路径不能断
            if (healthBarFill == null) healthBarFill = fill;
        }

        /// <summary>生命条整行动效入口：掉血时 Punch（一次性缩放反馈），常态零开销。</summary>
        private void EnsureHealthRowMotion(RectTransform card)
        {
            if (_hpRowMotion == null) _hpRowMotion = card.GetComponent<UISpringMotion>();
            if (_hpRowMotion == null) _hpRowMotion = card.gameObject.AddComponent<UISpringMotion>();
            _hpRowMotion.SetTheme(UIThemeSO.Resolve(uiTheme));
        }

        /// <summary>
        /// 移动端布局适配：与桌面共用同一套底部双横条，只做三件事——
        /// 隐藏键鼠提示、横条组按移动端尺寸放大重排、隐藏条内附加小件（已无 Q 键帽 / 读数）。
        /// </summary>
        public void ApplyMobileLayout()
        {
            // 0. 战况条与中央计时器切到移动端尺寸（放大 8~12%）
            if (topRosterBar != null) topRosterBar.SetMobileLayout(true);
            if (centerTimer != null) centerTimer.SetMobileLayout(true);

            // 1. 隐藏底部硬编码的 PC 键鼠操作提示（细墨黑条 + 文字一起退场）
            Transform hint = FindHudChild("BottomStatusPanel/ControlsHintText");
            if (hint != null) hint.gameObject.SetActive(false);
            Transform hintStrip = FindHudChild("BottomStatusPanel/" + KeyHintStripName);
            if (hintStrip != null) hintStrip.gameObject.SetActive(false);

            // 2. 隐藏被双横条 / 准星墨罐取代的旧仪表与旧文字
            Transform inkBg = FindHudChild("BottomStatusPanel/InkBarBg");
            if (inkBg != null) inkBg.gameObject.SetActive(false);
            Transform specBg = FindHudChild("BottomStatusPanel/SpecialBarBg");
            if (specBg != null) specBg.gameObject.SetActive(false);
            Transform inkText = FindHudChild("BottomStatusPanel/InkStateText");
            if (inkText != null) inkText.gameObject.SetActive(false);

            // 3. 横条组切到移动端几何（行高 38、轨道 330×20、底边 y≈46；整组 379 宽居中，左右留给摇杆与操作键）
            ApplyBottomPanelLayout(true);

            // 4. 移动端隐藏「Q」键盘键签（触控大招键自带就绪反馈）
            HudSpecialGaugeFactory.ApplyMobileBottomLayout(inkTankView, liquidOrbView);
        }

        private void AdaptLayoutForInputMode()
        {
            bool isMobile = Application.isMobilePlatform;
            var touchUI = FindAnyObjectByType<MobileTouchUI>();
            if (touchUI != null && touchUI.IsShown)
            {
                isMobile = true;
            }

            if (isMobile)
            {
                ApplyMobileLayout();
            }
        }

        private void EnsureWeaponReticle()
        {
            if (weaponReticle == null)
            {
                weaponReticle = GetComponentInChildren<WeaponReticleUI>();
                if (weaponReticle == null)
                {
                    // 屏幕中心准星挂在 Canvas 根，而不是 HudSafeArea：
                    // 刘海/挖孔屏横屏时 SafeArea 会内缩，准星会偏离相机中心射线。
                    Canvas rootCanvas = _hudCanvas != null ? _hudCanvas : GetComponent<Canvas>();
                    Transform parent = rootCanvas != null ? rootCanvas.transform : ContentRoot;
                    var reticleGo = new GameObject("WeaponReticleUI", typeof(RectTransform));
                    reticleGo.transform.SetParent(parent, false);
                    weaponReticle = reticleGo.AddComponent<WeaponReticleUI>();
                }
            }
        }

        /// <summary>
        /// P18-A：确保踩敌墨全屏边缘墨迹视图存在。
        /// 直接挂在画布根（不进 SafeArea），并置于 sibling 0 ——
        /// 盖住 3D 场景，但在所有 HUD 文字 / 准星 / 触控按钮之下。
        /// </summary>
        private void EnsureInkEdgeWarning()
        {
            if (inkEdgeWarning == null)
            {
                inkEdgeWarning = GetComponentInChildren<InkEdgeWarningView>(true);
            }

            if (inkEdgeWarning == null)
            {
                if (!autoBuildInkEdgeWarning) return;

                var edgeGo = new GameObject("InkEdgeWarning",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                edgeGo.transform.SetParent(transform, false);
                edgeGo.transform.SetAsFirstSibling();
                inkEdgeWarning = edgeGo.AddComponent<InkEdgeWarningView>();
            }

            if (inkEdgeWarning != null)
            {
                inkEdgeWarning.EnsureBuilt();
            }
        }

        /// <summary>
        /// P6-B：确保击杀确认屏幕边缘闪光视图存在。
        /// 与 InkEdgeWarning 同款层级约定——直接挂在画布根（不进 SafeArea）并置于 sibling 0，
        /// 盖住 3D 场景，但在所有 HUD 文字 / 准星 / 触控按钮之下；由 KillFlashView 自行订阅击杀事件。
        /// </summary>
        private void EnsureKillFlash()
        {
            if (killFlash == null)
            {
                killFlash = GetComponentInChildren<KillFlashView>(true);
            }

            if (killFlash == null)
            {
                if (!autoBuildKillFlash) return;

                var flashGo = new GameObject("KillFlash",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                flashGo.transform.SetParent(transform, false);
                flashGo.transform.SetAsFirstSibling();
                killFlash = flashGo.AddComponent<KillFlashView>();
            }

            if (killFlash != null)
            {
                killFlash.EnsureBuilt();
            }
        }

        /// <summary>P6-B 显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindKillFlash(KillFlashView view)
        {
            killFlash = view;
            if (killFlash != null) killFlash.EnsureBuilt();
        }

        /// <summary>P18-A 显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindInkEdgeWarning(InkEdgeWarningView view)
        {
            inkEdgeWarning = view;
            if (inkEdgeWarning != null) inkEdgeWarning.EnsureBuilt();
        }

        private void EnsureLensInk()
        {
            if (lensInk == null)
            {
                lensInk = GetComponentInChildren<LensInkView>(true);
            }

            if (lensInk == null)
            {
                if (!autoBuildLensInk) return;

                var lensGo = new GameObject("LensInk",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lensGo.transform.SetParent(transform, false);
                lensGo.transform.SetAsFirstSibling();
                lensInk = lensGo.AddComponent<LensInkView>();
            }

            if (lensInk != null)
            {
                lensInk.EnsureBuilt();
            }
        }

        private void EnsureScreenInkTransition()
        {
            if (screenInkTransition == null)
            {
                screenInkTransition = GetComponentInChildren<ScreenInkTransitionView>(true);
            }

            if (screenInkTransition == null)
            {
                if (!autoBuildScreenInkTransition) return;

                var transGo = new GameObject("ScreenInkTransition",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                transGo.transform.SetParent(transform, false);
                transGo.transform.SetAsLastSibling();
                screenInkTransition = transGo.AddComponent<ScreenInkTransitionView>();
            }

            if (screenInkTransition != null)
            {
                screenInkTransition.EnsureBuilt();
            }
        }

        public void BindLensInk(LensInkView view)
        {
            lensInk = view;
            if (lensInk != null) lensInk.EnsureBuilt();
        }

        public void BindScreenInkTransition(ScreenInkTransitionView view)
        {
            screenInkTransition = view;
            if (screenInkTransition != null) screenInkTransition.EnsureBuilt();
        }

        private void EnsureMatchCallout()
        {
            if (matchCallout == null)
            {
                matchCallout = GetComponentInChildren<MatchCalloutView>(true);
            }

            if (matchCallout == null)
            {
                if (!autoBuildMatchCallout) return;

                var calloutGo = new GameObject("MatchCalloutView",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup), typeof(MatchCalloutView));
                calloutGo.transform.SetParent(ContentRoot, false);
                matchCallout = calloutGo.GetComponent<MatchCalloutView>();
            }

            if (matchCallout != null)
            {
                if (uiTheme != null) matchCallout.SetTheme(uiTheme);
                matchCallout.EnsureBuilt();
            }
        }

        public void BindMatchCallout(MatchCalloutView view)
        {
            matchCallout = view;
            if (matchCallout != null)
            {
                if (uiTheme != null) matchCallout.SetTheme(uiTheme);
                matchCallout.EnsureBuilt();
            }
        }

        /// <summary>
        /// P42-D：确保涂地飘字层存在（准星右下的 +N m² 飘字，无常驻读数卡）。
        /// 数据全部来自 MatchStatsService，本视图只负责显示，不参与任何统计。
        /// </summary>
        private void EnsureTurfScore()
        {
            if (turfScoreView == null) turfScoreView = GetComponentInChildren<TurfScoreView>(true);
            if (turfScoreView == null)
            {
                if (!autoBuildTurfScore) return;

                var turfGo = new GameObject("TurfScoreView",
                    typeof(RectTransform));
                turfGo.transform.SetParent(ContentRoot, false);
                turfScoreView = turfGo.AddComponent<TurfScoreView>();
            }

            if (turfScoreView != null)
            {
                turfScoreView.EnsureBuilt();
            }
        }

        /// <summary>P42-B 显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindTurfScore(TurfScoreView view)
        {
            turfScoreView = view;
            if (turfScoreView != null) turfScoreView.EnsureBuilt();
        }

        /// <summary>
        /// P42-E：确保准星旁墨水罐存在（竖罐挂在准星右侧，取代底部墨水卡）。
        /// 数据来自 UpdateSpecialGauges 的同一来源；本视图只做显示与低墨告警。
        /// </summary>
        private void EnsureInkTankCrosshair()
        {
            if (inkTankCrosshairView == null)
                inkTankCrosshairView = GetComponentInChildren<InkTankCrosshairView>(true);
            if (inkTankCrosshairView == null)
            {
                if (!autoBuildInkTankCrosshair) return;

                var tankGo = new GameObject("InkTankCrosshairView", typeof(RectTransform));
                tankGo.transform.SetParent(ContentRoot, false);
                inkTankCrosshairView = tankGo.AddComponent<InkTankCrosshairView>();
            }

            if (inkTankCrosshairView != null)
            {
                inkTankCrosshairView.EnsureBuilt();
            }
        }

        /// <summary>P42-E 显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindInkTankCrosshair(InkTankCrosshairView view)
        {
            inkTankCrosshairView = view;
            if (inkTankCrosshairView != null) inkTankCrosshairView.EnsureBuilt();
        }

        /// <summary>
        /// P42-B：确保低血屏幕边缘暗角视图存在。
        /// 与 InkEdgeWarning / KillFlash 同款层级约定——直接挂在画布根（不进 SafeArea）
        /// 并置于 sibling 0 附近，盖住 3D 场景但在所有 HUD 文字 / 准星 / 触控按钮之下。
        /// </summary>
        private void EnsureLowHealthVignette()
        {
            if (lowHealthVignette == null)
            {
                lowHealthVignette = GetComponentInChildren<LowHealthVignetteView>(true);
            }

            if (lowHealthVignette == null)
            {
                if (!autoBuildLowHealthVignette) return;

                var vignetteGo = new GameObject("LowHealthVignette",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                vignetteGo.transform.SetParent(transform, false);
                vignetteGo.transform.SetAsFirstSibling();
                lowHealthVignette = vignetteGo.AddComponent<LowHealthVignetteView>();
            }

            if (lowHealthVignette != null) lowHealthVignette.EnsureBuilt();
        }

        /// <summary>P42-B 显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindLowHealthVignette(LowHealthVignetteView view)
        {
            lowHealthVignette = view;
            if (lowHealthVignette != null) lowHealthVignette.EnsureBuilt();
        }

        /// <summary>
        /// 潜入墨水屏幕浸染：直接挂在画布根（不进 SafeArea），
        /// 与低血暗角同款层级约定——盖住 3D 场景，但在所有 HUD 文字 / 准星 / 触控按钮之下。
        /// </summary>
        private void EnsureSubmergedInk()
        {
            if (submergedInk == null)
            {
                submergedInk = GetComponentInChildren<SubmergedInkView>(true);
            }

            if (submergedInk == null)
            {
                if (!autoBuildSubmergedInk) return;

                var submergedGo = new GameObject("SubmergedInk",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                submergedGo.transform.SetParent(transform, false);
                submergedGo.transform.SetAsFirstSibling();
                submergedInk = submergedGo.AddComponent<SubmergedInkView>();
            }

            if (submergedInk != null) submergedInk.EnsureBuilt();
        }

        /// <summary>潜入浸染显式绑定入口（编辑器装配 / 测试用）。</summary>
        public void BindSubmergedInk(SubmergedInkView view)
        {
            submergedInk = view;
            if (submergedInk != null) submergedInk.EnsureBuilt();
        }

        public void BindReferences(
            Image aFill,
            Image bFill,
            Text aText,
            Text bText,
            Text statusText,
            Image inkFill,
            Image hpFill,
            Image specFill,
            Text inkText,
            Text specText,
            RawImage mapRaw,
            RectTransform mapRect,
            RectTransform playerDot)
        {
            teamABarFill = aFill;
            teamBBarFill = bFill;
            teamAPercentText = aText;
            teamBPercentText = bText;
            centerStatusText = statusText;
            inkBarFill = inkFill;
            healthBarFill = hpFill;
            specialBarFill = specFill;
            inkStateText = inkText;
            specialStateText = specText;
            minimapRawImage = mapRaw;
            minimapContainer = mapRect;
            playerMarkerDot = playerDot;
        }

        private void Update()
        {
            if (_hudCanvas == null || !_hudCanvas.enabled) return;

            // 分辨率/安全区变化（横竖屏、分屏、编辑器改 Game 视图）后重套移动端布局。
            // ApplyMobileLayout 是幂等的：只隐藏旧元素、收窄底部面板，重复调用安全。
            if (Screen.width != _lastScreenSize.x || Screen.height != _lastScreenSize.y || Screen.safeArea != _lastSafeArea)
            {
                _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
                _lastSafeArea = Screen.safeArea;
                AdaptLayoutForInputMode();
            }

            _elapsedPlayTime += Time.deltaTime;
            UpdateCoverageHeader();
            UpdatePlayerStatusBars();
            UpdateMinimap();
            UpdateFpsBadge();
        }

        private void UpdateCoverageHeader()
        {
            var surface = InkSurfaceController.Instance;
            float covA = surface != null ? surface.GetCoveragePercent(Team.TeamA) : 0f;
            float covB = surface != null ? surface.GetCoveragePercent(Team.TeamB) : 0f;

            if (teamABarFill != null) teamABarFill.fillAmount = Mathf.Clamp01(covA / 100f);
            if (teamBBarFill != null) teamBBarFill.fillAmount = Mathf.Clamp01(covB / 100f);

            // 文本 10Hz 节流：0.05% 的数值阈值在战斗中几乎每帧都会跨过，旧版等于每帧重建 TMP 网格。
            bool textTick = Time.unscaledTime >= _nextCoverageTextTime;
            if (textTick)
                _nextCoverageTextTime = Time.unscaledTime + 0.1f;

            if (textTick && teamAPercentText != null && Mathf.Abs(covA - _lastCovA) >= 0.05f)
            {
                _lastCovA = covA;
                SetLegacyText(teamAPercentText, _teamAPercentTmp, $"橙队 {covA:F1}%");
            }
            if (textTick && teamBPercentText != null && Mathf.Abs(covB - _lastCovB) >= 0.05f)
            {
                _lastCovB = covB;
                SetLegacyText(teamBPercentText, _teamBPercentTmp, $"{covB:F1}% 蓝队");
            }

            var match = MatchManager.Instance;

            // 比分反超提示（旧版完全没有声音）：领先方发生切换且差距超过 0.4% 才提示。
            // 0.4% 的滞回 + AudioManager 侧 6s 节流，避免开局互刷时刷成音墙。
            if (match != null && match.CurrentPhase == MatchManager.Phase.Playing)
            {
                int lead = covA > covB + 0.4f ? 1 : (covB > covA + 0.4f ? -1 : 0);
                if (lead != 0)
                {
                    if (_lastLeadSide != 0 && lead != _lastLeadSide && AudioManager.Instance != null)
                    {
                        // TeamA = 本地队伍（橙队）：领先给上行，被反超给下行
                        AudioManager.Instance.PlayLeadChange(lead > 0);
                    }
                    _lastLeadSide = lead;
                }
            }
            else if (match != null)
            {
                _lastLeadSide = 0; // 离开 Playing（重开/终场）后重新建立基线
            }
            if (centerTimer != null)
            {
                centerTimer.SetCoverage(covA, covB);
                if (match != null)
                {
                    bool isPlayingOrFinished = match.CurrentPhase == MatchManager.Phase.Playing ||
                                               match.CurrentPhase == MatchManager.Phase.Finished;
                    centerTimer.SetTime(match.RemainingSeconds, isPlayingOrFinished);
                }
                else
                {
                    centerTimer.SetTime(_elapsedPlayTime, true);
                }
            }

            if (centerStatusText != null)
            {
                if (match != null)
                {
                    int secs = Mathf.CeilToInt(match.RemainingSeconds);
                    bool isPlayingOrFinished = match.CurrentPhase == MatchManager.Phase.Playing || match.CurrentPhase == MatchManager.Phase.Finished;
                    if (isPlayingOrFinished)
                    {
                        if (secs != _lastCenterStatusSecs || _lastCenterStatusPhase != match.CurrentPhase)
                        {
                            _lastCenterStatusSecs = secs;
                            _lastCenterStatusPhase = match.CurrentPhase;
                            SetLegacyText(centerStatusText, _centerStatusTmp, $"{secs / 60:00}:{secs % 60:00}");
                            SetLegacyColor(centerStatusText, _centerStatusTmp, secs <= (int)Mathf.Max(1f, 30f)
                                ? new Color(1f, 0.42f, 0.35f, 1f)
                                : Color.white);
                        }
                    }
                    else
                    {
                        if (_lastCenterStatusPhase != match.CurrentPhase)
                        {
                            _lastCenterStatusSecs = -1;
                            _lastCenterStatusPhase = match.CurrentPhase;
                            SetLegacyText(centerStatusText, _centerStatusTmp, "等待开赛");
                            SetLegacyColor(centerStatusText, _centerStatusTmp, Color.white);
                        }
                    }
                }
                else
                {
                    int totalSecs = Mathf.FloorToInt(_elapsedPlayTime);
                    if (totalSecs != _lastCenterStatusSecs)
                    {
                        _lastCenterStatusSecs = totalSecs;
                        int mins = totalSecs / 60;
                        int s = totalSecs % 60;
                        SetLegacyText(centerStatusText, _centerStatusTmp, $"手感试玩场  {mins:00}:{s:00}");
                    }
                }
            }
        }

        private void UpdatePlayerStatusBars()
        {
            var player = PlayerController.LocalPlayer;
            if (player == null) return;

            var motor = player.Motor;
            var weapon = player.Weapon;
            var health = player.Health;

            float bombCost = weapon.Config != null ? weapon.Config.bombInkCost : 70f;

            if (inkBarFill != null)
            {
                inkBarFill.fillAmount = motor.Ink01;
                inkBarFill.color = motor.CurrentInk >= bombCost
                    ? new Color(1.0f, 0.50f, 0.08f, 1f)
                    : new Color(0.85f, 0.32f, 0.12f, 1f);
            }

            if (_hpFill != null || healthBarFill != null)
            {
                bool isDanger = motor.IsInEnemyInk || health.Health01 < 0.3f;

                // 低血/踩墨告警闪烁（功能性，2.2Hz，与低墨告警同款 Sin 门控；脱险即停，不做常驻循环）。
                // 右端读数芯片已下线，改为闪**填充色**：条长始终等于剩余血量，只换色不换长度，读量不受影响。
                if (isDanger)
                {
                    _hpBlinkPhase += Time.unscaledDeltaTime * 2.2f;
                    _hpBlinkOn = Mathf.Sin(_hpBlinkPhase * Mathf.PI * 2f) > 0f;
                }
                else
                {
                    _hpBlinkPhase = 0f;
                    _hpBlinkOn = false;
                }

                Color hpColor = isDanger ? ComicStyle.Red : ComicStyle.Green;
                // 告警两相：红 ↔ 纸白（都在墨黑轨道上高对比；不闪墨底，避免条看起来「断了」）
                Color hpFillColor = isDanger && _hpBlinkOn ? ComicStyle.Paper : hpColor;

                if (healthBarFill != null)
                {
                    // 旧横条 Image：只当数据载体（禁绘制），保留序列化绑定的写入路径
                    healthBarFill.fillAmount = health.Health01;
                    healthBarFill.color = hpColor;
                }

                if (_hpFill != null)
                {
                    // 横条胶囊填充：绿 → 红硬切；告警时红 ↔ 纸白 2.2Hz 闪（功能性闪烁）
                    _hpFill.fillAmount = health.Health01;
                    _hpFill.color = hpFillColor;
                }

                if (_hpBadgePill != null)
                {
                    // 左端十字徽章跟随告警翻红（绿=健康身份，红=危险）；徽章不参与闪烁，保持身份稳定
                    _hpBadgePill.color = hpColor;
                }

                // 掉血受击反馈：整行一次 Punch（0.25s 节流，回血/复活不触发）
                float hpNow = health.CurrentHealth;
                if (_hpRowMotion != null && _lastHpForPunch >= 0f && hpNow < _lastHpForPunch - 0.5f
                    && Time.unscaledTime >= _nextHpPunchTime)
                {
                    _hpRowMotion.Punch(0.05f);
                    _nextHpPunchTime = Time.unscaledTime + 0.25f;
                }
                _lastHpForPunch = hpNow;
            }

            // 复活倒计时：只在不存活时出现（存活时的「墨水 / 手雷 / HP」长串已下线——
            // 墨水看准星旁墨罐、HP 看生命横条、手雷冷却看准星下徽标，底部不再压一行小字）
            if (inkStateText != null)
            {
                bool showRespawn = !health.IsAlive;
                if (inkStateText.gameObject.activeSelf != showRespawn)
                {
                    inkStateText.gameObject.SetActive(showRespawn);
                }

                if (showRespawn)
                {
                    // 复活倒计时滴答（旧版只有文字、完全静音）：每跨过一整秒响一声，
                    // 让「还要等多久」不靠盯文字也能感知。
                    int sec = Mathf.CeilToInt(health.RespawnRemaining);
                    if (sec > 0 && sec != _lastRespawnTickSeconds && AudioManager.Instance != null)
                        AudioManager.Instance.PlayRespawnTick();
                    _lastRespawnTickSeconds = sec;

                    int tenths = Mathf.RoundToInt(health.RespawnRemaining * 10f);
                    if (_lastAlive || tenths != _lastRespawnTenths)
                    {
                        _lastAlive = false;
                        _lastRespawnTenths = tenths;
                        SetLegacyText(inkStateText, _inkStateTmp, $"复活倒计时 {health.RespawnRemaining:F1}s...");
                    }
                }
                else
                {
                    _lastRespawnTickSeconds = -1;
                    _lastAlive = true;
                }
            }

            if (specialBarFill != null)
            {
                specialBarFill.fillAmount = weapon.SpecialCharge01;
                // 满能只做纯色硬切（规范禁止正弦呼吸/循环脉冲）
                specialBarFill.color = weapon.IsSpecialReady
                    ? ComicStyle.Yellow
                    : new Color(0.25f, 0.78f, 1f, 1f);
            }

            if (specialStateText != null && specialStateText.gameObject.activeInHierarchy)
            {
                SpecialKind specialKind = weapon.ActiveSpecialKind;
                bool isPerforming = weapon.IsPerformingSpecial;
                bool isReady = weapon.IsSpecialReady;
                int chargePct = Mathf.RoundToInt(weapon.SpecialCharge01 * 100f);

                if (specialKind != _lastSpecialKind || isPerforming != _lastSpecialPerforming ||
                    isReady != _lastSpecialReady || (!isPerforming && !isReady && chargePct != _lastSpecialChargePct))
                {
                    _lastSpecialKind = specialKind;
                    _lastSpecialPerforming = isPerforming;
                    _lastSpecialReady = isReady;
                    _lastSpecialChargePct = chargePct;

                    string specialName = specialKind == SpecialKind.Storm
                        ? "墨水风暴云"
                        : "超级墨水砸地";

                    if (isPerforming)
                    {
                        SetLegacyText(specialStateText, _specialStateTmp, $">>> {specialName}释放中! <<<");
                    }
                    else if (isReady)
                    {
                        SetLegacyText(specialStateText, _specialStateTmp, $"【 大招已满！按 [Q] {specialName} 】");
                    }
                    else
                    {
                        SetLegacyText(specialStateText, _specialStateTmp,
                            $"大招充能 {(weapon.SpecialCharge01 * 100f):F0}% (涂地攒能量)");
                    }
                }
            }

            UpdateSpecialGauges(motor.CurrentInk, motor.MaxInk,
                weapon.SpecialCharge01, weapon.IsSpecialReady, weapon.ActiveSpecialKind,
                bombCost);
        }

        /// <summary>P4-C：把墨水余量、手雷耗墨刻度、大招充能与技能图标类型喂给右上角簇。</summary>
        private void UpdateSpecialGauges(float currentInk, float maxInk, float specialCharge01,
            bool specialReady, SpecialKind specialKind, float bombInkCost)
        {
            if (inkTankView != null)
            {
                inkTankView.SetInk(currentInk, maxInk);
                inkTankView.SetThreshold01(bombInkCost / Mathf.Max(1f, maxInk));
            }

            if (inkTankCrosshairView != null)
            {
                inkTankCrosshairView.SetInk(currentInk, maxInk);
                inkTankCrosshairView.SetThreshold01(bombInkCost / Mathf.Max(1f, maxInk));
            }

            if (liquidOrbView != null)
            {
                liquidOrbView.SetSpecialKind(specialKind);
                liquidOrbView.SetCharge(specialCharge01, specialReady);
            }
        }

        private void UpdateMinimap()
        {
            if (!_showMinimapEnabled) return;

            if (minimapView != null)
            {
                minimapView.UpdateMinimap();
                return;
            }

            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return;

            if (minimapRawImage != null && minimapRawImage.texture != surface.InkTexture)
            {
                minimapRawImage.texture = surface.InkTexture;
            }

            var player = PlayerController.LocalPlayer;
            if (player != null && minimapContainer != null && playerMarkerDot != null)
            {
                Vector2 origin = surface.Grid.OriginXZ;
                Vector2 size = surface.Grid.MapSize;
                Vector3 pos = player.transform.position;

                float nx = Mathf.Clamp01((pos.x - origin.x) / size.x);
                float ny = Mathf.Clamp01((pos.z - origin.y) / size.y);

                Rect rect = minimapContainer.rect;
                playerMarkerDot.anchoredPosition = new Vector2(
                    (nx - 0.5f) * rect.width,
                    (ny - 0.5f) * rect.height);
            }
        }
    }
}
