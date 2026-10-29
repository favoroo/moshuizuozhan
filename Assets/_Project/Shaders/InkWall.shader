Shader "P1/InkWall"
{
    // 墙面墨水叠加层：仅在已涂区域显示墨水（alpha 来自墙面墨格贴图），
    // 未涂区域完全透明，因此原墙体的水泥外观保持不变。
    Properties
    {
        _InkTex ("Ink Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _DitherAlpha ("Dither Alpha", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

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
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _DitherAlpha;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 俯视角网点半透明遮挡剔除
                float effectiveAlpha = min(_DitherAlpha, _Tint.a);
                if (effectiveAlpha < 0.999)
                {
                    static const float4x4 _BayerMatrix4x4 = float4x4(
                         0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                        12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                         3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                        15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
                    );
                    float2 screenPixel = input.positionCS.xy;
                    int bx = int(fmod(screenPixel.x, 4.0));
                    int by = int(fmod(screenPixel.y, 4.0));
                    if (_BayerMatrix4x4[by][bx] >= effectiveAlpha)
                    {
                        discard;
                    }
                }

                half4 inkSample = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, input.uv);

                // 墨格 alpha：0 = 未涂，255 = 已涂；双线性过滤让边缘自然过渡
                half inkMask = smoothstep(0.30, 0.62, inkSample.a);
                if (inkMask < 0.01)
                    discard;

                Light mainLight = GetMainLight();
                float3 N = normalize(input.normalWS);
                half ndotl = saturate(dot(N, normalize(mainLight.direction))) * 0.45 + 0.55;

                // 轻微高光让墙面墨水看起来湿润
                half3 inkColor = inkSample.rgb * _Tint.rgb * ndotl;

                return half4(inkColor, inkMask * _Tint.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}