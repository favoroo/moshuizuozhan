Shader "P1/InkGround"
{
    Properties
    {
        [Header(Base Ground Settings)]
        _BaseColor ("Base Ground Color", Color) = (0.20, 0.22, 0.26, 1)
        _GridLineColor ("Grid Line Color", Color) = (0.25, 0.28, 0.33, 1)
        _InkTex ("Ink Atlas (R=team G=wet B=tone A=coverage)", 2D) = "black" {}
        _InkMapOriginXZ ("Ink Map Origin XZ", Vector) = (-20, -28, 40, 56)
        _InkTexelSize ("Ink Texel Size", Vector) = (0.0002441406, 0.0002441406, 4096, 4096)
        _GroundTex ("Ground Albedo (Arena)", 2D) = "white" {}
        _GroundTexStrength ("Ground Texture Strength", Range(0, 1)) = 1.0
        _CliffColor ("Cliff / Vertical Side Color", Color) = (0.34, 0.33, 0.31, 1.0)

        [Header(Team Colors)]
        _TeamAColor ("Team A Color", Color) = (1.00, 0.44, 0.06, 1.0)
        _TeamBColor ("Team B Color", Color) = (0.15, 0.42, 1.00, 1.0)

        [Header(Gel Lip Settings)]
        _LipHeight ("Lip Height (m)", Range(0.0, 0.06)) = 0.014
        _LipSoftness ("Lip Falloff", Range(0.5, 4.0)) = 1.7
        _ContactShadowStrength ("Contact Shadow Strength", Range(0.0, 1.0)) = 0.45
        _EdgeSoftness ("Edge Screen AA Width", Range(0.0, 2.0)) = 0.9

        [Header(Wet Coat)]
        _WetGloss ("Wet Smoothness", Range(0.4, 1.0)) = 0.88
        _DryGloss ("Dry Smoothness", Range(0.2, 0.9)) = 0.52
        _ClearcoatStrength ("Clearcoat Specular Strength", Range(0.0, 3.0)) = 1.4
        _WetnessFactor ("Wetness Multiplier", Range(0.0, 2.0)) = 1.0
        _InkGlow ("Subsurface Gel Glow", Range(0.0, 1.0)) = 0.16

        [Header(Gel Micro Surface)]
        _GelNoiseScale ("Gel Noise Scale", Range(0.2, 12.0)) = 2.2
        _GelNoiseStrength ("Gel Noise Strength", Range(0.0, 0.25)) = 0.035
        _ToneStrength ("Per Splat Tone Strength", Range(0.0, 2.0)) = 1.0

        [Header(Overlap Seam)]
        _SeamLipHeight ("Overlap Lip Height (m)", Range(0.0, 0.06)) = 0.02
        _SeamShadowStrength ("Overlap Crease Shadow", Range(0.0, 1.0)) = 0.48

        [Header(Dither Occlusion)]
        _DitherAlpha ("Dither Alpha", Range(0.0, 1.0)) = 1.0
        _FocusPos ("Focus World Pos", Vector) = (0, 0, 0, 0)
        _FocusRadius ("Focus Radius", Float) = 2.2
        _FocusMinAlpha ("Focus Min Alpha", Range(0.0, 1.0)) = 0.25
        _FocusWeight ("Focus Weight", Range(0.0, 1.0)) = 0.0
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_INK_RIPPLES 8

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);

            TEXTURE2D(_GroundTex);
            SAMPLER(sampler_GroundTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GridLineColor;
                float4 _InkMapOriginXZ;
                float4 _InkTexelSize;
                half4 _TeamAColor;
                half4 _TeamBColor;
                float _LipHeight;
                float _LipSoftness;
                float _ContactShadowStrength;
                float _EdgeSoftness;
                float _WetGloss;
                float _DryGloss;
                float _ClearcoatStrength;
                float _WetnessFactor;
                float _InkGlow;
                float _GelNoiseScale;
                float _GelNoiseStrength;
                float _ToneStrength;
                float _SeamLipHeight;
                float _SeamShadowStrength;
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
                half4 _CliffColor;
                float _GroundTexStrength;
            CBUFFER_END

            // 由 InkSurfaceController 每帧通过 MaterialPropertyBlock 传入的动态波纹数据与时间
            float4 _InkRipples[MAX_INK_RIPPLES];      // xyz = worldPos, w = birthTime
            float4 _InkRippleParams[MAX_INK_RIPPLES]; // x = amplitude, y = wavelength, z = speed, w = lifetime
            float _InkTime;

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            // ---------------------------------------------------------------- 程序化噪声
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
                // 俯视角网点半透明遮挡剔除 (Screen-Door Dither Occlusion)
                float effectiveAlpha = _DitherAlpha;
                if (_FocusWeight > 0.001)
                {
                    float d = length(input.positionWS.xz - _FocusPos.xz);
                    float focusFade = saturate(d / max(0.1, _FocusRadius));
                    effectiveAlpha = lerp(_FocusMinAlpha, _DitherAlpha, focusFade);
                }

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

                float2 mapOrigin = _InkMapOriginXZ.xy;
                float2 mapSize = max(float2(0.001, 0.001), _InkMapOriginXZ.zw);
                float2 inkUV = saturate((input.positionWS.xz - mapOrigin) / mapSize);

                // 图集采样：图集已是高分辨率的连续覆盖率场，不再是二值网格
                float2 texelUV = _InkTexelSize.xy > 0.000001 ? _InkTexelSize.xy : float2(1.0 / 4096.0, 1.0 / 4096.0);
                float4 inkSample = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, inkUV);

                // 4 邻域采样：解析覆盖率 / 队伍比例 / 湿润度梯度
                float2 uvOffset = texelUV;
                float4 sampR = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV + float2(uvOffset.x, 0.0)));
                float4 sampL = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV - float2(uvOffset.x, 0.0)));
                float4 sampU = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV + float2(0.0, uvOffset.y)));
                float4 sampD = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV - float2(0.0, uvOffset.y)));

                // 图集通道全部按覆盖率预乘，这里反预乘回真实数据
                float amt = inkSample.a;
                float invA = 1.0 / max(amt, 0.02);
                float tRaw = saturate(inkSample.r * invA);
                float wetRaw = saturate(inkSample.g * invA);
                float tone = saturate(inkSample.b * invA);

                // 覆盖率边缘：图集内已有约 3 texel 的柔和过渡，这里只补一层屏幕空间抗锯齿
                float fw = fwidth(amt);
                float edgeWidth = clamp(fw * _EdgeSoftness, 0.012, 0.35);
                float inkMask = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, amt);

                // 队伍交界：用 R 通道（队伍占比）做 1 像素锐利过渡，绝不出现浑浊混色带
                float wT = clamp(fwidth(tRaw) * 0.75, 0.012, 0.25);
                float tm = smoothstep(0.5 - wT, 0.5 + wT, tRaw);
                half3 teamCol = lerp(_TeamAColor.rgb, _TeamBColor.rgb, tm);

                // 世界空间覆盖率梯度（单位：每米），保证任何图集分辨率下法线尺度都正确
                float2 worldOffset = max(uvOffset * mapSize, float2(1e-5, 1e-5));
                float2 gradAmt = float2(sampR.a - sampL.a, sampU.a - sampD.a) / (2.0 * worldOffset);

                // 2m 混凝土板缝仅作为无贴图时的兜底；烘焙贴图已经包含板缝/标线/AO，优先级更高。
                float2 gridCoord = abs(frac(input.positionWS.xz * 0.5) - 0.5);
                float gridLine = smoothstep(0.47, 0.495, max(gridCoord.x, gridCoord.y));

                half4 groundTex = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex, inkUV);
                half3 bakedGround = lerp(_BaseColor.rgb, groundTex.rgb, _GroundTexStrength);
                half3 groundCol = lerp(bakedGround, _GridLineColor.rgb, gridLine * 0.12);

                // 高度阶梯增强：高台/斜坡顶面略提亮，但不过度洗掉混凝土细节
                float heightFactor = saturate((input.positionWS.y - 0.05) / 1.05);
                half3 elevatedGroundCol = groundCol + heightFactor * half3(0.06, 0.07, 0.08);

                Light mainLight = GetMainLight();
                float shadowAtten = mainLight.shadowAttenuation;
                float3 geomN = normalize(input.normalWS);

                // 朝上的面（地面/斜坡/高台顶面）才涂墨，垂直侧面不涂
                half upness = smoothstep(0.55, 0.85, geomN.y);
                inkMask *= upness;

                half3 baseSurfaceCol = lerp(_CliffColor.rgb, elevatedGroundCol, upness);

                // -------------------------------------------------------------
                // 1. 边缘接触软阴影 (Contact Shadow)
                // -------------------------------------------------------------
                float contactHalo = smoothstep(0.04, 0.48, amt) * (1.0 - inkMask);
                float2 outN = -gradAmt / max(length(gradAmt), 0.001);
                float2 sunDirXZ = normalize(mainLight.direction.xz + float2(0.001, 0.001));
                float shadowBias = clamp(0.65 - 0.75 * dot(outN, sunDirXZ), 0.25, 1.35);
                float contactShadow = contactHalo * shadowBias * _ContactShadowStrength * upness;
                baseSurfaceCol *= (1.0 - contactShadow);

                // -------------------------------------------------------------
                // 2. 湿润度（图集 G 通道，随烘干 Pass 在约 6 秒内衰减）
                // -------------------------------------------------------------
                float wetness = saturate(wetRaw * _WetnessFactor);
                float fresh = smoothstep(0.2, 0.95, wetness);

                // -------------------------------------------------------------
                // 3. 厚度剖面与边缘鼓起 (Meniscus Lip)
                //    s: 边缘 0 -> 内部 1；hs: 圆滑高程轮廓
                // -------------------------------------------------------------
                float s = saturate((amt - 0.5) * _LipSoftness);
                float hs = 1.0 - (1.0 - s) * (1.0 - s);
                float lipSlope = 2.0 * (1.0 - s) * _LipSoftness;
                float thick = 1.0 + 0.5 * fresh;
                float2 lipNormalOffsetXZ = -gradAmt * lipSlope * _LipHeight * thick;

                // -------------------------------------------------------------
                // 4. 两队墨水交界重叠缝与暗影
                // -------------------------------------------------------------
                float aR = max(sampR.a, 0.02);
                float aL = max(sampL.a, 0.02);
                float aU = max(sampU.a, 0.02);
                float aD = max(sampD.a, 0.02);
                float tR = saturate(sampR.r / aR);
                float tL = saturate(sampL.r / aL);
                float tU = saturate(sampU.r / aU);
                float tD = saturate(sampD.r / aD);

                float2 gradTeam = float2(tR - tL, tU - tD) * 0.5;
                float gTl = length(gradTeam);

                float seamShadow = 0.0;
                float2 seamNormalOffsetXZ = float2(0.0, 0.0);

                if (gTl > 0.02 && amt > 0.52)
                {
                    float2 sn = gradTeam / gTl; // 跨缝法线向 (Team A -> Team B)
                    float x = (tRaw - 0.5) / max(gTl, 0.001);
                    float e1 = exp(-x * x * 2.2);
                    float e2 = exp(-x * x * 4.8);

                    // 较湿的新墨水微骑在旧墨水上方
                    float wetR = sampR.g / aR;
                    float wetL = sampL.g / aL;
                    float wetU = sampU.g / aU;
                    float wetD = sampD.g / aD;
                    float2 gradWet = float2(wetR - wetL, wetU - wetD) * 0.5;
                    float newer = clamp(dot(gradWet, sn) / max(gTl, 0.001) * 2.0, -1.0, 1.0);

                    float dhSeam = newer * 1.6 * e1 + 1.1 * x * e2 * (1.0 - 0.5 * abs(newer));
                    seamNormalOffsetXZ = -sn * dhSeam * _SeamLipHeight * thick;

                    seamShadow = e2 * (1.0 - 0.55 * abs(newer)) * _SeamShadowStrength;
                }

                // -------------------------------------------------------------
                // 5. 8 组动态波纹 (Ripples)
                // -------------------------------------------------------------
                float2 rippleNormalOffsetXZ = float2(0.0, 0.0);
                float rippleGloss = 0.0;

                for (int i = 0; i < MAX_INK_RIPPLES; i++)
                {
                    float4 Pr = _InkRipples[i];
                    float4 Qr = _InkRippleParams[i];
                    float age = _InkTime - Pr.w;
                    if (age <= 0.001 || age >= Qr.w) continue;

                    float2 D = input.positionWS.xz - Pr.xz;
                    float dist = length(D);
                    float front = Qr.z * age;
                    float x = dist - front;
                    float sg = max(0.02, Qr.y * 0.85);
                    if (x > sg * 3.5 || x < -Qr.y * 4.0) continue;

                    float env = exp(-x * x / (sg * sg));
                    float k = 6.2831853 / max(0.02, Qr.y);
                    float fade = 1.0 - (age / Qr.w);
                    fade = fade * fade * smoothstep(0.0, 0.04, age);
                    float amp = Qr.x * fade / (1.0 + dist * 1.5);

                    float dh = amp * env * (-2.0 * x / (sg * sg) * cos(k * x) - k * sin(k * x));
                    float2 dir = D / max(dist, 0.0001);
                    rippleNormalOffsetXZ += -dir * dh;
                    rippleGloss += env * fade * saturate(amp * 45.0);
                }

                // -------------------------------------------------------------
                // 6. 胶体微表面：细碎起伏让湿面反射碎开，而不是一块镜面塑料
                //    （按世界空间像素足迹淡出，远处自动趋平）
                // -------------------------------------------------------------
                float gelFade = 1.0 - smoothstep(0.02, 0.14, length(fwidth(input.positionWS.xz)));
                float2 gp = input.positionWS.xz * _GelNoiseScale;
                gp += float2(_InkTime * 0.02, -_InkTime * 0.013)
                    + fresh * fresh * 0.03 * float2(sin(_InkTime * 2.3), cos(_InkTime * 1.9));
                float2 q = gp * 2.2;
                const float ge = 0.12;
                float hx = vnoise(q + float2(ge, 0.0)) - vnoise(q - float2(ge, 0.0));
                float hy = vnoise(q + float2(0.0, ge)) - vnoise(q - float2(0.0, ge));
                float2 gelSlope = float2(hx, hy) * (1.0 / (2.0 * ge))
                                * _GelNoiseStrength * gelFade * inkMask * (0.5 + 0.5 * (1.0 - fresh));

                // 汇总法线微扰
                float2 totalOffsetXZ = (lipNormalOffsetXZ + seamNormalOffsetXZ + rippleNormalOffsetXZ + gelSlope) * inkMask;
                float3 finalNormalWS = normalize(geomN + float3(totalOffsetXZ.x, 0.0, totalOffsetXZ.y));

                // -------------------------------------------------------------
                // 7. 光照、清漆镜面高光与缎面干墨衰减
                // -------------------------------------------------------------
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(finalNormalWS, L));
                float NdotV = saturate(dot(finalNormalWS, V));
                float NdotH = saturate(dot(finalNormalWS, H));

                float baseSmoothness = lerp(_DryGloss, _WetGloss, fresh);
                float smoothness = saturate(baseSmoothness + rippleGloss * 0.15 * inkMask);

                // 法线变化快于像素网格的地方展宽高光 —— 消除边缘/远处的碎钻闪点
                float3 cdu = ddx(finalNormalWS);
                float3 cdv = ddy(finalNormalWS);
                float kern = min(0.35 * (dot(cdu, cdu) + dot(cdv, cdv)), 0.3);
                float effSmooth = saturate(smoothness * (1.0 - kern * 1.8));

                float specPower = exp2(10.0 * effSmooth + 1.0);
                float specNorm = (specPower + 8.0) / 8.0;
                float directSpec = pow(NdotH, specPower) * specNorm * (0.04 + 0.08 * fresh);

                // 针对俯视角调优的掠射菲涅尔（保证大角度不发白，锁住纯净鲜艳的队伍墨色）
                float fresnel = pow(1.0 - NdotV, 3.5);
                float clearcoatF0 = lerp(0.035, 0.065, fresh);
                float F = clearcoatF0 + (0.35 - clearcoatF0) * fresnel;

                float finalClearcoat = directSpec * F * _ClearcoatStrength * NdotL * inkMask;

                // 漫反射光照（暗部补光，绝不出现脏暗黑褐；阴影只做温和压暗，保留墨色辨识度）
                float shadowFill = lerp(0.55, 1.0, shadowAtten);
                float diffuseLight = (NdotL * 0.52 + 0.48) * lerp(0.74, 1.0, shadowAtten);

                // 墨水颜色：队伍色 × 单斑色调微差 × 低频色相起伏（取代旧的逐格马赛克噪声）
                half3 inkColor = teamCol * (0.93 + 0.13 * tone * _ToneStrength);
                inkColor *= 1.0 + 0.06 * (vnoise(input.positionWS.xz * 0.55 + 11.3) - 0.5) * _ToneStrength;

                // 薄缘半透明略亮更鲜活，厚体略深
                float lipHighlight = (1.0 - hs);
                inkColor = lerp(inkColor, inkColor * 1.16 + teamCol * 0.05, lipHighlight * 0.35);
                inkColor *= 1.0 - 0.05 * hs;

                // 新鲜湿润状态轻微提亮与增加通透度
                inkColor *= 1.0 + 0.07 * fresh * fresh;

                // 扣减重叠交界缝暗影
                inkColor *= 1.0 - 0.3 * seamShadow * inkMask;

                half3 litInkColor = inkColor * diffuseLight + finalClearcoat;

                // 胶质内发光 (Subsurface Gel Glow) 保证在背光暗部也鲜艳通透
                litInkColor += inkColor * _InkGlow * (0.75 + 0.35 * hs) * (1.0 - NdotL * 0.5);

                // 最终混合地面石板基底与果冻立体墨水
                float baseDiffuse = (saturate(dot(geomN, L)) * 0.48 + 0.52) * shadowFill;
                half3 finalRgb = lerp(baseSurfaceCol * baseDiffuse, litInkColor, inkMask);
                finalRgb = MixFog(finalRgb, input.fogFactor);

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }

        // 高台/斜坡/地面也需要投影：否则俯视角下所有高低差都像贴纸一样「飘」在地面上。
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
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GridLineColor;
                float4 _InkMapOriginXZ;
                float4 _InkTexelSize;
                half4 _TeamAColor;
                half4 _TeamBColor;
                float _LipHeight;
                float _LipSoftness;
                float _ContactShadowStrength;
                float _EdgeSoftness;
                float _WetGloss;
                float _DryGloss;
                float _ClearcoatStrength;
                float _WetnessFactor;
                float _InkGlow;
                float _GelNoiseScale;
                float _GelNoiseStrength;
                float _ToneStrength;
                float _SeamLipHeight;
                float _SeamShadowStrength;
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
                half4 _CliffColor;
                float _GroundTexStrength;
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

                output.positionWS = positionWS;
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
                float effectiveAlpha = _DitherAlpha;
                if (_FocusWeight > 0.001)
                {
                    float d = length(input.positionWS.xz - _FocusPos.xz);
                    float focusFade = saturate(d / max(0.1, _FocusRadius));
                    effectiveAlpha = lerp(_FocusMinAlpha, _DitherAlpha, focusFade);
                }

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
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
