using System;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 通用 UI 动效控制器（几何大胆风版）：用 **≤100ms 线性插值**驱动 localScale /
    /// 局部 Z 旋转 / CanvasGroup 透明度，做「块状硬切」反馈，不做任何弹簧回弹。
    ///
    /// 旧版是物理阻尼弹簧（P4-A），漫画风时代允许过冲；几何风规范（Docs/UI-GeometricBold.md）
    /// 明确禁止 bounce/elastic，因此内部实现整体换成 timed lerp，公开 API 与序列化字段保持不变，
    /// 18 个调用方无需改签名。
    ///
    /// 只改 localScale 与 localRotation，不动锚点/尺寸，因此不会与 LayoutGroup 抢布局。
    /// </summary>
    [AddComponentMenu("P1/UI/Spring Motion")]
    [DisallowMultipleComponent]
    public class UISpringMotion : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;

        [Header("自动播放")]
        [SerializeField] private bool playInOnEnable;

        [SerializeField] private float playInDelay;

        [Header("入场（几何风：0.9→1 硬切，不做倾斜）")]
        [Tooltip("入场时的起始缩放倍率（0.9 = 从 90% 硬切到 100%）")]
        [SerializeField, Range(0.01f, 1f)] private float entryStartScale = 0.9f;

        [Tooltip("兼容字段：几何风不再倾斜，固定按 0 处理")]
        [SerializeField, Range(-45f, 45f)] private float entryStartRotation = 0f;

        [Header("交互")]
        [Tooltip("悬停时的放大倍率（hover:scale-105）")]
        [SerializeField, Range(1f, 1.3f)] private float hoverScale = 1.05f;

        [Tooltip("按下时的压缩倍率（active:scale-95）")]
        [SerializeField, Range(0.7f, 1f)] private float pressScale = 0.95f;

        [Header("几何风（P14）")]
        [Tooltip("交互参数优先读 UIThemeSO（1.05 / 0.95 / 0°）；关掉才用上面的手填值")]
        [SerializeField] private bool useThemeInteraction = true;

        [Tooltip("兼容字段：几何风禁止倾斜，保留但默认 0")]
        [SerializeField, Range(-10f, 10f)] private float hoverTiltDegrees = 0f;

        [SerializeField] private bool driveRotation = true;

        [Tooltip("需要 CanvasGroup 才会生效；缺失时自动补一个")]
        [SerializeField] private bool driveAlpha = true;

        [SerializeField] private bool useUnscaledTime = true;

        /// <summary>一次 PlayIn 完整落地（缩放、旋转、透明度都收敛）后触发。</summary>
        public event Action OnPlayedIn;

        /// <summary>PlayOut 完全收起后触发。</summary>
        public event Action OnPlayedOut;

        private Vector3 _homeScale;
        private Quaternion _homeRotation;
        private CanvasGroup _canvasGroup;

        private Vector3 _currentScale;
        private Vector3 _scaleFrom;
        private Vector3 _scaleTo;
        private float _scaleT = 1f;
        private float _scaleDuration = 0.1f;

        private float _tiltCurrent;
        private float _tiltFrom;
        private float _tiltTo;
        private float _tiltT = 1f;

        private float _alpha = 1f;
        private float _alphaSpeed = 10f;
        private float _punch;
        private float _delay;
        private bool _entering;
        private bool _leaving;
        private bool _pressed;
        private bool _hovered;
        private bool _running;
        private bool _built;

        public Vector3 HomeScale => _homeScale;

        public bool IsEntering => _entering;

        public bool IsRunning => _running;

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            if (playInOnEnable && Application.isPlaying)
            {
                PlayIn(playInDelay);
            }
        }

        private void OnDisable()
        {
            _running = false;
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            theme = themeAsset;
            if (themeAsset != null) hoverTiltDegrees = themeAsset.hoverTiltDegrees;
        }

        /// <summary>批量套主题时设置悬停倾角（度）。几何风恒为 0。</summary>
        public void SetTilt(float degrees)
        {
            hoverTiltDegrees = degrees;
        }

        public void Build()
        {
            if (_built) return;
            _built = true;

            _homeScale = transform.localScale;
            _homeRotation = transform.localRotation;
            _currentScale = _homeScale;
            _scaleFrom = _homeScale;
            _scaleTo = _homeScale;
            _tiltCurrent = 0f;
            _tiltFrom = 0f;
            _tiltTo = 0f;
            _punch = 0f;

            if (driveAlpha)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }

                _canvasGroup.alpha = 1f;
            }
        }

        /// <summary>从「略缩小 + 透明」硬切到常态。可重复调用，delay 单位秒。</summary>
        public void PlayIn(float delay = 0f)
        {
            Build();
            _entering = true;
            _leaving = false;
            _pressed = false;
            _hovered = false;
            _delay = Mathf.Max(0f, delay);
            _running = true;

            if (_delay <= 0f)
            {
                BeginEnter();
            }
        }

        /// <summary>缩小并淡出；收敛后触发 OnPlayedOut（不会自动隐藏 GameObject）。</summary>
        public void PlayOut()
        {
            Build();
            _entering = false;
            _leaving = true;
            _pressed = false;
            _hovered = false;
            _delay = 0f;
            _running = true;
            StartScaleTween(_homeScale * 0.9f, CurrentSettings().entranceDuration);
        }

        public void SetPressed(bool value)
        {
            Build();
            if (_pressed == value) return;
            _pressed = value;
            if (value) _hovered = false;
            _running = true;
            ApplyInteractionTargets();
        }

        public void SetHovered(bool value)
        {
            Build();
            if (_hovered == value) return;
            _hovered = value;
            _running = true;
            ApplyInteractionTargets();
        }

        /// <summary>给缩放加一记瞬时脉冲（用于点击、击杀等「弹一下」反馈）。40ms 内线性归零。</summary>
        public void Punch(float strength = 0.08f)
        {
            Build();
            _punch = Mathf.Max(_punch, Mathf.Clamp(strength, 0f, 0.2f));
            _running = true;
        }

        /// <summary>立即回到常态（跳过动画），用于对象池复用或界面重置。</summary>
        public void SnapHome()
        {
            Build();
            _entering = false;
            _leaving = false;
            _pressed = false;
            _hovered = false;
            _delay = 0f;
            _currentScale = _homeScale;
            _scaleFrom = _homeScale;
            _scaleTo = _homeScale;
            _scaleT = 1f;
            _tiltCurrent = 0f;
            _tiltFrom = 0f;
            _tiltTo = 0f;
            _tiltT = 1f;
            _punch = 0f;
            transform.localScale = _homeScale;
            transform.localRotation = _homeRotation;

            if (_canvasGroup != null)
            {
                _alpha = 1f;
                _canvasGroup.alpha = 1f;
            }

            _running = false;
        }

        private void BeginEnter()
        {
            _currentScale = _homeScale * Mathf.Max(0.01f, entryStartScale);
            _scaleFrom = _currentScale;
            _scaleTo = _homeScale;
            _scaleT = 0f;
            _scaleDuration = CurrentSettings().entranceDuration;
            transform.localScale = _currentScale;

            _tiltCurrent = 0f;
            _tiltFrom = 0f;
            _tiltTo = 0f;
            _tiltT = 1f;

            if (driveRotation)
            {
                transform.localRotation = _homeRotation;
            }

            if (_canvasGroup != null)
            {
                _alpha = 0f;
                _canvasGroup.alpha = 0f;
            }
        }

        private UIThemeSO CurrentSettings()
        {
            return UIThemeSO.Resolve(theme);
        }

        private void ApplyInteractionTargets()
        {
            UIThemeSO settings = CurrentSettings();
            float useHoverScale = useThemeInteraction ? settings.hoverScale : hoverScale;
            float usePressScale = useThemeInteraction ? settings.pressScale : pressScale;
            float useTilt = useThemeInteraction ? 0f : hoverTiltDegrees;

            Vector3 target = _homeScale;
            if (_leaving) target = _homeScale * 0.9f;
            else if (_pressed) target = _homeScale * usePressScale;
            else if (_hovered) target = _homeScale * useHoverScale;

            float duration = _pressed ? settings.pressDuration : settings.hoverDuration;
            StartScaleTween(target, duration);

            float tiltTarget = !_leaving && _hovered && !_pressed ? useTilt : 0f;
            _tiltFrom = _tiltCurrent;
            _tiltTo = tiltTarget;
            _tiltT = 0f;
        }

        private void StartScaleTween(Vector3 target, float duration)
        {
            _scaleFrom = _currentScale;
            _scaleTo = target;
            _scaleT = 0f;
            _scaleDuration = Mathf.Max(0.01f, UIThemeSO.Resolve(theme).ClampDuration(duration));
        }

        private void Update()
        {
            if (!_running || !_built) return;

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (deltaTime <= 0f) return;

            if (_delay > 0f)
            {
                _delay -= deltaTime;
                if (_delay > 0f) return;
                BeginEnter();
            }

            UIThemeSO settings = CurrentSettings();

            // 缩放：100ms 内线性硬切（timed lerp），无回弹
            if (_scaleT < 1f)
            {
                _scaleT = Mathf.Min(1f, _scaleT + deltaTime / _scaleDuration);
                _currentScale = Vector3.LerpUnclamped(_scaleFrom, _scaleTo, _scaleT);
            }

            // 点击脉冲：40ms 线性衰减后归零（纯位移反馈，不做曲线抖动）
            if (_punch > 0f)
            {
                _punch = Mathf.Max(0f, _punch - deltaTime / 0.04f * 0.12f);
                if (_punch < 0.0005f) _punch = 0f;
            }

            transform.localScale = _currentScale * (1f + _punch);

            bool rotationSettled = true;
            if (driveRotation)
            {
                if (_tiltT < 1f)
                {
                    _tiltT = Mathf.Min(1f, _tiltT + deltaTime / Mathf.Max(0.01f, settings.hoverDuration));
                    _tiltCurrent = Mathf.Lerp(_tiltFrom, _tiltTo, _tiltT);
                }

                rotationSettled = _tiltT >= 1f;
                transform.localRotation = _homeRotation * Quaternion.Euler(0f, 0f, _tiltCurrent);
            }

            if (_canvasGroup != null)
            {
                float fadeStep = deltaTime / Mathf.Max(0.05f, settings.entranceFadeDuration);
                if (_entering) _alpha = Mathf.MoveTowards(_alpha, 1f, fadeStep);
                else if (_leaving) _alpha = Mathf.MoveTowards(_alpha, 0f, fadeStep);

                _canvasGroup.alpha = _alpha;
            }

            bool scaleSettled = _scaleT >= 1f;

            if (_entering && scaleSettled && rotationSettled && _alpha >= 0.999f)
            {
                _entering = false;
                OnPlayedIn?.Invoke();
            }

            if (_leaving && scaleSettled && (_canvasGroup == null || _alpha <= 0.001f))
            {
                _leaving = false;
                _running = false;
                OnPlayedOut?.Invoke();
                return;
            }

            if (!_entering && !_leaving && !_pressed && !_hovered && scaleSettled && rotationSettled &&
                _punch <= 0.0005f)
            {
                _running = false;
            }
        }
    }
}
