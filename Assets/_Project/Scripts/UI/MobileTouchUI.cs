using System.Collections.Generic;
using P1.Core;
using P1.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端触控交互系统（P9 第三人称，参考《和平精英》）：
    /// 1. 左侧：半屏动态浮动摇杆（Floating Joystick），手指落在左下半屏任意位置均可唤醒并平滑驱动移动；
    /// 2. 右侧：人体工学扇形按键群（射击大键、潜行、跳跃、手雷、大招），右下角锚定；
    /// 3. 右半屏：滑屏转视角感应区（每帧触摸增量驱动相机 Yaw/Pitch；开火键与手雷键拖拽也可转视角）；
    /// 4. 手雷：短按（&lt;0.22s）秒投，长按进入瞄准态并显示 64 点抛物线与爆炸范围红圈，松手投掷；
    /// 5. 自定义键位：支持在游戏内点击【⚙️ 键位】自由拖拽换位、滑块调节大小与透明度，本地持久化记忆。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MobileTouchUI : MonoBehaviour
    {
        [Header("调试与显示")]
        [Tooltip("在 Unity 编辑器中强制显示触控层，方便鼠标模拟与调试。桌面/编辑器默认关闭，避免全屏感应区抢鼠标视角")]
        [SerializeField] private bool showTouchUiInEditor = false;

        [Tooltip("旧版字段，仅保留场景序列化兼容；P9 起不再参与显示判断，请使用 showTouchUiInEditor")]
        [SerializeField] private bool forceShowInEditor = true;

        [Header("左侧动态浮动摇杆参数")]
        [SerializeField] private float moveStickRadius = 110f;
        [SerializeField] private float moveDeadZone = 8f;
        [SerializeField] private Vector2 moveStickDefaultPos = new Vector2(200f, 200f);

        [Header("按键图标资产引用（为空时自动从 Art/Sprites/Icons 加载）")]
        [SerializeField] private Sprite fireIcon;
        [SerializeField] private Sprite swimIcon;
        [SerializeField] private Sprite jumpIcon;
        [SerializeField] private Sprite bombIcon;
        [SerializeField] private Sprite specialIcon;
        [SerializeField] private Sprite joystickKnobIcon;

        private Canvas _canvas;
        private RectTransform _safeArea;
        private DynamicFloatingJoystick _floatingJoystick;
        private RightAimSwipeZone _aimSwipeZone;
        private MobileLayoutCustomizer _customizer;

        private MobileActionButton _fireButton;
        private MobileActionButton _swimButton;
        private MobileActionButton _jumpButton;
        private MobileActionButton _bombButton;
        private MobileActionButton _specialButton;

        private MatchManager _match;
        private bool _subscribedMatch;

        private static Sprite _circleFillSprite;
        private static Sprite _circleRingSprite;

        public bool IsShown => _canvas != null && _canvas.gameObject.activeInHierarchy;
        public DynamicFloatingJoystick FloatingJoystick => _floatingJoystick;
        public MobileLayoutCustomizer Customizer => _customizer;

        /// <summary>是否处于触屏驱动模式（真机，或编辑器里手动勾选 showTouchUiInEditor 的模拟）。</summary>
        public static bool IsTouchSimulationActive { get; private set; }

        public void OpenLayoutCustomizer()
        {
            EnsureBuilt();
            if (_canvas != null && !_canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(true);
            }
            _customizer?.Open();
        }

        private void Awake()
        {
            bool shouldShow = ShouldShowTouchUi();
            IsTouchSimulationActive = shouldShow;

            if (shouldShow)
            {
                EnsureBuilt();
            }

            var hud = FindAnyObjectByType<HudController>();
            if (hud != null && shouldShow) hud.ApplyMobileLayout();
        }

        public bool ShowTouchUiInEditor
        {
            get => showTouchUiInEditor;
            set
            {
                showTouchUiInEditor = value;
                IsTouchSimulationActive = ShouldShowTouchUi();
                UpdateVisibilityForCurrentPhase();
            }
        }

        /// <summary>
        /// 真机始终显示；桌面/编辑器默认隐藏（避免全屏透明感应区抢鼠标视角），
        /// 需要模拟手游时勾选 showTouchUiInEditor 即可。
        /// </summary>
        private bool ShouldShowTouchUi()
        {
            return Application.isMobilePlatform ||
                   (Application.isEditor && (showTouchUiInEditor || forceShowInEditor));
        }

        private void OnEnable()
        {
            if (ShouldShowTouchUi())
                IsTouchSimulationActive = true;
            SubscribeMatch();
            UpdateVisibilityForCurrentPhase();
        }

        private void OnDisable()
        {
            IsTouchSimulationActive = false;
            UnsubscribeMatch();
            ResetAllInputs();
        }

        private void OnDestroy()
        {
            IsTouchSimulationActive = false;
            UnsubscribeMatch();
            if (_customizer != null)
            {
                _customizer.OnClosed -= HandleCustomizerClosed;
            }
        }

        private void Start()
        {
            var hud = FindAnyObjectByType<HudController>();
            if (hud != null) hud.ApplyMobileLayout();

            SubscribeMatch();
            UpdateVisibilityForCurrentPhase();
        }

        private void SubscribeMatch()
        {
            if (_subscribedMatch) return;
            _match = MatchManager.Instance != null ? MatchManager.Instance : FindAnyObjectByType<MatchManager>();
            if (_match != null)
            {
                _match.PhaseChanged += HandlePhaseChanged;
                _subscribedMatch = true;
            }
        }

        private void UnsubscribeMatch()
        {
            if (!_subscribedMatch) return;
            if (_match != null)
            {
                _match.PhaseChanged -= HandlePhaseChanged;
            }
            _subscribedMatch = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            // 如果玩家当前正在进行键位自定义编辑，保持画布展示供其拖拽
            if (_customizer != null && _customizer.IsEditing)
                return;

            switch (phase)
            {
                case MatchManager.Phase.Ready:
                    // 首页准备阶段：完全隐藏，不遮挡首页比赛/选枪/更新按钮
                    SetVisible(false);
                    break;
                case MatchManager.Phase.Countdown:
                case MatchManager.Phase.Playing:
                    // 倒计时与游玩阶段：显示操作按钮与摇杆
                    SetVisible(ShouldShowTouchUi());
                    break;
                case MatchManager.Phase.Finished:
                case MatchManager.Phase.Result:
                    // 比赛结束与结算阶段：隐藏操作按钮，避免遮挡结算画面与按钮
                    SetVisible(false);
                    break;
            }
        }

        private void HandleCustomizerClosed()
        {
            UpdateVisibilityForCurrentPhase();
        }

        public void UpdateVisibilityForCurrentPhase()
        {
            if (_match != null)
            {
                HandlePhaseChanged(_match.CurrentPhase);
            }
            else
            {
                // 无比赛管理器（如独立 UI 测试场景），按环境配置默认可见
                SetVisible(ShouldShowTouchUi());
            }
        }

        /// <summary>
        /// 显隐移动端触控交互层：
        /// 隐藏时关闭 Canvas 并重置所有输入与摇杆，绝不产生多余渲染或拦截底层 UI 射线检测。
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible && _canvas == null)
            {
                EnsureBuilt();
            }

            if (_canvas == null) return;

            if (!visible)
            {
                ResetAllInputs();
            }

            if (_canvas.gameObject.activeSelf != visible)
            {
                _canvas.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 确保 Canvas 及全部触控按键、摇杆与感应区已构建完毕。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_canvas != null) return;
            LoadIconsIfNeeded();
            Build();
        }

        /// <summary>
        /// 重置所有触控按键、摇杆与滑屏增量，杜绝隐藏时残存持续开火或位移。
        /// </summary>
        public void ResetAllInputs()
        {
            if (_floatingJoystick != null) _floatingJoystick.ResetJoystick();
            if (_aimSwipeZone != null) _aimSwipeZone.ResetZone();
            if (_fireButton != null) _fireButton.ResetState();
            if (_swimButton != null) _swimButton.ResetState();
            if (_jumpButton != null) _jumpButton.ResetState();
            if (_bombButton != null) _bombButton.ResetState();
            if (_specialButton != null) _specialButton.ResetState();

            var player = PlayerController.LocalPlayer;
            if (player != null)
            {
                player.TouchMoveInput = Vector2.zero;
                player.TouchLookDelta = Vector2.zero;
                player.TouchFireHeld = false;
                player.TouchSwimHeld = false;
                player.TouchBombHeld = false;
                player.TouchBombTriggered = false;
                player.TouchJumpTriggered = false;
                player.TouchSpecialTriggered = false;
            }
        }

        private void LoadIconsIfNeeded()
        {
#if UNITY_EDITOR
            const string iconRoot = "Assets/_Project/Art/Sprites/Icons/";
            if (fireIcon == null) fireIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Weapons/weapon_shooter.png");
            if (swimIcon == null) swimIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Rules/rule_swim.png");
            if (jumpIcon == null) jumpIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Rules/rule_climb.png");
            if (bombIcon == null) bombIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Subs/sub_bomb.png");
            if (specialIcon == null) specialIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Specials/special_slam.png");
            if (joystickKnobIcon == null) joystickKnobIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(iconRoot + "Glyphs/glyph_gamepad.png");
#endif
        }

        private void Update()
        {
            if (!IsShown) return;

            var player = PlayerController.LocalPlayer;
            if (player == null) return;

            // 在自定义键位编辑模式下，跳过玩法输入读取，并清空本帧累计的滑屏增量
            if (_customizer != null && _customizer.IsEditing)
            {
                if (_aimSwipeZone != null) _aimSwipeZone.ConsumeLookDelta();
                if (_fireButton != null) _fireButton.ConsumeLookDelta();
                if (_bombButton != null) _bombButton.ConsumeLookDelta();
                player.TouchLookDelta = Vector2.zero;
                return;
            }

            // 1. 移动摇杆输入
            player.TouchMoveInput = _floatingJoystick != null ? _floatingJoystick.Value : Vector2.zero;

            // 2. 右半屏滑屏 / 开火键拖拽 / 手雷键拖拽 → 本帧视角增量（像素）
            Vector2 lookDelta = Vector2.zero;
            if (_aimSwipeZone != null) lookDelta += _aimSwipeZone.ConsumeLookDelta();
            if (_fireButton != null) lookDelta += _fireButton.ConsumeLookDelta();
            if (_bombButton != null) lookDelta += _bombButton.ConsumeLookDelta();
            player.TouchLookDelta = lookDelta;

            // 3. 开火与潜行（长按持续驱动）
            player.TouchFireHeld = _fireButton != null && _fireButton.IsHeld;
            player.TouchSwimHeld = _swimButton != null && _swimButton.IsHeld;

            // 4. 跳跃点按
            if (_jumpButton != null && _jumpButton.ConsumeTap())
            {
                player.TouchJumpTriggered = true;
            }

            // 5. 手雷（短按秒投，长按瞄准后松手投掷）
            if (_bombButton != null)
            {
                if (_bombButton.ConsumeTap())
                {
                    player.TouchBombTriggered = true;
                }
                player.TouchBombHeld = _bombButton.IsAimingHeld;
            }

            // 6. 大招状态同步与触发
            if (_specialButton != null)
            {
                var weapon = player.Weapon;
                bool isSpecialReady = weapon != null && weapon.IsSpecialReady;
                float specialCharge = weapon != null ? weapon.SpecialCharge01 : 0f;
                _specialButton.UpdateSpecialState(isSpecialReady, specialCharge);

                if (_specialButton.ConsumeTap())
                {
                    player.TouchSpecialTriggered = true;
                }
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

            // 刘海屏 / 挖孔屏 / 底部横条 Safe Area 容器
            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(root, false);
            _safeArea = safeGo.GetComponent<RectTransform>();
            _safeArea.anchorMin = Vector2.zero;
            _safeArea.anchorMax = Vector2.one;
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
            safeGo.AddComponent<SafeAreaFitter>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem_Mobile");
                es.transform.SetParent(transform, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            EnsureCircleSprites();

            // 1. 右半屏滑屏瞄准感应区（置于最底层，不遮挡按钮）
            _aimSwipeZone = RightAimSwipeZone.Create(_safeArea);

            // 2. 左侧半屏动态浮动摇杆
            _floatingJoystick = DynamicFloatingJoystick.Create(
                _safeArea,
                moveStickDefaultPos,
                moveStickRadius,
                moveDeadZone,
                _circleFillSprite,
                _circleRingSprite,
                joystickKnobIcon);

            // 3. 右下角扇形人体工学操作按键群（参考和平精英/蛋仔派对）
            _fireButton = MobileActionButton.Create(
                _safeArea, "FireButton", "射击",
                new Vector2(-160f, 160f), 148f,
                new Color(1f, 0.44f, 0.08f, 0.92f), // 鲜明橙墨色
                fireIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.HoldWithAimDrag);

            _swimButton = MobileActionButton.Create(
                _safeArea, "SwimButton", "潜行",
                new Vector2(-310f, 110f), 118f,
                new Color(0.12f, 0.52f, 0.98f, 0.88f), // 亮蓝墨水色
                swimIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Hold);

            _jumpButton = MobileActionButton.Create(
                _safeArea, "JumpButton", "跳跃",
                new Vector2(-110f, 310f), 110f,
                new Color(0.18f, 0.85f, 0.48f, 0.88f), // 翡翠薄荷绿
                jumpIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Tap);

            _bombButton = MobileActionButton.Create(
                _safeArea, "BombButton", "手雷",
                new Vector2(-245f, 260f), 112f,
                new Color(0.55f, 0.28f, 0.95f, 0.88f), // 荧光紫
                bombIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.BombTapOrHoldAim);

            _specialButton = MobileActionButton.Create(
                _safeArea, "SpecialButton", "大招",
                new Vector2(-370f, 340f), 124f,
                new Color(1f, 0.76f, 0.12f, 0.90f), // 炫金墨水色
                specialIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Special);

            // 4. 初始化按键自定义布局管理器，注册所有支持拖动与缩放的按键
            _customizer = MobileLayoutCustomizer.Create(this, _safeArea, _circleRingSprite);
            _customizer.RegisterItem("MoveStick", "移动摇杆", _floatingJoystick.VisualContainer, _floatingJoystick.CanvasGroup, moveStickDefaultPos, 1.0f, 0.85f);
            _customizer.RegisterItem("Fire", "射击大键", _fireButton.RectTransform, _fireButton.CanvasGroup, new Vector2(-160f, 160f), 1.0f, 0.92f);
            _customizer.RegisterItem("Swim", "潜行按键", _swimButton.RectTransform, _swimButton.CanvasGroup, new Vector2(-310f, 110f), 1.0f, 0.88f);
            _customizer.RegisterItem("Jump", "跳跃按键", _jumpButton.RectTransform, _jumpButton.CanvasGroup, new Vector2(-110f, 310f), 1.0f, 0.88f);
            _customizer.RegisterItem("Bomb", "手雷技能", _bombButton.RectTransform, _bombButton.CanvasGroup, new Vector2(-245f, 260f), 1.0f, 0.88f);
            _customizer.RegisterItem("Special", "大招技能", _specialButton.RectTransform, _specialButton.CanvasGroup, new Vector2(-370f, 340f), 1.0f, 0.90f);

            // 读取已持久化的自定义按键配置（若玩家此前调整过）
            var savedConfig = MobileLayoutStorage.Load();
            if (savedConfig != null)
            {
                _customizer.ApplyConfig(savedConfig);
            }
            _customizer.OnClosed += HandleCustomizerClosed;

            // 5. 屏幕左上角悬浮【⚙️ 键位】快速编辑入口
            CreateSettingsEntryButton(_safeArea);

            // 初始构建完成后，若处于 Ready 阶段，默认先行隐藏，防止遮挡首屏按钮
            var initialMatch = MatchManager.Instance != null ? MatchManager.Instance : FindAnyObjectByType<MatchManager>();
            if (initialMatch != null && initialMatch.CurrentPhase == MatchManager.Phase.Ready)
            {
                SetVisible(false);
            }
        }

        private void CreateSettingsEntryButton(RectTransform safeArea)
        {
            var go = new GameObject("SettingsLayoutBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(safeArea, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); // 左上角
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(25f, -25f);
            rt.sizeDelta = new Vector2(96f, 38f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.08f, 0.10f, 0.15f, 0.78f);

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => OpenLayoutCustomizer());

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.text = "⚙️ 键位";
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(1f, 0.84f, 0.28f, 0.95f);
            txt.raycastTarget = false;
        }

        private static void EnsureCircleSprites()
        {
            if (_circleFillSprite != null && _circleRingSprite != null) return;

            const int size = 128;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f - 2f;

            var fillTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            fillTex.wrapMode = TextureWrapMode.Clamp;
            fillTex.filterMode = FilterMode.Bilinear;

            var ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            ringTex.wrapMode = TextureWrapMode.Clamp;
            ringTex.filterMode = FilterMode.Bilinear;

            var fillColors = new Color32[size * size];
            var ringColors = new Color32[size * size];

            float ringWidth = 8f;
            float innerR = radius - ringWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                    // 1.5 像素抗锯齿平滑边缘
                    float outerAlpha = Mathf.Clamp01((radius - d) / 1.5f);
                    byte aFill = (byte)(outerAlpha * 255f);
                    fillColors[idx] = new Color32(255, 255, 255, aFill);

                    float innerAlpha = Mathf.Clamp01((d - innerR) / 1.5f);
                    float ringAlpha = outerAlpha * innerAlpha;
                    byte aRing = (byte)(ringAlpha * 255f);
                    ringColors[idx] = new Color32(255, 255, 255, aRing);
                }
            }

            fillTex.SetPixels32(fillColors);
            fillTex.Apply(false, false); // 保持 CPU 可读，支持 alphaHitTestMinimumThreshold 纯圆判定

            ringTex.SetPixels32(ringColors);
            ringTex.Apply(false, false);

            _circleFillSprite = Sprite.Create(fillTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _circleRingSprite = Sprite.Create(ringTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
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

    /// <summary>
    /// 左半屏动态浮动摇杆：
    /// 包含左半屏触控感应区与动态居中底座。手指落在左半屏任意位置，摇杆底座瞬间出现在指下并平滑跟踪拖拽。
    /// </summary>
    public class DynamicFloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }
        public RectTransform VisualContainer => _container;
        public CanvasGroup CanvasGroup => _canvasGroup;

        private RectTransform _safeAreaRect;
        private RectTransform _container;
        private RectTransform _knob;
        private CanvasGroup _canvasGroup;

        private float _radius;
        private float _deadZone;
        private Vector2 _defaultPos;
        private Vector2 _centerPos;
        private int _activePointerId = -999;

        public static DynamicFloatingJoystick Create(
            RectTransform safeArea,
            Vector2 defaultPos,
            float radius,
            float deadZone,
            Sprite fillSprite,
            Sprite ringSprite,
            Sprite knobIcon)
        {
            // 1. 左下半屏专属触控感应区（透明图层，专门拦截左半屏手势）
            var touchZoneGo = new GameObject("MoveTouchZone", typeof(RectTransform), typeof(Image));
            touchZoneGo.transform.SetParent(safeArea, false);
            touchZoneGo.transform.SetAsFirstSibling();

            var zoneRt = touchZoneGo.GetComponent<RectTransform>();
            zoneRt.anchorMin = new Vector2(0f, 0f);
            zoneRt.anchorMax = new Vector2(0.5f, 0.78f);
            zoneRt.offsetMin = Vector2.zero;
            zoneRt.offsetMax = Vector2.zero;

            var zoneImg = touchZoneGo.GetComponent<Image>();
            zoneImg.color = Color.clear;
            zoneImg.raycastTarget = true;

            var joystick = touchZoneGo.AddComponent<DynamicFloatingJoystick>();
            joystick._safeAreaRect = safeArea;
            joystick._radius = radius;
            joystick._deadZone = deadZone;
            joystick._defaultPos = defaultPos;

            // 2. 摇杆视觉容器（底座与把手）
            var containerGo = new GameObject("JoystickVisualContainer", typeof(RectTransform), typeof(CanvasGroup));
            containerGo.transform.SetParent(safeArea, false);
            var containerRt = containerGo.GetComponent<RectTransform>();
            containerRt.anchorMin = Vector2.zero;
            containerRt.anchorMax = Vector2.zero;
            containerRt.pivot = new Vector2(0.5f, 0.5f);
            containerRt.anchoredPosition = defaultPos;
            containerRt.sizeDelta = new Vector2(radius * 2f, radius * 2f);

            var group = containerGo.GetComponent<CanvasGroup>();
            group.alpha = 0.45f;
            group.blocksRaycasts = false;
            joystick._container = containerRt;
            joystick._canvasGroup = group;

            // 底盘暗黑背景
            var bgGo = new GameObject("BaseBg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(containerRt, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = fillSprite;
            bgImg.color = new Color(0.08f, 0.10f, 0.14f, 0.55f);
            bgImg.raycastTarget = false;

            // 底盘发光外环
            var ringGo = new GameObject("BaseRing", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(containerRt, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = new Color(1f, 1f, 1f, 0.40f);
            ringImg.raycastTarget = false;

            // 中心小把手 (Knob)
            var knobGo = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            knobGo.transform.SetParent(containerRt, false);
            var knobRt = knobGo.GetComponent<RectTransform>();
            knobRt.anchorMin = new Vector2(0.5f, 0.5f);
            knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.anchoredPosition = Vector2.zero;
            knobRt.sizeDelta = new Vector2(radius * 0.85f, radius * 0.85f);

            var knobImg = knobGo.GetComponent<Image>();
            knobImg.sprite = fillSprite;
            knobImg.color = new Color(0.95f, 0.98f, 1.0f, 0.85f);
            knobImg.raycastTarget = false;

            // 把手内部装饰图标
            if (knobIcon != null)
            {
                var iconGo = new GameObject("KnobIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(knobRt, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.2f, 0.2f);
                iconRt.anchorMax = new Vector2(0.8f, 0.8f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = knobIcon;
                iconImg.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);
                iconImg.raycastTarget = false;
            }

            joystick._knob = knobRt;
            return joystick;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_activePointerId != -999) return;
            _activePointerId = eventData.pointerId;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _safeAreaRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

            _centerPos = localPoint;
            _container.anchoredPosition = localPoint;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;

            if (_canvasGroup != null) _canvasGroup.alpha = 0.92f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _safeAreaRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

            Vector2 delta = localPoint - _centerPos;
            float dist = delta.magnitude;
            Vector2 dir = dist > 0.001f ? (delta / dist) : Vector2.zero;

            if (dist < _deadZone)
            {
                Value = Vector2.zero;
            }
            else
            {
                float norm = Mathf.Clamp01((dist - _deadZone) / (_radius - _deadZone));
                Value = dir * norm;
            }

            float knobDist = Mathf.Min(dist, _radius * 0.72f);
            if (_knob != null) _knob.anchoredPosition = dir * knobDist;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            ResetJoystick();
        }

        public void ResetJoystick()
        {
            _activePointerId = -999;
            Value = Vector2.zero;

            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            if (_container != null) _container.anchoredPosition = _defaultPos;
            if (_canvasGroup != null) _canvasGroup.alpha = 0.45f;
        }

        private void OnDisable()
        {
            ResetJoystick();
        }
    }

    /// <summary>
    /// 右半屏滑屏转视角感应区（P9）：
    /// 置于按键下层。手指在右侧非按键空白区域滑动时，每帧累计 PointerEventData.delta（像素），
    /// 由 PlayerController 乘灵敏度后驱动第三人称相机 Yaw/Pitch，手感对齐和平精英。
    /// </summary>
    public class RightAimSwipeZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public bool HasAimInput { get; private set; }
        public Vector2 AimValue { get; private set; }

        private Vector2 _lookDelta;
        private int _activePointerId = -999;

        /// <summary>取出本帧累计的滑屏像素增量并清零（x=右, y=上）。</summary>
        public Vector2 ConsumeLookDelta()
        {
            Vector2 delta = _lookDelta;
            _lookDelta = Vector2.zero;
            return delta;
        }

        public static RightAimSwipeZone Create(RectTransform safeArea)
        {
            var go = new GameObject("RightAimSwipeZone", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(safeArea, false);
            go.transform.SetAsFirstSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(1f, 0.78f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;

            return go.AddComponent<RightAimSwipeZone>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_activePointerId != -999) return;
            _activePointerId = eventData.pointerId;
            _lookDelta = Vector2.zero;
            HasAimInput = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            _lookDelta += eventData.delta;
            AimValue = _lookDelta.sqrMagnitude > 1f ? _lookDelta.normalized : Vector2.zero;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

            ResetZone();
        }

        public void ResetZone()
        {
            _activePointerId = -999;
            HasAimInput = false;
            AimValue = Vector2.zero;
            _lookDelta = Vector2.zero;
        }

        private void OnDisable()
        {
            ResetZone();
        }
    }

    public enum MobileButtonMode
    {
        Hold,
        Tap,
        HoldWithAimDrag,
        BombTapOrHoldAim,
        Special
    }

    /// <summary>
    /// 移动端多功能纯圆操作按钮：
    /// 1. 严格采用纯圆形状与发光环；
    /// 2. 启用像素级 alphaHitTestMinimumThreshold = 0.1f，杜绝透明角落误触；
    /// 3. 支持缩放（0.92x）、高清图标、按住开火、滑拽瞄准、手雷短按秒投/长按瞄准、大招蓄能状态。
    /// </summary>
    public class MobileActionButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform RectTransform => _rectTransform;
        public CanvasGroup CanvasGroup => _canvasGroup;

        private MobileButtonMode _mode;
        private RectTransform _rectTransform;
        private Image _bgImage;
        private Image _ringImage;
        private Image _iconImage;
        private Text _labelText;
        private CanvasGroup _canvasGroup;

        private Color _baseColor;
        private bool _isHeld;
        private bool _tapped;
        private float _pointerDownTime;
        private bool _isAimingHeld;
        private Vector2 _lookDelta;

        public bool IsHeld => _isHeld;
        public bool IsAimingHeld => _isAimingHeld;
        public bool HasDragAim { get; private set; }
        public Vector2 DragAimValue { get; private set; }

        /// <summary>取出本帧拖拽产生的视角像素增量并清零。</summary>
        public Vector2 ConsumeLookDelta()
        {
            Vector2 delta = _lookDelta;
            _lookDelta = Vector2.zero;
            return delta;
        }

        public bool ConsumeTap()
        {
            if (!_tapped) return false;
            _tapped = false;
            return true;
        }

        public static MobileActionButton Create(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPos,
            float size,
            Color mainColor,
            Sprite icon,
            Sprite fillSprite,
            Sprite ringSprite,
            MobileButtonMode mode)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f); // 右下角锚点
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(size, size);

            var group = go.GetComponent<CanvasGroup>();

            // 1. 底盘背景（纯圆抗锯齿，带 0.1f 像素级透明点击穿透）
            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = fillSprite;
            bgImg.color = mainColor;
            bgImg.raycastTarget = true;
            bgImg.alphaHitTestMinimumThreshold = 0.1f; // 严格限制在圆形内部点击

            // 2. 发光外环/边框
            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(go.transform, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = new Color(1f, 1f, 1f, 0.70f);
            ringImg.raycastTarget = false;

            // 3. 高清图标
            Image iconImg = null;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.20f, 0.28f);
                iconRt.anchorMax = new Vector2(0.80f, 0.88f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;

                iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            // 4. 文字标题
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.05f, 0.06f);
            textRt.anchorMax = new Vector2(0.95f, 0.36f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.text = label;
            txt.fontSize = Mathf.RoundToInt(size * 0.18f);
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.raycastTarget = false;

            var btn = go.AddComponent<MobileActionButton>();
            btn._mode = mode;
            btn._rectTransform = rt;
            btn._bgImage = bgImg;
            btn._ringImage = ringImg;
            btn._iconImage = iconImg;
            btn._labelText = txt;
            btn._canvasGroup = group;
            btn._baseColor = mainColor;

            return btn;
        }

        private void Update()
        {
            // 手雷键长按 0.22s 后即使手指不动也必须进入瞄准态（旧实现只在 OnDrag 里判定，
            // 导致「按住不动等瞄准」永远无法呼出抛物线落点）。
            if (_mode == MobileButtonMode.BombTapOrHoldAim &&
                _isHeld &&
                !_isAimingHeld &&
                Time.unscaledTime - _pointerDownTime > 0.22f)
            {
                _isAimingHeld = true;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isHeld = true;
            _pointerDownTime = Time.unscaledTime;
            _lookDelta = Vector2.zero;

            _rectTransform.localScale = Vector3.one * 0.92f;
            _bgImage.color = _baseColor * 1.15f;

            if (_mode == MobileButtonMode.Tap || _mode == MobileButtonMode.Special)
            {
                _tapped = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_mode == MobileButtonMode.HoldWithAimDrag)
            {
                _lookDelta += eventData.delta;
                HasDragAim = _lookDelta.sqrMagnitude > 400f;
                DragAimValue = HasDragAim ? _lookDelta.normalized : Vector2.zero;
            }
            else if (_mode == MobileButtonMode.BombTapOrHoldAim)
            {
                if (!_isAimingHeld && Time.unscaledTime - _pointerDownTime > 0.22f)
                {
                    _isAimingHeld = true;
                }

                if (_isAimingHeld)
                {
                    _lookDelta += eventData.delta;
                }
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isHeld = false;
            _lookDelta = Vector2.zero;
            _rectTransform.localScale = Vector3.one;
            _bgImage.color = _baseColor;

            if (_mode == MobileButtonMode.HoldWithAimDrag)
            {
                HasDragAim = false;
                DragAimValue = Vector2.zero;
            }
            else if (_mode == MobileButtonMode.BombTapOrHoldAim)
            {
                float duration = Time.unscaledTime - _pointerDownTime;
                if (_isAimingHeld)
                {
                    _isAimingHeld = false;
                    _tapped = true;
                }
                else if (duration <= 0.22f)
                {
                    _tapped = true;
                }
            }
        }

        public void ResetState()
        {
            _isHeld = false;
            _tapped = false;
            _isAimingHeld = false;
            HasDragAim = false;
            _lookDelta = Vector2.zero;
            DragAimValue = Vector2.zero;
            if (_rectTransform != null) _rectTransform.localScale = Vector3.one;
            if (_bgImage != null) _bgImage.color = _baseColor;
        }

        private void OnDisable()
        {
            ResetState();
        }

        /// <summary>同步大招充能状态视觉反馈。</summary>
        public void UpdateSpecialState(bool isReady, float charge01)
        {
            if (_mode != MobileButtonMode.Special) return;

            if (isReady)
            {
                _canvasGroup.alpha = 1.0f;
                _bgImage.color = _baseColor;
                _ringImage.color = new Color(1f, 1f, 1f, 0.95f);

                float breath = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5.5f);
                if (!_isHeld) _rectTransform.localScale = Vector3.one * breath;
                if (_labelText != null) _labelText.text = "大招就绪!";
            }
            else
            {
                _canvasGroup.alpha = 0.55f;
                _bgImage.color = new Color(0.4f, 0.38f, 0.32f, 0.85f);
                _ringImage.color = new Color(1f, 1f, 1f, 0.30f);
                if (!_isHeld) _rectTransform.localScale = Vector3.one;
                if (_labelText != null) _labelText.text = $"{Mathf.FloorToInt(charge01 * 100f)}%";
            }
        }
    }
}
