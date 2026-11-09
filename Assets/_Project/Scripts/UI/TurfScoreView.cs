using P1.Core;
using P1.Ink;
using P1.Player;
using TMPro;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 「+N m²」涂地飘字层（P42-D，对齐 INKWAVE <c>.iw-tpops</c>）：
    /// 飘字直接打在准星右下方（屏幕中心 + 30px 偏移）——涂到哪飘到哪，玩家视线焦点处即时反馈；
    /// 大额（≥ <see cref="TurfScoreMath.BigPopupThresholdSquareMeters"/>）放大并染己方墨色。
    /// 无常驻读数卡、无角落数字（用户明确要求去掉：常驻卡没有及时反馈感）。
    ///
    /// 曲线全部在 <see cref="TurfScoreMath"/>（INKWAVE <c>iw-tpop</c> 关键帧：
    /// 缩放 .5→1.15→1 弹入 → 停留 → 上飘淡出，±8px 横向抖动）。
    /// 数据：<see cref="MatchStatsService.TurfRegistered"/>（武器/手雷/风暴云均已埋点），
    /// 0.4s 聚合窗口防连发刷屏；不新增任何涂地计数逻辑。
    /// </summary>
    [AddComponentMenu("P1/UI/Turf Score View")]
    [RequireComponent(typeof(RectTransform))]
    public class TurfScoreView : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;

        /// <summary>飘字出生点相对准星的偏移：准星正下偏右（INKWAVE 同位），远离右上方的墨水罐，避免飘字钻进罐后被挡。</summary>
        private const float SpawnOffsetX = 24f;
        private const float SpawnOffsetY = -52f;

        private readonly UIChunkyText[] _popups = new UIChunkyText[TurfScoreMath.PopupPoolSize];
        private readonly RectTransform[] _popupRects = new RectTransform[TurfScoreMath.PopupPoolSize];
        private readonly CanvasGroup[] _popupGroups = new CanvasGroup[TurfScoreMath.PopupPoolSize];
        private readonly Vector2[] _popupSpawn = new Vector2[TurfScoreMath.PopupPoolSize];
        private readonly float[] _popupAge = new float[TurfScoreMath.PopupPoolSize];
        private readonly bool[] _popupActive = new bool[TurfScoreMath.PopupPoolSize];
        private int _popupCursor;

        private RectTransform _rect;
        private bool _built;
        private MatchManager _phaseMatch;
        private float _accumulated;
        private float _sinceFlush;
        private Color _teamColor = ComicStyle.Green;
        private bool _handlerErrorLogged;

        /// <summary>
        /// 大额飘字轻音的节流间隔：涂地是连续动作，0.4s 聚合窗口只是下限，
        /// 连续大额涂地时每个窗口都播会把音效糊成噪音，所以再单独限一次 0.26s。
        /// </summary>
        private const float PopupSoundIntervalSeconds = 0.26f;

        /// <summary>上一次大额飘字音的时间（unscaled：暂停 / 慢动作下节流节奏不受 timeScale 影响）。</summary>
        private float _lastPopupSoundTime = -1f;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            MatchStatsService.TurfRegistered += HandleTurfRegistered;
            SubscribePhaseChanged();
            _accumulated = 0f;
            _sinceFlush = 0f;
            RefreshTeamColor();
        }

        private void OnDisable()
        {
            MatchStatsService.TurfRegistered -= HandleTurfRegistered;
            UnsubscribePhaseChanged();
            for (int i = 0; i < _popups.Length; i++) HidePopup(i);
        }

        /// <summary>新一局开始（Countdown）时清掉跨局残留的聚合量，防止开局弹上一局的尾巴。</summary>
        private void SubscribePhaseChanged()
        {
            if (_phaseMatch != null) return;

            _phaseMatch = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (_phaseMatch != null) _phaseMatch.PhaseChanged += HandlePhaseChanged;
        }

        private void UnsubscribePhaseChanged()
        {
            if (_phaseMatch != null) _phaseMatch.PhaseChanged -= HandlePhaseChanged;
            _phaseMatch = null;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase != MatchManager.Phase.Countdown && phase != MatchManager.Phase.Ready) return;

            _accumulated = 0f;
            _sinceFlush = 0f;
            for (int i = 0; i < _popups.Length; i++) HidePopup(i);
        }

        /// <summary>
        /// 构建飘字池（锚在屏幕中心，无任何常驻视觉）。幂等；主题缺失时只退化为纯文字，不抛异常
        /// （纯装饰性 HUD 失败不该让整局游戏起不来）。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;

            try
            {
                if (_rect == null) _rect = GetComponent<RectTransform>();

                // 锚准星：画布中心（飘字出生点 = 中心 + SpawnOffset，桌面/移动端同位）
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
                _rect.pivot = new Vector2(0.5f, 0.5f);
                _rect.anchoredPosition = Vector2.zero;
                _rect.sizeDelta = Vector2.zero;

                EnsurePopupPool(UIThemeSO.Resolve(theme));

                _built = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TurfScore] 构建涂地飘字失败（不影响游戏）：" + e.Message);
                _built = false;
            }
        }

        private void EnsurePopupPool(UIThemeSO resolved)
        {
            for (int i = 0; i < _popups.Length; i++)
            {
                if (_popups[i] != null) continue;

                var go = new GameObject("Popup_" + i, typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(SpawnOffsetX, SpawnOffsetY);
                rt.sizeDelta = new Vector2(160f, 40f);

                var group = go.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;

                var tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;

                var chunky = go.AddComponent<UIChunkyText>();
                chunky.Configure(resolved, "+0", UITypography.FontSnapped(24f),
                    displayFont: true, doubleLayered: true, fill: Color.white);
                chunky.SetUppercaseLatin(false);
                chunky.Build();

                _popups[i] = chunky;
                _popupRects[i] = rt;
                _popupGroups[i] = group;
                _popupActive[i] = false;
                _popupAge[i] = 0f;
                go.SetActive(false);
            }
        }

        private void HandleTurfRegistered(CombatantHealth painter, float squareMeters)
        {
            if (painter == null || squareMeters <= 0f) return;
            if (!CombatantHealth.IsLocalPlayer(painter)) return;

            // 这个回调挂在涂地热路径上（MatchStatsService.RegisterTurf 同步派发）：
            // HUD 装饰失败绝不能冒泡回武器/涂地逻辑，只记一次警告。
            try
            {
                _accumulated += squareMeters;
            }
            catch (System.Exception e)
            {
                if (!_handlerErrorLogged)
                {
                    _handlerErrorLogged = true;
                    Debug.LogWarning("[TurfScore] 涂地飘字累计失败（不影响涂地本身）：" + e.Message);
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            _sinceFlush += dt;
            if (TurfScoreMath.ShouldFlush(_accumulated, _sinceFlush, 0f, force: false))
            {
                FlushPopup(_accumulated);
                _accumulated = 0f;
                _sinceFlush = 0f;
            }

            TickPopups(dt);
        }

        /// <summary>
        /// 聚合窗口到点：把累计面积结算成一条准星飘字。
        /// 测试 / 调试可直接调用（不走 Update），传入面积即弹一条。
        /// </summary>
        public void FlushPopup(float squareMeters)
        {
            if (squareMeters < TurfScoreMath.MinPopupSquareMeters) return;
            if (!_built) EnsureBuilt();
            if (_popups.Length == 0) return;

            int index = _popupCursor;
            _popupCursor = (_popupCursor + 1) % _popups.Length;

            UIChunkyText popup = _popups[index];
            if (popup == null) return;

            bool big = squareMeters >= TurfScoreMath.BigPopupThresholdSquareMeters;
            TMP_Text comp = popup.TextComponent;
            if (comp != null) comp.fontSize = UITypography.FontSnapped(big ? 30f : 24f);
            popup.SetText(TurfScoreMath.FormatPopupRich(squareMeters));
            popup.SetFillColor(big ? _teamColor : Color.white);

            _popupSpawn[index] = new Vector2(
                SpawnOffsetX + Random.Range(-TurfScoreMath.PopupJitterPixels, TurfScoreMath.PopupJitterPixels),
                SpawnOffsetY);
            popup.gameObject.SetActive(true);
            _popupActive[index] = true;
            _popupAge[index] = 0f;

            RefreshTeamColor();

            // 只有大额飘字出声：小飘字触发太密，全播会让核心正反馈变成噪音。
            // 音频放在 FlushPopup（Update 里调用）而不是 HandleTurfRegistered（涂地热路径），
            // 避免音频代码跟着每一笔涂地同步执行。
            if (big) PlayPopupSound();
        }

        /// <summary>
        /// 大额飘字轻音：Toggle 比 Confirm 更轻、更短，不会和击杀确认音抢注意力。
        /// 双重节流——只在大额 + 距上次 ≥ 0.26s 时播，连续涂地不会叠成噪音。
        /// </summary>
        private void PlayPopupSound()
        {
            if (AudioManager.Instance == null) return;

            float now = Time.unscaledTime;
            if (now - _lastPopupSoundTime < PopupSoundIntervalSeconds) return;

            _lastPopupSoundTime = now;
            AudioManager.Instance.PlayUISound(UiSoundKind.Toggle);
        }

        private void TickPopups(float dt)
        {
            for (int i = 0; i < _popups.Length; i++)
            {
                if (!_popupActive[i]) continue;

                _popupAge[i] += dt;
                float progress = TurfScoreMath.PopupProgress(_popupAge[i], TurfScoreMath.PopupLifetimeSeconds);

                UIChunkyText popup = _popups[i];
                if (popup == null)
                {
                    _popupActive[i] = false;
                    continue;
                }

                RectTransform rt = _popupRects[i];
                if (rt != null)
                {
                    rt.anchoredPosition = _popupSpawn[i] + new Vector2(0f, TurfScoreMath.PopupRiseOffset(progress));
                    rt.localScale = Vector3.one * TurfScoreMath.PopupScale(progress);
                }

                if (_popupGroups[i] != null) _popupGroups[i].alpha = TurfScoreMath.PopupAlpha(progress);

                if (progress >= 1f) HidePopup(i);
            }
        }

        private void HidePopup(int index)
        {
            _popupActive[index] = false;
            _popupAge[index] = 0f;
            if (_popupGroups[index] != null) _popupGroups[index].alpha = 0f;
            if (_popups[index] != null)
            {
                if (_popupRects[index] != null) _popupRects[index].localScale = Vector3.one;
                _popups[index].gameObject.SetActive(false);
            }
        }

        /// <summary>大额飘字染己方墨色（InkSurfaceController 起来后也能补上）。</summary>
        private void RefreshTeamColor()
        {
            PlayerController player = PlayerController.LocalPlayer;
            CombatantHealth health = player != null ? player.Health : null;
            if (health != null)
            {
                InkSurfaceController surface = InkSurfaceController.Instance;
                _teamColor = surface != null ? surface.GetTeamColor(health.Team) : ComicStyle.Green;
            }
        }
    }
}
