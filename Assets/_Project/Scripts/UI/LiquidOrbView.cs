using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 底部大招横条（2026-11 第四轮：放大 + 抗锯齿，满能改「徽章反色 + 外圈状态环」）。
    ///
    /// 结构：左端身份徽章（正圆 + 技能图标）+ 墨黑胶囊轨道 + 黄色胶囊填充，轨道拉满整行。
    /// 充能中 = 墨底徽章 + 黄图标 + 黄填充 + 外圈隐藏；
    /// 满能 = **填充铺满 + 徽章静默反色（黄底墨图标）+ 外圈 <see cref="ReadyRing"/> 亮黄环**，
    /// 不闪条身、不爆闪、不 Pop——满能只允许「徽章换状态」。
    ///
    /// 保留旧公开 API（SetCharge / IsReady / ReadyBurstCount / SpecialIconSprite / DisplayedSpecialKind /
    /// SetArcRing / SetBurst / Configure 旧签名），HudController 与测试无需改调用方式；
    /// 百分比读数与 Q 键帽均已下线（条长本身就是读数，满能靠徽章反色 + 状态环表达）。
    /// </summary>
    [AddComponentMenu("P1/UI/Liquid Orb View")]
    public class LiquidOrbView : MonoBehaviour
    {
        private const string EditorSlamIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_slam.png";
        private const string EditorStormIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_storm.png";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;

        [Header("横条填充（UIPillImage；旧环形弧字段保留兼容）")]
        [SerializeField] private Image barFill;
        [SerializeField] private TMP_Text percentLabel;
        [SerializeField] private UIArcRingGraphic arcRing;

        [Header("左端身份徽章 + 技能图标")]
        [SerializeField] private UIInkShapeGraphic iconPlate;
        [SerializeField] private RectTransform specialIconRoot;
        [SerializeField] private Image specialIcon;
        [SerializeField] private Image specialIconShadow;

        [Header("满能状态环（徽章外圈；仅满能显示）")]
        [SerializeField] private UIInkShapeGraphic readyRing;

        [Header("满能反馈（Q 键帽 + 一次性爆闪 + 整行 Punch）")]
        [SerializeField] private RectTransform readyHint;
        [SerializeField] private UIInkShapeGraphic readyHintPill;
        [SerializeField] private TMP_Text readyHintLabel;
        [SerializeField] private UISpringMotion readyHintMotion;
        [SerializeField] private UIBurstGraphic readyBurst;
        [SerializeField] private UISpringMotion rowMotion;

        [Header("旧卡片外壳（横条化后不再传入，仅兼容字段）")]
        [SerializeField] private UIInkShapeGraphic cardBackground;
        [SerializeField] private UIInkShapeGraphic cardRail;

        [SerializeField] private bool autoBindLocalPlayerTeam = true;

        private Team _team = Team.Neutral;
        private float _charge01;
        private int _displayedPercent = -1;
        private bool _ready;
        private bool _iconApplied;
        private bool _readyHintEnabled = true;
        private UIChunkyText _hintLabelChunky;
        private SpecialKind _specialKind = SpecialKind.Slam;

        /// <summary>大招是否处于满能待放状态。</summary>
        public bool IsReady => _ready;

        public float Charge01 => _charge01;

        /// <summary>横条填充（胶囊 Image；<see cref="SetCharge"/> 直接驱动 fillAmount）。</summary>
        public Image BarFill => barFill;

        /// <summary>遗留：环形充能弧（横条化后恒为 null，仅保留兼容字段）。</summary>
        public UIArcRingGraphic ArcRing => arcRing;

        /// <summary>左端身份徽章（满能时翻黄）。</summary>
        public UIInkShapeGraphic IconPlate => iconPlate;

        /// <summary>满能状态环（徽章外圈；充能时隐藏）。</summary>
        public UIInkShapeGraphic ReadyRing => readyRing;

        /// <summary>当前显示的技能图标（供测试/调试读取）。</summary>
        public Sprite SpecialIconSprite => specialIcon != null ? specialIcon.sprite : null;

        public SpecialKind DisplayedSpecialKind => _specialKind;

        /// <summary>进入满能的累计次数，供测试与调试读取。</summary>
        public int ReadyBurstCount { get; private set; }

        public void Configure(UIThemeSO themeAsset, HudGaugeStyleSO styleAsset,
            Image fill, TMP_Text percent, UIInkShapeGraphic plate,
            RectTransform hint, UIInkShapeGraphic hintPill, TMP_Text hintLabel,
            UISpringMotion hintMotion, string hintText)
        {
            theme = themeAsset;
            style = styleAsset;
            barFill = fill;
            percentLabel = percent;
            iconPlate = plate;
            readyHint = hint;
            readyHintPill = hintPill;
            readyHintLabel = hintLabel;
            readyHintMotion = hintMotion;

            if (readyHintLabel != null && !string.IsNullOrEmpty(hintText))
            {
                readyHintLabel.text = hintText;
            }

            ApplyTeamColors();

            // 兼容旧装配：旧场景若还传了「Q」键帽就按旧规则常驻（新装配一律传 null，条内不再有键帽）；
            // 移动端由 SetReadyHintEnabled(false) 隐藏。
            if (readyHint != null) readyHint.gameObject.SetActive(true);
            if (percentLabel != null) percentLabel.gameObject.SetActive(true);
        }

        /// <summary>装配徽章里的技能图标，由程序化工厂调用（阴影层可为 null）。</summary>
        public void ConfigureSpecialIcon(RectTransform iconRoot, Image icon, Image shadow)
        {
            specialIconRoot = iconRoot;
            specialIcon = icon;
            specialIconShadow = shadow;
            ApplySpecialIcon(true);
            ApplyTeamColors();
        }

        /// <summary>
        /// 装配满能状态环（徽章外圈的黄环 + 墨边，由工厂在装配时传入）。
        /// 只由满能状态驱动显隐；不做旋转 / 缩放 / 循环脉冲（几何风禁止装饰性动画）。
        /// </summary>
        public void ConfigureReadyRing(UIInkShapeGraphic ring)
        {
            readyRing = ring;
            if (readyRing != null)
            {
                readyRing.raycastTarget = false;
                readyRing.Shape = UIInkShape.Pill;
                readyRing.SpeechTail = false;
                readyRing.BorderWidth = HudSpecialGaugeFactory.ReadyRingBand;
                readyRing.BorderColor = ComicStyle.Yellow;
                readyRing.HardShadow = false;
                readyRing.ShadowOffset = Vector2.zero;
                readyRing.Halftone = false;
                readyRing.GlossAlpha = 0f;
                readyRing.DrawGloss = false;
                readyRing.InnerRimAlpha = 0f;
                readyRing.color = Color.clear; // 只画环带（填充透明），不遮挡徽章
            }

            RefreshReadyRing();
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            theme = themeAsset;
            ApplyTeamColors();
        }

        /// <summary>
        /// 旧卡片外壳绑定（t-1 底部三卡）：横条化后工厂不再传卡片，这里保留签名只为兼容，
        /// 传 null 时静默（满能的整卡翻色改由左端徽章承担）。
        /// </summary>
        public void SetCardChrome(UIInkShapeGraphic background, UIInkShapeGraphic rail)
        {
            cardBackground = background;
            cardRail = rail;
            ApplyTeamColors();
        }

        /// <summary>遗留：装配环形充能弧（横条化后工厂不再创建环弧；传入时仍会驱动 Fill01 以便旧场景可用）。</summary>
        public void SetArcRing(UIArcRingGraphic ring)
        {
            arcRing = ring;
            if (arcRing != null)
            {
                arcRing.raycastTarget = false;
                arcRing.Fill01 = _charge01;
            }
            ApplyTeamColors();
        }

        /// <summary>装配满能爆闪（一次性冲击波；进入满能瞬间 <see cref="UIBurstGraphic.Play"/>，播完自停）。</summary>
        public void SetBurst(UIBurstGraphic burst)
        {
            readyBurst = burst;
            if (readyBurst != null) readyBurst.raycastTarget = false;
        }

        /// <summary>装配整行动效（满能瞬间一记 Punch；横条化后由工厂传入）。</summary>
        public void SetRowMotion(UISpringMotion motion)
        {
            rowMotion = motion;
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
            _charge01 = clamped;

            if (barFill != null) barFill.fillAmount = clamped;
            if (arcRing != null) arcRing.Fill01 = clamped; // 旧环形布局兼容

            SetReady(isReady);
            ApplyPercent();
        }

        /// <summary>切换队伍（横条用行动黄表达充能，这里仅保留接口兼容）。</summary>
        public void SetTeam(Team team)
        {
            _team = team;
            ApplyTeamColors();
        }

        /// <summary>切换徽章图标（随主武器大招类型 Slam / Storm 变化）。</summary>
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

        /// <summary>
        /// 移动端紧凑布局：隐藏键盘「Q 释放」键帽（触控大招键本身有就绪反馈）。
        /// 重复调用幂等。
        /// </summary>
        public void SetCompactBottomLayout(bool compact)
        {
            SetReadyHintEnabled(!compact);
        }

        /// <summary>是否允许显示满能键盘提示（移动端由触控大招键承担提示，故关闭）。</summary>
        public void SetReadyHintEnabled(bool enabled)
        {
            _readyHintEnabled = enabled;
            RefreshReadyWidgets();
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

        private void SetReady(bool ready)
        {
            if (ready != _ready)
            {
                _ready = ready;

                if (ready)
                {
                    ReadyBurstCount++;
                    // 满能只做静默换状态：徽章反色 + 外圈状态环亮起（RefreshReadyRing）。
                    // 不做条身闪烁、不做爆闪、不做 Pop/Punch——用户明确要求「图标稍微变一下状态就行」。
                }

                ApplyTeamColors();
            }

            RefreshReadyWidgets();
            RefreshReadyRing();
        }

        /// <summary>满能状态环显隐：满能露出黄环 + 墨边，充能 / 释放后立即收起。</summary>
        private void RefreshReadyRing()
        {
            if (readyRing != null && readyRing.gameObject.activeSelf != _ready)
            {
                readyRing.gameObject.SetActive(_ready);
            }
        }

        /// <summary>
        /// 「Q」键帽常驻右槽：是否显示只由 <see cref="_readyHintEnabled"/>（移动端隐藏）决定，
        /// 充能/满能状态靠翻色表达（见 <see cref="ApplyTeamColors"/>）。
        /// </summary>
        private void RefreshReadyWidgets()
        {
            if (readyHint != null && readyHint.gameObject.activeSelf != _readyHintEnabled)
            {
                readyHint.gameObject.SetActive(_readyHintEnabled);
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
                // 避免徽章空白；正式构建以 HudGaugeStyleSO 的序列化引用为准。
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
                // 几何风禁用阴影：阴影层保留引用但永不绘制。
                specialIconShadow.sprite = sprite;
                specialIconShadow.enabled = false;
            }
        }

        private void ApplyTeamColors()
        {
            // 横条仪表（2026-11 四轮）：填充恒为行动黄；满能时左端徽章**整体反色**——
            // 充能 = 墨底黄图标，满能 = 黄底墨图标 + 墨边 + 外圈 ReadyRing（3px 墨边常驻，反色后依然读得出圆盘轮廓）。
            // 反色是硬切，不做旋转环 / 呼吸缩放。
            if (barFill != null) barFill.color = ComicStyle.Yellow;
            if (arcRing != null) arcRing.color = ComicStyle.Yellow;

            if (iconPlate != null)
            {
                iconPlate.color = _ready ? ComicStyle.Yellow : ComicStyle.Ink;
                iconPlate.BorderColor = ComicStyle.Ink;
            }

            if (readyRing != null)
            {
                // 满能环只画 4px 黄色环带（填充透明）：徽章外留 2px 缝，满能时一眼可见
                readyRing.color = Color.clear;
                readyRing.BorderColor = ComicStyle.Yellow;
            }

            // 旧卡片外壳（t-1）：横条化后工厂传 null，保留兼容分支
            if (cardBackground != null)
            {
                cardBackground.color = _ready ? ComicStyle.Yellow : ComicStyle.Paper;
                cardBackground.BorderColor = ComicStyle.Ink;
            }
            if (cardRail != null) cardRail.color = _ready ? ComicStyle.Ink : ComicStyle.Yellow;

            if (specialIcon != null)
            {
                // 图标与徽章底色始终互为反色：充能 = 墨底黄图标，满能 = 黄底墨图标
                specialIcon.color = _ready ? ComicStyle.Ink : ComicStyle.Yellow;
            }
            if (specialIconShadow != null) specialIconShadow.enabled = false;

            // 旧横条布局的百分读数（横条化后为 null）：纸白卡上必须是墨字
            if (percentLabel != null) percentLabel.color = ComicStyle.Ink;

            if (readyHintPill != null)
            {
                readyHintPill.Shape = UIInkShape.Pill;
                readyHintPill.SpeechTail = false;
                readyHintPill.BorderWidth = ComicStyle.BorderThin;
                // 「Q」键帽在横条右槽：充能 = 墨底纸白字（与轨道同色，读作条的端头）；
                // 满能 = 翻黄底墨字 + 硬影离地，与徽章同一身份色。
                readyHintPill.color = _ready ? ComicStyle.Yellow : ComicStyle.Ink;
                readyHintPill.BorderColor = _ready ? ComicStyle.Ink : ComicStyle.Paper;
                // 色签档硬影由工厂在装配时一次性写入（键帽已下线，不再有后续补影）
            }

            if (readyHintLabel != null)
            {
                // 走 UIChunkyText.SetFillColor：doubleLayer=false 时它会同步 source.color，
                // 直接写 TMP.color 会在下次重建时被 chunky 的 fillColor 冲掉
                if (_hintLabelChunky == null && readyHintLabel != null)
                {
                    _hintLabelChunky = readyHintLabel.GetComponent<UIChunkyText>();
                }

                Color labelColor = _ready ? ComicStyle.Ink : ComicStyle.Paper;
                if (_hintLabelChunky != null) _hintLabelChunky.SetFillColor(labelColor);
                else readyHintLabel.color = labelColor;
            }
        }

        private void ApplyPercent()
        {
            if (percentLabel == null) return;

            int percent = Mathf.RoundToInt(Mathf.Clamp01(_charge01) * 100f);
            if (percent == _displayedPercent) return;

            _displayedPercent = percent;
            percentLabel.text = percent >= 100 ? "100%" : $"{percent}%";
        }
    }
}
