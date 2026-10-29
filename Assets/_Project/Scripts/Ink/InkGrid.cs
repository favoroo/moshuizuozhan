using System;
using P1.Core;
using UnityEngine;

namespace P1.Ink
{
    /// <summary>
    /// 纯 C# 涂地网格核心。支持世界坐标 XZ 映射、圆形涂色、掩体不可涂格标记、增量覆盖率统计与零 GC 像素缓冲导出。
    /// </summary>
    public sealed class InkGrid
    {
        public int Width { get; }
        public int Height { get; }
        public Vector2 MapSize { get; }
        public Vector2 OriginXZ { get; }
        public float CellWidth { get; }
        public float CellHeight { get; }

        private readonly byte[] _owners;
        private readonly bool[] _blocked;
        private readonly Color32[] _pixels;

        private Color32 _neutralColor;
        private Color32 _teamAColor;
        private Color32 _teamBColor;
        private Color32 _blockedColor;

        private int _paintableCount;
        private int _teamACount;
        private int _teamBCount;

        public int PaintableCount => _paintableCount;
        public int TeamACount => _teamACount;
        public int TeamBCount => _teamBCount;
        public bool IsDirty { get; private set; }

        public InkGrid(
            int width,
            int height,
            Vector2 mapSize,
            Vector2? originXZ = null,
            Color32? neutralColor = null,
            Color32? teamAColor = null,
            Color32? teamBColor = null,
            Color32? blockedColor = null)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (mapSize.x <= 0f || mapSize.y <= 0f) throw new ArgumentOutOfRangeException(nameof(mapSize));

            Width = width;
            Height = height;
            MapSize = mapSize;
            OriginXZ = originXZ ?? new Vector2(-mapSize.x * 0.5f, -mapSize.y * 0.5f);
            CellWidth = mapSize.x / width;
            CellHeight = mapSize.y / height;

            int total = width * height;
            _owners = new byte[total];
            _blocked = new bool[total];
            _pixels = new Color32[total];

            _neutralColor = neutralColor ?? new Color32(56, 61, 71, 0);
            _teamAColor = teamAColor ?? new Color32(255, 112, 15, 255);
            _teamBColor = teamBColor ?? new Color32(38, 107, 255, 255);
            _blockedColor = blockedColor ?? new Color32(25, 28, 36, 180);

            _paintableCount = total;
            ResetPaintOnly();
        }

        public void SetPalette(Color32 neutral, Color32 teamA, Color32 teamB, Color32 blocked)
        {
            _neutralColor = neutral;
            _teamAColor = teamA;
            _teamBColor = teamB;
            _blockedColor = blocked;
            RebuildAllPixels();
        }

        /// <summary>
        /// 清空所有墨水归属，但保留已标记的不可涂障碍物（用于重开比赛）。
        /// </summary>
        public void ResetPaintOnly()
        {
            Array.Clear(_owners, 0, _owners.Length);
            _teamACount = 0;
            _teamBCount = 0;

            for (int i = 0; i < _owners.Length; i++)
            {
                _pixels[i] = _blocked[i] ? _blockedColor : _neutralColor;
            }

            IsDirty = true;
        }

        /// <summary>
        /// 同时清空障碍物与墨水。
        /// </summary>
        public void ClearAll()
        {
            Array.Clear(_blocked, 0, _blocked.Length);
            _paintableCount = _owners.Length;
            ResetPaintOnly();
        }

        /// <summary>
        /// 将世界坐标 XZ 矩形区域标记为不可涂（墙体、出生点安全区）。
        /// </summary>
        public void SetBlockedWorldRect(Vector2 minXZ, Vector2 maxXZ, bool blocked = true)
        {
            WorldToCellClamped(minXZ, out int minX, out int minY);
            WorldToCellClamped(maxXZ, out int maxX, out int maxY);

            if (minX > maxX) (minX, maxX) = (maxX, minX);
            if (minY > maxY) (minY, maxY) = (maxY, minY);

            for (int y = minY; y <= maxY; y++)
            {
                int rowOffset = y * Width;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = rowOffset + x;
                    float cx = OriginXZ.x + (x + 0.5f) * CellWidth;
                    float cz = OriginXZ.y + (y + 0.5f) * CellHeight;
                    if (cx < minXZ.x || cx > maxXZ.x || cz < minXZ.y || cz > maxXZ.y)
                        continue;

                    SetCellBlocked(idx, blocked);
                }
            }
        }

        private void SetCellBlocked(int idx, bool blocked)
        {
            if (_blocked[idx] == blocked)
                return;

            if (blocked)
            {
                Team prev = (Team)_owners[idx];
                if (prev == Team.TeamA) _teamACount--;
                else if (prev == Team.TeamB) _teamBCount--;

                _owners[idx] = (byte)Team.Neutral;
                _blocked[idx] = true;
                _paintableCount--;
                _pixels[idx] = _blockedColor;
            }
            else
            {
                _blocked[idx] = false;
                _paintableCount++;
                _pixels[idx] = _neutralColor;
            }

            IsDirty = true;
        }

        /// <summary>
        /// 在世界坐标 XZ 处涂一个半径为 radius 的圆斑。
        /// 返回本次新涂或改写归属的格子数量（可直接用于大招充能）。
        /// </summary>
        public int PaintCircle(Vector2 centerXZ, float radius, Team team)
        {
            if (radius <= 0f || team == Team.Neutral)
                return 0;

            Vector2 minXZ = new Vector2(centerXZ.x - radius, centerXZ.y - radius);
            Vector2 maxXZ = new Vector2(centerXZ.x + radius, centerXZ.y + radius);

            if (maxXZ.x < OriginXZ.x || minXZ.x > OriginXZ.x + MapSize.x ||
                maxXZ.y < OriginXZ.y || minXZ.y > OriginXZ.y + MapSize.y)
            {
                return 0;
            }

            WorldToCellClamped(minXZ, out int minX, out int minY);
            WorldToCellClamped(maxXZ, out int maxX, out int maxY);

            float rSq = radius * radius;
            byte targetByte = (byte)team;
            Color32 baseColor = team == Team.TeamA ? _teamAColor : _teamBColor;
            int changed = 0;

            for (int y = minY; y <= maxY; y++)
            {
                float cz = OriginXZ.y + (y + 0.5f) * CellHeight;
                float dz = cz - centerXZ.y;
                float dzSq = dz * dz;
                int rowOffset = y * Width;

                for (int x = minX; x <= maxX; x++)
                {
                    int idx = rowOffset + x;
                    if (_blocked[idx])
                        continue;

                    float cx = OriginXZ.x + (x + 0.5f) * CellWidth;
                    float dx = cx - centerXZ.x;
                    if (dx * dx + dzSq > rSq)
                        continue;

                    byte prev = _owners[idx];
                    if (prev == targetByte)
                        continue;

                    if (prev == (byte)Team.TeamA) _teamACount--;
                    else if (prev == (byte)Team.TeamB) _teamBCount--;

                    _owners[idx] = targetByte;
                    if (team == Team.TeamA) _teamACount++;
                    else if (team == Team.TeamB) _teamBCount++;

                    _pixels[idx] = TintCellColor(baseColor, x, y);
                    changed++;
                }
            }

            if (changed > 0)
            {
                IsDirty = true;
            }

            return changed;
        }

        public int PaintCircle(Vector3 worldPos, float radius, Team team)
        {
            return PaintCircle(new Vector2(worldPos.x, worldPos.z), radius, team);
        }

        public Team InkAt(Vector2 worldXZ)
        {
            if (!TryWorldToCell(worldXZ, out int x, out int y))
                return Team.Neutral;

            int idx = y * Width + x;
            if (_blocked[idx])
                return Team.Neutral;

            return (Team)_owners[idx];
        }

        public Team InkAt(Vector3 worldPos)
        {
            return InkAt(new Vector2(worldPos.x, worldPos.z));
        }

        public bool IsBlockedAt(Vector2 worldXZ)
        {
            if (!TryWorldToCell(worldXZ, out int x, out int y))
                return true;

            return _blocked[y * Width + x];
        }

        public int GetTeamCount(Team team)
        {
            return team switch
            {
                Team.TeamA => _teamACount,
                Team.TeamB => _teamBCount,
                _ => Math.Max(0, _paintableCount - _teamACount - _teamBCount)
            };
        }

        public float GetCoverage01(Team team)
        {
            if (_paintableCount <= 0)
                return 0f;

            return Mathf.Clamp01((float)GetTeamCount(team) / _paintableCount);
        }

        public float GetCoveragePercent(Team team)
        {
            return GetCoverage01(team) * 100f;
        }

        public Color32[] GetPixelBuffer() => _pixels;

        public void ClearDirtyFlag()
        {
            IsDirty = false;
        }

        public bool TryWorldToCell(Vector2 worldXZ, out int x, out int y)
        {
            float nx = (worldXZ.x - OriginXZ.x) / MapSize.x;
            float ny = (worldXZ.y - OriginXZ.y) / MapSize.y;

            if (nx < 0f || nx >= 1f || ny < 0f || ny >= 1f)
            {
                x = -1;
                y = -1;
                return false;
            }

            x = Mathf.Clamp(Mathf.FloorToInt(nx * Width), 0, Width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(ny * Height), 0, Height - 1);
            return true;
        }

        public Vector2 CellToWorldCenterXZ(int x, int y)
        {
            return new Vector2(
                OriginXZ.x + (x + 0.5f) * CellWidth,
                OriginXZ.y + (y + 0.5f) * CellHeight);
        }

        private void WorldToCellClamped(Vector2 worldXZ, out int x, out int y)
        {
            float nx = (worldXZ.x - OriginXZ.x) / MapSize.x;
            float ny = (worldXZ.y - OriginXZ.y) / MapSize.y;

            x = Mathf.Clamp(Mathf.FloorToInt(nx * Width), 0, Width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(ny * Height), 0, Height - 1);
        }

        private void RebuildAllPixels()
        {
            for (int y = 0; y < Height; y++)
            {
                int rowOffset = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int idx = rowOffset + x;
                    if (_blocked[idx])
                    {
                        _pixels[idx] = _blockedColor;
                        continue;
                    }

                    Team t = (Team)_owners[idx];
                    _pixels[idx] = t switch
                    {
                        Team.TeamA => TintCellColor(_teamAColor, x, y),
                        Team.TeamB => TintCellColor(_teamBColor, x, y),
                        _ => _neutralColor
                    };
                }
            }

            IsDirty = true;
        }

        private static Color32 TintCellColor(Color32 c, int x, int y)
        {
            int hash = ((x * 73856093) ^ (y * 19349663)) & 7;
            int delta = hash - 3;
            return new Color32(
                (byte)Mathf.Clamp(c.r + delta * 2, 0, 255),
                (byte)Mathf.Clamp(c.g + delta * 2, 0, 255),
                (byte)Mathf.Clamp(c.b + delta * 2, 0, 255),
                255);
        }
    }
}
