using System;
using System.Collections.Generic;
using P1.Core;
using UnityEngine;

namespace P1.Ink
{
    /// <summary>
    /// 场景涂地表面管理器：持有 InkGrid 与实时 Texture2D，每帧最多上传一次贴图，并同步给地面与斜坡材质。
    /// 支持动态墨水表面波纹（Ripples）与果冻材质属性驱动。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class InkSurfaceController : MonoBehaviour
    {
        public static InkSurfaceController Instance { get; private set; }

        public const int MaxRipples = 8;

        /// <summary>
        /// 涟漪合并窗口（秒）：同一落点在该时间内的重复波纹只抬升振幅，不再占用新槽位。
        /// 一次弹丸撞击会铺十几枚圆形墨斑，旧实现每枚都写一次波纹 → 8 个槽位被同一发子弹冲光，
        /// 玩家实际什么都看不到。合并后一发子弹 = 一条干净扩散的波纹。
        /// </summary>
        public const float RippleMergeWindow = 0.09f;

        /// <summary>涟漪合并半径（米）。</summary>
        public const float RippleMergeRadius = 0.55f;

        /// <summary>
        /// 墨迹图集向场地四周外扩的默认边距（米）：只用于避免场地边界处的双线性过滤/写入被图集边缘截断。
        /// 计分网格和可见受墨范围仍是 40x56 —— 场外表面（场外码头、边界墙顶）由
        /// <see cref="GlobalInkPlayableRectProp"/> 屏蔽，不显墨。
        /// </summary>
        private const float DefaultAtlasVisualMargin = 1.5f;

        [SerializeField] private InkGridConfigSO config;
        [SerializeField] private List<Renderer> inkableSurfaceRenderers = new List<Renderer>();
        [SerializeField] private List<Collider> blockedColliders = new List<Collider>();

        /// <summary>
        /// 没有碰撞体、但必须从计分里剔除的矩形禁区（xmin, zmin, xmax, zmax）。
        /// 用于可按可行走、却涂不上墨的地形（例如「海鸥港湾」的浅湾水线）：
        /// 若给水面加碰撞体会挡住角色/子弹，若不加又会被算进可涂面积。
        /// </summary>
        [SerializeField] private List<Vector4> blockedWorldRects = new List<Vector4>();

        [Header("动态墨水与材质参数")]
        [Tooltip("全局湿润度乘数，随时间或比赛阶段可动态调节")]
        [Range(0f, 2f)]
        [SerializeField] private float wetnessFactor = 1.0f;

        [Tooltip("胶质内发光强度：顶面/物件墨迹与地面 Mat_InkGround._InkGlow 对齐用，保证墨色暗部同样鲜艳")]
        [Range(0f, 1f)]
        [SerializeField] private float inkGlow = 0.2f;

        private InkGrid _grid;
        private Texture2D _inkTexture;
        private Texture2D _scoreMaskTexture;
        private byte[] _scoreMaskScratch;

        // 掩体接触遮蔽场（从不可涂格烘的软遮蔽，PC/移动端共用；见 RebuildGroundContactMask）
        private Texture2D _groundContactTexture;
        private byte[] _groundContactScratch;
        private byte[] _groundContactPixels;
        private float[] _groundContactField;
        private float[] _groundContactTemp;
        private InkSplatAtlas _atlas;
        private readonly List<Material> _inkSurfaceMaterials = new List<Material>(4);
        private float _nextMinimapFlushTime;
        private float _nextRippleSyncTime;
        private float _nextWallFlushTime;
        private Color32[] _uploadScratch;

        // 8组动态波纹环形缓冲区与脏检查缓存
        private readonly Vector4[] _ripplePositions = new Vector4[MaxRipples];
        private readonly Vector4[] _rippleParams = new Vector4[MaxRipples];
        private readonly Vector4[] _cachedRipplePositions = new Vector4[MaxRipples];
        private readonly Vector4[] _cachedRippleParams = new Vector4[MaxRipples];
        private float _lastWrittenWetnessFactor = -1f;
        private int _rippleHead;

        private static readonly int InkTexProp = Shader.PropertyToID("_InkTex");
        private static readonly int InkTexelSizeProp = Shader.PropertyToID("_InkTexelSize");
        private static readonly int InkMapOriginProp = Shader.PropertyToID("_InkMapOriginXZ");
        private static readonly int TeamAColorProp = Shader.PropertyToID("_TeamAColor");
        private static readonly int TeamBColorProp = Shader.PropertyToID("_TeamBColor");
        private static readonly int WetnessFactorProp = Shader.PropertyToID("_WetnessFactor");
        private static readonly int RipplesProp = Shader.PropertyToID("_InkRipples");
        private static readonly int RippleParamsProp = Shader.PropertyToID("_InkRippleParams");
        private static readonly int InkTimeProp = Shader.PropertyToID("_InkTime");

        // 湿润时间戳时钟（图集 G 通道 = 最新落墨时间戳，地面片元按 (_InkWetClockNorm - G) 现算湿润度）
        private static readonly int WetClockNormProp = Shader.PropertyToID("_InkWetClockNorm");
        private static readonly int WetClockPeriodProp = Shader.PropertyToID("_InkWetClockPeriod");
        private static readonly int WetDryingSecondsProp = Shader.PropertyToID("_InkWetDryingSeconds");
        private static readonly int SheenSecondsProp = Shader.PropertyToID("_InkSheenSeconds");
        private static readonly int GrowSecondsProp = Shader.PropertyToID("_InkGrowSeconds");
        private static readonly int GrowShrinkMetersProp = Shader.PropertyToID("_InkGrowShrinkMeters");
        private static readonly int GrowActiveProp = Shader.PropertyToID("_InkGrowActive");

        // 边缘先干偏置（InkGround ForwardLit 湿润现算区读取）与墨斑方向性拉伸全局倍率（图集写入 Pass 读取）。
        // 两者都是非 UnityPerMaterial 的全局 uniform（与 _InkWetDryingSeconds 等同一套传法）。
        private static readonly int EdgeDryBiasProp = Shader.PropertyToID("_InkEdgeDryBias");
        private static readonly int SplatDirStretchProp = Shader.PropertyToID("_InkSplatDirStretch");

        // 全局专供顶面采样（防止材质本身的 _InkTex 属性遮蔽全局贴图）
        private static readonly int GlobalInkAtlasTexProp = Shader.PropertyToID("_GlobalInkAtlasTex");
        private static readonly int GlobalInkMapOriginProp = Shader.PropertyToID("_GlobalInkMapOriginXZ");
        private static readonly int GlobalInkTexelSizeProp = Shader.PropertyToID("_GlobalInkTexelSize");
        private static readonly int GlobalTeamAColorProp = Shader.PropertyToID("_GlobalTeamAColor");
        private static readonly int GlobalTeamBColorProp = Shader.PropertyToID("_GlobalTeamBColor");
        private static readonly int GlobalWetnessFactorProp = Shader.PropertyToID("_GlobalWetnessFactor");
        private static readonly int GlobalInkGlowProp = Shader.PropertyToID("_GlobalInkGlow");
        private static readonly int GlobalInkPlayableRectProp = Shader.PropertyToID("_GlobalInkPlayableRect");
        // 掩体接触遮蔽场（见 RebuildGroundContactMask）
        private static readonly int GlobalInkGroundContactTexProp = Shader.PropertyToID("_GlobalInkGroundContactTex");
        private static readonly int GlobalInkGroundContactTexelProp = Shader.PropertyToID("_GlobalInkGroundContactTexel");
        private static readonly int GlobalInkGroundContactStrengthProp =
            Shader.PropertyToID("_GlobalInkGroundContactStrength");
        // 计分掩码：不可涂格（道具/掩体占位、窄缝补偿）在顶面墨迹着色器里直接裁掉。
        private static readonly int GlobalInkScoreMaskTexProp = Shader.PropertyToID("_GlobalInkScoreMaskTex");
        private static readonly int GlobalInkScoreMaskEnabledProp = Shader.PropertyToID("_GlobalInkScoreMaskEnabled");

        public InkGrid Grid => _grid;

        /// <summary>
        /// 单个墨格的世界面积（m²）：大招充能按面积计价用，与网格分辨率无关（桌面 400 / 移动 200 格时分别 ≈0.014 / 0.056）。
        /// 网格未初始化时回退到 40×56m 地图、桌面 400 分辨率的默认值。
        /// </summary>
        public float CellArea => _grid != null ? _grid.CellWidth * _grid.CellHeight : 0.014f;

        /// <summary>
        /// 单张墙墨格的世界面积（m²）：墙面涂墨充能/统计的换算基准，与地面 <see cref="CellArea"/> 无关。
        /// 未初始化或未配置时回退到 0.1m 格（0.01 m²）。
        /// </summary>
        public float WallCellArea => config != null ? config.WallCellArea : 0.01f;

        /// <summary>CPU 粗网格贴图：小地图与编辑器工具使用，保持原有类型与语义。</summary>
        public Texture2D InkTexture => _inkTexture;

        /// <summary>GPU 墨迹图集（纯视觉层）：地面材质实际采样的贴图。</summary>
        public Texture VisualInkTexture => _atlas != null ? (Texture)_atlas.Atlas : _inkTexture;

        /// <summary>
        /// 计分掩码（R8，0=可涂 / 255=不可涂）：顶面墨迹着色器据此裁掉不可涂格。
        /// 供覆盖率审计与自动化测试只读，不应在运行时修改。
        /// </summary>
        public Texture2D ScoreMaskTexture => _scoreMaskTexture;

        /// <summary>顶面受墨（XZ 投影图集）的渲染器列表，供覆盖率审计工具只读使用。</summary>
        public IReadOnlyList<Renderer> InkableSurfaceRenderers => inkableSurfaceRenderers;

        /// <summary>不可涂掩体/道具碰撞体列表，供覆盖率审计与调试只读读取。</summary>
        public IReadOnlyList<Collider> BlockedColliders => blockedColliders;

        public InkSplatAtlas SplatAtlas => _atlas;

        public InkGridConfigSO Config => config;

        public double LastAtlasCommandMs => _atlas != null ? _atlas.LastCommandMs : 0.0;

        /// <summary>GPU 涂图集累计命令提交次数（性能探针判定本帧是否发生新提交，避免陈旧值重复计数）。</summary>
        public int AtlasCommandExecuteCount => _atlas != null ? _atlas.CommandExecuteCount : 0;

        public float LastUploadDurationMs { get; private set; }
        public float PeakUploadDurationMs { get; private set; }
        public float AverageUploadDurationMs => _uploadCount > 0 ? (float)(_totalUploadDurationMs / _uploadCount) : 0f;
        public int TotalUploadCount => _uploadCount;

        private double _totalUploadDurationMs;
        private int _uploadCount;

        public event Action<Vector3, float, Team, int> OnInkPainted;

        private void Awake()
        {
            Instance = this;
            InitializeRipples();
            InitializeIfNeeded();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
            }

            if (_inkTexture != null)
            {
                Destroy(_inkTexture);
                _inkTexture = null;
            }

            // 组件销毁后不能再让着色器采样已释放的掩码贴图。
            Shader.SetGlobalFloat(GlobalInkScoreMaskEnabledProp, 0f);
            if (_scoreMaskTexture != null)
            {
                if (Application.isPlaying) Destroy(_scoreMaskTexture);
                else DestroyImmediate(_scoreMaskTexture);
                _scoreMaskTexture = null;
            }

            Shader.SetGlobalFloat(GlobalInkGroundContactStrengthProp, 0f);
            if (_groundContactTexture != null)
            {
                if (Application.isPlaying) Destroy(_groundContactTexture);
                else DestroyImmediate(_groundContactTexture);
                _groundContactTexture = null;
            }
        }

        private void InitializeRipples()
        {
            for (int i = 0; i < MaxRipples; i++)
            {
                _ripplePositions[i] = new Vector4(0f, -999f, 0f, -999f);
                _rippleParams[i] = new Vector4(0f, 0.35f, 3.5f, 0.01f);
            }
            _rippleHead = 0;
            _lastWrittenWetnessFactor = -1f;
        }

        public void Configure(InkGridConfigSO gridConfig, IEnumerable<Renderer> renderers, IEnumerable<Collider> obstacles)
        {
            Configure(gridConfig, renderers, obstacles, null);
        }

        /// <summary>
        /// 增量扩展：追加"无碰撞体的计分禁区矩形"（xmin, zmin, xmax, zmax）。
        /// 旧三参签名保持原语义（禁区列表清空），旧调用方无需改动。
        /// </summary>
        public void Configure(
            InkGridConfigSO gridConfig,
            IEnumerable<Renderer> renderers,
            IEnumerable<Collider> obstacles,
            IEnumerable<Vector4> blockedRects)
        {
            config = gridConfig;
            inkableSurfaceRenderers.Clear();
            if (renderers != null) inkableSurfaceRenderers.AddRange(renderers);

            blockedColliders.Clear();
            if (obstacles != null) blockedColliders.AddRange(obstacles);

            blockedWorldRects.Clear();
            if (blockedRects != null) blockedWorldRects.AddRange(blockedRects);

            RebuildGridAndTexture();
        }

        public void InitializeIfNeeded()
        {
            if (_grid != null && _inkTexture != null && _atlas != null)
                return;

            RebuildGridAndTexture();
        }

        public void RebuildGridAndTexture()
        {
            Vector2 mapSize = config != null ? config.mapSize : new Vector2(40f, 40f);
            int res = config != null ? config.GetPlatformResolution() : 200;

            Color32 neutral = config != null ? (Color32)config.neutralGroundColor : new Color32(56, 61, 71, 0);
            Color32 teamA = config != null ? (Color32)config.teamAColor : new Color32(255, 112, 15, 255);
            Color32 teamB = config != null ? (Color32)config.teamBColor : new Color32(38, 107, 255, 255);
            Color32 blocked = config != null ? (Color32)config.blockedMapColor : new Color32(25, 28, 36, 180);

            _grid = new InkGrid(res, res, mapSize, null, neutral, teamA, teamB, blocked);

            if (_inkTexture != null)
            {
                // 编辑器菜单（ProjectScaffolder 重建场景）也会走 Configure → RebuildGridAndTexture：
                // 编辑模式下必须 DestroyImmediate，否则 Unity 会报
                // "RuntimeInkTexture: Destroy may not be called from edit mode" 并留下旧贴图。
                if (Application.isPlaying) Destroy(_inkTexture);
                else DestroyImmediate(_inkTexture);
            }

            _inkTexture = new Texture2D(res, res, TextureFormat.RGBA32, false, false)
            {
                name = "RuntimeInkTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            RebuildInkAtlas(mapSize);

            BakeBlockedColliders();
            BindSurfaceMaterials();
            FlushTexture();
        }

        private void RebuildInkAtlas(Vector2 mapSize)
        {
            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
            }

            int atlasRes = config != null ? config.GetPlatformAtlasResolution() : 4096;
            bool mipmaps = config == null || config.atlasMipmaps;
            float growSeconds = config != null ? config.splatGrowSeconds : 0.08f;
            float dryingSeconds = config != null ? config.inkDryingSeconds : 6f;
            float wetClockPeriod = config != null ? config.atlasWetClockPeriod : InkSplatAtlas.DefaultWetClockPeriod;

            // 图集比计分网格四周多出可视边距：只影响贴图覆盖范围与显墨位置，玩法网格和计分不变。
            float margin = config != null && config.atlasVisualMargin > 0.01f
                ? config.atlasVisualMargin
                : DefaultAtlasVisualMargin;
            Vector2 atlasSize = mapSize + new Vector2(margin * 2f, margin * 2f);
            Vector2 atlasOrigin = _grid.OriginXZ - new Vector2(margin, margin);

            _atlas = new InkSplatAtlas(atlasSize, atlasOrigin, atlasRes, mipmaps, growSeconds, dryingSeconds,
                wetClockPeriod: wetClockPeriod,
                mipIntervalSeconds: config != null ? config.atlasMipIntervalSeconds : 0.5f,
                // 单帧绘制预算取 config.maxSplatsPerFrame（未配置 / 无 config 时回退 InkSplatAtlas.MaxQuads = 384）
                maxSplatsPerFrame: config != null ? config.maxSplatsPerFrame : InkSplatAtlas.MaxQuads);

            // 图集内容不进序列化：重建后立刻清空一次，避免首帧读到未初始化显存
            _atlas.Clear();
            _atlas.FlushImmediate();
        }

        public void BakeBlockedColliders()
        {
            if (_grid == null)
                return;

            // 统一计分口径兜底：若场景内已存在 Arena_Decor 实体道具碰撞代理，自动补入 blockedColliders
            var decorRoot = GameObject.Find("Arena_Decor");
            if (decorRoot != null)
            {
                var decorColliders = decorRoot.GetComponentsInChildren<BoxCollider>(false);
                for (int d = 0; d < decorColliders.Length; d++)
                {
                    BoxCollider box = decorColliders[d];
                    if (box != null && !blockedColliders.Contains(box))
                    {
                        blockedColliders.Add(box);
                    }
                }
            }

            // 编辑模式下改完 Transform 后碰撞体 bounds 不会立即刷新，必须先同步，
            // 否则所有障碍都会被当成原点处 1x1 的方块，导致几乎不产生不可涂区。
            Physics.SyncTransforms();

            Vector2 playMin = _grid.OriginXZ;
            Vector2 playMax = _grid.OriginXZ + _grid.MapSize;

            for (int i = 0; i < blockedColliders.Count; i++)
            {
                Collider col = blockedColliders[i];
                if (col == null || !col.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds b = col.bounds;
                Vector2 minXZ = new Vector2(b.min.x, b.min.z);
                Vector2 maxXZ = new Vector2(b.max.x, b.max.z);

                // 顶面打得到吗？场内实体（掩体墙/木箱/集装箱/花坛/售卖亭）的顶面用
                // ObstacleDitherLit / PropLit 采样顶视墨迹图集，从上方命中就「该显墨、该计分」，
                // 因此标成软占位（带顶面高度）；伸出计分矩形外的实体（边界墙、远景堆场）
                // 顶面在可涂范围之外，保持硬不可涂。
                bool topReachable = b.min.x >= playMin.x && b.max.x <= playMax.x
                                    && b.min.z >= playMin.y && b.max.z <= playMax.y;
                _grid.SetBlockedWorldRect(minXZ, maxXZ, true, topReachable ? b.max.y : float.NaN);
            }

            // 无碰撞体的地形禁区（浅湾水线等）：必须在 SealNarrowSlits 之前写入，
            // 否则窄缝补偿看不到这些区域，会把水线两侧的窄条判成可涂。
            for (int i = 0; i < blockedWorldRects.Count; i++)
            {
                Vector4 rect = blockedWorldRects[i];
                if (rect.z <= rect.x || rect.w <= rect.y) continue;
                _grid.SetBlockedWorldRect(new Vector2(rect.x, rect.y), new Vector2(rect.z, rect.w), true);
            }

            // 接触遮蔽场必须在窄缝补偿**之前**烘：SealNarrowSlits 会把封闭死缝也标成不可涂，
            // 那些细缝不是掩体落地处，烘进去会在地面上出现莫名的暗线。
            RebuildGroundContactMask();

            // P16-D 窄缝自动剔除：封闭两面受夹且净宽 ≤ 0.9m 的不可行/不可涂死缝
            _grid.SealNarrowSlits(0.9f);

            // 掩码必须在窄缝补偿之后重建，否则顶面墨迹会在被剔除的窄缝里继续显墨。
            RebuildScoreMask();
        }

        /// <summary>
        /// 把「仍占位、且还没被涂过」的格子同步成一张单通道贴图（R8），供顶面墨迹着色器裁剪。
        /// 俯视投影图集无法区分「道具顶面」和「脚下地面」——两者共用同一个 XZ 格；
        /// 没涂过的占位格若显墨，玩家就会看到「箱子旁边泼一地、箱顶也蹭上墨」却不计分的假反馈。
        /// 这里让可见墨迹严格跟随计分状态：从上方命中箱顶后该格转为已涂（见
        /// <see cref="InkGrid.PaintCircle(Vector3, float, Core.Team)"/>），掩码随即放开，
        /// **看得到的顶面墨迹 = 会计分的墨迹**；硬占位格（地形禁区、死缝）永远裁掉。
        /// 墙面墨迹由 InkWallSurface 独立负责（竖向面本来就不计入涂地），不受影响。
        /// </summary>
        private void RebuildScoreMask()
        {
            if (_grid == null)
                return;

            int width = _grid.Width;
            int height = _grid.Height;
            int total = width * height;

            if (_scoreMaskTexture == null ||
                _scoreMaskTexture.width != width ||
                _scoreMaskTexture.height != height)
            {
                if (_scoreMaskTexture != null)
                {
                    if (Application.isPlaying) Destroy(_scoreMaskTexture);
                    else DestroyImmediate(_scoreMaskTexture);
                }

                _scoreMaskTexture = new Texture2D(width, height, TextureFormat.R8, false, true)
                {
                    name = "RuntimeInkScoreMask",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            if (_scoreMaskScratch == null || _scoreMaskScratch.Length != total)
                _scoreMaskScratch = new byte[total];

            _grid.CopyUnpaintedBlockedMaskTo(_scoreMaskScratch);
            _scoreMaskTexture.SetPixelData(_scoreMaskScratch, 0);
            _scoreMaskTexture.Apply(false, false);
        }

        /// <summary>
        /// 烘一张「掩体落地接触遮蔽」场：不可涂格（道具/边界墙/地形禁区）先做两遍可分离盒式模糊，
        /// 得到从掩体足迹向外约 <see cref="InkGridConfigSO.groundContactAORadiusMeters"/> 米渐隐的软场，
        /// 由 InkGround 乘到地面基底色上（墨迹不乘，墨面已经有 shadowmap 的阴影）。
        /// 为什么要它：实时阴影只覆盖 shadowDistance(48m) 内、SSAO 只有屏幕空间 0.6m 半径，
        /// 远景箱底/墙脚是干净的，整片堆场读作「浮在干净地面上」。
        /// 与计分掩码同一套 XZ 映射；分辨率跟随网格（400²/200²），R8 约 160KB，只在场景初始化时算一次。
        /// </summary>
        private void RebuildGroundContactMask()
        {
            if (_grid == null)
                return;

            float strength = config != null ? Mathf.Clamp01(config.groundContactAOStrength) : 0f;
            if (strength <= 0.001f)
            {
                Shader.SetGlobalFloat(GlobalInkGroundContactStrengthProp, 0f);
                return;
            }

            int width = _grid.Width;
            int height = _grid.Height;
            int total = width * height;

            float radiusMeters = config != null ? Mathf.Max(0.15f, config.groundContactAORadiusMeters) : 1.2f;
            float cellMeters = Mathf.Max(0.01f, 0.5f * (_grid.CellWidth + _grid.CellHeight));
            int radius = Mathf.Clamp(Mathf.RoundToInt(radiusMeters / cellMeters), 1,
                Mathf.Max(1, Mathf.Min(width, height) / 4));

            if (_groundContactScratch == null || _groundContactScratch.Length != total)
                _groundContactScratch = new byte[total];
            if (_groundContactField == null || _groundContactField.Length != total)
                _groundContactField = new float[total];
            if (_groundContactTemp == null || _groundContactTemp.Length != total)
                _groundContactTemp = new float[total];

            _grid.CopyBlockedMaskTo(_groundContactScratch);
            for (int i = 0; i < total; i++)
                _groundContactField[i] = _groundContactScratch[i] > 0 ? 1f : 0f;

            // 两遍「水平+垂直」盒式模糊 ≈ 三角核，边缘连续、没有逐格台阶。
            BoxBlurSeparable(_groundContactField, _groundContactTemp, width, height, radius);
            BoxBlurSeparable(_groundContactField, _groundContactTemp, width, height, radius);

            // 四周各留 1 texel 白边（0 = 不遮蔽）：场地外的 74×84m 码头基座也用同一套 UV，
            // 不留边会被 Clamp 把边界墙的暗带整条拉到场外。
            int padW = width + 2;
            int padH = height + 2;
            int padTotal = padW * padH;

            if (_groundContactTexture == null ||
                _groundContactTexture.width != padW ||
                _groundContactTexture.height != padH)
            {
                if (_groundContactTexture != null)
                {
                    if (Application.isPlaying) Destroy(_groundContactTexture);
                    else DestroyImmediate(_groundContactTexture);
                }

                _groundContactTexture = new Texture2D(padW, padH, TextureFormat.R8, false, true)
                {
                    name = "RuntimeInkGroundContact",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            if (_groundContactPixels == null || _groundContactPixels.Length != padTotal)
            {
                _groundContactPixels = new byte[padTotal];
            }
            else
            {
                System.Array.Clear(_groundContactPixels, 0, padTotal); // 白边保持 0
            }

            // 盒式模糊后掩体内约 1、边缘约 0.5：×1.9 让「贴着一圈」吃满强度，按半径外渐隐。
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * width;
                int dstRow = (y + 1) * padW + 1;
                for (int x = 0; x < width; x++)
                {
                    float contact = Mathf.Clamp01(_groundContactField[srcRow + x] * 1.9f);
                    _groundContactPixels[dstRow + x] = (byte)Mathf.RoundToInt(contact * 255f);
                }
            }

            _groundContactTexture.SetPixelData(_groundContactPixels, 0);
            _groundContactTexture.Apply(false, false);

            Shader.SetGlobalTexture(GlobalInkGroundContactTexProp, _groundContactTexture);
            Shader.SetGlobalVector(GlobalInkGroundContactTexelProp,
                new Vector4(1f / padW, 1f / padH, padW, padH));
            Shader.SetGlobalFloat(GlobalInkGroundContactStrengthProp, strength);
        }

        /// <summary>可分离盒式模糊（src 原地写出，temp 为同长临时缓冲）。边界按复制延拓。</summary>
        private static void BoxBlurSeparable(float[] src, float[] temp, int width, int height, int radius)
        {
            int window = radius * 2 + 1;
            float inv = 1f / window;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += src[row + Mathf.Clamp(k, 0, width - 1)];

                for (int x = 0; x < width; x++)
                {
                    temp[row + x] = sum * inv;
                    int add = Mathf.Clamp(x + radius + 1, 0, width - 1);
                    int sub = Mathf.Clamp(x - radius, 0, width - 1);
                    sum += src[row + add] - src[row + sub];
                }
            }

            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += temp[Mathf.Clamp(k, 0, height - 1) * width + x];

                for (int y = 0; y < height; y++)
                {
                    src[y * width + x] = sum * inv;
                    int add = Mathf.Clamp(y + radius + 1, 0, height - 1);
                    int sub = Mathf.Clamp(y - radius, 0, height - 1);
                    sum += temp[add * width + x] - temp[sub * width + x];
                }
            }
        }

        public void ResetSurface()
        {
            InitializeIfNeeded();
            _grid.ResetPaintOnly();
            _atlas?.Clear();
            _atlas?.FlushImmediate();
            InitializeRipples();
            _totalUploadDurationMs = 0;
            _uploadCount = 0;
            PeakUploadDurationMs = 0f;
            LastUploadDurationMs = 0f;
            FlushTexture();
            // 清场后所有格回到未涂：箱顶墨迹必须重新被计分掩码裁掉。
            RebuildScoreMask();
            InkWallSurface.ResetAllPaint();
            InkWallSurface.FlushAllDirty();
        }

        /// <summary>
        /// 触发地面墨水动态波纹（落墨、爆炸、跳跃着陆、脚步踩墨），传递给 Shader 形成动态表面波浪。
        /// 同一位置 0.09s 内的重复触发会合并到已有槽位（取更大振幅），避免连发把 8 槽环形缓冲冲干净。
        /// </summary>
        public void TriggerRipple(Vector3 worldPos, float amplitude = 0.045f, float wavelength = 0.35f, float speed = 3.6f, float lifetime = 1.2f)
        {
            float now = Time.time;

            for (int i = 0; i < MaxRipples; i++)
            {
                if (!ShouldMergeRipple(_ripplePositions[i], worldPos, now))
                    continue;

                Vector4 existing = _rippleParams[i];
                _rippleParams[i] = new Vector4(
                    Mathf.Max(existing.x, amplitude), wavelength, speed, lifetime);
                return;
            }

            _ripplePositions[_rippleHead] = new Vector4(worldPos.x, worldPos.y, worldPos.z, now);
            _rippleParams[_rippleHead] = new Vector4(amplitude, wavelength, speed, lifetime);
            _rippleHead = (_rippleHead + 1) % MaxRipples;
        }

        /// <summary>
        /// 纯逻辑（EditMode 可断言）：新波纹是否应合并进已有槽位。
        /// <paramref name="slot"/> = (x, y, z, birthTime)；未使用的槽位 birth 为负值，永不合并。
        /// </summary>
        public static bool ShouldMergeRipple(Vector4 slot, Vector3 worldPos, float now)
        {
            float birth = slot.w;
            if (birth < 0f || now - birth > RippleMergeWindow)
                return false;

            float dx = slot.x - worldPos.x;
            float dz = slot.z - worldPos.z;
            return dx * dx + dz * dz <= RippleMergeRadius * RippleMergeRadius;
        }

        /// <summary>
        /// 设置全局湿润度乘数。
        /// </summary>
        public void SetWetnessFactor(float factor)
        {
            wetnessFactor = Mathf.Clamp(factor, 0f, 2f);
        }

        public int PaintCircle(Vector3 worldPos, float radius, Team team)
        {
            return PaintCircle(worldPos, radius, team, true, true);
        }

        /// <summary>
        /// 带表现开关的落墨重载（旧 3 参签名行为不变）：
        /// <paramref name="emitRipple"/> = false 用于「一次撞击铺多枚墨斑」的中间印记，
        /// 由调用方在撞击点单独触发一条合并后的涟漪；
        /// <paramref name="decorated"/> = false 让图集只画平滑主体（不叠射线/卫星），
        /// 避免连续弹幕把地面涂成一团带刺的噪点；
        /// <paramref name="visualRadiusScale"/> 只放大图集里的视觉墨斑，玩法网格半径保持不变（不改覆盖率）；
        /// <paramref name="directionXZ"/> = 墨斑沿地面拉伸方向（世界 XZ 平面，无需归一化，零向量 = 不拉伸）；
        /// <paramref name="stretch01"/> = 该墨斑的拉伸强度建议值 0-1（随弹速提高），实际拉伸量还受
        /// <see cref="InkGridConfigSO.splatDirectionalStretch"/> 全局倍率约束。玩法网格不受方向影响，仍是正圆。
        /// </summary>
        public int PaintCircle(
            Vector3 worldPos,
            float radius,
            Team team,
            bool emitRipple,
            bool decorated,
            float visualRadiusScale = 1f,
            Vector2 directionXZ = default,
            float stretch01 = 0f)
        {
            InitializeIfNeeded();
            int changed = _grid.PaintCircle(worldPos, radius, team);

            // 视觉层：把同一次落墨作为一枚有机墨斑画进高分辨率图集（玩法判定仍由 _grid 负责）
            if (_atlas != null && team != Team.Neutral && radius > 0f)
            {
                float visualRadius = radius * Mathf.Max(0.05f, visualRadiusScale);
                // 方向性拉伸（A1）：方向/强度只进视觉图集（顶点 TEXCOORD2），
                // 由 InkSurfaceOverlay 的 SDF 拉伸主体并把射线/卫星收进前向锥；
                // 全局倍率见 InkGridConfigSO.splatDirectionalStretch，玩法网格与覆盖率完全不受影响。
                _atlas.AddSplat(
                    new Vector2(worldPos.x, worldPos.z),
                    visualRadius,
                    team,
                    decorated,
                    directionXZ,
                    stretch01);
                if (!Application.isPlaying)
                {
                    // 编辑模式（Scaffolder 预览图）没有 LateUpdate，立刻出图
                    _atlas.FlushImmediate();
                }
            }

            if (changed > 0)
            {
                // 新落墨处自动激活动态表面波纹
                if (emitRipple)
                {
                    TriggerRipple(
                        worldPos,
                        0.035f + Mathf.Clamp(radius * 0.015f, 0f, 0.04f),
                        0.32f, 3.2f, 1.0f);
                }
                OnInkPainted?.Invoke(worldPos, radius, team, changed);
            }
            return changed;
        }

        /// <summary>
        /// 装饰性小墨点（飞溅水珠落地补涂）：与普通落墨走同一条「计分网格 + 视觉图集」路径，
        /// 但**不触发涟漪**（由调用方合并成一条）。调用方必须自行做每帧节流，避免挤占正式墨斑的图集配额。
        /// 旧实现只写图集、不写玩法网格，玩家会看到地上有墨而比分不涨；按「宁可画面糙，不可比分假」
        /// 改为真实计分（半径 0.06~0.12m，最多覆盖 1~4 格，不会造成充能膨胀）。
        /// </summary>
        public int PaintSpeck(Vector3 worldPos, float radius, Team team, bool decorated = false)
        {
            return PaintCircle(worldPos, radius, team, false, decorated);
        }

        /// <summary>
        /// 球形范围同时涂地面与附近墙面，返回总变化格数（供手雷 / 大招 / 击杀爆墨使用）。
        /// </summary>
        public int PaintSphereWithWalls(Vector3 worldPos, float radius, Team team)
        {
            return PaintSphereWithWalls(worldPos, radius, team, out _);
        }

        /// <summary>
        /// 球形范围同时涂地面与附近墙面。返回值仍是总变化格数（地面 + 墙面，保持旧语义），
        /// <paramref name="wallChanged"/> 单独给出其中的墙面格数：墙面格面积与地面格不同，
        /// 充能与涂地统计必须分开换算。
        /// </summary>
        public int PaintSphereWithWalls(Vector3 worldPos, float radius, Team team, out int wallChanged)
        {
            int changed = PaintCircle(worldPos, radius, team);
            wallChanged = 0;
            if (config == null || config.enableWallInk)
            {
                wallChanged = InkWallSurface.PaintAllInSphere(worldPos, radius, team);
                changed += wallChanged;
            }
            return changed;
        }

        public Team InkAt(Vector3 worldPos)
        {
            InitializeIfNeeded();
            return _grid.InkAt(worldPos);
        }

        public float GetCoverage01(Team team)
        {
            InitializeIfNeeded();
            return _grid.GetCoverage01(team);
        }

        public float GetCoveragePercent(Team team)
        {
            InitializeIfNeeded();
            return _grid.GetCoveragePercent(team);
        }

        public Color GetTeamColor(Team team)
        {
            if (config != null)
                return config.GetTeamColor(team);

            return team switch
            {
                Team.TeamA => new Color(1.00f, 0.44f, 0.06f, 1f),
                Team.TeamB => new Color(0.15f, 0.42f, 1.00f, 1f),
                _ => new Color(0.22f, 0.24f, 0.28f, 1f)
            };
        }

        /// <summary>
        /// 线性空间的队伍色：**只** 供 <see cref="Shader.SetGlobalColor"/> 这类不做色彩空间转换的
        /// 全局属性使用（如 InkSurfaceController.BindSurfaceMaterials）。
        /// 材质 Color 属性与 MaterialPropertyBlock.SetColor 会由 Unity 自动做 sRGB→Linear，
        /// 传本方法的返回值会二次转换——橙色变深红（v0.1.7 弹丸/手雷色偏回归教训）。
        /// </summary>
        public Color GetTeamColorLinear(Team team)
        {
            Color c = GetTeamColor(team);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        private void LateUpdate()
        {
            InitializeIfNeeded();

            if (_grid != null && _grid.IsDirty && Time.unscaledTime >= _nextMinimapFlushTime)
            {
                // CPU 贴图只服务小地图，没必要每帧全量上传
                FlushTexture();
                float hz = config != null ? config.minimapRefreshHz : 12f;
                _nextMinimapFlushTime = hz > 0.01f ? Time.unscaledTime + 1f / hz : 0f;
            }

            // 箱顶刚被涂到：计分掩码要放开对应格（只在「软占位格首次被涂 / 清场」的帧重传，
            // 常规泼地掩码内容不变，不会每帧多传一张 400² R8）。
            if (_grid != null && _grid.ScoreMaskDirty)
            {
                RebuildScoreMask();
            }

            // 墨迹图集：仅在有新落墨 / 清屏 / mip 待更新时提交（烘干与生长由片元按时间戳现算）
            _atlas?.Advance();

            // 每帧同步动态波纹与时间给材质属性块（波纹 MPB 写入按 config.rippleSyncHz 节流）
            UpdateDynamicMaterialProperties();

            // 墙面贴图上传按 config.wallFlushHz 节流：墙面墨迹延迟 ≤1 帧，肉眼不可见
            float wallHz = config != null ? config.wallFlushHz : 30f;
            float now = Time.unscaledTime;
            if (wallHz <= 0.01f || now >= _nextWallFlushTime)
            {
                InkWallSurface.FlushAllDirty();
                _nextWallFlushTime = wallHz > 0.01f ? now + 1f / wallHz : 0f;
            }
        }

        public void FlushTexture()
        {
            if (_grid == null || _inkTexture == null)
                return;

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            // 小地图只需更新脏矩形：战斗时常见几十到几百格变化，避免 400×400 全量拷贝 + 整图重传。
            Color32[] pixels = _grid.GetPixelBuffer();
            int width = _grid.Width;
            int height = _grid.Height;
            int minX = _grid.DirtyMinX;
            int minY = _grid.DirtyMinY;
            int maxX = _grid.DirtyMaxX;
            int maxY = _grid.DirtyMaxY;
            int rectW = maxX - minX + 1;
            int rectH = maxY - minY + 1;

            if (rectW > 0 && rectH > 0 && (long)rectW * rectH * 4L <= (long)width * height)
            {
                int need = rectW * rectH;
                if (_uploadScratch == null)
                {
                    // 一次性按整图上限预留（40x40 格 = 640KB），避免战斗中脏矩形突然变大时
                    // new Color32[need] 在采样帧里分配出 100~640KB 的 GC 尖刺。
                    _uploadScratch = new Color32[width * height];
                }
                else if (_uploadScratch.Length < need)
                {
                    _uploadScratch = new Color32[width * height];
                }

                for (int y = 0; y < rectH; y++)
                {
                    Array.Copy(pixels, (minY + y) * width + minX, _uploadScratch, y * rectW, rectW);
                }
                _inkTexture.SetPixels32(minX, minY, rectW, rectH, _uploadScratch);
            }
            else
            {
                _inkTexture.SetPixels32(pixels);
            }

            _inkTexture.Apply(false, false);
            _grid.ClearDirtyFlag();
            long endTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            float elapsedMs = (float)((endTicks - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            LastUploadDurationMs = elapsedMs;
            _totalUploadDurationMs += elapsedMs;
            _uploadCount++;
            if (elapsedMs > PeakUploadDurationMs)
            {
                PeakUploadDurationMs = elapsedMs;
            }
        }

        /// <summary>
        /// 推送湿润时间戳时钟与生长全局（InkGround 片元与 InkSurfaceOverlay 顶面叠加共用）。
        /// 时间基为 Time.time，与 InkSplatAtlas 的落墨编码一致：暂停（timeScale=0）时湿度同步冻结。
        /// </summary>
        private void PushWetClockGlobals()
        {
            float period = config != null ? Mathf.Max(1f, config.atlasWetClockPeriod) : InkSplatAtlas.DefaultWetClockPeriod;
            float now = Time.time;
            Shader.SetGlobalFloat(WetClockPeriodProp, period);
            Shader.SetGlobalFloat(WetClockNormProp, Mathf.Repeat(now, period) / period);
            Shader.SetGlobalFloat(WetDryingSecondsProp, config != null ? config.inkDryingSeconds : 6f);
            Shader.SetGlobalFloat(SheenSecondsProp, config != null ? config.inkSheenSeconds : 22f);
            // 环境镜面全局 + 平台画质档位（P18）：地面/顶面/墙面/墨体共享同一套湿润反光参数。
            // 这些值只随 ArenaStyleSO / 平台档位变化，按 1Hz 节流即可（旧版每帧推 14 个全局 uniform）。
            RenderEnvGlobals.PumpThrottled();
            Shader.SetGlobalFloat(GrowSecondsProp, config != null ? config.splatGrowSeconds : 0.08f);
            Shader.SetGlobalFloat(GrowShrinkMetersProp, config != null ? config.atlasGrowShrinkMeters : 0.18f);
            // P52：生长动画改由写入端编排（InkSplatAtlas._growing 逐帧重绘，INKWAVE 同款
            // 「主体泛出 → 射线冲出 → 卫星错峰落地」），地面片元的均匀形态学侵蚀停用——
            // 两者叠加会互相打架。回滚方式：把下面这行改回
            // `_atlas != null && _atlas.HasGrowingSplats() ? 1f : 0f` 即恢复旧片元侵蚀。
            Shader.SetGlobalFloat(GrowActiveProp, 0f);
            // 边缘先干偏置（0 = 关闭）与墨斑方向性拉伸全局倍率：只影响着色 / 图集绘制读数，
            // 不参与玩法网格判定与覆盖率。两项都在这里每帧推送，改 config 立刻生效（无需重建图集）。
            Shader.SetGlobalFloat(EdgeDryBiasProp, config != null ? Mathf.Clamp01(config.inkEdgeDryBias) : 0f);
            Shader.SetGlobalFloat(SplatDirStretchProp,
                config != null ? Mathf.Clamp(config.splatDirectionalStretch, 0f, 2f) : 1f);
        }

        private void UpdateDynamicMaterialProperties()
        {
            float currentTime = Time.time;
            Shader.SetGlobalFloat(InkTimeProp, currentTime);
            Shader.SetGlobalFloat(WetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(GlobalWetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(GlobalInkGlowProp, inkGlow);
            // 湿润时间戳时钟与生长全局：湿润度/生长全部由片元按时间戳现算，必须每帧刷新，
            // 且要先于下方波纹脏检查的 early-return 推送。
            PushWetClockGlobals();

            Texture inkTexture = _atlas != null ? (Texture)_atlas.Atlas : _inkTexture;
            if (inkTexture != null)
            {
                Shader.SetGlobalTexture(InkTexProp, inkTexture);
                Shader.SetGlobalTexture(GlobalInkAtlasTexProp, inkTexture);
            }

            if (inkableSurfaceRenderers == null || inkableSurfaceRenderers.Count == 0)
                return;

            if (!AreRipplesDirty())
                return;

            // 波纹数组对所有墨面完全相同，改为全局 uniform 推送（_InkRipples / _InkRippleParams
            // 在 InkGround 的 ForwardLit 中本来就是非 CBUFFER 的全局 uniform）。
            // 这样 34 个墨面不再各写一块 MPB，恢复「同 mesh + 同 material」的合批资格。
            // 仍按 rippleSyncHz 节流：数组只在新增波纹时变化，≤30 次/秒肉眼不可察觉。
            float rippleHz = config != null ? config.rippleSyncHz : 30f;
            if (rippleHz > 0.01f)
            {
                float now = Time.unscaledTime;
                if (now < _nextRippleSyncTime)
                    return;
                _nextRippleSyncTime = now + 1f / rippleHz;
            }

            SyncRippleCache();
            PushRippleGlobals();
            ApplyWetnessToSharedMaterials();
        }

        /// <summary>把 8 组波纹一次性推给所有墨面共享的全局 uniform。</summary>
        private void PushRippleGlobals()
        {
            Shader.SetGlobalVectorArray(RipplesProp, _ripplePositions);
            Shader.SetGlobalVectorArray(RippleParamsProp, _rippleParams);
        }

        /// <summary>
        /// 湿润度写在墨面共享材质上（所有墨面共用同一材质），而不是逐 renderer MPB。
        /// 这样 GPU instancing / GRD 能继续把这些墨面批在一起。
        /// </summary>
        private void ApplyWetnessToSharedMaterials()
        {
            for (int i = 0; i < _inkSurfaceMaterials.Count; i++)
            {
                Material mat = _inkSurfaceMaterials[i];
                if (mat != null && mat.HasProperty(WetnessFactorProp))
                    mat.SetFloat(WetnessFactorProp, wetnessFactor);
            }
        }

        private bool AreRipplesDirty()
        {
            if (Mathf.Abs(wetnessFactor - _lastWrittenWetnessFactor) > 0.0001f)
                return true;

            for (int i = 0; i < MaxRipples; i++)
            {
                if (_ripplePositions[i] != _cachedRipplePositions[i] ||
                    _rippleParams[i] != _cachedRippleParams[i])
                {
                    return true;
                }
            }
            return false;
        }

        private void SyncRippleCache()
        {
            _lastWrittenWetnessFactor = wetnessFactor;
            for (int i = 0; i < MaxRipples; i++)
            {
                _cachedRipplePositions[i] = _ripplePositions[i];
                _cachedRippleParams[i] = _rippleParams[i];
            }
        }

        private void BindSurfaceMaterials()
        {
            // InkGround 的 ForwardLit 已全部改读全局 uniform（_GlobalInkAtlasTex / _GlobalInkMapOriginXZ /
            // _GlobalInkTexelSize / 队伍色 / _InkRipples），不再需要逐 renderer MPB。
            // 只有湿润度是 UnityPerMaterial 属性，写到所有墨面共享的材质实例上即可。
            Vector2 atlasOrigin = _atlas != null ? _atlas.OriginXZ : _grid.OriginXZ;
            Vector2 atlasMapSize = _atlas != null ? _atlas.MapSize : _grid.MapSize;
            Vector4 originAndSize = new Vector4(
                atlasOrigin.x,
                atlasOrigin.y,
                atlasMapSize.x,
                atlasMapSize.y);

            Color colorA = config != null ? config.teamAColor : new Color(1.00f, 0.44f, 0.06f, 1f);
            Color colorB = config != null ? config.teamBColor : new Color(0.15f, 0.42f, 1.00f, 1f);

            // Shader.SetGlobalColor 不会像材质 Color 属性 / MaterialPropertyBlock 那样自动做
            // sRGB→Linear 转换（线性工程里全局色是原样传给 shader 的）。顶面墨迹
            // （ApplyTopInkOverlay，见 InkSurfaceOverlay.hlsl）直接吃这两个全局色，
            // 不转换的话橙色 (1, 0.44, 0.06) 会被当成线性值输出成 (255, 180, 68) 的土黄、
            // 蓝色变成浅天蓝，与地面/墙面的同色墨完全对不上。这里手动对齐。
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                colorA = colorA.linear;
                colorB = colorB.linear;
            }

            Texture inkTexture = _atlas != null ? (Texture)_atlas.Atlas : _inkTexture;
            int texWidth = inkTexture != null ? inkTexture.width : 1;
            int texHeight = inkTexture != null ? inkTexture.height : 1;
            Vector4 texelSize = new Vector4(1f / texWidth, 1f / texHeight, texWidth, texHeight);

            // 同步全局墨水属性给所有着色器（如 ObstacleDitherLit 与 PropLit 的顶面墨迹采样）
            Shader.SetGlobalTexture(InkTexProp, inkTexture);
            Shader.SetGlobalTexture(GlobalInkAtlasTexProp, inkTexture);
            Shader.SetGlobalVector(InkTexelSizeProp, texelSize);
            Shader.SetGlobalVector(GlobalInkTexelSizeProp, texelSize);
            Shader.SetGlobalVector(InkMapOriginProp, originAndSize);
            Shader.SetGlobalVector(GlobalInkMapOriginProp, originAndSize);

            // 计分场地矩形 (minX, minZ, sizeX, sizeZ)：顶面/地面着色器用它把场外表面
            // （74x84m 码头基座、远景堆场等同样带墨迹采样的几何）直接判为无墨。
            // 只传图集外扩矩形时，场外坐标会被 saturate 钳到图集边界纹素，
            // 整片场外沿边界拉伸成"一条一条"的墨色。
            Vector2 playOrigin = _grid.OriginXZ;
            Vector2 playSize = _grid.MapSize;
            Shader.SetGlobalVector(GlobalInkPlayableRectProp,
                new Vector4(playOrigin.x, playOrigin.y, playSize.x, playSize.y));

            // 计分掩码与场地矩形同一套 XZ 映射（u = x / sizeX，v = z / sizeZ）。
            // 未建掩码（编辑器预览 / 裸测试）时禁用，保持旧行为。
            Shader.SetGlobalTexture(GlobalInkScoreMaskTexProp, _scoreMaskTexture);
            Shader.SetGlobalFloat(GlobalInkScoreMaskEnabledProp, _scoreMaskTexture != null ? 1f : 0f);
            Shader.SetGlobalColor(TeamAColorProp, colorA);
            Shader.SetGlobalColor(GlobalTeamAColorProp, colorA);
            Shader.SetGlobalColor(TeamBColorProp, colorB);
            Shader.SetGlobalColor(GlobalTeamBColorProp, colorB);
            Shader.SetGlobalFloat(WetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(GlobalWetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(InkTimeProp, Time.time);
            PushWetClockGlobals();
            PushRippleGlobals();

            // 收集墨面共享材质（通常只有 Mat_InkGround 一个），湿润度只写一次。
            _inkSurfaceMaterials.Clear();
            for (int i = 0; i < inkableSurfaceRenderers.Count; i++)
            {
                Renderer r = inkableSurfaceRenderers[i];
                if (r == null) continue;

                Material mat = r.sharedMaterial;
                if (mat == null) continue;

                bool exists = false;
                for (int m = 0; m < _inkSurfaceMaterials.Count; m++)
                {
                    if (ReferenceEquals(_inkSurfaceMaterials[m], mat))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    _inkSurfaceMaterials.Add(mat);
            }

            ApplyWetnessToSharedMaterials();
            SyncRippleCache();
        }
    }
}
