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
        private static bool _autoStartPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Domain Reload 关闭时静态事件订阅会跨 Play 会话残留：必须先退订再复位，
            // 否则每次进入 Play 都会多挂一份 OnSceneLoaded（样式反复复位、日志翻倍）。
            if (_hooked) SceneManager.sceneLoaded -= OnSceneLoaded;
            _hooked = false;
            _autoApplied = false;
            _autoStartPending = false;
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
        /// 切图交给 <see cref="PersistedSelectionDriver"/> 逐帧重试：在 AfterSceneLoad 阶段
        /// 直接 LoadScene 可能被引擎吞掉（真机表现为「存档里的图永远不生效」）。
        /// </summary>
        private static void ApplyPersistedSelection()
        {
            if (_autoApplied) return;
            _autoApplied = true;

            MapDefinition active = MapCatalogService.MapForActiveScene();
            if (active == null) return;                                  // 不在任何地图场景：不管
            if (active.Id == MapCatalogService.Selected.Id) return;      // 已经在记住的图里

            var go = new GameObject("~MapStartupDriver") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<PersistedSelectionDriver>();
        }

        /// <summary>
        /// 「开始游戏」发现选中的图与当前场景不一致时调用：目标场景加载完成后自动开赛，
        /// 玩家不需要在场地重载后再按一次开始。加载失败由调用方 <see cref="CancelAutoStart"/>。
        /// </summary>
        public static void RequestAutoStart()
        {
            _autoStartPending = true;
        }

        public static void CancelAutoStart()
        {
            _autoStartPending = false;
        }

        /// <summary>
        /// 启动补切图：按“真实时间”每 0.5s 重试一次，最多 10s，切成功或放弃后自毁。
        /// 不能用帧数计数：手机加载一张场景可能几十帧，帧数预算会在加载完成前就耗光。
        /// LoadMap 在加载途中会直接拒绝重复请求，所以这里的轮询不会打断正在进行的加载。
        /// </summary>
        private sealed class PersistedSelectionDriver : MonoBehaviour
        {
            private const float RetryInterval = 0.5f;
            private const float GiveUpAfterSeconds = 10f;
            private float _elapsed;
            private float _nextAttempt;

            private void Awake()
            {
                DontDestroyOnLoad(gameObject);
            }

            private void Update()
            {
                _elapsed += Time.unscaledDeltaTime;
                if (MapCatalogService.IsSelectedSceneActive() || _elapsed >= GiveUpAfterSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
                if (_elapsed < _nextAttempt) return;

                _nextAttempt = _elapsed + RetryInterval;
                MapCatalogService.LoadMap(MapCatalogService.Selected);
            }
        }

        /// <summary>
        /// 切图后自动开赛。等两帧让新场景里的 MatchManager / MatchFlowUI 完成 Awake/Start，
        /// 否则开赛事件（PhaseChanged）会被还没订阅的 UI 漏掉，画面停在首页。
        /// </summary>
        private sealed class AutoStartDriver : MonoBehaviour
        {
            private int _frames;

            private void Awake()
            {
                DontDestroyOnLoad(gameObject);
            }

            private void Update()
            {
                if (_frames++ < 2) return;

                MatchManager match = MatchManager.Instance;
                if (match == null || match.CurrentPhase != MatchManager.Phase.Ready)
                {
                    if (_frames > 300) Destroy(gameObject);   // ~5 秒还没就绪就放弃，别留幽灵驱动
                    return;
                }

                Destroy(gameObject);
                match.StartMatch();
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            MapCatalogService.ResetRuntimeState();
            MapDefinition loaded = MapCatalogService.MapForScene(scene.path);
            if (loaded == null && !string.IsNullOrEmpty(scene.name))
            {
                // 玩家包里 scene.path 可能为空，退回场景名判断。
                loaded = MapCatalogService.MapForActiveScene();
            }
            if (loaded != null && loaded.Id == MapCatalogService.DefaultMap.Id)
            {
                RenderEnvGlobals.ResetStyleToDefault();
            }

            if (!_autoStartPending) return;

            // 只有「加载完成的就是选中的那张图」才自动开赛；任何其它场景载入都视为切图失败/外部载入。
            _autoStartPending = false;
            if (loaded == null || loaded.Id != MapCatalogService.Selected.Id) return;

            var go = new GameObject("~MapAutoStartDriver") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<AutoStartDriver>();
        }
    }
}
