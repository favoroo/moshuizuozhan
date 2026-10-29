using System.Collections.Generic;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// 纯 C# 导航网格：从物理场景采样可行走格与地表高度，提供 A* 寻路。
    /// 走 2D 格子 + 高度差限制，让 Bot 只能通过斜坡上下高台，不能直接爬上高台侧壁。
    /// 支持按格查询外部代价（墨水加权：己方 0.5 / 中性 1.0 / 敌方 3.0），
    /// 并在 A* 之后做「代价感知直线化」剪枝：能顺墨道冲刺就拉直，代价明显变差的捷径不取。
    /// </summary>
    public sealed class NavGrid
    {
        /// <summary>每秒/每格的地形代价倍率查询（由 Bot 侧按脚下墨水归属提供）。</summary>
        public delegate float TraversalCostProvider(int x, int y);

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector2 OriginXZ { get; }

        private readonly bool[] _walkable;
        private readonly float[] _surfaceY;

        private const float ProbeTopY = 12f;
        private const float ProbeDepth = 30f;

        // 直线化剪枝：只向前看 12 个路点，采样步长 0.25m，允许比原路径贵 25% 以内的直线
        private const int SmoothLookahead = 12;
        private const float SmoothSampleStep = 0.25f;
        private const float SmoothCostSlack = 1.25f;

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        public NavGrid(int width, int height, float cellSize, Vector2 originXZ)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            OriginXZ = originXZ;
            _walkable = new bool[width * height];
            _surfaceY = new float[width * height];
        }

        /// <summary>
        /// 用物理查询采样整张地图：有地面、坡度可站、且身体半径内无高障碍的格子才算可行走。
        /// </summary>
        public static NavGrid Build(Vector2 mapSize, float cellSize, float agentRadius, float agentHeight, LayerMask mask)
        {
            int w = Mathf.CeilToInt(mapSize.x / cellSize);
            int h = Mathf.CeilToInt(mapSize.y / cellSize);
            Vector2 origin = new Vector2(-mapSize.x * 0.5f, -mapSize.y * 0.5f);
            var grid = new NavGrid(w, h, cellSize, origin);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vector2 centerXZ = grid.CellToWorldXZ(x, y);
                    int idx = y * w + x;

                    if (!Physics.Raycast(new Vector3(centerXZ.x, ProbeTopY, centerXZ.y), Vector3.down,
                            out RaycastHit hit, ProbeDepth, mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    if (Vector3.Angle(hit.normal, Vector3.up) > 52f)
                        continue;

                    grid._surfaceY[idx] = hit.point.y;

                    // 身体胶囊内若有明显高于脚下地面的障碍，则视为不可行走
                    Vector3 bottom = hit.point + Vector3.up * (agentRadius + 0.06f);
                    Vector3 top = hit.point + Vector3.up * agentHeight;
                    int count = Physics.OverlapCapsuleNonAlloc(bottom, top, agentRadius * 0.92f,
                        OverlapBuffer, mask, QueryTriggerInteraction.Ignore);

                    bool blocked = false;
                    for (int i = 0; i < count; i++)
                    {
                        Collider c = OverlapBuffer[i];
                        if (c == null) continue;
                        // 角色自身碰撞体不应阻塞导航采样
                        if (c.GetComponentInParent<P1.Player.CombatantHealth>() != null) continue;
                        if (c.bounds.max.y > hit.point.y + 0.42f)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                    {
                        grid._walkable[idx] = true;
                    }
                }
            }

            return grid;
        }

        /// <summary>
        /// 直接用可行走掩码构建网格（不做物理采样）：供 EditMode 测试与离线工具使用。
        /// </summary>
        public static NavGrid CreateFromMask(int width, int height, float cellSize, Vector2 originXZ,
            bool[] walkable, float[] surfaceY = null)
        {
            var grid = new NavGrid(width, height, cellSize, originXZ);
            if (walkable != null)
            {
                System.Array.Copy(walkable, grid._walkable, Mathf.Min(walkable.Length, grid._walkable.Length));
            }
            if (surfaceY != null)
            {
                System.Array.Copy(surfaceY, grid._surfaceY, Mathf.Min(surfaceY.Length, grid._surfaceY.Length));
            }
            return grid;
        }

        public Vector2 CellToWorldXZ(int x, int y)
        {
            return new Vector2(
                OriginXZ.x + (x + 0.5f) * CellSize,
                OriginXZ.y + (y + 0.5f) * CellSize);
        }

        public bool TryWorldToCell(Vector3 world, out int x, out int y)
        {
            float nx = (world.x - OriginXZ.x) / CellSize;
            float ny = (world.z - OriginXZ.y) / CellSize;
            if (nx < 0f || ny < 0f || nx >= Width || ny >= Height)
            {
                x = -1;
                y = -1;
                return false;
            }
            x = Mathf.Clamp(Mathf.FloorToInt(nx), 0, Width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(ny), 0, Height - 1);
            return true;
        }

        public bool IsWalkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return _walkable[y * Width + x];
        }

        public float GetSurfaceY(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return 0f;
            return _surfaceY[y * Width + x];
        }

        public Vector3 CellToWorld(int x, int y)
        {
            Vector2 xz = CellToWorldXZ(x, y);
            return new Vector3(xz.x, GetSurfaceY(x, y), xz.y);
        }

        /// <summary>
        /// 把任意世界坐标吸附到最近的可行走格（用于起点/终点落在障碍里时的兜底）。
        /// </summary>
        public bool TryGetNearestWalkable(Vector3 world, out Vector3 result, int searchRadius = 12)
        {
            if (!TryWorldToCell(world, out int cx, out int cy))
            {
                // 场外坐标先夹到最近的边界格；否则会从 (-1,-1) 起搜，
                // 把越界点错误吸附到地图对角角落（撤退/涂地目标会因此指到反方向）。
                cx = Mathf.Clamp(Mathf.FloorToInt((world.x - OriginXZ.x) / CellSize), 0, Width - 1);
                cy = Mathf.Clamp(Mathf.FloorToInt((world.z - OriginXZ.y) / CellSize), 0, Height - 1);
            }

            if (IsWalkable(cx, cy))
            {
                result = CellToWorld(cx, cy);
                return true;
            }

            for (int r = 1; r <= searchRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (IsWalkable(x, y))
                        {
                            result = CellToWorld(x, y);
                            return true;
                        }
                    }
                }
            }

            result = world;
            return false;
        }

        /// <summary>
        /// A* 寻路。maxHeightStep 限制相邻格的高度差，防止 Bot 直接攀爬高台侧壁。
        /// </summary>
        public bool TryFindPath(Vector3 startWorld, Vector3 endWorld, List<Vector3> outPath, float maxHeightStep = 0.55f)
        {
            return TryFindPath(startWorld, endWorld, outPath, maxHeightStep, null, 1f);
        }

        /// <summary>
        /// 带地形代价的 A*：<paramref name="costProvider"/> 给每格返回一个代价倍率
        /// （己方墨水 0.5 → Bot 天然沿墨道高速切入；敌方墨水 3.0 → 自动绕开墨陷阱）。
        /// <paramref name="minCostMultiplier"/> 是代价倍率的下界，用于收缩启发函数保持可采纳性，
        /// 传 0（或负）时按 1 处理（等价于原版等权 A*）。
        /// </summary>
        public bool TryFindPath(Vector3 startWorld, Vector3 endWorld, List<Vector3> outPath,
            float maxHeightStep, TraversalCostProvider costProvider, float minCostMultiplier = 1f)
        {
            outPath.Clear();

            if (!TryGetNearestWalkable(startWorld, out Vector3 startSnap)) return false;
            if (!TryGetNearestWalkable(endWorld, out Vector3 endSnap)) return false;

            TryWorldToCell(startSnap, out int sx, out int sy);
            TryWorldToCell(endSnap, out int ex, out int ey);

            if (sx == ex && sy == ey)
            {
                outPath.Add(endSnap);
                return true;
            }

            int total = Width * Height;
            var gScore = new Dictionary<int, float>(total);
            var cameFrom = new Dictionary<int, int>(total);
            var closed = new HashSet<int>();
            var open = new MinHeap();

            int startIdx = sy * Width + sx;
            int goalIdx = ey * Width + ex;

            // 代价倍率可以小于 1，启发函数同步收缩才不会被高估（可采纳性）
            float heuristicScale = minCostMultiplier > 0f ? Mathf.Min(1f, minCostMultiplier) : 1f;

            gScore[startIdx] = 0f;
            open.Push(startIdx, Heuristic(sx, sy, ex, ey) * heuristicScale);

            bool found = false;
            int guard = 0;

            while (open.Count > 0 && guard++ < total * 4)
            {
                int current = open.Pop();
                if (!closed.Add(current)) continue;

                if (current == goalIdx)
                {
                    found = true;
                    break;
                }

                int cx = current % Width;
                int cy = current / Width;
                float cg = gScore[current];

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = cx + dx, ny = cy + dy;
                        if (!IsWalkable(nx, ny)) continue;

                        // 斜向移动不允许穿墙角
                        if (dx != 0 && dy != 0)
                        {
                            if (!IsWalkable(cx + dx, cy) || !IsWalkable(cx, cy + dy)) continue;
                        }

                        float stepHeight = GetSurfaceY(nx, ny) - GetSurfaceY(cx, cy);
                        if (Mathf.Abs(stepHeight) > maxHeightStep) continue;

                        float stepCost = (dx != 0 && dy != 0) ? 1.4142f : 1f;
                        if (costProvider != null)
                        {
                            stepCost *= Mathf.Max(0.05f, costProvider(nx, ny));
                        }
                        stepCost += Mathf.Max(0f, stepHeight) * 2.5f;

                        int nIdx = ny * Width + nx;
                        float tentative = cg + stepCost;

                        if (gScore.TryGetValue(nIdx, out float known) && tentative >= known) continue;

                        gScore[nIdx] = tentative;
                        cameFrom[nIdx] = current;
                        open.Push(nIdx, tentative + Heuristic(nx, ny, ex, ey) * heuristicScale);
                    }
                }
            }

            if (!found) return false;

            var reversed = new List<Vector3>();
            int walk = goalIdx;
            while (walk != startIdx)
            {
                int wx = walk % Width;
                int wy = walk / Width;
                reversed.Add(CellToWorld(wx, wy));
                if (!cameFrom.TryGetValue(walk, out walk)) break;
            }
            reversed.Add(startSnap);

            for (int i = reversed.Count - 1; i >= 0; i--)
            {
                outPath.Add(reversed[i]);
            }

            // 终点用真实目标点替换，避免吸附误差
            if (outPath.Count > 0)
            {
                outPath[outPath.Count - 1] = endSnap;
            }

            if (costProvider != null)
            {
                SmoothPath(outPath, maxHeightStep, costProvider);
            }

            return outPath.Count > 0;
        }

        /// <summary>
        /// 代价感知直线化：把 0.5m 网格的锯齿路点拉直成尽量少的长直线，走位立刻顺滑。
        /// 直线必须全程可行走且代价不显著变差，因此不会为了抄近道踩进敌方墨池。
        /// </summary>
        private void SmoothPath(List<Vector3> points, float maxHeightStep, TraversalCostProvider provider)
        {
            int count = points.Count;
            if (count <= 2) return;

            // 前缀代价：prefix[k] = 原路点从 0 走到 k 的墨水加权里程
            var prefix = new float[count];
            for (int i = 1; i < count; i++)
            {
                prefix[i] = prefix[i - 1] + EstimateSegmentCost(points[i - 1], points[i], provider);
            }

            var smoothed = new List<Vector3>(count);
            smoothed.Add(points[0]);

            int read = 0;
            while (read < count - 1)
            {
                int limit = Mathf.Min(count - 1, read + SmoothLookahead);
                int next = read + 1;

                for (int candidate = limit; candidate > read + 1; candidate--)
                {
                    float replacedCost = prefix[candidate] - prefix[read];
                    if (LineIsTraversable(points[read], points[candidate], maxHeightStep, provider,
                            replacedCost))
                    {
                        next = candidate;
                        break;
                    }
                }

                smoothed.Add(points[next]);
                read = next;
            }

            points.Clear();
            points.AddRange(smoothed);
        }

        /// <summary>用中点所在格的代价估算一段路点的墨水加权里程。</summary>
        private float EstimateSegmentCost(Vector3 from, Vector3 to, TraversalCostProvider provider)
        {
            float length = Vector3.Distance(from, to);
            if (length < 0.0001f) return 0f;

            Vector3 mid = (from + to) * 0.5f;
            float multiplier = 1f;
            if (TryWorldToCell(mid, out int cx, out int cy))
            {
                multiplier = Mathf.Max(0.05f, provider(cx, cy));
            }
            return length * multiplier;
        }

        /// <summary>
        /// 检查两点之间的直线是否可以整段直走，并且墨水代价不比被替换的原路径段显著更贵。
        /// </summary>
        private bool LineIsTraversable(Vector3 from, Vector3 to, float maxHeightStep,
            TraversalCostProvider provider, float replacedCost)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;

            float lineCost = 0f;
            int prevCell = -1;
            float prevY = 0f;

            int samples = Mathf.CeilToInt(distance / SmoothSampleStep);
            float sampleLength = distance / samples;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 p = Vector3.Lerp(from, to, t);
                if (!TryWorldToCell(p, out int cx, out int cy) || !IsWalkable(cx, cy))
                    return false;

                int cell = cy * Width + cx;
                if (cell != prevCell)
                {
                    float y = GetSurfaceY(cx, cy);
                    if (prevCell >= 0 && Mathf.Abs(y - prevY) > maxHeightStep)
                        return false;
                    prevCell = cell;
                    prevY = y;
                }

                lineCost += Mathf.Max(0.05f, provider(cx, cy)) * sampleLength;
            }

            return lineCost <= replacedCost * SmoothCostSlack + 0.6f;
        }


        private static float Heuristic(int ax, int ay, int bx, int by)
        {
            int dx = Mathf.Abs(ax - bx);
            int dy = Mathf.Abs(ay - by);
            return (dx + dy) + (1.4142f - 2f) * Mathf.Min(dx, dy);
        }

        /// <summary>极简二叉最小堆，避免 A* 每步排序。</summary>
        private sealed class MinHeap
        {
            private readonly List<int> _items = new List<int>();
            private readonly List<float> _keys = new List<float>();

            public int Count => _items.Count;

            public void Push(int item, float key)
            {
                _items.Add(item);
                _keys.Add(key);
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(parent, i);
                    i = parent;
                }
            }

            public int Pop()
            {
                int result = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _keys[0] = _keys[last];
                _items.RemoveAt(last);
                _keys.RemoveAt(last);

                int i = 0;
                int n = _items.Count;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < n && _keys[l] < _keys[smallest]) smallest = l;
                    if (r < n && _keys[r] < _keys[smallest]) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }

                return result;
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
            }
        }
    }
}
