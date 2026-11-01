Shader "P1/SpawnPad"
{
    Properties
    {
        _BaseColor ("Steel Inlay Color", Color) = (0.22, 0.25, 0.29, 1.0)
        _PlatformColor ("Platform Concrete Color", Color) = (0.65, 0.63, 0.58, 1.0)
        _RingColor ("Ring Color", Color) = (0.92, 0.94, 0.98, 1.0)
        _ChevronColor ("Chevron Color", Color) = (0.86, 0.68, 0.20, 1.0)
        _TeamColor ("Team Color", Color) = (1.0, 0.48, 0.08, 1.0)
        _GlowStrength ("Glow Strength", Range(0.0, 2.0)) = 0.25
        _PulseSpeed ("Pulse Speed", Float) = 1.6
        _ShadowTint ("Shadow Tint", Color) = (0.40, 0.45, 0.62, 1.0)
        _ShadowFloor ("Shadow Floor", Range(0.0, 1.0)) = 0.42
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.03
        // A 队箭头朝 +Z；B 队材质设为 -1 让箭头朝场地中央。
        _ForwardSign ("Forward Sign", Float) = 1.0
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
            Name "Forward"
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
            #pragma multi_compile _ P1_INK_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"
            #include "InkSurfaceOverlay.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 padCenterWS : TEXCOORD2;
                half   fogFactor   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _PlatformColor;
                half4 _RingColor;
                half4 _ChevronColor;
                half4 _TeamColor;
                half4 _ShadowTint;
                float _GlowStrength;
                float _PulseSpeed;
                float _ShadowFloor;
                float _ShadeSoftness;
                float _ForwardSign;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS  = posInputs.positionCS;
                output.positionWS  = posInputs.positionWS;
                output.normalWS    = normInputs.normalWS;
                output.padCenterWS = TransformObjectToWorld(float3(0, 0, 0)).xz;
                output.fogFactor   = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            // 墨滴 2D 轮廓：底部饱满圆弧、前方向中心逐渐收尖
            float dropletDist(float2 pt)
            {
                float yCenter = 0.38;
                float rCircle = 0.44;
                float yTip = 1.48;

                if (pt.y <= yCenter)
                {
                    return length(pt - float2(0.0, yCenter)) - rCircle;
                }
                else if (pt.y >= yTip)
                {
                    return length(pt - float2(0.0, yTip));
                }
                else
                {
                    float t = (pt.y - yCenter) / (yTip - yCenter);
                    float halfW = rCircle * (1.0 - t) * sqrt(saturate(1.0 - t * 0.45));
                    return abs(pt.x) - halfW;
                }
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz - input.padCenterWS;
                // 箭头/墨滴朝向按队别翻转：A 队 +Z（推进方向），B 队 -Z（推向场地中央）。
                float2 q = float2(p.x, p.y * _ForwardSign);
                float r = length(p);

                // 仅在朝上的面绘制顶视图形（避免侧壁立面拉伸）
                float3 N = normalize(input.normalWS);
                half upness = smoothstep(0.3, 0.7, N.y);

                // 0. 缓慢呼吸脉动与扩散能量涟漪
                float pulse = sin(_Time.y * _PulseSpeed) * 0.5 + 0.5;
                float breathGlow = 0.85 + 0.35 * pulse;
                float ripplePhase = frac(_Time.y * (_PulseSpeed * 0.18));
                float rippleR = ripplePhase * 2.85;
                float rippleDist = abs(r - rippleR);
                float rippleMask = exp(-pow(rippleDist * 5.0, 2.0)) * (1.0 - ripplePhase) * upness;

                // 1. 钢灰嵌板范围（r < 3.0m），外缘 0.1m 过渡到混凝土基台
                float inlayMask = smoothstep(3.06, 2.96, r) * upness;

                // 2. 队伍色渐变边缘（r 从 1.8m 到 2.95m 呈放射状渐变出队伍鲜艳色彩）
                float teamEdgeMask = smoothstep(1.7, 2.85, r) * (1.0 - smoothstep(2.94, 3.02, r)) * upness;

                // 3. 同心环 2 道（外环 r=2.62m，内环 r=1.65m，线宽经 fwidth 抗锯齿）
                float distRing1 = abs(r - 2.62);
                float fw1 = max(fwidth(distRing1), 0.008);
                float ring1Mask = (1.0 - smoothstep(0.035 + fw1, 0.055 + fw1, distRing1)) * upness;

                float distRing2 = abs(r - 1.65);
                float fw2 = max(fwidth(distRing2), 0.008);
                float ring2Mask = (1.0 - smoothstep(0.025 + fw2, 0.045 + fw2, distRing2)) * upness;

                float ringsMask = max(ring1Mask, ring2Mask);

                // 4. 墨滴形主指针 + 2 颗前方卫星墨滴
                float dMain = dropletDist(q);
                float dLeading = length(q - float2(0.0, 1.88)) - 0.10;
                float dMicro = length(q - float2(0.0, 2.18)) - 0.055;
                float dDrop = min(min(dMain, dLeading), dMicro);
                float fwDrop = max(fwidth(dDrop), 0.008);
                float dropletMask = (1.0 - smoothstep(-0.01 + fwDrop, 0.018 + fwDrop, dDrop)) * upness;

                // 4.1 墨滴后方两道尾迹警戒折线 (q.y < 0)
                float chevronMask = 0.0;
                const float slope = 0.85;
                const float invLen = 0.762;
                for (int k = 0; k < 2; k++)
                {
                    float z0 = -0.40 - (float)k * 0.45;
                    float spanMask = 1.0 - smoothstep(0.42, 0.58, abs(q.x));
                    float distToLine = abs((q.y - z0) + slope * abs(q.x)) * invLen;
                    float fwCh = max(fwidth(distToLine), 0.008);
                    float ch = 1.0 - smoothstep(0.032 + fwCh, 0.052 + fwCh, distToLine);
                    chevronMask = max(chevronMask, ch * spanMask);
                }
                chevronMask *= upness;

                // 5. 颜色合成：基台混凝土 -> 钢板 -> 队伍渐变边缘
                half3 inlayCol = _BaseColor.rgb * lerp(1.10, 0.86, smoothstep(0.0, 2.9, r));
                inlayCol = lerp(inlayCol, _TeamColor.rgb * 0.75, teamEdgeMask * 0.60);
                half3 col = lerp(_PlatformColor.rgb, inlayCol, inlayMask);

                // 队伍色墨滴指针（中心带亮白高光核）
                float dropInner = (1.0 - smoothstep(0.0, 0.22, dDrop + 0.22)) * dropletMask;
                half3 dropCol = lerp(_TeamColor.rgb * 1.25, half3(1.0, 1.0, 1.0), dropInner * 0.65);
                col = lerp(col, dropCol, dropletMask);

                // 导向警戒折线
                col = lerp(col, _ChevronColor.rgb, chevronMask * 0.88);

                // 双同心白环
                col = lerp(col, _RingColor.rgb, ringsMask);

                // 嵌板外缘压暗收口（r=3.01m）
                float rimDist = abs(r - 3.01);
                float fwRim = max(fwidth(rimDist), 0.010);
                float rimMask = (1.0 - smoothstep(0.014 + fwRim, 0.032 + fwRim, rimDist)) * upness;
                col = lerp(col, _PlatformColor.rgb * 0.50, rimMask * 0.65);

                // 缓慢呼吸自发光（双环 + 墨滴 + 能量涟漪）
                half3 glowCol = _RingColor.rgb * (ringsMask * _GlowStrength * breathGlow);
                glowCol += _TeamColor.rgb * (dropletMask * _GlowStrength * 1.35 * breathGlow);
                glowCol += _TeamColor.rgb * (rippleMask * _GlowStrength * 0.85);

                // 6. 光照：主光 + 二分阴影 + 冷色环境补光
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadowTerm = P1ToonShadowTerm(mainLight.shadowAttenuation, _ShadowFloor, _ShadeSoftness);
                float NdotL = saturate(dot(N, normalize(mainLight.direction)));
                half3 ambient = SampleSH(N) * 0.55 + _ShadowTint.rgb * 0.14;
                half3 litColor = col * (ambient + mainLight.color * (NdotL * 0.55 + 0.35) * shadowTerm) + glowCol;

                // 6.1 顶面墨迹：出生台射击同样留下墨
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                ApplyTopInkOverlay(litColor, input.positionWS, N, mainLight.shadowAttenuation, mainLight.direction, viewDirWS);

                // 7. 雾效
                litColor = MixFog(litColor, input.fogFactor);

                return half4(litColor, 1.0);
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

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
                half4 _PlatformColor;
                half4 _RingColor;
                half4 _ChevronColor;
                half4 _TeamColor;
                half4 _ShadowTint;
                float _GlowStrength;
                float _PulseSpeed;
                float _ShadowFloor;
                float _ShadeSoftness;
                float _ForwardSign;
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
    }
    FallBack "Universal Render Pipeline/Unlit"
}