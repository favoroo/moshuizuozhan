using System;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 局内 HUD 中央偏上大字播报视图（Geometric Bold 几何大胆风）：
    /// 对齐 INKWAVE 的 Callout 播报链，在对局中展示 WIPEOUT! / DOUBLE SPLAT! / FIRST SPLAT! 等。
    /// 纯色直角色块底板 + 超大粗体字，出现 0.10s linear 硬切，停留 1.1~1.4s，快速淡出。
    /// 高优先级打断低优先级，同优先级 0.3s 内去抖。
    /// </summary>
    [AddComponentMenu("P1/UI/Match Callout View")]
    [RequireComponent(typeof(RectTransform))]
    public class MatchCalloutView : MonoBehaviour
    {
        public enum DisplayState
        {
            Inactive,
            Entering,
            Holding,
            Fading
        }

        [Header("主题与字体")]
        [SerializeField] private UIThemeSO theme;

        [Tooltip("已退役：主场景里可能仍序列化着 TitanOne，代码不再读取（字体统一走主题 MiSans）")]
        [HideInInspector]
        [SerializeField] private TMP_FontAsset calloutFont;

        [Header("组件引用")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UIInkShapeGraphic bgGraphic;
        [SerializeField] private TextMeshProUGUI mainText;
        [SerializeField] private TextMeshProUGUI subText;

        [Header("动效参数（规范：≤100ms linear 入场，无弹簧）")]
        [SerializeField] private float entranceDuration = 0.10f;
        [SerializeField] private float holdDurationNormal = 1.20f;
        [SerializeField] private float holdDurationBig = 1.40f;
        [SerializeField] private float fadeDuration = 0.15f;
        [SerializeField] private float samePriorityDebounceSeconds = 0.30f;

        private RectTransform _rootRect;
        private RectTransform _mainRect;
        private RectTransform _subRect;

        private MatchCalloutMath _math = new MatchCalloutMath();
        private CalloutResult _currentCallout = CalloutResult.None;
        private DisplayState _state = DisplayState.Inactive;
        private float _stateTimer;
        private float _lastCalloutStartTime = -999f;
        private bool _subscribedPhase;
        private bool _isBuilt;

        public MatchCalloutMath Math => _math;
        public CalloutResult CurrentCallout => _currentCallout;
        public DisplayState State => _state;
        public bool IsShowing => _state != DisplayState.Inactive;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        public void SetMath(MatchCalloutMath math)
        {
            if (math != null) _math = math;
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            KillFeedEventHub.OnKillReported += HandleKillReported;
            TrySubscribePhase();
        }

        private void OnDisable()
        {
            KillFeedEventHub.OnKillReported -= HandleKillReported;
            TryUnsubscribePhase();
            DismissImmediately();
        }

        private void Start()
        {
            TrySubscribePhase();
        }

        private void Update()
        {
            if (!_subscribedPhase)
            {
                TrySubscribePhase();
            }

            if (_state == DisplayState.Inactive) return;

            float dt = Time.unscaledDeltaTime;
            _stateTimer += dt;

            switch (_state)
            {
                case DisplayState.Entering:
                {
                    float t = entranceDuration > 0.001f ? Mathf.Clamp01(_stateTimer / entranceDuration) : 1f;
                    transform.localScale = Vector3.LerpUnclamped(Vector3.one * 0.90f, Vector3.one, t);
                    if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);

                    if (t >= 1f)
                    {
                        transform.localScale = Vector3.one;
                        if (canvasGroup != null) canvasGroup.alpha = 1f;
                        _state = DisplayState.Holding;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case DisplayState.Holding:
                {
                    transform.localScale = Vector3.one;
                    if (canvasGroup != null) canvasGroup.alpha = 1f;
                    float hold = _currentCallout.IsBig ? holdDurationBig : holdDurationNormal;
                    if (_stateTimer >= hold)
                    {
                        _state = DisplayState.Fading;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case DisplayState.Fading:
                {
                    float t = fadeDuration > 0.001f ? Mathf.Clamp01(_stateTimer / fadeDuration) : 1f;
                    if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);

                    if (t >= 1f)
                    {
                        _state = DisplayState.Inactive;
                        _stateTimer = 0f;
                        _currentCallout = CalloutResult.None;
                        if (canvasGroup != null) canvasGroup.alpha = 0f;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 外部或事件驱动播放播报条目。
        /// </summary>
        public void PlayCallout(CalloutResult result)
        {
            if (!result.HasCallout) return;

            // 1. 优先级打断与去抖仲裁
            if (_state != DisplayState.Inactive)
            {
                if (result.Priority < _currentCallout.Priority)
                {
                    // 低优先级不得打断高优先级
                    return;
                }

                if (result.Priority == _currentCallout.Priority)
                {
                    // 相同优先级在去抖窗口内忽略，防止高频连击画面闪烁
                    if (Time.unscaledTime - _lastCalloutStartTime < samePriorityDebounceSeconds)
                    {
                        return;
                    }
                }
            }

            EnsureBuilt();

            // 2. 接收新播报并刷新文字与外观
            _currentCallout = result;
            _lastCalloutStartTime = Time.unscaledTime;

            ApplyVisuals(result);

            // 3. 伴随音效（大播报播放大招就绪确认音，常规播报播放 UI 确认音）
            PlaySoundFeedback(result);

            // 4. 重置动效计时
            _state = DisplayState.Entering;
            _stateTimer = 0f;
            transform.localScale = Vector3.one * 0.90f;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        /// <summary>
        /// 立即隐藏并清空当前播报（切阶段或复位使用）。
        /// </summary>
        public void DismissImmediately()
        {
            _state = DisplayState.Inactive;
            _stateTimer = 0f;
            _currentCallout = CalloutResult.None;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private void ApplyVisuals(in CalloutResult result)
        {
            bool hasSub = !string.IsNullOrEmpty(result.SubText);

            if (mainText != null)
            {
                // 移动端 52×1.28≈66 号时，"WIPEOUT!" 这类大字在 76px 高的主行里会纵向溢出；
                // TMP 自动缩字在「不超过设计字号」的前提下自适配到当前行高/行宽。
                float mainFontSize = UITypography.FontSnapped(result.IsBig ? 52f : 38f);
                mainText.enableAutoSizing = true;
                mainText.fontSizeMin = UITypography.FontSnapped(22f);
                mainText.fontSizeMax = mainFontSize;
                mainText.fontSize = mainFontSize;
                mainText.textWrappingMode = TextWrappingModes.NoWrap;
                mainText.overflowMode = TextOverflowModes.Overflow;
                mainText.text = result.MainText;
            }

            if (subText != null)
            {
                float subFontSize = UITypography.FontSnapped(18f);
                subText.enableAutoSizing = true;
                subText.fontSizeMin = UITypography.FontSnapped(16f);
                subText.fontSizeMax = subFontSize;
                subText.fontSize = subFontSize;
                subText.textWrappingMode = TextWrappingModes.NoWrap;
                subText.overflowMode = TextOverflowModes.Overflow;
                subText.text = result.SubText;
                subText.gameObject.SetActive(hasSub);
            }

            // 版面自适应：有副标题时面板加高
            if (_rootRect != null)
            {
                _rootRect.sizeDelta = hasSub ? new Vector2(620f, 112f) : new Vector2(540f, 82f);
            }

            if (_mainRect != null)
            {
                if (hasSub)
                {
                    _mainRect.anchorMin = new Vector2(0f, 0.32f);
                    _mainRect.anchorMax = new Vector2(1f, 1f);
                }
                else
                {
                    _mainRect.anchorMin = Vector2.zero;
                    _mainRect.anchorMax = Vector2.one;
                }
                // 左右 24px 内边距：大字永不贴住 6px 墨边（旧版直接拉到边缘，截图里像“字撑破了板”）。
                _mainRect.offsetMin = new Vector2(24f, 4f);
                _mainRect.offsetMax = new Vector2(-24f, -4f);
            }

            if (_subRect != null)
            {
                _subRect.anchorMin = new Vector2(0f, 0f);
                _subRect.anchorMax = new Vector2(1f, hasSub ? 0.36f : 0f);
                _subRect.offsetMin = new Vector2(24f, 4f);
                _subRect.offsetMax = new Vector2(-24f, 0f);
            }

            // 配色方案（几何大胆风 token）
            Color fillColor = ComicStyle.Ink;
            Color borderColor = ComicStyle.Border > 0f ? ComicStyle.Paper : ComicStyle.Ink;
            float borderWidth = ComicStyle.Border;
            Color textColor = ComicStyle.Paper;
            Color subColor = ComicStyle.PaperAlt;

            switch (result.Kind)
            {
                case CalloutKind.Wipeout:
                    fillColor = ComicStyle.Yellow;
                    borderColor = ComicStyle.Ink;
                    borderWidth = ComicStyle.BorderHeavy;
                    textColor = ComicStyle.Ink;
                    subColor = ComicStyle.Ink;
                    break;

                case CalloutKind.MultiSplat:
                    fillColor = ComicStyle.Ink;
                    borderColor = result.IsBig ? ComicStyle.Yellow : ComicStyle.Paper;
                    borderWidth = result.IsBig ? ComicStyle.BorderHeavy : ComicStyle.Border;
                    textColor = result.IsBig ? ComicStyle.Yellow : ComicStyle.Paper;
                    break;

                case CalloutKind.FirstSplat:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Paper;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Paper;
                    break;

                case CalloutKind.Revenge:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Red;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Red;
                    break;

                case CalloutKind.Shutdown:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Yellow;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Yellow;
                    subColor = ComicStyle.PaperAlt;
                    break;

                case CalloutKind.Streak:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Paper;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Paper;
                    break;
            }

            if (bgGraphic != null)
            {
                bgGraphic.ApplyComic(fillColor, border: borderWidth);
                bgGraphic.BorderColor = borderColor;
            }

            if (mainText != null)
            {
                mainText.color = textColor;
            }

            if (subText != null)
            {
                subText.color = subColor;
            }
        }

        private void PlaySoundFeedback(in CalloutResult result)
        {
            try
            {
                if (AudioManager.Instance != null)
                {
                    if (result.IsBig)
                    {
                        AudioManager.Instance.PlayUltReady();
                    }
                    else
                    {
                        AudioManager.Instance.PlayUISound(UiSoundKind.Confirm);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchCalloutView] Audio feedback failed: {ex.Message}");
            }
        }

        private void HandleKillReported(KillFeedEvent ev)
        {
            // 比赛对局中才播报
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase != MatchManager.Phase.Playing)
            {
                return;
            }

            try
            {
                var result = _math.Evaluate(ev, Time.time, CombatantHealthRegistry.All);
                if (result.HasCallout)
                {
                    PlayCallout(result);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchCalloutView] HandleKillReported failed: {ex.Message}");
            }
        }

        private void TrySubscribePhase()
        {
            if (_subscribedPhase) return;
            var match = MatchManager.Instance;
            if (match != null)
            {
                match.PhaseChanged += HandlePhaseChanged;
                _subscribedPhase = true;
            }
        }

        private void TryUnsubscribePhase()
        {
            if (!_subscribedPhase) return;
            var match = MatchManager.Instance;
            if (match != null)
            {
                match.PhaseChanged -= HandlePhaseChanged;
            }
            _subscribedPhase = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase != MatchManager.Phase.Playing)
            {
                DismissImmediately();
                _math.Reset();
            }
        }

        public void EnsureBuilt()
        {
            if (_isBuilt) return;

            try
            {
                BuildUI();
                _isBuilt = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchCalloutView] Failed to build callout UI: {ex.Message}");
            }
        }

        private void BuildUI()
        {
            _rootRect = GetComponent<RectTransform>();
            if (_rootRect == null) _rootRect = gameObject.AddComponent<RectTransform>();

            // 锚定于中央偏上（y=180），避开中心十字准星与顶部战况条
            _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            _rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRect.pivot = new Vector2(0.5f, 0.5f);
            _rootRect.anchoredPosition = new Vector2(0f, 180f);
            _rootRect.sizeDelta = new Vector2(540f, 82f);

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            TMP_FontAsset resolvedCalloutFont = UIThemeSO.Resolve(theme).GetFont(true);

            // 1. 直角纯色墨溅底板（UIInkShapeGraphic 纯网格直角色块）
            if (bgGraphic == null)
            {
                Transform bgTr = transform.Find("CalloutBg");
                if (bgTr != null)
                {
                    bgGraphic = bgTr.GetComponent<UIInkShapeGraphic>();
                }
                else
                {
                    var bgGo = new GameObject("CalloutBg",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                    bgGo.transform.SetParent(transform, false);
                    var bgRt = (RectTransform)bgGo.transform;
                    bgRt.anchorMin = Vector2.zero;
                    bgRt.anchorMax = Vector2.one;
                    bgRt.offsetMin = Vector2.zero;
                    bgRt.offsetMax = Vector2.zero;
                    bgGo.transform.SetAsFirstSibling();

                    bgGraphic = bgGo.GetComponent<UIInkShapeGraphic>();
                }
            }

            if (bgGraphic != null)
            {
                bgGraphic.raycastTarget = false;
                bgGraphic.ApplyComic(ComicStyle.Ink, border: ComicStyle.Border);
            }

            // 2. 主标题文字（MainText）
            if (mainText == null)
            {
                Transform mTr = transform.Find("MainText");
                if (mTr != null)
                {
                    mainText = mTr.GetComponent<TextMeshProUGUI>();
                    _mainRect = (RectTransform)mTr;
                }
                else
                {
                    var mainGo = new GameObject("MainText",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    mainGo.transform.SetParent(transform, false);
                    _mainRect = (RectTransform)mainGo.transform;
                    _mainRect.anchorMin = Vector2.zero;
                    _mainRect.anchorMax = Vector2.one;
                    _mainRect.offsetMin = Vector2.zero;
                    _mainRect.offsetMax = Vector2.zero;

                    mainText = mainGo.GetComponent<TextMeshProUGUI>();
                }
            }

            if (mainText != null)
            {
                mainText.raycastTarget = false;
                mainText.alignment = TextAlignmentOptions.Center;
                mainText.fontStyle = FontStyles.Bold;
                mainText.fontSize = UITypography.FontSnapped(38f);
                mainText.color = ComicStyle.Paper;
                if (resolvedCalloutFont != null) mainText.font = resolvedCalloutFont;
            }

            // 3. 副标题文字（SubText）
            if (subText == null)
            {
                Transform sTr = transform.Find("SubText");
                if (sTr != null)
                {
                    subText = sTr.GetComponent<TextMeshProUGUI>();
                    _subRect = (RectTransform)sTr;
                }
                else
                {
                    var subGo = new GameObject("SubText",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    subGo.transform.SetParent(transform, false);
                    _subRect = (RectTransform)subGo.transform;
                    _subRect.anchorMin = new Vector2(0f, 0f);
                    _subRect.anchorMax = new Vector2(1f, 0.36f);
                    _subRect.offsetMin = Vector2.zero;
                    _subRect.offsetMax = Vector2.zero;

                    subText = subGo.GetComponent<TextMeshProUGUI>();
                }
            }

            if (subText != null)
            {
                subText.raycastTarget = false;
                subText.alignment = TextAlignmentOptions.Center;
                subText.fontStyle = FontStyles.Bold;
                subText.fontSize = UITypography.FontSnapped(18f);
                subText.color = ComicStyle.PaperAlt;
                if (resolvedCalloutFont != null) subText.font = resolvedCalloutFont;
                subText.gameObject.SetActive(false);
            }
        }
    }
}
