using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 底部横向大招条（P14-H：由旧的右上角大招球改成血条上方的横向纯色条）。
    ///
    /// 结构：左侧「技能图标徽章」+ 中间横向充能条 + 右侧百分比。
    /// 充能中条与徽章都是纸白；充满瞬间三者一起硬切成黄色（徽章 / 满条 / 「Q 释放」提示），
    /// 没有任何旋转环、爆闪、液面晃动或脉冲震颤——几何大胆风只做 ≤100ms 的状态切换。
    ///
    /// 保留旧公开 API（SetCharge / IsReady / ReadyBurstCount / SpecialIconSprite / DisplayedSpecialKind），
    /// HudController 与测试无需改调用方式。
    /// </summary>
    [AddComponentMenu("P1/UI/Liquid Orb View")]
    public class LiquidOrbView : MonoBehaviour
    {
        private const string EditorSlamIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_slam.png";
        private const string EditorStormIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_storm.png";

        [SerializeField] private UIThemeSO theme;
        [SerializeField] private HudGaugeStyleSO style;

        [Header("横向充能条")]
        [SerializeField] private Image barFill;
        [SerializeField] private TMP_Text percentLabel;

        [Header("技能图标徽章")]
        [SerializeField] private UIInkShapeGraphic iconPlate;
        [SerializeField] private RectTransform specialIconRoot;
        [SerializeField] private Image specialIcon;
        [SerializeField] private Image specialIconShadow;

        [Header("满能提示")]
        [SerializeField] private RectTransform readyHint;
        [SerializeField] private UIInkShapeGraphic readyHintPill;
        [SerializeField] private TMP_Text readyHintLabel;
        [SerializeField] private UISpringMotion readyHintMotion;

        [SerializeField] private bool autoBindLocalPlayerTeam = true;

        private Team _team = Team.Neutral;
        private float _charge01;
        private int _displayedPercent = -1;
        private bool _ready;
        private bool _wired;
        private bool _iconApplied;
        private bool _readyHintEnabled = true;
        private SpecialKind _specialKind = SpecialKind.Slam;

        /// <summary>大招是否处于满能待放状态。</summary>
        public bool IsReady => _ready;

        public float Charge01 => _charge01;

        /// <summary>横向充能条填充图（0..1 由 fillAmount 驱动）。</summary>
        public Image BarFill => barFill;

        /// <summary>技能图标徽章（满能时翻黄）。</summary>
        public UIInkShapeGraphic IconPlate => iconPlate;

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

            EnsureWiring();
            ApplyTeamColors();

            if (readyHint != null) readyHint.gameObject.SetActive(false);
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
            _charge01 = clamped;

            if (barFill != null) barFill.fillAmount = clamped;

            SetReady(isReady);
            ApplyPercent();
        }

        /// <summary>切换队伍（横向条用纸白→黄色表达充能，这里仅保留接口兼容）。</summary>
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
        /// 移动端紧凑布局：隐藏键盘「Q 释放」提示（触控大招键本身有就绪反馈），
        /// 读数回到百分比。重复调用幂等。
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

        private void SetReady(bool ready)
        {
            if (ready == _ready)
            {
                RefreshReadyWidgets();
                return;
            }

            _ready = ready;
            HudGaugeStyleSO resolved = HudGaugeStyleSO.Resolve(style);

            if (ready)
            {
                ReadyBurstCount++;

                if (readyHint != null && _readyHintEnabled && readyHintMotion != null)
                {
                    readyHint.gameObject.SetActive(true);
                    readyHintMotion.PlayIn(resolved.readyHintDelay);
                }
            }

            ApplyTeamColors();
            RefreshReadyWidgets();
        }

        /// <summary>右侧读数列：未满能显示百分比；桌面端满能时硬切成黄底黑字「Q 释放」。</summary>
        private void RefreshReadyWidgets()
        {
            bool showHint = _ready && _readyHintEnabled;

            if (readyHint != null && readyHint.gameObject.activeSelf != showHint)
            {
                readyHint.gameObject.SetActive(showHint);
            }

            if (percentLabel != null && percentLabel.gameObject.activeSelf == showHint)
            {
                percentLabel.gameObject.SetActive(!showHint);
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
            // 充能中 = 纸白；满能 = 黄。条、徽章、提示同色，形成一眼可读的「硬切就绪」。
            Color accent = _ready ? ComicStyle.Yellow : ComicStyle.Paper;
            if (barFill != null) barFill.color = accent;
            if (iconPlate != null) iconPlate.color = accent;

            if (specialIcon != null) specialIcon.color = ComicStyle.Ink;
            if (specialIconShadow != null) specialIconShadow.enabled = false;

            if (percentLabel != null) percentLabel.color = ComicStyle.Paper;

            if (readyHintPill != null)
            {
                readyHintPill.Shape = UIInkShape.SharpRect;
                readyHintPill.CornerRadius = ComicStyle.CornerSharp;
                readyHintPill.SpeechTail = false;
                readyHintPill.HardShadow = false;
                readyHintPill.color = ComicStyle.Yellow;
                readyHintPill.BorderColor = ComicStyle.Ink;
            }

            if (readyHintLabel != null) readyHintLabel.color = ComicStyle.Ink;
        }

        private void ApplyPercent()
        {
            if (percentLabel == null) return;

            int percent = Mathf.RoundToInt(Mathf.Clamp01(_charge01) * 100f);
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