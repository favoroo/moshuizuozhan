#!/usr/bin/env python3
"""P1 道具表面贴图程序化烘焙脚本 (tools/bake_prop_surfaces.py)

用途：
- 为 P1 木箱/木板与布料道具烘焙程序化法线 (_N) 与材质掩码 (_RMO) 贴图；
- 采用本地确定性算法（NumPy + iFFT），固定随机种子，零外部 API 依赖，100% 确定性可复现；
- 产出覆盖：
    1. Surfaces_Wood_N.png (512² RGB Linear OpenGL Y+):
       沿 V 轴延展的木纹年轮微起伏、导管细孔凹槽与纤维微结构。
    2. Surfaces_Wood_RMO.png (512² RGBA Linear):
       R=Metallic(0, 非金属电介质), G=AO(导管/年轮缝隙自遮蔽, 约 0.65~1.0),
       B=Detail(0.5/128 微纤维基线), A=Smoothness(0.35~0.55, 硬木年轮致密光滑, 导管粗糙).
    3. Surfaces_Cloth_N.png (512² RGB Linear OpenGL Y+):
       精细平纹编织（48×48 经纬交织）、纱线加捻微纹、柔和织物起毛。
    4. Surfaces_Cloth_RMO.png (512² RGBA Linear):
       R=Metallic(0), G=AO(经纬网眼交叠自阴影, 0.70~1.0),
       B=Fiber Detail(0.5/128 基线), A=Smoothness(0.25~0.45, 纱线凸起微亮, 凹孔粗糙).

目录输出规则：
- 原稿输出至 MediaRaw/props/
- 副本同步至 Assets/_Project/Art/Textures/Surfaces/
- 不生成任何 .meta 文件（由 Unity MediaImportPostprocessor 自动识别 _N 与 _RMO）。

命令行示例：
    python3 tools/bake_prop_surfaces.py
    python3 tools/bake_prop_surfaces.py --seed 20261029
    python3 tools/bake_prop_surfaces.py --res 512
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
# 工具函数：数学、噪声与法线
# ==============================================================================

def smoothstep(e0: float, e1: float, x: np.ndarray | float) -> np.ndarray:
    """标准三次方 smoothstep 插值 (0..1)。"""
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def seamless_band_noise(n: int, rng: np.random.RandomState, center_k: float, bw: float) -> np.ndarray:
    """使用 2D 快速傅里叶逆变换（iFFT）生成数学上严格四方无缝（Periodic）的高斯带通噪声。"""
    kx = np.fft.fftfreq(n)
    ky = np.fft.fftfreq(n)
    KX, KY = np.meshgrid(kx, ky)
    K = np.sqrt(KX**2 + KY**2)
    band = np.exp(-0.5 * ((K - center_k / float(n)) / (bw / float(n) + 1e-6))**2)
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
    """生成具有明显各向异性（拉伸纹理）的 2D 严格周期无缝带通噪声。
    在频域设计椭圆高斯通带，使某一方向频率密集、另一方向拉伸平滑。
    """
    kx = np.fft.fftfreq(n) * float(n)
    ky = np.fft.fftfreq(n) * float(n)
    KX, KY = np.meshgrid(kx, ky)
    dist_x = np.minimum(np.abs(KX - center_kx), np.abs(KX + center_kx))
    dist_y = np.minimum(np.abs(KY - center_ky), np.abs(KY + center_ky))
    band = np.exp(-0.5 * ((dist_x / (bwx + 1e-6))**2 + (dist_y / (bwy + 1e-6))**2))
    phases = rng.uniform(0.0, 2.0 * np.pi, (n, n))
    amplitudes = rng.rayleigh(1.0, (n, n))
    F = amplitudes * np.exp(1j * phases) * band
    layer = np.real(np.fft.ifft2(F))
    std = layer.std()
    return layer / (std + 1e-7)


def height_to_normal_periodic(height: np.ndarray, strength: float = 0.4, slope_factor: float = 5.0) -> np.ndarray:
    """对无缝平铺高度场生成 OpenGL (Y+) 法线贴图 (RGB uint8)；
    使用 np.roll 循环差分，边界处严格保持一阶导数连续。
    """
    # U 轴向右(+X): dh/du
    dh_du = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    # V 轴向上(+Y): 行索引减小对应 UV 向上，故 V+ 导数为 -(H[r+1] - H[r-1])
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
# 木箱/木板表面贴图烘焙器 (Surfaces_Wood_N, Surfaces_Wood_RMO)
# ==============================================================================

def bake_wood_surfaces(n: int = 512, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray]:
    """烘焙 512×512 无缝平铺的木质表面法线与 RMO：
    - N: 沿 V 轴延展的木质纹理、年轮生长带、导管毛细孔凹孔与木纤维起伏 (OpenGL Y+)；
    - RMO:
      - R: Metallic ≈ 0 (木材为电介质)；
      - G: AO (导管凹孔与年轮凹缝自遮蔽 0.65~1.0)；
      - B: Detail (0.5/128 基准，木质纤维微细节)；
      - A: Smoothness (0.35~0.55，致密晚材较平滑，导管/早材较粗糙).
    """
    rng = np.random.RandomState(seed ^ 0x2A7F)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 1. 各向异性木纤维与年轮扭曲底噪
    n_warp = anisotropic_seamless_noise(n, rng, center_kx=8.0, center_ky=1.0, bwx=4.0, bwy=1.5)
    n_fib = anisotropic_seamless_noise(n, rng, center_kx=32.0, center_ky=2.0, bwx=12.0, bwy=2.0)
    n_pore = anisotropic_seamless_noise(n, rng, center_kx=72.0, center_ky=4.0, bwx=24.0, bwy=3.0)
    n_micro = seamless_band_noise(n, rng, center_k=64.0, bw=28.0)

    # 2. 年轮（Growth Rings）：由周期正弦波与大尺度扭曲场叠加，边界四方严格无缝
    phi = np.sin(2.0 * np.pi * U * 2.0) * 1.5 + np.cos(2.0 * np.pi * U * 1.0) * 0.8 + n_warp * 1.8
    rings = np.sin(phi * 5.0) * 0.5 + np.sin(phi * 10.0) * 0.25

    # 3. 导管细孔凹槽（Wood Pores）：狭长细凹坑
    pores = smoothstep(0.9, 1.7, n_pore)

    # 4. 综合高度场
    h_wood = rings * 0.35 + n_fib * 0.22 - pores * 0.28 + n_micro * 0.08

    # 5. 生成法线 (中等法线强度)
    normal_rgb = height_to_normal_periodic(h_wood, strength=0.38, slope_factor=5.0)

    # 6. 生成 RMO 通道
    r_metal = np.zeros((n, n), dtype=np.uint8)

    # G: AO 范围约 0.65~1.0
    g_ao_float = np.clip(1.0 - pores * 0.28 - smoothstep(0.3, 0.8, -rings) * 0.07, 0.65, 1.0)
    g_ao = np.clip(g_ao_float * 255.0, 0, 255).astype(np.uint8)

    # B: Detail (0.5 / 128 基准)
    b_detail = np.clip((0.5 + n_micro * 0.08) * 255.0, 0, 255).astype(np.uint8)

    # A: Smoothness 范围严格约束在 0.35~0.55
    a_smooth_float = np.clip(0.45 + rings * 0.06 - pores * 0.10 + n_fib * 0.03, 0.35, 0.55)
    a_smooth = np.clip(a_smooth_float * 255.0, 0, 255).astype(np.uint8)

    rmo_rgba = np.stack([r_metal, g_ao, b_detail, a_smooth], axis=-1)
    return normal_rgb, rmo_rgba


# ==============================================================================
# 布料表面贴图烘焙器 (Surfaces_Cloth_N, Surfaces_Cloth_RMO)
# ==============================================================================

def bake_cloth_surfaces(n: int = 512, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray]:
    """烘焙 512×512 无缝平铺的布料表面法线与 RMO：
    - N: 48×48 经纬交织平纹编织（Warp & Weft）、纱线圆弧剖面、加捻斜纹与起毛 (OpenGL Y+)；
    - RMO:
      - R: Metallic = 0 (完全非金属)；
      - G: AO (网眼凹陷/经纬交叠阴影, 0.70~1.0)；
      - B: Detail (0.5/128 基准，微纤维细节)；
      - A: Smoothness (0.25~0.45，纱线凸顶微平滑，孔隙粗糙).
    """
    rng = np.random.RandomState(seed ^ 0x6E3B)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 1. 经纬网格解析定义 (48 个完整周期，天然严格周期无缝)
    mx = 48
    my = 48
    X = 2.0 * np.pi * mx * U
    Y = 2.0 * np.pi * my * V

    # 纱线截面圆弧拱起
    thread_x = np.power(np.cos(X) * 0.5 + 0.5, 0.7)
    thread_y = np.power(np.cos(Y) * 0.5 + 0.5, 0.7)

    # 平纹交叠高度：经纱与纬纱交替沉浮
    h_warp = thread_x * (np.sin(Y) * 0.45 + 0.5)
    h_weft = thread_y * (-np.sin(X) * 0.45 + 0.5)
    h_weave = np.maximum(h_warp, h_weft)

    # 2. 纱线加捻细纹 (Yarn Twist) 与纤维起毛
    twist = np.sin(X * 4.0 + Y * 2.0) * 0.04 * thread_x + np.sin(Y * 4.0 - X * 2.0) * 0.04 * thread_y
    n_wrinkle = seamless_band_noise(n, rng, center_k=4.0, bw=2.0) * 0.08
    n_fuzz = seamless_band_noise(n, rng, center_k=64.0, bw=28.0) * 0.08

    # 3. 综合高度场
    h_cloth = h_weave * 0.60 + twist + n_wrinkle + n_fuzz

    # 4. 生成法线 (柔和法线强度)
    normal_rgb = height_to_normal_periodic(h_cloth, strength=0.28, slope_factor=4.0)

    # 5. 生成 RMO 通道
    r_metal = np.zeros((n, n), dtype=np.uint8)

    # G: AO 织纹凹陷 0.70~1.0
    g_ao_float = np.clip(1.0 - (1.0 - h_weave) * 0.30, 0.70, 1.0)
    g_ao = np.clip(g_ao_float * 255.0, 0, 255).astype(np.uint8)

    # B: Detail (0.5 / 128 基准)
    b_detail = np.clip((0.5 + n_fuzz * 0.5) * 255.0, 0, 255).astype(np.uint8)

    # A: Smoothness 范围严格约束在 0.25~0.45
    a_smooth_float = np.clip(0.28 + h_weave * 0.14 + n_wrinkle * 0.03, 0.25, 0.45)
    a_smooth = np.clip(a_smooth_float * 255.0, 0, 255).astype(np.uint8)

    rmo_rgba = np.stack([r_metal, g_ao, b_detail, a_smooth], axis=-1)
    return normal_rgb, rmo_rgba


def bake_cloth_albedo(n: int = 512, seed: int = DEFAULT_SEED) -> np.ndarray:
    """烘焙 512×512 无缝平铺布料 albedo（P31 软化卡通的材质细节层）：

    - 基础近白暖灰 (0.94, 0.92, 0.89) sRGB：乘到材质 _BaseColor 上只加肌理、不改色相；
    - 48×48 平纹编织 ±3.5% 明暗、低频带通 ±2%、微纤维噪点 ±1.5%；
    - 对比刻意压低：角色/道具是卡通纯色基底，albedo 只负责"有生活痕迹"，不抢色。
    """
    rng = np.random.RandomState(seed ^ 0xA17D)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 与 bake_cloth_surfaces 同一套 48×48 平纹编织高度场（albedo 明暗与法线/N 凹凸同源）
    X = 2.0 * np.pi * 48 * U
    Y = 2.0 * np.pi * 48 * V
    thread_x = np.power(np.cos(X) * 0.5 + 0.5, 0.7)
    thread_y = np.power(np.cos(Y) * 0.5 + 0.5, 0.7)
    h_warp = thread_x * (np.sin(Y) * 0.45 + 0.5)
    h_weft = thread_y * (-np.sin(X) * 0.45 + 0.5)
    h_weave = np.maximum(h_warp, h_weft)

    low_freq = seamless_band_noise(n, rng, center_k=3.0, bw=1.5) * 0.020
    fiber = seamless_band_noise(n, rng, center_k=48.0, bw=18.0) * 0.015

    shade = 1.0 + (h_weave - 0.5) * 0.070 + low_freq + fiber
    base = np.array([0.94, 0.92, 0.89], dtype=np.float32)
    rgb = np.clip(base[None, None, :] * shade[..., None] * 255.0, 0, 255).astype(np.uint8)
    return rgb


# ==============================================================================
# 质量审计
# ==============================================================================

def compute_seamless_ratio(img: np.ndarray) -> float:
    """计算边界接缝不连续梯度 vs 内部平均梯度的比值。无缝平铺纹理应 < 1.5。"""
    arr = img.astype(np.float32)
    gx_int = np.abs(arr[:, 1:] - arr[:, :-1]).mean()
    gy_int = np.abs(arr[1:, :] - arr[:-1, :]).mean()
    g_int = (gx_int + gy_int) * 0.5

    gx_seam = np.abs(arr[:, 0] - arr[:, -1]).mean()
    gy_seam = np.abs(arr[0, :] - arr[-1, :]).mean()
    g_seam = (gx_seam + gy_seam) * 0.5

    return float(g_seam / (g_int + 1e-8))


def audit_normal(normal_rgb: np.ndarray) -> tuple[float, float, float, float]:
    """返回 (平均倾斜角(度), 最大倾斜角(度), 向量模长均值, 非平坦像素占比)。"""
    v = (normal_rgb.astype(np.float32) / 255.0) * 2.0 - 1.0
    v_len = np.linalg.norm(v, axis=-1)
    len_mean = float(v_len.mean())
    cos_theta = np.clip(v[:, :, 2] / (v_len + 1e-8), -1.0, 1.0)
    tilt_deg = np.arccos(cos_theta) * (180.0 / np.pi)
    tilt_mean = float(tilt_deg.mean())
    tilt_max = float(tilt_deg.max())
    non_flat = float(np.mean(tilt_deg > 0.5))
    return tilt_mean, tilt_max, len_mean, non_flat


def save_image(img_arr: np.ndarray, path: Path) -> int:
    """以标准 8bit PNG 写入目标路径，返回文件字节大小。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    img = Image.fromarray(img_arr)
    img.save(str(path), format='PNG', optimize=True)
    return path.stat().st_size


# ==============================================================================
# 主执行入口
# ==============================================================================

def main():
    parser = argparse.ArgumentParser(description="P1 木箱木板与布料道具表面贴图程序化烘焙器")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED, help=f"确定性随机种子 (默认 {DEFAULT_SEED})")
    parser.add_argument("--res", type=int, default=512, help="贴图分辨率 (默认 512)")
    parser.add_argument("--out-raw", type=Path, default=DEFAULT_RAW_DIR, help="原稿输出目录 (MediaRaw/props/)")
    parser.add_argument("--out-assets", type=Path, default=DEFAULT_ASSETS_DIR, help="Assets 输出目录")
    args = parser.parse_args()

    raw_dir: Path = args.out_raw
    assets_dir: Path = args.out_assets
    raw_dir.mkdir(parents=True, exist_ok=True)
    assets_dir.mkdir(parents=True, exist_ok=True)

    print("=" * 80)
    print(" [P1 Prop Surfaces Baker] 开始程序化烘焙木质与布料表面贴图")
    print(f" - 随机种子: {args.seed}")
    print(f" - 分辨率: {args.res}x{args.res}")
    print(f" - 原稿目录: {raw_dir}")
    print(f" - Assets 目录: {assets_dir}")
    print("=" * 80)

    t0 = time.time()
    outputs_summary: list[dict] = []

    # 1. 木材贴图
    print("\n>>> [1/2] 烘焙木箱/木板贴图 (Surfaces_Wood_N, Surfaces_Wood_RMO)...")
    t_start = time.time()
    wood_n, wood_rmo = bake_wood_surfaces(n=args.res, seed=args.seed)
    print(f"    - 木材计算耗时: {time.time() - t_start:.2f}s")

    # 2. 布料贴图
    print("\n>>> [2/3] 烘焙布料贴图 (Surfaces_Cloth_N, Surfaces_Cloth_RMO)...")
    t_start = time.time()
    cloth_n, cloth_rmo = bake_cloth_surfaces(n=args.res, seed=args.seed)
    print(f"    - 布料计算耗时: {time.time() - t_start:.2f}s")

    # 3. 布料 albedo（P31 角色/道具材质细节层）
    print("\n>>> [3/3] 烘焙布料 albedo (Surfaces_Cloth_Albedo, P31 软化卡通材质细节)...")
    t_start = time.time()
    cloth_albedo = bake_cloth_albedo(n=args.res, seed=args.seed)
    print(f"    - 布料 albedo 计算耗时: {time.time() - t_start:.2f}s")

    # 4. 保存并审计
    items = [
        ("Surfaces_Wood_N.png", wood_n, True),
        ("Surfaces_Wood_RMO.png", wood_rmo, False),
        ("Surfaces_Cloth_N.png", cloth_n, True),
        ("Surfaces_Cloth_RMO.png", cloth_rmo, False),
        ("Surfaces_Cloth_Albedo.png", cloth_albedo, False),
    ]

    print("\n>>> 正在写入 PNG 并执行贴图审计...")
    for fname, arr, is_normal in items:
        raw_p = raw_dir / fname
        asset_p = assets_dir / fname
        save_image(arr, raw_p)
        sz = save_image(arr, asset_p)

        sr = compute_seamless_ratio(arr)
        channels = arr.shape[-1]
        c_min = arr.min(axis=(0, 1))
        c_max = arr.max(axis=(0, 1))
        c_mean = np.round(arr.mean(axis=(0, 1)), 2)

        summary_item = {
            "name": fname,
            "shape": f"{arr.shape[1]}x{arr.shape[0]}x{channels}",
            "size_kb": f"{sz / 1024.0:.1f} KB",
            "seamless_ratio": f"{sr:.3f}",
            "c_min": str(c_min.tolist()),
            "c_max": str(c_max.tolist()),
            "c_mean": str(c_mean.tolist()),
        }

        if is_normal:
            tilt_mean, tilt_max, len_mean, non_flat = audit_normal(arr)
            summary_item["tilt_mean"] = f"{tilt_mean:.2f}°"
            summary_item["tilt_max"] = f"{tilt_max:.2f}°"
            summary_item["non_flat_pct"] = f"{non_flat * 100.0:.1f}%"

        outputs_summary.append(summary_item)

    # 打印审计表格
    print("\n" + "=" * 90)
    print(f"{'贴图名称':<25} {'尺寸通道':<12} {'大小':<10} {'接缝比':<8} {'通道均值':<24}")
    print("-" * 90)
    for s in outputs_summary:
        print(f"{s['name']:<25} {s['shape']:<12} {s['size_kb']:<10} {s['seamless_ratio']:<8} {s['c_mean']:<24}")
    print("=" * 90)

    print("\n法线贴图倾斜角自检：")
    for s in outputs_summary:
        if "tilt_mean" in s:
            print(f" - {s['name']}: 平均倾角={s['tilt_mean']}, 最大倾角={s['tilt_max']}, 起伏像素占比={s['non_flat_pct']}")

    total_time = time.time() - t0
    print(f"\n[OK] 4 张贴图生成完毕，总耗时 {total_time:.2f}s。")


if __name__ == "__main__":
    main()
