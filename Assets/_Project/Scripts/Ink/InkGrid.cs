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
        // 软占位（掩体墙/木箱/集装箱/花坛/售卖亭）：格子被实体占着（小地图占位色、接触遮蔽、窄缝剔除照旧），
        // 但它们的顶面是能从上方打到的平面 —— 从 y ≥ 顶面高度打下来的墨可以落在这一格并计入覆盖率。
        // 俯视投影把「道具顶面」和「脚下地面」压在同一个 XZ 格上，只有这条高度判据能区分
        // 「打在箱顶」（该显墨、该计分）和「在箱子旁边泼地」（不能隔箱蹭分）。
        private readonly bool[] _softBlocked;
        private readonly float[] _blockedTopY;
        private readonly Color32[] _pixels;

        /// <summary>软占位格的顶面高度容差：命中点落在顶面上时 y 与 collider.max.y 是同一值，留一点浮点余量。</summary>
        public const float SoftBlockTopTolerance = 0.05f;

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

        /// <summary>计分掩码需要重传：软占位格被涂到 / 占位重新标记 / 清场时置位。</summary>
        public bool ScoreMaskDirty { get; private set; }

        // 脏矩形：小地图贴图只上传真正变化过的格子，避免 12Hz 全量 400×400 拷贝。
        // 语义与 IsDirty 同步：IsDirty=false 时 max < min（空矩形）。
        private int _dirtyMinX = int.MaxValue;
        private int _dirtyMinY = int.MaxValue;
        private int _dirtyMaxX = int.MinValue;
        private int _dirtyMaxY = int.MinValue;

        public int DirtyMinX => _dirtyMinX;
        public int DirtyMinY => _dirtyMinY;
        public int DirtyMaxX => _dirtyMaxX;
        public int DirtyMaxY => _dirtyMaxY;

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
            _softBlocked = new bool[total];
            _blockedTopY = new float[total];
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

            // 全部回到未涂：软占位格重新被计分掩码裁掉。
            ScoreMaskDirty = true;
            MarkDirtyRect(0, 0, Width - 1, Height - 1);
        }

        /// <summary>
        /// 同时清空障碍物与墨水。
        /// </summary>
        public void ClearAll()
        {
            Array.Clear(_blocked, 0, _blocked.Length);
            Array.Clear(_softBlocked, 0, _softBlocked.Length);
            Array.Clear(_blockedTopY, 0, _blockedTopY.Length);
            _paintableCount = _owners.Length;
            ScoreMaskDirty = true;
            ResetPaintOnly();
        }

        /// <summary>
        /// 将世界坐标 XZ 矩形区域标记为不可涂（墙体、出生点安全区）。
        /// 硬不可涂：任何高度都涂不到（地形禁区、死缝、场外实体）。
        /// </summary>
        public void SetBlockedWorldRect(Vector2 minXZ, Vector2 maxXZ, bool blocked = true)
        {
            SetBlockedWorldRect(minXZ, maxXZ, blocked, float.NaN);
        }

        /// <summary>
        /// 带顶面高度的重载：<paramref name="topY"/> 为有限值时是「软占位」——
        /// 格子仍按占位处理（小地图占位色、接触遮蔽、窄缝剔除都不变），但
        /// <see cref="PaintCircle(Vector3, float, Team)"/> 从 topY 及以上打下来（即打在道具顶面上）时可以涂到，
        /// 并照常计入覆盖率。这样「打在箱顶」会显墨也计分，「在箱子旁边泼地」则涂不到箱顶——
        /// 既修掉「箱顶喷不上油漆」，又不会隔箱白拿覆盖面积。
        /// 多层碰撞体叠在同一格时取最高顶面。
        /// </summary>
        public void SetBlockedWorldRect(Vector2 minXZ, Vector2 maxXZ, bool blocked, float topY)
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

                    SetCellBlocked(idx, blocked, topY);
                }
            }
        }

        private void SetCellBlocked(int idx, bool blocked, float softTopY = float.NaN)
        {
            if (!blocked)
            {
                if (!_blocked[idx])
                    return;

                // 硬占位扣了可涂分母的坑，解除时要补回；软占位本来就没扣。
                if (!_softBlocked[idx])
                    _paintableCount++;

                _blocked[idx] = false;
                _softBlocked[idx] = false;
                _blockedTopY[idx] = 0f;
                Team owner = (Team)_owners[idx];
                _pixels[idx] = owner switch
                {
                    Team.TeamA => _teamAColor,
                    Team.TeamB => _teamBColor,
                    _ => _neutralColor
                };
                ScoreMaskDirty = true;
                MarkDirtyCell(idx % Width, idx / Width);
                return;
            }

            bool soft = !float.IsNaN(softTopY);
            if (_blocked[idx])
            {
                // 已占位：多层实体叠在同一格时，只要有一层顶面可打就算软占位，并取最高顶面。
                if (soft && !_softBlocked[idx])
                {
                    _softBlocked[idx] = true;
                    _blockedTopY[idx] = softTopY;
                    _paintableCount++;
                    ScoreMaskDirty = true;
                }
                else if (soft && softTopY > _blockedTopY[idx])
                {
                    _blockedTopY[idx] = softTopY;
                }

                return;
            }

            Team prev = (Team)_owners[idx];
            if (prev == Team.TeamA) _teamACount--;
            else if (prev == Team.TeamB) _teamBCount--;

            _owners[idx] = (byte)Team.Neutral;
            _blocked[idx] = true;
            _softBlocked[idx] = soft;
            _blockedTopY[idx] = soft ? softTopY : 0f;
            if (!soft)
                _paintableCount--;
            _pixels[idx] = _blockedColor;

            ScoreMaskDirty = true;
            MarkDirtyCell(idx % Width, idx / Width);
        }

        /// <summary>
        /// 窄缝自动剔除（移植 slotSides 思路）：
        /// 遍历所有非 blocked 格，若其在 X 方向左右两侧的相对障碍物缝宽 ≤ maxGapMeters
        /// （或在 Z 方向前后两侧的相对障碍物缝宽 ≤ maxGapMeters），判定为角色无法通过且难以涂抹的死缝，
        /// 将该格标记为 blocked 并同步扣减 _paintableCount。
        /// 单侧贴墙格与宽度 > maxGapMeters 的走廊不会被封闭。
        /// </summary>
        public int SealNarrowSlits(float maxGapMeters = 0.9f)
        {
            if (maxGapMeters <= 0f)
                return 0;

            int maxCellsX = Mathf.CeilToInt(maxGapMeters / CellWidth) + 1;
            int maxCellsY = Mathf.CeilToInt(maxGapMeters / CellHeight) + 1;

            var toSeal = new System.Collections.Generic.List<int>();

            for (int y = 0; y < Height; y++)
            {
                int rowOffset = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int idx = rowOffset + x;
                    if (_blocked[idx])
                        continue;

                    // 1. 检查 X 方向左右两侧障碍物缝宽
                    int leftBlockedX = -1;
                    int minLeft = Math.Max(0, x - maxCellsX);
                    for (int lx = x - 1; lx >= minLeft; lx--)
                    {
                        if (_blocked[rowOffset + lx])
                        {
                            leftBlockedX = lx;
                            break;
                        }
                    }

                    bool sealedInX = false;
                    if (leftBlockedX >= 0)
                    {
                        int rightBlockedX = -1;
                        int maxRight = Math.Min(Width - 1, x + maxCellsX);
                        for (int rx = x + 1; rx <= maxRight; rx++)
                        {
                            if (_blocked[rowOffset + rx])
                            {
                                rightBlockedX = rx;
                                break;
                            }
                        }

                        if (rightBlockedX >= 0)
                        {
                            // 左右障碍物之间的净空宽度（开阔格数 × 单格米数）
                            float gapX = (rightBlockedX - leftBlockedX - 1) * CellWidth;
                            if (gapX <= maxGapMeters)
                            {
                                toSeal.Add(idx);
                                sealedInX = true;
                            }
                        }
                    }

                    if (sealedInX)
                        continue;

                    // 2. 检查 Z (Y) 方向前后两侧障碍物缝宽
                    int downBlockedY = -1;
                    int minDown = Math.Max(0, y - maxCellsY);
                    for (int dy = y - 1; dy >= minDown; dy--)
                    {
                        if (_blocked[dy * Width + x])
                        {
                            downBlockedY = dy;
                            break;
                        }
                    }

                    if (downBlockedY >= 0)
                    {
                        int upBlockedY = -1;
                        int maxUp = Math.Min(Height - 1, y + maxCellsY);
                        for (int uy = y + 1; uy <= maxUp; uy++)
                        {
                            if (_blocked[uy * Width + x])
                            {
                                upBlockedY = uy;
                                break;
                            }
                        }

                        if (upBlockedY >= 0)
                        {
                            float gapZ = (upBlockedY - downBlockedY - 1) * CellHeight;
                            if (gapZ <= maxGapMeters)
                            {
                                toSeal.Add(idx);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < toSeal.Count; i++)
            {
                SetCellBlocked(toSeal[i], true);
            }

            return toSeal.Count;
        }

        /// <summary>
        /// 在世界坐标 XZ 处涂一个半径为 radius 的圆斑。
        /// 返回本次新涂或改写归属的格子数量（可直接用于大招充能）。
        /// 本重载没有高度信息（按地面 y=0 落墨）：软占位格（道具/木箱/集装箱顶面）涂不到，
        /// 需要「打在顶面上」的弹道请用 <see cref="PaintCircle(Vector3, float, Team)"/>。
        /// </summary>
        public int PaintCircle(Vector2 centerXZ, float radius, Team team)
        {
            return PaintCircleCore(centerXZ, 0f, radius, team);
        }

        private int PaintCircleCore(Vector2 centerXZ, float paintY, float radius, Team team)
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
                    {
                        // 软占位（道具/木箱/集装箱顶面）三条规则：
                        // ① 从顶面及以上打下来的墨一定能落在这一格（直接命中箱顶）；
                        // ② 没涂过的格不接受地面溅射 —— 否则在箱子旁边泼一地就能隔箱白拿覆盖面积；
                        // ③ 已经涂过的格接受任意高度：2D 图集里的地面墨会视觉上盖到箱顶，
                        //    网格必须跟着翻色，否则会出现「箱顶显示敌墨、比分却算我的」。
                        if (!_softBlocked[idx])
                            continue;

                        bool fromAbove = paintY >= _blockedTopY[idx] - SoftBlockTopTolerance;
                        if (!fromAbove && _owners[idx] == (byte)Team.Neutral)
                            continue;
                    }

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

                    // 纯色写入：视觉层的有机边缘与色调微差由 GPU 墨迹图集负责，
                    // 这里若再做逐格噪声就会在 0.1m 单元格上显出明显的马赛克砖块。
                    _pixels[idx] = baseColor;
                    changed++;

                    // 箱顶第一次被涂到：计分掩码要放开这一格。
                    // 已是「已涂」态时掩码不变（含敌我翻色），不重复上传。
                    if (_softBlocked[idx] && prev == (byte)Team.Neutral)
                        ScoreMaskDirty = true;
                }
            }

            if (changed > 0)
            {
                MarkDirtyRect(minX, minY, maxX, maxY);
            }

            return changed;
        }

        public int PaintCircle(Vector3 worldPos, float radius, Team team)
        {
            return PaintCircleCore(new Vector2(worldPos.x, worldPos.z), worldPos.y, radius, team);
        }

        public Team InkAt(Vector2 worldXZ)
        {
            if (!TryWorldToCell(worldXZ, out int x, out int y))
                return Team.Neutral;

            // 硬占位格永远没被涂过（PaintCircle 直接跳过），读到的就是 Neutral；
            // 软占位格返回「箱顶上的墨」，站在涂了敌墨的箱顶照常掉血/在己方墨里潜行。
            return (Team)_owners[y * Width + x];
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

        /// <summary>
        /// 该格是否为「软占位」（实体道具/掩体，顶面可从上方打到）。
        /// 供覆盖率审计区分「顶面能涂到」与「顶面在可涂范围外」。
        /// </summary>
        public bool IsSoftBlockedAt(Vector2 worldXZ)
        {
            if (!TryWorldToCell(worldXZ, out int x, out int y))
                return false;

            return _softBlocked[y * Width + x];
        }

        /// <summary>
        /// 把不可涂掩码复制成 0/255 的单通道图（行序 = Z 轴，列序 = X 轴）。
        /// 静态占位掩码（软硬都算）：供「掩体落地接触遮蔽」场烘焙用，与是否已被涂过无关。
        /// 顶面墨迹的计分裁剪请用 <see cref="CopyUnpaintedBlockedMaskTo"/>。
        /// </summary>
        public void CopyBlockedMaskTo(byte[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < _blocked.Length)
                throw new ArgumentException("destination 长度不足", nameof(destination));

            for (int i = 0; i < _blocked.Length; i++)
            {
                destination[i] = _blocked[i] ? (byte)255 : (byte)0;
            }
        }

        /// <summary>
        /// 把「仍占位、且还没被涂过」的格子复制成 0/255 单通道图（行序 = Z 轴，列序 = X 轴）。
        /// 供 <see cref="InkSurfaceController"/> 上传给顶面墨迹着色器：俯视投影下道具顶面与脚下地面
        /// 共用同一个 XZ 格，没涂过的占位格必须把图集里的溅射墨迹裁掉（否则「箱子旁边泼一地、
        /// 箱顶也蹭上墨」却不计分）；从上方命中箱顶后该格转为已涂，掩码随即放开，
        /// **看得到的顶面墨迹 = 会计分的墨迹**。硬占位格（地形禁区、死缝）永远返回 255。
        /// </summary>
        public void CopyUnpaintedBlockedMaskTo(byte[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < _blocked.Length)
                throw new ArgumentException("destination 长度不足", nameof(destination));

            for (int i = 0; i < _blocked.Length; i++)
            {
                destination[i] = _blocked[i] && _owners[i] == (byte)Team.Neutral ? (byte)255 : (byte)0;
            }

            ScoreMaskDirty = false;
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
            _dirtyMinX = int.MaxValue;
            _dirtyMinY = int.MaxValue;
            _dirtyMaxX = int.MinValue;
            _dirtyMaxY = int.MinValue;
        }

        /// <summary>把单格变化并入脏矩形。</summary>
        private void MarkDirtyCell(int x, int y)
        {
            MarkDirtyRect(x, y, x, y);
        }

        /// <summary>把一块矩形变化并入脏矩形（自动裁剪到网格范围，供小地图局部上传）。</summary>
        private void MarkDirtyRect(int minX, int minY, int maxX, int maxY)
        {
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX >= Width) maxX = Width - 1;
            if (maxY >= Height) maxY = Height - 1;
            if (maxX < minX || maxY < minY)
                return;

            IsDirty = true;
            if (minX < _dirtyMinX) _dirtyMinX = minX;
            if (minY < _dirtyMinY) _dirtyMinY = minY;
            if (maxX > _dirtyMaxX) _dirtyMaxX = maxX;
            if (maxY > _dirtyMaxY) _dirtyMaxY = maxY;
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
                        Team.TeamA => _teamAColor,
                        Team.TeamB => _teamBColor,
                        _ => _neutralColor
                    };
                }
            }

            MarkDirtyRect(0, 0, Width - 1, Height - 1);
        }
    }
}
