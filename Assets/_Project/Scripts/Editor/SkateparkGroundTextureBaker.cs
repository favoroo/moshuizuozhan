// ==============================================================================================
// SkateparkGroundTextureBaker.cs — 「霓虹海滨工业滑板场 / NEON HARBOR SKATEPARK」地面贴图烘焙器
//
// 对应概念设计「霓虹海滨工业滑板场」（中灰工业混凝土码头 + 黑色沥青服务道 + 碗池内衬）：
//   - 中灰工业混凝土：4m 大板 + 12m 伸缩缝 + 细粒骨料 + 玩家通行通道磨损暗带 (sRGB 亮度 ~0.44-0.52)
//   - 深黑沥青车道：70° 对角主干道 + 东侧停机坪辅助道 + 饱和黄双虚线 + 白色虚线边 + 导向箭头 + 斑马线
//   - 滑板碗池内衬：较浅的高光平滑混凝土 (sRGB 亮度 ~0.58-0.65) + 30° 放射缝 + 距墙 0.4m 环缝 + 轮迹
//   - **地面不烘焙任何油漆/涂鸦**：油漆只由游玩时的喷漆机制动态产生，场地本体保持干净
//   - 工业标线与安全警示：天桥坡道落地口与出生楼梯底座黄黑 45° 警示斜纹块、塔吊/挖掘机基座警示边
//   - 工业做旧与细节：吊车/挖掘机底座油污、集装箱脚印锈斑、雨水箅子铸铁栅格、全场轮胎转弯擦痕
//
// 输出 2048 反照率 + 1024 法线 + 1024 RMO，世界范围 X∈[-20,20]、Z∈[-28,28]，
// 与 InkGround.shader 的 inkUV 采样口径一致；固定种子，完全确定性、零配额、单次烘焙约 13 秒。
// ==============================================================================================

using System.IO;
using P1.Environment;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    public static class SkateparkGroundTextureBaker
    {
        public const string AlbedoPath = "Assets/_Project/Art/Textures/Arena/SkateparkGround_Albedo.png";
        public const string NormalPath = "Assets/_Project/Art/Textures/Arena/SkateparkGround_N.png";
        public const string RmoPath = "Assets/_Project/Art/Textures/Arena/SkateparkGround_RMO.png";

        public const int AlbedoResolution = 2048;
        public const int DetailResolution = 1024;
        public const int FixedSeed = 20261118;

        private const float WorldMinX = -20f;
        private const float WorldMaxX = 20f;
        private const float WorldMinZ = -28f;
        private const float WorldMaxZ = 28f;
        private const float WorldSizeX = WorldMaxX - WorldMinX;
        private const float WorldSizeZ = WorldMaxZ - WorldMinZ;

        // 对角服务车道：与天桥 yaw 一致（沿 70° 对角线延伸）。
        private static readonly float LaneDirX = Mathf.Sin(ArenaLayoutSkatepark.BridgeYawDegrees * Mathf.Deg2Rad);
        private static readonly float LaneDirZ = Mathf.Cos(ArenaLayoutSkatepark.BridgeYawDegrees * Mathf.Deg2Rad);
        private const float LaneHalfWidth = 3.25f;

        private struct Palette
        {
            public Color ConcreteBase;
            public Color ConcreteSeam;
            public Color ConcreteFine;
            public Color ExpansionSeam;
            public Color AsphaltBase;
            public Color AsphaltSeam;
            public Color AsphaltCrack;
            public Color BowlBase;
            public Color BowlSeam;
            public Color BowlRimDark;
            public Color WaterBed;
            public Color MarkingWhite;
            public Color HazardYellow;
            public Color HazardDark;
            public Color LaneTeal;
            public Color TeamAWarm;
            public Color TeamBCool;
            public Color Rust;
            public Color Oil;

            public static Palette FromStyle(ArenaStyleSO style)
            {
                Color rawGround = style != null ? style.groundBase : new Color(0.76f, 0.74f, 0.68f, 1f);
                Color yellow = style != null ? style.hazardYellow : new Color(0.96f, 0.78f, 0.12f, 1f);

                // 1. 目标 sRGB ~0.44-0.52 亮度中度工业灰，收敛原先 >0.6 发白洗白的观感
                Color industrialCoolGray = new Color(0.465f, 0.480f, 0.495f, 1f);
                Color concreteBase = Color.Lerp(rawGround * 0.62f, industrialCoolGray, 0.65f);

                return new Palette
                {
                    ConcreteBase = concreteBase,
                    ConcreteSeam = new Color(0.25f, 0.26f, 0.27f, 1f),
                    ConcreteFine = new Color(0.38f, 0.39f, 0.40f, 1f),
                    ExpansionSeam = new Color(0.18f, 0.19f, 0.20f, 1f),

                    // 2. 黑色沥青服务道：近黑沥青 sRGB ~0.16-0.22
                    AsphaltBase = new Color(0.180f, 0.185f, 0.190f, 1f),
                    AsphaltSeam = new Color(0.11f, 0.11f, 0.12f, 1f),
                    AsphaltCrack = new Color(0.09f, 0.09f, 0.10f, 1f),

                    // 3. 碗池底面：必须比甲板暗，才能读成「下沉的坑」。
                    // 旧值 0.615 比周围混凝土地面（~0.41）亮 50%，俯视/斜视下整个碗池读作
                    // 一块凸起的白色圆台（叠加旧版被水面灌满，更强化「转盘」观感）。
                    BowlBase = new Color(0.345f, 0.355f, 0.365f, 1f),
                    BowlSeam = new Color(0.235f, 0.245f, 0.255f, 1f),
                    BowlRimDark = new Color(0.165f, 0.175f, 0.185f, 1f),

                    WaterBed = new Color(0.055f, 0.16f, 0.20f, 1f),
                    MarkingWhite = new Color(0.94f, 0.94f, 0.92f, 1f),
                    HazardYellow = Color.Lerp(yellow, new Color(1.0f, 0.82f, 0.08f, 1f), 0.55f),
                    HazardDark = new Color(0.12f, 0.12f, 0.13f, 1f),
                    LaneTeal = style != null ? style.containerTeal : new Color(0.13f, 0.46f, 0.48f, 1f),
                    TeamAWarm = new Color(0.96f, 0.44f, 0.08f, 1f),
                    TeamBCool = new Color(0.16f, 0.42f, 0.88f, 1f),

                    // 4. 工业污渍（地面不烘焙任何油漆，油漆由游玩喷漆机制生成）
                    Rust = new Color(0.52f, 0.24f, 0.12f, 1f),
                    Oil = new Color(0.05f, 0.055f, 0.06f, 1f),
                };
            }
        }

        public static void BakeAll(ArenaStyleSO style)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Palette pal = Palette.FromStyle(style);

            int albedoSize = AlbedoResolution;
            var albedo = new Color32[albedoSize * albedoSize];
            for (int y = 0; y < albedoSize; y++)
            {
                float worldZ = WorldMinZ + (y + 0.5f) * (WorldSizeZ / albedoSize);
                int row = y * albedoSize;
                for (int x = 0; x < albedoSize; x++)
                {
                    float worldX = WorldMinX + (x + 0.5f) * (WorldSizeX / albedoSize);
                    albedo[row + x] = SampleAlbedo(worldX, worldZ, pal);
                }
            }
            SaveTexture(AlbedoPath, albedo, albedoSize, true);

            // 法线与 RMO 贴图走 1024：减半烘焙时间与显存开销
            int detailSize = DetailResolution;
            var normal = new Color32[detailSize * detailSize];
            var rmo = new Color32[detailSize * detailSize];
            for (int y = 0; y < detailSize; y++)
            {
                float worldZ = WorldMinZ + (y + 0.5f) * (WorldSizeZ / detailSize);
                int row = y * detailSize;
                for (int x = 0; x < detailSize; x++)
                {
                    float worldX = WorldMinX + (x + 0.5f) * (WorldSizeX / detailSize);
                    SampleDetail(worldX, worldZ, out Vector3 n, out float ao, out float smoothness, out float metallic);
                    normal[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255), 255);
                    rmo[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(metallic * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(ao * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(0.5f * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(smoothness * 255f), 0, 255));
                }
            }
            SaveTexture(NormalPath, normal, detailSize, false);
            SaveTexture(RmoPath, rmo, detailSize, false);

            sw.Stop();
            Debug.Log($"[SkateparkGroundTextureBaker] 滑板场地面成套烘焙完成：{sw.ElapsedMilliseconds} ms " +
                      $"（albedo {albedoSize}² / normal+RMO {detailSize}²）→ {AlbedoPath}");
        }

        // =====================================================================================
        // 分区判定
        // =====================================================================================

        /// <summary>碗池总外轮廓（11×7m，与新几何体匹配）。用于沥青排除与贴图分区。</summary>
        private static bool IsBowl(float x, float z)
        {
            return Mathf.Abs(x) <= ArenaLayoutSkatepark.BowlHalfX &&
                   Mathf.Abs(z) <= ArenaLayoutSkatepark.BowlHalfZ;
        }

        /// <summary>碗池真实开口（椭圆）。只有开口内部才应该用 BowlBase 烘底色；
        /// 旧实现用 11×7 矩形分区，把 y=0 的四个转角地面块也刷成了碗池色，池沿外侧出现方形色斑。</summary>
        private static bool IsBowlOpening(float x, float z)
        {
            float nx = x / ArenaLayoutSkatepark.BowlHalfX;
            float nz = z / ArenaLayoutSkatepark.BowlHalfZ;
            return nx * nx + nz * nz <= 1.0f;
        }

        /// <summary>碗池底部平坦底面（7.4×3.4m，使用地面材质展示涂鸦与板缝）。</summary>
        private static bool IsBowlFloor(float x, float z)
        {
            return Mathf.Abs(x) <= ArenaLayoutSkatepark.BowlFloorHalfX &&
                   Mathf.Abs(z) <= ArenaLayoutSkatepark.BowlFloorHalfZ;
        }

        /// <summary>对角服务车道：沿天桥 70° 延伸的深色沥青路带。</summary>
        private static bool IsDiagonalLane(float x, float z)
        {
            float perp = Mathf.Abs(x * LaneDirZ - z * LaneDirX);
            float edgeNoise = (Hash(x * 3.8f, z * 3.8f, FixedSeed ^ 0x3E) - 0.5f) * 0.35f;
            if (perp > LaneHalfWidth + edgeNoise) return false;
            return Mathf.Abs(z) < 21.0f;
        }

        /// <summary>东侧辅助沥青停机坪车道（x ≈ 15.6..20.0，并在东南角扩展）。</summary>
        private static bool IsEastAsphalt(float x, float z)
        {
            float edgeNoise = (Hash(x * 3.2f, z * 3.2f, FixedSeed ^ 0x5C) - 0.5f) * 0.40f;
            if (x >= 15.6f + edgeNoise && z >= -21.5f && z <= 18.5f) return true;
            if (x >= 13.2f + edgeNoise && z >= -21.5f && z <= -8.0f) return true;
            return false;
        }

        /// <summary>西侧 quarter-pipe 下方沥青垫层。</summary>
        private static bool IsPipePad(float x, float z)
        {
            return x < -15.2f && Mathf.Abs(z) < 4.0f;
        }

        /// <summary>所有沥青路面总和（碗池区域优先挖除）。</summary>
        private static bool IsAsphalt(float x, float z)
        {
            if (IsBowl(x, z)) return false;
            return IsDiagonalLane(x, z) || IsEastAsphalt(x, z) || IsPipePad(x, z);
        }

        // =====================================================================================
        // 反照率采样
        // =====================================================================================

        private static Color SampleAlbedo(float x, float z, Palette pal)
        {
            Color color;
            if (IsBowlOpening(x, z))
            {
                color = SampleBowl(x, z, pal);
            }
            else if (IsAsphalt(x, z))
            {
                color = SampleAsphalt(x, z, pal);
            }
            else
            {
                color = SampleConcrete(x, z, pal);
            }

            color = ApplyMarkings(x, z, color, pal);
            // 地面不烘焙任何油漆/涂鸦：油漆只由游玩时的喷漆机制动态生成
            color = ApplyDrainGrates(x, z, color, pal);
            color = ApplyIndustrialStains(x, z, color, pal);
            return color;
        }

        private static Color SampleConcrete(float x, float z, Palette pal)
        {
            // 4m 大板 + 12m 伸缩缝 + 1m 细微划分
            float seam4 = Mathf.Min(DistToGrid(x, 4f), DistToGrid(z, 4f));
            float seam12 = Mathf.Min(DistToGrid(x, 12f), DistToGrid(z, 12f));
            float seam1 = Mathf.Min(DistToGrid(x, 1f), DistToGrid(z, 1f));

            // 每块 4m 混凝土板微小的明度变化 (±4%)
            float slab = (Hash(Mathf.Floor(x / 4f), Mathf.Floor(z / 4f), FixedSeed) - 0.5f) * 0.08f;
            Color c = pal.ConcreteBase * (1f + slab);

            // 冷暖细微噪点偏移
            float cool = (Hash(Mathf.Floor(x / 2f), Mathf.Floor(z / 2f), FixedSeed ^ 0x2C) - 0.5f) * 0.03f;
            c = new Color(c.r - cool * 0.8f, c.g - cool * 0.3f, c.b + cool, c.a);

            // 细粒骨料噪点与沙粒
            float grain = (Hash(x * 6.3f, z * 6.3f, FixedSeed ^ 0x51) - 0.5f) * 0.035f;
            float pebble = (Hash(x * 21.0f, z * 21.0f, FixedSeed ^ 0x63) - 0.5f) * 0.020f;
            c = new Color(c.r + grain + pebble, c.g + grain + pebble, c.b + grain + pebble, c.a);

            // 玩家主要通行磨损暗带 (wear lanes)
            float wear = CalculateWear(x, z);
            c = Color.Lerp(c, pal.ConcreteSeam, wear * 0.28f);

            // 板缝混合
            float fineMask = SeamMask(seam1, 0.008f, 0.006f) * 0.30f;
            c = Color.Lerp(c, pal.ConcreteFine, fineMask);
            c = Color.Lerp(c, pal.ConcreteSeam, SeamMask(seam4, 0.015f, 0.010f) * 0.70f);
            c = Color.Lerp(c, pal.ExpansionSeam, SeamMask(seam12, 0.040f, 0.020f) * 0.85f);
            return c;
        }

        private static float CalculateWear(float x, float z)
        {
            float wear = 0f;
            float az = Mathf.Abs(z);
            float ax = Mathf.Abs(x);

            // 1. 出生大门与台阶出口主通行通道
            if (az >= 13.5f && az <= 21.5f)
            {
                if (ax < 4.0f) wear = Mathf.Max(wear, (1f - ax / 4.0f) * 0.75f);
                float dStair = Mathf.Abs(ax - 9.5f);
                if (dStair < 2.5f) wear = Mathf.Max(wear, (1f - dStair / 2.5f) * 0.65f);
            }

            // 2. 碗池池沿外围回转道 (coping rim)
            float nx = x / ArenaLayoutSkatepark.BowlCopingRadiusX;
            float nz = z / ArenaLayoutSkatepark.BowlCopingRadiusZ;
            float ellipse = Mathf.Sqrt(nx * nx + nz * nz);
            if (ellipse >= 1.02f && ellipse <= 1.35f)
            {
                float rimT = 1f - Mathf.Abs(ellipse - 1.18f) / 0.16f;
                wear = Mathf.Max(wear, Mathf.Clamp01(rimT) * 0.60f);
            }

            // 3. 天桥两端坡道落地口
            float dNE = Vector2.Distance(new Vector2(x, z), new Vector2(16.91f, 6.16f));
            if (dNE < 3.2f) wear = Mathf.Max(wear, (1f - dNE / 3.2f) * 0.85f);
            float dSW = Vector2.Distance(new Vector2(x, z), new Vector2(-16.91f, -6.16f));
            if (dSW < 3.2f) wear = Mathf.Max(wear, (1f - dSW / 3.2f) * 0.85f);

            return wear;
        }

        private static Color SampleAsphalt(float x, float z, Palette pal)
        {
            // 沥青修补拼块与微弱明度浮动
            float patch = (Hash(Mathf.Floor(x / 2.8f), Mathf.Floor(z / 2.8f), FixedSeed ^ 0x77) - 0.5f) * 0.12f;
            Color c = pal.AsphaltBase * (1f + patch);

            // 沥青粗粒骨料
            float gravel = (Hash(x * 19f, z * 19f, FixedSeed ^ 0x1B) - 0.5f) * 0.08f;
            c = new Color(c.r + gravel, c.g + gravel, c.b + gravel, c.a);

            // 局部油污浸润斑块
            float oilPatch = Hash(Mathf.Floor(x / 3.2f) + 11f, Mathf.Floor(z / 3.2f) - 7f, FixedSeed ^ 0x4D);
            if (oilPatch > 0.80f)
            {
                float dCenter = Vector2.Distance(new Vector2(x, z),
                    new Vector2(Mathf.Floor(x / 3.2f) * 3.2f + 1.6f, Mathf.Floor(z / 3.2f) * 3.2f + 1.6f));
                float oilMask = Mathf.Clamp01(1f - dCenter / 1.4f);
                c = Color.Lerp(c, pal.Oil, oilMask * 0.50f);
            }

            // 沥青细微裂痕 (cracks)
            float crackPattern = Mathf.Abs(Mathf.Sin(x * 1.8f + Mathf.Cos(z * 2.2f) * 1.2f) *
                                           Mathf.Cos(z * 1.8f + Mathf.Sin(x * 2.2f) * 1.2f));
            if (crackPattern < 0.035f)
            {
                float crackNoise = Hash(x * 8f, z * 8f, FixedSeed ^ 0x88);
                if (crackNoise > 0.45f)
                {
                    float crackAlpha = (1f - crackPattern / 0.035f) * (crackNoise - 0.45f) * 1.8f;
                    c = Color.Lerp(c, pal.AsphaltCrack, Mathf.Clamp01(crackAlpha) * 0.75f);
                }
            }

            // 轮胎擦痕 (tire marks: 双弧线)
            float perp = x * LaneDirZ - z * LaneDirX;
            float along = x * LaneDirX + z * LaneDirZ;
            for (int i = -1; i <= 1; i += 2)
            {
                float track = Mathf.Abs(perp - i * 1.35f);
                float streak = (1f - Mathf.Clamp01(track / 0.26f)) *
                               (0.5f + 0.5f * Mathf.Sin(along * 0.9f + i * 1.7f));
                c = Color.Lerp(c, pal.Oil, streak * 0.32f);
            }

            return c;
        }

        private static Color SampleBowl(float x, float z, Palette pal)
        {
            float worldR = Mathf.Sqrt(x * x + z * z);
            float angle = Mathf.Atan2(z, x);

            bool isFloor = IsBowlFloor(x, z);
            Color baseCol = isFloor ? pal.BowlBase : Color.Lerp(pal.BowlBase, pal.ConcreteBase, 0.45f);

            // 扇形面板微小色阶差
            float sector = (Hash(Mathf.Floor(angle * 3f), Mathf.Floor(worldR * 0.8f), FixedSeed ^ 0x8A) - 0.5f) * 0.04f;
            Color c = baseCol * (1f + sector);

            // 细腻骨料噪点
            float grain = (Hash(x * 7.5f, z * 7.5f, FixedSeed ^ 0x9B) - 0.5f) * 0.025f;
            c = new Color(c.r + grain, c.g + grain, c.b + grain, c.a);

            // 1. 每 30° 一道放射状伸缩缝
            float spoke = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI / 6f), 1f) - 0.5f) * 2f;
            c = Color.Lerp(c, pal.BowlSeam, SeamMask(spoke, 0.008f, 0.005f) * 0.78f);

            // 2. 距离内壁 0.4m 处的深色环向分格缝 (darker ring 0.4m from the wall)
            if (isFloor)
            {
                float dWall = Mathf.Min(ArenaLayoutSkatepark.BowlFloorHalfX - Mathf.Abs(x),
                                        ArenaLayoutSkatepark.BowlFloorHalfZ - Mathf.Abs(z));
                float ringDist = Mathf.Abs(dWall - 0.40f);
                float ringMask = 1f - Mathf.Clamp01(ringDist / 0.06f);
                c = Color.Lerp(c, pal.BowlRimDark, ringMask * 0.82f);
            }

            // 3. 滑板碗池弧形轮迹与擦痕 (scuff marks)
            float scuffAngle = angle * 3.5f + Mathf.Sin(worldR * 1.8f);
            float scuffCell = Mathf.Floor(scuffAngle);
            float scuffN = Hash(scuffCell, Mathf.Floor(worldR * 1.2f), FixedSeed ^ 0xA5);
            if (scuffN > 0.70f)
            {
                float scuffLine = 1f - Mathf.Clamp01(Mathf.Abs(scuffAngle - scuffCell - 0.5f) / 0.14f);
                c = Color.Lerp(c, pal.BowlRimDark, scuffLine * (scuffN - 0.70f) * 0.90f);
            }

            // 4. 池底贴壁环境光遮蔽：越靠池壁越暗。俯视时这是「坑」而不是「白圆台」的关键读形。
            //    用椭圆归一化距离而不是矩形距离，避免四角出现方形暗块。
            if (isFloor)
            {
                float nx = x / ArenaLayoutSkatepark.BowlFloorHalfX;
                float nz = z / ArenaLayoutSkatepark.BowlFloorHalfZ;
                float edge = Mathf.Sqrt(nx * nx + nz * nz);
                float ao = 1f - Mathf.Clamp01((1f - edge) / 0.22f);
                c = Color.Lerp(c, pal.BowlRimDark, ao * 0.70f);
            }

            return c;
        }

        // =====================================================================================
        // 标线绘制（黄双虚线、白边虚线、导向箭头、斑马线、45°黄黑安全斑马块）
        // =====================================================================================

        private static Color ApplyMarkings(float x, float z, Color color, Palette pal)
        {
            float ax = Mathf.Abs(x);
            float az = Mathf.Abs(z);

            // 1. 碗池池沿：椭圆黄白池沿双线
            float nx = x / ArenaLayoutSkatepark.BowlCopingRadiusX;
            float nz = z / ArenaLayoutSkatepark.BowlCopingRadiusZ;
            float ellipse = Mathf.Sqrt(nx * nx + nz * nz);
            // 池沿：白色 coping 线 + 外侧一圈很淡的黄色安全线。
            // 旧版池沿是一条高饱和黄实线，配合被水灌满的亮池底，俯视读作「转盘跑道」。
            if (Mathf.Abs(ellipse - 1f) < 0.0065f)
            {
                color = Color.Lerp(color, pal.MarkingWhite, 0.82f);
            }
            if (Mathf.Abs(ellipse - 1.045f) < 0.0040f)
            {
                color = Color.Lerp(color, pal.HazardYellow, 0.34f);
            }

            // 2. 对角沥青车道标线 (Yellow double dashed line + White dashed edge)
            float perp = x * LaneDirZ - z * LaneDirX;
            float along = x * LaneDirX + z * LaneDirZ;
            if (IsDiagonalLane(x, z) && !IsBowl(x, z))
            {
                // 饱和黄色双虚线 (0.12m 宽，间距 0.28m)
                float dYellow = Mathf.Min(Mathf.Abs(perp - 0.14f), Mathf.Abs(perp + 0.14f));
                float dashY = Mathf.Repeat(along + 0.8f, 3.6f);
                if (dYellow < 0.06f && dashY < 2.2f)
                {
                    color = Color.Lerp(color, pal.HazardYellow, 0.88f);
                }

                // 白色虚线边缘 (0.10m 宽)
                float edgeDist = Mathf.Min(Mathf.Abs(perp - (LaneHalfWidth - 0.25f)),
                                           Mathf.Abs(perp + (LaneHalfWidth - 0.25f)));
                float dashW = Mathf.Repeat(along, 2.6f);
                if (edgeDist < 0.05f && dashW < 1.5f)
                {
                    color = Color.Lerp(color, pal.MarkingWhite, 0.78f);
                }
            }

            // 3. 东侧沥青停机坪车道标线
            if (IsEastAsphalt(x, z) && !IsBowl(x, z))
            {
                if (Mathf.Abs(x - 17.5f) < 0.06f && Mathf.Repeat(z + 1.2f, 3.6f) < 2.0f)
                {
                    color = Color.Lerp(color, pal.HazardYellow, 0.85f);
                }
                if (Mathf.Abs(x - 15.9f) < 0.05f && Mathf.Repeat(z, 2.6f) < 1.5f)
                {
                    color = Color.Lerp(color, pal.MarkingWhite, 0.75f);
                }
            }

            // 4. 白色导向箭头 (2 white directional arrows, 3-rectangle shapes)
            // 箭头 1: 对角车道西南段 (朝天桥/中场方向)
            DrawRoadArrow(x, z, -7.8f, -2.8f, ArenaLayoutSkatepark.BridgeYawDegrees, 1.15f, ref color, pal.MarkingWhite);
            // 箭头 2: 东侧沥青车道 (朝北直行方向)
            DrawRoadArrow(x, z, 17.5f, 2.0f, 0f, 1.15f, ref color, pal.MarkingWhite);

            // 5. 斑马线 (1 crosswalk: 6 white bars near north spawn staircase)
            DrawNorthCrosswalk(x, z, ref color, pal.MarkingWhite);

            // 6. 黄黑 45° 警示斜纹块 (Hazard chevrons: 2m x 1.2m patches at ramp landings & spawn stair bases)
            // 天桥坡道落地口 (2 处)
            DrawHazardPatch(x, z, 16.91f, 6.16f, 1.0f, 0.6f, ArenaLayoutSkatepark.BridgeYawDegrees, ref color, pal);
            DrawHazardPatch(x, z, -16.91f, -6.16f, 1.0f, 0.6f, ArenaLayoutSkatepark.BridgeYawDegrees, ref color, pal);
            // 出生楼梯接地口 (4 处)
            DrawHazardPatch(x, z, 11.5f, 21.5f, 1.0f, 0.6f, 0f, ref color, pal);
            DrawHazardPatch(x, z, -11.5f, 21.5f, 1.0f, 0.6f, 0f, ref color, pal);
            DrawHazardPatch(x, z, 11.5f, -21.5f, 1.0f, 0.6f, 0f, ref color, pal);
            DrawHazardPatch(x, z, -11.5f, -21.5f, 1.0f, 0.6f, 0f, ref color, pal);

            // 7. 出生通道起跑线与队色人字
            if (ax < 7.2f)
            {
                if (Mathf.Abs(az - 20.8f) < 0.16f)
                {
                    color = Color.Lerp(color, pal.MarkingWhite, 0.70f);
                }
                float chevronCenter = z < 0f ? -17.2f : 17.2f;
                float band = Mathf.Abs(z - chevronCenter - (z < 0f ? ax * 0.55f : -ax * 0.55f));
                if (band < 0.32f && ax < 2.6f)
                {
                    color = Color.Lerp(color, z < 0f ? pal.TeamAWarm : pal.TeamBCool, 0.72f);
                }
            }

            // 8. 塔吊与挖掘机基座黄黑警示边框
            for (int m = 0; m < 2; m++)
            {
                float sign = m == 0 ? 1f : -1f;
                Vector3 crane = new Vector3(ArenaLayoutSkatepark.CranePos.x * sign, 0f, ArenaLayoutSkatepark.CranePos.z * sign);
                float craneHalf = ArenaLayoutSkatepark.CraneBaseSize.x * 0.5f + 0.35f;
                if ((Mathf.Abs(Mathf.Abs(x - crane.x) - craneHalf) < 0.14f && Mathf.Abs(z - crane.z) < craneHalf) ||
                    (Mathf.Abs(Mathf.Abs(z - crane.z) - craneHalf) < 0.14f && Mathf.Abs(x - crane.x) < craneHalf))
                {
                    color = Color.Lerp(color, pal.HazardYellow, 0.65f);
                }

                Vector3 exc = new Vector3(ArenaLayoutSkatepark.ExcavatorPos.x * sign, 0f, ArenaLayoutSkatepark.ExcavatorPos.z * sign);
                float excHalf = ArenaLayoutSkatepark.ExcavatorPlinthSize.x * 0.5f + 0.25f;
                if ((Mathf.Abs(Mathf.Abs(x - exc.x) - excHalf) < 0.12f && Mathf.Abs(z - exc.z) < excHalf) ||
                    (Mathf.Abs(Mathf.Abs(z - exc.z) - excHalf) < 0.12f && Mathf.Abs(x - exc.x) < excHalf))
                {
                    bool stripe = Mathf.Repeat((x + z) * 2.2f, 1f) < 0.5f;
                    color = Color.Lerp(color, stripe ? pal.HazardYellow : pal.HazardDark, 0.65f);
                }
            }

            // 9. 贴墙外围安全警戒线
            if (Mathf.Abs(ax - 19.7f) < 0.08f || Mathf.Abs(az - 27.5f) < 0.08f)
            {
                color = Color.Lerp(color, pal.MarkingWhite, 0.50f);
            }

            return color;
        }

        private static void DrawRoadArrow(float x, float z, float arrowX, float arrowZ, float yawDeg, float scale, ref Color color, Color arrowColor)
        {
            float rad = -yawDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);
            float dx = x - arrowX;
            float dz = z - arrowZ;
            float lx = dx * cosA - dz * sinA;
            float lz = dx * sinA + dz * cosA;

            if (Mathf.Abs(lx) > 1.2f * scale || Mathf.Abs(lz) > 1.5f * scale) return;

            // 1. 箭杆 (stem rectangle)
            bool stem = Mathf.Abs(lx) <= 0.10f * scale && lz >= -0.70f * scale && lz <= 0.35f * scale;

            // 2. 左箭头翼 (left barb: rotated 35° rectangle)
            float radL = 35f * Mathf.Deg2Rad;
            float cosL = Mathf.Cos(radL);
            float sinL = Mathf.Sin(radL);
            float rlx = lx * cosL - (lz - 0.35f * scale) * sinL;
            float rlz = lx * sinL + (lz - 0.35f * scale) * cosL;
            bool barbL = Mathf.Abs(rlx) <= 0.09f * scale && rlz >= -0.55f * scale && rlz <= 0.05f * scale;

            // 3. 右箭头翼 (right barb: rotated -35° rectangle)
            float radR = -35f * Mathf.Deg2Rad;
            float cosR = Mathf.Cos(radR);
            float sinR = Mathf.Sin(radR);
            float rrx = lx * cosR - (lz - 0.35f * scale) * sinR;
            float rrz = lx * sinR + (lz - 0.35f * scale) * cosR;
            bool barbR = Mathf.Abs(rrx) <= 0.09f * scale && rrz >= -0.55f * scale && rrz <= 0.05f * scale;

            if (stem || barbL || barbR)
            {
                color = Color.Lerp(color, arrowColor, 0.85f);
            }
        }

        private static void DrawNorthCrosswalk(float x, float z, ref Color color, Color white)
        {
            // 北侧出生楼梯底座旁的 6 根白色斑马线条 (x ∈ [7.2, 11.8], z ∈ [20.1, 21.5])
            if (z < 20.1f || z > 21.5f || x < 7.2f || x > 11.8f) return;

            float barWidth = 0.42f;
            float barSpacing = 0.75f;
            float barX = Mathf.Repeat(x - 7.3f, barSpacing);
            if (barX < barWidth)
            {
                float edge = Mathf.Clamp01((z - 20.1f) / 0.04f) * Mathf.Clamp01((21.5f - z) / 0.04f) *
                             Mathf.Clamp01(barX / 0.03f) * Mathf.Clamp01((barWidth - barX) / 0.03f);
                color = Color.Lerp(color, white, 0.85f * edge);
            }
        }

        private static void DrawHazardPatch(float x, float z, float centerX, float centerZ, float halfW, float halfH, float yawDeg, ref Color color, Palette pal)
        {
            float rad = -yawDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);
            float dx = x - centerX;
            float dz = z - centerZ;
            float lx = dx * cosA - dz * sinA;
            float lz = dx * sinA + dz * cosA;

            if (Mathf.Abs(lx) <= halfW && Mathf.Abs(lz) <= halfH)
            {
                float stripe = Mathf.Repeat((lx + lz) / 0.28f, 1f);
                Color c = stripe < 0.5f ? pal.HazardYellow : pal.HazardDark;
                float edge = Mathf.Clamp01((halfW - Mathf.Abs(lx)) / 0.04f) * Mathf.Clamp01((halfH - Mathf.Abs(lz)) / 0.04f);
                color = Color.Lerp(color, c, 0.90f * edge);
            }
        }

        // =====================================================================================
        // 雨水箅子（3 处铸铁框 + 平行钢格栅）
        // =====================================================================================

        private static Color ApplyDrainGrates(float x, float z, Color color, Palette pal)
        {
            Vector3[] grates =
            {
                new Vector3(2.8f, 0f, 3.8f),    // 对角车道东侧
                new Vector3(-8.2f, 0f, -6.8f),  // 对角车道西南侧
                new Vector3(15.2f, 0f, -2.0f),  // 东侧沥青停机坪西沿
            };

            for (int i = 0; i < grates.Length; i++)
            {
                float dx = x - grates[i].x;
                float dz = z - grates[i].z;
                if (Mathf.Abs(dx) > 0.45f || Mathf.Abs(dz) > 0.65f) continue;

                bool onFrame = Mathf.Abs(dx) >= 0.29f || Mathf.Abs(dz) >= 0.49f;
                if (onFrame)
                {
                    color = Color.Lerp(color, new Color(0.18f, 0.19f, 0.20f, 1f), 0.90f);
                    continue;
                }

                float barZ = Mathf.Repeat(dz + 0.45f, 0.90f / 6f);
                if (barZ < 0.045f)
                {
                    color = Color.Lerp(color, new Color(0.26f, 0.27f, 0.29f, 1f), 0.92f);
                }
                else
                {
                    color = Color.Lerp(color, new Color(0.04f, 0.04f, 0.05f, 1f), 0.95f);
                }
            }

            return color;
        }

        // =====================================================================================
        // 工业做旧（油污、锈迹、墙根飞溅、地面潮痕）
        // =====================================================================================

        private static Color ApplyIndustrialStains(float x, float z, Color color, Palette pal)
        {
            // 1. 塔吊与挖掘机底座下方软边大油污 (Oil stains: soft dark ellipses)
            Vector3[] oilSpots =
            {
                ArenaLayoutSkatepark.CranePos,
                -ArenaLayoutSkatepark.CranePos,
                ArenaLayoutSkatepark.ExcavatorPos,
                -ArenaLayoutSkatepark.ExcavatorPos,
            };
            for (int i = 0; i < oilSpots.Length; i++)
            {
                float dx = (x - oilSpots[i].x) / 2.4f;
                float dz = (z - oilSpots[i].z) / 1.9f;
                float dSq = dx * dx + dz * dz;
                if (dSq < 1.0f)
                {
                    float noise = 0.6f + 0.4f * Hash(x * 4.5f + i * 11f, z * 4.5f - i * 7f, FixedSeed ^ 0xB7);
                    float oilAlpha = (1f - dSq) * noise;
                    color = Color.Lerp(color, pal.Oil, oilAlpha * 0.70f);
                }
            }

            // 2. 集装箱堆与办公室脚下的锈斑积尘
            Vector3[] containerSpots =
            {
                ArenaLayoutSkatepark.StackPos,
                -ArenaLayoutSkatepark.StackPos,
                ArenaLayoutSkatepark.SingleContainerPos,
                -ArenaLayoutSkatepark.SingleContainerPos,
                ArenaLayoutSkatepark.OfficePos,
                -ArenaLayoutSkatepark.OfficePos,
            };
            for (int i = 0; i < containerSpots.Length; i++)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(containerSpots[i].x, containerSpots[i].z));
                if (d > 4.5f) continue;
                float mask = 1f - Mathf.Clamp01(d / 4.5f);
                float n = Hash(x * 3.5f + i * 13f, z * 3.5f - i * 7f, FixedSeed ^ 0xB3);
                if (n > 0.50f)
                {
                    Color stainCol = i % 2 == 0 ? pal.Rust : pal.Oil;
                    color = Color.Lerp(color, stainCol, mask * (n - 0.50f) * 0.60f);
                }
            }

            // 3. 靠墙潮气暗部 (Wall moisture)
            float wallDist = Mathf.Min(Mathf.Abs(20f - Mathf.Abs(x)), Mathf.Abs(28f - Mathf.Abs(z)));
            if (wallDist < 0.8f)
            {
                float wet = 1f - Mathf.Clamp01(wallDist / 0.8f);
                color *= 1f - wet * 0.12f;
            }

            return color;
        }

        // =====================================================================================
        // 法线 / RMO（粗糙度、金属度、AO）
        // =====================================================================================

        private static void SampleDetail(float x, float z, out Vector3 normal, out float ao, out float smoothness, out float metallic)
        {
            normal = Vector3.up;
            ao = 1f;
            smoothness = 0.30f;
            metallic = 0f;
            float slopeX = 0f;
            float slopeZ = 0f;

            if (IsBowl(x, z))
            {
                bool isFloor = IsBowlFloor(x, z);
                smoothness = isFloor ? 0.44f : 0.36f;

                float angle = Mathf.Atan2(z, x);
                float spoke = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI / 6f), 1f) - 0.5f) * 2f;
                float worldR = Mathf.Sqrt(x * x + z * z);
                float ring = DistToGrid(worldR, 2f);

                // 放射缝坡度
                float grooveSpoke = Groove(spoke, 0.012f) * 0.14f;
                slopeX += -Mathf.Sin(angle) * grooveSpoke;
                slopeZ += Mathf.Cos(angle) * grooveSpoke;

                // 同心环缝坡度
                float grooveRing = Groove(ring, 0.014f) * 0.16f;
                float invR = 1f / Mathf.Max(0.01f, worldR);
                slopeX += x * invR * grooveRing;
                slopeZ += z * invR * grooveRing;

                // 碗底 0.4m 控制缝
                if (isFloor)
                {
                    float dWall = Mathf.Min(ArenaLayoutSkatepark.BowlFloorHalfX - Mathf.Abs(x),
                                            ArenaLayoutSkatepark.BowlFloorHalfZ - Mathf.Abs(z));
                    float ringDist = Mathf.Abs(dWall - 0.40f);
                    float grooveWallRing = Groove(ringDist, 0.025f) * 0.20f;
                    slopeX += Mathf.Sign(x) * grooveWallRing * (ArenaLayoutSkatepark.BowlFloorHalfX - Mathf.Abs(x) < ArenaLayoutSkatepark.BowlFloorHalfZ - Mathf.Abs(z) ? 1f : 0f);
                    slopeZ += Mathf.Sign(z) * grooveWallRing * (ArenaLayoutSkatepark.BowlFloorHalfZ - Mathf.Abs(z) <= ArenaLayoutSkatepark.BowlFloorHalfX - Mathf.Abs(x) ? 1f : 0f);
                    ao *= 1f - SeamMask(ringDist, 0.025f, 0.015f) * 0.28f;
                }

                ao *= 1f - Mathf.Max(SeamMask(spoke, 0.008f, 0.005f), SeamMask(ring, 0.012f, 0.008f)) * 0.25f;
            }
            else if (IsAsphalt(x, z))
            {
                smoothness = 0.22f;
                ao = 0.88f;

                // 沥青细微粗糙坡度扰动
                float nX = (Hash(x * 16f, z * 16f, FixedSeed ^ 0x41) - 0.5f) * 0.16f;
                float nZ = (Hash(x * 16f, z * 16f, FixedSeed ^ 0x47) - 0.5f) * 0.16f;
                slopeX += nX;
                slopeZ += nZ;

                // 标线处微提光滑度
                float perp = x * LaneDirZ - z * LaneDirX;
                if (Mathf.Abs(perp) < 0.22f) smoothness = Mathf.Lerp(smoothness, 0.46f, 0.75f);
            }
            else
            {
                smoothness = 0.30f;

                // 4m 板缝坡度
                float seam4X = DistToGrid(x, 4f);
                float seam4Z = DistToGrid(z, 4f);
                float seam4 = Mathf.Min(seam4X, seam4Z);
                if (seam4 < 0.015f)
                {
                    float t4 = seam4 / 0.015f;
                    float g4 = 4f * t4 * (1f - t4) * 0.22f;
                    if (seam4Z <= seam4X) slopeZ = Mathf.Sign(DistSigned(z, 4f)) * g4;
                    else slopeX = Mathf.Sign(DistSigned(x, 4f)) * g4;
                }

                // 12m 伸缩缝坡度
                float seam12X = DistToGrid(x, 12f);
                float seam12Z = DistToGrid(z, 12f);
                float seam12 = Mathf.Min(seam12X, seam12Z);
                if (seam12 < 0.040f)
                {
                    float t12 = seam12 / 0.040f;
                    float g12 = 4f * t12 * (1f - t12) * 0.34f;
                    if (seam12Z <= seam12X) slopeZ += Mathf.Sign(DistSigned(z, 12f)) * g12;
                    else slopeX += Mathf.Sign(DistSigned(x, 12f)) * g12;
                }

                float mask4 = SeamMask(seam4, 0.015f, 0.010f);
                float mask12 = SeamMask(seam12, 0.040f, 0.020f);
                ao = 1f - Mathf.Max(mask4 * 0.25f, mask12 * 0.42f);

                // 行动磨损通道平滑度
                float wear = CalculateWear(x, z);
                smoothness = Mathf.Lerp(smoothness, 0.38f, wear * 0.50f);
            }

            // 油污增加高光滑度 (Oil sheen)
            Vector3[] oilSpots =
            {
                ArenaLayoutSkatepark.CranePos,
                -ArenaLayoutSkatepark.CranePos,
                ArenaLayoutSkatepark.ExcavatorPos,
                -ArenaLayoutSkatepark.ExcavatorPos,
            };
            for (int i = 0; i < oilSpots.Length; i++)
            {
                float dx = (x - oilSpots[i].x) / 2.4f;
                float dz = (z - oilSpots[i].z) / 1.9f;
                float dSq = dx * dx + dz * dz;
                if (dSq < 1.0f)
                {
                    float oilAlpha = 1f - dSq;
                    smoothness = Mathf.Lerp(smoothness, 0.72f, oilAlpha * 0.85f);
                    ao = Mathf.Lerp(ao, 0.65f, oilAlpha * 0.45f);
                }
            }

            // 雨水箅子法线、金属度与深槽遮挡 (Drain grates)
            Vector3[] grates =
            {
                new Vector3(2.8f, 0f, 3.8f),
                new Vector3(-8.2f, 0f, -6.8f),
                new Vector3(15.2f, 0f, -2.0f),
            };
            for (int i = 0; i < grates.Length; i++)
            {
                float dx = x - grates[i].x;
                float dz = z - grates[i].z;
                if (Mathf.Abs(dx) <= 0.35f && Mathf.Abs(dz) <= 0.55f)
                {
                    smoothness = 0.52f;
                    metallic = 0.65f;
                    bool onFrame = Mathf.Abs(dx) >= 0.29f || Mathf.Abs(dz) >= 0.49f;
                    if (onFrame)
                    {
                        ao = 0.85f;
                    }
                    else
                    {
                        float barZ = Mathf.Repeat(dz + 0.45f, 0.90f / 6f);
                        if (barZ < 0.045f)
                        {
                            ao = 0.90f;
                        }
                        else
                        {
                            ao = 0.25f;
                        }
                    }
                }
            }

            normal = new Vector3(-slopeX, -slopeZ, 1f).normalized;
        }

        // =====================================================================================
        // 数学与几何工具
        // =====================================================================================

        private static float DistToGrid(float value, float period)
        {
            float r = Mathf.Repeat(value, period);
            return Mathf.Min(r, period - r);
        }

        private static float DistSigned(float value, float period)
        {
            float r = Mathf.Repeat(value, period);
            return r <= period * 0.5f ? r : r - period;
        }

        private static float SeamMask(float distance, float halfWidth, float softness)
        {
            float t = Mathf.InverseLerp(halfWidth + softness, halfWidth - softness, distance);
            return t * t * (3f - 2f * t);
        }

        /// <summary>平滑 V 槽：t=0/1 处斜率为 0，避免法线在槽心产生阶跃闪烁。</summary>
        private static float Groove(float distance, float width)
        {
            if (distance >= width) return 0f;
            float t = distance / width;
            return 4f * t * (1f - t);
        }

        private static float Hash(float x, float y, int seed)
        {
            float v = Mathf.Sin(x * 12.9898f + y * 78.233f + seed * 0.0017f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static void SaveTexture(string path, Color32[] pixels, int size, bool sRGB)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !sRGB);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool isNormal = path.EndsWith("_N.png", System.StringComparison.OrdinalIgnoreCase);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal && sRGB;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
