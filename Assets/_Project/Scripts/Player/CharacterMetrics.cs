using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 角色外形与碰撞体的唯一尺寸来源：调整体型只改这里。
    /// 蛋形装配器、视觉控制器与场景脚手架共用同一组数值，
    /// 避免"视觉变小了但命中盒还是 1.6m"这类隐形不一致。
    /// </summary>
    public static class CharacterMetrics
    {
        // ---- 蛋形身体 ----
        public const float BodyHeight = 1.12f;
        public const float BodyMaxRadius = 0.43f;

        // ---- 躯干枢轴 ----
        // 枢轴贴地：前倾与左右摇摆会像"蛋在原地摇"，而不是上半身单独折叠。
        public const float ChestPivotY = 0.05f;

        // ---- CharacterController ----
        public const float ColliderHeight = 1.25f;
        public const float ColliderRadius = 0.45f;
        public const float ColliderCenterY = ColliderHeight * 0.5f;
        public const float ColliderStepOffset = 0.30f;

        // ---- 头顶队伍标记 ----
        // 头顶标记已移除；保留常量仅为兼容可能仍引用它的外部/历史代码。
        public const float HeadMarkerHeight = 1.52f;

        // ---- 武器挂载 ----
        // 7 套武器位姿按旧的 1.6m 人形编写：蛋体的前后厚度（半径 0.40m）远大于旧人形胸腔，
        // 因此 Z 需要外推（否则枪身会埋进肚子里），Y 需要下压（否则枪会挡脸）。
        public const float WeaponMountScaleX = 0.95f;
        // 0.72 -> 0.66：旧挂点让枪口停在脸侧；压低 8cm 后握把与圆手同高，武器落在胸前
        public const float WeaponMountScaleY = 0.66f;
        public const float WeaponMountScaleZ = 1.55f;
        public const float WeaponModelScale = 0.92f;

        /// <summary>把按人形写的武器挂点偏移换算到蛋形身体。</summary>
        public static Vector3 WeaponMountOffset(float x, float y, float z)
            => new Vector3(x * WeaponMountScaleX, y * WeaponMountScaleY, z * WeaponMountScaleZ);
    }
}
