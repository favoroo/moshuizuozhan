using System.Collections.Generic;
using P1.Ink;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 零 GC 视觉反馈池：负责墨水落点溅射粒子、手雷/大招冲击波环以及地面延时预警圈。
    /// P30 起还承载弹丸拖尾墨雾、枪口方向性水珠锥、桌面端单盏枪口点光，
    /// 以及命中卫星墨珠落地后的纯装饰补涂。
    /// </summary>
    public class VfxPool : MonoBehaviour
    {
        public static VfxPool Instance { get; private set; }

        private readonly Queue<ParticleSystem> _splatParticles = new Queue<ParticleSystem>();
        private readonly List<ExpandingRing> _activeRings = new List<ExpandingRing>();
        private readonly Queue<ExpandingRing> _ringPool = new Queue<ExpandingRing>();
        private readonly List<PendingRing> _pendingRings = new List<PendingRing>(4);
        private readonly List<SatelliteDroplet> _activeDroplets = new List<SatelliteDroplet>(24);
        private readonly Queue<SatelliteDroplet> _dropletPool = new Queue<SatelliteDroplet>(24);

        private Material _sharedParticleMat;
        private Material _sharedRingMat;
        private Material _flashMat;
        private Texture2D _flashTex;
        private ParticleSystem _shotFlashPs;
        private Mesh _discMesh;
        private Mesh _dropletMesh;

        // P30-A2：弹丸拖尾墨雾（单个 PS 承载全场景拖尾雾，1 draw call）
        private Material _puffMat;
        private ParticleSystem _trailPuffPs;

        // P30-A4：桌面端单盏池化枪口点光（全场景同时最多 1 盏，0.08s 内衰减到 0）
        private Light _muzzleLight;
        private float _muzzleLightRemaining;
        private float _muzzleLightDuration;
        private float _muzzleLightPeakIntensity;

        // P30-A3：卫星墨珠落地补涂的每帧预算（含涟漪），超出直接丢弃
        private const int MaxSpeckPaintsPerFrame = 3;
        private const float LargeSpeckRippleRadius = 0.09f;
        private int _speckBudgetFrame = -1;
        private int _speckPaintedThisFrame;

        // 墨珠池保留额度：拖尾卫星珠 / 枪口水珠不能把「命中溅射珠」挤空（命中反馈优先级最高）
        private const int DropletReserveForImpacts = 12;
        private const int DropletReserveForMuzzle = 4;
        private const int DropletReserveForRain = 6;

        /// <summary>枪口点光默认峰值强度 / 半径 / 衰减时长（桌面端 in 编辑器可见的可调常量）。</summary>
        private const float MuzzleLightPeakIntensity = 2.6f;
        private const float MuzzleLightRange = 3.4f;
        private const float MuzzleLightDecaySeconds = 0.08f;

        private struct SatelliteDroplet
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public Vector3 Velocity;
            public float Gravity;
            public float Elapsed;
            public float Lifetime;
            public float BaseScale;
            public float StretchY;
            /// <summary>落地补涂归属队伍；Neutral = 只做视觉飞行，不落地补涂。</summary>
            public Team Team;
            /// <summary>该墨珠是否需要在撞地时补一枚装饰墨点（命中溅射珠子开启，墨魂/雨滴关闭）。</summary>
            public bool PaintsOnLand;
            /// <summary>本生命周期是否已经落地结算过：池复用必须重置，防止重复回调。</summary>
            public bool Landed;
        }

        private struct PendingRing
        {
            public float Delay;
            public Vector3 Position;
            public Color Color;
            public float StartRadius;
            public float EndRadius;
            public float Duration;
        }

        private struct ExpandingRing
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public Color BaseColor;
            public float StartRadius;
            public float EndRadius;
            public float Elapsed;
            public float Duration;
        }

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Instance = this;
            InitializeResources();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            DestroyObject(_sharedParticleMat);
            _sharedParticleMat = null;

            DestroyObject(_sharedRingMat);
            _sharedRingMat = null;

            DestroyObject(_flashMat);
            _flashMat = null;

            DestroyObject(_flashTex);
            _flashTex = null;

            DestroyObject(_puffMat);
            _puffMat = null;

            DestroyObject(_discMesh);
            _discMesh = null;

            // 拖尾雾 PS / 枪口点光都是本组件的子物体，随 GameObject 一起销毁，这里只清引用。
            _trailPuffPs = null;
            _muzzleLight = null;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        /// <summary>当前活跃的扩散环数量（测试/调试可观测，用于零泄露守门）。</summary>
        public int ActiveRingCount => _activeRings.Count;

        /// <summary>当前活跃的卫星墨珠数量（测试/调试可观测，用于零泄露守门）。</summary>
        public int ActiveDropletCount => _activeDroplets.Count;

        /// <summary>等待延迟触发的冲击波环数量。</summary>
        public int PendingRingCount => _pendingRings.Count;

        /// <summary>
        /// 所有有限生命周期的 VFX 是否已经自然回收（不包含 ParticleSystem 自身剩余粒子）。
        /// 供 PlayMode 冒烟测试断言对象池无泄露。
        /// </summary>
        public bool IsEffectPoolSettled =>
            _activeRings.Count == 0 && _activeDroplets.Count == 0 && _pendingRings.Count == 0;

        private void InitializeResources()
        {
            Shader particleShader = Shader.Find("P1/InkVfx");
            if (particleShader == null)
                particleShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (particleShader == null)
                particleShader = Shader.Find("Sprites/Default");

            _sharedParticleMat = new Material(particleShader) { name = "VfxInkParticleMat" };
            // P12-D：所有卫星墨珠共享同一球网格/材质，队色是逐实例属性；
            // 开启 instancing 后同屏 20+ 墨珠从逐个 draw 收敛为 1 个批次。
            _sharedParticleMat.enableInstancing = true;
            if (_sharedParticleMat.HasProperty(BaseColorProp))
                _sharedParticleMat.SetColor(BaseColorProp, Color.white);

            // 冲击环改用加法混合：旧版 URP/Unlit 是不透明材质，环的 alpha 淡出根本不生效，
            // 只能靠「变白→突然消失」收尾；加法混合后 1-t 的 alpha 才会真的让冲击波淡掉。
            Shader additiveShader = Shader.Find("P1/VfxAdditive");
            if (additiveShader == null)
                additiveShader = Shader.Find("Sprites/Default");
            _sharedRingMat = new Material(additiveShader) { name = "VfxRingMat" };
            if (_sharedRingMat.HasProperty("_MainTex"))
                _sharedRingMat.SetTexture("_MainTex", Texture2D.whiteTexture);

            _flashTex = BuildRadialFlashTexture(64);
            _flashMat = new Material(additiveShader) { name = "VfxShotFlashMat" };
            if (_flashMat.HasProperty("_MainTex"))
                _flashMat.SetTexture("_MainTex", _flashTex);
            if (_flashMat.HasProperty("_Color"))
                _flashMat.SetColor("_Color", Color.white);

            _discMesh = BuildRingMesh(32, 0.84f, 1.0f);
            _dropletMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            _shotFlashPs = CreateShotFlashSystem();

            // P30-A2：拖尾墨雾是 alpha 混合的软圆盘（不是加法混合），复用同一张程序化软边贴图。
            Shader puffShader = Shader.Find("Sprites/Default");
            if (puffShader != null)
            {
                _puffMat = new Material(puffShader) { name = "VfxTrailPuffMat" };
                if (_puffMat.HasProperty("_MainTex"))
                    _puffMat.SetTexture("_MainTex", _flashTex);
                if (_puffMat.HasProperty("_Color"))
                    _puffMat.SetColor("_Color", Color.white);
            }
            _trailPuffPs = CreateTrailPuffSystem();

            // P30-A4：枪口点光只在桌面端创建；移动端连对象都不建，避免多一盏实时光的开销。
            if (!Application.isMobilePlatform)
                _muzzleLight = CreateMuzzleLight();

            for (int i = 0; i < 20; i++)
            {
                _splatParticles.Enqueue(CreateSplatParticleSystem(i));
            }

            for (int i = 0; i < 16; i++)
            {
                _ringPool.Enqueue(CreateRingInstance(i));
            }

            for (int i = 0; i < 24; i++)
            {
                _dropletPool.Enqueue(CreateDropletInstance(i));
            }
        }

        public void SpawnInkSplat(Vector3 worldPos, Color color, float radius = 0.9f, int burstCount = 10)
        {
            if (_splatParticles.Count == 0)
                return;

            ParticleSystem ps = _splatParticles.Dequeue();
            _splatParticles.Enqueue(ps);

            ps.transform.position = worldPos + Vector3.up * 0.08f;
            var main = ps.main;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f * radius, 0.24f * radius);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f * radius, 4.5f * radius);

            ps.Emit(burstCount);
        }

        /// <summary>
        /// 命中身体的定向墨花（P6-B，对齐 Inkwave hitSplash）：一小片墨幕从受击者胸口沿弹道方向撕开，
        /// 约 2/3 液滴顺着弹道贯穿方向飞出、1/3 逆着弹道回溅向射击者，再叠一枚短促高光让远处也读得出命中。
        /// damage01 = 伤害 / 基准（Inkwave 为 amount/60，夹在 0.3~1.5）；击杀走 <see cref="SpawnKillBurst"/>，不经此入口。
        /// <paramref name="paintTeam"/> 非 Neutral 时，甩出的卫星墨珠落地会补装饰墨点。
        /// </summary>
        public void SpawnHitSplash(
            Vector3 worldPos,
            Vector3 shotDirection,
            Color color,
            float damage01,
            bool highlight = false,
            Team paintTeam = Team.Neutral)
        {
            float k = Mathf.Clamp(damage01, 0.3f, 1.5f);
            Vector3 dir = shotDirection;
            dir.y *= 0.5f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;

            float radius = Mathf.Lerp(0.55f, 1.05f, Mathf.Clamp01(k / 1.5f));

            // 顺着弹道贯穿飞出的主喷
            int through = Mathf.Max(4, Mathf.RoundToInt(6f + 8f * k));
            SpawnInkSplatAligned(worldPos, dir, color, radius, through);

            // 逆着弹道回溅向射击者的碎墨（Inkwave：每 3 滴有 1 滴回溅）
            int back = Mathf.Max(2, through / 3);
            SpawnInkSplatAligned(worldPos, -dir, color, radius * 0.7f, back);

            // 绕弹道轴的甩出墨珠 + 短促高光（0.08s，命中在远处也能一眼读出）
            SpawnSatelliteDroplets(worldPos, dir, color, radius, Mathf.RoundToInt(2f + 2f * k), dir, paintTeam);
            // P30-C2：高光仍只给「本地玩家打中 / 本地玩家被打中」，但 SpawnImpactFlash 已把队色压到
            // 55%、alpha 收到 0.22，加法混合的白盘不会再被 Bloom 放大成整块胸口过曝。
            if (highlight)
                SpawnImpactFlash(worldPos, dir, color, radius * 1.1f);
        }

        /// <summary>
        /// 击杀爆散（P6-B，对齐 Inkwave killed 版 hitSplash + ghost）：
        /// 在 Die 已有的全方位爆墨与首层冲击环之上，补方向性贯穿爆散、延迟第二层冲击环，
        /// 以及缓缓升起消散的「墨魂」墨珠。
        /// </summary>
        public void SpawnKillBurst(Vector3 worldPos, Vector3 shotDirection, Color color, float splatRadius, Team paintTeam = Team.Neutral)
        {
            Vector3 dir = shotDirection;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            float radius = Mathf.Max(0.8f, splatRadius);

            Vector3 burstCenter = worldPos + Vector3.up * 0.5f;
            SpawnInkSplatAligned(burstCenter, dir, color, radius, 16);
            SpawnInkSplatAligned(burstCenter, -dir, color, radius * 0.7f, 6);
            SpawnSatelliteDroplets(worldPos + Vector3.up * 0.9f, dir, color, radius, 6, dir, paintTeam);

            // 第二层冲击波延迟追上主波，制造「余波」层次
            SpawnDelayedShockwaveRing(0.10f, worldPos, color, 0.4f, radius * 1.45f, 0.32f);

            SpawnGhostWisps(worldPos, color, 5);
        }

        /// <summary>
        /// 墨魂（对齐 Inkwave ghost）：几颗较大较慢的浅色墨珠从爆点缓缓升起并消散，
        /// 读作「乌贼的灵魂从墨斑里飘出来」。重力取小值让珠子减速上飘而不是抛物线坠落。
        /// </summary>
        private void SpawnGhostWisps(Vector3 worldPos, Color color, int count)
        {
            Color ghostColor = Color.Lerp(color, Color.white, 0.3f);

            for (int i = 0; i < count && _dropletPool.Count > 0; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                droplet.Transform.gameObject.SetActive(true);
                float jitterX = Random.Range(-0.25f, 0.25f);
                float jitterZ = Random.Range(-0.25f, 0.25f);
                droplet.Transform.position = worldPos
                    + new Vector3(jitterX, Random.Range(0.5f, 1.2f), jitterZ);
                droplet.Velocity = new Vector3(jitterX * 0.4f, Random.Range(1.4f, 2.4f), jitterZ * 0.4f);
                droplet.Gravity = Random.Range(0.8f, 1.6f);
                droplet.Elapsed = 0f;
                droplet.Lifetime = Random.Range(0.8f, 1.15f);
                droplet.BaseScale = Random.Range(0.06f, 0.10f);
                droplet.StretchY = 1.15f;
                // 墨魂只飘不落地补涂：池复用必须把落地状态显式清零
                droplet.Team = Team.Neutral;
                droplet.PaintsOnLand = false;
                droplet.Landed = false;
                droplet.Transform.rotation = Quaternion.LookRotation(droplet.Velocity);
                droplet.Transform.localScale = Vector3.one * droplet.BaseScale;

                droplet.PropBlock.SetColor(BaseColorProp, ghostColor);
                droplet.PropBlock.SetColor(ColorProp, ghostColor);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        /// <summary>
        /// 弹丸撞击的综合反馈入口：主溅射粒子 + 沿撞击法线甩出的延迟落地卫星墨珠。
        /// 卫星墨珠只做视觉表现（不参与涂地判定），因此不会给 Ink 系统带来额外耦合。
        /// </summary>
        public void SpawnInkImpact(Vector3 worldPos, Vector3 normal, Color color, float radius = 0.9f, int satelliteCount = 4)
        {
            SpawnInkImpact(worldPos, normal, color, radius, satelliteCount, Vector3.zero, false);
        }

        /// <summary>
        /// 带入射方向的撞击反馈（P16）：溅射液滴朝「弹丸来向」偏置，读得出这一发是从哪边打来的；
        /// <paramref name="highlight"/> 为 true 时（本地玩家自己的命中）额外叠一枚短促高光，
        /// 让玩家在 8 人混战里一眼确认「这一下是我打的」。
        /// <paramref name="paintTeam"/> 只用于「卫星墨珠落地补涂」：Neutral 表示不补涂（纯视觉）。
        /// </summary>
        public void SpawnInkImpact(
            Vector3 worldPos,
            Vector3 normal,
            Color color,
            float radius,
            int satelliteCount,
            Vector3 incomingDirection,
            bool highlight,
            Team paintTeam = Team.Neutral)
        {
            int burstCount = Mathf.Clamp(Mathf.RoundToInt(6f + radius * 5f), 6, 20);
            SpawnInkSplatAligned(worldPos, normal, color, radius, burstCount);

            // 光束 / 爆破 / 手雷 / 大招等旧调用方只传颜色：颜色恰好是队色时补上落地补涂归属，
            // 这样不必改那些系统也能让它们的卫星墨珠落地补涂（匹配不上则保持 Neutral，不补涂）。
            if (paintTeam == Team.Neutral)
                paintTeam = ResolvePaintTeamFromColor(color);

            SpawnSatelliteDroplets(worldPos, normal, color, radius, satelliteCount, incomingDirection, paintTeam);

            if (highlight)
                SpawnImpactFlash(worldPos, normal, color, radius);
        }

        /// <summary>
        /// 从溅射颜色反查队伍（只给落地补涂用）：容差 0.045，匹配不上返回 Neutral。
        /// 纯表现用途，颜色不精确时宁可少补几个墨点，也不能把敌方墨点画成己方色。
        /// </summary>
        private static Team ResolvePaintTeamFromColor(Color color)
        {
            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface == null)
                return Team.Neutral;

            const float tolerance = 0.045f;
            float toleranceSqr = tolerance * tolerance;

            Color teamA = surface.GetTeamColor(Team.TeamA);
            float dr = teamA.r - color.r;
            float dg = teamA.g - color.g;
            float db = teamA.b - color.b;
            if (dr * dr + dg * dg + db * db <= toleranceSqr)
                return Team.TeamA;

            Color teamB = surface.GetTeamColor(Team.TeamB);
            dr = teamB.r - color.r;
            dg = teamB.g - color.g;
            db = teamB.b - color.b;
            if (dr * dr + dg * dg + db * db <= toleranceSqr)
                return Team.TeamB;

            return Team.Neutral;
        }

        /// <summary>
        /// 枪口闪光：白热核心 + 沿射向甩出的短尾，0.06s 内淡出。
        /// 用单个 ParticleSystem 承载全部枪口闪光（一次 draw call），零 GC。
        /// </summary>
        public void SpawnShotFlash(Vector3 worldPos, Vector3 direction, Color color, float scale = 1f)
        {
            if (_shotFlashPs == null)
                return;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            float size = 0.30f * Mathf.Max(0.1f, scale);
            Color core = Color.Lerp(color, Color.white, 0.62f);

            var emit = new ParticleSystem.EmitParams
            {
                position = worldPos + dir * 0.03f,
                velocity = dir * 1.2f,
                startColor = core,
                startSize = size,
                startLifetime = 0.055f,
                rotation = Random.Range(0f, 360f)
            };
            _shotFlashPs.Emit(emit, 1);

            // 沿射向的第二枚更小、更快，形成「喷出去」的方向感
            emit.position = worldPos + dir * 0.12f;
            emit.velocity = dir * 3.2f;
            emit.startColor = new Color(core.r, core.g, core.b, 0.65f);
            emit.startSize = size * 0.62f;
            emit.startLifetime = 0.045f;
            _shotFlashPs.Emit(emit, 1);
        }

        /// <summary>
        /// 落点高光：短暂加法混合的软圆盘，只给本地玩家相关的命中用，避免全屏闪光污染。
        /// P30-C2 过曝修复：加法混合 + Bloom 下，队色（尤其橙红）仍会把角色胸口烧成一块白盘，
        /// 因此把颜色整体压到 55%、alpha 收到 0.22，并把最大尺寸从 1.5m 收到 1.15m。
        /// 高光保留（确认「这一下是我打的」），但只泛一下，不再过曝。
        /// </summary>
        public void SpawnImpactFlash(Vector3 worldPos, Vector3 normal, Color color, float radius)
        {
            if (_shotFlashPs == null)
                return;

            Vector3 up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            // 乘 0.55 而不是 lerp 到白：保留队色，同时让加法项远离 Bloom threshold。
            Color flashColor = color * 0.55f;
            flashColor.a = 0.22f;

            var emit = new ParticleSystem.EmitParams
            {
                position = worldPos + up * 0.04f,
                velocity = Vector3.zero,
                startColor = flashColor,
                startSize = Mathf.Clamp(radius * 1.3f, 0.32f, 1.15f),
                startLifetime = 0.08f,
                rotation = Random.Range(0f, 360f)
            };
            _shotFlashPs.Emit(emit, 1);
        }

        /// <summary>
        /// P30-C2：被 0.05s 节流合并掉的命中只放「最小化墨花」——单颗小墨珠、无卫星珠、无高光，
        /// 走同一个溅射粒子池但不触发完整 SpawnHitSplash 路径，避免连射时粒子峰值翻倍。
        /// </summary>
        public void SpawnHitSpeck(Vector3 worldPos, Vector3 shotDirection, Color color, float scale = 1f)
        {
            if (_splatParticles.Count == 0)
                return;

            ParticleSystem ps = _splatParticles.Dequeue();
            _splatParticles.Enqueue(ps);

            Vector3 dir = shotDirection;
            dir.y *= 0.4f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            ps.transform.position = worldPos + Vector3.up * 0.05f;
            ps.transform.rotation = Quaternion.LookRotation(dir);

            var main = ps.main;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f * scale, 0.09f * scale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * scale, 1.4f * scale);

            ps.Emit(1);
        }

        /// <summary>
        /// P30-A2：弹丸飞行拖尾墨雾。全场景所有弹丸共享一个 ParticleSystem（1 draw call），
        /// 低透明度队色 + 存活期内轻微膨胀淡出，读作「墨滴甩在空气里的雾」。
        /// 每帧生成数量由调用方（InkProjectilePool）做全场景节流。
        /// </summary>
        public void SpawnTrailPuff(Vector3 worldPos, Vector3 direction, Color color, float scale = 1f, float lifetime = 0.30f)
        {
            if (_trailPuffPs == null)
                return;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            var emit = new ParticleSystem.EmitParams
            {
                // 略微沿弹道反方向 + 随机偏移：雾团不能像第二颗弹丸一样黏在弹丸中心
                position = worldPos - dir * Random.Range(0.04f, 0.16f) + Random.insideUnitSphere * 0.05f,
                velocity = -dir * 0.22f + Vector3.up * 0.10f,
                startColor = new Color(color.r, color.g, color.b, 0.30f),
                startSize = Mathf.Clamp(0.38f * Mathf.Max(0.1f, scale), 0.08f, 1.6f),
                startLifetime = Mathf.Clamp(lifetime, 0.08f, 1.2f),
                rotation = Random.Range(0f, 360f)
            };
            _trailPuffPs.Emit(emit, 1);
        }

        /// <summary>
        /// P30-A2：拖尾墨雾偶尔甩出的一颗小卫星墨珠（沿速度反方向 + 少量横向散开）。
        /// 复用命中卫星珠的对象池与落地补涂逻辑；珠子很小，落地补涂受每帧预算约束。
        /// </summary>
        public void SpawnTrailSatellite(Vector3 worldPos, Vector3 direction, Color color, Team paintTeam, float scale = 1f)
        {
            // 保留池底：命中溅射珠永远优先，拖尾珠只在富余额度里生成
            if (_dropletPool.Count <= DropletReserveForImpacts)
                return;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            SatelliteDroplet droplet = _dropletPool.Dequeue();
            droplet.Transform.gameObject.SetActive(true);
            droplet.Transform.position = worldPos;

            Vector3 back = -dir;
            if (back.y > 0.15f)
                back.y = 0.15f; // 反方向不能把珠子甩得「往上飞」太多
            Vector3 velocity = back * Random.Range(1.4f, 2.8f)
                               + Random.insideUnitSphere * 0.55f
                               + Vector3.up * Random.Range(0.15f, 0.7f);

            droplet.Velocity = velocity;
            droplet.Gravity = Random.Range(12f, 18f);
            droplet.Elapsed = 0f;
            droplet.Lifetime = Random.Range(0.32f, 0.55f);
            droplet.BaseScale = Random.Range(0.030f, 0.055f) * Mathf.Clamp(scale, 0.5f, 1.6f);
            droplet.StretchY = 1f;
            // 池复用重置：队伍 / 落地标记必须重新写入
            droplet.Team = paintTeam;
            droplet.PaintsOnLand = paintTeam != Team.Neutral;
            droplet.Landed = false;
            droplet.Transform.rotation = Quaternion.LookRotation(velocity);
            droplet.Transform.localScale = Vector3.one * droplet.BaseScale;

            droplet.PropBlock.SetColor(BaseColorProp, color);
            droplet.PropBlock.SetColor(ColorProp, color);
            droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

            _activeDroplets.Add(droplet);
        }

        /// <summary>
        /// P30-A4：枪口方向性水珠锥——从枪口沿射向喷 3~5 颗短命小水珠（锥角约 12°），
        /// 复用卫星墨珠池（网格珠 + 速度拉伸），只在开火节流窗口内由 WeaponController 调用。
        /// </summary>
        public void SpawnMuzzleDroplets(
            Vector3 origin,
            Vector3 direction,
            Color color,
            int count,
            float coneDegrees = 12f,
            float scale = 1f)
        {
            if (count <= 0 || _dropletPool.Count <= DropletReserveForMuzzle)
                return;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.Cross(Vector3.forward, dir);
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right).normalized;

            float coneTan = Mathf.Tan(Mathf.Clamp(coneDegrees, 1f, 45f) * Mathf.Deg2Rad);
            float sizeScale = Mathf.Clamp(scale, 0.4f, 2f);

            for (int i = 0; i < count && _dropletPool.Count > DropletReserveForMuzzle; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                droplet.Transform.gameObject.SetActive(true);
                droplet.Transform.position = origin;

                // 锥形散布：sqrt 让水滴在锥面上均匀分布，不会挤在中心
                float spread = coneTan * Mathf.Sqrt(Random.value);
                float angle = Random.value * Mathf.PI * 2f;
                Vector3 launchDir = (dir + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * spread).normalized;

                droplet.Velocity = launchDir * Random.Range(4.5f, 8.0f) * sizeScale;
                droplet.Gravity = Random.Range(10f, 16f);
                droplet.Elapsed = 0f;
                // 短命：0.05~0.11s 内消失，读作枪口「喷出去的水线」，不会飞成第二发子弹
                droplet.Lifetime = Random.Range(0.05f, 0.11f);
                droplet.BaseScale = Random.Range(0.020f, 0.042f) * sizeScale;
                droplet.StretchY = 1f;
                // 枪口水珠落地前就消失了，不需要落地补涂；仍显式重置池复用状态
                droplet.Team = Team.Neutral;
                droplet.PaintsOnLand = false;
                droplet.Landed = false;
                droplet.Transform.rotation = Quaternion.LookRotation(launchDir);
                droplet.Transform.localScale = Vector3.one * droplet.BaseScale;

                droplet.PropBlock.SetColor(BaseColorProp, color);
                droplet.PropBlock.SetColor(ColorProp, color);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        /// <summary>
        /// P30-A4：池化枪口点光（全场景同时最多 1 盏）。只在桌面端由 WeaponController 为本地玩家开火调用；
        /// 峰值强度后 0.08s 内平方衰减到 0，range 只有 3.4m，不会照亮半个战场。
        /// </summary>
        public void SpawnMuzzleLight(Vector3 worldPos, Color color, float intensityScale = 1f)
        {
            if (_muzzleLight == null || Application.isMobilePlatform)
                return;

            _muzzleLight.transform.position = worldPos;
            _muzzleLight.color = new Color(color.r, color.g, color.b, 1f);
            _muzzleLight.range = MuzzleLightRange;
            _muzzleLightPeakIntensity = MuzzleLightPeakIntensity * Mathf.Clamp(intensityScale, 0.5f, 2f);
            _muzzleLightDuration = MuzzleLightDecaySeconds;
            _muzzleLightRemaining = MuzzleLightDecaySeconds;
            _muzzleLight.intensity = _muzzleLightPeakIntensity;
            _muzzleLight.enabled = true;
        }

        private void SpawnInkSplatAligned(Vector3 worldPos, Vector3 normal, Color color, float radius, int burstCount)
        {
            if (_splatParticles.Count == 0)
                return;

            ParticleSystem ps = _splatParticles.Dequeue();
            _splatParticles.Enqueue(ps);

            Vector3 up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            ps.transform.position = worldPos + up * 0.08f;
            // 半球发射器对齐表面法线：打在立面上时墨滴应向外溅，而不是统一朝世界 +Y 喷。
            ps.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

            var main = ps.main;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f * radius, 0.24f * radius);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f * radius, 4.5f * radius);

            ps.Emit(burstCount);
        }

        private void SpawnSatelliteDroplets(Vector3 worldPos, Vector3 normal, Color color, float radius, int count)
        {
            SpawnSatelliteDroplets(worldPos, normal, color, radius, count, Vector3.zero, Team.Neutral);
        }

        /// <summary>
        /// 卫星墨珠：除绕法线的径向扩散外，还会朝弹丸水平来向偏置。
        /// 打在斜坡/墙面上时，液滴会沿来向继续「甩」出去，而不是均匀八爪鱼式乱喷。
        /// <paramref name="paintTeam"/> 非 Neutral 时，珠子撞地会补一枚装饰墨点（纯视觉，不计覆盖率）。
        /// </summary>
        private void SpawnSatelliteDroplets(
            Vector3 worldPos,
            Vector3 normal,
            Color color,
            float radius,
            int count,
            Vector3 incomingDirection,
            Team paintTeam)
        {
            if (count <= 0)
                return;

            Vector3 up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            Vector3 tangentA = Vector3.ProjectOnPlane(Vector3.right, up);
            if (tangentA.sqrMagnitude < 0.0001f)
                tangentA = Vector3.ProjectOnPlane(Vector3.forward, up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA);

            Vector3 forward = Vector3.ProjectOnPlane(incomingDirection, up);
            bool hasForward = forward.sqrMagnitude > 0.0001f;
            if (hasForward)
                forward.Normalize();

            float sizeScale = Mathf.Clamp(radius * 1.2f, 0.7f, 1.6f);
            bool paintsOnLand = paintTeam != Team.Neutral;

            for (int i = 0; i < count && _dropletPool.Count > 0; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                droplet.Transform.gameObject.SetActive(true);
                droplet.Transform.position = worldPos + up * 0.04f;

                float angle = Random.value * Mathf.PI * 2f;
                Vector3 outward = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                Vector3 radial = outward * Random.Range(0.35f, 0.9f);
                Vector3 bias = hasForward ? forward * Random.Range(0.35f, 0.85f) : Vector3.zero;
                Vector3 launchDir = (radial + bias + up * Random.Range(0.75f, 1.35f)).normalized;
                droplet.Velocity = launchDir * (Random.Range(2.6f, 4.6f) * sizeScale);
                droplet.Gravity = Random.Range(16f, 22f);
                droplet.Elapsed = 0f;
                droplet.Lifetime = Random.Range(0.32f, 0.62f);
                droplet.BaseScale = Random.Range(0.045f, 0.085f) * sizeScale;
                droplet.StretchY = 1f;
                // 池复用重置：上一条珠子的队伍 / 落地标记绝不能残留（否则会重复回调补涂）
                droplet.Team = paintTeam;
                droplet.PaintsOnLand = paintsOnLand;
                droplet.Landed = false;
                droplet.Transform.rotation = Quaternion.LookRotation(launchDir);
                droplet.Transform.localScale = Vector3.one * droplet.BaseScale;

                droplet.PropBlock.SetColor(BaseColorProp, color);
                droplet.PropBlock.SetColor(ColorProp, color);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        public void SpawnShockwaveRing(Vector3 worldPos, Color color, float startRadius, float endRadius, float duration = 0.28f)
        {
            ExpandingRing ring = _ringPool.Count > 0 ? _ringPool.Dequeue() : CreateRingInstance(_activeRings.Count + 30);
            ring.Transform.gameObject.SetActive(true);
            ring.Transform.position = new Vector3(worldPos.x, worldPos.y + 0.04f, worldPos.z);
            ring.StartRadius = startRadius;
            ring.EndRadius = endRadius;
            ring.Elapsed = 0f;
            ring.Duration = Mathf.Max(0.05f, duration);
            ring.BaseColor = color;

            UpdateRingVisual(ring, 0f);
            _activeRings.Add(ring);
        }

        /// <summary>
        /// 延迟冲击波环：用于砸地/爆炸的第二层扩散波，制造「余波追上主波」的层次感。
        /// </summary>
        public void SpawnDelayedShockwaveRing(
            float delay,
            Vector3 worldPos,
            Color color,
            float startRadius,
            float endRadius,
            float duration = 0.3f)
        {
            _pendingRings.Add(new PendingRing
            {
                Delay = Mathf.Max(0f, delay),
                Position = worldPos,
                Color = color,
                StartRadius = startRadius,
                EndRadius = endRadius,
                Duration = duration
            });
        }

        /// <summary>
        /// 风暴云落雨：从云底向下坠落的拉伸墨滴（纯视觉，不参与涂地判定）。
        /// </summary>
        public void SpawnRainDrops(Vector3 cloudBottom, float radius, Color color, int count)
        {
            if (count <= 0)
                return;

            float safeRadius = Mathf.Max(0.1f, radius);
            // 风暴雨滴是环境表现：池底留 6 颗给命中溅射珠，别让大招把战斗反馈的墨珠挤空
            for (int i = 0; i < count && _dropletPool.Count > DropletReserveForRain; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                float angle = Random.value * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Random.value) * safeRadius;

                droplet.Transform.gameObject.SetActive(true);
                droplet.Transform.position = cloudBottom + new Vector3(
                    Mathf.Cos(angle) * distance,
                    Random.Range(0f, 0.6f),
                    Mathf.Sin(angle) * distance);
                droplet.Velocity = Vector3.down * Random.Range(9f, 13f);
                droplet.Gravity = 0f;
                droplet.Elapsed = 0f;
                droplet.Lifetime = Random.Range(0.20f, 0.34f);
                droplet.BaseScale = Random.Range(0.030f, 0.055f);
                droplet.StretchY = Random.Range(2.2f, 3.2f);
                // 风暴雨滴是环境表现，不做落地补涂（避免抢占战斗命中的每帧墨点预算）
                droplet.Team = Team.Neutral;
                droplet.PaintsOnLand = false;
                droplet.Landed = false;
                droplet.Transform.localScale = new Vector3(
                    droplet.BaseScale * 0.7f, droplet.BaseScale * droplet.StretchY, droplet.BaseScale * 0.7f);

                droplet.PropBlock.SetColor(BaseColorProp, color);
                droplet.PropBlock.SetColor(ColorProp, color);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = _pendingRings.Count - 1; i >= 0; i--)
            {
                PendingRing pending = _pendingRings[i];
                pending.Delay -= dt;
                if (pending.Delay > 0f)
                {
                    _pendingRings[i] = pending;
                    continue;
                }

                _pendingRings.RemoveAt(i);
                SpawnShockwaveRing(pending.Position, pending.Color, pending.StartRadius, pending.EndRadius, pending.Duration);
            }

            for (int i = _activeRings.Count - 1; i >= 0; i--)
            {
                ExpandingRing r = _activeRings[i];
                r.Elapsed += dt;
                float t = Mathf.Clamp01(r.Elapsed / r.Duration);
                UpdateRingVisual(r, t);

                if (r.Elapsed >= r.Duration)
                {
                    r.Transform.gameObject.SetActive(false);
                    _ringPool.Enqueue(r);
                    _activeRings.RemoveAt(i);
                }
                else
                {
                    _activeRings[i] = r;
                }
            }

            UpdateSatelliteDroplets(dt);
            UpdateMuzzleLight(dt);
        }

        /// <summary>枪口点光峰值后平方衰减：0.08s 内从峰值落到 0，随后关闭组件（不销毁 GameObject）。</summary>
        private void UpdateMuzzleLight(float dt)
        {
            if (_muzzleLight == null || _muzzleLightRemaining <= 0f)
                return;

            _muzzleLightRemaining -= dt;
            if (_muzzleLightRemaining <= 0f)
            {
                _muzzleLightRemaining = 0f;
                _muzzleLight.intensity = 0f;
                _muzzleLight.enabled = false;
                return;
            }

            float k = _muzzleLightRemaining / Mathf.Max(0.0001f, _muzzleLightDuration);
            _muzzleLight.intensity = _muzzleLightPeakIntensity * k * k;
        }

        private void UpdateSatelliteDroplets(float dt)
        {
            for (int i = _activeDroplets.Count - 1; i >= 0; i--)
            {
                SatelliteDroplet droplet = _activeDroplets[i];
                droplet.Elapsed += dt;

                if (droplet.Elapsed >= droplet.Lifetime)
                {
                    droplet.Transform.gameObject.SetActive(false);
                    _dropletPool.Enqueue(droplet);
                    _activeDroplets.RemoveAt(i);
                    continue;
                }

                Vector3 previousPosition = droplet.Transform.position;
                droplet.Velocity.y -= droplet.Gravity * dt;
                Vector3 nextPosition = previousPosition + droplet.Velocity * dt;
                droplet.Transform.position = nextPosition;

                // P30-A3：命中卫星墨珠撞地 → 补一枚纯装饰墨点（不写玩法网格 / 不计覆盖率）。
                // 只在向下飞行时做一次线段射线，避免每一帧每颗珠子都做物理查询。
                if (droplet.PaintsOnLand && !droplet.Landed && droplet.Velocity.y < 0f)
                {
                    Vector3 segment = nextPosition - previousPosition;
                    float segmentLength = segment.magnitude;
                    if (segmentLength > 0.0001f &&
                        Physics.Raycast(
                            previousPosition,
                            segment / segmentLength,
                            out RaycastHit landHit,
                            segmentLength + 0.03f,
                            GameLayers.WorldMask,
                            QueryTriggerInteraction.Ignore))
                    {
                        // 标记 + 立即回收：同一颗珠子绝不会二次结算（池复用 spawn 时显式重置）
                        droplet.Landed = true;
                        HandleDropletLanding(landHit.point, landHit.normal, droplet.Team, droplet.BaseScale);

                        droplet.Transform.gameObject.SetActive(false);
                        _dropletPool.Enqueue(droplet);
                        _activeDroplets.RemoveAt(i);
                        continue;
                    }
                }

                // 墨珠沿速度方向拉伸：高速飞行的滴墨读作「水线」，落地前再整体收缩，
                // 而不是一颗颗匀速平移的小球（那是弹球，不是墨水）。
                Vector3 velocity = droplet.Velocity;
                float speed = velocity.magnitude;
                float stretch = Mathf.Max(droplet.StretchY, Mathf.Clamp(1f + speed * 0.10f, 1f, 3.4f));
                float width = 1f / Mathf.Sqrt(stretch);

                float t = droplet.Elapsed / droplet.Lifetime;
                float scale = droplet.BaseScale * Mathf.Max(0.12f, 1f - t * t);
                if (speed > 0.05f)
                    droplet.Transform.rotation = Quaternion.LookRotation(velocity / speed);
                droplet.Transform.localScale = new Vector3(scale * width, scale * width, scale * stretch);

                _activeDroplets[i] = droplet;
            }
        }

        /// <summary>
        /// P30-A3：卫星墨珠落地补涂。全局每帧最多 3 枚（超出直接丢弃，不再排队），
        /// 只有接近水平的落点才写地面图集（墙面/立面走不到 PaintSpeckVisualOnly 的 2D 图集）；
        /// 半径 ≥ 0.09m 的大颗珠额外补一条轻微涟漪。
        /// </summary>
        private void HandleDropletLanding(Vector3 point, Vector3 normal, Team team, float baseScale)
        {
            if (team == Team.Neutral)
                return;

            // 只有落在地面/顶面才补涂：PaintSpeckVisualOnly 是 XZ 平面图集，打在墙面上会画错位置
            if (normal.y < 0.55f)
                return;

            if (InkSurfaceController.Instance == null)
                return;

            // 终场后不再留下新墨迹（与弹丸沿途滴墨的规则一致），但珠子本身已正常回收
            if (!MatchManager.CombatSettlementAllowed)
                return;

            if (_speckBudgetFrame != Time.frameCount)
            {
                _speckBudgetFrame = Time.frameCount;
                _speckPaintedThisFrame = 0;
            }
            if (_speckPaintedThisFrame >= MaxSpeckPaintsPerFrame)
                return;
            _speckPaintedThisFrame++;

            float radius = Mathf.Clamp(baseScale * 1.35f, 0.06f, 0.12f);
            InkSurfaceController.Instance.PaintSpeckVisualOnly(point, radius, team, true);

            if (radius >= LargeSpeckRippleRadius)
            {
                InkSurfaceController.Instance.TriggerRipple(point, 0.02f, 0.3f, 3.0f, 0.6f);
            }
        }

        private void UpdateRingVisual(ExpandingRing ring, float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(ring.StartRadius, ring.EndRadius, eased);
            ring.Transform.localScale = new Vector3(radius, 1f, radius);

            Color c = Color.Lerp(ring.BaseColor, Color.white, (1f - t) * 0.35f);
            c.a = 1f - t;
            ring.PropBlock.SetColor(BaseColorProp, c);
            ring.PropBlock.SetColor(ColorProp, c);
            ring.Renderer.SetPropertyBlock(ring.PropBlock);
        }

        /// <summary>
        /// P30-A2 拖尾墨雾系统：单个 Billboard ParticleSystem 承载全场景弹丸拖尾雾（1 draw call）。
        /// alpha 混合 + 软边圆盘，粒子在生命周期内轻微膨胀并淡出；发射器关闭，全部靠 Emit 手动发射。
        /// </summary>
        private ParticleSystem CreateTrailPuffSystem()
        {
            if (_puffMat == null)
                return null;

            var go = new GameObject("TrailPuffPS");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.30f;
            main.startSpeed = 0f;
            main.startSize = 0.38f;
            main.startColor = Color.white;
            main.gravityModifier = -0.02f; // 极轻微上浮：雾团不会像石子一样坠地
            main.maxParticles = 128;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            // 出生略不透明 → 死亡完全透明：低透明度墨雾不能抢弹丸本体的可读性
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.5f));

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = _puffMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>
        /// P30-A4 单盏枪口点光：桌面端全场景唯一一盏，常驻但默认 disabled，
        /// 开火时把位置/队色/强度推上去，由 <see cref="UpdateMuzzleLight"/> 在 0.08s 内衰减回 0。
        /// </summary>
        private Light CreateMuzzleLight()
        {
            var go = new GameObject("MuzzleLight");
            go.transform.SetParent(transform, false);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.range = MuzzleLightRange;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
            light.enabled = false;

            return light;
        }

        /// <summary>
        /// 枪口闪光 / 落点高光共用的单个 Billboard ParticleSystem：
        /// 一次 draw call 承载全部闪光，比每个闪光一个 MeshRenderer 省得多。
        /// </summary>
        private ParticleSystem CreateShotFlashSystem()
        {
            var go = new GameObject("ShotFlashPS");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.06f;
            main.startSpeed = 0f;
            main.startSize = 0.3f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            // 生命周期内淡出 + 轻微收缩：加法混合下就是「闪一下就没」
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.62f));

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.sharedMaterial = _flashMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.sortingFudge = -50f;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>程序化软边圆盘贴图：中心亮、边缘透明，枪口闪光与落点高光共用。</summary>
        private static Texture2D BuildRadialFlashTexture(int size)
        {
            size = Mathf.Clamp(size, 16, 256);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "VfxRadialFlash",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float inv = 1f / Mathf.Max(1, size - 1);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x * inv * 2f - 1f;
                    float v = y * inv * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a); // smoothstep：软边，不出现硬圆盘
                    a *= a;                    // 中心更亮，外圈更快衰减
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private ParticleSystem CreateSplatParticleSystem(int idx)
        {
            var go = new GameObject($"InkSplatPS_{idx:00}");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            // 新建的 ParticleSystem 会自动开始播放，必须先彻底停止再改 duration 等参数，
            // 否则 Unity 会持续报 "Setting the duration while system is still playing"。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            psr.sharedMaterial = _sharedParticleMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            return ps;
        }

        private ExpandingRing CreateRingInstance(int idx)
        {
            var go = new GameObject($"ShockwaveRing_{idx:00}");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _discMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedRingMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);

            return new ExpandingRing
            {
                Transform = go.transform,
                Renderer = mr,
                PropBlock = new MaterialPropertyBlock()
            };
        }

        private SatelliteDroplet CreateDropletInstance(int idx)
        {
            var go = new GameObject($"InkSatelliteDrop_{idx:00}");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _dropletMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedParticleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            go.SetActive(false);

            return new SatelliteDroplet
            {
                Transform = go.transform,
                Renderer = mr,
                PropBlock = new MaterialPropertyBlock(),
                BaseScale = 0.06f,
                StretchY = 1f
            };
        }

        public static Mesh BuildRingMesh(int segments, float innerRatio, float outerRadius)
        {
            Mesh mesh = new Mesh { name = "ProceduralFlatRing" };
            int vCount = segments * 2;
            Vector3[] verts = new Vector3[vCount];
            Vector3[] norms = new Vector3[vCount];
            int[] tris = new int[segments * 6];

            float innerR = outerRadius * innerRatio;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                verts[i * 2] = new Vector3(cos * innerR, 0f, sin * innerR);
                verts[i * 2 + 1] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                norms[i * 2] = Vector3.up;
                norms[i * 2 + 1] = Vector3.up;

                int next = (i + 1) % segments;
                int ti = i * 6;
                tris[ti + 0] = i * 2;
                tris[ti + 1] = next * 2 + 1;
                tris[ti + 2] = i * 2 + 1;
                tris[ti + 3] = i * 2;
                tris[ti + 4] = next * 2;
                tris[ti + 5] = next * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 把 URP 材质切换为透明表面混合模式（Lit/Unlit 均适用；缺属性时静默跳过）。
        /// 程序化生成的地面指示类特效（预警盘、免疫提示环等）用它拿到 SrcAlpha 混合；
        /// 与 InkCharacterMotor.MakeTransparent 同一写法，收在 VfxPool 供 VFX 域复用。
        /// </summary>
        public static void MakeUrpUnlitTransparent(Material m)
        {
            if (m == null)
                return;
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // 运行时手动切透明必须显式改队列，否则仍排 Opaque(2000)，会被不透明体遮挡/覆盖
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
