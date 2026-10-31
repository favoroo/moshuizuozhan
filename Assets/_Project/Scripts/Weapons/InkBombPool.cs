using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 副武器「弹道延时墨水手雷」对象池（对齐 inkwave 投掷模型）：
    /// 手雷以出手初速做真实物理飞行（重力 + 撞墙反弹），接触地面后才启动 0.95s 警报引信
    /// → 核心 180 秒杀（3.1m 伤害半径内平方衰减到 35）→ 2.7m 涂地。
    /// 飞行常数与 <see cref="InkBombTrajectoryUtility.SimulateThrow"/> 预览仿真完全同参，所见即所得。
    /// </summary>
    public class InkBombPool : MonoBehaviour
    {
        public static InkBombPool Instance { get; private set; }

        private struct ActiveBomb
        {
            public Transform BombTransform;
            public MeshRenderer BombRenderer;
            public Transform OuterRingTransform;
            public MeshRenderer OuterRingRenderer;
            public Transform InnerFillTransform;
            public MeshRenderer InnerFillRenderer;

            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 LandingPos;
            public Team Team;
            public Color Color;
            public float Gravity;
            public float Age;
            public float FuseDelay;
            public float Elapsed;
            public bool Armed;
            public float SplatRadius;
            public float DamageRadius;
            public float InnerKillRadius;
            public float InnerDamage;
            public float OuterDamage;
            public Action<int> OnCellsPainted;
            public CombatantHealth Attacker;
        }

        /// <summary>未引信手雷的最长滞空时间（秒）：防极端地形无限反弹，超时静默回收。</summary>
        private const float MaxAirLifetime = 8f;
        /// <summary>引信启动后手雷球心离地高度（米）：与球体半径 0.24 对齐。</summary>
        private const float ArmedRestHeight = 0.24f;

        private readonly List<ActiveBomb> _active = new List<ActiveBomb>(16);
        private readonly Queue<ActiveBomb> _pool = new Queue<ActiveBomb>(16);
        private readonly RaycastHit[] _flightHits = new RaycastHit[8];

        private Material _sharedMat;
        private Mesh _ringMesh;
        private MaterialPropertyBlock _propBlock;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();

            Shader sh = Shader.Find("P1/InkBody");
            if (sh == null)
            {
                Debug.LogWarning("[InkBombPool] 未找到 Shader 'P1/InkBody'，回退到 Unlit 材质。");
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
            }
            _sharedMat = new Material(sh) { name = "InkBombSharedMat" };
            _ringMesh = VfxPool.BuildRingMesh(36, 0.90f, 1.0f);

            for (int i = 0; i < 8; i++)
            {
                _pool.Enqueue(CreateBombNode(i));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_sharedMat != null)
            {
                Destroy(_sharedMat);
                _sharedMat = null;
            }

            if (_ringMesh != null)
            {
                Destroy(_ringMesh);
                _ringMesh = null;
            }
        }

        public void ClearAllBombs()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                RecycleAt(i);
            }

            // P3-B：比赛重开时同时收回飞行中的风暴云发生器与降雨中的云层
            InkStormCloudPool.Instance?.ClearAllClouds();
        }

        /// <summary>
        /// 投掷手雷（inkwave 弹道模型）：以指定初速从 startPos 真实抛出，远近与反弹全部由物理决定。
        /// attacker 为投掷者（P6-A 打击标记/击杀 Hitstop 归属，旧调用可省略）。
        /// </summary>
        public void ThrowBomb(
            Vector3 startPos,
            Vector3 throwVelocity,
            Team team,
            WeaponConfigSO config,
            Action<int> onCellsPainted,
            CombatantHealth attacker = null)
        {
            ActiveBomb b = _pool.Count > 0 ? _pool.Dequeue() : CreateBombNode(_active.Count + 16);

            Color teamCol = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColorLinear(team)
                : (team == Team.TeamA ? new Color(1f, 0.44f, 0.06f).linear : new Color(0.15f, 0.42f, 1f).linear);

            b.Position = startPos;
            b.Velocity = throwVelocity;
            b.LandingPos = startPos;
            b.Team = team;
            b.Color = teamCol;
            b.Gravity = config != null && config.bombThrowGravity > 0.1f ? config.bombThrowGravity : 24f;
            b.Age = 0f;
            b.Elapsed = 0f;
            b.Armed = false;
            b.FuseDelay = config != null ? config.bombFuseDelay : 0.95f;
            b.SplatRadius = config != null ? config.bombSplatRadius : 2.7f;
            b.DamageRadius = config != null
                ? Mathf.Max(config.bombSplatRadius, config.bombDamageRadius)
                : 3.1f;
            b.InnerKillRadius = config != null ? config.bombInnerKillRadius : 1.2f;
            b.InnerDamage = config != null ? config.bombInnerDamage : 180f;
            b.OuterDamage = config != null ? config.bombOuterDamage : 35f;
            b.OnCellsPainted = onCellsPainted;
            b.Attacker = attacker;

            b.BombTransform.gameObject.SetActive(true);
            b.BombTransform.position = startPos;
            b.BombTransform.localScale = Vector3.one * 0.48f;
            SetRendererColor(b.BombRenderer, teamCol);

            b.OuterRingTransform.gameObject.SetActive(false);
            b.InnerFillTransform.gameObject.SetActive(false);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayBombThrow();
            }

            _active.Add(b);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveBomb b = _active[i];

                if (!b.Armed)
                {
                    b.Age += dt;
                    if (b.Age > MaxAirLifetime || b.Position.y < -10f)
                    {
                        // 极端地形兜底：无限反弹或飞出地图时静默回收，不引信不爆
                        RecycleAt(i);
                        continue;
                    }

                    // 半隐式欧拉积分 + 分段射线：与 SimulateThrow 预览同常数，所见即所得
                    b.Velocity = new Vector3(b.Velocity.x, b.Velocity.y - b.Gravity * dt, b.Velocity.z);
                    Vector3 next = b.Position + b.Velocity * dt;
                    Vector3 delta = next - b.Position;
                    float dist = delta.magnitude;

                    bool hitWorld = false;
                    RaycastHit hit = default;
                    if (dist > 0.0001f)
                    {
                        Vector3 dir = delta / dist;
                        hitWorld = CastBombSegment(b.Position, dir, dist, b, out hit);
                    }

                    if (hitWorld)
                    {
                        bool isFloor = hit.normal.y > 0.6f;
                        if (isFloor)
                        {
                            // 落地即引信：停在接触点旁，进入 0.95s 警报（inkwave bomb:arm 语义）
                            b.Position = hit.point + hit.normal * ArmedRestHeight;
                            b.LandingPos = b.Position;
                            b.Armed = true;
                            b.Elapsed = 0f;

                            b.BombTransform.position = b.Position;
                            Vector3 ringPos = hit.point + Vector3.up * 0.05f;
                            b.OuterRingTransform.position = ringPos;
                            b.OuterRingTransform.localScale = new Vector3(b.DamageRadius, 1f, b.DamageRadius);
                            b.OuterRingTransform.gameObject.SetActive(true);
                            SetRendererColor(b.OuterRingRenderer, b.Color);

                            b.InnerFillTransform.position = ringPos;
                            b.InnerFillTransform.localScale = new Vector3(0.2f, 1f, 0.2f);
                            b.InnerFillTransform.gameObject.SetActive(true);
                            SetRendererColor(b.InnerFillRenderer, Color.Lerp(b.Color, Color.white, 0.45f));

                            if (AudioManager.Instance != null)
                            {
                                AudioManager.Instance.PlayBombBeep();
                            }
                        }
                        else
                        {
                            // 撞墙反弹：vn' = -vn * 1.35，整体衰减 0.6（inkwave 同参数）
                            b.Position = hit.point + hit.normal * ArmedRestHeight;
                            float vn = Vector3.Dot(b.Velocity, hit.normal);
                            b.Velocity += hit.normal * (-vn * InkBombTrajectoryUtility.BounceReflect);
                            b.Velocity *= InkBombTrajectoryUtility.BounceDampWall;
                        }
                    }
                    else
                    {
                        b.Position = next;
                    }

                    b.BombTransform.position = b.Position;
                    b.BombTransform.Rotate(Vector3.right * (540f * dt), Space.Self);
                    _active[i] = b;
                }
                else
                {
                    b.Elapsed += dt;
                    float fuseT = Mathf.Clamp01(b.Elapsed / Mathf.Max(0.05f, b.FuseDelay));
                    float curRingRadius = Mathf.Lerp(0.2f, b.DamageRadius, fuseT);
                    b.InnerFillTransform.localScale = new Vector3(curRingRadius, 1f, curRingRadius);

                    // 临爆高频闪烁与膨胀
                    float pulse = 1f + Mathf.Sin(fuseT * fuseT * 38f) * 0.16f;
                    b.BombTransform.localScale = Vector3.one * (0.48f * pulse);
                    Color flashCol = Mathf.Sin(fuseT * 42f) > 0f ? Color.white : b.Color;
                    SetRendererColor(b.BombRenderer, flashCol);

                    if (fuseT >= 1f)
                    {
                        DetonateBomb(b);
                        RecycleAt(i);
                    }
                    else
                    {
                        _active[i] = b;
                    }
                }
            }
        }

        /// <summary>
        /// 弹道分段射线：命中最近的世界表面，穿透投掷者与同队角色（inkwave 炸弹不与角色碰撞）。
        /// </summary>
        private bool CastBombSegment(Vector3 origin, Vector3 direction, float distance, in ActiveBomb b, out RaycastHit hit)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin, direction, _flightHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            hit = default;
            for (int bix = 0; bix < hitCount; bix++)
            {
                RaycastHit candidate = _flightHits[bix];
                if (candidate.collider == null || candidate.distance > nearest)
                    continue;

                Transform candidateRoot = candidate.collider.transform.root;
                if (b.Attacker != null && candidateRoot == b.Attacker.transform.root)
                    continue;

                CombatantHealth body = candidate.collider.GetComponentInParent<CombatantHealth>();
                if (body != null && body.Team == b.Team)
                    continue;

                nearest = candidate.distance;
                hit = candidate;
                found = true;
            }
            return found;
        }

        private void DetonateBomb(in ActiveBomb b)
        {
            // 1. 2.7m 涂地（同时覆盖附近可涂墙面）
            if (InkSurfaceController.Instance != null)
            {
                int changed = InkSurfaceController.Instance.PaintSphereWithWalls(b.LandingPos, b.SplatRadius, b.Team);
                if (changed > 0)
                {
                    b.OnCellsPainted?.Invoke(changed);
                }
            }

            // 2. 3.1m 伤害判定：核心 180 秒杀，圈外平方衰减到 35
            SpecialCombat.ApplyBombDamage(
                b.LandingPos, b.Team, b.DamageRadius, b.InnerKillRadius, b.InnerDamage, b.OuterDamage, b.Attacker);

            // 3. 粒子、双层冲击波、音效与震屏
            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(b.LandingPos, Vector3.up, b.Color, 0.85f, 12);
                VfxPool.Instance.SpawnInkSplat(b.LandingPos, b.Color, b.SplatRadius * 0.9f, 30);
                VfxPool.Instance.SpawnShockwaveRing(b.LandingPos, b.Color, 0.35f, b.DamageRadius, 0.30f);
                VfxPool.Instance.SpawnDelayedShockwaveRing(
                    0.06f, b.LandingPos, b.Color, b.DamageRadius * 0.35f, b.DamageRadius, 0.38f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayBombExplode();
                AudioManager.Instance.PlayInkHitGround(b.LandingPos, 1.5f, b.SplatRadius);
            }

            if (TopDownCameraFollow.Instance != null)
            {
                // P6-A：手雷震屏按与本地玩家的距离平方衰减，核心 0.6、远处几乎无感
                TopDownCameraFollow.Instance.AddTraumaFromPoint(b.LandingPos, 0.6f, Mathf.Max(b.DamageRadius * 3f, 8f));
            }
        }

        private void SetRendererColor(MeshRenderer mr, Color c)
        {
            if (mr == null) return;
            _propBlock.SetColor(BaseColorProp, c);
            _propBlock.SetColor(ColorProp, c);
            mr.SetPropertyBlock(_propBlock);
        }

        private void RecycleAt(int index)
        {
            ActiveBomb b = _active[index];
            if (b.BombTransform != null) b.BombTransform.gameObject.SetActive(false);
            if (b.OuterRingTransform != null) b.OuterRingTransform.gameObject.SetActive(false);
            if (b.InnerFillTransform != null) b.InnerFillTransform.gameObject.SetActive(false);
            _pool.Enqueue(b);
            _active.RemoveAt(index);
        }

        private ActiveBomb CreateBombNode(int idx)
        {
            var bombGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bombGo.name = $"InkBomb_{idx:00}";
            bombGo.transform.SetParent(transform, false);
            var col = bombGo.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var bombMr = bombGo.GetComponent<MeshRenderer>();
            bombMr.sharedMaterial = _sharedMat;
            // 手雷球体每帧缩放脉动，投影会在地面/角色身上扫出闪烁圆影；环体已关闭，本体一并关闭。
            bombMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bombMr.receiveShadows = false;
            bombGo.SetActive(false);

            var outerGo = new GameObject($"BombOuterRing_{idx:00}");
            outerGo.transform.SetParent(transform, false);
            outerGo.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            var outerMr = outerGo.AddComponent<MeshRenderer>();
            outerMr.sharedMaterial = _sharedMat;
            outerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outerGo.SetActive(false);

            var innerGo = new GameObject($"BombInnerRing_{idx:00}");
            innerGo.transform.SetParent(transform, false);
            innerGo.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            var innerMr = innerGo.AddComponent<MeshRenderer>();
            innerMr.sharedMaterial = _sharedMat;
            innerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            innerGo.SetActive(false);

            return new ActiveBomb
            {
                BombTransform = bombGo.transform,
                BombRenderer = bombMr,
                OuterRingTransform = outerGo.transform,
                OuterRingRenderer = outerMr,
                InnerFillTransform = innerGo.transform,
                InnerFillRenderer = innerMr
            };
        }
    }
}
