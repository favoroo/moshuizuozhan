using System.Collections.Generic;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 击杀播报控制器（Kill Feed View，P4-D）：
    /// 位于屏幕右侧偏上区域，管理击杀卡片（Kill Card）的排队、滑动入场、堆叠布局与平滑淡出。
    /// 自动监听 <see cref="KillFeedEventHub.OnKillReported"/>，支持对象池复用与最大卡片数限制。
    /// </summary>
    [AddComponentMenu("P1/UI/Kill Feed View")]
    [RequireComponent(typeof(RectTransform))]
    public class KillFeedView : MonoBehaviour
    {
        private const string WeaponsPath = "Assets/_Project/Art/Sprites/Icons/Weapons/";
        private const string SubsPath = "Assets/_Project/Art/Sprites/Icons/Subs/";
        private const string SpecialsPath = "Assets/_Project/Art/Sprites/Icons/Specials/";

        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;
        [SerializeField] private int maxVisibleCards = 4;
        [SerializeField] private float cardSpacing = 58f;
        [SerializeField] private Vector2 cardSize = new Vector2(300f, 50f);

        [Header("武器与技能图标库")]
        [SerializeField] private Sprite shooterIcon;
        [SerializeField] private Sprite rollerIcon;
        [SerializeField] private Sprite chargerIcon;
        [SerializeField] private Sprite dualiesIcon;
        [SerializeField] private Sprite slosherIcon;
        [SerializeField] private Sprite blasterIcon;
        [SerializeField] private Sprite splatlingIcon;
        [SerializeField] private Sprite bombIcon;
        [SerializeField] private Sprite slamIcon;
        [SerializeField] private Sprite stormIcon;

        private readonly List<KillCardView> _activeCards = new List<KillCardView>(8);
        private readonly Queue<KillCardView> _cardPool = new Queue<KillCardView>(8);
        private RectTransform _rectTransform;

        public IReadOnlyList<KillCardView> ActiveCards => _activeCards;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        private void Awake()
        {
            EnsureBuilt();
            LoadIconsIfNeeded();
        }

        private void OnEnable()
        {
            KillFeedEventHub.OnKillReported -= HandleKillReported;
            KillFeedEventHub.OnKillReported += HandleKillReported;
        }

        private void OnDisable()
        {
            KillFeedEventHub.OnKillReported -= HandleKillReported;
        }

        public void EnsureBuilt()
        {
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

            // 默认定位在屏幕右侧、计时器与小地图下方（约 Y=-230）
            _rectTransform.anchorMin = new Vector2(1f, 1f);
            _rectTransform.anchorMax = new Vector2(1f, 1f);
            _rectTransform.pivot = new Vector2(1f, 1f);
            if (_rectTransform.anchoredPosition == Vector2.zero)
            {
                _rectTransform.anchoredPosition = new Vector2(-20f, -230f);
            }
            _rectTransform.sizeDelta = new Vector2(cardSize.x, 300f);
        }

        private void LoadIconsIfNeeded()
        {
#if UNITY_EDITOR
            if (shooterIcon == null) shooterIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_shooter.png");
            if (rollerIcon == null) rollerIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_roller.png");
            if (chargerIcon == null) chargerIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_charger.png");
            if (dualiesIcon == null) dualiesIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_dualies.png");
            if (slosherIcon == null) slosherIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_slosher.png");
            if (blasterIcon == null) blasterIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_blaster.png");
            if (splatlingIcon == null) splatlingIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(WeaponsPath + "weapon_splatling.png");
            if (bombIcon == null) bombIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(SubsPath + "sub_bomb.png");
            if (slamIcon == null) slamIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(SpecialsPath + "special_slam.png");
            if (stormIcon == null) stormIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(SpecialsPath + "special_storm.png");
#endif
        }

        private void HandleKillReported(KillFeedEvent ev)
        {
            PushKill(ev);
        }

        /// <summary>压入一条新击杀，自动计算图标并从右侧弹入。</summary>
        public KillCardView PushKill(KillFeedEvent ev)
        {
            EnsureBuilt();
            LoadIconsIfNeeded();

            // 若活跃卡片超出上限，让最老的一张立即淡出并移出活跃列表
            while (_activeCards.Count >= maxVisibleCards)
            {
                var oldest = _activeCards[0];
                _activeCards.RemoveAt(0);
                oldest.Dismiss();
                _cardPool.Enqueue(oldest);
            }

            KillCardView card = GetOrCreateCard();
            Sprite icon = ResolveWeaponIcon(ev);

            card.Bind(ev, theme, icon, OnCardDismissed);
            _activeCards.Add(card);

            RearrangeCards();
            return card;
        }

        private void OnCardDismissed(KillCardView card)
        {
            _activeCards.Remove(card);
            _cardPool.Enqueue(card);
            RearrangeCards();
        }

        private void RearrangeCards()
        {
            // 从上到下排布，每张卡垂直间隔 cardSpacing
            for (int i = 0; i < _activeCards.Count; i++)
            {
                var rt = _activeCards[i].Rect;
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(0f, -i * cardSpacing);
            }
        }

        private KillCardView GetOrCreateCard()
        {
            if (_cardPool.Count > 0)
            {
                var card = _cardPool.Dequeue();
                card.transform.SetAsLastSibling();
                return card;
            }

            var go = new GameObject($"KillCard_{_activeCards.Count + _cardPool.Count:00}", typeof(RectTransform), typeof(KillCardView));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = cardSize;

            var view = go.GetComponent<KillCardView>();
            view.EnsureBuilt();
            return view;
        }

        public Sprite ResolveWeaponIcon(KillFeedEvent ev)
        {
            if (ev.IsSubBomb) return bombIcon;
            if (ev.IsSpecial)
            {
                if (ev.WeaponDisplayName != null && ev.WeaponDisplayName.IndexOf("Storm", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return stormIcon;
                return slamIcon;
            }

            return ev.WeaponKind switch
            {
                WeaponKind.Roller => rollerIcon,
                WeaponKind.Charger => chargerIcon,
                WeaponKind.Dualies => dualiesIcon,
                WeaponKind.Slosher => slosherIcon,
                WeaponKind.Blaster => blasterIcon,
                WeaponKind.Splatling => splatlingIcon,
                _ => shooterIcon
            };
        }
    }
}
