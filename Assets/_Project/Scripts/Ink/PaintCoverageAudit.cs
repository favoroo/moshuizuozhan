using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace P1.Ink
{
    /// <summary>
    /// 涂地覆盖率审计（对齐 INKWAVE 的 "paintable 默认全部可涂" 保证机制）。
    ///
    /// 判定规则：竞技场里每一个"有非 Trigger 碰撞体、且有可视表面"的对象，必须至少满足
    /// 下列一条，否则子弹打上去不会留下任何墨迹 —— 玩家视角就是"这里喷不上油漆"：
    ///
    ///   1) 立面受墨：挂有 <see cref="InkWallSurface"/>（4 张可涂竖直立面）；
    ///   2) 顶面受墨：渲染器使用采样顶视墨迹图集的着色器
    ///      （P1/InkGround、P1/ObstacleDitherLit、P1/PropLit、P1/SpawnPad），
    ///      或已注册进 <see cref="InkSurfaceController.InkableSurfaceRenderers"/>；
    ///   3) 白名单：地板本体（顶面走图集，0.5m 侧沿整圈在界外）；
    ///   4) 界外远景：不在 40×56 计分范围内，玩家无法到达。
    ///
    /// 无渲染器的纯碰撞体（例如南边界外侧的隐形防越界 Blocker）不产生可视表面，直接跳过。
    /// </summary>
    public static class PaintCoverageAudit
    {
        public const string ArenaRootName = "Arena_40x56";
        public const string DecorRootName = "Arena_Decor";

        /// <summary>顶面采样墨迹图集的着色器（含 ApplyTopInkOverlay 的那一族）。</summary>
        private static readonly HashSet<string> TopInkShaders = new HashSet<string>
        {
            "P1/InkGround",
            "P1/ObstacleDitherLit",
            "P1/PropLit",
            "P1/SpawnPad",
        };

        /// <summary>不需要（或不该）要求墨面的对象名单。</summary>
        private static readonly HashSet<string> WhitelistedNames = new HashSet<string>
        {
            "Ground_Main_40x56",
            "Ground_Main_40x40",
        };

        /// <summary>对当前场景里约定名的竞技场根节点做审计。</summary>
        public static int RunScene(List<string> violations, List<string> warnings)
        {
            var roots = new List<Transform>(2);
            AddRoot(roots, ArenaRootName);
            AddRoot(roots, DecorRootName);
            return Run(roots, null, violations, warnings);
        }

        /// <summary>
        /// 对给定根节点做审计（测试可直接传入刚构建出来的竞技场根节点，
        /// 避免 GameObject.Find 命中场景里另一个同名竞技场）。
        /// 返回被检查的"有可视表面的碰撞体"数量。
        /// </summary>
        public static int Run(
            IList<Transform> roots,
            InkSurfaceController surface,
            List<string> violations,
            List<string> warnings)
        {
            violations?.Clear();
            warnings?.Clear();
            if (roots == null || roots.Count == 0)
                return 0;

            if (surface == null)
            {
                surface = Object.FindAnyObjectByType<InkSurfaceController>();
            }

            var registered = new HashSet<Renderer>();
            if (surface != null)
            {
                IReadOnlyList<Renderer> list = surface.InkableSurfaceRenderers;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] != null) registered.Add(list[i]);
                    }
                }
            }

            int checkedCount = 0;
            for (int r = 0; r < roots.Count; r++)
            {
                if (roots[r] == null)
                    continue;

                Collider[] colliders = roots[r].GetComponentsInChildren<Collider>(false);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider col = colliders[i];
                    if (col == null || col.isTrigger)
                        continue;

                    GameObject go = col.gameObject;
                    if (WhitelistedNames.Contains(go.name))
                        continue;

                    Renderer ownRenderer = go.GetComponent<Renderer>();
                    // 纯拦截体：没有渲染器也没有墨面 —— 不产生可视表面，跳过
                    if (ownRenderer == null && go.GetComponent<InkWallSurface>() == null)
                        continue;

                    checkedCount++;

                    // 1) 立面受墨
                    if (HasSideInk(go))
                        continue;

                    // 2) 顶面受墨
                    Renderer topRenderer = FindTopInkRenderer(go, registered);
                    if (topRenderer != null)
                    {
                        if (!registered.Contains(topRenderer))
                        {
                            warnings?.Add($"{DescribePath(go)}: 顶面着色器 {topRenderer.sharedMaterial.shader.name} 可受墨，但渲染器未注册进 inkableSurfaceRenderers");
                        }
                        continue;
                    }

                    // 3) 界外远景：不在 40×56 计分范围内，玩家无法到达
                    if (IsOutsidePlayfield(col.bounds))
                        continue;

                    violations?.Add($"{DescribePath(go)}: 既无顶面墨迹着色器，也没有 InkWallSurface");
                }
            }

            return checkedCount;
        }

        private static void AddRoot(List<Transform> roots, string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
            {
                roots.Add(go.transform);
            }
        }

        private static bool HasSideInk(GameObject go)
        {
            return go.GetComponent<InkWallSurface>() != null || go.GetComponentInParent<InkWallSurface>() != null;
        }

        /// <summary>沿自身与祖先链找第一个使用"顶面受墨着色器"的渲染器。</summary>
        private static Renderer FindTopInkRenderer(GameObject go, HashSet<Renderer> registered)
        {
            Transform t = go.transform;
            while (t != null)
            {
                Renderer r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    if (registered.Contains(r))
                        return r;

                    Material mat = r.sharedMaterial;
                    if (mat != null && mat.shader != null && TopInkShaders.Contains(mat.shader.name))
                        return r;
                }
                t = t.parent;
            }
            return null;
        }

        private static bool IsOutsidePlayfield(Bounds bounds)
        {
            const float halfX = 20.5f; // ArenaLayoutV2.BoundaryX
            const float halfZ = 28.5f; // ArenaLayoutV2.BoundaryZ
            return bounds.min.x > halfX + 0.01f || bounds.max.x < -halfX - 0.01f
                || bounds.min.z > halfZ + 0.01f || bounds.max.z < -halfZ - 0.01f;
        }

        /// <summary>输出 "Arena_40x56/Cover_Wall" 形式的分层路径，便于定位。</summary>
        public static string DescribePath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            Transform t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}