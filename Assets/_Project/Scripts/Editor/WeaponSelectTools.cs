using System.IO;
using P1.Core;
using P1.UI;
using P1.UI.WeaponSelect;
using P1.Weapons;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 武器选择界面调试与留底截图工具（P8 选枪系统）：
    /// - 提供 [MenuItem("P1/UI/10. 捕获武器选择界面截图 (Logs/p8_weapon_select.png)")]；
    /// - 运行态直接捕获全高清游戏画面；
    /// - 编辑态走离屏相机与临时 Canvas 独立渲染，绝不触碰或重建当前场景（AGENTS.md 硬性规则）。
    /// </summary>
    public static class WeaponSelectTools
    {
        public const string ScreenshotPath = "Logs/p8_weapon_select.png";
        private const string EditorThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        [MenuItem("P1/UI/10. 捕获武器选择界面截图 (Logs/p8_weapon_select.png)", false, 110)]
        public static void CaptureWeaponSelectScreenshot()
        {
            Directory.CreateDirectory("Logs");

            if (Application.isPlaying)
            {
                ScreenCapture.CaptureScreenshot(ScreenshotPath);
                Debug.Log($"[WeaponSelectTools] 已在运行态捕获武器选择界面截图至：{ScreenshotPath}");
                return;
            }

            RenderEditorPreviewScreenshot(ScreenshotPath, 1920, 1080);
        }

        private static void RenderEditorPreviewScreenshot(string outputPath, int width, int height)
        {
            var previewRoot = new GameObject("__WeaponSelect_PreviewHost__");
            try
            {
                // 1. 创建离屏渲染专用相机
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(previewRoot.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
                cam.orthographic = true;
                cam.orthographicSize = 5f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100f;

                // 2. 创建临时独立 Canvas
                var canvasGo = new GameObject("PreviewCanvas");
                canvasGo.transform.SetParent(previewRoot.transform, false);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.sortingOrder = 15;

                var view = canvasGo.AddComponent<WeaponSelectView>();

                // 3. 加载资产并配置视图
                UIThemeSO theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
                var configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
                var icons = new Sprite[WeaponSelectMath.WeaponCount];

                for (int i = 0; i < WeaponSelectMath.WeaponCount; i++)
                {
                    WeaponKind kind = WeaponSelectMath.IndexToKind(i);
                    configs[i] = AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(WeaponSelectMath.GetWeaponAssetPath(kind));
                    icons[i] = AssetDatabase.LoadAssetAtPath<Sprite>(WeaponSelectMath.GetWeaponIconAssetPath(kind));
                }

                int savedIndex = WeaponSelectMath.LoadSelectedWeaponIndex();
                view.Configure(theme, configs, icons, savedIndex);

                // 4. 强制刷新布局并渲染到 RenderTexture
                Canvas.ForceUpdateCanvases();

                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
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
                File.WriteAllBytes(outputPath, png);

                Debug.Log($"[WeaponSelectTools] 已成功渲染武器选择界面离屏预览图：{outputPath} ({width}x{height})");
            }
            finally
            {
                Object.DestroyImmediate(previewRoot);
            }
        }
    }
}
