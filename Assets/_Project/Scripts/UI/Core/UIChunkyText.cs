using P1.Core;
using TMPro;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 双层 Chunky 描边文本（P4-A）：底层是放大的黑色墨边（TMP outline），
    /// 上层是略微上移的填充字，形成 Inkwave 的「厚黑边 + 硬阴影 + 果冻字」观感。
    ///
    /// 只负责外观与文本同步；布局仍由 RectTransform / LayoutGroup 控制。
    /// 中文字符需要 TMP 字体本身（或 fallback）包含对应字形，Latin 字体请用英文/数字。
    /// </summary>
    [AddComponentMenu("P1/UI/Chunky Text")]
    [RequireComponent(typeof(TMP_Text))]
    [DisallowMultipleComponent]
    public class UIChunkyText : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;

        [Tooltip("留空则取同物体上的 TMP_Text")]
        [SerializeField] private TMP_Text source;

        [SerializeField] private bool useDisplayFont = true;

        [Tooltip("关闭则退化为单层 TMP 文本（适合小号说明文字）")]
        [SerializeField] private bool doubleLayer = true;

        [SerializeField] private bool useThemeColors = true;

        [Tooltip("单层模式下是否保留黑色描边；小号说明文字建议关闭")]
        [SerializeField] private bool outlined = true;

        [SerializeField] private Color fillColor = Color.white;

        [SerializeField] private Color outlineColor = new Color(0.082f, 0.071f, 0.110f, 1f);

        [Tooltip("TMP 描边宽度（0..1）。受字体 SDF 图集 padding 限制，太大会在边缘削平")]
        [SerializeField, Range(0f, 1f)] private float outlineWidth = 0.35f;

        [SerializeField, Range(0f, 1f)] private float innerOutlineWidth = 0.05f;

        [Tooltip("填充层上移量（em），让底层黑边从下方露出成硬阴影")]
        [SerializeField, Range(-0.25f, 0.25f)] private float shadowOffsetY = 0.05f;

        [SerializeField, Range(-0.25f, 0.25f)] private float shadowOffsetX;

        [Tooltip("自动把父层 TMP 的文本/字号/对齐同步给填充层")]
        [SerializeField] private bool autoSync = true;

        private TMP_Text _fillLayer;
        private Material _outlineMaterial;
        private Material _fillMaterial;
        private RectTransform _fillRect;
        private bool _built;
        private bool _dirty = true;

        public TMP_Text TextComponent
        {
            get
            {
                EnsureSource();
                return source;
            }
        }

        public TMP_Text FillLayer => _fillLayer;

        public Color FillColor => fillColor;

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            if (_built) _dirty = true;
        }

        private void OnDestroy()
        {
            if (_outlineMaterial != null)
            {
                Destroy(_outlineMaterial);
                _outlineMaterial = null;
            }

            if (_fillMaterial != null)
            {
                Destroy(_fillMaterial);
                _fillMaterial = null;
            }
        }

        /// <summary>
        /// 编辑器工具 / 运行时都可调用的配置入口。编辑模式下只写序列化外观（不碰共享材质），
        /// 运行时才会真正创建描边材质与填充层。
        /// </summary>
        public void Configure(UIThemeSO themeAsset, string text, float fontSize,
            bool displayFont = true, bool doubleLayered = true, Color? fill = null,
            bool outlinedText = true)
        {
            theme = themeAsset;
            useDisplayFont = displayFont;
            doubleLayer = doubleLayered;
            outlined = outlinedText;
            if (fill.HasValue) fillColor = fill.Value;

            EnsureSource();
            if (source != null)
            {
                source.text = text;
                source.fontSize = fontSize;
            }

            _dirty = true;

            if (Application.isPlaying && _built)
            {
                ApplyStyle();
            }
            else if (!Application.isPlaying)
            {
                ApplySerializedLook();
            }
        }

        public void SetText(string text)
        {
            EnsureSource();
            if (source == null) return;

            source.text = text;
            _dirty = true;
        }

        public void SetFillColor(Color color)
        {
            fillColor = color;
            if (_fillLayer != null)
            {
                _fillLayer.color = color;
                if (_fillLayer.fontMaterial != null) _fillLayer.outlineColor = color;
            }

            if (!doubleLayer && source != null) source.color = color;
        }

        public void SetOutlineColor(Color color)
        {
            outlineColor = color;
            if (source != null)
            {
                source.color = doubleLayer ? color : fillColor;
                if (source.fontMaterial != null) source.outlineColor = color;
            }

            if (_outlineMaterial != null)
            {
                _outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, color);
            }
        }

        /// <summary>把文字染成队伍墨色（与 InkTeamColorFilter 互补：这里只改填充层）。</summary>
        public void SetTeam(Team team)
        {
            SetFillColor(UIThemeSO.Resolve(theme).GetTeamColor(team));
        }

        public void ApplyTheme(UIThemeSO themeAsset)
        {
            theme = themeAsset;
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            if (useThemeColors)
            {
                outlineColor = resolved.outlineColor;
                outlineWidth = resolved.chunkyOutlineWidth;
                innerOutlineWidth = resolved.chunkyInnerOutlineWidth;
                shadowOffsetY = resolved.chunkyShadowOffsetY;
            }

            if (!Application.isPlaying)
            {
                ApplySerializedLook();
                return;
            }

            if (!_built) Build();
            else ApplyStyle();
        }

        /// <summary>在文本上叠一记弹性缩放（委托给同物体的 UISpringMotion）。</summary>
        public void PlayPop(float delay = 0f)
        {
            UISpringMotion motion = GetComponent<UISpringMotion>();
            if (motion == null) motion = gameObject.AddComponent<UISpringMotion>();

            motion.SetTheme(theme);
            motion.PlayIn(delay);
        }

        private void EnsureSource()
        {
            if (source == null) source = GetComponent<TMP_Text>();
        }

        public void Build()
        {
            EnsureSource();
            if (source == null) return;

            if (_built)
            {
                ApplyStyle();
                return;
            }

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            if (useThemeColors)
            {
                outlineColor = resolved.outlineColor;
                outlineWidth = resolved.chunkyOutlineWidth;
                innerOutlineWidth = resolved.chunkyInnerOutlineWidth;
                shadowOffsetY = resolved.chunkyShadowOffsetY;
            }

            TMP_FontAsset font = useDisplayFont ? resolved.displayFont : resolved.bodyFont;
            if (font != null) source.font = font;

            source.extraPadding = true;
            source.raycastTarget = false;

            if (source.font != null && (doubleLayer || outlined))
            {
                _outlineMaterial = CreateOutlineMaterial(source.font, outlineColor, outlineWidth);
                if (_outlineMaterial != null)
                {
                    source.fontMaterial = _outlineMaterial;
                    source.outlineColor = outlineColor;
                    source.outlineWidth = outlineWidth;
                }
            }

            source.color = doubleLayer ? outlineColor : fillColor;

            if (doubleLayer)
            {
                EnsureFillLayer();
                _fillLayer.font = source.font;
                _fillLayer.color = fillColor;

                if (source.font != null)
                {
                    _fillMaterial = CreateOutlineMaterial(source.font, fillColor, innerOutlineWidth);
                    if (_fillMaterial != null)
                    {
                        _fillLayer.fontMaterial = _fillMaterial;
                        _fillLayer.outlineColor = fillColor;
                        _fillLayer.outlineWidth = innerOutlineWidth;
                    }
                }
            }
            else if (_fillLayer != null)
            {
                _fillLayer.gameObject.SetActive(false);
            }

            _built = true;
            _dirty = true;
            SyncFillLayer();
            ApplyShadowOffset();
        }

        private void EnsureFillLayer()
        {
            if (_fillLayer != null) return;

            GameObject go = new GameObject("ChunkyFill", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(transform, false);

            _fillRect = (RectTransform)go.transform;
            _fillRect.anchorMin = Vector2.zero;
            _fillRect.anchorMax = Vector2.one;
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;

            _fillLayer = go.AddComponent<TextMeshProUGUI>();
            _fillLayer.raycastTarget = false;
            _fillLayer.extraPadding = true;
        }

        private void Update()
        {
            if (!autoSync || !_built) return;
            if (!_dirty && !NeedsSync()) return;

            SyncFillLayer();
            _dirty = false;
        }

        private bool NeedsSync()
        {
            if (_fillLayer == null || source == null) return false;

            return _fillLayer.text != source.text
                   || _fillLayer.font != source.font
                   || !Mathf.Approximately(_fillLayer.fontSize, source.fontSize)
                   || _fillLayer.fontStyle != source.fontStyle
                   || _fillLayer.alignment != source.alignment
                   || _fillLayer.textWrappingMode != source.textWrappingMode
                   || !Mathf.Approximately(_fillLayer.characterSpacing, source.characterSpacing)
                   || !Mathf.Approximately(_fillLayer.wordSpacing, source.wordSpacing)
                   || !Mathf.Approximately(_fillLayer.lineSpacing, source.lineSpacing)
                   || !Mathf.Approximately(_fillLayer.paragraphSpacing, source.paragraphSpacing)
                   || _fillLayer.margin != source.margin
                   || _fillLayer.overflowMode != source.overflowMode
                   || _fillLayer.enableAutoSizing != source.enableAutoSizing;
        }

        private void SyncFillLayer()
        {
            if (_fillLayer == null || source == null) return;

            _fillLayer.font = source.font;
            _fillLayer.fontSize = source.fontSize;
            _fillLayer.fontStyle = source.fontStyle;
            _fillLayer.alignment = source.alignment;
            _fillLayer.textWrappingMode = source.textWrappingMode;
            _fillLayer.characterSpacing = source.characterSpacing;
            _fillLayer.wordSpacing = source.wordSpacing;
            _fillLayer.lineSpacing = source.lineSpacing;
            _fillLayer.paragraphSpacing = source.paragraphSpacing;
            _fillLayer.margin = source.margin;
            _fillLayer.overflowMode = source.overflowMode;
            _fillLayer.enableAutoSizing = source.enableAutoSizing;
            _fillLayer.fontSizeMin = source.fontSizeMin;
            _fillLayer.fontSizeMax = source.fontSizeMax;
            _fillLayer.richText = source.richText;
            _fillLayer.text = source.text;

            ApplyShadowOffset();
        }

        private void ApplyShadowOffset()
        {
            if (_fillRect == null || source == null) return;

            float size = source.fontSize;
            Vector2 offset = new Vector2(shadowOffsetX * size, shadowOffsetY * size);
            _fillRect.offsetMin = offset;
            _fillRect.offsetMax = offset;
        }

        /// <summary>运行时刷新颜色/描边参数（不重建层级）。</summary>
        private void ApplyStyle()
        {
            if (source != null)
            {
                source.color = doubleLayer ? outlineColor : fillColor;

                if (source.fontMaterial != null && (doubleLayer || outlined))
                {
                    source.outlineColor = outlineColor;
                    source.outlineWidth = outlineWidth;
                }
            }

            if (_outlineMaterial != null)
            {
                _outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
                _outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(outlineWidth));
            }

            if (_fillLayer != null && _fillLayer.fontMaterial != null)
            {
                _fillLayer.color = fillColor;
                _fillLayer.outlineColor = fillColor;
                _fillLayer.outlineWidth = innerOutlineWidth;
            }

            if (_fillMaterial != null)
            {
                _fillMaterial.SetColor(ShaderUtilities.ID_OutlineColor, fillColor);
                _fillMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(innerOutlineWidth));
            }

            _dirty = true;
        }

        /// <summary>编辑模式预览：只写文本自身颜色与字体，绝不修改共享字体材质。</summary>
        private void ApplySerializedLook()
        {
            EnsureSource();
            if (source == null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            TMP_FontAsset font = useDisplayFont ? resolved.displayFont : resolved.bodyFont;
            if (font != null) source.font = font;

            source.extraPadding = true;
            source.color = fillColor;
        }

        private static Material CreateOutlineMaterial(TMP_FontAsset font, Color color, float width)
        {
            if (font == null || font.material == null) return null;

            Material material = new Material(font.material)
            {
                name = font.name + " (Chunky UI)"
            };
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, color);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp(width, 0f, 1f));
            material.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
            return material;
        }
    }
}