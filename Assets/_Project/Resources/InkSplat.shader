Shader "P1/InkSplat"
{
    // 墨迹图集绘制着色器：把每个 splat 直接画进世界空间的 RGBA8 图集。
    //
    // 通道打包（与 inkwave-aah-research/src/world/paint.js 一致）：
    //   R = 队伍归属 (0 = A, 1 = B)      —— 覆盖式混合，新墨压旧墨
    //   G = 湿润度 (1 = 刚落墨)           —— 由 C# 侧定时的烘干 Pass 递减
    //   B = 每个 splat 的色调微差         —— 消除整片纯色的塑料感
    //   A = 覆盖率                        —— ~3 texel 平滑过渡，Max 混合，叠加幂等
    //
    // 混合：RGB 用 SrcAlpha/OneMinusSrcAlpha（预乘），A 用 Max（并集）。
    Properties
    {
        _InkMapSize ("Arena Size XZ", Vector) = (40, 40, 0, 0)
        _InkTexelMeters ("Atlas Texel Size", Float) = 0.009765625
        _InkYFlip ("Atlas Y Flip", Float) = 1
        _DryAmount ("Dry Amount Per Pass", Range(0, 0.25)) = 0.011
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        // ---------------------------------------------------------------- Pass 0: splat
        Pass
        {
            Name "InkSplat"
            ZTest Always
            ZWrite Off
            Cull Off
            BlendOp Add, Max
            Blend SrcAlpha OneMinusSrcAlpha, One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            float4 _InkMapSize;
            float _InkTexelMeters;
            float _InkYFlip;   // +1 = 纹理 UV 原点在左下；-1 = 渲染目标原点在左上（D3D/Metal/Vulkan）

            struct Attributes
            {
                float4 positionOS : POSITION;   // 单位四边面片 [-1,1]^2
                float4 splatA     : TEXCOORD0;  // xy = 中心图集 UV, z = 半径(米), w = 队伍(0/1)
                float4 splatB     : TEXCOORD1;  // x = 随机种子, y = 种类, z = 生长进度, w = 四边面覆盖倍数
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local      : TEXCOORD0;  // 距 splat 中心的米制偏移
                float4 splatA     : TEXCOORD1;
                float4 splatB     : TEXCOORD2;
            };

            float hsh(float n)
            {
                return frac(sin(n) * 43758.5453123);
            }

            // 有机构型：多阶正弦叠加 + 两根由高次余弦拉出的窄「手指」。
            // CPU 侧不参与该轮廓（玩法网格保持正圆），最大外扩 1.59R。
            float wob(float a, float s)
            {
                return 1.0
                    + 0.12 * sin(3.0 * a + s * 6.2831)
                    + 0.08 * sin(5.0 * a + s * 17.0)
                    + 0.05 * sin(7.0 * a + s * 41.0)
                    + 0.03 * sin(11.0 * a + s * 73.0)
                    + 0.018 * sin(17.0 * a + s * 29.0)
                    + 0.17 * pow(max(cos(a - s * 37.7), 0.0), 28.0)
                    + 0.12 * pow(max(cos(a - s * 53.3 - 2.1), 0.0), 36.0);
            }

            float smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // 由 a(半径 ra) 指向 b(半径 rb) 的锥形射线
            float sdRay(float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
                return length(pa - ba * h) - lerp(ra, rb, h);
            }

            // 每种墨斑的装饰件数量：x = 射线, y = 卫星墨滴, z = 细雾
            float4 kindShape(float k)
            {
                if (k < 0.5) return float4(2.0, 3.0, 0.0, 0.0);    // 小墨滴
                if (k < 1.5) return float4(3.0, 5.0, 5.0, 0.0);    // 拖尾滴墨
                if (k < 2.5) return float4(5.0, 7.0, 8.0, 0.0);    // 单发喷点
                if (k < 3.5) return float4(7.0, 9.0, 10.0, 0.0);   // 爆斑
                return float4(10.0, 12.0, 14.0, 0.0);              // 炸弹 / 大招
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float2 quad = v.positionOS.xy;
                float2 offsetMeters = quad * v.splatA.z * v.splatB.w;
                float2 uv = v.splatA.xy + offsetMeters / max(_InkMapSize.xy, float2(0.001, 0.001));
                float2 clip = uv * 2.0 - 1.0;
                // 渲染目标原点在左上时（D3D / Metal / Vulkan），clip Y 与纹理 V 反向
                clip.y *= _InkYFlip;
                o.positionCS = float4(clip, 0.0, 1.0);
                o.local = offsetMeters;
                o.splatA = v.splatA;
                o.splatB = v.splatB;
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float R = input.splatA.z;
                float team = input.splatA.w;
                float seed = input.splatB.x;
                float kind = input.splatB.y;
                float tn = input.splatB.z;
                float2 p0 = input.local;

                if (R <= 0.0) discard;

                // 主体从 40% 半径快速铺开（ease-out），结束半径即玩法网格半径
                float tb = 1.0 - pow(1.0 - saturate(tn), 4.0);
                float grow = lerp(0.4, 1.0, tb);
                float r = R * grow;

                float4 ks = kindShape(kind);
                float sd = length(p0) - r * wob(atan2(p0.y, p0.x), seed);

                // ---- 射线：短锥形墨刺，末端收成一颗小墨珠
                float tsp = 1.0 - pow(1.0 - saturate(tn * 1.4), 3.0);
                bool big = kind > 2.5;
                [loop]
                for (int i = 0; i < 10; i++)
                {
                    float fi = (float)i;
                    if (fi >= ks.x) break;
                    float h1 = hsh(seed * 7.31 + fi * 1.93);
                    float h2 = hsh(seed * 3.17 + fi * 5.71);
                    float h3 = hsh(seed * 11.3 + fi * 2.39);
                    float a = (fi + 0.35 + 0.6 * h1) / ks.x * 6.2831 + seed * 6.2831;
                    float2 u = float2(cos(a), sin(a));
                    float edge = r * wob(a, seed);
                    float len = r * (0.07 + (big ? 0.5 : 0.34) * h2 * h2 * h2) * tsp;
                    float wB = r * (0.055 + 0.06 * h3);
                    float tipR = max(r * (0.012 + 0.012 * h3), _InkTexelMeters * 0.45);
                    float2 tip = u * (edge + len) + float2(-u.y, u.x) * len * 0.18 * (h1 - 0.5);
                    float ray = min(sdRay(p0, u * edge * 0.72, tip, wB, tipR),
                                    length(p0 - tip) - tipR * (1.6 + 1.4 * h2));
                    sd = smin(sd, ray, r * 0.06);
                }

                // ---- 卫星墨滴：稍晚落地，被飞行方向拉长
                [loop]
                for (int j = 0; j < 12; j++)
                {
                    float fj = (float)j;
                    if (fj >= ks.y) break;
                    float h1 = hsh(seed * 13.1 + fj * 7.7);
                    float h2 = hsh(seed * 5.3 + fj * 3.1);
                    float h3 = hsh(seed * 9.9 + fj * 1.7);
                    float tl = 0.28 + 0.95 * h2;
                    float land = smoothstep(tl, tl + 0.2, tn);
                    if (land <= 0.0) continue;
                    float a2 = h1 * 6.2831;
                    float2 u = float2(cos(a2), sin(a2));
                    float dist = r * (1.1 + (big ? 1.05 : 0.8) * h2 * h2);
                    float rad = r * (0.028 + 0.085 * h3) * (1.0 - 0.4 * h2) * (big ? 0.8 : 1.0) * land;
                    float2 q = p0 - u * dist;
                    sd = smin(sd, length(q) - rad, rad * 0.8);
                }

                // ---- 细雾：比玩法网格更细的妆点，永远不计入涂地
                [loop]
                for (int k = 0; k < 14; k++)
                {
                    float fk = (float)k;
                    if (fk >= ks.z) break;
                    float h1 = hsh(seed * 17.9 + fk * 4.13);
                    float h2 = hsh(seed * 2.71 + fk * 8.09);
                    float h3 = hsh(seed * 6.47 + fk * 3.37);
                    if (tn < 0.45 + 0.95 * h2) continue;
                    float a3 = h1 * 6.2831;
                    float2 u = float2(cos(a3), sin(a3));
                    float rad = max(r * (0.011 + 0.02 * h3), _InkTexelMeters * 0.9);
                    sd = min(sd, length(p0 - u * r * (1.3 + 1.2 * h2)) - rad);
                }

                // 至少 1.5 texel 的柔和边缘，保证任何缩放下都不出现硬阶梯
                float fw = max(fwidth(sd), _InkTexelMeters * 0.6);
                float a = 1.0 - smoothstep(-1.5 * fw, 1.5 * fw, sd);
                if (a <= 0.003) discard;

                float tone = hsh(seed * 1.73);
                return float4(team, 1.0, tone, a);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Pass 1: 烘干
        // 整张图集按固定步长从 G 通道减去湿润度；RGB 用 RevSub（dst - src），A 原样保留。
        Pass
        {
            Name "InkDry"
            ZTest Always
            ZWrite Off
            Cull Off
            BlendOp RevSub, Add
            Blend One One, Zero One

            HLSLPROGRAM
            #pragma vertex vertDry
            #pragma fragment fragDry
            #pragma target 3.0

            float _DryAmount;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct VaryingsDry
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsDry vertDry(Attributes v)
            {
                VaryingsDry o;
                o.positionCS = float4(v.positionOS.xy, 0.0, 1.0);
                return o;
            }

            float4 fragDry(VaryingsDry input) : SV_Target
            {
                return float4(0.0, _DryAmount, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
