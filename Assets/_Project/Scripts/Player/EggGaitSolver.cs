using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 蛋形角色单帧步态姿态（纯数据，无 Transform 依赖）。
    /// </summary>
    public struct EggGaitPose
    {
        /// <summary>身体抬升（米，向上为正）。</summary>
        public float BobY;
        /// <summary>绕 Z 轴的左右摇摆（度）。</summary>
        public float RockDeg;
        /// <summary>竖向缩放系数（触地压扁 &lt; 1）。</summary>
        public float SquashY;
        /// <summary>横向缩放系数（触地时膨胀 &gt; 1）。</summary>
        public float SquashXZ;
        /// <summary>双臂反相摆动幅度（度）。</summary>
        public float ArmSwingDeg;

        public static readonly EggGaitPose Neutral = new EggGaitPose
        {
            BobY = 0f,
            RockDeg = 0f,
            SquashY = 1f,
            SquashXZ = 1f,
            ArmSwingDeg = 0f
        };
    }

    /// <summary>
    /// 无腿蛋形角色的贴地步态求解（左右摇摆，不做上下弹跳）。
    /// 旧版用 |sin| 驱动垂直弹跳与 12% 触地挤压，蛋仔走起来像在连续蹦；
    /// 现在只保留很小的起伏（≤ 8 mm）与摇摆，让脚底始终贴地，站立时完全静止。
    /// 纯函数、无状态、零分配，可在 EditMode 直接单测；实际位移仍由物理移动组件负责。
    /// </summary>
    public static class EggGaitSolver
    {
        /// <summary>低于此速度视为静止，调用方不应推进步态相位。</summary>
        public const float MoveSpeedThreshold = 1.0f;

        /// <summary>触地频率（次/秒）：慢走约 1.35，潜墨全速约 2.2（旧版 2.0~3.4 太急促）。</summary>
        public const float CadenceSlow = 1.35f;
        public const float CadenceFast = 2.2f;

        // 起伏必须远小于体型（1.12m），否则会读成"跳跃"而不是"走路"
        public const float BobSlow = 0.004f;
        public const float BobFast = 0.008f;
        public const float SquashDepth = 0.060f;
        public const float SquashBulge = 0.040f;
        public const float RockSlow = 1.6f;
        public const float RockFast = 3.2f;
        public const float ArmSwingSlow = 6f;
        public const float ArmSwingFast = 14f;

        /// <summary>把实际移速归一化（11.8 m/s 为潜墨全速，也是本项目的最高移速）。</summary>
        public static float Speed01(float moveSpeed)
            => Mathf.Clamp01(moveSpeed / 11.8f);

        public static float CadenceHz(float speed01)
            => Mathf.Lerp(CadenceSlow, CadenceFast, Mathf.Clamp01(speed01));

        /// <summary>
        /// 采样步态。phaseRadians 由调用方按 <c>Mathf.PI * CadenceHz * dt</c> 累积
        /// （因为 |sin| 的周期是 π，这样 CadenceHz 就是每秒触地次数）。
        /// </summary>
        public static EggGaitPose Sample(float speed01, float phaseRadians)
        {
            speed01 = Mathf.Clamp01(speed01);

            float sway = Mathf.Sin(phaseRadians);
            float lift = Mathf.Abs(sway);
            float contact = 1f - lift;
            // 慢速时保留一半形变量，避免小碎步看起来僵硬
            float effort = 0.5f + 0.5f * speed01;

            EggGaitPose pose;
            pose.BobY = lift * Mathf.Lerp(BobSlow, BobFast, speed01);
            pose.RockDeg = sway * Mathf.Lerp(RockSlow, RockFast, speed01);
            pose.SquashY = 1f - SquashDepth * contact * effort;
            pose.SquashXZ = 1f + SquashBulge * contact * effort;
            pose.ArmSwingDeg = sway * Mathf.Lerp(ArmSwingSlow, ArmSwingFast, speed01);
            return pose;
        }
    }
}
