using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// Tidal Slam 起跳表演的四段权重求解（纯逻辑，可 EditMode 直接断言）。
    ///
    /// 为什么需要它：<c>WeaponController.UpdateSpecialSlam</c> 只把视觉根节点整体抬升 4.2m，
    /// 角色本体的 <c>IsGrounded</c> 全程为真，<c>CharacterVisualController</c> 于是继续跑贴地待机姿态
    /// —— 观感是「一具站直的身体被吊上去」。INKWAVE 的做法（character.js <c>_poseLeap</c> /
    /// <c>_poseSlam</c>）是把升空拆成 **蹬伸 → 团身 → 顶点举枪**，中途夹一次前空翻，落地再压成低蹲。
    ///
    /// 时间窗直接对齐 INKWAVE 的绝对秒数（它的 T_LEAP 计时器跨 rise+hang，T_SLAM 跨 fall+impact）：
    /// 蹬伸 0.04~0.24s、团身 0.12~0.56s、顶点 0.44s 起常驻、空翻 0.14~0.50s 转满一圈。
    /// 本类只输出 **权重**；所有角度 / 位移量级走 <see cref="CharacterAnimConfigSO"/>，便于调参。
    /// </summary>
    public struct SlamPerformance
    {
        /// <summary>蹬伸：起跳瞬间腿伸直、脚尖绷紧、背弓。</summary>
        public float LaunchW;
        /// <summary>团身：收腿抱膝、身体前卷。</summary>
        public float TuckW;
        /// <summary>顶点：滞空举枪过顶、双腿微开。</summary>
        public float HangW;
        /// <summary>俯冲：腿绷直向下、枪从过顶甩到身前。</summary>
        public float DiveW;
        /// <summary>落地：宽而低的蹲姿，枪砸进地里。</summary>
        public float ImpactW;
        /// <summary>前空翻累计角（弧度）。0 = 关闭；一整圈结束回到单位姿态，不会残留倾斜。</summary>
        public float FlipRadians;
        /// <summary>整体演出包络（0 = 完全不接管，1 = 完全接管姿态）。起跳快速淡入、落地收势淡出。</summary>
        public float Blend01;

        /// <summary>五段权重之和（&gt;1 时调用方应做归一化，避免相邻段叠加把角度叠爆）。</summary>
        public float TotalWeight => LaunchW + TuckW + HangW + DiveW + ImpactW;

        public static readonly SlamPerformance None = default;
    }

    public static class SlamPoseSolver
    {
        /// <summary>Slam 阶段编号，与 <c>WeaponController</c> 的 _slamPhase 对齐。</summary>
        public const int PhaseRise = 0;
        public const int PhaseHang = 1;
        public const int PhaseFall = 2;
        /// <summary>落地后的收势段（本体已不再空中，由视觉层自己计时）。</summary>
        public const int PhaseImpact = 3;

        // 时间窗（秒，绝对时间轴，对齐 INKWAVE character.js _poseLeap / _poseSlam）
        // 蹬伸/落地两段把淡入起点放到 -1，保证 t=0 当帧就是满权重：
        // 落地是硬冲击，晚一帧起蹲就会读成「先站着、再蹲下」。
        private const float LaunchIn0 = -1f, LaunchIn1 = 0.04f, LaunchOut0 = 0.12f, LaunchOut1 = 0.24f;
        private const float TuckIn0 = 0.12f, TuckIn1 = 0.26f, TuckOut0 = 0.42f, TuckOut1 = 0.56f;
        private const float HangIn0 = 0.44f, HangIn1 = 0.58f;
        private const float FlipStart = 0.14f, FlipEnd = 0.50f;
        private const float DiveEaseSeconds = 0.07f;
        private const float ImpactIn0 = -1f, ImpactIn1 = 0f;

        /// <summary>
        /// 采样当前应播放的起跳表演。
        /// </summary>
        /// <param name="phase">见 PhaseRise / PhaseHang / PhaseFall / PhaseImpact。</param>
        /// <param name="phaseElapsed">当前阶段已过秒数。</param>
        /// <param name="riseDuration">升空总时长（WeaponConfigSO.specialWindupDuration）。</param>
        /// <param name="hangDuration">顶点滞空时长。</param>
        /// <param name="fallDuration">下砸时长。</param>
        /// <param name="impactHoldSeconds">落地蹲姿保持秒数。</param>
        /// <param name="impactRecoverSeconds">蹲姿回到常态的秒数。</param>
        /// <param name="flipDegrees">前空翻总角度（0 = 关闭）。</param>
        public static SlamPerformance Sample(
            int phase,
            float phaseElapsed,
            float riseDuration,
            float hangDuration,
            float fallDuration,
            float impactHoldSeconds,
            float impactRecoverSeconds,
            float flipDegrees)
        {
            riseDuration = Mathf.Max(0.01f, riseDuration);
            hangDuration = Mathf.Max(0f, hangDuration);
            fallDuration = Mathf.Max(0.01f, fallDuration);
            impactHoldSeconds = Mathf.Max(0f, impactHoldSeconds);
            impactRecoverSeconds = Mathf.Max(0.01f, impactRecoverSeconds);

            SlamPerformance result = SlamPerformance.None;

            // 升空段用「leap 时间」统一计时：rise 阶段就是 elapsed，hang 阶段累加 rise 时长，
            // 下砸起 leap 已结束（窗口全部停在 1 区，收势交给 DiveW/ImpactW）。
            float leapTime;
            switch (phase)
            {
                case PhaseRise:
                    leapTime = Mathf.Max(0f, phaseElapsed);
                    break;
                case PhaseHang:
                    leapTime = riseDuration + Mathf.Max(0f, phaseElapsed);
                    break;
                default:
                    leapTime = riseDuration + hangDuration;
                    break;
            }

            if (phase == PhaseRise || phase == PhaseHang)
            {
                result.LaunchW = Window(leapTime, LaunchIn0, LaunchIn1, LaunchOut0, LaunchOut1);
                result.TuckW = Window(leapTime, TuckIn0, TuckIn1, TuckOut0, TuckOut1);
                // 顶点权重在升空尾段（0.44~0.58s）才淡到满 —— INKWAVE 的 win(lt,0.44,0.58,...) 同样如此，
                // 于是「升空结束」时约 0.88，滞空开始 0.03s 后到 1.0，交接是渐进的不是硬切
                result.HangW = Window(leapTime, HangIn0, HangIn1, float.MaxValue, float.MaxValue);

                float flip01 = Ease01((leapTime - FlipStart) / Mathf.Max(0.0001f, FlipEnd - FlipStart));
                result.FlipRadians = Mathf.Max(0f, flipDegrees) * Mathf.Deg2Rad * flip01;
                result.Blend01 = Mathf.Clamp01(leapTime / 0.05f);
            }
            else if (phase == PhaseFall)
            {
                result.DiveW = Ease01(Mathf.Max(0f, phaseElapsed) / DiveEaseSeconds);
                result.Blend01 = 1f;
            }

            if (phase == PhaseImpact)
            {
                float t = Mathf.Max(0f, phaseElapsed);
                result.ImpactW = Window(
                    t, ImpactIn0, ImpactIn1, impactHoldSeconds, impactHoldSeconds + impactRecoverSeconds);
                // 保持段权重 1，恢复段随蹲姿一起淡出，保证姿态不会在恢复末尾突然跳回
                float fade = 1f - Ease01(
                    (t - impactHoldSeconds) / impactRecoverSeconds);
                result.Blend01 = Mathf.Max(result.Blend01, Mathf.Clamp01(fade));
            }

            // 一整圈空翻在时间轴末端回到 360°=单位姿态；超过一圈时按剩余角度截断，避免无限累加
            result.FlipRadians = Mathf.Min(result.FlipRadians, Mathf.Max(0f, flipDegrees) * Mathf.Deg2Rad);

            return result;
        }

        /// <summary>
        /// 梯形窗：in0→in1 淡入，保持到 out0，out0→out1 淡出。
        /// out0/out1 传 float.MaxValue 表示「不再淡出」（顶点段常驻到阶段结束）。
        /// </summary>
        public static float Window(float t, float in0, float in1, float out0, float out1)
        {
            if (t <= in0) return 0f;
            if (t >= out1) return 0f;

            if (t < in1)
                return Ease01((t - in0) / Mathf.Max(0.0001f, in1 - in0));
            if (t <= out0)
                return 1f;
            return 1f - Ease01((t - out0) / Mathf.Max(0.0001f, out1 - out0));
        }

        /// <summary>0..1 smoothstep（缓入缓出），越界自动夹紧。</summary>
        public static float Ease01(float t)
        {
            float x = Mathf.Clamp01(t);
            return x * x * (3f - 2f * x);
        }
    }
}
