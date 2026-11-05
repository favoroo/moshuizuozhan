using System.Collections.Generic;
using P1.Ink;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「海鸥港湾 / GULL HARBOR」几何构建器（复刻 INKWAVE `halyard` 渡轮码头）。
    ///
    /// 设计要点：
    /// 1. **玩法常量复用**：40×56 地面、边界墙、出生甲板/台阶/出生盘/出生线全部沿用
    ///    <see cref="ArenaLayoutV2"/>，涂地网格、覆盖率判据与 Bot NavGrid 口径不变；
    /// 2. **全新立体结构**：中央渡轮（甲板 1.5m，只有南北两条登船坡道可上）+ 南北浅湾水线
    ///    （可行走涉水、但涂不上墨的禁区）+ 东西 4.8m 木栈道绕后；
    /// 3. **侧翼高台**：每队左翼一艘搁墩拖船（甲板 1.15m，可跳上），取代旧图的东西侧翼平台；
    /// 4. **细节**：舷墙、舷窗、救生圈、系泊缆、桅杆、灯塔与南侧栏杆，全部是无碰撞装饰件，
    ///    不进入 NavGrid、不吃涂地计分。
    /// </summary>
    public static class HarborGeometryBuilder
    {
        public const string ArenaRootName = "Arena_GullHarbor";
        public const string LandmarkRootName = "Harbor_Landmarks";

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
            Material hullMat = mats.hull ?? mats.containerSlate ?? wallMat;
            Material cabinMat = mats.cabin ?? mats.concrete ?? wallMat;
            Material deckMat = mats.deck ?? mats.wood ?? wallMat;
            Material accentMat = mats.accent ?? mats.containerRust ?? wallMat;
            Material glassMat = mats.glass ?? mats.steelDark ?? wallMat;
            Material woodMat = mats.wood ?? deckMat;
            // 注意：mats.propMetal / propEmissive 是顶点色着色器（PropLit），
            // 直接给运行时 Cube 用会渲染成纯白（Cube 没有顶点色）——舷梯、栏杆全白就是这个坑。
            // 装饰盒一律用带真实 baseColor 的 Mat_Prop_Steel / SteelDark。
            Material metalMat = mats.steel ?? mats.propMetal ?? wallMat;
            Material emissiveMat = mats.propEmissive ?? mats.steel ?? wallMat;
            Material waterMat = mats.waterInner ?? mats.water;

            var arenaRoot = new GameObject(ArenaRootName);

            // 1. 主地面 40×56m（名字保持 Ground_Main_40x56：覆盖率白名单 / 地面 AO 烘焙按名字排除）
            var mainFloor = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Ground_Main_40x56",
                new Vector3(0f, -0.25f, 0f), new Vector3(ArenaLayoutV2.MapSizeX, 0.5f, ArenaLayoutV2.MapSizeZ), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(mainFloor.GetComponent<Renderer>());

            // 2. 浅湾水线（南北各一条，跨过登船坡道）
            BuildWaterBasin(arenaRoot.transform, waterMat, boundaryMat, mats.cabin ?? boundaryMat);
            // 2.1 木栈道沿水线的码头细节（救生圈/系缆桩/路灯）：纯装饰，不进碰撞与计分
            BuildBoardwalkDressing(arenaRoot.transform, metalMat, accentMat,
                mats.containerRust ?? accentMat, mats.containerTeal ?? accentMat, emissiveMat);

            // 3. 中央渡轮（彩色集装箱材质单独传入：甲板货柜/围裙/腰线要真彩色）
            BuildFerry(arenaRoot.transform, hullMat, cabinMat, deckMat, accentMat, glassMat, metalMat, emissiveMat,
                mats.containerTeal ?? accentMat, mats.containerRust ?? accentMat, mats.containerMustard ?? accentMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 4. 船坞：搁墩拖船（每队左翼，甲板可跳上）
            BuildTugPair(arenaRoot.transform, hullMat, cabinMat, accentMat, glassMat, metalMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 5. 系泊游艇（船头探向水面，低矮掩体）
            BuildHouseboatPair(arenaRoot.transform, cabinMat, deckMat, accentMat, glassMat, metalMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders);

            // 6. 加油亭：正对登船坡道的中路掩体
            AddCoverPair(arenaRoot.transform, "Harbor_FuelHut", ArenaLayoutHarbor.FuelHutPos, ArenaLayoutHarbor.FuelHutSize,
                cabinMat, inkCfg, inkWallMat, blockedColliders);
            BuildFuelHutDetails(arenaRoot.transform, cabinMat, accentMat, metalMat, emissiveMat, glassMat,
                mats.containerTeal ?? accentMat, mats.containerRust ?? accentMat, mats.containerMustard ?? accentMat);

            // 7. 出生闸口两侧储物柜（沿用旧图坐标，保证出生出口节奏）
            AddCoverPair(arenaRoot.transform, "SpawnGate_Wing", ArenaLayoutHarbor.SpawnGateWingPos,
                ArenaLayoutHarbor.SpawnGateWingSize, cabinMat, inkCfg, inkWallMat, blockedColliders);

            // 8. 出生甲板 + 两侧台阶 + 出生盘
            BuildSpawnArea(arenaRoot.transform, groundMat, spawnMatA, spawnMatB, boundaryMat, metalMat,
                inkCfg, inkWallMat, inkableRenderers, out teamASpawnAnchor);

            // 9. 四周边界墙：南侧矮墙 + 白栏杆（面向港湾水面），北/东西为常规高墙
            BuildBoundaryWalls(arenaRoot.transform, boundaryMat, metalMat, inkCfg, inkWallMat);

            // 10. 覆盖率兜底：给漏装墨面的可视碰撞盒补全
            ArenaGeometryBuilder.EnsurePaintableArena(arenaRoot, inkCfg, inkWallMat);

            // 11. 场地地标（不参与碰撞、不吃计分）
            BuildLandmarks(arenaRoot.transform, boundaryMat, accentMat, emissiveMat);

            return arenaRoot;
        }

        // =====================================================================================
        // 浅湾水线
        // =====================================================================================

        private static void BuildWaterBasin(Transform parent, Material waterMat, Material edgeMat, Material foamMat)
        {
            float width = ArenaLayoutHarbor.WaterHalfX * 2f;
            float depth = ArenaLayoutHarbor.WaterOuterZ - ArenaLayoutHarbor.WaterInnerZ;
            float centerZ = (ArenaLayoutHarbor.WaterOuterZ + ArenaLayoutHarbor.WaterInnerZ) * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                // 水面：0.02m 薄盒，去掉碰撞体 → 角色可以涉水通过，但涂地网格已把这一片标成禁区。
                var water = ArenaGeometryBuilder.CreateBox(parent, "Harbor_WaterLine_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, ArenaLayoutHarbor.WaterSurfaceY, side * centerZ),
                    new Vector3(width, 0.02f, depth), waterMat);
                StripCollider(water);

                // 水线外缘压边（码头沿口）：留出登船坡道口 x∈[-1.9,1.9]。
                float outerZ = side * ArenaLayoutHarbor.WaterOuterZ;
                float segmentHalf = (ArenaLayoutHarbor.WaterHalfX - ArenaLayoutHarbor.RampHalfWidth) * 0.5f;
                float segmentCenter = (ArenaLayoutHarbor.WaterHalfX + ArenaLayoutHarbor.RampHalfWidth) * 0.5f;
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateDecorBox(parent, "Harbor_WaterCurb_" + (side < 0 ? "A" : "B") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * segmentCenter, ArenaLayoutHarbor.WaterCurbHeight * 0.5f, outerZ),
                        new Vector3(segmentHalf * 2f, ArenaLayoutHarbor.WaterCurbHeight, 0.34f), edgeMat);
                }

                // 水线两端封头
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateDecorBox(parent, "Harbor_WaterEnd_" + (side < 0 ? "A" : "B") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * ArenaLayoutHarbor.WaterHalfX, ArenaLayoutHarbor.WaterCurbHeight * 0.5f, side * centerZ),
                        new Vector3(0.34f, ArenaLayoutHarbor.WaterCurbHeight, depth), edgeMat);
                }


                // 漂浮泊标：纯装饰，压出"这是水"的读法。
                for (int i = 0; i < 4; i++)
                {
                    float x = (i - 1.5f) * 6.2f;
                    CreateDecorBox(parent, "Harbor_Buoy_" + (side < 0 ? "A" : "B") + "_" + i,
                        new Vector3(x, ArenaLayoutHarbor.WaterSurfaceY + 0.09f, side * (centerZ + (i % 2 == 0 ? 0.8f : -0.9f))),
                        new Vector3(0.28f, 0.18f, 0.28f), edgeMat);
                }
            }
        }

        /// <summary>
        /// 木栈道沿水线的码头细节：红白救生圈、系缆桩、路灯。
        /// 全部是无碰撞装饰盒，不参与涂地计分，也不会挡子弹/Bot 寻路。
        /// </summary>
        private static void BuildBoardwalkDressing(Transform parent, Material metalMat, Material creamMat,
            Material rustMat, Material tealMat, Material emissiveMat)
        {
            for (int zSide = -1; zSide <= 1; zSide += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    // 救生圈立柱（面向水面）
                    for (int i = 0; i < 2; i++)
                    {
                        float x = xSide * (6.5f + i * 5.0f);
                        float z = zSide * 10.65f;
                        CreateDecorBox(parent, "Harbor_LifeRingPost_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 0.62f, z), new Vector3(0.09f, 1.24f, 0.09f), metalMat);
                        CreateDecorBox(parent, "Harbor_LifeRing_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 1.28f, z), new Vector3(0.58f, 0.58f, 0.09f), rustMat);
                        CreateDecorBox(parent, "Harbor_LifeRingCore_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 1.28f, z + zSide * 0.01f), new Vector3(0.26f, 0.26f, 0.1f), creamMat);
                    }

                    // 系缆桩（深色铁桩 + 奶油顶）
                    for (int i = 0; i < 2; i++)
                    {
                        float x = xSide * (9.4f + i * 4.2f);
                        float z = zSide * 10.30f;
                        CreateDecorBox(parent, "Harbor_Mooring_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 0.3f, z), new Vector3(0.3f, 0.6f, 0.3f), metalMat);
                        CreateDecorBox(parent, "Harbor_MooringCap_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 0.63f, z), new Vector3(0.36f, 0.08f, 0.36f), creamMat);
                    }

                    // 栈道路灯：4m 灯杆 + 朝向车道中心的横臂 + 暖光灯罩
                    for (int i = 0; i < 2; i++)
                    {
                        float x = xSide * 19.1f;
                        float z = zSide * (7.5f + i * 10.5f);
                        CreateDecorBox(parent, "Harbor_LaneLampPole_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x, 2.0f, z), new Vector3(0.14f, 4.0f, 0.14f), metalMat);
                        CreateDecorBox(parent, "Harbor_LaneLampArm_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x - xSide * 0.55f, 3.95f, z), new Vector3(1.1f, 0.08f, 0.08f), metalMat);
                        CreateDecorBox(parent, "Harbor_LaneLampHead_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(x - xSide * 1.05f, 3.86f, z), new Vector3(0.46f, 0.22f, 0.46f), emissiveMat);
                    }

                    // 靠墙的防撞胎（黑色块）与缆绳盘（青色）
                    for (int i = 0; i < 3; i++)
                    {
                        CreateDecorBox(parent, "Harbor_WallFender_" + zSide + "_" + xSide + "_" + i,
                            new Vector3(xSide * 19.85f, 0.5f, zSide * (2f + i * 7f)),
                            new Vector3(0.2f, 0.55f, 0.7f), metalMat);
                    }
                    CreateDecorBox(parent, "Harbor_RopeCoil_" + zSide + "_" + xSide,
                        new Vector3(xSide * 18.2f, 0.12f, zSide * 14.2f),
                        new Vector3(0.9f, 0.18f, 0.9f), tealMat);
                }
            }
        }

        // =====================================================================================
        // 渡轮
        // =====================================================================================

        private static void BuildFerry(
            Transform parent,
            Material hullMat,
            Material cabinMat,
            Material deckMat,
            Material accentMat,
            Material glassMat,
            Material metalMat,
            Material emissiveMat,
            Material containerTealMat,
            Material containerRustMat,
            Material containerMustardMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            float halfX = ArenaLayoutHarbor.FerryHalfX;
            float halfZ = ArenaLayoutHarbor.FerryHalfZ;
            float hullTop = ArenaLayoutHarbor.FerryHullTopY;
            float deckTop = ArenaLayoutHarbor.FerryDeckTopY;

            // 3.1 船壳：23.6×9.2，深蓝。顶面被甲板板盖住，四侧立面可涂墨。
            var hull = ArenaGeometryBuilder.CreateBox(parent, "Ferry_Hull",
                new Vector3(0f, hullTop * 0.5f, 0f), new Vector3(halfX * 2f, hullTop, halfZ * 2f), hullMat);
            ArenaGeometryBuilder.AddInkWallSurface(hull, inkCfg, inkWallMat);

            // 3.2 甲板：内收 0.1m 露出舷缘，顶面走顶视墨迹图集。
            float deckHalfX = halfX - ArenaLayoutHarbor.FerryDeckInset;
            float deckHalfZ = halfZ - ArenaLayoutHarbor.FerryDeckInset;
            var deck = ArenaGeometryBuilder.CreateBox(parent, "Ferry_CarDeck",
                new Vector3(0f, hullTop + ArenaLayoutHarbor.FerryDeckThickness * 0.5f, 0f),
                new Vector3(deckHalfX * 2f, ArenaLayoutHarbor.FerryDeckThickness, deckHalfZ * 2f), deckMat);
            if (inkableRenderers != null) inkableRenderers.Add(deck.GetComponent<Renderer>());
            ArenaGeometryBuilder.AddInkWallSurface(deck, inkCfg, inkWallMat);

            // 3.2.1 船艏/船艉收尖：三段递减宽度的钢甲板台阶，把方盒子船体收成尖头。
            //       每一段都可站立、可涂墨（顶面走图集），也是绕舱路线的一部分。
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                string bowTag = xSide > 0 ? "Bow" : "Stern";
                float[][] steps =
                {
                    new[] { halfX + 0.35f, 3.45f },
                    new[] { halfX + 0.90f, 2.15f },
                    new[] { halfX + 1.30f, 1.05f },
                };
                float prevEdge = halfX;
                for (int i = 0; i < steps.Length; i++)
                {
                    float edge = steps[i][0];
                    float center = (prevEdge + edge) * 0.5f;
                    var tip = ArenaGeometryBuilder.CreateBox(parent,
                        "Ferry_" + bowTag + "_Step_" + i,
                        new Vector3(xSide * center, deckTop * 0.5f, 0f),
                        new Vector3(edge - prevEdge, deckTop, steps[i][1] * 2f), deckMat);
                    if (inkableRenderers != null) inkableRenderers.Add(tip.GetComponent<Renderer>());
                    ArenaGeometryBuilder.AddInkWallSurface(tip, inkCfg, inkWallMat);
                    // 尖端白漆干舷条：俯视/平视都能一眼读出船艏轮廓
                    CreateDecorBox(parent, "Ferry_" + bowTag + "_Rail_" + i,
                        new Vector3(xSide * edge, deckTop + 0.05f, 0f),
                        new Vector3(0.22f, 0.1f, steps[i][1] * 2f + 0.1f), accentMat);
                    prevEdge = edge;
                }
            }

            // 3.3 舷缘装饰：白色干舷线 + 珊瑚色水线带 + 舷窗
            AddHullBand(parent, "Ferry_Band_Sheer", cabinMat, deckTop - 0.16f, halfX + 0.015f, halfZ + 0.015f, 0.09f);
            AddHullBand(parent, "Ferry_Band_Boot", containerTealMat, 0.22f, halfX + 0.012f, halfZ + 0.012f, 0.12f);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    float x = -8.4f + i * 4.2f;
                    CreateDecorBox(parent, "Ferry_Porthole_" + (side < 0 ? "S" : "N") + "_" + i,
                        new Vector3(x, 0.95f, side * (halfZ + 0.02f)), new Vector3(0.5f, 0.34f, 0.06f), glassMat);
                }

                // 舷侧轮胎护舷：深色橡胶块，靠岸停泊的读形
                for (int i = 0; i < 7; i++)
                {
                    CreateDecorBox(parent, "Ferry_Fender_" + (side < 0 ? "S" : "N") + "_" + i,
                        new Vector3(-9f + i * 3f, 0.52f, side * (halfZ + 0.09f)),
                        new Vector3(0.46f, 0.5f, 0.18f), glassMat);
                }

                // 船名板：奶油白底 + 青色下划线（参考图船艏的 KRAKEN LINES 白字横牌）
                CreateDecorBox(parent, "Ferry_NameBoard_" + (side < 0 ? "S" : "N"),
                    new Vector3(8.4f, 1.12f, side * (halfZ + 0.045f)),
                    new Vector3(2.9f, 0.44f, 0.05f), accentMat);
                CreateDecorBox(parent, "Ferry_NameBoardLine_" + (side < 0 ? "S" : "N"),
                    new Vector3(8.4f, 0.86f, side * (halfZ + 0.05f)),
                    new Vector3(2.9f, 0.08f, 0.05f), containerTealMat);
            }

            // 3.4 舷墙：甲板两侧 0.72m 可跳矮墙，中间留 4m 登船口（坡道正对这里）。
            float gap = ArenaLayoutHarbor.BulwarkGapHalfX;
            float segmentLength = halfX - gap;
            float segmentCenter = gap + segmentLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateSolidBox(parent,
                        "Ferry_Bulwark_" + (side < 0 ? "S" : "N") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * segmentCenter, deckTop + ArenaLayoutHarbor.BulwarkHeight * 0.5f,
                            side * ArenaLayoutHarbor.BulwarkCenterZ),
                        new Vector3(segmentLength, ArenaLayoutHarbor.BulwarkHeight, ArenaLayoutHarbor.BulwarkThickness),
                        hullMat, inkCfg, inkWallMat, blockedColliders, true);
                }
            }

            // 3.4.1 舷墙白色压顶条：把 0.72m 深蓝矮墙的顶边勾成一条白线，俯视角下才读得出"船舷"
            for (int side = -1; side <= 1; side += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateDecorBox(parent,
                        "Ferry_BulwarkRail_" + (side < 0 ? "S" : "N") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * segmentCenter,
                            deckTop + ArenaLayoutHarbor.BulwarkHeight + 0.04f,
                            side * ArenaLayoutHarbor.BulwarkCenterZ),
                        new Vector3(segmentLength, 0.08f, ArenaLayoutHarbor.BulwarkThickness + 0.08f),
                        cabinMat);
                }
            }


            // 3.4.3 船首/船尾各一个彩色集装箱：既是可用掩体，也是"车渡甲板在运货"的读形
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                Material boxMat = xSide > 0 ? containerTealMat : containerRustMat;
                CreateSolidBox(parent, "Ferry_DeckContainer_" + (xSide < 0 ? "W" : "E"),
                    new Vector3(xSide * 10.55f, deckTop + 1.22f, 0f),
                    new Vector3(1.9f, 2.44f, 4.6f), boxMat, inkCfg, inkWallMat, blockedColliders, true);
                CreateDecorBox(parent, "Ferry_DeckContainerTop_" + (xSide < 0 ? "W" : "E"),
                    new Vector3(xSide * 10.55f, deckTop + 2.46f, 0f),
                    new Vector3(1.96f, 0.08f, 4.66f), cabinMat);
            }

            // 登船口两侧的系缆桩
            for (int side = -1; side <= 1; side += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateDecorBox(parent, "Ferry_Cleat_" + (side < 0 ? "S" : "N") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * (gap + 0.45f), deckTop + 0.16f, side * (halfZ - 0.35f)),
                        new Vector3(0.18f, 0.32f, 0.18f), metalMat);
                }
            }

            // 3.5 客舱：10.4×4.6，2.1m 高；四面可涂，是甲板上唯一的硬掩体。
            CreateSolidBox(parent, "Ferry_Cabin",
                new Vector3(0f, deckTop + (ArenaLayoutHarbor.CabinTopY - deckTop) * 0.5f, 0f),
                new Vector3(ArenaLayoutHarbor.CabinHalfX * 2f, ArenaLayoutHarbor.CabinTopY - deckTop,
                    ArenaLayoutHarbor.CabinHalfZ * 2f),
                cabinMat, inkCfg, inkWallMat, blockedColliders, true);

            // 客舱顶珊瑚色压顶：只做四边压条（整块盖板会把客舱顶刷成一整片深红，读作"红屋顶"）
            float corniceX = ArenaLayoutHarbor.CabinHalfX * 2f + 0.2f;
            float corniceZ = ArenaLayoutHarbor.CabinHalfZ * 2f + 0.2f;
            CreateDecorBox(parent, "Ferry_Cabin_Cornice_S",
                new Vector3(0f, ArenaLayoutHarbor.CabinTopY + 0.06f, -corniceZ * 0.5f),
                new Vector3(corniceX, 0.12f, 0.18f), accentMat);
            CreateDecorBox(parent, "Ferry_Cabin_Cornice_N",
                new Vector3(0f, ArenaLayoutHarbor.CabinTopY + 0.06f, corniceZ * 0.5f),
                new Vector3(corniceX, 0.12f, 0.18f), accentMat);
            CreateDecorBox(parent, "Ferry_Cabin_Cornice_W",
                new Vector3(-corniceX * 0.5f, ArenaLayoutHarbor.CabinTopY + 0.06f, 0f),
                new Vector3(0.18f, 0.12f, corniceZ), accentMat);
            CreateDecorBox(parent, "Ferry_Cabin_Cornice_E",
                new Vector3(corniceX * 0.5f, ArenaLayoutHarbor.CabinTopY + 0.06f, 0f),
                new Vector3(0.18f, 0.12f, corniceZ), accentMat);
            // 客舱顶中央浅色检修口，避免大面积顶面空成一块纯色
            CreateDecorBox(parent, "Ferry_Cabin_RoofHatch",
                new Vector3(0f, ArenaLayoutHarbor.CabinTopY + 0.04f, 0f), new Vector3(1.6f, 0.08f, 1.2f), metalMat);
            for (int side = -1; side <= 1; side += 2)
            {
                string sideTag = side < 0 ? "S" : "N";
                float faceZ = side * (ArenaLayoutHarbor.CabinHalfZ + 0.03f);

                // 连续深色窗带 + 奶油色竖梃：比 4 个孤立小窗更像客舱
                CreateDecorBox(parent, "Ferry_CabinWindowBand_" + sideTag,
                    new Vector3(0f, deckTop + 1.28f, faceZ), new Vector3(10.1f, 0.62f, 0.06f), glassMat);
                for (int i = 0; i < 5; i++)
                {
                    CreateDecorBox(parent, "Ferry_CabinMullion_" + sideTag + "_" + i,
                        new Vector3(-5.0f + i * 2.5f, deckTop + 1.28f, faceZ + side * 0.015f),
                        new Vector3(0.12f, 0.62f, 0.05f), cabinMat);
                }
                // 青色腰线：参考图客舱下沿的彩色围裙
                CreateDecorBox(parent, "Ferry_CabinWaist_" + sideTag,
                    new Vector3(0f, deckTop + 0.34f, faceZ), new Vector3(10.2f, 0.16f, 0.06f), containerTealMat);

                // 舱门 + 门斗雨棚
                CreateDecorBox(parent, "Ferry_CabinDoor_" + sideTag,
                    new Vector3(0f, deckTop + 0.62f, faceZ), new Vector3(0.82f, 1.24f, 0.06f), containerTealMat);
                CreateDecorBox(parent, "Ferry_CabinDoorAwning_" + sideTag,
                    new Vector3(0f, deckTop + 1.86f, faceZ + side * 0.22f),
                    new Vector3(1.9f, 0.1f, 0.6f), containerRustMat);
            }

            // 3.6 驾驶室 + 桅杆（地标剪影）
            CreateSolidBox(parent, "Ferry_Wheelhouse",
                new Vector3(0f, ArenaLayoutHarbor.CabinTopY + (ArenaLayoutHarbor.WheelhouseTopY - ArenaLayoutHarbor.CabinTopY) * 0.5f, 0f),
                new Vector3(2.1f, ArenaLayoutHarbor.WheelhouseTopY - ArenaLayoutHarbor.CabinTopY, 2.1f),
                cabinMat, inkCfg, inkWallMat, blockedColliders, true);
            CreateDecorBox(parent, "Ferry_Wheelhouse_Glass",
                new Vector3(0f, ArenaLayoutHarbor.CabinTopY + 0.55f, 0f), new Vector3(2.2f, 0.5f, 2.2f), glassMat);
            CreateDecorBox(parent, "Ferry_Wheelhouse_Roof",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 0.05f, 0f), new Vector3(2.4f, 0.1f, 2.4f), accentMat);

            // 3.6.1 烟囱（船艉侧）：海军蓝筒身 + 奶油色箍 + 青色细箍 + 深色排气口
            float funnelTop = ArenaLayoutHarbor.CabinTopY + 1.95f;
            CreateDecorBox(parent, "Ferry_Funnel",
                new Vector3(-3.1f, ArenaLayoutHarbor.CabinTopY + 0.95f, 0f),
                new Vector3(1.6f, 1.9f, 1.25f), hullMat);
            CreateDecorBox(parent, "Ferry_FunnelBandCream",
                new Vector3(-3.1f, funnelTop - 0.18f, 0f), new Vector3(1.66f, 0.2f, 1.31f), accentMat);
            CreateDecorBox(parent, "Ferry_FunnelBandTeal",
                new Vector3(-3.1f, funnelTop - 0.46f, 0f), new Vector3(1.64f, 0.12f, 1.29f), containerTealMat);
            CreateDecorBox(parent, "Ferry_FunnelExhaust",
                new Vector3(-3.1f, funnelTop + 0.02f, 0f), new Vector3(1.0f, 0.06f, 0.72f), glassMat);

            // 3.6.2 驾驶台两翼：从驾驶室向两舷伸出的小平台，是"船"最强的剪影特征
            for (int side = -1; side <= 1; side += 2)
            {
                string wingTag = side < 0 ? "S" : "N";
                float wingZ = side * 2.62f;
                CreateDecorBox(parent, "Ferry_BridgeWing_" + wingTag,
                    new Vector3(0f, 4.02f, wingZ), new Vector3(1.55f, 0.16f, 2.1f), cabinMat);
                CreateDecorBox(parent, "Ferry_BridgeWingRail_" + wingTag,
                    new Vector3(0f, 4.42f, side * 3.6f), new Vector3(1.55f, 0.06f, 0.06f), metalMat);
                for (int pst = 0; pst < 3; pst++)
                {
                    CreateDecorBox(parent, "Ferry_BridgeWingPost_" + wingTag + "_" + pst,
                        new Vector3(-0.6f + pst * 0.6f, 4.22f, side * 3.6f), new Vector3(0.06f, 0.4f, 0.06f), metalMat);
                }
                CreateDecorBox(parent, "Ferry_BridgeWingStrut_" + wingTag,
                    new Vector3(0f, 3.82f, side * 3.35f), new Vector3(0.14f, 0.44f, 0.14f), metalMat);
            }

            // 3.6.3 雷达桅：两根交叉旋转的扫描条
            CreateDecorBox(parent, "Ferry_RadarPedestal",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 0.25f, 0f), new Vector3(0.2f, 0.5f, 0.2f), metalMat);
            var radarBar = CreateDecorBox(parent, "Ferry_RadarBar",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 0.56f, 0f), new Vector3(1.7f, 0.07f, 0.16f), accentMat);
            radarBar.transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
            var radarBar2 = CreateDecorBox(parent, "Ferry_RadarBar2",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 0.68f, 0f), new Vector3(0.95f, 0.06f, 0.13f), metalMat);
            radarBar2.transform.localRotation = Quaternion.Euler(0f, -32f, 0f);

            // 3.6.4 客舱顶护栏：一圈细栏杆，把"平屋顶"读成露天甲板
            for (int side = -1; side <= 1; side += 2)
            {
                string railTag = side < 0 ? "S" : "N";
                float railZ = side * (ArenaLayoutHarbor.CabinHalfZ - 0.08f);
                CreateDecorBox(parent, "Ferry_RoofRail_" + railTag,
                    new Vector3(0f, ArenaLayoutHarbor.CabinTopY + 0.52f, railZ),
                    new Vector3(ArenaLayoutHarbor.CabinHalfX * 2f - 0.2f, 0.06f, 0.06f), accentMat);
                for (int pst = 0; pst < 7; pst++)
                {
                    CreateDecorBox(parent, "Ferry_RoofRailPost_" + railTag + "_" + pst,
                        new Vector3(-4.9f + pst * 1.63f, ArenaLayoutHarbor.CabinTopY + 0.26f, railZ),
                        new Vector3(0.06f, 0.52f, 0.06f), metalMat);
                }
            }

            CreateDecorBox(parent, "Ferry_Mast",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 1.35f, 0f), new Vector3(0.12f, 2.6f, 0.12f), metalMat);
            CreateDecorBox(parent, "Ferry_Mast_Yard",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 1.9f, 0f), new Vector3(1.8f, 0.08f, 0.08f), metalMat);
            CreateDecorBox(parent, "Ferry_Mast_Lamp",
                new Vector3(0f, ArenaLayoutHarbor.WheelhouseTopY + 2.72f, 0f), new Vector3(0.26f, 0.26f, 0.26f), emissiveMat);

            // 3.7 救生圈（舷墙外侧珊瑚色圆盘读形）
            for (int side = -1; side <= 1; side += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    CreateDecorBox(parent, "Ferry_LifeRing_" + (side < 0 ? "S" : "N") + "_" + (xSide < 0 ? "W" : "E"),
                        new Vector3(xSide * 4.6f, deckTop + 0.42f, side * (ArenaLayoutHarbor.BulwarkCenterZ + 0.2f)),
                        new Vector3(0.5f, 0.5f, 0.1f), accentMat);
                }
            }

            // 3.8 南北登船坡道：跨过水线接上甲板；坡道顶面受墨，两侧栏杆无碰撞。
            for (int side = -1; side <= 1; side += 2)
            {
                string tag = side < 0 ? "A" : "B";
                float angle = side < 0 ? -ArenaLayoutHarbor.RampAngle : ArenaLayoutHarbor.RampAngle;
                float slopeLength = Mathf.Sqrt(ArenaLayoutHarbor.RampRunZ * ArenaLayoutHarbor.RampRunZ +
                                               ArenaLayoutHarbor.FerryDeckTopY * ArenaLayoutHarbor.FerryDeckTopY);
                // 钢质蹬船板：坡道面用蓝灰钢甲板材质（deckMat），不再是木纹"地毯"
                var ramp = ArenaGeometryBuilder.CreateBox(parent, "Ferry_Gangway_" + tag,
                    new Vector3(0f, ArenaLayoutHarbor.RampCenterY, side * ArenaLayoutHarbor.RampCenterZ),
                    new Vector3(ArenaLayoutHarbor.RampHalfWidth * 2f, 0.25f, slopeLength), deckMat);
                ramp.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
                if (inkableRenderers != null) inkableRenderers.Add(ramp.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddOrientedInkWallSurface(ramp, inkCfg, inkWallMat);

                // 坡道扶手立柱：稳固立在坡道两侧，离地 0.9m
                for (int railSide = -1; railSide <= 1; railSide += 2)
                {
                    string railTag = tag + "_" + (railSide < 0 ? "W" : "E");
                    float railX = railSide * (ArenaLayoutHarbor.RampHalfWidth - 0.1f);
                    Vector3 railBase = ramp.transform.TransformPoint(new Vector3(railX, 0.14f, 0f));
                    var rail = CreateDecorBox(parent, "Ferry_Gangway_Rail_" + railTag,
                        railBase + Vector3.up * 0.90f, new Vector3(0.07f, 0.07f, slopeLength), accentMat);
                    rail.transform.rotation = ramp.transform.rotation;
                    for (int pst = 0; pst < 3; pst++)
                    {
                        float pz = -slopeLength * 0.5f + 0.35f + pst * (slopeLength - 0.7f) * 0.5f;
                        Vector3 postPos = ramp.transform.TransformPoint(new Vector3(railX, 0.14f, pz));
                        CreateDecorBox(parent, "Ferry_Gangway_Post_" + railTag + "_" + pst,
                            postPos + Vector3.up * 0.45f, new Vector3(0.06f, 0.90f, 0.06f), metalMat);
                    }
                }
            }

            // 3.9 系泊缆：从船艏/艉拉向码头，细装饰
            for (int side = -1; side <= 1; side += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    Vector3 from = new Vector3(xSide * (ArenaLayoutHarbor.FerryHalfX - 0.4f), 0.28f,
                        side * (ArenaLayoutHarbor.FerryHalfZ + 0.1f));
                    Vector3 to = new Vector3(xSide * (ArenaLayoutHarbor.FerryHalfX + 1.8f), 0.2f,
                        side * (ArenaLayoutHarbor.WaterOuterZ + 0.3f));
                    CreateRope(parent, "Ferry_Mooring_" + (side < 0 ? "S" : "N") + "_" + (xSide < 0 ? "W" : "E"), from, to, accentMat);
                }
            }
        }

        // =====================================================================================
        // 船坞拖船 / 游艇
        // =====================================================================================

        private static void BuildTugPair(
            Transform parent,
            Material hullMat,
            Material cabinMat,
            Material accentMat,
            Material glassMat,
            Material metalMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 pos = new Vector3(ArenaLayoutHarbor.TugPos.x * sign, 0f, ArenaLayoutHarbor.TugPos.z * sign);
                string tag = mirror == 0 ? "A" : "B";
                float deckY = ArenaLayoutHarbor.TugSize.y;

                var hull = ArenaGeometryBuilder.CreateBox(parent, "Boatyard_Tug_" + tag,
                    new Vector3(pos.x, deckY * 0.5f, pos.z),
                    new Vector3(ArenaLayoutHarbor.TugSize.x, deckY, ArenaLayoutHarbor.TugSize.z), hullMat);
                if (inkableRenderers != null) inkableRenderers.Add(hull.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddInkWallSurface(hull, inkCfg, inkWallMat);

                // 甲板沿口 + 驾驶室
                CreateDecorBox(parent, "Boatyard_Tug_Deck_" + tag,
                    new Vector3(pos.x, deckY + 0.02f, pos.z),
                    new Vector3(ArenaLayoutHarbor.TugSize.x - 0.3f, 0.04f, ArenaLayoutHarbor.TugSize.z - 0.3f), cabinMat);
                CreateSolidBox(parent, "Boatyard_Tug_House_" + tag,
                    new Vector3(pos.x - sign * 0.6f, deckY + ArenaLayoutHarbor.TugHouseSize.y * 0.5f, pos.z - sign * 0.8f),
                    ArenaLayoutHarbor.TugHouseSize, cabinMat, inkCfg, inkWallMat, blockedColliders, true);
                CreateDecorBox(parent, "Boatyard_Tug_HouseGlass_" + tag,
                    new Vector3(pos.x - sign * 0.6f, deckY + 1.15f, pos.z - sign * 0.8f),
                    new Vector3(ArenaLayoutHarbor.TugHouseSize.x + 0.04f, 0.5f, ArenaLayoutHarbor.TugHouseSize.z + 0.04f), glassMat);
                CreateDecorBox(parent, "Boatyard_Tug_Funnel_" + tag,
                    new Vector3(pos.x + sign * 1.6f, deckY + 0.5f, pos.z + sign * 1.7f),
                    new Vector3(0.4f, 1.0f, 0.4f), accentMat);

                // 船头灯 + 护栏
                CreateDecorBox(parent, "Boatyard_Tug_Lamp_" + tag,
                    new Vector3(pos.x, deckY + 1.1f, pos.z + sign * 3.0f), new Vector3(0.2f, 0.2f, 0.2f), accentMat);
                CreateDecorBox(parent, "Boatyard_Tug_Rail_" + tag,
                    new Vector3(pos.x - sign * 1.6f, deckY + 0.35f, pos.z - sign * 2.6f),
                    new Vector3(0.08f, 0.06f, 1.6f), metalMat);

                // 墩木（船底垫块）
                for (int i = 0; i < 3; i++)
                {
                    CreateDecorBox(parent, "Boatyard_Tug_Block_" + tag + "_" + i,
                        new Vector3(pos.x - sign * 1.4f + i * sign * 1.4f, 0.16f, pos.z + sign * 2.5f),
                        new Vector3(0.5f, 0.32f, 0.5f), metalMat);
                }
            }
        }

        private static void BuildHouseboatPair(
            Transform parent,
            Material cabinMat,
            Material deckMat,
            Material accentMat,
            Material glassMat,
            Material metalMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders)
        {
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                Vector3 pos = new Vector3(ArenaLayoutHarbor.HouseboatPos.x * sign, 0f, ArenaLayoutHarbor.HouseboatPos.z * sign);
                string tag = mirror == 0 ? "A" : "B";
                float deckY = ArenaLayoutHarbor.HouseboatDeckY;

                var hull = ArenaGeometryBuilder.CreateBox(parent, "Harbor_Houseboat_" + tag,
                    new Vector3(pos.x, deckY * 0.5f, pos.z),
                    new Vector3(ArenaLayoutHarbor.HouseboatSize.x, deckY, ArenaLayoutHarbor.HouseboatSize.z), deckMat);
                if (inkableRenderers != null) inkableRenderers.Add(hull.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddInkWallSurface(hull, inkCfg, inkWallMat);

                CreateSolidBox(parent, "Harbor_Houseboat_Cabin_" + tag,
                    new Vector3(pos.x, deckY + ArenaLayoutHarbor.HouseboatCabinSize.y * 0.5f, pos.z + sign * 0.4f),
                    ArenaLayoutHarbor.HouseboatCabinSize, cabinMat, inkCfg, inkWallMat, blockedColliders, true);
                CreateDecorBox(parent, "Harbor_Houseboat_Window_" + tag,
                    new Vector3(pos.x, deckY + 1.3f, pos.z + sign * (0.4f + ArenaLayoutHarbor.HouseboatCabinSize.z * 0.5f + 0.03f)),
                    new Vector3(1.1f, 0.5f, 0.06f), glassMat);
                CreateDecorBox(parent, "Harbor_Houseboat_Bimini_" + tag,
                    new Vector3(pos.x, deckY + 2.35f, pos.z + sign * (0.4f + ArenaLayoutHarbor.HouseboatCabinSize.z * 0.5f + 0.25f)),
                    new Vector3(1.5f, 0.08f, 0.8f), accentMat);
                CreateDecorBox(parent, "Harbor_Houseboat_Bow_" + tag,
                    new Vector3(pos.x, deckY + 0.12f, pos.z - sign * (ArenaLayoutHarbor.HouseboatSize.z * 0.5f + 0.15f)),
                    new Vector3(1.4f, 0.24f, 0.3f), accentMat);
                CreateDecorBox(parent, "Harbor_Houseboat_Cleat_" + tag,
                    new Vector3(pos.x + sign * 0.9f, deckY + 0.12f, pos.z - sign * 1.6f),
                    new Vector3(0.14f, 0.24f, 0.14f), metalMat);
            }
        }

        // =====================================================================================
        // 加油亭细节
        // =====================================================================================

        private static void BuildFuelHutDetails(Transform parent, Material roofMat, Material accentMat, Material metalMat,
            Material emissiveMat, Material glassMat, Material containerTealMat, Material containerRustMat,
            Material containerMustardMat)
        {
            for (int mirror = 0; mirror < 2; mirror++)
            {
                float sign = mirror == 0 ? 1f : -1f;
                string tag = mirror == 0 ? "A" : "B";
                Vector3 pos = new Vector3(0f, 0f, ArenaLayoutHarbor.FuelHutPos.z * sign);

                // 顶棚：奶油色屋面 + 港青沿口（参考图白色小楼 + 青色屋檐）
                CreateDecorBox(parent, "Harbor_FuelHut_Canopy_" + tag,
                    new Vector3(pos.x, 2.78f, pos.z), new Vector3(3.5f, 0.12f, 3.5f), containerTealMat);
                CreateDecorBox(parent, "Harbor_FuelHut_CanopyTrim_" + tag,
                    new Vector3(pos.x, 2.90f, pos.z), new Vector3(3.7f, 0.08f, 3.7f), roofMat);
                // 加油机 + 黄色防撞柱
                CreateDecorBox(parent, "Harbor_FuelPump_" + tag,
                    new Vector3(pos.x + sign * 2.1f, 0.7f, pos.z), new Vector3(0.45f, 1.4f, 0.45f), metalMat);
                CreateDecorBox(parent, "Harbor_FuelPump_Lamp_" + tag,
                    new Vector3(pos.x + sign * 2.1f, 1.48f, pos.z), new Vector3(0.4f, 0.16f, 0.4f), emissiveMat);
                for (int guard = -1; guard <= 1; guard += 2)
                {
                    CreateDecorBox(parent, "Harbor_FuelGuard_" + tag + "_" + (guard < 0 ? "W" : "E"),
                        new Vector3(pos.x + sign * 2.1f + guard * 0.55f, 0.45f, pos.z + 0.55f),
                        new Vector3(0.12f, 0.9f, 0.12f), containerMustardMat);
                }
                // 雨棚：青白条纹，压在加油机上方（参考图加油站的条纹棚）
                for (int stripe = -1; stripe <= 1; stripe += 2)
                {
                    CreateDecorBox(parent, "Harbor_FuelAwning_" + tag + "_" + (stripe < 0 ? "W" : "E"),
                        new Vector3(pos.x + sign * 2.1f + stripe * 0.62f, 2.2f, pos.z),
                        new Vector3(1.2f, 0.08f, 2.6f), accentMat);
                }
                // 小屋门脸：青色门 + 两扇窗 + 招牌灯箱
                CreateDecorBox(parent, "Harbor_FuelHut_Door_" + tag,
                    new Vector3(pos.x + sign * 0.7f, 0.7f, pos.z + sign * 1.42f),
                    new Vector3(0.8f, 1.4f, 0.06f), containerTealMat);
                CreateDecorBox(parent, "Harbor_FuelHut_Window_" + tag,
                    new Vector3(pos.x - sign * 0.6f, 1.4f, pos.z + sign * 1.42f),
                    new Vector3(1.1f, 0.6f, 0.06f), glassMat);
                CreateDecorBox(parent, "Harbor_FuelHut_Sign_" + tag,
                    new Vector3(pos.x, 2.2f, pos.z + sign * 1.45f), new Vector3(1.9f, 0.42f, 0.08f), accentMat);
                CreateDecorBox(parent, "Harbor_FuelHut_SignTrim_" + tag,
                    new Vector3(pos.x, 2.2f, pos.z + sign * 1.46f), new Vector3(2.1f, 0.6f, 0.06f), containerTealMat);
                // 墙上救生圈：红白圆盘的读形（浮标）
                CreateDecorBox(parent, "Harbor_FuelHut_LifeRing_" + tag,
                    new Vector3(pos.x - sign * 1.0f, 1.9f, pos.z + sign * 1.44f),
                    new Vector3(0.55f, 0.55f, 0.08f), containerRustMat);
            }
        }

        // =====================================================================================
        // 出生区
        // =====================================================================================

        private static void BuildSpawnArea(
            Transform parent,
            Material groundMat,
            Material spawnMatA,
            Material spawnMatB,
            Material boundaryMat,
            Material metalMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            out Transform teamASpawnAnchor)
        {
            float deckTop = ArenaLayoutV2.SpawnDeckTopY;
            float terraceZ = ArenaLayoutV2.SpawnTerraceZ;
            float deckWidth = ArenaLayoutV2.SpawnDeckWidth;
            float deckDepth = ArenaLayoutV2.SpawnDeckDepth;

            for (int side = -1; side <= 1; side += 2)
            {
                string tag = side < 0 ? "TeamA" : "TeamB";
                var terrace = ArenaGeometryBuilder.CreateBox(parent, "SpawnTerrace_" + tag,
                    new Vector3(0f, deckTop * 0.5f, side * terraceZ),
                    new Vector3(deckWidth, deckTop, deckDepth), groundMat);
                if (inkableRenderers != null) inkableRenderers.Add(terrace.GetComponent<Renderer>());
                ArenaGeometryBuilder.AddInkWallSurface(terrace, inkCfg, inkWallMat, true);

                // 甲板前沿压顶 + 白色围栏
                float frontZ = side * (terraceZ + deckDepth * 0.5f - 0.06f);
                CreateDecorBox(parent, "Harbor_SpawnDeck_Coping_" + tag,
                    new Vector3(0f, deckTop + 0.06f, frontZ), new Vector3(deckWidth + 0.2f, 0.12f, 0.34f), boundaryMat);
                BuildSpawnRailing(parent, tag, side, metalMat);

                // 出生盘
                var padMat = side < 0 ? spawnMatA : spawnMatB;
                var pad = ArenaGeometryBuilder.CreateBox(parent, "SafeSpawnPad_" + tag,
                    new Vector3(0f, deckTop + ArenaLayoutV2.SpawnPadThickness * 0.5f, side * terraceZ),
                    new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), padMat);
                ArenaGeometryBuilder.AddInkWallSurface(pad, inkCfg, inkWallMat, true);

                CreateSpawnStair(parent, "Ramp_SpawnStair_" + (side < 0 ? "A" : "B") + "_East", 1f, side, groundMat, inkableRenderers);
                CreateSpawnStair(parent, "Ramp_SpawnStair_" + (side < 0 ? "A" : "B") + "_West", -1f, side, groundMat, inkableRenderers);
            }

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(parent, false);
            spawnAnchorGo.transform.position = new Vector3(0f, ArenaLayoutV2.SpawnPointY, -ArenaLayoutV2.SpawnAnchorZ);
            teamASpawnAnchor = spawnAnchorGo.transform;
        }

        private static void BuildSpawnRailing(Transform parent, string tag, float side, Material metalMat)
        {
            float deckTop = ArenaLayoutV2.SpawnDeckTopY;
            float terraceZ = ArenaLayoutV2.SpawnTerraceZ;
            float halfWidth = ArenaLayoutV2.SpawnDeckWidth * 0.5f;

            // 前沿两段栏杆（中间留出出生盘正前方 4m 的视线口）
            float inner = 2.4f;
            float outer = halfWidth - 0.2f;
            float segmentHalf = (outer - inner) * 0.5f;
            float segmentCenter = (outer + inner) * 0.5f;
            float frontZ = side * (terraceZ + ArenaLayoutV2.SpawnDeckDepth * 0.5f - 0.02f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                CreateDecorBox(parent, "Harbor_SpawnRail_" + tag + "_" + (xSide < 0 ? "W" : "E"),
                    new Vector3(xSide * segmentCenter, deckTop + 0.55f, frontZ),
                    new Vector3(segmentHalf * 2f, 0.06f, 0.06f), metalMat);
                for (int i = 0; i < 4; i++)
                {
                    float x = xSide * (inner + (segmentHalf * 2f) * (i / 3f));
                    CreateDecorBox(parent, "Harbor_SpawnRailPost_" + tag + "_" + (xSide < 0 ? "W" : "E") + "_" + i,
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

        // =====================================================================================
        // 边界墙
        // =====================================================================================

        private static void BuildBoundaryWalls(
            Transform parent,
            Material boundaryMat,
            Material metalMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat)
        {
            // 南侧（TeamA 方向）：0.55m 矮墙面向港湾水面，顶上加白栏杆；外侧仍有隐形防越界体。
            var southWall = ArenaGeometryBuilder.CreateBox(parent, "Boundary_South",
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f), boundaryMat);
            ArenaGeometryBuilder.AddOrientedInkWallSurface(southWall, inkCfg, inkWallMat, false,
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f));
            ArenaGeometryBuilder.CreateInvisibleBlocker(parent, "Boundary_South_Blocker",
                new Vector3(0f, 1.5f, -ArenaLayoutV2.BoundaryZ - 1f), new Vector3(42f, 3f, 1f));
            for (int i = 0; i < 21; i++)
            {
                float x = -20f + i * 2f;
                CreateDecorBox(parent, "Harbor_SouthRailPost_" + i,
                    new Vector3(x, 0.87f, -ArenaLayoutV2.BoundaryZ), new Vector3(0.07f, 0.64f, 0.07f), metalMat);
            }
            CreateDecorBox(parent, "Harbor_SouthRail",
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
        // 地标与装饰工具
        // =====================================================================================

        private static void BuildLandmarks(Transform parent, Material boundaryMat, Material accentMat, Material emissiveMat)
        {
            // 南北各一座灯塔：给低角度相机一个明确的港湾天际线锚点（墙外，不参与碰撞）。
            for (int side = -1; side <= 1; side += 2)
            {
                string tag = side < 0 ? "A" : "B";
                float z = side * 35.5f;
                float lighthouseX = side * 14f;
                CreateDecorBox(parent, "Harbor_Lighthouse_Base_" + tag,
                    new Vector3(lighthouseX, 0.4f, z), new Vector3(3.2f, 0.8f, 3.2f), boundaryMat);
                CreateDecorBox(parent, "Harbor_Lighthouse_Tower_" + tag,
                    new Vector3(lighthouseX, 4.6f, z), new Vector3(1.8f, 8.4f, 1.8f), boundaryMat);
                for (int i = 0; i < 3; i++)
                {
                    CreateDecorBox(parent, "Harbor_Lighthouse_Band_" + tag + "_" + i,
                        new Vector3(lighthouseX, 2.2f + i * 2.6f, z), new Vector3(1.88f, 0.9f, 1.88f), accentMat);
                }
                CreateDecorBox(parent, "Harbor_Lighthouse_Lantern_" + tag,
                    new Vector3(lighthouseX, 9.2f, z), new Vector3(1.4f, 1.4f, 1.4f), emissiveMat);
                CreateDecorBox(parent, "Harbor_Lighthouse_Cap_" + tag,
                    new Vector3(lighthouseX, 10.1f, z), new Vector3(1.9f, 0.3f, 1.9f), accentMat);

                // 灯塔侧的低矮仓库剪影，填住墙外空白（同样按点半对称摆放）
                float officeX = -side * 12f;
                CreateDecorBox(parent, "Harbor_Office_" + tag,
                    new Vector3(officeX, 1.8f, z - side * 1.5f), new Vector3(9f, 3.6f, 6f), boundaryMat);
                CreateDecorBox(parent, "Harbor_Office_Roof_" + tag,
                    new Vector3(officeX, 3.7f, z - side * 1.5f), new Vector3(9.6f, 0.2f, 6.6f), accentMat);
            }
        }

        private static void AddCoverPair(
            Transform parent,
            string namePrefix,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Collider> blockedColliders,
            float minAlpha = 0.35f,
            bool enableWallInk = true)
        {
            CreateSolidBox(parent, namePrefix + "_A", pos, scale, mat, inkCfg, inkWallMat, blockedColliders, enableWallInk);
            CreateSolidBox(parent, namePrefix + "_B", new Vector3(-pos.x, pos.y, -pos.z), scale, mat, inkCfg, inkWallMat, blockedColliders, enableWallInk);
        }

        /// <summary>实体掩体盒：带碰撞、带涂墨立面，但【不挂 OcclusionFader】避免相机旋转时 Bayer 矩阵闪烁。</summary>
        private static GameObject CreateSolidBox(
            Transform parent,
            string name,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Collider> blockedColliders,
            bool enableWallInk = true)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat);
            var col = go.GetComponent<Collider>();
            if (col != null && blockedColliders != null)
            {
                blockedColliders.Add(col);
            }
            if (enableWallInk)
            {
                ArenaGeometryBuilder.AddInkWallSurface(go, inkCfg, inkWallMat);
            }
            return go;
        }

        /// <summary>装饰盒：只渲染、不带 Collider，因此不会被 EnsurePaintableArena 补墨面。</summary>
        private static GameObject CreateDecorBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat);
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

        private static void AddHullBand(Transform parent, string name, Material mat,
            float y, float halfX, float halfZ, float height)
        {
            // 沿船壳四周贴一圈薄带：四条边各一段（比船壳大 1~2cm，不产生共面闪烁）。
            float thickness = 0.06f;
            CreateDecorBox(parent, name + "_S",
                new Vector3(0f, y, -halfZ), new Vector3(halfX * 2f, height, thickness), mat);
            CreateDecorBox(parent, name + "_N",
                new Vector3(0f, y, halfZ), new Vector3(halfX * 2f, height, thickness), mat);
            CreateDecorBox(parent, name + "_W",
                new Vector3(-halfX, y, 0f), new Vector3(thickness, height, halfZ * 2f), mat);
            CreateDecorBox(parent, name + "_E",
                new Vector3(halfX, y, 0f), new Vector3(thickness, height, halfZ * 2f), mat);
        }

        private static void CreateRope(Transform parent, string name, Vector3 from, Vector3 to, Material mat)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f) return;

            var rope = CreateDecorBox(parent, name, (from + to) * 0.5f, new Vector3(0.055f, 0.055f, length), mat);
            rope.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }
    }
}