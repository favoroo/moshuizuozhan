using P1.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace P1.Editor
{
    /// <summary>
    /// P7-B 场景氛围装配器：把 ArenaStyleSO 的日光、环境光、雾、程序化天空与后处理
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
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SkyCloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int SkyCloudStrengthId = Shader.PropertyToID("_CloudStrength");

        private static Color Linear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        public static void Apply(ArenaStyleSO style)
        {
            if (style == null)
            {
                Debug.LogWarning("[ArenaAtmosphereBuilder] 未提供 ArenaStyleSO，跳过日光/雾/天空/后处理。");
                return;
            }

            ApplySunAndEnvironment(style);
            ApplyFog(style);
            ApplySkybox(style);
            ApplyPostProcess(style);

            // 让相机看到天空盒（相机可能由 Scaffolder 稍后创建，找不到就跳过）。
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
            }

            DynamicGI.UpdateEnvironment();
            Debug.Log("[ArenaAtmosphereBuilder] P7-B 日光/雾/天空/后处理已应用。");
        }

        private static void ApplySunAndEnvironment(ArenaStyleSO style)
        {
            Light sun = FindDirectionalLight();
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = Linear(style.sunColor);
                sun.intensity = style.sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(style.sunEuler);
                RenderSettings.sun = sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Linear(style.ambientSky);
            RenderSettings.ambientEquatorColor = Linear(style.ambientEquator);
            RenderSettings.ambientGroundColor = Linear(style.ambientGround);
            RenderSettings.ambientIntensity = style.ambientIntensity;
            RenderSettings.reflectionIntensity = 0.55f;
        }

        private static void ApplyFog(ArenaStyleSO style)
        {
            RenderSettings.fog = style.fogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Linear(style.fogColor);
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
            sky.SetColor(SkyCloudColorId, Linear(style.cloudColor));
            sky.SetFloat(SkyCloudStrengthId, style.cloudStrength);

            Vector3 sunDir = Quaternion.Euler(style.sunEuler) * Vector3.back;
            sky.SetVector(SkySunDirectionId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        private static void ApplyPostProcess(ArenaStyleSO style)
        {
            VolumeProfile profile = EnsurePostProfile();
            if (profile == null) return;

            GetOrAddComponent<Tonemapping>(profile, c =>
            {
                c.mode.Override(TonemappingMode.ACES);
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
