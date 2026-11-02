using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Core
{
    /// <summary>
    /// WebGL 运行时兼容修正：统一关闭 GPU instancing。
    /// WebGL2 的 shader model 只到 3.0，INSTANCING_ON 变体不可用；若材质仍勾选
    /// Enable GPU Instancing，部分驱动会跳过 draw（地面/角色整片消失）。这里在
    /// 场景加载后把所有已加载材质的 instancing 关掉，统一走默认变体。
    /// </summary>
    internal static class WebGLRuntimeCompat
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void DisableGpuInstancing()
        {
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat != null && mat.enableInstancing)
                {
                    mat.enableInstancing = false;
                }
            }
        }
#endif
    }
}