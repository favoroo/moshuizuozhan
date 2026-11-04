using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 武器专属屏幕准星控制器（Screen-space Reticle UI）：
    /// P9 第三人称起准星固定在屏幕正中心（相机中心射线即瞄准方向），不再随世界落点漂移。
    /// 借鉴 Inkwave hud.css (.iw-ret & .iw-subaim) 潮流设计：
    /// 1. 枪形（小枪 / 双枪 / 泼桶 / 爆破枪）：中心点 + 扩散圆环 + 4个随 Bloom 散布角度动态扩张/收缩的刻度角；
    /// 2. 滚筒：宽屏扇面横向指示条；
    /// 3. 蓄力环（蓄力狙 / 加特林）：同心圆 + 左右收紧瞄准线 + 360° 动态蓄力进度环 (Charge01) + 满蓄力过冲脉冲；
    /// 4. 副武器冷却微胶囊：手雷投出后显示倒计时与充能条，就绪时短促 READY。
    /// 武器类型到形状的映射统一走 <see cref="ReticleStateMath.ShapeFor"/>，新增武器类型不必改本文件。
    /// P30 可读性重做：对齐 Inkwave 的「白芯 + 墨色硬描边」对比策略（旧版墨芯白边在亮地面只剩发丝线）；
    /// 刻度静止时收在环带内侧，开火随 Bloom 越过环带外扫，不再与环带重叠糊成一团。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WeaponReticleUI : MonoBehaviour
    {
        private Canvas _parentCanvas;
        private RectTransform _canvasRect;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;

        /// <summary>初始化已完成（已建好子节点且已挂进画布根）：LateUpdate 的热路径据此早退。</summary>
        private bool _initComplete;

        // 三种武器的主容器
        private RectTransform _shooterContainer;
        private RectTransform _rollerContainer;
        private RectTransform _chargerContainer;

        // 小枪组件（刻度为白芯 + 墨边双层条，见 OutlinedBar）
        private readonly OutlinedBar[] _shooterBars = new OutlinedBar[4];
        private Image _shooterRing;
        private Image _shooterCenterDot;
        /// <summary>枪形上的蓄力环：只给「蓄力后扫射」的武器（加特林）显示，其它武器常隐。</summary>
        private Image _shooterChargeRing;

        // 滚筒组件
        private RectTransform _rollerBarRect;
        private Image _rollerBarImage;

        // 蓄力狙组件
        private Image _chargerCenterDot;
        private Image _chargerTrackRing;
        private Image _chargerFillRing;
        private OutlinedBar _chargerLineLeftBar;
        private OutlinedBar _chargerLineRightBar;

        // 副武器冷却微胶囊组件（旧名 SubAimBadge，保留节点名以兼容旧测试/装配）
        private RectTransform _subAimBadge;
        private CanvasGroup _subAimCanvasGroup;
        private UIInkShapeGraphic _subAimBg;
        private Image _subAimFill;
        private RectTransform _subAimBarBg;
        private Image _subAimIcon;
        private TMP_Text _subAimLabelText;
        private TMP_Text _subAimTimeText;
        private TMP_Text _subAimReadyText;
        private float _bombReadyFlashTimer;
        private bool _bombWasCooling;
        private bool _subAimMobile;
        private int _lastBombCooldownTenths = int.MinValue;

        // 程序化生成的 UI 贴图与 Sprite
        private Sprite _dotSprite;
        private Sprite _ringSprite;
        private Sprite _ringDashedSprite;
        private Sprite _pillSprite;
        private Sprite _arcSprite;

        /// <summary>白芯刻度条四周的墨边厚度（像素，单侧）。</summary>
        private const float BarOutline = 1.5f;

        // 打击标记（Hit Marker, P6-A）
        private RectTransform _hitMarkerRoot;
        private CanvasGroup _hitMarkerGroup;
        private readonly OutlinedBar[] _hitBars = new OutlinedBar[4];
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

        /// <summary>准星当前是否处于「墨水不够下一发」状态（准星染红 + 中央禁止叉）。</summary>
        public bool IsOutOfInk => _isOutOfInk;

        /// <summary>
        /// 空打冲击进度 0~1（1 = 刚拒射）：红叉弹跳 + 刻度反向收拢共用这条曲线。
        /// </summary>
        public float EmptyPunch01 => _emptyPunch;

        /// <summary>「弹药耗尽」toast 是否正在显示（调试 / 自动化测试用）。</summary>
        public bool IsEmptyToastVisible => _emptyToastTimer > 0f;

        /// <summary>toast 文案（自动化测试断言用；正常恒为「弹药耗尽」）。</summary>
        public string EmptyToastText => _emptyToastText != null ? _emptyToastText.text : string.Empty;

        /// <summary>手雷冷却徽标当前是否可见（调试 / 自动化测试用）。</summary>
        public bool IsBombCooldownBadgeVisible =>
            _subAimBadge != null && _subAimBadge.gameObject.activeSelf;

        /// <summary>手雷冷却徽标的充能进度 0~1（1 = 就绪；调试 / 自动化测试用）。</summary>
        public float BombCooldownBadgeFill01 => _subAimFill != null ? _subAimFill.fillAmount : 0f;

        private WeaponKind _lastKind = WeaponKind.Shooter;
        private int _lastShotsFired = -1;
        private float _shotPunch;
        private float _shooterWeight = 1f;
        private float _rollerWeight = 0f;
        private float _chargerWeight = 0f;
        private float _farWeight;
        private float _targetWeight;

        // 空墨/没子弹反馈（P36）：准星染红 + 中央禁止红叉 + 扣扳机空打震颤 + 底部短 toast
        private RectTransform _emptyInkWarningRoot;
        private CanvasGroup _emptyInkWarningGroup;
        private readonly OutlinedBar[] _emptyInkBars = new OutlinedBar[2];
        private RectTransform _emptyToastRoot;
        private CanvasGroup _emptyToastGroup;
        private TMP_Text _emptyToastText;
        private int _lastEmptyFeedbackStamp = -1;
        private bool _isOutOfInk;
        private bool _emptyBlinkOn = true;
        private float _emptyPunch;
        private float _emptyShake;
        private Vector2 _emptyShakeOffset;
        private float _emptyToastTimer;
        private float _emptyToastCanvasHeight = -1f;

        /// <summary>空打 red-X 冲击的衰减速度（1/s）：约 0.18s 读完，短促不拖沓。</summary>
        private const float EmptyPunchDecayPerSecond = 5.5f;

        /// <summary>空打震颤衰减速度（1/s）：约 0.22s。</summary>
        private const float EmptyShakeDecayPerSecond = 4.5f;

        /// <summary>空打震颤最大像素位移。</summary>
        private const float EmptyShakeAmplitude = 5f;

        /// <summary>「弹药耗尽」toast 总时长（秒）：很短，扣扳机才出现。</summary>
        private const float EmptyToastDuration = 0.9f;

        /// <summary>toast 距离画布底边的高度（参考像素）：落在底部三卡（顶边约 144）之上，不挡准星视野。</summary>
        private const float EmptyToastBottomOffset = 176f;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            // 初始化完成标记：旧实现每帧都在 LateUpdate 里跑 GetComponentInParent<Canvas>() + 父级比对，
            // 而这两件事只在「还没挂进画布根」时才需要。挂好之后直接早退，热路径只留一次 bool 判断。
            if (_initComplete) return;

            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();

            // 屏幕中心准星必须直接挂在画布根：HudSafeArea 在刘海/挖孔屏横屏时会整体内缩，
            // 挂在 SafeArea 里会让准星中心偏离相机中心射线（Android 上表现为「静止瞄准也偏」）。
            // 运行时创建顺序可能是「先 AddComponent（此时还没挂到 Canvas 下）→ 再 SetParent」，
            // 因此只有确实挂到画布根之后才允许锁定初始化。
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null && _rectTransform.parent != rootCanvas.transform)
            {
                _rectTransform.SetParent(rootCanvas.transform, false);
            }

            if (_shooterContainer != null)
            {
                // 已经建好且已挂进画布根：锁定，后续帧不再查找父画布。
                _initComplete = rootCanvas != null && _rectTransform.parent == rootCanvas.transform;
                return;
            }

            // P9：准星固定屏幕中心。显式锁定中心锚点，避免运行时被父级布局拉伸。
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.anchoredPosition = Vector2.zero;

            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _parentCanvas = rootCanvas;
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
            BuildEmptyInkFeedback();
            BuildDamageArcLayer();

            _initComplete = rootCanvas != null && _rectTransform.parent == rootCanvas.transform;
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
            // 对比度策略（P30 准星重做）：对齐 INKWAVE 的「白色主体 + 墨色硬描边」，
            // 在亮地面（白芯起作用）与暗墨面（墨边起作用）上都可读；旧版墨芯白边在亮场景里只剩发丝线。
            // 1. 圆点 Sprite (64x64)：白芯 + 墨色硬描边（几何风纯色硬边，无柔光）
            Texture2D dotTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "ReticleDotTex" };
            Color[] dotCols = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    if (dist <= 17f)
                    {
                        dotCols[y * 64 + x] = Color.white;
                    }
                    else if (dist <= 30f)
                    {
                        dotCols[y * 64 + x] = ComicStyle.Ink;
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

            // 2. 圆环 Sprite (128x128)：白芯环带 + 墨色双侧硬描边（几何风纯色硬边，无柔光）
            Texture2D ringTex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "ReticleRingTex" };
            Color[] ringCols = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f));
                    float ringDiff = Mathf.Abs(dist - 56f);
                    if (ringDiff <= 3f)
                    {
                        ringCols[y * 128 + x] = Color.white;
                    }
                    else if (ringDiff <= 5.5f)
                    {
                        ringCols[y * 128 + x] = ComicStyle.Ink;
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

            // 3. 纯色胶囊贴图 (64x32)：白芯 + 墨色硬描边（几何风纯色硬边，无柔光）
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
                        pillCols[y * 64 + x] = Color.white;
                    }
                    else if (dist <= 15.5f)
                    {
                        pillCols[y * 64 + x] = ComicStyle.Ink;
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

            // 4. 受击方向弧贴图 (256x256)：纯色几何硬边扇面
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
                    if (ringDiff > 5.5f)
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

                    pixels[y * size + x] = ringDiff <= 3f ? Color.white : ComicStyle.Ink;
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

        // ================================================================ 白芯刻度条（准星可读性 P30）

        /// <summary>
        /// 双层直角色条：墨色底板比白芯大一圈硬边描边。细小 UI（刻度/收束线）用贴图描边会在
        /// 短轴上被压成亚像素碎边，双层 Image 才能保证四边描边宽度一致、亮暗地面都读得清。
        /// </summary>
        private sealed class OutlinedBar
        {
            public RectTransform Root;
            public RectTransform Core;
            public RectTransform Backing;
            public Image CoreImage;
            public Image BackingImage;

            /// <summary>设置白芯尺寸；墨边自动外扩 <see cref="BarOutline"/>。</summary>
            public void SetSize(Vector2 coreSize)
            {
                Core.sizeDelta = coreSize;
                Backing.sizeDelta = coreSize + new Vector2(BarOutline * 2f, BarOutline * 2f);
            }

            public void SetCoreColor(Color color)
            {
                CoreImage.color = color;
            }

            public void SetBackingColor(Color color)
            {
                BackingImage.color = color;
            }
        }

        private static OutlinedBar CreateOutlinedBar(Transform parent, string name, Vector2 coreSize, float zAngle = 0f)
        {
            var rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            RectTransform root = rootGo.GetComponent<RectTransform>();
            root.localRotation = Quaternion.Euler(0f, 0f, zAngle);

            var backingGo = new GameObject("Backing", typeof(RectTransform), typeof(Image));
            backingGo.transform.SetParent(root, false);
            Image backingImg = backingGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(backingImg);
            backingImg.color = ComicStyle.Ink;
            backingImg.raycastTarget = false;

            var coreGo = new GameObject("Core", typeof(RectTransform), typeof(Image));
            coreGo.transform.SetParent(root, false);
            Image coreImg = coreGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(coreImg);
            coreImg.raycastTarget = false;

            var bar = new OutlinedBar
            {
                Root = root,
                Core = coreGo.GetComponent<RectTransform>(),
                Backing = backingGo.GetComponent<RectTransform>(),
                CoreImage = coreImg,
                BackingImage = backingImg
            };
            bar.SetSize(coreSize);
            return bar;
        }

        private void BuildShooterUI()
        {
            var go = new GameObject("ShooterReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _shooterContainer = go.GetComponent<RectTransform>();

            // 中心圆点 (11x11 px)：白芯 + 墨边
            var dotGo = new GameObject("CenterDot", typeof(Image));
            dotGo.transform.SetParent(_shooterContainer, false);
            _shooterCenterDot = dotGo.GetComponent<Image>();
            _shooterCenterDot.sprite = _dotSprite;
            _shooterCenterDot.rectTransform.sizeDelta = new Vector2(11f, 11f);

            // 主圆环 (60x60 px，环带半径约 26px)
            var ringGo = new GameObject("MainRing", typeof(Image));
            ringGo.transform.SetParent(_shooterContainer, false);
            _shooterRing = ringGo.GetComponent<Image>();
            _shooterRing.sprite = _ringSprite;
            _shooterRing.rectTransform.sizeDelta = new Vector2(60f, 60f);

            // 蓄力进度环：与蓄力狙同款 Radial360 填充，套在主环外侧（76px），
            // 加特林蓄力时才能看到进度条；其它枪形武器 Charge01 恒为 0，这一层始终隐藏。
            var chargeGo = new GameObject("ChargeRing", typeof(Image));
            chargeGo.transform.SetParent(_shooterContainer, false);
            _shooterChargeRing = chargeGo.GetComponent<Image>();
            _shooterChargeRing.sprite = _ringSprite;
            _shooterChargeRing.type = Image.Type.Filled;
            _shooterChargeRing.fillMethod = Image.FillMethod.Radial360;
            _shooterChargeRing.fillOrigin = (int)Image.Origin360.Top;
            _shooterChargeRing.fillClockwise = true;
            _shooterChargeRing.fillAmount = 0f;
            _shooterChargeRing.rectTransform.sizeDelta = new Vector2(76f, 76f);
            chargeGo.SetActive(false);

            // 4 个刻度 (白芯 4x12 + 墨边)：静止时收在环带内侧（r≈16，与圆点/环带各留空隙不粘连），
            // 开火随 Bloom 越过环带向外扫
            float[] angles = new float[] { 0f, 90f, 180f, 270f };
            for (int i = 0; i < 4; i++)
            {
                _shooterBars[i] = CreateOutlinedBar(_shooterContainer, $"Tick_{i}", new Vector2(4f, 12f), angles[i]);
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

            // 中间瞄准凹槽指示点：白芯条上必须用墨色才能读出来
            var notch = new GameObject("Notch", typeof(Image));
            notch.transform.SetParent(_rollerContainer, false);
            var notchImg = notch.GetComponent<Image>();
            notchImg.sprite = _dotSprite;
            notchImg.color = ComicStyle.Ink;
            notch.GetComponent<RectTransform>().sizeDelta = new Vector2(8f, 8f);
        }

        private void BuildChargerUI()
        {
            var go = new GameObject("ChargerReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _chargerContainer = go.GetComponent<RectTransform>();

            // 中心双层圆点 (10x10 px)
            var dotGo = new GameObject("CenterDot", typeof(Image));
            dotGo.transform.SetParent(_chargerContainer, false);
            _chargerCenterDot = dotGo.GetComponent<Image>();
            _chargerCenterDot.sprite = _dotSprite;
            _chargerCenterDot.rectTransform.sizeDelta = new Vector2(10f, 10f);

            // 背景导轨环 (暗度 0.55：旧 0.3 在中场调背景上几乎不可见)
            var trackGo = new GameObject("TrackRing", typeof(Image));
            trackGo.transform.SetParent(_chargerContainer, false);
            _chargerTrackRing = trackGo.GetComponent<Image>();
            _chargerTrackRing.sprite = _ringSprite;
            _chargerTrackRing.rectTransform.sizeDelta = new Vector2(62f, 62f);
            _chargerTrackRing.color = new Color(1f, 1f, 1f, 0.55f);

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

            // 左右两条蓄力收束线 (白芯 18x3.5 + 墨边)，停在进度环外侧，蓄力时向内收 10px
            _chargerLineLeftBar = CreateOutlinedBar(_chargerContainer, "LineLeft", new Vector2(18f, 3.5f));
            _chargerLineLeftBar.Root.anchoredPosition = new Vector2(-42f, 0f);

            _chargerLineRightBar = CreateOutlinedBar(_chargerContainer, "LineRight", new Vector2(18f, 3.5f));
            _chargerLineRightBar.Root.anchoredPosition = new Vector2(42f, 0f);
        }

        /// <summary>
        /// 手雷冷却徽标（旧名 SubAimBadge）：准星下方一条几何风读数——
        /// 冷却中「[手雷图标] █████░░ 5.4s」，就绪时整条翻黄显示「手雷就绪」，1.8s 后收起。
        ///
        /// 左侧优先用手雷图标（UIIconLibrary.sub_bomb），取不到时退回中文「手雷」；
        /// 不再使用 "BOMB" / "BOMB READY · E" 这类英文长标签。
        /// </summary>
        private void BuildSubAimBadge()
        {
            _subAimMobile = Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;

            float badgeWidth = _subAimMobile ? 340f : 292f;
            float badgeHeight = _subAimMobile ? 48f : 40f;
            float pad = 14f;
            Sprite bombSprite = UIIconLibrary.Get("sub_bomb");
            bool hasBombIcon = bombSprite != null;
            float labelWidth = hasBombIcon
                ? (_subAimMobile ? 42f : 36f)
                : (_subAimMobile ? 88f : 70f);
            float timeWidth = _subAimMobile ? 78f : 66f;
            float gap = 10f;

            var badgeGo = new GameObject("SubAimBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(transform, false);
            _subAimBadge = badgeGo.GetComponent<RectTransform>();
            _subAimBadge.anchorMin = _subAimBadge.anchorMax = new Vector2(0.5f, 0.5f);
            _subAimBadge.pivot = new Vector2(0.5f, 0.5f);
            _subAimBadge.sizeDelta = new Vector2(badgeWidth, badgeHeight);
            _subAimBadge.anchoredPosition = new Vector2(0f, _subAimMobile ? -84f : -68f);
            _subAimCanvasGroup = badgeGo.AddComponent<CanvasGroup>();
            _subAimCanvasGroup.alpha = 1f;
            _subAimCanvasGroup.blocksRaycasts = false;
            _subAimCanvasGroup.interactable = false;

            // 底板：墨黑实心 + 纸白硬边（几何大胆风，无圆角 / 无渐变 / 无阴影）
            var bg = badgeGo.GetComponent<UIInkShapeGraphic>();
            _subAimBg = bg;
            bg.raycastTarget = false;
            bg.Shape = UIInkShape.SharpRect;
            bg.CornerRadius = ComicStyle.CornerSharp;
            bg.BorderWidth = ComicStyle.BorderThin;
            bg.BorderColor = ComicStyle.Paper;
            bg.color = ComicStyle.Ink;
            bg.HardShadow = false;
            bg.ShadowOffset = Vector2.zero;
            bg.Halftone = false;
            bg.GlossAlpha = 0f;
            bg.DrawGloss = false;
            bg.InnerRimAlpha = 0f;

            // 左侧：优先画手雷图标（纯白剪影），取不到图标才退回中文标签。
            if (hasBombIcon)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(_subAimBadge, false);
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(pad, 0f);
                iconRect.sizeDelta = new Vector2(26f, 26f);

                _subAimIcon = iconGo.GetComponent<Image>();
                _subAimIcon.sprite = bombSprite;
                _subAimIcon.preserveAspect = true;
                _subAimIcon.color = ComicStyle.Paper;
                _subAimIcon.raycastTarget = false;
            }
            else
            {
                _subAimLabelText = CreateBadgeText("Text", new Vector2(pad, 0f),
                    new Vector2(labelWidth, badgeHeight), TextAnchor.MiddleLeft,
                    "手雷", ComicStyle.Paper);
            }

            // 中部冷却进度条：宽度吃掉「左标签 + 右读数」之外的剩余空间，任何平台都不会挤字。
            float barX = pad + labelWidth + gap;
            float barWidth = Mathf.Max(64f, badgeWidth - barX - pad - timeWidth - gap);

            var barBgGo = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            barBgGo.transform.SetParent(_subAimBadge, false);
            _subAimBarBg = barBgGo.GetComponent<RectTransform>();
            var barBg = barBgGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(barBg);
            barBg.color = new Color(1f, 1f, 1f, 0.18f);
            barBg.raycastTarget = false;
            _subAimBarBg.anchorMin = _subAimBarBg.anchorMax = new Vector2(0f, 0.5f);
            _subAimBarBg.pivot = new Vector2(0f, 0.5f);
            _subAimBarBg.sizeDelta = new Vector2(barWidth, 10f);
            _subAimBarBg.anchoredPosition = new Vector2(barX, 0f);

            var barFillGo = new GameObject("BarFill", typeof(Image));
            barFillGo.transform.SetParent(barBgGo.transform, false);
            _subAimFill = barFillGo.GetComponent<Image>();
            UIFlatSprite.MakeFlat(_subAimFill, filled: true);
            _subAimFill.color = ComicStyle.Yellow;
            _subAimFill.fillAmount = 0f;
            _subAimFill.raycastTarget = false;
            var barFillRect = barFillGo.GetComponent<RectTransform>();
            barFillRect.anchorMin = Vector2.zero;
            barFillRect.anchorMax = Vector2.one;
            barFillRect.offsetMin = Vector2.zero;
            barFillRect.offsetMax = Vector2.zero;

            // 右侧倒计时
            _subAimTimeText = CreateBadgeText("TimeText", new Vector2(-pad, 0f),
                new Vector2(timeWidth, badgeHeight), TextAnchor.MiddleRight,
                "6.0s", ComicStyle.Yellow);

            // 就绪态：整条黄底 + 墨字，替换掉冷却中的三块读数（比原来只在右侧写一个 READY 更醒目）。
            var readyGo = new GameObject("ReadyText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            readyGo.transform.SetParent(_subAimBadge, false);
            var readyRect = readyGo.GetComponent<RectTransform>();
            readyRect.anchorMin = Vector2.zero;
            readyRect.anchorMax = Vector2.one;
            readyRect.offsetMin = new Vector2(10f, 2f);
            readyRect.offsetMax = new Vector2(-10f, -2f);
            _subAimReadyText = readyGo.GetComponent<TextMeshProUGUI>();
            _subAimReadyText.font = UIThemeSO.ResolveGlobalFont(true, true);
            _subAimReadyText.fontSize = UITypography.FontSnapped(20f);
            _subAimReadyText.fontStyle = FontStyles.Bold;
            _subAimReadyText.alignment = TextAlignmentOptions.Center;
            _subAimReadyText.color = ComicStyle.Ink;
            _subAimReadyText.text = _subAimMobile ? "手雷就绪" : "手雷就绪 · E";
            _subAimReadyText.raycastTarget = false;
            _subAimReadyText.textWrappingMode = TextWrappingModes.NoWrap;
            _subAimReadyText.overflowMode = TextOverflowModes.Overflow;
            _subAimReadyText.gameObject.SetActive(false);

            badgeGo.SetActive(false);
        }

        /// <summary>
        /// 副武器徽标文字（BOMB / 冷却秒数）：走 TMP + 主题 display 字体，
        /// 不再用 LegacyRuntime.ttf（Arial 兜底字在真机缺字形且没有 SDF 描边）。
        /// 传入的 pos 按 TextAnchor 指定的一侧吸附（左/中/右），边框内留白由调用方给。
        /// </summary>
        private TMP_Text CreateBadgeText(string name, Vector2 pos, Vector2 size,
            TextAnchor anchor, string text, Color color)
        {
            var txtGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_subAimBadge, false);
            var txt = txtGo.GetComponent<TextMeshProUGUI>();
            txt.font = UIThemeSO.ResolveGlobalFont(true, ComicStyle.ContainsCjk(text));
            txt.fontSize = UITypography.FontSnapped(18f);
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = ToTmpAlignment(anchor);
            txt.text = text;
            txt.color = color;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Overflow;
            var rect = txtGo.GetComponent<RectTransform>();
            bool right = anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight;
            bool center = anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.LowerCenter;
            if (right)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
            }
            else if (center)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
            }
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            return txt;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
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
                OutlinedBar bar = CreateOutlinedBar(_hitMarkerRoot, $"HitTick_{i}", new Vector2(4.5f, 15f), angles[i]);
                bar.Root.anchorMin = new Vector2(0.5f, 0.5f);
                bar.Root.anchorMax = new Vector2(0.5f, 0.5f);
                bar.Root.pivot = new Vector2(0.5f, 0.5f);
                float rad = angles[i] * Mathf.Deg2Rad;
                bar.Root.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 9f;
                _hitBars[i] = bar;
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

            for (int i = 0; i < _hitBars.Length; i++)
            {
                OutlinedBar bar = _hitBars[i];
                if (bar == null) continue;
                bar.SetCoreColor(_hitMarkerColor);
                bar.SetSize(fat ? new Vector2(6.5f, 20f) : new Vector2(4.5f, 15f));
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
            for (int i = 0; i < _hitBars.Length; i++)
            {
                RectTransform tick = _hitBars[i]?.Root;
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

        // ================================================================ 空墨 / 没子弹反馈 (P36)

        /// <summary>
        /// 空墨反馈层（没子弹时，P36）：
        /// 1. 中央禁止红叉（两根 45° 粗刻度）——没墨时常驻并按 3.5Hz 方波硬闪，扣扳机被拒时弹跳；
        /// 2. 底部居中的短 toast「弹药耗尽」——只在拒射瞬间弹出 0.9s，落在底部三卡之上，不挡准星视野。
        /// 全部几何风：硬切、直角色块、无渐变，与 HUD 墨水罐的警告节奏一致。
        /// </summary>
        private void BuildEmptyInkFeedback()
        {
            var xGo = new GameObject("EmptyInkX", typeof(RectTransform), typeof(CanvasGroup));
            xGo.transform.SetParent(transform, false);
            _emptyInkWarningRoot = xGo.GetComponent<RectTransform>();
            _emptyInkWarningRoot.anchorMin = _emptyInkWarningRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _emptyInkWarningRoot.pivot = new Vector2(0.5f, 0.5f);
            _emptyInkWarningRoot.anchoredPosition = Vector2.zero;
            _emptyInkWarningRoot.sizeDelta = new Vector2(64f, 64f);
            _emptyInkWarningGroup = xGo.GetComponent<CanvasGroup>();
            _emptyInkWarningGroup.alpha = 0f;
            _emptyInkWarningGroup.blocksRaycasts = false;
            _emptyInkWarningGroup.interactable = false;

            float[] angles = { 45f, 135f };
            for (int i = 0; i < _emptyInkBars.Length; i++)
            {
                _emptyInkBars[i] = CreateOutlinedBar(_emptyInkWarningRoot, $"EmptyBar_{i}",
                    new Vector2(5f, 32f), angles[i]);
                _emptyInkBars[i].SetCoreColor(ComicStyle.Red);
                _emptyInkBars[i].SetBackingColor(ComicStyle.Ink);
            }
            xGo.SetActive(false);

            // toast：墨底 + 红色硬边 + 纸白字，尺寸压在 128×24；底部三卡顶边约 144，
            // 这里落在 176，既不挡准星中心射线，也不和墨水卡/大招卡/生命卡打架。
            var toastGo = new GameObject("EmptyInkToast",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            toastGo.transform.SetParent(transform, false);
            _emptyToastRoot = toastGo.GetComponent<RectTransform>();
            _emptyToastRoot.anchorMin = _emptyToastRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _emptyToastRoot.pivot = new Vector2(0.5f, 0.5f);
            _emptyToastRoot.sizeDelta = new Vector2(128f, 24f);

            var toastBg = toastGo.GetComponent<UIInkShapeGraphic>();
            toastBg.raycastTarget = false;
            toastBg.Shape = UIInkShape.SharpRect;
            toastBg.CornerRadius = ComicStyle.CornerSharp;
            toastBg.BorderWidth = ComicStyle.BorderThin;
            toastBg.BorderColor = ComicStyle.Red;
            toastBg.color = ComicStyle.Ink;
            toastBg.HardShadow = false;
            toastBg.ShadowOffset = Vector2.zero;
            toastBg.Halftone = false;
            toastBg.GlossAlpha = 0f;
            toastBg.DrawGloss = false;
            toastBg.InnerRimAlpha = 0f;

            _emptyToastGroup = toastGo.AddComponent<CanvasGroup>();
            _emptyToastGroup.alpha = 0f;
            _emptyToastGroup.blocksRaycasts = false;
            _emptyToastGroup.interactable = false;

            var txtGo = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_emptyToastRoot, false);
            _emptyToastText = txtGo.GetComponent<TextMeshProUGUI>();
            _emptyToastText.font = UIThemeSO.ResolveGlobalFont(true, true);
            _emptyToastText.fontSize = UITypography.FontSnapped(15f);
            _emptyToastText.fontStyle = FontStyles.Bold;
            _emptyToastText.alignment = TextAlignmentOptions.Center;
            _emptyToastText.color = ComicStyle.Paper;
            _emptyToastText.text = "弹药耗尽";
            _emptyToastText.raycastTarget = false;
            _emptyToastText.textWrappingMode = TextWrappingModes.NoWrap;
            _emptyToastText.overflowMode = TextOverflowModes.Overflow;
            var txtRect = txtGo.GetComponent<RectTransform>();
            txtRect.anchorMin = txtRect.anchorMax = new Vector2(0.5f, 0.5f);
            txtRect.pivot = new Vector2(0.5f, 0.5f);
            txtRect.sizeDelta = new Vector2(120f, 24f);
            txtRect.anchoredPosition = Vector2.zero;

            PositionEmptyToast();
            toastGo.SetActive(false);
        }

        /// <summary>
        /// toast 贴画布底边居中：画布高度/宽高比变化时重新落位，保证永远待在底部三卡上方，
        /// 不进入屏幕中心的瞄准视野（锚点是父级中心，所以要按画布高度换算成相对中心的 Y）。
        /// </summary>
        private void PositionEmptyToast()
        {
            if (_emptyToastRoot == null || _canvasRect == null) return;
            float height = _canvasRect.rect.height;
            if (height <= 1f || Mathf.Approximately(height, _emptyToastCanvasHeight)) return;
            _emptyToastCanvasHeight = height;
            _emptyToastRoot.anchoredPosition = new Vector2(0f, EmptyToastBottomOffset - height * 0.5f);
        }

        /// <summary>扣扳机被拒（空墨）：红叉弹跳 + 准星震颤 + 弹出「弹药耗尽」toast。</summary>
        private void TriggerEmptyPunch()
        {
            _emptyPunch = 1f;
            _emptyShake = 1f;
            _emptyToastTimer = EmptyToastDuration;
        }

        /// <summary>
        /// 空墨反馈每帧推进：红叉显隐 / 方波硬闪 / 空打弹跳，toast 的淡入淡出与自动收起。
        /// 全部走真实时间，Hitstop 期间也完整播放。
        /// </summary>
        private void UpdateEmptyInkFeedback(float unscaledDt)
        {
            PositionEmptyToast();

            if (_emptyInkWarningRoot != null)
            {
                bool showX = _isOutOfInk || _emptyPunch > 0.001f;
                if (_emptyInkWarningRoot.gameObject.activeSelf != showX)
                    _emptyInkWarningRoot.gameObject.SetActive(showX);

                if (showX)
                {
                    // 亮相位拉满、暗相位压到 0.35；空打瞬间无视相位直接拉满，保证一定被看到。
                    float blink = _isOutOfInk && !_emptyBlinkOn ? 0.35f : 1f;
                    _emptyInkWarningGroup.alpha = Mathf.Clamp01(blink + _emptyPunch * 0.8f);
                    float pop = 1f + 0.5f * _emptyPunch;
                    _emptyInkWarningRoot.localScale = new Vector3(pop, pop, 1f);
                }
            }

            if (_emptyToastTimer <= 0f || _emptyToastRoot == null)
                return;

            if (!_emptyToastRoot.gameObject.activeSelf)
                _emptyToastRoot.gameObject.SetActive(true);

            _emptyToastTimer = Mathf.Max(0f, _emptyToastTimer - unscaledDt);
            float elapsed = EmptyToastDuration - _emptyToastTimer;
            float fadeIn = Mathf.Clamp01(elapsed / 0.06f);
            float fadeOut = Mathf.Clamp01(_emptyToastTimer / 0.18f);
            _emptyToastGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            float toastPop = Mathf.Lerp(0.92f, 1f, Mathf.Clamp01(elapsed / 0.08f));
            _emptyToastRoot.localScale = new Vector3(toastPop, toastPop, 1f);

            if (_emptyToastTimer <= 0f)
            {
                _emptyToastGroup.alpha = 0f;
                _emptyToastRoot.gameObject.SetActive(false);
            }
        }

        private void HideEmptyInkToast()
        {
            _emptyToastTimer = 0f;
            if (_emptyToastGroup != null) _emptyToastGroup.alpha = 0f;
            if (_emptyToastRoot != null && _emptyToastRoot.gameObject.activeSelf)
                _emptyToastRoot.gameObject.SetActive(false);
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
                HideBombCooldownBadge();
                HideEmptyInkToast();
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
            {
                // P28：准星弹跳随蓄力刻度分层——蓄力狙点按小跳、满蓄大跳
                float strength = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(weapon.LastShotStrength01));
                _shotPunch = Mathf.Min(1f, _shotPunch + 0.55f * strength);
            }
            _lastShotsFired = shotsFired;
            _shotPunch = Mathf.MoveTowards(_shotPunch, 0f, unscaledDt * 7f);

            // 3.2 空墨 / 没子弹（P36）：墨水低于「下一发门槛」→ 准星整体染红 + 中央禁止叉；
            //     扣扳机被拒（EmptyFeedbackStamp 递增）→ 红叉弹跳 + 准星短震颤 + 底部「弹药耗尽」toast。
            //     门槛走 MainWeaponStrategy.InkRequiredToFire，蓄力狙/加特林/滚筒的起射条件与准星一致。
            ReticleStateMath.InkState inkState = ReticleStateMath.ComputeInk(
                player.Motor != null ? player.Motor.CurrentInk : 0f,
                weapon.InkRequiredToFire,
                Time.unscaledTime);
            _isOutOfInk = inkState.OutOfInk;
            _emptyBlinkOn = inkState.BlinkOn;

            int emptyStamp = weapon.EmptyFeedbackStamp;
            if (_lastEmptyFeedbackStamp < 0)
            {
                // 首帧只记录基线，避免把装配前的旧戳记当成一次真实拒射。
                _lastEmptyFeedbackStamp = emptyStamp;
            }
            else if (emptyStamp != _lastEmptyFeedbackStamp)
            {
                _lastEmptyFeedbackStamp = emptyStamp;
                TriggerEmptyPunch();
            }

            _emptyPunch = Mathf.MoveTowards(_emptyPunch, 0f, unscaledDt * EmptyPunchDecayPerSecond);
            _emptyShake = Mathf.MoveTowards(_emptyShake, 0f, unscaledDt * EmptyShakeDecayPerSecond);
            _emptyShakeOffset = Random.insideUnitCircle * (EmptyShakeAmplitude * _emptyShake);

            float dt = Time.deltaTime;

            // 武器类型 → 准星形状（唯一映射源 ReticleStateMath.ShapeFor）：
            // 双枪 / 泼桶 / 爆破枪走枪形，加特林与蓄力狙共用蓄力环。
            // 旧实现直接比较 kind == Shooter/Roller/Charger，导致这 4 把武器三个容器权重全为 0、准星整块消失。
            ReticleShape shape = ReticleStateMath.ShapeFor(kind);
            float targetShooter = shape == ReticleShape.Shooter ? 1f : 0f;
            float targetRoller = shape == ReticleShape.Roller ? 1f : 0f;
            float targetCharger = shape == ReticleShape.Charger ? 1f : 0f;

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

            // 副武器冷却徽标更新
            UpdateSubAim(player);

            // 空墨红叉 / 空打震颤 / 「弹药耗尽」toast
            UpdateEmptyInkFeedback(unscaledDt);
        }

        private void UpdateShooter(WeaponController weapon, Color teamColor)
        {
            if (_shooterWeight > 0.01f)
            {
                if (!_shooterContainer.gameObject.activeSelf) _shooterContainer.gameObject.SetActive(true);

                float alpha = _shooterWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _shooterContainer.localScale = Vector3.one * (_shooterWeight * lockScale);
                _shooterContainer.anchoredPosition = _emptyShakeOffset * _shooterWeight;

                // 散布 Bloom: 1.2° ~ 5.5° 映射到 16px ~ 38px 刻度距离（静止收在环带内侧，开火越过环带）；
                // 开火再叠一层短促外扩。0 散布武器（泼桶 / 双枪架枪）按 1.2° 下限计算，避免刻度叠在中心点上。
                // 空打（没子弹扣扳机）反向收拢 4px：与正常击发的向外弹跳形成相反手感。
                float spreadDeg = ReticleStateMath.ClampSpread(weapon.CurrentSpreadDegrees);
                float tickOffset = 16f + (spreadDeg - 1.2f) * 5.2f + _shotPunch * 5f - _emptyPunch * 4f;
                tickOffset = Mathf.Max(10f, tickOffset);

                // 4 个刻度沿自身角度向外偏移
                _shooterBars[0].Root.anchoredPosition = new Vector2(0f, tickOffset);
                _shooterBars[1].Root.anchoredPosition = new Vector2(-tickOffset, 0f);
                _shooterBars[2].Root.anchoredPosition = new Vector2(0f, -tickOffset);
                _shooterBars[3].Root.anchoredPosition = new Vector2(tickOffset, 0f);

                // 环根据 Bloom 散布微缩放
                float ringScale = 1.0f + (spreadDeg - 1.2f) * 0.06f;
                _shooterRing.rectTransform.localScale = Vector3.one * ringScale;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                if (_isOutOfInk) tint = Color.Lerp(tint, ComicStyle.Red, 0.9f);
                tint.a = alpha;
                _shooterCenterDot.color = tint;
                _shooterCenterDot.rectTransform.localScale = Vector3.one *
                    (1f + _shotPunch * 0.35f) * (1f - 0.45f * _farWeight);
                _shooterRing.color = tint;
                Sprite ringSprite = _farWeight > 0.5f ? _ringDashedSprite : _ringSprite;
                if (_shooterRing.sprite != ringSprite) _shooterRing.sprite = ringSprite;
                Color barBacking = ComicStyle.Ink;
                barBacking.a = alpha;
                for (int i = 0; i < 4; i++)
                {
                    _shooterBars[i].SetCoreColor(tint);
                    _shooterBars[i].SetBackingColor(barBacking);
                }

                UpdateShooterChargeRing(weapon, tint, alpha);
            }
            else if (_shooterContainer.gameObject.activeSelf)
            {
                _shooterContainer.gameObject.SetActive(false);
                if (_shooterChargeRing != null && _shooterChargeRing.gameObject.activeSelf)
                    _shooterChargeRing.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 加特林这类「蓄力后扫射」的枪形武器：蓄力阶段在主环外侧再套一圈径向蓄力环。
        /// 只认 IsCharging（扫射阶段 Charge01 会从 1 递减，此时若继续显示会与随 Bloom 外扩的刻度交叉）；
        /// 其它枪形武器 IsCharging 恒为 false、Charge01 恒为 0，这一层完全不显示，零额外开销。
        /// </summary>
        private void UpdateShooterChargeRing(WeaponController weapon, Color tint, float alpha)
        {
            if (_shooterChargeRing == null)
                return;

            float charge01 = Mathf.Clamp01(weapon.Charge01);
            bool show = weapon.IsCharging && charge01 > 0.02f;
            if (_shooterChargeRing.gameObject.activeSelf != show)
                _shooterChargeRing.gameObject.SetActive(show);
            if (!show)
                return;

            _shooterChargeRing.fillAmount = charge01;

            // 蓄力越满越白、环越亮；满蓄时轻微脉冲，给「可以松手了」的明确信号（与蓄力狙同语义）
            Color ringColor = Color.Lerp(tint, Color.white, charge01 * 0.75f);
            ringColor.a = alpha;
            _shooterChargeRing.color = ringColor;

            float pulse = charge01 >= 0.999f ? 1f + Mathf.PingPong(Time.time * 6f, 0.12f) : 1f;
            _shooterChargeRing.rectTransform.localScale = Vector3.one * pulse;
        }

        private void UpdateRoller(WeaponController weapon, Color teamColor)
        {
            if (_rollerWeight > 0.01f)
            {
                if (!_rollerContainer.gameObject.activeSelf) _rollerContainer.gameObject.SetActive(true);

                float alpha = _rollerWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _rollerContainer.localScale = Vector3.one * (_rollerWeight * lockScale);
                _rollerContainer.anchoredPosition = _emptyShakeOffset * _rollerWeight;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                if (_isOutOfInk) tint = Color.Lerp(tint, ComicStyle.Red, 0.9f);
                tint.a = alpha;
                _rollerBarImage.color = tint;

                // 空打：推地条横向收窄，与枪形的刻度内收同语义（没墨推不动）。
                float clench = 1f - 0.08f * _emptyPunch;
                _rollerBarImage.rectTransform.localScale = new Vector3(clench, 1f, 1f);
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
                _chargerContainer.anchoredPosition = _emptyShakeOffset * _chargerWeight;

                float ch01 = weapon.Charge01;
                _chargerFillRing.fillAmount = ch01;

                // 超距时导轨环切虚线，整环与两条收束线同步变暗；锁定敌人时统一染红。
                Sprite trackSprite = _farWeight > 0.5f ? _ringDashedSprite : _ringSprite;
                if (_chargerTrackRing.sprite != trackSprite) _chargerTrackRing.sprite = trackSprite;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                if (_isOutOfInk) tint = Color.Lerp(tint, ComicStyle.Red, 0.9f);
                Color trackColor = tint;
                trackColor.a = 0.3f * alpha;
                _chargerTrackRing.color = trackColor;

                Color lineColor = tint;
                lineColor.a = alpha;
                Color lineBacking = ComicStyle.Ink;
                lineBacking.a = alpha;
                if (_chargerLineLeftBar != null)
                {
                    _chargerLineLeftBar.SetCoreColor(lineColor);
                    _chargerLineLeftBar.SetBackingColor(lineBacking);
                }
                if (_chargerLineRightBar != null)
                {
                    _chargerLineRightBar.SetCoreColor(lineColor);
                    _chargerLineRightBar.SetBackingColor(lineBacking);
                }

                // 左右两条收紧线：从 ±42px 靠拢到 ±32px (对应 Inkwave var(--ch, 0) 收 10px)
                // 空打时再向内收 3px：没墨扣扳机的「落空感」与正常蓄力收束区分开。
                float lineOffset = Mathf.Lerp(42f, 32f, ch01) - _emptyPunch * 3f;
                if (_chargerLineLeftBar != null) _chargerLineLeftBar.Root.anchoredPosition = new Vector2(-lineOffset, 0f);
                if (_chargerLineRightBar != null) _chargerLineRightBar.Root.anchoredPosition = new Vector2(lineOffset, 0f);

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
                    fillColor = Color.Lerp(fillColor, ComicStyle.Red, Mathf.Max(_targetWeight, _isOutOfInk ? 0.8f : 0f));
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

        /// <summary>
        /// 手雷冷却徽标：投出后显示倒计时与充能条，冷却结束短促显示 READY 再隐藏。
        /// 旧的「按住瞄准 / 耗墨就绪」语义已随一键直投移除；徽标不再读取墨水。
        /// </summary>
        private void UpdateSubAim(PlayerController player)
        {
            var weapon = player != null ? player.Weapon : null;
            if (weapon == null)
            {
                HideBombCooldownBadge();
                return;
            }

            float remaining = weapon.BombCooldownRemaining;
            float duration = Mathf.Max(0.01f, weapon.BombCooldownDuration);
            bool cooling = remaining > 0.001f;

            // 冷却结束瞬间：整条翻黄显示「手雷就绪」，1.8s 后隐藏（旧版只换右侧 READY 小字，1.2s 常被错过）。
            if (_bombWasCooling && !cooling)
            {
                _bombReadyFlashTimer = 1.8f;
            }
            _bombWasCooling = cooling;

            if (_bombReadyFlashTimer > 0f)
            {
                _bombReadyFlashTimer -= Time.unscaledDeltaTime;
            }

            if (!cooling && _bombReadyFlashTimer <= 0f)
            {
                HideBombCooldownBadge();
                return;
            }

            if (!_subAimBadge.gameObject.activeSelf)
            {
                _subAimBadge.gameObject.SetActive(true);
            }
            _subAimCanvasGroup.alpha = 1f;

            if (cooling)
            {
                SetBombBadgeReadyState(false);

                // 充能式进度：0 → 1 表示剩余冷却逐渐恢复；数字同步倒计时
                _subAimFill.fillAmount = 1f - Mathf.Clamp01(remaining / duration);
                _subAimFill.color = ComicStyle.Yellow;

                int tenths = Mathf.Max(1, Mathf.CeilToInt(remaining * 10f));
                if (tenths != _lastBombCooldownTenths)
                {
                    _lastBombCooldownTenths = tenths;
                    _subAimTimeText.text = $"{tenths / 10f:0.0}s";
                    _subAimTimeText.color = ComicStyle.Yellow;
                }
            }
            else
            {
                SetBombBadgeReadyState(true);
                _subAimFill.fillAmount = 1f;
                _subAimFill.color = ComicStyle.Yellow;
                // 归位到「未写秒数」状态：下一次投出时第一帧就会刷新倒计时。
                _lastBombCooldownTenths = int.MinValue;
            }
        }

        /// <summary>
        /// 切换徽标的「冷却中 / 就绪」两态：
        /// 冷却 = 墨底 + 左标签 + 进度条 + 秒数；就绪 = 整条行动黄底 + 墨色「手雷就绪」。
        /// 两态子物体固定，只切 activeSelf / 颜色，不重建节点（零 GC）。
        /// </summary>
        private void SetBombBadgeReadyState(bool ready)
        {
            if (_subAimBg != null)
            {
                _subAimBg.color = ready ? ComicStyle.Yellow : ComicStyle.Ink;
                _subAimBg.BorderColor = ready ? ComicStyle.Ink : ComicStyle.Paper;
            }

            if (_subAimIcon != null)
            {
                // 冷却中墨底 → 纸白图标；就绪时黄底 → 墨色图标，保证对比度。
                _subAimIcon.color = ready ? ComicStyle.Ink : ComicStyle.Paper;
            }

            if (_subAimLabelText != null && _subAimLabelText.gameObject.activeSelf == ready)
            {
                _subAimLabelText.gameObject.SetActive(!ready);
            }
            if (_subAimBarBg != null && _subAimBarBg.gameObject.activeSelf == ready)
            {
                _subAimBarBg.gameObject.SetActive(!ready);
            }
            if (_subAimTimeText != null && _subAimTimeText.gameObject.activeSelf == ready)
            {
                _subAimTimeText.gameObject.SetActive(!ready);
            }
            if (_subAimReadyText != null && _subAimReadyText.gameObject.activeSelf != ready)
            {
                _subAimReadyText.gameObject.SetActive(ready);
            }
        }

        private void HideBombCooldownBadge()
        {
            _lastBombCooldownTenths = int.MinValue;
            if (_subAimBadge != null && _subAimBadge.gameObject.activeSelf)
            {
                _subAimBadge.gameObject.SetActive(false);
            }
        }
    }
}
