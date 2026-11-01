Shader "P1/SkyGradient"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.24, 0.45, 0.85, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.75, 0.85, 0.95, 1.0)
        _GroundColor ("Ground Color", Color) = (0.20, 0.22, 0.26, 1.0)
        _SunColor ("Sun Color", Color) = (1.0, 0.95, 0.80, 1.0)
        _SunDirection ("Sun Direction", Vector) = (0.3, 0.5, 0.2, 0.0)
        _SunIntensity ("Sun Intensity", Float) = 2.5
        _CloudColor ("Cloud Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _CloudStrength ("Cloud Strength", Range(0.0, 1.0)) = 0.35
        _HorizonWarmthColor ("Horizon Warmth Color", Color) = (1.0, 0.94, 0.84, 1.0)
        _HorizonWarmth ("Horizon Warmth Strength", Range(0.0, 1.0)) = 0.35
        _CloudLayer2Strength ("Cloud Layer 2 Strength", Range(0.0, 1.0)) = 0.25
        _CloudEdgeCrispness ("Cloud Edge Crispness", Range(0.01, 0.2)) = 0.06
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
                float _SunIntensity;
                half4 _HorizonWarmthColor;
                float _HorizonWarmth;
                float _CloudLayer2Strength;
                float _CloudEdgeCrispness;
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

            // 几何感大积云塑形：域扭曲形成饱满圆润的团块
            float cumulusLobe(float2 p)
            {
                float2 warp = float2(
                    vnoise(p * 1.3 + float2(2.1, 7.8)),
                    vnoise(p * 1.3 + float2(8.4, 3.2))
                );
                float2 q = p + (warp - 0.5) * 0.55;

                float n1 = vnoise(q);
                float n2 = vnoise(q * 2.2 + float2(4.3, 1.7));
                float n3 = vnoise(q * 4.4 + float2(7.1, 9.3));

                return n1 * 0.54 + n2 * 0.32 + n3 * 0.14;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDirWS);

                // 1. 天空与地面渐变 + 地平线暖色过渡带
                half3 skyCol;
                if (dir.y < 0.0)
                {
                    // 地平线以下用 _GroundColor，靠近地平线微平滑防锯齿
                    float groundEdge = saturate(-dir.y * 28.0);
                    skyCol = lerp(_HorizonColor.rgb, _GroundColor.rgb, groundEdge);
                }
                else
                {
                    // 向上按 pow 混合 _HorizonColor -> _TopColor
                    float h = saturate(dir.y);
                    float factor = pow(h, 0.72);
                    skyCol = lerp(_HorizonColor.rgb, _TopColor.rgb, factor);

                    // 地平线暖色过渡带：朝向太阳方向略微增强，产生海港明快日照暖光
                    float sunFacing = saturate(dot(normalize(float3(dir.x, 0.0, dir.z) + 1e-5), normalize(float3(_SunDirection.x, 0.0, _SunDirection.z) + 1e-5)) * 0.5 + 0.5);
                    float warmthBand = exp(-pow(h * 7.5, 1.5)) * (0.55 + 0.45 * sunFacing) * _HorizonWarmth;
                    skyCol = lerp(skyCol, _HorizonWarmthColor.rgb, saturate(warmthBand));
                }

                // 2. 太阳盘聚拢与柔和光晕（紧凑亮核 + 漫画风光晕）
                float3 sunDir = normalize(_SunDirection.xyz);
                float cosTheta = dot(dir, sunDir);
                if (cosTheta > 0.0)
                {
                    // 太阳亮盘：更聚拢紧凑（HDR 亮斑供 Bloom 拾取）
                    float sunDisk = smoothstep(0.9991, 0.9997, cosTheta);
                    // 紧凑内晕 + 柔和外晕
                    float glowMod = saturate(_SunIntensity * 0.55);
                    float innerGlow = pow(saturate(cosTheta), 96.0) * 0.55;
                    float outerGlow = pow(saturate(cosTheta), 14.0) * 0.20 + pow(saturate(cosTheta), 4.0) * 0.06;
                    float sunGlow = (innerGlow + outerGlow) * glowMod;

                    // 地平线以下遮挡
                    float aboveHorizon = smoothstep(-0.01, 0.05, dir.y);
                    half3 sun = _SunColor.rgb * ((sunDisk * _SunIntensity + sunGlow) * aboveHorizon);
                    skyCol += sun;
                }

                // 3. 两层视差大积云（几何感团块、边缘干净、带阴面立体感）
                if (dir.y > 0.005 && _CloudStrength > 0.001)
                {
                    float crisp = max(_CloudEdgeCrispness, 0.01);
                    float horizonFade = smoothstep(0.01, 0.12, dir.y) * smoothstep(0.95, 0.45, dir.y);

                    // 第 1 层：中景大积云（宏伟饱满团块）
                    float2 uv1 = (dir.xz / max(dir.y + 0.14, 0.04)) * 1.15;
                    float n1 = cumulusLobe(uv1);
                    float thresh1 = 0.55;
                    float mask1 = smoothstep(thresh1, thresh1 + crisp, n1);

                    // 积云立体受光：顶部受光白，底部背光泛淡蓝紫
                    float topLit1 = smoothstep(thresh1 + 0.02, thresh1 + 0.18, n1);
                    half3 cloudShade1 = lerp(_HorizonColor.rgb * 0.78 + half3(0.08, 0.08, 0.15), _CloudColor.rgb, topLit1);

                    float alpha1 = mask1 * horizonFade * _CloudStrength;
                    skyCol = lerp(skyCol, cloudShade1, alpha1);

                    // 第 2 层：远景高空视差小积云（深远层次感，避开第 1 层实心区域）
                    if (_CloudLayer2Strength > 0.001)
                    {
                        float2 uv2 = (dir.xz / max(dir.y + 0.24, 0.04)) * 2.5 + float2(14.8, 31.6);
                        float n2 = cumulusLobe(uv2);
                        float thresh2 = 0.58;
                        float mask2 = smoothstep(thresh2, thresh2 + crisp * 1.3, n2);
                        float alpha2 = mask2 * horizonFade * (_CloudStrength * _CloudLayer2Strength) * (1.0 - mask1 * 0.8);
                        skyCol = lerp(skyCol, _CloudColor.rgb * 0.94, alpha2);
                    }
                }

                return half4(skyCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
