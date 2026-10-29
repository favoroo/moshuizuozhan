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

        /// <summary>
        /// 严格根据起点、期望目标点和武器配置，计算最终落点（包含最大距离钳制、高墙截断、地面贴合）。
        /// </summary>
        public static Vector3 ResolveLandingPosition(Vector3 startPos, Vector3 targetGroundPos, WeaponConfigSO config)
        {
            Vector3 flatDelta = targetGroundPos - startPos;
            flatDelta.y = 0f;
            float maxDist = config != null ? config.bombMaxThrowDistance : 7.5f;
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
    }
}
