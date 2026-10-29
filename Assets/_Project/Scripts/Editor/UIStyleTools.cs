using P1.Core;
using P1.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace P1.Editor
{
    /// <summary>
    /// P4-A UI 基类工具：生成 <c>UITheme.asset</c>、构建可视觉验收的 <c>UIDemo.unity</c> 演示场景，
    /// 并提供一次轻量体检。所有场景装配都走 Editor API（AGENTS.md 规则 1：不手写 YAML）。
    ///
    /// 演示场景用 Additive 方式构建：不会重写当前打开的 PhaseA 主场景；
    /// 只有「打开演示场景」菜单会在确认当前场景无未保存修改后切换过去。
    /// </summary>
    public static class UIStyleTools
    {
        private const string LogTag = "[UI基类]";

        public const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        public const string DemoScenePath = "Assets/_Project/Scenes/UIDemo.unity";

        private const string DisplayFontPath = "Assets/_Project/Art/Fonts/TMP/TitanOne-Regular SDF.asset";

        private const string BodyFontPath = "Assets/_Project/Art/Fonts/TMP/Rubik-Bold SDF.asset";

        private const string IconRoot = "Assets/_Project/Art/Sprites/Icons/";

        private static readonly Color DemoBackground = new Color(0.055f, 0.043f, 0.086f, 1f);

        [MenuItem("P1/UI/1. 生成 UI 主题资产 (UITheme.asset)", false, 1)]
        public static void CreateThemeAsset()
        {
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
            }

            UIThemeSO existing = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            if (existing != null)
            {
                Debug.Log(LogTag + " 主题资产已存在，保持现有调参不覆盖：" + ThemePath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            UIThemeSO theme = ScriptableObject.CreateInstance<UIThemeSO>();
            theme.displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
            theme.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);

            AssetDatabase.CreateAsset(theme, ThemePath);
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();

            if (theme.displayFont == null || theme.bodyFont == null)
            {
                Debug.LogWarning(LogTag + " 主题已创建，但 TMP 字体缺失；先跑 P1/素材/4 生成 TMP 字体资产。");
            }

            Debug.Log(LogTag + " UITheme.asset 已生成：" + ThemePath);
            EditorGUIUtility.PingObject(theme);
        }

        [MenuItem("P1/UI/2. 生成 UI 基类演示场景 (UIDemo.unity)", false, 2)]
        public static void BuildDemoScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning(LogTag + " 请先退出 Play 模式再生成演示场景。");
                return;
            }

            Scene loadedDemo = SceneManager.GetSceneByPath(DemoScenePath);

            UIThemeSO theme = EnsureTheme();

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene;
            bool createdNew;

            if (loadedDemo.IsValid() && loadedDemo.isLoaded)
            {
                // 演示场景是本工具的全权产物：直接清空重建，避免「同路径已被打开」导致保存失败。
                scene = loadedDemo;
                SceneManager.SetActiveScene(scene);

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Object.DestroyImmediate(roots[i]);
                }

                createdNew = false;
                Debug.Log(LogTag + " 复用已打开的 UIDemo 场景并重建（旧演示内容已清除）。");
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                createdNew = true;
            }

            BuildDemoContent(theme);

            bool saved = EditorSceneManager.SaveScene(scene, DemoScenePath);
            AssetDatabase.Refresh();

            if (createdNew)
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            Debug.Log(saved
                ? LogTag + " UIDemo 演示场景已生成：" + DemoScenePath
                : LogTag + " UIDemo 保存失败，请检查控制台。");
        }

        [MenuItem("P1/UI/3. 打开 UI 演示场景 (UIDemo.unity)", false, 3)]
        public static void OpenDemoScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning(LogTag + " 请先退出 Play 模式。");
                return;
            }

            Scene loaded = SceneManager.GetSceneByPath(DemoScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                Debug.Log(LogTag + " UIDemo 已在打开状态，按 Play 即可查看。");
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.isDirty)
            {
                Debug.LogWarning(LogTag + " 当前场景有未保存修改，已拒绝切换场景（先保存或放弃修改）。");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                BuildDemoScene();
            }

            EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            Debug.Log(LogTag + " 已打开演示场景。按 Play 后点击 PRESS ME 查看弹簧与墨汁晃动。");
        }

        [MenuItem("P1/UI/4. 体检 UI 基类（主题/字体/图标）", false, 4)]
        public static void ReportFoundation()
        {
            UIThemeSO theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            string[] iconGuids = AssetDatabase.FindAssets("t:Sprite", new[] { IconRoot.TrimEnd('/') });

            string fontState = theme == null
                ? "主题缺失"
                : $"display={(theme.displayFont != null ? theme.displayFont.name : "缺失")}, " +
                  $"body={(theme.bodyFont != null ? theme.bodyFont.name : "缺失")}";

            Debug.Log($"{LogTag} UI 基类体检：" +
                      $"主题={(theme != null ? ThemePath : "缺失")}；{fontState}；" +
                      $"图标 {iconGuids.Length} 个；演示场景={(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) != null ? "已生成" : "未生成")}。");

            if (theme == null)
            {
                Debug.LogWarning(LogTag + " 先运行 P1/UI/1 生成主题资产。");
            }
            else if (theme.displayFont == null || theme.bodyFont == null)
            {
                Debug.LogWarning(LogTag + " 主题缺少 TMP 字体引用。");
            }
        }

        private static UIThemeSO EnsureTheme()
        {
            UIThemeSO theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            if (theme == null)
            {
                CreateThemeAsset();
                theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);
            }

            bool changed = false;
            if (theme.displayFont == null)
            {
                theme.displayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayFontPath);
                changed = true;
            }

            if (theme.bodyFont == null)
            {
                theme.bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
            }

            return theme;
        }

        private static void BuildDemoContent(UIThemeSO theme)
        {
            GameObject cameraGo = new GameObject("Demo Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DemoBackground;
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            GameObject eventSystemGo = new GameObject("EventSystem", typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystemGo.transform.SetParent(null);

            GameObject canvasGo = new GameObject("UIDemo_Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            CreateFullScreenImage(canvasGo.transform, "Background", DemoBackground);

            CreateDecorativeBlob(canvasGo.transform, "BlobA", new Vector2(-650f, -400f),
                new Vector2(980f, 980f), new Color(1f, 0.44f, 0.06f, 0.17f), 11);
            CreateDecorativeBlob(canvasGo.transform, "BlobB", new Vector2(700f, 430f),
                new Vector2(820f, 820f), new Color(0.15f, 0.42f, 1f, 0.16f), 23);
            CreateDecorativeBlob(canvasGo.transform, "BlobC", new Vector2(250f, -560f),
                new Vector2(420f, 420f), new Color(0.15f, 0.42f, 1f, 0.10f), 37);

            CreateChunkyText(canvasGo.transform, "Title", theme, "INKWAVE  UI  FOUNDATION",
                92f, true, Color.white, new Vector2(0f, 372f), new Vector2(1500f, 140f));

            CreateChunkyText(canvasGo.transform, "Subtitle", theme,
                "P4-A  ·  SPRING MOTION  ·  CHUNKY OUTLINE  ·  INK TEAM TINT",
                26f, false, new Color(0.86f, 0.84f, 0.92f, 1f), new Vector2(0f, 296f),
                new Vector2(1500f, 46f), false, false);

            CreateCard(canvasGo.transform, "CardA", theme, UIInkCardVariant.Team, Team.TeamA,
                "TURF  4v4", "P4-B  ·  ROSTER & TIMER",
                new[]
                {
                    IconRoot + "Weapons/weapon_shooter.png",
                    IconRoot + "Glyphs/glyph_drop.png",
                    IconRoot + "Specials/special_slam.png",
                },
                new Vector2(-280f, 20f), new Vector2(500f, 330f), 5);

            CreateCard(canvasGo.transform, "CardB", theme, UIInkCardVariant.Team, Team.TeamB,
                "SPECIAL  ORB", "P4-C  ·  LIQUID ORB & TANK",
                new[]
                {
                    IconRoot + "Weapons/weapon_charger.png",
                    IconRoot + "Weapons/weapon_roller.png",
                    IconRoot + "Specials/special_storm.png",
                },
                new Vector2(280f, 20f), new Vector2(500f, 330f), 17);

            CreateCard(canvasGo.transform, "CardC", theme, UIInkCardVariant.Dark, Team.TeamA,
                "KILL  CARD", "P4-D  ·  SLIDE-IN BANNER",
                new[]
                {
                    IconRoot + "Marks/mark_splat.png",
                    IconRoot + "Glyphs/glyph_crown.png",
                },
                new Vector2(-280f, -300f), new Vector2(500f, 200f), 29, true, false);

            CreateJellyButton(canvasGo.transform, "JellyButton", theme, new Vector2(350f, -300f));

            CreateChunkyText(canvasGo.transform, "Footer", theme,
                "CLICK THE BUTTON  ·  PRESS SQUASH  ·  RELEASE OVERSHOOT  ·  INK WOBBLE",
                20f, false, new Color(0.765f, 0.741f, 0.839f, 1f), new Vector2(0f, -440f),
                new Vector2(1400f, 36f), false, false);
        }

        private static void CreateFullScreenImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rectTransform = (RectTransform)go.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static UIInkShapeGraphic CreateDecorativeBlob(Transform parent, string name,
            Vector2 position, Vector2 size, Color color, int seed)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);

            RectTransform rectTransform = (RectTransform)go.transform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;

            UIInkShapeGraphic graphic = go.GetComponent<UIInkShapeGraphic>();
            UIInkCardStyle style = new UIInkCardStyle
            {
                shape = UIInkShape.Blob,
                borderWidth = 0f,
                wobble = 0.10f,
                glossAlpha = 0f,
                innerRimAlpha = 0f,
            };
            graphic.ApplyStyle(style, color);
            graphic.Seed = seed;
            graphic.raycastTarget = false;
            return graphic;
        }

        private static UIChunkyText CreateChunkyText(Transform parent, string name, UIThemeSO theme,
            string text, float fontSize, bool displayFont, Color fill, Vector2 position, Vector2 size,
            bool doubleLayer = true, bool outlined = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI),
                typeof(UIChunkyText));
            go.transform.SetParent(parent, false);

            RectTransform rectTransform = (RectTransform)go.transform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            tmp.color = fill;

            UIChunkyText chunky = go.GetComponent<UIChunkyText>();
            chunky.Configure(theme, text, fontSize, displayFont, doubleLayer, fill, outlined);
            return chunky;
        }

        private static UIInkCard CreateCard(Transform parent, string name, UIThemeSO theme,
            UIInkCardVariant variant, Team team, string titleText, string bodyText, string[] iconPaths,
            Vector2 position, Vector2 size, int seed, bool compact = false, bool tintCardFill = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(UIInkCard));
            go.transform.SetParent(parent, false);

            RectTransform rectTransform = (RectTransform)go.transform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;

            UIInkCard card = go.GetComponent<UIInkCard>();
            card.Configure(theme, variant, team, tintCardFill);
            card.EnsureBuilt();
            card.Background.Seed = seed;

            float halfHeight = size.y * 0.5f;
            float titleY = compact ? halfHeight * 0.42f : halfHeight * 0.52f;
            float bodyY = compact ? halfHeight * 0.06f : halfHeight * 0.26f;
            float iconY = compact ? -halfHeight * 0.40f : -halfHeight * 0.32f;

            UIChunkyText title = CreateChunkyText(card.Content, "CardTitle", theme, titleText,
                compact ? 30f : 40f, true, Color.white, new Vector2(0f, titleY),
                new Vector2(size.x - 70f, compact ? 42f : 56f));
            card.SetTitle(title);

            CreateChunkyText(card.Content, "CardBody", theme, bodyText, 20f, false,
                new Color(0.88f, 0.86f, 0.94f, 1f), new Vector2(0f, bodyY),
                new Vector2(size.x - 80f, 30f), false, false);

            GameObject rowGo = new GameObject("IconRow", typeof(RectTransform));
            rowGo.transform.SetParent(card.Content, false);
            RectTransform rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, iconY);
            rowRect.sizeDelta = new Vector2(size.x - 100f, compact ? 66f : 84f);

            float spacing = compact ? 86f : 106f;
            float chipSize = compact ? 66f : 84f;
            for (int i = 0; i < iconPaths.Length; i++)
            {
                float x = (i - (iconPaths.Length - 1) * 0.5f) * spacing;
                CreateIconChip(rowRect, "IconChip" + i, iconPaths[i], new Vector2(x, 0f), chipSize,
                    team, theme);
            }

            return card;
        }

        /// <summary>
        /// 深色圆形徽章 + 纯白图标。图标由 InkTeamColorFilter 染成队伍墨色，
        /// 在深色底上才看得清（这也是 HUD 名册 / 击杀卡片的标准做法）。
        /// </summary>
        private static void CreateIconChip(Transform parent, string name, string spritePath,
            Vector2 position, float chipSize, Team team, UIThemeSO theme)
        {
            GameObject chipGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            chipGo.transform.SetParent(parent, false);

            RectTransform chipRect = (RectTransform)chipGo.transform;
            chipRect.anchorMin = new Vector2(0.5f, 0.5f);
            chipRect.anchorMax = new Vector2(0.5f, 0.5f);
            chipRect.anchoredPosition = position;
            chipRect.sizeDelta = new Vector2(chipSize, chipSize);

            UIInkShapeGraphic chip = chipGo.GetComponent<UIInkShapeGraphic>();
            UIInkCardStyle chipStyle = new UIInkCardStyle
            {
                shape = UIInkShape.RoundedRect,
                borderWidth = 3f,
                borderColor = theme != null ? theme.outlineColor : new Color(0.082f, 0.071f, 0.110f, 1f),
                cornerRadius = chipSize * 0.30f,
                wobble = 0f,
                glossAlpha = 0.12f,
                glossCenter = 0.78f,
                glossThickness = 0.22f,
                innerRimAlpha = 0.12f,
            };
            chip.ApplyStyle(chipStyle, new Color(0.105f, 0.088f, 0.148f, 0.96f));
            chip.raycastTarget = false;

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image),
                typeof(InkTeamColorFilter));
            iconGo.transform.SetParent(chipGo.transform, false);

            RectTransform iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(chipSize * 0.62f, chipSize * 0.62f);

            Image image = iconGo.GetComponent<Image>();
            image.sprite = LoadSprite(spritePath);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            if (image.sprite == null)
            {
                Debug.LogWarning(LogTag + " 图标缺失：" + spritePath);
            }

            InkTeamColorFilter filter = iconGo.GetComponent<InkTeamColorFilter>();
            filter.Configure(team, false, theme);
        }

        private static void CreateJellyButton(Transform parent, string name, UIThemeSO theme,
            Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UIJellyButton));
            go.transform.SetParent(parent, false);

            RectTransform rectTransform = (RectTransform)go.transform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = new Vector2(460f, 128f);

            UIInkShapeGraphic background = go.GetComponent<UIInkShapeGraphic>();
            UIInkCardStyle buttonStyle = theme.darkCard.Clone();
            buttonStyle.shape = UIInkShape.RoundedRect;
            buttonStyle.cornerRadius = 52f;
            buttonStyle.borderWidth = 9f;
            buttonStyle.glossAlpha = 0.20f;
            buttonStyle.innerRimAlpha = 0.14f;
            background.ApplyStyle(buttonStyle, theme.GetTeamColor(Team.TeamA));
            background.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;

            CreateChunkyText(go.transform, "Label", theme, "PRESS  ME", 46f, true, Color.white,
                Vector2.zero, new Vector2(420f, 96f));
        }

        private static Sprite LoadSprite(string assetPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite) return sprite;
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}