Shader "P1/PropLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.2
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionStrength ("Emission Strength", Float) = 0.0
        _AmbientBoost ("Ambient Boost", Range(0.0, 2.0)) = 1.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2

        [Header(Surface PBR P18)]
        _SurfaceN ("Surface Normal (tileable)", 2D) = "bump" {}
        _SurfaceRMO ("Surface RMO (R=metal G=AO B=detail A=smooth)", 2D) = "black" {}
        _SurfaceTexelScale ("Surface Texel Scale (periods/m)", Range(0.05, 8)) = 0.5
        _SurfaceNormalStrength ("Surface Normal Strength", Range(0, 1.5)) = 0.6
        _SurfaceAOStrength ("Surface AO Strength", Range(0, 1.5)) = 0.7
        _SurfaceSmoothnessScale ("Surface Smoothness Scale", Range(0, 2)) = 1.0

        [Header(Toon Shading)]
        _ShadeThreshold ("Shade Threshold", Range(0.2, 0.8)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.005, 0.2)) = 0.03
        _ShadowTint ("Shadow Tint", Color) = (0.40, 0.45, 0.62, 1)
        _ShadowFloor ("Shadow Floor", Range(0.0, 1.0)) = 0.42
        _RimColor ("Rim Color", Color) = (0.75, 0.86, 1.0, 1)
        _RimStrength ("Rim Strength", Range(0.0, 1.0)) = 0.18
        _RimPower ("Rim Power", Range(0.5, 8.0)) = 3.0
        _SpecStrength ("Spec Strength", Range(0.0, 2.0)) = 0.5
        _SpecThreshold ("Spec Threshold", Range(0.5, 0.999)) = 0.95
        _SpecSoftness ("Spec Softness", Range(0.001, 0.1)) = 0.02
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体；GRD 启用时走 BatchRendererGroup 提交，关闭时走原渲染路径
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ P1_INK_HIGH

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"
            #include "InkSurfaceOverlay.hlsl"
            #include "InkWetShading.hlsl"

            TEXTURE2D(_SurfaceN);
            SAMPLER(sampler_SurfaceN);
            TEXTURE2D(_SurfaceRMO);
            SAMPLER(sampler_SurfaceRMO);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                float _Smoothness;
                float _Metallic;
                float _EmissionStrength;
                float _AmbientBoost;
                float _Cull;
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                half4 _RimColor;
                float _RimStrength;
                float _RimPower;
                float _SpecStrength;
                float _SpecThreshold;
                float _SpecSoftness;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = normInputs.normalWS;
                output.color      = input.color;
                output.uv         = input.uv;
                output.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 顶点色与基础色混合
                half4 albedo = _BaseColor * input.color;

                float3 geomN = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

                #if defined(P1_INK_HIGH)
                    float2 surfUV;
                    float3 surfT, surfB;
                    if (abs(geomN.y) > 0.5)
                    {
                        surfUV = input.positionWS.xz * _SurfaceTexelScale;
                        surfT = float3(1.0, 0.0, 0.0);
                        surfB = float3(0.0, 0.0, 1.0);
                    }
                    else
                    {
                        if (abs(geomN.x) > abs(geomN.z))
                        {
                            surfUV = input.positionWS.zy * _SurfaceTexelScale;
                            surfT = float3(0.0, 0.0, 1.0);
                            surfB = float3(0.0, 1.0, 0.0);
                        }
                        else
                        {
                            surfUV = input.positionWS.xy * _SurfaceTexelScale;
                            surfT = float3(1.0, 0.0, 0.0);
                            surfB = float3(0.0, 1.0, 0.0);
                        }
                    }

                    half4 nSample = SAMPLE_TEXTURE2D(_SurfaceN, sampler_SurfaceN, surfUV);
                    half3 tsN = UnpackNormalScale(nSample, _SurfaceNormalStrength);
                    float3 worldN = normalize(geomN + (surfT * tsN.x + surfB * tsN.y));

                    half4 rmo = SAMPLE_TEXTURE2D(_SurfaceRMO, sampler_SurfaceRMO, surfUV);
                    half ao = clamp(lerp(1.0, rmo.g, _SurfaceAOStrength), 0.35, 1.0);
                    float metallic = saturate(max(_Metallic, rmo.r));
                    float smoothness = saturate(lerp(_Smoothness, rmo.a * _SurfaceSmoothnessScale, 0.85));
                    albedo.rgb *= (0.88 + 0.24 * rmo.b);
                #else
                    float3 worldN = geomN;
                    half ao = 1.0;
                    float metallic = _Metallic;
                    float smoothness = _Smoothness;
                #endif

                // 主光采样与阴影衰减
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadowAtten = mainLight.shadowAttenuation;

                float3 L = normalize(mainLight.direction);
                float3 H = normalize(L + viewDirWS);

                float NdotH = saturate(dot(worldN, H));

                half3 ambient = P1ToonAmbient(worldN, _ShadowTint.rgb, _AmbientBoost, 0.06);
                half3 diffuse = P1ToonDiffuse(albedo.rgb, mainLight.color, dot(worldN, L), shadowAtten,
                                              _ShadeThreshold, _ShadeSoftness, _ShadowFloor, _ShadowTint.rgb, ambient);
                diffuse *= ao;

                half spec = P1ToonSpec(NdotH, _SpecThreshold, _SpecSoftness, _SpecStrength * (0.5 + metallic) * (0.3 + 1.4 * smoothness), shadowAtten);
                half3 specColor = lerp(mainLight.color, mainLight.color * albedo.rgb, metallic);
                half3 specular = specColor * spec;

                half rim = P1ToonRim(saturate(dot(worldN, viewDirWS)), _RimPower, shadowAtten) * _RimStrength;
                half3 emission = _EmissionColor.rgb * _EmissionStrength;

                // 环境镜面（纯数学，不分支）
                float3 R = reflect(-viewDirWS, worldN);
                float rough = saturate(1.0 - smoothness);
                half3 envSpec = P1InkEnvSpecular(R, rough, saturate(dot(worldN, viewDirWS)));
                envSpec *= lerp(half3(1.0, 1.0, 1.0), albedo.rgb, metallic) * ao;

                half3 finalRgb = diffuse + specular + _RimColor.rgb * rim + emission + envSpec;

                ApplyTopInkOverlay(finalRgb, input.positionWS, geomN, shadowAtten, L, viewDirWS);

                // 雾效
                finalRgb = MixFog(finalRgb, input.fogFactor);

                return half4(finalRgb, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（shadowmap 同样走 GRD 批处理）
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                float _Smoothness;
                float _Metallic;
                float _EmissionStrength;
                float _AmbientBoost;
                float _Cull;
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                half4 _RimColor;
                float _RimStrength;
                float _RimPower;
                float _SpecStrength;
                float _SpecThreshold;
                float _SpecSoftness;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

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
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（DepthNormals prepass 同样走 GRD 批处理）
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                float _Smoothness;
                float _Metallic;
                float _EmissionStrength;
                float _AmbientBoost;
                float _Cull;
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                half4 _RimColor;
                float _RimStrength;
                float _RimPower;
                float _SpecStrength;
                float _SpecThreshold;
                float _SpecSoftness;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
            CBUFFER_END

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
