using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 一次性给场景碰撞体分层：有 <see cref="P1.Player.CombatantHealth"/> 父级 → Combatant，
    /// 其余带 Collider 的物体 → World。只为物理查询掩码服务，不改变碰撞矩阵与渲染 cullingMask。
    ///
    /// 已经烘焙进场景的物体默认都在 Default 层；不改场景 YAML，靠启动时扫一遍（~1500 个 Collider，
    /// 仅 Awake 一次）。层未建立（旧工程 / 单元测试）时直接跳过，所有 mask 回退全开。
    /// </summary>
    public static class PhysicsLayerBootstrap
    {
        private static bool _done;

        /// <summary>幂等：比赛启动时调用一次。</summary>
        public static void EnsureSceneLayers()
        {
            if (_done)
                return;

            if (!GameLayers.LayersConfigured)
                return; // 层没建立：留给 GameLayers 的 ~0 回退，不改变任何行为

            _done = true;

            int worldLayer = GameLayers.WorldLayer;
            int combatantLayer = GameLayers.CombatantLayer;

            Collider[] colliders = Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                // 已分层（例如 CombatantHealth.Awake 自己设过、或后续场景生成器已标记）不重复处理
                int current = collider.gameObject.layer;
                if (current == worldLayer || current == combatantLayer)
                    continue;

                bool isCombatant = collider.GetComponentInParent<P1.Player.CombatantHealth>() != null;
                collider.gameObject.layer = isCombatant ? combatantLayer : worldLayer;
            }
        }

        /// <summary>测试用：允许在下一个场景重新分层。</summary>
        public static void ResetForTests()
        {
            _done = false;
            GameLayers.InvalidateCache();
        }
    }
}