using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
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
        private bool _lastCanBomb;
        private int _lastBombCostVal = -1;

        private SpecialKind _lastSpecialKind = (SpecialKind)(-1);
        private bool _lastSpecialPerforming;
        private bool _lastSpecialReady;
        private int _lastSpecialChargePct = -1;

        private void Awake()
        {
            _hudCanvas = GetComponent<Canvas>();
            EnsureSafeAreaRoot();
            EnsureSpecialGauges();
            EnsureTopRosterAndTimer();
            EnsureMinimapAndKillFeed();
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
            // 这类情况没有仪表条根节点，_gaugeRoot 保持 null，面板布局照常生效。
            if (inkTankView != null && liquidOrbView != null) return;
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
            HideLegacyGauges();
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
        }

        private void HandlePhaseVisibilityChanged(MatchManager.Phase phase)
        {
            ApplyHudPhaseVisibility();
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

        /// <summary>
        /// 底部信息条（P14-H）：黑底 + 4px 白边的直角面板，从上到下三行——
        /// 横向墨水条 / 横向大招条（由 <see cref="HudSpecialGaugeFactory"/> 生成）、
        /// 纯平生命条、键鼠操作提示（移动端隐藏）。
        ///
        /// 生命条必须在这里改成 1×1 纯白精灵：场景烘焙的是内置九宫格 UISprite，
        /// 拉到 18px 高会露出圆角与边缘渐变（实机表现为"两头尖尖 + 中间发灰"），
        /// 几何风要求的是纯色直角块。
        /// </summary>
        private void ApplyBottomStatusPanel()
        {
            Transform bottomPanel = FindHudChild("BottomStatusPanel");
            if (bottomPanel != null)
            {
                var bg = bottomPanel.GetComponent<UIInkShapeGraphic>();
                if (bg == null)
                {
                    var img = bottomPanel.GetComponent<Image>();
                    if (img != null)
                    {
                        // 只关不掉：Image 关掉后由子物体上的面板 Graphic 接管绘制。
                        // 不能在同一物体再加 Graphic（[DisallowMultipleComponent] 会返回 null）。
                        img.enabled = false;
                        img.raycastTarget = false;
                    }

                    bg = UIInkShapeGraphic.CreateChildBackground(bottomPanel as RectTransform, "ComicBubble");
                }
                if (bg != null)
                {
                    bg.Shape = UIInkShape.SharpRect;
                    bg.CornerRadius = ComicStyle.CornerSharp;
                    bg.SpeechTail = false;
                    bg.BorderWidth = ComicStyle.Border;
                    bg.BorderColor = ComicStyle.Paper;
                    bg.HardShadow = false;
                    bg.ShadowOffset = Vector2.zero;
                    bg.ShadowColor = ComicStyle.Ink;
                    // 棋盘式黑底面板：白字 + 纯色仪表，比大白板更贴场地、不刺眼。
                    bg.color = ComicStyle.Ink;
                    bg.Halftone = false;
                    bg.GlossAlpha = 0f;
                    bg.DrawGloss = false;
                    bg.InnerRimAlpha = 0f;
                }
            }

            Transform hint = FindHudChild("BottomStatusPanel/ControlsHintText");
            if (hint != null)
            {
                var text = hint.GetComponent<Text>();
                if (text != null)
                {
                    text.text = "WASD 移动 · 鼠标 视角 · 左键 开火 · 右键/Shift 潜行 · 空格 跳跃 · E 手雷 · Q 大招 · Esc 暂停";
                    text.color = ComicStyle.Paper;
                    text.fontSize = Mathf.RoundToInt(UITypography.FontSnapped(18f));
                    text.fontStyle = FontStyle.Bold;
                }
            }

            ApplyBottomPanelLayout(false);
        }

        /// <summary>
        /// 底部面板几何（桌面 / 移动共用）：面板与仪表条同尺寸同心，行位置按锚点定义——
        /// 仪表行贴顶、生命条贴底、提示行最下。移动端只是面板变矮、提示行隐藏。
        /// </summary>
        private void ApplyBottomPanelLayout(bool mobile)
        {
            float width = HudSpecialGaugeFactory.StripWidth;
            float height = mobile
                ? HudSpecialGaugeFactory.StripHeightMobile
                : HudSpecialGaugeFactory.StripHeightDesktop;
            float bottomY = mobile
                ? HudSpecialGaugeFactory.StripBottomYMobile
                : HudSpecialGaugeFactory.StripBottomYDesktop;
            var size = new Vector2(width, height);
            var position = new Vector2(0f, bottomY);

            Transform bottomPanel = FindHudChild("BottomStatusPanel");
            if (bottomPanel is RectTransform panelRect)
            {
                // 场景里烘焙的面板是中心轴心；仪表条根节点是底边轴心。
                // 两者必须统一成「底边锚定」才能用同一个 anchoredPosition 精确重合。
                panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.sizeDelta = size;
                panelRect.anchoredPosition = position;
            }

            if (_gaugeRoot != null)
            {
                _gaugeRoot.sizeDelta = size;
                _gaugeRoot.anchoredPosition = position;
            }

            if (bottomPanel == null) return;

            Transform hpBg = bottomPanel.Find("HealthBarBg");
            if (hpBg is RectTransform hpRect)
            {
                hpRect.anchorMin = hpRect.anchorMax = new Vector2(0.5f, 0f);
                hpRect.pivot = new Vector2(0.5f, 0f);
                hpRect.sizeDelta = new Vector2(width - 48f, 18f);
                hpRect.anchoredPosition = new Vector2(0f, mobile ? 12f : 38f);

                var trackImage = hpBg.GetComponent<Image>();
                if (trackImage != null)
                {
                    trackImage.color = new Color(ComicStyle.Paper.r, ComicStyle.Paper.g, ComicStyle.Paper.b, 0.16f);
                    UIFlatSprite.MakeFlat(trackImage);
                }

                Transform hpFill = hpBg.Find("HealthBarFill");
                var fillImage = hpFill != null ? hpFill.GetComponent<Image>() : null;
                if (fillImage != null)
                {
                    // 纯平绿色直角条：无渐变、无圆角、无描边
                    fillImage.color = ComicStyle.Green;
                    UIFlatSprite.MakeFlat(fillImage, filled: true);
                }
            }

            Transform hint = FindHudChild("BottomStatusPanel/ControlsHintText");
            if (hint is RectTransform hintRect)
            {
                hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
                hintRect.pivot = new Vector2(0.5f, 0f);
                hintRect.sizeDelta = new Vector2(width - 48f, 22f);
                hintRect.anchoredPosition = new Vector2(0f, 8f);
            }
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

            // 1. 隐藏底部硬编码的 PC 键鼠操作提示
            Transform hint = FindHudChild("BottomStatusPanel/ControlsHintText");
            if (hint != null) hint.gameObject.SetActive(false);

            // 2. 隐藏被 InkTankView / LiquidOrbView 取代的旧仪表
            Transform inkBg = FindHudChild("BottomStatusPanel/InkBarBg");
            if (inkBg != null) inkBg.gameObject.SetActive(false);
            Transform specBg = FindHudChild("BottomStatusPanel/SpecialBarBg");
            if (specBg != null) specBg.gameObject.SetActive(false);
            Transform inkText = FindHudChild("BottomStatusPanel/InkStateText");
            if (inkText != null) inkText.gameObject.SetActive(false);

            // 3. 面板收矮 + 生命条与仪表行贴到新的锚点（横向条不会被手指挡住：
            //    面板宽 940 居中，左右各留下 490px 给摇杆与操作键）
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
                    var reticleGo = new GameObject("WeaponReticleUI", typeof(RectTransform), typeof(WeaponReticleUI));
                    reticleGo.transform.SetParent(ContentRoot, false);
                    weaponReticle = reticleGo.GetComponent<WeaponReticleUI>();
                }
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
                teamAPercentText.text = $"橙队 {covA:F1}%";
            }
            if (teamBPercentText != null && Mathf.Abs(covB - _lastCovB) >= 0.05f)
            {
                _lastCovB = covB;
                teamBPercentText.text = $"{covB:F1}% 蓝队";
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
                            centerStatusText.text = $"{secs / 60:00}:{secs % 60:00}";
                            centerStatusText.color = secs <= (int)Mathf.Max(1f, 30f)
                                ? new Color(1f, 0.42f, 0.35f, 1f)
                                : Color.white;
                        }
                    }
                    else
                    {
                        if (_lastCenterStatusPhase != match.CurrentPhase)
                        {
                            _lastCenterStatusSecs = -1;
                            _lastCenterStatusPhase = match.CurrentPhase;
                            centerStatusText.text = "等待开赛";
                            centerStatusText.color = Color.white;
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
                        centerStatusText.text = $"手感试玩场  {mins:00}:{s:00}";
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
                healthBarFill.color = motor.IsInEnemyInk ? ComicStyle.Red : ComicStyle.Green;
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
                        inkStateText.text = $"复活倒计时 {health.RespawnRemaining:F1}s...";
                    }
                }
                else
                {
                    int mode = motor.IsSwimming ? 1 : (motor.IsInEnemyInk ? 2 : 0);
                    int inkVal = Mathf.RoundToInt(motor.CurrentInk);
                    int hpVal = Mathf.RoundToInt(health.CurrentHealth);
                    bool canBomb = motor.CurrentInk >= bombCost;
                    int bombCostVal = Mathf.RoundToInt(bombCost);

                    if (!_lastAlive || mode != _lastInkStateMode || inkVal != _lastInkVal || hpVal != _lastHpVal ||
                        (mode == 0 && (canBomb != _lastCanBomb || bombCostVal != _lastBombCostVal)))
                    {
                        _lastAlive = true;
                        _lastInkStateMode = mode;
                        _lastInkVal = inkVal;
                        _lastHpVal = hpVal;
                        _lastCanBomb = canBomb;
                        _lastBombCostVal = bombCostVal;

                        if (mode == 1)
                        {
                            inkStateText.text = $"[潜行极速回墨] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}";
                        }
                        else if (mode == 2)
                        {
                            inkStateText.text = $"[敌方墨水减速!] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}";
                        }
                        else
                        {
                            string bombHint = canBomb
                                ? "手雷[E]就绪"
                                : $"墨水不足手雷(需{bombCost:F0})";
                            inkStateText.text = $"墨水 {motor.CurrentInk:F0}/100 ({bombHint})  |  HP {health.CurrentHealth:F0}";
                        }
                    }
                }
            }

            if (specialBarFill != null)
            {
                specialBarFill.fillAmount = weapon.SpecialCharge01;
                if (weapon.IsSpecialReady)
                {
                    float pulse = 0.78f + Mathf.Sin(Time.time * 10f) * 0.22f;
                    specialBarFill.color = new Color(1f, 0.92f * pulse, 0.15f, 1f);
                }
                else
                {
                    specialBarFill.color = new Color(0.25f, 0.78f, 1f, 1f);
                }
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
                        specialStateText.text = $">>> {specialName}释放中! <<<";
                    }
                    else if (isReady)
                    {
                        specialStateText.text = $"【 大招已满！按 [Q] {specialName} 】";
                    }
                    else
                    {
                        specialStateText.text = $"大招充能 {(weapon.SpecialCharge01 * 100f):F0}% (涂地攒能量)";
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
            }
        }

        private void UpdateMinimap()
        {
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
