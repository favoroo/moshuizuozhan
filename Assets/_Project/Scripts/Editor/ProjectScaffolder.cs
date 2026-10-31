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
        private const string ArenaStylePath = "Assets/_Project/ScriptableObjects/ArenaStyle.asset";

        [MenuItem("P1/构建/1. 生成默认配置与材质 (SO & Materials)", false, 10)]
        public static void GenerateConfigsAndMaterials()
        {
            EnsureFolders();

            var inkCfg = LoadOrCreateSO<InkGridConfigSO>($"{SoFolder}/InkGridConfig.asset");
            var playerCfg = LoadOrCreateSO<PlayerConfigSO>($"{SoFolder}/PlayerConfig.asset");
            var weaponCfg = LoadOrCreateSO<WeaponConfigSO>($"{SoFolder}/WeaponConfig.asset");
            var gameCfg = LoadOrCreateSO<GameConfigSO>($"{SoFolder}/GameConfig.asset");
            var botCfg = LoadOrCreateSO<BotConfigSO>($"{SoFolder}/BotConfig.asset");
            var arenaStyle = LoadOrCreateSO<ArenaStyleSO>(ArenaStylePath);

            // P10-A：雾距统一由 ArenaStyleSO 驱动，重建材质时不再覆盖风格参数。
            EditorUtility.SetDirty(arenaStyle);

            // P7-B：40×56 集装箱码头地图与涂地网格（保持原有 400/200 网格规格，兼容冒烟测试断言）
            inkCfg.mapSize = ArenaLayoutV2.MapSize;
            inkCfg.desktopResolution = 400;
            inkCfg.mobileResolution = 200;
            EditorUtility.SetDirty(inkCfg);

            // 同步最新紧凑参数到资产
            weaponCfg.maxRange = 12f;
            weaponCfg.trailDropDistance = 1.05f;
            weaponCfg.projectileSpeed = 34f;
            weaponCfg.bombMaxThrowDistance = 12f;
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

            CreateOrUpdateGroundMaterial(arenaStyle);
            // 可涂墙面：透明墨水叠加层，未涂区域保持原水泥墙外观
            CreateOrUpdateInkWallMaterial($"{MatFolder}/Mat_InkWall.mat");
            // 掩体墙与集装箱：支持 Screen-Door Dither 遮挡剔除与工业瓦楞条纹
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_ObstacleWall.mat", arenaStyle.concreteLight, arenaStyle.concreteDark, 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Teal.mat", arenaStyle.containerTeal, Color.Lerp(arenaStyle.containerTeal, arenaStyle.steelDark, 0.55f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Rust.mat", arenaStyle.containerRust, Color.Lerp(arenaStyle.containerRust, arenaStyle.steelDark, 0.55f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Slate.mat", arenaStyle.containerSlate, Color.Lerp(arenaStyle.containerSlate, arenaStyle.steelDark, 0.55f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Container_Mustard.mat", arenaStyle.containerMustard, Color.Lerp(arenaStyle.containerMustard, arenaStyle.steelDark, 0.55f), 1.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_BoundaryWall.mat", arenaStyle.boundaryConcrete, arenaStyle.boundaryTrim, 0.0f);

            // P7-B 新增：竞技场掩体用建材（支持 dither 遮挡剔除）
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_Concrete.mat", arenaStyle.concreteLight, arenaStyle.concreteDark, 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_ConcreteDark.mat", arenaStyle.concreteMid, arenaStyle.concreteDark, 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_Steel.mat", arenaStyle.steel, arenaStyle.steelDark, 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_SteelDark.mat", arenaStyle.steelDark, Color.Lerp(arenaStyle.steelDark, Color.black, 0.25f), 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_Wood.mat", arenaStyle.wood, arenaStyle.woodDark, 0.0f);
            CreateOrUpdateObstacleDitherMaterial($"{MatFolder}/Mat_Prop_WoodDark.mat", arenaStyle.woodDark, Color.Lerp(arenaStyle.woodDark, Color.black, 0.2f), 0.0f);

            // P7-B 新增：装饰道具 / 旗帜 / 水面 / 天空 / 横幅
            CreateOrUpdatePropMaterial($"{MatFolder}/Mat_Prop_Matte.mat", Color.white, 0.15f, 0f, Color.black, 0f, 2f);
            CreateOrUpdatePropMaterial($"{MatFolder}/Mat_Prop_Metal.mat", Color.white, 0.55f, 0.65f, Color.black, 0f, 2f);
            CreateOrUpdatePropMaterial($"{MatFolder}/Mat_Prop_Emissive.mat", Color.white, 0.2f, 0f, arenaStyle.lampGlass, 1.6f, 2f);
            CreateOrUpdatePropMaterial($"{MatFolder}/Mat_Prop_Cloth.mat", Color.white, 0.1f, 0f, Color.black, 0f, 0f);
            CreateOrUpdatePropMaterial($"{MatFolder}/Mat_Prop_Foliage.mat", Color.white, 0.08f, 0f, Color.black, 0f, 2f);
            CreateOrUpdateSpawnPadMaterial($"{MatFolder}/Mat_SpawnPad_TeamA.mat", arenaStyle.teamAPad);
            CreateOrUpdateSpawnPadMaterial($"{MatFolder}/Mat_SpawnPad_TeamB.mat", arenaStyle.teamBPad);
            CreateOrUpdateWaterMaterial(arenaStyle);
            CreateOrUpdateSkyboxMaterial(arenaStyle);
            CreateOrUpdateBannerMaterial();

            CreateOrUpdateLitMaterial($"{MatFolder}/Mat_TeamA_Body.mat", new Color(1.00f, 0.45f, 0.08f, 1f));
            CreateOrUpdateLitMaterial($"{MatFolder}/Mat_TeamB_Body.mat", new Color(0.16f, 0.42f, 1.00f, 1f));
            CreateOrUpdateUnlitMaterial($"{MatFolder}/Mat_VisorDark.mat", new Color(0.08f, 0.09f, 0.12f, 1f));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectScaffolder] 默认 ScriptableObjects 与材质已生成完毕（P7-B 40×56 集装箱码头风格）。");
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

            var teamAMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamA_Body.mat");
            var teamBMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamB_Body.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_VisorDark.mat");
            var arenaStyle = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(ArenaStylePath);
            var arenaMats = BuildArenaMaterialSet(arenaStyle);

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

            // 3. 搭建 40×56 集装箱码头竞技场（含中立高台、四向斜坡、集装箱掩体群、延长区与出生网关）
            var inkableRenderers = new List<Renderer>();
            var blockedColliders = new List<Collider>();
            ArenaGeometryBuilder.BuildArenaGeometry(
                arenaMats, inkCfg, arenaMats.inkWall, inkableRenderers, blockedColliders, out _);

            inkSurface.Configure(inkCfg, inkableRenderers, blockedColliders);
            navigator.Configure(inkCfg != null ? inkCfg.mapSize : ArenaLayoutV2.MapSize, 0.5f, ~0);

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

            // 7. 创建第三人称跟随相机（类名 TopDownCameraFollow 保留以兼容旧调用方）
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            camGo.AddComponent<AudioListener>();
            var camFollow = camGo.AddComponent<TopDownCameraFollow>();
            camFollow.Configure(playerGo.transform, gameCfg);

            // 7.1 P7-B：烘焙地面/横幅贴图、生成装饰与港口远景、应用日光/雾/天空/后处理
            BakeArenaVisuals(arenaMats, arenaStyle);

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

            float z = team == Team.TeamA ? -ArenaLayoutV2.SpawnZ : ArenaLayoutV2.SpawnZ;
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
            // P5-B 旧入口保留（菜单路径不变），内部升级为 P7-B 的 40×56 集装箱码头。
            UpgradeSceneVisualsP7B();
        }

        [MenuItem("P1/关卡与地图/场景视觉重构：集装箱码头 (P7-B)", false, 29)]
        public static void UpgradeSceneVisualsP7B()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ProjectScaffolder] 请先退出 Play Mode 再执行场景视觉重构。");
                return;
            }

            GenerateConfigsAndMaterials();

            var inkCfg = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>($"{SoFolder}/InkGridConfig.asset");
            var arenaStyle = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(ArenaStylePath);

            // 确保主场景处于打开状态；若当前是 UIDemo 等其它场景则切回主场景。
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid() || active.path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var arenaMats = BuildArenaMaterialSet(arenaStyle);

            // 1. 幂等替换旧竞技场、装饰与远景
            DestroyImmediateByName("Arena_40x40");
            DestroyImmediateByName(ArenaGeometryBuilder.ArenaRootName);
            DestroyImmediateByName("Arena_Decor");
            DestroyImmediateByName("_ArenaBackdrop");

            var inkableRenderers = new List<Renderer>();
            var blockedColliders = new List<Collider>();
            ArenaGeometryBuilder.BuildArenaGeometry(
                arenaMats, inkCfg, arenaMats.inkWall, inkableRenderers, blockedColliders, out _);

            // 2. 重新绑定涂地 / 导航 / 遮挡
            var inkSurface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (inkSurface != null)
            {
                inkSurface.Configure(inkCfg, inkableRenderers, blockedColliders);
            }

            var navigator = Object.FindAnyObjectByType<ArenaNavigator>();
            if (navigator != null)
            {
                navigator.Configure(inkCfg != null ? inkCfg.mapSize : ArenaLayoutV2.MapSize, 0.5f, ~0);
            }

            // 3. 烘焙贴图 + 生成装饰/远景 + 应用氛围（内部会重载材质并绑定贴图）
            BakeArenaVisuals(arenaMats, arenaStyle);

            var occMgr = Object.FindAnyObjectByType<TopDownOcclusionManager>();
            if (occMgr != null)
            {
                occMgr.AutoBindSceneFaders();
            }

            // 4. 出生点与场景内 8 个角色移到新的 40×56 出生线
            UpdateSpawnTransformsAndActors();

            // 5. 小地图比例与 40×56 对齐
            ResizeMinimapFrame();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[ProjectScaffolder] P7-B 场景视觉重构完成：40×56 集装箱码头。");
        }

        /// <summary>
        /// 加载 P7-B 所需的全部材质（缺失时退回现有掩体材质），供几何/装饰/氛围共用。
        /// </summary>
        private static ArenaMaterialSet BuildArenaMaterialSet(ArenaStyleSO style)
        {
            Material Load(string file) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{file}");

            var set = new ArenaMaterialSet
            {
                ground = Load("Mat_InkGround.mat"),
                wall = Load("Mat_ObstacleWall.mat"),
                inkWall = Load("Mat_InkWall.mat"),
                boundary = Load("Mat_BoundaryWall.mat"),
                spawnPadA = Load("Mat_SpawnPad_TeamA.mat"),
                spawnPadB = Load("Mat_SpawnPad_TeamB.mat"),
                containerTeal = Load("Mat_Container_Teal.mat"),
                containerRust = Load("Mat_Container_Rust.mat"),
                containerSlate = Load("Mat_Container_Slate.mat"),
                containerMustard = Load("Mat_Container_Mustard.mat"),
                propMatte = Load("Mat_Prop_Matte.mat"),
                propMetal = Load("Mat_Prop_Metal.mat"),
                propEmissive = Load("Mat_Prop_Emissive.mat"),
                propCloth = Load("Mat_Prop_Cloth.mat"),
                bannerAtlas = Load("Mat_BannerAtlas.mat"),
                water = Load("Mat_Water.mat"),
                skybox = Load("Mat_Skybox.mat"),
            };

            set.concrete = Load("Mat_Prop_Concrete.mat") ?? set.wall;
            set.concreteDark = Load("Mat_Prop_ConcreteDark.mat") ?? set.concrete;
            set.steel = Load("Mat_Prop_Steel.mat") ?? set.wall;
            set.steelDark = Load("Mat_Prop_SteelDark.mat") ?? set.steel;
            set.wood = Load("Mat_Prop_Wood.mat") ?? set.wall;
            set.woodDark = Load("Mat_Prop_WoodDark.mat") ?? set.wood;
            set.foliage = Load("Mat_Prop_Foliage.mat");
            return set;
        }

        /// <summary>
        /// P7-B 视觉总装：烘焙地面/横幅贴图 -> 绑定材质 -> 生成装饰与远景 -> 应用日光/雾/天空/后处理。
        /// 在场景里已有 Arena 之后调用（AO 需要场景碰撞体）。
        /// </summary>
        private static void BakeArenaVisuals(ArenaMaterialSet mats, ArenaStyleSO style)
        {
            // 场景打开/资产刷新后，外部传入的 SO 引用可能变成 Unity 的「假 null」，这里重新解析一次。
            if (style == null)
            {
                style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(ArenaStylePath);
            }

            if (style == null)
            {
                Debug.LogWarning("[ProjectScaffolder] ArenaStyle.asset 未找到，P7-B 将使用内置默认配色并跳过氛围。");
            }

            ArenaGroundTextureBaker.BakeAll(style);

            // 贴图导入后重新取一次材质，避免资产重导入造成「假 null」。
            var fresh = BuildArenaMaterialSet(style);
            AssignGroundTexture(fresh.ground);
            AssignBannerAtlas(fresh.bannerAtlas);

            DestroyImmediateByName("Arena_Decor");
            DestroyImmediateByName("_ArenaBackdrop");

            ArenaDecorationBuilder.Build(null, style, fresh);
            ArenaDecorationBuilder.BuildBackdrop(null, style, fresh);
            ArenaAtmosphereBuilder.Apply(style);
        }

        private static void AssignGroundTexture(Material groundMat)
        {
            if (groundMat == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArenaGroundTextureBaker.GroundTexturePath);
            if (tex == null) return;
            groundMat.SetTexture("_GroundTex", tex);
            groundMat.SetFloat("_GroundTexStrength", 1f);
            EditorUtility.SetDirty(groundMat);
        }

        private static void AssignBannerAtlas(Material bannerMat)
        {
            if (bannerMat == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArenaGroundTextureBaker.BannerAtlasPath);
            if (tex == null) return;
            bannerMat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(bannerMat);
        }

        private static void DestroyImmediateByName(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 把 _Spawns 下的出生线与 8 个角色迁移到 40×56 的新出生区（z=±23.5）。
        /// </summary>
        private static void UpdateSpawnTransformsAndActors()
        {
            var spawnsRoot = GameObject.Find("_Spawns");
            if (spawnsRoot == null) return;

            Transform teamAGroup = spawnsRoot.transform.Find("TeamA_SpawnPoints");
            Transform teamBGroup = spawnsRoot.transform.Find("TeamB_SpawnPoints");

            UpdateSpawnGroup(teamAGroup, Team.TeamA);
            UpdateSpawnGroup(teamBGroup, Team.TeamB);

            MoveActorToSpawn("Player_TeamA", teamAGroup, 0);
            for (int i = 1; i < 4; i++) MoveActorToSpawn($"BotA_{i:00}", teamAGroup, i);
            for (int i = 0; i < 4; i++) MoveActorToSpawn($"BotB_{i:00}", teamBGroup, i);
        }

        private static void UpdateSpawnGroup(Transform group, Team team)
        {
            if (group == null) return;

            float z = team == Team.TeamA ? -ArenaLayoutV2.SpawnZ : ArenaLayoutV2.SpawnZ;
            float facing = team == Team.TeamA ? 0f : 180f;
            float[] offsets = { -2.4f, -0.8f, 0.8f, 2.4f };

            for (int i = 0; i < group.childCount && i < offsets.Length; i++)
            {
                Transform t = group.GetChild(i);
                t.position = new Vector3(offsets[i], 0.22f, z);
                t.rotation = Quaternion.Euler(0f, facing, 0f);
            }
        }

        private static void MoveActorToSpawn(string actorName, Transform group, int index)
        {
            if (group == null || index < 0 || index >= group.childCount) return;

            var actor = GameObject.Find(actorName);
            if (actor == null) return;

            Transform spawn = group.GetChild(index);
            actor.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.1f, spawn.rotation);
        }

        /// <summary>把右上角小地图框改成 5:7 内容区，匹配 40×56 世界比例。</summary>
        private static void ResizeMinimapFrame()
        {
            var canvas = GameObject.Find("HUD_Canvas");
            if (canvas == null) return;

            var frame = canvas.transform.Find("MinimapFrame") as RectTransform;
            if (frame != null)
            {
                frame.sizeDelta = new Vector2(168f, 230f);
                frame.anchoredPosition = new Vector2(-104f, -135f);
            }

            var raw = frame != null ? frame.Find("MinimapRawImage") as RectTransform : null;
            if (raw != null)
            {
                raw.sizeDelta = new Vector2(156f, 218f);
                raw.anchoredPosition = new Vector2(0f, -6f);
            }
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

        /// <summary>
        /// P7-B 视觉留底：全景、出生区、中场、北端远景四张图，供人眼验收材质/光照/道具/远景。
        /// </summary>
        [MenuItem("P1/关卡与地图/渲染 P7-B 场景视觉留底图", false, 32)]
        public static void CaptureP7BVerificationShots()
        {
            UpgradeSceneVisualsP7B();
            System.IO.Directory.CreateDirectory("Logs");

            Camera cam = Camera.main;
            GameObject tempCam = null;
            if (cam == null)
            {
                tempCam = new GameObject("TempP7BRenderCam", typeof(Camera));
                cam = tempCam.GetComponent<Camera>();
            }

            Vector3 origPos = cam.transform.position;
            Quaternion origRot = cam.transform.rotation;
            float origFov = cam.fieldOfView;
            bool origOrtho = cam.orthographic;

            var surface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (surface != null)
            {
                surface.InitializeIfNeeded();
                PaintP7BInk(surface);
                surface.FlushTexture();
            }

            cam.orthographic = false;

            cam.fieldOfView = 55f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 42f, -62f), Quaternion.Euler(48f, 0f, 0f));
            RenderCameraToPng(cam, "Logs/p7b_overview.png", 1920, 1080);

            cam.fieldOfView = 42f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 9.0f, -33.0f), Quaternion.Euler(48f, 0f, 0f));
            RenderCameraToPng(cam, "Logs/p7b_spawn_teamA.png", 1920, 1080);

            cam.fieldOfView = 46f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 14f, -14f), Quaternion.Euler(46f, 0f, 0f));
            RenderCameraToPng(cam, "Logs/p7b_midfield.png", 1920, 1080);

            cam.fieldOfView = 52f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 12f, 20f), Quaternion.Euler(42f, 0f, 0f));
            RenderCameraToPng(cam, "Logs/p7b_north_vista.png", 1920, 1080);

            cam.fieldOfView = origFov;
            cam.orthographic = origOrtho;
            cam.transform.SetPositionAndRotation(origPos, origRot);
            if (tempCam != null) Object.DestroyImmediate(tempCam);

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            var uniqueMats = new HashSet<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMaterial != null)
                    uniqueMats.Add(renderers[i].sharedMaterial);
            }
            Debug.Log($"[P7B] 场景统计：Renderer={renderers.Length}，唯一材质={uniqueMats.Count}。留底图已写入 Logs/p7b_*.png。");
        }

        private static void PaintP7BInk(InkSurfaceController surface)
        {
            // Team A：南侧出生区、中线推进、西侧绕后走廊
            surface.PaintCircle(new Vector3(0f, 0f, -21f), 4.0f, Team.TeamA);
            surface.PaintCircle(new Vector3(0f, 0f, -17f), 3.0f, Team.TeamA);
            surface.PaintCircle(new Vector3(0f, 0f, -12f), 2.6f, Team.TeamA);
            surface.PaintCircle(new Vector3(0f, 0.5f, -7.4f), 2.2f, Team.TeamA);
            surface.PaintCircle(new Vector3(0f, 1.1f, -2.5f), 2.8f, Team.TeamA);
            surface.PaintCircle(new Vector3(-7.0f, 0f, -14f), 2.6f, Team.TeamA);
            surface.PaintCircle(new Vector3(-13.5f, 0f, -12f), 2.2f, Team.TeamA);
            surface.PaintCircle(new Vector3(-13.5f, 0.9f, 0f), 2.6f, Team.TeamA);
            surface.PaintCircle(new Vector3(-17.5f, 0f, -4f), 2.0f, Team.TeamA);

            // Team B：北侧出生区、中线推进、东侧绕后走廊
            surface.PaintCircle(new Vector3(0f, 0f, 21f), 4.0f, Team.TeamB);
            surface.PaintCircle(new Vector3(0f, 0f, 17f), 3.0f, Team.TeamB);
            surface.PaintCircle(new Vector3(0f, 0f, 12f), 2.6f, Team.TeamB);
            surface.PaintCircle(new Vector3(0f, 0.5f, 7.4f), 2.2f, Team.TeamB);
            surface.PaintCircle(new Vector3(0f, 1.1f, 2.5f), 2.8f, Team.TeamB);
            surface.PaintCircle(new Vector3(7.0f, 0f, 14f), 2.6f, Team.TeamB);
            surface.PaintCircle(new Vector3(13.5f, 0f, 12f), 2.2f, Team.TeamB);
            surface.PaintCircle(new Vector3(13.5f, 0.9f, 0f), 2.6f, Team.TeamB);
            surface.PaintCircle(new Vector3(17.5f, 0f, 4f), 2.0f, Team.TeamB);
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

                // 蛋形身体比旧人形矮：命中盒必须同步缩小，否则墨弹会在头顶上方"打空却判定命中"
                var cc = motor.GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.height = CharacterMetrics.ColliderHeight;
                    cc.radius = CharacterMetrics.ColliderRadius;
                    cc.center = new Vector3(0f, CharacterMetrics.ColliderCenterY, 0f);
                    cc.stepOffset = CharacterMetrics.ColliderStepOffset;
                    EditorUtility.SetDirty(cc);
                }

                motor.Configure(motor.Config, visualRoot, visualCtrl.WeaponRoot.gameObject);
                if (weapon != null)
                {
                    weapon.Configure(weapon.Config, visualCtrl.ActiveMuzzle, visualRoot);
                }
                visualCtrl.Initialize(motor, weapon, team);
                BindCharacterAnimConfig(visualCtrl);
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

                // Shot 1: 正面特写（蛋形身体连墨焰约 1.32m，相机拉到 2.45m 留出边距）
                cam.transform.position = new Vector3(0.45f, 0.70f, -2.45f);
                cam.transform.rotation = Quaternion.Euler(3f, -11f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_character_kid_front.png", 1600, 900);

                // Shot 2: 后背背包与墨焰特写
                heroGo.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
                cam.transform.position = new Vector3(-0.45f, 0.75f, -2.45f);
                cam.transform.rotation = Quaternion.Euler(5f, 11f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_character_kid_back.png", 1600, 900);

                // Shot 3: 乌贼形态特写
                heroGo.transform.rotation = Quaternion.Euler(0f, 145f, 0f);
                heroCtrl.KidRoot.SetActive(false);
                heroCtrl.SquidRoot.SetActive(true);
                heroCtrl.SquidRoot.transform.localScale = Vector3.one;
                cam.transform.position = new Vector3(0f, 0.55f, -1.0f);
                cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
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
                    float x = (i - 3) * 0.95f;
                    var cGo = new GameObject("Char_" + allKinds[i]);
                    cGo.transform.SetParent(showcaseGo.transform, false);
                    cGo.transform.position = new Vector3(x, 0f, 0f);
                    cGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                    var ctrl = CharacterVisualAssembler.Assemble(cGo.transform, i % 2 == 0 ? Team.TeamA : Team.TeamB, i % 2 == 0 ? teamAMat : teamBMat, visorMat, null);
                    ctrl.EquipWeaponKind(allKinds[i]);
                }

                cam.fieldOfView = 52f;
                cam.transform.position = new Vector3(0f, 0.95f, -5.0f);
                cam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
                RenderCameraToPng(cam, "Logs/p5a_weapons_arsenal.png", 1920, 1080);

                Debug.Log("[ProjectScaffolder] 已成功生成角色与武器多形态高清展示图：Logs/p5a_*.png");
            }
            finally
            {
                Object.DestroyImmediate(showcaseGo);
            }
        }

        /// <summary>
        /// 生成 6 帧蛋形步态接触表（触地→最高点），用于无人值守时用截图自查弹跳节奏与挤压量。
        /// 直接采样 EggGaitSolver 并写回骨架，不依赖进入 PlayMode。
        /// </summary>
        [MenuItem("P1/角色与武器/生成蛋仔步态序列图 (P8-D)", false, 32)]
        public static void CaptureEggGaitSheet()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!activeScene.isLoaded || string.IsNullOrEmpty(activeScene.path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            }

            var teamAMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_TeamA_Body.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Mat_VisorDark.mat");

            var showcaseGo = new GameObject("__GaitSheet_P8D__");
            try
            {
                var heroGo = new GameObject("GaitHero");
                heroGo.transform.SetParent(showcaseGo.transform, false);
                heroGo.transform.position = Vector3.zero;
                heroGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                var ctrl = CharacterVisualAssembler.Assemble(heroGo.transform, Team.TeamA, teamAMat, visorMat, null);
                ctrl.EquipWeaponKind(WeaponKind.Shooter);

                Transform bodySquash = ctrl.BodySquashTransform;
                Transform chest = ctrl.KidRoot.transform.Find("Chest");
                Transform leftArm = chest != null ? chest.Find("LeftArm") : null;
                Transform rightArm = chest != null ? chest.Find("RightArm") : null;
                // P22-A：双腿 FK 链（旧场景可能没有，逐节点判空）
                Transform hips = chest != null ? chest.Find("Hips") : null;
                Transform leftLeg = hips != null ? hips.Find("LeftLeg") : null;
                Transform rightLeg = hips != null ? hips.Find("RightLeg") : null;
                Transform leftKnee = leftLeg != null ? leftLeg.Find("Knee") : null;
                Transform rightKnee = rightLeg != null ? rightLeg.Find("Knee") : null;
                Transform leftAnkle = leftKnee != null ? leftKnee.Find("Ankle") : null;
                Transform rightAnkle = rightKnee != null ? rightKnee.Find("Ankle") : null;
                if (bodySquash == null || chest == null)
                {
                    Debug.LogError("[ProjectScaffolder] 蛋形骨架节点缺失，无法生成步态序列图");
                    return;
                }

                Vector3 squashBasePos = bodySquash.localPosition;
                Vector3 squashBaseScale = bodySquash.localScale;
                Quaternion chestBaseRot = chest.localRotation;
                Quaternion leftBaseRot = leftArm != null ? leftArm.localRotation : Quaternion.identity;
                Quaternion rightBaseRot = rightArm != null ? rightArm.localRotation : Quaternion.identity;
                Quaternion leftLegBaseRot = leftLeg != null ? leftLeg.localRotation : Quaternion.identity;
                Quaternion rightLegBaseRot = rightLeg != null ? rightLeg.localRotation : Quaternion.identity;
                Quaternion leftKneeBaseRot = leftKnee != null ? leftKnee.localRotation : Quaternion.identity;
                Quaternion rightKneeBaseRot = rightKnee != null ? rightKnee.localRotation : Quaternion.identity;
                Quaternion leftAnkleBaseRot = leftAnkle != null ? leftAnkle.localRotation : Quaternion.identity;
                Quaternion rightAnkleBaseRot = rightAnkle != null ? rightAnkle.localRotation : Quaternion.identity;

                var camGo = new GameObject("GaitCam");
                camGo.transform.SetParent(showcaseGo.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
                cam.fieldOfView = 34f;
                cam.transform.position = new Vector3(0.45f, 0.70f, -2.45f);
                cam.transform.rotation = Quaternion.Euler(3f, -11f, 0f);

                System.IO.Directory.CreateDirectory("Logs");

                const float speed01 = 0.5f;   // 对应约 5.9 m/s 的正常跑速
                const float leanPitch = 6f;
                for (int frame = 0; frame < 6; frame++)
                {
                    float phase = frame * Mathf.PI * 0.25f;   // 0° / 45° / 90° / 135° / 180° / 225°
                    EggGaitPose pose = EggGaitSolver.Sample(speed01, phase);

                    bodySquash.localPosition = squashBasePos + Vector3.up * pose.BobY;
                    bodySquash.localScale = Vector3.Scale(squashBaseScale, new Vector3(pose.SquashXZ, pose.SquashY, pose.SquashXZ));
                    chest.localRotation = chestBaseRot * Quaternion.Euler(leanPitch, 0f, pose.RockDeg);
                    if (leftArm != null) leftArm.localRotation = leftBaseRot * Quaternion.Euler(pose.ArmSwingDeg, 0f, 0f);
                    if (rightArm != null) rightArm.localRotation = rightBaseRot * Quaternion.Euler(-pose.ArmSwingDeg, 0f, 0f);
                    // P22-A：腿部与控制器 UpdateBodyPose 相同的符号约定（X 正 = 后摆/屈膝/脚尖下垂；
                    // 左腿与左臂反相），膝弯峰值落在双腿穿过躯干下方的收步相位
                    if (leftLeg != null) leftLeg.localRotation = leftLegBaseRot * Quaternion.Euler(-pose.LegSwingDeg, 0f, 0f);
                    if (rightLeg != null) rightLeg.localRotation = rightLegBaseRot * Quaternion.Euler(pose.LegSwingDeg, 0f, 0f);
                    if (leftKnee != null) leftKnee.localRotation = leftKneeBaseRot * Quaternion.Euler(pose.KneeBendDeg, 0f, 0f);
                    if (rightKnee != null) rightKnee.localRotation = rightKneeBaseRot * Quaternion.Euler(pose.KneeBendDeg, 0f, 0f);
                    if (leftAnkle != null) leftAnkle.localRotation = leftAnkleBaseRot * Quaternion.Euler(pose.FootPitchDeg, 0f, 0f);
                    if (rightAnkle != null) rightAnkle.localRotation = rightAnkleBaseRot * Quaternion.Euler(pose.FootPitchDeg, 0f, 0f);

                    RenderCameraToPng(cam, $"Logs/p8d_gait_{frame}.png", 900, 900);
                }

                Debug.Log("[ProjectScaffolder] 已生成蛋仔步态序列图：Logs/p8d_gait_0~5.png");
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

            // 批处理下第一次 cam.Render() 的光照/环境探针尚未就绪，会拍出整体偏暗偏色的图；
            // 先丢弃一帧做预热，再正式采样，自检图的配色才可信。
            cam.Render();
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
            cc.height = CharacterMetrics.ColliderHeight;
            cc.radius = CharacterMetrics.ColliderRadius;
            cc.center = new Vector3(0f, CharacterMetrics.ColliderCenterY, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = CharacterMetrics.ColliderStepOffset;

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
            BindCharacterAnimConfig(visualCtrl);

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

        /// <summary>
        /// 绑定角色动画调参配置（CharacterAnimConfig.asset）：资产存在则绑定，缺失时跳过——
        /// CharacterVisualController 运行时回退到 INKWAVE 数值的共享默认实例，绝不因缺资产起不来。
        /// </summary>
        private static void BindCharacterAnimConfig(CharacterVisualController visualCtrl)
        {
            var animCfg = AssetDatabase.LoadAssetAtPath<CharacterAnimConfigSO>(CharacterAnimConfigTools.AnimConfigPath);
            if (animCfg != null)
            {
                visualCtrl.BindAnimationConfig(animCfg);
            }
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
            var minimapFrame = CreateUiPanel(canvasGo.transform, "MinimapFrame", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-104f, -135f), new Vector2(168f, 230f), new Color(0.06f, 0.08f, 0.12f, 0.90f));
            CreateUiText(minimapFrame, "MinimapTitle", font, "实时全图涂地小地图", 15, TextAnchor.UpperCenter, new Vector2(0f, 96f), new Vector2(150f, 24f), new Color(0.82f, 0.88f, 0.96f, 1f));

            var rawGo = new GameObject("MinimapRawImage", typeof(RectTransform), typeof(RawImage));
            rawGo.transform.SetParent(minimapFrame, false);
            var rawRect = rawGo.GetComponent<RectTransform>();
            rawRect.sizeDelta = new Vector2(156f, 218f);
            rawRect.anchoredPosition = new Vector2(0f, -6f);
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
            CreateUiText(bottomPanel, "ControlsHintText", font, "操作：[WASD]移动  [鼠标]转视角  [左键]开火  [右键/Shift]己墨潜行回墨·贴着己方墨墙可潜上墙  [空格]跳跃  [E/中键]抛延时手雷  [Q]大招  [Esc]暂停", 14, TextAnchor.MiddleCenter, new Vector2(0f, -38f), new Vector2(740f, 22f), new Color(0.80f, 0.86f, 0.95f, 1f));

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

        /// <summary>
        /// 按资产路径优先加载自定义 Shader：AssetDatabase 刷新/编译期间 Shader.Find 可能返回 null，
        /// 导致材质静默回退到 URP/Lit（地面就会失去涂地能力）。
        /// </summary>
        /// <summary>把风格档案里的 sRGB 美术色转换成线性空间，避免线性工程里整体发白/过曝。</summary>
        private static Color Linear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        private static Shader FindArenaShader(string fileName, string shaderName, string fallbackName)
        {
            Shader sh = AssetDatabase.LoadAssetAtPath<Shader>($"Assets/_Project/Shaders/{fileName}");
            if (sh == null) sh = Shader.Find(shaderName);
            if (sh == null) sh = Shader.Find(fallbackName);
            return sh;
        }

        private static Material CreateOrUpdateGroundMaterial(ArenaStyleSO style)
        {
            string path = $"{MatFolder}/Mat_InkGround.mat";
            Shader sh = FindArenaShader("InkGround.shader", "P1/InkGround", "Universal Render Pipeline/Lit");

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

            Color baseColor = style != null ? style.groundBase : new Color(0.72f, 0.69f, 0.62f, 1f);
            Color seamColor = style != null ? style.groundSeam : new Color(0.52f, 0.50f, 0.45f, 1f);
            Color cliffColor = style != null ? style.groundCliff : new Color(0.34f, 0.33f, 0.31f, 1f);

            // 有烘焙贴图时 _BaseColor 只作为白色乘数，避免把 sRGB 地面再乘暗一次。
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_GridLineColor", Linear(seamColor));
            mat.SetColor("_CliffColor", Linear(cliffColor));
            mat.SetFloat("_GroundTexStrength", 1f);
            mat.color = baseColor;

            var groundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArenaGroundTextureBaker.GroundTexturePath);
            if (groundTex != null)
            {
                mat.SetTexture("_GroundTex", groundTex);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>P7-B：装饰道具主材质（顶点色 × 基础色，可设置金属度/自发光/双面）。</summary>
        private static Material CreateOrUpdatePropMaterial(string path, Color baseColor, float smoothness, float metallic,
            Color emissionColor, float emissionStrength, float cull)
        {
            Shader sh = FindArenaShader("PropLit.shader", "P1/PropLit", "Universal Render Pipeline/Lit");

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

            mat.SetColor("_BaseColor", Linear(baseColor));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetColor("_EmissionColor", Linear(emissionColor));
            mat.SetFloat("_EmissionStrength", emissionStrength);
            mat.SetFloat("_AmbientBoost", 1f);
            mat.SetFloat("_Cull", cull);
            mat.color = Linear(baseColor);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// P7-B：出生台圆盘材质（P11-C：中性钢灰出生池，与码头风格协调，不带队色；
        /// 白色圆环 + 警戒黄导向箭头 + 混凝土基台外缘）。teamColor 参数保留兼容旧调用。
        /// </summary>
        private static Material CreateOrUpdateSpawnPadMaterial(string path, Color teamColor)
        {
            Shader sh = FindArenaShader("SpawnPad.shader", "P1/SpawnPad", "Universal Render Pipeline/Unlit");

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

            ArenaStyleSO style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(ArenaStylePath);
            mat.SetColor("_BaseColor", Linear(new Color(0.30f, 0.33f, 0.38f, 1f)));
            mat.SetColor("_PlatformColor", Linear(style != null ? style.concreteMid : new Color(0.65f, 0.63f, 0.58f, 1f)));
            mat.SetColor("_RingColor", Linear(new Color(0.92f, 0.93f, 0.95f, 1f)));
            mat.SetColor("_ChevronColor", Linear(style != null ? style.hazardYellow : new Color(0.86f, 0.68f, 0.20f, 1f)));
            mat.SetFloat("_GlowStrength", 0.30f);
            mat.color = Linear(new Color(0.30f, 0.33f, 0.38f, 1f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateWaterMaterial(ArenaStyleSO style)
        {
            string path = $"{MatFolder}/Mat_Water.mat";
            Shader sh = FindArenaShader("Water.shader", "P1/Water", "Universal Render Pipeline/Unlit");

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

            mat.SetColor("_ShallowColor", Linear(style != null ? style.waterShallow : new Color(0.20f, 0.55f, 0.62f, 1f)));
            mat.SetColor("_DeepColor", Linear(style != null ? style.waterDeep : new Color(0.05f, 0.24f, 0.36f, 1f)));
            mat.SetColor("_HorizonColor", Linear(style != null ? style.waterHorizon : new Color(0.66f, 0.78f, 0.84f, 1f)));
            mat.SetFloat("_WaveScale", 1f);
            mat.SetFloat("_WaveSpeed", 0.15f);
            mat.SetFloat("_WaveStrength", 0.25f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateSkyboxMaterial(ArenaStyleSO style)
        {
            string path = $"{MatFolder}/Mat_Skybox.mat";
            Shader sh = FindArenaShader("SkyGradient.shader", "P1/SkyGradient", "Skybox/Procedural");

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

            if (style != null)
            {
                mat.SetColor("_TopColor", Linear(style.skyTop));
                mat.SetColor("_HorizonColor", Linear(style.skyHorizon));
                mat.SetColor("_GroundColor", Linear(style.skyGround));
                mat.SetColor("_SunColor", Linear(style.sunDisc));
                mat.SetColor("_CloudColor", Linear(style.cloudColor));
                mat.SetFloat("_CloudStrength", style.cloudStrength);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateBannerMaterial()
        {
            string path = $"{MatFolder}/Mat_BannerAtlas.mat";
            Shader sh = FindArenaShader("DecalUnlit.shader", "P1/DecalUnlit", "Universal Render Pipeline/Unlit");

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

            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_AlphaCutoff", 0.35f);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(ArenaGroundTextureBaker.BannerAtlasPath);
            if (atlas != null)
            {
                mat.SetTexture("_BaseMap", atlas);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateInkWallMaterial(string path)
        {
            Shader sh = FindArenaShader("InkWall.shader", "P1/InkWall", "Universal Render Pipeline/Unlit");

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
            Shader sh = FindArenaShader("ObstacleDitherLit.shader", "P1/ObstacleDitherLit", "Universal Render Pipeline/Lit");

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

            mat.SetColor("_BaseColor", Linear(baseColor));
            mat.SetColor("_TrimColor", Linear(trimColor));
            mat.SetFloat("_Smoothness", 0.38f);
            mat.SetFloat("_Metallic", 0.12f);
            mat.SetFloat("_EnableCorrugation", corrugation);
            mat.SetFloat("_CorrugationScale", 2.5f);
            mat.SetFloat("_CorrugationDepth", 0.28f);
            mat.SetFloat("_DitherAlpha", 1.0f);
            mat.color = Linear(baseColor);
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

        /// <summary>P7-B 诊断：打印全部自定义 Shader 的解析状态与关键属性，便于定位 ShaderLab 解析失败。</summary>
        [MenuItem("P1/诊断/打印 P7-B Shader 解析状态", false, 91)]
        public static void DiagnoseArenaShaders()
        {
            string[] names =
            {
                "P1/InkGround", "P1/ObstacleDitherLit", "P1/InkWall", "P1/PropLit",
                "P1/SkyGradient", "P1/Water", "P1/DecalUnlit", "P1/SpawnPad"
            };

            foreach (string name in names)
            {
                Shader sh = Shader.Find(name);
                int propCount = sh != null ? sh.GetPropertyCount() : -1;
                Debug.Log($"[P7B-Diag] Shader.Find({name}) => {(sh != null ? sh.name : "NULL")}, properties={propCount}");

                if (sh == null || propCount <= 0) continue;

                for (int i = 0; i < propCount; i++)
                {
                    string p = sh.GetPropertyName(i);
                    if (p.Contains("Ground") || p.Contains("Cliff") || p.Contains("Dither") ||
                        p.Contains("Edge") || p.Contains("Panel") || p.Contains("Emissive") || p.Contains("Cull"))
                    {
                        Debug.Log($"[P7B-Diag]   {name}.{p}");
                    }
                }
            }
        }

        /// <summary>P7-B 诊断：检查合并网格的顶点色与场景材质绑定是否生效。</summary>
        [MenuItem("P1/诊断/打印 P7-B 网格与材质", false, 92)]
        public static void DiagnoseArenaMeshes()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(ArenaStylePath);
            if (style != null)
            {
                Debug.Log($"[P7B-Diag] colorSpace={QualitySettings.activeColorSpace}, " +
                          $"groundBase={style.groundBase}, gamma={style.groundBase.gamma}, linear={style.groundBase.linear}, wood={style.wood}");
            }

            Material LoadMat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{n}");
            Material groundMatForDiag = LoadMat("Mat_InkGround.mat");
            Material cm = LoadMat("Mat_Container_Teal.mat");
            Material pm = LoadMat("Mat_Prop_Matte.mat");
            Debug.Log($"[P7B-Diag] Mat_InkGround shader={(groundMatForDiag != null ? groundMatForDiag.shader.name : "NULL")} " +
                      $"base={(groundMatForDiag != null ? groundMatForDiag.GetColor("_BaseColor").ToString() : "-")} " +
                      $"tex={(groundMatForDiag != null && groundMatForDiag.GetTexture("_GroundTex") != null ? groundMatForDiag.GetTexture("_GroundTex").name : "NULL")}");
            Debug.Log($"[P7B-Diag] Mat_Container_Teal base={(cm != null ? cm.GetColor("_BaseColor").ToString() : "-")}, " +
                      $"Mat_Prop_Matte base={(pm != null ? pm.GetColor("_BaseColor").ToString() : "-")}");

            Mesh Decor(string file) => AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/_Project/Art/Meshes/Arena/{file}");

            foreach (string file in new[] { "arena_decor_matte.asset", "arena_backdrop_matte.asset" })
            {
                Mesh m = Decor(file);
                int colorCount = m != null ? m.colors.Length : 0;
                int vertCount = m != null ? m.vertexCount : 0;
                Debug.Log($"[P7B-Diag] {file}: verts={vertCount}, colors={colorCount}");
                if (m != null && colorCount > 0)
                {
                    Color[] c = m.colors;
                    Debug.Log($"[P7B-Diag]   c[0]={c[0]}, c[mid]={c[c.Length / 2]}, c[last]={c[c.Length - 1]}");
                }
            }

            var pier = GameObject.Find("Backdrop_Matte");
            var pierMr = pier != null ? pier.GetComponent<MeshRenderer>() : null;
            Debug.Log($"[P7B-Diag] Backdrop_Matte mat={(pierMr != null && pierMr.sharedMaterial != null ? pierMr.sharedMaterial.name : "NULL")} " +
                      $"base={(pierMr != null && pierMr.sharedMaterial != null ? pierMr.sharedMaterial.GetColor("_BaseColor").ToString() : "-")}");

            var decor = GameObject.Find("Decor_Matte");
            var decorMr = decor != null ? decor.GetComponent<MeshRenderer>() : null;
            Debug.Log($"[P7B-Diag] Decor_Matte mat={(decorMr != null && decorMr.sharedMaterial != null ? decorMr.sharedMaterial.name : "NULL")} " +
                      $"base={(decorMr != null && decorMr.sharedMaterial != null ? decorMr.sharedMaterial.GetColor("_BaseColor").ToString() : "-")}");

            var ground = GameObject.Find("Ground_Main_40x56");
            var groundMr = ground != null ? ground.GetComponent<MeshRenderer>() : null;
            Material gm = groundMr != null ? groundMr.sharedMaterial : null;
            Debug.Log($"[P7B-Diag] Ground mat={(gm != null ? gm.shader.name : "NULL")} base={(gm != null ? gm.GetColor("_BaseColor").ToString() : "-")} " +
                      $"tex={(gm != null && gm.GetTexture("_GroundTex") != null ? gm.GetTexture("_GroundTex").name : "NULL")}");
        }

        /// <summary>P7-B 诊断：逐层隐藏对象截屏，精准定位全景图中的发白区域来源。</summary>
        [MenuItem("P1/诊断/逐层隔离排查发白区域", false, 93)]
        public static void LocateGlowObject()
        {
            UpgradeSceneVisualsP7B();

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 origPos = cam.transform.position;
            Quaternion origRot = cam.transform.rotation;
            float origFov = cam.fieldOfView;
            bool origOrtho = cam.orthographic;

            cam.orthographic = false;
            cam.fieldOfView = 55f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 42f, -62f), Quaternion.Euler(48f, 0f, 0f));

            var backdrop = GameObject.Find("_ArenaBackdrop");
            var decor = GameObject.Find("Arena_Decor");
            var atmosphere = GameObject.Find("_Atmosphere");

            // 1. 基准图（全开）
            RenderCameraToPng(cam, "Logs/diag_0_all_on.png", 960, 540);

            // 2. 隐藏 _ArenaBackdrop
            if (backdrop != null) backdrop.SetActive(false);
            RenderCameraToPng(cam, "Logs/diag_1_no_backdrop.png", 960, 540);
            if (backdrop != null) backdrop.SetActive(true);

            // 3. 隐藏 _Atmosphere (Volume 后处理)
            if (atmosphere != null) atmosphere.SetActive(false);
            RenderCameraToPng(cam, "Logs/diag_2_no_post.png", 960, 540);
            if (atmosphere != null) atmosphere.SetActive(true);

            // 4. 隐藏 Backdrop_Water
            var water = GameObject.Find("Backdrop_Water");
            if (water != null) water.SetActive(false);
            RenderCameraToPng(cam, "Logs/diag_3_no_water.png", 960, 540);
            if (water != null) water.SetActive(true);

            // 5. 隐藏 Backdrop_Matte
            var bMatte = GameObject.Find("Backdrop_Matte");
            if (bMatte != null) bMatte.SetActive(false);
            RenderCameraToPng(cam, "Logs/diag_4_no_bmatte.png", 960, 540);
            if (bMatte != null) bMatte.SetActive(true);

            cam.fieldOfView = origFov;
            cam.orthographic = origOrtho;
            cam.transform.SetPositionAndRotation(origPos, origRot);

            Debug.Log("[P7B-Diag] 隔离排查截图已输出到 Logs/diag_*.png。");
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
