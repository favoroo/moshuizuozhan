using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// NEW! 徽章弹出（漫画规范 Badge Pop）：默认 scale=0 隐藏，
    /// 被选中/悬停时 80ms 内从 0 弹到 1，并带一点反向倾斜消掉。
    ///
    /// 与 UISpringMotion 的区别：徽章只需要「一次性弹出」，不需要持续跟随，
    /// 因此自带极小的插值状态机，避免为了一个角标再挂一套弹簧。
    /// </summary>
    [AddComponentMenu("P1/UI/Comic Badge")]
    [DisallowMultipleComponent]
    public class ComicBadge : MonoBehaviour
    {
        [SerializeField] private bool hiddenOnStart = true;

        [SerializeField, Range(0.02f, 0.1f)] private float popDuration = 0.08f;

        [SerializeField, Range(-20f, 20f)] private float startTilt = -7f;

        [SerializeField] private bool useUnscaledTime = true;

        private float _elapsed;
        private bool _visible;
        private bool _animating;

        public bool IsVisible => _visible;

        private void Awake()
        {
            if (hiddenOnStart) HideImmediate();
        }

        private void OnEnable()
        {
            if (hiddenOnStart && !_visible) HideImmediate();
        }

        /// <summary>弹出显示。</summary>
        public void Play()
        {
            _visible = true;
            _elapsed = 0f;
            _animating = true;
            gameObject.SetActive(true);
            ComicMotion.PopScale(transform, 0f, popDuration, 0f, startTilt);
        }

        /// <summary>立即显示（用于已选中状态的初始帧，避免闪一下）。</summary>
        public void ShowImmediate()
        {
            _visible = true;
            _animating = false;
            _elapsed = popDuration;
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(true);
        }

        public void HideImmediate()
        {
            _visible = false;
            _animating = false;
            _elapsed = 0f;
            transform.localScale = Vector3.zero;
            transform.localRotation = Quaternion.Euler(0f, 0f, startTilt);
        }

        /// <summary>收起：直接归零（漫画风格不做淡出）。</summary>
        public void Hide()
        {
            HideImmediate();
        }

        public void SetShown(bool shown, bool animate = true)
        {
            if (shown)
            {
                if (animate) Play();
                else ShowImmediate();
            }
            else
            {
                HideImmediate();
            }
        }

        private void Update()
        {
            if (!_animating) return;

            _elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_elapsed >= popDuration)
            {
                _animating = false;
                transform.localScale = Vector3.one;
                transform.localRotation = Quaternion.identity;
                return;
            }

            ComicMotion.PopScale(transform, _elapsed, popDuration, 0f, startTilt);
        }
    }
}