Shader "P1/InkBody"
{
    // ============================================================================
    // P1 - InkBody.shader (P18 画风重做)
    // 飞行墨滴/墨水手雷/风暴云发生器墨体专用着色器：
    // 假球法线、边缘菲涅尔暗边、背光透光内亮斑、太阳高光与 P1InkEnvSpecular 环境镜面，
    // 以及队色纯度保护（高光不发白洗脱队伍色）。
    // ============================================================================
    Properties
    {
        [Header(Base Color)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Color ("Color (Fallback)", Color) = (1, 1, 1, 1)

        [Header(Wet Shading)]
        _WetGloss ("Wet Gloss (Roughness Low)", Range(0.01, 0.5)) = 0.08
        _DryGloss ("Dry Gloss (Roughness High)", Range(0.1, 1.0)) = 0.38
        _EnvSpec ("Env Specular Strength", Range(0.0, 3.0)) = 1.0

        [Header(Stylized Gel Shading)]
        _FresnelDarkEdge ("Fresnel Dark Edge", Range(0.0, 1.0)) = 0.65
        _BacklightTransmittance ("Backlight Transmittance", Range(0.0, 2.0)) = 0.75
        _SpecTintFactor ("Spec Tint Factor", Range(0.0, 1.0)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/InkWetShading.hlsl"

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
                float3 positionOS : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                float _WetGloss;
                float _DryGloss;
                float _EnvSpec;
                float _FresnelDarkEdge;
                float _BacklightTransmittance;
                float _SpecTintFactor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 队伍颜色读取：兼容 _BaseColor 与 _Color 两套命名
                half4 rawColor = _BaseColor.a > 0.001 ? _BaseColor : _Color;
                half3 baseColor = rawColor.rgb;

                // 法线解算（假球法线兜底）：当顶点法线过小/退化时，使用局部中心发散向量
                float3 N = input.normalWS;
                if (dot(N, N) < 0.001)
                {
                    float3 pseudoLocal = length(input.positionOS) > 0.001
                        ? normalize(input.positionOS)
                        : float3(0.0, 1.0, 0.0);
                    N = TransformObjectToWorldNormal(pseudoLocal);
                }
                N = normalize(N);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
                float NdotV = saturate(dot(N, V));
                float NdotL = saturate(dot(N, L));

                // 漫反射明暗阶梯（清亮果冻感，阴影最低保住 0.75 鲜艳度）
                half halfLambert = (half)(NdotL * 0.5 + 0.5);
                half shadow = lerp(0.75, 1.0, mainLight.shadowAttenuation);
                half3 diffuseCol = baseColor * (halfLambert * shadow);

                // 边缘菲涅尔暗边：水滴轮廓收缩感，对齐 Splatoon / INKWAVE
                float edgeDark = pow(saturate(1.0 - NdotV), 2.8) * _FresnelDarkEdge;
                diffuseCol = lerp(diffuseCol, diffuseCol * 0.38, (half)edgeDark);

                // 背光透光内亮斑（Subsurface scattering 透光）
                float transDot = saturate(dot(-V, L + N * 0.35));
                half trans = (half)(pow(transDot, 3.2) * _BacklightTransmittance);
                diffuseCol += baseColor * trans * 1.6;

                // 太阳高光 + P1InkEnvSpecular 环境反射
                float3 R = reflect(-V, N);
                float rough = lerp(_DryGloss, _WetGloss, 0.9);
                half3 envSpec = P1InkEnvSpecular(R, rough, NdotV) * (half)_EnvSpec;

                // 队色纯度保护：镜面高光染上队伍底色，强光下不发白洗脱颜色
                half3 specTint = lerp(half3(1.0, 1.0, 1.0), baseColor, (half)_SpecTintFactor);
                half3 finalSpec = envSpec * specTint;

                half3 finalRgb = diffuseCol + finalSpec;
                finalRgb = MixFog(finalRgb, input.fogFactor);

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
