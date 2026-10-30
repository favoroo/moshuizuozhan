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
    /// 几何大胆风样式画廊：把全部 UI 基元摆在一屏里，供截图验收与后续回归。
    ///
    /// 为什么需要它：项目 UI 全是代码拼装的，agent 看不见画面；
    /// 每次改 <see cref="UIInkShapeGraphic"/> 的网格或字体 fallback，
    /// 必须先在这张「样张」上确认直角、纯色块、粗体字、中文渲染都正常，
    /// 再去改真实界面，避免在 HUD 里大海捞针。
    ///
    /// 菜单：P1/画风/几何风/2. 生成样式画廊场景
    /// 产物：Assets/_Project/Scenes/ComicGallery.unity（可重复生成）
    /// </summary>
    public static class ComicGalleryBuilder
    {
        const string k_ScenePath = "Assets/_Project/Scenes/ComicGallery.unity";
        const string k_ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        [MenuItem("P1/画风/几何风/2. 生成样式画廊场景", false, 4)]
        [MenuItem("P1/画风/漫画风/2. 生成样式画廊场景（旧路径）", false, 4)]
        public static void BuildGalleryScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[几何画廊] 请先退出 Play 模式，再生成画廊场景。");
                return;
            }

            var theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(k_ThemePath);
            if (theme == null)
            {
                Debug.LogError("[几何画廊] 找不到 UITheme.asset");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("GalleryCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ComicStyle.Paper;
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

            var paper = CreateImage("Paper", canvasGo.transform, ComicStyle.Paper);
            Stretch(paper.rectTransform);

            // 左侧超大标题（不对称构图）+ 旋转几何装饰
            CreateText(canvasGo.transform, "Title", "GEOMETRIC", 110f,
                new Vector2(-550f, 420f), new Vector2(700f, 130f), theme, display: true,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Left);
            CreateText(canvasGo.transform, "TitleCjk", "几何大胆风", 56f,
                new Vector2(-760f, 330f), new Vector2(520f, 70f), theme, display: true,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Left);
            CreateText(canvasGo.transform, "Subtitle", "BOLD BLOCKS / SHARP CORNERS / BIG TYPE / NO SHADOW",
                20f, new Vector2(-470f, 268f), new Vector2(820f, 34f), theme, display: false,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Left);

            CreateRotatedBlock(canvasGo.transform, "DecorRedCircle", new Vector2(700f, 420f),
                new Vector2(150f, 150f), ComicStyle.Red, 0f, circle: true);
            CreateRotatedBlock(canvasGo.transform, "DecorInkSquare", new Vector2(860f, 300f),
                new Vector2(120f, 120f), ComicStyle.Ink, 45f);
            CreateRotatedBlock(canvasGo.transform, "DecorBlueBar", new Vector2(610f, 300f),
                new Vector2(180f, 56f), ComicStyle.Blue, -15f);

            // ---- 第一排：纯色块面板 ----
            CreateBlock(canvasGo.transform, "PaperPanel", new Vector2(-640f, 150f), new Vector2(300f, 180f),
                ComicStyle.Paper, ComicStyle.Ink, "纸面 PANEL", theme);
            CreateBlock(canvasGo.transform, "InkPanel", new Vector2(-300f, 150f), new Vector2(300f, 180f),
                ComicStyle.Ink, ComicStyle.Paper, "墨黑 INK", theme);
            CreateBlock(canvasGo.transform, "RedPanel", new Vector2(40f, 150f), new Vector2(300f, 180f),
                ComicStyle.Red, ComicStyle.Paper, "强调 RED", theme);
            CreateBlock(canvasGo.transform, "BluePanel", new Vector2(380f, 150f), new Vector2(300f, 180f),
                ComicStyle.Blue, ComicStyle.Paper, "信息 BLUE", theme);

            // ---- 第二排：形状基元（直角 / 胶囊 / 正圆 / 旋转方块） ----
            CreateShape(canvasGo.transform, "ShapeSharp", new Vector2(-640f, -60f), new Vector2(300f, 140f),
                UIInkShape.SharpRect, ComicStyle.Paper, ComicStyle.Ink, "直角 SHARP", theme);
            CreateShape(canvasGo.transform, "ShapePill", new Vector2(-300f, -60f), new Vector2(300f, 140f),
                UIInkShape.Pill, ComicStyle.Ink, ComicStyle.Paper, "胶囊 PILL", theme);
            CreateShape(canvasGo.transform, "ShapeCircle", new Vector2(40f, -60f), new Vector2(140f, 140f),
                UIInkShape.Blob, ComicStyle.Yellow, ComicStyle.Ink, "正圆", theme);
            CreateShape(canvasGo.transform, "ShapeTeam", new Vector2(380f, -60f), new Vector2(300f, 140f),
                UIInkShape.SharpRect, theme.teamAColor, ComicStyle.Ink, "队伍色 TEAM", theme);

            // ---- 第三排：按钮三态 ----
            CreateButtonSample(canvasGo.transform, new Vector2(-640f, -260f), "普通 NORMAL", ComicStyle.Paper,
                ComicStyle.Ink, theme);
            CreateButtonSample(canvasGo.transform, new Vector2(-330f, -260f), "悬停 HOVER", ComicStyle.Red,
                ComicStyle.Paper, theme);
            CreateButtonSample(canvasGo.transform, new Vector2(-20f, -260f), "按下 PRESS", ComicStyle.Ink,
                ComicStyle.Paper, theme, pressed: true);

            // ---- 字号阶梯 + 数字即设计 ----
            CreateText(canvasGo.transform, "TypeHero", "02:00", 132f, new Vector2(520f, -250f),
                new Vector2(560f, 150f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "TypeH1", "H1 判定 VICTORY", 60f, new Vector2(520f, -350f),
                new Vector2(560f, 70f), theme, display: true, fill: ComicStyle.Red,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "TypeH2", "H2 标题：墨水作战", 40f, new Vector2(520f, -415f),
                new Vector2(560f, 50f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "TypeBody", "正文 22：主音量 / 屏幕震动 / 触控灵敏度", 22f,
                new Vector2(520f, -465f), new Vector2(620f, 34f), theme, display: false, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "TypeSmall", "小字 18：最低可读字号，不允许更小", 18f,
                new Vector2(520f, -505f), new Vector2(620f, 28f), theme, display: false,
                fill: ComicStyle.Ink, align: TextAlignmentOptions.Center);

            // 覆盖率大数字（结算页语言）
            CreateText(canvasGo.transform, "PercentA", "52.8%", 84f, new Vector2(-560f, -450f),
                new Vector2(360f, 100f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "PercentB", "47.2%", 84f, new Vector2(-40f, -450f),
                new Vector2(360f, 100f), theme, display: true, fill: ComicStyle.Ink,
                align: TextAlignmentOptions.Center);
            CreateText(canvasGo.transform, "VsLabel", "VS", 30f, new Vector2(-300f, -450f),
                new Vector2(120f, 60f), theme, display: true, fill: ComicStyle.Red,
                align: TextAlignmentOptions.Center);

            EditorSceneManager.MarkSceneDirty(scene);
            EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("[几何画廊] 已生成 " + k_ScenePath + "（进 Play 后截图验收）");
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

        static void CreateRotatedBlock(Transform parent, string name, Vector2 pos, Vector2 size,
            Color fill, float rotation, bool circle = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = circle ? UIInkShape.Blob : UIInkShape.SharpRect;
            shape.ApplyComic(fill, false);
            shape.WobbleAmplitude = 0f;
            shape.raycastTarget = false;
        }

        static void CreateBlock(Transform parent, string name, Vector2 pos, Vector2 size, Color fill,
            Color textColor, string caption, UIThemeSO theme)
        {
            CreateShape(parent, name, pos, size, UIInkShape.SharpRect, fill, textColor, caption, theme);
        }

        static void CreateShape(Transform parent, string name, Vector2 pos, Vector2 size,
            UIInkShape shapeKind, Color fill, Color textColor, string caption, UIThemeSO theme)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = shapeKind;
            shape.ApplyComic(fill, false);
            shape.raycastTarget = false;

            float captionSize = Mathf.Min(size.x, size.y) < 180f ? 20f : 26f;
            CreateText(go.transform, "Caption", caption, captionSize, Vector2.zero, size * 0.8f,
                theme, display: false, fill: textColor, align: TextAlignmentOptions.Center);
        }

        static void CreateButtonSample(Transform parent, Vector2 pos, string label, Color fill, Color textColor,
            UIThemeSO theme, bool pressed = false)
        {
            var go = new GameObject("Btn_" + label.Replace(" ", ""),
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(250f, 96f);
            rect.anchoredPosition = pos + (pressed ? new Vector2(0f, ComicStyle.PressOffsetY) : Vector2.zero);

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.SharpRect;
            shape.ApplyComic(fill, false);
            shape.BorderWidth = ComicStyle.Border;
            shape.BorderColor = ComicStyle.Ink;

            CreateText(go.transform, "Label", label, 26f, Vector2.zero, new Vector2(230f, 70f),
                theme, display: false, fill: textColor, align: TextAlignmentOptions.Center)
                .fontStyle = FontStyles.Bold;
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
            tmp.fontStyle = display ? FontStyles.Bold : FontStyles.Normal;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;

            if (theme != null)
            {
                var font = theme.ResolveFont(display, text);
                if (font != null) tmp.font = font;
            }

            return tmp;
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
