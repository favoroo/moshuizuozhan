using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 武器专属屏幕准星控制器（Screen-space Reticle UI）：
    /// P9 第三人称起准星固定在屏幕正中心（相机中心射线即瞄准方向），不再随世界落点漂移。
    /// 借鉴 Inkwave hud.css (.iw-ret & .iw-subaim) 潮流设计：
    /// 1. 小枪：中心点 + 扩散圆环 + 4个随 Bloom 散布角度动态扩张/收缩的刻度角；
    /// 2. 滚筒：宽屏扇面横向指示条；
    /// 3. 蓄力狙：同心圆 + 左右收紧瞄准线 + 360° 动态蓄力进度环 (Charge01) + 满蓄力过冲脉冲；
    /// 4. 副武器瞄准微胶囊：按住副武器键时长条滑出，显示手雷耗墨与就绪状态。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WeaponReticleUI : MonoBehaviour
    {
        private Canvas _parentCanvas;
        private RectTransform _canvasRect;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;

        // 三种武器的主容器
        private RectTransform _shooterContainer;
        private RectTransform _rollerContainer;
        private RectTransform _chargerContainer;

        // 小枪组件
        private RectTransform[] _shooterTicks = new RectTransform[4];
        private Image[] _shooterTickImages = new Image[4];
        private Image _shooterRing;
        private Image _shooterCenterDot;

        // 滚筒组件
        private RectTransform _rollerBarRect;
        private Image _rollerBarImage;

        // 蓄力狙组件
        private Image _chargerCenterDot;
        private Image _chargerTrackRing;
        private Image _chargerFillRing;
        private Image _chargerLineLeftImage;
        private Image _chargerLineRightImage;
        private RectTransform _chargerLineLeft;
        private RectTransform _chargerLineRight;

        // 副武器微胶囊组件
        private RectTransform _subAimBadge;
        private CanvasGroup _subAimCanvasGroup;
        private Image _subAimFill;
        private Text _subAimText;

        // 程序化生成的 UI 贴图与 Sprite
        private Sprite _dotSprite;
        private Sprite _ringSprite;
        private Sprite _ringDashedSprite;
        private Sprite _boxSprite;
        private Sprite _pillSprite;
        private Sprite _arcSprite;

        // 打击标记（Hit Marker, P6-A）
        private RectTransform _hitMarkerRoot;
        private CanvasGroup _hitMarkerGroup;
        private readonly Image[] _hitTicks = new Image[4];
        private readonly RectTransform[] _hitTickRects = new RectTransform[4];
        private Image _hitRing;
        private RectTransform _hitRingRect;
        private float _hitMarkerTimer;
        private float _hitMarkerLifetime = 0.24f;
        private float _hitMarkerScale = 1f;
        private float _hitMarkerTravel = 10f;
        private bool _hitRingEnabled;
        private int _hitCombo;
        private float _lastHitTime = -99f;
        private float _lastHitSoundTime = -99f;
        private Color _hitMarkerColor = Color.white;

        // 击杀确认（P6-B，对齐 Inkwave is-killflash / .iw-kill__splat）：
        // 准星本体 scale 1.28→1.0 ease-out，准星上方弹一枚队伍色墨滴徽标（弹出过冲→回稳→上飘淡出）
        private RectTransform _splatBadgeRect;
        private CanvasGroup _splatBadgeGroup;
        private UIInkShapeGraphic _splatBadgeGraphic;
        private float _splatBadgeTimer;
        private float _killPopTimer;
        private const float KillPopDuration = 0.45f;
        private const float SplatBadgeDuration = 0.8f;

        // 定向受击指示弧（Damage Direction Arcs, P6-A）
        private RectTransform _damageArcLayer;
        private readonly List<DamageArc> _damageArcs = new List<DamageArc>(6);
        private readonly List<DamageArc> _damageArcPool = new List<DamageArc>(6);

        [Header("打击标记 (P6-A)")]
        [Tooltip("普通命中的标记停留时长（秒）")]
        [SerializeField] private float hitMarkerDuration = 0.24f;
        [Tooltip("重击（≥ heavyThreshold）标记时长")]
        [SerializeField] private float hitMarkerHeavyDuration = 0.32f;
        [Tooltip("击杀标记停留时长（更大更亮，带冲击环）")]
        [SerializeField] private float hitMarkerKillDuration = 0.55f;
        [Tooltip("重击/暴击判定伤害阈值：达到后刻度加粗并弹出冲击环")]
        [SerializeField] private float hitMarkerHeavyThreshold = 60f;
        [Tooltip("命中音效音量")]
        [SerializeField] private float hitMarkerSoundVolume = 0.62f;
        [Tooltip("连击判定回退窗口（秒）；PlayerConfigSO 存在时以配置为准")]
        [SerializeField] private float hitComboWindowFallback = 0.55f;

        [Header("定向受击指示弧 (P6-A)")]
        [Tooltip("屏幕上同时存在的最大受击弧数量")]
        [SerializeField] private int maxDamageArcs = 6;
        [Tooltip("受击弧保持满亮度的时长（秒）")]
        [SerializeField] private float damageArcHoldSeconds = 1.0f;
        [Tooltip("之后淡出时长（秒）")]
        [SerializeField] private float damageArcFadeSeconds = 0.6f;
        [Tooltip("受击弧贴图尺寸（像素）；弧线落在该尺寸的外缘")]
        [SerializeField] private float damageArcSizePixels = 300f;
        [Tooltip("受击弧颜色（红色预警）")]
        [SerializeField] private Color damageArcColor = new Color(1f, 0.22f, 0.28f, 0.95f);

        private sealed class DamageArc
        {
            public RectTransform Rect;
            public Image Image;
            public CombatantHealth Attacker;
            public Vector3 LastSourcePos;
            public float Age;
        }

        /// <summary>当前连击数（0~8），调试/自动化测试可读。</summary>
        public int HitCombo => _hitCombo;

        /// <summary>准星打击标记是否正在播放。</summary>
        public bool IsHitMarkerPlaying => _hitMarkerTimer > 0f;

        /// <summary>屏幕上当前存活的定向受击弧数量。</summary>
        public int ActiveDamageArcCount => _damageArcs.Count;

        /// <summary>准星当前是否处于超距状态（变暗 + 虚线）。</summary>
        public bool IsOutOfRange => _farWeight > 0.5f;

        /// <summary>准星当前是否锁定敌人（染红 + 放大）。</summary>
        public bool IsOnTarget => _targetWeight > 0.5f;

        private WeaponKind _lastKind = WeaponKind.Shooter;
        private int _lastShotsFired = -1;
        private float _shotPunch;
        private float _shooterWeight = 1f;
        private float _rollerWeight = 0f;
        private float _chargerWeight = 0f;
        private float _subAimWeight = 0f;
        private float _farWeight;
        private float _targetWeight;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

            // 屏幕中心准星必须直接挂在画布根：HudSafeArea 在刘海/挖孔屏横屏时会整体内缩，
            // 挂在 SafeArea 里会让准星中心偏离相机中心射线（Android 上表现为「静止瞄准也偏」）。
            // 运行时创建顺序可能是「先 AddComponent（此时还没挂到 Canvas 下）→ 再 SetParent」，
            // 因此这里每次初始化都校正一次父级，且放在早退之前。
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null && _rectTransform.parent != rootCanvas.transform)
            {
                _rectTransform.SetParent(rootCanvas.transform, false);
            }

            if (_shooterContainer != null) return;

            // P9：准星固定屏幕中心。显式锁定中心锚点，避免运行时被父级布局拉伸。
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.anchoredPosition = Vector2.zero;

            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas != null)
            {
                _canvasRect = _parentCanvas.GetComponent<RectTransform>();
            }

            GenerateProceduralSprites();
            BuildShooterUI();
            BuildRollerUI();
            BuildChargerUI();
            BuildSubAimBadge();
            BuildHitMarker();
            BuildDamageArcLayer();
        }

        private void OnEnable()
        {
            CombatantHealth.DamageApplied += HandleDamageApplied;
        }

        private void OnDisable()
        {
            CombatantHealth.DamageApplied -= HandleDamageApplied;
        }

        private void OnDestroy()
        {
            CombatantHealth.DamageApplied -= HandleDamageApplied;
            ClearDamageArcs();
            _damageArcs.Clear();
            _damageArcPool.Clear();
            if (_damageArcLayer != null)
            {
                Destroy(_damageArcLayer.gameObject);
                _damageArcLayer = null;
            }
            DestroyProceduralSprites();
        }

        private void DestroyProceduralSprites()
        {
            DestroySpriteAndTexture(ref _dotSprite);
            DestroySpriteAndTexture(ref _ringSprite);
            DestroySpriteAndTexture(ref _ringDashedSprite);
            DestroySpriteAndTexture(ref _boxSprite);
            DestroySpriteAndTexture(ref _pillSprite);
            DestroySpriteAndTexture(ref _arcSprite);
        }

        private static void DestroySpriteAndTexture(ref Sprite sprite)
        {
            if (sprite != null)
            {
                Texture2D tex = sprite.texture;
                if (tex != null)
                {
                    Destroy(tex);
                }
                Destroy(sprite);
                sprite = null;
            }
        }

        private void GenerateProceduralSprites()
        {
            // 1. 圆点 Sprite (64x64)：墨黑实心 + 白色硬描边（几何风纯色硬边，无柔光）
            Texture2D dotTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "ReticleDotTex" };
            Color[] dotCols = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    if (dist <= 18f)
                    {
                        dotCols[y * 64 + x] = ComicStyle.Ink;
                    }
                    else if (dist <= 26.5f)
                    {
                        dotCols[y * 64 + x] = Color.white;
                    }
                    else
                    {
                        dotCols[y * 64 + x] = Color.clear;
                    }
                }
            }
            dotTex.SetPixels(dotCols);
            dotTex.Apply();
            _dotSprite = Sprite.Create(dotTex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));

            // 2. 细圆环 Sprite (128x128)：墨黑实心环 + 白色双侧硬描边（几何风纯色硬边，无柔光）
            Texture2D ringTex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "ReticleRingTex" };
            Color[] ringCols = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f));
                    float ringDiff = Mathf.Abs(dist - 56f);
                    if (ringDiff <= 2.8f)
                    {
                        ringCols[y * 128 + x] = ComicStyle.Ink;
                    }
                    else if (ringDiff <= 5.2f)
                    {
                        ringCols[y * 128 + x] = Color.white;
                    }
                    else
                    {
                        ringCols[y * 128 + x] = Color.clear;
                    }
                }
            }
            ringTex.SetPixels(ringCols);
            ringTex.Apply();
            _ringSprite = Sprite.Create(ringTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            _ringDashedSprite = GenerateDashedRingSprite();

            // 3. 刻度矩形贴图 (32x32)：墨黑实心 + 白色硬描边
            Texture2D boxTex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "ReticleBoxTex" };
            Color[] boxCols = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool isBorder = x < 4 || x >= 28 || y < 4 || y >= 28;
                    boxCols[y * 32 + x] = isBorder ? Color.white : ComicStyle.Ink;
                }
            }
            boxTex.SetPixels(boxCols);
            boxTex.Apply();
            _boxSprite = Sprite.Create(boxTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));

            // 4. 纯色胶囊贴图 (64x32)：墨黑底 + 白色硬描边（几何风纯色硬边，无柔光）
            Texture2D pillTex = new Texture2D(64, 32, TextureFormat.RGBA32, false) { name = "ReticlePillTex" };
            Color[] pillCols = new Color[64 * 32];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = x < 16 ? (16 - x) : (x > 47 ? (x - 47) : 0);
                    float dy = Mathf.Abs(y - 15.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= 11f)
                    {
                        pillCols[y * 64 + x] = ComicStyle.Ink;
                    }
                    else if (dist <= 15.5f)
                    {
                        pillCols[y * 64 + x] = Color.white;
                    }
                    else
                    {
                        pillCols[y * 64 + x] = Color.clear;
                    }
                }
            }
            pillTex.SetPixels(pillCols);
            pillTex.Apply();
            _pillSprite = Sprite.Create(pillTex, new Rect(0, 0, 64, 32), new Vector2(0.5f, 0.5f));

            // 5. 受击方向弧贴图 (256x256)：纯色几何硬边扇面
            _arcSprite = GenerateArcSprite();
        }

        /// <summary>
        /// 超距状态用的虚线圆环：与实心环同半径/同描边，按 18 段 55% 实、45% 空切开。
        /// 几何风硬边、无柔光；Image.color 仍可正常染色。
        /// </summary>
        private static Sprite GenerateDashedRingSprite()
        {
            const int size = 128;
            const int segments = 18;
            const float solidFraction = 0.55f;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ReticleDashedRingTex"
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(63.5f, 63.5f);
            float segmentDegrees = 360f / segments;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float ringDiff = Mathf.Abs(dist - 56f);
                    if (ringDiff > 5.2f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;
                    float phase = Mathf.Repeat(angle / segmentDegrees, 1f);
                    if (phase > solidFraction)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    pixels[y * size + x] = ringDiff <= 2.8f ? ComicStyle.Ink : Color.white;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private Sprite GenerateArcSprite()
        {
            const int size = 256;
            const float halfSpanDegrees = 26f;
            const float inner01 = 0.72f;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ReticleArcTex" };
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float outer = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 delta = new Vector2(x, y) - center;
                    float radius01 = delta.magnitude / outer;
                    float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    float angleDelta = Mathf.Abs(Mathf.DeltaAngle(angle, 90f));

                    bool inRadius = radius01 >= inner01 && radius01 <= 0.98f;
                    bool inAngle = angleDelta <= halfSpanDegrees;

                    pixels[y * size + x] = (inRadius && inAngle)
                        ? Color.white
                        : Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private void BuildShooterUI()
        {
            var go = new GameObject("ShooterReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _shooterContainer = go.GetComponent<RectTransform>();

            // 中心圆点 (6x6 px)
            var dotGo = new GameObject("CenterDot", typeof(Image));
            dotGo.transform.SetParent(_shooterContainer, false);
            _shooterCenterDot = dotGo.GetComponent<Image>();
            _shooterCenterDot.sprite = _dotSprite;
            _shooterCenterDot.rectTransform.sizeDelta = new Vector2(6f, 6f);

            // 主圆环 (52x52 px)
            var ringGo = new GameObject("MainRing", typeof(Image));
            ringGo.transform.SetParent(_shooterContainer, false);
            _shooterRing = ringGo.GetComponent<Image>();
            _shooterRing.sprite = _ringSprite;
            _shooterRing.rectTransform.sizeDelta = new Vector2(52f, 52f);

            // 4 个刻度角 (Tick Marks: 12x3 px)
            float[] angles = new float[] { 0f, 90f, 180f, 270f };
            for (int i = 0; i < 4; i++)
            {
                var tickGo = new GameObject($"Tick_{i}", typeof(Image));
                tickGo.transform.SetParent(_shooterContainer, false);
                var img = tickGo.GetComponent<Image>();
                img.sprite = _boxSprite;
                var rt = tickGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(3f, 11f);
                rt.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                _shooterTicks[i] = rt;
                _shooterTickImages[i] = img;
            }
        }

        private void BuildRollerUI()
        {
            var go = new GameObject("RollerReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _rollerContainer = go.GetComponent<RectTransform>();

            // 宽屏横向指示条 (140x16 px)
            var barGo = new GameObject("Bar", typeof(Image));
            barGo.transform.SetParent(_rollerContainer, false);
            _rollerBarImage = barGo.GetComponent<Image>();
            _rollerBarImage.sprite = _pillSprite;
            _rollerBarRect = barGo.GetComponent<RectTransform>();
            _rollerBarRect.sizeDelta = new Vector2(140f, 14f);

            // 中间瞄准凹槽指示点
            var notch = new GameObject("Notch", typeof(Image));
            notch.transform.SetParent(_rollerContainer, false);
            var notchImg = notch.GetComponent<Image>();
            notchImg.sprite = _dotSprite;
            notch.GetComponent<RectTransform>().sizeDelta = new Vector2(5f, 5f);
        }

        private void BuildChargerUI()
        {
            var go = new GameObject("ChargerReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _chargerContainer = go.GetComponent<RectTransform>();

            // 中心双层圆点 (8x8 px)
            var dotGo = new GameObject("CenterDot", typeof(Image));
            dotGo.transform.SetParent(_chargerContainer, false);
            _chargerCenterDot = dotGo.GetComponent<Image>();
            _chargerCenterDot.sprite = _dotSprite;
            _chargerCenterDot.rectTransform.sizeDelta = new Vector2(8f, 8f);

            // 背景导轨环 (暗度 0.3)
            var trackGo = new GameObject("TrackRing", typeof(Image));
            trackGo.transform.SetParent(_chargerContainer, false);
            _chargerTrackRing = trackGo.GetComponent<Image>();
            _chargerTrackRing.sprite = _ringSprite;
            _chargerTrackRing.rectTransform.sizeDelta = new Vector2(62f, 62f);
            _chargerTrackRing.color = new Color(1f, 1f, 1f, 0.3f);

            // 360° 蓄力进度环 (Radial 360 Filled)
            var fillGo = new GameObject("ChargeFillRing", typeof(Image));
            fillGo.transform.SetParent(_chargerContainer, false);
            _chargerFillRing = fillGo.GetComponent<Image>();
            _chargerFillRing.sprite = _ringSprite;
            _chargerFillRing.type = Image.Type.Filled;
            _chargerFillRing.fillMethod = Image.FillMethod.Radial360;
            _chargerFillRing.fillOrigin = (int)Image.Origin360.Top;
            _chargerFillRing.fillClockwise = true;
            _chargerFillRing.fillAmount = 0f;
            _chargerFillRing.rectTransform.sizeDelta = new Vector2(62f, 62f);

            // 左右两条蓄力收束横线 (长 16px, 宽 2.5px)
            var leftGo = new GameObject("LineLeft", typeof(Image));
            leftGo.transform.SetParent(_chargerContainer, false);
            var leftImg = leftGo.GetComponent<Image>();
            leftImg.sprite = _boxSprite;
            _chargerLineLeftImage = leftImg;
            _chargerLineLeft = leftGo.GetComponent<RectTransform>();
            _chargerLineLeft.sizeDelta = new Vector2(16f, 2.5f);
            _chargerLineLeft.anchoredPosition = new Vector2(-46f, 0f);

            var rightGo = new GameObject("LineRight", typeof(Image));
            rightGo.transform.SetParent(_chargerContainer, false);
            var rightImg = rightGo.GetComponent<Image>();
            rightImg.sprite = _boxSprite;
            _chargerLineRightImage = rightImg;
            _chargerLineRight = rightGo.GetComponent<RectTransform>();
            _chargerLineRight.sizeDelta = new Vector2(16f, 2.5f);
            _chargerLineRight.anchoredPosition = new Vector2(46f, 0f);
        }

        private void BuildSubAimBadge()
        {
            var badgeGo = new GameObject("SubAimBadge", typeof(RectTransform));
            badgeGo.transform.SetParent(transform, false);
            _subAimBadge = badgeGo.GetComponent<RectTransform>();
            _subAimBadge.anchoredPosition = new Vector2(42f, 28f);
            _subAimCanvasGroup = badgeGo.AddComponent<CanvasGroup>();

            // 胶囊底板
            var bgGo = new GameObject("Bg", typeof(Image));
            bgGo.transform.SetParent(_subAimBadge, false);
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = _pillSprite;
            bgImg.color = new Color(0.08f, 0.06f, 0.12f, 0.88f);
            bgGo.GetComponent<RectTransform>().sizeDelta = new Vector2(72f, 22f);

            // 墨水进度条槽
            var barBgGo = new GameObject("BarBg", typeof(Image));
            barBgGo.transform.SetParent(_subAimBadge, false);
            var barBg = barBgGo.GetComponent<Image>();
            barBg.sprite = _pillSprite;
            barBg.color = new Color(1f, 1f, 1f, 0.2f);
            var barBgRect = barBgGo.GetComponent<RectTransform>();
            barBgRect.sizeDelta = new Vector2(34f, 6f);
            barBgRect.anchoredPosition = new Vector2(-12f, 0f);

            // 墨水进度填充条
            var barFillGo = new GameObject("BarFill", typeof(Image));
            barFillGo.transform.SetParent(barBgGo.transform, false);
            _subAimFill = barFillGo.GetComponent<Image>();
            _subAimFill.sprite = _pillSprite;
            _subAimFill.type = Image.Type.Filled;
            _subAimFill.fillMethod = Image.FillMethod.Horizontal;
            _subAimFill.fillAmount = 1f;
            barFillGo.GetComponent<RectTransform>().sizeDelta = new Vector2(34f, 6f);

            // 提示文字 "BOMB"
            var txtGo = new GameObject("Text", typeof(Text));
            txtGo.transform.SetParent(_subAimBadge, false);
            _subAimText = txtGo.GetComponent<Text>();
            _subAimText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _subAimText.fontSize = 10;
            _subAimText.alignment = TextAnchor.MiddleCenter;
            _subAimText.text = "BOMB";
            _subAimText.color = Color.white;
            var txtRect = txtGo.GetComponent<RectTransform>();
            txtRect.sizeDelta = new Vector2(30f, 18f);
            txtRect.anchoredPosition = new Vector2(18f, 0f);

            _subAimCanvasGroup.alpha = 0f;
            badgeGo.SetActive(false);
        }

        // ================================================================ 打击标记 (Hit Marker)

        /// <summary>
        /// 准星中央的 X 型打击标记：4 根 45° 刻度 + 可选的冲击环。
        /// 与准星同属一个 RectTransform，因此永远精确落在当前瞄准点（Inkwave 的 .iw-hit 同款结构）。
        /// </summary>
        private void BuildHitMarker()
        {
            var rootGo = new GameObject("HitMarker", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            _hitMarkerRoot = rootGo.GetComponent<RectTransform>();
            _hitMarkerRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _hitMarkerRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _hitMarkerRoot.pivot = new Vector2(0.5f, 0.5f);
            _hitMarkerRoot.anchoredPosition = Vector2.zero;
            _hitMarkerRoot.sizeDelta = new Vector2(64f, 64f);
            rootGo.transform.SetAsLastSibling();

            _hitMarkerGroup = rootGo.AddComponent<CanvasGroup>();
            _hitMarkerGroup.alpha = 0f;
            _hitMarkerGroup.blocksRaycasts = false;
            _hitMarkerGroup.interactable = false;

            float[] angles = { 45f, 135f, 225f, 315f };
            for (int i = 0; i < angles.Length; i++)
            {
                var tickGo = new GameObject($"HitTick_{i}", typeof(RectTransform), typeof(Image));
                tickGo.transform.SetParent(_hitMarkerRoot, false);
                var img = tickGo.GetComponent<Image>();
                img.sprite = _boxSprite;
                img.raycastTarget = false;
                img.color = Color.white;

                var rt = tickGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(4.5f, 15f);
                rt.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                float rad = angles[i] * Mathf.Deg2Rad;
                rt.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 9f;

                _hitTicks[i] = img;
                _hitTickRects[i] = rt;
            }

            var ringGo = new GameObject("HitRing", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(_hitMarkerRoot, false);
            _hitRing = ringGo.GetComponent<Image>();
            _hitRing.sprite = _ringSprite;
            _hitRing.raycastTarget = false;
            _hitRing.color = new Color(1f, 1f, 1f, 0f);
            _hitRingRect = ringGo.GetComponent<RectTransform>();
            _hitRingRect.anchorMin = new Vector2(0.5f, 0.5f);
            _hitRingRect.anchorMax = new Vector2(0.5f, 0.5f);
            _hitRingRect.pivot = new Vector2(0.5f, 0.5f);
            _hitRingRect.sizeDelta = new Vector2(30f, 30f);
            _hitRingRect.anchoredPosition = Vector2.zero;

            // P6-B 击杀墨滴徽标：Blob 形队伍色墨滴，平时 alpha=0 完全隐藏（几何风：不用图片资源，纯程序化形状）。
            // 挂准星根而不是 _hitMarkerRoot：HitMarker 的 CanvasGroup 在 0.55s 后淡出至 alpha=0，
            // 嵌套 CanvasGroup 会把徽标最后 0.25s 的上飘淡出连带截断（Inkwave 里 killEl 与 hitEl 也是兄弟节点）。
            var splatGo = new GameObject("KillSplatBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            splatGo.transform.SetParent(transform, false);
            splatGo.transform.SetAsLastSibling();
            _splatBadgeGraphic = splatGo.GetComponent<UIInkShapeGraphic>();
            _splatBadgeGraphic.Shape = UIInkShape.Blob;
            _splatBadgeGraphic.SpeechTail = false;
            _splatBadgeGraphic.BorderWidth = 0f;
            _splatBadgeGraphic.HardShadow = false;
            _splatBadgeGraphic.ShadowOffset = Vector2.zero;
            _splatBadgeGraphic.Halftone = false;
            _splatBadgeGraphic.GlossAlpha = 0f;
            _splatBadgeGraphic.DrawGloss = false;
            _splatBadgeGraphic.InnerRimAlpha = 0f;
            _splatBadgeGraphic.color = Color.white;
            _splatBadgeGraphic.raycastTarget = false;

            _splatBadgeRect = splatGo.GetComponent<RectTransform>();
            _splatBadgeRect.anchorMin = new Vector2(0.5f, 0.5f);
            _splatBadgeRect.anchorMax = new Vector2(0.5f, 0.5f);
            _splatBadgeRect.pivot = new Vector2(0.5f, 0.5f);
            _splatBadgeRect.sizeDelta = new Vector2(44f, 44f);
            _splatBadgeRect.anchoredPosition = new Vector2(0f, 66f);

            _splatBadgeGroup = splatGo.AddComponent<CanvasGroup>();
            _splatBadgeGroup.alpha = 0f;
            _splatBadgeGroup.blocksRaycasts = false;
            _splatBadgeGroup.interactable = false;
        }

        private void TriggerHitMarker(float damage, bool killed, int combo, Color teamColor)
        {
            if (_hitMarkerRoot == null)
                return;

            bool heavy = damage >= hitMarkerHeavyThreshold;
            bool fat = heavy || killed;

            _hitMarkerLifetime = killed
                ? hitMarkerKillDuration
                : (heavy ? hitMarkerHeavyDuration : hitMarkerDuration);
            _hitMarkerTimer = _hitMarkerLifetime;
            _hitMarkerScale = HitFeedbackMath.MarkerScale(damage, combo) * (killed ? 1.45f : 1f);
            _hitMarkerTravel = killed ? 18f : 10f;
            _hitMarkerColor = killed
                ? Color.Lerp(teamColor, Color.white, 0.25f)
                : HitFeedbackMath.ComboTint(Color.white, teamColor, combo);
            _hitRingEnabled = fat;

            for (int i = 0; i < _hitTicks.Length; i++)
            {
                if (_hitTicks[i] == null) continue;
                _hitTicks[i].color = _hitMarkerColor;
                if (_hitTickRects[i] != null)
                    _hitTickRects[i].sizeDelta = fat ? new Vector2(6.5f, 20f) : new Vector2(4.5f, 15f);
            }

            if (_hitRing != null)
                _hitRing.color = new Color(_hitMarkerColor.r, _hitMarkerColor.g, _hitMarkerColor.b, 0f);

            // P6-B 击杀确认：准星本体 killflash + 墨滴徽标（Inkwave kill tier：ret 与 killEl 同帧重启）
            if (killed)
            {
                _killPopTimer = KillPopDuration;
                _splatBadgeTimer = SplatBadgeDuration;
                if (_splatBadgeGraphic != null)
                {
                    Color splat = Color.Lerp(teamColor, Color.white, 0.15f);
                    splat.a = 1f;
                    _splatBadgeGraphic.color = splat;
                }
            }

            _hitMarkerGroup.alpha = 1f;
        }

        /// <summary>
        /// 击杀反馈动画（P6-B）：真实时间推进，Hitstop 冻结期间照常播放。
        /// 准星本体 scale 1.28→1.0 ease-out（不做旋转：蓄力狙横线保持正交）；
        /// 墨滴徽标 20% 内弹出到 1.25 过冲、35% 回稳归正、75% 后上飘淡出（对齐 Inkwave iw-kill-splat 曲线）。
        /// </summary>
        private void UpdateKillFeedback(float unscaledDt)
        {
            if (_killPopTimer > 0f)
            {
                _killPopTimer = Mathf.Max(0f, _killPopTimer - unscaledDt);
                float popT = 1f - _killPopTimer / KillPopDuration;
                float s = Mathf.LerpUnclamped(1.28f, 1f, EaseOutCubic(popT));
                _rectTransform.localScale = new Vector3(s, s, 1f);
                if (_killPopTimer <= 0f)
                    _rectTransform.localScale = Vector3.one;
            }

            if (_splatBadgeGroup == null || _splatBadgeRect == null)
                return;

            if (_splatBadgeTimer <= 0f)
            {
                if (_splatBadgeGroup.alpha > 0f)
                {
                    _splatBadgeGroup.alpha = 0f;
                    _splatBadgeRect.localScale = Vector3.one;
                    _splatBadgeRect.localRotation = Quaternion.identity;
                    _splatBadgeRect.anchoredPosition = new Vector2(0f, 66f);
                }
                return;
            }

            _splatBadgeTimer = Mathf.Max(0f, _splatBadgeTimer - unscaledDt);
            float p = 1f - _splatBadgeTimer / SplatBadgeDuration;

            float scale = p < 0.20f
                ? Mathf.Lerp(0.2f, 1.25f, EaseOutCubic(p / 0.20f))
                : p < 0.35f ? Mathf.Lerp(1.25f, 1f, (p - 0.20f) / 0.15f)
                : 1f;
            _splatBadgeRect.localScale = new Vector3(scale, scale, 1f);

            float rot = p < 0.35f ? Mathf.Lerp(-40f, 0f, EaseOutCubic(p / 0.35f)) : 0f;
            _splatBadgeRect.localRotation = Quaternion.Euler(0f, 0f, rot);

            float alpha = p < 0.20f
                ? EaseOutCubic(p / 0.20f)
                : p < 0.75f ? 1f
                : 1f - (p - 0.75f) / 0.25f;
            _splatBadgeGroup.alpha = Mathf.Clamp01(alpha);

            float rise = p < 0.75f ? 0f : Mathf.Lerp(0f, 10f, (p - 0.75f) / 0.25f);
            _splatBadgeRect.anchoredPosition = new Vector2(0f, 66f + rise);
        }

        /// <summary>
        /// 标记动画用「真实时间」推进：Hitstop 冻结游戏时标记仍完整播放，
        /// 玩家才能同时读到时间定格与 UI 命中确认。
        /// </summary>
        private void UpdateHitMarker(float unscaledDt)
        {
            if (_hitMarkerRoot == null)
                return;

            if (_hitMarkerTimer <= 0f)
            {
                if (_hitMarkerGroup != null && _hitMarkerGroup.alpha > 0f)
                    _hitMarkerGroup.alpha = 0f;
                return;
            }

            _hitMarkerTimer = Mathf.Max(0f, _hitMarkerTimer - unscaledDt);
            float progress = 1f - _hitMarkerTimer / Mathf.Max(0.01f, _hitMarkerLifetime);

            // 前 35% 从 1.30 过冲回 1.0，之后保持；整段时间在 45% 后开始淡出
            float pop = progress < 0.35f
                ? Mathf.LerpUnclamped(1.30f, 1f, EaseOutCubic(progress / 0.35f))
                : 1f;
            float scale = _hitMarkerScale * pop;
            _hitMarkerRoot.localScale = new Vector3(scale, scale, 1f);

            float alpha = progress < 0.45f ? 1f : 1f - EaseOutCubic((progress - 0.45f) / 0.55f);
            _hitMarkerGroup.alpha = Mathf.Clamp01(alpha);

            float spread = 9f + Mathf.Clamp(_hitCombo, 0, 6) * 1.2f + progress * _hitMarkerTravel;
            for (int i = 0; i < _hitTickRects.Length; i++)
            {
                RectTransform tick = _hitTickRects[i];
                if (tick == null) continue;

                float rad = tick.localRotation.eulerAngles.z * Mathf.Deg2Rad;
                tick.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * spread;
            }

            if (_hitRing != null && _hitRingRect != null)
            {
                float ringT = Mathf.Clamp01(progress / 0.7f);
                _hitRingRect.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.45f, EaseOutCubic(ringT));
                Color ringColor = _hitMarkerColor;
                ringColor.a = _hitRingEnabled ? (1f - ringT) * 0.95f : 0f;
                _hitRing.color = ringColor;
            }
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        // ================================================================ 定向受击指示弧 (Damage Direction Arcs)

        private void BuildDamageArcLayer()
        {
            if (_damageArcLayer != null)
                return;

            if (_parentCanvas == null)
                _parentCanvas = GetComponentInParent<Canvas>();
            if (_parentCanvas == null)
                return;

            if (_canvasRect == null)
                _canvasRect = _parentCanvas.GetComponent<RectTransform>();

            var go = new GameObject("DamageDirectionArcs", typeof(RectTransform));
            go.transform.SetParent(_parentCanvas.transform, false);
            _damageArcLayer = go.GetComponent<RectTransform>();
            _damageArcLayer.anchorMin = new Vector2(0.5f, 0.5f);
            _damageArcLayer.anchorMax = new Vector2(0.5f, 0.5f);
            _damageArcLayer.pivot = new Vector2(0.5f, 0.5f);
            _damageArcLayer.anchoredPosition = Vector2.zero;
            _damageArcLayer.sizeDelta = Vector2.zero;
            _damageArcLayer.SetAsLastSibling();

            int poolCount = Mathf.Max(1, maxDamageArcs);
            _damageArcPool.Clear();
            for (int i = 0; i < poolCount; i++)
            {
                var arcGo = new GameObject($"DamageArc_{i}", typeof(RectTransform), typeof(Image));
                arcGo.transform.SetParent(_damageArcLayer, false);

                var image = arcGo.GetComponent<Image>();
                image.sprite = _arcSprite;
                image.raycastTarget = false;
                image.color = damageArcColor;

                var rect = arcGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(damageArcSizePixels, damageArcSizePixels);
                arcGo.SetActive(false);

                _damageArcPool.Add(new DamageArc
                {
                    Rect = rect,
                    Image = image,
                    Attacker = null,
                    LastSourcePos = Vector3.zero,
                    Age = 0f
                });
            }
        }

        private void HandleDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            PlayerController player = PlayerController.LocalPlayer;
            if (player == null || player.Health == null || victim == null || attacker == null)
                return;

            // 1) 本地玩家打中敌人 → 准星打击标记 + 连击音高
            if (attacker == player.Health && victim != player.Health &&
                victim.Team.IsOpponentOf(player.Health.Team) && damage > 0f)
            {
                float now = Time.unscaledTime;
                float comboWindow = player.Health.Config != null
                    ? player.Health.Config.hitComboWindowSeconds
                    : hitComboWindowFallback;

                _hitCombo = now - _lastHitTime <= comboWindow
                    ? Mathf.Min(_hitCombo + 1, 8)
                    : 0;
                _lastHitTime = now;

                Color teamColor = InkSurfaceController.Instance != null
                    ? InkSurfaceController.Instance.GetTeamColor(player.Health.Team)
                    : Color.white;

                TriggerHitMarker(damage, killed, _hitCombo, teamColor);
                PlayHitSound(_hitCombo, killed);
                return;
            }

            // 2) 本地玩家受击 → 屏幕中央红色定向受击弧（伤害来源方向）
            if (victim == player.Health && attacker != victim)
            {
                SpawnDamageArc(attacker);
            }
        }

        private void PlayHitSound(int combo, bool killed)
        {
            AudioManager audio = AudioManager.Instance;
            if (audio == null)
                return;

            // 范围伤害一帧内命中多人时只发一次瞬态音，避免音效池被同帧刷爆
            float now = Time.unscaledTime;
            if (now - _lastHitSoundTime < 0.035f)
                return;
            _lastHitSoundTime = now;

            // P6-B 对齐 Inkwave 的双层命中音：UI tick 换用 hit_marker（连击每级升 1.5 半音），
            // hit_enemy 系列降级为受击点 3D「墨拍身体」音（AudioManager.PlayInkHitBody，CombatantHealth 统一触发）
            float pitch = HitFeedbackMath.ComboPitch(combo, killed);
            audio.PlayHitMarker(pitch, hitMarkerSoundVolume);
        }

        private void SpawnDamageArc(CombatantHealth attacker)
        {
            EnsureDamageArcLayer();
            if (_damageArcLayer == null || attacker == null)
                return;

            // 同一攻击者只保留一条弧，持续刷新寿命与来源位置，避免弹幕刷屏
            for (int i = 0; i < _damageArcs.Count; i++)
            {
                if (_damageArcs[i].Attacker == attacker)
                {
                    _damageArcs[i].Age = 0f;
                    _damageArcs[i].LastSourcePos = attacker.transform.position;
                    return;
                }
            }

            if (_damageArcs.Count >= Mathf.Max(1, maxDamageArcs))
            {
                DamageArc oldest = _damageArcs[0];
                _damageArcs.RemoveAt(0);
                if (oldest.Rect != null) oldest.Rect.gameObject.SetActive(false);
                oldest.Attacker = null;
                _damageArcPool.Add(oldest);
            }

            DamageArc arc;
            if (_damageArcPool.Count > 0)
            {
                arc = _damageArcPool[_damageArcPool.Count - 1];
                _damageArcPool.RemoveAt(_damageArcPool.Count - 1);
            }
            else
            {
                var go = new GameObject("DamageArc", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_damageArcLayer, false);

                var image = go.GetComponent<Image>();
                image.sprite = _arcSprite;
                image.raycastTarget = false;
                image.color = damageArcColor;

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(damageArcSizePixels, damageArcSizePixels);

                arc = new DamageArc
                {
                    Rect = rect,
                    Image = image,
                    Attacker = null,
                    LastSourcePos = Vector3.zero,
                    Age = 0f
                };
            }

            arc.Attacker = attacker;
            arc.LastSourcePos = attacker.transform.position;
            arc.Age = 0f;
            if (arc.Rect != null)
            {
                arc.Rect.gameObject.SetActive(true);
            }
            if (arc.Image != null)
            {
                arc.Image.color = damageArcColor;
            }

            _damageArcs.Add(arc);
        }

        private void UpdateDamageArcs(float unscaledDt)
        {
            if (_damageArcs.Count == 0)
                return;

            PlayerController player = PlayerController.LocalPlayer;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

            Camera cam = TopDownCameraFollow.Instance != null
                ? TopDownCameraFollow.Instance.CameraComponent
                : Camera.main;
            Vector3 camForward = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;

            float lifetime = Mathf.Max(0.1f, damageArcHoldSeconds + damageArcFadeSeconds);

            for (int i = _damageArcs.Count - 1; i >= 0; i--)
            {
                DamageArc arc = _damageArcs[i];
                if (arc.Rect == null)
                {
                    _damageArcs.RemoveAt(i);
                    continue;
                }

                arc.Age += unscaledDt;

                // 攻击者存活时实时跟随来源方向；死亡/销毁后保留最后一次方向
                if (arc.Attacker != null && arc.Attacker.IsAlive)
                    arc.LastSourcePos = arc.Attacker.transform.position;

                Vector3 toSource = arc.LastSourcePos - playerPos;
                float angle = HitFeedbackMath.SourceAngleDegrees(toSource, camForward, camRight);
                arc.Rect.localRotation = Quaternion.Euler(0f, 0f, -angle);

                float pop = Mathf.LerpUnclamped(1.22f, 1f, EaseOutCubic(arc.Age / 0.26f));
                arc.Rect.localScale = new Vector3(pop, pop, 1f);

                float alpha = arc.Age <= damageArcHoldSeconds
                    ? 1f
                    : 1f - Mathf.Clamp01((arc.Age - damageArcHoldSeconds) / Mathf.Max(0.01f, damageArcFadeSeconds));
                Color color = damageArcColor;
                color.a *= alpha * alpha;
                if (arc.Image != null) arc.Image.color = color;

                if (arc.Age >= lifetime)
                {
                    if (arc.Rect != null) arc.Rect.gameObject.SetActive(false);
                    arc.Attacker = null;
                    _damageArcPool.Add(arc);
                    _damageArcs.RemoveAt(i);
                }
            }
        }

        private void EnsureDamageArcLayer()
        {
            if (_damageArcLayer == null)
                BuildDamageArcLayer();
        }

        private void ClearDamageArcs()
        {
            for (int i = 0; i < _damageArcs.Count; i++)
            {
                if (_damageArcs[i].Rect != null)
                    _damageArcs[i].Rect.gameObject.SetActive(false);
                _damageArcs[i].Attacker = null;
                _damageArcPool.Add(_damageArcs[i]);
            }
            _damageArcs.Clear();
        }

        private void LateUpdate()
        {
            EnsureInitialized();

            // 打击反馈用真实时间推进：Hitstop 期间 UI 仍需完整播放（不被 timeScale 拖慢）
            float unscaledDt = Time.unscaledDeltaTime;
            if (_hitMarkerRoot == null) BuildHitMarker();
            EnsureDamageArcLayer();
            UpdateHitMarker(unscaledDt);
            UpdateKillFeedback(unscaledDt);
            UpdateDamageArcs(unscaledDt);

            var player = PlayerController.LocalPlayer;
            if (player == null || player.Health == null || !player.Health.IsAlive || !MatchManager.InputAllowed)
            {
                _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 0f, Time.deltaTime * 6f);
                return;
            }

            // 1. P9：准星固定在屏幕中心。根节点 anchor/pivot 已在 EnsureInitialized 锁为 (0.5, 0.5)，
            //    不再把世界地面瞄准点 WorldToScreenPoint 投影过来。

            // 2. 潜墨隐匿透明度
            bool isSwimming = player.Motor != null && player.Motor.IsSwimming;
            float targetAlpha = isSwimming ? 0.35f : 1.0f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, Time.deltaTime * 8f);

            // 3. 武器形态平滑插值
            var weapon = player.Weapon;
            if (weapon == null) return;

            // 3.1 准星状态：超距 → 变暗 + 虚线；锁定敌人 → 染红 + 放大（INKWAVE 的 is-far / is-target）。
            // 用真实时间过渡，Hitstop 期间状态也能读完；100ms linear 符合几何大胆风。
            WeaponKind kind = weapon.CurrentWeaponKind;
            _lastKind = kind;
            ReticleStateMath.State reticleState = ReticleStateMath.Compute(
                player.AimFlatDistance,
                weapon.EffectiveRange,
                player.AimTarget != null,
                kind);
            _farWeight = Mathf.MoveTowards(_farWeight, reticleState.OutOfRange ? 1f : 0f, unscaledDt * 10f);
            _targetWeight = Mathf.MoveTowards(_targetWeight, reticleState.OnTarget ? 1f : 0f, unscaledDt * 10f);

            // 开火弹跳（P16）：每打出一发，准星刻度短促外扩再回弹。
            // 用 ShotsFired 增量检测，不新增事件订阅；滚筒推地每帧都在结算，排除掉避免持续抖动。
            int shotsFired = weapon.ShotsFired;
            if (_lastShotsFired >= 0 && shotsFired > _lastShotsFired && !weapon.IsRolling)
                _shotPunch = Mathf.Min(1f, _shotPunch + 0.55f);
            _lastShotsFired = shotsFired;
            _shotPunch = Mathf.MoveTowards(_shotPunch, 0f, unscaledDt * 7f);

            float dt = Time.deltaTime;

            float targetShooter = kind == WeaponKind.Shooter ? 1f : 0f;
            float targetRoller = kind == WeaponKind.Roller ? 1f : 0f;
            float targetCharger = kind == WeaponKind.Charger ? 1f : 0f;

            _shooterWeight = Mathf.MoveTowards(_shooterWeight, targetShooter, dt * 10f);
            _rollerWeight = Mathf.MoveTowards(_rollerWeight, targetRoller, dt * 10f);
            _chargerWeight = Mathf.MoveTowards(_chargerWeight, targetCharger, dt * 10f);

            // 4. 更新颜色
            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(player.Health.Team)
                : Color.white;

            // 小枪形态更新
            UpdateShooter(weapon, teamColor);

            // 滚筒形态更新
            UpdateRoller(weapon, teamColor);

            // 蓄力狙形态更新
            UpdateCharger(weapon, teamColor);

            // 副武器微胶囊更新
            UpdateSubAim(player, teamColor);
        }

        private void UpdateShooter(WeaponController weapon, Color teamColor)
        {
            if (_shooterWeight > 0.01f)
            {
                if (!_shooterContainer.gameObject.activeSelf) _shooterContainer.gameObject.SetActive(true);

                float alpha = _shooterWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _shooterContainer.localScale = Vector3.one * (_shooterWeight * lockScale);

                // 散布 Bloom: 1.2° ~ 5.5° 映射到 24px ~ 42px 刻度距离；开火再叠一层短促外扩
                float spreadDeg = weapon.CurrentSpreadDegrees;
                float tickOffset = 24f + (spreadDeg - 1.2f) * 4.2f + _shotPunch * 6f;

                // 4 个刻度沿自身角度向外偏移
                _shooterTicks[0].anchoredPosition = new Vector2(0f, tickOffset);
                _shooterTicks[1].anchoredPosition = new Vector2(-tickOffset, 0f);
                _shooterTicks[2].anchoredPosition = new Vector2(0f, -tickOffset);
                _shooterTicks[3].anchoredPosition = new Vector2(tickOffset, 0f);

                // 环根据 Bloom 散布微缩放
                float ringScale = 1.0f + (spreadDeg - 1.2f) * 0.06f;
                _shooterRing.rectTransform.localScale = Vector3.one * ringScale;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                tint.a = alpha;
                _shooterCenterDot.color = tint;
                _shooterCenterDot.rectTransform.localScale = Vector3.one *
                    (1f + _shotPunch * 0.35f) * (1f - 0.45f * _farWeight);
                _shooterRing.color = tint;
                Sprite ringSprite = _farWeight > 0.5f ? _ringDashedSprite : _ringSprite;
                if (_shooterRing.sprite != ringSprite) _shooterRing.sprite = ringSprite;
                for (int i = 0; i < 4; i++) _shooterTickImages[i].color = tint;
            }
            else if (_shooterContainer.gameObject.activeSelf)
            {
                _shooterContainer.gameObject.SetActive(false);
            }
        }

        private void UpdateRoller(WeaponController weapon, Color teamColor)
        {
            if (_rollerWeight > 0.01f)
            {
                if (!_rollerContainer.gameObject.activeSelf) _rollerContainer.gameObject.SetActive(true);

                float alpha = _rollerWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _rollerContainer.localScale = Vector3.one * (_rollerWeight * lockScale);

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                tint.a = alpha;
                _rollerBarImage.color = tint;
            }
            else if (_rollerContainer.gameObject.activeSelf)
            {
                _rollerContainer.gameObject.SetActive(false);
            }
        }

        private void UpdateCharger(WeaponController weapon, Color teamColor)
        {
            if (_chargerWeight > 0.01f)
            {
                if (!_chargerContainer.gameObject.activeSelf) _chargerContainer.gameObject.SetActive(true);

                float alpha = _chargerWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _chargerContainer.localScale = Vector3.one * (_chargerWeight * lockScale);

                float ch01 = weapon.Charge01;
                _chargerFillRing.fillAmount = ch01;

                // 超距时导轨环切虚线，整环与两条收束线同步变暗；锁定敌人时统一染红。
                Sprite trackSprite = _farWeight > 0.5f ? _ringDashedSprite : _ringSprite;
                if (_chargerTrackRing.sprite != trackSprite) _chargerTrackRing.sprite = trackSprite;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                Color trackColor = tint;
                trackColor.a = 0.3f * alpha;
                _chargerTrackRing.color = trackColor;

                Color lineColor = tint;
                lineColor.a = alpha;
                if (_chargerLineLeftImage != null) _chargerLineLeftImage.color = lineColor;
                if (_chargerLineRightImage != null) _chargerLineRightImage.color = lineColor;

                // 左右两条收紧线：从 ±46px 靠拢到 ±26px (对应 Inkwave var(--ch, 0))
                float lineOffset = Mathf.Lerp(46f, 26f, ch01);
                _chargerLineLeft.anchoredPosition = new Vector2(-lineOffset, 0f);
                _chargerLineRight.anchoredPosition = new Vector2(lineOffset, 0f);

                // 满蓄力闪烁与放大动效
                if (ch01 >= 0.999f)
                {
                    float flashPulse = 1f + Mathf.PingPong(Time.time * 6f, 0.20f);
                    _chargerFillRing.rectTransform.localScale = Vector3.one * flashPulse;
                    Color fullColor = Color.white;
                    fullColor.a = alpha;
                    _chargerFillRing.color = fullColor;
                    _chargerCenterDot.color = fullColor;
                }
                else
                {
                    _chargerFillRing.rectTransform.localScale = Vector3.one;
                    Color fillColor = Color.Lerp(teamColor, Color.white, ch01 * 0.7f);
                    fillColor = Color.Lerp(fillColor, ComicStyle.Red, _targetWeight);
                    fillColor.a = alpha;
                    _chargerFillRing.color = fillColor;
                    _chargerCenterDot.color = lineColor;
                }
            }
            else if (_chargerContainer.gameObject.activeSelf)
            {
                _chargerContainer.gameObject.SetActive(false);
            }
        }

        private void UpdateSubAim(PlayerController player, Color teamColor)
        {
            bool isAimingBomb = player.IsAimingBomb;
            float targetWeight = isAimingBomb ? 1f : 0f;
            _subAimWeight = Mathf.MoveTowards(_subAimWeight, targetWeight, Time.deltaTime * 10f);

            if (_subAimWeight > 0.01f)
            {
                if (!_subAimBadge.gameObject.activeSelf) _subAimBadge.gameObject.SetActive(true);
                _subAimCanvasGroup.alpha = _subAimWeight;
                _subAimBadge.localScale = Vector3.one * (0.8f + 0.2f * _subAimWeight);

                float bombCost = player.Weapon.Config != null ? player.Weapon.Config.bombInkCost : 55f;
                float curInk = player.Motor != null ? player.Motor.CurrentInk : 100f;
                bool enough = curInk >= bombCost;

                _subAimFill.fillAmount = Mathf.Clamp01(curInk / 100f);
                if (enough)
                {
                    _subAimFill.color = teamColor;
                    _subAimText.color = Color.white;
                }
                else
                {
                    // 缺墨警报闪烁
                    _subAimFill.color = new Color(1f, 0.24f, 0.36f, 1f);
                    _subAimText.color = new Color(1f, 0.48f, 0.55f, 1f);
                }
            }
            else if (_subAimBadge.gameObject.activeSelf)
            {
                _subAimBadge.gameObject.SetActive(false);
            }
        }
    }
}
