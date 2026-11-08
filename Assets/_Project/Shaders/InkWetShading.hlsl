#ifndef P1_INK_WET_SHADING_INCLUDED
#define P1_INK_WET_SHADING_INCLUDED

// ============================================================================
// P1 - InkWetShading.hlsl (P18 画风重做)
// 墨面质感共享函数库：解析天空环境镜面、湿润三段曲线、切线空间微表面扰动。
//
// 设计约束：
//  - 只声明全局 uniform（由 Core/RenderEnvGlobals 从 ArenaStyleSO 推送），不声明
//    材质属性 / CBUFFER，由各 Shader 自行声明，保持 SRP Batcher 兼容（与 ToonLighting.hlsl 同约定）。
//  - 纯函数，供 InkGround / InkSurfaceOverlay / InkWall / 墨体着色器复用，
//    保证「地面 = 顶面 = 墙面 = 飞行墨滴」是同一套湿润语言。
//  - 依赖：调用方必须已 include URP 的 Core.hlsl 与 Lighting.hlsl。
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// ---- 环境全局（Core/RenderEnvGlobals.Push 推送；值来自 ArenaStyleSO）----
float3 _P1EnvSkyTop;        // 天顶色（线性）
float3 _P1EnvSkyHorizon;    // 地平线色（线性）
float3 _P1EnvSkyGround;     // 地面反射色（线性）
float  _P1EnvSpecStrength;  // 环境镜面总强度（0 = 关闭，1 = 竞品基准）
float  _P1EnvSunDiscPower;  // 太阳盘锐度（湿面 ~900，干面拉宽 ~80）

// 桌面端环境 Cubemap 反射（RenderEnvGlobals 每帧推送）
TEXTURECUBE(_P1EnvCube);
SAMPLER(sampler_P1EnvCube);
float  _P1EnvCubeMips;
float  _P1EnvCubeReady;
float  _P1EnvCubeStrength;

// 墨水质感参数（RenderEnvGlobals 每帧推送）
float  _P1InkCoatStrength;
float  _P1InkCoatRoughness;
float  _P1InkEdgeRimStrength;
float  _P1InkEdgeSmoothStrength;
float  _P1InkTopDetailStrength;
// 图集三次 B 样条重建强度（0 = 双线性旧行为，1 = 全量 C1 连续场，对齐 INKWAVE paintCubic）
float  _P1InkBicubicStrength;
// 阴影中墨水保饱和（INKWAVE INK_SHADE）：漫反射越弱，色度提升越多（亮度守恒）
float  _P1InkShadowChroma;

// ----------------------------------------------------------------------------
// 太阳直射光盘项（glitter），由 P1InkEnvSpecular 与 P1InkEnvSpecularFull 共用
// ----------------------------------------------------------------------------
half3 P1InkEnvSunGlint(float3 R, float rough, float NdotV)
{
    float3 L = normalize(GetMainLight().direction);
    float sd = saturate(dot(R, L));
    float discPow = max(8.0, lerp(_P1EnvSunDiscPower, 80.0, rough));
    half disc = (half)pow(sd, discPow) * (half)lerp(1.0, 0.25, rough);
    disc *= (half)saturate(NdotV * 2.0);
    return _MainLightColor.rgb * disc * 1.2;
}

// ----------------------------------------------------------------------------
// 解析天空环境镜面
// R      : 反射方向（从表面指向场景）
// rough  : 0（镜面）.. 1（粗糙）—— 控制太阳盘宽窄与亮度衰减
// NdotV  : 掠射角菲涅尔用
// 返回线性 HDR RGB（已含菲涅尔与全局强度）。
// ----------------------------------------------------------------------------
half3 P1InkEnvSpecular(float3 R, float rough, float NdotV)
{
    float t = R.y;
    // 下半球（反射朝向地面/远后方）：不能停在接近白的地平线色上——颗粒法线会让大量像素的
    // 反射方向扫进 [-45°, 0°] 亮带，把整片队色墨刷成浅鲑鱼色（P18 实测回归）。
    // 这里把下半球起点压暗到 35% 地平线色，并在 14° 内快速落入深色的地面反射色。
    half3 belowHorizon = _P1EnvSkyHorizon * 0.35;
    half3 sky = t >= 0.0
        ? lerp(_P1EnvSkyHorizon, _P1EnvSkyTop, pow(saturate(t), 0.55))
        : lerp(belowHorizon, _P1EnvSkyGround, saturate(-t * 4.0));

    // 太阳盘：湿面窄亮、干面拉宽变暗（粗糙度展宽，模拟微表面积分）。
    // 掠射角强抑制：低视角下 R 会扫进太阳方向形成宽亮带，把整片墨洗白（P18 实测回归），
    // 盘亮度必须随 NdotV 收紧，只保留正上方俯视时的镜面 glitter。
    half3 env = sky + P1InkEnvSunGlint(R, rough, NdotV);

    // 清漆层菲涅尔。F0 抬到 0.09（物理 0.04 在斜俯视角下环境反射只剩 ~5%，墨面读作哑光粉漆；
    // Splatoon 式湿墨靠作弊的常数缎面光泽）；掠射上限压到 0.42，避免掠射白光把队色洗浅（P18 实测）。
    float fres = pow(saturate(1.0 - NdotV), 5.0);
    half F = (half)(0.09 + 0.42 * fres);
    half3 spec = env * F * (half)_P1EnvSpecStrength;
    // 单通道硬上限：任何角度下环境镜面都不允许把队色整体洗白（只保留 glitter 峰值）
    return min(spec, (half3)1.0);
}

// ----------------------------------------------------------------------------
// 全量环境镜面（桌面端 Cubemap + 解析太阳盘回退）
// ----------------------------------------------------------------------------
half3 P1InkEnvSpecularFull(float3 R, float rough, float NdotV, half3 inkTint)
{
    half3 result = P1InkEnvSpecular(R, rough, NdotV);
#if defined(P1_INK_HIGH)
    if (_P1EnvCubeReady > 0.5)
    {
        float lod = rough * max(1.0, _P1EnvCubeMips - 1.0);
        half3 cube = SAMPLE_TEXTURECUBE_LOD(_P1EnvCube, sampler_P1EnvCube, R, lod).rgb;
        half lum = Luminance(cube);
        half3 neutral = lerp(cube, half3(lum, lum, lum), 0.35);
        half3 tinted = lerp(neutral, neutral * saturate(inkTint * 2.0), 0.25) * (half)_P1EnvCubeStrength;

        half3 env = tinted + P1InkEnvSunGlint(R, rough, NdotV);

        float fres = pow(saturate(1.0 - NdotV), 5.0);
        half F = (half)(0.09 + 0.42 * fres);
        half3 spec = env * F * (half)_P1EnvSpecStrength;
        result = min(spec, (half3)1.5);
    }
#endif
    return result;
}

// ----------------------------------------------------------------------------
// 双层清漆镜面高光：尖锐清漆层 + 宽 sheen 层
// 返回白色光项（由调用方乘 _MainLightColor）
// ----------------------------------------------------------------------------
half3 P1InkCoatSpec(float NdotH, float NdotV, float shadowAtten, float wet, float sheen)
{
    float rough = clamp(_P1InkCoatRoughness, 0.02, 0.95);
#if defined(P1_INK_HIGH)
    float sharpPower = exp2(10.0 * (1.0 - rough) + 1.0);
    // 注意：不用 (power+8)/8 的 GGX 归一化。roughness≈0.06 时该系数约 170，
    // 叠加 1.2 的强度与 1.6 光强会得到 100+ 的 HDR 单像素 firefly。
    // 这里用有上限的直接权重：峰值 ≈ _P1InkCoatStrength（1.2），足够 Bloom 拾取但不爆白。
    // 干墨保留缎面余晖光泽：Splatoon 的墨落地几十秒后仍是缎面而不是哑光，
    // 纯 wet 权重会让老墨区高光彻底消失（整片老墨读作平涂粉漆的根因）。
    float wetWeight = saturate(wet + sheen * 0.6);
    float sharpTerm = pow(NdotH, sharpPower)
                    * (0.35 + 0.65 * wetWeight)
                    * _P1InkCoatStrength
                    * shadowAtten * saturate(NdotV * 1.5);

    float sheenPower = exp2(4.0 * (1.0 - rough) + 1.0);
    float sheenWeight = 0.13 * (saturate(sheen) + saturate(wet) * 0.5);
    float sheenTerm = pow(NdotH, sheenPower) * sheenWeight * shadowAtten;

    half totalSpec = (half)min(sharpTerm + sheenTerm, 1.0);
    return half3(totalSpec, totalSpec, totalSpec);
#else
    // 移动端降级：单层较宽的高光（1 次 pow，无 sheen 层），保留湿润亮点但避免双 pow 开销。
    float power = exp2(6.0 * (1.0 - rough) + 1.0);
    half term = (half)(pow(NdotH, power) * (0.30 + 0.35 * saturate(wet))
                        * _P1InkCoatStrength * shadowAtten);
    return half3(term, term, term);
#endif
}

// ----------------------------------------------------------------------------
// 湿润薄缘高光带（供地面与顶面共用）
// ----------------------------------------------------------------------------
float P1InkWetRim(float amtEff, float wet, float sheen)
{
    // 窄带必须骑在「可见墨缘」上：地面 inkMask 在 amtEff≈0.5 起跳，
    // 顶面 mask 在 0.55~0.72 起跳。旧的 0.08~0.32 完全落在 mask 外，
    // 乘上 inkMask 后恒为 0（薄缘高光被吞掉）。这里取 0.30~0.88 的
    // 内缘带，峰值覆盖 0.5~0.7，并只在新墨湿润/缎面余晖期显形。
    float rimBand = smoothstep(0.30, 0.52, amtEff) * (1.0 - smoothstep(0.64, 0.88, amtEff));
    return rimBand * saturate(wet * 1.25 + sheen * 0.5);
}

// ----------------------------------------------------------------------------
// 湿润三段曲线：落墨 ageSec 秒后返回 (wet, sheen) ∈ [0,1]²
//   wet   : 镜面期（0..wetSeconds），平方衰减，前几秒最亮
//   sheen : 缎面余晖（0..sheenSeconds，应远长于 wet），三次衰减，保住「墨」而不是「灰漆」
// ----------------------------------------------------------------------------
float2 P1InkWetProfile(float ageSec, float wetSeconds, float sheenSeconds)
{
    float wet = saturate(1.0 - ageSec / max(0.25, wetSeconds));
    wet *= wet;
    float sheen = saturate(1.0 - ageSec / max(0.25, sheenSeconds));
    sheen *= sheen * sheen;
    return float2(wet, sheen);
}

// ----------------------------------------------------------------------------
// 由湿润度插值粗糙度（0 = 全干，1 = 全湿）
// ----------------------------------------------------------------------------
float P1InkRoughness(float wet, float sheen, float wetRough, float dryRough)
{
    float glossMix = saturate(wet + sheen * 0.45);
    return lerp(dryRough, wetRough, glossMix);
}

// ----------------------------------------------------------------------------
// 切线空间微表面扰动 → 世界法线
// tn        : 采样到的切线空间法线（UnpackNormalScale 输出）
// N         : 几何/基底法线
// T1,T2     : 面内正交基（地面可用 (1,0,0)/(0,0,1)；墙面用 U/V 轴）
// strength  : 扰动幅度（0 关闭；墨面建议 0.2~0.5）
// ----------------------------------------------------------------------------
float3 P1InkApplyTangentPerturb(float3 tn, float3 N, float3 T1, float3 T2, half strength)
{
    float3 n = N + (T1 * tn.x + T2 * tn.y) * strength;
    return normalize(n);
}

// ----------------------------------------------------------------------------
// 三次 B 样条图集重建（对齐 INKWAVE inkShading.js 的 paintCubic）
//
// 动机：墨迹图集按双线性采样时，覆盖率场是分片双线性的，其中心差分梯度在
// 纹元尺度上恒定 → 墨缘法线 / 接触阴影 / 队伍交界呈现纹元级「马赛克小面片」。
// 三次 B 样条重建得到 C1 连续场 + 解析梯度，墨缘圆润顺滑（INKWAVE 实机观感的
// 主要来源之一）。B 样条核无负瓣、不过冲，覆盖率场不会被滤出 >1 或 <0 的伪值。
//
// 采样 16 tap（4×4 整数纹元），每 tap 解析一阶导数权重，梯度按「每纹元」返回，
// 由调用方按需换算成每米 / 每双纹元量纲。mip 安全：所有 tap 用
// SAMPLE_TEXTURE2D_GRAD，屏幕空间导数钳到 ±8 纹元（INKWAVE textureGrad 同款钳制，
// 远观防闪；图集默认无 mip 时该钳制无副作用）。
// ----------------------------------------------------------------------------

// 三次 B 样条核：输入连续纹元坐标 x，输出 4 个整数纹元（floor(x)-1 .. floor(x)+2）
// 的权重 w 与一阶导数 dw（对 x 求导）。Σw = 1，B 样条无过冲。
void P1CubicBSplineWeights(float x, out float4 w, out float4 dw)
{
    float i0 = floor(x) - 1.0;
    float f = x - (i0 + 1.0);          // f ∈ [0,1)
    float g = 1.0 - f;
    float f2 = f * f;
    float g2 = g * g;
    w  = float4(g2 * g * (1.0 / 6.0),
                2.0 / 3.0 - f2 + f2 * f * 0.5,
                2.0 / 3.0 - g2 + g2 * g * 0.5,
                f2 * f * (1.0 / 6.0));
    dw = float4(-g2 * 0.5,
                -2.0 * f + 1.5 * f2,
                2.0 * g - 1.5 * g2,
                f2 * 0.5);
}

// 重建预乘 RGBA 并返回每纹元梯度（gradX/gradY = d/d(texelX), d/d(texelY)）。
// uv 必须已按调用方语义处理（saturate / 场外屏蔽由调用方负责）。
float4 P1SampleInkAtlasCubicGrad(
    TEXTURE2D_PARAM(tex, samp),
    float2 uv, float2 texelUV,
    float2 ddxUV, float2 ddyUV,
    out float4 gradX, out float4 gradY)
{
    float2 res = 1.0 / max(texelUV, float2(1e-8, 1e-8));
    float2 dX = clamp(ddxUV, -8.0 * texelUV, 8.0 * texelUV);
    float2 dY = clamp(ddyUV, -8.0 * texelUV, 8.0 * texelUV);

    float4 wx, dwx, wy, dwy;
    P1CubicBSplineWeights(uv.x * res.x, wx, dwx);
    P1CubicBSplineWeights(uv.y * res.y, wy, dwy);
    float baseX = floor(uv.x * res.x) - 1.0;
    float baseY = floor(uv.y * res.y) - 1.0;

    float4 col0 = 0, col1 = 0, col2 = 0, col3 = 0;    // 每行沿 X 卷积结果（供 Y 梯度）
    float4 dcol0 = 0, dcol1 = 0, dcol2 = 0, dcol3 = 0; // 每行沿 X 导数卷积结果（供 X 梯度）

    [unroll]
    for (int j = 0; j < 4; j++)
    {
        float rowV = (baseY + (float)j + 0.5) * texelUV.y;
        float4 acc = 0;
        float4 dacc = 0;
        [unroll]
        for (int i = 0; i < 4; i++)
        {
            float2 tapUV = saturate(float2((baseX + (float)i + 0.5) * texelUV.x, rowV));
            float4 t = SAMPLE_TEXTURE2D_GRAD(tex, samp, tapUV, dX, dY);
            acc += wx[i] * t;
            dacc += dwx[i] * t;
        }
        if (j == 0) { col0 = acc; dcol0 = dacc; }
        else if (j == 1) { col1 = acc; dcol1 = dacc; }
        else if (j == 2) { col2 = acc; dcol2 = dacc; }
        else { col3 = acc; dcol3 = dacc; }
    }

    // value = Σ wy[j]·col[j]；d/d(texelX) = Σ wy[j]·dcol[j]；d/d(texelY) = Σ dwy[j]·col[j]
    gradX = wy[0] * dcol0 + wy[1] * dcol1 + wy[2] * dcol2 + wy[3] * dcol3;
    gradY = dwy[0] * col0 + dwy[1] * col1 + dwy[2] * col2 + dwy[3] * col3;
    return wy[0] * col0 + wy[1] * col1 + wy[2] * col2 + wy[3] * col3;
}

// ----------------------------------------------------------------------------
// 阴影中墨水保饱和（对齐 INKWAVE INK_SHADE）：亮度守恒地提升色度。
// amount = 0 时恒等返回；漫反射越弱（背光/阴影），调用方给的 amount 越大。
// ----------------------------------------------------------------------------
half3 P1InkShadowChroma(half3 color, float amount)
{
    half lum = Luminance(color);
    half3 boosted = lerp(half3(lum, lum, lum), color, half(1.0 + amount));
    half boostedLum = max(Luminance(boosted), half(1e-4));
    return boosted * (lum / boostedLum);
}

#endif // P1_INK_WET_SHADING_INCLUDED
