using System;
using System.Collections.Generic;
using System.IO;
using P1.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Editor
{
    /// <summary>
    /// 「霓虹滑板场」（Neon Harbor Skatepark）专属海滨工业远景构建器（第 2 轮构图精修）：
    /// 严格对齐参考原画（Angle A 俯视与 Angle B 反向俯视）的构图特征，并满足构图微调规范：
    /// 1. 远景去除近景高耸大绿丘，天际线保持开阔通透，远景山丘仅在极远边界（z>130m 或 |x|>100m）保留低平灰绿轮廓（高度<6m）；
    /// 2. 东侧海滨城市整体向东平移 10-14m（最近建筑立面 x≥44m），留出 >14m 宽阔海滨街道缓冲带；最高楼降至 ~22m；采用深砖红/深棕/暖灰与米白浅灰配色，剔除鲑鱼粉；
    /// 3. 南侧工业仓储区（立式储罐/锯齿形厂房/拖头卡车）全部南撤至 z≤-48m 且 x≥30m，彻底清空俯视相机前景，露出宽广湛蓝的西南港池；
    /// 4. 4 座 22-28m 集装箱岸桥改用「钢青自动化码头」配色：深钢青门架/主梁 + 纯白机房（2 道深色横条纹）
    ///    + 港青品牌色带 + 黄黑警示防撞台车。不再使用整片警示黄 —— 场内已有 2 座黄色英雄塔吊，
    ///    远景岸桥若同色会与英雄地标抢戏，整个北天际线糊成一团黄；深钢青与港池水色/蓝色集装箱同族，整体更协调。
    /// 5. 严格遵守安全包络线：任何远景几何均不进入 x∈[-24, 24], z∈[-32, 32]，绝不遮挡俯视对局全景。
    ///
    /// 性能与架构契约：
    /// - 静态、无 Collider、无阴影投射/接收（ShadowCastingMode.Off / receiveShadows=false）；
    /// - 全场按材质严格合批为 Water、Matte、Metal、Glass 四张合并网格资产；
    /// - 全场仅 4 个渲染子物体，Draw Call 极低，完美适配 URP 与 SRP Batcher。
    /// </summary>
    public static class SkateparkBackdropBuilder
    {
        public const string MeshFolder = "Assets/_Project/Art/Meshes/Arena";

        #region 公开构建入口
        /// <summary>
        /// 构建霓虹滑板场专属港口远景。
        /// </summary>
        public static GameObject Build(
            Transform parent,
            ArenaStyleSO style,
            ArenaMaterialSet mats,
            string meshPrefix = "skate_backdrop")
        {
            if (style != null && !style.buildBackdrop)
            {
                return null;
            }

            var backdropRoot = new GameObject("_SkateBackdrop");
            if (parent != null)
            {
                backdropRoot.transform.SetParent(parent, false);
            }

            EnsureFolder();

            var palette = new BackdropPalette(style, mats);

            // 1. 水面网格（900x900m，y=-1.35，中央挖去碗池开口）
            // 旧实现是整张 240x240 满铺 Quad：碗池底部 y=-2.1 低于水面 y=-1.35，
            // 水会灌满整个碗池，俯视读作「蓝色水池」而不是下沉碗池（地图卡预览同病）。
            // 现在在水面上挖出碗池开口（24x16m，略大于 22x14m 池口，边缘藏在 y=0 地面下），
            // 并把水域扩到 900m，让水缘落到 380m 雾末之外，消除地平线处那条硬切水带。
            Mesh waterMesh = BuildWaterMesh(900f, -1.35f, meshPrefix + "_water");
            SaveAsset(waterMesh, $"{MeshFolder}/{meshPrefix}_water.asset");
            CreateRendererChild(backdropRoot.transform, "SkateBackdrop_Water", waterMesh, mats?.water);

            // 2. 远景按 Matte、Metal、Glass 三大材质分桶
            var matteBucket = new MeshCombiner();
            var metalBucket = new MeshCombiner();
            var glassBucket = new MeshCombiner();

            // 2.1 码头基座（严格位于 x≤-24.5 或 |z|≥33，包含西侧泊位、护舷、系船柱、登船舷梯、高压线杆）
            BuildPierBase(matteBucket, metalBucket, palette);

            // 2.2 北侧高饱和集装箱堆场（z≥34m，4排进深、行车铁轨、高杆照明塔、调度岗亭）
            BuildContainerTerminal(matteBucket, metalBucket, glassBucket, palette);

            // 2.3 4座大型黄色岸桥（z≥42m，白机房+双暗横条，黄黑警示台车，悬空起吊姿态）
            BuildGantryCranes(metalBucket, matteBucket, glassBucket, palette);

            // 2.4 西侧外海水域船队（x≤-28m，46m集装箱船、13m拖船、近海驳船、航标）
            BuildShipsAndFleet(matteBucket, metalBucket, glassBucket, palette);

            // 2.5 东侧海滨城市街区（最近立面 x≥44m，最高楼~22m，深砖红/深棕/暖灰/米白，绿色工业大管道）
            BuildEastCityBlock(matteBucket, metalBucket, glassBucket, palette);

            // 2.6 南侧工业仓储区（全退至 z≤-48m 且 x≥30m，锯齿形天窗厂房、立式储罐与管廊、拖头卡车）
            BuildSouthIndustrial(matteBucket, metalBucket, glassBucket, palette);

            // 2.6b 南侧出生台后方港湾库场（z≤-34m）：给南出生点一个实体背景，
            // 不再让出生台背面的 3 个集装箱悬在空水面上。
            BuildSouthSpawnHarbor(matteBucket, metalBucket, glassBucket, palette);

            // 2.7 远景天际线（删除近景大绿山，仅在 z>130m / |x|>100m 保留高度<6m的低平灰绿轮廓，极远岸桥骨架，防波堤）
            BuildFarHorizon(matteBucket, metalBucket, palette);

            // 3. 构建并保存合并资产，挂载渲染器
            Mesh matteMesh = matteBucket.BuildMesh(meshPrefix + "_matte");
            Mesh metalMesh = metalBucket.BuildMesh(meshPrefix + "_metal");
            Mesh glassMesh = glassBucket.BuildMesh(meshPrefix + "_glass");

            SaveAsset(matteMesh, $"{MeshFolder}/{meshPrefix}_matte.asset");
            SaveAsset(metalMesh, $"{MeshFolder}/{meshPrefix}_metal.asset");
            if (glassMesh != null)
            {
                SaveAsset(glassMesh, $"{MeshFolder}/{meshPrefix}_glass.asset");
            }

            CreateRendererChild(backdropRoot.transform, "SkateBackdrop_Matte", matteMesh, mats?.propMatte ?? mats?.concrete);
            CreateRendererChild(backdropRoot.transform, "SkateBackdrop_Metal", metalMesh, mats?.propMetal ?? mats?.steel);
            if (glassMesh != null)
            {
                CreateRendererChild(backdropRoot.transform, "SkateBackdrop_Glass", glassMesh, mats?.glass ?? mats?.propMetal ?? mats?.steel);
            }

            return backdropRoot;
        }

        /// <summary>
        /// 在霓虹滑板场场景中只重建远景（不整场重建，不重烘地面/材质）：
        /// 销毁旧 _SkateBackdrop，按当前画风与 SP 材质重新合并网格并覆盖同名网格资产后保存场景。
        /// 供远景观感迭代使用，避免每次都跑整场「生成新地图」。
        /// </summary>
        [MenuItem("P1/关卡与地图/重建霓虹滑板场远景 (Skate Backdrop)", false, 33)]
        public static void RebuildSkateparkBackdropInOpenScene()
        {
            const string skateScenePath = "Assets/_Project/Scenes/NeonSkatepark.unity";
            const string skateStylePath = "Assets/_Project/ScriptableObjects/ArenaStyle_NeonSkatepark.asset";
            const string matFolder = "Assets/_Project/Materials";

            var active = EditorSceneManager.GetActiveScene();
            if (!string.Equals(active.path, skateScenePath, StringComparison.Ordinal))
            {
                if (active.isDirty)
                {
                    EditorSceneManager.SaveScene(active);
                }
                EditorSceneManager.OpenScene(skateScenePath, OpenSceneMode.Single);
            }

            GameObject old = GameObject.Find("_SkateBackdrop");
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old);
            }

            ArenaStyleSO style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(skateStylePath);
            Material Load(string file) => AssetDatabase.LoadAssetAtPath<Material>($"{matFolder}/{file}");
            var mats = new ArenaMaterialSet
            {
                water = Load("Mat_SP_Water.mat"),
                propMatte = Load("Mat_Prop_Matte.mat"),
                propMetal = Load("Mat_Prop_Metal.mat"),
                concrete = Load("Mat_SP_Concrete.mat"),
                steel = Load("Mat_SP_Steel.mat"),
                glass = Load("Mat_SP_Glass.mat"),
            };

            GameObject root = Build(null, style, mats, "skate_backdrop");
            if (root == null)
            {
                Debug.LogWarning("[SkateparkBackdrop] buildBackdrop=false 或构建失败，远景未生成。");
                return;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SkateparkBackdrop] 霓虹滑板场远景已重建并保存：{scene.path}");
        }
        #endregion

        #region 水面网格生成
        /// <summary>
        /// 四块 Quad 拼成带中央开口的水面。开口对应 40x56 场地下沉碗池（22x14m），
        /// 尺寸放大到 24x16m，让水缘完全压在 y=0 的地面块下方，从场内看不到水缝。
        /// UV 用世界坐标 / 15m，和旧 240m/16 次重复保持同一波浪尺度；外扩到 900m 后不会拉伸。
        /// </summary>
        private static Mesh BuildWaterMesh(float size, float yPos, string meshName)
        {
            var comb = new MeshCombiner();
            float h = size * 0.5f;
            const float holeHalfX = 12.0f;
            const float holeHalfZ = 8.0f;
            const float uvMetersPerTile = 15f;

            void AddQuad(float x0, float z0, float x1, float z1)
            {
                Vector3 v0 = new Vector3(x0, yPos, z0);
                Vector3 v1 = new Vector3(x1, yPos, z0);
                Vector3 v2 = new Vector3(x1, yPos, z1);
                Vector3 v3 = new Vector3(x0, yPos, z1);
                comb.AddQuadRaw(v0, v1, v2, v3, Vector3.up,
                    new Vector2(x0 / uvMetersPerTile, z0 / uvMetersPerTile),
                    new Vector2(x1 / uvMetersPerTile, z0 / uvMetersPerTile),
                    new Vector2(x1 / uvMetersPerTile, z1 / uvMetersPerTile),
                    new Vector2(x0 / uvMetersPerTile, z1 / uvMetersPerTile),
                    Color.white);
            }

            // 北 / 南两条横带
            AddQuad(-h, holeHalfZ, h, h);
            AddQuad(-h, -h, h, -holeHalfZ);
            // 西 / 东两块侧带
            AddQuad(-h, -holeHalfZ, -holeHalfX, holeHalfZ);
            AddQuad(holeHalfX, -holeHalfZ, h, holeHalfZ);

            return comb.BuildMesh(meshName);
        }
        #endregion

        #region 模块 1：码头基座与临水护舷（安全包络：全在 x≤-24.5 或 |z|≥33）
        /// <summary>
        /// 码头混凝土高台基座：
        /// 严格不进入 x∈[-24, 24], z∈[-32, 32] 保护区。
        /// 西侧作业码头位于 x∈[-28.0, -24.5]，立壁在 x=-27.5，临海面设防撞块、系船柱、登船舷梯与沿岸电线杆。
        /// </summary>
        private static void BuildPierBase(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            const float topY = -0.05f;
            const float bottomY = -2.50f;
            const float height = topY - bottomY;
            float wallCenterY = (topY + bottomY) * 0.5f;

            // ---- 1. 西侧码头作业前沿板 (x: -28.0 ~ -24.5，z: -32.0 ~ 33.0) ----
            // 在 z: [2, 12] 预留凹进装卸泊位 (平台下沉至 y=-0.80)
            matteComb.AddBox(new Vector3(-26.25f, topY - 0.08f, -14.0f), new Vector3(3.5f, 0.16f, 32.0f), pal.ConcreteMid);
            matteComb.AddBox(new Vector3(-26.25f, topY - 0.08f, 22.5f),  new Vector3(3.5f, 0.16f, 21.0f), pal.ConcreteMid);
            // 凹进内泊位下沉平台 (水上小码头台面)
            matteComb.AddBox(new Vector3(-26.25f, -0.80f, 7.0f), new Vector3(3.5f, 0.20f, 10.0f), pal.ConcreteDark);
            // 凹进泊位内侧连接小台阶 (从 y=-0.80 上升至 -0.05)
            for (int step = 0; step < 4; step++)
            {
                float sy = -0.80f + (step + 1) * 0.18f;
                float sx = -25.2f + step * 0.22f;
                matteComb.AddBox(new Vector3(sx, sy, 7.0f), new Vector3(0.24f, 0.20f, 6.0f), pal.ConcreteMid);
            }

            // ---- 2. 西侧外立壁与深基础挡水墙 (x = -27.5m，面向 -X 外海，深至 y=-2.5m) ----
            matteComb.AddBox(new Vector3(-27.6f, wallCenterY, -14.0f), new Vector3(0.30f, height, 32.0f), pal.ConcreteDark);
            matteComb.AddBox(new Vector3(-27.6f, wallCenterY, 22.5f),  new Vector3(0.30f, height, 21.0f), pal.ConcreteDark);
            // 凹进泊位立壁
            matteComb.AddBox(new Vector3(-24.6f, wallCenterY, 7.0f),  new Vector3(0.30f, height, 10.0f), pal.ConcreteDark);
            matteComb.AddBox(new Vector3(-26.1f, wallCenterY, 2.0f),  new Vector3(3.0f, height, 0.30f),  pal.ConcreteDark);
            matteComb.AddBox(new Vector3(-26.1f, wallCenterY, 12.0f), new Vector3(3.0f, height, 0.30f),  pal.ConcreteDark);

            // 西侧外码头南端闭合山墙 (z = -32.0m, x: -28.0 ~ -24.5)
            matteComb.AddBox(new Vector3(-26.25f, wallCenterY, -32.1f), new Vector3(3.5f, height, 0.30f), pal.ConcreteDark);

            // ---- 3. 西侧边缘压顶界石、黄黑警戒线、防撞护舷与系船柱 ----
            const float curbH = 0.22f;
            const float curbW = 0.40f;
            float curbY = topY + curbH * 0.5f;

            // 沿口界石
            matteComb.AddBox(new Vector3(-27.3f, curbY, -14.0f), new Vector3(curbW, curbH, 32.0f), pal.ConcreteLight);
            matteComb.AddBox(new Vector3(-27.3f, curbY, 22.5f),  new Vector3(curbW, curbH, 21.0f), pal.ConcreteLight);
            matteComb.AddBox(new Vector3(-27.3f, topY + curbH + 0.01f, -14.0f), new Vector3(0.16f, 0.02f, 32.0f), pal.HazardYellow);
            matteComb.AddBox(new Vector3(-27.3f, topY + curbH + 0.01f, 22.5f),  new Vector3(0.16f, 0.02f, 21.0f), pal.HazardYellow);

            // 橡胶防撞护舷块 (贴在 x=-27.8m 外壁上)
            Vector3 fenderSz = new Vector3(0.45f, 0.90f, 0.65f);
            Color fenderCol = pal.HazardDark;
            for (float z = -28f; z <= 30f; z += 5.2f)
            {
                if (z >= 1.5f && z <= 12.5f) continue; // 避开凹进小港湾
                matteComb.AddBox(new Vector3(-27.8f, -0.75f, z), fenderSz, fenderCol);
            }

            // 铸铁系船柱 (Bollards)
            float bollardR = 0.18f;
            float bollardH = 0.55f;
            for (float z = -26f; z <= 28f; z += 6.5f)
            {
                if (z >= 0.5f && z <= 13.5f) continue;
                Vector3 bp = new Vector3(-27.15f, topY + curbH, z);
                metalComb.AddCylinder(bp, bollardR, bollardH, 8, pal.SteelDark);
                metalComb.AddBox(bp + new Vector3(0f, bollardH * 0.82f, 0f), new Vector3(0.16f, 0.12f, 0.52f), pal.SteelDark);
            }

            // 垂直钢爬梯
            float[] ladderZ = { -22f, -6f, 16f, 26f };
            for (int i = 0; i < ladderZ.Length; i++)
            {
                float lz = ladderZ[i];
                metalComb.AddBox(new Vector3(-27.65f, -0.6f, lz - 0.22f), new Vector3(0.06f, 1.8f, 0.06f), pal.Steel);
                metalComb.AddBox(new Vector3(-27.65f, -0.6f, lz + 0.22f), new Vector3(0.06f, 1.8f, 0.06f), pal.Steel);
                for (float ry = -1.2f; ry <= 0.1f; ry += 0.32f)
                {
                    metalComb.AddBox(new Vector3(-27.65f, ry, lz), new Vector3(0.05f, 0.04f, 0.44f), pal.HazardYellow);
                }
            }

            // 连接外海货船的舷梯斜桥 (Gangway)
            Vector3 gStart = new Vector3(-27.2f, topY + curbH, 6.0f);
            Vector3 gEnd = new Vector3(-42.5f, 1.25f, 6.0f);
            Vector3 gMid = (gStart + gEnd) * 0.5f;
            float gLen = Vector3.Distance(gStart, gEnd);
            float gAngle = Mathf.Atan2(gEnd.y - gStart.y, gStart.x - gEnd.x) * Mathf.Rad2Deg;
            Quaternion gRot = Quaternion.Euler(0f, 0f, -gAngle);

            metalComb.AddBox(gMid, new Vector3(gLen, 0.12f, 1.4f), pal.Steel, gRot);
            metalComb.AddBox(gMid + Vector3.up * 0.55f + Vector3.forward * 0.70f, new Vector3(gLen, 0.08f, 0.08f), pal.HazardYellow, gRot);
            metalComb.AddBox(gMid + Vector3.up * 0.55f + Vector3.back * 0.70f, new Vector3(gLen, 0.08f, 0.08f), pal.HazardYellow, gRot);

            // ---- 4. 沿海电线杆与电缆（原画 Angle A 特征，位于 x=-26.5m，安全包络之外）----
            Vector3[] polePos =
            {
                new Vector3(-26.5f, topY, -14.0f),
                new Vector3(-26.5f, topY, 16.0f)
            };
            for (int p = 0; p < polePos.Length; p++)
            {
                Vector3 baseP = polePos[p];
                float poleH = 10.5f;
                metalComb.AddCylinder(baseP, 0.22f, poleH, 8, pal.ConcreteDark);
                metalComb.AddBox(baseP + new Vector3(0f, poleH - 0.4f, 0f), new Vector3(0.14f, 0.14f, 2.6f), pal.SteelDark);
                for (float zOff = -1.1f; zOff <= 1.1f; zOff += 1.1f)
                {
                    metalComb.AddCylinder(baseP + new Vector3(0f, poleH - 0.3f, zOff), 0.08f, 0.25f, 6, pal.ConcreteLight);
                }
                if (p == 0)
                {
                    metalComb.AddCylinder(baseP + new Vector3(0.35f, 6.5f, 0f), 0.38f, 1.1f, 8, pal.Steel);
                }
            }

            Vector3 pw0 = polePos[0] + new Vector3(0f, 10.3f, 0f);
            Vector3 pw1 = polePos[1] + new Vector3(0f, 10.3f, 0f);
            int wireSegs = 10;
            for (int s = 0; s < wireSegs; s++)
            {
                float t0 = (float)s / wireSegs;
                float t1 = (float)(s + 1) / wireSegs;
                float sag0 = Mathf.Sin(t0 * Mathf.PI) * 1.35f;
                float sag1 = Mathf.Sin(t1 * Mathf.PI) * 1.35f;
                Vector3 pA = Vector3.Lerp(pw0, pw1, t0) - new Vector3(0f, sag0, 0f);
                Vector3 pB = Vector3.Lerp(pw0, pw1, t1) - new Vector3(0f, sag1, 0f);
                metalComb.AddStayRod(pA, pB, 0.04f, pal.SteelDark);
            }
        }
        #endregion

        #region 模块 2：北侧集装箱堆场（安全包络：全在 z≥34m）
        /// <summary>
        /// 北侧集装箱堆场（z=34..75m）：
        /// 4 排高饱和色彩集装箱（电光青、深海蓝、草绿、铁锈红、芥末黄），
        /// 纵深递减亮度，地面铺设双轨岸桥铁轨、18m 高杆照明塔与场务值班岗亭。
        /// </summary>
        private static void BuildContainerTerminal(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float baseY = -0.05f;

            // 北侧堆场地坪大板 (z: 33.0 ~ 76.0m, x: -28.0 ~ 40.0m)
            matteComb.AddBox(new Vector3(6.0f, baseY - 0.08f, 54.5f), new Vector3(68.0f, 0.16f, 43.0f), pal.ConcreteLight);
            // 堆场地面黄色行车导向线
            matteComb.AddBox(new Vector3(6.0f, baseY + 0.005f, 44.0f), new Vector3(66.0f, 0.01f, 0.35f), pal.HazardYellow);
            matteComb.AddBox(new Vector3(6.0f, baseY + 0.005f, 60.0f), new Vector3(66.0f, 0.01f, 0.35f), pal.HazardYellow);

            // ---- 1. 岸桥行走重型双铁轨 (East-West 贯穿) ----
            float[] railZ = { 46.5f, 61.5f };
            for (int r = 0; r < railZ.Length; r++)
            {
                float rz = railZ[r];
                matteComb.AddBox(new Vector3(6.0f, baseY + 0.03f, rz), new Vector3(66.0f, 0.06f, 2.4f), pal.ConcreteDark);
                metalComb.AddBox(new Vector3(6.0f, baseY + 0.12f, rz - 0.72f), new Vector3(66.0f, 0.12f, 0.10f), pal.Steel);
                metalComb.AddBox(new Vector3(6.0f, baseY + 0.12f, rz + 0.72f), new Vector3(66.0f, 0.12f, 0.10f), pal.Steel);
            }

            // ---- 2. 高杆照明塔 (18m 方钢桁架 + 矩阵泛光灯，z=37m) ----
            Vector3[] floodlightPos =
            {
                new Vector3(-21.0f, baseY, 37.0f),
                new Vector3(25.0f,  baseY, 37.0f)
            };
            for (int fl = 0; fl < floodlightPos.Length; fl++)
            {
                Vector3 bp = floodlightPos[fl];
                float towerH = 18.0f;
                metalComb.AddBox(bp + new Vector3(0f, towerH * 0.5f, 0f), new Vector3(0.85f, towerH, 0.85f), pal.SteelDark);
                Vector3 headP = bp + new Vector3(0f, towerH, 0f);
                metalComb.AddBox(headP, new Vector3(3.2f, 0.35f, 1.2f), pal.SteelDark);
                for (int li = 0; li < 4; li++)
                {
                    float lx = -1.2f + li * 0.8f;
                    metalComb.AddBox(headP + new Vector3(lx, 0.5f, 0f), new Vector3(0.55f, 0.45f, 0.35f), pal.Steel);
                    glassComb.AddBox(headP + new Vector3(lx, 0.5f, -0.19f), new Vector3(0.48f, 0.38f, 0.04f), pal.WindowLit);
                }
            }

            // ---- 3. 堆场调度值班岗亭 (z=35.5m) ----
            Vector3 cabinP = new Vector3(-10.0f, baseY + 1.6f, 35.5f);
            matteComb.AddBox(cabinP, new Vector3(4.8f, 3.2f, 3.4f), pal.ConcreteLight);
            matteComb.AddBox(cabinP + new Vector3(0f, 1.68f, 0f), new Vector3(5.1f, 0.20f, 3.7f), pal.SteelDark);
            glassComb.AddBox(cabinP + new Vector3(0f, 0.2f, -1.71f), new Vector3(2.4f, 1.2f, 0.06f), pal.WindowGlass);
            metalComb.AddBox(cabinP + new Vector3(1.8f, 0.8f, 1.71f), new Vector3(0.9f, 0.7f, 0.55f), pal.Steel);

            // ---- 4. 四排进深集装箱堆叠 ----
            Color[] saturatedCols =
            {
                pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard,
                pal.ContainerBlue, pal.ContainerGreen, pal.ContainerSlate
            };

            void PlaceContainer(Vector3 pos, Vector3 size, Color col, float yaw = 0f, bool postMetal = true)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                matteComb.AddBox(pos, size, col, rot);
                if (postMetal)
                {
                    float hx = size.x * 0.5f;
                    float hy = size.y;
                    float hz = size.z * 0.5f;
                    Vector3 postSz = new Vector3(0.12f, hy, 0.12f);
                    Color postCol = Color.Lerp(col, pal.SteelDark, 0.40f);
                    metalComb.AddBox(pos + rot * new Vector3(-hx, 0f, -hz), postSz, postCol, rot);
                    metalComb.AddBox(pos + rot * new Vector3(hx,  0f, -hz), postSz, postCol, rot);
                    metalComb.AddBox(pos + rot * new Vector3(-hx, 0f, hz),  postSz, postCol, rot);
                    metalComb.AddBox(pos + rot * new Vector3(hx,  0f, hz),  postSz, postCol, rot);
                }
            }

            Vector3 c20 = new Vector3(2.44f, 2.59f, 6.06f);
            Vector3 c40 = new Vector3(2.44f, 2.59f, 12.19f);

            // 第一排 (前排，z=38.5m，饱满鲜亮，2-high)
            float[] r1X = { -18f, -12f, -3.5f, 3.5f, 12f, 19f, 26f };
            for (int i = 0; i < r1X.Length; i++)
            {
                float x = r1X[i];
                int tiers = (i % 3 == 0) ? 1 : 2;
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 cp = new Vector3(x, baseY + t * c20.y + c20.y * 0.5f, 38.5f + (i % 2) * 1.2f);
                    Color col = saturatedCols[(i * 2 + t) % saturatedCols.Length];
                    PlaceContainer(cp, c20, col, (i % 2 == 0 ? 90f : 88f));
                }
            }

            // 第二排 (中前排，z=51.0m，2-3层)
            float[] r2X = { -22f, -15f, -8f, 7f, 15f, 23f };
            for (int i = 0; i < r2X.Length; i++)
            {
                float x = r2X[i];
                int tiers = (i % 2 == 0) ? 3 : 2;
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 cp = new Vector3(x, baseY + t * c20.y + c20.y * 0.5f, 51.0f);
                    Color col = saturatedCols[(i * 3 + t + 2) % saturatedCols.Length];
                    PlaceContainer(cp, c20, col, 90f);
                }
            }

            // 第三排 (中深排，z=63.0m，40ft长箱，压暗派生色)
            float[] r3X = { -18f, -4f, 10f, 24f };
            for (int i = 0; i < r3X.Length; i++)
            {
                float x = r3X[i];
                int tiers = (i % 2 == 1) ? 3 : 2;
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 cp = new Vector3(x, baseY + t * c40.y + c40.y * 0.5f, 63.0f);
                    Color baseCol = saturatedCols[(i + t * 2 + 3) % saturatedCols.Length];
                    Color dimCol = Color.Lerp(baseCol, pal.SteelDark, 0.22f);
                    PlaceContainer(cp, c40, dimCol, 90f);
                }
            }

            // 第四排 (深层背景堆叠墙，z=73.0m，密实压阵)
            for (float x = -24f; x <= 32f; x += 6.5f)
            {
                int tiers = 3;
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 cp = new Vector3(x, baseY + t * c20.y + c20.y * 0.5f, 73.0f);
                    Color baseCol = saturatedCols[((int)Mathf.Abs(x) + t) % saturatedCols.Length];
                    Color dimCol = Color.Lerp(baseCol, pal.SteelDark, 0.40f);
                    PlaceContainer(cp, c20, dimCol, 0f, postMetal: false);
                }
            }

            // 货盘与电缆盘细节 (z≥36.0m)
            Vector3[] palletPos =
            {
                new Vector3(-6.0f, baseY, 36.0f),
                new Vector3(8.0f,  baseY, 36.0f),
                new Vector3(18.0f, baseY, 44.5f)
            };
            for (int p = 0; p < palletPos.Length; p++)
            {
                matteComb.AddBox(palletPos[p] + new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.24f, 1.4f), pal.Wood);
                matteComb.AddBox(palletPos[p] + new Vector3(0.1f, 0.50f, 0.1f), new Vector3(1.1f, 0.55f, 1.0f), pal.WoodDark);
            }
        }
        #endregion

        #region 模块 3：集装箱岸桥（钢青门架 + 纯白机房 + 港青色带，无镜像反字）
        /// <summary>
        /// 4 座高 22-28m 集装箱门式起重机（岸桥）：
        /// 位于 z=47..66m，「钢青自动化码头」配色 —— 深钢青门架/主梁/塔架，白机房 + 双暗横条，
        /// 主梁上缘一道港青品牌色带；黄黑警示只保留在支脚台车（小面积工业点缀），
        /// 部分小车悬空吊运集装箱。
        /// </summary>
        private static void BuildGantryCranes(MeshCombiner metalComb, MeshCombiner matteComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float baseY = -0.05f;

            var craneConfigs = new[]
            {
                new { pos = new Vector3(-18.0f, baseY, 47.0f), yaw = 0f,  totalH = 26f, hoist = true,  contCol = pal.ContainerRust,    contZOff = 7.0f },
                new { pos = new Vector3(-1.0f,  baseY, 59.0f), yaw = 0f,  totalH = 28f, hoist = false, contCol = pal.ContainerTeal,    contZOff = 2.0f },
                new { pos = new Vector3(19.0f,  baseY, 50.0f), yaw = 0f,  totalH = 25f, hoist = true,  contCol = pal.ContainerMustard, contZOff = 9.0f },
                new { pos = new Vector3(-32.0f, baseY, 66.0f), yaw = 10f, totalH = 27f, hoist = false, contCol = pal.ContainerBlue,    contZOff = 4.0f }
            };

            for (int k = 0; k < craneConfigs.Length; k++)
            {
                var cfg = craneConfigs[k];
                Vector3 origin = cfg.pos;
                Quaternion rot = Quaternion.Euler(0f, cfg.yaw, 0f);

                // 相邻岸桥在主色与亮面板色之间交替，避免一排同色读成复制粘贴；
                // 最远一座（k=3）再向雾色压暗半档，强化纵深。
                Color structure = (k % 2 == 0) ? pal.CraneSteelMain : Color.Lerp(pal.CraneSteelMain, pal.Steel, 0.30f);
                Color panel = (k % 2 == 0) ? pal.CraneSteelPanel : Color.Lerp(pal.CraneSteelPanel, pal.Steel, 0.30f);
                if (k == 3) structure = Color.Lerp(structure, pal.HorizonCity, 0.18f);

                float spanX = 8.6f;
                float spanZ = 9.2f;
                float portalH = 14.0f;
                float apexH = cfg.totalH;

                // 1. 支脚台车 (4 组重载轮箱，黄黑斜向警示条纹)
                Vector3[] footOffsets =
                {
                    new Vector3(-spanX * 0.5f, 0.55f, -spanZ * 0.5f),
                    new Vector3(spanX * 0.5f,  0.55f, -spanZ * 0.5f),
                    new Vector3(-spanX * 0.5f, 0.55f, spanZ * 0.5f),
                    new Vector3(spanX * 0.5f,  0.55f, spanZ * 0.5f),
                };
                Vector3 bogieSz = new Vector3(1.15f, 1.10f, 3.6f);
                for (int f = 0; f < footOffsets.Length; f++)
                {
                    metalComb.AddHazardBogie(origin + rot * footOffsets[f], bogieSz, pal.HazardYellow, pal.HazardDark, rot);
                }

                // 2. 门架倾斜立柱（0.78m 截面，比旧 0.95m 纤细，避免远景读成粗黄砖柱）
                float legLen = portalH - 1.1f;
                Vector3 legSz = new Vector3(0.78f, legLen, 0.78f);
                for (int f = 0; f < footOffsets.Length; f++)
                {
                    Vector3 legCenter = footOffsets[f] + new Vector3(0f, legLen * 0.5f + 0.55f, 0f);
                    metalComb.AddBox(origin + rot * legCenter, legSz, structure, rot);
                }

                // 3. 门梁与 X 型剪刀撑
                metalComb.AddBox(origin + rot * new Vector3(0f, portalH, -spanZ * 0.5f), new Vector3(spanX + 1.2f, 1.1f, 1.0f), structure, rot);
                metalComb.AddBox(origin + rot * new Vector3(0f, portalH, spanZ * 0.5f),  new Vector3(spanX + 1.2f, 1.1f, 1.0f), structure, rot);
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH, 0f), new Vector3(1.0f, 1.1f, spanZ), structure, rot);
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f,  portalH, 0f), new Vector3(1.0f, 1.1f, spanZ), structure, rot);

                Vector3 braceSz = new Vector3(0.34f, 0.34f, spanZ * 1.28f);
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, structure, rot * Quaternion.Euler(38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, structure, rot * Quaternion.Euler(-38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f,  portalH * 0.5f + 0.5f, 0f), braceSz, structure, rot * Quaternion.Euler(38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f,  portalH * 0.5f + 0.5f, 0f), braceSz, structure, rot * Quaternion.Euler(-38f, 0f, 0f));

                // 4. 门架上部 A 字塔架 (延伸至 apexH 25-28m)
                float aFrameH = apexH - portalH;
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.30f, portalH + aFrameH * 0.5f, 0f), new Vector3(0.70f, aFrameH, 0.70f), structure, rot * Quaternion.Euler(0f, 0f, 6.5f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.30f,  portalH + aFrameH * 0.5f, 0f), new Vector3(0.70f, aFrameH, 0.70f), structure, rot * Quaternion.Euler(0f, 0f, -6.5f));
                metalComb.AddBox(origin + rot * new Vector3(0f, apexH, 0f), new Vector3(spanX * 0.70f, 0.9f, 1.3f), structure, rot);

                // 5. 主悬臂箱梁 (伸展 34m，2.2x1.7 截面比旧 2.5x2.1 轻盈)
                float boomLength = 34.0f;
                float boomZOffset = 2.0f;
                float boomY = portalH + 2.0f;
                Vector3 boomCenter = origin + rot * new Vector3(0f, boomY, boomZOffset);
                metalComb.AddBox(boomCenter, new Vector3(2.2f, 1.7f, boomLength), panel, rot);

                // 港青品牌色带：沿主梁两侧上缘各一道，呼应场内港青集装箱与「霓虹海滨」主题
                Vector3 boomStripeSz = new Vector3(0.08f, 0.34f, boomLength - 3.0f);
                metalComb.AddBox(origin + rot * new Vector3(1.14f, boomY + 0.45f, boomZOffset), boomStripeSz, pal.CraneAccent, rot);
                metalComb.AddBox(origin + rot * new Vector3(-1.14f, boomY + 0.45f, boomZOffset), boomStripeSz, pal.CraneAccent, rot);

                // 走台护栏
                metalComb.AddBox(origin + rot * new Vector3(-1.05f, boomY + 1.30f, boomZOffset), new Vector3(0.12f, 0.75f, boomLength), pal.SteelDark, rot);
                metalComb.AddBox(origin + rot * new Vector3(1.05f,  boomY + 1.30f, boomZOffset), new Vector3(0.12f, 0.75f, boomLength), pal.SteelDark, rot);

                // 塔顶前后斜拉索
                Vector3 towerApex = origin + rot * new Vector3(0f, apexH, 0f);
                Vector3 stayTipFront = origin + rot * new Vector3(0f, boomY + 1.0f, boomZOffset + 13.5f);
                Vector3 stayTipBack  = origin + rot * new Vector3(0f, boomY + 1.0f, boomZOffset - 13.5f);
                metalComb.AddStayRod(towerApex, stayTipFront, 0.14f, pal.SteelDark);
                metalComb.AddStayRod(towerApex, stayTipBack,  0.14f, pal.SteelDark);

                // 6. 梁顶卷扬机械机房 (纯白面板 + 2道深色横条纹，避免字母反字)
                Vector3 cabPos = origin + rot * new Vector3(0f, boomY + 2.4f, -4.5f);
                matteComb.AddBox(cabPos, new Vector3(3.8f, 2.7f, 6.2f), pal.CabinWhite, rot);
                matteComb.AddBox(cabPos + new Vector3(0f, 1.45f, 0f), new Vector3(4.0f, 0.25f, 6.4f), pal.SteelDark, rot); // 屋顶深色压边

                // 左右两侧纯白面板上的 2 道深色横向条纹 (Dark Stencil Stripes)
                Vector3 stripeSz = new Vector3(0.08f, 0.26f, 5.6f);
                metalComb.AddBox(cabPos + rot * new Vector3(1.94f, 0.45f, 0f),  stripeSz, pal.HazardDark, rot);
                metalComb.AddBox(cabPos + rot * new Vector3(-1.94f, 0.45f, 0f), stripeSz, pal.HazardDark, rot);
                metalComb.AddBox(cabPos + rot * new Vector3(1.94f, -0.45f, 0f), stripeSz, pal.HazardDark, rot);
                metalComb.AddBox(cabPos + rot * new Vector3(-1.94f, -0.45f, 0f), stripeSz, pal.HazardDark, rot);

                // 正向面向南侧场地/相机的操作室玻璃窗
                glassComb.AddBox(cabPos + rot * new Vector3(0f, -0.2f, 3.12f), new Vector3(2.8f, 1.4f, 0.06f), pal.WindowGlass, rot);

                // 7. 下挂小车 (Trolley) 与吊具
                float trolleyZ = boomZOffset + cfg.contZOff;
                Vector3 trolleyP = origin + rot * new Vector3(0f, boomY - 1.30f, trolleyZ);
                metalComb.AddBox(trolleyP, new Vector3(2.4f, 0.65f, 3.2f), pal.SteelDark, rot);

                float cableLen = cfg.hoist ? 6.5f : 3.2f;
                float spreaderY = trolleyP.y - cableLen;
                Vector3 spreaderP = origin + rot * new Vector3(0f, spreaderY, trolleyZ);

                // 4 根垂直悬吊钢缆
                float cx = 0.95f;
                float cz = 1.25f;
                metalComb.AddBox(origin + rot * new Vector3(-cx, trolleyP.y - cableLen * 0.5f, trolleyZ - cz), new Vector3(0.06f, cableLen, 0.06f), pal.SteelDark, rot);
                metalComb.AddBox(origin + rot * new Vector3(cx,  trolleyP.y - cableLen * 0.5f, trolleyZ - cz), new Vector3(0.06f, cableLen, 0.06f), pal.SteelDark, rot);
                metalComb.AddBox(origin + rot * new Vector3(-cx, trolleyP.y - cableLen * 0.5f, trolleyZ + cz), new Vector3(0.06f, cableLen, 0.06f), pal.SteelDark, rot);
                metalComb.AddBox(origin + rot * new Vector3(cx,  trolleyP.y - cableLen * 0.5f, trolleyZ + cz), new Vector3(0.06f, cableLen, 0.06f), pal.SteelDark, rot);

                metalComb.AddBox(spreaderP, new Vector3(2.55f, 0.45f, 6.25f), pal.SteelDark, rot);

                // 悬空起吊集装箱
                if (cfg.hoist)
                {
                    Vector3 contP = spreaderP - new Vector3(0f, 1.48f, 0f);
                    Vector3 contSz = new Vector3(2.44f, 2.59f, 6.06f);
                    matteComb.AddBox(contP, contSz, cfg.contCol, rot);
                    Color postC = Color.Lerp(cfg.contCol, pal.SteelDark, 0.45f);
                    metalComb.AddBox(contP + rot * new Vector3(-1.22f, 0f, -3.03f), new Vector3(0.12f, 2.6f, 0.12f), postC, rot);
                    metalComb.AddBox(contP + rot * new Vector3(1.22f,  0f, -3.03f), new Vector3(0.12f, 2.6f, 0.12f), postC, rot);
                    metalComb.AddBox(contP + rot * new Vector3(-1.22f, 0f, 3.03f),  new Vector3(0.12f, 2.6f, 0.12f), postC, rot);
                    metalComb.AddBox(contP + rot * new Vector3(1.22f,  0f, 3.03f),  new Vector3(0.12f, 2.6f, 0.12f), postC, rot);
                }
            }
        }
        #endregion

        #region 模块 4：西侧外海船舶船队（安全包络：全在 x≤-28m）
        /// <summary>
        /// 西侧海面船队（严格位于 x≤-28m）：
        /// 46m 大型集装箱货船、13m 港务拖船、近海驳船、航道浮标。
        /// </summary>
        private static void BuildShipsAndFleet(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float waterY = -1.35f;

            // ---- 1. 大型集装箱远洋货轮 (Container Ship, 46m) ----
            {
                Vector3 shipOrigin = new Vector3(-48.0f, waterY, 8.0f);
                float shipL = 46.0f;
                float shipW = 11.5f;
                float hullH = 4.8f;
                Quaternion sRot = Quaternion.Euler(0f, 1.5f, 0f);

                // 吃水红线
                matteComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH * 0.22f, 0f),
                    new Vector3(shipW * 0.94f, hullH * 0.44f, shipL * 0.72f), pal.WaterlineRed, sRot);
                matteComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH * 0.22f, shipL * 0.42f),
                    new Vector3(shipW * 0.65f, hullH * 0.44f, shipL * 0.26f), pal.WaterlineRed, sRot);

                // 上部主船体 (深海军蓝 Navy Blue)
                matteComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH * 0.72f, 0f),
                    new Vector3(shipW, hullH * 0.56f, shipL * 0.72f), pal.HullNavy, sRot);
                matteComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH * 0.72f, shipL * 0.42f),
                    new Vector3(shipW * 0.70f, hullH * 0.56f, shipL * 0.26f), pal.HullNavy, sRot);
                metalComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH * 0.5f, shipL * 0.53f),
                    new Vector3(shipW * 0.35f, hullH * 0.6f, shipL * 0.08f), pal.HullNavy, sRot);

                metalComb.AddBox(shipOrigin + sRot * new Vector3(0f, hullH + 0.35f, shipL * 0.45f),
                    new Vector3(shipW * 0.60f, 0.70f, 0.15f), pal.SteelDark, sRot);

                // 船尾 4 层驾驶楼上层建筑
                float bridgeZ = -shipL * 0.28f;
                Vector3 bridgeP = shipOrigin + sRot * new Vector3(0f, hullH + 4.2f, bridgeZ);
                matteComb.AddBox(bridgeP, new Vector3(shipW * 0.84f, 8.4f, 7.8f), pal.CabinWhite, sRot);
                metalComb.AddBox(bridgeP + new Vector3(0f, 2.8f, 0f), new Vector3(shipW * 1.05f, 0.75f, 3.2f), pal.CabinWhite, sRot);
                glassComb.AddBox(bridgeP + sRot * new Vector3(0f, 3.1f, 3.92f), new Vector3(shipW * 0.95f, 1.1f, 0.06f), pal.WindowGlass, sRot);

                // 烟囱
                Vector3 funnelP = shipOrigin + sRot * new Vector3(0f, hullH + 9.8f, bridgeZ - 3.2f);
                matteComb.AddBox(funnelP, new Vector3(2.4f, 4.0f, 2.8f), pal.SteelDark, sRot);
                matteComb.AddBox(funnelP + new Vector3(0f, 0.6f, 0f), new Vector3(2.45f, 1.2f, 2.85f), pal.ContainerMustard, sRot);

                // 雷达桅杆
                metalComb.AddBox(bridgeP + new Vector3(0f, 6.0f, 0f), new Vector3(0.35f, 4.2f, 0.35f), pal.Steel, sRot);
                metalComb.AddBox(bridgeP + new Vector3(0f, 7.5f, 0f), new Vector3(2.8f, 0.14f, 0.14f), pal.Steel, sRot);

                // 甲板集装箱垛 (3贝位，双层)
                Color[] shipContCols = { pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerGreen, pal.ContainerBlue };
                Vector3 sContSz = new Vector3(2.44f, 2.59f, 6.06f);
                for (int row = -1; row <= 1; row++)
                {
                    for (int bay = 0; bay < 3; bay++)
                    {
                        for (int tier = 0; tier < 2; tier++)
                        {
                            Vector3 boxP = shipOrigin + sRot * new Vector3(row * 2.8f, hullH + tier * 2.6f + 1.3f, -4.5f + bay * 6.8f);
                            Color cCol = shipContCols[(row + bay + tier + 5) % shipContCols.Length];
                            matteComb.AddBox(boxP, sContSz, cCol, sRot);
                        }
                    }
                }

                // 甲板旋转起重机
                Vector3 dCraneP = shipOrigin + sRot * new Vector3(0f, hullH, 16.5f);
                metalComb.AddCylinder(dCraneP, 1.1f, 2.2f, 8, pal.SteelDark, sRot);
                metalComb.AddBox(dCraneP + sRot * new Vector3(0f, 2.8f, 0f), new Vector3(2.0f, 1.6f, 2.2f), pal.HazardYellow, sRot);
                float boomL = 13.0f;
                Quaternion dBoomRot = sRot * Quaternion.Euler(-28f, 35f, 0f);
                Vector3 dBoomCenter = dCraneP + sRot * new Vector3(0f, 3.4f, 0f) + dBoomRot * new Vector3(0f, boomL * 0.5f, 0f);
                metalComb.AddBox(dBoomCenter, new Vector3(0.55f, boomL, 0.55f), pal.HazardYellow, dBoomRot);
            }

            // ---- 2. 港务工作拖船 (Tugboat, 13m) ----
            {
                Vector3 tugP = new Vector3(-38.0f, waterY, -18.0f);
                Quaternion tugRot = Quaternion.Euler(0f, -26.0f, 0f);
                float tugL = 13.0f;
                float tugW = 4.8f;
                float tugH = 2.4f;

                matteComb.AddBox(tugP + tugRot * new Vector3(0f, tugH * 0.5f, 0f), new Vector3(tugW, tugH, tugL * 0.8f), pal.SteelDark, tugRot);
                matteComb.AddBox(tugP + tugRot * new Vector3(0f, tugH * 0.5f, tugL * 0.42f), new Vector3(tugW * 0.65f, tugH, tugL * 0.24f), pal.SteelDark, tugRot);
                matteComb.AddBox(tugP + tugRot * new Vector3(0f, tugH * 0.7f, tugL * 0.52f), new Vector3(2.2f, 1.6f, 0.8f), pal.HazardDark, tugRot);

                Vector3 houseP = tugP + tugRot * new Vector3(0f, tugH + 1.2f, 0.5f);
                matteComb.AddBox(houseP, new Vector3(3.2f, 2.4f, 4.2f), pal.CabinWhite, tugRot);
                glassComb.AddBox(houseP + tugRot * new Vector3(0f, 0.4f, 2.12f), new Vector3(2.6f, 0.8f, 0.05f), pal.WindowGlass, tugRot);

                metalComb.AddCylinder(houseP + tugRot * new Vector3(0f, 1.8f, -1.2f), 0.45f, 1.8f, 8, pal.SteelDark, tugRot);
                metalComb.AddCylinder(houseP + tugRot * new Vector3(0f, 2.5f, -1.2f), 0.48f, 0.45f, 8, pal.HazardYellow, tugRot);
                metalComb.AddBox(tugP + tugRot * new Vector3(0f, tugH + 0.5f, -tugL * 0.32f), new Vector3(1.6f, 0.9f, 1.2f), pal.Steel, tugRot);
            }

            // ---- 3. 近海驳船/渔船 (Coastal Barge, 15m) ----
            {
                Vector3 bargeP = new Vector3(-42.0f, waterY, -34.0f);
                Quaternion bargeRot = Quaternion.Euler(0f, 38.0f, 0f);
                float bL = 15.0f;
                float bW = 5.2f;

                matteComb.AddBox(bargeP + bargeRot * new Vector3(0f, 0.9f, 0f), new Vector3(bW, 1.8f, bL), pal.ConcreteDark, bargeRot);
                matteComb.AddBox(bargeP + bargeRot * new Vector3(0f, 2.2f, 1.0f), new Vector3(bW * 0.85f, 1.4f, 6.0f), pal.ContainerTeal, bargeRot);
                matteComb.AddBox(bargeP + bargeRot * new Vector3(0f, 2.5f, -4.5f), new Vector3(3.0f, 2.2f, 3.2f), pal.ConcreteLight, bargeRot);
                metalComb.AddStayRod(bargeP + bargeRot * new Vector3(-1.8f, 1.8f, 6.0f), bargeP + bargeRot * new Vector3(0f, 6.2f, 4.0f), 0.12f, pal.Steel);
                metalComb.AddStayRod(bargeP + bargeRot * new Vector3(1.8f,  1.8f, 6.0f), bargeP + bargeRot * new Vector3(0f, 6.2f, 4.0f), 0.12f, pal.Steel);
            }

            // ---- 4. 航标浮标 ----
            Vector3 buoyGreenP = new Vector3(-31.0f, waterY + 0.3f, 22.0f);
            matteComb.AddCylinder(buoyGreenP, 0.75f, 0.8f, 8, pal.ContainerGreen);
            metalComb.AddCone(buoyGreenP + Vector3.up * 0.8f, 0.75f, 0.05f, 1.2f, 8, pal.ContainerGreen);

            Vector3 buoyRedP = new Vector3(-33.0f, waterY + 0.3f, -6.0f);
            matteComb.AddCylinder(buoyRedP, 0.75f, 1.6f, 8, pal.WaterlineRed);
        }
        #endregion

        #region 模块 5：东侧海滨城市街区（东移10-14m，最近立面x≥44m，最高楼~22m，剔除粉色）
        /// <summary>
        /// 东侧海滨城市街区（建筑群整体东移 10-14m，最近立面严格 x≥44.0m）：
        /// 留出 >14m 宽阔海滨街道/码头带，最高楼控制在 21.5m（~22m），
        /// 建筑配色改用深砖红、深棕、暖灰与浅灰/米白，窗格保留，草绿色工业管道退至 x=42.0m。
        /// </summary>
        private static void BuildEastCityBlock(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float baseY = -0.05f;

            // 东侧宽阔街道与港区地面 (x: 24.5 ~ 68.0m, z: -32.0 ~ 34.0m)
            matteComb.AddBox(new Vector3(46.25f, baseY - 0.08f, 1.0f), new Vector3(43.5f, 0.16f, 66.0f), pal.ConcreteDark);
            // 人行道路缘分界石 (x=43.2m，沿建筑群前沿展开)
            matteComb.AddBox(new Vector3(43.2f, baseY + 0.06f, 1.0f), new Vector3(1.2f, 0.12f, 64.0f), pal.ConcreteMid);

            // ---- 1. 6 栋多层建筑（最近立面严格控制在 x ≥ 44.0m）----
            // 楼 1：转角港务大楼 (深砖红 4 层仓库写字楼，高 15m)
            // 尺寸: 12m(X) x 15m(Y) x 16m(Z)，中心: (50.0m, baseY + 7.5m, 18.0m)，西立面恰在 x = 44.0m!
            {
                Vector3 bPos = new Vector3(50.0f, baseY + 7.5f, 18.0f);
                Vector3 bSz = new Vector3(12.0f, 15.0f, 16.0f);
                matteComb.AddBox(bPos, bSz, pal.BrickRed);
                matteComb.AddBox(bPos + new Vector3(0f, 7.65f, 0f), new Vector3(12.4f, 0.35f, 16.4f), pal.ConcreteLight);

                // 底层货运卷帘门
                metalComb.AddBox(bPos + new Vector3(-6.02f, -5.0f, 0f), new Vector3(0.08f, 4.2f, 6.5f), pal.SteelDark);

                // 4层 x 3列 窗格矩阵
                for (int floor = 0; floor < 4; floor++)
                {
                    float wy = bPos.y - 3.6f + floor * 3.2f;
                    for (int col = -1; col <= 1; col++)
                    {
                        float wz = bPos.z + col * 4.2f;
                        Vector3 winCenter = new Vector3(bPos.x - 6.02f, wy, wz);
                        metalComb.AddBox(winCenter, new Vector3(0.06f, 1.6f, 2.2f), pal.SteelDark);
                        Color wCol = ((floor + col) % 2 == 0) ? pal.WindowLit : pal.WindowGlass;
                        glassComb.AddBox(winCenter, new Vector3(0.08f, 1.4f, 2.0f), wCol);
                    }
                }

                // 屋顶木质高架水塔
                Vector3 tankBase = bPos + new Vector3(0f, 7.7f, 2.0f);
                float stiltH = 3.2f;
                for (int s = 0; s < 4; s++)
                {
                    float sx = (s % 2 == 0 ? -1.0f : 1.0f);
                    float sz = (s < 2 ? -1.0f : 1.0f);
                    metalComb.AddBox(tankBase + new Vector3(sx, stiltH * 0.5f, sz), new Vector3(0.16f, stiltH, 0.16f), pal.SteelDark);
                }
                Vector3 drumCenter = tankBase + new Vector3(0f, stiltH, 0f);
                matteComb.AddCylinder(drumCenter, 1.5f, 2.4f, 10, pal.Wood);
                metalComb.AddCone(drumCenter + Vector3.up * 2.4f, 1.6f, 0.1f, 1.0f, 10, pal.SteelDark);
            }

            // 楼 2：现代主塔楼 (全景最高地标，高 21.5m ~22m，浅米白建筑混凝土立面)
            // 尺寸: 13m(X) x 21.5m(Y) x 16m(Z)，中心: (53.5m, baseY + 10.75m, 2.0m)，西立面恰在 x = 47.0m!
            {
                Vector3 bPos = new Vector3(53.5f, baseY + 10.75f, 2.0f);
                Vector3 bSz = new Vector3(13.0f, 21.5f, 16.0f);
                matteComb.AddBox(bPos, bSz, pal.CreamWhite);

                // 立面竖向遮阳肋柱
                for (float zOff = -6.0f; zOff <= 6.0f; zOff += 3.0f)
                {
                    matteComb.AddBox(bPos + new Vector3(-6.6f, 0f, zOff), new Vector3(0.22f, 21.0f, 0.35f), pal.WarmGray);
                }
                // 横向带状深色窗
                for (int f = 0; f < 5; f++)
                {
                    float wy = bPos.y - 7.5f + f * 3.8f;
                    glassComb.AddBox(new Vector3(bPos.x - 6.53f, wy, bPos.z), new Vector3(0.06f, 1.5f, 14.0f), pal.WindowGlass);
                }

                // 屋顶电梯机房与冷却塔
                matteComb.AddBox(bPos + new Vector3(0f, 11.8f, -2.0f), new Vector3(5.0f, 2.2f, 5.5f), pal.OfficeLightGray);
                metalComb.AddBox(bPos + new Vector3(2.2f, 11.8f, 3.0f), new Vector3(2.8f, 2.0f, 2.8f), pal.Steel);
                metalComb.AddBox(bPos + new Vector3(0f, 14.5f, -2.0f), new Vector3(0.16f, 4.5f, 0.16f), pal.Steel); // 通信天线杆
            }

            // 楼 3：深棕砖商务楼 (中东沿街，高 12.5m)
            // 尺寸: 11m(X) x 12.5m(Y) x 14m(Z)，中心: (49.5m, baseY + 6.25m, -14.0f)，西立面恰在 x = 44.0m!
            {
                Vector3 bPos = new Vector3(49.5f, baseY + 6.25f, -14.0f);
                Vector3 bSz = new Vector3(11.0f, 12.5f, 14.0f);
                matteComb.AddBox(bPos, bSz, pal.BrickDarkBrown);
                matteComb.AddBox(bPos + new Vector3(0f, 6.35f, 0f), new Vector3(11.4f, 0.30f, 14.4f), pal.WarmGray);

                // 底层雨棚
                metalComb.AddBox(bPos + new Vector3(-5.7f, -3.8f, 0f), new Vector3(1.6f, 0.10f, 11.5f), pal.SteelDark);

                // 窗格网格
                for (int f = 0; f < 3; f++)
                {
                    float wy = bPos.y - 2.0f + f * 3.2f;
                    for (int c = -1; c <= 1; c++)
                    {
                        Vector3 wp = new Vector3(bPos.x - 5.52f, wy, bPos.z + c * 3.6f);
                        glassComb.AddBox(wp, new Vector3(0.06f, 1.5f, 2.0f), (f == 1 ? pal.WindowLit : pal.WindowGlass));
                    }
                }
            }

            // 楼 4：暖灰重工业仓储楼 (高 14m，带外挂金属消防楼梯)
            // 尺寸: 12m(X) x 14m(Y) x 14m(Z)，中心: (50.0m, baseY + 7.0m, -28.0m)，西立面恰在 x = 44.0m!
            {
                Vector3 bPos = new Vector3(50.0f, baseY + 7.0f, -28.0f);
                Vector3 bSz = new Vector3(12.0f, 14.0f, 14.0f);
                matteComb.AddBox(bPos, bSz, pal.WarmGray);

                // 北墙外挂金属消防楼梯
                float stairX = bPos.x - 3.5f;
                for (int s = 0; s < 3; s++)
                {
                    float sy = bPos.y - 3.8f + s * 3.8f;
                    metalComb.AddBox(new Vector3(stairX, sy, bPos.z + 7.3f), new Vector3(2.2f, 0.10f, 1.1f), pal.SteelDark);
                    metalComb.AddBox(new Vector3(stairX, sy + 0.45f, bPos.z + 7.9f), new Vector3(2.2f, 0.80f, 0.06f), pal.SteelDark);
                    Vector3 pA = new Vector3(stairX, sy, bPos.z + 7.3f);
                    Vector3 pB = new Vector3(stairX + (s % 2 == 0 ? 2.4f : -2.4f), sy + 3.4f, bPos.z + 7.3f);
                    metalComb.AddStayRod(pA, pB, 0.10f, pal.SteelDark);
                }
            }

            // 楼 5 与楼 6：深远背景大楼 (x=64..66m，高 16-18m)
            {
                Vector3 b5P = new Vector3(65.0f, baseY + 9.0f, 20.0f);
                matteComb.AddBox(b5P, new Vector3(15.0f, 18.0f, 16.0f), pal.OfficeLightGray);
                glassComb.AddBox(b5P + new Vector3(-7.55f, 1.5f, 0f), new Vector3(0.06f, 10.0f, 13.0f), pal.WindowGlass);

                Vector3 b6P = new Vector3(66.0f, baseY + 8.0f, -10.0f);
                matteComb.AddBox(b6P, new Vector3(14.0f, 16.0f, 16.0f), pal.BrickDarkBrown);
                glassComb.AddBox(b6P + new Vector3(-7.05f, 1.0f, 0f), new Vector3(0.06f, 9.0f, 13.0f), pal.WindowGlass);
            }

            // ---- 2. 标志性工业输送管道系统（排布于 x=42.0m，彻底让开宽敞街区）----
            float pipeX = 42.0f;
            Color pipeCol = pal.IndustrialPipeGreen;

            // 水平主管段 (沿 z 方向从 -26m 延伸至 14m，标高 y=3.8m)
            metalComb.AddCylinder(new Vector3(pipeX, 3.8f, -26.0f), 0.40f, 40.0f, 8, pipeCol, Quaternion.Euler(90f, 0f, 0f));
            for (float pz = -24f; pz <= 12f; pz += 7.0f)
            {
                metalComb.AddBox(new Vector3(pipeX, 1.9f, pz), new Vector3(0.18f, 3.8f, 0.18f), pal.SteelDark);
                metalComb.AddBox(new Vector3(pipeX, 3.6f, pz), new Vector3(1.1f, 0.18f, 0.18f), pal.SteelDark);
            }

            // 管道弯折注入地面泵房
            metalComb.AddCylinder(new Vector3(pipeX, 0f, 14.0f), 0.40f, 3.8f, 8, pipeCol);
            metalComb.AddBox(new Vector3(pipeX, 0.9f, 14.0f), new Vector3(1.8f, 1.8f, 1.8f), pal.ConcreteDark);

            // 连接到建筑立面的管廊通梁
            Vector3 bridgeStart = new Vector3(pipeX, 4.2f, -6.0f);
            Vector3 bridgeEnd = new Vector3(48.0f, 4.2f, -6.0f);
            Vector3 bMid = (bridgeStart + bridgeEnd) * 0.5f;
            float bSpan = Vector3.Distance(bridgeStart, bridgeEnd);
            metalComb.AddBox(bMid, new Vector3(bSpan, 1.2f, 1.2f), pal.SteelDark);
            metalComb.AddCylinder(new Vector3(pipeX, 4.2f, -5.8f), 0.22f, bSpan, 8, pal.Steel, Quaternion.Euler(0f, 90f, 0f));

            // 东侧沿街高压电线杆 (x=42.0m, z=-22.0m)
            Vector3 ePole = new Vector3(pipeX, baseY, -22.0f);
            metalComb.AddCylinder(ePole, 0.22f, 10.5f, 8, pal.ConcreteDark);
            metalComb.AddBox(ePole + new Vector3(0f, 10.1f, 0f), new Vector3(0.14f, 0.14f, 2.6f), pal.SteelDark);
        }
        #endregion

        #region 模块 6：南侧工业仓储区（全退至 z≤-48m 且 x≥30m，清空前景）
        /// <summary>
        /// 南侧/东南工业仓储区：
        /// 严格遵循最近边界 z≤-48m 且 x≥30m，彻底清空俯视相机前景！
        /// 包含 2 座锯齿形天窗厂房、3 座立式钢制储罐与互联管道、40ft 集装箱拖车。
        /// </summary>
        private static void BuildSouthIndustrial(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float baseY = -0.05f;

            // 工业区地表大板 (x: 28.5 ~ 66.0m, z: -75.0 ~ -48.0m)
            matteComb.AddBox(new Vector3(47.25f, baseY - 0.08f, -61.5f), new Vector3(37.5f, 0.16f, 27.0f), pal.ConcreteMid);

            // ---- 1. 两座锯齿形采光天窗厂房 (Sawtooth Roof Warehouses) ----
            // 厂房 A (x: 31 ~ 45m, z: -61 ~ -73m)
            BuildSawtoothWarehouse(matteComb, metalComb, glassComb, pal,
                origin: new Vector3(38.0f, baseY, -67.0f),
                widthX: 14.0f, lengthZ: 12.0f, wallH: 6.0f, bays: 3);

            // 厂房 B (x: 47 ~ 61m, z: -61 ~ -73m)
            BuildSawtoothWarehouse(matteComb, metalComb, glassComb, pal,
                origin: new Vector3(54.0f, baseY, -67.0f),
                widthX: 14.0f, lengthZ: 12.0f, wallH: 5.6f, bays: 3);

            // ---- 2. 三座大型立式储罐 (Chemical / Fuel Silos, 全部满足 x≥31.4, z≤-49.4) ----
            var tankConfigs = new[]
            {
                new { pos = new Vector3(34.0f, baseY, -52.0f), r = 2.6f, h = 9.5f },
                new { pos = new Vector3(40.5f, baseY, -57.0f), r = 3.0f, h = 11.0f },
                new { pos = new Vector3(47.0f, baseY, -53.0f), r = 2.4f, h = 8.5f  }
            };

            for (int t = 0; t < tankConfigs.Length; t++)
            {
                var tc = tankConfigs[t];
                matteComb.AddCylinder(tc.pos, tc.r * 1.08f, 0.45f, 10, pal.ConcreteDark);
                metalComb.AddCylinder(tc.pos + new Vector3(0f, 0.45f, 0f), tc.r, tc.h - 0.45f, 12, pal.Steel);
                Vector3 topP = tc.pos + new Vector3(0f, tc.h, 0f);
                metalComb.AddCone(topP, tc.r, 0.05f, 0.70f, 12, pal.SteelDark);
                metalComb.AddCylinder(topP, tc.r * 0.98f, 0.80f, 10, pal.HazardYellow); // 顶缘黄色警示带

                // 罐体外侧爬梯
                for (int lp = 0; lp < 3; lp++)
                {
                    float ay = 1.0f + lp * (tc.h * 0.28f);
                    float ang = lp * 1.1f;
                    Vector3 ladderPos = tc.pos + new Vector3(Mathf.Cos(ang) * (tc.r + 0.18f), ay, Mathf.Sin(ang) * (tc.r + 0.18f));
                    metalComb.AddBox(ladderPos, new Vector3(0.12f, 2.5f, 0.45f), pal.SteelDark);
                }
            }

            // 储罐互联高空管廊 (高 7.5m)
            metalComb.AddStayRod(tankConfigs[0].pos + new Vector3(0f, 7.5f, 0f), tankConfigs[1].pos + new Vector3(0f, 7.5f, 0f), 0.26f, pal.Steel);
            metalComb.AddStayRod(tankConfigs[1].pos + new Vector3(0f, 7.5f, 0f), tankConfigs[2].pos + new Vector3(0f, 7.5f, 0f), 0.26f, pal.Steel);

            // ---- 3. 停放的集装箱拖头卡车 (位于 x=35.0m, z=-60.0m) ----
            Vector3 truckP = new Vector3(35.0f, baseY, -60.0f);
            Quaternion truckRot = Quaternion.Euler(0f, 15.0f, 0f);

            matteComb.AddBox(truckP + truckRot * new Vector3(0f, 1.8f, 7.5f), new Vector3(2.5f, 3.2f, 3.6f), pal.ContainerRust, truckRot);
            glassComb.AddBox(truckP + truckRot * new Vector3(0f, 2.4f, 9.32f), new Vector3(2.2f, 1.0f, 0.06f), pal.WindowGlass, truckRot);
            metalComb.AddBox(truckP + truckRot * new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 0.6f, 16.0f), pal.HazardDark, truckRot);
            matteComb.AddBox(truckP + truckRot * new Vector3(0f, 2.3f, -1.0f), new Vector3(2.44f, 2.59f, 12.19f), pal.ContainerBlue, truckRot);
        }

        /// <summary>
        /// 南出生台背后的港湾库场（z -34 ~ -50）：混凝土岸台 + 两座锯齿厂房 + 堆场集装箱 + 高杆灯 + 拖船。
        /// 旧版本南侧只有空水面：出生台后的 3 个集装箱悬在水上，反向出生视角没有任何地标。
        /// 所有几何严格落在 z≤-34（距边界墙 5.5m），不进入 x∈[-24,24], z∈[-32,32] 安全包络。
        /// </summary>
        private static void BuildSouthSpawnHarbor(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal)
        {
            const float baseY = -0.05f;

            // 1. 岸台大板（x -34..34, z -50..-34）与临水压顶
            //    顶面比东侧工业区大板（top=-0.05）高 2cm，避免两块共面大板在远视角 z-fighting。
            matteComb.AddBox(new Vector3(0f, baseY - 0.07f, -42f), new Vector3(68f, 0.18f, 16f), pal.ConcreteMid);
            matteComb.AddBox(new Vector3(0f, baseY + 0.11f, -34.1f), new Vector3(68f, 0.22f, 0.55f), pal.ConcreteLight);
            matteComb.AddBox(new Vector3(0f, baseY + 0.24f, -34.1f), new Vector3(68f, 0.02f, 0.14f), pal.HazardYellow);

            // 2. 出生台后方的挡土墙（把 1.2m 高的出生甲板与 -0.05m 的库场接上，同时封住集装箱底部）
            matteComb.AddBox(new Vector3(0f, 0.60f, -34.95f), new Vector3(16.4f, 1.30f, 0.35f), pal.ConcreteDark);

            // 3. 一座低矮长条分拨仓（墙高 3.2m，屋脊 5.4m）：只作为出生台后方的库场剪影。
            //    旧版两座 5.4/4.7m 高仓在俯视缩略图里占掉画幅底部两角，读成两块大蓝灰平台。
            BuildSawtoothWarehouse(matteComb, metalComb, glassComb, pal,
                origin: new Vector3(0f, baseY, -46.5f),
                widthX: 40.0f, lengthZ: 8.0f, wallH: 3.2f, bays: 3);

            // 4. 仓库前立面：3 个货台门 + 4 条窗带 + 通长雨棚，避免一整面灰墙
            for (int d = -1; d <= 1; d++)
            {
                float dx = d * 10f;
                matteComb.AddBox(new Vector3(dx, baseY + 1.1f, -42.45f), new Vector3(4.2f, 2.2f, 0.12f), pal.ContainerSlate);
                matteComb.AddBox(new Vector3(dx, baseY + 0.15f, -42.2f), new Vector3(4.4f, 0.3f, 0.7f), pal.ConcreteLight);
            }
            for (int w = -1; w <= 1; w += 2)
            {
                glassComb.AddBox(new Vector3(w * 12.5f, baseY + 2.35f, -42.45f), new Vector3(9.0f, 0.7f, 0.08f), pal.WindowGlass);
                glassComb.AddBox(new Vector3(w * 3.5f, baseY + 2.35f, -42.45f), new Vector3(4.0f, 0.7f, 0.08f), pal.WindowGlass);
            }
            metalComb.AddBox(new Vector3(0f, baseY + 2.75f, -42.0f), new Vector3(36f, 0.12f, 0.9f), pal.SteelDark);
            metalComb.AddBox(new Vector3(0f, baseY + 2.56f, -41.62f), new Vector3(36f, 0.30f, 0.06f), pal.HazardYellow);

            // 5. 岸沿集装箱堆（离出生台两侧各留缺口，读作后方堆场而不是一堵墙）
            Vector3 containerSize = new Vector3(2.44f, 2.59f, 6.06f);
            float[] stackXs = { -30.5f, -26.0f, -21.5f, 21.5f, 26.0f, 30.5f };
            Color[] stackCols = { pal.ContainerBlue, pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerBlue, pal.ContainerTeal };
            for (int i = 0; i < stackXs.Length; i++)
            {
                float sx = stackXs[i];
                int layers = (i % 3 == 0) ? 2 : 1;
                for (int l = 0; l < layers; l++)
                {
                    matteComb.AddBox(new Vector3(sx, baseY + containerSize.y * (l + 0.5f) + l * 0.02f, -36.6f),
                        containerSize, stackCols[i], Quaternion.identity);
                }
                // 箱角深色压条
                metalComb.AddBox(new Vector3(sx, baseY + containerSize.y * layers + 0.06f, -36.6f),
                    new Vector3(containerSize.x + 0.06f, 0.08f, containerSize.z + 0.06f), pal.SteelDark);
            }

            // 6. 高杆灯（3 座，灯头朝库场；每座 3 个小灯面，避免远看成一枚白叉）
            float[] lampXs = { -30f, 0f, 30f };
            for (int i = 0; i < lampXs.Length; i++)
            {
                Vector3 p = new Vector3(lampXs[i], baseY, -38.5f);
                metalComb.AddBox(p + Vector3.up * 6.2f, new Vector3(0.22f, 12.4f, 0.22f), pal.SteelDark);
                metalComb.AddBox(p + Vector3.up * 12.2f, new Vector3(1.6f, 0.12f, 0.55f), pal.SteelDark);
                for (int l = -1; l <= 1; l++)
                {
                    matteComb.AddBox(p + new Vector3(l * 0.45f, 11.95f, -0.06f),
                        new Vector3(0.34f, 0.22f, 0.36f), pal.LampGlass);
                }
            }

            // 7. 岸沿系缆桩（不进入场内，纯远景剪影）
            for (float x = -31f; x <= 31f; x += 6.2f)
            {
                metalComb.AddCylinder(new Vector3(x, baseY + 0.24f, -34.35f), 0.16f, 0.5f, 8, pal.SteelDark);
            }

            // 8. 南侧水面拖船（避开岸台：x=-40，z=-44）
            Vector3 tugBase = new Vector3(-40f, -1.35f, -44f);
            matteComb.AddBox(tugBase + new Vector3(0f, 0.55f, 0f), new Vector3(4.2f, 1.4f, 9.0f), pal.HullNavy);
            matteComb.AddBox(tugBase + new Vector3(0f, 1.55f, -0.6f), new Vector3(3.2f, 1.6f, 4.2f), pal.CabinWhite);
            matteComb.AddBox(tugBase + new Vector3(0f, 1.55f, 1.7f), new Vector3(2.6f, 1.2f, 2.0f), pal.ContainerRust);
            glassComb.AddBox(tugBase + new Vector3(0f, 1.9f, -2.35f), new Vector3(2.4f, 0.55f, 0.08f), pal.WindowGlass);
            metalComb.AddBox(tugBase + new Vector3(0f, 2.75f, -0.6f), new Vector3(3.4f, 0.14f, 4.4f), pal.SteelDark);
            metalComb.AddCylinder(tugBase + new Vector3(1.1f, 2.8f, -1.2f), 0.28f, 1.1f, 8, pal.SteelDark);
        }

        private static void BuildSawtoothWarehouse(MeshCombiner matteComb, MeshCombiner metalComb, MeshCombiner glassComb, BackdropPalette pal,
            Vector3 origin, float widthX, float lengthZ, float wallH, int bays)
        {
            Vector3 bodySz = new Vector3(widthX, wallH, lengthZ);
            matteComb.AddBox(origin + new Vector3(0f, wallH * 0.5f, 0f), bodySz, pal.WarmGray);

            float bayZ = lengthZ / bays;
            float toothH = 2.2f;
            float halfW = widthX * 0.5f;

            for (int b = 0; b < bays; b++)
            {
                float zFront = origin.z - lengthZ * 0.5f + b * bayZ;
                float zPeak = zFront + bayZ;

                // 1. 倾斜金属屋面
                Vector3 v0 = new Vector3(origin.x - halfW, origin.y + wallH, zFront);
                Vector3 v1 = new Vector3(origin.x + halfW, origin.y + wallH, zFront);
                Vector3 v2 = new Vector3(origin.x + halfW, origin.y + wallH + toothH, zPeak);
                Vector3 v3 = new Vector3(origin.x - halfW, origin.y + wallH + toothH, zPeak);
                Vector3 roofNorm = Vector3.Cross(v1 - v0, v3 - v0).normalized;
                metalComb.AddQuadRaw(v0, v1, v2, v3, roofNorm, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, pal.Steel);

                // 2. 垂直朝北的采光天窗
                Vector3 g0 = new Vector3(origin.x - halfW, origin.y + wallH, zPeak);
                Vector3 g1 = new Vector3(origin.x + halfW, origin.y + wallH, zPeak);
                Vector3 g2 = new Vector3(origin.x + halfW, origin.y + wallH + toothH, zPeak);
                Vector3 g3 = new Vector3(origin.x - halfW, origin.y + wallH + toothH, zPeak);
                glassComb.AddQuadRaw(g0, g1, g2, g3, Vector3.forward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, pal.WindowGlass);

                // 3. 左右两侧封堵山墙
                matteComb.AddQuadRaw(v0, g0, v3, v3, Vector3.left, Vector2.zero, Vector2.right, Vector2.up, Vector2.up, pal.WarmGray);
                matteComb.AddQuadRaw(v1, g1, g2, g2, Vector3.right, Vector2.zero, Vector2.right, Vector2.up, Vector2.up, pal.WarmGray);
            }
        }
        #endregion

        #region 模块 7：极远景与天际线剪影（删除近景大山，高度<6m极远低坡，天际线通透）
        /// <summary>
        /// 远景地平线：
        /// - 彻底删除近景高耸大山，天际线保持开阔通透；
        /// - 仅在 z>130m 或 |x|>100m 保留极低平的灰绿地平线轮廓（高度<6m）；
        /// - 2 座极远岸桥骨架剪影（z>135m）；
        /// - 西北海角防波堤与引航灯标。
        /// </summary>
        private static void BuildFarHorizon(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            const float waterY = -1.35f;

            // ---- 1. 极远低平地貌轮廓 (z>130m 或 |x|>100m，高度全部严格 < 6m，灰绿加雾) ----
            var subtleMounds = new[]
            {
                new { pos = new Vector3(-110f, waterY, 135f), r = 45f, h = 4.5f },
                new { pos = new Vector3(-40f,  waterY, 145f), r = 50f, h = 4.0f },
                new { pos = new Vector3(35f,   waterY, 142f), r = 48f, h = 4.2f },
                new { pos = new Vector3(115f,  waterY, 135f), r = 45f, h = 4.8f },
                new { pos = new Vector3(-115f, waterY, 40f),  r = 40f, h = 3.8f }
            };
            for (int i = 0; i < subtleMounds.Length; i++)
            {
                matteComb.AddCone(subtleMounds[i].pos, subtleMounds[i].r, 1.5f, subtleMounds[i].h, 8, pal.HorizonHill);
            }

            // ---- 2. 远景城市阶梯天际线剪影 (北东远方，z=105..125m，高度控制在 12-16m) ----
            float[] cityAngles = { 15f, 28f, 40f, 52f, 65f, 78f };
            for (int i = 0; i < cityAngles.Length; i++)
            {
                float ang = cityAngles[i] * Mathf.Deg2Rad;
                float dist = 115f + (i % 3) * 6f;
                float w = 12f + (i % 2) * 3f;
                float d = 11f;
                float h = 12f + ((i * 13) % 5) * 1.5f; // 最大高度 18m

                Vector3 pos = new Vector3(Mathf.Sin(ang) * dist, waterY + h * 0.5f, Mathf.Cos(ang) * dist);
                matteComb.AddBox(pos, new Vector3(w, h, d), pal.HorizonCity, Quaternion.Euler(0f, cityAngles[i], 0f));
            }

            // ---- 3. 极远景岸桥骨架剪影 (z>135m，纤细轮廓，强化海港纵深) ----
            Vector3[] farCranePos =
            {
                new Vector3(-15.0f, waterY, 138.0f),
                new Vector3(26.0f,  waterY, 142.0f)
            };
            for (int fc = 0; fc < farCranePos.Length; fc++)
            {
                Vector3 fp = farCranePos[fc];
                float cH = 28.0f;
                // 极远岸桥剪影：与新岸桥同族的钢青色，压入雾色 —— 旧版带黄，
                // 会在真岸桥正后方读成「重影黄塔」。
                Color silhouetteCol = Color.Lerp(pal.CraneSteelMain, pal.HorizonCity, 0.78f);

                metalComb.AddBox(fp + new Vector3(-3.5f, cH * 0.5f, 0f), new Vector3(0.7f, cH, 0.7f), silhouetteCol);
                metalComb.AddBox(fp + new Vector3(3.5f,  cH * 0.5f, 0f), new Vector3(0.7f, cH, 0.7f), silhouetteCol);
                metalComb.AddBox(fp + new Vector3(0f, cH - 3.5f, 0f), new Vector3(32.0f, 1.5f, 1.5f), silhouetteCol);
                metalComb.AddStayRod(fp + new Vector3(-3.0f, cH - 3.5f, 0f), fp + new Vector3(0f, cH + 4.0f, 0f), 0.20f, silhouetteCol);
                metalComb.AddStayRod(fp + new Vector3(3.0f,  cH - 3.5f, 0f), fp + new Vector3(0f, cH + 4.0f, 0f), 0.20f, silhouetteCol);
            }

            // ---- 4. 海湾防波堤与引航灯标 (位于西北海角 x=-75m, z=60m) ----
            Vector3 breakwaterP = new Vector3(-75.0f, waterY, 60.0f);
            matteComb.AddBox(breakwaterP + new Vector3(0f, 1.0f, 0f), new Vector3(18.0f, 2.0f, 4.0f), pal.ConcreteDark);
            Vector3 lightBeaconP = breakwaterP + new Vector3(7.0f, 2.0f, 0f);
            matteComb.AddCylinder(lightBeaconP, 0.9f, 1.4f, 8, pal.CabinWhite);
            matteComb.AddCylinder(lightBeaconP + Vector3.up * 1.4f, 0.8f, 1.4f, 8, pal.WaterlineRed);
            matteComb.AddCylinder(lightBeaconP + Vector3.up * 2.8f, 0.7f, 1.4f, 8, pal.CabinWhite);
            metalComb.AddBox(lightBeaconP + Vector3.up * 4.4f, new Vector3(0.8f, 0.6f, 0.8f), pal.HazardYellow);
        }
        #endregion

        #region 资源管理与场景装配
        private static void EnsureFolder()
        {
            if (!Directory.Exists(MeshFolder))
            {
                Directory.CreateDirectory(MeshFolder);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void SaveAsset(Mesh mesh, string path)
        {
            if (mesh == null) return;
            EnsureFolder();
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        private static GameObject CreateRendererChild(Transform parent, string name, Mesh mesh, Material mat)
        {
            if (mesh == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            if (mat != null)
            {
                mr.sharedMaterial = mat;
            }

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }
        #endregion

        #region 配色解包结构
        /// <summary>
        /// 风格解包与去饱和/冷暖色阶：剔除粉红杂色，还原经典工业港口质感。
        /// </summary>
        private readonly struct BackdropPalette
        {
            public readonly Color ConcreteLight;
            public readonly Color ConcreteMid;
            public readonly Color ConcreteDark;
            public readonly Color Steel;
            public readonly Color SteelDark;
            public readonly Color HazardYellow;
            public readonly Color HazardDark;
            public readonly Color ContainerTeal;
            public readonly Color ContainerRust;
            public readonly Color ContainerSlate;
            public readonly Color ContainerMustard;
            public readonly Color ContainerGreen;
            public readonly Color ContainerBlue;
            public readonly Color Wood;
            public readonly Color WoodDark;
            public readonly Color Foliage;
            public readonly Color LampGlass;
            public readonly Color HullNavy;
            public readonly Color WaterlineRed;
            public readonly Color CabinWhite;

            // 专属真实工业建筑配色（绝不使用鲑鱼粉）
            public readonly Color BrickRed;
            public readonly Color BrickDarkBrown;
            public readonly Color WarmGray;
            public readonly Color OfficeLightGray;
            public readonly Color CreamWhite;
            public readonly Color IndustrialPipeGreen;

            public readonly Color HorizonCity;
            public readonly Color HorizonHill;
            public readonly Color WindowGlass;
            public readonly Color WindowLit;

            // 「钢青自动化岸桥」配色（远景重设计）：不再与场内黄色英雄塔吊抢戏
            public readonly Color CraneSteelMain;
            public readonly Color CraneSteelPanel;
            public readonly Color CraneAccent;

            public BackdropPalette(ArenaStyleSO s, ArenaMaterialSet mats)
            {
                ConcreteLight = s != null ? s.concreteLight : new Color(0.80f, 0.78f, 0.72f, 1f);
                ConcreteMid   = s != null ? s.concreteMid   : new Color(0.64f, 0.62f, 0.57f, 1f);
                ConcreteDark  = s != null ? s.concreteDark  : new Color(0.42f, 0.41f, 0.38f, 1f);
                Steel         = s != null ? s.steel         : new Color(0.42f, 0.47f, 0.56f, 1f);
                SteelDark     = s != null ? s.steelDark     : new Color(0.24f, 0.27f, 0.32f, 1f);
                HazardYellow  = s != null ? s.hazardYellow  : new Color(0.86f, 0.68f, 0.20f, 1f);
                HazardDark    = s != null ? s.hazardDark    : new Color(0.16f, 0.16f, 0.16f, 1f);
                ContainerTeal = s != null ? s.containerTeal : new Color(0.22f, 0.56f, 0.54f, 1f);
                ContainerRust = s != null ? s.containerRust : new Color(0.68f, 0.32f, 0.22f, 1f);
                ContainerSlate= s != null ? s.containerSlate: new Color(0.36f, 0.47f, 0.62f, 1f);
                ContainerMustard= s!= null ? s.containerMustard: new Color(0.85f, 0.64f, 0.22f, 1f);
                Wood          = s != null ? s.wood          : new Color(0.72f, 0.58f, 0.42f, 1f);
                WoodDark      = s != null ? s.woodDark      : new Color(0.48f, 0.36f, 0.24f, 1f);
                Foliage       = s != null ? s.foliage       : new Color(0.24f, 0.45f, 0.28f, 1f);
                LampGlass     = s != null ? s.lampGlass     : new Color(1.00f, 0.85f, 0.60f, 1f);

                ContainerGreen = new Color(0.18f, 0.58f, 0.28f, 1f);
                ContainerBlue  = new Color(0.12f, 0.38f, 0.78f, 1f);
                HullNavy       = new Color(0.12f, 0.16f, 0.24f, 1f);
                WaterlineRed   = new Color(0.65f, 0.22f, 0.18f, 1f);
                CabinWhite     = new Color(0.90f, 0.92f, 0.94f, 1f);

                // 建筑真实质感调色
                BrickRed        = new Color(0.42f, 0.22f, 0.18f, 1f); // 焦褐红砖
                BrickDarkBrown  = new Color(0.32f, 0.25f, 0.21f, 1f); // 工业深棕砖
                WarmGray        = new Color(0.50f, 0.48f, 0.45f, 1f); // 风化暖灰混凝土
                OfficeLightGray = new Color(0.74f, 0.76f, 0.77f, 1f); // 现代办公楼浅冷灰
                CreamWhite      = new Color(0.85f, 0.84f, 0.80f, 1f); // 建筑米白

                IndustrialPipeGreen = new Color(0.24f, 0.46f, 0.40f, 1f);
                HorizonCity    = Color.Lerp(Color.Lerp(SteelDark, ConcreteMid, 0.35f), OfficeLightGray, 0.30f);
                // 极远地貌加深雾化与去饱和
                HorizonHill    = Color.Lerp(Color.Lerp(Foliage, SteelDark, 0.65f), ConcreteMid, 0.25f);
                WindowGlass    = new Color(0.18f, 0.25f, 0.32f, 1f);
                WindowLit      = new Color(1.00f, 0.88f, 0.54f, 1f);

                // 岸桥钢青三色：主结构 = SteelDark→港蓝容器中线，面板亮一档，色带 = 港青集装箱色。
                // 全部由画风 SO 派生，跟随 ArenaStyle_NeonSkatepark 调色自动变化。
                CraneSteelMain  = Color.Lerp(SteelDark, ContainerSlate, 0.50f);
                CraneSteelPanel = Color.Lerp(Steel, ContainerSlate, 0.40f);
                CraneAccent     = ContainerTeal;
            }
        }
        #endregion

        #region 多来源网格合并器 MeshCombiner
        private sealed class MeshCombiner
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(8192);
            public readonly List<Vector3> Normals = new List<Vector3>(8192);
            public readonly List<Vector2> UVs = new List<Vector2>(8192);
            public readonly List<Color> Colors = new List<Color>(8192);
            public readonly List<int> Triangles = new List<int>(12288);

            private static Color Linear(Color c)
            {
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            }

            public void AddQuadRaw(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal,
                Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, Color color)
            {
                int start = Vertices.Count;
                Vertices.Add(v0); Vertices.Add(v1); Vertices.Add(v2); Vertices.Add(v3);
                Normals.Add(normal); Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
                UVs.Add(uv0); UVs.Add(uv1); UVs.Add(uv2); UVs.Add(uv3);
                Color linearColor = Linear(color);
                Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor);

                Triangles.Add(start); Triangles.Add(start + 3); Triangles.Add(start + 2);
                Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 1);
            }

            public void AddBox(Vector3 center, Vector3 size, Color color, Quaternion? rot = null)
            {
                Vector3 h = size * 0.5f;
                Matrix4x4 trs = Matrix4x4.TRS(center, rot ?? Quaternion.identity, Vector3.one);

                void AddSide(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 norm)
                {
                    Vector3 wp0 = trs.MultiplyPoint3x4(p0);
                    Vector3 wp1 = trs.MultiplyPoint3x4(p1);
                    Vector3 wp2 = trs.MultiplyPoint3x4(p2);
                    Vector3 wp3 = trs.MultiplyPoint3x4(p3);
                    Vector3 wn = trs.MultiplyVector(norm).normalized;

                    AddQuadRaw(wp0, wp1, wp2, wp3, wn,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), color);
                }

                // +Y (顶面)
                AddSide(new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.up);
                // -Y (底面)
                AddSide(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), Vector3.down);
                // +Z (北面)
                AddSide(new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), Vector3.forward);
                // -Z (南面)
                AddSide(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), Vector3.back);
                // +X (东面)
                AddSide(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), Vector3.right);
                // -X (西面)
                AddSide(new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), Vector3.left);
            }

            public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Color color, Quaternion? rot = null)
            {
                int segs = Mathf.Max(segments, 6);
                Matrix4x4 trs = Matrix4x4.TRS(baseCenter, rot ?? Quaternion.identity, Vector3.one);
                Matrix4x4 normM = trs.inverse.transpose;
                Color linearColor = Linear(color);

                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segs;
                    float a1 = (i + 1) * Mathf.PI * 2f / segs;

                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                    Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                    Vector3 p2 = new Vector3(Mathf.Cos(a1) * radius, height, Mathf.Sin(a1) * radius);
                    Vector3 p3 = new Vector3(Mathf.Cos(a0) * radius, height, Mathf.Sin(a0) * radius);

                    Vector3 n0 = normM.MultiplyVector(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0))).normalized;
                    Vector3 n1 = normM.MultiplyVector(new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1))).normalized;

                    Vector3 wp0 = trs.MultiplyPoint3x4(p0);
                    Vector3 wp1 = trs.MultiplyPoint3x4(p1);
                    Vector3 wp2 = trs.MultiplyPoint3x4(p2);
                    Vector3 wp3 = trs.MultiplyPoint3x4(p3);

                    int start = Vertices.Count;
                    Vertices.Add(wp0); Vertices.Add(wp1); Vertices.Add(wp2); Vertices.Add(wp3);
                    Normals.Add(n0); Normals.Add(n1); Normals.Add(n1); Normals.Add(n0);
                    UVs.Add(new Vector2(0f, 0f)); UVs.Add(new Vector2(1f, 0f)); UVs.Add(new Vector2(1f, 1f)); UVs.Add(new Vector2(0f, 1f));
                    Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor);

                    Triangles.Add(start); Triangles.Add(start + 3); Triangles.Add(start + 2);
                    Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 1);
                }

                // 顶盖
                int centerIdx = Vertices.Count;
                Vector3 topCenter = trs.MultiplyPoint3x4(new Vector3(0f, height, 0f));
                Vector3 upNorm = normM.MultiplyVector(Vector3.up).normalized;
                Vertices.Add(topCenter);
                Normals.Add(upNorm);
                UVs.Add(new Vector2(0.5f, 0.5f));
                Colors.Add(linearColor);

                int rimStart = Vertices.Count;
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    Vector3 p = trs.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
                    Vertices.Add(p);
                    Normals.Add(upNorm);
                    UVs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                    Colors.Add(linearColor);
                }

                for (int i = 0; i < segs; i++)
                {
                    int next = (i + 1) % segs;
                    Triangles.Add(centerIdx);
                    Triangles.Add(rimStart + next);
                    Triangles.Add(rimStart + i);
                }
            }

            public void AddCone(Vector3 baseCenter, float bottomRadius, float topRadius, float height, int segments, Color color)
            {
                int segs = Mathf.Max(segments, 3);
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segs;
                    float a1 = (i + 1) * Mathf.PI * 2f / segs;

                    Vector3 vb0 = baseCenter + new Vector3(Mathf.Cos(a0) * bottomRadius, 0f, Mathf.Sin(a0) * bottomRadius);
                    Vector3 vb1 = baseCenter + new Vector3(Mathf.Cos(a1) * bottomRadius, 0f, Mathf.Sin(a1) * bottomRadius);
                    Vector3 vt1 = baseCenter + new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
                    Vector3 vt0 = baseCenter + new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);

                    Vector3 n0 = new Vector3(Mathf.Cos(a0), 0.2f, Mathf.Sin(a0)).normalized;
                    Vector3 n1 = new Vector3(Mathf.Cos(a1), 0.2f, Mathf.Sin(a1)).normalized;

                    int start = Vertices.Count;
                    Vertices.Add(vb0); Vertices.Add(vb1); Vertices.Add(vt1); Vertices.Add(vt0);
                    Normals.Add(n0); Normals.Add(n1); Normals.Add(n1); Normals.Add(n0);
                    UVs.Add(new Vector2(0f, 0f)); UVs.Add(new Vector2(1f, 0f)); UVs.Add(new Vector2(1f, 1f)); UVs.Add(new Vector2(0f, 1f));
                    Color linearColor = Linear(color);
                    Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor); Colors.Add(linearColor);

                    Triangles.Add(start); Triangles.Add(start + 3); Triangles.Add(start + 2);
                    Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 1);
                }
            }

            public void AddHazardBogie(Vector3 center, Vector3 size, Color yellow, Color dark, Quaternion rot)
            {
                AddBox(center, size, dark, rot);

                float halfX = size.x * 0.5f + 0.01f;
                float halfY = size.y * 0.5f;
                float halfZ = size.z * 0.5f;

                int stripes = 6;
                float stepZ = size.z / stripes;
                float slantZ = stepZ * 0.5f;

                for (int s = 0; s < stripes; s++)
                {
                    Color col = (s % 2 == 0) ? yellow : dark;
                    float z0 = -halfZ + s * stepZ;
                    float z1 = z0 + stepZ;

                    Vector3 p0 = center + rot * new Vector3(halfX, -halfY, z0);
                    Vector3 p1 = center + rot * new Vector3(halfX, -halfY, z1);
                    Vector3 p2 = center + rot * new Vector3(halfX, halfY, z1 + slantZ);
                    Vector3 p3 = center + rot * new Vector3(halfX, halfY, z0 + slantZ);
                    Vector3 nX = rot * Vector3.right;
                    AddQuadRaw(p0, p1, p2, p3, nX, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, col);

                    Vector3 q0 = center + rot * new Vector3(-halfX, -halfY, z1);
                    Vector3 q1 = center + rot * new Vector3(-halfX, -halfY, z0);
                    Vector3 q2 = center + rot * new Vector3(-halfX, halfY, z0 + slantZ);
                    Vector3 q3 = center + rot * new Vector3(-halfX, halfY, z1 + slantZ);
                    Vector3 nXn = rot * Vector3.left;
                    AddQuadRaw(q0, q1, q2, q3, nXn, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, col);
                }
            }

            public void AddStayRod(Vector3 p0, Vector3 p1, float thickness, Color color)
            {
                Vector3 mid = (p0 + p1) * 0.5f;
                float len = Vector3.Distance(p0, p1);
                if (len < 0.001f) return;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized);
                AddBox(mid, new Vector3(thickness, len, thickness), color, rot);
            }

            public Mesh BuildMesh(string meshName)
            {
                if (Vertices.Count == 0) return null;

                var mesh = new Mesh { name = meshName };
                if (Vertices.Count > 60000)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

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
