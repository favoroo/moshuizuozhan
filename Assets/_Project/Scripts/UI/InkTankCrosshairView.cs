using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 准星旁墨水罐（P42-E，对标 INKWAVE <c>.iw-tank</c>）：竖直小罐挂在准星右侧
    /// （中心 +56,0，罐体 20×78），玻璃底 + 队伍墨色液面 + 手雷耗墨刻度线，
    /// 液面追值滚动（14/s 指数逼近），侧移时液面倾斜，回墨时冒气泡。
    ///
    /// 状态（全部反映在罐体上，取代底部墨水卡）：
    /// - 低墨（<see cref="HudGaugeMath.IsLowInk"/> 迟滞）：罐下弹「墨水不足」红签 + 罐框翻红
    ///   （进入瞬间硬切闪 3 下后保持稳定红，不循环；低墨警报音沿用原底部条的节奏与音量）；
    /// - 空罐（墨水打到 0 的边沿）：罐体一记 0.35s 衰减抖动（INKWAVE is-empty）；
    /// - 刻度线变红 = 墨水不够丢一颗手雷（INKWAVE is-nosub）；
    /// - 满罐且无告警持续 1.4s → 整罐淡出（INKWAVE is-idle），一有消耗立刻回来。
    ///
    /// 数据：<see cref="SetInk"/> 由 <see cref="HudController"/> 每帧喂入（与旧底部条同一来源），
    /// 本视图不读武器/机动逻辑，只做显示与告警。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Tank Crosshair View")]
    [RequireComponent(typeof(RectTransform))]
    public class InkTankCrosshairView : MonoBehaviour
    {
        private const string EditorLowInkClipPath = "Assets/_Project/Audio/SFX/low_ink_warning_1.wav";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;

        // 几何（桌面参考分辨率）：罐体 20×78，中心在准星右侧 56px
        private const float TankWidth = 20f;
        private const float TankHeight = 78f;
        private const float TankOffsetX = 56f;
        private const float Inset = 3f;

        // 刻度线双色（用户反馈：实色红杠太突兀）——常态半透明白，缺墨时柔红
        private static readonly Color s_thresholdIdle = ComicStyle.WithAlpha(ComicStyle.Paper, 0.55f);
        private static readonly Color s_thresholdWarn = ComicStyle.WithAlpha(ComicStyle.Red, 0.8f);

        private RectTransform _rect;
        private RectTransform _tankRect;
        private UIInkShapeGraphic _glass;
        private RectTransform _liquidRect;
        private UIInkShapeGraphic _liquid;
        private RectTransform _surfaceRect;
        private RectTransform _thresholdRect;
        private Image _thresholdLine;
        private CanvasGroup _canvasGroup;
        private UIInkShapeGraphic _lowTag;
        private TMP_Text _lowTagText;

        private readonly UIInkShapeGraphic[] _bubbles = new UIInkShapeGraphic[InkTankCrosshairMath.BubblePoolSize];
        private readonly RectTransform[] _bubbleRects = new RectTransform[InkTankCrosshairMath.BubblePoolSize];
        private readonly float[] _bubbleAge = new float[InkTankCrosshairMath.BubblePoolSize];
        private readonly bool[] _bubbleActive = new bool[InkTankCrosshairMath.BubblePoolSize];
        private int _bubbleCursor;

        private Team _team = Team.Neutral;
        private bool _teamResolved;
        private float _ink01 = 1f;
        private float _level = 1f;
        private float _prevInk01 = 1f;
        private float _threshold01;
        private bool _low;
        private float _lowBlinkAge = -1f;
        private float _nextWarningTime;
        private float _emptyShakeAge = -1f;
        private float _idleSeconds;
        private float _visibleAlpha = 1f;
        private int _warningPlayCount;
        private bool _hostSuppressed;
        private bool _suppressEntryAlert;
        private bool _built;
        private bool _handlerErrorLogged;
        private AudioClip _lowInkClip;
        private AudioSource _warningSource;
        private TopDownCameraFollow _camera;
        private MatchManager _phaseMatch;

        /// <summary>当前是否处于低墨告警（带迟滞）。</summary>
        public bool IsLowInk => _low;

        /// <summary>低墨警报音已触发次数（供测试 / 调试读取）。</summary>
        public int WarningPlayCount => _warningPlayCount;

        /// <summary>喂入的墨水量 0..1。</summary>
        public float Ink01 => _ink01;

        /// <summary>液面显示值 0..1（追值后的平滑读数）。</summary>
        public float Level01 => _level;

        /// <summary>手雷耗墨刻度 0..1。</summary>
        public float Threshold01 => _threshold01;

        /// <summary>罐体是否处于空闲淡出（满罐省视觉）。</summary>
        public bool IsIdleHidden => _built && _canvasGroup != null && _canvasGroup.alpha < 0.05f;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            SubscribePhaseChanged();
            ApplyTeamColors();
        }

        private void OnDisable()
        {
            UnsubscribePhaseChanged();
        }

        /// <summary>新一局开始：墨水回满、告警复位。</summary>
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

            _ink01 = 1f;
            _prevInk01 = 1f;
            _level = 1f;
            _idleSeconds = 0f;
            _lowBlinkAge = -1f;
            _emptyShakeAge = -1f;
            ResetAlertState(true);
            ApplyLiquid();
            HideAllBubbles();
        }

        /// <summary>
        /// 构建罐体。幂等；主题 / 图形缺失时只退化外观，不抛异常
        /// （纯装饰性 HUD 失败不该让整局游戏起不来）。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;

            try
            {
                if (_rect == null) _rect = GetComponent<RectTransform>();

                // 锚准星：与 TurfScoreView 同款中心锚点
                _rect.anchorMin = new Vector2(0.5f, 0.5f);
                _rect.anchorMax = new Vector2(0.5f, 0.5f);
                _rect.pivot = new Vector2(0.5f, 0.5f);
                _rect.anchoredPosition = Vector2.zero;
                _rect.sizeDelta = Vector2.zero;

                _canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;

                EnsureTank();
                EnsureLiquid();
                EnsureThresholdLine();
                EnsureLowTag();
                EnsureBubbles();

                _built = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[InkTankCrosshair] 构建准星墨水罐失败（不影响游戏）：" + e.Message);
                _built = false;
            }
        }

        private void EnsureTank()
        {
            if (_tankRect != null) return;

            var tankGo = new GameObject("Tank", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            tankGo.transform.SetParent(transform, false);
            _tankRect = tankGo.GetComponent<RectTransform>();
            _tankRect.anchorMin = _tankRect.anchorMax = new Vector2(0.5f, 0.5f);
            _tankRect.pivot = new Vector2(0.5f, 0.5f);
            _tankRect.anchoredPosition = new Vector2(TankOffsetX, 0f);
            _tankRect.sizeDelta = new Vector2(TankWidth, TankHeight);

            _glass = tankGo.GetComponent<UIInkShapeGraphic>();
            _glass.Shape = UIInkShape.Pill;
            _glass.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f); // INKWAVE 玻璃底 rgba(14,11,22,.62)
            _glass.BorderWidth = ComicStyle.BorderThin;
            _glass.BorderColor = ComicStyle.Ink;
            _glass.GlossAlpha = 0f;
            _glass.WobbleAmplitude = 0f;
            _glass.Halftone = false;
            _glass.raycastTarget = false;
            UIThemeSO.Resolve(theme).ApplyHardShadow(_glass, UIHardShadowLevel.Tag);
        }

        private void EnsureLiquid()
        {
            if (_liquid != null) return;

            var go = new GameObject("Liquid", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(_tankRect, false);
            _liquidRect = go.GetComponent<RectTransform>();
            _liquidRect.anchorMin = new Vector2(0.5f, 0f);
            _liquidRect.anchorMax = new Vector2(0.5f, 0f);
            _liquidRect.pivot = new Vector2(0.5f, 0f);
            _liquidRect.anchoredPosition = new Vector2(0f, Inset);
            _liquidRect.sizeDelta = new Vector2(TankWidth - Inset * 2f, TankHeight - Inset * 2f);

            _liquid = go.GetComponent<UIInkShapeGraphic>();
            _liquid.Shape = UIInkShape.Pill;
            _liquid.BorderWidth = 0f;
            _liquid.GlossAlpha = 0f;
            _liquid.WobbleAmplitude = 0f;
            _liquid.Halftone = false;
            _liquid.raycastTarget = false;

            // 液面亮条：宽同液柱，随侧移倾斜（INKWAVE 波浪液面的等效简化）
            var surfaceGo = new GameObject("Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            surfaceGo.transform.SetParent(_liquidRect, false);
            _surfaceRect = surfaceGo.GetComponent<RectTransform>();
            _surfaceRect.anchorMin = new Vector2(0.5f, 1f);
            _surfaceRect.anchorMax = new Vector2(0.5f, 1f);
            _surfaceRect.pivot = new Vector2(0.5f, 0.5f);
            _surfaceRect.anchoredPosition = new Vector2(0f, -2f);
            _surfaceRect.sizeDelta = new Vector2(TankWidth - Inset * 2f, 3.5f);
            var surfaceImage = surfaceGo.GetComponent<Image>();
            surfaceImage.sprite = UIFlatSprite.White;
            surfaceImage.color = ComicStyle.WithAlpha(ComicStyle.Paper, 0.75f);
            surfaceImage.raycastTarget = false;
        }

        private void EnsureThresholdLine()
        {
            if (_thresholdLine != null) return;

            var go = new GameObject("Threshold", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_tankRect, false);
            _thresholdRect = go.GetComponent<RectTransform>();
            _thresholdRect.anchorMin = new Vector2(0.5f, 0f);
            _thresholdRect.anchorMax = new Vector2(0.5f, 0f);
            _thresholdRect.pivot = new Vector2(0.5f, 0.5f);
            // 内收细线：留在玻璃宽度内、不再探出罐体（用户反馈整条红杠太突兀）
            _thresholdRect.sizeDelta = new Vector2(TankWidth - Inset * 2f + 2f, 2f);

            _thresholdLine = go.GetComponent<Image>();
            _thresholdLine.sprite = UIFlatSprite.White;
            _thresholdLine.color = s_thresholdIdle;
            _thresholdLine.raycastTarget = false;
        }

        private void EnsureLowTag()
        {
            if (_lowTag != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            var go = new GameObject("LowTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(_tankRect, false);
            var tagRect = go.GetComponent<RectTransform>();
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0.5f, 0.5f);
            tagRect.pivot = new Vector2(0.5f, 1f);
            tagRect.anchoredPosition = new Vector2(0f, -TankHeight * 0.5f - 8f);
            tagRect.sizeDelta = new Vector2(104f, 26f);

            _lowTag = go.GetComponent<UIInkShapeGraphic>();
            _lowTag.Shape = UIInkShape.Pill;
            _lowTag.color = ComicStyle.Red;
            _lowTag.BorderWidth = 0f;
            _lowTag.GlossAlpha = 0f;
            _lowTag.WobbleAmplitude = 0f;
            _lowTag.Halftone = false;
            _lowTag.raycastTarget = false;
            resolved.ApplyHardShadow(_lowTag, UIHardShadowLevel.Tag);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            _lowTagText = textGo.GetComponent<TextMeshProUGUI>();
            _lowTagText.text = "墨水不足";
            _lowTagText.fontSize = UITypography.FontSnapped(14f);
            _lowTagText.color = ComicStyle.Paper;
            _lowTagText.alignment = TextAlignmentOptions.Center;
            _lowTagText.raycastTarget = false;
            TMP_FontAsset font = resolved.ResolveFont(false, "墨水不足");
            if (font != null) _lowTagText.font = font;

            go.SetActive(false);
        }

        private void EnsureBubbles()
        {
            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (_bubbles[i] != null) continue;

                var go = new GameObject("Bubble_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                go.transform.SetParent(_liquidRect, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(5f, 5f);

                var bubble = go.GetComponent<UIInkShapeGraphic>();
                bubble.Shape = UIInkShape.Pill;
                bubble.color = ComicStyle.WithAlpha(ComicStyle.Paper, 0.6f);
                bubble.BorderWidth = 0f;
                bubble.GlossAlpha = 0f;
                bubble.WobbleAmplitude = 0f;
                bubble.Halftone = false;
                bubble.raycastTarget = false;

                _bubbles[i] = bubble;
                _bubbleRects[i] = rt;
                _bubbleActive[i] = false;
                _bubbleAge[i] = 0f;
                go.SetActive(false);
            }
        }

        /// <summary>每帧由 HudController 喂入当前墨水（绝对值 + 上限）。</summary>
        public void SetInk(float currentInk, float maxInk)
        {
            if (!_built) EnsureBuilt();

            _ink01 = maxInk > 0.01f ? Mathf.Clamp01(currentInk / maxInk) : 0f;
        }

        /// <summary>手雷刻度：传入 0..1（= bombInkCost / maxInk）。</summary>
        public void SetThreshold01(float threshold01)
        {
            _threshold01 = Mathf.Clamp01(threshold01);
        }

        /// <summary>
        /// 进入不可见态（HUD 画布关闭 / 开新一局）：清掉告警状态，并让回战斗后的首次进入告警延后一个间隔。
        /// </summary>
        public void ResetAlertState(bool suppressEntryAlert = true)
        {
            _low = false;
            _lowBlinkAge = -1f;
            _nextWarningTime = 0f;
            _suppressEntryAlert = suppressEntryAlert;
            ApplyLowVisuals();
        }

        private void Update()
        {
            if (!_built) EnsureBuilt();
            if (!_built) return;

            // HUD 画布隐藏（首页 / 结算）时静默：沿用 InkTankView 的历史教训——
            // 只关画布不停 Update，残留低墨值会在结算页继续蜂鸣。
            if (!IsHostCanvasVisible())
            {
                SuppressAlerts();
                return;
            }

            _hostSuppressed = false;

            float dt = Time.deltaTime;
            HudGaugeStyleSO resolved = HudGaugeStyleSO.Resolve(style);

            // 空罐边沿：墨水打到 0 的那一刻抖一记
            if (_ink01 <= 0.001f && _prevInk01 > 0.001f) _emptyShakeAge = 0f;
            _prevInk01 = _ink01;

            // 低墨迟滞 + 警报音（节奏与音量沿用旧底部条口径）
            bool lowNow = HudGaugeMath.IsLowInk(_ink01, resolved.lowInkThreshold01, _low, resolved.lowInkHysteresis);
            if (lowNow != _low)
            {
                _low = lowNow;
                if (_low)
                {
                    _lowBlinkAge = 0f;
                    _nextWarningTime = Time.unscaledTime + resolved.warningRepeatInterval;
                    // 回战斗首帧的残留低墨值不算告警
                    if (_suppressEntryAlert) _suppressEntryAlert = false;
                    else TriggerWarning(resolved);
                }
                else
                {
                    _lowBlinkAge = -1f;
                }
            }

            if (_low && Time.unscaledTime >= _nextWarningTime)
            {
                _nextWarningTime = Time.unscaledTime + Mathf.Max(0.2f, resolved.warningRepeatInterval);
                TriggerWarning(resolved);
            }

            // 液面追值 + 回墨气泡
            bool refilling = _ink01 > _level + 0.0005f;
            _level = InkTankCrosshairMath.StepLevel(_level, _ink01, dt);
            if (refilling) SpawnBubble();
            TickBubbles(dt);

            // 空闲淡出：满罐 + 无低墨 → 省一处常驻视觉；任何消耗立刻拉回
            bool fullAndCalm = _level >= InkTankCrosshairMath.FullLevel01 && !_low;
            _idleSeconds = InkTankCrosshairMath.StepIdle(_idleSeconds, fullAndCalm, dt);
            float targetAlpha = InkTankCrosshairMath.ShouldFade(_idleSeconds) ? 0f : 1f;
            _visibleAlpha = Mathf.MoveTowards(_visibleAlpha, targetAlpha, dt / 0.4f);
            if (_canvasGroup != null) _canvasGroup.alpha = _visibleAlpha;

            if (_lowBlinkAge >= 0f) _lowBlinkAge += dt;
            if (_emptyShakeAge >= 0f) _emptyShakeAge += dt;

            ApplyLiquid();
            ApplyLowVisuals();
        }

        /// <summary>液柱高度 / 液面倾角 / 刻度线 / 罐体抖动，每帧刷新（值不变时不写，稳态零 GC）。</summary>
        private void ApplyLiquid()
        {
            if (_liquidRect != null)
            {
                float inner = TankHeight - Inset * 2f;
                float h = Mathf.Max(2f, _level * inner);
                Vector2 size = _liquidRect.sizeDelta;
                if (!Mathf.Approximately(size.y, h)) _liquidRect.sizeDelta = new Vector2(size.x, h);
            }

            if (_surfaceRect != null)
            {
                float lateral = 0f;
                PlayerController player = PlayerController.LocalPlayer;
                if (player != null && player.Motor != null)
                {
                    if (_camera == null) _camera = FindAnyObjectByType<TopDownCameraFollow>();
                    if (_camera != null)
                    {
                        lateral = Vector3.Dot(player.Motor.PlanarVelocity, _camera.PlanarRight);
                    }
                }

                float tilt = InkTankCrosshairMath.TiltDegrees(lateral);
                var euler = _surfaceRect.localEulerAngles;
                if (!Mathf.Approximately(euler.z, tilt)) _surfaceRect.localEulerAngles = new Vector3(0f, 0f, tilt);
            }

            if (_tankRect != null)
            {
                // 空罐抖动：整罐一记衰减摆动（INKWAVE iw-shake）
                float shake = InkTankCrosshairMath.EmptyShakeOffset(_emptyShakeAge);
                var tankEuler = _tankRect.localEulerAngles;
                if (!Mathf.Approximately(tankEuler.z, shake)) _tankRect.localEulerAngles = new Vector3(0f, 0f, shake);
            }

            if (_thresholdRect != null)
            {
                float inner = TankHeight - Inset * 2f;
                float y = Inset + Mathf.Clamp01(_threshold01) * inner;
                Vector2 pos = _thresholdRect.anchoredPosition;
                if (!Mathf.Approximately(pos.y, y)) _thresholdRect.anchoredPosition = new Vector2(pos.x, y);

                // 刻度线配色：常态半透明白（淡淡的刻度），墨水不够丢雷时才转柔红提示——不做实色大杠
                bool nosub = _threshold01 > 0.001f && _ink01 < _threshold01;
                Color want = _threshold01 <= 0.001f
                    ? Color.clear
                    : (nosub ? s_thresholdWarn : s_thresholdIdle);
                if (_thresholdLine != null && _thresholdLine.color != want) _thresholdLine.color = want;
            }
        }

        /// <summary>低墨视觉：罐框翻红（进入瞬间硬切闪 3 下后稳定红）+「墨水不足」红签。</summary>
        private void ApplyLowVisuals()
        {
            float blink = _low ? InkTankCrosshairMath.LowBlinkWeight(_lowBlinkAge) : 0f;
            bool red = _low && blink > 0.5f;

            if (_glass != null)
            {
                Color wantBorder = red ? ComicStyle.Red : ComicStyle.Ink;
                if (_glass.BorderColor != wantBorder) _glass.BorderColor = wantBorder;
            }

            if (_lowTag != null && _lowTag.gameObject.activeSelf != _low)
            {
                _lowTag.gameObject.SetActive(_low);
            }
        }

        // ── 回墨气泡 ─────────────────────────────────────────────────

        private void SpawnBubble()
        {
            int index = _bubbleCursor;
            _bubbleCursor = (_bubbleCursor + 1) % _bubbles.Length;

            if (_bubbleActive[index] || _bubbleRects[index] == null) return;

            _bubbleActive[index] = true;
            _bubbleAge[index] = 0f;
            _bubbleRects[index].anchoredPosition = new Vector2(
                Random.Range(-4f, 4f),
                Random.Range(0f, 6f));
            _bubbles[index].gameObject.SetActive(true);
        }

        private void TickBubbles(float dt)
        {
            if (_liquidRect == null) return;

            float inner = TankHeight - Inset * 2f;
            for (int i = 0; i < _bubbles.Length; i++)
            {
                if (!_bubbleActive[i]) continue;

                _bubbleAge[i] += dt;
                float progress = InkTankCrosshairMath.BubbleProgress(_bubbleAge[i]);

                // 到液面即消：罐内比例坐标 → 液柱内像素
                float surface01 = Mathf.Max(0.06f, _level);
                if (progress >= surface01)
                {
                    HideBubble(i);
                    continue;
                }

                if (_bubbleRects[i] != null)
                {
                    var pos = _bubbleRects[i].anchoredPosition;
                    pos.y = progress * inner;
                    _bubbleRects[i].anchoredPosition = pos;
                }
            }
        }

        private void HideBubble(int index)
        {
            _bubbleActive[index] = false;
            _bubbleAge[index] = 0f;
            if (_bubbles[index] != null) _bubbles[index].gameObject.SetActive(false);
        }

        private void HideAllBubbles()
        {
            for (int i = 0; i < _bubbles.Length; i++) HideBubble(i);
        }

        // ── 队伍色 / 警报音（沿用旧底部条口径）───────────────────────

        private void LateUpdate()
        {
            PlayerController player = PlayerController.LocalPlayer;
            if (player == null || player.Health == null) return;

            Team localTeam = player.Health.Team;
            if (localTeam == _team && _teamResolved) return;

            _team = localTeam;
            _teamResolved = true;
            ApplyTeamColors();
        }

        private void ApplyTeamColors()
        {
            Color ink = InkTeamColorFilter.ResolveTeamColor(_team, theme);
            if (_liquid != null) _liquid.color = ink;
        }

        private void TriggerWarning(HudGaugeStyleSO resolved)
        {
            _warningPlayCount++;

            AudioClip clip = ResolveLowInkClip();
            if (clip == null) return;

            AudioManager manager = AudioManager.Instance;
            if (manager != null)
            {
                manager.PlayOne(clip, resolved.lowInkWarningVolume, 0.98f, 1.02f);
                return;
            }

            if (_warningSource == null)
            {
                _warningSource = gameObject.AddComponent<AudioSource>();
                _warningSource.playOnAwake = false;
                _warningSource.spatialBlend = 0f;
            }

            _warningSource.PlayOneShot(clip, resolved.lowInkWarningVolume);
        }

        private AudioClip ResolveLowInkClip()
        {
            if (_lowInkClip != null) return _lowInkClip;

            AudioManager manager = AudioManager.Instance;
            if (manager != null && manager.lowInkWarningClip != null)
            {
                _lowInkClip = manager.lowInkWarningClip;
                return _lowInkClip;
            }

#if UNITY_EDITOR
            _lowInkClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(EditorLowInkClipPath);
#endif
            return _lowInkClip;
        }

        private void SuppressAlerts()
        {
            if (_hostSuppressed) return;

            _hostSuppressed = true;
            ResetAlertState(true);
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            _visibleAlpha = 0f;
        }

        /// <summary>宿主 HUD 画布是否可见（HudController 隐藏 HUD 时只关画布不停 GameObject）。</summary>
        public bool IsHostCanvasVisible()
        {
            if (!isActiveAndEnabled) return false;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return true; // 无画布（纯逻辑/测试）时不做门禁

            Canvas root = canvas.rootCanvas;
            return root != null && root.enabled && root.gameObject.activeInHierarchy;
        }
    }
}
