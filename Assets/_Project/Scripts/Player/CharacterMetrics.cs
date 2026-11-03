using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 角色外形与碰撞体的唯一尺寸来源：调整体型只改这里。
    /// 装配器、视觉控制器与场景脚手架共用同一组数值，
    /// 避免"视觉变小了但命中盒还是 1.6m"这类隐形不一致。
    ///
    /// P33 骨骼重建：数值来自 INKWAVE 逆向源码（inkwave-aah-research/src/game/character-geo.js
    /// 骨位表 L32-58）按 1.12/1.39 = 0.806 缩放到本项目包络：
    /// 全身（脚底→头顶）1.12m ≈ 4 头身（头高 ~0.284m），碰撞体 / 相机 / 掩体 / 武器挂点零改动。
    /// 层级：KidRoot → Hips(0.520) → BodySquash → Spine(0.605) → Chest(0.710) → Neck(0.880) → Head(0.978 头心)。
    /// </summary>
    public static class CharacterMetrics
    {
        // ---- 包络 ----
        public const float BodyHeight = 1.12f;          // 脚底 → 头顶（= HeadCenterY + HeadHalfHeight，恰好贴 1.12 包络）
        public const float HairTopHeight = 1.30f;       // 发顶参考高度（剪影/展示图取景用，不参与碰撞）

        // ---- 核心骨骼（KidRoot 空间世界高度；feet y=0，面向 +Z）----
        public const float HipBoneY = 0.520f;           // 骨盆枢轴（腿链与上身链的根）
        public const float SpineLocalY = 0.085f;        // Spine 挂 Hips/BodySquash 下：世界 0.605
        public const float ChestLocalY = 0.105f;        // Chest 挂 Spine 下：世界 0.710
        public const float NeckLocalY = 0.170f;         // Neck 挂 Chest 下：世界 0.880
        public const float HeadLocalY = 0.098f;         // Head 挂 Neck 下：头心世界 0.978
        public const float HeadCenterY = 0.978f;

        // ---- 馒头头（mochi head：颊线最宽、小下巴、额后倾，INKWAVE character-face.js）----
        public const float HeadHalfWidth = 0.143f;
        public const float HeadHalfHeight = 0.142f;
        public const float HeadHalfDepth = 0.133f;

        // ---- 腿（两骨：大腿 0.222 + 小腿 0.211，髋→踝 = KidGaitSolver.LegLengthDefault）----
        public const float LegHipLocalY = -0.019f;      // LeftLeg/RightLeg 挂 Hips 下的局部下沉（髋关节世界 0.501）
        public const float HipHalfX = 0.063f;           // 髋关节横向半间距
        public const float ThighLength = 0.222f;        // 髋→膝
        public const float ShinLength = 0.211f;         // 膝→踝（踝世界 0.068，鞋高即触地余量）

        // ---- 臂（两骨：上臂 0.169 + 前臂 0.156）----
        // 肩枢轴落在 T 恤肩线最宽处（世界 y≈0.746、半宽 0.124 的内侧 1.2cm）：
        // 旧值 (±0.118, 0.762) 已到衣服轮廓之外的肩线顶端，手臂只剩袖口薄薄一层相连，
        // 摆动时会露出"木偶插销"式断缝。内收 + 下沉后肩球能嵌进衣服，抬臂/摆臂不再脱开。
        public const float ShoulderLocalX = 0.112f;     // 肩枢轴（挂 Chest 下，世界 ±0.112, 0.746）
        public const float ShoulderLocalY = 0.036f;
        public const float ShoulderLocalZ = 0.008f;
        public const float UpperArmLength = 0.169f;
        public const float ForearmLength = 0.156f;

        // ---- 球鞋（INKWAVE chunky court sneaker：长 0.20、底缘必须落在世界 y=0）----
        public const float ShoeLength = 0.20f;
        public const float ShoeWidth = 0.095f;
        public const float ShoeHeight = 0.062f;

        // ---- 服装覆盖带（网格在世界中的纵向范围，用于装配定位）----
        public const float TeeHemY = 0.505f;            // T 恤下摆
        public const float TeeTopY = 0.775f;            // T 恤肩线（Spine 0.605 + 0.170）
        public const float ShortsTopY = 0.635f;         // 短裤腰头上沿
        public const float ShortsHemY = 0.400f;         // 短裤裤脚（膝上灯笼裤）
        public const float TankCenterY = 0.700f;        // 墨罐罐心（背驮）
        public const float TankBackZ = 0.163f;          // 墨罐中心到身体表面的横向距离

        // ---- 领口与颈筒（P35 颈部衔接）：肩线以上收成锥形领口并封闭顶面 ----
        // 旧版顶环 0.078m 宽且两端敞口，脖子只有 0.038m 粗：俯视时领口露出 3~4cm 的空心筒，
        // 表现为"脖子下面是空的"。现在顶环收到 0.050m 且封口，前领窝从 0.038 收到 0.014。
        public const float TeeNecklineY = 0.212f;           // 领口上沿（Spine 空间）→ 世界 0.817
        public const float TeeNecklineHalfWidth = 0.050f;   // 领口环半宽：必须 > NeckRadius，封口环才能始终罩住脖子
        public const float TeeNecklineHalfDepth = 0.048f;   // 领口环半深
        public const float TeeNecklineZShift = 0.006f;      // 领口环向颈轴（z≈0.008）前移，脖子不从领圈前侧穿出
        public const float TeeCollarDip = 0.014f;           // 前领窝下沉（旧 0.038 挖到胸口，脖子露出一大截）
        public const float NeckRadius = 0.040f;             // 颈筒半径（旧 0.038 偏细，像一根棍子）
        public const float NeckLength = 0.150f;             // 颈筒总长：下端埋进胸腔、上端埋进馒头头
        public const float NeckSkinCenterY = -0.065f;       // NeckSkin 圆柱中心（Neck 骨局部 → 世界 0.815）
        public const float CollarRibHeight = 0.010f;        // 领口螺纹条上凸高度

        // ---- 躯干枢轴（历史兼容：P33 起 Chest 不再贴地，保留常量防旧引用编译失败）----
        public const float ChestPivotY = 0.05f;

        // ---- CharacterController（零玩法改动：碰撞体维持标定值）----
        public const float ColliderHeight = 1.25f;
        public const float ColliderRadius = 0.45f;
        public const float ColliderCenterY = ColliderHeight * 0.5f;
        public const float ColliderStepOffset = 0.30f;

        // ---- 头顶队伍标记 ----
        // 头顶标记已移除；保留常量仅为兼容可能仍引用它的外部/历史代码。
        public const float HeadMarkerHeight = 1.52f;

        // ---- 武器挂载 ----
        // 7 套武器位姿按旧的 1.6m 人形编写。P33 真骨骼版：手臂自然前伸握枪，
        // 手位 ≈ 世界 (±0.16, 0.50, 0.28)。换算系数按新手位重标：
        //   X 0.24 → 0.173（枪收向身体中线）、Y 0.80 → 0.496（握把高度不变，与腕同高）、
        //   Z 0.32 → 0.304（细躯干不再需要 1.55 的外推）。
        public const float WeaponMountScaleX = 0.72f;
        public const float WeaponMountScaleY = 0.62f;
        public const float WeaponMountScaleZ = 0.95f;
        public const float WeaponModelScale = 0.92f;

        /// <summary>把按 1.6m 人形写的武器挂点偏移换算到当前骨骼体型。</summary>
        public static Vector3 WeaponMountOffset(float x, float y, float z)
            => new Vector3(x * WeaponMountScaleX, y * WeaponMountScaleY, z * WeaponMountScaleZ);
    }
}
