using System.IO;
using P1.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace P1.Editor
{
    /// <summary>
    /// P10-A 三渲二画风装配器：把 ArenaStyleSO 的风格参数幂等地落到
    /// 环境材质、全屏描边 Renderer Feature、SSAO 与当前场景光照/后处理上。
    /// 所有资产修改都走 Unity API，不手改 .asset / .unity YAML。
    /// </summary>
    public static class ToonStyleInstaller
    {
        private const string StylePath = "Assets/_Project/ScriptableObjects/ArenaStyle.asset";
        private const string MatFolder = "Assets/_Project/Materials";
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string OutlineMaterialPath = "Assets/_Project/Materials/Mat_ToonOutline.mat";
        private const string OutlineFeatureName = "P1 Toon Outline";
        private const string BackupPath = "Logs/PC_Renderer.backup.asset";

        private static readonly int ShadeThresholdId = Shader.PropertyToID("_ShadeThreshold");
        private static readonly int ShadeSoftnessId = Shader.PropertyToID("_ShadeSoftness");
        private static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");
        private static readonly int ShadowFloorId = Shader.PropertyToID("_ShadowFloor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimStrengthId = Shader.PropertyToID("_RimStrength");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int SpecStrengthId = Shader.PropertyToID("_SpecStrength");
        private static readonly int SpecThresholdId = Shader.PropertyToID("_SpecThreshold");
        private static readonly int SpecSoftnessId = Shader.PropertyToID("_SpecSoftness");
        private static readonly int InkShadeThresholdId = Shader.PropertyToID("_InkShadeThreshold");
        private static readonly int InkShadeSoftnessId = Shader.PropertyToID("_InkShadeSoftness");
        private static readonly int InkShadowFloorId = Shader.PropertyToID("_InkShadowFloor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TrimColorId = Shader.PropertyToID("_TrimColor");
        private static readonly int GridLineColorId = Shader.PropertyToID("_GridLineColor");
        private static readonly int CliffColorId = Shader.PropertyToID("_CliffColor");

        [MenuItem("P1/画风/1. 应用三渲二画风（材质+光照+描边）", false, 10)]
        public static void ApplyToonStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null)
            {
                Debug.LogError($"[P10-A] 找不到 {StylePath}，请先运行 P1/构建/1 生成配置。");
                return;
            }

            WriteStylePreset(style);
            ApplyEnvironmentMaterials(style);
            ApplyCharacterMaterials(style);
            ApplyPostProcessStyle(style);

            Material outlineMat = EnsureOutlineMaterial(style);
            if (outlineMat != null)
            {
                InstallOutlineFeature(outlineMat, style.sceneOutlineEnabled, style.ssaoIntensity);
            }

            ArenaAtmosphereBuilder.Apply(style);

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[P10-A] 三渲二画风已应用：材质色阶/冷暖阴影、高饱和日光、后处理、PC 全屏描边。");
        }

        [MenuItem("P1/画风/2. 开关全屏卡通描边", false, 11)]
        public static void ToggleSceneOutline()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null) return;

            style.sceneOutlineEnabled = !style.sceneOutlineEnabled;
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var feature = data != null ? FindOutlineFeature(data) : null;
            if (feature == null)
            {
                Debug.LogWarning("[P10-A] PC_Renderer 上还没有安装 P1 Toon Outline，先运行菜单 1。");
                return;
            }

            SetFeatureActive(feature, style.sceneOutlineEnabled);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[P10-A] 全屏卡通描边已{(style.sceneOutlineEnabled ? "开启" : "关闭")}。");
        }

        [MenuItem("P1/画风/3. 截取当前画风截图（Play 模式）", false, 12)]
        public static void CaptureStyleScreenshot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[P10-A] 请在 Play 模式运行，屏幕覆盖层 UI 才会进入截图。");
                return;
            }
            ScreenCapture.CaptureScreenshot("Logs/style_after_ui.png");
            Debug.Log("[P10-A] 已请求截图 Logs/style_after_ui.png（约 2 秒后落盘）。");
        }

        private static void WriteStylePreset(ArenaStyleSO style)
        {
            // 场地配色：提高明度与饱和度，让橙/蓝墨水在浅灰场地上更跳。
            style.groundBase = new Color(0.76f, 0.74f, 0.68f, 1f);
            style.groundSeam = new Color(0.52f, 0.50f, 0.45f, 1f);
            style.groundCliff = new Color(0.34f, 0.33f, 0.31f, 1f);
            style.concreteLight = new Color(0.80f, 0.78f, 0.72f, 1f);
            style.concreteMid = new Color(0.64f, 0.62f, 0.57f, 1f);
            style.concreteDark = new Color(0.42f, 0.41f, 0.38f, 1f);
            style.steel = new Color(0.42f, 0.47f, 0.56f, 1f);
            style.steelDark = new Color(0.24f, 0.27f, 0.32f, 1f);
            style.containerTeal = new Color(0.22f, 0.56f, 0.54f, 1f);
            style.containerRust = new Color(0.68f, 0.32f, 0.22f, 1f);
            style.containerSlate = new Color(0.36f, 0.47f, 0.62f, 1f);
            style.containerMustard = new Color(0.85f, 0.64f, 0.22f, 1f);
            style.boundaryConcrete = new Color(0.80f, 0.78f, 0.72f, 1f);
            style.boundaryTrim = new Color(0.55f, 0.53f, 0.48f, 1f);

            // 高饱和明亮日光 + 冷暖分离阴影。
            style.sunColor = new Color(1.00f, 0.97f, 0.91f, 1f);
            style.sunIntensity = 1.60f;
            style.sunEuler = new Vector3(52f, -38f, 0f);
            style.shadowStrength = 0.88f;
            style.ambientSky = new Color(0.36f, 0.50f, 0.76f, 1f);
            style.ambientEquator = new Color(0.56f, 0.54f, 0.50f, 1f);
            style.ambientGround = new Color(0.18f, 0.17f, 0.17f, 1f);
            style.ambientIntensity = 0.75f;
            style.reflectionIntensity = 0.35f;

            style.shadeThreshold = 0.50f;
            style.shadeSoftness = 0.03f;
            style.shadowTint = new Color(0.40f, 0.45f, 0.62f, 1f);
            style.shadowFloor = 0.42f;
            style.rimColor = new Color(0.75f, 0.86f, 1f, 1f);
            style.rimStrength = 0.18f;
            style.rimPower = 3f;
            style.specStrength = 0.5f;
            style.specThreshold = 0.95f;
            style.specSoftness = 0.02f;
            style.inkShadeThreshold = 0.48f;
            style.inkShadeSoftness = 0.08f;
            style.inkShadowFloor = 0.72f;
            style.characterShadowFloor = 0.55f;

            style.fogColor = new Color(0.62f, 0.74f, 0.86f, 1f);
            style.fogStartDistance = 45f;
            style.fogEndDistance = 135f;
            style.skyTop = new Color(0.16f, 0.46f, 0.86f, 1f);
            style.skyHorizon = new Color(0.80f, 0.89f, 0.97f, 1f);
            style.skyGround = new Color(0.52f, 0.60f, 0.66f, 1f);
            style.cloudStrength = 0.28f;

            style.tonemapMode = ArenaTonemapMode.Neutral;
            style.bloomIntensity = 0.35f;
            style.bloomThreshold = 1.05f;
            style.colorSaturation = 18f;
            style.colorContrast = 10f;
            style.vignetteIntensity = 0.12f;
            style.postExposure = 0.08f;
            style.ssaoIntensity = 0.28f;

            style.sceneOutlineEnabled = true;
            style.outlineColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            style.outlineThickness = 1.6f;
            style.outlineDepthThreshold = 0.02f;
            style.outlineNormalThreshold = 0.35f;
            style.outlineFadeStart = 25f;
            style.outlineFadeEnd = 65f;

            EditorUtility.SetDirty(style);
        }

        private static void ApplyEnvironmentMaterials(ArenaStyleSO style)
        {
            SetGroundMaterial(style);
            SetInkWallMaterial(style);

            SetObstacleMaterial("Mat_ObstacleWall", style.concreteLight, style.concreteDark, style);
            SetObstacleMaterial("Mat_Container_Teal", style.containerTeal,
                Color.Lerp(style.containerTeal, style.steelDark, 0.55f), style);
            SetObstacleMaterial("Mat_Container_Rust", style.containerRust,
                Color.Lerp(style.containerRust, style.steelDark, 0.55f), style);
            SetObstacleMaterial("Mat_Container_Slate", style.containerSlate,
                Color.Lerp(style.containerSlate, style.steelDark, 0.55f), style);
            SetObstacleMaterial("Mat_Container_Mustard", style.containerMustard,
                Color.Lerp(style.containerMustard, style.steelDark, 0.55f), style);
            SetObstacleMaterial("Mat_BoundaryWall", style.boundaryConcrete, style.boundaryTrim, style);
            SetObstacleMaterial("Mat_Prop_Concrete", style.concreteLight, style.concreteDark, style);
            SetObstacleMaterial("Mat_Prop_ConcreteDark", style.concreteMid, style.concreteDark, style);
            SetObstacleMaterial("Mat_Prop_Steel", style.steel, style.steelDark, style);
            SetObstacleMaterial("Mat_Prop_SteelDark", style.steelDark,
                Color.Lerp(style.steelDark, Color.black, 0.25f), style);
            SetObstacleMaterial("Mat_Prop_Wood", style.wood, style.woodDark, style);
            SetObstacleMaterial("Mat_Prop_WoodDark", style.woodDark,
                Color.Lerp(style.woodDark, Color.black, 0.2f), style);

            string[] propMaterials =
            {
                "Mat_Prop_Matte", "Mat_Prop_Metal", "Mat_Prop_Emissive",
                "Mat_Prop_Cloth", "Mat_Prop_Foliage"
            };
            foreach (string name in propMaterials)
            {
                var mat = LoadMaterial(name);
                if (mat != null) ApplyToonProperties(mat, style, 0.8f);
            }
        }

        private static void SetGroundMaterial(ArenaStyleSO style)
        {
            var mat = LoadMaterial("Mat_InkGround");
            if (mat == null) return;

            if (mat.HasProperty(GridLineColorId)) mat.SetColor(GridLineColorId, Linear(style.groundSeam));
            if (mat.HasProperty(CliffColorId)) mat.SetColor(CliffColorId, Linear(style.groundCliff));
            ApplyToonProperties(mat, style, 1f);
            if (mat.HasProperty(ShadeThresholdId)) mat.SetFloat(ShadeThresholdId, style.shadeThreshold);
            if (mat.HasProperty(ShadeSoftnessId)) mat.SetFloat(ShadeSoftnessId, style.shadeSoftness);
            if (mat.HasProperty(ShadowFloorId)) mat.SetFloat(ShadowFloorId, 0.55f);
            if (mat.HasProperty(InkShadeThresholdId)) mat.SetFloat(InkShadeThresholdId, style.inkShadeThreshold);
            if (mat.HasProperty(InkShadeSoftnessId)) mat.SetFloat(InkShadeSoftnessId, style.inkShadeSoftness);
            if (mat.HasProperty(InkShadowFloorId)) mat.SetFloat(InkShadowFloorId, style.inkShadowFloor);
            if (mat.HasProperty("_GelNoiseStrength")) mat.SetFloat("_GelNoiseStrength", 0.022f);
            if (mat.HasProperty("_InkGlow")) mat.SetFloat("_InkGlow", 0.20f);
            EditorUtility.SetDirty(mat);
        }

        private static void SetInkWallMaterial(ArenaStyleSO style)
        {
            var mat = LoadMaterial("Mat_InkWall");
            if (mat == null) return;
            ApplyToonProperties(mat, style, 0.6f);
            EditorUtility.SetDirty(mat);
        }

        private static void SetObstacleMaterial(string name, Color baseColor, Color trimColor, ArenaStyleSO style)
        {
            var mat = LoadMaterial(name);
            if (mat == null) return;

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Linear(baseColor));
            if (mat.HasProperty(TrimColorId)) mat.SetColor(TrimColorId, Linear(trimColor));
            ApplyToonProperties(mat, style, 1f);
            if (mat.HasProperty("_DetailNoiseStrength")) mat.SetFloat("_DetailNoiseStrength", 0.04f);
            EditorUtility.SetDirty(mat);
        }

        private static void ApplyCharacterMaterials(ArenaStyleSO style)
        {
            const string folder = "Assets/_Project/Materials/Characters";
            if (!AssetDatabase.IsValidFolder(folder)) return;

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            foreach (string guid in guids)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !mat.HasProperty(ShadowTintId)) continue;

                mat.SetColor(ShadowTintId, Linear(style.shadowTint));
                if (mat.HasProperty(ShadeThresholdId)) mat.SetFloat(ShadeThresholdId, style.shadeThreshold);
                if (mat.HasProperty(ShadowFloorId)) mat.SetFloat(ShadowFloorId, style.characterShadowFloor);
                if (mat.HasProperty(SpecThresholdId)) mat.SetFloat(SpecThresholdId, style.specThreshold);
                if (mat.HasProperty(SpecSoftnessId)) mat.SetFloat(SpecSoftnessId, style.specSoftness);
                if (mat.HasProperty(RimColorId)) mat.SetColor(RimColorId, Linear(style.rimColor));
                if (mat.HasProperty(RimPowerId)) mat.SetFloat(RimPowerId, style.rimPower);
                EditorUtility.SetDirty(mat);
            }
        }

        private static void ApplyToonProperties(Material mat, ArenaStyleSO style, float rimScale)
        {
            if (mat.HasProperty(ShadeThresholdId)) mat.SetFloat(ShadeThresholdId, style.shadeThreshold);
            if (mat.HasProperty(ShadeSoftnessId)) mat.SetFloat(ShadeSoftnessId, style.shadeSoftness);
            if (mat.HasProperty(ShadowTintId)) mat.SetColor(ShadowTintId, Linear(style.shadowTint));
            if (mat.HasProperty(ShadowFloorId)) mat.SetFloat(ShadowFloorId, style.shadowFloor);
            if (mat.HasProperty(RimColorId)) mat.SetColor(RimColorId, Linear(style.rimColor));
            if (mat.HasProperty(RimStrengthId)) mat.SetFloat(RimStrengthId, style.rimStrength * rimScale);
            if (mat.HasProperty(RimPowerId)) mat.SetFloat(RimPowerId, style.rimPower);
            if (mat.HasProperty(SpecStrengthId)) mat.SetFloat(SpecStrengthId, style.specStrength);
            if (mat.HasProperty(SpecThresholdId)) mat.SetFloat(SpecThresholdId, style.specThreshold);
            if (mat.HasProperty(SpecSoftnessId)) mat.SetFloat(SpecSoftnessId, style.specSoftness);
        }

        private static void ApplyPostProcessStyle(ArenaStyleSO style)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null) return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RendererPath);
            foreach (Object asset in assets)
            {
                if (asset == null || !asset.name.StartsWith("ScreenSpaceAmbientOcclusion")) continue;
                var so = new SerializedObject(asset);
                SerializedProperty intensity = so.FindProperty("m_Settings.Intensity");
                if (intensity != null)
                {
                    intensity.floatValue = style.ssaoIntensity;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                }
            }
        }

        private static Material EnsureOutlineMaterial(ArenaStyleSO style)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/_Project/Shaders/ToonOutline.shader");
            if (shader == null) shader = Shader.Find("P1/ToonOutline");
            if (shader == null)
            {
                Debug.LogError("[P10-A] P1/ToonOutline Shader 未编译成功，跳过全屏描边安装。");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_ToonOutline" };
                AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_OutlineColor", Linear(style.outlineColor));
            mat.SetFloat("_OutlineThickness", style.outlineThickness);
            mat.SetFloat("_DepthThreshold", style.outlineDepthThreshold);
            mat.SetFloat("_NormalThreshold", style.outlineNormalThreshold);
            mat.SetFloat("_FadeStart", style.outlineFadeStart);
            mat.SetFloat("_FadeEnd", style.outlineFadeEnd);
            mat.SetFloat("_SkyCutoff", 0.97f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void InstallOutlineFeature(Material outlineMat, bool enabled, float ssaoIntensity)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                Debug.LogError($"[P10-A] 找不到 {RendererPath}，无法安装全屏描边。");
                return;
            }

            if (File.Exists(RendererPath))
            {
                Directory.CreateDirectory("Logs");
                File.Copy(RendererPath, BackupPath, true);
            }

            FullScreenPassRendererFeature feature = FindOutlineFeature(data);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = OutlineFeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);

                var dataSo = new SerializedObject(data);
                SerializedProperty features = dataSo.FindProperty("m_RendererFeatures");
                SerializedProperty featureMap = dataSo.FindProperty("m_RendererFeatureMap");
                if (features == null)
                {
                    Debug.LogError("[P10-A] PC_Renderer 序列化结构异常，未安装描边。");
                    return;
                }

                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

                if (featureMap != null)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                    featureMap.arraySize = features.arraySize;
                    featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
                }
                dataSo.ApplyModifiedPropertiesWithoutUndo();
            }

            feature.passMaterial = outlineMat;
            feature.passIndex = 0;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            feature.fetchColorBuffer = true;
            feature.bindDepthStencilAttachment = false;

            SetFeatureActive(feature, enabled);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private static FullScreenPassRendererFeature FindOutlineFeature(UniversalRendererData data)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is FullScreenPassRendererFeature fullScreen &&
                    feature.name == OutlineFeatureName)
                {
                    return fullScreen;
                }
            }
            return null;
        }

        private static void SetFeatureActive(ScriptableRendererFeature feature, bool active)
        {
            var so = new SerializedObject(feature);
            SerializedProperty prop = so.FindProperty("m_Active");
            if (prop != null)
            {
                prop.boolValue = active;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(feature);
        }

        private static Material LoadMaterial(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");
        }

        private static Color Linear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }
    }
}