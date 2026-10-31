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

        // 深炭紫回退默认值（与 STYLE.md 的 #15121C 系一致；sRGB 值在这里转线性）
        private static readonly Color FallbackSkyTop = new Color(0.055f, 0.050f, 0.090f, 1f);
        private static readonly Color FallbackSkyHorizon = new Color(0.130f, 0.120f, 0.170f, 1f);
        private static readonly Color FallbackSkyGround = new Color(0.030f, 0.028f, 0.045f, 1f);

        private static ArenaStyleSO _style;
        private static bool _styleLookupDone;
        private static bool _tierApplied;

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
            float spec = _style != null ? _style.envSpecStrength : 1f;
            float sunDisc = _style != null ? _style.envSunDiscPower : 900f;

            Shader.SetGlobalColor(SkyTopId, ToLinear(top));
            Shader.SetGlobalColor(SkyHorizonId, ToLinear(horizon));
            Shader.SetGlobalColor(SkyGroundId, ToLinear(ground));
            Shader.SetGlobalFloat(SpecStrengthId, spec);
            Shader.SetGlobalFloat(SunDiscPowerId, sunDisc);
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
