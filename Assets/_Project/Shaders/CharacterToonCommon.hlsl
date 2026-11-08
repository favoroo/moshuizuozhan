#ifndef CHARACTER_TOON_COMMON_INCLUDED
#define CHARACTER_TOON_COMMON_INCLUDED

// ============================================================================
// P1 - CharacterToonCommon.hlsl
// 角色专用卡通着色共享库：顶点/片元/阴影/描边函数与材质常量缓冲区
// 专为 4v4 第三人称竞技设计：半兰伯特二分阶梯、轮廓边缘光、阴影着色、紧凑高光
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
    // P32 敌方墨覆盖 + 受击闪白：CombatantHealth 用 MaterialPropertyBlock 逐单位覆盖，
    // 默认 0 → 逐像素等同旧观感（与描边属性一样放在 CBUFFER 中段，末尾留给 Outlined 独有字段）
    float _FlashAmount;
    half4 _InkColor;
    float _InkAmount;
    float _InkSeed;
    float _InkThresholdMin;
    float _InkThresholdMax;
    float _InkCurvePower;
    // 每个 renderer 的静止位姿坐标系（CombatantHealth 用 MPB 逐部件推送）；
    // Vector 属性必须是 float4，XYZ 取位置/缩放（SRP Batcher 要求 CBUFFER 类型与 Properties 一致）
    float4 _InkPartOffset;
    float4 _InkPartScale;
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
// P32 角色被墨覆盖用的程序化噪声（对齐 INKWAVE character-mats.js 的 iwNoise / iwFbm）：
// 纯 HLSL 内建、零纹理采样、完全自包含（沿用 InkWallStyle.hlsl 的 P1* 纯函数约定）。
// ----------------------------------------------------------------------------
float P1CharHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

/// 3D 值噪声（smoothstep 插值），返回 0..1
float P1CharNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    float n000 = P1CharHash31(i + float3(0, 0, 0));
    float n100 = P1CharHash31(i + float3(1, 0, 0));
    float n010 = P1CharHash31(i + float3(0, 1, 0));
    float n110 = P1CharHash31(i + float3(1, 1, 0));
    float n001 = P1CharHash31(i + float3(0, 0, 1));
    float n101 = P1CharHash31(i + float3(1, 0, 1));
    float n011 = P1CharHash31(i + float3(0, 1, 1));
    float n111 = P1CharHash31(i + float3(1, 1, 1));

    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

/// 3 倍频 fbm（权重归一，值域约 0..1）：低频决定墨斑团块形状，高频只负责打断直边。
/// 实测分布（1.2m 高身体表面采样）：中位数 ~0.50、p90 ~0.68 —— 覆盖阈值曲线按此标定。
float P1CharFbm3(float3 p)
{
    float f = 0.6000 * P1CharNoise3(p);
    f += 0.2500 * P1CharNoise3(p * 2.03 + 7.7);
    f += 0.1500 * P1CharNoise3(p * 3.11 + 19.1);
    return saturate(f);
}

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
    // P32 墨覆盖墨斑在「绑定/网格本地空间」求值：墨必须黏在身体上，不能随角色移动滑动
    float3 positionOS : TEXCOORD4;
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
    output.positionOS = input.positionOS.xyz;
    return output;
}

half4 frag(Varyings input) : SV_Target
{
    // P31 albedo 细节层：默认 white（WebGL 未绑定 BaseMap 时直接用 BaseColor 避免采样返 0 变黑）
#if UNITY_WEBGL && !UNITY_EDITOR
    half4 albedo = _BaseColor;
#else
    float2 baseUV = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    half3 albedoTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV).rgb;
    half4 albedo = half4(_BaseColor.rgb * albedoTex, _BaseColor.a);
#endif

    float3 N = normalize(input.normalWS);
    float3 V = normalize(GetCameraPositionWS() - input.positionWS);

    // P32 敌方墨覆盖（对齐 INKWAVE character-mats.js HURT_FRAG）：绑定空间程序化噪声墨斑，
    // 覆盖量升高 → 阈值从 _InkThresholdMax 降到 _InkThresholdMin，墨斑从零散血点扩展到大半个身体；
    // 墨斑边缘加一圈压暗的「湿边」，并压掉该处卡通高光，读作「身体被糊了一层敌墨」。
    // _InkAmount = 0（默认）时整段被跳过，逐像素等同旧观感。
    half inkMask = 0.0h;
    half3 baseAlbedo = albedo.rgb;
    if (_InkAmount > 0.002)
    {
        // 顶点先还原到「角色空间的静止世界坐标」再求噪声：角色由 ~70 件刚体部件拼成，
        // 各部件网格的 object space 互不相同，直接用 positionOS 会让左右腿/手臂得到镜像重复的墨斑。
        float3 restPos = _InkPartOffset.xyz + input.positionOS * _InkPartScale.xyz;
        float3 hp = restPos * float3(13.0, 8.5, 13.0) + _InkSeed;
        hp.y += 0.35 * P1CharNoise3(restPos * 12.0);
        float n = P1CharFbm3(hp);
        // 覆盖曲线：th = lerp(ThresholdMax, ThresholdMin, amount^power)。
        // 三个参数是按本噪声的实际分布（body 表面采样）标定的：
        // 轻命中 0.05 → 约 5% 表面、半管 0.5 → 约 29%、糊满 1.0 → 约 52%。
        // 上限刻意锁在 ~52% 而不是 100%：整身换成敌方队色会让 4v4 里分不清敌我，
        // 半身覆盖已经足够读作「被糊了一身敌墨」，同时保留本队色可辨认。
        float a = pow(saturate(_InkAmount), max(_InkCurvePower, 0.05));
        float th = lerp(_InkThresholdMax, _InkThresholdMin, a);
        float w = clamp(fwidth(n) * 0.9, 0.004, 0.15);
        inkMask = (half)smoothstep(th - w, th + w, n);

        // 墨面本身要有一层深浅不匀的「稠墨」质感：整块纯色会被读成「换了个颜色的零件」，
        // 叠一层更高频的噪声做浓淡，才像糊上去的湿墨。
        half3 inkTint = _InkColor.rgb * (half)(0.84 + 0.24 * P1CharNoise3(restPos * 31.0 + 3.7));
        float rimLo = th - 0.05;
        float rimHi = th - w;
        half rim = rimHi > rimLo ? (half)smoothstep(rimLo, rimHi, n) * (1.0h - inkMask) : 0.0h;
        baseAlbedo = lerp(baseAlbedo, baseAlbedo * 0.80h, rim * 0.6h);
        baseAlbedo = lerp(baseAlbedo, inkTint, inkMask);
    }

    // P32 受击闪白：改由 _FlashAmount 驱动（旧实现经 MPB 直接改 _BaseColor，会和墨覆盖抢同一块
    // MaterialPropertyBlock —— 白闪结束时的 SetPropertyBlock(null) 会把墨覆盖一起抹掉）
    baseAlbedo = lerp(baseAlbedo, half3(1.0, 1.0, 1.0), saturate(_FlashAmount));
    albedo.rgb = baseAlbedo;

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
    // P32 被墨糊住的部位不再有干净的卡通高光（湿润但哑光，和地面积墨观感一致）
    spec *= 1.0h - inkMask * 0.5h;
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
