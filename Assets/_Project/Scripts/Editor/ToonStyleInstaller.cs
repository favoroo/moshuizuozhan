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
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        private const string PcRpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string OutlineMaterialPath = "Assets/_Project/Materials/Mat_ToonOutline.mat";
        private const string MobileOutlineMaterialPath = "Assets/_Project/Materials/Mat_ToonOutlineMobile.mat";
        private const string OutlineFeatureName = "P1 Toon Outline";
        private const string MobileOutlineFeatureName = "P1 Toon Outline Mobile";
        private const string BackupPath = "Logs/PC_Renderer.backup.asset";
        private const string MobileBackupPath = "Logs/Mobile_Renderer.backup.asset";

        /// <summary>
        /// 移动端是否安装「全屏描边」RendererFeature —— 默认 **关闭**（Android 切后台灰屏热修，2026-11-01）。
        ///
        /// 真机现象（v0.1.8 回归）：Android 切到别的 App 再切回来，3D 画面整片消失，
        /// 屏幕上只剩相机 clear color 被后处理上色后的底色（观感「全屏灰」），
        /// HUD / 触控 / 小地图 / 对局逻辑全部正常；v0.1.7 移动端 Renderer 里一个 Feature 都没有，同样操作正常。
        ///
        /// 原因：全屏 Pass 需要把当前画面拷进中间纹理再全屏重绘（`fetchColorBuffer = true` →
        /// `requiresIntermediateTexture`），并额外要求一次深度拷贝；Android 恢复时会重建 surface，
        /// 这条「中间纹理 + 最终呈现」链路在恢复后失效，后处理读到的只剩被清成 clear color 的缓冲。
        ///
        /// 所以移动端回到 v0.1.7 的直绘路径：角色自身的描边（`CharacterToonOutlined`）不受影响，
        /// 同时省掉每帧一次全屏颜色拷贝 + 深度拷贝，对 30 FPS 目标更友好。
        ///
        /// 需要对比验证时：把此开关改回 `true` 再跑菜单 `P1/画风/5`，即可恢复移动端全屏描边。
        ///
        /// 用 `static readonly` 而非 `const`：常量会让 `if (!EnableMobileFullScreenOutline)` 之后的
        /// 安装分支被判成不可达并报 CS0162 警告（本项目不接受「warning 而已」的交付）。
        /// </summary>
        private static readonly bool EnableMobileFullScreenOutline = false;

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
        private static readonly int EdgeLineColorId = Shader.PropertyToID("_EdgeLineColor");
        private static readonly int EdgeLineWidthId = Shader.PropertyToID("_EdgeLineWidth");
        private static readonly int EdgeLineStrengthId = Shader.PropertyToID("_EdgeLineStrength");
        private static readonly int TopLiftId = Shader.PropertyToID("_TopLift");

        // ---- P18 INKWAVE 质感画风：新贴图与新材质参数 ----
        private const string GroundAlbedoV1Path = "Assets/_Project/Art/Textures/Arena/ArenaGround_Albedo.png";
        private const string GroundAlbedoV2Path = "Assets/_Project/Art/Textures/Arena/ArenaGround_Albedo_V2.png";
        private const string GroundNormalPath = "Assets/_Project/Art/Textures/Arena/ArenaGround_N.png";
        private const string GroundRmoPath = "Assets/_Project/Art/Textures/Arena/ArenaGround_RMO.png";
        private const string InkGrainNPath = "Assets/_Project/Art/Textures/Ink/InkGrain_N.png";
        private const string InkGrainRmoPath = "Assets/_Project/Art/Textures/Ink/InkGrain_RMO.png";
        private const string ConcreteNPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Concrete_N.png";
        private const string ConcreteRmoPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Concrete_RMO.png";
        private const string MetalNPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Metal_N.png";
        private const string MetalRmoPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Metal_RMO.png";

        private static readonly int GroundTexId = Shader.PropertyToID("_GroundTex");
        private static readonly int GroundTexNId = Shader.PropertyToID("_GroundTexN");
        private static readonly int GroundTexRmoId = Shader.PropertyToID("_GroundTexRMO");
        private static readonly int GroundNormalStrengthId = Shader.PropertyToID("_GroundNormalStrength");
        private static readonly int GroundAoStrengthId = Shader.PropertyToID("_GroundAOStrength");
        private static readonly int InkGrainNId = Shader.PropertyToID("_InkGrainN");
        private static readonly int InkGrainRmoId = Shader.PropertyToID("_InkGrainRMO");
        private static readonly int InkGrainScaleId = Shader.PropertyToID("_InkGrainScale");
        private static readonly int InkGrainStrengthId = Shader.PropertyToID("_InkGrainStrength");
        private static readonly int InkRoughVarStrengthId = Shader.PropertyToID("_InkRoughVarStrength");
        private static readonly int CoffeeRingStrengthId = Shader.PropertyToID("_CoffeeRingStrength");
        private static readonly int EdgeSpeckleStrengthId = Shader.PropertyToID("_EdgeSpeckleStrength");
        private static readonly int SurfaceNId = Shader.PropertyToID("_SurfaceN");
        private static readonly int SurfaceRmoId = Shader.PropertyToID("_SurfaceRMO");
        private static readonly int SurfaceTexelScaleId = Shader.PropertyToID("_SurfaceTexelScale");
        private static readonly int SurfaceNormalStrengthId = Shader.PropertyToID("_SurfaceNormalStrength");
        private static readonly int SurfaceAoStrengthId = Shader.PropertyToID("_SurfaceAOStrength");
        private static readonly int SurfaceSmoothnessScaleId = Shader.PropertyToID("_SurfaceSmoothnessScale");
        private static readonly int WallWetnessId = Shader.PropertyToID("_WallWetness");
        private static readonly int WallEnvSpecId = Shader.PropertyToID("_WallEnvSpec");
        private static readonly int DampHaloStrengthId = Shader.PropertyToID("_DampHaloStrength");
        private static readonly int WallDripStrengthId = Shader.PropertyToID("_WallDripStrength");

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

        /// <summary>
        /// 移动端灰屏热修入口：把 Mobile_Renderer 上的「P1 Toon Outline Mobile」停用（幂等）。
        /// 只动这一个 Feature，不碰画风参数/材质/场景，可与菜单 5 之外的任何写入并行安全执行。
        /// </summary>
        [MenuItem("P1/画风/6. 应用移动端灰屏热修（停用移动端全屏描边）", false, 15)]
        public static void ApplyMobileResumeHotfix()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRendererPath);
            if (data == null)
            {
                Debug.LogError($"[P31] 找不到 {MobileRendererPath}，无法应用移动端灰屏热修。");
                return;
            }

            DisableMobileOutlineFeature(data);

            var pcData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var pcFeature = pcData != null ? FindOutlineFeature(pcData) : null;
            string pcState = pcFeature == null ? "未安装" : (IsFeatureActive(pcFeature) ? "已开启" : "已关闭");
            Debug.Log($"[P31] 移动端灰屏热修已应用：移动端全屏描边停用（PC 全屏描边保持{pcState}）。");
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

        [MenuItem("P1/画风/4. 应用画质增强（湿墨反光+微表面，保留明亮基调）", false, 13)]
        public static void ApplyInkwaveStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null)
            {
                Debug.LogError($"[P18] 找不到 {StylePath}，请先运行 P1/构建/1 生成配置。");
                return;
            }

            WriteStylePresetV2(style);
            ApplyEnvironmentMaterials(style, groundShadowFloor: 0.55f, inkGlow: 0.10f);
            ApplyInkwaveAssets(style);
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
            Debug.Log("[P18] 画质增强已应用：明亮基调不变，墨面获得湿墨环境反射与微表面颗粒，" +
                      "场景表面获得法线/粗糙度/接触遮蔽。回退纯旧档请运行 P1/画风/1。");
        }

        [MenuItem("P1/画风/5. 应用软化卡通参数（三阶光影+减淡描边+环境光增强）", false, 14)]
        public static void ApplySoftCartoonStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null)
            {
                Debug.LogError("[P31] 找不到 " + StylePath + "，请先运行 P1/构建/1 生成配置。");
                return;
            }

            WriteStylePresetSoftCartoon(style);
            ApplyEnvironmentMaterials(style);
            ApplyCharacterMaterials(style);
            ApplyAlbedoDetailMaps(style);
            ApplyPostProcessStyle(style);

            Material outlineMat = EnsureOutlineMaterial(style);
            if (outlineMat != null)
            {
                InstallOutlineFeature(outlineMat, style.sceneOutlineEnabled, style.ssaoIntensity);
            }

            InstallMobileOutlineFeature(style);

            InstallScreenFxFeature();

            ArenaAtmosphereBuilder.Apply(style);

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[P31] 软化卡通参数已应用：三阶柔和光影 + 减淡描边 + 环境光增强。");
        }

        /// <summary>
        /// P32 场景精修档：以 P31 软化卡通为基线，叠加一轮面向「场内+外围统一观感」的调色——
        /// 白云、更通透的水天交界、更清晰的地面接缝、掩体微提亮、水面反射收敛。
        /// 只改风格数值，不动布局/碰撞/玩法。
        /// </summary>
        [MenuItem("P1/画风/7. 应用场景精修档（白云+通透水天+接缝清晰+掩体提亮）", false, 16)]
        public static void ApplyScenePolishStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null)
            {
                Debug.LogError("[P32] 找不到 " + StylePath + "，请先运行 P1/构建/1 生成配置。");
                return;
            }

            WriteStylePresetSoftCartoon(style);
            WriteScenePolishDeltas(style);
            ApplyEnvironmentMaterials(style, groundShadowFloor: 0.55f, inkGlow: 0.10f);
            ApplyInkwaveAssets(style);
            ApplyCharacterMaterials(style);
            ApplyAlbedoDetailMaps(style);
            ApplyPostProcessStyle(style);

            Material outlineMat = EnsureOutlineMaterial(style);
            if (outlineMat != null)
            {
                InstallOutlineFeature(outlineMat, style.sceneOutlineEnabled, style.ssaoIntensity);
            }

            InstallMobileOutlineFeature(style);
            InstallScreenFxFeature();
            ArenaAtmosphereBuilder.Apply(style);

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[P32] 场景精修档已应用：白云/通透水天/接缝清晰/掩体提亮/水面反射收敛。");
        }

        /// <summary>P32 精修增量（在 P31 基线之上覆写，随预设档重放不会丢）。</summary>
        private static void WriteScenePolishDeltas(ArenaStyleSO style)
        {
            // 云朵由灰紫（0.55,0.52,0.62）提回近白，天空更晴朗。
            style.cloudColor = new Color(0.98f, 0.97f, 0.99f, 1f);
            // 太阳盘不过橘，贴合暖白日光基调。
            style.sunDisc = new Color(1f, 0.95f, 0.82f, 1f);
            // 地面接缝加深，远看场地分格结构可读（原值远看几乎消失）。
            style.groundSeam = new Color(0.44f, 0.42f, 0.38f, 1f);
            // 深水更蓝更亮、水天线更通透，消除远景「灰绿 muddy 带」。
            style.waterDeep = new Color(0.06f, 0.30f, 0.44f, 1f);
            style.waterHorizon = new Color(0.72f, 0.84f, 0.90f, 1f);
            // 天空盒地平线以下（240m 水面之外的露底区）同步提亮到水天线色系：
            // 原 slate 灰绿带在水天交界处形成一条脏带，提亮后远景融入薄雾。
            style.skyGround = new Color(0.66f, 0.76f, 0.84f, 1f);
            // 水面环境反射收敛：掠射角不再把反射内容铺成重复亮斑。
            style.waterReflectionStrength = 0.5f;
            // 中灰混凝土与掩体顶面微提亮，远看不再是「黑盒子」。
            style.concreteMid = new Color(0.68f, 0.66f, 0.61f, 1f);
            style.coverTopLift = 0.09f;
            EditorUtility.SetDirty(style);
        }

        /// <summary>
        /// P31 软化卡通预设：完整继承 P18 质感档（材质/湿墨/后处理），仅软化观感三要素——
        /// 1) Toon 色阶从硬切换改为柔和三阶；2) 全屏描边减淡（色值与粗细）；3) 环境光增强。
        /// 硬基础（涂装色/布局/反射体系）全部保留，回退旧档运行 P1/画风/4。
        /// </summary>
        private static void WriteStylePresetSoftCartoon(ArenaStyleSO style)
        {
            // 1) 继承 P18 完整质感档
            WriteStylePresetV2(style);

            // 2) 软化三渲二：更宽过渡带 + 阴影染色提亮 + 暗部不再死黑。
            //    三阶切换由 ToonLighting.hlsl 的 P1ToonTriBand 内部完成，此处只调数值。
            style.shadeSoftness = 0.10f;
            style.specSoftness = 0.04f;
            style.shadowTint = new Color(0.48f, 0.50f, 0.64f, 1f);
            style.shadowFloor = 0.50f;

            // 3) 描边减淡：近纯黑 → 深炭紫，1.6px 粗线 → 1.0px 细线，远处更早变细。
            //    P17-A 掩体边界线同色系同步减淡，避免"描边淡了但掩体一圈还糊着黑框"。
            style.outlineColor = new Color(0.14f, 0.12f, 0.18f, 1f);
            style.outlineThickness = 1.0f;
            style.outlineFadeStart = 18f;
            style.coverEdgeLineColor = new Color(0.14f, 0.12f, 0.18f, 1f);

            // 4) 环境光增强：暖地面反弹 + 强度提升，背光面呈"冷阴影 + 暖底"立体关系。
            style.ambientIntensity = 0.88f;
            style.ambientGround = new Color(0.28f, 0.22f, 0.18f, 1f);

            // 5) 后处理精调：饱和度回落保护队伍墨色、对比与暗角微增收拢视线。
            //    bloom 阈值必须保持 1.05 以上：角色受光面与受击白闪的 HDR 亮度实测约 0.9~1.06，
            //    阈值低于它会把每次受击/枪口闪光放大成角色身上的大团白光（2026-11-01 白光问题实测）。
            //    若出生台/警示灯需要更亮起辉，请提高这些材质自身的 emissive 强度，不要降全局阈值。
            style.colorSaturation = 10f;
            style.colorContrast = 12f;
            style.vignetteIntensity = 0.18f;
            style.bloomThreshold = 1.05f;
            style.bloomIntensity = 0.45f;

            EditorUtility.SetDirty(style);
        }

        /// <summary>
        /// P18 画质增强预设：**完整继承旧扁平哑光档的全部配色与光照基调**（用户明确要求保留明亮基调，
        /// 只解决"哑光塑料感"），在此之上仅追加质感参数——湿墨环境反射、墨面颗粒、咖啡环、
        /// 场景表面 PBR 与轻微分离调色。基调字段一个都不改。
        /// </summary>
        private static void WriteStylePresetV2(ArenaStyleSO style)
        {
            // 1) 原明亮基调（与菜单 1 完全一致）
            WriteStylePreset(style);

            // 2) P18 质感参数（不动基调，只加质感）
            style.envSpecStrength = 1.35f;
            style.envSunDiscPower = 900f;
            style.inkGrainScale = 1.6f;
            style.inkGrainStrength = 0.35f;
            style.inkRoughVarStrength = 0.5f;
            style.coffeeRingStrength = 0.55f;
            style.surfaceNormalStrength = 0.55f;
            style.surfaceAOStrength = 0.45f;
            style.surfaceSmoothnessScale = 1f;
            // 地面沿用原明亮 albedo（深炭紫重烘版仅作备选，用户已否决暗色调）
            style.useInkwaveGround = false;
            // 墨面明暗稍拉开放点深度（0.72 → 0.62，仍保亮），亮度由环境反射补
            style.inkShadowFloor = 0.62f;
            style.inkShadeSoftness = 0.10f;
            // 分离调色：阴影微冷、高光微暖，强度很低只做氛围
            style.splitToningStrength = 0.15f;

            EditorUtility.SetDirty(style);
        }

        /// <summary>
        /// P18：把程序化烘焙的 PBR 贴图与新质感参数落到各材质
        /// （贴图引用走 Unity API，不手改 .mat YAML）。
        /// </summary>
        private static void ApplyInkwaveAssets(ArenaStyleSO style)
        {
            Material ground = LoadMaterial("Mat_InkGround");
            if (ground != null)
            {
                Texture albedo = LoadTexture(style.useInkwaveGround ? GroundAlbedoV2Path : GroundAlbedoV1Path);
                if (albedo != null) ground.SetTexture(GroundTexId, albedo);
                SetTextureIf(ground, GroundTexNId, GroundNormalPath);
                SetTextureIf(ground, GroundTexRmoId, GroundRmoPath);
                SetTextureIf(ground, InkGrainNId, InkGrainNPath);
                SetTextureIf(ground, InkGrainRmoId, InkGrainRmoPath);
                SetFloatIf(ground, GroundNormalStrengthId, style.surfaceNormalStrength);
                SetFloatIf(ground, GroundAoStrengthId, style.surfaceAOStrength);
                SetFloatIf(ground, InkGrainScaleId, style.inkGrainScale);
                SetFloatIf(ground, InkGrainStrengthId, style.inkGrainStrength);
                SetFloatIf(ground, InkRoughVarStrengthId, style.inkRoughVarStrength);
                SetFloatIf(ground, CoffeeRingStrengthId, style.coffeeRingStrength);
                SetFloatIf(ground, EdgeSpeckleStrengthId, 0.3f);
                EditorUtility.SetDirty(ground);
            }

            Material wall = LoadMaterial("Mat_InkWall");
            if (wall != null)
            {
                SetTextureIf(wall, InkGrainNId, InkGrainNPath);
                SetTextureIf(wall, InkGrainRmoId, InkGrainRmoPath);
                SetFloatIf(wall, InkGrainScaleId, style.inkGrainScale);
                SetFloatIf(wall, InkGrainStrengthId, style.inkGrainStrength);
                SetFloatIf(wall, WallEnvSpecId, 0.8f);
                SetFloatIf(wall, DampHaloStrengthId, 0.5f);
                SetFloatIf(wall, WallDripStrengthId, 0.4f);
                SetFloatIf(wall, WallWetnessId, 0.6f);
                EditorUtility.SetDirty(wall);
            }

            // 掩体与道具：混凝土族 / 钢板族分别赋贴图；木箱与布料无对应贴图，保持原样。
            string[] concreteMaterials =
            {
                "Mat_ObstacleWall", "Mat_BoundaryWall", "Mat_Prop_Concrete",
                "Mat_Prop_ConcreteDark", "Mat_Prop_Matte"
            };
            string[] metalMaterials =
            {
                "Mat_Container_Teal", "Mat_Container_Rust", "Mat_Container_Slate",
                "Mat_Container_Mustard", "Mat_Prop_Steel", "Mat_Prop_SteelDark", "Mat_Prop_Metal"
            };
            foreach (string name in concreteMaterials)
            {
                ApplySurfacePbr(LoadMaterial(name), ConcreteNPath, ConcreteRmoPath,
                    texelScale: 0.7f, smoothnessScale: 0.7f, style);
            }
            foreach (string name in metalMaterials)
            {
                ApplySurfacePbr(LoadMaterial(name), MetalNPath, MetalRmoPath,
                    texelScale: 0.5f, smoothnessScale: 1.25f, style);
            }

            // P18 后续修正：出生盘保持中性钢灰，不再回灌队色（队别信息由地面导向标/A-B 标记承担）。
            // 统一走 ProjectScaffolder 的材质写入逻辑，避免两处调色规则再次分叉。
            ProjectScaffolder.RefreshSpawnPadMaterials(style);
        }

        /// <summary>多地图材质可选的表面细节图族（法线 + RMO）。</summary>
        public enum StyleSurfaceKind
        {
            None = 0,
            Concrete = 1,
            Metal = 2,
            Wood = 3
        }

        /// <summary>
        /// 给多地图材质挂上表面细节图（法线 + RMO）。
        /// **必须调用**：ObstacleDitherLit 的 _SurfaceRMO 默认是 black，G=0 时 AO 被 lerp 到 0.35，
        /// 整块材质的亮度只剩 35%（实测新图船甲板/船壳比同色地面暗一大截，读作"酒红塑料"）。
        /// </summary>
        public static void ApplySurfaceToMaterial(Material mat, StyleSurfaceKind kind, ArenaStyleSO style,
            float texelScale = 0.5f, float smoothnessScale = 1.0f)
        {
            if (mat == null || style == null || kind == StyleSurfaceKind.None) return;
            string nPath;
            string rmoPath;
            switch (kind)
            {
                case StyleSurfaceKind.Metal:
                    nPath = MetalNPath;
                    rmoPath = MetalRmoPath;
                    break;
                case StyleSurfaceKind.Wood:
                    nPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Wood_N.png";
                    rmoPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Wood_RMO.png";
                    break;
                default:
                    nPath = ConcreteNPath;
                    rmoPath = ConcreteRmoPath;
                    break;
            }
            ApplySurfacePbr(mat, nPath, rmoPath, texelScale, smoothnessScale, style);
        }

        private static void ApplySurfacePbr(
            Material mat, string normalPath, string rmoPath, float texelScale, float smoothnessScale, ArenaStyleSO style)
        {
            if (mat == null) return;
            SetTextureIf(mat, SurfaceNId, normalPath);
            SetTextureIf(mat, SurfaceRmoId, rmoPath);
            SetFloatIf(mat, SurfaceTexelScaleId, texelScale);
            SetFloatIf(mat, SurfaceNormalStrengthId, style.surfaceNormalStrength);
            SetFloatIf(mat, SurfaceAoStrengthId, style.surfaceAOStrength);
            SetFloatIf(mat, SurfaceSmoothnessScaleId, smoothnessScale);
            EditorUtility.SetDirty(mat);
        }

        private static void SetTextureIf(Material mat, int id, string path)
        {
            if (mat == null || !mat.HasProperty(id)) return;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) mat.SetTexture(id, texture);
        }

        private static void SetFloatIf(Material mat, int id, float value)
        {
            if (mat != null && mat.HasProperty(id)) mat.SetFloat(id, value);
        }

        private static Texture LoadTexture(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            style.shadowStrength = 0.94f;
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
            // 雾距外推：45/135 把 60m 外的吊机/楼群洗成奶白，远景没层次（"廉价感"来源之一）
            style.fogStartDistance = 65f;
            style.fogEndDistance = 210f;
            style.skyTop = new Color(0.16f, 0.46f, 0.86f, 1f);
            style.skyHorizon = new Color(0.80f, 0.89f, 0.97f, 1f);
            style.skyGround = new Color(0.52f, 0.60f, 0.66f, 1f);
            style.cloudStrength = 0.28f;

            style.tonemapMode = ArenaTonemapMode.Neutral;
            style.bloomIntensity = 0.45f;
            // 与 ArenaStyleSO 默认值保持一致：0.98 会把枪口/命中加法闪光放大成角色白光
            style.bloomThreshold = 1.05f;
            style.colorSaturation = 14f;
            style.colorContrast = 16f;
            style.vignetteIntensity = 0.12f;
            style.postExposure = 0.12f;
            style.ssaoIntensity = 1.15f;

            style.sceneOutlineEnabled = true;
            style.outlineColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            style.outlineThickness = 1.6f;
            style.outlineDepthThreshold = 0.02f;
            style.outlineNormalThreshold = 0.35f;
            style.outlineFadeStart = 25f;
            style.outlineFadeEnd = 65f;

            // P17-A 掩体边界线：与全屏描边同一深色系。墨色统一后掩体与地面同色，
            // 这圈线是辨认掩体轮廓的主要线索（压在墨水叠加之后，涂满墨仍可见）。
            style.coverEdgeLineColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            style.coverEdgeLineWidth = 0.05f;
            style.coverEdgeLineStrength = 0.85f;
            style.coverTopLift = 0.06f;

            // P29 渲染基础设施新字段默认值
            style.postProcessingEnabled = true;
            style.inkProbeStrength = 1.15f;
            style.inkCoatStrength = 1.5f;
            style.inkCoatRoughness = 0.06f;
            style.inkEdgeRimStrength = 0.8f;
            style.inkEdgeSmoothStrength = 0.7f;
            style.inkTopDetailStrength = 1f;
            style.wallDripSpeed = 1.3f;
            style.waterReflectionStrength = 0.8f;
            style.emissiveBloomStrength = 2f;
            style.skySunIntensity = 2.2f;

            EditorUtility.SetDirty(style);
        }

        private static void ApplyEnvironmentMaterials(ArenaStyleSO style, float groundShadowFloor = 0.55f, float inkGlow = 0.20f)
        {
            SetGroundMaterial(style, groundShadowFloor, inkGlow);
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

        private static void SetGroundMaterial(ArenaStyleSO style, float groundShadowFloor = 0.55f, float inkGlow = 0.20f)
        {
            var mat = LoadMaterial("Mat_InkGround");
            if (mat == null) return;

            if (mat.HasProperty(GridLineColorId)) mat.SetColor(GridLineColorId, Linear(style.groundSeam));
            if (mat.HasProperty(CliffColorId)) mat.SetColor(CliffColorId, Linear(style.groundCliff));
            ApplyToonProperties(mat, style, 1f);
            if (mat.HasProperty(ShadeThresholdId)) mat.SetFloat(ShadeThresholdId, style.shadeThreshold);
            if (mat.HasProperty(ShadeSoftnessId)) mat.SetFloat(ShadeSoftnessId, style.shadeSoftness);
            if (mat.HasProperty(ShadowFloorId)) mat.SetFloat(ShadowFloorId, groundShadowFloor);
            if (mat.HasProperty(InkShadeThresholdId)) mat.SetFloat(InkShadeThresholdId, style.inkShadeThreshold);
            if (mat.HasProperty(InkShadeSoftnessId)) mat.SetFloat(InkShadeSoftnessId, style.inkShadeSoftness);
            if (mat.HasProperty(InkShadowFloorId)) mat.SetFloat(InkShadowFloorId, style.inkShadowFloor);
            if (mat.HasProperty("_GelNoiseStrength")) mat.SetFloat("_GelNoiseStrength", 0.022f);
            if (mat.HasProperty("_InkGlow")) mat.SetFloat("_InkGlow", inkGlow);
            EditorUtility.SetDirty(mat);
        }

        private static void SetInkWallMaterial(ArenaStyleSO style)
        {
            var mat = LoadMaterial("Mat_InkWall");
            if (mat == null) return;
            ApplyToonProperties(mat, style, 0.6f);
            // P17-A：涂墨立面边界线与掩体基面同参数，两处边线拼成完整轮廓
            if (mat.HasProperty(EdgeLineColorId)) mat.SetColor(EdgeLineColorId, Linear(style.coverEdgeLineColor));
            if (mat.HasProperty(EdgeLineWidthId)) mat.SetFloat(EdgeLineWidthId, style.coverEdgeLineWidth);
            if (mat.HasProperty(EdgeLineStrengthId)) mat.SetFloat(EdgeLineStrengthId, style.coverEdgeLineStrength);
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
            if (mat.HasProperty(EdgeLineColorId)) mat.SetColor(EdgeLineColorId, Linear(style.coverEdgeLineColor));
            if (mat.HasProperty(EdgeLineWidthId)) mat.SetFloat(EdgeLineWidthId, style.coverEdgeLineWidth);
            if (mat.HasProperty(EdgeLineStrengthId)) mat.SetFloat(EdgeLineStrengthId, style.coverEdgeLineStrength);
            if (mat.HasProperty(TopLiftId)) mat.SetFloat(TopLiftId, style.coverTopLift);
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
                if (mat == null) continue;

                // 墨罐玻璃（URP Lit，不带 ShadowTint）：压哑并关掉镜面高光/环境反射。
                // smoothness 0.95 的紧凑高光在阳光下会被 Bloom 放大成后背墨罐上的一团白闪。
                if (mat.name.StartsWith("Mat_TankGlass"))
                {
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.20f);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                    if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
                    if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
                    mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    EditorUtility.SetDirty(mat);
                    continue;
                }

                if (!mat.HasProperty(ShadowTintId)) continue;

                // 墨罐液面与角色共用 Mat_TeamLiquid：压哑高光，避免液面上的镜面高光偶尔闪白
                if (mat.name.StartsWith("Mat_TeamLiquid") && mat.HasProperty("_Smoothness"))
                {
                    mat.SetFloat("_Smoothness", 0.50f);
                }

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

        /// <summary>
        /// P31 把程序化烘焙的织物 albedo 指到角色与布料道具材质的可选 _BaseMap 槽。
        /// 贴图为 ±3.5% 低对比肌理（tools/bake_prop_surfaces.py 生成），只加"生活痕迹"不抢角色色。
        /// 找不到贴图时静默跳过（先跑 baker），不影响其余画风参数落地。
        /// </summary>
        private static void ApplyAlbedoDetailMaps(ArenaStyleSO style)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_Project/Art/Textures/Surfaces/Surfaces_Cloth_Albedo.png");
            if (tex == null)
            {
                Debug.LogWarning("[P31] 找不到 Surfaces_Cloth_Albedo.png，跳过 albedo 细节层" +
                                 "（请先运行 python3 tools/bake_prop_surfaces.py）。");
                return;
            }

            int characterCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Materials/Characters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                string shaderName = mat.shader != null ? mat.shader.name : "";
                if (shaderName != "P1/CharacterToon" && shaderName != "P1/CharacterToonOutlined") continue;
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_DetailNormalStrength", 0.3f);
                EditorUtility.SetDirty(mat);
                characterCount++;
            }

            var clothProp = LoadMaterial("Mat_Prop_Cloth");
            if (clothProp != null && clothProp.HasProperty("_BaseMap"))
            {
                clothProp.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(clothProp);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[P31] albedo 细节层已应用：{characterCount} 个角色材质 + Mat_Prop_Cloth。");
        }

        /// <summary>
        /// 把画风档位的三渲二参数写到任意材质（多地图材质集共用）。
        /// 新地图的 Mat_GH_* 不经过 <see cref="ApplyEnvironmentMaterials"/> 的固定路径，必须显式调用本方法，
        /// 否则材质会停在 Shader 默认阴影底线 0.42，背光面被压成脏紫（实际症状：船甲板读作"酒红塑料"）。
        /// </summary>
        public static void ApplyStyleToMaterial(Material mat, ArenaStyleSO style, float rimScale = 1.0f)
        {
            if (mat == null || style == null) return;
            ApplyToonProperties(mat, style, rimScale);
            if (mat.HasProperty(EdgeLineColorId)) mat.SetColor(EdgeLineColorId, Linear(style.coverEdgeLineColor));
            if (mat.HasProperty(EdgeLineWidthId)) mat.SetFloat(EdgeLineWidthId, style.coverEdgeLineWidth);
            if (mat.HasProperty(EdgeLineStrengthId)) mat.SetFloat(EdgeLineStrengthId, style.coverEdgeLineStrength);
            if (mat.HasProperty(TopLiftId)) mat.SetFloat(TopLiftId, style.coverTopLift);
            EditorUtility.SetDirty(mat);
        }

        /// <summary>地面材质的画风参数（含墨水色阶与胶质内发光），供多地图的 _InkGround 变体复用。</summary>
        public static void ApplyGroundStyleToMaterial(Material mat, ArenaStyleSO style,
            float groundShadowFloor = 0.55f, float inkGlow = 0.20f)
        {
            if (mat == null || style == null) return;
            if (mat.HasProperty(GridLineColorId)) mat.SetColor(GridLineColorId, Linear(style.groundSeam));
            if (mat.HasProperty(CliffColorId)) mat.SetColor(CliffColorId, Linear(style.groundCliff));
            ApplyToonProperties(mat, style, 1f);
            if (mat.HasProperty(ShadeThresholdId)) mat.SetFloat(ShadeThresholdId, style.shadeThreshold);
            if (mat.HasProperty(ShadeSoftnessId)) mat.SetFloat(ShadeSoftnessId, style.shadeSoftness);
            if (mat.HasProperty(ShadowFloorId)) mat.SetFloat(ShadowFloorId, groundShadowFloor);
            if (mat.HasProperty(InkShadeThresholdId)) mat.SetFloat(InkShadeThresholdId, style.inkShadeThreshold);
            if (mat.HasProperty(InkShadeSoftnessId)) mat.SetFloat(InkShadeSoftnessId, style.inkShadeSoftness);
            if (mat.HasProperty(InkShadowFloorId)) mat.SetFloat(InkShadowFloorId, style.inkShadowFloor);
            if (mat.HasProperty("_GelNoiseStrength")) mat.SetFloat("_GelNoiseStrength", 0.022f);
            if (mat.HasProperty("_InkGlow")) mat.SetFloat("_InkGlow", inkGlow);
            EditorUtility.SetDirty(mat);
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
            if (data != null)
            {
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RendererPath);
                foreach (Object asset in assets)
                {
                    if (asset == null || !asset.name.StartsWith("ScreenSpaceAmbientOcclusion")) continue;
                    var so = new SerializedObject(asset);
                    bool modified = false;

                    SerializedProperty intensity = so.FindProperty("m_Settings.Intensity");
                    if (intensity != null)
                    {
                        intensity.floatValue = style.ssaoIntensity;
                        modified = true;
                    }

                    SerializedProperty radius = so.FindProperty("m_Settings.Radius");
                    if (radius != null)
                    {
                        radius.floatValue = 0.55f;
                        modified = true;
                    }

                    SerializedProperty directLighting = so.FindProperty("m_Settings.DirectLightingStrength");
                    if (directLighting != null)
                    {
                        // P31：0.35 → 0.30，SSAO 阴影少压亮面，配合三阶柔和光影更通透。
                        directLighting.floatValue = 0.30f;
                        modified = true;
                    }

                    // P31：NormalSamples Medium(1) → High(2)。枚举定义见
                    // ScreenSpaceAmbientOcclusion.cs NormalQuality { Low, Medium, High }。
                    // BlurQuality 保持 0 = Bilateral（BlurQualityOptions 枚举 0 即最高档，勿改 1）。
                    SerializedProperty normalSamples = so.FindProperty("m_Settings.NormalSamples");
                    if (normalSamples != null)
                    {
                        normalSamples.intValue = 2;
                        modified = true;
                    }

                    if (modified)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(asset);
                    }
                }
            }

            var rpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRpAssetPath);
            if (rpAsset != null)
            {
                var rpSo = new SerializedObject(rpAsset);
                bool rpModified = false;

                SerializedProperty colorGrading = rpSo.FindProperty("m_ColorGradingMode");
                if (colorGrading != null)
                {
                    colorGrading.intValue = 1; // HighDynamicRange (HDR)
                    rpModified = true;
                }
                else
                {
                    Debug.LogWarning("[ToonStyleInstaller] 在 PC_RPAsset.asset 中未找到 m_ColorGradingMode 属性。");
                }

                // P31 桌面阴影质量：2048→4096、距离 40→48、级联 2→3。
                // 40×56m 场地中景阴影边缘更干净，三级级联减少远处阴影模糊。
                SerializedProperty mainShadowRes = rpSo.FindProperty("m_MainLightShadowmapResolution");
                if (mainShadowRes != null)
                {
                    mainShadowRes.intValue = 4096;
                    rpModified = true;
                }

                SerializedProperty shadowDistance = rpSo.FindProperty("m_ShadowDistance");
                if (shadowDistance != null)
                {
                    shadowDistance.floatValue = 48f;
                    rpModified = true;
                }

                SerializedProperty cascadeCount = rpSo.FindProperty("m_ShadowCascadeCount");
                if (cascadeCount != null)
                {
                    cascadeCount.intValue = 3;
                    rpModified = true;
                }

                if (rpModified)
                {
                    rpSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rpAsset);
                }
            }
            else
            {
                Debug.LogWarning($"[ToonStyleInstaller] 找不到 {PcRpAssetPath}。");
            }

            // P31 移动端：开启低质量软阴影（SoftShadowsSupported 是总开关，
            // 移动端画质档 SoftShadowQuality 已是 Low）。描边 Feature 由 InstallMobileOutlineFeature 安装。
            var mobileRp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAssetPath);
            if (mobileRp != null)
            {
                var mSo = new SerializedObject(mobileRp);
                SerializedProperty softShadows = mSo.FindProperty("m_SoftShadowsSupported");
                if (softShadows != null)
                {
                    softShadows.intValue = 1;
                    mSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mobileRp);
                }
            }
            else
            {
                Debug.LogWarning($"[ToonStyleInstaller] 找不到 {MobileRpAssetPath}。");
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

            InstallOutlineFeatureOn(data, RendererPath, BackupPath, OutlineFeatureName,
                outlineMat, enabled, useNormals: true);
        }

        /// <summary>
        /// P31 移动端 mini 描边：与 PC 同一 ToonOutline shader，但——
        /// 1) 强度降到 65%（色值直接乘系数，全 edge 处也不会糊成黑块）；
        /// 2) 只保留深度边缘（_NormalThreshold = 2 超出法线差上限 1，法线项恒 0），
        ///    每像素省 4 次法线纹理采样；
        /// 3) 线宽 0.7px、距离淡化更早开始（14m），控制移动端像素与带宽开销。
        /// 与 PC 描边相互独立：单独材质 + 单独 RendererFeature，互不影响。
        ///
        /// 2026-11-01 热修：<see cref="EnableMobileFullScreenOutline"/> 默认 false。
        /// 关闭时这里不再安装/更新 Feature，并把 v0.1.8 已装上的那份**停用**（保留对象便于复现），
        /// 保证任何一次菜单写入（1/4/5）都不会再把移动端推回「全屏 Pass」路径。
        /// </summary>
        private static void InstallMobileOutlineFeature(ArenaStyleSO style)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRendererPath);
            if (data == null)
            {
                Debug.LogWarning($"[P31] 找不到 {MobileRendererPath}，跳过移动端描边安装。");
                return;
            }

            if (!EnableMobileFullScreenOutline)
            {
                DisableMobileOutlineFeature(data);
                return;
            }

            Material mobileMat = EnsureMobileOutlineMaterial(style);
            if (mobileMat == null) return;

            InstallOutlineFeatureOn(data, MobileRendererPath, MobileBackupPath, MobileOutlineFeatureName,
                mobileMat, enabled: style.sceneOutlineEnabled, useNormals: false);
        }

        /// <summary>停用移动端全屏描边 Feature（幂等）：Android 切后台灰屏热修用。</summary>
        private static void DisableMobileOutlineFeature(UniversalRendererData data)
        {
            FullScreenPassRendererFeature feature = FindOutlineFeature(data, MobileOutlineFeatureName);
            if (feature == null)
            {
                Debug.Log("[P31] 移动端全屏描边：未安装，保持 v0.1.7 直绘路径（Android 切后台灰屏热修）。");
                return;
            }

            if (!IsFeatureActive(feature))
            {
                Debug.Log("[P31] 移动端全屏描边：已是停用状态（Android 切后台灰屏热修）。");
                return;
            }

            SetFeatureActive(feature, false);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[P31] 移动端全屏描边已停用：移动端回到无全屏 Pass 的直绘路径（Android 切后台灰屏热修）。");
        }

        private static Material EnsureMobileOutlineMaterial(ArenaStyleSO style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/_Project/Shaders/ToonOutline.shader");
            if (shader == null) shader = Shader.Find("P1/ToonOutline");
            if (shader == null)
            {
                Debug.LogError("[P31] P1/ToonOutline Shader 未编译成功，跳过移动端描边材质。");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MobileOutlineMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_ToonOutlineMobile" };
                AssetDatabase.CreateAsset(mat, MobileOutlineMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Color dim = Linear(style.outlineColor) * 0.65f;
            mat.SetColor("_OutlineColor", dim);
            mat.SetFloat("_OutlineThickness", 0.7f);
            mat.SetFloat("_DepthThreshold", style.outlineDepthThreshold);
            mat.SetFloat("_NormalThreshold", 2.0f); // > normalDiff 上限 1.0，禁用 normal 边缘项
            mat.SetFloat("_FadeStart", 14f);
            mat.SetFloat("_FadeEnd", 45f);
            mat.SetFloat("_SkyCutoff", 0.97f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>把 ToonOutline FullScreenPass 幂等挂到指定 RendererData（PC/移动共用）。</summary>
        private static void InstallOutlineFeatureOn(
            UniversalRendererData data, string rendererPath, string backupPath, string featureName,
            Material outlineMat, bool enabled, bool useNormals)
        {
            ScriptableRenderPassInput requirements = useNormals
                ? ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal
                : ScriptableRenderPassInput.Depth;
            InstallFullScreenFeature(data, rendererPath, backupPath, featureName, outlineMat, enabled, requirements);
        }

        /// <summary>
        /// P31 ScreenFx（受击色差 kick）只挂 PC_Renderer：全屏颜色拷贝，不需要深度/法线输入。
        /// 材质资产在 Resources/ 下，运行时 ScreenFxController 加载同一资产写参数。
        /// </summary>
        private static void InstallScreenFxFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                Debug.LogWarning("[P31] 找不到 PC_Renderer，跳过 ScreenFx 安装。");
                return;
            }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Resources/ScreenFx.shader");
            if (shader == null) shader = Shader.Find("P1/ScreenFx");
            if (shader == null)
            {
                Debug.LogWarning("[P31] P1/ScreenFx Shader 未编译，跳过 ScreenFx 安装。");
                return;
            }

            const string matPath = "Assets/_Project/Resources/Mat_ScreenFx.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "Mat_ScreenFx" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.SetFloat("_ChromaAmount", 0f);
            EditorUtility.SetDirty(mat);

            InstallFullScreenFeature(data, RendererPath, BackupPath, "P1 ScreenFx",
                mat, enabled: true, requirements: ScriptableRenderPassInput.None);
        }

        /// <summary>通用 FullScreenPass 安装：按 featureName 幂等创建/更新，requirements 显式传入。</summary>
        private static void InstallFullScreenFeature(
            UniversalRendererData data, string rendererPath, string backupPath, string featureName,
            Material passMaterial, bool enabled, ScriptableRenderPassInput requirements)
        {
            if (File.Exists(rendererPath))
            {
                Directory.CreateDirectory("Logs");
                File.Copy(rendererPath, backupPath, true);
            }

            FullScreenPassRendererFeature feature = FindOutlineFeature(data, featureName);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = featureName;
                AssetDatabase.AddObjectToAsset(feature, data);

                var dataSo = new SerializedObject(data);
                SerializedProperty features = dataSo.FindProperty("m_RendererFeatures");
                SerializedProperty featureMap = dataSo.FindProperty("m_RendererFeatureMap");
                if (features == null)
                {
                    Debug.LogError($"[ToonStyleInstaller] {rendererPath} 序列化结构异常，未安装 {featureName}。");
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

            feature.passMaterial = passMaterial;
            feature.passIndex = 0;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.requirements = requirements;
            feature.fetchColorBuffer = true;
            feature.bindDepthStencilAttachment = false;

            SetFeatureActive(feature, enabled);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private static FullScreenPassRendererFeature FindOutlineFeature(UniversalRendererData data)
        {
            return FindOutlineFeature(data, OutlineFeatureName);
        }

        private static FullScreenPassRendererFeature FindOutlineFeature(UniversalRendererData data, string featureName)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is FullScreenPassRendererFeature fullScreen &&
                    feature.name == featureName)
                {
                    return fullScreen;
                }
            }
            return null;
        }

        private static bool IsFeatureActive(ScriptableRendererFeature feature)
        {
            var so = new SerializedObject(feature);
            SerializedProperty prop = so.FindProperty("m_Active");
            return prop == null || prop.boolValue;
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