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

        [SerializeField] private InkGridConfigSO config;
        [SerializeField] private List<Renderer> inkableSurfaceRenderers = new List<Renderer>();
        [SerializeField] private List<Collider> blockedColliders = new List<Collider>();

        [Header("动态墨水与材质参数")]
        [Tooltip("全局湿润度乘数，随时间或比赛阶段可动态调节")]
        [Range(0f, 2f)]
        [SerializeField] private float wetnessFactor = 1.0f;

        private InkGrid _grid;
        private Texture2D _inkTexture;
        private MaterialPropertyBlock _surfaceBlock;

        // 8组动态波纹环形缓冲区
        private readonly Vector4[] _ripplePositions = new Vector4[MaxRipples];
        private readonly Vector4[] _rippleParams = new Vector4[MaxRipples];
        private int _rippleHead;

        private static readonly int InkTexProp = Shader.PropertyToID("_InkTex");
        private static readonly int InkMapOriginProp = Shader.PropertyToID("_InkMapOriginXZ");
        private static readonly int TeamAColorProp = Shader.PropertyToID("_TeamAColor");
        private static readonly int TeamBColorProp = Shader.PropertyToID("_TeamBColor");
        private static readonly int WetnessFactorProp = Shader.PropertyToID("_WetnessFactor");
        private static readonly int RipplesProp = Shader.PropertyToID("_InkRipples");
        private static readonly int RippleParamsProp = Shader.PropertyToID("_InkRippleParams");
        private static readonly int InkTimeProp = Shader.PropertyToID("_InkTime");

        public InkGrid Grid => _grid;
        public Texture2D InkTexture => _inkTexture;
        public InkGridConfigSO Config => config;

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
            if (_grid != null && _inkTexture != null)
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
                Destroy(_inkTexture);
            }

            _inkTexture = new Texture2D(res, res, TextureFormat.RGBA32, false, false)
            {
                name = "RuntimeInkTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            BakeBlockedColliders();
            BindSurfaceMaterials();
            FlushTexture();
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
            if (_grid != null && _grid.IsDirty)
            {
                FlushTexture();
            }

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

        private void UpdateDynamicMaterialProperties()
        {
            if (inkableSurfaceRenderers == null || inkableSurfaceRenderers.Count == 0)
                return;

            if (_surfaceBlock == null)
            {
                _surfaceBlock = new MaterialPropertyBlock();
            }

            float currentTime = Time.time;
            for (int i = 0; i < inkableSurfaceRenderers.Count; i++)
            {
                Renderer r = inkableSurfaceRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_surfaceBlock);
                _surfaceBlock.SetVectorArray(RipplesProp, _ripplePositions);
                _surfaceBlock.SetVectorArray(RippleParamsProp, _rippleParams);
                _surfaceBlock.SetFloat(InkTimeProp, currentTime);
                _surfaceBlock.SetFloat(WetnessFactorProp, wetnessFactor);
                r.SetPropertyBlock(_surfaceBlock);
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

            Vector4 originAndSize = new Vector4(
                _grid.OriginXZ.x,
                _grid.OriginXZ.y,
                _grid.MapSize.x,
                _grid.MapSize.y);

            Color colorA = config != null ? config.teamAColor : new Color(1.00f, 0.44f, 0.06f, 1f);
            Color colorB = config != null ? config.teamBColor : new Color(0.15f, 0.42f, 1.00f, 1f);

            for (int i = 0; i < inkableSurfaceRenderers.Count; i++)
            {
                Renderer r = inkableSurfaceRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(_surfaceBlock);
                _surfaceBlock.SetTexture(InkTexProp, _inkTexture);
                _surfaceBlock.SetVector(InkMapOriginProp, originAndSize);
                _surfaceBlock.SetColor(TeamAColorProp, colorA);
                _surfaceBlock.SetColor(TeamBColorProp, colorB);
                _surfaceBlock.SetFloat(WetnessFactorProp, wetnessFactor);
                _surfaceBlock.SetVectorArray(RipplesProp, _ripplePositions);
                _surfaceBlock.SetVectorArray(RippleParamsProp, _rippleParams);
                _surfaceBlock.SetFloat(InkTimeProp, Time.time);
                r.SetPropertyBlock(_surfaceBlock);
            }
        }
    }
}
