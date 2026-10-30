using System.Collections.Generic;
using P1.Core;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端触控交互系统（漫画风重绘 P10）：
    /// 1. 左侧：半屏动态浮动摇杆（Floating Joystick），支持半屏唤醒与平滑跟踪；
    /// 2. 右侧：漫画风人体工学扇形按键群（实心队伍色 + 6px 墨线边框 + 硬阴影压印效果）；
    /// 3. 右半屏：滑屏转视角感应区（每帧触摸增量驱动相机 Yaw/Pitch）；
    /// 4. 图标库统一走 UIIconLibrary，缺失时安全隐藏并由文字兜底，绝无白方块；
    /// 5. 尺寸与排布默认值统一暴露为 DefaultLayout，便于运行时与测试共享；
    /// 6. 支持 DPI 物理毫米自适应与自定义键位持久化；
    /// 7. 3/4 指方案提供左下角独立左开火键，并与 2/3/4 指预设、固定摇杆、辅助瞄准档位联动。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class MobileTouchUI : MonoBehaviour
    {
        /// <summary>
        /// 1920x1080 参考分辨率下的 5 个主操作按键标准排布表（两两间距均已验算 >= 半径和 - 1）。
        /// 格式：(id, label, 锚定右下角的 anchoredPosition, 直径尺寸)。
        /// </summary>
        public static readonly (string id, string label, Vector2 pos, float size)[] DefaultLayout = new[]
        {
            ("Fire",    "射击", new Vector2(-150f, 150f), 176f),
            ("Swim",    "潜行", new Vector2(-320f, 100f), 140f),
            ("Jump",    "跳跃", new Vector2(-108f, 320f), 132f),
            ("Bomb",    "手雷", new Vector2(-290f, 355f), 132f),
            ("Special", "大招", new Vector2(-430f, 245f), 148f)
        };

        [Header("调试与显示")]
        [Tooltip("在 Unity 编辑器中强制显示触控层，方便鼠标模拟与调试。桌面/编辑器默认关闭，避免全屏感应区抢鼠标视角")]
        [SerializeField] private bool showTouchUiInEditor = false;

        [Tooltip("旧版字段，仅保留场景序列化兼容；P9 起不再参与显示判断，请使用 showTouchUiInEditor")]
        [SerializeField] private bool forceShowInEditor = true;

        [Header("左侧动态浮动摇杆参数")]
        [SerializeField] private float moveStickRadius = 170f;
        [SerializeField] private float moveDeadZone = 8f;
        [SerializeField] private Vector2 moveStickDefaultPos = new Vector2(200f, 200f);

        [Header("按键图标资产引用（为空时自动从 UIIconLibrary 加载）")]
        [SerializeField] private Sprite fireIcon;
        [SerializeField] private Sprite swimIcon;
        [SerializeField] private Sprite jumpIcon;
        [SerializeField] private Sprite bombIcon;
        [SerializeField] private Sprite specialIcon;
        [SerializeField] private Sprite joystickKnobIcon;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _safeArea;
        private DynamicFloatingJoystick _floatingJoystick;
        private RightAimSwipeZone _aimSwipeZone;
        private MobileLayoutCustomizer _customizer;

        private MobileActionButton _fireButton;
        private MobileActionButton _swimButton;
        private MobileActionButton _jumpButton;
        private MobileActionButton _bombButton;
        private MobileActionButton _specialButton;
        private MobileActionButton _leftFireButton;
        private Button _settingsEntryButton;

        private MatchManager _match;
        private bool _subscribedMatch;
        private bool _subscribedSettings;
        private bool _dpiScaleApplied;

        private static Sprite _circleFillSprite;
        private static Sprite _circleRingSprite;

        public bool IsShown => _canvas != null && _canvas.gameObject.activeInHierarchy;
        public DynamicFloatingJoystick FloatingJoystick => _floatingJoystick;
        public MobileLayoutCustomizer Customizer => _customizer;
        public float CanvasScaleFactor => _canvas != null ? _canvas.scaleFactor : 1.0f;

        /// <summary>左开火键是否正在显示（3/4 指方案开启，2 指方案关闭）。</summary>
        public bool IsLeftFireVisible => _leftFireButton != null && _leftFireButton.gameObject.activeSelf;

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

        /// <summary>按当前设备 DPI 生成指定方案（位置与缩放已乘 DPI 系数）。</summary>
        public MobileLayoutConfig BuildPresetConfig(MobileLayoutPreset preset)
        {
            float scale = MobileUiMetrics.ComputeScale(Screen.dpi, CanvasScaleFactor);
            return MobileLayoutPresets.Build(preset, scale);
        }

        /// <summary>删除自定义布局后，按当前设备 DPI 重新应用默认排布（供「恢复默认」使用）。</summary>
        public void ApplyDefaultDeviceLayout()
        {
            MobileUiMetrics.ApplyToTouchUI(this);
        }

        /// <summary>显示/隐藏左开火键；隐藏时立即清理按压状态，避免残留开火。</summary>
        public void SetLeftFireVisible(bool visible)
        {
            if (_leftFireButton == null) return;

            if (!visible)
            {
                _leftFireButton.ResetState();
            }

            if (_leftFireButton.gameObject.activeSelf != visible)
            {
                _leftFireButton.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 进入/退出按键编辑态：编辑时暂停摇杆触摸、隐藏左上角「操作」入口，
        /// 并联动暂停按钮显隐，避免误触把编辑层盖掉。
        /// </summary>
        public void SetLayoutEditing(bool editing)
        {
            if (_floatingJoystick != null)
            {
                _floatingJoystick.InputEnabled = !editing;
            }

            if (_settingsEntryButton != null)
            {
                _settingsEntryButton.gameObject.SetActive(!editing);
            }

            P1.UI.Menus.PauseMenuOverlay.Instance?.RefreshMobileButtonVisibility();
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
            SubscribeSettings();
            ApplyAuxSettings();
            UpdateVisibilityForCurrentPhase();
        }

        private void OnDisable()
        {
            IsTouchSimulationActive = false;
            UnsubscribeMatch();
            UnsubscribeSettings();
            ResetAllInputs();
        }

        private void OnDestroy()
        {
            IsTouchSimulationActive = false;
            UnsubscribeMatch();
            UnsubscribeSettings();
            if (_customizer != null)
            {
                _customizer.OnClosed -= HandleCustomizerClosed;
            }
        }

        private void SubscribeSettings()
        {
            if (_subscribedSettings) return;
            GameSettingsService.SettingsChanged += HandleSettingsChanged;
            _subscribedSettings = true;
        }

        private void UnsubscribeSettings()
        {
            if (!_subscribedSettings) return;
            GameSettingsService.SettingsChanged -= HandleSettingsChanged;
            _subscribedSettings = false;
        }

        private void HandleSettingsChanged()
        {
            ApplyAuxSettings();
        }

        private void ApplyAuxSettings()
        {
            if (_floatingJoystick != null)
            {
                _floatingJoystick.FixedBase = GameSettingsService.FixedJoystick;
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
            if (_customizer != null && _customizer.IsEditing)
                return;

            switch (phase)
            {
                case MatchManager.Phase.Ready:
                    SetVisible(false);
                    break;
                case MatchManager.Phase.Countdown:
                case MatchManager.Phase.Playing:
                    SetVisible(ShouldShowTouchUi());
                    break;
                case MatchManager.Phase.Finished:
                case MatchManager.Phase.Result:
                    SetVisible(false);
                    break;
            }
        }

        private void HandleCustomizerClosed()
        {
            UpdateVisibilityForCurrentPhase();
            P1.UI.Menus.PauseMenuOverlay.Instance?.RefreshMobileButtonVisibility();
        }

        public void UpdateVisibilityForCurrentPhase()
        {
            if (_match != null)
            {
                HandlePhaseChanged(_match.CurrentPhase);
            }
            else
            {
                SetVisible(ShouldShowTouchUi());
            }
        }

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

        public void EnsureBuilt()
        {
            if (_canvas != null) return;
            LoadIconsIfNeeded();
            Build();
        }

        public void ResetAllInputs()
        {
            if (_floatingJoystick != null) _floatingJoystick.ResetJoystick();
            if (_aimSwipeZone != null) _aimSwipeZone.ResetZone();
            if (_fireButton != null) _fireButton.ResetState();
            if (_swimButton != null) _swimButton.ResetState();
            if (_jumpButton != null) _jumpButton.ResetState();
            if (_bombButton != null) _bombButton.ResetState();
            if (_specialButton != null) _specialButton.ResetState();
            if (_leftFireButton != null) _leftFireButton.ResetState();

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
            if (fireIcon == null) fireIcon = UIIconLibrary.Get("weapon_shooter");
            if (swimIcon == null) swimIcon = UIIconLibrary.Get("rule_swim");
            if (jumpIcon == null) jumpIcon = UIIconLibrary.Get("rule_climb");
            if (bombIcon == null) bombIcon = UIIconLibrary.Get("sub_bomb");
            if (specialIcon == null) specialIcon = UIIconLibrary.Get("special_slam");
            if (joystickKnobIcon == null) joystickKnobIcon = UIIconLibrary.Get("glyph_gamepad");

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
            // 首次可见时补一次 DPI 适配：Build 发生在 Awake，ScreenSpaceOverlay 画布此时
            // 还没完成首次布局（scaleFactor 约为 1），直接算会把按键放大过头。
            if (!_dpiScaleApplied && IsShown)
            {
                _dpiScaleApplied = true;
                MobileUiMetrics.ApplyToTouchUI(this);
            }

            if (!IsShown) return;

            var player = PlayerController.LocalPlayer;
            if (player == null) return;

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

            // 2. 右半屏滑屏 / 开火键拖拽 / 手雷键拖拽 -> 本帧视角增量（像素）
            Vector2 lookDelta = Vector2.zero;
            if (_aimSwipeZone != null) lookDelta += _aimSwipeZone.ConsumeLookDelta();
            if (_fireButton != null) lookDelta += _fireButton.ConsumeLookDelta();
            if (_bombButton != null) lookDelta += _bombButton.ConsumeLookDelta();
            player.TouchLookDelta = lookDelta;

            // 3. 开火与潜行（左开火键是 3/4 指方案的独立开火通道，任一按住即开火）
            player.TouchFireHeld = (_fireButton != null && _fireButton.IsHeld) ||
                                   (_leftFireButton != null && _leftFireButton.gameObject.activeSelf && _leftFireButton.IsHeld);
            player.TouchSwimHeld = _swimButton != null && _swimButton.IsHeld;

            // 4. 跳跃点按
            if (_jumpButton != null && _jumpButton.ConsumeTap())
            {
                player.TouchJumpTriggered = true;
            }

            // 5. 手雷
            if (_bombButton != null)
            {
                if (_bombButton.ConsumeTap())
                {
                    player.TouchBombTriggered = true;
                }
                player.TouchBombHeld = _bombButton.IsAimingHeld;
            }

            // 6. 大招
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

            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var root = canvasGo.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // Safe Area 容器
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

            // 1. 右半屏滑屏瞄准感应区（置于最底层）
            _aimSwipeZone = RightAimSwipeZone.Create(_safeArea);

            // 2. 左侧半屏动态浮动摇杆（半径 170）
            _floatingJoystick = DynamicFloatingJoystick.Create(
                _safeArea,
                moveStickDefaultPos,
                moveStickRadius,
                moveDeadZone,
                _circleFillSprite,
                _circleRingSprite,
                joystickKnobIcon);
            _floatingJoystick.FixedBase = GameSettingsService.FixedJoystick;

            // 3. 右下角漫画风人体工学操作按键群
            var fireCfg = DefaultLayout[0];
            var swimCfg = DefaultLayout[1];
            var jumpCfg = DefaultLayout[2];
            var bombCfg = DefaultLayout[3];
            var specialCfg = DefaultLayout[4];

            _fireButton = MobileActionButton.Create(
                _safeArea, "FireButton", fireCfg.label,
                fireCfg.pos, fireCfg.size,
                new Color(1f, 0.44f, 0.08f, 1f), // 鲜明橙墨色
                fireIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.HoldWithAimDrag);

            _swimButton = MobileActionButton.Create(
                _safeArea, "SwimButton", swimCfg.label,
                swimCfg.pos, swimCfg.size,
                new Color(0.12f, 0.52f, 0.98f, 1f), // 亮蓝墨水色
                swimIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Hold);

            _jumpButton = MobileActionButton.Create(
                _safeArea, "JumpButton", jumpCfg.label,
                jumpCfg.pos, jumpCfg.size,
                new Color(0.18f, 0.85f, 0.48f, 1f), // 翡翠薄荷绿
                jumpIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Tap);

            _bombButton = MobileActionButton.Create(
                _safeArea, "BombButton", bombCfg.label,
                bombCfg.pos, bombCfg.size,
                new Color(0.58f, 0.28f, 0.95f, 1f), // 荧光紫
                bombIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.BombTapOrHoldAim);

            _specialButton = MobileActionButton.Create(
                _safeArea, "SpecialButton", specialCfg.label,
                specialCfg.pos, specialCfg.size,
                new Color(1f, 0.76f, 0.12f, 1f), // 炫金墨水色
                specialIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Special);

            // 3b. 左开火键（3/4 指方案专用，2 指方案默认隐藏；左下角锚定，便于左食指开火）
            _leftFireButton = MobileActionButton.Create(
                _safeArea, "LeftFireButton", "射击",
                MobileLayoutPresets.ThreeFingerLeftFirePos, MobileLayoutPresets.LeftFireSize,
                new Color(1f, 0.44f, 0.08f, 1f),
                fireIcon, _circleFillSprite, _circleRingSprite,
                MobileButtonMode.Hold, new Vector2(0f, 0f));
            _leftFireButton.gameObject.SetActive(false);

            // 4. 初始化按键自定义布局管理器
            _customizer = MobileLayoutCustomizer.Create(this, _safeArea, _circleRingSprite);
            _customizer.RegisterItem("MoveStick", "移动摇杆", _floatingJoystick.VisualContainer, _floatingJoystick.CanvasGroup, moveStickDefaultPos, 1.0f, MobileLayoutPresets.DefaultMoveStickAlpha);
            _customizer.RegisterItem(fireCfg.id, "射击大键", _fireButton.RectTransform, _fireButton.CanvasGroup, fireCfg.pos, 1.0f, 1.0f);
            _customizer.RegisterItem(swimCfg.id, "潜行按键", _swimButton.RectTransform, _swimButton.CanvasGroup, swimCfg.pos, 1.0f, 1.0f);
            _customizer.RegisterItem(jumpCfg.id, "跳跃按键", _jumpButton.RectTransform, _jumpButton.CanvasGroup, jumpCfg.pos, 1.0f, 1.0f);
            _customizer.RegisterItem(bombCfg.id, "手雷技能", _bombButton.RectTransform, _bombButton.CanvasGroup, bombCfg.pos, 1.0f, 1.0f);
            _customizer.RegisterItem(specialCfg.id, "大招技能", _specialButton.RectTransform, _specialButton.CanvasGroup, specialCfg.pos, 1.0f, 1.0f);
            _customizer.RegisterItem("LeftFire", "左开火键", _leftFireButton.RectTransform, _leftFireButton.CanvasGroup,
                MobileLayoutPresets.ThreeFingerLeftFirePos, 1.0f, 1.0f);

            BindActionButtonItem(fireCfg.id, _fireButton);
            BindActionButtonItem(swimCfg.id, _swimButton);
            BindActionButtonItem(jumpCfg.id, _jumpButton);
            BindActionButtonItem(bombCfg.id, _bombButton);
            BindActionButtonItem(specialCfg.id, _specialButton);
            BindActionButtonItem("LeftFire", _leftFireButton);

            var stickItem = _customizer.GetItem("MoveStick");
            if (stickItem != null)
            {
                stickItem.applyPosition = _floatingJoystick.SetDefaultPosition;
                stickItem.applyAlpha = _floatingJoystick.SetBaseAlpha;
                stickItem.readAlpha = () => _floatingJoystick.BaseAlpha;
            }

            // 应用 DPI 物理毫米缩放适配
            MobileUiMetrics.ApplyToTouchUI(this);

            // 读取已持久化的自定义按键配置（若玩家此前调整过）
            var savedConfig = MobileLayoutStorage.Load();
            if (savedConfig != null)
            {
                _customizer.ApplyConfig(savedConfig);
            }
            else
            {
                SetLeftFireVisible(false);
            }

            // 旧存档没有 LeftFire 项时，左开火键仍按当前设备 DPI 初始化，
            // 避免玩家在旧自定义布局上打开它时突然比其他按键小一圈。
            if (savedConfig == null || savedConfig.Find("LeftFire") == null)
            {
                float deviceScale = MobileUiMetrics.ComputeScale(Screen.dpi, CanvasScaleFactor);
                ApplyButtonScale(_leftFireButton, MobileLayoutPresets.ThreeFingerLeftFirePos, deviceScale);
            }

            _customizer.OnClosed += HandleCustomizerClosed;

            // 5. 屏幕左上角悬浮【⚙️ 键位】快速编辑入口（132x64，字号 20）
            // 左上角最内侧 (24,-24) 的 88x88 留给 PauseMenuOverlay 的移动端暂停按钮，
            // 键位入口横排在暂停按钮右侧并与之垂直居中对齐，避免占用第二行遮挡视野。
            CreateSettingsEntryButton(_safeArea);

            var initialMatch = MatchManager.Instance != null ? MatchManager.Instance : FindAnyObjectByType<MatchManager>();
            if (initialMatch != null && initialMatch.CurrentPhase == MatchManager.Phase.Ready)
            {
                SetVisible(false);
            }
        }

        private void BindActionButtonItem(string id, MobileActionButton button)
        {
            // 由调用方保证 customizer 已创建；找不到 item 时静默跳过（独立测试/极端构造顺序）。
            var item = _customizer != null ? _customizer.GetItem(id) : null;
            if (item == null || button == null) return;

            item.applyScale = button.SetBaseScale;
            item.readScale = () => button.BaseScale;
            item.applyAlpha = button.SetBaseAlpha;
            item.readAlpha = () => button.BaseAlpha;
        }

        /// <summary>应用 DPI 缩放到触控层按键群（仅影响触控层）。</summary>
        public void ApplyDpiScale(float scale)
        {
            // 自定义布局优先：玩家保存过的位置与缩放不再被 DPI 适配覆盖
            if (MobileLayoutStorage.HasCustomLayout) return;

            // 位置必须与尺寸同步缩放：默认布局间距是按 1.0x 验算的，
            // 只放大尺寸会让 1.25x 时大招↔手雷重叠约 19px、射击↔潜行约 10px。
            // 基准永远取 DefaultLayout，保证本方法可重复调用（幂等）。
            ApplyButtonScale(_fireButton, DefaultLayout[0].pos, scale);
            ApplyButtonScale(_swimButton, DefaultLayout[1].pos, scale);
            ApplyButtonScale(_jumpButton, DefaultLayout[2].pos, scale);
            ApplyButtonScale(_bombButton, DefaultLayout[3].pos, scale);
            ApplyButtonScale(_specialButton, DefaultLayout[4].pos, scale);
            ApplyButtonScale(_leftFireButton, MobileLayoutPresets.ThreeFingerLeftFirePos, scale);
        }

        private static void ApplyButtonScale(MobileActionButton button, Vector2 basePos, float scale)
        {
            if (button == null) return;
            button.RectTransform.anchoredPosition = basePos * scale;
            button.SetBaseScale(scale);
        }

        private void CreateSettingsEntryButton(RectTransform safeArea)
        {
            // 漫画标签块：纸面 + 4px 墨边 + 硬阴影 + 齿轮图标
            var go = new GameObject("SettingsLayoutBtn", typeof(RectTransform), typeof(Button));
            go.transform.SetParent(safeArea, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); // 左上角
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            // 暂停按钮占 (24,-24) 的 88x88；键位入口贴在其右侧（间隔 12px），
            // 用 -36 让 64 高的小标签与 88 高的暂停按钮垂直中心对齐（-68）。
            rt.anchoredPosition = new Vector2(124f, -36f);
            rt.sizeDelta = new Vector2(132f, 64f);

            // 1. 硬阴影
            var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(go.transform, false);
            var shadowRt = shadowGo.GetComponent<RectTransform>();
            shadowRt.anchorMin = Vector2.zero;
            shadowRt.anchorMax = Vector2.one;
            shadowRt.anchoredPosition = new Vector2(4f, -4f);
            shadowRt.sizeDelta = Vector2.zero;
            var shadowImg = shadowGo.GetComponent<Image>();
            shadowImg.color = ComicStyle.Ink;
            shadowImg.raycastTarget = false;

            // 2. 纸面底盘 + 4px 墨边
            var shapeGo = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            shapeGo.transform.SetParent(go.transform, false);
            var shapeRt = shapeGo.GetComponent<RectTransform>();
            shapeRt.anchorMin = Vector2.zero;
            shapeRt.anchorMax = Vector2.one;
            shapeRt.offsetMin = Vector2.zero;
            shapeRt.offsetMax = Vector2.zero;
            var shape = shapeGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 6f;
            shape.BorderWidth = 4f;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = shape;
            btn.onClick.AddListener(OpenLayoutCustomizer);
            _settingsEntryButton = btn;

            // 3. 齿轮图标
            var gearSprite = UIIconLibrary.Get("glyph_gear");
            if (gearSprite != null)
            {
                var iconGo = new GameObject("GearIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(shapeGo.transform, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(10f, 0f);
                iconRt.sizeDelta = new Vector2(28f, 28f);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = gearSprite;
                iconImg.color = ComicStyle.Ink;
                iconImg.raycastTarget = false;
            }

            // 4. 文字标签（字号 20，墨线色，加粗）
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(shapeGo.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(gearSprite != null ? 36f : 0f, 0f);
            trt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<TextMeshProUGUI>();
            txt.text = gearSprite != null ? "操作" : "⚙️ 操作";
            txt.fontSize = 20;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = ComicStyle.Ink;
            txt.raycastTarget = false;

            var theme = UIThemeSO.Resolve(null);
            var font = theme.ResolveFont(false, "操作");
            if (font != null) txt.font = font;
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

            // 6px 墨线边框对应的纹理宽度（基于 128 像素图）
            float ringWidth = 8f;
            float innerR = radius - ringWidth;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

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
            fillTex.Apply(false, false);

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
    /// 采用漫画风格重绘：6px 墨线粗环 + 纸面/硬阴影把手。
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
        private float _baseAlpha = MobileLayoutPresets.DefaultMoveStickAlpha;
        private int _activePointerId = -999;

        /// <summary>固定摇杆：底座不跟手，以配置位置为中心；按下点离底座过远时不激活。</summary>
        public bool FixedBase { get; set; }

        /// <summary>布局编辑期间关闭摇杆触摸逻辑，避免拖拽按键时摇杆跟着动。</summary>
        public bool InputEnabled { get; set; } = true;

        public float BaseAlpha => _baseAlpha;

        public static DynamicFloatingJoystick Create(
            RectTransform safeArea,
            Vector2 defaultPos,
            float radius,
            float deadZone,
            Sprite fillSprite,
            Sprite ringSprite,
            Sprite knobIcon)
        {
            // 1. 左下半屏触控感应区（透明图层）
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
            group.alpha = MobileLayoutPresets.DefaultMoveStickAlpha;
            group.blocksRaycasts = false;
            joystick._baseAlpha = MobileLayoutPresets.DefaultMoveStickAlpha;
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
            bgImg.color = new Color(0.06f, 0.08f, 0.12f, 0.50f);
            bgImg.raycastTarget = false;

            // 漫画风 6px 墨线外环
            var ringGo = new GameObject("BaseRing", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(containerRt, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = ComicStyle.Ink;
            ringImg.raycastTarget = false;

            // 中心把手（Knob）：纸面底 + 6px 墨环
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
            knobImg.color = ComicStyle.Paper;
            knobImg.raycastTarget = false;

            // 把手 6px 墨线环
            var knobRingGo = new GameObject("KnobRing", typeof(RectTransform), typeof(Image));
            knobRingGo.transform.SetParent(knobRt, false);
            var knobRingRt = knobRingGo.GetComponent<RectTransform>();
            knobRingRt.anchorMin = Vector2.zero;
            knobRingRt.anchorMax = Vector2.one;
            knobRingRt.offsetMin = Vector2.zero;
            knobRingRt.offsetMax = Vector2.zero;
            var knobRingImg = knobRingGo.GetComponent<Image>();
            knobRingImg.sprite = ringSprite;
            knobRingImg.color = ComicStyle.Ink;
            knobRingImg.raycastTarget = false;

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
                iconImg.color = ComicStyle.Ink;
                iconImg.raycastTarget = false;
            }

            joystick._knob = knobRt;
            return joystick;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!InputEnabled || _activePointerId != -999) return;

            Vector2 anchored = ScreenToAnchored(eventData.position, eventData.pressEventCamera);

            if (FixedBase)
            {
                // 固定底座：以当前底座位置为中心；按在底座外太远则不抢这个指针，
                // 避免左手随便一碰左侧空白就满速移动。
                _centerPos = _container.anchoredPosition;
                if ((anchored - _centerPos).sqrMagnitude > (_radius * 1.4f) * (_radius * 1.4f))
                    return;
            }
            else
            {
                _centerPos = anchored;
                _container.anchoredPosition = anchored;
            }

            _activePointerId = eventData.pointerId;
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;

            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Clamp01(_baseAlpha + 0.4f);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!InputEnabled || eventData.pointerId != _activePointerId) return;

            Vector2 anchored = ScreenToAnchored(eventData.position, eventData.pressEventCamera);
            Vector2 delta = anchored - _centerPos;
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
            if (_canvasGroup != null) _canvasGroup.alpha = _baseAlpha;
        }

        /// <summary>把自定义位置同步为新的默认位置，ResetJoystick 不再弹回建层时的旧坐标。</summary>
        public void SetDefaultPosition(Vector2 pos)
        {
            _defaultPos = pos;
            if (_activePointerId == -999 && _container != null)
            {
                _container.anchoredPosition = pos;
            }
        }

        /// <summary>自定义待机透明度；按下态在此基础上 +0.4，保持“按下更明显”的反馈。</summary>
        public void SetBaseAlpha(float alpha)
        {
            _baseAlpha = Mathf.Clamp(alpha, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha);
            if (_canvasGroup != null && _activePointerId == -999)
            {
                _canvasGroup.alpha = _baseAlpha;
            }
        }

        /// <summary>
        /// 屏幕坐标 → SafeArea 左下角 anchored 空间。
        /// ScreenPointToLocalPointInRectangle 返回的是 pivot 相对坐标，而 VisualContainer 锚定在左下角，
        /// 直接赋值会让底座偏移半个屏幕；这里减去 rect.min 统一坐标系。
        /// </summary>
        private Vector2 ScreenToAnchored(Vector2 screenPos, Camera cam)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _safeAreaRect, screenPos, cam, out Vector2 localPoint))
            {
                return localPoint - _safeAreaRect.rect.min;
            }

            return _container != null ? _container.anchoredPosition : Vector2.zero;
        }

        private void OnDisable()
        {
            ResetJoystick();
        }
    }

    /// <summary>
    /// 右半屏滑屏转视角感应区（P9）：
    /// 置于按键下层。手指在右侧非按键空白区域滑动时，每帧累计 PointerEventData.delta（像素）。
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
    /// 移动端漫画风触控按钮（Comic P10）：
    /// 1. 实心语义色填充 + 6px 墨线粗边框（#1A1A1A）+ 硬阴影压印效果；
    /// 2. 按下时按钮位移并收缩阴影（压印感），松开瞬时复原（<=0.06s），禁止渐变与回弹；
    /// 3. 图标安全查找，缺失时隐藏并由中文标签兜底，绝无白块；
    /// 4. 支持像素级圆形命中测试，防止误触透明角落；
    /// 5. 支持开火按住、滑拽瞄准、手雷短按投掷/长按瞄准、大招就绪状态。
    /// </summary>
    public class MobileActionButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform RectTransform => _rectTransform;
        public CanvasGroup CanvasGroup => _canvasGroup;

        private MobileButtonMode _mode;
        private RectTransform _rectTransform;
        private RectTransform _contentContainer;
        private Image _shadowImage;
        private Image _bgImage;
        private Image _ringImage;
        private Image _iconImage;
        private TextMeshProUGUI _labelText;
        private CanvasGroup _canvasGroup;

        private Color _baseColor;
        private Vector2 _shadowOffset = new Vector2(5f, -5f);
        private bool _isHeld;
        private bool _tapped;
        private float _pointerDownTime;
        private bool _isAimingHeld;
        private Vector2 _lookDelta;
        private int _lastChargePercentInt = -1;
        private bool _lastReadyState;
        private bool _hasSpecialState;
        private float _baseScale = 1f;
        private float _baseAlpha = 1f;

        public bool IsHeld => _isHeld;
        public bool IsAimingHeld => _isAimingHeld;
        public bool HasDragAim { get; private set; }
        public Vector2 DragAimValue { get; private set; }
        public float BaseScale => _baseScale;
        public float BaseAlpha => _baseAlpha;

        /// <summary>自定义尺寸（相对基准的缩放）；按下/抬起/大招呼吸都在此基准上乘系数，不再重置回 1.0。</summary>
        public void SetBaseScale(float scale)
        {
            _baseScale = Mathf.Clamp(scale, MobileLayoutConfig.MinScale, MobileLayoutConfig.MaxScale);
            if (_rectTransform != null && !_isHeld)
            {
                _rectTransform.localScale = Vector3.one * _baseScale;
            }
        }

        /// <summary>自定义透明度；大招未就绪的 55% 状态会在该基准上按比例相乘。</summary>
        public void SetBaseAlpha(float alpha)
        {
            _baseAlpha = Mathf.Clamp(alpha, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _baseAlpha * GetSpecialStateAlpha();
            }
        }

        private float GetSpecialStateAlpha()
        {
            return _mode == MobileButtonMode.Special && _hasSpecialState && !_lastReadyState ? 0.55f : 1f;
        }

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
            return Create(parent, name, label, anchoredPos, size, mainColor, icon, fillSprite, ringSprite, mode,
                new Vector2(1f, 0f));
        }

        /// <summary>
        /// 带锚点重载：默认仍锚定右下角；左开火键传 (0,0) 锚定左下角。
        /// </summary>
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
            MobileButtonMode mode,
            Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(size, size);

            var group = go.GetComponent<CanvasGroup>();

            // 1. 硬阴影（墨色实心，无模糊）
            var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(go.transform, false);
            var shadowRt = shadowGo.GetComponent<RectTransform>();
            shadowRt.anchorMin = Vector2.zero;
            shadowRt.anchorMax = Vector2.one;
            shadowRt.anchoredPosition = new Vector2(5f, -5f);
            shadowRt.sizeDelta = Vector2.zero;
            var shadowImg = shadowGo.GetComponent<Image>();
            shadowImg.sprite = fillSprite;
            shadowImg.color = ComicStyle.Ink;
            shadowImg.raycastTarget = false;

            // 2. 视觉内容容器（按下时向右下轻微偏移制造压印感）
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(go.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            // 3. 底盘背景（实心队伍/语义色填充，带像素级透明穿透）
            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(contentRt, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = fillSprite;
            bgImg.color = mainColor;
            bgImg.raycastTarget = true;
            bgImg.alphaHitTestMinimumThreshold = 0.1f;

            // 4. 6px 墨线粗边框（#1A1A1A）
            var ringGo = new GameObject("InkBorder", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(contentRt, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = ComicStyle.Ink;
            ringImg.raycastTarget = false;

            // 5. 中心图标（若为 null 必须隐藏，绝不留白块）
            Image iconImg = null;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(contentRt, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0.20f, 0.28f);
                iconRt.anchorMax = new Vector2(0.80f, 0.88f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;

                iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = ComicStyle.Paper;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            // 6. 中文文字标题（字号 >= 20，粗体高对比度）
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(contentRt, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.05f, 0.06f);
            textRt.anchorMax = new Vector2(0.95f, 0.38f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = Mathf.Max(20f, size * 0.18f);
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = ComicStyle.Paper;
            txt.raycastTarget = false;

            var theme = UIThemeSO.Resolve(null);
            var font = theme.ResolveFont(false, label);
            if (font != null) txt.font = font;

            var btn = go.AddComponent<MobileActionButton>();
            btn._mode = mode;
            btn._rectTransform = rt;
            btn._contentContainer = contentRt;
            btn._shadowImage = shadowImg;
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
            HasDragAim = false;
            DragAimValue = Vector2.zero;

            // 压印效果：内容向右下压入阴影，阴影暂时收起（缩放基于自定义基准）
            if (_contentContainer != null) _contentContainer.anchoredPosition = _shadowOffset;
            if (_shadowImage != null) _shadowImage.enabled = false;
            _rectTransform.localScale = Vector3.one * (_baseScale * 0.96f);
            _bgImage.color = ComicStyle.Darken(_baseColor, 0.12f);

            if (_mode == MobileButtonMode.Tap || _mode == MobileButtonMode.Special)
            {
                _tapped = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_mode == MobileButtonMode.HoldWithAimDrag)
            {
                // 3/4 指玩家可用右食指按住开火、右拇指在滑屏区瞄准；
                // 关闭后开火键上的轻微滑动不再污染视角。
                if (GameSettingsService.FireButtonRotatesCamera)
                {
                    _lookDelta += eventData.delta;
                }

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

            // 压印恢复（<=0.06s，禁止回弹）
            if (_contentContainer != null) _contentContainer.anchoredPosition = Vector2.zero;
            if (_shadowImage != null) _shadowImage.enabled = true;
            _rectTransform.localScale = Vector3.one * _baseScale;
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
            _hasSpecialState = false;
            _lastChargePercentInt = -1;

            if (_contentContainer != null) _contentContainer.anchoredPosition = Vector2.zero;
            if (_shadowImage != null) _shadowImage.enabled = true;
            if (_rectTransform != null) _rectTransform.localScale = Vector3.one * _baseScale;
            if (_bgImage != null) _bgImage.color = _baseColor;
            if (_canvasGroup != null) _canvasGroup.alpha = _baseAlpha * GetSpecialStateAlpha();
        }

        private void OnDisable()
        {
            ResetState();
        }

        public void UpdateSpecialState(bool isReady, float charge01)
        {
            if (_mode != MobileButtonMode.Special) return;

            int chargePct = Mathf.FloorToInt(charge01 * 100f);

            if (isReady)
            {
                _canvasGroup.alpha = _baseAlpha;
                _bgImage.color = _baseColor;
                _ringImage.color = ComicStyle.Ink;

                float breath = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5.5f);
                if (!_isHeld) _rectTransform.localScale = Vector3.one * (_baseScale * breath);
                if (_labelText != null && (!_hasSpecialState || !_lastReadyState))
                {
                    _labelText.text = "大招就绪!";
                }
            }
            else
            {
                _canvasGroup.alpha = _baseAlpha * 0.55f;
                _bgImage.color = new Color(0.38f, 0.36f, 0.32f, 0.90f);
                _ringImage.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
                if (!_isHeld) _rectTransform.localScale = Vector3.one * _baseScale;
                if (_labelText != null && (!_hasSpecialState || _lastReadyState || _lastChargePercentInt != chargePct))
                {
                    _labelText.text = $"{chargePct}%";
                }
            }

            _hasSpecialState = true;
            _lastReadyState = isReady;
            _lastChargePercentInt = chargePct;
        }
    }
}
