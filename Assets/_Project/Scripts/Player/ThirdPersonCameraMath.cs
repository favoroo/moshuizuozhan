using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 第三人称相机与相机相对操作的纯数学工具（不依赖 MonoBehaviour 生命周期，EditMode 友好）：
    /// 1. 俯仰角安全夹紧（支持 NaN 兜底与 min/max 颠倒）；
    /// 2. 人形 / 潜墨枢轴高度选择；
    /// 3. WASD / 左摇杆 → 相机相对世界移动向量；
    /// 4. 屏幕中心瞄准射线起点：沿相机中心线平移到玩家胸口深度，避开相机与玩家之间的几何。
    /// </summary>
    public static class ThirdPersonCameraMath
    {
        /// <summary>夹紧俯仰角；NaN 时回落到区间中点，min/max 颠倒时自动交换。</summary>
        public static float ClampPitch(float pitch, float minPitch, float maxPitch)
        {
            if (minPitch > maxPitch)
            {
                float swap = minPitch;
                minPitch = maxPitch;
                maxPitch = swap;
            }

            if (float.IsNaN(pitch))
                return Mathf.Clamp(0f, minPitch, maxPitch);

            return Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        /// <summary>按形态选择相机枢轴高度（人形略高于头顶，潜墨压扁贴地）。</summary>
        public static float ComputePivotHeight(bool swimming, float standingHeight, float swimmingHeight)
        {
            return swimming ? swimmingHeight : standingHeight;
        }

        /// <summary>
        /// 把 2D 输入（x=右, y=前）转换为相机水平面向量。
        /// 输入模长保留（摇杆轻推慢走）；超过 1 时整体归一化，避免斜向加速。
        /// </summary>
        public static Vector3 ComputeMoveWorld(Vector2 moveInput, Vector3 planarForward, Vector3 planarRight)
        {
            Vector3 forward = new Vector3(planarForward.x, 0f, planarForward.z);
            Vector3 right = new Vector3(planarRight.x, 0f, planarRight.z);

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();

            Vector3 move = forward * moveInput.y + right * moveInput.x;
            return Vector3.ClampMagnitude(move, 1f);
        }

        /// <summary>
        /// 计算屏幕中心瞄准射线的起点：
        /// 眼睛位置（玩家胸口）投影到相机中心线上，射线方向仍严格等于相机 forward。
        /// 这样准星就是相机中心点，同时忽略相机与玩家之间的墙体/角色自身。
        /// </summary>
        public static Vector3 ComputeAimRayOrigin(
            Vector3 cameraPosition,
            Vector3 cameraForward,
            Vector3 playerChestPosition)
        {
            float along = Mathf.Max(0f, Vector3.Dot(playerChestPosition - cameraPosition, cameraForward));
            return cameraPosition + cameraForward * along;
        }
    }
}
