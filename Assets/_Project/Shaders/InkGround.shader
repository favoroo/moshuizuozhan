Shader "P1/InkGround"
{
    Properties
    {
        [Header(Base Ground Settings)]
        _BaseColor ("Base Ground Color", Color) = (0.20, 0.22, 0.26, 1)
        _GridLineColor ("Grid Line Color", Color) = (0.25, 0.28, 0.33, 1)
        _InkTex ("Ink Atlas (R=team G=birth-ts*coverage B=tone A=coverage)", 2D) = "black" {}
        _InkMapOriginXZ ("Ink Map Origin XZ", Vector) = (-20, -28, 40, 56)
        _InkTexelSize ("Ink Texel Size", Vector) = (0.0002441406, 0.0002441406, 4096, 4096)
        // 仅 InkAtlasPaint / InkAtlasStamp 两个图集写入 Pass 使用，由 InkSplatAtlas 在运行时
        // 通过 Material.SetVector/SetFloat 写入。声明为隐藏属性是为了让 UnityPerMaterial
        // 常量缓冲区一定有这三项（并给出非零兜底默认值：_InkYFlip 若为 0 会把墨斑四边形压成
        // 一条零高度线，图集将静默地画不出任何墨）。
        [HideInInspector] _InkMapSize ("Ink Map Size (m)", Vector) = (40, 56, 0, 0)
        [HideInInspector] _InkTexelMeters ("Ink Atlas Texel Meters", Float) = 0.01
        [HideInInspector] _InkYFlip ("Ink Atlas Y Flip", Float) = 1
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

        [Header(Toon Shading)]
        _ShadeThreshold ("Ground Shade Threshold", Range(0.2, 0.8)) = 0.5
        _ShadeSoftness ("Ground Shade Softness", Range(0.005, 0.2)) = 0.05
        _ShadowTint ("Shadow Tint", Color) = (0.42, 0.47, 0.64, 1)
        _ShadowFloor ("Ground Shadow Floor", Range(0.0, 1.0)) = 0.55
        _InkShadeThreshold ("Ink Shade Threshold", Range(0.2, 0.8)) = 0.48
        _InkShadeSoftness ("Ink Shade Softness", Range(0.005, 0.2)) = 0.08
        _InkShadowFloor ("Ink Shadow Floor", Range(0.0, 1.0)) = 0.72
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
            #include "ToonLighting.hlsl"

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

            // 墨迹图集改为采样全局 uniform：由 InkSurfaceController 每帧 Shader.SetGlobal* 推送
            // （UpdateDynamicMaterialProperties），不依赖 per-renderer MaterialPropertyBlock。
            // 历史写法读取 UnityPerMaterial._InkTex，而 OcclusionFader 恢复不透明时会
            // SetPropertyBlock(null) 把整块 MPB 清空 —— 高台/坡道/出生台随即回落到材质默认的
            // 黑贴图，覆盖率恒为 0，表现为"这些地方永远喷不上油漆"。改全局后与遮挡系统彻底解耦。
            TEXTURE2D(_GlobalInkAtlasTex);
            float4 _GlobalInkMapOriginXZ;
            float4 _GlobalInkTexelSize;

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
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                float _InkShadeThreshold;
                float _InkShadeSoftness;
                float _InkShadowFloor;
                // 墨迹图集写入端（InkAtlasPaint / InkAtlasStamp Pass）使用的映射参数；
                // 加入同一 UnityPerMaterial 保证所有 Pass 布局一致（SRP Batcher 兼容）。
                // ForwardLit / ShadowCaster / DepthNormals 不读取它们，数值不影响场景渲染。
                float4 _InkMapSize;
                float _InkTexelMeters;
                float _InkYFlip;
            CBUFFER_END

            // 由 InkSurfaceController 每帧通过 MaterialPropertyBlock 传入的动态波纹数据与时间
            float4 _InkRipples[MAX_INK_RIPPLES];      // xyz = worldPos, w = birthTime
            float4 _InkRippleParams[MAX_INK_RIPPLES]; // x = amplitude, y = wavelength, z = speed, w = lifetime
            float _InkTime;

            // ---- 湿润时间戳时钟（图集 G 通道新语义：G = 最新落墨时间戳×覆盖率，片元反预乘后现算）----
            // 由 InkSurfaceController 每帧 SetGlobal；周期与 InkSplatAtlas 的落墨编码共用同一配置，
            // 湿润度/烘干/生长全部由片元按 (_InkWetClockNorm - G) 现算，图集不再做周期性整图重绘。
            float _InkWetClockNorm;      // (Time.time % 周期) / 周期 ∈ [0,1)
            float _InkWetClockPeriod;    // 时间戳回绕周期（秒）
            float _InkWetDryingSeconds;  // inkDryingSeconds：湿润→烘干时长
            float _InkGrowSeconds;       // splatGrowSeconds：生长动画时长（0 = 无生长动画）
            float _InkGrowShrinkMeters;  // 生长侵蚀最大回缩距离（米）
            float _InkGrowActive;        // 1 = 本帧存在生长窗口内的墨斑（仅作快速剔除，正确性由纹素年龄保证）

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

                float2 mapOrigin = _GlobalInkMapOriginXZ.xy;
                float2 mapSize = max(float2(0.001, 0.001), _GlobalInkMapOriginXZ.zw);
                float2 inkUV = saturate((input.positionWS.xz - mapOrigin) / mapSize);

                // 图集采样：图集已是高分辨率的连续覆盖率场，不再是二值网格
                float2 texelUV = _GlobalInkTexelSize.xy > 0.000001
                    ? _GlobalInkTexelSize.xy
                    : float2(1.0 / 4096.0, 1.0 / 4096.0);
                float4 inkSample = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, inkUV);

                // 4 邻域采样：解析覆盖率 / 队伍比例 / 湿润度梯度
                float2 uvOffset = texelUV;
                float4 sampR = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV + float2(uvOffset.x, 0.0)));
                float4 sampL = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV - float2(uvOffset.x, 0.0)));
                float4 sampU = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV + float2(0.0, uvOffset.y)));
                float4 sampD = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV - float2(0.0, uvOffset.y)));

                // R/G/B 均按覆盖率预乘，这里反预乘回真实数据：
                // G = 最新落墨时间戳 × 覆盖率（InkAtlasStamp Pass 预乘后 Max 混合写入），
                // 预乘保证双线性/mip 过滤下 G/A 恒等于真实时间戳（原始时间戳在墨斑边缘会被插值污染）。
                float amt = inkSample.a;
                float invA = 1.0 / max(amt, 0.02);
                float tRaw = saturate(inkSample.r * invA);
                float tone = saturate(inkSample.b * invA);

                // 湿润度改为片元现算：age = (now − 落墨时间戳) mod 周期，
                // 线性烘干曲线与旧「每 1/15s 整图 G−=步长」的烘干 Pass 数学等价，但不再重写图集。
                float tsNewest = saturate(inkSample.g * invA);
                float inkAgeSec = frac(_InkWetClockNorm - tsNewest + 1.0) * max(0.001, _InkWetClockPeriod);
                float wetLinear = saturate(1.0 - inkAgeSec / max(0.25, _InkWetDryingSeconds));

                // 队伍交界：用 R 通道（队伍占比）做 1 像素锐利过渡，绝不出现浑浊混色带
                float wT = clamp(fwidth(tRaw) * 0.75, 0.012, 0.25);
                float tm = smoothstep(0.5 - wT, 0.5 + wT, tRaw);
                half3 teamCol = lerp(_TeamAColor.rgb, _TeamBColor.rgb, tm);

                // 世界空间覆盖率梯度（单位：每米），保证任何图集分辨率下法线尺度都正确
                float2 worldOffset = max(uvOffset * mapSize, float2(1e-5, 1e-5));
                float2 gradAmt = float2(sampR.a - sampL.a, sampU.a - sampD.a) / (2.0 * worldOffset);

                // 生长动画（片元侧）：新墨斑落墨即以最终形态写入图集，这里按时间戳把可见边缘
                // 从最终轮廓向外偏移采样（形态学侵蚀）回缩 _InkGrowShrinkMeters，随生长进度松开，
                // 观感等价旧的「生长期间每帧重画放大墨斑」，但图集只在落墨那一刻写一次。
                // 额外的一次图集采样只发生在 age < grow*1.6 的新鲜纹素上，空闲时零开销。
                float amtEff = amt;
                if (_InkGrowActive > 0.5 && _InkGrowSeconds > 0.001 && inkAgeSec < _InkGrowSeconds * 1.6)
                {
                    float gTn = saturate(inkAgeSec / _InkGrowSeconds);
                    float gTb = 1.0 - pow(1.0 - gTn, 4.0);   // 与旧 splat 写入端的 ease-out 铺开一致
                    float shrinkM = (1.0 - gTb) * _InkGrowShrinkMeters;
                    float gradMag = length(gradAmt);
                    if (shrinkM > 0.001 && gradMag > 0.0001)
                    {
                        // gradAmt 指向覆盖率增大方向（墨内部），向外偏移采样即得侵蚀后的覆盖
                        float2 erodeUV = saturate(inkUV - (gradAmt / gradMag) * (shrinkM / mapSize));
                        amtEff = min(amtEff, SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, erodeUV).a);
                    }
                }

                // 覆盖率边缘：图集内已有约 3 texel 的柔和过渡，这里只补一层屏幕空间抗锯齿
                float fw = fwidth(amtEff);
                float edgeWidth = clamp(fw * _EdgeSoftness, 0.012, 0.35);
                float inkMask = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, amtEff);

                // 2m 混凝土板缝仅作为无贴图时的兜底；烘焙贴图已经包含板缝/标线/AO，优先级更高。
                float2 gridCoord = abs(frac(input.positionWS.xz * 0.5) - 0.5);
                float gridLine = smoothstep(0.47, 0.495, max(gridCoord.x, gridCoord.y));

                half4 groundTex = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex, inkUV);
                half3 bakedGround = lerp(_BaseColor.rgb, groundTex.rgb, _GroundTexStrength);
                half3 groundCol = lerp(bakedGround, _GridLineColor.rgb, gridLine * 0.12);

                // 高度阶梯增强：高台/斜坡顶面略提亮，但不过度洗掉混凝土细节
                float heightFactor = saturate((input.positionWS.y - 0.05) / 1.05);
                half3 elevatedGroundCol = groundCol + heightFactor * half3(0.06, 0.07, 0.08);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float shadowAtten = mainLight.shadowAttenuation;
                float3 geomN = normalize(input.normalWS);

                // 朝上的面（地面/斜坡/高台顶面）才涂墨，垂直侧面不涂
                half upness = smoothstep(0.55, 0.85, geomN.y);
                inkMask *= upness;

                // 垂直侧沿（高台/斜坡裸露边）出现在高光台地四周，是玩家视线的焦点之一。
                // 只靠 `_CliffColor` 单独成色会读作"贴了一片深色板"，和地面脱节；
                // 这里让侧沿向"压暗一档的地面色"过渡 55%，结构上保持同一族材质，
                // 再配合下面的垂直面环境保底，避免背光侧沿被压成死黑。
                half3 cliffCol = lerp(_CliffColor.rgb, elevatedGroundCol * 0.62, 0.55);
                half3 baseSurfaceCol = lerp(cliffCol, elevatedGroundCol, upness);

                // -------------------------------------------------------------
                // 1. 边缘接触软阴影 (Contact Shadow)
                // -------------------------------------------------------------
                float contactHalo = smoothstep(0.04, 0.48, amtEff) * (1.0 - inkMask);
                float2 outN = -gradAmt / max(length(gradAmt), 0.001);
                float2 sunDirXZ = normalize(mainLight.direction.xz + float2(0.001, 0.001));
                float shadowBias = clamp(0.65 - 0.75 * dot(outN, sunDirXZ), 0.25, 1.35);
                float contactShadow = contactHalo * shadowBias * _ContactShadowStrength * upness;
                baseSurfaceCol *= (1.0 - contactShadow);

                // -------------------------------------------------------------
                // 2. 湿润度（G 通道时间戳片元现算，约 inkDryingSeconds 秒内线性烘干；
                //    不再有烘干 Pass，图集在墨变干的全过程保持不变）
                // -------------------------------------------------------------
                float wetness = saturate(wetLinear * _WetnessFactor);
                float fresh = smoothstep(0.2, 0.95, wetness);

                // -------------------------------------------------------------
                // 3. 厚度剖面与边缘鼓起 (Meniscus Lip)
                //    s: 边缘 0 -> 内部 1；hs: 圆滑高程轮廓
                // -------------------------------------------------------------
                float s = saturate((amtEff - 0.5) * _LipSoftness);
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

                    // 新墨水微骑在旧墨水上方：G 通道是「时间戳×覆盖率」（预乘），先反预乘再用环形差分
                    // 取「更新方向」，并按 周期/烘干秒数 换算回旧湿润度梯度的量纲，符号语义与旧实现一致。
                    float tsR = sampR.g / aR;
                    float tsL = sampL.g / aL;
                    float tsU = sampU.g / aU;
                    float tsD = sampD.g / aD;
                    float2 gradTs = float2(tsR - tsL, tsU - tsD) * 0.5;
                    gradTs = (frac(gradTs + 0.5) - 0.5) * 2.0;
                    float wetScale = max(0.001, _InkWetClockPeriod) / max(0.25, _InkWetDryingSeconds);
                    float2 gradWet = gradTs * wetScale;
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

                // 漫反射光照（三渲二色阶）
                half inkBand = P1ToonBand(NdotL, _InkShadeThreshold, _InkShadeSoftness);
                half inkShadow = P1ToonShadowTerm(shadowAtten, _InkShadowFloor, _InkShadeSoftness);
                float diffuseLight = lerp(0.72, 1.0, inkBand * inkShadow);

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
                half groundBand = P1ToonBand(dot(geomN, L), _ShadeThreshold, _ShadeSoftness);
                half groundShadow = P1ToonShadowTerm(shadowAtten, _ShadowFloor, _ShadeSoftness);
                half3 groundAmbient = SampleSH(geomN) * 0.35 + _ShadowTint.rgb * 0.18;
                half3 baseDiffuse = groundAmbient + lerp(_ShadowTint.rgb, half3(1,1,1), groundBand) * groundShadow * 0.85;
                // 垂直侧沿的环境保底：半球球谐在竖直法线方向本身取值很低，再叠上阴影项后
                // 高台/坡道的裸露侧面会塌成纯黑。按 (1 - upness) 补一层极弱的冷色环境光，
                // 让侧沿始终读作"深一号的混凝土"而不是一块黑边。
                baseDiffuse += (1.0 - upness) * _ShadowTint.rgb * 0.55;
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
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                float _InkShadeThreshold;
                float _InkShadeSoftness;
                float _InkShadowFloor;
                // 墨迹图集写入端（InkAtlasPaint / InkAtlasStamp Pass）使用的映射参数；
                // 加入同一 UnityPerMaterial 保证所有 Pass 布局一致（SRP Batcher 兼容）。
                // ForwardLit / ShadowCaster / DepthNormals 不读取它们，数值不影响场景渲染。
                float4 _InkMapSize;
                float _InkTexelMeters;
                float _InkYFlip;
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
                // 阴影 Pass 不做网点剔除：这里从光源视角渲染阴影贴图，
                // input.positionCS.xy 是阴影图像素坐标，而不是屏幕像素坐标。
                // 单级联/硬阴影（Android）下图案会随相机每帧重新拟合而滑动，
                // 角色身上就会看到乱闪的碎影。遮挡淡化只作用于颜色 Pass，
                // 阴影保持实体，物理上掩体仍然存在。
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
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
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                float _InkShadeThreshold;
                float _InkShadeSoftness;
                float _InkShadowFloor;
                // 墨迹图集写入端（InkAtlasPaint / InkAtlasStamp Pass）使用的映射参数；
                // 加入同一 UnityPerMaterial 保证所有 Pass 布局一致（SRP Batcher 兼容）。
                // ForwardLit / ShadowCaster / DepthNormals 不读取它们，数值不影响场景渲染。
                float4 _InkMapSize;
                float _InkTexelMeters;
                float _InkYFlip;
            CBUFFER_END

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                // 法线缓冲不做网点剔除：遮挡物在几何上仍然存在，保持实体法线
                // 可避免 PC 全屏描边在网点孔洞上误描黑边；Android 无此 Pass 消费者。
                #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
                #else
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }

        // =====================================================================
        // 墨迹图集写入端：由 InkSplatAtlas 通过 CommandBuffer 直接绘制进图集 RT。
        // 两个 Pass 使用自定义 LightMode，URP 主循环不认识这些 tag，
        // 永远不会把它们当场景几何执行；只会通过 CommandBuffer.DrawMesh(材质, passIndex) 调用。
        //
        // 时间驱动的外观演化全部「存时刻、片元现算」：
        //   InkAtlasPaint —— R=队伍 B=色调 A=覆盖率（混合与旧 P1/InkSplat Pass0 完全一致；不写 G）
        //   InkAtlasStamp —— G=最新落墨时间戳×覆盖率（预乘），Max 混合 → 覆盖同一纹素的最新墨斑获胜；
        //                    预乘保证双线性/mip 下片元 G/A 恒等于真实时间戳
        // 湿度烘干与生长动画由 ForwardLit 片元按 (_InkWetClockNorm − G/A) 现算，
        // 图集因此只在有新墨斑时增量绘制，没有任何周期性整图重绘（旧版为 15Hz 全屏烘干 Pass）。
        // =====================================================================
        Pass
        {
            Name "InkAtlasPaint"
            Tags { "LightMode" = "P1InkAtlasPaint" }
            ZTest Always
            ZWrite Off
            Cull Off
            BlendOp Add, Max
            Blend SrcAlpha OneMinusSrcAlpha, One One
            ColorMask RBA

            HLSLPROGRAM
            #pragma vertex P1InkAtlasVert
            #pragma fragment P1InkAtlasPaintFrag
            #pragma target 3.5

            // CBUFFER_START / CBUFFER_END 由 SRP Core 的平台 API 头定义，
            // 本 Pass 必须显式包含 Core.hlsl（否则 Metal/GLES3/Vulkan 报 unrecognized identifier 'CBUFFER_START'，
            // 写入 Pass 编译失败 → 地面完全没有墨迹）。Core.hlsl 带 include guard，后面的
            // InkSurfaceOverlay.hlsl 再次包含它不会重复定义。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与场景 Pass 完全一致的 UnityPerMaterial 布局（SRP Batcher 兼容）；
            // 本 Pass 实际只使用 _InkMapSize / _InkTexelMeters / _InkYFlip。
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
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                float _InkShadeThreshold;
                float _InkShadeSoftness;
                float _InkShadowFloor;
                float4 _InkMapSize;
                float _InkTexelMeters;
                float _InkYFlip;
            CBUFFER_END

            // 共享的有机墨斑 SDF 与顶点结构（additive 追加在 InkSurfaceOverlay.hlsl）。
            // P1InkAtlasVert 需要读 UnityPerMaterial 的 _InkMapSize/_InkYFlip，因此在各 Pass 内定义。
            #include "InkSurfaceOverlay.hlsl"

            P1InkAtlasVaryings P1InkAtlasVert(P1InkAtlasAttributes v)
            {
                P1InkAtlasVaryings o;
                float2 quad = v.positionOS.xy;
                float2 offsetMeters = quad * v.splatA.z * v.splatB.w;
                float2 uv = v.splatA.xy + offsetMeters / max(_InkMapSize.xy, float2(0.001, 0.001));
                float2 clipPos = uv * 2.0 - 1.0;
                // 渲染目标原点在左上时（D3D / Metal / Vulkan），clip Y 与纹理 V 反向
                clipPos.y *= _InkYFlip;
                o.positionCS = float4(clipPos, 0.0, 1.0);
                o.local = offsetMeters;
                o.splatA = v.splatA;
                o.splatB = v.splatB;
                return o;
            }

            float4 P1InkAtlasPaintFrag(P1InkAtlasVaryings input) : SV_Target
            {
                float radius = input.splatA.z;
                if (radius <= 0.0) discard;

                float sd = P1InkSplatDistance(input.local, radius, input.splatB.x, input.splatB.y, _InkTexelMeters);
                float a = P1InkSplatCoverage(sd, _InkTexelMeters);
                if (a <= 0.003) discard;

                float tone = P1InkSplatTone(input.splatB.x);
                // G 通道不写（ColorMask RBA）；落墨时间戳由 InkAtlasStamp Pass 负责
                return float4(input.splatA.w, 0.0, tone, a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InkAtlasStamp"
            Tags { "LightMode" = "P1InkAtlasStamp" }
            ZTest Always
            ZWrite Off
            Cull Off
            BlendOp Max, Add
            Blend One One, One One
            ColorMask G

            HLSLPROGRAM
            #pragma vertex P1InkAtlasVert
            #pragma fragment P1InkAtlasStampFrag
            #pragma target 3.5

            // 与 InkAtlasPaint 同上：CBUFFER_START 需要先包含 SRP Core 的平台 API 头。
            // 缺少这一行时 Stamp Pass 编译失败 → G 通道时间戳永远不写，湿润度/生长动画全部失效。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与场景 Pass 完全一致的 UnityPerMaterial 布局（SRP Batcher 兼容）；
            // 本 Pass 实际只使用 _InkMapSize / _InkTexelMeters / _InkYFlip。
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
                float _ShadeThreshold;
                float _ShadeSoftness;
                half4 _ShadowTint;
                float _ShadowFloor;
                float _InkShadeThreshold;
                float _InkShadeSoftness;
                float _InkShadowFloor;
                float4 _InkMapSize;
                float _InkTexelMeters;
                float _InkYFlip;
            CBUFFER_END

            #include "InkSurfaceOverlay.hlsl"

            P1InkAtlasVaryings P1InkAtlasVert(P1InkAtlasAttributes v)
            {
                P1InkAtlasVaryings o;
                float2 quad = v.positionOS.xy;
                float2 offsetMeters = quad * v.splatA.z * v.splatB.w;
                float2 uv = v.splatA.xy + offsetMeters / max(_InkMapSize.xy, float2(0.001, 0.001));
                float2 clipPos = uv * 2.0 - 1.0;
                // 渲染目标原点在左上时（D3D / Metal / Vulkan），clip Y 与纹理 V 反向
                clipPos.y *= _InkYFlip;
                o.positionCS = float4(clipPos, 0.0, 1.0);
                o.local = offsetMeters;
                o.splatA = v.splatA;
                o.splatB = v.splatB;
                return o;
            }

            float4 P1InkAtlasStampFrag(P1InkAtlasVaryings input) : SV_Target
            {
                float radius = input.splatA.z;
                if (radius <= 0.0) discard;

                // 与 InkAtlasPaint 逐位相同的 SDF/覆盖率 → 两个 Pass 足迹严格一致，
                // 时间戳只会写进真正有墨覆盖的纹素。
                float sd = P1InkSplatDistance(input.local, radius, input.splatB.x, input.splatB.y, _InkTexelMeters);
                float a = P1InkSplatCoverage(sd, _InkTexelMeters);
                if (a <= 0.003) discard;

                // splatB.z = 归一化落墨时间戳；预乘覆盖率后 Max 混合 = 覆盖同一纹素的最新墨斑获胜。
                // 预乘使双线性/mip 过滤下 G/A 恒等于真实时间戳（与 R/B 通道同一反预乘约定）。
                float stamp = input.splatB.z * a;
                return float4(stamp, stamp, stamp, a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
