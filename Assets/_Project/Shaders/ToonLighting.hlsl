#ifndef P1_TOON_LIGHTING_INCLUDED
#define P1_TOON_LIGHTING_INCLUDED

// ============================================================================
// P1 - ToonLighting.hlsl
// 全场景共享的三渲二光照函数：二分色阶、冷暖分离阴影、硬边高光、轮廓光。
// 只提供纯函数，不声明材质属性 / CBUFFER，由各 Shader 自行声明，保持 SRP Batcher 兼容。
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// 半兰伯特二分色阶：0 = 暗部，1 = 亮部。softness 控制交界抗锯齿宽度，越小越硬。
half P1ToonBand(float NdotL, float threshold, float softness)
{
    float halfLambert = NdotL * 0.5 + 0.5;
    float s = max(softness, 0.001);
    return (half)smoothstep(threshold - s, threshold + s, halfLambert);
}

// 实时阴影衰减：把软阴影收成较清晰的明暗交界，并保留 shadowFloor 的最低直射亮度，
// 避免卡通暗部塌成死黑。
half P1ToonShadowTerm(float shadowAtten, float shadowFloor, float softness)
{
    float s = max(softness, 0.001) * 4.0;
    float crisp = smoothstep(0.5 - s, 0.5 + s, shadowAtten);
    return (half)lerp(saturate(shadowFloor), 1.0, crisp);
}

// 环境光：球谐天空光 + 阴影染色保底，保证暗部是冷色而不是灰色。
half3 P1ToonAmbient(float3 normalWS, half3 shadowTint, half scale, half floorStrength)
{
    half3 sh = SampleSH(normalWS) * scale;
    return sh + shadowTint * floorStrength;
}

// 卡通漫反射：albedo ×（环境光 + 主光 × 色阶 × 阴影项 × 冷暖染色）。
half3 P1ToonDiffuse(
    half3 albedo,
    half3 lightColor,
    float NdotL,
    half shadowAtten,
    float threshold,
    float softness,
    float shadowFloor,
    half3 shadowTint,
    half3 ambient)
{
    half band = P1ToonBand(NdotL, threshold, softness);
    half shadowTerm = P1ToonShadowTerm(shadowAtten, shadowFloor, softness);
    // 阴影色与白色混合 25%，避免暖色反照率被纯蓝阴影压成脏褐色。
    half3 shadeTint = lerp(shadowTint, half3(1.0, 1.0, 1.0), 0.25);
    half3 litTint = lerp(shadeTint, half3(1.0, 1.0, 1.0), band);
    // 直射项只用 shadowTerm 收阴影，不能再乘 band：band 在背光面为 0，
    // 相乘会把背光/侧向面（集装箱、木箱、花坛的竖直立面）的直射光整体归零，
    // 只剩很弱的环境光，观感就是"物体侧面一直是黑色"。角色着色
    // （CharacterToonCommon.hlsl）一直是不乘 band 的写法，这里与之对齐。
    half3 direct = lightColor * shadowTerm * litTint;
    return albedo * (ambient + direct);
}

// 硬边卡通高光块：高光只在亮部出现，强度由 strength 控制。
half P1ToonSpec(float NdotH, float threshold, float softness, float strength, half shadowAtten)
{
    float s = max(softness, 0.001);
    half band = (half)smoothstep(threshold - s, threshold + s, NdotH);
    return band * strength * shadowAtten;
}

// 菲涅尔轮廓光：阴影区保留 35% 基础轮廓，避免暗部完全失去边缘。
half P1ToonRim(float NdotV, float power, half shadowAtten)
{
    half fres = (half)pow(saturate(1.0 - NdotV), max(power, 0.01));
    return fres * (0.35 + 0.65 * shadowAtten);
}

#endif // P1_TOON_LIGHTING_INCLUDED