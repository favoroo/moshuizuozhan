using System;
using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 道具材质表面分类，对应 ArenaMaterialSet 中的 4 种道具通用材质（P7-B）。
    /// </summary>
    public enum ArenaPropSurface
    {
        Matte,
        Metal,
        Emissive,
        Cloth
    }

    /// <summary>
    /// 单个道具网格部件，携带网格、所属表面类型与基础调色。
    /// Solid 标记该部件是否参与碰撞/涂墨代理盒的包围盒计算：
    /// 花坛的土壤与灌木、售卖亭的挑檐这类伸出主体、又薄又碎的装饰件标 false，
    /// 让碰撞盒贴合真正能挡子弹、能涂墨的实体轮廓。
    /// </summary>
    public struct ArenaPropMeshPart
    {
        public Mesh Mesh;
        public ArenaPropSurface Surface;
        public Color Color;
        public bool Solid;

        public ArenaPropMeshPart(Mesh mesh, ArenaPropSurface surface, Color color, bool solid = true)
        {
            Mesh = mesh;
            Surface = surface;
            Color = color;
            Solid = solid;
        }
    }

    /// <summary>
    /// 竞技场地表道具程序化网格工厂（P7-B / P30 场地精修轮）：
    /// 纯网格代码生成，不依赖 GameObject.CreatePrimitive 与 UnityEditor API。
    /// 所有生成的网格均包含 position/normal/uv/color 四个完整顶点流。
    /// 坐标约定：道具底面在 y=0、正面朝 +Z、单位米。
    /// </summary>
    public static class ArenaPropMeshFactory
    {
        /// <summary>
        /// 样式配色解包辅助结构，对 null style 做安全回退，派生色严格走 Color.Lerp。
        /// </summary>
        private readonly struct Palette
        {
            public readonly Color Wood;
            public readonly Color WoodDark;
            public readonly Color WoodLight;
            public readonly Color WoodDarker;
            public readonly Color Foliage;
            public readonly Color FoliageLight;
            public readonly Color FoliageDark;
            public readonly Color LampGlass;
            public readonly Color Steel;
            public readonly Color SteelLight;
            public readonly Color SteelDark;
            public readonly Color SteelBlack;
            public readonly Color ConcreteLight;
            public readonly Color ConcreteMid;
            public readonly Color ConcreteDark;
            public readonly Color ContainerTeal;
            public readonly Color ContainerRust;
            public readonly Color ContainerSlate;
            public readonly Color ContainerMustard;
            public readonly Color TeamAPad;
            public readonly Color TeamBPad;
            public readonly Color HazardYellow;
            public readonly Color ClothWhite;
            public readonly Color StripeRed;
            public readonly Color StripeBlue;
            public readonly Color FlowerYellow;

            public Palette(ArenaStyleSO s)
            {
                Wood = s != null ? s.wood : new Color(0.72f, 0.58f, 0.42f, 1f);
                WoodDark = s != null ? s.woodDark : new Color(0.48f, 0.36f, 0.24f, 1f);
                WoodLight = Color.Lerp(Wood, Color.white, 0.22f);
                WoodDarker = Color.Lerp(WoodDark, Color.black, 0.25f);

                Foliage = s != null ? s.foliage : new Color(0.24f, 0.45f, 0.28f, 1f);
                FoliageLight = Color.Lerp(Foliage, Color.white, 0.22f);
                FoliageDark = Color.Lerp(Foliage, s != null ? s.concreteDark : new Color(0.40f, 0.39f, 0.36f, 1f), 0.35f);

                LampGlass = s != null ? s.lampGlass : new Color(1.00f, 0.85f, 0.60f, 1f);
                Steel = s != null ? s.steel : new Color(0.42f, 0.47f, 0.52f, 1f);
                SteelLight = Color.Lerp(Steel, Color.white, 0.30f);
                SteelDark = s != null ? s.steelDark : new Color(0.24f, 0.27f, 0.30f, 1f);
                SteelBlack = Color.Lerp(SteelDark, Color.black, 0.30f);

                ConcreteLight = s != null ? s.concreteLight : new Color(0.78f, 0.76f, 0.70f, 1f);
                ConcreteMid = s != null ? s.concreteMid : new Color(0.62f, 0.60f, 0.55f, 1f);
                ConcreteDark = s != null ? s.concreteDark : new Color(0.40f, 0.39f, 0.36f, 1f);

                ContainerTeal = s != null ? s.containerTeal : new Color(0.25f, 0.52f, 0.50f, 1f);
                ContainerRust = s != null ? s.containerRust : new Color(0.62f, 0.33f, 0.24f, 1f);
                ContainerSlate = s != null ? s.containerSlate : new Color(0.37f, 0.46f, 0.57f, 1f);
                ContainerMustard = s != null ? s.containerMustard : new Color(0.79f, 0.60f, 0.24f, 1f);

                TeamAPad = s != null ? s.teamAPad : new Color(1.00f, 0.48f, 0.08f, 1f);
                TeamBPad = s != null ? s.teamBPad : new Color(0.15f, 0.35f, 1.00f, 1f);
                HazardYellow = s != null ? s.hazardYellow : new Color(0.86f, 0.68f, 0.20f, 1f);

                ClothWhite = Color.Lerp(ConcreteLight, Color.white, 0.50f);
                StripeRed = Color.Lerp(ContainerRust, Color.white, 0.15f);
                StripeBlue = Color.Lerp(ContainerSlate, Color.white, 0.15f);
                FlowerYellow = Color.Lerp(LampGlass, Color.white, 0.25f);
            }
        }

        /// <summary>
        /// 程序化构建指定种类的道具网格部件列表。
        /// </summary>
        public static void BuildProp(
            ArenaLayoutV2.ArenaPropKind kind,
            float scale,
            int variant,
            ArenaStyleSO style,
            List<ArenaPropMeshPart> output)
        {
            if (output == null) return;
            var palette = new Palette(style);
            float s = scale > 0.001f ? scale : 1.0f;

            switch (kind)
            {
                case ArenaLayoutV2.ArenaPropKind.CrateCluster:
                    BuildCrateCluster(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.CrateTall:
                    BuildCrateTall(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.BarrelCluster:
                    BuildBarrelCluster(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.PlanterBox:
                    BuildPlanterBox(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.ConeCluster:
                    BuildConeCluster(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.BollardRow:
                    BuildBollardRow(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.Bench:
                    BuildBench(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.AcUnit:
                    BuildAcUnit(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.PipeRun:
                    BuildPipeRun(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.LampPost:
                    BuildLampPost(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.PalmTree:
                    BuildPalmTree(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.FlagPole:
                    BuildFlagPole(s, variant, palette, output);
                    break;
                case ArenaLayoutV2.ArenaPropKind.Kiosk:
                    BuildKiosk(s, variant, palette, output);
                    break;
                default:
                    BuildCrateTall(s, variant, palette, output);
                    break;
            }
        }

        #region 1. CrateCluster (倒角木箱堆叠：板条分缝、加厚边框、打包带、金属包角与模板喷漆)
        private static void BuildCrateCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var trimBuf = new MeshBuffer();
            var plankBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();
            var strapBuf = new MeshBuffer();
            var stencilBuf = new MeshBuffer();

            void AddDetailedCrate(Vector3 center, Vector3 size, float yaw, bool hasStrap, bool yellowStrap, bool hasArrow, bool hasCargoBlock)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(center * scale, rot, size * scale);

                // 1. 核心实木箱体（Solid = true，严格决定碰撞与涂墨代理盒）
                woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, trs);

                // 2. 四角垂直木立柱
                float postW = 0.08f;
                trimBuf.AddBox(new Vector3(0.46f, 0f, 0.46f), new Vector3(postW, 1.01f, postW), Color.white, trs);
                trimBuf.AddBox(new Vector3(-0.46f, 0f, 0.46f), new Vector3(postW, 1.01f, postW), Color.white, trs);
                trimBuf.AddBox(new Vector3(0.46f, 0f, -0.46f), new Vector3(postW, 1.01f, postW), Color.white, trs);
                trimBuf.AddBox(new Vector3(-0.46f, 0f, -0.46f), new Vector3(postW, 1.01f, postW), Color.white, trs);

                // 3. 上下横框（四面）
                float railH = 0.08f;
                trimBuf.AddBox(new Vector3(0f, 0.46f, 0.505f), new Vector3(0.86f, railH, 0.02f), Color.white, trs);
                trimBuf.AddBox(new Vector3(0f, -0.46f, 0.505f), new Vector3(0.86f, railH, 0.02f), Color.white, trs);
                trimBuf.AddBox(new Vector3(0f, 0.46f, -0.505f), new Vector3(0.86f, railH, 0.02f), Color.white, trs);
                trimBuf.AddBox(new Vector3(0f, -0.46f, -0.505f), new Vector3(0.86f, railH, 0.02f), Color.white, trs);

                trimBuf.AddBox(new Vector3(0.505f, 0.46f, 0f), new Vector3(0.02f, railH, 0.86f), Color.white, trs);
                trimBuf.AddBox(new Vector3(0.505f, -0.46f, 0f), new Vector3(0.02f, railH, 0.86f), Color.white, trs);
                trimBuf.AddBox(new Vector3(-0.505f, 0.46f, 0f), new Vector3(0.02f, railH, 0.86f), Color.white, trs);
                trimBuf.AddBox(new Vector3(-0.505f, -0.46f, 0f), new Vector3(0.02f, railH, 0.86f), Color.white, trs);

                // 4. 四面各 3 根水平分缝木板条
                float plankH = 0.22f;
                float[] plankYs = { -0.25f, 0f, 0.25f };
                for (int p = 0; p < 3; p++)
                {
                    float py = plankYs[p];
                    plankBuf.AddBox(new Vector3(0f, py, 0.503f), new Vector3(0.84f, plankH, 0.008f), Color.white, trs);
                    plankBuf.AddBox(new Vector3(0f, py, -0.503f), new Vector3(0.84f, plankH, 0.008f), Color.white, trs);
                    plankBuf.AddBox(new Vector3(0.503f, py, 0f), new Vector3(0.008f, plankH, 0.84f), Color.white, trs);
                    plankBuf.AddBox(new Vector3(-0.503f, py, 0f), new Vector3(0.008f, plankH, 0.84f), Color.white, trs);
                }

                // 5. 8 个三向金属包角
                float cSz = 0.12f;
                for (int cx = -1; cx <= 1; cx += 2)
                {
                    for (int cy = -1; cy <= 1; cy += 2)
                    {
                        for (int cz = -1; cz <= 1; cz += 2)
                        {
                            metalBuf.AddBox(new Vector3(cx * 0.48f, cy * 0.48f, cz * 0.48f), new Vector3(cSz, cSz, cSz), Color.white, trs);
                        }
                    }
                }

                // 6. 打包带与金属锁扣
                if (hasStrap)
                {
                    Color strapCol = yellowStrap ? pal.HazardYellow : pal.SteelBlack;
                    strapBuf.AddBox(new Vector3(0f, 0.10f, 0f), new Vector3(1.025f, 0.04f, 1.025f), strapCol, trs);
                    strapBuf.AddBox(new Vector3(0f, 0.10f, 0.518f), new Vector3(0.07f, 0.06f, 0.015f), pal.Steel, trs);
                }

                // 7. 侧面模板喷印标记
                if (hasArrow)
                {
                    stencilBuf.AddBox(new Vector3(0f, -0.04f, 0.512f), new Vector3(0.06f, 0.10f, 0.005f), Color.white, trs);
                    Vector3 a0 = new Vector3(-0.09f, 0.01f, 0.512f);
                    Vector3 a1 = new Vector3(0.09f, 0.01f, 0.512f);
                    Vector3 a2 = new Vector3(0f, 0.14f, 0.512f);
                    stencilBuf.AddQuad(a0, a1, a2, a2, Color.white, trs);
                }
                else if (hasCargoBlock)
                {
                    stencilBuf.AddBox(new Vector3(0f, 0.04f, 0.512f), new Vector3(0.32f, 0.12f, 0.005f), Color.white, trs);
                }
            }

            int v = Mathf.Abs(variant) % 3;
            if (v == 0)
            {
                // 3箱堆叠 + 底部垫木板条
                AddDetailedCrate(new Vector3(-0.45f, 0.45f, -0.05f), new Vector3(0.9f, 0.9f, 0.9f), 6f, true, false, true, false);
                AddDetailedCrate(new Vector3(0.45f, 0.35f, 0.05f), new Vector3(0.7f, 0.7f, 0.7f), -8f, true, true, false, true);
                AddDetailedCrate(new Vector3(-0.35f, 1.25f, 0.0f), new Vector3(0.65f, 0.65f, 0.65f), 22f, false, false, false, false);

                // 底部木托垫木方
                trimBuf.AddBox(new Vector3(-0.45f * scale, 0.03f * scale, -0.05f * scale), new Vector3(0.92f * scale, 0.06f * scale, 0.12f * scale), Color.white);
                trimBuf.AddBox(new Vector3(0.45f * scale, 0.03f * scale, 0.05f * scale), new Vector3(0.72f * scale, 0.06f * scale, 0.12f * scale), Color.white);
            }
            else if (v == 1)
            {
                // 2大箱堆叠 (重型货箱 + 顶部错位箱)
                AddDetailedCrate(new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 1.1f, 1.1f), 0f, true, true, true, false);
                AddDetailedCrate(new Vector3(0.1f, 1.45f, -0.05f), new Vector3(0.75f, 0.7f, 0.75f), -14f, true, false, false, true);
            }
            else
            {
                // 4箱分散错落堆叠
                AddDetailedCrate(new Vector3(-0.5f, 0.4f, -0.2f), new Vector3(0.8f, 0.8f, 0.8f), -5f, true, false, true, false);
                AddDetailedCrate(new Vector3(0.4f, 0.45f, -0.1f), new Vector3(0.9f, 0.9f, 0.9f), 8f, true, true, false, true);
                AddDetailedCrate(new Vector3(-0.05f, 0.25f, 0.45f), new Vector3(0.7f, 0.5f, 0.7f), -12f, false, false, false, false);
                AddDetailedCrate(new Vector3(-0.1f, 1.15f, 0.1f), new Vector3(0.6f, 0.6f, 0.6f), 25f, true, false, false, false);
            }

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("CrateCluster_Wood"), ArenaPropSurface.Matte, pal.Wood, true));
            output.Add(new ArenaPropMeshPart(plankBuf.ToMesh("CrateCluster_Planks"), ArenaPropSurface.Matte, pal.WoodLight, false));
            output.Add(new ArenaPropMeshPart(trimBuf.ToMesh("CrateCluster_Trim"), ArenaPropSurface.Matte, pal.WoodDark, false));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("CrateCluster_Metal"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(strapBuf.ToMesh("CrateCluster_Straps"), ArenaPropSurface.Metal, Color.white, false));
            output.Add(new ArenaPropMeshPart(stencilBuf.ToMesh("CrateCluster_Stencil"), ArenaPropSurface.Matte, pal.ClothWhite, false));
        }
        #endregion

        #region 2. CrateTall (1.4m 高木箱 + 顶部小箱：双段 X 斜撑、打包带与箭头模板喷漆)
        private static void BuildCrateTall(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var trimBuf = new MeshBuffer();
            var plankBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();
            var strapBuf = new MeshBuffer();
            var stencilBuf = new MeshBuffer();

            // 主高箱 (1.0 x 1.4 x 1.0m)
            Matrix4x4 mBase = Matrix4x4.TRS(new Vector3(0f, 0.7f * scale, 0f), Quaternion.identity, new Vector3(1.0f * scale, 1.4f * scale, 1.0f * scale));
            woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, mBase);

            // 四角立柱
            float postW = 0.08f;
            trimBuf.AddBox(new Vector3(0.46f, 0f, 0.46f), new Vector3(postW, 1.01f, postW), Color.white, mBase);
            trimBuf.AddBox(new Vector3(-0.46f, 0f, 0.46f), new Vector3(postW, 1.01f, postW), Color.white, mBase);
            trimBuf.AddBox(new Vector3(0.46f, 0f, -0.46f), new Vector3(postW, 1.01f, postW), Color.white, mBase);
            trimBuf.AddBox(new Vector3(-0.46f, 0f, -0.46f), new Vector3(postW, 1.01f, postW), Color.white, mBase);

            // 顶、中、底三道环向厚实横梁
            float[] railYs = { -0.46f, 0f, 0.46f };
            for (int r = 0; r < 3; r++)
            {
                float ry = railYs[r];
                trimBuf.AddBox(new Vector3(0f, ry, 0.505f), new Vector3(0.86f, 0.07f, 0.02f), Color.white, mBase);
                trimBuf.AddBox(new Vector3(0f, ry, -0.505f), new Vector3(0.86f, 0.07f, 0.02f), Color.white, mBase);
                trimBuf.AddBox(new Vector3(0.505f, ry, 0f), new Vector3(0.02f, 0.07f, 0.86f), Color.white, mBase);
                trimBuf.AddBox(new Vector3(-0.505f, ry, 0f), new Vector3(0.02f, 0.07f, 0.86f), Color.white, mBase);
            }

            // 5 道水平横板条 (四面)
            for (int i = 0; i < 5; i++)
            {
                float py = -0.38f + i * 0.19f;
                plankBuf.AddBox(new Vector3(0f, py, 0.503f), new Vector3(0.84f, 0.14f, 0.008f), Color.white, mBase);
                plankBuf.AddBox(new Vector3(0f, py, -0.503f), new Vector3(0.84f, 0.14f, 0.008f), Color.white, mBase);
                plankBuf.AddBox(new Vector3(0.503f, py, 0f), new Vector3(0.008f, 0.14f, 0.84f), Color.white, mBase);
                plankBuf.AddBox(new Vector3(-0.503f, py, 0f), new Vector3(0.008f, 0.14f, 0.84f), Color.white, mBase);
            }

            // 8 个三向金属包角
            float cSz = 0.12f;
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        metalBuf.AddBox(new Vector3(cx * 0.48f, cy * 0.48f, cz * 0.48f), new Vector3(cSz, cSz, cSz), Color.white, mBase);
                    }
                }
            }

            // 上下两道金属/黄色打包带
            int v = Mathf.Abs(variant) % 3;
            Color strapCol = (v == 1) ? pal.HazardYellow : pal.SteelBlack;
            strapBuf.AddBox(new Vector3(0f, 0.23f, 0f), new Vector3(1.025f, 0.04f, 1.025f), strapCol, mBase);
            strapBuf.AddBox(new Vector3(0f, -0.23f, 0f), new Vector3(1.025f, 0.04f, 1.025f), strapCol, mBase);
            strapBuf.AddBox(new Vector3(0f, 0.23f, 0.518f), new Vector3(0.07f, 0.06f, 0.015f), pal.Steel, mBase);
            strapBuf.AddBox(new Vector3(0f, -0.23f, 0.518f), new Vector3(0.07f, 0.06f, 0.015f), pal.Steel, mBase);

            // 正面喷印大号向上箭头与 CARGO 模板块
            stencilBuf.AddBox(new Vector3(0f, 0.12f, 0.512f), new Vector3(0.08f, 0.14f, 0.005f), Color.white, mBase);
            stencilBuf.AddQuad(new Vector3(-0.14f, 0.19f, 0.512f), new Vector3(0.14f, 0.19f, 0.512f),
                               new Vector3(0f, 0.36f, 0.512f), new Vector3(0f, 0.36f, 0.512f), Color.white, mBase);
            stencilBuf.AddBox(new Vector3(0f, -0.12f, 0.512f), new Vector3(0.42f, 0.12f, 0.005f), Color.white, mBase);

            // 顶部小箱 (0.65 x 0.5 x 0.65m，错位旋转)
            float topYaw = (v == 0) ? 12f : ((v == 1) ? -18f : 8f);
            Matrix4x4 mTop = Matrix4x4.TRS(new Vector3(0.05f * scale, 1.65f * scale, -0.05f * scale), Quaternion.Euler(0f, topYaw, 0f), new Vector3(0.65f * scale, 0.5f * scale, 0.65f * scale));
            woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, mTop);

            // 顶箱角柱与边条
            trimBuf.AddBox(new Vector3(0f, 0f, 0.505f), new Vector3(1.02f, 0.10f, 0.02f), Color.white, mTop);
            trimBuf.AddBox(new Vector3(0f, 0f, -0.505f), new Vector3(1.02f, 0.10f, 0.02f), Color.white, mTop);
            plankBuf.AddBox(new Vector3(0f, 0.20f, 0.503f), new Vector3(0.85f, 0.12f, 0.008f), Color.white, mTop);
            plankBuf.AddBox(new Vector3(0f, -0.20f, 0.503f), new Vector3(0.85f, 0.12f, 0.008f), Color.white, mTop);

            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        metalBuf.AddBox(new Vector3(cx * 0.48f, cy * 0.48f, cz * 0.48f), new Vector3(cSz, cSz, cSz), Color.white, mTop);
                    }
                }
            }
            strapBuf.AddBox(new Vector3(0f, 0f, 0f), new Vector3(1.03f, 0.04f, 1.03f), strapCol, mTop);

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("CrateTall_Wood"), ArenaPropSurface.Matte, pal.Wood, true));
            output.Add(new ArenaPropMeshPart(plankBuf.ToMesh("CrateTall_Planks"), ArenaPropSurface.Matte, pal.WoodLight, false));
            output.Add(new ArenaPropMeshPart(trimBuf.ToMesh("CrateTall_Trim"), ArenaPropSurface.Matte, pal.WoodDark, false));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("CrateTall_Metal"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(strapBuf.ToMesh("CrateTall_Straps"), ArenaPropSurface.Metal, Color.white, false));
            output.Add(new ArenaPropMeshPart(stencilBuf.ToMesh("CrateTall_Stencil"), ArenaPropSurface.Matte, pal.ClothWhite, false));
        }
        #endregion

        #region 3. BarrelCluster (油桶群：双道凸起环箍、沉槽桶盖与注油孔、木托盘与防溢方箱)
        private static void BuildBarrelCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var barrelBuf = new MeshBuffer();
            var hoopBuf = new MeshBuffer();
            var woodBuf = new MeshBuffer();
            var accentBuf = new MeshBuffer();

            int v = Mathf.Abs(variant) % 3;
            Color mainColor = (v == 0) ? pal.ContainerTeal : ((v == 1) ? pal.ContainerRust : pal.ContainerMustard);
            Color accentColor = (v == 0) ? pal.ContainerRust : ((v == 1) ? pal.HazardYellow : pal.ContainerTeal);

            void AddSingleBarrel(Vector3 basePos, bool isAccent = false)
            {
                float r = 0.28f * scale;
                float h = 0.88f * scale;
                Vector3 b = basePos * scale;
                MeshBuffer targetBuf = isAccent ? accentBuf : barrelBuf;

                // 桶身 (12 段圆滑圆柱，Solid = true)
                targetBuf.AddCylinder(b, r, h, 12, Color.white, false, true);

                // 上下卷边边缘 (Chimes)
                float chimeR = r * 1.04f;
                hoopBuf.AddCylinder(b + Vector3.up * (0.01f * scale), chimeR, 0.04f * scale, 12, Color.white, false, false);
                hoopBuf.AddCylinder(b + Vector3.up * (h - 0.04f * scale), chimeR, 0.04f * scale, 12, Color.white, false, false);

                // 中间两道凸出加强滚箍 (Rolling Hoops)
                float hoopR = r * 1.06f;
                hoopBuf.AddCylinder(b + Vector3.up * (0.28f * scale), hoopR, 0.035f * scale, 12, Color.white, false, false);
                hoopBuf.AddCylinder(b + Vector3.up * (0.58f * scale), hoopR, 0.035f * scale, 12, Color.white, false, false);

                // 内凹沉坑顶盖 (凹入 0.03m)
                targetBuf.AddDisk(b + Vector3.up * (h - 0.03f * scale), r * 0.96f, 12, Vector3.up, Color.white);

                // 桶盖上的注油孔凸盖 (2" 大注油盖 + 3/4" 小排气盖)
                hoopBuf.AddCylinder(b + new Vector3(0.12f, h - 0.03f, 0f) * scale, 0.035f * scale, 0.02f * scale, 8, Color.white, true, false);
                hoopBuf.AddCylinder(b + new Vector3(-0.12f, h - 0.03f, 0f) * scale, 0.022f * scale, 0.016f * scale, 8, Color.white, true, false);
            }

            if (v == 0)
            {
                // 3 桶正三角 + 底部垫木托盘 (Pallet)
                AddSingleBarrel(new Vector3(-0.30f, 0.08f, -0.18f), false);
                AddSingleBarrel(new Vector3(0.30f, 0.08f, -0.18f), false);
                AddSingleBarrel(new Vector3(0.0f, 0.08f, 0.32f), true);

                // 实木托盘底座 (3 根底木梁 + 5 根上铺板)
                woodBuf.AddBox(new Vector3(0f, 0.04f * scale, 0f), new Vector3(1.1f * scale, 0.08f * scale, 1.1f * scale), Color.white);
            }
            else if (v == 1)
            {
                // 4 桶 (底 3 + 顶 1 倾斜黄色警示桶)
                AddSingleBarrel(new Vector3(-0.32f, 0f, -0.20f), false);
                AddSingleBarrel(new Vector3(0.32f, 0f, -0.20f), false);
                AddSingleBarrel(new Vector3(0.0f, 0f, 0.32f), false);
                AddSingleBarrel(new Vector3(0.0f, 0.88f, 0.04f), true);
            }
            else
            {
                // 3 桶斜列 + 侧边 1 个黄色防溢方桶
                AddSingleBarrel(new Vector3(-0.45f, 0f, -0.25f), false);
                AddSingleBarrel(new Vector3(0.0f, 0f, 0.0f), false);
                AddSingleBarrel(new Vector3(0.45f, 0f, 0.25f), false);

                // 黄色便携方油桶
                accentBuf.AddBox(new Vector3(0.35f * scale, 0.22f * scale, -0.35f * scale), new Vector3(0.30f * scale, 0.44f * scale, 0.30f * scale), Color.white);
                hoopBuf.AddBox(new Vector3(0.35f * scale, 0.47f * scale, -0.35f * scale), new Vector3(0.04f * scale, 0.06f * scale, 0.16f * scale), Color.white);
            }

            output.Add(new ArenaPropMeshPart(barrelBuf.ToMesh("BarrelCluster_Body"), ArenaPropSurface.Matte, mainColor, true));
            if (accentBuf.Vertices.Count > 0)
            {
                output.Add(new ArenaPropMeshPart(accentBuf.ToMesh("BarrelCluster_Accent"), ArenaPropSurface.Matte, accentColor, false));
            }
            output.Add(new ArenaPropMeshPart(hoopBuf.ToMesh("BarrelCluster_Hoops"), ArenaPropSurface.Metal, pal.SteelDark, false));
            if (woodBuf.Vertices.Count > 0)
            {
                output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("BarrelCluster_Pallet"), ArenaPropSurface.Matte, pal.Wood, false));
            }
        }
        #endregion

        #region 4. PlanterBox (倒角混凝土花坛：外立面内凹装饰板、深色土壤、分层饱满灌木与微型花朵)
        private static void BuildPlanterBox(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var concreteBuf = new MeshBuffer();
            var trimBuf = new MeshBuffer();
            var soilBuf = new MeshBuffer();
            var foliageBuf = new MeshBuffer();
            var flowerBuf = new MeshBuffer();

            float h = 0.85f * scale;
            float sz = 1.35f * scale;

            // 混凝土花坛主体 (Solid = true，严格限定主体包围盒)
            concreteBuf.AddBox(new Vector3(0f, h * 0.45f, 0f), new Vector3(sz, h * 0.9f, sz), Color.white);

            // 顶部外翻厚实边檐 (Solid = false)
            trimBuf.AddBox(new Vector3(0f, h - 0.05f * scale, 0f), new Vector3(sz * 1.08f, 0.10f * scale, sz * 1.08f), Color.white);

            // 四面内凹装饰板槽 (增强建筑立体感)
            float panelW = sz * 0.70f;
            float panelH = h * 0.50f;
            trimBuf.AddBox(new Vector3(0f, h * 0.45f, sz * 0.505f), new Vector3(panelW, panelH, 0.015f * scale), Color.white);
            trimBuf.AddBox(new Vector3(0f, h * 0.45f, -sz * 0.505f), new Vector3(panelW, panelH, 0.015f * scale), Color.white);
            trimBuf.AddBox(new Vector3(sz * 0.505f, h * 0.45f, 0f), new Vector3(0.015f * scale, panelH, panelW), Color.white);
            trimBuf.AddBox(new Vector3(-sz * 0.505f, h * 0.45f, 0f), new Vector3(0.015f * scale, panelH, panelW), Color.white);

            // 内凹湿润土壤面 (沉入 0.08m，Solid = false)
            soilBuf.AddBox(new Vector3(0f, h - 0.08f * scale, 0f), new Vector3(sz * 0.88f, 0.04f * scale, sz * 0.88f), Color.white);

            // 分层饱满圆润多边形灌木球 (带受光渐变顶点色)
            void AddDenseBush(Vector3 center, float radius, float height, float baseShade)
            {
                int segs = 10;
                Vector3 b = center * scale;
                float r = radius * scale;
                float ht = height * scale;
                int start = foliageBuf.Vertices.Count;

                foliageBuf.Vertices.Add(b);
                foliageBuf.Normals.Add(Vector3.down);
                foliageBuf.UVs.Add(new Vector2(0.5f, 0f));
                foliageBuf.Colors.Add(new Color(baseShade * 0.65f, baseShade * 0.65f, baseShade * 0.65f, 1f));

                for (int ring = 1; ring <= 3; ring++)
                {
                    float yFrac = ring == 1 ? 0.35f : (ring == 2 ? 0.70f : 0.90f);
                    float ringR = ring == 1 ? r * 0.95f : (ring == 2 ? r : r * 0.70f);
                    float tint = ring == 1 ? baseShade * 0.85f : (ring == 2 ? baseShade * 1.05f : baseShade * 1.25f);
                    Color c = new Color(tint, tint, tint, 1f);

                    for (int i = 0; i < segs; i++)
                    {
                        float ang = i * Mathf.PI * 2f / segs;
                        Vector3 pos = b + new Vector3(Mathf.Cos(ang) * ringR, ht * yFrac, Mathf.Sin(ang) * ringR);
                        foliageBuf.Vertices.Add(pos);
                        foliageBuf.Normals.Add((pos - b - Vector3.up * (ht * 0.45f)).normalized);
                        foliageBuf.UVs.Add(new Vector2((float)i / segs, yFrac));
                        foliageBuf.Colors.Add(c);
                    }
                }

                int topIdx = foliageBuf.Vertices.Count;
                foliageBuf.Vertices.Add(b + Vector3.up * ht);
                foliageBuf.Normals.Add(Vector3.up);
                foliageBuf.UVs.Add(new Vector2(0.5f, 1f));
                foliageBuf.Colors.Add(new Color(baseShade * 1.35f, baseShade * 1.35f, baseShade * 1.35f, 1f));

                int r1 = start + 1;
                int r2 = r1 + segs;
                int r3 = r2 + segs;

                for (int i = 0; i < segs; i++)
                {
                    int nxt = (i + 1) % segs;
                    foliageBuf.Triangles.Add(start);
                    foliageBuf.Triangles.Add(r1 + nxt);
                    foliageBuf.Triangles.Add(r1 + i);

                    foliageBuf.Triangles.Add(r1 + i);
                    foliageBuf.Triangles.Add(r1 + nxt);
                    foliageBuf.Triangles.Add(r2 + nxt);
                    foliageBuf.Triangles.Add(r1 + i);
                    foliageBuf.Triangles.Add(r2 + nxt);
                    foliageBuf.Triangles.Add(r2 + i);

                    foliageBuf.Triangles.Add(r2 + i);
                    foliageBuf.Triangles.Add(r2 + nxt);
                    foliageBuf.Triangles.Add(r3 + nxt);
                    foliageBuf.Triangles.Add(r2 + i);
                    foliageBuf.Triangles.Add(r3 + nxt);
                    foliageBuf.Triangles.Add(r3 + i);

                    foliageBuf.Triangles.Add(r3 + i);
                    foliageBuf.Triangles.Add(r3 + nxt);
                    foliageBuf.Triangles.Add(topIdx);
                }
            }

            void AddFlower(Vector3 pos, float flSz)
            {
                flowerBuf.AddDisk(pos * scale, flSz * scale, 5, Vector3.up, Color.white);
            }

            int v = Mathf.Abs(variant) % 3;
            if (v == 0)
            {
                // 3 团高低灌木 + 少量白花点缀
                AddDenseBush(new Vector3(-0.25f, h * 0.95f, -0.22f), 0.35f, 0.48f, 1.0f);
                AddDenseBush(new Vector3(0.26f, h * 0.95f, 0.16f), 0.40f, 0.55f, 1.15f);
                AddDenseBush(new Vector3(-0.08f, h * 0.95f, 0.28f), 0.30f, 0.42f, 0.92f);

                AddFlower(new Vector3(-0.20f, h * 0.95f + 0.42f, -0.15f), 0.05f);
                AddFlower(new Vector3(0.22f, h * 0.95f + 0.50f, 0.20f), 0.05f);
                AddFlower(new Vector3(0.32f, h * 0.95f + 0.38f, 0.05f), 0.04f);
                AddFlower(new Vector3(-0.05f, h * 0.95f + 0.38f, 0.24f), 0.045f);
            }
            else if (v == 1)
            {
                // 2 团精修几何修剪灌木球 + 黄色小花点缀
                AddDenseBush(new Vector3(-0.26f, h * 0.95f, 0f), 0.38f, 0.52f, 1.10f);
                AddDenseBush(new Vector3(0.26f, h * 0.95f, 0f), 0.38f, 0.52f, 1.05f);

                AddFlower(new Vector3(-0.24f, h * 0.95f + 0.48f, 0.08f), 0.05f);
                AddFlower(new Vector3(0.24f, h * 0.95f + 0.48f, -0.08f), 0.05f);
            }
            else
            {
                // 1 团大中心灌木 + 2 丛低矮阔叶灌木
                AddDenseBush(new Vector3(0f, h * 0.95f, 0f), 0.45f, 0.60f, 1.15f);
                AddDenseBush(new Vector3(-0.35f, h * 0.95f, -0.25f), 0.26f, 0.32f, 0.90f);
                AddDenseBush(new Vector3(0.32f, h * 0.95f, 0.25f), 0.28f, 0.35f, 0.95f);

                AddFlower(new Vector3(0.05f, h * 0.95f + 0.56f, 0.05f), 0.05f);
                AddFlower(new Vector3(-0.10f, h * 0.95f + 0.52f, -0.10f), 0.045f);
            }

            output.Add(new ArenaPropMeshPart(concreteBuf.ToMesh("PlanterBox_Concrete"), ArenaPropSurface.Matte, pal.ConcreteMid, true));
            output.Add(new ArenaPropMeshPart(trimBuf.ToMesh("PlanterBox_Trim"), ArenaPropSurface.Matte, pal.ConcreteDark, false));
            output.Add(new ArenaPropMeshPart(soilBuf.ToMesh("PlanterBox_Soil"), ArenaPropSurface.Matte, pal.ConcreteDark, false));
            output.Add(new ArenaPropMeshPart(foliageBuf.ToMesh("PlanterBox_Foliage"), ArenaPropSurface.Matte, pal.Foliage, false));
            if (flowerBuf.Vertices.Count > 0)
            {
                Color flColor = (v == 1) ? pal.FlowerYellow : pal.ClothWhite;
                output.Add(new ArenaPropMeshPart(flowerBuf.ToMesh("PlanterBox_Flowers"), ArenaPropSurface.Matte, flColor, false));
            }
        }
        #endregion

        #region 5. ConeCluster (交通锥群：倒角双层橡胶底座、双道高对比反光带、空心抓握顶环)
        private static void BuildConeCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var orangeBuf = new MeshBuffer();
            var whiteBuf = new MeshBuffer();
            var baseBuf = new MeshBuffer();

            Color coneOrange = new Color(1.0f, 0.42f, 0.08f, 1f);

            void AddSingleCone(Vector3 pos, float yaw, float tilt = 0f)
            {
                Vector3 p = pos * scale;
                Quaternion rot = Quaternion.Euler(tilt, yaw, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(p, rot, Vector3.one * scale);

                // 1. 厚实双台阶防滑方形底座 (带倒角切面)
                baseBuf.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(0.38f, 0.04f, 0.38f), Color.white, trs);
                baseBuf.AddBox(new Vector3(0f, 0.045f, 0f), new Vector3(0.30f, 0.015f, 0.30f), Color.white, trs);

                // 2. 锥体下段 (橙)
                orangeBuf.AddCone(new Vector3(0f, 0.05f, 0f), 0.15f, 0.11f, 0.20f, 10, Color.white, false, false, trs);
                // 3. 第一道反光带 (下宽白)
                whiteBuf.AddCone(new Vector3(0f, 0.25f, 0f), 0.11f, 0.085f, 0.14f, 10, Color.white, false, false, trs);
                // 4. 中间橙色隔离圈
                orangeBuf.AddCone(new Vector3(0f, 0.39f, 0f), 0.085f, 0.07f, 0.07f, 10, Color.white, false, false, trs);
                // 5. 第二道反光带 (上窄白)
                whiteBuf.AddCone(new Vector3(0f, 0.46f, 0f), 0.07f, 0.055f, 0.08f, 10, Color.white, false, false, trs);
                // 6. 锥体上段 (橙)
                orangeBuf.AddCone(new Vector3(0f, 0.54f, 0f), 0.055f, 0.035f, 0.12f, 10, Color.white, false, false, trs);
                // 7. 顶部外翻抓握提手凸环
                orangeBuf.AddCylinder(new Vector3(0f, 0.66f, 0f), 0.042f, 0.035f, 10, Color.white, true, false, trs);
            }

            int v = Mathf.Abs(variant) % 3;
            if (v == 0)
            {
                // 经典 3 锥不等边三角摆放，其中 1 个微倾斜
                AddSingleCone(new Vector3(0f, 0f, 0f), 0f, 0f);
                AddSingleCone(new Vector3(-0.35f, 0f, 0.22f), 35f, 4f);
                AddSingleCone(new Vector3(0.32f, 0f, 0.18f), -42f, 0f);
            }
            else if (v == 1)
            {
                // 2 锥竖立 + 1 锥横卧在地面 (生动现场施工感)
                AddSingleCone(new Vector3(-0.25f, 0f, -0.15f), 15f, 0f);
                AddSingleCone(new Vector3(0.35f, 0f, 0.10f), -20f, 0f);

                // 横倒的交通锥 (绕 X 轴倒下 86°)
                Vector3 fallenPos = new Vector3(-0.10f * scale, 0.12f * scale, 0.30f * scale);
                Quaternion fallenRot = Quaternion.Euler(86f, 55f, 0f);
                Matrix4x4 trsFallen = Matrix4x4.TRS(fallenPos, fallenRot, Vector3.one * scale);

                baseBuf.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(0.38f, 0.04f, 0.38f), Color.white, trsFallen);
                orangeBuf.AddCone(new Vector3(0f, 0.05f, 0f), 0.15f, 0.11f, 0.20f, 10, Color.white, false, false, trsFallen);
                whiteBuf.AddCone(new Vector3(0f, 0.25f, 0f), 0.11f, 0.085f, 0.14f, 10, Color.white, false, false, trsFallen);
                orangeBuf.AddCone(new Vector3(0f, 0.39f, 0f), 0.085f, 0.07f, 0.07f, 10, Color.white, false, false, trsFallen);
                whiteBuf.AddCone(new Vector3(0f, 0.46f, 0f), 0.07f, 0.055f, 0.08f, 10, Color.white, false, false, trsFallen);
                orangeBuf.AddCone(new Vector3(0f, 0.54f, 0f), 0.055f, 0.035f, 0.12f, 10, Color.white, false, true, trsFallen);
            }
            else
            {
                // 3 锥成弧线排布
                AddSingleCone(new Vector3(-0.42f, 0f, -0.22f), -12f, 0f);
                AddSingleCone(new Vector3(0.0f, 0f, 0.0f), 18f, 0f);
                AddSingleCone(new Vector3(0.42f, 0f, 0.25f), -28f, 0f);
            }

            output.Add(new ArenaPropMeshPart(orangeBuf.ToMesh("ConeCluster_Orange"), ArenaPropSurface.Matte, coneOrange, false));
            output.Add(new ArenaPropMeshPart(whiteBuf.ToMesh("ConeCluster_White"), ArenaPropSurface.Cloth, pal.ClothWhite, false));
            output.Add(new ArenaPropMeshPart(baseBuf.ToMesh("ConeCluster_Base"), ArenaPropSurface.Matte, pal.SteelBlack, false));
        }
        #endregion

        #region 6. BollardRow (码头系缆桩：加粗圆润铸钢桩体、蘑菇顶盖/T型头、底座法兰与地脚螺栓、双耳牛角)
        private static void BuildBollardRow(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var yellowBuf = new MeshBuffer();

            float totalLength = Mathf.Max(scale, 1.5f);
            float spacing = 1.5f;
            int count = Mathf.Max(2, Mathf.RoundToInt(totalLength / spacing) + 1);
            float step = totalLength / (count - 1);

            int v = Mathf.Abs(variant) % 3;

            for (int k = 0; k < count; k++)
            {
                Vector3 basePos = new Vector3(0f, 0f, k * step);
                float r = 0.16f; // 加粗桩身 (直径 0.32m)
                float h = 0.68f;

                // 1. 底部加厚圆形法兰盘
                metalBuf.AddCylinder(basePos, r * 1.45f, 0.06f, 10, Color.white, true, true);

                // 2. 6 颗凸起地脚六角螺母 (围绕法兰盘)
                for (int b = 0; b < 6; b++)
                {
                    float bang = b * Mathf.PI * 2f / 6;
                    Vector3 bPos = basePos + new Vector3(Mathf.Cos(bang) * r * 1.25f, 0.06f, Mathf.Sin(bang) * r * 1.25f);
                    metalBuf.AddBox(bPos + Vector3.up * 0.015f, new Vector3(0.035f, 0.03f, 0.035f), Color.white);
                }

                // 3. 主圆柱身 (10 段圆柱)
                metalBuf.AddCylinder(basePos + Vector3.up * 0.06f, r, h - 0.06f, 10, Color.white, false, false);

                // 4. 双侧横向系缆牛角 (Cross Horns)
                metalBuf.AddBox(basePos + new Vector3(0f, h * 0.72f, 0f), new Vector3(r * 2.6f, 0.07f, 0.07f), Color.white);
                metalBuf.AddCylinder(basePos + new Vector3(r * 1.3f, h * 0.72f, 0f), 0.055f, 0.02f, 8, Color.white, true, true);
                metalBuf.AddCylinder(basePos + new Vector3(-r * 1.3f, h * 0.72f, 0f), 0.055f, 0.02f, 8, Color.white, true, true);

                // 5. 顶盖造型 (随 variant 变化)
                if (v == 0)
                {
                    // 经典蘑菇圆弧顶盖 (外翻下檐 + 球顶)
                    metalBuf.AddCylinder(basePos + Vector3.up * h, r * 1.22f, 0.04f, 10, Color.white, false, false);
                    metalBuf.AddCone(basePos + Vector3.up * (h + 0.04f), r * 1.22f, 0.03f, 0.09f, 10, Color.white, false, true);
                }
                else if (v == 1)
                {
                    // T-Head 横梁型集装箱码头重型桩
                    metalBuf.AddBox(basePos + Vector3.up * (h + 0.05f), new Vector3(r * 2.8f, 0.10f, r * 1.4f), Color.white);
                }
                else
                {
                    // 双阶倒角防脱顶盖
                    metalBuf.AddCylinder(basePos + Vector3.up * h, r * 1.28f, 0.05f, 10, Color.white, true, false);
                    metalBuf.AddCylinder(basePos + Vector3.up * (h + 0.05f), r * 1.05f, 0.06f, 10, Color.white, true, true);
                }

                // 6. 黄色反光警戒带
                yellowBuf.AddCylinder(basePos + Vector3.up * (h * 0.45f), r * 1.025f, 0.12f, 10, Color.white, false, false);
            }

            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("BollardRow_Metal"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(yellowBuf.ToMesh("BollardRow_Yellow"), ArenaPropSurface.Matte, pal.HazardYellow, false));
        }
        #endregion

        #region 7. Bench (木条长椅：5根圆润木板条座面、倾斜靠背、加粗铸铁腿架与中央防滑中肋)
        private static void BuildBench(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();

            float benchW = 1.8f * scale;
            int v = Mathf.Abs(variant) % 3;

            // 1. 木板座面 (5 根带倒角感实木板条，Solid = true)
            for (int i = 0; i < 5; i++)
            {
                float zOffset = (-0.20f + i * 0.095f) * scale;
                woodBuf.AddBox(new Vector3(0f, 0.42f * scale, zOffset), new Vector3(benchW, 0.035f * scale, 0.075f * scale), Color.white);
            }

            // 2. 倾斜靠背木条 (4 根倾斜后仰木条，顶部板条稍微加厚)
            for (int i = 0; i < 4; i++)
            {
                float yOffset = (0.53f + i * 0.10f) * scale;
                float zOffset = (-0.22f - i * 0.035f) * scale;
                float slatW = (i == 3) ? 0.095f * scale : 0.075f * scale;
                woodBuf.AddBox(new Vector3(0f, yOffset, zOffset), new Vector3(benchW, slatW, 0.03f * scale), Color.white);
            }

            // 3. 铸铁椅腿架构 (左右两副 + 中央加固支撑中肋，圆润厚重)
            void AddLegFrame(float x, bool isArmrest)
            {
                Vector3 legPos = new Vector3(x * scale, 0f, 0f);
                // 地面固定横底座
                metalBuf.AddBox(legPos + new Vector3(0f, 0.03f * scale, 0.02f * scale), new Vector3(0.06f * scale, 0.06f * scale, 0.68f * scale), Color.white);
                // 前支撑竖柱 (杆件直径 ≥ 6cm)
                metalBuf.AddBox(legPos + new Vector3(0f, 0.22f * scale, 0.18f * scale), new Vector3(0.06f * scale, 0.38f * scale, 0.06f * scale), Color.white);
                // 后支撑横杆向上延伸为靠背架
                metalBuf.AddBox(legPos + new Vector3(0f, 0.42f * scale, -0.23f * scale), new Vector3(0.06f * scale, 0.78f * scale, 0.06f * scale), Color.white);
                // 斜向三角支撑角铁
                metalBuf.AddBox(legPos + new Vector3(0f, 0.26f * scale, -0.02f * scale), new Vector3(0.05f * scale, 0.05f * scale, 0.38f * scale), Color.white);

                // 扶手 (圆润弯曲，满足直径 ≥ 6cm)
                if (isArmrest)
                {
                    metalBuf.AddBox(legPos + new Vector3(0f, 0.58f * scale, 0.02f * scale), new Vector3(0.06f * scale, 0.045f * scale, 0.48f * scale), Color.white);
                }
                else
                {
                    // 中央分隔肋
                    metalBuf.AddBox(legPos + new Vector3(0f, 0.52f * scale, 0.02f * scale), new Vector3(0.05f * scale, 0.08f * scale, 0.42f * scale), Color.white);
                }
            }

            AddLegFrame(-0.76f, true);
            AddLegFrame(0.76f, true);
            AddLegFrame(0.0f, false); // 中央加固支撑肋

            // variant 1: 靠背上方金属小广告板 / 站牌框
            if (v == 1)
            {
                metalBuf.AddBox(new Vector3(0f, 0.94f * scale, -0.34f * scale), new Vector3(0.75f * scale, 0.20f * scale, 0.04f * scale), Color.white);
            }

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("Bench_Wood"), ArenaPropSurface.Matte, pal.Wood, true));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("Bench_Metal"), ArenaPropSurface.Metal, pal.SteelDark, true));
        }
        #endregion

        #region 8. AcUnit (空调外机：倾斜百叶格栅、双圆盘风扇防护罩、冷媒弯管阀门与减震工字脚)
        private static void BuildAcUnit(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalLightBuf = new MeshBuffer();
            var metalDarkBuf = new MeshBuffer();
            var yellowBuf = new MeshBuffer();

            Vector3 bodySz = new Vector3(1.10f * scale, 0.88f * scale, 0.46f * scale);
            Vector3 center = new Vector3(0f, bodySz.y * 0.5f, 0f);

            // 1. 机身金属烤漆主体 (Solid = false，因为在墙顶)
            metalLightBuf.AddBox(center, bodySz, Color.white);

            // 顶部防雨坡面披檐
            metalLightBuf.AddBox(new Vector3(0f, bodySz.y + 0.015f * scale, 0.01f * scale), new Vector3(bodySz.x * 1.04f, 0.03f * scale, bodySz.z * 1.06f), Color.white);

            // 2. 底部减震支撑脚 (2 条工字轨带地脚螺钉)
            metalDarkBuf.AddBox(new Vector3(-0.42f * scale, 0.03f * scale, 0f), new Vector3(0.12f * scale, 0.06f * scale, 0.54f * scale), Color.white);
            metalDarkBuf.AddBox(new Vector3(0.42f * scale, 0.03f * scale, 0f), new Vector3(0.12f * scale, 0.06f * scale, 0.54f * scale), Color.white);

            int v = Mathf.Abs(variant) % 3;

            void AddFanAssembly(Vector3 fCenter, float radius)
            {
                // 风扇内凹沉槽
                metalDarkBuf.AddCylinder(fCenter, radius, 0.02f * scale, 12, Color.white, true, true);
                // 外圈同心金属护罩钢环
                metalLightBuf.AddCylinder(fCenter + Vector3.forward * (0.015f * scale), radius * 0.95f, 0.015f * scale, 12, Color.white, false, false);
                metalLightBuf.AddCylinder(fCenter + Vector3.forward * (0.015f * scale), radius * 0.55f, 0.015f * scale, 12, Color.white, false, false);
                // 十字防护筋条
                metalLightBuf.AddBox(fCenter + Vector3.forward * (0.02f * scale), new Vector3(radius * 1.9f, 0.02f * scale, 0.015f * scale), Color.white);
                metalLightBuf.AddBox(fCenter + Vector3.forward * (0.02f * scale), new Vector3(0.02f * scale, radius * 1.9f, 0.015f * scale), Color.white);
                // 中心风机轴心罩
                metalLightBuf.AddCylinder(fCenter + Vector3.forward * (0.025f * scale), radius * 0.28f, 0.025f * scale, 10, Color.white, true, true);
            }

            if (v == 1)
            {
                // 工业高型双联风扇外机
                AddFanAssembly(new Vector3(-0.16f * scale, center.y + 0.18f * scale, bodySz.z * 0.505f), 0.22f * scale);
                AddFanAssembly(new Vector3(-0.16f * scale, center.y - 0.18f * scale, bodySz.z * 0.505f), 0.22f * scale);
            }
            else
            {
                // 单大风扇 + 7 道向下倾斜百叶窗
                AddFanAssembly(new Vector3(-0.16f * scale, center.y, bodySz.z * 0.505f), 0.29f * scale);
            }

            // 右侧 7 道向下倾斜凹槽百叶叶片
            for (int i = 0; i < 7; i++)
            {
                float y = center.y + (-0.28f + i * 0.092f) * scale;
                metalDarkBuf.AddBox(new Vector3(0.32f * scale, y, bodySz.z * 0.505f), new Vector3(0.34f * scale, 0.035f * scale, 0.025f * scale), Color.white);
            }

            // 侧面冷媒铜管弯头与黄铜截止阀门 (转折 90° 走向墙体)
            Vector3 pipeExit = new Vector3(-bodySz.x * 0.505f, center.y + 0.15f * scale, 0f);
            metalDarkBuf.AddCylinder(pipeExit, 0.045f * scale, 0.12f * scale, 8, Color.white, true, true);
            metalLightBuf.AddBox(pipeExit + new Vector3(-0.04f * scale, 0f, 0f), new Vector3(0.08f * scale, 0.07f * scale, 0.07f * scale), Color.white);

            // 侧面黄色高压安全警示铭牌
            yellowBuf.AddBox(new Vector3(bodySz.x * 0.505f, center.y + 0.15f * scale, 0f), new Vector3(0.01f * scale, 0.14f * scale, 0.14f * scale), Color.white);

            output.Add(new ArenaPropMeshPart(metalLightBuf.ToMesh("AcUnit_Chassis"), ArenaPropSurface.Metal, pal.Steel, false));
            output.Add(new ArenaPropMeshPart(metalDarkBuf.ToMesh("AcUnit_Details"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(yellowBuf.ToMesh("AcUnit_Warning"), ArenaPropSurface.Matte, pal.HazardYellow, false));
        }
        #endregion

        #region 9. PipeRun (工业管线：粗壮直管、分段八角法兰盘、U型双斜撑托架、醒目红色阀门手轮与警戒色环)
        private static void BuildPipeRun(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var pipeBuf = new MeshBuffer();
            var bracketBuf = new MeshBuffer();
            var valveBuf = new MeshBuffer();
            var yellowBuf = new MeshBuffer();

            float totalLength = Mathf.Max(scale, 2.0f);
            float r = 0.15f; // 加粗管道 (直径 0.30m)
            int pipeSegs = 10;

            // 1. 直管圆柱 (沿 +Z 排布，10 边型圆滑曲面)
            for (int i = 0; i < pipeSegs; i++)
            {
                float a0 = i * Mathf.PI * 2f / pipeSegs;
                float a1 = (i + 1) * Mathf.PI * 2f / pipeSegs;

                Vector3 v0 = new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, 0f);
                Vector3 v1 = new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, 0f);
                Vector3 v2 = new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, totalLength);
                Vector3 v3 = new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, totalLength);

                Vector3 n0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                Vector3 n1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);

                pipeBuf.AddQuadExplicit(v0, v1, v2, v3, n0, n1, n1, n0, Color.white);
            }

            // 2. 分段八角法兰盘 + 环形地脚螺栓 (每 3.5m 一处)
            int flangeCount = Mathf.Max(2, Mathf.RoundToInt(totalLength / 3.5f) + 1);
            float fStep = totalLength / (flangeCount - 1);
            for (int k = 0; k < flangeCount; k++)
            {
                float z = k * fStep;
                pipeBuf.AddBox(new Vector3(0f, 0f, z), new Vector3(r * 2.8f, r * 2.8f, 0.07f), Color.white);

                // 托架：U 形抱箍 + 槽钢立柱 + 双向三角形斜撑角钢
                bracketBuf.AddBox(new Vector3(0f, -0.22f, z), new Vector3(0.08f, 0.44f, 0.10f), Color.white);
                bracketBuf.AddBox(new Vector3(0f, -0.46f, z), new Vector3(0.28f, 0.05f, 0.18f), Color.white);
                // 斜撑角铁
                bracketBuf.AddBox(new Vector3(0.12f, -0.32f, z), new Vector3(0.04f, 0.22f, 0.05f), Color.white);
                bracketBuf.AddBox(new Vector3(-0.12f, -0.32f, z), new Vector3(0.04f, 0.22f, 0.05f), Color.white);
            }

            int v = Mathf.Abs(variant) % 3;

            // 3. 中部工业阀门与手轮 (记忆点！)
            float midZ = totalLength * 0.5f;
            // 阀体膨大球型铸件
            valveBuf.AddBox(new Vector3(0f, 0f, midZ), new Vector3(r * 2.4f, r * 2.4f, 0.40f), Color.white);
            // 垂直向上阀杆
            bracketBuf.AddCylinder(new Vector3(0f, r, midZ), 0.035f, 0.18f, 8, Color.white, true, true);

            if (v == 1)
            {
                // 圆形压力表盘 (带发光表盘与表针)
                bracketBuf.AddBox(new Vector3(0f, r + 0.24f, midZ), new Vector3(0.18f, 0.18f, 0.05f), Color.white);
                yellowBuf.AddDisk(new Vector3(0f, r + 0.24f, midZ + 0.026f), 0.075f, 10, Vector3.forward, Color.white);
            }
            else
            {
                // 经典八角形大红色手轮 (外圈圆环 + 4 根十字轮辐 + 中心螺母)
                Vector3 wheelCenter = new Vector3(0f, r + 0.20f, midZ);
                float wheelR = 0.16f; // 直径 0.32m
                valveBuf.AddCylinder(wheelCenter, wheelR, 0.03f, 8, Color.white, true, true);
                valveBuf.AddCylinder(wheelCenter + Vector3.up * 0.005f, wheelR * 0.72f, 0.035f, 8, pal.SteelDark, true, true);
                // 十字轮辐
                valveBuf.AddBox(wheelCenter + Vector3.up * 0.015f, new Vector3(wheelR * 1.9f, 0.025f, 0.03f), Color.white);
                valveBuf.AddBox(wheelCenter + Vector3.up * 0.015f, new Vector3(0.03f, 0.025f, wheelR * 1.9f), Color.white);
                bracketBuf.AddCylinder(wheelCenter + Vector3.up * 0.025f, 0.035f, 0.03f, 8, Color.white, true, true);
            }

            // 4. 黄色流体警戒环带 (阀门两侧)
            yellowBuf.AddCylinder(new Vector3(0f, 0f, midZ - 0.50f), r * 1.025f, 0.18f, 10, Color.white, false, false);
            yellowBuf.AddCylinder(new Vector3(0f, 0f, midZ + 0.50f), r * 1.025f, 0.18f, 10, Color.white, false, false);

            output.Add(new ArenaPropMeshPart(pipeBuf.ToMesh("PipeRun_Pipe"), ArenaPropSurface.Metal, pal.Steel, false));
            output.Add(new ArenaPropMeshPart(bracketBuf.ToMesh("PipeRun_Brackets"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(valveBuf.ToMesh("PipeRun_Valve"), ArenaPropSurface.Metal, pal.ContainerRust, false));
            output.Add(new ArenaPropMeshPart(yellowBuf.ToMesh("PipeRun_Hazard"), ArenaPropSurface.Matte, pal.HazardYellow, false));
        }
        #endregion

        #region 10. LampPost (4.2m 路灯：底座八角法兰加劲肋、加粗渐细圆滑灯杆、单臂吊灯/双臂投光灯/庭院折角灯)
        private static void BuildLampPost(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var glowBuf = new MeshBuffer();

            float totalH = 4.2f * scale;
            int v = Mathf.Abs(variant) % 3;

            // 1. 底座八角法兰盘与地脚加劲肋板 (Gussets)
            metalBuf.AddBox(new Vector3(0f, 0.06f * scale, 0f), new Vector3(0.46f * scale, 0.12f * scale, 0.46f * scale), Color.white);
            // 4 块三角形加劲肋板
            metalBuf.AddBox(new Vector3(0.18f * scale, 0.16f * scale, 0f), new Vector3(0.04f * scale, 0.20f * scale, 0.12f * scale), Color.white);
            metalBuf.AddBox(new Vector3(-0.18f * scale, 0.16f * scale, 0f), new Vector3(0.04f * scale, 0.20f * scale, 0.12f * scale), Color.white);
            metalBuf.AddBox(new Vector3(0f, 0.16f * scale, 0.18f * scale), new Vector3(0.12f * scale, 0.20f * scale, 0.04f * scale), Color.white);
            metalBuf.AddBox(new Vector3(0f, 0.16f * scale, -0.18f * scale), new Vector3(0.12f * scale, 0.20f * scale, 0.04f * scale), Color.white);

            // 离地 0.6m 处检修门板
            metalBuf.AddBox(new Vector3(0f, 0.65f * scale, 0.11f * scale), new Vector3(0.10f * scale, 0.24f * scale, 0.02f * scale), Color.white);

            // 2. 加粗渐细灯杆 (底径 0.20m -> 顶径 0.09m，10 边形圆滑圆台，满足直径 ≥ 6cm 要求)
            metalBuf.AddCone(new Vector3(0f, 0.12f * scale, 0f), 0.10f * scale, 0.045f * scale, totalH * 0.86f, 10, Color.white, false, false);

            // 3. 灯头造型 (3 种截然不同的可辨识 variant！)
            if (v == 0)
            {
                // 码头单臂大弧弯管吊灯 (钟形反射罩 + 发光凸透镜)
                Vector3 armStart = new Vector3(0f, totalH * 0.88f, 0f);
                Vector3 armMid = new Vector3(0f, totalH * 0.98f, 0.45f * scale);
                Vector3 armEnd = new Vector3(0f, totalH * 0.94f, 0.95f * scale);

                metalBuf.AddBox((armStart + armMid) * 0.5f, new Vector3(0.08f * scale, 0.08f * scale, 0.55f * scale), Color.white);
                metalBuf.AddBox((armMid + armEnd) * 0.5f, new Vector3(0.07f * scale, 0.07f * scale, 0.60f * scale), Color.white);

                // 钟形灯罩顶壳 (直径 0.44m)
                metalBuf.AddCone(armEnd + Vector3.up * (0.08f * scale), 0.22f * scale, 0.08f * scale, 0.14f * scale, 10, Color.white, true, true);
                // 朝下的下凸磨砂发光灯罩 (自发光 Emissive)
                glowBuf.AddCone(armEnd - Vector3.up * (0.04f * scale), 0.20f * scale, 0.04f * scale, 0.08f * scale, 10, Color.white, true, true);
            }
            else if (v == 1)
            {
                // 集装箱码头重型双臂投光灯塔
                Vector3 yokeCenter = new Vector3(0f, totalH * 0.90f, 0f);
                // 左右横向桁架支臂 (跨度 1.6m)
                metalBuf.AddBox(yokeCenter, new Vector3(1.5f * scale, 0.08f * scale, 0.08f * scale), Color.white);

                void AddFloodlight(float xOffset)
                {
                    Vector3 fPos = yokeCenter + new Vector3(xOffset * scale, 0.05f * scale, 0.20f * scale);
                    // 矩形投光灯箱体 + 遮光斜檐
                    metalBuf.AddBox(fPos, new Vector3(0.42f * scale, 0.28f * scale, 0.22f * scale), Color.white);
                    metalBuf.AddBox(fPos + Vector3.up * (0.16f * scale), new Vector3(0.44f * scale, 0.04f * scale, 0.28f * scale), Color.white);
                    // 前置自发光发光板
                    glowBuf.AddBox(fPos + Vector3.forward * (0.115f * scale), new Vector3(0.36f * scale, 0.22f * scale, 0.02f * scale), Color.white);
                }

                AddFloodlight(-0.65f);
                AddFloodlight(0.65f);
            }
            else
            {
                // 潮流街区庭院直角折管灯
                Vector3 armStart = new Vector3(0f, totalH * 0.88f, 0f);
                Vector3 armTop = new Vector3(0f, totalH * 1.02f, 0f);
                Vector3 armEnd = new Vector3(0f, totalH * 1.02f, 0.65f * scale);

                metalBuf.AddBox((armStart + armTop) * 0.5f, new Vector3(0.08f * scale, 0.30f * scale, 0.08f * scale), Color.white);
                metalBuf.AddBox((armTop + armEnd) * 0.5f, new Vector3(0.07f * scale, 0.07f * scale, 0.68f * scale), Color.white);

                // 倒梯形现代灯头
                Vector3 headPos = armEnd - Vector3.up * (0.10f * scale);
                metalBuf.AddBox(headPos, new Vector3(0.24f * scale, 0.16f * scale, 0.32f * scale), Color.white);
                // 底部平整自发光透镜
                glowBuf.AddBox(headPos - Vector3.up * (0.085f * scale), new Vector3(0.20f * scale, 0.02f * scale, 0.28f * scale), Color.white);
            }

            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("LampPost_Metal"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(glowBuf.ToMesh("LampPost_Glass"), ArenaPropSurface.Emissive, pal.LampGlass, false));
        }
        #endregion

        #region 11. PalmTree (5m 棕榈树：微弯节疤树干、膨大根基、双层丰满下垂大叶冠与顶部椰子簇)
        private static void BuildPalmTree(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var trunkBuf = new MeshBuffer();
            var leafBuf = new MeshBuffer();
            var leafDarkBuf = new MeshBuffer();
            var coconutBuf = new MeshBuffer();

            int v = Mathf.Abs(variant) % 3;
            int trunkSegments = 7;
            float totalH = 5.0f * scale;
            float segH = totalH / trunkSegments;
            Vector3 prevCenter = Vector3.zero;
            float prevR = 0.32f * scale;

            // 根基膨大基座 (稳固扎根地面)
            trunkBuf.AddCylinder(Vector3.zero, prevR * 1.25f, 0.18f * scale, 10, Color.white, true, true);

            // 7 节带鳞片状外翘节环的微弯树干
            for (int i = 1; i <= trunkSegments; i++)
            {
                float t = (float)i / trunkSegments;
                float xOffset = (v == 2) ? Mathf.Sin(t * 1.8f) * 0.65f * scale : Mathf.Sin(t * 1.2f) * 0.25f * scale;
                float zOffset = (1f - Mathf.Cos(t * 1.2f)) * (v == 0 ? 0.65f : 0.40f) * scale;

                Vector3 curCenter = new Vector3(xOffset, i * segH, zOffset);
                float curR = Mathf.Lerp(0.30f, 0.14f, t) * scale;

                // 节段倒圆台 (下细上粗，形成重叠感)
                trunkBuf.AddCone(prevCenter, prevR, curR, (curCenter - prevCenter).magnitude, 10, Color.white, false, false);
                // 节疤外翘突环
                trunkBuf.AddCylinder(curCenter - Vector3.up * (0.03f * scale), curR * 1.18f, 0.05f * scale, 10, Color.white, false, false);

                prevCenter = curCenter;
                prevR = curR;
            }

            Vector3 crownCenter = prevCenter;

            // 顶部叶柄基部的 3 颗深色椰子 (记忆点！)
            coconutBuf.AddCone(crownCenter - Vector3.up * (0.15f * scale) + new Vector3(0.08f, 0f, 0.06f) * scale, 0.11f * scale, 0.04f * scale, 0.16f * scale, 8, Color.white, true, true);
            coconutBuf.AddCone(crownCenter - Vector3.up * (0.15f * scale) + new Vector3(-0.09f, 0f, 0.05f) * scale, 0.11f * scale, 0.04f * scale, 0.16f * scale, 8, Color.white, true, true);
            coconutBuf.AddCone(crownCenter - Vector3.up * (0.15f * scale) + new Vector3(0f, 0f, -0.10f) * scale, 0.11f * scale, 0.04f * scale, 0.16f * scale, 8, Color.white, true, true);

            // 10 片宽大下垂大叶冠 (4 片下层深色下垂老叶 + 6 片中上层饱满拱垂鲜叶)
            void AddDetailedFrond(MeshBuffer targetBuf, float angleDeg, float archHeight, float droopDist, float frondLen, float baseWidth)
            {
                float rad = angleDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)).normalized;
                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;

                Vector3 p0 = crownCenter;
                Vector3 p1 = crownCenter + dir * (frondLen * 0.35f * scale) + Vector3.up * (archHeight * scale);
                Vector3 p2 = crownCenter + dir * (frondLen * 0.72f * scale) - Vector3.up * (droopDist * 0.20f * scale);
                Vector3 p3 = crownCenter + dir * (frondLen * 1.05f * scale) - Vector3.up * (droopDist * scale);

                void AddFrondSection(Vector3 a, Vector3 b, float wA, float wB, float shadeA, float shadeB)
                {
                    Vector3 v0 = a - side * (wA * 0.5f);
                    Vector3 v1 = a + side * (wA * 0.5f);
                    Vector3 v2 = b + side * (wB * 0.5f);
                    Vector3 v3 = b - side * (wB * 0.5f);

                    Color cA = new Color(shadeA, shadeA, shadeA, 1f);
                    Color cB = new Color(shadeB, shadeB, shadeB, 1f);

                    int idx = targetBuf.Vertices.Count;
                    Vector3 norm = Vector3.Cross(b - a, side).normalized;

                    targetBuf.Vertices.Add(v0); targetBuf.Normals.Add(norm); targetBuf.UVs.Add(new Vector2(0f, 0f)); targetBuf.Colors.Add(cA);
                    targetBuf.Vertices.Add(v1); targetBuf.Normals.Add(norm); targetBuf.UVs.Add(new Vector2(1f, 0f)); targetBuf.Colors.Add(cA);
                    targetBuf.Vertices.Add(v2); targetBuf.Normals.Add(norm); targetBuf.UVs.Add(new Vector2(1f, 1f)); targetBuf.Colors.Add(cB);
                    targetBuf.Vertices.Add(v3); targetBuf.Normals.Add(norm); targetBuf.UVs.Add(new Vector2(0f, 1f)); targetBuf.Colors.Add(cB);

                    // 双面可见
                    targetBuf.Triangles.Add(idx); targetBuf.Triangles.Add(idx + 3); targetBuf.Triangles.Add(idx + 2);
                    targetBuf.Triangles.Add(idx); targetBuf.Triangles.Add(idx + 2); targetBuf.Triangles.Add(idx + 1);
                    targetBuf.Triangles.Add(idx); targetBuf.Triangles.Add(idx + 1); targetBuf.Triangles.Add(idx + 2);
                    targetBuf.Triangles.Add(idx); targetBuf.Triangles.Add(idx + 2); targetBuf.Triangles.Add(idx + 3);
                }

                AddFrondSection(p0, p1, 0.16f * scale, baseWidth * scale, 0.85f, 1.05f);
                AddFrondSection(p1, p2, baseWidth * scale, baseWidth * 0.80f * scale, 1.05f, 1.20f);
                AddFrondSection(p2, p3, baseWidth * 0.80f * scale, 0.05f * scale, 1.20f, 1.35f);
            }

            // 下层 4 片大角度下垂老叶 (FoliageDark)
            for (int k = 0; k < 4; k++)
            {
                float ang = k * 90f + 45f;
                AddDetailedFrond(leafDarkBuf, ang, 0.15f, 1.15f, 2.1f, 0.44f);
            }

            // 中上层 6 片舒展拱垂大叶 (Foliage)
            for (int k = 0; k < 6; k++)
            {
                float ang = k * 60f + 15f;
                AddDetailedFrond(leafBuf, ang, 0.45f, 0.80f, 2.5f, 0.52f);
            }

            output.Add(new ArenaPropMeshPart(trunkBuf.ToMesh("PalmTree_Trunk"), ArenaPropSurface.Matte, pal.WoodDark, false));
            output.Add(new ArenaPropMeshPart(leafBuf.ToMesh("PalmTree_Leaves"), ArenaPropSurface.Matte, pal.Foliage, false));
            output.Add(new ArenaPropMeshPart(leafDarkBuf.ToMesh("PalmTree_LeavesOld"), ArenaPropSurface.Matte, pal.FoliageDark, false));
            output.Add(new ArenaPropMeshPart(coconutBuf.ToMesh("PalmTree_Coconuts"), ArenaPropSurface.Matte, pal.WoodDarker, false));
        }
        #endregion

        #region 12. FlagPole (5.4m 旗杆：八角混凝土配重基座、加粗金属杆、6段正弦波浪旗与拉索滑轮)
        private static void BuildFlagPole(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var clothBuf = new MeshBuffer();
            var plinthBuf = new MeshBuffer();

            float totalH = 5.4f * scale;

            // 1. 八角加重混凝土底墩 (0.65 x 0.20 x 0.65m)
            plinthBuf.AddBox(new Vector3(0f, 0.10f * scale, 0f), new Vector3(0.65f * scale, 0.20f * scale, 0.65f * scale), Color.white);

            // 墩顶钢法兰与加劲肋板
            metalBuf.AddCylinder(new Vector3(0f, 0.20f * scale, 0f), 0.26f * scale, 0.05f * scale, 10, Color.white, true, true);
            metalBuf.AddBox(new Vector3(0.14f * scale, 0.28f * scale, 0f), new Vector3(0.03f * scale, 0.12f * scale, 0.08f * scale), Color.white);
            metalBuf.AddBox(new Vector3(-0.14f * scale, 0.28f * scale, 0f), new Vector3(0.03f * scale, 0.12f * scale, 0.08f * scale), Color.white);

            // 2. 加粗渐细金属旗杆 (底径 0.12m -> 顶径 0.064m，满足 ≥ 6cm 要求)
            metalBuf.AddCone(new Vector3(0f, 0.25f * scale, 0f), 0.06f * scale, 0.032f * scale, totalH - 0.25f * scale, 10, Color.white, false, false);

            // 杆顶滑轮箱与金球顶饰
            metalBuf.AddCone(new Vector3(0f, totalH, 0f), 0.09f * scale, 0.02f * scale, 0.14f * scale, 10, Color.white, true, true);

            // 侧面绷紧的细钢丝拉索
            metalBuf.AddBox(new Vector3(0.065f * scale, totalH * 0.50f, 0f), new Vector3(0.015f * scale, totalH * 0.90f, 0.015f * scale), Color.white);

            // 3. 悬挂队旗布面 (宽 2.0m，高 1.15m，沿 +Z 延伸，带 6 段立体正弦飘动褶皱)
            // P30 返修：颜色提亮防洗白；波浪幅度收敛并加沿程上扬弧度，让旗面"扬起"而非蔫垂
            int v = Mathf.Abs(variant) % 3;
            Color flagColor = (v == 0) ? pal.TeamAPad : ((v == 1) ? pal.TeamBPad : pal.HazardYellow);
            flagColor = Color.Lerp(flagColor, Color.white, 0.10f);

            int segments = 6;
            float flagLen = 2.0f * scale;
            float flagH = 1.15f * scale;
            float topY = totalH - 0.30f * scale;
            float botY = topY - flagH;
            Color hemTint = new Color(0.78f, 0.78f, 0.78f, 1f); // 下缘收深色包边，增强旗帜读感

            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments;
                float t1 = (float)(i + 1) / segments;
                float z0 = t0 * flagLen;
                float z1 = t1 * flagLen;

                float wave0 = Mathf.Sin((float)i * 1.35f) * 0.05f * scale;
                float wave1 = Mathf.Sin((float)(i + 1) * 1.35f) * 0.05f * scale;
                // 沿旗面长度的上扬弧度：尾部抬起 ~0.22m，风扬感
                float lift0 = t0 * t0 * 0.22f * scale;
                float lift1 = t1 * t1 * 0.22f * scale;

                Vector3 v0 = new Vector3(wave0, botY + lift0, z0);
                Vector3 v1 = new Vector3(wave1, botY + lift1, z1);
                Vector3 v2 = new Vector3(wave1, topY + lift1, z1);
                Vector3 v3 = new Vector3(wave0, topY + lift0, z0);

                int idx = clothBuf.Vertices.Count;
                Vector3 norm = Vector3.Cross(v2 - v0, v3 - v0).normalized;

                clothBuf.Vertices.Add(v0); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2(t0, 0f)); clothBuf.Colors.Add(hemTint);
                clothBuf.Vertices.Add(v1); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2(t1, 0f)); clothBuf.Colors.Add(hemTint);
                clothBuf.Vertices.Add(v2); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2(t1, 1f)); clothBuf.Colors.Add(Color.white);
                clothBuf.Vertices.Add(v3); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2(t0, 1f)); clothBuf.Colors.Add(Color.white);

                // 双面可见
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 3); clothBuf.Triangles.Add(idx + 2);
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 2); clothBuf.Triangles.Add(idx + 1);
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 1); clothBuf.Triangles.Add(idx + 2);
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 2); clothBuf.Triangles.Add(idx + 3);
            }

            output.Add(new ArenaPropMeshPart(plinthBuf.ToMesh("FlagPole_Base"), ArenaPropSurface.Matte, pal.ConcreteMid, false));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("FlagPole_Metal"), ArenaPropSurface.Metal, pal.Steel, false));
            output.Add(new ArenaPropMeshPart(clothBuf.ToMesh("FlagPole_Cloth"), ArenaPropSurface.Cloth, flagColor, false));
        }
        #endregion

        #region 13. Kiosk (售卖亭：双色条纹遮阳篷、内凹柜台木吧台、自发光招牌看板与侧壁菜单板)
        private static void BuildKiosk(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var wallBuf = new MeshBuffer();
            var woodBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();
            var stripeBuf1 = new MeshBuffer();
            var stripeBuf2 = new MeshBuffer();
            var signGlowBuf = new MeshBuffer();

            // 亭体核心尺寸 (宽 2.6m x 高 2.4m x 深 1.8m)
            Vector3 kSz = new Vector3(2.6f * scale, 2.4f * scale, 1.8f * scale);
            Vector3 center = new Vector3(0f, kSz.y * 0.5f, 0f);

            // 1. 混凝土主体墙身 (Solid = true，严格限定碰撞代理与涂墨立面)
            wallBuf.AddBox(new Vector3(0f, center.y, -0.45f * scale), new Vector3(kSz.x, kSz.y, 0.9f * scale), Color.white);
            wallBuf.AddBox(new Vector3(0f, 0.45f * scale, 0.45f * scale), new Vector3(kSz.x, 0.90f * scale, 0.9f * scale), Color.white);

            // 2. 窗口深色内凹背板 (深色开口视觉)
            wallBuf.AddBox(new Vector3(0f, 1.55f * scale, 0.10f * scale), new Vector3(kSz.x * 0.88f, 1.10f * scale, 0.05f * scale), new Color(0.18f, 0.18f, 0.18f, 1f));

            // 3. 实木售卖吧台 (厚实台面 + 金属三角支架，Solid = false)
            woodBuf.AddBox(new Vector3(0f, 0.94f * scale, 0.58f * scale), new Vector3(kSz.x * 0.98f, 0.08f * scale, 0.42f * scale), Color.white);
            // 吧台下方 3 组金属三角支架
            metalBuf.AddBox(new Vector3(-0.95f * scale, 0.80f * scale, 0.65f * scale), new Vector3(0.06f * scale, 0.20f * scale, 0.22f * scale), Color.white);
            metalBuf.AddBox(new Vector3(0.95f * scale, 0.80f * scale, 0.65f * scale), new Vector3(0.06f * scale, 0.20f * scale, 0.22f * scale), Color.white);
            metalBuf.AddBox(new Vector3(0f, 0.80f * scale, 0.65f * scale), new Vector3(0.06f * scale, 0.20f * scale, 0.22f * scale), Color.white);

            // 4. 外挑双色条纹遮阳篷 (布面 Cloth，8 道条纹交替，Solid = false)
            int v = Mathf.Abs(variant) % 3;
            Color stripeCol = (v == 0) ? pal.StripeRed : ((v == 1) ? pal.StripeBlue : pal.HazardYellow);

            int stripeCount = 8;
            float totalW = kSz.x * 1.05f;
            float stripeW = totalW / stripeCount;
            float xStart = -totalW * 0.5f;

            for (int i = 0; i < stripeCount; i++)
            {
                float xs0 = xStart + i * stripeW;
                float xs1 = xs0 + stripeW;

                MeshBuffer curStripeBuf = (i % 2 == 0) ? stripeBuf1 : stripeBuf2;

                // 斜面篷顶
                Vector3 p0 = new Vector3(xs0, 2.38f * scale, 0.40f * scale);
                Vector3 p1 = new Vector3(xs1, 2.38f * scale, 0.40f * scale);
                Vector3 p2 = new Vector3(xs1, 2.05f * scale, 1.05f * scale);
                Vector3 p3 = new Vector3(xs0, 2.05f * scale, 1.05f * scale);
                curStripeBuf.AddQuad(p0, p1, p2, p3, Color.white);

                // 前缘下垂荷叶边 (Scalloped Valance)
                Vector3 f0 = p3;
                Vector3 f1 = p2;
                Vector3 f2 = new Vector3(xs1, 1.88f * scale, 1.05f * scale);
                Vector3 f3 = new Vector3(xs0, 1.88f * scale, 1.05f * scale);
                curStripeBuf.AddQuad(f0, f1, f2, f3, Color.white);
            }

            // 5. 顶部发光招牌看板 (Emissive 自发光，Solid = false)
            metalBuf.AddBox(new Vector3(0f, 2.65f * scale, 0.20f * scale), new Vector3(kSz.x * 0.82f, 0.40f * scale, 0.08f * scale), Color.white);
            signGlowBuf.AddBox(new Vector3(0f, 2.65f * scale, 0.25f * scale), new Vector3(kSz.x * 0.76f, 0.32f * scale, 0.03f * scale), Color.white);

            // 6. 侧面挂板 (黑板风格菜单板)
            metalBuf.AddBox(new Vector3(kSz.x * 0.505f, 1.45f * scale, -0.20f * scale), new Vector3(0.02f * scale, 0.65f * scale, 0.50f * scale), pal.SteelDark);
            wallBuf.AddBox(new Vector3(kSz.x * 0.512f, 1.45f * scale, -0.20f * scale), new Vector3(0.01f * scale, 0.55f * scale, 0.42f * scale), new Color(0.22f, 0.22f, 0.22f, 1f));

            output.Add(new ArenaPropMeshPart(wallBuf.ToMesh("Kiosk_Walls"), ArenaPropSurface.Matte, pal.ConcreteMid, true));
            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("Kiosk_WoodCounter"), ArenaPropSurface.Matte, pal.Wood, false));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("Kiosk_Metal"), ArenaPropSurface.Metal, pal.SteelDark, false));
            output.Add(new ArenaPropMeshPart(stripeBuf1.ToMesh("Kiosk_StripeColor"), ArenaPropSurface.Cloth, stripeCol, false));
            output.Add(new ArenaPropMeshPart(stripeBuf2.ToMesh("Kiosk_StripeWhite"), ArenaPropSurface.Cloth, pal.ClothWhite, false));
            output.Add(new ArenaPropMeshPart(signGlowBuf.ToMesh("Kiosk_SignGlow"), ArenaPropSurface.Emissive, pal.LampGlass, false));
        }
        #endregion

        #region 内部轻量网格构建缓冲器
        /// <summary>
        /// 纯逻辑网格构建缓冲区，确保 position/normal/uv/color 四个流完整。
        /// </summary>
        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(256);
            public readonly List<Vector3> Normals = new List<Vector3>(256);
            public readonly List<Vector2> UVs = new List<Vector2>(256);
            public readonly List<Color> Colors = new List<Color>(256);
            public readonly List<int> Triangles = new List<int>(384);

            public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color color, Matrix4x4? trs = null)
            {
                if (trs.HasValue)
                {
                    v0 = trs.Value.MultiplyPoint3x4(v0);
                    v1 = trs.Value.MultiplyPoint3x4(v1);
                    v2 = trs.Value.MultiplyPoint3x4(v2);
                    v3 = trs.Value.MultiplyPoint3x4(v3);
                }

                // 与三角形绕序保持一致的外法线
                Vector3 normal = Vector3.Cross(v3 - v0, v2 - v0).normalized;
                int start = Vertices.Count;

                Vertices.Add(v0);
                Vertices.Add(v1);
                Vertices.Add(v2);
                Vertices.Add(v3);

                Normals.Add(normal);
                Normals.Add(normal);
                Normals.Add(normal);
                Normals.Add(normal);

                UVs.Add(new Vector2(0f, 0f));
                UVs.Add(new Vector2(1f, 0f));
                UVs.Add(new Vector2(1f, 1f));
                UVs.Add(new Vector2(0f, 1f));

                Colors.Add(color);
                Colors.Add(color);
                Colors.Add(color);
                Colors.Add(color);

                Triangles.Add(start);
                Triangles.Add(start + 3);
                Triangles.Add(start + 2);

                Triangles.Add(start);
                Triangles.Add(start + 2);
                Triangles.Add(start + 1);
            }

            public void AddQuadExplicit(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
                Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3, Color color)
            {
                int start = Vertices.Count;
                Vertices.Add(v0); Vertices.Add(v1); Vertices.Add(v2); Vertices.Add(v3);
                Normals.Add(n0); Normals.Add(n1); Normals.Add(n2); Normals.Add(n3);
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

                // +Y 上面
                AddQuad(c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(h.x, h.y, -h.z),
                        c + new Vector3(h.x, h.y, h.z), c + new Vector3(-h.x, h.y, h.z), color, trs);
                // -Y 下面
                AddQuad(c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(h.x, -h.y, h.z),
                        c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(-h.x, -h.y, -h.z), color, trs);
                // +Z 前面
                AddQuad(c + new Vector3(h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, h.z),
                        c + new Vector3(-h.x, h.y, h.z), c + new Vector3(h.x, h.y, h.z), color, trs);
                // -Z 后面
                AddQuad(c + new Vector3(-h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, -h.z),
                        c + new Vector3(h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, -h.z), color, trs);
                // +X 右面
                AddQuad(c + new Vector3(h.x, -h.y, -h.z), c + new Vector3(h.x, -h.y, h.z),
                        c + new Vector3(h.x, h.y, h.z), c + new Vector3(h.x, h.y, -h.z), color, trs);
                // -X 左面
                AddQuad(c + new Vector3(-h.x, -h.y, h.z), c + new Vector3(-h.x, -h.y, -h.z),
                        c + new Vector3(-h.x, h.y, -h.z), c + new Vector3(-h.x, h.y, h.z), color, trs);
            }

            public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments, Color color, bool topCap, bool bottomCap, Matrix4x4? trs = null)
            {
                AddCone(baseCenter, radius, radius, height, segments, color, bottomCap, topCap, trs);
            }

            public void AddCone(Vector3 baseCenter, float bottomRadius, float topRadius, float height, int segments, Color color, bool bottomCap, bool topCap, Matrix4x4? trs = null)
            {
                int segs = Mathf.Max(segments, 3);

                // 侧面四边形
                for (int i = 0; i < segs; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segs;
                    float a1 = (i + 1) * Mathf.PI * 2f / segs;

                    Vector3 vb0 = baseCenter + new Vector3(Mathf.Cos(a0) * bottomRadius, 0f, Mathf.Sin(a0) * bottomRadius);
                    Vector3 vb1 = baseCenter + new Vector3(Mathf.Cos(a1) * bottomRadius, 0f, Mathf.Sin(a1) * bottomRadius);
                    Vector3 vt1 = baseCenter + new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
                    Vector3 vt0 = baseCenter + new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);

                    AddQuad(vb0, vb1, vt1, vt0, color, trs);
                }

                // 顶盖
                if (topCap && topRadius > 0.001f)
                {
                    Vector3 topC = baseCenter + Vector3.up * height;
                    if (trs.HasValue) topC = trs.Value.MultiplyPoint3x4(topC);

                    int cIdx = Vertices.Count;
                    Vertices.Add(topC);
                    Normals.Add(trs.HasValue ? trs.Value.MultiplyVector(Vector3.up).normalized : Vector3.up);
                    UVs.Add(new Vector2(0.5f, 0.5f));
                    Colors.Add(color);

                    int ringStart = Vertices.Count;
                    for (int i = 0; i < segs; i++)
                    {
                        float a = i * Mathf.PI * 2f / segs;
                        Vector3 p = baseCenter + new Vector3(Mathf.Cos(a) * topRadius, height, Mathf.Sin(a) * topRadius);
                        if (trs.HasValue) p = trs.Value.MultiplyPoint3x4(p);

                        Vertices.Add(p);
                        Normals.Add(trs.HasValue ? trs.Value.MultiplyVector(Vector3.up).normalized : Vector3.up);
                        UVs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                        Colors.Add(color);
                    }

                    for (int i = 0; i < segs; i++)
                    {
                        int nxt = (i + 1) % segs;
                        Triangles.Add(cIdx);
                        Triangles.Add(ringStart + nxt);
                        Triangles.Add(ringStart + i);
                    }
                }

                // 底盖
                if (bottomCap && bottomRadius > 0.001f)
                {
                    Vector3 botC = baseCenter;
                    if (trs.HasValue) botC = trs.Value.MultiplyPoint3x4(botC);

                    int cIdx = Vertices.Count;
                    Vertices.Add(botC);
                    Normals.Add(trs.HasValue ? trs.Value.MultiplyVector(Vector3.down).normalized : Vector3.down);
                    UVs.Add(new Vector2(0.5f, 0.5f));
                    Colors.Add(color);

                    int ringStart = Vertices.Count;
                    for (int i = 0; i < segs; i++)
                    {
                        float a = i * Mathf.PI * 2f / segs;
                        Vector3 p = baseCenter + new Vector3(Mathf.Cos(a) * bottomRadius, 0f, Mathf.Sin(a) * bottomRadius);
                        if (trs.HasValue) p = trs.Value.MultiplyPoint3x4(p);

                        Vertices.Add(p);
                        Normals.Add(trs.HasValue ? trs.Value.MultiplyVector(Vector3.down).normalized : Vector3.down);
                        UVs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                        Colors.Add(color);
                    }

                    for (int i = 0; i < segs; i++)
                    {
                        int nxt = (i + 1) % segs;
                        Triangles.Add(cIdx);
                        Triangles.Add(ringStart + i);
                        Triangles.Add(ringStart + nxt);
                    }
                }
            }

            public void AddDisk(Vector3 center, float radius, int segments, Vector3 normal, Color color, Matrix4x4? trs = null)
            {
                int segs = Mathf.Max(segments, 3);
                Vector3 n = normal.normalized;
                Vector3 right = Mathf.Abs(n.y) > 0.99f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
                Vector3 up = Vector3.Cross(n, right).normalized;

                Vector3 c = center;
                if (trs.HasValue) c = trs.Value.MultiplyPoint3x4(c);
                Vector3 trsNorm = trs.HasValue ? trs.Value.MultiplyVector(n).normalized : n;

                int centerIdx = Vertices.Count;
                Vertices.Add(c);
                Normals.Add(trsNorm);
                UVs.Add(new Vector2(0.5f, 0.5f));
                Colors.Add(color);

                int ringStart = Vertices.Count;
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    Vector3 p = center + right * (Mathf.Cos(a) * radius) + up * (Mathf.Sin(a) * radius);
                    if (trs.HasValue) p = trs.Value.MultiplyPoint3x4(p);

                    Vertices.Add(p);
                    Normals.Add(trsNorm);
                    UVs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                    Colors.Add(color);
                }

                for (int i = 0; i < segs; i++)
                {
                    int nxt = (i + 1) % segs;
                    Triangles.Add(centerIdx);
                    Triangles.Add(ringStart + i);
                    Triangles.Add(ringStart + nxt);
                }
            }

            public Mesh ToMesh(string meshName = "PropMesh")
            {
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
