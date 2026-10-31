Shader "P1/InkWall"
{
    // 墙面墨水叠加层：仅在已涂区域显示墨水（alpha 来自墙面墨格贴图），
    // 未涂区域完全透明，因此原墙体的水泥外观保持不变。
    Properties
    {
        _InkTex ("Ink Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _DitherAlpha ("Dither Alpha", Range(0.0, 1.0)) = 1.0

        [Header(Edge Line)]
        // P17-A：涂墨立面四周一圈深色边线（quad 外扩 1cm 会盖住基面边线，
        // 涂满墨的掩体立面必须自己保留轮廓）。UV1 = (面宽, 面高) 米。
        _EdgeLineColor ("Edge Line Color", Color) = (0.09, 0.10, 0.13, 1.0)
        _EdgeLineWidth ("Edge Line Width (m)", Float) = 0.05
        _EdgeLineStrength ("Edge Line Strength", Range(0.0, 1.0)) = 0.85

        [Header(Toon Shading)]
        _ShadeThreshold ("Shade Threshold", Range(0.2, 0.8)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.005, 0.2)) = 0.08
        _ShadowTint ("Shadow Tint", Color) = (0.42, 0.47, 0.64, 1)

        [Header(Wetness and Shading)]
        _WallWetness ("Wall Wetness", Range(0.0, 1.0)) = 0.6
        _InkGrainN ("Ink Grain Normal", 2D) = "bump" {}
        _InkGrainRMO ("Ink Grain RMO", 2D) = "white" {}
        _InkGrainScale ("Ink Grain Scale", Float) = 1.0
        _InkGrainStrength ("Ink Grain Strength", Range(0.0, 1.0)) = 0.35
        _DampHaloStrength ("Damp Halo", Range(0.0, 1.0)) = 0.5
        _WallDripStrength ("Wall Drip", Range(0.0, 1.0)) = 0.4
        _WallEnvSpec ("Wall Env Spec", Range(0.0, 3.0)) = 0.8
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
            #pragma multi_compile _ P1_INK_HIGH

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"
            #include "InkWetShading.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // (面宽, 面高) 米，由 InkWallSurface 写入
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float2 uv2        : TEXCOORD4;
            };

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);
            TEXTURE2D(_InkGrainN);
            SAMPLER(sampler_InkGrainN);
            TEXTURE2D(_InkGrainRMO);
            SAMPLER(sampler_InkGrainRMO);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _DitherAlpha;
                half4 _EdgeLineColor;
                float _EdgeLineWidth;
                float _EdgeLineStrength;
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _WallWetness;
                float _InkGrainScale;
                float _InkGrainStrength;
                float _DampHaloStrength;
                float _WallDripStrength;
                float _WallEnvSpec;
            CBUFFER_END

            // 全局胶质内发光（InkSurfaceController 每帧 SetGlobalFloat），与地面/顶面墨共用，
            // 不放进 UnityPerMaterial，避免不同材质各写一份导致墙地墨色再次分叉。
            float _GlobalInkGlow;

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                output.uv2 = input.uv2;
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

                float2 sampleUV = input.uv;
                #if defined(P1_INK_HIGH)
                float2 meterPos = input.uv * input.uv2;
                if (_WallDripStrength > 0.001 && input.uv2.y > 0.05)
                {
                    float colSize = 0.38;
                    float col = floor(meterPos.x / colSize);
                    float rnd = frac(sin(col * 317.15 + 1.37) * 43758.5453);
                    float colCenter = (col + 0.5 + (rnd - 0.5) * 0.55) * colSize;
                    float distCol = abs(meterPos.x - colCenter);
                    float dripW = lerp(0.012, 0.024, rnd);
                    float dripMaskX = smoothstep(dripW, dripW * 0.15, distCol);
                    if (dripMaskX > 0.001)
                    {
                        float dripLenM = lerp(0.06, 0.22, rnd) * (_WallWetness * 0.75 + 0.25) * _WallDripStrength;
                        float dripV = (dripLenM / input.uv2.y) * dripMaskX;
                        float2 dripUV = sampleUV + float2(0.0, dripV);
                        half4 upperInk = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, dripUV);
                        if (upperInk.a > 0.35)
                        {
                            sampleUV = dripUV;
                        }
                    }
                }
                #endif

                half4 inkSample = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, sampleUV);

                // 墨格 alpha：0 = 未涂；贴图侧已写入亚格子覆盖率，这里再用屏幕空间导数收一层 AA，
                // 避免墙面墨迹出现 10cm 方块的锯齿边。
                float fwA = max(fwidth(inkSample.a), 0.004);
                half coreMask = smoothstep(0.5 - fwA, 0.5 + fwA, inkSample.a);

                // 浸润暗 halo（damp halo）：墨缘向外一圈渗透暗痕（对齐竞品 32% 肌理透出）
                half haloMask = smoothstep(0.12, 0.5, inkSample.a) * (1.0 - coreMask);
                half haloAlpha = haloMask * (half)(0.42 * _DampHaloStrength);
                half effectiveMask = coreMask + haloAlpha;
                if (effectiveMask < 0.01)
                    discard;

                float3 N = normalize(input.normalWS);
                // 建立立面正交切线基（T1 水平向右，T2 垂直向上）
                float3 upRef = abs(N.y) > 0.95 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
                float3 T1 = normalize(cross(upRef, N));
                float3 T2 = cross(N, T1);

                float roughPerturb = 0.0;
                #if defined(P1_INK_HIGH)
                float2 grainUV = (input.uv * input.uv2) * float2(0.9, 0.42) * _InkGrainScale;
                half4 grainNSample = SAMPLE_TEXTURE2D(_InkGrainN, sampler_InkGrainN, grainUV);
                float3 grainNormal = UnpackNormalScale(grainNSample, _InkGrainStrength);
                N = P1InkApplyTangentPerturb(grainNormal, N, T1, T2, (half)_InkGrainStrength);
                half4 grainRMO = SAMPLE_TEXTURE2D(_InkGrainRMO, sampler_InkGrainRMO, grainUV);
                roughPerturb = (grainRMO.a - 0.5) * 0.18;
                #endif

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = normalize(mainLight.direction);
                half band = P1ToonBand(dot(N, L), _ShadeThreshold, _ShadeSoftness);
                half shadowTerm = P1ToonShadowTerm(mainLight.shadowAttenuation, 0.72, _ShadeSoftness);

                // 与地面墨（InkGround）和顶面墨（ApplyTopInkOverlay）同一套"墨色明暗"：
                // 只把队伍色压到 0.72~1.0，绝不乘暗色阴影 tint、也不按朝向重压。
                half diffuseLight = lerp(0.72, 1.0, band * shadowTerm);
                half3 coreInkColor = inkSample.rgb * _Tint.rgb * diffuseLight;

                // 浸润暗痕：渗进墙面的半透明暗痕，颜色更暗且带有泥渗感
                half3 haloColor = coreInkColor * 0.32;
                half3 inkColor = lerp(haloColor, coreInkColor, coreMask);

                // 胶质内发光：与地面 _InkGlow / 顶面 _GlobalInkGlow 同项，保证背光墙面的墨仍鲜艳
                inkColor += coreInkColor * _GlobalInkGlow * 0.85 * coreMask;

                // 薄缘提亮：墨迹内侧一圈微微反光，边缘才有厚度感
                float rim = smoothstep(0.52, 0.9, coreMask) * (1.0 - smoothstep(0.9, 0.995, coreMask));
                inkColor *= 1.0 + rim * 0.3;

                // 环境镜面（P1InkEnvSpecular）与防掠射糊白
                float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 R = reflect(-V, N);
                float NdotV = saturate(dot(N, V));

                float rough = P1InkRoughness(_WallWetness, _WallWetness * 0.45, 0.08, 0.52);
                rough = clamp(rough + roughPerturb, 0.04, 0.95);

                half3 envSpec = P1InkEnvSpecular(R, rough, NdotV) * (half)_WallEnvSpec;

                // 竖直立面防掠射糊白对策：
                // 1. 在大掠射角（NdotV接近0）适度平滑衰减，防止整面大角度反光刺眼
                half grazeDamp = smoothstep(0.02, 0.28, NdotV);
                // 2. 镜面保留 25% 队伍墨色，防止强光完全洗白底色
                half3 specTint = lerp(half3(1.0, 1.0, 1.0), coreInkColor, 0.25);
                half3 finalSpec = envSpec * specTint * lerp(0.45, 1.0, grazeDamp);

                // 镜面高光叠加（随 _WallWetness 强度调制）
                inkColor += finalSpec * coreMask * (half)(_WallWetness * 0.7 + 0.3);

                // 掩体立面边界线（P17-A）：UV1 = (面宽, 面高) 米，到 quad 四条边的米距离取 min。
                // 无 UV1 数据（旧网格，均 ≤ 0）时退化为不画线；无墨处 alpha=0 露出基面边线，两者自然拼接。
                float sizeW = input.uv2.x;
                float sizeH = input.uv2.y;
                if (min(sizeW, sizeH) > 0.001)
                {
                    float edgeDist = min(min(input.uv.x * sizeW, (1.0 - input.uv.x) * sizeW),
                                         min(input.uv.y * sizeH, (1.0 - input.uv.y) * sizeH));
                    float edgeFw = max(fwidth(edgeDist), 0.005);
                    float edgeLine = 1.0 - smoothstep(_EdgeLineWidth, _EdgeLineWidth + edgeFw, edgeDist);
                    inkColor = lerp(inkColor, _EdgeLineColor.rgb, edgeLine * _EdgeLineStrength * coreMask);
                }

                inkColor = MixFog(inkColor, input.fogFactor);

                return half4(inkColor, effectiveMask * _Tint.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}