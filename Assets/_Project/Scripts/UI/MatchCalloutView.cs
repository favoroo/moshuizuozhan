using System;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 局内 HUD 中央偏上成就播报视图（Geometric Bold 几何大胆风）：
    /// 把原来的「一整条英文」改成「图标圆章 + 中文大字 + ×N 徽章 + 连杀方块」。
    ///
    /// 参考主流射击游戏的连杀播报：玩家一眼只需要读三件事——
    /// 1. 发生了什么（双杀 / 三杀 / 全队击倒 / 反杀 / 终结连杀）；
    /// 2. 这次是几连（右侧 ×N 方块 + 底部连杀方块逐颗点亮）；
    /// 3. 情绪强度（大播报用黄底/黄边、图标翻转、Back-Out 入场）。
    ///
    /// 动效仍守几何风规范：入场 0.20s Back-Out（轻微过冲）+ alpha，停留后 0.12s 淡出；
    /// 不做弹簧、循环脉冲或无限呼吸。
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

        private const int MaxStreakPips = 8;
        private const float HomeY = 180f;

        // 紧凑型版面尺寸常量（对齐中文短词「双杀/三杀/连杀」视觉重心，杜绝原英文 620px 巨大空洞）
        private const float IconLeft = 36f;
        private const float IconSizeNormal = 48f;
        private const float IconSizeBig = 54f;
        private const float TextLeftWithIcon = 68f;
        private const float TextLeftNoIcon = 18f;
        private const float CountRight = 12f;
        private const float CountWidthNormal = 52f;
        private const float CountWidthBig = 58f;
        private const float TextRightNoCount = -18f;
        private const float PipSize = 9f;
        private const float PipPitch = 13f;

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

        [Header("成就装饰（运行时自动补齐）")]
        [SerializeField] private UIInkShapeGraphic accentGraphic;
        [SerializeField] private UIInkShapeGraphic iconBadgeGraphic;
        [SerializeField] private Image iconImage;
        [SerializeField] private UIInkShapeGraphic countBadgeGraphic;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private UIInkShapeGraphic[] streakPips = new UIInkShapeGraphic[MaxStreakPips];

        [Header("动效参数（规范：入场 0.20s Back-Out，其余硬切）")]
        [SerializeField] private float entranceDuration = 0.20f;
        [SerializeField] private float holdDurationNormal = 1.20f;
        [SerializeField] private float holdDurationBig = 1.45f;
        [SerializeField] private float fadeDuration = 0.12f;
        [SerializeField] private float samePriorityDebounceSeconds = 0.30f;
        [SerializeField] private float countPopDelay = 0.06f;
        [SerializeField] private float pipStagger = 0.03f;

        private RectTransform _rootRect;
        private RectTransform _mainRect;
        private RectTransform _subRect;
        private RectTransform _iconRect;
        private RectTransform _countRect;
        private RectTransform _pipsRect;
        private Vector2 _homePosition;

        private MatchCalloutMath _math = new MatchCalloutMath();
        private CalloutResult _currentCallout = CalloutResult.None;
        private DisplayState _state = DisplayState.Inactive;
        private float _stateTimer;
        private float _lastCalloutStartTime = -999f;
        private bool _subscribedPhase;
        private bool _isBuilt;
        private bool _iconAvailable;

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
                    float eased = UICubicBezier.BackOut.Evaluate(t);

                    // 主块：从 78% 弹到 100%，同时从上方 24px 砸回原位。
                    transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.78f, 1f, eased);
                    if (_rootRect != null)
                    {
                        _rootRect.anchoredPosition = _homePosition + new Vector2(0f, 24f * (1f - eased));
                    }
                    if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(t / 0.55f);

                    AnimateBadge(_iconRect, _stateTimer, 0f);
                    AnimateBadge(_countRect, _stateTimer, countPopDelay);
                    AnimatePips(_stateTimer);

                    if (t >= 1f)
                    {
                        transform.localScale = Vector3.one;
                        if (_rootRect != null) _rootRect.anchoredPosition = _homePosition;
                        if (canvasGroup != null) canvasGroup.alpha = 1f;
                        SnapBadge(_iconRect);
                        SnapBadge(_countRect);
                        SnapPips();
                        _state = DisplayState.Holding;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case DisplayState.Holding:
                {
                    transform.localScale = Vector3.one;
                    if (_rootRect != null) _rootRect.anchoredPosition = _homePosition;
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
                    transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.05f, t);

                    if (t >= 1f)
                    {
                        _state = DisplayState.Inactive;
                        _stateTimer = 0f;
                        _currentCallout = CalloutResult.None;
                        ResetVisualState();
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

            // 4. 重置动效计时（图标 / 计数 / 连杀方块在入场段内错峰弹出）
            _state = DisplayState.Entering;
            _stateTimer = 0f;
            transform.localScale = Vector3.one * 0.78f;
            if (_rootRect != null) _rootRect.anchoredPosition = _homePosition + new Vector2(0f, 24f);
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            if (_iconRect != null && _iconRect.gameObject.activeSelf)
            {
                _iconRect.localScale = Vector3.one * 0.45f;
            }
            if (_countRect != null && _countRect.gameObject.activeSelf)
            {
                _countRect.localScale = Vector3.one * 0.35f;
            }
            ResetPipScales(0.2f);
        }

        /// <summary>
        /// 立即隐藏并清空当前播报（切阶段或复位使用）。
        /// </summary>
        public void DismissImmediately()
        {
            _state = DisplayState.Inactive;
            _stateTimer = 0f;
            _currentCallout = CalloutResult.None;
            ResetVisualState();
        }

        private void ResetVisualState()
        {
            transform.localScale = Vector3.one;
            if (_rootRect != null) _rootRect.anchoredPosition = _homePosition;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            SnapBadge(_iconRect);
            SnapBadge(_countRect);
            SnapPips();
        }

        private static void AnimateBadge(RectTransform rect, float timer, float delay)
        {
            if (rect == null || !rect.gameObject.activeSelf) return;

            float t = Mathf.Clamp01((timer - delay) / 0.16f);
            float eased = UICubicBezier.BackOut.Evaluate(t);
            rect.localScale = Vector3.one * Mathf.LerpUnclamped(0.35f, 1f, eased);
        }

        private static void SnapBadge(RectTransform rect)
        {
            if (rect != null) rect.localScale = Vector3.one;
        }

        private void AnimatePips(float timer)
        {
            if (_pipsRect == null || !_pipsRect.gameObject.activeSelf) return;

            for (int i = 0; i < streakPips.Length; i++)
            {
                var pip = streakPips[i];
                if (pip == null || !pip.gameObject.activeSelf) continue;

                float t = Mathf.Clamp01((timer - countPopDelay - i * pipStagger) / 0.12f);
                float eased = UICubicBezier.BackOut.Evaluate(t);
                pip.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, eased);
            }
        }

        /// <summary>
        /// 按「已点亮颗数」把方块串整体居中，而不是按 8 格槽位居中。
        /// 否则 3~7 连时点亮段会偏左，看起来像贴在中文大字的左下角（数学居中、视觉不居中）。
        /// </summary>
        private void LayoutStreakPips(int litCount)
        {
            if (streakPips.Length == 0) return;

            int count = Mathf.Clamp(litCount, 1, streakPips.Length);
            float startX = -(count - 1) * PipPitch * 0.5f;

            for (int i = 0; i < streakPips.Length; i++)
            {
                var pip = streakPips[i];
                if (pip == null) continue;

                var rect = pip.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(startX + i * PipPitch, 0f);
                rect.sizeDelta = new Vector2(PipSize, PipSize);
            }
        }

        private void ResetPipScales(float scale)
        {
            for (int i = 0; i < streakPips.Length; i++)
            {
                if (streakPips[i] != null) streakPips[i].rectTransform.localScale = Vector3.one * scale;
            }
        }

        private void SnapPips()
        {
            for (int i = 0; i < streakPips.Length; i++)
            {
                if (streakPips[i] != null) streakPips[i].rectTransform.localScale = Vector3.one;
            }
        }

        private void ApplyVisuals(in CalloutResult result)
        {
            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            bool hasSub = !string.IsNullOrEmpty(result.SubText);
            bool isStreak = result.Kind == CalloutKind.Streak;
            bool hasCount = result.Count > 1 && (result.Kind == CalloutKind.MultiSplat || isStreak);

            // 1. 图标：取不到就整块隐藏，绝不留下白方块。
            string iconKey = result.IconKey;
            Sprite icon = string.IsNullOrEmpty(iconKey) ? null : UIIconLibrary.Get(iconKey);
            _iconAvailable = icon != null;
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = _iconAvailable;
            }
            if (iconBadgeGraphic != null)
            {
                iconBadgeGraphic.gameObject.SetActive(_iconAvailable);
            }

            // 2. 计数徽章：双杀 ×2 / 三杀 ×3 / 连杀 ×N
            if (countBadgeGraphic != null)
            {
                countBadgeGraphic.gameObject.SetActive(hasCount);
            }
            if (countText != null)
            {
                countText.text = hasCount ? "×" + result.Count : string.Empty;
                countText.gameObject.SetActive(hasCount);
            }

            // 3. 连杀方块：只在 STREAK 时逐颗点亮（最多 8 颗，超出部分由 ×N 承担）
            if (_pipsRect != null)
            {
                _pipsRect.gameObject.SetActive(isStreak && !hasSub);
            }
            int litPips = isStreak ? Mathf.Clamp(result.Count, 0, MaxStreakPips) : 0;
            for (int i = 0; i < streakPips.Length; i++)
            {
                if (streakPips[i] != null)
                {
                    streakPips[i].gameObject.SetActive(i < litPips);
                }
            }
            if (litPips > 0) LayoutStreakPips(litPips);

            // 4. 文字与字体（中文必须按 CJK 分支取字体，否则标题字重会缺字形）
            string main = result.MainText ?? string.Empty;
            if (mainText != null)
            {
                float mainFontSize = UITypography.FontSnapped(result.IsBig ? 44f : 36f);
                mainText.enableAutoSizing = true;
                mainText.fontSizeMin = UITypography.FontSnapped(22f);
                mainText.fontSizeMax = mainFontSize;
                mainText.fontSize = mainFontSize;
                mainText.textWrappingMode = TextWrappingModes.NoWrap;
                mainText.overflowMode = TextOverflowModes.Overflow;
                mainText.font = resolved.ResolveFont(true, main);
                mainText.text = main;
            }

            if (subText != null)
            {
                float subFontSize = UITypography.FontSnapped(16f);
                subText.enableAutoSizing = true;
                subText.fontSizeMin = UITypography.FontSnapped(14f);
                subText.fontSizeMax = subFontSize;
                subText.fontSize = subFontSize;
                subText.textWrappingMode = TextWrappingModes.NoWrap;
                subText.overflowMode = TextOverflowModes.Overflow;
                subText.font = resolved.ResolveFont(false, result.SubText ?? string.Empty);
                subText.text = result.SubText;
                subText.gameObject.SetActive(hasSub);
            }

            ApplyLayout(result.IsBig, hasSub, hasCount, isStreak);

            // 5. 配色方案（几何大胆风 token）
            Color fillColor = ComicStyle.Ink;
            Color borderColor = ComicStyle.Paper;
            float borderWidth = ComicStyle.Border;
            Color textColor = ComicStyle.Paper;
            Color subColor = ComicStyle.PaperAlt;
            Color accentColor = ComicStyle.Yellow;
            Color iconBadgeFill = ComicStyle.Yellow;
            Color iconBadgeBorder = ComicStyle.Ink;
            Color iconColor = ComicStyle.Ink;
            Color countFill = ComicStyle.Yellow;
            Color countBorder = ComicStyle.Ink;
            Color countColor = ComicStyle.Ink;

            switch (result.Kind)
            {
                case CalloutKind.Wipeout:
                    fillColor = ComicStyle.Yellow;
                    borderColor = ComicStyle.Ink;
                    borderWidth = ComicStyle.BorderHeavy;
                    textColor = ComicStyle.Ink;
                    subColor = ComicStyle.Ink;
                    accentColor = ComicStyle.Ink;
                    iconBadgeFill = ComicStyle.Ink;
                    iconBadgeBorder = ComicStyle.Yellow;
                    iconColor = ComicStyle.Paper;
                    break;

                case CalloutKind.MultiSplat:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Yellow;
                    borderWidth = result.IsBig ? ComicStyle.BorderHeavy : ComicStyle.Border;
                    textColor = ComicStyle.Paper;
                    subColor = ComicStyle.PaperAlt;
                    accentColor = ComicStyle.Yellow;
                    iconBadgeFill = ComicStyle.Yellow;
                    iconBadgeBorder = ComicStyle.Ink;
                    iconColor = ComicStyle.Ink;
                    break;

                case CalloutKind.FirstSplat:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Paper;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Paper;
                    subColor = ComicStyle.PaperAlt;
                    accentColor = ComicStyle.Paper;
                    iconBadgeFill = ComicStyle.Paper;
                    iconBadgeBorder = ComicStyle.Ink;
                    iconColor = ComicStyle.Ink;
                    break;

                case CalloutKind.Revenge:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Red;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Red;
                    subColor = ComicStyle.PaperAlt;
                    accentColor = ComicStyle.Red;
                    iconBadgeFill = ComicStyle.Red;
                    iconBadgeBorder = ComicStyle.Ink;
                    iconColor = ComicStyle.Paper;
                    countFill = ComicStyle.Red;
                    countColor = ComicStyle.Paper;
                    break;

                case CalloutKind.Shutdown:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Yellow;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Yellow;
                    subColor = ComicStyle.PaperAlt;
                    accentColor = ComicStyle.Yellow;
                    iconBadgeFill = ComicStyle.Yellow;
                    iconBadgeBorder = ComicStyle.Ink;
                    iconColor = ComicStyle.Ink;
                    break;

                case CalloutKind.Streak:
                    fillColor = ComicStyle.Ink;
                    borderColor = ComicStyle.Paper;
                    borderWidth = ComicStyle.Border;
                    textColor = ComicStyle.Paper;
                    subColor = ComicStyle.PaperAlt;
                    accentColor = ComicStyle.Yellow;
                    iconBadgeFill = ComicStyle.Yellow;
                    iconBadgeBorder = ComicStyle.Ink;
                    iconColor = ComicStyle.Ink;
                    countFill = ComicStyle.Yellow;
                    countBorder = ComicStyle.Ink;
                    countColor = ComicStyle.Ink;
                    break;
            }

            if (bgGraphic != null)
            {
                bgGraphic.ApplyComic(fillColor, border: borderWidth);
                bgGraphic.BorderColor = borderColor;
                // 浮起的成就条：面板档硬影 (4,-5)
                resolved.ApplyHardShadow(bgGraphic, UIHardShadowLevel.Panel);
            }

            if (accentGraphic != null)
            {
                accentGraphic.color = accentColor;
            }

            if (iconBadgeGraphic != null)
            {
                iconBadgeGraphic.color = iconBadgeFill;
                iconBadgeGraphic.BorderColor = iconBadgeBorder;
                resolved.ApplyHardShadow(iconBadgeGraphic, UIHardShadowLevel.Tag);
            }

            if (iconImage != null)
            {
                iconImage.color = iconColor;
            }

            if (countBadgeGraphic != null)
            {
                countBadgeGraphic.color = countFill;
                countBadgeGraphic.BorderColor = countBorder;
                resolved.ApplyHardShadow(countBadgeGraphic, UIHardShadowLevel.Tag);
            }

            if (countText != null)
            {
                countText.color = countColor;
                countText.font = resolved.ResolveFont(true, countText.text);
            }

            if (mainText != null)
            {
                mainText.color = textColor;
            }

            if (subText != null)
            {
                subText.color = subColor;
            }

            for (int i = 0; i < streakPips.Length; i++)
            {
                if (streakPips[i] != null) streakPips[i].color = ComicStyle.Yellow;
            }
        }

        /// <summary>
        /// 紧凑型版面自适应：
        /// - 连杀/多杀（有计数徽章）：宽 320/330，高 80/84，左右图标与计数方块紧密包围居中中文短词；
        /// - 首杀/反杀（无计数徽章）：宽 260，高 80，饱满利落；
        /// - 全队击倒/终结连杀（带副标题）：宽 410，高 108/112；
        /// - 底部连杀小方块（8 颗）：居中放置在卡片底部，对称点亮。
        /// </summary>
        private void ApplyLayout(bool isBig, bool hasSub, bool hasCount, bool isStreak)
        {
            float panelWidth;
            float panelHeight;

            if (hasSub)
            {
                panelWidth = 410f;
                panelHeight = isBig ? 112f : 108f;
            }
            else
            {
                panelWidth = hasCount ? (isBig ? 330f : 320f) : 260f;
                panelHeight = isBig ? 84f : 80f;
            }

            if (_rootRect != null)
            {
                _rootRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            }

            float iconSize = isBig ? IconSizeBig : IconSizeNormal;
            float iconY = hasSub ? 0.62f : 0.5f;
            if (_iconRect != null)
            {
                _iconRect.anchorMin = _iconRect.anchorMax = new Vector2(0f, iconY);
                _iconRect.pivot = new Vector2(0.5f, 0.5f);
                _iconRect.anchoredPosition = new Vector2(IconLeft, 0f);
                _iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            }

            float countWidth = isBig ? CountWidthBig : CountWidthNormal;
            if (_countRect != null)
            {
                _countRect.anchorMin = _countRect.anchorMax = new Vector2(1f, iconY);
                _countRect.pivot = new Vector2(0.5f, 0.5f);
                _countRect.anchoredPosition = new Vector2(-CountRight - countWidth * 0.5f, 0f);
                _countRect.sizeDelta = new Vector2(countWidth, countWidth);
            }

            float textLeft = _iconAvailable ? TextLeftWithIcon : TextLeftNoIcon;
            float textRight = hasCount ? -(CountRight + countWidth + 8f) : TextRightNoCount;

            if (_mainRect != null)
            {
                float bottom = hasSub ? 0.36f : (isStreak ? 0.20f : 0f);
                _mainRect.anchorMin = new Vector2(0f, bottom);
                _mainRect.anchorMax = Vector2.one;
                _mainRect.offsetMin = new Vector2(textLeft, 2f);
                _mainRect.offsetMax = new Vector2(textRight, -2f);
            }

            if (_subRect != null)
            {
                _subRect.anchorMin = Vector2.zero;
                _subRect.anchorMax = new Vector2(1f, hasSub ? 0.38f : 0f);
                _subRect.offsetMin = new Vector2(textLeft, 3f);
                _subRect.offsetMax = new Vector2(hasCount ? textRight : -18f, -1f);
            }

            if (_pipsRect != null)
            {
                _pipsRect.anchorMin = _pipsRect.anchorMax = new Vector2(0.5f, 0f);
                _pipsRect.pivot = new Vector2(0.5f, 0f);
                _pipsRect.anchoredPosition = new Vector2(0f, 7f);
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
            _rootRect.anchoredPosition = new Vector2(0f, HomeY);
            _rootRect.sizeDelta = new Vector2(320f, 80f);
            _homePosition = _rootRect.anchoredPosition;

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            // 1. 直角纯色墨底 + 左侧行动黄竖条（与 HUD 卡色带 / 决战横幅同一语言）
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
                resolved.ApplyHardShadow(bgGraphic, UIHardShadowLevel.Panel);
            }

            if (accentGraphic == null)
            {
                var accentGo = new GameObject("CalloutAccent",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                accentGo.transform.SetParent(transform, false);
                accentGraphic = accentGo.GetComponent<UIInkShapeGraphic>();
                var accentRect = (RectTransform)accentGo.transform;
                accentRect.anchorMin = new Vector2(0f, 0f);
                accentRect.anchorMax = new Vector2(0f, 1f);
                accentRect.pivot = new Vector2(0f, 0.5f);
                accentRect.anchoredPosition = new Vector2(5f, 0f);
                accentRect.sizeDelta = new Vector2(6f, -14f);

                accentGraphic.Shape = UIInkShape.SharpRect;
                accentGraphic.CornerRadius = ComicStyle.CornerSharp;
                accentGraphic.BorderWidth = 0f;
                accentGraphic.HardShadow = false;
                accentGraphic.Halftone = false;
                accentGraphic.GlossAlpha = 0f;
                accentGraphic.DrawGloss = false;
                accentGraphic.InnerRimAlpha = 0f;
                accentGraphic.color = ComicStyle.Yellow;
                accentGraphic.raycastTarget = false;
            }

            // 2. 图标圆章（成就符号：墨溅 / 交叉剑 / 闪电 / 皇冠 / 星）
            if (iconBadgeGraphic == null)
            {
                var badgeGo = new GameObject("IconBadge",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(transform, false);
                iconBadgeGraphic = badgeGo.GetComponent<UIInkShapeGraphic>();
                _iconRect = (RectTransform)badgeGo.transform;
                _iconRect.anchorMin = _iconRect.anchorMax = new Vector2(0f, 0.5f);
                _iconRect.pivot = new Vector2(0.5f, 0.5f);
                _iconRect.anchoredPosition = new Vector2(IconLeft, 0f);
                _iconRect.sizeDelta = new Vector2(IconSizeNormal, IconSizeNormal);

                iconBadgeGraphic.Shape = UIInkShape.Blob;
                iconBadgeGraphic.CornerRadius = ComicStyle.CornerBubble;
                iconBadgeGraphic.BorderWidth = ComicStyle.BorderThin;
                iconBadgeGraphic.BorderColor = ComicStyle.Ink;
                iconBadgeGraphic.Halftone = false;
                iconBadgeGraphic.GlossAlpha = 0f;
                iconBadgeGraphic.DrawGloss = false;
                iconBadgeGraphic.InnerRimAlpha = 0f;
                iconBadgeGraphic.color = ComicStyle.Yellow;
                iconBadgeGraphic.raycastTarget = false;
                resolved.ApplyHardShadow(iconBadgeGraphic, UIHardShadowLevel.Tag);

                var iconGo = new GameObject("Icon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(badgeGo.transform, false);
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(9f, 9f);
                iconRect.offsetMax = new Vector2(-9f, -9f);

                iconImage = iconGo.GetComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.color = ComicStyle.Ink;
                iconImage.raycastTarget = false;
                iconImage.enabled = false;
            }
            else if (_iconRect == null)
            {
                _iconRect = iconBadgeGraphic.rectTransform;
            }

            // 3. 主标题文字（中文大字）
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
            else if (_mainRect == null)
            {
                _mainRect = mainText.rectTransform;
            }

            if (mainText != null)
            {
                mainText.raycastTarget = false;
                mainText.alignment = TextAlignmentOptions.Center;
                mainText.fontStyle = FontStyles.Bold;
                mainText.fontSize = UITypography.FontSnapped(36f);
                mainText.color = ComicStyle.Paper;
                mainText.font = resolved.ResolveFont(true, "双杀");
            }

            // 4. 副标题文字（说明性短句）
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
            else if (_subRect == null)
            {
                _subRect = subText.rectTransform;
            }

            if (subText != null)
            {
                subText.raycastTarget = false;
                subText.alignment = TextAlignmentOptions.Center;
                subText.fontStyle = FontStyles.Bold;
                subText.fontSize = UITypography.FontSnapped(16f);
                subText.color = ComicStyle.PaperAlt;
                subText.font = resolved.ResolveFont(false, "敌方全员倒地");
                subText.gameObject.SetActive(false);
            }

            // 5. ×N 计数徽章（双杀/三杀/连杀）
            if (countBadgeGraphic == null)
            {
                var countGo = new GameObject("CountBadge",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                countGo.transform.SetParent(transform, false);
                countBadgeGraphic = countGo.GetComponent<UIInkShapeGraphic>();
                _countRect = (RectTransform)countGo.transform;
                _countRect.anchorMin = _countRect.anchorMax = new Vector2(1f, 0.5f);
                _countRect.pivot = new Vector2(0.5f, 0.5f);
                _countRect.anchoredPosition = new Vector2(-CountRight - CountWidthNormal * 0.5f, 0f);
                _countRect.sizeDelta = new Vector2(CountWidthNormal, CountWidthNormal);

                countBadgeGraphic.Shape = UIInkShape.SharpRect;
                countBadgeGraphic.CornerRadius = ComicStyle.CornerSharp;
                countBadgeGraphic.BorderWidth = ComicStyle.Border;
                countBadgeGraphic.BorderColor = ComicStyle.Ink;
                countBadgeGraphic.Halftone = false;
                countBadgeGraphic.GlossAlpha = 0f;
                countBadgeGraphic.DrawGloss = false;
                countBadgeGraphic.InnerRimAlpha = 0f;
                countBadgeGraphic.color = ComicStyle.Yellow;
                countBadgeGraphic.raycastTarget = false;
                resolved.ApplyHardShadow(countBadgeGraphic, UIHardShadowLevel.Tag);

                var countTxtGo = new GameObject("CountText",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                countTxtGo.transform.SetParent(countGo.transform, false);
                var countTxtRect = (RectTransform)countTxtGo.transform;
                countTxtRect.anchorMin = Vector2.zero;
                countTxtRect.anchorMax = Vector2.one;
                countTxtRect.offsetMin = new Vector2(2f, 2f);
                countTxtRect.offsetMax = new Vector2(-2f, -2f);

                countText = countTxtGo.GetComponent<TextMeshProUGUI>();
                countText.raycastTarget = false;
                countText.alignment = TextAlignmentOptions.Center;
                countText.fontStyle = FontStyles.Bold;
                countText.enableAutoSizing = true;
                countText.fontSizeMin = UITypography.FontSnapped(18f);
                countText.fontSizeMax = UITypography.FontSnapped(34f);
                countText.fontSize = UITypography.FontSnapped(34f);
                countText.color = ComicStyle.Ink;
                countText.font = resolved.ResolveFont(true, "×3");
                countText.textWrappingMode = TextWrappingModes.NoWrap;
                countText.overflowMode = TextOverflowModes.Overflow;
            }
            else if (_countRect == null)
            {
                _countRect = countBadgeGraphic.rectTransform;
            }

            // 6. 连杀方块行（8 颗，逐颗点亮，水平居中）
            if (_pipsRect == null)
            {
                Transform pTr = transform.Find("StreakPips");
                if (pTr != null)
                {
                    _pipsRect = (RectTransform)pTr;
                }
                else
                {
                    var pipsGo = new GameObject("StreakPips", typeof(RectTransform));
                    pipsGo.transform.SetParent(transform, false);
                    _pipsRect = (RectTransform)pipsGo.transform;
                }
            }

            float totalPipsWidth = (MaxStreakPips - 1) * PipPitch + PipSize;
            _pipsRect.anchorMin = _pipsRect.anchorMax = new Vector2(0.5f, 0f);
            _pipsRect.pivot = new Vector2(0.5f, 0f);
            _pipsRect.anchoredPosition = new Vector2(0f, 7f);
            _pipsRect.sizeDelta = new Vector2(totalPipsWidth, PipSize);

            float startX = -(totalPipsWidth - PipSize) * 0.5f;

            for (int i = 0; i < streakPips.Length; i++)
            {
                if (streakPips[i] == null)
                {
                    Transform pipTr = _pipsRect.Find("Pip" + i);
                    if (pipTr != null)
                    {
                        streakPips[i] = pipTr.GetComponent<UIInkShapeGraphic>();
                    }
                    else
                    {
                        var pipGo = new GameObject("Pip" + i,
                            typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                        pipGo.transform.SetParent(_pipsRect, false);
                        var pip = pipGo.GetComponent<UIInkShapeGraphic>();
                        pip.Shape = UIInkShape.SharpRect;
                        pip.CornerRadius = ComicStyle.CornerSharp;
                        pip.BorderWidth = 0f;
                        pip.HardShadow = false;
                        pip.Halftone = false;
                        pip.GlossAlpha = 0f;
                        pip.DrawGloss = false;
                        pip.InnerRimAlpha = 0f;
                        pip.color = ComicStyle.Yellow;
                        pip.raycastTarget = false;
                        streakPips[i] = pip;
                    }
                }

                if (streakPips[i] != null)
                {
                    var pipRect = streakPips[i].rectTransform;
                    pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0.5f);
                    pipRect.pivot = new Vector2(0.5f, 0.5f);
                    pipRect.anchoredPosition = new Vector2(startX + i * PipPitch, 0f);
                    pipRect.sizeDelta = new Vector2(PipSize, PipSize);
                    streakPips[i].gameObject.SetActive(false);
                }
            }

            if (countBadgeGraphic != null) countBadgeGraphic.gameObject.SetActive(false);
            if (_pipsRect != null) _pipsRect.gameObject.SetActive(false);
            if (iconBadgeGraphic != null) iconBadgeGraphic.gameObject.SetActive(false);
            ApplyLayout(false, false, false, false);
        }
    }
}
