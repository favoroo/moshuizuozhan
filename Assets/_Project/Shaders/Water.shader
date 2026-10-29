Shader "P1/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.20, 0.65, 0.85, 1.0)
        _DeepColor ("Deep Color", Color) = (0.05, 0.25, 0.55, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.70, 0.82, 0.92, 1.0)
        _WaveScale ("Wave Scale", Float) = 1.0
        _WaveSpeed ("Wave Speed", Float) = 0.15
        _WaveStrength ("Wave Strength", Range(0.0, 1.0)) = 0.25
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

                // 2-3 组不同朝向与频率的正弦波纹
                float w1 = sin(dot(pos, float2(0.894, 0.447)) * 2.2 + t * 3.1);
                float w2 = sin(dot(pos, float2(-0.555, 0.832)) * 3.7 - t * 2.4);
                float w3 = sin(dot(pos, float2(0.316, -0.949)) * 1.6 + t * 1.8);

                float waveComposite = (w1 + w2 + w3) * 0.333333;
                float wave01 = saturate(waveComposite * 0.5 + 0.5);

                // 混合深浅水色
                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, lerp(0.5, wave01, _WaveStrength));

                // 远景与掠射角向 _HorizonColor 平滑过渡
                float3 viewVec = GetCameraPositionWS() - input.positionWS;
                float dist = length(viewVec);
                float distFade = smoothstep(20.0, 120.0, dist);

                float3 V = normalize(viewVec);
                float grazing = pow(1.0 - saturate(V.y), 3.0);
                float horizonBlend = saturate(distFade * 0.75 + grazing * 0.45);

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
