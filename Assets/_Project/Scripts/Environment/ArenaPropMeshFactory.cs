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
    /// 竞技场地表道具程序化网格工厂（P7-B）：
    /// 纯网格代码生成，不依赖 GameObject.CreatePrimitive 与 UnityEditor API。
    /// 所有生成的网格均包含 position/normal/uv/color 四个完整顶点流。
    /// 坐标约定：道具底面在 y=0、正面朝 +Z、单位米。
    /// </summary>
    public static class ArenaPropMeshFactory
    {
        /// <summary>
        /// 样式配色解包辅助结构，对 null style 做安全回退。
        /// </summary>
        private readonly struct Palette
        {
            public readonly Color Wood;
            public readonly Color WoodDark;
            public readonly Color Foliage;
            public readonly Color LampGlass;
            public readonly Color Steel;
            public readonly Color SteelDark;
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

            public Palette(ArenaStyleSO s)
            {
                Wood = s != null ? s.wood : new Color(0.72f, 0.58f, 0.42f, 1f);
                WoodDark = s != null ? s.woodDark : new Color(0.48f, 0.36f, 0.24f, 1f);
                Foliage = s != null ? s.foliage : new Color(0.24f, 0.45f, 0.28f, 1f);
                LampGlass = s != null ? s.lampGlass : new Color(1.00f, 0.85f, 0.60f, 1f);
                Steel = s != null ? s.steel : new Color(0.42f, 0.47f, 0.52f, 1f);
                SteelDark = s != null ? s.steelDark : new Color(0.24f, 0.27f, 0.30f, 1f);
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

        #region 1. CrateCluster (倒角木箱/板条箱堆叠)
        private static void BuildCrateCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var trimBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();

            void AddCrate(Vector3 center, Vector3 size, float yaw)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(center * scale, rot, size * scale);
                // 主木箱
                woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, trs);
                // 边角加固板条
                trimBuf.AddBox(new Vector3(0f, 0f, 0.505f), new Vector3(1.02f, 0.12f, 0.02f), Color.white, trs);
                trimBuf.AddBox(new Vector3(0f, 0f, -0.505f), new Vector3(1.02f, 0.12f, 0.02f), Color.white, trs);
                // 铁角件
                metalBuf.AddBox(new Vector3(0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, trs);
                metalBuf.AddBox(new Vector3(-0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, trs);
                metalBuf.AddBox(new Vector3(0.48f, 0.48f, -0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, trs);
                metalBuf.AddBox(new Vector3(-0.48f, 0.48f, -0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, trs);
            }

            int v = Mathf.Abs(variant) % 3;
            if (v == 0)
            {
                // 3箱堆叠
                AddCrate(new Vector3(-0.45f, 0.45f, -0.05f), new Vector3(0.9f, 0.9f, 0.9f), 6f);
                AddCrate(new Vector3(0.45f, 0.35f, 0.05f), new Vector3(0.7f, 0.7f, 0.7f), -8f);
                AddCrate(new Vector3(-0.35f, 1.25f, 0.0f), new Vector3(0.65f, 0.65f, 0.65f), 22f);
            }
            else if (v == 1)
            {
                // 2大箱堆叠
                AddCrate(new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 1.1f, 1.1f), 0f);
                AddCrate(new Vector3(0.1f, 1.45f, -0.05f), new Vector3(0.75f, 0.7f, 0.75f), -14f);
            }
            else
            {
                // 4箱分散错落堆叠
                AddCrate(new Vector3(-0.5f, 0.4f, -0.2f), new Vector3(0.8f, 0.8f, 0.8f), -5f);
                AddCrate(new Vector3(0.4f, 0.45f, -0.1f), new Vector3(0.9f, 0.9f, 0.9f), 8f);
                AddCrate(new Vector3(-0.05f, 0.35f, 0.5f), new Vector3(0.7f, 0.7f, 0.7f), -12f);
                AddCrate(new Vector3(-0.1f, 1.15f, 0.1f), new Vector3(0.6f, 0.6f, 0.6f), 25f);
            }

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("CrateCluster_Wood"), ArenaPropSurface.Matte, pal.Wood));
            output.Add(new ArenaPropMeshPart(trimBuf.ToMesh("CrateCluster_Trim"), ArenaPropSurface.Matte, pal.WoodDark));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("CrateCluster_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 2. CrateTall (1.4m 高木箱 + 顶部小箱)
        private static void BuildCrateTall(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var trimBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();

            // 主高箱 (1.0 x 1.4 x 1.0)
            Matrix4x4 mBase = Matrix4x4.TRS(new Vector3(0f, 0.7f * scale, 0f), Quaternion.identity, new Vector3(1.0f * scale, 1.4f * scale, 1.0f * scale));
            woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, mBase);

            // 侧边水平与交叉板条
            trimBuf.AddBox(new Vector3(0f, 0.25f, 0.505f), new Vector3(1.02f, 0.12f, 0.02f), Color.white, mBase);
            trimBuf.AddBox(new Vector3(0f, -0.25f, 0.505f), new Vector3(1.02f, 0.12f, 0.02f), Color.white, mBase);
            trimBuf.AddBox(new Vector3(0.505f, 0.25f, 0f), new Vector3(0.02f, 0.12f, 1.02f), Color.white, mBase);
            trimBuf.AddBox(new Vector3(0.505f, -0.25f, 0f), new Vector3(0.02f, 0.12f, 1.02f), Color.white, mBase);

            // 铁边包角
            metalBuf.AddBox(new Vector3(0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mBase);
            metalBuf.AddBox(new Vector3(-0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mBase);
            metalBuf.AddBox(new Vector3(0.48f, -0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mBase);
            metalBuf.AddBox(new Vector3(-0.48f, -0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mBase);

            // 顶部小箱 (0.65 x 0.5 x 0.65, 错位 12°)
            Matrix4x4 mTop = Matrix4x4.TRS(new Vector3(0.05f * scale, 1.65f * scale, -0.05f * scale), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.65f * scale, 0.5f * scale, 0.65f * scale));
            woodBuf.AddBox(Vector3.zero, Vector3.one, Color.white, mTop);
            metalBuf.AddBox(new Vector3(0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mTop);
            metalBuf.AddBox(new Vector3(-0.48f, 0.48f, 0.48f), new Vector3(0.12f, 0.12f, 0.12f), Color.white, mTop);

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("CrateTall_Wood"), ArenaPropSurface.Matte, pal.Wood));
            output.Add(new ArenaPropMeshPart(trimBuf.ToMesh("CrateTall_Trim"), ArenaPropSurface.Matte, pal.WoodDark));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("CrateTall_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 3. BarrelCluster (3-4 个油桶，按 variant 选柜色)
        private static void BuildBarrelCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var barrelBuf = new MeshBuffer();
            var hoopBuf = new MeshBuffer();

            Color barrelColor;
            int v = Mathf.Abs(variant) % 3;
            if (v == 0) barrelColor = pal.ContainerTeal;
            else if (v == 1) barrelColor = pal.ContainerRust;
            else barrelColor = pal.ContainerMustard;

            void AddSingleBarrel(Vector3 basePos)
            {
                float r = 0.28f * scale;
                float h = 0.88f * scale;
                Vector3 b = basePos * scale;
                // 桶身 (10段圆柱)
                barrelBuf.AddCylinder(b, r, h, 10, Color.white, true, true);
                // 上下卷边箍 + 中间两道加强筋
                float hoopR = r * 1.05f;
                hoopBuf.AddCylinder(b + Vector3.up * (0.02f * scale), hoopR, 0.04f * scale, 10, Color.white, false, false);
                hoopBuf.AddCylinder(b + Vector3.up * (0.30f * scale), hoopR, 0.03f * scale, 10, Color.white, false, false);
                hoopBuf.AddCylinder(b + Vector3.up * (0.58f * scale), hoopR, 0.03f * scale, 10, Color.white, false, false);
                hoopBuf.AddCylinder(b + Vector3.up * (0.82f * scale), hoopR, 0.04f * scale, 10, Color.white, false, false);
            }

            if (v == 0)
            {
                // 3 桶正三角
                AddSingleBarrel(new Vector3(-0.30f, 0f, -0.18f));
                AddSingleBarrel(new Vector3(0.30f, 0f, -0.18f));
                AddSingleBarrel(new Vector3(0.0f, 0f, 0.32f));
            }
            else if (v == 1)
            {
                // 4 桶 (底 3 + 顶 1)
                AddSingleBarrel(new Vector3(-0.32f, 0f, -0.20f));
                AddSingleBarrel(new Vector3(0.32f, 0f, -0.20f));
                AddSingleBarrel(new Vector3(0.0f, 0f, 0.32f));
                AddSingleBarrel(new Vector3(0.0f, 0.88f, 0.0f));
            }
            else
            {
                // 3 桶斜列
                AddSingleBarrel(new Vector3(-0.45f, 0f, -0.25f));
                AddSingleBarrel(new Vector3(0.0f, 0f, 0.0f));
                AddSingleBarrel(new Vector3(0.45f, 0f, 0.25f));
            }

            output.Add(new ArenaPropMeshPart(barrelBuf.ToMesh("BarrelCluster_Body"), ArenaPropSurface.Matte, barrelColor));
            output.Add(new ArenaPropMeshPart(hoopBuf.ToMesh("BarrelCluster_Hoops"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 4. PlanterBox (0.85m 倒角混凝土花坛 + 土壤 + 灌木)
        private static void BuildPlanterBox(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var concreteBuf = new MeshBuffer();
            var soilBuf = new MeshBuffer();
            var foliageBuf = new MeshBuffer();

            float h = 0.85f * scale;
            float sz = 1.35f * scale;

            // 混凝土花坛主体 (下底微收的倒角盒)
            concreteBuf.AddBox(new Vector3(0f, h * 0.45f, 0f), new Vector3(sz, h * 0.9f, sz), Color.white);
            // 顶部外翻边檐
            concreteBuf.AddBox(new Vector3(0f, h - 0.05f * scale, 0f), new Vector3(sz * 1.08f, 0.1f * scale, sz * 1.08f), Color.white);

            // 深色土壤面 (凹入 0.1m)
            soilBuf.AddBox(new Vector3(0f, h - 0.08f * scale, 0f), new Vector3(sz * 0.88f, 0.04f * scale, sz * 0.88f), Color.white);

            // 2-3 团低多边形灌木 (带顶点色阴影渐变)
            void AddBush(Vector3 center, float radius, float height)
            {
                int segs = 8;
                Vector3 b = center * scale;
                float r = radius * scale;
                float ht = height * scale;
                int start = foliageBuf.Vertices.Count;

                // 底部收拢点
                foliageBuf.Vertices.Add(b);
                foliageBuf.Normals.Add(Vector3.down);
                foliageBuf.UVs.Add(new Vector2(0.5f, 0f));
                foliageBuf.Colors.Add(new Color(0.65f, 0.65f, 0.65f, 1f)); // 底部偏暗

                // 腰部圆环 (2 层)
                for (int ring = 1; ring <= 2; ring++)
                {
                    float yFrac = ring == 1 ? 0.45f : 0.85f;
                    float ringR = ring == 1 ? r : r * 0.75f;
                    float tint = ring == 1 ? 0.9f : 1.15f;
                    Color c = new Color(tint, tint, tint, 1f);

                    for (int i = 0; i < segs; i++)
                    {
                        float ang = i * Mathf.PI * 2f / segs;
                        Vector3 pos = b + new Vector3(Mathf.Cos(ang) * ringR, ht * yFrac, Mathf.Sin(ang) * ringR);
                        foliageBuf.Vertices.Add(pos);
                        foliageBuf.Normals.Add((pos - b - Vector3.up * (ht * 0.5f)).normalized);
                        foliageBuf.UVs.Add(new Vector2((float)i / segs, yFrac));
                        foliageBuf.Colors.Add(c);
                    }
                }

                // 顶部顶尖
                int topIdx = foliageBuf.Vertices.Count;
                foliageBuf.Vertices.Add(b + Vector3.up * ht);
                foliageBuf.Normals.Add(Vector3.up);
                foliageBuf.UVs.Add(new Vector2(0.5f, 1f));
                foliageBuf.Colors.Add(new Color(1.25f, 1.25f, 1.25f, 1f)); // 顶部受光高亮

                // 缝合三角形
                int r1 = start + 1;
                int r2 = r1 + segs;
                for (int i = 0; i < segs; i++)
                {
                    int nxt = (i + 1) % segs;
                    // 底面扇形
                    foliageBuf.Triangles.Add(start);
                    foliageBuf.Triangles.Add(r1 + nxt);
                    foliageBuf.Triangles.Add(r1 + i);

                    // 腰部梯形
                    foliageBuf.Triangles.Add(r1 + i);
                    foliageBuf.Triangles.Add(r1 + nxt);
                    foliageBuf.Triangles.Add(r2 + nxt);

                    foliageBuf.Triangles.Add(r1 + i);
                    foliageBuf.Triangles.Add(r2 + nxt);
                    foliageBuf.Triangles.Add(r2 + i);

                    // 顶盖扇形
                    foliageBuf.Triangles.Add(r2 + i);
                    foliageBuf.Triangles.Add(r2 + nxt);
                    foliageBuf.Triangles.Add(topIdx);
                }
            }

            AddBush(new Vector3(-0.24f, h * 0.95f, -0.22f), 0.32f, 0.44f);
            AddBush(new Vector3(0.26f, h * 0.95f, 0.16f), 0.38f, 0.52f);
            AddBush(new Vector3(-0.06f, h * 0.95f, 0.28f), 0.28f, 0.38f);

            output.Add(new ArenaPropMeshPart(concreteBuf.ToMesh("PlanterBox_Concrete"), ArenaPropSurface.Matte, pal.ConcreteMid));
            output.Add(new ArenaPropMeshPart(soilBuf.ToMesh("PlanterBox_Soil"), ArenaPropSurface.Matte, pal.ConcreteDark, false));
            output.Add(new ArenaPropMeshPart(foliageBuf.ToMesh("PlanterBox_Foliage"), ArenaPropSurface.Matte, pal.Foliage, false));
        }
        #endregion

        #region 5. ConeCluster (3 个交通锥：圆锥+方底座+反光带)
        private static void BuildConeCluster(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var orangeBuf = new MeshBuffer();
            var whiteBuf = new MeshBuffer();
            var baseBuf = new MeshBuffer();

            Color coneOrange = new Color(1.0f, 0.42f, 0.08f, 1f);
            Color coneWhite = new Color(0.95f, 0.95f, 0.95f, 1f);

            void AddSingleCone(Vector3 pos, float yaw)
            {
                Vector3 p = pos * scale;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(p, rot, Vector3.one * scale);

                // 黑色方底座 (0.38 x 0.04 x 0.38)
                baseBuf.AddBox(new Vector3(0f, 0.02f, 0f), new Vector3(0.38f, 0.04f, 0.38f), Color.white, trs);

                // 锥体下段 (橙)
                orangeBuf.AddCone(new Vector3(0f, 0.04f, 0f), 0.15f, 0.09f, 0.24f, 8, Color.white, false, false, trs);
                // 中间反光带 (白)
                whiteBuf.AddCone(new Vector3(0f, 0.28f, 0f), 0.09f, 0.06f, 0.16f, 8, Color.white, false, false, trs);
                // 锥体上段 (橙，顶端平切带小盖)
                orangeBuf.AddCone(new Vector3(0f, 0.44f, 0f), 0.06f, 0.03f, 0.22f, 8, Color.white, false, true, trs);
            }

            // 3个紧密簇拥
            AddSingleCone(new Vector3(0f, 0f, 0f), 0f);
            AddSingleCone(new Vector3(-0.34f, 0f, 0.22f), 28f);
            AddSingleCone(new Vector3(0.32f, 0f, 0.18f), -35f);

            output.Add(new ArenaPropMeshPart(orangeBuf.ToMesh("ConeCluster_Orange"), ArenaPropSurface.Matte, coneOrange));
            output.Add(new ArenaPropMeshPart(whiteBuf.ToMesh("ConeCluster_White"), ArenaPropSurface.Cloth, coneWhite));
            output.Add(new ArenaPropMeshPart(baseBuf.ToMesh("ConeCluster_Base"), ArenaPropSurface.Matte, pal.SteelDark));
        }
        #endregion

        #region 6. BollardRow (沿 +Z 排布系缆桩，scale 为总长)
        private static void BuildBollardRow(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var yellowBuf = new MeshBuffer();

            float totalLength = Mathf.Max(scale, 1.5f);
            float spacing = 1.5f;
            int count = Mathf.Max(2, Mathf.RoundToInt(totalLength / spacing) + 1);
            float step = totalLength / (count - 1);

            for (int k = 0; k < count; k++)
            {
                Vector3 basePos = new Vector3(0f, 0f, k * step);
                float r = 0.14f;
                float h = 0.62f;

                // 底部安装盘
                metalBuf.AddCylinder(basePos, r * 1.35f, 0.04f, 8, Color.white, true, true);
                // 主金属圆柱身
                metalBuf.AddCylinder(basePos + Vector3.up * 0.04f, r, h, 8, Color.white, false, false);
                // 顶部半球顶盖
                metalBuf.AddCone(basePos + Vector3.up * (0.04f + h), r, 0.02f, 0.08f, 8, Color.white, false, true);

                // 黄色反光警戒带 (贴在桶身中上部)
                yellowBuf.AddCylinder(basePos + Vector3.up * (0.42f), r * 1.02f, 0.10f, 8, Color.white, false, false);
            }

            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("BollardRow_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
            output.Add(new ArenaPropMeshPart(yellowBuf.ToMesh("BollardRow_Yellow"), ArenaPropSurface.Matte, pal.HazardYellow));
        }
        #endregion

        #region 7. Bench (木条长椅 + 金属腿)
        private static void BuildBench(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var woodBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();

            // 木板座面 (4 根水平木条)
            float benchW = 1.8f * scale;
            for (int i = 0; i < 4; i++)
            {
                float zOffset = (-0.18f + i * 0.11f) * scale;
                woodBuf.AddBox(new Vector3(0f, 0.42f * scale, zOffset), new Vector3(benchW, 0.04f * scale, 0.08f * scale), Color.white);
            }

            // 靠背木条 (3 根倾斜木条)
            for (int i = 0; i < 3; i++)
            {
                float yOffset = (0.54f + i * 0.12f) * scale;
                float zOffset = (-0.22f - i * 0.04f) * scale;
                woodBuf.AddBox(new Vector3(0f, yOffset, zOffset), new Vector3(benchW, 0.08f * scale, 0.03f * scale), Color.white);
            }

            // 金属椅腿 (左右两副)
            void AddLeg(float x)
            {
                Vector3 legPos = new Vector3(x * scale, 0f, 0f);
                // 前后地脚
                metalBuf.AddBox(legPos + new Vector3(0f, 0.03f * scale, 0f), new Vector3(0.06f * scale, 0.06f * scale, 0.65f * scale), Color.white);
                // 前支撑竖柱
                metalBuf.AddBox(legPos + new Vector3(0f, 0.21f * scale, 0.18f * scale), new Vector3(0.05f * scale, 0.38f * scale, 0.05f * scale), Color.white);
                // 后支撑横杆向上延伸为靠背架与扶手
                metalBuf.AddBox(legPos + new Vector3(0f, 0.40f * scale, -0.22f * scale), new Vector3(0.05f * scale, 0.76f * scale, 0.05f * scale), Color.white);
                // 扶手
                metalBuf.AddBox(legPos + new Vector3(0f, 0.58f * scale, 0f), new Vector3(0.05f * scale, 0.04f * scale, 0.45f * scale), Color.white);
            }

            AddLeg(-0.75f);
            AddLeg(0.75f);

            output.Add(new ArenaPropMeshPart(woodBuf.ToMesh("Bench_Wood"), ArenaPropSurface.Matte, pal.Wood));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("Bench_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 8. AcUnit (倒角机箱 + 风扇圆盘 + 格栅条)
        private static void BuildAcUnit(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalLightBuf = new MeshBuffer();
            var metalDarkBuf = new MeshBuffer();

            // 机身主体 (1.1 x 0.85 x 0.45m)
            Vector3 bodySz = new Vector3(1.1f * scale, 0.85f * scale, 0.45f * scale);
            Vector3 center = new Vector3(0f, bodySz.y * 0.5f, 0f);
            metalLightBuf.AddBox(center, bodySz, Color.white);

            // 底部减震支撑脚 (2 条工字轨)
            metalDarkBuf.AddBox(new Vector3(-0.42f * scale, 0.03f * scale, 0f), new Vector3(0.10f * scale, 0.06f * scale, 0.52f * scale), Color.white);
            metalDarkBuf.AddBox(new Vector3(0.42f * scale, 0.03f * scale, 0f), new Vector3(0.10f * scale, 0.06f * scale, 0.52f * scale), Color.white);

            // 前部圆形风扇沉凹槽与叶盘 (z 朝 +Z)
            Vector3 fanCenter = new Vector3(-0.16f * scale, center.y, bodySz.z * 0.505f);
            metalDarkBuf.AddCylinder(fanCenter, 0.28f * scale, 0.02f * scale, 12, Color.white, true, true);
            // 风扇中心轮毂
            metalLightBuf.AddCylinder(fanCenter + Vector3.forward * (0.01f * scale), 0.08f * scale, 0.03f * scale, 8, Color.white, true, true);

            // 右侧 5 道散热横格栅条
            for (int i = 0; i < 5; i++)
            {
                float y = center.y + (-0.22f + i * 0.11f) * scale;
                metalDarkBuf.AddBox(new Vector3(0.32f * scale, y, bodySz.z * 0.505f), new Vector3(0.32f * scale, 0.035f * scale, 0.02f * scale), Color.white);
            }

            output.Add(new ArenaPropMeshPart(metalLightBuf.ToMesh("AcUnit_Chassis"), ArenaPropSurface.Metal, pal.Steel));
            output.Add(new ArenaPropMeshPart(metalDarkBuf.ToMesh("AcUnit_Details"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 9. PipeRun (沿 +Z 管道 + 法兰 + 支架，scale 为长度)
        private static void BuildPipeRun(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var pipeBuf = new MeshBuffer();
            var bracketBuf = new MeshBuffer();

            float totalLength = Mathf.Max(scale, 2.0f);
            float r = 0.12f;

            // 主管道 (沿 +Z 排布，8边型圆柱，水平轴通过局部变换构造)
            int pipeSegs = 8;
            Vector3 pStart = Vector3.zero;
            Vector3 pEnd = new Vector3(0f, 0f, totalLength);

            // 构建沿 Z 轴的直管圆柱
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

            // 两端与中间法兰凸缘 (每 3.5m 一处)
            int flangeCount = Mathf.Max(2, Mathf.RoundToInt(totalLength / 3.5f) + 1);
            float fStep = totalLength / (flangeCount - 1);
            for (int k = 0; k < flangeCount; k++)
            {
                float z = k * fStep;
                pipeBuf.AddBox(new Vector3(0f, 0f, z), new Vector3(r * 2.8f, r * 2.8f, 0.06f), Color.white);

                // 中间与下垂支架扣具 (向下延伸 0.45m)
                bracketBuf.AddBox(new Vector3(0f, -0.25f, z), new Vector3(0.06f, 0.45f, 0.08f), Color.white);
                bracketBuf.AddBox(new Vector3(0f, -0.47f, z), new Vector3(0.24f, 0.04f, 0.16f), Color.white);
            }

            output.Add(new ArenaPropMeshPart(pipeBuf.ToMesh("PipeRun_Pipe"), ArenaPropSurface.Metal, pal.Steel));
            output.Add(new ArenaPropMeshPart(bracketBuf.ToMesh("PipeRun_Brackets"), ArenaPropSurface.Metal, pal.SteelDark));
        }
        #endregion

        #region 11. LampPost (4m 灯杆 + 弯臂 + 灯罩，自发光)
        private static void BuildLampPost(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var glowBuf = new MeshBuffer();

            // 4m 渐细直杆
            float totalH = 4.0f * scale;
            metalBuf.AddBox(new Vector3(0f, 0.05f * scale, 0f), new Vector3(0.42f * scale, 0.10f * scale, 0.42f * scale), Color.white);
            metalBuf.AddCone(Vector3.zero, 0.12f * scale, 0.05f * scale, totalH * 0.88f, 8, Color.white, false, false);

            // 顶端弯臂 (朝 +Z 前倾延伸)
            Vector3 armStart = new Vector3(0f, totalH * 0.88f, 0f);
            Vector3 armMid = new Vector3(0f, totalH * 0.98f, 0.40f * scale);
            Vector3 armEnd = new Vector3(0f, totalH * 0.95f, 0.85f * scale);
            metalBuf.AddBox((armStart + armMid) * 0.5f, new Vector3(0.07f * scale, 0.07f * scale, 0.55f * scale), Color.white);
            metalBuf.AddBox((armMid + armEnd) * 0.5f, new Vector3(0.06f * scale, 0.06f * scale, 0.55f * scale), Color.white);

            // 灯罩顶壳 (倒扣金属锥)
            metalBuf.AddCone(armEnd + Vector3.up * (0.06f * scale), 0.24f * scale, 0.08f * scale, 0.12f * scale, 8, Color.white, true, true);

            // 灯罩发光面 (朝下的平底透镜，低强度自发光)
            glowBuf.AddCylinder(armEnd - Vector3.up * (0.02f * scale), 0.22f * scale, 0.04f * scale, 8, Color.white, true, true);

            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("LampPost_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
            output.Add(new ArenaPropMeshPart(glowBuf.ToMesh("LampPost_Glass"), ArenaPropSurface.Emissive, pal.LampGlass));
        }
        #endregion

        #region 12. PalmTree (弯曲分节树干 + 7-8 片扁平分叉叶片)
        private static void BuildPalmTree(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var trunkBuf = new MeshBuffer();
            var leafBuf = new MeshBuffer();

            // 6 节稍微弯曲的树干 (高约 4.8m)
            int trunkSegments = 6;
            float totalH = 4.8f * scale;
            float segH = totalH / trunkSegments;
            Vector3 prevCenter = Vector3.zero;
            float prevR = 0.26f * scale;

            for (int i = 1; i <= trunkSegments; i++)
            {
                float t = (float)i / trunkSegments;
                // 沿 +Z 与 +X 轻微弧度前倾
                Vector3 curCenter = new Vector3(
                    Mathf.Sin(t * 1.5f) * 0.45f * scale,
                    i * segH,
                    (1f - Mathf.Cos(t * 1.2f)) * 0.65f * scale);
                float curR = Mathf.Lerp(0.26f, 0.13f, t) * scale;

                // 树干单节圆台 (并在节间加入外凸环，增强棕榈纹理感)
                trunkBuf.AddCone(prevCenter, prevR, curR, (curCenter - prevCenter).magnitude, 8, Color.white, false, false);
                trunkBuf.AddCylinder(curCenter - Vector3.up * (0.04f * scale), curR * 1.15f, 0.06f * scale, 8, Color.white, false, false);

                prevCenter = curCenter;
                prevR = curR;
            }

            // 顶冠：8 片放射状向下垂弯的宽大叶片
            int leafCount = 8;
            Vector3 crownCenter = prevCenter;
            for (int k = 0; k < leafCount; k++)
            {
                float baseAng = (k * 360f / leafCount + (k % 2) * 12f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(baseAng), 0f, Mathf.Sin(baseAng)).normalized;

                // 每片叶子由 4 段多边形沿曲线向前下延伸
                Vector3 p0 = crownCenter;
                Vector3 p1 = crownCenter + dir * (0.8f * scale) + Vector3.up * (0.35f * scale);
                Vector3 p2 = crownCenter + dir * (1.6f * scale) - Vector3.up * (0.15f * scale);
                Vector3 p3 = crownCenter + dir * (2.3f * scale) - Vector3.up * (0.75f * scale);

                Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;

                void AddFrondSegment(Vector3 a, Vector3 b, float wA, float wB, float tintA, float tintB)
                {
                    Vector3 v0 = a - side * (wA * 0.5f);
                    Vector3 v1 = a + side * (wA * 0.5f);
                    Vector3 v2 = b + side * (wB * 0.5f);
                    Vector3 v3 = b - side * (wB * 0.5f);

                    Color cA = new Color(tintA, tintA, tintA, 1f);
                    Color cB = new Color(tintB, tintB, tintB, 1f);

                    int idx = leafBuf.Vertices.Count;
                    Vector3 norm = Vector3.Cross(b - a, side).normalized;

                    leafBuf.Vertices.Add(v0); leafBuf.Normals.Add(norm); leafBuf.UVs.Add(new Vector2(0f, 0f)); leafBuf.Colors.Add(cA);
                    leafBuf.Vertices.Add(v1); leafBuf.Normals.Add(norm); leafBuf.UVs.Add(new Vector2(1f, 0f)); leafBuf.Colors.Add(cA);
                    leafBuf.Vertices.Add(v2); leafBuf.Normals.Add(norm); leafBuf.UVs.Add(new Vector2(1f, 1f)); leafBuf.Colors.Add(cB);
                    leafBuf.Vertices.Add(v3); leafBuf.Normals.Add(norm); leafBuf.UVs.Add(new Vector2(0f, 1f)); leafBuf.Colors.Add(cB);

                    // 双面可见 (正面与背面)
                    leafBuf.Triangles.Add(idx); leafBuf.Triangles.Add(idx + 3); leafBuf.Triangles.Add(idx + 2);
                    leafBuf.Triangles.Add(idx); leafBuf.Triangles.Add(idx + 2); leafBuf.Triangles.Add(idx + 1);

                    leafBuf.Triangles.Add(idx); leafBuf.Triangles.Add(idx + 1); leafBuf.Triangles.Add(idx + 2);
                    leafBuf.Triangles.Add(idx); leafBuf.Triangles.Add(idx + 2); leafBuf.Triangles.Add(idx + 3);
                }

                AddFrondSegment(p0, p1, 0.15f * scale, 0.45f * scale, 0.85f, 1.05f);
                AddFrondSegment(p1, p2, 0.45f * scale, 0.40f * scale, 1.05f, 1.20f);
                AddFrondSegment(p2, p3, 0.40f * scale, 0.04f * scale, 1.20f, 1.35f);
            }

            output.Add(new ArenaPropMeshPart(trunkBuf.ToMesh("PalmTree_Trunk"), ArenaPropSurface.Matte, pal.WoodDark));
            output.Add(new ArenaPropMeshPart(leafBuf.ToMesh("PalmTree_Leaves"), ArenaPropSurface.Matte, pal.Foliage));
        }
        #endregion

        #region 13. FlagPole (5m 旗杆 + 布旗，Cloth，队色)
        private static void BuildFlagPole(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var metalBuf = new MeshBuffer();
            var clothBuf = new MeshBuffer();

            float totalH = 5.2f * scale;

            // 底座盘
            metalBuf.AddBox(new Vector3(0f, 0.05f * scale, 0f), new Vector3(0.45f * scale, 0.10f * scale, 0.45f * scale), Color.white);
            // 细直金属杆 (8面)
            metalBuf.AddCylinder(Vector3.zero, 0.045f * scale, totalH, 8, Color.white, true, true);
            // 顶端圆球装饰帽
            metalBuf.AddCone(new Vector3(0f, totalH, 0f), 0.08f * scale, 0.01f * scale, 0.12f * scale, 8, Color.white, true, true);

            // 悬挂队旗布面 (宽 1.7m，高 1.1m，沿 +Z 延伸，带 4 段柔和飘动微折痕)
            Color flagColor = (variant == 0) ? pal.TeamAPad : pal.TeamBPad;
            int segments = 4;
            float flagLen = 1.7f * scale;
            float flagH = 1.1f * scale;
            float topY = totalH - 0.25f * scale;
            float botY = topY - flagH;

            for (int i = 0; i < segments; i++)
            {
                float z0 = (float)i / segments * flagLen;
                float z1 = (float)(i + 1) / segments * flagLen;

                float wave0 = Mathf.Sin((float)i * 1.4f) * 0.07f * scale;
                float wave1 = Mathf.Sin((float)(i + 1) * 1.4f) * 0.07f * scale;

                Vector3 v0 = new Vector3(wave0, botY, z0);
                Vector3 v1 = new Vector3(wave1, botY, z1);
                Vector3 v2 = new Vector3(wave1, topY, z1);
                Vector3 v3 = new Vector3(wave0, topY, z0);

                int idx = clothBuf.Vertices.Count;
                Vector3 norm = Vector3.Cross(v2 - v0, v3 - v0).normalized;

                clothBuf.Vertices.Add(v0); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2((float)i / segments, 0f)); clothBuf.Colors.Add(Color.white);
                clothBuf.Vertices.Add(v1); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2((float)(i + 1) / segments, 0f)); clothBuf.Colors.Add(Color.white);
                clothBuf.Vertices.Add(v2); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2((float)(i + 1) / segments, 1f)); clothBuf.Colors.Add(Color.white);
                clothBuf.Vertices.Add(v3); clothBuf.Normals.Add(norm); clothBuf.UVs.Add(new Vector2((float)i / segments, 1f)); clothBuf.Colors.Add(Color.white);

                // 双面可见
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 3); clothBuf.Triangles.Add(idx + 2);
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 2); clothBuf.Triangles.Add(idx + 1);

                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 1); clothBuf.Triangles.Add(idx + 2);
                clothBuf.Triangles.Add(idx); clothBuf.Triangles.Add(idx + 2); clothBuf.Triangles.Add(idx + 3);
            }

            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("FlagPole_Metal"), ArenaPropSurface.Metal, pal.Steel));
            output.Add(new ArenaPropMeshPart(clothBuf.ToMesh("FlagPole_Cloth"), ArenaPropSurface.Cloth, flagColor));
        }
        #endregion

        #region 14. Kiosk (小型售卖亭)
        private static void BuildKiosk(float scale, int variant, Palette pal, List<ArenaPropMeshPart> output)
        {
            var wallBuf = new MeshBuffer();
            var roofBuf = new MeshBuffer();
            var metalBuf = new MeshBuffer();
            var stripeBuf = new MeshBuffer();

            // 亭体 (宽 2.6m x 深 1.8m x 高 2.4m)
            Vector3 kSz = new Vector3(2.6f * scale, 2.4f * scale, 1.8f * scale);
            Vector3 center = new Vector3(0f, kSz.y * 0.5f, 0f);

            // 混凝土墙身
            wallBuf.AddBox(new Vector3(0f, center.y, -0.45f * scale), new Vector3(kSz.x, kSz.y, 0.9f * scale), Color.white);
            // 前柜台下半部墙面
            wallBuf.AddBox(new Vector3(0f, 0.45f * scale, 0.45f * scale), new Vector3(kSz.x, 0.90f * scale, 0.9f * scale), Color.white);

            // 柜台金属台面 (伸出 0.15m)
            metalBuf.AddBox(new Vector3(0f, 0.92f * scale, 0.55f * scale), new Vector3(kSz.x * 1.05f, 0.06f * scale, 0.45f * scale), Color.white);

            // 窗口深色内凹背板 (深色开口视觉)
            wallBuf.AddBox(new Vector3(0f, 1.55f * scale, 0.10f * scale), new Vector3(kSz.x * 0.85f, 1.10f * scale, 0.05f * scale), new Color(0.2f, 0.2f, 0.2f, 1f));

            // 外挑金属倾斜屋顶 (前挑 0.5m)
            roofBuf.AddBox(new Vector3(0f, 2.45f * scale, 0.15f * scale), new Vector3(kSz.x * 1.15f, 0.10f * scale, kSz.z * 1.35f), Color.white);

            // 檐口队伍色饰条 (根据 variant 呈现队色)
            Color stripeCol = (variant == 0) ? pal.TeamAPad : ((variant == 1) ? pal.TeamBPad : pal.HazardYellow);
            stripeBuf.AddBox(new Vector3(0f, 2.38f * scale, 0.83f * scale), new Vector3(kSz.x * 1.16f, 0.12f * scale, 0.04f * scale), Color.white);

            output.Add(new ArenaPropMeshPart(wallBuf.ToMesh("Kiosk_Walls"), ArenaPropSurface.Matte, pal.ConcreteMid));
            output.Add(new ArenaPropMeshPart(roofBuf.ToMesh("Kiosk_Roof"), ArenaPropSurface.Metal, pal.Steel, false));
            output.Add(new ArenaPropMeshPart(metalBuf.ToMesh("Kiosk_Metal"), ArenaPropSurface.Metal, pal.SteelDark));
            output.Add(new ArenaPropMeshPart(stripeBuf.ToMesh("Kiosk_Stripe"), ArenaPropSurface.Matte, stripeCol));
        }
        #endregion

        #region 内部轻量网格构建缓冲器
        /// <summary>
        /// 纯逻辑网格构建缓冲区，确保 position/normal/uv/color 四个流完整。
        /// </summary>
        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(128);
            public readonly List<Vector3> Normals = new List<Vector3>(128);
            public readonly List<Vector2> UVs = new List<Vector2>(128);
            public readonly List<Color> Colors = new List<Color>(128);
            public readonly List<int> Triangles = new List<int>(192);

            public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color color, Matrix4x4? trs = null)
            {
                if (trs.HasValue)
                {
                    v0 = trs.Value.MultiplyPoint3x4(v0);
                    v1 = trs.Value.MultiplyPoint3x4(v1);
                    v2 = trs.Value.MultiplyPoint3x4(v2);
                    v3 = trs.Value.MultiplyPoint3x4(v3);
                }

                // 与三角形绕序（0,3,2 / 0,2,1）保持一致的外法线
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
                int baseStart = Vertices.Count;

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
