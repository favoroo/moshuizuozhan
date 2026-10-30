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
        public const float SpawnZ = 23.5f;
        public const float SpawnGateZ = 21.4f;
        public const float SpawnTerraceZ = 26.2f;
        public const float SpawnAnchorZ = 21.0f;

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
            AddSymmetric(ArenaPropKind.Kiosk, new Vector3(-7.4f, 0f, -26.0f), 0f, 1.0f, 0);

            // 边界墙顶的设备与管线（高于 1.8m，不参与地面战斗）。
            // 管线必须沿墙走向贴墙铺设：PipeRun 沿局部 +Z 生长，所以贴西/东墙要 yaw=0，
            // 贴南/北墙要 yaw=90，并按长度把起点前移半个身位，否则会横穿中场。
            AddSymmetric(ArenaPropKind.AcUnit, new Vector3(-19.85f, 1.9f, -5.0f), 90f, 1.0f, 0);
            AddSymmetric(ArenaPropKind.PipeRun, new Vector3(-19.85f, 2.05f, -6.0f), 0f, 12.0f, 0);
            AddSymmetric(ArenaPropKind.PipeRun, new Vector3(-7.0f, 2.05f, -27.9f), 90f, 14.0f, 0);
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
