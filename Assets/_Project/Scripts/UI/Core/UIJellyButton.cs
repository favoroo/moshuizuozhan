using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 漫画按钮反馈（P4-A 的 UIJellyButton，P10 改为 Comic 语言，P19 波次1a 升级为 t-1 按压物理）。
    ///
    /// 旧版是果冻弹簧——悬停放大、按下压扁、松开过冲回弹。
    /// 几何风明确禁止 bounce/elastic，P19 t-1 把按压改成「硬切压印 + Cubic-Out 回弹」：
    /// - 悬停：硬影 (3,-4)→(5,-6) 硬切（阴影变大，不补间颜色/阴影），缩放交给 UISpringMotion；
    /// - 按下（0ms 硬切）：硬影瞬间归零，内容 RectTransform 向阴影方向位移同样像素（压进底板）；
    /// - 松开：0.1s Cubic-Out，缩放回 1.0（UISpringMotion）、内容归位、阴影恢复。
    ///
    /// 缩放/旋转为什么不在这里做：同一个物体上通常也挂着 UISpringMotion，
    /// 两个组件同时写 localScale 会互相打架。这里只负责「墨线/网点/阴影/内容位移」，
    /// 变换由 UISpringMotion 统一驱动（它会读主题里的 hoverScale / pressScale / pressInstant）。
    /// </summary>
    [AddComponentMenu("P1/UI/Comic Button FX")]
    public class UIJellyButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private UIThemeSO theme;

        [SerializeField] private Button button;

        [SerializeField] private UISpringMotion motion;

        [SerializeField] private UIInkShapeGraphic background;

        [Header("漫画反馈")]
        [Tooltip("悬停时把填充色翻成强调色（默认关闭，避免盖掉队伍色语义）")]
        [SerializeField] private bool tintOnHover;

        [SerializeField] private Color hoverTint = new Color(1f, 0.8f, 0f, 1f);

        [Header("t-1 硬影与按压位移（P19）")]
        [Tooltip("按下时要一起位移的内容层（标签/图标容器）。留空自动取背景之外的第一层子物体")]
        [SerializeField] private RectTransform pressContent;

        [Tooltip("在 Awake/Configure 时自动把主题按钮硬影套到背景上（旧界面保持关闭，由各界面自行开启）")]
        [SerializeField] private bool useThemeHardShadow;

        private Vector2 _homeShadowOffset;
        private float _homeHalftoneAlpha;
        private Color _homeColor;
        private bool _captured;
        private bool _hovered;
        private bool _pressed;

        // 松开回弹（阴影 + 内容一起走 0.1s Cubic-Out，缩放由 UISpringMotion 同步回弹）
        private RectTransform _content;
        private Vector2 _contentHome;
        private bool _contentCaptured;
        private Vector2 _contentShift;
        private Vector2 _shiftFrom;
        private Vector2 _shiftTo;
        private Vector2 _shadowFrom;
        private Vector2 _shadowTo;
        private float _releaseT = 1f;
        private float _releaseDuration = 0.1f;
        private UICubicBezier _releaseEasing = UICubicBezier.EaseOut;
        private bool _releasing;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (motion == null) motion = GetComponent<UISpringMotion>();
            if (background == null) background = GetComponent<UIInkShapeGraphic>();
            if (background == null) background = GetComponentInChildren<UIInkShapeGraphic>();
            if (theme == null)
            {
                UIInkCard card = GetComponentInParent<UIInkCard>();
                if (card != null) theme = card.Theme;
            }

            if (motion != null) motion.SetTheme(theme);
            if (useThemeHardShadow) EnableThemeHardShadow();

            if (button != null) button.onClick.AddListener(PlayClickFeedback);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(PlayClickFeedback);
        }

        private void OnDisable()
        {
            _releasing = false;
            if (background != null && _captured) background.ShadowOffset = _homeShadowOffset;
            RestoreContentInstant();
        }

        private bool IsInteractable => button == null || button.IsInteractable();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable) return;
            CaptureHome();
            _pressed = true;
            _releasing = false;
            ApplyState();

            // t-1 压印：硬影 0ms 归零，内容 0ms 压进阴影原来的位置。
            if (background != null && background.HardShadow)
            {
                background.ShadowOffset = Vector2.zero;
                ApplyContentShift(UIThemeSO.Resolve(theme).pressContentShift
                    ? _homeShadowOffset
                    : Vector2.zero);
            }

            if (motion != null) motion.SetPressed(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;

            // 先按「当前还在按」的状态启动 0.1s 回弹（此时阴影仍是 0），
            // 再由回弹把阴影/内容送回悬停或常态；不能先 ApplyState，否则阴影会瞬跳。
            BeginRelease();
            if (background != null)
            {
                background.HalftoneAlpha = UIThemeSO.Resolve(theme).HalftoneAlphaFor(_hovered);
                if (tintOnHover) background.color = _hovered ? hoverTint : _homeColor;
            }

            if (motion != null)
            {
                // 指针仍停在按钮上时，松开要回到 hover 态（scale 1.05 + 悬停阴影），
                // 不能落回 1.0；先恢复 hovered 再松开，回弹目标才是对的。
                if (_hovered && IsInteractable) motion.SetHovered(true);
                motion.SetPressed(false);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsInteractable) return;
            CaptureHome();
            _hovered = true;
            ApplyState();
            if (motion != null) motion.SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 指针移出时同时松开按压，避免在按钮外抬起导致「卡住压扁」
            _hovered = false;
            _pressed = false;
            _releasing = false;
            ApplyState();
            ApplyContentShift(Vector2.zero);
            if (motion != null)
            {
                motion.SetHovered(false);
                motion.SetPressed(false);
            }
        }

        private void Update()
        {
            if (!_releasing) return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f) return;

            _releaseT = Mathf.Min(1f, _releaseT + deltaTime / Mathf.Max(0.01f, _releaseDuration));
            float eased = _releaseEasing.Evaluate(_releaseT);

            if (background != null)
            {
                background.ShadowOffset = Vector2.LerpUnclamped(_shadowFrom, _shadowTo, eased);
            }

            ApplyContentShift(Vector2.LerpUnclamped(_shiftFrom, _shiftTo, eased));

            if (_releaseT >= 1f)
            {
                _releasing = false;
            }
        }

        /// <summary>Button.onClick 的统一反馈；也供键盘/手柄 Submit 或代码直接调用。</summary>
        public void PlayClickFeedback()
        {
            if (motion != null) motion.Punch(0.05f);

            // 一记「印章压下再弹回」：阴影归零 60ms 后按 t-1 曲线回弹
            if (background != null && background.HardShadow && !_pressed)
            {
                CaptureHome();
                background.ShadowOffset = Vector2.zero;
                if (UIThemeSO.Resolve(theme).pressContentShift) ApplyContentShift(_homeShadowOffset);
                _releasing = false;
                CancelInvoke(nameof(BeginRelease));
                Invoke(nameof(BeginRelease), 0.06f);
            }
        }

        /// <summary>0.1s Cubic-Out：阴影 + 内容一起回弹到当前悬停/常态。</summary>
        private void BeginRelease()
        {
            if (background == null)
            {
                RestoreContentInstant();
                return;
            }

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            _releaseDuration = Mathf.Max(0.02f, resolved.pressReboundDuration);
            _releaseEasing = resolved.pressReboundEasing;

            _shadowFrom = background.ShadowOffset;
            _shadowTo = _pressed
                ? Vector2.zero
                : (_hovered && background.HardShadow ? resolved.GetHardShadowOffset(UIHardShadowLevel.ButtonHover) : _homeShadowOffset);

            _shiftFrom = _contentShift;
            _shiftTo = Vector2.zero;

            _releaseT = 0f;
            _releasing = true;
        }

        private void RestoreContentInstant()
        {
            if (_content == null || !_contentCaptured) return;
            _contentShift = Vector2.zero;
            _content.anchoredPosition = _contentHome;
        }

        private void ApplyContentShift(Vector2 shift)
        {
            if (_content == null || !_contentCaptured) return;
            _contentShift = shift;
            _content.anchoredPosition = _contentHome + shift;
        }

        private void CaptureHome()
        {
            if (_captured && (_content != null || _contentCaptured)) return;

            if (!_captured)
            {
                if (background != null)
                {
                    _homeShadowOffset = background.ShadowOffset;
                    _homeHalftoneAlpha = background.HalftoneAlpha;
                    _homeColor = background.color;
                }

                _captured = true;
            }

            // 内容层可能比 Configure 晚一步创建（先配按钮、后建 Label），每次按下时重试一次。
            if (_content == null && !_contentCaptured)
            {
                _content = ResolvePressContent();
                if (_content != null)
                {
                    _contentHome = _content.anchoredPosition;
                    _contentCaptured = true;
                }
            }
        }

        private RectTransform ResolvePressContent()
        {
            if (pressContent != null) return pressContent;
            if (transform.childCount == 0) return null;

            bool backgroundIsChild = background != null && background.transform != transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (backgroundIsChild && child == background.transform) continue;
                if (child is RectTransform rect) return rect;
            }

            return null;
        }

        private void ApplyState()
        {
            if (background == null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            if (_pressed)
            {
                // Heavy Ink：阴影瞬间归零，制造墨水压印感
                background.ShadowOffset = Vector2.zero;
                background.HalftoneAlpha = resolved.HalftoneAlphaFor(true);
                if (tintOnHover) background.color = ComicStyle.Darken(hoverTint, 0.12f);
            }
            else if (_hovered)
            {
                background.ShadowOffset = background.HardShadow
                    ? resolved.GetHardShadowOffset(UIHardShadowLevel.ButtonHover)
                    : resolved.HoverShadowOffset;
                background.HalftoneAlpha = resolved.HalftoneAlphaFor(true);
                if (tintOnHover) background.color = hoverTint;
            }
            else
            {
                background.ShadowOffset = _homeShadowOffset;
                background.HalftoneAlpha = _homeHalftoneAlpha;
                if (tintOnHover) background.color = _homeColor;
            }
        }

        /// <summary>
        /// 一键开按钮硬影（t-1）：背景改用主题的按钮档偏移 + 不透明墨黑。
        /// 不开也能用：旧界面保持无阴影，按下时只做缩放反馈。
        /// </summary>
        public void EnableThemeHardShadow(UIHardShadowLevel level = UIHardShadowLevel.Button)
        {
            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            if (background == null || !resolved.HasHardShadow(level)) return;
            useThemeHardShadow = true;
            resolved.ApplyHardShadow(background, level);
            _captured = false;
            CaptureHome();
        }

        /// <summary>指定按下时要一起位移的内容层（默认自动取背景之外的第一层子物体）。</summary>
        public void SetPressContent(RectTransform content)
        {
            _content = content;
            _contentCaptured = false;
            _contentShift = Vector2.zero;
            if (content != null)
            {
                _contentHome = content.anchoredPosition;
                _contentCaptured = true;
            }
        }

        public void Configure(UIThemeSO themeAsset, Button targetButton = null,
            UIInkShapeGraphic targetBackground = null)
        {
            theme = themeAsset;
            if (targetButton != null) button = targetButton;
            if (targetBackground != null) background = targetBackground;
            if (motion == null) motion = GetComponent<UISpringMotion>();
            if (motion != null)
            {
                motion.SetTheme(theme);
                motion.SetTilt(UIThemeSO.Resolve(theme).hoverTiltDegrees);
            }

            _captured = false;
            _content = pressContent;
            _contentCaptured = false;
            CaptureHome();
        }

        /// <summary>供主题安装器/运行时批量套色：悬停翻色开关。</summary>
        public void SetHoverTint(bool enabled, Color tint)
        {
            tintOnHover = enabled;
            hoverTint = tint;
        }
    }
}