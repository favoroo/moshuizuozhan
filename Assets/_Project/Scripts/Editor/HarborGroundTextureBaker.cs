// ==============================================================================================
// HarborGroundTextureBaker.cs — 「海鸥港湾 / GULL HARBOR」地面贴图确定性烘焙器
//
// 与 ArenaGroundTextureBaker（集装箱码头：4m 混凝土大板 + 工业标线）并行的第二套地面皮肤：
//   - 木栈道：船舷/侧翼车道/出生通道的柚木板条（0.30m 板宽、2.20m 板长、错缝、木纹明度差）
//   - 码头面：登船坡道与中央通道的暖奶油色混凝土板（4m 分缝）
//   - 浅湾水床：深青底（会被 P1/Water 水面盖住，只负责水线边缘的暗部过渡）
//   - 港湾标线：白实线/虚线车道、登船口黄黑警示带、队色人字箭头、H 字泊位标
//
// 输出 2048 反照率 + 1024 法线 + 1024 RMO，世界范围 X∈[-20,20]、Z∈[-28,28]，
// 与 InkGround.shader 的 inkUV 采样口径一致；固定种子，可重复、零配额、单次烘焙 ≤ 6 秒。
// ==============================================================================================

using System.IO;
using P1.Environment;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    public static class HarborGroundTextureBaker
    {
        public const string AlbedoPath = "Assets/_Project/Art/Textures/Arena/HarborGround_Albedo.png";
        public const string NormalPath = "Assets/_Project/Art/Textures/Arena/HarborGround_N.png";
        public const string RmoPath = "Assets/_Project/Art/Textures/Arena/HarborGround_RMO.png";

        public const int AlbedoResolution = 2048;
        public const int DetailResolution = 1024;
        public const int FixedSeed = 20261104;

        private const float WorldMinX = -20f;
        private const float WorldMaxX = 20f;
        private const float WorldMinZ = -28f;
        private const float WorldMaxZ = 28f;
        private const float WorldSizeX = WorldMaxX - WorldMinX;
        private const float WorldSizeZ = WorldMaxZ - WorldMinZ;

        private struct Palette
        {
            public Color QuayBase;
            public Color QuaySeam;
            public Color WoodLight;
            public Color WoodDark;
            public Color WoodSeam;
            public Color WaterBed;
            public Color MarkingWhite;
            public Color HazardYellow;
            public Color HazardDark;
            public Color WalkBandTeal;
            public Color TeamAWarm;
            public Color TeamBCool;

            public static Palette FromStyle(ArenaStyleSO style)
            {
                Color quay = style != null ? style.concreteLight : new Color(0.86f, 0.81f, 0.72f, 1f);
                Color quaySeam = style != null ? style.concreteMid : new Color(0.62f, 0.57f, 0.50f, 1f);
                Color wood = style != null ? style.wood : new Color(0.72f, 0.56f, 0.38f, 1f);
                Color woodDark = style != null ? style.woodDark : new Color(0.44f, 0.31f, 0.20f, 1f);
                return new Palette
                {
                    QuayBase = Color.Lerp(quay, new Color(0.84f, 0.78f, 0.66f, 1f), 0.30f),
                    QuaySeam = quaySeam,
                    WoodLight = Color.Lerp(wood, new Color(0.86f, 0.70f, 0.48f, 1f), 0.30f),
                    WoodDark = woodDark,
                    WoodSeam = Color.Lerp(woodDark, Color.black, 0.18f),
                    WaterBed = new Color(0.055f, 0.16f, 0.20f, 1f),
                    MarkingWhite = new Color(0.94f, 0.92f, 0.85f, 1f),
                    HazardYellow = style != null ? style.hazardYellow : new Color(0.88f, 0.70f, 0.20f, 1f),
                    HazardDark = new Color(0.14f, 0.14f, 0.14f, 1f),
                    WalkBandTeal = style != null ? style.containerTeal : new Color(0.13f, 0.46f, 0.48f, 1f),
                    TeamAWarm = new Color(0.94f, 0.44f, 0.16f, 1f),
                    TeamBCool = new Color(0.16f, 0.40f, 0.78f, 1f),
                };
            }
        }

        public static void BakeAll(ArenaStyleSO style)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Palette pal = Palette.FromStyle(style);

            int albedoSize = AlbedoResolution;
            var albedo = new Color32[albedoSize * albedoSize];
            float step = WorldSizeX / albedoSize;
            for (int y = 0; y < albedoSize; y++)
            {
                float worldZ = WorldMinZ + (y + 0.5f) * (WorldSizeZ / albedoSize);
                int row = y * albedoSize;
                for (int x = 0; x < albedoSize; x++)
                {
                    float worldX = WorldMinX + (x + 0.5f) * step;
                    albedo[row + x] = SampleAlbedo(worldX, worldZ, pal);
                }
            }
            SaveTexture(AlbedoPath, albedo, albedoSize, true);

            // 法线/RMO 走 1024：板缝的坡度在斜视地面时已经足够，减半内存与烘焙时间。
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
                    SampleDetail(worldX, worldZ, out Vector3 n, out float ao, out float smoothness);
                    normal[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 0, 255), 255);
                    rmo[row + x] = new Color32(0, (byte)Mathf.Clamp(Mathf.RoundToInt(ao * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(0.5f * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(smoothness * 255f), 0, 255));
                }
            }
            SaveTexture(NormalPath, normal, detailSize, false);
            SaveTexture(RmoPath, rmo, detailSize, false);

            sw.Stop();
            Debug.Log($"[HarborGroundTextureBaker] 海鸥港湾地面成套烘焙完成：{sw.ElapsedMilliseconds} ms " +
                      $"（albedo {albedoSize}² / normal+RMO {detailSize}²）→ {AlbedoPath}");
        }

        // =====================================================================================
        // 分区与采样
        // =====================================================================================

        private static bool IsWater(float x, float z)
        {
            return Mathf.Abs(z) >= ArenaLayoutHarbor.WaterInnerZ &&
                   Mathf.Abs(z) <= ArenaLayoutHarbor.WaterOuterZ &&
                   Mathf.Abs(x) <= ArenaLayoutHarbor.WaterHalfX;
        }

        /// <summary>木栈道：侧翼车道 + 出生通道（中央坡道引道保留混凝土）。</summary>
        private static bool IsBoardwalk(float x, float z)
        {
            if (Mathf.Abs(x) >= ArenaLayoutHarbor.WaterHalfX) return true;
            float az = Mathf.Abs(z);
            if (az < ArenaLayoutHarbor.WaterOuterZ + 0.25f) return false;
            if (az > ArenaLayoutV2.SpawnTerraceZ + ArenaLayoutV2.SpawnDeckDepth * 0.5f) return true;
            // 登船引道：中央 5.2m 宽混凝土，两侧木板
            return Mathf.Abs(x) > 2.6f;
        }

        private static Color SampleAlbedo(float x, float z, Palette pal)
        {
            if (IsWater(x, z))
            {
                float n = Hash(x * 2.3f, z * 2.3f, FixedSeed) * 0.08f - 0.04f;
                return pal.WaterBed * (1f + n);
            }

            Color color;
            if (IsBoardwalk(x, z))
            {
                color = SampleWood(x, z, pal);
            }
            else
            {
                color = SampleQuay(x, z, pal);
            }

            color = ApplyMarkings(x, z, color, pal);
            color = ApplyWetEdge(x, z, color);
            return color;
        }

        private static Color SampleQuay(float x, float z, Palette pal)
        {
            // 4m 大板 + 2m 细缝：大板负责尺度感，细缝让近景混凝土不糊成一块白面。
            float seam = DistToGrid(z, 4f);
            float seamX = DistToGrid(x, 4f);
            float seamAlpha = SeamMask(Mathf.Min(seam, seamX), 0.030f, 0.012f);
            float fine = Mathf.Min(DistToGrid(z, 2f), DistToGrid(x, 2f));
            float fineAlpha = SeamMask(fine, 0.012f, 0.008f) * 0.55f;
            float slab = Hash(Mathf.Floor(x / 4f), Mathf.Floor(z / 4f), FixedSeed) * 0.05f - 0.025f;
            Color c = pal.QuayBase * (1f + slab);
            // 板面偏冷一点的灰度差，避免整片暖白在强光下没有层次
            float cool = Hash(Mathf.Floor(x / 2f), Mathf.Floor(z / 2f), FixedSeed ^ 0x2C) * 0.03f;
            c = new Color(c.r - cool, c.g - cool * 0.6f, c.b + cool * 0.4f, c.a);
            float grain = (Hash(x * 3.1f, z * 3.1f, FixedSeed ^ 0x51) - 0.5f) * 0.03f;
            c = new Color(c.r + grain, c.g + grain, c.b + grain, c.a);
            c = Color.Lerp(c, pal.QuaySeam, fineAlpha);
            return Color.Lerp(c, pal.QuaySeam, seamAlpha * 0.55f);
        }

        private static Color SampleWood(float x, float z, Palette pal)
        {
            const float plankWidth = 0.30f;
            const float plankLength = 2.20f;
            int plankIndex = Mathf.FloorToInt(z / plankWidth);
            float offset = Hash(plankIndex, 7, FixedSeed ^ 0xA7) * plankLength;
            float endDist = DistToGrid(x - offset, plankLength);
            float sideDist = DistToGrid(z, plankWidth);

            float plankTone = Hash(plankIndex, 3, FixedSeed ^ 0x33);
            Color plankColor = Color.Lerp(pal.WoodDark, pal.WoodLight, 0.55f + plankTone * 0.45f);

            // 木纹：沿板长方向的细条纹 + 低频斑驳
            float grain = Mathf.Sin((x * 9.0f + plankTone * 40f)) * 0.5f + 0.5f;
            float stripe = (Hash(Mathf.Floor(x * 6f), plankIndex, FixedSeed ^ 0x77) - 0.5f) * 0.10f;
            plankColor *= (1f + stripe + (grain - 0.5f) * 0.035f);

            // 板缝：纵向板缝更深，横向断头缝略浅
            float sideSeam = SeamMask(sideDist, 0.012f, 0.010f);
            float endSeam = SeamMask(endDist, 0.016f, 0.010f);
            Color c = Color.Lerp(plankColor, pal.WoodSeam, sideSeam * 0.65f);
            c = Color.Lerp(c, pal.WoodSeam, endSeam * 0.45f);

            // 钉头：每块板两端各一枚，2cm 见方
            if (endDist < 0.10f && Mathf.Abs(sideDist - plankWidth * 0.5f) > 0.02f)
            {
                float nail = (1f - Mathf.Clamp01(endDist / 0.10f)) * 0.5f;
                c = Color.Lerp(c, pal.WoodDark, nail * 0.35f);
            }

            return c;
        }

        private static Color ApplyMarkings(float x, float z, Color color, Palette pal)
        {
            float az = Mathf.Abs(z);
            float ax = Mathf.Abs(x);

            // 1. 水线沿口白色安全线（x=±15.2）
            float waterEdge = Mathf.Abs(ax - ArenaLayoutHarbor.WaterHalfX);
            if (waterEdge < 0.11f && az > ArenaLayoutHarbor.WaterOuterZ && az < 22.4f)
            {
                color = Color.Lerp(color, pal.MarkingWhite, 0.70f);
            }

            // 2. 侧翼车道虚线（x=±17.6，2.4m 实 / 1.8m 空）
            float laneDist = Mathf.Abs(ax - 17.6f);
            float dash = Mathf.Repeat(z + 1.2f, 4.2f);
            if (laneDist < 0.08f && dash < 2.4f && az < 20.0f)
            {
                color = Color.Lerp(color, pal.MarkingWhite, 0.48f);
            }

            // 3. 登船口黄黑警示带（z=±9.9，x∈[-2.4,2.4]）
            float hazardZ = Mathf.Abs(az - (ArenaLayoutHarbor.WaterOuterZ + 0.12f));
            if (hazardZ < 0.34f && ax < 2.5f)
            {
                bool yellow = Mathf.Repeat((x + z) * 2.2f, 1f) < 0.5f;
                color = Color.Lerp(color, yellow ? pal.HazardYellow : pal.HazardDark, 0.8f);
            }

            // 3.1 水线整条黄安全边 + 港青步道带：让 4.6m 水沟在斜俯视角有明确的沿口读形。
            if (ax <= ArenaLayoutHarbor.WaterHalfX + 0.2f)
            {
                float edge = Mathf.Abs(az - (ArenaLayoutHarbor.WaterOuterZ + 0.30f));
                if (edge < 0.16f)
                {
                    color = Color.Lerp(color, pal.HazardYellow, 0.72f);
                }
                float walkBand = Mathf.Abs(az - (ArenaLayoutHarbor.WaterOuterZ + 0.95f));
                if (walkBand < 0.30f)
                {
                    color = Color.Lerp(color, pal.WalkBandTeal, 0.50f);
                }
            }

            // 4. 队色人字箭头（z=±12.4，指向中场）
            float chevronCenter = z < 0f ? -12.4f : 12.4f;
            float band = Mathf.Abs(z - chevronCenter - (z < 0f ? ax * 0.55f : -ax * 0.55f));
            if (band < 0.36f && ax < 2.4f)
            {
                color = Color.Lerp(color, z < 0f ? pal.TeamAWarm : pal.TeamBCool, 0.62f);
            }

            // 5. 泊位 H 字标（z=±18.6）
            float hCenter = z < 0f ? -18.6f : 18.6f;
            if (Mathf.Abs(z - hCenter) < 1.25f && ax < 1.25f)
            {
                bool vertical = Mathf.Abs(ax - 0.78f) < 0.20f;
                bool cross = Mathf.Abs(z - hCenter) < 0.24f && ax < 1.0f;
                if (vertical || cross) color = Color.Lerp(color, pal.MarkingWhite, 0.55f);
            }

            // 6. 贴墙安全线（x=±19.7 / z=±27.5）
            if (Mathf.Abs(ax - 19.7f) < 0.09f || Mathf.Abs(az - 27.5f) < 0.09f)
            {
                color = Color.Lerp(color, pal.MarkingWhite, 0.40f);
            }

            return color;
        }

        /// <summary>水线附近的湿痕：让木板在靠水一侧压暗一档，读作被海水打湿。</summary>
        private static Color ApplyWetEdge(float x, float z, Color color)
        {
            float dist = Mathf.Abs(Mathf.Abs(z) - ArenaLayoutHarbor.WaterOuterZ);
            if (dist < 1.4f && Mathf.Abs(x) < ArenaLayoutHarbor.WaterHalfX + 1.0f)
            {
                float wet = 1f - Mathf.Clamp01(dist / 1.4f);
                float noise = Hash(x * 7f, z * 7f, FixedSeed ^ 0x9E) * 0.35f + 0.65f;
                color *= 1f - wet * 0.18f * noise;
            }
            return color;
        }

        private static void SampleDetail(float x, float z, out Vector3 normal, out float ao, out float smoothness)
        {
            normal = Vector3.up;
            ao = 1f;
            smoothness = 0.35f;

            if (IsWater(x, z))
            {
                smoothness = 0.62f;
                return;
            }

            float slopeX = 0f;
            float slopeZ = 0f;
            if (IsBoardwalk(x, z))
            {
                const float plankWidth = 0.30f;
                const float plankLength = 2.20f;
                int plankIndex = Mathf.FloorToInt(z / plankWidth);
                float offset = Hash(plankIndex, 7, FixedSeed ^ 0xA7) * plankLength;
                float sideDist = DistToGrid(z, plankWidth);
                float endDist = DistToGrid(x - offset, plankLength);

                // 平滑连续的 V 槽坡度：t=0(槽心)与 t=1(边缘)处斜率恒为 0，与 sign 相乘在槽心无阶跃跳变。
                // 过去使用 Mathf.Sign * mask 造成两邻像素法线 ±0.85 突变，在视角转动时产生剧烈走样闪烁。
                const float seamW = 0.018f;
                if (sideDist < seamW)
                {
                    float t = sideDist / seamW;
                    float groove = 4f * t * (1f - t);
                    slopeZ = Mathf.Sign(DistSigned(z, plankWidth)) * groove * 0.24f;
                }
                if (endDist < seamW)
                {
                    float t = endDist / seamW;
                    float groove = 4f * t * (1f - t);
                    slopeX = Mathf.Sign(DistSigned(x - offset, plankLength)) * groove * 0.18f;
                }

                float sideMask = SeamMask(sideDist, 0.012f, 0.010f);
                float endMask = SeamMask(endDist, 0.016f, 0.010f);
                ao = 1f - Mathf.Max(sideMask, endMask) * 0.22f;
                smoothness = 0.28f;
            }
            else
            {
                float seamZ = DistToGrid(z, 4f);
                float seamX = DistToGrid(x, 4f);
                float seam = Mathf.Min(seamX, seamZ);
                const float quaySeamW = 0.025f;
                if (seam < quaySeamW)
                {
                    float t = seam / quaySeamW;
                    float groove = 4f * t * (1f - t);
                    if (seamZ <= seamX) slopeZ = Mathf.Sign(DistSigned(z, 4f)) * groove * 0.20f;
                    else slopeX = Mathf.Sign(DistSigned(x, 4f)) * groove * 0.20f;
                }

                float mask = SeamMask(seam, 0.025f, 0.012f);
                ao = 1f - mask * 0.25f;
                smoothness = 0.38f;
            }

            normal = new Vector3(-slopeX, -slopeZ, 1f).normalized;
        }

        // =====================================================================================
        // 数学/落盘工具
        // =====================================================================================

        /// <summary>到最近周期边界的距离（0 = 正好落在缝上）。</summary>
        private static float DistToGrid(float value, float period)
        {
            float r = Mathf.Repeat(value, period);
            return Mathf.Min(r, period - r);
        }

        /// <summary>带符号的距离：正值在缝的 + 侧，负值在 - 侧（用于法线朝向）。</summary>
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

        /// <summary>确定性二维哈希 [0,1)。</summary>
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
            // 三线性插值（Trilinear）在 Mipmap 之间平滑过渡，消灭斜视角相机转动时的 LOD 跳跃闪烁
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}