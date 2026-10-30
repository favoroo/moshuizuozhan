using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端按键自定义布局编辑器（漫画风重绘 P10）：
    /// 1. 进入编辑模式后游戏自动暂停，退出时严格还原进入前的 Time.timeScale；
    /// 2. 根节点全屏背板 raycastTarget=false，杜绝吞掉按键 PointerDown；
    /// 3. 为每个可配置按键挂载透明命中代理层，并在编辑态解开摇杆 blocksRaycasts 阻断；
    /// 4. 坐标拖拽采用 (startPos + delta) 增量运算，并按父级 SafeArea 尺寸做 Clamp（留出半宽半高边距）；
    /// 5. 选中项展示 4px 墨色边框与实时读数角标「X,Y · 缩放 1.0」；
    /// 6. 面板采用漫画纸面 + 8px 墨边 + 硬阴影 + 网点底纹；提供 220x72 标签式保存/重置按钮与 72x72 关闭按钮。
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

            // 2. 为目标生成与自身同尺寸的透明命中层（HitProxy），确保 raycastTarget=true 且不拦截子视图渲染
            var hitProxyGo = new GameObject("HitProxy", typeof(RectTransform), typeof(Image));
            hitProxyGo.transform.SetParent(target, false);
            var hitRt = hitProxyGo.GetComponent<RectTransform>();
            hitRt.anchorMin = Vector2.zero;
            hitRt.anchorMax = Vector2.one;
            hitRt.offsetMin = Vector2.zero;
            hitRt.offsetMax = Vector2.zero;

            var hitImg = hitProxyGo.GetComponent<Image>();
            hitImg.color = Color.clear;
            hitImg.raycastTarget = true; // 确保在编辑模式下始终能被 PointerDown 命中

            var proxy = hitProxyGo.AddComponent<LayoutDragProxy>();
            proxy.Init(this, item);
        }

        private void BuildUI(Sprite circleRingSprite)
        {
            _overlayRoot = gameObject;
            var theme = UIThemeSO.Resolve(null);

            // 1. 全屏半透明暗色背景遮罩（必须 raycastTarget = false，绝不能吞底层射线）
            var bgImg = gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.10f, 0.55f);
            bgImg.raycastTarget = false; // 【关键修复 1】：背板不拦截射线

            // 2. 顶部控制栏（漫画纸面 + 6px 墨边 + 硬阴影）
            var topBar = CreateComicPanel(transform, "TopBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -48f), new Vector2(0f, 96f),
                ComicStyle.Paper, ComicStyle.Border, false);

            var titleText = CreateTextMesh("Title", topBar.transform, "自 定 义 按 键 布 局", 26f, ComicStyle.Ink,
                theme.ResolveFont(true, "自 定义按键布局"), TextAlignmentOptions.Left);
            var titleRt = titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.5f);
            titleRt.anchorMax = new Vector2(0f, 0.5f);
            titleRt.pivot = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(28f, 12f);
            titleRt.sizeDelta = new Vector2(400f, 34f);

            var subText = CreateTextMesh("Subtitle", topBar.transform, "点击按键拖动位置 · 调节下方滑块缩放大小与透明度", 15f, ComicStyle.Darken(ComicStyle.Ink, -0.4f),
                theme.ResolveFont(false, "点击按键拖动位置"), TextAlignmentOptions.Left);
            var subRt = subText.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0.5f);
            subRt.anchorMax = new Vector2(0f, 0.5f);
            subRt.pivot = new Vector2(0f, 0.5f);
            subRt.anchoredPosition = new Vector2(28f, -20f);
            subRt.sizeDelta = new Vector2(480f, 26f);

            // 控制栏右侧三键：恢复默认 (220x72)、保存完成 (220x72)、关闭 (72x72)
            CreateComicButton(topBar.transform, "ResetBtn", "恢 复 默 认",
                new Vector2(-540f, 0f), new Vector2(220f, 72f),
                ComicStyle.PaperAlt, ComicStyle.Ink, 20f, () => ResetToDefault());

            CreateComicButton(topBar.transform, "SaveBtn", "保 存 完 成",
                new Vector2(-300f, 0f), new Vector2(220f, 72f),
                ComicStyle.Green, ComicStyle.Ink, 22f, () => Close(save: true));

            CreateComicButton(topBar.transform, "CloseBtn", "X",
                new Vector2(-60f, 0f), new Vector2(72f, 72f),
                ComicStyle.Red, ComicStyle.Paper, 28f, () => Close(save: false));

            // 3. 浮动属性调节卡片（中上部：纸面 + 8px 墨边 + 硬阴影 + 网点）
            _inspectorCard = CreateComicPanel(transform, "InspectorCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -165f), new Vector2(660f, 110f),
                ComicStyle.Paper, ComicStyle.BorderHeavy, true);

            _inspectorTitle = CreateTextMesh("InspectorTitle", _inspectorCard, "请点击任意按键开始调整", 20f, ComicStyle.Ink,
                theme.ResolveFont(true, "请点击任意按键开始调整"), TextAlignmentOptions.Left);
            var inspTitleRt = _inspectorTitle.rectTransform;
            inspTitleRt.anchorMin = new Vector2(0f, 0.5f);
            inspTitleRt.anchorMax = new Vector2(0f, 0.5f);
            inspTitleRt.pivot = new Vector2(0f, 0.5f);
            inspTitleRt.anchoredPosition = new Vector2(24f, 30f);
            inspTitleRt.sizeDelta = new Vector2(400f, 30f);

            // 大小调节 Slider
            var scaleLabel = CreateTextMesh("ScaleLabel", _inspectorCard, "大小:", 17f, ComicStyle.Ink,
                theme.ResolveFont(false, "大小:"), TextAlignmentOptions.Left);
            var scaleLabelRt = scaleLabel.rectTransform;
            scaleLabelRt.anchorMin = new Vector2(0f, 0.5f);
            scaleLabelRt.anchorMax = new Vector2(0f, 0.5f);
            scaleLabelRt.pivot = new Vector2(0f, 0.5f);
            scaleLabelRt.anchoredPosition = new Vector2(24f, -15f);
            scaleLabelRt.sizeDelta = new Vector2(55f, 26f);

            var scaleSliderGo = new GameObject("ScaleSlider", typeof(RectTransform));
            scaleSliderGo.transform.SetParent(_inspectorCard, false);
            var scaleRt = scaleSliderGo.GetComponent<RectTransform>();
            scaleRt.anchorMin = new Vector2(0f, 0.5f);
            scaleRt.anchorMax = new Vector2(0f, 0.5f);
            scaleRt.pivot = new Vector2(0f, 0.5f);
            scaleRt.anchoredPosition = new Vector2(80f, -15f);
            scaleRt.sizeDelta = new Vector2(170f, 24f);

            _scaleSlider = TouchSlider.Create(scaleSliderGo, 0.65f, 1.70f, 1.0f);
            _scaleSlider.OnValueChanged += OnScaleSliderChanged;

            _scaleValueText = CreateTextMesh("ScaleValue", _inspectorCard, "100%", 17f, ComicStyle.Ink,
                theme.ResolveFont(false, "100%"), TextAlignmentOptions.Left);
            var scaleValRt = _scaleValueText.rectTransform;
            scaleValRt.anchorMin = new Vector2(0f, 0.5f);
            scaleValRt.anchorMax = new Vector2(0f, 0.5f);
            scaleValRt.pivot = new Vector2(0f, 0.5f);
            scaleValRt.anchoredPosition = new Vector2(258f, -15f);
            scaleValRt.sizeDelta = new Vector2(60f, 26f);

            // 透明度调节 Slider
            var alphaLabel = CreateTextMesh("AlphaLabel", _inspectorCard, "透明度:", 17f, ComicStyle.Ink,
                theme.ResolveFont(false, "透明度:"), TextAlignmentOptions.Left);
            var alphaLabelRt = alphaLabel.rectTransform;
            alphaLabelRt.anchorMin = new Vector2(0f, 0.5f);
            alphaLabelRt.anchorMax = new Vector2(0f, 0.5f);
            alphaLabelRt.pivot = new Vector2(0f, 0.5f);
            alphaLabelRt.anchoredPosition = new Vector2(330f, -15f);
            alphaLabelRt.sizeDelta = new Vector2(70f, 26f);

            var alphaSliderGo = new GameObject("AlphaSlider", typeof(RectTransform));
            alphaSliderGo.transform.SetParent(_inspectorCard, false);
            var alphaRt = alphaSliderGo.GetComponent<RectTransform>();
            alphaRt.anchorMin = new Vector2(0f, 0.5f);
            alphaRt.anchorMax = new Vector2(0f, 0.5f);
            alphaRt.pivot = new Vector2(0f, 0.5f);
            alphaRt.anchoredPosition = new Vector2(405f, -15f);
            alphaRt.sizeDelta = new Vector2(160f, 24f);

            _alphaSlider = TouchSlider.Create(alphaSliderGo, 0.30f, 1.0f, 0.9f);
            _alphaSlider.OnValueChanged += OnAlphaSliderChanged;

            _alphaValueText = CreateTextMesh("AlphaValue", _inspectorCard, "90%", 17f, ComicStyle.Ink,
                theme.ResolveFont(false, "90%"), TextAlignmentOptions.Left);
            var alphaValRt = _alphaValueText.rectTransform;
            alphaValRt.anchorMin = new Vector2(0f, 0.5f);
            alphaValRt.anchorMax = new Vector2(0f, 0.5f);
            alphaValRt.pivot = new Vector2(0f, 0.5f);
            alphaValRt.anchoredPosition = new Vector2(575f, -15f);
            alphaValRt.sizeDelta = new Vector2(60f, 26f);

            // 4. 选中项 4px 墨色边框 + 实时读数角标
            BuildSelectionVisual(transform);
        }

        private void BuildSelectionVisual(Transform parent)
        {
            var selGo = new GameObject("SelectionFrame", typeof(RectTransform), typeof(UIInkShapeGraphic));
            selGo.transform.SetParent(parent, false);
            _selectionHighlight = selGo.GetComponent<RectTransform>();
            _selectionHighlight.sizeDelta = new Vector2(160f, 160f);

            var shape = selGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 12f;
            shape.BorderWidth = 4f; // 4px 墨色边框
            shape.BorderColor = ComicStyle.Ink;
            shape.color = Color.clear;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(3f, -3f);
            shape.raycastTarget = false;

            // 实时读数角标（位于边框顶部正上方）
            var badgeGo = new GameObject("ReadoutBadge", typeof(RectTransform), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(selGo.transform, false);
            _readoutBadge = badgeGo.GetComponent<RectTransform>();
            _readoutBadge.anchorMin = new Vector2(0.5f, 1f);
            _readoutBadge.anchorMax = new Vector2(0.5f, 1f);
            _readoutBadge.pivot = new Vector2(0.5f, 0f);
            _readoutBadge.anchoredPosition = new Vector2(0f, 8f);
            _readoutBadge.sizeDelta = new Vector2(170f, 32f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.RoundedRect;
            badgeShape.CornerRadius = 6f;
            badgeShape.BorderWidth = 3f;
            badgeShape.BorderColor = ComicStyle.Ink;
            badgeShape.color = ComicStyle.Paper;
            badgeShape.HardShadow = true;
            badgeShape.ShadowOffset = new Vector2(2f, -2f);
            badgeShape.raycastTarget = false;

            _readoutText = CreateTextMesh("Text", badgeGo.transform, "X: 0, Y: 0 · 1.0x", 15f, ComicStyle.Ink,
                null, TextAlignmentOptions.Center);
            var textRt = _readoutText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            selGo.SetActive(false);
        }

        public void Open()
        {
            if (IsEditing) return;
            IsEditing = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f; // 暂停游戏

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
            }

            _workingConfig = MobileLayoutStorage.Load() ?? GetCurrentConfig();
            gameObject.SetActive(true);

            // 默认选中第一个按键（射击大键）
            if (_items.Count > 0)
            {
                var fireItem = _items.Find(x => x.id == "Fire") ?? _items[0];
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
            }

            IsEditing = false;
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

            float scale = item.target.localScale.x;
            _scaleSlider.SetValueWithoutNotify(scale);
            if (_scaleValueText != null) _scaleValueText.text = $"{Mathf.RoundToInt(scale * 100f)}%";

            float alpha = item.group != null ? item.group.alpha : 1f;
            _alphaSlider.SetValueWithoutNotify(alpha);
            if (_alphaValueText != null) _alphaValueText.text = $"{Mathf.RoundToInt(alpha * 100f)}%";

            UpdateSelectionVisual(item);
        }

        private void UpdateSelectionVisual(MobileConfigurableItem item)
        {
            if (item == null || _selectionHighlight == null) return;

            _selectionHighlight.gameObject.SetActive(true);
            _selectionHighlight.position = item.target.position;
            float scale = item.target.localScale.x;
            _selectionHighlight.sizeDelta = item.target.sizeDelta * (scale * 1.08f);

            if (_readoutText != null)
            {
                int x = Mathf.RoundToInt(item.target.anchoredPosition.x);
                int y = Mathf.RoundToInt(item.target.anchoredPosition.y);
                _readoutText.text = $"({x}, {y}) · {scale:F1}x";
            }
        }

        private void OnScaleSliderChanged(float val)
        {
            if (_selectedItem == null) return;
            _selectedItem.target.localScale = Vector3.one * val;
            if (_scaleValueText != null) _scaleValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            UpdateSelectionVisual(_selectedItem);
            UpdateWorkingConfigForItem(_selectedItem);
        }

        private void OnAlphaSliderChanged(float val)
        {
            if (_selectedItem == null) return;
            if (_selectedItem.group != null) _selectedItem.group.alpha = val;
            if (_alphaValueText != null) _alphaValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
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

            item.target.anchoredPosition = new Vector2(
                Mathf.Clamp(desired.x, minX, maxX),
                Mathf.Clamp(desired.y, minY, maxY));

            UpdateSelectionVisual(item);
            UpdateWorkingConfigForItem(item);
        }

        private void UpdateWorkingConfigForItem(MobileConfigurableItem item)
        {
            if (_workingConfig == null) _workingConfig = new MobileLayoutConfig();
            float alpha = item.group != null ? item.group.alpha : 1f;
            _workingConfig.Set(item.id, item.target.anchoredPosition, item.target.localScale.x, alpha);
        }

        public void ResetToDefault()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                item.target.anchoredPosition = item.defaultPos;
                item.target.localScale = Vector3.one * item.defaultScale;
                if (item.group != null) item.group.alpha = item.defaultAlpha;
            }

            MobileLayoutStorage.Delete();
            _workingConfig = GetCurrentConfig();
            if (_selectedItem != null) SelectItem(_selectedItem);
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
                    item.target.anchoredPosition = data.anchoredPosition;
                    item.target.localScale = Vector3.one * Mathf.Clamp(data.scale, 0.6f, 1.8f);
                    if (item.group != null) item.group.alpha = Mathf.Clamp(data.alpha, 0.25f, 1.0f);
                }
            }
        }

        private MobileLayoutConfig GetCurrentConfig()
        {
            var cfg = new MobileLayoutConfig();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                float alpha = item.group != null ? item.group.alpha : 1f;
                cfg.Set(item.id, item.target.anchoredPosition, item.target.localScale.x, alpha);
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
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 8f;
            shape.BorderWidth = borderWidth;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fillColor;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(6f, -6f);
            shape.Halftone = halftone;
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

        private static void CreateComicButton(Transform parent, string name, string label, Vector2 pos, Vector2 size,
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
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fillColor;
            shape.GlossAlpha = 0f;
            shape.WobbleAmplitude = 0f;
            shape.HardShadow = true;
            shape.ShadowOffset = new Vector2(5f, -5f);
            shape.ShadowColor = ComicStyle.Ink;
            shape.Halftone = false;
            shape.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = shape;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var theme = UIThemeSO.Resolve(null);
            var font = theme.ResolveFont(false, label);
            var txt = CreateTextMesh("Label", go.transform, label, fontSize, textColor, font, TextAlignmentOptions.Center);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
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

    /// <summary>漫画风轻量触控滑动条组件。</summary>
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

            // 滑轨底色（带 2px 墨线）
            var bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
            bgImg.raycastTarget = true;

            // 填充条（漫画强调黄）
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

            // 把手（20x30 纸面白底 + 2px 墨边）
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(20f, 32f);
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

        private void UpdateValueFromPointer(PointerEventData eventData)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                float width = _rectTransform.rect.width;
                if (width <= 0f) return;

                float norm = Mathf.Clamp01((localPoint.x + width * 0.5f) / width);
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
