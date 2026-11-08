using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 第一张地图（货柜码头）场内"生活痕迹"细节层。
    ///
    /// 旧版场内只剩木箱与花坛：油桶、长椅、锥桶、系缆桩在早期被整体移除，
    /// 理由是"用盒代理会飞出悬空墨片、用精确网格又挡子弹并切碎 NavGrid"。
    /// 于是整张图读起来像空仓库 —— 集装箱是立方体、地面干净、没有一个细碎件。
    ///
    /// 这里补回"不参与碰撞、不参与涂地"的纯装饰散件（与 <see cref="ArenaGeometryBuilder"/> 的
    /// BuildStructuralDecorations 同一约定）：
    /// - **全部贴墙、贴箱根、贴死角**，不落在任何车道中心、坡道口、出生点通道；
    /// - **高度全部 ≤ 1.1m**，且都紧贴已有掩体，不会被误读成"可当掩体"；
    /// - 无 Collider、无 InkWallSurface，合并成 3 张网格（木/金属/发光），
    ///   静态合批后只有 3 个 DrawCall。
    ///
    /// 放置点全部手工核对过 <see cref="ArenaLayoutV2"/> 的车道宽度与坡道口位置。
    /// </summary>
    internal static class ArenaClutterBuilder
    {
        /// <summary>
        /// 生成场内散件。
        /// </summary>
        /// <param name="woodBuf">木材类（托盘、木箱、垫木）。</param>
        /// <param name="metalBuf">金属类（油桶、锥桶、轮胎架、护栏、电缆盘）。</param>
        /// <param name="emissiveBuf">自发光类（少量警示灯、指示灯）。</param>
        public static void Build(ArenaGeometryBuilder.MeshBuffer woodBuf, ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.MeshBuffer emissiveBuf)
        {
            var pal = new ArenaGeometryBuilder.Palette(null);

            BuildPalletsAndCrates(woodBuf, metalBuf, pal);
            BuildBarrelsAndDrums(metalBuf, pal);
            BuildConesAndBollards(metalBuf, pal);
            BuildCableSpools(woodBuf, metalBuf, pal);
            BuildTireStacks(metalBuf, pal);
            BuildGuardRails(metalBuf, pal);
            BuildWallServices(metalBuf, emissiveBuf, pal);
        }

        // =====================================================================================
        // 0. 集装箱喷印（编号 / 标牌 / 品牌条）—— 最能把"色块"读成"真集装箱"的一层
        // =====================================================================================

        /// <summary>
        /// 给场内每个集装箱的长边喷印编号与标牌。
        ///
        /// 为什么必须做：真实集装箱的识别特征除了瓦楞，就是侧面那行白色喷码与
        /// 边角的小标牌。缺了它，即使几何瓦楞做对了，箱子仍读作"刷了漆的大盒子"。
        ///
        /// 实现约束：
        /// - 走纯几何（薄片四边形），不新增材质/贴图，合并进已有的 matte 网格；
        /// - 全部贴在箱面外 <b>0.6cm</b>：墨面 Quad 在 1cm 处，因此涂满墨时
        ///   喷印会被墨盖住（与"喷漆覆盖"的直觉一致），未涂时清晰可读；
        /// - 只对 <c>Container_</c> 前缀的子物体生效，不碰掩体墙与远景。
        /// </summary>
        public static void BuildContainerLivery(
            GameObject arenaRoot,
            ArenaGeometryBuilder.MeshBuffer matteBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            if (arenaRoot == null) return;

            Color inkWhite = new Color(0.94f, 0.94f, 0.92f, 1f);
            Color inkDark = new Color(0.10f, 0.11f, 0.13f, 1f);

            int serial = 0;
            foreach (Transform t in arenaRoot.transform)
            {
                if (t == null || !t.name.StartsWith("Container_", System.StringComparison.Ordinal)) continue;

                Vector3 center = t.localPosition;
                Vector3 size = t.localScale;
                serial++;

                // 长面 = 尺寸较大的水平轴的法线面。场内箱体是 2.2×1.8×4.2，长面朝 ±X。
                bool longAlongZ = size.z >= size.x;
                float halfX = size.x * 0.5f;
                float halfZ = size.z * 0.5f;

                // 两侧长面各来一遍，让从任意方向看都有喷码
                for (int side = -1; side <= 1; side += 2)
                {
                    if (longAlongZ)
                    {
                        // ±X 长面：面朝 ±X，横向沿 Z
                        Vector3 normal = new Vector3(side, 0f, 0f);
                        Vector3 faceCenter = center + new Vector3(side * (halfX + 0.004f), 0f, 0f);
                        PaintStencil(matteBuf, faceCenter, normal, FaceRight(normal),
                            size.z, size.y, serial, inkWhite, inkDark, pal);
                    }
                    else
                    {
                        // ±Z 长面：面朝 ±Z，横向沿 X
                        Vector3 normal = new Vector3(0f, 0f, side);
                        Vector3 faceCenter = center + new Vector3(0f, 0f, side * (halfZ + 0.004f));
                        PaintStencil(matteBuf, faceCenter, normal, FaceRight(normal),
                            size.x, size.y, serial, inkWhite, inkDark, pal);
                    }
                }
            }
        }

        /// <summary>
        /// 站在该面正前方（沿法线看向箱体）时，屏幕上"从左到右"对应的世界方向。
        /// = cross(normal, up)。固定写 (0,0,1) / (1,0,0) 会让另外一侧的
        /// 喷码整片左右镜像（字反着读、CSC 牌跑到另一端）。
        /// </summary>
        private static Vector3 FaceRight(Vector3 normal)
        {
            return Vector3.Cross(normal, Vector3.up).normalized;
        }

        /// <summary>
        /// 在单个长面上画：品牌涂装条 + 编号行 + 技术参数行 + 角部标牌 + 叉车孔警示片。
        /// faceCenter 是箱面中心（高度方向也在正中），faceHeight 是箱高。
        /// </summary>
        private static void PaintStencil(
            ArenaGeometryBuilder.MeshBuffer buf,
            Vector3 faceCenter, Vector3 normal, Vector3 right,
            float faceWidth, float faceHeight,
            int serial,
            Color inkWhite, Color inkDark,
            ArenaGeometryBuilder.Palette pal)
        {
            Vector3 up = Vector3.up;
            float halfH = faceHeight * 0.5f;

            // 1) 顶部品牌条：一条贯穿 56% 宽度的实心浅色带（远看是"公司涂装"）
            float bandW = faceWidth * 0.56f;
            float bandH = 0.11f;
            float bandY = halfH - 0.26f;
            AddDecalQuad(buf, faceCenter + up * bandY, normal, right, up,
                bandW, bandH, inkWhite, 0f);

            // 2) 品牌条右侧压一段深色，形成"双色涂装"的分界（不是纯白条）
            AddDecalQuad(buf, faceCenter + up * bandY + right * (bandW * 0.5f + bandW * 0.09f),
                normal, right, up, bandW * 0.18f, bandH, inkDark, 0f);

            // 3) 编号行：P1 + 4 位箱号（3×5 点阵字，字高 0.26m）
            string code = "P1" + (4000 + serial * 137 % 5000).ToString("0000");
            float glyphH = 0.26f;
            float glyphW = glyphH * 0.62f;
            float gap = glyphW * 0.34f;
            float totalW = code.Length * glyphW + (code.Length - 1) * gap;
            Vector3 codeOrigin = faceCenter - right * (totalW * 0.5f) + up * (halfH - 0.62f);
            for (int i = 0; i < code.Length; i++)
            {
                Vector3 p = codeOrigin + right * (i * (glyphW + gap) + glyphW * 0.5f);
                DrawGlyph(buf, p, normal, right, up, code[i], glyphW, glyphH, inkWhite);
            }

            // 4) 编号下方的尺寸/箱型小字行（更小字高，做出"技术参数"的密度感）
            string sub = (20 + serial % 5) + "G1";
            float subH = 0.125f;
            float subW = subH * 0.62f;
            float subGap = subW * 0.32f;
            float subTotal = sub.Length * subW + (sub.Length - 1) * subGap;
            Vector3 subOrigin = faceCenter - right * (subTotal * 0.5f) + up * (halfH - 0.82f);
            for (int i = 0; i < sub.Length; i++)
            {
                Vector3 p = subOrigin + right * (i * (subW + subGap) + subW * 0.5f);
                DrawGlyph(buf, p, normal, right, up, sub[i], subW, subH, inkWhite);
            }

            // 5) 右上角 CSC 安全标牌：深色底 + 浅色内芯
            float plateW = 0.28f;
            float plateH = 0.18f;
            Vector3 plateCenter = faceCenter + right * (faceWidth * 0.5f - 0.34f) + up * (halfH - 0.46f);
            AddDecalQuad(buf, plateCenter, normal, right, up, plateW, plateH, inkDark, 0f);
            AddDecalQuad(buf, plateCenter, normal, right, up,
                plateW * 0.76f, plateH * 0.62f, new Color(0.80f, 0.82f, 0.84f, 1f), 0.0012f);

            // 6) 左下角叉车孔位置的黄黑警示小片
            for (int i = 0; i < 2; i++)
            {
                Color c = (i % 2 == 0) ? pal.HazardYellow : pal.HazardDark;
                AddDecalQuad(buf, faceCenter - right * (faceWidth * 0.5f - 0.26f - i * 0.13f) - up * (halfH - 0.20f),
                    normal, right, up, 0.11f, 0.13f, c, 0f);
            }
        }

        /// <summary>贴面薄片（自动按纸面朝向给法线，双面都不需要）。</summary>
        private static void AddDecalQuad(
            ArenaGeometryBuilder.MeshBuffer buf,
            Vector3 center, Vector3 normal, Vector3 right, Vector3 up,
            float width, float height, Color color, float extraOffset)
        {
            Vector3 c = center + normal * (0.0015f + extraOffset);
            Vector3 hr = right * (width * 0.5f);
            Vector3 hu = up * (height * 0.5f);

            Vector3 p0 = c - hr - hu;
            Vector3 p1 = c + hr - hu;
            Vector3 p2 = c + hr + hu;
            Vector3 p3 = c - hr + hu;

            // MeshBuffer.AddQuad 的法线 = cross(p3-p0, p2-p0) = cross(up, right) 的 2 倍方向。
            // 贴在 ±X / ±Z 的两侧时该叉积会翻向，背面被剔除（喷印只在箱子一侧可见）。
            // 按目标法线校正绕序。
            if (Vector3.Dot(Vector3.Cross(p3 - p0, p2 - p0), normal) < 0f)
            {
                (p1, p3) = (p3, p1);
            }

            buf.AddQuad(p0, p1, p2, p3, color);
        }

        /// <summary>
        /// 3×5 点阵字形（只覆盖编号里用到的字符）。
        /// 每个"点亮"的格子生成一个小四边形，字符宽度按 5 列映射。
        /// </summary>
        private static void DrawGlyph(
            ArenaGeometryBuilder.MeshBuffer buf,
            Vector3 center, Vector3 normal, Vector3 right, Vector3 up,
            char ch, float glyphW, float glyphH, Color color)
        {
            string[] rows = GlyphRows(ch);
            if (rows == null) return;

            float cellW = glyphW / 3f;
            float cellH = glyphH / 5f;

            for (int r = 0; r < 5; r++)
            {
                string row = rows[r];
                for (int c = 0; c < 3; c++)
                {
                    if (c >= row.Length || row[c] != '#') continue;

                    Vector3 cellCenter = center
                        + right * ((c - 1) * cellW)
                        + up * ((2 - r) * cellH);

                    AddDecalQuad(buf, cellCenter, normal, right, up, cellW * 0.94f, cellH * 0.94f, color, 0f);
                }
            }
        }

        private static string[] GlyphRows(char ch)
        {
            switch (ch)
            {
                case 'P': return new[] { "###", "#.#", "###", "#..", "#.." };
                case '1': return new[] { ".#.", "##.", ".#.", ".#.", "###" };
                case '0': return new[] { "###", "#.#", "#.#", "#.#", "###" };
                case '2': return new[] { "###", "..#", "###", "#..", "###" };
                case '3': return new[] { "###", "..#", "###", "..#", "###" };
                case '4': return new[] { "#.#", "#.#", "###", "..#", "..#" };
                case '5': return new[] { "###", "#..", "###", "..#", "###" };
                case '6': return new[] { "###", "#..", "###", "#.#", "###" };
                case '7': return new[] { "###", "..#", "..#", "..#", "..#" };
                case '8': return new[] { "###", "#.#", "###", "#.#", "###" };
                case '9': return new[] { "###", "#.#", "###", "..#", "###" };
                case 'G': return new[] { "###", "#..", "#.#", "#.#", "###" };
                default: return null;
            }
        }

        // =====================================================================================
        // 木质散件配色
        //
        // 两处坑叠在一起：
        // 1) 结构装饰层的 Palette 是 `new Palette(null)`（兜底色），ArenaStyle.asset 里那套深木色
        //    根本没传下来；
        // 2) 顶点色是按原值直接写进网格的，而项目是 Linear 色彩空间 —— sRGB 0.47 当线性用，
        //    屏幕上会变成 ~0.72，木箱/托盘整堆冲成惨白的"纸箱"。
        // 这里用 .linear 做一次 gamma→linear 转换，屏幕上的木色才等于下面写的这个值。
        // =====================================================================================
        private static readonly Color WoodTone = new Color(0.50f, 0.35f, 0.21f, 1f).linear;
        private static readonly Color WoodToneDark = new Color(0.30f, 0.20f, 0.12f, 1f).linear;

        // =====================================================================================
        // 1. 托盘与木箱堆（集装箱门口、出生区死角）
        // =====================================================================================

        private static void BuildPalletsAndCrates(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 摆放原则（对照 ArenaLayoutV2 逐点核对）：
            //   · 出生台 SpawnDeckWidth=14 → |x|>7 且 |z|>22 是台侧真空地，玩家不经过；
            //   · 四角 |x|>18.5、|z|>25 在围墙内侧死角，无车道；
            //   · 绝不放 |x|∈[17.8,20.0] 的外侧走廊中段（那是 2.1m 的战术绕后路线）。
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(-9.6f, 0f, -25.9f), 0f, 2, 3);
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(9.6f, 0f, 25.9f), 180f, 2, 3);
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(-9.6f, 0f, 25.9f), 0f, 1, 2);
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(9.6f, 0f, -25.9f), 180f, 1, 2);

            // 出生台背后（必须 |x|>7：出生台横跨 x∈[-7,7]、z∈[±22,±28.4]，
            // |x|<7 的坐标会整块埋进 1.2m 高的台体里）
            AddCrateStack(woodBuf, metalBuf, pal, new Vector3(-8.7f, 0f, -27.1f), 12f, 2);
            AddCrateStack(woodBuf, metalBuf, pal, new Vector3(8.7f, 0f, 27.1f), 192f, 2);
            AddCrateStack(woodBuf, metalBuf, pal, new Vector3(11.0f, 0f, -27.2f), 200f, 3);
            AddCrateStack(woodBuf, metalBuf, pal, new Vector3(-11.0f, 0f, 27.2f), 20f, 3);

            // 四角死角托盘（贴两面围墙内角）
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(-18.8f, 0f, -26.2f), 90f, 1, 2);
            AddPalletStack(woodBuf, metalBuf, pal, new Vector3(18.8f, 0f, 26.2f), 270f, 1, 2);
        }

        /// <summary>
        /// 标准欧标托盘（1.2 × 0.14 × 0.8m）+ 上方码放 1~3 层货物。
        /// </summary>
        private static void AddPalletStack(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos, float yaw, int layers, int seed)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            float pw = 1.2f;
            float pd = 0.8f;
            float deckY = 0.14f;

            for (int l = 0; l < layers; l++)
            {
                Vector3 p = basePos + new Vector3(0f, l * deckY, 0f);
                AddSinglePallet(woodBuf, metalBuf, pal, p, rot, pw, pd, deckY);
            }

            // 托盘上的货物：麻袋包（矮而宽的木色块 + 顶部捆扎带）
            float top = basePos.y + layers * deckY;
            int bags = 2 + (seed % 2);
            for (int b = 0; b < bags; b++)
            {
                float bx = (b - (bags - 1) * 0.5f) * 0.34f;
                Vector3 bagPos = basePos + rot * new Vector3(bx, top + 0.16f, 0f);
                woodBuf.AddBox(bagPos, new Vector3(0.30f, 0.32f, 0.62f), Color.Lerp(WoodTone, WoodToneDark, 0.25f), rot);
                // 捆扎带
                metalBuf.AddBox(bagPos + new Vector3(0f, 0.02f, 0f), new Vector3(0.31f, 0.035f, 0.63f),
                    Color.Lerp(pal.SteelDark, Color.black, 0.2f), rot);
            }
        }

        private static void AddSinglePallet(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, Quaternion rot, float pw, float pd, float deckY)
        {
            // 顶板（3 条纵向板，留缝）
            float boardW = pw * 0.30f;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 bp = pos + rot * new Vector3(i * (pw * 0.335f), deckY - 0.018f, 0f);
                woodBuf.AddBox(bp, new Vector3(boardW, 0.036f, pd), WoodTone, rot);
            }

            // 三个纵梁（顺 pd 方向）
            for (int i = -1; i <= 1; i++)
            {
                Vector3 bp = pos + rot * new Vector3(i * (pw * 0.40f), deckY * 0.55f, 0f);
                woodBuf.AddBox(bp, new Vector3(0.10f, deckY * 0.62f, pd), WoodToneDark, rot);
            }

            // 底部横板（两条）
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 bp = pos + rot * new Vector3(0f, 0.02f, i * (pd * 0.36f));
                woodBuf.AddBox(bp, new Vector3(pw, 0.035f, pd * 0.20f), WoodToneDark, rot);
            }

            // 托盘钢带（金属包边，让托盘不是纯木头色块）
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 bp = pos + rot * new Vector3(0f, deckY * 0.55f, i * (pd * 0.5f - 0.012f));
                metalBuf.AddBox(bp, new Vector3(pw * 0.98f, 0.022f, 0.018f), pal.SteelDark, rot);
            }
        }

        /// <summary>
        /// 木箱堆（贴墙死角）：2~3 个大小不一的木箱叠放 + 顶部绑带与角铁。
        /// </summary>
        private static void AddCrateStack(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos, float yaw, int count)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            float[] sizes = { 0.86f, 0.70f, 0.58f };
            for (int i = 0; i < count; i++)
            {
                float s = sizes[i % sizes.Length];
                float y = basePos.y;
                for (int k = 0; k < i; k++) y += sizes[k % sizes.Length];

                Vector3 offs = rot * new Vector3((i % 2 == 0 ? -0.06f : 0.09f) * i, 0f, (i % 2 == 0 ? 0.05f : -0.07f) * i);
                Vector3 c = new Vector3(basePos.x, y + s * 0.5f, basePos.z) + offs;

                AddCrate(woodBuf, metalBuf, pal, c, rot, s);
            }
        }

        private static void AddCrate(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 center, Quaternion rot, float s)
        {
            woodBuf.AddBox(center, new Vector3(s, s, s), WoodTone, rot);

            // 4 根竖向角铁
            float e = s * 0.5f - 0.02f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    metalBuf.AddBox(center + rot * new Vector3(sx * e, 0f, sz * e),
                        new Vector3(0.045f, s + 0.006f, 0.045f), pal.SteelDark, rot);
                }
            }

            // 上下两道横向包边
            for (int sy = -1; sy <= 1; sy += 2)
            {
                metalBuf.AddBox(center + rot * new Vector3(0f, sy * (s * 0.5f - 0.02f), 0f),
                    new Vector3(s + 0.008f, 0.035f, s + 0.008f), pal.SteelDark, rot);
            }

            // 正面斜撑板（木箱识别的关键细节）
            woodBuf.AddBox(center + rot * new Vector3(0f, 0f, s * 0.5f + 0.006f),
                new Vector3(s * 0.94f, 0.055f, 0.014f), WoodToneDark, rot * Quaternion.Euler(0f, 0f, 38f));
        }

        // =====================================================================================
        // 2. 油桶与化工桶
        // =====================================================================================

        private static void BuildBarrelsAndDrums(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 油桶直径 0.58m，属于"会被误读成可当掩体"的中等高件 ——
            // 只放两处：围墙内角（玩家贴墙走时读作墙根杂物）与出生台侧后方（无车道）。
            AddBarrelCluster(metalBuf, pal, new Vector3(-19.35f, 0f, -22.6f), 0f, 3, 0);
            AddBarrelCluster(metalBuf, pal, new Vector3(19.35f, 0f, 22.6f), 180f, 3, 1);
            AddBarrelCluster(metalBuf, pal, new Vector3(-12.4f, 0f, -26.6f), 0f, 2, 2);
            AddBarrelCluster(metalBuf, pal, new Vector3(12.4f, 0f, 26.6f), 180f, 2, 3);

            // 翻倒的桶（贴墙横躺，读作"被随手扔下"）
            AddFallenBarrel(metalBuf, pal, new Vector3(-19.2f, 0f, -20.2f), 32f, pal.ContainerRust);
            AddFallenBarrel(metalBuf, pal, new Vector3(19.2f, 0f, 20.2f), 212f, pal.ContainerTeal);
        }

        private static void AddBarrelCluster(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos, float yaw, int count, int seed)
        {
            Color[] cols =
            {
                pal.ContainerRust, pal.ContainerTeal, pal.ContainerSlate,
                pal.ContainerMustard, Color.Lerp(pal.SteelDark, pal.ContainerRust, 0.35f)
            };

            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + yaw * Mathf.Deg2Rad;
                float r = count <= 1 ? 0f : 0.30f;
                Vector3 p = basePos + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                AddBarrel(metalBuf, pal, p, 0.29f, 0.88f, cols[(i + seed) % cols.Length]);
            }
        }

        private static void AddBarrel(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos, float radius, float height, Color color)
        {
            // 桶身
            metalBuf.AddCylinder(basePos, radius, height, 12, color);
            // 上下两道加强箍
            for (int i = 0; i < 2; i++)
            {
                float y = 0.22f + i * 0.42f;
                metalBuf.AddCylinder(basePos + new Vector3(0f, y, 0f), radius * 1.035f, 0.05f, 12,
                    Color.Lerp(color, pal.SteelDark, 0.55f));
            }
            // 顶盖
            metalBuf.AddCylinder(basePos + new Vector3(0f, height, 0f), radius * 0.94f, 0.02f, 12,
                Color.Lerp(color, pal.SteelDark, 0.3f));
            // 桶口塞
            metalBuf.AddCylinder(basePos + new Vector3(radius * 0.45f, height + 0.02f, 0f), 0.055f, 0.03f, 8, pal.SteelDark);
        }

        private static void AddFallenBarrel(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg, Color color)
        {
            // 躺倒：绕 Z 轴转 90°，中心抬到半径高度
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 90f);
            Matrix4x4 trs = Matrix4x4.TRS(pos + new Vector3(0f, 0.29f, 0f), rot, Vector3.one);
            // 用盒体近似躺倒的桶（圆柱在旋转矩阵里已经够用，这里直接给圆环）
            metalBuf.AddCylinder(Vector3.zero, 0.29f, 0.88f, 12, color, trs);
            metalBuf.AddCylinder(new Vector3(0f, 0.22f, 0f), 0.30f, 0.05f, 12,
                Color.Lerp(color, pal.SteelDark, 0.5f), trs);
            metalBuf.AddCylinder(new Vector3(0f, 0.64f, 0f), 0.30f, 0.05f, 12,
                Color.Lerp(color, pal.SteelDark, 0.5f), trs);
        }

        // =====================================================================================
        // 3. 交通锥与系缆桩
        // =====================================================================================

        private static void BuildConesAndBollards(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 中央四向坡道口的警示锥（真实码头语言，且锥桶只有 0.30m 高，
            // 不会被误读成掩体）。放在坡道踢面外侧 0.55m，避开坡道本体。
            AddConePair(metalBuf, pal, new Vector3(-2.75f, 0f, -9.6f), new Vector3(2.75f, 0f, -9.6f));
            AddConePair(metalBuf, pal, new Vector3(-2.75f, 0f, 9.6f), new Vector3(2.75f, 0f, 9.6f));
            AddConePair(metalBuf, pal, new Vector3(-9.6f, 0f, -2.75f), new Vector3(-9.6f, 0f, 2.75f));
            AddConePair(metalBuf, pal, new Vector3(9.6f, 0f, -2.75f), new Vector3(9.6f, 0f, 2.75f));

            // 侧翼坡道口的锥桶
            AddConePair(metalBuf, pal, new Vector3(-15.35f, 0f, -11.5f), new Vector3(-11.55f, 0f, -11.5f));
            AddConePair(metalBuf, pal, new Vector3(15.35f, 0f, 11.5f), new Vector3(11.55f, 0f, 11.5f));

            // 系缆桩（沿西/东边界墙内侧贴墙 0.35m，间隔 8m）
            for (float z = -24f; z <= 24.1f; z += 8f)
            {
                AddBollard(metalBuf, pal, new Vector3(-19.55f, 0f, z));
                AddBollard(metalBuf, pal, new Vector3(19.55f, 0f, z));
            }

            // 北墙内侧系缆桩（避开横幅与出生台）
            for (float x = -17.5f; x <= -8.1f; x += 4.5f)
            {
                AddBollard(metalBuf, pal, new Vector3(x, 0f, 27.55f));
                AddBollard(metalBuf, pal, new Vector3(-x, 0f, 27.55f));
            }
        }

        private static void AddConePair(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 a, Vector3 b)
        {
            AddCone(metalBuf, pal, a, 0.30f);
            AddCone(metalBuf, pal, b, 0.30f);
        }

        private static void AddCone(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos, float height)
        {
            // 底盘（方座）
            metalBuf.AddBox(basePos + new Vector3(0f, 0.018f, 0f),
                new Vector3(0.34f, 0.036f, 0.34f), pal.HazardDark);

            // 锥身（下粗上细，8 边足够在卡通渲染里读成圆）
            metalBuf.AddCone(basePos + new Vector3(0f, 0.036f, 0f), 0.145f, 0.028f, height - 0.036f, 8, pal.TeamAPad);

            // 反光白环（两道）
            for (int i = 0; i < 2; i++)
            {
                float t = 0.34f + i * 0.34f;
                float r = Mathf.Lerp(0.145f, 0.028f, t);
                metalBuf.AddCone(basePos + new Vector3(0f, 0.036f + (height - 0.036f) * t, 0f),
                    r * 1.06f, r * 0.92f, (height - 0.036f) * 0.13f, 8, new Color(0.95f, 0.95f, 0.94f, 1f));
            }
        }

        private static void AddBollard(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 basePos)
        {
            // 柱身 + 顶帽
            metalBuf.AddCylinder(basePos, 0.17f, 0.42f, 10, pal.SteelDark);
            metalBuf.AddCylinder(basePos + new Vector3(0f, 0.42f, 0f), 0.21f, 0.08f, 10, pal.Steel);
            // 底座法兰
            metalBuf.AddCylinder(basePos, 0.26f, 0.05f, 10, pal.SteelDark);
            // 系缆横臂
            metalBuf.AddBox(basePos + new Vector3(0f, 0.36f, 0f), new Vector3(0.52f, 0.075f, 0.14f), pal.SteelDark);
        }

        // =====================================================================================
        // 4. 电缆盘
        // =====================================================================================

        private static void BuildCableSpools(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 电缆盘高 1.04m，放围墙内角，贴墙摆放（不落在任何车道）
            AddCableSpool(woodBuf, metalBuf, pal, new Vector3(-19.1f, 0f, 24.4f), 0f);
            AddCableSpool(woodBuf, metalBuf, pal, new Vector3(19.1f, 0f, -24.4f), 180f);

            // 平躺的小线轴（躺在地上，读作随手丢弃的线缆）
            AddLyingSpool(woodBuf, metalBuf, pal, new Vector3(-18.3f, 0f, 22.3f), 26f);
            AddLyingSpool(woodBuf, metalBuf, pal, new Vector3(18.3f, 0f, -22.3f), 206f);
        }

        private static void AddCableSpool(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float r = 0.52f;
            float halfW = 0.24f;

            // 两片木质法兰盘（竖立）
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 p = pos + rot * new Vector3(i * halfW, r, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(p, rot * Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                woodBuf.AddCylinder(Vector3.zero, r, 0.06f, 12, WoodTone, trs);
                woodBuf.AddCylinder(new Vector3(0f, 0.06f, 0f), r * 0.92f, 0.03f, 12, WoodToneDark, trs);
            }

            // 中间缠的电缆（深色芯）
            Vector3 mid = pos + rot * new Vector3(0f, r, 0f);
            Matrix4x4 midTrs = Matrix4x4.TRS(mid, rot * Quaternion.Euler(0f, 0f, 90f), Vector3.one);
            woodBuf.AddCylinder(Vector3.zero, r * 0.74f, halfW * 2f - 0.06f, 12, pal.HazardDark, midTrs);

            // 轴心钢管
            metalBuf.AddCylinder(mid, 0.07f, halfW * 2f + 0.10f, 8, pal.Steel, midTrs);

            // 支架（两块立板 + 底横梁）
            for (int i = -1; i <= 1; i += 2)
            {
                metalBuf.AddBox(pos + rot * new Vector3(i * (halfW + 0.12f), 0.16f, 0f),
                    new Vector3(0.06f, 0.32f, 0.34f), pal.SteelDark, rot);
            }
            metalBuf.AddBox(pos + new Vector3(0f, 0.03f, 0f),
                new Vector3((halfW + 0.12f) * 2f + 0.06f, 0.06f, 0.34f), pal.SteelDark, rot);
        }

        private static void AddLyingSpool(
            ArenaGeometryBuilder.MeshBuffer woodBuf,
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float r = 0.34f;

            // 平躺：法兰盘水平，轴向竖直
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 p = pos + rot * new Vector3(i * 0.14f, 0.03f, 0f);
                Matrix4x4 trs = Matrix4x4.TRS(p, rot, Vector3.one);
                woodBuf.AddCylinder(Vector3.zero, r, 0.055f, 10, WoodTone, trs);
            }
            metalBuf.AddCylinder(pos, 0.05f, 0.30f, 6, pal.Steel);
            woodBuf.AddCylinder(pos + new Vector3(0f, 0.055f, 0f), r * 0.68f, 0.10f, 10, pal.HazardDark);
        }

        // =====================================================================================
        // 5. 轮胎堆（码头/滑板场通用的"脏细节"）
        // =====================================================================================

        private static void BuildTireStacks(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 只在四角死角（|x|>18.6、|z|>23.6），任何车道都碰不到
            AddTireStack(metalBuf, pal, new Vector3(-19.1f, 0f, -25.4f), 3, pal.HazardDark);
            AddTireStack(metalBuf, pal, new Vector3(19.1f, 0f, 25.4f), 3, pal.HazardDark);
            AddTireStack(metalBuf, pal, new Vector3(19.1f, 0f, -25.0f), 2, pal.HazardDark);
            AddTireStack(metalBuf, pal, new Vector3(-19.1f, 0f, 25.0f), 2, pal.HazardDark);
        }

        private static void AddTireStack(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, int count, Color color)
        {
            float tireH = 0.20f;
            float outer = 0.36f;
            float inner = 0.20f;

            for (int i = 0; i < count; i++)
            {
                Vector3 p = pos + new Vector3((i % 2 == 0 ? 0.03f : -0.02f) * i, i * tireH, (i % 2 == 0 ? -0.02f : 0.04f) * i);
                AddTire(metalBuf, pal, p, outer, inner, tireH, color);
            }
        }

        /// <summary>用 8 边形圆环拼一个轮胎（外壁 + 内壁 + 上下胎面）。</summary>
        private static void AddTire(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float outer, float inner, float height, Color color)
        {
            const int segs = 10;
            Color tread = Color.Lerp(color, Color.black, 0.25f);
            Color side = Color.Lerp(color, pal.SteelDark, 0.2f);

            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs;
                float a1 = (i + 1) * Mathf.PI * 2f / segs;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);

                Vector3 o0 = pos + new Vector3(c0 * outer, 0f, s0 * outer);
                Vector3 o1 = pos + new Vector3(c1 * outer, 0f, s1 * outer);
                Vector3 o0t = o0 + new Vector3(0f, height, 0f);
                Vector3 o1t = o1 + new Vector3(0f, height, 0f);

                Vector3 i0 = pos + new Vector3(c0 * inner, 0f, s0 * inner);
                Vector3 i1 = pos + new Vector3(c1 * inner, 0f, s1 * inner);
                Vector3 i0t = i0 + new Vector3(0f, height, 0f);
                Vector3 i1t = i1 + new Vector3(0f, height, 0f);

                // 外壁（法线朝外）
                metalBuf.AddQuad(o0, o1, o1t, o0t, tread);
                // 内壁（法线朝内）
                metalBuf.AddQuad(i1, i0, i0t, i1t, tread);
                // 上胎面：法线必须朝上。旧绕序算出来是 (0,-1,0)，
                // 俯视时上胎面被背面剔除 —— 轮胎看起来是个空心圈。
                metalBuf.AddQuad(o0t, o1t, i1t, i0t, side);
                // 下胎面：法线朝下
                metalBuf.AddQuad(i0, i1, o1, o0, side);
            }
        }

        // =====================================================================================
        // 6. 护栏（贴墙的低矮防撞栏）
        // =====================================================================================

        private static void BuildGuardRails(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 出生台两侧贴墙的低矮防撞栏（|x|>7 避开台体；纯作远景层次，
            // 不落在出生通道上，不会制造"看着能挡、实际能穿"的假掩体）
            // 护栏沿本地 Z 铺开：中心 z 与长度必须落在墙内（内墙面 |z|=28.0），
            // 否则会穿出围墙；同时与 z=-27.1 的木箱堆错开 x。
            AddGuardRail(metalBuf, pal, new Vector3(-13.6f, 0f, -27.0f), 0f, 1.9f);
            AddGuardRail(metalBuf, pal, new Vector3(13.6f, 0f, 27.0f), 180f, 1.9f);
        }

        private static void AddGuardRail(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg, float length)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float h = 0.82f;

            // 两根立柱
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 p = pos + rot * new Vector3(0f, 0f, i * (length * 0.5f - 0.08f));
                metalBuf.AddBox(p + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.07f, h, 0.07f), pal.SteelDark, rot);
                metalBuf.AddBox(p + new Vector3(0f, 0.02f, 0f), new Vector3(0.16f, 0.04f, 0.16f), pal.SteelDark, rot);
            }

            // 上下两道横管
            for (int i = 0; i < 2; i++)
            {
                float y = h - 0.06f - i * 0.34f;
                metalBuf.AddBox(pos + new Vector3(0f, y, 0f), new Vector3(0.055f, 0.055f, length), pal.Steel, rot);
            }

            // 中段黄黑反光贴（每 0.6m 一段）
            int bands = Mathf.Max(1, Mathf.RoundToInt(length / 0.6f));
            for (int i = 0; i < bands; i++)
            {
                float t = (i + 0.5f) / bands - 0.5f;
                Color c = (i % 2 == 0) ? pal.HazardYellow : pal.HazardDark;
                metalBuf.AddBox(pos + rot * new Vector3(0f, h - 0.40f, t * length),
                    new Vector3(0.062f, 0.11f, length / bands * 0.86f), c, rot);
            }
        }

        // =====================================================================================
        // 7. 墙面设备：配电箱、管线、挂灯、告示牌
        // =====================================================================================

        private static void BuildWallServices(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.MeshBuffer emissiveBuf,
            ArenaGeometryBuilder.Palette pal)
        {
            // 配电箱：z 必须落在横幅之间的空档里。
            // 西/东墙横幅挂在 z=-18/-6/6/18，各宽 5.2m（占 z±2.6），
            // 空档是 [-15.4,-8.6] / [-3.4,3.4] / [8.6,15.4]。
            float[] zs = { -12f, -4.6f, 4.6f, 12f };
            for (int i = 0; i < zs.Length; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                AddWallCabinet(metalBuf, emissiveBuf, pal, new Vector3(sx * 19.82f, 0f, zs[i]), sx > 0 ? -90f : 90f);
            }

            // 沿墙走的镀锌线管：贴地 0.30m 走（横幅最低到 y=0.25、墙顶 2.4m，
            // 中间没有能避开横幅的净空，走地面线槽才不穿横幅）。
            // yaw 必须是 0：盒体长轴在本地 Z，转 90° 会变成横穿墙体。
            AddWallConduit(metalBuf, pal, -19.80f, -14.6f, 14.6f, 0.30f, 0f);
            AddWallConduit(metalBuf, pal, 19.80f, -14.6f, 14.6f, 0.30f, 0f);

            // 北墙内侧的卷帘门框（伪装成仓库出入口，纯装饰、不开口）。
            // x 必须避开出生台（SpawnDeckWidth=14 → x∈[-7,7]），否则门柱会插进台体；
            // 也不能压横幅（x=±5/±13）与配电箱（x=±17.5）。
            AddRollerDoorFrame(metalBuf, pal, new Vector3(-9.6f, 0f, 27.8f), 180f);
            AddRollerDoorFrame(metalBuf, pal, new Vector3(9.6f, 0f, 27.8f), 180f);
        }

        private static void AddWallCabinet(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.MeshBuffer emissiveBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float w = 0.46f, h = 0.66f, d = 0.16f;

            // 箱体（灰漆）
            metalBuf.AddBox(pos + new Vector3(0f, 1.35f, 0f), new Vector3(w, h, d), Color.Lerp(pal.Steel, pal.SteelDark, 0.5f), rot);
            // 门板（略凸）
            metalBuf.AddBox(pos + rot * new Vector3(0f, 1.35f, d * 0.5f + 0.012f), new Vector3(w * 0.86f, h * 0.86f, 0.022f),
                Color.Lerp(pal.Steel, Color.white, 0.12f), rot);
            // 把手
            metalBuf.AddBox(pos + rot * new Vector3(w * 0.32f, 1.30f, d * 0.5f + 0.03f), new Vector3(0.03f, 0.13f, 0.03f), pal.SteelDark, rot);
            // 顶部散热檐
            metalBuf.AddBox(pos + rot * new Vector3(0f, 1.35f + h * 0.5f + 0.03f, 0.02f), new Vector3(w * 1.08f, 0.05f, d * 1.2f), pal.SteelDark, rot);
            // 运行指示灯（绿色小灯）
            emissiveBuf.AddBox(pos + rot * new Vector3(-w * 0.30f, 1.35f + h * 0.30f, d * 0.5f + 0.026f),
                new Vector3(0.045f, 0.045f, 0.014f), new Color(0.35f, 1.0f, 0.55f, 1f), rot);
            // 高压警示黄标
            metalBuf.AddBox(pos + rot * new Vector3(0f, 1.35f - h * 0.30f, d * 0.5f + 0.026f),
                new Vector3(0.16f, 0.10f, 0.012f), pal.HazardYellow, rot);
        }

        private static void AddWallConduit(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            float x, float zFrom, float zTo, float y, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float len = Mathf.Abs(zTo - zFrom);
            float mid = (zFrom + zTo) * 0.5f;
            // 主管
            metalBuf.AddBox(new Vector3(x, y, mid), new Vector3(0.055f, 0.055f, len), pal.Steel, rot);
            // 每隔 2.4m 一个管卡
            int clamps = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
            for (int i = 0; i <= clamps; i++)
            {
                float z = Mathf.Lerp(zFrom, zTo, i / (float)clamps);
                metalBuf.AddBox(new Vector3(x, y, z), new Vector3(0.09f, 0.09f, 0.045f), pal.SteelDark, rot);
            }
        }

        private static void AddRollerDoorFrame(
            ArenaGeometryBuilder.MeshBuffer metalBuf,
            ArenaGeometryBuilder.Palette pal,
            Vector3 pos, float yawDeg)
        {
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            float w = 4.4f, h = 2.6f;

            // 两侧门柱
            for (int i = -1; i <= 1; i += 2)
            {
                metalBuf.AddBox(pos + rot * new Vector3(i * (w * 0.5f), h * 0.5f, 0f),
                    new Vector3(0.22f, h, 0.22f), pal.SteelDark, rot);
                metalBuf.AddBox(pos + rot * new Vector3(i * (w * 0.5f), 0.05f, 0f),
                    new Vector3(0.34f, 0.10f, 0.34f), pal.SteelDark, rot);
            }

            // 门楣横梁
            metalBuf.AddBox(pos + rot * new Vector3(0f, h + 0.14f, 0f),
                new Vector3(w + 0.5f, 0.28f, 0.26f), pal.SteelDark, rot);

            // 卷帘盒（横梁上方的方形罩）
            metalBuf.AddBox(pos + rot * new Vector3(0f, h + 0.42f, 0f),
                new Vector3(w + 0.2f, 0.30f, 0.34f), Color.Lerp(pal.Steel, pal.SteelDark, 0.6f), rot);

            // 门洞内的暗色背板（读作"门关着"，避免看穿到远景）
            metalBuf.AddBox(pos + rot * new Vector3(0f, h * 0.5f, 0.06f),
                new Vector3(w - 0.3f, h - 0.1f, 0.04f), Color.Lerp(pal.SteelDark, Color.black, 0.55f), rot);

            // 卷帘竖向分条（8 条，做出"卷帘门"的波纹）
            for (int i = 0; i < 8; i++)
            {
                float t = (i + 0.5f) / 8f - 0.5f;
                metalBuf.AddBox(pos + rot * new Vector3(t * (w - 0.4f), h * 0.5f, 0.03f),
                    new Vector3((w - 0.4f) / 8f * 0.86f, h - 0.16f, 0.03f),
                    Color.Lerp(pal.Steel, pal.SteelDark, 0.35f), rot);
            }
        }
    }
}
