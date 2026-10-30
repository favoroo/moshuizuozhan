#ifndef CHARACTER_TOON_COMMON_INCLUDED
#define CHARACTER_TOON_COMMON_INCLUDED

// ============================================================================
// P1 - CharacterToonCommon.hlsl
// 角色专用卡通着色共享库：顶点/片元/阴影/描边函数与材质常量缓冲区
// 专为 4v4 俯视角竞技设计：半兰伯特二分阶梯、轮廓边缘光、阴影着色、紧凑高光
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "ToonLighting.hlsl"

// ----------------------------------------------------------------------------
// 常量缓冲区 (UnityPerMaterial)
// 保证 SRP Batcher 兼容：必须完整覆盖 Shader Properties 声明的所有非纹理属性。
// 必须暴露并包含运行时潜墨幽灵态所需的透明混合属性（_Surface/_Blend/_SrcBlend/_DstBlend/_ZWrite），
// 否则潜墨隐匿幽灵态的动态材质克隆与半透明切换会失效。
// ----------------------------------------------------------------------------
CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half4 _Color;
    float _Smoothness;
    float _Metallic;
    half4 _EmissionColor;
    float _EmissionStrength;
    half4 _RimColor;
    float _RimStrength;
    float _RimPower;
    half4 _ShadowTint;
    float _ShadeThreshold;
    float _ShadeSoftness;
    float _Cull;
    float _Surface;
    float _Blend;
    float _SrcBlend;
    float _DstBlend;
    float _ZWrite;
    float _ShadowFloor;
    float _SpecThreshold;
    float _SpecSoftness;
#if defined(CHARACTER_TOON_OUTLINE)
    // 描边属性仅在 CharacterToonOutlined 中启用，放于 CBUFFER 末尾保持前序字段一致
    float _OutlineWidth;
    half4 _OutlineColor;
#endif
CBUFFER_END

// 阴影投射所用的光源方向与光源位置（由 URP 阴影管线 per-light 传入，不在 UnityPerMaterial 内）
float3 _LightDirection;
float3 _LightPosition;

// ----------------------------------------------------------------------------
// ForwardLit Pass: 顶点与片元定义
// ----------------------------------------------------------------------------
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float2 uv         : TEXCOORD2;
    half   fogFactor  : TEXCOORD3;
};

Varyings vert(Attributes input)
{
    Varyings output;
    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS   = normInputs.normalWS;
    output.uv         = input.uv;
    output.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
    return output;
}

half4 frag(Varyings input) : SV_Target
{
    // 基础色（程序化角色网格可能未烘焙顶点色，故不乘顶点色）
    half4 albedo = _BaseColor;

    float3 N = normalize(input.normalWS);
    float3 V = normalize(GetCameraPositionWS() - input.positionWS);

    // 主光采样与阴影衰减
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half shadowAtten = mainLight.shadowAttenuation;

    float3 L = normalize(mainLight.direction);
    float3 H = normalize(L + V);
    half NdotH = saturate(dot(N, H));

    half band = P1ToonBand(dot(N, L), _ShadeThreshold, _ShadeSoftness);
    half shadowTerm = P1ToonShadowTerm(shadowAtten, _ShadowFloor, 0.02);
    half3 ambient = P1ToonAmbient(N, _ShadowTint.rgb, 0.72, 0.18);
    half3 shadeTint = lerp(_ShadowTint.rgb, half3(1,1,1), 0.30);
    half3 toonLight = lerp(shadeTint, half3(1,1,1), band) * shadowTerm;
    half3 diffuse = albedo.rgb * (ambient + mainLight.color * toonLight);
    half3 rim = _RimColor.rgb * _RimStrength * P1ToonRim(saturate(dot(N,V)), _RimPower, shadowAtten);
    half spec = P1ToonSpec(NdotH, _SpecThreshold, _SpecSoftness, _Smoothness * 0.55, shadowAtten);
    half3 specular = spec * mainLight.color;

    // 5. 自发光
    half3 emission = _EmissionColor.rgb * _EmissionStrength;

    // 汇总各光照分量
    half3 finalRgb = diffuse + specular + rim + emission;

    // 6. 雾效收尾
    finalRgb = MixFog(finalRgb, input.fogFactor);

    // 半透明变体分支：潜墨隐匿幽灵态下返回 albedo.a，否则恒定不透明 1.0
#if defined(_SURFACE_TYPE_TRANSPARENT)
    return half4(finalRgb, albedo.a);
#else
    return half4(finalRgb, 1.0);
#endif
}

// ----------------------------------------------------------------------------
// ShadowCaster Pass: 阴影投射定义（与 PropLit 保持完全一致）
// ----------------------------------------------------------------------------
struct ShadowAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

struct ShadowVaryings
{
    float4 positionCS : SV_POSITION;
};

ShadowVaryings ShadowPassVertex(ShadowAttributes input)
{
    ShadowVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif

    output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    #if UNITY_REVERSED_Z
        output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
        output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    return output;
}

half4 ShadowPassFragment(ShadowVaryings input) : SV_Target
{
    return 0;
}

// ----------------------------------------------------------------------------
// Outline Pass: 仅 Outlined 材质启用的反向法线外扩描边（不受透明变体影响）
// ----------------------------------------------------------------------------
#if defined(CHARACTER_TOON_OUTLINE)
struct OutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
};

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    half   fogFactor  : TEXCOORD0;
};

OutlineVaryings OutlinePassVertex(OutlineAttributes input)
{
    OutlineVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float camDist = distance(positionWS, GetCameraPositionWS());
    float widthScale = clamp(camDist / 4.5, 0.7, 2.2);
    float3 posOS = input.positionOS.xyz + input.normalOS * _OutlineWidth * widthScale;
    output.positionCS = TransformObjectToHClip(posOS);
    output.fogFactor  = ComputeFogFactor(output.positionCS.z);
    return output;
}

half4 OutlinePassFragment(OutlineVaryings input) : SV_Target
{
    half3 rgb = _OutlineColor.rgb;
    rgb = MixFog(rgb, input.fogFactor);
    return half4(rgb, 1.0);
}
#endif

#endif // CHARACTER_TOON_COMMON_INCLUDED
