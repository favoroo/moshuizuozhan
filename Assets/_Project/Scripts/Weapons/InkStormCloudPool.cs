using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace P1.Weapons
{
    /// <summary>
    /// Ink Tempest（墨水风暴云）发生器与云层对象池（P3-B）。
    /// 流程：发生器抛物线飞向目标点 → 落点上空生成盘旋墨雨云 → 沿投掷方向以 1.1m/s 漂移 6.5s，
    /// 持续向下雨点涂地，并对范围内敌方施加每秒 34 点 DOT。
    /// 场景无需预置：<see cref="EnsureInstance"/> 首次释放时自动创建，避免改动 ProjectScaffolder 的主场景。
    /// </summary>
    public class InkStormCloudPool : MonoBehaviour
    {
        public static InkStormCloudPool Instance { get; private set; }

        private const int RingPuffCount = 9;
        private const int TopPuffCount = 4;
        private const int PuffCount = RingPuffCount + TopPuffCount;
        private const int MaxRainDropsPerFrame = 24;

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

            public bool CloudSpawned;
            public Team Team;
            public Color TeamColor;
            public Color PuffColor;
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
            public float DamageTimer;
            public float VfxTimer;
            public float SplashTimer;
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

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();
            InitializeResources();

            for (int i = 0; i < 3; i++)
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
        }

        private void InitializeResources()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            _sharedMat = new Material(sh) { name = "InkStormCloudMat" };
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
            if (Physics.Raycast(snapOrigin, Vector3.down, out RaycastHit groundHit, 24f, ~0, QueryTriggerInteraction.Ignore))
            {
                targetGroundPos.y = groundHit.point.y;
            }
            else
            {
                targetGroundPos.y = startPos.y - 1f;
            }

            Vector3 drift = new Vector3(flatDelta.x, 0f, flatDelta.z);
            if (drift.sqrMagnitude < 0.0001f)
            {
                drift = new Vector3(startPos.x, 0f, startPos.z) - new Vector3(targetGroundPos.x, 0f, targetGroundPos.z);
            }
            if (drift.sqrMagnitude < 0.0001f)
            {
                drift = Vector3.forward;
            }

            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(team)
                : (team == Team.TeamA ? new Color(1f, 0.44f, 0.06f) : new Color(0.15f, 0.42f, 1f));

            node.CloudSpawned = false;
            node.Team = team;
            node.Attacker = attacker;
            node.TeamColor = teamColor;
            node.PuffColor = Color.Lerp(teamColor, Color.white, 0.30f);
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
            node.CloudHeight = config != null ? Mathf.Max(0.5f, config.stormCloudHeight) : 4.0f;
            node.RainInterval = config != null ? Mathf.Max(0.012f, config.stormRainInterval) : 0.06f;
            node.RainRadiusMin = config != null ? Mathf.Max(0.05f, config.stormRainSplatRadiusMin) : 0.42f;
            node.RainRadiusMax = config != null ? Mathf.Max(node.RainRadiusMin, config.stormRainSplatRadiusMax) : 0.72f;
            node.DamageTickInterval = config != null ? Mathf.Max(0.05f, config.stormDamageTickInterval) : 0.5f;
            node.GrowTime = config != null ? Mathf.Max(0.05f, config.stormGrowTime) : 0.5f;
            node.FadeTime = config != null ? Mathf.Max(0.05f, config.stormFadeTime) : 0.6f;
            node.RainTimer = 0f;
            node.DamageTimer = 0f;
            node.VfxTimer = 0f;
            node.SplashTimer = 0f;

            node.Generator.gameObject.SetActive(true);
            node.Generator.position = startPos;
            node.Generator.localScale = Vector3.one * 0.42f;
            SetColor(node.GeneratorRenderer, Color.Lerp(teamColor, Color.white, 0.45f));

            node.CloudRoot.gameObject.SetActive(false);
            _active.Add(node);
        }

        /// <summary>比赛重开/清场时回收全部风暴云与飞行中的发生器。</summary>
        public void ClearAllClouds()
        {
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
                }

                if (node.CloudSpawned && node.CloudElapsed >= node.Duration)
                {
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
        }

        private void UpdateFlight(StormNode node, float dt)
        {
            node.FlightElapsed += dt;
            float t = Mathf.Clamp01(node.FlightElapsed / Mathf.Max(0.05f, node.FlightDuration));

            Vector3 pos = InkBombTrajectoryUtility.EvaluateArcPoint(node.Start, node.Target + Vector3.up * 0.35f, node.ArcHeight, t);
            node.Generator.position = pos;
            node.Generator.Rotate(new Vector3(320f, 240f, 0f) * dt, Space.Self);

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
            node.DamageTimer = 0f;
            node.VfxTimer = 0f;
            node.SplashTimer = 0f;

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkSplat(node.Target, node.PuffColor, 0.9f, 12);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayLoop(LoopSfxKind.StormRain);
            }
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

            // 云层收尾的最后 0.3s 停止降雨（视觉上仍在收缩消散）
            if (node.CloudElapsed < node.Duration - 0.3f)
            {
                UpdateRainPaint(node, dt, scale);
                UpdateRainDamage(node, dt);
            }
        }

        private void UpdateRainPaint(StormNode node, float dt, float scale)
        {
            Vector3 cloudPos = node.CloudRoot.position;
            node.RainTimer -= dt;

            int drops = 0;
            bool hadHit = false;
            Vector3 lastHit = Vector3.zero;

            while (node.RainTimer <= 0f && drops < MaxRainDropsPerFrame)
            {
                node.RainTimer += node.RainInterval;
                drops++;

                float radius = node.Radius * Mathf.Max(0.2f, scale);
                float angle = Random.value * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Random.value) * radius;
                Vector3 from = new Vector3(
                    cloudPos.x + Mathf.Cos(angle) * distance,
                    cloudPos.y - 0.6f,
                    cloudPos.z + Mathf.Sin(angle) * distance);

                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, node.CloudHeight + 8f, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                hadHit = true;
                lastHit = hit.point;

                if (InkSurfaceController.Instance != null)
                {
                    float paintRadius = Random.Range(node.RainRadiusMin, node.RainRadiusMax);
                    // 风暴云涂地计入涂地覆盖率，但不返还大招能量：雨点数量太多，返还会造成「一云接一云」的自循环。
                    InkSurfaceController.Instance.PaintCircle(hit.point, paintRadius, node.Team);
                }
            }

            if (VfxPool.Instance == null)
                return;

            if (hadHit)
            {
                node.SplashTimer -= dt;
                if (node.SplashTimer <= 0f)
                {
                    node.SplashTimer = 0.14f;
                    VfxPool.Instance.SpawnInkSplat(lastHit, node.PuffColor, 0.42f, 3);
                }
            }

            node.VfxTimer -= dt;
            if (node.VfxTimer <= 0f)
            {
                node.VfxTimer = 0.1f;
                VfxPool.Instance.SpawnRainDrops(
                    cloudPos + Vector3.down * 0.7f,
                    node.Radius * Mathf.Max(0.25f, scale),
                    node.PuffColor,
                    2);
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
                SpecialCombat.ApplyStormTick(node.CloudRoot.position, node.Team, node.Radius, tickDamage, node.Attacker);
            }
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
            if (node.Generator != null)
                node.Generator.gameObject.SetActive(false);
            if (node.CloudRoot != null)
                node.CloudRoot.gameObject.SetActive(false);
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

            for (int i = 0; i < RingPuffCount; i++)
            {
                float angle = (i / (float)RingPuffCount) * Mathf.PI * 2f;
                float ringRadius = 1.55f + (i % 3) * 0.16f;
                float puffScale = 0.92f + (i % 4) * 0.10f;
                float tint = 0.18f + (i % 3) * 0.05f;
                CreatePuff(node, cloud.transform, i,
                    new Vector3(Mathf.Cos(angle) * ringRadius, (i % 2) * 0.12f - 0.06f, Mathf.Sin(angle) * ringRadius),
                    puffScale, tint);
            }

            for (int i = 0; i < TopPuffCount; i++)
            {
                float angle = (i / (float)TopPuffCount) * Mathf.PI * 2f + 0.5f;
                float ringRadius = 0.55f + (i % 2) * 0.3f;
                int index = RingPuffCount + i;
                float puffScale = 0.72f + (i % 3) * 0.09f;
                CreatePuff(node, cloud.transform, index,
                    new Vector3(Mathf.Cos(angle) * ringRadius, 0.62f + (i % 2) * 0.16f, Mathf.Sin(angle) * ringRadius),
                    puffScale, 0.52f + (i % 2) * 0.10f);
            }

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
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            node.Puffs[index] = renderer;
            node.PuffTints[index] = tint;
            node.PuffBaseY[index] = localPos.y;
            node.PuffBobs[index] = Random.value * Mathf.PI * 2f;
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
