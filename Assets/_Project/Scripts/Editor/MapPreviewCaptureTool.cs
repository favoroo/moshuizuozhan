using System.IO;
using P1.Ink;
using P1.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P1.Editor
{
    /// <summary>
    /// 地图选择卡「实景缩略图」出图工具。
    /// 逐张打开地图场景，用一台 3/4 斜俯视的相机把整块场地渲成 40×56 比例（与卡片预览框同比例）的 PNG，
    /// 写进 <c>Resources/MapPreview/&lt;mapId&gt;.png</c>，运行时由 <see cref="MapPreviewBuilder"/> 直接读。
    /// 视角是「立体沙盘」：能看见箱体侧面与坡道斜面，而不是俯视平面图。
    /// 本工具**只读场景**：不保存、不重建；当前场景有未保存改动时直接拒绝执行，避免冲掉别人的编辑。
    /// </summary>
    public static class MapPreviewCaptureTool
    {
        private const string OutputFolder = "Assets/_Project/Resources/MapPreview";
        private const int OutputWidth = 960;
        private const int OutputHeight = 1344;   // 40:56 = 960:1344，和卡片预览框一致

        // 立体沙盘视角：俯角 50° 仍看得见集装箱侧面、坡道斜面与信号塔高度，Y 轴再斜 6° 打破对称。
        // 竖幅画幅下俯角越大场地占得越满：45°/10° 时场地只占约 57% 画高，50°/6° 约 72%。
        private const float PitchDegrees = 50f;
        private const float YawDegrees = 6f;
        private const float FrameMargin = 0.03f;

        [MenuItem("P1/关卡与地图/8. 生成地图实景缩略图（地图选择卡）", false, 8)]
        public static void CaptureAllMaps()
        {
            Scene original = EditorSceneManager.GetActiveScene();
            if (original.isDirty)
            {
                Debug.LogError($"[MapPreview] 当前场景「{original.name}」有未保存改动，先保存再生成缩略图。");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            for (int i = 0; i < MapCatalogService.Maps.Count; i++)
            {
                MapDefinition map = MapCatalogService.Maps[i];
                if (!CaptureMap(map))
                {
                    Debug.LogError($"[MapPreview] {map.Id} 出图失败，已保留原有缩略图。");
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (!string.IsNullOrEmpty(original.path))
            {
                EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
            }
        }

        private static bool CaptureMap(MapDefinition map)
        {
            Scene active = EditorSceneManager.GetActiveScene();
            if (!string.Equals(active.path, map.ScenePath, System.StringComparison.Ordinal))
            {
                if (!File.Exists(map.ScenePath))
                {
                    Debug.LogError($"[MapPreview] 场景不存在：{map.ScenePath}");
                    return false;
                }
                EditorSceneManager.OpenScene(map.ScenePath, OpenSceneMode.Single);
            }

            SetCharactersVisible(false);
            // 场景存的是「整片己方墨」的涂地纹理解析结果（编辑态没跑过 ResetSurface），
            // 直接拍会得到一张全橙/全蓝的地面；出图前清一次涂地，恢复混凝土底色。
            var surface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (surface != null)
            {
                surface.InitializeIfNeeded();
                surface.ResetSurface();
            }

            // 相机架在 200m 高空，场景距离雾会把整张图糊成灰白：出图期间临时关雾，出图后还原。
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try
            {
                var camGo = new GameObject("~MapPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                // 正交等距：透视会把近边放大、把场地挤到画面中央一小块；正交构图尺寸可精确算，
                // 场地能吃满 40:56 画幅，同时 50° 俯角仍看得见箱体侧面与坡道斜面。
                cam.orthographic = true;
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = 1200f;
                cam.allowHDR = true;
                cam.allowMSAA = true;
                // 必须先把宽高比改成输出图的 40:56：否则 FrameArena 按 Game 视图的宽高比构图，
                // 换成竖幅渲染后左右两侧会被切掉、上方空出一大片海。
                cam.aspect = OutputWidth / (float)OutputHeight;
                FrameArena(cam);

                byte[] png = RenderToPng(cam, OutputWidth, OutputHeight);
                Object.DestroyImmediate(camGo);
                if (png == null) return false;

                string path = $"{OutputFolder}/{map.Id}.png";
                File.WriteAllBytes(path, png);
                ConfigureImporter(path);
                Debug.Log($"[MapPreview] 已生成 {map.Id} 实景缩略图：{path}（{png.Length / 1024} KB）");
                return true;
            }
            finally
            {
                RenderSettings.fog = fog;
                SetCharactersVisible(true);
            }
        }

        /// <summary>正交等距取景：把场地包围盒八个角投到相机的水平/竖直轴上，反解出恰好装下它的正交尺寸。</summary>
        private static void FrameArena(Camera cam)
        {
            // 40×56 是玩法边界，外扩一点把边界墙与出生台完整收进画面；高度覆盖信号塔与集装箱。
            var bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(46f, 10f, 62f));
            Quaternion rotation = Quaternion.Euler(PitchDegrees, YawDegrees, 0f);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;

            float halfWidth = 0f;
            float halfHeight = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    bounds.center.x + ((i & 1) == 0 ? -bounds.extents.x : bounds.extents.x),
                    bounds.center.y + ((i & 2) == 0 ? -bounds.extents.y : bounds.extents.y),
                    bounds.center.z + ((i & 4) == 0 ? -bounds.extents.z : bounds.extents.z));

                Vector3 offset = corner - bounds.center;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, up)));
            }

            // 宽高两边同时满足，再留一圈边距；正交投影与相机距离无关，所以距离只要够远即可。
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / cam.aspect) / (1f - FrameMargin);
            cam.transform.SetPositionAndRotation(bounds.center - forward * 200f, rotation);
        }

        /// <summary>角色（玩家 + 7 个 Bot）不进地图缩略图，出图期间临时关掉渲染，出图后恢复。</summary>
        private static void SetCharactersVisible(bool visible)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null) continue;
                string name = t.name;
                if (name != "Player_TeamA" && !name.StartsWith("BotA_") && !name.StartsWith("BotB_")) continue;

                var renderers = t.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null) renderers[r].enabled = visible;
                }
            }
        }

        private static byte[] RenderToPng(Camera cam, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            // 第一次 Render 时光照/反射探针还没就绪，会拍出整体偏暗的图：先弃一帧再正式采样。
            cam.Render();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        private static void ConfigureImporter(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            // 960×1344 是 NPOT：不关 nPOTScale 会被缩到 1024 长边，卡片上就糊了。
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
    }
}