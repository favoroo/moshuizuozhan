using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 踩敌墨全屏边缘墨迹（P18-A）。
    ///
    /// 参考 INKWAVE `src/fx/screenfx.js` 的 edgeInk：
    /// 本地玩家站在敌墨上时，屏幕四边浮现敌方队色的有机墨边（底边最厚），
    /// 以 9/s 淡入、3/s 淡出；离开或死亡后自动关闭 Graphic，空闲成本为 0。
    ///
    /// 层级：由 <see cref="HudController"/> 自动装配到 HUD 画布根节点并置于最前（sibling 0），
    /// 因此盖在 3D 场景之上、所有 HUD 文字 / 准星 / 触控按钮之下；
    /// 不放进 SafeArea，保证墨边贴着物理屏幕边缘。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class InkEdgeWarningView : MonoBehaviour
    {
        private const string ShaderName = "P1/UI/InkEdgeWarning";
        private const string StyleResourcePath = "InkEdgeWarningStyle";

        private static readonly int InkColorProp = Shader.PropertyToID("_InkColor");
        private static readonly int IntensityProp = Shader.PropertyToID("_Intensity");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");
        private static readonly int BottomBandProp = Shader.PropertyToID("_BottomBand");
        private static readonly int EdgeBandProp = Shader.PropertyToID("_EdgeBand");
        private static readonly int WobbleProp = Shader.PropertyToID("_Wobble");
        private static readonly int DripProp = Shader.PropertyToID("_DripStrength");
        private static readonly int MaxAlphaProp = Shader.PropertyToID("_MaxAlpha");
        private static readonly int SpecularIntensityProp = Shader.PropertyToID("_SpecularIntensity");
        private static readonly int DripScaleProp = Shader.PropertyToID("_DripScale");

        [SerializeField] private InkEdgeWarningStyleSO style;
        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        private Material _material;
        private InkEdgeWarningStyleSO _loadedStyle;
        private InkEdgeWarningStyleSO _appliedStyle;
        private Canvas _resolvedCanvas;
        private bool _built;
        private bool _shaderMissingLogged;
        private bool _styleLoadAttempted;
        private float _intensity;
        private float _lastAspect = -1f;
        private Color _lastInkColor;
        private bool _hasInkColor;

        // 仅用于截图/调试验收：临时把目标强度锁为 1（DebugPreview 在发布包里是空操作）
        private float _debugPreviewUntil;

        /// <summary>当前墨边强度 0~1（供测试 / 调试面板读取）。</summary>
        public float CurrentIntensity => _intensity;

        /// <summary>当前是否真的在绘制（Image 启用）。</summary>
        public bool IsShowing => overlayImage != null && overlayImage.enabled;

        public InkEdgeWarningStyleSO Style => ResolveStyle();

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
                        Debug.LogWarning($"[InkEdgeWarning] 找不到 Shader {ShaderName}，踩敌墨屏幕边缘提示已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "InkEdgeWarning (Runtime)",
                    hideFlags = HideFlags.DontSave
                };

                overlayImage.material = _material;
                overlayImage.enabled = false;
                ApplyStyle(ResolveStyle());

                if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
                _built = true;
            }
            catch (System.Exception e)
            {
                // 纯装饰性屏幕反馈失败不应让整局游戏起不来
                Debug.LogWarning("[InkEdgeWarning] 构建屏幕边缘墨迹失败：" + e.Message);
                _built = false;
            }
        }

        private void Update()
        {
            bool inEnemyInk = false;
            PlayerController player = PlayerController.LocalPlayer;
            if (player != null && player.Health != null && player.Health.IsAlive
                && player.Motor != null && player.Motor.IsInEnemyInk)
            {
                inEnemyInk = true;
            }

            Tick(Time.deltaTime, inEnemyInk);
        }

        /// <summary>
        /// 推进一帧强度并刷新材质。Update 内部调用；测试 / 外部驱动可直接调用。
        /// </summary>
        public void Tick(float dt, bool inEnemyInk)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null) return;

            if (_material == null) return;

            InkEdgeWarningStyleSO resolved = ResolveStyle();
            if (_appliedStyle != resolved) ApplyStyle(resolved);

            if (_resolvedCanvas == null) _resolvedCanvas = parentCanvas;
            if (_resolvedCanvas == null) _resolvedCanvas = GetComponentInParent<Canvas>();

            // HUD 画布在 Ready/Result 阶段整体关闭时，墨边也必须立即退场
            if (_resolvedCanvas != null && !_resolvedCanvas.enabled)
            {
                inEnemyInk = false;
            }

            if (_debugPreviewUntil > Time.unscaledTime) inEnemyInk = true;

            _intensity = InkEdgeWarningMath.StepIntensity(
                _intensity, inEnemyInk, resolved.fadeInRate, resolved.fadeOutRate, dt);

            if (!InkEdgeWarningMath.IsVisible(_intensity, resolved.visibleThreshold))
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

            Color ink = ResolveEnemyInkColor();
            if (!_hasInkColor || !Approximately(ink, _lastInkColor))
            {
                _lastInkColor = ink;
                _hasInkColor = true;
                _material.SetColor(InkColorProp, ink);
            }

            _material.SetFloat(IntensityProp, _intensity);
            if (!overlayImage.enabled) overlayImage.enabled = true;
        }

        /// <summary>供编辑器/开发包截图验收：临时强制显示若干秒（发布包里是空操作）。</summary>
        public void DebugPreview(float seconds = 5f)
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;

            _debugPreviewUntil = Mathf.Max(_debugPreviewUntil,
                Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }

        private InkEdgeWarningStyleSO ResolveStyle()
        {
            if (style != null) return style;

            if (!_styleLoadAttempted)
            {
                _styleLoadAttempted = true;
                _loadedStyle = Resources.Load<InkEdgeWarningStyleSO>(StyleResourcePath);
            }

            return InkEdgeWarningStyleSO.Resolve(_loadedStyle);
        }

        private void ApplyStyle(InkEdgeWarningStyleSO s)
        {
            _appliedStyle = s;
            if (_material == null || s == null) return;

            _material.SetFloat(BottomBandProp, s.bottomBand);
            _material.SetFloat(EdgeBandProp, s.edgeBand);
            _material.SetFloat(WobbleProp, s.wobble);
            _material.SetFloat(DripProp, s.dripStrength);
            _material.SetFloat(MaxAlphaProp, s.maxAlpha);
            _material.SetFloat(SpecularIntensityProp, s.specularIntensity);
            _material.SetFloat(DripScaleProp, s.dripLengthScale);
        }

        /// <summary>敌方队色（踩在谁的墨上就提示谁的颜色）。</summary>
        private static Color ResolveEnemyInkColor()
        {
            Team enemyTeam = Team.TeamB;
            PlayerController player = PlayerController.LocalPlayer;
            if (player != null && player.Health != null)
            {
                enemyTeam = player.Health.Team.GetOpponent();
            }

            Color color;
            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                color = surface.GetTeamColor(enemyTeam);
            }
            else
            {
                color = enemyTeam == Team.TeamA
                    ? new Color(1.00f, 0.44f, 0.06f, 1f)
                    : new Color(0.15f, 0.42f, 1.00f, 1f);
            }

            // Linear 工程里材质 Color 属性不会自动做 sRGB→Linear 转换（对齐 P14 的顶面墨色修复）
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                color = color.linear;
            }

            color.a = 1f;
            return color;
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.0005f
                   && Mathf.Abs(a.g - b.g) < 0.0005f
                   && Mathf.Abs(a.b - b.b) < 0.0005f
                   && Mathf.Abs(a.a - b.a) < 0.0005f;
        }
    }
}
