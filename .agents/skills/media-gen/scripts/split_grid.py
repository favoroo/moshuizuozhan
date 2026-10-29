#!/usr/bin/env python3
"""网格切表：把一张 N 宫格素材表（转身表/表情表/图标网格）切成单张 PNG。

用法: split_grid.py <输入图> <输出前缀> <列数> <行数>
输出: <前缀>_r<行>c<列>.png（从 1 开始编号，先行后列）

配合 gen_image.sh 的 --grid 使用：一张图内画多视角/多表情/多图标，
同图天然保证角色与画风一致，切分后即为可单独使用的素材。
"""
import sys
from PIL import Image

src, prefix = sys.argv[1], sys.argv[2]
cols, rows = int(sys.argv[3]), int(sys.argv[4])
im = Image.open(src)
W, H = im.size
cw, ch = W // cols, H // rows
for r in range(rows):
    for c in range(cols):
        cell = im.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
        out = '%s_r%dc%d.png' % (prefix, r + 1, c + 1)
        cell.save(out)
        print(out, cell.size)
