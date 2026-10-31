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
                Physics.Raycast(rayOrigin, flatDir.normalized, out RaycastHit wallHit, flatDir.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (wallHit.normal.y < 0.4f)
                {
                    targetGroundPos = wallHit.point - flatDir.normalized * 0.45f;
                }
            }

            if (Physics.Raycast(new Vector3(targetGroundPos.x, 8f, targetGroundPos.z), Vector3.down, out RaycastHit groundHit, 16f, ~0, QueryTriggerInteraction.Ignore))
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
        /// 按相机俯仰解算出手初速（inkwave throwVelocity 公式）：
        /// pitch = clamp(相机俯仰 + 偏移, 下限, 上限)；
        /// v = 水平瞄准方向 * cos(pitch) * 初速 + up * (sin(pitch) * 初速 + 上抛加成) + 0.4 * 携带速度。
        /// 远近完全由俯仰物理决定：平抛约 8m、满仰角最远约 10.5m、低头贴脚丢，无任何落点钳制。
        /// </summary>
        public static Vector3 ComputeThrowVelocity(
            Vector3 planarAimDirection,
            float cameraPitchDegrees,
            Vector3 carryVelocity,
            WeaponConfigSO config)
        {
            float speed = config != null && config.bombThrowSpeed > 0.1f ? config.bombThrowSpeed : 13.5f;
            float upBoost = config != null ? config.bombThrowUpBoost : 1.5f;
            float offsetRad = (config != null ? config.bombThrowPitchOffsetDegrees : 16f) * Mathf.Deg2Rad;
            float minRad = (config != null ? config.bombThrowPitchMinDegrees : -17.2f) * Mathf.Deg2Rad;
            float maxRad = (config != null ? config.bombThrowPitchMaxDegrees : 63f) * Mathf.Deg2Rad;

            float pitch = Mathf.Clamp(cameraPitchDegrees * Mathf.Deg2Rad + offsetRad, minRad, maxRad);

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
            firstHitPoint = startPos;
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

        /// <summary>
        /// 弹道学反解：给定固定初速与重力，求命中目标点的低弧出手速度（旧落点制契约专用）。
        /// 玩家链路走 <see cref="ComputeThrowVelocity"/>（俯仰决定远近）；本方法服务于
        /// Bot 与旧 API——「投到 targetGroundPos」的语义必须保持。
        /// 初速够不到目标时退化为 45° 全力抛（最大射程）。
        /// </summary>
        public static Vector3 ComputeThrowVelocityToLandAt(
            Vector3 startPos,
            Vector3 targetGroundPos,
            WeaponConfigSO config)
        {
            float speed = config != null && config.bombThrowSpeed > 0.1f ? config.bombThrowSpeed : 13.5f;
            float g = config != null && config.bombThrowGravity > 0.1f ? config.bombThrowGravity : 24f;

            Vector3 flat = targetGroundPos - startPos;
            flat.y = 0f;
            float d = flat.magnitude;
            Vector3 planar = d > 0.0001f ? flat / d : Vector3.forward;

            float v2 = speed * speed;
            float dy = targetGroundPos.y - startPos.y;
            float disc = v2 * v2 - g * (g * d * d + 2f * dy * v2);

            float sinTheta, cosTheta;
            if (d < 0.05f)
            {
                // 目标贴脚：以俯仰下限温柔丢出（反解在 d→0 时发散）
                float minRad = (config != null ? config.bombThrowPitchMinDegrees : -17.2f) * Mathf.Deg2Rad;
                sinTheta = Mathf.Sin(minRad);
                cosTheta = Mathf.Cos(minRad);
            }
            else if (disc < 0f)
            {
                // 初速够不到目标：45° 全力抛
                sinTheta = cosTheta = Mathf.Sqrt(0.5f);
            }
            else
            {
                // 低弧解（tanθ 取小根）：弹道更平、落地更快，贴近 inkwave 的利落手感
                float tanTheta = (v2 - Mathf.Sqrt(disc)) / (g * d);
                float denom = Mathf.Sqrt(1f + tanTheta * tanTheta);
                cosTheta = 1f / denom;
                sinTheta = tanTheta / denom;
            }

            return new Vector3(planar.x * cosTheta * speed, sinTheta * speed, planar.z * cosTheta * speed);
        }
        /// <summary>
        /// 单段弹道射线：命中最近的世界表面，跳过投掷者自身与触发器。
        /// 预览与实飞共用，保证反弹行为一致。
        /// </summary>
        public static bool CastWorldSegment(Vector3 origin, Vector3 direction, float distance, Transform ignoreRoot, out RaycastHit hit)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin, direction, s_segmentHits, distance, ~0, QueryTriggerInteraction.Ignore);
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
