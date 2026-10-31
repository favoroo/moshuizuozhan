using System;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 镜头墨滴视图（P1/UI/LensInkView）。
    /// 
    /// 参考 INKWAVE `src/fx/screenfx.js` 中的 LensInk 机制：
    /// 在镜头上飞溅出具有表面张力（Metaball SDF）的墨滴与清亮水珠；
    /// 包含：重力下滑、蛇形摇摆、干涸收缩、粘滞卡顿、下滴拖尾薄膜以及折射与双高光。
    /// 
    /// 采用全预分配数组，热路径 0 GC；无墨滴时自动关闭 Graphic，零开销。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(Image))]
    public class LensInkView : MonoBehaviour
    {
        public const int MaxDrops = 96;
        private const string ShaderName = "P1/UI/LensInk";

        public const int ChannelEnemy = 0;
        public const int ChannelOwn = 1;
        public const int ChannelWater = 2;

        private static readonly int DropsProp = Shader.PropertyToID("_LensDrops");
        private static readonly int DropParamsProp = Shader.PropertyToID("_LensDropParams");
        private static readonly int DropCountProp = Shader.PropertyToID("_LensDropCount");
        private static readonly int AspectProp = Shader.PropertyToID("_Aspect");
        private static readonly int RefractionOnProp = Shader.PropertyToID("_RefractionOn");
        private static readonly int LensColAProp = Shader.PropertyToID("_LensColA");
        private static readonly int LensColBProp = Shader.PropertyToID("_LensColB");
        private static readonly int WaterColProp = Shader.PropertyToID("_WaterCol");

        private struct DropItem
        {
            public bool active;
            public int kind; // 0: drop, 1: arm, 2: sat, 3: trail
            public int channel; // 0: enemy, 1: own, 2: water
            public float x, y;
            public float r, rx, ry;
            public float vx, vy;
            public float rot;
            public float age, life;
            public float stick;
            public bool slide;
            public float hold;
            public float trailT;
            public float mass;
            public float pop, grow;
            public float initialIntensity, intensity;
            public float seed;
        }

        [SerializeField] private Image overlayImage;
        [SerializeField] private Canvas parentCanvas;

        private readonly DropItem[] _drops = new DropItem[MaxDrops];
        private readonly Vector4[] _dropGeom = new Vector4[MaxDrops];
        private readonly Vector4[] _dropParams = new Vector4[MaxDrops];

        private Material _material;
        private bool _built;
        private bool _shaderMissingLogged;
        private int _activeCount;
        private float _lastAspect = -1f;
        private Color _lastColA;
        private Color _lastColB;
        private MatchManager _phaseMatch;
        private bool _phaseSubscribed;

        private CombatantHealth _subscribedLocalHealth;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _debugPreviewUntil;
#endif

        public int ActiveDropCount => _activeCount;
        public bool IsShowing => overlayImage != null && overlayImage.enabled;

        private void Awake()
        {
            parentCanvas = GetComponentInParent<Canvas>();
            EnsureBuilt();
        }

        private void OnEnable()
        {
            CombatantHealth.DamageApplied += HandleDamageApplied;
            SubscribePhaseChanged();
        }

        private void OnDisable()
        {
            CombatantHealth.DamageApplied -= HandleDamageApplied;
            UnsubscribePhaseChanged();
            UnsubscribeLocalHealth();
            Clear();
            if (overlayImage != null) overlayImage.enabled = false;
        }

        private void OnDestroy()
        {
            UnsubscribePhaseChanged();
            UnsubscribeLocalHealth();

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void SubscribePhaseChanged()
        {
            if (_phaseSubscribed) return;

            _phaseMatch = MatchManager.Instance != null
                ? MatchManager.Instance
                : FindAnyObjectByType<MatchManager>();
            if (_phaseMatch != null)
            {
                _phaseMatch.PhaseChanged += HandlePhaseChanged;
                _phaseSubscribed = true;
            }
        }

        private void UnsubscribePhaseChanged()
        {
            if (_phaseSubscribed && _phaseMatch != null)
            {
                _phaseMatch.PhaseChanged -= HandlePhaseChanged;
            }

            _phaseSubscribed = false;
            _phaseMatch = null;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            if (phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Finished)
            {
                Clear();
            }
        }

        private void CheckLocalHealthSubscription()
        {
            PlayerController player = PlayerController.LocalPlayer;
            CombatantHealth health = player != null ? player.Health : null;
            if (health != _subscribedLocalHealth)
            {
                UnsubscribeLocalHealth();
                if (health != null)
                {
                    _subscribedLocalHealth = health;
                    _subscribedLocalHealth.OnKilled += HandleLocalPlayerKilled;
                }
            }
        }

        private void UnsubscribeLocalHealth()
        {
            if (_subscribedLocalHealth != null)
            {
                _subscribedLocalHealth.OnKilled -= HandleLocalPlayerKilled;
                _subscribedLocalHealth = null;
            }
        }

        private void HandleLocalPlayerKilled(CombatantHealth victim, Team killerTeam)
        {
            // 本地玩家阵亡时，受击方向与屏幕四周飞溅大墨斑
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : LensInkMath.DefaultAspect;
            int enemyCh = ChannelEnemy;
            Splat(enemyCh, 0.5f, 0.5f, 0.16f, aspect, 8, 8, 2.8f);
            Splat(enemyCh, 0.28f, 0.42f, 0.11f, aspect, 6, 6, 2.4f);
            Splat(enemyCh, 0.72f, 0.46f, 0.11f, aspect, 6, 6, 2.4f);
        }

        public void EnsureBuilt()
        {
            if (_built) return;

            try
            {
                var rt = transform as RectTransform;
                if (rt == null) rt = gameObject.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                if (overlayImage == null) overlayImage = GetComponent<Image>();
                if (overlayImage == null) overlayImage = gameObject.AddComponent<Image>();

                overlayImage.sprite = UIFlatSprite.White;
                overlayImage.type = Image.Type.Simple;
                overlayImage.preserveAspect = false;
                overlayImage.color = Color.white;
                overlayImage.raycastTarget = false;
                overlayImage.maskable = false;

                Shader shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    if (!_shaderMissingLogged)
                    {
                        _shaderMissingLogged = true;
                        Debug.LogWarning($"[LensInk] 找不到 Shader {ShaderName}，镜头墨滴效果已停用。");
                    }

                    overlayImage.enabled = false;
                    return;
                }

                _material = new Material(shader)
                {
                    name = "LensInk (Runtime)",
                    hideFlags = HideFlags.DontSave
                };

                // 折射仅在 PC 桌面开启，移动端自动走半透明膜分支
                bool allowRefraction = !Application.isMobilePlatform
                                       && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3;
                _material.SetFloat(RefractionOnProp, allowRefraction ? 1f : 0f);
                _material.SetColor(WaterColProp, new Color(0.93f, 0.98f, 1.04f, 1f));

                overlayImage.material = _material;
                overlayImage.enabled = false;

                if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
                _built = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LensInk] 构建镜头墨滴视图失败：" + e.Message);
                _built = false;
            }
        }

        private void Update()
        {
            CheckLocalHealthSubscription();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_debugPreviewUntil > Time.unscaledTime && _activeCount == 0)
            {
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : LensInkMath.DefaultAspect;
                Splat(ChannelEnemy, 0.35f, 0.65f, 0.08f, aspect);
                Splat(ChannelOwn, 0.65f, 0.55f, 0.07f, aspect);
            }
#endif

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 推进模拟一帧（支持外部驱动与 EditMode 测试）。
        /// </summary>
        public void Tick(float dt)
        {
            if (!_built) EnsureBuilt();
            if (!_built || overlayImage == null || _material == null) return;

            if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled)
            {
                Clear();
                if (overlayImage.enabled) overlayImage.enabled = false;
                return;
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : LensInkMath.DefaultAspect;
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                _lastAspect = aspect;
                _material.SetFloat(AspectProp, aspect);
            }

            UpdateTeamColors();

            if (_activeCount <= 0)
            {
                // 注意：AddRawDrop 只置 _drops[i].active，不改 _activeCount（它由 SimulateDrops
                // 按本帧打包数重算）。这里必须扫一遍数组，否则"刚添加的第一滴"永远进不了
                // 模拟分支，镜头墨滴整个效果会静默失效（P18 PlayMode 用例逮住的真 bug）。
                bool anyPending = false;
                for (int i = 0; i < MaxDrops; i++)
                {
                    if (_drops[i].active) { anyPending = true; break; }
                }

                if (!anyPending)
                {
                    if (overlayImage.enabled) overlayImage.enabled = false;
                    return;
                }
            }

            SimulateDrops(dt, aspect);

            if (_activeCount > 0)
            {
                _material.SetVectorArray(DropsProp, _dropGeom);
                _material.SetVectorArray(DropParamsProp, _dropParams);
                _material.SetInt(DropCountProp, _activeCount);

                if (!overlayImage.enabled) overlayImage.enabled = true;
            }
            else
            {
                if (overlayImage.enabled) overlayImage.enabled = false;
            }
        }

        private void SimulateDrops(float dt, float aspect)
        {
            int packedIndex = 0;

            for (int i = 0; i < MaxDrops; i++)
            {
                if (!_drops[i].active) continue;

                ref DropItem p = ref _drops[i];
                p.age += dt;

                if (p.grow < 1f)
                {
                    p.grow = Mathf.Min(1f, p.grow + dt / Mathf.Max(0.01f, p.pop));
                }

                p.intensity = LensInkMath.StepIntensity(p.age, p.life, p.initialIntensity, p.kind == 3);
                if (p.intensity <= 0.01f || p.y < -0.1f)
                {
                    p.active = false;
                    continue;
                }

                if (p.kind == 0) // 主液滴，处理滑动
                {
                    if (!p.slide && LensInkMath.ShouldSlide(p.slide, p.age, p.stick, p.r))
                    {
                        p.slide = true;
                    }

                    if (p.slide)
                    {
                        p.hold -= dt;
                        if (p.hold <= 0f && UnityEngine.Random.value < dt * 0.45f)
                        {
                            p.hold = UnityEngine.Random.Range(0.06f, 0.22f);
                        }

                        float g = LensInkMath.CalculateSlideGravity(p.r, p.mass, p.hold);
                        float damp = LensInkMath.CalculateDamping(p.hold);

                        p.vy -= g * dt;
                        p.vy *= Mathf.Exp(-dt * damp);
                        p.vx = LensInkMath.CalculateSlideWobble(p.age, p.seed);

                        float dx = p.vx * dt;
                        float dy = p.vy * dt;
                        p.x += dx;
                        p.y += dy;

                        float sp = -p.vy;
                        p.trailT += Mathf.Sqrt(dx * aspect * dx * aspect + dy * dy);

                        // 下滑留下一小滴薄膜拖尾
                        if (p.trailT > p.r * 0.55f && packedIndex < MaxDrops - 1)
                        {
                            p.trailT = 0f;
                            SpawnTrail(p.channel, p.x, p.y + p.r * 0.45f, p.r * 0.55f);
                            p.r *= 0.965f;
                        }

                        p.ry = p.r * (1f + Mathf.Min(0.9f, sp * 9f));
                        p.rx = p.r * (1f - Mathf.Min(0.25f, sp * 2.5f));

                        if (p.r < 0.01f)
                        {
                            p.slide = false;
                            p.life = Mathf.Min(p.life, p.age + 0.2f);
                        }
                    }
                    else
                    {
                        p.rx = p.ry = p.r;
                    }
                }

                float gFactor = p.grow < 1f
                    ? 0.35f + 0.65f * Mathf.SmoothStep(0f, 1f, p.grow) * (1f + 0.12f * Mathf.Sin(p.grow * Mathf.PI))
                    : 1f;

                _dropGeom[packedIndex] = new Vector4(p.x, p.y, p.rx * gFactor, p.ry * gFactor);
                _dropParams[packedIndex] = new Vector4(p.intensity, p.channel, 0f, 0f);
                packedIndex++;
            }

            _activeCount = packedIndex;
        }

        private void SpawnTrail(int channel, float x, float y, float r)
        {
            int slot = FindFreeSlot();
            if (slot < 0) return;

            ref DropItem item = ref _drops[slot];
            item.active = true;
            item.kind = 3; // trail
            item.channel = channel;
            item.x = x;
            item.y = y;
            item.r = r;
            item.rx = r * 0.42f;
            item.ry = r * 0.72f;
            item.vx = 0f;
            item.vy = 0f;
            item.rot = 0f;
            item.age = 0f;
            item.life = 1.2f;
            item.stick = 99f;
            item.slide = false;
            item.hold = 0f;
            item.trailT = 0f;
            item.mass = 0.2f;
            item.pop = 0f;
            item.grow = 1f;
            item.initialIntensity = 0.5f;
            item.intensity = 0.5f;
            item.seed = UnityEngine.Random.Range(0f, 100f);
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < MaxDrops; i++)
            {
                if (!_drops[i].active) return i;
            }

            // 若满，淘汰最老且最小的 trail
            int oldestIndex = -1;
            float oldestAge = -1f;
            for (int i = 0; i < MaxDrops; i++)
            {
                if (_drops[i].kind == 3 && _drops[i].age > oldestAge)
                {
                    oldestAge = _drops[i].age;
                    oldestIndex = i;
                }
            }

            return oldestIndex >= 0 ? oldestIndex : 0;
        }

        /// <summary>
        /// 在指定屏幕归一化位置飞溅一团有机墨滴簇。
        /// 包含一个不规则主核、多条分支触手（bulb-tipped arms）以及外围飞溅微滴（satellites）。
        /// </summary>
        public void Splat(int channel, float cx, float cy, float size, float aspect = 1.777f, int arms = 6, int sats = 6, float life = 2.1f)
        {
            float rot0 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            // 1. 主核心墨珠
            AddRawDrop(0, channel, cx, cy, size * 0.92f, size * 0.92f, size * 0.92f,
                life * UnityEngine.Random.Range(0.95f, 1.15f), UnityEngine.Random.Range(0.25f, 0.55f), 1f, 1.4f, 0.08f);

            // 2. 核心凸起不规则斑
            int lumps = UnityEngine.Random.Range(3, 5);
            for (int i = 0; i < lumps; i++)
            {
                float a = rot0 + (i / (float)lumps) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.5f, 0.5f);
                float dist = size * UnityEngine.Random.Range(0.3f, 0.55f);
                float lx = cx + Mathf.Cos(a) * dist / aspect;
                float ly = cy + Mathf.Sin(a) * dist;
                float lr = size * UnityEngine.Random.Range(0.5f, 0.72f);
                AddRawDrop(0, channel, lx, ly, lr, lr, lr,
                    life * UnityEngine.Random.Range(0.75f, 1.0f), UnityEngine.Random.Range(0.5f, 1.2f), 0.6f, 1.25f, 0.08f);
            }

            // 3. 分支触角
            for (int i = 0; i < arms; i++)
            {
                float a = rot0 + ((i + UnityEngine.Random.Range(-0.3f, 0.3f)) / arms) * Mathf.PI * 2f;
                bool isLong = UnityEngine.Random.value < 0.3f;
                float armLen = size * (isLong ? UnityEngine.Random.Range(1.35f, 1.8f) : UnityEngine.Random.Range(0.85f, 1.2f));

                float nx = cx + Mathf.Cos(a) * armLen * 0.5f / aspect;
                float ny = cy + Mathf.Sin(a) * armLen * 0.5f;
                float rx = size * UnityEngine.Random.Range(0.2f, 0.3f) * (isLong ? 0.8f : 1f);
                float ry = armLen * 0.5f;
                AddRawDrop(1, channel, nx, ny, size * 0.3f, rx, ry,
                    life * UnityEngine.Random.Range(0.6f, 0.85f), 99f, 0.2f, 1.3f, 0.06f);

                float bx = cx + Mathf.Cos(a) * armLen / aspect;
                float by = cy + Mathf.Sin(a) * armLen;
                float bulbR = size * (isLong ? UnityEngine.Random.Range(0.26f, 0.34f) : UnityEngine.Random.Range(0.3f, 0.42f));
                AddRawDrop(0, channel, bx, by, bulbR, bulbR, bulbR,
                    life * UnityEngine.Random.Range(0.7f, 1.0f), UnityEngine.Random.Range(0.4f, 1.1f), 0.4f, 1.35f, 0.08f);
            }

            // 4. 外围飞溅细滴
            for (int i = 0; i < sats; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = size * UnityEngine.Random.Range(1.45f, 2.5f);
                float sr = size * UnityEngine.Random.Range(0.07f, 0.16f);
                float sx = cx + Mathf.Cos(a) * dist / aspect;
                float sy = cy + Mathf.Sin(a) * dist;
                bool stretched = UnityEngine.Random.value < 0.4f;
                AddRawDrop(2, channel, sx, sy, sr, sr * (stretched ? 0.7f : 1f), sr * (stretched ? 1.8f : 1f),
                    UnityEngine.Random.Range(0.5f, 1.3f), 99f, 0.2f, 1.3f, 0.05f);
            }
        }

        /// <summary>
        /// 添加单个微小水珠/墨滴（用于受击细散水沫、水波滴落）。
        /// </summary>
        public void AddDroplet(int channel, float x, float y, float r, float life = 1.1f, float slide = 0.25f)
        {
            AddRawDrop(0, channel, x, y, r, r, r, life, slide, 0.3f, 1.3f, 0.05f);
        }

        private void AddRawDrop(int kind, int channel, float x, float y, float r, float rx, float ry,
            float life, float stick, float mass, float intensity, float pop)
        {
            int slot = FindFreeSlot();
            if (slot < 0) return;

            ref DropItem p = ref _drops[slot];
            p.active = true;
            p.kind = kind;
            p.channel = channel;
            p.x = x;
            p.y = y;
            p.r = r;
            p.rx = rx;
            p.ry = ry;
            p.vx = 0f;
            p.vy = 0f;
            p.rot = 0f;
            p.age = 0f;
            p.life = life;
            p.stick = stick;
            p.slide = false;
            p.hold = 0f;
            p.trailT = 0f;
            p.mass = mass;
            p.pop = pop;
            p.grow = pop > 0f ? 0f : 1f;
            p.initialIntensity = intensity;
            p.intensity = intensity;
            p.seed = UnityEngine.Random.Range(0f, 100f);
        }

        /// <summary>
        /// 外部/战斗域注册镜头冲击（世界坐标点）：
        /// 根据撞击点距相机位置判断是否在视野内，并在屏幕投影对应边缘泼洒墨水。
        /// </summary>
        public void RegisterImpact(Vector3 worldPos, int team, float power)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 viewPos = cam.WorldToViewportPoint(worldPos);
            if (viewPos.z < 0.1f) return; // 相机背后

            float aspect = cam.aspect > 0.01f ? cam.aspect : LensInkMath.DefaultAspect;
            float size = Mathf.Clamp(0.045f + 0.04f * power, 0.04f, 0.14f);

            int channel = (team == (int)Team.TeamA) ? ChannelEnemy : ChannelOwn;
            PlayerController localPlayer = PlayerController.LocalPlayer;
            if (localPlayer != null && localPlayer.Health != null)
            {
                channel = (team == (int)localPlayer.Health.Team) ? ChannelOwn : ChannelEnemy;
            }

            Splat(channel, Mathf.Clamp01(viewPos.x), Mathf.Clamp01(viewPos.y), size, aspect);
        }

        private void HandleDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            if (!CombatantHealth.IsLocalPlayer(victim)) return;

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : LensInkMath.DefaultAspect;
            float k = Mathf.Clamp(damage / 70f, 0.18f, 1.2f);

            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            if (attacker != null)
            {
                Vector3 toAttacker = attacker.transform.position - victim.transform.position;
                toAttacker.y = 0f;
                if (toAttacker.sqrMagnitude > 0.01f)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 screenDir = cam.transform.InverseTransformDirection(toAttacker);
                        ang = Mathf.Atan2(screenDir.y, screenDir.x);
                    }
                }
            }

            int n = k > 0.7f ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                float aa = ang + (i != 0 ? UnityEngine.Random.Range(-0.7f, 0.7f) : UnityEngine.Random.Range(-0.18f, 0.18f));
                float size = (0.045f + 0.05f * k) * (i != 0 ? 0.6f : 1f) * UnityEngine.Random.Range(0.85f, 1.15f);
                Vector2 edge = LensInkMath.EdgePoint(aa, size * UnityEngine.Random.Range(0.2f, 0.9f), aspect);
                Splat(ChannelEnemy, edge.x, edge.y, size, aspect, 6, 5, 1.5f + k * 0.9f);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < MaxDrops; i++)
            {
                _drops[i].active = false;
            }
            _activeCount = 0;
            if (overlayImage != null && overlayImage.enabled)
            {
                overlayImage.enabled = false;
            }
        }

        private void UpdateTeamColors()
        {
            Color colA = new Color(1.00f, 0.44f, 0.06f, 1f);
            Color colB = new Color(0.15f, 0.42f, 1.00f, 1f);

            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                colA = surface.GetTeamColor(Team.TeamA);
                colB = surface.GetTeamColor(Team.TeamB);
            }

            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                colA = colA.linear;
                colB = colB.linear;
            }

            if (colA != _lastColA)
            {
                _lastColA = colA;
                _material.SetColor(LensColAProp, colA);
            }

            if (colB != _lastColB)
            {
                _lastColB = colB;
                _material.SetColor(LensColBProp, colB);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugPreview(float seconds = 3f)
        {
            _debugPreviewUntil = Mathf.Max(_debugPreviewUntil, Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }
#endif
    }
}
