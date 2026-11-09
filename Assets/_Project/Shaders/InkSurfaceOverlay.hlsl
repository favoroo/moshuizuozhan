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
#include "InkWetShading.hlsl"

// 全局由 InkSurfaceController 驱动的墨水图集与参数
TEXTURE2D(_GlobalInkAtlasTex);

float4 _GlobalInkMapOriginXZ;
float4 _GlobalInkTexelSize;
// 计分场地矩形 (minX, minZ, sizeX, sizeZ)：由 InkSurfaceController 推送。
// 场地之外（场外码头、远景堆场）虽然也使用带顶面墨迹的着色器，但不显墨。
float4 _GlobalInkPlayableRect;
// 计分掩码（R8，0=可涂 / 255=不可涂）：由 InkSurfaceController 按计分网格生成。
// 道具顶面与脚下地面共用同一个 XZ 格，网格把道具占位格判为不可涂；
// 顶面墨迹必须同步裁掉，否则会出现「涂了但不计分」的假反馈。
TEXTURE2D(_GlobalInkScoreMaskTex);
float _GlobalInkScoreMaskEnabled;
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
float  _InkSheenSeconds;      // 缎面余晖时长（秒，默认 22）

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
    if (k < 4.5) return float4(10.0, 12.0, 14.0, 0.0); // 炸弹 / 大招
    if (k < 5.5) return float4(0.0, 0.0, 0.0, 0.0);    // 平滑尾迹 (InkSplatAtlas.PlainKind)：无射线 / 卫星
    return float4(10.0, 12.0, 14.0, 0.0);              // 兜底
}

// ---- 方向性拉伸（A1）：逐墨斑沿喷射方向做各向异性塑形 ----
// 全局倍率（InkGridConfigSO.splatDirectionalStretch，由 InkSurfaceController 每帧 SetGlobalFloat 推送）。
// 逐墨斑强度先乘它再 saturate 到 0..1：0 = 关闭（所有墨斑恒为圆盘 + wobble，与旧版逐位一致）。
float _InkSplatDirStretch;

#define P1_INK_STRETCH_LONG_FWD  0.60   // 前沿（喷射方向）拉长比例：满强度 +60%
#define P1_INK_STRETCH_LONG_BACK 0.15   // 后沿拉长比例：只轻微拖尾，避免读成"前后对称的椭圆"
#define P1_INK_STRETCH_LAT_COMP  0.15   // 横向压扁比例：满强度 -15%，让拉长读作"被喷出去"而不是整体放大
#define P1_INK_STRETCH_CONE      2.5    // 满强度时装饰件收进 ±1.25rad（≈±72°）的前向锥

// 把「距墨斑中心的世界 XZ 米制偏移」映射进拉伸后的 SDF 空间：
// 沿 dir 的分量除以拉长系数、垂直分量除以横向压扁系数 → 在该空间求值得到的等值面
// 就是「沿 dir 拉长、横向压扁」的墨斑（与 INKWAVE paint.js 的 smear 同构：前沿拉得多、后沿拉得少）。
float2 P1InkSplatStretchLocal(float2 p0, float2 dir, float stretch01)
{
    float along = dot(p0, dir);
    float2 perp = p0 - dir * along;
    float sLong = along >= 0.0
        ? 1.0 + P1_INK_STRETCH_LONG_FWD * stretch01
        : 1.0 + P1_INK_STRETCH_LONG_BACK * stretch01;
    float sLat = 1.0 - P1_INK_STRETCH_LAT_COMP * stretch01;
    return dir * (along / sLong) + perp / sLat;
}

// 墨斑最终形态的 SDF（等价旧实现 tn=1.6 饱和态：主体满半径、射线全长、卫星全落、细雾全出）
// inkTexelMeters = 图集纹素边长（米），由调用方从 UnityPerMaterial._InkTexelMeters 传入。
// stretchDir = 单位拉伸方向（世界 XZ），stretch01 = 0..1 拉伸强度（已乘全局倍率并 clamp）。
//   stretch01 = 0 时 p 原样使用、射线/卫星/细雾角度沿用旧公式 → 与旧实现逐位一致（回归安全）。
// tn = 归一化生长时间（落墨后 age / 扩散时长；3 = 最终形态，与旧实现逐位一致）。
// 时间编排对齐 INKWAVE paint.js PAINT_FS：
//   主体从 40% 半径 ease-out 泛出（1-pow(1-tn,4)），射线先于主体冲满（tn*1.4），
//   卫星墨滴各自错峰落地（tl = 0.28+0.95*h2，越远越晚），细雾最后出现。
float P1InkSplatDistanceDirGrow(float2 p0, float radius, float seed, float kind, float inkTexelMeters,
                                float2 stretchDir, float stretch01, float tn)
{
    float r = radius;
    float4 ks = P1InkSplatKindShape(kind);
    bool big = kind > 2.5;
    bool stretched = stretch01 > 0.0001;

    // 主体与所有装饰件都在拉伸后的局部空间求值：一次坐标变换就把整枚墨斑拉长压扁
    //（decorated = false 的平滑尾迹同样走这里，所以主体也会拉伸）。
    float2 p = p0;
    if (stretched)
        p = P1InkSplatStretchLocal(p0, stretchDir, stretch01);

    // 拉伸时装饰件不再整圈均匀铺开，而是收进以喷射方向为轴的前向锥（读作"顺着弹道溅出去"）。
    float dirAngle = 0.0;
    float spread = 6.2831853;
    if (stretched)
    {
        dirAngle = atan2(stretchDir.y, stretchDir.x);
        spread = lerp(6.2831853, P1_INK_STRETCH_CONE, saturate(stretch01 * 1.2));
    }

    // ---- 主体：从 40% 半径强 ease-out 泛出（tn=3 时 grow=1，与旧最终形态逐位一致）----
    float bodyGrow = lerp(0.4, 1.0, 1.0 - pow(1.0 - saturate(tn), 4.0));
    float sd = length(p) - r * bodyGrow * P1InkSplatWob(atan2(p.y, p.x), seed);

    // ---- 射线：短锥形墨刺，末端收成一颗小墨珠（先于主体冲满全长）----
    float rayGrow = 1.0 - pow(1.0 - saturate(tn * 1.4), 3.0);
    [loop]
    for (int i = 0; i < 10; i++)
    {
        float fi = (float)i;
        if (fi >= ks.x) break;
        float h1 = P1InkSplatHsh(seed * 7.31 + fi * 1.93);
        float h2 = P1InkSplatHsh(seed * 3.17 + fi * 5.71);
        float h3 = P1InkSplatHsh(seed * 11.3 + fi * 2.39);
        float a = stretched
            ? dirAngle + (h1 - 0.5) * spread
            : (fi + 0.35 + 0.6 * h1) / ks.x * 6.2831 + seed * 6.2831;
        float2 u = float2(cos(a), sin(a));
        float edge = r * P1InkSplatWob(a, seed);
        float len = r * (0.07 + (big ? 0.5 : 0.34) * h2 * h2 * h2) * rayGrow;
        float wB = r * (0.055 + 0.06 * h3);
        float tipR = max(r * (0.012 + 0.012 * h3), inkTexelMeters * 0.45);
        float2 tip = u * (edge + len) + float2(-u.y, u.x) * len * 0.18 * (h1 - 0.5);
        float ray = min(P1InkSplatSdRay(p, u * edge * 0.72, tip, wB, tipR),
                        length(p - tip) - tipR * (1.6 + 1.4 * h2));
        sd = P1InkSplatSmin(sd, ray, r * 0.06);
    }

    // ---- 卫星墨滴：被飞行方向拉长，各自错峰落地（越远越晚）----
    [loop]
    for (int j = 0; j < 12; j++)
    {
        float fj = (float)j;
        if (fj >= ks.y) break;
        float h1 = P1InkSplatHsh(seed * 13.1 + fj * 7.7);
        float h2 = P1InkSplatHsh(seed * 5.3 + fj * 3.1);
        float h3 = P1InkSplatHsh(seed * 9.9 + fj * 1.7);
        // 每颗卫星有自己的落地时刻：h2 同时驱动距离（越远）与时刻（越晚）→ 远者后落
        float landT = 0.28 + 0.95 * h2;
        float land = smoothstep(landT, landT + 0.2, tn);
        if (land <= 0.0) continue;
        // 拉伸时卫星滴与前向锥对齐（不再绕整圈），并沿各自飞行线拉成短 streak
        float a2 = stretched ? dirAngle + (h1 - 0.5) * spread : h1 * 6.2831;
        float2 u = float2(cos(a2), sin(a2));
        float dist = r * (1.1 + (big ? 1.05 : 0.8) * h2 * h2);
        float rad = r * (0.028 + 0.085 * h3) * (1.0 - 0.4 * h2) * (big ? 0.8 : 1.0) * land;
        float2 q = p - u * dist;
        if (stretched)
        {
            // 距离不变，只把墨滴本体沿 u 拉长（1/el 压缩 q 的 u 分量 = 形状沿 u 放大 el 倍）
            float el = 1.0 + (0.5 + 1.6 * stretch01) * h2;
            q -= u * dot(q, u) * (1.0 - 1.0 / el);
        }
        sd = P1InkSplatSmin(sd, length(q) - rad, rad * 0.8);
    }

    // ---- 细雾：比玩法网格更细的妆点，永远不计入涂地（最后出现）----
    [loop]
    for (int k = 0; k < 14; k++)
    {
        float fk = (float)k;
        if (fk >= ks.z) break;
        float h1 = P1InkSplatHsh(seed * 17.9 + fk * 4.13);
        float h2 = P1InkSplatHsh(seed * 2.71 + fk * 8.09);
        float h3 = P1InkSplatHsh(seed * 6.47 + fk * 3.37);
        if (tn < 0.45 + 0.95 * h2) continue;
        float a3 = stretched ? dirAngle + (h1 - 0.5) * spread * 1.15 : h1 * 6.2831;
        float2 u = float2(cos(a3), sin(a3));
        float rad = max(r * (0.011 + 0.02 * h3), inkTexelMeters * 0.9);
        sd = min(sd, length(p - u * r * (1.3 + 1.2 * h2)) - rad);
    }
    return sd;
}

// 旧签名（无生长时间）：tn=3 等价于旧最终形态，算术路径逐位一致（回归安全）。
float P1InkSplatDistanceDir(float2 p0, float radius, float seed, float kind, float inkTexelMeters,
                            float2 stretchDir, float stretch01)
{
    return P1InkSplatDistanceDirGrow(p0, radius, seed, kind, inkTexelMeters, stretchDir, stretch01, 3.0);
}

// 旧签名（无方向性拉伸）：等价于 stretch = 0 的新实现，内部走与旧版完全相同的算术路径。
float P1InkSplatDistance(float2 p0, float radius, float seed, float kind, float inkTexelMeters)
{
    return P1InkSplatDistanceDir(p0, radius, seed, kind, inkTexelMeters, float2(1.0, 0.0), 0.0);
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
    float2 splatDir   : TEXCOORD2;  // 单位拉伸方向 × 强度（世界 XZ，长度 0 = 不拉伸；见 P1InkSplatDistanceDir）
    float4 splatGrow  : TEXCOORD3;  // x = 归一化生长时间 tn（3 = 最终形态，与旧实现逐位一致）
};

struct P1InkAtlasVaryings
{
    float4 positionCS : SV_POSITION;
    float2 local      : TEXCOORD0;  // 距 splat 中心的米制偏移
    float4 splatA     : TEXCOORD1;
    float4 splatB     : TEXCOORD2;
    float2 splatDir   : TEXCOORD3;  // 同 P1InkAtlasAttributes.splatDir（四顶点同值，插值恒等）
    float4 splatGrow  : TEXCOORD4;  // 同 P1InkAtlasAttributes.splatGrow（四顶点同值，插值恒等）
};

// 解包顶点携带的 (方向 × 强度)：返回单位方向 + 已乘全局倍率并 clamp 到 0..1 的强度。
// 零向量（不拉伸）时方向回退 (1,0)、强度 0，调用方据此走无拉伸分支。
void P1InkSplatDecodeStretch(float2 packedStretch, out float2 dir, out float stretch01)
{
    float len = length(packedStretch);
    stretch01 = saturate(min(len, 1.0) * max(0.0, _InkSplatDirStretch));
    dir = len > 1e-5 ? packedStretch / len : float2(1.0, 0.0);
}

// 方向性拉伸的四边形包络（A1）：把单位面片 [-1,1]^2 的半幅扩到能包住拉长后的主体与装饰件。
// 返回 (1,1) 表示无需扩面片 —— 调用方此时必须保持旧的 offsetMeters 表达式不变（逐位回归安全）。
// 用椭圆支撑函数 sqrt((sLong·dir)² + (sLat·perp)²) 求世界 X/Z 轴上的最大投影，再 clamp ≥ 1：
// 只放大不缩小，避免极端方向下把卫星滴/射线裁在面片外。
float2 P1InkSplatQuadEnvelope(float2 packedStretch)
{
    float2 env = float2(1.0, 1.0);
    float2 dir;
    float stretch01;
    P1InkSplatDecodeStretch(packedStretch, dir, stretch01);
    if (stretch01 <= 0.0001)
        return env;

    float sLong = 1.0 + P1_INK_STRETCH_LONG_FWD * stretch01;
    float sLat = 1.0 - P1_INK_STRETCH_LAT_COMP * stretch01;
    float alongX = sLong * dir.x;
    float alongY = sLong * dir.y;
    float latX = sLat * dir.y;   // perp = (-dir.y, dir.x)
    float latY = sLat * dir.x;
    env.x = sqrt(alongX * alongX + latX * latX);
    env.y = sqrt(alongY * alongY + latY * latY);
    return max(env, float2(1.0, 1.0));
}

static float _P1TopInkContactShadowTerm = 0.0;

/// <summary>
/// 顶面/物体表面墨水完整物理求值函数（P18 画风重做）：
/// 包含：队伍配色 + 三段湿润 (wet, sheen) + 胶质厚度 + 三渲二漫反射 (重定标 0.40)
///       + 胶质内发光 + 双层清漆高光 + 环境镜面 P1InkEnvSpecularFull + 顶面 lip 法线扰动。
/// </summary>
bool P1EvaluateTopInk(
    float3 positionWS,
    float3 normalWS,
    half shadowAtten,
    float3 mainLightDir,
    float3 viewDirWS,
    float wetRough,
    float dryRough,
    float clearcoatStrength,
    float inkGlow,
    out float outInkMask,
    out half3 outLitInkColor)
{
    outInkMask = 0.0;
    outLitInkColor = half3(0.0, 0.0, 0.0);
    _P1TopInkContactShadowTerm = 0.0;

    // 仅在朝上的面绘制顶视图形（避免垂直立面拉伸，立面由 InkWallSurface 负责）
    half upness = smoothstep(0.28, 0.68, normalWS.y);
    if (upness <= 0.002)
        return false;

    // 计分场地之外不显墨：场外码头/远景堆场也挂本函数（PropLit 等），
    // 图集外坐标经 saturate 会固定采到边界纹素，整片场外被拉伸成"一条一条"的墨色。
    // 矩形未推送（size=0，编辑器预览等）时跳过屏蔽，保持旧行为。
    float2 playMin = _GlobalInkPlayableRect.xy;
    float2 playSize = _GlobalInkPlayableRect.zw;
    if (playSize.x > 0.001 && playSize.y > 0.001)
    {
        float2 playMax = playMin + playSize;
        if (positionWS.x < playMin.x || positionWS.x > playMax.x ||
            positionWS.z < playMin.y || positionWS.z > playMax.y)
        {
            return false;
        }

        // 计分掩码：不可涂格（道具/掩体占位、窄缝补偿）不显墨。
        // 这样玩家看到的每一块顶面墨迹都一定会计入涂地比分；道具侧面墨迹
        // 仍由 InkWallSurface 显示（竖向面本来就不计入涂地）。
        if (_GlobalInkScoreMaskEnabled > 0.5)
        {
            float2 maskUV = saturate((positionWS.xz - playMin) / playSize);
            float blocked = SAMPLE_TEXTURE2D(_GlobalInkScoreMaskTex, sampler_LinearClamp, maskUV).r;
            if (blocked > 0.5)
                return false;
        }
    }

    float2 mapOrigin = _GlobalInkMapOriginXZ.xy;
    float2 mapSize = max(float2(0.001, 0.001), _GlobalInkMapOriginXZ.zw);
    float2 inkUV = saturate((positionWS.xz - mapOrigin) / mapSize);

    float4 inkSample = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, inkUV);

    // 三次 B 样条重建（与 InkGround ForwardLit 同款）：C1 连续场 + 解析梯度，
    // 顶面墨缘观感与地面一致；_P1InkBicubicStrength = 0 时逐位回到双线性旧行为。
    float bicu = saturate(_P1InkBicubicStrength);
    float2 gradAmtCubicTexel = float2(0.0, 0.0);
    if (bicu > 0.001)
    {
        float2 texelUVs = _GlobalInkTexelSize.xy > 1e-6 ? _GlobalInkTexelSize.xy : float2(1.0 / 4096.0, 1.0 / 4096.0);
        float4 cubicGradX, cubicGradY;
        float4 cubicSample = P1SampleInkAtlasCubicGrad(_GlobalInkAtlasTex, sampler_LinearClamp,
            inkUV, texelUVs,
            clamp(ddx(inkUV), -8.0 * texelUVs, 8.0 * texelUVs),
            clamp(ddy(inkUV), -8.0 * texelUVs, 8.0 * texelUVs),
            cubicGradX, cubicGradY);
        gradAmtCubicTexel = float2(cubicGradX.a, cubicGradY.a);
        inkSample = lerp(inkSample, cubicSample, bicu);
    }

    float amt = inkSample.a;
    if (amt < 0.02)
        return false;

    float invA = 1.0 / max(amt, 0.02);
    float tRaw = saturate(inkSample.r * invA);
    float inkTsNewest = saturate(inkSample.g * invA);
    float tone = saturate(inkSample.b * invA);

    // 屏幕空间边缘抗锯齿（保留 0.55..0.72 收紧语义，防止拉伸）
    float fw = fwidth(amt);
    float maskEdge = clamp(fw * 1.2, 0.01, 0.06);
    float inkMask = smoothstep(0.55 - maskEdge, 0.72 + maskEdge, amt) * upness;

    // 墨缘接触阴影 halo：smoothstep(0.03,0.55,amt) * (1-inkMask)，按太阳 XZ 方向给 shaded 压暗
    float halo = smoothstep(0.03, 0.55, amt) * (1.0 - inkMask) * upness;
    if (inkMask <= 0.005 && halo <= 0.005)
        return false;

    // 覆盖率梯度与顶面 lip 法线偏移（P1_INK_HIGH：新增 2 次图集邻域采样）
    float2 gradAmt = float2(0.0, 0.0);
    float3 perturbedNormalWS = normalWS;

#if defined(P1_INK_HIGH)
    float2 texelUV = _GlobalInkTexelSize.xy > 1e-6 ? _GlobalInkTexelSize.xy : float2(1.0 / 4096.0, 1.0 / 4096.0);
    float amtX = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV + float2(texelUV.x, 0.0))).a;
    float amtZ = SAMPLE_TEXTURE2D(_GlobalInkAtlasTex, sampler_LinearClamp, saturate(inkUV + float2(0.0, texelUV.y))).a;
    gradAmt = float2(amtX - amt, amtZ - amt);
    // B 样条解析梯度（每纹元，量纲同前向差分）：顶面 lip 法线与接触阴影偏向随连续场平滑
    if (bicu > 0.001)
        gradAmt = lerp(gradAmt, gradAmtCubicTexel, bicu);

    float gradLen = length(gradAmt);
    if (gradLen > 0.0001)
    {
        float2 outDir = -gradAmt / gradLen;
        float lipProfile = saturate((amt - 0.35) * 2.0) * saturate((1.0 - amt) * 3.0);
        float2 lipOffsetXZ = outDir * (lipProfile * _P1InkTopDetailStrength * 0.35);
        perturbedNormalWS = normalize(normalWS + float3(lipOffsetXZ.x, 0.0, lipOffsetXZ.y));
    }
#endif

    // 计算墨缘接触阴影按太阳 XZ 偏向
    float2 sunDirXZ = normalize(mainLightDir.xz + float2(0.001, 0.001));
    float shadowBias = 1.0;
#if defined(P1_INK_HIGH)
    float gradLenM = length(gradAmt);
    if (gradLenM > 0.0001)
    {
        float2 outN = -gradAmt / gradLenM;
        shadowBias = clamp(0.65 - 0.75 * dot(outN, sunDirXZ), 0.25, 1.35);
    }
#endif
    _P1TopInkContactShadowTerm = halo * shadowBias * 0.18 * _P1InkTopDetailStrength;

    if (inkMask <= 0.005)
    {
        outInkMask = 0.0;
        outLitInkColor = half3(0.0, 0.0, 0.0);
        return true;
    }

    // 队伍交界 1px 锐利过渡
    float wT = clamp(fwidth(tRaw) * 0.75, 0.012, 0.25);
    float tm = smoothstep(0.5 - wT, 0.5 + wT, tRaw);
    half3 teamCol = lerp(_GlobalTeamAColor.rgb, _GlobalTeamBColor.rgb, tm);

    // 三段湿润：(wet, sheen)
    float ageSec = frac(_InkWetClockNorm - inkTsNewest + 1.0) * max(0.001, _InkWetClockPeriod);
    float2 wetSheen = P1InkWetProfile(ageSec, _InkWetDryingSeconds, _InkSheenSeconds);
    float wet = saturate(wetSheen.x * _GlobalWetnessFactor);
    float sheen = saturate(wetSheen.y * _GlobalWetnessFactor);

    // 单斑色调微差 + 厚度剖面
    half3 inkColor = teamCol * (0.93 + 0.13 * tone);
    float s = saturate((amt - 0.5) * 1.7);
    float hs = 1.0 - (1.0 - s) * (1.0 - s);
    inkColor = lerp(inkColor, inkColor * 1.16 + teamCol * 0.05, (1.0 - hs) * 0.35);
    inkColor *= 1.0 - 0.05 * hs;
    // 新鲜湿润状态轻微提亮
    inkColor *= 1.0 + 0.07 * wet * wet;

    // 漫反射光照（三渲二色阶，重定标下限 0.40）
    float3 L = normalize(mainLightDir);
    float3 V = normalize(viewDirWS);
    float NdotL = saturate(dot(perturbedNormalWS, L));
    float NdotV = saturate(dot(perturbedNormalWS, V));
    half inkBand = P1ToonTriBand(NdotL, 0.48, 0.08);
    half inkShadow = P1ToonShadowTerm(shadowAtten, 0.55, 0.08);
    float diffuseLight = lerp(0.40, 1.0, inkBand * inkShadow);

    // 粗糙度与清漆镜面高光
    float rough = P1InkRoughness(wet, sheen, wetRough, dryRough);
    float smoothness = saturate(1.0 - rough);
    float specPower = exp2(10.0 * smoothness + 1.0);
    float specNorm = (specPower + 8.0) / 8.0;
    float3 H = normalize(L + V);
    float NdotH = saturate(dot(perturbedNormalWS, H));
    float directSpec = pow(NdotH, specPower) * specNorm * (0.04 + 0.08 * wet);

    // 掠射菲涅尔
    float fresnel = pow(1.0 - NdotV, 3.5);
    float clearcoatF0 = lerp(0.035, 0.065, wet);
    float F = clearcoatF0 + (0.35 - clearcoatF0) * fresnel;
    half3 directTerm = _MainLightColor.rgb * (half)(directSpec * F * NdotL);

    // 双层清漆叠加与薄缘高光（湿润果冻墨质感）
    float edgeBand = P1InkWetRim(amt, wet, sheen);
    half3 rimLight = _MainLightColor.rgb * (half)(edgeBand * (0.10 + 0.35 * F) * _P1InkEdgeRimStrength);
    directTerm += _MainLightColor.rgb * P1InkCoatSpec(NdotH, NdotV, shadowAtten, wet, sheen) + rimLight;

    // 湿润边缘队色轻微强化（与地面呼应）
    inkColor += teamCol * (half)(0.12 * edgeBand * saturate(wet + sheen * 0.4));

    // 环境镜面（桌面端 Cubemap + 解析太阳盘，含掠射角抑制）
    float3 R = reflect(-V, perturbedNormalWS);
    half glanceDamp = (half)(1.0 - 0.65 * pow(saturate(1.0 - NdotV), 2.5));
    half3 envSpec = P1InkEnvSpecularFull(R, rough, NdotV, teamCol) * glanceDamp;
    // 与地面一致：新墨短期增强环境反射，干后回落，保证顶面/地面同一套湿润语言。
    envSpec *= (half)(1.0 + 0.70 * wet + 0.25 * sheen);

    half3 envTint = lerp(half3(1.0, 1.0, 1.0), saturate(teamCol * 2.2), 0.80);
    half3 finalClearcoat = (directTerm + envSpec * envTint) * (half)clearcoatStrength;

    // 阴影中保饱和（与地面同一套 INK_SHADE 语言）：亮度守恒地提升色度
    inkColor = P1InkShadowChroma(inkColor, _P1InkShadowChroma * (1.0 - diffuseLight));

    // 胶质内发光
    float effectiveGlow = (inkGlow > 0.0) ? inkGlow : _GlobalInkGlow;
    half3 litInkColor = inkColor * diffuseLight + finalClearcoat;
    litInkColor += inkColor * effectiveGlow * (0.75 + 0.35 * hs) * (1.0 - NdotL * 0.5);

    outInkMask = inkMask;
    outLitInkColor = litInkColor;
    return true;
}

/// <summary>
/// 扩展 PBR 接口：供道具/掩体顶面着色器显式调配粗糙度、清漆强度与内发光。
/// </summary>
bool ApplyTopInkOverlayPBR(
    inout half3 surfaceColor,
    float3 positionWS,
    float3 normalWS,
    half shadowAtten,
    float3 mainLightDir,
    float3 viewDirWS,
    float wetRough,
    float dryRough,
    float clearcoatStrength,
    float inkGlow)
{
    float inkMask;
    half3 litInkColor;
    if (!P1EvaluateTopInk(positionWS, normalWS, shadowAtten, mainLightDir, viewDirWS,
                          wetRough, dryRough, clearcoatStrength, inkGlow,
                          inkMask, litInkColor))
    {
        return false;
    }
    // 墨缘接触阴影 halo：压暗底色，让顶面墨有厚度
    surfaceColor *= saturate(1.0 - _P1TopInkContactShadowTerm);
    surfaceColor = lerp(surfaceColor, litInkColor, inkMask);
    return true;
}

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
    return ApplyTopInkOverlayPBR(surfaceColor, positionWS, normalWS, shadowAtten,
                                 mainLightDir, viewDirWS, 0.06, 0.42, 1.4, 0.06);
}

#endif
