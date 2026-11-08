using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 集装箱瓦楞外壳网格工厂（第一张地图视觉精修）。
    ///
    /// 旧集装箱是纯立方体：瓦楞只是 ObstacleDitherLit 里一层"颜色条纹"，
    /// 没有任何法线、AO 或轮廓起伏，斜俯视角下读成一块平板。
    ///
    /// 这里生成"棱脊贴合标称盒面、凹槽向内收"的瓦楞箱体，关键约束：
    /// - 网格写在 unit 空间（-0.5..0.5），随 localScale 缩放 —— 于是 BoxCollider、
    ///   InkWallSurface 的墨面 Quad（由 col.bounds 生成、外扩 1cm）、OcclusionFader、
    ///   静态合批包围盒与旧立方体**逐位一致**，玩法与测试断言不受影响；
    /// - 凹槽一律向内收（棱脊不外凸），因此 1cm 外的墨面 Quad 始终压在棱脊之外，
    ///   涂满墨时不会出现"瓦楞穿出墨面"的破面，墨面仍是干净的一整块；
    /// - 凹槽深度按世界米换算成 unit 空间（除以该轴 localScale），
    ///   所以非均匀缩放的箱体上瓦楞深度在世界空间里仍然一致；
    /// - uv0 是"整面归一化坐标"（u 横向 / v 竖向 0..1）：ObstacleDitherLit 的踢脚线、
    ///   压顶、板缝都读 uv.y，必须跨整面连续，不能按每个瓦楞格重来。
    ///   底色贴图改走世界空间投影（材质开 _BaseMapWorldSpace），因此不吃 uv0 的拉伸。
    /// </summary>
    public static class ArenaContainerMeshFactory
    {
        /// <summary>
        /// 一根瓦楞的相位采样：0..1 为肋距内的位置，权重 1 = 棱脊（外）/ 0 = 凹槽（内）。
        /// 5 个采样点 = 4 段，形成"棱脊平台 → 斜过渡 → 凹槽平台 → 斜过渡"的梯形剖面，
        /// 比 V 形剖面在斜俯视角下更容易读出金属板的硬折面。
        /// </summary>
        private static readonly float[] RibWeights = { 1f, 1f, 0f, 0f, 1f };
        private const int SegmentsPerRib = 4;

        /// <summary>
        /// 生成瓦楞集装箱外壳。
        /// </summary>
        /// <param name="worldSize">标称盒尺寸（米），与 localScale 同值。</param>
        /// <param name="ribPitch">瓦楞肋距（米）。0.36m ≈ 4.6m 长面上 13 根。</param>
        /// <param name="ribDepth">凹槽深度（米）。</param>
        /// <param name="railHeight">上下横梁（不瓦楞的实心带）高度（米）。</param>
        public static Mesh Build(
            Vector3 worldSize,
            float ribPitch = 0.36f,
            float ribDepth = 0.05f,
            float railHeight = 0.1f)
        {
            var verts = new List<Vector3>(2048);
            var norms = new List<Vector3>(2048);
            var uvs = new List<Vector2>(2048);
            var tris = new List<int>(3072);

            float sx = Mathf.Max(0.01f, worldSize.x);
            float sy = Mathf.Max(0.01f, worldSize.y);
            float sz = Mathf.Max(0.01f, worldSize.z);

            // 4 个瓦楞立面：axis 0（法线 ±X，横向跨度 = sz）/ axis 2（法线 ±Z，横向跨度 = sx）
            AddCorrugatedFace(verts, norms, uvs, tris, 0, 1f, sx, sy, sz, ribPitch, ribDepth, railHeight);
            AddCorrugatedFace(verts, norms, uvs, tris, 0, -1f, sx, sy, sz, ribPitch, ribDepth, railHeight);
            AddCorrugatedFace(verts, norms, uvs, tris, 2, 1f, sx, sy, sz, ribPitch, ribDepth, railHeight);
            AddCorrugatedFace(verts, norms, uvs, tris, 2, -1f, sx, sy, sz, ribPitch, ribDepth, railHeight);

            // 顶/底平面
            AddRoofRibs(verts, norms, uvs, tris, +1f, sx, sy, sz, ribPitch, ribDepth);
            AddCap(verts, norms, uvs, tris, -1f);

            var mesh = new Mesh { name = "Container_Corrugated" };
            if (verts.Count > 60000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 单个瓦楞立面。
        /// 竖向 6 行：底梁下沿 / 底梁上沿 / 瓦楞区上沿 / 顶梁下沿 / 顶梁上沿 / 顶。
        /// 同一 v 值出现两次形成水平台阶，台阶法线朝 ±Y ——
        /// 于是"上下横梁"是一圈真正凸起的实心带，而不是画上去的条纹。
        /// </summary>
        private static void AddCorrugatedFace(
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            int axis, float sign,
            float sx, float sy, float sz,
            float ribPitch, float ribDepth, float railHeight)
        {
            float horizontal = axis == 0 ? sz : sx;
            float normalAxisSize = axis == 0 ? sx : sz;

            // 世界米 → unit 空间；夹紧到合理区间，避免极端缩放下把箱体翻面或吃掉整个厚度
            float valleyUnit = Mathf.Clamp(ribDepth / Mathf.Max(0.01f, normalAxisSize), 0f, 0.16f);
            float railUnit = Mathf.Clamp(railHeight / Mathf.Max(0.01f, sy), 0f, 0.35f);

            int ribCount = Mathf.Max(1, Mathf.RoundToInt(horizontal / Mathf.Max(0.05f, ribPitch)));
            int columns = ribCount * SegmentsPerRib + 1;

            // (v, 该行是否压到棱脊平面)
            float[] rowV = { 0f, railUnit, railUnit, 1f - railUnit, 1f - railUnit, 1f };
            bool[] rowCrest = { true, true, false, false, true, true };

            Vector3 outward = axis == 0 ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);

            for (int r = 0; r < rowV.Length - 1; r++)
            {
                bool crestBottom = rowCrest[r];
                bool crestTop = rowCrest[r + 1];

                for (int c = 0; c < columns - 1; c++)
                {
                    float wA = crestBottom ? 1f : RibWeights[c % SegmentsPerRib];
                    float wB = crestBottom ? 1f : RibWeights[(c + 1) % SegmentsPerRib];
                    float wC = crestTop ? 1f : RibWeights[(c + 1) % SegmentsPerRib];
                    float wD = crestTop ? 1f : RibWeights[c % SegmentsPerRib];

                    float uA = (float)c / (columns - 1);
                    float uB = (float)(c + 1) / (columns - 1);

                    Vector3 p0 = FacePoint(axis, sign, uA, rowV[r], wA, valleyUnit);
                    Vector3 p1 = FacePoint(axis, sign, uB, rowV[r], wB, valleyUnit);
                    Vector3 p2 = FacePoint(axis, sign, uB, rowV[r + 1], wC, valleyUnit);
                    Vector3 p3 = FacePoint(axis, sign, uA, rowV[r + 1], wD, valleyUnit);

                    AddQuad(verts, norms, uvs, tris, p0, p1, p2, p3, outward,
                        new Vector2(uA, rowV[r]), new Vector2(uB, rowV[r]),
                        new Vector2(uB, rowV[r + 1]), new Vector2(uA, rowV[r + 1]));
                }
            }
        }

        /// <summary>
        /// unit 空间取样：u/v 为整面归一化坐标，weight 1 = 棱脊（贴标称面）/ 0 = 凹槽（向内收）。
        /// </summary>
        private static Vector3 FacePoint(int axis, float sign, float u, float v, float weight, float valleyUnit)
        {
            float h = -0.5f + u;
            float y = -0.5f + v;
            float n = sign * (0.5f - valleyUnit * (1f - weight));
            return axis == 0 ? new Vector3(n, y, h) : new Vector3(h, y, n);
        }

        /// <summary>
        /// 顶面纵肋：斜俯视角（俯角 48°）下顶面占集装箱可见面积的一半以上，
        /// 纯平顶面会让整箱读成一块色板。海运箱顶面本来就有纵向压型肋，
        /// 这里沿"长轴方向"铺若干道浅肋（凹槽仍向内收，不破坏墨面）。
        ///
        /// 实现为"整张顶面按剖面重采样"而不是"平板 + 贴几片肋"：
        /// 后者会在肋两侧留下没有面的缝隙（能看到箱体内部）。
        /// </summary>
        private static void AddRoofRibs(
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            float sign, float sx, float sy, float sz, float ribPitch, float ribDepth)
        {
            float n = sign * 0.5f;

            // 沿长轴铺肋：长轴 = Z 时肋沿 Z 延伸、沿 X 排布；反之亦然
            bool alongZ = sz >= sx;
            float spanAcross = alongZ ? sx : sz;   // 排布方向跨度（米）

            int ribCount = Mathf.Clamp(Mathf.RoundToInt(spanAcross / Mathf.Max(0.24f, ribPitch * 1.5f)), 2, 9);
            float valleyUnit = Mathf.Clamp(ribDepth / Mathf.Max(0.01f, sy), 0f, 0.09f);

            float margin = 0.09f;      // 端部留边：肋从离边 9% 处才起，两侧留出平板
            float usable = 1f - margin * 2f;
            float pitchT = usable / ribCount;
            // 单道肋剖面：28% 斜坡 / 44% 槽底 / 28% 斜坡
            float slopeT = pitchT * 0.28f;
            float flatT = pitchT * 0.44f;

            // 1) 采样排布方向的剖面位置（0..1）与对应高度（unit 空间 y）
            var ts = new List<float>(ribCount * 5 + 2);
            var ys = new List<float>(ribCount * 5 + 2);
            ts.Add(0f); ys.Add(n);
            for (int i = 0; i < ribCount; i++)
            {
                float start = margin + i * pitchT;
                ts.Add(start + slopeT * 0.5f); ys.Add(n);
                ts.Add(start + slopeT);        ys.Add(n - valleyUnit);
                ts.Add(start + slopeT + flatT); ys.Add(n - valleyUnit);
                ts.Add(start + slopeT + flatT + slopeT * 0.5f); ys.Add(n);
            }
            ts.Add(1f); ys.Add(n);

            // 2) 相邻采样点之间铺四边形（沿肋方向一条到底）。
            //    沿肋方向必须铺满 ±0.5：留边只作用于"肋的起止"，不能缩短顶盖本身，
            //    否则箱顶四周会出现一圈没有面的缝，俯视时能直接看到箱体内部。
            const float alongMin = -0.5f;
            const float alongMax = 0.5f;
            Vector3 outward = new Vector3(0f, sign, 0f);

            for (int k = 0; k < ts.Count - 1; k++)
            {
                float t0 = ts[k], t1 = ts[k + 1];
                float y0 = ys[k], y1 = ys[k + 1];
                if (t1 - t0 < 1e-5f) continue;

                Vector3 p0, p1, p2, p3;
                if (alongZ)
                {
                    float x0 = -0.5f + t0;
                    float x1 = -0.5f + t1;
                    p0 = new Vector3(x0, y0, alongMin);
                    p1 = new Vector3(x1, y1, alongMin);
                    p2 = new Vector3(x1, y1, alongMax);
                    p3 = new Vector3(x0, y0, alongMax);
                }
                else
                {
                    float z0 = -0.5f + t0;
                    float z1 = -0.5f + t1;
                    p0 = new Vector3(alongMin, y0, z0);
                    p1 = new Vector3(alongMin, y1, z1);
                    p2 = new Vector3(alongMax, y1, z1);
                    p3 = new Vector3(alongMax, y0, z0);
                }

                AddQuad(verts, norms, uvs, tris, p0, p1, p2, p3, outward,
                    new Vector2(t0, 0f), new Vector2(t1, 0f), new Vector2(t1, 1f), new Vector2(t0, 1f));
            }
        }

        private static void AddCap(
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, float sign)
        {
            float n = sign * 0.5f;
            Vector3 p0 = new Vector3(-0.5f, n, -0.5f);
            Vector3 p1 = new Vector3(0.5f, n, -0.5f);
            Vector3 p2 = new Vector3(0.5f, n, 0.5f);
            Vector3 p3 = new Vector3(-0.5f, n, 0.5f);
            AddQuad(verts, norms, uvs, tris, p0, p1, p2, p3, new Vector3(0f, sign, 0f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        private static void AddQuad(
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outwardHint,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            // 与 ArenaGeometryBuilder.MeshBuffer 相同的绕序约定：法线 = cross(p3-p0, p2-p0)
            Vector3 n = Vector3.Cross(p3 - p0, p2 - p0);
            if (Vector3.Dot(n, outwardHint) < 0f)
            {
                // 反手：交换顶点顺序同时把 UV 一起换，保证贴图不镜像
                (p1, p3) = (p3, p1);
                (uv1, uv3) = (uv3, uv1);
                n = -n;
            }
            n = n.sqrMagnitude > 1e-12f ? n.normalized : outwardHint;

            int start = verts.Count;
            verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);

            tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
        }
    }
}
