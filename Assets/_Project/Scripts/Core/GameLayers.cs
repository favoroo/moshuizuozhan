using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 物理层常量与查询掩码（性能优化）：项目原先所有射线/球扫都用 <c>~0</c>，
    /// 79 面可涂墙 + 全部道具 + 8 名角色的碰撞体每帧都进入 broadphase。
    /// 这里统一提供 <c>P1_World</c>（静态场景/掩体/道具）与 <c>P1_Combatant</c>（角色胶囊）两层；
    /// 层未建立时自动回退 <c>~0</c>，旧场景 / 单元测试行为不变。
    /// </summary>
    public static class GameLayers
    {
        public const string WorldLayerName = "P1_World";
        public const string CombatantLayerName = "P1_Combatant";

        // TagManager 的前 8 个槽位是 Unity 内建层，自定义层从 8 开始。
        public const int WorldLayerIndex = 8;
        public const int CombatantLayerIndex = 9;

        private static bool _resolved;
        private static int _worldLayer = -1;
        private static int _combatantLayer = -1;

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            _worldLayer = LayerMask.NameToLayer(WorldLayerName);
            _combatantLayer = LayerMask.NameToLayer(CombatantLayerName);
        }

        /// <summary>场景层索引；未建立时返回 -1。</summary>
        public static int WorldLayer
        {
            get { Resolve(); return _worldLayer; }
        }

        /// <summary>角色层索引；未建立时返回 -1。</summary>
        public static int CombatantLayer
        {
            get { Resolve(); return _combatantLayer; }
        }

        /// <summary>两层是否都已建立（未建立时所有 mask 回退全开）。</summary>
        public static bool LayersConfigured
        {
            get { Resolve(); return _worldLayer >= 0 && _combatantLayer >= 0; }
        }

        /// <summary>
        /// 只命中静态场景/掩体/道具 + 任何未分类层（Default 等）。
        /// 只排除角色层：这样运行时新建的碰撞体（测试地面、临时道具）仍然会被查询命中，
        /// 不会因为「没赶上启动分类」而静默失效。
        /// </summary>
        public static int WorldMask
        {
            get
            {
                Resolve();
                return _combatantLayer >= 0 ? ~0 & ~(1 << _combatantLayer) : ~0;
            }
        }

        /// <summary>只命中角色胶囊。</summary>
        public static int CombatantMask
        {
            get { Resolve(); return _combatantLayer >= 0 ? 1 << _combatantLayer : ~0; }
        }

        /// <summary>命中场景 + 角色（弹丸命中、视线遮挡、瞄准）。保持全层语义，避免漏掉未分类碰撞体。</summary>
        public static int QueryMask => ~0;

        /// <summary>测试用：清空惰性解析缓存（层被运行时改动后重新解析）。</summary>
        public static void InvalidateCache()
        {
            _resolved = false;
        }
    }
}