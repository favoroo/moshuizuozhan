using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「潮汐货站 / Kelp Terminal」新地图的布局数据源。
    ///
    /// 与 <see cref="ArenaLayoutV2"/> 的关系：
    /// - 场地尺寸、边界、出生甲板、台阶、出生线等**玩法常量全部复用 ArenaLayoutV2**，
    ///   保证涂地网格（40×56）、覆盖率判据、出生禁入圈、Bot NavGrid 口径完全不变；
    /// - 只替换场内掩体/高台的**摆放方式**与本图特有的地标、氛围道具。
    ///
    /// 对称性：所有新增物件都按原点 180° 点对称（与既有地图一致），
    /// 保证双方出生点到中场的路径长度、掩体数量与高度层级完全等价。
    /// </summary>
    public static class ArenaLayoutKelp
    {
        // ---- 本图特有的场内几何（尺寸/坐标集中在此，构建器不再硬编码）----

        /// <summary>中央长条货台：东西向展开，坡道只在东西两端（与旧图四向坡道形成不同争夺节奏）。</summary>
        public const float CenterRidgeSizeX = 10f;
        public const float CenterRidgeSizeZ = 6f;
        public const float CenterRidgeHeight = 1.1f;
        public const float CenterRampX = 7.4f;
        public const float CenterRampY = 0.48f;
        public const float CenterRampSizeX = 5.0f;
        public const float CenterRampSizeZ = 4.4f;
        public const float CenterRampAngle = 12.8f;

        /// <summary>中央信标柱：立在货台正中的可涂掩体，作为全场唯一记忆点。</summary>
        public const float BeaconX = 1.2f;
        public const float BeaconHeight = 3.6f;

        /// <summary>中场东西各一座低位「箱桥」，与高柜错位形成 S 形推进线。</summary>
        public const float MidLowBoxX = 8.1f;
        public const float MidLowBoxZ = 5.5f;

        /// <summary>出生出口前的低位横向挡墙（可跳 1.0m），把 7m 出口拆成左右两条推进线。</summary>
        public const float ExitBarrierZ = 4.6f;

        /// <summary>延伸区系缆桩列（0.5m 可跨），用于外侧车道的节奏点。</summary>
        public const float BollardX = 10.4f;
        public const float BollardZ = 12.0f;

        private static readonly List<ArenaLayoutV2.ArenaPropSpec> PropList = new List<ArenaLayoutV2.ArenaPropSpec>(64);

        public static IReadOnlyList<ArenaLayoutV2.ArenaPropSpec> Props => PropList;

        static ArenaLayoutKelp()
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
            // 边界外侧路灯（与既有地图同规格，8 盏，不参与碰撞）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -20f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 8f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 22f), 0f, 1.0f, 0);

            // 场外棕榈：四个角各一株，强化码头度假感并压住远景。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PalmTree, new Vector3(-23.0f, 0f, -25.5f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PalmTree, new Vector3(23.0f, 0f, -25.5f), 0f, 1.0f, 1);

            // 出生区队旗（墙外，细杆件不参与碰撞）与售卖亭（场内盒状实体）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(-4.8f, 0f, -29.6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(4.8f, 0f, -29.6f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.Kiosk, new Vector3(-8.5f, 0f, -21.0f), 0f, 1.0f, 0);

            // 场外油桶簇与系缆桩列：只做剪影，不进 NavGrid。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BarrelCluster, new Vector3(-22.0f, 0f, -8.0f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BarrelCluster, new Vector3(-22.0f, 0f, 8.0f), 0f, 1.0f, 1);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BollardRow, new Vector3(22.2f, 0f, -18.0f), 0f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BollardRow, new Vector3(22.2f, 0f, 18.0f), 0f, 1.0f, 0);

            // 出生台外侧锥桶与长椅（装饰，不挡子弹路径）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(-9.6f, 0f, -23.2f), 0f, 0.9f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.Bench, new Vector3(9.9f, 0f, -22.4f), 0f, 1.0f, 0);

            // 边界墙顶设备与管线（与既有地图同位置，高于战斗面）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.AcUnit, new Vector3(-19.85f, 1.9f, -5.0f), 90f, 1.0f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PipeRun, new Vector3(-19.85f, 2.05f, -6.0f), 0f, 12.0f, 0);
        }
    }
}
