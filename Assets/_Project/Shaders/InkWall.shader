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

        [Header(Geometric Organic)]
        // B2：由 InkWallSurface 从 InkGridConfigSO 三个新字段写入（与 _WallWetness 同一条传值路径）。
        // 三个值全为 0 时片元逻辑退化为旧实现（硬边 / 纯色 / 无垂流 / 整面均匀湿润）。
        _WallEdgeSoftness ("Wall Edge Softness", Range(0.0, 1.0)) = 1.0
        _WallToneJitter ("Wall Tone Jitter", Range(0.0, 2.0)) = 1.0
        _WallDripIntensity ("Wall Drip Intensity", Range(0.0, 2.0)) = 1.0
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
            #pragma target 3.0
                        #if !UNITY_WEBGL
            #pragma multi_compile_instancing
            #endif
            #pragma multi_compile _ P1_INK_HIGH

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"
            #include "InkWetShading.hlsl"
            #include "InkWallStyle.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // (面宽, 面高) 米，由 InkWallSurface 写入
                UNITY_VERTEX_INPUT_INSTANCE_ID
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
                float _WallEdgeSoftness;
                float _WallToneJitter;
                float _WallDripIntensity;
            CBUFFER_END

            // 全局胶质内发光（InkSurfaceController 每帧 SetGlobalFloat），与地面/顶面墨共用，
            // 不放进 UnityPerMaterial，避免不同材质各写一份导致墙地墨色再次分叉。
            float _GlobalInkGlow;
            float _P1WallDripSpeed;
            float4 _InkTex_TexelSize;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
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
                float2 meterPos = input.uv * input.uv2;
                #if defined(P1_INK_HIGH)
                if (_WallDripStrength > 0.001 && input.uv2.y > 0.05 && _WallWetness > 0.01)
                {
                    float colSize = 0.38;
                    float col = floor(meterPos.x / colSize);
                    float rnd = frac(sin(col * 317.15 + 1.37) * 43758.5453);
                    float phase = frac(_Time.y * max(0.0, _P1WallDripSpeed) * (0.25 + rnd * 0.35) + rnd * 7.13);
                    float colCenter = (col + 0.5 + (rnd - 0.5) * 0.55) * colSize;
                    float distCol = abs(meterPos.x - colCenter);
                    float dripW = lerp(0.012, 0.024, rnd);
                    float dripMaskX = smoothstep(dripW, dripW * 0.15, distCol);
                    if (dripMaskX > 0.001)
                    {
                        float dripLenM = lerp(0.06, 0.22, rnd) * (_WallWetness * 0.75 + 0.25) * _WallDripStrength;
                        dripLenM *= 0.72 + 0.28 * sin(phase * 6.2831853);
                        float dripV = (dripLenM / input.uv2.y) * dripMaskX * (0.8 + 0.2 * phase);
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
                // B2-1 软边：wallEdgeSoftness 加宽 fwidth smoothstep 带，并给 alpha 读数叠加低频
                // 噪声扰动，打断 10cm 格子的直线阶梯；soft=0 时 edgeW=fwA、alpha 原样返回，
                // 与原式 smoothstep(0.5 ± fwA, inkSample.a) 逐项一致（硬边）。
                float soft = saturate(_WallEdgeSoftness);
                float fwA = max(fwidth(inkSample.a), 0.004);
                float edgeW = fwA * (1.0 + 1.25 * soft) + soft * 0.03;
                half coreMask = smoothstep(0.5 - edgeW, 0.5 + edgeW,
                                           P1WallSoftEdgeAlpha(inkSample.a, fwA, soft, meterPos, input.positionWS));

                // 浸润暗 halo（damp halo）：墨缘向外一圈渗透暗痕（对齐竞品 32% 肌理透出）
                half haloMask = smoothstep(0.12, 0.5, inkSample.a) * (1.0 - coreMask);
                half haloAlpha = haloMask * (half)(0.42 * _DampHaloStrength);
                half effectiveMask = coreMask + haloAlpha;

                // B2-3 垂流：湿墨下方拉出向下渐隐的细流。
                // 用两次上采样测得「最近的湿墨上缘距离 d」，再乘列噪声、湿润度与格 alpha。
                // 纯着色层（alpha/color），不写回图集，也不参与任何 CPU 判定。
                // _WallDripIntensity=0 时整块跳过，dripMask 恒为 0，输出与旧实现一致。
                half dripMask = 0.0h;
                half3 dripColor = half3(0.0, 0.0, 0.0);
                float dripGate = saturate(_WallDripIntensity);
                if (dripGate > 0.001 && _WallWetness > 0.01 && coreMask < 0.98)
                {
                    float faceH = max(0.10, input.uv2.y);
                    float nearDist = 0.05; // 米：近层上采样，保证贴着墨缘的细流一定取到颜色
                    #if defined(P1_INK_HIGH)
                    float farDist = 0.18;
                    half4 upFar = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, input.uv + float2(0.0, farDist / faceH));
                    #else
                    float farDist = nearDist;
                    half4 upFar = half4(0.0, 0.0, 0.0, 0.0);
                    #endif
                    half4 upNear = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, input.uv + float2(0.0, nearDist / faceH));

                    half aNear = upNear.a;
                    half aFar = upFar.a;
                    half upMax = max(aNear, aFar);
                    if (upMax > 0.5)
                    {
                        // 两层采样的 alpha 线性插值出连续的墨缘距离：d 越大，细流越靠尾端
                        float d;
                        half3 upColor;
                        if (aNear > 0.5)
                        {
                            d = nearDist * (0.5 / max((float)aNear, 0.5));
                            upColor = upNear.rgb;
                        }
                        else
                        {
                            float t = saturate((0.5 - (float)aNear) / max(1e-3, (float)aFar - (float)aNear));
                            d = lerp(nearDist, farDist, t);
                            upColor = upFar.rgb;
                        }

                        // 竖直细流：按墙面横向米制坐标量化成 ~3.5cm 列（与立面 U 轴对齐、等宽；
                        // 叠加法线 hash 让相邻立面的列图案不共形），列内再取一根细窄中心，
                        // 形成细长墨流而不是整片墨帘
                        float colCoord = meterPos.x / 0.035
                                       + dot(input.normalWS, float3(13.7, 7.1, 29.3));
                        float col = floor(colCoord);
                        float colRnd = P1WallHash21(float2(col, 3.7));
                        float colRnd2 = P1WallHash21(float2(col + 11.0, 8.1));
                        float colCenter = 0.5 + (colRnd2 - 0.5) * 0.55;
                        float distCol = abs(frac(colCoord) - colCenter) * 0.035;
                        float halfW = lerp(0.004, 0.011, colRnd2);
                        float thin = 1.0 - smoothstep(halfW, halfW * 2.2, distCol);

                        // 世界 Y 向下梯度：d / 该列细流长度，越往下越淡（二次衰减）
                        float len = lerp(0.07, 0.26, colRnd) * (0.55 + 0.65 * _WallWetness) * _WallDripIntensity;
                        float fade = saturate(1.0 - d / max(0.001, len));
                        fade *= fade;

                        dripMask = (half)(thin * fade * saturate(_WallWetness) * upMax * 0.72);
                        dripColor = upColor;
                    }
                }
                effectiveMask = max(effectiveMask, dripMask);
                if (effectiveMask < 0.01)
                    discard;

                float3 N = normalize(input.normalWS);
                // 建立立面正交切线基（T1 水平向右，T2 垂直向上）
                float3 upRef = abs(N.y) > 0.95 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
                float3 T1 = normalize(cross(upRef, N));
                float3 T2 = cross(N, T1);

                float roughPerturb = 0.0;
                #if defined(P1_INK_HIGH)
                // 墙面 lip 法线：±1 texel 采样求覆盖率梯度，只在边缘带且湿润时扰动法线
                if (_WallWetness > 0.01)
                {
                    float a = inkSample.a;
                    float lipBand = smoothstep(0.30, 0.55, a) * (1.0 - smoothstep(0.62, 0.95, a));
                    if (lipBand > 0.001)
                    {
                        float2 tx = _InkTex_TexelSize.xy;
                        float aR = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, sampleUV + float2(tx.x, 0.0)).a;
                        float aL = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, sampleUV - float2(tx.x, 0.0)).a;
                        float aU = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, sampleUV + float2(0.0, tx.y)).a;
                        float aD = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, sampleUV - float2(0.0, tx.y)).a;
                        float2 gradA = float2(aR - aL, aU - aD) * 0.5;
                        float3 lipN = -(T1 * gradA.x + T2 * gradA.y);
                        N = normalize(N + lipN * (_P1InkTopDetailStrength * 0.15 * _WallWetness * lipBand));
                    }
                }

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
                half band = P1ToonTriBand(dot(N, L), _ShadeThreshold, _ShadeSoftness);
                half shadowTerm = P1ToonShadowTerm(mainLight.shadowAttenuation, 0.72, _ShadeSoftness);

                // 与地面墨（InkGround）和顶面墨（ApplyTopInkOverlay）同一套"墨色明暗"：
                // 只把队伍色压到 0.72~1.0，绝不乘暗色阴影 tint、也不按朝向重压。
                half diffuseLight = lerp(0.72, 1.0, band * shadowTerm);
                half3 coreInkColor = inkSample.rgb * _Tint.rgb * diffuseLight;

                // B2-2 逐区域色调微差：世界坐标 hash/值噪声驱动的亮度抖动（jitter=0 → 恒等 1.0），
                // 呼应地面 splatToneStrength 的观感；只乘墨色，不改图集数据。
                coreInkColor *= (half)P1WallToneMul(input.positionWS, meterPos, _WallToneJitter);

                // 浸润暗痕：渗进墙面的半透明暗痕，颜色更暗且带有泥渗感
                half3 haloColor = coreInkColor * 0.32;
                half3 inkColor = lerp(haloColor, coreInkColor, coreMask);

                // 胶质内发光：与地面 _InkGlow / 顶面 _GlobalInkGlow 同项，保证背光墙面的墨仍鲜艳
                inkColor += coreInkColor * _GlobalInkGlow * 0.85 * coreMask;

                // 薄缘提亮：墨迹内侧一圈微微反光，边缘才有厚度感
                float rim = smoothstep(0.52, 0.9, coreMask) * (1.0 - smoothstep(0.9, 0.995, coreMask));
                inkColor *= 1.0 + rim * 0.3;

                // 视线与高光参数
                float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float NdotV = saturate(dot(N, V));
                float NdotL = saturate(dot(N, L));

                // B2-4 非均匀湿润：整面标量湿润度 × (世界Y + 噪声 + 格 alpha) 分布因子。
                // wallDripIntensity=0 时 P1WallWetMul 返回恒等 1.0，wet 与旧实现完全一致（整面均匀）。
                float wetMul = P1WallWetMul(meterPos, input.positionWS.y, input.uv.y, inkSample.a, _WallDripIntensity);
                float wet = saturate(_WallWetness * wetMul);
                float sheen = wet * 0.5;

                // B2-3 垂流合成：颜色取上游湿墨 + 同一套 toon 漫反射（非预乘，alpha 走 effectiveMask）。
                // 与主体墨重叠时按 coreMask/halo 权重让回主体色，只有墨外区域整段读作细流色。
                if (dripMask > 0.001h)
                {
                    half3 dripLit = dripColor * _Tint.rgb * diffuseLight;
                    dripLit *= (half)(1.0 + _GlobalInkGlow * 0.6 + 0.10 * wet);
                    half inkWeight = saturate(coreMask + haloAlpha * 2.0h);
                    inkColor = lerp(dripLit, inkColor, inkWeight);
                }
                float rough = saturate(1.0 - (wet * 0.6 + 0.2));
                rough = clamp(rough + roughPerturb, 0.04, 0.95);
                float smoothVal = saturate(1.0 - rough);

                // 直接太阳高光 (克制自然高光)
                float shadowAtten = mainLight.shadowAttenuation;
                float specPower = exp2(8.0 * smoothVal + 1.0);
                float specNorm = (specPower + 8.0) / 8.0;
                float directSpec = pow(NdotH, specPower) * specNorm * (0.04 + 0.10 * wet) * shadowAtten * saturate(NdotL * 2.0);
                half3 directTerm = _MainLightColor.rgb * (half)directSpec;

                // 双层清漆镜面高光 (P1InkCoatSpec)
                directTerm += _MainLightColor.rgb * P1InkCoatSpec(NdotH, NdotV, shadowAtten, wet, sheen);

                // 湿润薄缘 (P1InkWetRim)：队色提亮 + 清漆高光
                float edgeBand = P1InkWetRim(inkSample.a, wet, sheen);
                directTerm += _MainLightColor.rgb * (half)(edgeBand * 0.25 * _P1InkEdgeRimStrength * shadowAtten);
                inkColor += coreInkColor * (half)(0.12 * edgeBand * _P1InkEdgeRimStrength * saturate(wet + sheen * 0.4));

                // 直接光高光叠加上墙墨
                inkColor += directTerm * coreMask;

                // 全量环境反射（P1InkEnvSpecularFull，支持桌面 Cubemap 反射与掠射抑制）
                float3 R = reflect(-V, N);
                half3 envSpec = P1InkEnvSpecularFull(R, rough, NdotV, coreInkColor) * (half)_WallEnvSpec;

                // 竖直立面防掠射糊白对策：
                // 1. 在大掠射角（NdotV接近0）适度平滑衰减，防止整面大角度反光刺眼
                half grazeDamp = smoothstep(0.02, 0.28, NdotV);
                // 2. 镜面保留 25% 队伍墨色，防止强光完全洗白底色
                half3 specTint = lerp(half3(1.0, 1.0, 1.0), coreInkColor, 0.25);
                half3 finalSpec = envSpec * specTint * lerp(0.45, 1.0, grazeDamp);

                // 环境镜面高光叠加（随湿润度强度调制；B2 后 wet 含非均匀分布，
                // wallDripIntensity=0 时 wet 恒等于 _WallWetness）
                inkColor += finalSpec * coreMask * (half)(wet * 0.7 + 0.3);

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