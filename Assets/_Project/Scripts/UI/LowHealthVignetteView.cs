using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 低血屏幕边缘暗角（P42-B）。
    ///
    /// 参考 INKWAVE <c>src/ui/hud.js</c> 的 _updHp 危急暗角：血量跌破 35% 后屏幕四周压暗、
    /// 越接近 0 越强；跌破 20% 时做 <see cref="LowHealthVignetteMath.CriticalFlashCount"/> 次
    /// 硬切告警闪烁后停在稳定暗角（不循环，符合 Docs/UI-GeometricBold.md 的告警例外条款）。
    ///
    /// 与既有屏幕反馈的分工：
    /// - <see cref="InkEdgeWarningView"/>：踩敌墨，有机墨水形状 + 队色，语义是"快离开"；
    /// - <see cref="KillFlashView"/>：击杀确认，加法叠亮，语义是"打得好"；
    /// - 本视图：血量告急，径向压暗，语义是"你快死了"。
    ///
    /// 无障碍：受「屏幕震动」滑杆门控（与 KillFlashView 同一口径），震屏关掉时同步降低强度。
    ///
    /// 层级：由 <see cref="HudController"/> 自动装配到画布根并置于最前（sibling 0 之外），
    /// 盖住 3D 场景，但在所有 HUD 文字 / 准星 / 触控按钮之下；空闲时关闭 Graphic，零开销。
    /// </summary>
    [AddComponentMenu("P1/UI/Low Health Vignette View")]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class LowHealthVignetteView : MonoBehaviour
    {
        private const string ShaderName = "P1/UI/LowHealthVignette";

        private static readonly int VignetteColorProp = Shader.PropertyToID("_VignetteColor");
        private static readonly int IntensityProp = Shader.PropertyToID("_Intensity");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");
        private static readonly int MaxAlphaProp = Shader.PropertyToID("_MaxAlpha");

        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        private Material _material;
        private bool _built;
        private bool _shaderMissingLogged;
        private float _intensity;
        private float _lastAspect = -1f;
        private bool _isCritical;
        private float _criticalElapsed;
        private Color _lastColor;
        private bool _hasColor;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 仅用于截图/调试验收：临时把强度抬到可见
        private float _debugPreviewUntil;
#endif

        /// <summary>当前暗角强度 0~1（供测试 / 调试面板读取）。</summary>
        public float CurrentIntensity => _intensity;

        /// <summary>当前是否处于危急闪烁态。</summary>
        public bool IsCritical => _isCritical;

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
            _isCritical = false;
            _criticalElapsed = 0f;
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
        /// Shader 缺失时保持未构建状态并只记一次警告，不抛异常。
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
                        Debug.LogWarning($"[LowHealthVignette] 找不到 Shader {ShaderName}，低血暗角已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "LowHealthVignette (Runtime)",
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
                Debug.LogWarning("[LowHealthVignette] 构建低血暗角失败：" + e.Message);
                _built = false;
            }
        }

        private void Update()
        {
            PlayerController player = PlayerController.LocalPlayer;
            CombatantHealth health = player != null ? player.Health : null;

            float health01 = 1f;
            bool alive = false;
            if (health != null && health.IsAlive)
            {
                health01 = health.Health01;
                alive = true;
            }

            Tick(Time.deltaTime, health01, alive);
        }

        /// <summary>
        /// 推进一帧强度并刷新材质。Update 内部调用；测试 / 外部驱动可直接调用。
        /// </summary>
        public void Tick(float dt, float health01, bool alive)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled)
            {
                // HUD 画布在 Ready/Result 阶段整体关闭时，暗角必须立即退场
                alive = false;
            }

            if (!alive) health01 = 1f;

            float hp = Mathf.Clamp01(health01);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugPreviewUntil > Time.unscaledTime)
            {
                alive = true;
                hp = 0.02f;
            }
#endif

            bool wasCritical = _isCritical;
            _isCritical = LowHealthVignetteMath.UpdateCritical(_isCritical, hp);
            if (_isCritical && !wasCritical) _criticalElapsed = 0f;
            if (!_isCritical) _criticalElapsed = 0f;
            if (_isCritical) _criticalElapsed += Mathf.Max(0f, dt);

            float target = LowHealthVignetteMath.TargetIntensity(hp);
            if (_isCritical)
            {
                target *= LowHealthVignetteMath.CriticalFlashWeight(_criticalElapsed);
            }

            // 无障碍：震屏强度同样压低屏幕压暗反馈
            float shakeScale = Mathf.Clamp01(GameSettingsService.ShakeScale);
            target *= Mathf.Lerp(0.35f, 1f, shakeScale);

            _intensity = LowHealthVignetteMath.StepIntensity(_intensity, target, dt);

            if (!LowHealthVignetteMath.IsVisible(_intensity))
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

            Color tint = ComicStyle.Red;
            if (!_hasColor || tint != _lastColor)
            {
                _lastColor = tint;
                _hasColor = true;
                _material.SetColor(VignetteColorProp, tint);
            }

            _material.SetFloat(MaxAlphaProp, 0.88f);
            _material.SetFloat(IntensityProp, _intensity);
            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>供编辑器 / 开发包截图验收：临时把强度锁为 1（发布包里是空操作）。</summary>
        public void DebugPreview(float seconds = 5f)
        {
            _debugPreviewUntil = Mathf.Max(_debugPreviewUntil,
                Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }
#endif
    }
}
