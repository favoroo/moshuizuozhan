using P1.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace P1.Editor
{
    /// <summary>
    /// P7-B / P30 场景氛围装配器：把 ArenaStyleSO 的日光、环境光、雾、程序化天空、水面、出生台与后处理
    /// 幂等地写进当前场景（不重建角色/HUD），供一键升级菜单与全量场景构建共用。
    /// </summary>
    public static class ArenaAtmosphereBuilder
    {
        public const string PostProfilePath = "Assets/_Project/Settings/ArenaPostProfile.asset";
        public const string AtmosphereRootName = "_Atmosphere";

        private static readonly int SkyTopId = Shader.PropertyToID("_TopColor");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_HorizonColor");
        private static readonly int SkyGroundId = Shader.PropertyToID("_GroundColor");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SkySunIntensityId = Shader.PropertyToID("_SunIntensity");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SkyCloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int SkyCloudStrengthId = Shader.PropertyToID("_CloudStrength");
        private static readonly int SkyHorizonWarmthColorId = Shader.PropertyToID("_HorizonWarmthColor");
        private static readonly int SkyHorizonWarmthId = Shader.PropertyToID("_HorizonWarmth");
        private static readonly int SkyCloudLayer2StrengthId = Shader.PropertyToID("_CloudLayer2Strength");
        private static readonly int SkyCloudEdgeCrispnessId = Shader.PropertyToID("_CloudEdgeCrispness");

        private static readonly int WaterShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int WaterDeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int WaterHorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int WaterWaveScaleId = Shader.PropertyToID("_WaveScale");
        private static readonly int WaterWaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int WaterWaveStrengthId = Shader.PropertyToID("_WaveStrength");
        private static readonly int WaterWaveNormalStrengthId = Shader.PropertyToID("_WaveNormalStrength");
        private static readonly int WaterGlitterStrengthId = Shader.PropertyToID("_GlitterStrength");
        private static readonly int WaterGlitterThresholdId = Shader.PropertyToID("_GlitterThreshold");
        private static readonly int WaterShoreDistanceId = Shader.PropertyToID("_ShoreDistance");
        private static readonly int WaterFoamStrengthId = Shader.PropertyToID("_FoamStrength");

        private static readonly int SpawnBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SpawnPlatformColorId = Shader.PropertyToID("_PlatformColor");
        private static readonly int SpawnRingColorId = Shader.PropertyToID("_RingColor");
        private static readonly int SpawnChevronColorId = Shader.PropertyToID("_ChevronColor");
        private static readonly int SpawnTeamColorId = Shader.PropertyToID("_TeamColor");
        private static readonly int SpawnGlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private static readonly int SpawnTeamTintBlendId = Shader.PropertyToID("_TeamTintBlend");
        private static readonly int SpawnPulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int SpawnShadowTintId = Shader.PropertyToID("_ShadowTint");
        private static readonly int SpawnShadowFloorId = Shader.PropertyToID("_ShadowFloor");
        private static readonly int SpawnShadeSoftnessId = Shader.PropertyToID("_ShadeSoftness");
        private static readonly int SpawnForwardSignId = Shader.PropertyToID("_ForwardSign");

        private static Color Linear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        public static void Apply(ArenaStyleSO style)
        {
            if (style == null)
            {
                Debug.LogWarning("[ArenaAtmosphereBuilder] 未提供 ArenaStyleSO，跳过日光/雾/天空/水面/后处理。");
                return;
            }

            ApplySunAndEnvironment(style);
            ApplyFog(style);
            ApplySkybox(style);
            ApplyWater(style);
            ApplySpawnPads(style);
            ApplyPostProcess(style);
            EnsureCameraData(style);

            DynamicGI.UpdateEnvironment();
            Debug.Log("[ArenaAtmosphereBuilder] P7-B / P30 日光/雾/天空/水面/出生台/后处理已应用。");
        }

        private static void EnsureCameraData(ArenaStyleSO style)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = style.postProcessingEnabled;
                data.renderShadows = true;
                if (Application.isMobilePlatform)
                {
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    data.antialiasingQuality = AntialiasingQuality.Low;
                }
                else
                {
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                }
                EditorUtility.SetDirty(data);
                // GetUniversalAdditionalCameraData 可能刚把组件加到相机上：只 SetDirty 组件
                // 不一定会把所在场景标脏，后续 SaveScene 可能漏掉这个新组件（P29 实测）。
                if (cam.gameObject.scene.IsValid())
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
                }
            }
        }

        private static void ApplySunAndEnvironment(ArenaStyleSO style)
        {
            Light sun = FindDirectionalLight();
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = Linear(style.sunColor);
                // 日光微调提亮（+8% 照度），与海港明快日照契约对齐
                sun.intensity = style.sunIntensity * 1.08f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = style.shadowStrength;
                sun.transform.rotation = Quaternion.Euler(style.sunEuler);
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // 环境天顶提亮微调：避免深部阴影发暗发灰，强化晴朗天空漫反射
            Color liftedSky = Color.Lerp(style.ambientSky, new Color(0.40f, 0.56f, 0.82f, 1f), 0.22f);
            Color liftedEquator = Color.Lerp(style.ambientEquator, new Color(0.60f, 0.58f, 0.54f, 1f), 0.15f);
            RenderSettings.ambientSkyColor = Linear(liftedSky);
            RenderSettings.ambientEquatorColor = Linear(liftedEquator);
            RenderSettings.ambientGroundColor = Linear(style.ambientGround);
            RenderSettings.ambientIntensity = Mathf.Max(style.ambientIntensity, 0.82f);
            RenderSettings.reflectionIntensity = style.reflectionIntensity;
        }

        private static void ApplyFog(ArenaStyleSO style)
        {
            RenderSettings.fog = style.fogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            // 雾色贴近 skyHorizon 的亮蓝白，彻底消除原本洗灰全场的灰蒙蒙工业烟尘感
            Color clearSkyFog = Color.Lerp(style.fogColor, style.skyHorizon, 0.85f);
            RenderSettings.fogColor = Linear(clearSkyFog);
            // 竞技场 40x56m，fogStart 70m 确保近中景全部通透无雾，fogEnd 190m 让远景柔和融入天际线（P30 场地精修调优值）
            RenderSettings.fogStartDistance = style.fogStartDistance;
            RenderSettings.fogEndDistance = style.fogEndDistance;
        }

        private static void ApplySkybox(ArenaStyleSO style)
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Mat_Skybox.mat");
            if (sky == null) return;

            sky.SetColor(SkyTopId, Linear(style.skyTop));
            sky.SetColor(SkyHorizonId, Linear(style.skyHorizon));
            sky.SetColor(SkyGroundId, Linear(style.skyGround));
            sky.SetColor(SkySunColorId, Linear(style.sunDisc));
            if (sky.HasProperty(SkySunIntensityId))
            {
                sky.SetFloat(SkySunIntensityId, style.skySunIntensity);
            }
            sky.SetColor(SkyCloudColorId, Linear(style.cloudColor));
            sky.SetFloat(SkyCloudStrengthId, style.cloudStrength);

            // 新增天空盒属性兜底设置
            if (sky.HasProperty(SkyHorizonWarmthColorId))
            {
                sky.SetColor(SkyHorizonWarmthColorId, Linear(new Color(1.0f, 0.94f, 0.84f, 1.0f)));
            }
            if (sky.HasProperty(SkyHorizonWarmthId))
            {
                sky.SetFloat(SkyHorizonWarmthId, 0.35f);
            }
            if (sky.HasProperty(SkyCloudLayer2StrengthId))
            {
                sky.SetFloat(SkyCloudLayer2StrengthId, 0.25f);
            }
            if (sky.HasProperty(SkyCloudEdgeCrispnessId))
            {
                sky.SetFloat(SkyCloudEdgeCrispnessId, 0.06f);
            }

            Vector3 sunDir = Quaternion.Euler(style.sunEuler) * Vector3.back;
            sky.SetVector(SkySunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        private static void ApplyWater(ArenaStyleSO style)
        {
            Material water = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Mat_Water.mat");
            if (water == null) return;

            water.SetColor(WaterShallowColorId, Linear(style.waterShallow));
            water.SetColor(WaterDeepColorId, Linear(style.waterDeep));
            water.SetColor(WaterHorizonColorId, Linear(style.waterHorizon));

            // 场景精修：波形放大（0.8）、波涌减弱（0.22）——原 1.0/0.35 在 240m 水面上
            // 密铺出细碎亮斑，掠射角远看像满屏白色噪点；大波低频后远看是大片蓝面。
            if (water.HasProperty(WaterWaveScaleId)) water.SetFloat(WaterWaveScaleId, 0.8f);
            if (water.HasProperty(WaterWaveSpeedId)) water.SetFloat(WaterWaveSpeedId, 0.18f);
            if (water.HasProperty(WaterWaveStrengthId)) water.SetFloat(WaterWaveStrengthId, 0.22f);
            if (water.HasProperty(WaterWaveNormalStrengthId)) water.SetFloat(WaterWaveNormalStrengthId, 0.30f);
            // P30 返修 + 场景精修：glitter 再次收敛——更弱（0.45）、更稀疏（阈值 0.988），
            // 远看只剩零星波峰闪点；阈值仍低于 1 保证近看有活泼高光。
            if (water.HasProperty(WaterGlitterStrengthId)) water.SetFloat(WaterGlitterStrengthId, 0.45f);
            if (water.HasProperty(WaterGlitterThresholdId)) water.SetFloat(WaterGlitterThresholdId, 0.988f);
            if (water.HasProperty(WaterShoreDistanceId)) water.SetFloat(WaterShoreDistanceId, 12.0f);
            if (water.HasProperty(WaterFoamStrengthId)) water.SetFloat(WaterFoamStrengthId, 0.35f);

            EditorUtility.SetDirty(water);
        }

        private static void ApplySpawnPads(ArenaStyleSO style)
        {
            ConfigureSpawnPadMaterial("Assets/_Project/Materials/Mat_SpawnPad_TeamA.mat", style, style.teamAPad, 1.0f);
            ConfigureSpawnPadMaterial("Assets/_Project/Materials/Mat_SpawnPad_TeamB.mat", style, style.teamBPad, -1.0f);
            ConfigureSpawnPadMaterial("Assets/_Project/Materials/Mat_SpawnPad.mat", style, style.hazardYellow, 1.0f);
        }

        private static void ConfigureSpawnPadMaterial(string path, ArenaStyleSO style, Color teamColor, float forwardSign)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) return;

            // 嵌板钢灰：必须与 ProjectScaffolder.CreateOrUpdateSpawnPadMaterial 的 steelCol 同值。
            // 旧值 (0.22,0.25,0.29) 在出生台上渲染成近黑 (16,16,24)，看着像台面挖了个洞；
            // 0.50 档实测屏上 ≈ (110,103,102)，是正常的磨砂钢板灰。
            var steelBase = new Color(0.50f, 0.52f, 0.55f, 1f);
            mat.SetColor(SpawnBaseColorId, Linear(steelBase));
            mat.SetColor(SpawnPlatformColorId, Linear(style.concreteMid));
            mat.SetColor(SpawnRingColorId, Linear(new Color(0.92f, 0.94f, 0.98f, 1f)));
            mat.SetColor(SpawnChevronColorId, Linear(style.hazardYellow));
            mat.SetColor(SpawnTeamColorId, Linear(teamColor));
            mat.SetColor(SpawnShadowTintId, Linear(style.shadowTint));
            mat.SetFloat(SpawnShadowFloorId, style.shadowFloor);
            mat.SetFloat(SpawnShadeSoftnessId, style.shadeSoftness);
            mat.SetFloat(SpawnGlowStrengthId, 0.25f);
            // 队色降饱和：原始队色（A 队鲜红橙）大面积压在浅灰混凝土上太突兀，
            // 向基台混凝土色混合 80% 后是褪色陶土/灰蓝涂料，与地面协调且仍能区分队别。
            if (mat.HasProperty(SpawnTeamTintBlendId)) mat.SetFloat(SpawnTeamTintBlendId, 0.80f);
            if (mat.HasProperty(SpawnPulseSpeedId)) mat.SetFloat(SpawnPulseSpeedId, 1.6f);
            mat.SetFloat(SpawnForwardSignId, forwardSign);

            EditorUtility.SetDirty(mat);
        }

        private static void ApplyPostProcess(ArenaStyleSO style)
        {
            VolumeProfile profile = EnsurePostProfile();
            if (profile == null) return;

            GetOrAddComponent<Tonemapping>(profile, c =>
            {
                c.mode.Override(style.tonemapMode == ArenaTonemapMode.Neutral
                    ? TonemappingMode.Neutral
                    : TonemappingMode.ACES);
            });

            GetOrAddComponent<Bloom>(profile, c =>
            {
                c.intensity.Override(style.bloomIntensity);
                c.threshold.Override(style.bloomThreshold);
                c.scatter.Override(0.62f);
                c.highQualityFiltering.Override(false);
            });

            GetOrAddComponent<ColorAdjustments>(profile, c =>
            {
                c.postExposure.Override(style.postExposure);
                c.contrast.Override(style.colorContrast);
                c.saturation.Override(style.colorSaturation);
                c.colorFilter.Override(Color.white);
                c.hueShift.Override(0f);
            });

            GetOrAddComponent<Vignette>(profile, c =>
            {
                c.intensity.Override(style.vignetteIntensity);
                c.smoothness.Override(0.42f);
                c.color.Override(new Color(0.05f, 0.06f, 0.09f, 1f));
            });

            // P18 INKWAVE grade：阴影染冷紫、高光染暖（白 = 不染色，按强度插值），
            // 高饱和队色主要落在中亮部，受阴影染色影响小，纯度得以保留（对齐竞品 split-tone）。
            GetOrAddComponent<SplitToning>(profile, c =>
            {
                float strength = Mathf.Clamp01(style.splitToningStrength);
                c.shadows.Override(Color.Lerp(Color.white, Linear(style.splitShadowColor), strength));
                c.highlights.Override(Color.Lerp(Color.white, Linear(style.splitHighlightColor), strength));
                c.balance.Override(0f);
            });

            // P18：胶片曲线——压 toe（暗部收进深炭紫）+ 压 shoulder（高光不过曝发白），
            // 中灰 0.5 处保持线性，避免整体对比拉爆后队色偏色。
            GetOrAddComponent<ColorCurves>(profile, c =>
            {
                var master = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0.55f),
                    new Keyframe(0.25f, 0.20f, 0.9f, 1.05f),
                    new Keyframe(0.5f, 0.5f, 1f, 1f),
                    new Keyframe(0.75f, 0.80f, 1.05f, 0.9f),
                    new Keyframe(1f, 1f, 0.55f, 0f));
                master.preWrapMode = WrapMode.Clamp;
                master.postWrapMode = WrapMode.Clamp;
                c.master.Override(new TextureCurve(master, 0f, false, new Vector2(0f, 1f)));
            });

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var root = GameObject.Find(AtmosphereRootName);
            if (root == null)
            {
                root = new GameObject(AtmosphereRootName);
            }

            var volume = root.GetComponent<Volume>();
            if (volume == null)
            {
                volume = root.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        private static VolumeProfile EnsurePostProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Settings");
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            if (profile != null && (profile.components == null || profile.components.Count == 0 || profile.components.Contains(null)))
            {
                AssetDatabase.DeleteAsset(PostProfilePath);
                profile = null;
            }

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostProfilePath);
            }
            return profile;
        }

        private static void GetOrAddComponent<T>(VolumeProfile profile, System.Action<T> configure) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var comp) || comp == null)
            {
                comp = profile.Add<T>(true);
                comp.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            configure(comp);
            EditorUtility.SetDirty(comp);
        }

        private static Light FindDirectionalLight()
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                    return lights[i];
            }

            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            return light;
        }
    }
}
