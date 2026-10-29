#!/usr/bin/env python3
"""纯色底（品红 #FF00FF 或纯绿 #00FF00）图 -> 透明底 PNG。

用法: keyout.py <输入图> <输出.png> [最长边像素] [--green]
流程:
1. 定位背景区域并自适应裁剪到主体视界
2. 软抠边（线性渐变过渡）+ 消除背景边缘溢色
3. alpha 通道 1px 腐蚀消除残余杂边
4. 紧致裁剪到非透明主体 bbox，按需等比缩放
"""
import sys
import numpy as np
from PIL import Image, ImageFilter


def keyout(src, dst, size=None, use_green=False):
    img = Image.open(src).convert('RGB')
    a = np.asarray(img).astype(np.int16)
    R, G, B = a[..., 0], a[..., 1], a[..., 2]

    if use_green:
        # 绿色幕布度量：G 明显高于 R 和 B
        metric = G - np.maximum(R, B)
        mask = (metric > 50) & (G > 120)
    else:
        # 品红幕布度量：R 与 B 明显高于 G
        metric = np.minimum(R, B) - G
        mask = (metric > 60) & (R > 130) & (B > 130)

    if mask.mean() > 0.04:  # 存在大面积背景背板 -> 裁剪到背板
        ys, xs = np.where(mask)
        img = img.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
        a = np.asarray(img).astype(np.int16)
        R, G, B = a[..., 0], a[..., 1], a[..., 2]
        if use_green:
            metric = G - np.maximum(R, B)
        else:
            metric = np.minimum(R, B) - G

    lo, hi = 25, 105  # metric 在 [lo, hi] 间线性过渡为半透明
    alpha = (np.clip((hi - metric) / (hi - lo), 0, 1) * 255).astype(np.uint8)
    semi = (alpha > 0) & (alpha < 255)

    if use_green:
        # 去除绿色溢色
        max_rb = np.maximum(R, B).astype(np.int16)
        G2 = np.where(semi, np.minimum(G, max_rb + 40), G)
        out = np.dstack([R, G2, B, alpha]).astype(np.uint8)
    else:
        # 去除品红溢色
        g = G.astype(np.int16)
        R2 = np.where(semi, np.minimum(R, g + 50), R)
        B2 = np.where(semi, np.minimum(B, g + 50), B)
        out = np.dstack([R2, G, B2, alpha]).astype(np.uint8)

    im = Image.fromarray(out)
    # 腐蚀 1px 去晕边
    im.putalpha(im.getchannel('A').filter(ImageFilter.MinFilter(3)))
    bbox = im.getchannel('A').getbbox()
    if bbox:
        im = im.crop(bbox)
    if size:
        im.thumbnail((size, size), Image.LANCZOS)
    im.save(dst)
    print("[keyout] 导出透明底素材: %s (尺寸 %dx%d)" % (dst, im.size[0], im.size[1]))


if __name__ == '__main__':
    args = sys.argv[1:]
    is_green = '--green' in args
    if is_green:
        args.remove('--green')

    target_size = None
    if len(args) > 2 and args[2].isdigit():
        target_size = int(args[2])

    if len(args) >= 2:
        keyout(args[0], args[1], target_size, is_green)
    else:
        print("Usage: keyout.py <src> <dst> [size] [--green]")
