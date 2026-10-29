Shader "P1/ObstacleDitherLit"
{
    Properties
    {
        [Header(Surface Appearance)]
        _BaseColor ("Base Color", Color) = (0.70, 0.73, 0.78, 1.0)
        _TrimColor ("Trim / Groove Color", Color) = (0.42, 0.45, 0.50, 1.0)
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.35
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.10

        [Header(Container Styling)]
        _CorrugationScale ("Corrugation Frequency", Float) = 2.5
        _CorrugationDepth ("Corrugation Depth", Range(0.0, 1.0)) = 0.28
        [Toggle] _EnableCorrugation ("Enable Corrugation Ribs", Float) = 1.0

        [Header(Surface Detail)]
        _EdgeHighlight ("Edge Highlight", Range(0.0, 2.0)) = 0.25
        _PanelLineStrength ("Panel Line Strength", Range(0.0, 1.0)) = 0.35
        _DetailNoiseStrength ("Detail Noise Strength", Range(0.0, 1.0)) = 0.12

        [Header(Dither Occlusion Settings)]
        _DitherAlpha ("Dither Alpha", Range(0.0, 1.0)) = 1.0
        _FocusPos ("Focus World Pos", Vector) = (0, 0, 0, 0)
        _FocusRadius ("Focus Radius", Float) = 2.2
        _FocusMinAlpha ("Focus Min Alpha", Range(0.0, 1.0)) = 0.25
        _FocusWeight ("Focus Weight", Range(0.0, 1.0)) = 0.0
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

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TrimColor;
                float _Smoothness;
                float _Metallic;
                float _CorrugationScale;
                float _CorrugationDepth;
                float _EnableCorrugation;
                float _EdgeHighlight;
                float _PanelLineStrength;
                float _DetailNoiseStrength;
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
            CBUFFER_END

            static const float4x4 _BayerMatrix4x4 = float4x4(
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            );

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. 俯视角网点半透明遮挡剔除 (Screen-Door Dither Occlusion)
                float effectiveAlpha = _DitherAlpha;
                if (_FocusWeight > 0.001)
                {
                    float d = length(input.positionWS.xz - _FocusPos.xz);
                    float focusFade = saturate(d / max(0.1, _FocusRadius));
                    effectiveAlpha = lerp(_FocusMinAlpha, _DitherAlpha, focusFade);
                }

                if (effectiveAlpha < 0.999)
                {
                    float2 screenPixel = input.positionCS.xy;
                    int bx = int(fmod(screenPixel.x, 4.0));
                    int by = int(fmod(screenPixel.y, 4.0));
                    if (_BayerMatrix4x4[by][bx] >= effectiveAlpha)
                    {
                        discard;
                    }
                }

                // 2. 集装箱条纹瓦楞造型 (Container Corrugation Ribs)
                float3 geomN = normalize(input.normalWS);
                half3 surfaceColor = _BaseColor.rgb;

                // 2.1 混凝土/钢板结构细节：踢脚线、压顶、横向板缝与细噪点
                float sideMask = 1.0 - saturate(abs(geomN.y) * 1.5);
                if (sideMask > 0.02)
                {
                    float plinth = smoothstep(0.10, 0.055, input.uv.y);
                    float coping = smoothstep(0.90, 0.945, input.uv.y);
                    surfaceColor = lerp(surfaceColor, _TrimColor.rgb, (plinth * 0.55 + coping * 0.32) * sideMask);

                    float seamCoord = abs(frac(input.uv.y * 4.0) - 0.5);
                    float seam = smoothstep(0.035, 0.0, seamCoord);
                    surfaceColor = lerp(surfaceColor, _TrimColor.rgb, seam * _PanelLineStrength * 0.22 * sideMask);

                    surfaceColor += _BaseColor.rgb * coping * _EdgeHighlight * 0.08 * sideMask;

                    float grain = frac(sin(dot(floor(input.positionWS.xz * 9.0 + input.uv.y * 31.0),
                        float2(12.9898, 78.233))) * 43758.5453);
                    surfaceColor *= 1.0 - (grain - 0.5) * _DetailNoiseStrength;
                }

                if (_EnableCorrugation > 0.5)
                {
                    // 仅立面呈现竖直凹凸楞纹，顶面与底面保持平滑
                    float sideFactor = 1.0 - saturate(abs(geomN.y) * 1.5);
                    if (sideFactor > 0.05)
                    {
                        // 沿 X 或 Z 坐标取周期性波纹
                        float coord = (abs(geomN.x) > 0.5) ? input.positionWS.z : input.positionWS.x;
                        float rib = abs(frac(coord * _CorrugationScale) - 0.5) * 2.0; // 0..1
                        float ribSmooth = smoothstep(0.15, 0.85, rib);
                        half3 ribColor = lerp(_TrimColor.rgb, _BaseColor.rgb, ribSmooth);
                        surfaceColor = lerp(surfaceColor, ribColor, sideFactor * _CorrugationDepth);
                    }
                    else
                    {
                        // 顶面边缘轻微修边
                        float edgeDist = min(frac(input.uv.x * 4.0), frac(input.uv.y * 4.0));
                        if (edgeDist < 0.05)
                        {
                            surfaceColor = lerp(surfaceColor, _TrimColor.rgb, 0.35);
                        }
                    }
                }

                // 3. 基础光照与高光（含主光阴影，道具/柜体才会真正落地）
                Light mainLight = GetMainLight();
                float shadowAtten = mainLight.shadowAttenuation;
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(geomN, L));
                float NdotH = saturate(dot(geomN, H));

                // 漫反射与暗部环境补光
                half3 ambient = SampleSH(geomN) * 0.45 + half3(0.18, 0.20, 0.24);
                half3 diffuse = surfaceColor * (ambient + mainLight.color * ((NdotL * 0.75 + 0.25) * shadowAtten));

                // 粗糙度高光
                float specPower = exp2(8.0 * _Smoothness + 1.0);
                float spec = pow(NdotH, specPower) * (NdotL * 0.8 + 0.2) * _Metallic;
                half3 specular = mainLight.color * (spec * shadowAtten);

                half3 finalColor = MixFog(diffuse + specular, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TrimColor;
                float _Smoothness;
                float _Metallic;
                float _CorrugationScale;
                float _CorrugationDepth;
                float _EnableCorrugation;
                float _EdgeHighlight;
                float _PanelLineStrength;
                float _DetailNoiseStrength;
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
            CBUFFER_END

            static const float4x4 _BayerMatrix4x4 = float4x4(
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            );

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                // 当遮挡淡化激活时，阴影也进行 Dither 剔除，防止角色被遮挡时处于生硬的全黑投影下
                float effectiveAlpha = _DitherAlpha;
                if (_FocusWeight > 0.001)
                {
                    float d = length(input.positionWS.xz - _FocusPos.xz);
                    float focusFade = saturate(d / max(0.1, _FocusRadius));
                    effectiveAlpha = lerp(_FocusMinAlpha, _DitherAlpha, focusFade);
                }

                if (effectiveAlpha < 0.999)
                {
                    float2 screenPixel = input.positionCS.xy;
                    int bx = int(fmod(screenPixel.x, 4.0));
                    int by = int(fmod(screenPixel.y, 4.0));
                    if (_BayerMatrix4x4[by][bx] >= effectiveAlpha)
                    {
                        discard;
                    }
                }
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
