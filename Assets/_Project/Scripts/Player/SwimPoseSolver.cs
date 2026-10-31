using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 乌贼游动姿态纯函数求解器（对标 EggGaitSolver 的人形态步态）。
    /// 数值全部来自 INKWAVE 逆向源码（inkwave-aah-research/src/game/character.js L3550-3611）：
    /// 身体起伏波（频率随速度 5→14 rad/s）、转向侧倾 banking（-yawRate·0.08，钳 ±20° 保守版）、
    /// 保体积速度拉伸（Z 轴 1+0.2·sv，横向 1/√sy）、头部点摆、触手行波（逐根相位差近似根→尖传播）。
    /// 纯静态函数零 GC，供 CharacterVisualController 与 EditMode 单测共用。
    /// </summary>
    public static class SwimPoseSolver
    {
        /// <summary>游动姿态采样结果（一次算齐，避免逐字段重复计算）。</summary>
        public struct SwimPose
        {
            public float PitchDeg;    // 身体起伏俯仰（±undulationPitch·sv）
            public float RollDeg;     // 转向侧倾（banking）
            public float SwayDeg;     // 头部点摆偏航
            public float StretchZ;    // 沿游进方向拉伸（保体积）
            public float StretchXZ;   // 横向收缩 = 1/√StretchZ
        }

        /// <summary>真实位移速度 → 速度系数 sv（全速 = 1）。</summary>
        public static float Speed01(float planarSpeed, float swimRefSpeed = 11.8f)
        {
            return Mathf.Clamp01(planarSpeed / Mathf.Max(0.1f, swimRefSpeed));
        }

        /// <summary>起伏波频率 (rad/s)：慢速 5 → 全速 14。</summary>
        public static float UndulationFreq(float sv, float slow, float fast)
        {
            return Mathf.Lerp(slow, fast, sv);
        }

        /// <summary>
        /// 采样游动姿态。t = 累计时间（秒），yawRateDeg = 偏航角速度（度/秒，右转为正）。
        /// 注意：CharacterVisualController 的 _yawRate 来自 Mathf.DeltaAngle/dt，单位是度/秒；
        /// 侧倾公式 rollDeg = -yawRateDeg·factor 与 Inkwave 的 rollRad = -yawRateRad·0.08 量纲等价
        /// （0.08 rad per rad/s ≡ 0.08 deg per deg/s）。
        /// </summary>
        public static SwimPose Sample(float sv, float t, float yawRateDeg, CharacterAnimConfigSO cfg)
        {
            var pose = new SwimPose();

            // 保体积速度拉伸：纵向拉长、横向按 1/√ 收缩（character.js L3558: sy = 1+0.2sv, sxz = 1/√sy）
            pose.StretchZ = 1f + Mathf.Clamp01(cfg.swimStretchMax) * sv;
            pose.StretchXZ = 1f / Mathf.Sqrt(pose.StretchZ);

            // 身体起伏波：频率随速度，幅度随速度（慢速近乎静止，避免隐匿蠕动穿帮）
            float undFreq = UndulationFreq(sv, cfg.swimUndulationFreqSlow, cfg.swimUndulationFreqFast);
            float und = Mathf.Sin(t * undFreq);
            pose.PitchDeg = und * cfg.swimUndulationPitchDeg * sv;

            // 转向侧倾：角速度 → 滚转（负号 = 内倾压弯），钳制防急打转圈翻车
            pose.RollDeg = Mathf.Clamp(
                -yawRateDeg * cfg.swimBankFactor,
                -cfg.swimBankMaxDeg,
                cfg.swimBankMaxDeg);

            // 头部点摆（character.js L3556: yaw + 0.1·sin(t·lerp(4,11,sv)+1)·sv，0.1rad ≈ 5.7°）
            float swayFreq = Mathf.Lerp(cfg.swimSwayFreqSlow, cfg.swimSwayFreqFast, sv);
            pose.SwayDeg = Mathf.Sin(t * swayFreq + 1f) * cfg.swimSwayDeg * sv;

            return pose;
        }

        /// <summary>
        /// 单根触须的摆角（度）。根→尖行波用相邻触须相位差近似（P1 无蒙皮，不能走顶点着色器行波）。
        /// flapDeg = 前后拍水，sideDeg = 侧向飘摆。
        /// </summary>
        public static void TentacleFlap(
            float sv, float t, int tentacleIndex, CharacterAnimConfigSO cfg,
            out float flapDeg, out float sideDeg)
        {
            float freq = Mathf.Lerp(cfg.tentacleFreqSlow, cfg.tentacleFreqFast, sv);
            float amp = Mathf.Lerp(cfg.tentacleAmpIdleDeg, cfg.tentacleAmpFastDeg, sv);
            float phase = tentacleIndex * cfg.tentaclePhaseStep;

            flapDeg = Mathf.Sin(t * freq - phase) * amp;
            sideDeg = Mathf.Sin(t * freq * 0.8f + phase * 1.3f) * amp * cfg.tentacleSideSwayFactor;
        }
    }
}
