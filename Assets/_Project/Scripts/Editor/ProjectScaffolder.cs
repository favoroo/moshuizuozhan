using System.Collections.Generic;
using P1.AI;
using P1.Core;
using P1.Environment;
using P1.Ink;
using P1.Player;
using P1.UI;
using P1.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace P1.Editor
{
    /// <summary>
    /// 一键生成项目默认配置（ScriptableObject）、材质与第一阶段单人手感试玩场景（Phase A Playbox）。
    /// 遵循 AGENTS.md 规则 1：严禁手改 .unity/.prefab/.asset YAML，全部由本脚本确定性构建。
    /// </summary>
    public static class ProjectScaffolder
    {
        private const string SoFolder = "Assets/_Project/ScriptableObjects";
        private const string MatFolder = "Assets/_Project/Materials";
        private const string ScenePath = "Assets/_Project/Scenes/PhaseA_Playbox.unity";

        [MenuItem("P1/构建/1. 生成默认配置与材质 (SO & Materials)", false, 10)]
        public static void GenerateConfigsAndMaterials()
        {
            EnsureFolders();

            var inkCfg = LoadOrCreateSO<InkGridConfigSO>($"{SoFolder}/InkGridConfig.asset");
            var playerCfg = LoadOrCreateSO<PlayerConfigSO>($"{SoFolder}/PlayerConfig.asset");
            var weaponCfg = LoadOrCreateSO<WeaponConfigSO>($"{SoFolder}/WeaponConfig.asset");
            var gameCfg = LoadOrCreateSO<GameConfigSO>($"{SoFolder}/GameConfig.asset");
            var botCfg = LoadOrCreateSO<BotConfigSO>($"{SoFolder}/BotConfig.asset");

            // 同步最新紧凑参数到资产
            weaponCfg.maxRange = 7.2f;
            weaponCfg.trailDropDistance = 1.05f;
            weaponCfg.projectileSpeed = 21f;
            weaponCfg.bombMaxThrowDistance = 7.5f;
            EditorUtility.SetDirty(weaponCfg);

            gameCfg.usePerspective = true;
            gameCfg.cameraFov = 40f;
            gameCfg.cameraOrthoSize = 4.4f;
            gameCfg.cameraOffset = new Vector3(0f, 9.2f, -8.2f);
            gameCfg.cameraLookAheadMax = 1.5f;
            gameCfg.inkGridConfig = inkCfg;
            gameCfg.playerConfig = playerCfg;
            gameCfg.weaponConfig = weaponCfg;
            EditorUtility.SetDirty(gameCfg);

            botCfg.sightRange = 8.5f;
            botCfg.keepDistance = 4.2f;
            botCfg.paintAimRange = 6.0f;
            botCfg.bombRangeMax = 7.0f;
            botCfg.specialSearchRange = 5.5f;
            EditorUtility.SetDirty(botCfg);

            CreateOrUpdateGroundMaterial();
            // 可涂墙面：透明墨水叠加层，未涂区域保持原水泥墙外观
            CreateOrUpdateInkWallMaterial($"{MatFolder}/Mat_InkWall.mat");
            // 掩体墙与集装箱：支持 Screen-Door Dither 遮挡剔除与工业瓦楞条纹
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_ObstacleWall.mat", new Color(0.70f, 0.73f, 0.78f, 1f), new Color(0.48f, 0.52f, 0.58f, 1f), 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Teal.mat", new Color(0.37f, 0.62f, 0.63f, 1f), new Color(0.24f, 0.42f, 0.44f, 1f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Rust.mat", new Color(0.77f, 0.48f, 0.37f, 1f), new Color(0.52f, 0.28f, 0.20f, 1f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Slate.mat", new Color(0.56f, 0.60f, 0.63f, 1f), new Color(0.36f, 0.40f, 0.44f, 1f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Mustard.mat", new Color(0.84f, 0.68f, 0.32f, 1f), new Color(0.58f, 0.44f, 0.18f, 1f), 1.0f);
            CreateOrUpdateUnlitMaterial($"{MatFolder}/Mat_BoundaryWall.mat", new Color(0.38f, 0.40f, 0.46f, 1f));
            CreateOrUpdateUnlitMaterial($"{MatFolder}/Mat_SpawnPad_TeamA.mat", new Color(0.42f, 0.32f, 0.22f, 1f));
            CreateOrUpdateUnlitMaterial($"{MatFolder}/Mat_SpawnPad_TeamB.mat", new Color(0.20f, 0.28f, 0.42f, 1f));
            CreateOrUpdateLitMaterial($"{MatFolder}/Mat_TeamA_Body.mat", new Color(1.00f, 0.45f, 0.08f, 1f));
            CreateOrUpdateLitMaterial($"{MatFolder}/Mat_TeamB_Body.mat", new Color(0.16f, 0.42f, 1.00f, 1f));
            CreateOrUpdateUnlitMaterial($"{MatFolder}/Mat_VisorDark.mat", new Color(0.08f, 0.09f, 0.12f, 1f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectScaffolder] 默认 ScriptableObjects 与材质已生成完毕。");
        }

        [MenuItem("P1/构建/2. 一键生成并打开第一阶段试玩场景 (Phase A Playbox)", false, 11)]
        public static void BuildPhaseAPlayboxScene()
        {
            GenerateConfigsAndMaterials();
            ApplyProjectPlayerSettings();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 注意：资产必须在 NewScene 之后再加载。GenerateConfigsAndMaterials 内部的
            // AssetDatabase.Refresh 会重导入资产，使先前 LoadAssetAtPath 得到的实例变成
            // Unity 的「假 null」，从而让 InkSurfaceController 回退到默认分辨率。
            var inkCfg = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>($"{SoFolder}/InkGridConfig.asset");
            var playerCfg = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>($"{SoFolder}/PlayerConfig.asset");
            var weaponCfg = AssetDatabase.LoadAssetAtPath<WeaponConfigSO>($"{SoFolder}/WeaponConfig.asset");
            var gameCfg = AssetDatabase.LoadAssetAtPath<GameConfigSO>($"{SoFolder}/GameConfig.asset");
            var botCfg = AssetDatabase.LoadAssetAtPath<BotConfigSO>($"{SoFolder}/BotConfig.asset");

            var groundMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_InkGround.mat");
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_ObstacleWall.mat");
            var inkWallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_InkWall.mat");
            var boundaryMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_BoundaryWall.mat");
            var spawnMatA = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_SpawnPad_TeamA.mat");
            var spawnMatB = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_SpawnPad_TeamB.mat");
            var containerTealMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Teal.mat");
            var containerRustMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Rust.mat");
            var containerSlateMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Slate.mat");
            var containerMustardMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Mustard.mat");
            var teamAMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamA_Body.mat");
            var teamBMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamB_Body.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_VisorDark.mat");

            // 1. 主光源与环境光
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.98f, 0.94f, 1f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            // 2. 管理器根节点（涂地、音效、粒子、弹丸池、手雷池、F12调试、俯视角遮挡管理器）
            var managersRoot = new GameObject("_Managers");
            var inkSurface = managersRoot.AddComponent<InkSurfaceController>();
            var audioMgr = managersRoot.AddComponent<AudioManager>();
            WireAudioClips(audioMgr);
            managersRoot.AddComponent<VfxPool>();
            managersRoot.AddComponent<InkProjectilePool>();
            managersRoot.AddComponent<InkBombPool>();
            managersRoot.AddComponent<DebugOverlay>();
            var navigator = managersRoot.AddComponent<ArenaNavigator>();
            managersRoot.AddComponent<TopDownOcclusionManager>();

            // 3. 搭建 40x40m 对称竞技场（含中立高台、四向斜坡、集装箱掩体群与侧翼走廊）
            var inkableRenderers = new List<Renderer>();
            var blockedColliders = new List<Collider>();
            BuildArenaGeometry(
                groundMat, wallMat, boundaryMat, spawnMatA, spawnMatB,
                containerTealMat, containerRustMat, containerSlateMat, containerMustardMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders, out _);

            inkSurface.Configure(inkCfg, inkableRenderers, blockedColliders);
            navigator.Configure(inkCfg != null ? inkCfg.mapSize : new Vector2(40f, 40f), 0.5f, ~0);

            // 4. 双方各 4 个出生点（Team A = 玩家 + 3 Bot，Team B = 4 Bot）
            var spawnsRoot = new GameObject("_Spawns");
            Transform[] teamASpawns = CreateSpawnLine(spawnsRoot.transform, Team.TeamA);
            Transform[] teamBSpawns = CreateSpawnLine(spawnsRoot.transform, Team.TeamB);

            // 5. 创建主角（Team A 橙队）
            GameObject playerGo = BuildCharacterActor(
                "Player_TeamA",
                Team.TeamA,
                teamASpawns[0].position + Vector3.up * 0.1f,
                teamASpawns[0].rotation,
                teamAMat,
                visorMat,
                playerCfg,
                weaponCfg,
                teamASpawns[0],
                isLocalPlayer: true,
                botCfg: null);

            // 6. 3 个友方 Bot + 4 个敌方 Bot
            for (int i = 1; i < teamASpawns.Length; i++)
            {
                BuildCharacterActor($"BotA_{i:00}", Team.TeamA, teamASpawns[i].position + Vector3.up * 0.1f,
                    teamASpawns[i].rotation, teamAMat, visorMat, playerCfg, weaponCfg, teamASpawns[i],
                    isLocalPlayer: false, botCfg: botCfg);
            }

            for (int i = 0; i < teamBSpawns.Length; i++)
            {
                BuildCharacterActor($"BotB_{i:00}", Team.TeamB, teamBSpawns[i].position + Vector3.up * 0.1f,
                    teamBSpawns[i].rotation, teamBMat, visorMat, playerCfg, weaponCfg, teamBSpawns[i],
                    isLocalPlayer: false, botCfg: botCfg);
            }

            // 7. 创建正交俯视跟随相机
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            camGo.AddComponent<AudioListener>();
            var camFollow = camGo.AddComponent<TopDownCameraFollow>();
            camFollow.Configure(playerGo.transform, gameCfg);

            // 8. 创建战斗 HUD（含顶部覆盖率条、右上角实时涂地小地图、底部墨水/手雷/大招槽与操作说明）
            BuildHudCanvas();

            // 9. 比赛状态机 + 开始/倒计时/结算叠层
            var matchManager = managersRoot.AddComponent<MatchManager>();
            matchManager.Configure(gameCfg);
            BuildMatchFlowCanvas();

            // 10. 移动端触控层（桌面自动隐藏）
            managersRoot.AddComponent<MobileTouchUI>();

            // 10. 保存场景并写入 BuildSettings
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings(ScenePath);

            Debug.Log($"[ProjectScaffolder] 4v4 竞技场场景已生成并打开：{ScenePath}");
        }

        private static Transform[] CreateSpawnLine(Transform parent, Team team)
        {
            var group = new GameObject($"{team}_SpawnPoints");
            group.transform.SetParent(parent, false);

            float z = team == Team.TeamA ? -15.5f : 15.5f;
            float facing = team == Team.TeamA ? 0f : 180f;
            var offsets = new[] { -2.4f, -0.8f, 0.8f, 2.4f };
            var result = new Transform[offsets.Length];

            for (int i = 0; i < offsets.Length; i++)
            {
                var go = new GameObject($"{team}_Spawn_{i:00}");
                go.transform.SetParent(group.transform, false);
                go.transform.position = new Vector3(offsets[i], 0.22f, z);
                go.transform.rotation = Quaternion.Euler(0f, facing, 0f);
                result[i] = go.transform;
            }

            return result;
        }

        [MenuItem("P1/关卡与地图/升级当前场景竞技场地貌与遮挡剔除 (P5-B)", false, 30)]
        public static void UpgradeSceneArenaAndOcclusion()
        {
            GenerateConfigsAndMaterials();

            var inkCfg = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>($"{SoFolder}/InkGridConfig.asset");
            var groundMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_InkGround.mat");
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_ObstacleWall.mat");
            var inkWallMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_InkWall.mat");
            var boundaryMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_BoundaryWall.mat");
            var spawnMatA = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_SpawnPad_TeamA.mat");
            var spawnMatB = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_SpawnPad_TeamB.mat");
            var containerTealMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Teal.mat");
            var containerRustMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Rust.mat");
            var containerSlateMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Slate.mat");
            var containerMustardMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_Container_Mustard.mat");

            // 1. 确保 _Managers 挂载 TopDownOcclusionManager
            var managersRoot = GameObject.Find("_Managers");
            if (managersRoot != null)
            {
                var occlusionMgr = managersRoot.GetComponent<TopDownOcclusionManager>();
                if (occlusionMgr == null)
                {
                    managersRoot.AddComponent<TopDownOcclusionManager>();
                }
            }

            // 2. 找到原 Arena_40x40 并在同层级幂等替换
            var oldArena = GameObject.Find("Arena_40x40");
            if (oldArena != null)
            {
                Object.DestroyImmediate(oldArena);
            }

            var inkableRenderers = new List<Renderer>();
            var blockedColliders = new List<Collider>();
            BuildArenaGeometry(
                groundMat, wallMat, boundaryMat, spawnMatA, spawnMatB,
                containerTealMat, containerRustMat, containerSlateMat, containerMustardMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders, out _);

            // 3. 重新配置 InkSurfaceController 与 ArenaNavigator
            var inkSurface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (inkSurface != null)
            {
                inkSurface.Configure(inkCfg, inkableRenderers, blockedColliders);
            }

            var navigator = Object.FindAnyObjectByType<ArenaNavigator>();
            if (navigator != null)
            {
                navigator.Configure(inkCfg != null ? inkCfg.mapSize : new Vector2(40f, 40f), 0.5f, ~0);
            }

            // 4. 刷新 OcclusionManager
            var occMgr = Object.FindAnyObjectByType<TopDownOcclusionManager>();
            if (occMgr != null)
            {
                occMgr.AutoBindSceneFaders();
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[ProjectScaffolder] P5-B 竞技场地貌与俯视角半透明遮挡剔除升级完毕！");
        }

        [MenuItem("P1/关卡与地图/渲染遮挡剔除视觉留底图 (P5-B)", false, 31)]
        public static void CaptureP5BVerificationShots()
        {
            UpgradeSceneArenaAndOcclusion();
            System.IO.Directory.CreateDirectory("Logs");

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("TempRenderCam", typeof(Camera));
                cam = camGo.GetComponent<Camera>();
            }

            // Shot 1: 全场高空全景鸟瞰视角 (40x40m 对称竞技场总览)
            Vector3 origPos = cam.transform.position;
            Quaternion origRot = cam.transform.rotation;
            float origFov = cam.fieldOfView;
            bool origOrtho = cam.orthographic;

            cam.orthographic = false;
            cam.fieldOfView = 58f;
            cam.transform.position = new Vector3(0f, 26f, -22f);
            cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            // 给地面涂些代表性的队伍墨水，展现战术推进动线
            var surface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (surface != null)
            {
                surface.InitializeIfNeeded();
                // Team A 沿南斜坡与西走廊铺墨
                surface.PaintCircle(new Vector3(0f, 0f, -12f), 3.5f, Team.TeamA);
                surface.PaintCircle(new Vector3(0f, 0.5f, -7.4f), 2.2f, Team.TeamA);
                surface.PaintCircle(new Vector3(0f, 1.1f, -2.5f), 2.8f, Team.TeamA);
                surface.PaintCircle(new Vector3(-13.5f, 0f, -8f), 2.5f, Team.TeamA);
                surface.PaintCircle(new Vector3(-13.5f, 0.9f, 0f), 2.8f, Team.TeamA);
                surface.PaintCircle(new Vector3(-17.5f, 0f, -4f), 2.0f, Team.TeamA);

                // Team B 沿北斜坡与东走廊铺墨
                surface.PaintCircle(new Vector3(0f, 0f, 12f), 3.5f, Team.TeamB);
                surface.PaintCircle(new Vector3(0f, 0.5f, 7.4f), 2.2f, Team.TeamB);
                surface.PaintCircle(new Vector3(0f, 1.1f, 2.5f), 2.8f, Team.TeamB);
                surface.PaintCircle(new Vector3(13.5f, 0f, 8f), 2.5f, Team.TeamB);
                surface.PaintCircle(new Vector3(13.5f, 0.9f, 0f), 2.8f, Team.TeamB);
                surface.PaintCircle(new Vector3(17.5f, 0f, 4f), 2.0f, Team.TeamB);
                surface.FlushTexture();
            }

            RenderCameraToPng(cam, "Logs/p5b_arena_symmetric_overview.png", 1600, 900);

            // Shot 2: 俯视角集装箱掩体半透明遮挡剔除特写 (Screen-Door Dither Fade & X-Ray)
            // 将玩家放置在西南集装箱 (Container_Mid_SW_Mustard) 正后方
            var player = GameObject.Find("Player_TeamA");
            Vector3 origPlayerPos = Vector3.zero;
            if (player != null)
            {
                origPlayerPos = player.transform.position;
                player.transform.position = new Vector3(-9.2f, 0.05f, -3.2f);
                Physics.SyncTransforms();
            }

            // 俯视相机对准角色
            cam.fieldOfView = 40f;
            cam.transform.position = new Vector3(-9.2f, 8.8f, -11.4f);
            cam.transform.rotation = Quaternion.Euler(48f, 0f, 0f);

            var occMgr = Object.FindAnyObjectByType<TopDownOcclusionManager>();
            if (occMgr != null)
            {
                occMgr.SetCamera(cam);
                for (int i = 0; i < 20; i++)
                {
                    typeof(TopDownOcclusionManager).GetMethod("LateUpdate",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.Invoke(occMgr, null);
                }
            }

            RenderCameraToPng(cam, "Logs/p5b_occlusion_dither_fade.png", 1600, 900);

            // 恢复相机与角色位置
            if (player != null)
            {
                player.transform.position = origPlayerPos;
                Physics.SyncTransforms();
            }
            cam.transform.position = origPos;
            cam.transform.rotation = origRot;
            cam.fieldOfView = origFov;
            cam.orthographic = origOrtho;

            Debug.Log("[ProjectScaffolder] P5-B 视觉留底截图已保存至 Logs/p5b_arena_symmetric_overview.png 与 Logs/p5b_occlusion_dither_fade.png！");
        }

        [MenuItem("P1/构建/4. 渲染场景涂地自检预览图 (Logs/phaseA_preview.png)", false, 13)]
        public static void CaptureVerificationScreenshot()
        {
            AssetDatabase.ImportAsset("Assets/_Project/Shaders/InkGround.shader", ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            BuildPhaseAPlayboxScene();

            var surface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (surface != null)
            {
                surface.InitializeIfNeeded();
                // 模拟主角出生圈 + 一串沿途滴落与终点大斑 + 一颗延时手雷大爆斑 + 敌方木桩蓝墨水
                surface.PaintCircle(new Vector3(0f, 0f, -13.0f), 2.6f, Team.TeamA);
                surface.PaintCircle(new Vector3(0f, 0f, -10.2f), 0.50f, Team.TeamA);
                surface.PaintCircle(new Vector3(0f, 0f, -8.0f), 0.50f, Team.TeamA);
                surface.PaintCircle(new Vector3(0f, 0.5f, -5.8f), 0.95f, Team.TeamA);
                surface.PaintCircle(new Vector3(-3.2f, 0f, -5.5f), 3.0f, Team.TeamA);

                surface.PaintCircle(new Vector3(-6.5f, 0f, 3.5f), 2.5f, Team.TeamB);
                surface.PaintCircle(new Vector3(0f, 1.2f, 2.0f), 2.8f, Team.TeamB);
                surface.PaintCircle(new Vector3(4.5f, 0f, -3.5f), 2.2f, Team.TeamB);
                surface.FlushTexture();
                Debug.Log($"[ProjectScaffolder] 预览涂地: 橙队 {surface.GetCoveragePercent(Team.TeamA):F2}% / " +
                          $"蓝队 {surface.GetCoveragePercent(Team.TeamB):F2}% / " +
                          $"可涂格 {surface.Grid.PaintableCount}");
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                // 临时对准场地中南段，同时拍到主角、斜坡、高台、掩体与红蓝墨水（使用最新倾斜透视视角）
                Vector3 origPos = cam.transform.position;
                Quaternion origRot = cam.transform.rotation;
                float origSize = cam.orthographicSize;
                bool origOrtho = cam.orthographic;
                float origFov = cam.fieldOfView;

                cam.orthographic = false;
                cam.fieldOfView = 42f;
                cam.transform.position = new Vector3(0f, 14.5f, -14.0f);
                cam.transform.rotation = Quaternion.Euler(48.5f, 0f, 0f);

                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;

                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();

                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);

                cam.transform.SetPositionAndRotation(origPos, origRot);
                cam.orthographic = origOrtho;
                cam.fieldOfView = origFov;
                cam.orthographicSize = origSize;

                System.IO.Directory.CreateDirectory("Logs");
                byte[] png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                string outPath = "Logs/phaseA_preview.png";
                System.IO.File.WriteAllBytes(outPath, png);
                Debug.Log($"[ProjectScaffolder] 自检预览图已输出：{outPath} ({png.Length} bytes)");
            }

            // 重新把场景重置为干净初始态并保存
            if (surface != null)
            {
                surface.ResetSurface();
            }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        }

        [MenuItem("P1/构建/3. 应用项目 PlayerSettings (横屏 / 包名 / ARM64)", false, 12)]
        public static void ApplyProjectPlayerSettings()
        {
            PlayerSettings.companyName = "P1Studio";
            PlayerSettings.productName = "InkTurfDemo";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.SetApplicationIdentifier( UnityEditor.Build.NamedBuildTarget.Android, "com.p1studio.inkturfdemo");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.p1studio.inkturfdemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }

        [MenuItem("P1/角色与武器/升级场景中所有角色模型 (P5-A)", false, 30)]
        public static void UpgradeAllCharactersVisuals()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!activeScene.isLoaded || string.IsNullOrEmpty(activeScene.path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            }

            var teamAMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamA_Body.mat");
            var teamBMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamB_Body.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_VisorDark.mat");

            var motors = Object.FindObjectsByType<InkCharacterMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var motor in motors)
            {
                var health = motor.GetComponent<CombatantHealth>();
                var weapon = motor.GetComponent<WeaponController>();
                Team team = health != null ? health.Team : Team.TeamA;
                Material bodyMat = team == Team.TeamA ? teamAMat : teamBMat;
                WeaponConfigSO weaponCfg = weapon != null ? weapon.Config : null;

                // 查找或新建 VisualRoot
                Transform visualRoot = motor.transform.Find("VisualRoot");
                if (visualRoot == null)
                {
                    var vrGo = new GameObject("VisualRoot");
                    vrGo.transform.SetParent(motor.transform, false);
                    visualRoot = vrGo.transform;
                }

                var visualCtrl = CharacterVisualAssembler.Assemble(
                    visualRoot,
                    team,
                    bodyMat,
                    visorMat,
                    weaponCfg);

                motor.Configure(motor.Config, visualRoot, visualCtrl.WeaponRoot.gameObject);
                if (weapon != null)
                {
                    weapon.Configure(weapon.Config, visualCtrl.ActiveMuzzle, visualRoot);
                }
                visualCtrl.Initialize(motor, weapon, team);
                EditorUtility.SetDirty(motor.gameObject);
                count++;
            }

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log($"[ProjectScaffolder] 已升级 {count} 个角色的程序化多部件模型 (Squidkid + 乌贼 + 7类武器)");
        }

        [MenuItem("P1/角色与武器/生成角色与武器多形态高清展示图 (P5-A)", false, 31)]
        public static void CaptureCharacterVisualShowcase()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!activeScene.isLoaded || string.IsNullOrEmpty(activeScene.path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            }

            var teamAMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamA_Body.mat");
            var teamBMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamB_Body.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_VisorDark.mat");

            // 创建展示临时容器
            var showcaseGo = new GameObject("__Showcase_P5A__");
            try
            {
                // 1. 创建单人角色用于前视与后视特写
                var heroGo = new GameObject("ShowcaseHero");
                heroGo.transform.SetParent(showcaseGo.transform, false);
                heroGo.transform.position = new Vector3(0f, 0f, 0f);
                heroGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 面向南方 (+Z 向后，面向 -Z 摄像机)

                var heroCtrl = CharacterVisualAssembler.Assemble(heroGo.transform, Team.TeamA, teamAMat, visorMat, null);
                heroCtrl.EquipWeaponKind(WeaponKind.Shooter);

                // 创建临时渲染相机
                var camGo = new GameObject("ShowcaseCam");
                camGo.transform.SetParent(showcaseGo.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
                cam.fieldOfView = 38f;

                System.IO.Directory.CreateDirectory("Logs");

                // Shot 1: 正面人形态特写
                cam.transform.position = new Vector3(0.35f, 1.05f, -2.4f);
                cam.transform.rotation = Quaternion.Euler(10f, -8f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_character_kid_front.png", 1600, 900);

                // Shot 2: 后背背包与发辫特写
                heroGo.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
                cam.transform.position = new Vector3(-0.4f, 1.15f, -2.4f);
                cam.transform.rotation = Quaternion.Euler(14f, 10f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_character_kid_back.png", 1600, 900);

                // Shot 3: 乌贼形态特写
                heroGo.transform.rotation = Quaternion.Euler(0f, 145f, 0f);
                heroCtrl.KidRoot.SetActive(false);
                heroCtrl.SquidRoot.SetActive(true);
                heroCtrl.SquidRoot.transform.localScale = Vector3.one;
                cam.transform.position = new Vector3(0f, 1.1f, -1.8f);
                cam.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_squid_form.png", 1600, 900);

                // Shot 4: 7 种武器全家福队列 (Arsenal Showcase)
                Object.DestroyImmediate(heroGo);
                WeaponKind[] allKinds = new[]
                {
                    WeaponKind.Shooter,
                    WeaponKind.Roller,
                    WeaponKind.Charger,
                    WeaponKind.Dualies,
                    WeaponKind.Slosher,
                    WeaponKind.Blaster,
                    WeaponKind.Splatling
                };

                for (int i = 0; i < allKinds.Length; i++)
                {
                    float x = (i - 3) * 1.05f;
                    var cGo = new GameObject("Char_" + allKinds[i]);
                    cGo.transform.SetParent(showcaseGo.transform, false);
                    cGo.transform.position = new Vector3(x, 1.25f, 0f);
                    cGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                    var ctrl = CharacterVisualAssembler.Assemble(cGo.transform, i % 2 == 0 ? Team.TeamA : Team.TeamB, i % 2 == 0 ? teamAMat : teamBMat, visorMat, null);
                    ctrl.EquipWeaponKind(allKinds[i]);
                }

                cam.fieldOfView = 68f;
                cam.transform.position = new Vector3(0f, 2.5f, -6.2f);
                cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_weapons_arsenal.png", 1920, 1080);

                Debug.Log("[ProjectScaffolder] 已成功生成角色与武器多形态高清展示图：Logs/p5a_*.png");
            }
            finally
            {
                Object.DestroyImmediate(showcaseGo);
            }
        }

        private static void RenderCameraToPng(Camera cam, string outputPath, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            System.IO.File.WriteAllBytes(outputPath, png);
        }

        private static void BuildArenaGeometry(
            Material groundMat,
            Material wallMat,
            Material boundaryMat,
            Material spawnMatA,
            Material spawnMatB,
            InkGridConfigSO inkCfg,
            Material inkWallMat,
            List<Renderer> inkableRenderers,
            List<Collider> blockedColliders,
            out Transform teamASpawnAnchor)
        {
            BuildArenaGeometry(
                groundMat, wallMat, boundaryMat, spawnMatA, spawnMatB,
                wallMat, wallMat, wallMat, wallMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders, out teamASpawnAnchor);
        }

        private static void BuildArenaGeometry(
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
            ArenaGeometryBuilder.BuildArenaGeometry(
                groundMat, wallMat, boundaryMat, spawnMatA, spawnMatB,
                containerTealMat, containerRustMat, containerSlateMat, containerMustardMat,
                inkCfg, inkWallMat, inkableRenderers, blockedColliders, out teamASpawnAnchor);
        }

        private static GameObject BuildCharacterActor(
            string actorName,
            Team team,
            Vector3 position,
            Quaternion rotation,
            Material bodyMat,
            Material visorMat,
            PlayerConfigSO playerCfg,
            WeaponConfigSO weaponCfg,
            Transform spawnAnchor,
            bool isLocalPlayer,
            BotConfigSO botCfg)
        {
            var root = new GameObject(actorName);
            root.transform.SetPositionAndRotation(position, rotation);

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.radius = 0.42f;
            cc.center = new Vector3(0f, 0.8f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;

            // 角色视觉节点（搭载多部件潮流涂鸦少年 Squidkid、乌贼潜行形态与 7 类主武器专属模型）
            var visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            var visualCtrl = CharacterVisualAssembler.Assemble(
                visualRoot.transform,
                team,
                bodyMat,
                visorMat,
                weaponCfg);

            var health = root.AddComponent<CombatantHealth>();
            health.Configure(team, playerCfg, spawnAnchor);

            var motor = root.AddComponent<InkCharacterMotor>();
            motor.Configure(playerCfg, visualRoot.transform, visualCtrl.WeaponRoot.gameObject);

            var weapon = root.AddComponent<WeaponController>();
            weapon.Configure(weaponCfg, visualCtrl.ActiveMuzzle, visualRoot.transform);

            visualCtrl.Initialize(motor, weapon, team);

            if (isLocalPlayer)
            {
                root.AddComponent<PlayerController>();
            }
            else if (botCfg != null)
            {
                root.AddComponent<BotController>().Configure(botCfg);
                // Bot 的寻路只在地面 NavGrid 上，不让他们贴墙攀爬（否则会脱离寻路预期卡在墙顶）
                motor.AllowWallClimb = false;
            }

            return root;
        }

        private static void BuildHudCanvas()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            var canvasGo = new GameObject("HUD_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite whiteSprite = CreateSolidWhiteSprite();

            // 1. 顶部覆盖率比拼条
            var topPanel = CreateUiPanel(canvasGo.transform, "TopCoveragePanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(820f, 52f), new Color(0.06f, 0.08f, 0.12f, 0.86f));
            var barBg = CreateUiPanel(topPanel, "CoverageBarBg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(780f, 16f), new Color(0.18f, 0.20f, 0.25f, 1f));

            var teamAFill = CreateFilledBar(barBg, "TeamAFill", whiteSprite, new Color(1.0f, 0.45f, 0.08f, 1f), Image.OriginHorizontal.Left);
            var teamBFill = CreateFilledBar(barBg, "TeamBFill", whiteSprite, new Color(0.16f, 0.42f, 1.0f, 1f), Image.OriginHorizontal.Right);

            var teamAText = CreateUiText(topPanel, "TeamAPercent", font, "橙队 0.0%", 20, TextAnchor.MiddleLeft, new Vector2(-240f, 10f), new Vector2(260f, 28f), new Color(1f, 0.65f, 0.25f, 1f));
            var teamBText = CreateUiText(topPanel, "TeamBPercent", font, "0.0% 蓝队", 20, TextAnchor.MiddleRight, new Vector2(240f, 10f), new Vector2(260f, 28f), new Color(0.45f, 0.72f, 1f, 1f));
            var centerText = CreateUiText(topPanel, "CenterStatus", font, "手感试玩场 00:00", 20, TextAnchor.MiddleCenter, new Vector2(0f, 10f), new Vector2(280f, 28f), Color.white);

            // 2. 右上角常驻实时涂地小地图
            var minimapFrame = CreateUiPanel(canvasGo.transform, "MinimapFrame", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-135f, -135f), new Vector2(230f, 230f), new Color(0.06f, 0.08f, 0.12f, 0.90f));
            CreateUiText(minimapFrame, "MinimapTitle", font, "实时全图涂地小地图", 15, TextAnchor.UpperCenter, new Vector2(0f, 96f), new Vector2(210f, 24f), new Color(0.82f, 0.88f, 0.96f, 1f));

            var rawGo = new GameObject("MinimapRawImage", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(minimapFrame, false);
            var rawRect = rawGo.GetComponent<RectTransform>();
            rawRect.sizeDelta = new Vector2(196f, 196f);
            rawRect.anchoredPosition = new Vector2(0f, -8f);
            var rawImg = rawGo.GetComponent<RawImage>();
            rawImg.color = Color.white;

            var dotGo = new GameObject("PlayerMarkerDot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(rawRect, false);
            var dotRect = dotGo.GetComponent<RectTransform>();
            dotRect.sizeDelta = new Vector2(10f, 10f);
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.sprite = whiteSprite;
            dotImg.color = Color.white;

            // 3. 底部主角状态（墨水槽 + 手雷55%刻度线 + 生命条 + 大招槽 + 操作说明）
            var bottomPanel = CreateUiPanel(canvasGo.transform, "BottomStatusPanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(760f, 106f), new Color(0.06f, 0.08f, 0.12f, 0.86f));

            var inkBg = CreateUiPanel(bottomPanel, "InkBarBg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150f, 12f), new Vector2(400f, 22f), new Color(0.18f, 0.20f, 0.25f, 1f));
            var inkFill = CreateFilledBar(inkBg, "InkBarFill", whiteSprite, new Color(1.0f, 0.50f, 0.08f, 1f), Image.OriginHorizontal.Left);
            // 55% 手雷耗墨刻度线
            CreateUiPanel(inkBg, "BombThresholdTick", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(400f * 0.55f, 0f), new Vector2(3f, 26f), Color.white);

            var hpBg = CreateUiPanel(bottomPanel, "HealthBarBg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150f, -16f), new Vector2(400f, 12f), new Color(0.18f, 0.20f, 0.25f, 1f));
            var hpFill = CreateFilledBar(hpBg, "HealthBarFill", whiteSprite, new Color(0.25f, 0.92f, 0.48f, 1f), Image.OriginHorizontal.Left);

            var specBg = CreateUiPanel(bottomPanel, "SpecialBarBg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(215f, -2f), new Vector2(270f, 38f), new Color(0.16f, 0.18f, 0.24f, 1f));
            var specFill = CreateFilledBar(specBg, "SpecialBarFill", whiteSprite, new Color(0.25f, 0.78f, 1f, 1f), Image.OriginHorizontal.Left);
            var specText = CreateUiText(specBg, "SpecialStateText", font, "大招充能 0%", 16, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(260f, 34f), Color.white);

            var inkText = CreateUiText(bottomPanel, "InkStateText", font, "墨水 100/100", 16, TextAnchor.MiddleLeft, new Vector2(-150f, 36f), new Vector2(400f, 24f), Color.white);
            CreateUiText(bottomPanel, "ControlsHintText", font, "操作：[WASD]移动  [空格]跳跃  [鼠标左键]连发涂地  [按住右键/Shift]己墨潜行回墨·贴着己方墨墙可潜上墙  [E/中键]抛延时手雷  [Q]超级砸地大招", 14, TextAnchor.MiddleCenter, new Vector2(0f, -38f), new Vector2(740f, 22f), new Color(0.80f, 0.86f, 0.95f, 1f));

            var hud = canvasGo.AddComponent<HudController>();
            hud.BindReferences(teamAFill, teamBFill, teamAText, teamBText, centerText, inkFill, hpFill, specFill, inkText, specText, rawImg, rawRect, dotRect);
            hud.SetGaugeTheme(
                AssetDatabase.LoadAssetAtPath<UIThemeSO>(UIStyleTools.ThemePath),
                AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(HudGaugeStyleTools.GaugeStylePath));
        }

        private static void BuildMatchFlowCanvas()
        {
            var canvasGo = new GameObject("MatchFlow_Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite whiteSprite = CreateSolidWhiteSprite();

            // ---- 开始叠层 ----
            var ready = CreateFullScreenPanel(canvasGo.transform, "ReadyPanel", new Color(0.03f, 0.04f, 0.07f, 0.82f));
            var title = CreateUiText(ready, "Title", font, "涂 地 热 身 赛", 76, TextAnchor.MiddleCenter,
                new Vector2(0f, 230f), new Vector2(900f, 110f), new Color(1f, 0.62f, 0.18f, 1f));
            var subtitle = CreateUiText(ready, "Subtitle", font, "4 v 4 · 120 秒 · 涂地面积高者获胜", 30, TextAnchor.MiddleCenter,
                new Vector2(0f, 130f), new Vector2(900f, 50f), new Color(0.86f, 0.9f, 0.98f, 1f));
            var startBtn = CreateUiButton(ready, "StartButton", font, "开 始 比 赛", new Vector2(0f, -20f), new Vector2(360f, 96f),
                new Color(1f, 0.48f, 0.10f, 0.95f), out var startLabel);
            CreateUiText(ready, "StartHint", font, "也可以按 空格 / 回车 开始", 22, TextAnchor.MiddleCenter,
                new Vector2(0f, -120f), new Vector2(700f, 40f), new Color(0.75f, 0.8f, 0.9f, 1f));

            var countdown = CreateUiText(canvasGo.transform, "CountdownText", font, "3", 140, TextAnchor.MiddleCenter,
                new Vector2(0f, 60f), new Vector2(600f, 220f), new Color(1f, 1f, 1f, 1f));
            countdown.fontStyle = FontStyle.Bold;
            countdown.gameObject.SetActive(false);

            var finish = CreateUiText(canvasGo.transform, "FinishText", font, "FINISH!", 130, TextAnchor.MiddleCenter,
                new Vector2(0f, 60f), new Vector2(900f, 220f), new Color(1f, 0.85f, 0.2f, 1f));
            finish.fontStyle = FontStyle.Bold;
            finish.gameObject.SetActive(false);

            // ---- 结算叠层 ----
            var result = CreateFullScreenPanel(canvasGo.transform, "ResultPanel", new Color(0.03f, 0.04f, 0.07f, 0.90f));
            result.gameObject.SetActive(false);

            var resultTitle = CreateUiText(result, "ResultTitle", font, "比 赛 结 束", 56, TextAnchor.MiddleCenter,
                new Vector2(0f, 280f), new Vector2(900f, 90f), Color.white);

            var barA = CreateUiPanel(result, "ResultBarA", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-20f, 90f), new Vector2(700f, 46f), new Color(0.18f, 0.20f, 0.25f, 1f));
            var fillA = CreateFilledBar(barA, "FillA", whiteSprite, new Color(1f, 0.45f, 0.08f, 1f), Image.OriginHorizontal.Left);

            var barB = CreateUiPanel(result, "ResultBarB", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-20f, 20f), new Vector2(700f, 46f), new Color(0.18f, 0.20f, 0.25f, 1f));
            var fillB = CreateFilledBar(barB, "FillB", whiteSprite, new Color(0.16f, 0.42f, 1f, 1f), Image.OriginHorizontal.Left);

            var percentA = CreateUiText(result, "PercentA", font, "0.0%", 34, TextAnchor.MiddleRight,
                new Vector2(-470f, 90f), new Vector2(200f, 50f), new Color(1f, 0.62f, 0.2f, 1f));
            var percentB = CreateUiText(result, "PercentB", font, "0.0%", 34, TextAnchor.MiddleRight,
                new Vector2(-470f, 20f), new Vector2(200f, 50f), new Color(0.45f, 0.7f, 1f, 1f));

            var verdict = CreateUiText(result, "Verdict", font, "统计涂地面积中...", 64, TextAnchor.MiddleCenter,
                new Vector2(0f, -120f), new Vector2(900f, 110f), new Color(0.85f, 0.9f, 1f, 1f));

            var restartBtn = CreateUiButton(result, "RestartButton", font, "再 来 一 局", new Vector2(0f, -270f), new Vector2(340f, 90f),
                new Color(0.20f, 0.55f, 0.95f, 0.95f), out _);

            var flow = canvasGo.AddComponent<MatchFlowUI>();
            flow.BindReferences(
                ready.gameObject, title, subtitle, startBtn, startLabel,
                countdown, finish,
                result.gameObject, resultTitle,
                fillA, fillB, percentA, percentB, verdict, restartBtn);
        }

        private static RectTransform CreateFullScreenPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private static Button CreateUiButton(Transform parent, string name, Font font, string label,
            Vector2 anchoredPos, Vector2 size, Color bgColor, out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = bgColor;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            labelText = CreateUiText(go.transform, "Label", font, label, 36, TextAnchor.MiddleCenter,
                Vector2.zero, size * 0.9f, Color.white);
            labelText.fontStyle = FontStyle.Bold;

            return btn;
        }

        private static RectTransform CreateUiPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            return rt;
        }

        private static Image CreateFilledBar(Transform parent, string name, Sprite whiteSprite, Color color, Image.OriginHorizontal origin)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = whiteSprite;
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)origin;
            img.fillAmount = 0f;
            return img;
        }

        private static Text CreateUiText(Transform parent, string name, Font font, string content, int fontSize, TextAnchor align, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var txt = go.GetComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return txt;
        }

        private static Sprite CreateSolidWhiteSprite()
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        }

        private static GameObject CreateCover(
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

        private static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
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

        /// <summary>
        /// 给轴对齐盒体挂上可涂 / 可潜行攀爬的立面（4 张竖直面）。
        /// 立面的网格、贴图与叠加 Quad 由 InkWallSurface 在运行时按碰撞体包围盒确定性生成。
        /// </summary>
        private static void AddInkWallSurface(GameObject box, InkGridConfigSO inkCfg, Material inkWallMat)
        {
            if (box == null || inkCfg == null || !inkCfg.enableWallInk)
                return;

            var col = box.GetComponent<BoxCollider>();
            if (col == null)
                return;

            var wall = box.AddComponent<InkWallSurface>();
            wall.Configure(col, inkWallMat, inkCfg);
        }

        private static void WireAudioClips(AudioManager mgr)
        {
            const string sfxDir = "Assets/_Project/Audio/SFX";
            mgr.shotClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/shot_1.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/shot_2.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/shot_3.wav")
            };
            mgr.inkHitGroundClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ink_hit_ground_1.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ink_hit_ground_2.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ink_hit_ground_3.wav")
            };
            mgr.hitEnemyClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/hit_enemy_1.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/hit_enemy_2.wav")
            };
            mgr.inkEmptyClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ink_empty_1.wav");
            mgr.inkRefillClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ink_refill_1.wav");
            mgr.ultReadyClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ult_ready_1.wav");
            mgr.ultCastClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ult_cast_1.wav");
            mgr.killClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/kill_1.wav");
            mgr.deathClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/death_1.wav");
            mgr.respawnClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/respawn_1.wav");
            mgr.countdownTickClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/countdown_tick_1.wav");
            mgr.matchStartClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/match_start_1.wav");
            mgr.matchEndClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/match_end_1.wav");
            mgr.resultWinClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/result_win_1.wav");
            mgr.resultLoseClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/result_lose_1.wav");
            mgr.uiClickClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/ui_click_1.wav");
            mgr.lowInkWarningClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{sfxDir}/low_ink_warning_1.wav");

            // P5-C：播报员语音 + 双段动态 BGM + 混音调参
            const string voiceDir = "Assets/_Project/Audio/Voice";
            const string musicDir = "Assets/_Project/Audio/Music";
            mgr.voiceCount3Clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_count_3_01.wav");
            mgr.voiceCount2Clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_count_2_01.wav");
            mgr.voiceCount1Clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_count_1_01.wav");
            mgr.voiceGoClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_go_01.wav");
            mgr.voiceOneMinuteClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_one_minute_01.wav");
            mgr.voiceFinishClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{voiceDir}/vo_announcer_finish_01.wav");
            mgr.bgmMainClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{musicDir}/bgm_match_main.ogg");
            mgr.bgmFinalClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{musicDir}/bgm_match_final.ogg");
            mgr.mixConfig = LoadOrCreateSO<AudioMixConfigSO>($"{SoFolder}/AudioMixConfig.asset");

            EditorUtility.SetDirty(mgr);
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SoFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes"))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
        }

        private static T LoadOrCreateSO<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            var instance = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(instance, path);
            return instance;
        }

        private static Material CreateOrUpdateGroundMaterial()
        {
            string path = $"{MatFolder}/Mat_InkGround.mat";
            Shader sh = Shader.Find("P1/InkGround");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.SetColor("_BaseColor", new Color(0.20f, 0.22f, 0.26f, 1f));
            mat.SetColor("_GridLineColor", new Color(0.26f, 0.29f, 0.35f, 1f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateInkWallMaterial(string path)
        {
            Shader sh = Shader.Find("P1/InkWall");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateObstacleDitherMaterial(string path, Color baseColor, Color trimColor, float corrugation = 1.0f)
        {
            Shader sh = Shader.Find("P1/ObstacleDitherLit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_TrimColor", trimColor);
            mat.SetFloat("_Smoothness", 0.38f);
            mat.SetFloat("_Metallic", 0.12f);
            mat.SetFloat("_EnableCorrugation", corrugation);
            mat.SetFloat("_CorrugationScale", 2.5f);
            mat.SetFloat("_CorrugationDepth", 0.28f);
            mat.SetFloat("_DitherAlpha", 1.0f);
            mat.color = baseColor;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateLitMaterial(string path, Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.SetColor("_BaseColor", color);
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateUnlitMaterial(string path, Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            mat.SetColor("_BaseColor", color);
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == scenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
