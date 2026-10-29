using P1.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.UI.Menus
{
    /// <summary>
    /// 暂停与设置菜单的运行时自装配启动器（零入侵场景，不修改场景 YAML）：
    /// 在检测到场景中存在 <see cref="MatchManager"/> 时，自动创建独立的暂停与设置 UI Canvas。
    /// </summary>
    public static class MenuBootstrap
    {
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (!Application.isPlaying)
                return;

            EnsureHooked();
            CheckAndMount();
        }

        public static void EnsureHooked()
        {
            if (_hooked)
                return;

            _hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;

            var go = new GameObject("~MenuBootstrapRunner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MenuBootstrapRunner>();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CheckAndMount();
        }

        public static void CheckAndMount()
        {
            if (!Application.isPlaying)
                return;

            if (MatchManager.Instance != null && PauseMenuOverlay.Instance == null)
            {
                PauseMenuOverlay.CreateInstance();
            }
        }

        private class MenuBootstrapRunner : MonoBehaviour
        {
            private float _checkTimer;

            private void Update()
            {
                // 每隔 0.5s 检查一次，防止 MatchManager 在场景加载后异步实例化时漏挂
                _checkTimer += Time.unscaledDeltaTime;
                if (_checkTimer >= 0.5f)
                {
                    _checkTimer = 0f;
                    CheckAndMount();
                }
            }
        }
    }
}
