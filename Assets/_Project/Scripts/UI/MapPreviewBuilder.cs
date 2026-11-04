using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 地图小预览：优先用 <c>Resources/MapPreview/&lt;mapId&gt;</c> 的实景缩略图
    /// （菜单 <c>P1/关卡与地图/8</c> 出图，3/4 正交沙盘视角），图缺失时回落到程序化示意图——
    /// 示意图按 40×56 玩法范围等比压进小矩形，用纯色块表达出生台、掩体、坡道与地标。
    /// 只做辨识，不参与任何玩法判定。
    /// </summary>
    public static class MapPreviewBuilder
    {
        private const float MapSizeX = 40f;
        private const float MapSizeZ = 56f;
        private const string PhotoFolder = "MapPreview";

        private static readonly Dictionary<string, Sprite> PhotoCache = new Dictionary<string, Sprite>();

        public static RectTransform BuildPreview(
            RectTransform parent,
            MapDefinition map,
            Vector2 size,
            UIThemeSO theme,
            string name = "MapPreview")
        {
            RectTransform root = CreateBlock(parent, name, ComicStyle.PaperAlt, 2f, ComicStyle.Ink);
            root.sizeDelta = size;

            Sprite photo = map != null ? LoadPhoto(map.Id) : null;
            if (photo != null)
            {
                BuildPhoto(root, photo);
                return root;
            }

            float pad = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.04f, 4f, 10f);
            float scale = Mathf.Min((size.x - pad * 2f) / MapSizeX, (size.y - pad * 2f) / MapSizeZ);
            float mapWidth = MapSizeX * scale;
            float mapHeight = MapSizeZ * scale;

            RectTransform boundary = CreateBlock(
                root,
                "Boundary",
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.02f),
                1.5f,
                ComicStyle.Ink);
            boundary.sizeDelta = new Vector2(mapWidth, mapHeight);
            boundary.anchoredPosition = Vector2.zero;

            if (map == null) return root;

            Color teamA = theme != null ? theme.teamAColor : ComicStyle.Yellow;
            Color teamB = theme != null ? theme.teamBColor : ComicStyle.Blue;

            DrawRole(root, map, MapSchematicRole.Terrain, ComicStyle.WithAlpha(ComicStyle.Ink, 0.10f), 0f, Color.clear, scale, teamA, teamB);
            DrawRole(root, map, MapSchematicRole.Cover, ComicStyle.InkSoft, 0f, Color.clear, scale, teamA, teamB);
            DrawRole(root, map, MapSchematicRole.Landmark, ComicStyle.WithAlpha(map.Accent, 0.92f), 0f, Color.clear, scale, teamA, teamB);
            DrawRole(root, map, MapSchematicRole.Spawn, ComicStyle.Paper, 1.5f, ComicStyle.Ink, scale, teamA, teamB, true);
            return root;
        }

        private static void DrawRole(
            RectTransform parent,
            MapDefinition map,
            MapSchematicRole role,
            Color fill,
            float border,
            Color borderColor,
            float scale,
            Color teamA,
            Color teamB,
            bool teamTint = false)
        {
            for (int i = 0; i < map.Schematic.Count; i++)
            {
                MapSchematicBlock block = map.Schematic[i];
                if (block.Role != role) continue;

                Color blockFill = fill;
                if (teamTint)
                {
                    blockFill = block.Z > 0f ? ComicStyle.WithAlpha(teamA, 0.9f) : ComicStyle.WithAlpha(teamB, 0.9f);
                }

                RectTransform rt = CreateBlock(parent, role.ToString(), blockFill, border, borderColor);
                rt.sizeDelta = new Vector2(block.Width * scale, block.Depth * scale);
                rt.anchoredPosition = new Vector2(block.X * scale, -block.Z * scale);
            }
        }

        /// <summary>实景缩略图（Resources）按需加载并缓存；缺图返回 null 走示意图回落。</summary>
        private static Sprite LoadPhoto(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            if (PhotoCache.TryGetValue(mapId, out Sprite cached) && cached != null) return cached;

            var texture = Resources.Load<Texture2D>(PhotoFolder + "/" + mapId);
            if (texture == null) return null;

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "MapPhoto_" + mapId;
            PhotoCache[mapId] = sprite;
            return sprite;
        }

        /// <summary>照片铺满预览框内圈（留 2px 让外框的墨色描边露出来）。</summary>
        private static void BuildPhoto(RectTransform root, Sprite photo)
        {
            var go = new GameObject("Photo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-2f, -2f);

            var image = go.GetComponent<Image>();
            image.sprite = photo;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;   // 出图比例与预览框相同（40:56），直接铺满不裁切
            image.raycastTarget = false;
        }

        private static RectTransform CreateBlock(
            RectTransform parent,
            string name,
            Color fill,
            float border,
            Color borderColor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            UIInkShapeGraphic graphic = go.GetComponent<UIInkShapeGraphic>();
            graphic.raycastTarget = false;
            graphic.HardShadow = false;
            graphic.Halftone = false;
            graphic.BorderWidth = border;
            graphic.BorderColor = borderColor;
            graphic.color = fill;
            return rt;
        }
    }
}
