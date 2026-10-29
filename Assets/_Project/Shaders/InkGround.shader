Shader "P1/InkGround"
{
    Properties
    {
        [Header(Base Ground Settings)]
        _BaseColor ("Base Ground Color", Color) = (0.20, 0.22, 0.26, 1)
        _GridLineColor ("Grid Line Color", Color) = (0.25, 0.28, 0.33, 1)
        _InkTex ("Ink Texture", 2D) = "black" {}
        _InkMapOriginXZ ("Ink Map Origin XZ", Vector) = (-20, -20, 40, 40)

        [Header(Team Colors for Seam Detection)]
        _TeamAColor ("Team A Color", Color) = (1.00, 0.44, 0.06, 1.0)
        _TeamBColor ("Team B Color", Color) = (0.15, 0.42, 1.00, 1.0)

        [Header(Jelly Lip Settings)]
        _LipStrength ("Meniscus Lip Strength", Range(0.0, 5.0)) = 2.4
        _LipThickness ("Meniscus Lip Thickness", Range(0.1, 2.0)) = 1.0
        _ContactShadowStrength ("Contact Shadow Strength", Range(0.0, 1.0)) = 0.45

        [Header(Wetness and Clearcoat Specular)]
        _WetSmoothness ("Wet Smoothness", Range(0.5, 1.0)) = 0.95
        _DrySmoothness ("Dry Smoothness", Range(0.2, 0.9)) = 0.62
        _ClearcoatStrength ("Clearcoat Specular Strength", Range(0.0, 3.0)) = 1.4
        _WetnessFactor ("Wetness Multiplier", Range(0.0, 2.0)) = 1.0
        _InkGlow ("Subsurface Gel Glow", Range(0.0, 1.0)) = 0.16

        [Header(Overlap Seam Settings)]
        _SeamLipStrength ("Overlap Lip Strength", Range(0.0, 4.0)) = 1.8
        _SeamShadowStrength ("Overlap Crease Shadow", Range(0.0, 1.0)) = 0.48

        [Header(Dither Occlusion Settings)]
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
            };

            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);
            float4 _InkTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GridLineColor;
                float4 _InkMapOriginXZ; // xy = originXZ, zw = mapSizeXZ
                half4 _TeamAColor;
                half4 _TeamBColor;
                float _LipStrength;
                float _LipThickness;
                float _ContactShadowStrength;
                float _WetSmoothness;
                float _DrySmoothness;
                float _ClearcoatStrength;
                float _WetnessFactor;
                float _InkGlow;
                float _SeamLipStrength;
                float _SeamShadowStrength;
                float _DitherAlpha;
                float4 _FocusPos;
                float _FocusRadius;
                float _FocusMinAlpha;
                float _FocusWeight;
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
                return output;
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

                // 贴图采样与纹素大小
                float4 inkSample = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, inkUV);
                float2 texelUV = _InkTex_TexelSize.xy > 0.00001 ? _InkTex_TexelSize.xy : (1.0 / max(float2(64.0, 64.0), mapSize * 10.0));

                // 4 邻域微偏移采样以获取覆盖度与队伍比例在 UV 空间的解析梯度
                float2 uvOffset = texelUV * 1.25;
                float4 sampR = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV + float2(uvOffset.x, 0.0)));
                float4 sampL = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV - float2(uvOffset.x, 0.0)));
                float4 sampU = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV + float2(0.0, uvOffset.y)));
                float4 sampD = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, saturate(inkUV - float2(0.0, uvOffset.y)));

                // 覆盖度 (Coverage) 及其解析梯度
                float amt = inkSample.a;
                float fw = fwidth(amt);
                float edgeWidth = clamp(fw * 0.8, 0.008, 0.22);
                float inkMask = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, amt);

                // 梯度 gradAmt: 指向覆盖度增加的方向 (沿 UV xz)
                float2 gradAmt = float2(sampR.a - sampL.a, sampU.a - sampD.a) * 0.5;

                // 1米战术参考网格线（辅助俯视角判断距离与移动速度感）
                float2 gridCoord = abs(frac(input.positionWS.xz * 0.5) - 0.5);
                float gridLine = smoothstep(0.47, 0.495, max(gridCoord.x, gridCoord.y));
                half3 groundCol = lerp(_BaseColor.rgb, _GridLineColor.rgb, gridLine * 0.55);

                // 高度阶梯增强：地面 (Y<=0.1) 为暗灰石基，斜坡线性平滑提亮，高台顶面明度显著提升
                float heightFactor = saturate((input.positionWS.y - 0.05) / 1.05);
                half3 elevatedGroundCol = groundCol + heightFactor * half3(0.12, 0.14, 0.18);

                Light mainLight = GetMainLight();
                float3 geomN = normalize(input.normalWS);

                // 朝上的面（地面/斜坡/高台顶面）才涂墨，垂直侧面不涂
                half upness = smoothstep(0.55, 0.85, geomN.y);
                inkMask *= upness;

                // 竖直侧立面深度
                half3 cliffCol = half3(0.10, 0.11, 0.14);
                half3 baseSurfaceCol = lerp(cliffCol, elevatedGroundCol, upness);

                // -------------------------------------------------------------
                // 1. 边缘接触软阴影 (Contact Shadow)
                // 墨水边缘外侧 (0.05 < amt < 0.52 且 inkMask 接近 0) 向背光方向投射软阴影
                // -------------------------------------------------------------
                float contactHalo = smoothstep(0.04, 0.48, amt) * (1.0 - inkMask);
                float2 outN = -gradAmt / max(length(gradAmt), 0.001);
                float2 sunDirXZ = normalize(mainLight.direction.xz + float2(0.001, 0.001));
                float shadowBias = clamp(0.65 - 0.75 * dot(outN, sunDirXZ), 0.25, 1.35);
                float contactShadow = contactHalo * shadowBias * _ContactShadowStrength * upness;
                baseSurfaceCol *= (1.0 - contactShadow);

                // -------------------------------------------------------------
                // 2. 湿润度 (Wetness) 解析
                // 优先从 G 通道或采样色提取，随时间或衰减因子平滑过渡
                // -------------------------------------------------------------
                float rawWet = inkSample.g * _WetnessFactor;
                float wetness = saturate(rawWet);
                float fresh = smoothstep(0.18, 0.92, wetness);

                // -------------------------------------------------------------
                // 3. 片元梯度分析计算解析法线微凸 (Meniscus Lip)
                // 边缘饱满鼓起，呈现油漆表面张力形成的 3D 果冻边缘
                // -------------------------------------------------------------
                float s = saturate((amt - 0.46) * 1.85);
                float hs = 1.0 - (1.0 - s) * (1.0 - s); // 边缘 0 -> 内部 1 的圆滑高程轮廓
                float lipSlope = 2.0 * (1.0 - s) * 1.85;

                // 湿润墨水边缘更厚更饱满
                float thick = _LipThickness * (1.0 + 0.5 * fresh);
                float2 lipNormalOffsetXZ = -gradAmt * lipSlope * (_LipStrength * 2.0) * thick;

                // -------------------------------------------------------------
                // 4. 两队墨水交界重叠凹凸缝与暗影 (Overlap Lip & Crease)
                // -------------------------------------------------------------
                half3 dCol = _TeamBColor.rgb - _TeamAColor.rgb;
                float dColLenSq = max(dot(dCol, dCol), 0.001);
                float tRaw = saturate(dot(inkSample.rgb - _TeamAColor.rgb, dCol) / dColLenSq);

                float tR = saturate(dot(sampR.rgb - _TeamAColor.rgb, dCol) / dColLenSq);
                float tL = saturate(dot(sampL.rgb - _TeamAColor.rgb, dCol) / dColLenSq);
                float tU = saturate(dot(sampU.rgb - _TeamAColor.rgb, dCol) / dColLenSq);
                float tD = saturate(dot(sampD.rgb - _TeamAColor.rgb, dCol) / dColLenSq);

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

                    // 估算两边湿润度差（较湿的新墨水微骑在旧墨水上方）
                    float wetR = sampR.g;
                    float wetL = sampL.g;
                    float wetU = sampU.g;
                    float wetD = sampD.g;
                    float2 gradWet = float2(wetR - wetL, wetU - wetD) * 0.5;
                    float newer = clamp(dot(gradWet, sn) / max(gTl, 0.001) * 2.0, -1.0, 1.0);

                    float dhSeam = newer * 1.6 * e1 + 1.1 * x * e2 * (1.0 - 0.5 * abs(newer));
                    seamNormalOffsetXZ = -sn * dhSeam * _SeamLipStrength * thick;

                    // 交界缝接触微暗阴影
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

                // 汇总法线微扰
                float2 totalOffsetXZ = (lipNormalOffsetXZ + seamNormalOffsetXZ + rippleNormalOffsetXZ) * inkMask;
                float3 finalNormalWS = normalize(geomN + float3(totalOffsetXZ.x, 0.0, totalOffsetXZ.y));

                // -------------------------------------------------------------
                // 6. 光照、清漆镜面高光与缎面干墨衰减
                // -------------------------------------------------------------
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(finalNormalWS, L));
                float NdotV = saturate(dot(finalNormalWS, V));
                float NdotH = saturate(dot(finalNormalWS, H));

                // 平滑度：新落墨提升至 0.95，衰减为缎面 0.62；波纹波峰处微增光泽
                float baseSmoothness = lerp(_DrySmoothness, _WetSmoothness, fresh);
                float smoothness = saturate(baseSmoothness + rippleGloss * 0.15 * inkMask);

                // 清漆镜面高光 (Clearcoat Specular)
                float specPower = exp2(10.0 * smoothness + 1.0); // 0.95 时高光极细极亮
                float specNorm = (specPower + 8.0) / 8.0;
                float directSpec = pow(NdotH, specPower) * specNorm * (0.04 + 0.08 * fresh);

                // 针对俯视角调优的掠射菲涅尔（保证大角度不发白，锁住纯净鲜艳的队伍墨色）
                float fresnel = pow(1.0 - NdotV, 3.5);
                float clearcoatF0 = lerp(0.035, 0.065, fresh);
                float F = clearcoatF0 + (0.35 - clearcoatF0) * fresnel;

                float finalClearcoat = directSpec * F * _ClearcoatStrength * NdotL * inkMask;

                // 漫反射光照（暗部补光，绝不出现脏暗黑褐）
                float diffuseLight = NdotL * 0.52 + 0.48;

                // 墨水颜色构建：半透明微凸缘略微透光更亮更鲜活，中心饱满厚重
                float lipHighlight = (1.0 - hs) * smoothstep(0.4, 0.9, amt);
                half3 inkColor = inkSample.rgb;
                inkColor = lerp(inkColor, inkColor * 1.15 + half3(0.04, 0.04, 0.04), lipHighlight * 0.35);

                // 新鲜湿润状态轻微提亮与增加通透度
                inkColor *= (1.0 + 0.08 * fresh);

                // 扣减重叠交界缝暗影
                inkColor *= (1.0 - seamShadow);

                // 胶质内发光 (Subsurface Gel Glow) 保证在背光暗部也鲜艳通透
                half3 gelGlow = inkSample.rgb * _InkGlow * (0.7 + 0.3 * hs) * (1.0 - NdotL * 0.5);

                half3 litInkColor = inkColor * diffuseLight + finalClearcoat + gelGlow;

                // 最终混合地面石板基底与果冻立体墨水
                half3 finalRgb = lerp(baseSurfaceCol * (saturate(dot(geomN, L)) * 0.48 + 0.52), litInkColor, inkMask);

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
