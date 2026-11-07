using System.Collections.Generic;
using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 顶部 4v4 战况条视图（P4-B）：
    /// 位于屏幕顶部中央两侧，左侧为本队（Team A，4名队员），右侧为敌队（Team B，4名队员）。
    /// 支持纯代码程序化装配、自动绑定 CombatantHealthRegistry 战局实体，并在生死切换与大招就绪时提供果冻弹簧反馈。
    /// </summary>
    [AddComponentMenu("P1/UI/Top Roster Bar View")]
    public class TopRosterBarView : MonoBehaviour
    {
        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;
        [SerializeField] private Sprite squidSprite;
        [SerializeField] private Sprite deathSprite;
        // 旧字段（TitanOne）已退役：字体统一由 UIThemeSO.ResolveFont 提供，场景里残留的序列化值不再读取。

        [Header("槽位引用（共 8 名选手）")]
        [SerializeField] private RosterAvatarSlot[] teamASlots = new RosterAvatarSlot[4];
        [SerializeField] private RosterAvatarSlot[] teamBSlots = new RosterAvatarSlot[4];

        [Header("布局容器")]
        [SerializeField] private RectTransform teamAContainer;
        [SerializeField] private RectTransform teamBContainer;

        /// <summary>安全区顶部内边距（规范要求 ≥12px）：整条战况条再往下让。</summary>
        private const float TopSafePadding = 12f;

        /// <summary>「YOU」角标底边离槽顶的间隙。</summary>
        private const float YouBadgeGap = 4f;

        /// <summary>「YOU」角标兜底高度（旧版固定 24；实际高度按文本实测推导）。</summary>
        private const float YouBadgeHeightFallback = 24f;

        /// <summary>「YOU」角标兜底宽度（旧版固定 52；实际宽度按文本实测推导）。</summary>
        private const float YouBadgeWidthFallback = 52f;

        /// <summary>根节点固定宽度（只作容器参考，子容器按中心偏移排布，可越出根矩形）。</summary>
        private const float RootWidth = 1360f;

        private float _rebindTimer = 0f;
        private bool _hasBoundCombatants;
        private bool _isMobileLayout;
        private float _youBadgeHeight = YouBadgeHeightFallback;
        private readonly List<CombatantHealth> _bindListA = new List<CombatantHealth>(4);
        private readonly List<CombatantHealth> _bindListB = new List<CombatantHealth>(4);

        /// <summary>
        /// 左右两队容器内边缘距屏幕中心的距离。
        /// 中央比分条半宽 130 + 两侧百分比读数（约 78）≈ 208，取 230 让阵容条不再压住比分条。
        /// </summary>
        public IReadOnlyList<RosterAvatarSlot> TeamASlots => teamASlots;
        public IReadOnlyList<RosterAvatarSlot> TeamBSlots => teamBSlots;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        public void SetMobileLayout(bool isMobile)
        {
            _isMobileLayout = isMobile;
            ApplyLayoutMetrics();
        }

        private void Awake()
        {
            EnsureSlotsBuilt();
        }

        private void Start()
        {
            TryAutoBindCombatants();
        }

        private void Update()
        {
            // 开局前几秒或者成员发生变更时，定期自检并重新绑定
            _rebindTimer -= Time.deltaTime;
            if (_rebindTimer <= 0f)
            {
                _rebindTimer = 1.0f;
                TryAutoBindCombatants();
            }
        }

        /// <summary>
        /// 自动搜寻场上的 8 名战斗人员并分配给左右两队。
        /// </summary>
        public void TryAutoBindCombatants()
        {
            var all = CombatantHealthRegistry.All;
            if (all == null || all.Count == 0) return;

            _bindListA.Clear();
            _bindListB.Clear();

            PlayerController localPlayer = PlayerController.LocalPlayer;
            CombatantHealth playerHealth = localPlayer != null ? localPlayer.Health : null;

            // 优先把主角放在 Team A 第 0 位
            if (playerHealth != null && playerHealth.Team == Team.TeamA)
            {
                _bindListA.Add(playerHealth);
            }

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth ch = all[i];
                if (ch == null) continue;
                if (ch == playerHealth) continue;

                if (ch.Team == Team.TeamA && _bindListA.Count < 4)
                {
                    _bindListA.Add(ch);
                }
                else if (ch.Team == Team.TeamB && _bindListB.Count < 4)
                {
                    _bindListB.Add(ch);
                }
            }

            // 绑定到槽位
            for (int i = 0; i < teamASlots.Length; i++)
            {
                if (teamASlots[i] == null) continue;
                CombatantHealth ch = i < _bindListA.Count ? _bindListA[i] : null;
                bool isMe = ch != null && ch == playerHealth;
                teamASlots[i].BindCombatant(ch, isMe, theme);
            }

            for (int i = 0; i < teamBSlots.Length; i++)
            {
                if (teamBSlots[i] == null) continue;
                CombatantHealth ch = i < _bindListB.Count ? _bindListB[i] : null;
                teamBSlots[i].BindCombatant(ch, false, theme);
            }

            _hasBoundCombatants = true;
        }

        /// <summary>
        /// 单元测试或外部手动注入选手数据。
        /// </summary>
        public void BindCombatants(IList<CombatantHealth> teamA, IList<CombatantHealth> teamB, CombatantHealth localPlayer = null)
        {
            EnsureSlotsBuilt();

            for (int i = 0; i < teamASlots.Length; i++)
            {
                if (teamASlots[i] == null) continue;
                CombatantHealth ch = (teamA != null && i < teamA.Count) ? teamA[i] : null;
                bool isMe = ch != null && ch == localPlayer;
                teamASlots[i].BindCombatant(ch, isMe, theme);
            }

            for (int i = 0; i < teamBSlots.Length; i++)
            {
                if (teamBSlots[i] == null) continue;
                CombatantHealth ch = (teamB != null && i < teamB.Count) ? teamB[i] : null;
                teamBSlots[i].BindCombatant(ch, false, theme);
            }

            _hasBoundCombatants = true;
        }

        /// <summary>
        /// 确保左右两队的 4v4 槽位在层级树中完整建立。
        /// </summary>
        public void EnsureSlotsBuilt()
        {
            EnsureSlotsBuilt(Application.isMobilePlatform || Screen.height <= 800);
        }

        public void EnsureSlotsBuilt(bool isMobile)
        {
            _isMobileLayout = isMobile;
            if (teamASlots != null && teamASlots.Length == 4 && teamASlots[0] != null &&
                teamBSlots != null && teamBSlots.Length == 4 && teamBSlots[0] != null)
            {
                ApplyLayoutMetrics();
                return;
            }

            BuildHierarchy();
        }

        private void ApplyLayoutMetrics()
        {
            float slotSize = _isMobileLayout ? 84f : 72f;
            float spacing = _isMobileLayout ? 8f : 10f;
            float containerWidth = 4f * slotSize + 3f * spacing;
            float centerGap = _isMobileLayout ? 360f : 340f;

            // 纵向：根高 = 槽高，槽顶与根顶对齐；根顶再下移「安全内边距 + YOU 角标实际高度」。
            // 旧版角标与下沉量都写死 24+4，移动端 24pt 粗体「YOU」被 TMP 折成两行后
            // 文本块远高于角标，悬在槽上方正好顶出屏幕——这里每次都按角标真实高度重新推导。
            _youBadgeHeight = MeasureYouBadgeHeight();
            ApplyRootMetrics(slotSize);

            if (teamAContainer != null)
            {
                teamAContainer.anchoredPosition = new Vector2(-centerGap, 0f);
                teamAContainer.sizeDelta = new Vector2(containerWidth, slotSize);
                var lA = teamAContainer.GetComponent<HorizontalLayoutGroup>();
                if (lA != null) lA.spacing = spacing;
            }
            if (teamBContainer != null)
            {
                teamBContainer.anchoredPosition = new Vector2(centerGap, 0f);
                teamBContainer.sizeDelta = new Vector2(containerWidth, slotSize);
                var lB = teamBContainer.GetComponent<HorizontalLayoutGroup>();
                if (lB != null) lB.spacing = spacing;
            }

            if (teamASlots != null)
            {
                for (int i = 0; i < teamASlots.Length; i++)
                {
                    if (teamASlots[i] != null)
                    {
                        var rt = teamASlots[i].GetComponent<RectTransform>();
                        if (rt != null) rt.sizeDelta = new Vector2(slotSize, slotSize);
                    }
                }
            }

            if (teamBSlots != null)
            {
                for (int i = 0; i < teamBSlots.Length; i++)
                {
                    if (teamBSlots[i] != null)
                    {
                        var rt = teamBSlots[i].GetComponent<RectTransform>();
                        if (rt != null) rt.sizeDelta = new Vector2(slotSize, slotSize);
                    }
                }
            }
        }

        /// <summary>整条战况条相对安全区顶部要额外下沉的高度：角标间隙 + 角标实际高度。</summary>
        private float YouBadgeOverhead => YouBadgeGap + Mathf.Max(YouBadgeHeightFallback, _youBadgeHeight);

        /// <summary>
        /// 读取已建好的「YOU」角标真实高度（移动端 24pt 文本比桌面更高），
        /// 让根节点下沉量跟角标实际尺寸走，避免角标顶部被屏幕边缘裁掉。
        /// </summary>
        private float MeasureYouBadgeHeight()
        {
            float height = YouBadgeHeightFallback;
            if (teamASlots == null) return height;

            for (int i = 0; i < teamASlots.Length; i++)
            {
                if (teamASlots[i] == null) continue;
                Transform badge = teamASlots[i].transform.Find("YouIndicator");
                if (badge is RectTransform badgeRect && badgeRect.rect.height > height)
                {
                    height = badgeRect.rect.height;
                }
            }

            return height;
        }

        /// <summary>
        /// 根节点纵向几何（桌面 / 移动共用同一套推导）：
        /// 根高 = 槽高（容器垂直居中后槽顶恰好等于根顶），根顶下移 12px 安全内边距 + YOU 角标（间隙 + 实测高度）。
        /// 这样 84px 槽 + 3px 色签硬影 + 12px 顶部内边距全部落在屏内，不再被屏幕顶边裁切。
        /// </summary>
        private void ApplyRootMetrics(float slotSize)
        {
            RectTransform rootRect = transform as RectTransform;
            if (rootRect == null) rootRect = GetComponent<RectTransform>();
            if (rootRect == null) return;

            // 子容器按根中心偏移排布，可越出根矩形；根只提供纵向基准
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.sizeDelta = new Vector2(RootWidth, slotSize);
            rootRect.anchoredPosition = new Vector2(0f, -(TopSafePadding + YouBadgeOverhead));
        }

        private void BuildHierarchy()
        {
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);

            if (squidSprite == null)
            {
                squidSprite = UIIconLibrary.Get("mark_squid");
            }
            if (deathSprite == null)
            {
                deathSprite = UIIconLibrary.Get("mark_death") ?? UIIconLibrary.Get("mark_splat");
            }

#if UNITY_EDITOR
            if (squidSprite == null)
            {
                squidSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Icons/Marks/mark_squid.png");
            }
            if (deathSprite == null)
            {
                deathSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Icons/Marks/mark_death.png");
            }
#endif

            float slotSize = _isMobileLayout ? 84f : 72f;
            float spacing = _isMobileLayout ? 8f : 10f;
            float containerWidth = 4f * slotSize + 3f * spacing;

            // 中央让位：比分条 280 宽、两侧百分比各 96 宽并外挂 12px，
            // 半边距至少要 248；取 340 保证百分比不被头像压住。
            float centerGap = _isMobileLayout ? 360f : 340f;

            // 根节点锚定在顶部居中（宽度只作容器参考；子容器按中心偏移排布，可越出根矩形）。
            // 纵向偏移由 ApplyRootMetrics 推导：槽顶 = 根顶，再留 12px 安全内边距 + YOU 角标实际高度。
            // 角标尺寸要等槽位建好后才能实测，因此这里先给兜底值、建完再按真实高度重算一次。
            ApplyRootMetrics(slotSize);

            // 1. 左侧队伍 A 容器（内边缘固定在中心左侧 centerGap 处，整体向屏幕左侧外移）
            if (teamAContainer == null)
            {
                var goA = new GameObject("SquadA_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goA.transform.SetParent(transform, false);
                teamAContainer = goA.GetComponent<RectTransform>();
                teamAContainer.anchorMin = new Vector2(0.5f, 0.5f);
                teamAContainer.anchorMax = new Vector2(0.5f, 0.5f);
                teamAContainer.pivot = new Vector2(1f, 0.5f);
                teamAContainer.anchoredPosition = new Vector2(-centerGap, 0f);
                teamAContainer.sizeDelta = new Vector2(containerWidth, slotSize);

                var layoutA = goA.GetComponent<HorizontalLayoutGroup>();
                layoutA.spacing = spacing;
                layoutA.childAlignment = TextAnchor.MiddleLeft;
                layoutA.childControlWidth = false;
                layoutA.childControlHeight = false;
                layoutA.childForceExpandWidth = false;
                layoutA.childForceExpandHeight = false;
            }

            // 2. 右侧队伍 B 容器（内边缘固定在中心右侧 centerGap 处）
            if (teamBContainer == null)
            {
                var goB = new GameObject("SquadB_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goB.transform.SetParent(transform, false);
                teamBContainer = goB.GetComponent<RectTransform>();
                teamBContainer.anchorMin = new Vector2(0.5f, 0.5f);
                teamBContainer.anchorMax = new Vector2(0.5f, 0.5f);
                teamBContainer.pivot = new Vector2(0f, 0.5f);
                teamBContainer.anchoredPosition = new Vector2(centerGap, 0f);
                teamBContainer.sizeDelta = new Vector2(containerWidth, slotSize);

                var layoutB = goB.GetComponent<HorizontalLayoutGroup>();
                layoutB.spacing = spacing;
                layoutB.childAlignment = TextAnchor.MiddleRight;
                layoutB.childControlWidth = false;
                layoutB.childControlHeight = false;
                layoutB.childForceExpandWidth = false;
                layoutB.childForceExpandHeight = false;
            }

            // 3. 构建 4 个 Team A 槽位
            teamASlots = new RosterAvatarSlot[4];
            for (int i = 0; i < 4; i++)
            {
                teamASlots[i] = CreateAvatarSlot(teamAContainer, $"SlotA_{i}", Team.TeamA, resolvedTheme, slotSize);
            }

            // 4. 构建 4 个 Team B 槽位（向内排列）
            teamBSlots = new RosterAvatarSlot[4];
            for (int i = 0; i < 4; i++)
            {
                teamBSlots[i] = CreateAvatarSlot(teamBContainer, $"SlotB_{i}", Team.TeamB, resolvedTheme, slotSize);
            }

            // 5. 槽位建好后「YOU」角标才有真实尺寸：按实测高度重推根节点下沉量
            _youBadgeHeight = MeasureYouBadgeHeight();
            ApplyRootMetrics(slotSize);
        }

        private RosterAvatarSlot CreateAvatarSlot(RectTransform parent, string slotName, Team slotTeam, UIThemeSO resolvedTheme, float slotSize = 72f)
        {
            var slotGo = new GameObject(slotName, typeof(RectTransform), typeof(UISpringMotion), typeof(RosterAvatarSlot));
            slotGo.transform.SetParent(parent, false);

            var slotRect = slotGo.GetComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(slotSize, slotSize);

            var springMotion = slotGo.GetComponent<UISpringMotion>();
            springMotion.SetTheme(resolvedTheme);

            // 1. 背景正圆（圆角化：Pill 方形 rect 即正圆，4px 墨边，纯色块，无阴影）
            var bgGo = new GameObject("BackgroundBlob", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(slotGo.transform, false);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.Pill;
            bgShape.BorderWidth = ComicStyle.Border;
            bgShape.BorderColor = ComicStyle.Ink;
            bgShape.Halftone = false;
            bgShape.GlossAlpha = 0f;
            bgShape.DrawGloss = false;
            bgShape.InnerRimAlpha = 0f;
            bgShape.color = resolvedTheme.GetTeamColor(slotTeam);
            // 头像槽（72px）用色签档硬影 (2,-3)：多槽横排时留 8px 可见阴影，不互相糊住
            resolvedTheme.ApplyHardShadow(bgShape, UIHardShadowLevel.Tag);

            // 2. 乌贼图标与兜底
            var squidGo = new GameObject("SquidIcon", typeof(RectTransform), typeof(Image));
            squidGo.transform.SetParent(slotGo.transform, false);
            var squidRect = squidGo.GetComponent<RectTransform>();
            squidRect.anchorMin = new Vector2(0.14f, 0.14f);
            squidRect.anchorMax = new Vector2(0.86f, 0.86f);
            squidRect.sizeDelta = Vector2.zero;

            var squidImg = squidGo.GetComponent<Image>();
            squidImg.sprite = squidSprite;
            squidImg.color = Color.white;
            squidImg.preserveAspect = true;
            squidImg.enabled = squidSprite != null;

            // 乌贼图标缺失时的程序化剪影兜底
            var fallbackSquidGo = new GameObject("SquidFallback", typeof(RectTransform), typeof(TextMeshProUGUI));
            fallbackSquidGo.transform.SetParent(slotGo.transform, false);
            var fbSquidRect = fallbackSquidGo.GetComponent<RectTransform>();
            fbSquidRect.anchorMin = Vector2.zero;
            fbSquidRect.anchorMax = Vector2.one;
            fbSquidRect.sizeDelta = Vector2.zero;
            var fbSquidTmp = fallbackSquidGo.GetComponent<TextMeshProUGUI>();
            fbSquidTmp.font = resolvedTheme.ResolveFont(true, "^");
            fbSquidTmp.text = "^";
            fbSquidTmp.fontSize = UITypography.FontSnapped(28f);
            fbSquidTmp.alignment = TextAlignmentOptions.Center;
            fbSquidTmp.color = Color.white;
            fallbackSquidGo.SetActive(squidSprite == null);

            // 3. 阵亡骷髅/X 标与兜底（纯 Red 红字，配合黑底遮罩）
            var deathGo = new GameObject("DeathIcon", typeof(RectTransform), typeof(Image));
            deathGo.transform.SetParent(slotGo.transform, false);
            var deathRect = deathGo.GetComponent<RectTransform>();
            deathRect.anchorMin = new Vector2(0.14f, 0.14f);
            deathRect.anchorMax = new Vector2(0.86f, 0.86f);
            deathRect.sizeDelta = Vector2.zero;

            var deathImg = deathGo.GetComponent<Image>();
            deathImg.sprite = deathSprite;
            deathImg.color = ComicStyle.Red;
            deathImg.preserveAspect = true;
            deathImg.enabled = deathSprite != null;
            deathGo.SetActive(false);

            var fallbackDeathGo = new GameObject("DeathFallback", typeof(RectTransform), typeof(TextMeshProUGUI));
            fallbackDeathGo.transform.SetParent(slotGo.transform, false);
            var fbDeathRect = fallbackDeathGo.GetComponent<RectTransform>();
            fbDeathRect.anchorMin = Vector2.zero;
            fbDeathRect.anchorMax = Vector2.one;
            fbDeathRect.sizeDelta = Vector2.zero;
            var fbDeathTmp = fallbackDeathGo.GetComponent<TextMeshProUGUI>();
            fbDeathTmp.font = resolvedTheme.ResolveFont(true, "X");
            fbDeathTmp.text = "X";
            fbDeathTmp.fontSize = UITypography.FontSnapped(32f);
            fbDeathTmp.fontStyle = FontStyles.Bold;
            fbDeathTmp.alignment = TextAlignmentOptions.Center;
            fbDeathTmp.color = ComicStyle.Red;
            fallbackDeathGo.SetActive(false);

            // 4. 复活倒计时环（加粗到 5px）
            var ringGo = new GameObject("ProgressRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIProgressRingGraphic));
            ringGo.transform.SetParent(slotGo.transform, false);
            var ringRect = ringGo.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.sizeDelta = new Vector2(8f, 8f);

            var progRing = ringGo.GetComponent<UIProgressRingGraphic>();
            progRing.RadiusFraction = 1.05f;
            progRing.Thickness = 5f;
            progRing.ProgressColor = ComicStyle.Yellow;
            progRing.RingBackgroundColor = new Color(ComicStyle.Ink.r, ComicStyle.Ink.g, ComicStyle.Ink.b, 0.5f);
            ringGo.SetActive(false);

            // 5. 大招就绪旋转光环
            var specRingGo = new GameObject("SpecialReadyRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIRotatingRingGraphic));
            specRingGo.transform.SetParent(slotGo.transform, false);
            var specRingRect = specRingGo.GetComponent<RectTransform>();
            specRingRect.anchorMin = Vector2.zero;
            specRingRect.anchorMax = Vector2.one;
            specRingRect.sizeDelta = new Vector2(12f, 12f);

            var specRing = specRingGo.GetComponent<UIRotatingRingGraphic>();
            specRing.Configure(1.18f, 5f, 8, 140f, resolvedTheme.readyGold);
            specRing.Intensity = 0f;

            // 5b. 大招就绪角标：右上角 14×14 纯黄圆点（圆角化：Pill 方形 rect 即正圆）
            var pipGo = new GameObject("SpecialReadyPip", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            pipGo.transform.SetParent(slotGo.transform, false);
            var pipRect = pipGo.GetComponent<RectTransform>();
            pipRect.anchorMin = new Vector2(1f, 1f);
            pipRect.anchorMax = new Vector2(1f, 1f);
            pipRect.pivot = new Vector2(1f, 1f);
            pipRect.anchoredPosition = new Vector2(-2f, -2f);
            pipRect.sizeDelta = new Vector2(14f, 14f);

            var pipShape = pipGo.GetComponent<UIInkShapeGraphic>();
            pipShape.Shape = UIInkShape.Pill;
            pipShape.BorderWidth = 2f;
            pipShape.BorderColor = ComicStyle.Ink;
            pipShape.HardShadow = false;
            pipShape.color = ComicStyle.Yellow;
            pipShape.raycastTarget = false;
            pipGo.SetActive(false);

            // 6. 倒计时秒数角标（圆角化：胶囊黑块）
            var badgeGo = new GameObject("CountdownBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(slotGo.transform, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.anchoredPosition = new Vector2(6f, -6f);
            badgeRect.sizeDelta = new Vector2(34f, 30f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.Pill;
            badgeShape.BorderWidth = ComicStyle.BorderThin;
            badgeShape.BorderColor = ComicStyle.Paper;
            badgeShape.color = ComicStyle.Ink;
            // 复活秒数角标 = 色签档硬影 (2,-3)
            resolvedTheme.ApplyHardShadow(badgeShape, UIHardShadowLevel.Tag);

            var countTextGo = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countTextGo.transform.SetParent(badgeGo.transform, false);
            var countTextRect = countTextGo.GetComponent<RectTransform>();
            countTextRect.anchorMin = Vector2.zero;
            countTextRect.anchorMax = Vector2.one;
            countTextRect.sizeDelta = Vector2.zero;

            var tmp = countTextGo.GetComponent<TextMeshProUGUI>();
            tmp.font = resolvedTheme.ResolveFont(true, "9");
            tmp.fontSize = UITypography.FontSnapped(18f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = ComicStyle.Yellow;
            badgeGo.SetActive(false);

            // 7. 本地玩家「YOU」黄底黑字角标（胶囊 + 色签档硬影）
            var youGo = new GameObject("YouIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            youGo.transform.SetParent(slotGo.transform, false);
            var youRect = youGo.GetComponent<RectTransform>();
            youRect.anchorMin = new Vector2(0.5f, 1f);
            youRect.anchorMax = new Vector2(0.5f, 1f);
            youRect.pivot = new Vector2(0.5f, 0f);
            youRect.anchoredPosition = new Vector2(0f, YouBadgeGap);
            youRect.sizeDelta = new Vector2(YouBadgeWidthFallback, YouBadgeHeightFallback);

            var youShape = youGo.GetComponent<UIInkShapeGraphic>();
            youShape.Shape = UIInkShape.Pill; // 圆角化：YOU 角签改胶囊
            youShape.BorderWidth = ComicStyle.BorderThin;
            youShape.BorderColor = ComicStyle.Ink;
            youShape.color = ComicStyle.Yellow;
            resolvedTheme.ApplyHardShadow(youShape, UIHardShadowLevel.Tag);

            var youTextGo = new GameObject("YouText", typeof(RectTransform), typeof(TextMeshProUGUI));
            youTextGo.transform.SetParent(youGo.transform, false);
            var youTextRect = youTextGo.GetComponent<RectTransform>();
            youTextRect.anchorMin = Vector2.zero;
            youTextRect.anchorMax = Vector2.one;
            youTextRect.sizeDelta = Vector2.zero;

            var youTmp = youTextGo.GetComponent<TextMeshProUGUI>();
            youTmp.font = resolvedTheme.ResolveFont(true, "YOU");
            youTmp.fontSize = UITypography.FontSnapped(18f);
            youTmp.fontStyle = FontStyles.Bold;
            youTmp.alignment = TextAlignmentOptions.Center;
            youTmp.color = ComicStyle.Ink;
            youTmp.text = "YOU";

            // 「YOU」必须单行：TMP 默认开启换行，移动端 24pt 粗体「YOU」实测约 62px，
            // 比旧角标的 52px 宽，会被拆成「YO / U」两行；两行文本块高约 64px，
            // 角标只有 24px 高，文字上下溢出后正好顶出屏幕顶部（真机 20:9 截图复现）。
            youTmp.textWrappingMode = TextWrappingModes.NoWrap;
            youTmp.overflowMode = TextOverflowModes.Overflow;

            // 角标按实测文本撑开（含边框与少量内边距），高度只按字形高推导，
            // 不跟随 TMP 行高（1.33em），避免角标被行距撑得过大。
            // 「YOU」≈2.6em 粗体宽：TMP_Settings 缺失 / 文本尚未初始化时 GetPreferredValues 返回 0，
            // 兜底值保证角标永远不会窄到把单词折行。
            float youTextWidth = youTmp.fontSize * 2.6f;
            try
            {
                Vector2 youPreferred = youTmp.GetPreferredValues("YOU");
                if (youPreferred.x > 1f) youTextWidth = youPreferred.x;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[TopRosterBarView] 「YOU」角标文本测量失败，按 2.6em 兜底：" + ex.Message);
            }

            float youBadgeWidth = Mathf.Max(YouBadgeWidthFallback,
                Mathf.Ceil(youTextWidth + 2f * ComicStyle.BorderThin + 4f));
            float youBadgeHeight = Mathf.Max(YouBadgeHeightFallback,
                Mathf.Ceil(youTmp.fontSize * 0.78f + 2f * ComicStyle.BorderThin + 3f));
            youRect.sizeDelta = new Vector2(youBadgeWidth, youBadgeHeight);
            if (youBadgeHeight > _youBadgeHeight) _youBadgeHeight = youBadgeHeight;

            youGo.SetActive(false);

            // 装配槽位组件
            var slot = slotGo.GetComponent<RosterAvatarSlot>();
            slot.ConfigureComponents(
                bgShape,
                squidImg,
                deathImg,
                progRing,
                specRing,
                tmp,
                badgeRect,
                null,
                springMotion,
                resolvedTheme,
                slotTeam);

            slot.SetFallbacks(fallbackSquidGo, fallbackDeathGo, youGo);

            return slot;
        }
    }
}
