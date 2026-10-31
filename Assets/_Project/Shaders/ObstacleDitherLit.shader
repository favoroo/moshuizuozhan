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
        _DetailNoiseStrength ("Detail Noise Strength", Range(0.0, 1.0)) = 0.04

        [Header(Surface PBR P18)]
        _SurfaceN ("Surface Normal (tileable)", 2D) = "bump" {}
        _SurfaceRMO ("Surface RMO (R=metal G=AO B=detail A=smooth)", 2D) = "black" {}
        _SurfaceTexelScale ("Surface Texel Scale (periods/m)", Range(0.05, 8)) = 0.5
        _SurfaceNormalStrength ("Surface Normal Strength", Range(0, 1.5)) = 0.6
        _SurfaceAOStrength ("Surface AO Strength", Range(0, 1.5)) = 0.7
        _SurfaceSmoothnessScale ("Surface Smoothness Scale", Range(0, 2)) = 1.0

        [Header(Cover Edge Line)]
        // P17-A：掩体每个面四周一圈深色硬边线（世界米宽），压在墨水叠加之后，
        // 涂满墨后掩体轮廓依然可辨（否则墨色统一后掩体与地面不可区分）。
        _EdgeLineColor ("Edge Line Color", Color) = (0.09, 0.10, 0.13, 1.0)
        _EdgeLineWidth ("Edge Line Width (m)", Float) = 0.05
        _EdgeLineStrength ("Edge Line Strength", Range(0.0, 1.0)) = 0.85
        _TopLift ("Top Face Brightness Lift", Range(0.0, 0.5)) = 0.06

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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体；GRD 启用时走 BatchRendererGroup 提交，关闭时走原渲染路径
            // 注：带 MPB（OcclusionFader 淡化中）的 renderer 会被 GRD 排除并自动回退普通路径，仍正确
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
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float3 edgeDist3  : TEXCOORD4; // 到三条棱边组的距离（米，见 vert）
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
                half4 _EdgeLineColor;
                float _EdgeLineWidth;
                float _EdgeLineStrength;
                float _TopLift;
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
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
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

                // 掩体均为轴对齐盒（object space 半径恒 0.5）：每轴乘世界尺度，
                // 得到"到该轴两侧棱边的米距离"，fragment 里取 min 即到最近棱边的距离。
                // 用 UNITY_MATRIX_M 而非 unity_ObjectToWorld，兼容 GPU Resident Drawer 实例化矩阵。
                float3 objScale = float3(
                    length(UNITY_MATRIX_M._11_21_31),
                    length(UNITY_MATRIX_M._12_22_32),
                    length(UNITY_MATRIX_M._13_23_33));
                float3 edgeDist3 = (0.5 - abs(input.positionOS.xyz)) * objScale;

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                output.edgeDist3 = edgeDist3;
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

                // 2.2 顶面未涂墨区轻微提亮（P17-A）：第三人称视角下把"可站立的掩体顶"
                // 从同色地面里分出来；放在墨水叠加之前，墨覆盖后仍是统一墨色。
                surfaceColor *= 1.0 + _TopLift * saturate(geomN.y * 2.0 - 1.0);

                // 2.3 表面 PBR 贴图采样（世界空间双轴投影，P1_INK_HIGH 桌面开启/移动端跳过）
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
                    surfaceColor *= (0.88 + 0.24 * rmo.b);
                #else
                    float3 worldN = geomN;
                    half ao = 1.0;
                    float metallic = _Metallic;
                    float smoothness = _Smoothness;
                #endif

                // 3. 基础光照与高光（含主光阴影与三渲二光照）
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float shadowAtten = mainLight.shadowAttenuation;
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                float3 H = normalize(L + V);

                float NdotH = saturate(dot(worldN, H));

                half3 ambient = P1ToonAmbient(worldN, _ShadowTint.rgb, 0.55, 0.10);
                half3 diffuse = P1ToonDiffuse(surfaceColor, mainLight.color, dot(worldN, L), shadowAtten,
                                              _ShadeThreshold, _ShadeSoftness, _ShadowFloor, _ShadowTint.rgb, ambient);
                diffuse *= ao;

                half spec = P1ToonSpec(NdotH, _SpecThreshold, _SpecSoftness, _SpecStrength * (0.5 + metallic) * (0.3 + 1.4 * smoothness), shadowAtten);
                half3 specColor = lerp(mainLight.color, mainLight.color * surfaceColor, metallic);
                half3 specular = specColor * spec;

                half rim = P1ToonRim(saturate(dot(worldN, V)), _RimPower, shadowAtten) * _RimStrength;
                half3 rimLight = _RimColor.rgb * rim;

                // 环境镜面反射（纯数学不分支，使用解析天空盒与太阳盘，暗部受 AO 遮挡）
                float3 R = reflect(-V, worldN);
                float rough = saturate(1.0 - smoothness);
                half3 envSpec = P1InkEnvSpecular(R, rough, saturate(dot(worldN, V)));
                envSpec *= lerp(half3(1.0, 1.0, 1.0), surfaceColor, metallic) * ao;

                half3 shaded = diffuse + specular + rimLight + envSpec;
                ApplyTopInkOverlay(shaded, input.positionWS, geomN, shadowAtten, L, V);

                // 4. 掩体边界线（P17-A）：到最近棱边的世界米距离 → 四周一圈深色硬边。
                // 必须压在墨水叠加之后：顶面/立面涂满墨后轮廓仍然可辨，掩体不会"消失"在同色地面里。
                float edgeDist = min(min(input.edgeDist3.x, input.edgeDist3.y), input.edgeDist3.z);
                float edgeFw = max(fwidth(edgeDist), 0.005);
                float edgeLine = 1.0 - smoothstep(_EdgeLineWidth, _EdgeLineWidth + edgeFw, edgeDist);
                shaded = lerp(shaded, _EdgeLineColor.rgb, edgeLine * _EdgeLineStrength);

                half3 finalColor = MixFog(shaded, input.fogFactor);
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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体（shadowmap 同样走 GRD 批处理）
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

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
                half4 _EdgeLineColor;
                float _EdgeLineWidth;
                float _EdgeLineStrength;
                float _TopLift;
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
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
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
                // 阴影 Pass 不做网点剔除：这里从光源视角渲染阴影贴图，
                // input.positionCS.xy 是阴影图像素坐标，而不是屏幕像素坐标。
                // 单级联/硬阴影（Android）下图案会随相机每帧重新拟合而滑动，
                // 角色身上就会看到乱闪的碎影。遮挡淡化只作用于颜色 Pass，
                // 阴影保持实体，物理上掩体仍然存在。
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Back

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
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
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
                half4 _EdgeLineColor;
                float _EdgeLineWidth;
                float _EdgeLineStrength;
                float _TopLift;
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
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
                float _SurfaceTexelScale;
                float _SurfaceNormalStrength;
                float _SurfaceAOStrength;
                float _SurfaceSmoothnessScale;
            CBUFFER_END

            static const float4x4 _BayerMatrix4x4 = float4x4(
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            );

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                // 法线缓冲不做网点剔除：遮挡物在几何上仍然存在，保持实体法线
                // 可避免 PC 全屏描边在网点孔洞上误描黑边；Android 无此 Pass 消费者。
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
