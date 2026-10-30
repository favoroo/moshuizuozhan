using P1.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace P1.Editor
{
    /// <summary>
    /// 漫画样式画廊：把全部 UI 基元摆在一屏里，供截图验收与后续回归。
    ///
    /// 为什么需要它：项目 UI 全是代码拼装的，agent 看不见画面；
    /// 每次改 <see cref="UIInkShapeGraphic"/> 的网格或字体 fallback，
    /// 必须先在这张「样张」上确认硬阴影、网点、气泡尾、中文渲染都正常，
    /// 再去改真实界面，避免在 HUD 里大海捞针。
    ///
    /// 菜单：P1/画风/漫画风/2. 生成样式画廊场景
    /// 产物：Assets/_Project/Scenes/ComicGallery.unity（可重复生成）
    /// </summary>
    public static class ComicGalleryBuilder
    {
        const string k_ScenePath = "Assets/_Project/Scenes/ComicGallery.unity";
        const string k_ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        [MenuItem("P1/画风/漫画风/2. 生成样式画廊场景", false, 4)]
        public static void BuildGalleryScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[漫画画廊] 请先退出 Play 模式，再生成画廊场景。");
                return;
            }

            var theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(k_ThemePath);
            if (theme == null)
            {
                Debug.LogError("[漫画画廊] 找不到 UITheme.asset");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("GalleryCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ComicStyle.PaperAlt;
            cam.orthographic = true;

            var canvasGo = new GameObject("GalleryCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 纸面 + 网点
            var paper = CreateImage("Paper", canvasGo.transform, ComicStyle.Paper);
            Stretch(paper.rectTransform);

            var halftoneGo = new GameObject("Halftone",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicHalftoneGraphic));
            halftoneGo.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)halftoneGo.transform);
            var halftone = halftoneGo.GetComponent<ComicHalftoneGraphic>();
            halftone.color = ComicStyle.Ink;
            halftone.Alpha = 0.06f;
            halftone.raycastTarget = false;

            // 标题
            CreateChunkyText(canvasGo.transform, "Title", "漫画风格 · COMIC STYLE", 64f,
                new Vector2(0f, 440f), new Vector2(1400f, 100f), theme, display: true,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Center);

            CreateText(canvasGo.transform, "Subtitle", "PAPER / INK / HALFTONE / HARD SHADOW / BUBBLE",
                22f, new Vector2(0f, 372f), new Vector2(1400f, 40f), theme, display: false,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Center);

            // ---- 第一排：三种面板 ----
            CreatePanel(canvasGo.transform, "PaperPanel", new Vector2(-560f, 180f), new Vector2(420f, 260f),
                ComicStyle.Paper, ComicStyle.Ink, halftoneDots: true, tail: false, theme: theme,
                caption: "纸面面板 PANEL");
            CreatePanel(canvasGo.transform, "InkPanel", new Vector2(0f, 180f), new Vector2(420f, 260f),
                ComicStyle.Ink, ComicStyle.Paper, halftoneDots: true, tail: false, theme: theme,
                caption: "墨黑面板 INK");
            CreatePanel(canvasGo.transform, "TeamPanel", new Vector2(560f, 180f), new Vector2(420f, 260f),
                theme.teamAColor, Color.white, halftoneDots: true, tail: false, theme: theme,
                caption: "队伍色 TEAM");

            // ---- 第二排：对话气泡（四个方向） ----
            CreatePanel(canvasGo.transform, "BubbleDown", new Vector2(-680f, -120f), new Vector2(360f, 180f),
                ComicStyle.Paper, ComicStyle.Ink, halftoneDots: false, tail: true, theme: theme,
                caption: "对话气泡 · 下", tailAngle: 270f);
            CreatePanel(canvasGo.transform, "BubbleLeft", new Vector2(-250f, -120f), new Vector2(360f, 180f),
                ComicStyle.Yellow, ComicStyle.Ink, halftoneDots: true, tail: true, theme: theme,
                caption: "对话气泡 · 左", tailAngle: 180f);
            CreatePanel(canvasGo.transform, "BubbleRight", new Vector2(180f, -120f), new Vector2(360f, 180f),
                ComicStyle.Blue, Color.white, halftoneDots: false, tail: true, theme: theme,
                caption: "对话气泡 · 右", tailAngle: 0f);
            CreatePanel(canvasGo.transform, "BubbleUp", new Vector2(610f, -120f), new Vector2(360f, 180f),
                ComicStyle.Red, Color.white, halftoneDots: false, tail: true, theme: theme,
                caption: "对话气泡 · 上", tailAngle: 90f);

            // ---- 第三排：动作线 + 按钮三态 ----
            var burstRoot = new GameObject("ActionLines",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicActionLinesGraphic));
            burstRoot.transform.SetParent(canvasGo.transform, false);
            var burstRect = (RectTransform)burstRoot.transform;
            burstRect.sizeDelta = new Vector2(300f, 300f);
            burstRect.anchoredPosition = new Vector2(-640f, -360f);
            var lines = burstRoot.GetComponent<ComicActionLinesGraphic>();
            lines.color = ComicStyle.Red;
            lines.Alpha = 0.28f;
            lines.raycastTarget = false;

            CreateBadge(canvasGo.transform, new Vector2(-740f, -330f), "NEW!", ComicStyle.Yellow);
            CreateBadge(canvasGo.transform, new Vector2(-540f, -390f), "POW!", ComicStyle.Red);

            CreateButtonSample(canvasGo.transform, new Vector2(-160f, -360f), "普通 NORMAL", ComicStyle.Paper, theme);
            CreateButtonSample(canvasGo.transform, new Vector2(140f, -360f), "悬停 HOVER", ComicStyle.Yellow, theme);
            CreateButtonSample(canvasGo.transform, new Vector2(440f, -360f), "按下 PRESS", ComicStyle.Paper, theme,
                pressed: true);

            // 字体样张
            CreateText(canvasGo.transform, "FontCjkDisplay", "庆科黄油体 · 大招就绪", 40f,
                new Vector2(560f, -300f), new Vector2(520f, 60f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "FontCjkBody", "快乐体正文：主音量 / 屏幕震动 / 触控灵敏度", 24f,
                new Vector2(560f, -375f), new Vector2(560f, 40f), theme, display: false, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "FontDigits", "02:00  100%", 48f,
                new Vector2(560f, -450f), new Vector2(520f, 60f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[漫画画廊] 已生成 " + k_ScenePath + "（进 Play 后截图验收）");
        }

        // ---------- 构件 ----------

        static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color fill,
            Color textColor, bool halftoneDots, bool tail, UIThemeSO theme, string caption,
            float tailAngle = 270f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = tail ? UIInkShape.SpeechBubble : UIInkShape.RoundedRect;
            shape.ApplyComic(fill, halftoneDots);
            shape.CornerRadius = tail ? ComicStyle.CornerBubble : ComicStyle.CornerSharp;
            shape.SpeechTail = tail;
            shape.TailAngle = tailAngle;
            shape.raycastTarget = false;

            CreateText(go.transform, "Caption", caption, 30f, new Vector2(0f, 0f), size * 0.72f,
                theme, display: false, fill: textColor, align: TextAlignmentOptions.Center);
        }

        static void CreateButtonSample(Transform parent, Vector2 pos, string label, Color fill, UIThemeSO theme,
            bool pressed = false)
        {
            var go = new GameObject("Btn_" + label.Replace(" ", ""),
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(240f, 96f);
            rect.anchoredPosition = pos;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.ApplyComic(fill, true);
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.HardShadow = !pressed;
            shape.ShadowOffset = pressed
                ? Vector2.zero
                : new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            shape.HalftoneAlpha = pressed ? ComicStyle.HalftoneHoverAlpha : ComicStyle.HalftoneAlpha;

            CreateChunkyText(go.transform, "Label", label, 26f, Vector2.zero, new Vector2(220f, 70f),
                theme, display: false, fill: ComicStyle.Ink, align: TextAlignmentOptions.Center);
        }

        static void CreateBadge(Transform parent, Vector2 pos, string text, Color fill)
        {
            var go = new GameObject("Badge_" + text, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(ComicBadge));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(150f, 54f);
            rect.anchoredPosition = pos;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.ApplyComic(fill, false, ComicStyle.Border, new Vector2(5f, -5f));
            shape.Shape = UIInkShape.SpeechBubble;
            shape.CornerRadius = 14f;

            var label = CreateText(go.transform, "Label", text, 26f, Vector2.zero, new Vector2(140f, 44f),
                null, display: true, fill: ComicStyle.Ink, align: TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size,
            Vector2 pos, Vector2 box, UIThemeSO theme, bool display, Color fill,
            TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = box;
            rect.anchoredPosition = pos;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = fill;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;

            if (theme != null)
            {
                var font = theme.ResolveFont(display, text);
                if (font != null) tmp.font = font;
            }

            return tmp;
        }

        static void CreateChunkyText(Transform parent, string name, string text, float size,
            Vector2 pos, Vector2 box, UIThemeSO theme, bool display, Color fill, TextAlignmentOptions align)
        {
            var tmp = CreateText(parent, name, text, size, pos, box, theme, display, fill, align);
            var chunky = tmp.gameObject.AddComponent<UIChunkyText>();
            chunky.Configure(theme, text, size, display, true, fill, true);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}