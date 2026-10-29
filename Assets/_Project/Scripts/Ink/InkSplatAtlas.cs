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
    /// 通道打包（见 InkSplat.shader）：
    ///   R = 队伍归属，G = 湿润度，B = 单斑色调，A = 覆盖率。
    /// 因此不再有「每格一个随机色」的马赛克，湿润度也能真正随时间衰减。
    /// </summary>
    public sealed class InkSplatAtlas : IDisposable
    {
        /// <summary>同一帧最多绘制的墨斑数量（含生长中的重绘），超出者直接以完成态补画。</summary>
        public const int MaxQuads = 384;

        private const int QuadVerts = 4;

        private struct Splat
        {
            public Vector2 CenterXZ;
            public float Radius;
            public float Team;
            public float Seed;
            public float Kind;
            public float Reach;
            public float BirthTime;
        }

        private static readonly float[] KindReach = { 1.9f, 2.3f, 2.5f, 2.7f, 2.75f };

        private readonly RenderTexture _atlas;
        private readonly Material _splatMaterial;
        private readonly Material _dryMaterial;
        private readonly Mesh _quadMesh;
        private readonly Mesh _fullscreenMesh;
        private readonly CommandBuffer _cmd = new CommandBuffer { name = "InkSplatAtlas" };

        private readonly List<Splat> _pending = new List<Splat>(256);
        private readonly List<Splat> _active = new List<Splat>(256);
        private readonly List<Splat> _instant = new List<Splat>(64);

        private readonly List<Vector3> _verts = new List<Vector3>(MaxQuads * QuadVerts);
        private readonly List<Vector4> _uv0 = new List<Vector4>(MaxQuads * QuadVerts);
        private readonly List<Vector4> _uv1 = new List<Vector4>(MaxQuads * QuadVerts);
        private readonly int[] _indices = new int[MaxQuads * 6];

        private readonly Vector2 _mapSize;
        private readonly Vector2 _originXZ;
        private readonly float _texelMeters;
        private readonly float _growSeconds;
        private readonly float _dryingSeconds;
        private readonly float _dryHz;

        private bool _needsClear = true;
        private bool _disposed;
        private float _wetUntil;
        private float _nextDryTime;
        private float _baseDryStep;
        private float _nextMipTime;

        /// <summary>最近一次命令缓冲执行的 CPU 耗时（毫秒）。</summary>
        public double LastCommandMs { get; private set; }

        /// <summary>累计落墨次数（诊断用）。</summary>
        public int TotalSplatCount { get; private set; }

        /// <summary>当前正在生长动画中的墨斑数量。</summary>
        public int ActiveSplatCount => _active.Count;

        public RenderTexture Atlas => _atlas;
        public Vector2 MapSize => _mapSize;
        public Vector2 OriginXZ => _originXZ;

        public InkSplatAtlas(
            Vector2 mapSize,
            Vector2 originXZ,
            int resolution,
            bool mipmaps,
            float growSeconds,
            float dryingSeconds,
            float dryHz = 15f)
        {
            _mapSize = new Vector2(Mathf.Max(0.01f, mapSize.x), Mathf.Max(0.01f, mapSize.y));
            _originXZ = originXZ;
            _growSeconds = Mathf.Max(0f, growSeconds);
            _dryingSeconds = Mathf.Max(0.25f, dryingSeconds);
            _dryHz = Mathf.Clamp(dryHz, 1f, 60f);

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
            _baseDryStep = 1f / _dryingSeconds / _dryHz;

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

            // 放在 Resources 下，保证打包时不会被 Shader Stripping 剔除（Shader.Find 在 Player 里找不到未引用着色器）
            Shader shader = Resources.Load<Shader>("InkSplat");
            if (shader == null)
            {
                shader = Shader.Find("P1/InkSplat");
            }
            if (shader == null)
            {
                Debug.LogError("[InkSplatAtlas] 找不到 P1/InkSplat 着色器，地面将不显示墨水。");
                return;
            }

            _splatMaterial = new Material(shader)
            {
                name = "Mat_InkSplat",
                hideFlags = HideFlags.DontSave
            };
            _splatMaterial.SetVector("_InkMapSize", new Vector4(_mapSize.x, _mapSize.y, 0f, 0f));
            _splatMaterial.SetFloat("_InkTexelMeters", _texelMeters);
            // D3D / Metal / Vulkan 的渲染目标原点在左上，clip Y 需要翻转才能对上纹理 V
            _splatMaterial.SetFloat("_InkYFlip", SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);

            _dryMaterial = new Material(shader)
            {
                name = "Mat_InkDry",
                hideFlags = HideFlags.DontSave
            };
            _dryMaterial.SetFloat("_DryAmount", _baseDryStep);

            _quadMesh = BuildQuadMesh();
            _fullscreenMesh = BuildFullscreenMesh();

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
            DestroyObject(_dryMaterial);
            DestroyObject(_quadMesh);
            DestroyObject(_fullscreenMesh);

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
            _wetUntil = 0f;
        }

        /// <summary>
        /// 记录一次落墨。位置与半径决定随机种子：同一处重复落墨外观稳定，连续滴墨自然各不相同。
        /// </summary>
        public void AddSplat(Vector2 centerXZ, float radius, Team team)
        {
            if (_disposed || radius <= 0.001f || team == Team.Neutral)
                return;

            float kind = ClassifyKind(radius);
            var splat = new Splat
            {
                CenterXZ = centerXZ,
                Radius = radius,
                Team = team == Team.TeamB ? 1f : 0f,
                Seed = MakeSeed(centerXZ, radius),
                Kind = kind,
                Reach = KindReach[(int)kind],
                BirthTime = Time.realtimeSinceStartup
            };

            if (_pending.Count >= MaxQuads * 2)
                _pending.RemoveAt(0);
            _pending.Add(splat);
            TotalSplatCount++;

            _wetUntil = Time.realtimeSinceStartup + _dryingSeconds + 0.3f;
        }

        /// <summary>每帧调用一次（LateUpdate）：推进生长动画、烘干，并把本帧所有墨斑一次性画进图集。</summary>
        public void Advance()
        {
            if (_disposed)
                return;

            float now = Time.realtimeSinceStartup;
            bool canGrow = _growSeconds > 0.001f;

            // 待绘制 -> 生长队列 / 完成态补画
            _instant.Clear();
            for (int i = 0; i < _pending.Count; i++)
            {
                if (canGrow && _active.Count < MaxQuads)
                {
                    _active.Add(_pending[i]);
                }
                else
                {
                    _instant.Add(_pending[i]);
                }
            }
            _pending.Clear();

            if (_active.Count > 0)
            {
                float life = _growSeconds * 1.6f;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (now - _active[i].BirthTime >= life)
                        _active.RemoveAt(i);
                }
            }

            bool dry = now < _wetUntil && now >= _nextDryTime;
            if (dry)
            {
                _nextDryTime = now + 1f / _dryHz;
            }

            bool hasDraw = _active.Count > 0 || _instant.Count > 0;
            if (!_needsClear && !hasDraw && !dry)
                return;

            bool wantMips = _atlas.useMipMap && now >= _nextMipTime;

            _cmd.Clear();

            if (_needsClear)
            {
                _cmd.SetRenderTarget(_atlas);
                _cmd.ClearRenderTarget(false, true, Color.clear);
                _needsClear = false;
            }

            if (_active.Count > 0)
            {
                BuildMesh(_active, false, now);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, 0);
            }

            if (_instant.Count > 0)
            {
                BuildMesh(_instant, true, now);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, 0);
            }

            if (dry)
            {
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_fullscreenMesh, Matrix4x4.identity, _dryMaterial, 0, 1);
            }

            if (wantMips)
            {
                _cmd.GenerateMips(_atlas);
                _nextMipTime = now + 0.25f;
            }

            _cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Execute();
        }

        /// <summary>编辑模式 / 立即出图：把所有待绘制墨斑点以完成态画进去，不做生长动画。</summary>
        public void FlushImmediate()
        {
            if (_disposed)
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
                BuildMesh(_pending, true, Time.realtimeSinceStartup);
                _cmd.SetRenderTarget(_atlas);
                _cmd.DrawMesh(_quadMesh, Matrix4x4.identity, _splatMaterial, 0, 0);
                _pending.Clear();
                did = true;
            }

            if (!did)
                return;

            _cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Execute();
        }

        private void BuildMesh(List<Splat> splats, bool instant, float now)
        {
            _verts.Clear();
            _uv0.Clear();
            _uv1.Clear();

            float grow = Mathf.Max(0.02f, _growSeconds);
            for (int i = 0; i < splats.Count; i++)
            {
                Splat s = splats[i];
                float progress = instant ? 3f : (now - s.BirthTime) / grow;
                AddQuad(s, progress);
            }

            // 必须先 Clear：上一帧的索引缓冲可能引用比本帧更多的顶点，直接 SetVertices 会报
            // "Mesh.vertices is too small"，并且在测试里会被判为运行时错误。
            // 同时限制 quadCount：FlushImmediate 可能一次提交 MaxQuads*2 个待绘制墨斑，
            // 而 _indices 只为 MaxQuads 个 quad 预留，超出会索引越界。
            int quadCount = Mathf.Min(_verts.Count / QuadVerts, MaxQuads);
            _quadMesh.Clear();
            _quadMesh.SetVertices(_verts);
            _quadMesh.SetUVs(0, _uv0);
            _quadMesh.SetUVs(1, _uv1);
            _quadMesh.SetIndices(_indices, 0, quadCount * 6, MeshTopology.Triangles, 0, false);
        }

        private void AddQuad(Splat s, float progress)
        {
            float uvX = (s.CenterXZ.x - _originXZ.x) / _mapSize.x;
            float uvY = (s.CenterXZ.y - _originXZ.y) / _mapSize.y;
            // 顶点是单位四边面片 [-1,1]；真实米制尺寸由顶点着色器用 radius * reach 展开，
            // 这样片元里的 local 坐标就是「距墨斑中心的米制偏移」，SDF 才算在正确尺度上。
            _verts.Add(new Vector3(-1f, -1f, 0f));
            _verts.Add(new Vector3(1f, -1f, 0f));
            _verts.Add(new Vector3(1f, 1f, 0f));
            _verts.Add(new Vector3(-1f, 1f, 0f));

            var a = new Vector4(uvX, uvY, s.Radius, s.Team);
            var b = new Vector4(s.Seed, s.Kind, progress, s.Reach);
            for (int i = 0; i < QuadVerts; i++)
            {
                _uv0.Add(a);
                _uv1.Add(b);
            }
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

        private static Mesh BuildFullscreenMesh()
        {
            var mesh = new Mesh
            {
                name = "InkDryQuad",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 1f, 0f)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }
    }
}
