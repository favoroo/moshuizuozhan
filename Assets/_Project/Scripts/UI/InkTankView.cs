using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 底部横向墨水条（P14-H：由旧的右上角竖罐改成血条上方的横向纯色条）。
    ///
    /// 职责：
    /// 1. 用 <see cref="Image.fillAmount"/> 显示剩余墨水（纯色直角条，无渐变/无圆角/无波浪）；
    /// 2. 标注副武器（手雷）耗墨刻度：横向条上的**竖红线**，位置由 <see cref="SetThreshold01"/> 实时给入；
    /// 3. 墨水低于阈值时轨道描边与红光闪烁，并在 <see cref="Update"/> 里按间隔播放 low_ink 警报音。
    ///
    /// 所有数值来自 <see cref="HudGaugeStyleSO"/>，配色来自 <see cref="UIThemeSO"/> 与队伍墨色。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Tank View")]
    public class InkTankView : MonoBehaviour
    {
        private const string EditorLowInkClipPath = "Assets/_Project/Audio/SFX/low_ink_warning_1.wav";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;
        [SerializeField] private Image barFill;
        [SerializeField] private RectTransform thresholdMarker;
        [SerializeField] private UIInkShapeGraphic frameShape;
        [SerializeField] private UIInkShapeGraphic warningGlow;
        [SerializeField] private TMP_Text percentLabel;
        [SerializeField] private AudioSource warningSource;

        [Header("卡片外壳（t-1：顶部身份色带 + 墨底徽章）")]
        [SerializeField] private UIInkShapeGraphic cardRail;
        [SerializeField] private UIInkShapeGraphic badgePlate;
        [SerializeField] private Image badgeIcon;

        [Tooltip("留空则依次尝试 AudioManager.lowInkWarningClip 与编辑器内置路径")]
        [SerializeField] private AudioClip lowInkClip;

        [SerializeField] private bool autoBindLocalPlayerTeam = true;

        private float _thresholdInset = 2f;

        private Team _team = Team.Neutral;
        private float _ink01 = 1f;
        private float _threshold01 = 0.55f;
        private bool _low;
        private float _blinkPhase;
        private float _nextWarningTime;
        private int _displayedPercent = -1;
        private Canvas _rootCanvas;
        private bool _hostSuppressed;
        private bool _suppressEntryAlert;

        private void Awake()
        {
            EnsureSubCanvas();
        }

        private void EnsureSubCanvas()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = false;
                var parentCanvas = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
                if (parentCanvas != null)
                {
                    canvas.additionalShaderChannels = parentCanvas.additionalShaderChannels | AdditionalCanvasShaderChannels.TexCoord1;
                }
                else
                {
                    canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                }
            }
        }

        /// <summary>当前是否处于低墨告警（带迟滞）。</summary>
        public bool IsLowInk => _low;

        /// <summary>低墨警报被触发的累计次数（含首次进入与重复提醒），供测试与调试读取。</summary>
        public int WarningPlayCount { get; private set; }

        public float Ink01 => _ink01;

        /// <summary>手雷刻度位置 0..1。</summary>
        public float Threshold01 => _threshold01;

        /// <summary>横向条填充图（0..1 由 fillAmount 驱动）。</summary>
        public Image BarFill => barFill;

        public void Configure(UIThemeSO themeAsset, HudGaugeStyleSO styleAsset,
            Image fill, RectTransform marker, UIInkShapeGraphic frame,
            UIInkShapeGraphic warning, TMP_Text percent, float thresholdInset)
        {
            theme = themeAsset;
            style = styleAsset;
            barFill = fill;
            thresholdMarker = marker;
            frameShape = frame;
            warningGlow = warning;
            percentLabel = percent;
            _thresholdInset = Mathf.Max(0f, thresholdInset);

            ApplyTeamColors();
            ApplyThreshold();
            ApplyPercent(true);
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            theme = themeAsset;
            ApplyTeamColors();
        }

        /// <summary>
        /// 卡片外壳绑定（t-1 底部三卡）：身份色带 / 墨底徽章 / 水滴图标随队伍墨色硬切。
        /// 新增入口，旧的 <see cref="Configure"/> 签名与调用方零变化。
        /// </summary>
        public void SetCardChrome(UIInkShapeGraphic rail, UIInkShapeGraphic plate, Image icon)
        {
            cardRail = rail;
            badgePlate = plate;
            badgeIcon = icon;
            ApplyTeamColors();
        }

        public void SetStyle(HudGaugeStyleSO styleAsset)
        {
            style = styleAsset;
            ApplyThreshold();
        }

        /// <summary>每帧由 HudController 喂入当前墨水（绝对值 + 上限）。</summary>
        public void SetInk(float currentInk, float maxInk)
        {
            float next = maxInk > 0.01f ? Mathf.Clamp01(currentInk / maxInk) : 0f;
            if (Mathf.Abs(next - _ink01) > 0.001f)
            {
                // 真实数据已经到位：离场残留值造成的一次性抑制到此为止
                _suppressEntryAlert = false;
            }

            _ink01 = next;

            if (barFill != null) barFill.fillAmount = _ink01;
            ApplyPercent();
        }

        /// <summary>手雷刻度：传入 0..1（一般 = bombInkCost / maxInk）。</summary>
        public void SetThreshold01(float threshold01)
        {
            float clamped = Mathf.Clamp01(threshold01);
            if (Mathf.Abs(clamped - _threshold01) < 0.0005f) return;

            _threshold01 = clamped;
            ApplyThreshold();
        }

        /// <summary>切换队伍墨色（填充条跟随队伍色）。</summary>
        public void SetTeam(Team team)
        {
            _team = team;
            ApplyTeamColors();
        }

        public void SetAutoBindLocalPlayerTeam(bool enabled)
        {
            autoBindLocalPlayerTeam = enabled;
        }

        /// <summary>
        /// 兼容入口（移动端布局会调用）：横向条本身位置/尺寸不随输入方式变化，
        /// 这里只重算一次刻度，保证父级尺寸变化后红线仍在正确比例上。重复调用幂等。
        /// </summary>
        public void SetCompactBottomLayout(bool compact)
        {
            ApplyThreshold();
        }

        private void OnEnable()
        {
            ApplyTeamColors();
            ApplyThreshold();
        }

        private void LateUpdate()
        {
            if (!autoBindLocalPlayerTeam) return;

            PlayerController player = PlayerController.LocalPlayer;
            if (player == null || player.Health == null) return;

            Team localTeam = player.Health.Team;
            if (localTeam == Team.Neutral || localTeam == _team) return;

            SetTeam(localTeam);
        }

        private void Update()
        {
            HudGaugeStyleSO resolved = HudGaugeStyleSO.Resolve(style);

            // 宿主画布不可见（首页 / 结算页：HudController 把 _hudCanvas.enabled=false）时直接跳过。
            // 历史 bug——只关画布不停 Update，_ink01 停在离场时的低墨值，结算/首页每 1.15s 继续蜂鸣。
            if (!IsHostCanvasVisible())
            {
                SuppressAlerts();
                return;
            }

            _hostSuppressed = false;

            bool lowNow = HudGaugeMath.IsLowInk(_ink01, resolved.lowInkThreshold01, _low, resolved.lowInkHysteresis);
            if (lowNow != _low)
            {
                _low = lowNow;
                if (_low)
                {
                    _blinkPhase = 0f;
                    _nextWarningTime = Time.unscaledTime + resolved.warningRepeatInterval;
                    // 离场残留的墨水值造成的「回来第一帧就响」不算告警：延迟一个间隔再提醒
                    if (_suppressEntryAlert) _suppressEntryAlert = false;
                    else TriggerWarning(resolved);
                }
            }

            if (_low && Time.unscaledTime >= _nextWarningTime)
            {
                _nextWarningTime = Time.unscaledTime + Mathf.Max(0.2f, resolved.warningRepeatInterval);
                TriggerWarning(resolved);
            }

            UpdateWarningVisuals(resolved);
        }

        /// <summary>
        /// 宿主 HUD 画布是否可见：本视图挂在 HUD canvas 子树里，
        /// HudController 隐藏 HUD 时只关画布（不停 GameObject），所以必须自查根画布开关。
        /// 注意 <see cref="EnsureSubCanvas"/> 会给本节点自己加一层子画布，
        /// 因此解析必须「沿父级链找上级画布」，不能只看自己的 rootCanvas——
        /// 否则本组件恰好挂在画布根节点上时会解析到自己、被误判为不可见。
        /// </summary>
        public bool IsHostCanvasVisible()
        {
            if (!isActiveAndEnabled) return false;

            // 尚未解析，或只解析到自己的子画布（运行时「先 AddComponent 后 SetParent」的中间态、
            // 或本节点本身就是画布根）时重新解析；拿到真正的上级画布后即缓存。
            if (_rootCanvas == null || _rootCanvas.transform == transform)
            {
                Canvas own = GetComponent<Canvas>();
                Canvas root = own != null ? own.rootCanvas : null;
                if (root == null || root.transform == transform)
                {
                    Canvas parentCanvas = transform.parent != null
                        ? transform.parent.GetComponentInParent<Canvas>()
                        : null;
                    if (parentCanvas != null) root = parentCanvas.rootCanvas;
                }

                if (root == null) return true; // 完全没有画布（纯逻辑/测试）时不做门禁，保持历史行为
                _rootCanvas = root;
            }

            return _rootCanvas.enabled && _rootCanvas.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// 进入不可见态：清掉告警状态与红闪视觉，并让「回到战斗后的首次进入告警」延后一个间隔。
        /// 公开入口供 HudController 在隐藏 HUD / 开新一局时主动调用（幂等）。
        /// </summary>
        public void ResetAlertState(bool suppressEntryAlert = true)
        {
            _low = false;
            _blinkPhase = 0f;
            _nextWarningTime = 0f;
            _suppressEntryAlert = suppressEntryAlert;
            UpdateWarningVisuals(HudGaugeStyleSO.Resolve(style));
        }

        private void SuppressAlerts()
        {
            if (_hostSuppressed) return;

            _hostSuppressed = true;
            ResetAlertState(true);
        }

        private void TriggerWarning(HudGaugeStyleSO resolved)
        {
            WarningPlayCount++;

            AudioClip clip = ResolveLowInkClip();
            if (clip == null) return;

            AudioManager manager = AudioManager.Instance;
            if (manager != null)
            {
                manager.PlayOne(clip, resolved.lowInkWarningVolume, 0.98f, 1.02f);
                return;
            }

            if (warningSource == null)
            {
                warningSource = gameObject.AddComponent<AudioSource>();
                warningSource.playOnAwake = false;
                warningSource.spatialBlend = 0f;
            }

            warningSource.PlayOneShot(clip, resolved.lowInkWarningVolume);
        }

        private AudioClip ResolveLowInkClip()
        {
            if (lowInkClip != null) return lowInkClip;

            AudioManager manager = AudioManager.Instance;
            if (manager != null && manager.lowInkWarningClip != null)
            {
                lowInkClip = manager.lowInkWarningClip;
                return lowInkClip;
            }

#if UNITY_EDITOR
            lowInkClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(EditorLowInkClipPath);
#endif
            return lowInkClip;
        }

        private void UpdateWarningVisuals(HudGaugeStyleSO resolved)
        {
            bool blinkOn = false;
            if (_low)
            {
                _blinkPhase += Time.unscaledDeltaTime * Mathf.Max(0.1f, resolved.warningBlinkHz);
                blinkOn = (Mathf.Sin(_blinkPhase * Mathf.PI * 2f) > 0f);
            }

            if (warningGlow != null)
            {
                // 纯色块：亮 = 实红，灭 = 全透明（硬切，不做淡入淡出）
                warningGlow.color = blinkOn
                    ? new Color(ComicStyle.Red.r, ComicStyle.Red.g, ComicStyle.Red.b, resolved.warningGlowAlpha)
                    : Color.clear;
            }

            if (frameShape != null)
            {
                // 纸白卡片上墨框才是常态：低墨时硬切翻红，不闪回不可见的白边
                frameShape.BorderColor = blinkOn ? ComicStyle.Red : ComicStyle.Ink;
                frameShape.BorderWidth = ComicStyle.BorderThin;
            }
        }

        private void ApplyTeamColors()
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);
            Color ink = InkTeamColorFilter.ResolveTeamColor(_team, theme);

            if (barFill != null)
            {
                barFill.color = new Color(ink.r, ink.g, ink.b, 1f);
            }

            // 卡片身份色带 / 墨底徽章边框 / 水滴图标：同一支队伍色，换队一起硬切
            if (cardRail != null) cardRail.color = new Color(ink.r, ink.g, ink.b, 1f);
            if (badgePlate != null) badgePlate.BorderColor = new Color(ink.r, ink.g, ink.b, 1f);
            if (badgeIcon != null) badgeIcon.color = new Color(ink.r, ink.g, ink.b, 1f);

            if (frameShape != null)
            {
                frameShape.Shape = UIInkShape.SharpRect;
                frameShape.CornerRadius = ComicStyle.CornerSharp;
                // 轨道边框：纸白卡片上墨框常态（低墨告警时由 UpdateWarningVisuals 硬切翻红）
                frameShape.BorderColor = ComicStyle.Ink;
                frameShape.BorderWidth = ComicStyle.BorderThin;
                // 描边层填充全透明：UIInkShapeGraphic 的实心阴影一开就会把整条盖成墨黑。
                frameShape.HardShadow = false;
                frameShape.GlossAlpha = 0f;
                frameShape.DrawGloss = false;
                frameShape.InnerRimAlpha = 0f;
            }

            if (percentLabel != null)
            {
                // 纸白卡片：大百分比用墨色（旧黑底信息条时代是纸白字）
                percentLabel.color = ComicStyle.Ink;
            }

            // 队伍色解析失败时退回主题默认墨色，避免白条看不见
            if (barFill != null && barFill.color.a < 0.01f)
            {
                barFill.color = themeResolved.teamAColor;
            }
        }

        private void ApplyThreshold()
        {
            if (thresholdMarker == null) return;

            // 手雷不耗墨（bombInkCost <= 0，如自定义配置）时刻度线无意义，直接隐藏，避免条首出现误导性红线
            bool visible = _threshold01 > 0.001f;
            if (thresholdMarker.gameObject.activeSelf != visible)
            {
                thresholdMarker.gameObject.SetActive(visible);
            }
            if (!visible) return;

            RectTransform bar = barFill != null ? barFill.rectTransform : (RectTransform)transform;
            float width = bar.rect.width;
            if (width <= 1f) width = 294f;

            float inset = Mathf.Max(0f, _thresholdInset);
            float usable = Mathf.Max(1f, width - inset * 2f);
            float x = inset + HudGaugeMath.ThresholdOffset(usable, _threshold01);
            thresholdMarker.anchoredPosition = new Vector2(x, 0f);
        }

        private void ApplyPercent(bool force = false)
        {
            if (percentLabel == null) return;

            int percent = Mathf.RoundToInt(_ink01 * 100f);
            if (!force && percent == _displayedPercent) return;

            _displayedPercent = percent;
            percentLabel.text = $"{percent}%";
        }
    }
}
