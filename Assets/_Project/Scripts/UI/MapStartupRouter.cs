using P1.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.UI
{
    /// <summary>
    /// 多地图启动恢复：首次进入时把玩家记住的地图场景载入；
    /// 并在地图切换时复位静态环境样式，避免上一张图的天空/雾残留。
    /// 只在地图场景内生效，测试/示例场景不受影响。
    /// </summary>
    public static class MapStartupRouter
    {
        private static bool _hooked;
        private static bool _autoApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload 关闭时静态事件订阅会跨 Play 会话残留：必须先退订再复位，
            // 否则每次进入 Play 都会多挂一份 OnSceneLoaded（样式反复复位、日志翻倍）。
            if (_hooked) SceneManager.sceneLoaded -= OnSceneLoaded;
            _hooked = false;
            _autoApplied = false;
            MapCatalogService.ResetRuntimeState();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying) return;
            EnsureHooked();
            ApplyPersistedSelection();
        }

        private static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 构建包固定从 Build Index 0 起步；若玩家记住的是另一张图，在首帧内切过去。
        /// 只在当前场景本身是地图场景时执行，避免把开发/测试场景拽走。
        /// </summary>
        private static void ApplyPersistedSelection()
        {
            if (_autoApplied) return;
            _autoApplied = true;

            Scene active = SceneManager.GetActiveScene();
            if (!MapCatalogService.IsMapScene(active.path)) return;

            MapDefinition selected = MapCatalogService.Selected;
            if (active.path == selected.ScenePath) return;

            MapCatalogService.LoadMap(selected);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            MapCatalogService.ResetRuntimeState();
            if (scene.path == MapCatalogService.PhaseAScenePath)
            {
                RenderEnvGlobals.ResetStyleToDefault();
            }
        }
    }
}
