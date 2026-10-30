Shader "P1/InkWall"
{
    // 墙面墨水叠加层：仅在已涂区域显示墨水（alpha 来自墙面墨格贴图），
    // 未涂区域完全透明，因此原墙体的水泥外观保持不变。
    Properties
    {
        _InkTex ("Ink Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _DitherAlpha ("Dither Alpha", Range(0.0, 1.0)) = 1.0

        [Header(Toon Shading)]
        _ShadeThreshold ("Shade Threshold", Range(0.2, 0.8)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.005, 0.2)) = 0.08
        _ShadowTint ("Shadow Tint", Color) = (0.42, 0.47, 0.64, 1)
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

            // GPU Resident Drawer (P12-C)：DOTS 实例化变体；GRD 启用时走 BatchRendererGroup 提交，关闭时走原渲染路径
            #pragma target 4.5
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"

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

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _DitherAlpha;
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
            CBUFFER_END

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

                // 墨格 alpha：0 = 未涂；贴图侧已写入亚格子覆盖率，这里再用屏幕空间导数收一层 AA，
                // 避免墙面墨迹出现 10cm 方块的锯齿边。
                float fwA = max(fwidth(inkSample.a), 0.004);
                half inkMask = smoothstep(0.5 - fwA, 0.5 + fwA, inkSample.a);
                if (inkMask < 0.01)
                    discard;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 N = normalize(input.normalWS);
                half band = P1ToonBand(dot(N, normalize(mainLight.direction)), _ShadeThreshold, _ShadeSoftness);
                half shadowTerm = P1ToonShadowTerm(mainLight.shadowAttenuation, 0.55, _ShadeSoftness);
                half ndotl = lerp(0.62, 1.0, band * shadowTerm);
                half3 toonTint = lerp(_ShadowTint.rgb, half3(1,1,1), band);
                half3 inkColor = inkSample.rgb * _Tint.rgb * ndotl * toonTint;

                // 薄缘提亮：墨迹内侧一圈微微反光，边缘才有厚度感
                float rim = smoothstep(0.52, 0.9, inkMask) * (1.0 - smoothstep(0.9, 0.995, inkMask));
                inkColor *= 1.0 + rim * 0.3;
                inkColor = MixFog(inkColor, input.fogFactor);

                return half4(inkColor, inkMask * _Tint.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}