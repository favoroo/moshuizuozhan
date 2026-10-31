#!/usr/bin/env python3
"""P1 竞技场程序化贴图烘焙脚本 (tools/bake_arena_textures.py)

用途：
- 为 P1 画风重做（深炭紫底、低饱和工业场景、高对比高饱和墨水）烘焙高品质贴图；
- 采用本地确定性算法（NumPy），固定随机种子，零外部 API 依赖，100% 确定性可复现；
- 产出覆盖：
    1. ArenaGround_Albedo_V2.png (2048² RGB sRGB): 深炭紫工业底（明度 0.10~0.22），2m板缝、中线/车道标线、
       警示带、防滑线、地表文字（P1/TURF/A/B）、橡胶胎痕、低频油污、局部锈斑。
    2. ArenaGround_N.png (2048² RGB Linear OpenGL Y+): 混凝土颗粒、板缝凹槽、细微划痕、胎痕凹痕与防滑纹。
    3. ArenaGround_RMO.png (2048² RGBA Linear): R=Metallic(0), G=AO(板缝/污渍自遮罩, 0.45~1.0),
       B=Detail(0.5基线), A=Smoothness(0.35~0.75, 板缝与污渍粗糙, 胎痕与标线较平滑).
    4. InkGrain_N.png (1024² RGB Linear OpenGL Y+): 无缝平铺干墨胶质表皮（流体起伏+细微颗粒+结皮斑块+龟裂）。
    5. InkGrain_RMO.png (1024² RGBA Linear): R=0, G=咖啡环/边缘积墨噪声掩码, B=0, A=粗糙度扰动 (均值约 0.55).
    6. Surfaces_Concrete_N.png (512² RGB Linear): 无缝平铺浇筑混凝土（蜂窝孔/气泡坑/模板缝/砂浆颗粒）。
    7. Surfaces_Concrete_RMO.png (512² RGBA Linear): R=0, G=AO(气泡与接缝自遮罩), B=0.5, A=Smoothness.
    8. Surfaces_Metal_N.png (512² RGB Linear): 无缝平铺集装箱波纹钢板（垂直瓦楞周期~0.2m, 焊缝焊道, 划痕, 锈蚀）。
    9. Surfaces_Metal_RMO.png (512² RGBA Linear): R=Metallic, G=AO, B=0.5, A=Smoothness (瓦楞凸脊比凹槽更光滑).

目录输出规则：
- 原稿输出至 MediaRaw/arena/
- 副本同步至 Assets/_Project/Art/Textures/ 下对应子目录 (Arena/, Ink/, Surfaces/)
- 不生成任何 .meta 文件。

命令行示例：
    python3 tools/bake_arena_textures.py                 # 烘焙全量贴图并执行自检
    python3 tools/bake_arena_textures.py --only ground   # 仅烘焙地面 3 张
    python3 tools/bake_arena_textures.py --only ink      # 仅烘焙墨面 2 张
    python3 tools/bake_arena_textures.py --only concrete # 仅烘焙混凝土 2 张
    python3 tools/bake_arena_textures.py --only metal    # 仅烘焙金属 2 张
    python3 tools/bake_arena_textures.py --seed 123456   # 指定随机种子
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RAW_DIR = ROOT / "MediaRaw" / "arena"
DEFAULT_ASSETS_DIR = ROOT / "Assets" / "_Project" / "Art" / "Textures"
DEFAULT_SEED = 20261029

# 5×7 标准工业字模（行 0 到 6，低 5 位有效）
FONT_5X7: dict[str, list[int]] = {
    'A': [0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
    'B': [0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E],
    'C': [0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E],
    'D': [0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E],
    'E': [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F],
    'F': [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10],
    'G': [0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F],
    'H': [0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
    'I': [0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x1F],
    'J': [0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C],
    'K': [0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11],
    'L': [0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F],
    'M': [0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11],
    'N': [0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11],
    'O': [0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E],
    'P': [0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10],
    'Q': [0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D],
    'R': [0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11],
    'S': [0x0E, 0x11, 0x10, 0x0E, 0x01, 0x11, 0x0E],
    'T': [0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
    'U': [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E],
    'V': [0x11, 0x11, 0x11, 0x11, 0x0A, 0x0A, 0x04],
    'W': [0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A],
    'X': [0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11],
    'Y': [0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04],
    'Z': [0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F],
    '0': [0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E],
    '1': [0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E],
    '2': [0x0E, 0x11, 0x01, 0x06, 0x08, 0x10, 0x1F],
    '3': [0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E],
    '4': [0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02],
    '5': [0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E],
    '6': [0x0E, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x0E],
    '7': [0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08],
    '8': [0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E],
    '9': [0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E],
    ' ': [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
    '/': [0x01, 0x02, 0x04, 0x04, 0x08, 0x10, 0x10],
    '-': [0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00],
    '.': [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04],
}


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
    # 高斯频带权重
    band = np.exp(-0.5 * ((K - center_k / float(n)) / (bw / float(n) + 1e-6))**2)
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


def height_to_normal_clamped(height: np.ndarray, strength: float = 0.4, slope_factor: float = 6.0) -> np.ndarray:
    """对整场非平铺高度场生成 OpenGL (Y+) 法线贴图 (RGB uint8)；
    使用边界边缘复制 padding 计算中央差分。
    """
    P = np.pad(height, ((1, 1), (1, 1)), mode='edge')
    dh_du = (P[1:-1, 2:] - P[1:-1, :-2]) * 0.5
    dh_dv = (P[:-2, 1:-1] - P[2:, 1:-1]) * 0.5

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


def render_stencil_bitmap(text: str) -> np.ndarray:
    """把字符串转为 5x7 像素掩码 (行高 7，每字符宽 5，字符间距 1)。"""
    text = text.upper()
    cols_per_char = 6
    total_w = max(1, len(text) * cols_per_char - 1)
    bm = np.zeros((7, total_w), dtype=np.float32)
    for i, c in enumerate(text):
        rows = FONT_5X7.get(c, [0] * 7)
        for r in range(7):
            val = rows[r]
            for col in range(5):
                if (val >> (4 - col)) & 1:
                    bm[r, i * cols_per_char + col] = 1.0
    return bm


# ==============================================================================
# 地面贴图烘焙器 (ArenaGround_Albedo_V2, ArenaGround_N, ArenaGround_RMO)
# ==============================================================================

def bake_ground_textures(res: int = 2048, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """烘焙 2048×2048 地面全套三张贴图（共享几何与特征场，保证对齐）：
    1. Albedo_V2 (RGB, sRGB): 深炭紫调色板，2m 板缝，标线，胎痕，油污与锈斑。
    2. Normal (RGB, Linear OpenGL Y+): 板缝凹角，微粒粗糙，胎痕与标线起伏。
    3. RMO (RGBA, Linear): R=Metallic(0), G=AO(板缝/油污自遮罩), B=Detail(0.5), A=Smoothness.
    """
    rng = np.random.RandomState(seed)

    world_min_x, world_max_x = -20.0, 20.0
    world_min_z, world_max_z = -28.0, 28.0
    world_size_x = world_max_x - world_min_x  # 40m
    world_size_z = world_max_z - world_min_z  # 56m

    wx = np.linspace(world_min_x + world_size_x / (res * 2.0),
                     world_max_x - world_size_x / (res * 2.0), res, dtype=np.float32)
    wz = np.linspace(world_min_z + world_size_z / (res * 2.0),
                     world_max_z - world_size_z / (res * 2.0), res, dtype=np.float32)
    WX, WZ = np.meshgrid(wx, wz)

    # --------------------------------------------------------------------------
    # 1. 2m 预制混凝土板块与 3cm 板缝
    # --------------------------------------------------------------------------
    slab_x = np.floor((WX - world_min_x) / 2.0).astype(np.int32)
    slab_z = np.floor((WZ - world_min_z) / 2.0).astype(np.int32)

    # 确定性哈希板块明度差 (±3.5%)
    slab_hash = np.sin(slab_x * 12.9898 + slab_z * 78.233 + float(seed % 1000)) * 43758.5453
    slab_delta = ((slab_hash - np.floor(slab_hash)) * 2.0 - 1.0) * 0.035

    # 板块接缝（3cm 宽缝，半宽 1.5cm）
    dist_seam_x = np.abs(WX - np.round(WX / 2.0) * 2.0)
    dist_seam_z = np.abs(WZ - np.round(WZ / 2.0) * 2.0)
    dist_seam = np.minimum(dist_seam_x, dist_seam_z)
    seam_mask = smoothstep(0.022, 0.005, dist_seam)

    # --------------------------------------------------------------------------
    # 2. 调色板定义（深炭紫工业底，匹配 STYLE.md，低明度 0.10~0.20，低饱和）
    # --------------------------------------------------------------------------
    c_base = np.array([0.118, 0.098, 0.169], dtype=np.float32)       # #1E192B (均值 0.128)
    c_seam = np.array([0.075, 0.063, 0.106], dtype=np.float32)       # #13101B
    c_grime = np.array([0.059, 0.047, 0.082], dtype=np.float32)      # #0F0C15
    c_tire = np.array([0.067, 0.055, 0.094], dtype=np.float32)       # #110E18
    c_rust = np.array([0.220, 0.137, 0.094], dtype=np.float32)       # #382318 (局部暖锈斑)
    c_mark_neutral = np.array([0.431, 0.408, 0.510], dtype=np.float32) # #6E6882 (低饱和中性白字/车道标线)
    c_hazard_yellow = np.array([0.549, 0.455, 0.176], dtype=np.float32)# #8C742D (工业安全黄)
    c_hazard_dark = np.array([0.094, 0.078, 0.133], dtype=np.float32)  # #181422
    c_team_a = np.array([0.541, 0.282, 0.133], dtype=np.float32)      # #8A4822 (Team A 橙金暗标)
    c_team_b = np.array([0.157, 0.259, 0.471], dtype=np.float32)      # #284278 (Team B 钴蓝暗标)

    albedo = np.empty((res, res, 3), dtype=np.float32)
    for ch in range(3):
        albedo[:, :, ch] = c_base[ch] * (1.0 + slab_delta)

    # 低频微斑驳网格 (双线性插值)
    mottle_raw = rng.uniform(-0.025, 0.025, (44, 58)).astype(np.float32)
    mottle_img = Image.fromarray(mottle_raw).resize((res, res), resample=Image.Resampling.BILINEAR)
    mottle = np.array(mottle_img)
    albedo *= (1.0 + mottle[:, :, None])

    # 融入板缝色
    albedo = albedo * (1.0 - seam_mask[:, :, None]) + c_seam * seam_mask[:, :, None]

    # --------------------------------------------------------------------------
    # 3. 初始高度场与掩码初始化
    # --------------------------------------------------------------------------
    height = np.full((res, res), 0.50, dtype=np.float32)
    height -= seam_mask * 0.14  # 板缝微凹槽

    # 混凝土多尺度骨料微起伏与磨损刮痕
    grain_fine = rng.normal(0.0, 0.04, (res, res)).astype(np.float32)
    grain_med_raw = rng.normal(0.0, 0.06, (res // 4, res // 4)).astype(np.float32)
    grain_med = np.array(Image.fromarray(grain_med_raw).resize((res, res), Image.Resampling.BILINEAR))
    concrete_grain = grain_fine * 0.026 + grain_med * 0.045
    height += concrete_grain

    # 特征掩码（用于高度与 RMO 严格同源对齐）
    marking_mask = np.zeros((res, res), dtype=np.float32)
    stain_mask = np.zeros((res, res), dtype=np.float32)
    tire_mask = np.zeros((res, res), dtype=np.float32)
    rust_mask = np.zeros((res, res), dtype=np.float32)

    # --------------------------------------------------------------------------
    # 4. 污渍与磨损 (12 个低频软边深色油污斑)
    # --------------------------------------------------------------------------
    stain_centers = [
        (-4.5, -6.0, 2.5, 3.2, 0.4), (5.2, -12.0, 3.0, 2.2, -0.6),
        (0.0, 2.0, 3.5, 3.5, 0.0), (-8.0, 14.0, 2.2, 4.0, 0.8),
        (7.5, 16.0, 3.8, 2.4, -0.3), (-12.0, -18.0, 2.8, 2.8, 0.5),
        (13.0, -8.0, 3.2, 2.6, -0.2), (-14.0, 8.0, 2.6, 3.4, 0.3),
        (11.0, 20.0, 2.4, 3.0, -0.5), (-2.0, -22.0, 3.0, 2.0, 0.1),
        (3.0, 24.0, 2.8, 2.2, 0.7), (0.0, -15.0, 4.0, 3.0, -0.4)
    ]
    for cx, cz, rx, rz, angle in stain_centers:
        cos_a = np.cos(angle)
        sin_a = np.sin(angle)
        dx = WX - cx
        dz = WZ - cz
        lx = dx * cos_a + dz * sin_a
        lz = -dx * sin_a + dz * cos_a
        d_norm = (lx / rx)**2 + (lz / rz)**2
        s = smoothstep(1.0, 0.0, d_norm) * 0.65
        stain_mask = np.maximum(stain_mask, s)

    albedo = albedo * (1.0 - stain_mask[:, :, None]) + c_grime * stain_mask[:, :, None]
    height -= stain_mask * 0.015

    # --------------------------------------------------------------------------
    # 5. 叉车与车辆胎痕（沿主通道 Z 轴的压痕与橡胶沉积）
    # --------------------------------------------------------------------------
    # 中央通道双轮胎轨迹 (轮距 1.6m, 胎宽 0.22m, Z 轴在 [-23, 23] 范围)
    for center_x in [0.0, -8.2, 8.2]:
        for wheel_offset in [-0.8, 0.8]:
            tx = center_x + wheel_offset
            dist_t = np.abs(WX - tx)
            in_lane_z = smoothstep(23.5, 21.0, np.abs(WZ))
            t_strip = smoothstep(0.12, 0.03, dist_t) * in_lane_z * 0.45
            tire_mask = np.maximum(tire_mask, t_strip)

    albedo = albedo * (1.0 - tire_mask[:, :, None]) + c_tire * tire_mask[:, :, None]
    height -= tire_mask * 0.01

    # --------------------------------------------------------------------------
    # 6. 边缘与排水角局部锈斑 (低频氧化痕迹)
    # --------------------------------------------------------------------------
    for rx_pos, rz_pos, rad in [(-19.0, -26.0, 1.8), (19.0, -26.0, 2.0),
                                (-19.0, 26.0, 1.9), (19.0, 26.0, 2.1),
                                (-19.2, 0.0, 1.4), (19.2, 0.0, 1.5)]:
        d_rust = np.sqrt((WX - rx_pos)**2 + (WZ - rz_pos)**2)
        r_val = smoothstep(rad, rad * 0.3, d_rust) * 0.55
        rust_mask = np.maximum(rust_mask, r_val)

    albedo = albedo * (1.0 - rust_mask[:, :, None]) + c_rust * rust_mask[:, :, None]
    height -= rust_mask * 0.03

    # --------------------------------------------------------------------------
    # 7. 场地标线绘制（继承 ArenaLayoutV2 坐标与尺寸语义）
    # --------------------------------------------------------------------------
    edge_px = 1.5 * ((world_size_x + world_size_z) / (2.0 * res))

    def draw_lane_dash(cx: float, cz: float, sx: float, sz: float):
        nonlocal albedo, marking_mask, height
        half_x, half_z = sx * 0.5, sz * 0.5
        dx = np.abs(WX - cx)
        dz = np.abs(WZ - cz)
        in_box = (dx <= half_x + edge_px) & (dz <= half_z + edge_px)
        if not np.any(in_box):
            return
        dist_across = half_x - dx[in_box]
        dist_end = half_z - dz[in_box]
        pos = (WZ[in_box] - cz) + half_z
        cycle = np.mod(pos, 3.5)
        dist_dash = 1.0 - np.abs(cycle - 1.0)
        dist = np.minimum(dist_across, np.minimum(dist_end, dist_dash))
        alpha = smoothstep(-edge_px * 0.5, edge_px * 0.5, dist) * 0.85
        sub_alb = albedo[in_box]
        albedo[in_box] = sub_alb * (1.0 - alpha[:, None]) + c_mark_neutral * alpha[:, None]
        marking_mask[in_box] = np.maximum(marking_mask[in_box], alpha)
        height[in_box] += alpha * 0.015

    # 车道虚线
    draw_lane_dash(6.2, 0.0, 0.16, 44.0)
    draw_lane_dash(-6.2, 0.0, 0.16, 44.0)
    draw_lane_dash(10.4, 0.0, 0.14, 40.0)
    draw_lane_dash(-10.4, 0.0, 0.14, 40.0)

    def draw_safety_line(cx: float, cz: float, sx: float, sz: float):
        nonlocal albedo, marking_mask, height
        half_x, half_z = sx * 0.5, sz * 0.5
        dx = np.abs(WX - cx)
        dz = np.abs(WZ - cz)
        in_box = (dx <= half_x + edge_px) & (dz <= half_z + edge_px)
        if not np.any(in_box):
            return
        dist = np.minimum(half_x - dx[in_box], half_z - dz[in_box])
        alpha = smoothstep(-edge_px * 0.5, edge_px * 0.5, dist) * 0.80
        sub_alb = albedo[in_box]
        albedo[in_box] = sub_alb * (1.0 - alpha[:, None]) + c_hazard_yellow * alpha[:, None]
        marking_mask[in_box] = np.maximum(marking_mask[in_box], alpha)
        height[in_box] += alpha * 0.015

    # 边界黄色安全线
    draw_safety_line(-19.55, 0.0, 0.14, 52.0)
    draw_safety_line(19.55, 0.0, 0.14, 52.0)
    draw_safety_line(0.0, -27.55, 38.0, 0.14)
    draw_safety_line(0.0, 27.55, 38.0, 0.14)

    def draw_hazard_band(cx: float, cz: float, sx: float, sz: float):
        nonlocal albedo, marking_mask, height
        half_x, half_z = sx * 0.5, sz * 0.5
        dx = np.abs(WX - cx)
        dz = np.abs(WZ - cz)
        in_box = (dx <= half_x + edge_px) & (dz <= half_z + edge_px)
        if not np.any(in_box):
            return
        dist_rect = np.minimum(half_x - dx[in_box], half_z - dz[in_box])
        rect_alpha = smoothstep(-edge_px * 0.5, edge_px * 0.5, dist_rect)
        coord = ((WX[in_box] - cx) + (WZ[in_box] - cz)) * 0.7071
        stripe_phase = np.mod(coord, 0.35) / 0.35
        stripe_alpha = smoothstep(0.45, 0.55, stripe_phase)
        color = c_hazard_yellow * (1.0 - stripe_alpha[:, None]) + c_hazard_dark * stripe_alpha[:, None]
        eff_alpha = rect_alpha * 0.85
        sub_alb = albedo[in_box]
        albedo[in_box] = sub_alb * (1.0 - eff_alpha[:, None]) + color * eff_alpha[:, None]
        marking_mask[in_box] = np.maximum(marking_mask[in_box], eff_alpha)
        height[in_box] += eff_alpha * 0.02

    # 坡道口 45° 警示斜纹
    for hx, hz, wsx, wsz in [(0.0, -5.15, 4.4, 0.5), (0.0, 5.15, 4.4, 0.5),
                             (-5.15, 0.0, 0.5, 4.4), (5.15, 0.0, 0.5, 4.4),
                             (-13.5, -4.55, 3.6, 0.5), (13.5, 4.55, 3.6, 0.5),
                             (-13.5, 4.55, 3.6, 0.5), (13.5, -4.55, 3.6, 0.5)]:
        draw_hazard_band(hx, hz, wsx, wsz)

    def draw_ring(cx: float, cz: float, radius: float, line_width: float):
        nonlocal albedo, marking_mask, height
        half_w = line_width * 0.5
        d = np.sqrt((WX - cx)**2 + (WZ - cz)**2)
        dist = half_w - np.abs(d - radius)
        alpha = smoothstep(-edge_px * 0.5, edge_px * 0.5, dist) * 0.85
        albedo = albedo * (1.0 - alpha[:, :, None]) + c_mark_neutral * alpha[:, :, None]
        marking_mask = np.maximum(marking_mask, alpha)
        height += alpha * 0.015

    # 中央争夺圈
    draw_ring(0.0, 0.0, 3.4, 0.16)

    def draw_chevron(cx: float, cz: float, sx: float, sz: float, team_color: np.ndarray, flip_z: bool = False):
        nonlocal albedo, marking_mask, height
        half_x, half_z = sx * 0.5, sz * 0.5
        dx = np.abs(WX - cx)
        dz = (WZ - cz) if not flip_z else -(WZ - cz)
        in_box = (dx <= half_x + edge_px) & (np.abs(dz) <= half_z + edge_px)
        if not np.any(in_box):
            return
        local_x = dx[in_box]
        local_z = dz[in_box]
        # 3 个嵌套 V 形箭头
        dist_chevron = np.full(local_x.shape, -999.0, dtype=np.float32)
        thickness = 0.25
        slope = 0.70
        norm_s = np.sqrt(1.0 + slope * slope)
        for s_idx in range(3):
            peak_z = half_z - 0.2 - s_idx * 0.75
            arm_z = peak_z - slope * local_x
            d = np.abs(local_z - arm_z) / norm_s
            dist_arm = thickness * 0.5 - d
            dist_box_x = half_x - local_x
            dist_stripe = np.minimum(dist_arm, dist_box_x)
            dist_chevron = np.maximum(dist_chevron, dist_stripe)
        alpha = smoothstep(-edge_px * 0.5, edge_px * 0.5, dist_chevron) * 0.85
        sub_alb = albedo[in_box]
        albedo[in_box] = sub_alb * (1.0 - alpha[:, None]) + team_color * alpha[:, None]
        marking_mask[in_box] = np.maximum(marking_mask[in_box], alpha)
        height[in_box] += alpha * 0.02

    # 导向箭头 (Neutral 与出生口队伍导向)
    draw_chevron(10.4, -14.0, 2.0, 2.4, c_mark_neutral, False)
    draw_chevron(-10.4, 14.0, 2.0, 2.4, c_mark_neutral, True)
    draw_chevron(10.4, -19.5, 2.0, 2.4, c_mark_neutral, False)
    draw_chevron(-10.4, 19.5, 2.0, 2.4, c_mark_neutral, True)
    draw_chevron(0.0, -21.0, 5.6, 2.4, c_team_a, False)
    draw_chevron(0.0, 21.0, 5.6, 2.4, c_team_b, True)

    def draw_stencil_text(cx: float, cz: float, sx: float, sz: float, text: str, color: np.ndarray, flip_z: bool = False):
        nonlocal albedo, marking_mask, height
        bm = render_stencil_bitmap(text)
        gh, gw = bm.shape
        scale_x = (sx * 0.85) / gw
        scale_z = (sz * 0.85) / gh
        scale = min(scale_x, scale_z)
        if scale <= 1e-4:
            return
        tw = gw * scale
        th = gh * scale

        half_tw = tw * 0.5
        half_th = th * 0.5
        dx = WX - cx
        dz = (WZ - cz) if not flip_z else -(WZ - cz)
        in_box = (np.abs(dx) <= half_tw + edge_px) & (np.abs(dz) <= half_th + edge_px)
        if not np.any(in_box):
            return
        lx = dx[in_box]
        lz = dz[in_box]
        u = (lx + half_tw) / scale
        v = (half_th - lz) / scale
        iu = np.clip(np.floor(u).astype(int), 0, gw - 1)
        iv = np.clip(np.floor(v).astype(int), 0, gh - 1)
        hits = bm[iv, iu]
        alpha = hits * 0.85
        sub_alb = albedo[in_box]
        albedo[in_box] = sub_alb * (1.0 - alpha[:, None]) + color * alpha[:, None]
        marking_mask[in_box] = np.maximum(marking_mask[in_box], alpha)
        height[in_box] += alpha * 0.02

    # 地面字模标线
    draw_stencil_text(0.0, 0.0, 3.2, 1.4, "P1", c_mark_neutral)
    draw_stencil_text(0.0, -18.2, 1.8, 1.8, "A", c_team_a, False)
    draw_stencil_text(0.0, 18.2, 1.8, 1.8, "B", c_team_b, True)
    draw_stencil_text(0.0, -24.6, 3.2, 0.8, "TURF", c_team_a, False)
    draw_stencil_text(0.0, 24.6, 3.2, 0.8, "TURF", c_team_b, True)

    # --------------------------------------------------------------------------
    # 8. 生成法线贴图 (ArenaGround_N.png, OpenGL Y+)
    # --------------------------------------------------------------------------
    normal_rgb = height_to_normal_clamped(height, strength=0.42, slope_factor=18.0)

    # --------------------------------------------------------------------------
    # 9. 生成 RMO 贴图 (ArenaGround_RMO.png)
    #    R = Metallic (≈0)
    #    G = AO (0.45~1.0, 仅板缝/凹槽/油污自遮罩，不含掩体投影)
    #    B = Detail (0.5 基线)
    #    A = Smoothness (0.35~0.75, 粗糙度变化，板缝/油污粗糙，胎痕/标线较光滑)
    # --------------------------------------------------------------------------
    r_metal = np.clip(rust_mask * 0.15, 0.0, 1.0)
    # AO: 仅来自板缝自遮蔽与污渍吸收
    g_ao = np.clip(1.0 - seam_mask * 0.50 - stain_mask * 0.12 - rust_mask * 0.15, 0.0, 1.0)
    b_detail = np.full((res, res), 0.5, dtype=np.float32)

    # Smoothness: 基底 0.52，板缝更粗糙(-0.16)，污渍粗糙(-0.10)，胎痕压实(+0.08)，标线光滑(+0.12)，微粗糙抖动
    a_smooth = (0.52
                - seam_mask * 0.16
                - stain_mask * 0.10
                - rust_mask * 0.22
                + tire_mask * 0.08
                + marking_mask * 0.12
                + concrete_grain * 0.5)
    a_smooth = np.clip(a_smooth, 0.35, 0.75)

    rmo_rgba = np.stack([
        np.clip(r_metal * 255.0, 0, 255).astype(np.uint8),
        np.clip(g_ao * 255.0, 0, 255).astype(np.uint8),
        np.clip(b_detail * 255.0, 0, 255).astype(np.uint8),
        np.clip(a_smooth * 255.0, 0, 255).astype(np.uint8)
    ], axis=-1)

    albedo_rgb = np.clip(albedo * 255.0, 0, 255).astype(np.uint8)
    return albedo_rgb, normal_rgb, rmo_rgba


# ==============================================================================
# 墨面微结构烘焙器 (InkGrain_N, InkGrain_RMO)
# ==============================================================================

def bake_ink_grain(n: int = 1024, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray]:
    """烘焙 1024×1024 真正无缝平铺的胶质墨面法线与 RMO：
    - N: 中频流体起伏 + 高频微细胶质颗粒 + 结皮斑块 + 极细龟裂 (OpenGL Y+)；
    - RMO: R=0, G=咖啡环/边缘积墨噪声掩码, B=0, A=粗糙度扰动 (0..1, 均值约 0.55).
    """
    rng = np.random.RandomState(seed ^ 0x1A4C)

    # 1. 胶质流体表面起伏 (波动扭曲)
    n1 = seamless_band_noise(n, rng, 6.0, 4.0)
    n2 = seamless_band_noise(n, rng, 8.0, 5.0)
    swells = np.sin(n1 * 1.8 + np.cos(n2 * 1.5))

    # 2. 干燥结皮斑块 (中频非线性阈值)
    n_crust = seamless_band_noise(n, rng, 14.0, 8.0)
    crust = smoothstep(0.40, 1.20, n_crust)

    # 3. 极细干裂纹理 (高频脊线噪声局限在结皮斑块上)
    n_crack1 = seamless_band_noise(n, rng, 48.0, 20.0)
    n_crack2 = seamless_band_noise(n, rng, 64.0, 25.0)
    ridge1 = 1.0 - np.abs(n_crack1)
    ridge2 = 1.0 - np.abs(n_crack2)
    cracks = smoothstep(0.60, 1.00, ridge1 * ridge2) * crust

    # 4. 高频胶质细颗粒
    fine = seamless_band_noise(n, rng, 96.0, 40.0) * 0.16

    # 综合高度场
    H = swells * 0.45 + crust * 0.32 - cracks * 0.38 + fine

    # 法线计算 (使用周期 roll 差分，保证四边完全无缝)
    normal_rgb = height_to_normal_periodic(H, strength=0.42, slope_factor=4.5)

    # 5. RMO 通道
    # G 通道: 咖啡环与结皮边缘积墨掩码 (结皮梯度边缘 + 细颗粒亮点)
    dcrust_x = (np.roll(crust, -1, axis=1) - np.roll(crust, 1, axis=1)) * 0.5
    dcrust_y = (np.roll(crust, -1, axis=0) - np.roll(crust, 1, axis=0)) * 0.5
    crust_edge = np.sqrt(dcrust_x**2 + dcrust_y**2) * 14.0
    fine_specks = smoothstep(0.06, 0.16, fine)
    g_mask = np.clip(crust_edge * 0.70 + crust * 0.25 + fine_specks * 0.35, 0.0, 1.0)

    # A 通道: 粗糙度扰动 (流体区光滑~0.42, 结皮/裂纹粗糙~0.68, 校准均值至约 0.55)
    roughness = 0.51 - swells * 0.07 + crust * 0.13 + cracks * 0.22 + fine * 0.25
    roughness = np.clip(roughness, 0.0, 1.0)

    rmo_rgba = np.stack([
        np.zeros((n, n), dtype=np.uint8),
        np.clip(g_mask * 255.0, 0, 255).astype(np.uint8),
        np.zeros((n, n), dtype=np.uint8),
        np.clip(roughness * 255.0, 0, 255).astype(np.uint8)
    ], axis=-1)

    return normal_rgb, rmo_rgba


# ==============================================================================
# 混凝土掩体贴图烘焙器 (Surfaces_Concrete_N, Surfaces_Concrete_RMO)
# ==============================================================================

def bake_concrete_surfaces(n: int = 512, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray]:
    """烘焙 512×512 无缝平铺的浇筑混凝土法线与 RMO：
    - N: 蜂窝孔/气泡坑、模板板缝、砂浆骨料微粒；
    - RMO: R=0, G=AO(气泡与接缝自阴影), B=0.5, A=Smoothness (基准 0.52，孔洞与缝隙粗糙).
    """
    rng = np.random.RandomState(seed ^ 0x3C8E)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 1. 浇筑模板接缝（在 v=0.0 与 v=0.5，u=0.0 与 u=0.5 处的微台阶与浅槽）
    dist_bv = np.minimum(np.abs(V - 0.5), np.minimum(V, 1.0 - V))
    dist_bu = np.minimum(np.abs(U - 0.5), np.minimum(U, 1.0 - U))
    seam_v = smoothstep(0.010, 0.002, dist_bv)
    seam_u = smoothstep(0.010, 0.002, dist_bu)
    seams = np.maximum(seam_v, seam_u)

    # 2. 蜂窝孔与气泡坑（周期性散布圆形凹坑）
    pits = np.zeros((n, n), dtype=np.float32)
    for _ in range(45):
        px = rng.uniform(0.0, 1.0)
        py = rng.uniform(0.0, 1.0)
        pr = rng.uniform(0.014, 0.035)
        depth_scale = rng.uniform(0.5, 1.0)
        dx = np.minimum(np.abs(U - px), 1.0 - np.abs(U - px))
        dy = np.minimum(np.abs(V - py), 1.0 - np.abs(V - py))
        dist = np.sqrt(dx**2 + dy**2)
        p = smoothstep(pr, pr * 0.4, dist) * depth_scale
        pits = np.maximum(pits, p)

    # 3. 砂浆骨料微粗糙
    noise_agg = seamless_band_noise(n, rng, 24.0, 12.0) * 0.25
    noise_micro = seamless_band_noise(n, rng, 64.0, 30.0) * 0.15

    # 综合高度场
    H = -seams * 0.25 - pits * 0.45 + noise_agg + noise_micro

    # 法线
    normal_rgb = height_to_normal_periodic(H, strength=0.45, slope_factor=5.0)

    # RMO
    g_ao = np.clip(1.0 - pits * 0.60 - seams * 0.45, 0.35, 1.0)
    a_smooth = np.clip(0.52 - pits * 0.26 - seams * 0.18 + noise_agg * 0.08, 0.25, 0.65)

    rmo_rgba = np.stack([
        np.zeros((n, n), dtype=np.uint8),
        np.clip(g_ao * 255.0, 0, 255).astype(np.uint8),
        np.full((n, n), 128, dtype=np.uint8),
        np.clip(a_smooth * 255.0, 0, 255).astype(np.uint8)
    ], axis=-1)

    return normal_rgb, rmo_rgba


# ==============================================================================
# 金属集装箱瓦楞贴图烘焙器 (Surfaces_Metal_N, Surfaces_Metal_RMO)
# ==============================================================================

def bake_metal_surfaces(n: int = 512, seed: int = DEFAULT_SEED) -> tuple[np.ndarray, np.ndarray]:
    """烘焙 512×512 无缝平铺的集装箱瓦楞钢板法线与 RMO：
    - N: 垂直瓦楞波纹（8周期，匹配掩体 0.2m 尺度）、横向焊缝与焊点、细微划痕、锈蚀凹坑；
    - RMO: R=Metallic (漆面 0, 划痕/锈蚀 0.2~0.4), G=AO (瓦楞凹槽与焊缝自遮蔽),
      B=0.5, A=Smoothness (凸脊 0.78 比凹槽 0.62 更平滑，锈斑处粗糙 0.25).
    """
    rng = np.random.RandomState(seed ^ 0x5F10)

    u = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    v = np.linspace(0.0, 1.0, n, endpoint=False, dtype=np.float32)
    U, V = np.meshgrid(u, v)

    # 1. 垂直瓦楞（8 个周期，梯形平滑波，居中对称使边界平滑无缝）
    u_phase = np.mod(U * 8.0 + 0.175, 1.0)
    corrugation = np.zeros_like(u_phase)
    # 0..0.35 凸脊(1.0), 0.35..0.50 斜下, 0.50..0.85 凹槽(0.0), 0.85..1.0 斜上
    m_ridge = (u_phase >= 0.0) & (u_phase < 0.35)
    m_down = (u_phase >= 0.35) & (u_phase < 0.50)
    m_valley = (u_phase >= 0.50) & (u_phase < 0.85)
    m_up = (u_phase >= 0.85) & (u_phase <= 1.0)

    corrugation[m_ridge] = 1.0
    corrugation[m_down] = 1.0 - smoothstep(0.35, 0.50, u_phase[m_down])
    corrugation[m_valley] = 0.0
    corrugation[m_up] = smoothstep(0.85, 1.00, u_phase[m_up])

    # 2. 水平拼缝与焊道（在 v=0.38 处贯通横切，并带周期焊珠）
    dist_weld_v = np.abs(V - 0.38)
    weld_line = smoothstep(0.015, 0.003, dist_weld_v)
    weld_bead_phase = np.mod(U * 32.0, 1.0)
    weld_beads = smoothstep(0.40, 0.0, np.abs(weld_bead_phase - 0.5)) * weld_line

    # 3. 漆面划痕（细微周期划痕）
    scratch_noise = seamless_band_noise(n, rng, 48.0, 16.0)
    scratches = smoothstep(1.30, 1.80, scratch_noise) * 0.30

    # 4. 局部锈蚀斑块与掉漆
    rust_raw = seamless_band_noise(n, rng, 8.0, 4.0)
    rust_mask = smoothstep(0.60, 1.40, rust_raw)

    # 综合高度场
    H = corrugation * 0.65 + weld_beads * 0.15 - scratches * 0.08 - rust_mask * 0.20

    # 法线
    normal_rgb = height_to_normal_periodic(H, strength=0.42, slope_factor=5.5)

    # RMO
    # R: 绝大部分漆面非金属 (0)，划痕与锈蚀处露铁微金属 (0.25~0.35)
    r_metal = np.clip(scratches * 0.60 + rust_mask * 0.28, 0.0, 1.0)

    # G: AO 凹槽 0.72，焊缝浅槽 0.65，凸脊平坦 1.0
    g_ao = np.clip(1.0 - (1.0 - corrugation) * 0.28 - weld_line * 0.25 - rust_mask * 0.20, 0.55, 1.0)

    # A: Smoothness —— 凸脊处比平坦凹槽更光滑 (0.78 vs 0.62)，锈蚀非常粗糙 (0.25)
    a_smooth = (0.62
                + corrugation * 0.16
                - rust_mask * 0.38
                - scratches * 0.18
                + weld_beads * 0.06)
    a_smooth = np.clip(a_smooth, 0.20, 0.85)

    rmo_rgba = np.stack([
        np.clip(r_metal * 255.0, 0, 255).astype(np.uint8),
        np.clip(g_ao * 255.0, 0, 255).astype(np.uint8),
        np.full((n, n), 128, dtype=np.uint8),
        np.clip(a_smooth * 255.0, 0, 255).astype(np.uint8)
    ], axis=-1)

    return normal_rgb, rmo_rgba


# ==============================================================================
# 质量自检套件 (Seamless, Heightfield, Brightness, Normal, FileSize)
# ==============================================================================

def compute_seamless_ratio(img: np.ndarray) -> float:
    """计算边界接缝不连续梯度 vs 内部平均梯度的比值。对于无缝平铺纹理，比值应 < 1.5。"""
    arr = img.astype(np.float32)
    # 内部梯度
    gx_int = np.abs(arr[:, 1:] - arr[:, :-1]).mean()
    gy_int = np.abs(arr[1:, :] - arr[:-1, :]).mean()
    g_int = (gx_int + gy_int) * 0.5

    # 边界接缝阶跃
    gx_seam = np.abs(arr[:, 0] - arr[:, -1]).mean()
    gy_seam = np.abs(arr[0, :] - arr[-1, :]).mean()
    g_seam = (gx_seam + gy_seam) * 0.5

    return float(g_seam / (g_int + 1e-8))


def audit_brightness(albedo_rgb: np.ndarray) -> tuple[float, float, float, float]:
    """返回 (sRGB通道均值, sRGB标准差, 相对明度Luminance均值, 相对明度标准差)。"""
    arr = albedo_rgb.astype(np.float32) / 255.0
    srgb_mean = float(arr.mean())
    srgb_std = float(arr.std())
    lum = 0.2126 * arr[:, :, 0] + 0.7152 * arr[:, :, 1] + 0.0722 * arr[:, :, 2]
    lum_mean = float(lum.mean())
    lum_std = float(lum.std())
    return srgb_mean, srgb_std, lum_mean, lum_std


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


# ==============================================================================
# 保存与同步管理
# ==============================================================================

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
    parser = argparse.ArgumentParser(description="P1 竞技场地表及材质贴图程序化烘焙器")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED, help=f"确定性随机种子 (默认 {DEFAULT_SEED})")
    parser.add_argument("--out-raw", type=Path, default=DEFAULT_RAW_DIR, help="原稿输出目录 (MediaRaw/arena/)")
    parser.add_argument("--out-assets", type=Path, default=DEFAULT_ASSETS_DIR, help="Assets 纹理根目录")
    parser.add_argument("--only", type=str, default="all", choices=["all", "ground", "ink", "concrete", "metal"],
                        help="仅烘焙指定子集")
    args = parser.parse_args()

    raw_dir: Path = args.out_raw
    assets_dir: Path = args.out_assets
    raw_dir.mkdir(parents=True, exist_ok=True)

    print("=" * 80)
    print(" [P1 Arena Texture Baker] 开始程序化烘焙流水线")
    print(f" - 随机种子: {args.seed}")
    print(f" - MediaRaw 目录: {raw_dir}")
    print(f" - Assets 纹理根目录: {assets_dir}")
    print(f" - 任务范围: {args.only}")
    print("=" * 80)

    t0 = time.time()
    outputs_summary: list[dict] = []

    # 1. 地面贴图
    if args.only in ("all", "ground"):
        print("\n>>> [1/4] 烘焙地面核心贴图 (2048x2048)...")
        t_start = time.time()
        g_alb, g_norm, g_rmo = bake_ground_textures(res=2048, seed=args.seed)
        t_end = time.time()
        print(f"    - 地面计算耗时: {t_end - t_start:.2f}s")

        # 路径分配
        paths = [
            ("ArenaGround_Albedo_V2.png", g_alb, "Arena"),
            ("ArenaGround_N.png", g_norm, "Arena"),
            ("ArenaGround_RMO.png", g_rmo, "Arena")
        ]
        for fname, arr, sub in paths:
            raw_p = raw_dir / fname
            asset_p = assets_dir / sub / fname
            sz_raw = save_image(arr, raw_p)
            save_image(arr, asset_p)
            outputs_summary.append({
                "name": fname,
                "folder": sub,
                "shape": f"{arr.shape[1]}x{arr.shape[0]}",
                "channels": "RGBA" if arr.shape[-1] == 4 else "RGB",
                "bytes": sz_raw,
                "arr": arr,
                "is_tiling": False
            })

    # 2. 墨面贴图
    if args.only in ("all", "ink"):
        print("\n>>> [2/4] 烘焙墨面胶质表皮贴图 (1024x1024)...")
        t_start = time.time()
        ink_norm, ink_rmo = bake_ink_grain(n=1024, seed=args.seed)
        t_end = time.time()
        print(f"    - 墨面计算耗时: {t_end - t_start:.2f}s")

        paths = [
            ("InkGrain_N.png", ink_norm, "Ink"),
            ("InkGrain_RMO.png", ink_rmo, "Ink")
        ]
        for fname, arr, sub in paths:
            raw_p = raw_dir / fname
            asset_p = assets_dir / sub / fname
            sz_raw = save_image(arr, raw_p)
            save_image(arr, asset_p)
            outputs_summary.append({
                "name": fname,
                "folder": sub,
                "shape": f"{arr.shape[1]}x{arr.shape[0]}",
                "channels": "RGBA" if arr.shape[-1] == 4 else "RGB",
                "bytes": sz_raw,
                "arr": arr,
                "is_tiling": True
            })

    # 3. 混凝土贴图
    if args.only in ("all", "concrete"):
        print("\n>>> [3/4] 烘焙浇筑混凝土贴图 (512x512)...")
        t_start = time.time()
        conc_norm, conc_rmo = bake_concrete_surfaces(n=512, seed=args.seed)
        t_end = time.time()
        print(f"    - 混凝土计算耗时: {t_end - t_start:.2f}s")

        paths = [
            ("Surfaces_Concrete_N.png", conc_norm, "Surfaces"),
            ("Surfaces_Concrete_RMO.png", conc_rmo, "Surfaces")
        ]
        for fname, arr, sub in paths:
            raw_p = raw_dir / fname
            asset_p = assets_dir / sub / fname
            sz_raw = save_image(arr, raw_p)
            save_image(arr, asset_p)
            outputs_summary.append({
                "name": fname,
                "folder": sub,
                "shape": f"{arr.shape[1]}x{arr.shape[0]}",
                "channels": "RGBA" if arr.shape[-1] == 4 else "RGB",
                "bytes": sz_raw,
                "arr": arr,
                "is_tiling": True
            })

    # 4. 集装箱金属贴图
    if args.only in ("all", "metal"):
        print("\n>>> [4/4] 烘焙集装箱瓦楞钢板贴图 (512x512)...")
        t_start = time.time()
        metal_norm, metal_rmo = bake_metal_surfaces(n=512, seed=args.seed)
        t_end = time.time()
        print(f"    - 金属计算耗时: {t_end - t_start:.2f}s")

        paths = [
            ("Surfaces_Metal_N.png", metal_norm, "Surfaces"),
            ("Surfaces_Metal_RMO.png", metal_rmo, "Surfaces")
        ]
        for fname, arr, sub in paths:
            raw_p = raw_dir / fname
            asset_p = assets_dir / sub / fname
            sz_raw = save_image(arr, raw_p)
            save_image(arr, asset_p)
            outputs_summary.append({
                "name": fname,
                "folder": sub,
                "shape": f"{arr.shape[1]}x{arr.shape[0]}",
                "channels": "RGBA" if arr.shape[-1] == 4 else "RGB",
                "bytes": sz_raw,
                "arr": arr,
                "is_tiling": True
            })

    total_time = time.time() - t0
    print("\n" + "=" * 80)
    print(" 质量自检与审计报告 (Quality Audit Report)")
    print("=" * 80)

    # 1. 无缝平铺审计 (Seamlessness Audit)
    print("\n[1. 无缝平铺边界梯度审计 (np.roll 比值目标 < 1.5)]")
    for item in outputs_summary:
        if item["is_tiling"]:
            ratio = compute_seamless_ratio(item["arr"])
            passed = "PASS" if ratio < 1.5 else "FAIL"
            print(f" - {item['name']:<28} Seam/Internal 梯度比: {ratio:.4f}  [{passed}]")

    # 2. 明度审计 (Brightness Audit)
    print("\n[2. 明度与色温审计 (Brightness & Luminance Audit)]")
    # 对比旧版反照率
    old_albedo_p = ROOT / "Assets" / "_Project" / "Art" / "Textures" / "Arena" / "ArenaGround_Albedo.png"
    if old_albedo_p.exists():
        old_img = Image.open(old_albedo_p)
        old_srgb_m, old_srgb_std, old_lum_m, old_lum_std = audit_brightness(np.array(old_img))
        print(f" - [旧版对比] ArenaGround_Albedo.png:      sRGB均值={old_srgb_m:.4f} (std={old_srgb_std:.4f}), 相对明度={old_lum_m:.4f} (std={old_lum_std:.4f}) [旧暖浅灰]")
    for item in outputs_summary:
        if "Albedo" in item["name"]:
            srgb_m, srgb_std, lum_m, lum_std = audit_brightness(item["arr"])
            in_range = "PASS" if (0.10 <= srgb_m <= 0.22) else "WARN"
            print(f" - [重做验收] {item['name']:<27} sRGB均值={srgb_m:.4f} (std={srgb_std:.4f}), 相对明度={lum_m:.4f} (std={lum_std:.4f}) [目标 0.10~0.22: {in_range}]")

    # 3. 法线有效性与倾斜角审计 (Normal Validity & Tilt Audit)
    print("\n[3. 法线有效性与倾斜度审计 (Normal Map Validity)]")
    for item in outputs_summary:
        if item["name"].endswith("_N.png"):
            tilt_m, tilt_max, len_m, non_flat = audit_normal(item["arr"])
            print(f" - {item['name']:<28} 平均倾角={tilt_m:.2f}°, 最大倾角={tilt_max:.2f}°, 模长均值={len_m:.3f}, 非平坦占比={non_flat*100:.1f}%")

    # 4. RMO 通道统计审计
    print("\n[4. RMO 通道分布审计 (Roughness/Metallic/Occlusion Audit)]")
    for item in outputs_summary:
        if item["name"].endswith("_RMO.png"):
            arr = item["arr"].astype(np.float32) / 255.0
            r_m = arr[:, :, 0].mean()
            g_m = arr[:, :, 1].mean()
            b_m = arr[:, :, 2].mean()
            a_m = arr[:, :, 3].mean()
            print(f" - {item['name']:<28} R(Metal)={r_m:.3f}, G(AO)={g_m:.3f}, B(Detail)={b_m:.3f}, A(Smooth/Rough)={a_m:.3f}")

    # 5. 文件体积统计
    print("\n[5. 贴图落盘体积审计 (File Size Target: 单张 < 4MB, 全量 < 25MB)]")
    total_bytes = 0
    for item in outputs_summary:
        sz_mb = item["bytes"] / (1024 * 1024)
        total_bytes += item["bytes"]
        print(f" - {item['name']:<28} {item['shape']:<10} {item['channels']:<5} {sz_mb:.2f} MB")
    total_mb = total_bytes / (1024 * 1024)
    print(f"\n >>> 全量贴图产出: {len(outputs_summary)} 张，总耗时: {total_time:.2f}s，总大小: {total_mb:.2f} MB  [{'PASS' if total_mb < 25.0 else 'FAIL'}]")
    print("=" * 80)


if __name__ == "__main__":
    main()
