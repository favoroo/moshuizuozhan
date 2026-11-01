using System.Collections.Generic;
using P1.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Ink
{
    /// <summary>
    /// 可涂 / 可潜行攀爬的竖直墙面。
    ///
    /// 实现方式：以自身 BoxCollider 的世界包围盒生成 4 张竖直面（±X / ±Z），每面一套独立的
    /// <see cref="InkWallFace"/> 墨格与运行时贴图，并以略微外扩的透明 Quad 叠在原墙面之上渲染墨水；
    /// 未涂区域 alpha 为 0，因此原墙面外观完全不变。
    ///
    /// 该方案不修改墙体自身的网格与材质，也不依赖墙体 Shader，墙面几何全部在运行时按包围盒确定性生成。
    /// 前提：墙体为轴对齐盒子（当前场景的掩体墙与高台侧面均满足）。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class InkWallSurface : MonoBehaviour
    {
        private static readonly List<InkWallSurface> Registered = new List<InkWallSurface>();
        // FlushAllDirty 的轮转游标：保证墙面上传预算不会长期饿死队尾表面
        private static int _flushCursor;
        private static readonly Dictionary<Collider, InkWallSurface> ColliderLookup = new Dictionary<Collider, InkWallSurface>();

        /// <summary>场景内全部可涂墙面（运行时注册，供手雷/大招/重开统一处理）。</summary>
        public static IReadOnlyList<InkWallSurface> All => Registered;
        public static int ActiveCount => Registered.Count;

        [SerializeField] private BoxCollider wallCollider;
        [SerializeField] private Material overlayMaterial;
        [SerializeField] private InkGridConfigSO config;
        [SerializeField] private bool canClimb = true;

        [Header("朝向盒立面（旋转道具 / 斜坡）")]
        [Tooltip("启用后按 orientedCenterWorld / orientedSizeWorld / orientedRotation 生成 4 张立面，不再使用 Collider 的世界 AABB。")]
        [SerializeField] private bool useOrientedBox;
        [SerializeField] private Vector3 orientedCenterWorld;
        [SerializeField] private Vector3 orientedSizeWorld = Vector3.one;
        [SerializeField] private Quaternion orientedRotation = Quaternion.identity;

        private readonly List<InkWallFace> _faces = new List<InkWallFace>(4);
        private readonly List<Mesh> _meshes = new List<Mesh>(4);
        // P30-C 渲染收敛：整个表面（≤4~5 张立面）合并为 1 网格 + 1 张条带图集 + 1 材质 + 1 渲染器。
        // 逐面渲染器方案在涂墙功能上线后会把 DrawCalls 推高约 +300（316 张激活立面各 1 次 DrawCall）。
        private Renderer _wallRenderer;
        private Material _wallMaterial;
        private Texture2D _inkAtlas;
        private readonly List<Rect> _pageRects = new List<Rect>(4);
        private int[] _pageXOffsets = new int[4];
        private MaterialPropertyBlock _propBlock;

        private float[] _lastPaintTimes = new float[4];
        private float[] _currentWetness = new float[4];

        private Bounds _worldBounds;
        private bool _initialized;
        private bool _isRegistered;

        private static readonly int InkTexProp = Shader.PropertyToID("_InkTex");
        private static readonly int WallWetnessProp = Shader.PropertyToID("_WallWetness");

        public Bounds WorldBounds => _worldBounds;

        /// <summary>墙面顶部世界 Y 坐标（4 张立面等高）。</summary>
        public float TopY => _worldBounds.max.y;

        public int FaceCount => _faces.Count;
        public InkGridConfigSO Config => config;
        public BoxCollider WallCollider => wallCollider;
        public bool CanClimb { get => canClimb; set => canClimb = value; }

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && !_isRegistered)
            {
                Registered.Add(this);
                _isRegistered = true;
            }
            RegisterColliders();
        }

        private void OnDisable()
        {
            if (_isRegistered)
            {
                Registered.Remove(this);
                _isRegistered = false;
            }
            UnregisterColliders();
        }

        private void OnDestroy()
        {
            UnregisterColliders();

            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] != null)
                {
                    if (Application.isPlaying) Destroy(_meshes[i]);
                    else DestroyImmediate(_meshes[i]);
                }
            }
            _meshes.Clear();

            if (_wallMaterial != null)
            {
                if (Application.isPlaying) Destroy(_wallMaterial);
                else DestroyImmediate(_wallMaterial);
                _wallMaterial = null;
            }

            if (_inkAtlas != null)
            {
                if (Application.isPlaying) Destroy(_inkAtlas);
                else DestroyImmediate(_inkAtlas);
                _inkAtlas = null;
            }
            _pageRects.Clear();

            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i]?.DestroyTexture();
            }
            _faces.Clear();
            _wallRenderer = null;
        }

        private void RegisterColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col != null)
                {
                    ColliderLookup[col] = this;
                }
            }
        }

        private void UnregisterColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col != null && ColliderLookup.TryGetValue(col, out var existing) && existing == this)
                {
                    ColliderLookup.Remove(col);
                }
            }
        }

        /// <summary>由生成脚本注入墙体碰撞体、叠加层材质与涂地配置。</summary>
        public void Configure(BoxCollider collider, Material material, InkGridConfigSO gridConfig, bool allowClimb = true)
        {
            wallCollider = collider;
            overlayMaterial = material;
            config = gridConfig;
            canClimb = allowClimb;
            if (collider != null)
            {
                ColliderLookup[collider] = this;
            }
        }

        public void Configure(Collider collider, Material material, InkGridConfigSO gridConfig, bool allowClimb = true)
        {
            if (collider is BoxCollider box)
                wallCollider = box;
            overlayMaterial = material;
            config = gridConfig;
            canClimb = allowClimb;
            if (collider != null)
            {
                ColliderLookup[collider] = this;
            }
        }

        /// <summary>
        /// 按任意朝向的盒子生成 4 张立面（旋转过的木箱/花坛、带坡度的斜坡等）。
        /// 与 <see cref="Configure(BoxCollider, Material, InkGridConfigSO, bool)"/> 的区别：
        /// 前者取 Collider 的世界 AABB（只适合轴对齐盒），这里直接用传入的世界中心/尺寸/旋转，
        /// 因此旋转盒的立面与网格表面严格贴合，不会出现"墨片悬空"。
        /// collider 只用于弹道命中映射与攀爬探测。
        /// </summary>
        public void ConfigureOrientedBox(
            Collider collider,
            Vector3 centerWorld,
            Vector3 sizeWorld,
            Quaternion rotation,
            Material material,
            InkGridConfigSO gridConfig,
            bool allowClimb = true)
        {
            if (collider is BoxCollider box)
                wallCollider = box;
            useOrientedBox = true;
            orientedCenterWorld = centerWorld;
            orientedSizeWorld = new Vector3(
                Mathf.Max(0.01f, sizeWorld.x),
                Mathf.Max(0.01f, sizeWorld.y),
                Mathf.Max(0.01f, sizeWorld.z));
            orientedRotation = rotation;
            overlayMaterial = material;
            config = gridConfig;
            canClimb = allowClimb;
            if (collider != null)
            {
                ColliderLookup[collider] = this;
            }
        }

        public bool InitializeIfNeeded(bool force = false)
        {
            if (_initialized)
                return true;

            // 贴图与 Quad 都是运行时对象：编辑器里默认不生成（除非显式 force，供单测验证结构）
            if (!Application.isPlaying && !force)
                return false;

            if (wallCollider == null)
            {
                wallCollider = GetComponent<BoxCollider>();
            }

            Collider col = wallCollider != null ? (Collider)wallCollider : GetComponent<Collider>();
            if (useOrientedBox)
            {
                // 朝向盒自身就是墨面的真实范围：不能用 collider.bounds（南边界矮墙的
                // 碰撞体比可见网格高 2.45m，用 AABB 会让墨片飘在墙上方）。
                _worldBounds = ComputeOrientedWorldBounds();
            }
            else if (col != null)
            {
                _worldBounds = col.bounds;
            }
            else
            {
                Renderer rend = GetComponent<Renderer>();
                if (rend != null)
                {
                    _worldBounds = rend.bounds;
                }
                else
                {
                    Debug.LogWarning($"[InkWallSurface] {name} 没有 Collider 或 Renderer，无法生成可涂墙面。", this);
                    return false;
                }
            }

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            if (overlayMaterial == null)
            {
                Shader sh = Shader.Find("P1/InkWall");
                if (sh != null)
                {
                    overlayMaterial = new Material(sh) { name = "RuntimeInkWallMat" };
                }
            }
            float cellSize = config != null ? config.wallCellSize : 0.1f;
            Color32 teamA = config != null ? (Color32)config.teamAColor : new Color32(255, 112, 15, 255);
            Color32 teamB = config != null ? (Color32)config.teamBColor : new Color32(38, 107, 255, 255);
            Color32 neutral = new Color32(0, 0, 0, 0);

            Vector3 min = _worldBounds.min;
            Vector3 max = _worldBounds.max;
            Vector3 size = _worldBounds.size;

            if (useOrientedBox)
            {
                AddOrientedBoxFaces(cellSize, neutral, teamA, teamB);
            }
            else
            {
                // 4 张立面：原点取 (u=0,v=0) 角点，U×V 必须等于朝外法线，保证法线/朝向一致
                AddFace("X+", new Vector3(max.x, min.y, max.z), Vector3.back, Vector3.up, size.z, size.y, cellSize, neutral, teamA, teamB);
                AddFace("X-", new Vector3(min.x, min.y, min.z), Vector3.forward, Vector3.up, size.z, size.y, cellSize, neutral, teamA, teamB);
                AddFace("Z+", new Vector3(min.x, min.y, max.z), Vector3.right, Vector3.up, size.x, size.y, cellSize, neutral, teamA, teamB);
                AddFace("Z-", new Vector3(max.x, min.y, min.z), Vector3.left, Vector3.up, size.x, size.y, cellSize, neutral, teamA, teamB);
            }

            if (_lastPaintTimes == null || _lastPaintTimes.Length < _faces.Count)
            {
                _lastPaintTimes = new float[_faces.Count];
                _currentWetness = new float[_faces.Count];
            }
            for (int i = 0; i < _lastPaintTimes.Length; i++)
            {
                _lastPaintTimes[i] = -1000f;
                _currentWetness[i] = 0f;
            }

            _initialized = true;
            BuildMergedWallVisual();

            if (!_isRegistered)
            {
                Registered.Add(this);
                _isRegistered = true;
            }

            return true;
        }

        private void AddFace(
            string faceName,
            Vector3 originWorld,
            Vector3 uAxis,
            Vector3 vAxis,
            float worldWidth,
            float worldHeight,
            float cellSize,
            Color32 neutral,
            Color32 teamA,
            Color32 teamB)
        {
            var face = new InkWallFace(
                originWorld,
                uAxis,
                vAxis,
                Vector3.Cross(uAxis, vAxis),
                worldWidth,
                worldHeight,
                cellSize,
                neutral,
                teamA,
                teamB,
                $"{name}_{faceName}");

            _faces.Add(face);
            // 视觉不再逐面构建：所有立面加完后由 BuildMergedWallVisual 统一合并
            // （1 网格 + 1 图集 + 1 渲染器），见该方法的性能注释。
        }

        /// <summary>朝向盒 8 个角点的世界 AABB（供攀爬判定与诊断使用）。</summary>
        private Bounds ComputeOrientedWorldBounds()
        {
            Vector3 right = orientedRotation * Vector3.right;
            Vector3 up = orientedRotation * Vector3.up;
            Vector3 forward = orientedRotation * Vector3.forward;
            Vector3 half = orientedSizeWorld * 0.5f;

            Bounds bounds = new Bounds(orientedCenterWorld, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = orientedCenterWorld
                                 + right * (half.x * sign.x)
                                 + up * (half.y * sign.y)
                                 + forward * (half.z * sign.z);
                bounds.Encapsulate(corner);
            }
            return bounds;
        }

        /// <summary>
        /// 按朝向盒生成 4 张竖直立面。U/V 取旋转后的局部轴，U×V = 朝外法线，
        /// 因此旋转盒子与斜坡的墨面与网格表面共面（外扩 1cm 防 Z-Fighting 由 BuildMergedWallVisual 负责）。
        /// </summary>
        private void AddOrientedBoxFaces(float cellSize, Color32 neutral, Color32 teamA, Color32 teamB)
        {
            Vector3 right = orientedRotation * Vector3.right;
            Vector3 up = orientedRotation * Vector3.up;
            Vector3 forward = orientedRotation * Vector3.forward;
            Vector3 center = orientedCenterWorld;
            Vector3 half = orientedSizeWorld * 0.5f;
            float height = orientedSizeWorld.y;

            // X+ / X-：宽 = 局部 Z，法线 = ±right
            AddFace("X+",
                center + right * half.x - up * half.y + forward * half.z,
                -forward, up, orientedSizeWorld.z, height, cellSize, neutral, teamA, teamB);
            AddFace("X-",
                center - right * half.x - up * half.y - forward * half.z,
                forward, up, orientedSizeWorld.z, height, cellSize, neutral, teamA, teamB);

            // Z+ / Z-：宽 = 局部 X，法线 = ±forward
            AddFace("Z+",
                center - right * half.x - up * half.y + forward * half.z,
                right, up, orientedSizeWorld.x, height, cellSize, neutral, teamA, teamB);
            AddFace("Z-",
                center + right * half.x - up * half.y - forward * half.z,
                -right, up, orientedSizeWorld.x, height, cellSize, neutral, teamA, teamB);
        }

        /// <summary>
        /// P30-C 渲染收敛：把本表面全部立面合并为 1 个网格 + 1 张条带图集 + 1 材质 + 1 渲染器。
        /// 逐面方案每张激活立面各占 1 次 DrawCall（全场 300+ 张 → +300 DrawCalls），
        /// 合并后每表面恒定 1 次。UV0 直接映射到图集页矩形，InkWall shader 无需任何改动；
        /// UV1（面尺寸描边）与顶点法线保持逐顶点语义不变。
        /// </summary>
        private void BuildMergedWallVisual()
        {
            if (_faces.Count == 0)
                return;

            // 1) 图集布局：横向条带，每面一页；同表面立面等高、宽度可不同
            int atlasWidth = 0;
            int atlasHeight = 1;
            for (int i = 0; i < _faces.Count; i++)
            {
                atlasWidth += _faces[i].Width;
                atlasHeight = Mathf.Max(atlasHeight, _faces[i].Height);
            }

            _pageRects.Clear();
            if (_pageXOffsets == null || _pageXOffsets.Length < _faces.Count)
            {
                _pageXOffsets = new int[_faces.Count];
            }
            int cursor = 0;
            for (int i = 0; i < _faces.Count; i++)
            {
                _pageXOffsets[i] = cursor;
                _pageRects.Add(new Rect(
                    (float)cursor / atlasWidth,
                    0f,
                    (float)_faces[i].Width / atlasWidth,
                    (float)_faces[i].Height / atlasHeight));
                cursor += _faces[i].Width;
            }

            _inkAtlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"InkWallAtlas_{name}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            // 2) 合并网格：每面一个四边形，顶点/法线仍写在墙体局部空间并沿法线外扩 1cm 防 Z-Fighting
            int faceCount = _faces.Count;
            var verts = new List<Vector3>(faceCount * 4);
            var uv0 = new List<Vector2>(faceCount * 4);
            var uv1 = new List<Vector2>(faceCount * 4);
            var norms = new List<Vector3>(faceCount * 4);
            var tris = new List<int>(faceCount * 6);

            for (int i = 0; i < faceCount; i++)
            {
                InkWallFace face = _faces[i];
                Vector3 outward = face.NormalWorld * 0.01f;
                Vector3 center = face.FaceToWorld(new Vector2(face.WorldWidth * 0.5f, face.WorldHeight * 0.5f));
                Vector3 uHalf = face.UAxis * (face.WorldWidth * 0.5f);
                Vector3 vHalf = face.VAxis * (face.WorldHeight * 0.5f);

                verts.Add(transform.InverseTransformPoint(center + outward - uHalf - vHalf));
                verts.Add(transform.InverseTransformPoint(center + outward + uHalf - vHalf));
                verts.Add(transform.InverseTransformPoint(center + outward + uHalf + vHalf));
                verts.Add(transform.InverseTransformPoint(center + outward - uHalf + vHalf));

                Rect page = _pageRects[i];
                uv0.Add(new Vector2(page.x, page.y));
                uv0.Add(new Vector2(page.x + page.width, page.y));
                uv0.Add(new Vector2(page.x + page.width, page.y + page.height));
                uv0.Add(new Vector2(page.x, page.y + page.height));

                // UV1 = (面宽, 面高) 米：InkWall shader 用它在涂墨立面四周画掩体边界线（P17-A）
                var faceSize = new Vector2(face.WorldWidth, face.WorldHeight);
                Vector3 localNormal = transform.InverseTransformDirection(face.NormalWorld);
                for (int k = 0; k < 4; k++)
                {
                    uv1.Add(faceSize);
                    norms.Add(localNormal);
                }

                // Unity 正面绕序：按 U×V = 朝外法线 的顺序取 0-1-2 / 0-2-3
                int b = i * 4;
                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
            }

            var mesh = new Mesh { name = $"InkWallMerged_{name}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject($"InkWallMerged_{name}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            // 整表面一个材质实例：持有图集贴图（OcclusionFader.SetPropertyBlock(null) 冲刷
            // 也不会丢绑定），并在 FlushDirty 时重新写入 MPB 双保险
            _wallMaterial = overlayMaterial != null
                ? new Material(overlayMaterial) { name = $"InkWallMat_{name}" }
                : null;
            if (_wallMaterial != null)
            {
                _wallMaterial.SetTexture(InkTexProp, _inkAtlas);
                _wallMaterial.SetFloat(WallWetnessProp, 0f);
            }
            mr.sharedMaterial = _wallMaterial;

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // 整面未落墨前保持禁用，0 DrawCall 开销；任意立面首次涂上墨斑后再激活
            mr.enabled = HasAnyInk();

            if (_propBlock == null)
            {
                _propBlock = new MaterialPropertyBlock();
            }
            _propBlock.SetTexture(InkTexProp, _inkAtlas);
            mr.SetPropertyBlock(_propBlock);

            _meshes.Add(mesh);
            _wallRenderer = mr;

            // 初始像素（全透明中性色）刷进图集，保证首次激活时图集内容已就绪
            bool uploaded = false;
            for (int i = 0; i < faceCount; i++)
            {
                uploaded |= _faces[i].UploadIfDirtyTo(_inkAtlas, _pageXOffsets[i]);
            }
            if (uploaded)
            {
                _inkAtlas.Apply(false, false);
            }
        }

        private bool HasAnyInk()
        {
            for (int i = 0; i < _faces.Count; i++)
            {
                if (_faces[i].HasInk)
                    return true;
            }
            return false;
        }

        // ---------------- 涂色 ----------------

        /// <summary>
        /// 按命中点与命中法线在对应立面上涂一个圆斑。
        /// 返回发生归属变化的格子数；命中顶面或不属于该墙体时返回 0。
        /// </summary>
        public int PaintAtWorld(Vector3 worldPoint, Vector3 worldNormal, float radius, Team team)
        {
            if (!InitializeIfNeeded())
                return 0;

            InkWallFace face = FindFace(worldNormal);
            if (face == null)
                return 0;

            Vector2 facePoint = face.WorldToFace(worldPoint);
            if (!face.IsInsideFace(facePoint, 0.2f))
                return 0;

            int changed = face.PaintCircle(facePoint, radius, team);
            if (changed > 0)
            {
                int idx = _faces.IndexOf(face);
                if (idx >= 0 && idx < _lastPaintTimes.Length)
                {
                    _lastPaintTimes[idx] = Time.time;
                }
                EnsureWallRendererEnabled();
            }
            return changed;
        }

        /// <summary>该世界点在该墙面上是否为指定队伍的墨水。</summary>
        public Team InkAtWorld(Vector3 worldPoint, Vector3 worldNormal)
        {
            if (!InitializeIfNeeded())
                return Team.Neutral;

            InkWallFace face = FindFace(worldNormal);
            return face != null ? face.InkAt(face.WorldToFace(worldPoint)) : Team.Neutral;
        }

        /// <summary>
        /// 用球形范围涂整面墙（手雷 / 大招 / 击杀爆墨用）：
        /// 球心到墙面所在平面的距离小于半径时，按相交圆半径涂在该面的投影点上。
        /// </summary>
        public int PaintSphere(Vector3 center, float radius, Team team)
        {
            if (!InitializeIfNeeded() || radius <= 0f || team == Team.Neutral)
                return 0;

            int changed = 0;
            for (int i = 0; i < _faces.Count; i++)
            {
                InkWallFace face = _faces[i];
                float planeDistance = Vector3.Dot(center - face.OriginWorld, face.NormalWorld);
                float absDist = Mathf.Abs(planeDistance);
                if (absDist >= radius)
                    continue;

                Vector3 projected = center - face.NormalWorld * planeDistance;
                float onFaceRadius = Mathf.Sqrt(radius * radius - absDist * absDist);
                int painted = face.PaintCircle(face.WorldToFace(projected), onFaceRadius, team);
                if (painted > 0)
                {
                    if (i < _lastPaintTimes.Length)
                    {
                        _lastPaintTimes[i] = Time.time;
                    }
                    EnsureWallRendererEnabled();
                    changed += painted;
                }
            }

            return changed;
        }

        public void ResetPaint()
        {
            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i].ResetPaint();
                if (i < _lastPaintTimes.Length)
                {
                    _lastPaintTimes[i] = -1000f;
                    _currentWetness[i] = 0f;
                }
            }
            if (_wallMaterial != null)
            {
                _wallMaterial.SetFloat(WallWetnessProp, 0f);
            }
            if (_wallRenderer != null)
            {
                _wallRenderer.enabled = false;
            }
        }

        /// <summary>本表面是否有脏像素块（供全局上传预算挑出需要 Apply 的表面）。</summary>
        public bool HasDirtyFaces
        {
            get
            {
                for (int i = 0; i < _faces.Count; i++)
                {
                    if (_faces[i] != null && _faces[i].IsDirty)
                        return true;
                }
                return false;
            }
        }

        public void FlushDirty()
        {
            if (_inkAtlas == null)
                return;

            bool uploaded = false;
            for (int i = 0; i < _faces.Count; i++)
            {
                // 各面把脏像素块写入条带图集的对应页，整表面一帧最多一次 Apply
                uploaded |= _faces[i].UploadIfDirtyTo(_inkAtlas, _pageXOffsets[i]);
            }

            if (!uploaded)
                return;

            _inkAtlas.Apply(false, false);

            // 自愈：这次上传后重新把图集绑回渲染器。
            // 材质实例直接持有图集（SetPropertyBlock(null) 也不会丢），
            // 这里再写一次 MPB 双保险，只在真正发生上传的帧执行，空闲帧零开销。
            RebindAtlasTexture();
        }

        /// <summary>把图集贴图重新写进表面渲染器的 MaterialPropertyBlock。</summary>
        private void RebindAtlasTexture()
        {
            if (_wallRenderer == null || _inkAtlas == null)
                return;

            if (_wallMaterial != null)
            {
                _wallMaterial.SetTexture(InkTexProp, _inkAtlas);
            }

            if (_propBlock == null)
                _propBlock = new MaterialPropertyBlock();

            _wallRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetTexture(InkTexProp, _inkAtlas);
            _wallRenderer.SetPropertyBlock(_propBlock);
        }

        /// <summary>整表面渲染器尚未激活时激活（首次落墨后调用）。</summary>
        private void EnsureWallRendererEnabled()
        {
            if (_wallRenderer != null && !_wallRenderer.enabled)
            {
                _wallRenderer.enabled = true;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized || _faces.Count == 0 || _wallMaterial == null)
                return;

            float sheenSeconds = config != null ? config.inkSheenSeconds : 22f;
            float now = Time.time;

            // 逐面计算湿润度（供 GetFaceWetness 诊断），材质取整表面最大值——
            // 合并渲染后一个材质对应多张立面，取最近被涂的面的湿润度最贴近视觉直觉
            float maxWet = 0f;
            bool dirty = false;
            for (int i = 0; i < _faces.Count; i++)
            {
                InkWallFace face = _faces[i];

                if (!face.HasInk)
                {
                    if (_currentWetness[i] > 0f)
                    {
                        _currentWetness[i] = 0f;
                        dirty = true;
                    }
                    continue;
                }

                float elapsed = now - _lastPaintTimes[i];
                float wet = ComputeWallWetness(elapsed, sheenSeconds);

                // 只有有效变化才记录，严格零 GC
                if (Mathf.Abs(_currentWetness[i] - wet) > 0.005f || (wet == 0f && _currentWetness[i] != 0f))
                {
                    _currentWetness[i] = wet;
                    dirty = true;
                }
                if (wet > maxWet)
                {
                    maxWet = wet;
                }
            }

            if (dirty)
            {
                _wallMaterial.SetFloat(WallWetnessProp, maxWet);
            }
        }

        /// <summary>
        /// 纯数学解算墙面湿润度：saturate(1 - elapsed / max(1, sheenSeconds))。
        /// 供 LateUpdate 驱动材质，以及 EditMode 单测验证单调性与边界保护。
        /// </summary>
        public static float ComputeWallWetness(float elapsedSincePaint, float sheenSeconds)
        {
            if (elapsedSincePaint < 0f)
                return 1f;

            float denom = Mathf.Max(1f, sheenSeconds);
            float wet = 1f - (elapsedSincePaint / denom);
            return Mathf.Clamp01(wet);
        }

        /// <summary>获取指定立面当前的湿润度数值（供单测与诊断）。</summary>
        public float GetFaceWetness(int index)
        {
            return (index >= 0 && index < _currentWetness.Length) ? _currentWetness[index] : 0f;
        }

        /// <summary>获取指定立面最近一次被涂时间（供单测与诊断）。</summary>
        public float GetFaceLastPaintTime(int index)
        {
            return (index >= 0 && index < _lastPaintTimes.Length) ? _lastPaintTimes[index] : -1000f;
        }

        private InkWallFace FindFace(Vector3 worldNormal)
        {
            // 命中朝上的顶面（Ny > 0.55）归顶面墨迹系统处理，不属于 4 竖直立面
            if (worldNormal.y > 0.55f)
                return null;

            InkWallFace best = null;
            float bestDot = 0.55f;
            for (int i = 0; i < _faces.Count; i++)
            {
                float dot = Vector3.Dot(_faces[i].NormalWorld, worldNormal);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = _faces[i];
                }
            }
            return best;
        }

        // ---------------- 静态批量接口 ----------------

        public static InkWallSurface FindForCollider(Collider collider)
        {
            if (collider == null)
                return null;

            if (ColliderLookup.TryGetValue(collider, out InkWallSurface wall))
            {
                if (wall != null)
                    return wall;
            }

            wall = collider.GetComponentInParent<InkWallSurface>();
            if (wall != null)
            {
                ColliderLookup[collider] = wall;
            }
            return wall;
        }

        /// <summary>对场景内所有墙面做球形涂色，返回总变化格数。</summary>
        public static int PaintAllInSphere(Vector3 center, float radius, Team team)
        {
            int changed = 0;
            for (int i = 0; i < Registered.Count; i++)
            {
                InkWallSurface wall = Registered[i];
                if (wall == null) continue;
                changed += wall.PaintSphere(center, radius, team);
            }

            return changed;
        }

        public static void ResetAllPaint()
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                Registered[i]?.ResetPaint();
            }
        }

        /// <summary>
        /// 全局墙面上传：只处理脏表面，并限制单帧 Apply 次数（默认 4）。
        /// 每面墙持有独立贴图，一次齐射/滚筒可能同时弄脏十几面墙——旧实现一帧内连续 Apply 会形成尖刺；
        /// 预算外的表面顺延到下一帧（30Hz 节流下最多滞后 ~33ms，肉眼不可见）。
        /// 使用轮转游标，保证任何一面墙都不会因为总在队尾而长期挨饿。
        /// </summary>
        public static void FlushAllDirty(int maxUploadsPerFrame = 4)
        {
            int count = Registered.Count;
            if (count == 0)
                return;

            int budget = maxUploadsPerFrame > 0 ? maxUploadsPerFrame : count;
            int start = _flushCursor % count;
            int uploaded = 0;
            int scanned = 0;

            for (; scanned < count; scanned++)
            {
                if (uploaded >= budget)
                    break;

                int idx = (start + scanned) % count;
                InkWallSurface wall = Registered[idx];
                if (wall == null || !wall.HasDirtyFaces)
                    continue;

                wall.FlushDirty();
                uploaded++;
            }

            _flushCursor = (start + Mathf.Max(1, scanned)) % count;
        }
    }
}