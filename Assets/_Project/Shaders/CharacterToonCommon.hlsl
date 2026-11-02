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
    // P31 可选 albedo 细节层（纹理属性不进 CBUFFER，但 _ST 缩放偏移要进以保持 SRP Batcher 兼容）
    float4 _BaseMap_ST;
    float4 _DetailNormalMap_ST;
    float _DetailNormalStrength;
#if defined(CHARACTER_TOON_OUTLINE)
    // 描边属性仅在 CharacterToonOutlined 中启用，放于 CBUFFER 末尾保持前序字段一致
    float _OutlineWidth;
    half4 _OutlineColor;
#endif
CBUFFER_END

// P31 可选 albedo / 细节法线纹理（默认 white / bump，材质不赋值时零视觉差异）
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_DetailNormalMap);
SAMPLER(sampler_DetailNormalMap);

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
    UNITY_VERTEX_INPUT_INSTANCE_ID
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
    UNITY_SETUP_INSTANCE_ID(input);
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
    // P31 albedo 细节层：默认 white（不贴图时与旧版纯色完全一致），
    // 贴图时以乘法混入织物/壳面肌理，程序化网格 UV 未展开也不影响正确性。
    float2 baseUV = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    half3 albedoTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV).rgb;
    half4 albedo = half4(_BaseColor.rgb * albedoTex, _BaseColor.a);

    float3 N = normalize(input.normalWS);
    float3 V = normalize(GetCameraPositionWS() - input.positionWS);

    // P31 细节法线：强度 0（默认）时 UnpackNormalScale 返回平面，零扰动零开销差异
    float3 detailN = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, baseUV * _DetailNormalMap_ST.xy),
        _DetailNormalStrength);
    float3 refT = normalize(cross(float3(0.0, 1.0, 0.0), N));
    float3 refB = cross(N, refT);
    N = normalize(N + refT * detailN.x + refB * detailN.y);

    // 主光采样与阴影衰减
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half shadowAtten = mainLight.shadowAttenuation;

    float3 L = normalize(mainLight.direction);
    float3 H = normalize(L + V);
    half NdotH = saturate(dot(N, H));

    half band = P1ToonTriBand(dot(N, L), _ShadeThreshold, _ShadeSoftness);
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
    // 2026-11-01 白光问题收口：二值卡通高光带（smoothness 0.5×0.55 ≈ 0.275 × 太阳 1.6 ≈ +0.44 HDR）
    // 会在半向量对齐的瞬间把发顶/白衣/墨罐这些光滑部件从 ~0.9 直接顶到 HDR≈1.4，
    // 越过全局 Bloom 阈值 1.05，被放大成"满场角色乱闪大团白晕"（站立不动也会随待机摇头周期性触发）。
    // 角色不需要任何 Bloom（emission 默认 0），这里把输出钉在 0.99：乘 postExposure(0.08→×1.057)
    // 后恰好压在阈值 1.05 之下，角色永远不进 Bloom；需要起辉的场景物件请走 PropLit 自发光，不要改回这里。
    half3 finalRgb = min(diffuse + specular + rim + emission, half3(0.99, 0.99, 0.99));

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
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShadowVaryings
{
    float4 positionCS : SV_POSITION;
};

ShadowVaryings ShadowPassVertex(ShadowAttributes input)
{
    ShadowVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
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
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    half   fogFactor  : TEXCOORD0;
};

OutlineVaryings OutlinePassVertex(OutlineAttributes input)
{
    OutlineVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
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
