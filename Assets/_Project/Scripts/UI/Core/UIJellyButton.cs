using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 果冻按钮（P4-A）：在标准 uGUI Button 之上叠一层弹簧反馈。
    /// 悬停放大 / 按下压缩 / 松开过冲回弹，并让背景墨滴同步晃动。
    /// 不改变 Button 的点击语义（键盘、手柄 Submit 仍然可用）。
    /// </summary>
    [AddComponentMenu("P1/UI/Jelly Button")]
    public class UIJellyButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private UIThemeSO theme;

        [SerializeField] private Button button;

        [SerializeField] private UISpringMotion motion;

        [SerializeField] private UIInkShapeGraphic background;

        [SerializeField] private bool wobbleOnPress = true;

        [SerializeField, Range(0f, 0.25f)] private float pressWobble = 0.11f;

        [SerializeField, Range(0f, 0.25f)] private float hoverWobble = 0.045f;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (motion == null) motion = GetComponent<UISpringMotion>();
            if (motion == null) motion = gameObject.AddComponent<UISpringMotion>();
            if (background == null) background = GetComponent<UIInkShapeGraphic>();
            if (background == null) background = GetComponentInChildren<UIInkShapeGraphic>();
            if (theme == null)
            {
                UIInkCard card = GetComponentInParent<UIInkCard>();
                if (card != null) theme = card.Theme;
            }

            motion.SetTheme(theme);

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

            motion.SetPressed(true);
            if (wobbleOnPress && background != null)
            {
                background.PlayWobble(pressWobble, 0.38f);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            motion.SetPressed(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsInteractable) return;

            motion.SetHovered(true);
            if (background != null)
            {
                background.PlayWobble(hoverWobble, 0.30f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // 指针移出时同时松开按压，避免在按钮外松开导致「卡住压扁」
            motion.SetHovered(false);
            motion.SetPressed(false);
        }

        /// <summary>Button.onClick 的统一反馈；也供键盘/手柄 Submit 或代码直接调用。</summary>
        public void PlayClickFeedback()
        {
            if (motion == null) return;

            motion.Punch(0.06f);
            if (wobbleOnPress && background != null)
            {
                background.PlayWobble(pressWobble * 0.9f, 0.34f);
            }
        }

        public void Configure(UIThemeSO themeAsset, Button targetButton = null,
            UIInkShapeGraphic targetBackground = null)
        {
            theme = themeAsset;
            if (targetButton != null) button = targetButton;
            if (targetBackground != null) background = targetBackground;
            if (motion == null) motion = GetComponent<UISpringMotion>();
            if (motion != null) motion.SetTheme(theme);
        }
    }
}