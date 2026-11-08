using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// Ink Tempest 投掷表演的权重求解（纯逻辑，可 EditMode 直接断言，P41）。
    ///
    /// 对齐 Inkwave character.js <c>_poseThrow</c> 的三段节奏：引臂后仰（cock）→ 鞭甩出手（whip）
    /// → 随挥收势（follow-through）。发生器在 t=0 即离手（与 Inkwave 一致：projectile leaves at t≈0），
    /// 0.35s 的动作锁只控制移动 —— 表演窗口更长，控制权恢复后身体继续完成随挥再渐出回步态，
    /// 观感是「扔完顺势收」而不是「僵在原地」。
    ///
    /// 本类只输出权重；所有角度 / 位移量级走 <see cref="CharacterAnimConfigSO"/>，便于调参。
    /// 相邻段过渡区权重之和可能 &gt;1，调用方需按 TotalWeight 归一化（与 Slam 的 legW 同构）。
    /// </summary>
    public struct StormThrowPerformance
    {
        /// <summary>引臂：躯干后仰侧倾蓄势、武器抬过肩。</summary>
        public float CockW;
        /// <summary>鞭甩：躯干前压、武器向前甩出（爆发段）。</summary>
        public float WhipW;
        /// <summary>随挥：身体前倾保持、武器顺势收势（惯性段）。</summary>
        public float FollowW;
        /// <summary>整体演出包络（0 = 完全不接管，1 = 完全接管姿态）。起手快速淡入、尾段渐出。</summary>
        public float Blend01;

        /// <summary>三段权重之和（&gt;1 时调用方应做归一化，避免相邻段叠加把角度叠爆）。</summary>
        public float TotalWeight => CockW + WhipW + FollowW;

        public static readonly StormThrowPerformance None = default;
    }

    public static class StormPoseSolver
    {
        // 时间窗（秒，绝对时间轴，对齐 Inkwave _poseThrow 的 cock→whip→follow-through）
        // 引臂从 t=0 满权重起手（In0=-1）：投掷是快动作，慢起手会读成「犹豫」。
        // 鞭甩 0.10~0.28s 顶满 —— 发生器 t=0 已离手，鞭甩是「身体跟上」而不是「决定出手」。
        private const float CockIn0 = -1f, CockIn1 = 0.02f, CockOut0 = 0.10f, CockOut1 = 0.18f;
        private const float WhipIn0 = 0.10f, WhipIn1 = 0.14f, WhipOut0 = 0.20f, WhipOut1 = 0.28f;
        private const float FollowIn0 = 0.24f, FollowIn1 = 0.30f, FollowOut0 = 0.42f, FollowOut1 = 0.55f;

        /// <summary>引臂窗结束的时刻（鞭甩交接的基准点，测试断言用）。</summary>
        public const float CockEnd = CockOut1;
        /// <summary>表演总时长（秒）：超过后 Blend01 归零，交还步态层。</summary>
        public const float TotalDuration = 0.55f;

        /// <summary>采样当前应播放的投掷表演。</summary>
        /// <param name="elapsed">释放大招后已过秒数（<c>WeaponController.StormThrowElapsed</c>）。</param>
        public static StormThrowPerformance Sample(float elapsed)
        {
            float t = Mathf.Max(0f, elapsed);
            if (t >= TotalDuration)
                return StormThrowPerformance.None;

            StormThrowPerformance result = StormThrowPerformance.None;
            result.CockW = SlamPoseSolver.Window(t, CockIn0, CockIn1, CockOut0, CockOut1);
            result.WhipW = SlamPoseSolver.Window(t, WhipIn0, WhipIn1, WhipOut0, WhipOut1);
            result.FollowW = SlamPoseSolver.Window(t, FollowIn0, FollowIn1, FollowOut0, FollowOut1);

            // 包络：起手 0.05s 淡入，尾段 0.08s 渐出（与 0.35s 动作锁解耦，收势期间已恢复移动）
            float fadeIn = Mathf.Clamp01(t / 0.05f);
            float fadeOut = Mathf.Clamp01((TotalDuration - t) / 0.08f);
            result.Blend01 = Mathf.Min(fadeIn, fadeOut);

            return result;
        }
    }
}
