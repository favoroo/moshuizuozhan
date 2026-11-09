using P1.Environment;
using P1.Ink;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace P1.Editor
{
    /// <summary>
    /// 2026-11-09 画面质感调优档：只做「观察得到的差异」所需的少量精确改动，
    /// 不重跑整档画风预设（避免把材质上已调好的其它数值一起冲掉）。
    ///
    /// 五项：
    /// 1. 后处理曝光从 +0.12 EV 收到 -0.10 EV、对比 18，并给低饱和区加 satVsSat vibrance
    ///    （中性表面提彩、队色墨保护）；
    /// 2. 阴影下限整体下压（地面 0.36 / 道具 0.40 / 角色 0.46），把「亮底」保住的同时
    ///    让投射阴影和背光面真正暗下去 —— 画面平的主因是暗部被 shadowFloor 抬得太高；
    /// 3. 墨面清漆高光加保色相软肩（P1InkHeadroom）+ 墨缘背光侧压暗（厚度感），
    ///    `_InkGlow` 0.10 → 0.04、场景 `inkGlow` 0.20 → 0.06，把被内发光抹平的体积找回来；
    /// 4. SSAO 改 AfterOpaque：本项目自定义 Toon shader 不采样 _ScreenSpaceOcclusionTexture，
    ///    AfterOpaque=0 时 SSAO 对场景完全无效。PC 用 DepthNormals 源（法线更准），
    ///    移动端原本没挂 SSAO，这里补一个 Depth 源 + 半分辨率 + 低采样的低成本档
    ///    （Depth 源不需要额外跑一遍 DepthNormals 几何 prepass，只多一次深度拷贝）；
    /// 5. 墨迹图集开启 mipmap（0.2s 节流），消除远景墨面摩尔纹；
    /// 6. 移动端调色模式 LDR → HDR：PC 是 HDR 调色，移动端用 LDR 会被先裁高光再调色，
    ///    同一个 Neutral tonemap + 负曝光在两端会读出不同对比，属于画质不同步而不是降档。
    ///
    /// 幂等：可重复运行；想回退请重跑对应画风档菜单（会覆盖这里的数值）。
    /// </summary>
    public static class VisualPolishTools
    {
        private const string StylePath = "Assets/_Project/ScriptableObjects/ArenaStyle.asset";
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        private const string PcRpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string GroundMaterialPath = "Assets/_Project/Materials/Mat_InkGround.mat";
        private const string InkConfigPath = "Assets/_Project/ScriptableObjects/InkGridConfig.asset";

        private const string SsaoFeatureTypeName =
            "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";

        private const string MaterialsRoot = "Assets/_Project/Materials";

        private static readonly int InkGlowId = Shader.PropertyToID("_InkGlow");
        private static readonly int InkShadowFloorId = Shader.PropertyToID("_InkShadowFloor");
        private static readonly int ContactShadowStrengthId = Shader.PropertyToID("_ContactShadowStrength");
        private static readonly int ShadowFloorId = Shader.PropertyToID("_ShadowFloor");
        private static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");

        [MenuItem("P1/画风/8. 应用质感调优档（曝光/墨膜/SSAO/图集mip）", false, 17)]
        public static void ApplyVisualPolish()
        {
            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            if (style == null)
            {
                Debug.LogError($"[质感调优] 找不到 {StylePath}，请先运行 P1/构建/1。");
                return;
            }

            // 1. 后处理：曝光 / 对比 / vibrance / 墨面阴影下限
            style.postExposure = -0.10f;
            style.colorContrast = 18f;
            style.colorVibrance = 0.30f;
            style.inkShadowFloor = 0.50f;
            style.shadowFloor = 0.40f;
            style.characterShadowFloor = 0.46f;
            style.shadowTint = new Color(0.44f, 0.48f, 0.62f, 1f);
            EditorUtility.SetDirty(style);
            ArenaAtmosphereBuilder.ApplyPostProcess(style);

            // 2. 三张地图的墨面材质（Mat_InkGround / Mat_GH_InkGround / Mat_SP_InkGround）一起调：
            //    内发光收小、墨面阴影下限下压（清漆/软肩已在高光侧补回通透感）。
            int groundCount = ApplyInkGroundMaterialDeltas();

            ApplyShadowFloorToToonMaterials();

            // 3. 场景推给 InkWall / 道具顶面的全局内发光（0.20 会把墙面墨刷成平涂）
            InkSurfaceController surface = Object.FindAnyObjectByType<InkSurfaceController>();
            if (surface != null)
            {
                var so = new SerializedObject(surface);
                SerializedProperty glow = so.FindProperty("inkGlow");
                if (glow != null)
                {
                    glow.floatValue = 0.06f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(surface);
                }
            }

            // 4. SSAO：AfterOpaque + 轻微收敛强度（全屏乘算，1.15 会压脏墨面）。
            //    PC 维持 DepthNormals 源；移动端此前完全没有 SSAO feature，这里补挂一档更省的。
            bool pcSsao = EnsureSsaoFeature(RendererPath, PcSsaoTuning(), out bool pcCreated);
            bool mobileSsao = EnsureSsaoFeature(MobileRendererPath, MobileSsaoTuning(), out bool mobileCreated);
            int prefilterTouched = ApplySsaoPrefilters();

            // 5. 移动端调色模式对齐 PC 的 HDR 调色（LDR 会在调色前裁掉高光）
            bool gradingSynced = AlignMobileColorGradingMode();

            // 6. 墨迹图集 mip：远景不再闪，延迟压到 0.2s
            var inkConfig = AssetDatabase.LoadAssetAtPath<InkGridConfigSO>(InkConfigPath);
            if (inkConfig != null)
            {
                inkConfig.atlasMipmaps = true;
                inkConfig.atlasMipIntervalSeconds = 0.2f;
                EditorUtility.SetDirty(inkConfig);
            }

            // 组件上的 SerializedObject 改动不一定会把场景标脏（P29 实测），必须显式 Mark 后保存；
            // 否则退出 Play 重载场景时会悄悄回到旧的 inkGlow。
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[质感调优] 已应用：曝光 -0.10 EV / 对比 18 / vibrance 0.30 / " +
                      $"阴影下限 0.36~0.46 / 墨面 glow 0.04 / 场景 inkGlow 0.06 / " +
                      $"SSAO PC {(pcSsao ? (pcCreated ? "已新建" : "已更新") : "未找到")}、" +
                      $"移动端 {(mobileSsao ? (mobileCreated ? "已新建" : "已更新") : "未找到")} " +
                      $"(剔除标记 {prefilterTouched} 项) / 移动端 HDR 调色 {(gradingSynced ? "已对齐" : "无需改")} / " +
                      $"图集 mip 0.2s。");
        }

        /// <summary>
        /// 三张地图的墨面材质统一：`_InkGlow` 0.2/0.1 → 0.04、`_InkShadowFloor` → 0.50、
        /// `_ShadowFloor` → 0.36（地面还要保留高台侧沿的暗部保底，不能太低）。
        /// 返回处理到的地面材质数量。
        /// </summary>
        private static int ApplyInkGroundMaterialDeltas()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot });
            int touched = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("InkGround")) continue;

                var ground = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (ground == null) continue;

                if (ground.HasProperty(InkGlowId)) ground.SetFloat(InkGlowId, 0.04f);
                if (ground.HasProperty(InkShadowFloorId)) ground.SetFloat(InkShadowFloorId, 0.50f);
                if (ground.HasProperty(ContactShadowStrengthId)) ground.SetFloat(ContactShadowStrengthId, 0.6f);
                if (ground.HasProperty(ShadowFloorId)) ground.SetFloat(ShadowFloorId, 0.36f);
                EditorUtility.SetDirty(ground);
                touched++;
            }

            if (touched == 0) Debug.LogWarning("[质感调优] 没找到任何 InkGround 材质，跳过墨面调参。");
            return touched;
        }

        /// <summary>
        /// 把「暗部抬得太高」的阴影下限压下去：角色 0.46、其它 Toon 材质 0.40。
        /// 地面材质由 <see cref="ApplyInkGroundMaterialDeltas"/> 单独设为 0.36。
        /// 只改 `_ShadowFloor` 与 `_ShadowTint` 两个字段，不动别的材质参数。
        /// </summary>
        private static void ApplyShadowFloorToToonMaterials()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot });
            Color tint = new Color(0.44f, 0.48f, 0.62f, 1f);
            Color linearTint = QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint;
            int touched = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("InkGround")) continue;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || !mat.HasProperty(ShadowFloorId)) continue;

                mat.SetFloat(ShadowFloorId, path.Contains("/Characters/") ? 0.46f : 0.40f);
                if (mat.HasProperty(ShadowTintId)) mat.SetColor(ShadowTintId, linearTint);
                EditorUtility.SetDirty(mat);
                touched++;
            }

            Debug.Log($"[质感调优] 阴影下限已写入 {touched} 个 Toon 材质（角色 0.46 / 其它 0.40）。");
        }

        /// <summary>SSAO 一档参数。枚举取值与 URP 序列化字段一致，见 ScreenSpaceAmbientOcclusionSettings。</summary>
        private struct SsaoTuning
        {
            public int method;        // AOMethodOptions: 0 BlueNoise / 1 InterleavedGradient
            public bool downsample;   // true = 半分辨率算 AO
            public int source;        // DepthSource: 0 Depth（重建法线）/ 1 DepthNormals
            public int normalSamples; // NormalQuality: 0 Low / 1 Medium / 2 High
            public float intensity;
            public float directLighting;
            public float radius;
            public int samples;       // AOSampleOption: 0 High(12) / 1 Medium(8) / 2 Low(4)
            public int blurQuality;   // BlurQualityOptions: 0 High(双边) / 1 Medium(高斯) / 2 Low(Kawase)
            public float falloff;
        }

        private static SsaoTuning PcSsaoTuning()
        {
            return new SsaoTuning
            {
                method = 0, downsample = false, source = 1, normalSamples = 2,
                intensity = 0.85f, directLighting = 0.30f, radius = 0.60f,
                samples = 2, blurQuality = 2, falloff = 100f
            };
        }

        /// <summary>
        /// 移动端档：半分辨率 + 4 采样 + 高斯模糊 + Depth 源。
        /// 不用 DepthNormals 源是刻意的 —— 那需要每帧多跑一遍几何 DepthNormals prepass，
        /// 移动端 DrawCalls 预算是 150，一遍 prepass 的提交开销比 AO 本身更贵；
        /// Depth 源只多一次深度拷贝，AO 由片元从深度重建法线，近景接触阴影照样成立。
        /// </summary>
        private static SsaoTuning MobileSsaoTuning()
        {
            return new SsaoTuning
            {
                method = 0, downsample = true, source = 0, normalSamples = 0,
                intensity = 0.90f, directLighting = 0.30f, radius = 0.60f,
                samples = 2, blurQuality = 1, falloff = 100f
            };
        }

        /// <summary>
        /// 找到（或新建）renderer 上的 SSAO feature 并写入参数。
        /// URP 的 ScreenSpaceAmbientOcclusion 是 internal 类型，只能反射取 Type 再
        /// AddObjectToAsset —— 不能手改 renderer 的 YAML（AGENTS.md 硬性规则 1）。
        /// </summary>
        private static bool EnsureSsaoFeature(string rendererPath, SsaoTuning tuning, out bool created)
        {
            created = false;
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (data == null)
            {
                Debug.LogWarning($"[质感调优] 找不到 {rendererPath}，跳过 SSAO。");
                return false;
            }

            System.Type featureType = typeof(UniversalRendererData).Assembly.GetType(SsaoFeatureTypeName);
            if (featureType == null)
            {
                Debug.LogError("[质感调优] URP 里找不到 ScreenSpaceAmbientOcclusion 类型，SSAO 未处理。");
                return false;
            }

            ScriptableRendererFeature feature = null;
            foreach (ScriptableRendererFeature f in data.rendererFeatures)
            {
                if (f != null && f.GetType() == featureType) { feature = f; break; }
            }

            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                feature.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(feature, data);

                var dataSo = new SerializedObject(data);
                SerializedProperty features = dataSo.FindProperty("m_RendererFeatures");
                SerializedProperty featureMap = dataSo.FindProperty("m_RendererFeatureMap");
                if (features == null)
                {
                    Debug.LogError($"[质感调优] {rendererPath} 序列化结构异常，SSAO 未挂上。");
                    return false;
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
                created = true;
            }

            var so = new SerializedObject(feature);
            SetInt(so, "m_Settings.AOMethod", tuning.method);
            SetBool(so, "m_Settings.Downsample", tuning.downsample);
            SetBool(so, "m_Settings.AfterOpaque", true);
            SetInt(so, "m_Settings.Source", tuning.source);
            SetInt(so, "m_Settings.NormalSamples", tuning.normalSamples);
            SetFloat(so, "m_Settings.Intensity", tuning.intensity);
            SetFloat(so, "m_Settings.DirectLightingStrength", tuning.directLighting);
            SetFloat(so, "m_Settings.Radius", tuning.radius);
            SetInt(so, "m_Settings.Samples", tuning.samples);
            SetInt(so, "m_Settings.BlurQuality", tuning.blurQuality);
            SetFloat(so, "m_Settings.Falloff", tuning.falloff);
            SetInt(so, "m_Settings.SampleCount", -1); // 旧字段：-1 = 已迁移到 Samples
            SetBool(so, "m_Active", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            return true;
        }

        /// <summary>
        /// 修 RPAsset 上的 SSAO 变体剔除标记：Steam/构建期会按这些 bool 删掉对应 local 变体，
        /// 标错就会出现「编辑器正常、包里 SSAO 全黑或某档失效」。这里只保留两端实际用到的档位。
        /// </summary>
        private static int ApplySsaoPrefilters()
        {
            int changed = 0;
            // PC：DepthNormals 源 + 4 采样 + BlueNoise（Low 之前被误剔除）
            changed += PatchPrefilters(PcRpAssetPath, new (string, bool)[]
            {
                ("m_PrefilterSSAODepthNormals", false),
                ("m_PrefilterSSAOSourceDepthLow", true),
                ("m_PrefilterSSAOSourceDepthMedium", true),
                ("m_PrefilterSSAOSourceDepthHigh", true),
                ("m_PrefilterSSAOSampleCountLow", false),
                ("m_PrefilterSSAOSampleCountMedium", true),
                ("m_PrefilterSSAOSampleCountHigh", true),
                ("m_PrefilterSSAOBlueNoise", false),
                ("m_PrefilterSSAOInterleaved", true),
            });

            // 移动端：Depth 源（Low）+ 4 采样 + BlueNoise
            changed += PatchPrefilters(MobileRpAssetPath, new (string, bool)[]
            {
                ("m_PrefilterSSAODepthNormals", true),
                ("m_PrefilterSSAOSourceDepthLow", false),
                ("m_PrefilterSSAOSourceDepthMedium", true),
                ("m_PrefilterSSAOSourceDepthHigh", true),
                ("m_PrefilterSSAOSampleCountLow", false),
                ("m_PrefilterSSAOSampleCountMedium", true),
                ("m_PrefilterSSAOSampleCountHigh", true),
                ("m_PrefilterSSAOBlueNoise", false),
                ("m_PrefilterSSAOInterleaved", true),
            });
            return changed;
        }

        /// <summary>返回真正发生改动的字段数。0 = 已经是目标值。</summary>
        private static int PatchPrefilters(string rpAssetPath, (string name, bool strip)[] values)
        {
            var rpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpAssetPath);
            if (rpAsset == null)
            {
                Debug.LogWarning($"[质感调优] 找不到 {rpAssetPath}，SSAO 剔除标记未同步。");
                return 0;
            }

            var so = new SerializedObject(rpAsset);
            int changed = 0;
            foreach ((string name, bool strip) in values)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p == null) continue;
                if (p.boolValue == strip) continue;
                p.boolValue = strip;
                changed++;
            }
            if (changed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rpAsset);
            }
            return changed;
        }

        /// <summary>
        /// 移动端 ColorGradingMode 对齐 PC：0 = LDR 调色（调色前就裁掉高光），1 = HDR 调色。
        /// 不统一的话同一份 Neutral tonemap + 负曝光在两端会读出不同对比，属于两端画质不同步。
        /// </summary>
        private static bool AlignMobileColorGradingMode()
        {
            var mobileRp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAssetPath);
            if (mobileRp == null) return false;

            var so = new SerializedObject(mobileRp);
            SerializedProperty mode = so.FindProperty("m_ColorGradingMode");
            if (mode == null || mode.intValue == 1) return false;

            mode.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mobileRp);
            return true;
        }

        private static void SetBool(SerializedObject so, string path, bool value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
        }

        private static void SetInt(SerializedObject so, string path, int value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.intValue = value;
        }

        private static void SetFloat(SerializedObject so, string path, float value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.floatValue = value;
        }
    }
}
