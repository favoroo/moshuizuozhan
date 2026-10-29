using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 40x40m 对称竞技场几何装配器（P5-B）：
    /// 纯代码装配 10x10m 中央高台 (高 1.1m ≤ 1.2m)、四向平缓斜坡 (12.8°)、
    /// 东西两侧侧翼高台 (高 0.9m) 与双向斜坡 (12.5°)、
    /// 外侧绕后战术走廊与集装箱掩体群、双方出生安全台与边界护栏。
    /// 所有掩体自动挂载 OcclusionFader (minAlpha 0.35) 与 InkWallSurface。
    /// </summary>
    public static class ArenaGeometryBuilder
    {
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
            var arenaRoot = new GameObject("Arena_40x40");

            // 1. 主地面 40x40m
            var mainFloor = CreateBox(arenaRoot.transform, "Ground_Main_40x40", new Vector3(0f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f), groundMat);
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

            // 北斜坡 (Team B 进攻主道)
            var northRamp = CreateBox(arenaRoot.transform, "Ramp_North_Inkable", new Vector3(0f, 0.48f, 7.4f), new Vector3(4.4f, 0.25f, 5.0f), groundMat);
            northRamp.transform.rotation = Quaternion.Euler(12.8f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(northRamp.GetComponent<Renderer>());
            northRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            // 西斜坡 (西侧翼切入高台)
            var westRamp = CreateBox(arenaRoot.transform, "Ramp_West_Inkable", new Vector3(-7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), groundMat);
            westRamp.transform.rotation = Quaternion.Euler(0f, 0f, 12.8f);
            if (inkableRenderers != null) inkableRenderers.Add(westRamp.GetComponent<Renderer>());
            westRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            // 东斜坡 (东侧翼切入高台)
            var eastRamp = CreateBox(arenaRoot.transform, "Ramp_East_Inkable", new Vector3(7.4f, 0.48f, 0f), new Vector3(5.0f, 0.25f, 4.4f), groundMat);
            eastRamp.transform.rotation = Quaternion.Euler(0f, 0f, -12.8f);
            if (inkableRenderers != null) inkableRenderers.Add(eastRamp.GetComponent<Renderer>());
            eastRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            // 中央高台低矮掩体花坛（高 0.8m，置于高台两侧，保持原点 (0,0) 高台争夺圈开阔可涂）
            CreateCover(arenaRoot.transform, "Cover_Center_West_Planter", new Vector3(-2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Cover_Center_East_Planter", new Vector3(2.6f, 1.5f, 0f), new Vector3(1.6f, 0.8f, 1.6f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            // 3. 东西两侧侧翼高台（高 0.9m）与双向斜坡
            var westPlatform = CreateBox(arenaRoot.transform, "HighGround_West", new Vector3(-13.5f, 0.45f, 0f), new Vector3(4.5f, 0.9f, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(westPlatform.GetComponent<Renderer>());
            westPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(westPlatform, inkCfg, inkWallMat);

            var westSouthRamp = CreateBox(arenaRoot.transform, "Ramp_West_South_Inkable", new Vector3(-13.5f, 0.40f, -6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            westSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westSouthRamp.GetComponent<Renderer>());
            westSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            var westNorthRamp = CreateBox(arenaRoot.transform, "Ramp_West_North_Inkable", new Vector3(-13.5f, 0.40f, 6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            westNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(westNorthRamp.GetComponent<Renderer>());
            westNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            var eastPlatform = CreateBox(arenaRoot.transform, "HighGround_East", new Vector3(13.5f, 0.45f, 0f), new Vector3(4.5f, 0.9f, 9.0f), groundMat);
            if (inkableRenderers != null) inkableRenderers.Add(eastPlatform.GetComponent<Renderer>());
            eastPlatform.AddComponent<OcclusionFader>().MinAlpha = 0.35f;
            AddInkWallSurface(eastPlatform, inkCfg, inkWallMat);

            var eastSouthRamp = CreateBox(arenaRoot.transform, "Ramp_East_South_Inkable", new Vector3(13.5f, 0.40f, -6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            eastSouthRamp.transform.rotation = Quaternion.Euler(-12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastSouthRamp.GetComponent<Renderer>());
            eastSouthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            var eastNorthRamp = CreateBox(arenaRoot.transform, "Ramp_East_North_Inkable", new Vector3(13.5f, 0.40f, 6.6f), new Vector3(3.6f, 0.25f, 4.4f), groundMat);
            eastNorthRamp.transform.rotation = Quaternion.Euler(12.5f, 0f, 0f);
            if (inkableRenderers != null) inkableRenderers.Add(eastNorthRamp.GetComponent<Renderer>());
            eastNorthRamp.AddComponent<OcclusionFader>().MinAlpha = 0.35f;

            // 4. 外侧战术绕后走廊与集装箱掩体群
            CreateCover(arenaRoot.transform, "Container_Flank_West_01_Teal", new Vector3(-17.5f, 0.9f, -3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Container_Flank_West_02_Rust", new Vector3(-17.5f, 0.9f, 4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            CreateCover(arenaRoot.transform, "Container_Flank_East_01_Teal", new Vector3(17.5f, 0.9f, 3.2f), new Vector3(2.2f, 1.8f, 4.6f),
                containerTealMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Container_Flank_East_02_Rust", new Vector3(17.5f, 0.9f, -4.5f), new Vector3(2.2f, 1.8f, 3.8f),
                containerRustMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            // 5. 中场两侧战术集装箱与经典掩体墙（兼容现有 WallInkPlayboxTests）
            CreateCover(arenaRoot.transform, "CoverWall_00", new Vector3(-6.5f, 0.9f, -4.5f), new Vector3(2.6f, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "CoverWall_01", new Vector3(6.5f, 0.9f, 4.5f), new Vector3(2.6f, 1.8f, 1.4f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            CreateCover(arenaRoot.transform, "Container_Mid_SW_Mustard", new Vector3(-9.2f, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Container_Mid_NE_Mustard", new Vector3(9.2f, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerMustardMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            CreateCover(arenaRoot.transform, "Container_Mid_SE_Slate", new Vector3(8.0f, 0.9f, -5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerSlateMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Container_Mid_NW_Slate", new Vector3(-8.0f, 0.9f, 5.5f), new Vector3(2.2f, 1.8f, 4.2f),
                containerSlateMat ?? wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            // 6. 双方出生前沿战术掩体墙
            CreateCover(arenaRoot.transform, "Barrier_TeamA_Left", new Vector3(-4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Barrier_TeamA_Right", new Vector3(4.5f, 0.75f, -11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Parapet_TeamA_Exit", new Vector3(0f, 0.5f, -13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            CreateCover(arenaRoot.transform, "Barrier_TeamB_Right", new Vector3(4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Barrier_TeamB_Left", new Vector3(-4.5f, 0.75f, 11.2f), new Vector3(2.8f, 1.5f, 1.2f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);
            CreateCover(arenaRoot.transform, "Parapet_TeamB_Exit", new Vector3(0f, 0.5f, 13.2f), new Vector3(2.6f, 1.0f, 0.8f),
                wallMat, inkCfg, inkWallMat, blockedColliders, 0.35f);

            // 7. 四周边界围墙
            var southWall = CreateBox(arenaRoot.transform, "Boundary_South", new Vector3(0f, 0.175f, -20.5f), new Vector3(42f, 0.35f, 1f), boundaryMat);
            var southCol = southWall.GetComponent<BoxCollider>();
            if (southCol != null)
            {
                southCol.center = new Vector3(0f, 1.3f, 0f);
                southCol.size = new Vector3(42f, 3.0f, 1f);
            }
            CreateBox(arenaRoot.transform, "Boundary_North", new Vector3(0f, 1.2f, 20.5f), new Vector3(42f, 2.4f, 1f), boundaryMat);
            CreateBox(arenaRoot.transform, "Boundary_West", new Vector3(-20.5f, 1.2f, 0f), new Vector3(1f, 2.4f, 42f), boundaryMat);
            CreateBox(arenaRoot.transform, "Boundary_East", new Vector3(20.5f, 1.2f, 0f), new Vector3(1f, 2.4f, 42f), boundaryMat);

            // 8. 双方安全出生台
            var spawnPadA = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamA", new Vector3(0f, 0.08f, -15.5f), new Vector3(6.0f, 0.16f, 4.0f), spawnMatA);
            if (blockedColliders != null) blockedColliders.Add(spawnPadA.GetComponent<Collider>());

            var spawnPadB = CreateBox(arenaRoot.transform, "SafeSpawnPad_TeamB", new Vector3(0f, 0.08f, 15.5f), new Vector3(6.0f, 0.16f, 4.0f), spawnMatB);
            if (blockedColliders != null) blockedColliders.Add(spawnPadB.GetComponent<Collider>());

            var spawnAnchorGo = new GameObject("TeamA_SpawnAnchor");
            spawnAnchorGo.transform.SetParent(arenaRoot.transform, false);
            spawnAnchorGo.transform.position = new Vector3(0f, 0.2f, -13.0f);
            teamASpawnAnchor = spawnAnchorGo.transform;

            return arenaRoot;
        }

        public static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (mat != null)
            {
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }
            return go;
        }

        public static GameObject CreateCover(
            Transform parent,
            string name,
            Vector3 pos,
            Vector3 scale,
            Material mat,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Collider> blockedColliders,
            float minAlpha = 0.35f)
        {
            var go = CreateBox(parent, name, pos, scale, mat);
            var col = go.GetComponent<Collider>();
            if (col != null && blockedColliders != null)
            {
                blockedColliders.Add(col);
            }
            var fader = go.AddComponent<OcclusionFader>();
            fader.MinAlpha = minAlpha;
            AddInkWallSurface(go, inkCfg, inkWallMat);
            return go;
        }

        public static void AddInkWallSurface(GameObject box, InkGridConfigSO inkCfg, Material inkWallMat)
        {
            if (box == null || inkCfg == null || !inkCfg.enableWallInk)
                return;

            var col = box.GetComponent<BoxCollider>();
            if (col == null)
                return;

            var wall = box.AddComponent<InkWallSurface>();
            wall.Configure(col, inkWallMat, inkCfg);
        }
    }
}
