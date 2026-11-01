using System.Collections.Generic;
using P1.Core;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 40×56 集装箱码头竞技场的单一布局数据源（P7-B）：
    /// 尺寸常量、新增装饰道具、地面标线全部集中在这里，几何构建器、装饰构建器与地面烘焙器共用，
    /// 避免坐标散落在多个文件里。所有对称项以原点做 180° 点对称，保证公平性。
    /// </summary>
    public static class ArenaLayoutV2
    {
        public const float MapSizeX = 40f;
        public const float MapSizeZ = 56f;
        public const float HalfX = MapSizeX * 0.5f;
        public const float HalfZ = MapSizeZ * 0.5f;

        public const float BoundaryX = 20.5f;
        public const float BoundaryZ = 28.5f;

        /// <summary>四周边界墙高度（基准 2.4m / 加高 3.0m）。</summary>
        public const float BoundaryWallHeight = 3.0f;
        /// <summary>北侧边界墙高度：防止站在出生甲板顶（1.2m）上的角色跳出场地（顶面 - 甲板顶 ≥ jumpHeight + 0.3）。南侧保持矮墙不变。</summary>
        public const float NorthBoundaryWallHeight = 3.0f;

        // ---- 车道槽位（X 轴，西半区；东侧镜像）----
        // 5.0 中央台边 | 5.0-7.0 内侧车道 | 7.0-9.2 中场集装箱 | 9.2-11.2 外侧车道
        // | 11.2-15.7 侧翼高台 | 15.7-17.9 贴台集装箱 | 17.9-20.0 贴墙外走廊
        /// <summary>中场集装箱中心 |x|：内边 |x|=7.0 贴住掩体墙，外边 |x|=9.2 让出 2.0m 外侧车道。</summary>
        public const float ContainerMidX = 8.1f;

        /// <summary>掩体墙中心 |x| 与宽度：跨在中央台边（|x|=5）与中场集装箱内边（|x|=7）之间，两端贴死。</summary>
        public const float CoverWallX = 6.0f;
        public const float CoverWallWidth = 2.0f;

        /// <summary>侧翼高台中心 |x| 与台面高度：1.5m 高于跳跃 1.35m，坡道成为唯一常规入口。</summary>
        public const float FlankPlatformX = 13.45f;
        public const float FlankPlatformHeight = 1.5f;

        /// <summary>侧翼集装箱中心 |x|：与侧翼台外沿（|x|=15.7）重叠 0.05m 消除死缝，贴墙外走廊 2.10m。</summary>
        public const float ContainerFlankX = 16.75f;

        /// <summary>延长区集装箱中心 |x|：与贴墙外走廊对齐（走廊 2.05m）。</summary>
        public const float ExtensionContainerX = 16.75f;

        // ---- 中路错位低掩体（1.2m 可跳；避开中央坡道底端 4.4m 坡道口，左右交错形成 S 形推进线）----
        public static readonly Vector3 MidCoverNearPos = new Vector3(3.4f, 0.6f, 9.0f);
        public static readonly Vector3 MidCoverNearSize = new Vector3(1.8f, 1.2f, 1.2f);
        public static readonly Vector3 MidCoverFarPos = new Vector3(-2.6f, 0.6f, 16.5f);
        public static readonly Vector3 MidCoverFarSize = new Vector3(1.8f, 1.2f, 1.2f);

        /// <summary>出生线中心 |z|：站在出生甲板顶面上（不再落在地面出生盘）。</summary>
        public const float SpawnZ = 23.5f;
        public const float SpawnGateZ = 21.4f;

        // ---- 出生甲板（对齐 INKWAVE：抬高台面 + 两侧上下通道）----
        /// <summary>出生甲板中心 |z|：台面从后墙一直伸到 z=±22.0（正好顶住 SpawnGate 背面）。</summary>
        public const float SpawnTerraceZ = 25.2f;
        /// <summary>甲板台面高度（m）。</summary>
        public const float SpawnDeckTopY = 1.2f;
        public const float SpawnDeckWidth = 14f;
        public const float SpawnDeckDepth = 6.4f;
        /// <summary>甲板顶面中性钢灰出生盘：盘面深度必须容得下 SpawnPad.shader 的 r=3.15m 外圈。</summary>
        public const float SpawnPadWidth = 8f;
        public const float SpawnPadDepth = 6.3f;
        public const float SpawnPadThickness = 0.06f;
        /// <summary>出生点 Y：台面 + 盘厚 + 6cm 落位余量（与原地面出生点 y=0.22 的抬升习惯一致）。</summary>
        public const float SpawnPointY = SpawnDeckTopY + SpawnPadThickness + 0.06f;

        /// <summary>两侧台阶：沿 X 投影 5m、沿 Z 宽 4m、5 级（0.24m/级 ≤ CC stepOffset 0.30）。</summary>
        public const float SpawnStairRun = 5f;
        public const float SpawnStairWidth = 4f;
        public const int SpawnStairSteps = 5;
        /// <summary>单个出生锚点（旧接口）与出生线同 Z：都落在台面上。</summary>
        public const float SpawnAnchorZ = SpawnZ;

        public static Vector2 MapSize => new Vector2(MapSizeX, MapSizeZ);

        public enum ArenaPropKind
        {
            CrateCluster,
            CrateTall,
            BarrelCluster,
            PlanterBox,
            ConeCluster,
            BollardRow,
            Bench,
            AcUnit,
            PipeRun,
            LampPost,
            PalmTree,
            FlagPole,
            Kiosk,
        }

        public readonly struct ArenaPropSpec
        {
            public readonly ArenaPropKind Kind;
            public readonly Vector3 Position;
            public readonly float Yaw;
            public readonly float Scale;
            public readonly int Variant;

            public ArenaPropSpec(ArenaPropKind kind, Vector3 position, float yaw, float scale, int variant)
            {
                Kind = kind;
                Position = position;
                Yaw = yaw;
                Scale = scale;
                Variant = variant;
            }
        }

        public enum GroundMarkingKind
        {
            LaneDash,
            Chevron,
            HazardBand,
            Ring,
            StencilText,
            SafetyLine,
        }

        public readonly struct GroundMarking
        {
            public readonly GroundMarkingKind Kind;
            public readonly Vector2 CenterXZ;
            public readonly Vector2 SizeXZ;
            public readonly float Yaw;
            public readonly Team Team;
            public readonly string Text;

            public GroundMarking(GroundMarkingKind kind, Vector2 centerXZ, Vector2 sizeXZ, float yaw, Team team, string text = null)
            {
                Kind = kind;
                CenterXZ = centerXZ;
                SizeXZ = sizeXZ;
                Yaw = yaw;
                Team = team;
                Text = text;
            }
        }

        private static readonly List<ArenaPropSpec> PropList = new List<ArenaPropSpec>(96);
        private static readonly List<GroundMarking> MarkingList = new List<GroundMarking>(32);

        public static IReadOnlyList<ArenaPropSpec> Props => PropList;
        public static IReadOnlyList<GroundMarking> GroundMarkings => MarkingList;

        /// <summary>
        /// 该道具是否作为实体掩体参与碰撞。
        /// 地形装饰（锥桶、系缆桩、路灯、棕榈、旗杆、墙顶空调与管线）一律不生成碰撞：
        /// 装饰网格是整场按材质合并后统一挂 MeshCollider 的，只要算进去，
        /// 细杆件就会顶住角色胶囊、挡住子弹，并把 Bot 的 NavGrid 切出无数死格。
        /// </summary>
        public static bool IsSolidProp(ArenaPropKind kind)
        {
            switch (kind)
            {
                case ArenaPropKind.CrateCluster:
                case ArenaPropKind.CrateTall:
                case ArenaPropKind.BarrelCluster:
                case ArenaPropKind.PlanterBox:
                case ArenaPropKind.Bench:
                case ArenaPropKind.Kiosk:
                    return true;
                default:
                    return false;
            }
        }

        static ArenaLayoutV2()
        {
            BuildProps();
            BuildMarkings();
        }

        /// <summary>同时添加原点 180° 点对称的镜像件（yaw 自动 +180°）。</summary>
        private static void AddSymmetric(ArenaPropKind kind, Vector3 position, float yaw, float scale, int variant)
        {
            PropList.Add(new ArenaPropSpec(kind, position, yaw, scale, variant));
            PropList.Add(new ArenaPropSpec(
                kind,
                new Vector3(-position.x, position.y, -position.z),
                yaw + 180f,
                scale,
                variant));
        }

        private static void AddSymmetric(GroundMarkingKind kind, Vector2 centerXZ, Vector2 sizeXZ, float yaw, Team team, string text = null)
        {
            MarkingList.Add(new GroundMarking(kind, centerXZ, sizeXZ, yaw, team, text));
            MarkingList.Add(new GroundMarking(kind, -centerXZ, sizeXZ, yaw + 180f, team, text));
        }

        private static void BuildProps()
        {
            // 延长区箱堆：放在 z≈-18 的两侧，避开 CoverWall_00 / Container_Mid_SW_Mustard 的测试视线。
            AddSymmetric(ArenaPropKind.CrateCluster, new Vector3(-11.2f, 0f, -18.6f), 16f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.CrateCluster, new Vector3(-14.6f, 0f, -19.8f), -12f, 0.9f, 1);
            AddSymmetric(ArenaPropKind.CrateTall, new Vector3(11.4f, 0f, -19.6f), 8f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.CrateCluster, new Vector3(7.6f, 0f, -17.2f), -6f, 0.85f, 2);

            // 场内实体道具只剩"盒状"物件（木箱/花坛/售卖亭）：它们由 ArenaDecorationBuilder
            // 按实体部件包围盒生成盒碰撞代理 + 四面可涂墨面。
            // 原先摆在场内的油桶簇、长椅、锥桶、系缆桩属于曲面/细杆/镂空件，
            // 用盒代理会飞出悬空墨片，用精确网格又会挡住子弹并切碎 NavGrid，
            // 因此按"涂不上的就不摆在场内"的约定移除（网格工厂保留，后续可复用）。
            AddSymmetric(ArenaPropKind.PlanterBox, new Vector3(-6.9f, 0f, -13.6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.PlanterBox, new Vector3(6.9f, 0f, -13.6f), 0f, 1.0f, 0);

            // 边界外侧路灯与棕榈：不参与碰撞，避免挡子弹。
            AddSymmetric(ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -20f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 8f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 22f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.PalmTree, new Vector3(-23.0f, 0f, -25.5f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.PalmTree, new Vector3(23.0f, 0f, -25.5f), 0f, 1.0f, 1);

            // 出生区：队旗 + 售卖亭，形成明确的队伍入口记忆点。
            // 旗杆没有碰撞体（细杆件会卡角色），因此摆在边界墙外 z=±29.6：
            // 场内仍能看到旗面，但不会被当成"打不中的场内物件"。
            AddSymmetric(ArenaPropKind.FlagPole, new Vector3(-4.8f, 0f, -29.6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.FlagPole, new Vector3(4.8f, 0f, -29.6f), 0f, 1.0f, 0);
            // 售卖亭从 z=-26（原出生台后角）挪到出生甲板西前角外侧：
            // 原位置正好压在甲板西侧台阶的落点区，且一半埋进抬高的台面里。
            AddSymmetric(ArenaPropKind.Kiosk, new Vector3(-8.5f, 0f, -21.0f), 0f, 1.0f, 0);

            // 边界墙顶的设备与管线（高于 1.8m，不参与地面战斗）。
            // 管线必须沿墙走向贴墙铺设：PipeRun 沿局部 +Z 生长，所以贴西/东墙要 yaw=0。
            AddSymmetric(ArenaPropKind.AcUnit, new Vector3(-19.85f, 1.9f, -5.0f), 90f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.PipeRun, new Vector3(-19.85f, 2.05f, -6.0f), 0f, 12.0f, 0);

            // 出生区（z=±27.9）不再放墙顶管线：14m 横管正好横在出生点正后方，
            // 管心 2.05m 与第三人称相机吊臂末端（约 2.14m）重合；细装饰没有 Collider，
            // 相机防穿墙探针看不到它，镜头会卡进管身，开局整屏被管壁糊住。
            // 另外南侧边界墙只有 0.55m 高，放在这里的管线本身就是悬空的。
        }

        private static void BuildMarkings()
        {
            // 中央与侧翼车道虚线（沿 Z 轴，2m 实 / 1.5m 空）。
            AddSymmetric(GroundMarkingKind.LaneDash, new Vector2(6.2f, 0f), new Vector2(0.16f, 44f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.LaneDash, new Vector2(10.4f, 0f), new Vector2(0.14f, 40f), 0f, Team.Neutral);

            // 侧翼导向箭头：Team A 半场指向 +Z，镜像后 Team B 指向 -Z。
            AddSymmetric(GroundMarkingKind.Chevron, new Vector2(10.4f, -14.0f), new Vector2(2.0f, 2.4f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.Chevron, new Vector2(10.4f, -19.5f), new Vector2(2.0f, 2.4f), 0f, Team.Neutral);

            // 出生出口的宽幅队色导向条。
            AddSymmetric(GroundMarkingKind.Chevron, new Vector2(0f, -21.0f), new Vector2(5.6f, 2.4f), 0f, Team.TeamA);

            // 坡道口黄黑警示带（中央四向 + 侧翼四向）。
            AddSymmetric(GroundMarkingKind.HazardBand, new Vector2(0f, -5.15f), new Vector2(4.4f, 0.5f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.HazardBand, new Vector2(-5.15f, 0f), new Vector2(0.5f, 4.4f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.HazardBand, new Vector2(-13.5f, -4.55f), new Vector2(3.6f, 0.5f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.HazardBand, new Vector2(-13.5f, 4.55f), new Vector2(3.6f, 0.5f), 0f, Team.Neutral);

            // 中央争夺圈与场地字标。
            AddSymmetric(GroundMarkingKind.Ring, Vector2.zero, new Vector2(3.4f, 0.16f), 0f, Team.Neutral);
            MarkingList.Add(new GroundMarking(GroundMarkingKind.StencilText, Vector2.zero, new Vector2(3.2f, 1.4f), 0f, Team.Neutral, "P1"));
            AddSymmetric(GroundMarkingKind.StencilText, new Vector2(0f, -18.2f), new Vector2(1.8f, 1.8f), 0f, Team.TeamA, "A");
            AddSymmetric(GroundMarkingKind.StencilText, new Vector2(0f, -24.6f), new Vector2(3.2f, 0.8f), 0f, Team.TeamA, "TURF");

            // 边界内侧黄色安全线。
            AddSymmetric(GroundMarkingKind.SafetyLine, new Vector2(-19.55f, 0f), new Vector2(0.14f, 52f), 0f, Team.Neutral);
            AddSymmetric(GroundMarkingKind.SafetyLine, new Vector2(0f, -27.55f), new Vector2(38f, 0.14f), 0f, Team.Neutral);
        }
    }
}
