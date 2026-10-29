using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 全局击杀微停顿（Hitstop）时间线（纯逻辑，可 EditMode 直接断言）：
    /// 先按 <c>timeScale</c> 冻结 <c>holdSeconds</c>，随后用临界阻尼弹簧（ζ=1）在
    /// <c>recoverSeconds</c> 内平滑回到 1.0，杜绝瞬间跳回造成的视觉撕裂。
    /// </summary>
    public sealed class HitstopTimeline
    {
        private enum Phase
        {
            Idle = 0,
            Freeze = 1,
            Recover = 2
        }

        private Phase _phase;
        private float _holdRemaining;
        private float _freezeScale = 1f;
        private float _omega = 60f;
        private float _recoverElapsed;
        private float _offset0;
        private float _velocity0;

        /// <summary>是否处于定格/恢复过程中。</summary>
        public bool IsActive => _phase != Phase.Idle;

        /// <summary>当前应施加的时间尺度（Idle 时恒为 1）。</summary>
        public float CurrentScale { get; private set; } = 1f;

        /// <summary>定格阶段剩余的真实秒数（恢复阶段为 0）。</summary>
        public float FreezeRemaining => _phase == Phase.Freeze ? Mathf.Max(0f, _holdRemaining) : 0f;

        /// <summary>
        /// 请求一次微停顿。重复调用（例如一次爆炸同时击杀多人）取更长的定格与更低的冻结尺度，不会叠加成多段冻结。
        /// </summary>
        public void Pulse(float holdSeconds, float timeScale, float recoverSeconds)
        {
            if (holdSeconds <= 0f || timeScale <= 0f || timeScale >= 1f)
                return;

            bool alreadyFreezing = _phase == Phase.Freeze;
            _phase = Phase.Freeze;
            _holdRemaining = Mathf.Max(alreadyFreezing ? _holdRemaining : 0f, holdSeconds);
            _freezeScale = alreadyFreezing ? Mathf.Min(_freezeScale, timeScale) : timeScale;
            CurrentScale = _freezeScale;
            _recoverElapsed = 0f;
            _offset0 = _freezeScale - 1f;
            _velocity0 = 0f;
            // 临界阻尼 6 个时间常数 ≈ 99.8% 收敛：ω = 6 / recover，
            // 因此 recoverSeconds 结束时玩家看到的已经是 1.0（无跳变）。
            _omega = 6f / Mathf.Max(0.02f, recoverSeconds);
        }

        /// <summary>
        /// 推进真实时间；返回当前时间尺度。
        /// 恢复段使用临界阻尼弹簧的解析解 x(t) = (x0 + (v0 + ωx0)·t)·e^(-ωt)，
        /// 任意大 dt（掉帧、切后台）都不会数值发散，也无需拆子步。
        /// </summary>
        public float Advance(float unscaledDeltaTime)
        {
            if (_phase == Phase.Idle || unscaledDeltaTime <= 0f)
                return CurrentScale;

            float remaining = unscaledDeltaTime;

            if (_phase == Phase.Freeze)
            {
                float consumed = Mathf.Min(_holdRemaining, remaining);
                _holdRemaining -= consumed;
                remaining -= consumed;

                if (_holdRemaining > 0f)
                    return CurrentScale;

                _phase = Phase.Recover;
                _recoverElapsed = 0f;
                _offset0 = _freezeScale - 1f;
                _velocity0 = 0f;
            }

            if (remaining <= 0f)
                return CurrentScale;

            _recoverElapsed += remaining;
            float t = _recoverElapsed;
            float b = _velocity0 + _omega * _offset0;
            float decay = Mathf.Exp(-_omega * t);
            float offset = (_offset0 + b * t) * decay;
            float velocity = (b - _omega * (_offset0 + b * t)) * decay;

            CurrentScale = Mathf.Clamp(1f + offset, 0.05f, 1f);

            if (Mathf.Abs(offset) < 0.004f && Mathf.Abs(velocity) < 0.25f)
                Cancel();

            return CurrentScale;
        }

        /// <summary>立即结束并把时间尺度归位。</summary>
        public void Cancel()
        {
            _phase = Phase.Idle;
            _holdRemaining = 0f;
            _recoverElapsed = 0f;
            _offset0 = 0f;
            _velocity0 = 0f;
            CurrentScale = 1f;
        }
    }

    /// <summary>
    /// 全局时间微缩服务：击杀敌方的瞬间制造 2~3 帧的入肉定格，再用弹簧平滑恢复。
    /// 由隐藏的 <see cref="HitstopRunner"/> 在不受 <see cref="Time.timeScale"/> 影响的真实时间里推进，
    /// 因此即使缩到 0.05 也必定按时恢复（严禁用 WaitForSeconds 这类被缩放的时间源）。
    /// </summary>
    public static class HitstopService
    {
        public const float DefaultHoldSeconds = 0.05f;
        public const float DefaultTimeScale = 0.2f;
        public const float DefaultRecoverSeconds = 0.1f;

        private static readonly HitstopTimeline Timeline = new HitstopTimeline();
        private static HitstopRunner _runner;

        /// <summary>是否处于暂停状态。暂停期间跳过对 Time.timeScale 的赋值，防止覆盖暂停时的 0 尺度。</summary>
        public static bool IsPaused { get; set; }

        /// <summary>是否正在定格/恢复。调试与测试可读。</summary>
        public static bool IsActive => Timeline.IsActive;

        /// <summary>当前时间尺度（未激活时 1.0）。</summary>
        public static float CurrentScale => Timeline.CurrentScale;

        /// <summary>内部时间线（EditMode 纯逻辑测试用）。</summary>
        public static HitstopTimeline TimelineForTests => Timeline;

        /// <summary>触发一次击杀微停顿。所有参数缺省值即 Inkwave 手感基准：0.05s @ 0.2x，0.1s 弹簧恢复。</summary>
        public static void Pulse(
            float holdSeconds = DefaultHoldSeconds,
            float timeScale = DefaultTimeScale,
            float recoverSeconds = DefaultRecoverSeconds)
        {
            if (holdSeconds <= 0f || timeScale <= 0f || timeScale >= 1f)
                return;

            Timeline.Pulse(holdSeconds, timeScale, recoverSeconds);
            EnsureRunner();
            Apply();
        }

        /// <summary>立即取消并恢复时间尺度（比赛重开 / 退出 Play 时调用）。</summary>
        public static void Cancel()
        {
            Timeline.Cancel();
            Apply();
        }

        internal static void Tick(float unscaledDeltaTime)
        {
            if (!Timeline.IsActive)
                return;

            Timeline.Advance(unscaledDeltaTime);
            Apply();
        }

        private static void Apply()
        {
            if (Application.isPlaying && !IsPaused)
                Time.timeScale = Timeline.CurrentScale;
        }

        private static void EnsureRunner()
        {
            if (_runner != null || !Application.isPlaying)
                return;

            var go = new GameObject("~HitstopRunner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<HitstopRunner>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            IsPaused = false;
            Cancel();
            EnsureRunner();
        }
    }
}
