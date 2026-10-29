using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// <see cref="HitstopService"/> 的真实时间驱动器：把 <see cref="Time.unscaledDeltaTime"/>
    /// 喂给时间线，保证命中定格在被缩放的游戏时间里也能按时恢复。
    /// </summary>
    internal sealed class HitstopRunner : MonoBehaviour
    {
        private void Update()
        {
            HitstopService.Tick(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            HitstopService.Cancel();
        }
    }
}
