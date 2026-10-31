using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 击杀确认屏幕边缘闪光（P6-B）。
    ///
    /// 参考 INKWAVE <c>src/fx/screenfx.js</c> 的 kill 通道（uKill）：本地玩家击杀敌人的瞬间，
    /// 屏幕四周按径向 smoothstep 染上己方队色，硬切入场、线性淡出；
    /// 连杀（<see cref="KillFeedbackState"/> 窗口内）逐层提高强度。
    ///
    /// 无障碍：受「屏幕震动」滑杆（<see cref="GameSettingsService.ShakeScale"/>）门控——
    /// 对齐 INKWAVE 把 flash 归入「随 cameraShake 设置减弱的强反馈」的做法。
    ///
    /// 层级：由 <see cref="HudController"/> 自动装配到 HUD 画布根节点并置于最前（sibling 0），
    /// 盖住 3D 场景，但在所有 HUD 文字 / 准星 / 触控按钮之下。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class KillFlashView : MonoBehaviour
    {
        private const string ShaderName = "P1/UI/KillFlash";

        private static readonly int KillColorProp = Shader.PropertyToID("_KillColor");
        private static readonly int IntensityProp = Shader.PropertyToID("_Intensity");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");

        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        [Header("强度节奏（连杀加码）")]
        [Tooltip("首杀的基础强度 0~1")]
        [SerializeField] private float baseStrength = 0.70f;
        [Tooltip("连杀每层额外强度")]
        [SerializeField] private float streakStrengthStep = 0.15f;
        [Tooltip("连杀加码的最高层数（超出不再增强）")]
        [SerializeField] private int maxStreakBonus = 3;

        private Material _material;
        private bool _built;
        private bool _shaderMissingLogged;
        private float _strength;
        private float _duration = 0.42f;
        private float _lastAspect = -1f;
        private MatchManager _phaseMatch;
        private bool _phaseSubscribed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 仅用于截图/调试验收：临时把强度抬到可见
        private float _debugPreviewUntil;
#endif

        /// <summary>当前边缘闪光强度 0~1（供测试 / 调试面板读取）。</summary>
        public float CurrentStrength => _strength;

        /// <summary>当前是否真的在绘制（Image 启用）。</summary>
        public bool IsShowing => overlayImage != null && overlayImage.enabled;

        private void Awake()
        {
            parentCanvas = GetComponentInParent<Canvas>();
            EnsureBuilt();
        }

        private void OnEnable()
        {
            KillFeedEventHub.OnKillReported += HandleKillReported;
            SubscribePhaseChanged();
        }

        private void OnDisable()
        {
            KillFeedEventHub.OnKillReported -= HandleKillReported;
            UnsubscribePhaseChanged();
            _strength = 0f;
            if (overlayImage != null) overlayImage.enabled = false;
        }

        private void OnDestroy()
        {
            UnsubscribePhaseChanged();

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        /// <summary>阶段事件用于跨局清零连杀（对齐 HudController 的实例订阅模式）。</summary>
        private void SubscribePhaseChanged()
        {
            if (_phaseSubscribed) return;

            _phaseMatch = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (_phaseMatch != null)
            {
                _phaseMatch.PhaseChanged += HandlePhaseChanged;
                _phaseSubscribed = true;
            }
        }

        private void UnsubscribePhaseChanged()
        {
            if (_phaseSubscribed && _phaseMatch != null)
            {
                _phaseMatch.PhaseChanged -= HandlePhaseChanged;
            }

            _phaseSubscribed = false;
            _phaseMatch = null;
        }

        /// <summary>
        /// 构建全屏 Image 与运行时材质。可重复调用（幂等）；
        /// Shader 尚未导入或取不到时保持未构建状态，Update 会重试，不抛异常。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;

            try
            {
                var rt = transform as RectTransform;
                if (rt == null) rt = gameObject.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                if (overlayImage == null) overlayImage = GetComponent<Image>();
                if (overlayImage == null) overlayImage = gameObject.AddComponent<Image>();

                overlayImage.sprite = UIFlatSprite.White;
                overlayImage.type = Image.Type.Simple;
                overlayImage.preserveAspect = false;
                overlayImage.color = Color.white;
                overlayImage.raycastTarget = false;
                overlayImage.maskable = false;

                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    if (!_shaderMissingLogged)
                    {
                        _shaderMissingLogged = true;
                        Debug.LogWarning($"[KillFlash] 找不到 Shader {ShaderName}，击杀边缘闪光已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "KillFlash (Runtime)",
                    hideFlags = HideFlags.DontSave
                };

                overlayImage.material = _material;
                overlayImage.enabled = false;

                if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
                _built = true;
            }
            catch (System.Exception e)
            {
                // 纯装饰性屏幕反馈失败不应让整局游戏起不来
                Debug.LogWarning("[KillFlash] 构建击杀边缘闪光失败：" + e.Message);
                _built = false;
            }
        }

        /// <summary>
        /// 击杀事件 → 边缘闪光：只回应本地玩家的击杀；
        /// 连杀数来自 <see cref="KillFeedbackState"/>（CombatantHealth.Die 在事件分发前已登记）。
        /// </summary>
        private void HandleKillReported(KillFeedEvent ev)
        {
            if (!ev.IsLocalPlayerKiller)
                return;

            // HUD 画布整体关闭（Ready/Result）时不闪
            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled)
                return;

            Trigger(ResolveTeamColor(ev.KillerTeam), KillFeedbackState.CurrentStreak, ResolveDuration());
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            // 新对局开始时清空连杀（窗口跨局不作数）
            if (phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Finished)
                KillFeedbackState.Reset();
        }

        /// <summary>
        /// 触发一次击杀边缘闪光（硬切入场、线性淡出）。
        /// <paramref name="streak"/> = 1 表示首杀，连杀逐层加码。
        /// </summary>
        public void Trigger(Color teamColor, int streak, float durationSeconds)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null)
                return;

            int bonus = Mathf.Clamp(Mathf.Max(0, streak) - 1, 0, Mathf.Max(0, maxStreakBonus));
            _strength = Mathf.Clamp01(baseStrength + streakStrengthStep * bonus);
            _duration = Mathf.Max(0.12f, durationSeconds);

            _material.SetColor(KillColorProp, ToLinear(teamColor));
            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

        private void Update()
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugPreviewUntil > Time.unscaledTime && _strength <= 0.001f)
            {
                _strength = 0.85f;
                _duration = 0.5f;
                _material.SetColor(KillColorProp, ToLinear(ResolveTeamColor(LocalTeam())));
                if (!overlayImage.enabled) overlayImage.enabled = true;
            }
#endif

            if (_strength <= 0.0005f)
            {
                if (overlayImage.enabled) overlayImage.enabled = false;
                return;
            }

            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();

            // HUD 画布在 Ready/Result 阶段整体关闭时，闪光也必须立即退场
            if (parentCanvas != null && !parentCanvas.enabled)
            {
                _strength = 0f;
                overlayImage.enabled = false;
                return;
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                _lastAspect = aspect;
                _material.SetFloat(AspectProp, aspect);
            }

            // 无障碍门控：对齐 INKWAVE——flash 属于强反馈，随「屏幕震动」滑杆一起减弱/关闭
            float gated = _strength * GameSettingsService.ShakeScale;
            _material.SetFloat(IntensityProp, Mathf.Clamp01(gated));

            _strength = Mathf.Max(0f, _strength - Time.unscaledDeltaTime / Mathf.Max(0.01f, _duration));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>供编辑器/开发包截图验收：临时强制显示若干秒。</summary>
        public void DebugPreview(float seconds = 3f)
        {
            _debugPreviewUntil = Mathf.Max(_debugPreviewUntil,
                Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }
#endif

        private float ResolveDuration()
        {
            PlayerController player = PlayerController.LocalPlayer;
            PlayerConfigSO config = player != null && player.Health != null ? player.Health.Config : null;
            return config != null && config.killFlashSeconds > 0.05f ? config.killFlashSeconds : 0.42f;
        }

        private static Team LocalTeam()
        {
            PlayerController player = PlayerController.LocalPlayer;
            return player != null && player.Health != null ? player.Health.Team : Team.TeamA;
        }

        private static Color ResolveTeamColor(Team team)
        {
            InkSurfaceController surface = InkSurfaceController.Instance;
            Color color = surface != null
                ? surface.GetTeamColor(team)
                : (team == Team.TeamA
                    ? new Color(1.00f, 0.44f, 0.06f, 1f)
                    : new Color(0.15f, 0.42f, 1.00f, 1f));

            color.a = 1f;
            return color;
        }

        /// <summary>Linear 工程里材质 Color 属性不会自动做 sRGB→Linear 转换（对齐 InkEdgeWarningView 的处理）。</summary>
        private static Color ToLinear(Color color)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            return color;
        }
    }
}
