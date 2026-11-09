#ifndef P1_TOON_LIGHTING_INCLUDED
#define P1_TOON_LIGHTING_INCLUDED

// ============================================================================
// P1 - ToonLighting.hlsl
// 全场景共享的三渲二光照函数：二分色阶、冷暖分离阴影、硬边高光、轮廓光。
// 只提供纯函数，不声明材质属性 / CBUFFER，由各 Shader 自行声明，保持 SRP Batcher 兼容。
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// 半兰伯特二分色阶：0 = 暗部，1 = 亮部。softness 控制交界抗锯齿宽度，越小越硬。
// 保留给需要硬切换的对外接口；默认 Toon 漫反射已迁移到 P1ToonTriBand 三阶版。
half P1ToonBand(float NdotL, float threshold, float softness)
{
    float halfLambert = NdotL * 0.5 + 0.5;
    float s = max(softness, 0.001);
    return (half)smoothstep(threshold - s, threshold + s, halfLambert);
}

// 半兰伯特三阶色阶：0 = 暗部，0.5 = 中调，1.0 = 亮部。
// 在 threshold 附近做暗→中过渡，在 (threshold, 1) 中点做中→亮过渡，
// 亮暗面不再是硬切换，而是"暗 → 中调 → 亮"的柔和三阶：
// 这是把卡通风从 Flash 质感推进到"Splatoon 式软卡通"的核心。
// softness 扩大可同时放大两个过渡带的宽度。
half P1ToonTriBand(float NdotL, float threshold, float softness)
{
    float halfLambert = saturate(NdotL * 0.5 + 0.5);
    float s = max(softness, 0.001);
    float midCenter = (1.0 + threshold) * 0.5;
    float lo = smoothstep(threshold - s, threshold + s, halfLambert);
    float hi = smoothstep(midCenter - s, midCenter + s, halfLambert);
    return (half)(lo * 0.5 + hi * 0.5);
}

// 实时阴影衰减：把软阴影收成较清晰的明暗交界，并保留 shadowFloor 的最低直射亮度，
// 避免卡通暗部塌成死黑。
//
// 过渡带下限 `P1_TOON_SHADOW_MIN_BAND`：URP 的软阴影是 9(Medium)/16(High) tap
// 的 tent 滤波，衰减值本来就横跨约 5 个阴影纹素（Δatten ≈ 0.65）。若 smoothstep
// 的窗口比这还窄（角色曾硬编码 softness=0.02 → 窗口只有 ±0.08），PCF 的连续渐变
// 会被重新压成一条硬边，阴影轮廓就退化成阴影贴图的纹素台阶 —— 移动端 1024 贴图 /
// 50m 阴影距离 = 6cm 一块，到屏幕上就是一块块的马赛克。
// 0.12 让窗口至少覆盖 ~2 个纹素的渐变，台阶摊成连续过渡。
// 桌面端（4096/3 级联）近景一个纹素在屏幕上不足 1px，这个下限不可见，硬朗观感不变。
static const float P1_TOON_SHADOW_MIN_BAND = 0.12;

half P1ToonShadowTerm(float shadowAtten, float shadowFloor, float softness)
{
    float s = max(max(softness, 0.001) * 4.0, P1_TOON_SHADOW_MIN_BAND);
    float crisp = smoothstep(0.5 - s, 0.5 + s, shadowAtten);
    return (half)lerp(saturate(shadowFloor), 1.0, crisp);
}

// ============================================================================
// 屏幕网点半透明（Screen-Door Dither）
//
// 旧实现是 4×4 Bayer 矩阵：alpha 被量化成 16 级，图案是固定屏幕格，0.35 的
// 淡化＝每 4×4 挖掉 10 个孔 —— 移动端 renderScale 0.8 双线性放大后每个孔变成
// 5×5 屏幕像素，一块块读作「马赛克」。且 `int(fmod(screenPixel.x, 4.0))` 对负
// 屏幕坐标返回 -1，矩阵越界读，部分 GPU 上会闪。
//
// 改成 1:1 像素的交错梯度噪声（Interleaved Gradient Noise）：没有格子结构，
// 读作细颗粒；连续 hash 对负坐标天然安全；3 条 ALU，无贴图绑定（移动端/WebGL
// 通吃）。常数是 IGN 的标准取值。
// ============================================================================
float P1IgnDither(float2 screenPixel)
{
    return frac(52.9829189 * frac(dot(screenPixel, float2(0.06711056, 0.00583715))));
}

// 该像素是否应被抠掉：alpha 越接近 1 保留越多，1.0 时整片保留。
bool P1ScreenDoorDiscard(float2 screenPixel, float alpha)
{
    return alpha < 0.999 && P1IgnDither(screenPixel) >= alpha;
}

// 环境光：球谐天空光 + 阴影染色保底，保证暗部是冷色而不是灰色。
half3 P1ToonAmbient(float3 normalWS, half3 shadowTint, half scale, half floorStrength)
{
    half3 sh = SampleSH(normalWS) * scale;
    return sh + shadowTint * floorStrength;
}

// 卡通漫反射：albedo ×（环境光 + 主光 × 色阶 × 阴影项 × 冷暖染色）。
// 内部走三阶柔和过渡（暗→中→亮），签名不变；想要硬切风格的调用方直接改用 P1ToonBand。
half3 P1ToonDiffuse(
    half3 albedo,
    half3 lightColor,
    float NdotL,
    half shadowAtten,
    float threshold,
    float softness,
    float shadowFloor,
    half3 shadowTint,
    half3 ambient)
{
    half band = P1ToonTriBand(NdotL, threshold, softness);
    half shadowTerm = P1ToonShadowTerm(shadowAtten, shadowFloor, softness);
    // 阴影色与白色混合 25%，避免暖色反照率被纯蓝阴影压成脏褐色。
    half3 shadeTint = lerp(shadowTint, half3(1.0, 1.0, 1.0), 0.25);
    half3 litTint = lerp(shadeTint, half3(1.0, 1.0, 1.0), band);
    // 直射项只用 shadowTerm 收阴影，不能再乘 band：band 在背光面为 0，
    // 相乘会把背光/侧向面（集装箱、木箱、花坛的竖直立面）的直射光整体归零，
    // 只剩很弱的环境光，观感就是"物体侧面一直是黑色"。角色着色
    // （CharacterToonCommon.hlsl）一直是不乘 band 的写法，这里与之对齐。
    half3 direct = lightColor * shadowTerm * litTint;
    return albedo * (ambient + direct);
}

// 硬边卡通高光块：高光只在亮部出现，强度由 strength 控制。
half P1ToonSpec(float NdotH, float threshold, float softness, float strength, half shadowAtten)
{
    float s = max(softness, 0.001);
    half band = (half)smoothstep(threshold - s, threshold + s, NdotH);
    return band * strength * shadowAtten;
}

// 菲涅尔轮廓光：阴影区保留 35% 基础轮廓，避免暗部完全失去边缘。
half P1ToonRim(float NdotV, float power, half shadowAtten)
{
    half fres = (half)pow(saturate(1.0 - NdotV), max(power, 0.01));
    return fres * (0.35 + 0.65 * shadowAtten);
}

#endif // P1_TOON_LIGHTING_INCLUDED