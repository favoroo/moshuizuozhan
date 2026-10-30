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
            public bool HasTrailGroundPoint;
            public Vector3 LastTrailGroundPoint;
            public SlosherVolley Volley;
            public bool IsBlaster;
            public float SplashRadius;
            public float SplashDamageMax;
            public float SplashDamageMin;
        }

        private readonly List<ActiveProjectile> _active = new List<ActiveProjectile>(64);
        private readonly Queue<Transform> _pool = new Queue<Transform>(64);
        private Material _sharedMat;
        private MaterialPropertyBlock _propBlock;
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private static readonly float[] GroundStretchOffsets = { 0f, 0.38f, 0.78f, 1.18f, 1.55f };
        private static readonly float[] GroundStretchScales = { 0.78f, 0.70f, 0.56f, 0.40f, 0.24f };
        private static readonly float[] WallStretchOffsets = { -0.35f, 0f, 0.45f, 0.9f, 1.3f };
        private static readonly float[] WallStretchScales = { 0.40f, 0.62f, 0.55f, 0.40f, 0.24f };

        private void Awake()
        {
            Instance = this;
            _propBlock = new MaterialPropertyBlock();

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _sharedMat = new Material(sh) { name = "InkProjectileMat" };

            for (int i = 0; i < 48; i++)
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
        }

        public void ClearAllProjectiles()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                RecycleAt(i);
            }
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
            Action<int> onCellsPainted)
        {
            Transform node = _pool.Count > 0 ? _pool.Dequeue() : CreateVisualNode(_active.Count + 64);
            node.gameObject.SetActive(true);
            node.position = shot.Origin;

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
                Volley = shot.Volley,
                IsBlaster = shot.IsBlaster,
                SplashRadius = shot.SplashRadius,
                SplashDamageMax = shot.SplashDamageMax,
                SplashDamageMin = shot.SplashDamageMin
            };

            _active.Add(proj);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveProjectile p = _active[i];

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

                // 1. SphereCast 碰撞检测（敌人 / 掩体墙 / 地面斜坡）
                if (TryCheckImpact(in p, dir, stepDist, out RaycastHit hit, out CombatantHealth hitCombatant))
                {
                    HandleImpact(ref p, hit, hitCombatant, dir);
                    RecycleAt(i);
                    continue;
                }

                Vector3 previousPosition = p.Position;
                p.Position += moveVec;
                p.DistanceTraveled += stepDist;

                // 2. 沿途滴墨：按 1.05m 间距向地面投射小墨点，并在两次落点间补间成连续墨道
                UpdateTrailDrips(ref p, previousPosition, moveVec, stepDist);

                // 3. 到达最大射程：落地爆开终点拉伸大墨斑（爆破枪在此处空中爆裂）
                if (p.DistanceTraveled >= p.MaxRange)
                {
                    if (p.IsBlaster)
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
                            p.OnCellsPainted);
                    }
                    else
                    {
                        Vector3 groundPoint = ProjectToGround(p.Position, out Vector3 groundNormal);
                        float radius = UnityEngine.Random.Range(p.EndSplatRadiusMin, p.EndSplatRadiusMax);
                        PaintStretchBlob(groundPoint, groundNormal, p.Velocity, radius, p.EndSplatStretch, p.EndSplatRayCount, p.Team, p.OnCellsPainted);

                        if (VfxPool.Instance != null)
                        {
                            VfxPool.Instance.SpawnInkImpact(groundPoint, groundNormal, p.Color, radius, 3);
                        }
                        if (AudioManager.Instance != null)
                        {
                            AudioManager.Instance.PlayInkHitGround(0.8f);
                        }
                    }

                    RecycleAt(i);
                    continue;
                }

                p.Transform.position = p.Position;
                if (p.Velocity.sqrMagnitude > 0.0001f)
                {
                    p.Transform.rotation = Quaternion.LookRotation(p.Velocity);
                }
                _active[i] = p;
            }
        }

        private void HandleImpact(ref ActiveProjectile p, RaycastHit hit, CombatantHealth hitCombatant, Vector3 incomingDir)
        {
            Vector3 impactPos = hit.point;
            Vector3 impactNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
            float splatRadius = UnityEngine.Random.Range(p.EndSplatRadiusMin, p.EndSplatRadiusMax);

            // 爆破枪命中时发生爆裂溅射
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
                    p.OnCellsPainted);
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
                PaintStretchBlob(belowPoint, belowNormal, incomingDir, splatRadius * 0.85f, p.EndSplatStretch, Mathf.Max(2, p.EndSplatRayCount / 2), p.Team, p.OnCellsPainted);
            }
            else
            {
                // 命中的是可涂墙面的侧立面（impactNormal.y <= 0.55f）时涂在墙面上；
                // 命中顶面（如高台顶、集装箱顶、木箱顶等）走顶面图集涂墨
                InkWallSurface wall = InkWallSurface.FindForCollider(hit.collider);
                if (wall != null && impactNormal.y <= 0.55f)
                {
                    int wallChanged = PaintWallStretchBlob(wall, impactPos, impactNormal, incomingDir, splatRadius, p.EndSplatStretch, p.Team);
                    if (wallChanged > 0)
                    {
                        p.OnCellsPainted?.Invoke(wallChanged);
                    }
                }
                else
                {
                    PaintStretchBlob(impactPos, impactNormal, incomingDir, splatRadius, p.EndSplatStretch, p.EndSplatRayCount, p.Team, p.OnCellsPainted);
                }

                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayInkHitGround(0.75f);
                }
            }

            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(impactPos, impactNormal, p.Color, splatRadius, 4);
            }
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
                        PaintGroundStreak(p.LastTrailGroundPoint, groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted);
                    }
                    else
                    {
                        PaintGroundCircle(groundPoint, p.TrailSplatRadius, p.Team, p.OnCellsPainted);
                    }

                    p.LastTrailGroundPoint = groundPoint;
                    p.HasTrailGroundPoint = true;
                }

                p.NextTrailDropDistance += p.TrailDropSpacing;
            }
        }

        /// <summary>沿地面把两点之间补成连续墨道（圆与圆按半径的 0.9 倍步距重叠）。</summary>
        private static void PaintGroundStreak(Vector3 from, Vector3 to, float radius, Team team, Action<int> onCellsPainted)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;

            if (distance < 0.0001f)
            {
                PaintGroundCircle(to, radius, team, onCellsPainted);
                return;
            }

            float spacing = Mathf.Max(0.12f, radius * 0.9f);
            int steps = Mathf.Min(16, Mathf.CeilToInt(distance / spacing));
            for (int i = 1; i <= steps; i++)
            {
                PaintGroundCircle(Vector3.Lerp(from, to, i / (float)steps), radius, team, onCellsPainted);
            }
        }

        /// <summary>
        /// 方向性拉伸爆斑：沿弹丸入射方向逐级收窄的圆链构成椭圆主瓣，再甩出星状小射线。
        /// 只使用 InkSurfaceController 现有公共 API，不越界改 Ink 系统。
        /// </summary>
        private static void PaintStretchBlob(
            Vector3 point,
            Vector3 surfaceNormal,
            Vector3 incomingDir,
            float radius,
            float stretch,
            int rayCount,
            Team team,
            Action<int> onCellsPainted)
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

            // 主瓣：尾部短、前部沿入射方向拉长
            PaintGroundCircle(point - tangent * (radius * 0.38f), radius * 0.52f, team, onCellsPainted);

            for (int i = 0; i < GroundStretchOffsets.Length; i++)
            {
                Vector3 blobPoint = point + tangent * (radius * GroundStretchOffsets[i] * stretch);
                PaintGroundCircle(blobPoint, radius * GroundStretchScales[i], team, onCellsPainted);
            }

            // 星状小射线
            for (int i = 0; i < rayCount; i++)
            {
                float angle = (i / (float)Mathf.Max(1, rayCount)) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.4f, 0.4f);
                Vector3 radial = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, normal) * tangent;
                float distance = radius * stretch * UnityEngine.Random.Range(0.7f, 1.15f);
                float rayRadius = radius * UnityEngine.Random.Range(0.10f, 0.20f);
                PaintGroundCircle(point + radial * distance, rayRadius, team, onCellsPainted);
            }
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

        private bool TryCheckImpact(
            in ActiveProjectile p,
            Vector3 dir,
            float stepDist,
            out RaycastHit bestHit,
            out CombatantHealth bestCombatant)
        {
            bestHit = default;
            bestCombatant = null;

            int count = Physics.SphereCastNonAlloc(p.Position, p.HitRadius, dir, _hitBuffer, stepDist, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0)
                return false;

            float closestDist = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hitBuffer[i];
                if (h.collider == null) continue;
                if (p.OwnerRoot != null && h.collider.transform.IsChildOf(p.OwnerRoot.transform))
                    continue;

                var combatant = h.collider.GetComponentInParent<CombatantHealth>();
                if (combatant != null)
                {
                    // 友方角色不挡己方子弹
                    if (combatant.Team == p.Team || !combatant.IsAlive)
                        continue;
                }

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

        private static void PaintGroundCircle(Vector3 pos, float radius, Team team, Action<int> onCellsPainted)
        {
            if (InkSurfaceController.Instance == null)
                return;

            int changed = InkSurfaceController.Instance.PaintCircle(pos, radius, team);
            if (changed > 0)
            {
                onCellsPainted?.Invoke(changed);
            }
        }

        private static bool TryProjectToGround(Vector3 worldPos, out Vector3 groundPoint)
        {
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                groundPoint = hit.point;
                return true;
            }

            groundPoint = new Vector3(worldPos.x, 0f, worldPos.z);
            return false;
        }

        private static Vector3 ProjectToGround(Vector3 worldPos, out Vector3 groundNormal)
        {
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
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
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"InkShot_{idx:00}";
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.32f, 0.24f, 0.42f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            go.SetActive(false);
            return go.transform;
        }
    }
}
