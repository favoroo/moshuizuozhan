using System.IO;
using P1.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace P1.EditorTools
{
    public static class TopRosterTools
    {
        public const string LogTag = "[TopRosterTools]";
        public const string ScreenShotPath = "Logs/p4b_hud_roster_timer.png";
        public const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        [MenuItem("P1/UI/8. 绑定战况条与中央墨滴计时器到当前场景 HUD", false, 8)]
        public static void BindCurrentSceneRosterAndTimer()
        {
            HudController hud = Object.FindFirstObjectByType<HudController>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning(LogTag + " 当前场景未找到 HudController。");
                return;
            }

            UIThemeSO theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);

            var serialized = new SerializedObject(hud);
            SerializedProperty themeProp = serialized.FindProperty("uiTheme");
            if (themeProp != null && theme != null) themeProp.objectReferenceValue = theme;

            // 查找或创建 TopRosterBarView
            TopRosterBarView roster = hud.GetComponentInChildren<TopRosterBarView>(true);
            if (roster == null)
            {
                var rosterGo = new GameObject("TopRosterBarView", typeof(RectTransform), typeof(TopRosterBarView));
                rosterGo.transform.SetParent(hud.transform, false);
                roster = rosterGo.GetComponent<TopRosterBarView>();
            }
            if (theme != null) roster.SetTheme(theme);
            roster.EnsureSlotsBuilt();

            // 查找或创建 CenterTimerView
            CenterTimerView timer = hud.GetComponentInChildren<CenterTimerView>(true);
            if (timer == null)
            {
                var timerGo = new GameObject("CenterTimerView", typeof(RectTransform), typeof(CenterTimerView));
                timerGo.transform.SetParent(hud.transform, false);
                timer = timerGo.GetComponent<CenterTimerView>();
            }
            if (theme != null) timer.SetTheme(theme);
            timer.EnsureTimerBuilt();

            SerializedProperty rosterProp = serialized.FindProperty("topRosterBar");
            SerializedProperty timerProp = serialized.FindProperty("centerTimer");
            if (rosterProp != null) rosterProp.objectReferenceValue = roster;
            if (timerProp != null) timerProp.objectReferenceValue = timer;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);

            Debug.Log(LogTag + " 战况条与中央墨滴计时器已成功绑定到 " + hud.name);
        }

        [MenuItem("P1/UI/9. 捕获顶部战况条与中央计时器截图 (Logs/p4b_hud_roster_timer.png)", false, 9)]
        public static void CaptureRosterAndTimerScreenshot()
        {
            BindCurrentSceneRosterAndTimer();

            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning(LogTag + " 场景中没有找到 Canvas，无法截图。");
                return;
            }

            // 确保目录存在
            string dir = Path.GetDirectoryName(ScreenShotPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RenderMode prevMode = canvas.renderMode;
            Camera mainCam = Camera.main;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCam;

            int width = 1600;
            int height = 900;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevRt = RenderTexture.active;

            if (mainCam != null)
            {
                mainCam.targetTexture = rt;
                mainCam.Render();
                mainCam.targetTexture = null;
            }

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            canvas.renderMode = prevMode;
            RenderTexture.active = prevRt;
            Object.DestroyImmediate(rt);

            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(ScreenShotPath, bytes);

            Debug.Log(LogTag + " 截图已成功保存到: " + ScreenShotPath + " (" + bytes.Length + " bytes)");
        }
    }
}
