using System.Collections.Generic;
using P1.Ink;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「潮汐货站 / Kelp Terminal」新地图的几何构建器。
    ///
    /// 设计原则：
    /// 1. **玩法几何等价**：地面 40×56、边界墙、出生甲板/台阶/出生盘、出生禁入圈、涂地网格
    ///    全部沿用 <see cref="ArenaLayoutV2"/> 的常量与尺寸，覆盖率判据与 Bot NavGrid 口径不变；
    /// 2. **只换节奏**：中央高台由「10×10 四向坡道」改为「10×6 东西长条 + 两端坡道」，
    ///    出生正前方加一道 1.2m 可跳低墙，把 7m 出口拆成左右两条推进线；
    /// 3. **地标**：中央信标柱 + 南北两座场外信号塔（无碰撞、不进 NavGrid），
    ///    形成「一眼能认出来」的剪影，而不只是另一堆集装箱；
    /// 4. **贴地安全**：可站立高差仍为 1.1m（中央）/ 1.5m（侧翼），≤ 跳跃 1.35m；
    ///    所有新增实体都是轴对齐盒体，Bot 的 0.5m NavGrid 采样不会出现斜向死缝。
    /// </summary>
    public static class KelpTerminalGeometryBuilder
    {
        public const string ArenaRootName = "Arena_KelpTerminal";
        public const string LandmarkRootName = "Dock_Landmarks_Kelp";

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
            Material containerTealMat = mats.containerTeal ?? wallMat;
            Material containerRustMat = mats.containerRust ?? wallMat;
            Material containerSlateMat = mats.containerSlate ?? wallMat;
            Material containerMustardMat = mats.containerMustard ?? wallMat;
            Material woodMat = mats.wood ?? mats.woodDark ?? wallMat;

            var arenaRoot = new GameObject(ArenaRootName);

            // 1. 主地面 40 x 56m（名字保持 Ground_Main_40x56：覆盖率白名单 / 地面 AO 烘焙按名字排除）
            var mainFloor = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Ground_Main_40x56",
                new Vector3(0f, -0.25f, 0f), new Vector3(ArenaLayoutV2.MapSizeX, 0.5f, ArenaLayoutV2.MapSizeZ), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(mainFloor.GetComponent<Renderer>());

            // 2. 中央长条货台：东西向 10×6m，高 1.1m；坡道只在东西两端
            var centerPlatform = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "HighGround_Center",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight * 0.5f, 0f),
                new Vector3(ArenaLayoutKelp.CenterRidgeSizeX, ArenaLayoutKelp.CenterRidgeHeight, ArenaLayoutKelp.CenterRidgeSizeZ),
                groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(centerPlatform.GetComponent<Renderer>());
            centerPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddInkWallSurface(centerPlatform, inkCfg, inkWallMat);

            var westRamp = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Ramp_Center_West_Inkable",
                new Vector3(-ArenaLayoutKelp.CenterRampX, ArenaLayoutKelp.CenterRampY, 0f),
                new Vector3(ArenaLayoutKelp.CenterRampSizeX, 0.25f, ArenaLayoutKelp.CenterRampSizeZ), groundMat);
            westRamp.transform.rotation = Quaternion.Euler(0f, 0f, ArenaLayoutKelp.CenterRampAngle);
            if (inkableRenderers != null) inkableRenderers.Add(westRamp.GetComponent<Renderer>());
            westRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddOrientedInkWallSurface(westRamp, inkCfg, inkWallMat);

            var eastRamp = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Ramp_Center_East_Inkable",
                new Vector3(ArenaLayoutKelp.CenterRampX, ArenaLayoutKelp.CenterRampY, 0f),
                new Vector3(ArenaLayoutKelp.CenterRampSizeX, 0.25f, ArenaLayoutKelp.CenterRampSizeZ), groundMat);
            eastRamp.transform.rotation = Quaternion.Euler(0f, 0f, -ArenaLayoutKelp.CenterRampAngle);
            if (inkableRenderers != null) inkableRenderers.Add(eastRamp.GetComponent<Renderer>());
            eastRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddOrientedInkWallSurface(eastRamp, inkCfg, inkWallMat);

            // 2.1 中央信标柱：全场唯一记忆点，同时是台面上的可涂掩体
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "KT_Beacon_Central",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + ArenaLayoutKelp.BeaconHeight * 0.5f, 0f),
                new Vector3(ArenaLayoutKelp.BeaconX, ArenaLayoutKelp.BeaconHeight, ArenaLayoutKelp.BeaconX),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 信标配色环 + 灯罩 + 细天线：让中央地标在逆光下也有辨识度
            Material beaconBandMat = mats.containerMustard ?? wallMat;
            Material beaconRustMat = mats.containerRust ?? wallMat;
            for (int i = 0; i < 3; i++)
            {
                Material band = i % 2 == 0 ? beaconBandMat : beaconRustMat;
                CreateDecorBox(arenaRoot.transform, "KT_Beacon_Band_" + i,
                    new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + 0.75f + i * 0.95f, 0f),
                    new Vector3(ArenaLayoutKelp.BeaconX + 0.08f, 0.32f, ArenaLayoutKelp.BeaconX + 0.08f), band);
            }

            CreateDecorBox(arenaRoot.transform, "KT_Beacon_Lamp",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + ArenaLayoutKelp.BeaconHeight + 0.22f, 0f),
                new Vector3(1.0f, 0.44f, 1.0f), mats.propEmissive);
            CreateDecorBox(arenaRoot.transform, "KT_Beacon_Cap",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + ArenaLayoutKelp.BeaconHeight + 0.50f, 0f),
                new Vector3(1.4f, 0.12f, 1.4f), mats.propMetal);
            CreateDecorBox(arenaRoot.transform, "KT_Beacon_Antenna",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + ArenaLayoutKelp.BeaconHeight + 1.10f, 0f),
                new Vector3(0.12f, 1.2f, 0.12f), mats.propMetal);

            // 2.2 货台长边的浅色压顶条：给高台边缘一条高光，逆光时仍能读出 1.1m 的高度
            Material copingMat = boundaryMat;
            CreateDecorBox(arenaRoot.transform, "KT_Ridge_Coping_South",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + 0.06f, -ArenaLayoutKelp.CenterRidgeSizeZ * 0.5f - 0.06f),
                new Vector3(ArenaLayoutKelp.CenterRidgeSizeX + 0.16f, 0.12f, 0.34f), copingMat);
            CreateDecorBox(arenaRoot.transform, "KT_Ridge_Coping_North",
                new Vector3(0f, ArenaLayoutKelp.CenterRidgeHeight + 0.06f, ArenaLayoutKelp.CenterRidgeSizeZ * 0.5f + 0.06f),
                new Vector3(ArenaLayoutKelp.CenterRidgeSizeX + 0.16f, 0.12f, 0.34f), copingMat);

            // 2.2 高台两端的低位花坛：保留原有争夺圈的开阔可涂面
            AddCoverPair(arenaRoot.transform, "KT_Center_Planter", new Vector3(3.2f, 1.5f, 0f), new Vector3(1.4f, 0.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders);

            // 3. 东西侧翼高台：1.5m > 跳跃 1.35m，坡道是唯一常规入口（复用既有尺寸与四条坡道）
            BuildFlankPlatform(arenaRoot.transform, -1f, groundMat, boundaryMat, inkCfg, inkWallMat, inkableRenderers);
            BuildFlankPlatform(arenaRoot.transform, 1f, groundMat, boundaryMat, inkCfg, inkWallMat, inkableRenderers);

            // 3.1 侧翼台面中段低柜：高台争夺的第二个掩体点
            AddCoverPair(arenaRoot.transform, "KT_Flank_Locker", new Vector3(13.45f, 2.3f, 0f), new Vector3(1.6f, 1.6f, 2.4f),
                containerSlateMat, inkCfg, inkWallMat, blockedColliders);

            // 4. 外侧绕后走廊：贴住侧翼台外沿，保持 2.10m 外走廊
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Container_Flank_West_01_Teal",
                new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, -3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Container_Flank_West_02_Rust",
                new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, 4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Container_Flank_East_01_Teal",
                new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, 3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Container_Flank_East_02_Rust",
                new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, -4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 5. 中场：只保留一对高柜 + 一对 1.2m 可跳低柜，高柜/低柜错位形成 S 形推进线
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "KT_Container_Mid_A",
                new Vector3(ArenaLayoutKelp.MidLowBoxX, 0.9f, -ArenaLayoutKelp.MidLowBoxZ), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "KT_Container_Mid_B",
                new Vector3(-ArenaLayoutKelp.MidLowBoxX, 0.9f, ArenaLayoutKelp.MidLowBoxZ), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "KT_LowBox_Mid_A",
                new Vector3(-ArenaLayoutKelp.MidLowBoxX, 0.6f, -ArenaLayoutKelp.MidLowBoxZ), new Vector3(2.2f, 1.2f, 4.2f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "KT_LowBox_Mid_B",
                new Vector3(ArenaLayoutKelp.MidLowBoxX, 0.6f, ArenaLayoutKelp.MidLowBoxZ), new Vector3(2.2f, 1.2f, 4.2f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 5.1 出生正前方低位横墙：把 7m 出口拆成左右两条推进线（1.2m 可跳，不是死墙）
            AddCoverPair(arenaRoot.transform, "KT_ExitBarrier", new Vector3(0f, 0.6f, -ArenaLayoutKelp.ExitBarrierZ),
                new Vector3(5.2f, 1.2f, 0.8f), wallMat, inkCfg, inkWallMat, blockedColliders);

            // 6. 出生前沿战术掩体 + 出口矮墙（保留既有战斗节奏与对象命名）
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Barrier_TeamA_Left", new Vector3(-4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Barrier_TeamA_Right", new Vector3(4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Parapet_TeamA_Exit", new Vector3(0f, 0.5f, -13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Barrier_TeamB_Left", new Vector3(-4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Barrier_TeamB_Right", new Vector3(4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            ArenaGeometryBuilder.CreateCover(arenaRoot.transform, "Parapet_TeamB_Exit", new Vector3(0f, 0.5f, 13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7. 延长区（z 12..24）：出生网关、集装箱、花坛、木箱（位置与既有图一致，贴图 AO 对齐）
            AddCoverPair(arenaRoot.transform, "SpawnGate", new Vector3(5.0f, 0.75f, 21.4f), new Vector3(3.0f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders);
            AddCoverPair(arenaRoot.transform, "SpawnGate_Wing", new Vector3(-5.0f, 0.75f, 21.4f), new Vector3(3.0f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders);

            AddCoverPair(arenaRoot.transform, "Container_Extension_Teal",
                new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 16.5f), new Vector3(2.4f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders);
            AddCoverPair(arenaRoot.transform, "Container_Extension_Rust",
                new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 21.2f), new Vector3(2.4f, 1.8f, 4.6f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders);

            AddCoverPair(arenaRoot.transform, "Planter_Extension", new Vector3(7.0f, 0.45f, 13.5f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders);
            AddCoverPair(arenaRoot.transform, "Planter_Extension_Deep", new Vector3(7.0f, 0.45f, 19.0f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders);
            AddCoverPair(arenaRoot.transform, "Crate_Extension", new Vector3(11.5f, 0.6f, 19.5f), new Vector3(1.6f, 1.2f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders);
            AddCoverPair(arenaRoot.transform, "Crate_Extension_Outer", new Vector3(10.0f, 0.6f, 22.5f), new Vector3(1.6f, 1.2f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders);

            // 7.1 系缆桩列：0.6m 可跨的小掩体，给外侧推进线一个节奏点；不挂墨面以节省运行时立面贴图
            AddCoverPair(arenaRoot.transform, "KT_Bollard", new Vector3(8.9f, 0.3f, -13.8f), new Vector3(0.4f, 0.6f, 0.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, false);
            AddCoverPair(arenaRoot.transform, "KT_Bollard_Deep", new Vector3(8.9f, 0.3f, -17.2f), new Vector3(0.4f, 0.6f, 0.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, false);

            // 8. 出生甲板 + 两侧台阶（复用既有常量：1.2m 台面、5 级 0.24m 台阶）
            var terraceA = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "SpawnTerrace_TeamA",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY * 0.5f, -ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnDeckWidth, ArenaLayoutV2.SpawnDeckTopY, ArenaLayoutV2.SpawnDeckDepth), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceA.GetComponent<Renderer>());
            terraceA.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddInkWallSurface(terraceA, inkCfg, inkWallMat, true);

            var terraceB = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "SpawnTerrace_TeamB",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY * 0.5f, ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnDeckWidth, ArenaLayoutV2.SpawnDeckTopY, ArenaLayoutV2.SpawnDeckDepth), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceB.GetComponent<Renderer>());
            terraceB.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddInkWallSurface(terraceB, inkCfg, inkWallMat, true);

            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_A_East", 1f, -1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_A_West", -1f, -1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_B_East", 1f, 1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_B_West", -1f, 1f, groundMat, inkableRenderers);

            // 出生甲板前沿压顶：让 1.2m 台面在逆光下也有清晰边界
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (ArenaLayoutV2.SpawnTerraceZ + ArenaLayoutV2.SpawnDeckDepth * 0.5f - 0.06f);
                CreateDecorBox(arenaRoot.transform, "KT_SpawnDeck_Coping_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY + 0.06f, z),
                    new Vector3(ArenaLayoutV2.SpawnDeckWidth + 0.2f, 0.12f, 0.34f), boundaryMat);
            }

            // 9. 四周边界墙：与既有图完全一致（南 0.55m 可见墙 + 外侧隐形防越界体）
            var southWall = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Boundary_South",
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f), boundaryMat);
            ArenaGeometryBuilder.AddOrientedInkWallSurface(southWall, inkCfg, inkWallMat, false,
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f));
            ArenaGeometryBuilder.CreateInvisibleBlocker(arenaRoot.transform, "Boundary_South_Blocker",
                new Vector3(0f, 1.5f, -ArenaLayoutV2.BoundaryZ - 1f), new Vector3(42f, 3f, 1f));

            var northWall = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Boundary_North",
                new Vector3(0f, ArenaLayoutV2.NorthBoundaryWallHeight * 0.5f, ArenaLayoutV2.BoundaryZ),
                new Vector3(42f, ArenaLayoutV2.NorthBoundaryWallHeight, 1f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(northWall, inkCfg, inkWallMat, false);

            var westWall = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Boundary_West",
                new Vector3(-ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(westWall, inkCfg, inkWallMat, false);

            var eastWall = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "Boundary_East",
                new Vector3(ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            ArenaGeometryBuilder.AddInkWallSurface(eastWall, inkCfg, inkWallMat, false);

            // 10. 出生盘（顶面由 SpawnPad.shader 采样顶视墨迹图集）
            var spawnPadA = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamA",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY + ArenaLayoutV2.SpawnPadThickness * 0.5f, -ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), spawnMatA);
            ArenaGeometryBuilder.AddInkWallSurface(spawnPadA, inkCfg, inkWallMat, true);

            var spawnPadB = ArenaGeometryBuilder.CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamB",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY + ArenaLayoutV2.SpawnPadThickness * 0.5f, ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), spawnMatB);
            ArenaGeometryBuilder.AddInkWallSurface(spawnPadB, inkCfg, inkWallMat, true);

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(arenaRoot.transform, false);
            spawnAnchorGo.transform.position = new Vector3(0f, ArenaLayoutV2.SpawnPointY, -ArenaLayoutV2.SpawnAnchorZ);
            teamASpawnAnchor = spawnAnchorGo.transform;

            // 11. 覆盖率兜底 + 本图地标（地标不进 arenaRoot，避免被自动补墨面）
            ArenaGeometryBuilder.EnsurePaintableArena(arenaRoot, inkCfg, inkWallMat);
            BuildDockLandmarks(mats);

            return arenaRoot;
        }

        private static void BuildFlankPlatform(
            Transform parent,
            float sideSign,
            Material groundMat,
            Material copingMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers)
        {
            string side = sideSign < 0f ? "West" : "East";
            var platform = ArenaGeometryBuilder.CreateBox(parent, "HighGround_" + side,
                new Vector3(sideSign * ArenaLayoutV2.FlankPlatformX, ArenaLayoutV2.FlankPlatformHeight * 0.5f, 0f),
                new Vector3(4.5f, ArenaLayoutV2.FlankPlatformHeight, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(platform.GetComponent<Renderer>());
            platform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddInkWallSurface(platform, inkCfg, inkWallMat);

            CreateDecorBox(parent, "KT_Flank_Coping_Outer_" + side,
                new Vector3(sideSign * (ArenaLayoutV2.FlankPlatformX + 2.25f + 0.06f), ArenaLayoutV2.FlankPlatformHeight + 0.06f, 0f),
                new Vector3(0.34f, 0.12f, 9.16f), copingMat);
            CreateDecorBox(parent, "KT_Flank_Coping_Inner_" + side,
                new Vector3(sideSign * (ArenaLayoutV2.FlankPlatformX - 2.25f - 0.06f), ArenaLayoutV2.FlankPlatformHeight + 0.06f, 0f),
                new Vector3(0.34f, 0.12f, 9.16f), copingMat);

            BuildFlankRamp(parent, "Ramp_" + side + "_South_Inkable", sideSign, -1f,
                new Vector3(sideSign * ArenaLayoutV2.FlankPlatformX, 0.645f, -7.85f),
                Quaternion.Euler(-12.5f, 0f, 0f), groundMat, inkCfg, inkWallMat, inkableRenderers);
            BuildFlankRamp(parent, "Ramp_" + side + "_North_Inkable", sideSign, 1f,
                new Vector3(sideSign * ArenaLayoutV2.FlankPlatformX, 0.645f, 7.85f),
                Quaternion.Euler(12.5f, 0f, 0f), groundMat, inkCfg, inkWallMat, inkableRenderers);
        }

        private static void BuildFlankRamp(
            Transform parent,
            string name,
            float sideSign,
            float zSign,
            Vector3 pos,
            Quaternion rot,
            Material groundMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers)
        {
            var ramp = ArenaGeometryBuilder.CreateBox(parent, name, pos, new Vector3(3.6f, 0.25f, 6.8f), groundMat);
            ramp.transform.rotation = rot;
            if (inkableRenderers != null) inkableRenderers.Add(ramp.GetComponent<Renderer>());
            ramp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            ArenaGeometryBuilder.AddOrientedInkWallSurface(ramp, inkCfg, inkWallMat);
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
            ArenaGeometryBuilder.CreateCover(parent, namePrefix + "_A", pos, scale, mat, inkCfg, inkWallMat, blockedColliders, minAlpha, enableWallInk);
            ArenaGeometryBuilder.CreateCover(parent, namePrefix + "_B", new Vector3(-pos.x, pos.y, -pos.z), scale, mat, inkCfg, inkWallMat, blockedColliders, minAlpha, enableWallInk);
        }

        /// <summary>装饰盒：只渲染、不带 Collider，因此不会被 EnsurePaintableArena 补墨面。</summary>
        private static GameObject CreateDecorBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = ArenaGeometryBuilder.CreateBox(parent, name, pos, scale, mat);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
#if UNITY_EDITOR
                Object.DestroyImmediate(col);
#else
                Object.Destroy(col);
#endif
            }
            return go;
        }

        /// <summary>
        /// 场外地标：南北两座信号塔（在边界墙外，不参与碰撞、不进 NavGrid）。
        /// 目的：给低角度相机一个可辨识的天际线锚点，避免远景只剩一片雾。
        /// </summary>
        private static void BuildDockLandmarks(ArenaMaterialSet mats)
        {
            var root = new GameObject(LandmarkRootName);
            Material concrete = mats.boundary ?? mats.wall;
            Material rust = mats.containerRust;
            Material lamp = mats.propEmissive;

            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 35.5f;
                CreateDecorBox(root.transform, "KT_SignalTower_Base_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, 0.4f, z), new Vector3(2.2f, 0.8f, 2.2f), concrete);
                CreateDecorBox(root.transform, "KT_SignalTower_Shaft_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, 5.0f, z), new Vector3(0.9f, 9.2f, 0.9f), concrete);

                // 红白相间的信号色环
                for (int i = 0; i < 3; i++)
                {
                    Material band = i % 2 == 0 ? rust : concrete;
                    CreateDecorBox(root.transform, "KT_SignalTower_Band_" + (side < 0 ? "A" : "B") + "_" + i,
                        new Vector3(0f, 2.4f + i * 2.4f, z), new Vector3(1.02f, 0.55f, 1.02f), band);
                }

                CreateDecorBox(root.transform, "KT_SignalTower_Lantern_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, 9.95f, z), new Vector3(0.9f, 0.9f, 0.9f), lamp);
                CreateDecorBox(root.transform, "KT_SignalTower_Cap_" + (side < 0 ? "A" : "B"),
                    new Vector3(0f, 10.5f, z), new Vector3(1.3f, 0.16f, 1.3f), rust);
            }
        }
    }
}
