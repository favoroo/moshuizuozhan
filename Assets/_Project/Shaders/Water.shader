Shader "P1/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.20, 0.65, 0.85, 1.0)
        _DeepColor ("Deep Color", Color) = (0.05, 0.25, 0.55, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.70, 0.82, 0.92, 1.0)
        _WaveScale ("Wave Scale", Float) = 1.0
        _WaveSpeed ("Wave Speed", Float) = 0.18
        _WaveStrength ("Wave Strength", Range(0.0, 1.0)) = 0.35
        _WaveNormalStrength ("Wave Normal Strength", Range(0.0, 2.0)) = 0.45
        _GlitterStrength ("Glitter Strength", Range(0.0, 3.0)) = 0.72
        _GlitterThreshold ("Glitter Threshold", Range(0.7, 0.999)) = 0.972
        _ShoreDistance ("Shore Distance", Float) = 12.0
        _FoamStrength ("Foam Strength", Range(0.0, 1.0)) = 0.65
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
            #pragma multi_compile_fog
            #pragma multi_compile _ P1_INK_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "InkWetShading.hlsl"

            // 全局环境参数（由 Core/RenderEnvGlobals 每帧推送，不在 CBUFFER 中以保持 SRP Batcher 兼容）
            float _P1WaterReflectionStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _HorizonColor;
                float _WaveScale;
                float _WaveSpeed;
                float _WaveStrength;
                float _WaveNormalStrength;
                float _GlitterStrength;
                float _GlitterThreshold;
                float _ShoreDistance;
                float _FoamStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 pos = input.positionWS.xz * _WaveScale;
                float t = _Time.y * _WaveSpeed;

                // 第 1 层：大波浪涌（主海流方向）
                float2 d1 = float2(0.894, 0.447);
                float2 d2 = float2(-0.555, 0.832);

                float a1 = dot(pos, d1) * 1.8 + t * 2.8;
                float a2 = dot(pos, d2) * 2.5 - t * 2.1;

                float w1 = sin(a1);
                float w2 = sin(a2);
                float2 dw1 = cos(a1) * (1.8 * d1);
                float2 dw2 = cos(a2) * (2.5 * d2);

                // 第 2 层：高频微风波纹与交叉涟漪
                float2 d3 = float2(0.316, -0.949);
                float2 d4 = float2(-0.707, -0.707);

                float a3 = dot(pos, d3) * 4.6 + t * 3.8;
                float a4 = dot(pos, d4) * 6.2 - t * 3.2;

                float w3 = sin(a3);
                float w4 = sin(a4);
                float2 dw3 = cos(a3) * (4.6 * d3);
                float2 dw4 = cos(a4) * (6.2 * d4);

                float waveComposite = (w1 + w2) * 0.35 + (w3 + w4) * 0.15;
                float wave01 = saturate(waveComposite + 0.5);

                // 组合两层波导数生成世界法线
                float2 gradH = (dw1 + dw2) * (0.35 * _WaveScale) + (dw3 + dw4) * (0.15 * _WaveScale);
                float3 N = normalize(float3(-gradH.x * _WaveNormalStrength, 1.0, -gradH.y * _WaveNormalStrength));

                // 近岸浅水渐变到 waterShallow（码头混凝土基座占地 52x68m，边界在 ±26, ±34）
                float2 pierDist2D = max(abs(input.positionWS.xz) - float2(26.0, 34.0), 0.0);
                float distToPier = length(pierDist2D);
                float shoreFactor = 1.0 - smoothstep(0.0, _ShoreDistance, distToPier);
                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(shoreFactor * 0.70 + wave01 * _WaveStrength * 0.5));

                // 岸边水花白沫动态边缘 (distToPier < 1.6m)
                float foamNoise = sin(dot(input.positionWS.xz, float2(4.2, 3.1)) + _Time.y * 2.5) * 0.5 + 0.5;
                float foamMask = (1.0 - smoothstep(0.15, 1.5, distToPier + (foamNoise - 0.5) * 0.6)) * _FoamStrength;
                waterColor = lerp(waterColor, half3(0.96, 0.98, 1.0), foamMask);

                float3 viewVec = GetCameraPositionWS() - input.positionWS;
                float3 V = normalize(viewVec);
                float3 R = reflect(-V, N);
                float NdotV = saturate(dot(N, V));

                // 环境反射（桌面端 Cubemap + 移动端解析天空回退；由 P1InkEnvSpecularFull 内部处理）
                half3 env = P1InkEnvSpecularFull(R, 0.06, NdotV, _ShallowColor.rgb) * (half)_P1WaterReflectionStrength;
                float F = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                waterColor = lerp(waterColor, waterColor * 0.35 + env, F * 0.75);

                // 阳光 glitter 闪烁（主光镜面微面元闪光，全端支持）
                // P30 返修：更低频、更稀疏的波峰亮点（INKWAVE 水面=大片蓝+稀疏闪光点），避免远看成白色网格噪点
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));

                float microSparkle = sin(dot(input.positionWS.xz, float2(5.1, 6.3)) + _Time.y * 4.5)
                                   * sin(dot(input.positionWS.xz, float2(-3.7, 4.4)) - _Time.y * 3.1);
                float glitterMask = smoothstep(_GlitterThreshold, 1.0, NdotH + microSparkle * 0.02);
                float sharpGlint = pow(NdotH, 240.0);
                half3 glitterColor = (half3)mainLight.color * (half)(glitterMask * _GlitterStrength + sharpGlint * 0.85);
                waterColor += glitterColor;

                // 远景与掠射角向 _HorizonColor 平滑过渡
                float dist = length(viewVec);
                float distFade = smoothstep(25.0, 140.0, dist);
                float grazing = pow(1.0 - saturate(V.y), 3.0);
                float horizonBlend = saturate(distFade * 0.80 + grazing * 0.40);
                waterColor = lerp(waterColor, _HorizonColor.rgb, horizonBlend);

                // 雾效混合
                waterColor = MixFog(waterColor, input.fogFactor);

                return half4(waterColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
