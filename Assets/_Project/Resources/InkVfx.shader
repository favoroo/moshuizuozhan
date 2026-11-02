Shader "P1/InkVfx"
{
    // ============================================================================
    // P1 - InkVfx.shader
    // 溅射粒子与卫星墨珠材质专用着色器：
    // 湿润果冻高光、双层清漆镜面 (P1InkCoatSpec)、全量环境反射 (P1InkEnvSpecularFull)、
    // 菲涅尔外缘提亮与队伍色微自发光，与地面/墙面墨水共享同套湿润语言。
    // ============================================================================
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.8
        _EmissionStrength ("Emission Strength", Range(0.0, 2.0)) = 0.15
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

            #pragma multi_compile _ P1_INK_HIGH
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Shaders/InkWetShading.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
                half   fogFactor  : TEXCOORD3;
            };

            // 队色逐实例：墨珠/溅射共享同一 material，颜色进 instancing buffer 才能合批。
            UNITY_INSTANCING_BUFFER_START(PerInstance)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(PerInstance)

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
                float _EmissionStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.color = input.color * UNITY_ACCESS_INSTANCED_PROP(PerInstance, _BaseColor);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 col = input.color;
                half3 albedo = col.rgb;

                float3 N = normalize(input.normalWS);
                if (dot(N, N) < 0.001)
                {
                    N = float3(0.0, 1.0, 0.0);
                }

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(N, L));
                float NdotV = saturate(dot(N, V));
                float NdotH = saturate(dot(N, H));
                float shadowAtten = mainLight.shadowAttenuation;

                // 半兰伯特漫反射（保持背光侧依然鲜艳）
                half halfLambert = (half)(NdotL * 0.5 + 0.5);
                half shadow = lerp(0.72, 1.0, shadowAtten);
                half3 diffuseCol = albedo * (halfLambert * shadow);

                // 菲涅尔外缘微提亮（通透边缘，避免发灰，权重克制）
                float fresnel = pow(saturate(1.0 - NdotV), 3.0);
                diffuseCol += albedo * (half)(fresnel * 0.35);

                // 队色染色保护：高光染上底色，避免强光完全洗白
                half3 specTint = lerp(half3(1.0, 1.0, 1.0), albedo, 0.35);

                // 双层清漆镜面高光 (wet = 1.0, sheen = 0.4)
                half3 coatSpec = _MainLightColor.rgb * P1InkCoatSpec(NdotH, NdotV, shadowAtten, 1.0, 0.4) * specTint * 0.75;

                // 全量环境反射 (P1InkEnvSpecularFull，支持桌面 Cubemap 反射与掠射抑制)
                float3 R = reflect(-V, N);
                float rough = saturate(1.0 - _Smoothness);
                half3 envSpec = P1InkEnvSpecularFull(R, rough, NdotV, albedo) * specTint * 0.70;

                // 自发光
                half3 emission = albedo * (half)_EmissionStrength;

                half3 finalRgb = diffuseCol + coatSpec + envSpec + emission;
                finalRgb = MixFog(finalRgb, input.fogFactor);

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
