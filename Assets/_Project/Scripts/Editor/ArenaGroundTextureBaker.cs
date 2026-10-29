// ==============================================================================================
// ArenaGroundTextureBaker.cs — P7-B 程序化地面反照率/AO/标线贴图 + 墙面横幅图集烘焙器
//
// UV 分区顺序与 ArenaDecorationBuilder 约定一致：
//   索引 0 = (0.0, 0.0) 左下 (Bottom-Left)  -> 中性奶油底 + 深色 "P1 // TURF" + 波浪/集装箱条纹
//   索引 1 = (0.5, 0.0) 右下 (Bottom-Right) -> 黄黑警示斜纹 + "CARGO" 货运字标
//   索引 2 = (0.0, 0.5) 左上 (Top-Left)     -> Team A 橙底白箭头 + "A" + 队色条
//   索引 3 = (0.5, 0.5) 右上 (Top-Right)    -> Team B 蓝底白箭头 + "B" + 队色条
// （注：Unity 纹理原点在左下角 (0, 0)，U 轴向右，V 轴向上）。
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using P1.Core;
using P1.Environment;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace P1.Editor
{
    /// <summary>
    /// P7-B 竞技场地表反照率与横幅贴图确定性烘焙器：
    /// 1. 生成 2048×2048 地面贴图（世界范围 X∈[-20,20], Z∈[-28,28]）：
    ///    混凝土预制板接缝与明度差、低频微斑驳、软边污渍、场地标线（LaneDash/Chevron/HazardBand/Ring/StencilText/SafetyLine）、
    ///    以及基于场景 Collider 投影的环境软 AO（跳过高台/斜坡/出生台顶面，避免把阴影烘在高台上）。
    /// 2. 生成 1024×1024 横幅图集（2×2 512×512 分区）：
    ///    支持 Team A、Team B、中性码头标语、警示货运图集。
    /// 3. 全过程固定随机种子 20261029，支持单次 < 5 秒高效烘焙与 style 为空时的安全默认色回退。
    /// </summary>
    public static class ArenaGroundTextureBaker
    {
        public const string GroundTexturePath = "Assets/_Project/Art/Textures/Arena/ArenaGround_Albedo.png";
        public const string BannerAtlasPath = "Assets/_Project/Art/Textures/Arena/ArenaBannerAtlas.png";

        public const int GroundResolution = 2048;
        public const int BannerResolution = 1024;
        public const int FixedSeed = 20261029;

        private const float WorldMinX = -20f;
        private const float WorldMaxX = 20f;
        private const float WorldMinZ = -28f;
        private const float WorldMaxZ = 28f;
        private const float WorldSizeX = WorldMaxX - WorldMinX; // 40m
        private const float WorldSizeZ = WorldMaxZ - WorldMinZ; // 56m

        #region 配色与字模

        /// <summary>内部解析的配色方案（style 为 null 时提供完整硬编码默认值兜底）。</summary>
        public readonly struct Palette
        {
            public readonly Color groundBase;
            public readonly Color groundSeam;
            public readonly Color groundMarking;
            public readonly Color groundMarkingAccent;
            public readonly Color hazardYellow;
            public readonly Color hazardDark;
            public readonly Color concreteLight;
            public readonly Color concreteMid;
            public readonly Color concreteDark;
            public readonly Color teamAPad;
            public readonly Color teamBPad;
            public readonly Color containerSlate;

            public Palette(ArenaStyleSO style)
            {
                if (style != null)
                {
                    groundBase = style.groundBase;
                    groundSeam = style.groundSeam;
                    groundMarking = style.groundMarking;
                    groundMarkingAccent = style.groundMarkingAccent;
                    hazardYellow = style.hazardYellow;
                    hazardDark = style.hazardDark;
                    concreteLight = style.concreteLight;
                    concreteMid = style.concreteMid;
                    concreteDark = style.concreteDark;
                    teamAPad = style.teamAPad;
                    teamBPad = style.teamBPad;
                    containerSlate = style.containerSlate;
                }
                else
                {
                    groundBase = new Color(0.72f, 0.69f, 0.62f, 1f);
                    groundSeam = new Color(0.52f, 0.50f, 0.45f, 1f);
                    groundMarking = new Color(0.88f, 0.86f, 0.78f, 1f);
                    groundMarkingAccent = new Color(0.82f, 0.65f, 0.26f, 1f);
                    hazardYellow = new Color(0.86f, 0.68f, 0.20f, 1f);
                    hazardDark = new Color(0.16f, 0.16f, 0.16f, 1f);
                    concreteLight = new Color(0.78f, 0.76f, 0.70f, 1f);
                    concreteMid = new Color(0.62f, 0.60f, 0.55f, 1f);
                    concreteDark = new Color(0.40f, 0.39f, 0.36f, 1f);
                    teamAPad = new Color(1.00f, 0.48f, 0.08f, 1f);
                    teamBPad = new Color(0.15f, 0.35f, 1.00f, 1f);
                    containerSlate = new Color(0.37f, 0.46f, 0.57f, 1f);
                }
            }
        }

        /// <summary>5×7 标准工业字模数据（7 行，每行低 5 位有效，从顶行 row 0 到末行 row 6）。</summary>
        private static readonly byte[,] Font5x7 = InitializeFont();

        private static byte[,] InitializeFont()
        {
            var f = new byte[128, 7];

            void Set(char c, byte r0, byte r1, byte r2, byte r3, byte r4, byte r5, byte r6)
            {
                int idx = c;
                if (idx < 128)
                {
                    f[idx, 0] = r0;
                    f[idx, 1] = r1;
                    f[idx, 2] = r2;
                    f[idx, 3] = r3;
                    f[idx, 4] = r4;
                    f[idx, 5] = r5;
                    f[idx, 6] = r6;
                }
            }

            // 字母 A-Z
            Set('A', 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
            Set('B', 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E);
            Set('C', 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E);
            Set('D', 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E);
            Set('E', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F);
            Set('F', 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10);
            Set('G', 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F);
            Set('H', 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
            Set('I', 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x1F);
            Set('J', 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C);
            Set('K', 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11);
            Set('L', 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F);
            Set('M', 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11);
            Set('N', 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11);
            Set('O', 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
            Set('P', 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10);
            Set('Q', 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D);
            Set('R', 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11);
            Set('S', 0x0E, 0x11, 0x10, 0x0E, 0x01, 0x11, 0x0E);
            Set('T', 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04);
            Set('U', 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
            Set('V', 0x11, 0x11, 0x11, 0x11, 0x0A, 0x0A, 0x04);
            Set('W', 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A);
            Set('X', 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11);
            Set('Y', 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04);
            Set('Z', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F);

            // 数字 0-9
            Set('0', 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E);
            Set('1', 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E);
            Set('2', 0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F);
            Set('3', 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E);
            Set('4', 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02);
            Set('5', 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E);
            Set('6', 0x0E, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x0E);
            Set('7', 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08);
            Set('8', 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E);
            Set('9', 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E);

            // 标点与空白
            Set(' ', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            Set('/', 0x01, 0x02, 0x04, 0x04, 0x08, 0x10, 0x10);
            Set('-', 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00);
            Set('.', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04);

            return f;
        }

        private static bool GetFontBit(char c, int col, int row)
        {
            c = char.ToUpperInvariant(c);
            int idx = c;
            if (idx < 0 || idx >= 128) return false;
            if (col < 0 || col > 4 || row < 0 || row > 6) return false;
            byte r = Font5x7[idx, row];
            return ((r >> (4 - col)) & 1) != 0;
        }

        #endregion

        #region 公开菜单与 API

        [MenuItem("P1/关卡与地图/烘焙地面反照率与横幅贴图 (P7-B)", false, 35)]
        public static void BakeAllMenuItem()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>("Assets/_Project/ScriptableObjects/ArenaStyle.asset");
            if (style == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:ArenaStyleSO");
                if (guids != null && guids.Length > 0)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guids[0]);
                    style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(p);
                }
            }
            BakeAll(style);
        }

        /// <summary>烘焙所有地面与横幅贴图并落盘，触发 AssetDatabase 导入。</summary>
        public static void BakeAll(ArenaStyleSO style)
        {
            var sw = Stopwatch.StartNew();
            var palette = new Palette(style);

            int aoCollidersCount = BakeGroundTexture(palette);
            BakeBannerAtlas(palette);

            sw.Stop();
            Debug.Log($"[ArenaGroundTextureBaker] 烘焙全部完成！总耗时: {sw.ElapsedMilliseconds} ms, AO 覆盖碰撞体数量: {aoCollidersCount}。\n" +
                      $" - 地面反照率贴图: {GroundTexturePath} ({GroundResolution}x{GroundResolution} RGB)\n" +
                      $" - 横幅图集: {BannerAtlasPath} ({BannerResolution}x{BannerResolution} RGBA)");
        }

        #endregion

        #region 地面贴图烘焙 (2048×2048, RGB)

        public static int BakeGroundTexture(ArenaStyleSO style)
        {
            return BakeGroundTexture(new Palette(style));
        }

        public static int BakeGroundTexture(Palette palette)
        {
            var sw = Stopwatch.StartNew();

            // 1. 预分配像素颜色缓存（RGB 浮点）
            var ground = new Color[GroundResolution * GroundResolution];

            // 2. 预生成 0.5m 尺度低频微斑驳网格（82×114 个点覆盖 40×56m，双线性插值避免每像素频繁算哈希）
            const int mottleGridW = 82;
            const int mottleGridH = 114;
            var mottleGrid = new float[mottleGridW, mottleGridH];
            var mottleRng = new System.Random(FixedSeed ^ 0x55AA);
            for (int gz = 0; gz < mottleGridH; gz++)
            {
                for (int gx = 0; gx < mottleGridW; gx++)
                {
                    // ±3% 斑驳扰动
                    mottleGrid[gx, gz] = ((float)mottleRng.NextDouble() * 2f - 1f) * 0.03f;
                }
            }

            // 3. 第一阶段：基底混凝土板块（2m×2m 预制板 + 确定性哈希 ±4% 明度差 + 3cm 板缝 + 0.5m 微斑驳）
            float stepX = WorldSizeX / GroundResolution; // ~0.01953m
            float stepZ = WorldSizeZ / GroundResolution; // ~0.02734m
            float seamHalfWidth = 0.015f; // 3cm 宽缝的一半
            float seamEdgeSoftness = 0.015f; // 抗锯齿软边宽度

            for (int y = 0; y < GroundResolution; y++)
            {
                float worldZ = WorldMinZ + (y + 0.5f) * stepZ;
                int slabZ = Mathf.FloorToInt((worldZ - WorldMinZ) * 0.5f);
                float distSeamZ = Mathf.Abs(worldZ - Mathf.Round(worldZ * 0.5f) * 2f);

                // 斑驳 Z 轴采样
                float cellZ = (worldZ - WorldMinZ) * 2f;
                int gz0 = Mathf.Clamp(Mathf.FloorToInt(cellZ), 0, mottleGridH - 2);
                float tz = cellZ - gz0;
                float sz = tz * tz * (3f - 2f * tz);

                int rowOffset = y * GroundResolution;

                for (int x = 0; x < GroundResolution; x++)
                {
                    float worldX = WorldMinX + (x + 0.5f) * stepX;
                    int slabX = Mathf.FloorToInt((worldX - WorldMinX) * 0.5f);

                    // 板块确定性明度差 ±4%
                    float slabBrightnessDelta = Hash2DToRange(slabX, slabZ, FixedSeed, -0.04f, 0.04f);
                    Color pixelColor = palette.groundBase * (1.0f + slabBrightnessDelta);

                    // 板块接缝 3cm（smoothstep 软边抗锯齿）
                    float distSeamX = Mathf.Abs(worldX - Mathf.Round(worldX * 0.5f) * 2f);
                    float distSeam = Mathf.Min(distSeamX, distSeamZ);
                    float seamAlpha = 1.0f - SmoothStep01(seamHalfWidth - seamEdgeSoftness * 0.5f,
                                                          seamHalfWidth + seamEdgeSoftness * 0.5f,
                                                          distSeam);
                    if (seamAlpha > 0.001f)
                    {
                        pixelColor = Color.Lerp(pixelColor, palette.groundSeam, seamAlpha);
                    }

                    // 0.5m 尺度低频微斑驳
                    float cellX = (worldX - WorldMinX) * 2f;
                    int gx0 = Mathf.Clamp(Mathf.FloorToInt(cellX), 0, mottleGridW - 2);
                    float tx = cellX - gx0;
                    float sx = tx * tx * (3f - 2f * tx);

                    float m00 = mottleGrid[gx0, gz0];
                    float m10 = mottleGrid[gx0 + 1, gz0];
                    float m01 = mottleGrid[gx0, gz0 + 1];
                    float m11 = mottleGrid[gx0 + 1, gz0 + 1];
                    float mottle = (m00 * (1f - sx) + m10 * sx) * (1f - sz) + (m01 * (1f - sx) + m11 * sx) * sz;
                    pixelColor *= (1.0f + mottle);

                    ground[rowOffset + x] = pixelColor;
                }
            }

            // 4. 第二阶段：大尺度软边深色污渍（固定种子，10 个半径 1.5-4m 的深色椭圆，alpha 0.04-0.08，不抢墨水）
            ApplyDirtStains(ground, palette);

            // 5. 第三阶段：标线绘制（遍历 ArenaLayoutV2.GroundMarkings，1.5px 软边抗锯齿）
            ApplyGroundMarkings(ground, palette);

            // 6. 第四阶段：场景环境 AO（基于 Arena_40x56 / Arena_40x40 下所有 Collider 投影软阴影，避开高台顶面）
            int aoColliderCount = ApplyEnvironmentAO(ground);

            // 7. 转为 Color32 数组并写入 PNG
            var outputColors = new Color32[GroundResolution * GroundResolution];
            for (int i = 0; i < outputColors.Length; i++)
            {
                Color c = ground[i];
                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                outputColors[i] = new Color32(r, g, b, 255);
            }

            var groundTex = new Texture2D(GroundResolution, GroundResolution, TextureFormat.RGB24, false);
            groundTex.SetPixels32(outputColors);
            groundTex.Apply(false);

            byte[] pngBytes = groundTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(groundTex);

            string directory = Path.GetDirectoryName(GroundTexturePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(GroundTexturePath, pngBytes);
            AssetDatabase.ImportAsset(GroundTexturePath, ImportAssetOptions.ForceUpdate);

            sw.Stop();
            Debug.Log($"[ArenaGroundTextureBaker] 地面反照率贴图已生成: {GroundTexturePath}，耗时: {sw.ElapsedMilliseconds} ms，AO 碰撞体: {aoColliderCount}");
            return aoColliderCount;
        }

        #endregion

        #region 污渍与标线绘制

        private static void ApplyDirtStains(Color[] ground, Palette palette)
        {
            var rng = new System.Random(FixedSeed);
            int count = 10;
            Color stainBase = palette.concreteDark * 0.7f;

            for (int i = 0; i < count; i++)
            {
                float cx = (float)(rng.NextDouble() * 32.0 - 16.0); // X ∈ [-16, 16]
                float cz = (float)(rng.NextDouble() * 44.0 - 22.0); // Z ∈ [-22, 22]
                float rx = (float)(1.5 + rng.NextDouble() * 2.5);   // 1.5 ~ 4.0m
                float rz = (float)(1.5 + rng.NextDouble() * 2.5);
                float angle = (float)(rng.NextDouble() * Mathf.PI);
                float alpha = (float)(0.04 + rng.NextDouble() * 0.04); // 0.04 ~ 0.08

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                float maxR = Mathf.Max(rx, rz);
                int minPx = Mathf.Clamp(Mathf.FloorToInt((cx - maxR - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int maxPx = Mathf.Clamp(Mathf.CeilToInt((cx + maxR - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int minPy = Mathf.Clamp(Mathf.FloorToInt((cz - maxR - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
                int maxPy = Mathf.Clamp(Mathf.CeilToInt((cz + maxR - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

                for (int py = minPy; py <= maxPy; py++)
                {
                    float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                    int row = py * GroundResolution;

                    for (int px = minPx; px <= maxPx; px++)
                    {
                        float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;

                        float dx = wx - cx;
                        float dz = wz - cz;
                        float lx = dx * cos + dz * sin;
                        float lz = -dx * sin + dz * cos;

                        float normDist = (lx * lx) / (rx * rx) + (lz * lz) / (rz * rz);
                        if (normDist < 1.0f)
                        {
                            float t = 1.0f - Mathf.Sqrt(normDist);
                            float falloff = t * t * (3f - 2f * t); // smoothstep
                            float blend = falloff * alpha;
                            ground[row + px] = Color.Lerp(ground[row + px], stainBase, blend);
                        }
                    }
                }
            }
        }

        private static void ApplyGroundMarkings(Color[] ground, Palette palette)
        {
            var markings = ArenaLayoutV2.GroundMarkings;
            if (markings == null || markings.Count == 0) return;

            // 1.5 像素对应的世界尺寸 (~0.035m)
            float pxWorld = (WorldSizeX / GroundResolution + WorldSizeZ / GroundResolution) * 0.5f;
            float edgeSoftness = 1.5f * pxWorld;

            foreach (var marking in markings)
            {
                switch (marking.Kind)
                {
                    case ArenaLayoutV2.GroundMarkingKind.LaneDash:
                        DrawLaneDash(ground, marking, palette, edgeSoftness);
                        break;
                    case ArenaLayoutV2.GroundMarkingKind.Chevron:
                        DrawChevron(ground, marking, palette, edgeSoftness);
                        break;
                    case ArenaLayoutV2.GroundMarkingKind.HazardBand:
                        DrawHazardBand(ground, marking, palette, edgeSoftness);
                        break;
                    case ArenaLayoutV2.GroundMarkingKind.Ring:
                        DrawRing(ground, marking, palette, edgeSoftness);
                        break;
                    case ArenaLayoutV2.GroundMarkingKind.StencilText:
                        DrawStencilText(ground, marking, palette, edgeSoftness, pxWorld);
                        break;
                    case ArenaLayoutV2.GroundMarkingKind.SafetyLine:
                        DrawSafetyLine(ground, marking, palette, edgeSoftness);
                        break;
                }
            }
        }

        private static void DrawLaneDash(Color[] ground, ArenaLayoutV2.GroundMarking m, Palette palette, float edge)
        {
            // 沿较长轴画 2m 实 / 1.5m 空虚线，线宽取短边；颜色 groundMarking
            bool isLongZ = m.SizeXZ.y >= m.SizeXZ.x;
            float length = isLongZ ? m.SizeXZ.y : m.SizeXZ.x;
            float width = isLongZ ? m.SizeXZ.x : m.SizeXZ.y;

            float rad = -m.Yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float halfX = m.SizeXZ.x * 0.5f;
            float halfZ = m.SizeXZ.y * 0.5f;
            float bound = Mathf.Max(halfX, halfZ) + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float lx = dx * cos - dz * sin;
                    float lz = dx * sin + dz * cos;

                    float along = isLongZ ? lz : lx;
                    float across = isLongZ ? lx : lz;

                    float distAcross = width * 0.5f - Mathf.Abs(across);
                    float distEnd = length * 0.5f - Mathf.Abs(along);

                    // 虚线周期 3.5m（2.0m 实 / 1.5m 空）
                    float pos = along + length * 0.5f;
                    float cycle = Mathf.Repeat(pos, 3.5f);
                    float distDash = 1.0f - Mathf.Abs(cycle - 1.0f); // cycle ∈ [0, 2] 为实线，中点 1.0，半宽 1.0

                    float dist = Mathf.Min(distAcross, Mathf.Min(distEnd, distDash));
                    float t = Mathf.Clamp01((dist + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t);

                    if (alpha > 0.001f)
                    {
                        ground[row + px] = Color.Lerp(ground[row + px], palette.groundMarking, alpha);
                    }
                }
            }
        }

        private static void DrawChevron(Color[] ground, ArenaLayoutV2.GroundMarking m, Palette palette, float edge)
        {
            // 在矩形内画 2-3 道嵌套 V 形箭头，默认指向 +Z，再按 Yaw 旋转
            // TeamA/TeamB 用风格队色与白色混合，Neutral 用 groundMarkingAccent
            Color chevronColor;
            switch (m.Team)
            {
                case Team.TeamA: chevronColor = Color.Lerp(palette.teamAPad, Color.white, 0.40f); break;
                case Team.TeamB: chevronColor = Color.Lerp(palette.teamBPad, Color.white, 0.40f); break;
                default: chevronColor = palette.groundMarkingAccent; break;
            }

            float rad = -m.Yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float halfX = m.SizeXZ.x * 0.5f;
            float halfZ = m.SizeXZ.y * 0.5f;
            float bound = Mathf.Max(halfX, halfZ) + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            int stripeCount = m.SizeXZ.y > 2.0f ? 3 : 2;
            float thickness = Mathf.Clamp(m.SizeXZ.y * 0.15f, 0.18f, 0.35f);
            float slope = Mathf.Min(0.85f, (halfZ * 0.75f) / halfX);
            float norm = Mathf.Sqrt(1f + slope * slope);
            float stride = (m.SizeXZ.y - thickness) / (stripeCount + 0.2f);
            float peakBase = halfZ - thickness * 0.5f;

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float lx = dx * cos - dz * sin;
                    float lz = dx * sin + dz * cos;

                    if (Mathf.Abs(lx) > halfX + edge || Mathf.Abs(lz) > halfZ + edge)
                        continue;

                    float distChevron = -999f;
                    for (int s = 0; s < stripeCount; s++)
                    {
                        float peakZ = peakBase - s * stride;
                        float armZ = peakZ - slope * Mathf.Abs(lx);
                        float d = Mathf.Abs(lz - armZ) / norm;
                        float distArm = thickness * 0.5f - d;
                        float distBoxX = halfX - Mathf.Abs(lx);
                        float distStripe = Mathf.Min(distArm, distBoxX);
                        if (distStripe > distChevron) distChevron = distStripe;
                    }

                    float t = Mathf.Clamp01((distChevron + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t);
                    if (alpha > 0.001f)
                    {
                        ground[row + px] = Color.Lerp(ground[row + px], chevronColor, alpha);
                    }
                }
            }
        }

        private static void DrawHazardBand(Color[] ground, ArenaLayoutV2.GroundMarking m, Palette palette, float edge)
        {
            // 矩形内 45° 黄黑条纹，黄 hazardYellow，黑 hazardDark
            float rad = -m.Yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float halfX = m.SizeXZ.x * 0.5f;
            float halfZ = m.SizeXZ.y * 0.5f;
            float bound = Mathf.Max(halfX, halfZ) + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            float stripePeriod = 0.35f;

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float lx = dx * cos - dz * sin;
                    float lz = dx * sin + dz * cos;

                    float distRect = Mathf.Min(halfX - Mathf.Abs(lx), halfZ - Mathf.Abs(lz));
                    float tRect = Mathf.Clamp01((distRect + edge * 0.5f) / edge);
                    float rectAlpha = tRect * tRect * (3f - 2f * tRect);
                    if (rectAlpha <= 0.001f) continue;

                    float coord = (lx + lz) * 0.70710678f;
                    float phase = Mathf.Repeat(coord, stripePeriod) / stripePeriod;
                    float stripeAlpha = SmoothStep01(0.5f - 0.05f, 0.5f + 0.05f, phase);

                    Color stripeColor = Color.Lerp(palette.hazardYellow, palette.hazardDark, stripeAlpha);
                    ground[row + px] = Color.Lerp(ground[row + px], stripeColor, rectAlpha);
                }
            }
        }

        private static void DrawRing(Color[] ground, ArenaLayoutV2.GroundMarking m, Palette palette, float edge)
        {
            // 圆环：半径 size.x，线宽 size.y
            float radius = m.SizeXZ.x;
            float lineWidth = m.SizeXZ.y;
            float bound = radius + lineWidth * 0.5f + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);

                    float distRing = lineWidth * 0.5f - Mathf.Abs(r - radius);
                    float t = Mathf.Clamp01((distRing + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t);

                    if (alpha > 0.001f)
                    {
                        ground[row + px] = Color.Lerp(ground[row + px], palette.groundMarking, alpha);
                    }
                }
            }
        }

        private static void DrawSafetyLine(Color[] ground, ArenaLayoutV2.GroundMarking m, Palette palette, float edge)
        {
            // 实线，hazardYellow，透明度 0.75
            float rad = -m.Yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float halfX = m.SizeXZ.x * 0.5f;
            float halfZ = m.SizeXZ.y * 0.5f;
            float bound = Mathf.Max(halfX, halfZ) + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float lx = dx * cos - dz * sin;
                    float lz = dx * sin + dz * cos;

                    float dist = Mathf.Min(halfX - Mathf.Abs(lx), halfZ - Mathf.Abs(lz));
                    float t = Mathf.Clamp01((dist + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t) * 0.75f;

                    if (alpha > 0.001f)
                    {
                        ground[row + px] = Color.Lerp(ground[row + px], palette.hazardYellow, alpha);
                    }
                }
            }
        }

        private static void DrawStencilText(
            Color[] ground,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge,
            float pxWorld)
        {
            if (string.IsNullOrEmpty(m.Text)) return;
            string text = m.Text;
            int len = text.Length;
            int gridW = len * 6 - 1;
            int gridH = 7;

            float scaleX = (m.SizeXZ.x * 0.85f) / gridW;
            float scaleZ = (m.SizeXZ.y * 0.85f) / gridH;
            float scale = Mathf.Min(scaleX, scaleZ);
            if (scale <= 0.0001f) return;

            float textW = gridW * scale;
            float textH = gridH * scale;

            Color textColor;
            switch (m.Team)
            {
                case Team.TeamA: textColor = palette.teamAPad; break;
                case Team.TeamB: textColor = palette.teamBPad; break;
                default: textColor = palette.groundMarking; break;
            }

            float rad = -m.Yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            float bound = Mathf.Max(m.SizeXZ.x, m.SizeXZ.y) * 0.6f + edge;
            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            float sub = 0.25f * pxWorld;

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int rowOffset = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - m.CenterXZ.x;
                    float dz = wz - m.CenterXZ.y;
                    float lx = dx * cos - dz * sin;
                    float lz = dx * sin + dz * cos;

                    if (Mathf.Abs(lx) > textW * 0.5f + pxWorld || Mathf.Abs(lz) > textH * 0.5f + pxWorld)
                        continue;

                    // 2×2 超采样进行软边抗锯齿
                    float hits = 0f;
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        float slz = lz + sy * sub;
                        // top row (0) at +lz
                        float v = (textH * 0.5f - slz) / scale;
                        int row = Mathf.FloorToInt(v);
                        if (row < 0 || row > 6) continue;

                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            float slx = lx + sx * sub;
                            // col (0) at -lx
                            float u = (slx + textW * 0.5f) / scale;
                            if (u < 0f || u >= gridW) continue;

                            int charIdx = Mathf.FloorToInt(u / 6.0f);
                            float charU = u - charIdx * 6.0f;
                            if (charU < 5.0f && charIdx < len)
                            {
                                int col = Mathf.FloorToInt(charU);
                                if (GetFontBit(text[charIdx], col, row))
                                {
                                    hits += 0.25f;
                                }
                            }
                        }
                    }

                    if (hits > 0.001f)
                    {
                        ground[rowOffset + px] = Color.Lerp(ground[rowOffset + px], textColor, hits);
                    }
                }
            }
        }

        #endregion

        #region 环境 AO (软阴影 + 排除高台顶面)

        private sealed class TopSurfaceArea
        {
            public BoxCollider box;
            public Transform transform;
            public Bounds bounds;

            public bool Contains(float worldX, float worldZ)
            {
                if (box != null && transform != null)
                {
                    Vector3 local = transform.InverseTransformPoint(new Vector3(worldX, transform.position.y, worldZ));
                    local -= box.center;
                    return Mathf.Abs(local.x) <= Mathf.Abs(box.size.x) * 0.5f &&
                           Mathf.Abs(local.z) <= Mathf.Abs(box.size.z) * 0.5f;
                }
                return worldX >= bounds.min.x && worldX <= bounds.max.x &&
                       worldZ >= bounds.min.z && worldZ <= bounds.max.z;
            }
        }

        private sealed class ShadowCaster
        {
            public BoxCollider box;
            public Transform transform;
            public Bounds bounds;

            public bool CalcDistance(float worldX, float worldZ, out bool isInside, out float dist)
            {
                if (box != null && transform != null)
                {
                    Vector3 local = transform.InverseTransformPoint(new Vector3(worldX, transform.position.y, worldZ));
                    local -= box.center;
                    float hx = Mathf.Abs(box.size.x) * 0.5f;
                    float hz = Mathf.Abs(box.size.z) * 0.5f;
                    float sx = Mathf.Abs(transform.lossyScale.x);
                    float sz = Mathf.Abs(transform.lossyScale.z);

                    float dx = Mathf.Max(0f, Mathf.Abs(local.x) - hx) * sx;
                    float dz = Mathf.Max(0f, Mathf.Abs(local.z) - hz) * sz;

                    isInside = Mathf.Abs(local.x) <= hx && Mathf.Abs(local.z) <= hz;
                    dist = Mathf.Sqrt(dx * dx + dz * dz);
                    return true;
                }
                else
                {
                    float dx = Mathf.Max(0f, Mathf.Max(bounds.min.x - worldX, worldX - bounds.max.x));
                    float dz = Mathf.Max(0f, Mathf.Max(bounds.min.z - worldZ, worldZ - bounds.max.z));
                    isInside = worldX >= bounds.min.x && worldX <= bounds.max.x &&
                               worldZ >= bounds.min.z && worldZ <= bounds.max.z;
                    dist = Mathf.Sqrt(dx * dx + dz * dz);
                    return true;
                }
            }
        }

        private static int ApplyEnvironmentAO(Color[] ground)
        {
            var arenaRoot = GameObject.Find("Arena_40x56");
            if (arenaRoot == null)
            {
                arenaRoot = GameObject.Find("Arena_40x40");
            }

            if (arenaRoot == null)
            {
                Debug.LogWarning("[ArenaGroundTextureBaker] 未在当前场景找到 Arena_40x56 或 Arena_40x40 根节点，跳过环境 AO 阴影烘焙。");
                return 0;
            }

            var allColliders = arenaRoot.GetComponentsInChildren<Collider>(false);
            if (allColliders == null || allColliders.Length == 0) return 0;

            // 1. 收集高台/斜坡/出生台顶面（避免把阴影烘在高台上）
            var topSurfaces = new List<TopSurfaceArea>(16);
            foreach (var col in allColliders)
            {
                if (col == null || col.isTrigger) continue;
                string n = col.gameObject.name;
                if (n.StartsWith("HighGround_", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Ramp_", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("SafeSpawnPad_", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("SpawnTerrace_", StringComparison.OrdinalIgnoreCase))
                {
                    topSurfaces.Add(new TopSurfaceArea
                    {
                        box = col as BoxCollider,
                        transform = col.transform,
                        bounds = col.bounds
                    });
                }
            }

            // 预构建顶面快速遮罩
            var excludedMask = new bool[GroundResolution * GroundResolution];
            foreach (var top in topSurfaces)
            {
                Bounds b = top.bounds;
                int minPx = Mathf.Clamp(Mathf.FloorToInt((b.min.x - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int maxPx = Mathf.Clamp(Mathf.CeilToInt((b.max.x - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int minPy = Mathf.Clamp(Mathf.FloorToInt((b.min.z - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
                int maxPy = Mathf.Clamp(Mathf.CeilToInt((b.max.z - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

                for (int py = minPy; py <= maxPy; py++)
                {
                    float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                    int row = py * GroundResolution;
                    for (int px = minPx; px <= maxPx; px++)
                    {
                        float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                        if (top.Contains(wx, wz))
                        {
                            excludedMask[row + px] = true;
                        }
                    }
                }
            }

            // 2. 收集投射阴影的 Collider（跳过 isTrigger、跳过 bounds.max.y > 4 的高空装饰，跳过主地面自身）
            var casters = new List<ShadowCaster>(allColliders.Length);
            foreach (var col in allColliders)
            {
                if (col == null || col.isTrigger) continue;
                if (col.bounds.max.y > 4.0f) continue;
                string n = col.gameObject.name;
                if (n.StartsWith("Ground_Main", StringComparison.OrdinalIgnoreCase) ||
                    (col.bounds.size.x >= 35f && col.bounds.size.z >= 35f))
                {
                    continue;
                }

                casters.Add(new ShadowCaster
                {
                    box = col as BoxCollider,
                    transform = col.transform,
                    bounds = col.bounds
                });
            }

            // 3. 计算软阴影：外围 0.7m 软阴影（最大压暗 22%），内部轻压暗 10%
            var aoMap = new float[GroundResolution * GroundResolution];
            for (int i = 0; i < aoMap.Length; i++) aoMap[i] = 1.0f;

            float shadowRadius = 0.7f;
            foreach (var caster in casters)
            {
                Bounds b = caster.bounds;
                float minWX = b.min.x - shadowRadius;
                float maxWX = b.max.x + shadowRadius;
                float minWZ = b.min.z - shadowRadius;
                float maxWZ = b.max.z + shadowRadius;

                int minPx = Mathf.Clamp(Mathf.FloorToInt((minWX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int maxPx = Mathf.Clamp(Mathf.CeilToInt((maxWX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int minPy = Mathf.Clamp(Mathf.FloorToInt((minWZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
                int maxPy = Mathf.Clamp(Mathf.CeilToInt((maxWZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

                for (int py = minPy; py <= maxPy; py++)
                {
                    float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                    int row = py * GroundResolution;

                    for (int px = minPx; px <= maxPx; px++)
                    {
                        int idx = row + px;
                        if (excludedMask[idx]) continue;

                        float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                        if (caster.CalcDistance(wx, wz, out bool isInside, out float dist))
                        {
                            float factor = 1.0f;
                            if (isInside)
                            {
                                factor = 0.90f; // 矩形内部轻压暗 10%
                            }
                            else if (dist < shadowRadius)
                            {
                                float t = 1.0f - dist / shadowRadius;
                                float smooth = t * t * (3f - 2f * t);
                                factor = 1.0f - 0.22f * smooth; // 矩形外 0.7m 最大压暗 22%
                            }

                            if (factor < aoMap[idx])
                            {
                                aoMap[idx] = factor;
                            }
                        }
                    }
                }
            }

            // 4. 将 AO 压暗因子合成到地面
            for (int i = 0; i < ground.Length; i++)
            {
                ground[i] *= aoMap[i];
            }

            return casters.Count;
        }

        #endregion

        #region 横幅图集烘焙 (1024×1024, RGBA)

        public static void BakeBannerAtlas(ArenaStyleSO style)
        {
            BakeBannerAtlas(new Palette(style));
        }

        public static void BakeBannerAtlas(Palette palette)
        {
            var sw = Stopwatch.StartNew();

            // 1024×1024，RGBA32，四块 512×512
            var pixels = new Color32[BannerResolution * BannerResolution];

            // 索引 0: (0, 0) 左下 -> 中性奶油底 + 深色 "P1 // TURF" + 波浪/集装箱条纹
            BakeQuadrantNeutralTurf(pixels, 0, 0, palette);

            // 索引 1: (0.5, 0) 右下 -> 黄黑警示斜纹 + "CARGO"
            BakeQuadrantHazardCargo(pixels, 512, 0, palette);

            // 索引 2: (0, 0.5) 左上 -> Team A 橙底白箭头 + "A" + 队色条
            BakeQuadrantTeamA(pixels, 0, 512, palette);

            // 索引 3: (0.5, 0.5) 右上 -> Team B 蓝底白箭头 + "B" + 队色条
            BakeQuadrantTeamB(pixels, 512, 512, palette);

            var bannerTex = new Texture2D(BannerResolution, BannerResolution, TextureFormat.RGBA32, false);
            bannerTex.SetPixels32(pixels);
            bannerTex.Apply(false);

            byte[] pngBytes = bannerTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(bannerTex);

            string directory = Path.GetDirectoryName(BannerAtlasPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(BannerAtlasPath, pngBytes);
            AssetDatabase.ImportAsset(BannerAtlasPath, ImportAssetOptions.ForceUpdate);

            sw.Stop();
            Debug.Log($"[ArenaGroundTextureBaker] 横幅图集已生成: {BannerAtlasPath}，耗时: {sw.ElapsedMilliseconds} ms");
        }

        private static void BakeQuadrantTeamA(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 左上：Team A 橙底白箭头 + "A" + 队色条
            Color32 teamAColor = (Color32)palette.teamAPad;
            Color32 white = new Color32(255, 255, 255, 255);

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    Color32 bg = teamAColor;

                    // 队色条：上下白色竞速饰条
                    if ((qy >= 435 && qy <= 465) || (qy >= 45 && qy <= 75))
                    {
                        bg = white;
                    }
                    else if ((qy >= 425 && qy <= 430) || (qy >= 80 && qy <= 85))
                    {
                        bg = new Color32(210, 80, 10, 255);
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    bg.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = bg;
                }
            }

            // 白箭头（向上 V 形指示标）
            DrawBannerChevron(pixels, startX, startY, 256f, 395f, 210f, 65f, 24f, white);

            // 大号字母 "A"（5×7 放大 28 倍，高 196px，居中）
            DrawBannerText(pixels, startX, startY, "A", 256f, 225f, 28f, white);
        }

        private static void BakeQuadrantTeamB(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 右上：Team B 蓝底白箭头 + "B" + 队色条
            Color32 teamBColor = (Color32)palette.teamBPad;
            Color32 white = new Color32(255, 255, 255, 255);

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    Color32 bg = teamBColor;

                    // 队色条：上下白色竞速饰条
                    if ((qy >= 435 && qy <= 465) || (qy >= 45 && qy <= 75))
                    {
                        bg = white;
                    }
                    else if ((qy >= 425 && qy <= 430) || (qy >= 80 && qy <= 85))
                    {
                        bg = new Color32(20, 50, 180, 255);
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    bg.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = bg;
                }
            }

            // 白箭头
            DrawBannerChevron(pixels, startX, startY, 256f, 395f, 210f, 65f, 24f, white);

            // 大号字母 "B"
            DrawBannerText(pixels, startX, startY, "B", 256f, 225f, 28f, white);
        }

        private static void BakeQuadrantNeutralTurf(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 左下：中性奶油底 + 深色 "P1 // TURF" + 波浪/集装箱条纹
            Color creamBase = new Color(0.92f, 0.90f, 0.83f, 1f);
            Color containerSlate = palette.containerSlate;
            Color concreteDark = palette.concreteDark;

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    Color c = creamBase;

                    // 顶部深色条带
                    if (qy >= 420 && qy <= 465)
                    {
                        c = containerSlate;
                    }
                    // 集装箱/波浪竖向瓦楞条纹（下半部区域）
                    else if (qy >= 45 && qy <= 210)
                    {
                        float wave = Mathf.Sin(qx * (Mathf.PI * 2f / 24f));
                        c = Color.Lerp(creamBase, palette.concreteMid, 0.22f + 0.16f * wave);
                    }
                    // 标语装饰横线
                    else if ((qy >= 250 && qy <= 254) || (qy >= 366 && qy <= 370))
                    {
                        c = concreteDark * 0.85f;
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 顶部小号标识
            DrawBannerText(pixels, startX, startY, "CONTAINER TERMINAL", 256f, 442f, 3.5f, new Color32(240, 240, 240, 255));

            // 主标题深色 "P1 // TURF"
            Color32 textDark = new Color32(32, 34, 38, 255);
            DrawBannerText(pixels, startX, startY, "P1 // TURF", 256f, 310f, 7.0f, textDark);
        }

        private static void BakeQuadrantHazardCargo(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 右下：黄黑警示斜纹 + "CARGO"
            Color yellow = palette.hazardYellow;
            Color dark = palette.hazardDark;

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    // 45° 黄黑条纹底纹
                    float coord = (qx + qy) * 0.70710678f;
                    float phase = Mathf.Repeat(coord, 40f) / 40f;
                    float stripe = SmoothStep01(0.5f - 0.04f, 0.5f + 0.04f, phase);
                    Color c = Color.Lerp(yellow, dark, stripe);

                    // 中央深色货运牌照框 (380×160)
                    float pdx = Mathf.Max(0f, Mathf.Abs(qx - 256f) - (190f - 12f));
                    float pdy = Mathf.Max(0f, Mathf.Abs(qy - 256f) - (80f - 12f));
                    float pdist = Mathf.Sqrt(pdx * pdx + pdy * pdy) - 12f;

                    if (pdist <= 0f)
                    {
                        // 4px 亮黄色边框
                        if (pdist >= -4f)
                        {
                            c = yellow;
                        }
                        else
                        {
                            c = new Color(0.12f, 0.12f, 0.14f, 1f);
                        }
                    }
                    else if (pdist < 1.0f)
                    {
                        // 牌照外沿 1px 抗锯齿
                        float aa = 1.0f - pdist;
                        c = Color.Lerp(c, yellow, aa);
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 货运主标 "CARGO"（亮黄色 5×7 放大 12 倍，居中）
            Color32 textYellow = (Color32)palette.hazardYellow;
            DrawBannerText(pixels, startX, startY, "CARGO", 256f, 256f, 12.0f, textYellow);
        }

        private static float CalcBannerFrameAlpha(float qx, float qy)
        {
            // 边缘留出 16px 透明衬垫，圆角半径 14px，边缘做 1px 抗锯齿
            float dx = Mathf.Max(0f, Mathf.Abs(qx - 255.5f) - (240f - 14f));
            float dy = Mathf.Max(0f, Mathf.Abs(qy - 255.5f) - (240f - 14f));
            float dist = Mathf.Sqrt(dx * dx + dy * dy) - 14f;
            return Mathf.Clamp01(0.5f - dist);
        }

        private static void DrawBannerChevron(
            Color32[] pixels,
            int startX,
            int startY,
            float cx,
            float cy,
            float width,
            float height,
            float thickness,
            Color32 color)
        {
            float halfW = width * 0.5f;
            float slope = height / halfW;
            float norm = Mathf.Sqrt(1f + slope * slope);

            int minQx = Mathf.Clamp(Mathf.FloorToInt(cx - halfW - thickness), 0, 511);
            int maxQx = Mathf.Clamp(Mathf.CeilToInt(cx + halfW + thickness), 0, 511);
            int minQy = Mathf.Clamp(Mathf.FloorToInt(cy - height - thickness), 0, 511);
            int maxQy = Mathf.Clamp(Mathf.CeilToInt(cy + thickness), 0, 511);

            for (int qy = minQy; qy <= maxQy; qy++)
            {
                for (int qx = minQx; qx <= maxQx; qx++)
                {
                    float dx = Mathf.Abs(qx - cx);
                    if (dx > halfW) continue;

                    float armY = cy - dx * slope;
                    float dist = Mathf.Abs(qy - armY) / norm;
                    float inside = thickness * 0.5f - dist;
                    float alpha = Mathf.Clamp01(inside + 0.5f);

                    if (alpha > 0.001f)
                    {
                        int idx = (startY + qy) * BannerResolution + (startX + qx);
                        BlendPixel(pixels, idx, color, alpha);
                    }
                }
            }
        }

        private static void DrawBannerText(
            Color32[] pixels,
            int startX,
            int startY,
            string text,
            float cx,
            float cy,
            float scale,
            Color32 color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int len = text.Length;
            int gridW = len * 6 - 1;
            int gridH = 7;

            float textW = gridW * scale;
            float textH = gridH * scale;

            int minQx = Mathf.Clamp(Mathf.FloorToInt(cx - textW * 0.5f - 1.5f), 0, 511);
            int maxQx = Mathf.Clamp(Mathf.CeilToInt(cx + textW * 0.5f + 1.5f), 0, 511);
            int minQy = Mathf.Clamp(Mathf.FloorToInt(cy - textH * 0.5f - 1.5f), 0, 511);
            int maxQy = Mathf.Clamp(Mathf.CeilToInt(cy + textH * 0.5f + 1.5f), 0, 511);

            for (int qy = minQy; qy <= maxQy; qy++)
            {
                for (int qx = minQx; qx <= maxQx; qx++)
                {
                    float coverage = 0f;
                    // 2×2 子像素采样实现 1px 软边抗锯齿
                    for (float sy = -0.25f; sy <= 0.26f; sy += 0.5f)
                    {
                        float py = qy + sy;
                        float v = (cy + textH * 0.5f - py) / scale;
                        int row = Mathf.FloorToInt(v);
                        if (row < 0 || row > 6) continue;

                        for (float sx = -0.25f; sx <= 0.26f; sx += 0.5f)
                        {
                            float px = qx + sx;
                            float u = (px - (cx - textW * 0.5f)) / scale;
                            if (u < 0f || u >= gridW) continue;

                            int charIdx = Mathf.FloorToInt(u / 6.0f);
                            float charU = u - charIdx * 6.0f;
                            if (charU < 5.0f && charIdx < len)
                            {
                                int col = Mathf.FloorToInt(charU);
                                if (GetFontBit(text[charIdx], col, row))
                                {
                                    coverage += 0.25f;
                                }
                            }
                        }
                    }

                    if (coverage > 0.001f)
                    {
                        int idx = (startY + qy) * BannerResolution + (startX + qx);
                        BlendPixel(pixels, idx, color, coverage);
                    }
                }
            }
        }

        private static void BlendPixel(Color32[] pixels, int idx, Color32 color, float coverage)
        {
            float a = coverage * (color.a / 255f);
            if (a <= 0.0001f) return;

            Color32 cur = pixels[idx];
            float curA = cur.a / 255f;
            float outA = curA + a * (1f - curA);
            if (outA <= 0.0001f) return;

            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt((cur.r * curA * (1f - a) + color.r * a) / outA), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((cur.g * curA * (1f - a) + color.g * a) / outA), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((cur.b * curA * (1f - a) + color.b * a) / outA), 0, 255);
            byte finalA = (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255);

            pixels[idx] = new Color32(r, g, b, finalA);
        }

        #endregion

        #region 工具函数

        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(0.00001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Hash2DToRange(int x, int y, int seed, float min, float max)
        {
            uint h = (uint)seed ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h = (h ^ (h >> 13)) * 0x5bd1e995;
            h = h ^ (h >> 15);
            float normalized = (h & 0xFFFF) / 65535.0f;
            return min + normalized * (max - min);
        }

        #endregion
    }
}
