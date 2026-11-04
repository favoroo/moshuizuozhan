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
        public const string KelpScenePath = "Assets/_Project/Scenes/KelpTerminal.unity";

        private static readonly MapDefinition PhaseAMap = new MapDefinition(
            "phase_a",
            PhaseAScenePath,
            "货柜码头",
            "CONTAINER DOCKS",
            "中央高台 · 四向坡道 · 集装箱掩体",
            ComicStyle.Red,
            PhaseASchematic());

        private static readonly MapDefinition KelpMap = new MapDefinition(
            "kelp_terminal",
            KelpScenePath,
            "潮汐货站",
            "KELP TERMINAL",
            "中央长货台 · 东西坡道 · 信标柱地标",
            ComicStyle.Blue,
            KelpSchematic());

        private static readonly MapDefinition[] AllMaps = { PhaseAMap, KelpMap };

        private static bool _loading;

        public static IReadOnlyList<MapDefinition> Maps => AllMaps;

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
                MapDefinition active = MapForScene(SceneManager.GetActiveScene().path);
                return active ?? Selected;
            }
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

        public static MapDefinition OtherThan(MapDefinition map)
        {
            for (int i = 0; i < AllMaps.Length; i++)
            {
                if (AllMaps[i] != map) return AllMaps[i];
            }
            return null;
        }

        public static bool IsSelectedSceneActive()
        {
            return SceneManager.GetActiveScene().path == Selected.ScenePath;
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
            if (_loading)
            {
                return false;
            }
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(map.ScenePath);
            if (buildIndex < 0)
            {
                Debug.LogError($"[MapCatalog] 场景未加入 Build Settings，无法加载：{map.ScenePath}");
                return false;
            }
            _loading = true;
            SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
            return true;
        }

        public static void ResetRuntimeState()
        {
            _loading = false;
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

        private static MapSchematicBlock[] KelpSchematic()
        {
            return new[]
            {
                // 出生台（南北）
                new MapSchematicBlock(0f, -25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                new MapSchematicBlock(0f, 25.2f, 14f, 6.4f, MapSchematicRole.Spawn),
                // 中央长货台 + 东西坡道
                new MapSchematicBlock(0f, 0f, 10f, 6f, MapSchematicRole.Landmark),
                new MapSchematicBlock(-7.4f, 0f, 5f, 4.4f, MapSchematicRole.Terrain),
                new MapSchematicBlock(7.4f, 0f, 5f, 4.4f, MapSchematicRole.Terrain),
                // 中央信标
                new MapSchematicBlock(1.2f, 0f, 1.6f, 1.6f, MapSchematicRole.Cover),
                // 中部低位掩体
                new MapSchematicBlock(-8.1f, -5.5f, 3.2f, 2.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, -5.5f, 3.2f, 2.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(-8.1f, 5.5f, 3.2f, 2.4f, MapSchematicRole.Cover),
                new MapSchematicBlock(8.1f, 5.5f, 3.2f, 2.4f, MapSchematicRole.Cover),
                // 侧翼长条箱体
                new MapSchematicBlock(-16.75f, 0f, 3.2f, 9f, MapSchematicRole.Cover),
                new MapSchematicBlock(16.75f, 0f, 3.2f, 9f, MapSchematicRole.Cover),
                // 侧翼花箱 / 系缆柱
                new MapSchematicBlock(-10.4f, -12f, 1.4f, 5f, MapSchematicRole.Terrain),
                new MapSchematicBlock(10.4f, -12f, 1.4f, 5f, MapSchematicRole.Terrain),
                new MapSchematicBlock(-10.4f, 12f, 1.4f, 5f, MapSchematicRole.Terrain),
                new MapSchematicBlock(10.4f, 12f, 1.4f, 5f, MapSchematicRole.Terrain),
            };
        }
    }
}
