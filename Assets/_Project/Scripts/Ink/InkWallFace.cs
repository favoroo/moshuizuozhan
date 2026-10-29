using System;
using P1.Core;
using UnityEngine;

namespace P1.Ink
{
    /// <summary>
    /// 单块矩形墙面（竖直立面或任意朝向平面）的 2D 涂地网格。
    ///
    /// 与地面 <see cref="InkGrid"/> 的区别：地面用世界 XZ 投影，墙面的水平/竖直两轴朝向各不相同，
    /// 因此这里用「平面原点 + U/V 轴 + 法线」把世界坐标直接投影到墙面的米制坐标，
    /// 同一套涂色与查询逻辑即可服务任意朝向的立面。
    /// 纯 C#，不依赖场景，可在 EditMode 直接单测。
    /// </summary>
    public sealed class InkWallFace
    {
        /// <summary>墙面 U=0、V=0 角点的世界坐标。</summary>
        public Vector3 OriginWorld { get; }

        /// <summary>墙面水平方向单位轴（面内，世界空间）。</summary>
        public Vector3 UAxis { get; }

        /// <summary>墙面竖直方向单位轴（面内，世界空间）。</summary>
        public Vector3 VAxis { get; }

        /// <summary>墙面朝外法线（世界空间）。</summary>
        public Vector3 NormalWorld { get; }

        public float WorldWidth { get; }
        public float WorldHeight { get; }

        /// <summary>横向格子数（同时是贴图宽度）。</summary>
        public int Width { get; }

        /// <summary>纵向格子数（同时是贴图高度）。</summary>
        public int Height { get; }

        public float CellWidth { get; }
        public float CellHeight { get; }
        public float CellSize => Mathf.Min(CellWidth, CellHeight);

        /// <summary>墙面顶部的世界 Y（用于潜行上墙到顶后的翻越判定）。</summary>
        public float TopY => OriginWorld.y + VAxis.y * WorldHeight;

        public bool IsDirty { get; private set; }
        public Texture2D Texture { get; }
        public Color32[] PixelBuffer => _pixels;
        public string Name { get; }

        private readonly byte[] _owners;
        private readonly Color32[] _pixels;
        private readonly Color32 _neutralColor;
        private readonly Color32 _teamAColor;
        private readonly Color32 _teamBColor;

        public InkWallFace(
            Vector3 originWorld,
            Vector3 uAxis,
            Vector3 vAxis,
            Vector3 normalWorld,
            float worldWidth,
            float worldHeight,
            float cellSize,
            Color32 neutralColor,
            Color32 teamAColor,
            Color32 teamBColor,
            string name = "WallFace")
        {
            if (worldWidth <= 0f || worldHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(worldWidth), "墙面尺寸必须为正");
            if (cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize), "墙面格边长必须为正");

            Name = name;
            OriginWorld = originWorld;
            UAxis = uAxis.normalized;
            VAxis = vAxis.normalized;
            NormalWorld = normalWorld.normalized;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;

            // 贴图分辨率与格数一一对应，格子尽量取正方形，边缘多出的部分由 UV 拉伸吸收
            Width = Mathf.Max(1, Mathf.RoundToInt(worldWidth / cellSize));
            Height = Mathf.Max(1, Mathf.RoundToInt(worldHeight / cellSize));
            CellWidth = worldWidth / Width;
            CellHeight = worldHeight / Height;

            _neutralColor = neutralColor;
            _teamAColor = teamAColor;
            _teamBColor = teamBColor;

            int total = Width * Height;
            _owners = new byte[total];
            _pixels = new Color32[total];

            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                name = $"InkWallTex_{name}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            ResetPaint();
        }

        /// <summary>世界坐标投影到墙面米制坐标（U 向右、V 向上，单位米）。</summary>
        public Vector2 WorldToFace(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - OriginWorld;
            return new Vector2(Vector3.Dot(d, UAxis), Vector3.Dot(d, VAxis));
        }

        public Vector3 FaceToWorld(Vector2 facePoint)
        {
            return OriginWorld + UAxis * facePoint.x + VAxis * facePoint.y;
        }

        /// <summary>面坐标是否落在墙面矩形内（tolerance 为米制容差，吸收浮点误差）。</summary>
        public bool IsInsideFace(Vector2 facePoint, float tolerance = 0.02f)
        {
            return facePoint.x >= -tolerance && facePoint.x <= WorldWidth + tolerance &&
                   facePoint.y >= -tolerance && facePoint.y <= WorldHeight + tolerance;
        }

        /// <summary>把世界点投影成墙面格子索引；越界返回 false。</summary>
        public bool TryWorldToCell(Vector3 worldPoint, out int x, out int y)
        {
            Vector2 face = WorldToFace(worldPoint);
            if (!IsInsideFace(face, 0f))
            {
                x = -1;
                y = -1;
                return false;
            }

            x = Mathf.Clamp(Mathf.FloorToInt(face.x / CellWidth), 0, Width - 1);
            y = Mathf.Clamp(Mathf.FloorToInt(face.y / CellHeight), 0, Height - 1);
            return true;
        }

        /// <summary>
        /// 在墙面米制坐标处涂一个半径 radius（米）的圆斑。
        /// 返回归属发生变化的格子数（可直接用于大招充能）。
        /// </summary>
        public int PaintCircle(Vector2 centerFacePoint, float radiusMeters, Team team)
        {
            if (radiusMeters <= 0f || team == Team.Neutral)
                return 0;

            if (centerFacePoint.x + radiusMeters < 0f || centerFacePoint.x - radiusMeters > WorldWidth ||
                centerFacePoint.y + radiusMeters < 0f || centerFacePoint.y - radiusMeters > WorldHeight)
            {
                return 0;
            }

            int minX = Mathf.Max(0, Mathf.FloorToInt((centerFacePoint.x - radiusMeters) / CellWidth));
            int maxX = Mathf.Min(Width - 1, Mathf.FloorToInt((centerFacePoint.x + radiusMeters) / CellWidth));
            int minY = Mathf.Max(0, Mathf.FloorToInt((centerFacePoint.y - radiusMeters) / CellHeight));
            int maxY = Mathf.Min(Height - 1, Mathf.FloorToInt((centerFacePoint.y + radiusMeters) / CellHeight));

            float rSq = radiusMeters * radiusMeters;
            byte targetByte = (byte)team;
            Color32 baseColor = team == Team.TeamA ? _teamAColor : _teamBColor;
            int changed = 0;

            for (int y = minY; y <= maxY; y++)
            {
                float cy = (y + 0.5f) * CellHeight;
                float dy = cy - centerFacePoint.y;
                float dySq = dy * dy;
                int rowOffset = y * Width;

                for (int x = minX; x <= maxX; x++)
                {
                    int idx = rowOffset + x;
                    if (_owners[idx] == targetByte)
                        continue;

                    float cx = (x + 0.5f) * CellWidth;
                    float dx = cx - centerFacePoint.x;
                    if (dx * dx + dySq > rSq)
                        continue;

                    _owners[idx] = targetByte;
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

        /// <summary>世界坐标处涂色；点不在该墙面上时返回 0。</summary>
        public int PaintCircleWorld(Vector3 worldPoint, float radiusMeters, Team team)
        {
            return PaintCircle(WorldToFace(worldPoint), radiusMeters, team);
        }

        /// <summary>墙面米制坐标处的墨水归属。</summary>
        public Team InkAt(Vector2 facePoint)
        {
            if (!IsInsideFace(facePoint, 0f))
                return Team.Neutral;

            int x = Mathf.Clamp(Mathf.FloorToInt(facePoint.x / CellWidth), 0, Width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(facePoint.y / CellHeight), 0, Height - 1);
            return (Team)_owners[y * Width + x];
        }

        public Team InkAtWorld(Vector3 worldPoint)
        {
            return InkAt(WorldToFace(worldPoint));
        }

        public void ResetPaint()
        {
            Array.Clear(_owners, 0, _owners.Length);
            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = _neutralColor;
            }
            IsDirty = true;
        }

        /// <summary>把脏数据上传到贴图；返回是否发生了上传。</summary>
        public bool UploadIfDirty()
        {
            if (!IsDirty || Texture == null)
                return false;

            Texture.SetPixels32(_pixels);
            Texture.Apply(false, false);
            IsDirty = false;
            return true;
        }

        /// <summary>销毁运行时创建的贴图（仅运行时调用）。</summary>
        public void DestroyTexture()
        {
            if (Texture == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(Texture);
            else
                UnityEngine.Object.DestroyImmediate(Texture);
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