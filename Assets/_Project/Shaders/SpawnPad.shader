Shader "P1/SpawnPad"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.15, 0.17, 0.20, 1.0)
        _PlatformColor ("Platform Concrete Color", Color) = (0.65, 0.63, 0.58, 1.0)
        _TeamColor ("Team Color", Color) = (1.00, 0.44, 0.06, 1.0)
        _RingColor ("Ring Color", Color) = (0.90, 0.92, 0.95, 1.0)
        _ChevronColor ("Chevron Color", Color) = (1.00, 0.85, 0.20, 1.0)
        _GlowStrength ("Glow Strength", Range(0.0, 4.0)) = 0.6
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                half4 _TeamColor;
                half4 _RingColor;
                half4 _ChevronColor;
                float _GlowStrength;
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

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz - input.padCenterWS;
                float r = length(p);

                // 仅在朝上的面绘制顶视图形（避免侧壁立面拉伸）
                float3 N = normalize(input.normalWS);
                half upness = smoothstep(0.3, 0.7, N.y);

                // 1. 外圈圆环（半径约 2.8m，线宽 0.16m，即半宽 0.08m；适配 8x6.5m 出生台）
                float distToRing = abs(r - 2.8);
                float fwRing = max(fwidth(distToRing), 0.015);
                float ringMask = smoothstep(0.08 + fwRing, 0.08 - fwRing, distToRing) * upness;

                // 2. 距中心 2.8-3.15m 的柔和队色发光边
                float glowEdge = smoothstep(2.8, 3.0, r) * (1.0 - smoothstep(3.0, 3.15, r)) * upness;

                // 3. 3 道指向 +Z 的 V 形箭头（围绕中心，间距 0.9m，线宽 0.16m）
                // 折线斜率 s = 0.8，半长截断约 1.0m
                float chevronMask = 0.0;
                const float s = 0.8;
                const float invLen = 1.0 / sqrt(1.0 + s * s);
                const float halfWidth = 0.08;
                float spanMask = smoothstep(1.0, 0.82, abs(p.x));

                [unroll]
                for (int k = -1; k <= 1; k++)
                {
                    float z0 = (float)k * 0.9;
                    // 点到折线 (p.y - z0) + s * |p.x| = 0 的法向距离
                    float distToLine = abs((p.y - z0) + s * abs(p.x)) * invLen;
                    float fwCh = max(fwidth(distToLine), 0.012);
                    float ch = smoothstep(halfWidth + fwCh, halfWidth - fwCh, distToLine);
                    chevronMask = max(chevronMask, ch * spanMask);
                }
                chevronMask *= upness;

                // 4. 颜色合成：
                // 台面外缘（r > 3.0m）以及侧立面使用自然混凝土基台色；中心圆形区域（r < 3.0m）为深色能量出生池
                float poolMask = smoothstep(3.15, 2.95, r) * upness;
                float innerAccent = smoothstep(2.7, 1.0, r) * 0.25 * poolMask;
                half3 poolBg = lerp(_BaseColor.rgb, _TeamColor.rgb, innerAccent);
                half3 col = lerp(_PlatformColor.rgb, poolBg, poolMask);

                // 叠加 V 形箭头
                col = lerp(col, _ChevronColor.rgb, chevronMask);

                // 叠加外圈圆环
                col = lerp(col, _RingColor.rgb, ringMask);

                // 边缘队色发光提升
                half3 glowCol = _TeamColor.rgb * (glowEdge * _GlowStrength);
                col += glowCol;

                // 5. 光照计算（受主光影响的简单 Lambert + 暗部环境补光）
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, normalize(mainLight.direction)));
                half3 ambient = SampleSH(N) * 0.5 + half3(0.15, 0.15, 0.15);
                half3 litColor = col * (ambient + mainLight.color * (NdotL * 0.6 + 0.4)) + glowCol * 0.6;

                // 6. 雾效
                litColor = MixFog(litColor, input.fogFactor);

                return half4(litColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
