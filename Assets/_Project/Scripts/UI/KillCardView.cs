using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 单条动态击杀播报卡片（Kill Card，P4-D）：
    /// 遵循 Inkwave .iw-kcard 设计：
    /// - 圆角长条胶囊底（UIInkShapeGraphic RoundedRect），带 Chunky 纯黑描边与果冻晃动；
    /// - 左侧队伍色圆形底，内嵌纯白矢量武器图标（7类主武器 / 手雷 / 大招）；
    /// - 击杀者粗体名、SPLAT 标签、受害者名字；
    /// - UISpringMotion 物理弹簧弹性滑入与淡出。
    /// </summary>
    [AddComponentMenu("P1/UI/Kill Card View")]
    [RequireComponent(typeof(RectTransform))]
    public class KillCardView : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;
        [SerializeField] private UIInkShapeGraphic cardBgGraphic;
        [SerializeField] private RectTransform weaponIconCircle;
        [SerializeField] private Image weaponIconImage;
        [SerializeField] private Image weaponCircleBg;
        [SerializeField] private TMP_Text killerText;
        [SerializeField] private TMP_Text actionTagText;
        [SerializeField] private TMP_Text victimText;
        [SerializeField] private UISpringMotion springMotion;
        [SerializeField] private CanvasGroup canvasGroup;

        private RectTransform _rectTransform;
        private float _lifeTimer;
        private float _displayDuration = 3.6f;
        private bool _isLeaving;
        private System.Action<KillCardView> _onDismiss;

        public bool IsActive => gameObject.activeSelf && !_isLeaving;
        public RectTransform Rect => _rectTransform != null ? _rectTransform : (_rectTransform = GetComponent<RectTransform>());

        private void Awake()
        {
            EnsureBuilt();
        }

        public void EnsureBuilt()
        {
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (springMotion == null)
            {
                springMotion = GetComponent<UISpringMotion>();
                if (springMotion == null) springMotion = gameObject.AddComponent<UISpringMotion>();
            }

            if (cardBgGraphic == null)
            {
                var bgGo = new GameObject("CardBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgGo.transform.SetParent(transform, false);
                Stretch((RectTransform)bgGo.transform);
                cardBgGraphic = bgGo.GetComponent<UIInkShapeGraphic>();
                cardBgGraphic.Shape = UIInkShape.RoundedRect;
                cardBgGraphic.CornerRadius = 24f;
                cardBgGraphic.BorderWidth = 4f;
                cardBgGraphic.BorderColor = new Color(0.082f, 0.071f, 0.110f, 1f);
                cardBgGraphic.color = new Color(0.098f, 0.082f, 0.145f, 0.94f);
                cardBgGraphic.raycastTarget = false;
            }

            if (weaponIconCircle == null)
            {
                var circleGo = new GameObject("WeaponCircle", typeof(RectTransform), typeof(Image));
                circleGo.transform.SetParent(transform, false);
                weaponIconCircle = circleGo.GetComponent<RectTransform>();
                weaponIconCircle.anchorMin = new Vector2(0f, 0.5f);
                weaponIconCircle.anchorMax = new Vector2(0f, 0.5f);
                weaponIconCircle.pivot = new Vector2(0.5f, 0.5f);
                weaponIconCircle.anchoredPosition = new Vector2(28f, 0f);
                weaponIconCircle.sizeDelta = new Vector2(38f, 38f);

                weaponCircleBg = circleGo.GetComponent<Image>();
                weaponCircleBg.sprite = GetCircleSprite();
                weaponCircleBg.raycastTarget = false;

                var iconGo = new GameObject("WeaponIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(weaponIconCircle, false);
                var iconRect = iconGo.GetComponent<RectTransform>();
                Stretch(iconRect, 4f);
                weaponIconImage = iconGo.GetComponent<Image>();
                weaponIconImage.color = Color.white;
                weaponIconImage.raycastTarget = false;
            }

            if (killerText == null)
            {
                var killerGo = new GameObject("KillerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                killerGo.transform.SetParent(transform, false);
                var kRect = killerGo.GetComponent<RectTransform>();
                kRect.anchorMin = new Vector2(0f, 0.5f);
                kRect.anchorMax = new Vector2(0f, 0.5f);
                kRect.pivot = new Vector2(0f, 0.5f);
                kRect.anchoredPosition = new Vector2(56f, 9f);
                kRect.sizeDelta = new Vector2(160f, 22f);

                killerText = killerGo.GetComponent<TextMeshProUGUI>();
                killerText.fontSize = 15f;
                killerText.fontStyle = FontStyles.Bold;
                killerText.alignment = TextAlignmentOptions.Left;
                killerText.color = Color.white;
                killerText.raycastTarget = false;
            }

            if (actionTagText == null)
            {
                var tagGo = new GameObject("ActionTag", typeof(RectTransform), typeof(TextMeshProUGUI));
                tagGo.transform.SetParent(transform, false);
                var tRect = tagGo.GetComponent<RectTransform>();
                tRect.anchorMin = new Vector2(0f, 0.5f);
                tRect.anchorMax = new Vector2(0f, 0.5f);
                tRect.pivot = new Vector2(0f, 0.5f);
                tRect.anchoredPosition = new Vector2(56f, -10f);
                tRect.sizeDelta = new Vector2(60f, 18f);

                actionTagText = tagGo.GetComponent<TextMeshProUGUI>();
                actionTagText.fontSize = 10f;
                actionTagText.fontStyle = FontStyles.Bold;
                actionTagText.alignment = TextAlignmentOptions.Left;
                actionTagText.color = new Color(0.72f, 0.70f, 0.80f, 1f);
                actionTagText.text = "SPLAT";
                actionTagText.raycastTarget = false;
            }

            if (victimText == null)
            {
                var victimGo = new GameObject("VictimText", typeof(RectTransform), typeof(TextMeshProUGUI));
                victimGo.transform.SetParent(transform, false);
                var vRect = victimGo.GetComponent<RectTransform>();
                vRect.anchorMin = new Vector2(0f, 0.5f);
                vRect.anchorMax = new Vector2(0f, 0.5f);
                vRect.pivot = new Vector2(0f, 0.5f);
                vRect.anchoredPosition = new Vector2(110f, -10f);
                vRect.sizeDelta = new Vector2(160f, 18f);

                victimText = victimGo.GetComponent<TextMeshProUGUI>();
                victimText.fontSize = 12f;
                victimText.fontStyle = FontStyles.Bold;
                victimText.alignment = TextAlignmentOptions.Left;
                victimText.color = new Color(0.88f, 0.88f, 0.92f, 1f);
                victimText.raycastTarget = false;
            }

            springMotion.SetTheme(theme);
            springMotion.OnPlayedOut -= HandlePlayedOut;
            springMotion.OnPlayedOut += HandlePlayedOut;
        }

        public void Bind(KillFeedEvent ev, UIThemeSO themeAsset, Sprite weaponIcon, System.Action<KillCardView> onDismiss)
        {
            EnsureBuilt();
            theme = themeAsset;
            _onDismiss = onDismiss;
            _isLeaving = false;
            _lifeTimer = _displayDuration;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            Color killerCol = resolved.GetTeamColor(ev.KillerTeam);
            Color victimCol = resolved.GetTeamColor(ev.VictimTeam);

            // 1. 背景：本玩家击杀时加金边，玩家阵亡时深紫红，普通击杀深黑
            if (cardBgGraphic != null)
            {
                if (ev.IsLocalPlayerKiller)
                {
                    cardBgGraphic.BorderColor = resolved.readyGold;
                    cardBgGraphic.BorderWidth = 5f;
                    cardBgGraphic.color = new Color(0.14f, 0.10f, 0.05f, 0.96f);
                }
                else if (ev.IsLocalPlayerVictim)
                {
                    cardBgGraphic.BorderColor = resolved.dangerRed;
                    cardBgGraphic.BorderWidth = 4.5f;
                    cardBgGraphic.color = new Color(0.18f, 0.05f, 0.08f, 0.96f);
                }
                else
                {
                    cardBgGraphic.BorderColor = resolved.outlineColor;
                    cardBgGraphic.BorderWidth = 3.5f;
                    cardBgGraphic.color = new Color(0.082f, 0.071f, 0.110f, 0.92f);
                }
                cardBgGraphic.PlayWobble(0.09f, 0.35f);
            }

            // 2. 武器圆形底与图标
            if (weaponCircleBg != null)
            {
                weaponCircleBg.color = killerCol;
            }

            if (weaponIconImage != null)
            {
                weaponIconImage.sprite = weaponIcon;
                weaponIconImage.enabled = weaponIcon != null;
            }

            // 3. 文字
            if (killerText != null)
            {
                killerText.text = ev.KillerName;
                killerText.color = ev.IsLocalPlayerKiller ? resolved.readyGold : killerCol;
            }

            if (actionTagText != null)
            {
                actionTagText.text = ev.IsLocalPlayerKiller ? "CRUSHED" : "SPLAT";
                actionTagText.color = new Color(0.75f, 0.72f, 0.85f, 1f);
            }

            if (victimText != null)
            {
                victimText.text = ev.VictimName;
                victimText.color = ev.IsLocalPlayerVictim ? resolved.dangerRed : victimCol;
            }

            gameObject.SetActive(true);
            if (springMotion != null)
            {
                springMotion.SnapHome();
                springMotion.PlayIn(0f);
            }
        }

        private void Update()
        {
            if (_isLeaving) return;

            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
            {
                Dismiss();
            }
        }

        public void Dismiss()
        {
            if (_isLeaving) return;
            _isLeaving = true;

            if (springMotion != null)
            {
                springMotion.PlayOut();
            }
            else
            {
                HandlePlayedOut();
            }
        }

        private void HandlePlayedOut()
        {
            gameObject.SetActive(false);
            _onDismiss?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (springMotion != null) springMotion.OnPlayedOut -= HandlePlayedOut;
        }

        private static Sprite _circleSprite;
        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "KillCardCircleTex" };
            Color[] cols = new Color[size * size];
            Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float r = size * 0.5f - 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float a = Mathf.Clamp01((r - d) * 1.5f);
                    cols[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
