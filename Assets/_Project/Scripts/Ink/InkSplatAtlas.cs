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
    /// 时间驱动的外观演化全部改为「存时刻、片元现算」：
    ///   烘干与生长动画由 InkGround 片元按 (_InkWetClockNorm − G) 现算，
    ///   因此图集只在有新墨斑（或需清屏 / 内容变化后重建 mip）时才增量绘制，
    ///   不再有任何周期性整图重绘（旧版：15Hz 全屏烘干 Pass + 生长期间每帧重画墨斑 + 定期 mip 重建）。
    /// </summary>
    public sealed class InkSplatAtlas : IDisposable
    {
        /// <summary>同一帧最多绘制的墨斑数量，超出者丢弃（与旧版一致）。</summary>
        public const int MaxQuads = 384;

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
        }

        private static readonly float[] KindReach = { 1.9f, 2.3f, 2.5f, 2.7f, 2.75f };

        private static readonly int InkMapSizeId = Shader.PropertyToID("_InkMapSize");
        private static readonly int InkTexelMetersId = Shader.PropertyToID("_InkTexelMeters");
        private static readonly int InkYFlipId = Shader.PropertyToID("_InkYFlip");

        private readonly RenderTexture _atlas;
        private readonly Material _splatMaterial;
        private readonly Mesh _quadMesh;
        private readonly Mesh _instantQuadMesh;
        private readonly CommandBuffer _cmd = new CommandBuffer { name = "InkSplatAtlas" };

        private readonly List<Splat> _pending = new List<Splat>(256);
        // 仅作 ActiveSplatCount 诊断计数用：生长动画已改为片元侧时间戳驱动，不再按帧重绘
        private readonly List<Splat> _active = new List<Splat>(256);

        private readonly List<Vector3> _verts = new List<Vector3>(MaxQuads * QuadVerts);
        private readonly List<Vector4> _uv0 = new List<Vector4>(MaxQuads * QuadVerts);
        private readonly List<Vector4> _uv1 = new List<Vector4>(MaxQuads * QuadVerts);
        private readonly int[] _indices = new int[MaxQuads * 6];

        private readonly Vector2 _mapSize;
        private readonly Vector2 _originXZ;
        private readonly float _texelMeters;
        private readonly float _growSeconds;
        private readonly float _wetClockPeriod;

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

        /// <summary>累计落墨次数（诊断用）。</summary>
        public int TotalSplatCount { get; private set; }

        /// <summary>当前处于生长窗口内的墨斑数量（诊断用；动画本身由片元按时间戳驱动）。</summary>
        public int ActiveSplatCount => _active.Count;

        public RenderTexture Atlas => _atlas;
        public Vector2 MapSize => _mapSize;
        public Vector2 OriginXZ => _originXZ;

        /// <param name="dryHz">兼容保留：旧「按 dryHz 定时全屏烘干」已删除，湿润度由片元现算，此参数不再使用。</param>
        /// <param name="dryingSeconds">兼容保留：烘干时长只被地面片元消费（全局 _InkWetDryingSeconds），此处不再使用。</param>
        /// <param name="wetClockPeriod">湿润时间戳回绕周期（秒），需与 InkSurfaceController 推送的 _InkWetClockPeriod 一致。</param>
        public InkSplatAtlas(
            Vector2 mapSize,
            Vector2 originXZ,
            int resolution,
            bool mipmaps,
            float growSeconds,
            float dryingSeconds,
            float dryHz = 15f,
            float wetClockPeriod = DefaultWetClockPeriod)
        {
            _mapSize = new Vector2(Mathf.Max(0.01f, mapSize.x), Mathf.Max(0.01f, mapSize.y));
            _originXZ = originXZ;
            _growSeconds = Mathf.Max(0f, growSeconds);
            _wetClockPeriod = Mathf.Clamp(wetClockPeriod, 4f, 600f);

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

            for (int q = 0; q < MaxQuads; q++)
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

            if (_atlas != null)
            {
                _atlas.Release();
                DestroyObject(_atlas);
            }

            _pending.Clear();
            _active.Clear();
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
            _active.Clear();
            _needsClear = true;
            _needsMips = true;
            _lastSplatTime = float.NegativeInfinity;
        }

        /// <summary>
        /// 记录一次落墨。位置与半径决定随机种子：同一处重复落墨外观稳定，连续滴墨自然各不相同。
        /// </summary>
        public void AddSplat(Vector2 centerXZ, float radius, Team team)
        {
            if (_disposed || !_shaderReady || radius <= 0.001f || team == Team.Neutral)
                return;

            float kind = ClassifyKind(radius);
            float birth = Time.time;
            var splat = new Splat
            {
                CenterXZ = centerXZ,
                Radius = radius,
                Team = team == Team.TeamB ? 1f : 0f,
                Seed = MakeSeed(centerXZ, radius),
                Kind = kind,
                Reach = KindReach[(int)kind],
                BirthTime = birth
            };

            if (_pending.Count >= MaxQuads * 2)
                _pending.RemoveAt(0);
            _pending.Add(splat);
            TotalSplatCount++;
            _lastSplatTime = birth;
        }

        /// <summary>
        /// 是否仍有墨斑处于生长窗口（growSeconds × 1.6）内。
        /// InkSurfaceController 据此开启地面片元的生长侵蚀；窗口结束或 growSeconds=0 时恒为 false。
        /// </summary>
        public bool HasGrowingSplats()
        {
            return _growSeconds > 0.001f &&
                   Time.time - _lastSplatTime < _growSeconds * 1.6f + 0.05f;
        }

        /// <summary>
        /// 每帧调用一次（LateUpdate）：仅当本帧有新落墨（或需要清屏 / 内容变化后重建 mip）时才提交绘制。
        /// 烘干与生长动画都在地面片元里按时间戳现算，空闲帧零 GPU 提交、零命令缓冲构建。
        /// </summary>
        public void Advance()
        {
            if (_disposed || !_shaderReady)
                return;

            float now = Time.time;

            // 待绘制墨斑移入诊断计数队列（生长动画由片元按时间戳驱动，这里不再按帧重绘）
            bool hasDraw = _pending.Count > 0;
            if (hasDraw)
            {
                for (int i = 0; i < _pending.Count; i++)
                    _active.Add(_pending[i]);
            }

            if (_active.Count > 0)
            {
                float life = _growSeconds * 1.6f;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (now - _active[i].BirthTime >= life)
                        _active.RemoveAt(i);
                }
            }

            // mip 只在图集内容有变化时重建（旧版每 0.25s 无条件重建）
            bool wantMips = _atlas.useMipMap && _needsMips && now >= _nextMipTime;
            if (!_needsClear && !hasDraw && !wantMips)
                return;

            _cmd.Clear();

            if (_needsClear)
            {
                _cmd.SetRenderTarget(_atlas);
                _cmd.ClearRenderTarget(false, true, Color.clear);
                _needsClear = false;
            }

            if (hasDraw)
            {
                // preAge = 0：时间戳即真实落墨时刻，片元据此做生长侵蚀。
                // 同一 mesh 连续两次 DrawMesh（Paint 写 R/B/A，Stamp 写 G），记录期间不修改 mesh，
                // 满足「record 与 execute 之间不得复用/清空同一 Mesh」的约束。
                BuildMesh(_pending, 0f, _quadMesh);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                _pending.Clear();
                _needsMips = true;
            }

            if (wantMips)
            {
                _cmd.GenerateMips(_atlas);
                _needsMips = false;
                _nextMipTime = now + 0.25f;
            }

            _cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Execute();
        }

        /// <summary>编辑模式 / 立即出图：把待绘制墨斑以最终形态画进图集（时间戳预老化 → 片元不做生长侵蚀）。</summary>
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

            if (_pending.Count > 0)
            {
                // 预老化一个生长周期：编辑模式预览没有持续运行的片元时钟，直接呈现完成态
                BuildMesh(_pending, _growSeconds * 1.6f, _instantQuadMesh);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_instantQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _paintPassIndex);
                _cmd.DrawMesh(_instantQuadMesh, Matrix4x4.identity, _splatMaterial, 0, _stampPassIndex);
                for (int i = 0; i < _pending.Count; i++)
                    _active.Add(_pending[i]);
                _pending.Clear();
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
            _verts.Clear();
            _uv0.Clear();
            _uv1.Clear();

            for (int i = 0; i < splats.Count; i++)
            {
                Splat s = splats[i];
                AddQuad(s, SplatTimestamp(s.BirthTime - preAgeSeconds));
            }

            // 必须先 Clear：上一帧的索引缓冲可能引用比本帧更多的顶点，直接 SetVertices 会报
            // "Mesh.vertices is too small"，并且在测试里会被判为运行时错误。
            // 同时限制 quadCount：FlushImmediate 可能一次提交 MaxQuads*2 个待绘制墨斑，
            // 而 _indices 只为 MaxQuads 个 quad 预留，超出会索引越界。
            int quadCount = Mathf.Min(_verts.Count / QuadVerts, MaxQuads);
            targetMesh.Clear();
            targetMesh.SetVertices(_verts);
            targetMesh.SetUVs(0, _uv0);
            targetMesh.SetUVs(1, _uv1);
            targetMesh.SetIndices(_indices, 0, quadCount * 6, MeshTopology.Triangles, 0, false);
        }

        private void AddQuad(Splat s, float birthTs01)
        {
            float uvX = (s.CenterXZ.x - _originXZ.x) / _mapSize.x;
            float uvY = (s.CenterXZ.y - _originXZ.y) / _mapSize.y;
            // 顶点是单位四边面片 [-1,1]；真实米制尺寸由顶点着色器用 radius * reach 展开，
            // 这样片元里的 local 坐标就是「距墨斑中心的米制偏移」，SDF 才算在正确尺度上。
            _verts.Add(new Vector3(-1f, -1f, 0f));
            _verts.Add(new Vector3(1f, -1f, 0f));
            _verts.Add(new Vector3(1f, 1f, 0f));
            _verts.Add(new Vector3(-1f, 1f, 0f));

            // splatA = (中心UV, 半径, 队伍)；splatB = (种子, 种类, 归一化落墨时间戳, 覆盖倍数)
            var a = new Vector4(uvX, uvY, s.Radius, s.Team);
            var b = new Vector4(s.Seed, s.Kind, birthTs01, s.Reach);
            for (int i = 0; i < QuadVerts; i++)
            {
                _uv0.Add(a);
                _uv1.Add(b);
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
        }

        private static float ClassifyKind(float radius)
        {
            if (radius < 0.25f) return 0f;
            if (radius < 0.55f) return 1f;
            if (radius < 1.0f) return 2f;
            if (radius < 1.9f) return 3f;
            return 4f;
        }

        private static float MakeSeed(Vector2 center, float radius)
        {
            int qx = Mathf.RoundToInt(center.x * 8f);
            int qz = Mathf.RoundToInt(center.y * 8f);
            int qr = Mathf.RoundToInt(radius * 16f);
            unchecked
            {
                uint h = (uint)(qx * 73856093) ^ (uint)(qz * 19349663) ^ (uint)(qr * 83492791);
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
