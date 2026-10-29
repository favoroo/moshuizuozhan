using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端按键自定义布局编辑器（参考《和平精英》《蛋仔派对》）：
    /// 1. 进入编辑模式后游戏自动暂停；
    /// 2. 任意按键点击可直接选中，手指触摸即可自由拖拽至屏幕任意位置；
    /// 3. 提供属性调节面板，支持 70%~160% 大小缩放与 30%~100% 透明度调节；
    /// 4. 支持【保存退出】（持久化到本地）与【恢复默认】（一键还原标准扇形排布）。
    /// </summary>
    public class MobileLayoutCustomizer : MonoBehaviour
    {
        private MobileTouchUI _touchUI;
        private RectTransform _safeArea;
        private GameObject _overlayRoot;
        private RectTransform _inspectorCard;
        private Text _inspectorTitle;
        private TouchSlider _scaleSlider;
        private Text _scaleValueText;
        private TouchSlider _alphaSlider;
        private Text _alphaValueText;
        private RectTransform _selectionHighlight;

        private MobileConfigurableItem _selectedItem;
        private readonly List<MobileConfigurableItem> _items = new List<MobileConfigurableItem>();
        private MobileLayoutConfig _workingConfig;
        private float _previousTimeScale = 1f;

        public bool IsEditing { get; private set; }
        public event Action OnClosed;

        public static MobileLayoutCustomizer Create(MobileTouchUI touchUI, RectTransform safeArea, Sprite circleRingSprite)
        {
            var go = new GameObject("MobileLayoutCustomizer", typeof(RectTransform));
            go.transform.SetParent(safeArea.parent, false);

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

            // 挂载拖拽与点击选择代理组件
            var proxy = target.gameObject.GetComponent<LayoutDragProxy>();
            if (proxy == null) proxy = target.gameObject.AddComponent<LayoutDragProxy>();
            proxy.Init(this, item);
        }

        private void BuildUI(Sprite circleRingSprite)
        {
            _overlayRoot = gameObject;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 1. 全屏半透明暗色背景遮罩
            var bgImg = gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.06f, 0.10f, 0.65f);
            bgImg.raycastTarget = true; // 拦截底层射击/移动输入

            // 2. 顶部控制栏
            var topBar = CreatePanel(transform, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -40f), new Vector2(0f, 80f), new Color(0.08f, 0.10f, 0.14f, 0.92f));

            var titleText = CreateText(topBar.transform, "Title", "自 定 义 按 键 布 局", font, 26, TextAnchor.MiddleLeft,
                new Vector2(30f, 8f), new Vector2(360f, 32f), Color.white, FontStyle.Bold);

            CreateText(topBar.transform, "Subtitle", "点击按键拖动位置 · 调节下方滑块缩放大小与透明度", font, 14, TextAnchor.MiddleLeft,
                new Vector2(30f, -20f), new Vector2(480f, 24f), new Color(0.75f, 0.82f, 0.92f, 0.85f));

            // 控制栏右侧三键：恢复默认、保存、退出
            CreateButton(topBar.transform, "ResetBtn", "恢复默认", font, new Vector2(-410f, 0f), new Vector2(120f, 44f),
                new Color(0.35f, 0.38f, 0.45f, 0.95f), () => ResetToDefault());

            CreateButton(topBar.transform, "CancelBtn", "取消", font, new Vector2(-270f, 0f), new Vector2(110f, 44f),
                new Color(0.85f, 0.28f, 0.25f, 0.90f), () => Close(save: false));

            CreateButton(topBar.transform, "SaveBtn", "保存并退出", font, new Vector2(-110f, 0f), new Vector2(160f, 44f),
                new Color(0.18f, 0.72f, 0.38f, 0.98f), () => Close(save: true));

            // 3. 浮动属性调节卡片（位于屏幕中上部）
            _inspectorCard = CreatePanel(transform, "InspectorCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -145f), new Vector2(560f, 100f), new Color(0.09f, 0.12f, 0.18f, 0.95f));

            _inspectorTitle = CreateText(_inspectorCard, "InspectorTitle", "请点击任意按键开始调整", font, 18, TextAnchor.MiddleLeft,
                new Vector2(-260f, 26f), new Vector2(400f, 26f), new Color(1f, 0.84f, 0.24f, 1f), FontStyle.Bold);

            // 大小调节 Slider
            CreateText(_inspectorCard, "ScaleLabel", "大小:", font, 15, TextAnchor.MiddleLeft,
                new Vector2(-260f, -10f), new Vector2(60f, 24f), Color.white);

            var scaleSliderGo = new GameObject("ScaleSlider", typeof(RectTransform));
            scaleSliderGo.transform.SetParent(_inspectorCard, false);
            var scaleRt = scaleSliderGo.GetComponent<RectTransform>();
            scaleRt.anchorMin = new Vector2(0.5f, 0.5f);
            scaleRt.anchorMax = new Vector2(0.5f, 0.5f);
            scaleRt.anchoredPosition = new Vector2(-110f, -10f);
            scaleRt.sizeDelta = new Vector2(180f, 22f);

            _scaleSlider = TouchSlider.Create(scaleSliderGo, 0.65f, 1.70f, 1.0f);
            _scaleSlider.OnValueChanged += OnScaleSliderChanged;

            _scaleValueText = CreateText(_inspectorCard, "ScaleValue", "100%", font, 14, TextAnchor.MiddleLeft,
                new Vector2(-10f, -10f), new Vector2(50f, 24f), new Color(0.8f, 0.9f, 1f, 1f));

            // 透明度调节 Slider
            CreateText(_inspectorCard, "AlphaLabel", "透明度:", font, 15, TextAnchor.MiddleLeft,
                new Vector2(50f, -10f), new Vector2(65f, 24f), Color.white);

            var alphaSliderGo = new GameObject("AlphaSlider", typeof(RectTransform));
            alphaSliderGo.transform.SetParent(_inspectorCard, false);
            var alphaRt = alphaSliderGo.GetComponent<RectTransform>();
            alphaRt.anchorMin = new Vector2(0.5f, 0.5f);
            alphaRt.anchorMax = new Vector2(0.5f, 0.5f);
            alphaRt.anchoredPosition = new Vector2(180f, -10f);
            alphaRt.sizeDelta = new Vector2(140f, 22f);

            _alphaSlider = TouchSlider.Create(alphaSliderGo, 0.30f, 1.0f, 0.9f);
            _alphaSlider.OnValueChanged += OnAlphaSliderChanged;

            _alphaValueText = CreateText(_inspectorCard, "AlphaValue", "90%", font, 14, TextAnchor.MiddleLeft,
                new Vector2(260f, -10f), new Vector2(50f, 24f), new Color(0.8f, 0.9f, 1f, 1f));

            // 4. 选中高光外环
            var selGo = new GameObject("SelectionRing", typeof(RectTransform), typeof(Image));
            selGo.transform.SetParent(transform, false);
            _selectionHighlight = selGo.GetComponent<RectTransform>();
            _selectionHighlight.sizeDelta = new Vector2(150f, 150f);
            var selImg = selGo.GetComponent<Image>();
            selImg.sprite = circleRingSprite;
            selImg.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            selImg.raycastTarget = false;
            selGo.SetActive(false);
        }

        public void Open()
        {
            if (IsEditing) return;
            IsEditing = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f; // 暂停游戏，专注调整

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
                // 放弃修改：重新载入保存好的旧配置
                var saved = MobileLayoutStorage.Load();
                if (saved != null) ApplyConfig(saved);
                else ResetToDefault();
            }

            IsEditing = false;
            Time.timeScale = _previousTimeScale;
            if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void SelectItem(MobileConfigurableItem item)
        {
            _selectedItem = item;
            if (item == null)
            {
                _inspectorTitle.text = "请点击任意按键开始调整";
                if (_selectionHighlight != null) _selectionHighlight.gameObject.SetActive(false);
                return;
            }

            _inspectorTitle.text = $"正在调整：【{item.displayName}】";

            float scale = item.target.localScale.x;
            _scaleSlider.SetValueWithoutNotify(scale);
            _scaleValueText.text = $"{Mathf.RoundToInt(scale * 100f)}%";

            float alpha = item.group != null ? item.group.alpha : 1f;
            _alphaSlider.SetValueWithoutNotify(alpha);
            _alphaValueText.text = $"{Mathf.RoundToInt(alpha * 100f)}%";

            // 移动高光环至目标按键
            if (_selectionHighlight != null)
            {
                _selectionHighlight.gameObject.SetActive(true);
                _selectionHighlight.position = item.target.position;
                _selectionHighlight.sizeDelta = item.target.sizeDelta * (scale * 1.15f);
            }
        }

        private void OnScaleSliderChanged(float val)
        {
            if (_selectedItem == null) return;
            _selectedItem.target.localScale = Vector3.one * val;
            _scaleValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            if (_selectionHighlight != null)
            {
                _selectionHighlight.sizeDelta = _selectedItem.target.sizeDelta * (val * 1.15f);
            }
            UpdateWorkingConfigForItem(_selectedItem);
        }

        private void OnAlphaSliderChanged(float val)
        {
            if (_selectedItem == null) return;
            if (_selectedItem.group != null) _selectedItem.group.alpha = val;
            _alphaValueText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            UpdateWorkingConfigForItem(_selectedItem);
        }

        public void OnItemDragged(MobileConfigurableItem item, Vector2 screenPos, Camera cam)
        {
            if (!IsEditing || item == null) return;

            // 转换到父级 LocalPoint
            var parentRt = item.target.parent as RectTransform;
            if (parentRt == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenPos, cam, out Vector2 localPoint))
            {
                item.target.anchoredPosition = localPoint;

                if (_selectedItem == item && _selectionHighlight != null)
                {
                    _selectionHighlight.position = item.target.position;
                }

                UpdateWorkingConfigForItem(item);
            }
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

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = color;
            return rt;
        }

        private static Text CreateText(Transform parent, string name, string text, Font font, int fontSize,
            TextAnchor alignment, Vector2 pos, Vector2 size, Color color, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var txt = go.GetComponent<Text>();
            txt.font = font;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = alignment;
            txt.color = color;
            txt.raycastTarget = false;
            return txt;
        }

        private static void CreateButton(Transform parent, string name, string label, Font font, Vector2 pos, Vector2 size,
            Color color, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = color;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var txt = txtGo.GetComponent<Text>();
            txt.font = font;
            txt.text = label;
            txt.fontSize = 16;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.raycastTarget = false;
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

    /// <summary>挂载在各操作按键上的拖拽代理，仅在编辑模式下生效。</summary>
    public class LayoutDragProxy : MonoBehaviour, IPointerDownHandler, IDragHandler
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
                _customizer.SelectItem(_item);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_customizer != null && _customizer.IsEditing)
            {
                _customizer.OnItemDragged(_item, eventData.position, eventData.pressEventCamera);
            }
        }
    }

    /// <summary>轻量无依赖的触控滑动条组件。</summary>
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

            // 滑轨底色
            var bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.24f, 0.32f, 0.95f);
            bgImg.raycastTarget = true;

            // 填充条
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            slider._fillImage = fillGo.GetComponent<Image>();
            slider._fillImage.color = new Color(0.18f, 0.72f, 0.38f, 0.95f);
            slider._fillImage.raycastTarget = false;

            // 把手
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(go.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24f, 32f);
            handleRt.anchorMin = new Vector2(0f, 0.5f);
            handleRt.anchorMax = new Vector2(0f, 0.5f);

            var handleImg = handleGo.GetComponent<Image>();
            handleImg.color = Color.white;
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
