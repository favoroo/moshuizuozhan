using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 右上角竖向墨水计量管（P4-C）。
    ///
    /// 职责：
    /// 1. 用 <see cref="UILiquidFillGraphic"/> 直观显示剩余墨水（液面带弹簧惯性与波浪）；
    /// 2. 标注副武器（手雷）耗墨刻度红线，位置由 <see cref="SetThreshold01"/> 实时给入；
    /// 3. 墨水低于阈值时罐身闪红并在 <see cref="Update"/> 里按间隔播放 low_ink 警报音。
    ///
    /// 所有数值来自 <see cref="HudGaugeStyleSO"/>，配色来自 <see cref="UIThemeSO"/> 与队伍墨色。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Tank View")]
    public class InkTankView : MonoBehaviour
    {
        private const string EditorLowInkClipPath = "Assets/_Project/Audio/SFX/low_ink_warning_1.wav";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;
        [SerializeField] private UILiquidFillGraphic liquid;
        [SerializeField] private RectTransform thresholdMarker;
        [SerializeField] private UIInkShapeGraphic frameShape;
        [SerializeField] private UIInkShapeGraphic bodyShape;
        [SerializeField] private UIInkShapeGraphic warningGlow;
        [SerializeField] private Text percentLabel;
        [SerializeField] private AudioSource warningSource;

        [Tooltip("留空则依次尝试 AudioManager.lowInkWarningClip 与编辑器内置路径")]
        [SerializeField] private AudioClip lowInkClip;

        [SerializeField] private bool autoBindLocalPlayerTeam = true;

        private float _thresholdInset = 4f;

        private Team _team = Team.Neutral;
        private float _ink01 = 1f;
        private float _threshold01 = 0.55f;
        private bool _low;
        private float _blinkPhase;
        private float _nextWarningTime;
        private int _displayedPercent = -1;

        /// <summary>当前是否处于低墨告警（带迟滞）。</summary>
        public bool IsLowInk => _low;

        /// <summary>低墨警报被触发的累计次数（含首次进入与重复提醒），供测试与调试读取。</summary>
        public int WarningPlayCount { get; private set; }

        public float Ink01 => _ink01;

        /// <summary>手雷红线位置 0..1。</summary>
        public float Threshold01 => _threshold01;

        public void Configure(UIThemeSO themeAsset, HudGaugeStyleSO styleAsset,
            UILiquidFillGraphic liquidGraphic, RectTransform marker, UIInkShapeGraphic frame,
            UIInkShapeGraphic body, UIInkShapeGraphic warning, Text percent, float thresholdInset)
        {
            theme = themeAsset;
            style = styleAsset;
            liquid = liquidGraphic;
            thresholdMarker = marker;
            frameShape = frame;
            bodyShape = body;
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

        public void SetStyle(HudGaugeStyleSO styleAsset)
        {
            style = styleAsset;
            ApplyThreshold();
        }

        /// <summary>每帧由 HudController 喂入当前墨水（绝对值 + 上限）。</summary>
        public void SetInk(float currentInk, float maxInk)
        {
            _ink01 = maxInk > 0.01f ? Mathf.Clamp01(currentInk / maxInk) : 0f;

            if (liquid != null) liquid.SetFill(_ink01);
            ApplyPercent();
        }

        /// <summary>手雷刻度红线：传入 0..1（一般 = bombInkCost / maxInk）。</summary>
        public void SetThreshold01(float threshold01)
        {
            float clamped = Mathf.Clamp01(threshold01);
            if (Mathf.Abs(clamped - _threshold01) < 0.0005f) return;

            _threshold01 = clamped;
            ApplyThreshold();
        }

        /// <summary>切换队伍墨色（液面用队伍色、浪冠用队伍亮色）。</summary>
        public void SetTeam(Team team)
        {
            _team = team;
            ApplyTeamColors();
        }

        public void SetAutoBindLocalPlayerTeam(bool enabled)
        {
            autoBindLocalPlayerTeam = enabled;
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

            bool lowNow = HudGaugeMath.IsLowInk(_ink01, resolved.lowInkThreshold01, _low, resolved.lowInkHysteresis);
            if (lowNow != _low)
            {
                _low = lowNow;
                if (_low)
                {
                    _blinkPhase = 0f;
                    _nextWarningTime = Time.unscaledTime + resolved.warningRepeatInterval;
                    TriggerWarning(resolved);
                }
            }

            if (_low && Time.unscaledTime >= _nextWarningTime)
            {
                _nextWarningTime = Time.unscaledTime + Mathf.Max(0.2f, resolved.warningRepeatInterval);
                TriggerWarning(resolved);
            }

            UpdateWarningVisuals(resolved);
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
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            float blink = 0f;
            if (_low)
            {
                _blinkPhase += Time.unscaledDeltaTime * Mathf.Max(0.1f, resolved.warningBlinkHz);
                blink = 0.5f + 0.5f * Mathf.Sin(_blinkPhase * Mathf.PI * 2f);
            }

            if (warningGlow != null)
            {
                Color red = themeResolved.dangerRed;
                warningGlow.color = new Color(red.r, red.g, red.b, blink * resolved.warningGlowAlpha);
            }

            if (frameShape != null)
            {
                Color normal = themeResolved.outlineColor;
                Color alarm = themeResolved.dangerRed;
                frameShape.BorderColor = _low
                    ? Color.Lerp(normal, alarm, 0.25f + blink * 0.75f)
                    : normal;
            }
        }

        private void ApplyTeamColors()
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            if (liquid != null)
            {
                Color ink = InkTeamColorFilter.ResolveTeamColor(_team, theme);
                Color light = themeResolved.GetTeamLight(_team);
                liquid.color = new Color(ink.r, ink.g, ink.b, 1f);

                // 浪冠是独立颜色字段，无法被 Graphic.color 自动染色，这里同步一次
                liquid.SetCrestColor(new Color(light.r, light.g, light.b, 0.55f));
            }

            if (bodyShape != null)
            {
                Color panel = themeResolved.panelColor;
                bodyShape.color = new Color(panel.r, panel.g, panel.b, panel.a);
            }
        }

        private void ApplyThreshold()
        {
            if (thresholdMarker == null) return;

            RectTransform root = (RectTransform)transform;
            float height = root.rect.height;
            if (height <= 1f) height = 180f;

            float usable = Mathf.Max(1f, height - _thresholdInset * 2f);
            float y = _thresholdInset + HudGaugeMath.ThresholdOffset(usable, _threshold01);
            thresholdMarker.anchoredPosition = new Vector2(0f, y);
        }

        private void ApplyPercent(bool force = false)
        {
            if (percentLabel == null) return;

            int percent = Mathf.RoundToInt(_ink01 * 100f);
            if (!force && percent == _displayedPercent) return;

            _displayedPercent = percent;
            percentLabel.text = percent.ToString();
        }
    }
}
