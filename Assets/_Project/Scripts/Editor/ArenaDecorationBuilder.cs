using System;
using System.Collections.Generic;
using System.IO;
using P1.Environment;
using P1.Ink;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Editor
{
    /// <summary>
    /// 竞技场场景装饰与港口远景构建器（P7-B）：
    /// 负责按 ArenaLayoutV2 数据与 ArenaStyleSO 风格，生成合并网格资产并保存至磁盘，
    /// 并在场景中装配静态近景道具（4 材质合并 + 墙面横幅）与港口远景（码头基座、水面、集装箱堆场、岸桥、货船、城市剪影）。
    /// 完全兼容 URP 与 SRP Batcher，全场 draw call 增量受控在 ~8 次以内。
    /// </summary>
    public static class ArenaDecorationBuilder
    {
        public const string MeshFolder = "Assets/_Project/Art/Meshes/Arena";

        private const string InkConfigPath = "Assets/_Project/ScriptableObjects/InkGridConfig.asset";

        #region 公开构建入口
        /// <summary>
        /// 构建场内地表道具与边界墙横幅，合并为 4 个材质子网格 + 1 个横幅网格。
        /// </summary>
        public static GameObject Build(Transform parent, ArenaStyleSO style, ArenaMaterialSet mats)
        {
            var decorRoot = new GameObject("Arena_Decor");
            if (parent != null)
            {
                decorRoot.transform.SetParent(parent, false);
            }

            EnsureFolder();

            // 1. 遍历道具并按密度过滤
            float density = style != null ? style.decorationDensity : 1.0f;
            // 实体掩体与纯装饰分开合批。实体掩体（木箱/花坛/售卖亭）不再使用合并网格的
            // MeshCollider：改为每个道具一个盒碰撞代理 + InkWallSurface（见 CreateInkPropProxy），
            // 这样弹丸命中时能把墨涂在对应立面上，而不是穿透到地面。
            // 锥桶、系缆桩、旗杆、墙顶空调与管线这类细杆件仍不带碰撞：
            // 它们会把 1.25m 高的角色胶囊顶住，同时挡子弹、切碎 Bot 寻路。
            var matteSolidBucket = new MeshCombiner();
            var metalSolidBucket = new MeshCombiner();
            var matteDecorBucket = new MeshCombiner();
            var metalDecorBucket = new MeshCombiner();
            var emissiveBucket = new MeshCombiner();
            var clothBucket = new MeshCombiner();

            var propParts = new List<ArenaPropMeshPart>(8);
            InkGridConfigSO inkCfg = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>(InkConfigPath);
            int proxySerial = 0;

            foreach (var prop in ArenaLayoutV2.Props)
            {
                // 出生区道具、路灯与旗杆始终保留；移动端低密度下跳过部分小装饰
                bool isPreserved = prop.Kind == ArenaLayoutV2.ArenaPropKind.LampPost
                                   || prop.Kind == ArenaLayoutV2.ArenaPropKind.FlagPole
                                   || prop.Kind == ArenaLayoutV2.ArenaPropKind.Kiosk
                                   || Mathf.Abs(prop.Position.z) > 20.0f;

                if (density < 0.99f && !isPreserved && (Mathf.Abs(prop.Variant) % 3 == 2))
                {
                    continue;
                }

                propParts.Clear();
                ArenaPropMeshFactory.BuildProp(prop.Kind, prop.Scale, prop.Variant, style, propParts);

                Quaternion rot = Quaternion.Euler(0f, prop.Yaw, 0f);
                Vector3 pos = prop.Position;
                bool solid = ArenaLayoutV2.IsSolidProp(prop.Kind);

                // 实体部件包围盒（道具局部空间）：既做碰撞盒，也做四面涂墨立面的尺寸。
                Bounds solidBounds = default;
                bool hasSolidBounds = false;

                for (int i = 0; i < propParts.Count; i++)
                {
                    var part = propParts[i];
                    if (part.Mesh == null) continue;

                    if (solid && part.Solid)
                    {
                        if (!hasSolidBounds)
                        {
                            solidBounds = part.Mesh.bounds;
                            hasSolidBounds = true;
                        }
                        else
                        {
                            solidBounds.Encapsulate(part.Mesh.bounds);
                        }
                    }

                    switch (part.Surface)
                    {
                        case ArenaPropSurface.Matte:
                            (solid ? matteSolidBucket : matteDecorBucket).AddMesh(part.Mesh, pos, rot, Vector3.one, part.Color);
                            break;
                        case ArenaPropSurface.Metal:
                            (solid ? metalSolidBucket : metalDecorBucket).AddMesh(part.Mesh, pos, rot, Vector3.one, part.Color);
                            break;
                        case ArenaPropSurface.Emissive:
                            emissiveBucket.AddMesh(part.Mesh, pos, rot, Vector3.one, part.Color);
                            break;
                        case ArenaPropSurface.Cloth:
                            clothBucket.AddMesh(part.Mesh, pos, rot, Vector3.one, part.Color);
                            break;
                    }

                    // 释放临时生成的零散 Mesh，避免编辑器内存堆积
                    UnityEngine.Object.DestroyImmediate(part.Mesh);
                }

                // 实体道具：每个道具一个盒碰撞代理 + 四面可涂墨面。
                // 旋转盒必须用朝向盒建面（见 InkWallSurface.ConfigureOrientedBox），
                // 用世界 AABB 会让旋转木箱/花坛的墨片悬在四个角外。
                if (solid && hasSolidBounds)
                {
                    CreateInkPropProxy(decorRoot.transform, prop, rot, solidBounds, mats, inkCfg, proxySerial++);
                }
            }

            // 2. 生成并保存 6 个合并网格资产
            Mesh matteMesh = matteSolidBucket.BuildMesh("arena_decor_matte");
            Mesh metalMesh = metalSolidBucket.BuildMesh("arena_decor_metal");
            Mesh matteDecorMesh = matteDecorBucket.BuildMesh("arena_decor_matte_thin");
            Mesh metalDecorMesh = metalDecorBucket.BuildMesh("arena_decor_metal_thin");
            Mesh emissiveMesh = emissiveBucket.BuildMesh("arena_decor_emissive");
            Mesh clothMesh = clothBucket.BuildMesh("arena_decor_cloth");

            SaveAsset(matteMesh, $"{MeshFolder}/arena_decor_matte.asset");
            SaveAsset(metalMesh, $"{MeshFolder}/arena_decor_metal.asset");
            SaveAsset(matteDecorMesh, $"{MeshFolder}/arena_decor_matte_thin.asset");
            SaveAsset(metalDecorMesh, $"{MeshFolder}/arena_decor_metal_thin.asset");
            SaveAsset(emissiveMesh, $"{MeshFolder}/arena_decor_emissive.asset");
            SaveAsset(clothMesh, $"{MeshFolder}/arena_decor_cloth.asset");

            // 3. 创建子渲染对象 (近景装饰阴影开启；碰撞与涂墨由每个实体道具的盒代理负责，
            //    合并网格不再挂 MeshCollider —— 否则弹丸命中合并网格找不到对应的 InkWallSurface)
            CreateRendererChild(decorRoot.transform, "Decor_Matte", matteMesh, mats?.propMatte ?? mats?.concrete, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Metal", metalMesh, mats?.propMetal ?? mats?.steel, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Matte_Thin", matteDecorMesh, mats?.propMatte ?? mats?.concrete, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Metal_Thin", metalDecorMesh, mats?.propMetal ?? mats?.steel, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Emissive", emissiveMesh, mats?.propEmissive ?? mats?.propMatte, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Cloth", clothMesh, mats?.propCloth ?? mats?.propMatte, true, false);

            // 4. 边界墙内侧图集横幅 (6-8 个 Quad，独占 mats.bannerAtlas)
            Mesh bannersMesh = BuildWallBannersMesh();
            SaveAsset(bannersMesh, $"{MeshFolder}/arena_banners.asset");
            CreateRendererChild(decorRoot.transform, "Banners", bannersMesh, mats?.bannerAtlas ?? mats?.propMatte, false);

            return decorRoot;
        }

        /// <summary>
        /// 构建港口远景（码头基座、水面、集装箱堆场、岸桥、货船、城市剪影）。
        /// 无 Collider、无阴影投放与接收、静态合批。
        /// </summary>
        public static GameObject BuildBackdrop(Transform parent, ArenaStyleSO style, ArenaMaterialSet mats)
        {
            if (style != null && !style.buildBackdrop)
            {
                return null;
            }

            var backdropRoot = new GameObject("_ArenaBackdrop");
            if (parent != null)
            {
                backdropRoot.transform.SetParent(parent, false);
            }

            EnsureFolder();

            var palette = new BackdropPalette(style);

            // 1. 水面单独网格 (240x240m Quad, y=-1.35)
            Mesh waterMesh = BuildWaterMesh(240f, -1.35f);
            SaveAsset(waterMesh, $"{MeshFolder}/arena_backdrop_water.asset");
            CreateRendererChild(backdropRoot.transform, "Backdrop_Water", waterMesh, mats?.water, false);

            // 2. 远景按 Matte / Metal 材质合并
            var matteBucket = new MeshCombiner();
            var metalBucket = new MeshCombiner();

            // 2.1 码头混凝土高台基座 (52 x 1.1 x 68m, 顶面 y=-0.15)
            BuildPierBase(matteBucket, palette);

            // 2.2 场外集装箱堆场 (24 组 1-3 层集装箱堆叠)
            BuildContainerYards(matteBucket, palette);

            // 2.3 岸桥/龙门吊 (4 座黄色桁架剪影，高 16m，位于外场四角)
            BuildGantryCranes(metalBucket, palette);

            // 2.4 仓库 4 座 + 货船 2 艘
            BuildWarehousesAndShips(matteBucket, metalBucket, palette);

            // 2.5 远景城市剪影 24 座方块 + 低多边形山丘 8 座 (半径 78-94m)
            BuildCityAndHills(matteBucket, palette);

            // 3. 保存合并资产并创建子物体
            Mesh matteMesh = matteBucket.BuildMesh("arena_backdrop_matte");
            Mesh metalMesh = metalBucket.BuildMesh("arena_backdrop_metal");

            SaveAsset(matteMesh, $"{MeshFolder}/arena_backdrop_matte.asset");
            SaveAsset(metalMesh, $"{MeshFolder}/arena_backdrop_metal.asset");

            CreateRendererChild(backdropRoot.transform, "Backdrop_Matte", matteMesh, mats?.propMatte ?? mats?.concrete, false);
            CreateRendererChild(backdropRoot.transform, "Backdrop_Metal", metalMesh, mats?.propMetal ?? mats?.steel, false);

            return backdropRoot;
        }
        #endregion

        #region 边界墙横幅网格生成
        /// <summary>
        /// 在边界墙内侧生成 8 个横幅 Quad，分 4 个 UV 图集区域。
        /// 距墙面只留 4mm：墨迹立面 Quad 在墙外 10mm 处，涂墨后墨会盖住横幅，
        /// 不再出现"横幅挡在墨前面、打了看不见"的死角（未涂区域仍能看到横幅）。
        /// </summary>
        private static Mesh BuildWallBannersMesh()
        {
            var combiner = new MeshCombiner();
            float w = 6.0f;
            float h = 2.4f;
            float cy = 1.2f;
            const float bannerOffset = 0.004f;

            // 北墙内侧 (z=+28.0，朝 -Z，正常墙高足够)
            float northZ = 28.0f - bannerOffset;
            AddBannerQuad(combiner, new Vector3(-8.0f, cy, northZ), w, h, Vector3.back, 0);
            AddBannerQuad(combiner, new Vector3(8.0f, cy, northZ), w, h, Vector3.back, 1);

            // 西墙内侧 (x=-20.0，朝 +X)
            float westX = -20.0f + bannerOffset;
            AddBannerQuad(combiner, new Vector3(westX, cy, -14.0f), w, h, Vector3.right, 2);
            AddBannerQuad(combiner, new Vector3(westX, cy, 0.0f), w, h, Vector3.right, 3);
            AddBannerQuad(combiner, new Vector3(westX, cy, 14.0f), w, h, Vector3.right, 0);

            // 东墙内侧 (x=+20.0，朝 -X)
            float eastX = 20.0f - bannerOffset;
            AddBannerQuad(combiner, new Vector3(eastX, cy, -14.0f), w, h, Vector3.left, 1);
            AddBannerQuad(combiner, new Vector3(eastX, cy, 0.0f), w, h, Vector3.left, 2);
            AddBannerQuad(combiner, new Vector3(eastX, cy, 14.0f), w, h, Vector3.left, 3);

            // 南墙为矮围墙 (0.35m)，按契约跳过
            return combiner.BuildMesh("arena_banners");
        }

        private static void AddBannerQuad(MeshCombiner comb, Vector3 center, float width, float height, Vector3 normal, int atlasIndex)
        {
            int p = Mathf.Abs(atlasIndex) % 4;
            // 图集是 2×2：索引 0/1 在下半（v 0~0.5），2/3 在上半（v 0.5~1）。
            float u0 = (p % 2) * 0.5f;
            float u1 = u0 + 0.5f;
            float v0 = (p / 2) * 0.5f;
            float v1 = v0 + 0.5f;

            // 正视四边形时，right 指向观察者的右手侧，避免文字水平镜像
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 up = Vector3.up;

            Vector3 halfR = right * (width * 0.5f);
            Vector3 halfU = up * (height * 0.5f);

            Vector3 vtx0 = center - halfR - halfU;
            Vector3 vtx1 = center + halfR - halfU;
            Vector3 vtx2 = center + halfR + halfU;
            Vector3 vtx3 = center - halfR + halfU;

            comb.AddQuadRaw(vtx0, vtx1, vtx2, vtx3, normal,
                new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), Color.white);
        }
        #endregion

        #region 远景各模块生成
        /// <summary>
        /// 码头基座：64x1.1x76m 混凝土基座，顶面 y=-0.15，彻底托住两翼集装箱堆场。
        /// </summary>
        private static void BuildPierBase(MeshCombiner comb, BackdropPalette pal)
        {
            Vector3 sz = new Vector3(64f, 1.1f, 76f);
            Vector3 center = new Vector3(0f, -0.70f, 0f);
            comb.AddBox(center, sz, pal.ConcreteDark);
        }

        /// <summary>
        /// 水面 Quad (240x240m)。
        /// </summary>
        private static Mesh BuildWaterMesh(float size, float yPos)
        {
            var comb = new MeshCombiner();
            float h = size * 0.5f;

            Vector3 v0 = new Vector3(-h, yPos, -h);
            Vector3 v1 = new Vector3(h, yPos, -h);
            Vector3 v2 = new Vector3(h, yPos, h);
            Vector3 v3 = new Vector3(-h, yPos, h);

            comb.AddQuadRaw(v0, v1, v2, v3, Vector3.up,
                new Vector2(0f, 0f), new Vector2(16f, 0f), new Vector2(16f, 16f), new Vector2(0f, 16f), Color.white);

            return comb.BuildMesh("arena_backdrop_water");
        }

        /// <summary>
        /// 场外集装箱堆场 (布置 24 组 1-3 层集装箱)。
        /// </summary>
        private static void BuildContainerYards(MeshCombiner comb, BackdropPalette pal)
        {
            Color[] colors = { pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerSlate };
            Vector3 cSz = new Vector3(2.44f, 2.59f, 6.06f);

            void AddStack(Vector3 basePos, float yaw, int tiers, int colOffset)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 localOffset = new Vector3(t * 0.04f, t * cSz.y + cSz.y * 0.5f - 0.15f, 0f);
                    Vector3 worldPos = basePos + rot * localOffset;
                    Color col = colors[(t + colOffset) % colors.Length];
                    comb.AddBox(worldPos, cSz, col, rot);
                }
            }

            // 西侧外场堆场 (10 组，Z 限制在 -30 到 30 之间，确保稳立在码头基座上)
            float[] westZ = { -30f, -24f, -17f, -10f, -3f, 4f, 11f, 18f, 24f, 30f };
            for (int i = 0; i < westZ.Length; i++)
            {
                float x = -25f - (i % 3) * 3.4f;
                float yaw = ((i * 17) % 7 - 3) * 1.5f;
                int tiers = (i % 3) + 1;
                AddStack(new Vector3(x, 0f, westZ[i]), yaw, tiers, i);
            }

            // 东侧外场堆场 (10 组)
            float[] eastZ = { -30f, -23f, -16f, -9f, -2f, 5f, 12f, 19f, 25f, 30f };
            for (int i = 0; i < eastZ.Length; i++)
            {
                float x = 25f + (i % 3) * 3.4f;
                float yaw = ((i * 13) % 7 - 3) * 1.5f;
                int tiers = ((i + 1) % 3) + 1;
                AddStack(new Vector3(x, 0f, eastZ[i]), yaw, tiers, i + 2);
            }

            // 北侧延展区 (2 组，位于基座末端 z=32)
            AddStack(new Vector3(-10f, 0f, 32f), 85f, 2, 1);
            AddStack(new Vector3(10f, 0f, 32f), -85f, 3, 3);
        }

        /// <summary>
        /// 岸桥/龙门吊 (4 座黄色钢构桁架，高 16m，立于四角)。
        /// </summary>
        private static void BuildGantryCranes(MeshCombiner metalComb, BackdropPalette pal)
        {
            Vector3[] positions =
            {
                new Vector3(-28.5f, -0.15f, -34.0f),
                new Vector3(-28.5f, -0.15f, 34.0f),
                new Vector3(28.5f, -0.15f, -34.0f),
                new Vector3(28.5f, -0.15f, 34.0f),
            };

            float[] yaws = { 15f, -15f, 165f, -165f };

            for (int k = 0; k < positions.Length; k++)
            {
                Vector3 origin = positions[k];
                Quaternion rot = Quaternion.Euler(0f, yaws[k], 0f);

                // 4 根倾斜金属桁架构件腿 (由地面升至 12m)
                float legH = 12f;
                float spanX = 6.0f;
                float spanZ = 8.0f;

                Vector3 l1 = origin + rot * new Vector3(-spanX * 0.5f, legH * 0.5f, -spanZ * 0.5f);
                Vector3 l2 = origin + rot * new Vector3(spanX * 0.5f, legH * 0.5f, -spanZ * 0.5f);
                Vector3 l3 = origin + rot * new Vector3(-spanX * 0.5f, legH * 0.5f, spanZ * 0.5f);
                Vector3 l4 = origin + rot * new Vector3(spanX * 0.5f, legH * 0.5f, spanZ * 0.5f);

                Vector3 legSz = new Vector3(0.55f, legH, 0.55f);
                metalComb.AddBox(l1, legSz, pal.HazardYellow, rot);
                metalComb.AddBox(l2, legSz, pal.HazardYellow, rot);
                metalComb.AddBox(l3, legSz, pal.HazardYellow, rot);
                metalComb.AddBox(l4, legSz, pal.HazardYellow, rot);

                // 腿部水平与对角加强桁架
                metalComb.AddBox(origin + rot * new Vector3(0f, legH, -spanZ * 0.5f), new Vector3(spanX, 0.45f, 0.45f), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(0f, legH, spanZ * 0.5f), new Vector3(spanX, 0.45f, 0.45f), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, legH, 0f), new Vector3(0.45f, 0.45f, spanZ), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f, legH, 0f), new Vector3(0.45f, 0.45f, spanZ), pal.HazardYellow, rot);

                // 顶部向外挑出的主悬臂桁架 (长 22m，高 1.6m)
                Vector3 boomCenter = origin + rot * new Vector3(-4f, legH + 1.2f, 0f);
                metalComb.AddBox(boomCenter, new Vector3(22f, 1.6f, 2.2f), pal.HazardYellow, rot);

                // 顶部后方重载卷扬机房 (深钢色)
                Vector3 cabCenter = origin + rot * new Vector3(5f, legH + 2.6f, 0f);
                metalComb.AddBox(cabCenter, new Vector3(4.5f, 2.2f, 3.2f), pal.SteelDark, rot);

                // 下挂吊具小车 (吊在悬臂下方)
                Vector3 trolleyCenter = origin + rot * new Vector3(-5f, legH + 0.1f, 0f);
                metalComb.AddBox(trolleyCenter, new Vector3(2.5f, 0.8f, 1.8f), pal.SteelDark, rot);
            }
        }

        /// <summary>
        /// 港口仓库 4 座 + 货船 2 艘。
        /// </summary>
        private static void BuildWarehousesAndShips(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            // 4 座港口货运仓库 (混凝土墙 + 钢构双坡屋顶)
            Vector3[] whPos =
            {
                new Vector3(-37f, -0.15f, -18f),
                new Vector3(-37f, -0.15f, 18f),
                new Vector3(37f, -0.15f, -18f),
                new Vector3(37f, -0.15f, 18f),
            };

            for (int i = 0; i < whPos.Length; i++)
            {
                Vector3 p = whPos[i];
                float w = 14f;
                float wallH = 6.5f;
                float len = 22f;

                // 混凝土仓库主体
                matteComb.AddBox(p + Vector3.up * (wallH * 0.5f), new Vector3(w, wallH, len), pal.ConcreteMid);

                // 双坡屋顶 (金属色)
                metalComb.AddBox(p + Vector3.up * (wallH + 1.2f), new Vector3(w * 1.06f, 1.8f, len * 1.02f), pal.Steel);
                // 门洞深色装饰
                matteComb.AddBox(p + Vector3.up * 2.2f + (i < 2 ? Vector3.right : Vector3.left) * (w * 0.505f),
                    new Vector3(0.1f, 4.2f, 6.0f), pal.SteelDark);
            }

            // 2 艘集装箱货船 (漂浮在水面 y=-1.35)
            void AddCargoShip(Vector3 origin, float yaw, Color hullColor)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                float shipL = 54f;
                float shipW = 11f;
                float hullH = 4.8f;

                // 船体 (平直中段 + 尖削船首)
                Vector3 midPos = origin + rot * new Vector3(0f, hullH * 0.5f, 0f);
                matteComb.AddBox(midPos, new Vector3(shipW, hullH, shipL * 0.7f), hullColor, rot);

                Vector3 bowPos = origin + rot * new Vector3(0f, hullH * 0.5f, shipL * 0.42f);
                matteComb.AddBox(bowPos, new Vector3(shipW * 0.7f, hullH, shipL * 0.28f), hullColor, rot);

                // 船尾驾驶楼 (白浅灰 4 层塔楼)
                Vector3 bridgePos = origin + rot * new Vector3(0f, hullH + 4.5f, -shipL * 0.26f);
                matteComb.AddBox(bridgePos, new Vector3(shipW * 0.85f, 8.5f, 7.5f), pal.ConcreteLight, rot);

                // 楼顶金属雷达桅杆
                metalComb.AddBox(bridgePos + Vector3.up * 5.2f, new Vector3(0.4f, 4.5f, 0.4f), pal.Steel, rot);

                // 甲板集装箱垛 (密集阵列)
                Color[] cCols = { pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerSlate };
                for (int row = -1; row <= 1; row++)
                {
                    for (int col = 0; col < 4; col++)
                    {
                        for (int tier = 0; tier < 2; tier++)
                        {
                            Vector3 boxPos = origin + rot * new Vector3(row * 2.8f, hullH + tier * 2.6f + 1.3f, -4f + col * 6.5f);
                            Color cCol = cCols[(row + col + tier + 5) % cCols.Length];
                            matteComb.AddBox(boxPos, new Vector3(2.44f, 2.59f, 6.06f), cCol, rot);
                        }
                    }
                }
            }

            AddCargoShip(new Vector3(-62f, -1.35f, 8f), 8f, new Color(0.22f, 0.24f, 0.28f, 1f));
            AddCargoShip(new Vector3(62f, -1.35f, -12f), 188f, new Color(0.32f, 0.22f, 0.20f, 1f));
        }

        /// <summary>
        /// 远景城市剪影 24 座方块 + 低多边形山丘 8 座 (半径 78-94m)。
        /// </summary>
        private static void BuildCityAndHills(MeshCombiner matteComb, BackdropPalette pal)
        {
            // 远景城市剪影 (24 座方块楼，半径 78-92m，沉稳的青蓝深灰)
            Color cityColor = new Color(0.22f, 0.28f, 0.36f, 1f);
            float[] cityAngles =
            {
                25f, 38f, 52f, 66f, 80f, 95f, 110f, 125f, 140f, 155f,
                205f, 218f, 232f, 245f, 260f, 275f, 290f, 305f, 320f, 335f,
                348f, 5f, 15f, 170f
            };

            for (int i = 0; i < cityAngles.Length; i++)
            {
                float ang = cityAngles[i] * Mathf.Deg2Rad;
                float dist = 80f + ((i * 13) % 7) * 1.8f;
                float w = 8f + (i % 4) * 2.2f;
                float d = 8f + ((i + 2) % 4) * 2.2f;
                float h = 12f + ((i * 19) % 15) * 1.3f;

                Vector3 pos = new Vector3(Mathf.Sin(ang) * dist, h * 0.5f - 1.35f, Mathf.Cos(ang) * dist);
                Quaternion rot = Quaternion.Euler(0f, cityAngles[i], 0f);
                matteComb.AddBox(pos, new Vector3(w, h, d), cityColor, rot);
            }

            // 远景低多边形山丘 (8 座，半径 84-95m，深海岛绿灰)
            Color hillColor = new Color(0.18f, 0.25f, 0.22f, 1f);
            float[] hillAngles = { 45f, 75f, 105f, 135f, 225f, 255f, 285f, 315f };

            for (int k = 0; k < hillAngles.Length; k++)
            {
                float ang = hillAngles[k] * Mathf.Deg2Rad;
                float dist = 86f + (k % 3) * 3.5f;
                float baseR = 18f + (k % 4) * 2.5f;
                float hillH = 14f + (k % 3) * 3.0f;

                Vector3 center = new Vector3(Mathf.Sin(ang) * dist, -1.35f, Mathf.Cos(ang) * dist);
                matteComb.AddCone(center, baseR, 2.0f, hillH, 8, hillColor);
            }
        }
        #endregion

        #region 工具与网格合并辅助类
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

        /// <summary>
        /// 实体道具的碰撞 + 涂墨代理：一个朝向盒碰撞体 + 四面 <see cref="InkWallSurface"/>。
        /// 合并网格仍然负责渲染；代理盒只负责挡子弹/挡人和"打中时把墨涂在哪一面"。
        /// 尺寸取实体部件的局部包围盒（花坛的灌木、售卖亭的挑檐这类非实体件已排除）。
        /// </summary>
        private static void CreateInkPropProxy(
            Transform parent,
            ArenaLayoutV2.ArenaPropSpec prop,
            Quaternion rotation,
            Bounds localBounds,
            ArenaMaterialSet mats,
            InkGridConfigSO inkCfg,
            int serial)
        {
            var go = new GameObject($"PropCollider_{prop.Kind}_{serial:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = prop.Position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = localBounds.size;

            if (inkCfg != null && !inkCfg.enableWallInk)
            {
                return;
            }

            Vector3 centerWorld = prop.Position + rotation * localBounds.center;
            var wall = go.AddComponent<InkWallSurface>();
            // 售卖亭 2.4m 高、顶檐外挑，爬上去会挡住俯视角视线，禁止上爬；
            // 木箱 / 花坛保持可爬（与场上掩体一致）。
            wall.ConfigureOrientedBox(
                box,
                centerWorld,
                localBounds.size,
                rotation,
                mats != null ? mats.inkWall : null,
                inkCfg,
                allowClimb: prop.Kind != ArenaLayoutV2.ArenaPropKind.Kiosk);
        }

        private static GameObject CreateRendererChild(Transform parent, string name, Mesh mesh, Material mat, bool castShadows, bool addCollider = false)
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

            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;

            if (addCollider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }

            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>
        /// 风格解包辅助结构。
        /// </summary>
        private readonly struct BackdropPalette
        {
            public readonly Color ConcreteLight;
            public readonly Color ConcreteMid;
            public readonly Color ConcreteDark;
            public readonly Color Steel;
            public readonly Color SteelDark;
            public readonly Color HazardYellow;
            public readonly Color ContainerTeal;
            public readonly Color ContainerRust;
            public readonly Color ContainerSlate;
            public readonly Color ContainerMustard;

            public BackdropPalette(ArenaStyleSO s)
            {
                ConcreteLight = s != null ? s.concreteLight : new Color(0.78f, 0.76f, 0.70f, 1f);
                ConcreteMid = s != null ? s.concreteMid : new Color(0.62f, 0.60f, 0.55f, 1f);
                ConcreteDark = s != null ? s.concreteDark : new Color(0.40f, 0.39f, 0.36f, 1f);
                Steel = s != null ? s.steel : new Color(0.42f, 0.47f, 0.52f, 1f);
                SteelDark = s != null ? s.steelDark : new Color(0.24f, 0.27f, 0.30f, 1f);
                HazardYellow = s != null ? s.hazardYellow : new Color(0.86f, 0.68f, 0.20f, 1f);
                ContainerTeal = s != null ? s.containerTeal : new Color(0.25f, 0.52f, 0.50f, 1f);
                ContainerRust = s != null ? s.containerRust : new Color(0.62f, 0.33f, 0.24f, 1f);
                ContainerSlate = s != null ? s.containerSlate : new Color(0.37f, 0.46f, 0.57f, 1f);
                ContainerMustard = s != null ? s.containerMustard : new Color(0.79f, 0.60f, 0.24f, 1f);
            }
        }

        /// <summary>
        /// 多来源网格合并器，支持直接写入图元或变换追加子网格。
        /// </summary>
        private sealed class MeshCombiner
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(2048);
            public readonly List<Vector3> Normals = new List<Vector3>(2048);
            public readonly List<Vector2> UVs = new List<Vector2>(2048);
            public readonly List<Color> Colors = new List<Color>(2048);
            public readonly List<int> Triangles = new List<int>(3072);

            private static Color Linear(Color c)
            {
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            }

            public void AddMesh(Mesh source, Vector3 position, Quaternion rotation, Vector3 scale, Color tint)
            {
                if (source == null) return;
                int baseIndex = Vertices.Count;
                var srcVerts = source.vertices;
                var srcNorms = source.normals;
                var srcUvs = source.uv;
                var srcColors = source.colors;
                var srcTris = source.triangles;

                bool hasNorms = srcNorms != null && srcNorms.Length == srcVerts.Length;
                bool hasUvs = srcUvs != null && srcUvs.Length == srcVerts.Length;
                bool hasColors = srcColors != null && srcColors.Length == srcVerts.Length;

                Matrix4x4 trs = Matrix4x4.TRS(position, rotation, scale);
                Matrix4x4 normM = trs.inverse.transpose;

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    Vertices.Add(trs.MultiplyPoint3x4(srcVerts[i]));
                    Normals.Add(hasNorms ? normM.MultiplyVector(srcNorms[i]).normalized : Vector3.up);
                    UVs.Add(hasUvs ? srcUvs[i] : Vector2.zero);

                    Color c = hasColors ? srcColors[i] : Color.white;
                    Colors.Add(Linear(new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a)));
                }

                for (int i = 0; i < srcTris.Length; i++)
                {
                    Triangles.Add(baseIndex + srcTris[i]);
                }
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

                // +Y
                AddSide(new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.up);
                // -Y
                AddSide(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), Vector3.down);
                // +Z
                AddSide(new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), Vector3.forward);
                // -Z
                AddSide(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), Vector3.back);
                // +X
                AddSide(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), Vector3.right);
                // -X
                AddSide(new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), Vector3.left);
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
