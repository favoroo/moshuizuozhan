using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// P37 程序化角色视觉层的帧率健壮性纯数学（不依赖 MonoBehaviour 生命周期，EditMode 友好）：
    /// 1. 视觉层帧间隔钳制 —— 卡顿帧 / 切后台时 Time.deltaTime 会飙到几十上百毫秒，
    ///    而步态相位是 <c>_gaitPhase += π·cadence·dt</c>，一次大 dt 会把相位推进大半圈，
    ///    姿态直接跳到随机相位（观感「人抽一下」），低帧率下更像头在乱跳；
    /// 2. 骨盆竖向起伏的低通时间常数（帧率自适应）—— 几何贴地下沉是 6 步/秒的「方波」
    ///    （4.5 m/s 以上约 40% 相位贴住 kidHipDropMaxMeters 封顶），直接写回 Hips 会让
    ///    整个上半身含头跟着 6 Hz 抖；掉帧越狠越要压平，否则混叠成不规则跳动。
    /// </summary>
    public static class VisualMotionMath
    {
        /// <summary>视觉层单帧 dt 上限（秒）：只兜住「真卡顿」（读盘/GC/切后台/首帧着色器编译造成的 ≥100ms 假帧），
        /// 正常掉帧（10~100ms）不介入 —— 若按更紧的上限截断，视觉层时间基准会与移动层
        /// （InkCharacterMotor 对 CharacterController 的 0.05s 钳制）解耦，脚会与位移脱节，
        /// 破坏 CharacterVisualPlayTests 的「每米触地次数」防滑契约。</summary>
        public const float MaxVisualDt = 0.1f;

        /// <summary>低帧率端点（FPS）：不高于它按「最平滑」处理。</summary>
        public const float BobSmoothLowFps = 18f;
        /// <summary>高帧率端点（FPS）：不低于它按「正常手感」处理。</summary>
        public const float BobSmoothHighFps = 45f;

        /// <summary>
        /// 视觉层 dt 消毒：NaN / 无穷 / 负数一律当 0（调用方据此跳过本帧），
        /// 超出上限的卡顿帧按上限处理（宁可动画慢一拍，也不要把姿态推飞）。
        /// </summary>
        public static float SanitizeFrameDt(float rawDt, float maxDt = MaxVisualDt)
        {
            if (float.IsNaN(rawDt) || float.IsInfinity(rawDt) || rawDt <= 0f)
                return 0f;

            float cap = maxDt > 0f ? maxDt : MaxVisualDt;
            return rawDt > cap ? cap : rawDt;
        }

        /// <summary>
        /// 帧率归一（0 = 低帧率档，1 = 高帧率档）。低帧率端点必须小于高帧率端点，
        /// 参数颠倒时自动交换；dt ≤ 0 视为低帧率（保守取最平滑）。
        /// </summary>
        public static float FrameRate01(float dt, float lowFps = BobSmoothLowFps, float highFps = BobSmoothHighFps)
        {
            if (lowFps > highFps)
            {
                float swap = lowFps;
                lowFps = highFps;
                highFps = swap;
            }
            if (float.IsNaN(dt) || dt <= 0f)
                return 0f;

            float fps = 1f / dt;
            if (fps <= lowFps) return 0f;
            if (fps >= highFps) return 1f;
            return (fps - lowFps) / (highFps - lowFps);
        }

        /// <summary>
        /// 骨盆竖向起伏的平滑时间常数（秒）：高帧率用 <paramref name="smoothTimeFast"/>（保留跑姿微沉），
        /// 低帧率用 <paramref name="smoothTimeLowFps"/>（把起伏压成缓波，防混叠乱跳）。
        /// 时间常数只增不减：传入非法值时回落到快速档。
        /// </summary>
        public static float BobSmoothTime(float dt, float smoothTimeFast, float smoothTimeLowFps,
            float lowFps = BobSmoothLowFps, float highFps = BobSmoothHighFps)
        {
            if (float.IsNaN(smoothTimeFast) || smoothTimeFast <= 0f)
                smoothTimeFast = 0.07f;
            if (float.IsNaN(smoothTimeLowFps) || smoothTimeLowFps < smoothTimeFast)
                smoothTimeLowFps = smoothTimeFast;

            return Mathf.Lerp(smoothTimeLowFps, smoothTimeFast, FrameRate01(dt, lowFps, highFps));
        }
    }
}
