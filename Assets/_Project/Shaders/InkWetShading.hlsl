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
    float3 L = normalize(GetMainLight().direction);
    float sd = saturate(dot(R, L));
    float discPow = max(8.0, lerp(_P1EnvSunDiscPower, 80.0, rough));
    half disc = (half)pow(sd, discPow) * (half)lerp(1.0, 0.25, rough);
    disc *= (half)saturate(NdotV * 2.0);

    half3 env = sky + _MainLightColor.rgb * disc * 1.2;

    // 清漆层菲涅尔（F0 = 0.04）。掠射上限压到 0.55：墨水是颜料液体，掠射处若让环境白光
    // 全额进入，会把俯视角大斜率区域的队色整体洗浅（P18 实测）。
    float fres = pow(saturate(1.0 - NdotV), 5.0);
    half F = (half)(0.04 + 0.51 * fres);
    half3 spec = env * F * (half)_P1EnvSpecStrength;
    // 单通道硬上限：任何角度下环境镜面都不允许把队色整体洗白（只保留 glitter 峰值）
    return min(spec, (half3)1.0);
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

#endif // P1_INK_WET_SHADING_INCLUDED
