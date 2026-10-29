Shader "P1/SkyGradient"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.24, 0.45, 0.85, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.75, 0.85, 0.95, 1.0)
        _GroundColor ("Ground Color", Color) = (0.20, 0.22, 0.26, 1.0)
        _SunColor ("Sun Color", Color) = (1.0, 0.95, 0.80, 1.0)
        _SunDirection ("Sun Direction", Vector) = (0.3, 0.5, 0.2, 0.0)
        _CloudColor ("Cloud Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _CloudStrength ("Cloud Strength", Range(0.0, 1.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "Skybox"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS  : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                half4 _SunColor;
                float4 _SunDirection;
                half4 _CloudColor;
                float _CloudStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDirWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            // ------------------------------------------------------------- 程序化噪声
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDirWS);

                // 1. 天空与地面渐变
                half3 skyCol;
                if (dir.y < 0.0)
                {
                    // 地平线以下用 _GroundColor，靠近地平线微平滑防锯齿
                    float groundEdge = saturate(-dir.y * 30.0);
                    skyCol = lerp(_HorizonColor.rgb, _GroundColor.rgb, groundEdge);
                }
                else
                {
                    // 向上按 pow 混合 _HorizonColor -> _TopColor
                    float h = saturate(dir.y);
                    float factor = pow(h, 0.7);
                    skyCol = lerp(_HorizonColor.rgb, _TopColor.rgb, factor);
                }

                // 2. 太阳盘与光晕
                float3 sunDir = normalize(_SunDirection.xyz);
                float cosTheta = dot(dir, sunDir);
                if (cosTheta > 0.0)
                {
                    // 太阳盘亮斑
                    float sunDisk = smoothstep(0.9985, 0.9996, cosTheta);
                    // 柔和光晕
                    float sunGlow = pow(saturate(cosTheta), 64.0) * 0.45 + pow(saturate(cosTheta), 12.0) * 0.18;
                    // 地平线以下遮挡太阳光晕
                    float aboveHorizon = smoothstep(-0.02, 0.05, dir.y);
                    half3 sun = _SunColor.rgb * ((sunDisk * 2.5 + sunGlow) * aboveHorizon);
                    skyCol += sun;
                }

                // 3. 程序化云层（仅地平线以上，低强度叠加）
                if (dir.y > 0.001 && _CloudStrength > 0.001)
                {
                    float2 cloudUV = (dir.xz / max(dir.y + 0.15, 0.05)) * 1.8;
                    float n = vnoise(cloudUV * 1.0) * 0.50
                            + vnoise(cloudUV * 2.3 + float2(1.7, 3.1)) * 0.32
                            + vnoise(cloudUV * 5.1 + float2(7.2, 2.5)) * 0.18;

                    float cloudMask = smoothstep(0.48, 0.78, n);
                    // 地平线边缘平滑淡入
                    float horizonFade = smoothstep(0.0, 0.12, dir.y);
                    float cloudAlpha = cloudMask * horizonFade * _CloudStrength;
                    skyCol = lerp(skyCol, _CloudColor.rgb, cloudAlpha);
                }

                return half4(skyCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
