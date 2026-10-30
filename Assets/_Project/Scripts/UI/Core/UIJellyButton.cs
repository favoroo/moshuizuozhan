using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 漫画按钮反馈（P4-A 的 UIJellyButton，P10 改为 Comic 语言，类名保留以免改动调用方）。
    ///
    /// 旧版是果冻弹簧——悬停放大、按下压扁、松开过冲回弹。
    /// 漫画规范明确禁止 bounce/elastic，且动效上限 100ms，所以这里改成：
    /// - 悬停：背景硬阴影缩到一半 + 网点浮现（Halftone Reveal），缩放/倾斜交给 UISpringMotion；
    /// - 按下：硬阴影瞬间归零，像盖印章压下去（Heavy Ink）；
    /// - 松开：80ms 内回到常态，绝不回弹。
    ///
    /// 缩放/旋转为什么不在这里做：同一个物体上通常也挂着 UISpringMotion，
    /// 两个组件同时写 localScale 会互相打架。这里只负责「墨线/网点/阴影」，
    /// 变换由 UISpringMotion 统一驱动（它会读主题里的 hoverScale / hoverTilt）。
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

        private Vector2 _homeShadowOffset;
        private float _homeHalftoneAlpha;
        private Color _homeColor;
        private bool _captured;
        private bool _hovered;
        private bool _pressed;

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

            if (button != null) button.onClick.AddListener(PlayClickFeedback);
        }

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(PlayClickFeedback);
        }

        private bool IsInteractable => button == null || button.IsInteractable();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable) return;
            CaptureHome();
            _pressed = true;
            ApplyState();
            if (motion != null) motion.SetPressed(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            ApplyState();
            if (motion != null) motion.SetPressed(false);
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
            ApplyState();
            if (motion != null)
            {
                motion.SetHovered(false);
                motion.SetPressed(false);
            }
        }

        /// <summary>Button.onClick 的统一反馈；也供键盘/手柄 Submit 或代码直接调用。</summary>
        public void PlayClickFeedback()
        {
            if (motion != null) motion.Punch(0.05f);

            // 一记「印章压下再弹回」：阴影归零 60ms 后恢复
            if (background != null)
            {
                CaptureHome();
                background.ShadowOffset = Vector2.zero;
                CancelInvoke(nameof(RestoreShadow));
                Invoke(nameof(RestoreShadow), 0.06f);
            }
        }

        private void RestoreShadow()
        {
            if (background == null) return;
            background.ShadowOffset = _pressed ? Vector2.zero : _homeShadowOffset;
        }

        private void CaptureHome()
        {
            if (_captured || background == null) return;
            _homeShadowOffset = background.ShadowOffset;
            _homeHalftoneAlpha = background.HalftoneAlpha;
            _homeColor = background.color;
            _captured = true;
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
                background.ShadowOffset = resolved.HoverShadowOffset;
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