using System;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 通用弹簧动效控制器（P4-A 核心）：用物理阻尼弹簧驱动 localScale / 局部 Z 旋转 / CanvasGroup 透明度。
    /// 支持按钮按住（压缩）→ 松开（过冲回弹）、卡片弹入（Overshoot &amp; Settle）、随时 Punch 打断。
    ///
    /// 只改 localScale 与 localRotation，不动锚点/尺寸，因此不会与 LayoutGroup、动画系统或别家代码抢布局。
    /// </summary>
    [AddComponentMenu("P1/UI/Spring Motion")]
    [DisallowMultipleComponent]
    public class UISpringMotion : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;

        [Header("自动播放")]
        [SerializeField] private bool playInOnEnable;

        [SerializeField] private float playInDelay;

        [Header("弹入")]
        [Tooltip("弹入时的起始缩放倍率（0.55 = 从 55% 弹到 100%）")]
        [SerializeField, Range(0.01f, 1f)] private float entryStartScale = 0.55f;

        [Tooltip("弹入时的起始倾角（度），负值向左歪")]
        [SerializeField, Range(-45f, 45f)] private float entryStartRotation = -4f;

        [Header("交互")]
        [Tooltip("悬停时的放大倍率")]
        [SerializeField, Range(1f, 1.3f)] private float hoverScale = 1.045f;

        [Tooltip("按下时的压缩倍率")]
        [SerializeField, Range(0.7f, 1f)] private float pressScale = 0.94f;

        [Header("漫画风（P10）")]
        [Tooltip("交互参数优先读 UIThemeSO（1.06 / 0.97 / -3°）；关掉才用上面的手填值")]
        [SerializeField] private bool useThemeInteraction = true;

        [Tooltip("悬停倾斜角度（负值=逆时针）。漫画规范：hover rotate(-3deg)")]
        [SerializeField, Range(-10f, 10f)] private float hoverTiltDegrees = -3f;

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
        private UISpringVector3 _scale;
        private UISpringFloat _rotation;
        private float _alpha = 1f;
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

        /// <summary>批量套主题时设置悬停倾角（度）。</summary>
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
            _scale.Set(_homeScale);
            _rotation.Set(0f);
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

        /// <summary>从「缩小 + 微倾斜 + 透明」弹到常态。可重复调用，delay 单位秒。</summary>
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
        }

        public void SetPressed(bool value)
        {
            Build();
            if (_pressed == value) return;
            _pressed = value;
            if (value) _hovered = false;
            _running = true;
        }

        public void SetHovered(bool value)
        {
            Build();
            if (_hovered == value) return;
            _hovered = value;
            _running = true;
        }

        /// <summary>给缩放加一记瞬时脉冲（用于点击、击杀等「弹一下」反馈）。</summary>
        /// <remarks>P10：改成指数衰减而非弹簧冲量——漫画规范禁止过冲回弹。</remarks>
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
            _scale.Set(_homeScale);
            _rotation.Set(0f);
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
            _scale.Set(_homeScale * Mathf.Max(0.01f, entryStartScale));
            _rotation.Set(entryStartRotation);
            transform.localScale = _scale.Value;

            if (driveRotation)
            {
                transform.localRotation = _homeRotation * Quaternion.Euler(0f, 0f, _rotation.Value);
            }

            if (_canvasGroup != null)
            {
                _alpha = 0f;
                _canvasGroup.alpha = 0f;
            }
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

            UIThemeSO settings = UIThemeSO.Resolve(theme);
            UISpringSettings spring = _entering ? settings.popSpring : settings.pressSpring;

            float useHoverScale = useThemeInteraction ? settings.hoverScale : hoverScale;
            float usePressScale = useThemeInteraction ? settings.pressScale : pressScale;
            float useTilt = useThemeInteraction ? settings.hoverTiltDegrees : hoverTiltDegrees;

            // 点击脉冲：指数衰减，80ms 内归零，不产生回弹
            _punch = Mathf.Lerp(_punch, 0f, 1f - Mathf.Exp(-deltaTime / 0.07f));
            if (_punch < 0.0005f) _punch = 0f;

            Vector3 targetScale = _homeScale;
            if (_leaving)
            {
                targetScale = _homeScale * 0.01f;
            }
            else if (_pressed)
            {
                targetScale = _homeScale * usePressScale;
            }
            else if (_hovered)
            {
                targetScale = _homeScale * useHoverScale;
            }

            targetScale *= 1f + _punch;

            float restDistance = 5e-4f * Mathf.Max(0.1f, _homeScale.magnitude);
            bool scaleSettled = _scale.Step(targetScale, spring, deltaTime, restDistance);
            transform.localScale = _scale.Value;

            bool rotationSettled = true;
            if (driveRotation)
            {
                // 悬停时整块往左歪 3°，像漫画音效气泡；按下立即回正
                float targetTilt = !_leaving && _hovered && !_pressed ? useTilt : 0f;
                rotationSettled = _rotation.Step(targetTilt, spring, deltaTime, 0.02f, 0.02f);
                transform.localRotation = _homeRotation * Quaternion.Euler(0f, 0f, _rotation.Value);
            }

            if (_canvasGroup != null)
            {
                float fadeStep = deltaTime / Mathf.Max(0.05f, settings.entranceFadeDuration);
                if (_entering) _alpha = Mathf.MoveTowards(_alpha, 1f, fadeStep);
                else if (_leaving) _alpha = Mathf.MoveTowards(_alpha, 0f, fadeStep);

                _canvasGroup.alpha = _alpha;
            }

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

            if (!_entering && !_leaving && !_pressed && !_hovered && scaleSettled && rotationSettled && _punch <= 0.0005f)
            {
                _running = false;
            }
        }
    }
}