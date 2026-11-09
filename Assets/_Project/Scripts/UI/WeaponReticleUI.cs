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
    /// 1. 枪形（小枪 / 双枪 / 泼桶 / 爆破枪 / 加特林）：共享「中心点 + 4 向散布刻度」读数语言，
    ///    外壳按武器身份切换（P40，对齐 iw-ret--*）：小枪细环 / 双枪双小环 + 架枪菱形 /
    ///    泼桶拱形 + 落点斗 / 爆破枪内外双环 / 加特林 8 段蓄力环；
    /// 2. 滚筒：左右括号 + 底部落点弧线（表达推地宽度，不再是一根像 HUD 进度条的胶囊）；
    /// 3. 蓄力狙：同心圆 + 左右收紧瞄准线 + 360° 蓄力进度环 + 满蓄一次性闪光；
    /// 4. 副武器冷却微胶囊：手雷投出后显示倒计时与充能条，冷却结束即收起（不弹「手雷就绪」提示）。
    /// 武器类型到容器形状 / 身份外形的映射分别走 <see cref="ReticleStateMath.ShapeFor"/> 与
    /// <see cref="ReticleStateMath.StyleFor"/>，新增武器类型不必改本文件的装配结构。
    /// P30 可读性重做：对齐 Inkwave 的「白芯 + 墨色硬描边」对比策略（旧版墨芯白边在亮地面只剩发丝线）；
    /// 刻度静止时收在环带内侧，开火随 Bloom 越过环带外扫，不再与环带重叠糊成一团。
    /// P40 克制化：主环 60→44、圆环不再随散布缩放（同一个信息只画一遍）、墨边 1.5→1.2、
    /// 满蓄改一次性闪光（几何风禁止循环脉冲）、移动端整体 ×1.28 与字号同源。
    /// P41 亮背景可读性：P40 把墨边压到白芯的一半（4.8:2.4），米黄地面/天空反射下墨边撑不起轮廓；
    /// 现改为墨边 = 白芯（各环 outlineThickness 提到与 coreThickness 持平），亮地面上环读作一条深色轮廓。
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
        private Image _shooterCenterDot;
        /// <summary>枪形上的蓄力环：只给「蓄力后扫射」的武器（加特林）显示，其它武器常隐。</summary>
        private Image _shooterChargeRing;

        // 枪形身份外形壳（P40：5 把弹丸武器各一套，同一时刻只亮一套）
        private RectTransform _basicStyleRoot;
        private RectTransform _twinStyleRoot;
        private RectTransform _lobStyleRoot;
        private RectTransform _blastStyleRoot;
        private RectTransform _spinStyleRoot;
        private ReticleStyle _shooterStyle = (ReticleStyle)(-1);

        /// <summary>小枪细主环（Basic 壳）；超距时换虚线贴图。</summary>
        private Image _shooterRing;

        /// <summary>双枪两枚小环（左右各一）与架枪时的菱形锁定框。</summary>
        private Image _twinRingLeft;
        private Image _twinRingRight;
        private readonly OutlinedBar[] _twinLockBars = new OutlinedBar[4];
        private float _twinLockWeight;

        /// <summary>泼桶：拱形 3 根 + 落点斗 3 根（几何风用直条拼曲线）。</summary>
        private readonly OutlinedBar[] _lobBars = new OutlinedBar[6];
        /// <summary>拱形 / 落点斗各自一个子根：前摇时整组位移，不用逐根改坐标。</summary>
        private RectTransform _lobArchRoot;
        private RectTransform _lobBucketRoot;

        /// <summary>爆破枪：外环 = 爆炸半径，内环 = 直击核心。</summary>
        private Image _blastOuterRing;
        private Image _blastInnerRing;

        /// <summary>加特林：8 段旋转蓄力刻度（蓄满后整圈提亮）。</summary>
        private readonly OutlinedBar[] _spinSegments = new OutlinedBar[8];
        /// <summary>加特林的暗主环：给 8 段刻度做参照，否则整组读成一朵六角星。</summary>
        private Image _spinRing;

        // 滚筒组件（P40：括号 + 落点弧线，替代像 HUD 进度条的胶囊横条）
        private readonly OutlinedBar[] _rollerBracketBars = new OutlinedBar[6];
        private readonly OutlinedBar[] _rollerArcBars = new OutlinedBar[3];
        private RectTransform _rollerArcRoot;
        private Image _rollerNotch;
        private float _rollerHalfWidth = 70f;

        // 蓄力狙组件
        private Image _chargerCenterDot;
        private Image _chargerTrackRing;
        private Image _chargerFillRing;
        private OutlinedBar _chargerLineLeftBar;
        private OutlinedBar _chargerLineRightBar;

        // 副武器冷却微胶囊组件（旧名 SubAimBadge，保留节点名以兼容旧测试/装配）
        private RectTransform _subAimBadge;
        private CanvasGroup _subAimCanvasGroup;
        private Image _subAimFill;
        private RectTransform _subAimBarBg;
        private TMP_Text _subAimTimeText;
        private bool _subAimMobile;
        private float _subAimBadgeCanvasHeight = -1f;

        /// <summary>
        /// 手雷冷却徽标落位（徽标中心距画布底边，参考像素）：钉在底部三卡正上方，
        /// 不再悬在屏幕正中挡瞄准视野。桌面 = 三卡顶边 144 + 14 间距 + 徽标半高 20；
        /// 移动端三卡顶边 118 + 14 + 半高 24。
        /// </summary>
        private const float BombBadgeBottomOffsetDesktop = 178f;
        private const float BombBadgeBottomOffsetMobile = 156f;
        private int _lastBombCooldownTenths = int.MinValue;

        // 程序化生成的 UI 贴图与 Sprite
        private Sprite _dotSprite;
        /// <summary>128px 基准环：蓄力狙轨道环 / 蓄力进度环 / 命中环（按显示尺寸缩小后仍够厚）。</summary>
        private Sprite _ringSprite;
        private Sprite _ringDashedSprite;
        /// <summary>44px 细环：小枪主环、爆破枪外环（超距时换 44px 虚线版）。</summary>
        private Sprite _ringSlimSprite;
        private Sprite _ringSlimDashedSprite;
        /// <summary>30px 小环：双枪左右两枚。</summary>
        private Sprite _ringTwinSprite;
        /// <summary>20px 极细环：爆破枪直击核心。</summary>
        private Sprite _ringCoreSprite;
        private Sprite _arcSprite;

        /// <summary>
        /// 白芯直条四周的墨边厚度（像素，单侧）。P41 由 1.2 提到 2.4：与圆环同一策略——
        /// 亮地面（米黄混凝土 / 天空反射）上高对比的墨边才是轮廓主体，1.2px 会被亮背景吃掉，
        /// 直条只剩发丝白线。2.4 仍小于主体直条的白芯厚度 3.2，直条不会整体发黑糊成一坨。
        /// </summary>
        private const float BarOutline = 2.4f;

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

        /// <summary>手雷冷却徽标的充能进度 0~1（1 = 冷却结束；调试 / 自动化测试用）。</summary>
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

        /// <summary>满蓄一次性闪光的时长（秒）与最大放大比例（几何风禁止循环脉冲）。</summary>
        private const float ChargeFlashDuration = 0.35f;
        private const float ChargeFlashScale = 0.14f;

        /// <summary>
        /// 移动端准星整体放大（与 UITypography 字号同源 ×1.28）：
        /// 44px 主环在 1920 参考画布上约 4mm，缩到 6.5 寸屏只剩 3mm，不放大读不清。
        /// </summary>
        private float _baseScale = 1f;

        /// <summary>当前「刚好蓄满」的起始时刻；-1 = 未满。反复调用幂等，蓄力环与蓄力狙共用。</summary>
        private float _fullChargeSince = -1f;

        /// <summary>「弹药耗尽」toast 总时长（秒）：很短，扣扳机才出现。</summary>
        private const float EmptyToastDuration = 0.9f;

        /// <summary>toast 距离画布底边的高度（参考像素）：落在手雷冷却徽标带（上缘约 198）之上，两者同显时互不叠压。</summary>
        private const float EmptyToastBottomOffset = 218f;

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

            _baseScale = UITypography.IsMobileLike ? ComicStyle.MobileFontBoost : 1f;

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
            DestroySpriteAndTexture(ref _ringSlimSprite);
            DestroySpriteAndTexture(ref _ringSlimDashedSprite);
            DestroySpriteAndTexture(ref _ringTwinSprite);
            DestroySpriteAndTexture(ref _ringCoreSprite);
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

            // 2. 圆环 Sprite：参数化生成（P40）。旧实现只有一张 128px 环，缩到 60px 显示后
            //    环带只剩 2.8px、边缘发虚；命中环缩到 30px 更是只剩 1.4px 线（等于看不见）。
            //    现在每种用途按显示尺寸定做，环宽就是设计值。
            //
            //    亮背景可读性（P41）：实测白芯(Color.white) 在米黄混凝土(#B09D78) 上只有 2.62:1、
            //    墨边(ComicStyle.Ink) 3.72:1，都够不到规范要求的 4.5:1。颜色本身不能动（几何风纯色，
            //    改色会牵动整套主题），能补的只有「把高对比的墨边画宽」——所以把各环的 outlineThickness
            //    一律提到与 coreThickness 持平：墨边总宽（两侧 2×outlineHalf）≥ 白芯总宽（2×coreHalf），
            //    亮地面上读到的是一条完整的深色轮廓，而不是一根发丝白线。coreThickness 保持不变，
            //    白芯仍在环带内侧、墨边在外侧（判定是 ringDiff<=coreHalf 画白、否则画墨），内外关系不变。
            _ringSprite = MakeRingSprite("ReticleRingTex", 128f, 6.4f, 6.4f);
            _ringDashedSprite = MakeRingSprite("ReticleDashedRingTex", 128f, 6.4f, 6.4f, dashed: true, segments: 18);
            _ringSlimSprite = MakeRingSprite("ReticleRingSlimTex", ReticleStateMath.ShooterRingDiameter, 2.4f, 2.4f);
            _ringSlimDashedSprite = MakeRingSprite("ReticleRingSlimDashedTex",
                ReticleStateMath.ShooterRingDiameter, 2.4f, 2.4f, dashed: true, segments: 14);
            _ringTwinSprite = MakeRingSprite("ReticleRingTwinTex", 30f, 2.2f, 2.2f);
            _ringCoreSprite = MakeRingSprite("ReticleRingCoreTex", 20f, 2.2f, 2.2f);

            // 3. 受击方向弧贴图 (256x256)：纯色几何硬边扇面
            _arcSprite = GenerateArcSprite();
        }

        /// <summary>
        /// 参数化圆环贴图：按「显示直径 / 白芯宽度 / 单侧墨边」直接生成，厚度不靠缩放凑。
        /// 贴图取显示尺寸的 2 倍（超采样），绘制时缩到 displayDiameter：白芯与墨边宽度即设计值。
        /// dashed = true 时按 segments 段切开，实段占 55%（与 INKWAVE 的超距虚线同节奏）。
        /// 几何风硬边、无柔光；Image.color 仍可正常染色。
        /// </summary>
        private static Sprite MakeRingSprite(string name, float displayDiameter, float coreThickness,
            float outlineThickness, bool dashed = false, int segments = 18)
        {
            float diameter = Mathf.Max(8f, displayDiameter);
            int size = Mathf.Max(16, Mathf.RoundToInt(diameter * 2f));
            const float solidFraction = 0.55f;

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float coreHalf = Mathf.Max(0.75f, coreThickness);
            float outlineHalf = Mathf.Max(0.5f, outlineThickness);
            // 墨边外缘收在贴图半径内侧 1px，避免圆环被边缘裁成直边。
            // P41 加粗墨边后这条保护依然成立：本式以内侧 1px 处为准，ringRadius 随厚度自动内收，
            // 外缘恒等于 center-1（外径不变，只是环带向内变宽）；且 coreHalf+outlineHalf 远小于 center，
            // 环不会退化成实心圆盘（128 环 12.8<127.5、细环 4.8<43.5、twin/core 4.4<29.5/19.5）。
            float ringRadius = Mathf.Max(coreHalf + outlineHalf, center - coreHalf - outlineHalf - 1f);
            float segmentDegrees = 360f / Mathf.Max(2, segments);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float ringDiff = Mathf.Abs(dist - ringRadius);
                    if (ringDiff > coreHalf + outlineHalf)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    if (dashed)
                    {
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;
                        if (Mathf.Repeat(angle / segmentDegrees, 1f) > solidFraction)
                        {
                            pixels[y * size + x] = Color.clear;
                            continue;
                        }
                    }

                    pixels[y * size + x] = ringDiff <= coreHalf ? Color.white : ComicStyle.Ink;
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

        /// <summary>
        /// 由两个端点生成一条白芯墨边线段（长轴自动对齐 from→to）。
        /// 条形的长轴沿本地 +Y，绕 Z 转 θ 后方向是 (-sinθ, cosθ)，所以 θ = atan2(-dx, dy)。
        /// 用端点而不是「中心 + 手算角度」拼形状：拱形/斗/菱形的端点能直接读懂，也不会转错方向。
        /// </summary>
        private static OutlinedBar CreateSegment(Transform parent, string name, Vector2 from, Vector2 to,
            float thickness)
        {
            Vector2 delta = to - from;
            float length = Mathf.Max(1f, delta.magnitude);
            float angle = Mathf.Atan2(-delta.x, delta.y) * Mathf.Rad2Deg;
            OutlinedBar bar = CreateOutlinedBar(parent, name, new Vector2(thickness, length), angle);
            bar.Root.anchoredPosition = (from + to) * 0.5f;
            return bar;
        }

        private void BuildShooterUI()
        {
            var go = new GameObject("ShooterReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _shooterContainer = go.GetComponent<RectTransform>();

            // P40：5 套身份外形壳并列，同一时刻只亮一套（ApplyShooterStyle 硬切）。
            // 共用「中心点 + 4 向散布刻度」的读数语言，只换轮廓——玩家不用读 HUD 也能认出武器。
            _basicStyleRoot = CreateStyleRoot("Style_Basic");
            _twinStyleRoot = CreateStyleRoot("Style_Twin");
            _lobStyleRoot = CreateStyleRoot("Style_Lob");
            _blastStyleRoot = CreateStyleRoot("Style_Blast");
            _spinStyleRoot = CreateStyleRoot("Style_Spin");

            BuildBasicStyle();
            BuildTwinStyle();
            BuildLobStyle();
            BuildBlastStyle();
            BuildSpinStyle();

            // 蓄力进度环：只给「蓄力后扫射」的加特林显示，套在 8 段刻度外侧（64px），
            // 其它枪形武器 Charge01 恒为 0，这一层始终隐藏。
            var chargeGo = new GameObject("ChargeRing", typeof(Image));
            chargeGo.transform.SetParent(_shooterContainer, false);
            _shooterChargeRing = chargeGo.GetComponent<Image>();
            _shooterChargeRing.sprite = _ringSprite;
            _shooterChargeRing.type = Image.Type.Filled;
            _shooterChargeRing.fillMethod = Image.FillMethod.Radial360;
            _shooterChargeRing.fillOrigin = (int)Image.Origin360.Top;
            _shooterChargeRing.fillClockwise = true;
            _shooterChargeRing.fillAmount = 0f;
            _shooterChargeRing.rectTransform.sizeDelta = new Vector2(64f, 64f);
            chargeGo.SetActive(false);

            // 4 个散布刻度 (白芯 3.2x10 + 1.2 墨边)：静止时收在主环内侧（15px），
            // 开火随 Bloom 越过主环向外扫。Lob 只用左右两枚、Blast 不用、Spin 只用上下两枚。
            float[] angles = { 0f, 90f, 180f, 270f };
            for (int i = 0; i < 4; i++)
            {
                _shooterBars[i] = CreateOutlinedBar(_shooterContainer, $"Tick_{i}", new Vector2(3.2f, 10f), angles[i]);
                _shooterBars[i].SetCoreColor(Color.white);
                _shooterBars[i].SetBackingColor(ComicStyle.Ink);
            }

            // 中心圆点 (9x9 px)：最后建，永远压在环与刻度之上。
            var dotGo = new GameObject("CenterDot", typeof(Image));
            dotGo.transform.SetParent(_shooterContainer, false);
            _shooterCenterDot = dotGo.GetComponent<Image>();
            _shooterCenterDot.sprite = _dotSprite;
            _shooterCenterDot.raycastTarget = false;
            _shooterCenterDot.rectTransform.sizeDelta = new Vector2(9f, 9f);

            ApplyShooterStyle(ReticleStyle.Basic);
        }

        private RectTransform CreateStyleRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_shooterContainer, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private void BuildBasicStyle()
        {
            // 小枪：一枚细主环（44px）。超距时换 44px 虚线版，厚度与实体版一致。
            var ringGo = new GameObject("MainRing", typeof(Image));
            ringGo.transform.SetParent(_basicStyleRoot, false);
            _shooterRing = ringGo.GetComponent<Image>();
            _shooterRing.sprite = _ringSlimSprite;
            _shooterRing.raycastTarget = false;
            _shooterRing.rectTransform.sizeDelta =
                new Vector2(ReticleStateMath.ShooterRingDiameter, ReticleStateMath.ShooterRingDiameter);
        }

        private void BuildTwinStyle()
        {
            // 双枪：左右各一枚小环（26px，中心距 ±17）。架枪时再叠一枚菱形锁定框。
            _twinRingLeft = CreateRingImage(_twinStyleRoot, "TwinRing_L", _ringTwinSprite, 26f);
            _twinRingRight = CreateRingImage(_twinStyleRoot, "TwinRing_R", _ringTwinSprite, 26f);
            _twinRingLeft.rectTransform.anchoredPosition = new Vector2(-17f, 0f);
            _twinRingRight.rectTransform.anchoredPosition = new Vector2(17f, 0f);

            // 菱形锁定框：4 条边按顶点连出来（22 半径），几何风用直条拼形状，不引入新贴图。
            Vector2 top = new Vector2(0f, 22f);
            Vector2 right = new Vector2(22f, 0f);
            Vector2 bottom = new Vector2(0f, -22f);
            Vector2 left = new Vector2(-22f, 0f);
            _twinLockBars[0] = CreateSegment(_twinStyleRoot, "Lock_TR", top, right, 2.6f);
            _twinLockBars[1] = CreateSegment(_twinStyleRoot, "Lock_BR", right, bottom, 2.6f);
            _twinLockBars[2] = CreateSegment(_twinStyleRoot, "Lock_BL", bottom, left, 2.6f);
            _twinLockBars[3] = CreateSegment(_twinStyleRoot, "Lock_TL", left, top, 2.6f);
        }

        private void BuildLobStyle()
        {
            // 泼桶：上方拱形（A(-24,14)→B(-10,24)→C(10,24)→D(24,14)）+ 下方落点斗（\_/）。
            // 拱尾 y=14 / 斗尾 y=-14，中间留 28px 空隙——若两端靠太近会连成一个六边形环，读不出「泼出去的弧线」。
            var archRoot = new GameObject("Arch", typeof(RectTransform));
            archRoot.transform.SetParent(_lobStyleRoot, false);
            _lobArchRoot = archRoot.GetComponent<RectTransform>();
            _lobArchRoot.anchorMin = _lobArchRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _lobArchRoot.pivot = new Vector2(0.5f, 0.5f);
            _lobArchRoot.anchoredPosition = Vector2.zero;

            _lobBars[0] = CreateSegment(_lobArchRoot, "Arch_L", new Vector2(-24f, 14f), new Vector2(-10f, 24f), 2.6f);
            _lobBars[1] = CreateSegment(_lobArchRoot, "Arch_Top", new Vector2(-10f, 24f), new Vector2(10f, 24f), 2.6f);
            _lobBars[2] = CreateSegment(_lobArchRoot, "Arch_R", new Vector2(10f, 24f), new Vector2(24f, 14f), 2.6f);

            var bucketRoot = new GameObject("Bucket", typeof(RectTransform));
            bucketRoot.transform.SetParent(_lobStyleRoot, false);
            _lobBucketRoot = bucketRoot.GetComponent<RectTransform>();
            _lobBucketRoot.anchorMin = _lobBucketRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _lobBucketRoot.pivot = new Vector2(0.5f, 0.5f);
            _lobBucketRoot.anchoredPosition = Vector2.zero;

            _lobBars[3] = CreateSegment(_lobBucketRoot, "Bucket_L", new Vector2(-18f, -14f), new Vector2(-5f, -22f), 2.6f);
            _lobBars[4] = CreateSegment(_lobBucketRoot, "Bucket_Bottom", new Vector2(-5f, -22f), new Vector2(5f, -22f), 2.6f);
            _lobBars[5] = CreateSegment(_lobBucketRoot, "Bucket_R", new Vector2(5f, -22f), new Vector2(18f, -14f), 2.6f);
        }

        private void BuildBlastStyle()
        {
            // 爆破枪：外环 = 爆炸半径，内环 = 直击核心。不画散布刻度（弹道是单发重弹）。
            _blastOuterRing = CreateRingImage(_blastStyleRoot, "BlastOuterRing", _ringSlimSprite, 52f);
            _blastInnerRing = CreateRingImage(_blastStyleRoot, "BlastInnerRing", _ringCoreSprite, 20f);
        }

        private void BuildSpinStyle()
        {
            // 加特林：8 段旋转蓄力刻度（26px 半径），段间 45°。蓄满时整圈提亮。
            // 先铺一枚暗主环做参照：少了它，8 根径向短条会读成一朵六角星而不是「旋转蓄力」。
            _spinRing = CreateRingImage(_spinStyleRoot, "SpinRing", _ringSlimSprite,
                ReticleStateMath.ShooterRingDiameter);
            for (int i = 0; i < _spinSegments.Length; i++)
            {
                float rad = i * 45f * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                _spinSegments[i] = CreateSegment(_spinStyleRoot, $"SpinSeg_{i}",
                    dir * 22f, dir * 30f, 2.4f);
            }
        }

        private static Image CreateRingImage(Transform parent, string name, Sprite sprite, float diameter)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            return image;
        }

        /// <summary>
        /// 切换枪形身份外形壳（硬切，几何大胆风不做过场）：只亮当前武器那一套，
        /// 同时决定 4 枚散布刻度里哪几枚参与（Blast 不画刻度，Lob 只留左右，Spin 只留上下）。
        /// </summary>
        private void ApplyShooterStyle(ReticleStyle style)
        {
            if (_basicStyleRoot == null || _shooterStyle == style)
                return;

            _shooterStyle = style;
            _basicStyleRoot.gameObject.SetActive(style == ReticleStyle.Basic);
            _twinStyleRoot.gameObject.SetActive(style == ReticleStyle.Twin);
            _lobStyleRoot.gameObject.SetActive(style == ReticleStyle.Lob);
            _blastStyleRoot.gameObject.SetActive(style == ReticleStyle.Blast);
            _spinStyleRoot.gameObject.SetActive(style == ReticleStyle.Spin);

            SetTickVisible(0, style == ReticleStyle.Basic || style == ReticleStyle.Twin || style == ReticleStyle.Spin);
            // 双枪的左右两枚小环已经占据水平方向，再叠左右刻度会压在小环上糊成一团 → 只留上下。
            SetTickVisible(1, style == ReticleStyle.Basic || style == ReticleStyle.Lob);
            SetTickVisible(2, style == ReticleStyle.Basic || style == ReticleStyle.Twin || style == ReticleStyle.Spin);
            SetTickVisible(3, style == ReticleStyle.Basic || style == ReticleStyle.Lob);
        }

        private void SetTickVisible(int index, bool visible)
        {
            if (index < 0 || index >= _shooterBars.Length) return;
            OutlinedBar bar = _shooterBars[index];
            if (bar?.Root != null && bar.Root.gameObject.activeSelf != visible)
                bar.Root.gameObject.SetActive(visible);
        }

        private void BuildRollerUI()
        {
            var go = new GameObject("RollerReticle", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _rollerContainer = go.GetComponent<RectTransform>();

            // P40：胶囊横条（读起来像底部 HUD 的进度条）改成「左右括号 + 落点弧线」：
            // 括号 = 推地宽度，弧线 = 墨带落点方向。宽度随 weapon.Config.rollWidth 缩放。
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "L" : "R";
                int b = i * 3;
                _rollerBracketBars[b] = CreateOutlinedBar(_rollerContainer, $"Bracket_{side}_V", new Vector2(3.2f, 24f));
                _rollerBracketBars[b + 1] = CreateOutlinedBar(_rollerContainer, $"Bracket_{side}_Top", new Vector2(10f, 3.2f));
                _rollerBracketBars[b + 2] = CreateOutlinedBar(_rollerContainer, $"Bracket_{side}_Bottom", new Vector2(10f, 3.2f));
            }

            // 落点弧线：3 段折线按端点拼一条浅笑弧（两端高、中间低），落在中心点下方。
            var arcRootGo = new GameObject("Arc", typeof(RectTransform));
            arcRootGo.transform.SetParent(_rollerContainer, false);
            _rollerArcRoot = arcRootGo.GetComponent<RectTransform>();
            _rollerArcRoot.anchorMin = _rollerArcRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _rollerArcRoot.pivot = new Vector2(0.5f, 0.5f);
            _rollerArcRoot.anchoredPosition = Vector2.zero;

            _rollerArcBars[0] = CreateSegment(_rollerArcRoot, "Arc_L", new Vector2(-14f, -8f), new Vector2(-6f, -17f), 2.6f);
            _rollerArcBars[1] = CreateSegment(_rollerArcRoot, "Arc_Bottom", new Vector2(-6f, -17f), new Vector2(6f, -17f), 2.6f);
            _rollerArcBars[2] = CreateSegment(_rollerArcRoot, "Arc_R", new Vector2(6f, -17f), new Vector2(14f, -8f), 2.6f);

            // 中心瞄准点：括号之间唯一实心元素，白芯 + 墨边（与枪形共用同一枚圆点贴图）。
            var notch = new GameObject("Notch", typeof(Image));
            notch.transform.SetParent(_rollerContainer, false);
            _rollerNotch = notch.GetComponent<Image>();
            _rollerNotch.sprite = _dotSprite;
            _rollerNotch.raycastTarget = false;
            _rollerNotch.rectTransform.sizeDelta = new Vector2(9f, 9f);

            LayoutRollerBrackets(_rollerHalfWidth);
        }

        /// <summary>按半宽摆放左右括号（竖直条 + 上下两个向内的短爪）。</summary>
        private void LayoutRollerBrackets(float halfWidth)
        {
            _rollerHalfWidth = halfWidth;
            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1f : 1f;
                int b = i * 3;
                _rollerBracketBars[b].Root.anchoredPosition = new Vector2(sign * halfWidth, 0f);
                _rollerBracketBars[b + 1].Root.anchoredPosition = new Vector2(sign * (halfWidth - 5f), 10.5f);
                _rollerBracketBars[b + 2].Root.anchoredPosition = new Vector2(sign * (halfWidth - 5f), -10.5f);
            }
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

            // 背景导轨环 (暗度 0.55：旧 0.3 在中场调背景上几乎不可见)；P40 收到 54px 与细环族一致
            var trackGo = new GameObject("TrackRing", typeof(Image));
            trackGo.transform.SetParent(_chargerContainer, false);
            _chargerTrackRing = trackGo.GetComponent<Image>();
            _chargerTrackRing.sprite = _ringSprite;
            _chargerTrackRing.rectTransform.sizeDelta = new Vector2(54f, 54f);
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
            _chargerFillRing.rectTransform.sizeDelta = new Vector2(54f, 54f);

            // 左右两条蓄力收束线 (白芯 16x3.2 + 墨边)，停在进度环外侧，蓄力时向内收 10px
            _chargerLineLeftBar = CreateOutlinedBar(_chargerContainer, "LineLeft", new Vector2(16f, 3.2f));
            _chargerLineLeftBar.Root.anchoredPosition = new Vector2(-38f, 0f);

            _chargerLineRightBar = CreateOutlinedBar(_chargerContainer, "LineRight", new Vector2(16f, 3.2f));
            _chargerLineRightBar.Root.anchoredPosition = new Vector2(38f, 0f);
        }

        /// <summary>
        /// 手雷冷却徽标（旧名 SubAimBadge）：底部三卡正上方一条几何风读数——
        /// 冷却中「[手雷图标] █████░░ 5.4s」，冷却一结束立刻收起（不再弹「手雷就绪」提示）。
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
            PositionSubAimBadge(); // 底部三卡正上方：按画布高度换算，见 PositionSubAimBadge 注释
            _subAimCanvasGroup = badgeGo.AddComponent<CanvasGroup>();
            _subAimCanvasGroup.alpha = 1f;
            _subAimCanvasGroup.blocksRaycasts = false;
            _subAimCanvasGroup.interactable = false;

            // 底板：墨黑实心 + 纸白硬边（几何大胆风胶囊签，无渐变 / 无阴影）
            var bg = badgeGo.GetComponent<UIInkShapeGraphic>();
            bg.raycastTarget = false;
            bg.Shape = UIInkShape.Pill; // 圆角化：手雷冷却徽标改胶囊
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

                var icon = iconGo.GetComponent<Image>();
                icon.sprite = bombSprite;
                icon.preserveAspect = true;
                icon.color = ComicStyle.Paper;
                icon.raycastTarget = false;
            }
            else
            {
                CreateBadgeText("Text", new Vector2(pad, 0f),
                    new Vector2(labelWidth, badgeHeight), TextAnchor.MiddleLeft,
                    "手雷", ComicStyle.Paper);
            }

            // 中部冷却进度条：宽度吃掉「左标签 + 右读数」之外的剩余空间，任何平台都不会挤字。
            float barX = pad + labelWidth + gap;
            float barWidth = Mathf.Max(64f, badgeWidth - barX - pad - timeWidth - gap);

            var barBgGo = new GameObject("BarBg", typeof(RectTransform), typeof(UIPillImage));
            barBgGo.transform.SetParent(_subAimBadge, false);
            _subAimBarBg = barBgGo.GetComponent<RectTransform>();
            var barBg = barBgGo.GetComponent<Image>();
            barBg.color = new Color(1f, 1f, 1f, 0.18f); // 圆角化：胶囊轨道
            barBg.raycastTarget = false;
            _subAimBarBg.anchorMin = _subAimBarBg.anchorMax = new Vector2(0f, 0.5f);
            _subAimBarBg.pivot = new Vector2(0f, 0.5f);
            _subAimBarBg.sizeDelta = new Vector2(barWidth, 10f);
            _subAimBarBg.anchoredPosition = new Vector2(barX, 0f);

            var barFillGo = new GameObject("BarFill", typeof(UIPillImage));
            barFillGo.transform.SetParent(barBgGo.transform, false);
            _subAimFill = barFillGo.GetComponent<Image>();
            _subAimFill.type = Image.Type.Filled; // 圆角化：胶囊填充，fillAmount 逻辑不变
            _subAimFill.fillMethod = Image.FillMethod.Horizontal;
            _subAimFill.fillOrigin = (int)Image.OriginHorizontal.Left;
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
                // P40：3.2×11 + 起始半径 11——命中标记整体收在准星主环以内，
                // 旧版 13~15px 长条叠上 44px 冲击环会糊成一枚「菱形靶心」，读不出是命中确认。
                OutlinedBar bar = CreateOutlinedBar(_hitMarkerRoot, $"HitTick_{i}", new Vector2(3.2f, 11f), angles[i]);
                bar.Root.anchorMin = new Vector2(0.5f, 0.5f);
                bar.Root.anchorMax = new Vector2(0.5f, 0.5f);
                bar.Root.pivot = new Vector2(0.5f, 0.5f);
                float rad = angles[i] * Mathf.Deg2Rad;
                bar.Root.anchoredPosition = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 11f;
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
            // P40：旧版缩到 30px 时环带只剩 1.4px（等于看不见）；36px 下环带 1.8px 且不越过准星主环。
            _hitRingRect.sizeDelta = new Vector2(36f, 36f);
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
            _hitMarkerTravel = killed ? 16f : 8f;
            _hitMarkerColor = killed
                ? Color.Lerp(teamColor, Color.white, 0.25f)
                : HitFeedbackMath.ComboTint(Color.white, teamColor, combo);
            _hitRingEnabled = fat;

            for (int i = 0; i < _hitBars.Length; i++)
            {
                OutlinedBar bar = _hitBars[i];
                if (bar == null) continue;
                bar.SetCoreColor(_hitMarkerColor);
                bar.SetSize(fat ? new Vector2(4.6f, 15f) : new Vector2(3.2f, 11f));
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
            _hitMarkerRoot.localScale = new Vector3(scale * _baseScale, scale * _baseScale, 1f);

            float alpha = progress < 0.45f ? 1f : 1f - EaseOutCubic((progress - 0.45f) / 0.55f);
            _hitMarkerGroup.alpha = Mathf.Clamp01(alpha);

            float spread = 11f + Mathf.Clamp(_hitCombo, 0, 6) * 1.2f + progress * _hitMarkerTravel;
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
            toastBg.Shape = UIInkShape.Pill; // 圆角化：空墨提示改胶囊
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

        /// <summary>
        /// 手雷冷却徽标贴画布底边居中（底部三卡正上方）：与空墨 toast 同一套
        /// 「锚点在父级中心 → 按画布高度换算成相对中心的 Y」落位，画布尺寸变化时重算。
        /// 徽标只在冷却期间可见，因此随 UpdateSubAim 每帧调用即可覆盖激活与分辨率变化两种时机。
        /// </summary>
        private void PositionSubAimBadge()
        {
            if (_subAimBadge == null || _canvasRect == null) return;
            float height = _canvasRect.rect.height;
            if (height <= 1f || Mathf.Approximately(height, _subAimBadgeCanvasHeight)) return;
            _subAimBadgeCanvasHeight = height;
            float bottomOffset = _subAimMobile ? BombBadgeBottomOffsetMobile : BombBadgeBottomOffsetDesktop;
            _subAimBadge.anchoredPosition = new Vector2(0f, bottomOffset - height * 0.5f);
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
                    float pop = (1f + 0.5f * _emptyPunch) * _baseScale;
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
            bool wasOnTarget = _targetWeight > 0.5f;
            _targetWeight = Mathf.MoveTowards(_targetWeight, reticleState.OnTarget ? 1f : 0f, unscaledDt * 10f);

            // 准星咬住目标的那一刻给一声极轻的 tick（旧版只有命中才有声）。
            // 只在 false→true 的边沿触发，AudioManager 侧还有 0.18s 节流兜底，
            // 扫视多个目标时不会变成连续响。
            if (!wasOnTarget && _targetWeight > 0.5f && AudioManager.Instance != null)
                AudioManager.Instance.PlayReticleLock();

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
            if (_shooterWeight <= 0.01f)
            {
                if (_shooterContainer.gameObject.activeSelf)
                {
                    _shooterContainer.gameObject.SetActive(false);
                    if (_shooterChargeRing != null && _shooterChargeRing.gameObject.activeSelf)
                        _shooterChargeRing.gameObject.SetActive(false);
                }
                return;
            }

            if (!_shooterContainer.gameObject.activeSelf) _shooterContainer.gameObject.SetActive(true);

            // 武器身份外壳：小枪 / 双枪 / 泼桶 / 爆破 / 加特林（唯一映射源 ReticleStateMath.StyleFor）
            ApplyShooterStyle(ReticleStateMath.StyleFor(weapon.CurrentWeaponKind));

            float alpha = _shooterWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
            float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
            _shooterContainer.localScale = Vector3.one * (_shooterWeight * lockScale * _baseScale);
            _shooterContainer.anchoredPosition = _emptyShakeOffset * _shooterWeight;

            // 散布 Bloom 只驱动 4 枚刻度（P40 起主环不再跟着缩放：同一个信息只画一遍）。
            // 静止收在主环内侧、开火越过主环外扫；空打（没子弹扣扳机）反向收拢，形成相反手感。
            float spreadDeg = ReticleStateMath.ClampSpread(weapon.CurrentSpreadDegrees);
            float tickOffset = ReticleStateMath.TickOffset(spreadDeg, _shotPunch, _emptyPunch);

            _shooterBars[0].Root.anchoredPosition = new Vector2(0f, tickOffset);
            _shooterBars[1].Root.anchoredPosition = new Vector2(-tickOffset, 0f);
            _shooterBars[2].Root.anchoredPosition = new Vector2(0f, -tickOffset);
            _shooterBars[3].Root.anchoredPosition = new Vector2(tickOffset, 0f);

            Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
            if (_isOutOfInk) tint = Color.Lerp(tint, ComicStyle.Red, 0.9f);
            tint.a = alpha;

            _shooterCenterDot.color = tint;
            _shooterCenterDot.rectTransform.localScale = Vector3.one *
                (1f + _shotPunch * 0.25f) * (1f - 0.45f * _farWeight);

            Color barBacking = ComicStyle.Ink;
            barBacking.a = alpha;
            for (int i = 0; i < 4; i++)
            {
                _shooterBars[i].SetCoreColor(tint);
                _shooterBars[i].SetBackingColor(barBacking);
            }

            UpdateShooterStyleShell(weapon, tint, barBacking, alpha);
            UpdateShooterChargeRing(weapon, tint, alpha);
        }

        /// <summary>
        /// 身份外壳的每帧表现：只有主环/外环参与「超距虚线」；双枪叠架枪锁定框、
        /// 泼桶随前摇抬拱、加特林 8 段随蓄力提亮——全部用已有的 <see cref="OutlinedBar"/> 直条拼，
        /// 不引入新贴图，几何风硬边一条不走样。
        /// </summary>
        private void UpdateShooterStyleShell(WeaponController weapon, Color tint, Color backing, float alpha)
        {
            switch (_shooterStyle)
            {
                case ReticleStyle.Basic:
                    _shooterRing.color = tint;
                    SetRingSprite(_shooterRing, _farWeight > 0.5f);
                    break;

                case ReticleStyle.Twin:
                {
                    _twinLockWeight = Mathf.MoveTowards(
                        _twinLockWeight, weapon.IsTurretLocked ? 1f : 0f, Time.unscaledDeltaTime * 9f);

                    // 架枪时两枚小环向外让开，把中心留给菱形锁定框（INKWAVE dualies 的 is-lock 同语义）
                    float ringOffset = 17f + _twinLockWeight * 3f;
                    _twinRingLeft.rectTransform.anchoredPosition = new Vector2(-ringOffset, 0f);
                    _twinRingRight.rectTransform.anchoredPosition = new Vector2(ringOffset, 0f);

                    Color twinColor = tint;
                    twinColor.a = alpha * (1f - _twinLockWeight * 0.35f);
                    _twinRingLeft.color = twinColor;
                    _twinRingRight.color = twinColor;

                    Color lockColor = Color.Lerp(Color.white, tint, 0.35f);
                    lockColor.a = alpha * _twinLockWeight;
                    Color lockBacking = ComicStyle.Ink;
                    lockBacking.a = lockColor.a;
                    for (int i = 0; i < _twinLockBars.Length; i++)
                    {
                        _twinLockBars[i].SetCoreColor(lockColor);
                        _twinLockBars[i].SetBackingColor(lockBacking);
                    }
                    break;
                }

                case ReticleStyle.Lob:
                {
                    // 前摇（甩桶抬手）：拱形整组上抬、落点斗整组下沉，读作「墨浪起手」。
                    float windup = weapon.IsWindingUp ? Mathf.Clamp01(weapon.Windup01) : 0f;
                    if (_lobArchRoot != null)
                        _lobArchRoot.anchoredPosition = new Vector2(0f, windup * 3f);
                    if (_lobBucketRoot != null)
                        _lobBucketRoot.anchoredPosition = new Vector2(0f, -windup * 2f);

                    for (int i = 0; i < _lobBars.Length; i++)
                    {
                        _lobBars[i].SetCoreColor(tint);
                        _lobBars[i].SetBackingColor(backing);
                    }
                    break;
                }

                case ReticleStyle.Blast:
                {
                    _blastOuterRing.color = tint;
                    SetRingSprite(_blastOuterRing, _farWeight > 0.5f);
                    Color core = tint;
                    core.a = alpha * 0.85f;
                    _blastInnerRing.color = core;
                    break;
                }

                case ReticleStyle.Spin:
                {
                    float charge = Mathf.Clamp01(weapon.Charge01);
                    bool spinning = weapon.IsCharging || weapon.IsStreaming;
                    float segmentAlpha = SpinSegmentAlpha(spinning, charge);

                    // 暗主环只做参照，不参与蓄力读数（蓄力读数是 8 段 + 外圈进度环）。
                    Color ringColor = tint;
                    ringColor.a = alpha * 0.4f;
                    _spinRing.color = ringColor;

                    Color segment = Color.Lerp(tint, Color.white, charge * 0.8f);
                    segment.a = alpha * segmentAlpha;
                    Color segmentBacking = ComicStyle.Ink;
                    segmentBacking.a = alpha * segmentAlpha;
                    for (int i = 0; i < _spinSegments.Length; i++)
                    {
                        _spinSegments[i].SetCoreColor(segment);
                        _spinSegments[i].SetBackingColor(segmentBacking);
                    }
                    break;
                }
            }
        }

        private static float SpinSegmentAlpha(bool spinning, float charge)
        {
            // 平时压到 0.35（不抢中心视野），转起来随蓄力提亮到 1。
            return spinning ? Mathf.Lerp(0.45f, 1f, charge) : 0.35f;
        }

        /// <summary>主环/外环在实线与超距虚线之间硬切（两张贴图只差分段，厚度一致）。</summary>
        private void SetRingSprite(Image ring, bool dashed)
        {
            if (ring == null) return;
            Sprite sprite = dashed ? _ringSlimDashedSprite : _ringSlimSprite;
            if (ring.sprite != sprite) ring.sprite = sprite;
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
            {
                FullChargeFlash(0f);
                return;
            }

            _shooterChargeRing.fillAmount = charge01;

            // 蓄力越满越白、环越亮；满蓄只闪一次（几何风禁止循环脉冲），给「可以松手了」的明确信号。
            Color ringColor = Color.Lerp(tint, Color.white, charge01 * 0.75f);
            ringColor.a = alpha;
            _shooterChargeRing.color = ringColor;
            _shooterChargeRing.rectTransform.localScale = Vector3.one * FullChargeFlash(charge01);
        }

        /// <summary>
        /// 满蓄一次性闪光缩放系数：正从「未满」跨到「满」时记起始时刻，之后 0.35s 内
        /// 走一条 sin 半波放大再归 1；回落/松手后重置。幂等，蓄力狙与加特林共用。
        /// </summary>
        private float FullChargeFlash(float charge01)
        {
            bool full = charge01 >= 0.999f;
            if (!full)
            {
                _fullChargeSince = -1f;
                return 1f;
            }

            if (_fullChargeSince < 0f) _fullChargeSince = Time.unscaledTime;
            float t = (Time.unscaledTime - _fullChargeSince) / ChargeFlashDuration;
            if (t >= 1f) return 1f;
            return 1f + ChargeFlashScale * Mathf.Sin(t * Mathf.PI);
        }

        private void UpdateRoller(WeaponController weapon, Color teamColor)
        {
            if (_rollerWeight > 0.01f)
            {
                if (!_rollerContainer.gameObject.activeSelf) _rollerContainer.gameObject.SetActive(true);

                float alpha = _rollerWeight * Mathf.Lerp(1f, ReticleStateMath.FarAlpha, _farWeight);
                float lockScale = Mathf.Lerp(1f, ReticleStateMath.TargetScale, _targetWeight);
                _rollerContainer.localScale = Vector3.one * (_rollerWeight * lockScale * _baseScale);
                _rollerContainer.anchoredPosition = _emptyShakeOffset * _rollerWeight;

                Color tint = Color.Lerp(Color.white, ComicStyle.Red, _targetWeight);
                if (_isOutOfInk) tint = Color.Lerp(tint, ComicStyle.Red, 0.9f);
                tint.a = alpha;
                Color backing = ComicStyle.Ink;
                backing.a = alpha;

                // 括号宽度 = 实际推地宽度（1.9m 基准映射到 140px）；挥击时向外张 6px，
                // 空打（没墨扣扳机）向内收 6px——与枪形刻度的「击发外扩 / 空打内收」同一套手感语言。
                float rollWidth = weapon.Config != null ? Mathf.Max(0.5f, weapon.Config.rollWidth) : 1.9f;
                float flick = weapon.IsFlicking ? 6f : 0f;
                LayoutRollerBrackets(70f * Mathf.Clamp(rollWidth / 1.9f, 0.6f, 1.4f) + flick - _emptyPunch * 6f);

                for (int i = 0; i < _rollerBracketBars.Length; i++)
                {
                    _rollerBracketBars[i].SetCoreColor(tint);
                    _rollerBracketBars[i].SetBackingColor(backing);
                }

                // 落点弧线：散布越大弧线整组越往下沉（墨带落点更散）。
                float spreadDeg = ReticleStateMath.ClampSpread(weapon.CurrentSpreadDegrees);
                float sag = Mathf.Clamp01((spreadDeg - ReticleStateMath.MinSpreadDegrees) / 5f) * 3f;
                if (_rollerArcRoot != null)
                    _rollerArcRoot.anchoredPosition = new Vector2(0f, -sag);
                for (int i = 0; i < _rollerArcBars.Length; i++)
                {
                    _rollerArcBars[i].SetCoreColor(tint);
                    _rollerArcBars[i].SetBackingColor(backing);
                }

                _rollerNotch.color = tint;
                _rollerNotch.rectTransform.localScale = Vector3.one * (1f + _shotPunch * 0.2f);
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
                _chargerContainer.localScale = Vector3.one * (_chargerWeight * lockScale * _baseScale);
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

                // 左右两条收紧线：从 ±38px 靠拢到 ±28px (对应 Inkwave var(--ch, 0) 收 10px)
                // 空打时再向内收 3px：没墨扣扳机的「落空感」与正常蓄力收束区分开。
                float lineOffset = Mathf.Lerp(38f, 28f, ch01) - _emptyPunch * 3f;
                if (_chargerLineLeftBar != null) _chargerLineLeftBar.Root.anchoredPosition = new Vector2(-lineOffset, 0f);
                if (_chargerLineRightBar != null) _chargerLineRightBar.Root.anchoredPosition = new Vector2(lineOffset, 0f);

                // 满蓄：只闪一次（几何风禁止循环脉冲），之后稳在白；回落/松手后重置。
                if (ch01 >= 0.999f)
                {
                    _chargerFillRing.rectTransform.localScale = Vector3.one * FullChargeFlash(ch01);
                    Color fullColor = Color.white;
                    fullColor.a = alpha;
                    _chargerFillRing.color = fullColor;
                    _chargerCenterDot.color = fullColor;
                }
                else
                {
                    FullChargeFlash(0f);
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
        /// 手雷冷却徽标：投出后显示倒计时与充能条，冷却一结束立刻收起（不再弹「手雷就绪」提示）。
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

            // 徽标只回答「还要等多久」：冷却结束即隐藏，就绪不再额外弹提示。
            if (remaining <= 0.001f)
            {
                HideBombCooldownBadge();
                return;
            }

            if (!_subAimBadge.gameObject.activeSelf)
            {
                _subAimBadge.gameObject.SetActive(true);
            }
            _subAimCanvasGroup.alpha = 1f;
            PositionSubAimBadge();

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
