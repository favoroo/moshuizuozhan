using P1.Core;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// Tidal Slam 落点范围投影：把原先的纯平面预警圆盘换成贴合真实场地的「阴影状」网格。
    /// 场地不是一整块平地——出生甲板、5 级台阶（0.24m/级）、集装箱、坡道都在 5.2m 半径内，
    /// 平面圆盘会悬空或插进箱体，玩家读不出「圈到哪儿、盖住了什么」。
    /// 实现：以落点为中心按极坐标网格向下打射线（顶视投影），采样每个网格点的顶面高度与法线，
    /// 再生成顺着台阶/箱顶/地面起伏的三角面片；三层（危险盘 / 秒杀盘 / 亮边）共用一个顶点缓冲。
    /// 半径变化超过 10% 才重建（重建 = 一轮射线采样），中间用 XZ 缩放平滑长大，避免每帧采样。
    /// </summary>
    public sealed class SpecialRangeShadow
    {
        /// <summary>采样射线起点相对落点中心的高度（米）：够到集装箱顶/出生甲板。</summary>
        private const float DrapeCeiling = 9f;

        /// <summary>采样射线继续向下的长度（米）：够到码头水面/下沉台阶。</summary>
        private const float DrapeDepth = 12f;

        /// <summary>网格抬高量（米）：沿采样点法线抬起，避免与地面/台阶面 z-fight。</summary>
        private const float SurfaceLift = 0.045f;

        /// <summary>半径变化超过该比例才重建（重建一次约几百条射线，不能每帧做）。</summary>
        private const float RebuildRadiusRatio = 0.10f;

        /// <summary>单次射线最多收集的命中数（从高空向下，一般 2~3 个）。</summary>
        private const int HitCapacity = 8;

        private const int LayerCount = 3;

        // 极坐标分辨率：径向越密，台阶/箱沿的贴地折线越准；移动端降一档（射线采样是 CPU 成本）。
        private const int DesktopRings = 16;
        private const int DesktopSegments = 48;
        private const int MobileRings = 12;
        private const int MobileSegments = 36;

        /// <summary>三层配色透明度：危险盘淡、秒杀区亮、外沿亮边最实（沿用 P20 三层读法）。</summary>
        private static readonly float[] LayerAlphas = { 0.32f, 0.55f, 0.88f };

        private static readonly RaycastHit[] Hits = new RaycastHit[HitCapacity];
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private readonly Transform _ownerRoot;
        private readonly Transform _root;
        private readonly MeshRenderer _renderer;
        private readonly Mesh _mesh;
        private readonly Material[] _materials;
        private readonly MaterialPropertyBlock _block;
        private readonly int _rings;
        private readonly int _segments;

        // 采样结果（首环 = 圆心，之后每环 _segments 个点）
        private readonly float[] _heightOffsets;   // 顶面高度相对落点中心的偏移
        private readonly Vector3[] _sampleNormals;
        private readonly Vector3[] _vertices;
        private readonly Vector3[] _normals;
        private readonly int[] _triangles;         // 三个子网格共用（Mesh.SetTriangles 会拷贝）

        private Vector3 _center;
        private Color _color = Color.yellow;
        private float _innerRatio = 0.615f;
        private float _radius = 1f;
        private float _builtRadius = 1f;
        private bool _visible;
        private bool _built;
        private bool _disposed;

        /// <summary>最近一次重建采样到的顶面高差（米），供测试/调试确认「阴影确实贴地铺开了」。</summary>
        public float LastHeightSpread { get; private set; }

        public bool IsVisible => _visible;

        public SpecialRangeShadow(Transform ownerRoot, string ownerName = null)
        {
            _ownerRoot = ownerRoot;
            bool mobile = Application.isMobilePlatform;
            _rings = mobile ? MobileRings : DesktopRings;
            _segments = mobile ? MobileSegments : DesktopSegments;

            string rootName = string.IsNullOrEmpty(ownerName)
                ? "SpecialWarningRing"
                : $"{ownerName}_SpecialWarningRing";
            var rootGo = new GameObject(rootName);
            _root = rootGo.transform;

            int sampleCount = (_rings + 1) * _segments;
            _heightOffsets = new float[sampleCount];
            _sampleNormals = new Vector3[sampleCount];
            _vertices = new Vector3[sampleCount];
            _normals = new Vector3[sampleCount];
            _triangles = new int[_rings * _segments * 6];

            _mesh = new Mesh { name = "SpecialRangeDrapedMesh" };
            _mesh.MarkDynamic();
            var filter = rootGo.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _renderer = rootGo.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            _materials = new Material[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                var material = new Material(shader) { name = $"SpecialRangeShadowLayer{i}" };
                VfxPool.MakeUrpUnlitTransparent(material);
                _materials[i] = material;
            }
            _renderer.sharedMaterials = _materials;
            _block = new MaterialPropertyBlock();

            rootGo.SetActive(false);
        }

        /// <summary>
        /// 在落点显示范围投影。<paramref name="radius"/> 是当前动画半径（起跳时 35%，落地前长满），
        /// <paramref name="innerRadiusRatio"/> = 秒杀圈 / 伤害半径（默认 3.2/5.2），随当前半径等比缩放。
        /// </summary>
        public void Show(Vector3 center, float radius, float innerRadiusRatio, Color color)
        {
            if (_disposed)
                return;

            _center = center;
            _color = color;
            _innerRatio = Mathf.Clamp(innerRadiusRatio, 0.05f, 0.95f);
            _radius = Mathf.Max(0.05f, radius);
            _visible = true;

            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            _root.SetPositionAndRotation(center, Quaternion.identity);

            Rebuild();
            ApplyColors();
        }

        /// <summary>更新范围半径：变化超过 10% 时重新采样贴地网格，否则只做 XZ 缩放（连续长大）。</summary>
        public void SetRadius(float radius)
        {
            if (_disposed || !_visible)
                return;

            radius = Mathf.Max(0.05f, radius);
            _radius = radius;
            if (!_built || Mathf.Abs(radius - _builtRadius) > Mathf.Max(0.05f, _builtRadius * RebuildRadiusRatio))
                Rebuild();
            else
                ApplyScale();
        }

        public void Hide()
        {
            _visible = false;
            if (_root != null && _root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _visible = false;

            if (_root != null)
                DestroySafe(_root.gameObject);
            DestroySafe(_mesh);
            if (_materials != null)
            {
                for (int i = 0; i < _materials.Length; i++)
                    DestroySafe(_materials[i]);
            }
        }

        private void Rebuild()
        {
            SampleSurface(_radius);
            BuildMesh(_radius);
            _builtRadius = _radius;
            _built = true;
            ApplyScale();
        }

        private void ApplyScale()
        {
            if (_root == null || _builtRadius <= 0.0001f)
                return;
            float scale = _radius / _builtRadius;
            _root.localScale = new Vector3(scale, 1f, scale);
        }

        /// <summary>按极坐标网格向下打射线，记录每个采样点的顶面高度（相对落点中心）与法线。</summary>
        private void SampleSurface(float radius)
        {
            // 圆心只采一次，其余环逐点采样。
            SamplePoint(_center.x, _center.z, out float centerHeight, out Vector3 centerNormal);
            float centerOffset = centerHeight - _center.y;
            for (int i = 0; i < _segments; i++)
            {
                _heightOffsets[i] = centerOffset;
                _sampleNormals[i] = centerNormal;
            }

            for (int ring = 1; ring <= _rings; ring++)
            {
                float ringRadius = radius * ring / _rings;
                int ringBase = ring * _segments;
                for (int i = 0; i < _segments; i++)
                {
                    float angle = (i / (float)_segments) * Mathf.PI * 2f;
                    SamplePoint(
                        _center.x + Mathf.Cos(angle) * ringRadius,
                        _center.z + Mathf.Sin(angle) * ringRadius,
                        out float height,
                        out Vector3 normal);

                    int index = ringBase + i;
                    _heightOffsets[index] = height - _center.y;
                    _sampleNormals[index] = normal;
                }
            }
        }

        /// <summary>
        /// 单点顶视采样：从落点上方 9m 垂直向下，取最近的非自身/非角色碰撞面。
        /// 命中不到（水域/地图边缘）时回落到落点地面高度，圆盘不会塌陷。
        /// </summary>
        private void SamplePoint(float x, float z, out float height, out Vector3 normal)
        {
            Vector3 origin = new Vector3(x, _center.y + DrapeCeiling, z);
            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, Hits, DrapeCeiling + DrapeDepth,
                GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null || IsIgnored(hit.collider))
                    continue;
                if (hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                best = i;
            }

            if (best < 0)
            {
                height = _center.y;
                normal = Vector3.up;
                return;
            }

            height = Hits[best].point.y;
            normal = Hits[best].normal.sqrMagnitude > 0.0001f ? Hits[best].normal : Vector3.up;
        }

        /// <summary>
        /// 射线命中过滤：施法者自身/角色胶囊不参与投影。
        /// 角色身体在头顶高度，若命中会出现「影子糊在自己脑袋上」的悬空块；
        /// 层未建立时靠 ownerRoot 判断兜底（测试场景常用 Default 层）。
        /// </summary>
        private bool IsIgnored(Collider collider)
        {
            Transform t = collider.transform;
            if (_ownerRoot != null && (t == _ownerRoot || t.IsChildOf(_ownerRoot)))
                return true;

            int combatantLayer = GameLayers.CombatantLayer;
            return combatantLayer >= 0 && collider.gameObject.layer == combatantLayer;
        }

        /// <summary>生成贴合采样顶面的网格：三层子网格共用同一顶点缓冲（半径按环号比例分配）。</summary>
        private void BuildMesh(float radius)
        {
            int vertexCount = (_rings + 1) * _segments;
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            for (int ring = 0; ring <= _rings; ring++)
            {
                float ringRadius = radius * ring / _rings;
                int ringBase = ring * _segments;
                for (int i = 0; i < _segments; i++)
                {
                    int index = ringBase + i;
                    float angle = (i / (float)_segments) * Mathf.PI * 2f;
                    Vector3 normal = _sampleNormals[index];
                    float height = _heightOffsets[index];
                    if (height < minHeight) minHeight = height;
                    if (height > maxHeight) maxHeight = height;

                    _vertices[index] = new Vector3(
                        Mathf.Cos(angle) * ringRadius,
                        height,
                        Mathf.Sin(angle) * ringRadius) + normal * SurfaceLift;
                    _normals[index] = normal;
                }
            }
            LastHeightSpread = maxHeight - minHeight;

            _mesh.Clear();
            _mesh.SetVertices(_vertices, 0, vertexCount);
            _mesh.SetNormals(_normals, 0, vertexCount);
            _mesh.subMeshCount = LayerCount;

            int discCount = FillTriangles(0, _rings);
            _mesh.SetTriangles(_triangles, 0, discCount, 0, false);

            // 秒杀盘半径按环号取整（3.2/5.2=0.615 → 16 环时 10/16=0.625，误差 <6%）。
            int innerRing = Mathf.Clamp(Mathf.RoundToInt(_innerRatio * _rings), 1, _rings);
            int innerCount = FillTriangles(0, innerRing);
            _mesh.SetTriangles(_triangles, 0, innerCount, 1, false);

            int edgeCount = FillTriangles(_rings - 1, _rings);
            _mesh.SetTriangles(_triangles, 0, edgeCount, 2, false);

            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// 填第 firstRing~lastRing 环之间的三角形，返回写入个数（顺序对齐 VfxPool.BuildRingMesh 的顶面朝向）。
        /// </summary>
        private int FillTriangles(int firstRing, int lastRing)
        {
            int write = 0;
            for (int ring = firstRing; ring < lastRing; ring++)
            {
                int ringBase = ring * _segments;
                int nextBase = (ring + 1) * _segments;
                for (int i = 0; i < _segments; i++)
                {
                    int next = (i + 1) % _segments;
                    int a = ringBase + i;
                    int b = nextBase + i;
                    int c = ringBase + next;
                    int d = nextBase + next;
                    _triangles[write++] = a;
                    _triangles[write++] = d;
                    _triangles[write++] = b;
                    _triangles[write++] = a;
                    _triangles[write++] = c;
                    _triangles[write++] = d;
                }
            }
            return write;
        }

        private void ApplyColors()
        {
            for (int i = 0; i < LayerCount; i++)
                SetLayerColor(i, LayerAlphas[i]);
        }

        private void SetLayerColor(int layer, float alpha)
        {
            if (_renderer == null || _block == null)
                return;
            Color color = _color;
            color.a = alpha;
            _block.SetColor(BaseColorProp, color);
            _block.SetColor(ColorProp, color);
            _renderer.SetPropertyBlock(_block, layer);
        }

        private static void DestroySafe(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}