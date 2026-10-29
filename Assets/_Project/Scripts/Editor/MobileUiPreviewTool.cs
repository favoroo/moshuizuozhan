using System.IO;
using P1.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P1.Editor
{
    public static class MobileUiPreviewTool
    {
        [MenuItem("P1/移动端/渲染移动端 UI 布局预览图 (Logs/mobile_ui_preview.png)", false, 30)]
        public static void RenderMobileUiPreview()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/PhaseA_Playbox.unity", OpenSceneMode.Single);

            // 确保目录存在
            if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");

            var camGo = new GameObject("MobilePreviewCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            // 查找或添加 MobileTouchUI
            var mobileTouch = Object.FindAnyObjectByType<MobileTouchUI>();
            if (mobileTouch == null)
            {
                var managers = GameObject.Find("_Managers");
                if (managers != null) mobileTouch = managers.AddComponent<MobileTouchUI>();
            }

            // 查找或生成 HUD
            var hud = Object.FindAnyObjectByType<HudController>();
            if (hud != null)
            {
                hud.ApplyMobileLayout();
            }

            // 触发 MobileTouchUI 构建
            var buildMethod = typeof(MobileTouchUI).GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (buildMethod != null && mobileTouch != null)
            {
                buildMethod.Invoke(mobileTouch, null);
            }

            // 将 HUD Canvas 和 MobileTouch Canvas 临时绑定至该相机进行叠加渲染
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f;
            }

            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] bytes = tex.EncodeToPNG();
            File.WriteAllBytes("Logs/mobile_ui_preview.png", bytes);
            Debug.Log($"[MobileUiPreviewTool] 移动端 UI 预览已保存至 Logs/mobile_ui_preview.png ({bytes.Length} 字节)！");

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}
