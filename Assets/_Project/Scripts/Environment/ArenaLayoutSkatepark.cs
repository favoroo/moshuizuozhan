using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「霓虹海滨工业滑板场 / NEON HARBOR SKATEPARK」的布局数据源。
    ///
    /// 与 <see cref="ArenaLayoutV2"/> 的关系：
    /// - 场地尺寸、边界、出生甲板/台阶/出生线、涂地网格等**玩法常量继续复用 ArenaLayoutV2**，
    ///   保证 40×56 计分网格、出生禁入圈、Bot NavGrid 口径与其它图一致；
    /// - 场内节奏完全重写：中央是下沉式滑板碗池（MID ZONE，3 圈 35° 斜坡壁 + 对角 8 级台阶，最深 2.1m），
    ///   一条 24m 长的对角高架天桥跨过碗池（两端 6m 坡道落地），
    ///   东北/西南角是黄塔吊 + 红底绿顶集装箱堆，西北/东南角是橙色挖掘机工地 + 蓝色集装箱办公室。
    ///
    /// 对称性：所有场内物件按原点 180° 点对称，双方路线长度、掩体数量与高台收益等价。
    /// </summary>
    public static class ArenaLayoutSkatepark
    {
        // =====================================================================================
        // 中央滑板碗池（MID ZONE）
        // =====================================================================================
        /// <summary>碗池开口半尺寸（X/Z）：22 × 14m，占据场地中央。</summary>
        public const float BowlHalfX = 11f;
        public const float BowlHalfZ = 7f;

        /// <summary>斜向多边形环带数量：3 圈连续缓坡环带（单级 1.0m 进深 / 0.70m 落差，坡度 ~35° 可直接跑动）。</summary>
        public const int BowlSlopedBandCount = 3;
        /// <summary>每圈环带多边形段数（32 边形：20 段时池沿折角在俯视/贴墙视角仍能读成多边形）。</summary>
        public const int BowlSegmentsPerBand = 32;
        public const float BowlBandRun = 1.0f;
        public const float BowlBandRise = 0.70f;
        public const float BowlBandThickness = 0.40f;

        /// <summary>旧阶梯环常量（向后兼容保留；新实现采用连续斜坡多边形环）。</summary>
        public const int BowlRingCount = 4;
        public const float BowlStepInset = 0.9f;
        public const float BowlStepHeight = 0.3f;

        /// <summary>碗底高度（地面为 0，碗底深 2.1m）。</summary>
        public const float BowlFloorY = -2.1f;
        /// <summary>碗底半尺寸：8.0 × 4.0m。</summary>
        public const float BowlFloorHalfX = 8.0f;
        public const float BowlFloorHalfZ = 4.0f;

        /// <summary>碗池抬高方块（低矮掩体/长台 + 争夺点）：3.0×0.65×2.0m，沿原点 180° 点对称成对放置，双方各得一块。</summary>
        public static readonly Vector3 BowlBlockPos = new Vector3(-6.2f, 0f, 2.4f);
        public const float BowlBlockSizeX = 3.0f;
        public const float BowlBlockSizeZ = 2.0f;
        public const float BowlBlockHeight = 0.65f;
        public const float BowlBlockTopY = BowlFloorY + BowlBlockHeight;

        /// <summary>碗池中央低脊（3.6×0.55×1.8m）：给 16×8m 的池底一个中心掩体与滑板脊线，桥下可跳上。</summary>
        public static readonly Vector3 BowlCenterBlockPos = Vector3.zero;
        public const float BowlCenterBlockSizeX = 3.6f;
        public const float BowlCenterBlockSizeZ = 1.8f;
        public const float BowlCenterBlockHeight = 0.55f;

        /// <summary>池沿（coping）椭圆半径与分段：沿碗池边缘铺设连续护沿与细管。</summary>
        public const float BowlCopingRadiusX = BowlHalfX + 0.25f;
        public const float BowlCopingRadiusZ = BowlHalfZ + 0.25f;
        public const int BowlCopingSegments = 36;
        public const float BowlRimBandWidth = 0.26f;
        public const float BowlCopingPipeWidth = 0.16f;
        public const float BowlCopingPipeHeight = 0.08f;

        /// <summary>
        /// 对角台阶：NE 与 SW 各一组宽 2.4m、8 级台阶。
        /// 8 级 × 0.2625m = 2.1m 落差，踏面 0.40m（级高 &lt; CC stepOffset 0.30，可直接跑上跑下）；
        /// 台阶沿坡面「挖」出来：每级前沿落在 35° 坡面同高处，向内延伸一级，避免旧版整组台阶被埋在池壁里。
        /// </summary>
        public const int BowlStairStepCount = 8;
        public const float BowlStairWidth = 2.4f;
        public const float BowlStairTread = 0.40f;
        public const float BowlStairRise = 0.2625f;

        // =====================================================================================
        // 对角高架天桥（跨过碗池）
        // =====================================================================================
        /// <summary>天桥绕 Y 轴角度：70° 表示桥体沿「东南—西北」对角线横跨碗池。</summary>
        public const float BridgeYawDegrees = 70f;
        public const float BridgeLength = 24f;
        public const float BridgeWidth = 2.4f;
        public const float BridgeDeckTopY = 2.6f;
        public const float BridgeDeckThickness = 0.22f;
        /// <summary>两端下桥坡道：6m 跑长、22.5° 落差，低端落在本队侧翼通道。</summary>
        public const float BridgeRampLength = 6f;
        public const float BridgeRampPitchDegrees = 22.5f;
        /// <summary>侧面钢桁架高度与腹杆分段。</summary>
        public const float BridgeTrussHeight = 1.05f;
        public const float BridgeTrussBayLength = 1.6f;
        /// <summary>Y 形桥墩局部 Z 轴位置（碗池两侧，纯结构读形）。</summary>
        public const float BridgePierLocalZ = 7.5f;
        public const int BridgeSupportCount = 4;

        // =====================================================================================
        // 集装箱（东北/西南 = 红底绿顶双层堆；西北/东南 = 蓝色单箱；新增边缘集装箱堆）
        // =====================================================================================
        public static readonly Vector3 ContainerSize = new Vector3(2.44f, 2.59f, 6.06f);
        /// <summary>双层堆中心：东北象限，贴合天桥落桥点外侧。</summary>
        public static readonly Vector3 StackPos = new Vector3(14.8f, 0f, 10.5f);
        public const float StackYaw = 20f;
        /// <summary>单层蓝箱中心：西北象限；镜像件自动落在东南象限。</summary>
        public static readonly Vector3 SingleContainerPos = new Vector3(-14.8f, 0f, 10.5f);
        public const float SingleContainerYaw = 160f;

        /// <summary>新增集群 A：蓝队出生后方高位堆（NE 3 层蓝绿集装箱，SW 对称镜像）。</summary>
        public static readonly Vector3 ExtraStackPosA = new Vector3(16.5f, 0f, 21.0f);
        public const float ExtraStackYawA = 15f;

        /// <summary>新增集群 B：东南侧翼通道外侧（SE 2 层绿/蓝绿集装箱，NW 对称镜像）。</summary>
        public static readonly Vector3 ExtraStackPosB = new Vector3(16.0f, 0f, -16.0f);
        public const float ExtraStackYawB = 105f;

        // =====================================================================================
        // 工业地标
        // =====================================================================================
        /// <summary>
        /// 塔吊：东北象限，吊臂伸向碗池上方；镜像件在西南象限。
        /// 基座沿双层堆北面法线 (+0.342, +0.940) 外移 0.4m，使基座与双层堆之间留出 ~1.55m 可通行缝
        /// （玩家胶囊直径 0.9m）；基座与 ExtraA 高层堆仍保持咬合，不会新开第二条缝。
        /// </summary>
        public static readonly Vector3 CranePos = new Vector3(17.54f, 0f, 16.58f);
        public const float CraneYaw = 225f;
        public const float CraneBaseHeight = 1.0f;
        public static readonly Vector3 CraneBaseSize = new Vector3(3.2f, CraneBaseHeight, 3.2f);

        /// <summary>挖掘机工地：西北象限；镜像件在东南象限。</summary>
        public static readonly Vector3 ExcavatorPos = new Vector3(-15.4f, 0f, 16.6f);
        public const float ExcavatorYaw = 135f;
        public const float ExcavatorPlinthHeight = 0.3f;
        public static readonly Vector3 ExcavatorPlinthSize = new Vector3(4.4f, ExcavatorPlinthHeight, 4.4f);

        /// <summary>蓝色集装箱办公室：出生出口东侧，兼作进入中场前的高掩体。</summary>
        public static readonly Vector3 OfficePos = new Vector3(10.4f, 0f, 17.6f);
        public const float OfficeYaw = 90f;
        public static readonly Vector3 OfficeSize = new Vector3(3.0f, 2.8f, 4.6f);

        /// <summary>侧翼 bank 坡道（东侧靠墙；镜像件在西侧），高边贴边界墙、低边朝场地中心。</summary>
        public static readonly Vector3 QuarterPipePos = new Vector3(18.0f, 0f, -4.5f);
        public static readonly Vector3 QuarterPipeSize = new Vector3(3.6f, 0.22f, 3.2f);
        public const float QuarterPipeRollDegrees = 14f;

        /// <summary>
        /// 碗池两侧的滑板长台（0.55m 可跳矮掩体，台面可涂墨）。
        /// 外移到 |x|=14.3：旧值 12.9 的内沿距池沿只有 0.35m，东西向池沿通道变成死缝；
        /// 外移后池沿到长台留 1.75m 可跑通道，长台本身仍贴住侧翼车道。
        /// </summary>
        public static readonly Vector3 LedgePos = new Vector3(14.3f, 0f, 0f);
        public static readonly Vector3 LedgeSize = new Vector3(2.6f, 0.55f, 5.2f);

        /// <summary>天桥局部坐标 → 世界坐标（只用于绕 Y 旋转的桥体/坡道）。</summary>
        public static Vector3 BridgeLocalToWorld(float localX, float localZ)
        {
            Quaternion yaw = Quaternion.Euler(0f, BridgeYawDegrees, 0f);
            return yaw * new Vector3(localX, 0f, localZ);
        }

        // =====================================================================================
        // 场内通用装饰道具（沿用 ArenaPropMeshFactory 的既有道具族）
        // =====================================================================================
        private static readonly List<ArenaLayoutV2.ArenaPropSpec> PropList = new List<ArenaLayoutV2.ArenaPropSpec>(72);

        public static IReadOnlyList<ArenaLayoutV2.ArenaPropSpec> Props => PropList;

        static ArenaLayoutSkatepark()
        {
            BuildProps();
        }

        private static void AddSymmetric(ArenaLayoutV2.ArenaPropKind kind, Vector3 position, float yaw, float scale, int variant)
        {
            PropList.Add(new ArenaLayoutV2.ArenaPropSpec(kind, position, yaw, scale, variant));
            PropList.Add(new ArenaLayoutV2.ArenaPropSpec(
                kind,
                new Vector3(-position.x, position.y, -position.z),
                yaw + 180f,
                scale,
                variant));
        }

        private static void BuildProps()
        {
            // 边界外侧路灯：不参与碰撞，只做工业港口的远景节奏。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -20f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 8f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 22f), 0f, 1f, 0);

            // 出生区外墙的队旗（墙外剪影，不挡子弹）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(-7.5f, 0f, -29.6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(7.5f, 0f, -29.6f), 0f, 1f, 0);

            // 工地锥桶：挖掘机与塔吊脚下各一组，读作"施工中"。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(-12.6f, 0f, 13.4f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(-17.2f, 0f, 2.6f), 0f, 1f, 1);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(16.4f, 0f, -13.2f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(12.4f, 0f, -2.4f), 0f, 1f, 1);

            // 集装箱堆脚下的油桶与木箱（实体掩体，盒代理贴墨）。
            // 油桶组从 (17.4, 7.2) 下移到 3.6：原位置正压在天桥 NE 下桥坡道落点 (16.9, 6.16) 的
            // 西北角上，下桥时会被实体桶卡住；下移 3.6m 后让出完整落地区，同时仍守住侧翼车道。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BarrelCluster, new Vector3(17.4f, 0f, 3.6f), 12f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateCluster, new Vector3(11.6f, 0f, 12.6f), -8f, 0.95f, 1);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateTall, new Vector3(9.2f, 0f, 14.8f), 10f, 1f, 0);

            // 办公室旁的木箱与长椅，给出出生出口的第二段掩体。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateCluster, new Vector3(6.8f, 0f, 16.2f), -14f, 0.9f, 2);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PlanterBox, new Vector3(-7.0f, 0f, 20.2f), 0f, 1f, 0);

            // 出生台到碗池之间的中路矮箱：旧版这 8m 中央通道完全空着，出生后只能直线冲脸。
            // 放在 |x|=3.2、|z|=13.8，左右斜对（180° 点对称），给双方一个可绕的掩体，同时不堵中央。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateCluster, new Vector3(3.2f, 0f, 13.8f), 18f, 0.9f, 2);

            // 边界墙顶的空调外机与管线（高于 1.8m，不参与地面战斗）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.AcUnit, new Vector3(-19.85f, 1.9f, -5.0f), 90f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PipeRun, new Vector3(-19.85f, 2.05f, -6.0f), 0f, 12.0f, 0);
        }
    }
}