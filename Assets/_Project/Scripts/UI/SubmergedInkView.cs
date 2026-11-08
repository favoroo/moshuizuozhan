using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 潜入墨水屏幕浸染视图。
    ///
    /// 参考 INKWAVE <c>src/fx/screenfx.js</c> 的 swim 段：潜入己方墨水时屏幕边缘向
    /// 己方墨色轻微"沉下去"（乘法浸染）+ 缓慢流动的焦散光斑（加法），
    /// 是"你在墨水里"的环境提示而非告警——与 <see cref="InkEdgeWarningView"/>（踩敌墨，快离开）、
    /// <see cref="LowHealthVignetteView"/>（血量告急，径向压暗）分工互斥。
    ///
    /// 强度按 <see cref="SubmergedInkMath.FadeRate"/> 指数平滑（INKWAVE damp λ=7），
    /// 潜入/浮出都快速无跳变；无障碍受「屏幕震动」滑杆门控（与低血暗角同口径，
    /// 焦散流动属于持续屏幕动效，关震屏的用户同样不想看到）。
    ///
    /// 层级：由 <see cref="HudController"/> 自动装配到画布根（不进 SafeArea），
    /// 在低血暗角之后插入（最终位于低血暗角之上、SafeArea 的 HUD 控件之下）；
    /// 空闲时关闭 Graphic，零开销（双 Pass 全屏着色只在潜入期间运行）。
    /// </summary>
    [AddComponentMenu("P1/UI/Submerged Ink View")]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class SubmergedInkView : MonoBehaviour
    {
        private const string ShaderName = "P1/UI/SubmergedInk";

        private static readonly int InkColorProp = Shader.PropertyToID("_InkColor");
        private static readonly int IntensityProp = Shader.PropertyToID("_Intensity");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");

        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        private Material _material;
        private bool _built;
        private bool _shaderMissingLogged;
        private float _intensity;
        private float _lastAspect = -1f;
        private Color _lastColor;
        private bool _hasColor;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 仅用于截图/调试验收：临时锁显潜入状态
        private float _debugPreviewUntil;
#endif

        /// <summary>当前浸染强度 0~1（供测试 / 调试面板读取）。</summary>
        public float CurrentIntensity => _intensity;

        /// <summary>当前是否真的在绘制（Image 启用）。</summary>
        public bool IsShowing => overlayImage != null && overlayImage.enabled;

        private void Awake()
        {
            parentCanvas = GetComponentInParent<Canvas>();
            EnsureBuilt();
        }

        private void OnDisable()
        {
            _intensity = 0f;
            if (overlayImage != null) overlayImage.enabled = false;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        /// <summary>
        /// 构建全屏 Image 与运行时材质。可重复调用（幂等）；
        /// Shader 缺失时保持未构建状态并只记一次警告，不抛异常（纯装饰性特效不挡游戏）。
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
                rt.pivot = new Vector2(0.5f, 0.5f);

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
                        Debug.LogWarning($"[SubmergedInk] 找不到 Shader {ShaderName}，潜入浸染已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "SubmergedInk (Runtime)",
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
                Debug.LogWarning("[SubmergedInk] 构建潜入浸染视图失败：" + e.Message);
                _built = false;
            }
        }

        private void Update()
        {
            PlayerController player = PlayerController.LocalPlayer;
            InkCharacterMotor motor = player != null ? player.Motor : null;
            CombatantHealth health = player != null ? player.Health : null;

            // 阵亡瞬间必须立刻开始退场（IsSwimming 在死亡帧可能仍为 true）
            bool swimming = motor != null && motor.IsSwimming
                            && health != null && health.IsAlive;

            Tick(Time.deltaTime, swimming);
        }

        /// <summary>
        /// 推进一帧强度并刷新材质。Update 内部调用；测试 / 外部驱动可直接调用。
        /// </summary>
        public void Tick(float dt, bool swimming)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled)
            {
                // HUD 画布在 Ready/Result 阶段整体关闭时，浸染必须立即退场
                swimming = false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugPreviewUntil > Time.unscaledTime)
            {
                swimming = true;
            }
#endif

            float target = swimming ? 1f : 0f;

            // 无障碍：震屏强度同样压低这块持续流动的屏幕动效
            float shakeScale = Mathf.Clamp01(GameSettingsService.ShakeScale);
            target *= Mathf.Lerp(0.35f, 1f, shakeScale);

            _intensity = SubmergedInkMath.StepIntensity(_intensity, target, dt);

            if (!SubmergedInkMath.IsVisible(_intensity))
            {
                if (overlayImage.enabled) overlayImage.enabled = false;
                return;
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                _lastAspect = aspect;
                _material.SetFloat(AspectProp, aspect);
            }

            UpdateInkColor();

            _material.SetFloat(IntensityProp, _intensity);
            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

        /// <summary>浸染色 = 本地玩家所在队的墨水色（拿不到时回落队 A 橙，与 LensInkView 同默认）。</summary>
        private void UpdateInkColor()
        {
            Color col = new Color(1.00f, 0.44f, 0.06f, 1f);

            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                Team localTeam = Team.TeamA;
                PlayerController player = PlayerController.LocalPlayer;
                if (player != null && player.Health != null)
                {
                    localTeam = player.Health.Team;
                }

                col = surface.GetTeamColor(localTeam);
            }

            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                col = col.linear;
            }

            if (!_hasColor || col != _lastColor)
            {
                _lastColor = col;
                _hasColor = true;
                _material.SetColor(InkColorProp, col);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>供编辑器 / 开发包截图验收：临时锁显潜入浸染（发布包里是空操作）。</summary>
        public void DebugPreview(float seconds = 5f)
        {
            _debugPreviewUntil = Mathf.Max(_debugPreviewUntil,
                Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }
#endif
    }
}
