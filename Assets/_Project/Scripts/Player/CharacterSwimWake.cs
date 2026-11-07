using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 潜墨游动尾迹（A6，简化版 V 形尾波）：角色在己方墨里高速潜行时，身后拉出两道斜向后掠的
    /// 水花流 + 一层低频队色墨雾。自包含组件，由 <see cref="InkCharacterMotor"/> 在初始化时挂载、
    /// 每帧通过 <see cref="Tick"/> 驱动；组件本身不读取任何移动状态，只做发射与自清理。
    ///
    /// 设计约束（性能红线）：
    /// - 固定 2 个 ParticleSystem（Spray / Mist），运行时构建一次后复用，每系统 maxParticles ≤ 32；
    /// - 全部粒子走 <c>Emit(EmitParams, 1)</c> 手动发射（Emission 模块关闭），热路径零 GC；
    /// - 材质与贴图是进程内静态共享（所有角色 1 份），不逐角色 new Material；
    /// - 空闲时关闭 ParticleSystemRenderer，0 粒子 = 0 draw call；
    /// - 暂停（timeScale = 0）不发射；OnDestroy 只销毁自身子物体，绝不 new GameObject。
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSwimWake : MonoBehaviour
    {
        private const int SprayMaxParticles = 32;
        private const int MistMaxParticles = 24;
        private const int SharedTextureSize = 48;

        // 无配置时的回退手感（生产角色都会 Bind PlayerConfigSO，这里只防裸组件）
        private const float DefaultSpreadAngle = 38f;
        private const float DefaultColorAlpha = 0.55f;
        private const float DefaultMistAlpha = 0.16f;
        private const float DefaultMaxEmissionRate = 46f;
        private const float DefaultMistEmissionRate = 5f;
        private const float DefaultMaxSpraySpeed = 3.2f;

        private static Material s_sharedMaterial;
        private static Texture2D s_sharedTexture;
        private static Gradient s_fadeGradient;
        private static AnimationCurve s_shrinkCurve;
        private static bool s_sharedResourcesFailed;

        private PlayerConfigSO _config;

        private ParticleSystem _spray;
        private ParticleSystem _mist;
        private ParticleSystemRenderer _sprayRenderer;
        private ParticleSystemRenderer _mistRenderer;
        private GameObject _sprayRoot;
        private GameObject _mistRoot;

        // 复用的 EmitParams（struct：只写字段不分配）
        private ParticleSystem.EmitParams _emit;

        private float _sprayAccumulator;
        private float _mistAccumulator;
        private float _sideSign = 1f;
        private bool _built;
        private bool _broken;

        /// <summary>粒子系统是否已成功构建（调试 / 测试可观测）。</summary>
        public bool IsBuilt => _built;

        /// <summary>上一帧是否处于发射状态（调试 / 测试可观测）。</summary>
        public bool IsEmitting { get; private set; }

        /// <summary>当前 Spray 系统存活粒子数（调试 / 测试可观测，峰值受 32 上限约束）。</summary>
        public int SprayParticleCount => _spray != null ? _spray.particleCount : 0;

        /// <summary>当前 Mist 系统存活粒子数（调试 / 测试可观测，峰值受 24 上限约束）。</summary>
        public int MistParticleCount => _mist != null ? _mist.particleCount : 0;

        /// <summary>绑定调参配置（由 InkCharacterMotor 在 Awake / Configure 时调用）。</summary>
        public void Bind(PlayerConfigSO config)
        {
            _config = config;
        }

        /// <summary>
        /// 每帧驱动入口（由 InkCharacterMotor.Update 调用）：
        /// </summary>
        /// <param name="dt">已钳制的帧间隔（秒）。</param>
        /// <param name="emitting">是否应发射：潜墨 + 非攀墙 + 速度达到 <see cref="PlayerConfigSO.wakeMinSpeed"/>。</param>
        /// <param name="planarVelocity">当前水平速度向量（m/s），用于决定后掠方向；为零时回退角色朝向。</param>
        /// <param name="speed01">速度归一化强度 0~1（阈值 → 潜墨满速），控制发射率与初速。</param>
        /// <param name="teamColor">队伍墨色（由 motor 经 InkSurfaceController.GetTeamColor 取得）。</param>
        public void Tick(float dt, bool emitting, Vector3 planarVelocity, float speed01, Color teamColor)
        {
            if (_broken) return;
            if (!_built) BuildSystems();
            if (!_built) return;

            // 暂停（timeScale = 0）与卡顿帧不发新粒子；已有粒子按各自寿命自然消散
            if (dt <= 0.00001f || Time.timeScale <= 0.00001f)
            {
                IsEmitting = false;
                UpdateRendererVisibility();
                return;
            }

            bool active = emitting && (_config == null || _config.wakeEnabled) && speed01 > 0.0001f;
            IsEmitting = active;

            if (active)
            {
                Vector3 forward = new Vector3(planarVelocity.x, 0f, planarVelocity.z);
                if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 0.0001f) right = transform.right;

                EmitSpray(dt, forward, right, Mathf.Clamp01(speed01), teamColor);
                EmitMist(dt, forward, right, Mathf.Clamp01(speed01), teamColor);

                if (_sprayRenderer != null) _sprayRenderer.enabled = true;
                if (_mistRenderer != null) _mistRenderer.enabled = true;
            }

            UpdateRendererVisibility();
        }

        /// <summary>
        /// 立即清空尾迹（复活 / 瞬移 / 回收时调用）：清掉残留粒子并关闭渲染器，
        /// 避免跨死亡与传送点位拉出一条「幽灵尾迹」。
        /// </summary>
        public void ResetWake()
        {
            _sprayAccumulator = 0f;
            _mistAccumulator = 0f;
            _sideSign = 1f;
            IsEmitting = false;

            if (_spray != null) _spray.Clear(true);
            if (_mist != null) _mist.Clear(true);
            if (_sprayRenderer != null) _sprayRenderer.enabled = false;
            if (_mistRenderer != null) _mistRenderer.enabled = false;
        }

        private void OnDisable()
        {
            // 对象池回收 / 角色隐藏：彻底清理，不留下仍在模拟的粒子
            ResetWake();
        }

        private void OnDestroy()
        {
            // 只销毁自身子物体与释放引用。绝不在此 new GameObject / AddComponent
            // （否则触发 "Did you spawn new GameObjects from OnDestroy" 泄漏报错）。
            if (_spray != null) _spray.Clear(true);
            if (_mist != null) _mist.Clear(true);

            DestroyRuntimeObject(_sprayRoot);
            DestroyRuntimeObject(_mistRoot);

            _sprayRoot = null;
            _mistRoot = null;
            _spray = null;
            _mist = null;
            _sprayRenderer = null;
            _mistRenderer = null;
            _built = false;
        }

        /// <summary>运行期用 Destroy、编辑期用 DestroyImmediate（与 VfxPool 同一约定）。</summary>
        private static void DestroyRuntimeObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        // ==================== 粒子发射 ====================

        /// <summary>
        /// 两道斜向后掠的水花流（±wakeSpreadAngle）：发射率与初速随速度线性增强。
        /// 单系统交替左右侧发射，保证 V 形两侧密度对称，同时只占 1 个 ParticleSystem。
        /// </summary>
        private void EmitSpray(float dt, Vector3 forward, Vector3 right, float speed01, Color teamColor)
        {
            float maxRate = _config != null ? Mathf.Max(0f, _config.wakeMaxEmissionRate) : DefaultMaxEmissionRate;
            if (maxRate <= 0.01f) return;

            float rate = maxRate * Mathf.Lerp(0.35f, 1f, speed01);
            _sprayAccumulator += rate * dt;
            int count = (int)_sprayAccumulator;
            if (count <= 0) return;

            _sprayAccumulator -= count;
            // 卡顿帧保护：单帧最多补 10 颗，防止长帧后一帧内爆量
            if (count > 10) count = 10;

            float spreadDeg = _config != null ? Mathf.Clamp(_config.wakeSpreadAngle, 10f, 60f) : DefaultSpreadAngle;
            float spreadRad = spreadDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(spreadRad);
            float sin = Mathf.Sin(spreadRad);

            float maxSpraySpeed = _config != null ? Mathf.Max(0.5f, _config.wakeMaxSpraySpeed) : DefaultMaxSpraySpeed;
            float baseSpeed = Mathf.Lerp(0.9f, maxSpraySpeed, speed01);
            float baseSize = Mathf.Lerp(0.05f, 0.105f, speed01);
            float alpha = Mathf.Clamp01(_config != null ? _config.wakeColorAlpha : DefaultColorAlpha)
                          * Mathf.Lerp(0.7f, 1f, speed01);
            Color color = new Color(teamColor.r, teamColor.g, teamColor.b, alpha);

            Vector3 feet = transform.position + Vector3.up * 0.06f;

            for (int i = 0; i < count; i++)
            {
                _sideSign = -_sideSign;
                float side = _sideSign;

                // 后掠方向：-forward 与 ±right 按 spreadAngle 合成，再加少量横向随机抖动
                Vector3 dir = (-forward * cos + right * (side * sin)).normalized;
                float jitterDeg = Random.Range(-7f, 7f);
                dir = Quaternion.AngleAxis(jitterDeg, Vector3.up) * dir;

                Vector3 pos = feet
                              + right * (side * Random.Range(0.07f, 0.14f))
                              + forward * Random.Range(-0.10f, 0.02f)
                              + Vector3.up * Random.Range(0f, 0.04f);

                _emit.position = pos;
                _emit.velocity = dir * (baseSpeed * Random.Range(0.75f, 1.2f))
                                 + Vector3.up * (Random.Range(0.35f, 0.85f) * Mathf.Lerp(0.6f, 1f, speed01));
                _emit.startLifetime = Random.Range(0.24f, 0.40f);
                _emit.startSize = baseSize * Random.Range(0.8f, 1.25f);
                _emit.startColor = color;
                _emit.rotation = Random.Range(0f, 360f);
                _emit.angularVelocity = Random.Range(-90f, 90f);

                _spray.Emit(_emit, 1);
            }
        }

        /// <summary>
        /// 低频队色墨雾：脚部缓缓升起的低透明度雾团，负责把两道水花「黏」成一个整体尾波。
        /// </summary>
        private void EmitMist(float dt, Vector3 forward, Vector3 right, float speed01, Color teamColor)
        {
            float maxRate = _config != null ? Mathf.Max(0f, _config.wakeMistEmissionRate) : DefaultMistEmissionRate;
            if (maxRate <= 0.01f) return;

            float rate = maxRate * Mathf.Lerp(0.4f, 1f, speed01);
            _mistAccumulator += rate * dt;
            int count = (int)_mistAccumulator;
            if (count <= 0) return;

            _mistAccumulator -= count;
            if (count > 4) count = 4;

            float alpha = Mathf.Clamp01(_config != null ? _config.wakeMistAlpha : DefaultMistAlpha)
                          * Mathf.Lerp(0.6f, 1f, speed01);
            Color color = new Color(teamColor.r, teamColor.g, teamColor.b, alpha);
            float baseSize = Mathf.Lerp(0.16f, 0.30f, speed01);
            Vector3 feet = transform.position + Vector3.up * 0.05f;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = feet
                              + right * Random.Range(-0.18f, 0.18f)
                              + forward * Random.Range(-0.16f, 0.04f);

                _emit.position = pos;
                _emit.velocity = -forward * (Random.Range(0.08f, 0.22f) * Mathf.Lerp(0.4f, 1f, speed01))
                                 + Vector3.up * Random.Range(0.10f, 0.28f);
                _emit.startLifetime = Random.Range(0.55f, 0.90f);
                _emit.startSize = baseSize * Random.Range(0.8f, 1.25f);
                _emit.startColor = color;
                _emit.rotation = Random.Range(0f, 360f);
                _emit.angularVelocity = Random.Range(-40f, 40f);

                _mist.Emit(_emit, 1);
            }
        }

        // ==================== 构建与资源 ====================

        /// <summary>
        /// 构建两个 ParticleSystem（只做一次）。装饰性系统：任何异常都只 LogWarning 并停用，
        /// 绝不能因为尾迹构建失败让整局游戏起不来，也不能污染 PlayMode 的 log-error 门禁。
        /// </summary>
        private void BuildSystems()
        {
            if (_built || _broken) return;

            try
            {
                EnsureSharedResources();
                if (s_sharedMaterial == null)
                {
                    _broken = true;
                    Debug.LogWarning("[CharacterSwimWake] 未找到可用粒子着色器，潜墨尾迹已禁用。");
                    return;
                }

                _spray = CreateSystem(
                    "SwimWakeSpray", SprayMaxParticles,
                    lifeMin: 0.24f, lifeMax: 0.40f,
                    sizeMin: 0.05f, sizeMax: 0.11f,
                    gravity: 0.35f,
                    root: out _sprayRoot, renderer: out _sprayRenderer);

                _mist = CreateSystem(
                    "SwimWakeMist", MistMaxParticles,
                    lifeMin: 0.55f, lifeMax: 0.90f,
                    sizeMin: 0.16f, sizeMax: 0.30f,
                    gravity: -0.05f,
                    root: out _mistRoot, renderer: out _mistRenderer);

                _built = true;
            }
            catch (System.Exception e)
            {
                _broken = true;
                Debug.LogWarning($"[CharacterSwimWake] 潜墨尾迹构建失败，已禁用：{e.Message}");
            }
        }

        private ParticleSystem CreateSystem(
            string systemName,
            int maxParticles,
            float lifeMin,
            float lifeMax,
            float sizeMin,
            float sizeMax,
            float gravity,
            out GameObject root,
            out ParticleSystemRenderer renderer)
        {
            root = new GameObject(systemName);
            root.transform.SetParent(transform, false);

            var ps = root.AddComponent<ParticleSystem>();
            // 新建的 ParticleSystem 会自动开始播放：必须先彻底停止再改 duration 等参数，
            // 否则 Unity 持续报 "Setting the duration while system is still playing"。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = 0f;                       // 初速全部由 EmitParams.velocity 显式给出
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = Color.white;
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = false;               // 暂停时冻结（跟随 Time.timeScale）

            var emission = ps.emission;
            emission.enabled = false;                   // 手动 Emit，Emission 模块保持关闭

            var shape = ps.shape;
            shape.enabled = false;                      // 发射位置由 EmitParams.position 决定

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(GetFadeGradient());

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, GetShrinkCurve());

            // 速度上限 + 轻微阻尼（仅在超过 limit 后生效）：防止长帧/叠加把水花甩到远处
            var limitVelocity = ps.limitVelocityOverLifetime;
            limitVelocity.enabled = true;
            limitVelocity.limit = 4.5f;
            limitVelocity.dampen = 0.12f;
            limitVelocity.space = ParticleSystemSimulationSpace.World;

            renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = s_sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;
            renderer.enabled = false;                   // 有存活粒子才开渲染器，空闲 0 draw call

            ps.Play();                                  // Emission 关闭 + Play：只保证手动 Emit 的粒子会被模拟
            return ps;
        }

        private void UpdateRendererVisibility()
        {
            if (_sprayRenderer != null)
                _sprayRenderer.enabled = _spray != null && _spray.particleCount > 0;
            if (_mistRenderer != null)
                _mistRenderer.enabled = _mist != null && _mist.particleCount > 0;
        }

        /// <summary>
        /// 进程内共享的白色软圆盘贴图 + 粒子材质（所有角色 1 份，避免逐角色 new Material / 贴图）。
        /// 着色器优先 P1/VfxAdditive（在 Resources 下，独立包不会被剔除）；强度刻意压低，避免加法过曝。
        /// </summary>
        private static void EnsureSharedResources()
        {
            if (s_sharedMaterial != null) return;
            if (s_sharedResourcesFailed) return;

            Shader shader = Shader.Find("P1/VfxAdditive");
            bool additive = shader != null;
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                s_sharedResourcesFailed = true;
                return;
            }

            s_sharedTexture = BuildSoftDiscTexture(SharedTextureSize);

            s_sharedMaterial = new Material(shader)
            {
                name = "SwimWakeParticleMat",
                hideFlags = HideFlags.DontSave
            };
            if (s_sharedMaterial.HasProperty("_MainTex")) s_sharedMaterial.SetTexture("_MainTex", s_sharedTexture);
            if (s_sharedMaterial.HasProperty("_Color")) s_sharedMaterial.SetColor("_Color", Color.white);
            if (s_sharedMaterial.HasProperty("_Intensity"))
                s_sharedMaterial.SetFloat("_Intensity", additive ? 0.62f : 1f);
        }

        private static Gradient GetFadeGradient()
        {
            if (s_fadeGradient != null) return s_fadeGradient;

            s_fadeGradient = new Gradient();
            s_fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.55f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            return s_fadeGradient;
        }

        private static AnimationCurve GetShrinkCurve()
        {
            if (s_shrinkCurve != null) return s_shrinkCurve;
            s_shrinkCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.45f);
            return s_shrinkCurve;
        }

        /// <summary>程序化软边圆盘贴图（中心实、边缘透明），水花与墨雾共用。</summary>
        private static Texture2D BuildSoftDiscTexture(int size)
        {
            size = Mathf.Clamp(size, 16, 128);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "SwimWakeSoftDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
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
                    a = a * a * (3f - 2f * a); // smoothstep 软边
                    a *= a;                    // 中心更亮，外圈更快消散
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
