using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 右上角大招液体能量球（P4-C）。
    ///
    /// 涂地涨能时液面在球内波纹上升；充满瞬间球外亮起金色旋转光环、整体脉冲震颤，
    /// 下方弹性弹出「按 Q 释放」按键提示，并炸开一圈冲击波 + 射线。
    /// 释放大招后充能清零，液面自然回落、光环熄灭。
    /// </summary>
    [AddComponentMenu("P1/UI/Liquid Orb View")]
    public class LiquidOrbView : MonoBehaviour
    {
        private const string EditorSlamIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_slam.png";
        private const string EditorStormIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_storm.png";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;

        [Header("球体")]
        [SerializeField] private UILiquidFillGraphic liquid;
        [SerializeField] private UIInkShapeGraphic bodyShape;
        [SerializeField] private UIInkShapeGraphic rimShape;
        [SerializeField] private RectTransform pulseRoot;
        [SerializeField] private TMP_Text percentLabel;

        [Header("球心大招技能图标")]
        [SerializeField] private RectTransform specialIconRoot;
        [SerializeField] private Image specialIcon;
        [SerializeField] private Image specialIconShadow;

        [Header("满能反馈")]
        [SerializeField] private UIRotatingRingGraphic halo;
        [SerializeField] private UIBurstGraphic burst;
        [SerializeField] private RectTransform readyHint;
        [SerializeField] private UIInkShapeGraphic readyHintPill;
        [SerializeField] private Text readyHintLabel;
        [SerializeField] private UISpringMotion readyHintMotion;
        [SerializeField] private ComicActionLinesGraphic actionLines;

        [SerializeField] private bool autoBindLocalPlayerTeam = true;

        private Team _team = Team.Neutral;
        private float _charge01;
        private float _hitImpulse;
        private float _pulseTime;
        private UISpringFloat _haloIntensity;
        private int _displayedPercent = -1;
        private bool _ready;
        private bool _wired;
        private bool _iconApplied;
        private SpecialKind _specialKind = SpecialKind.Slam;

        /// <summary>大招是否处于满能待放状态。</summary>
        public bool IsReady => _ready;

        public float Charge01 => _charge01;

        /// <summary>球心当前显示的技能图标（供测试/调试读取）。</summary>
        public Sprite SpecialIconSprite => specialIcon != null ? specialIcon.sprite : null;

        public SpecialKind DisplayedSpecialKind => _specialKind;

        /// <summary>满能爆发（冲击波 + 射线 + 提示弹入）被触发的累计次数，供测试与调试读取。</summary>
        public int ReadyBurstCount { get; private set; }

        public void Configure(UIThemeSO themeAsset, HudGaugeStyleSO styleAsset,
            UILiquidFillGraphic liquidGraphic, UIInkShapeGraphic body, UIInkShapeGraphic rim,
            RectTransform pulse, TMP_Text percent,
            UIRotatingRingGraphic haloGraphic, UIBurstGraphic burstGraphic,
            RectTransform hint, UIInkShapeGraphic hintPill, Text hintLabel, UISpringMotion hintMotion,
            string hintText, string hintKey)
        {
            theme = themeAsset;
            style = styleAsset;
            liquid = liquidGraphic;
            bodyShape = body;
            rimShape = rim;
            pulseRoot = pulse;
            percentLabel = percent;
            halo = haloGraphic;
            burst = burstGraphic;
            readyHint = hint;
            readyHintPill = hintPill;
            readyHintLabel = hintLabel;
            readyHintMotion = hintMotion;

            if (readyHintLabel != null)
            {
                string text = hintText ?? string.Empty;
                if (!string.IsNullOrEmpty(hintKey) && text.Length > 0 && !text.Contains(hintKey))
                {
                    text = $"[{hintKey}] {text}";
                }

                readyHintLabel.text = text;
            }

            EnsureWiring();
            ApplyTeamColors();

            if (halo != null) halo.Intensity = 0f;
            if (halo != null && halo.gameObject.activeSelf) halo.gameObject.SetActive(false);
            if (readyHint != null) readyHint.gameObject.SetActive(false);

            if (actionLines == null && pulseRoot != null)
            {
                actionLines = pulseRoot.GetComponentInChildren<ComicActionLinesGraphic>(true);
            }
            if (actionLines != null) actionLines.Alpha = 0f;
        }

        /// <summary>装配球心技能图标（阴影 + 白色主图标），由程序化工厂调用。</summary>
        public void ConfigureSpecialIcon(RectTransform iconRoot, Image icon, Image shadow)
        {
            specialIconRoot = iconRoot;
            specialIcon = icon;
            specialIconShadow = shadow;
            ApplySpecialIcon(true);
            ApplyTeamColors();
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            theme = themeAsset;
            ApplyTeamColors();
        }

        public void SetStyle(HudGaugeStyleSO styleAsset)
        {
            style = styleAsset;
            ApplySpecialIcon(true);
        }

        /// <summary>每帧由 HudController 喂入大招充能（0..1）与是否可释放。</summary>
        public void SetCharge(float charge01, bool isReady)
        {
            float clamped = Mathf.Clamp01(charge01);
            float delta = clamped - _charge01;
            _charge01 = clamped;

            if (liquid != null)
            {
                liquid.SetFill(clamped);
                if (delta > 0.004f)
                {
                    liquid.Splash(Mathf.Clamp(delta * 2.4f, 0.06f, 0.5f));
                    _hitImpulse = Mathf.Min(0.06f, _hitImpulse + delta * 0.12f);
                }
            }

            SetReady(isReady);
        }

        /// <summary>切换队伍墨色（球内液体与按键提示底色跟随队伍）。</summary>
        public void SetTeam(Team team)
        {
            _team = team;
            ApplyTeamColors();
        }

        /// <summary>切换球心技能图标（随主武器大招类型 Slam / Storm 变化）。</summary>
        public void SetSpecialKind(SpecialKind kind)
        {
            if (_iconApplied && _specialKind == kind) return;

            _specialKind = kind;
            ApplySpecialIcon(true);
        }

        public void SetAutoBindLocalPlayerTeam(bool enabled)
        {
            autoBindLocalPlayerTeam = enabled;
        }

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

        private void Start()
        {
            EnsureSubCanvas();
            EnsureWiring();
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
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            _pulseTime += dt;
            _hitImpulse = Mathf.MoveTowards(_hitImpulse, 0f, dt * 0.55f);

            // 满能脉冲震颤：整簇缩放做正弦呼吸；涨能/爆发时叠一记短促冲击
            if (pulseRoot != null)
            {
                float pulse = _ready
                    ? Mathf.Sin(_pulseTime * Mathf.PI * 2f * resolved.readyPulseHz) * resolved.readyPulseScale
                    : 0f;
                float scale = 1f + pulse + _hitImpulse;
                pulseRoot.localScale = new Vector3(scale, scale, 1f);
            }

            // 技能图标与球体脉冲错相呼吸，满能时球心像在「跳」
            if (specialIconRoot != null)
            {
                float iconPulse = _ready
                    ? Mathf.Sin(_pulseTime * Mathf.PI * 2f * resolved.readyPulseHz + Mathf.PI) * 0.06f
                    : 0f;
                float iconScale = 1f + iconPulse;
                specialIconRoot.localScale = new Vector3(iconScale, iconScale, 1f);
            }

            // 金色光环：强度走弹簧，避免满能瞬间生硬地闪出来
            float haloTarget = _ready ? resolved.haloIntensity : 0f;
            _haloIntensity.Step(haloTarget, UISpringSettings.Follow, dt, 0.002f, 0.02f);

            if (halo != null)
            {
                bool visible = _haloIntensity.Value > 0.004f;
                if (halo.gameObject.activeSelf != visible) halo.gameObject.SetActive(visible);
                if (visible) halo.Intensity = _haloIntensity.Value;
            }

            // 漫画放射动作线：满能时亮起并自转
            if (actionLines == null && pulseRoot != null)
            {
                actionLines = pulseRoot.GetComponentInChildren<ComicActionLinesGraphic>(true);
            }
            if (actionLines != null)
            {
                actionLines.Alpha = _ready ? 0.24f : 0f;
                if (_ready)
                {
                    actionLines.transform.localRotation = Quaternion.Euler(0f, 0f, _pulseTime * 40f);
                }
            }

            ApplyPercent();
        }

        private void EnsureWiring()
        {
            if (_wired) return;
            _wired = true;

            if (readyHintMotion != null)
            {
                readyHintMotion.OnPlayedOut += HandleHintPlayedOut;
            }
        }

        private void HandleHintPlayedOut()
        {
            if (readyHint != null && !_ready) readyHint.gameObject.SetActive(false);
        }

        private void SetReady(bool ready)
        {
            if (ready == _ready) return;
            _ready = ready;

            HudGaugeStyleSO resolved = HudGaugeStyleSO.Resolve(style);

            if (ready)
            {
                ReadyBurstCount++;
                _hitImpulse = Mathf.Max(_hitImpulse, 0.1f);
                _pulseTime = 0f;

                if (burst != null) burst.Play();

                if (readyHint != null)
                {
                    readyHint.gameObject.SetActive(true);
                    if (readyHintMotion != null) readyHintMotion.PlayIn(resolved.readyHintDelay);
                }
            }
            else if (readyHintMotion != null)
            {
                readyHintMotion.PlayOut();
            }
        }

        private void ApplySpecialIcon(bool force)
        {
            if (!force && _iconApplied) return;
            _iconApplied = true;

            string iconName = _specialKind == SpecialKind.Storm ? "special_storm" : "special_slam";
            Sprite sprite = UIIconLibrary.Get(iconName);
            if (sprite == null)
            {
                sprite = HudGaugeStyleSO.Resolve(style).GetSpecialIcon(_specialKind);
            }

#if UNITY_EDITOR
            if (sprite == null)
            {
                // 资产引用尚未同步时（例如首次进 Play 前的旧 .asset），按资源路径兜底，
                // 避免球心空白；正式构建以 HudGaugeStyleSO 的序列化引用为准。
                string path = _specialKind == SpecialKind.Storm ? EditorStormIconPath : EditorSlamIconPath;
                sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
#endif

            if (specialIcon != null)
            {
                specialIcon.sprite = sprite;
                specialIcon.enabled = sprite != null;
            }

            if (specialIconShadow != null)
            {
                specialIconShadow.sprite = sprite;
                specialIconShadow.enabled = sprite != null;
            }
        }

        private void ApplyTeamColors()
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);
            Color ink = InkTeamColorFilter.ResolveTeamColor(_team, theme);
            Color light = themeResolved.GetTeamLight(_team);

            if (liquid != null)
            {
                liquid.color = new Color(ink.r, ink.g, ink.b, 1f);
                liquid.SetCrestColor(new Color(light.r, light.g, light.b, 0.6f));
            }

            if (bodyShape != null)
            {
                Color panel = themeResolved.panelColor;
                bodyShape.color = new Color(panel.r, panel.g, panel.b, panel.a);
            }

            if (rimShape != null)
            {
                rimShape.BorderColor = ComicStyle.Ink;
            }

            if (halo != null) halo.color = themeResolved.readyGold;
            if (burst != null) burst.color = themeResolved.readyGold;
            if (actionLines != null) actionLines.color = ComicStyle.Yellow;

            if (readyHintPill != null)
            {
                readyHintPill.color = ComicStyle.Paper;
                readyHintPill.BorderColor = ComicStyle.Ink;
            }

            if (readyHintLabel != null) readyHintLabel.color = ComicStyle.Ink;
            if (percentLabel != null) percentLabel.color = ComicStyle.Ink;

            // 白色矢量图标 + 深墨硬阴影，压在队伍色液面上依然清晰
            if (specialIcon != null) specialIcon.color = Color.white;

            if (specialIconShadow != null)
            {
                specialIconShadow.color = ComicStyle.Ink;
            }
        }

        private void ApplyPercent()
        {
            if (percentLabel == null) return;

            float shown = liquid != null ? liquid.DisplayFill : _charge01;
            int percent = Mathf.RoundToInt(Mathf.Clamp01(shown) * 100f);
            if (percent == _displayedPercent) return;

            _displayedPercent = percent;
            percentLabel.text = percent >= 100 ? "100%" : $"{percent}%";
        }

        private void OnDestroy()
        {
            if (readyHintMotion != null) readyHintMotion.OnPlayedOut -= HandleHintPlayedOut;
        }
    }
}
