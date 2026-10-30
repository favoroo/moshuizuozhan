using System;
using System.Collections.Generic;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端按键自定义布局编辑器（几何大胆风 Geometric Bold P14）：
    /// 1. 进入编辑模式后游戏自动暂停，退出时严格还原进入前的 Time.timeScale；
    /// 2. 根节点全屏背板 raycastTarget=false，杜绝吞掉按键 PointerDown；
    /// 3. 为每个可配置按键挂载透明命中代理层，并在编辑态解开摇杆 blocksRaycasts 阻断；
    /// 4. 坐标拖拽采用 (startPos + delta) 增量运算，并按父级 SafeArea 尺寸做 Clamp（留出半宽半高边距）；
    /// 5. 选中项展示 2px 黑白描边 + 四角方块几何标记与实时读数角标；
    /// 6. 面板采用直角黑底白字纯色大色块（无阴影无网点），字号整体放大（标题≥24/移动≥30，标签读数≥22/移动≥26）；
    /// 7. 额外提供 2/3/4 指方案、左开火键、开火转视角、固定摇杆与辅助瞄准档位，手动编辑会把方案标记为自定义。
    /// </summary>
    public class MobileLayoutCustomizer : MonoBehaviour
    {
        private MobileTouchUI _touchUI;
        private RectTransform _safeArea;
        private GameObject _overlayRoot;
        private RectTransform _inspectorCard;
        private TextMeshProUGUI _inspectorTitle;
        private TouchSlider _scaleSlider;
        private TextMeshProUGUI _scaleValueText;
        private TouchSlider _alphaSlider;
        private TextMeshProUGUI _alphaValueText;

        // 选中项视觉反馈（4px 墨色框 + 实时读数角标）
        private RectTransform _selectionHighlight;
        private RectTransform _readoutBadge;
        private TextMeshProUGUI _readoutText;

        private MobileConfigurableItem _selectedItem;
        private readonly List<MobileConfigurableItem> _items = new List<MobileConfigurableItem>();
        private readonly Dictionary<string, bool> _originalBlocksRaycasts = new Dictionary<string, bool>();
        private MobileLayoutConfig _workingConfig;
        private float _previousTimeScale = 1f;

        // 方案与辅助卡片控件
        private readonly TextMeshProUGUI[] _presetTexts = new TextMeshProUGUI[3];
        private readonly UIInkShapeGraphic[] _presetShapes = new UIInkShapeGraphic[3];
        private Button _leftFireToggle;
        private TextMeshProUGUI _leftFireText;
        private UIInkShapeGraphic _leftFireShape;
        private Button _fireRotateToggle;
        private TextMeshProUGUI _fireRotateText;
        private UIInkShapeGraphic _fireRotateShape;
        private Button _fixedJoystickToggle;
        private TextMeshProUGUI _fixedJoystickText;
        private UIInkShapeGraphic _fixedJoystickShape;
        private readonly Button[] _aimLevelButtons = new Button[4];
        private readonly TextMeshProUGUI[] _aimLevelTexts = new TextMeshProUGUI[4];
        private readonly UIInkShapeGraphic[] _aimLevelShapes = new UIInkShapeGraphic[4];
        private bool _subscribedSettings;

        /// <summary>当前打开的布局编辑器（全局单例）；供暂停菜单判断编辑态。</summary>
        public static MobileLayoutCustomizer ActiveEditor { get; private set; }

        public static bool IsEditingAny => ActiveEditor != null && ActiveEditor.IsEditing;

        // 拖拽坐标增量计算状态
        private Vector2 _startPointerLocal;
        private Vector2 _startAnchoredPosition;
        private Vector2 _startLocalPosition;
        private bool _isDragging;

        public bool IsEditing { get; private set; }
        public event Action OnClosed;

        public static MobileLayoutCustomizer Create(MobileTouchUI touchUI, RectTransform safeArea, Sprite circleRingSprite)
        {
            var go = new GameObject("MobileLayoutCustomizer", typeof(RectTransform));
            // 挂在 SafeArea 之内而不是画布根：顶栏右侧的「保存/关闭」按钮在刘海横屏时
            // 会被挖孔/圆角切掉；全屏背板本来就是 raycastTarget=false，不影响命中判断。
            go.transform.SetParent(safeArea, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var customizer = go.AddComponent<MobileLayoutCustomizer>();
            customizer._touchUI = touchUI;
            customizer._safeArea = safeArea;
            customizer.BuildUI(circleRingSprite);
            go.SetActive(false);

            return customizer;
        }

        public void RegisterItem(string id, string displayName, RectTransform target, CanvasGroup group, Vector2 defaultPos, float defaultScale, float defaultAlpha)
        {
            var item = new MobileConfigurableItem
            {
                id = id,
                displayName = displayName,
                target = target,
                group = group,
                defaultPos = defaultPos,
                defaultScale = defaultScale,
                defaultAlpha = defaultAlpha
            };
            _items.Add(item);

            // 1. 确保 target 本身挂载 LayoutDragProxy
            var rootProxy = target.gameObject.GetComponent<LayoutDragProxy>();
            if (rootProxy == null) rootProxy = target.gameObject.AddComponent<LayoutDragProxy>();
            rootProxy.Init(this, item);

            // 2. 为目标生成与自身同尺寸的透明命中层（HitProxy），确保 raycastTarget=true 且不拦截子视图渲染。
            //    默认隐藏：只有在编辑模式下才激活，避免它抢走玩法期按键的 PointerDown（见 MobileConfigurableItem.hitProxy 注释）。
            var hitProxyGo = new GameObject("HitProxy", typeof(RectTransform), typeof(Image));
            hitProxyGo.transform.SetParent(target, false);
            var hitRt = hitProxyGo.GetComponent<RectTransform>();
            hitRt.anchorMin = Vector2.zero;
            hitRt.anchorMax = Vector2.one;
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;

            var hitImg = hitProxyGo.GetComponent<Image>();
            hitImg.color = Color.clear;
            hitImg.raycastTarget = true; // 编辑模式下提供整块按键的可拖拽区域

            var proxy = hitProxyGo.AddComponent<LayoutDragProxy>();
            proxy.Init(this, item);

            item.hitProxy = hitProxyGo;
            hitProxyGo.SetActive(false);
        }

        /// <summary>按 id 取回已注册项，供外部绑定按钮/摇杆的自定义缩放与透明度回调。</summary>
        public MobileConfigurableItem GetItem(string id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null && _items[i].id == id)
                    return _items[i];
            }
            return null;
        }

        private void BuildUI(Sprite circleRingSprite)
        {
            _overlayRoot = gameObject;
            var theme = UIThemeSO.Resolve(null);

            // 1. 全屏半透明暗色背景遮罩（必须 raycastTarget = false，绝不能吞底层射线）
            var bgImg = gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.04f, 0.06f, 0.70f);
            bgImg.raycastTarget = false; // 【关键修复 1】：背板不拦截射线

            // 2. 顶部控制栏（几何大胆风：直角纯黑底 + 4px 纸白实线边框）
            var topBar = CreateComicPanel(transform, "TopBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -48f), new Vector2(0f, 96f),
                ComicStyle.Ink, ComicStyle.Border, false);

            var titleText = CreateTextMesh("Title", topBar.transform, "操作与按键设置", UITypography.FontSnapped(26f), ComicStyle.Paper,
                theme.ResolveFont(true, "操作与按键设置"), TextAlignmentOptions.Left);
            var titleRt = titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(28f, 14f);
            titleRt.sizeDelta = new Vector2(400f, 36f);

            var subText = CreateTextMesh("Subtitle", topBar.transform, "点选按键拖动/缩放 · 切换方案会覆盖当前位置 · 调整后点保存", UITypography.FontSnapped(18f), ComicStyle.PaperAlt,
                theme.ResolveFont(false, "点选按键拖动缩放切换方案会覆盖当前位置"), TextAlignmentOptions.Left);
            var subRt = subText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(28f, -20f);
            subRt.sizeDelta = new Vector2(560f, 28f);

            // 控制栏右侧三键：恢复默认 (220x72)、保存完成 (220x72)、关闭 (72x72)
            CreateComicButton(topBar.transform, "ResetBtn", "恢复默认",
                new Vector2(-550f, 0f), new Vector2(220f, 72f),
                ComicStyle.PaperAlt, ComicStyle.Ink, UITypography.FontSnapped(22f), () => ResetToDefault());

            CreateComicButton(topBar.transform, "SaveBtn", "保存完成",
                new Vector2(-310f, 0f), new Vector2(220f, 72f),
                ComicStyle.Green, ComicStyle.Ink, UITypography.FontSnapped(22f), () => Close(save: true));

            CreateComicButton(topBar.transform, "CloseBtn", "X",
                new Vector2(-70f, 0f), new Vector2(72f, 72f),
                ComicStyle.Red, ComicStyle.Paper, UITypography.FontSnapped(28f), () => Close(save: false));

            // 3. 浮动属性调节卡片（中上部：直角纯黑底 + 4px 白边，无阴影无网点）
            _inspectorCard = CreateComicPanel(transform, "InspectorCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -170f), new Vector2(800f, 120f),
                ComicStyle.Ink, ComicStyle.Border, false);

            _inspectorTitle = CreateTextMesh("InspectorTitle", _inspectorCard, "请点击任意按键开始调整", UITypography.FontSnapped(24f), ComicStyle.Paper,
                theme.ResolveFont(true, "请点击任意按键开始调整"), TextAlignmentOptions.Left);
            var inspTitleRt = _inspectorTitle.rectTransform;
            inspTitleRt.anchorMin = new Vector2(0f, 0.5f);
            inspTitleRt.anchorMax = new Vector2(0f, 0.5f);
            inspTitleRt.pivot = new Vector2(0f, 0.5f);
            inspTitleRt.anchoredPosition = new Vector2(24f, 32f);
            inspTitleRt.sizeDelta = new Vector2(500f, 34f);

            // 大小调节 Slider
            var scaleLabel = CreateTextMesh("ScaleLabel", _inspectorCard, "大小:", UITypography.FontSnapped(22f), ComicStyle.Paper,
                theme.ResolveFont(false, "大小:"), TextAlignmentOptions.Left);
            var scaleLabelRt = scaleLabel.rectTransform;
            scaleLabelRt.anchorMin = new Vector2(0f, 0.5f);
            scaleLabelRt.anchorMax = new Vector2(0f, 0.5f);
            scaleLabelRt.pivot = new Vector2(0f, 0.5f);
            scaleLabelRt.anchoredPosition = new Vector2(24f, -16f);
            scaleLabelRt.sizeDelta = new Vector2(70f, 32f);

            var scaleSliderGo = new GameObject("ScaleSlider", typeof(RectTransform));
            scaleSliderGo.transform.SetParent(_inspectorCard, false);
            var scaleRt = scaleSliderGo.GetComponent<RectTransform>();
            scaleRt.anchorMin = new Vector2(0f, 0.5f);
            scaleRt.anchorMax = new Vector2(0f, 0.5f);
            scaleRt.pivot = new Vector2(0f, 0.5f);
            scaleRt.anchoredPosition = new Vector2(100f, -16f);
            scaleRt.sizeDelta = new Vector2(180f, 26f);

            _scaleSlider = TouchSlider.Create(scaleSliderGo, MobileLayoutConfig.MinScale, MobileLayoutConfig.MaxScale, 1.0f);
            _scaleSlider.OnValueChanged += OnScaleSliderChanged;

            _scaleValueText = CreateTextMesh("ScaleValue", _inspectorCard, "100%", UITypography.FontSnapped(22f), ComicStyle.Yellow,
                theme.ResolveFont(false, "100%"), TextAlignmentOptions.Left);
            var scaleValRt = _scaleValueText.rectTransform;
            scaleValRt.anchorMin = new Vector2(0f, 0.5f);
            scaleValRt.anchorMax = new Vector2(0f, 0.5f);
            scaleValRt.pivot = new Vector2(0f, 0.5f);
            scaleValRt.anchoredPosition = new Vector2(290f, -16f);
            scaleValRt.sizeDelta = new Vector2(80f, 32f);

            // 透明度调节 Slider
            var alphaLabel = CreateTextMesh("AlphaLabel", _inspectorCard, "透明度:", UITypography.FontSnapped(22f), ComicStyle.Paper,
                theme.ResolveFont(false, "透明度:"), TextAlignmentOptions.Left);
            var alphaLabelRt = alphaLabel.rectTransform;
            alphaLabelRt.anchorMin = new Vector2(0f, 0.5f);
            alphaLabelRt.anchorMax = new Vector2(0f, 0.5f);
            alphaLabelRt.pivot = new Vector2(0f, 0.5f);
            alphaLabelRt.anchoredPosition = new Vector2(380f, -16f);
            alphaLabelRt.sizeDelta = new Vector2(95f, 32f);

            var alphaSliderGo = new GameObject("AlphaSlider", typeof(RectTransform));
            alphaSliderGo.transform.SetParent(_inspectorCard, false);
            var alphaRt = alphaSliderGo.GetComponent<RectTransform>();
            alphaRt.anchorMin = new Vector2(0f, 0.5f);
            alphaRt.anchorMax = new Vector2(0f, 0.5f);
            alphaRt.pivot = new Vector2(0f, 0.5f);
            alphaRt.anchoredPosition = new Vector2(485f, -16f);
            alphaRt.sizeDelta = new Vector2(180f, 26f);

            _alphaSlider = TouchSlider.Create(alphaSliderGo, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha, 1.0f);
            _alphaSlider.OnValueChanged += OnAlphaSliderChanged;

            _alphaValueText = CreateTextMesh("AlphaValue", _inspectorCard, "90%", UITypography.FontSnapped(22f), ComicStyle.Yellow,
                theme.ResolveFont(false, "90%"), TextAlignmentOptions.Left);
            var alphaValRt = _alphaValueText.rectTransform;
            alphaValRt.anchorMin = new Vector2(0f, 0.5f);
            alphaValRt.anchorMax = new Vector2(0f, 0.5f);
            alphaValRt.pivot = new Vector2(0f, 0.5f);
            alphaValRt.anchoredPosition = new Vector2(675f, -16f);
            alphaValRt.sizeDelta = new Vector2(80f, 32f);

            // 4. 方案与辅助设置卡片（2/3/4 指、左开火、开火转视角、固定摇杆、辅助瞄准）
            BuildAuxCard(theme);

            // 5. 选中项 2px 纯黑描边 + 四角方块几何标记 + 实时读数角标
            BuildSelectionVisual(transform);
        }

        /// <summary>
        /// 方案与辅助卡片：2/3/4 指方案、左开火键、开火转视角、固定摇杆、辅助瞄准档位。
        /// 小按钮统一使用左锚点排布，避免依赖运行时父级尺寸。
        /// </summary>
        private void BuildAuxCard(UIThemeSO theme)
        {
            var card = CreateComicPanel(transform, "AuxCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -315f), new Vector2(860f, 160f),
                ComicStyle.Ink, ComicStyle.Border, false);

            var font = theme.ResolveFont(false, "方案与辅助辅助瞄准关弱中强");
            var title = CreateTextMesh("AuxTitle", card, "方案与辅助（3/4 指）", UITypography.FontSnapped(24f), ComicStyle.Paper,
                theme.ResolveFont(true, "方案与辅助指"), TextAlignmentOptions.Left);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(24f, 54f);
            titleRt.sizeDelta = new Vector2(500f, 32f);

            // Row 1: 方案 + 左开火
            var presetLabel = CreateTextMesh("PresetLabel", card, "方案:", UITypography.FontSnapped(22f), ComicStyle.Paper, font, TextAlignmentOptions.Left);
            var presetLabelRt = presetLabel.rectTransform;
            presetLabelRt.anchorMin = new Vector2(0f, 0.5f);
            presetLabelRt.anchorMax = new Vector2(0f, 0.5f);
            presetLabelRt.pivot = new Vector2(0f, 0.5f);
            presetLabelRt.anchoredPosition = new Vector2(24f, 18f);
            presetLabelRt.sizeDelta = new Vector2(70f, 38f);

            var presetValues = new[] { MobileLayoutPreset.TwoFinger, MobileLayoutPreset.ThreeFinger, MobileLayoutPreset.FourFinger };
            for (int i = 0; i < presetValues.Length; i++)
            {
                int index = i;
                var preset = presetValues[i];
                var btn = CreateAuxButton(card, $"Preset_{preset}", MobileLayoutPresets.GetLabel(preset),
                    new Vector2(100f + i * 105f, 18f), new Vector2(95f, 42f), UITypography.FontSnapped(22f), font,
                    () => ApplyPreset(presetValues[index]));
                _presetShapes[i] = btn.GetComponentInChildren<UIInkShapeGraphic>();
                _presetTexts[i] = btn.GetComponentInChildren<TextMeshProUGUI>();
            }

            _leftFireToggle = CreateAuxButton(card, "LeftFireToggle", "左开火键：关",
                new Vector2(430f, 18f), new Vector2(190f, 42f), UITypography.FontSnapped(22f), font, ToggleLeftFire);
            _leftFireShape = _leftFireToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _leftFireText = _leftFireToggle.GetComponentInChildren<TextMeshProUGUI>();

            // Row 2: 开火转视角 / 固定摇杆 / 辅助瞄准
            _fireRotateToggle = CreateAuxButton(card, "FireRotateToggle", "开火转视角：开",
                new Vector2(24f, -34f), new Vector2(195f, 42f), UITypography.FontSnapped(22f), font, ToggleFireRotate);
            _fireRotateShape = _fireRotateToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _fireRotateText = _fireRotateToggle.GetComponentInChildren<TextMeshProUGUI>();

            _fixedJoystickToggle = CreateAuxButton(card, "FixedJoystickToggle", "固定摇杆：关",
                new Vector2(230f, -34f), new Vector2(180f, 42f), UITypography.FontSnapped(22f), font, ToggleFixedJoystick);
            _fixedJoystickShape = _fixedJoystickToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _fixedJoystickText = _fixedJoystickToggle.GetComponentInChildren<TextMeshProUGUI>();

            var aimLabel = CreateTextMesh("AimLabel", card, "辅助瞄准:", UITypography.FontSnapped(22f), ComicStyle.Paper, font, TextAlignmentOptions.Left);
            var aimLabelRt = aimLabel.rectTransform;
            aimLabelRt.anchorMin = new Vector2(0f, 0.5f);
            aimLabelRt.anchorMax = new Vector2(0f, 0.5f);
            aimLabelRt.pivot = new Vector2(0f, 0.5f);
            aimLabelRt.anchoredPosition = new Vector2(425f, -34f);
            aimLabelRt.sizeDelta = new Vector2(110f, 38f);

            for (int level = 0; level <= 3; level++)
            {
                int captured = level;
                var btn = CreateAuxButton(card, $"AimLevel_{level}", GameSettingsMath.GetAimAssistLevelLabel(level),
                    new Vector2(545f + level * 72f, -34f), new Vector2(65f, 42f), UITypography.FontSnapped(22f), font,
                    () => SetAimAssistLevel(captured));
                _aimLevelButtons[level] = btn;
                _aimLevelShapes[level] = btn.GetComponentInChildren<UIInkShapeGraphic>();
                _aimLevelTexts[level] = btn.GetComponentInChildren<TextMeshProUGUI>();
            }

            RefreshAuxButtons();
        }

        private static Button CreateAuxButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
            float fontSize, TMP_FontAsset font, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var visualGo = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            visualGo.transform.SetParent(go.transform, false);
            var visualRt = visualGo.GetComponent<RectTransform>();
            visualRt.anchorMin = Vector2.zero;
            visualRt.anchorMax = Vector2.one;
            visualRt.offsetMin = Vector2.zero;
            visualRt.offsetMax = Vector2.zero;

            var shape = visualGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.BorderThin;
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
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = CreateTextMesh("Label", go.transform, label, fontSize, ComicStyle.Ink, font, TextAlignmentOptions.Center);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btn;
        }

        private void BuildSelectionVisual(Transform parent)
        {
            var selGo = new GameObject("SelectionFrame", typeof(RectTransform), typeof(UIInkShapeGraphic));
            selGo.transform.SetParent(parent, false);
            _selectionHighlight = selGo.GetComponent<RectTransform>();
            _selectionHighlight.sizeDelta = new Vector2(160f, 160f);

            var shape = selGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 2f; // 纯黑白 2px 描边
            shape.BorderColor = ComicStyle.Ink;
            shape.color = Color.clear;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            // 选中框只画 2px 墨边，绝不能开硬阴影：硬阴影是整块实心墨色多边形，
            // 透明填充不会擦除它，会把当前选中的按键整个盖成黑色（测试断言）。
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.Halftone = false;
            shape.raycastTarget = false;

            // 几何大胆风：四角方块标记
            CreateCornerMarker(selGo.transform, "CornerTL", new Vector2(0f, 1f));
            CreateCornerMarker(selGo.transform, "CornerTR", new Vector2(1f, 1f));
            CreateCornerMarker(selGo.transform, "CornerBL", new Vector2(0f, 0f));
            CreateCornerMarker(selGo.transform, "CornerBR", new Vector2(1f, 0f));

            // 实时读数角标（位于边框顶部正上方）
            var badgeGo = new GameObject("ReadoutBadge", typeof(RectTransform), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(selGo.transform, false);
            _readoutBadge = badgeGo.GetComponent<RectTransform>();
            _readoutBadge.anchorMin = new Vector2(0.5f, 1f);
            _readoutBadge.anchorMax = new Vector2(0.5f, 1f);
            _readoutBadge.pivot = new Vector2(0.5f, 0f);
            _readoutBadge.anchoredPosition = new Vector2(0f, 10f);
            _readoutBadge.sizeDelta = new Vector2(200f, 36f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.SharpRect;
            badgeShape.CornerRadius = ComicStyle.CornerSharp;
            badgeShape.BorderWidth = 2f;
            badgeShape.BorderColor = ComicStyle.Ink;
            badgeShape.color = ComicStyle.Paper;
            badgeShape.GlossAlpha = 0f;
            badgeShape.WobbleAmplitude = 0f;
            badgeShape.HardShadow = false;
            badgeShape.ShadowOffset = Vector2.zero;
            badgeShape.Halftone = false;
            badgeShape.raycastTarget = false;

            _readoutText = CreateTextMesh("Text", badgeGo.transform, "X: 0, Y: 0 · 1.0x", UITypography.FontSnapped(18f), ComicStyle.Ink,
                null, TextAlignmentOptions.Center);
            var textRt = _readoutText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            selGo.SetActive(false);
        }

        private static void CreateCornerMarker(Transform parent, string name, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(8f, 8f);
            var img = go.GetComponent<Image>();
            img.color = ComicStyle.Ink;
            img.raycastTarget = false;
        }

        private void OnEnable()
        {
            if (!_subscribedSettings)
            {
                GameSettingsService.SettingsChanged += RefreshAuxButtons;
                _subscribedSettings = true;
            }
            RefreshAuxButtons();
        }

        private void OnDisable()
        {
            if (_subscribedSettings)
            {
                GameSettingsService.SettingsChanged -= RefreshAuxButtons;
                _subscribedSettings = false;
            }

            // 场景卸载/父节点被禁用时若仍在编辑，必须把时间尺度、命中层和摇杆输入全部还原，
            // 否则整局会卡在 timeScale=0，或编辑用 HitProxy 继续吞掉操作按钮。
            if (IsEditing)
            {
                IsEditing = false;
                Time.timeScale = _previousTimeScale;
                if (ActiveEditor == this) ActiveEditor = null;

                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    if (item != null && item.group != null && _originalBlocksRaycasts.TryGetValue(item.id, out bool orig))
                    {
                        item.group.blocksRaycasts = orig;
                    }
                    if (item != null && item.hitProxy != null)
                    {
                        item.hitProxy.SetActive(false);
                    }
                }

                if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
                _touchUI?.SetLayoutEditing(false);
                gameObject.SetActive(false);
            }
        }

        private void RefreshAuxButtons()
        {
            int preset = _workingConfig != null ? _workingConfig.preset : -1;
            for (int i = 0; i < _presetTexts.Length; i++)
            {
                bool selected = preset == i;
                if (_presetShapes[i] != null) _presetShapes[i].color = selected ? ComicStyle.Yellow : ComicStyle.Paper;
                if (_presetTexts[i] != null) _presetTexts[i].color = ComicStyle.Ink;
            }

            bool leftFire = _workingConfig != null
                ? _workingConfig.leftFireVisible
                : (_touchUI != null && _touchUI.IsLeftFireVisible);
            if (_leftFireText != null) _leftFireText.text = leftFire ? "左开火键：开" : "左开火键：关";
            if (_leftFireShape != null) _leftFireShape.color = leftFire ? ComicStyle.Yellow : ComicStyle.Paper;

            bool rotate = GameSettingsService.FireButtonRotatesCamera;
            if (_fireRotateText != null) _fireRotateText.text = rotate ? "开火转视角：开" : "开火转视角：关";
            if (_fireRotateShape != null) _fireRotateShape.color = rotate ? ComicStyle.Yellow : ComicStyle.Paper;

            bool fixedJoystick = GameSettingsService.FixedJoystick;
            if (_fixedJoystickText != null) _fixedJoystickText.text = fixedJoystick ? "固定摇杆：开" : "固定摇杆：关";
            if (_fixedJoystickShape != null) _fixedJoystickShape.color = fixedJoystick ? ComicStyle.Yellow : ComicStyle.Paper;

            int aimLevel = GameSettingsService.AimAssistLevel;
            for (int level = 0; level < _aimLevelButtons.Length; level++)
            {
                if (_aimLevelShapes[level] != null)
                    _aimLevelShapes[level].color = level == aimLevel ? ComicStyle.Yellow : ComicStyle.Paper;
                if (_aimLevelTexts[level] != null)
                    _aimLevelTexts[level].color = ComicStyle.Ink;
            }
        }

        private void ToggleLeftFire()
        {
            if (_workingConfig == null) _workingConfig = GetCurrentConfig();

            bool visible = !_workingConfig.leftFireVisible;
            _workingConfig.leftFireVisible = visible;
            _workingConfig.preset = -1;
            _touchUI?.SetLeftFireVisible(visible);

            if (!visible && _selectedItem != null && _selectedItem.id == "LeftFire")
            {
                SelectItem(GetItem("Fire"));
            }

            RefreshAuxButtons();
        }

        private void ToggleFireRotate()
        {
            GameSettingsService.FireButtonRotatesCamera = !GameSettingsService.FireButtonRotatesCamera;
            RefreshAuxButtons();
        }

        private void ToggleFixedJoystick()
        {
            GameSettingsService.FixedJoystick = !GameSettingsService.FixedJoystick;
            RefreshAuxButtons();
        }

        private void SetAimAssistLevel(int level)
        {
            GameSettingsService.AimAssistLevel = level;
            RefreshAuxButtons();
        }

        /// <summary>应用一套 2/3/4 指方案；方案是完整布局，立即写入存档。</summary>
        public void ApplyPreset(MobileLayoutPreset preset)
        {
            var config = _touchUI != null
                ? _touchUI.BuildPresetConfig(preset)
                : MobileLayoutPresets.Build(preset);

            ApplyConfig(config);
            MobileLayoutStorage.Save(config);
            _workingConfig = config.Clone();

            if (_selectedItem != null && !IsItemVisible(_selectedItem))
            {
                SelectItem(GetItem("Fire"));
            }
            else if (_selectedItem != null)
            {
                SelectItem(_selectedItem);
            }

            RefreshAuxButtons();
        }

        private bool IsItemVisible(MobileConfigurableItem item)
        {
            return item != null && (item.id != "LeftFire" || (_touchUI != null && _touchUI.IsLeftFireVisible));
        }

        public void Open()
        {
            if (IsEditing) return;
            IsEditing = true;
            ActiveEditor = this;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f; // 暂停游戏

            // 清空进入前的按压/滑屏状态，并暂停摇杆触摸，避免编辑时角色还在动。
            _touchUI?.ResetAllInputs();
            _touchUI?.SetLayoutEditing(true);

            // 【关键修复 2】：在编辑模式下为所有按键及摇杆打开射线阻挡，确保能接收点击与拖拽
            _originalBlocksRaycasts.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && item.group != null)
                {
                    _originalBlocksRaycasts[item.id] = item.group.blocksRaycasts;
                    item.group.blocksRaycasts = true;
                }

                // 编辑期才启用透明命中层，让整块按键（含透明圆角）都可拖拽
                if (item != null && item.hitProxy != null)
                {
                    item.hitProxy.SetActive(true);
                }
            }

            // 读档后必须 Apply 回按键：此前只读不应用，再次打开时滑块读到被动画重置的 1.0，
            // 任何拖动都会把 1.0 写回工作配置，导致已保存的自定义尺寸被静默覆盖。
            var saved = MobileLayoutStorage.Load();
            _workingConfig = saved != null ? saved.Clone() : GetCurrentConfig();
            if (saved != null)
            {
                ApplyConfig(saved);
            }

            gameObject.SetActive(true);
            RefreshAuxButtons();

            // 默认选中射击大键；若它被隐藏则回退到第一个可见项
            if (_items.Count > 0)
            {
                var fireItem = _items.Find(x => x.id == "Fire" && IsItemVisible(x))
                               ?? _items.Find(x => IsItemVisible(x))
                               ?? _items[0];
                SelectItem(fireItem);
            }
        }

        public void Close(bool save)
        {
            if (!IsEditing) return;

            if (save)
            {
                SaveCurrentWorkingConfig();
            }
            else
            {
                var saved = MobileLayoutStorage.Load();
                if (saved != null) ApplyConfig(saved);
                else ResetToDefault();
            }

            // 恢复所有可配置组件的原始 blocksRaycasts 状态（摇杆恢复为 false）
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && item.group != null && _originalBlocksRaycasts.TryGetValue(item.id, out bool orig))
                {
                    item.group.blocksRaycasts = orig;
                }

                // 关闭编辑后必须收回透明命中层，否则玩法期按键会被它吞掉 PointerDown
                if (item != null && item.hitProxy != null)
                {
                    item.hitProxy.SetActive(false);
                }
            }

            IsEditing = false;
            if (ActiveEditor == this) ActiveEditor = null;

            // 先按存档/当前值复位摇杆默认点与按压状态，再恢复编辑态外的手感。
            _touchUI?.ResetAllInputs();
            _touchUI?.SetLayoutEditing(false);

            Time.timeScale = _previousTimeScale; // 【关键修复 3】：还原进入前的值，绝不硬编码 1
            if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void SelectItem(MobileConfigurableItem item)
        {
            _selectedItem = item;
            if (item == null)
            {
                if (_inspectorTitle != null) _inspectorTitle.text = "请点击任意按键开始调整";
                if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
                return;
            }

            if (_inspectorTitle != null)
            {
                _inspectorTitle.text = $"正在调整：【{item.displayName}】";
            }

            float scale = ReadItemScale(item);
            _scaleSlider.SetValueWithoutNotify(scale);
            if (_scaleValueText != null) _scaleValueText.text = $"{Mathf.RoundToInt(scale * 100f)}%";

            float alpha = ReadItemAlpha(item);
            _alphaSlider.SetValueWithoutNotify(alpha);
            if (_alphaValueText != null) _alphaValueText.text = $"{Mathf.RoundToInt(alpha * 100f)}%";

            UpdateSelectionVisual(item);
        }

        private void UpdateSelectionVisual(MobileConfigurableItem item)
        {
            if (item == null || _selectionHighlight == null) return;

            _selectionHighlight.gameObject.SetActive(true);
            _selectionHighlight.position = item.target.position;
            float scale = ReadItemScale(item);
            _selectionHighlight.sizeDelta = item.target.sizeDelta * (scale * 1.08f);

            if (_readoutText != null)
            {
                int x = Mathf.RoundToInt(item.target.anchoredPosition.x);
                int y = Mathf.RoundToInt(item.target.anchoredPosition.y);
                _readoutText.text = $"({x}, {y}) · {scale:F1}x";
            }
        }

        private void OnScaleSliderChanged(float val) => SetSelectedScale(val);

        private void OnAlphaSliderChanged(float val) => SetSelectedAlpha(val);

        /// <summary>外部/测试直接设置选中项缩放；统一走 item 回调，按钮不会被动画重置回 1.0。</summary>
        public void SetSelectedScale(float val)
        {
            if (_selectedItem == null) return;

            val = Mathf.Clamp(val, MobileLayoutConfig.MinScale, MobileLayoutConfig.MaxScale);
            ApplyItemScale(_selectedItem, val);
            if (_scaleValueText != null) _scaleValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_scaleSlider != null) _scaleSlider.SetValueWithoutNotify(val);
            UpdateSelectionVisual(_selectedItem);
            UpdateWorkingConfigForItem(_selectedItem);
        }

        /// <summary>外部/测试直接设置选中项透明度；大招/摇杆的状态动画会在该基准上按比例叠加。</summary>
        public void SetSelectedAlpha(float val)
        {
            if (_selectedItem == null) return;

            val = Mathf.Clamp(val, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha);
            ApplyItemAlpha(_selectedItem, val);
            if (_alphaValueText != null) _alphaValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_alphaSlider != null) _alphaSlider.SetValueWithoutNotify(val);
            UpdateWorkingConfigForItem(_selectedItem);
        }

        public void OnItemPointerDown(MobileConfigurableItem item, PointerEventData eventData)
        {
            if (!IsEditing || item == null) return;
            SelectItem(item);

            var parentRt = item.target.parent as RectTransform;
            if (parentRt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRt, eventData.position, eventData.pressEventCamera, out Vector2 pointerLocal))
            {
                _startPointerLocal = pointerLocal;
                _startAnchoredPosition = item.target.anchoredPosition;
                _startLocalPosition = item.target.localPosition;
                _isDragging = true;
            }
        }

        public void OnItemPointerDrag(MobileConfigurableItem item, PointerEventData eventData)
        {
            if (!IsEditing || item == null) return;
            var parentRt = item.target.parent as RectTransform;
            if (parentRt == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRt, eventData.position, eventData.pressEventCamera, out Vector2 currentPointerLocal))
            {
                if (!_isDragging || _selectedItem != item)
                {
                    _startPointerLocal = currentPointerLocal;
                    _startAnchoredPosition = item.target.anchoredPosition;
                    _startLocalPosition = item.target.localPosition;
                    _isDragging = true;
                }

                ApplyClampedDrag(item, parentRt, currentPointerLocal - _startPointerLocal);
            }
        }

        public void OnItemPointerUp(MobileConfigurableItem item, PointerEventData eventData)
        {
            _isDragging = false;
            if (item != null)
            {
                UpdateWorkingConfigForItem(item);
            }
        }

        /// <summary>
        /// 公共 API 保留：外部或老用例直接调用拖拽时也能平滑响应与正确 Clamp。
        /// </summary>
        public void OnItemDragged(MobileConfigurableItem item, Vector2 screenPos, Camera cam)
        {
            if (!IsEditing || item == null) return;
            var parentRt = item.target.parent as RectTransform;
            if (parentRt == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenPos, cam, out Vector2 currentPointerLocal))
            {
                if (!_isDragging || _selectedItem != item)
                {
                    _startPointerLocal = currentPointerLocal;
                    _startAnchoredPosition = item.target.anchoredPosition;
                    _startLocalPosition = item.target.localPosition;
                    _isDragging = true;
                }

                ApplyClampedDrag(item, parentRt, currentPointerLocal - _startPointerLocal);
            }
        }

        private void ApplyClampedDrag(MobileConfigurableItem item, RectTransform parentRt, Vector2 pointerDelta)
        {
            // 【关键修复 3】直接在 anchoredPosition 空间做 Clamp。
            // 旧实现把「父级中心局部坐标」和「锚点偏移坐标」混用，锚点在右下角的按钮会被算到屏幕外。
            Vector2 desired = _startAnchoredPosition + pointerDelta;

            Vector2 pivot = item.target.pivot;
            float w = item.target.rect.width * item.target.localScale.x;
            float h = item.target.rect.height * item.target.localScale.y;

            Rect parentRect = parentRt.rect;

            // 锚点在父级局部空间的位置（anchorMin 通常等于 anchorMax）
            Vector2 anchorLocal = new Vector2(
                parentRect.xMin + item.target.anchorMin.x * parentRect.width,
                parentRect.yMin + item.target.anchorMin.y * parentRect.height);

            // 让整块目标都留在父级矩形内：两端各留出 pivot 对应的半宽/半高
            float minX = parentRect.xMin - anchorLocal.x + pivot.x * w;
            float maxX = parentRect.xMax - anchorLocal.x - (1f - pivot.x) * w;
            float minY = parentRect.yMin - anchorLocal.y + pivot.y * h;
            float maxY = parentRect.yMax - anchorLocal.y - (1f - pivot.y) * h;

            if (minX > maxX) { float mid = (minX + maxX) * 0.5f; minX = mid; maxX = mid; }
            if (minY > maxY) { float mid = (minY + maxY) * 0.5f; minY = mid; maxY = mid; }

            ApplyItemPosition(item, new Vector2(
                Mathf.Clamp(desired.x, minX, maxX),
                Mathf.Clamp(desired.y, minY, maxY)));

            UpdateSelectionVisual(item);
            UpdateWorkingConfigForItem(item);
        }

        private void UpdateWorkingConfigForItem(MobileConfigurableItem item)
        {
            if (_workingConfig == null) _workingConfig = new MobileLayoutConfig();
            _workingConfig.Set(item.id, item.target.anchoredPosition, ReadItemScale(item), ReadItemAlpha(item));
            // 任何手动拖动/缩放/透明度调整都让当前布局脱离预设，方案按钮不再高亮。
            _workingConfig.preset = -1;
        }

        private static void ApplyItemPosition(MobileConfigurableItem item, Vector2 pos)
        {
            if (item == null || item.target == null) return;
            item.target.anchoredPosition = pos;
            item.applyPosition?.Invoke(pos);
        }

        private static void ApplyItemScale(MobileConfigurableItem item, float scale)
        {
            if (item == null || item.target == null) return;
            float clamped = Mathf.Clamp(scale, MobileLayoutConfig.MinScale, MobileLayoutConfig.MaxScale);
            if (item.applyScale != null)
                item.applyScale(clamped);
            else
                item.target.localScale = Vector3.one * clamped;
        }

        private static void ApplyItemAlpha(MobileConfigurableItem item, float alpha)
        {
            if (item == null) return;
            float clamped = Mathf.Clamp(alpha, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha);
            if (item.applyAlpha != null)
                item.applyAlpha(clamped);
            else if (item.group != null)
                item.group.alpha = clamped;
        }

        private static float ReadItemScale(MobileConfigurableItem item)
        {
            if (item == null) return 1f;
            return item.readScale != null ? item.readScale() : item.target.localScale.x;
        }

        private static float ReadItemAlpha(MobileConfigurableItem item)
        {
            if (item == null) return 1f;
            if (item.readAlpha != null) return item.readAlpha();
            return item.group != null ? item.group.alpha : 1f;
        }

        public void ResetToDefault()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                ApplyItemPosition(item, item.defaultPos);
                ApplyItemScale(item, item.defaultScale);
                ApplyItemAlpha(item, item.defaultAlpha);
            }

            MobileLayoutStorage.Delete();
            if (_touchUI != null)
            {
                _touchUI.SetLeftFireVisible(false);
                // 删除存档后重跑 DPI 适配，让「恢复默认」回到当前设备应有的物理尺寸。
                _touchUI.ApplyDefaultDeviceLayout();
            }

            _workingConfig = GetCurrentConfig();
            _workingConfig.preset = (int)MobileLayoutPreset.TwoFinger;
            _workingConfig.leftFireVisible = false;
            RefreshAuxButtons();

            if (_selectedItem != null)
            {
                SelectItem(IsItemVisible(_selectedItem) ? _selectedItem : GetItem("Fire"));
            }
        }

        public void ApplyConfig(MobileLayoutConfig config)
        {
            if (config == null || config.buttons == null) return;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var data = config.Find(item.id);
                if (data != null)
                {
                    ApplyItemPosition(item, data.anchoredPosition);
                    ApplyItemScale(item, data.scale);
                    ApplyItemAlpha(item, data.alpha);
                }
            }

            // 左开火键显隐跟随布局；旧存档缺该字段时为 false（2 指），不会突然冒出新按钮。
            _touchUI?.SetLeftFireVisible(config.leftFireVisible);

            if (IsEditing && _selectedItem != null)
            {
                SelectItem(IsItemVisible(_selectedItem) ? _selectedItem : GetItem("Fire"));
            }
        }

        private MobileLayoutConfig GetCurrentConfig()
        {
            var cfg = new MobileLayoutConfig
            {
                leftFireVisible = _touchUI != null
                    ? _touchUI.IsLeftFireVisible
                    : (_workingConfig != null && _workingConfig.leftFireVisible),
                // 无自定义存档时展示的是 2 指基准布局，初次打开应高亮「2指」而不是全不选。
                preset = _workingConfig != null ? _workingConfig.preset : (int)MobileLayoutPreset.TwoFinger
            };

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                cfg.Set(item.id, item.target.anchoredPosition, ReadItemScale(item), ReadItemAlpha(item));
            }
            return cfg;
        }

        private void SaveCurrentWorkingConfig()
        {
            _workingConfig = GetCurrentConfig();
            MobileLayoutStorage.Save(_workingConfig);
        }

        private static RectTransform CreateComicPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pos, Vector2 size, Color fillColor, float borderWidth, bool halftone)
        {
            // 必须显式带 CanvasRenderer：只靠 RequireComponent 时，某些构造顺序下图形不会出网格
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = (fillColor == ComicStyle.Ink) ? ComicStyle.Paper : ComicStyle.Ink;
            shape.color = fillColor;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.Halftone = false;
            shape.raycastTarget = true;

            return rt;
        }

        private static TextMeshProUGUI CreateTextMesh(Transform parent, string name, string text, float fontSize,
            Color color, TMP_FontAsset font, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return CreateTextMesh(name, parent, text, fontSize, color, font, alignment);
        }

        private static TextMeshProUGUI CreateTextMesh(string name, Transform parent, string text, float fontSize,
            Color color, TMP_FontAsset font, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            if (font != null) tmp.font = font;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button CreateComicButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
            Color fillColor, Color textColor, float fontSize, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // 视觉主体用子物体 + 显式 CanvasRenderer（与 CreateComicPanel 同一条已验证路径）。
            // 直接 AddComponent<UIInkShapeGraphic> 在部分构造顺序下不出网格，按钮会变成只有文字。
            var visualGo = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            visualGo.transform.SetParent(go.transform, false);
            var visualRt = visualGo.GetComponent<RectTransform>();
            visualRt.anchorMin = Vector2.zero;
            visualRt.anchorMax = Vector2.one;
            visualRt.offsetMin = Vector2.zero;
            visualRt.offsetMax = Vector2.zero;

            var shape = visualGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fillColor;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.ShadowColor = ComicStyle.Ink;
            shape.Halftone = false;
            shape.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = shape;
            var colors = btn.colors;
            colors.normalColor = fillColor;
            colors.highlightedColor = ComicStyle.PaperAlt;
            colors.pressedColor = ComicStyle.Yellow;
            colors.selectedColor = fillColor;
            colors.fadeDuration = ComicStyle.PressDuration;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var theme = UIThemeSO.Resolve(null);
            var font = theme.ResolveFont(false, label);
            var txt = CreateTextMesh("Label", go.transform, label, fontSize, textColor, font, TextAlignmentOptions.Center);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btn;
        }
    }

    public class MobileConfigurableItem
    {
        public string id;
        public string displayName;
        public RectTransform target;
        public CanvasGroup group;
        public Vector2 defaultPos;
        public float defaultScale;
        public float defaultAlpha;

        // 可选回调：普通 RectTransform 留空即可；触控按钮/摇杆需要把「布局缩放/透明度」
        // 写入自己的基准值，避免按下、回弹、大招呼吸等状态动画把自定义值重置回 1.0。
        public Action<Vector2> applyPosition;
        public Action<float> applyScale;
        public Func<float> readScale;
        public Action<float> applyAlpha;
        public Func<float> readAlpha;

        /// <summary>
        /// 仅编辑期启用的透明命中层（子物体）。平时必须保持隐藏：
        /// 它挂有 LayoutDragProxy（实现了 IPointerDownHandler），一旦常驻激活，
        /// EventSystem 的 ExecuteHierarchy 会在它身上就找到「指针按下处理器」并停止向上冒泡，
        /// 父节点上的 MobileActionButton 永远收不到 OnPointerDown —— 表现为所有操作按钮点不动。
        /// </summary>
        public GameObject hitProxy;
    }

    /// <summary>挂载在各操作按键与透明命中层上的拖拽代理，仅在编辑模式下生效。</summary>
    public class LayoutDragProxy : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private MobileLayoutCustomizer _customizer;
        private MobileConfigurableItem _item;

        public void Init(MobileLayoutCustomizer customizer, MobileConfigurableItem item)
        {
            _customizer = customizer;
            _item = item;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_customizer != null && _customizer.IsEditing)
            {
                _customizer.OnItemPointerDown(_item, eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_customizer != null && _customizer.IsEditing)
            {
                _customizer.OnItemPointerDrag(_item, eventData);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_customizer != null && _customizer.IsEditing)
            {
                _customizer.OnItemPointerUp(_item, eventData);
            }
        }
    }

    /// <summary>几何大胆风轻量触控滑动条组件（纯色方块轨道 + 方形把手）。</summary>
    public class TouchSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public event Action<float> OnValueChanged;

        private float _min;
        private float _max;
        private float _current;
        private RectTransform _rectTransform;
        private RectTransform _handle;
        private Image _fillImage;

        public static TouchSlider Create(GameObject go, float min, float max, float current)
        {
            var slider = go.AddComponent<TouchSlider>();
            slider._min = min;
            slider._max = max;
            slider._current = current;

            slider._rectTransform = go.GetComponent<RectTransform>();

            // 纯色方块滑轨底色（直角黑底）
            var bgImg = go.AddComponent<Image>();
            bgImg.color = ComicStyle.Ink;
            bgImg.raycastTarget = true;

            // 亮黄纯色块填充条
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            slider._fillImage = fillGo.GetComponent<Image>();
            slider._fillImage.color = ComicStyle.Yellow;
            slider._fillImage.raycastTarget = false;

            // 方形把手（24x36 纯白实心方形方块）
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24f, 36f);
            handleRt.anchorMin = new Vector2(0f, 0.5f);
            handleRt.anchorMax = new Vector2(0f, 0.5f);

            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = ComicStyle.Paper;
            handleImg.raycastTarget = false;
            slider._handle = handleRt;

            slider.UpdateVisual();
            return slider;
        }

        public void SetValueWithoutNotify(float val)
        {
            _current = Mathf.Clamp(val, _min, _max);
            UpdateVisual();
        }

        public void OnPointerDown(PointerEventData eventData) => UpdateValueFromPointer(eventData);
        public void OnDrag(PointerEventData eventData) => UpdateValueFromPointer(eventData);

        /// <summary>
        /// 把本地 X 坐标映射到 0~1。必须传入 rect.xMin 而非假设 pivot 居中：
        /// 编辑器里的两个滑块 pivot 都是 (0, 0.5)，旧公式 (+width*0.5) 会把左半段压到 50%~100%、
        /// 右半段变成死区，表现为“大小/透明度怎么拖都几乎不动”。
        /// </summary>
        public static float NormalizeLocalX(float localX, float rectXMin, float width)
        {
            if (width <= 0.0001f) return 0f;
            return Mathf.Clamp01((localX - rectXMin) / width);
        }

        private void UpdateValueFromPointer(PointerEventData eventData)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                float width = _rectTransform.rect.width;
                if (width <= 0f) return;

                float norm = NormalizeLocalX(localPoint.x, _rectTransform.rect.xMin, width);
                _current = Mathf.Lerp(_min, _max, norm);
                UpdateVisual();
                OnValueChanged?.Invoke(_current);
            }
        }

        private void UpdateVisual()
        {
            float norm = Mathf.Clamp01((_current - _min) / (_max - _min));
            float width = _rectTransform != null ? _rectTransform.rect.width : 160f;

            if (_fillImage != null)
            {
                var rt = _fillImage.rectTransform;
                rt.anchorMax = new Vector2(norm, 1f);
            }

            if (_handle != null)
            {
                _handle.anchoredPosition = new Vector2(norm * width, 0f);
            }
        }
    }
}
