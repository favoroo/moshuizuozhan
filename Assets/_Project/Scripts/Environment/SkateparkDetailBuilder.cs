using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「霓虹海滨工业滑板场 / NEON HARBOR SKATEPARK」纯装饰工业细节层。
    ///
    /// 职责边界：
    /// - 玩法几何（地面/碗池/天桥/集装箱/办公室/边界/出生区）全部在
    ///   <see cref="SkateparkGeometryBuilder"/> 中生成，本类**不创建任何 Collider**；
    /// - 本类只做"读形"工业细节：14m 开放式桁架塔吊（蓝侧黄/橙侧橙）、1.3x 履带挖掘机、
    ///   集装箱外装（波纹筋/角件/门锁杆/CSC编码板/大幅涂鸦）、高空架空输电线与斜撑铁丝网、
    ///   轮胎堆/托盘/油桶/滑板/喷漆罐/水马/电缆盘以及码头系缆桩与直梯；
    /// - 所有随机使用确定性 hash，重建场景结果稳定；
    /// - 所有对象名统一 "Skatepark_" 前缀，方便在 Hierarchy 里整体定位。
    /// </summary>
    public static class SkateparkDetailBuilder
    {
        public const string DetailRootName = "Skatepark_Details";

        private static ArenaMaterialSet s_ActiveMats;

        public static ArenaMaterialSet ActiveMaterialSet
        {
            get => s_ActiveMats;
            set => s_ActiveMats = value;
        }

        public static GameObject Build(Transform parent, ArenaMaterialSet mats)
        {
            mats ??= new ArenaMaterialSet();
            s_ActiveMats = mats;

            var root = new GameObject(DetailRootName);
            root.transform.SetParent(parent, false);

            Material concrete = mats.concrete ?? mats.wall;
            Material concreteDark = mats.concreteDark ?? concrete;
            Material steel = mats.steel ?? mats.propMetal ?? concrete;
            Material steelDark = mats.steelDark ?? steel;
            Material glass = mats.glass ?? steelDark;
            Material emissive = mats.propEmissive ?? steel;
            Material yellow = mats.containerMustard ?? concrete;
            // accent 已从警示黄改为高饱和橙（0.95/0.45/0.06），挖掘机/锥桶/救生圈统一用它。
            Material orange = mats.accent ?? mats.containerRust ?? concrete;
            Material teal = mats.containerTeal ?? concrete;
            Material blue = mats.containerBlue ?? mats.containerSlate ?? concrete;
            Material cream = mats.accent ?? mats.cabin ?? concrete;
            Material wood = mats.wood ?? mats.woodDark ?? yellow;

            Material[] graffiti =
            {
                mats.graffitiPink ?? cream,
                mats.graffitiGreen ?? mats.containerGreen ?? teal,
                mats.graffitiCyan ?? blue,
                mats.graffitiPurple ?? cream,
                orange,
                yellow,
                cream
            };

            BuildCranes(root.transform, yellow, steelDark, concreteDark, glass, emissive, cream);
            BuildExcavators(root.transform, orange, steelDark, glass, emissive, yellow, steel);
            // 注意：集装箱装饰已移至 DressContainer()，由 SkateparkGeometryBuilder 逐箱调用，此处不再双重装饰。
            BuildOffices(root.transform, glass, steelDark, yellow, cream, graffiti);
            BuildBoundaryGraffiti(root.transform, graffiti, steelDark, cream);
            // 场内铁丝网围栏已移除：1.3m 高、跨在出生台到侧翼的主动线上却没有碰撞，
            // 是「看着能挡子弹、实际能穿过去」的典型假掩体；围栏构件保留给场外远景复用。
            BuildSiteProps(root.transform, steelDark, concreteDark, yellow, orange, cream, wood, blue, graffiti, steel);
            BuildPowerLinesAndPoles(root.transform, steelDark);
            BuildHarborEdgeDressing(root.transform, steelDark, yellow, cream, orange);
            BuildFloodlightTowers(root.transform, steelDark, emissive);

            return root;
        }

        // =====================================================================================
        // 集装箱装饰对外接口（协同契约：由 Subagent A 在构建每个集装箱时调用）
        // =====================================================================================

        public static void DressContainer(Transform root, Vector3 center, float yaw, Vector3 size, int variantSeed)
        {
            DressContainer(root, center, yaw, size, variantSeed, s_ActiveMats);
        }

        public static void DressContainer(Transform root, Vector3 center, float yaw, Vector3 size, int variantSeed, ArenaMaterialSet mats)
        {
            mats ??= s_ActiveMats ?? ResolveMaterialsFromContext(root);
            Material steel = mats.steel ?? mats.propMetal;
            Material steelDark = mats.steelDark ?? steel;
            Material cream = mats.accent ?? mats.cabin ?? steelDark;
            Material orange = mats.accent ?? mats.containerRust ?? steelDark;
            Material yellow = mats.containerMustard ?? steelDark;
            Material blue = mats.containerBlue ?? mats.containerSlate ?? steelDark;
            Material teal = mats.containerTeal ?? steelDark;

            Material[] graffitiPalette =
            {
                mats.graffitiPink ?? cream,
                mats.graffitiGreen ?? mats.containerGreen ?? teal,
                mats.graffitiCyan ?? blue,
                mats.graffitiPurple ?? cream,
                orange,
                yellow,
                cream
            };

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float x, float y, float z) => center + rot * new Vector3(x, y, z);
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            // 1. 两侧长边波纹立筋（16 根/侧，宽 0.045m，深 0.05m，高 size.y - 0.30m）
            // 旧版 26 根/侧把每个箱子刷成黑白斑马条，远景里密度过高；16 根足够读出瓦楞。
            const int ribCount = 16;
            float ribSpanZ = size.z - 0.56f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < ribCount; i++)
                {
                    float z = -hz + 0.28f + (i + 0.5f) * (ribSpanZ / ribCount);
                    Material ribMat = (Hash01(variantSeed, i, 11) > 0.94f) ? cream : steelDark;
                    BoxAt(root, $"Skatepark_Container_Rib_{variantSeed}_{side}_{i}",
                        W(side * (hx + 0.015f), 0f, z),
                        new Vector3(0.05f, size.y - 0.30f, 0.045f), ribMat, rot);
                }
            }

            // 2. 8 个角件（0.20 × 0.16 × 0.20m）
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    for (int cz = -1; cz <= 1; cz += 2)
                    {
                        BoxAt(root, $"Skatepark_Container_Corner_{variantSeed}_{cx}_{cy}_{cz}",
                            W(cx * (hx - 0.06f), cy * (hy - 0.05f), cz * (hz - 0.06f)),
                            new Vector3(0.20f, 0.16f, 0.20f), steelDark, rot);
                    }
                }
            }

            // 3. 顶面边梁（长边两道 + 短边两道）
            for (int side = -1; side <= 1; side += 2)
            {
                BoxAt(root, $"Skatepark_Container_TopRailX_{variantSeed}_{side}",
                    W(side * (hx - 0.035f), hy + 0.035f, 0f),
                    new Vector3(0.09f, 0.08f, size.z), steelDark, rot);
                BoxAt(root, $"Skatepark_Container_TopRailZ_{variantSeed}_{side}",
                    W(0f, hy + 0.035f, side * (hz - 0.035f)),
                    new Vector3(size.x, 0.08f, 0.09f), steelDark, rot);
            }

            // 4. 两端门锁杆与把手
            for (int side = -1; side <= 1; side += 2)
            {
                for (int b = 0; b < 4; b++)
                {
                    float x = -0.72f + b * 0.48f;
                    BoxAt(root, $"Skatepark_Container_DoorBar_{variantSeed}_{side}_{b}",
                        W(x, 0f, side * (hz + 0.035f)),
                        new Vector3(0.05f, size.y - 0.32f, 0.05f), steelDark, rot);
                }
                BoxAt(root, $"Skatepark_Container_DoorHandle_{variantSeed}_{side}",
                    W(0f, 0.05f, side * (hz + 0.08f)),
                    new Vector3(0.70f, 0.08f, 0.05f), cream, rot);
            }

            // 5. 侧面货柜编码牌（CSC 铭牌）+ 3 条深色条形码
            int pSide = (Hash01(variantSeed, 1, 99) > 0.5f) ? 1 : -1;
            Vector3 placardPos = W(pSide * (hx + 0.025f), hy * 0.25f, -hz * 0.35f);
            BoxAt(root, $"Skatepark_Container_Placard_{variantSeed}",
                placardPos, new Vector3(0.02f, 0.42f, 0.72f), cream, rot);
            for (int k = 0; k < 3; k++)
            {
                Vector3 barPos = W(pSide * (hx + 0.038f), hy * 0.25f + (0.10f - k * 0.10f), -hz * 0.35f);
                BoxAt(root, $"Skatepark_Container_CodeBar_{variantSeed}_{k}",
                    barPos, new Vector3(0.015f, 0.04f, 0.52f), steelDark, rot);
            }

            // 6. 大幅多层涂鸦：不再每个箱子两面都刷。
            // 参考图里只有部分箱体带 tag；全箱双面涂鸦会把整场读成「喷漆仓库」，
            // 也会让集装箱本色（红/绿/蓝/芥末）失去识别度。按确定性 hash 选 40% 的箱子刷一面。
            if (Hash01(variantSeed, 9, 5) > 0.42f)
            {
                float zOff1 = Mathf.Lerp(-1.2f, 1.2f, Hash01(variantSeed, 2, 17));
                Vector3 gPos1 = W(hx + 0.02f, 0.05f, zOff1);
                BuildGraffitiCluster(root, $"Skatepark_Graffiti_Cont_{variantSeed}_E",
                    gPos1, rot * Quaternion.Euler(0f, 90f, 0f), graffitiPalette,
                    2.2f, 0.80f, 4, variantSeed * 31 + 7, steelDark, cream);
            }

            if (Hash01(variantSeed, 10, 7) > 0.70f)
            {
                float zOff2 = Mathf.Lerp(-1.2f, 1.2f, Hash01(variantSeed, 3, 23));
                Vector3 gPos2 = W(-hx - 0.02f, 0.05f, zOff2);
                BuildGraffitiCluster(root, $"Skatepark_Graffiti_Cont_{variantSeed}_W",
                    gPos2, rot * Quaternion.Euler(0f, -90f, 0f), graffitiPalette,
                    2.0f, 0.72f, 4, variantSeed * 47 + 19, steelDark, cream);
            }
        }

        // =====================================================================================
        // 塔式起重机（英雄地标：11m 开放式桁架 / 14.5m 塔尖，统一醒目亮黄色，加粗立杆）
        // =====================================================================================

        private static void BuildCranes(Transform root, Material yellow, Material dark,
            Material concreteDark, Material glass, Material emissive, Material cream)
        {
            for (int m = 0; m < 2; m++)
            {
                float sign = m == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.CranePos;
                float yaw = ArenaLayoutSkatepark.CraneYaw + (m == 0 ? 0f : 180f);
                string tag = m == 0 ? "NE" : "SW";
                // 两台塔吊均统一采用亮黄色 (containerMustard)，彻底消除暖光下发粉观感；塔身加深色横带
                BuildCrane(root, new Vector3(p.x * sign, 0f, p.z * sign), yaw, tag,
                    yellow, dark, concreteDark, glass, emissive, cream, yellow);
            }
        }

        private static void BuildCrane(Transform root, Vector3 basePos, float yaw, string tag,
            Material craneColor, Material dark, Material concreteDark, Material glass,
            Material emissive, Material cream, Material yellow)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float x, float y, float z) => basePos + rot * new Vector3(x, y, z);

            const float halfW = 0.50f; // 1.15m 方形塔身半跨（比例厚重稳健）
            const float towerBaseY = 1.0f; // 混凝土基座上方起升
            const float towerTopY = 12.0f; // 塔身高度 11.0m（为原 14m 的 ~0.78 倍）

            // 1. 塔身四角立杆（截面加粗到 0.15 × 0.15m，完全开放式桁架，通透不遮光）
            for (int cx = -1; cx <= 1; cx += 2)
            {
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    string legCorner = (cx < 0 ? "W" : "E") + (cz < 0 ? "S" : "N");
                    BoxAt(root, $"Skatepark_Crane_Leg_{tag}_{legCorner}",
                        W(cx * halfW, (towerBaseY + towerTopY) * 0.5f, cz * halfW),
                        new Vector3(0.15f, towerTopY - towerBaseY, 0.15f), craneColor, rot);
                }
            }

            // 2. 塔身水平横圈（7 节间共 8 圈，间距同步缩小至约 1.57m）
            float[] ringY = { 1.0f, 2.57f, 4.14f, 5.71f, 7.28f, 8.85f, 10.42f, 12.0f };
            for (int r = 0; r < ringY.Length; r++)
            {
                float y = ringY[r];
                // X 轴向横梁
                BoxAt(root, $"Skatepark_Crane_RingX_{tag}_{r}_N",
                    W(0f, y, halfW), new Vector3(halfW * 2f, 0.08f, 0.08f), dark, rot);
                BoxAt(root, $"Skatepark_Crane_RingX_{tag}_{r}_S",
                    W(0f, y, -halfW), new Vector3(halfW * 2f, 0.08f, 0.08f), dark, rot);
                // Z 轴向横梁
                BoxAt(root, $"Skatepark_Crane_RingZ_{tag}_{r}_E",
                    W(halfW, y, 0f), new Vector3(0.08f, 0.08f, halfW * 2f), dark, rot);
                BoxAt(root, $"Skatepark_Crane_RingZ_{tag}_{r}_W",
                    W(-halfW, y, 0f), new Vector3(0.08f, 0.08f, halfW * 2f), dark, rot);
            }

            // 3. 塔身 4 面 X 形交叉斜撑腹杆（0.055m 细杆，紧凑密实）
            for (int b = 0; b < ringY.Length - 1; b++)
            {
                float y0 = ringY[b];
                float y1 = ringY[b + 1];

                // 北面 (+Z)
                BeamBetween(root, $"Skatepark_Crane_WebN_{tag}_{b}_0",
                    W(-halfW, y0, halfW), W(halfW, y1, halfW), 0.055f, craneColor);
                BeamBetween(root, $"Skatepark_Crane_WebN_{tag}_{b}_1",
                    W(halfW, y0, halfW), W(-halfW, y1, halfW), 0.055f, craneColor);

                // 南面 (-Z)
                BeamBetween(root, $"Skatepark_Crane_WebS_{tag}_{b}_0",
                    W(-halfW, y0, -halfW), W(halfW, y1, -halfW), 0.055f, craneColor);
                BeamBetween(root, $"Skatepark_Crane_WebS_{tag}_{b}_1",
                    W(halfW, y0, -halfW), W(-halfW, y1, -halfW), 0.055f, craneColor);

                // 东面 (+X)
                BeamBetween(root, $"Skatepark_Crane_WebE_{tag}_{b}_0",
                    W(halfW, y0, -halfW), W(halfW, y1, halfW), 0.055f, craneColor);
                BeamBetween(root, $"Skatepark_Crane_WebE_{tag}_{b}_1",
                    W(halfW, y0, halfW), W(halfW, y1, -halfW), 0.055f, craneColor);

                // 西面 (-X)
                BeamBetween(root, $"Skatepark_Crane_WebW_{tag}_{b}_0",
                    W(-halfW, y0, -halfW), W(-halfW, y1, halfW), 0.055f, craneColor);
                BeamBetween(root, $"Skatepark_Crane_WebW_{tag}_{b}_1",
                    W(-halfW, y0, halfW), W(-halfW, y1, -halfW), 0.055f, craneColor);
            }

            // 4. 底部黄黑警示护带（立柱外侧套筒）
            for (int i = 0; i < 3; i++)
            {
                BoxAt(root, $"Skatepark_Crane_Hazard_{tag}_{i}",
                    W(0f, 1.25f + i * 0.25f, 0f),
                    new Vector3(1.22f, 0.18f, 1.22f), (i % 2 == 0) ? yellow : dark, rot);
            }

            // 4.5 塔身中部 3 道黑色加强横箍（强调工业机械节奏）
            float[] midBands = { 4.8f, 6.5f, 8.2f };
            for (int b = 0; b < midBands.Length; b++)
            {
                BoxAt(root, $"Skatepark_Crane_MidBand_{tag}_{b}",
                    W(0f, midBands[b], 0f),
                    new Vector3(halfW * 2f + 0.18f, 0.24f, halfW * 2f + 0.18f), dark, rot);
            }

            // 5. 司机室（外挂在 ~9.2m 处）+ 检修走道 + 护栏 + 朝向场地内侧的白底 CARGO 铭牌
            Vector3 cabPlatCenter = W(-0.85f, 9.15f, 0.35f);
            BoxAt(root, $"Skatepark_Crane_CabPlatform_{tag}",
                cabPlatCenter, new Vector3(1.50f, 0.10f, 1.60f), dark, rot);

            // 走道护栏
            BoxAt(root, $"Skatepark_Crane_CabRailOut_{tag}",
                W(-1.55f, 9.70f, 0.35f), new Vector3(0.05f, 0.05f, 1.55f), craneColor, rot);
            BoxAt(root, $"Skatepark_Crane_CabRailFwd_{tag}",
                W(-0.85f, 9.70f, 1.10f), new Vector3(1.45f, 0.05f, 0.05f), craneColor, rot);
            for (int p = 0; p < 4; p++)
            {
                BoxAt(root, $"Skatepark_Crane_CabPost_{tag}_{p}",
                    W(-0.15f - p * 0.45f, 9.42f, 1.10f), new Vector3(0.04f, 0.55f, 0.04f), dark, rot);
            }

            // 司机室本体（1.35长 × 1.15高 × 1.20宽）
            BoxAt(root, $"Skatepark_Crane_Cab_{tag}",
                W(-0.85f, 9.75f, 0.35f), new Vector3(1.20f, 1.15f, 1.35f), cream, rot);
            // 深色玻璃窗（正前立面与侧立面）
            BoxAt(root, $"Skatepark_Crane_CabGlassFront_{tag}",
                W(-0.85f, 9.85f, 1.04f), new Vector3(1.10f, 0.80f, 0.04f), glass, rot);
            BoxAt(root, $"Skatepark_Crane_CabGlassSide_{tag}",
                W(-1.46f, 9.85f, 0.35f), new Vector3(0.04f, 0.80f, 1.05f), glass, rot);
            // 司机室顶盖
            BoxAt(root, $"Skatepark_Crane_CabRoof_{tag}",
                W(-0.85f, 10.35f, 0.35f), new Vector3(1.35f, 0.10f, 1.50f), dark, rot);

            // 司机室朝向场地中心可见的 CARGO 白色面板 + 4 道深色编码条（前立面与内侧面双向可见）
            // 面向 +Z（沿吊臂直接指向场地中央）
            BoxAt(root, $"Skatepark_Crane_CabStencilPlacardFwd_{tag}",
                W(-0.85f, 9.75f, 1.06f), new Vector3(0.95f, 0.48f, 0.02f), cream, rot);
            for (int s = 0; s < 4; s++)
            {
                BoxAt(root, $"Skatepark_Crane_CabStencilBarFwd_{tag}_{s}",
                    W(-0.85f, 9.90f - s * 0.10f, 1.08f),
                    new Vector3(0.72f, 0.05f, 0.025f), dark, rot);
            }
            // 面向 -X（朝向内场走道侧边，从侧面高台清晰可读）
            BoxAt(root, $"Skatepark_Crane_CabStencilPlacardSide_{tag}",
                W(-1.48f, 9.75f, 0.35f), new Vector3(0.02f, 0.48f, 0.95f), cream, rot);
            for (int s = 0; s < 4; s++)
            {
                BoxAt(root, $"Skatepark_Crane_CabStencilBarSide_{tag}_{s}",
                    W(-1.50f, 9.90f - s * 0.10f, 0.35f),
                    new Vector3(0.025f, 0.05f, 0.72f), dark, rot);
            }

            // 6. 回转支承转盘与塔顶回转台（Y = 12.0m）
            BoxAt(root, $"Skatepark_Crane_Turntable_{tag}",
                W(0f, 12.15f, 0f), new Vector3(1.45f, 0.28f, 1.45f), dark, rot);

            // 7. 塔尖 A 形人字架（从 12.3m 升至 14.55m，对齐 ~14.5m 比例）
            Vector3 apexTip = W(0f, 14.55f, 0.15f);
            BeamBetween(root, $"Skatepark_Crane_ApexLeg_{tag}_0", W(-halfW, 12.3f, -halfW), apexTip, 0.08f, craneColor);
            BeamBetween(root, $"Skatepark_Crane_ApexLeg_{tag}_1", W(halfW, 12.3f, -halfW), apexTip, 0.08f, craneColor);
            BeamBetween(root, $"Skatepark_Crane_ApexLeg_{tag}_2", W(-halfW, 12.3f, halfW), apexTip, 0.08f, craneColor);
            BeamBetween(root, $"Skatepark_Crane_ApexLeg_{tag}_3", W(halfW, 12.3f, halfW), apexTip, 0.08f, craneColor);

            // 塔顶航空障碍红灯
            BoxAt(root, $"Skatepark_Crane_AviationLight_{tag}",
                apexTip + Vector3.up * 0.12f, new Vector3(0.18f, 0.18f, 0.18f), emissive, rot);

            // 8. 主吊臂（缩短为 ~10m 长三角形桁架，朝场地中心延伸：局部 +Z 方向）
            const float jibLen = 10.0f;
            // 下弦杆（双拼）
            BoxAt(root, $"Skatepark_Crane_JibBottomL_{tag}",
                W(-0.34f, 12.35f, 0.5f + jibLen * 0.5f),
                new Vector3(0.08f, 0.08f, jibLen), craneColor, rot);
            BoxAt(root, $"Skatepark_Crane_JibBottomR_{tag}",
                W(0.34f, 12.35f, 0.5f + jibLen * 0.5f),
                new Vector3(0.08f, 0.08f, jibLen), craneColor, rot);
            // 上弦杆（单拼脊梁）
            BoxAt(root, $"Skatepark_Crane_JibTop_{tag}",
                W(0f, 13.20f, 0.5f + jibLen * 0.5f),
                new Vector3(0.08f, 0.08f, jibLen), dark, rot);
            // 臂尖端头
            BoxAt(root, $"Skatepark_Crane_JibTipBlock_{tag}",
                W(0f, 12.75f, 0.5f + jibLen),
                new Vector3(0.76f, 0.95f, 0.12f), craneColor, rot);

            // 吊臂立面腹杆与斜杆（8 节间）
            for (int j = 0; j < 8; j++)
            {
                float z = 1.0f + j * 1.15f;
                Vector3 topPt = W(0f, 13.20f, z);
                BeamBetween(root, $"Skatepark_Crane_JibStrutL_{tag}_{j}",
                    W(-0.34f, 12.35f, z), topPt, 0.05f, craneColor);
                BeamBetween(root, $"Skatepark_Crane_JibStrutR_{tag}_{j}",
                    W(0.34f, 12.35f, z), topPt, 0.05f, craneColor);
                if (j < 7)
                {
                    float nextZ = z + 1.15f;
                    BeamBetween(root, $"Skatepark_Crane_JibDiagL_{tag}_{j}",
                        topPt, W(-0.34f, 12.35f, nextZ), 0.045f, dark);
                    BeamBetween(root, $"Skatepark_Crane_JibDiagR_{tag}_{j}",
                        topPt, W(0.34f, 12.35f, nextZ), 0.045f, dark);
                }
            }

            // 9. 平衡臂（~3.2m）与 2 块混凝土大配重（局部 -Z 方向）
            const float counterLen = 3.2f;
            BoxAt(root, $"Skatepark_Crane_CounterJib_{tag}",
                W(0f, 12.35f, -0.5f - counterLen * 0.5f), new Vector3(0.80f, 0.35f, counterLen), craneColor, rot);
            // 配重 1 & 2
            BoxAt(root, $"Skatepark_Crane_CounterWeight1_{tag}",
                W(0f, 12.05f, -2.40f), new Vector3(1.35f, 1.00f, 0.75f), concreteDark, rot);
            BoxAt(root, $"Skatepark_Crane_CounterWeight2_{tag}",
                W(0f, 12.05f, -3.25f), new Vector3(1.35f, 1.00f, 0.75f), concreteDark, rot);

            // 10. 拉索系统（Apex 塔尖至主臂/平衡臂的悬链拉索）
            BeamBetween(root, $"Skatepark_Crane_TieJibMid_{tag}",
                apexTip, W(0f, 13.20f, 5.5f), 0.035f, dark);
            BeamBetween(root, $"Skatepark_Crane_TieJibTip_{tag}",
                apexTip, W(0f, 13.20f, 9.8f), 0.035f, dark);
            BeamBetween(root, $"Skatepark_Crane_TieCounter_{tag}",
                apexTip, W(0f, 12.40f, -3.5f), 0.035f, dark);

            // 11. 变幅小车与垂挂吊钩（吊钩滑轮组中心精确垂落至离地 3.5m 处）
            const float trolleyZ = 7.0f;
            Vector3 trolleyPos = W(0f, 12.18f, trolleyZ);
            BoxAt(root, $"Skatepark_Crane_Trolley_{tag}",
                trolleyPos, new Vector3(0.80f, 0.24f, 0.60f), dark, rot);

            // 吊绳
            BeamBetween(root, $"Skatepark_Crane_HookCable_{tag}",
                trolleyPos + Vector3.down * 0.12f, W(0f, 3.65f, trolleyZ), 0.035f, dark);
            // 吊钩滑轮组（中心刚好在 Y = 3.50m）与吊钩
            BoxAt(root, $"Skatepark_Crane_HookBlock_{tag}",
                W(0f, 3.50f, trolleyZ), new Vector3(0.46f, 0.38f, 0.36f), dark, rot);
            BoxAt(root, $"Skatepark_Crane_HookShank_{tag}",
                W(0f, 3.20f, trolleyZ), new Vector3(0.16f, 0.38f, 0.20f), craneColor, rot);

            // 12. 作业投光灯（2 盏，朝场地内部向下打光）
            BoxAt(root, $"Skatepark_Crane_FloodL_{tag}",
                W(-0.55f, 11.90f, 0.65f), new Vector3(0.28f, 0.24f, 0.32f), emissive,
                rot * Quaternion.Euler(30f, -15f, 0f));
            BoxAt(root, $"Skatepark_Crane_FloodR_{tag}",
                W(0.55f, 11.90f, 0.65f), new Vector3(0.28f, 0.24f, 0.32f), emissive,
                rot * Quaternion.Euler(30f, 15f, 0f));
        }

        // =====================================================================================
        // 橙色履带式挖掘机（放大 1.3x，精细动臂、液压缸、履带与铲齿）
        // =====================================================================================

        private static void BuildExcavators(Transform root, Material orange, Material dark, Material glass,
            Material emissive, Material yellow, Material steel)
        {
            for (int m = 0; m < 2; m++)
            {
                float sign = m == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.ExcavatorPos;
                float yaw = ArenaLayoutSkatepark.ExcavatorYaw + (m == 0 ? 0f : 180f);
                string tag = m == 0 ? "NW" : "SE";
                BuildExcavator(root, new Vector3(p.x * sign, 0f, p.z * sign), yaw, tag,
                    orange, dark, glass, emissive, yellow, steel);
            }
        }

        private static void BuildExcavator(Transform root, Vector3 basePos, float yaw, string tag,
            Material orange, Material dark, Material glass, Material emissive, Material yellow, Material steel)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float x, float y, float z) => basePos + rot * new Vector3(x, y, z);

            // 1. 履带底盘（左右两条深色履带，中心距 ±1.65m，长 4.4m，高 0.65m；底面精确贴合混凝土台面 Y = 0.30m）
            const float trackCenterY = 0.625f; // 底面 = 0.625 - 0.325 = 0.30m，完全严实接地，零悬空
            for (int side = -1; side <= 1; side += 2)
            {
                string sName = (side < 0) ? "W" : "E";
                // 履带架
                BoxAt(root, $"Skatepark_Excavator_TrackFrame_{tag}_{sName}",
                    W(side * 1.65f, trackCenterY, 0f), new Vector3(0.65f, 0.65f, 4.4f), dark, rot);
                // 履带板踏面（每条 5 组踏板凸起）
                for (int i = 0; i < 5; i++)
                {
                    float z = -1.60f + i * 0.80f;
                    BoxAt(root, $"Skatepark_Excavator_Tread_{tag}_{sName}_{i}",
                        W(side * 1.65f, 0.98f, z), new Vector3(0.68f, 0.10f, 0.42f), dark, rot);
                }
                // 前后驱动轮/导向轮
                BoxAt(root, $"Skatepark_Excavator_SprocketFwd_{tag}_{sName}",
                    W(side * 1.65f, trackCenterY, 2.15f), new Vector3(0.62f, 0.58f, 0.45f), dark, rot);
                BoxAt(root, $"Skatepark_Excavator_SprocketBack_{tag}_{sName}",
                    W(side * 1.65f, trackCenterY, -2.15f), new Vector3(0.62f, 0.58f, 0.45f), dark, rot);
            }

            // 底盘 H 型中央横梁
            BoxAt(root, $"Skatepark_Excavator_ChassisBeam_{tag}",
                W(0f, 0.58f, 0f), new Vector3(2.70f, 0.38f, 1.20f), dark, rot);
            // 回转支承转盘
            BoxAt(root, $"Skatepark_Excavator_Turntable_{tag}",
                W(0f, 0.85f, 0f), new Vector3(1.80f, 0.20f, 1.80f), dark, rot);

            // 2. 上车机身（发动机机棚 + 重型配重块 + 侧面散热百叶）
            BoxAt(root, $"Skatepark_Excavator_EngineHouse_{tag}",
                W(0f, 1.65f, -0.65f), new Vector3(2.80f, 1.45f, 3.80f), orange, rot);
            // 尾部深色配重块
            BoxAt(root, $"Skatepark_Excavator_Counterweight_{tag}",
                W(0f, 1.65f, -2.55f), new Vector3(3.00f, 1.45f, 0.95f), dark, rot);
            // 配重块黄色警示斑马条
            for (int s = 0; s < 3; s++)
            {
                BoxAt(root, $"Skatepark_Excavator_HazardStripe_{tag}_{s}",
                    W(0f, 1.25f + s * 0.30f, -3.05f), new Vector3(2.80f, 0.16f, 0.04f), yellow, rot);
            }
            // 侧边散热格栅百叶（左右各 4 道）
            for (int side = -1; side <= 1; side += 2)
            {
                for (int v = 0; v < 4; v++)
                {
                    BoxAt(root, $"Skatepark_Excavator_Louver_{tag}_{(side < 0 ? 'W' : 'E')}_{v}",
                        W(side * 1.42f, 1.35f + v * 0.20f, -0.60f),
                        new Vector3(0.04f, 0.08f, 1.20f), dark, rot);
                }
            }

            // 3. 驾驶室（偏右/左侧安置，深色玻璃窗 + 橙色顶棚 + ROPS 防滚架柱）
            Vector3 cabCenter = W(0.72f, 2.20f, 0.75f);
            BoxAt(root, $"Skatepark_Excavator_CabGlass_{tag}",
                cabCenter, new Vector3(1.30f, 1.25f, 1.45f), glass, rot);
            BoxAt(root, $"Skatepark_Excavator_CabRoof_{tag}",
                W(0.72f, 2.85f, 0.75f), new Vector3(1.45f, 0.12f, 1.60f), orange, rot);
            BoxAt(root, $"Skatepark_Excavator_CabRops_{tag}",
                W(0.02f, 2.20f, 0.75f), new Vector3(0.10f, 1.25f, 1.46f), dark, rot);

            // 4. 工作装置（动臂 + 斗杆 + 铲斗 + 斗齿 + 液压油缸；铲齿严密咬合压在地面 Y = 0.10m，彻底消除浮空）
            Vector3 boomPivot = W(0f, 1.65f, 1.45f);
            Vector3 boomKnee = W(0f, 3.25f, 3.40f);
            // 动臂（35° 上扬厚实橙色大梁）
            BeamBetween(root, $"Skatepark_Excavator_Boom_{tag}", boomPivot, boomKnee, 0.52f, orange);

            // 动臂主举升双液压缸（从底座前部连接至动臂中部）
            for (int h = -1; h <= 1; h += 2)
            {
                Vector3 cylBase = W(h * 0.55f, 1.15f, 1.50f);
                Vector3 cylMid = W(h * 0.30f, 2.35f, 2.45f);
                BeamBetween(root, $"Skatepark_Excavator_BoomCylBarrel_{tag}_{(h < 0 ? 'L' : 'R')}",
                    cylBase, cylMid, 0.16f, dark);
                BeamBetween(root, $"Skatepark_Excavator_BoomCylRod_{tag}_{(h < 0 ? 'L' : 'R')}",
                    cylMid, W(h * 0.22f, 2.80f, 2.85f), 0.10f, steel);
            }

            // 斗杆（从动臂肘部向下伸向地面土层）
            Vector3 armTip = W(0f, 0.70f, 4.70f);
            BeamBetween(root, $"Skatepark_Excavator_Arm_{tag}", boomKnee, armTip, 0.38f, orange);

            // 斗杆液压缸（背负在动臂顶面）
            BeamBetween(root, $"Skatepark_Excavator_ArmCylBarrel_{tag}",
                W(0f, 3.05f, 2.40f), W(0f, 2.80f, 3.20f), 0.14f, dark);
            BeamBetween(root, $"Skatepark_Excavator_ArmCylRod_{tag}",
                W(0f, 2.80f, 3.20f), W(0f, 2.05f, 3.85f), 0.09f, steel);

            // 铲斗本体（深色重型斗体，底部贴地 Y ≈ 0.08m）
            Vector3 bucketCenter = W(0f, 0.40f, 5.15f);
            BoxAt(root, $"Skatepark_Excavator_Bucket_{tag}",
                bucketCenter, new Vector3(1.15f, 0.65f, 0.85f), dark, rot);

            // 5 颗明黄色铲齿（切入地表，标高 0.10m 紧贴地面，扎实落地）
            for (int t = 0; t < 5; t++)
            {
                float tx = -0.44f + t * 0.22f;
                BoxAt(root, $"Skatepark_Excavator_BucketTooth_{tag}_{t}",
                    W(tx, 0.10f, 5.55f), new Vector3(0.12f, 0.12f, 0.22f), yellow, rot);
            }

            // 铲斗油缸及连杆
            BeamBetween(root, $"Skatepark_Excavator_BucketLink_{tag}",
                W(0f, 1.35f, 4.30f), W(0f, 0.70f, 4.95f), 0.08f, steel);

            // 5. 排气烟囱 + 黄色旋转警示灯
            BoxAt(root, $"Skatepark_Excavator_Exhaust_{tag}",
                W(-0.95f, 2.75f, -1.80f), new Vector3(0.18f, 1.00f, 0.18f), dark, rot);
            BoxAt(root, $"Skatepark_Excavator_Beacon_{tag}",
                W(0.72f, 2.95f, 0.35f), new Vector3(0.18f, 0.20f, 0.18f), emissive, rot);
        }

        // =====================================================================================
        // 办公室（集装箱改造式工地办公室）
        // =====================================================================================

        private static void BuildOffices(Transform root, Material glass, Material dark, Material yellow,
            Material cream, Material[] graffiti)
        {
            Vector3 size = ArenaLayoutSkatepark.OfficeSize;
            for (int m = 0; m < 2; m++)
            {
                float sign = m == 0 ? 1f : -1f;
                Vector3 p = ArenaLayoutSkatepark.OfficePos;
                Vector3 world = new Vector3(p.x * sign, 0f, p.z * sign);
                float yaw = ArenaLayoutSkatepark.OfficeYaw + (m == 0 ? 0f : 180f);
                string tag = m == 0 ? "A" : "B";
                BuildOffice(root, world + Vector3.up * (size.y * 0.5f), yaw, size, tag, glass, dark, yellow, cream, graffiti);
            }
        }

        private static void BuildOffice(Transform root, Vector3 center, float yaw, Vector3 size, string tag,
            Material glass, Material dark, Material yellow, Material cream, Material[] graffiti)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float x, float y, float z) => center + rot * new Vector3(x, y, z);
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            // 两侧长窗带
            for (int side = -1; side <= 1; side += 2)
            {
                BoxAt(root, "Skatepark_Office_Window_" + tag + (side < 0 ? "W" : "E"),
                    W(side * (hx + 0.015f), 0.45f, 0f), new Vector3(0.03f, 0.72f, 2.9f), glass, rot);
                BoxAt(root, "Skatepark_Office_WindowSill_" + tag + (side < 0 ? "W" : "E"),
                    W(side * (hx + 0.03f), 0.04f, 0f), new Vector3(0.06f, 0.08f, 3.0f), dark, rot);
                for (int i = -1; i <= 1; i++)
                {
                    BoxAt(root, "Skatepark_Office_WindowMullion_" + tag + (side < 0 ? "W" : "E") + "_" + (i + 1),
                        W(side * (hx + 0.035f), 0.45f, i * 0.95f), new Vector3(0.04f, 0.72f, 0.06f), dark, rot);
                }
            }

            // 端门 + 雨棚
            BoxAt(root, "Skatepark_Office_Door_" + tag, W(0.55f, -0.9f, hz + 0.025f),
                new Vector3(0.9f, 2.0f, 0.05f), dark, rot);
            BoxAt(root, "Skatepark_Office_DoorHandle_" + tag, W(0.22f, -0.9f, hz + 0.06f),
                new Vector3(0.06f, 0.22f, 0.05f), cream, rot);
            BoxAt(root, "Skatepark_Office_Canopy_" + tag, W(0.55f, 0.28f, hz + 0.42f),
                new Vector3(1.8f, 0.08f, 0.85f), yellow, rot);
            BoxAt(root, "Skatepark_Office_CanopyEdge_" + tag, W(0.55f, 0.23f, hz + 0.83f),
                new Vector3(1.8f, 0.16f, 0.06f), dark, rot);

            // 屋顶压条 + 小护栏
            for (int side = -1; side <= 1; side += 2)
            {
                BoxAt(root, "Skatepark_Office_RoofTrimX_" + tag + (side < 0 ? "W" : "E"),
                    W(side * (hx - 0.04f), hy + 0.04f, 0f), new Vector3(0.1f, 0.1f, size.z), cream, rot);
                BoxAt(root, "Skatepark_Office_RoofTrimZ_" + tag + (side < 0 ? "S" : "N"),
                    W(0f, hy + 0.04f, side * (hz - 0.04f)), new Vector3(size.x, 0.1f, 0.1f), cream, rot);
            }
            for (int i = 0; i < 5; i++)
            {
                float x = -hx + 0.4f + i * (size.x - 0.8f) / 4f;
                BoxAt(root, "Skatepark_Office_RoofPost_" + tag + "_" + i,
                    W(x, hy + 0.55f, hz - 0.1f), new Vector3(0.05f, 1.0f, 0.05f), dark, rot);
            }
            BoxAt(root, "Skatepark_Office_RoofRail_" + tag, W(0f, hy + 1.02f, hz - 0.1f),
                new Vector3(size.x - 0.4f, 0.05f, 0.05f), dark, rot);

            // 屋顶空调 + 侧墙外机
            BoxAt(root, "Skatepark_Office_Ac_" + tag, W(-0.75f, hy + 0.38f, -1.15f),
                new Vector3(0.95f, 0.55f, 0.75f), dark, rot);
            BoxAt(root, "Skatepark_Office_AcFan_" + tag, W(-0.75f, hy + 0.38f, -0.77f),
                new Vector3(0.55f, 0.55f, 0.04f), glass, rot);
            BoxAt(root, "Skatepark_Office_WallAc_" + tag, W(hx + 0.2f, 0.95f, -1.2f),
                new Vector3(0.35f, 0.45f, 0.65f), dark, rot);

            // 涂鸦
            int seed = Mathf.RoundToInt(center.x * 17f + center.z * 5f);
            BuildGraffitiCluster(root, "Skatepark_Graffiti_Office_" + tag + "_S",
                W(-0.8f, -0.85f, hz + 0.02f), rot, graffiti, 1.6f, 0.55f, 3, seed, dark, cream);
            BuildGraffitiCluster(root, "Skatepark_Graffiti_Office_" + tag + "_W",
                W(-hx - 0.02f, -0.1f, 1.2f), rot * Quaternion.Euler(0f, -90f, 0f), graffiti,
                2.2f, 0.8f, 4, seed + 23, dark, cream);
        }

        // =====================================================================================
        // 边界墙大幅多层涂鸦
        // =====================================================================================

        private static void BuildBoundaryGraffiti(Transform root, Material[] graffiti, Material dark, Material cream)
        {
            // 西 / 东墙内侧：大幅色块、深色衬底、斜向轮廓与垂流漆线
            float[] zs = { -22f, -15f, -8f, 0f, 8f, 15f, 22f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                int seed = 1000 + i * 37;
                BuildGraffitiCluster(root, "Skatepark_Graffiti_WestWall_" + i,
                    new Vector3(-19.96f, 0.95f, z), Quaternion.Euler(0f, 90f, 0f), graffiti,
                    3.8f, 0.80f, 5, seed, dark, cream);
                BuildGraffitiCluster(root, "Skatepark_Graffiti_EastWall_" + i,
                    new Vector3(19.96f, 0.95f, -z), Quaternion.Euler(0f, -90f, 0f), graffiti,
                    3.8f, 0.80f, 5, seed + 11, dark, cream);
                if (i % 2 == 0)
                {
                    BuildGraffitiCluster(root, "Skatepark_Graffiti_WestWallHigh_" + i,
                        new Vector3(-19.96f, 2.05f, z + 1.2f), Quaternion.Euler(0f, 90f, 0f), graffiti,
                        2.8f, 0.45f, 4, seed + 71, dark, cream);
                    BuildGraffitiCluster(root, "Skatepark_Graffiti_EastWallHigh_" + i,
                        new Vector3(19.96f, 2.05f, -z - 1.2f), Quaternion.Euler(0f, -90f, 0f), graffiti,
                        2.8f, 0.45f, 4, seed + 83, dark, cream);
                }
            }

            // 北墙内侧：3m 高墙面布置 6 幅大幅涂鸦 + 高位标牌喷漆
            float[] northXs = { -15f, -9f, -3f, 3f, 9f, 15f };
            for (int i = 0; i < northXs.Length; i++)
            {
                int seed = 4000 + i * 53;
                BuildGraffitiCluster(root, "Skatepark_Graffiti_NorthWall_" + i,
                    new Vector3(northXs[i], 0.85f, 27.96f), Quaternion.Euler(0f, 180f, 0f), graffiti,
                    4.2f, 0.85f, 5, seed, dark, cream);
                BuildGraffitiCluster(root, "Skatepark_Graffiti_NorthWallHigh_" + i,
                    new Vector3(northXs[i] + 0.6f, 2.20f, 27.96f), Quaternion.Euler(0f, 180f, 0f), graffiti,
                    3.2f, 0.50f, 4, seed + 97, dark, cream);
            }

            // 出生台出口立墙上的街头 Tag
            BuildGraffitiCluster(root, "Skatepark_Graffiti_SpawnExit_A1",
                new Vector3(-6.5f, 0.65f, -22.95f), Quaternion.identity, graffiti,
                2.4f, 0.65f, 4, 8001, dark, cream);
            BuildGraffitiCluster(root, "Skatepark_Graffiti_SpawnExit_A2",
                new Vector3(6.5f, 0.65f, -22.95f), Quaternion.identity, graffiti,
                2.4f, 0.65f, 4, 8002, dark, cream);
            BuildGraffitiCluster(root, "Skatepark_Graffiti_SpawnExit_B1",
                new Vector3(-6.5f, 0.65f, 22.95f), Quaternion.Euler(0f, 180f, 0f), graffiti,
                2.4f, 0.65f, 4, 8003, dark, cream);
            BuildGraffitiCluster(root, "Skatepark_Graffiti_SpawnExit_B2",
                new Vector3(6.5f, 0.65f, 22.95f), Quaternion.Euler(0f, 180f, 0f), graffiti,
                2.4f, 0.65f, 4, 8004, dark, cream);

            // 南侧海滨矮墙：贴地细长喷漆
            float[] southXs = { -16f, -10f, -2f, 5f, 11f, 17f };
            for (int i = 0; i < southXs.Length; i++)
            {
                BuildGraffitiCluster(root, "Skatepark_Graffiti_SouthWall_" + i,
                    new Vector3(southXs[i], 0.32f, -27.96f), Quaternion.identity, graffiti,
                    2.6f, 0.35f, 3, 7000 + i * 19, dark, cream);
            }
        }

        /// <summary>
        /// 确定性多层街头喷漆色块簇：底层深色投影框 + 多层重叠高对比色块 + 顶部轮廓断线 + 随机竖直垂漆滴落。
        /// </summary>
        private static void BuildGraffitiCluster(
            Transform root,
            string name,
            Vector3 surfaceCenter,
            Quaternion faceRot,
            Material[] palette,
            float width,
            float height,
            int count,
            int seed,
            Material dark,
            Material cream)
        {
            if (palette == null || palette.Length == 0) return;

            // 1. 深色阴影底衬块（增强街头贴图的对比度与体积感）
            BoxAt(root, name + "_Backing",
                surfaceCenter + faceRot * new Vector3(0f, 0f, 0.008f),
                new Vector3(width * 0.95f, height * 0.92f, 0.012f), dark, faceRot);

            // 2. 叠层喷漆主体色块
            for (int i = 0; i < count; i++)
            {
                float u = (Hash01(i, 1, seed) - 0.5f) * width * 0.85f;
                float v = (Hash01(i, 2, seed) - 0.5f) * height * 0.80f;
                float w = Mathf.Lerp(0.60f, 1.60f, Hash01(i, 3, seed)) * (width / (count * 0.7f));
                float h = Mathf.Lerp(0.30f, 0.70f, Hash01(i, 4, seed)) * (height * 0.75f);
                float roll = (Hash01(i, 5, seed) - 0.5f) * 22f;
                // Hash01 的返回值包含 1.0，直接乘 Length 会取到 Length 越界；必须显式夹到 Length-1。
                int matIndex = Mathf.Min(palette.Length - 1, (int)(Hash01(i, 6, seed) * palette.Length));
                Material mat = palette[matIndex] ?? cream;

                Quaternion rot = faceRot * Quaternion.Euler(0f, 0f, roll);
                Vector3 local = new Vector3(u, v, 0.014f + i * 0.003f);
                BoxAt(root, $"{name}_{i}", surfaceCenter + faceRot * local,
                    new Vector3(w, h, 0.02f), mat, rot);

                // 垂落漆滴
                if (Hash01(i, 7, seed) > 0.55f)
                {
                    float dw = Mathf.Lerp(0.035f, 0.065f, Hash01(i, 8, seed));
                    float dh = Mathf.Lerp(0.20f, 0.55f, Hash01(i, 9, seed)) * height;
                    BoxAt(root, $"{name}_Drip_{i}",
                        surfaceCenter + faceRot * new Vector3(u + w * 0.22f, v - h * 0.5f - dh * 0.5f, 0.018f),
                        new Vector3(dw, dh, 0.016f), mat, faceRot);
                }
            }

            // 3. 轮廓裁切线/断笔条（强化街头涂鸦的 outline/tag 特征）
            // seed 可能来自负坐标的 hash；C# 负数取模仍为负，直接索引会越界。
            Material highlightMat = palette[((seed * 3) & 0x7FFFFFFF) % palette.Length] ?? cream;
            BoxAt(root, name + "_Slash",
                surfaceCenter + faceRot * new Vector3(0f, height * 0.42f, 0.026f),
                new Vector3(width * 0.75f, 0.045f, 0.014f), highlightMat,
                faceRot * Quaternion.Euler(0f, 0f, (seed % 2 == 0) ? -7f : 7f));
        }

        // =====================================================================================
        // 铁丝网围栏（1.3m 柱高、顶/中/底三道横杆、每跨 4 根对角交叉斜撑钢丝）
        // =====================================================================================

        private static void BuildFences(Transform root, Material metal)
        {
            // 塔吊周边与挖掘机周边防护围栏
            BuildFenceRun(root, "Skatepark_Fence_CraneN", new Vector3(15.6f, 0f, 18.7f), new Vector3(19.2f, 0f, 18.7f), metal);
            BuildFenceRun(root, "Skatepark_Fence_CraneE", new Vector3(19.2f, 0f, 18.7f), new Vector3(19.2f, 0f, 14.4f), metal);
            BuildFenceRun(root, "Skatepark_Fence_ExcN", new Vector3(-17.6f, 0f, 18.9f), new Vector3(-13.4f, 0f, 18.9f), metal);
            BuildFenceRun(root, "Skatepark_Fence_ExcW", new Vector3(-17.6f, 0f, 18.9f), new Vector3(-17.6f, 0f, 14.4f), metal);
        }

        private static void BuildFenceRun(Transform root, string name, Vector3 a, Vector3 b, Material metal)
        {
            const float height = 1.3f;
            Vector3 flatA = new Vector3(a.x, 0f, a.z);
            Vector3 flatB = new Vector3(b.x, 0f, b.z);
            float length = Vector3.Distance(flatA, flatB);
            if (length < 0.1f) return;
            Vector3 dir = (flatB - flatA) / length;
            int posts = Mathf.Max(2, Mathf.RoundToInt(length / 1.5f) + 1);

            // 立柱
            for (int i = 0; i < posts; i++)
            {
                Vector3 p = Vector3.Lerp(flatA, flatB, i / (float)(posts - 1));
                BoxAt(root, $"{name}_Post_{i}", p + Vector3.up * (height * 0.5f),
                    new Vector3(0.06f, height, 0.06f), metal,
                    Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f));
            }

            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            // 顶杆、中腰杆、贴地底杆
            BoxAt(root, $"{name}_Top", (flatA + flatB) * 0.5f + Vector3.up * height,
                new Vector3(0.05f, 0.05f, length), metal, rot);
            BoxAt(root, $"{name}_Mid", (flatA + flatB) * 0.5f + Vector3.up * 0.70f,
                new Vector3(0.035f, 0.035f, length), metal, rot);
            BoxAt(root, $"{name}_Bottom", (flatA + flatB) * 0.5f + Vector3.up * 0.12f,
                new Vector3(0.035f, 0.035f, length), metal, rot);

            // 每跨单元格加 4 根对角交叉钢丝（形成菱形铁丝网质感）
            for (int i = 0; i < posts - 1; i++)
            {
                Vector3 p0 = Vector3.Lerp(flatA, flatB, i / (float)(posts - 1));
                Vector3 p1 = Vector3.Lerp(flatA, flatB, (i + 1) / (float)(posts - 1));
                Vector3 pMid = (p0 + p1) * 0.5f;

                BeamBetween(root, $"{name}_DiagA_{i}",
                    p0 + Vector3.up * 0.15f, p1 + Vector3.up * 1.25f, 0.025f, metal);
                BeamBetween(root, $"{name}_DiagB_{i}",
                    p0 + Vector3.up * 1.25f, p1 + Vector3.up * 0.15f, 0.025f, metal);
                BeamBetween(root, $"{name}_DiagC_{i}",
                    p0 + Vector3.up * 0.70f, pMid + Vector3.up * 1.25f, 0.020f, metal);
                BeamBetween(root, $"{name}_DiagD_{i}",
                    pMid + Vector3.up * 0.15f, p1 + Vector3.up * 0.70f, 0.020f, metal);
            }
        }

        // =====================================================================================
        // 高空架空高压输电线与工业电杆（10m 电杆跨场飞线，强烈工业港湾剪影）
        // =====================================================================================

        private static void BuildPowerLinesAndPoles(Transform root, Material dark)
        {
            // 1. 东西向横跨输电线路（East-West span across z ≈ 8.0m, y ≈ 9.5 ~ 10.2m）
            Vector3 ewWestPole = new Vector3(-21.8f, 0f, 8.0f);
            Vector3 ewEastPole = new Vector3(21.8f, 0f, 8.0f);
            BuildHighVoltagePole(root, "Skatepark_PwrPole_EW_W", ewWestPole, 10.5f, dark, false);
            BuildHighVoltagePole(root, "Skatepark_PwrPole_EW_E", ewEastPole, 10.5f, dark, false);

            // 东西两根 4 段下垂悬链线
            for (int w = 0; w < 2; w++)
            {
                float zWire = 8.0f + (w == 0 ? -0.65f : 0.65f);
                Vector3 p0 = new Vector3(-21.8f, 10.2f, zWire);
                Vector3 p1 = new Vector3(-10.9f, 9.75f, zWire);
                Vector3 p2 = new Vector3(0.0f, 9.45f, zWire);
                Vector3 p3 = new Vector3(10.9f, 9.75f, zWire);
                Vector3 p4 = new Vector3(21.8f, 10.2f, zWire);

                BeamBetween(root, $"Skatepark_OverheadWire_EW_{w}_0", p0, p1, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_EW_{w}_1", p1, p2, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_EW_{w}_2", p2, p3, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_EW_{w}_3", p3, p4, 0.035f, dark);
            }

            // 2. 南北向纵跨输电线路（North-South span across x ≈ -8.0m, y ≈ 9.5 ~ 10.2m）
            Vector3 nsSouthPole = new Vector3(-8.0f, 0f, -29.5f);
            Vector3 nsNorthPole = new Vector3(-8.0f, 0f, 29.5f);
            BuildHighVoltagePole(root, "Skatepark_PwrPole_NS_S", nsSouthPole, 10.5f, dark, true);
            BuildHighVoltagePole(root, "Skatepark_PwrPole_NS_N", nsNorthPole, 10.5f, dark, true);

            // 南北两根 4 段下垂悬链线
            for (int w = 0; w < 2; w++)
            {
                float xWire = -8.0f + (w == 0 ? -0.65f : 0.65f);
                Vector3 p0 = new Vector3(xWire, 10.2f, -29.5f);
                Vector3 p1 = new Vector3(xWire, 9.78f, -14.75f);
                Vector3 p2 = new Vector3(xWire, 9.50f, 0.0f);
                Vector3 p3 = new Vector3(xWire, 9.78f, 14.75f);
                Vector3 p4 = new Vector3(xWire, 10.2f, 29.5f);

                BeamBetween(root, $"Skatepark_OverheadWire_NS_{w}_0", p0, p1, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_NS_{w}_1", p1, p2, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_NS_{w}_2", p2, p3, 0.035f, dark);
                BeamBetween(root, $"Skatepark_OverheadWire_NS_{w}_3", p3, p4, 0.035f, dark);
            }

            // 3. 东西侧边界墙外侧的原生低压木质/钢电杆
            float[] zs = { -19f, -4f, 11f, 24f };
            for (int side = -1; side <= 1; side += 2)
            {
                string sTag = (side < 0) ? "W" : "E";
                for (int i = 0; i < zs.Length; i++)
                {
                    Vector3 p = new Vector3(side * 20.95f, 0f, zs[i]);
                    BoxAt(root, $"Skatepark_UtilityPole_{sTag}_{i}",
                        p + Vector3.up * 3.5f, new Vector3(0.18f, 7f, 0.18f), dark, Quaternion.identity);
                    BoxAt(root, $"Skatepark_UtilityArm_{sTag}_{i}",
                        p + Vector3.up * 6.15f, new Vector3(1.6f, 0.1f, 0.1f), dark, Quaternion.identity);
                    if (i < zs.Length - 1)
                    {
                        float z0 = zs[i] + 0.2f;
                        float z1 = zs[i + 1] - 0.2f;
                        float span = z1 - z0;
                        for (int wire = 0; wire < 2; wire++)
                        {
                            float x = side * (20.95f + (wire == 0 ? -0.55f : 0.55f));
                            Vector3 w0 = new Vector3(x, 6.55f, z0);
                            Vector3 w1 = new Vector3(x, 6.10f, z0 + span * 0.28f);
                            Vector3 w2 = new Vector3(x, 6.00f, z0 + span * 0.50f);
                            Vector3 w3 = new Vector3(x, 6.10f, z0 + span * 0.72f);
                            Vector3 w4 = new Vector3(x, 6.55f, z1);
                            BeamBetween(root, $"Skatepark_Wire_{sTag}_{i}_{wire}_0", w0, w1, 0.03f, dark);
                            BeamBetween(root, $"Skatepark_Wire_{sTag}_{i}_{wire}_1", w1, w2, 0.03f, dark);
                            BeamBetween(root, $"Skatepark_Wire_{sTag}_{i}_{wire}_2", w2, w3, 0.03f, dark);
                            BeamBetween(root, $"Skatepark_Wire_{sTag}_{i}_{wire}_3", w3, w4, 0.03f, dark);
                        }
                    }
                }
            }
        }

        private static void BuildHighVoltagePole(Transform root, string name, Vector3 basePos, float height, Material dark, bool crossarmAlongX)
        {
            // 主电杆主轴
            BoxAt(root, $"{name}_Post", basePos + Vector3.up * (height * 0.5f),
                new Vector3(0.24f, height, 0.24f), dark, Quaternion.identity);

            // 横担支架与斜撑
            Vector3 armScale = crossarmAlongX ? new Vector3(2.40f, 0.12f, 0.12f) : new Vector3(0.12f, 0.12f, 2.40f);
            BoxAt(root, $"{name}_Arm", basePos + Vector3.up * (height - 0.30f),
                armScale, dark, Quaternion.identity);

            // 电瓷绝缘子
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 insOffset = crossarmAlongX ? new Vector3(k * 0.95f, -0.15f, 0f) : new Vector3(0f, -0.15f, k * 0.95f);
                BoxAt(root, $"{name}_Insulator_{k}", basePos + Vector3.up * (height - 0.30f) + insOffset,
                    new Vector3(0.10f, 0.24f, 0.10f), dark, Quaternion.identity);
            }
        }

        // =====================================================================================
        // 南侧港湾岸线细节（系缆桩、防撞护舷、救生圈、垂直检修钢梯、系缆羊角）
        // =====================================================================================

        private static void BuildHarborEdgeDressing(Transform root, Material dark, Material yellow, Material cream, Material orange)
        {
            // 1. 9 组铸铁立式系缆桩 + 醒目黄色防挂帽
            for (int i = 0; i < 9; i++)
            {
                float x = -18f + i * 4.5f;
                BoxAt(root, "Skatepark_Bollard_" + i, new Vector3(x, 0.28f, -27.98f),
                    new Vector3(0.22f, 0.55f, 0.22f), dark, Quaternion.identity);
                BoxAt(root, "Skatepark_BollardCap_" + i, new Vector3(x, 0.58f, -27.98f),
                    new Vector3(0.32f, 0.07f, 0.32f), yellow, Quaternion.identity);
            }

            // 2. 6 组橡胶防撞靠球
            for (int i = 0; i < 6; i++)
            {
                float x = -16.5f + i * 6.6f;
                BoxAt(root, "Skatepark_Fender_" + i, new Vector3(x, 0.34f, -27.9f),
                    new Vector3(0.62f, 0.62f, 0.28f), dark, Quaternion.identity);
            }

            // 3. 救生圈柱
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 c = new Vector3(side * 8.5f, 0.95f, -27.92f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2f;
                    BoxAt(root, "Skatepark_LifeRing_" + (side < 0 ? "W" : "E") + "_" + i,
                        c + new Vector3(Mathf.Cos(a) * 0.22f, Mathf.Sin(a) * 0.22f, 0f),
                        new Vector3(0.16f, 0.07f, 0.07f), i % 2 == 0 ? cream : orange,
                        Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
                }
                BoxAt(root, "Skatepark_LifeRingPost_" + (side < 0 ? "W" : "E"), c + Vector3.up * 0.32f,
                    new Vector3(0.06f, 0.5f, 0.06f), yellow, Quaternion.identity);
            }

            // 4. 码头直梯（2 部垂直钢爬梯，两侧立杆 + 6 级踏步横档）
            float[] ladderXs = { -12.0f, 12.0f };
            for (int l = 0; l < ladderXs.Length; l++)
            {
                float lx = ladderXs[l];
                // 左右两根导轨
                for (int s = -1; s <= 1; s += 2)
                {
                    BoxAt(root, $"Skatepark_QuayLadder_Rail_{l}_{s}",
                        new Vector3(lx + s * 0.24f, -0.30f, -27.95f),
                        new Vector3(0.045f, 1.80f, 0.045f), yellow, Quaternion.identity);
                    // 码头顶部圆弧扶手出头
                    BoxAt(root, $"Skatepark_QuayLadder_Grip_{l}_{s}",
                        new Vector3(lx + s * 0.24f, 0.85f, -27.95f),
                        new Vector3(0.045f, 0.50f, 0.045f), yellow, Quaternion.identity);
                }
                // 6 级爬梯横档
                for (int r = 0; r < 6; r++)
                {
                    float ry = -1.0f + r * 0.30f;
                    BoxAt(root, $"Skatepark_QuayLadder_Rung_{l}_{r}",
                        new Vector3(lx, ry, -27.95f),
                        new Vector3(0.48f, 0.035f, 0.035f), dark, Quaternion.identity);
                }
            }

            // 5. 3 处系缆羊角（Mooring cleats，水平牛角 + 双支座）
            float[] cleatXs = { -6.0f, 0.0f, 6.0f };
            for (int c = 0; c < cleatXs.Length; c++)
            {
                float cx = cleatXs[c];
                // 羊角横梁
                BoxAt(root, $"Skatepark_MooringCleat_Horn_{c}",
                    new Vector3(cx, 0.62f, -27.95f),
                    new Vector3(0.55f, 0.08f, 0.14f), dark, Quaternion.identity);
                // 两个立柱短座
                BoxAt(root, $"Skatepark_MooringCleat_LegA_{c}",
                    new Vector3(cx - 0.14f, 0.55f, -27.95f),
                    new Vector3(0.09f, 0.12f, 0.10f), dark, Quaternion.identity);
                BoxAt(root, $"Skatepark_MooringCleat_LegB_{c}",
                    new Vector3(cx + 0.14f, 0.55f, -27.95f),
                    new Vector3(0.09f, 0.12f, 0.10f), dark, Quaternion.identity);
            }
        }

        // =====================================================================================
        // 场地高架十字投光灯塔（4 处角落升至 10m，十字交叉斜撑，3 盏大灯头）
        // =====================================================================================

        private static void BuildFloodlightTowers(Transform root, Material dark, Material emissive)
        {
            Vector2[] spots =
            {
                new Vector2(-19.2f, 25.4f),
                new Vector2(19.2f, 25.4f),
                new Vector2(-19.2f, -25.4f),
                new Vector2(19.2f, -25.4f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 p = new Vector3(spots[i].x, 0f, spots[i].y);

                // 10m 主钢管桅杆
                BoxAt(root, $"Skatepark_FloodTower_Pole_{i}",
                    p + Vector3.up * 5.0f, new Vector3(0.26f, 10.0f, 0.26f), dark, Quaternion.identity);

                // 底部四向斜撑支腿（A-frame 扩散腿）
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        BeamBetween(root, $"Skatepark_FloodTower_Brace_{i}_{sx}_{sz}",
                            p + new Vector3(sx * 0.70f, 0.05f, sz * 0.70f),
                            p + Vector3.up * 3.2f, 0.07f, dark);
                    }
                }

                // 顶部安装横担平台
                BoxAt(root, $"Skatepark_FloodTower_HeadRack_{i}",
                    p + Vector3.up * 9.85f, new Vector3(1.30f, 0.18f, 0.55f), dark, Quaternion.identity);

                // 3 盏大号泛光灯（朝向场地中央倾斜 25°）
                Vector3 lookTarget = new Vector3(0f, 0.5f, 0f);
                Vector3 toCenter = (lookTarget - (p + Vector3.up * 10f)).normalized;
                Quaternion lampRot = Quaternion.LookRotation(toCenter, Vector3.up);

                for (int l = -1; l <= 1; l++)
                {
                    Vector3 lampPos = p + new Vector3(l * 0.40f, 9.80f, 0f) + toCenter * 0.25f;
                    // 灯罩外壳
                    BoxAt(root, $"Skatepark_FloodLampHousing_{i}_{l}",
                        lampPos, new Vector3(0.32f, 0.26f, 0.35f), dark, lampRot);
                    // 发光面
                    BoxAt(root, $"Skatepark_FloodLampFace_{i}_{l}",
                        lampPos + toCenter * 0.16f, new Vector3(0.28f, 0.22f, 0.05f), emissive, lampRot);
                }
            }
        }

        // =====================================================================================
        // 场地边缘工业道具组（高密度工业港湾：轮胎堆/托盘/油桶/路障/木箱/滑板/喷罐/锥桶/电缆盘）
        // =====================================================================================

        private static void BuildSiteProps(Transform root, Material dark, Material concreteDark,
            Material yellow, Material orange, Material cream, Material wood, Material blue,
            Material[] graffitiPalette, Material steel)
        {
            // 1. 轮胎堆（4 处，每处 3 层轮胎，每只轮胎 8 个六边环段 + 轮毂中心）
            // 全部贴边界墙摆放：旧版 (12.9, ±14.6) 落在出生台到中场的主动线上，
            // 0.6m 高的轮胎堆看着像硬掩体，实际没有碰撞，会诱导玩家往「假掩体」后面躲。
            Vector3[] tirePiles =
            {
                new Vector3(18.7f, 0f, 14.6f),
                new Vector3(-18.7f, 0f, -14.6f),
                new Vector3(18.4f, 0f, -10.8f),
                new Vector3(-18.4f, 0f, 10.8f)
            };
            for (int p = 0; p < tirePiles.Length; p++)
            {
                Vector3 c = tirePiles[p];
                for (int level = 0; level < 3; level++)
                {
                    float y = 0.10f + level * 0.20f;
                    float rotOff = level * 0.45f + p * 0.2f;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * Mathf.PI * 2f + rotOff;
                        Vector3 pos = c + new Vector3(Mathf.Cos(a) * 0.36f, y, Mathf.Sin(a) * 0.36f);
                        BoxAt(root, $"Skatepark_Tire_P{p}_L{level}_{i}", pos,
                            new Vector3(0.26f, 0.18f, 0.15f), dark,
                            Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
                    }
                    BoxAt(root, $"Skatepark_TireHub_P{p}_L{level}", c + Vector3.up * (y + 0.01f),
                        new Vector3(0.38f, 0.12f, 0.38f), concreteDark, Quaternion.identity);
                }
            }

            // 2. 托盘堆已移除：三四个 0.04m 厚的薄板叠在一起，在俯视/斜视里读作
            //    「浮在地面上的橙色横条」，而且同样没有碰撞，放在主动线上只会误导走位。

            // 3. 蓝/红油桶组已移除：原位置 (16.5, ±8.2) 与布局层实体 BarrelCluster (17.4, ±7.2)
            //    重叠，两组桶互相穿插；实体掩体由 ArenaLayoutSkatepark 的 BarrelCluster 负责。

            // 4. 水泥防撞路障已移除：0.7m 高的新泽西护栏摆在中场主动线 (6, ±11.2) 上却没有碰撞，
            //    是场内最容易被误读成硬掩体的道具；中场掩体统一用可涂墨的实体箱体。

            // 5. 堆叠木箱已移除：原位置 (11.8, ±13.4) 与布局层实体 CrateCluster (11.6, ±12.6)
            //    基本重叠；重复体块除了穿模，还会让「哪一垛有碰撞」变得不可读。

            // 6. 靠在集装箱与墙边的滑板（5 块，强化滑板场主题）
            //    注：C# 不允许在方法内声明局部 struct，位置/朝向/板面材质拆成平行数组。
            Vector3[] skateboardPositions =
            {
                new Vector3(-14.3f, 0f, 13.5f), // 靠单层蓝箱
                new Vector3(14.3f, 0f, 7.5f),   // 靠双层箱堆
                new Vector3(11.9f, 0f, 19.6f),  // 靠办公室
                new Vector3(-19.5f, 0f, 1.5f)   // 靠西侧边界墙
            };
            float[] skateboardYaws = { 160f, 20f, 90f, 90f };
            int[] skateboardDeckPalette = { 0, 2, 1, -1 };
            for (int s = 0; s < skateboardPositions.Length; s++)
            {
                Vector3 sbPos = skateboardPositions[s];
                float sbYaw = skateboardYaws[s];
                Material sbDeckMat = skateboardDeckPalette[s] < 0
                    ? yellow
                    : graffitiPalette[skateboardDeckPalette[s] % graffitiPalette.Length];
                // 倾斜靠墙（绕局部 X 轴旋转 65°）
                Quaternion tiltRot = Quaternion.Euler(0f, sbYaw, 0f) * Quaternion.Euler(65f, 0f, 0f);
                // 板面
                BoxAt(root, $"Skatepark_Skateboard_Deck_{s}", sbPos + Vector3.up * 0.36f,
                    new Vector3(0.22f, 0.025f, 0.80f), sbDeckMat, tiltRot);
                // 磨砂防滑砂纸
                BoxAt(root, $"Skatepark_Skateboard_Grip_{s}", sbPos + Vector3.up * 0.37f,
                    new Vector3(0.20f, 0.006f, 0.76f), dark, tiltRot);
                // 桥架 (Trucks)
                BoxAt(root, $"Skatepark_Skateboard_TruckFwd_{s}", sbPos + tiltRot * new Vector3(0f, -0.025f, 0.25f) + Vector3.up * 0.36f,
                    new Vector3(0.18f, 0.03f, 0.03f), steel, tiltRot);
                BoxAt(root, $"Skatepark_Skateboard_TruckBack_{s}", sbPos + tiltRot * new Vector3(0f, -0.025f, -0.25f) + Vector3.up * 0.36f,
                    new Vector3(0.18f, 0.03f, 0.03f), steel, tiltRot);
                // 轮子 (4 只)
                for (int w = -1; w <= 1; w += 2)
                {
                    BoxAt(root, $"Skatepark_Skateboard_WheelFwd_{s}_{w}", sbPos + tiltRot * new Vector3(w * 0.09f, -0.025f, 0.25f) + Vector3.up * 0.36f,
                        new Vector3(0.06f, 0.05f, 0.06f), cream, tiltRot);
                    BoxAt(root, $"Skatepark_Skateboard_WheelBack_{s}_{w}", sbPos + tiltRot * new Vector3(w * 0.09f, -0.025f, -0.25f) + Vector3.up * 0.36f,
                        new Vector3(0.06f, 0.05f, 0.06f), cream, tiltRot);
                }
            }

            // 7. 散落喷漆罐（6 罐，分布在涂鸦墙角）
            Vector3[] sprayCans =
            {
                new Vector3(-19.65f, 0.11f, -5.8f),
                new Vector3(-19.65f, 0.11f, -5.5f),
                new Vector3(19.65f, 0.11f, 4.2f),
                new Vector3(-2.8f, 0.11f, 27.65f),
                new Vector3(3.2f, 0.11f, 27.65f),
                new Vector3(14.5f, 0.11f, 7.6f)
            };
            for (int k = 0; k < sprayCans.Length; k++)
            {
                Vector3 cp = sprayCans[k];
                Material canMat = graffitiPalette[k % graffitiPalette.Length];
                BoxAt(root, $"Skatepark_SprayCan_Body_{k}", cp,
                    new Vector3(0.08f, 0.20f, 0.08f), canMat, Quaternion.identity);
                BoxAt(root, $"Skatepark_SprayCan_Cap_{k}", cp + Vector3.up * 0.12f,
                    new Vector3(0.04f, 0.05f, 0.04f), dark, Quaternion.identity);
            }

            // 8. 施工警示路锥（2 组，每组 5 只）
            Vector3[] coneOrigins =
            {
                new Vector3(-12.8f, 0f, 14.5f), // 挖掘机脚下
                new Vector3(15.2f, 0f, 17.5f)   // 塔吊脚下
            };
            for (int g = 0; g < coneOrigins.Length; g++)
            {
                Vector3 orig = coneOrigins[g];
                for (int c = 0; c < 5; c++)
                {
                    Vector3 conePos = orig + new Vector3(c * 0.55f, 0f, (c % 2 == 0) ? 0f : 0.25f);
                    // 方形底座
                    BoxAt(root, $"Skatepark_Cone_Base_{g}_{c}", conePos + Vector3.up * 0.025f,
                        new Vector3(0.38f, 0.04f, 0.38f), dark, Quaternion.identity);
                    // 下段锥体
                    BoxAt(root, $"Skatepark_Cone_Lower_{g}_{c}", conePos + Vector3.up * 0.18f,
                        new Vector3(0.26f, 0.28f, 0.26f), orange, Quaternion.identity);
                    // 中段白色反光条
                    BoxAt(root, $"Skatepark_Cone_Stripe_{g}_{c}", conePos + Vector3.up * 0.38f,
                        new Vector3(0.20f, 0.14f, 0.20f), cream, Quaternion.identity);
                    // 顶段锥头
                    BoxAt(root, $"Skatepark_Cone_Upper_{g}_{c}", conePos + Vector3.up * 0.52f,
                        new Vector3(0.14f, 0.16f, 0.14f), orange, Quaternion.identity);
                }
            }

            // 9. 大型工业电缆盘（2 个木质线盘，放到出生台后方岸台上）
            //    旧位置 (18.2, ±14.5) 是外走廊主动线，1.2m 高的木盘看着像实体掩体却没有碰撞。
            Vector3[] spools =
            {
                new Vector3(6.2f, 0f, 30.8f),
                new Vector3(-6.2f, 0f, -30.8f)
            };
            const float spoolBaseY = 1.2f; // 出生台后岸台顶面
            for (int s = 0; s < spools.Length; s++)
            {
                Vector3 sp = spools[s];
                float yaw = 15f + s * 180f;
                Quaternion sRot = Quaternion.Euler(0f, yaw, 0f);
                // 左右两侧圆形木挡板
                for (int side = -1; side <= 1; side += 2)
                {
                    BoxAt(root, $"Skatepark_CableSpool_Flange_{s}_{side}",
                        sp + sRot * new Vector3(side * 0.40f, spoolBaseY + 0.60f, 0f),
                        new Vector3(0.06f, 1.20f, 1.20f), wood, sRot);
                }
                // 中轴缠绕的黑色粗电缆
                BoxAt(root, $"Skatepark_CableSpool_Core_{s}",
                    sp + Vector3.up * (spoolBaseY + 0.60f),
                    new Vector3(0.74f, 0.58f, 0.58f), dark, sRot);
            }
        }

        // =====================================================================================
        // 材质兜底解析器（当 DressContainer 在 Build 之前被调用时，从场景中懒加载已分配的材质）
        // =====================================================================================

        private static ArenaMaterialSet ResolveMaterialsFromContext(Transform root)
        {
            var set = new ArenaMaterialSet();
            if (root == null) return set;

            var renderers = root.root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var sm = renderers[i].sharedMaterial;
                if (sm == null) continue;
                string n = sm.name;

                if (set.steelDark == null && n.IndexOf("SteelDark", System.StringComparison.OrdinalIgnoreCase) >= 0) set.steelDark = sm;
                else if (set.steel == null && n.IndexOf("Steel", System.StringComparison.OrdinalIgnoreCase) >= 0) set.steel = sm;
                else if (set.concrete == null && n.IndexOf("Concrete", System.StringComparison.OrdinalIgnoreCase) >= 0) set.concrete = sm;
                else if (set.concreteDark == null && n.IndexOf("ConcreteDark", System.StringComparison.OrdinalIgnoreCase) >= 0) set.concreteDark = sm;
                else if (set.accent == null && n.IndexOf("Accent", System.StringComparison.OrdinalIgnoreCase) >= 0) set.accent = sm;
                else if (set.glass == null && n.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) set.glass = sm;
                else if (set.graffitiPink == null && n.IndexOf("Pink", System.StringComparison.OrdinalIgnoreCase) >= 0) set.graffitiPink = sm;
                else if (set.graffitiGreen == null && n.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0) set.graffitiGreen = sm;
                else if (set.graffitiCyan == null && n.IndexOf("Cyan", System.StringComparison.OrdinalIgnoreCase) >= 0) set.graffitiCyan = sm;
                else if (set.graffitiPurple == null && n.IndexOf("Purple", System.StringComparison.OrdinalIgnoreCase) >= 0) set.graffitiPurple = sm;
                else if (set.containerRust == null && n.IndexOf("Rust", System.StringComparison.OrdinalIgnoreCase) >= 0) set.containerRust = sm;
                else if (set.containerMustard == null && n.IndexOf("Mustard", System.StringComparison.OrdinalIgnoreCase) >= 0) set.containerMustard = sm;
                else if (set.containerSlate == null && n.IndexOf("Slate", System.StringComparison.OrdinalIgnoreCase) >= 0) set.containerSlate = sm;
                else if (set.containerBlue == null && n.IndexOf("Blue", System.StringComparison.OrdinalIgnoreCase) >= 0) set.containerBlue = sm;
                else if (set.containerTeal == null && n.IndexOf("Teal", System.StringComparison.OrdinalIgnoreCase) >= 0) set.containerTeal = sm;
            }

            return set;
        }

        // =====================================================================================
        // 工具方法
        // =====================================================================================

        private static GameObject BoxAt(Transform root, string name, Vector3 worldPos, Vector3 scale, Material mat, Quaternion worldRot)
        {
            Vector3 localPos = (root != null) ? root.InverseTransformPoint(worldPos) : worldPos;
            Quaternion localRot = (root != null) ? (Quaternion.Inverse(root.rotation) * worldRot) : worldRot;
            var go = ArenaGeometryBuilder.CreateBox(root, name, localPos, scale, mat, localRot);
            StripCollider(go);
            return go;
        }

        private static GameObject BoxRot(Transform root, string name, Vector3 pos, Vector3 scale, Material mat, Quaternion rot)
        {
            return BoxAt(root, name, pos, scale, mat, rot);
        }

        private static void BeamBetween(Transform root, string name, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-4f) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, delta / length);
            BoxAt(root, name, (a + b) * 0.5f, new Vector3(thickness, length, thickness), mat, rot);
        }

        private static void StripCollider(GameObject go)
        {
            if (go == null) return;
            var col = go.GetComponent<Collider>();
            if (col == null) return;
#if UNITY_EDITOR
            Object.DestroyImmediate(col);
#else
            Object.Destroy(col);
#endif
        }

        /// <summary>确定性 hash：同一坐标/种子在每次重建场景时得到相同随机值。</summary>
        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }
    }
}
