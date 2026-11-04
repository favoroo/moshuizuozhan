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
    /// 移动端触控交互系统（t-1 新野兽派 P19 波次2b）：
    /// 1. 左侧：半屏动态浮动摇杆（Floating Joystick），墨黑底盘 + 细纸白实线环 + 纸白把手；
    /// 2. 右侧：动作键群 = 墨黑半透明底 + 功能色 4~5px 描边 + 功能色图标 + 纸白标签，
    ///    按下才整块翻功能色实底（功能色只做编码，不再铺 5 块高饱和大色饼）；
    /// 3. 右半屏：滑屏转视角感应区（每帧触摸增量驱动相机 Yaw/Pitch）；
    /// 4. 动作键 / 摇杆把手图标走程序化几何字形（MobileControlGlyphFactory），缺失时安全隐藏并由文字兜底；
    ///    Inspector 显式赋值的 Sprite 仍优先，便于单独换皮；
    /// 5. 尺寸与排布默认值统一暴露为 DefaultLayout，便于运行时与测试共享；
    /// 6. 支持 DPI 物理毫米自适应与自定义键位持久化；
    /// 7. 3/4 指方案提供左上角独立左开火键，并与 2/3/4 指预设、固定摇杆、辅助瞄准档位联动。
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

        [Tooltip("是否在 HUD 左上角生成固定按键设置入口（已整合进暂停弹窗，默认关闭）")]
        [SerializeField] private bool showSettingsEntryButton = false;

        [Tooltip("旧版字段，仅保留场景序列化兼容；P9 起不再参与显示判断，请使用 showTouchUiInEditor")]
        [SerializeField] private bool forceShowInEditor = true;

        [Header("左侧动态浮动摇杆参数")]
        [SerializeField] private float moveStickRadius = 170f;
        [SerializeField] private float moveDeadZone = 8f;
        [SerializeField] private Vector2 moveStickDefaultPos = new Vector2(200f, 200f);

        [Header("按键图标覆盖（为空时使用程序化几何字形）")]
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

        /// <summary>手雷按钮圆形命中区向外扩展的透明命中边距（参考分辨率像素）：防边缘按压漏给下层滑屏区。</summary>
        private const float BombButtonHitPadding = 14f;

        private int _lastBombDenyStamp;

        private MatchManager _match;
        private bool _subscribedMatch;
        private bool _subscribedSettings;
        private bool _dpiScaleApplied;

        private static Sprite _circleFillSprite;
        private static Sprite _circleRingSprite;

        /// <summary>
        /// 动作键专用细环（128px 纹理上 4px 环宽）：132~176px 的按键拉到屏幕后约 4.1~5.5px，
        /// 与摇杆共用 2px 环会偏细，故单独烘一张。
        /// </summary>
        private static Sprite _actionRingSprite;

        public bool IsShown => _canvas != null && _canvas.gameObject.activeInHierarchy;
        public DynamicFloatingJoystick FloatingJoystick => _floatingJoystick;
        /// <summary>是否在 HUD 左上角生成常驻按键设置入口（默认关闭，改由暂停菜单内承载）。</summary>
        public bool ShowSettingsEntryButton
        {
            get => showSettingsEntryButton;
            set
            {
                showSettingsEntryButton = value;
                if (_settingsEntryButton != null)
                {
                    _settingsEntryButton.gameObject.SetActive(value);
                }
                else if (value && _safeArea != null)
                {
                    CreateSettingsEntryButton(_safeArea);
                }
            }
        }

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

        // 切后台 / 失焦时系统手势会吞掉 pointer-up，按钮的按住状态会永久卡死
        // （表现为开火键失灵、手雷按钮视觉不复原），此处统一清屏兜底。
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetAllInputs();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ResetAllInputs();
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
            // 只有真机/触屏模拟才切移动端 HUD 布局；桌面编辑器保持右上角仪表簇。
            // （历史 bug：这里无条件调用，导致桌面 HUD 也被压成底部紧凑条。）
            var hud = FindAnyObjectByType<HudController>();
            if (hud != null && ShouldShowTouchUi()) hud.ApplyMobileLayout();

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

            // 5. 手雷：pointer-down = 进入瞄准（弹道预览），pointer-up = 松手投出
            if (_bombButton != null)
            {
                if (_bombButton.ConsumeTap())
                {
                    player.TouchBombTriggered = true;
                }
                if (_bombButton.ConsumeRelease())
                {
                    player.TouchBombReleased = true;
                }
                player.TouchBombHeld = _bombButton.IsAimingHeld;

                // 冷却硬切显示（径向暗色扇形收缩 + 按钮标签倒计时 + 墨边翻黄）+ 拒绝闪红。
                // 拒绝走轮询戳而非事件订阅：LocalPlayer 重开/复活会换实例，轮询无需管理订阅生命周期。
                var weapon = player.Weapon;
                if (weapon != null)
                {
                    float cdDuration = weapon.BombCooldownDuration;
                    float cdRemaining = weapon.BombCooldownRemaining;
                    _bombButton.UpdateBombCooldown01(cdDuration > 0f
                        ? cdRemaining / cdDuration
                        : 0f, cdRemaining);

                    if (player.SubBombDenyStamp != _lastBombDenyStamp)
                    {
                        _lastBombDenyStamp = player.SubBombDenyStamp;
                        _bombButton.NotifyDenied();
                    }
                }
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
            //    把手字形用四向箭头，明确「移动/方向」；旧版手柄图标看像外设而不是摇杆功能。
            Sprite moveGlyph = joystickKnobIcon != null
                ? joystickKnobIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.MovePad);
            _floatingJoystick = DynamicFloatingJoystick.Create(
                _safeArea,
                moveStickDefaultPos,
                moveStickRadius,
                moveDeadZone,
                _circleFillSprite,
                _circleRingSprite,
                moveGlyph);
            _floatingJoystick.FixedBase = GameSettingsService.FixedJoystick;

            // 3. 右下角 t-1 人体工学操作按键群
            //    样式：墨黑半透明底 + 功能色 4px 描边 + 功能色图标；功能色只做「功能编码」，
            //    按下去才允许整块翻成功能色实底（消除过去的 5 块高饱和大色饼）。
            var fireCfg = DefaultLayout[0];
            var swimCfg = DefaultLayout[1];
            var jumpCfg = DefaultLayout[2];
            var bombCfg = DefaultLayout[3];
            var specialCfg = DefaultLayout[4];

            // 动作键字形：默认用程序化几何剪影（准星 / 墨滴 / 上箭头 / 手雷 / 下砸），
            // Inspector 里显式赋值的 Sprite 仍优先，便于单独换皮。
            Sprite fireGlyph = fireIcon != null
                ? fireIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.Crosshair);
            Sprite swimGlyph = swimIcon != null
                ? swimIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.InkDrop);
            Sprite jumpGlyph = jumpIcon != null
                ? jumpIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.Jump);
            Sprite bombGlyph = bombIcon != null
                ? bombIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.Bomb);
            Sprite specialGlyph = specialIcon != null
                ? specialIcon
                : MobileControlGlyphFactory.Get(MobileControlGlyph.Slam);

            _fireButton = MobileActionButton.Create(
                _safeArea, "FireButton", fireCfg.label,
                fireCfg.pos, fireCfg.size,
                ComicStyle.FromHex(0xFF7314), // 队伍橙（射击）
                fireGlyph, _circleFillSprite, _actionRingSprite,
                MobileButtonMode.HoldWithAimDrag);

            _swimButton = MobileActionButton.Create(
                _safeArea, "SwimButton", swimCfg.label,
                swimCfg.pos, swimCfg.size,
                ComicStyle.Blue, // 队伍蓝（潜行）
                swimGlyph, _circleFillSprite, _actionRingSprite,
                MobileButtonMode.Hold);

            _jumpButton = MobileActionButton.Create(
                _safeArea, "JumpButton", jumpCfg.label,
                jumpCfg.pos, jumpCfg.size,
                ComicStyle.Green, // 语义绿（跳跃）
                jumpGlyph, _circleFillSprite, _actionRingSprite,
                MobileButtonMode.Tap);

            _bombButton = MobileActionButton.Create(
                _safeArea, "BombButton", bombCfg.label,
                bombCfg.pos, bombCfg.size,
                new Color(0.58f, 0.28f, 0.95f, 1f), // 荧光紫（副武器）
                bombGlyph, _circleFillSprite, _actionRingSprite,
                MobileButtonMode.BombTapOrHoldAim,
                hitPadding: BombButtonHitPadding);

            _specialButton = MobileActionButton.Create(
                _safeArea, "SpecialButton", specialCfg.label,
                specialCfg.pos, specialCfg.size,
                ComicStyle.Yellow, // 主行动金（大招）
                specialGlyph, _circleFillSprite, _actionRingSprite,
                MobileButtonMode.Special);

            // 3b. 左开火键（3/4 指方案专用，2 指方案默认隐藏；左上角锚定，便于左食指开火）
            _leftFireButton = MobileActionButton.Create(
                _safeArea, "LeftFireButton", "射击",
                MobileLayoutPresets.ThreeFingerLeftFirePos, MobileLayoutPresets.LeftFireSize,
                ComicStyle.FromHex(0xFF7314),
                fireGlyph, _circleFillSprite, _actionRingSprite,
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
                ApplyDefaultLeftFireLayout(deviceScale);
            }

            _customizer.OnClosed += HandleCustomizerClosed;

            // 5. 屏幕左上角悬浮【⚙️ 键位】快速编辑入口（已整合进暂停弹窗，默认不再常驻生成）
            if (showSettingsEntryButton)
            {
                CreateSettingsEntryButton(_safeArea);
            }
            else
            {
                var existingBtn = _safeArea != null ? _safeArea.Find("SettingsLayoutBtn") : null;
                if (existingBtn != null)
                {
                    existingBtn.gameObject.SetActive(false);
                }
            }

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
            ApplyDefaultLeftFireLayout(scale);
        }

        private static void ApplyButtonScale(MobileActionButton button, Vector2 basePos, float scale)
        {
            if (button == null) return;
            button.RectTransform.anchoredPosition = basePos * scale;
            button.SetBaseScale(scale);
        }

        /// <summary>
        /// 左上角开火键的默认外观：位置取预设左上角，基准缩放比其他动作键大 1.7 倍，
        /// 并略微透出战场。3/4 指预设应用后仍以布局配置里的数值为准。
        /// </summary>
        private void ApplyDefaultLeftFireLayout(float scale)
        {
            if (_leftFireButton == null) return;

            _leftFireButton.RectTransform.anchoredPosition =
                MobileLayoutPresets.ThreeFingerLeftFirePos * scale;
            _leftFireButton.SetBaseScale(scale * MobileLayoutPresets.TopActionScale);
            _leftFireButton.SetBaseAlpha(MobileLayoutPresets.LeftFireAlpha);
        }

        private void CreateSettingsEntryButton(RectTransform safeArea)
        {
            // 几何大胆风操作入口：直角纯色块 + 4px 墨边 + 齿轮图标 + 放大粗字（无阴影）
            var go = new GameObject("SettingsLayoutBtn", typeof(RectTransform), typeof(Button));
            go.transform.SetParent(safeArea, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); // 左上角
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            // 暂停按钮占 (24,-24) 的 88x88；键位入口贴在其右侧（间隔 12px），
            // 用 -32 让 72 高的小标签与 88 高的暂停按钮垂直中心对齐（-68）。
            rt.anchoredPosition = new Vector2(124f, -32f);
            rt.sizeDelta = new Vector2(168f, 72f);

            // 纸面底盘 + 4px 墨边（直角 SharpRect）
            var shapeGo = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            shapeGo.transform.SetParent(go.transform, false);
            var shapeRt = shapeGo.GetComponent<RectTransform>();
            shapeRt.anchorMin = Vector2.zero;
            shapeRt.anchorMax = Vector2.one;
            shapeRt.offsetMin = Vector2.zero;
            shapeRt.offsetMax = Vector2.zero;
            var shape = shapeGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = ComicStyle.Paper;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.Halftone = false;
            shape.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = shape;
            var colors = btn.colors;
            colors.normalColor = ComicStyle.Paper;
            colors.highlightedColor = ComicStyle.PaperAlt;
            colors.pressedColor = ComicStyle.Yellow;
            colors.selectedColor = ComicStyle.Paper;
            colors.fadeDuration = ComicStyle.PressDuration;
            btn.colors = colors;
            btn.onClick.AddListener(OpenLayoutCustomizer);
            _settingsEntryButton = btn;

            // t-1 按压反馈：0ms 硬切 scale 0.97 + 松开 0.1s Cubic-Out 回弹（走主题交互）
            var motion = go.AddComponent<UISpringMotion>();
            motion.SetTheme(null);
            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(null, btn, shape);

            // 齿轮图标（显示尺寸 ≥24px）
            var gearSprite = UIIconLibrary.Get("glyph_gear");
            if (gearSprite != null)
            {
                var iconGo = new GameObject("GearIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(shapeGo.transform, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(12f, 0f);
                iconRt.sizeDelta = new Vector2(32f, 32f);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = gearSprite;
                iconImg.color = ComicStyle.Ink;
                iconImg.raycastTarget = false;
            }

            // 文字标签（字号 >= 22，移动端 >= 26，墨线色，超粗体）
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(shapeGo.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(gearSprite != null ? 48f : 0f, 0f);
            trt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<TextMeshProUGUI>();
            // MiSans 没有 ⚙/U+FE0F 字形：缺图标时不能拿齿轮字符顶替，否则真机走系统回退字体
            // 会让整层 UI 当帧重排（见 tools/font_exempt_chars.txt 的豁免规则）。
            txt.text = gearSprite != null ? "操作" : "键位";
            txt.fontSize = UITypography.FontSnapped(24f);
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
            if (_circleFillSprite != null && _circleRingSprite != null && _actionRingSprite != null) return;

            const int size = 128;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f - 2f;

            // 摇杆底盘 340px 拉到屏幕后环宽 = ringWidth * 340/128：
            // 旧 8px 环 = 21px 粗（规范 4~5px），现 2px 环 ≈ 5.3px。
            _circleRingSprite = BuildRingSprite(size, center, radius, 2f);
            // 动作键 132~176px：4px 环拉到屏幕约 4.1~5.5px（左开火 1.7 倍放大约 7px）。
            _actionRingSprite = BuildRingSprite(size, center, radius, 4f);

            var fillTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            fillTex.wrapMode = TextureWrapMode.Clamp;
            fillTex.filterMode = FilterMode.Bilinear;

            var fillColors = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float outerAlpha = Mathf.Clamp01((radius - d) / 1.5f);
                    fillColors[idx] = new Color32(255, 255, 255, (byte)(outerAlpha * 255f));
                }
            }

            fillTex.SetPixels32(fillColors);
            fillTex.Apply(false, false);
            _circleFillSprite = Sprite.Create(fillTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>生成「白色 alpha 环」纹理精灵（颜色交给 Image.color 染色）。</summary>
        private static Sprite BuildRingSprite(int size, Vector2 center, float radius, float ringWidth)
        {
            // 抗锯齿只占 0.75 纹理像素：1.5px 的双边过渡会把 2px 细环的峰值压到 44%（发灰），
            // 0.75 时峰值 alpha=1 的实心带 ≈ ringWidth - 1.5，屏幕观感就是 ringWidth 的等比宽度。
            const float aa = 0.75f;

            var ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            ringTex.wrapMode = TextureWrapMode.Clamp;
            ringTex.filterMode = FilterMode.Bilinear;

            var ringColors = new Color32[size * size];
            float innerR = radius - Mathf.Max(1f, ringWidth);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                    float outerAlpha = Mathf.Clamp01((radius - d) / aa);
                    float innerAlpha = Mathf.Clamp01((d - innerR) / aa);
                    byte aRing = (byte)(outerAlpha * innerAlpha * 255f);
                    ringColors[idx] = new Color32(255, 255, 255, aRing);
                }
            }

            ringTex.SetPixels32(ringColors);
            ringTex.Apply(false, false);
            return Sprite.Create(ringTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
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

            if (Screen.width <= 0 || Screen.height <= 0) return;

            // 启动/旋转瞬间 safeArea 与 Screen 可能短暂不在同一像素空间（如 16:9 兼容缓冲
            // 配物理像素安全区），归一化锚点会 >1，把安全区撑得比画布还大——真机 20:9 实测
            // 会把首页 2×2 卡阵整列推出屏幕。此时先不套用也不缓存，等 surface settle 后
            // Update 检测到新值再收敛。
            if (safe.xMax > Screen.width + 0.5f || safe.yMax > Screen.height + 0.5f ||
                safe.xMin < -0.5f || safe.yMin < -0.5f)
            {
                return;
            }

            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

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
    /// t-1 外观：墨黑底盘 + 细纸白实线环 + 纸白把手 + 墨黑细环（环宽 4~5px，不再是 21px 大圆环）。
    /// 按压反馈：按下 0ms 硬切 scale 0.97，松开 0.1s Cubic-Out 回弹（只看视觉，不影响输入判定）。
    /// </summary>
    public class DynamicFloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>按压缩放：与动作键一致（0ms 硬切）。</summary>
        private const float PressedScale = ComicStyle.PressScale;

        /// <summary>松开回弹时长（秒）：Cubic-Out，禁止弹簧/过冲。</summary>
        private const float PressReboundSeconds = 0.1f;

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

        // 按压回弹（0.1s Cubic-Out）：只驱动视觉容器缩放，绝不参与 Value 计算。
        // _pressHomeScale 记录按下前的基准缩放，避免把自定义布局里的摇杆缩放重置回 1.0。
        private float _pressReboundT = 1f;
        private Vector3 _pressHomeScale = Vector3.one;

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

            // 底盘纯黑纯色块（黑底白圈几何风）
            var bgGo = new GameObject("BaseBg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(containerRt, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = fillSprite;
            bgImg.color = ComicStyle.Ink;
            bgImg.raycastTarget = false;

            // 几何风细纸白实线外环（黑底白圈；2px 纹理环拉到 340px 约 5.3px）
            var ringGo = new GameObject("BaseRing", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(containerRt, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = ComicStyle.Paper;
            ringImg.raycastTarget = false;

            // 中心把手（Knob）：白底黑边（纸白底 + 墨黑环）
            var knobGo = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            knobGo.transform.SetParent(containerRt, false);
            var knobRt = knobGo.GetComponent<RectTransform>();
            knobRt.anchorMin = new Vector2(0.5f, 0.5f);
            knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.anchoredPosition = Vector2.zero;
            knobRt.sizeDelta = new Vector2(radius * 0.92f, radius * 0.92f);

            var knobImg = knobGo.GetComponent<Image>();
            knobImg.sprite = fillSprite;
            knobImg.color = ComicStyle.Paper;
            knobImg.raycastTarget = false;

            // 把手墨黑细实线环（白底黑边）
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
                // 四向箭头占把手 76%，比旧手柄图标放大一档：摇杆是高频控件，方向必须一眼读出。
                iconRt.anchorMin = new Vector2(0.12f, 0.12f);
                iconRt.anchorMax = new Vector2(0.88f, 0.88f);
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
            if (_knob != null)
            {
                _knob.anchoredPosition = Vector2.zero;
                var kImg = _knob.GetComponent<Image>();
                if (kImg != null) kImg.color = ComicStyle.Yellow;
            }
            Value = Vector2.zero;

            // 几何大胆风：硬切高亮（满不透明度 1.0）
            if (_canvasGroup != null) _canvasGroup.alpha = 1.0f;

            // t-1 按压：0ms 硬切 scale 0.97（纯视觉，不影响 Value / 触摸判定）
            _pressReboundT = 1f;
            if (_container != null)
            {
                _pressHomeScale = _container.localScale;
                _container.localScale = _pressHomeScale * PressedScale;
            }
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

            // 松开 0.1s Cubic-Out 回弹（与动作键同一套 t-1 按压物理）
            ResetJoystick(rebound: true);
        }

        public void ResetJoystick()
        {
            ResetJoystick(rebound: false);
        }

        private void ResetJoystick(bool rebound)
        {
            bool wasPressed = _activePointerId != -999 || _pressReboundT < 1f;

            _activePointerId = -999;
            Value = Vector2.zero;

            if (_knob != null)
            {
                _knob.anchoredPosition = Vector2.zero;
                var kImg = _knob.GetComponent<Image>();
                if (kImg != null) kImg.color = ComicStyle.Paper;
            }
            if (_container != null)
            {
                _container.anchoredPosition = _defaultPos;

                // 回弹从「按下态」起步；外部复位（编辑/失焦/切后台）直接回到基准缩放。
                // 没有按压时以当前缩放为新基准，避免把自定义布局里的摇杆缩放重置回 1.0。
                if (rebound) _container.localScale = _pressHomeScale * PressedScale;
                else if (wasPressed) _container.localScale = _pressHomeScale;
                else _pressHomeScale = _container.localScale;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = _baseAlpha;

            _pressReboundT = rebound ? 0f : 1f;
        }

        private void Update()
        {
            if (_pressReboundT >= 1f || _container == null) return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f) return;

            _pressReboundT = Mathf.Min(1f, _pressReboundT + deltaTime / PressReboundSeconds);
            float eased = UICubicBezier.EaseOut.Evaluate(_pressReboundT);
            _container.localScale = _pressHomeScale * Mathf.Lerp(PressedScale, 1f, eased);
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
    /// 移动端 t-1 新野兽派触控按钮：
    /// 1. 常态 = 墨黑半透明底（62%）+ 功能色 4px 描边 + 功能色图标 + 纸白标签；
    ///    功能色只做「功能编码」，不再整块高饱和实底；
    /// 2. 按下 0ms 硬切：整块翻成功能色实底（图标/标签转墨黑）+ scale 0.97；
    ///    松开 0.1s Cubic-Out 回弹到常态，禁止弹簧/过冲/呼吸；
    /// 3. 标签字号走 UITypography.FontSnapped(max(24, size*0.20))，物理极佳可读；
    /// 4. 图标安全查找，缺失时隐藏并由中文标签兜底，绝无白块；
    /// 5. 支持像素级圆形命中测试，防止误触透明角落；
    /// 6. 支持开火按住、滑拽瞄准、手雷按住瞄准/松手投、大招就绪状态。
    /// </summary>
    public class MobileActionButton : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>松开回弹时长（秒）：Cubic-Out 回到基准缩放，禁止弹簧。</summary>
        private const float PressReboundSeconds = 0.1f;

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
        private Vector2 _shadowOffset = Vector2.zero;
        private bool _isHeld;
        private bool _tapped;
        private bool _released;
        private float _pointerDownTime;
        private bool _isAimingHeld;
        private Vector2 _lookDelta;
        private int _lastChargePercentInt = -1;
        private bool _lastReadyState;
        private bool _hasSpecialState;
        private float _baseScale = 1f;
        private float _baseAlpha = 1f;
        private string _defaultLabelText = "手雷";

        // t-1 按压物理：按下 0ms 硬切 scale 0.97，松开 0.1s Cubic-Out 回弹。
        // 只驱动 localScale，不参与任何输入判定（开火按住/长按潜行不受影响）。
        private float _pressReboundT = 1f;

        /// <summary>动作键常态底：墨黑半透明（看得清战况，又给功能色描边/图标垫深底）。</summary>
        private static Color ActionFill => ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f);

        // 多指/丢 pointer-up 加固：只跟踪按下时的那根手指
        private int _activePointerId = int.MinValue;
        // 手雷按住瞄准阈值：保留旧字段/属性以兼容旧测试，现行逻辑按下即进入瞄准
        private float _aimHoldSeconds = DefaultAimHoldSeconds;
        // 手雷冷却显示（硬切径向扇形 + 按钮标签倒计时）与拒绝闪红
        private Image _cooldownFillImage;
        private Color _defaultRingColor = ComicStyle.Ink;
        private float _cooldown01;
        private float _denyFlashUntil;
        private int _lastCooldownTenths = int.MinValue;

        /// <summary>保留旧常量：手雷按下即进入瞄准，不再依赖按住时长阈值。</summary>
        private const float DefaultAimHoldSeconds = 0.15f;
        private const float DenyFlashSeconds = 0.2f;
        private static readonly Color DenyFlashColor = new Color(0.94f, 0.22f, 0.22f, 1f);

        public bool IsHeld => _isHeld;
        public bool IsAimingHeld => _isAimingHeld;
        public bool HasDragAim { get; private set; }
        public Vector2 DragAimValue { get; private set; }
        public float BaseScale => _baseScale;
        public float BaseAlpha => _baseAlpha;

        /// <summary>保留旧接口：现行手雷按下即进入瞄准，该阈值不再参与判定。</summary>
        public float AimHoldSeconds
        {
            get => _aimHoldSeconds;
            set => _aimHoldSeconds = Mathf.Max(0f, value);
        }

        /// <summary>自定义尺寸（相对基准的缩放）；按下/回弹都在此基准上乘系数，不再重置回 1.0。</summary>
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
            if (_mode == MobileButtonMode.Special && _hasSpecialState && !_lastReadyState) return 0.55f;
            if (_cooldown01 > 0f) return 0.8f;
            return 1f;
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

        /// <summary>
        /// 消费一次「手雷键松手」事件（BombTapOrHoldAim 模式）：按下进入瞄准，松手才真正投出。
        /// </summary>
        public bool ConsumeRelease()
        {
            if (!_released) return false;
            _released = false;
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
            MobileButtonMode mode,
            float hitPadding = 0f)
        {
            return Create(parent, name, label, anchoredPos, size, mainColor, icon, fillSprite, ringSprite, mode,
                new Vector2(1f, 0f), hitPadding);
        }

        /// <summary>
        /// 带锚点重载：默认仍锚定右下角；左开火键传 (0,0) 锚定左下角。
        /// <paramref name="hitPadding"/> &gt; 0 时生成一个向外扩展的透明命中体，并把圆形底盘移出
        /// raycast——防止按在圆形 alpha 命中区之外时，按压漏给下层的滑屏转视角区。
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
            Vector2 anchor,
            float hitPadding = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(size, size);

            // 0. 命中区扩展（可选）：透明矩形命中体垫在最底层，覆盖整个矩形 + 外扩边距
            if (hitPadding > 0f)
            {
                var hitGo = new GameObject("HitPad", typeof(RectTransform), typeof(Image));
                hitGo.transform.SetParent(go.transform, false);
                hitGo.transform.SetAsFirstSibling();
                var hitRt = hitGo.GetComponent<RectTransform>();
                hitRt.anchorMin = Vector2.zero;
                hitRt.anchorMax = Vector2.one;
                hitRt.offsetMin = new Vector2(-hitPadding, -hitPadding);
                hitRt.offsetMax = new Vector2(hitPadding, hitPadding);
                var hitImg = hitGo.GetComponent<Image>();
                hitImg.color = Color.clear;
                hitImg.raycastTarget = true;
            }

            var group = go.GetComponent<CanvasGroup>();

            // 1. 阴影占位（几何大胆风全局禁用阴影，设为 disabled 避免画出偏移阴影，同时保留对象避免外部判空）
            var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(go.transform, false);
            var shadowRt = shadowGo.GetComponent<RectTransform>();
            shadowRt.anchorMin = Vector2.zero;
            shadowRt.anchorMax = Vector2.one;
            shadowRt.anchoredPosition = Vector2.zero;
            shadowRt.sizeDelta = Vector2.zero;
            var shadowImg = shadowGo.GetComponent<Image>();
            shadowImg.sprite = fillSprite;
            shadowImg.color = Color.clear;
            shadowImg.enabled = false;
            shadowImg.raycastTarget = false;

            // 2. 视觉内容容器
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(go.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            // 3. 底盘背景：墨黑半透明（t-1 触控层不再用 5 色高饱和实底）
            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(contentRt, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = fillSprite;
            bgImg.color = ActionFill;
            // 有 HitPad 时命中统一走外扩矩形（全圆 + 边距都可按）；无 HitPad 保持圆形 alpha 命中
            bgImg.raycastTarget = hitPadding <= 0f;
            bgImg.alphaHitTestMinimumThreshold = 0.1f;

            // 4. 功能色细描边（屏幕实际 4~5px）：功能色只出现在描边与图标上
            var ringGo = new GameObject("InkBorder", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(contentRt, false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = Vector2.zero;
            ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = ringSprite;
            ringImg.color = mainColor;
            ringImg.raycastTarget = false;

            // 4.5 手雷冷却遮罩（仅 Bomb 模式）：径向暗色扇形表示剩余冷却，硬切更新禁 tween
            Image cooldownFillImg = null;
            if (mode == MobileButtonMode.BombTapOrHoldAim && fillSprite != null)
            {
                var cdGo = new GameObject("CooldownFill", typeof(RectTransform), typeof(Image));
                cdGo.transform.SetParent(contentRt, false);
                var cdRt = cdGo.GetComponent<RectTransform>();
                cdRt.anchorMin = Vector2.zero;
                cdRt.anchorMax = Vector2.one;
                cdRt.offsetMin = Vector2.zero;
                cdRt.offsetMax = Vector2.zero;
                cooldownFillImg = cdGo.GetComponent<Image>();
                cooldownFillImg.sprite = fillSprite;
                cooldownFillImg.color = new Color(0.08f, 0.08f, 0.09f, 0.62f);
                cooldownFillImg.type = Image.Type.Filled;
                cooldownFillImg.fillMethod = Image.FillMethod.Radial360;
                cooldownFillImg.fillOrigin = (int)Image.Origin360.Top;
                cooldownFillImg.fillClockwise = true;
                cooldownFillImg.fillAmount = 0f;
                cooldownFillImg.raycastTarget = false;
                cooldownFillImg.enabled = false;
            }

            // 5. 中心图标（若为 null 必须隐藏，绝不留白块）
            Image iconImg = null;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(contentRt, false);
                var iconRt = iconGo.GetComponent<RectTransform>();
                // 图标占上半区 64%×60%，下方留给中文标签；程序化字形主体只到 ±0.8，
                // 四周留白保证自定义缩放 / 高 DPI 下也不会压到描边或标签。
                iconRt.anchorMin = new Vector2(0.18f, 0.36f);
                iconRt.anchorMax = new Vector2(0.82f, 0.96f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;

                iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = mainColor; // 功能色图标（按下/大招就绪时翻墨黑）
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            // 6. 中文超粗大标签（字号走 UITypography: max(24, size*0.20)，纯白高对比度）
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(contentRt, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.04f, 0.05f);
            textRt.anchorMax = new Vector2(0.96f, 0.35f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var txt = textGo.GetComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = MobileUiMetrics.ComputeButtonLabelFontSize(size);
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = ComicStyle.Paper;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Overflow;

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
            btn._defaultRingColor = ringImg.color;
            btn._cooldownFillImage = cooldownFillImg;
            btn._defaultLabelText = label;

            return btn;
        }

        private void Update()
        {
            TickDenyFlash();
            AdvancePressFeedback(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 按压回弹步进（0.1s Cubic-Out 回到基准缩放）。Update 每帧调用；
        /// 测试可直接喂 deltaTime 一次走完，不需要真实帧。
        /// 只写 localScale，绝不触碰 _isHeld/_tapped 等输入状态（开火节流、长按潜行不受影响）。
        /// </summary>
        public void AdvancePressFeedback(float deltaTime)
        {
            if (_pressReboundT >= 1f || deltaTime <= 0f) return;

            _pressReboundT = Mathf.Min(1f, _pressReboundT + deltaTime / PressReboundSeconds);
            if (_rectTransform == null) return;

            float eased = UICubicBezier.EaseOut.Evaluate(_pressReboundT);
            _rectTransform.localScale = Vector3.one * (_baseScale * Mathf.Lerp(ComicStyle.PressScale, 1f, eased));
        }

        /// <summary>大招就绪的「实底强调」态（硬切高亮，不做呼吸缩放）。</summary>
        private bool IsSpecialReadyVisual =>
            _mode == MobileButtonMode.Special && _hasSpecialState && _lastReadyState;

        /// <summary>常态：墨黑半透明底 + 功能色描边/图标 + 纸白标签（冷却/就绪另有硬切）。</summary>
        private void ApplyIdleVisual()
        {
            if (_bgImage != null) _bgImage.color = GetDefaultBgColor();

            // 拒绝闪红期间保住红色描边，等 TickDenyFlash 复原
            if (_ringImage != null && Time.unscaledTime >= _denyFlashUntil) _ringImage.color = GetIdleRingColor();

            ApplyContentColors(pressed: false);
        }

        /// <summary>按下：0ms 整块翻功能色实底，描边/图标/标签转墨黑（硬切，不补间）。</summary>
        private void ApplyPressedVisual()
        {
            if (_bgImage != null) _bgImage.color = _baseColor;
            if (_ringImage != null) _ringImage.color = ComicStyle.Ink;
            ApplyContentColors(pressed: true);
        }

        private void ApplyContentColors(bool pressed)
        {
            bool dark = pressed || IsSpecialReadyVisual;

            if (_iconImage != null) _iconImage.color = dark ? ComicStyle.Ink : _baseColor;
            if (_labelText == null) return;

            if (dark) _labelText.color = ComicStyle.Ink;
            else if (_mode == MobileButtonMode.BombTapOrHoldAim && _cooldown01 > 0f) _labelText.color = ComicStyle.Yellow;
            else _labelText.color = ComicStyle.Paper;
        }

        private Color GetIdleRingColor()
        {
            if (IsSpecialReadyVisual) return ComicStyle.Paper; // 实底上留纸白 4px 边，仍能读清圆形
            if (_mode == MobileButtonMode.BombTapOrHoldAim && _cooldown01 > 0f) return ComicStyle.Yellow;
            return _defaultRingColor;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // 已有手指按住时忽略其他手指：第二根手指不得重置按住计时与拖拽状态
            if (_isHeld && eventData.pointerId != _activePointerId) return;

            _activePointerId = eventData.pointerId;
            _isHeld = true;
            _pointerDownTime = Time.unscaledTime;
            _lookDelta = Vector2.zero;
            HasDragAim = false;
            DragAimValue = Vector2.zero;

            // 上一根手指丢失 pointer-up（系统手势/切后台/来电话）会留下 _tapped/_isAimingHeld 残留，
            // 不清理会让一次残留事件被重复消费或按钮状态卡死
            _tapped = false;
            _released = false;
            _isAimingHeld = false;
            _denyFlashUntil = 0f;

            // t-1 按压：0ms 硬切 scale 0.97 + 整块翻功能色实底（功能色只在按下时铺满）
            if (_contentContainer != null) _contentContainer.anchoredPosition = Vector2.zero;
            if (_shadowImage != null) _shadowImage.enabled = false;
            _pressReboundT = 1f;
            _rectTransform.localScale = Vector3.one * (_baseScale * ComicStyle.PressScale);
            ApplyPressedVisual();

            if (_mode == MobileButtonMode.Tap || _mode == MobileButtonMode.Special)
            {
                _tapped = true;
            }
            else if (_mode == MobileButtonMode.BombTapOrHoldAim)
            {
                // inkwave 式手雷：pointer-down 进入瞄准（显示弹道预览），pointer-up 才投出。
                // ConsumeTap 作为「按下」事件注入 PlayerController，ConsumeRelease 作为「松手投掷」。
                _tapped = true;
                _isAimingHeld = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;

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
                // 按住手雷键拖动 = 瞄准：累计像素增量交给 MobileTouchUI 统一换算成视角转角，
                // 松开前可以边拖边看弹道预览落到准星处。
                _lookDelta += eventData.delta;
                _isAimingHeld = true;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _activePointerId) return;
            _activePointerId = int.MinValue;

            _isHeld = false;
            _lookDelta = Vector2.zero;

            // 颜色硬切复原；缩放交给 0.1s Cubic-Out 回弹（从按下态起步，不做瞬时跳变）
            if (_contentContainer != null) _contentContainer.anchoredPosition = Vector2.zero;
            if (_shadowImage != null) _shadowImage.enabled = false;
            ApplyIdleVisual();
            _pressReboundT = 0f;

            if (_mode == MobileButtonMode.HoldWithAimDrag)
            {
                HasDragAim = false;
                DragAimValue = Vector2.zero;
            }
            else if (_mode == MobileButtonMode.BombTapOrHoldAim)
            {
                // 松手 = 投出：产生一次 release 事件，PlayerController 按当前准星落点抛出
                _isAimingHeld = false;
                _released = true;
            }
        }

        /// <summary>
        /// 手雷冷却显示（0 = 就绪，1 = 刚投出）：径向暗色扇形收缩 + 墨边翻黄 + 按钮标签倒计时。
        /// remainingSeconds &lt; 0 时（旧调用方）只显示「冷却」文字。
        /// </summary>
        public void UpdateBombCooldown01(float cooldown01, float remainingSeconds = -1f)
        {
            cooldown01 = Mathf.Clamp01(cooldown01);
            bool cooling = cooldown01 > 0.0001f;

            if (!Mathf.Approximately(cooldown01, _cooldown01))
            {
                _cooldown01 = cooldown01;

                if (_cooldownFillImage != null)
                {
                    _cooldownFillImage.enabled = cooling;
                    if (cooling) _cooldownFillImage.fillAmount = cooldown01;
                }

                // 冷却中：描边硬切翻黄（功能色编码），底色仍是墨黑半透明
                if (!_isHeld) ApplyIdleVisual();
                if (_canvasGroup != null) _canvasGroup.alpha = _baseAlpha * GetSpecialStateAlpha();
            }

            if (_mode != MobileButtonMode.BombTapOrHoldAim || _labelText == null) return;

            if (cooling)
            {
                int tenths = remainingSeconds >= 0f
                    ? Mathf.Max(1, Mathf.CeilToInt(remainingSeconds * 10f))
                    : -1;
                if (tenths != _lastCooldownTenths)
                {
                    _lastCooldownTenths = tenths;
                    _labelText.text = tenths > 0 ? $"{tenths / 10f:0.0}s" : "冷却";
                }
            }
            else if (_lastCooldownTenths != int.MinValue)
            {
                _lastCooldownTenths = int.MinValue;
                _labelText.text = _defaultLabelText;
            }

            // 文字更新后统一上色：按下=墨黑、冷却=黄、常态=纸白（颜色硬切，不补间）
            ApplyContentColors(_isHeld);
        }

        /// <summary>投掷被拒（冷却中按下）的硬切反馈：墨边闪红 + scale 0.97，0.2s 后由 Update 复原（0.1s 回弹）。</summary>
        public void NotifyDenied()
        {
            _denyFlashUntil = Time.unscaledTime + DenyFlashSeconds;
            if (_ringImage != null) _ringImage.color = DenyFlashColor;
            if (_rectTransform != null)
            {
                _pressReboundT = 1f;
                _rectTransform.localScale = Vector3.one * (_baseScale * ComicStyle.PressScale);
            }
        }

        private void TickDenyFlash()
        {
            if (_denyFlashUntil <= 0f) return;
            if (Time.unscaledTime < _denyFlashUntil) return;

            _denyFlashUntil = 0f;
            if (_ringImage != null)
            {
                _ringImage.color = GetIdleRingColor();
            }
            if (_rectTransform != null && !_isHeld)
            {
                // 拒绝抖动结束后同样走 0.1s Cubic-Out，不做瞬时跳回
                _pressReboundT = 0f;
            }
        }

        private Color GetDefaultBgColor()
        {
            // 大招就绪：整块翻功能色实底做「就绪强调」（硬切高亮，无呼吸缩放）
            if (IsSpecialReadyVisual) return _baseColor;

            // 未就绪 / 冷却中 / 常态一律回到墨黑半透明底
            return ActionFill;
        }

        public void ResetState()
        {
            _isHeld = false;
            _tapped = false;
            _released = false;
            _isAimingHeld = false;
            HasDragAim = false;
            _lookDelta = Vector2.zero;
            DragAimValue = Vector2.zero;
            _hasSpecialState = false;
            _lastChargePercentInt = -1;
            _activePointerId = int.MinValue;
            _denyFlashUntil = 0f;
            _cooldown01 = -1f; // 强制下一次 UpdateBombCooldown01 生效
            _lastCooldownTenths = int.MinValue;

            if (_contentContainer != null) _contentContainer.anchoredPosition = Vector2.zero;
            if (_shadowImage != null) _shadowImage.enabled = false;
            if (_rectTransform != null) _rectTransform.localScale = Vector3.one * _baseScale;
            _pressReboundT = 1f;
            if (_cooldownFillImage != null) _cooldownFillImage.enabled = false;
            if (_bgImage != null) _bgImage.color = GetDefaultBgColor();
            if (_ringImage != null) _ringImage.color = GetIdleRingColor();
            ApplyContentColors(pressed: false);
            if (_labelText != null && _mode == MobileButtonMode.BombTapOrHoldAim)
            {
                _labelText.text = _defaultLabelText;
                _labelText.color = ComicStyle.Paper;
            }
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

            // 只做颜色/文字的硬切；缩放完全交给按压反馈（满能不再呼吸缩放）
            if (isReady)
            {
                _canvasGroup.alpha = _baseAlpha;
                if (_labelText != null && (!_hasSpecialState || !_lastReadyState))
                {
                    _labelText.text = _defaultLabelText; // 按钮装不下「大招就绪」，就绪态沿用「大招」
                }
            }
            else
            {
                _canvasGroup.alpha = _baseAlpha * 0.55f;
                if (_labelText != null && (!_hasSpecialState || _lastReadyState || _lastChargePercentInt != chargePct))
                {
                    _labelText.text = $"{chargePct}%";
                }
            }

            _hasSpecialState = true;
            _lastReadyState = isReady;
            _lastChargePercentInt = chargePct;

            // 状态位更新后重新落一次颜色（就绪/未就绪的功能色/纸白/墨黑切换是硬切）
            if (_isHeld) ApplyContentColors(pressed: true);
            else ApplyIdleVisual();
        }
    }
}
