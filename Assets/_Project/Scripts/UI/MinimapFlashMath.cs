using System.Collections.Generic;
using P1.Core;
using P1.Environment;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 小地图占领闪光事件载荷（零 GC 纯值结构体）。
    /// </summary>
    public struct MinimapFlashEvent
    {
        public Vector3 WorldPos;
        public Team Team;
        public float Timestamp;
        public float Radius;

        public MinimapFlashEvent(Vector3 worldPos, Team team, float timestamp, float radius = 0f)
        {
            WorldPos = worldPos;
            Team = team;
            Timestamp = timestamp;
            Radius = radius;
        }
    }

    /// <summary>
    /// 小地图占领闪光层（Flash Layer）纯数学与过滤算法集合（零 GC、无 MonoBehaviour 依赖）。
    /// 对标 INKWAVE 小地图第 3 层「刚被占领的地块发光闪烁 0.4s 淡出」。
    /// </summary>
    public static class MinimapFlashMath
    {
        /// <summary>闪光持续时间（秒）：对标 INKWAVE 0.4s 淡出。</summary>
        public const float DefaultLifetime = 0.4f;

        /// <summary>初始不透明度：0.9。</summary>
        public const float InitialAlpha = 0.9f;

        /// <summary>节流频率 6Hz（约 0.1667 秒每批次）。</summary>
        public const float ThrottleInterval = 1f / 6f;

        /// <summary>单批处理上限。</summary>
        public const int DefaultBatchLimit = 64;

        /// <summary>邻近合并距离阈值（米）：世界坐标 XZ 距离小于此值视作同一块落墨区域。</summary>
        public const float DefaultMergeDistance = 1.5f;

        /// <summary>去重时间窗口（秒）：在此时间差内的近距离落墨进行合并。</summary>
        public const float DefaultDedupeWindow = 0.25f;

        /// <summary>
        /// 把世界 XZ 坐标映射到 [0, 1] 归一化小地图坐标系。
        /// </summary>
        public static Vector2 WorldToNormalized(Vector3 worldPos, Vector2 originXZ, Vector2 mapSizeWorld)
        {
            if (mapSizeWorld.x <= 0.0001f || mapSizeWorld.y <= 0.0001f)
                return new Vector2(0.5f, 0.5f);

            float nx = Mathf.Clamp01((worldPos.x - originXZ.x) / mapSizeWorld.x);
            float ny = Mathf.Clamp01((worldPos.z - originXZ.y) / mapSizeWorld.y);
            return new Vector2(nx, ny);
        }

        /// <summary>
        /// 基于竞技场单一数据源 ArenaLayoutV2 的默认中心与尺寸换算归一化坐标。
        /// </summary>
        public static Vector2 WorldToNormalized(Vector3 worldPos)
        {
            Vector2 origin = new Vector2(-ArenaLayoutV2.HalfX, -ArenaLayoutV2.HalfZ);
            Vector2 mapSize = ArenaLayoutV2.MapSize;
            return WorldToNormalized(worldPos, origin, mapSize);
        }

        /// <summary>
        /// 归一化坐标映射到小地图容器 AnchoredPosition（以容器中心为原点）。
        /// 与 MinimapMath.WorldToMapAnchoredPosition 换算公式保持严格一致。
        /// </summary>
        public static Vector2 NormalizedToAnchoredPosition(Vector2 normalizedPos, Vector2 containerSize)
        {
            return new Vector2(
                (normalizedPos.x - 0.5f) * containerSize.x,
                (normalizedPos.y - 0.5f) * containerSize.y);
        }

        /// <summary>
        /// 世界空间坐标直接映射为小地图容器 AnchoredPosition。
        /// </summary>
        public static Vector2 WorldToMapAnchoredPosition(
            Vector3 worldPos,
            Vector2 originXZ,
            Vector2 mapSizeWorld,
            Vector2 containerSize)
        {
            Vector2 norm = WorldToNormalized(worldPos, originXZ, mapSizeWorld);
            return NormalizedToAnchoredPosition(norm, containerSize);
        }

        /// <summary>
        /// 基于 ArenaLayoutV2 默认几何的世界空间坐标直接映射。
        /// </summary>
        public static Vector2 WorldToMapAnchoredPosition(Vector3 worldPos, Vector2 containerSize)
        {
            Vector2 norm = WorldToNormalized(worldPos);
            return NormalizedToAnchoredPosition(norm, containerSize);
        }

        /// <summary>
        /// 6Hz 节流判定：判断是否达到批量刷新阈值。
        /// </summary>
        public static bool ShouldFlush(float currentTime, float lastFlushTime, float interval = ThrottleInterval)
        {
            if (currentTime < lastFlushTime) return true; // 时钟倒流或场景重置
            return (currentTime - lastFlushTime) >= interval;
        }

        /// <summary>
        /// 计算闪光衰减曲线（从 initialAlpha 到 0）。
        /// </summary>
        public static float EvaluateAlpha(float elapsed, float lifetime = DefaultLifetime, float initialAlpha = InitialAlpha)
        {
            if (lifetime <= 0.0001f || elapsed < 0f || elapsed >= lifetime)
                return 0f;

            float t = elapsed / lifetime;
            return Mathf.Clamp01(initialAlpha * (1f - t));
        }

        /// <summary>
        /// 邻近去重判定：同阵营、短时间窗口内、世界坐标距离相近的事件应合并。
        /// </summary>
        public static bool ShouldMerge(
            Vector3 posA, float timeA, Team teamA,
            Vector3 posB, float timeB, Team teamB,
            float maxDistance = DefaultMergeDistance,
            float maxTimeDiff = DefaultDedupeWindow)
        {
            if (teamA != teamB) return false;
            if (Mathf.Abs(timeA - timeB) > maxTimeDiff) return false;

            float dx = posA.x - posB.x;
            float dz = posA.z - posB.z;
            return (dx * dx + dz * dz) <= (maxDistance * maxDistance);
        }

        /// <summary>
        /// 批量去重与上限截断（零 GC，使用预分配列表）。
        /// </summary>
        public static int DeduplicateAndLimit(
            IReadOnlyList<MinimapFlashEvent> input,
            IList<MinimapFlashEvent> output,
            int maxCount = DefaultBatchLimit,
            float maxDistance = DefaultMergeDistance,
            float maxTimeDiff = DefaultDedupeWindow)
        {
            if (input == null || output == null || maxCount <= 0) return 0;

            int addedCount = 0;
            for (int i = 0; i < input.Count && addedCount < maxCount; i++)
            {
                var candidate = input[i];
                bool merged = false;

                for (int j = 0; j < addedCount; j++)
                {
                    var existing = output[j];
                    if (ShouldMerge(
                        existing.WorldPos, existing.Timestamp, existing.Team,
                        candidate.WorldPos, candidate.Timestamp, candidate.Team,
                        maxDistance, maxTimeDiff))
                    {
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    if (addedCount < output.Count)
                    {
                        output[addedCount] = candidate;
                    }
                    else
                    {
                        output.Add(candidate);
                    }
                    addedCount++;
                }
            }

            return addedCount;
        }

        /// <summary>
        /// 向已有列表追加事件并进行去重，防止连续弹幕刷屏溢出（零 GC）。
        /// </summary>
        public static bool TryAddWithDeduplication(
            IList<MinimapFlashEvent> list,
            MinimapFlashEvent item,
            int maxCapacity = DefaultBatchLimit,
            float maxDistance = DefaultMergeDistance,
            float maxTimeDiff = DefaultDedupeWindow)
        {
            if (list == null || list.Count >= maxCapacity) return false;

            for (int i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (ShouldMerge(
                    existing.WorldPos, existing.Timestamp, existing.Team,
                    item.WorldPos, item.Timestamp, item.Team,
                    maxDistance, maxTimeDiff))
                {
                    return false;
                }
            }

            list.Add(item);
            return true;
        }
    }
}
