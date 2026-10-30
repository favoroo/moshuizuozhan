#ifndef P1_INK_SURFACE_OVERLAY_INCLUDED
#define P1_INK_SURFACE_OVERLAY_INCLUDED

// =============================================================================
// InkSurfaceOverlay.hlsl
// 场景掩体、道具与高台顶面墨水图集采样与toon叠加
// 当几何体表面法线朝上（Ny > 0.28）时，采样顶视墨水图集并将其混合到物体原色上。
// =============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "ToonLighting.hlsl"

// 全局由 InkSurfaceController 驱动的墨水图集与参数
TEXTURE2D(_GlobalInkAtlasTex);

float4 _GlobalInkMapOriginXZ;
float4 _GlobalInkTexelSize;
half4  _GlobalTeamAColor;
half4  _GlobalTeamBColor;
float  _GlobalWetnessFactor;
// 胶质内发光强度：与 Mat_InkGround._InkGlow 对应，由 InkSurfaceController 统一推送
float  _GlobalInkGlow;

// 湿润时间戳时钟（图集 G 通道新语义：G = 覆盖此纹素最新墨斑的「归一化落墨时刻 × 覆盖率」预乘值，
// 片元反预乘后按 (_InkWetClockNorm − G/A) 现算湿润度）。
// 与 InkGround.shader ForwardLit / InkSurfaceController / InkSplatAtlas 共用同一组全局与周期配置。
float  _InkWetClockNorm;      // (Time.time % 周期) / 周期 ∈ [0,1)
float  _InkWetClockPeriod;    // 时间戳回绕周期（秒）
float  _InkWetDryingSeconds;  // inkDryingSeconds：湿润→烘干时长

// 按时间戳现算湿润度（与 InkGround ForwardLit 同一公式）：
// age = (now − 落墨时间戳) mod 周期，线性烘干曲线与旧整图烘干 Pass 数学等价。
float P1InkTimestampWetness(float birthTs01, float wetnessFactor)
{
    float ageSec = frac(_InkWetClockNorm - birthTs01 + 1.0) * max(0.001, _InkWetClockPeriod);
    float wetLinear = saturate(1.0 - ageSec / max(0.25, _InkWetDryingSeconds));
    return saturate(wetLinear * wetnessFactor);
}

// =============================================================================
// [additive] 墨迹图集写入端共享代码（InkGround.shader 的 InkAtlasPaint / InkAtlasStamp Pass 使用）
//
// 数学与旧 Assets/_Project/Resources/InkSplat.shader Pass0 完全一致，但取「生长时间 tn 饱和(=1.6)」
// 的最终形态：墨斑落墨瞬间即以最终轮廓写入图集，生长动画由地面片元按时间戳做形态学侵蚀。
// 注意：本段不引用任何 cbuffer 成员（inkTexelMeters 由调用方传入），
// 因此被 InkWall / PropLit / ObstacleDitherLit include 时也不会产生未声明标识符；
// 对既有消费方为纯增量，不改变任何既有函数。
// =============================================================================

float P1InkSplatHsh(float n)
{
    return frac(sin(n) * 43758.5453123);
}

// 有机构型：多阶正弦叠加 + 两根由高次余弦拉出的窄「手指」。
// CPU 侧不参与该轮廓（玩法网格保持正圆），最大外扩 1.59R。
float P1InkSplatWob(float a, float s)
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

float P1InkSplatSmin(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// 由 a(半径 ra) 指向 b(半径 rb) 的锥形射线
float P1InkSplatSdRay(float2 p, float2 a, float2 b, float ra, float rb)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
    return length(pa - ba * h) - lerp(ra, rb, h);
}

// 每种墨斑的装饰件数量：x = 射线, y = 卫星墨滴, z = 细雾
float4 P1InkSplatKindShape(float k)
{
    if (k < 0.5) return float4(2.0, 3.0, 0.0, 0.0);    // 小墨滴
    if (k < 1.5) return float4(3.0, 5.0, 5.0, 0.0);    // 拖尾滴墨
    if (k < 2.5) return float4(5.0, 7.0, 8.0, 0.0);    // 单发喷点
    if (k < 3.5) return float4(7.0, 9.0, 10.0, 0.0);   // 爆斑
    return float4(10.0, 12.0, 14.0, 0.0);              // 炸弹 / 大招
}

// 墨斑最终形态的 SDF（等价旧实现 tn=1.6 饱和态：主体满半径、射线全长、卫星全落、细雾全出）
// inkTexelMeters = 图集纹素边长（米），由调用方从 UnityPerMaterial._InkTexelMeters 传入。
float P1InkSplatDistance(float2 p0, float radius, float seed, float kind, float inkTexelMeters)
{
    float r = radius;
    float4 ks = P1InkSplatKindShape(kind);
    bool big = kind > 2.5;
    float sd = length(p0) - r * P1InkSplatWob(atan2(p0.y, p0.x), seed);

    // ---- 射线：短锥形墨刺，末端收成一颗小墨珠（全长）----
    [loop]
    for (int i = 0; i < 10; i++)
    {
        float fi = (float)i;
        if (fi >= ks.x) break;
        float h1 = P1InkSplatHsh(seed * 7.31 + fi * 1.93);
        float h2 = P1InkSplatHsh(seed * 3.17 + fi * 5.71);
        float h3 = P1InkSplatHsh(seed * 11.3 + fi * 2.39);
        float a = (fi + 0.35 + 0.6 * h1) / ks.x * 6.2831 + seed * 6.2831;
        float2 u = float2(cos(a), sin(a));
        float edge = r * P1InkSplatWob(a, seed);
        float len = r * (0.07 + (big ? 0.5 : 0.34) * h2 * h2 * h2);
        float wB = r * (0.055 + 0.06 * h3);
        float tipR = max(r * (0.012 + 0.012 * h3), inkTexelMeters * 0.45);
        float2 tip = u * (edge + len) + float2(-u.y, u.x) * len * 0.18 * (h1 - 0.5);
        float ray = min(P1InkSplatSdRay(p0, u * edge * 0.72, tip, wB, tipR),
                        length(p0 - tip) - tipR * (1.6 + 1.4 * h2));
        sd = P1InkSplatSmin(sd, ray, r * 0.06);
    }

    // ---- 卫星墨滴：被飞行方向拉长（全部落地）----
    [loop]
    for (int j = 0; j < 12; j++)
    {
        float fj = (float)j;
        if (fj >= ks.y) break;
        float h1 = P1InkSplatHsh(seed * 13.1 + fj * 7.7);
        float h2 = P1InkSplatHsh(seed * 5.3 + fj * 3.1);
        float h3 = P1InkSplatHsh(seed * 9.9 + fj * 1.7);
        float a2 = h1 * 6.2831;
        float2 u = float2(cos(a2), sin(a2));
        float dist = r * (1.1 + (big ? 1.05 : 0.8) * h2 * h2);
        float rad = r * (0.028 + 0.085 * h3) * (1.0 - 0.4 * h2) * (big ? 0.8 : 1.0);
        float2 q = p0 - u * dist;
        sd = P1InkSplatSmin(sd, length(q) - rad, rad * 0.8);
    }

    // ---- 细雾：比玩法网格更细的妆点，永远不计入涂地（全部出现）----
    [loop]
    for (int k = 0; k < 14; k++)
    {
        float fk = (float)k;
        if (fk >= ks.z) break;
        float h1 = P1InkSplatHsh(seed * 17.9 + fk * 4.13);
        float h2 = P1InkSplatHsh(seed * 2.71 + fk * 8.09);
        float h3 = P1InkSplatHsh(seed * 6.47 + fk * 3.37);
        float a3 = h1 * 6.2831;
        float2 u = float2(cos(a3), sin(a3));
        float rad = max(r * (0.011 + 0.02 * h3), inkTexelMeters * 0.9);
        sd = min(sd, length(p0 - u * r * (1.3 + 1.2 * h2)) - rad);
    }
    return sd;
}

// 与旧实现一致的低 alpha 剔除阈值 + 至少 1.5 texel 的柔和边缘。
// Paint / Stamp 两个 Pass 用相同输入调用即可得到逐位一致的覆盖率 → 足迹严格对齐。
float P1InkSplatCoverage(float sd, float inkTexelMeters)
{
    float fw = max(fwidth(sd), inkTexelMeters * 0.6);
    return 1.0 - smoothstep(-1.5 * fw, 1.5 * fw, sd);
}

float P1InkSplatTone(float seed)
{
    return P1InkSplatHsh(seed * 1.73);
}

// 图集写入端共享顶点结构（与旧 InkSplat Pass0 布局一致）。
// 顶点入口 P1InkAtlasVert 放在 InkGround.shader 的两个写入 Pass 内：
// 它要读 UnityPerMaterial 的 _InkMapSize/_InkYFlip，不能放进这个被多方 include 的文件。
struct P1InkAtlasAttributes
{
    float4 positionOS : POSITION;   // 单位四边面片 [-1,1]^2
    float4 splatA     : TEXCOORD0;  // xy = 中心图集 UV, z = 半径(米), w = 队伍(0/1)
    float4 splatB     : TEXCOORD1;  // x = 随机种子, y = 种类, z = 归一化落墨时间戳, w = 四边面覆盖倍数
};

struct P1InkAtlasVaryings
{
    float4 positionCS : SV_POSITION;
    float2 local      : TEXCOORD0;  // 距 splat 中心的米制偏移
    float4 splatA     : TEXCOORD1;
    float4 splatB     : TEXCOORD2;
};

/// <summary>
/// 对朝上表面采样并混合顶视墨迹（集装箱顶面、木箱顶面、护栏顶面、花坛顶面、各类近景道具）。
/// 配色管线与 InkGround.shader 的墨水段逐项对齐（色调 / 湿度 / 阴影底线 / 胶质内发光 / 清漆高光），
/// 保证同一片墨水落在顶面与落在地面时观感一致。
/// </summary>
bool ApplyTopInkOverlay(
    inout half3 surfaceColor,
    float3 positionWS,
    float3 normalWS,
    half shadowAtten,
    float3 mainLightDir,
    float3 viewDirWS)
{
    // 仅在朝上的面绘制顶视图形（避免垂直立面拉伸，立面由 InkWallSurface 负责）
    half upness = smoothstep(0.28, 0.68, normalWS.y);
    if (upness <= 0.002)
        return false;

    float2 mapOrigin = _GlobalInkMapOriginXZ.xy;
    float2 mapSize = max(float2(0.001, 0.001), _GlobalInkMapOriginXZ.zw);
    float2 inkUV = saturate((positionWS.xz - mapOrigin) / mapSize);

    float4 inkSample = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, inkUV);
    float amt = inkSample.a;
    if (amt < 0.02)
        return false;

    float invA = 1.0 / max(amt, 0.02);
    float tRaw = saturate(inkSample.r * invA);
    // G 通道 = 最新落墨时间戳 × 覆盖率（预乘，Max 混合写入），反预乘后用与地面相同的时间戳公式现算湿润度
    float inkTsNewest = saturate(inkSample.g * invA);
    float tone = saturate(inkSample.b * invA);

    // 屏幕空间边缘抗锯齿
    float fw = fwidth(amt);

    // 顶面只铺「实墨」：地面墨斑的装饰性外缘（射线/卫星墨滴/细雾的半覆盖区）
    // 投到木箱、集装箱的暖色底上会混成土黄（地面是中性灰底所以没有这个问题），
    // 因此可见边界从地面的 amt=0.5 收紧到 0.55~0.72：0.72 以上为完全实色，
    // 半覆盖雾区在顶面不渲染，只留一条窄的有机墨边。
    float maskEdge = clamp(fw * 1.2, 0.01, 0.06);
    float inkMask = smoothstep(0.55 - maskEdge, 0.72 + maskEdge, amt) * upness;
    if (inkMask <= 0.005)
        return false;

    // ---- 以下与 InkGround.shader 墨水段同参 ----

    // 队伍交界 1px 锐利过渡，绝不出现浑浊混色带
    float wT = clamp(fwidth(tRaw) * 0.75, 0.012, 0.25);
    float tm = smoothstep(0.5 - wT, 0.5 + wT, tRaw);
    half3 teamCol = lerp(_GlobalTeamAColor.rgb, _GlobalTeamBColor.rgb, tm);

    // 湿度：与地面同一片元现算曲线（G 通道时间戳 → age，约 inkDryingSeconds 秒内线性烘干）
    float wetness = P1InkTimestampWetness(inkTsNewest, _GlobalWetnessFactor);
    float fresh = smoothstep(0.2, 0.95, wetness);

    // 单斑色调微差 + 厚度剖面（地面 _ToneStrength=1 / _LipSoftness=1.7）：
    // 薄缘半透明略亮更鲜活，厚体略深
    half3 inkColor = teamCol * (0.93 + 0.13 * tone);
    float s = saturate((amt - 0.5) * 1.7);
    float hs = 1.0 - (1.0 - s) * (1.0 - s);
    inkColor = lerp(inkColor, inkColor * 1.16 + teamCol * 0.05, (1.0 - hs) * 0.35);
    inkColor *= 1.0 - 0.05 * hs;
    // 新鲜湿润状态轻微提亮与增加通透度
    inkColor *= 1.0 + 0.07 * fresh * fresh;

    // 漫反射光照（与地面 _InkShadeThreshold=0.48 / _InkShadowFloor=0.72 一致）
    float3 L = normalize(mainLightDir);
    float3 V = normalize(viewDirWS);
    float NdotL = saturate(dot(normalWS, L));
    half inkBand = P1ToonBand(NdotL, 0.48, 0.08);
    half inkShadow = P1ToonShadowTerm(shadowAtten, 0.72, 0.08);
    float diffuseLight = lerp(0.72, 1.0, inkBand * inkShadow);

    // 胶质内发光：地面靠该项保证墨色在背光/暗部也鲜艳通透，顶面缺它就会整体发灰发暗
    half3 litInkColor = inkColor * diffuseLight
                      + inkColor * _GlobalInkGlow * (0.75 + 0.35 * hs) * (1.0 - NdotL * 0.5);

    // 清漆高光：复刻地面 clearcoat（_WetGloss=0.88 / _DryGloss=0.52 / _ClearcoatStrength=1.4）
    float3 H = normalize(L + V);
    float NdotV = saturate(dot(normalWS, V));
    float NdotH = saturate(dot(normalWS, H));
    float smoothness = lerp(0.52, 0.88, fresh);
    float specPower = exp2(10.0 * smoothness + 1.0);
    float specNorm = (specPower + 8.0) / 8.0;
    float directSpec = pow(NdotH, specPower) * specNorm * (0.04 + 0.08 * fresh);
    float fresnel = pow(1.0 - NdotV, 3.5);
    float clearcoatF0 = lerp(0.035, 0.065, fresh);
    float F = clearcoatF0 + (0.35 - clearcoatF0) * fresnel;
    litInkColor += directSpec * F * 1.4 * NdotL;

    surfaceColor = lerp(surfaceColor, litInkColor, inkMask);
    return true;
}

#endif
