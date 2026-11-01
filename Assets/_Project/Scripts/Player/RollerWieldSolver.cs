using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 滚筒武器单帧持握姿态（纯数据，无 Transform 依赖）。
    /// 俯仰约定与 Unity 一致：负 = 滚筒抬起/举过肩，正 = 滚筒压向地面。
    /// </summary>
    public struct RollerWieldPose
    {
        /// <summary>武器模型俯仰附加角（度）。</summary>
        public float PitchDeg;
        /// <summary>武器挂点竖向下压量（米，≥ 0）。</summary>
        public float DropMeters;
        /// <summary>武器挂点水平前推量（米，≥ 0）。</summary>
        public float ForwardMeters;
        /// <summary>胸口附加俯仰（度，正 = 前倾压向滚筒）。</summary>
        public float BodyPitchDeg;
        /// <summary>身体重心下沉比例（0~1，竖向缩放乘 (1 - Sink)）。</summary>
        public float Sink01;
        /// <summary>滚轴贴地闭环权重（0~1）：1 = 由控制器把滚轴底缘钉在地面，0 = 纯姿态播放。</summary>
        public float GroundContact01;

        public static readonly RollerWieldPose Neutral = new RollerWieldPose
        {
            PitchDeg = 0f,
            DropMeters = 0f,
            ForwardMeters = 0f,
            BodyPitchDeg = 0f,
            Sink01 = 0f,
            GroundContact01 = 0f
        };
    }

    /// <summary>滚筒持握参数包（由 CharacterAnimConfigSO 填充；纯函数不直接依赖 SO，便于单测与复用）。</summary>
    public struct RollerWieldParams
    {
        public float CarryPitchDeg;
        public float FlickLiftPitchDeg;
        public float FlickSlamPitchDeg;
        public float FlickSlamDropMeters;
        public float FlickWhipDuration;
        public float FlickFollowDuration;
        public float FlickRecoverDuration;
        public float FlickCoilBodyPitchDeg;
        public float FlickWhipBodyPitchDeg;
        public float SlamSquashImpulse;
        public float RollPitchDeg;
        public float RollDropMeters;
        public float RollForwardMeters;
        public float RollBodyPitchDeg;
        public float RollSink;
        public float RollWeightInRate;
        public float RollWeightOutRate;
        public float DrumFlickSpinRad;
        public float DrumCoastDecay;

        /// <summary>默认值 = 本类常量（与 CharacterAnimConfigSO 滚筒字段默认值一致，测试锁定两者相等）。</summary>
        public static RollerWieldParams Default => new RollerWieldParams
        {
            CarryPitchDeg = RollerWieldSolver.CarryPitchDeg,
            FlickLiftPitchDeg = RollerWieldSolver.FlickLiftPitchDeg,
            FlickSlamPitchDeg = RollerWieldSolver.FlickSlamPitchDeg,
            FlickSlamDropMeters = RollerWieldSolver.FlickSlamDropMeters,
            FlickWhipDuration = RollerWieldSolver.FlickWhipDuration,
            FlickFollowDuration = RollerWieldSolver.FlickFollowDuration,
            FlickRecoverDuration = RollerWieldSolver.FlickRecoverDuration,
            FlickCoilBodyPitchDeg = RollerWieldSolver.FlickCoilBodyPitchDeg,
            FlickWhipBodyPitchDeg = RollerWieldSolver.FlickWhipBodyPitchDeg,
            SlamSquashImpulse = RollerWieldSolver.SlamSquashImpulse,
            RollPitchDeg = RollerWieldSolver.RollPitchDeg,
            RollDropMeters = RollerWieldSolver.RollDropMeters,
            RollForwardMeters = RollerWieldSolver.RollForwardMeters,
            RollBodyPitchDeg = RollerWieldSolver.RollBodyPitchDeg,
            RollSink = RollerWieldSolver.RollSink,
            RollWeightInRate = RollerWieldSolver.RollWeightInRate,
            RollWeightOutRate = RollerWieldSolver.RollWeightOutRate,
            DrumFlickSpinRad = RollerWieldSolver.DrumFlickSpinRad,
            DrumCoastDecay = RollerWieldSolver.DrumCoastDecay
        };
    }

    /// <summary>
    /// 滚筒武器持握/攻击姿态求解（P27，参考 INKWAVE character.js / character-weapons.js / weapons.js）：
    /// 1. 持握（carry）：滚筒抬起斜扛，替代旧版平举的死板姿态；
    /// 2. 挥击（flick，对应 _poseFlick 四段）：前摇期抡起过肩 → 释放瞬间加速砸地（与 9 滴墨同步）→
    ///    随挥保持 → 收势回持握；身体配合蓄力后仰 / 甩出前压；
/// 3. 拖地（roll，对应 wRoll 姿态）：滚筒压到地面、身体前倾、重心下沉，
        ///    权重按 INKWAVE 入 11 / 出 6 的阻尼混合；GroundContact01 标记"需要贴地闭环"的时段，
        ///    由 CharacterVisualController 依据真实骨架/地面射线做俯仰修正（视角/坡度不再影响贴地）；
    /// 4. 滚轴自转：ω = 地速 / 滚筒半径（无滑动滚动），停转按 exp(-decay·t) 惯性衰减，挥击甩转冲量。
    /// 纯函数、无状态、零分配，可在 EditMode 直接单测；Transform 写回由 CharacterVisualController 负责。
    /// </summary>
    public static class RollerWieldSolver
    {
        // ---- 持握 / 挥击角度（度）----
        /// <summary>待机持握俯仰：滚筒抬起斜扛（旧版 0° 平举显得死板）。</summary>
        public const float CarryPitchDeg = -28f;
        /// <summary>挥击抡起角：滚筒举过肩（INKWAVE _poseFlick 蓄力 -2.30rad ≈ -132°，按短杠杆收敛）。</summary>
        public const float FlickLiftPitchDeg = -115f;
        /// <summary>砸地角：whip 段末滚筒吻到身前地面（真实骨架标定：body18° 时滚筒轴心 y=0.095，
        /// 底缘 -0.015 轻陷地板，保留冲击观感）。</summary>
        public const float FlickSlamPitchDeg = 5f;
        /// <summary>砸地时武器挂点下压量（米）。</summary>
        public const float FlickSlamDropMeters = 0.03f;

        // ---- 挥击时间线（秒；前摇时长不在本表——跟 WeaponConfigSO.flickWindup 走）----
        public const float FlickWhipDuration = 0.09f;
        public const float FlickFollowDuration = 0.10f;
        public const float FlickRecoverDuration = 0.30f;

        // ---- 身体配合（度 / 比例）----
        /// <summary>蓄力（前摇）胸口后仰。</summary>
        public const float FlickCoilBodyPitchDeg = -12f;
        /// <summary>甩出瞬间胸口前压。</summary>
        public const float FlickWhipBodyPitchDeg = 18f;
        /// <summary>砸地瞬间果冻压扁冲量（复用跳跃/落地弹簧，负向压扁由控制器取负施加）。</summary>
        public const float SlamSquashImpulse = 2.8f;

        // ---- 拖地姿态（roll）：武器挂在 Chest 下，胸口俯仰（视角/移动前倾）会把长杠杆放大成数厘米的
        // 滚筒高度变化，固定角度不可能在所有视角下都贴地。以下为默认视角（body10°）的**造型基准**，
        // 真正的贴地由 CharacterVisualController 的闭环俯仰修正完成（GroundContact01 + 地面射线）。
        // 标定：body10° 拖行 p25/d0 → 滚筒轴心 y≈0.114，主鼓底缘约 +0.004 吻地；
        // body18° 砸地 p5/d0.03 → y=0.095（底缘 -0.015 轻陷保留冲击）。
        // 见 Tests RollerSolver_RollPoseCalibratedOnRig 与 PlayMode RollerGroundContactPlayTests。
        /// <summary>滚筒主鼓外皮半径（米）：DrumMesh 直径 0.24 × 模型缩放 0.92 / 2 = 0.1104。
        /// 贴地与滚轴自转都按主鼓体计算——TreadRib 花纹只比主鼓高 4.6mm，按花纹外缘贴地会让
        /// 大面积的队伍色鼓体悬空近 1cm，正是"看起来浮着"的来源；按主鼓贴地时花纹轻陷墨面，观感才对。</summary>
        public const float DrumRadiusMeters = 0.1104f;
        /// <summary>贴地闭环的目标压入量（米）：滚轴外缘略陷进墨面，读作"压住"而不是"擦过"。</summary>
        public const float GroundContactPenetration = 0.004f;

        public const float RollPitchDeg = 25f;
        public const float RollDropMeters = 0.00f;
        public const float RollForwardMeters = 0.10f;
        public const float RollBodyPitchDeg = 10f;
        public const float RollSink = 0.05f;

        // ---- 拖地权重阻尼（INKWAVE wRoll：切入 11 / 退出 6）----
        public const float RollWeightInRate = 11f;
        public const float RollWeightOutRate = 6f;

        // ---- 滚轴自转（character-weapons.js L691-699）----
        /// <summary>挥击砸地瞬间滚轴甩转冲量（rad/s，INKWAVE +34）。</summary>
        public const float DrumFlickSpinRad = 34f;
        /// <summary>滚轴惯性衰减系数（1/s，INKWAVE 2.2：约 0.45s 衰减到 37%）。</summary>
        public const float DrumCoastDecay = 2.2f;

        /// <summary>挥击时间线总时长（前摇 + 砸下 + 随挥 + 收势），控制器据此结束状态机。</summary>
        public static float TotalDuration(float windup, in RollerWieldParams p)
            => Mathf.Max(0f, windup)
               + Mathf.Max(0.01f, p.FlickWhipDuration)
               + Mathf.Max(0f, p.FlickFollowDuration)
               + Mathf.Max(0f, p.FlickRecoverDuration);

        /// <summary>持握姿态（滚筒抬起斜扛）。</summary>
        public static RollerWieldPose SampleCarry(in RollerWieldParams p)
        {
            RollerWieldPose pose = RollerWieldPose.Neutral;
            pose.PitchDeg = p.CarryPitchDeg;
            return pose;
        }

        /// <summary>
        /// 采样挥击时间线（time 从前摇开始计秒；前摇时长 windup 来自 WeaponConfigSO.flickWindup）。
        /// 分段：[0, windup] 抡起（smoothstep）→ (windup, +whip] 加速砸地（easeIn）→
        /// (+whip, +follow] 随挥保持 → (+follow, +recover] 收势（easeOut 回持握）。
        /// 释放瞬间（IsFlicking 下降沿）不必传给本函数——时间线只由时钟驱动，
        /// 释放边沿触发的果冻冲量与滚轴甩转由控制器负责，两段在抡起角处天然连续。
        /// </summary>
        public static RollerWieldPose SampleFlick(float time, float windup, in RollerWieldParams p)
        {
            windup = Mathf.Max(0f, windup);
            float whip = Mathf.Max(0.01f, p.FlickWhipDuration);

            // 1. 抡起（前摇）：持握角 → 举过肩
            if (time <= windup)
            {
                float t01 = windup > 0.0001f ? Mathf.Clamp01(time / windup) : 1f;
                float e = Mathf.SmoothStep(0f, 1f, t01);
                RollerWieldPose pose = RollerWieldPose.Neutral;
                pose.PitchDeg = Mathf.Lerp(p.CarryPitchDeg, p.FlickLiftPitchDeg, e);
                pose.BodyPitchDeg = Mathf.Lerp(0f, p.FlickCoilBodyPitchDeg, e);
                return pose;
            }

            float tRelease = time - windup;

            // 2. 砸下：easeIn 加速砸向地面（与墨滴释放同步开始）。
            // 贴地权重只在末段（60%→100%）并入，避免把整段抡砸轨迹提前拽向地面、削弱挥击弧线。
            if (tRelease <= whip)
            {
                float u = Mathf.Clamp01(tRelease / whip);
                float e = u * u;
                RollerWieldPose pose = RollerWieldPose.Neutral;
                pose.PitchDeg = Mathf.Lerp(p.FlickLiftPitchDeg, p.FlickSlamPitchDeg, e);
                pose.DropMeters = Mathf.Lerp(0f, p.FlickSlamDropMeters, e);
                pose.BodyPitchDeg = Mathf.Lerp(p.FlickCoilBodyPitchDeg, p.FlickWhipBodyPitchDeg, e);
                pose.GroundContact01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.6f) / 0.4f));
                return pose;
            }

            tRelease -= whip;

            // 3. 随挥：保持砸地姿态一拍（贴地闭环满权重，滚轴真正压在地上）
            if (tRelease <= Mathf.Max(0f, p.FlickFollowDuration))
            {
                RollerWieldPose pose = RollerWieldPose.Neutral;
                pose.PitchDeg = p.FlickSlamPitchDeg;
                pose.DropMeters = p.FlickSlamDropMeters;
                pose.BodyPitchDeg = p.FlickWhipBodyPitchDeg;
                pose.GroundContact01 = 1f;
                return pose;
            }

            // 4. 收势：easeOut 回持握（越界 v 钳到 1，即收势后稳定在持握姿态），贴地权重同步释放
            tRelease -= Mathf.Max(0f, p.FlickFollowDuration);
            float v = Mathf.Clamp01(tRelease / Mathf.Max(0.0001f, p.FlickRecoverDuration));
            float ev = 1f - (1f - v) * (1f - v);
            RollerWieldPose tail = RollerWieldPose.Neutral;
            tail.PitchDeg = Mathf.Lerp(p.FlickSlamPitchDeg, p.CarryPitchDeg, ev);
            tail.DropMeters = Mathf.Lerp(p.FlickSlamDropMeters, 0f, ev);
            tail.BodyPitchDeg = Mathf.Lerp(p.FlickWhipBodyPitchDeg, 0f, ev);
            tail.GroundContact01 = 1f - ev;
            return tail;
        }

        /// <summary>拖地姿态（滚筒压地、身体前倾、重心下沉；贴地权重交给控制器闭环）。</summary>
        public static RollerWieldPose SampleRoll(in RollerWieldParams p)
        {
            RollerWieldPose pose = RollerWieldPose.Neutral;
            pose.PitchDeg = p.RollPitchDeg;
            pose.DropMeters = p.RollDropMeters;
            pose.ForwardMeters = p.RollForwardMeters;
            pose.BodyPitchDeg = p.RollBodyPitchDeg;
            pose.Sink01 = p.RollSink;
            pose.GroundContact01 = 1f;
            return pose;
        }

        /// <summary>两姿态逐字段插值（t 钳制 0~1）。</summary>
        public static RollerWieldPose Blend(in RollerWieldPose a, in RollerWieldPose b, float t)
        {
            t = Mathf.Clamp01(t);
            RollerWieldPose pose;
            pose.PitchDeg = Mathf.Lerp(a.PitchDeg, b.PitchDeg, t);
            pose.DropMeters = Mathf.Lerp(a.DropMeters, b.DropMeters, t);
            pose.ForwardMeters = Mathf.Lerp(a.ForwardMeters, b.ForwardMeters, t);
            pose.BodyPitchDeg = Mathf.Lerp(a.BodyPitchDeg, b.BodyPitchDeg, t);
            pose.Sink01 = Mathf.Lerp(a.Sink01, b.Sink01, t);
            pose.GroundContact01 = Mathf.Lerp(a.GroundContact01, b.GroundContact01, t);
            return pose;
        }

        /// <summary>贴地闭环的目标滚轴轴心世界高度：地面 + 主鼓半径 - 压入量（压入量为正时鼓体略陷进墨面）。</summary>
        public static float GroundContactTargetAxisY(float groundWorldY, float penetration = GroundContactPenetration)
            => groundWorldY + DrumRadiusMeters - Mathf.Max(0f, penetration);

        /// <summary>
        /// 贴地闭环的目标 world forward.y：绕握把俯仰时，滚轴轴心高度 = 握把高度 + 杠杆长 × forward.y。
        /// 返回钳制到 [-1, 1] 的目标分量，供控制器用数值斜率反解俯仰增量（零分配、可直接单测）。
        /// </summary>
        public static float GroundContactTargetForwardY(
            float gripWorldY, float targetAxisWorldY, float leverAlongForward)
        {
            if (leverAlongForward <= 0.0001f)
                return 0f;
            return Mathf.Clamp((targetAxisWorldY - gripWorldY) / leverAlongForward, -1f, 1f);
        }

        /// <summary>
        /// 滚轴自转角速度（rad/s）：拖地且贴地时按地速无滑动滚动，否则 0（交给惯性衰减）。
        /// </summary>
        public static float DrumSpinRadPerSec(float groundSpeed, bool rolling)
            => rolling ? Mathf.Max(0f, groundSpeed) / DrumRadiusMeters : 0f;

        /// <summary>滚轴惯性衰减一帧（rad/s）：停转后按 exp(-decay·dt) 滑行减速。</summary>
        public static float DrumCoastStep(float spinRadPerSec, float decay, float dt)
            => spinRadPerSec * Mathf.Exp(-Mathf.Max(0f, decay) * Mathf.Max(0f, dt));
    }
}
