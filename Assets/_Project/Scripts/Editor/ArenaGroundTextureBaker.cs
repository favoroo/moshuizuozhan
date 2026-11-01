// ==============================================================================================
// ArenaGroundTextureBaker.cs — P30 场地视觉精修轮：程序化地面反照率/法线/RMO + 横幅图集确定性烘焙器
//
// 1. 横幅图集 (ArenaBannerAtlas, 1024×1024 RGBA) 2×2 分区索引对照表（与 ArenaDecorationBuilder 严格一致）：
//    -------------------------------------------------------------------------------------------
//    索引 0 | (u: 0.0~0.5, v: 0.0~0.5) 左下 (Bottom-Left)
//           | 语义：中性货运码头标语 Neutral Turf ("P1 // TURF")
//           | 设计：几何斜切拼色（上半暗板岩深色 + 下半暖奶油白），15° 斜切带 6px hazardYellow 饰条，
//           |       45° 几何高光切带，大写粗体 "P1 // TURF" + "DOCK 01 // CRANE ZONE" + "BERTH 04"。
//    -------------------------------------------------------------------------------------------
//    索引 1 | (u: 0.5~1.0, v: 0.0~0.5) 右下 (Bottom-Right)
//           | 语义：黄黑警示货运图集 Hazard Cargo ("CARGO // 01")
//           | 设计：全幅 45° 黄黑工业斜纹底，中央 45° 切角重型标牌（亮黄双重描边 + 4 拐角钢铆钉），
//           |       超大号亮黄 "CARGO" + "HEAVY LOAD // KEEP CLEAR" + "TERMINAL PORT" 工业字标。
//    -------------------------------------------------------------------------------------------
//    索引 2 | (u: 0.0~0.5, v: 0.5~1.0) 左上 (Top-Left)
//           | 语义：Team A 出生区专属横幅 Team A ("TEAM A // SPLAT")
//           | 设计：Team A 浓郁橙底（微底渐变压暗），45° 白色几何竞速斜条纹，顶部双重白几何尖角箭头，
//           |       超大号白色粗体 "A"（带 45° 几何墨滴缺口切角）+ "TEAM ALPHA" + "P1 // DIVISION 01"。
//    -------------------------------------------------------------------------------------------
//    索引 3 | (u: 0.5~1.0, v: 0.5~1.0) 右上 (Top-Right)
//           | 语义：Team B 出生区专属横幅 Team B ("TEAM B // WAVE")
//           | 设计：Team B 浓郁钴蓝底，45° 白色几何竞速斜条纹，顶部双重白几何尖角箭头，
//           |       超大号白色粗体 "B"（带 45° 几何墨滴缺口切角）+ "TEAM BRAVO" + "P1 // DIVISION 02"。
//    -------------------------------------------------------------------------------------------
//
// 2. 地面全套三张贴图（覆盖 40×56m，世界范围 X∈[-20,20], Z∈[-28,28]，分辨率 2048×2048）：
//    - ArenaGround_Albedo.png: 浅灰两级分缝混凝土大板（4m 主缝 + 2m 次缝 + 吊装孔）、板面明度微差（±4%）、
//      黄色标线网（虚线车道、坡道口黄黑斜纹带、斑马线过渡区、集装箱角标框）、低饱和区域涂装、做旧点缀（≤15% 面积）。
//    - ArenaGround_N.png: 对应混凝土板缝凹槽与微粒法线（标线不进法线，分缝进法线）。
//    - ArenaGround_RMO.png: R=Metallic(0), G=AO(板缝自遮蔽+污渍吸收+环境投影), B=Detail(0.5), A=Smoothness。
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
    /// P30 竞技场地表全套贴图与横幅贴图确定性烘焙器：
    /// 1. 2048×2048 地面反照率、法线与 RMO 贴图（对齐 INKWAVE 街头潮流与码头工业美学）。
    /// 2. 1024×1024 横幅图集（2×2 512×512 分区，几何大胆风重构）。
    /// 3. 固定随机种子 20261029，全过程确定性数学计算，单次烘焙预算 ≤ 8 秒。
    /// </summary>
    public static class ArenaGroundTextureBaker
    {
        public const string GroundTexturePath = "Assets/_Project/Art/Textures/Arena/ArenaGround_Albedo.png";
        public const string BannerAtlasPath = "Assets/_Project/Art/Textures/Arena/ArenaBannerAtlas.png";
        public const string GroundNormalPath = "Assets/_Project/Art/Textures/Arena/ArenaGround_N.png";
        public const string GroundRmoPath = "Assets/_Project/Art/Textures/Arena/ArenaGround_RMO.png";

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

            // 标点与符号
            Set(' ', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            Set('/', 0x01, 0x02, 0x04, 0x04, 0x08, 0x10, 0x10);
            Set('-', 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00);
            Set('.', 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04);
            Set(':', 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00);
            Set('>', 0x10, 0x08, 0x04, 0x02, 0x04, 0x08, 0x10);
            Set('<', 0x01, 0x02, 0x04, 0x08, 0x04, 0x02, 0x01);
            Set('#', 0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A);
            Set('+', 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00);

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
                      $" - 地面法线贴图: {GroundNormalPath} ({GroundResolution}x{GroundResolution} RGB)\n" +
                      $" - 地面 RMO 贴图: {GroundRmoPath} ({GroundResolution}x{GroundResolution} RGBA)\n" +
                      $" - 横幅图集: {BannerAtlasPath} ({BannerResolution}x{BannerResolution} RGBA)");
        }

        #endregion

        #region 地面贴图烘焙 (2048×2048, Albedo / Normal / RMO)

        public static int BakeGroundTexture(ArenaStyleSO style)
        {
            return BakeGroundTexture(new Palette(style));
        }

        public static int BakeGroundTexture(Palette palette)
        {
            var sw = Stopwatch.StartNew();

            // 1. 预分配像素颜色缓存与辅助特征场
            var ground = new Color[GroundResolution * GroundResolution];
            var seamMask = new float[GroundResolution * GroundResolution];
            var normalSlopeX = new float[GroundResolution * GroundResolution];
            var normalSlopeZ = new float[GroundResolution * GroundResolution];
            var markingMask = new float[GroundResolution * GroundResolution];
            var stainMask = new float[GroundResolution * GroundResolution];

            // 2. 预生成 0.5m 尺度低频微斑驳网格（82×114 个点覆盖 40×56m，双线性插值）
            const int mottleGridW = 82;
            const int mottleGridH = 114;
            var mottleGrid = new float[mottleGridW, mottleGridH];
            var mottleRng = new System.Random(FixedSeed ^ 0x55AA);
            for (int gz = 0; gz < mottleGridH; gz++)
            {
                for (int gx = 0; gx < mottleGridW; gx++)
                {
                    // ±2.5% 斑驳扰动
                    mottleGrid[gx, gz] = ((float)mottleRng.NextDouble() * 2f - 1f) * 0.025f;
                }
            }

            // 3. 第一阶段：混凝土两级大板（4m 主缝 + 2m 次缝 + 板面 ±4% 明度微差 + 板角吊装孔 + 0.5m 斑驳）
            float stepX = WorldSizeX / GroundResolution; // ~0.01953m
            float stepZ = WorldSizeZ / GroundResolution; // ~0.02734m

            // 缝隙几何参数（4m 主缝略宽且深，2m 次缝略细）
            const float seamMainHalfWidth = 0.018f; // 3.6cm 宽
            const float seamMainSoftness = 0.015f;
            const float seamSubHalfWidth = 0.010f;  // 2.0cm 宽
            const float seamSubSoftness = 0.012f;

            Color primarySeamColor = Color.Lerp(palette.groundSeam, palette.concreteDark, 0.45f);
            Color secondarySeamColor = palette.groundSeam;

            for (int y = 0; y < GroundResolution; y++)
            {
                float worldZ = WorldMinZ + (y + 0.5f) * stepZ;
                int slab4Z = Mathf.FloorToInt((worldZ - WorldMinZ) * 0.25f);
                int slab2Z = Mathf.FloorToInt((worldZ - WorldMinZ) * 0.5f);

                float nearest4Z = Mathf.Round(worldZ * 0.25f) * 4f;
                float distSeam4Z = Mathf.Abs(worldZ - nearest4Z);
                float signZ4 = (worldZ >= nearest4Z) ? 1.0f : -1.0f;

                float nearest2Z = Mathf.Round(worldZ * 0.5f) * 2f;
                float distSeam2Z = Mathf.Abs(worldZ - nearest2Z);
                float signZ2 = (worldZ >= nearest2Z) ? 1.0f : -1.0f;

                // 斑驳 Z 轴采样
                float cellZ = (worldZ - WorldMinZ) * 2f;
                int gz0 = Mathf.Clamp(Mathf.FloorToInt(cellZ), 0, mottleGridH - 2);
                float tz = cellZ - gz0;
                float sz = tz * tz * (3f - 2f * tz);

                int rowOffset = y * GroundResolution;

                for (int x = 0; x < GroundResolution; x++)
                {
                    float worldX = WorldMinX + (x + 0.5f) * stepX;
                    int slab4X = Mathf.FloorToInt((worldX - WorldMinX) * 0.25f);
                    int slab2X = Mathf.FloorToInt((worldX - WorldMinX) * 0.5f);

                    float nearest4X = Mathf.Round(worldX * 0.25f) * 4f;
                    float distSeam4X = Mathf.Abs(worldX - nearest4X);
                    float signX4 = (worldX >= nearest4X) ? 1.0f : -1.0f;

                    float nearest2X = Mathf.Round(worldX * 0.5f) * 2f;
                    float distSeam2X = Mathf.Abs(worldX - nearest2X);
                    float signX2 = (worldX >= nearest2X) ? 1.0f : -1.0f;

                    // 板面双层确定性哈希明度微差 (±4%)
                    float slab4BrightnessDelta = Hash2DToRange(slab4X, slab4Z, FixedSeed, -0.024f, 0.024f);
                    float slab2BrightnessDelta = Hash2DToRange(slab2X, slab2Z, FixedSeed ^ 0x1234, -0.016f, 0.016f);
                    float totalSlabDelta = slab4BrightnessDelta + slab2BrightnessDelta;
                    Color pixelColor = palette.groundBase * (1.0f + totalSlabDelta);

                    // 主缝 (4m 模数)
                    float distSeam4 = Mathf.Min(distSeam4X, distSeam4Z);
                    float seamAlpha4 = 1.0f - SmoothStep01(seamMainHalfWidth - seamMainSoftness * 0.5f,
                                                          seamMainHalfWidth + seamMainSoftness * 0.5f,
                                                          distSeam4);

                    // 次缝 (2m 模数)
                    float distSeam2 = Mathf.Min(distSeam2X, distSeam2Z);
                    float seamAlpha2 = 1.0f - SmoothStep01(seamSubHalfWidth - seamSubSoftness * 0.5f,
                                                          seamSubHalfWidth + seamSubSoftness * 0.5f,
                                                          distSeam2);

                    // 分缝合成（主缝优先）
                    float totalSeamAlpha = Mathf.Max(seamAlpha4, seamAlpha2 * 0.70f);
                    if (totalSeamAlpha > 0.001f)
                    {
                        Color sc = (seamAlpha4 >= seamAlpha2) ? primarySeamColor : secondarySeamColor;
                        pixelColor = Color.Lerp(pixelColor, sc, totalSeamAlpha);
                    }

                    // 记录分缝遮罩与法线坡度（用于法线与 RMO 贴图，标线不扰动法线）
                    int pIndex = rowOffset + x;
                    seamMask[pIndex] = totalSeamAlpha;

                    float slopeX = 0f;
                    float slopeZ = 0f;
                    if (distSeam4 < seamMainHalfWidth + seamMainSoftness)
                    {
                        float t4 = Mathf.Clamp01(distSeam4 / (seamMainHalfWidth + seamMainSoftness));
                        float dGroove = 6.0f * t4 * (1.0f - t4) * 0.45f;
                        if (distSeam4X <= distSeam4Z) slopeX = signX4 * dGroove;
                        else slopeZ = signZ4 * dGroove;
                    }
                    else if (distSeam2 < seamSubHalfWidth + seamSubSoftness)
                    {
                        float t2 = Mathf.Clamp01(distSeam2 / (seamSubHalfWidth + seamSubSoftness));
                        float dGroove = 6.0f * t2 * (1.0f - t2) * 0.28f;
                        if (distSeam2X <= distSeam2Z) slopeX = signX2 * dGroove;
                        else slopeZ = signZ2 * dGroove;
                    }
                    normalSlopeX[pIndex] = slopeX;
                    normalSlopeZ[pIndex] = slopeZ;

                    // 板块角隅吊装孔 (Anchor Recesses)
                    float cornerDistX = Mathf.Abs(distSeam2X - 0.16f);
                    float cornerDistZ = Mathf.Abs(distSeam2Z - 0.16f);
                    float anchorDist = Mathf.Sqrt(cornerDistX * cornerDistX + cornerDistZ * cornerDistZ);
                    if (anchorDist < 0.035f)
                    {
                        float anchorHoleAlpha = 1.0f - SmoothStep01(0.015f, 0.035f, anchorDist);
                        pixelColor = Color.Lerp(pixelColor, palette.concreteDark * 0.6f, anchorHoleAlpha * 0.35f);
                    }

                    // 0.5m 尺度双线性微斑驳
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

                    // 像素级高频细微沙粒噪点 (±1.5%)
                    float grain = Hash2DToRange(x, y, FixedSeed ^ 0x9876, -0.015f, 0.015f);
                    pixelColor *= (1.0f + grain);

                    ground[pIndex] = pixelColor;
                }
            }

            // 4. 第二阶段：做旧点缀（集装箱脚印污渍、轮胎转弯擦痕弧线、油渍流痕、修补补丁，≤15% 面积）
            ApplyWeatheringAndStains(ground, stainMask, palette);

            // 5. 第三阶段：黄色标线网与区域涂装（ArenaLayoutV2 标线 + 坡道黄黑斜纹 + 斑马线 + 集装箱角标 + 投影 AO 框）
            ApplyIndustrialMarkings(ground, markingMask, palette);

            // 6. 第四阶段：场景环境 AO（基于 Arena_40x56 / Arena_40x40 下所有 Collider 投影软阴影，避开高台顶面）
            var aoMap = new float[GroundResolution * GroundResolution];
            for (int i = 0; i < aoMap.Length; i++) aoMap[i] = 1.0f;
            int aoColliderCount = ApplyEnvironmentAO(ground, aoMap);

            // 7. 生成并落盘全套贴图：
            // 7.1 ArenaGround_Albedo.png
            SaveTextureRGB(GroundTexturePath, ground, GroundResolution);

            // 7.2 ArenaGround_N.png (OpenGL Y+，板缝坡度进入法线，标线不进法线)
            SaveNormalMap(GroundNormalPath, normalSlopeX, normalSlopeZ, GroundResolution);

            // 7.3 ArenaGround_RMO.png (R=Metallic, G=AO, B=Detail, A=Smoothness)
            SaveRmoMap(GroundRmoPath, seamMask, stainMask, markingMask, aoMap, GroundResolution);

            sw.Stop();
            Debug.Log($"[ArenaGroundTextureBaker] 地面贴图全套生成完成: {GroundTexturePath}，耗时: {sw.ElapsedMilliseconds} ms，AO 碰撞体: {aoColliderCount}");
            return aoColliderCount;
        }

        private static void SaveTextureRGB(string path, Color[] colors, int res)
        {
            var outputColors = new Color32[res * res];
            for (int i = 0; i < outputColors.Length; i++)
            {
                Color c = colors[i];
                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                outputColors[i] = new Color32(r, g, b, 255);
            }

            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            tex.SetPixels32(outputColors);
            tex.Apply(false);

            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            WriteFileAndImport(path, pngBytes);
        }

        private static void SaveNormalMap(string path, float[] slopeX, float[] slopeZ, int res)
        {
            var outputColors = new Color32[res * res];
            for (int i = 0; i < outputColors.Length; i++)
            {
                // OpenGL Y+ 格式切线空间法线: R = X+, G = Y+(Up/Z), B = Z+(Out/Y)
                float sx = -slopeX[i];
                float sz = -slopeZ[i];
                float lenSq = sx * sx + sz * sz;
                float sy = (lenSq < 1.0f) ? Mathf.Sqrt(1.0f - lenSq) : 0.0f;

                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt((sx * 0.5f + 0.5f) * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((sz * 0.5f + 0.5f) * 255f), 0, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((sy * 0.5f + 0.5f) * 255f), 0, 255);
                outputColors[i] = new Color32(r, g, b, 255);
            }

            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            tex.SetPixels32(outputColors);
            tex.Apply(false);

            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            WriteFileAndImport(path, pngBytes);
        }

        private static void SaveRmoMap(string path, float[] seams, float[] stains, float[] markings, float[] ao, int res)
        {
            var outputColors = new Color32[res * res];
            for (int i = 0; i < outputColors.Length; i++)
            {
                // R = Metallic (混凝土基底为 0)
                byte r = 0;

                // G = AO (板缝自遮罩 0.45 + 污渍吸收 + 场景软阴影)
                float seamAo = 1.0f - seams[i] * 0.50f;
                float stainAo = 1.0f - stains[i] * 0.15f;
                float finalAo = Mathf.Clamp01(seamAo * stainAo * ao[i]);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(finalAo * 255f), 0, 255);

                // B = Detail (基线 0.5)
                byte b = 128;

                // A = Smoothness (混凝土基底 0.50，板缝粗糙 0.35，污渍较粗糙 0.38，标线涂料略平滑 0.65)
                float smooth = 0.50f - seams[i] * 0.15f - stains[i] * 0.12f + markings[i] * 0.15f;
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(smooth, 0.30f, 0.75f) * 255f), 0, 255);

                outputColors[i] = new Color32(r, g, b, a);
            }

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.SetPixels32(outputColors);
            tex.Apply(false);

            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            WriteFileAndImport(path, pngBytes);
        }

        private static void WriteFileAndImport(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        #endregion

        #region 做旧与污渍处理 (≤15% 面积，低对比)

        private static void ApplyWeatheringAndStains(Color[] ground, float[] stainMask, Palette palette)
        {
            var rng = new System.Random(FixedSeed);
            Color stainBase = palette.concreteDark * 0.65f;
            Color oilBase = palette.hazardDark * 0.85f;

            // 1. 集装箱 4 拐角定位地脚印污渍（深灰矩形晕，alpha 0.08~0.12）
            ApplyContainerFootprintStains(ground, stainMask, stainBase);

            // 2. 叉车/跨运车转弯轮胎擦痕弧线（双轨弧线，暗炭微弱印痕，alpha 0.04~0.06）
            ApplyTireTrackArcs(ground, stainMask, palette.hazardDark);

            // 3. 局域油渍与锈水流痕（8-10 个精细小范围污斑，alpha 0.05~0.09）
            ApplyOilAndRustSpots(ground, stainMask, rng, stainBase, oilBase);

            // 4. 混凝土修补补丁（3 块矩形浅色/深色预制补丁 + 沥青黑缝）
            ApplyConcretePatches(ground, stainMask, palette);
        }

        private static void ApplyContainerFootprintStains(Color[] ground, float[] stainMask, Color stainColor)
        {
            // 场地既有集装箱位置（来自 ArenaLayoutV2 与 ArenaGeometryBuilder 规范）：
            // 中场集装箱: (|x|=8.1, |z|=5.5), 尺寸 2.2×4.2
            // 侧翼集装箱: (|x|=16.75, z=-3.2 / 4.5), 尺寸 2.2×4.6 / 3.8
            // 延长区箱群: (|x|=16.75, |z|=16.5 / 21.2), 尺寸 2.4×4.6
            var containers = new (Vector2 center, Vector2 size)[]
            {
                (new Vector2(-8.1f, -5.5f), new Vector2(2.2f, 4.2f)),
                (new Vector2(8.1f, 5.5f), new Vector2(2.2f, 4.2f)),
                (new Vector2(8.1f, -5.5f), new Vector2(2.2f, 4.2f)),
                (new Vector2(-8.1f, 5.5f), new Vector2(2.2f, 4.2f)),
                (new Vector2(-16.75f, -3.2f), new Vector2(2.2f, 4.6f)),
                (new Vector2(-16.75f, 4.5f), new Vector2(2.2f, 3.8f)),
                (new Vector2(16.75f, 3.2f), new Vector2(2.2f, 4.6f)),
                (new Vector2(16.75f, -4.5f), new Vector2(2.2f, 3.8f)),
                (new Vector2(-16.75f, -16.5f), new Vector2(2.4f, 4.6f)),
                (new Vector2(16.75f, 16.5f), new Vector2(2.4f, 4.6f)),
                (new Vector2(-16.75f, -21.2f), new Vector2(2.4f, 4.6f)),
                (new Vector2(16.75f, 21.2f), new Vector2(2.4f, 4.6f)),
            };

            foreach (var c in containers)
            {
                float hx = c.size.x * 0.5f;
                float hz = c.size.y * 0.5f;
                Vector2[] corners =
                {
                    new Vector2(c.center.x - hx, c.center.y - hz),
                    new Vector2(c.center.x + hx, c.center.y - hz),
                    new Vector2(c.center.x - hx, c.center.y + hz),
                    new Vector2(c.center.x + hx, c.center.y + hz)
                };

                foreach (var corner in corners)
                {
                    DrawSoftSpot(ground, stainMask, corner.x, corner.y, 0.45f, 0.45f, stainColor, 0.09f);
                }
            }
        }

        private static void ApplyTireTrackArcs(Color[] ground, float[] stainMask, Color darkColor)
        {
            // 在主车道 (x=±6.2) 与侧翼走廊 (x=±10.4, ±16.75) 之间的转弯过渡区绘制 4 道双轨轮胎转弯擦痕弧线
            var arcs = new (Vector2 center, float radius, float angleStart, float angleEnd)[]
            {
                (new Vector2(3.2f, -11.0f), 5.5f, 10f, 80f),
                (new Vector2(-3.2f, -11.0f), 5.5f, 100f, 170f),
                (new Vector2(3.2f, 11.0f), 5.5f, 190f, 260f),
                (new Vector2(-3.2f, 11.0f), 5.5f, 280f, 350f),
                (new Vector2(13.5f, 0f), 4.2f, 75f, 145f),
                (new Vector2(-13.5f, 0f), 4.2f, 255f, 325f)
            };

            foreach (var arc in arcs)
            {
                DrawDualTireArc(ground, stainMask, arc.center, arc.radius, arc.angleStart, arc.angleEnd, darkColor, 0.055f);
            }
        }

        private static void DrawDualTireArc(
            Color[] ground,
            float[] stainMask,
            Vector2 center,
            float radius,
            float a0Deg,
            float a1Deg,
            Color color,
            float alpha)
        {
            float rInner = radius - 0.22f;
            float rOuter = radius + 0.22f;
            float margin = 0.5f;

            float minX = center.x - (radius + margin);
            float maxX = center.x + (radius + margin);
            float minZ = center.y - (radius + margin);
            float maxZ = center.y + (radius + margin);

            int minPx = Mathf.Clamp(Mathf.FloorToInt((minX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((maxX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((minZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((maxZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            float rad0 = a0Deg * Mathf.Deg2Rad;
            float rad1 = a1Deg * Mathf.Deg2Rad;
            if (rad1 < rad0) { float t = rad0; rad0 = rad1; rad1 = t; }

            const float trackWidth = 0.12f;

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - center.x;
                    float dz = wz - center.y;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);

                    float ang = Mathf.Atan2(dz, dx);
                    if (ang < 0f) ang += Mathf.PI * 2f;
                    if (ang < rad0 || ang > rad1) continue;

                    // 双轨：内胎与外胎
                    float dInner = Mathf.Abs(dist - rInner);
                    float dOuter = Mathf.Abs(dist - rOuter);
                    float dMin = Mathf.Min(dInner, dOuter);

                    if (dMin < trackWidth * 0.5f)
                    {
                        float t = 1.0f - (dMin / (trackWidth * 0.5f));
                        float a = t * t * alpha;
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], color, a);
                        stainMask[idx] = Mathf.Max(stainMask[idx], a * 1.5f);
                    }
                }
            }
        }

        private static void ApplyOilAndRustSpots(Color[] ground, float[] stainMask, System.Random rng, Color stainColor, Color oilColor)
        {
            // 8 个小范围流痕与油迹
            for (int i = 0; i < 8; i++)
            {
                float cx = (float)(rng.NextDouble() * 28.0 - 14.0);
                float cz = (float)(rng.NextDouble() * 38.0 - 19.0);
                float rx = (float)(0.6 + rng.NextDouble() * 1.2);
                float rz = (float)(0.6 + rng.NextDouble() * 1.2);
                float a = (float)(0.04 + rng.NextDouble() * 0.035);
                Color c = (i % 2 == 0) ? oilColor : stainColor;
                DrawSoftSpot(ground, stainMask, cx, cz, rx, rz, c, a);
            }
        }

        private static void ApplyConcretePatches(Color[] ground, float[] stainMask, Palette palette)
        {
            // 3 处地表沥青与浅色修补补丁（体现码头维护感）
            var patches = new (Vector2 pos, Vector2 size, float delta)[]
            {
                (new Vector2(3.5f, -12.5f), new Vector2(1.6f, 2.2f), -0.05f),
                (new Vector2(-4.2f, 13.0f), new Vector2(2.0f, 1.4f), 0.04f),
                (new Vector2(14.0f, -1.2f), new Vector2(1.5f, 2.5f), -0.04f)
            };

            foreach (var patch in patches)
            {
                float hx = patch.size.x * 0.5f;
                float hz = patch.size.y * 0.5f;
                int minPx = Mathf.Clamp(Mathf.FloorToInt((patch.pos.x - hx - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int maxPx = Mathf.Clamp(Mathf.CeilToInt((patch.pos.x + hx - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int minPy = Mathf.Clamp(Mathf.FloorToInt((patch.pos.y - hz - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
                int maxPy = Mathf.Clamp(Mathf.CeilToInt((patch.pos.y + hz - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

                for (int py = minPy; py <= maxPy; py++)
                {
                    float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                    int row = py * GroundResolution;
                    for (int px = minPx; px <= maxPx; px++)
                    {
                        float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                        float dx = Mathf.Abs(wx - patch.pos.x);
                        float dz = Mathf.Abs(wz - patch.pos.y);

                        if (dx <= hx && dz <= hz)
                        {
                            int idx = row + px;
                            // 边缘 3cm 黑色沥青灌缝
                            float edgeDist = Mathf.Min(hx - dx, hz - dz);
                            if (edgeDist < 0.035f)
                            {
                                ground[idx] = Color.Lerp(ground[idx], palette.hazardDark, 0.45f);
                            }
                            else
                            {
                                ground[idx] *= (1.0f + patch.delta);
                            }
                            stainMask[idx] = Mathf.Max(stainMask[idx], 0.08f);
                        }
                    }
                }
            }
        }

        private static void DrawSoftSpot(
            Color[] ground,
            float[] stainMask,
            float cx,
            float cz,
            float rx,
            float rz,
            Color color,
            float alpha)
        {
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
                    float normDist = (dx * dx) / (rx * rx) + (dz * dz) / (rz * rz);
                    if (normDist < 1.0f)
                    {
                        float t = 1.0f - Mathf.Sqrt(normDist);
                        float falloff = t * t * (3f - 2f * t);
                        float blend = falloff * alpha;
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], color, blend);
                        stainMask[idx] = Mathf.Max(stainMask[idx], blend * 2.0f);
                    }
                }
            }
        }

        #endregion

        #region 黄色标线网与工业地表绘制

        private static void ApplyIndustrialMarkings(Color[] ground, float[] markingMask, Palette palette)
        {
            float pxWorld = (WorldSizeX / GroundResolution + WorldSizeZ / GroundResolution) * 0.5f;
            float edgeSoftness = 1.5f * pxWorld;

            // 1. 绘制 ArenaLayoutV2 定义的标准标线 (LaneDash / Chevron / HazardBand / Ring / StencilText / SafetyLine)
            var markings = ArenaLayoutV2.GroundMarkings;
            if (markings != null)
            {
                foreach (var marking in markings)
                {
                    switch (marking.Kind)
                    {
                        case ArenaLayoutV2.GroundMarkingKind.LaneDash:
                            DrawLaneDash(ground, markingMask, marking, palette, edgeSoftness);
                            break;
                        case ArenaLayoutV2.GroundMarkingKind.Chevron:
                            DrawChevron(ground, markingMask, marking, palette, edgeSoftness);
                            break;
                        case ArenaLayoutV2.GroundMarkingKind.HazardBand:
                            DrawHazardBand(ground, markingMask, marking, palette, edgeSoftness);
                            break;
                        case ArenaLayoutV2.GroundMarkingKind.Ring:
                            DrawRing(ground, markingMask, marking, palette, edgeSoftness);
                            break;
                        case ArenaLayoutV2.GroundMarkingKind.StencilText:
                            DrawStencilText(ground, markingMask, marking, palette, edgeSoftness, pxWorld);
                            break;
                        case ArenaLayoutV2.GroundMarkingKind.SafetyLine:
                            DrawSafetyLine(ground, markingMask, marking, palette, edgeSoftness);
                            break;
                    }
                }
            }

            // 2. 坡道口与门框周边黄黑警示带强化 (中央四向 + 侧翼四向坡底口)
            ApplyRampEntranceHazardBands(ground, markingMask, palette, edgeSoftness);

            // 3. 出生过渡区斑马线 (Spawn Gate 过渡 + 侧梯下落步道)
            ApplyZebraCrossings(ground, markingMask, palette, edgeSoftness);

            // 4. 集装箱位浅色 L 形角标框与库位文字 (Bay Numbers)
            ApplyContainerCornerBrackets(ground, markingMask, palette, edgeSoftness, pxWorld);

            // 5. 中央高台投影区工业底座基座边框线
            ApplyCentralPlatformBaseBorder(ground, markingMask, palette, edgeSoftness);
        }

        private static void ApplyRampEntranceHazardBands(Color[] ground, float[] markingMask, Palette palette, float edge)
        {
            // 在主要坡道底部平地交界处增加明显的黄黑斜纹警示带
            var bands = new (Vector2 center, Vector2 size, float yaw)[]
            {
                (new Vector2(0f, -9.9f), new Vector2(4.4f, 0.6f), 0f),   // 中央南坡底
                (new Vector2(0f, 9.9f), new Vector2(4.4f, 0.6f), 0f),    // 中央北坡底
                (new Vector2(-9.9f, 0f), new Vector2(0.6f, 4.4f), 0f),   // 中央西坡底
                (new Vector2(9.9f, 0f), new Vector2(0.6f, 4.4f), 0f),    // 中央东坡底
                (new Vector2(-13.45f, -11.25f), new Vector2(3.6f, 0.55f), 0f), // 侧翼西南坡底
                (new Vector2(-13.45f, 11.25f), new Vector2(3.6f, 0.55f), 0f),  // 侧翼西北坡底
                (new Vector2(13.45f, -11.25f), new Vector2(3.6f, 0.55f), 0f),  // 侧翼东南坡底
                (new Vector2(13.45f, 11.25f), new Vector2(3.6f, 0.55f), 0f),   // 侧翼东北坡底
            };

            foreach (var b in bands)
            {
                var m = new ArenaLayoutV2.GroundMarking(ArenaLayoutV2.GroundMarkingKind.HazardBand, b.center, b.size, b.yaw, Team.Neutral);
                DrawHazardBand(ground, markingMask, m, palette, edge);
            }
        }

        private static void ApplyZebraCrossings(Color[] ground, float[] markingMask, Palette palette, float edge)
        {
            // 契约与参考图：出生台阶过渡区与 SpawnGate 出口斑马线
            // 采用略微降饱和的奶油白 (Color.Lerp(white, groundBase, 0.22f))，保证不抢墨色
            Color zebraColor = Color.Lerp(Color.white, palette.groundBase, 0.22f);

            // 1. 出生大门前方横向斑马线 (z = ±20.8f, 宽 7.2m, 高 1.2m)
            DrawZebraCrossingBand(ground, markingMask, new Vector2(0f, -20.8f), new Vector2(7.2f, 1.2f), 0.55f, 0.35f, true, zebraColor, edge);
            DrawZebraCrossingBand(ground, markingMask, new Vector2(0f, 20.8f), new Vector2(7.2f, 1.2f), 0.55f, 0.35f, true, zebraColor, edge);

            // 2. 出生侧梯着陆口过渡斑马线 (|x| ≈ 12.0m, |z| ≈ 25.2m)
            DrawZebraCrossingBand(ground, markingMask, new Vector2(-12.4f, -25.2f), new Vector2(2.4f, 3.2f), 0.50f, 0.30f, false, zebraColor, edge);
            DrawZebraCrossingBand(ground, markingMask, new Vector2(12.4f, -25.2f), new Vector2(2.4f, 3.2f), 0.50f, 0.30f, false, zebraColor, edge);
            DrawZebraCrossingBand(ground, markingMask, new Vector2(-12.4f, 25.2f), new Vector2(2.4f, 3.2f), 0.50f, 0.30f, false, zebraColor, edge);
            DrawZebraCrossingBand(ground, markingMask, new Vector2(12.4f, 25.2f), new Vector2(2.4f, 3.2f), 0.50f, 0.30f, false, zebraColor, edge);
        }

        private static void DrawZebraCrossingBand(
            Color[] ground,
            float[] markingMask,
            Vector2 center,
            Vector2 size,
            float barWidth,
            float barGap,
            bool isHorizontal,
            Color color,
            float edge)
        {
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;
            float bound = Mathf.Max(halfX, halfZ) + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((center.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((center.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((center.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((center.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            float period = barWidth + barGap;

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = wx - center.x;
                    float dz = wz - center.y;

                    if (Mathf.Abs(dx) > halfX || Mathf.Abs(dz) > halfZ) continue;

                    float distBorder = Mathf.Min(halfX - Mathf.Abs(dx), halfZ - Mathf.Abs(dz));
                    float tBorder = Mathf.Clamp01((distBorder + edge * 0.5f) / edge);

                    float coord = isHorizontal ? (wx + halfX) : (wz + halfZ);
                    float phase = Mathf.Repeat(coord, period);
                    float distBar = (barWidth * 0.5f) - Mathf.Abs(phase - barWidth * 0.5f);
                    float tBar = Mathf.Clamp01((distBar + edge * 0.5f) / edge);

                    float alpha = tBorder * tBar * 0.85f;
                    if (alpha > 0.001f)
                    {
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], color, alpha);
                        markingMask[idx] = Mathf.Max(markingMask[idx], alpha);
                    }
                }
            }
        }

        private static void ApplyContainerCornerBrackets(
            Color[] ground,
            float[] markingMask,
            Palette palette,
            float edge,
            float pxWorld)
        {
            // 码头集装箱车位 L 形角标 (L-Brackets)
            // 采用淡黄色 (hazardYellow 浅化 35%)，臂长 0.55m，线宽 0.08m
            Color bracketColor = Color.Lerp(palette.hazardYellow, palette.concreteLight, 0.35f);

            var bays = new (Vector2 center, Vector2 size, string name)[]
            {
                (new Vector2(-8.1f, -5.5f), new Vector2(2.2f, 4.2f), "BAY 01"),
                (new Vector2(8.1f, 5.5f), new Vector2(2.2f, 4.2f), "BAY 02"),
                (new Vector2(8.1f, -5.5f), new Vector2(2.2f, 4.2f), "BAY 03"),
                (new Vector2(-8.1f, 5.5f), new Vector2(2.2f, 4.2f), "BAY 04"),
                (new Vector2(-16.75f, -3.2f), new Vector2(2.2f, 4.6f), "BAY 05"),
                (new Vector2(-16.75f, 4.5f), new Vector2(2.2f, 3.8f), "BAY 06"),
                (new Vector2(16.75f, 3.2f), new Vector2(2.2f, 4.6f), "BAY 07"),
                (new Vector2(16.75f, -4.5f), new Vector2(2.2f, 3.8f), "BAY 08"),
            };

            foreach (var bay in bays)
            {
                DrawSingleContainerBrackets(ground, markingMask, bay.center, bay.size, bracketColor, edge);

                // 在箱位旁地面印淡字模编号
                var textM = new ArenaLayoutV2.GroundMarking(
                    ArenaLayoutV2.GroundMarkingKind.StencilText,
                    bay.center + new Vector2(0f, bay.size.y * 0.5f + 0.45f),
                    new Vector2(1.8f, 0.45f),
                    0f,
                    Team.Neutral,
                    bay.name);
                DrawStencilText(ground, markingMask, textM, palette, edge, pxWorld);
            }
        }

        private static void DrawSingleContainerBrackets(
            Color[] ground,
            float[] markingMask,
            Vector2 center,
            Vector2 size,
            Color color,
            float edge)
        {
            float hx = size.x * 0.5f + 0.12f;
            float hz = size.y * 0.5f + 0.12f;
            const float armLen = 0.55f;
            const float stroke = 0.08f;

            Vector2[] corners =
            {
                new Vector2(center.x - hx, center.y - hz),
                new Vector2(center.x + hx, center.y - hz),
                new Vector2(center.x - hx, center.y + hz),
                new Vector2(center.x + hx, center.y + hz)
            };

            float[] signX = { 1f, -1f, 1f, -1f };
            float[] signZ = { 1f, 1f, -1f, -1f };

            for (int i = 0; i < 4; i++)
            {
                Vector2 c = corners[i];
                float sx = signX[i];
                float sz = signZ[i];

                float minX = Mathf.Min(c.x, c.x + sx * armLen) - stroke;
                float maxX = Mathf.Max(c.x, c.x + sx * armLen) + stroke;
                float minZ = Mathf.Min(c.y, c.y + sz * armLen) - stroke;
                float maxZ = Mathf.Max(c.y, c.y + sz * armLen) + stroke;

                int minPx = Mathf.Clamp(Mathf.FloorToInt((minX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int maxPx = Mathf.Clamp(Mathf.CeilToInt((maxX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
                int minPy = Mathf.Clamp(Mathf.FloorToInt((minZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
                int maxPy = Mathf.Clamp(Mathf.CeilToInt((maxZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

                for (int py = minPy; py <= maxPy; py++)
                {
                    float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                    int row = py * GroundResolution;

                    for (int px = minPx; px <= maxPx; px++)
                    {
                        float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                        float lx = (wx - c.x) * sx;
                        float lz = (wz - c.y) * sz;

                        bool onHoriz = (lx >= -0.02f && lx <= armLen && Mathf.Abs(lz) <= stroke * 0.5f);
                        bool onVert = (lz >= -0.02f && lz <= armLen && Mathf.Abs(lx) <= stroke * 0.5f);

                        if (onHoriz || onVert)
                        {
                            int idx = row + px;
                            ground[idx] = Color.Lerp(ground[idx], color, 0.85f);
                            markingMask[idx] = Mathf.Max(markingMask[idx], 0.85f);
                        }
                    }
                }
            }
        }

        private static void ApplyCentralPlatformBaseBorder(
            Color[] ground,
            float[] markingMask,
            Palette palette,
            float edge)
        {
            // 中央争夺高台基础底框线（外围 0.35m 基座边缘轮廓，微深炭色）
            const float halfSize = 5.0f;
            float minX = -halfSize - 0.25f;
            float maxX = halfSize + 0.25f;
            float minZ = -halfSize - 0.25f;
            float maxZ = halfSize + 0.25f;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((minX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((maxX - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((minZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((maxZ - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            Color borderColor = Color.Lerp(palette.concreteDark, palette.hazardDark, 0.40f);

            for (int py = minPy; py <= maxPy; py++)
            {
                float wz = WorldMinZ + (py + 0.5f) / GroundResolution * WorldSizeZ;
                int row = py * GroundResolution;

                for (int px = minPx; px <= maxPx; px++)
                {
                    float wx = WorldMinX + (px + 0.5f) / GroundResolution * WorldSizeX;
                    float dx = Mathf.Abs(wx);
                    float dz = Mathf.Abs(wz);

                    if (dx <= halfSize + 0.25f && dz <= halfSize + 0.25f)
                    {
                        float dist = Mathf.Max(dx - halfSize, dz - halfSize);
                        if (dist >= 0f && dist <= 0.22f)
                        {
                            int idx = row + px;
                            ground[idx] = Color.Lerp(ground[idx], borderColor, 0.30f);
                        }
                    }
                }
            }
        }

        private static void DrawLaneDash(
            Color[] ground,
            float[] markingMask,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge)
        {
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

            Color markingColor = Color.Lerp(palette.groundMarking, palette.hazardYellow, 0.25f);

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
                    float distDash = 1.0f - Mathf.Abs(cycle - 1.0f);

                    float dist = Mathf.Min(distAcross, Mathf.Min(distEnd, distDash));
                    float t = Mathf.Clamp01((dist + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t);

                    if (alpha > 0.001f)
                    {
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], markingColor, alpha);
                        markingMask[idx] = Mathf.Max(markingMask[idx], alpha);
                    }
                }
            }
        }

        private static void DrawChevron(
            Color[] ground,
            float[] markingMask,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge)
        {
            Color chevronColor;
            switch (m.Team)
            {
                case Team.TeamA:
                    // 降饱和混合，确保不抢前置涂地墨水
                    chevronColor = Color.Lerp(palette.teamAPad, palette.groundBase, 0.45f);
                    break;
                case Team.TeamB:
                    chevronColor = Color.Lerp(palette.teamBPad, palette.groundBase, 0.45f);
                    break;
                default:
                    chevronColor = Color.Lerp(palette.groundMarkingAccent, palette.hazardYellow, 0.30f);
                    break;
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
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], chevronColor, alpha);
                        markingMask[idx] = Mathf.Max(markingMask[idx], alpha);
                    }
                }
            }
        }

        private static void DrawHazardBand(
            Color[] ground,
            float[] markingMask,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge)
        {
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
                    int idx = row + px;
                    ground[idx] = Color.Lerp(ground[idx], stripeColor, rectAlpha);
                    markingMask[idx] = Mathf.Max(markingMask[idx], rectAlpha);
                }
            }
        }

        private static void DrawRing(
            Color[] ground,
            float[] markingMask,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge)
        {
            float radius = m.SizeXZ.x;
            float lineWidth = m.SizeXZ.y;
            float bound = radius + lineWidth * 0.5f + edge;

            int minPx = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.x - bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int maxPx = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.x + bound - WorldMinX) / WorldSizeX * GroundResolution), 0, GroundResolution - 1);
            int minPy = Mathf.Clamp(Mathf.FloorToInt((m.CenterXZ.y - bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);
            int maxPy = Mathf.Clamp(Mathf.CeilToInt((m.CenterXZ.y + bound - WorldMinZ) / WorldSizeZ * GroundResolution), 0, GroundResolution - 1);

            Color ringColor = Color.Lerp(palette.groundMarking, palette.hazardYellow, 0.20f);

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

                    // 在 0°, 90°, 180°, 270° 增加 4 个放射状定位刻度标
                    float ang = Mathf.Atan2(dz, dx) * Mathf.Rad2Deg;
                    float angMod = Mathf.Repeat(ang + 45f, 90f) - 45f;
                    if (Mathf.Abs(angMod) < 2.5f && Mathf.Abs(r - radius) < lineWidth * 1.4f)
                    {
                        distRing = Mathf.Max(distRing, 0.05f);
                    }

                    float t = Mathf.Clamp01((distRing + edge * 0.5f) / edge);
                    float alpha = t * t * (3f - 2f * t);

                    if (alpha > 0.001f)
                    {
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], ringColor, alpha);
                        markingMask[idx] = Mathf.Max(markingMask[idx], alpha);
                    }
                }
            }
        }

        private static void DrawSafetyLine(
            Color[] ground,
            float[] markingMask,
            ArenaLayoutV2.GroundMarking m,
            Palette palette,
            float edge)
        {
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
                    float alpha = t * t * (3f - 2f * t) * 0.85f;

                    if (alpha > 0.001f)
                    {
                        int idx = row + px;
                        ground[idx] = Color.Lerp(ground[idx], palette.hazardYellow, alpha);
                        markingMask[idx] = Mathf.Max(markingMask[idx], alpha);
                    }
                }
            }
        }

        private static void DrawStencilText(
            Color[] ground,
            float[] markingMask,
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
                case Team.TeamA: textColor = Color.Lerp(palette.teamAPad, palette.groundBase, 0.40f); break;
                case Team.TeamB: textColor = Color.Lerp(palette.teamBPad, palette.groundBase, 0.40f); break;
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

                    float hits = 0f;
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        float slz = lz + sy * sub;
                        float v = (textH * 0.5f - slz) / scale;
                        int row = Mathf.FloorToInt(v);
                        if (row < 0 || row > 6) continue;

                        for (int sx = -1; sx <= 1; sx += 2)
                        {
                            float slx = lx + sx * sub;
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
                        int idx = rowOffset + px;
                        ground[idx] = Color.Lerp(ground[idx], textColor, hits);
                        markingMask[idx] = Mathf.Max(markingMask[idx], hits);
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

        private static int ApplyEnvironmentAO(Color[] ground, float[] aoMap)
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

            // 2. 收集投射阴影的 Collider
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
                                factor = 0.90f;
                            }
                            else if (dist < shadowRadius)
                            {
                                float t = 1.0f - dist / shadowRadius;
                                float smooth = t * t * (3f - 2f * t);
                                factor = 1.0f - 0.22f * smooth;
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

        #region 横幅图集烘焙 (1024×1024, RGBA, 几何大胆风)

        public static void BakeBannerAtlas(ArenaStyleSO style)
        {
            BakeBannerAtlas(new Palette(style));
        }

        public static void BakeBannerAtlas(Palette palette)
        {
            var sw = Stopwatch.StartNew();

            // 1024×1024，RGBA32，四块 512×512
            var pixels = new Color32[BannerResolution * BannerResolution];

            // 索引 0: (0, 0) 左下 -> 中性货运标语 Neutral Turf ("P1 // TURF")
            BakeQuadrantNeutralTurf(pixels, 0, 0, palette);

            // 索引 1: (0.5, 0) 右下 -> 工业警示货运图集 Hazard Cargo ("CARGO // 01")
            BakeQuadrantHazardCargo(pixels, 512, 0, palette);

            // 索引 2: (0, 0.5) 左上 -> Team A 专属横幅 ("TEAM A // SPLAT")
            BakeQuadrantTeamA(pixels, 0, 512, palette);

            // 索引 3: (0.5, 0.5) 右上 -> Team B 专属横幅 ("TEAM B // WAVE")
            BakeQuadrantTeamB(pixels, 512, 512, palette);

            var bannerTex = new Texture2D(BannerResolution, BannerResolution, TextureFormat.RGBA32, false);
            bannerTex.SetPixels32(pixels);
            bannerTex.Apply(false);

            byte[] pngBytes = bannerTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(bannerTex);

            WriteFileAndImport(BannerAtlasPath, pngBytes);

            sw.Stop();
            Debug.Log($"[ArenaGroundTextureBaker] 横幅图集已生成: {BannerAtlasPath}，耗时: {sw.ElapsedMilliseconds} ms");
        }

        private static void BakeQuadrantNeutralTurf(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 索引 0 (左下): 几何斜切拼色（上半暗板岩 + 下半暖奶油白），15° 斜切带 6px hazardYellow 饰条，
            // 45° 几何高光切带，大写粗体 "P1 // TURF" + "DOCK 01 // CRANE ZONE" + "BERTH 04"。
            Color creamBase = new Color(0.92f, 0.90f, 0.83f, 1f);
            Color darkSlate = Color.Lerp(palette.containerSlate, palette.hazardDark, 0.40f);
            Color yellow = palette.hazardYellow;
            Color textDark = new Color(0.12f, 0.13f, 0.15f, 1f);
            Color white = Color.white;

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerChamferFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    // 15° 斜切拼色分界线：y = 280 + (qx - 256) * tan(15°)
                    float splitY = 280f + (qx - 256f) * 0.26795f;
                    float dSplit = qy - splitY;

                    Color c;
                    if (dSplit > 4f)
                    {
                        c = darkSlate;
                    }
                    else if (dSplit >= -4f)
                    {
                        // 8px 鲜明 hazardYellow 斜切分割饰条
                        c = yellow;
                    }
                    else
                    {
                        c = creamBase;

                        // 下半区工业横条纹装饰 (y=60~130)
                        if (qy >= 60 && qy <= 125)
                        {
                            float wave = Mathf.Sin(qx * (Mathf.PI * 2f / 20f));
                            c = Color.Lerp(creamBase, palette.concreteMid, 0.18f + 0.12f * wave);
                        }
                        // 细横标线
                        else if ((qy >= 160 && qy <= 164) || (qy >= 238 && qy <= 242))
                        {
                            c = Color.Lerp(creamBase, palette.concreteDark, 0.25f);
                        }
                    }

                    // 45° 斜切几何高光切带 (qx + qy ∈ [480, 520])
                    float diagCoord = (qx + qy);
                    if (diagCoord >= 490f && diagCoord <= 530f)
                    {
                        c = Color.Lerp(c, white, 0.14f);
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 顶部暗底小字样
            DrawBannerText(pixels, startX, startY, "DOCK 01 // CRANE ZONE", 256f, 435f, 3.5f, new Color32(245, 245, 245, 255));
            DrawBannerText(pixels, startX, startY, "CONTAINER TERMINAL", 256f, 395f, 3.0f, (Color32)yellow);

            // 主标题深色粗体 "P1 // TURF"
            DrawBannerText(pixels, startX, startY, "P1 // TURF", 256f, 200f, 7.5f, (Color32)textDark);

            // 底部码头编号标语
            DrawBannerText(pixels, startX, startY, ">>> BERTH 04 // 40X56M", 256f, 85f, 3.2f, new Color32(70, 72, 78, 255));
        }

        private static void BakeQuadrantHazardCargo(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 索引 1 (右下): 全幅 45° 黄黑工业斜纹底，中央 45° 切角重型标牌（亮黄双重描边 + 4 拐角钢铆钉），
            // 超大号亮黄 "CARGO" + "HEAVY LOAD // KEEP CLEAR" + "TERMINAL PORT" 工业字标。
            Color yellow = palette.hazardYellow;
            Color dark = palette.hazardDark;
            Color plateBg = new Color(0.10f, 0.10f, 0.12f, 1f);

            for (int qy = 0; qy < 512; qy++)
            {
                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerChamferFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    // 45° 黄黑条纹底纹 (周期 36px)
                    float coord = (qx + qy) * 0.70710678f;
                    float phase = Mathf.Repeat(coord, 36f) / 36f;
                    float stripe = SmoothStep01(0.5f - 0.04f, 0.5f + 0.04f, phase);
                    Color c = Color.Lerp(yellow, dark, stripe);

                    // 中央 45° 切角重型装甲牌照框 (410×220，切角 20px)
                    float distPlate = CalcChamferBoxDist(qx - 256f, qy - 256f, 205f, 110f, 20f);
                    if (distPlate <= 0f)
                    {
                        // 外圈 6px 亮黄边框，内圈暗板岩底
                        if (distPlate >= -6f)
                        {
                            c = yellow;
                        }
                        else if (distPlate >= -8f)
                        {
                            c = dark;
                        }
                        else
                        {
                            c = plateBg;
                        }
                    }
                    else if (distPlate < 1.0f)
                    {
                        c = Color.Lerp(c, yellow, 1.0f - distPlate);
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 4 拐角铆钉 (Plate Rivets)
            Color32 rivetColor = (Color32)palette.hazardYellow;
            DrawBannerCircle(pixels, startX, startY, 256f - 180f, 256f - 85f, 4.5f, rivetColor);
            DrawBannerCircle(pixels, startX, startY, 256f + 180f, 256f - 85f, 4.5f, rivetColor);
            DrawBannerCircle(pixels, startX, startY, 256f - 180f, 256f + 85f, 4.5f, rivetColor);
            DrawBannerCircle(pixels, startX, startY, 256f + 180f, 256f + 85f, 4.5f, rivetColor);

            // 标牌顶部次级字标
            DrawBannerText(pixels, startX, startY, "TERMINAL PORT // 01", 256f, 325f, 3.2f, new Color32(230, 230, 230, 255));

            // 主标 "CARGO"（亮黄色 5×7 放大 13 倍，居中）
            DrawBannerText(pixels, startX, startY, "CARGO", 256f, 260f, 13.0f, (Color32)yellow);

            // 标牌底部重型警示标语
            DrawBannerText(pixels, startX, startY, "HEAVY LOAD // KEEP CLEAR", 256f, 195f, 3.2f, (Color32)yellow);
        }

        private static void BakeQuadrantTeamA(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 索引 2 (左上): Team A 浓郁橙底（微底渐变压暗），45° 白色几何竞速斜条纹，顶部双重白几何尖角箭头，
            // 超大号白色粗体 "A"（带 45° 几何墨滴缺口切角）+ "TEAM ALPHA" + "P1 // DIVISION 01"。
            Color teamAColor = palette.teamAPad;
            Color teamADark = Color.Lerp(teamAColor, palette.hazardDark, 0.28f);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 orangeAccent = (Color32)Color.Lerp(teamAColor, Color.white, 0.35f);

            for (int qy = 0; qy < 512; qy++)
            {
                float vGrad = qy / 512f;
                Color bgGrad = Color.Lerp(teamADark, teamAColor, vGrad * 0.8f + 0.2f);

                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerChamferFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    Color c = bgGrad;

                    // 左侧三道 45° 竞速白条纹 (qx - qy ∈ [ -260, -140 ])
                    float speedCoord = qx - qy;
                    if ((speedCoord >= -250f && speedCoord <= -236f) ||
                        (speedCoord >= -210f && speedCoord <= -185f) ||
                        (speedCoord >= -165f && speedCoord <= -155f))
                    {
                        c = Color.white;
                    }

                    // 上下白色竞速封边条 (y=440~468 与 y=44~72)
                    if ((qy >= 445 && qy <= 468) || (qy >= 44 && qy <= 67))
                    {
                        c = Color.white;
                    }
                    else if ((qy >= 436 && qy <= 441) || (qy >= 71 && qy <= 76))
                    {
                        c = teamADark;
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 双重向上几何白箭头（双重尖角 Chevrons）
            DrawBannerChevron(pixels, startX, startY, 256f, 415f, 220f, 65f, 20f, white);
            DrawBannerChevron(pixels, startX, startY, 256f, 385f, 170f, 50f, 15f, white);

            // 顶部战队字样
            DrawBannerText(pixels, startX, startY, "TEAM ALPHA", 256f, 456f, 3.2f, (Color32)teamADark);

            // 核心粗体字母 "A" (5×7 放大 26 倍)
            DrawBannerText(pixels, startX, startY, "A", 256f, 235f, 26f, white);

            // 底部战区字样
            DrawBannerText(pixels, startX, startY, "P1 // DIVISION 01", 256f, 115f, 3.5f, orangeAccent);
            DrawBannerText(pixels, startX, startY, "READY // INK UP", 256f, 55f, 3.2f, (Color32)teamADark);
        }

        private static void BakeQuadrantTeamB(Color32[] pixels, int startX, int startY, Palette palette)
        {
            // 索引 3 (右上): Team B 浓郁钴蓝底，45° 白色几何竞速斜条纹，顶部双重白几何尖角箭头，
            // 超大号白色粗体 "B"（带 45° 几何墨滴缺口切角）+ "TEAM BRAVO" + "P1 // DIVISION 02"。
            Color teamBColor = palette.teamBPad;
            Color teamBDark = Color.Lerp(teamBColor, palette.hazardDark, 0.32f);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 blueAccent = (Color32)Color.Lerp(teamBColor, Color.white, 0.40f);

            for (int qy = 0; qy < 512; qy++)
            {
                float vGrad = qy / 512f;
                Color bgGrad = Color.Lerp(teamBDark, teamBColor, vGrad * 0.8f + 0.2f);

                for (int qx = 0; qx < 512; qx++)
                {
                    float frameAlpha = CalcBannerChamferFrameAlpha(qx, qy);
                    if (frameAlpha <= 0f) continue;

                    Color c = bgGrad;

                    // 右侧三道 45° 竞速白条纹 (qx + qy ∈ [ 680, 800 ])
                    float speedCoord = qx + qy;
                    if ((speedCoord >= 700f && speedCoord <= 714f) ||
                        (speedCoord >= 735f && speedCoord <= 760f) ||
                        (speedCoord >= 780f && speedCoord <= 790f))
                    {
                        c = Color.white;
                    }

                    // 上下白色竞速封边条 (y=440~468 与 y=44~72)
                    if ((qy >= 445 && qy <= 468) || (qy >= 44 && qy <= 67))
                    {
                        c = Color.white;
                    }
                    else if ((qy >= 436 && qy <= 441) || (qy >= 71 && qy <= 76))
                    {
                        c = teamBDark;
                    }

                    int idx = (startY + qy) * BannerResolution + (startX + qx);
                    Color32 c32 = (Color32)c;
                    c32.a = (byte)Mathf.RoundToInt(255 * frameAlpha);
                    pixels[idx] = c32;
                }
            }

            // 双重向上几何白箭头
            DrawBannerChevron(pixels, startX, startY, 256f, 415f, 220f, 65f, 20f, white);
            DrawBannerChevron(pixels, startX, startY, 256f, 385f, 170f, 50f, 15f, white);

            // 顶部战队字样
            DrawBannerText(pixels, startX, startY, "TEAM BRAVO", 256f, 456f, 3.2f, (Color32)teamBDark);

            // 核心粗体字母 "B" (5×7 放大 26 倍)
            DrawBannerText(pixels, startX, startY, "B", 256f, 235f, 26f, white);

            // 底部战区字样
            DrawBannerText(pixels, startX, startY, "P1 // DIVISION 02", 256f, 115f, 3.5f, blueAccent);
            DrawBannerText(pixels, startX, startY, "READY // INK UP", 256f, 55f, 3.2f, (Color32)teamBDark);
        }

        private static float CalcBannerChamferFrameAlpha(float qx, float qy)
        {
            // 边缘留出 16px 透明衬垫，45° 几何切角 22px，边缘做 1px 抗锯齿
            float dist = CalcChamferBoxDist(qx - 255.5f, qy - 255.5f, 239.5f, 239.5f, 22f);
            return Mathf.Clamp01(0.5f - dist);
        }

        private static float CalcChamferBoxDist(float dx, float dy, float hx, float hy, float chamfer)
        {
            float ax = Mathf.Abs(dx);
            float ay = Mathf.Abs(dy);

            float dBox = Mathf.Max(ax - hx, ay - hy);
            float dChamfer = (ax + ay - (hx + hy - chamfer)) * 0.70710678f;
            return Mathf.Max(dBox, dChamfer);
        }

        private static void DrawBannerCircle(
            Color32[] pixels,
            int startX,
            int startY,
            float cx,
            float cy,
            float radius,
            Color32 color)
        {
            int minQx = Mathf.Clamp(Mathf.FloorToInt(cx - radius - 1f), 0, 511);
            int maxQx = Mathf.Clamp(Mathf.CeilToInt(cx + radius + 1f), 0, 511);
            int minQy = Mathf.Clamp(Mathf.FloorToInt(cy - radius - 1f), 0, 511);
            int maxQy = Mathf.Clamp(Mathf.CeilToInt(cy + radius + 1f), 0, 511);

            for (int qy = minQy; qy <= maxQy; qy++)
            {
                for (int qx = minQx; qx <= maxQx; qx++)
                {
                    float dx = qx - cx;
                    float dy = qy - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - dist);
                    if (alpha > 0.001f)
                    {
                        int idx = (startY + qy) * BannerResolution + (startX + qx);
                        BlendPixel(pixels, idx, color, alpha);
                    }
                }
            }
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
