using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.UI
{
    public enum MapSchematicRole
    {
        Terrain,
        Cover,
        Landmark,
        Spawn
    }

    /// <summary>地图小预览的一枚世界坐标图元；只服务辨识，不参与玩法判定。</summary>
    public readonly struct MapSchematicBlock
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Width;
        public readonly float Depth;
        public readonly MapSchematicRole Role;

        public MapSchematicBlock(float x, float z, float width, float depth, MapSchematicRole role)
        {
            X = x;
            Z = z;
            Width = width;
            Depth = depth;
            Role = role;
        }
    }

    public sealed class MapDefinition
    {
        public readonly string Id;
        public readonly string ScenePath;
        public readonly string DisplayName;
        public readonly string EnglishName;
        public readonly string Descriptor;
        public readonly Color Accent;
        public readonly IReadOnlyList<MapSchematicBlock> Schematic;

        public MapDefinition(
            string id,
            string scenePath,
            string displayName,
            string englishName,
            string descriptor,
            Color accent,
            IReadOnlyList<MapSchematicBlock> schematic)
        {
            Id = id;
            ScenePath = scenePath;
            DisplayName = displayName;
            EnglishName = englishName;
            Descriptor = descriptor;
            Accent = accent;
            Schematic = schematic;
        }
    }

    /// <summary>
    /// 首页地图选择与场景加载的唯一入口。
    /// 选择写 PlayerPrefs；进对局按 Build Settings 索引加载场景，不按硬编码场景名。
    /// 本文件只描述 UI 所需的图元，玩法常量仍以 ArenaLayoutV2 为准。
    /// </summary>
    public static class MapCatalogService
    {
        public const string SelectedMapPrefsKey = "P1.Map.SelectedId";
        public const string PhaseAScenePath = "Assets/_Project/Scenes/PhaseA_Playbox.unity";
        public const string GullScenePath = "Assets/_Project/Scenes/GullHarbor.unity";

        private static readonly MapDefinition PhaseAMap = new MapDefinition(
            "phase_a",
            PhaseAScenePath,
            "货柜码头",
            "CONTAINER DOCKS",
            "中央高台 · 四向坡道 · 集装箱掩体",
            ComicStyle.Red,
            PhaseASchematic());

        private static readonly MapDefinition GullMap = new MapDefinition(
            "gull_harbor",
            GullScenePath,
            "海鸥港湾",
            "GULL HARBOR",
            "中央渡轮 · 浅湾水线 · 木栈道绕后",
            ComicStyle.Blue,
            GullSchematic());

        private static readonly MapDefinition[] AllMaps = { PhaseAMap, GullMap };

        private static bool _loading;
        private static AsyncOperation _loadOperation;
        private static float _loadStartRealtime;
        private const float LoadStaleSeconds = 8f;

        public static IReadOnlyList<MapDefinition> Maps => AllMaps;

        /// <summary>
        /// 全部地图场景路径（Build Index 0 的默认图在最前）。出包脚本必须把它整体塞进
        /// <c>BuildPlayerOptions.scenes</c>：只打主场景时，运行期第二张图永远拿不到 Build Index。
        /// </summary>
        public static string[] AllScenePaths()
        {
            var paths = new string[AllMaps.Length];
            for (int i = 0; i < AllMaps.Length; i++) paths[i] = AllMaps[i].ScenePath;
            return paths;
        }

        public static MapDefinition DefaultMap => PhaseAMap;

        /// <summary>读 Prefs；缺失或非法时回落 PhaseA，且不写盘（不改变老玩家首进体验）。</summary>
        public static string SelectedId
        {
            get
            {
                string saved = PlayerPrefs.GetString(SelectedMapPrefsKey, string.Empty);
                return MapForId(saved) != null ? saved : DefaultMap.Id;
            }
        }

        public static MapDefinition Selected => MapForId(SelectedId) ?? DefaultMap;

        /// <summary>首页牌面以「当前所在场景」为准；不在任何地图场景时回落已选存档。</summary>
        public static MapDefinition ActiveOrSelected
        {
            get
            {
                MapDefinition active = MapForActiveScene();
                return active ?? Selected;
            }
        }

        /// <summary>
        /// 当前激活场景对应的地图。玩家包里 <see cref="Scene.path"/> 可能为空字符串，
        /// 所以路径匹配不上时再按场景名兜底，否则「当前地图」会永远停在存档/默认值上。
        /// </summary>
        public static MapDefinition MapForActiveScene()
        {
            Scene active = SceneManager.GetActiveScene();
            MapDefinition byPath = MapForScene(active.path);
            if (byPath != null) return byPath;

            if (string.IsNullOrEmpty(active.name)) return null;
            for (int i = 0; i < AllMaps.Length; i++)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(AllMaps[i].ScenePath);
                if (string.Equals(fileName, active.name, System.StringComparison.Ordinal))
                {
                    return AllMaps[i];
                }
            }
            return null;
        }

        /// <summary>当前场景是否就是选中的那张图（路径或场景名任一匹配即可）。</summary>
        public static bool IsSelectedSceneActive()
        {
            MapDefinition active = MapForActiveScene();
            return active != null && active.Id == Selected.Id;
        }

        public static MapDefinition MapForId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < AllMaps.Length; i++)
            {
                if (AllMaps[i].Id == id) return AllMaps[i];
            }
            return null;
        }

        public static MapDefinition MapForScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;
            for (int i = 0; i < AllMaps.Length; i++)
            {
                if (AllMaps[i].ScenePath == scenePath) return AllMaps[i];
            }
            return null;
        }

        public static bool IsMapScene(string scenePath)
        {
            return MapForScene(scenePath) != null;
        }

        /// <summary>该地图的场景是否真的在本次安装包里：玩家包只带主场景时，切图会必然失败。</summary>
        public static bool CanLoadMap(MapDefinition map)
        {
            return map != null && ResolveBuildIndex(map) >= 0;
        }

        public static MapDefinition OtherThan(MapDefinition map)
        {
            for (int i = 0; i < AllMaps.Length; i++)
            {
                if (AllMaps[i] != map) return AllMaps[i];
            }
            return null;
        }

        /// <summary>只写 PlayerPrefs；真实切图必须紧接着调用 <see cref="LoadMap"/>。</summary>
        public static bool TrySelect(string id)
        {
            MapDefinition map = MapForId(id);
            if (map == null) return false;
            if (SelectedId == map.Id && PlayerPrefs.HasKey(SelectedMapPrefsKey)) return false;
            PlayerPrefs.SetString(SelectedMapPrefsKey, map.Id);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>按 Build Settings 索引加载；失败只报错，不让 UI 假装切换成功。</summary>
        public static bool LoadMap(MapDefinition map)
        {
            if (map == null)
            {
                Debug.LogError("[MapCatalog] LoadMap 收到空地图。");
                return false;
            }
            if (_loadOperation != null)
            {
                // 只要场景真的还在加载就拒绝重复请求：手机加载一张图可能几十帧，
                // 按帧号判过期会在加载途中反复重发 LoadSceneAsync(Single)，把加载自己打断。
                if (!_loadOperation.isDone && Time.realtimeSinceStartup - _loadStartRealtime < LoadStaleSeconds)
                {
                    return false;
                }
                // 超过 LoadStaleSeconds 仍未完成（被引擎吞掉/编辑器暂停）才视为过期请求，允许重发。
                Debug.LogWarning("[MapCatalog] 上一次切图请求未完成，重新发起：" + map.Id);
                _loadOperation = null;
                _loading = false;
            }
            int buildIndex = ResolveBuildIndex(map);
            if (buildIndex < 0)
            {
                Debug.LogError($"[MapCatalog] 场景未加入 Build Settings，无法加载：{map.ScenePath}");
                return false;
            }
            _loading = true;
            _loadStartRealtime = Time.realtimeSinceStartup;
            _loadOperation = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (_loadOperation == null)
            {
                _loading = false;
                Debug.LogError($"[MapCatalog] LoadSceneAsync 被拒绝：{map.ScenePath}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 解析场景的 Build Settings 索引。玩家包里 <see cref="SceneUtility.GetBuildIndexByScenePath"/>
        /// 可能回 -1（路径不可用），此时按场景名扫一遍 Build Settings 兜底，避免真机上切图永远失败。
        /// </summary>
        private static int ResolveBuildIndex(MapDefinition map)
        {
            int index = SceneUtility.GetBuildIndexByScenePath(map.ScenePath);
            if (index >= 0) return index;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(map.ScenePath);
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), fileName,
                        System.StringComparison.Ordinal)) continue;

                Debug.LogWarning($"[MapCatalog] 按路径找不到场景，已按场景名兜底：{map.ScenePath} -> index {i}");
                return i;
            }
            return -1;
        }

        public static bool IsLoading => _loading;

        public static void ResetRuntimeState()
        {
            _loading = false;
            _loadOperation = null;
            _loadStartRealtime = 0f;
        }

        /// <summary>测试专用：清掉选择存档，回到默认 PhaseA。</summary>
        public static void ResetForTests()
        {
            PlayerPrefs.DeleteKey(SelectedMapPrefsKey);
            PlayerPrefs.Save();
        }

        private static MapSchematicBlock[] PhaseASchematic()
        {
            return new[]
            {
                // 出生台（南北）
                new MapSchematicBlock(0f, -25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                new MapSchematicBlock(0f, 25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                // 中央高台 + 四向坡道
                new MapSchematicBlock(0f, 0f, 10f, 10f, MapSchematicRole.Terrain),
                new MapSchematicBlock(0f, -7.4f, 4.4f, 5f, MapSchematicRole.Terrain),
                new MapSchematicBlock(0f, 7.4f, 4.4f, 5f, MapSchematicRole.Terrain),
                new MapSchematicBlock(-7.4f, 0f, 5f, 4.4f, MapSchematicRole.Terrain),
                new MapSchematicBlock(7.4f, 0f, 5f, 4.4f, MapSchematicRole.Terrain),
                // 中路掩体墙
                new MapSchematicBlock(-6f, -9f, 1.8f, 13f, MapSchematicRole.Cover),
                new MapSchematicBlock(6f, -9f, 1.8f, 13f, MapSchematicRole.Cover),
                new MapSchematicBlock(-6f, 9f, 1.8f, 13f, MapSchematicRole.Cover),
                new MapSchematicBlock(6f, 9f, 1.8f, 13f, MapSchematicRole.Cover),
                // 集装箱组
                new MapSchematicBlock(-8.1f, -10f, 4f, 5.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, -10f, 4f, 5.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(-8.1f, 10f, 4f, 5.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, 10f, 4f, 5.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(-8.1f, -18f, 4f, 3.6f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, -18f, 4f, 3.6f, MapSchematicRole.Cover),
                new MapSchematicBlock(-8.1f, 18f, 4f, 3.6f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, 18f, 4f, 3.6f, MapSchematicRole.Cover),
                // 侧翼平台与侧翼箱体
                new MapSchematicBlock(-13.45f, 0f, 4.4f, 15f, MapSchematicRole.Terrain),
                new MapSchematicBlock(13.45f, 0f, 4.4f, 15f, MapSchematicRole.Terrain),
                new MapSchematicBlock(-16.75f, 0f, 3.2f, 9f, MapSchematicRole.Cover),
                new MapSchematicBlock(16.75f, 0f, 3.2f, 9f, MapSchematicRole.Cover),
                // 中场地标
                new MapSchematicBlock(0f, 0f, 2.4f, 2.4f, MapSchematicRole.Landmark),
            };
        }

        private static MapSchematicBlock[] GullSchematic()
        {
            return new[]
            {
                // 出生台（南北）
                new MapSchematicBlock(0f, -25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                new MapSchematicBlock(0f, 25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                // 中央渡轮（船体 + 客舱）
                new MapSchematicBlock(0f, 0f, 23.6f, 9.2f, MapSchematicRole.Landmark),
                new MapSchematicBlock(0f, 0f, 10.4f, 4.6f, MapSchematicRole.Cover),
                // 南北登船坡道
                new MapSchematicBlock(0f, -7.2f, 3.8f, 5.2f, MapSchematicRole.Terrain),
                new MapSchematicBlock(0f, 7.2f, 3.8f, 5.2f, MapSchematicRole.Terrain),
                // 浅湾水线（坡道左右两段）
                new MapSchematicBlock(-8.55f, -7.2f, 13.3f, 5.2f, MapSchematicRole.Terrain),
                new MapSchematicBlock(8.55f, -7.2f, 13.3f, 5.2f, MapSchematicRole.Terrain),
                new MapSchematicBlock(-8.55f, 7.2f, 13.3f, 5.2f, MapSchematicRole.Terrain),
                new MapSchematicBlock(8.55f, 7.2f, 13.3f, 5.2f, MapSchematicRole.Terrain),
                // 加油亭
                new MapSchematicBlock(0f, -15.4f, 2.8f, 2.8f, MapSchematicRole.Cover),
                new MapSchematicBlock(0f, 15.4f, 2.8f, 2.8f, MapSchematicRole.Cover),
                // 船坞拖船（每队左翼）
                new MapSchematicBlock(-15.6f, -14.2f, 5.4f, 6.6f, MapSchematicRole.Cover),
                new MapSchematicBlock(15.6f, 14.2f, 5.4f, 6.6f, MapSchematicRole.Cover),
                // 系泊游艇
                new MapSchematicBlock(-8.6f, -12.1f, 2.4f, 4.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.6f, 12.1f, 2.4f, 4.4f, MapSchematicRole.Cover),
                // 出生闸口与侧翼箱堆
                new MapSchematicBlock(-5f, -21.4f, 3f, 1.2f, MapSchematicRole.Cover),
                new MapSchematicBlock(5f, 21.4f, 3f, 1.2f, MapSchematicRole.Cover),
                new MapSchematicBlock(-18.8f, -11.6f, 2f, 2f, MapSchematicRole.Cover),
                new MapSchematicBlock(18.8f, 11.6f, 2f, 2f, MapSchematicRole.Cover),
            };
        }
    }
}
