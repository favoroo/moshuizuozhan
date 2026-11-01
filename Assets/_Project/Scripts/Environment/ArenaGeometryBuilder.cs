using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 40×56m 集装箱码头竞技场几何装配器（P7-B）：
    /// 纯代码装配中央争夺高台与四向斜坡、东西侧翼高台、绕后走廊与集装箱掩体群、
    /// 40×56 延长区的箱堆/花坛/出生网关，以及双方出生安全台与边界护栏。
    /// 所有掩体自动挂载 OcclusionFader (minAlpha 0.35)，主要柜体挂载 InkWallSurface。
    ///
    /// 兼容性约定：
    /// - 原有对象名（HighGround_Center / Ramp_* / CoverWall_00 / Container_Mid_SW_Mustard 等）
    ///   与坐标保持不变，避免破坏 PlayMode 墙面涂地与遮挡测试；
    /// - 旧的 9 材质签名继续保留，内部转成 ArenaMaterialSet 调用新实现。
    /// </summary>
    public static class ArenaGeometryBuilder
    {
        public const string ArenaRootName = "Arena_40x56";

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

            // 1. 主地面 40 x 56m
            var mainFloor = CreateBox(arenaRoot.transform, "Ground_Main_40x56",
                new Vector3(0f, -0.25f, 0f), new Vector3(ArenaLayoutV2.MapSizeX, 0.5f, ArenaLayoutV2.MapSizeZ), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(mainFloor.GetComponent<Renderer>());

            // 2. 中央中立争夺高台（10x10m，高 1.1m ≤ 1.2m）+ 四向斜坡
            var centerPlatform = CreateBox(arenaRoot.transform, "HighGround_Center", new Vector3(0f, 0.55f, 0f), new Vector3(10f, 1.1f, 10f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(centerPlatform.GetComponent<Renderer>());
            var centerFader = centerPlatform.AddComponent<OcclusionFader>();
            centerFader.MinAlpha = 0.35f;
            AddInkWallSurface(centerPlatform, inkCfg, inkWallMat);

            // 南斜坡 (Team A 进攻主道)
            var southRamp = CreateBox(arenaRoot.transform, "Ramp_South_Inkable", new Vector3(0f, 0.48f, -7.4f), new Vector3(4.4f, 0.25f, 5.0f), groundMat);
            southRamp.transform.rotation = Quaternion.Euler(-12.8f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(southRamp.GetComponent<Renderer>());
            southRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(southRamp, inkCfg, inkWallMat);

            // 北斜坡 (Team B 进攻主道)
            var northRamp = CreateBox(arenaRoot.transform, "Ramp_North_Inkable", new Vector3(0f, 0.48f, 7.4f), new Vector3(4.4f, 0.25f, 5.0f), groundMat);
            northRamp.transform.rotation = Quaternion.Euler(12.8f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(northRamp.GetComponent<Renderer>());
            northRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(northRamp, inkCfg, inkWallMat);

            // 西斜坡 (西侧翼切入高台)
            var westRamp = CreateBox(arenaRoot.transform, "Ramp_West_Inkable", new Vector3(-7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), groundMat);
            westRamp.transform.rotation = Quaternion.Euler(0f, 0f, 12.8f);
            if (inkableRenderers != null) inkableRenderers.Add(westRamp.GetComponent<Renderer>());
            westRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(westRamp, inkCfg, inkWallMat);

            // 东斜坡 (东侧翼切入高台)
            var eastRamp = CreateBox(arenaRoot.transform, "Ramp_East_Inkable", new Vector3(7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), groundMat);
            eastRamp.transform.rotation = Quaternion.Euler(0f, 0f, -12.8f);
            if (inkableRenderers != null) inkableRenderers.Add(eastRamp.GetComponent<Renderer>());
            eastRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(eastRamp, inkCfg, inkWallMat);

            // 中央高台低矮掩体花坛（高 0.8m，置于高台两侧，保持原点 (0,0) 高台争夺圈开阔可涂）
            CreateCover(arenaRoot.transform, "Cover_Center_West_Planter", new Vector3(-2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Cover_Center_East_Planter", new Vector3(2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 2.1 中央高台顶女儿墙：8 段 0.5m 矮墙，留出四向 4.4m 坡道口
            CreateCover(arenaRoot.transform, "Parapet_Center_SW", new Vector3(-3.6f, 1.35f, -4.85f), new Vector3(2.8f, 0.5f, 0.32f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_SE", new Vector3(3.6f, 1.35f, -4.85f), new Vector3(2.8f, 0.5f, 0.32f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_NW", new Vector3(-3.6f, 1.35f, 4.85f), new Vector3(2.8f, 0.5f, 0.32f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_NE", new Vector3(3.6f, 1.35f, 4.85f), new Vector3(2.8f, 0.5f, 0.32f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_WS", new Vector3(-4.85f, 1.35f, -3.6f), new Vector3(0.32f, 0.5f, 2.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_WN", new Vector3(-4.85f, 1.35f, 3.6f), new Vector3(0.32f, 0.5f, 2.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_ES", new Vector3(4.85f, 1.35f, -3.6f), new Vector3(0.32f, 0.5f, 2.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_Center_EN", new Vector3(4.85f, 1.35f, 3.6f), new Vector3(0.32f, 0.5f, 2.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 3. 东西两侧侧翼高台（高 1.5m > 跳跃 1.35m：坡道是唯一常规入口，形成高地争夺）
            //    坡道 6.8m 长、12.5° 平缓（顶端 1.5m 与台面等高，低端贴地）。
            float flankY = ArenaLayoutV2.FlankPlatformHeight;
            float flankRampLength = 6.8f;
            float flankRampY = 0.645f;
            float flankRampZ = 7.85f;
            var westPlatform = CreateBox(arenaRoot.transform, "HighGround_West",
                new Vector3(-ArenaLayoutV2.FlankPlatformX, flankY * 0.5f, 0f),
                new Vector3(4.5f, flankY, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(westPlatform.GetComponent<Renderer>());
            westPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(westPlatform, inkCfg, inkWallMat);

            var westSouthRamp = CreateBox(arenaRoot.transform, "Ramp_West_South_Inkable",
                new Vector3(-ArenaLayoutV2.FlankPlatformX, flankRampY, -flankRampZ),
                new Vector3(3.6f, 0.25f, flankRampLength), groundMat);
            westSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westSouthRamp.GetComponent<Renderer>());
            westSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(westSouthRamp, inkCfg, inkWallMat);

            var westNorthRamp = CreateBox(arenaRoot.transform, "Ramp_West_North_Inkable",
                new Vector3(-ArenaLayoutV2.FlankPlatformX, flankRampY, flankRampZ),
                new Vector3(3.6f, 0.25f, flankRampLength), groundMat);
            westNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westNorthRamp.GetComponent<Renderer>());
            westNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(westNorthRamp, inkCfg, inkWallMat);

            var eastPlatform = CreateBox(arenaRoot.transform, "HighGround_East",
                new Vector3(ArenaLayoutV2.FlankPlatformX, flankY * 0.5f, 0f),
                new Vector3(4.5f, flankY, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(eastPlatform.GetComponent<Renderer>());
            eastPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(eastPlatform, inkCfg, inkWallMat);

            var eastSouthRamp = CreateBox(arenaRoot.transform, "Ramp_East_South_Inkable",
                new Vector3(ArenaLayoutV2.FlankPlatformX, flankRampY, -flankRampZ),
                new Vector3(3.6f, 0.25f, flankRampLength), groundMat);
            eastSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastSouthRamp.GetComponent<Renderer>());
            eastSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(eastSouthRamp, inkCfg, inkWallMat);

            var eastNorthRamp = CreateBox(arenaRoot.transform, "Ramp_East_North_Inkable",
                new Vector3(ArenaLayoutV2.FlankPlatformX, flankRampY, flankRampZ),
                new Vector3(3.6f, 0.25f, flankRampLength), groundMat);
            eastNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastNorthRamp.GetComponent<Renderer>());
            eastNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(eastNorthRamp, inkCfg, inkWallMat);

            // 4. 外侧战术绕后走廊与集装箱掩体群（贴住侧翼台外沿、重叠 0.05m，死缝消除，外走廊拓宽至 2.10m）
            CreateCover(arenaRoot.transform, "Container_Flank_West_01_Teal", new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, -3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Flank_West_02_Rust", new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, 4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Flank_East_01_Teal", new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, 3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Flank_East_02_Rust", new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, -4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 5. 中场两侧战术集装箱与经典掩体墙（兼容现有 WallInkPlayboxTests）
            //    集装箱整体归位到 |x|=8.1：内边 |x|=7.0 与掩体墙贴死，外边 |x|=9.2 与侧翼台留 2.0m 车道。
            CreateCover(arenaRoot.transform, "CoverWall_00",
                new Vector3(-ArenaLayoutV2.CoverWallX, 0.9f, -4.5f),
                new Vector3(ArenaLayoutV2.CoverWallWidth, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "CoverWall_01",
                new Vector3(ArenaLayoutV2.CoverWallX, 0.9f, 4.5f),
                new Vector3(ArenaLayoutV2.CoverWallWidth, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Mid_SW_Mustard", new Vector3(-ArenaLayoutV2.ContainerMidX, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Mid_NE_Mustard", new Vector3(ArenaLayoutV2.ContainerMidX, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Mid_SE_Slate", new Vector3(ArenaLayoutV2.ContainerMidX, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerSlateMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Mid_NW_Slate", new Vector3(-ArenaLayoutV2.ContainerMidX, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerSlateMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 6. 双方出生前沿战术掩体墙（z=±11.2 保留为进入中场的最后一道掩体）
            CreateCover(arenaRoot.transform, "Barrier_TeamA_Left", new Vector3(-4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Barrier_TeamA_Right", new Vector3(4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_TeamA_Exit", new Vector3(0f, 0.5f, -13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Barrier_TeamB_Right", new Vector3(4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Barrier_TeamB_Left", new Vector3(-4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Parapet_TeamB_Exit", new Vector3(0f, 0.5f, 13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7. P7-B 延长区（z 12..24）：新增掩体与出生网关，全部以原点 180° 点对称布置
            // 7.0 中路错位低掩体：z≈±16.5 与 z≈±9.0，高 1.2m（可跳）；避开 Ramp 底端 z=±9.9、x∈[-2.2,2.2] 的 4.4m 坡道口，左右错位形成 S 形推进线
            CreateCoverPair(arenaRoot.transform, "Cover_MidLane_Far", ArenaLayoutV2.MidCoverFarPos, ArenaLayoutV2.MidCoverFarSize,
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Cover_MidLane_Near", ArenaLayoutV2.MidCoverNearPos, ArenaLayoutV2.MidCoverNearSize,
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.1 出生网关：z=±21.4，每队 2 块 3m 挡墙（x=±5.0），中间留 7m 出口
            CreateCoverPair(arenaRoot.transform, "SpawnGate", new Vector3(5.0f, 0.75f, 21.4f), new Vector3(3.0f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "SpawnGate_Wing", new Vector3(-5.0f, 0.75f, 21.4f), new Vector3(3.0f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.2 延长区集装箱：每半场两组，贴墙外走廊对齐 2.05m；Rust 与 Teal 沿 Z 重叠 0.1m，消除 0.4m 竖缝
            CreateCoverPair(arenaRoot.transform, "Container_Extension_Teal", new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 16.5f), new Vector3(2.4f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Container_Extension_Rust", new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 21.2f), new Vector3(2.4f, 1.8f, 4.6f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.3 延长区花坛与木箱：低矮掩体，保持中路快速推进的节奏（木箱 1.2m 可跳，不再是 1.4m 差 5cm）
            CreateCoverPair(arenaRoot.transform, "Planter_Extension", new Vector3(7.0f, 0.45f, 13.5f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Planter_Extension_Deep", new Vector3(7.0f, 0.45f, 19.0f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Crate_Extension", new Vector3(11.5f, 0.6f, 19.5f), new Vector3(1.6f, 1.2f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Crate_Extension_Outer", new Vector3(10.0f, 0.6f, 22.5f), new Vector3(1.6f, 1.2f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.4 出生甲板：抬到 1.2m 的台面并向后墙延展，覆盖整条出生线（对齐 INKWAVE 出生甲板）。
            //     台面顶面由 InkGround 采样顶视墨迹图集，因此不加入 blockedColliders；
            //     两侧各一条 5 级台阶负责上下，正面中央留落差 + SpawnGate 两翼矮墙当女儿墙。
            var terraceA = CreateBox(arenaRoot.transform, "SpawnTerrace_TeamA",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY * 0.5f, -ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnDeckWidth, ArenaLayoutV2.SpawnDeckTopY, ArenaLayoutV2.SpawnDeckDepth), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceA.GetComponent<Renderer>());
            terraceA.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(terraceA, inkCfg, inkWallMat, true);

            var terraceB = CreateBox(arenaRoot.transform, "SpawnTerrace_TeamB",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY * 0.5f, ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnDeckWidth, ArenaLayoutV2.SpawnDeckTopY, ArenaLayoutV2.SpawnDeckDepth), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceB.GetComponent<Renderer>());
            terraceB.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(terraceB, inkCfg, inkWallMat, true);

            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_A_East", 1f, -1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_A_West", -1f, -1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_B_East", 1f, 1f, groundMat, inkableRenderers);
            CreateSpawnStair(arenaRoot.transform, "Ramp_SpawnStair_B_West", -1f, 1f, groundMat, inkableRenderers);

            // 8. 四周边界围墙（可涂墨留痕，禁止攀爬越界）
            // 南墙：可见网格只有 0.55m 高，因此碰撞体与墨面都按 0.55m 建。
            // 历史写法把同一个 Collider 拉到 3m 高来防越界，结果朝矮墙上方开火时射线打在
            // 隐形墙上、命中点落在墨面范围之外 —— 玩家看到的就是"打南墙什么都不发生"。
            // 现在防越界改由墙体外部独立的隐形 Blocker 承担，可见墙面保持网格等高。
            var southWall = CreateBox(arenaRoot.transform, "Boundary_South", new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f), boundaryMat);
            AddOrientedInkWallSurface(southWall, inkCfg, inkWallMat, false,
                new Vector3(0f, 0.275f, -ArenaLayoutV2.BoundaryZ), new Vector3(42f, 0.55f, 1f));
            CreateInvisibleBlocker(arenaRoot.transform, "Boundary_South_Blocker",
                new Vector3(0f, 1.5f, -ArenaLayoutV2.BoundaryZ - 1f), new Vector3(42f, 3f, 1f));

            var northWall = CreateBox(arenaRoot.transform, "Boundary_North",
                new Vector3(0f, ArenaLayoutV2.NorthBoundaryWallHeight * 0.5f, ArenaLayoutV2.BoundaryZ),
                new Vector3(42f, ArenaLayoutV2.NorthBoundaryWallHeight, 1f), boundaryMat);
            AddInkWallSurface(northWall, inkCfg, inkWallMat, false);

            var westWall = CreateBox(arenaRoot.transform, "Boundary_West", new Vector3(-ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            AddInkWallSurface(westWall, inkCfg, inkWallMat, false);

            var eastWall = CreateBox(arenaRoot.transform, "Boundary_East", new Vector3(ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            AddInkWallSurface(eastWall, inkCfg, inkWallMat, false);

            // 9. 双方出生盘（随出生甲板上移；顶面由 SpawnPad.shader 采样顶视墨迹图集，因此不进 blockedColliders）
            //    盘面深度 6.3m 是 SpawnPad.shader 外圈 r=3.15m 的最小容纳尺寸，正好落在 6.4m 深的台面上。
            var spawnPadA = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamA",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY + ArenaLayoutV2.SpawnPadThickness * 0.5f, -ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), spawnMatA);
            AddInkWallSurface(spawnPadA, inkCfg, inkWallMat, true);

            var spawnPadB = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamB",
                new Vector3(0f, ArenaLayoutV2.SpawnDeckTopY + ArenaLayoutV2.SpawnPadThickness * 0.5f, ArenaLayoutV2.SpawnTerraceZ),
                new Vector3(ArenaLayoutV2.SpawnPadWidth, ArenaLayoutV2.SpawnPadThickness, ArenaLayoutV2.SpawnPadDepth), spawnMatB);
            AddInkWallSurface(spawnPadB, inkCfg, inkWallMat, true);

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(arenaRoot.transform, false);
            spawnAnchorGo.transform.position = new Vector3(0f, ArenaLayoutV2.SpawnPointY, -ArenaLayoutV2.SpawnAnchorZ);
            teamASpawnAnchor = spawnAnchorGo.transform;

            // 9.5 覆盖率兜底：把漏装墨面的可视碰撞盒补全（默认全部可涂）
            EnsurePaintableArena(arenaRoot, inkCfg, inkWallMat);

            // 9.6 程序化结构装饰层（P30 场地视觉精修）：
            // 为集装箱、掩体墙、高台/坡道、边界墙、出生区装配细节结构。
            // 全部合并为 2 个装饰网格（金属/警示 + 混凝土），无碰撞、无墨面、不破坏既有玩法。
            BuildStructuralDecorations(arenaRoot, mats);

            return arenaRoot;
        }

        /// <summary>
        /// 旧签名（9 材质）兼容入口：EditMode 测试与旧编辑器脚本仍可直接调用。
        /// </summary>
        public static GameObject BuildArenaGeometry(
            Material groundMat,
            Material wallMat,
            Material boundaryMat,
            Material spawnMatA,
            Material spawnMatB,
            Material containerTealMat,
            Material containerRustMat,
            Material containerSlateMat,
            Material containerMustardMat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders,
            out Transform teamASpawnAnchor)
        {
            var mats = new ArenaMaterialSet
            {
                ground = groundMat,
                wall = wallMat,
                boundary = boundaryMat,
                spawnPadA = spawnMatA,
                spawnPadB = spawnMatB,
                containerTeal = containerTealMat,
                containerRust = containerRustMat,
                containerSlate = containerSlateMat,
                containerMustard = containerMustardMat,
            };

            return BuildArenaGeometry(mats, inkCfg, inkWallMat, inkableRenderers, blockedColliders, out teamASpawnAnchor);
        }

        public static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            // 竞技场几何从不移动：整棵 arena 树标静态，构建期/运行期静态合批才能合并这几十个 Cube
            go.isStatic = true;
            if (mat != null)
            {
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }
            return go;
        }

        /// <summary>
        /// 创建只有碰撞、没有渲染的隐形拦截体（例如南边界矮墙外侧的防越界墙）。
        /// 它不产生任何可视表面，因此不装配墨面、也不参与涂地计分。
        /// </summary>
        public static GameObject CreateInvisibleBlocker(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.isStatic = true;

            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            return go;
        }

        /// <summary>
        /// 涂墨覆盖率兜底（对齐 INKWAVE 的 "paintable 默认全部可涂"）：
        /// 竞技场里每个"有 Renderer 又有非 Trigger BoxCollider"的盒子都必须有墨面，
        /// 否则子弹打上去只会退化成涂命中点下方的地面 —— 表现就是"这里喷不上油漆"。
        /// 已经显式装配过（含斜坡的朝向盒）的对象会被跳过，因此是纯增量。
        /// </summary>
        public static int EnsurePaintableArena(GameObject arenaRoot, InkGridConfigSO inkCfg, Material inkWallMat)
        {
            if (arenaRoot == null || (inkCfg != null && !inkCfg.enableWallInk))
                return 0;

            int added = 0;
            var colliders = arenaRoot.GetComponentsInChildren<BoxCollider>(false);
            for (int i = 0; i < colliders.Length; i++)
            {
                BoxCollider col = colliders[i];
                if (col == null || col.isTrigger)
                    continue;

                GameObject go = col.gameObject;

                // 白名单：地板本体顶面走图集，0.5m 侧沿整圈都在界外看不到
                if (NonPaintableArenaObjects.Contains(go.name))
                    continue;

                // 没有任何可视表面（纯拦截体）不需要墨面
                if (go.GetComponent<Renderer>() == null)
                    continue;

                // 出生台台阶：踏面已经通过 inkableRenderers 走顶视墨迹图集；
                // 每级踢面只有 0.24m 高，单独配 InkWallSurface 会在运行时多生成最多 20×4 张立面贴图。
                if (go.name.StartsWith("Ramp_SpawnStair_", System.StringComparison.Ordinal))
                    continue;

                // 已经装配过墨面（自身或祖先）
                if (go.GetComponent<InkWallSurface>() != null || go.GetComponentInParent<InkWallSurface>() != null)
                    continue;

                AddInkWallSurface(go, inkCfg, inkWallMat);
                added++;
            }
            return added;
        }

        /// <summary>不需要（或不该）自动补墨面的竞技场对象名单。</summary>
        private static readonly HashSet<string> NonPaintableArenaObjects = new HashSet<string>
        {
            "Ground_Main_40x56",
            "Ground_Main_40x40",
        };

        public static GameObject CreateCover(
            Transform parent,
            string name,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Collider> blockedColliders,
            float minAlpha = 0.35f,
            bool enableWallInk = true)
        {
            var go = CreateBox(parent, name, pos, scale, mat);
            var col = go.GetComponent<Collider>();
            if (col != null && blockedColliders != null)
            {
                blockedColliders.Add(col);
            }
            var fader = go.AddComponent<OcclusionFader>();
            fader.MinAlpha = minAlpha;
            if (enableWallInk)
            {
                AddInkWallSurface(go, inkCfg, inkWallMat);
            }
            return go;
        }

        /// <summary>
        /// 创建一组原点 180° 点对称的掩体（A 在传入坐标，B 在 (-x, y, -z)）。
        /// 仅用于 P7-B 新增结构，避免破坏旧对象命名与测试锚点。
        /// </summary>
        private static void CreateCoverPair(
            Transform parent,
            string namePrefix,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Collider> blockedColliders,
            float minAlpha,
            bool enableWallInk)
        {
            CreateCover(parent, namePrefix + "_A", pos, scale, mat, inkCfg, inkWallMat, blockedColliders, minAlpha, enableWallInk);
            CreateCover(parent, namePrefix + "_B", new Vector3(-pos.x, pos.y, -pos.z), scale, mat, inkCfg, inkWallMat, blockedColliders, minAlpha, enableWallInk);
        }

        /// <summary>
        /// 出生甲板两侧台阶：每级实体盒从地面叠到本级高度，5 级正好升到 1.2m 台面。
        /// 级高 0.24m ≤ CharacterController.stepOffset(0.30)，Bot 的 NavGrid 0.5m 格采样时
        /// 相邻级高差 0.24m < maxHeightStep(0.55) 也可正常上下。
        /// 顶面加入 inkableRenderers 走顶视墨迹图集；踢面不挂 InkWallSurface
        /// （每级一套立面贴图会新增 20×4 张运行时贴图，收益极低）。
        /// 命名以 "Ramp_" 开头，地面 AO 烘焙会把台阶顶面当作高台排除，避免阴影糊在踏面上。
        /// </summary>
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
                // i = 0 是最下一级；最高一级紧贴台面侧壁
                float height = rise * (i + 1);
                float outer = halfDeck + ArenaLayoutV2.SpawnStairRun - i * tread;
                float inner = outer - tread;
                var step = CreateBox(
                    parent,
                    name + "_Step_" + i,
                    new Vector3(sideSign * (inner + outer) * 0.5f, height * 0.5f, z),
                    new Vector3(tread, height, ArenaLayoutV2.SpawnStairWidth),
                    groundMat);
                if (inkableRenderers != null) inkableRenderers.Add(step.GetComponent<Renderer>());
            }
        }

        public static void AddInkWallSurface(GameObject box, InkGridConfigSO inkCfg, Material inkWallMat, bool allowClimb = true)
        {
            if (box == null || (inkCfg != null && !inkCfg.enableWallInk))
                return;

            var col = box.GetComponent<BoxCollider>();
            if (col == null)
            {
                var anyCol = box.GetComponent<Collider>();
                if (anyCol != null)
                {
                    var wall = box.AddComponent<InkWallSurface>();
                    wall.Configure(anyCol, inkWallMat, inkCfg, allowClimb);
                }
                return;
            }

            var wallBox = box.AddComponent<InkWallSurface>();
            wallBox.Configure(col, inkWallMat, inkCfg, allowClimb);
        }

        /// <summary>
        /// 按"变换后的盒子"而不是世界 AABB 生成可涂立面：斜坡、旋转过的箱体等。
        /// 省略 centerWorld/sizeWorld 时取 BoxCollider 的局部中心/尺寸乘以缩放；
        /// 需要把墨面限制在可见网格内（例如南边界矮墙）时显式传入世界中心与尺寸。
        /// </summary>
        public static void AddOrientedInkWallSurface(
            GameObject box,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            bool allowClimb = false,
            Vector3? centerWorld = null,
            Vector3? sizeWorld = null)
        {
            if (box == null || (inkCfg != null && !inkCfg.enableWallInk))
                return;

            var col = box.GetComponent<Collider>();
            if (col == null)
                return;

            Transform t = box.transform;
            var boxCol = col as BoxCollider;
            Vector3 size = sizeWorld ?? Vector3.Scale(boxCol != null ? boxCol.size : Vector3.one, t.lossyScale);
            Vector3 center = centerWorld ?? t.TransformPoint(boxCol != null ? boxCol.center : Vector3.zero);

            var wall = box.AddComponent<InkWallSurface>();
            wall.ConfigureOrientedBox(col, center, size, t.rotation, inkWallMat, inkCfg, allowClimb);
        }

        #region P30 场地视觉精修：结构装饰层（合并网格实现）
        /// <summary>
        /// 为既有玩法几何生成紧贴的"结构装饰层"（角柱、边轨、门框、压顶条、坡道警示条带、外侧扶壁柱等）。
        /// 严格遵守约束：无 Collider、无 InkWallSurface、凸出既有面 ≤ 4cm（警示条带 ≤ 2cm），
        /// 全部合并为 2 个 Renderer（金属/警示 + 混凝土），不破坏任何既有对象与测试断言。
        /// </summary>
        private static void BuildStructuralDecorations(GameObject arenaRoot, ArenaMaterialSet mats)
        {
            if (arenaRoot == null) return;

            var pal = new Palette(null);
            var metalBuf = new MeshBuffer();
            var concreteBuf = new MeshBuffer();

            // 1. 集装箱结构装饰（角柱、顶部边轨、底部裙边、端门与锁杆）
            BuildContainerDecorations(metalBuf, pal);

            // 2. 掩体墙与挡墙装饰（顶面压顶条 + 端部封头 + 花坛边框）
            BuildCoverWallDecorations(metalBuf, concreteBuf, pal);

            // 3. 中央高台、侧翼高台与坡道（边缘收边条、两侧斜纹警示条带、地面过渡缓冲条）
            BuildPlatformAndRampDecorations(metalBuf, concreteBuf, pal);

            // 4. 四周边界墙（顶面压顶条、外侧间隔扶壁柱、内侧局部墙脚警示斜纹）
            BuildBoundaryWallDecorations(metalBuf, concreteBuf, pal);

            // 5. 出生区（出生台外缘收边、楼梯侧板与防滑踏步沿条、网关门楣横梁与立柱包边）
            BuildSpawnAreaDecorations(metalBuf, pal);

            // 6. 中央女儿墙顶面小压顶条
            BuildParapetDecorations(metalBuf, pal);

            // 7. 合并网格并挂接至 arenaRoot（无碰撞、无墨面、仅渲染与阴影）
            Material metalMat = mats?.propMetal ?? mats?.steelDark ?? mats?.steel ?? mats?.wall;
            AttachDecorMesh(arenaRoot, "Decor_Structural_Metal", metalBuf.ToMesh("Decor_Structural_Metal_Mesh"), metalMat);

            Material concreteMat = mats?.propMatte ?? mats?.concrete ?? mats?.wall;
            AttachDecorMesh(arenaRoot, "Decor_Structural_Concrete", concreteBuf.ToMesh("Decor_Structural_Concrete_Mesh"), concreteMat);
        }

        private static void AttachDecorMesh(GameObject parent, string name, Mesh mesh, Material mat)
        {
            if (mesh == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.isStatic = true;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }

        private static void BuildContainerDecorations(MeshBuffer metalBuf, Palette pal)
        {
            // 侧翼集装箱 (4 柜)
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, -3.2f), new Vector3(2.2f, 1.8f, 4.6f), pal.ContainerTeal, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ContainerFlankX, 0.9f, 4.5f), new Vector3(2.2f, 1.8f, 3.8f), pal.ContainerRust, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, 3.2f), new Vector3(2.2f, 1.8f, 4.6f), pal.ContainerTeal, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ContainerFlankX, 0.9f, -4.5f), new Vector3(2.2f, 1.8f, 3.8f), pal.ContainerRust, pal);

            // 中场集装箱 (4 柜)
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ContainerMidX, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f), pal.ContainerMustard, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ContainerMidX, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f), pal.ContainerMustard, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ContainerMidX, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f), pal.ContainerSlate, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ContainerMidX, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f), pal.ContainerSlate, pal);

            // 延长区集装箱 (4 柜，2 对)
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 16.5f), new Vector3(2.4f, 1.8f, 4.6f), pal.ContainerTeal, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ExtensionContainerX, 0.9f, -16.5f), new Vector3(2.4f, 1.8f, 4.6f), pal.ContainerTeal, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(ArenaLayoutV2.ExtensionContainerX, 0.9f, 21.2f), new Vector3(2.4f, 1.8f, 4.6f), pal.ContainerRust, pal);
            AddContainerStructuralDecor(metalBuf, new Vector3(-ArenaLayoutV2.ExtensionContainerX, 0.9f, -21.2f), new Vector3(2.4f, 1.8f, 4.6f), pal.ContainerRust, pal);
        }

        private static void AddContainerStructuralDecor(
            MeshBuffer metalBuf,
            Vector3 center,
            Vector3 size,
            Color boxColor,
            Palette pal)
        {
            Vector3 c = center;
            float w = size.x;
            float h = size.y;
            float l = size.z;
            float d = 0.018f; // 凸出量 1.8cm ≤ 2cm 防 z-fight

            Color steelDark = pal.SteelDark;
            Color doorTrimColor = Color.Lerp(boxColor, steelDark, 0.35f);

            // 1. 四角竖向角柱（凸出立面 ≤ 2cm）
            float postW = 0.08f;
            float postD = 0.08f;
            float xOffset = w * 0.5f - postW * 0.5f + d;
            float zOffset = l * 0.5f - postD * 0.5f + d;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 postCenter = c + new Vector3(sx * xOffset, 0f, sz * zOffset);
                    metalBuf.AddBox(postCenter, new Vector3(postW, h + 0.01f, postD), steelDark);

                    // 顶底角铸件
                    metalBuf.AddBox(postCenter + new Vector3(0f, h * 0.5f - 0.045f, 0f), new Vector3(postW + 0.016f, 0.09f, postD + 0.016f), steelDark);
                    metalBuf.AddBox(postCenter - new Vector3(0f, h * 0.5f - 0.045f, 0f), new Vector3(postW + 0.016f, 0.09f, postD + 0.016f), steelDark);
                }
            }

            // 2. 顶部边轨圈 (Top rails)
            float innerL = l - postD * 2f;
            float innerW = w - postW * 2f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Vector3 railPos = c + new Vector3(sx * (w * 0.5f + d * 0.5f), h * 0.5f - 0.025f, 0f);
                metalBuf.AddBox(railPos, new Vector3(d, 0.05f, innerL), steelDark);
            }
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 railPos = c + new Vector3(0f, h * 0.5f - 0.03f, sz * (l * 0.5f + d * 0.5f));
                metalBuf.AddBox(railPos, new Vector3(innerW, 0.06f, d), steelDark);
            }

            // 3. 底部裙边条 (Bottom skirts)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Vector3 skirtPos = c + new Vector3(sx * (w * 0.5f + d * 0.5f), -h * 0.5f + 0.03f, 0f);
                metalBuf.AddBox(skirtPos, new Vector3(d, 0.06f, innerL), steelDark);
            }
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 skirtPos = c + new Vector3(0f, -h * 0.5f + 0.035f, sz * (l * 0.5f + d * 0.5f));
                metalBuf.AddBox(skirtPos, new Vector3(innerW, 0.07f, d), steelDark);
            }

            // 4. 两端短边端门（门框竖条 + 门缝横条 + 竖向锁杆）
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float zEnd = c.z + sz * (l * 0.5f + d);

                // 中央门缝竖条
                metalBuf.AddBox(new Vector3(c.x, c.y, zEnd), new Vector3(0.06f, h - 0.16f, 0.015f), doorTrimColor);

                // 横向加强筋条
                float ribSpan = innerW * 0.44f;
                float ribXOffset = innerW * 0.25f;
                for (int ry = -1; ry <= 1; ry += 2)
                {
                    float yRib = c.y + ry * (h * 0.22f);
                    metalBuf.AddBox(new Vector3(c.x - ribXOffset, yRib, zEnd), new Vector3(ribSpan, 0.045f, 0.015f), doorTrimColor);
                    metalBuf.AddBox(new Vector3(c.x + ribXOffset, yRib, zEnd), new Vector3(ribSpan, 0.045f, 0.015f), doorTrimColor);
                }

                // 2 根竖向锁杆与把手
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    float rodX = c.x + sx * 0.30f;
                    metalBuf.AddBox(new Vector3(rodX, c.y, zEnd + 0.008f), new Vector3(0.035f, h - 0.16f, 0.025f), steelDark);
                    // 门锁把手
                    metalBuf.AddBox(new Vector3(rodX - sx * 0.05f, c.y - 0.10f, zEnd + 0.022f), new Vector3(0.12f, 0.025f, 0.025f), steelDark);
                    // 锁杆固定座
                    metalBuf.AddBox(new Vector3(rodX, c.y + h * 0.38f, zEnd + 0.012f), new Vector3(0.05f, 0.04f, 0.03f), steelDark);
                    metalBuf.AddBox(new Vector3(rodX, c.y - h * 0.38f, zEnd + 0.012f), new Vector3(0.05f, 0.04f, 0.03f), steelDark);
                }
            }
        }

        private static void BuildCoverWallDecorations(MeshBuffer metalBuf, MeshBuffer concreteBuf, Palette pal)
        {
            Color concreteLight = pal.ConcreteLight;
            Color steelDark = pal.SteelDark;

            // 经典掩体墙 CoverWall_00 / CoverWall_01
            AddWallCopingAndEndCaps(metalBuf, concreteBuf,
                new Vector3(-ArenaLayoutV2.CoverWallX, 0.9f, -4.5f),
                new Vector3(ArenaLayoutV2.CoverWallWidth, 1.8f, 1.4f), concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf,
                new Vector3(ArenaLayoutV2.CoverWallX, 0.9f, 4.5f),
                new Vector3(ArenaLayoutV2.CoverWallWidth, 1.8f, 1.4f), concreteLight, steelDark);

            // 中路错位掩体 Cover_MidLane_Far / Near (A & B)
            Vector3 farPos = ArenaLayoutV2.MidCoverFarPos;
            Vector3 farSz = ArenaLayoutV2.MidCoverFarSize;
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, farPos, farSz, concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(-farPos.x, farPos.y, -farPos.z), farSz, concreteLight, steelDark);

            Vector3 nearPos = ArenaLayoutV2.MidCoverNearPos;
            Vector3 nearSz = ArenaLayoutV2.MidCoverNearSize;
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, nearPos, nearSz, concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(-nearPos.x, nearPos.y, -nearPos.z), nearSz, concreteLight, steelDark);

            // 双方出生前沿战术掩体墙 (Barrier Left/Right)
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(-4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f), concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f), concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(-4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f), concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f), concreteLight, steelDark);

            // 出口矮墙 Parapet_TeamA_Exit / Parapet_TeamB_Exit
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(0f, 0.5f, -13.2f), new Vector3(2.6f, 1.0f, 0.8f), concreteLight, steelDark);
            AddWallCopingAndEndCaps(metalBuf, concreteBuf, new Vector3(0f, 0.5f, 13.2f), new Vector3(2.6f, 1.0f, 0.8f), concreteLight, steelDark);

            // 花坛顶面收边框 (Central Planters & Extension Planters)
            AddPlanterRim(concreteBuf, new Vector3(-2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f), concreteLight);
            AddPlanterRim(concreteBuf, new Vector3(2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f), concreteLight);

            AddPlanterRim(concreteBuf, new Vector3(7.0f, 0.45f, 13.5f), new Vector3(1.8f, 0.9f, 1.8f), concreteLight);
            AddPlanterRim(concreteBuf, new Vector3(-7.0f, 0.45f, -13.5f), new Vector3(1.8f, 0.9f, 1.8f), concreteLight);
            AddPlanterRim(concreteBuf, new Vector3(7.0f, 0.45f, 19.0f), new Vector3(1.8f, 0.9f, 1.8f), concreteLight);
            AddPlanterRim(concreteBuf, new Vector3(-7.0f, 0.45f, -19.0f), new Vector3(1.8f, 0.9f, 1.8f), concreteLight);
        }

        private static void AddWallCopingAndEndCaps(
            MeshBuffer metalBuf,
            MeshBuffer concreteBuf,
            Vector3 center,
            Vector3 size,
            Color copingColor,
            Color endCapColor)
        {
            Vector3 c = center;
            float w = size.x;
            float h = size.y;
            float l = size.z;
            float topY = c.y + h * 0.5f;

            // 顶面压顶条 (Coping)
            concreteBuf.AddBox(new Vector3(c.x, topY + 0.015f, c.z), new Vector3(w + 0.04f, 0.03f, l + 0.04f), copingColor);

            // 端部封头 (End Caps)
            if (w >= l)
            {
                // X 为主跨度，端部在 X 两端
                float capW = 0.035f;
                metalBuf.AddBox(new Vector3(c.x - w * 0.5f - capW * 0.5f + 0.015f, c.y, c.z), new Vector3(capW, h, l + 0.02f), endCapColor);
                metalBuf.AddBox(new Vector3(c.x + w * 0.5f + capW * 0.5f - 0.015f, c.y, c.z), new Vector3(capW, h, l + 0.02f), endCapColor);
            }
            else
            {
                // Z 为主跨度，端部在 Z 两端
                float capL = 0.035f;
                metalBuf.AddBox(new Vector3(c.x, c.y, c.z - l * 0.5f - capL * 0.5f + 0.015f), new Vector3(w + 0.02f, h, capL), endCapColor);
                metalBuf.AddBox(new Vector3(c.x, c.y, c.z + l * 0.5f + capL * 0.5f - 0.015f), new Vector3(w + 0.02f, h, capL), endCapColor);
            }
        }

        private static void AddPlanterRim(MeshBuffer concreteBuf, Vector3 center, Vector3 size, Color rimColor)
        {
            Vector3 c = center;
            float w = size.x;
            float h = size.y;
            float l = size.z;
            float topY = c.y + h * 0.5f + 0.012f;
            float rimThickness = 0.12f;
            float rimHeight = 0.025f;

            // 4 边收口框
            concreteBuf.AddBox(new Vector3(c.x, topY, c.z - l * 0.5f + rimThickness * 0.5f), new Vector3(w, rimHeight, rimThickness), rimColor);
            concreteBuf.AddBox(new Vector3(c.x, topY, c.z + l * 0.5f - rimThickness * 0.5f), new Vector3(w, rimHeight, rimThickness), rimColor);
            concreteBuf.AddBox(new Vector3(c.x - w * 0.5f + rimThickness * 0.5f, topY, c.z), new Vector3(rimThickness, rimHeight, l - rimThickness * 2f), rimColor);
            concreteBuf.AddBox(new Vector3(c.x + w * 0.5f - rimThickness * 0.5f, topY, c.z), new Vector3(rimThickness, rimHeight, l - rimThickness * 2f), rimColor);
        }

        private static void BuildPlatformAndRampDecorations(MeshBuffer metalBuf, MeshBuffer concreteBuf, Palette pal)
        {
            Color concreteLight = pal.ConcreteLight;

            // 1. 中央争夺高台（10x10m，高 1.1m）边缘收边条
            // 4 个拐角区（避开四向 4.4m 坡道口，即 x, z 在 [-2.2, 2.2] 之外的外缘）
            float pTop = 1.1f + 0.012f;
            float curbH = 0.025f;
            float curbW = 0.08f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float xMid = sx * 3.6f;
                float segLen = 2.8f;
                concreteBuf.AddBox(new Vector3(xMid, pTop, -5f + curbW * 0.5f), new Vector3(segLen, curbH, curbW), concreteLight);
                concreteBuf.AddBox(new Vector3(xMid, pTop, 5f - curbW * 0.5f), new Vector3(segLen, curbH, curbW), concreteLight);
                concreteBuf.AddBox(new Vector3(-5f + curbW * 0.5f, pTop, xMid), new Vector3(curbW, curbH, segLen), concreteLight);
                concreteBuf.AddBox(new Vector3(5f - curbW * 0.5f, pTop, xMid), new Vector3(curbW, curbH, segLen), concreteLight);
            }

            // 2. 侧翼高台（长 9.0m，宽 4.5m，高 1.5m）内侧收边条
            float flankY = ArenaLayoutV2.FlankPlatformHeight + 0.012f;
            float flankX = ArenaLayoutV2.FlankPlatformX;
            concreteBuf.AddBox(new Vector3(-flankX + 2.25f - curbW * 0.5f, flankY, 0f), new Vector3(curbW, curbH, 9.0f), concreteLight);
            concreteBuf.AddBox(new Vector3(flankX - 2.25f + curbW * 0.5f, flankY, 0f), new Vector3(curbW, curbH, 9.0f), concreteLight);

            // 3. 中央四向斜坡与侧翼四向斜坡（侧缘斜纹警示条带 + 底端过渡缓冲条）
            AddRampDecor(metalBuf, new Vector3(0f, 0.48f, -7.4f), new Vector3(4.4f, 0.25f, 5.0f), Quaternion.Euler(-12.8f, 0f, 0f), true, pal);
            AddRampDecor(metalBuf, new Vector3(0f, 0.48f, 7.4f), new Vector3(4.4f, 0.25f, 5.0f), Quaternion.Euler(12.8f, 0f, 0f), true, pal);
            AddRampDecor(metalBuf, new Vector3(-7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), Quaternion.Euler(0f, 0f, 12.8f), false, pal);
            AddRampDecor(metalBuf, new Vector3(7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), Quaternion.Euler(0f, 0f, -12.8f), false, pal);

            float flankRampY = 0.645f;
            float flankRampZ = 7.85f;
            AddRampDecor(metalBuf, new Vector3(-flankX, flankRampY, -flankRampZ), new Vector3(3.6f, 0.25f, 6.8f), Quaternion.Euler(-12.5f, 0f, 0f), true, pal);
            AddRampDecor(metalBuf, new Vector3(-flankX, flankRampY, flankRampZ), new Vector3(3.6f, 0.25f, 6.8f), Quaternion.Euler(12.5f, 0f, 0f), true, pal);
            AddRampDecor(metalBuf, new Vector3(flankX, flankRampY, -flankRampZ), new Vector3(3.6f, 0.25f, 6.8f), Quaternion.Euler(-12.5f, 0f, 0f), true, pal);
            AddRampDecor(metalBuf, new Vector3(flankX, flankRampY, flankRampZ), new Vector3(3.6f, 0.25f, 6.8f), Quaternion.Euler(12.5f, 0f, 0f), true, pal);
        }

        private static void AddRampDecor(
            MeshBuffer metalBuf,
            Vector3 pos,
            Vector3 size,
            Quaternion rot,
            bool slopeAlongZ,
            Palette pal)
        {
            Matrix4x4 trs = Matrix4x4.TRS(pos, rot, Vector3.one);
            float w = size.x;
            float t = size.y;
            float l = size.z;
            float curbThick = 0.016f; // 凸出量 1.6cm ≤ 2cm
            float curbH = 0.07f;
            float stripeW = 0.22f;

            if (slopeAlongZ)
            {
                // 坡道纵向在 local Z，两侧在 local X = ±w/2
                Vector3 leftBotStart = new Vector3(-w * 0.5f - curbThick, -t * 0.5f, -l * 0.5f);
                Vector3 leftBotEnd = new Vector3(-w * 0.5f - curbThick, -t * 0.5f, l * 0.5f);
                AddDiagonalHazardQuadStrip(metalBuf, leftBotStart, leftBotEnd, curbH, true, stripeW, pal.HazardYellow, pal.HazardDark, trs);
                metalBuf.AddBox(new Vector3(-w * 0.5f - curbThick * 0.5f, t * 0.5f - 0.01f, 0f), new Vector3(curbThick, 0.02f, l), pal.SteelDark, trs);

                Vector3 rightBotStart = new Vector3(w * 0.5f + curbThick, -t * 0.5f, -l * 0.5f);
                Vector3 rightBotEnd = new Vector3(w * 0.5f + curbThick, -t * 0.5f, l * 0.5f);
                AddDiagonalHazardQuadStrip(metalBuf, rightBotStart, rightBotEnd, curbH, false, stripeW, pal.HazardYellow, pal.HazardDark, trs);
                metalBuf.AddBox(new Vector3(w * 0.5f + curbThick * 0.5f, t * 0.5f - 0.01f, 0f), new Vector3(curbThick, 0.02f, l), pal.SteelDark, trs);
            }
            else
            {
                // 坡道纵向在 local X，两侧在 local Z = ±l/2
                Vector3 frontBotStart = new Vector3(-w * 0.5f, -t * 0.5f, -l * 0.5f - curbThick);
                Vector3 frontBotEnd = new Vector3(w * 0.5f, -t * 0.5f, -l * 0.5f - curbThick);
                AddDiagonalHazardQuadStrip(metalBuf, frontBotStart, frontBotEnd, curbH, false, stripeW, pal.HazardYellow, pal.HazardDark, trs);
                metalBuf.AddBox(new Vector3(0f, t * 0.5f - 0.01f, -l * 0.5f - curbThick * 0.5f), new Vector3(w, 0.02f, curbThick), pal.SteelDark, trs);

                Vector3 backBotStart = new Vector3(-w * 0.5f, -t * 0.5f, l * 0.5f + curbThick);
                Vector3 backBotEnd = new Vector3(w * 0.5f, -t * 0.5f, l * 0.5f + curbThick);
                AddDiagonalHazardQuadStrip(metalBuf, backBotStart, backBotEnd, curbH, true, stripeW, pal.HazardYellow, pal.HazardDark, trs);
                metalBuf.AddBox(new Vector3(0f, t * 0.5f - 0.01f, l * 0.5f + curbThick * 0.5f), new Vector3(w, 0.02f, curbThick), pal.SteelDark, trs);
            }

            // 坡道底端地面缓冲过渡条 (贴地平放，厚度 1.5cm，带黄黑警示条)
            Vector3 worldBotLanding;
            Vector3 bufferSize;
            if (slopeAlongZ)
            {
                float zDir = (pos.z > 0f) ? 1f : -1f;
                float landingZ = pos.z + zDir * (l * 0.5f * Mathf.Cos(12.8f * Mathf.Deg2Rad) + 0.12f);
                worldBotLanding = new Vector3(pos.x, 0.008f, landingZ);
                bufferSize = new Vector3(w, 0.016f, 0.24f);
            }
            else
            {
                float xDir = (pos.x > 0f) ? 1f : -1f;
                float landingX = pos.x + xDir * (w * 0.5f * Mathf.Cos(12.8f * Mathf.Deg2Rad) + 0.12f);
                worldBotLanding = new Vector3(landingX, 0.008f, pos.z);
                bufferSize = new Vector3(0.24f, 0.016f, l);
            }
            metalBuf.AddBox(worldBotLanding, bufferSize, pal.SteelDark);

            int stripeCount = 8;
            if (slopeAlongZ)
            {
                float stepX = bufferSize.x / stripeCount;
                for (int s = 0; s < stripeCount; s++)
                {
                    Color sc = (s % 2 == 0) ? pal.HazardYellow : pal.HazardDark;
                    Vector3 stripPos = worldBotLanding + new Vector3(-bufferSize.x * 0.5f + (s + 0.5f) * stepX, 0.008f, 0f);
                    metalBuf.AddBox(stripPos, new Vector3(stepX * 0.85f, 0.004f, bufferSize.z * 0.8f), sc);
                }
            }
            else
            {
                float stepZ = bufferSize.z / stripeCount;
                for (int s = 0; s < stripeCount; s++)
                {
                    Color sc = (s % 2 == 0) ? pal.HazardYellow : pal.HazardDark;
                    Vector3 stripPos = worldBotLanding + new Vector3(0f, 0.008f, -bufferSize.z * 0.5f + (s + 0.5f) * stepZ);
                    metalBuf.AddBox(stripPos, new Vector3(bufferSize.x * 0.8f, 0.004f, stepZ * 0.85f), sc);
                }
            }
        }

        private static void BuildBoundaryWallDecorations(MeshBuffer metalBuf, MeshBuffer concreteBuf, Palette pal)
        {
            Color concreteLight = pal.ConcreteLight;
            Color yellow = pal.HazardYellow;
            Color dark = pal.HazardDark;

            // 1. 墙顶压顶檐口条 (Top copings)
            concreteBuf.AddBox(new Vector3(0f, ArenaLayoutV2.NorthBoundaryWallHeight + 0.035f, ArenaLayoutV2.BoundaryZ),
                new Vector3(42.2f, 0.07f, 1.08f), concreteLight);
            concreteBuf.AddBox(new Vector3(0f, 0.55f + 0.025f, -ArenaLayoutV2.BoundaryZ),
                new Vector3(42.2f, 0.05f, 1.08f), concreteLight);
            concreteBuf.AddBox(new Vector3(-ArenaLayoutV2.BoundaryX, 2.4f + 0.035f, 0f),
                new Vector3(1.08f, 0.07f, 58.2f), concreteLight);
            concreteBuf.AddBox(new Vector3(ArenaLayoutV2.BoundaryX, 2.4f + 0.035f, 0f),
                new Vector3(1.08f, 0.07f, 58.2f), concreteLight);

            // 2. 外侧间隔扶壁柱（间距 4m，浅混凝土色，凸出外侧）
            float westOuterX = -ArenaLayoutV2.BoundaryX - 0.5f - 0.10f;
            for (float z = -26f; z <= 26.1f; z += 4f)
            {
                concreteBuf.AddBox(new Vector3(westOuterX, 1.2f, z), new Vector3(0.20f, 2.4f, 0.40f), concreteLight);
            }
            float eastOuterX = ArenaLayoutV2.BoundaryX + 0.5f + 0.10f;
            for (float z = -26f; z <= 26.1f; z += 4f)
            {
                concreteBuf.AddBox(new Vector3(eastOuterX, 1.2f, z), new Vector3(0.20f, 2.4f, 0.40f), concreteLight);
            }
            float northOuterZ = ArenaLayoutV2.BoundaryZ + 0.5f + 0.10f;
            for (float x = -18f; x <= 18.1f; x += 4f)
            {
                concreteBuf.AddBox(new Vector3(x, ArenaLayoutV2.NorthBoundaryWallHeight * 0.5f, northOuterZ), new Vector3(0.40f, ArenaLayoutV2.NorthBoundaryWallHeight, 0.20f), concreteLight);
            }
            float southOuterZ = -ArenaLayoutV2.BoundaryZ - 0.5f - 0.08f;
            for (float x = -18f; x <= 18.1f; x += 4f)
            {
                concreteBuf.AddBox(new Vector3(x, 0.275f, southOuterZ), new Vector3(0.35f, 0.55f, 0.16f), concreteLight);
            }

            // 3. 内侧墙脚警示斜纹带（局部战术区，凸出立面 1.5cm ≤ 2cm，高 0.12m）
            Vector3 westBaseStart = new Vector3(-ArenaLayoutV2.BoundaryX + 0.5f + 0.015f, 0.01f, -8f);
            Vector3 westBaseEnd = new Vector3(-ArenaLayoutV2.BoundaryX + 0.5f + 0.015f, 0.01f, 8f);
            AddDiagonalHazardQuadStrip(metalBuf, westBaseStart, westBaseEnd, 0.12f, false, 0.25f, yellow, dark);

            Vector3 eastBaseStart = new Vector3(ArenaLayoutV2.BoundaryX - 0.5f - 0.015f, 0.01f, -8f);
            Vector3 eastBaseEnd = new Vector3(ArenaLayoutV2.BoundaryX - 0.5f - 0.015f, 0.01f, 8f);
            AddDiagonalHazardQuadStrip(metalBuf, eastBaseStart, eastBaseEnd, 0.12f, true, 0.25f, yellow, dark);

            Vector3 northBaseLeftStart = new Vector3(-18f, 0.01f, ArenaLayoutV2.BoundaryZ - 0.5f - 0.015f);
            Vector3 northBaseLeftEnd = new Vector3(-10f, 0.01f, ArenaLayoutV2.BoundaryZ - 0.5f - 0.015f);
            AddDiagonalHazardQuadStrip(metalBuf, northBaseLeftStart, northBaseLeftEnd, 0.12f, false, 0.25f, yellow, dark);

            Vector3 northBaseRightStart = new Vector3(10f, 0.01f, ArenaLayoutV2.BoundaryZ - 0.5f - 0.015f);
            Vector3 northBaseRightEnd = new Vector3(18f, 0.01f, ArenaLayoutV2.BoundaryZ - 0.5f - 0.015f);
            AddDiagonalHazardQuadStrip(metalBuf, northBaseRightStart, northBaseRightEnd, 0.12f, false, 0.25f, yellow, dark);

            Vector3 southBaseLeftStart = new Vector3(-18f, 0.01f, -ArenaLayoutV2.BoundaryZ + 0.5f + 0.015f);
            Vector3 southBaseLeftEnd = new Vector3(-10f, 0.01f, -ArenaLayoutV2.BoundaryZ + 0.5f + 0.015f);
            AddDiagonalHazardQuadStrip(metalBuf, southBaseLeftStart, southBaseLeftEnd, 0.12f, true, 0.25f, yellow, dark);

            Vector3 southBaseRightStart = new Vector3(10f, 0.01f, -ArenaLayoutV2.BoundaryZ + 0.5f + 0.015f);
            Vector3 southBaseRightEnd = new Vector3(18f, 0.01f, -ArenaLayoutV2.BoundaryZ + 0.5f + 0.015f);
            AddDiagonalHazardQuadStrip(metalBuf, southBaseRightStart, southBaseRightEnd, 0.12f, true, 0.25f, yellow, dark);
        }

        private static void BuildSpawnAreaDecorations(MeshBuffer metalBuf, Palette pal)
        {
            Color steelDark = pal.SteelDark;
            float deckW = ArenaLayoutV2.SpawnDeckWidth;
            float deckH = ArenaLayoutV2.SpawnDeckTopY;
            float terraceZ = ArenaLayoutV2.SpawnTerraceZ;
            float deckDepth = ArenaLayoutV2.SpawnDeckDepth;

            // 1. 出生甲板前沿收边工字钢梁 (Front curb beam)
            float frontZA = -terraceZ + deckDepth * 0.5f;
            metalBuf.AddBox(new Vector3(0f, deckH + 0.015f, frontZA), new Vector3(deckW + 0.04f, 0.03f, 0.08f), steelDark);
            metalBuf.AddBox(new Vector3(0f, deckH + 0.02f, frontZA), new Vector3(4.0f, 0.035f, 0.085f), pal.TeamAPad);

            float frontZB = terraceZ - deckDepth * 0.5f;
            metalBuf.AddBox(new Vector3(0f, deckH + 0.015f, frontZB), new Vector3(deckW + 0.04f, 0.03f, 0.08f), steelDark);
            metalBuf.AddBox(new Vector3(0f, deckH + 0.02f, frontZB), new Vector3(4.0f, 0.035f, 0.085f), pal.TeamBPad);

            // 2. 出生台阶两侧封闭侧板与踏步沿条 (4 座台阶)
            AddSpawnStairDetails(metalBuf, 1f, -1f, pal);
            AddSpawnStairDetails(metalBuf, -1f, -1f, pal);
            AddSpawnStairDetails(metalBuf, 1f, 1f, pal);
            AddSpawnStairDetails(metalBuf, -1f, 1f, pal);

            // 3. SpawnGate 门楣横梁与立柱包边 (4 块挡墙，做成门结构，高 ≤ 1.6m，不封堵 7m 出口)
            AddSpawnGateDetails(metalBuf, 5.0f, 21.4f, pal);
            AddSpawnGateDetails(metalBuf, -5.0f, 21.4f, pal);
            AddSpawnGateDetails(metalBuf, 5.0f, -21.4f, pal);
            AddSpawnGateDetails(metalBuf, -5.0f, -21.4f, pal);
        }

        private static void AddSpawnStairDetails(MeshBuffer metalBuf, float sideSign, float zSign, Palette pal)
        {
            float halfDeck = ArenaLayoutV2.SpawnDeckWidth * 0.5f;
            int steps = ArenaLayoutV2.SpawnStairSteps;
            float run = ArenaLayoutV2.SpawnStairRun;
            float tread = run / steps;
            float rise = ArenaLayoutV2.SpawnDeckTopY / steps;
            float z = zSign * ArenaLayoutV2.SpawnTerraceZ;
            float stairW = ArenaLayoutV2.SpawnStairWidth;
            Color steelDark = pal.SteelDark;

            // 踏步前沿防滑沿条 (Step nosings)
            for (int i = 0; i < steps; i++)
            {
                float height = rise * (i + 1);
                float outer = halfDeck + run - i * tread;
                Vector3 nosingPos = new Vector3(sideSign * (outer - 0.025f), height - 0.005f, z);
                metalBuf.AddBox(nosingPos, new Vector3(0.05f, 0.015f, stairW), steelDark);
                metalBuf.AddBox(nosingPos + new Vector3(sideSign * 0.02f, 0.002f, 0f), new Vector3(0.015f, 0.016f, stairW * 0.95f), pal.HazardYellow);
            }

            // 楼梯两侧封闭侧梁板 (Side Stringer Plates)
            float stringerThick = 0.03f;
            float zOuter1 = z - stairW * 0.5f - stringerThick * 0.5f;
            float zOuter2 = z + stairW * 0.5f + stringerThick * 0.5f;

            for (int s = 0; s < steps; s++)
            {
                float h = rise * (s + 1);
                float outer = halfDeck + run - s * tread;
                float inner = outer - tread;
                Vector3 segPos = new Vector3(sideSign * (inner + outer) * 0.5f, h * 0.5f, zOuter1);
                metalBuf.AddBox(segPos, new Vector3(tread, h + 0.04f, stringerThick), steelDark);

                Vector3 segPos2 = new Vector3(sideSign * (inner + outer) * 0.5f, h * 0.5f, zOuter2);
                metalBuf.AddBox(segPos2, new Vector3(tread, h + 0.04f, stringerThick), steelDark);
            }
        }

        private static void AddSpawnGateDetails(MeshBuffer metalBuf, float xPos, float zPos, Palette pal)
        {
            Vector3 c = new Vector3(xPos, 0.75f, zPos);
            Vector3 sz = new Vector3(3.0f, 1.5f, 1.2f);
            Color steelDark = pal.SteelDark;

            // 1. 门楣重型钢横梁 (Lintel beam，顶高 1.50 + 0.08 = 1.58m ≤ 1.6m)
            Vector3 beamCenter = new Vector3(c.x, 1.54f, c.z);
            metalBuf.AddBox(beamCenter, new Vector3(sz.x + 0.08f, 0.08f, sz.z + 0.06f), steelDark);

            // 横梁立面装饰黄黑斜纹警示带
            float facingZDir = (c.z > 0f) ? -1f : 1f;
            Vector3 hazardStart = beamCenter + new Vector3(-sz.x * 0.5f, -0.04f, facingZDir * (sz.z * 0.5f + 0.031f));
            Vector3 hazardEnd = beamCenter + new Vector3(sz.x * 0.5f, -0.04f, facingZDir * (sz.z * 0.5f + 0.031f));
            AddDiagonalHazardQuadStrip(metalBuf, hazardStart, hazardEnd, 0.08f, facingZDir > 0f, 0.22f, pal.HazardYellow, pal.HazardDark);

            // 2. 朝向 7m 出口内侧的立柱包边 (Inner Gate Post Casing)
            float innerX = (c.x > 0f) ? (c.x - sz.x * 0.5f + 0.03f) : (c.x + sz.x * 0.5f - 0.03f);
            Vector3 innerPostCenter = new Vector3(innerX, 0.75f, c.z);
            metalBuf.AddBox(innerPostCenter, new Vector3(0.06f, sz.y + 0.02f, sz.z + 0.04f), steelDark);

            // 3. 外角柱包边 (Outer Corner Posts)
            float outerX = (c.x > 0f) ? (c.x + sz.x * 0.5f - 0.03f) : (c.x - sz.x * 0.5f + 0.03f);
            metalBuf.AddBox(new Vector3(outerX, 0.75f, c.z - sz.z * 0.5f + 0.03f), new Vector3(0.06f, sz.y, 0.06f), steelDark);
            metalBuf.AddBox(new Vector3(outerX, 0.75f, c.z + sz.z * 0.5f - 0.03f), new Vector3(0.06f, sz.y, 0.06f), steelDark);
        }

        private static void BuildParapetDecorations(MeshBuffer metalBuf, Palette pal)
        {
            Color steelDark = pal.SteelDark;
            float topY = 1.35f + 0.25f + 0.015f; // 1.615m

            // 4 段 X 向女儿墙
            metalBuf.AddBox(new Vector3(-3.6f, topY, -4.85f), new Vector3(2.84f, 0.03f, 0.36f), steelDark);
            metalBuf.AddBox(new Vector3(3.6f, topY, -4.85f), new Vector3(2.84f, 0.03f, 0.36f), steelDark);
            metalBuf.AddBox(new Vector3(-3.6f, topY, 4.85f), new Vector3(2.84f, 0.03f, 0.36f), steelDark);
            metalBuf.AddBox(new Vector3(3.6f, topY, 4.85f), new Vector3(2.84f, 0.03f, 0.36f), steelDark);

            // 4 段 Z 向女儿墙
            metalBuf.AddBox(new Vector3(-4.85f, topY, -3.6f), new Vector3(0.36f, 0.03f, 2.84f), steelDark);
            metalBuf.AddBox(new Vector3(-4.85f, topY, 3.6f), new Vector3(0.36f, 0.03f, 2.84f), steelDark);
            metalBuf.AddBox(new Vector3(4.85f, topY, -3.6f), new Vector3(0.36f, 0.03f, 2.84f), steelDark);
            metalBuf.AddBox(new Vector3(4.85f, topY, 3.6f), new Vector3(0.36f, 0.03f, 2.84f), steelDark);
        }

        private static void AddDiagonalHazardQuadStrip(
            MeshBuffer buf,
            Vector3 baseStart,
            Vector3 baseEnd,
            float height,
            bool flipNormal,
            float stripeWidth,
            Color yellow,
            Color dark,
            Matrix4x4? trs = null)
        {
            Vector3 span = baseEnd - baseStart;
            float totalLen = span.magnitude;
            if (totalLen < 0.01f) return;
            Vector3 dir = span / totalLen;
            int segs = Mathf.Max(2, Mathf.RoundToInt(totalLen / stripeWidth));
            float step = totalLen / segs;
            float skew = height * 0.7f;

            for (int i = 0; i < segs; i++)
            {
                float s0 = i * step;
                float s1 = (i + 1) * step;

                Vector3 p0 = baseStart + dir * s0;
                Vector3 p1 = baseStart + dir * s1;
                Vector3 t0 = p0 + Vector3.up * height + dir * skew;
                Vector3 t1 = p1 + Vector3.up * height + dir * skew;

                Color c = (i % 2 == 0) ? yellow : dark;
                if (!flipNormal)
                {
                    buf.AddQuad(p0, p1, t1, t0, c, trs);
                }
                else
                {
                    buf.AddQuad(p1, p0, t0, t1, c, trs);
                }
            }
        }

        private readonly struct Palette
        {
            public readonly Color Steel;
            public readonly Color SteelDark;
            public readonly Color ConcreteLight;
            public readonly Color ConcreteMid;
            public readonly Color ConcreteDark;
            public readonly Color ContainerTeal;
            public readonly Color ContainerRust;
            public readonly Color ContainerSlate;
            public readonly Color ContainerMustard;
            public readonly Color HazardYellow;
            public readonly Color HazardDark;
            public readonly Color TeamAPad;
            public readonly Color TeamBPad;

            public Palette(ArenaStyleSO s)
            {
                Steel = s != null ? s.steel : new Color(0.42f, 0.47f, 0.56f, 1f);
                SteelDark = s != null ? s.steelDark : new Color(0.24f, 0.27f, 0.32f, 1f);
                ConcreteLight = s != null ? s.concreteLight : new Color(0.80f, 0.78f, 0.72f, 1f);
                ConcreteMid = s != null ? s.concreteMid : new Color(0.64f, 0.62f, 0.57f, 1f);
                ConcreteDark = s != null ? s.concreteDark : new Color(0.42f, 0.41f, 0.38f, 1f);
                ContainerTeal = s != null ? s.containerTeal : new Color(0.22f, 0.56f, 0.54f, 1f);
                ContainerRust = s != null ? s.containerRust : new Color(0.68f, 0.32f, 0.22f, 1f);
                ContainerSlate = s != null ? s.containerSlate : new Color(0.36f, 0.47f, 0.62f, 1f);
                ContainerMustard = s != null ? s.containerMustard : new Color(0.85f, 0.64f, 0.22f, 1f);
                HazardYellow = s != null ? s.hazardYellow : new Color(0.86f, 0.68f, 0.20f, 1f);
                HazardDark = s != null ? s.hazardDark : new Color(0.16f, 0.16f, 0.16f, 1f);
                TeamAPad = s != null ? s.teamAPad : new Color(1.00f, 0.48f, 0.08f, 1f);
                TeamBPad = s != null ? s.teamBPad : new Color(0.15f, 0.35f, 1.00f, 1f);
            }
        }

        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(2048);
            public readonly List<Vector3> Normals = new List<Vector3>(2048);
            public readonly List<Vector2> UVs = new List<Vector2>(2048);
            public readonly List<Color> Colors = new List<Color>(2048);
            public readonly List<int> Triangles = new List<int>(3072);

            public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color color, Matrix4x4? trs = null)
            {
                if (trs.HasValue)
                {
                    v0 = trs.Value.MultiplyPoint3x4(v0);
                    v1 = trs.Value.MultiplyPoint3x4(v1);
                    v2 = trs.Value.MultiplyPoint3x4(v2);
                    v3 = trs.Value.MultiplyPoint3x4(v3);
                }

                Vector3 normal = Vector3.Cross(v3 - v0, v2 - v0).normalized;
                int start = Vertices.Count;

                Vertices.Add(v0); Vertices.Add(v1); Vertices.Add(v2); Vertices.Add(v3);
                Normals.Add(normal); Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
                UVs.Add(new Vector2(0f, 0f)); UVs.Add(new Vector2(1f, 0f));
                UVs.Add(new Vector2(1f, 1f)); UVs.Add(new Vector2(0f, 1f));
                Colors.Add(color); Colors.Add(color); Colors.Add(color); Colors.Add(color);

                Triangles.Add(start); Triangles.Add(start + 3); Triangles.Add(start + 2);
                Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 1);
            }

            public void AddBox(Vector3 center, Vector3 size, Color color, Matrix4x4? trs = null)
            {
                Vector3 h = size * 0.5f;
                Vector3 c = center;

                // +Y
                AddQuad(c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(h.x, h.y, -h.z),
                        c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z), color, trs);
                // -Y
                AddQuad(c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(h.x, -h.y, h.z),
                        c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(-h.x, -h.y, -h.z), color, trs);
                // +Z
                AddQuad(c + new Vector3(h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, h.z),
                        c + new Vector3(-h.x, h.y, h.z), c + new Vector3(h.x, h.y, h.z), color, trs);
                // -Z
                AddQuad(c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z),
                        c + new Vector3(h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, -h.z), color, trs);
                // +X
                AddQuad(c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, h.z),
                        c + new Vector3(h.x, h.y, h.z), c + new Vector3(h.x, h.y, -h.z), color, trs);
                // -X
                AddQuad(c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, -h.z),
                        c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, h.z), color, trs);
            }

            public Mesh ToMesh(string meshName)
            {
                if (Vertices.Count == 0) return null;
                var mesh = new Mesh { name = meshName };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, UVs);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
        #endregion
    }
}
