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
    /// 无腿蛋形角色的弹跳步态求解。
    /// |sin| 给出每个周期两次触地（触地压扁、最高点拉长），sin 给出左右交替摇摆。
    /// 纯函数、无状态、零分配，可在 EditMode 直接单测；实际位移仍由物理移动组件负责。
    /// </summary>
    public static class EggGaitSolver
    {
        /// <summary>低于此速度视为静止，调用方不应推进步态相位。</summary>
        public const float MoveSpeedThreshold = 0.6f;

        /// <summary>触地频率（次/秒）：慢走约 2.0，潜墨全速约 3.4。</summary>
        public const float CadenceSlow = 2.0f;
        public const float CadenceFast = 3.4f;

        public const float BobSlow = 0.030f;
        public const float BobFast = 0.055f;
        public const float SquashDepth = 0.120f;
        public const float SquashBulge = 0.070f;
        public const float RockSlow = 2.5f;
        public const float RockFast = 6.0f;
        public const float ArmSwingSlow = 10f;
        public const float ArmSwingFast = 30f;

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

            float bounce = Mathf.Abs(Mathf.Sin(phaseRadians));
            float contact = 1f - bounce;
            // 慢速时保留一半形变量，避免小碎步看起来僵硬
            float effort = 0.5f + 0.5f * speed01;

            EggGaitPose pose;
            pose.BobY = bounce * Mathf.Lerp(BobSlow, BobFast, speed01);
            pose.RockDeg = Mathf.Sin(phaseRadians) * Mathf.Lerp(RockSlow, RockFast, speed01);
            pose.SquashY = 1f - SquashDepth * contact * effort;
            pose.SquashXZ = 1f + SquashBulge * contact * effort;
            pose.ArmSwingDeg = Mathf.Sin(phaseRadians) * Mathf.Lerp(ArmSwingSlow, ArmSwingFast, speed01);
            return pose;
        }
    }
}
