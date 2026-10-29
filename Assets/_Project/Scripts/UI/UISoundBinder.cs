using P1.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 为 UI 按钮自动绑定悬停与点击音效（P1-Audio）。
    /// 已有其他音频绑定或果冻按钮时支持防叠音保护。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UISoundBinder : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [SerializeField] private UiSoundKind clickSound = UiSoundKind.Click;
        [SerializeField] private UiSoundKind hoverSound = UiSoundKind.Hover;
        [SerializeField] private bool playHover = true;

        private Button _button;

        public UiSoundKind ClickSound { get => clickSound; set => clickSound = value; }
        public UiSoundKind HoverSound { get => hoverSound; set => hoverSound = value; }
        public bool PlayHover { get => playHover; set => playHover = value; }

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!playHover || (_button != null && !_button.IsInteractable()))
                return;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUISound(hoverSound);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_button != null && !_button.IsInteractable())
                return;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayUISound(clickSound);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoBindScene()
        {
            var buttons = Resources.FindObjectsOfTypeAll<Button>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button btn = buttons[i];
                if (btn == null || !btn.gameObject.scene.isLoaded)
                    continue;

                EnsureBinder(btn);
            }
        }

        public static UISoundBinder EnsureBinder(Button button)
        {
            if (button == null) return null;
            var binder = button.GetComponent<UISoundBinder>();
            if (binder == null)
            {
                binder = button.gameObject.AddComponent<UISoundBinder>();
            }
            return binder;
        }
    }
}
