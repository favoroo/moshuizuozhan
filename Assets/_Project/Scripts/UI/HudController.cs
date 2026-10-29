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

        private void Awake()
        {
            EnsureSpecialGauges();
            EnsureTopRosterAndTimer();
            EnsureMinimapAndKillFeed();
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
                kfGo.transform.SetParent(transform, false);
                killFeedView = kfGo.GetComponent<KillFeedView>();
                killFeedView.SetTheme(uiTheme);
                killFeedView.EnsureBuilt();
            }

            if (minimapView == null)
            {
                // 如果场景中有旧的 MinimapFrame，挂在其上或替换之
                Transform oldFrame = transform.Find("MinimapFrame");
                if (oldFrame != null)
                {
                    minimapView = oldFrame.gameObject.AddComponent<MinimapView>();
                    minimapView.SetTheme(uiTheme);
                    minimapView.EnsureBuilt();
                }
                else
                {
                    var mapGo = new GameObject("MinimapView", typeof(RectTransform), typeof(MinimapView));
                    mapGo.transform.SetParent(transform, false);
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
                timerGo.transform.SetParent(transform, false);
                centerTimer = timerGo.GetComponent<CenterTimerView>();
                centerTimer.SetTheme(uiTheme);
                centerTimer.EnsureTimerBuilt();
            }

            if (topRosterBar == null)
            {
                var rosterGo = new GameObject("TopRosterBarView", typeof(RectTransform), typeof(TopRosterBarView));
                rosterGo.transform.SetParent(transform, false);
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
        /// P4-C：确保右上角墨水计量管与大招液体球存在。
        /// 优先复用场景/子节点里已摆放的视图；两者都不存在时才程序化装配。
        /// </summary>
        private void EnsureSpecialGauges()
        {
            if (inkTankView == null) inkTankView = GetComponentInChildren<InkTankView>(true);
            if (liquidOrbView == null) liquidOrbView = GetComponentInChildren<LiquidOrbView>(true);

            if (inkTankView != null || liquidOrbView != null) return;
            if (!autoBuildSpecialGauges) return;

#if UNITY_EDITOR
            // 现有主场景是在 P4-C 之前生成的，没有序列化这两个资产引用；
            // 编辑器里按路径补一次，保证运行时用的是 Titan One 字体与主题配色，而不是兜底实例。
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
                HudSpecialGaugeFactory.BuildCluster(transform, uiTheme, gaugeStyle);

            liquidOrbView = cluster.Orb;
            inkTankView = cluster.Tank;
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

        private void Start()
        {
            EnsureWeaponReticle();
            ApplyThirdPersonControlsHint();
            AdaptLayoutForInputMode();
        }

        /// <summary>
        /// P9：把场景里烘焙的旧俯视/双摇杆操作提示更新为第三人称键鼠说明。
        /// 移动端会在 <see cref="ApplyMobileLayout"/> 中直接隐藏该文本。
        /// </summary>
        private void ApplyThirdPersonControlsHint()
        {
            Transform hint = transform.Find("BottomStatusPanel/ControlsHintText");
            if (hint == null)
                return;

            var text = hint.GetComponent<Text>();
            if (text != null)
            {
                text.text = "操作：[WASD]移动  [鼠标]转视角  [左键]开火  [右键/Shift]己墨潜行  [空格]跳跃  [E/中键]手雷  [Q]大招  [Esc]暂停";
            }
        }

        /// <summary>
        /// 移动端布局适配：隐藏 PC 键鼠操作说明（[WASD]移动...）及与右上角重复的旧墨水/大招表，
        /// 将底部面板精简为居中紧凑血条，彻底释放下半屏视野与触控操作空间。
        /// </summary>
        public void ApplyMobileLayout()
        {
            // 1. 隐藏底部硬编码的 PC 键鼠操作提示
            Transform hint = transform.Find("BottomStatusPanel/ControlsHintText");
            if (hint != null) hint.gameObject.SetActive(false);

            // 2. 隐藏与右上角 InkTankView / LiquidOrbView 重复的旧仪表
            Transform inkBg = transform.Find("BottomStatusPanel/InkBarBg");
            if (inkBg != null) inkBg.gameObject.SetActive(false);
            Transform specBg = transform.Find("BottomStatusPanel/SpecialBarBg");
            if (specBg != null) specBg.gameObject.SetActive(false);
            Transform inkText = transform.Find("BottomStatusPanel/InkStateText");
            if (inkText != null) inkText.gameObject.SetActive(false);

            // 3. 将底部黑底大面板重构为紧凑居中血条，避免遮挡脚下墨水与角色
            Transform bottomPanel = transform.Find("BottomStatusPanel");
            if (bottomPanel != null)
            {
                var panelRt = bottomPanel.GetComponent<RectTransform>();
                panelRt.sizeDelta = new Vector2(360f, 28f);
                panelRt.anchoredPosition = new Vector2(0f, 22f);

                var panelImg = bottomPanel.GetComponent<Image>();
                if (panelImg != null)
                {
                    panelImg.color = new Color(0.04f, 0.06f, 0.10f, 0.65f);
                }

                Transform hpBg = bottomPanel.Find("HealthBarBg");
                if (hpBg != null)
                {
                    var hpRt = hpBg.GetComponent<RectTransform>();
                    hpRt.anchoredPosition = Vector2.zero;
                    hpRt.sizeDelta = new Vector2(340f, 14f);
                }
            }
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
                    reticleGo.transform.SetParent(transform, false);
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
            if (teamAPercentText != null) teamAPercentText.text = $"橙队 {covA:F1}%";
            if (teamBPercentText != null) teamBPercentText.text = $"{covB:F1}% 蓝队";

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
                    if (match.CurrentPhase == MatchManager.Phase.Playing || match.CurrentPhase == MatchManager.Phase.Finished)
                    {
                        centerStatusText.text = $"{secs / 60:00}:{secs % 60:00}";
                        centerStatusText.color = secs <= (int)Mathf.Max(1f, 30f)
                            ? new Color(1f, 0.42f, 0.35f, 1f)
                            : Color.white;
                    }
                    else
                    {
                        centerStatusText.text = "等待开赛";
                        centerStatusText.color = Color.white;
                    }
                }
                else
                {
                    int mins = Mathf.FloorToInt(_elapsedPlayTime / 60f);
                    int s = Mathf.FloorToInt(_elapsedPlayTime % 60f);
                    centerStatusText.text = $"手感试玩场  {mins:00}:{s:00}";
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

            if (inkBarFill != null)
            {
                inkBarFill.fillAmount = motor.Ink01;
                inkBarFill.color = motor.CurrentInk >= 55f
                    ? new Color(1.0f, 0.50f, 0.08f, 1f)
                    : new Color(0.85f, 0.32f, 0.12f, 1f);
            }

            if (healthBarFill != null)
            {
                healthBarFill.fillAmount = health.Health01;
                healthBarFill.color = motor.IsInEnemyInk
                    ? new Color(1f, 0.25f, 0.35f, 1f)
                    : new Color(0.25f, 0.92f, 0.48f, 1f);
            }

            if (inkStateText != null)
            {
                if (!health.IsAlive)
                {
                    inkStateText.text = $"复活倒计时 {health.RespawnRemaining:F1}s...";
                }
                else if (motor.IsSwimming)
                {
                    inkStateText.text = $"[潜行极速回墨] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}";
                }
                else if (motor.IsInEnemyInk)
                {
                    inkStateText.text = $"[敌方墨水减速!] 墨水 {motor.CurrentInk:F0}/100  HP {health.CurrentHealth:F0}";
                }
                else
                {
                    float bombCost = weapon.Config != null ? weapon.Config.bombInkCost : 70f;
                    string bombHint = motor.CurrentInk >= bombCost
                        ? "手雷[E]就绪"
                        : $"墨水不足手雷(需{bombCost:F0})";
                    inkStateText.text = $"墨水 {motor.CurrentInk:F0}/100 ({bombHint})  |  HP {health.CurrentHealth:F0}";
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

            if (specialStateText != null)
            {
                string specialName = weapon.ActiveSpecialKind == SpecialKind.Storm
                    ? "墨水风暴云"
                    : "超级墨水砸地";

                if (weapon.IsPerformingSpecial)
                {
                    specialStateText.text = $">>> {specialName}释放中! <<<";
                }
                else if (weapon.IsSpecialReady)
                {
                    specialStateText.text = $"★ 大招已满！按 [Q] {specialName} ★";
                }
                else
                {
                    specialStateText.text = $"大招充能 {(weapon.SpecialCharge01 * 100f):F0}% (涂地攒能量)";
                }
            }

            UpdateSpecialGauges(motor.CurrentInk, motor.MaxInk,
                weapon.SpecialCharge01, weapon.IsSpecialReady, weapon.ActiveSpecialKind,
                weapon.Config != null ? weapon.Config.bombInkCost : 70f);
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
