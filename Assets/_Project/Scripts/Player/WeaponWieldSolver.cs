using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 一把武器的单帧持握/动作姿态（纯数据，无 Transform 依赖）。
    /// 角度约定与 Unity 一致：PitchDeg 正 = 枪口/桶口压向地面，负 = 抬起；
    /// DropMeters 正 = 挂点下压，ForwardMeters 正 = 向前推、负 = 向后收。
    /// 滚筒不参与本层（由 <see cref="RollerWieldSolver"/> 全权接管）。
    /// </summary>
    public struct WeaponWieldPose
    {
        /// <summary>武器模型俯仰附加角（度）。</summary>
        public float PitchDeg;
        /// <summary>武器模型偏航附加角（度，正 = 枪口朝右）。</summary>
        public float YawDeg;
        /// <summary>武器模型滚转附加角（度）。</summary>
        public float RollDeg;
        /// <summary>武器挂点下压量（米，≥ 0）。</summary>
        public float DropMeters;
        /// <summary>武器挂点前后位移（米，正 = 前推，负 = 后收）。</summary>
        public float ForwardMeters;
        /// <summary>胸口附加俯仰（度，正 = 前压）。</summary>
        public float BodyPitchDeg;
        /// <summary>胸口附加滚转（度，正 = 右倾）。</summary>
        public float BodyRollDeg;
        /// <summary>身体重心下沉比例（0~1，竖向缩放乘 (1 - Sink01)）。</summary>
        public float Sink01;

        public static WeaponWieldPose Neutral => new WeaponWieldPose();

        /// <summary>逐字段插值（t 钳制 0~1）。</summary>
        public static WeaponWieldPose Blend(in WeaponWieldPose a, in WeaponWieldPose b, float t)
        {
            t = Mathf.Clamp01(t);
            WeaponWieldPose pose;
            pose.PitchDeg = Mathf.Lerp(a.PitchDeg, b.PitchDeg, t);
            pose.YawDeg = Mathf.Lerp(a.YawDeg, b.YawDeg, t);
            pose.RollDeg = Mathf.Lerp(a.RollDeg, b.RollDeg, t);
            pose.DropMeters = Mathf.Lerp(a.DropMeters, b.DropMeters, t);
            pose.ForwardMeters = Mathf.Lerp(a.ForwardMeters, b.ForwardMeters, t);
            pose.BodyPitchDeg = Mathf.Lerp(a.BodyPitchDeg, b.BodyPitchDeg, t);
            pose.BodyRollDeg = Mathf.Lerp(a.BodyRollDeg, b.BodyRollDeg, t);
            pose.Sink01 = Mathf.Lerp(a.Sink01, b.Sink01, t);
            return pose;
        }
    }

    /// <summary>
    /// 单帧动作输入：由 <c>CharacterVisualController</c> 从 WeaponController 状态与开火事件汇总。
    /// 所有 0~1 权重都已平滑，纯函数不再做时间积分。
    /// </summary>
    public struct WeaponWieldInput
    {
        public float Time;
        /// <summary>水平移动速度归一化 0~1（用于待机微摆幅度）。</summary>
        public float Speed01;
        public bool Charging;
        public float Charge01;
        public bool Streaming;
        /// <summary>扫射权重 0~1（进入/退出平滑）。</summary>
        public float Streaming01;
        public bool Rolling;
        /// <summary>双枪翻滚权重 0~1（进入/退出平滑）。</summary>
        public float Dodge01;
        /// <summary>翻滚方向相对角色右方：±1 侧滚，0 前后滚。</summary>
        public float DodgeSign;
        /// <summary>翻滚方向相对角色前方：+1 前滚，-1 后滚。</summary>
        public float DodgeForwardSign;
        /// <summary>双枪架枪权重 0~1。</summary>
        public float Turret01;
        /// <summary>泼桶前摇进度 0~1（0 = 刚按下，1 = 即将甩出）。</summary>
        public float Windup01;
        /// <summary>泼桶甩出动作剩余强度 0~1（甩出瞬间 1，随后回落到 0）。</summary>
        public float Throw01;
        /// <summary>单发后坐强度 0~1（小枪/双枪/爆破）。</summary>
        public float FireKick01;
        /// <summary>双枪本次开火手别（+1 右手 / -1 左手）。</summary>
        public float FireKickSign;
        /// <summary>蓄力狙释放动作剩余强度 0~1（按蓄力刻度缩放）。</summary>
        public float Release01;
    }

    /// <summary>
    /// 武器持握/动作参数包（由 CharacterAnimConfigSO 填充；纯函数不直接依赖 SO，便于单测）。
    /// 默认值 = <see cref="WeaponWieldSolver"/> 常量，测试锁定两者相等。
    /// </summary>
    public struct WeaponWieldParams
    {
        public float SmoothRate;
        public float IdleSwayDeg;
        public float IdleSwayHz;
        public float ChargePullbackMeters;
        public float ChargeDropMeters;
        public float ChargeBodyPitchDeg;
        public float ChargeTremorDeg;
        public float SlosherCoilPitchDeg;
        public float SlosherThrowPitchDeg;
        public float SlosherThrowBodyPitchDeg;
        public float BlasterKickPitchDeg;
        public float BlasterKickBackMeters;
        public float DualiesDodgeBodyRollDeg;
        public float DualiesDodgeSink;
        public float DualiesTurretSink;
        public float DualiesTurretBodyPitchDeg;
        public float SplatlingChargeDropMeters;
        public float SplatlingChargeBodyPitchDeg;
        public float SplatlingSpinMinDegPerSec;
        public float SplatlingSpinMaxDegPerSec;
        public float SplatlingSpinCoastRate;
        public float SplatlingStreamShakeDeg;
        public float ShooterFireBodyPitchDeg;
        public float ShooterFirePullbackMeters;

        public static WeaponWieldParams Default => new WeaponWieldParams
        {
            SmoothRate = WeaponWieldSolver.DefaultSmoothRate,
            IdleSwayDeg = WeaponWieldSolver.DefaultIdleSwayDeg,
            IdleSwayHz = WeaponWieldSolver.DefaultIdleSwayHz,
            ChargePullbackMeters = WeaponWieldSolver.DefaultChargePullbackMeters,
            ChargeDropMeters = WeaponWieldSolver.DefaultChargeDropMeters,
            ChargeBodyPitchDeg = WeaponWieldSolver.DefaultChargeBodyPitchDeg,
            ChargeTremorDeg = WeaponWieldSolver.DefaultChargeTremorDeg,
            SlosherCoilPitchDeg = WeaponWieldSolver.DefaultSlosherCoilPitchDeg,
            SlosherThrowPitchDeg = WeaponWieldSolver.DefaultSlosherThrowPitchDeg,
            SlosherThrowBodyPitchDeg = WeaponWieldSolver.DefaultSlosherThrowBodyPitchDeg,
            BlasterKickPitchDeg = WeaponWieldSolver.DefaultBlasterKickPitchDeg,
            BlasterKickBackMeters = WeaponWieldSolver.DefaultBlasterKickBackMeters,
            DualiesDodgeBodyRollDeg = WeaponWieldSolver.DefaultDualiesDodgeBodyRollDeg,
            DualiesDodgeSink = WeaponWieldSolver.DefaultDualiesDodgeSink,
            DualiesTurretSink = WeaponWieldSolver.DefaultDualiesTurretSink,
            DualiesTurretBodyPitchDeg = WeaponWieldSolver.DefaultDualiesTurretBodyPitchDeg,
            SplatlingChargeDropMeters = WeaponWieldSolver.DefaultSplatlingChargeDropMeters,
            SplatlingChargeBodyPitchDeg = WeaponWieldSolver.DefaultSplatlingChargeBodyPitchDeg,
            SplatlingSpinMinDegPerSec = WeaponWieldSolver.DefaultSplatlingSpinMinDegPerSec,
            SplatlingSpinMaxDegPerSec = WeaponWieldSolver.DefaultSplatlingSpinMaxDegPerSec,
            SplatlingSpinCoastRate = WeaponWieldSolver.DefaultSplatlingSpinCoastRate,
            SplatlingStreamShakeDeg = WeaponWieldSolver.DefaultSplatlingStreamShakeDeg,
            ShooterFireBodyPitchDeg = WeaponWieldSolver.DefaultShooterFireBodyPitchDeg,
            ShooterFirePullbackMeters = WeaponWieldSolver.DefaultShooterFirePullbackMeters
        };
    }

    /// <summary>
    /// 通用武器持握/攻击姿态求解（P35，参考 INKWAVE character.js 的 per-weapon pose 与
    /// weapons.js 的蓄力/翻滚/泼洒节奏）：
    /// 1. 小枪：枪口微抬、持续射击身体前压 + 枪身回拉 + 微小左右摆，读作“压住枪”；
    /// 2. 蓄力狙：蓄力后拉/下压/后仰 + 高频张力抖动，释放瞬间前推；
    /// 3. 双枪：左右手交替的后坐偏航；翻滚侧滚/下沉，架枪前压/下沉；
    /// 4. 泼桶：前摇桶身后仰，甩出桶身前倾 + 胸口前压；
    /// 5. 爆破：重炮式后拉 + 枪口上抬 + 胸口后仰；
    /// 6. 加特林：蓄力压低/前压，扫射机身抖动；枪管转速随蓄力连续变化；
    /// 7. 滚筒：返回 Neutral，完全交给 <see cref="RollerWieldSolver"/>。
    /// 纯函数、无状态、零分配，可在 EditMode 直接单测；Transform 写回由控制器负责。
    /// </summary>
    public static class WeaponWieldSolver
    {
        // ---- 时间线（秒）----
        /// <summary>小枪单发后坐视觉衰减时长。</summary>
        public const float ShooterKickDuration = 0.14f;
        /// <summary>双枪单发交替后坐时长。</summary>
        public const float DualiesKickDuration = 0.10f;
        /// <summary>蓄力狙释放前推动作时长。</summary>
        public const float ChargerReleaseDuration = 0.12f;
        /// <summary>爆破枪后坐回稳时长。</summary>
        public const float BlasterKickDuration = 0.22f;

        // ---- 全局默认值（与 CharacterAnimConfigSO 字段默认值一致）----
        public const float DefaultSmoothRate = 18f;
        public const float DefaultIdleSwayDeg = 0.6f;
        public const float DefaultIdleSwayHz = 0.4f;
        public const float DefaultChargePullbackMeters = 0.05f;
        public const float DefaultChargeDropMeters = 0.015f;
        public const float DefaultChargeBodyPitchDeg = 4f;
        public const float DefaultChargeTremorDeg = 0.18f;
        public const float DefaultSlosherCoilPitchDeg = 14f;
        public const float DefaultSlosherThrowPitchDeg = 32f;
        public const float DefaultSlosherThrowBodyPitchDeg = 14f;
        public const float DefaultBlasterKickPitchDeg = 10f;
        public const float DefaultBlasterKickBackMeters = 0.07f;
        public const float DefaultDualiesDodgeBodyRollDeg = 16f;
        public const float DefaultDualiesDodgeSink = 0.05f;
        public const float DefaultDualiesTurretSink = 0.06f;
        public const float DefaultDualiesTurretBodyPitchDeg = 5f;
        public const float DefaultSplatlingChargeDropMeters = 0.025f;
        public const float DefaultSplatlingChargeBodyPitchDeg = 3f;
        public const float DefaultSplatlingSpinMinDegPerSec = 280f;
        public const float DefaultSplatlingSpinMaxDegPerSec = 1400f;
        public const float DefaultSplatlingSpinCoastRate = 5f;
        public const float DefaultSplatlingStreamShakeDeg = 0.5f;
        public const float DefaultShooterFireBodyPitchDeg = 2f;
        public const float DefaultShooterFirePullbackMeters = 0.01f;

        // ---- 持握基准角（度；按武器模型握把为枢轴，值小以避免与准星错位）----
        public const float ShooterCarryPitchDeg = -2f;
        public const float ChargerCarryPitchDeg = -1f;
        public const float ChargerCarryYawDeg = 2f;
        public const float SlosherCarryPitchDeg = -4f;
        public const float SlosherCarryYawDeg = -8f;
        public const float SlosherCarryRollDeg = 8f;
        public const float BlasterCarryRollDeg = 3f;
        public const float SplatlingCarryPitchDeg = 3f;
        public const float SplatlingCarryYawDeg = 4f;
        public const float SplatlingCarryRollDeg = -3f;

        /// <summary>按武器返回单帧姿态；滚筒返回 Neutral（由滚筒专属求解接管）。</summary>
        public static WeaponWieldPose Sample(WeaponKind kind, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            if (kind == WeaponKind.Roller)
                return WeaponWieldPose.Neutral;

            WeaponWieldPose pose = SampleCarry(kind, input, p);

            switch (kind)
            {
                case WeaponKind.Shooter:
                    SampleShooter(ref pose, input, p);
                    break;
                case WeaponKind.Charger:
                    SampleCharger(ref pose, input, p);
                    break;
                case WeaponKind.Dualies:
                    SampleDualies(ref pose, input, p);
                    break;
                case WeaponKind.Slosher:
                    SampleSlosher(ref pose, input, p);
                    break;
                case WeaponKind.Blaster:
                    SampleBlaster(ref pose, input, p);
                    break;
                case WeaponKind.Splatling:
                    SampleSplatling(ref pose, input, p);
                    break;
            }

            Clamp(ref pose);
            return pose;
        }

        /// <summary>持握基准 + 待机微摆（移动时淡出，站定时才明显）。</summary>
        public static WeaponWieldPose SampleCarry(WeaponKind kind, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float pitch;
            float yaw;
            float roll;

            switch (kind)
            {
                case WeaponKind.Shooter:
                    pitch = ShooterCarryPitchDeg;
                    yaw = 0f;
                    roll = 0f;
                    break;
                case WeaponKind.Charger:
                    pitch = ChargerCarryPitchDeg;
                    yaw = ChargerCarryYawDeg;
                    roll = 0f;
                    break;
                case WeaponKind.Slosher:
                    pitch = SlosherCarryPitchDeg;
                    yaw = SlosherCarryYawDeg;
                    roll = SlosherCarryRollDeg;
                    break;
                case WeaponKind.Blaster:
                    pitch = 0f;
                    yaw = 0f;
                    roll = BlasterCarryRollDeg;
                    break;
                case WeaponKind.Splatling:
                    pitch = SplatlingCarryPitchDeg;
                    yaw = SplatlingCarryYawDeg;
                    roll = SplatlingCarryRollDeg;
                    break;
                default:
                    pitch = 0f;
                    yaw = 0f;
                    roll = 0f;
                    break;
            }

            float idle01 = 1f - Mathf.Clamp01(input.Speed01 * 2f);
            float phase = input.Time * Mathf.PI * 2f * Mathf.Max(0.01f, p.IdleSwayHz);
            float sway = Mathf.Sin(phase) * Mathf.Max(0f, p.IdleSwayDeg) * idle01;
            yaw += sway;
            roll += Mathf.Cos(phase * 0.87f) * Mathf.Max(0f, p.IdleSwayDeg) * 0.5f * idle01;

            WeaponWieldPose pose = WeaponWieldPose.Neutral;
            pose.PitchDeg = pitch;
            pose.YawDeg = yaw;
            pose.RollDeg = roll;
            return pose;
        }

        private static void SampleShooter(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float kick = Mathf.Clamp01(input.FireKick01);
            pose.PitchDeg -= 0.8f * kick;
            pose.ForwardMeters -= Mathf.Max(0f, p.ShooterFirePullbackMeters) * kick;
            pose.BodyPitchDeg += Mathf.Max(0f, p.ShooterFireBodyPitchDeg) * kick;
            pose.YawDeg += Mathf.Sin(input.Time * 37f) * 0.35f * kick;
        }

        private static void SampleCharger(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float charge = Mathf.Clamp01(input.Charge01);
            pose.ForwardMeters -= Mathf.Max(0f, p.ChargePullbackMeters) * charge;
            pose.DropMeters += Mathf.Max(0f, p.ChargeDropMeters) * charge;
            // 蓄力时枪口微抬（负俯仰），读作“压住长枪管”
            pose.PitchDeg -= 3f * charge;
            pose.BodyPitchDeg -= Mathf.Max(0f, p.ChargeBodyPitchDeg) * charge;
            pose.YawDeg += Mathf.Sin(input.Time * 55f) * Mathf.Max(0f, p.ChargeTremorDeg) * charge;
            pose.RollDeg += Mathf.Cos(input.Time * 47f) * Mathf.Max(0f, p.ChargeTremorDeg) * charge;

            float release = Mathf.Clamp01(input.Release01);
            pose.ForwardMeters += 0.04f * release;
            pose.PitchDeg += 5f * release;
            pose.BodyPitchDeg += 2f * release;
        }

        private static void SampleDualies(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float kick = Mathf.Clamp01(input.FireKick01);
            float sign = input.FireKickSign >= 0f ? 1f : -1f;
            pose.YawDeg += sign * 1.2f * kick;
            pose.RollDeg -= sign * 0.8f * kick;
            pose.BodyRollDeg -= sign * 0.5f * kick;

            float dodge = Mathf.Clamp01(input.Dodge01);
            float side = Mathf.Clamp(input.DodgeSign, -1f, 1f);
            pose.RollDeg += side * 10f * dodge;
            pose.DropMeters += 0.03f * dodge;
            pose.ForwardMeters -= 0.02f * dodge;
            pose.BodyRollDeg += side * Mathf.Max(0f, p.DualiesDodgeBodyRollDeg) * dodge;
            pose.BodyPitchDeg += Mathf.Clamp(input.DodgeForwardSign, -1f, 1f) * 4f * dodge;
            pose.Sink01 += Mathf.Max(0f, p.DualiesDodgeSink) * dodge;

            float turret = Mathf.Clamp01(input.Turret01);
            pose.DropMeters += 0.015f * turret;
            pose.ForwardMeters -= 0.02f * turret;
            pose.PitchDeg += 1.5f * turret;
            pose.BodyPitchDeg += Mathf.Max(0f, p.DualiesTurretBodyPitchDeg) * turret;
            pose.Sink01 += Mathf.Max(0f, p.DualiesTurretSink) * turret;
        }

        private static void SampleSlosher(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float windup = Mathf.Clamp01(input.Windup01);
            pose.PitchDeg -= Mathf.Max(0f, p.SlosherCoilPitchDeg) * windup;
            pose.BodyPitchDeg -= 8f * windup;
            pose.ForwardMeters -= 0.05f * windup;
            pose.DropMeters += 0.01f * windup;

            float throw01 = Mathf.Clamp01(input.Throw01);
            pose.PitchDeg += Mathf.Max(0f, p.SlosherThrowPitchDeg) * throw01;
            pose.BodyPitchDeg += Mathf.Max(0f, p.SlosherThrowBodyPitchDeg) * throw01;
            pose.ForwardMeters += 0.07f * throw01;
        }

        private static void SampleBlaster(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float kick = Mathf.Clamp01(input.FireKick01);
            pose.ForwardMeters -= Mathf.Max(0f, p.BlasterKickBackMeters) * kick;
            pose.PitchDeg -= Mathf.Max(0f, p.BlasterKickPitchDeg) * kick;
            pose.BodyPitchDeg -= 5f * kick;
            pose.RollDeg += Mathf.Sin(input.Time * 43f) * 1.5f * kick;
        }

        private static void SampleSplatling(ref WeaponWieldPose pose, in WeaponWieldInput input, in WeaponWieldParams p)
        {
            float charge = Mathf.Clamp01(input.Charge01);
            pose.DropMeters += Mathf.Max(0f, p.SplatlingChargeDropMeters) * charge;
            pose.ForwardMeters -= 0.03f * charge;
            pose.PitchDeg += 2f * charge;
            pose.BodyPitchDeg += Mathf.Max(0f, p.SplatlingChargeBodyPitchDeg) * charge;

            float stream = Mathf.Clamp01(input.Streaming01);
            pose.BodyPitchDeg += 4f * stream;
            pose.ForwardMeters -= 0.01f * stream;
            pose.YawDeg += Mathf.Sin(input.Time * 38f) * Mathf.Max(0f, p.SplatlingStreamShakeDeg) * stream;
            pose.RollDeg += Mathf.Cos(input.Time * 31f) * Mathf.Max(0f, p.SplatlingStreamShakeDeg) * 0.7f * stream;
        }

        /// <summary>枪管目标转速：扫射满速，蓄力随 Charge01 从最低速升到满速，其余 0。</summary>
        public static float SplatlingSpinDegPerSec(bool charging, bool streaming, float charge01, in WeaponWieldParams p)
        {
            if (streaming)
                return Mathf.Max(0f, p.SplatlingSpinMaxDegPerSec);
            if (charging)
            {
                return Mathf.Lerp(
                    Mathf.Max(0f, p.SplatlingSpinMinDegPerSec),
                    Mathf.Max(0f, p.SplatlingSpinMaxDegPerSec),
                    Mathf.Clamp01(charge01));
            }
            return 0f;
        }

        /// <summary>枪管惯性衰减一帧（deg/s）：松开后按 exp(-coastRate·dt) 滑停。</summary>
        public static float SplatlingSpinCoastStep(float spinDegPerSec, float dt, in WeaponWieldParams p)
            => Mathf.Max(0f, spinDegPerSec)
               * Mathf.Exp(-Mathf.Max(0f, p.SplatlingSpinCoastRate) * Mathf.Max(0f, dt));

        /// <summary>安全钳制：极端配置/连续冲量下也不让武器穿出角色身体。</summary>
        private static void Clamp(ref WeaponWieldPose pose)
        {
            pose.PitchDeg = Mathf.Clamp(pose.PitchDeg, -45f, 45f);
            pose.YawDeg = Mathf.Clamp(pose.YawDeg, -20f, 20f);
            pose.RollDeg = Mathf.Clamp(pose.RollDeg, -25f, 25f);
            pose.DropMeters = Mathf.Clamp(pose.DropMeters, 0f, 0.12f);
            pose.ForwardMeters = Mathf.Clamp(pose.ForwardMeters, -0.12f, 0.12f);
            pose.BodyPitchDeg = Mathf.Clamp(pose.BodyPitchDeg, -15f, 22f);
            pose.BodyRollDeg = Mathf.Clamp(pose.BodyRollDeg, -20f, 20f);
            pose.Sink01 = Mathf.Clamp01(pose.Sink01);
        }
    }
}