#!/usr/bin/env python3
"""preview_alpha.py —— 把透明底素材合成到深色背景上出预览图，便于人/agent 肉眼检查形状。

为什么需要它：P1 的 UI 图标按约定是「纯白主体 + alpha」（颜色交给 Unity 染色），
而透明 PNG 在白色背景的看图器里**完全看不见**，无法判断形状对不对。
本脚本把图合成到中性深灰与棋盘格上并拼成一张接触表（contact sheet），同时在
stdout 打印每张图的 alpha 覆盖率与内容框——覆盖率异常（≈0% 或 ≈100%）通常意味着抠图失败。

用法:
    preview_alpha.py <输出预览.png> <输入1.png> [输入2.png ...]
    preview_alpha.py /tmp/preview.png MediaRaw/images/*.png

输出: 把预览图写到第一个参数；stdout 打印每张图的统计。
"""
import sys
import os

try:
    from PIL import Image
    import numpy as np
except ImportError as e:
    print("需要 Pillow 与 numpy: %s" % e, file=sys.stderr)
    sys.exit(1)

CELL = 256          # 单元格边长
PAD = 12
BG_DARK = (58, 63, 74)       # 接近 P1 地面灰
CHECK_A = (44, 48, 57)
CHECK_B = (68, 73, 84)
CHECK_SZ = 16


def checker(w, h):
    """棋盘格背景，用来暴露半透明边缘与「没抠透」的区域。"""
    bg = Image.new("RGB", (w, h), CHECK_A)
    px = bg.load()
    for y in range(h):
        for x in range(w):
            if ((x // CHECK_SZ) + (y // CHECK_SZ)) % 2 == 0:
                px[x, y] = CHECK_B
    return bg


def tile(img):
    """把一张 RGBA 图等比缩进 CELL×CELL，分别合成到深灰与棋盘格上。"""
    im = img.convert("RGBA")
    im.thumbnail((CELL, CELL), Image.LANCZOS)
    out = Image.new("RGB", (CELL * 2 + PAD, CELL), (26, 28, 33))
    for i, bg_color in enumerate((BG_DARK, None)):
        bg = checker(CELL, CELL) if bg_color is None else Image.new("RGB", (CELL, CELL), bg_color)
        # 深色底用队色示意（蓝/橙）看对比，棋盘格看边缘
        if bg_color is not None:
            bg = Image.new("RGB", (CELL, CELL), bg_color)
        ox = (CELL - im.width) // 2
        oy = (CELL - im.height) // 2
        bg.paste(im, (ox, oy), im)
        out.paste(bg, (i * (CELL + PAD), 0))
    return out


def stats(img):
    a = np.asarray(img.convert("RGBA"))[..., 3]
    total = a.size
    solid = int((a > 250).sum())
    semi = int(((a > 8) & (a <= 250)).sum())
    return {
        "coverage": 100.0 * (a > 8).sum() / total,
        "solid": 100.0 * solid / total,
        "semi": 100.0 * semi / total,
        "size": img.size,
    }


def main():
    if len(sys.argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    out_path, inputs = sys.argv[1], sys.argv[2:]
    inputs = [p for p in inputs if os.path.isfile(p)]
    if not inputs:
        print("没有可读的输入图", file=sys.stderr)
        return 1

    tiles = []
    print("%-42s %8s %8s %8s %10s" % ("文件", "尺寸", "覆盖%", "不透明%", "半透明%"))
    for p in inputs:
        im = Image.open(p)
        st = stats(im)
        print("%-42s %8s %8.2f %8.2f %8.2f" % (
            os.path.basename(p)[:42], "%dx%d" % st["size"], st["coverage"], st["solid"], st["semi"]))
        if st["coverage"] < 1.0:
            print("    ⚠ 覆盖率 <1%：可能没抠出来或主体几乎全透明")
        elif st["coverage"] > 92.0:
            print("    ⚠ 覆盖率 >92%：可能抠图失败，整张都没透")
        tiles.append(tile(im))

    # 每行最多 4 个
    per_row = 4
    rows = (len(tiles) + per_row - 1) // per_row
    tw, th = CELL * 2 + PAD, CELL
    sheet = Image.new("RGB", (per_row * tw + (per_row - 1) * PAD, rows * th + (rows - 1) * PAD), (18, 20, 24))
    for i, t in enumerate(tiles):
        r, c = divmod(i, per_row)
        sheet.paste(t, (c * (tw + PAD), r * (th + PAD)))

    os.makedirs(os.path.dirname(os.path.abspath(out_path)) or ".", exist_ok=True)
    sheet.save(out_path)
    print("\n预览图: %s（左=深灰底，右=棋盘格；棋盘格用于看边缘是否干净）" % out_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())