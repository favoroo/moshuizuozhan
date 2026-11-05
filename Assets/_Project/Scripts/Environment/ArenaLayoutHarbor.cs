using System.Collections.Generic;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 「海鸥港湾 / GULL HARBOR」的布局数据源（复刻 INKWAVE `halyard` 渡轮码头）。
    ///
    /// 与 <see cref="ArenaLayoutV2"/> 的关系：
    /// - 场地尺寸、边界、出生甲板/台阶/出生线、涂地网格等**玩法常量继续复用 ArenaLayoutV2**，
    ///   保证 40×56 计分网格、出生禁入圈、Bot NavGrid 口径与主图一致；
    /// - 场内节奏完全重写：中央是一艘横跨中场的渡轮（甲板 = 唯一需要坡道才能上的高台），
    ///   船体南北两侧是**不可涂的浅湾水线**，东西两侧留 4.8m 木栈道绕后。
    ///
    /// 对称性：所有场内物件按原点 180° 点对称（船体/水线自身对称），双方路线长度与掩体数量等价。
    /// </summary>
    public static class ArenaLayoutHarbor
    {
        // ---- 渡轮（中央地标）----
        /// <summary>船体半长（X 向，船首/船尾）。</summary>
        public const float FerryHalfX = 11.8f;
        /// <summary>船体半宽（Z 向，左右舷）。</summary>
        public const float FerryHalfZ = 4.6f;
        /// <summary>
        /// 船壳顶面高度：甲板板占 1.44~1.5，甲板可站立面 = 1.5m。
        /// 露出的 0.1m 舷缘抬高到 1.44（&gt; 跳跃 1.35m），玩家不能跳上舷缘绕过登船坡道。
        /// </summary>
        public const float FerryHullTopY = 1.44f;
        public const float FerryDeckTopY = 1.5f;
        public const float FerryDeckThickness = FerryDeckTopY - FerryHullTopY;
        /// <summary>甲板板比船壳内收 0.1m，露出一圈深色舷缘。</summary>
        public const float FerryDeckInset = 0.1f;

        /// <summary>舷墙：甲板上的 0.72m 可跳矮墙（掩体），在 x∈[-2.0,2.0] 留出登船口。</summary>
        public const float BulwarkHeight = 0.72f;
        public const float BulwarkThickness = 0.34f;
        public const float BulwarkGapHalfX = 2.0f;
        public const float BulwarkCenterZ = FerryHalfZ - 0.2f;

        /// <summary>客舱（甲板中央的实体掩体）与驾驶室。</summary>
        public const float CabinHalfX = 5.2f;
        public const float CabinHalfZ = 2.3f;
        public const float CabinTopY = 3.6f;
        public const float WheelhouseTopY = 4.7f;

        // ---- 浅湾水线（不可涂、可行走涉水）----
        /// <summary>水线沿 X 的范围（外侧 4.8m 木栈道从这里开始）。</summary>
        public const float WaterHalfX = 15.2f;
        /// <summary>水线内缘贴住船壳（z=±4.6），外缘到 z=±9.8。</summary>
        public const float WaterInnerZ = FerryHalfZ;
        public const float WaterOuterZ = 9.8f;
        /// <summary>水面高度：略高于地面，读作 3cm 浅水（角色可涉水通过，但涂不上墨）。</summary>
        public const float WaterSurfaceY = 0.035f;

        // ---- 登船坡道（跨过水线，从码头接上甲板）----
        public const float RampHalfWidth = 1.9f;
        /// <summary>坡道沿 Z 的投影长度：从水线外缘 9.8 到船壳 4.6。</summary>
        public const float RampRunZ = WaterOuterZ - WaterInnerZ;
        /// <summary>坡道倾角（度）：atan(1.5 / 5.2) ≈ 16.1°。</summary>
        public const float RampAngle = 16.1f;
        public const float RampCenterZ = (WaterOuterZ + WaterInnerZ) * 0.5f;
        /// <summary>坡道板厚 0.25m，中心下沉量（沿法线）= 0.125 * cos(16.1°)。</summary>
        public const float RampCenterY = 0.75f - 0.12f;

        // ---- 场内节奏（Team A 半场坐标，另一侧自动 180° 镜像）----
        /// <summary>加油亭：正对登船坡道的中路掩体，把 7m 出口拆成左右两条推进线。</summary>
        public static readonly Vector3 FuelHutPos = new Vector3(0f, 1.3f, -15.4f);
        public static readonly Vector3 FuelHutSize = new Vector3(2.8f, 2.6f, 2.8f);

        /// <summary>出生闸口两侧的储物柜掩体（沿用旧图坐标与尺寸，保证出生出口节奏一致）。</summary>
        public static readonly Vector3 SpawnGateWingPos = new Vector3(5.0f, 0.75f, -ArenaLayoutV2.SpawnGateZ);
        public static readonly Vector3 SpawnGateWingSize = new Vector3(3.0f, 1.5f, 1.2f);

        /// <summary>船坞（每队左翼）：搁在墩木上的拖船，甲板 1.15m 可直接跳上，是侧翼高台。</summary>
        public static readonly Vector3 TugPos = new Vector3(-15.6f, 0f, -14.2f);
        public static readonly Vector3 TugSize = new Vector3(5.4f, 1.15f, 6.6f);
        public static readonly Vector3 TugHouseSize = new Vector3(2.6f, 1.6f, 2.4f);

        /// <summary>系泊在码头边的游艇（低矮掩体，船头探向水面）。</summary>
        public static readonly Vector3 HouseboatPos = new Vector3(-8.6f, 0f, -12.1f);
        public const float HouseboatDeckY = 0.55f;
        public static readonly Vector3 HouseboatSize = new Vector3(2.4f, HouseboatDeckY, 4.4f);
        public static readonly Vector3 HouseboatCabinSize = new Vector3(1.9f, 2.1f, 2.2f);

        /// <summary>水面靠墙一侧的低矮栏杆（只做轮廓，不挡子弹；不参与碰撞）。</summary>
        public const float WaterCurbHeight = 0.18f;

        /// <summary>
        /// 涂地网格的水面禁区：xmin/zmin/xmax/zmax（世界坐标）。
        /// 登船坡道 x∈[-1.9,1.9] 特意**不**列入禁区，坡道顶面才能正常受墨。
        /// </summary>
        public static readonly Vector4[] WaterBlockedRects =
        {
            new Vector4(-WaterHalfX, -WaterOuterZ, -RampHalfWidth, -WaterInnerZ),
            new Vector4(RampHalfWidth, -WaterOuterZ, WaterHalfX, -WaterInnerZ),
            new Vector4(-WaterHalfX, WaterInnerZ, -RampHalfWidth, WaterOuterZ),
            new Vector4(RampHalfWidth, WaterInnerZ, WaterHalfX, WaterOuterZ),
        };

        // ---- 场内装饰道具（实体件只放盒状物件；细杆件一律不参与碰撞）----
        private static readonly List<ArenaLayoutV2.ArenaPropSpec> PropList = new List<ArenaLayoutV2.ArenaPropSpec>(64);

        public static IReadOnlyList<ArenaLayoutV2.ArenaPropSpec> Props => PropList;

        static ArenaLayoutHarbor()
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
            // 边界外侧路灯与棕榈：只做远景剪影，不参与碰撞。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -20f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, -6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 8f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.LampPost, new Vector3(-21.5f, 0f, 22f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PalmTree, new Vector3(-23.0f, 0f, -25.5f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PalmTree, new Vector3(23.0f, 0f, -25.5f), 0f, 1f, 1);

            // 出生区队旗（墙外）：四面旗给两个入口方向各一组记忆点。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(-8.2f, 0f, -29.6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.FlagPole, new Vector3(8.2f, 0f, -29.6f), 0f, 1f, 0);

            // 售票房（实体，出生出口前的第一段掩体）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.Kiosk, new Vector3(-11.8f, 0f, -20.4f), 0f, 1f, 0);

            // 木栈道外侧：桶堆 / 箱堆贴墙摆放，给 4.8m 车道留出 2.9m 净宽。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BarrelCluster, new Vector3(-18.8f, 0f, -11.6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateCluster, new Vector3(-18.6f, 0f, -4.0f), 12f, 1f, 1);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateCluster, new Vector3(-18.9f, 0f, 3.6f), -10f, 0.9f, 2);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.Bench, new Vector3(-19.0f, 0f, -1.2f), 90f, 1f, 0);

            // 出生通道两侧：花坛与高箱，形成进入中场前的最后一段掩体。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.PlanterBox, new Vector3(-6.4f, 0f, -18.6f), 0f, 1f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.CrateTall, new Vector3(-9.5f, 0f, -17.5f), 8f, 1f, 0);

            // 系缆桩列：沿水线外缘，纯装饰（0.6m 细杆件不参与碰撞）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.BollardRow, new Vector3(-12.5f, 0f, -9.2f), 90f, 1f, 0);

            // 登船口锥桶（装饰）。
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(-7.2f, 0f, -10.4f), 0f, 0.9f, 0);
            AddSymmetric(ArenaLayoutV2.ArenaPropKind.ConeCluster, new Vector3(7.2f, 0f, -10.4f), 0f, 0.9f, 1);
        }
    }
}