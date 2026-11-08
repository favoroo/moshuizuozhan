using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 主武器墨水弹丸对象池：统一驱动所有飞行中的弹丸，支持 SphereCast 防穿透、沿途连续滴墨、
    /// 终点/撞击处的方向性拉伸爆斑，以及 VfxPool 溅射粒子与延迟落地卫星墨珠联动。
    /// 射程（水平距离）耗尽且仍悬空时不再凭空回收，而是转入自由落体，撞到真实表面才落墨。
    /// </summary>
    public class InkProjectilePool : MonoBehaviour
    {
        public static InkProjectilePool Instance { get; private set; }

        private struct ActiveProjectile
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Position;
            public Vector3 Velocity;
            public Team Team;
            public Color Color;
            public float Damage;
            public float FarDamage;
            public float FalloffStart;
            public float FalloffDistance;
            public float MaxRange;
            public float Gravity;
            public float DistanceTraveled;
            /// <summary>水平投影走过的距离：射程判定基准（与 InkBallistics 的水平射程解算一致）。</summary>
            public float RangeTraveled;
            /// <summary>射程耗尽后的自由落体阶段（不再滴墨，落地才留圆形墨斑）。</summary>
            public bool Dropping;
            public float DropElapsed;
            public float NextTrailDropDistance;
            public float TrailDropSpacing;
            public float TrailSplatRadius;
            public float EndSplatRadiusMin;
            public float EndSplatRadiusMax;
            public float EndSplatStretch;
            public int EndSplatRayCount;
            public float HitRadius;
            public GameObject OwnerRoot;
            public Action<int> OnCellsPainted;
            /// <summary>墙面格数回调：墙格面积与地面格不同，且墙面不计涂地统计，必须分开上报。</summary>
            public Action<int> OnWallCellsPainted;
            public bool HasTrailGroundPoint;
            public Vector3 LastTrailGroundPoint;
            public SlosherVolley Volley;
            public bool IsBlaster;
            public float SplashRadius;
            public float SplashDamageMax;
            public float SplashDamageMin;
            public float VisualWidth;
            public float RippleScale;
            public float TrailVisualScale;

            /// <summary>下一团拖尾墨雾的触发距离（累计飞行距离，随池化 spawn 重置）。</summary>
            public float NextTrailPuffDistance;
            /// <summary>拖尾墨雾间距（米，来自 WeaponFeelProfile）。</summary>
            public float TrailPuffIntervalMeters;
            /// <summary>拖尾墨雾同时甩出小卫星墨珠的概率（0~1）。</summary>
            public float TrailSatelliteChance;
            /// <summary>单团拖尾墨雾存活时间（秒）。</summary>
            public float TrailPuffLifetime;
            /// <summary>拖尾墨雾尺寸倍率。</summary>
            public float TrailPuffScale;
            public bool IsLocalOwner;
            /// <summary>出生重叠补判的分帧相位：非首帧/非自由落体时按 4 帧轮转查询，避免每弹每帧双物理查询。</summary>
            public int OverlapPhase;
        }

        // 容量直接按「8 人加特林弹雨 + 抛物线落滴」峰值预留：旧版 96 起、命中/落滴并发时
        // 在战斗中 List 反复翻倍（96→384 每次 40~80KB 单帧分配，正是 GC 尖刺来源之一）。
        private readonly List<ActiveProjectile> _active = new List<ActiveProjectile>(512);
        private readonly Queue<Transform> _pool = new Queue<Transform>(96);
        private int _spawnSequence;
        // 弹丸每帧要做命中过滤：collider→角色缓存把每弹每帧的 GetComponentInParent 降为一次性。
        private readonly Dictionary<Collider, CombatantHealth> _combatantCache = new Dictionary<Collider, CombatantHealth>(64);
        private const int PrewarmCount = 96;

        /// <summary>全场景每帧最多生成的拖尾墨雾团数（8 人混战 + 加特林弹雨时的硬上限）。</summary>
        private const int TrailPuffsPerFrame = 4;
        private Material _sharedMat;
        private MaterialPropertyBlock _propBlock;
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];
        /// <summary>出生重叠补判用的角色层缓冲（全场最多 8 名角色，留余量）。</summary>
        private readonly Collider[] _combatantOverlapBuffer = new Collider[12];
        private Mesh _cometMesh;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        /// <summary>射程耗尽转入自由落体时保留的水平速度比例（墨滴失去前冲动能）。</summary>
        private const float DropMomentumRetain = 0.25f;

        /// <summary>自由落体阶段的水平阻尼（1/s）：高仰角射击不会靠惯性把墨迹甩出射程。</summary>
        private const float DropHorizontalDrag = 3.5f;

        /// <summary>自由落体阶段最长存续时间（秒），超时静默回收，防止掉出地图后无限下落。</summary>
        private const float MaxDropSeconds = 5f;

        /// <summary>掉出世界（下方没有地面）的回收高度。</summary>
        private const float KillPlaneY = -30f;

        /// <summary>自由落体落地墨斑相对常规爆斑的半径比例（落滴比直击更小）。</summary>
        private const float DropSplatScale = 0.85f;

        private static readonly float[] GroundStretchOffsets = { 0f, 0.38f, 0.78f, 1.18f, 1.55f };
        private static readonly float[] GroundStretchScales = { 0.78f, 0.70f, 0.56f, 0.40f, 0.24f };
        private static readonly float[] WallStretchOffsets = { -0.35f, 0f, 0.45f, 0.9f, 1.3f };
        private static readonly float[] WallStretchScales = { 0.40f, 0.62f, 0.55f, 0.40f, 0.24f };

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();

            // P30-A5 弹丸菲涅尔边缘提亮：复用 Resources 下已有的 P1/InkVfx（不在 Resources 下的 shader
            // 会在 Player 包里被剔除，见 VfxAdditive.shader 注释）。P1/InkBod 的菲涅尔是乘算压暗
            // （_FresnelDarkEdge 只往 0.38 倍压），无法通过材质属性改成「边缘亮」；
            // InkVfx 已实现「外缘按队色提亮 + 队色染色高光 + 逐实例 _BaseColor + GPU instancing」，
            // 因此不改任何 shader 文件即可拿到队色边缘亮边的液体感。
            Shader sh = Shader.Find("P1/InkVfx");
            if (sh == null)
                sh = Shader.Find("P1/InkBody");
            if (sh == null)
            {
                Debug.LogWarning("[InkProjectilePool] 未找到 Shader 'P1/InkVfx'，回退到 Unlit 材质。");
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
            }
            _sharedMat = new Material(sh) { name = "InkProjectileMat" };
            // 边缘提亮依赖 InkVfx 的菲涅尔项；同时把湿润高光推到最高，让彗星读作「液体」而不是塑料球。
            if (_sharedMat.HasProperty("_Smoothness"))
                _sharedMat.SetFloat("_Smoothness", 0.92f);
            if (_sharedMat.HasProperty("_EmissionStrength"))
                _sharedMat.SetFloat("_EmissionStrength", 0.18f);
            if (_sharedMat.HasProperty(BaseColorProp))
                _sharedMat.SetColor(BaseColorProp, Color.white);
            // P12-D：同池弹丸共享 mesh/material，队伍色已改为 shader 的逐实例属性；
            // 开启 instancing 后多枚弹丸一次提交（移动端 SRP Batcher 关闭时生效）。
            _sharedMat.enableInstancing = true;
            _cometMesh = BuildCometMesh(16);

            for (int i = 0; i < PrewarmCount; i++)
            {
                _pool.Enqueue(CreateVisualNode(i));
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

            if (_cometMesh != null)
            {
                Destroy(_cometMesh);
                _cometMesh = null;
            }
        }

        public void ClearAllProjectiles()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                RecycleAt(i);
            }
            _combatantCache.Clear();
        }

        /// <summary>
        /// collider → 角色缓存：世界碰撞体的查询结果（null）也缓存，避免弹丸反复对同一批墙面做
        /// GetComponentInParent 的层级遍历。比赛重置时随 <see cref="ClearAllProjectiles"/> 清空。
        /// </summary>
        private CombatantHealth ResolveCombatant(Collider collider)
        {
            if (collider == null)
                return null;

            if (_combatantCache.TryGetValue(collider, out CombatantHealth cached))
            {
                if (cached != null)
                    return cached;
                _combatantCache.Remove(collider);
            }

            CombatantHealth combatant = collider.GetComponentInParent<CombatantHealth>();
            _combatantCache[collider] = combatant;
            return combatant;
        }

        public void SpawnProjectile(
            Vector3 origin,
            Vector3 direction,
            Team team,
            WeaponConfigSO config,
            GameObject ownerRoot,
            Action<int> onCellsPainted)
        {
            // 旧签名保留：按配置文件默认弹道转发到新重载，跨系统调用方无需改动。
            SpawnProjectile(WeaponShot.FromConfig(config, origin, direction), team, config, ownerRoot, onCellsPainted);
        }

        /// <summary>
        /// 每发独立参数的弹丸入口（小枪单发 / 滚筒挥击扇形墨滴）。伤害、射程、重力与近远衰减
        /// 全部来自 <see cref="WeaponShot"/>，涂抹爆斑等外观参数仍沿用统一配置。
        /// </summary>
        public void SpawnProjectile(
            WeaponShot shot,
            Team team,
            WeaponConfigSO config,
            GameObject ownerRoot,
            Action<int> onCellsPainted,
            Action<int> onWallCellsPainted = null)
        {
            Transform node = _pool.Count > 0 ? _pool.Dequeue() : CreateVisualNode(_active.Count + 64);
            node.gameObject.SetActive(true);
            node.position = shot.Origin;

            // 队色必须传原始 sRGB：材质色 / MaterialPropertyBlock 由 Unity 自动做 sRGB→Linear，
            // 再手动 .linear 会二次转换，橙色会被压成深红（v0.1.7 实测）。
            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(team)
                : (team == Team.TeamA ? new Color(1f, 0.44f, 0.06f) : new Color(0.15f, 0.42f, 1f));

            var mr = node.GetComponent<MeshRenderer>();
            _propBlock.SetColor(BaseColorProp, teamColor);
            _propBlock.SetColor(ColorProp, teamColor);
            mr.SetPropertyBlock(_propBlock);

            float speed = Mathf.Max(0.01f, shot.Speed);
            float gravity = shot.Gravity;
            float endRadius = config != null ? config.endSplatRadius : 1f;
            float endRadiusMin = config != null ? Mathf.Min(config.endSplatRadiusMin, config.endSplatRadiusMax) : 0.85f;
            float endRadiusMax = config != null ? Mathf.Max(config.endSplatRadiusMin, config.endSplatRadiusMax) : 1.2f;
            if (endRadiusMax <= 0.001f)
            {
                // 旧版资产没有填新字段时退回单一基准半径
                endRadiusMin = endRadius;
                endRadiusMax = endRadius;
            }
            endRadiusMin = Mathf.Max(0.05f, endRadiusMin);
            endRadiusMax = Mathf.Max(endRadiusMin, endRadiusMax);
            float stretch = config != null ? Mathf.Max(1f, config.endSplatStretch) : 1.6f;
            int rayCount = config != null ? Mathf.Clamp(config.endSplatRayCount, 0, 8) : 5;

            // P16 手感：视觉尺寸/涟漪强度按武器预设走，碰撞与涂地半径完全不变
            WeaponFeelParams feel = WeaponFeelProfile.Resolve(config);
            bool isLocalOwner = ownerRoot != null &&
                                CombatantHealth.IsLocalPlayer(ownerRoot.GetComponent<CombatantHealth>());

            // 复用的池化节点残留上一发的旋转与拉伸缩放（新节点则是 identity + (1,1,1) 的巨型彗星）。
            // 从 SetActive(true) 到本组件下一次 Update 之间会先渲染一帧，上一发若以平飞/横飞收尾，
            // 枪口就会凭空闪出一帧「横向乱飞的子弹」。出生当帧直接摆成与 Update 首帧一致的姿态。
            float visualWidth = 0.155f * feel.ProjectileVisualScale;
            float spawnWidth = visualWidth * WeaponFeelMath.SpawnPopScale(0f, speed);
            node.rotation = Quaternion.LookRotation(
                shot.Direction.sqrMagnitude > 0.0001f ? shot.Direction.normalized : Vector3.forward);
            node.localScale = new Vector3(
                spawnWidth,
                spawnWidth * 0.82f,
                spawnWidth * WeaponFeelMath.ProjectileStretch(speed));

            var proj = new ActiveProjectile
            {
                Transform = node,
                Renderer = mr,
                Position = shot.Origin,
                Velocity = shot.Direction.sqrMagnitude > 0.0001f ? shot.Direction.normalized * speed : Vector3.forward * speed,
                Team = team,
                Color = teamColor,
                Damage = shot.Damage,
                FarDamage = shot.FarDamage,
                FalloffStart = shot.FalloffStart,
                FalloffDistance = shot.FalloffDistance,
                MaxRange = Mathf.Max(0.1f, shot.MaxRange),
                Gravity = gravity,
                DistanceTraveled = 0f,
                RangeTraveled = 0f,
                Dropping = false,
                DropElapsed = 0f,
                NextTrailDropDistance = config != null ? Mathf.Max(0f, config.trailStartDistance) : 2.5f,
                TrailDropSpacing = config != null ? config.trailDropDistance : 1.05f,
                TrailSplatRadius = config != null ? config.trailSplatRadius : 0.44f,
                EndSplatRadiusMin = endRadiusMin,
                EndSplatRadiusMax = endRadiusMax,
                EndSplatStretch = stretch,
                EndSplatRayCount = rayCount,
                HitRadius = Mathf.Max(0.01f, shot.HitRadius),
                OwnerRoot = ownerRoot,
                OnCellsPainted = onCellsPainted,
                // 旧调用方（只传地面回调）保持原行为：墙面格数仍走同一个回调
                OnWallCellsPainted = onWallCellsPainted ?? onCellsPainted,
                Volley = shot.Volley,
                IsBlaster = shot.IsBlaster,
                SplashRadius = shot.SplashRadius,
                SplashDamageMax = shot.SplashDamageMax,
                SplashDamageMin = shot.SplashDamageMin,
                VisualWidth = visualWidth,
                RippleScale = feel.ImpactRippleScale,
                TrailVisualScale = feel.TrailVisualScale,
                // P30-A2：拖尾墨雾的累计距离从「间距」开始算，池化结构体在这里每个字段都显式赋值，
                // 复用节点/复用结构体都不会残留上一发的墨雾计时器。
                NextTrailPuffDistance = Mathf.Max(0.1f, feel.TrailPuffIntervalMeters),
                TrailPuffIntervalMeters = feel.TrailPuffIntervalMeters,
                TrailSatelliteChance = feel.TrailSatelliteChance,
                TrailPuffLifetime = feel.TrailPuffLifetime,
                TrailPuffScale = feel.TrailPuffScale,
                IsLocalOwner = isLocalOwner,
                OverlapPhase = _spawnSequence++ & 3
            };

            _active.Add(proj);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 终场哨响后：已发射的墨滴仍继续飞行（表现不中断），但不再沿途滴墨、撞击结算伤害/涂地。
            bool settleAllowed = MatchManager.CombatSettlementAllowed;
            // 拖尾墨雾是全场景共享的每帧预算，用完本帧不再生成（距离阈值照常推进，不会攒着补发）。
            int trailPuffBudget = TrailPuffsPerFrame;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveProjectile p = _active[i];

                // 射程耗尽后的自由落体：水平惯性快速衰减，重力把墨滴拉回真实地面。
                if (p.Dropping)
                {
                    p.DropElapsed += dt;
                    if (p.DropElapsed > MaxDropSeconds)
                    {
                        RecycleAt(i);
                        continue;
                    }

                    float dropDrag = Mathf.Clamp01(1f - DropHorizontalDrag * dt);
                    p.Velocity.x *= dropDrag;
                    p.Velocity.z *= dropDrag;
                }

                // 恒定重力 + 梯形积分：位移与解析弹道完全一致，保证反解落点精确。
                Vector3 velocityBefore = p.Velocity;
                p.Velocity.y -= p.Gravity * dt;
                Vector3 moveVec = (velocityBefore + p.Velocity) * 0.5f * dt;
                float stepDist = moveVec.magnitude;
                if (stepDist <= 0.00001f)
                {
                    _active[i] = p;
                    continue;
                }

                Vector3 dir = moveVec / stepDist;
                Vector3 previousPosition = p.Position;

                // 1. SphereCast 碰撞检测（敌人 / 掩体墙 / 地面斜坡）
                if (TryCheckImpact(in p, dir, stepDist, out RaycastHit hit, out CombatantHealth hitCombatant))
                {
                    // 撞击帧不能吞滴墨：本步越过的滴墨阈值先补画（滴点按步内比例插值），
                    // 否则加载顿帧让一帧从 ~2.6m 直接跨到撞击点时，滴 2 的补间墨道整段缺失，
                    // 拖尾在「滴 1 圆斑边缘 → 终点爆斑边缘」之间出现 ~0.4m 断层（PlayMode 偶发）。
                    FlushTrailDripsOnImpact(ref p, previousPosition, moveVec, stepDist);
                    HandleImpact(ref p, hit, hitCombatant, dir, settleAllowed);
                    RecycleAt(i);
                    continue;
                }

                p.Position += moveVec;
                p.DistanceTraveled += stepDist;
                p.RangeTraveled += new Vector2(moveVec.x, moveVec.z).magnitude;

                // 2. 沿途滴墨：按 1.05m 间距向地面投射小墨点，并在两次落点间补间成连续墨道；
                //    自由落体阶段墨滴已失去拖尾能量，不再沿途滴墨；终场后也不再留下新墨迹。
                if (!p.Dropping && settleAllowed)
                {
                    UpdateTrailDrips(ref p, previousPosition, moveVec, stepDist);
                }

                // 2b. P30-A2 弹丸飞行拖尾墨雾：纯表现（不落墨、不计覆盖率），终场后仍保留以读出弹道。
                if (!p.Dropping)
                {
                    UpdateTrailPuffs(ref p, previousPosition, moveVec, stepDist, ref trailPuffBudget);
                }

                // 3. 射程判定
                if (p.IsBlaster)
                {
                    // 爆破枪平直飞行：到达最大射程仍在空中就空爆。
                    if (p.DistanceTraveled >= p.MaxRange)
                    {
                        WeaponBlasterSystem.Explode(
                            p.Position,
                            p.SplashRadius,
                            p.SplashDamageMax,
                            p.SplashDamageMin,
                            p.EndSplatRadiusMax,
                            p.Team,
                            p.OwnerRoot,
                            null,
                            p.OnCellsPainted,
                            p.OnWallCellsPainted);

                        RecycleAt(i);
                        continue;
                    }
                }
                else if (!p.Dropping && p.RangeTraveled >= p.MaxRange)
                {
                    if (p.Gravity <= 0.01f)
                    {
                        // 无重力直线墨弹没有下落过程：射程尽头直接回收。
                        RecycleAt(i);
                        continue;
                    }

                    // 朝天/越过掩体的墨滴在这里失去动能，转由重力带回地面，
                    // 落地（或撞墙/撞人）时才按真实表面落墨。
                    EnterDrop(ref p);
                }

                // 4. 掉出世界兜底：防止越过边界后无限下落。
                if (p.Position.y < KillPlaneY)
                {
                    RecycleAt(i);
                    continue;
                }

                p.Transform.position = p.Position;
                if (p.Velocity.sqrMagnitude > 0.0001f)
                {
                    p.Transform.rotation = Quaternion.LookRotation(p.Velocity);
                }

                // 速度拉伸（INKWAVE 的 aShape 观感）：头部保持球形，尾巴沿飞行方向拉长；
                // 出生 0.04s 内从 65% 弹到 100%，读作「墨滴被枪口挤出来」。
                float stretchSpeed = velocityBefore.magnitude;
                float stretch = WeaponFeelMath.ProjectileStretch(stretchSpeed);
                float pop = WeaponFeelMath.SpawnPopScale(p.DistanceTraveled, stretchSpeed);
                float width = p.VisualWidth * pop;
                p.Transform.localScale = new Vector3(width, width * 0.82f, width * stretch);

                _active[i] = p;
            }
        }

        private void HandleImpact(
            ref ActiveProjectile p,
            RaycastHit hit,
            CombatantHealth hitCombatant,
            Vector3 incomingDir,
            bool settleAllowed = true)
        {
            Vector3 impactPos = hit.point;
            Vector3 impactNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
            float splatRadius = UnityEngine.Random.Range(p.EndSplatRadiusMin, p.EndSplatRadiusMax);
            // P30 方向性墨斑：水平速度归一化给图集做拉伸（撞击瞬间的速度，含重力的竖直分量不参与）
            float horizontalSpeed = new Vector2(p.Velocity.x, p.Velocity.z).magnitude;

            // 终场哨响后不再结算伤害 / 涂地 / 统计，只保留一下撞击表现。
            if (!settleAllowed)
            {
                if (VfxPool.Instance != null)
                {
                    VfxPool.Instance.SpawnInkImpact(
                        impactPos, impactNormal, p.Color, splatRadius, 3, incomingDir, p.IsLocalOwner, p.Team);
                }
                return;
            }

            // 射程耗尽后的落滴：已经失去前冲动能，落地只留一枚圆形墨斑，不做方向拉伸。
            if (p.Dropping)
            {
                HandleDropImpact(ref p, impactPos, impactNormal, hit.collider, hitCombatant, incomingDir, splatRadius);
                return;
            }

            // 爆破枪命中：由爆裂本身完成溅射伤害、涂地与全部 VFX/音效，
            // 之后必须直接结束——旧实现漏了 return，会继续走通用分支二次涂墨、二次涟漪/粒子/音效。
            // 直击目标仍要在这里单独补结算一次直伤（Explode 会跳过直击目标，避免重复受伤）。
            if (p.IsBlaster)
            {
                WeaponBlasterSystem.Explode(
                    impactPos,
                    p.SplashRadius,
                    p.SplashDamageMax,
                    p.SplashDamageMin,
                    splatRadius,
                    p.Team,
                    p.OwnerRoot,
                    hitCombatant, // 直击目标跳过溅射
                    p.OnCellsPainted,
                    p.OnWallCellsPainted);

                if (hitCombatant != null)
                {
                    float directDamage = WeaponShot.ComputeDamage(
                        p.Damage, p.FarDamage, p.FalloffStart, p.FalloffDistance, p.DistanceTraveled);
                    if (directDamage > 0f)
                    {
                        CombatantHealth attacker = p.OwnerRoot != null
                            ? p.OwnerRoot.GetComponent<CombatantHealth>()
                            : null;
                        hitCombatant.TakeDamage(directDamage, p.Team, impactPos, attacker);
                    }
                }

                return;
            }

            if (hitCombatant != null)
            {
                // 滚筒挥击等弹丸按飞行距离做近→远伤害衰减（近 125 / 远 30）。
                float damage = WeaponShot.ComputeDamage(
                    p.Damage, p.FarDamage, p.FalloffStart, p.FalloffDistance, p.DistanceTraveled);

                // 泼桶 Volley 聚合算法：同一次泼洒对同目标最多只判 1 次直击
                if (p.Volley != null)
                {
                    if (!p.Volley.TryRegisterHit(hitCombatant))
                    {
                        damage = 0f;
                    }
                }

                if (damage > 0f)
                {
                    // P6-A：把弹丸归属者透传给受击结算，供打击标记 / 击杀 Hitstop / 击杀卡片使用
                    CombatantHealth attacker = p.OwnerRoot != null
                        ? p.OwnerRoot.GetComponent<CombatantHealth>()
                        : null;
                    hitCombatant.TakeDamage(damage, p.Team, impactPos, attacker);
                }
                Vector3 belowPoint = ProjectToGround(hitCombatant.transform.position, out Vector3 belowNormal);
                PaintStretchBlob(belowPoint, belowNormal, incomingDir, splatRadius * 0.85f, p.EndSplatStretch, Mathf.Max(2, p.EndSplatRayCount / 2), p.Team, p.OnCellsPainted, p.RippleScale, horizontalSpeed);
            }
            else
            {
                // 命中的是可涂墙面的侧立面（impactNormal.y <= 0.55f）时涂在墙面上；
                // 命中顶面（如高台顶、集装箱顶、木箱顶等）走顶面图集涂墨
                InkWallSurface wall = InkWallSurface.FindForCollider(hit.collider);
                bool hitVerticalWall = wall != null && impactNormal.y <= 0.55f;
                if (hitVerticalWall)
                {
                    int wallChanged = PaintWallStretchBlob(wall, impactPos, impactNormal, incomingDir, splatRadius, p.EndSplatStretch, p.Team);
                    if (wallChanged > 0)
                    {
                        p.OnWallCellsPainted?.Invoke(wallChanged);
                    }
                }
                else
                {
                    PaintStretchBlob(impactPos, impactNormal, incomingDir, splatRadius, p.EndSplatStretch, p.EndSplatRayCount, p.Team, p.OnCellsPainted, p.RippleScale, horizontalSpeed);
                }

                if (AudioManager.Instance != null)
                {
                    // 打墙与打地必须是两种音色：墙没有体积，用「啪」；地面用湿墨花。
                    // 旧版只有落地音，打墙听起来像打在棉花上。
                    if (hitVerticalWall)
                        AudioManager.Instance.PlayInkHitWall(impactPos, 0.9f);
                    else
                        AudioManager.Instance.PlayInkHitGround(impactPos, 0.75f, splatRadius);
                }
            }

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(
                    impactPos, impactNormal, p.Color, splatRadius, 4, incomingDir, p.IsLocalOwner, p.Team);
            }
        }

        /// <summary>
        /// 射程耗尽后的落滴结算：墨滴是垂直落下的，所以按落点表面铺一枚圆形墨斑
        /// （不做沿入射方向的拉伸，避免垂直落滴长出一条水平尾巴），伤害与 Volley 规则与直击一致。
        /// </summary>
        private void HandleDropImpact(
            ref ActiveProjectile p,
            Vector3 impactPos,
            Vector3 impactNormal,
            Collider hitCollider,
            CombatantHealth hitCombatant,
            Vector3 incomingDir,
            float splatRadius)
        {
            float radius = splatRadius * DropSplatScale;

            if (hitCombatant != null)
            {
                float damage = WeaponShot.ComputeDamage(
                    p.Damage, p.FarDamage, p.FalloffStart, p.FalloffDistance, p.DistanceTraveled);

                if (p.Volley != null && !p.Volley.TryRegisterHit(hitCombatant))
                    damage = 0f;

                if (damage > 0f)
                {
                    CombatantHealth attacker = p.OwnerRoot != null
                        ? p.OwnerRoot.GetComponent<CombatantHealth>()
                        : null;
                    hitCombatant.TakeDamage(damage, p.Team, impactPos, attacker);
                }

                Vector3 belowPoint = ProjectToGround(hitCombatant.transform.position, out _);
                PaintGroundCircle(belowPoint, radius * 0.85f, p.Team, p.OnCellsPainted, visualScale: p.TrailVisualScale);
            }
            else
            {
                InkWallSurface wall = InkWallSurface.FindForCollider(hitCollider);
                if (wall != null && impactNormal.y <= 0.55f)
                {
                    int wallChanged = wall.PaintAtWorld(impactPos, impactNormal, radius, p.Team);
                    if (wallChanged > 0)
                    {
                        p.OnWallCellsPainted?.Invoke(wallChanged);
                    }
                }
                else
                {
                    PaintGroundCircle(impactPos, radius, p.Team, p.OnCellsPainted, true, false, p.TrailVisualScale);

                    // 一次落地只发一条涟漪
                    if (InkSurfaceController.Instance != null)
                    {
                        InkSurfaceController.Instance.TriggerRipple(
                            impactPos,
                            WeaponFeelMath.ImpactRippleAmplitude(radius, p.RippleScale),
                            0.34f,
                            3.4f,
                            1.05f);
                    }
                }

                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayInkHitGround(impactPos, 0.65f, radius);
                }
            }

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(
                    impactPos, impactNormal, p.Color, radius, 3, incomingDir, p.IsLocalOwner, p.Team);
            }
        }

        /// <summary>
        /// 射程耗尽 → 自由落体：抹掉剩余上升速度、保留少量水平惯性（之后按
        /// <see cref="DropHorizontalDrag"/> 快速衰减），由重力把墨滴带回真实地面。
        /// </summary>
        private static void EnterDrop(ref ActiveProjectile p)
        {
            p.Dropping = true;
            p.DropElapsed = 0f;

            if (p.Velocity.y > 0f)
                p.Velocity.y = 0f;

            p.Velocity.x *= DropMomentumRetain;
            p.Velocity.z *= DropMomentumRetain;
        }

        /// <summary>
        /// 连续滴墨：阈值以“飞行距离”计，跨阈值时按本帧位移精确插值出滴墨点，
        /// 再把上一滴与当前滴之间补成胶囊状墨道，杜绝高速扫射时的断裂斑点。
        /// </summary>
        private static void UpdateTrailDrips(ref ActiveProjectile p, Vector3 previousPosition, Vector3 moveVec, float stepDist)
        {
            if (p.TrailDropSpacing <= 0.0001f)
                return;

            int guard = 0;
            while (p.DistanceTraveled >= p.NextTrailDropDistance && guard++ < 16)
            {
                float distanceIntoSegment = p.NextTrailDropDistance - (p.DistanceTraveled - stepDist);
                distanceIntoSegment = Mathf.Clamp(distanceIntoSegment, 0f, stepDist);
                Vector3 dropPos = previousPosition + moveVec * (distanceIntoSegment / stepDist);

                if (TryProjectToGround(dropPos, out Vector3 groundPoint))
                {
                    if (p.HasTrailGroundPoint)
                    {
                        PaintGroundStreak(p.LastTrailGroundPoint, groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted, p.TrailVisualScale);
                    }
                    else
                    {
                        PaintGroundCircle(groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted, visualScale: p.TrailVisualScale);
                    }

                    p.LastTrailGroundPoint = groundPoint;
                    p.HasTrailGroundPoint = true;
                }

                p.NextTrailDropDistance += p.TrailDropSpacing;
            }
        }

        /// <summary>
        /// 撞击帧的滴墨补画：撞击分支在位置推进<strong>之前</strong>短路返回，
        /// 本步越过的滴墨阈值（DistanceTraveled 尚未累加）会被整体跳过。
        /// 与 <see cref="UpdateTrailDrips"/> 同一套插值，只是阈值判定改用「旧里程 + 本步步长」。
        /// </summary>
        private static void FlushTrailDripsOnImpact(ref ActiveProjectile p, Vector3 previousPosition, Vector3 moveVec, float stepDist)
        {
            if (p.TrailDropSpacing <= 0.0001f)
                return;

            int guard = 0;
            while (p.DistanceTraveled + stepDist >= p.NextTrailDropDistance && guard++ < 16)
            {
                float distanceIntoSegment = Mathf.Clamp(p.NextTrailDropDistance - p.DistanceTraveled, 0f, stepDist);
                Vector3 dropPos = previousPosition + moveVec * (distanceIntoSegment / stepDist);

                if (TryProjectToGround(dropPos, out Vector3 groundPoint))
                {
                    if (p.HasTrailGroundPoint)
                    {
                        PaintGroundStreak(p.LastTrailGroundPoint, groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted, p.TrailVisualScale);
                    }
                    else
                    {
                        PaintGroundCircle(groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted, visualScale: p.TrailVisualScale);
                    }

                    p.LastTrailGroundPoint = groundPoint;
                    p.HasTrailGroundPoint = true;
                }

                p.NextTrailDropDistance += p.TrailDropSpacing;
            }
        }

        /// <summary>
        /// P30-A2：按飞行距离在弹道后方留下低透明度队色墨雾，按武器预设的概率同时甩出一颗
        /// 沿速度反方向的小卫星墨珠。阈值按本帧位移精确插值，高速弹的雾团不会偏到错误位置。
        /// 每帧全场景预算由调用方传入（耗尽后仍推进阈值，避免下帧补发一串）。
        /// </summary>
        private static void UpdateTrailPuffs(
            ref ActiveProjectile p,
            Vector3 previousPosition,
            Vector3 moveVec,
            float stepDist,
            ref int budget)
        {
            float interval = p.TrailPuffIntervalMeters;
            if (interval <= 0.01f || stepDist <= 0.00001f)
                return;

            int guard = 0;
            while (p.DistanceTraveled >= p.NextTrailPuffDistance && guard++ < 8)
            {
                float distanceIntoSegment = p.NextTrailPuffDistance - (p.DistanceTraveled - stepDist);
                distanceIntoSegment = Mathf.Clamp(distanceIntoSegment, 0f, stepDist);
                Vector3 puffPos = previousPosition + moveVec * (distanceIntoSegment / stepDist);

                if (budget > 0 && VfxPool.Instance != null)
                {
                    budget--;
                    Vector3 velocityDir = p.Velocity.sqrMagnitude > 0.0001f
                        ? p.Velocity.normalized
                        : Vector3.forward;
                    VfxPool.Instance.SpawnTrailPuff(
                        puffPos, velocityDir, p.Color, p.TrailPuffScale, p.TrailPuffLifetime);

                    if (p.TrailSatelliteChance > 0f && UnityEngine.Random.value < p.TrailSatelliteChance)
                    {
                        VfxPool.Instance.SpawnTrailSatellite(
                            puffPos, velocityDir, p.Color, p.Team, p.TrailPuffScale);
                    }
                }

                p.NextTrailPuffDistance += interval;
            }
        }

        /// <summary>沿地面把两点之间补成连续墨道（圆与圆按半径的 0.9 倍步距重叠）。只铺平滑主体、不刷涟漪。</summary>
        private static void PaintGroundStreak(
            Vector3 from,
            Vector3 to,
            float radius,
            Team team,
            Action<int> onCellsPainted,
            float visualScale = 1f)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;

            if (distance < 0.0001f)
            {
                PaintGroundCircle(to, radius, team, onCellsPainted, visualScale: visualScale);
                return;
            }

            float spacing = Mathf.Max(0.12f, radius * 0.9f);
            int steps = Mathf.Min(16, Mathf.CeilToInt(distance / spacing));
            for (int i = 1; i <= steps; i++)
            {
                PaintGroundCircle(
                    Vector3.Lerp(from, to, i / (float)steps), radius, team, onCellsPainted, visualScale: visualScale);
            }
        }

        /// <summary>
        /// 方向性拉伸爆斑：主瓣保留完整有机装饰（射线 + 卫星），拖尾链与星状射线只铺平滑主体。
        /// 视觉上读作「一枚大墨斑 + 一道被拉长的尾巴」，而不是十几枚同样带刺的斑点叠在一起；
        /// 同时整次撞击只发一条合并后的涟漪，让冲击波真的能扩散开。
        /// </summary>
        private static void PaintStretchBlob(
            Vector3 point,
            Vector3 surfaceNormal,
            Vector3 incomingDir,
            float radius,
            float stretch,
            int rayCount,
            Team team,
            Action<int> onCellsPainted,
            float rippleScale,
            float horizontalSpeed = 0f)
        {
            if (InkSurfaceController.Instance == null || radius <= 0f)
                return;

            Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
            Vector3 tangent = Vector3.ProjectOnPlane(incomingDir, normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;
            tangent.Normalize();

            // 主瓣：完整有机装饰，是这一发墨斑的视觉主体。
            // P30：把「世界 XZ 拉伸方向 + 速度拉伸强度」透传给图集（视觉拉伸由 Ink 域实现，玩法网格仍为正圆）。
            Vector2 directionXZ = new Vector2(tangent.x, tangent.z);
            float stretch01 = Mathf.Clamp01(horizontalSpeed / 25f);
            PaintGroundCircle(
                point - tangent * (radius * 0.38f),
                radius * 0.52f,
                team,
                onCellsPainted,
                true,
                false,
                1f,
                directionXZ,
                stretch01);

            // 拖尾链：只铺平滑主体，构成被拉长的椭圆主瓣
            for (int i = 0; i < GroundStretchOffsets.Length; i++)
            {
                Vector3 blobPoint = point + tangent * (radius * GroundStretchOffsets[i] * stretch);
                PaintGroundCircle(blobPoint, radius * GroundStretchScales[i], team, onCellsPainted);
            }

            // 星状小射线：平滑小斑，避免每条尾巴都长出 7~10 根带卫星的刺
            for (int i = 0; i < rayCount; i++)
            {
                float angle = (i / (float)Mathf.Max(1, rayCount)) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.4f, 0.4f);
                Vector3 radial = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, normal) * tangent;
                float distance = radius * stretch * UnityEngine.Random.Range(0.7f, 1.15f);
                float rayRadius = radius * UnityEngine.Random.Range(0.10f, 0.20f);
                PaintGroundCircle(point + radial * distance, rayRadius, team, onCellsPainted);
            }

            // 一次撞击 = 一条干净扩散的冲击涟漪（同一位置 0.09s 内的连发会自动合并）
            InkSurfaceController.Instance.TriggerRipple(
                point,
                WeaponFeelMath.ImpactRippleAmplitude(radius, rippleScale),
                0.34f,
                3.4f,
                1.05f);
        }

        /// <summary>墙面拉伸：把入射方向投影到墙面上，沿切向铺一串逐渐收窄的墨斑。</summary>
        private static int PaintWallStretchBlob(
            InkWallSurface wall,
            Vector3 point,
            Vector3 normal,
            Vector3 incomingDir,
            float radius,
            float stretch,
            Team team)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(incomingDir, normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;
            tangent.Normalize();

            // 墙面面积有限，拉伸幅度按 0.65 收敛，避免墨斑跑到墙外
            int changed = 0;
            for (int i = 0; i < WallStretchOffsets.Length; i++)
            {
                Vector3 blobPoint = point + tangent * (radius * WallStretchOffsets[i] * stretch * 0.65f);
                changed += wall.PaintAtWorld(blobPoint, normal, radius * WallStretchScales[i], team);
            }
            return changed;
        }

        /// <summary>
        /// 爆破弹撞墙/撞地用的世界判定半径（米）：几乎等于细射线。
        /// 弹丸半径只用来「接住角色」，不能拿去扫几何体——0.26~0.38m 的球体下沿在下压弹道上
        /// 会提前「半径 / tan(俯角)」米贴地（枪口约 0.55m、瞄 10m 外地面时约 5m 就炸在脚边）。
        /// 对齐 INKWAVE：世界走细线段、角色走胶囊 + 弹丸尺寸。
        /// </summary>
        private const float BlasterWorldHitRadius = 0.02f;

        private bool TryCheckImpact(
            in ActiveProjectile p,
            Vector3 dir,
            float stepDist,
            out RaycastHit bestHit,
            out CombatantHealth bestCombatant)
        {
            bestHit = default;
            bestCombatant = null;

            // 角色：按弹丸半径球扫（爆破枪的大半径只为让直击更好接）；
            // 其它弹丸一次 Any 扫完角色与几何体，与旧实现完全一致。
            bool found = CollectClosestImpact(
                in p, dir, stepDist, p.HitRadius,
                p.IsBlaster ? ImpactFilter.ActorsOnly : ImpactFilter.Any,
                ref bestHit, ref bestCombatant);
            if (!p.IsBlaster)
            {
                // 角色命中优先（含贴脸重叠补判）。
                if (bestCombatant != null)
                    return true;

                // 球扫已经命中墙面 / 地面 / 掩体：必须按世界命中结算。
                // 旧写法只把「角色重叠补判」的结果当返回值，把 found（世界命中）丢掉了，
                // 于是非爆破弹丸直接穿过墙与地面，墙面永远涂不上墨（只剩沿途滴墨的 XZ 投影）。
                if (found)
                    return true;

                // 球扫没扫到任何东西时，再补一次「出生/悬停已重叠」判定
                // （贴脸挥击出生盲区，见 TryResolveOverlappedCombatant）。
                return OverlapCheckDue(in p) && TryResolveOverlappedCombatant(in p, ref bestHit, ref bestCombatant);
            }

            // 几何体：细射线。两段取更近者，避免大球体把「墙前一点点的地面/墙角」判成命中。
            CollectClosestImpact(in p, dir, stepDist, BlasterWorldHitRadius, ImpactFilter.WorldOnly, ref bestHit, ref bestCombatant);
            if (bestCombatant == null && OverlapCheckDue(in p) && TryResolveOverlappedCombatant(in p, ref bestHit, ref bestCombatant))
                return true;

            return bestCombatant != null || bestHit.collider != null;
        }

        /// <summary>出生首帧（RangeTraveled==0）与自由落体阶段需要逐帧补判，其余按相位 4 帧轮转。</summary>
        private static bool OverlapCheckDue(in ActiveProjectile p)
        {
            return p.RangeTraveled <= 0.0001f || p.Dropping || ((Time.frameCount + p.OverlapPhase) & 3) == 0;
        }

        /// <summary>
        /// 球扫的出生重叠盲区补判（对齐 INKWAVE weapons.js _step 的「线段-胶囊距离」语义，重叠即命中）：
        /// <c>Physics.SphereCast</c> 不报告球体起始时已相交的碰撞体——滚筒挥击贴脸释放时，出射点
        /// （身前 0.55m / 高 1.15m，命中半径 0.85m）已经嵌进敌人胶囊（半径 0.45m），球扫整段静默失败，
        /// 墨滴飞过头顶，隔一段距离才命中。这里在球扫未找到角色时，用只含角色层的 OverlapSphere
        /// 补判「已重叠」的敌人，命中点取碰撞体上离弹丸最近的点；若已有更近的世界命中则不覆盖。
        /// 顺带覆盖「敌人走进悬停墨滴」导致球扫起始即重叠而漏判的情况。
        /// </summary>
        private bool TryResolveOverlappedCombatant(
            in ActiveProjectile p,
            ref RaycastHit bestHit,
            ref CombatantHealth bestCombatant)
        {
            int count = Physics.OverlapSphereNonAlloc(
                p.Position, p.HitRadius, _combatantOverlapBuffer, GameLayers.CombatantMask, QueryTriggerInteraction.Ignore);
            if (count <= 0)
                return false;

            Vector3 pos = p.Position;
            float bestDist = bestHit.collider != null ? bestHit.distance : float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            Vector3 bestNormal = Vector3.up;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider col = _combatantOverlapBuffer[i];
                if (col == null)
                    continue;
                if (p.OwnerRoot != null && col.transform.IsChildOf(p.OwnerRoot.transform))
                    continue;

                CombatantHealth combatant = ResolveCombatant(col);
                if (combatant == null || combatant.Team == p.Team || !combatant.IsAlive)
                    continue;

                Vector3 closest = col.ClosestPoint(pos);
                float dist = Vector3.Distance(closest, pos);
                // 出生点已在胶囊内部时 ClosestPoint 返回原点，改用胸口位置兜底，
                // 保证受击特效 / 击杀卡有合理落点。
                if (dist < 0.02f)
                {
                    closest = combatant.transform.position + Vector3.up * 0.9f;
                    dist = Vector3.Distance(closest, pos);
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = closest;
                    bestNormal = (pos - closest).sqrMagnitude > 0.0001f ? (pos - closest).normalized : Vector3.up;
                    bestCombatant = combatant;
                    found = true;
                }
            }

            if (!found)
                return false;

            // RaycastHit.collider 为只读且在此路径不可达（combatant 命中分支不会查世界碰撞体），保持 null。
            bestHit = default;
            bestHit.point = bestPoint;
            bestHit.normal = bestNormal;
            bestHit.distance = bestDist;
            return true;
        }

        /// <summary>本次球扫收哪一类命中：任意 / 只收角色 / 只收几何体。</summary>
        private enum ImpactFilter
        {
            Any,
            ActorsOnly,
            WorldOnly,
        }

        /// <summary>
        /// 一次球扫，按 <paramref name="filter"/> 收命中，与已有的最近命中比较后写回。
        /// 爆破弹分两段调用（角色大半径 + 几何体细射线），其它弹丸一次 Any 走完全部。
        /// </summary>
        private bool CollectClosestImpact(
            in ActiveProjectile p,
            Vector3 dir,
            float stepDist,
            float radius,
            ImpactFilter filter,
            ref RaycastHit bestHit,
            ref CombatantHealth bestCombatant)
        {
            int count = Physics.SphereCastNonAlloc(p.Position, radius, dir, _hitBuffer, stepDist, GameLayers.QueryMask, QueryTriggerInteraction.Ignore);
            if (count <= 0)
                return false;

            float closestDist = bestHit.collider != null ? bestHit.distance : float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hitBuffer[i];
                if (h.collider == null) continue;
                if (p.OwnerRoot != null && h.collider.transform.IsChildOf(p.OwnerRoot.transform))
                    continue;

                var combatant = ResolveCombatant(h.collider);
                if (combatant != null)
                {
                    // 友方角色不挡己方子弹
                    if (combatant.Team == p.Team || !combatant.IsAlive)
                        continue;
                }

                if (filter == ImpactFilter.ActorsOnly && combatant == null)
                    continue;
                if (filter == ImpactFilter.WorldOnly && combatant != null)
                    continue;

                if (h.distance < closestDist)
                {
                    closestDist = h.distance;
                    bestHit = h;
                    bestCombatant = combatant;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 铺一枚圆形墨斑。<paramref name="decorated"/> = false 时图集只画平滑主体（无射线/卫星），
        /// <paramref name="emitRipple"/> 由调用方在撞击点统一触发，避免每枚墨斑都刷一条涟漪。
        /// </summary>
        private static void PaintGroundCircle(
            Vector3 pos,
            float radius,
            Team team,
            Action<int> onCellsPainted,
            bool decorated = false,
            bool emitRipple = false,
            float visualScale = 1f,
            Vector2 directionXZ = default,
            float stretch01 = 0f)
        {
            if (InkSurfaceController.Instance == null)
                return;

            int changed = InkSurfaceController.Instance.PaintCircle(
                pos, radius, team, emitRipple, decorated, visualScale, directionXZ, stretch01);
            if (changed > 0)
            {
                onCellsPainted?.Invoke(changed);
            }
        }

        private static bool TryProjectToGround(Vector3 worldPos, out Vector3 groundPoint)
        {
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                groundPoint = hit.point;
                return true;
            }

            groundPoint = new Vector3(worldPos.x, 0f, worldPos.z);
            return false;
        }

        private static Vector3 ProjectToGround(Vector3 worldPos, out Vector3 groundNormal)
        {
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 12f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
                return hit.point;
            }

            groundNormal = Vector3.up;
            return new Vector3(worldPos.x, 0f, worldPos.z);
        }

        private void RecycleAt(int index)
        {
            Transform node = _active[index].Transform;
            if (node != null)
            {
                node.gameObject.SetActive(false);
                _pool.Enqueue(node);
            }
            _active.RemoveAt(index);
        }

        private Transform CreateVisualNode(int idx)
        {
            var go = new GameObject($"InkShot_{idx:00}");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _cometMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            go.SetActive(false);
            return go.transform;
        }

        /// <summary>
        /// 程序化「彗星墨滴」网格：+Z 为飞行方向，头部接近球形、尾部收细到 0。
        /// 所有弹丸共享同一份 Mesh（池内复用），靠 Transform.localScale 做速度拉伸，
        /// 不再每颗弹丸各建一个 Primitive Sphere。
        /// </summary>
        private static Mesh BuildCometMesh(int radialSegments)
        {
            radialSegments = Mathf.Max(6, radialSegments);

            // (z, radius)：头部圆润、尾部锥形收细；与世界单位一致，由 localScale 决定实际尺寸
            float[] ringZ = { 0.95f, 0.78f, 0.50f, 0.15f, -0.35f, -0.95f, -1.65f, -2.45f, -3.15f, -3.35f };
            float[] ringR = { 0.00f, 0.42f, 0.78f, 1.00f, 0.86f, 0.62f, 0.40f, 0.20f, 0.06f, 0.00f };
            int ringCount = ringZ.Length;

            var vertices = new Vector3[ringCount * radialSegments];
            var uvs = new Vector2[vertices.Length];
            // P30-A5：InkVfx 的顶点色乘了逐实例队色；彗星网格没有顶点色时必须显式补白色，
            // 否则不同图形 API 下缺失的 COLOR 流可能被读成 0，弹丸会整颗发黑。
            var colors = new Color32[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
            }

            for (int i = 0; i < ringCount; i++)
            {
                for (int s = 0; s < radialSegments; s++)
                {
                    float angle = s / (float)radialSegments * Mathf.PI * 2f;
                    int v = i * radialSegments + s;
                    vertices[v] = new Vector3(
                        Mathf.Cos(angle) * ringR[i],
                        Mathf.Sin(angle) * ringR[i],
                        ringZ[i]);
                    uvs[v] = new Vector2(s / (float)radialSegments, i / (float)(ringCount - 1));
                }
            }

            var triangles = new int[(ringCount - 1) * radialSegments * 6];
            int ti = 0;
            for (int i = 0; i < ringCount - 1; i++)
            {
                for (int s = 0; s < radialSegments; s++)
                {
                    int s1 = (s + 1) % radialSegments;
                    int a = i * radialSegments + s;
                    int b = i * radialSegments + s1;
                    int c = (i + 1) * radialSegments + s;
                    int d = (i + 1) * radialSegments + s1;

                    // 外法线绕序：a → c → b / b → c → d
                    triangles[ti++] = a;
                    triangles[ti++] = c;
                    triangles[ti++] = b;
                    triangles[ti++] = b;
                    triangles[ti++] = c;
                    triangles[ti++] = d;
                }
            }

            var mesh = new Mesh
            {
                name = "InkProjectileComet",
                hideFlags = HideFlags.DontSave
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
