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
        /// <summary>双臂反相摆动幅度（度，带符号：正 = 左臂向后）。</summary>
        public float ArmSwingDeg;
        /// <summary>大腿摆动幅度（度，带符号：正 = 左腿向后）；控制器对左右腿取反相。</summary>
        public float LegSwingDeg;
        /// <summary>摆动相膝弯角度（度，正 = 屈膝）；双腿同步，峰值在双腿从躯干下方穿过的相位。</summary>
        public float KneeBendDeg;
        /// <summary>脚掌俯仰（度，正 = 脚尖下垂）；随膝弯同步，触地相归零。</summary>
        public float FootPitchDeg;

        public static readonly EggGaitPose Neutral = new EggGaitPose
        {
            BobY = 0f,
            RockDeg = 0f,
            SquashY = 1f,
            SquashXZ = 1f,
            ArmSwingDeg = 0f,
            LegSwingDeg = 0f,
            KneeBendDeg = 0f,
            FootPitchDeg = 0f
        };
    }

    /// <summary>
    /// 蛋仔风角色贴地步态求解（左右摇摆 + FK 摆腿，不做上下弹跳）。
    /// 旧版用 |sin| 驱动垂直弹跳与 12% 触地挤压，蛋仔走起来像在连续蹦；
    /// 现在只保留很小的起伏（≤ 8 mm）与摇摆，站立时完全静止。
    /// 腿部为纯 FK 相位步态（P22-A）：幅度/膝弯随速度插值（参考 INKWAVE 相位步态的
    /// duty/lift 插值机制），不做世界锁定落脚 IK——俯视角下脚部滑步可被摇摆掩盖。
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

        // ---- 双腿 FK 步态（P22-A）：慢走收着走、快跑迈开腿，幅度随速度插值 ----
        /// <summary>大腿摆幅（度）：慢走 / 快跑。摆动方向与同侧手臂反相（交叉协同）。</summary>
        public const float LegSwingSlow = 8f;
        public const float LegSwingFast = 26f;
        /// <summary>摆动相膝弯峰值（度）：双腿穿过躯干下方时屈膝抬脚，慢走 3mm、快跑 ~3cm 离地。</summary>
        public const float KneeBendSlow = 15f;
        public const float KneeBendFast = 45f;
        /// <summary>屈膝时脚尖随动下垂上限（度，极简版 toe-roll）。</summary>
        public const float FootPitchMax = 10f;

        // ---- 滞空 / 攀墙收腿（供控制器驱动，参考 INKWAVE _poseAir 的团身收腿）----
        public const float AirTuckThighDeg = 22f;
        public const float AirTuckKneeDeg = 48f;
        public const float AirTuckFootDeg = 14f;
        public const float WallClimbTuckKneeDeg = 26f;

        /// <summary>把实际移速归一化（11.8 m/s 为潜墨全速，也是本项目的最高移速）。</summary>
        public static float Speed01(float moveSpeed)
            => Mathf.Clamp01(moveSpeed / 11.8f);

        public static float CadenceHz(float speed01)
            => Mathf.Lerp(CadenceSlow, CadenceFast, Mathf.Clamp01(speed01));

        /// <summary>
        /// 采样步态。phaseRadians 由调用方按 <c>Mathf.PI * CadenceHz * dt</c> 累积
        /// （因为 |sin| 的周期是 π，这样 CadenceHz 就是每秒触地次数）。
        /// 腿部时序与躯干步态天然协调：双腿摆到最大分开（|sin|=1）时身体抬升舒展；
        /// 双腿穿过躯干下方（|sin|=0，cos 峰值）时屈膝抬脚 + 触地挤压 —— 收步与压扁同一瞬间。
        /// </summary>
        public static EggGaitPose Sample(float speed01, float phaseRadians)
        {
            speed01 = Mathf.Clamp01(speed01);

            float sway = Mathf.Sin(phaseRadians);
            float lift = Mathf.Abs(sway);
            float contact = 1f - lift;
            float kneePhase = Mathf.Max(0f, Mathf.Cos(phaseRadians));
            // 慢速时保留一半形变量，避免小碎步看起来僵硬
            float effort = 0.5f + 0.5f * speed01;

            EggGaitPose pose;
            pose.BobY = lift * Mathf.Lerp(BobSlow, BobFast, speed01);
            pose.RockDeg = sway * Mathf.Lerp(RockSlow, RockFast, speed01);
            pose.SquashY = 1f - SquashDepth * contact * effort;
            pose.SquashXZ = 1f + SquashBulge * contact * effort;
            pose.ArmSwingDeg = sway * Mathf.Lerp(ArmSwingSlow, ArmSwingFast, speed01);
            pose.LegSwingDeg = sway * Mathf.Lerp(LegSwingSlow, LegSwingFast, speed01);
            pose.KneeBendDeg = kneePhase * Mathf.Lerp(KneeBendSlow, KneeBendFast, speed01) * effort;
            pose.FootPitchDeg = kneePhase * FootPitchMax * effort;
            return pose;
        }
    }
}
