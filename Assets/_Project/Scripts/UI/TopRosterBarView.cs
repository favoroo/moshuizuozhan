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
        [SerializeField] private TMP_FontAsset fontAsset;

        [Header("槽位引用（共 8 名选手）")]
        [SerializeField] private RosterAvatarSlot[] teamASlots = new RosterAvatarSlot[4];
        [SerializeField] private RosterAvatarSlot[] teamBSlots = new RosterAvatarSlot[4];

        [Header("布局容器")]
        [SerializeField] private RectTransform teamAContainer;
        [SerializeField] private RectTransform teamBContainer;

        private float _rebindTimer = 0f;
        private bool _hasBoundCombatants;
        private bool _isMobileLayout;
        private readonly List<CombatantHealth> _bindListA = new List<CombatantHealth>(4);
        private readonly List<CombatantHealth> _bindListB = new List<CombatantHealth>(4);

        /// <summary>
        /// 左右两队容器内边缘距屏幕中心的距离。
        /// 中央比分条半宽 130 + 两侧百分比读数（约 78）≈ 208，取 230 让阵容条不再压住比分条。
        /// </summary>
        private const float CenterGap = 230f;

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
            float slotSize = _isMobileLayout ? 76f : 72f;
            float spacing = 10f;
            float containerWidth = 4f * slotSize + 3f * spacing;

            if (teamAContainer != null)
            {
                teamAContainer.sizeDelta = new Vector2(containerWidth, slotSize);
                var lA = teamAContainer.GetComponent<HorizontalLayoutGroup>();
                if (lA != null) lA.spacing = spacing;
            }
            if (teamBContainer != null)
            {
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
            if (fontAsset == null)
            {
                fontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/TMP/TitanOne-Regular SDF.asset");
            }
#endif

            RectTransform rootRect = GetComponent<RectTransform>();
            if (rootRect == null) rootRect = gameObject.AddComponent<RectTransform>();

            float slotSize = _isMobileLayout ? 76f : 72f;
            float spacing = 10f;
            float containerWidth = 4f * slotSize + 3f * spacing;

            // 根节点锚定在顶部居中（宽度只作容器参考；子容器按中心偏移排布，可越出根矩形）
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -8f);
            rootRect.sizeDelta = new Vector2(1200f, 84f);

            // 1. 左侧队伍 A 容器（内边缘固定在中心左侧 CenterGap 处，整体向屏幕左侧外移）
            if (teamAContainer == null)
            {
                var goA = new GameObject("SquadA_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goA.transform.SetParent(transform, false);
                teamAContainer = goA.GetComponent<RectTransform>();
                teamAContainer.anchorMin = new Vector2(0.5f, 0.5f);
                teamAContainer.anchorMax = new Vector2(0.5f, 0.5f);
                teamAContainer.pivot = new Vector2(1f, 0.5f);
                teamAContainer.anchoredPosition = new Vector2(-CenterGap, 0f);
                teamAContainer.sizeDelta = new Vector2(containerWidth, slotSize);

                var layoutA = goA.GetComponent<HorizontalLayoutGroup>();
                layoutA.spacing = spacing;
                layoutA.childAlignment = TextAnchor.MiddleLeft;
                layoutA.childControlWidth = false;
                layoutA.childControlHeight = false;
                layoutA.childForceExpandWidth = false;
                layoutA.childForceExpandHeight = false;
            }

            // 2. 右侧队伍 B 容器（内边缘固定在中心右侧 CenterGap 处）
            if (teamBContainer == null)
            {
                var goB = new GameObject("SquadB_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goB.transform.SetParent(transform, false);
                teamBContainer = goB.GetComponent<RectTransform>();
                teamBContainer.anchorMin = new Vector2(0.5f, 0.5f);
                teamBContainer.anchorMax = new Vector2(0.5f, 0.5f);
                teamBContainer.pivot = new Vector2(0f, 0.5f);
                teamBContainer.anchoredPosition = new Vector2(CenterGap, 0f);
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
        }

        private RosterAvatarSlot CreateAvatarSlot(RectTransform parent, string slotName, Team slotTeam, UIThemeSO resolvedTheme, float slotSize = 72f)
        {
            var slotGo = new GameObject(slotName, typeof(RectTransform), typeof(UISpringMotion), typeof(RosterAvatarSlot));
            slotGo.transform.SetParent(parent, false);

            var slotRect = slotGo.GetComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(slotSize, slotSize);

            var springMotion = slotGo.GetComponent<UISpringMotion>();
            springMotion.SetTheme(resolvedTheme);

            // 1. 背景圆角方章（6px 墨边 + 硬阴影 (6, -6)）
            var bgGo = new GameObject("BackgroundBlob", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(slotGo.transform, false);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.RoundedRect;
            bgShape.CornerRadius = ComicStyle.CornerSharp;
            bgShape.BorderWidth = ComicStyle.Border;
            bgShape.BorderColor = ComicStyle.Ink;
            bgShape.HardShadow = true;
            bgShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            bgShape.ShadowColor = ComicStyle.Ink;
            bgShape.Halftone = false;
            bgShape.GlossAlpha = 0f;
            bgShape.DrawGloss = false;
            bgShape.InnerRimAlpha = 0f;
            bgShape.color = resolvedTheme.GetTeamColor(slotTeam);

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
            fbSquidTmp.fontSize = 28f;
            fbSquidTmp.alignment = TextAlignmentOptions.Center;
            fbSquidTmp.color = Color.white;
            fallbackSquidGo.SetActive(squidSprite == null);

            // 3. 阵亡骷髅/X 标与兜底（红底黑 X）
            var deathGo = new GameObject("DeathIcon", typeof(RectTransform), typeof(Image));
            deathGo.transform.SetParent(slotGo.transform, false);
            var deathRect = deathGo.GetComponent<RectTransform>();
            deathRect.anchorMin = new Vector2(0.14f, 0.14f);
            deathRect.anchorMax = new Vector2(0.86f, 0.86f);
            deathRect.sizeDelta = Vector2.zero;

            var deathImg = deathGo.GetComponent<Image>();
            deathImg.sprite = deathSprite;
            deathImg.color = ComicStyle.Ink;
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
            fbDeathTmp.fontSize = 32f;
            fbDeathTmp.fontStyle = FontStyles.Bold;
            fbDeathTmp.alignment = TextAlignmentOptions.Center;
            fbDeathTmp.color = ComicStyle.Ink;
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

            // 6. 倒计时秒数角标
            var badgeGo = new GameObject("CountdownBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(slotGo.transform, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.anchoredPosition = new Vector2(6f, -6f);
            badgeRect.sizeDelta = new Vector2(26f, 24f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.RoundedRect;
            badgeShape.CornerRadius = 3f;
            badgeShape.BorderWidth = 2.5f;
            badgeShape.BorderColor = ComicStyle.Paper;
            badgeShape.color = ComicStyle.Ink;

            var countTextGo = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countTextGo.transform.SetParent(badgeGo.transform, false);
            var countTextRect = countTextGo.GetComponent<RectTransform>();
            countTextRect.anchorMin = Vector2.zero;
            countTextRect.anchorMax = Vector2.one;
            countTextRect.sizeDelta = Vector2.zero;

            var tmp = countTextGo.GetComponent<TextMeshProUGUI>();
            tmp.font = resolvedTheme.ResolveFont(true, "9");
            tmp.fontSize = 14f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = ComicStyle.Yellow;
            badgeGo.SetActive(false);

            // 7. 本地玩家「YOU」黄底黑字角标
            var youGo = new GameObject("YouIndicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            youGo.transform.SetParent(slotGo.transform, false);
            var youRect = youGo.GetComponent<RectTransform>();
            youRect.anchorMin = new Vector2(0.5f, 1f);
            youRect.anchorMax = new Vector2(0.5f, 1f);
            youRect.pivot = new Vector2(0.5f, 0f);
            youRect.anchoredPosition = new Vector2(0f, 4f);
            youRect.sizeDelta = new Vector2(38f, 18f);

            var youShape = youGo.GetComponent<UIInkShapeGraphic>();
            youShape.Shape = UIInkShape.RoundedRect;
            youShape.CornerRadius = 3f;
            youShape.BorderWidth = 2.5f;
            youShape.BorderColor = ComicStyle.Ink;
            youShape.HardShadow = true;
            youShape.ShadowOffset = new Vector2(2f, -2f);
            youShape.ShadowColor = ComicStyle.Ink;
            youShape.color = ComicStyle.Yellow;

            var youTextGo = new GameObject("YouText", typeof(RectTransform), typeof(TextMeshProUGUI));
            youTextGo.transform.SetParent(youGo.transform, false);
            var youTextRect = youTextGo.GetComponent<RectTransform>();
            youTextRect.anchorMin = Vector2.zero;
            youTextRect.anchorMax = Vector2.one;
            youTextRect.sizeDelta = Vector2.zero;

            var youTmp = youTextGo.GetComponent<TextMeshProUGUI>();
            youTmp.font = resolvedTheme.ResolveFont(true, "YOU");
            youTmp.fontSize = 11f;
            youTmp.fontStyle = FontStyles.Bold;
            youTmp.alignment = TextAlignmentOptions.Center;
            youTmp.color = ComicStyle.Ink;
            youTmp.text = "YOU";

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
