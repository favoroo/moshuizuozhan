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

        /// <summary>场景内全部可涂墙面（运行时注册，供手雷/大招/重开统一处理）。</summary>
        public static IReadOnlyList<InkWallSurface> All => Registered;

        [SerializeField] private BoxCollider wallCollider;
        [SerializeField] private Material overlayMaterial;
        [SerializeField] private InkGridConfigSO config;

        private readonly List<InkWallFace> _faces = new List<InkWallFace>(4);
        private readonly List<Mesh> _meshes = new List<Mesh>(4);
        private readonly List<Renderer> _faceRenderers = new List<Renderer>(4);
        private MaterialPropertyBlock _propBlock;

        private Bounds _worldBounds;
        private bool _initialized;
        private bool _isRegistered;

        private static readonly int InkTexProp = Shader.PropertyToID("_InkTex");

        public Bounds WorldBounds => _worldBounds;

        /// <summary>墙面顶部世界 Y 坐标（4 张立面等高）。</summary>
        public float TopY => _worldBounds.max.y;

        public int FaceCount => _faces.Count;
        public InkGridConfigSO Config => config;
        public BoxCollider WallCollider => wallCollider;

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
        }

        private void OnDisable()
        {
            if (_isRegistered)
            {
                Registered.Remove(this);
                _isRegistered = false;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _meshes.Count; i++)
            {
                if (_meshes[i] != null)
                {
                    if (Application.isPlaying) Destroy(_meshes[i]);
                    else DestroyImmediate(_meshes[i]);
                }
            }
            _meshes.Clear();

            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i]?.DestroyTexture();
            }
            _faces.Clear();
            _faceRenderers.Clear();
        }

        /// <summary>由生成脚本注入墙体碰撞体、叠加层材质与涂地配置。</summary>
        public void Configure(BoxCollider collider, Material material, InkGridConfigSO gridConfig)
        {
            wallCollider = collider;
            overlayMaterial = material;
            config = gridConfig;
        }

        public bool InitializeIfNeeded()
        {
            if (_initialized)
                return true;

            // 贴图与 Quad 都是运行时对象：编辑器里不生成，避免把临时贴图留在场景中
            if (!Application.isPlaying)
                return false;

            if (wallCollider == null)
            {
                wallCollider = GetComponent<BoxCollider>();
            }
            if (wallCollider == null)
            {
                Debug.LogWarning($"[InkWallSurface] {name} 没有 BoxCollider，无法生成可涂墙面。", this);
                return false;
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

            _worldBounds = wallCollider.bounds;
            float cellSize = config != null ? config.wallCellSize : 0.1f;
            Color32 teamA = config != null ? (Color32)config.teamAColor : new Color32(255, 112, 15, 255);
            Color32 teamB = config != null ? (Color32)config.teamBColor : new Color32(38, 107, 255, 255);
            Color32 neutral = new Color32(0, 0, 0, 0);

            Vector3 min = _worldBounds.min;
            Vector3 max = _worldBounds.max;
            Vector3 size = _worldBounds.size;

            // 4 张立面：原点取 (u=0,v=0) 角点，U×V 必须等于朝外法线，保证法线/朝向一致
            AddFace("X+", new Vector3(max.x, min.y, max.z), Vector3.back, Vector3.up, size.z, size.y, cellSize, neutral, teamA, teamB);
            AddFace("X-", new Vector3(min.x, min.y, min.z), Vector3.forward, Vector3.up, size.z, size.y, cellSize, neutral, teamA, teamB);
            AddFace("Z+", new Vector3(min.x, min.y, max.z), Vector3.right, Vector3.up, size.x, size.y, cellSize, neutral, teamA, teamB);
            AddFace("Z-", new Vector3(max.x, min.y, min.z), Vector3.left, Vector3.up, size.x, size.y, cellSize, neutral, teamA, teamB);

            _initialized = true;

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
            BuildFaceVisual(face);
            face.UploadIfDirty();
        }

        private void BuildFaceVisual(InkWallFace face)
        {
            var go = new GameObject($"InkFace_{face.Name}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 顶点与法线写在墙体局部空间：子节点使用单位变换，渲染结果与墙体表面精确重合；
            // 再沿法线外扩 1cm，避免与原墙面 Z-Fighting。
            Vector3 outward = face.NormalWorld * 0.01f;
            Vector3 center = face.FaceToWorld(new Vector2(face.WorldWidth * 0.5f, face.WorldHeight * 0.5f));
            Vector3 uHalf = face.UAxis * (face.WorldWidth * 0.5f);
            Vector3 vHalf = face.VAxis * (face.WorldHeight * 0.5f);

            Vector3 v0 = transform.InverseTransformPoint(center + outward - uHalf - vHalf);
            Vector3 v1 = transform.InverseTransformPoint(center + outward + uHalf - vHalf);
            Vector3 v2 = transform.InverseTransformPoint(center + outward + uHalf + vHalf);
            Vector3 v3 = transform.InverseTransformPoint(center + outward - uHalf + vHalf);
            Vector3 localNormal = transform.InverseTransformDirection(face.NormalWorld);

            var mesh = new Mesh { name = $"InkWallQuad_{face.Name}" };
            mesh.SetVertices(new List<Vector3> { v0, v1, v2, v3 });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetNormals(new List<Vector3> { localNormal, localNormal, localNormal, localNormal });
            // Unity 正面绕序：三角形叉乘方向与法线一致（内置 Quad 的 0,3,1 即为此规则），
            // 因此按 U×V = 朝外法线 的顺序取 0-1-2 / 0-2-3
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = overlayMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _propBlock.SetTexture(InkTexProp, face.Texture);
            mr.SetPropertyBlock(_propBlock);

            _meshes.Add(mesh);
            _faceRenderers.Add(mr);
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

            return face.PaintCircle(facePoint, radius, team);
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
                changed += face.PaintCircle(face.WorldToFace(projected), onFaceRadius, team);
            }

            return changed;
        }

        public void ResetPaint()
        {
            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i].ResetPaint();
            }
        }

        public void FlushDirty()
        {
            for (int i = 0; i < _faces.Count; i++)
            {
                _faces[i].UploadIfDirty();
            }
        }

        private InkWallFace FindFace(Vector3 worldNormal)
        {
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
            return collider != null ? collider.GetComponentInParent<InkWallSurface>() : null;
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

        public static void FlushAllDirty()
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                Registered[i]?.FlushDirty();
            }
        }
    }
}