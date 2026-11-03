using P1.Environment;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Core
{
    /// <summary>
    /// 环境镜面全局 + 平台画质档位（P18 画风重做）。
    ///
    /// 把「解析天空环境镜面」需要的 3 色天空、强度与太阳盘锐度以全局 uniform 推送给
    /// 所有 include <c>InkWetShading.hlsl</c> 的着色器（地面 / 顶面 / 墙面 / 飞行墨滴），
    /// 保证它们共享同一套湿润反光语言；同时按平台开启 <c>P1_INK_HIGH</c> 画质关键词
    /// （移动端不开，着色器走降级分支，保持 30 FPS 红线）。
    ///
    /// 数据来源是 <see cref="ArenaStyleSO"/>（画风的唯一调参入口，符合硬性规则 4）。
    /// 运行时通过 <see cref="Object.FindObjectsOfTypeAll"/> 惰性拾取已加载的样式资产
    /// （场景中的 ArenaPropMeshFactory 会引用它）；拾取不到时用深炭紫回退默认值，
    /// 保证独立包不因缺引用而静默变亮变平。
    /// </summary>
    public static class RenderEnvGlobals
    {
        /// <summary>高画质档关键词：桌面端启用墨面颗粒/环境反射全量，移动端走降级分支。</summary>
        public const string InkHighKeyword = "P1_INK_HIGH";

        private static readonly int SkyTopId = Shader.PropertyToID("_P1EnvSkyTop");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_P1EnvSkyHorizon");
        private static readonly int SkyGroundId = Shader.PropertyToID("_P1EnvSkyGround");
        private static readonly int SpecStrengthId = Shader.PropertyToID("_P1EnvSpecStrength");
        private static readonly int SunDiscPowerId = Shader.PropertyToID("_P1EnvSunDiscPower");

        // P29 渲染基础设施：环境反射 Cubemap 与质感全局标量
        private static readonly int EnvCubeId = Shader.PropertyToID("_P1EnvCube");
        private static readonly int EnvCubeMipsId = Shader.PropertyToID("_P1EnvCubeMips");
        private static readonly int EnvCubeReadyId = Shader.PropertyToID("_P1EnvCubeReady");
        private static readonly int EnvCubeStrengthId = Shader.PropertyToID("_P1EnvCubeStrength");

        private static readonly int InkCoatStrengthId = Shader.PropertyToID("_P1InkCoatStrength");
        private static readonly int InkCoatRoughnessId = Shader.PropertyToID("_P1InkCoatRoughness");
        private static readonly int InkEdgeRimStrengthId = Shader.PropertyToID("_P1InkEdgeRimStrength");
        private static readonly int InkEdgeSmoothStrengthId = Shader.PropertyToID("_P1InkEdgeSmoothStrength");
        private static readonly int InkTopDetailStrengthId = Shader.PropertyToID("_P1InkTopDetailStrength");
        private static readonly int WallDripSpeedId = Shader.PropertyToID("_P1WallDripSpeed");
        private static readonly int WaterReflectionStrengthId = Shader.PropertyToID("_P1WaterReflectionStrength");
        private static readonly int EmissiveStrengthId = Shader.PropertyToID("_P1EmissiveStrength");

        // 港口日间画风回退默认值（与 ArenaStyleSO.asset 一致；sRGB 值在 Push 时转线性）
        private static readonly Color FallbackSkyTop = new Color(0.16f, 0.46f, 0.86f, 1f);
        private static readonly Color FallbackSkyHorizon = new Color(0.80f, 0.89f, 0.97f, 1f);
        private static readonly Color FallbackSkyGround = new Color(0.52f, 0.60f, 0.66f, 1f);

        private static ArenaStyleSO _style;
        private static bool _styleLookupDone;
        private static bool _tierApplied;
        private static bool _cubeStateSet;
        private static float _cubeStrengthOverride = -1f;
        private static float _nextPumpTime;

        /// <summary>当前已加载的环境风格资产（惰性拾取，未找到时返回 null）。</summary>
        public static ArenaStyleSO CurrentStyle
        {
            get
            {
                EnsureStyle();
                return _style;
            }
        }

        /// <summary>
        /// 设置当前场景的环境反射 Cubemap 与反射强度。
        /// </summary>
        public static void SetEnvironmentCube(Cubemap cube, float strength)
        {
            _cubeStrengthOverride = strength;
            _cubeStateSet = true;
            if (cube != null)
            {
                Shader.SetGlobalTexture(EnvCubeId, cube);
                Shader.SetGlobalFloat(EnvCubeMipsId, cube.mipmapCount);
                Shader.SetGlobalFloat(EnvCubeReadyId, 1f);
                Shader.SetGlobalFloat(EnvCubeStrengthId, strength);
            }
            else
            {
                ClearEnvironmentCube();
            }
        }

        /// <summary>
        /// 清理环境反射 Cubemap 状态，回退为未就绪。
        /// </summary>
        public static void ClearEnvironmentCube()
        {
            _cubeStrengthOverride = -1f;
            _cubeStateSet = true;
            Shader.SetGlobalFloat(EnvCubeReadyId, 0f);
            Shader.SetGlobalFloat(EnvCubeMipsId, 1f);
        }

        /// <summary>
        /// <see cref="Pump"/> 的节流版本：环境样式与平台档位在一局内是常量，
        /// 无需每帧重推 14 个全局 uniform。默认 1Hz 刷新，样式/Cubemap 变更仍会立即生效
        /// （变更方可直接调用 <see cref="Pump"/>）。
        /// </summary>
        public static void PumpThrottled(float minIntervalSeconds = 1f)
        {
            float now = Time.unscaledTime;
            if (now < _nextPumpTime)
                return;

            _nextPumpTime = now + Mathf.Max(0.05f, minIntervalSeconds);
            Pump();
        }

        /// <summary>
        /// 推送环境全局并确保平台档位关键词生效。幂等、可每帧调用（几次 SetGlobal* 开销可忽略）。
        /// 由 InkSurfaceController.PushWetClockGlobals 与编辑器画风装配器共同驱动。
        /// </summary>
        public static void Pump()
        {
            EnsureTier();
            EnsureStyle();

            Color top = _style != null ? _style.skyTop : FallbackSkyTop;
            Color horizon = _style != null ? _style.skyHorizon : FallbackSkyHorizon;
            Color ground = _style != null ? _style.skyGround : FallbackSkyGround;
            float spec = _style != null ? _style.envSpecStrength : 1.35f;
            float sunDisc = _style != null ? _style.envSunDiscPower : 900f;

            Shader.SetGlobalColor(SkyTopId, ToLinear(top));
            Shader.SetGlobalColor(SkyHorizonId, ToLinear(horizon));
            Shader.SetGlobalColor(SkyGroundId, ToLinear(ground));
            Shader.SetGlobalFloat(SpecStrengthId, spec);
            Shader.SetGlobalFloat(SunDiscPowerId, sunDisc);

            if (!_cubeStateSet)
            {
                _cubeStateSet = true;
                Shader.SetGlobalFloat(EnvCubeReadyId, 0f);
                Shader.SetGlobalFloat(EnvCubeMipsId, 1f);
            }

            float probe = _style != null ? _style.inkProbeStrength : (_cubeStrengthOverride >= 0f ? _cubeStrengthOverride : 0f);
            float coat = _style != null ? _style.inkCoatStrength : 1.5f;
            float coatRough = _style != null ? _style.inkCoatRoughness : 0.06f;
            float edgeRim = _style != null ? _style.inkEdgeRimStrength : 0.8f;
            float edgeSmooth = _style != null ? _style.inkEdgeSmoothStrength : 0.7f;
            float topDetail = _style != null ? _style.inkTopDetailStrength : 1f;
            float dripSpeed = _style != null ? _style.wallDripSpeed : 1.3f;
            float waterReflect = _style != null ? _style.waterReflectionStrength : 0.8f;
            float emissive = _style != null ? _style.emissiveBloomStrength : 2f;

            Shader.SetGlobalFloat(EnvCubeStrengthId, probe);
            Shader.SetGlobalFloat(InkCoatStrengthId, coat);
            Shader.SetGlobalFloat(InkCoatRoughnessId, coatRough);
            Shader.SetGlobalFloat(InkEdgeRimStrengthId, edgeRim);
            Shader.SetGlobalFloat(InkEdgeSmoothStrengthId, edgeSmooth);
            Shader.SetGlobalFloat(InkTopDetailStrengthId, topDetail);
            Shader.SetGlobalFloat(WallDripSpeedId, dripSpeed);
            Shader.SetGlobalFloat(WaterReflectionStrengthId, waterReflect);
            Shader.SetGlobalFloat(EmissiveStrengthId, emissive);
        }

        /// <summary>按平台开启/关闭高画质关键词（桌面全量，移动降级）。</summary>
        public static void EnsureTier()
        {
            if (_tierApplied) return;
            _tierApplied = true;
            if (Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Shader.DisableKeyword(InkHighKeyword);
            }
            else
            {
                Shader.EnableKeyword(InkHighKeyword);
            }
        }

        private static void EnsureStyle()
        {
            if (_styleLookupDone) return;
            _styleLookupDone = true;
            _style = Resources.Load<ArenaStyleSO>("ArenaStyle");
            if (_style != null) return;

#if UNITY_EDITOR
            // ArenaStyle.asset 在 ScriptableObjects/（非 Resources），编辑器内 FindObjectsOfTypeAll
            // 在场景引用加载前可能拾取不到，导致 Play 模式全程用回退默认值（质感参数静默降级）。
            _style = UnityEditor.AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(
                "Assets/_Project/ScriptableObjects/ArenaStyle.asset");
            if (_style != null) return;
#endif

            Object[] found = Object.FindObjectsOfTypeAll(typeof(ArenaStyleSO));
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is ArenaStyleSO so)
                {
                    _style = so;
                    break;
                }
            }
        }

        private static Color ToLinear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }
    }
}
