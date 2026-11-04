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
    /// 2. 全屏实心深墨背板（独立节点）压在首页/战场之上、所有触屏按键之下：
    ///    首页卡片与白色底板不再从半透明遮罩里透出来；背板同时吃掉落在空白处的点击，
    ///    避免隔着不透明背板误触首页卡片（按键在背板之上，不受影响）；
    /// 3. 为每个可配置按键挂载透明命中代理层，并在编辑态解开摇杆 blocksRaycasts 阻断；
/// 4. 坐标拖拽采用 (startPos + delta) 增量运算，并按父级 SafeArea 尺寸做 Clamp（留出半宽半高边距）；
    /// 5. 选中项展示 3px 纯黄描边 + 四角方块几何标记与实时读数角标；
    /// 6. 面板采用直角黑底白字纯色大色块（无阴影无网点）；t-1 控件下限：
    ///    设置类按钮高 ≥64、字号 ≥20，顶栏/属性舱/辅助卡统一 960 宽居中（高 88/88/152），
    ///    滑轨 110x44；顶栏不再横贯全屏，给顶部两角的按键留出拖拽空间；
    ///    所有按钮走 UIJellyButton/UISpringMotion 的 0ms scale 0.97 + 0.1s Cubic-Out 回弹；
    /// 7. 额外提供 2/3/4 指方案、左开火键、开火转视角、固定摇杆与辅助瞄准档位，手动编辑会把方案标记为自定义。
    /// </summary>
    public class MobileLayoutCustomizer : MonoBehaviour
    {
        // ===== t-1 控件度量（Docs/UI-GeometricBold.md 触控/设置控件下限） =====
        // 设置项/编辑器类按钮：高 ≥64px、字号 ≥20；触控反馈走 0.97 硬切 + 0.1s Cubic-Out 回弹。
        private const float TopBarHeight = 88f;
        private const float ControlHeight = 64f;
        private const float ControlFontBase = 20f;
        private const float PanelWidth = 960f;
        private const float InspectorHeight = 88f;
        private const float SliderTrackWidth = 110f;
        private const float SliderTrackHeight = 44f;

        /// <summary>辅助卡：两行 64 高按钮 + 内边距。规范给的 112 高塞不下两行 64，故取 152。</summary>
        private const float AuxCardHeight = 152f;

        private const float PanelGap = 12f;

        private MobileTouchUI _touchUI;
        private RectTransform _safeArea;
        private GameObject _overlayRoot;
        private GameObject _backdrop;
        private RectTransform _inspectorCard;
        private TextMeshProUGUI _inspectorTitle;
        private TextMeshProUGUI _inspectorCoord;
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
        private readonly MobileLayoutEditSession _editSession = new MobileLayoutEditSession();
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
            customizer.BuildBackdrop();
            go.SetActive(false);

            return customizer;
        }

        /// <summary>
        /// 全屏实心编辑背板：与首页舞台同色的不透明深墨底（#12100E）。
        /// 挂在触屏画布根（SafeArea 的父级）并置为首个兄弟节点：
        /// - 排在 SafeArea 之前 → 压在首页卡片/战斗 HUD 之上、所有触屏按键与编辑面板之下；
        /// - 不受 SafeArea 缩进影响 → 刘海/圆角区域也不会漏出首页的白底。
        /// 旧实现是挂在编辑器根节点上的 65% 半透明遮罩，既挡不住首页 UI，又把按键一起压暗一层。
        /// </summary>
        private void BuildBackdrop()
        {
            Transform host = _safeArea != null && _safeArea.parent != null ? _safeArea.parent : (Transform)_safeArea;
            if (host == null) host = transform;

            var go = new GameObject("LayoutEditBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = MatchFlowUI.StageInk;
            // 背板在按键之下，命中顺序天然是「按键 → 背板」：按键与拖拽代理照常收到 PointerDown，
            // 只有落在空白处的点击会被背板吃掉，不会穿透到已被遮住的首页卡片/武器抽屉。
            img.raycastTarget = true;

            go.transform.SetAsFirstSibling();
            go.SetActive(false);
            _backdrop = go;
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
            float controlFont = UITypography.FontSnapped(ControlFontBase);

            // 1. 根节点背板：只保留「不吞射线」的图形语义（旧版本这里是 65% 半透明黑，
            //    既挡不住首页 UI，又会把已渲染的按键再压暗一层）。真正的实心背板见 BuildBackdrop()。
            var bgImg = gameObject.AddComponent<Image>();
            bgImg.color = Color.clear;
            bgImg.raycastTarget = false; // 【关键修复 1】：背板不拦截射线

            // 2. 顶部主控制栏（直角纯黑底 + 3px 实线边框，960 宽居中，不再横贯全屏挡住顶部按键）
            var topBar = CreateComicPanel(transform, "TopBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -TopBarHeight * 0.5f), new Vector2(PanelWidth, TopBarHeight),
                ComicStyle.Ink, ComicStyle.BorderThin, false);

            // 左侧黄色装饰标块（8x44 直角纯黄方块）
            CreateCornerMarker(topBar.transform, "DecoYellow", new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(8f, 44f), ComicStyle.Yellow);

            var titleText = CreateTextMesh("Title", topBar.transform, "按键布局", UITypography.FontSnapped(24f), ComicStyle.Paper,
                theme.ResolveFont(true, "按键布局"), TextAlignmentOptions.Left);
            var titleRt = titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(44f, 0f);
            titleRt.sizeDelta = new Vector2(140f, 48f);

            // 竖直分割线
            CreateSeparator(topBar.transform, new Vector2(196f, 0f), new Vector2(2f, 44f));

            var subText = CreateTextMesh("Subtitle", topBar.transform, "拖动按键 · 改完保存", UITypography.FontSnapped(18f), ComicStyle.FromHex(0xA8A8A8),
                theme.ResolveFont(false, "拖动按键改完保存"), TextAlignmentOptions.Left);
            var subRt = subText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(208f, 0f);
            subRt.sizeDelta = new Vector2(240f, 48f);

            // 控制栏右侧三键：重置 (110x64)、保存 (110x64)、关闭 (64x64)
            CreateComicButton(topBar.transform, "ResetBtn", "重置",
                new Vector2(-280f, 0f), new Vector2(110f, ControlHeight),
                ComicStyle.FromHex(0x282828), ComicStyle.Paper, controlFont, () => ResetToDefault());

            CreateComicButton(topBar.transform, "SaveBtn", "保存",
                new Vector2(-155f, 0f), new Vector2(110f, ControlHeight),
                ComicStyle.Yellow, ComicStyle.Ink, controlFont, () => Close(save: true));

            CreateComicButton(topBar.transform, "CloseBtn", "X",
                new Vector2(-52f, 0f), new Vector2(64f, 64f),
                ComicStyle.Red, ComicStyle.Paper, UITypography.FontSnapped(26f), () => Close(save: false));

            // 3. 属性调节舱（直角纯黑底 + 3px 边框，960x88，紧贴顶栏下方 12px）
            _inspectorCard = CreateComicPanel(transform, "InspectorCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(TopBarHeight + PanelGap)), new Vector2(PanelWidth, InspectorHeight),
                ComicStyle.Ink, ComicStyle.BorderThin, false);
            _inspectorCard.pivot = new Vector2(0.5f, 1f);

            // 黄色小标块
            CreateCornerMarker(_inspectorCard.transform, "InspDeco", new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(4f, 32f), ComicStyle.Yellow);

            _inspectorTitle = CreateTextMesh("InspectorTitle", _inspectorCard, "点选按键调整", controlFont, ComicStyle.Paper,
                theme.ResolveFont(true, "点选按键调整正在"), TextAlignmentOptions.Left);
            var inspTitleRt = _inspectorTitle.rectTransform;
            inspTitleRt.anchorMin = new Vector2(0f, 0.5f);
            inspTitleRt.anchorMax = new Vector2(0f, 0.5f);
            inspTitleRt.pivot = new Vector2(0f, 0.5f);
            inspTitleRt.anchoredPosition = new Vector2(28f, 0f);
            inspTitleRt.sizeDelta = new Vector2(200f, 48f);

            // 读数走 18/24（正文底线），避免 26 号字把 960 宽的舱体挤爆
            float readoutFont = UITypography.FontSnapped(18f);
            _inspectorCoord = CreateTextMesh("InspectorCoord", _inspectorCard, "", readoutFont, ComicStyle.Yellow,
                theme.ResolveFont(false, "坐标"), TextAlignmentOptions.Left);
            var coordRt = _inspectorCoord.rectTransform;
            coordRt.anchorMin = new Vector2(0f, 0.5f);
            coordRt.anchorMax = new Vector2(0f, 0.5f);
            coordRt.pivot = new Vector2(0f, 0.5f);
            coordRt.anchoredPosition = new Vector2(236f, 0f);
            coordRt.sizeDelta = new Vector2(120f, 48f);

            // 竖分割线
            CreateSeparator(_inspectorCard.transform, new Vector2(372f, 0f), new Vector2(2f, 40f));

            // 大小调节 Slider 组（滑轨收窄到 110x44，把手 20x44）
            var scaleLabel = CreateTextMesh("ScaleLabel", _inspectorCard, "大小", controlFont, ComicStyle.Paper,
                theme.ResolveFont(false, "大小"), TextAlignmentOptions.Left);
            var scaleLabelRt = scaleLabel.rectTransform;
            scaleLabelRt.anchorMin = new Vector2(0f, 0.5f);
            scaleLabelRt.anchorMax = new Vector2(0f, 0.5f);
            scaleLabelRt.pivot = new Vector2(0f, 0.5f);
            scaleLabelRt.anchoredPosition = new Vector2(386f, 0f);
            scaleLabelRt.sizeDelta = new Vector2(60f, 48f);

            var scaleSliderGo = new GameObject("ScaleSlider", typeof(RectTransform));
            scaleSliderGo.transform.SetParent(_inspectorCard, false);
            var scaleRt = scaleSliderGo.GetComponent<RectTransform>();
            scaleRt.anchorMin = new Vector2(0f, 0.5f);
            scaleRt.anchorMax = new Vector2(0f, 0.5f);
            scaleRt.pivot = new Vector2(0f, 0.5f);
            scaleRt.anchoredPosition = new Vector2(454f, 0f);
            scaleRt.sizeDelta = new Vector2(SliderTrackWidth, SliderTrackHeight);

            _scaleSlider = TouchSlider.Create(scaleSliderGo, MobileLayoutConfig.MinScale, MobileLayoutConfig.MaxScale, 1.0f,
                new Vector2(20f, SliderTrackHeight));
            _scaleSlider.OnValueChanged += OnScaleSliderChanged;

            _scaleValueText = CreateTextMesh("ScaleValue", _inspectorCard, "100%", readoutFont, ComicStyle.Yellow,
                theme.ResolveFont(false, "100%"), TextAlignmentOptions.Left);
            var scaleValRt = _scaleValueText.rectTransform;
            scaleValRt.anchorMin = new Vector2(0f, 0.5f);
            scaleValRt.anchorMax = new Vector2(0f, 0.5f);
            scaleValRt.pivot = new Vector2(0f, 0.5f);
            scaleValRt.anchoredPosition = new Vector2(570f, 0f);
            scaleValRt.sizeDelta = new Vector2(64f, 48f);

            // 竖分割线
            CreateSeparator(_inspectorCard.transform, new Vector2(648f, 0f), new Vector2(2f, 40f));

            // 透明度调节 Slider 组（容器宽度宽裕给足，严禁任何换行）
            var alphaLabel = CreateTextMesh("AlphaLabel", _inspectorCard, "透明", controlFont, ComicStyle.Paper,
                theme.ResolveFont(false, "透明"), TextAlignmentOptions.Left);
            var alphaLabelRt = alphaLabel.rectTransform;
            alphaLabelRt.anchorMin = new Vector2(0f, 0.5f);
            alphaLabelRt.anchorMax = new Vector2(0f, 0.5f);
            alphaLabelRt.pivot = new Vector2(0f, 0.5f);
            alphaLabelRt.anchoredPosition = new Vector2(662f, 0f);
            alphaLabelRt.sizeDelta = new Vector2(60f, 48f);

            var alphaSliderGo = new GameObject("AlphaSlider", typeof(RectTransform));
            alphaSliderGo.transform.SetParent(_inspectorCard, false);
            var alphaRt = alphaSliderGo.GetComponent<RectTransform>();
            alphaRt.anchorMin = new Vector2(0f, 0.5f);
            alphaRt.anchorMax = new Vector2(0f, 0.5f);
            alphaRt.pivot = new Vector2(0f, 0.5f);
            alphaRt.anchoredPosition = new Vector2(730f, 0f);
            alphaRt.sizeDelta = new Vector2(SliderTrackWidth, SliderTrackHeight);

            _alphaSlider = TouchSlider.Create(alphaSliderGo, MobileLayoutConfig.MinAlpha, MobileLayoutConfig.MaxAlpha, 1.0f,
                new Vector2(20f, SliderTrackHeight));
            _alphaSlider.OnValueChanged += OnAlphaSliderChanged;

            _alphaValueText = CreateTextMesh("AlphaValue", _inspectorCard, "90%", readoutFont, ComicStyle.Yellow,
                theme.ResolveFont(false, "90%"), TextAlignmentOptions.Left);
            var alphaValRt = _alphaValueText.rectTransform;
            alphaValRt.anchorMin = new Vector2(0f, 0.5f);
            alphaValRt.anchorMax = new Vector2(0f, 0.5f);
            alphaValRt.pivot = new Vector2(0f, 0.5f);
            alphaValRt.anchoredPosition = new Vector2(846f, 0f);
            alphaValRt.sizeDelta = new Vector2(64f, 48f);

            // 4. 方案与辅助设置卡片（置顶收敛，两行 64 高按钮，绝不遮挡中央操作区）
            BuildAuxCard(theme);

            // 5. 选中项 2.5px 几何明黄描边 + 四角方块几何标记 + 实时读数角标
            BuildSelectionVisual(transform);
        }

        /// <summary>
        /// 方案与辅助卡片：两行精致直角排布，收敛在顶部，留出大面积中央视野。
        /// 单行容器宽度充分留足，enableWordWrapping=false，彻底杜绝换行截断。
        /// </summary>
        private void BuildAuxCard(UIThemeSO theme)
        {
            var card = CreateComicPanel(transform, "AuxCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(TopBarHeight + InspectorHeight + PanelGap * 2f)), new Vector2(PanelWidth, AuxCardHeight),
                ComicStyle.Ink, ComicStyle.BorderThin, false);
            card.pivot = new Vector2(0.5f, 1f);

            var font = theme.ResolveFont(false, "方案辅助瞄准关弱中强固定摇杆转视角左开火拖动调整位置大小");
            float controlFont = UITypography.FontSnapped(ControlFontBase);
            float rowY = 36f;

            // Row 1 (y = +36): 方案切换 + 左开火 + 开火转视角 + 固定摇杆
            var presetLabel = CreateTextMesh("PresetLabel", card, "方案:", controlFont, ComicStyle.Paper, font, TextAlignmentOptions.Left);
            var presetLabelRt = presetLabel.rectTransform;
            presetLabelRt.anchorMin = new Vector2(0f, 0.5f);
            presetLabelRt.anchorMax = new Vector2(0f, 0.5f);
            presetLabelRt.pivot = new Vector2(0f, 0.5f);
            presetLabelRt.anchoredPosition = new Vector2(16f, rowY);
            presetLabelRt.sizeDelta = new Vector2(80f, ControlHeight);

            var presetValues = new[] { MobileLayoutPreset.TwoFinger, MobileLayoutPreset.ThreeFinger, MobileLayoutPreset.FourFinger };
            for (int i = 0; i < presetValues.Length; i++)
            {
                int index = i;
                var preset = presetValues[i];
                // 2/3/4 指方案按钮：80x64（标签只有 2 字，80 宽仍有充足点按余量）
                var btn = CreateAuxButton(card, $"Preset_{preset}", MobileLayoutPresets.GetLabel(preset),
                    new Vector2(104f + i * 88f, rowY), new Vector2(80f, ControlHeight), controlFont, font,
                    () => ApplyPreset(presetValues[index]));
                _presetShapes[i] = btn.GetComponentInChildren<UIInkShapeGraphic>();
                _presetTexts[i] = btn.GetComponentInChildren<TextMeshProUGUI>();
            }

            CreateSeparator(card.transform, new Vector2(372f, rowY), new Vector2(2f, 40f));

            _leftFireToggle = CreateAuxButton(card, "LeftFireToggle", "左开火：关",
                new Vector2(384f, rowY), new Vector2(150f, ControlHeight), controlFont, font, ToggleLeftFire);
            _leftFireShape = _leftFireToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _leftFireText = _leftFireToggle.GetComponentInChildren<TextMeshProUGUI>();

            CreateSeparator(card.transform, new Vector2(546f, rowY), new Vector2(2f, 40f));

            _fireRotateToggle = CreateAuxButton(card, "FireRotateToggle", "转视角：开",
                new Vector2(558f, rowY), new Vector2(150f, ControlHeight), controlFont, font, ToggleFireRotate);
            _fireRotateShape = _fireRotateToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _fireRotateText = _fireRotateToggle.GetComponentInChildren<TextMeshProUGUI>();

            CreateSeparator(card.transform, new Vector2(720f, rowY), new Vector2(2f, 40f));

            _fixedJoystickToggle = CreateAuxButton(card, "FixedJoystickToggle", "固定摇杆：关",
                new Vector2(732f, rowY), new Vector2(170f, ControlHeight), controlFont, font, ToggleFixedJoystick);
            _fixedJoystickShape = _fixedJoystickToggle.GetComponentInChildren<UIInkShapeGraphic>();
            _fixedJoystickText = _fixedJoystickToggle.GetComponentInChildren<TextMeshProUGUI>();

            // Row 2 (y = -36): 辅助瞄准档位 + 提示文本
            float rowY2 = -36f;
            var aimLabel = CreateTextMesh("AimLabel", card, "辅助瞄准:", controlFont, ComicStyle.Paper, font, TextAlignmentOptions.Left);
            var aimLabelRt = aimLabel.rectTransform;
            aimLabelRt.anchorMin = new Vector2(0f, 0.5f);
            aimLabelRt.anchorMax = new Vector2(0f, 0.5f);
            aimLabelRt.pivot = new Vector2(0f, 0.5f);
            aimLabelRt.anchoredPosition = new Vector2(16f, rowY2);
            aimLabelRt.sizeDelta = new Vector2(140f, ControlHeight);

            for (int level = 0; level <= 3; level++)
            {
                int captured = level;
                var btn = CreateAuxButton(card, $"AimLevel_{level}", GameSettingsMath.GetAimAssistLevelLabel(level),
                    new Vector2(164f + level * 72f, rowY2), new Vector2(64f, ControlHeight), controlFont, font,
                    () => SetAimAssistLevel(captured));
                _aimLevelButtons[level] = btn;
                _aimLevelShapes[level] = btn.GetComponentInChildren<UIInkShapeGraphic>();
                _aimLevelTexts[level] = btn.GetComponentInChildren<TextMeshProUGUI>();
            }

            CreateSeparator(card.transform, new Vector2(456f, rowY2), new Vector2(2f, 40f));

            var hintText = CreateTextMesh("AuxHint", card, "拖动按键调整位置与大小", UITypography.FontSnapped(18f),
                ComicStyle.FromHex(0x9A9A9A), font, TextAlignmentOptions.Left);
            var hintRt = hintText.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 0.5f);
            hintRt.anchorMax = new Vector2(0f, 0.5f);
            hintRt.pivot = new Vector2(0f, 0.5f);
            hintRt.anchoredPosition = new Vector2(470f, rowY2);
            hintRt.sizeDelta = new Vector2(380f, ControlHeight);

            RefreshAuxButtons();
        }

        private static void CreateSeparator(Transform parent, Vector2 pos, Vector2 size)
        {
            var lineGo = new GameObject("Separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGo.transform.SetParent(parent, false);
            var lrt = lineGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = pos;
            lrt.sizeDelta = size;
            var img = lineGo.GetComponent<Image>();
            img.color = ComicStyle.FromHex(0x4E4E4E);
            img.raycastTarget = false;
        }

        private static void CreateCornerMarker(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
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
            shape.BorderColor = ComicStyle.FromHex(0x4A4A4A);
            shape.color = ComicStyle.FromHex(0x222222);
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

            var txt = CreateTextMesh("Label", go.transform, label, fontSize, ComicStyle.Paper, font, TextAlignmentOptions.Center);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            // t-1 按压反馈：0ms 硬切 scale 0.97 + 松开 0.1s Cubic-Out 回弹（走主题交互参数）
            var motion = go.AddComponent<UISpringMotion>();
            motion.SetTheme(null);
            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(null, btn, shape);

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
            shape.BorderWidth = ComicStyle.BorderThin; // 纯黄 3px 几何高亮描边
            shape.BorderColor = ComicStyle.Yellow;
            shape.color = Color.clear;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            // 选中框只画 2.5px 墨边，绝不能开硬阴影：硬阴影是整块实心墨色多边形，
            // 透明填充不会擦除它，会把当前选中的按键整个盖成黑色（测试断言）。
            shape.HardShadow = false;
            shape.ShadowOffset = Vector2.zero;
            shape.Halftone = false;
            shape.raycastTarget = false;

            // 几何大胆风：四角黄色方块标记
            CreateCornerMarker(selGo.transform, "CornerTL", new Vector2(0f, 1f));
            CreateCornerMarker(selGo.transform, "CornerTR", new Vector2(1f, 1f));
            CreateCornerMarker(selGo.transform, "CornerBL", new Vector2(0f, 0f));
            CreateCornerMarker(selGo.transform, "CornerBR", new Vector2(1f, 0f));

            // 实时读数角标（位于边框顶部正上方，纯黑底金边几何方块）
            var badgeGo = new GameObject("ReadoutBadge", typeof(RectTransform), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(selGo.transform, false);
            _readoutBadge = badgeGo.GetComponent<RectTransform>();
            _readoutBadge.anchorMin = new Vector2(0.5f, 1f);
            _readoutBadge.anchorMax = new Vector2(0.5f, 1f);
            _readoutBadge.pivot = new Vector2(0.5f, 0f);
            _readoutBadge.anchoredPosition = new Vector2(0f, 8f);
            _readoutBadge.sizeDelta = new Vector2(240f, 36f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.SharpRect;
            badgeShape.CornerRadius = ComicStyle.CornerSharp;
            badgeShape.BorderWidth = ComicStyle.BorderThin;
            badgeShape.BorderColor = ComicStyle.Yellow;
            badgeShape.color = ComicStyle.Ink;
            badgeShape.GlossAlpha = 0f;
            badgeShape.WobbleAmplitude = 0f;
            badgeShape.HardShadow = false;
            badgeShape.ShadowOffset = Vector2.zero;
            badgeShape.Halftone = false;
            badgeShape.raycastTarget = false;

            var theme = UIThemeSO.Resolve(null);
            _readoutText = CreateTextMesh("Text", badgeGo.transform, "X: 0, Y: 0 · 1.0x", UITypography.FontSnapped(18f), ComicStyle.Yellow,
                theme.ResolveFont(false, "0"), TextAlignmentOptions.Center);
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
            rt.sizeDelta = new Vector2(12f, 12f);
            var img = go.GetComponent<Image>();
            img.color = ComicStyle.Yellow;
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
                if (_backdrop != null) _backdrop.SetActive(false);
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
                if (_presetShapes[i] != null)
                {
                    _presetShapes[i].color = selected ? ComicStyle.Yellow : ComicStyle.FromHex(0x222222);
                    _presetShapes[i].BorderColor = selected ? ComicStyle.Yellow : ComicStyle.FromHex(0x3E3E3E);
                }
                if (_presetTexts[i] != null)
                {
                    _presetTexts[i].color = selected ? ComicStyle.Ink : ComicStyle.Paper;
                }
            }

            bool leftFire = _workingConfig != null
                ? _workingConfig.leftFireVisible
                : (_touchUI != null && _touchUI.IsLeftFireVisible);
            if (_leftFireText != null) _leftFireText.text = leftFire ? "左开火：开" : "左开火：关";
            if (_leftFireShape != null)
            {
                _leftFireShape.color = leftFire ? ComicStyle.Yellow : ComicStyle.FromHex(0x222222);
                _leftFireShape.BorderColor = leftFire ? ComicStyle.Yellow : ComicStyle.FromHex(0x3E3E3E);
            }
            if (_leftFireText != null) _leftFireText.color = leftFire ? ComicStyle.Ink : ComicStyle.Paper;

            bool rotate = GameSettingsService.FireButtonRotatesCamera;
            if (_fireRotateText != null) _fireRotateText.text = rotate ? "转视角：开" : "转视角：关";
            if (_fireRotateShape != null)
            {
                _fireRotateShape.color = rotate ? ComicStyle.Yellow : ComicStyle.FromHex(0x222222);
                _fireRotateShape.BorderColor = rotate ? ComicStyle.Yellow : ComicStyle.FromHex(0x3E3E3E);
            }
            if (_fireRotateText != null) _fireRotateText.color = rotate ? ComicStyle.Ink : ComicStyle.Paper;

            bool fixedJoystick = GameSettingsService.FixedJoystick;
            if (_fixedJoystickText != null) _fixedJoystickText.text = fixedJoystick ? "固定摇杆：开" : "固定摇杆：关";
            if (_fixedJoystickShape != null)
            {
                _fixedJoystickShape.color = fixedJoystick ? ComicStyle.Yellow : ComicStyle.FromHex(0x222222);
                _fixedJoystickShape.BorderColor = fixedJoystick ? ComicStyle.Yellow : ComicStyle.FromHex(0x3E3E3E);
            }
            if (_fixedJoystickText != null) _fixedJoystickText.color = fixedJoystick ? ComicStyle.Ink : ComicStyle.Paper;

            int aimLevel = GameSettingsService.AimAssistLevel;
            for (int level = 0; level < _aimLevelButtons.Length; level++)
            {
                bool selected = (level == aimLevel);
                if (_aimLevelShapes[level] != null)
                {
                    _aimLevelShapes[level].color = selected ? ComicStyle.Yellow : ComicStyle.FromHex(0x222222);
                    _aimLevelShapes[level].BorderColor = selected ? ComicStyle.Yellow : ComicStyle.FromHex(0x3E3E3E);
                }
                if (_aimLevelTexts[level] != null)
                {
                    _aimLevelTexts[level].color = selected ? ComicStyle.Ink : ComicStyle.Paper;
                }
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

        /// <summary>
        /// 应用一套 2/3/4 指方案。
        /// 立即 ApplyConfig = 所见即所得的预览（体验保留）；同时写入存档，
        /// 「点 X 撤销」由 <see cref="Close"/> 恢复打开时快照实现（见 MobileLayoutEditSession）。
        /// </summary>
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
            // 打开时快照：取消（点 X / Esc）要把布局与存档都还原到这一刻，
            // 否则 ApplyPreset 的即时写盘没法撤销。
            _editSession.Begin(_workingConfig, saved != null);
            if (saved != null)
            {
                ApplyConfig(saved);
            }

            if (_backdrop != null) _backdrop.SetActive(true);
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
                // 取消：回到「打开编辑器那一帧」的布局与存档。
                // 旧实现只 Load() 存档，而 ApplyPreset 已把方案写进存档，
                // 于是「切 4 指 → 点 X」撤销不掉。
                var snapshot = _editSession.ResolveCancelSnapshot();
                if (snapshot != null)
                {
                    ApplyConfig(snapshot);
                    MobileLayoutStorage.Save(snapshot);
                    _workingConfig = snapshot.Clone();
                }
                else
                {
                    // 打开时没有存档 = 默认布局：删档并回设备默认。
                    ResetToDefault();
                }
            }

            _editSession.End();

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
            if (_backdrop != null) _backdrop.SetActive(false);
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void SelectItem(MobileConfigurableItem item)
        {
            _selectedItem = item;
            if (item == null)
            {
                if (_inspectorTitle != null) _inspectorTitle.text = "点选按键调整";
                if (_inspectorCoord != null) _inspectorCoord.text = "";
                if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
                return;
            }

            if (_inspectorTitle != null)
            {
                // 26 号字下 960 宽舱体只容得下 8 个汉字：去掉冗余前缀，保留「调整【名称】」
                _inspectorTitle.text = $"调整【{item.displayName}】";
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

            int x = Mathf.RoundToInt(item.target.anchoredPosition.x);
            int y = Mathf.RoundToInt(item.target.anchoredPosition.y);

            if (_readoutText != null)
            {
                _readoutText.text = $"({x}, {y}) · {scale:F1}x";
            }

            if (_inspectorCoord != null)
            {
                // 只留坐标数值：24 号字下 150px 宽刚好放下 (999, 999)，加「坐标:」前缀会溢出到滑轨
                _inspectorCoord.text = $"({x}, {y})";
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
            Vector2 desired = ClampToParent(parentRt, item, _startAnchoredPosition + pointerDelta);

            ApplyItemPosition(item, desired);

            UpdateSelectionVisual(item);
            UpdateWorkingConfigForItem(item);
        }

        /// <summary>
        /// 把 anchoredPosition 钳制在父级矩形（通常是 SafeArea）内，留出 pivot 对应的半宽/半高。
        /// 【关键修复 3】直接在 anchoredPosition 空间做 Clamp：旧实现把「父级中心局部坐标」和
        /// 「锚点偏移坐标」混用，锚点在右下角的按钮会被算到屏幕外。
        /// 父级尺寸退化（0/未布局）时不做钳制，避免把按键硬压到一点。
        /// </summary>
        private static Vector2 ClampToParent(RectTransform parentRt, MobileConfigurableItem item, Vector2 desired)
        {
            if (parentRt == null || item == null || item.target == null) return desired;

            Rect parentRect = parentRt.rect;
            if (parentRect.width <= 1f || parentRect.height <= 1f) return desired;

            Vector2 pivot = item.target.pivot;
            float w = item.target.rect.width * item.target.localScale.x;
            float h = item.target.rect.height * item.target.localScale.y;

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

            return new Vector2(
                Mathf.Clamp(desired.x, minX, maxX),
                Mathf.Clamp(desired.y, minY, maxY));
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
                    // 先尺寸后位置：钳制要用最终 localScale 算包围盒（左开火键/潜行键是 1.7 倍放大）。
                    ApplyItemScale(item, data.scale);
                    ApplyItemAlpha(item, data.alpha);

                    // 预设/存档里的坐标也要过一遍 SafeArea 钳制：
                    // 20:9 挖孔屏横屏时画布实际高度小于基准 1080，左上角左开火键 / 右上角潜行键
                    // 会顶出安全区被裁掉（见 MobileLayoutPresets 的基准坐标）。
                    var parentRt = item.target != null ? item.target.parent as RectTransform : null;
                    ApplyItemPosition(item, ClampToParent(parentRt, item, data.anchoredPosition));
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
            tmp.textWrappingMode = TextWrappingModes.NoWrap; // 严防任何换行挤断
            tmp.overflowMode = TextOverflowModes.Overflow;
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
            shape.BorderWidth = (fillColor == ComicStyle.Yellow || fillColor == ComicStyle.Red) ? 0f : ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.FromHex(0x444444);
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
            colors.highlightedColor = (fillColor == ComicStyle.Yellow) ? ComicStyle.Paper : ComicStyle.FromHex(0x383838);
            colors.pressedColor = ComicStyle.Paper;
            colors.selectedColor = fillColor;
            colors.fadeDuration = ComicStyle.PressDuration;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // t-1 按压反馈：0ms 硬切 scale 0.97 + 松开 0.1s Cubic-Out 回弹
            var motion = go.AddComponent<UISpringMotion>();
            motion.SetTheme(null);
            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(null, btn, shape);

            var theme = UIThemeSO.Resolve(null);
            var font = theme.ResolveFont(true, label);
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
            return Create(go, min, max, current, new Vector2(16f, 28f));
        }

        /// <summary>
        /// 带把手尺寸的重载：设置类界面传 24x64 的把手，配合 64 高滑轨满足触控下限（≥64）。
        /// 旧签名保留，默认仍是小把手（HUD/旧调用零行为变化）。
        /// </summary>
        public static TouchSlider Create(GameObject go, float min, float max, float current, Vector2 handleSize)
        {
            var slider = go.AddComponent<TouchSlider>();
            slider._min = min;
            slider._max = max;
            slider._current = current;

            slider._rectTransform = go.GetComponent<RectTransform>();

            // 纯色方块滑轨底色（直角黑底）
            var bgImg = go.AddComponent<Image>();
            bgImg.color = ComicStyle.FromHex(0x181818);
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

            // 方形把手（默认 16x28 纯白实心方块；设置类界面传 24x64）
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = handleSize;
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
