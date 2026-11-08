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
    /// 竞技场场景装饰与港口远景构建器（P7-B / P30 视觉精修）：
    /// 负责按 ArenaLayoutV2 数据与 ArenaStyleSO 风格，生成合并网格资产并保存至磁盘，
    /// 并在场景中装配静态近景道具（4 材质合并 + 墙面横幅）与港口远景。
    /// 升级为三层纵深港口远景：大型黄色岸桥群（含 CARGO 标识、黄黑警示条、起吊姿态）、
    /// 三排进深集装箱堆场、货船（带甲板吊杆）、远景悬索桥剪影、海岛双层绿丘与海滨城市剪影。
    /// 完全兼容 URP 与 SRP Batcher，全场 draw call 受控。
    /// </summary>
    public static class ArenaDecorationBuilder
    {
        public const string MeshFolder = "Assets/_Project/Art/Meshes/Arena";

        private const string InkConfigPath = "Assets/_Project/ScriptableObjects/InkGridConfig.asset";

        #region 公开构建入口
        /// <summary>
        /// 构建场内地表道具与边界墙横幅，合并为 4 个材质子网格 + 1 个横幅网格。
        /// </summary>
        public static GameObject Build(
            Transform parent,
            ArenaStyleSO style,
            ArenaMaterialSet mats,
            List<Collider> blockedColliders = null,
            IReadOnlyList<ArenaLayoutV2.ArenaPropSpec> props = null,
            string meshPrefix = "arena_decor",
            bool includeInArenaDecor = true)
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
            var matteSolidBucket = new MeshCombiner();
            var metalSolidBucket = new MeshCombiner();
            var matteDecorBucket = new MeshCombiner();
            var metalDecorBucket = new MeshCombiner();
            var emissiveBucket = new MeshCombiner();
            var clothBucket = new MeshCombiner();

            var propParts = new List<ArenaPropMeshPart>(8);
            InkGridConfigSO inkCfg = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>(InkConfigPath);
            int proxySerial = 0;

            foreach (var prop in props ?? ArenaLayoutV2.Props)
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
                if (solid && hasSolidBounds)
                {
                    CreateInkPropProxy(decorRoot.transform, prop, rot, solidBounds, mats, inkCfg, proxySerial++, blockedColliders);
                }
            }

            var pal = new BackdropPalette(style);

            // 1.1 场内轻量纯装饰增强（贴墙配电箱、走线管、死角电缆盘、售卖亭圆凳与垃圾桶）
            // 全走 MeshCombiner，无 Collider、无 InkWallSurface，严守通道与坡道安全。
            if (includeInArenaDecor)
            {
                BuildInArenaDecorations(matteDecorBucket, metalDecorBucket, density, pal);
            }

            // 2. 生成并保存 6 个合并网格资产
            Mesh matteMesh = matteSolidBucket.BuildMesh(meshPrefix + "_matte");
            Mesh metalMesh = metalSolidBucket.BuildMesh(meshPrefix + "_metal");
            Mesh matteDecorMesh = matteDecorBucket.BuildMesh(meshPrefix + "_matte_thin");
            Mesh metalDecorMesh = metalDecorBucket.BuildMesh(meshPrefix + "_metal_thin");
            Mesh emissiveMesh = emissiveBucket.BuildMesh(meshPrefix + "_emissive");
            Mesh clothMesh = clothBucket.BuildMesh(meshPrefix + "_cloth");

            SaveAsset(matteMesh, $"{MeshFolder}/{meshPrefix}_matte.asset");
            SaveAsset(metalMesh, $"{MeshFolder}/{meshPrefix}_metal.asset");
            SaveAsset(matteDecorMesh, $"{MeshFolder}/{meshPrefix}_matte_thin.asset");
            SaveAsset(metalDecorMesh, $"{MeshFolder}/{meshPrefix}_metal_thin.asset");
            SaveAsset(emissiveMesh, $"{MeshFolder}/{meshPrefix}_emissive.asset");
            SaveAsset(clothMesh, $"{MeshFolder}/{meshPrefix}_cloth.asset");

            // 3. 创建子渲染对象 (近景装饰阴影开启；碰撞与涂墨由每个实体道具的盒代理负责)
            CreateRendererChild(decorRoot.transform, "Decor_Matte", matteMesh, mats?.propMatte ?? mats?.concrete, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Metal", metalMesh, mats?.propMetal ?? mats?.steel, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Matte_Thin", matteDecorMesh, mats?.propMatte ?? mats?.concrete, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Metal_Thin", metalDecorMesh, mats?.propMetal ?? mats?.steel, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Emissive", emissiveMesh, mats?.propEmissive ?? mats?.propMatte, true, false);
            CreateRendererChild(decorRoot.transform, "Decor_Cloth", clothMesh, mats?.propCloth ?? mats?.propMatte, true, false);

            // 4. 边界墙内侧图集横幅 (12 个 Banner，含折角造型与安装挂梁)
            //    港湾图关闭：图集内容（CARGO / DOCK / CRANE ZONE）与港湾主题冲突。
            if (style == null || style.buildWallBanners)
            {
                Mesh bannersMesh = BuildWallBannersMesh(metalDecorBucket, pal);
                SaveAsset(bannersMesh, $"{MeshFolder}/{meshPrefix}_banners.asset");
                CreateRendererChild(decorRoot.transform, "Banners", bannersMesh, mats?.bannerAtlas ?? mats?.propMatte, false);
            }

            return decorRoot;
        }

        /// <summary>
        /// 构建港口远景（码头基座、水面、集装箱堆场、岸桥、货船、悬索桥、城市绿丘剪影）。
        /// 无 Collider、无阴影投放与接收、静态合批。
        /// </summary>
        public static GameObject BuildBackdrop(Transform parent, ArenaStyleSO style, ArenaMaterialSet mats, string meshPrefix = "arena_backdrop")
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
            SaveAsset(waterMesh, $"{MeshFolder}/{meshPrefix}_water.asset");
            CreateRendererChild(backdropRoot.transform, "Backdrop_Water", waterMesh, mats?.water, false);

            // 2. 远景按 Matte / Metal 材质合并
            var matteBucket = new MeshCombiner();
            var metalBucket = new MeshCombiner();

            if (style != null && style.backdropTheme == ArenaBackdropTheme.Marina)
            {
                // 港湾主题：近景栈桥/游艇 + 对岸彩色小镇，不再生成岸桥与集装箱堆场。
                BuildMarinaScenery(matteBucket, metalBucket, palette);
                BuildMarinaTown(matteBucket, metalBucket, palette);
            }
            else
            {
                // 2.1 码头混凝土高台基座 (74 x 1.2 x 84m，边缘护舷、系船柱、登船梯)
                BuildPierBase(matteBucket, metalBucket, palette);

                // 2.2 场外集装箱堆场 (3 排进深层次，前排鲜艳，后排压暗，走廊分明)
                BuildContainerYards(matteBucket, metalBucket, palette);

                // 2.3 岸桥/大型集装箱龙门吊 (4 座黄色钢构桁架，高 25m，含 CARGO 标识、黄黑警示条、起吊集装箱)
                BuildGantryCranes(metalBucket, matteBucket, palette);

                // 2.4 仓库 4 座 + 货船 3 艘 (含甲板起重机与深色船体)
                BuildWarehousesAndShips(matteBucket, metalBucket, palette);

                // 2.5 远景悬索桥剪影 (跨度 190m，双塔 + 主缆 + 吊杆)
                BuildSuspensionBridge(matteBucket, metalBucket, palette);

                // 2.6 远景城市剪影 + 双层绿丘 + 灯塔 + 水塔 + 帆船
                BuildCityAndHills(matteBucket, metalBucket, palette);
            }

            // 3. 保存合并资产并创建子物体
            Mesh matteMesh = matteBucket.BuildMesh(meshPrefix + "_matte");
            Mesh metalMesh = metalBucket.BuildMesh(meshPrefix + "_metal");

            SaveAsset(matteMesh, $"{MeshFolder}/{meshPrefix}_matte.asset");
            SaveAsset(metalMesh, $"{MeshFolder}/{meshPrefix}_metal.asset");

            CreateRendererChild(backdropRoot.transform, "Backdrop_Matte", matteMesh, mats?.propMatte ?? mats?.concrete, false);
            CreateRendererChild(backdropRoot.transform, "Backdrop_Metal", metalMesh, mats?.propMetal ?? mats?.steel, false);

            return backdropRoot;
        }
        #endregion

        #region 边界墙横幅网格生成
        /// <summary>
        /// 在边界墙内侧生成 12 个横幅，分 4 个 UV 图集区域。
        /// 带有旗面底角折角造型与顶部金属挂杆。
        /// 距墙面只留 4mm：墨迹立面 Quad 在墙外 10mm 处，涂墨后墨会盖住横幅。
        /// </summary>
        private static Mesh BuildWallBannersMesh(MeshCombiner metalComb = null, BackdropPalette? pal = null)
        {
            var combiner = new MeshCombiner();
            float w = 5.2f;
            float h = 2.2f;
            float cy = 1.35f;
            const float bannerOffset = 0.004f;

            void PlaceBanner(Vector3 center, Vector3 normal, int atlasIndex)
            {
                AddBannerQuad(combiner, center, w, h, normal, atlasIndex);
                if (metalComb != null && pal.HasValue)
                {
                    AddBannerMountingRails(metalComb, center, w, h, normal, pal.Value);
                }
            }

            // 北墙内侧 (z=+28.0 - bannerOffset，朝 -Z，正常墙高 3.0m，4 面横幅富有韵律)
            float northZ = 28.0f - bannerOffset;
            PlaceBanner(new Vector3(-13.0f, cy, northZ), Vector3.back, 2);
            PlaceBanner(new Vector3(-5.0f,  cy, northZ), Vector3.back, 0);
            PlaceBanner(new Vector3(5.0f,   cy, northZ), Vector3.back, 1);
            PlaceBanner(new Vector3(13.0f,  cy, northZ), Vector3.back, 3);

            // 西墙内侧 (x=-20.0 + bannerOffset，朝 +X)
            float westX = -20.0f + bannerOffset;
            PlaceBanner(new Vector3(westX, cy, -18.0f), Vector3.right, 0);
            PlaceBanner(new Vector3(westX, cy, -6.0f),  Vector3.right, 1);
            PlaceBanner(new Vector3(westX, cy, 6.0f),   Vector3.right, 2);
            PlaceBanner(new Vector3(westX, cy, 18.0f),  Vector3.right, 3);

            // 东墙内侧 (x=+20.0 - bannerOffset，朝 -X)
            float eastX = 20.0f - bannerOffset;
            PlaceBanner(new Vector3(eastX, cy, -18.0f), Vector3.left, 2);
            PlaceBanner(new Vector3(eastX, cy, -6.0f),  Vector3.left, 3);
            PlaceBanner(new Vector3(eastX, cy, 6.0f),   Vector3.left, 0);
            PlaceBanner(new Vector3(eastX, cy, 18.0f),  Vector3.left, 1);

            // 南墙为矮围墙 (0.35m)，按契约跳过
            return combiner.BuildMesh("arena_banners");
        }

        private static void AddBannerMountingRails(MeshCombiner metalComb, Vector3 center, float width, float height, Vector3 normal, BackdropPalette pal)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 up = Vector3.up;

            Vector3 topCenter = center + up * (height * 0.5f + 0.04f);
            // 挂杆横梁
            metalComb.AddBox(topCenter, new Vector3(width + 0.35f, 0.07f, 0.05f), pal.SteelDark, Quaternion.LookRotation(normal));
            // 墙面固定扣
            Vector3 b1 = topCenter - right * (width * 0.45f) - normal * 0.02f;
            Vector3 b2 = topCenter + right * (width * 0.45f) - normal * 0.02f;
            metalComb.AddBox(b1, new Vector3(0.08f, 0.12f, 0.08f), pal.Steel, Quaternion.LookRotation(normal));
            metalComb.AddBox(b2, new Vector3(0.08f, 0.12f, 0.08f), pal.Steel, Quaternion.LookRotation(normal));
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

            // 右下角做折角切口 (35cm)
            float cut = 0.35f;
            float uCut = (cut / width) * 0.5f;
            float vCut = (cut / height) * 0.5f;

            Vector3 c0 = center - halfR - halfU;
            Vector3 v1a = center + halfR - halfU - right * cut;
            Vector3 v1b = center + halfR - halfU + up * cut;
            Vector3 v2 = center + halfR + halfU;
            Vector3 v3 = center - halfR + halfU;

            Vector2 uv0 = new Vector2(u0, v0);
            Vector2 uv1a = new Vector2(u1 - uCut, v0);
            Vector2 uv1b = new Vector2(u1, v0 + vCut);
            Vector2 uv2 = new Vector2(u1, v1);
            Vector2 uv3 = new Vector2(u0, v1);

            // 主体分为 2 个四边形平铺拼合，保持 UV 映射严格无拉伸
            Vector3 vTopCut = center + halfR + halfU - right * cut;
            Vector2 uvTopCut = new Vector2(u1 - uCut, v1);

            comb.AddQuadRaw(c0, v1a, vTopCut, v3, normal, uv0, uv1a, uvTopCut, uv3, Color.white);
            comb.AddQuadRaw(v1a, v1b, v2, vTopCut, normal, uv1a, uv1b, uv2, uvTopCut, Color.white);

            // 折角面：折向内侧稍凸起 6mm，带背面阴影色
            Vector3 vFoldApex = center + halfR - halfU - right * (cut * 0.3f) + up * (cut * 0.7f) + normal * 0.006f;
            Vector2 uvFoldApex = new Vector2(u1 - uCut * 0.5f, v0 + vCut * 0.5f);
            Color foldColor = new Color(0.72f, 0.72f, 0.72f, 1f);

            Vector3 foldNorm = (normal + right * 0.2f - up * 0.2f).normalized;
            int start = comb.Vertices.Count;
            comb.Vertices.Add(v1a);
            comb.Vertices.Add(v1b);
            comb.Vertices.Add(vFoldApex);
            comb.Normals.Add(foldNorm);
            comb.Normals.Add(foldNorm);
            comb.Normals.Add(foldNorm);
            comb.UVs.Add(uv1a);
            comb.UVs.Add(uv1b);
            comb.UVs.Add(uvFoldApex);

            Color linFold = QualitySettings.activeColorSpace == ColorSpace.Linear ? foldColor.linear : foldColor;
            comb.Colors.Add(linFold);
            comb.Colors.Add(linFold);
            comb.Colors.Add(linFold);

            comb.Triangles.Add(start);
            comb.Triangles.Add(start + 1);
            comb.Triangles.Add(start + 2);
        }
        #endregion

        #region 场内轻量纯装饰增强
        /// <summary>
        /// 场内轻量纯装饰：贴墙配电箱、金属走线管、死角电缆盘、售卖亭周边圆凳与分类垃圾桶。
        /// 无 Collider、无 InkWallSurface，严格贴墙/贴道具死角放置，绝不阻挡通道与坡道。
        /// </summary>
        private static void BuildInArenaDecorations(
            MeshCombiner matteComb,
            MeshCombiner metalComb,
            float density,
            BackdropPalette pal)
        {
            // 1. 贴墙配电箱与金属走线管 (6 处：西墙 2、东墙 2、北墙 2)
            // 贴附在边界墙内侧（y=1.25m，厚仅 0.14m，不占用行走主干，无法站立）
            var boxSpecs = new[]
            {
                new { pos = new Vector3(-19.92f, 1.25f, -12.0f), rot = Quaternion.Euler(0f, 90f, 0f) },
                new { pos = new Vector3(-19.92f, 1.25f, 12.0f),  rot = Quaternion.Euler(0f, 90f, 0f) },
                new { pos = new Vector3(19.92f, 1.25f, -12.0f),  rot = Quaternion.Euler(0f, -90f, 0f) },
                new { pos = new Vector3(19.92f, 1.25f, 12.0f),   rot = Quaternion.Euler(0f, -90f, 0f) },
                new { pos = new Vector3(-17.5f, 1.25f, 27.92f),  rot = Quaternion.Euler(0f, 180f, 0f) },
                new { pos = new Vector3(17.5f, 1.25f, 27.92f),   rot = Quaternion.Euler(0f, 180f, 0f) },
            };

            for (int i = 0; i < boxSpecs.Length; i++)
            {
                var spec = boxSpecs[i];
                // 混凝土/工业灰箱体
                matteComb.AddBox(spec.pos, new Vector3(0.44f, 0.65f, 0.14f), pal.ConcreteMid, spec.rot);
                // 箱门与把手
                metalComb.AddBox(spec.pos + spec.rot * new Vector3(0f, 0f, 0.08f), new Vector3(0.38f, 0.58f, 0.02f), pal.SteelDark, spec.rot);
                // 警示小黄标
                matteComb.AddBox(spec.pos + spec.rot * new Vector3(0f, 0.12f, 0.09f), new Vector3(0.12f, 0.12f, 0.01f), pal.HazardYellow, spec.rot);
                // 向上延伸至墙顶的镀锌导管
                float pipeLen = 2.85f - (spec.pos.y + 0.325f);
                Vector3 pipeCenter = spec.pos + spec.rot * new Vector3(0f, 0.325f + pipeLen * 0.5f, 0.04f);
                metalComb.AddBox(pipeCenter, new Vector3(0.04f, pipeLen, 0.04f), pal.Steel, spec.rot);
            }

            // 移动端低密度裁剪散件
            if (density < 0.99f) return;

            // 2. 木制电缆盘 (2 处，位于北墙阶梯侧翼与南墙角落死角)
            Vector3[] spoolPositions =
            {
                new Vector3(-18.5f, 0.45f, 25.5f),
                new Vector3(18.5f, 0.45f, -25.5f)
            };
            for (int i = 0; i < spoolPositions.Length; i++)
            {
                Vector3 sp = spoolPositions[i];
                // 两个大圆木盘法兰
                matteComb.AddCylinder(sp + new Vector3(-0.22f, 0f, 0f), 0.45f, 0.05f, 10, pal.Wood, Quaternion.Euler(0f, 0f, 90f));
                matteComb.AddCylinder(sp + new Vector3(0.18f, 0f, 0f), 0.45f, 0.05f, 10, pal.Wood, Quaternion.Euler(0f, 0f, 90f));
                // 中间缠线轴心
                matteComb.AddCylinder(sp + new Vector3(-0.17f, 0f, 0f), 0.22f, 0.35f, 8, pal.WoodDark, Quaternion.Euler(0f, 0f, 90f));
            }

            // 3. 售卖亭周边圆凳与垃圾桶 (Kiosk 位于 (-8.5, 0, -21.0) 与 (8.5, 0, 21.0))
            Vector3[] kioskOffsets =
            {
                new Vector3(-8.5f, 0f, -21.0f),
                new Vector3(8.5f, 0f, 21.0f)
            };
            for (int k = 0; k < kioskOffsets.Length; k++)
            {
                float sign = (k == 0) ? 1f : -1f;
                Vector3 kPos = kioskOffsets[k];

                // 圆凳 2 只 (贴售卖亭侧面)
                Vector3[] stoolLocs =
                {
                    kPos + new Vector3(sign * 1.4f, 0f, sign * 0.8f),
                    kPos + new Vector3(sign * 1.4f, 0f, sign * -0.6f)
                };
                for (int s = 0; s < stoolLocs.Length; s++)
                {
                    Vector3 st = stoolLocs[s];
                    // 金属底盘 + 立柱
                    metalComb.AddCylinder(st, 0.18f, 0.03f, 8, pal.SteelDark);
                    metalComb.AddCylinder(st, 0.04f, 0.40f, 6, pal.Steel);
                    // 软垫座面 (高 0.45m，不可作为攀爬阶梯)
                    matteComb.AddCylinder(st + new Vector3(0f, 0.40f, 0f), 0.20f, 0.08f, 10, pal.ContainerMustard);
                }

                // 小型分类垃圾桶 (置于售卖亭后角)
                Vector3 binPos = kPos + new Vector3(sign * -1.3f, 0f, sign * -0.7f);
                metalComb.AddCylinder(binPos, 0.22f, 0.60f, 8, pal.SteelDark);
                matteComb.AddBox(binPos + new Vector3(0f, 0.61f, 0f), new Vector3(0.46f, 0.06f, 0.46f), pal.ContainerTeal);
            }
        }
        #endregion

        #region 远景各模块生成
        /// <summary>
        /// 码头基座：74x1.2x84m 混凝土基座，顶面 y=-0.15，边缘护舷、系船柱与登船梯。
        /// </summary>
        private static void BuildPierBase(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            // 主码头大板
            Vector3 sz = new Vector3(74f, 1.2f, 84f);
            Vector3 center = new Vector3(0f, -0.75f, 2f);
            matteComb.AddBox(center, sz, pal.ConcreteDark);

            // 边缘混凝土压顶 (沿四周边缘凸出)
            float halfX = sz.x * 0.5f;
            float halfZ = sz.z * 0.5f;
            float topY = -0.15f;
            float curbH = 0.12f;
            float curbW = 0.65f;

            matteComb.AddBox(new Vector3(0f, topY + curbH * 0.5f, center.z - halfZ + curbW * 0.5f), new Vector3(sz.x, curbH, curbW), pal.ConcreteMid);
            matteComb.AddBox(new Vector3(0f, topY + curbH * 0.5f, center.z + halfZ - curbW * 0.5f), new Vector3(sz.x, curbH, curbW), pal.ConcreteMid);
            matteComb.AddBox(new Vector3(-halfX + curbW * 0.5f, topY + curbH * 0.5f, center.z), new Vector3(curbW, curbH, sz.z), pal.ConcreteMid);
            matteComb.AddBox(new Vector3(halfX - curbW * 0.5f, topY + curbH * 0.5f, center.z), new Vector3(curbW, curbH, sz.z), pal.ConcreteMid);

            // 码头边缘橡胶护舷 (Rubber Arch Fenders)
            Vector3 fenderSz = new Vector3(0.65f, 0.85f, 0.45f);
            Color fenderCol = pal.HazardDark;

            // 南侧护舷
            for (float x = -32f; x <= 32f; x += 6.4f)
            {
                matteComb.AddBox(new Vector3(x, -0.70f, center.z - halfZ - fenderSz.z * 0.4f), fenderSz, fenderCol);
            }
            // 北侧护舷
            for (float x = -32f; x <= 32f; x += 6.4f)
            {
                matteComb.AddBox(new Vector3(x, -0.70f, center.z + halfZ + fenderSz.z * 0.4f), fenderSz, fenderCol);
            }
            // 东西侧泊位护舷
            Vector3 fenderSideSz = new Vector3(0.45f, 0.85f, 0.65f);
            for (float z = -36f; z <= 40f; z += 7.0f)
            {
                matteComb.AddBox(new Vector3(-halfX - fenderSideSz.x * 0.4f, -0.70f, z), fenderSideSz, fenderCol);
                matteComb.AddBox(new Vector3(halfX + fenderSideSz.x * 0.4f, -0.70f, z), fenderSideSz, fenderCol);
            }

            // 码头边缘系船柱 (Bollards)
            float bollardR = 0.18f;
            float bollardH = 0.55f;
            float[] bollardX = { -30f, -18f, -6f, 6f, 18f, 30f };
            for (int i = 0; i < bollardX.Length; i++)
            {
                Vector3 bSouth = new Vector3(bollardX[i], topY, center.z - halfZ + curbW * 0.5f);
                metalComb.AddCylinder(bSouth, bollardR, bollardH, 8, pal.SteelDark);
                metalComb.AddBox(bSouth + new Vector3(0f, bollardH * 0.8f, 0f), new Vector3(0.55f, 0.10f, 0.18f), pal.SteelDark);

                Vector3 bNorth = new Vector3(bollardX[i], topY, center.z + halfZ - curbW * 0.5f);
                metalComb.AddCylinder(bNorth, bollardR, bollardH, 8, pal.SteelDark);
                metalComb.AddBox(bNorth + new Vector3(0f, bollardH * 0.8f, 0f), new Vector3(0.55f, 0.10f, 0.18f), pal.SteelDark);
            }

            float[] bollardZ = { -32f, -18f, -4f, 10f, 24f, 38f };
            for (int i = 0; i < bollardZ.Length; i++)
            {
                Vector3 bWest = new Vector3(-halfX + curbW * 0.5f, topY, bollardZ[i]);
                metalComb.AddCylinder(bWest, bollardR, bollardH, 8, pal.SteelDark);
                metalComb.AddBox(bWest + new Vector3(0f, bollardH * 0.8f, 0f), new Vector3(0.18f, 0.10f, 0.55f), pal.SteelDark);

                Vector3 bEast = new Vector3(halfX - curbW * 0.5f, topY, bollardZ[i]);
                metalComb.AddCylinder(bEast, bollardR, bollardH, 8, pal.SteelDark);
                metalComb.AddBox(bEast + new Vector3(0f, bollardH * 0.8f, 0f), new Vector3(0.18f, 0.10f, 0.55f), pal.SteelDark);
            }

            // 登船梯斜桥 (Gangway Ramp，连接西侧码头到西侧货船登船口)
            Vector3 gangwayStart = new Vector3(-halfX, topY + curbH, 4.0f);
            Vector3 gangwayEnd = new Vector3(-halfX - 7.5f, 1.20f, 4.0f);
            Vector3 gMid = (gangwayStart + gangwayEnd) * 0.5f;
            float gLen = Vector3.Distance(gangwayStart, gangwayEnd);
            float gAngle = Mathf.Atan2(gangwayEnd.y - gangwayStart.y, gangwayStart.x - gangwayEnd.x) * Mathf.Rad2Deg;
            Quaternion gRot = Quaternion.Euler(0f, 0f, -gAngle);

            metalComb.AddBox(gMid, new Vector3(gLen, 0.12f, 1.3f), pal.Steel, gRot);
            metalComb.AddBox(gMid + Vector3.up * 0.55f + Vector3.forward * 0.65f, new Vector3(gLen, 0.08f, 0.08f), pal.HazardYellow, gRot);
            metalComb.AddBox(gMid + Vector3.up * 0.55f + Vector3.back * 0.65f, new Vector3(gLen, 0.08f, 0.08f), pal.HazardYellow, gRot);
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
        /// 场外集装箱堆场 (3 排进深层次，前排鲜艳，后排压暗派生色，留出通道感)。
        /// </summary>
        private static void BuildContainerYards(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            Color[] colorsFront = { pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerSlate };
            Vector3 cSz = new Vector3(2.44f, 2.59f, 6.06f);

            void AddContainerBox(Vector3 pos, Color col, Quaternion rot, bool corrugated)
            {
                if (corrugated)
                {
                    // 近排箱体补竖向瓦楞 + 角件：远景里最抢眼的就是这一圈，
                    // 纯色块在斜俯视角下会读成"贴了张彩纸的平板"。
                    AddCorrugatedBackdropContainer(matteComb, metalComb, pos, cSz, col, rot, pal);
                    return;
                }

                matteComb.AddBox(pos, cSz, col, rot);
                // 4 个角柱/角件加深金属描边感
                float hx = cSz.x * 0.5f;
                float hy = cSz.y;
                float hz = cSz.z * 0.5f;
                Vector3 postSz = new Vector3(0.10f, hy, 0.10f);
                Color postCol = Color.Lerp(col, pal.SteelDark, 0.40f);
                metalComb.AddBox(pos + rot * new Vector3(-hx, 0f, -hz), postSz, postCol, rot);
                metalComb.AddBox(pos + rot * new Vector3(hx, 0f, -hz), postSz, postCol, rot);
                metalComb.AddBox(pos + rot * new Vector3(-hx, 0f, hz), postSz, postCol, rot);
                metalComb.AddBox(pos + rot * new Vector3(hx, 0f, hz), postSz, postCol, rot);
            }

            void AddStack(Vector3 basePos, float yaw, int tiers, int colOffset, bool isBackRow, bool corrugated)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                for (int t = 0; t < tiers; t++)
                {
                    Vector3 localOffset = new Vector3(t * 0.04f, t * cSz.y + cSz.y * 0.5f - 0.15f, 0f);
                    Vector3 worldPos = basePos + rot * localOffset;
                    Color baseCol = colorsFront[(t + colOffset) % colorsFront.Length];
                    Color finalCol = isBackRow ? Color.Lerp(baseCol, pal.SteelDark, 0.35f) : baseCol;
                    // 只有最下层做瓦楞：上层箱体被前层遮住大半，做全瓦楞等于白烧顶点。
                    AddContainerBox(worldPos, finalCol, rot, corrugated && t == 0);
                }
            }

            // ---- 西侧外场三排进深堆场 ----
            // 第一排 (前排，近墙 |x|=25.5m，鲜艳原色)
            float[] westZ1 = { -26f, -19f, -12f, -5f, 2f, 9f, 16f, 23f };
            for (int i = 0; i < westZ1.Length; i++)
            {
                int tiers = (i % 2) + 1;
                AddStack(new Vector3(-25.5f, 0f, westZ1[i]), (i % 2 == 0 ? 0.5f : -0.5f), tiers, i, false, true);
            }
            // 第二排 (中排，|x|=30.8m，2-3层)
            float[] westZ2 = { -23f, -16f, -9f, -2f, 5f, 12f, 19f };
            for (int i = 0; i < westZ2.Length; i++)
            {
                int tiers = (i % 2 == 0) ? 3 : 2;
                AddStack(new Vector3(-30.8f, 0f, westZ2[i]), (i % 3 - 1) * 1.2f, tiers, i + 1, false, false);
            }
            // 第三排 (后排，|x|=36.0m，压暗派生色，强化进深)
            float[] westZ3 = { -25f, -18f, -11f, -4f, 3f, 10f, 17f, 24f };
            for (int i = 0; i < westZ3.Length; i++)
            {
                int tiers = (i % 3 == 1) ? 3 : 2;
                AddStack(new Vector3(-36.0f, 0f, westZ3[i]), 0f, tiers, i + 2, true, false);
            }

            // ---- 东侧外场三排进深堆场 ----
            // 第一排 (前排，|x|=25.5m)
            float[] eastZ1 = { -26f, -19f, -12f, -5f, 2f, 9f, 16f, 23f };
            for (int i = 0; i < eastZ1.Length; i++)
            {
                int tiers = ((i + 1) % 2) + 1;
                AddStack(new Vector3(25.5f, 0f, eastZ1[i]), (i % 2 == 0 ? -0.5f : 0.5f), tiers, i + 2, false, true);
            }
            // 第二排 (中排，|x|=30.8m)
            float[] eastZ2 = { -23f, -16f, -9f, -2f, 5f, 12f, 19f };
            for (int i = 0; i < eastZ2.Length; i++)
            {
                int tiers = (i % 2 == 1) ? 3 : 2;
                AddStack(new Vector3(30.8f, 0f, eastZ2[i]), (1 - i % 3) * 1.2f, tiers, i + 3, false, false);
            }
            // 第三排 (后排，|x|=36.0m，压暗派生色)
            float[] eastZ3 = { -25f, -18f, -11f, -4f, 3f, 10f, 17f, 24f };
            for (int i = 0; i < eastZ3.Length; i++)
            {
                int tiers = (i % 3 == 0) ? 3 : 2;
                AddStack(new Vector3(36.0f, 0f, eastZ3[i]), 0f, tiers, i, true, false);
            }

            // ---- 北侧延展区 (北墙外前景堆叠，填补北向地平线) ----
            AddStack(new Vector3(-13.5f, 0f, 33.5f), 90f, 2, 0, false, true);
            AddStack(new Vector3(-7.5f,  0f, 34.0f), 88f, 3, 2, false, true);
            AddStack(new Vector3(7.5f,   0f, 34.0f), -88f, 3, 1, false, true);
            AddStack(new Vector3(13.5f,  0f, 33.5f), -90f, 2, 3, false, true);

            // 堆场过道木质栈板/杂物箱散落 (增加生活气息)
            Vector3[] palletLocs =
            {
                new Vector3(-28.2f, -0.15f, -10f),
                new Vector3(-28.2f, -0.15f, 8f),
                new Vector3(28.2f, -0.15f, -8f),
                new Vector3(28.2f, -0.15f, 12f),
            };
            for (int p = 0; p < palletLocs.Length; p++)
            {
                matteComb.AddBox(palletLocs[p] + new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.24f, 1.4f), pal.Wood);
                matteComb.AddBox(palletLocs[p] + new Vector3(0.1f, 0.55f, 0.1f), new Vector3(1.1f, 0.60f, 1.0f), pal.WoodDark);
            }

            // 堆场里的油桶与锥桶（远景"有人干活"的痕迹，纯色小件足够，不抢近景）
            Vector3[] yardBarrels =
            {
                new Vector3(-27.4f, -0.15f, -14.2f), new Vector3(-27.4f, -0.15f, 12.4f),
                new Vector3(27.4f, -0.15f, -4.6f),   new Vector3(27.4f, -0.15f, 16.8f),
                new Vector3(-24.0f, -0.15f, 19.6f),  new Vector3(24.0f, -0.15f, -20.4f),
            };
            for (int i = 0; i < yardBarrels.Length; i++)
            {
                Color c = (i % 2 == 0) ? pal.ContainerRust : pal.ContainerTeal;
                matteComb.AddCylinder(yardBarrels[i], 0.29f, 0.88f, 8, c);
                metalComb.AddCylinder(yardBarrels[i] + new Vector3(0f, 0.22f, 0f), 0.30f, 0.05f, 8,
                    Color.Lerp(c, pal.SteelDark, 0.5f));
            }
        }

        /// <summary>
        /// 远景近排集装箱：主体 + 两侧长面/两端短面竖向瓦楞 + 8 个角件 + 顶底边梁。
        /// 肋距比场内粗（0.62m），远景看不出差别，但顶点数只有场内箱体的一小部分。
        /// </summary>
        private static void AddCorrugatedBackdropContainer(
            MeshCombiner matteComb, MeshCombiner metalComb,
            Vector3 pos, Vector3 size, Color color, Quaternion rot, BackdropPalette pal)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            // 主体略微内收，让瓦楞棱脊正好落在标称面上（与场内瓦楞壳同一手法）
            const float valley = 0.055f;
            matteComb.AddBox(pos, new Vector3(size.x - valley * 2f, size.y, size.z - valley * 2f), color, rot);

            Color ribColor = Color.Lerp(color, pal.SteelDark, 0.18f);
            const float pitch = 0.62f;

            // 两侧长面（法线 ±X）竖向瓦楞
            int ribs = Mathf.Max(2, Mathf.RoundToInt(size.z / pitch));
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < ribs; i++)
                {
                    float z = -hz + (i + 0.5f) * (size.z / ribs);
                    Vector3 p = pos + rot * new Vector3(side * (hx - valley * 0.5f), 0f, z);
                    matteComb.AddBox(p, new Vector3(valley, size.y - 0.30f, size.z / ribs * 0.55f), ribColor, rot);
                }
            }

            // 两端短面（法线 ±Z）竖向瓦楞
            int ribsEnd = Mathf.Max(2, Mathf.RoundToInt(size.x / pitch));
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < ribsEnd; i++)
                {
                    float x = -hx + (i + 0.5f) * (size.x / ribsEnd);
                    Vector3 p = pos + rot * new Vector3(x, 0f, side * (hz - valley * 0.5f));
                    matteComb.AddBox(p, new Vector3(size.x / ribsEnd * 0.55f, size.y - 0.30f, valley), ribColor, rot);
                }
            }

            // 8 个角件（海运箱最识别的特征）
            Color cornerCol = Color.Lerp(color, pal.SteelDark, 0.55f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 p = pos + rot * new Vector3(sx * (hx - 0.06f), sy * (hy - 0.05f), sz * (hz - 0.06f));
                        metalComb.AddBox(p, new Vector3(0.20f, 0.16f, 0.20f), cornerCol, rot);
                    }
                }
            }

            // 顶底边梁
            for (int sy = -1; sy <= 1; sy += 2)
            {
                metalComb.AddBox(pos + rot * new Vector3(0f, sy * (hy - 0.03f), 0f),
                    new Vector3(size.x + 0.02f, 0.07f, size.z + 0.02f), cornerCol, rot);
            }
        }

        /// <summary>
        /// 岸桥/大型集装箱龙门吊 (4 座黄色钢构桁架，高 25m，含 CARGO 标识、支脚黄黑警示条、起吊集装箱姿态)。
        /// </summary>
        private static void BuildGantryCranes(MeshCombiner metalComb, MeshCombiner matteComb, BackdropPalette pal)
        {
            var craneSpecs = new[]
            {
                new { pos = new Vector3(-18f, -0.15f, -38f), yaw = 180f, hoistContainer = true },
                new { pos = new Vector3(18f, -0.15f, -38f),  yaw = 180f, hoistContainer = false },
                new { pos = new Vector3(-20f, -0.15f, 40f),  yaw = 0f,   hoistContainer = false },
                new { pos = new Vector3(22f,  -0.15f, 40f),  yaw = 0f,   hoistContainer = false }
            };

            for (int k = 0; k < craneSpecs.Length; k++)
            {
                var spec = craneSpecs[k];
                Vector3 origin = spec.pos;
                Quaternion rot = Quaternion.Euler(0f, spec.yaw, 0f);

                float spanX = 8.0f;
                float spanZ = 9.0f;
                float portalH = 13.5f;

                // 1. 支脚台车 (4 组支腿底盘，带黄黑斜向警示条)
                Vector3[] footOffsets =
                {
                    new Vector3(-spanX * 0.5f, 0.5f, -spanZ * 0.5f),
                    new Vector3(spanX * 0.5f,  0.5f, -spanZ * 0.5f),
                    new Vector3(-spanX * 0.5f, 0.5f, spanZ * 0.5f),
                    new Vector3(spanX * 0.5f,  0.5f, spanZ * 0.5f),
                };

                Vector3 bogieSz = new Vector3(1.1f, 1.0f, 3.4f);
                for (int f = 0; f < footOffsets.Length; f++)
                {
                    metalComb.AddHazardBogie(origin + rot * footOffsets[f], bogieSz, pal.HazardYellow, pal.HazardDark, rot);
                }

                // 2. 门架立柱 (4 根高强度倾斜钢柱)
                float legLen = portalH - 1.0f;
                Vector3 legSz = new Vector3(0.85f, legLen, 0.85f);
                for (int f = 0; f < footOffsets.Length; f++)
                {
                    Vector3 legCenter = footOffsets[f] + new Vector3(0f, legLen * 0.5f + 0.5f, 0f);
                    metalComb.AddBox(origin + rot * legCenter, legSz, pal.HazardYellow, rot);
                }

                // 3. 门架横梁与侧翼 X 撑桁架
                metalComb.AddBox(origin + rot * new Vector3(0f, portalH, -spanZ * 0.5f), new Vector3(spanX + 0.8f, 1.1f, 1.0f), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(0f, portalH, spanZ * 0.5f), new Vector3(spanX + 0.8f, 1.1f, 1.0f), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH, 0f), new Vector3(1.0f, 1.1f, spanZ), pal.HazardYellow, rot);
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f, portalH, 0f), new Vector3(1.0f, 1.1f, spanZ), pal.HazardYellow, rot);

                Vector3 braceSz = new Vector3(0.35f, 0.35f, spanZ * 1.25f);
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, pal.HazardYellow, rot * Quaternion.Euler(38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, pal.HazardYellow, rot * Quaternion.Euler(-38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, pal.HazardYellow, rot * Quaternion.Euler(38f, 0f, 0f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.5f, portalH * 0.5f + 0.5f, 0f), braceSz, pal.HazardYellow, rot * Quaternion.Euler(-38f, 0f, 0f));

                // 4. 顶部 A 字塔架 (从 y=13.5m 升至 25m)
                float aFrameH = 11.5f;
                metalComb.AddBox(origin + rot * new Vector3(-spanX * 0.3f, portalH + aFrameH * 0.5f, 0f), new Vector3(0.7f, aFrameH, 0.7f), pal.HazardYellow, rot * Quaternion.Euler(0f, 0f, 7f));
                metalComb.AddBox(origin + rot * new Vector3(spanX * 0.3f, portalH + aFrameH * 0.5f, 0f), new Vector3(0.7f, aFrameH, 0.7f), pal.HazardYellow, rot * Quaternion.Euler(0f, 0f, -7f));
                metalComb.AddBox(origin + rot * new Vector3(0f, portalH + aFrameH, 0f), new Vector3(spanX * 0.75f, 0.9f, 1.4f), pal.HazardYellow, rot);

                // 5. 主悬臂走台梁 (长 32m，伸向外海与堆场)
                float boomLength = 32f;
                float boomZOffset = 2.0f;
                float boomY = 15.5f;
                Vector3 boomCenter = origin + rot * new Vector3(0f, boomY, boomZOffset);
                metalComb.AddBox(boomCenter, new Vector3(2.4f, 2.0f, boomLength), pal.HazardYellow, rot);

                // 梁顶安全护栏
                metalComb.AddBox(origin + rot * new Vector3(-1.1f, boomY + 1.3f, boomZOffset), new Vector3(0.12f, 0.7f, boomLength), pal.SteelDark, rot);
                metalComb.AddBox(origin + rot * new Vector3(1.1f, boomY + 1.3f, boomZOffset), new Vector3(0.12f, 0.7f, boomLength), pal.SteelDark, rot);

                // 塔顶至主梁的前后斜拉索 (Tension Tie Rods)
                Vector3 apexTop = origin + rot * new Vector3(0f, portalH + aFrameH, 0f);
                Vector3 stayFront = origin + rot * new Vector3(0f, boomY + 1.0f, boomZOffset + 12f);
                Vector3 stayBack = origin + rot * new Vector3(0f, boomY + 1.0f, boomZOffset - 12f);
                AddStayRod(metalComb, apexTop, stayFront, 0.12f, pal.SteelDark);
                AddStayRod(metalComb, apexTop, stayBack, 0.12f, pal.SteelDark);

                // 6. 梁顶重载卷扬机房与操作舱 (白浅灰 + CARGO 醒目标识)
                Vector3 cabPos = origin + rot * new Vector3(0f, boomY + 2.3f, -4.5f);
                matteComb.AddBox(cabPos, new Vector3(3.6f, 2.6f, 5.8f), pal.ConcreteLight, rot);
                matteComb.AddBox(cabPos + new Vector3(0f, 1.4f, 0f), new Vector3(3.8f, 0.25f, 6.0f), pal.SteelDark, rot);

                // 左右两侧醒目 CARGO 几何大字
                matteComb.AddWordCargo(cabPos + rot * new Vector3(1.85f, 0.1f, 0f), 1.1f, pal.HazardDark, rot * Quaternion.Euler(0f, 90f, 0f));
                matteComb.AddWordCargo(cabPos + rot * new Vector3(-1.85f, 0.1f, 0f), 1.1f, pal.HazardDark, rot * Quaternion.Euler(0f, -90f, 0f));

                // 7. 下挂小车、吊具缆绳与集装箱起吊
                float trolleyZ = spec.hoistContainer ? (boomZOffset + 9f) : (boomZOffset + 2f);
                Vector3 trolleyPos = origin + rot * new Vector3(0f, boomY - 1.25f, trolleyZ);
                metalComb.AddBox(trolleyPos, new Vector3(2.4f, 0.6f, 3.0f), pal.SteelDark, rot);

                float cableLen = spec.hoistContainer ? 6.5f : 3.0f;
                float spreaderY = trolleyPos.y - cableLen;
                Vector3 spreaderPos = origin + rot * new Vector3(0f, spreaderY, trolleyZ);

                // 4 根垂直悬挂钢缆
                float cOffX = 0.9f;
                float cOffZ = 1.1f;
                Vector3[] cablePts =
                {
                    new Vector3(-cOffX, trolleyPos.y - cableLen * 0.5f, trolleyZ - cOffZ),
                    new Vector3(cOffX,  trolleyPos.y - cableLen * 0.5f, trolleyZ - cOffZ),
                    new Vector3(-cOffX, trolleyPos.y - cableLen * 0.5f, trolleyZ + cOffZ),
                    new Vector3(cOffX,  trolleyPos.y - cableLen * 0.5f, trolleyZ + cOffZ),
                };
                for (int c = 0; c < cablePts.Length; c++)
                {
                    metalComb.AddBox(origin + rot * cablePts[c], new Vector3(0.06f, cableLen, 0.06f), pal.SteelDark, rot);
                }

                // 吊具框架 (Spreader)
                metalComb.AddBox(spreaderPos, new Vector3(2.5f, 0.45f, 6.2f), pal.SteelDark, rot);

                // 悬空吊起的集装箱（主角姿态）
                if (spec.hoistContainer)
                {
                    Vector3 containerPos = spreaderPos - new Vector3(0f, 1.45f, 0f);
                    Vector3 contSz = new Vector3(2.44f, 2.59f, 6.06f);
                    matteComb.AddBox(containerPos, contSz, pal.ContainerRust, rot);
                    // 角柱细节
                    Color postCol = Color.Lerp(pal.ContainerRust, pal.SteelDark, 0.4f);
                    metalComb.AddBox(containerPos + rot * new Vector3(-1.22f, 0f, -3.03f), new Vector3(0.12f, 2.6f, 0.12f), postCol, rot);
                    metalComb.AddBox(containerPos + rot * new Vector3(1.22f,  0f, -3.03f), new Vector3(0.12f, 2.6f, 0.12f), postCol, rot);
                    metalComb.AddBox(containerPos + rot * new Vector3(-1.22f, 0f, 3.03f),  new Vector3(0.12f, 2.6f, 0.12f), postCol, rot);
                    metalComb.AddBox(containerPos + rot * new Vector3(1.22f,  0f, 3.03f),  new Vector3(0.12f, 2.6f, 0.12f), postCol, rot);
                }
            }
        }

        private static void AddStayRod(MeshCombiner comb, Vector3 p0, Vector3 p1, float thickness, Color color)
        {
            Vector3 mid = (p0 + p1) * 0.5f;
            float len = Vector3.Distance(p0, p1);
            if (len < 0.001f) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, (p1 - p0).normalized);
            comb.AddBox(mid, new Vector3(thickness, len, thickness), color, rot);
        }

        /// <summary>
        /// 港口仓库 4 座 + 货船 3 艘 (深色船体、甲板集装箱垛、驾驶楼、甲板起重吊杆)。
        /// </summary>
        private static void BuildWarehousesAndShips(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            // 4 座港口货运仓库 (移至两侧堆场后方外沿)
            Vector3[] whPos =
            {
                new Vector3(-43f, -0.15f, -18f),
                new Vector3(-43f, -0.15f, 18f),
                new Vector3(43f,  -0.15f, -18f),
                new Vector3(43f,  -0.15f, 18f),
            };

            for (int i = 0; i < whPos.Length; i++)
            {
                Vector3 p = whPos[i];
                float w = 12f;
                float wallH = 6.5f;
                float len = 22f;

                matteComb.AddBox(p + Vector3.up * (wallH * 0.5f), new Vector3(w, wallH, len), pal.ConcreteMid);
                metalComb.AddBox(p + Vector3.up * (wallH + 1.2f), new Vector3(w * 1.06f, 1.8f, len * 1.02f), pal.Steel);
                matteComb.AddBox(p + Vector3.up * 2.2f + (i < 2 ? Vector3.right : Vector3.left) * (w * 0.505f),
                    new Vector3(0.1f, 4.2f, 6.0f), pal.SteelDark);
            }

            // 货船 1：西侧大型集装箱货船（停泊在西侧码头，带甲板起重吊杆）
            BuildMooredCargoShip(
                matteComb, metalComb, pal,
                origin: new Vector3(-53f, -1.35f, 2f),
                yaw: 0f,
                shipL: 66f, shipW: 13f, hullH: 5.2f,
                hullColor: pal.SteelDark,
                hasDeckCrane: true);

            // 货船 2：东侧深铁锈色货船（停泊在东侧码头）
            BuildMooredCargoShip(
                matteComb, metalComb, pal,
                origin: new Vector3(53f, -1.35f, -6f),
                yaw: 180f,
                shipL: 60f, shipW: 12f, hullH: 5.0f,
                hullColor: Color.Lerp(pal.ContainerRust, pal.SteelDark, 0.45f),
                hasDeckCrane: false);

            // 货船 3：北侧海湾远洋支线集装箱船（航行在海湾中，直接填补北向眺望海面）
            BuildMooredCargoShip(
                matteComb, metalComb, pal,
                origin: new Vector3(-14f, -1.35f, 85f),
                yaw: 80f,
                shipL: 50f, shipW: 11f, hullH: 4.5f,
                hullColor: Color.Lerp(pal.Steel, pal.SteelDark, 0.45f),
                hasDeckCrane: false);
        }

        private static void BuildMooredCargoShip(
            MeshCombiner matteComb,
            MeshCombiner metalComb,
            BackdropPalette pal,
            Vector3 origin,
            float yaw,
            float shipL,
            float shipW,
            float hullH,
            Color hullColor,
            bool hasDeckCrane)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            // 1. 船体下半部防污漆吃水线 (深铁锈红带)
            Color waterLineCol = Color.Lerp(pal.ContainerRust, pal.SteelDark, 0.40f);
            matteComb.AddBox(origin + rot * new Vector3(0f, hullH * 0.25f, 0f), new Vector3(shipW * 0.95f, hullH * 0.5f, shipL * 0.72f), waterLineCol, rot);
            matteComb.AddBox(origin + rot * new Vector3(0f, hullH * 0.25f, shipL * 0.42f), new Vector3(shipW * 0.65f, hullH * 0.5f, shipL * 0.26f), waterLineCol, rot);

            // 2. 主船体 (平直中段 + 尖削船首)
            matteComb.AddBox(origin + rot * new Vector3(0f, hullH * 0.75f, 0f), new Vector3(shipW, hullH * 0.5f, shipL * 0.72f), hullColor, rot);
            matteComb.AddBox(origin + rot * new Vector3(0f, hullH * 0.75f, shipL * 0.42f), new Vector3(shipW * 0.70f, hullH * 0.5f, shipL * 0.26f), hullColor, rot);
            metalComb.AddBox(origin + rot * new Vector3(0f, hullH * 0.4f, shipL * 0.54f), new Vector3(shipW * 0.35f, hullH * 0.45f, shipL * 0.08f), hullColor, rot);

            // 3. 船尾驾驶楼 (白浅灰 4 层)
            float bridgeZ = -shipL * 0.27f;
            Vector3 bridgePos = origin + rot * new Vector3(0f, hullH + 4.5f, bridgeZ);
            matteComb.AddBox(bridgePos, new Vector3(shipW * 0.82f, 8.5f, 8.0f), pal.ConcreteLight, rot);

            // 驾驶台飞桥
            metalComb.AddBox(bridgePos + new Vector3(0f, 3.2f, 0f), new Vector3(shipW * 1.05f, 0.8f, 3.0f), pal.ConcreteLight, rot);

            // 驾驶楼烟囱
            Vector3 funnelPos = origin + rot * new Vector3(0f, hullH + 10.2f, bridgeZ - 3.2f);
            matteComb.AddBox(funnelPos, new Vector3(2.4f, 4.2f, 3.0f), pal.SteelDark, rot);
            matteComb.AddBox(funnelPos + new Vector3(0f, 0.6f, 0f), new Vector3(2.45f, 1.2f, 3.05f), pal.ContainerMustard, rot);

            // 雷达桅杆
            metalComb.AddBox(bridgePos + new Vector3(0f, 6.2f, 0f), new Vector3(0.35f, 4.5f, 0.35f), pal.Steel, rot);
            metalComb.AddBox(bridgePos + new Vector3(0f, 7.8f, 0f), new Vector3(2.6f, 0.15f, 0.15f), pal.Steel, rot);

            // 4. 甲板集装箱垛 (3 个货舱贝位，色彩丰富)
            Color[] cCols = { pal.ContainerTeal, pal.ContainerRust, pal.ContainerMustard, pal.ContainerSlate };
            Vector3 cSz = new Vector3(2.44f, 2.59f, 6.06f);
            for (int row = -1; row <= 1; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    int maxTier = (col == 3) ? 1 : 2;
                    for (int tier = 0; tier < maxTier; tier++)
                    {
                        Vector3 boxPos = origin + rot * new Vector3(row * 2.7f, hullH + tier * 2.6f + 1.3f, -5f + col * 6.6f);
                        Color cCol = cCols[(row + col + tier + 7) % cCols.Length];
                        matteComb.AddBox(boxPos, cSz, cCol, rot);
                    }
                }
            }

            // 5. 甲板起重吊杆 (Deck Crane)
            if (hasDeckCrane)
            {
                Vector3 craneBase = origin + rot * new Vector3(0f, hullH, 8.5f);
                metalComb.AddCylinder(craneBase, 1.2f, 2.2f, 8, pal.SteelDark, rot);
                metalComb.AddBox(craneBase + rot * new Vector3(0f, 3.0f, 0f), new Vector3(2.2f, 1.8f, 2.4f), pal.HazardYellow, rot);

                float boomL = 15f;
                Quaternion boomRot = rot * Quaternion.Euler(-32f, 45f, 0f);
                Vector3 boomCenter = craneBase + rot * new Vector3(0f, 3.8f, 0f) + boomRot * new Vector3(0f, boomL * 0.5f, 0f);
                metalComb.AddBox(boomCenter, new Vector3(0.6f, boomL, 0.6f), pal.HazardYellow, boomRot);
            }
        }

        /// <summary>
        /// 远景悬索桥剪影 (跨度 190m，双柱塔身 + 悬索弧线 + 桥面，单色压暗不抢戏)。
        /// </summary>
        private static void BuildSuspensionBridge(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            float zBridge = 120f;
            float deckY = 17.0f;
            float towerTopY = 46.0f;
            float waterY = -1.35f;
            float spanHalf = 95f;
            Color bridgeColor = pal.BridgeColor;
            Color cableColor = Color.Lerp(bridgeColor, pal.SteelDark, 0.25f);

            // 1. 桥面主梁
            matteComb.AddBox(new Vector3(0f, deckY, zBridge), new Vector3(spanHalf * 2f, 2.2f, 6.0f), bridgeColor);
            matteComb.AddBox(new Vector3(0f, deckY + 1.15f, zBridge), new Vector3(spanHalf * 2f, 0.1f, 5.2f), pal.ConcreteDark);
            metalComb.AddBox(new Vector3(0f, deckY + 1.8f, zBridge - 2.8f), new Vector3(spanHalf * 2f, 1.2f, 0.3f), bridgeColor);
            metalComb.AddBox(new Vector3(0f, deckY + 1.8f, zBridge + 2.8f), new Vector3(spanHalf * 2f, 1.2f, 0.3f), bridgeColor);

            // 2. 双桥塔 (双柱门式造型)
            float[] towerX = { -40f, 40f };
            for (int t = 0; t < towerX.Length; t++)
            {
                float tx = towerX[t];
                float legZSpan = 5.6f;
                float legW = 1.3f;
                float legH = towerTopY - waterY;
                float legCenterY = waterY + legH * 0.5f;

                metalComb.AddBox(new Vector3(tx, legCenterY, zBridge - legZSpan * 0.5f), new Vector3(legW, legH, legW), bridgeColor);
                metalComb.AddBox(new Vector3(tx, legCenterY, zBridge + legZSpan * 0.5f), new Vector3(legW, legH, legW), bridgeColor);

                float[] crossTiers = { deckY, deckY + 9f, deckY + 18f, towerTopY };
                for (int c = 0; c < crossTiers.Length; c++)
                {
                    metalComb.AddBox(new Vector3(tx, crossTiers[c], zBridge), new Vector3(legW * 0.9f, 1.1f, legZSpan), bridgeColor);
                }

                metalComb.AddBox(new Vector3(tx, towerTopY + 0.8f, zBridge - legZSpan * 0.5f), new Vector3(legW * 1.3f, 0.8f, legW * 1.3f), pal.SteelDark);
                metalComb.AddBox(new Vector3(tx, towerTopY + 0.8f, zBridge + legZSpan * 0.5f), new Vector3(legW * 1.3f, 0.8f, legW * 1.3f), pal.SteelDark);
            }

            // 3. 悬索主缆与吊杆
            float[] cableZOffsets = { -2.7f, 2.7f };
            for (int cz = 0; cz < cableZOffsets.Length; cz++)
            {
                float czOff = cableZOffsets[cz];
                float cableZ = zBridge + czOff;

                float CableY(float x)
                {
                    if (x < -40f)
                    {
                        float t = (x - (-95f)) / 55f;
                        return Mathf.Lerp(deckY, towerTopY, t * t);
                    }
                    else if (x <= 40f)
                    {
                        float normX = x / 40f;
                        float sag = towerTopY - 20.0f;
                        return 20.0f + sag * (normX * normX);
                    }
                    else
                    {
                        float t = (x - 40f) / 55f;
                        return Mathf.Lerp(towerTopY, deckY, (1f - (1f - t) * (1f - t)));
                    }
                }

                int totalSegs = 36;
                float minX = -spanHalf;
                float maxX = spanHalf;
                float step = (maxX - minX) / totalSegs;

                for (int s = 0; s < totalSegs; s++)
                {
                    float x0 = minX + s * step;
                    float x1 = x0 + step;
                    float y0 = CableY(x0);
                    float y1 = CableY(x1);

                    Vector3 p0 = new Vector3(x0, y0, cableZ);
                    Vector3 p1 = new Vector3(x1, y1, cableZ);
                    Vector3 mid = (p0 + p1) * 0.5f;
                    float segLen = Vector3.Distance(p0, p1);
                    float angle = Mathf.Atan2(y1 - y0, x1 - x0) * Mathf.Rad2Deg;
                    Quaternion rot = Quaternion.Euler(0f, 0f, angle);

                    metalComb.AddBox(mid, new Vector3(segLen, 0.22f, 0.22f), cableColor, rot);

                    if (s % 2 == 1 && y0 > deckY + 1.5f && Mathf.Abs(x0 - (-40f)) > 2.5f && Mathf.Abs(x0 - 40f) > 2.5f)
                    {
                        float suspH = y0 - deckY - 1.1f;
                        metalComb.AddBox(new Vector3(x0, deckY + 1.1f + suspH * 0.5f, cableZ), new Vector3(0.08f, suspH, 0.08f), cableColor);
                    }
                }
            }
        }

        /// <summary>
        /// 远景城市剪影 + 双层低多边形山丘 + 海角灯塔 + 工业水塔 + 海面白帆船。
        /// </summary>
        private static void BuildCityAndHills(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            // ---- 1. 远景海岛低多边形山丘 (双层纵深派生绿) ----
            // 近层山丘
            var nearHills = new[]
            {
                new { pos = new Vector3(-65f, -1.35f, 138f), r = 36f, h = 28f },
                new { pos = new Vector3(-22f, -1.35f, 148f), r = 42f, h = 34f },
                new { pos = new Vector3(25f,  -1.35f, 145f), r = 40f, h = 32f },
                new { pos = new Vector3(68f,  -1.35f, 136f), r = 34f, h = 26f },
                new { pos = new Vector3(-88f, -1.35f, 65f),  r = 32f, h = 24f },
                new { pos = new Vector3(88f,  -1.35f, 60f),  r = 30f, h = 23f }
            };
            for (int i = 0; i < nearHills.Length; i++)
            {
                var h = nearHills[i];
                matteComb.AddCone(h.pos, h.r, 2.5f, h.h, 10, pal.HillNearColor);
            }

            // 远层更高山丘 (更淡青绿大气色)
            var farHills = new[]
            {
                new { pos = new Vector3(-45f, -1.35f, 168f), r = 48f, h = 42f },
                new { pos = new Vector3(2f,   -1.35f, 175f), r = 52f, h = 45f },
                new { pos = new Vector3(48f,  -1.35f, 170f), r = 46f, h = 39f },
                new { pos = new Vector3(-96f, -1.35f, 25f),  r = 38f, h = 26f },
                new { pos = new Vector3(96f,  -1.35f, 20f),  r = 36f, h = 25f }
            };
            for (int i = 0; i < farHills.Length; i++)
            {
                var h = farHills[i];
                matteComb.AddCone(h.pos, h.r, 3.0f, h.h, 10, pal.HillFarColor);
            }

            // ---- 2. 远景海滨城市剪影 (20 栋建筑) ----
            float[] cityAngles =
            {
                30f, 42f, 54f, 66f, 78f, 90f, 102f, 114f, 126f, 138f,
                222f, 234f, 246f, 258f, 270f, 282f, 294f, 306f, 318f, 330f
            };
            for (int i = 0; i < cityAngles.Length; i++)
            {
                float ang = cityAngles[i] * Mathf.Deg2Rad;
                float dist = 82f + ((i * 17) % 7) * 2.2f;
                float w = 9f + (i % 3) * 2.5f;
                float d = 9f + ((i + 1) % 3) * 2.5f;
                float h = 12f + ((i * 23) % 11) * 1.5f;

                Vector3 pos = new Vector3(Mathf.Sin(ang) * dist, h * 0.5f - 1.35f, Mathf.Cos(ang) * dist);
                Quaternion rot = Quaternion.Euler(0f, cityAngles[i], 0f);
                matteComb.AddBox(pos, new Vector3(w, h, d), pal.CityColor, rot);
            }

            // ---- 3. 海角灯塔 (Lighthouse，位于东北岬角) ----
            Vector3 lhPos = new Vector3(68f, -1.35f, 75f);
            matteComb.AddCone(lhPos, 8.0f, 5.0f, 4.0f, 8, pal.ConcreteDark);
            matteComb.AddCone(lhPos + Vector3.up * 4.0f, 2.4f, 1.6f, 6.0f, 8, pal.ConcreteLight);
            matteComb.AddCone(lhPos + Vector3.up * 10.0f, 1.6f, 1.3f, 4.5f, 8, pal.ContainerRust);
            matteComb.AddCone(lhPos + Vector3.up * 14.5f, 1.3f, 1.1f, 3.5f, 8, pal.ConcreteLight);
            metalComb.AddCylinder(lhPos + Vector3.up * 18.0f, 1.4f, 1.6f, 8, pal.SteelDark);
            matteComb.AddBox(lhPos + Vector3.up * 18.8f, new Vector3(1.0f, 1.0f, 1.0f), pal.LampGlass);
            metalComb.AddCone(lhPos + Vector3.up * 19.6f, 1.5f, 0.1f, 1.2f, 8, pal.SteelDark);

            // ---- 4. 工业水塔 (Water Tower，位于西北岸边) ----
            Vector3 wtPos = new Vector3(-68f, -1.35f, 72f);
            float wtLegH = 13.0f;
            float wtLegR = 2.4f;
            for (int l = 0; l < 4; l++)
            {
                float a = l * Mathf.PI * 0.5f;
                Vector3 legP = wtPos + new Vector3(Mathf.Cos(a) * wtLegR, wtLegH * 0.5f, Mathf.Sin(a) * wtLegR);
                metalComb.AddBox(legP, new Vector3(0.35f, wtLegH, 0.35f), pal.Steel);
            }
            metalComb.AddCylinder(wtPos + Vector3.up * wtLegH, 3.0f, 3.5f, 10, pal.Steel);
            metalComb.AddCone(wtPos + Vector3.up * (wtLegH + 3.5f), 3.2f, 0.1f, 1.4f, 10, pal.SteelDark);

            // ---- 5. 海面白帆船 (Sailboats) ----
            Vector3[] boatPos =
            {
                new Vector3(-18f, -1.35f, 62f),
                new Vector3(32f,  -1.35f, 88f)
            };
            for (int b = 0; b < boatPos.Length; b++)
            {
                Vector3 bp = boatPos[b];
                matteComb.AddBox(bp + Vector3.up * 0.35f, new Vector3(1.6f, 0.7f, 4.2f), pal.ConcreteLight);
                metalComb.AddBox(bp + Vector3.up * 2.8f, new Vector3(0.1f, 4.5f, 0.1f), pal.SteelDark);
                Vector3 v0 = bp + new Vector3(0f, 0.8f, -1.6f);
                Vector3 v1 = bp + new Vector3(0f, 0.8f, 0.4f);
                Vector3 v2 = bp + new Vector3(0f, 4.8f, -0.4f);
                matteComb.AddQuadRaw(v0, v1, v2, v2, Vector3.right, Vector2.zero, Vector2.right, Vector2.up, Vector2.up, pal.ConcreteLight);
            }
        }

        /// <summary>
        /// 港湾近景（backdropTheme = Marina）：竞技场坐落在伸入海面的混凝土栈桥上，
        /// 四周是浮桥、游艇、航道标与港务小屋；远景城市/山丘由 <see cref="BuildCityAndHills"/> 负责。
        /// 全部无碰撞、无墨面、无阴影，合并为 Matte / Metal 两张网格。
        /// </summary>
        private static void BuildMarinaScenery(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            const float waterY = -1.35f;
            Color whiteHull = Color.Lerp(pal.ConcreteLight, Color.white, 0.55f);
            Color teal = pal.ContainerTeal;
            Color coral = pal.ContainerRust;
            Color woodDeck = pal.Wood;

            // ---- 1. 竞技场脚下的码头大平台（场界外扩 4m，水面从边缘开始）----
            //     台面不能刷成纯白：在斜俯视角下会把整圈读成"雪地"，用暖灰压一档。
            matteComb.AddBox(new Vector3(0f, -0.62f, 0f), new Vector3(48f, 1.2f, 64f), pal.ConcreteMid);
            // 顶板只铺竞技场外的 3.8m 环带：整块 47.6×63.6 顶板与竞技场地面（40×56，顶面 y=0）
            // 完全共面重叠，会在场内整片 z-fighting —— 相机一移动就出现横向白色条带闪烁。
            // 现在拆成四条环带，顶面仍保持 y=0 与场地齐平，但不再有任何一块覆盖场内。
            Color plateColor = Color.Lerp(pal.ConcreteLight, pal.ConcreteMid, 0.45f);
            matteComb.AddBox(new Vector3(0f, -0.03f, 29.9f), new Vector3(47.6f, 0.06f, 3.8f), plateColor);
            matteComb.AddBox(new Vector3(0f, -0.03f, -29.9f), new Vector3(47.6f, 0.06f, 3.8f), plateColor);
            matteComb.AddBox(new Vector3(21.9f, -0.03f, 0f), new Vector3(3.8f, 0.06f, 56f), plateColor);
            matteComb.AddBox(new Vector3(-21.9f, -0.03f, 0f), new Vector3(3.8f, 0.06f, 56f), plateColor);

            // 沿口黄安全线：近景一圈细黄线，给码头一个可读的边界
            for (int side = -1; side <= 1; side += 2)
            {
                matteComb.AddBox(new Vector3(0f, 0.012f, side * 31.55f), new Vector3(46.8f, 0.02f, 0.16f), pal.HazardYellow);
                matteComb.AddBox(new Vector3(side * 23.55f, 0.012f, 0f), new Vector3(0.16f, 0.02f, 62.8f), pal.HazardYellow);
            }

            // 沿口压边 + 系缆桩
            for (int side = -1; side <= 1; side += 2)
            {
                matteComb.AddBox(new Vector3(0f, 0.22f, side * 32.2f), new Vector3(48.6f, 0.5f, 0.45f), pal.ConcreteDark);
                matteComb.AddBox(new Vector3(side * 24.2f, 0.22f, 0f), new Vector3(0.45f, 0.5f, 64.6f), pal.ConcreteDark);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 7; i++)
                {
                    float x = -18f + i * 6f;
                    metalComb.AddCylinder(new Vector3(x, -0.02f, side * 31.6f), 0.20f, 0.55f, 8, pal.SteelDark);
                }
            }

            // ---- 2. 四座浮桥：从平台伸出，端部 T 头 ----
            for (int zSide = -1; zSide <= 1; zSide += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    float px = xSide * 10.5f;
                    float pierCenterZ = zSide * 41f;
                    matteComb.AddBox(new Vector3(px, -0.20f, pierCenterZ), new Vector3(4.2f, 0.36f, 16f), woodDeck);
                    matteComb.AddBox(new Vector3(px, -0.20f, zSide * 48.6f), new Vector3(9.0f, 0.36f, 4.4f), woodDeck);

                    // 木桩
                    for (int p = 0; p < 4; p++)
                    {
                        float sz = pierCenterZ + (p % 2 == 0 ? -7.4f : 7.4f);
                        float sx = px + (p < 2 ? -1.9f : 1.9f);
                        metalComb.AddCylinder(new Vector3(sx, waterY, sz), 0.20f, 1.5f, 7, pal.WoodDark);
                    }
                    for (int p = 0; p < 4; p++)
                    {
                        float sx = px + (p < 2 ? -4.2f : 4.2f);
                        float sz = zSide * 48.6f + (p % 2 == 0 ? -2.0f : 2.0f);
                        metalComb.AddCylinder(new Vector3(sx, waterY, sz), 0.20f, 1.5f, 7, pal.WoodDark);
                    }

                    // T 头小屋（港务/寄存）
                    matteComb.AddBox(new Vector3(px, 1.6f, zSide * 49.4f), new Vector3(5.2f, 3.2f, 3.4f), whiteHull);
                    matteComb.AddBox(new Vector3(px, 3.26f, zSide * 49.4f), new Vector3(5.8f, 0.24f, 4.0f), teal);
                    matteComb.AddBox(new Vector3(px, 2.0f, zSide * (49.4f - 1.72f)), new Vector3(2.4f, 0.7f, 0.06f), pal.SteelDark);

                    // ---- 3. 游艇（泊在浮桥两侧）----
                    for (int b = 0; b < 4; b++)
                    {
                        float bx = px + (b % 2 == 0 ? -4.3f : 4.3f);
                        float bz = zSide * (35.5f + (b / 2) * 7.2f);
                        AddYacht(matteComb, metalComb, new Vector3(bx, waterY, bz), teal, whiteHull, coral, pal.SteelDark);
                    }
                }
            }

            // ---- 4. 航道标：红白相间的入口柱 ----
            for (int zSide = -1; zSide <= 1; zSide += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    Vector3 p = new Vector3(xSide * 19.0f, -0.02f, zSide * 34.2f);
                    metalComb.AddCylinder(p, 0.42f, 1.6f, 8, coral);
                    metalComb.AddCylinder(p + Vector3.up * 1.6f, 0.44f, 0.62f, 8, whiteHull);
                    metalComb.AddCylinder(p + Vector3.up * 2.2f, 0.42f, 1.2f, 8, coral);
                    metalComb.AddCylinder(p + Vector3.up * 3.4f, 0.44f, 0.55f, 8, whiteHull);
                    metalComb.AddBox(p + Vector3.up * 4.15f, new Vector3(0.5f, 0.5f, 0.5f), pal.LampGlass);
                }
            }

            // ---- 5. 防波堤：两端各一道，围出港湾入口 ----
            for (int zSide = -1; zSide <= 1; zSide += 2)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                {
                    matteComb.AddBox(new Vector3(xSide * 38f, -0.55f, zSide * 58f), new Vector3(42f, 1.6f, 3.2f), pal.ConcreteDark);
                    metalComb.AddBox(new Vector3(xSide * 38f, 0.30f, zSide * 58f), new Vector3(42.4f, 0.16f, 3.5f), pal.ConcreteLight);
                }
            }

            // ---- 6. 平台上的港务小楼（点对称两座，给出生侧一个近景建筑）----
            for (int side = -1; side <= 1; side += 2)
            {
                float bx = side * 21.2f;
                float bz = -side * 29.0f;
                matteComb.AddBox(new Vector3(bx, 2.0f, bz), new Vector3(6.0f, 4.0f, 5.2f), pal.ConcreteLight);
                matteComb.AddBox(new Vector3(bx, 4.12f, bz), new Vector3(6.6f, 0.24f, 5.8f), teal);
                matteComb.AddBox(new Vector3(bx, 2.2f, bz + (bz < 0f ? 2.62f : -2.62f)), new Vector3(3.2f, 1.1f, 0.06f), pal.SteelDark);
            }
        }

        /// <summary>远景游艇：白船体 + 青绿舱室 + 珊瑚吃水线 + 桅杆与三角帆（全部合并进远景网格）。</summary>
        private static void AddYacht(
            MeshCombiner matteComb,
            MeshCombiner metalComb,
            Vector3 waterAnchor,
            Color cabinColor,
            Color hullColor,
            Color accentColor,
            Color mastColor)
        {
            // 船体（水线以上 0.9m）
            matteComb.AddBox(waterAnchor + Vector3.up * 0.45f, new Vector3(2.4f, 0.9f, 7.4f), hullColor);
            // 吃水线
            matteComb.AddBox(waterAnchor + Vector3.up * 0.10f, new Vector3(2.46f, 0.14f, 7.36f), accentColor);
            // 舱室
            matteComb.AddBox(waterAnchor + Vector3.up * 1.35f, new Vector3(1.9f, 1.0f, 2.6f), cabinColor);
            matteComb.AddBox(waterAnchor + Vector3.up * 1.92f, new Vector3(2.0f, 0.12f, 2.7f), hullColor);
            // 桅杆
            metalComb.AddBox(waterAnchor + Vector3.up * 4.4f, new Vector3(0.12f, 5.6f, 0.12f), mastColor);
            // 三角帆（两面，读作帆船）
            Vector3 s0 = waterAnchor + new Vector3(0f, 1.0f, -1.2f);
            Vector3 s1 = waterAnchor + new Vector3(0f, 1.0f, 0.8f);
            Vector3 s2 = waterAnchor + new Vector3(0f, 6.2f, -0.2f);
            matteComb.AddQuadRaw(s0, s1, s2, s2, Vector3.right, Vector2.zero, Vector2.right, Vector2.up, Vector2.up, hullColor);
            matteComb.AddQuadRaw(s0, s1, s2, s2, Vector3.left, Vector2.zero, Vector2.right, Vector2.up, Vector2.up, hullColor);
        }

        /// <summary>
        /// 对岸港湾小镇：四段远岸 + 彩色低层建筑（奶油白/薄荷/珊瑚/黄油/港青）+ 屋顶 + 远景山丘。
        /// 替换集装箱主题的灰色城市剪影，让港湾图在第三人称远眺与俯瞰预览下都有明确的"海滨小镇"读形。
        /// </summary>
        private static void BuildMarinaTown(MeshCombiner matteComb, MeshCombiner metalComb, BackdropPalette pal)
        {
            const float waterY = -1.35f;

            Color[] facades =
            {
                Color.Lerp(pal.ConcreteLight, Color.white, 0.35f),
                Color.Lerp(pal.ContainerTeal, pal.ConcreteLight, 0.45f),
                Color.Lerp(pal.ContainerRust, pal.ConcreteLight, 0.42f),
                Color.Lerp(pal.ContainerMustard, pal.ConcreteLight, 0.40f),
                Color.Lerp(pal.ConcreteMid, pal.ContainerTeal, 0.28f),
            };
            Color roofDark = Color.Lerp(pal.SteelDark, pal.ContainerTeal, 0.25f);
            Color shoreTop = Color.Lerp(pal.ConcreteLight, pal.ConcreteMid, 0.35f);

            // ---- 1. 四段远岸：台面比水面高 1.4m，小镇坐在上面 ----
            float[] shoreZ = { 62f, -62f };
            for (int s = 0; s < shoreZ.Length; s++)
            {
                float z = shoreZ[s];
                matteComb.AddBox(new Vector3(0f, waterY + 0.55f, z), new Vector3(150f, 1.7f, 26f), pal.ConcreteDark);
                matteComb.AddBox(new Vector3(0f, waterY + 1.42f, z), new Vector3(149f, 0.1f, 25f), shoreTop);
                matteComb.AddBox(new Vector3(0f, waterY + 1.48f, z + (z > 0 ? -12.2f : 12.2f)),
                    new Vector3(148f, 0.04f, 0.3f), pal.HazardYellow);
            }
            float[] shoreX = { 58f, -58f };
            for (int s = 0; s < shoreX.Length; s++)
            {
                float x = shoreX[s];
                matteComb.AddBox(new Vector3(x, waterY + 0.55f, 0f), new Vector3(26f, 1.7f, 120f), pal.ConcreteDark);
                matteComb.AddBox(new Vector3(x, waterY + 1.42f, 0f), new Vector3(25f, 0.1f, 119f), shoreTop);
            }

            // ---- 2. 南北岸各 7 栋、东西岸各 5 栋彩色建筑 ----
            for (int s = 0; s < 2; s++)
            {
                float z = shoreZ[s];
                float faceSign = z > 0 ? -1f : 1f;
                for (int i = 0; i < 7; i++)
                {
                    float x = -63f + i * 19.5f + (i % 2) * 3.5f;
                    float w = 8f + (i % 3) * 2.4f;
                    float d = 8f + ((i + 1) % 3) * 2f;
                    float h = 5.5f + ((i * 7) % 5) * 1.6f;
                    Color body = facades[i % facades.Length];
                    Vector3 pos = new Vector3(x, waterY + 1.45f + h * 0.5f, z + faceSign * 6f);
                    matteComb.AddBox(pos, new Vector3(w, h, d), body);
                    matteComb.AddBox(pos + Vector3.up * (h * 0.5f + 0.18f), new Vector3(w + 0.5f, 0.36f, d + 0.5f), roofDark);
                    metalComb.AddBox(new Vector3(x, pos.y + h * 0.15f, z + faceSign * (6f + d * 0.5f + 0.02f)),
                        new Vector3(w * 0.62f, 0.8f, 0.06f), Color.Lerp(pal.SteelDark, body, 0.25f));
                    if (i % 2 == 0)
                    {
                        matteComb.AddBox(pos + new Vector3(w * 0.22f, h * 0.5f + 0.5f, 0f),
                            new Vector3(0.8f, 0.7f, 0.8f), pal.ConcreteDark);
                    }
                }
            }
            for (int s = 0; s < 2; s++)
            {
                float x = shoreX[s];
                float faceSign = x > 0 ? -1f : 1f;
                for (int i = 0; i < 5; i++)
                {
                    float z = -48f + i * 24f;
                    float w = 9f + (i % 3) * 2f;
                    float d = 8f + ((i + 1) % 3) * 2f;
                    float h = 5.5f + ((i * 5) % 5) * 1.5f;
                    Color body = facades[(i + 2) % facades.Length];
                    Vector3 pos = new Vector3(x + faceSign * 6f, waterY + 1.45f + h * 0.5f, z);
                    matteComb.AddBox(pos, new Vector3(w, h, d), body);
                    matteComb.AddBox(pos + Vector3.up * (h * 0.5f + 0.18f),
                        new Vector3(w + 0.5f, 0.36f, d + 0.5f), roofDark);
                }
            }

            // ---- 3. 远景山丘：雾里只留剪影，给小镇一层纵深 ----
            var hills = new[]
            {
                new { pos = new Vector3(-70f, waterY, 118f), r = 34f, h = 26f },
                new { pos = new Vector3(-10f, waterY, 126f), r = 40f, h = 31f },
                new { pos = new Vector3(52f, waterY, 120f), r = 36f, h = 28f },
                new { pos = new Vector3(-95f, waterY, 40f), r = 30f, h = 22f },
                new { pos = new Vector3(95f, waterY, 30f), r = 30f, h = 22f },
            };
            for (int i = 0; i < hills.Length; i++)
            {
                matteComb.AddCone(hills[i].pos, hills[i].r, 2.5f, hills[i].h, 10, pal.HillFarColor);
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
        /// </summary>
        private static void CreateInkPropProxy(
            Transform parent,
            ArenaLayoutV2.ArenaPropSpec prop,
            Quaternion rotation,
            Bounds localBounds,
            ArenaMaterialSet mats,
            InkGridConfigSO inkCfg,
            int serial,
            List<Collider> blockedColliders = null)
        {
            var go = new GameObject($"PropCollider_{prop.Kind}_{serial:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = prop.Position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.center = localBounds.center;
            box.size = localBounds.size;

            if (box != null && blockedColliders != null)
            {
                blockedColliders.Add(box);
            }

            if (inkCfg != null && !inkCfg.enableWallInk)
            {
                return;
            }

            Vector3 centerWorld = prop.Position + rotation * localBounds.center;
            var wall = go.AddComponent<InkWallSurface>();
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
        /// 风格解包辅助结构：从 ArenaStyleSO 统一解构与派生颜色。
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
            public readonly Color Wood;
            public readonly Color WoodDark;
            public readonly Color Foliage;
            public readonly Color LampGlass;
            public readonly Color WaterShallow;
            public readonly Color WaterDeep;
            public readonly Color WaterHorizon;
            public readonly Color BridgeColor;
            public readonly Color HillNearColor;
            public readonly Color HillFarColor;
            public readonly Color CityColor;

            public BackdropPalette(ArenaStyleSO s)
            {
                ConcreteLight = s != null ? s.concreteLight : new Color(0.78f, 0.76f, 0.70f, 1f);
                ConcreteMid = s != null ? s.concreteMid : new Color(0.62f, 0.60f, 0.55f, 1f);
                ConcreteDark = s != null ? s.concreteDark : new Color(0.40f, 0.39f, 0.36f, 1f);
                Steel = s != null ? s.steel : new Color(0.42f, 0.47f, 0.52f, 1f);
                SteelDark = s != null ? s.steelDark : new Color(0.24f, 0.27f, 0.30f, 1f);
                HazardYellow = s != null ? s.hazardYellow : new Color(0.86f, 0.68f, 0.20f, 1f);
                HazardDark = s != null ? s.hazardDark : new Color(0.16f, 0.16f, 0.16f, 1f);
                ContainerTeal = s != null ? s.containerTeal : new Color(0.25f, 0.52f, 0.50f, 1f);
                ContainerRust = s != null ? s.containerRust : new Color(0.62f, 0.33f, 0.24f, 1f);
                ContainerSlate = s != null ? s.containerSlate : new Color(0.37f, 0.46f, 0.57f, 1f);
                ContainerMustard = s != null ? s.containerMustard : new Color(0.79f, 0.60f, 0.24f, 1f);
                Wood = s != null ? s.wood : new Color(0.72f, 0.58f, 0.42f, 1f);
                WoodDark = s != null ? s.woodDark : new Color(0.48f, 0.36f, 0.24f, 1f);
                Foliage = s != null ? s.foliage : new Color(0.24f, 0.45f, 0.28f, 1f);
                LampGlass = s != null ? s.lampGlass : new Color(1.00f, 0.85f, 0.60f, 1f);
                WaterShallow = s != null ? s.waterShallow : new Color(0.20f, 0.55f, 0.62f, 1f);
                WaterDeep = s != null ? s.waterDeep : new Color(0.05f, 0.24f, 0.36f, 1f);
                WaterHorizon = s != null ? s.waterHorizon : new Color(0.66f, 0.78f, 0.84f, 1f);

                BridgeColor = Color.Lerp(Steel, ContainerRust, 0.32f);
                HillNearColor = Color.Lerp(Foliage, ConcreteMid, 0.20f);
                HillFarColor = Color.Lerp(Foliage, Steel, 0.42f);
                CityColor = Color.Lerp(SteelDark, ConcreteMid, 0.25f);
            }
        }

        /// <summary>
        /// 多来源网格合并器，支持直接写入图元或变换追加子网格。
        /// </summary>
        private sealed class MeshCombiner
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(4096);
            public readonly List<Vector3> Normals = new List<Vector3>(4096);
            public readonly List<Vector2> UVs = new List<Vector2>(4096);
            public readonly List<Color> Colors = new List<Color>(4096);
            public readonly List<int> Triangles = new List<int>(6144);

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

            public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Color color, Quaternion? rot = null)
            {
                int segs = Mathf.Max(segments, 6);
                Matrix4x4 trs = Matrix4x4.TRS(baseCenter, rot ?? Quaternion.identity, Vector3.one);
                Matrix4x4 normM = trs.inverse.transpose;
                Color linearColor = Linear(color);

                // 侧面
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

                // 顶盖 (朝向 +Y)
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

            public void AddWordCargo(Vector3 center, float height, Color color, Quaternion rot)
            {
                float h = height;
                float w = h * 0.65f;
                float t = h * 0.18f;
                float d = 0.08f;
                float sp = h * 0.22f;
                float totalW = 5 * w + 4 * sp;
                float startX = -totalW * 0.5f + w * 0.5f;

                void AddStroke(Vector3 letterPos, Vector3 localCenter, Vector3 size)
                {
                    Vector3 pos = letterPos + localCenter;
                    AddBox(center + rot * pos, size, color, rot);
                }

                // C
                {
                    Vector3 lp = new Vector3(startX + 0 * (w + sp), 0f, 0f);
                    AddStroke(lp, new Vector3(-w * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, d));
                    AddStroke(lp, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(0f, -h * 0.5f + t * 0.5f, 0f), new Vector3(w, t, d));
                }
                // A
                {
                    Vector3 lp = new Vector3(startX + 1 * (w + sp), 0f, 0f);
                    AddStroke(lp, new Vector3(-w * 0.5f + t * 0.5f, -t * 0.5f, 0f), new Vector3(t, h - t, d));
                    AddStroke(lp, new Vector3(w * 0.5f - t * 0.5f, -t * 0.5f, 0f), new Vector3(t, h - t, d));
                    AddStroke(lp, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(0f, 0f, 0f), new Vector3(w, t, d));
                }
                // R
                {
                    Vector3 lp = new Vector3(startX + 2 * (w + sp), 0f, 0f);
                    AddStroke(lp, new Vector3(-w * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, d));
                    AddStroke(lp, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(0f, 0.05f * h, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(w * 0.5f - t * 0.5f, 0.28f * h, 0f), new Vector3(t, 0.45f * h, d));
                    AddStroke(lp, new Vector3(w * 0.5f - t * 0.5f, -0.28f * h, 0f), new Vector3(t, 0.45f * h, d));
                }
                // G
                {
                    Vector3 lp = new Vector3(startX + 3 * (w + sp), 0f, 0f);
                    AddStroke(lp, new Vector3(-w * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, d));
                    AddStroke(lp, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(0f, -h * 0.5f + t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(w * 0.5f - t * 0.5f, -0.22f * h, 0f), new Vector3(t, 0.55f * h, d));
                    AddStroke(lp, new Vector3(0.12f * w, 0f, 0f), new Vector3(w * 0.55f, t, d));
                }
                // O
                {
                    Vector3 lp = new Vector3(startX + 4 * (w + sp), 0f, 0f);
                    AddStroke(lp, new Vector3(-w * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, d));
                    AddStroke(lp, new Vector3(w * 0.5f - t * 0.5f, 0f, 0f), new Vector3(t, h, d));
                    AddStroke(lp, new Vector3(0f, h * 0.5f - t * 0.5f, 0f), new Vector3(w, t, d));
                    AddStroke(lp, new Vector3(0f, -h * 0.5f + t * 0.5f, 0f), new Vector3(w, t, d));
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
