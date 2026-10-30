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
        /// 墨迹图集向场地四周外扩的默认可视边距（米）：计分网格仍是 40x56，
        /// 但边界墙顶、场边沿这些落在网格外、玩家仍能打到的表面需要图集多覆盖一圈才能显墨。
        /// </summary>
        private const float DefaultAtlasVisualMargin = 1.5f;

        [SerializeField] private InkGridConfigSO config;
        [SerializeField] private List<Renderer> inkableSurfaceRenderers = new List<Renderer>();
        [SerializeField] private List<Collider> blockedColliders = new List<Collider>();

        [Header("动态墨水与材质参数")]
        [Tooltip("全局湿润度乘数，随时间或比赛阶段可动态调节")]
        [Range(0f, 2f)]
        [SerializeField] private float wetnessFactor = 1.0f;

        [Tooltip("胶质内发光强度：顶面/物件墨迹与地面 Mat_InkGround._InkGlow 对齐用，保证墨色暗部同样鲜艳")]
        [Range(0f, 1f)]
        [SerializeField] private float inkGlow = 0.2f;

        private InkGrid _grid;
        private Texture2D _inkTexture;
        private InkSplatAtlas _atlas;
        private MaterialPropertyBlock _surfaceBlock;
        private float _nextMinimapFlushTime;

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
        private static readonly int GrowSecondsProp = Shader.PropertyToID("_InkGrowSeconds");
        private static readonly int GrowShrinkMetersProp = Shader.PropertyToID("_InkGrowShrinkMeters");
        private static readonly int GrowActiveProp = Shader.PropertyToID("_InkGrowActive");

        // 全局专供顶面采样（防止材质本身的 _InkTex 属性遮蔽全局贴图）
        private static readonly int GlobalInkAtlasTexProp = Shader.PropertyToID("_GlobalInkAtlasTex");
        private static readonly int GlobalInkMapOriginProp = Shader.PropertyToID("_GlobalInkMapOriginXZ");
        private static readonly int GlobalInkTexelSizeProp = Shader.PropertyToID("_GlobalInkTexelSize");
        private static readonly int GlobalTeamAColorProp = Shader.PropertyToID("_GlobalTeamAColor");
        private static readonly int GlobalTeamBColorProp = Shader.PropertyToID("_GlobalTeamBColor");
        private static readonly int GlobalWetnessFactorProp = Shader.PropertyToID("_GlobalWetnessFactor");
        private static readonly int GlobalInkGlowProp = Shader.PropertyToID("_GlobalInkGlow");

        public InkGrid Grid => _grid;

        /// <summary>CPU 粗网格贴图：小地图与编辑器工具使用，保持原有类型与语义。</summary>
        public Texture2D InkTexture => _inkTexture;

        /// <summary>GPU 墨迹图集（纯视觉层）：地面材质实际采样的贴图。</summary>
        public Texture VisualInkTexture => _atlas != null ? (Texture)_atlas.Atlas : _inkTexture;

        public InkSplatAtlas SplatAtlas => _atlas;

        public InkGridConfigSO Config => config;

        public double LastAtlasCommandMs => _atlas != null ? _atlas.LastCommandMs : 0.0;

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
            config = gridConfig;
            inkableSurfaceRenderers.Clear();
            if (renderers != null) inkableSurfaceRenderers.AddRange(renderers);

            blockedColliders.Clear();
            if (obstacles != null) blockedColliders.AddRange(obstacles);

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
            float growSeconds = config != null ? config.splatGrowSeconds : 0.12f;
            float dryingSeconds = config != null ? config.inkDryingSeconds : 6f;
            float wetClockPeriod = config != null ? config.atlasWetClockPeriod : InkSplatAtlas.DefaultWetClockPeriod;

            // 图集比计分网格四周多出可视边距：只影响贴图覆盖范围与显墨位置，玩法网格和计分不变。
            float margin = config != null && config.atlasVisualMargin > 0.01f
                ? config.atlasVisualMargin
                : DefaultAtlasVisualMargin;
            Vector2 atlasSize = mapSize + new Vector2(margin * 2f, margin * 2f);
            Vector2 atlasOrigin = _grid.OriginXZ - new Vector2(margin, margin);

            _atlas = new InkSplatAtlas(atlasSize, atlasOrigin, atlasRes, mipmaps, growSeconds, dryingSeconds,
                wetClockPeriod: wetClockPeriod);

            // 图集内容不进序列化：重建后立刻清空一次，避免首帧读到未初始化显存
            _atlas.Clear();
            _atlas.FlushImmediate();
        }

        public void BakeBlockedColliders()
        {
            if (_grid == null)
                return;

            // 编辑模式下改完 Transform 后碰撞体 bounds 不会立即刷新，必须先同步，
            // 否则所有障碍都会被当成原点处 1x1 的方块，导致几乎不产生不可涂区。
            Physics.SyncTransforms();

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
                _grid.SetBlockedWorldRect(minXZ, maxXZ, true);
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
            InkWallSurface.ResetAllPaint();
            InkWallSurface.FlushAllDirty();
        }

        /// <summary>
        /// 触发地面墨水动态波纹（落墨、爆炸、跳跃着陆、脚步踩墨），传递给 Shader 形成动态表面波浪。
        /// </summary>
        public void TriggerRipple(Vector3 worldPos, float amplitude = 0.045f, float wavelength = 0.35f, float speed = 3.6f, float lifetime = 1.2f)
        {
            _ripplePositions[_rippleHead] = new Vector4(worldPos.x, worldPos.y, worldPos.z, Time.time);
            _rippleParams[_rippleHead] = new Vector4(amplitude, wavelength, speed, lifetime);
            _rippleHead = (_rippleHead + 1) % MaxRipples;
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
            InitializeIfNeeded();
            int changed = _grid.PaintCircle(worldPos, radius, team);

            // 视觉层：把同一次落墨作为一枚有机墨斑画进高分辨率图集（玩法判定仍由 _grid 负责）
            if (_atlas != null && team != Team.Neutral && radius > 0f)
            {
                _atlas.AddSplat(new Vector2(worldPos.x, worldPos.z), radius, team);
                if (!Application.isPlaying)
                {
                    // 编辑模式（Scaffolder 预览图）没有 LateUpdate，立刻出图
                    _atlas.FlushImmediate();
                }
            }

            if (changed > 0)
            {
                // 新落墨处自动激活动态表面波纹
                TriggerRipple(worldPos, 0.035f + Mathf.Clamp(radius * 0.015f, 0f, 0.04f), 0.32f, 3.2f, 1.0f);
                OnInkPainted?.Invoke(worldPos, radius, team, changed);
            }
            return changed;
        }

        /// <summary>
        /// 球形范围同时涂地面与附近墙面，返回总变化格数（供手雷 / 大招 / 击杀爆墨使用）。
        /// </summary>
        public int PaintSphereWithWalls(Vector3 worldPos, float radius, Team team)
        {
            int changed = PaintCircle(worldPos, radius, team);
            if (config == null || config.enableWallInk)
            {
                changed += InkWallSurface.PaintAllInSphere(worldPos, radius, team);
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

            // 墨迹图集：仅在有新落墨 / 清屏 / mip 待更新时提交（烘干与生长由片元按时间戳现算）
            _atlas?.Advance();

            // 每帧同步动态波纹与时间给材质属性块
            UpdateDynamicMaterialProperties();

            // 墙面贴图每帧最多上传一次（与地面同一节流点）
            InkWallSurface.FlushAllDirty();
        }

        public void FlushTexture()
        {
            if (_grid == null || _inkTexture == null)
                return;

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _inkTexture.SetPixels32(_grid.GetPixelBuffer());
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
            Shader.SetGlobalFloat(GrowSecondsProp, config != null ? config.splatGrowSeconds : 0.12f);
            Shader.SetGlobalFloat(GrowShrinkMetersProp, config != null ? config.atlasGrowShrinkMeters : 0.4f);
            Shader.SetGlobalFloat(GrowActiveProp, _atlas != null && _atlas.HasGrowingSplats() ? 1f : 0f);
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

            SyncRippleCache();

            if (_surfaceBlock == null)
            {
                _surfaceBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < inkableSurfaceRenderers.Count; i++)
            {
                Renderer r = inkableSurfaceRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_surfaceBlock);
                _surfaceBlock.SetVectorArray(RipplesProp, _ripplePositions);
                _surfaceBlock.SetVectorArray(RippleParamsProp, _rippleParams);
                _surfaceBlock.SetFloat(WetnessFactorProp, wetnessFactor);
                r.SetPropertyBlock(_surfaceBlock);
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
            // 运行时 Texture2D 无法序列化进 .mat 资产，因此用 MaterialPropertyBlock 逐 Renderer
            // 绑定墨水和世界映射参数：不污染资产、不打断合批，EditMode 与 PlayMode 都生效。
            if (_surfaceBlock == null)
            {
                _surfaceBlock = new MaterialPropertyBlock();
            }

            Vector2 atlasOrigin = _atlas != null ? _atlas.OriginXZ : _grid.OriginXZ;
            Vector2 atlasMapSize = _atlas != null ? _atlas.MapSize : _grid.MapSize;
            Vector4 originAndSize = new Vector4(
                atlasOrigin.x,
                atlasOrigin.y,
                atlasMapSize.x,
                atlasMapSize.y);

            Color colorA = config != null ? config.teamAColor : new Color(1.00f, 0.44f, 0.06f, 1f);
            Color colorB = config != null ? config.teamBColor : new Color(0.15f, 0.42f, 1.00f, 1f);

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
            Shader.SetGlobalColor(TeamAColorProp, colorA);
            Shader.SetGlobalColor(GlobalTeamAColorProp, colorA);
            Shader.SetGlobalColor(TeamBColorProp, colorB);
            Shader.SetGlobalColor(GlobalTeamBColorProp, colorB);
            Shader.SetGlobalFloat(WetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(GlobalWetnessFactorProp, wetnessFactor);
            Shader.SetGlobalFloat(InkTimeProp, Time.time);
            PushWetClockGlobals();

            for (int i = 0; i < inkableSurfaceRenderers.Count; i++)
            {
                Renderer r = inkableSurfaceRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_surfaceBlock);
                _surfaceBlock.SetTexture(InkTexProp, inkTexture);
                _surfaceBlock.SetVector(InkTexelSizeProp, texelSize);
                _surfaceBlock.SetVector(InkMapOriginProp, originAndSize);
                _surfaceBlock.SetColor(TeamAColorProp, colorA);
                _surfaceBlock.SetColor(TeamBColorProp, colorB);
                _surfaceBlock.SetFloat(WetnessFactorProp, wetnessFactor);
                _surfaceBlock.SetVectorArray(RipplesProp, _ripplePositions);
                _surfaceBlock.SetVectorArray(RippleParamsProp, _rippleParams);
                r.SetPropertyBlock(_surfaceBlock);
            }

            SyncRippleCache();
        }
    }
}
