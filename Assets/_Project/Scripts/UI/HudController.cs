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

#if UNITY_EDITOR
        private const string EditorThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        private const string EditorGaugeStylePath = "Assets/_Project/ScriptableObjects/HudGaugeStyle.asset";
#endif

        private float _elapsedPlayTime;
        private float _lastCovA = -1f;
        private float _lastCovB = -1f;
        private int _lastCenterStatusSecs = -1;
        private MatchManager.Phase _lastCenterStatusPhase = (MatchManager.Phase)(-1);

        // Legacy text dirty check caches
        private bool _lastAlive = true;
        private int _lastRespawnTenths = -1;
        private int _lastInkStateMode = -1; // 0: normal, 1: swim, 2: enemy
        private int _lastInkVal = -1;
        private int _lastHpVal = -1;
        private bool _lastBombReady;
        private int _lastBombCooldownTenths = -1;

        private SpecialKind _lastSpecialKind = (SpecialKind)(-1);
        private bool _lastSpecialPerforming;
        private bool _lastSpecialReady;
        private int _lastSpecialChargePct = -1;

        private UIInkShapeGraphic _hpBadgeGraphic;
        private TMP_Text _hpBadgeText;
        private TMP_Text _hpText;

        /// <summary>HP 大数字上次写入的值：只在变化时更新文本，避免每帧插值字符串（战斗热路径零 GC）。</summary>
        private int _lastHpTextValue = int.MinValue;

        // ── 底部三张状态卡（t-1 收尾波次） ──────────────────────────────────
        private const string HealthCardName = "HealthCard";
        private const string KeyHintStripName = "KeyHintStrip";

        /// <summary>墨水卡 / 大招卡（工厂生成；旧场景自带视图时为空）。</summary>
        private RectTransform _inkCard;
        private RectTransform _specialCard;

        /// <summary>旧场景没有工厂卡片时的兜底三卡区（只承载生命卡）。</summary>
        private RectTransform _fallbackCardsRoot;

        private UIInkShapeGraphic _hpCardRail;

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
            EnsureScreenInkTransition();
            EnsureKillFlash();
            EnsureLensInk();
            EnsureInkEdgeWarning();
            EnsureMatchCallout();

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
            // 新结构场景会顺带认领三卡区根节点，老结构保持 _gaugeRoot = null、面板布局照常生效。
            if (inkTankView != null && liquidOrbView != null)
            {
                AdoptCardLayoutFromViews();
                EnsureReadyHintHardShadow();
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
            _inkCard = cluster.InkCard;
            _specialCard = cluster.SpecialCard;

            // BuildCluster 的 SetActive(true) 会让 LiquidOrbView 复位 hintPill 硬影，这里补回色签档
            EnsureReadyHintHardShadow();
        }

        /// <summary>
        /// 场景序列化了「新三卡结构」的视图（InkCard / SpecialCard）时，认领卡片与根节点，
        /// 让 <see cref="ApplyBottomPanelLayout"/> 能继续做桌面/移动两套几何。老结构不认领，零行为变化。
        /// </summary>
        private void AdoptCardLayoutFromViews()
        {
            if (_gaugeRoot != null) return;
            if (inkTankView == null || liquidOrbView == null) return;

            Transform inkRoot = inkTankView.transform;
            Transform specialRoot = liquidOrbView.transform;
            if (inkRoot.name != "InkCard" || specialRoot.name != "SpecialCard") return;
            if (inkRoot.parent == null || inkRoot.parent != specialRoot.parent) return;
            if (!(inkRoot.parent is RectTransform cardsRoot)) return;

            _gaugeRoot = cardsRoot;
            _inkCard = inkRoot as RectTransform;
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
            EnsureReadyHintHardShadow();
        }

        /// <summary>P4-C 视图引用（供测试与调试面板读取）。</summary>
        public InkTankView InkTank => inkTankView;

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
        private UIInkShapeGraphic _readyHintShadowPill;
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
        /// 底部状态区（UI 对标 t-1 新野兽派收尾波次）：旧「一个墨黑大块」整体退场，
        /// 改成三张并排的独立卡（墨水 / 大招 / 生命，纸白 + 3px 墨框 + 面板硬影 + 5px 身份色带）
        /// 以及三卡下方一条独立的细墨黑键鼠提示条（移动端隐藏）。
        ///
        /// 旧面板节点和旧绑定全部保留（<c>BottomStatusPanel</c> 只当不可见容器、<c>HealthBarFill</c> 迁进血卡），
        /// 因为硬性规则禁止手改 .unity，且 HudController 的 Bind 路径不能断。
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
            }

            // 键鼠提示条 / 文字已从战斗 HUD 移除（改由暂停菜单「操作说明」卡承载）：
            // 旧场景节点保留但整体隐藏，避免底部再出现一排小字。
            HideLegacyKeyHints(bottomPanel);

            ApplyBottomPanelLayout(false);
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
        /// 底部三卡几何（桌面 / 移动两套）：<see cref="HudSpecialGaugeFactory.ApplyStatusCardsLayout"/>
        /// 是唯一入口，三卡与旧面板矩形保持同心；键鼠提示条是三卡下方独立细条，移动端隐藏。
        /// </summary>
        private void ApplyBottomPanelLayout(bool mobile)
        {
            float height = mobile
                ? HudSpecialGaugeFactory.StripHeightMobile
                : HudSpecialGaugeFactory.StripHeightDesktop;
            float bottomY = mobile
                ? HudSpecialGaugeFactory.StripBottomYMobile
                : HudSpecialGaugeFactory.StripBottomYDesktop;
            var size = new Vector2(HudSpecialGaugeFactory.StripWidth, height);
            var position = new Vector2(0f, bottomY);

            Transform bottomPanel = FindHudChild("BottomStatusPanel");
            if (bottomPanel is RectTransform panelRect)
            {
                // 旧面板只剩「不可见容器」职责：与三卡区同矩形，承载旧提示文字与迁移前的血条节点
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.sizeDelta = size;
                panelRect.anchoredPosition = position;
            }

            // 三卡区：正常路径用工厂生成的根节点；旧场景自带视图时用兜底根节点。
            // 编辑器非 Play 的预览工具（Awake 不执行、没有视图）不建卡，避免只在预览里冒出一张孤零零的血卡。
            RectTransform cardsRoot = _gaugeRoot;
            if (cardsRoot == null && (Application.isPlaying || _inkCard != null))
            {
                cardsRoot = EnsureFallbackCardsRoot();
            }

            RectTransform healthCard = cardsRoot != null ? EnsureHealthCard(cardsRoot) : null;
            if (cardsRoot != null)
            {
                HudSpecialGaugeFactory.ApplyStatusCardsLayout(cardsRoot, _inkCard, _specialCard, healthCard, mobile);
            }

            if (bottomPanel == null) return;

            // 战术分割线已被三卡之间的留白取代：旧节点存在就隐藏，不再新建
            Transform divider = bottomPanel.Find("PanelDivider");
            if (divider != null) divider.gameObject.SetActive(false);

            // 键鼠提示条已从战斗 HUD 移除：旧节点保留但隐藏，底部只留三张状态卡
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
        /// 生命卡（380×88）：语义绿身份色带（低血量 / 踩敌墨硬切翻红）、墨底 [HP] 徽章、
        /// 220×24 绿轨道 + 3px 墨框、右侧 display 36 大数字。
        /// 场景里的 <c>HealthBarBg/HealthBarFill</c> 会被迁进来——序列化绑定保持不变。
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

            HudSpecialGaugeFactory.CardChrome chrome =
                HudSpecialGaugeFactory.BuildCardChrome(card, uiTheme, ComicStyle.Green);
            _hpCardRail = chrome.Rail;

            EnsureHealthBadge(card, themeResolved);

            // 血条：优先迁移场景里的 HealthBarBg（保留 HudController.healthBarFill 的序列化绑定）
            Transform track = card.Find("HealthBarBg");
            if (track == null) track = FindHudChild("BottomStatusPanel/HealthBarBg");
            if (track == null)
            {
                var trackGo = new GameObject("HealthBarBg", typeof(RectTransform), typeof(Image));
                trackGo.transform.SetParent(card, false);
                track = trackGo.transform;
            }
            else if (track.parent != card)
            {
                track.SetParent(card, false);
            }

            if (track is RectTransform trackRect)
            {
                float trackLeft = HudSpecialGaugeFactory.CardPadding
                                  + HudSpecialGaugeFactory.CardBadgeSize
                                  + HudSpecialGaugeFactory.CardBadgeGap;
                trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 0.5f);
                trackRect.pivot = new Vector2(0f, 0.5f);
                trackRect.sizeDelta = new Vector2(HudSpecialGaugeFactory.HealthTrackWidth,
                    HudSpecialGaugeFactory.TrackHeight);
                trackRect.anchoredPosition = new Vector2(trackLeft, 0f);

                var trackImage = track.GetComponent<Image>();
                if (trackImage != null)
                {
                    trackImage.enabled = true;
                    trackImage.raycastTarget = false;
                    // 纸白 ALT 轨道底（旧版是黑底 16% 白）：场景烘焙的九宫格精灵必须换成 1×1 纯色
                    trackImage.color = ComicStyle.PaperAlt;
                    UIFlatSprite.MakePill(trackImage); // 圆角化：胶囊轨道
                }

                // 3px 墨框（画在填充之上）
                Transform frameTr = track.Find("HpFrame");
                if (frameTr == null)
                {
                    var frameGo = new GameObject("HpFrame",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                    frameGo.transform.SetParent(track, false);
                    frameTr = frameGo.transform;
                    var fRect = (RectTransform)frameGo.transform;
                    fRect.anchorMin = Vector2.zero;
                    fRect.anchorMax = Vector2.one;
                    fRect.offsetMin = fRect.offsetMax = Vector2.zero;
                }

                var frame = frameTr.GetComponent<UIInkShapeGraphic>();
                if (frame != null)
                {
                    frame.raycastTarget = false;
                    frame.Shape = UIInkShape.Pill; // 圆角化：胶囊描边环，与轨道/填充轮廓一致
                    frame.BorderWidth = ComicStyle.BorderThin;
                    frame.BorderColor = ComicStyle.Ink;
                    frame.HardShadow = false;
                    frame.ShadowOffset = Vector2.zero;
                    frame.Halftone = false;
                    frame.GlossAlpha = 0f;
                    frame.DrawGloss = false;
                    frame.InnerRimAlpha = 0f;
                    frame.color = Color.clear;
                }

                Image fill = healthBarFill;
                if (fill == null)
                {
                    Transform fillTr = track.Find("HealthBarFill");
                    if (fillTr == null)
                    {
                        var fillGo = new GameObject("HealthBarFill", typeof(RectTransform), typeof(Image));
                        fillGo.transform.SetParent(track, false);
                        fillTr = fillGo.transform;
                        var fRect = (RectTransform)fillTr;
                        fRect.anchorMin = Vector2.zero;
                        fRect.anchorMax = Vector2.one;
                        fRect.offsetMin = fRect.offsetMax = Vector2.zero;
                    }
                    fill = fillTr.GetComponent<Image>();
                }

                if (fill != null)
                {
                    if (fill.transform.parent != track) fill.transform.SetParent(track, false);
                    var fillRect = (RectTransform)fill.transform;
                    fillRect.anchorMin = Vector2.zero;
                    fillRect.anchorMax = Vector2.one;
                    fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
                    // 填充必须画在轨道底之上、3px 墨框之下
                    fill.transform.SetSiblingIndex(0);

                    fill.enabled = true;
                    fill.raycastTarget = false;
                    fill.color = ComicStyle.Green;
                    UIFlatSprite.MakePill(fill, filled: true); // 圆角化：胶囊填充
                    // 迁移场景血条时重新绑定，Update 的填充路径不能断
                    if (healthBarFill == null) healthBarFill = fill;
                }
            }

            EnsureHealthValue(card, themeResolved);
            return card;
        }

        /// <summary>生命卡墨底徽章：4px 语义色边框 + [HP] 字（display 字体）。</summary>
        private void EnsureHealthBadge(RectTransform card, UIThemeSO themeResolved)
        {
            Transform badgeTr = card.Find("HpBadge");
            if (badgeTr == null) badgeTr = FindHudChild("BottomStatusPanel/HpBadge");
            if (badgeTr == null)
            {
                var badgeGo = new GameObject("HpBadge",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(card, false);
                badgeTr = badgeGo.transform;
            }
            else if (badgeTr.parent != card)
            {
                badgeTr.SetParent(card, false);
            }

            if (badgeTr is RectTransform badgeRect)
            {
                badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f);
                badgeRect.pivot = new Vector2(0f, 0.5f);
                badgeRect.sizeDelta = new Vector2(HudSpecialGaugeFactory.CardBadgeSize,
                    HudSpecialGaugeFactory.CardBadgeSize);
                badgeRect.anchoredPosition = new Vector2(HudSpecialGaugeFactory.CardPadding, 0f);
            }

            _hpBadgeGraphic = badgeTr.GetComponent<UIInkShapeGraphic>();
            if (_hpBadgeGraphic != null)
            {
                _hpBadgeGraphic.raycastTarget = false;
                _hpBadgeGraphic.Shape = UIInkShape.Pill; // 圆角化：44×44 徽章改正圆
                _hpBadgeGraphic.BorderWidth = HudSpecialGaugeFactory.CardBadgeBorderWidth;
                _hpBadgeGraphic.BorderColor = ComicStyle.Green;
                _hpBadgeGraphic.HardShadow = false;
                _hpBadgeGraphic.ShadowOffset = Vector2.zero;
                _hpBadgeGraphic.Halftone = false;
                _hpBadgeGraphic.GlossAlpha = 0f;
                _hpBadgeGraphic.DrawGloss = false;
                _hpBadgeGraphic.InnerRimAlpha = 0f;
                _hpBadgeGraphic.color = ComicStyle.Ink;
            }

            Transform labelTr = badgeTr.Find("Label");
            bool badgeCreated = false;
            if (labelTr == null)
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
                labelGo.transform.SetParent(badgeTr, false);
                labelTr = labelGo.transform;
                var labelRt = (RectTransform)labelTr;
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
                _hpBadgeText = labelGo.AddComponent<TextMeshProUGUI>();
                badgeCreated = true;
            }
            else
            {
                _hpBadgeText = labelTr.GetComponent<TextMeshProUGUI>();
            }

            if (_hpBadgeText != null)
            {
                float badgeFontSize = UITypography.FontSnapped(22f);
                _hpBadgeText.raycastTarget = false;
                _hpBadgeText.fontSize = badgeFontSize;
                _hpBadgeText.fontStyle = FontStyles.Bold;
                _hpBadgeText.alignment = TextAlignmentOptions.Center;
                _hpBadgeText.color = ComicStyle.Green;
                _hpBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
                _hpBadgeText.overflowMode = TextOverflowModes.Overflow;

                var labelChunky = labelTr.GetComponent<UIChunkyText>();
                if (labelChunky == null) labelChunky = labelTr.gameObject.AddComponent<UIChunkyText>();
                // 只在首次创建时写入文案：重复布局（分辨率/移动端切换）不能把「HP」重刷一遍
                if (badgeCreated)
                {
                    _hpBadgeText.text = "HP";
                }
                labelChunky.Configure(themeResolved, "HP", badgeFontSize, displayFont: true,
                    doubleLayered: false, fill: ComicStyle.Green, outlinedText: false);
            }
        }

        /// <summary>生命卡右侧大数字：display 36、墨色（低血量由 Update 硬切翻红）。</summary>
        private void EnsureHealthValue(RectTransform card, UIThemeSO themeResolved)
        {
            Transform valueTr = card.Find("HpText");
            bool valueCreated = false;
            if (valueTr == null) valueTr = FindHudChild("BottomStatusPanel/HpText");
            if (valueTr == null)
            {
                var valueGo = new GameObject("HpText", typeof(RectTransform), typeof(CanvasRenderer));
                valueGo.transform.SetParent(card, false);
                valueTr = valueGo.transform;
                _hpText = valueGo.AddComponent<TextMeshProUGUI>();
                valueCreated = true;
            }
            else if (valueTr.parent != card)
            {
                valueTr.SetParent(card, false);
            }

            if (_hpText == null) _hpText = valueTr.GetComponent<TextMeshProUGUI>();
            if (_hpText == null) return;

            var rect = (RectTransform)valueTr;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(HudSpecialGaugeFactory.HealthReadoutWidth, 48f);
            rect.anchoredPosition = new Vector2(-HudSpecialGaugeFactory.ReadoutRightPadding, 0f);

            float fontSize = HudSpecialGaugeFactory.ReadoutFontSize();
            _hpText.raycastTarget = false;
            _hpText.fontSize = fontSize;
            _hpText.fontStyle = FontStyles.Bold;
            _hpText.alignment = TextAlignmentOptions.MidlineRight;
            _hpText.color = ComicStyle.Ink;
            _hpText.textWrappingMode = TextWrappingModes.NoWrap;
            _hpText.overflowMode = TextOverflowModes.Overflow;

            var chunky = valueTr.GetComponent<UIChunkyText>();
            if (chunky == null) chunky = valueTr.gameObject.AddComponent<UIChunkyText>();
            // 只在首次创建时写入占位文案：重复布局不能把实时血量刷回 100
            if (valueCreated)
            {
                _hpText.text = "100";
            }
            string currentText = !string.IsNullOrEmpty(_hpText.text) ? _hpText.text : "100";
            chunky.Configure(themeResolved, currentText, fontSize, displayFont: true,
                doubleLayered: false, fill: ComicStyle.Ink, outlinedText: false);
        }

        /// <summary>
        /// 移动端布局适配：与桌面共用同一块底部信息条，只做三件事——
        /// 隐藏键鼠提示、把面板收矮、把「Q 释放」提示交给触控大招键。
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

            // 2. 隐藏被 InkTankView / LiquidOrbView 取代的旧仪表
            Transform inkBg = FindHudChild("BottomStatusPanel/InkBarBg");
            if (inkBg != null) inkBg.gameObject.SetActive(false);
            Transform specBg = FindHudChild("BottomStatusPanel/SpecialBarBg");
            if (specBg != null) specBg.gameObject.SetActive(false);
            Transform inkText = FindHudChild("BottomStatusPanel/InkStateText");
            if (inkText != null) inkText.gameObject.SetActive(false);

            // 3. 三卡收矮、间距 12、底边 y≈46（三卡区宽 1076 居中，左右各留 422px 给摇杆与操作键）
            ApplyBottomPanelLayout(true);

            // 4. 移动端隐藏「Q 释放」键盘提示
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
            if (teamAPercentText != null && Mathf.Abs(covA - _lastCovA) >= 0.05f)
            {
                _lastCovA = covA;
                SetLegacyText(teamAPercentText, _teamAPercentTmp, $"橙队 {covA:F1}%");
            }
            if (teamBPercentText != null && Mathf.Abs(covB - _lastCovB) >= 0.05f)
            {
                _lastCovB = covB;
                SetLegacyText(teamBPercentText, _teamBPercentTmp, $"{covB:F1}% 蓝队");
            }

            var match = MatchManager.Instance;
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

            if (healthBarFill != null)
            {
                healthBarFill.fillAmount = health.Health01;
                bool isDanger = motor.IsInEnemyInk || health.Health01 < 0.3f;
                Color hpColor = isDanger ? ComicStyle.Red : ComicStyle.Green;
                healthBarFill.color = hpColor;

                // 生命卡：身份色带 / 徽章边框 / [HP] 字一起硬切翻红（<30% 或踩敌墨）
                if (_hpCardRail != null)
                {
                    _hpCardRail.color = hpColor;
                }
                if (_hpBadgeGraphic != null)
                {
                    _hpBadgeGraphic.BorderColor = hpColor;
                }
                if (_hpBadgeText != null)
                {
                    _hpBadgeText.color = hpColor;
                }
                if (_hpText != null)
                {
                    // 只在数值真的变了才写文本：旧实现每帧插值建字符串（战斗热路径持续 GC）
                    int hpValue = Mathf.CeilToInt(health.CurrentHealth);
                    if (hpValue != _lastHpTextValue)
                    {
                        _lastHpTextValue = hpValue;
                        _hpText.text = hpValue.ToString();
                    }
                    // 纸白卡片：常态墨色大数字保证对比度；告警时才翻红
                    _hpText.color = isDanger ? ComicStyle.Red : ComicStyle.Ink;
                }
            }

            if (inkStateText != null && inkStateText.gameObject.activeInHierarchy)
            {
                if (!health.IsAlive)
                {
                    int tenths = Mathf.RoundToInt(health.RespawnRemaining * 10f);
                    if (!_lastAlive || tenths != _lastRespawnTenths)
                    {
                        _lastAlive = false;
                        _lastRespawnTenths = tenths;
                        SetLegacyText(inkStateText, _inkStateTmp, $"复活倒计时 {health.RespawnRemaining:F1}s...");
                    }
                }
                else
                {
                    int mode = motor.IsSwimming ? 1 : (motor.IsInEnemyInk ? 2 : 0);
                    int inkVal = Mathf.RoundToInt(motor.CurrentInk);
                    int hpVal = Mathf.RoundToInt(health.CurrentHealth);
                    bool bombReady = weapon.BombCooldownRemaining <= 0.001f;
                    int bombCdTenths = Mathf.CeilToInt(weapon.BombCooldownRemaining * 10f);

                    if (!_lastAlive || mode != _lastInkStateMode || inkVal != _lastInkVal || hpVal != _lastHpVal ||
                        (mode == 0 && (bombReady != _lastBombReady || bombCdTenths != _lastBombCooldownTenths)))
                    {
                        _lastAlive = true;
                        _lastInkStateMode = mode;
                        _lastInkVal = inkVal;
                        _lastHpVal = hpVal;
                        _lastBombReady = bombReady;
                        _lastBombCooldownTenths = bombCdTenths;

                        if (mode == 1)
                        {
                            SetLegacyText(inkStateText, _inkStateTmp,
                                $"[潜行极速回墨] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}");
                        }
                        else if (mode == 2)
                        {
                            SetLegacyText(inkStateText, _inkStateTmp,
                                $"[敌方墨水减速!] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}");
                        }
                        else
                        {
                            string bombHint = bombReady
                                ? "手雷[E]就绪"
                                : $"手雷冷却 {weapon.BombCooldownRemaining:F1}s";
                            SetLegacyText(inkStateText, _inkStateTmp,
                                $"墨水 {motor.CurrentInk:F0}/100 ({bombHint})  |  HP {health.CurrentHealth:F0}");
                        }
                    }
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

            if (liquidOrbView != null)
            {
                liquidOrbView.SetSpecialKind(specialKind);
                liquidOrbView.SetCharge(specialCharge01, specialReady);
                EnsureReadyHintHardShadow();
            }
        }

        /// <summary>
        /// 满能提示块 = 色签档硬影 (2,-3)。
        /// LiquidOrbView 的满能状态刷新会把 hintPill 的硬影复位成 false，
        /// 这里在每次喂充能后幂等补回（值相同不触发重绘，零 GC）。
        /// </summary>
        private void EnsureReadyHintHardShadow()
        {
            if (_readyHintShadowPill == null && liquidOrbView != null)
            {
                Transform hint = liquidOrbView.transform.Find("ReadyHint");
                if (hint != null) _readyHintShadowPill = hint.GetComponent<UIInkShapeGraphic>();
            }

            if (_readyHintShadowPill == null) return;
            UIThemeSO.Resolve(uiTheme).ApplyHardShadow(_readyHintShadowPill, UIHardShadowLevel.Tag);
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
