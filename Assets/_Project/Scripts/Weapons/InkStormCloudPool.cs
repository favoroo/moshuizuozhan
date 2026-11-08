using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace P1.Weapons
{
    /// <summary>
    /// 小地图 / 调试用的风暴云快照：位置、伤害半径、剩余寿命比例、所属队伍。
    /// </summary>
    public struct StormCloudInfo
    {
        /// <summary>展开云的云心世界坐标；仍在飞行时为发生器当前位置。</summary>
        public Vector3 Position;
        /// <summary>降雨 / 伤害半径（米）。</summary>
        public float Radius;
        /// <summary>剩余寿命 0~1（飞行中恒为 1）。</summary>
        public float Remaining01;
        /// <summary>false = 发生器仍在飞行，云尚未展开。</summary>
        public bool Spawned;
        /// <summary>投掷方的队伍（小地图按队色区分敌我风暴）。</summary>
        public Team Team;
    }

    /// <summary>
    /// Ink Tempest（墨水风暴云）发生器与云层对象池（P3-B，P41 表现重做）。
    /// 流程：发生器抛物线飞向目标点 → 落点上空生成盘旋墨雨云 → 沿投掷方向以 1.1m/s 漂移 6.5s，
    /// 持续向下雨点涂地，并对范围内敌方施加每秒 34 点 DOT。
    ///
    /// P41 对齐 Inkwave _spawnCloud / _updateClouds：
    /// - 云体 15 puff 双色调积云（深色墨腹 + 浅色云顶 + 顶冠），云体投影，剪影覆盖住伤害圈；
    /// - 顶部 puff 周期性雷光闪烁（stormFlash）；落地瞬间冲击环 + 大墨花爆发（stormStart）；
    /// - 双层雨：密集短雨滴 + 细长亮雨丝（跨屏可读性）；溅射/涟漪打在随机圆盘点而非单点；
    /// - 云底 2 层地面圈：出手即显示落点预警，展开后随云漂移、随消散淡出；
    /// - 游戏半径（涂地 / 伤害）全程恒定，视觉 grow/fade 只缩放表现。
    /// 场景无需预置：<see cref="EnsureInstance"/> 首次释放时自动创建，避免改动 ProjectScaffolder 的主场景。
    /// </summary>
    public class InkStormCloudPool : MonoBehaviour
    {
        public static InkStormCloudPool Instance { get; private set; }

        // Puff 布局（对齐 Inkwave weapons.js _spawnCloud）：1 核心 + 8 环 + 5 顶 + 1 顶冠
        private const int CorePuffCount = 1;
        private const int RingPuffCount = 8;
        private const int TopPuffCount = 5;
        private const int CrownPuffCount = 1;
        private const int PuffCount = CorePuffCount + RingPuffCount + TopPuffCount + CrownPuffCount;
        private const int MaxRainDropsPerFrame = 24;
        /// <summary>溅射/涟漪采样的近期命中点环形缓冲大小。</summary>
        private const int RecentHitCapacity = 4;
        /// <summary>雷光闪烁时长（秒）。</summary>
        private const float FlashDuration = 0.12f;
        /// <summary>顶部/顶冠 puff 在数组中的起始下标（雷光只闪上半身）。</summary>
        private const int FlashFirstIndex = CorePuffCount + RingPuffCount;

        /// <summary>首次释放风暴云时按需创建对象池单例。</summary>
        public static InkStormCloudPool EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("InkStormCloudPool");
                Instance = go.AddComponent<InkStormCloudPool>();
            }
            return Instance;
        }

        private sealed class StormNode
        {
            public Transform Root;
            public Transform Generator;
            public MeshRenderer GeneratorRenderer;
            public Transform CloudRoot;
            public MeshRenderer[] Puffs;
            public float[] PuffTints;
            public float[] PuffBaseY;
            public float[] PuffBobs;

            // 云底地面圈（盘 + 亮边）：出手即预警，展开后随云漂移
            public Transform RingRoot;
            public MeshRenderer RingDiscRenderer;
            public MeshRenderer RingEdgeRenderer;
            public float RingGroundY;

            // 雷光闪烁（stormFlash）：随机顶部 puff 短暂提亮
            public float FlashTimer;
            public float FlashTimeLeft;
            public int FlashPuffIndex;

            public bool CloudSpawned;
            public Team Team;
            public Color TeamColor;
            public Color PuffColor;
            /// <summary>雨丝层颜色：墨色向白混 35%，跨屏可读（Inkwave rainSheet）。</summary>
            public Color StreakColor;
            /// <summary>释放大招的角色（P6-A 打击标记/击杀 Hitstop 归属）。</summary>
            public CombatantHealth Attacker;

            public Vector3 Start;
            public Vector3 Target;
            public Vector3 DriftDir;

            public float FlightElapsed;
            public float FlightDuration;
            public float ArcHeight;

            public float CloudElapsed;
            public float Duration;
            public float Radius;
            public float Dps;
            public float DriftSpeed;
            public float CloudHeight;
            public float RainInterval;
            public float RainRadiusMin;
            public float RainRadiusMax;
            public float DamageTickInterval;
            public float GrowTime;
            public float FadeTime;
            public float RainTimer;
            public float StreakTimer;
            public float DamageTimer;
            public float VfxTimer;
            public float SplashTimer;
            public float RippleTimer;
            public Vector3[] RecentHits;
            public int RecentHitCount;
        }

        private readonly List<StormNode> _active = new List<StormNode>(4);
        private readonly Queue<StormNode> _pool = new Queue<StormNode>(4);

        private Material _sharedMat;
        private MaterialPropertyBlock _propBlock;
        private int _createdCount;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        /// <summary>当前存活（飞行中或降雨中）的风暴云节点数量，调试/验收可观测。</summary>
        public int ActiveStormCount => _active.Count;

        /// <summary>取第一朵已展开云层的世界坐标（用于漂移验收）；没有已展开的云时返回 false。</summary>
        public bool TryGetActiveCloudPosition(out Vector3 position)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                StormNode node = _active[i];
                if (!node.CloudSpawned)
                    continue;

                position = node.CloudRoot.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 取当前全部风暴云快照（含飞行中的发生器），写入 results 并返回条数。
        /// 小地图倒计时盘 / 调试观测用；results 可复用列表，调用前无需清空。
        /// </summary>
        public int TryGetActiveClouds(List<StormCloudInfo> results)
        {
            if (results == null)
                return 0;

            results.Clear();
            for (int i = 0; i < _active.Count; i++)
            {
                StormNode node = _active[i];
                bool spawned = node.CloudSpawned;
                results.Add(new StormCloudInfo
                {
                    Position = spawned ? node.CloudRoot.position : node.Generator.position,
                    Radius = node.Radius,
                    Remaining01 = spawned
                        ? Mathf.Clamp01((node.Duration - node.CloudElapsed) / Mathf.Max(0.01f, node.Duration))
                        : 1f,
                    Spawned = spawned,
                    Team = node.Team
                });
            }

            return results.Count;
        }

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();
            InitializeResources();

            // 预热 6：每个风暴云由 15 puff + 地面圈组成，运行时新建会形成明显尖刺
            for (int i = 0; i < 6; i++)
            {
                _pool.Enqueue(CreateNode(_createdCount++));
            }
        }

        private void OnDestroy()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.StormRain);
            }

            if (Instance == this)
                Instance = null;

            if (_sharedMat != null)
            {
                Destroy(_sharedMat);
                _sharedMat = null;
            }
        }

        private void InitializeResources()
        {
            Shader sh = Shader.Find("P1/InkBody");
            if (sh == null)
            {
                Debug.LogWarning("[InkStormCloudPool] 未找到 Shader 'P1/InkBody'，回退到 Unlit 材质。");
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null)
                    sh = Shader.Find("Sprites/Default");
            }
            _sharedMat = new Material(sh) { name = "InkStormCloudMat" };
            _sharedMat.enableInstancing = true;
        }

        /// <summary>
        /// 投掷风暴云发生器。startPos 为角色手部，targetGroundPos 为地面瞄准点（会被钳制到最大投掷距离并贴地）。
        /// </summary>
        public void ThrowStorm(
            Vector3 startPos,
            Vector3 targetGroundPos,
            Team team,
            WeaponConfigSO config,
            CombatantHealth attacker = null)
        {
            StormNode node = _pool.Count > 0 ? _pool.Dequeue() : CreateNode(_createdCount++);

            Vector3 flatDelta = targetGroundPos - startPos;
            flatDelta.y = 0f;
            float maxDistance = config != null ? config.stormThrowDistance : 9f;
            if (flatDelta.magnitude > maxDistance && flatDelta.sqrMagnitude > 0.0001f)
            {
                targetGroundPos = startPos + flatDelta.normalized * maxDistance;
            }

            // 贴地：风暴云必须落在真实地面上，否则雨点射线会全部打空。
            Vector3 snapOrigin = new Vector3(targetGroundPos.x, startPos.y + 8f, targetGroundPos.z);
            if (Physics.Raycast(snapOrigin, Vector3.down, out RaycastHit groundHit, 24f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                targetGroundPos.y = groundHit.point.y;
            }
            else
            {
                targetGroundPos.y = startPos.y - 1f;
            }

            // 投掷瞬间的风压（旧版从投出到云层落地之间完全静音）
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayStormWind();

            Vector3 drift = new Vector3(flatDelta.x, 0f, flatDelta.z);
            if (drift.sqrMagnitude < 0.0001f)
            {
                drift = new Vector3(startPos.x, 0f, startPos.z) - new Vector3(targetGroundPos.x, 0f, targetGroundPos.z);
            }
            if (drift.sqrMagnitude < 0.0001f)
            {
                drift = Vector3.forward;
            }

            // 队色传原始 sRGB：MaterialPropertyBlock 会自动转 Linear，手动 .linear 会二次转换发深红。
            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(team)
                : (team == Team.TeamA ? new Color(1f, 0.44f, 0.06f) : new Color(0.15f, 0.42f, 1f));

            node.CloudSpawned = false;
            node.Team = team;
            node.Attacker = attacker;
            node.TeamColor = teamColor;
            node.PuffColor = Color.Lerp(teamColor, Color.white, 0.30f);
            node.StreakColor = Color.Lerp(node.PuffColor, Color.white, 0.35f);
            node.Start = startPos;
            node.Target = targetGroundPos;
            node.DriftDir = drift.normalized;
            node.FlightElapsed = 0f;
            node.FlightDuration = config != null ? Mathf.Max(0.05f, config.stormThrowFlightDuration) : 0.45f;
            node.ArcHeight = config != null ? config.stormThrowArcHeight : 3.2f;

            node.Duration = config != null ? Mathf.Max(0.5f, config.stormDuration) : 6.5f;
            node.Radius = config != null ? Mathf.Max(0.5f, config.stormRadius) : 3.4f;
            node.Dps = config != null ? Mathf.Max(0f, config.stormDps) : 34f;
            node.DriftSpeed = config != null ? Mathf.Max(0f, config.stormDriftSpeed) : 1.1f;
            node.CloudHeight = config != null ? Mathf.Max(0.5f, config.stormCloudHeight) : 4.5f;
            node.RainInterval = config != null ? Mathf.Max(0.012f, config.stormRainInterval) : 0.045f;
            node.RainRadiusMin = config != null ? Mathf.Max(0.05f, config.stormRainSplatRadiusMin) : 0.45f;
            node.RainRadiusMax = config != null ? Mathf.Max(node.RainRadiusMin, config.stormRainSplatRadiusMax) : 0.8f;
            node.DamageTickInterval = config != null ? Mathf.Max(0.05f, config.stormDamageTickInterval) : 0.5f;
            node.GrowTime = config != null ? Mathf.Max(0.05f, config.stormGrowTime) : 0.5f;
            node.FadeTime = config != null ? Mathf.Max(0.05f, config.stormFadeTime) : 0.6f;
            node.RainTimer = 0f;
            node.StreakTimer = 0f;
            node.DamageTimer = 0f;
            node.VfxTimer = 0f;
            node.SplashTimer = 0f;
            node.RippleTimer = 0f;
            node.RecentHitCount = 0;
            node.FlashTimer = Random.Range(0.4f, 1.0f);
            node.FlashTimeLeft = 0f;
            node.FlashPuffIndex = -1;
            node.RingGroundY = targetGroundPos.y;

            node.Generator.gameObject.SetActive(true);
            node.Generator.position = startPos;
            node.Generator.localScale = Vector3.one * 0.42f;
            SetColor(node.GeneratorRenderer, Color.Lerp(teamColor, Color.white, 0.35f));

            node.CloudRoot.gameObject.SetActive(false);

            // 落点预警圈：出手即显示（飞行期快脉冲），展开后转为云底范围圈
            if (node.RingRoot != null)
            {
                node.RingRoot.position = new Vector3(targetGroundPos.x, targetGroundPos.y + 0.06f, targetGroundPos.z);
                node.RingRoot.localScale = new Vector3(node.Radius, 1f, node.Radius);
                SetRingColor(node.RingDiscRenderer, teamColor, 0.26f);
                SetRingColor(node.RingEdgeRenderer, teamColor, 0.8f);
                node.RingRoot.gameObject.SetActive(true);
            }

            _active.Add(node);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Storm] ThrowStorm added, active={_active.Count}, dur={node.Duration:F2}, target={targetGroundPos}");
#endif
        }

        /// <summary>比赛重开/清场时回收全部风暴云与飞行中的发生器。</summary>
        public void ClearAllClouds()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Storm] ClearAllClouds called, active={_active.Count}");
#endif
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StormNode node = _active[i];
                Recycle(node);
                _pool.Enqueue(node);
            }
            _active.Clear();

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.StopLoop(LoopSfxKind.StormRain);
            }
        }

        private void Update()
        {
            if (_active.Count == 0)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            float loudestFade = 0f;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StormNode node = _active[i];
                if (!node.CloudSpawned)
                {
                    UpdateFlight(node, dt);
                }
                else
                {
                    UpdateCloud(node, dt);
                    // 多朵云共享一条雨声通道：取最响的一朵（寿命比例最大）驱动循环音量
                    float fade = Mathf.Clamp01((node.Duration - node.CloudElapsed) / node.FadeTime);
                    if (fade > loudestFade)
                        loudestFade = fade;
                }

                if (node.CloudSpawned && node.CloudElapsed >= node.Duration)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Storm] Recycle natural end, elapsed={node.CloudElapsed:F2}, dur={node.Duration:F2}");
#endif
                    Recycle(node);
                    _pool.Enqueue(node);
                    _active.RemoveAt(i);

                    // 检查场上是否仍有展开降雨中的风暴云
                    bool hasActiveCloud = false;
                    for (int k = 0; k < _active.Count; k++)
                    {
                        if (_active[k].CloudSpawned)
                        {
                            hasActiveCloud = true;
                            break;
                        }
                    }
                    if (!hasActiveCloud && AudioManager.Instance != null)
                    {
                        AudioManager.Instance.StopLoop(LoopSfxKind.StormRain);
                    }
                }
            }

            // 雨声随消散淡出（Inkwave: loop volume 跟 fade 系数）；0 以下不再写，交给 StopLoop 复位
            if (loudestFade > 0f && AudioManager.Instance != null)
            {
                AudioManager.Instance.SetLoopVolume(LoopSfxKind.StormRain, Mathf.Clamp01(loudestFade * 0.85f + 0.15f));
            }
        }

        private void UpdateFlight(StormNode node, float dt)
        {
            node.FlightElapsed += dt;
            float t = Mathf.Clamp01(node.FlightElapsed / Mathf.Max(0.05f, node.FlightDuration));

            Vector3 pos = InkBombTrajectoryUtility.EvaluateArcPoint(node.Start, node.Target + Vector3.up * 0.35f, node.ArcHeight, t);
            node.Generator.position = pos;
            node.Generator.Rotate(new Vector3(320f, 240f, 0f) * dt, Space.Self);

            // 飞行期落点圈快脉冲（8Hz 硬告警节奏）
            UpdateRingPulse(node, 1f, 8f);

            if (t >= 1f)
            {
                SpawnCloud(node);
            }
        }

        private void SpawnCloud(StormNode node)
        {
            node.CloudSpawned = true;
            node.Generator.gameObject.SetActive(false);

            node.CloudRoot.gameObject.SetActive(true);
            node.CloudRoot.position = new Vector3(node.Target.x, node.Target.y + node.CloudHeight, node.Target.z);
            node.CloudRoot.localScale = Vector3.one * 0.05f;

            for (int i = 0; i < node.Puffs.Length; i++)
            {
                SetColor(node.Puffs[i], Color.Lerp(node.TeamColor, Color.white, node.PuffTints[i]));
            }

            node.CloudElapsed = 0f;
            node.RainTimer = 0f;
            node.StreakTimer = 0f;
            node.DamageTimer = 0f;
            node.VfxTimer = 0f;
            node.SplashTimer = 0f;
            node.RippleTimer = 0f;
            node.RecentHitCount = 0;
            node.RingGroundY = node.Target.y;

            if (VfxPool.Instance != null)
            {
                // 落地爆发（Inkwave stormStart）：地面冲击环 + 大墨花 + 涟漪
                VfxPool.Instance.SpawnShockwaveRing(
                    new Vector3(node.Target.x, node.Target.y + 0.1f, node.Target.z),
                    node.PuffColor, node.Radius * 0.3f, node.Radius * 1.25f, 0.4f);
                VfxPool.Instance.SpawnInkSplat(node.Target, node.PuffColor, 1.3f, 12);
            }

            if (InkSurfaceController.Instance != null)
            {
                InkSurfaceController.Instance.TriggerRipple(node.Target, 0.08f, 0.6f, 3.4f, 0.9f);
            }

            if (AudioManager.Instance != null)
            {
                // 雷声按云层落点衰减；雨声循环是全局环境层，不衰减（它是氛围不是事件）
                AudioManager.Instance.PlayStormThunder(node.CloudRoot.position);
                AudioManager.Instance.PlayLoop(LoopSfxKind.StormRain);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Storm] SpawnCloud at {node.CloudRoot.position}, dur={node.Duration:F2}");
#endif
        }

        private void UpdateCloud(StormNode node, float dt)
        {
            node.CloudElapsed += dt;

            float grow = Mathf.Clamp01(node.CloudElapsed / node.GrowTime);
            float fade = Mathf.Clamp01((node.Duration - node.CloudElapsed) / node.FadeTime);
            float growEase = 1f - Mathf.Pow(1f - grow, 3f);
            float scale = (0.3f + 0.7f * growEase) * (0.2f + 0.8f * fade);
            node.CloudRoot.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            Vector3 pos = SpecialCombat.StormDriftStep(node.CloudRoot.position, node.DriftDir, node.DriftSpeed, dt);
            pos = ClampInsideMap(pos, ref node.DriftDir);
            node.CloudRoot.position = pos;

            for (int i = 0; i < node.Puffs.Length; i++)
            {
                Transform puff = node.Puffs[i].transform;
                Vector3 local = puff.localPosition;
                local.y = node.PuffBaseY[i] + Mathf.Sin(Time.time * 1.7f + node.PuffBobs[i]) * 0.06f;
                puff.localPosition = local;
            }

            UpdateThunderFlash(node, dt);

            // 云底范围圈跟随漂移：XZ 贴云心，Y 贴云下地面（云飘过集装箱顶时圈跟着爬上箱顶）
            if (node.RingRoot != null && node.RingRoot.gameObject.activeSelf)
            {
                Vector3 ringOrigin = new Vector3(pos.x, pos.y + 2f, pos.z);
                if (Physics.Raycast(ringOrigin, Vector3.down, out RaycastHit ringHit, node.CloudHeight + 8f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                {
                    node.RingGroundY = ringHit.point.y;
                }
                node.RingRoot.position = new Vector3(pos.x, node.RingGroundY + 0.06f, pos.z);
                UpdateRingPulse(node, fade, 2.5f);
            }

            // 云层收尾的最后 0.3s 停止降雨（视觉上仍在收缩消散）
            if (node.CloudElapsed < node.Duration - 0.3f)
            {
                UpdateRainPaint(node, dt);
                // 终场哨响后雨点照常落（表现不中断），但不再结算 DOT 伤害。
                if (MatchManager.CombatSettlementAllowed)
                {
                    UpdateRainDamage(node, dt);
                }
            }
        }

        /// <summary>雷光闪烁：随机让一个顶部/顶冠 puff 提亮 FlashDuration 秒后还原（零新增渲染系统）。</summary>
        private void UpdateThunderFlash(StormNode node, float dt)
        {
            if (node.FlashPuffIndex >= 0)
            {
                node.FlashTimeLeft -= dt;
                if (node.FlashTimeLeft <= 0f)
                {
                    SetColor(node.Puffs[node.FlashPuffIndex],
                        Color.Lerp(node.TeamColor, Color.white, node.PuffTints[node.FlashPuffIndex]));
                    node.FlashPuffIndex = -1;
                }
                return;
            }

            node.FlashTimer -= dt;
            if (node.FlashTimer > 0f)
                return;

            node.FlashTimer = Random.Range(0.6f, 1.4f);
            node.FlashTimeLeft = FlashDuration;
            node.FlashPuffIndex = Random.Range(FlashFirstIndex, node.Puffs.Length);
            SetColor(node.Puffs[node.FlashPuffIndex], Color.Lerp(node.TeamColor, Color.white, 0.82f));
        }

        /// <summary>云底圈呼吸脉冲：pulseHz 控制节奏，fadeAlpha 随消散淡出。</summary>
        private void UpdateRingPulse(StormNode node, float fadeAlpha, float pulseHz)
        {
            if (node.RingDiscRenderer == null || node.RingEdgeRenderer == null)
                return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f);
            SetRingColor(node.RingDiscRenderer, node.TeamColor, (0.30f + 0.12f * pulse) * fadeAlpha);
            SetRingColor(node.RingEdgeRenderer, node.TeamColor, (0.72f + 0.28f * pulse) * fadeAlpha);
        }

        private void UpdateRainPaint(StormNode node, float dt)
        {
            Vector3 cloudPos = node.CloudRoot.position;
            node.RainTimer -= dt;

            int drops = 0;
            // 终场哨响后：雨点仍在下（表现），但不再涂地、不再登记涂地统计。
            bool settleAllowed = MatchManager.CombatSettlementAllowed;

            while (node.RainTimer <= 0f && drops < MaxRainDropsPerFrame)
            {
                node.RainTimer += node.RainInterval;
                drops++;

                // 游戏半径恒定（Inkwave 行为）：grow/fade 只缩放视觉，涂地/伤害圈全程一致
                float angle = Random.value * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Random.value) * node.Radius;
                Vector3 from = new Vector3(
                    cloudPos.x + Mathf.Cos(angle) * distance,
                    cloudPos.y - 0.6f,
                    cloudPos.z + Mathf.Sin(angle) * distance);

                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, node.CloudHeight + 8f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                    continue;

                RecordRecentHit(node, hit.point);

                if (settleAllowed && InkSurfaceController.Instance != null)
                {
                    float paintRadius = Random.Range(node.RainRadiusMin, node.RainRadiusMax);
                    // 风暴云涂地计入涂地覆盖率，但不返还大招能量：雨点数量太多，返还会造成「一云接一云」的自循环。
                    // 雨点只铺平滑主体：一秒十几滴雨，每滴都刷涟漪会把 8 槽缓冲彻底冲散。
                    int changed = InkSurfaceController.Instance.PaintCircle(hit.point, paintRadius, node.Team, false, false);
                    if (changed > 0 && node.Attacker != null)
                    {
                        MatchStatsService.RegisterTurf(node.Attacker, changed * InkSurfaceController.Instance.CellArea);
                    }
                }
            }

            if (VfxPool.Instance == null)
                return;

            // 溅射/涟漪打在随机圆盘点（近期命中缓冲里挑），不再只钉在最后一个命中点
            if (node.RecentHitCount > 0)
            {
                node.SplashTimer -= dt;
                if (node.SplashTimer <= 0f)
                {
                    node.SplashTimer = 0.14f;
                    VfxPool.Instance.SpawnInkSplat(PickRecentHit(node), node.PuffColor, 0.42f, 3);
                }

                node.RippleTimer -= dt;
                if (node.RippleTimer <= 0f && InkSurfaceController.Instance != null)
                {
                    node.RippleTimer = 0.1f;
                    InkSurfaceController.Instance.TriggerRipple(PickRecentHit(node), 0.04f, 0.35f, 3.2f, 0.7f);
                }
            }

            // 双层雨（Inkwave：密雨滴 ~98/s + 长亮雨丝 ~37/s，跨屏可读）：
            // 密雨层 —— 快速短雨滴，从云底洒向整个伤害盘；视觉半径随 grow/fade 缩放。
            node.VfxTimer -= dt;
            if (node.VfxTimer <= 0f)
            {
                node.VfxTimer = 0.1f;
                float scale = Mathf.Max(0.25f, node.CloudRoot.localScale.x);
                VfxPool.Instance.SpawnRainDrops(
                    cloudPos + Vector3.down * 0.7f,
                    node.Radius * scale,
                    node.PuffColor,
                    8,
                    16f, 20f, 0.34f, 0.5f, 0.036f, 0.062f, 2.2f, 3.2f);
            }

            // 雨丝层 —— 细长、更亮、更慢，远距离也能看出「这底下在下雨」。
            node.StreakTimer -= dt;
            if (node.StreakTimer <= 0f)
            {
                node.StreakTimer = 0.12f;
                float scale = Mathf.Max(0.3f, node.CloudRoot.localScale.x);
                VfxPool.Instance.SpawnRainDrops(
                    cloudPos + Vector3.down * 0.5f,
                    node.Radius * scale,
                    node.StreakColor,
                    3,
                    17f, 22f, 0.4f, 0.55f, 0.022f, 0.036f, 4.5f, 6f);
            }
        }

        private static void UpdateRainDamage(StormNode node, float dt)
        {
            node.DamageTimer += dt;
            int guard = 0;
            while (node.DamageTimer >= node.DamageTickInterval && guard++ < 8)
            {
                node.DamageTimer -= node.DamageTickInterval;
                float tickDamage = SpecialCombat.StormTickDamage(node.Dps, node.DamageTickInterval);
                SpecialCombat.ApplyStormTick(node.CloudRoot.position, node.Team, node.Radius, tickDamage, node.Attacker, SpecialCombat.DefaultStormMaxDropBelow, true);
            }
        }

        private static void RecordRecentHit(StormNode node, Vector3 point)
        {
            if (node.RecentHits == null || node.RecentHits.Length < RecentHitCapacity)
                node.RecentHits = new Vector3[RecentHitCapacity];

            node.RecentHits[node.RecentHitCount % RecentHitCapacity] = point;
            node.RecentHitCount++;
        }

        private static Vector3 PickRecentHit(StormNode node)
        {
            int index = Random.Range(0, Mathf.Min(node.RecentHitCount, RecentHitCapacity));
            return node.RecentHits[index];
        }

        private static Vector3 ClampInsideMap(Vector3 pos, ref Vector3 driftDir)
        {
            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null)
                return pos;

            const float Margin = 0.6f;
            Vector2 origin = surface.Grid.OriginXZ;
            Vector2 size = surface.Grid.MapSize;
            float minX = origin.x + Margin;
            float maxX = origin.x + size.x - Margin;
            float minZ = origin.y + Margin;
            float maxZ = origin.y + size.y - Margin;

            if (pos.x < minX || pos.x > maxX)
            {
                driftDir.x = -driftDir.x;
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
            }
            if (pos.z < minZ || pos.z > maxZ)
            {
                driftDir.z = -driftDir.z;
                pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            }
            return pos;
        }

        private void Recycle(StormNode node)
        {
            node.CloudSpawned = false;
            node.FlashPuffIndex = -1;
            if (node.Generator != null)
                node.Generator.gameObject.SetActive(false);
            if (node.CloudRoot != null)
                node.CloudRoot.gameObject.SetActive(false);
            if (node.RingRoot != null)
                node.RingRoot.gameObject.SetActive(false);
        }

        private StormNode CreateNode(int idx)
        {
            var node = new StormNode
            {
                Puffs = new MeshRenderer[PuffCount],
                PuffTints = new float[PuffCount],
                PuffBaseY = new float[PuffCount],
                PuffBobs = new float[PuffCount]
            };

            var root = new GameObject($"StormNode_{idx:00}");
            root.transform.SetParent(transform, false);
            node.Root = root.transform;

            var generator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            generator.name = "StormGenerator";
            generator.transform.SetParent(root.transform, false);
            Collider genCollider = generator.GetComponent<Collider>();
            if (genCollider != null)
                Destroy(genCollider);
            node.GeneratorRenderer = generator.GetComponent<MeshRenderer>();
            node.GeneratorRenderer.sharedMaterial = _sharedMat;
            node.GeneratorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            node.GeneratorRenderer.receiveShadows = false;
            generator.SetActive(false);
            node.Generator = generator.transform;

            var cloud = new GameObject("StormCloud");
            cloud.transform.SetParent(root.transform, false);
            node.CloudRoot = cloud.transform;

            // 核心：最大最深的一颗，撑起云腹（Inkwave puff(0,0,0,1.9,base)）
            CreatePuff(node, cloud.transform, 0, Vector3.zero, 1.9f, 0.12f);

            // 8 环：深色墨腹，环径 1.7~2.3，剪影直径 ≈ 2.3 + 1.5 = 3.8m，盖住 3.4m 伤害圈
            for (int i = 0; i < RingPuffCount; i++)
            {
                float angle = (i / (float)RingPuffCount) * Mathf.PI * 2f;
                float ringRadius = 1.7f + (i % 3) * 0.3f;
                float puffScale = 1.5f - (i % 2) * 0.45f;
                int index = CorePuffCount + i;
                CreatePuff(node, cloud.transform, index,
                    new Vector3(Mathf.Cos(angle) * ringRadius, (i % 2) * 0.12f - 0.06f, Mathf.Sin(angle) * ringRadius),
                    puffScale, 0.12f + (i % 3) * 0.04f);
            }

            // 5 顶：浅色云顶，堆在 0.75~1.05 高度
            for (int i = 0; i < TopPuffCount; i++)
            {
                float angle = (i / (float)TopPuffCount) * Mathf.PI * 2f + 0.4f;
                float ringRadius = 0.6f + (i % 3) * 0.4f;
                float puffScale = 1.3f - (i % 2) * 0.4f;
                int index = FlashFirstIndex + i;
                CreatePuff(node, cloud.transform, index,
                    new Vector3(Mathf.Cos(angle) * ringRadius, 0.75f + (i % 2) * 0.15f, Mathf.Sin(angle) * ringRadius),
                    puffScale, 0.48f + (i % 3) * 0.07f);
            }

            // 顶冠：最浅的一颗压轴（Inkwave puff(0,1.15,0,1.0,top)）
            CreatePuff(node, cloud.transform, PuffCount - 1, new Vector3(0f, 1.15f, 0f), 1.0f, 0.55f);

            BuildGroundRing(node);

            cloud.SetActive(false);
            return node;
        }

        private void CreatePuff(StormNode node, Transform parent, int index, Vector3 localPos, float scale, float tint)
        {
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = $"Puff_{index:00}";
            puff.transform.SetParent(parent, false);
            puff.transform.localPosition = localPos;
            puff.transform.localScale = new Vector3(scale, scale * 0.68f, scale);

            Collider collider = puff.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            MeshRenderer renderer = puff.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMat;
            // 云体投影：顶视相机下云影本身就是落点提示（Inkwave castShadow = true）
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = false;

            node.Puffs[index] = renderer;
            node.PuffTints[index] = tint;
            node.PuffBaseY[index] = localPos.y;
            node.PuffBobs[index] = Random.value * Mathf.PI * 2f;
        }

        /// <summary>
        /// 云底地面圈（复用 Slam 三层圈模式，2 层即可）：实心盘读范围、亮边读边界。
        /// 出手即显示在落点（飞行期快脉冲预警），展开后随云漂移。
        /// </summary>
        private void BuildGroundRing(StormNode node)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");

            var ring = new GameObject("StormRing");
            ring.transform.SetParent(node.Root.transform, false);
            node.RingRoot = ring.transform;

            Mesh discMesh = VfxPool.BuildRingMesh(48, 0.02f, 1f);
            node.RingDiscRenderer = CreateRingLayer(ring.transform, "Disc", discMesh, sh, 0f);

            Mesh edgeMesh = VfxPool.BuildRingMesh(40, 0.86f, 1f);
            node.RingEdgeRenderer = CreateRingLayer(ring.transform, "Edge", edgeMesh, sh, 0.015f);

            ring.SetActive(false);
        }

        /// <summary>云底圈单层：独立 renderer + 透明材质，localY 分层防 z-fight（容器 XZ 缩放统一驱动半径）。</summary>
        private static MeshRenderer CreateRingLayer(Transform parent, string layerName, Mesh mesh, Shader shader, float localY)
        {
            var layerGo = new GameObject($"StormRing_{layerName}");
            layerGo.transform.SetParent(parent, false);
            layerGo.transform.localPosition = new Vector3(0f, localY, 0f);

            var mf = layerGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var renderer = layerGo.AddComponent<MeshRenderer>();
            var material = new Material(shader);
            VfxPool.MakeUrpUnlitTransparent(material);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return renderer;
        }

        /// <summary>云底圈单层配色：队色 × 层专属透明度。</summary>
        private void SetRingColor(MeshRenderer renderer, Color teamColor, float alpha)
        {
            if (renderer == null)
                return;
            teamColor.a = Mathf.Clamp01(alpha);
            _propBlock.SetColor(BaseColorProp, teamColor);
            _propBlock.SetColor(ColorProp, teamColor);
            renderer.SetPropertyBlock(_propBlock);
        }

        private void SetColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null)
                return;

            _propBlock.SetColor(BaseColorProp, color);
            _propBlock.SetColor(ColorProp, color);
            renderer.SetPropertyBlock(_propBlock);
        }
    }
}
