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

            // 3. 东西两侧侧翼高台（高 0.9m）与双向斜坡
            var westPlatform = CreateBox(arenaRoot.transform, "HighGround_West", new Vector3(-13.5f, 0.45f, 0f), new Vector3(4.5f, 0.9f, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(westPlatform.GetComponent<Renderer>());
            westPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(westPlatform, inkCfg, inkWallMat);

            var westSouthRamp = CreateBox(arenaRoot.transform, "Ramp_West_South_Inkable", new Vector3(-13.5f, 0.40f, -6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            westSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westSouthRamp.GetComponent<Renderer>());
            westSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(westSouthRamp, inkCfg, inkWallMat);

            var westNorthRamp = CreateBox(arenaRoot.transform, "Ramp_West_North_Inkable", new Vector3(-13.5f, 0.40f, 6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            westNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westNorthRamp.GetComponent<Renderer>());
            westNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(westNorthRamp, inkCfg, inkWallMat);

            var eastPlatform = CreateBox(arenaRoot.transform, "HighGround_East", new Vector3(13.5f, 0.45f, 0f), new Vector3(4.5f, 0.9f, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(eastPlatform.GetComponent<Renderer>());
            eastPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(eastPlatform, inkCfg, inkWallMat);

            var eastSouthRamp = CreateBox(arenaRoot.transform, "Ramp_East_South_Inkable", new Vector3(13.5f, 0.40f, -6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            eastSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastSouthRamp.GetComponent<Renderer>());
            eastSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(eastSouthRamp, inkCfg, inkWallMat);

            var eastNorthRamp = CreateBox(arenaRoot.transform, "Ramp_East_North_Inkable", new Vector3(13.5f, 0.40f, 6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            eastNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastNorthRamp.GetComponent<Renderer>());
            eastNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddOrientedInkWallSurface(eastNorthRamp, inkCfg, inkWallMat);

            // 4. 外侧战术绕后走廊与集装箱掩体群（保持旧坐标，测试锚点）
            CreateCover(arenaRoot.transform, "Container_Flank_West_01_Teal", new Vector3(-17.5f, 0.9f, -3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Flank_West_02_Rust", new Vector3(-17.5f, 0.9f, 4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Flank_East_01_Teal", new Vector3(17.5f, 0.9f, 3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Flank_East_02_Rust", new Vector3(17.5f, 0.9f, -4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 5. 中场两侧战术集装箱与经典掩体墙（兼容现有 WallInkPlayboxTests）
            CreateCover(arenaRoot.transform, "CoverWall_00", new Vector3(-6.5f, 0.9f, -4.5f), new Vector3(2.6f, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "CoverWall_01", new Vector3(6.5f, 0.9f, 4.5f), new Vector3(2.6f, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Mid_SW_Mustard", new Vector3(-9.2f, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Mid_NE_Mustard", new Vector3(9.2f, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            CreateCover(arenaRoot.transform, "Container_Mid_SE_Slate", new Vector3(8.0f, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerSlateMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCover(arenaRoot.transform, "Container_Mid_NW_Slate", new Vector3(-8.0f, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
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
            // 7.1 出生网关：z=±21.4，两侧 1.5m 挡墙，中间留 7m 出口
            CreateCoverPair(arenaRoot.transform, "SpawnGate", new Vector3(5.0f, 0.75f, 21.4f), new Vector3(3.0f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.2 延长区集装箱：每半场两组，形成外侧 2.2m 高掩体与绕后路线
            CreateCoverPair(arenaRoot.transform, "Container_Extension_Teal", new Vector3(15.5f, 0.9f, 16.5f), new Vector3(2.4f, 1.8f, 4.6f),
                containerTealMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Container_Extension_Rust", new Vector3(16.5f, 0.9f, 21.5f), new Vector3(2.4f, 1.8f, 4.6f),
                containerRustMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.3 延长区花坛与木箱：低矮掩体，保持中路快速推进的节奏
            CreateCoverPair(arenaRoot.transform, "Planter_Extension", new Vector3(7.0f, 0.45f, 13.5f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Planter_Extension_Deep", new Vector3(7.0f, 0.45f, 19.0f), new Vector3(1.8f, 0.9f, 1.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Crate_Extension", new Vector3(11.5f, 0.7f, 19.5f), new Vector3(1.6f, 1.4f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);
            CreateCoverPair(arenaRoot.transform, "Crate_Extension_Outer", new Vector3(10.0f, 0.7f, 22.5f), new Vector3(1.6f, 1.4f, 1.6f),
                woodMat, inkCfg, inkWallMat, blockedColliders, 0.35f, true);

            // 7.4 出生区后景台：z=±27，14×1.2×2.6，纯视觉后靠背，不参与出生点碰撞
            var terraceA = CreateBox(arenaRoot.transform, "SpawnTerrace_TeamA", new Vector3(0f, 0.6f, -27.0f), new Vector3(14f, 1.2f, 2.6f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceA.GetComponent<Renderer>());
            terraceA.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(terraceA, inkCfg, inkWallMat, true);

            var terraceB = CreateBox(arenaRoot.transform, "SpawnTerrace_TeamB", new Vector3(0f, 0.6f, 27.0f), new Vector3(14f, 1.2f, 2.6f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(terraceB.GetComponent<Renderer>());
            terraceB.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(terraceB, inkCfg, inkWallMat, true);

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

            var northWall = CreateBox(arenaRoot.transform, "Boundary_North", new Vector3(0f, 1.2f, ArenaLayoutV2.BoundaryZ), new Vector3(42f, 2.4f, 1f), boundaryMat);
            AddInkWallSurface(northWall, inkCfg, inkWallMat, false);

            var westWall = CreateBox(arenaRoot.transform, "Boundary_West", new Vector3(-ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            AddInkWallSurface(westWall, inkCfg, inkWallMat, false);

            var eastWall = CreateBox(arenaRoot.transform, "Boundary_East", new Vector3(ArenaLayoutV2.BoundaryX, 1.2f, 0f), new Vector3(1f, 2.4f, 58f), boundaryMat);
            AddInkWallSurface(eastWall, inkCfg, inkWallMat, false);

            // 9. 双方安全出生台（8×6.5m，队色圆盘由 P1/SpawnPad Shader 绘制）
            // 出生台也属于场上表面：顶面由 SpawnPad.shader 采样顶视墨迹图集，因此不再加入
            // blockedColliders（否则网格层面会拒绝上墨，打上去什么都不会发生）。
            var spawnPadA = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamA", new Vector3(0f, 0.08f, -ArenaLayoutV2.SpawnZ), new Vector3(8.0f, 0.16f, 6.5f), spawnMatA);
            AddInkWallSurface(spawnPadA, inkCfg, inkWallMat, true);

            var spawnPadB = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamB", new Vector3(0f, 0.08f, ArenaLayoutV2.SpawnZ), new Vector3(8.0f, 0.16f, 6.5f), spawnMatB);
            AddInkWallSurface(spawnPadB, inkCfg, inkWallMat, true);

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(arenaRoot.transform, false);
            spawnAnchorGo.transform.position = new Vector3(0f, 0.2f, -ArenaLayoutV2.SpawnAnchorZ);
            teamASpawnAnchor = spawnAnchorGo.transform;

            // 9.5 覆盖率兜底：把漏装墨面的可视碰撞盒补全（默认全部可涂）
            EnsurePaintableArena(arenaRoot, inkCfg, inkWallMat);

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
    }
}
