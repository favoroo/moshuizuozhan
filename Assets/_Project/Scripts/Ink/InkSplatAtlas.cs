using System;
using System.Collections.Generic;
using P1.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Ink
{
    /// <summary>
    /// 地面墨迹图集 —— 纯视觉层。
    ///
    /// 玩法判定仍由 <see cref="InkGrid"/>（0.1m 的 CPU 粗网格）负责；本类只负责把每次落墨
    /// 以带 SDF 的有机构型直接绘制进一张高分辨率 RenderTexture（40m 场地默认 4096² = 1cm/texel），
    /// 让墨迹边缘是平滑连续曲线，而不是二值网格的阶梯。
    ///
    /// 通道打包（写入端为 InkGround.shader 的 InkAtlasPaint / InkAtlasStamp Pass）：
    ///   R = 队伍归属，B = 单斑色调，A = 覆盖率（语义与旧版一致）；
    ///   G = 覆盖此纹素的「最新落墨时间戳 × 覆盖率」（预乘，Max 混合，最新墨斑获胜）。
    ///   预乘与 R/B 同一约定：双线性 / mip 过滤下片元 G/A 恒等于真实时间戳。
    ///
    /// 时间驱动的外观演化：
    ///   烘干由 InkGround 片元按 (_InkWetClockNorm − G) 现算（图集在墨变干全过程保持不变）；
    ///   生长动画（P52）改回写入端编排（对齐 INKWAVE paint.js growing）：decorated 墨斑落墨后
    ///   进入 _growing 队列逐帧重绘（主体从 40% 泛出 → 射线冲出 → 卫星错峰落地 → 细雾收尾，
    ///   A=Max / R,B=Over 混合下重绘幂等），寿命 1.75×扩散时长；拖尾（PlainKind）与
    ///   growSeconds=0 仍是一次性写最终形态。图集依旧不做任何周期性整图重绘。
    /// </summary>
    public sealed class InkSplatAtlas : IDisposable
    {
        /// <summary>
        /// 顶点/索引缓冲与网格的容量下限（同时也是缺省单帧绘制预算）。
        /// 实际每帧预算见 <see cref="PerFrameBudget"/>（取 <see cref="InkGridConfigSO.maxSplatsPerFrame"/>）；
        /// 超出预算的墨斑不再丢弃：留在待绘制队列里由后续帧继续画（<see cref="Advance"/>）。
        /// </summary>
        public const int MaxQuads = 384;

        /// <summary>
        /// 待绘制队列的**缺省**容量（两帧预算）。实例容量见构造时定死的 <see cref="_pendingCapacity"/>
        /// （= max(<see cref="InkGridConfigSO.maxSplatsPerFrame"/>, MaxQuads) × 2），此常量只在预算取缺省值时相等。
        /// 因为每帧都会消费最多一帧预算枚，正常节奏下装不下；
        /// 只有同一帧内瞬间落墨超过容量（超大范围必杀 / 清场）才会触发丢最旧策略。
        /// </summary>
        public const int PendingCapacity = MaxQuads * 2;

        /// <summary>队头空洞超过该值时压缩一次 List（避免每帧 RemoveRange 搬运整个队列）。</summary>
        private const int PendingCompactionThreshold = MaxQuads;

        /// <summary>湿润时间戳默认回绕周期（秒），与 <see cref="InkGridConfigSO.atlasWetClockPeriod"/> 默认值一致。</summary>
        public const float DefaultWetClockPeriod = 32f;

        private const int QuadVerts = 4;

        private struct Splat
        {
            public Vector2 CenterXZ;
            public float Radius;
            public float Team;
            public float Seed;
            public float Kind;
            public float Reach;
            public float BirthTime;   // Time.time 时基，与片元 _InkWetClockNorm 一致（暂停时湿度同步冻结）

            /// <summary>
            /// 写侧生长时长（秒）：decorated 墨斑进入 <see cref="_growing"/> 逐帧重绘，
            /// tn = age / SpreadSeconds；0 = 一次性写最终形态（拖尾 / 关闭生长 / 测试路径）。
            /// 对齐 INKWAVE paint.js：扩散时长随半径缩放（大墨斑更"重"，泛出更久）。
            /// </summary>
            public float SpreadSeconds;

            /// <summary>
            /// 方向性拉伸：单位拉伸方向 × 强度（世界 XZ；长度 0~1 即强度，零向量 = 不拉伸）。
            /// 打包成两个 float 直接进顶点 TEXCOORD2，着色器无需反解三角函数；
            /// 只影响视觉图集，玩法网格（<see cref="InkGrid"/>）判定仍是正圆。
            /// </summary>
            public Vector2 Stretch;
        }

        /// <summary>写侧生长中的墨斑：每帧按 tn 重绘（A=Max / R,B=Over 混合下幂等）。</summary>
        private struct GrowingSplat
        {
            public Splat Splat;
            public bool ForceFinish;   // 敌队新墨压顶：立即以最终形态收尾（保证重叠区显示最新队伍）
        }

        private static readonly float[] KindReach = { 1.9f, 2.3f, 2.5f, 2.7f, 2.75f };

        /// <summary>
        /// 「平滑尾迹」种类：只有主体 wobble 轮廓，不画射线与卫星墨滴。
        /// 用于一次撞击里铺出的中间墨斑（拖尾链、沿途滴墨），否则每枚都带 7~10 根刺，
        /// 连续射击会把地面涂成一团带刺的噪点，也白白浪费填充率。
        /// </summary>
        public const float PlainKind = 5f;

        /// <summary>平滑尾迹的面片覆盖倍数：只要包住 wobble 最大外扩 1.59R。</summary>
        public const float PlainReach = 1.75f;

        private static readonly int InkMapSizeId = Shader.PropertyToID("_InkMapSize");
        private static readonly int InkTexelMetersId = Shader.PropertyToID("_InkTexelMeters");
        private static readonly int InkYFlipId = Shader.PropertyToID("_InkYFlip");

        private readonly RenderTexture _atlas;
        private readonly Material _splatMaterial;
        private readonly Mesh _quadMesh;
        private readonly Mesh _instantQuadMesh;
        // 写侧生长重绘网格：常规重绘与「敌队压顶强收尾」各一张——
        // 同一 CommandBuffer 内按 [收尾(最终形态) → pending(新墨) → 常规重绘] 的顺序提交，
        // 保证 R 通道 Over 混合下重叠区永远显示最新的队伍。
        private readonly Mesh _growFinishQuadMesh;
        private readonly Mesh _growQuadMesh;
        private readonly CommandBuffer _cmd = new CommandBuffer { name = "InkSplatAtlas" };

        private readonly List<Splat> _pending = new List<Splat>(PendingCapacity);
        // 有效待绘制区间 = [_pendingHead, _pending.Count)：绘制后只推进头指针，避免每帧搬运整个 List
        private int _pendingHead;

        // 写侧生长队列（对齐 INKWAVE paint.js 的 growing）：decorated 墨斑落墨进入，
        // Advance() 每帧重绘其 quad（A=Max / R,B=Over 混合幂等），寿命 1.75×扩散时长；
        // 敌队新墨压顶时 ForceFinish 立即收尾。平滑尾迹（PlainKind）与 growSeconds=0 不进队列。
        private readonly List<GrowingSplat> _growing = new List<GrowingSplat>(64);
        // 重绘分类暂存（每帧 Clear 复用容量，零 GC）：收尾（画 tn=3 并移除）与常规重绘
        private readonly List<GrowingSplat> _growFinishScratch = new List<GrowingSplat>(64);
        private readonly List<GrowingSplat> _growDrawScratch = new List<GrowingSplat>(64);

        private readonly List<Vector3> _verts;
        private readonly List<Vector4> _uv0;
        private readonly List<Vector4> _uv1;
        // TEXCOORD2 = 方向性拉伸（单位方向 × 强度）。与 splatA/splatB 分开一个通道，
        // 避免改动既有通道的位宽与语义（stretch = 0 时整条链路逐位退化为旧行为）。
        private readonly List<Vector2> _uv2;
        // TEXCOORD3 = 生长时间 tn（3 = 最终形态；<3 时写入端 SDF 走 INKWAVE 式时间编排）。
        private readonly List<Vector4> _uv3;
        private readonly int[] _indices;

        // ---- 单帧绘制预算与缓冲容量（config.maxSplatsPerFrame 接线）----
        // 容量在构造时取 max(配置值, MaxQuads) 后**定死**：_indices/_uv* 一次性分配，
        // 不支持运行时改容量（改 config.maxSplatsPerFrame 只影响每帧预算，容量变化需重建 InkSplatAtlas）。
        private readonly int _quadCapacity;
        private readonly int _perFrameBudget;
        private readonly int _pendingCapacity;

        private readonly Vector2 _mapSize;
        private readonly Vector2 _originXZ;
        private readonly float _texelMeters;
        private readonly float _growSeconds;
        private readonly float _wetClockPeriod;
        private readonly float _mipIntervalSeconds;

        private readonly int _paintPassIndex;
        private readonly int _stampPassIndex;
        private readonly bool _shaderReady;

        private bool _needsClear = true;
        private bool _needsMips = true;
        private bool _disposed;
        private float _lastSplatTime = float.NegativeInfinity;
        private float _nextMipTime;

        /// <summary>最近一次命令缓冲执行的 CPU 耗时（毫秒）。</summary>
        public double LastCommandMs { get; private set; }

        /// <summary>累计命令缓冲执行次数（性能探针用于判定「本帧是否发生了新提交」）。</summary>
        public int CommandExecuteCount { get; private set; }

        /// <summary>累计落墨次数（诊断用）。</summary>
        public int TotalSplatCount { get; private set; }

        /// <summary>最近一次落墨时刻（诊断用；写侧生长以 _growing 队列为准）。</summary>
        public float LastSplatTime => _lastSplatTime;

        /// <summary>因队列满（单帧落墨超过 <see cref="PendingCapacity"/>）被丢弃的最旧墨斑数（诊断用）。</summary>
        public int PendingDroppedCount { get; private set; }

        /// <summary>当前仍在等待绘制（尚未提交进图集）的墨斑数：单帧预算排不下时会跨帧积压。</summary>
        public int PendingSplatCount => _pending.Count - _pendingHead;

        /// <summary>单帧绘制预算（构造时由 `config.maxSplatsPerFrame` 定死；未配置回退 <see cref="MaxQuads"/>）。</summary>
        public int PerFrameBudget => _perFrameBudget;

        /// <summary>顶点/索引缓冲容量 = max(单帧预算, <see cref="MaxQuads"/>)，构造后不可变。</summary>
        public int QuadCapacity => _quadCapacity;

        /// <summary>
        /// 单帧绘制预算规划：给定待绘制数量与单帧上限，给出本帧应绘制数量与剩余数量。
        /// 纯函数，便于 EditMode 覆盖「1000 枚 → 384 / 384 / 232 三帧画完」的分批消费行为。
        /// </summary>
        public static void PlanDrawBatch(int pendingCount, int perFrameBudget, out int drawCount, out int remaining)
        {
            int pending = pendingCount > 0 ? pendingCount : 0;
            int budget = perFrameBudget > 0 ? perFrameBudget : 0;
            drawCount = pending < budget ? pending : budget;
            remaining = pending - drawCount;
        }

        /// <summary>以 <paramref name="perFrameBudget"/> 枚/帧的速度画完 <paramref name="pendingCount"/> 枚所需帧数（0 = 无需绘制）。</summary>
        public static int FramesToDrain(int pendingCount, int perFrameBudget)
        {
            if (pendingCount <= 0)
                return 0;
            if (perFrameBudget <= 0)
                return int.MaxValue;
            return (pendingCount + perFrameBudget - 1) / perFrameBudget;
        }

        /// <summary>
        /// 队列容量策略：加入一枚后若超出 <paramref name="capacity"/>，需从队头丢弃的最旧枚数（0 = 无需丢弃）。
        /// 丢弃只发生在队列已满时，实测正常战斗节奏下不会触发（每帧都会消费 <see cref="MaxQuads"/> 枚）。
        /// </summary>
        public static int PendingOverflowDropCount(int countAfterAdd, int capacity)
        {
            int over = countAfterAdd - capacity;
            return over > 0 ? over : 0;
        }

        /// <summary>
        /// 当前处于生长窗口内 / 待绘制的墨斑数量（诊断用）。旧实现维护一份每帧增删的
        /// 诊断 List，O(n²) 清理且容量会持续扩容产生 GC；现改为 O(1) 计数。
        /// </summary>
        public int ActiveSplatCount => PendingSplatCount + _growing.Count;

        public RenderTexture Atlas => _atlas;
        public Vector2 MapSize => _mapSize;
        public Vector2 OriginXZ => _originXZ;

        /// <param name="dryHz">兼容保留：旧「按 dryHz 定时全屏烘干」已删除，湿润度由片元现算，此参数不再使用。</param>
        /// <param name="dryingSeconds">兼容保留：烘干时长只被地面片元消费（全局 _InkWetDryingSeconds），此处不再使用。</param>
        /// <param name="wetClockPeriod">湿润时间戳回绕周期（秒），需与 InkSurfaceController 推送的 _InkWetClockPeriod 一致。</param>
        /// <param name="maxSplatsPerFrame">
        /// 每帧绘制预算（<see cref="InkGridConfigSO.maxSplatsPerFrame"/>）；≤0 回退 <see cref="MaxQuads"/>。
        /// 缓冲容量取 max(此值, <see cref="MaxQuads"/>) 并在构造时定死：不支持运行时扩容（需重建图集）。
        /// </param>
        public InkSplatAtlas(
            Vector2 mapSize,
            Vector2 originXZ,
            int resolution,
            bool mipmaps,
            float growSeconds,
            float dryingSeconds,
            float dryHz = 15f,
            float wetClockPeriod = DefaultWetClockPeriod,
            float mipIntervalSeconds = 0.25f,
            int maxSplatsPerFrame = 0)
        {
            _mapSize = new Vector2(Mathf.Max(0.01f, mapSize.x), Mathf.Max(0.01f, mapSize.y));
            _originXZ = originXZ;
            _growSeconds = Mathf.Max(0f, growSeconds);
            _wetClockPeriod = Mathf.Clamp(wetClockPeriod, 4f, 600f);
            _mipIntervalSeconds = Mathf.Clamp(mipIntervalSeconds, 0.1f, 5f);

            // 预算与容量：预算决定每帧真正画几枚（超出者留队列跨帧补画）；
            // 容量是顶点/索引缓冲的物理上限，取 max(预算, MaxQuads) 后定死。
            _perFrameBudget = maxSplatsPerFrame > 0 ? maxSplatsPerFrame : MaxQuads;
            _quadCapacity = Mathf.Max(_perFrameBudget, MaxQuads);
            _pendingCapacity = _quadCapacity * 2;

            _verts = new List<Vector3>(_quadCapacity * QuadVerts);
            _uv0 = new List<Vector4>(_quadCapacity * QuadVerts);
            _uv1 = new List<Vector4>(_quadCapacity * QuadVerts);
            _uv2 = new List<Vector2>(_quadCapacity * QuadVerts);
            _uv3 = new List<Vector4>(_quadCapacity * QuadVerts);
            _indices = new int[_quadCapacity * 6];

            int size = Mathf.Clamp(resolution, 128, 8192);

            // 图集按场地长宽比分配，保证 texel 是正方形（否则墨斑在长边方向会被拉伸、AA 宽度也不一致）
            float aspect = _mapSize.y / Mathf.Max(0.01f, _mapSize.x);
            int texWidth;
            int texHeight;
            if (aspect >= 1f)
            {
                texHeight = size;
                texWidth = Mathf.Max(64, Mathf.RoundToInt(size / aspect));
            }
            else
            {
                texWidth = size;
                texHeight = Mathf.Max(64, Mathf.RoundToInt(size * aspect));
            }

            _texelMeters = Mathf.Max(_mapSize.x / texWidth, _mapSize.y / texHeight);

            var desc = new RenderTextureDescriptor(texWidth, texHeight, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false,
                useMipMap = mipmaps,
                autoGenerateMips = false,
                msaaSamples = 1,
                volumeDepth = 1
            };

            _atlas = new RenderTexture(desc)
            {
                name = "InkSplatAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave
            };
            _atlas.Create();

            // 写入端 Pass 挂在 P1/InkGround 上：该着色器被场景地面材质引用，必然随包分发，
            // 不会被 Shader Stripping 剔除（旧 Resources/P1/InkSplat 写入端已由这两个 Pass 取代）。
            Shader shader = Shader.Find("P1/InkGround");
            if (shader == null)
            {
                Debug.LogError("[InkSplatAtlas] 找不到 P1/InkGround 着色器（InkAtlas 写入 Pass），地面将不显示墨水。");
                return;
            }

            _splatMaterial = new Material(shader)
            {
                name = "Mat_InkSplat",
                hideFlags = HideFlags.DontSave
            };
            // Shader 类没有公开 FindPass（Unity 6000.6）：按名查 Pass 必须经由 Material.FindPass
            _paintPassIndex = _splatMaterial.FindPass("InkAtlasPaint");
            _stampPassIndex = _splatMaterial.FindPass("InkAtlasStamp");
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 玩家包会剥掉自定义 Pass 名（FindPass 恒为 -1），但 Pass 顺序与 shader 文件一致：
            // ForwardLit=0 / ShadowCaster=1 / DepthNormals=2 / InkAtlasPaint=3 / InkAtlasStamp=4。
            if (_paintPassIndex < 0 || _stampPassIndex < 0)
            {
                int passCount = _splatMaterial.passCount;
                var passNames = new System.Text.StringBuilder();
                for (int p = 0; p < passCount; p++)
                {
                    passNames.Append(p).Append('=').Append(_splatMaterial.GetPassName(p)).Append(' ');
                }
                Debug.LogWarning($"[InkSplatAtlas] WebGL FindPass 失败，passCount={passCount}，PassNames=[{passNames}]");
                if (passCount >= 5)
                {
                    _paintPassIndex = 3;
                    _stampPassIndex = 4;
                }
            }
#endif
            if (_paintPassIndex < 0 || _stampPassIndex < 0)
            {
                Debug.LogError("[InkSplatAtlas] P1/InkGround 缺少 InkAtlasPaint/InkAtlasStamp 写入 Pass，地面将不显示墨水。");
                return;
            }
            _shaderReady = true;

            _splatMaterial.SetVector(InkMapSizeId, new Vector4(_mapSize.x, _mapSize.y, 0f, 0f));
            _splatMaterial.SetFloat(InkTexelMetersId, _texelMeters);
            // D3D / Metal / Vulkan 的渲染目标原点在左上，clip Y 需要翻转才能对上纹理 V
            _splatMaterial.SetFloat(InkYFlipId, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);

            _quadMesh = BuildQuadMesh();
            _instantQuadMesh = BuildQuadMesh();
            _growFinishQuadMesh = BuildQuadMesh();
            _growQuadMesh = BuildQuadMesh();

            for (int q = 0; q < _quadCapacity; q++)
            {
                int v = q * QuadVerts;
                int i = q * 6;
                _indices[i + 0] = v + 0;
                _indices[i + 1] = v + 1;
                _indices[i + 2] = v + 2;
                _indices[i + 3] = v + 0;
                _indices[i + 4] = v + 2;
                _indices[i + 5] = v + 3;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _cmd.Release();

            DestroyObject(_splatMaterial);
            DestroyObject(_quadMesh);
            DestroyObject(_instantQuadMesh);
            DestroyObject(_growFinishQuadMesh);
            DestroyObject(_growQuadMesh);

            if (_atlas != null)
            {
                _atlas.Release();
                DestroyObject(_atlas);
            }

            _pending.Clear();
            _pendingHead = 0;
            _growing.Clear();
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        /// <summary>清空整张图集（重开比赛）。</summary>
        public void Clear()
        {
            if (_disposed)
                return;

            _pending.Clear();
            _pendingHead = 0;
            _growing.Clear();
            _needsClear = true;
            _needsMips = true;
            _lastSplatTime = float.NegativeInfinity;
        }

        /// <summary>
        /// 记录一次落墨。位置与半径决定随机种子：同一处重复落墨外观稳定，连续滴墨自然各不相同。
        /// </summary>
        public void AddSplat(Vector2 centerXZ, float radius, Team team)
        {
            AddSplat(centerXZ, radius, team, true);
        }

        /// <summary>
        /// 记录一次落墨。<paramref name="decorated"/> = false 时只画平滑主体（无射线 / 卫星），
        /// 供拖尾链与连续弹幕的中间印记使用；战斗主墨斑仍走默认的完整装饰。
        /// </summary>
        public void AddSplat(Vector2 centerXZ, float radius, Team team, bool decorated)
        {
            AddSplat(centerXZ, radius, team, decorated, default, 0f);
        }

        /// <summary>
        /// 记录一次落墨，并带上「方向性拉伸」数据（视觉层专有，玩法网格判定仍是正圆）。
        /// <paramref name="directionXZ"/> = 世界 XZ 平面的拉伸方向（无需归一化，零向量 = 不拉伸）；
        /// <paramref name="stretch01"/> = 该墨斑的拉伸强度 0~1（随弹速提高），最终拉伸量还要乘
        /// <see cref="InkGridConfigSO.splatDirectionalStretch"/> 全局倍率（着色器侧读取）。
        /// 主体沿方向拉长、横向略压扁，射线 / 卫星滴收进前向锥并沿飞行线拉成短 streak。
        /// </summary>
        public void AddSplat(
            Vector2 centerXZ,
            float radius,
            Team team,
            bool decorated,
            Vector2 directionXZ,
            float stretch01)
        {
            if (_disposed || !_shaderReady || radius <= 0.001f || team == Team.Neutral)
                return;

            float kind = decorated ? ClassifyKind(radius) : PlainKind;
            float reach = decorated ? KindReach[(int)kind] : PlainReach;
            float birth = Time.time;
            // 写侧生长时长：随半径缩放（对齐 INKWAVE 0.085+min(0.22, r*0.075) 的量级），
            // 仅 decorated 墨斑进入逐帧重绘队列；拖尾 / 关闭生长（含测试）一次性写最终形态。
            float spreadSeconds = decorated && _growSeconds > 0.001f
                ? _growSeconds * (1f + 0.9f * Mathf.Min(radius, 3f))
                : 0f;
            var splat = new Splat
            {
                CenterXZ = centerXZ,
                Radius = radius,
                Team = team == Team.TeamB ? 1f : 0f,
                Seed = MakeSeed(centerXZ, radius),
                Kind = kind,
                Reach = reach,
                BirthTime = birth,
                SpreadSeconds = spreadSeconds,
                Stretch = PackStretch(directionXZ, stretch01)
            };

            // 最新墨在上（INKWAVE paint.js 同款规则）：范围内敌队仍在生长的墨斑立即收尾。
            // 本帧 Advance 按 [收尾(最终形态) → 新墨 → 其余生长重绘] 顺序提交，
            // R 通道 Over 混合下重叠区永远显示最新落墨的队伍（与玩法网格一致）。
            if (_growing.Count > 0)
            {
                float myReach = radius * reach;
                for (int i = 0; i < _growing.Count; i++)
                {
                    GrowingSplat g = _growing[i];
                    if (g.Splat.Team == splat.Team)
                        continue;
                    float rs = g.Splat.Radius * KindReach[Mathf.Clamp((int)g.Splat.Kind, 0, KindReach.Length - 1)] + myReach;
                    Vector2 d = g.Splat.CenterXZ - centerXZ;
                    if (d.sqrMagnitude <= rs * rs)
                        g.ForceFinish = true;
                    _growing[i] = g;
                }
            }

            if (spreadSeconds > 0.0001f)
            {
                // decorated 墨斑走写侧生长：不再进 pending（Advance 每帧按 tn 重绘）。
                _growing.Add(new GrowingSplat { Splat = splat });
                TotalSplatCount++;
                _lastSplatTime = birth;
                return;
            }

            int drop = PendingOverflowDropCount(PendingSplatCount + 1, _pendingCapacity);
            if (drop > 0)
            {
                // 队列满：丢最旧的若干枚（保证最新的涂地一定能显示）。
                // 顺手把队头空洞一起删掉（一次 memmove），而不是逐枚 RemoveAt(0) 持续搬运整个队列。
                int removeFromList = Mathf.Min(_pendingHead + drop, _pending.Count);
                _pending.RemoveRange(0, removeFromList);
                _pendingHead = 0;
                PendingDroppedCount += drop;
            }
            _pending.Add(splat);
            TotalSplatCount++;
            _lastSplatTime = birth;
        }

        /// <summary>
        /// 方向 + 强度 → 顶点携带的「单位方向 × 强度」（长度 0~1，零向量 = 不拉伸）。
        /// 输入方向无需归一化；NaN / 零向量 / 非正强度一律返回零向量（着色器完全跳过拉伸分支）。
        /// </summary>
        private static Vector2 PackStretch(Vector2 directionXZ, float stretch01)
        {
            float strength = Mathf.Clamp01(stretch01);
            if (strength <= 0.0001f)
                return Vector2.zero;

            float len = directionXZ.magnitude;
            if (len <= 1e-4f || float.IsNaN(len))
                return Vector2.zero;

            return directionXZ * (strength / len);
        }

        /// <summary>
        /// 是否仍有墨斑处于写侧生长队列中（decorated 墨斑逐帧重绘期间）。
        /// 生长动画已由写入端编排（P52）；InkSurfaceController 不再用它驱动地面片元侵蚀。
        /// </summary>
        public bool HasGrowingSplats()
        {
            return _growing.Count > 0;
        }

        /// <summary>
        /// 每帧调用一次（LateUpdate）：仅当本帧有新落墨 / 生长重绘（或需要清屏 / 内容变化后重建 mip）时才提交绘制。
        /// 单帧最多消费 <see cref="PerFrameBudget"/> 枚，pending（首画）优先于 growing（重绘），剩余的留到后续帧（不丢墨）。
        /// 提交顺序 [生长收尾(最终形态) → pending 新墨 → 常规生长重绘]：R 通道 Over 混合下重叠区永远显示最新队伍。
        /// </summary>
        public void Advance()
        {
            if (_disposed || !_shaderReady)
                return;

            float now = Time.time;

            // 单帧预算 = config.maxSplatsPerFrame（构造时定死）；BuildMesh / _indices 只装得下 _quadCapacity 个 quad。
            // 旧版把 _pending 一次性搬进 _active 再 Clear，超出的墨斑既不绘制也不再排队 → 永久丢墨
            // （玩法网格与覆盖率已经算上，玩家读作「涂了不显示」）。现在改为分批消费：剩余留到下一帧。
            PlanDrawBatch(PendingSplatCount, _perFrameBudget, out int drawCount, out _);
            bool hasDraw = drawCount > 0;

            // ---- 写侧生长队列分类（单趟原地压缩，零 GC）----
            // 收尾（ForceFinish 或 age ≥ 1.75×扩散时长）：画 tn=3 最终形态并移除；
            // 常规重绘：按当前 tn 重画一帧。两者共享 pending 之后的剩余预算，收尾优先。
            _growFinishScratch.Clear();
            _growDrawScratch.Clear();
            int growBudget = _perFrameBudget - drawCount;
            if (growBudget > 0 && _growing.Count > 0)
            {
                int w = 0;
                for (int i = 0; i < _growing.Count; i++)
                {
                    GrowingSplat g = _growing[i];
                    float spread = Mathf.Max(0.001f, g.Splat.SpreadSeconds);
                    bool finished = g.ForceFinish || now - g.Splat.BirthTime >= spread * 1.75f;
                    if (finished)
                    {
                        if (_growFinishScratch.Count < growBudget)
                        {
                            _growFinishScratch.Add(g);
                            continue;   // 本帧画最终形态并移出队列
                        }
                    }
                    else if (_growDrawScratch.Count < growBudget - _growFinishScratch.Count)
                    {
                        _growDrawScratch.Add(g);
                    }
                    _growing[w] = g;
                    w++;
                }
                if (w < _growing.Count)
                    _growing.RemoveRange(w, _growing.Count - w);
            }
            bool hasGrowDraw = _growFinishScratch.Count > 0 || _growDrawScratch.Count > 0;

            // mip 只在图集内容有变化时重建（旧版每 0.25s 无条件重建）
            bool wantMips = _atlas.useMipMap && _needsMips && now >= _nextMipTime;
            if (!_needsClear && !hasDraw && !hasGrowDraw && !wantMips)
                return;

            _cmd.Clear();

            if (_needsClear)
            {
                _cmd.SetRenderTarget(_atlas);
                _cmd.ClearRenderTarget(false, true, Color.clear);
                _needsClear = false;
            }

            _cmd.SetRenderTarget(_atlas);

            if (_growFinishScratch.Count > 0)
            {
                // 敌队压顶强收尾：先画它们的最终形态，本帧稍后画的新墨才能压在上面
                BuildGrowingMesh(_growFinishScratch, _growFinishQuadMesh);
                _cmd.DrawMesh(_growFinishQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_growFinishQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
            }

            if (hasDraw)
            {
                // preAge = 0：时间戳即真实落墨时刻，片元据此现算湿润度。
                // 同一 mesh 连续两次 DrawMesh（Paint 写 R/B/A，Stamp 写 G），记录期间不修改 mesh，
                // 满足「record 与 execute 之间不得复用/清空同一 Mesh」的约束。
                BuildMesh(_pending, _pendingHead, drawCount, 0f, _quadMesh);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                ConsumePending(drawCount);
                _needsMips = true;
            }

            if (_growDrawScratch.Count > 0)
            {
                // 生长中的墨斑逐帧重绘（A=Max / R,B=Over 幂等）：主体泛出、射线冲出、卫星错峰落地
                BuildGrowingMesh(_growDrawScratch, _growQuadMesh);
                _cmd.DrawMesh(_growQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_growQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                _needsMips = true;
            }

            if (wantMips)
            {
                _cmd.GenerateMips(_atlas);
                _needsMips = false;
                _nextMipTime = now + _mipIntervalSeconds;
            }

            _cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Execute();
        }

        /// <summary>
        /// 把队头 <paramref name="drawCount"/> 枚已绘制墨斑移出待绘制队列：只推进头指针，
        /// 空洞积累到 <see cref="PendingCompactionThreshold"/> 时才做一次 memmove 压缩。
        /// </summary>
        private void ConsumePending(int drawCount)
        {
            if (drawCount <= 0)
                return;

            _pendingHead += drawCount;
            if (_pendingHead >= _pending.Count)
            {
                _pending.Clear();
                _pendingHead = 0;
            }
            else if (_pendingHead >= PendingCompactionThreshold)
            {
                _pending.RemoveRange(0, _pendingHead);
                _pendingHead = 0;
            }
        }

        /// <summary>
        /// 编辑模式 / 立即出图：把待绘制墨斑以最终形态画进图集（时间戳预老化 → 片元不做生长侵蚀）。
        /// 同样受单帧 <see cref="PerFrameBudget"/> 上限约束，超出的留在队列里等下一次调用，不静默丢弃。
        /// </summary>
        public void FlushImmediate()
        {
            if (_disposed || !_shaderReady)
                return;

            _cmd.Clear();
            bool did = false;

            if (_needsClear)
            {
                _cmd.SetRenderTarget(_atlas);
                _cmd.ClearRenderTarget(false, true, Color.clear);
                _needsClear = false;
                did = true;
            }

            PlanDrawBatch(PendingSplatCount, _perFrameBudget, out int drawCount, out _);
            if (drawCount > 0)
            {
                // 预老化一个生长周期：编辑模式预览没有持续运行的片元时钟，直接呈现完成态
                BuildMesh(_pending, _pendingHead, drawCount, _growSeconds * 1.6f, _instantQuadMesh);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_instantQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_instantQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                ConsumePending(drawCount);
                _needsMips = true;
                did = true;
            }

            // 写侧生长队列里的墨斑立即以最终形态收尾（编辑模式没有逐帧 Advance）
            if (_growing.Count > 0)
            {
                _cmd.SetRenderTarget(_atlas);
                int growCount = Mathf.Min(_growing.Count, _quadCapacity);
                for (int i = 0; i < growCount; i++)
                {
                    GrowingSplat g = _growing[i];
                    g.ForceFinish = true;
                    _growDrawScratch.Add(g);
                }
                BuildGrowingMesh(_growDrawScratch, _growQuadMesh);
                _cmd.DrawMesh(_growQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_growQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                _growDrawScratch.Clear();
                _growing.Clear();
                _needsMips = true;
                did = true;
            }

            if (!did)
                return;

            _cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Execute();
        }

        private void BuildMesh(List<Splat> splats, float preAgeSeconds, Mesh targetMesh)
        {
            BuildMesh(splats, 0, splats.Count, preAgeSeconds, targetMesh);
        }

        /// <summary>绘制 <paramref name="splats"/> 的 [<paramref name="start"/>, start+<paramref name="count"/>) 区间（最终形态 tn=3）。</summary>
        private void BuildMesh(List<Splat> splats, int start, int count, float preAgeSeconds, Mesh targetMesh)
        {
            _verts.Clear();
            _uv0.Clear();
            _uv1.Clear();
            _uv2.Clear();
            _uv3.Clear();

            int end = Mathf.Clamp(start + count, start, splats.Count);
            for (int i = start; i < end; i++)
            {
                Splat s = splats[i];
                AddQuad(s, SplatTimestamp(s.BirthTime - preAgeSeconds), 3f);
            }

            CommitMesh(targetMesh);
        }

        /// <summary>
        /// 构建写侧生长重绘网格：每枚墨斑按 (now − BirthTime) / SpreadSeconds 求当前 tn，
        /// ForceFinish / 到期收尾的墨斑由调用方钳到最终形态（AddQuad 收到 tn≥3 即最终形态）。
        /// </summary>
        private void BuildGrowingMesh(List<GrowingSplat> growing, Mesh targetMesh)
        {
            _verts.Clear();
            _uv0.Clear();
            _uv1.Clear();
            _uv2.Clear();
            _uv3.Clear();

            int count = Mathf.Min(growing.Count, _quadCapacity);
            for (int i = 0; i < count; i++)
            {
                Splat s = growing[i].Splat;
                float spread = Mathf.Max(0.001f, s.SpreadSeconds);
                float tn = growing[i].ForceFinish
                    ? 3f
                    : Mathf.Min((Time.time - s.BirthTime) / spread, 3f);
                AddQuad(s, SplatTimestamp(s.BirthTime), tn);
            }

            CommitMesh(targetMesh);
        }

        /// <summary>提交顶点/UV/索引缓冲到网格（容量防御 clamp 与 Clear 顺序约束见旧注释）。</summary>
        private void CommitMesh(Mesh targetMesh)
        {
            // 必须先 Clear：上一帧的索引缓冲可能引用比本帧更多的顶点，直接 SetVertices 会报
            // "Mesh.vertices is too small"，并且在测试里会被判为运行时错误。
            // 同时限制 quadCount：_indices 只为 _quadCapacity 个 quad 预留，超出会索引越界
            //（调用方已按 _perFrameBudget ≤ _quadCapacity 分批，这里的 clamp 只是防御）。
            int quadCount = Mathf.Min(_verts.Count / QuadVerts, _quadCapacity);
            // Clear(false) 只清空顶点/索引数据、保留顶点布局，避免每帧重设 Layout 的原生开销；
            // 索引缓冲仍会先失效，规避上一帧引用更多顶点导致的 "Mesh.vertices is too small"。
            targetMesh.Clear(false);
            targetMesh.SetVertices(_verts);
            targetMesh.SetUVs(0, _uv0);
            targetMesh.SetUVs(1, _uv1);
            // TEXCOORD2 = 方向性拉伸（单位方向 × 强度）。缺省全零 → 着色器走无拉伸路径。
            targetMesh.SetUVs(2, _uv2);
            // TEXCOORD3 = 生长时间 tn（x 分量有效；3 = 最终形态）。
            targetMesh.SetUVs(3, _uv3);
            targetMesh.SetIndices(_indices, 0, quadCount * 6, MeshTopology.Triangles, 0, false);
        }

        private void AddQuad(Splat s, float birthTs01, float growTn)
        {
            float uvX = (s.CenterXZ.x - _originXZ.x) / _mapSize.x;
            float uvY = (s.CenterXZ.y - _originXZ.y) / _mapSize.y;
            // 顶点是单位四边面片 [-1,1]；真实米制尺寸由顶点着色器用 radius * reach 展开，
            // 这样片元里的 local 坐标就是「距墨斑中心的米制偏移」，SDF 才算在正确尺度上。
            _verts.Add(new Vector3(-1f, -1f, 0f));
            _verts.Add(new Vector3(1f, -1f, 0f));
            _verts.Add(new Vector3(1f, 1f, 0f));
            _verts.Add(new Vector3(-1f, 1f, 0f));

            // splatA = (中心UV, 半径, 队伍)；splatB = (种子, 种类, 归一化落墨时间戳, 覆盖倍数)；
            // splatDir = 单位拉伸方向 × 强度（世界 XZ，长度 0 = 不拉伸，见 InkSurfaceOverlay.P1InkSplatDistanceDir）；
            // splatGrow.x = 归一化生长时间 tn（3 = 最终形态，与旧实现逐位一致）
            var a = new Vector4(uvX, uvY, s.Radius, s.Team);
            var b = new Vector4(s.Seed, s.Kind, birthTs01, s.Reach);
            Vector2 dir = s.Stretch;
            var grow = new Vector4(growTn, 0f, 0f, 0f);
            for (int i = 0; i < QuadVerts; i++)
            {
                _uv0.Add(a);
                _uv1.Add(b);
                _uv2.Add(dir);
                _uv3.Add(grow);
            }
        }

        /// <summary>落墨时刻 → 归一化时间戳 [0,1)。8bit G 通道下的量化步长 = 周期/256 秒。</summary>
        private float SplatTimestamp(float birthTime)
        {
            return Mathf.Repeat(birthTime, _wetClockPeriod) / _wetClockPeriod;
        }

        private void Execute()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Graphics.ExecuteCommandBuffer(_cmd);
            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            LastCommandMs = (end - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            CommandExecuteCount++;
        }

        private static float ClassifyKind(float radius)
        {
            if (radius < 0.25f) return 0f;
            if (radius < 0.55f) return 1f;
            if (radius < 1.0f) return 2f;
            if (radius < 1.9f) return 3f;
            return 4f;
        }

        // 逐墨斑递增的种子盐：位置量化（0.125m）+ 半径量化的 hash 在同点位连喷时会重复
        //（同一格内 N 发子弹形状完全一样 → 读作"死板"），加盐保证每枚墨斑构型唯一。
        private static int _seedSalt;

        private static float MakeSeed(Vector2 center, float radius)
        {
            int qx = Mathf.RoundToInt(center.x * 8f);
            int qz = Mathf.RoundToInt(center.y * 8f);
            int qr = Mathf.RoundToInt(radius * 16f);
            unchecked
            {
                uint h = (uint)(qx * 73856093) ^ (uint)(qz * 19349663) ^ (uint)(qr * 83492791) ^ (uint)_seedSalt++;
                h ^= h >> 13;
                h *= 0x85ebca6b;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "InkSplatQuad",
                hideFlags = HideFlags.DontSave
            };
            mesh.MarkDynamic();
            return mesh;
        }
    }
}
