#ifndef P1_INK_WALL_STYLE_INCLUDED
#define P1_INK_WALL_STYLE_INCLUDED

// ============================================================================
// P1 - InkWallStyle.hlsl (B2 墙面观感向地面看齐)
//
// 墙面墨迹的纯程序化工具：hash / 低频值噪声 / 软边扰动 / 逐区域色调 / 湿润分布。
// 设计约束：
//  - 完全自包含，只依赖 HLSL 内建（saturate/lerp/floor/frac 等），不 include
//    InkSurfaceOverlay.hlsl（另一域正在改），也不声明任何 uniform / CBUFFER。
//  - 纯函数、零纹理采样；采样留在 InkWall.shader 里，方便按平台裁剪。
//  - 所有强度输入为 0 时返回恒等结果（soft=0 → alpha 原值，strength=0 → 1.0，
//    湿润 mix=0 → 1.0），保证三个新 config 字段全 0 时与旧墙面观感逐像素一致。
//  - 名字统一 P1Wall* 前缀，避免与 InkGround / InkSurfaceOverlay 的 vnoise 冲突。
// ============================================================================

// 2D → 1D hash（数值上足够稳，够低频噪声用；不用于需要连续导数的场景）
float P1WallHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 2D 值噪声（smoothstep 插值）：返回 0..1，连续且零纹理采样
float P1WallValueNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = P1WallHash21(i);
    float b = P1WallHash21(i + float2(1.0, 0.0));
    float c = P1WallHash21(i + float2(0.0, 1.0));
    float d = P1WallHash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// ----------------------------------------------------------------------------
// B2-1 软边：给格 alpha 叠加低频 + 高频噪声扰动。
// 只改「送进 smoothstep 的 alpha 读数」，不动图集数据；soft=0 时原样返回，
// 因此 InkWall.shader 里 smoothstep(0.5 ± fwA, alpha) 与旧实现完全等价。
// meterPos 为墙面米制坐标（uv * uv2），worldPos 用来让不同朝向的立面图案不共形。
// ----------------------------------------------------------------------------
float P1WallSoftEdgeAlpha(float alpha, float fwidthA, float soft, float2 meterPos, float3 worldPos)
{
    if (soft <= 0.0001)
        return alpha;

    // 低频负责「墨斑轮廓起伏」，高频负责打断 10cm 格子的直线阶梯
    float2 q = meterPos * 3.4 + float2(worldPos.y * 1.7, worldPos.y * 0.9);
    float low = P1WallValueNoise2(q + 4.7) - 0.5;
    float high = P1WallValueNoise2(q * 3.1 + 9.2) - 0.5;
    // fwidth 大（正处在格子跳变处）时少扰动，避免把细碎边缘搅成噪点
    float calm = 1.0 - saturate(fwidthA * 2.5);
    return alpha + (low * 0.16 + high * 0.07) * soft * calm;
}

// ----------------------------------------------------------------------------
// B2-2 逐区域色调微差：世界坐标驱动的低频值噪声，返回亮度倍率。
// 与地面 splatToneStrength 的观感呼应（地面是 0.93~1.06 的单斑随机 + 6% 低频），
// 墙面没有逐斑数据，改用 1.15m 级低频区域 + 0.35m 级细节，幅度 ±9% × strength。
// strength=0 → 1.0。
// ----------------------------------------------------------------------------
float P1WallToneMul(float3 worldPos, float2 meterPos, float strength)
{
    if (strength <= 0.0001)
        return 1.0;

    // 采样域同时含世界 XZ 混合与 Y：±X / ±Z 立面不会共用同一张图案
    float2 q = float2(dot(worldPos.xz, float2(0.83, 0.56)) + worldPos.y * 0.31,
                      meterPos.y * 0.94 + worldPos.y * 0.67);
    float n = P1WallValueNoise2(q * 1.15 + 17.3) * 0.65
            + P1WallValueNoise2(q * 3.70 + 5.1) * 0.35;
    return 1.0 + (n - 0.5) * 0.18 * strength;
}

// ----------------------------------------------------------------------------
// B2-4 非均匀湿润：把整面标量湿润度乘上「世界 Y + 噪声 + 格 alpha」的分布因子。
// 返回的是倍率，最终仍由 shader 乘 _WallWetness 并 saturate，因此语义不变。
// mix=0（wallDripIntensity=0）→ 恒等 1.0，墙面回到整面均匀湿润。
//   - 世界 Y / uvY：越靠墙脚越湿（垂流积墨），墙顶更快收干；
//   - 噪声：形成大小不一的湿斑，避免整面均匀反光；
//   - 格 alpha：薄缘（亚格覆盖率低）先干，厚墨藏湿。
// ----------------------------------------------------------------------------
float P1WallWetMul(float2 meterPos, float worldY, float uvY, float cellAlpha, float mix)
{
    if (mix <= 0.0001)
        return 1.0;

    float n = P1WallValueNoise2(float2(meterPos.x * 1.35, worldY * 1.90) + 7.7) * 0.70
            + P1WallValueNoise2(float2(meterPos.x * 4.10, worldY * 5.30) + 2.3) * 0.30;
    float vertical = lerp(0.80, 1.12, saturate(1.0 - uvY)); // 1 = 墙脚
    float thickness = lerp(0.82, 1.0, saturate(cellAlpha));
    float vary = lerp(0.55, 1.35, n) * vertical * thickness;
    return lerp(1.0, vary, saturate(mix));
}

#endif // P1_INK_WALL_STYLE_INCLUDED
