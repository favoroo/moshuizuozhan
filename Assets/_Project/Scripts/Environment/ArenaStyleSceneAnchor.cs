using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 场景级画风绑定：把本场景使用的地图风格资产推给 <see cref="P1.Core.RenderEnvGlobals"/>。
    /// 没有这个组件时，运行时只按固定路径加载 <c>ArenaStyle.asset</c>，
    /// 多地图场景会全部沿用同一套天空/反射全局，地图专属画风档位不会生效。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class ArenaStyleSceneAnchor : MonoBehaviour
    {
        [SerializeField] private ArenaStyleSO style;

        public ArenaStyleSO Style => style;

        private void Awake()
        {
            P1.Core.RenderEnvGlobals.SetStyle(style);
        }

        public void Configure(ArenaStyleSO target)
        {
            style = target;
        }
    }
}
