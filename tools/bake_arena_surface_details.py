#!/usr/bin/env python3
"""P1 场地表面细节贴图程序化烘焙脚本 (tools/bake_arena_surface_details.py)

用途：
- 为第一张地图（货柜码头）的集装箱烘焙"瓦楞钢板"表面三件套，
  让箱体在斜俯视角下读出真实的钢材质感，而不是一块纯色平板；
- 采用本地确定性算法（NumPy + iFFT 带通噪声），固定随机种子，零外部 API 依赖；
- 与 Surfaces_Metal_* 的区别：金属通用贴图是"光滑钢板 + 焊接痕"，
  这里专门做"海运集装箱"特征——竖瓦楞浅沟、压型板折痕、锈迹垂流、
  掉漆露底、角件磨痕、以及整箱尺度的色斑不均。

产出（均为严格四方无缝、可平铺）：
    1. Surfaces_Container_N.png (512² RGB Linear OpenGL Y+):
       竖瓦楞浅沟 + 钢板折痕 + 细麻点（配合几何瓦楞使用，只补微起伏）。
    2. Surfaces_Container_RMO.png (512² RGBA Linear):
       R=Metallic(0.55~0.85，漆面处偏低、掉漆处偏高), G=AO(瓦楞沟与锈坑自遮蔽),
       B=Detail(色斑与掉漆斑块，供 shader 做 albedo 明暗调制), A=Smoothness(漆面 0.45~0.62、锈面 0.12~0.28).
    3. Surfaces_Container_Albedo.png (512² RGB sRGB):
       近白的中性漆面调制层（乘在 _BaseColor 上）：细划痕、水渍垂流、边缘积灰、
       少量掉漆亮点。保持近白是为了让每队箱色（teal/rust/slate/mustard）各自成立。
    4. Surfaces_Concrete_Albedo.png (512² RGB sRGB):
       近白的混凝土调制层：骨料斑点、水渍、垂流污痕、浅裂缝。
       场内掩体墙/挡墙/高台侧面全是纯白大平面，加这一层才有"浇筑混凝土"的质感。

目录输出规则：
- 原稿输出至 MediaRaw/props/
- 副本同步至 Assets/_Project/Art/Textures/Surfaces/
- 不生成任何 .meta（由 Unity MediaImportPostprocessor 按 _N / _RMO 后缀自动识别）

命令行示例：
    python3 tools/bake_arena_surface_details.py
    python3 tools/bake_arena_surface_details.py --seed 20261029 --res 512
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RAW_DIR = ROOT / "MediaRaw" / "props"
DEFAULT_ASSETS_DIR = ROOT / "Assets" / "_Project" / "Art" / "Textures" / "Surfaces"
DEFAULT_SEED = 20261029


# ==============================================================================
# 工具函数：噪声与法线
# ==============================================================================

def smoothstep(e0: float, e1: float, x: np.ndarray | float) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def seamless_band_noise(n: int, rng: np.random.RandomState, center_k: float, bw: float) -> np.ndarray:
    """iFFT 带通噪声，数学上严格四方无缝（周期）。"""
    kx = np.fft.fftfreq(n)
    ky = np.fft.fftfreq(n)
    KX, KY = np.meshgrid(kx, ky)
    K = np.sqrt(KX**2 + KY**2)
    band = np.exp(-0.5 * ((K - center_k / float(n)) / (bw / float(n) + 1e-6)) ** 2)
    phases = rng.uniform(0.0, 2.0 * np.pi, (n, n))
    amplitudes = rng.rayleigh(1.0, (n, n))
    F = amplitudes * np.exp(1j * phases) * band
    layer = np.real(np.fft.ifft2(F))
    std = layer.std()
    return layer / (std + 1e-7)


def anisotropic_seamless_noise(
    n: int, rng: np.random.RandomState,
    center_kx: float, center_ky: float,
    bwx: float, bwy: float
) -> np.ndarray:
    """各向异性周期噪声（频域椭圆通带）：用于"竖向拉长"的锈迹垂流与划痕。"""
    kx = np.fft.fftfreq(n) * float(n)
    ky = np.fft.fftfreq(n) * float(n)
    KX, KY = np.meshgrid(kx, ky)
    dist_x = np.minimum(np.abs(KX - center_kx), np.abs(KX + center_kx))
    dist_y = np.minimum(np.abs(KY - center_ky), np.abs(KY + center_ky))
    band = np.exp(-0.5 * ((dist_x / (bwx + 1e-6)) ** 2 + (dist_y / (bwy + 1e-6)) ** 2))
    phases = rng.uniform(0.0, 2.0 * np.pi, (n, n))
    amplitudes = rng.rayleigh(1.0, (n, n))
    F = amplitudes * np.exp(1j * phases) * band
    layer = np.real(np.fft.ifft2(F))
    std = layer.std()
    return layer / (std + 1e-7)


def height_to_normal_periodic(height: np.ndarray, strength: float = 0.4, slope_factor: float = 5.0) -> np.ndarray:
    """无缝高度场 → OpenGL (Y+) 法线 RGB uint8。"""
    dh_du = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dh_dv = (np.roll(height, 1, axis=0) - np.roll(height, -1, axis=0)) * 0.5

    nx = -strength * slope_factor * dh_du
    ny = -strength * slope_factor * dh_dv
    nz = np.ones_like(nx)
    norm = np.sqrt(nx**2 + ny**2 + nz**2)
    nx /= norm
    ny /= norm
    nz /= norm

    r = np.clip((nx * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)
    g = np.clip((ny * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)
    b = np.clip((nz * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)
    return np.stack([r, g, b], axis=-1)


# ==============================================================================
# 集装箱表面烘焙
# ==============================================================================

def bake_container_surfaces(n: int = 512, seed: int = DEFAULT_SEED):
    """返回 (normal_rgb, rmo_rgba, albedo_rgb)。

    三张图共享同一套"锈斑 / 掉漆 / 水渍"掩码，保证法线、AO 与底色对得上，
    否则会出现"凹下去的地方反而更亮"这种穿帮。
    """
    rng = np.random.RandomState(seed ^ 0x5C1A)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # ---- 1. 竖瓦楞微沟（几何已有大瓦楞，这里只补钢板自身的细折痕）----
    # 24 个周期：512px 上约 21px 一根，世界尺度下对应 15cm 左右的压型板细肋。
    # 剖面取"窄沟 + 宽平面"：沟只占 18% 相位，其余是平钢板 —— 海运箱的压型板就是这样，
    # 用对称正弦会把整面刷成波浪，读不出"板"的感觉。
    rib_phase = np.abs(((U * 24.0) % 1.0) - 0.5) * 2.0      # 0 = 沟心, 1 = 板面
    rib_groove = 1.0 - smoothstep(0.10, 0.34, rib_phase)     # 1 = 沟底, 0 = 板面

    # ---- 2. 钢板折痕 / 麻点 ----
    n_dent = seamless_band_noise(n, rng, center_k=96.0, bw=40.0)
    n_micro = seamless_band_noise(n, rng, center_k=150.0, bw=60.0)

    # ---- 3. 竖向锈迹垂流（各向异性：竖向拉伸）----
    rust_streak = anisotropic_seamless_noise(n, rng, center_kx=26.0, center_ky=2.0, bwx=10.0, bwy=1.4)
    rust_streak = smoothstep(0.55, 1.9, rust_streak)

    # ---- 4. 大尺度色斑不均（整箱喷涂厚薄）----
    paint_blotch = seamless_band_noise(n, rng, center_k=5.0, bw=2.5)

    # ---- 5. 掉漆露底斑块 ----
    n_chip = seamless_band_noise(n, rng, center_k=48.0, bw=22.0)
    chip = smoothstep(1.35, 2.05, n_chip)                  # 稀疏小块

    # ---- 6. 水渍 / 积灰（沿底边更重）----
    grime = smoothstep(0.35, 0.95, paint_blotch * 0.6 + rust_streak * 0.5 + 0.25)

    # ------------------------------------------------------------------
    # 法线：瓦楞细沟 + 折痕 + 锈迹起伏（锈处表面变粗糙、微凸起）
    # ------------------------------------------------------------------
    h = (rib_groove * 0.55
         + n_dent * 0.16
         + n_micro * 0.07
         + rust_streak * 0.30
         + chip * 0.20)
    normal_rgb = height_to_normal_periodic(h, strength=0.30, slope_factor=4.2)

    # ------------------------------------------------------------------
    # RMO
    # ------------------------------------------------------------------
    # R: Metallic —— 漆面 0.55（带漆钢板仍偏金属），掉漆露钢处 0.88
    r_metal_f = np.clip(0.55 + chip * 0.33 + n_micro * 0.03, 0.0, 1.0)
    r_metal = np.clip(r_metal_f * 255.0, 0, 255).astype(np.uint8)

    # G: AO —— 瓦楞沟、锈坑、掉漆边缘自遮蔽
    g_ao_f = np.clip(
        1.0
        - rib_groove * 0.16
        - smoothstep(0.4, 1.2, rust_streak) * 0.10
        - chip * 0.14
        - n_micro * 0.02,
        0.58, 1.0)
    g_ao = np.clip(g_ao_f * 255.0, 0, 255).astype(np.uint8)

    # B: Detail —— 色斑 + 掉漆 + 水渍（shader 用 0.88+0.24*b 做 albedo 明暗调制）
    b_detail_f = np.clip(
        0.5
        + paint_blotch * 0.10
        + chip * 0.16
        - grime * 0.18
        - rust_streak * 0.10,
        0.0, 1.0)
    b_detail = np.clip(b_detail_f * 255.0, 0, 255).astype(np.uint8)

    # A: Smoothness —— 漆面 0.52、锈面掉到 0.16
    a_smooth_f = np.clip(
        0.52
        + n_micro * 0.03
        - rust_streak * 0.30
        - chip * 0.10
        - grime * 0.12,
        0.10, 0.68)
    a_smooth = np.clip(a_smooth_f * 255.0, 0, 255).astype(np.uint8)

    rmo_rgba = np.stack([r_metal, g_ao, b_detail, a_smooth], axis=-1)

    # ------------------------------------------------------------------
    # Albedo 调制层（近白）
    # 只做"明暗不均 + 细划痕 + 水渍"，不引入色相：色相由每队 _BaseColor 决定。
    # 幅度刻意压得很小（±4% 量级）：瓦楞已经是真实几何，albedo 再花就会读成湿大理石。
    # ------------------------------------------------------------------
    alb = np.ones((n, n, 3), dtype=np.float32)

    # 大尺度喷漆厚薄 ±3.5%
    alb *= (1.0 + paint_blotch * 0.035)[..., None]

    # 瓦楞沟里略暗（金属板沟槽积灰），幅度小到只在近景可读
    alb *= (1.0 - rib_groove * 0.045)[..., None]

    # 锈迹垂流：偏暖的暗色（只压一档亮度并轻微偏红）
    rust_tint = np.array([1.00, 0.955, 0.925], dtype=np.float32)
    rust_amt = np.clip(rust_streak * 0.13, 0.0, 0.16)
    alb = alb * (1.0 - rust_amt)[..., None] + rust_tint * rust_amt[..., None]

    # 掉漆：露出的裸钢比漆面亮一档（冷灰）
    chip_tint = np.array([1.05, 1.05, 1.06], dtype=np.float32)
    chip_amt = np.clip(chip * 0.45, 0.0, 0.5)
    alb = alb * (1.0 - chip_amt)[..., None] + chip_tint * chip_amt[..., None]

    # 水渍积灰：整体压暗一点，偏中性
    alb *= (1.0 - grime * 0.055)[..., None]

    # 细划痕：极稀疏的亮线（用高频各向异性噪声切成线状）
    scratch = anisotropic_seamless_noise(n, rng, center_kx=6.0, center_ky=110.0, bwx=2.0, bwy=45.0)
    scratch = smoothstep(2.4, 3.1, scratch) * 0.055
    alb += scratch[..., None]

    albedo_rgb = np.clip(alb * 255.0, 0, 255).astype(np.uint8)

    return normal_rgb, rmo_rgba, albedo_rgb


# ==============================================================================
# 混凝土 albedo 调制层
# ==============================================================================

def bake_concrete_albedo(n: int = 512, seed: int = DEFAULT_SEED) -> np.ndarray:
    """近白的混凝土调制层（乘在 _BaseColor 上）。

    场内掩体墙/挡墙/高台立面的底色是单一的 concreteLight，斜俯视角下是一整片
    纯白大平面，读起来像塑料积木。这里补：
    - 骨料斑点（高频小幅明暗）；
    - 模板拼缝的轻微错台（低频网格）；
    - 竖向水渍垂流（各向异性噪声）；
    - 稀疏浅裂缝；
    - 局部泛碱白斑（比底色更亮一点）。
    整体均值保持在 0.94 附近，避免把场地压暗。
    """
    rng = np.random.RandomState(seed ^ 0x3B71)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 骨料：高频颗粒 + 中频斑块
    aggregate = seamless_band_noise(n, rng, center_k=110.0, bw=52.0)
    blotch = seamless_band_noise(n, rng, center_k=16.0, bw=8.0)

    # 竖向水渍垂流（沿 V 拉长）
    streak = anisotropic_seamless_noise(n, rng, center_kx=22.0, center_ky=1.6, bwx=9.0, bwy=1.2)
    streak = smoothstep(0.7, 2.0, streak)

    # 模板拼缝：每 1/4 一条细线（与 shader 的 2m 板缝不同周期，形成双层节奏）
    seam_x = np.abs(((U * 4.0) % 1.0) - 0.5) * 2.0
    seam_y = np.abs(((V * 4.0) % 1.0) - 0.5) * 2.0
    seam = 1.0 - smoothstep(0.0, 0.055, np.minimum(seam_x, seam_y))

    # 浅裂缝：稀疏的细长暗线
    crack_n = anisotropic_seamless_noise(n, rng, center_kx=30.0, center_ky=4.0, bwx=3.0, bwy=22.0)
    crack = smoothstep(2.1, 2.7, crack_n)

    # 泛碱白斑（比底色更亮）
    efflor = smoothstep(0.9, 1.8, blotch)

    alb = np.ones((n, n, 3), dtype=np.float32) * 0.955

    # 骨料与斑块的明暗
    alb *= (1.0 + aggregate * 0.030 + blotch * 0.035)[..., None]

    # 水渍垂流压暗
    alb *= (1.0 - streak * 0.10)[..., None]

    # 拼缝压暗（细而浅）
    alb *= (1.0 - seam * 0.16)[..., None]

    # 裂缝压暗
    alb *= (1.0 - crack * 0.22)[..., None]

    # 泛碱提亮（略偏冷，混凝土泛碱是白灰）
    alb += efflor[..., None] * np.array([0.045, 0.046, 0.050], dtype=np.float32)

    # 极轻微的暖冷起伏（避免整片死灰）
    warm = (blotch * 0.012)[..., None]
    alb *= np.array([1.0 + warm[0, 0, 0], 1.0, 1.0 - warm[0, 0, 0]], dtype=np.float32)

    return np.clip(alb * 255.0, 0, 255).astype(np.uint8)


# ==============================================================================
# 审计与保存
# ==============================================================================

def compute_seamless_ratio(arr: np.ndarray) -> float:
    """接缝比：首尾列/行差 与 内部相邻列/行平均差 的比值，越接近 1 越无缝。"""
    a = arr.astype(np.float32)
    seam = np.abs(a[:, 0] - a[:, -1]).mean() + np.abs(a[0, :] - a[-1, :]).mean()
    inner_x = np.abs(np.diff(a, axis=1)).mean()
    inner_y = np.abs(np.diff(a, axis=0)).mean()
    inner = inner_x + inner_y + 1e-6
    return float(seam / inner)


def audit_normal(arr: np.ndarray):
    n = arr.astype(np.float32) / 255.0 * 2.0 - 1.0
    nz = np.clip(n[..., 2], 1e-4, 1.0)
    tilt = np.degrees(np.arccos(nz))
    return float(tilt.mean()), float(tilt.max()), float((tilt > 3.0).mean())


def save_image(arr: np.ndarray, path: Path) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    img = Image.fromarray(arr)
    img.save(str(path), format="PNG", optimize=True)
    return path.stat().st_size


def main():
    parser = argparse.ArgumentParser(description="P1 集装箱瓦楞钢板表面贴图程序化烘焙器")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED, help=f"确定性随机种子 (默认 {DEFAULT_SEED})")
    parser.add_argument("--res", type=int, default=512, help="贴图分辨率 (默认 512)")
    parser.add_argument("--out-raw", type=Path, default=DEFAULT_RAW_DIR, help="原稿输出目录 (MediaRaw/props/)")
    parser.add_argument("--out-assets", type=Path, default=DEFAULT_ASSETS_DIR, help="Assets 输出目录")
    args = parser.parse_args()

    print("=" * 80)
    print(" [P1 Container Surfaces Baker] 集装箱瓦楞钢板表面贴图")
    print(f" - 随机种子: {args.seed}")
    print(f" - 分辨率: {args.res}x{args.res}")
    print("=" * 80)

    t0 = time.time()
    normal_rgb, rmo_rgba, albedo_rgb = bake_container_surfaces(n=args.res, seed=args.seed)
    print(f"    - 集装箱计算耗时: {time.time() - t0:.2f}s")

    t1 = time.time()
    concrete_albedo = bake_concrete_albedo(n=args.res, seed=args.seed)
    print(f"    - 混凝土计算耗时: {time.time() - t1:.2f}s")

    items = [
        ("Surfaces_Container_N.png", normal_rgb, True),
        ("Surfaces_Container_RMO.png", rmo_rgba, False),
        ("Surfaces_Container_Albedo.png", albedo_rgb, False),
        ("Surfaces_Concrete_Albedo.png", concrete_albedo, False),
    ]

    print("\n" + "=" * 96)
    print(f"{'贴图名称':<32} {'尺寸通道':<12} {'大小':<10} {'接缝比':<8} {'通道均值':<22}")
    print("-" * 96)

    for fname, arr, is_normal in items:
        save_image(arr, args.out_raw / fname)
        size = save_image(arr, args.out_assets / fname)
        sr = compute_seamless_ratio(arr)
        c_mean = np.round(arr.mean(axis=(0, 1)), 1)
        print(f"{fname:<32} {arr.shape[1]}x{arr.shape[0]}x{arr.shape[2]:<6} "
              f"{size / 1024.0:>7.1f} KB {sr:>7.3f} {str(c_mean.tolist()):<22}")
        if is_normal:
            tilt_mean, tilt_max, non_flat = audit_normal(arr)
            print(f"    法线自检: 平均倾角={tilt_mean:.2f}°, 最大倾角={tilt_max:.2f}°, 起伏占比={non_flat * 100.0:.1f}%")

    print("=" * 96)
    print(f"[OK] {len(items)} 张贴图生成完毕，总耗时 {time.time() - t0:.2f}s。")


if __name__ == "__main__":
    main()
