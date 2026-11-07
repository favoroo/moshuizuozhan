using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 双枪战术翻滚的单帧姿态求解结果（纯数据）。
    /// </summary>
    public struct DodgeRollPose
    {
        /// <summary>累计转角（弧度）。u=1 时恰为整圈 = 单位姿态，翻完不残留倾斜。</summary>
        public float TumbleRadians;
        /// <summary>转轴 X 分量（KidRoot 局部空间，y 恒为 0）。</summary>
        public float TumbleAxisX;
        /// <summary>转轴 Z 分量（KidRoot 局部空间，y 恒为 0）。</summary>
        public float TumbleAxisZ;
        /// <summary>蜷身权重（0=不接管，1=完全团身）。进入快、滚完展开，直接衔接架枪。</summary>
        public float Tuck01;
        /// <summary>本帧整体抬升（米）。翻滚中段给一点跳弧，落地时归零，保证落地贴地。</summary>
        public float LiftMeters;
        /// <summary>纵向压缩乘子（&lt;1 = 压扁）。蜷身时压，落地展开。</summary>
        public float SquashMul;

        public static readonly DodgeRollPose None = default;
    }

    /// <summary>
    /// 双枪战术翻滚的姿态参数（全部来自 <see cref="CharacterAnimConfigSO"/>，便于调参）。
    /// </summary>
    public struct DodgeRollPoseParams
    {
        /// <summary>总转角（度）。360 = 完整前滚翻。</summary>
        public float TumbleDegrees;
        /// <summary>快出缓停指数（越大起步越爆发）。INKWAVE _poseDodge 用 2.4。</summary>
        public float TumbleEaseExponent;
        /// <summary>翻滚中段最大抬升（米）。</summary>
        public float LiftMeters;
        /// <summary>蜷身纵向压缩量（0-1，0.07 ≈ INKWAVE SQY 压 7%）。</summary>
        public float SquashAmount;

        public static DodgeRollPoseParams Default => new DodgeRollPoseParams
        {
            TumbleDegrees = 360f,
            TumbleEaseExponent = 2.4f,
            LiftMeters = 0.30f,
            SquashAmount = 0.07f
        };
    }

    /// <summary>
    /// 双枪战术翻滚的姿态求解（纯逻辑、零分配，可 EditMode 直接断言）。
    ///
    /// 为什么需要它：旧翻滚只有「匀速滑行 + 身体侧倾 16°」，观感是平移冲刺而不是翻滚。
    /// INKWAVE 的配方（character.js <c>_poseDodge</c> / weapons.js <c>dodgeVel</c>）：
    /// 一整圈前滚翻绕 <c>up × 滚向</c> 轴、快出缓停 <c>1-(1-u)^2.4</c>，
    /// 配合蜷身（win(0,0.1,0.62,0.96)：0.1s 内抱团、0.62s 起展开）、纵向压扁与落地归位，
    /// 滚完身体恰好转回单位姿态、直接接 0.5s 架枪站桩。
    ///
    /// 本类只输出权重 / 角度 / 轴向；变换应用（KidRoot 旋转、pivot 补偿、蜷身姿势混合）
    /// 由 <see cref="CharacterVisualController"/> 完成，角度与量级走参数结构便于调参。
    /// </summary>
    public static class DodgeRollPoseSolver
    {
        /// <summary>蜷身时间窗（秒为单位的 progress 窗）：0~0.1s 抱团，0.62s 起到 0.96s 展开完毕。</summary>
        private const float TuckIn0 = 0f, TuckIn1 = 0.10f, TuckOut0 = 0.62f, TuckOut1 = 0.96f;

        /// <summary>
        /// 采样翻滚某一进度下的姿态。
        /// </summary>
        /// <param name="progress01">翻滚进度 0-1（elapsed / duration）。</param>
        /// <param name="dodgeSide">滚向相对身体右方的分量（触发时锁定，-1 左 ~ +1 右）。</param>
        /// <param name="dodgeForward">滚向相对身体前方的分量（-1 后 ~ +1 前）。</param>
        /// <param name="p">姿态参数。</param>
        public static DodgeRollPose Sample(float progress01, float dodgeSide, float dodgeForward, in DodgeRollPoseParams p)
        {
            float u = Mathf.Clamp01(progress01);
            if (u <= 0f)
            {
                return DodgeRollPose.None;
            }

            // 一整圈快出缓停：起步角速度约 2.4 倍均值，落地段缓缓转正，u=1 恰回单位姿态
            float easeExp = Mathf.Max(0.5f, p.TumbleEaseExponent);
            float tumble01 = 1f - Mathf.Pow(1f - u, easeExp);
            float tumbleDegrees = Mathf.Max(0f, p.TumbleDegrees) * tumble01;

            // 转轴 = up × 滚向（身体局部空间）：前滚 → +X（头前下栽），
            // 右滚 → -Z（向右倾转），斜向自动插值 —— 与 INKWAVE tumbleX/Z 同构
            Vector3 dir = new Vector3(dodgeSide, 0f, dodgeForward);
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector3.forward;
            }
            dir.Normalize();

            // 蜷身窗：早抱团（0.1s 内，赶在身体转横之前缩回收转半径）、后展开（0.62s 起落位架枪）
            float tuck = Window(u, TuckIn0, TuckIn1, TuckOut0, TuckOut1);

            // 中段跳弧：sin(π·u) 两端归零 —— 起跳贴地、落地贴地，只在翻转最吃半径的半程托一把
            float lift = Mathf.Max(0f, p.LiftMeters) * Mathf.Sin(Mathf.PI * u);

            return new DodgeRollPose
            {
                TumbleRadians = tumbleDegrees * Mathf.Deg2Rad,
                TumbleAxisX = dir.z,
                TumbleAxisZ = -dir.x,
                Tuck01 = tuck,
                LiftMeters = lift,
                SquashMul = 1f - Mathf.Clamp01(p.SquashAmount) * tuck
            };
        }

        /// <summary>INKWAVE 的 win(t, i0, i1, o0, o1)：淡入 × (1 - 淡出)。</summary>
        private static float Window(float t, float in0, float in1, float out0, float out1)
        {
            float fadeIn = SmoothStep(in0, in1, t);
            float fadeOut = 1f - SmoothStep(out0, out1, t);
            return fadeIn * fadeOut;
        }

        private static float SmoothStep(float a, float b, float x)
        {
            if (Mathf.Approximately(a, b))
            {
                return x >= b ? 1f : 0f;
            }
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
