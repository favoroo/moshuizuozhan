using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 小地图几何与能见度判定的纯函数集合（P4-D）：
    /// 便于在 EditMode 中进行高精度坐标映射与潜墨隐匿逻辑测试。
    /// </summary>
    public static class MinimapMath
    {
        /// <summary>
        /// 把世界空间 XZ 坐标映射到小地图容器的 AnchoredPosition（以容器中心为原点）。
        /// </summary>
        public static Vector2 WorldToMapAnchoredPosition(
            Vector3 worldPos,
            Vector2 originXZ,
            Vector2 mapSizeWorld,
            Vector2 mapContainerSize)
        {
            if (mapSizeWorld.x <= 0.001f || mapSizeWorld.y <= 0.001f)
                return Vector2.zero;

            float nx = Mathf.Clamp01((worldPos.x - originXZ.x) / mapSizeWorld.x);
            float ny = Mathf.Clamp01((worldPos.z - originXZ.y) / mapSizeWorld.y);

            return new Vector2(
                (nx - 0.5f) * mapContainerSize.x,
                (ny - 0.5f) * mapContainerSize.y);
        }

        /// <summary>
        /// 判断敌人是否应该在小地图上被标注。
        /// 规则：
        /// 1. 阵亡敌人不显示；
        /// 2. 处于潜墨隐匿（IsStealthed = true）的敌人完全不显示（契合 P2-A 潜墨规范）；
        /// 3. 正常人形态移动、开火或受伤的敌人探出标记。
        /// </summary>
        public static bool ShouldShowEnemy(CombatantHealth health, InkCharacterMotor motor)
        {
            if (health == null || !health.IsAlive) return false;
            if (motor != null && motor.IsStealthed) return false;
            return true;
        }

        /// <summary>
        /// 把世界空间的半径尺寸缩放到小地图像素半径。
        /// </summary>
        public static float WorldRadiusToMapRadius(float worldRadius, Vector2 mapSizeWorld, Vector2 mapContainerSize)
        {
            if (mapSizeWorld.x <= 0.001f) return 0f;
            float scale = mapContainerSize.x / mapSizeWorld.x;
            return worldRadius * scale;
        }
    }
}
