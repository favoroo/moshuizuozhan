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

        public IReadOnlyList<RosterAvatarSlot> TeamASlots => teamASlots;
        public IReadOnlyList<RosterAvatarSlot> TeamBSlots => teamBSlots;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
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

            var listA = new List<CombatantHealth>(4);
            var listB = new List<CombatantHealth>(4);

            PlayerController localPlayer = PlayerController.LocalPlayer;
            CombatantHealth playerHealth = localPlayer != null ? localPlayer.Health : null;

            // 优先把主角放在 Team A 第 0 位
            if (playerHealth != null && playerHealth.Team == Team.TeamA)
            {
                listA.Add(playerHealth);
            }

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth ch = all[i];
                if (ch == null) continue;
                if (ch == playerHealth) continue;

                if (ch.Team == Team.TeamA && listA.Count < 4)
                {
                    listA.Add(ch);
                }
                else if (ch.Team == Team.TeamB && listB.Count < 4)
                {
                    listB.Add(ch);
                }
            }

            // 绑定到槽位
            for (int i = 0; i < teamASlots.Length; i++)
            {
                if (teamASlots[i] == null) continue;
                CombatantHealth ch = i < listA.Count ? listA[i] : null;
                bool isMe = ch != null && ch == playerHealth;
                teamASlots[i].BindCombatant(ch, isMe, theme);
            }

            for (int i = 0; i < teamBSlots.Length; i++)
            {
                if (teamBSlots[i] == null) continue;
                CombatantHealth ch = i < listB.Count ? listB[i] : null;
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
            if (teamASlots != null && teamASlots.Length == 4 && teamASlots[0] != null &&
                teamBSlots != null && teamBSlots.Length == 4 && teamBSlots[0] != null)
            {
                return;
            }

            BuildHierarchy();
        }

        private void BuildHierarchy()
        {
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);

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

            // 根节点锚定在顶部居中
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -8f);
            rootRect.sizeDelta = new Vector2(820f, 68f);

            // 1. 左侧队伍 A 容器
            if (teamAContainer == null)
            {
                var goA = new GameObject("SquadA_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goA.transform.SetParent(transform, false);
                teamAContainer = goA.GetComponent<RectTransform>();
                teamAContainer.anchorMin = new Vector2(0f, 0.5f);
                teamAContainer.anchorMax = new Vector2(0f, 0.5f);
                teamAContainer.pivot = new Vector2(0f, 0.5f);
                teamAContainer.anchoredPosition = new Vector2(16f, 0f);
                teamAContainer.sizeDelta = new Vector2(248f, 56f);

                var layoutA = goA.GetComponent<HorizontalLayoutGroup>();
                layoutA.spacing = 8f;
                layoutA.childAlignment = TextAnchor.MiddleLeft;
                layoutA.childControlWidth = false;
                layoutA.childControlHeight = false;
                layoutA.childForceExpandWidth = false;
                layoutA.childForceExpandHeight = false;
            }

            // 2. 右侧队伍 B 容器
            if (teamBContainer == null)
            {
                var goB = new GameObject("SquadB_Container", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                goB.transform.SetParent(transform, false);
                teamBContainer = goB.GetComponent<RectTransform>();
                teamBContainer.anchorMin = new Vector2(1f, 0.5f);
                teamBContainer.anchorMax = new Vector2(1f, 0.5f);
                teamBContainer.pivot = new Vector2(1f, 0.5f);
                teamBContainer.anchoredPosition = new Vector2(-16f, 0f);
                teamBContainer.sizeDelta = new Vector2(248f, 56f);

                var layoutB = goB.GetComponent<HorizontalLayoutGroup>();
                layoutB.spacing = 8f;
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
                teamASlots[i] = CreateAvatarSlot(teamAContainer, $"SlotA_{i}", Team.TeamA, resolvedTheme);
            }

            // 4. 构建 4 个 Team B 槽位（向内排列）
            teamBSlots = new RosterAvatarSlot[4];
            for (int i = 0; i < 4; i++)
            {
                teamBSlots[i] = CreateAvatarSlot(teamBContainer, $"SlotB_{i}", Team.TeamB, resolvedTheme);
            }
        }

        private RosterAvatarSlot CreateAvatarSlot(RectTransform parent, string slotName, Team slotTeam, UIThemeSO resolvedTheme)
        {
            var slotGo = new GameObject(slotName, typeof(RectTransform), typeof(UISpringMotion), typeof(RosterAvatarSlot));
            slotGo.transform.SetParent(parent, false);

            var slotRect = slotGo.GetComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(54f, 54f);

            var springMotion = slotGo.GetComponent<UISpringMotion>();
            springMotion.SetTheme(resolvedTheme);

            // 1. 背景墨滴 Blob
            var bgGo = new GameObject("BackgroundBlob", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(slotGo.transform, false);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            var bgShape = bgGo.GetComponent<UIInkShapeGraphic>();
            bgShape.Shape = UIInkShape.Blob;
            bgShape.BorderWidth = 4.2f;
            bgShape.BorderColor = resolvedTheme.outlineColor;
            bgShape.WobbleAmplitude = 0.065f;
            bgShape.Seed = slotName.GetHashCode();
            bgShape.color = resolvedTheme.GetTeamColor(slotTeam);

            // 2. 乌贼图标
            var squidGo = new GameObject("SquidIcon", typeof(RectTransform), typeof(Image));
            squidGo.transform.SetParent(slotGo.transform, false);
            var squidRect = squidGo.GetComponent<RectTransform>();
            squidRect.anchorMin = new Vector2(0.12f, 0.12f);
            squidRect.anchorMax = new Vector2(0.88f, 0.88f);
            squidRect.sizeDelta = Vector2.zero;

            var squidImg = squidGo.GetComponent<Image>();
            squidImg.sprite = squidSprite;
            squidImg.color = Color.white;
            squidImg.preserveAspect = true;

            // 3. 阵亡骷髅/X 标
            var deathGo = new GameObject("DeathIcon", typeof(RectTransform), typeof(Image));
            deathGo.transform.SetParent(slotGo.transform, false);
            var deathRect = deathGo.GetComponent<RectTransform>();
            deathRect.anchorMin = new Vector2(0.12f, 0.12f);
            deathRect.anchorMax = new Vector2(0.88f, 0.88f);
            deathRect.sizeDelta = Vector2.zero;

            var deathImg = deathGo.GetComponent<Image>();
            deathImg.sprite = deathSprite != null ? deathSprite : squidSprite;
            deathImg.color = new Color(1f, 0.35f, 0.42f, 0.95f);
            deathImg.preserveAspect = true;
            deathGo.SetActive(false);

            // 4. 复活倒计时环
            var ringGo = new GameObject("ProgressRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIProgressRingGraphic));
            ringGo.transform.SetParent(slotGo.transform, false);
            var ringRect = ringGo.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.one;
            ringRect.sizeDelta = new Vector2(6f, 6f);

            var progRing = ringGo.GetComponent<UIProgressRingGraphic>();
            progRing.RadiusFraction = 1.08f;
            progRing.Thickness = 3.5f;
            progRing.ProgressColor = Color.white;
            progRing.RingBackgroundColor = new Color(0f, 0f, 0f, 0.4f);
            ringGo.SetActive(false);

            // 5. 大招就绪旋转光环
            var specRingGo = new GameObject("SpecialReadyRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIRotatingRingGraphic));
            specRingGo.transform.SetParent(slotGo.transform, false);
            var specRingRect = specRingGo.GetComponent<RectTransform>();
            specRingRect.anchorMin = Vector2.zero;
            specRingRect.anchorMax = Vector2.one;
            specRingRect.sizeDelta = new Vector2(10f, 10f);

            var specRing = specRingGo.GetComponent<UIRotatingRingGraphic>();
            specRing.Configure(1.15f, 4f, 8, 140f, resolvedTheme.readyGold);
            specRing.Intensity = 0f;

            // 6. 倒计时秒数角标
            var badgeGo = new GameObject("CountdownBadge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(slotGo.transform, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.anchoredPosition = new Vector2(4f, -4f);
            badgeRect.sizeDelta = new Vector2(20f, 20f);

            var badgeImg = badgeGo.GetComponent<Image>();
            badgeImg.color = resolvedTheme.outlineColor;

            var countTextGo = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            countTextGo.transform.SetParent(badgeGo.transform, false);
            var countTextRect = countTextGo.GetComponent<RectTransform>();
            countTextRect.anchorMin = Vector2.zero;
            countTextRect.anchorMax = Vector2.one;
            countTextRect.sizeDelta = Vector2.zero;

            var tmp = countTextGo.GetComponent<TextMeshProUGUI>();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.fontSize = 13f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            badgeGo.SetActive(false);

            // 7. 本地玩家倒三角指示器
            var youGo = new GameObject("YouIndicator", typeof(RectTransform), typeof(Image));
            youGo.transform.SetParent(slotGo.transform, false);
            var youRect = youGo.GetComponent<RectTransform>();
            youRect.anchorMin = new Vector2(0.5f, 0f);
            youRect.anchorMax = new Vector2(0.5f, 0f);
            youRect.pivot = new Vector2(0.5f, 1f);
            youRect.anchoredPosition = new Vector2(0f, -4f);
            youRect.sizeDelta = new Vector2(10f, 8f);

            var youImg = youGo.GetComponent<Image>();
            youImg.color = Color.white;
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
                youImg,
                springMotion,
                resolvedTheme,
                slotTeam);

            return slot;
        }
    }
}
