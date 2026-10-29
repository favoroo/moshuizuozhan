using System.Collections.Generic;
using P1.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端触控层：左侧移动摇杆、右侧瞄准摇杆、射击/潜行长按键与手雷/大招点按键。
    /// 运行时自建 UI，桌面平台默认隐藏，避免影响键鼠操作。
    /// </summary>
    public class MobileTouchUI : MonoBehaviour
    {
        [SerializeField] private bool forceShowInEditor;
        [SerializeField] private float moveStickRadius = 120f;
        [SerializeField] private float aimStickRadius = 130f;

        private Canvas _canvas;
        private VirtualJoystick _moveStick;
        private VirtualJoystick _aimStick;
        private HoldButton _fireButton;
        private HoldButton _swimButton;
        private TouchButton _jumpButton;
        private TouchButton _bombButton;
        private TouchButton _specialButton;

        public bool IsShown => _canvas != null && _canvas.gameObject.activeSelf;

        private void Awake()
        {
            bool shouldShow = Application.isMobilePlatform || forceShowInEditor || Debug.isDebugBuild && Application.isMobilePlatform;
            if (!shouldShow)
            {
                enabled = false;
                return;
            }

            Build();
        }

        private void Update()
        {
            var player = PlayerController.LocalPlayer;
            if (player == null) return;

            player.TouchMoveInput = _moveStick != null ? _moveStick.Value : Vector2.zero;
            player.TouchAimInput = _aimStick != null ? _aimStick.Value : Vector2.zero;
            player.TouchFireHeld = _fireButton != null && _fireButton.IsHeld;
            player.TouchSwimHeld = _swimButton != null && _swimButton.IsHeld;

            player.TouchBombHeld = _bombButton != null && _bombButton.IsHeld;

            if (_jumpButton != null && _jumpButton.ConsumeTap())
            {
                player.TouchJumpTriggered = true;
            }

            if (_bombButton != null && _bombButton.ConsumeTap())
            {
                player.TouchBombTriggered = true;
            }

            if (_specialButton != null && _specialButton.ConsumeTap())
            {
                player.TouchSpecialTriggered = true;
            }
        }

        private void Build()
        {
            var canvasGo = new GameObject("MobileTouch_Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var root = canvasGo.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // 刘海屏安全区：所有触控元素放在安全区容器内
            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(root, false);
            var safe = safeGo.GetComponent<RectTransform>();
            safe.anchorMin = Vector2.zero;
            safe.anchorMax = Vector2.one;
            safe.offsetMin = Vector2.zero;
            safe.offsetMax = Vector2.zero;
            safeGo.AddComponent<SafeAreaFitter>();

            root = safe;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem_Mobile");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            Sprite sprite = CreateWhiteSprite();

            _moveStick = VirtualJoystick.Create(root, "MoveStick", new Vector2(230f, 230f),
                moveStickRadius, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.38f), sprite);

            _aimStick = VirtualJoystick.Create(root, "AimStick", new Vector2(-300f, 250f),
                aimStickRadius, new Color(0.4f, 0.7f, 1f, 0.16f), new Color(0.45f, 0.75f, 1f, 0.42f), sprite);

            _fireButton = HoldButton.Create(root, "FireButton", "射击", new Vector2(-155f, 420f), 132f,
                new Color(1f, 0.45f, 0.1f, 0.55f), sprite);

            _swimButton = HoldButton.Create(root, "SwimButton", "潜行", new Vector2(-150f, 165f), 120f,
                new Color(0.2f, 0.5f, 1f, 0.5f), sprite);

            _jumpButton = TouchButton.Create(root, "JumpButton", "跳跃", new Vector2(-320f, 440f), 104f,
                new Color(0.35f, 0.85f, 0.55f, 0.5f), sprite);

            _bombButton = TouchButton.Create(root, "BombButton", "手雷", new Vector2(-470f, 470f), 108f,
                new Color(0.55f, 0.3f, 1f, 0.55f), sprite);

            _specialButton = TouchButton.Create(root, "SpecialButton", "大招", new Vector2(-620f, 320f), 108f,
                new Color(1f, 0.85f, 0.2f, 0.55f), sprite);
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>把 RectTransform 收缩到 Screen.safeArea，适配刘海屏与手势条。</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        private Rect _lastSafeArea;
        private Vector2Int _lastScreen;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafeArea ||
                Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            var rt = (RectTransform)transform;
            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            if (Screen.width <= 0 || Screen.height <= 0) return;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>虚拟摇杆：按下后拖动，返回归一化方向与力度。</summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }

        private RectTransform _knob;
        private float _radius;
        private Vector2 _center;

        public static VirtualJoystick Create(Transform parent, string name, Vector2 anchoredPos,
            float radius, Color bgColor, Color knobColor, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);

            var bg = go.GetComponent<Image>();
            bg.sprite = sprite;
            bg.color = bgColor;
            bg.raycastTarget = true;

            var knobGo = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            knobGo.transform.SetParent(go.transform, false);
            var knobRt = knobGo.GetComponent<RectTransform>();
            knobRt.anchorMin = new Vector2(0.5f, 0.5f);
            knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.sizeDelta = new Vector2(radius * 0.85f, radius * 0.85f);

            var knobImg = knobGo.GetComponent<Image>();
            knobImg.sprite = sprite;
            knobImg.color = knobColor;
            knobImg.raycastTarget = false;

            var stick = go.AddComponent<VirtualJoystick>();
            stick._knob = knobRt;
            stick._radius = radius;
            return stick;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out _center);
            UpdateValue(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateValue(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Value = Vector2.zero;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
        }

        private void UpdateValue(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local);

            Vector2 delta = local - _center;
            float magnitude = Mathf.Clamp01(delta.magnitude / _radius);
            Vector2 dir = delta.sqrMagnitude > 1e-4f ? delta.normalized : Vector2.zero;

            Value = dir * magnitude;
            if (_knob != null) _knob.anchoredPosition = dir * magnitude * (_radius * 0.75f);
        }
    }

    /// <summary>长按按钮（射击 / 潜行）。</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsHeld { get; private set; }

        public static HoldButton Create(Transform parent, string name, string label, Vector2 anchoredPos,
            float size, Color color, Sprite sprite)
        {
            var (go, _) = CreateButtonBase(parent, name, label, anchoredPos, size, color, sprite);
            return go.AddComponent<HoldButton>();
        }

        public void OnPointerDown(PointerEventData eventData) => IsHeld = true;
        public void OnPointerUp(PointerEventData eventData) => IsHeld = false;
        private void OnDisable() => IsHeld = false;

        internal static (GameObject, Text) CreateButtonBase(Transform parent, string name, string label,
            Vector2 anchoredPos, float size, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = true;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = Mathf.RoundToInt(size * 0.30f);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;

            return (go, text);
        }
    }

    /// <summary>点按/长按按钮（手雷 / 大招），每次按下产生一次 tap 事件，同时支持 IsHeld 按住状态。</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private bool _tapped;
        public bool IsHeld { get; private set; }

        public bool ConsumeTap()
        {
            if (!_tapped) return false;
            _tapped = false;
            return true;
        }

        public static TouchButton Create(Transform parent, string name, string label, Vector2 anchoredPos,
            float size, Color color, Sprite sprite)
        {
            var (go, _) = HoldButton.CreateButtonBase(parent, name, label, anchoredPos, size, color, sprite);
            return go.AddComponent<TouchButton>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _tapped = true;
            IsHeld = true;
        }

        public void OnPointerUp(PointerEventData eventData) => IsHeld = false;
        private void OnDisable() => IsHeld = false;
    }
}
