using P1.Core;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 副武器「墨水手雷」弹道与落点统一数学解析工具：
    /// 保证实际飞行路径与 64 点弹道预测线算法 100% 相同、落点完全吻合。
    /// </summary>
    public static class InkBombTrajectoryUtility
    {
        public const int TrajectoryPointCount = 64;

        /// <summary>手雷抛物线参数（弧高 + 飞行时长），弹道预览与实际飞行必须共用，保证落点完全重合。</summary>
        public struct BombArcParams
        {
            public float ArcHeight;
            public float FlightDuration;
        }

        /// <summary>
        /// 严格根据起点、期望目标点和武器配置，计算最终落点（包含最大距离钳制、高墙截断、地面贴合）。
        /// </summary>
        public static Vector3 ResolveLandingPosition(Vector3 startPos, Vector3 targetGroundPos, WeaponConfigSO config)
        {
            Vector3 flatDelta = targetGroundPos - startPos;
            flatDelta.y = 0f;
            float maxDist = config != null ? config.bombMaxThrowDistance : 12f;
            if (flatDelta.magnitude > maxDist)
            {
                targetGroundPos = startPos + flatDelta.normalized * maxDist;
            }

            Vector3 rayOrigin = new Vector3(startPos.x, startPos.y + 0.3f, startPos.z);
            Vector3 flatDir = targetGroundPos - startPos;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.05f &&
                Physics.Raycast(rayOrigin, flatDir.normalized, out RaycastHit wallHit, flatDir.magnitude, GameLayers.QueryMask, QueryTriggerInteraction.Ignore))
            {
                if (wallHit.normal.y < 0.4f)
                {
                    targetGroundPos = wallHit.point - flatDir.normalized * 0.45f;
                }
            }

            if (Physics.Raycast(new Vector3(targetGroundPos.x, 8f, targetGroundPos.z), Vector3.down, out RaycastHit groundHit, 16f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                targetGroundPos.y = groundHit.point.y;
            }
            else
            {
                targetGroundPos.y = 0f;
            }

            return targetGroundPos;
        }

        /// <summary>
        /// 按落点距离动态解算弧高与飞行时长：远投弧线更高、飞行更久，近投干脆利落，
        /// 让「力度感」随瞄准距离变化（对齐 inkwave 的投掷弧线可读性）。
        /// 必须传入 ResolveLandingPosition 解析后的落点；弹道预览（SubWeaponThrowTrajectory）与
        /// 实际飞行（InkBombPool）共用本方法，保证弧线与落点 100% 重合。
        /// </summary>
        public static BombArcParams ComputeArcParams(Vector3 startPos, Vector3 landingPos, WeaponConfigSO config)
        {
            Vector3 flatDelta = landingPos - startPos;
            flatDelta.y = 0f;
            float dist = flatDelta.magnitude;

            float maxDist = config != null ? config.bombMaxThrowDistance : 12f;
            float arcNear = config != null ? config.bombArcHeightNear : 1.6f;
            float arcFar = config != null ? config.bombArcHeightFar : 3.8f;
            float speed = config != null && config.bombFlightSpeed > 0.1f ? config.bombFlightSpeed : 15f;

            float t = maxDist > 0.01f ? Mathf.Clamp01(dist / maxDist) : 1f;
            return new BombArcParams
            {
                ArcHeight = Mathf.Lerp(arcNear, arcFar, t),
                // 0.28s 下限防贴脸瞬移，保留出手与落点的可读性
                FlightDuration = Mathf.Max(0.28f, dist / speed),
            };
        }

        /// <summary>
        /// 在归一化时间 t ∈ [0, 1] 处计算抛物线上的世界三维点。
        /// 采用与 InkBombPool 相同的平滑抛物线解析公式：y(t) = lerp(y0, y1, t) + 4 * H * t * (1 - t)
        /// </summary>
        public static Vector3 EvaluateArcPoint(Vector3 startPos, Vector3 landingPos, float arcHeight, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 pos = Vector3.Lerp(startPos, landingPos, t);
            pos.y += 4f * arcHeight * t * (1f - t);
            return pos;
        }

        /// <summary>
        /// 生成 64 点平滑抛物线轨迹点序列。
        /// </summary>
        public static void GenerateTrajectoryPoints(
            Vector3 startPos,
            Vector3 landingPos,
            float arcHeight,
            Vector3[] outPoints)
        {
            if (outPoints == null || outPoints.Length == 0) return;
            int count = outPoints.Length;
            if (count == 1)
            {
                outPoints[0] = startPos;
                return;
            }

            float step = 1f / (count - 1);
            for (int i = 0; i < count; i++)
            {
                float t = i * step;
                outPoints[i] = EvaluateArcPoint(startPos, landingPos, arcHeight, t);
            }
        }

        // ------------------------------------------------------------------
        // inkwave 弹道模型（真实物理抛掷）：预览 = 同一套积分器的仿真，所见即所得
        // ------------------------------------------------------------------

        /// <summary>仿真固定步长（秒）：与 inkwave 的 60Hz 半隐式欧拉积分一致。</summary>
        public const float SimDt = 1f / 60f;
        /// <summary>每 2 个积分步记录 1 个预览顶点（inkwave updateArc 同参数）。</summary>
        public const int SimStepsPerVertex = 2;
        /// <summary>碰撞反射系数：vn' = -vn * 1.35（inkwave 炸弹撞墙/落地同参数）。</summary>
        public const float BounceReflect = 1.35f;
        /// <summary>落地（法线朝上）后的速度衰减：0.45，几乎原地弹起。</summary>
        public const float BounceDampFloor = 0.45f;
        /// <summary>撞墙（法线不朝上）后的速度衰减：0.6，保留大部分反弹动能。</summary>
        public const float BounceDampWall = 0.6f;

        /// <summary>
        /// P1 相机俯仰（TopDownCameraFollow.PitchDegrees，正数 = 低头俯视）换算成
        /// inkwave 的瞄准俯仰（正数 = 抬头仰视）。<see cref="ComputeThrowVelocity"/> 的 pitch
        /// 参数用的是后者；直接把相机俯仰传进去会让「抬头」变成负仰角、手雷砸脚，必须经过本方法。
        /// </summary>
        public static float CameraPitchToAimPitchDegrees(float cameraPitchDegrees)
        {
            return -cameraPitchDegrees;
        }

        /// <summary>
        /// 按瞄准俯仰解算出手初速（inkwave throwVelocity 公式）：
        /// pitch = clamp(瞄准俯仰 + 偏移, 下限, 上限)，瞄准俯仰正数 = 抬头；
        /// v = 水平瞄准方向 * cos(pitch) * 初速 + up * (sin(pitch) * 初速 + 上抛加成) + 0.4 * 携带速度。
        /// 远近完全由俯仰物理决定：平视约 8m、抬头约 30° 接近最远约 10m、低头贴脚丢，无任何落点钳制。
        /// 相机链路必须先经 <see cref="CameraPitchToAimPitchDegrees"/> 翻符号（两套俯仰约定相反）。
        /// </summary>
        public static Vector3 ComputeThrowVelocity(
            Vector3 planarAimDirection,
            float aimPitchDegrees,
            Vector3 carryVelocity,
            WeaponConfigSO config)
        {
            float speed = config != null && config.bombThrowSpeed > 0.1f ? config.bombThrowSpeed : 13.5f;
            float upBoost = config != null ? config.bombThrowUpBoost : 1.5f;
            float offsetRad = (config != null ? config.bombThrowPitchOffsetDegrees : 16f) * Mathf.Deg2Rad;
            float minRad = (config != null ? config.bombThrowPitchMinDegrees : -17.2f) * Mathf.Deg2Rad;
            float maxRad = (config != null ? config.bombThrowPitchMaxDegrees : 63f) * Mathf.Deg2Rad;

            float pitch = Mathf.Clamp(aimPitchDegrees * Mathf.Deg2Rad + offsetRad, minRad, maxRad);

            Vector3 flat = planarAimDirection;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;

            float cp = Mathf.Cos(pitch);
            Vector3 velocity = new Vector3(
                flat.x * cp * speed + carryVelocity.x * 0.4f,
                Mathf.Sin(pitch) * speed + upBoost,
                flat.z * cp * speed + carryVelocity.z * 0.4f);
            return velocity;
        }

        /// <summary>
        /// 预览仿真：用与 InkBombPool 实飞完全相同的常数（初速、重力、反弹）做固定步长积分，
        /// 每 2 步记录 1 个顶点，首个碰撞点即预测落点（inkwave updateArc 做法）。
        /// 未写满的缓冲区尾部全部折叠为最后一个点，调用方可整段交给 LineRenderer。
        /// 返回 true 表示在预览窗口内落到了表面上（ring 才显示）；ignoreRoot 为投掷者根节点（穿透自身）。
        /// </summary>
        public static bool SimulateThrow(
            Vector3 startPos,
            Vector3 velocity,
            WeaponConfigSO config,
            Transform ignoreRoot,
            Vector3[] outPoints,
            out Vector3 firstHitPoint)
        {
            return SimulateThrow(
                startPos, velocity, config, ignoreRoot, outPoints, out firstHitPoint, out _);
        }

        /// <summary>
        /// 同 <see cref="SimulateThrow(Vector3,Vector3,WeaponConfigSO,Transform,Vector3[],out Vector3)"/>，
        /// 额外输出首个命中面的法线：落点预警环按法线贴放，打墙时不再悬空水平放置。
        /// </summary>
        public static bool SimulateThrow(
            Vector3 startPos,
            Vector3 velocity,
            WeaponConfigSO config,
            Transform ignoreRoot,
            Vector3[] outPoints,
            out Vector3 firstHitPoint,
            out Vector3 firstHitNormal)
        {
            firstHitPoint = startPos;
            firstHitNormal = Vector3.up;
            if (outPoints == null || outPoints.Length == 0)
                return false;

            float gravity = config != null && config.bombThrowGravity > 0.1f ? config.bombThrowGravity : 24f;
            Vector3 pos = startPos;
            Vector3 vel = velocity;
            int n = 0;
            outPoints[n++] = pos;

            int maxSteps = (outPoints.Length - 1) * SimStepsPerVertex;
            bool landed = false;
            for (int step = 0; step < maxSteps; step++)
            {
                Vector3 prev = pos;
                vel.y -= gravity * SimDt;
                pos += vel * SimDt;

                Vector3 delta = pos - prev;
                float dist = delta.magnitude;
                if (dist > 0.0001f && CastWorldSegment(prev, delta / dist, dist, ignoreRoot, out RaycastHit hit))
                {
                    pos = hit.point;
                    firstHitPoint = hit.point;
                    firstHitNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
                    landed = true;
                    break;
                }

                if ((step + 1) % SimStepsPerVertex == 0)
                {
                    outPoints[n++] = pos;
                    if (n >= outPoints.Length)
                        break;
                }
            }

            for (int i = n; i < outPoints.Length; i++)
            {
                outPoints[i] = pos;
            }
            return landed;
        }

        /// <summary>准星投掷的最近水平距离（米）：目标贴脚时也留出一点前抛距离，避免垂直砸脚。</summary>
        public const float MinThrowFlatDistance = 0.9f;

        /// <summary>
        /// 把「准星命中的世界点」解析成手雷实际要砸的目标点：
        /// 水平距离钳制在 bombMaxThrowDistance 内；超距（准星指向天空/射程外）时取最远水平落点并贴地，
        /// 避免手雷飞向空中。近距保底 <see cref="MinThrowFlatDistance"/>。
        /// </summary>
        public static Vector3 ResolveThrowTarget(Vector3 startPos, Vector3 aimPoint, WeaponConfigSO config)
        {
            float maxDist = config != null && config.bombMaxThrowDistance > 0.1f
                ? config.bombMaxThrowDistance
                : 22f;
            maxDist = Mathf.Max(maxDist, MinThrowFlatDistance);

            Vector3 flat = aimPoint - startPos;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 planar = distance > 0.0001f ? flat / distance : Vector3.forward;
            bool clamped = distance > maxDist;
            float targetDistance = Mathf.Clamp(distance, MinThrowFlatDistance, maxDist);

            Vector3 target = startPos + planar * targetDistance;
            target.y = aimPoint.y;

            if (clamped)
            {
                // 准星指向天空 / 射程外：水平取最远点后向下找地面，落点仍然可读
                if (Physics.Raycast(
                        new Vector3(target.x, startPos.y + 8f, target.z),
                        Vector3.down,
                        out RaycastHit groundHit,
                        40f,
                        GameLayers.WorldMask,
                        QueryTriggerInteraction.Ignore))
                {
                    target.y = groundHit.point.y;
                }
                else
                {
                    target.y = startPos.y - 0.5f;
                }
            }

            return target;
        }

        /// <summary>
        /// 弹道学反解：按固定重力求「精确命中目标点」的出手速度（指哪落哪）。
        /// 飞行时间随水平距离在 bombThrowFlightTimeNear/Far 间插值：近投干脆、远投有滞空弧线，
        /// 再由 T 反推水平速度与垂直初速，确保落点与准星一致。
        /// 这是玩家链路与 Bot 共用的落点制入口；旧的俯仰制 <see cref="ComputeThrowVelocity"/> 保留兼容。
        /// </summary>
        public static Vector3 ComputeThrowVelocityToLandAt(
            Vector3 startPos,
            Vector3 targetGroundPos,
            WeaponConfigSO config,
            out float flightTime)
        {
            float g = config != null && config.bombThrowGravity > 0.1f ? config.bombThrowGravity : 24f;
            float maxDist = config != null && config.bombMaxThrowDistance > 0.1f
                ? config.bombMaxThrowDistance
                : 22f;
            maxDist = Mathf.Max(maxDist, MinThrowFlatDistance);
            float nearTime = config != null && config.bombThrowFlightTimeNear > 0.01f
                ? config.bombThrowFlightTimeNear
                : 0.5f;
            float farTime = config != null && config.bombThrowFlightTimeFar > nearTime
                ? config.bombThrowFlightTimeFar
                : Mathf.Max(nearTime, 1.2f);

            Vector3 flat = targetGroundPos - startPos;
            flat.y = 0f;
            float distance = flat.magnitude;
            Vector3 planar = distance > 0.0001f ? flat / distance : Vector3.forward;

            // 超距目标：只保留最大水平距离（垂直目标高度保留，供高台/墙面落点）
            if (distance > maxDist)
            {
                distance = maxDist;
                targetGroundPos = new Vector3(
                    startPos.x + planar.x * maxDist,
                    targetGroundPos.y,
                    startPos.z + planar.z * maxDist);
            }

            // 贴脚目标：水平距离保底，方向沿准星水平朝向
            if (distance < MinThrowFlatDistance)
            {
                distance = MinThrowFlatDistance;
                targetGroundPos = new Vector3(
                    startPos.x + planar.x * distance,
                    targetGroundPos.y,
                    startPos.z + planar.z * distance);
            }

            float t = Mathf.Clamp01(distance / maxDist);
            flightTime = Mathf.Lerp(nearTime, farTime, t);

            float dy = targetGroundPos.y - startPos.y;
            float vy = (dy + 0.5f * g * flightTime * flightTime) / flightTime;

            // 预览仿真与实飞都走半隐式欧拉（先加速度后位移），同一步长下弹道会比解析解略高；
            // 这里预扣半步重力，保证「预览线落点 = 准星点」而不是系统性偏近/偏高。
            vy -= 0.5f * g * SimDt;

            float horizontalSpeed = distance / flightTime;
            return new Vector3(
                planar.x * horizontalSpeed,
                vy,
                planar.z * horizontalSpeed);
        }

        /// <summary>旧落点制签名（Bot / 既有测试）：忽略飞行时间输出。</summary>
        public static Vector3 ComputeThrowVelocityToLandAt(
            Vector3 startPos,
            Vector3 targetGroundPos,
            WeaponConfigSO config)
        {
            return ComputeThrowVelocityToLandAt(startPos, targetGroundPos, config, out _);
        }
        /// <summary>
        /// 单段弹道射线：命中最近的世界表面，跳过投掷者自身与触发器。
        /// 预览与实飞共用，保证反弹行为一致。
        /// </summary>
        public static bool CastWorldSegment(Vector3 origin, Vector3 direction, float distance, Transform ignoreRoot, out RaycastHit hit)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin, direction, s_segmentHits, distance, GameLayers.QueryMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            hit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = s_segmentHits[i];
                if (candidate.collider == null || candidate.distance > nearest)
                    continue;
                if (ignoreRoot != null && candidate.collider.transform.root == ignoreRoot)
                    continue;
                nearest = candidate.distance;
                hit = candidate;
                found = true;
            }
            return found;
        }

        private static readonly RaycastHit[] s_segmentHits = new RaycastHit[8];
    }
}
