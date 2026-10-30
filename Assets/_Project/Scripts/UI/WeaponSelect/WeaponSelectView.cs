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
    /// 战前武器选择面板总视图（P8 选枪系统，P10 漫画风重构）：
    /// - 独立 Canvas，CanvasScaler 1920×1080 / match 0.5 / 支持 Safe Area / sortingOrder 15；
    /// - 屏幕左侧抽屉（桌面 620px，移动端 84% 屏宽且最大 720px），绝不遮挡中央「开始比赛」按钮；
    /// - 7 张主武器卡片（124px 高）：图标、中文(26px)+英文(14px)、5段粗块属性条、队伍色填充与漫画墨印；
    /// - 标题对话气泡（庆科黄油体 32px）+ 176×72 粗边展开/收起按钮；
    /// - 彻底修复中文字体方块字问题：文本统一经 UIThemeSO.ResolveFont 获取 CJK 字体；
    /// - 支持鼠标/触摸点击、数字键 1~7、方向键上下/左右循环切换、Enter 确认、Esc 收起/展开抽屉。
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
        private UIJellyButton _toggleJelly;

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
        private float _drawerWidth = 620f;
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

            // 主菜单构图优先：抽屉默认收起（左缘黄色「武器整备 >」页签常驻），
            // 展开时会压住居中的标题贴纸，按需点开再选武器；键盘 1~7 也会自动展开。
            SetDrawerOpen(false);
        }

        private float CalculateDrawerWidth()
        {
            // 桌面 620px；触屏固定 720px（约等于手机横屏逻辑宽度的 1/3，
            // 既放大卡片点击区，又不压住居中的开赛按钮）。
            if (Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive)
            {
                return 720f;
            }
            return 620f;
        }

        private void BuildCanvasAndHierarchy()
        {
            if (_built) return;
            _built = true;

            // 触屏模式：真机，或编辑器里勾选 showTouchUiInEditor 的模拟
            bool touch = Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;

            _drawerWidth = CalculateDrawerWidth();

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

            // 3. 左侧抽屉容器 (620px 宽，移动端 720px)
            var drawerGo = new GameObject("DrawerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            drawerGo.transform.SetParent(_safeAreaRoot, false);
            _drawerRoot = drawerGo.GetComponent<RectTransform>();
            _drawerRoot.anchorMin = new Vector2(0f, 0f);
            _drawerRoot.anchorMax = new Vector2(0f, 1f);
            _drawerRoot.pivot = new Vector2(0f, 0.5f);
            _drawerRoot.sizeDelta = new Vector2(_drawerWidth, -48f);
            _drawerRoot.anchoredPosition = new Vector2(24f, 0f);

            _drawerBg = drawerGo.GetComponent<UIInkShapeGraphic>();
            _drawerBg.Shape = UIInkShape.RoundedRect;
            _drawerBg.CornerRadius = ComicStyle.CornerSharp;
            _drawerBg.BorderWidth = ComicStyle.Border;
            _drawerBg.BorderColor = ComicStyle.Ink;
            _drawerBg.color = ComicStyle.Paper;
            _drawerBg.HardShadow = true;
            _drawerBg.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            _drawerBg.ShadowColor = ComicStyle.Ink;
            _drawerBg.Halftone = true;
            _drawerBg.HalftoneAlpha = ComicStyle.HalftoneAlpha;
            _drawerBg.GlossAlpha = 0f;
            _drawerBg.DrawGloss = false;
            _drawerBg.raycastTarget = true;

            // 4. 抽屉内 Header：标题对话气泡（庆科黄油体 32px）
            var headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(_drawerRoot, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -12f);
            headerRect.sizeDelta = new Vector2(-28f, 76f);

            // 标题对话气泡底图
            var bubbleGo = new GameObject("TitleBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bubbleGo.transform.SetParent(headerGo.transform, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchorMin = new Vector2(0f, 0.5f);
            bubbleRt.anchorMax = new Vector2(0f, 0.5f);
            bubbleRt.pivot = new Vector2(0f, 0.5f);
            bubbleRt.anchoredPosition = new Vector2(8f, 10f);
            bubbleRt.sizeDelta = new Vector2(360f, 52f);

            var bubbleShape = bubbleGo.GetComponent<UIInkShapeGraphic>();
            bubbleShape.Shape = UIInkShape.SpeechBubble;
            bubbleShape.CornerRadius = 24f;
            bubbleShape.BorderWidth = ComicStyle.BorderThin;
            bubbleShape.BorderColor = ComicStyle.Ink;
            bubbleShape.color = ComicStyle.Paper;
            bubbleShape.HardShadow = true;
            bubbleShape.ShadowOffset = new Vector2(3f, -3f);
            bubbleShape.ShadowColor = ComicStyle.Ink;
            bubbleShape.SpeechTail = true;
            bubbleShape.TailSize = new Vector2(20f, 14f);
            bubbleShape.TailAngle = 270f;
            bubbleShape.GlossAlpha = 0f;
            bubbleShape.DrawGloss = false;

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(bubbleGo.transform, false);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(12f, 0f);
            titleRect.offsetMax = new Vector2(-12f, 0f);
            _headerTitle = titleGo.GetComponent<TextMeshProUGUI>();
            // 不设字体就会出现方块字：必须使用 theme.ResolveFont 解析 CJK 黄油体
            _headerTitle.font = theme.ResolveFont(true, "武器整备 · ARSENAL");
            _headerTitle.fontSize = 32f;
            _headerTitle.fontStyle = FontStyles.Bold;
            _headerTitle.alignment = TextAlignmentOptions.Center;
            _headerTitle.color = ComicStyle.Ink;
            _headerTitle.text = "武器整备 · ARSENAL";
            _headerTitle.raycastTarget = false;

            var subGo = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            subGo.transform.SetParent(headerGo.transform, false);
            var subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0f);
            subRect.pivot = new Vector2(0f, 0f);
            subRect.anchoredPosition = new Vector2(12f, 4f);
            subRect.sizeDelta = new Vector2(-80f, 22f);
            _headerSubtitle = subGo.GetComponent<TextMeshProUGUI>();
            // 触屏设备没有 1~7 键，文案换成点击说明
            string subtitleText = touch
                ? "选择本局出战武器 · 点击卡片切换"
                : "选择本局出战武器 · 按 1~7 快速切换";
            _headerSubtitle.font = theme.ResolveFont(false, subtitleText);
            _headerSubtitle.fontSize = touch ? 18f : 16f;
            _headerSubtitle.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.8f);
            _headerSubtitle.text = subtitleText;
            _headerSubtitle.raycastTarget = false;

            // Header 内部收起小按钮
            var closeGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            closeGo.transform.SetParent(headerGo.transform, false);
            var closeRect = closeGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(0f, 6f);
            closeRect.sizeDelta = new Vector2(64f, 42f);

            var closeBg = closeGo.GetComponent<UIInkShapeGraphic>();
            closeBg.Shape = UIInkShape.RoundedRect;
            closeBg.CornerRadius = ComicStyle.CornerSharp;
            closeBg.BorderWidth = ComicStyle.BorderThin;
            closeBg.BorderColor = ComicStyle.Ink;
            closeBg.color = ComicStyle.PaperAlt;
            closeBg.HardShadow = true;
            closeBg.ShadowOffset = new Vector2(2f, -2f);
            closeBg.ShadowColor = ComicStyle.Ink;

            var closeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            closeTextGo.transform.SetParent(closeGo.transform, false);
            var ctr = closeTextGo.GetComponent<RectTransform>();
            ctr.anchorMin = Vector2.zero;
            ctr.anchorMax = Vector2.one;
            ctr.sizeDelta = Vector2.zero;
            var closeTxt = closeTextGo.GetComponent<TextMeshProUGUI>();
            closeTxt.font = theme.ResolveFont(false, "收起");
            closeTxt.alignment = TextAlignmentOptions.Center;
            closeTxt.fontSize = Application.isMobilePlatform ? 19f : 16f;
            closeTxt.fontStyle = FontStyles.Bold;
            closeTxt.color = ComicStyle.Ink;
            closeTxt.text = "收起";
            closeTxt.raycastTarget = false;

            _closeButton = closeGo.GetComponent<Button>();
            _closeButton.targetGraphic = closeBg;
            _closeButton.transition = Selectable.Transition.None;
            _closeButton.onClick.AddListener(() => SetDrawerOpen(false));

            var closeJelly = closeGo.GetComponent<UIJellyButton>();
            closeJelly.Configure(theme, _closeButton, closeBg);

            // 5. 卡片列表容器 (带滑动支持)
            var scrollGo = new GameObject("CardScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_drawerRoot, false);
            var scrollRectTransform = scrollGo.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(14f, 42f);
            scrollRectTransform.offsetMax = new Vector2(-14f, -96f);

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

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
            vlg.padding = new RectOffset(6, 6, 8, 8);
            vlg.spacing = 10f;
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
            footerRect.anchoredPosition = new Vector2(0f, 12f);
            footerRect.sizeDelta = new Vector2(-28f, 26f);

            var footerText = footerGo.GetComponent<TextMeshProUGUI>();
            // 触屏设备隐藏键盘快捷键说明，换成点击操作提示
            string footerLabel = touch
                ? "点击卡片选择 · 点「收起」返回主菜单"
                : "Enter 确认 · 1~7/上下键 切换 · Esc 收起抽屉";
            footerText.font = theme.ResolveFont(false, footerLabel);
            footerText.alignment = TextAlignmentOptions.Center;
            footerText.fontSize = touch ? 17f : 14f;
            footerText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.6f);
            footerText.text = footerLabel;
            footerText.raycastTarget = false;

            // 7. 抽屉开关按钮（规范尺寸 176×72，带 6px 墨线边框与 UIJellyButton 墨印反馈）
            BuildDrawerToggleButton();
        }

        private void BuildDrawerToggleButton()
        {
            var btnGo = new GameObject("DrawerToggleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(UIJellyButton));
            btnGo.transform.SetParent(_safeAreaRoot, false);
            _toggleButtonRoot = btnGo.GetComponent<RectTransform>();
            _toggleButtonRoot.anchorMin = new Vector2(0f, 0.5f);
            _toggleButtonRoot.anchorMax = new Vector2(0f, 0.5f);
            _toggleButtonRoot.pivot = new Vector2(0f, 0.5f);
            _toggleButtonRoot.sizeDelta = new Vector2(176f, 72f);
            _toggleButtonRoot.anchoredPosition = new Vector2(16f, 0f);

            _toggleButtonBg = btnGo.GetComponent<UIInkShapeGraphic>();
            _toggleButtonBg.Shape = UIInkShape.RoundedRect;
            _toggleButtonBg.CornerRadius = ComicStyle.CornerSharp;
            _toggleButtonBg.BorderWidth = ComicStyle.Border;
            _toggleButtonBg.BorderColor = ComicStyle.Ink;
            _toggleButtonBg.color = ComicStyle.Yellow;
            _toggleButtonBg.HardShadow = true;
            _toggleButtonBg.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            _toggleButtonBg.ShadowColor = ComicStyle.Ink;
            _toggleButtonBg.Halftone = false;
            _toggleButtonBg.raycastTarget = true;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(btnGo.transform, false);
            var tr = txtGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            _toggleButtonText = txtGo.GetComponent<TextMeshProUGUI>();
            _toggleButtonText.font = theme.ResolveFont(true, "武器库 [1-7]");
            _toggleButtonText.fontSize = Application.isMobilePlatform ? 24f : 20f;
            _toggleButtonText.fontStyle = FontStyles.Bold;
            _toggleButtonText.alignment = TextAlignmentOptions.Center;
            _toggleButtonText.color = ComicStyle.Ink;
            _toggleButtonText.text = "武器库 [1-7]";
            _toggleButtonText.raycastTarget = false;

            _toggleMotion = btnGo.GetComponent<UISpringMotion>();
            _toggleMotion.SetTheme(theme);

            _toggleButton = btnGo.GetComponent<Button>();
            _toggleButton.targetGraphic = _toggleButtonBg;
            _toggleButton.transition = Selectable.Transition.None;
            _toggleButton.onClick.AddListener(() => SetDrawerOpen(!_isDrawerOpen));

            _toggleJelly = btnGo.GetComponent<UIJellyButton>();
            _toggleJelly.Configure(theme, _toggleButton, _toggleButtonBg);

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
            _targetDrawerX = open ? 24f : -(_drawerWidth + 40f);
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
                // 抽屉展开时不显示外挂切换块：Header 里已有「收起」按钮，
                // 屏幕中间再凸一块既挡内容也不符合主菜单排版（P11 用户反馈）。
                _toggleButtonRoot.gameObject.SetActive(false);
            }
            else
            {
                // 抽屉收起时，页签贴屏幕左缘（不再悬空），漫画明黄色醒目
                _toggleButtonRoot.gameObject.SetActive(true);
                _toggleButtonRoot.anchoredPosition = new Vector2(0f, 0f);
                if (_toggleButtonText != null) _toggleButtonText.text = "武器整备 >";
                if (_toggleButtonBg != null) _toggleButtonBg.color = ComicStyle.Yellow;
                if (_toggleButtonText != null) _toggleButtonText.color = ComicStyle.Ink;
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
