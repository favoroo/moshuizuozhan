using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「霓虹海滨工业滑板场 / NEON HARBOR SKATEPARK」几何构建器。
    ///
    /// 设计要点：
    /// 1. **玩法常量复用**：40×56 地面、边界、出生甲板/台阶/出生盘/出生线全部沿用
    ///    <see cref="ArenaLayoutV2"/>，涂地网格、覆盖率判据与 Bot NavGrid 口径不变；
    /// 2. **中央碗池**：地面在碗池开口处切成四块，碗池由 3 圈 32 段 35° 斜坡环带 + 碗底构成，
    ///    （2.1m 深）。碗池是干的：远景水面在碗池开口处挖空 24×16m，避免水灌进池底；
    ///    NE/SW 各一组 8 级对角台阶（0.2625m/级 &lt; stepOffset）从池沿直下碗底；
    /// 3. **对角天桥**：24m 桥面（顶面 2.6m）沿 70° 对角线跨过碗池，两端 6m 坡道落地，
    ///    桥面/坡道顶面受墨、侧面临时不可攀爬（必须走坡道）；
    /// 4. **工业地标**：东北/西南红底绿顶集装箱堆 + 黄塔吊，西北/东南橙色挖掘机工地 +
    ///    蓝色集装箱办公室，细节由 <see cref="SkateparkDetailBuilder"/> 追加（纯装饰）。
    /// 5. 所有场内物件按原点 180° 点对称，双方路线与掩体收益等价。
    /// </summary>
    public static class SkateparkGeometryBuilder
    {
        public const string ArenaRootName = "Arena_NeonSkatepark";

        public static GameObject BuildArenaGeometry(
            ArenaMaterialSet mats,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders,
            out Transform teamASpawnAnchor)
        {
            mats ??= new ArenaMaterialSet();
            Material groundMat = mats.ground;
            Material wallMat = mats.wall ?? mats.concrete;
            Material boundaryMat = mats.boundary ?? wallMat;
            Material spawnMatA = mats.spawnPadA;
            Material spawnMatB = mats.spawnPadB;
            Material concreteMat = mats.concrete ?? wallMat;
            Material concreteDarkMat = mats.concreteDark ?? concreteMat;
            Material deckMat = mats.deck ?? mats.steel ?? concreteMat;
            Material accentMat = mats.accent ?? concreteMat;
            Material steelMat = mats.steel ?? mats.propMetal ?? wallMat;
            Material steelDarkMat = mats.steelDark ?? steelMat;
            Material glassMat = mats.glass ?? steelDarkMat;
            Material emissiveMat = mats.propEmissive ?? steelMat;
            Material containerBlueMat = mats.containerBlue ?? mats.containerSlate ?? wallMat;
            Material containerGreenMat = mats.containerGreen ?? mats.containerTeal ?? wallMat;
            Material containerTealMat = mats.containerTeal ?? wallMat;
            Material containerRustMat = mats.containerRust ?? wallMat;
            Material containerSlateMat = mats.containerSlate ?? wallMat;
            Material containerMustardMat = mats.containerMustard ?? wallMat;
            Material copingMat = mats.boundary ?? mats.cabin ?? concreteMat;

            Material graffitiPinkMat = mats.graffitiPink ?? accentMat;
            Material graffitiCyanMat = mats.graffitiCyan ?? containerTealMat;
            Material graffitiPurpleMat = mats.graffitiPurple ?? accentMat;
            Material graffitiGreenMat = mats.graffitiGreen ?? containerGreenMat;

            var arenaRoot = new GameObject(ArenaRootName);

            // 1. 主地面与多边形平滑碗池（含连续池沿、下沉对角阶梯与内壁涂鸦）
            BuildGroundAndBowl(arenaRoot.transform, groundMat, concreteMat, concreteDarkMat, accentMat, copingMat,
                containerRustMat, graffitiPinkMat, graffitiCyanMat, graffitiPurpleMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 2. 对角高架天桥（深钢色桥面 + 侧面钢桁架 X 腹杆 + 桥底横梁 + Y 形桥墩 + 橙色坡道）
            BuildSkybridge(arenaRoot.transform, deckMat, accentMat, steelMat, steelDarkMat, containerTealMat, containerRustMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 3. 集装箱群（原双层/单箱 + 新增 NE 3 层与 SE 2 层集群，全部接入 DressContainer 细节装配）
            BuildContainers(arenaRoot.transform, containerRustMat, containerTealMat, containerBlueMat,
                containerGreenMat, containerSlateMat, containerMustardMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 4. 工业地标底座（扩大塔吊基座 / 挖掘机低台 / 双方队伍识别集装箱办公室）
            BuildIndustrialBases(arenaRoot.transform, concreteMat, concreteDarkMat, steelDarkMat,
                containerMustardMat, containerRustMat, containerBlueMat, emissiveMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 5. 侧翼 quarter-pipe 与碗池长台（可涂坡道 + 低掩体）
            BuildFlankFeatures(arenaRoot.transform, concreteMat, concreteDarkMat, accentMat, copingMat, steelDarkMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 6. 出生甲板（队伍识别色饰板 + 警示条 + 3 个标准集装箱队伍背景墙 + 两侧台阶 + 出生盘）
            BuildSpawnArea(arenaRoot.transform, groundMat, spawnMatA, spawnMatB, boundaryMat, steelMat, steelDarkMat,
                containerBlueMat, containerGreenMat, containerRustMat, containerMustardMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders, out teamASpawnAnchor);

            // 7. 四周边界墙：南侧矮墙面向港湾水面，北/东西为常规高墙
            BuildBoundaryWalls(arenaRoot.transform, boundaryMat, steelMat, inkCfg, inkWallMat);

            // 8. 覆盖率兜底：给漏装墨面的可视碰撞盒补全
            ArenaGeometryBuilder.EnsurePaintableArena(arenaRoot, inkCfg, inkWallMat);

            // 9. 纯装饰工业细节（塔吊/挖掘机/涂鸦/围栏/电缆/轮胎……全部无碰撞）
            SkateparkDetailBuilder.Build(arenaRoot.transform, mats);

            return arenaRoot;
        }

        // =====================================================================================
        // 地面 + 碗池
        // =====================================================================================

        private static void BuildGroundAndBowl(
            Transform parent,
            Material groundMat,
            Material concreteMat,
            Material concreteDarkMat,
            Material accentMat,
            Material copingMat,
            Material containerRustMat,
            Material graffitiPinkMat,
            Material graffitiCyanMat,
            Material graffitiPurpleMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            float bx = ArenaLayoutSkatepark.BowlHalfX;
            float bz = ArenaLayoutSkatepark.BowlHalfZ;
            float halfMapX = ArenaLayoutV2.MapSizeX * 0.5f;
            float halfMapZ = ArenaLayoutV2.MapSizeZ * 0.5f;

            // 1.1 地面主块：西/东为整条，南/北为碗池开口两侧横条（顶面 y=0）。
            // 名字沿用 Ground_Main_ 前缀，EnsurePaintableArena 会自动跳过立面墨面，
            // 顶面通过 inkableRenderers 采样顶视墨迹图集。
            CreateGroundPiece(parent, "Ground_Main_40x56_Skate_W",
                new Vector3((-halfMapX - bx) * 0.5f, -0.25f, 0f),
                new Vector3(halfMapX - bx, 0.5f, halfMapZ * 2f), groundMat, inkableRenderers);
            CreateGroundPiece(parent, "Ground_Main_40x56_Skate_E",
                new Vector3((halfMapX + bx) * 0.5f, -0.25f, 0f),
                new Vector3(halfMapX - bx, 0.5f, halfMapZ * 2f), groundMat, inkableRenderers);
            CreateGroundPiece(parent, "Ground_Main_40x56_Skate_N",
                new Vector3(0f, -0.25f, (halfMapZ + bz) * 0.5f),
                new Vector3(bx * 2f, 0.5f, halfMapZ - bz), groundMat, inkableRenderers);
            CreateGroundPiece(parent, "Ground_Main_40x56_Skate_S",
                new Vector3(0f, -0.25f, -(halfMapZ + bz) * 0.5f),
                new Vector3(bx * 2f, 0.5f, halfMapZ - bz), groundMat, inkableRenderers);

            // 1.2 地面转角垫块：填充矩形开口与椭圆碗沿之间的四个象限角，保证地面 100% 封闭无破洞。
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    string cornerTag = (sx > 0 ? "E" : "W") + (sz > 0 ? "N" : "S");
                    // 角块 A (外侧大角)：x∈[8.2, 11], z∈[4.6, 7]
                    CreateGroundPiece(parent, $"Ground_Main_Skate_Corner_{cornerTag}_A",
                        new Vector3(sx * 9.6f, -0.25f, sz * 5.8f),
                        new Vector3(2.8f, 0.5f, 2.4f), groundMat, inkableRenderers);
                    // 角块 B (沿 Z 边贴合)：x∈[5.4, 8.2], z∈[6.0, 7]
                    CreateGroundPiece(parent, $"Ground_Main_Skate_Corner_{cornerTag}_B",
                        new Vector3(sx * 6.8f, -0.25f, sz * 6.5f),
                        new Vector3(2.8f, 0.5f, 1.0f), groundMat, inkableRenderers);
                    // 角块 C (沿 X 边贴合)：x∈[9.8, 11], z∈[2.6, 4.6]
                    CreateGroundPiece(parent, $"Ground_Main_Skate_Corner_{cornerTag}_C",
                        new Vector3(sx * 10.4f, -0.25f, sz * 3.6f),
                        new Vector3(1.2f, 0.5f, 2.0f), groundMat, inkableRenderers);
                    // 角块 D (沿 Z 外缘极窄缝弥补)：x∈[0, 5.4], z∈[6.5, 7.0]
                    CreateGroundPiece(parent, $"Ground_Main_Skate_Corner_{cornerTag}_D",
                        new Vector3(sx * 2.7f, -0.25f, sz * 6.75f),
                        new Vector3(5.4f, 0.5f, 0.5f), groundMat, inkableRenderers);
                }
            }

            // 1.3 斜向平滑碗池壁：3 圈多边形环带，每圈 20 段，斜率约 35°（完全可跑动，< 52° 门禁）。
            // 环带之间与切向均带重叠，消除拼缝；每段带 BoxCollider，立面可涂墨且可攀爬潜行。
            int bandCount = ArenaLayoutSkatepark.BowlSlopedBandCount;
            int segments = ArenaLayoutSkatepark.BowlSegmentsPerBand;
            float runPerBand = ArenaLayoutSkatepark.BowlBandRun;
            float risePerBand = ArenaLayoutSkatepark.BowlBandRise;
            float thickness = ArenaLayoutSkatepark.BowlBandThickness;

            for (int b = 0; b < bandCount; b++)
            {
                float yTop = -b * risePerBand;
                float yBot = -(b + 1) * risePerBand;
                float yMid = (yTop + yBot) * 0.5f;

                float rxTop = ArenaLayoutSkatepark.BowlHalfX - b * runPerBand;
                float rxBot = ArenaLayoutSkatepark.BowlHalfX - (b + 1) * runPerBand;
                float rxMid = (rxTop + rxBot) * 0.5f;

                float rzTop = ArenaLayoutSkatepark.BowlHalfZ - b * runPerBand;
                float rzBot = ArenaLayoutSkatepark.BowlHalfZ - (b + 1) * runPerBand;
                float rzMid = (rzTop + rzBot) * 0.5f;

                for (int s = 0; s < segments; s++)
                {
                    float theta0 = s * Mathf.PI * 2f / segments;
                    float theta1 = (s + 1) * Mathf.PI * 2f / segments;
                    float thetaMid = (s + 0.5f) * Mathf.PI * 2f / segments;

                    float cosMid = Mathf.Cos(thetaMid);
                    float sinMid = Mathf.Sin(thetaMid);
                    Vector3 midPos = new Vector3(rxMid * cosMid, yMid, rzMid * sinMid);

                    Vector3 tangent = new Vector3(-rxMid * sinMid, 0f, rzMid * cosMid).normalized;
                    Vector3 normalXZ = new Vector3(tangent.z, 0f, -tangent.x);

                    Vector3 upSlope = (normalXZ * runPerBand + Vector3.up * risePerBand).normalized;
                    // 碗池内壁的可见面朝“碗内 + 朝上”，即 tangent × upSlope
                    Vector3 slopeNormal = Vector3.Cross(tangent, upSlope);

                    Quaternion rot = Quaternion.LookRotation(upSlope, slopeNormal);

                    Vector3 p0 = new Vector3(rxMid * Mathf.Cos(theta0), yMid, rzMid * Mathf.Sin(theta0));
                    Vector3 p1 = new Vector3(rxMid * Mathf.Cos(theta1), yMid, rzMid * Mathf.Sin(theta1));
                    float chordLength = Vector3.Distance(p0, p1);

                    float scaleX = chordLength * 1.10f; // 10% 切向重叠，杜绝漏缝
                    float scaleY = thickness;
                    float scaleZ = 1.32f; // 沿坡长向 8% 重叠

                    // 盒体中心向法线负方向微移半厚度，使斜板正面精准落在 midPos
                    Vector3 boxCenter = midPos - slopeNormal * (scaleY * 0.5f);

                    var seg = ArenaGeometryBuilder.CreateBox(parent, $"Skatepark_Bowl_Band{b}_Seg{s}",
                        boxCenter, new Vector3(scaleX, scaleY, scaleZ), concreteDarkMat, rot);
                    if (inkableRenderers != null) inkableRenderers.Add(seg.GetComponent<Renderer>());
                    ArenaGeometryBuilder.AddOrientedInkWallSurface(seg, inkCfg, inkWallMat, allowClimb: true);
                }
            }

            // 1.4 碗底（顶面 y = -2.1m）：采用 groundMat 承接烘焙的碗底纹理与涂墨
            var bowlFloor = ArenaGeometryBuilder.CreateBox(parent, "Ground_Main_Skate_BowlFloor",
                new Vector3(0f, ArenaLayoutSkatepark.BowlFloorY - 0.15f, 0f),
                new Vector3(ArenaLayoutSkatepark.BowlFloorHalfX * 2f + 0.5f, 0.3f,
                    ArenaLayoutSkatepark.BowlFloorHalfZ * 2f + 0.5f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(bowlFloor.GetComponent<Renderer>());

            // 1.5 碗底争夺方块：原点 180° 点对称成对放置（旧版只有西北一块，先到的一方白得掩体）
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 blockPos = new Vector3(
                    ArenaLayoutSkatepark.BowlBlockPos.x * sign,
                    ArenaLayoutSkatepark.BowlFloorY + ArenaLayoutSkatepark.BowlBlockHeight * 0.5f,
                    ArenaLayoutSkatepark.BowlBlockPos.z * sign);
                var block = ArenaGeometryBuilder.CreateBox(parent, "Skatepark_Bowl_Block_" + (mirror == 0 ? "NW" : "SE"),
                    blockPos,
                    new Vector3(ArenaLayoutSkatepark.BowlBlockSizeX, ArenaLayoutSkatepark.BowlBlockHeight,
                        ArenaLayoutSkatepark.BowlBlockSizeZ), concreteDarkMat);
                ArenaGeometryBuilder.AddInkWallSurface(block, inkCfg, inkWallMat, true);
                if (inkableRenderers != null) inkableRenderers.Add(block.GetComponent<Renderer>());
                if (blockedColliders != null) blockedColliders.Add(block.GetComponent<Collider>());
            }

            // 1.5b 碗池中央低脊：池底 16×8m 原本是一整片空地，从天桥俯视读作空水槽。
            // 3.6×0.55×1.8m 的低脊给池底一个中心掩体 + 滑板脊线，桥下 4m 净高足够跳上。
            Vector3 centerBlockPos = new Vector3(
                ArenaLayoutSkatepark.BowlCenterBlockPos.x,
                ArenaLayoutSkatepark.BowlFloorY + ArenaLayoutSkatepark.BowlCenterBlockHeight * 0.5f,
                ArenaLayoutSkatepark.BowlCenterBlockPos.z);
            var centerBlock = ArenaGeometryBuilder.CreateBox(parent, "Skatepark_Bowl_Block_Center",
                centerBlockPos,
                new Vector3(ArenaLayoutSkatepark.BowlCenterBlockSizeX, ArenaLayoutSkatepark.BowlCenterBlockHeight,
                    ArenaLayoutSkatepark.BowlCenterBlockSizeZ), concreteDarkMat);
            ArenaGeometryBuilder.AddInkWallSurface(centerBlock, inkCfg, inkWallMat, true);
            if (inkableRenderers != null) inkableRenderers.Add(centerBlock.GetComponent<Renderer>());
            if (blockedColliders != null) blockedColliders.Add(centerBlock.GetComponent<Collider>());

            // 1.6 碗底与斜壁交界处深色收边（0.25m 宽 concreteDark 薄板，y = -2.09m，增强下沉对比）
            int floorEdgeSegments = 32;
            float rFloorX = ArenaLayoutSkatepark.BowlFloorHalfX;
            float rFloorZ = ArenaLayoutSkatepark.BowlFloorHalfZ;
            for (int i = 0; i < floorEdgeSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / floorEdgeSegments;
                float a1 = (i + 1) * Mathf.PI * 2f / floorEdgeSegments;
                float aMid = (i + 0.5f) * Mathf.PI * 2f / floorEdgeSegments;

                Vector3 p0 = new Vector3(rFloorX * Mathf.Cos(a0), 0f, rFloorZ * Mathf.Sin(a0));
                Vector3 p1 = new Vector3(rFloorX * Mathf.Cos(a1), 0f, rFloorZ * Mathf.Sin(a1));
                float chord = Vector3.Distance(p0, p1);
                float segLen = chord * 1.12f;

                float x = Mathf.Cos(aMid) * rFloorX;
                float z = Mathf.Sin(aMid) * rFloorZ;
                float dx = -Mathf.Sin(aMid) * rFloorX;
                float dz = Mathf.Cos(aMid) * rFloorZ;
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                Quaternion edgeRot = Quaternion.Euler(0f, yaw, 0f);

                CreateDecorRotated(parent, $"Skatepark_Bowl_FloorEdgeTrim_{i}",
                    new Vector3(x, -2.09f, z),
                    new Vector3(0.25f, 0.02f, segLen),
                    concreteDarkMat, edgeRot);
            }

            // 1.7 池沿内侧暗色阴影带（0.12m 宽，y = 0.005m，增强边缘下沉读形）
            int rimShadowSegments = 36;
            float rRimShadowX = ArenaLayoutSkatepark.BowlHalfX - 0.06f;
            float rRimShadowZ = ArenaLayoutSkatepark.BowlHalfZ - 0.06f;
            for (int i = 0; i < rimShadowSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / rimShadowSegments;
                float a1 = (i + 1) * Mathf.PI * 2f / rimShadowSegments;
                float aMid = (i + 0.5f) * Mathf.PI * 2f / rimShadowSegments;

                Vector3 p0 = new Vector3(rRimShadowX * Mathf.Cos(a0), 0f, rRimShadowZ * Mathf.Sin(a0));
                Vector3 p1 = new Vector3(rRimShadowX * Mathf.Cos(a1), 0f, rRimShadowZ * Mathf.Sin(a1));
                float chord = Vector3.Distance(p0, p1);
                float segLen = chord * 1.12f;

                float x = Mathf.Cos(aMid) * rRimShadowX;
                float z = Mathf.Sin(aMid) * rRimShadowZ;
                float dx = -Mathf.Sin(aMid) * rRimShadowX;
                float dz = Mathf.Cos(aMid) * rRimShadowZ;
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                Quaternion shadowRot = Quaternion.Euler(0f, yaw, 0f);

                CreateDecorRotated(parent, $"Skatepark_Bowl_RimShadow_{i}",
                    new Vector3(x, 0.005f, z),
                    new Vector3(0.12f, 0.015f, segLen),
                    concreteDarkMat, shadowRot);
            }

            // 1.8 连续池沿（Coping）：平整收口环带 + 36 段连续金属/浅灰圆管（纯装饰、无碰撞、无缝隙）
            int copingSegments = ArenaLayoutSkatepark.BowlCopingSegments;
            float rimBandWidth = ArenaLayoutSkatepark.BowlRimBandWidth;
            float rxRim = ArenaLayoutSkatepark.BowlHalfX;
            float rzRim = ArenaLayoutSkatepark.BowlHalfZ;

            for (int i = 0; i < copingSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / copingSegments;
                float a1 = (i + 1) * Mathf.PI * 2f / copingSegments;
                float aMid = (i + 0.5f) * Mathf.PI * 2f / copingSegments;

                Vector3 p0 = new Vector3(rxRim * Mathf.Cos(a0), 0f, rzRim * Mathf.Sin(a0));
                Vector3 p1 = new Vector3(rxRim * Mathf.Cos(a1), 0f, rzRim * Mathf.Sin(a1));
                float chord = Vector3.Distance(p0, p1);
                float segLen = chord * 1.12f;

                float x = Mathf.Cos(aMid) * rxRim;
                float z = Mathf.Sin(aMid) * rzRim;
                float dx = -Mathf.Sin(aMid) * rxRim;
                float dz = Mathf.Cos(aMid) * rzRim;
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                Quaternion rimRot = Quaternion.Euler(0f, yaw, 0f);

                Vector3 outward = new Vector3(Mathf.Cos(aMid), 0f, Mathf.Sin(aMid)).normalized;

                // 平整收口压带：用深色混凝土而不是浅灰，让唯一的高亮留在 coping 管上。
                // 旧版 0.34m 浅灰压带 + 浅灰 coping + 烘焙白线叠成 3 圈白环，俯瞰读作「转盘跑道」。
                CreateDecorRotated(parent, $"Skatepark_Bowl_RimBand_{i}",
                    new Vector3(x, 0.015f, z) + outward * (rimBandWidth * 0.45f),
                    new Vector3(rimBandWidth, 0.03f, segLen),
                    concreteDarkMat, rimRot);

                // 连续 coping 管（内缘高亮细管）
                CreateDecorRotated(parent, $"Skatepark_Bowl_Coping_{i}",
                    new Vector3(x, 0.04f, z),
                    new Vector3(ArenaLayoutSkatepark.BowlCopingPipeWidth, ArenaLayoutSkatepark.BowlCopingPipeHeight, segLen),
                    copingMat, rimRot);
            }

            // 1.9 宽阔混凝土下沉台阶（NE 与 SW 对角线，180° 点对称，直通碗底）
            BuildDiagonalStairs(parent, groundMat, copingMat, inkCfg, inkWallMat, inkableRenderers);

            // 1.10 碗池内壁装饰：沿池沿内侧的橙黄警示带与彩色涂鸦斑块（0.015m 厚、无碰撞、无 z-fighting）
            BuildBowlWallArt(parent, accentMat, containerRustMat,
                graffitiPinkMat, graffitiCyanMat, graffitiPurpleMat);
        }

        private static void BuildDiagonalStairs(
            Transform parent,
            Material groundMat,
            Material copingMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers)
        {
            // 沿 NE 52° 与 SW 232° 铺设宽 2.4m 的下沉大阶梯
            float stairAngle = 52f * Mathf.Deg2Rad;
            float rx = ArenaLayoutSkatepark.BowlHalfX;
            float rz = ArenaLayoutSkatepark.BowlHalfZ;

            for (int side = -1; side <= 1; side += 2)
            {
                string tag = side > 0 ? "NE" : "SW";
                Vector3 rimPt = new Vector3(side * rx * Mathf.Cos(stairAngle), 0f, side * rz * Mathf.Sin(stairAngle));
                Vector3 inwardDir = -new Vector3(rimPt.x / rx, 0f, rimPt.z / rz).normalized;
                Quaternion stairRot = Quaternion.LookRotation(inwardDir, Vector3.up);

                int stepCount = ArenaLayoutSkatepark.BowlStairStepCount; // 8
                float rise = ArenaLayoutSkatepark.BowlStairRise; // 0.2625m
                float tread = ArenaLayoutSkatepark.BowlStairTread; // 0.40m
                float width = ArenaLayoutSkatepark.BowlStairWidth; // 2.4m
                float bottomY = ArenaLayoutSkatepark.BowlFloorY - 0.15f;
                // 35° 池壁的落差/进深比：台阶前沿必须落在坡面同高处，整组台阶才是「挖」进池壁，
                // 旧版按 i*tread 从池沿起排，每一级都低于坡面，5 级台阶整体被埋在环带里（玩家看不到也走不了）。
                float slopeDropPerRun = ArenaLayoutSkatepark.BowlBandRise / ArenaLayoutSkatepark.BowlBandRun;

                for (int i = 0; i < stepCount; i++)
                {
                    float topY = -(i + 1) * rise;
                    float height = topY - bottomY;
                    float centerY = (topY + bottomY) * 0.5f;
                    float noseRun = (i + 1) * rise / slopeDropPerRun;
                    float dist = noseRun + tread * 0.5f;

                    Vector3 center = rimPt + inwardDir * dist;
                    center.y = centerY;

                    var step = ArenaGeometryBuilder.CreateBox(parent, $"Skatepark_BowlStair_{tag}_Step{i}",
                        center, new Vector3(width, height, tread * 1.06f), groundMat, stairRot);
                    if (inkableRenderers != null) inkableRenderers.Add(step.GetComponent<Renderer>());
                    ArenaGeometryBuilder.AddOrientedInkWallSurface(step, inkCfg, inkWallMat, true);

                    // 浅色踏步鼻线：台阶在池壁阴影里会读成一整条黑梯，鼻线让每一级读成混凝土踏步。
                    Vector3 nosePos = center - inwardDir * (tread * 0.5f - 0.035f);
                    nosePos.y = topY + 0.02f;
                    CreateDecorRotated(parent, $"Skatepark_BowlStair_{tag}_Nose{i}",
                        nosePos, new Vector3(width + 0.02f, 0.04f, 0.07f), copingMat, stairRot);
                }
            }
        }

        private static void BuildBowlWallArt(
            Transform parent,
            Material accentMat,
            Material rustMat,
            Material pinkMat,
            Material cyanMat,
            Material purpleMat)
        {
            // 1. 池沿内壁贴地警示条（沿顶带内壁，带 +0.015m 法向偏移）
            float rxTop = ArenaLayoutSkatepark.BowlHalfX - 0.5f * ArenaLayoutSkatepark.BowlBandRun;
            float rzTop = ArenaLayoutSkatepark.BowlHalfZ - 0.5f * ArenaLayoutSkatepark.BowlBandRun;
            float yTop = -0.35f;

            float[] stripAngles = { 15f, 75f, 135f, 195f, 255f, 315f };
            for (int i = 0; i < stripAngles.Length; i++)
            {
                float a = stripAngles[i] * Mathf.Deg2Rad;
                Vector3 p = new Vector3(rxTop * Mathf.Cos(a), yTop, rzTop * Mathf.Sin(a));
                Vector3 tan = new Vector3(-rxTop * Mathf.Sin(a), 0f, rzTop * Mathf.Cos(a)).normalized;
                Vector3 normXZ = new Vector3(tan.z, 0f, -tan.x);
                Vector3 upSlope = (normXZ * 1.0f + Vector3.up * 0.7f).normalized;
                // 贴花面向碗内 + 朝上，微偏离开墙体进入可见空间
                Vector3 slopeNorm = Vector3.Cross(tan, upSlope);
                Quaternion rot = Quaternion.LookRotation(upSlope, slopeNorm);

                Material stripMat = (i % 2 == 0) ? rustMat : accentMat;
                CreateDecorRotated(parent, $"Skatepark_Bowl_RimStripe_{i}",
                    p + slopeNorm * 0.015f, new Vector3(1.6f, 0.012f, 0.14f), stripMat, rot);
            }

            // 2. 碗池内壁涂鸦色块（参考图中的 SKATE 风格大面积喷漆）
            // 在 SW 与 NE 对称墙面各设 3 组彩色涂鸦贴花
            for (int side = -1; side <= 1; side += 2)
            {
                string tag = side > 0 ? "NE" : "SW";
                // 橙色大字块（Band 0 & 1 交界处）
                float a1 = (side > 0 ? 32f : 212f) * Mathf.Deg2Rad;
                SpawnWallDecal(parent, $"Skatepark_BowlGraffiti_{tag}_Orange",
                    a1, 1.0f, -0.7f, 2.6f, 0.85f, rustMat);

                // 荧光粉辅助块
                float a2 = (side > 0 ? 42f : 222f) * Mathf.Deg2Rad;
                SpawnWallDecal(parent, $"Skatepark_BowlGraffiti_{tag}_Pink",
                    a2, 0.5f, -0.4f, 1.6f, 0.55f, pinkMat);

                // 电光青细线条
                float a3 = (side > 0 ? 22f : 202f) * Mathf.Deg2Rad;
                SpawnWallDecal(parent, $"Skatepark_BowlGraffiti_{tag}_Cyan",
                    a3, 1.4f, -1.0f, 1.8f, 0.45f, cyanMat);
            }
        }

        private static void SpawnWallDecal(
            Transform parent,
            string name,
            float angleRad,
            float runOffset,
            float yPos,
            float width,
            float height,
            Material mat)
        {
            float rx = ArenaLayoutSkatepark.BowlHalfX - runOffset;
            float rz = ArenaLayoutSkatepark.BowlHalfZ - runOffset;
            Vector3 pos = new Vector3(rx * Mathf.Cos(angleRad), yPos, rz * Mathf.Sin(angleRad));
            Vector3 tan = new Vector3(-rx * Mathf.Sin(angleRad), 0f, rz * Mathf.Cos(angleRad)).normalized;
            Vector3 normXZ = new Vector3(tan.z, 0f, -tan.x);
            Vector3 upSlope = (normXZ * 1.0f + Vector3.up * 0.7f).normalized;
            // 贴花面向碗内 + 朝上
            Vector3 slopeNorm = Vector3.Cross(tan, upSlope);
            Quaternion rot = Quaternion.LookRotation(upSlope, slopeNorm);

            CreateDecorRotated(parent, name, pos + slopeNorm * 0.018f,
                new Vector3(width, 0.015f, height), mat, rot);
        }

        private static GameObject CreateGroundPiece(
            Transform parent,
            string name,
            Vector3 center,
            Vector3 size,
            Material mat,
            List<Renderer> inkableRenderers)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, center, size, mat);
            if (inkableRenderers != null) inkableRenderers.Add(go.GetComponent<Renderer>());
            return go;
        }

        private static void RegisterGround(
            GameObject box,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers)
        {
            if (inkableRenderers != null) inkableRenderers.Add(box.GetComponent<Renderer>());
            // 碗池立面可涂、可潜行攀爬（涂满己方墨后能贴墙游上去）
            ArenaGeometryBuilder.AddInkWallSurface(box, inkCfg, inkWallMat, true);
        }

        // =====================================================================================
        // 对角高架天桥
        // =====================================================================================

        private static void BuildSkybridge(
            Transform parent,
            Material deckMat,
            Material accentMat,
            Material steelMat,
            Material steelDarkMat,
            Material containerTealMat,
            Material containerRustMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            float yaw = ArenaLayoutSkatepark.BridgeYawDegrees;
            float width = ArenaLayoutSkatepark.BridgeWidth;
            float length = ArenaLayoutSkatepark.BridgeLength;
            float deckTop = ArenaLayoutSkatepark.BridgeDeckTopY;
            float thickness = ArenaLayoutSkatepark.BridgeDeckThickness;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

            // 2.1 桥面：深钢色 deckMat，顶面受墨，自身不进 blockedColliders（避免桥下碗池被判不可涂）。
            var deck = ArenaGeometryBuilder.CreateBox(parent, "Skatepark_Skybridge_Deck",
                new Vector3(0f, deckTop - thickness * 0.5f, 0f),
                new Vector3(width, thickness, length), steelDarkMat, yawRot);
            if (inkableRenderers != null) inkableRenderers.Add(deck.GetComponent<Renderer>());
            ArenaGeometryBuilder.AddOrientedInkWallSurface(deck, inkCfg, inkWallMat, false);
            var deckFader = deck.AddComponent<OcclusionFader>();
            deckFader.MinAlpha = 0.35f;

            // 2.2 两侧工业钢桁架（Truss Chords + Vertical Posts + X-Diagonals，参考图关键特征）
            Material trussMat = containerTealMat ?? steelDarkMat;
            float trussH = ArenaLayoutSkatepark.BridgeTrussHeight; // 1.05m
            float halfLen = length * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                float sideX = side * (width * 0.5f + 0.04f);
                string sideTag = side < 0 ? "W" : "E";

                // 下弦杆（贴紧桥面边缘）
                Vector3 botChordLocal = new Vector3(sideX, deckTop - thickness * 0.5f, 0f);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_ChordBot_{sideTag}",
                    yawRot * botChordLocal, new Vector3(0.10f, 0.12f, length), trussMat, yawRot);

                // 上弦杆（桥面以上 1.05m）
                Vector3 topChordLocal = new Vector3(sideX, deckTop + trussH, 0f);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_ChordTop_{sideTag}",
                    yawRot * topChordLocal, new Vector3(0.10f, 0.10f, length), trussMat, yawRot);

                // 桁架立柱与 X 交叉腹杆（每节间约 1.6m）
                int bayCount = 14;
                float bayStep = (length - 0.8f) / bayCount;
                float bayStart = -halfLen + 0.4f;

                for (int b = 0; b <= bayCount; b++)
                {
                    float postZ = bayStart + b * bayStep;
                    Vector3 postLocal = new Vector3(sideX, deckTop + trussH * 0.5f, postZ);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_TrussPost_{sideTag}_{b}",
                        yawRot * postLocal, new Vector3(0.08f, trussH, 0.08f), trussMat, yawRot);
                }

                for (int b = 0; b < bayCount; b++)
                {
                    float zA = bayStart + b * bayStep;
                    float zB = bayStart + (b + 1) * bayStep;
                    float zMid = (zA + zB) * 0.5f;
                    float bayLen = zB - zA;
                    float diagLen = Mathf.Sqrt(bayLen * bayLen + trussH * trussH);
                    float pitchAngle = Mathf.Atan2(trussH, bayLen) * Mathf.Rad2Deg;

                    Vector3 midLocal = new Vector3(sideX, deckTop + trussH * 0.5f, zMid);

                    // 斜腹杆 1
                    Quaternion rotA = yawRot * Quaternion.Euler(pitchAngle, 0f, 0f);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_XDiagA_{sideTag}_{b}",
                        yawRot * midLocal, new Vector3(0.06f, 0.06f, diagLen), trussMat, rotA);

                    // 斜腹杆 2
                    Quaternion rotB = yawRot * Quaternion.Euler(-pitchAngle, 0f, 0f);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_XDiagB_{sideTag}_{b}",
                        yawRot * midLocal, new Vector3(0.06f, 0.06f, diagLen), trussMat, rotB);
                }
            }

            // 2.3 桥底横梁（每隔 2.0m 一根，碗底仰视清晰可见）
            for (float z = -halfLen + 1.2f; z <= halfLen - 0.5f; z += 2.0f)
            {
                Vector3 beamLocal = new Vector3(0f, deckTop - thickness - 0.06f, z);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_CrossBeam_{Mathf.RoundToInt(z + 50f)}",
                    yawRot * beamLocal, new Vector3(width + 0.16f, 0.12f, 0.12f), steelDarkMat, yawRot);
            }

            // 2.4 两组 Y 形工业桥墩（位于 local Z = ±7.5m，从碗底 -2.1m 升起支撑桥身）
            float pierZ = ArenaLayoutSkatepark.BridgePierLocalZ;
            for (int pSign = -1; pSign <= 1; pSign += 2)
            {
                string pierTag = pSign > 0 ? "NE" : "SW";
                float lz = pSign * pierZ;
                float floorY = ArenaLayoutSkatepark.BowlFloorY;
                float forkY = 0.55f;
                float deckBotY = deckTop - thickness;

                // 碗底基础混凝土承台
                Vector3 footLocal = new Vector3(0f, floorY + 0.18f, lz);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_PierFoot_{pierTag}",
                    yawRot * footLocal, new Vector3(0.75f, 0.36f, 0.75f), steelDarkMat, yawRot);

                // 下部垂直立柱
                float trunkH = forkY - (floorY + 0.36f);
                Vector3 trunkLocal = new Vector3(0f, floorY + 0.36f + trunkH * 0.5f, lz);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_PierTrunk_{pierTag}",
                    yawRot * trunkLocal, new Vector3(0.34f, trunkH, 0.34f), steelDarkMat, yawRot);

                // Y 形分叉臂（左右对称斜撑到桥面底梁）
                float armH = deckBotY - forkY;
                float armReachX = width * 0.5f - 0.18f;
                float armLen = Mathf.Sqrt(armReachX * armReachX + armH * armH);
                float armAngle = Mathf.Atan2(armReachX, armH) * Mathf.Rad2Deg;

                for (int armSide = -1; armSide <= 1; armSide += 2)
                {
                    string armTag = armSide < 0 ? "W" : "E";
                    Vector3 armMid = new Vector3(armSide * armReachX * 0.5f, forkY + armH * 0.5f, lz);
                    Quaternion armRot = yawRot * Quaternion.Euler(0f, 0f, -armSide * armAngle);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_PierArm_{pierTag}_{armTag}",
                        yawRot * armMid, new Vector3(0.26f, armLen, 0.26f), steelDarkMat, armRot);
                }
            }

            // 2.5 桥面中线引导虚线（黄色窄虚线）
            for (int i = 0; i < 11; i++)
            {
                float localZ = -halfLen + 1.5f + i * 2.1f;
                Vector3 dashWorld = yawRot * new Vector3(0f, 0f, localZ);
                CreateDecorRotated(parent, $"Skatepark_Skybridge_Dash_{i}",
                    dashWorld + Vector3.up * (deckTop + 0.012f),
                    new Vector3(0.10f, 0.015f, 1.1f), accentMat, yawRot);
            }

            // 2.6 两端下桥橙色坡道（6m 跑长，22.5°，坡面涂墨可攀爬，两侧带深色钢挡板与简易护栏）
            float pitch = ArenaLayoutSkatepark.BridgeRampPitchDegrees;
            float rampLength = ArenaLayoutSkatepark.BridgeRampLength;
            for (int end = -1; end <= 1; end += 2)
            {
                string tag = end < 0 ? "SW" : "NE";
                Quaternion rampRot = yawRot * Quaternion.Euler(end > 0 ? pitch : -pitch, 0f, 0f);
                Vector3 rampCenterLocal = new Vector3(0f, 0f, end * (length * 0.5f + rampLength * 0.5f));
                Vector3 rampWorld = yawRot * rampCenterLocal;
                float rampCenterY = deckTop * 0.5f;

                // 坡道主体用高饱和橙 accent（本次已把 Mat_SP_Accent 从警示黄改成 0.95/0.45/0.06）
                var ramp = ArenaGeometryBuilder.CreateBox(parent, $"Skatepark_Skybridge_Ramp_{tag}",
                    new Vector3(rampWorld.x, rampCenterY, rampWorld.z),
                    new Vector3(width, 0.22f, rampLength), accentMat, rampRot);
                if (inkableRenderers != null) inkableRenderers.Add(ramp.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddOrientedInkWallSurface(ramp, inkCfg, inkWallMat, false);
                ramp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

                // 两侧深色护沿钢板（增强侧向体积感）
                for (int side = -1; side <= 1; side += 2)
                {
                    float sideX = side * (width * 0.5f + 0.04f);
                    Vector3 plateLocal = new Vector3(sideX, 0.08f, 0f);
                    Vector3 plateWorld = ramp.transform.TransformPoint(plateLocal);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_RampPlate_{tag}_{(side < 0 ? "W" : "E")}",
                        plateWorld, new Vector3(0.08f, 0.32f, rampLength), steelDarkMat, rampRot);

                    // 坡道栏杆
                    Vector3 railLocal = new Vector3(side * (width * 0.5f - 0.08f), 0.11f, 0f);
                    Vector3 railWorld = ramp.transform.TransformPoint(railLocal);
                    CreateDecorRotated(parent, $"Skatepark_Skybridge_RampRail_{tag}_{(side < 0 ? "W" : "E")}",
                        railWorld + Vector3.up * 0.65f, new Vector3(0.06f, 0.06f, rampLength),
                        containerRustMat, rampRot);
                }

                // 坡道下方接地支撑方柱
                for (int sup = -1; sup <= 1; sup += 2)
                {
                    Vector3 under = ramp.transform.TransformPoint(new Vector3(0f, -0.11f, sup * 1.8f));
                    if (under.y > 0.08f)
                    {
                        CreateDecorRotated(parent, $"Skatepark_Skybridge_RampSupport_{tag}_{(sup < 0 ? "In" : "Out")}",
                            new Vector3(under.x, under.y * 0.5f, under.z),
                            new Vector3(0.22f, under.y, 0.22f), steelDarkMat, Quaternion.identity);
                    }
                }
            }
        }
        // =====================================================================================
        // 集装箱
        // =====================================================================================

        private static void BuildContainers(
            Transform parent,
            Material rustMat,
            Material tealMat,
            Material blueMat,
            Material greenMat,
            Material slateMat,
            Material mustardMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            Vector3 size = ArenaLayoutSkatepark.ContainerSize;

            // 3.1 经典双层堆（NE 红底绿顶堆，SW 对称镜像）
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.StackPos;
                Vector3 world = new Vector3(p.x * sign, 0f, p.z * sign);
                float yaw = ArenaLayoutSkatepark.StackYaw + (mirror == 0 ? 0f : 180f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                string tag = mirror == 0 ? "NE" : "SW";

                Vector3 botCenter = world + Vector3.up * (size.y * 0.5f);
                var bottom = CreateSolidRotated(parent, "Skatepark_Container_Stack" + tag + "_Bottom",
                    botCenter, size, rustMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                bottom.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, botCenter, yaw, size, mirror * 10 + 1);

                Vector3 topCenter = world + Vector3.up * (size.y * 1.5f + 0.02f);
                var top = CreateSolidRotated(parent, "Skatepark_Container_Stack" + tag + "_Top",
                    topCenter, size, greenMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                top.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, topCenter, yaw, size, mirror * 10 + 2);
            }

            // 3.2 经典单层箱（NW 蓝箱，SE 对称镜像）
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.SingleContainerPos;
                Vector3 world = new Vector3(p.x * sign, 0f, p.z * sign);
                float yaw = ArenaLayoutSkatepark.SingleContainerYaw + (mirror == 0 ? 0f : 180f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                string tag = mirror == 0 ? "NW" : "SE";

                Vector3 boxCenter = world + Vector3.up * (size.y * 0.5f);
                var box = CreateSolidRotated(parent, "Skatepark_Container_Single" + tag,
                    boxCenter, size, blueMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                box.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, boxCenter, yaw, size, mirror * 10 + 3);
            }

            // 3.3 新增集群 A（NE 蓝队后方 3 层高堆，SW 对称镜像）：
            // 底层亮蓝、中层草绿、顶层青蓝，形成港湾集装箱天际线
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.ExtraStackPosA;
                Vector3 world = new Vector3(p.x * sign, 0f, p.z * sign);
                float yaw = ArenaLayoutSkatepark.ExtraStackYawA + (mirror == 0 ? 0f : 180f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                string tag = mirror == 0 ? "NE" : "SW";

                // Level 1: 亮蓝
                Vector3 c1 = world + Vector3.up * (size.y * 0.5f);
                var l1 = CreateSolidRotated(parent, $"Skatepark_Container_ExtraA_{tag}_L1",
                    c1, size, blueMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                l1.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, c1, yaw, size, mirror * 20 + 4);

                // Level 2: 草绿
                Vector3 c2 = world + Vector3.up * (size.y * 1.5f + 0.02f);
                var l2 = CreateSolidRotated(parent, $"Skatepark_Container_ExtraA_{tag}_L2",
                    c2, size, greenMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                l2.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, c2, yaw, size, mirror * 20 + 5);

                // Level 3: 青蓝
                Vector3 c3 = world + Vector3.up * (size.y * 2.5f + 0.04f);
                var l3 = CreateSolidRotated(parent, $"Skatepark_Container_ExtraA_{tag}_L3",
                    c3, size, tealMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                l3.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, c3, yaw, size, mirror * 20 + 6);
            }

            // 3.4 新增集群 B（SE 侧翼通道 2 层堆，NW 对称镜像）：
            // 底层草绿、顶层青蓝
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.ExtraStackPosB;
                Vector3 world = new Vector3(p.x * sign, 0f, p.z * sign);
                float yaw = ArenaLayoutSkatepark.ExtraStackYawB + (mirror == 0 ? 0f : 180f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                string tag = mirror == 0 ? "SE" : "NW";

                // Level 1: 草绿
                Vector3 c1 = world + Vector3.up * (size.y * 0.5f);
                var l1 = CreateSolidRotated(parent, $"Skatepark_Container_ExtraB_{tag}_L1",
                    c1, size, greenMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                l1.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, c1, yaw, size, mirror * 20 + 7);

                // Level 2: 蓝青
                Vector3 c2 = world + Vector3.up * (size.y * 1.5f + 0.02f);
                var l2 = CreateSolidRotated(parent, $"Skatepark_Container_ExtraB_{tag}_L2",
                    c2, size, tealMat, rot,
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                l2.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, c2, yaw, size, mirror * 20 + 8);
            }
        }

        // =====================================================================================
        // 工业地标底座
        // =====================================================================================

        private static void BuildIndustrialBases(
            Transform parent,
            Material concreteMat,
            Material concreteDarkMat,
            Material steelDarkMat,
            Material mustardMat,
            Material rustMat,
            Material blueMat,
            Material emissiveMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                string tag = mirror == 0 ? "A" : "B";

                // 塔吊基座：1.0m 高混凝土台（3.2m 见方，45° 斜置），黄黑警示压顶
                // 尺寸统一取布局常量，避免与地面烘焙贴花/通行缝审计脱节
                Vector3 cranePos = ArenaLayoutSkatepark.CranePos;
                Vector3 craneWorld = new Vector3(cranePos.x * sign, 0f, cranePos.z * sign);
                float craneYaw = ArenaLayoutSkatepark.CraneYaw + (mirror == 0 ? 0f : 180f);
                Vector3 craneBaseSize = ArenaLayoutSkatepark.CraneBaseSize;
                var craneBase = CreateSolidRotated(parent, "Skatepark_Crane_Base_" + tag,
                    craneWorld + Vector3.up * (ArenaLayoutSkatepark.CraneBaseHeight * 0.5f),
                    craneBaseSize, concreteMat,
                    Quaternion.Euler(0f, craneYaw, 0f),
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                craneBase.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                CreateHazardTrim(parent, craneWorld, craneBaseSize.x,
                    ArenaLayoutSkatepark.CraneBaseHeight, craneYaw, mustardMat, steelDarkMat);

                // 挖掘机：低台 + 履带/机身实体盒
                Vector3 excPos = ArenaLayoutSkatepark.ExcavatorPos;
                Vector3 excWorld = new Vector3(excPos.x * sign, 0f, excPos.z * sign);
                float excYaw = ArenaLayoutSkatepark.ExcavatorYaw + (mirror == 0 ? 0f : 180f);
                var plinth = CreateSolidRotated(parent, "Skatepark_Excavator_Plinth_" + tag,
                    excWorld + Vector3.up * (ArenaLayoutSkatepark.ExcavatorPlinthHeight * 0.5f),
                    ArenaLayoutSkatepark.ExcavatorPlinthSize, concreteDarkMat,
                    Quaternion.Euler(0f, excYaw, 0f),
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                plinth.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                CreateHazardTrim(parent, excWorld, ArenaLayoutSkatepark.ExcavatorPlinthSize.x,
                    ArenaLayoutSkatepark.ExcavatorPlinthHeight, excYaw, mustardMat, steelDarkMat);

                var body = CreateSolidRotated(parent, "Skatepark_Excavator_Body_" + tag,
                    excWorld + Vector3.up * (ArenaLayoutSkatepark.ExcavatorPlinthHeight + 0.8f),
                    new Vector3(2.6f, 1.6f, 3.6f), rustMat,
                    Quaternion.Euler(0f, excYaw, 0f),
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                body.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

                // 集装箱办公室：出生出口高掩体
                // 北侧（Team B 出口，mirror 0）为蓝色；南侧（Team A 出口，mirror 1）为橙色！
                Vector3 officePos = ArenaLayoutSkatepark.OfficePos;
                Vector3 officeWorld = new Vector3(officePos.x * sign, 0f, officePos.z * sign);
                float officeYaw = ArenaLayoutSkatepark.OfficeYaw + (mirror == 0 ? 0f : 180f);
                Vector3 officeSize = ArenaLayoutSkatepark.OfficeSize;
                Material officeMat = mirror == 0 ? blueMat : rustMat;
                Vector3 officeCenter = officeWorld + Vector3.up * (officeSize.y * 0.5f);
                var office = CreateSolidRotated(parent, "Skatepark_Office_" + tag,
                    officeCenter, officeSize, officeMat,
                    Quaternion.Euler(0f, officeYaw, 0f),
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                office.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                SkateparkDetailBuilder.DressContainer(parent, officeCenter, officeYaw, officeSize, mirror * 10 + 9);

                // 场外远景路灯柱（纯装饰，站台之外，不挡子弹）
                CreateDecorRotated(parent, "Skatepark_YardLamp_" + tag,
                    officeWorld + new Vector3(sign * 2.6f, 3.4f, 0f),
                    new Vector3(0.14f, 6.8f, 0.14f), steelDarkMat, Quaternion.identity);
                CreateDecorRotated(parent, "Skatepark_YardLampHead_" + tag,
                    officeWorld + new Vector3(sign * 2.6f, 6.7f, 0f),
                    new Vector3(0.5f, 0.24f, 0.5f), emissiveMat, Quaternion.identity);
            }
        }

        private static void CreateHazardTrim(Transform parent, Vector3 center, float size, float baseHeight,
            float yaw, Material mustardMat, Material darkMat)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            float half = size * 0.5f;
            // 四条边交替黄/黑短块，俯瞰读成"工地边缘"
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 4; i++)
                {
                    float t = -half + 0.4f + i * (size - 0.8f) / 3f;
                    Vector3 local = new Vector3(t, baseHeight + 0.03f, side * half);
                    CreateDecorRotated(parent, $"Skatepark_HazardTrim_{side}_{i}", rot * local + center,
                        new Vector3(0.42f, 0.08f, 0.16f), (i % 2 == 0) ? mustardMat : darkMat, rot);
                }
            }
        }

        // =====================================================================================
        // 侧翼坡道与长台
        // =====================================================================================

        private static void BuildFlankFeatures(
            Transform parent,
            Material concreteMat,
            Material concreteDarkMat,
            Material accentMat,
            Material copingMat,
            Material steelDarkMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            // 1. quarter-pipe：靠边界墙一侧高、朝场地中心低（原点 180° 点对称）。
            // 旧版是一块 4.2x0.26 的平板、滚角只有 14°，高边还朝场地中心：读作「倒在地上的红板」。
            // 改成 4 段递进坡面（0 / 0.14 / 0.38 / 0.72 / 1.15m，3.6m 跑长），轮廓接近真正的 bank，
            // 且高边贴墙、低边朝场地，玩家可以顺着跑到 1.15m 台面。
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.QuarterPipePos;
                float zc = p.z * sign;
                float zSize = ArenaLayoutSkatepark.QuarterPipeSize.z;
                string tag = mirror == 0 ? "E" : "W";

                const float runStart = 16.2f;
                const float runStep = 0.9f;
                float[] rise = { 0.0f, 0.14f, 0.38f, 0.72f, 1.15f };

                for (int j = 0; j < 4; j++)
                {
                    float y0 = rise[j];
                    float y1 = rise[j + 1];
                    float dy = y1 - y0;
                    float theta = Mathf.Atan2(dy, runStep);
                    float slopeLen = Mathf.Sqrt(runStep * runStep + dy * dy);
                    float cx = sign * (runStart + (j + 0.5f) * runStep);
                    // 薄板顶面 = 目标坡面：中心沿法线回退半厚度（竖直分量 0.11/cosθ）。
                    float cy = (y0 + y1) * 0.5f - 0.11f / Mathf.Max(0.2f, Mathf.Cos(theta));
                    Quaternion rot = Quaternion.Euler(0f, 0f, sign * theta * Mathf.Rad2Deg);

                    var strip = ArenaGeometryBuilder.CreateBox(parent, $"Skatepark_QuarterPipe_{tag}_Strip{j}",
                        new Vector3(cx, cy, zc), new Vector3(slopeLen + 0.04f, 0.22f, zSize), concreteMat, rot);
                    if (inkableRenderers != null) inkableRenderers.Add(strip.GetComponent<Renderer>());
                    ArenaGeometryBuilder.AddOrientedInkWallSurface(strip, inkCfg, inkWallMat, false);
                }

                // 坡面下的阶梯填充（无碰撞：薄板已经提供可站立碰撞，填充只负责从侧面读成实心 bank）
                for (int fill = 0; fill < 3; fill++)
                {
                    float fillX = sign * (runStart + 1.05f + fill * 0.92f);
                    float fillH = 0.32f + fill * 0.34f;
                    CreateDecorRotated(parent, "Skatepark_QuarterPipe_Fill_" + tag + "_" + fill,
                        new Vector3(fillX, fillH * 0.5f, zc),
                        new Vector3(1.15f, fillH, zSize - 0.04f), concreteDarkMat, Quaternion.identity);
                }

                // 坡顶 coping（视觉黄条，不挡人；位置贴着最后一段坡面顶边）
                float topX = sign * (runStart + 4f * runStep);
                CreateDecorRotated(parent, "Skatepark_QuarterPipe_Coping_" + tag,
                    new Vector3(topX - sign * 0.04f, rise[4] + 0.03f, zc),
                    new Vector3(0.20f, 0.09f, zSize + 0.02f), copingMat, Quaternion.identity);

                // 坡面防滑条：沿 4 段坡面各铺一条细橙线（薄、无碰撞、贴坡旋转）
                for (int s = 0; s < 4; s++)
                {
                    float y0 = rise[s];
                    float y1 = rise[s + 1];
                    float theta = Mathf.Atan2(y1 - y0, runStep);
                    float cx = sign * (runStart + (s + 0.5f) * runStep);
                    float cy = (y0 + y1) * 0.5f + 0.02f;
                    CreateDecorRotated(parent, "Skatepark_QuarterPipe_Stripe_" + tag + "_" + s,
                        new Vector3(cx, cy, zc), new Vector3(runStep * 0.55f, 0.02f, zSize - 0.5f),
                        accentMat, Quaternion.Euler(0f, 0f, sign * theta * Mathf.Rad2Deg));
                }
            }

            // 2. 碗池两侧长台：0.55m 可跳矮掩体，台面顶墨 + 四面可涂。
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.LedgePos;
                Vector3 world = new Vector3(p.x * sign, ArenaLayoutSkatepark.LedgeSize.y * 0.5f, p.z * sign);
                var ledge = CreateSolidRotated(parent, "Skatepark_Ledge_" + (sign > 0 ? "E" : "W"),
                    world, ArenaLayoutSkatepark.LedgeSize, concreteMat,
                    Quaternion.Euler(0f, mirror == 0 ? 0f : 180f, 0f),
                    inkCfg, inkWallMat, inkableRenderers, blockedColliders, true);
                // 台面白边条（贴顶面，只做视觉；用池沿材质，避免整块台面被刷成木色）
                CreateDecorRotated(parent, "Skatepark_Ledge_Coping_" + (sign > 0 ? "E" : "W"),
                    new Vector3(world.x, ArenaLayoutSkatepark.LedgeSize.y + 0.02f, world.z),
                    new Vector3(ArenaLayoutSkatepark.LedgeSize.x + 0.08f, 0.04f, ArenaLayoutSkatepark.LedgeSize.z + 0.08f),
                    copingMat, Quaternion.identity);
            }

            // 3. 磨轨（grind rail）：碗池两侧各一组，纯装饰细杆。
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 baseCenter = new Vector3(10.3f * sign, 0f, 9.4f * sign);
                Quaternion rot = Quaternion.Euler(0f, 38f * sign, 0f);
                for (int post = -1; post <= 1; post += 2)
                {
                    Vector3 postLocal = rot * new Vector3(0f, 0f, post * 1.7f);
                    CreateDecorBox(parent, "Skatepark_RailPost_" + (sign > 0 ? "E" : "W") + (post > 0 ? "N" : "S"),
                        baseCenter + postLocal + Vector3.up * 0.21f,
                        new Vector3(0.07f, 0.42f, 0.07f), steelDarkMat);
                }
                CreateDecorRotated(parent, "Skatepark_GrindRail_" + (sign > 0 ? "E" : "W"),
                    baseCenter + Vector3.up * 0.44f, new Vector3(0.07f, 0.07f, 3.6f), accentMat, rot);
            }
        }

        // =====================================================================================
        // 出生区 / 边界墙
        // =====================================================================================

        private static void BuildSpawnArea(
            Transform parent,
            Material groundMat,
            Material spawnMatA,
            Material spawnMatB,
            Material boundaryMat,
            Material metalMat,
            Material steelDarkMat,
            Material containerBlueMat,
            Material containerGreenMat,
            Material containerRustMat,
            Material containerMustardMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders,
            out Transform teamASpawnAnchor)
        {
            float deckTop = ArenaLayoutV2.SpawnDeckTopY;
            float terraceZ = ArenaLayoutV2.SpawnTerraceZ;
            float deckWidth = ArenaLayoutV2.SpawnDeckWidth;
            float deckDepth = ArenaLayoutV2.SpawnDeckDepth;

            for (int side = -1; side <= 1; side += 2)
            {
                bool isTeamA = side < 0;
                string tag = isTeamA ? "TeamA" : "TeamB";
                Material teamColorMat = isTeamA ? (spawnMatA ?? containerRustMat) : (spawnMatB ?? containerBlueMat);
                Material padMat = isTeamA ? spawnMatA : spawnMatB;

                // 6.1 出生甲板基础
                var terrace = ArenaGeometryBuilder.CreateBox(parent, "SpawnTerrace_" + tag,
                    new Vector3(0f, deckTop * 0.5f, side * terraceZ),
                    new Vector3(deckWidth, deckTop, deckDepth), groundMat);
                if (inkableRenderers != null) inkableRenderers.Add(terrace.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddInkWallSurface(terrace, inkCfg, inkWallMat, true);

                // 6.2 甲板前沿收口与队伍识别色板
                float frontZ = side * (terraceZ + deckDepth * 0.5f - 0.06f);
                CreateDecorBox(parent, "Skatepark_SpawnDeck_Coping_" + tag,
                    new Vector3(0f, deckTop + 0.06f, frontZ), new Vector3(deckWidth + 0.2f, 0.12f, 0.34f), boundaryMat);

                // 前沿队伍强调色板（Team A 橙色，Team B 蓝色）
                CreateDecorBox(parent, "Skatepark_SpawnDeck_TeamBanner_" + tag,
                    new Vector3(0f, deckTop + 0.08f, frontZ - side * 0.02f),
                    new Vector3(deckWidth * 0.8f, 0.16f, 0.12f), teamColorMat);

                // 出生区队伍专属警示纹路（橙黑 / 蓝黑条纹）
                CreateSpawnHazardTrim(parent, new Vector3(0f, deckTop + 0.01f, frontZ),
                    deckWidth * 0.6f, teamColorMat, steelDarkMat);

                // 6.3 甲板围栏
                BuildSpawnRailing(parent, tag, side, metalMat);

                // 6.4 出生安全盘
                var pad = ArenaGeometryBuilder.CreateBox(parent, "SafeSpawnPad_" + tag,
                    new Vector3(0f, deckTop + ArenaLayoutV2.SpawnPadThickness * 0.5f, side * terraceZ),
                    new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), padMat);
                ArenaGeometryBuilder.AddInkWallSurface(pad, inkCfg, inkWallMat, true);

                // 6.5 甲板后方 3 个标准集装箱并排（2.44×2.59×6.06，间隔 0.12m，队伍 A 用 rust/mustard 交替，队伍 B 用 blue/green 交替）
                Vector3 boxSize = ArenaLayoutSkatepark.ContainerSize;
                float yaw = isTeamA ? 0f : 180f;
                // 中心 Z：让集装箱前脸紧贴甲板后边缘
                float centerZ = side * (terraceZ + deckDepth * 0.5f + boxSize.z * 0.5f - 0.40f);

                for (int c = -1; c <= 1; c++)
                {
                    float x = c * (boxSize.x + 0.12f);
                    Material cMat = isTeamA
                        ? (c == 0 ? containerMustardMat : containerRustMat)
                        : (c == 0 ? containerGreenMat : containerBlueMat);

                    Vector3 cCenter = new Vector3(x, deckTop + boxSize.y * 0.5f, centerZ);
                    var cBox = CreateSolidRotated(parent, $"Skatepark_SpawnContainer_{tag}_{c + 1}",
                        cCenter, boxSize, cMat,
                        Quaternion.Euler(0f, yaw, 0f),
                        inkCfg, inkWallMat, inkableRenderers, blockedColliders, true, true);
                    cBox.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
                    SkateparkDetailBuilder.DressContainer(parent, cCenter, yaw, boxSize, (isTeamA ? 500 : 600) + (c + 1) * 7);
                }

                // 6.6 出生台后方的岸台垫层（视觉/承托，不可达）：
                // 3 个背景集装箱跨 z≈28..34，已经越过 28.5 边界墙，旧版下面没有任何地面，
                // 从出生台回身看是一排「漂在水上的集装箱」，地图卡俯视图同样穿帮。
                // 垫层从 z=28.4 铺到 34.2、顶面与出生甲板同为 1.2m，整体藏在边界墙后。
                float apronDepth = 5.8f;
                float apronCenterZ = side * (ArenaLayoutV2.BoundaryZ + 0.5f + apronDepth * 0.5f);
                var apron = ArenaGeometryBuilder.CreateBox(parent, "SpawnBackApron_" + tag,
                    new Vector3(0f, deckTop * 0.5f, apronCenterZ),
                    new Vector3(16.4f, deckTop, apronDepth), groundMat);
                StripCollider(apron);

                // 南侧边界墙只有 0.55m：墙顶到集装箱底还差 0.65m，补一条前沿压台填平。
                if (isTeamA)
                {
                    var lip = ArenaGeometryBuilder.CreateBox(parent, "SpawnBackApron_Lip_" + tag,
                        new Vector3(0f, (0.55f + deckTop) * 0.5f, side * (ArenaLayoutV2.BoundaryZ + 0.05f)),
                        new Vector3(16.4f, deckTop - 0.55f, 1.3f), groundMat);
                    StripCollider(lip);
                }

                // 6.7 出生台背面防越界：南侧边界墙只有 0.55m，甲板顶 1.2m，玩家可以从背沿
                // 直接走到新岸台上（岸台又提供了落脚点）。在甲板背沿补一条不可见阻挡，
                // 避免躲进出生台与背景集装箱之间的死角。
                ArenaGeometryBuilder.CreateInvisibleBlocker(parent, "SpawnBackBlocker_" + tag,
                    new Vector3(0f, 1.5f, side * (ArenaLayoutV2.SpawnTerraceZ + ArenaLayoutV2.SpawnDeckDepth * 0.5f + 0.25f)),
                    new Vector3(ArenaLayoutV2.SpawnDeckWidth, 3f, 0.4f));

                // 6.8 两侧下行台阶
                CreateSpawnStair(parent, "Ramp_SpawnStair_" + (side < 0 ? "A" : "B") + "_East", 1f, side, groundMat, inkableRenderers);
                CreateSpawnStair(parent, "Ramp_SpawnStair_" + (side < 0 ? "A" : "B") + "_West", -1f, side, groundMat, inkableRenderers);
            }

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(parent, false);
            spawnAnchorGo.transform.position = new Vector3(0f, ArenaLayoutV2.SpawnPointY, -ArenaLayoutV2.SpawnAnchorZ);
            teamASpawnAnchor = spawnAnchorGo.transform;
        }

        private static void CreateSpawnHazardTrim(Transform parent, Vector3 center, float width, Material teamMat, Material darkMat)
        {
            int segments = 12;
            float step = width / segments;
            float startX = -width * 0.5f + step * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                CreateDecorBox(parent, $"Skatepark_SpawnHazard_{i}",
                    center + new Vector3(startX + i * step, 0.06f, 0f),
                    new Vector3(step * 0.85f, 0.04f, 0.16f),
                    (i % 2 == 0) ? teamMat : darkMat);
            }
        }

        private static void BuildSpawnRailing(Transform parent, string tag, float side, Material metalMat)
        {
            float deckTop = ArenaLayoutV2.SpawnDeckTopY;
            float terraceZ = ArenaLayoutV2.SpawnTerraceZ;
            float halfWidth = ArenaLayoutV2.SpawnDeckWidth * 0.5f;

            float inner = 2.4f;
            float outer = halfWidth - 0.2f;
            float segmentHalf = (outer - inner) * 0.5f;
            float segmentCenter = (outer + inner) * 0.5f;
            float frontZ = side * (terraceZ + ArenaLayoutV2.SpawnDeckDepth * 0.5f - 0.02f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                CreateDecorBox(parent, "Skatepark_SpawnRail_" + tag + "_" + (xSide < 0 ? "W" : "E"),
                    new Vector3(xSide * segmentCenter, deckTop + 0.55f, frontZ),
                    new Vector3(segmentHalf * 2f, 0.06f, 0.06f), metalMat);
                for (int i = 0; i < 4; i++)
                {
                    float x = xSide * (inner + (segmentHalf * 2f) * (i / 3f));
                    CreateDecorBox(parent, "Skatepark_SpawnRailPost_" + tag + "_" + (xSide < 0 ? "W" : "E") + "_" + i,
                        new Vector3(x, deckTop + 0.28f, frontZ), new Vector3(0.07f, 0.56f, 0.07f), metalMat);
                }
            }
        }

        private static void CreateSpawnStair(
            Transform parent,
            string name,
            float sideSign,
            float zSign,
            Material groundMat,
            List<Renderer> inkableRenderers)
        {
            float halfDeck = ArenaLayoutV2.SpawnDeckWidth * 0.5f;
            int steps = ArenaLayoutV2.SpawnStairSteps;
            float tread = ArenaLayoutV2.SpawnStairRun / steps;
            float rise = ArenaLayoutV2.SpawnDeckTopY / steps;
            float z = zSign * ArenaLayoutV2.SpawnTerraceZ;

            for (int i = 0; i < steps; i++)
            {
                float height = rise * (i + 1);
                float outer = halfDeck + ArenaLayoutV2.SpawnStairRun - i * tread;
                float inner = outer - tread;
                var step = ArenaGeometryBuilder.CreateBox(
                    parent,
                    name + "_Step_" + i,
                    new Vector3(sideSign * (inner + outer) * 0.5f, height * 0.5f, z),
                    new Vector3(tread, height, ArenaLayoutV2.SpawnStairWidth),
                    groundMat);
                if (inkableRenderers != null) inkableRenderers.Add(step.GetComponent<Renderer>());
            }
        }

        private static void BuildBoundaryWalls(
            Transform parent,
            Material boundaryMat,
            Material metalMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat)
        {
            // 南侧（Team A 方向）：0.55m 矮墙面向港湾水面，顶上加白栏杆；外侧仍有隐形防越界体。
            var southWall = ArenaGeometryBuilder.CreateBox(parent, "Boundary_South",
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f), boundaryMat);
            ArenaGeometryBuilder.AddOrientedInkWallSurface(southWall, inkCfg, inkWallMat, false,
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f));
            ArenaGeometryBuilder.CreateInvisibleBlocker(parent, "Boundary_South_Blocker",
                new Vector3(0f, 1.5f, -ArenaLayoutV2.BoundaryZ - 1f), new Vector3(42f, 3f, 1f));
            for (int i = 0; i < 21; i++)
            {
                float x = -20f + i * 2f;
                CreateDecorBox(parent, "Skatepark_SouthRailPost_" + i,
                    new Vector3(x, 0.87f, -ArenaLayoutV2.BoundaryZ), new Vector3(0.07f, 0.64f, 0.07f), metalMat);
            }
            CreateDecorBox(parent, "Skatepark_SouthRail",
                new Vector3(0f, 1.16f, -ArenaLayoutV2.BoundaryZ), new Vector3(40f, 0.06f, 0.06f), metalMat);

            var northWall = ArenaGeometryBuilder.CreateBox(parent, "Boundary_North",
                new Vector3(0f, ArenaLayoutV2.NorthBoundaryWallHeight * 0.5f, ArenaLayoutV2.BoundaryZ),
                new Vector3(42f, ArenaLayoutV2.NorthBoundaryWallHeight, 1f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(northWall, inkCfg, inkWallMat, false);

            var westWall = ArenaGeometryBuilder.CreateBox(parent, "Boundary_West",
                new Vector3(-ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(westWall, inkCfg, inkWallMat, false);

            var eastWall = ArenaGeometryBuilder.CreateBox(parent, "Boundary_East",
                new Vector3(ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(eastWall, inkCfg, inkWallMat, false);
        }

        // =====================================================================================
        // 工具
        // =====================================================================================

        private static GameObject CreateSolidRotated(
            Transform parent,
            string name,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            Quaternion rotation,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders,
            bool addTopInk,
            bool allowClimb = true)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat, rotation);
            if (addTopInk && inkableRenderers != null) inkableRenderers.Add(go.GetComponent<Renderer>());
            ArenaGeometryBuilder.AddOrientedInkWallSurface(go, inkCfg, inkWallMat, allowClimb);
            var col = go.GetComponent<Collider>();
            if (col != null && blockedColliders != null) blockedColliders.Add(col);
            return go;
        }

        private static GameObject CreateDecorBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat);
            StripCollider(go);
            return go;
        }

        private static GameObject CreateDecorRotated(
            Transform parent,
            string name,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            Quaternion rotation)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat, rotation);
            StripCollider(go);
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
#if UNITY_EDITOR
            Object.DestroyImmediate(col);
#else
            Object.Destroy(col);
#endif
        }
    }
}