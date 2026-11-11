using System;
using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// [P36] 阵亡整备条（复活换枪）运行时装配视图：屏幕底部居中的一条 7 槽武器条。
    ///
    /// 为什么是「死亡时换枪」而不是「复活后换枪」：
    /// - 复活后换枪会让对枪中途凭空换武器，破坏「阵亡 = 代价」的节奏；
    /// - 死亡 4 秒是纯等待窗口，把选择权交回来既填补空窗，也让「这把枪打不过」有即时出口；
    /// - 代价是**清空大招充能**（<see cref="WeaponController.EquipWeapon(WeaponConfigSO, bool)"/>），
    ///   否则「死一次换把克制的枪」会变成无成本的最优解。
    ///
    /// 视觉沿用 P19 波次 3b 的几何大胆风：纸白面板 + 4px 墨框 + 弹窗硬影，
    /// 选中槽 = 行动黄填充 + 加粗墨框（不靠颜色区分色相，色弱也能读），
    /// 槽内只放「图标 + 中文名」；阵亡窗口时间紧，不再展示武器定位/四栏读数，
    /// 底部只留一行小字代价提示（换枪清空大招充能）。
    ///
    /// 生命周期：由 <see cref="WeaponSelectBootstrap"/> 在 DDOL 画布上创建并驱动；
    /// 视图本身不知道比赛阶段/生命值，只负责「显示 + 选择」，门禁逻辑在
    /// <see cref="RespawnLoadoutMath"/> 与 Bootstrap 里。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class RespawnLoadoutView : MonoBehaviour
    {
        /// <summary>淡入/滑入时长（秒，unscaled）。死亡演出里不能抢戏，160ms 以内。</summary>
        private const float FadeDuration = 0.14f;

        /// <summary>收起时向屏幕下方滑出的距离（参考画布像素）。</summary>
        private const float HiddenSlide = 36f;

        /// <summary>选中槽的压印放大倍率。</summary>
        private const float SelectedScale = 1.06f;

        // 桌面 / 移动端两套显式尺寸（移动端整套更大，而不是只放大字号）。
        private const float DesktopPanelMargin = 320f;
        private const float DesktopPanelHeight = 196f;
        private const float DesktopBottomOffset = 32f;
        private const float DesktopPadding = 24f;
        private const float DesktopSlotGap = 12f;
        private const float DesktopSlotHeight = 102f;
        private const float DesktopIconSize = 56f;
        private const float DesktopNameFont = 20f;

        private const float MobilePanelMargin = 60f;
        private const float MobilePanelHeight = 256f;
        private const float MobileBottomOffset = 24f;
        private const float MobilePadding = 28f;
        private const float MobileSlotGap = 16f;
        private const float MobileSlotHeight = 142f;
        private const float MobileIconSize = 72f;
        private const float MobileNameFont = 24f;

        private UIThemeSO _theme;
        private WeaponConfigSO[] _configs = new WeaponConfigSO[RespawnLoadoutMath.SlotCount];
        private Sprite[] _icons = new Sprite[RespawnLoadoutMath.SlotCount];

        private RectTransform _safeAreaRoot;
        private RectTransform _panelRoot;
        private CanvasGroup _canvasGroup;
        private Rect _lastSafeArea;
        private bool _mobile;
        private bool _built;

        private TextMeshProUGUI _countdownText;
        /// <summary>倒计时文本上一次的 0.1s 档位：先比档位再决定是否构造字符串（每帧调用零 GC）。</summary>
        private int _lastCountdownKey = int.MinValue;
        private readonly UIInkShapeGraphic[] _slotBackgrounds = new UIInkShapeGraphic[RespawnLoadoutMath.SlotCount];
        private readonly Image[] _slotIcons = new Image[RespawnLoadoutMath.SlotCount];
        private readonly TextMeshProUGUI[] _slotNames = new TextMeshProUGUI[RespawnLoadoutMath.SlotCount];
        private readonly RectTransform[] _slotRects = new RectTransform[RespawnLoadoutMath.SlotCount];

        private int _selectedIndex;
        private bool _visible;
        private float _visible01;

        /// <summary>玩家点选某把武器（index 已清洗，config 可能为 null：资产缺失时由 Bootstrap 兜底）。</summary>
        public event Action<int, WeaponConfigSO> OnWeaponSelected;

        public int SelectedIndex => _selectedIndex;
        public bool IsVisible => _visible;
        public WeaponConfigSO SelectedConfig =>
            _selectedIndex >= 0 && _selectedIndex < _configs.Length ? _configs[_selectedIndex] : null;

        /// <summary>运行时装配（幂等）：重复 Configure 只刷新数据，不重建层级。</summary>
        public void Configure(UIThemeSO themeAsset, WeaponConfigSO[] configs, Sprite[] icons)
        {
            _theme = UIThemeSO.Resolve(themeAsset);
            _configs = configs ?? new WeaponConfigSO[RespawnLoadoutMath.SlotCount];
            _icons = icons ?? new Sprite[RespawnLoadoutMath.SlotCount];
            _mobile = UITypography.IsMobileLike;

            if (!_built)
            {
                BuildCanvas();
                _built = true;
            }

            RefreshSlotData();
            ApplySelectionVisuals();
            SetVisible(false);
        }

        // ==================== 层级装配 ====================

        private void BuildCanvas()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 24 = 高于移动端触控层（MobileTouchUI 20），低于暂停菜单（100）：
            // 死亡期间整备条是可交互层，不能被摇杆/开火键盖住。
            canvas.sortingOrder = 24;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            // 1. 安全区容器：底部条必须避开刘海/手势条，和武器抽屉共用同一套换算。
            var safeGo = new GameObject("SafeAreaContainer", typeof(RectTransform));
            safeGo.transform.SetParent(transform, false);
            _safeAreaRoot = safeGo.GetComponent<RectTransform>();
            ApplySafeArea();

            // 2. 纸白面板 + 墨框 + 弹窗硬影
            float panelMargin = _mobile ? MobilePanelMargin : DesktopPanelMargin;
            float panelHeight = _mobile ? MobilePanelHeight : DesktopPanelHeight;
            float bottomOffset = _mobile ? MobileBottomOffset : DesktopBottomOffset;
            float padding = _mobile ? MobilePadding : DesktopPadding;
            float slotGap = _mobile ? MobileSlotGap : DesktopSlotGap;

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            panelGo.transform.SetParent(_safeAreaRoot, false);
            _panelRoot = panelGo.GetComponent<RectTransform>();
            _panelRoot.anchorMin = new Vector2(0f, 0f);
            _panelRoot.anchorMax = new Vector2(1f, 0f);
            _panelRoot.pivot = new Vector2(0.5f, 0f);
            _panelRoot.anchoredPosition = new Vector2(0f, bottomOffset);
            _panelRoot.sizeDelta = new Vector2(-panelMargin * 2f, panelHeight);

            var panelBg = panelGo.GetComponent<UIInkShapeGraphic>();
            panelBg.Shape = UIInkShape.RoundedRect; // 圆角化：复活换枪面板自适应圆角
            panelBg.BorderWidth = ComicStyle.Border;
            panelBg.BorderColor = ComicStyle.Ink;
            panelBg.color = ComicStyle.Paper;
            panelBg.Halftone = false;
            panelBg.GlossAlpha = 0f;
            panelBg.DrawGloss = false;
            panelBg.raycastTarget = true;
            _theme.ApplyHardShadow(panelBg, UIHardShadowLevel.Dialog);

            // 3. 标题（左）+ 复活倒计时（右）
            float headerTop = _mobile ? 16f : 14f;
            float headerHeight = _mobile ? 46f : 40f;
            var headerGo = new GameObject("Header", typeof(RectTransform));
            headerGo.transform.SetParent(panelGo.transform, false);
            var headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -headerTop);
            headerRect.sizeDelta = new Vector2(-padding * 2f, headerHeight);

            string titleLabel = "阵亡整备";
            var titleText = CreateText(
                headerGo.transform, "Title", _theme.ResolveFont(true, titleLabel),
                _mobile ? 38f : 34f, TextAlignmentOptions.Left, ComicStyle.Ink);
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.6f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            titleText.text = titleLabel;

            _countdownText = CreateText(
                headerGo.transform, "Countdown", _theme.ResolveFont(false, "复活倒计时 0.0s"),
                _mobile ? 28f : 24f, TextAlignmentOptions.Right, ComicStyle.WithAlpha(ComicStyle.Ink, 0.78f));
            var countdownRect = _countdownText.rectTransform;
            countdownRect.anchorMin = new Vector2(0.4f, 0f);
            countdownRect.anchorMax = new Vector2(1f, 1f);
            countdownRect.offsetMin = Vector2.zero;
            countdownRect.offsetMax = Vector2.zero;
            _countdownText.text = RespawnLoadoutMath.FormatCountdown(0f);

            // 4. 7 个武器槽：HorizontalLayoutGroup 均分宽度，安全区/分辨率变化自动重排
            float slotsTop = _mobile ? 68f : 58f;
            var slotsGo = new GameObject("Slots", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            slotsGo.transform.SetParent(panelGo.transform, false);
            var slotsRect = slotsGo.GetComponent<RectTransform>();
            slotsRect.anchorMin = new Vector2(0f, 1f);
            slotsRect.anchorMax = new Vector2(1f, 1f);
            slotsRect.pivot = new Vector2(0.5f, 1f);
            slotsRect.anchoredPosition = new Vector2(0f, -slotsTop);
            slotsRect.sizeDelta = new Vector2(-padding * 2f, _mobile ? MobileSlotHeight : DesktopSlotHeight);

            var layout = slotsGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = slotGap;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.padding = new RectOffset(0, 0, 0, 0);

            for (int i = 0; i < RespawnLoadoutMath.SlotCount; i++)
            {
                BuildSlot(slotsGo.transform, i);
            }

            // 5. 底部一行代价提示：整备条只保留武器槽，换枪代价用最小号灰字居中兜底
            float hintBottom = _mobile ? 10f : 8f;
            float hintHeight = _mobile ? 28f : 20f;
            var hintText = CreateText(
                panelGo.transform, "Hint", _theme.ResolveFont(false, RespawnLoadoutMath.SwapCostHint),
                _mobile ? 20f : 18f, TextAlignmentOptions.Center, ComicStyle.WithAlpha(ComicStyle.Ink, 0.5f));
            var hintRect = hintText.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, hintBottom);
            hintRect.sizeDelta = new Vector2(-padding * 2f, hintHeight);
            hintText.text = RespawnLoadoutMath.SwapCostHint;

            // 槽高 = 面板高 - 标题区 - 代价提示，用锚点撑满而不是写死，移动端只改两套常量
            slotsRect.sizeDelta = new Vector2(
                -padding * 2f,
                panelHeight - slotsTop - hintBottom - hintHeight - 8f);
        }

        private void BuildSlot(Transform parent, int index)
        {
            var slotGo = new GameObject(
                $"Slot{index}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic),
                typeof(Button), typeof(LayoutElement));
            slotGo.transform.SetParent(parent, false);

            var rect = slotGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1f, 1f);

            var layoutElement = slotGo.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.minWidth = 0f;

            var background = slotGo.GetComponent<UIInkShapeGraphic>();
            background.Shape = UIInkShape.RoundedRect; // 圆角化：换枪槽位自适应圆角
            background.BorderWidth = ComicStyle.BorderThin;
            background.BorderColor = ComicStyle.Ink;
            background.color = ComicStyle.PaperAlt;
            background.Halftone = false;
            background.GlossAlpha = 0f;
            background.DrawGloss = false;
            background.raycastTarget = true;
            _theme.ApplyHardShadow(background, UIHardShadowLevel.Button);

            var button = slotGo.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.82f, 1f);
            colors.pressedColor = new Color(0.92f, 0.88f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            colors.fadeDuration = ComicStyle.PressDuration;
            button.colors = colors;
            int captured = index;
            button.onClick.AddListener(() => SelectIndex(captured, playSound: true));

            float iconSize = _mobile ? MobileIconSize : DesktopIconSize;
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(slotGo.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, _mobile ? -12f : -10f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            var iconImage = iconGo.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.enabled = false;

            var nameText = CreateText(
                slotGo.transform, "Name", _theme.ResolveFont(false, "喷射小枪"),
                _mobile ? MobileNameFont : DesktopNameFont, TextAlignmentOptions.Center, ComicStyle.Ink);
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.anchoredPosition = new Vector2(0f, _mobile ? 10f : 8f);
            nameRect.sizeDelta = new Vector2(0f, _mobile ? 30f : 24f);

            _slotRects[index] = rect;
            _slotBackgrounds[index] = background;
            _slotIcons[index] = iconImage;
            _slotNames[index] = nameText;
        }

        private static TextMeshProUGUI CreateText(
            Transform parent, string name, TMP_FontAsset font, float size,
            TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = UITypography.FontSnapped(size);
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        // ==================== 数据刷新 ====================

        private void RefreshSlotData()
        {
            if (!_built) return;

            for (int i = 0; i < RespawnLoadoutMath.SlotCount; i++)
            {
                WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                if (_slotNames[i] != null)
                {
                    _slotNames[i].text = WeaponSelectMath.GetWeaponChineseName(kind);
                }

                Sprite icon = i < _icons.Length ? _icons[i] : null;
                if (_slotIcons[i] != null)
                {
                    _slotIcons[i].sprite = icon;
                    // 图标缺失时隐藏 Image，绝不出现白块（AGENTS.md 硬规则）。
                    _slotIcons[i].enabled = icon != null;
                }
            }
        }

        private void ApplySelectionVisuals()
        {
            if (!_built) return;

            for (int i = 0; i < RespawnLoadoutMath.SlotCount; i++)
            {
                if (_slotBackgrounds[i] == null) continue;

                bool selected = i == _selectedIndex;
                _slotBackgrounds[i].color = selected ? ComicStyle.Yellow : ComicStyle.PaperAlt;
                _slotBackgrounds[i].BorderWidth = selected ? ComicStyle.Border : ComicStyle.BorderThin;
                _slotBackgrounds[i].BorderColor = ComicStyle.Ink;
            }
        }

        // ==================== 对外状态 ====================

        /// <summary>显示/隐藏整备条；隐藏时淡出并滑出屏幕，完全透明后停用对象。</summary>
        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;

            _visible = visible;
            if (visible)
            {
                gameObject.SetActive(true);
                _canvasGroup.blocksRaycasts = true;
            }
            else
            {
                _canvasGroup.blocksRaycasts = false;
            }
        }

        /// <summary>只更新倒计时文本（Bootstrap 每帧调用；相同文案不重排）。</summary>
        public void SetCountdown(float seconds)
        {
            if (_countdownText == null) return;

            // 先按 0.1s 档位判等：旧版每帧都先 FormatCountdown（插值 + 装箱）再比较文案，
            // 阵亡整备的 4~8 秒里持续产生 GC；现在只有档位变化才构造字符串。
            int key = seconds <= RespawnLoadoutMath.ImminentRespawnSeconds
                ? -2
                : Mathf.CeilToInt(seconds * 10f);
            if (key == _lastCountdownKey) return;
            _lastCountdownKey = key;

            string label = RespawnLoadoutMath.FormatCountdown(seconds);
            if (!string.Equals(_countdownText.text, label, StringComparison.Ordinal))
            {
                _countdownText.text = label;
            }
        }

        /// <summary>外部同步当前装备（第一次阵亡时以玩家手里的枪为初始选项），不派发选择事件。</summary>
        public void SetSelectedIndex(int index)
        {
            int clamped = RespawnLoadoutMath.ClampIndex(index);
            if (clamped == _selectedIndex)
            {
                ApplySelectionVisuals();
                return;
            }

            _selectedIndex = clamped;
            ApplySelectionVisuals();
        }

        /// <summary>选中某把武器。点击与外部调用共用同一入口；<paramref name="notify"/> 为 false 时只刷新视觉。</summary>
        public void SelectIndex(int index, bool playSound = true, bool notify = true)
        {
            int clamped = RespawnLoadoutMath.ClampIndex(index);
            bool changed = clamped != _selectedIndex;
            _selectedIndex = clamped;
            ApplySelectionVisuals();

            if (playSound && changed)
            {
                PlaySelectSound();
            }

            if (notify)
            {
                OnWeaponSelected?.Invoke(_selectedIndex, SelectedConfig);
            }
        }

        private void PlaySelectSound()
        {
            if (AudioManager.Instance != null)
            {
                // 走 AudioManager 的统一节流：直接 PlayOne 会绕过 30ms 窗口，连点叠成爆音
                AudioManager.Instance.PlayWeaponCardSelect();
            }
        }

        // ==================== 动效与安全区 ====================

        private void Update()
        {
            Rect safe = Screen.safeArea;
            if (safe != _lastSafeArea)
            {
                ApplySafeArea();
            }

            float target = _visible ? 1f : 0f;
            if (!Mathf.Approximately(_visible01, target))
            {
                _visible01 = Mathf.MoveTowards(_visible01, target, Time.unscaledDeltaTime / FadeDuration);
                ApplyVisualState();
            }
            else if (!_visible && _visible01 <= 0.001f && gameObject.activeSelf)
            {
                // 完全收起后停用：既省掉每帧 Layout，又保证不出现在截图/射线里。
                gameObject.SetActive(false);
            }

            // 选中槽的压印回弹：0ms 起手、指数逼近，和武器卡的 select snap 同一手感。
            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (_slotRects[i] == null) continue;
                float targetScale = i == _selectedIndex ? SelectedScale : 1f;
                float current = _slotRects[i].localScale.x;
                if (!Mathf.Approximately(current, targetScale))
                {
                    float next = Mathf.Lerp(current, targetScale, Time.unscaledDeltaTime * 24f);
                    if (Mathf.Abs(next - targetScale) < 0.002f)
                    {
                        next = targetScale;
                    }
                    _slotRects[i].localScale = new Vector3(next, next, 1f);
                }
            }
        }

        private void ApplyVisualState()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _visible01;
            }

            if (_panelRoot != null)
            {
                float bottomOffset = _mobile ? MobileBottomOffset : DesktopBottomOffset;
                _panelRoot.anchoredPosition = new Vector2(0f, bottomOffset - (1f - _visible01) * HiddenSlide);
            }
        }

        private void ApplySafeArea()
        {
            if (_safeAreaRoot == null) return;

            Rect safe = Screen.safeArea;
            _lastSafeArea = safe;
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
    }
}
