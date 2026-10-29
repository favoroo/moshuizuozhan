using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 战前武器选择面板总视图（P8 选枪系统）：
    /// - 独立 Canvas，CanvasScaler 1920×1080 / match 0.5 / 支持 Safe Area / sortingOrder 15；
    /// - 屏幕左侧抽屉（宽度 480px），绝不遮挡 Ready 面板正中央的「开始比赛」按钮；
    /// - 7 张主武器卡片：图标、中英文名、属性（伤害/射程/射速/耗墨/大招）、队伍色选中态与果冻弹簧；
    /// - 支持鼠标/触摸点击、数字键 1~7、方向键上下/左右循环切换、Enter 确认、Esc 收起/展开抽屉；
    /// - 持久化到 PlayerPrefs，非法值钳制回 Shooter。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class WeaponSelectView : MonoBehaviour
    {
        public static WeaponSelectView Instance { get; private set; }

        [SerializeField] private UIThemeSO theme;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private RectTransform _safeAreaRoot;
        private RectTransform _drawerRoot;
        private UIInkShapeGraphic _drawerBg;

        private RectTransform _toggleButtonRoot;
        private Button _toggleButton;
        private UIInkShapeGraphic _toggleButtonBg;
        private TextMeshProUGUI _toggleButtonText;
        private UISpringMotion _toggleMotion;

        private TextMeshProUGUI _headerTitle;
        private TextMeshProUGUI _headerSubtitle;
        private Button _closeButton;

        private readonly List<WeaponCardView> _cards = new List<WeaponCardView>();
        private WeaponConfigSO[] _configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
        private Sprite[] _icons = new Sprite[WeaponSelectMath.WeaponCount];

        private int _selectedIndex;
        private Team _currentTeam = Team.TeamA;
        private bool _isDrawerOpen = true;
        private float _currentDrawerX = 24f;
        private float _targetDrawerX = 24f;
        private bool _built;

        public event Action<int, WeaponConfigSO> OnWeaponSelected;

        public int SelectedIndex => _selectedIndex;
        public WeaponConfigSO SelectedConfig => _configs != null && _selectedIndex >= 0 && _selectedIndex < _configs.Length
            ? _configs[_selectedIndex]
            : null;
        public bool IsDrawerOpen => _isDrawerOpen;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            theme = UIThemeSO.Resolve(theme);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Configure(UIThemeSO themeAsset, WeaponConfigSO[] configs, Sprite[] icons, int initialIndex = 0)
        {
            theme = UIThemeSO.Resolve(themeAsset);

            if (configs != null && configs.Length == WeaponSelectMath.WeaponCount)
            {
                _configs = configs;
            }
            if (icons != null && icons.Length == WeaponSelectMath.WeaponCount)
            {
                _icons = icons;
            }

            BuildCanvasAndHierarchy();
            PopulateCards();

            int clamped = WeaponSelectMath.ClampIndex(initialIndex);
            SelectWeapon(clamped, silent: true);
        }

        private void BuildCanvasAndHierarchy()
        {
            if (_built) return;
            _built = true;

            // 1. Canvas 基础配置
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 15; // 高于 HUD (0~5) 与 MatchFlow (10)

            _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            _scaler.matchWidthOrHeight = 0.5f;

            _raycaster = GetComponent<GraphicRaycaster>();
            if (_raycaster == null) _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            // 2. Safe Area 容器
            var safeGo = new GameObject("SafeAreaContainer", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);
            _safeAreaRoot = safeGo.GetComponent<RectTransform>();
            _safeAreaRoot.anchorMin = Vector2.zero;
            _safeAreaRoot.anchorMax = Vector2.one;
            _safeAreaRoot.sizeDelta = Vector2.zero;
            ApplySafeArea();

            // 3. 左侧抽屉容器 (480px 宽)
            var drawerGo = new GameObject("DrawerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            drawerGo.transform.SetParent(_safeAreaRoot, false);
            _drawerRoot = drawerGo.GetComponent<RectTransform>();
            _drawerRoot.anchorMin = new Vector2(0f, 0f);
            _drawerRoot.anchorMax = new Vector2(0f, 1f);
            _drawerRoot.pivot = new Vector2(0f, 0.5f);
            _drawerRoot.sizeDelta = new Vector2(480f, -48f);
            _drawerRoot.anchoredPosition = new Vector2(24f, 0f);

            _drawerBg = drawerGo.GetComponent<UIInkShapeGraphic>();
            _drawerBg.Shape = UIInkShape.RoundedRect;
            _drawerBg.CornerRadius = 28f;
            _drawerBg.BorderWidth = 6.5f;
            _drawerBg.BorderColor = theme.outlineColor;
            _drawerBg.color = theme.panelColor;
            _drawerBg.raycastTarget = true;

            // 4. 抽屉内 Header
            var headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(_drawerRoot, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -12f);
            headerRect.sizeDelta = new Vector2(-28f, 68f);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(headerGo.transform, false);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(8f, 0f);
            titleRect.sizeDelta = new Vector2(-60f, 38f);
            _headerTitle = titleGo.GetComponent<TextMeshProUGUI>();
            _headerTitle.font = theme.GetFont(true);
            _headerTitle.fontSize = 28f;
            _headerTitle.fontStyle = FontStyles.Bold;
            _headerTitle.color = theme.readyGold;
            _headerTitle.text = "武器整备 · ARSENAL";
            _headerTitle.raycastTarget = false;

            var subGo = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            subGo.transform.SetParent(headerGo.transform, false);
            var subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0f);
            subRect.pivot = new Vector2(0f, 0f);
            subRect.anchoredPosition = new Vector2(8f, 4f);
            subRect.sizeDelta = new Vector2(-60f, 24f);
            _headerSubtitle = subGo.GetComponent<TextMeshProUGUI>();
            _headerSubtitle.font = theme.GetFont(false);
            _headerSubtitle.fontSize = 13.5f;
            _headerSubtitle.color = theme.textMuted;
            _headerSubtitle.text = "选择本局出战武器 · 按 1~7 快速切换";
            _headerSubtitle.raycastTarget = false;

            // 收起按钮
            var closeGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion));
            closeGo.transform.SetParent(headerGo.transform, false);
            var closeRect = closeGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(0f, 0f);
            closeRect.sizeDelta = new Vector2(50f, 34f);

            var closeBg = closeGo.GetComponent<UIInkShapeGraphic>();
            closeBg.Shape = UIInkShape.RoundedRect;
            closeBg.CornerRadius = 8f;
            closeBg.BorderWidth = 2.5f;
            closeBg.BorderColor = theme.outlineColor;
            closeBg.color = new Color(0.18f, 0.16f, 0.25f, 0.95f);

            var closeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            closeTextGo.transform.SetParent(closeGo.transform, false);
            var ctr = closeTextGo.GetComponent<RectTransform>();
            ctr.anchorMin = Vector2.zero;
            ctr.anchorMax = Vector2.one;
            ctr.sizeDelta = Vector2.zero;
            var closeTxt = closeTextGo.GetComponent<TextMeshProUGUI>();
            closeTxt.font = theme.GetFont(false);
            closeTxt.alignment = TextAlignmentOptions.Center;
            closeTxt.fontSize = 13f;
            closeTxt.fontStyle = FontStyles.Bold;
            closeTxt.color = Color.white;
            closeTxt.text = "收起";
            closeTxt.raycastTarget = false;

            _closeButton = closeGo.GetComponent<Button>();
            _closeButton.transition = Selectable.Transition.None;
            _closeButton.onClick.AddListener(() => SetDrawerOpen(false));

            // 5. 卡片列表容器 (带滑动支持)
            var scrollGo = new GameObject("CardScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_drawerRoot, false);
            var scrollRectTransform = scrollGo.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(14f, 42f);
            scrollRectTransform.offsetMax = new Vector2(-14f, -86f);

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRect;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);
            scroll.content = contentRect;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 6. 底部提示行 (Footer)
            var footerGo = new GameObject("Footer", typeof(RectTransform), typeof(TextMeshProUGUI));
            footerGo.transform.SetParent(_drawerRoot, false);
            var footerRect = footerGo.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 10f);
            footerRect.sizeDelta = new Vector2(-28f, 26f);

            var footerText = footerGo.GetComponent<TextMeshProUGUI>();
            footerText.font = theme.GetFont(false);
            footerText.alignment = TextAlignmentOptions.Center;
            footerText.fontSize = 12f;
            footerText.color = theme.textMuted;
            footerText.text = "Enter 确认 · 1~7/↑↓ 切换 · Esc 收起抽屉";
            footerText.raycastTarget = false;

            // 7. 抽屉开关按钮（浮动在左边缘，收起时清晰可见）
            BuildDrawerToggleButton();
        }

        private void BuildDrawerToggleButton()
        {
            var btnGo = new GameObject("DrawerToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion));
            btnGo.transform.SetParent(_safeAreaRoot, false);
            _toggleButtonRoot = btnGo.GetComponent<RectTransform>();
            _toggleButtonRoot.anchorMin = new Vector2(0f, 0.5f);
            _toggleButtonRoot.anchorMax = new Vector2(0f, 0.5f);
            _toggleButtonRoot.pivot = new Vector2(0f, 0.5f);
            _toggleButtonRoot.sizeDelta = new Vector2(148f, 52f);
            _toggleButtonRoot.anchoredPosition = new Vector2(16f, 0f);

            _toggleButtonBg = btnGo.GetComponent<UIInkShapeGraphic>();
            _toggleButtonBg.Shape = UIInkShape.RoundedRect;
            _toggleButtonBg.CornerRadius = 14f;
            _toggleButtonBg.BorderWidth = 4f;
            _toggleButtonBg.BorderColor = theme.outlineColor;
            _toggleButtonBg.color = theme.readyGold;
            _toggleButtonBg.raycastTarget = true;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(btnGo.transform, false);
            var tr = txtGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            _toggleButtonText = txtGo.GetComponent<TextMeshProUGUI>();
            _toggleButtonText.font = theme.GetFont(true);
            _toggleButtonText.fontSize = 15f;
            _toggleButtonText.fontStyle = FontStyles.Bold;
            _toggleButtonText.alignment = TextAlignmentOptions.Center;
            _toggleButtonText.color = theme.outlineColor;
            _toggleButtonText.text = "武器库 [1-7]";
            _toggleButtonText.raycastTarget = false;

            _toggleMotion = btnGo.GetComponent<UISpringMotion>();
            _toggleMotion.SetTheme(theme);

            _toggleButton = btnGo.GetComponent<Button>();
            _toggleButton.transition = Selectable.Transition.None;
            _toggleButton.onClick.AddListener(() => SetDrawerOpen(!_isDrawerOpen));

            UpdateToggleButtonState();
        }

        private void PopulateCards()
        {
            var contentRect = _drawerRoot.Find("CardScroll/Viewport/Content");
            if (contentRect == null) return;

            // 清理旧卡片
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null) Destroy(_cards[i].gameObject);
            }
            _cards.Clear();

            // 生成 7 张武器卡片
            for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                WeaponConfigSO cfg = (_configs != null && i < _configs.Length) ? _configs[i] : null;
                Sprite icon = (_icons != null && i < _icons.Length) ? _icons[i] : null;

                var cardGo = new GameObject($"Card_{i}_{kind}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(WeaponCardView));
                cardGo.transform.SetParent(contentRect, false);

                var cardView = cardGo.GetComponent<WeaponCardView>();
                cardView.Initialize(i, kind, cfg, icon, theme, _currentTeam, HandleCardClicked);
                _cards.Add(cardView);
            }
        }

        private void HandleCardClicked(int index)
        {
            SelectWeapon(index);
        }

        public void SelectWeapon(int index, bool silent = false)
        {
            int clamped = WeaponSelectMath.ClampIndex(index);
            _selectedIndex = clamped;
            WeaponSelectMath.SaveSelectedWeaponIndex(clamped);

            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetSelected(i == clamped, animate: !silent);
                }
            }

            if (!silent)
            {
                PlaySelectSound();
                OnWeaponSelected?.Invoke(clamped, SelectedConfig);
            }
        }

        public void SelectRelative(int delta)
        {
            int next = (_selectedIndex + delta) % WeaponSelectMath.WeaponCount;
            if (next < 0) next += WeaponSelectMath.WeaponCount;
            SelectWeapon(next);
            if (!_isDrawerOpen)
            {
                SetDrawerOpen(true);
            }
        }

        public void SetDrawerOpen(bool open)
        {
            _isDrawerOpen = open;
            _targetDrawerX = open ? 24f : -520f;
            UpdateToggleButtonState();

            if (open && _toggleMotion != null)
            {
                _toggleMotion.Punch(0.06f);
            }
        }

        private void UpdateToggleButtonState()
        {
            if (_toggleButtonRoot == null) return;

            if (_isDrawerOpen)
            {
                // 抽屉展开时，按钮移到抽屉外缘右侧跟随
                _toggleButtonRoot.anchoredPosition = new Vector2(494f, 0f);
                if (_toggleButtonText != null) _toggleButtonText.text = "◀ 收起";
                if (_toggleButtonBg != null) _toggleButtonBg.color = new Color(0.18f, 0.16f, 0.25f, 0.90f);
                if (_toggleButtonText != null) _toggleButtonText.color = Color.white;
            }
            else
            {
                // 抽屉收起时，按钮吸附在屏幕左侧，金黄色醒目
                _toggleButtonRoot.anchoredPosition = new Vector2(16f, 0f);
                if (_toggleButtonText != null) _toggleButtonText.text = "▶ 武器库 [1-7]";
                if (_toggleButtonBg != null) _toggleButtonBg.color = theme.readyGold;
                if (_toggleButtonText != null) _toggleButtonText.color = theme.outlineColor;
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void SetTeam(Team team)
        {
            _currentTeam = team;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i] != null)
                {
                    _cards[i].SetTeam(team);
                }
            }
        }

        private void PlaySelectSound()
        {
            if (AudioManager.Instance != null && AudioManager.Instance.uiClickClip != null)
            {
                AudioManager.Instance.PlayOne(AudioManager.Instance.uiClickClip, 0.75f, 0.96f, 1.05f);
            }
        }

        private void ApplySafeArea()
        {
            if (_safeAreaRoot == null) return;
            Rect safe = Screen.safeArea;
            Vector2 min = safe.position;
            Vector2 max = min + safe.size;

            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _safeAreaRoot.anchorMin = min;
            _safeAreaRoot.anchorMax = max;
            _safeAreaRoot.offsetMin = Vector2.zero;
            _safeAreaRoot.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            // 抽屉平滑滑入滑出
            if (!Mathf.Approximately(_currentDrawerX, _targetDrawerX))
            {
                _currentDrawerX = Mathf.Lerp(_currentDrawerX, _targetDrawerX, Time.unscaledDeltaTime * 18f);
                if (Mathf.Abs(_currentDrawerX - _targetDrawerX) < 0.5f)
                {
                    _currentDrawerX = _targetDrawerX;
                }
                if (_drawerRoot != null)
                {
                    _drawerRoot.anchoredPosition = new Vector2(_currentDrawerX, 0f);
                }
            }

            // 只在 Ready 阶段且激活状态响应快捷键输入
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase != MatchManager.Phase.Ready)
            {
                return;
            }

            HandleInput();
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 键盘数字键 1~7 直接换枪（主键盘 + 小键盘）
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SelectWeapon(0);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SelectWeapon(1);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SelectWeapon(2);
            else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) SelectWeapon(3);
            else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) SelectWeapon(4);
            else if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) SelectWeapon(5);
            else if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) SelectWeapon(6);

            // 方向键选枪
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
            {
                SelectRelative(-1);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                SelectRelative(1);
            }

            // Enter 确认
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                SelectWeapon(_selectedIndex);
            }

            // Esc 键开关抽屉
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetDrawerOpen(!_isDrawerOpen);
            }
        }
    }
}
