using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// 墨水加权寻路的权重档位：己方墨道便宜（Bot 天生沿自家墨道高速切入），
    /// 中性地面为标准价，敌方墨水昂贵（自动绕开墨陷阱）。数值可在 <c>BotConfigSO</c> 调。
    /// </summary>
    [System.Serializable]
    public struct InkPathWeights
    {
        public float ownInk;
        public float neutral;
        public float enemyInk;

        public InkPathWeights(float ownInk, float neutral, float enemyInk)
        {
            this.ownInk = ownInk;
            this.neutral = neutral;
            this.enemyInk = enemyInk;
        }

        /// <summary>代价倍率下界，用于收缩启发函数（保持 A* 可采纳）。</summary>
        public float MinCost => Mathf.Max(0.05f, Mathf.Min(ownInk, Mathf.Min(neutral, enemyInk)));

        /// <summary>默认：己方 0.5 / 中性 1.0 / 敌方 3.0。</summary>
        public static InkPathWeights Default => new InkPathWeights(0.5f, 1f, 3f);
    }

    /// <summary>
    /// 场景级导航服务：启动时按地图与障碍物采样一次 NavGrid，供所有 Bot 共用。
    /// 额外提供「按队伍墨水归属加权」的寻路入口：Bot 不需要知道 InkGrid 的细节，
    /// 共享同一套格子采样，避免每个 Bot 各自持有一份墨水缓存。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ArenaNavigator : MonoBehaviour
    {
        public static ArenaNavigator Instance { get; private set; }

        [SerializeField] private Vector2 mapSize = new Vector2(40f, 40f);
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private float agentRadius = 0.42f;
        [SerializeField] private float agentHeight = 1.6f;
        [SerializeField] private LayerMask samplingMask = ~0;

        public NavGrid Grid { get; private set; }

        private InkSurfaceController _inkSurface;
        private InkGrid _cachedInk;
        private Team _cachedTeam;
        private InkPathWeights _cachedWeights;
        private NavGrid.TraversalCostProvider _cellCostProvider;

        public void Configure(Vector2 size, float cell, LayerMask mask)
        {
            mapSize = size;
            cellSize = cell;
            samplingMask = mask;
            Rebuild();
        }

        private void Awake()
        {
            Instance = this;
            _cellCostProvider = SampleCellCost;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Rebuild()
        {
            Grid = NavGrid.Build(mapSize, cellSize, agentRadius, agentHeight, samplingMask);

            int walkable = 0;
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    if (Grid.IsWalkable(x, y)) walkable++;
                }
            }

            Debug.Log($"[NavGrid] {Grid.Width}x{Grid.Height} 格（{cellSize}m/格），可行走 {walkable} 格");
        }

        /// <summary>
        /// 按 <paramref name="team"/> 的墨水归属加权寻路。没有墨水网格时自动退回等权 A*。
        /// </summary>
        public bool TryFindPath(Vector3 startWorld, Vector3 endWorld, System.Collections.Generic.List<Vector3> outPath,
            Team team, InkPathWeights weights, float maxHeightStep = 0.55f)
        {
            if (Grid == null) return false;

            if (_inkSurface == null)
                _inkSurface = InkSurfaceController.Instance;

            InkGrid ink = _inkSurface != null ? _inkSurface.Grid : null;
            if (ink == null)
                return Grid.TryFindPath(startWorld, endWorld, outPath, maxHeightStep);

            _cachedInk = ink;
            _cachedTeam = team;
            _cachedWeights = weights;

            if (_cellCostProvider == null)
                _cellCostProvider = SampleCellCost;

            return Grid.TryFindPath(startWorld, endWorld, outPath, maxHeightStep, _cellCostProvider, weights.MinCost);
        }

        private float SampleCellCost(int x, int y)
        {
            InkGrid ink = _cachedInk;
            if (ink == null || Grid == null) return 1f;

            Vector2 center = Grid.CellToWorldXZ(x, y);
            Team cellTeam = ink.InkAt(center);
            return BotTactics.ResolveInkPathCost(cellTeam, _cachedTeam,
                _cachedWeights.ownInk, _cachedWeights.neutral, _cachedWeights.enemyInk);
        }
    }
}
