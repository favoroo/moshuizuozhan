using System.Collections;
using System.Reflection;
using NUnit.Framework;
using P1.Core;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// PlayMode 用例公共计时工具（只压缩用例自身的真实等待，不改变被测契约）：
    /// 1. <see cref="ShortenMatchTimers"/>：把场景 MatchManager 的 3s 倒计时与 1.6s 终局定格改短，
    ///    场景重载后自动还原，不影响其它用例；
    /// 2. <see cref="WaitForPhase"/>：按阶段轮询代替固定秒数，消除低帧率下的掐点抖动；
    /// 3. <see cref="WaitGameSecondsAccelerated"/>：临时抬高 timeScale 等待"游戏内秒"，
    ///    用于复活、回收等纯计时流程（被观察逻辑全部走 Time.deltaTime），结束必然恢复原倍率。
    /// </summary>
    internal static class PlayModeTestKit
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// 把当前场景 MatchManager 的倒计时与终局定格调短。
        /// 反射直写序列化字段（与 MatchFlowTests 里改 _remaining 的做法一致），不修改共享资产。
        /// </summary>
        public static void ShortenMatchTimers(float countdownSeconds = 0.40f, float finishFreezeSeconds = 0.35f)
        {
            MatchManager match = MatchManager.Instance;
            if (match == null)
            {
                Assert.Fail("场景里没有 MatchManager，无法压缩比赛计时");
                return;
            }

            System.Type type = typeof(MatchManager);
            FieldInfo countdown = type.GetField("countdownSeconds", PrivateInstance);
            FieldInfo freeze = type.GetField("finishFreezeSeconds", PrivateInstance);
            Assert.IsNotNull(countdown, "MatchManager.countdownSeconds 字段缺失（压缩倒计时用）");
            Assert.IsNotNull(freeze, "MatchManager.finishFreezeSeconds 字段缺失（压缩终局定格用）");

            countdown.SetValue(match, countdownSeconds);
            freeze.SetValue(match, finishFreezeSeconds);
        }

        /// <summary>轮询等待比赛进入指定阶段；超时用游戏内秒计（受 timeScale 影响）。</summary>
        public static IEnumerator WaitForPhase(MatchManager match, MatchManager.Phase phase, float timeoutGameSeconds = 10f)
        {
            float deadline = Time.time + timeoutGameSeconds;
            while (match != null && match.CurrentPhase != phase && Time.time < deadline)
            {
                yield return null;
            }

            MatchManager.Phase actual = match != null ? match.CurrentPhase : (MatchManager.Phase)(-1);
            Assert.AreEqual(phase, actual, $"等待比赛阶段 {phase} 超时（当前 {actual}）");
        }

        /// <summary>
        /// 以 scale 倍时间倍率等待 gameSeconds 游戏内秒。
        /// try/finally 保证即使等待期间抛异常也会恢复 timeScale，避免污染后续用例。
        /// </summary>
        public static IEnumerator WaitGameSecondsAccelerated(float gameSeconds, float scale = 8f)
        {
            float previous = Time.timeScale;
            Time.timeScale = Mathf.Max(1f, scale);
            try
            {
                yield return new WaitForSeconds(gameSeconds);
            }
            finally
            {
                Time.timeScale = previous;
            }
        }
    }
}
