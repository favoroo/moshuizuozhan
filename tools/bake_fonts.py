#!/usr/bin/env python3
"""把 Inkwave 调研库的 woff2 字体解包成 Unity 可用的静态 TTF，并落地到 Art/Fonts/。

MiSans（完整 CJK 字库）的源件在 MediaRaw/fonts/（小米官方免费商用），同样在这里转静态 TTF，
作为 TMP 的中文正文字体（Semibold）与标题字体（Heavy），替换站酷两款。

为什么需要这个脚本：
- Unity 的 TrueTypeFontImporter **不认 woff2**（不认 woff2 容器，也不认可变字体轴）；
  调研库给的是 Google Fonts 的 latin 子集 woff2，必须先转成静态 TTF。
- `Rubik-latin.woff2` 是**可变字体**（wght 300–900，默认实例是 Light 300）。
  直接转出来会得到「300 细体」，HUD 上根本看不清；而 TMP 生成字体图集时只会取默认主实例。
  所以这里按参考实现的用法（`font: 900 'Rubik'`）实例化出 Regular / Bold / Black 三个静态字重。

依赖：`fontTools` + `brotli`（fontTools 解 woff2 需要）。

用法：
    python3 tools/bake_fonts.py                 # 全量 → Assets/_Project/Art/Fonts/
    python3 tools/bake_fonts.py --list          # 看源字体与将产出的字重
    python3 tools/bake_fonts.py --out-dir /tmp/fonts
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parents[1]
SRC_DIR = ROOT / "inkwave-aah-research" / "assets" / "fonts"
DEFAULT_OUT = ROOT / "Assets" / "_Project" / "Art" / "Fonts"

# 源文件 → 产出清单。每条 = (输出文件名, 可变轴实例化坐标, 家族名, 字重名, usWeightClass)
# Rubik 三档对应 INKWAVE 的设计用法：正文 Regular、强调 Bold、标题/标签 Black(900)。
# 家族名必须显式指定：Google 的可变 woff2 把 name 表里 family 写成了默认实例的
# "Rubik Light"，实例化后不纠正就会产出 "Rubik Light Bold" 这种鬼名字。
FONT_PLAN = {
    "TitanOne-latin.woff2": [
        ("TitanOne-Regular.ttf", None, "Titan One", "Regular", 400),
    ],
    "Rubik-latin.woff2": [
        ("Rubik-Regular.ttf", {"wght": 400}, "Rubik", "Regular", 400),
        ("Rubik-Bold.ttf", {"wght": 700}, "Rubik", "Bold", 700),
        ("Rubik-Black.ttf", {"wght": 900}, "Rubik", "Black", 900),
    ],
}

# MiSans：完整 CJK 字库（小米官方免费商用，原稿 woff2 在 MediaRaw/fonts/）。
# 每档字重本就是独立静态文件，无需实例化，解容器 + 重写 name 表即可。
# 分工对齐 TMP fallback 链：Semibold(650) 正文 / Heavy(900) 标题显示。
MISANS_SRC_DIR = ROOT / "MediaRaw" / "fonts"
MISANS_FONT_PLAN = {
    "MiSans-Semibold.woff2": [
        ("MiSans-Semibold.ttf", None, "MiSans", "Semibold", 650),
    ],
    "MiSans-Heavy.woff2": [
        ("MiSans-Heavy.ttf", None, "MiSans", "Heavy", 900),
    ],
}


def set_names(font: TTFont, family: str, style: str, weight: int) -> None:
    """重写 name 表，让 Unity/TMP 看到干净的家族名与字重名。"""
    full = family if style == "Regular" else f"{family} {style}"
    postscript = f"{family.replace(' ', '')}-{style}"
    wanted = {
        1: family,          # Font Family
        2: style,           # Font Subfamily (RIBBI 规则下只有 Regular/Bold，这里按实际字重写)
        3: f"{full};P1-icon-bake",  # Unique ID，写死来源方便排查
        4: full,            # Full Name
        6: postscript,      # PostScript Name
        16: family,         # Typographic Family
        17: style,          # Typographic Subfamily
    }
    name = font["name"]
    for rec in list(name.names):
        if rec.nameID in wanted:
            name.setName(wanted[rec.nameID], rec.nameID, rec.platformID, rec.platEncID, rec.langID)
    for name_id in (16, 17):
        if not name.getName(name_id, 3, 1, 0x409):
            name.setName(wanted[name_id], name_id, 3, 1, 0x409)

    font["OS/2"].usWeightClass = weight
    bold = style in ("Bold", "Black")
    # fsSelection：bit0=italic, bit5=bold, bit6=regular。三元互斥，先清干净再设一位，
    # 否则 fontTools 会报 "bit 6 (regular) is set, bits 0/5 must be clear"。
    fs = font["OS/2"].fsSelection & ~(0b1 | 0b100000 | 0b1000000)
    font["OS/2"].fsSelection = fs | (0b100000 if bold else 0b1000000)
    if bold:
        font["head"].macStyle |= 0b1
    else:
        font["head"].macStyle &= ~0b11


def convert(src: Path, dst: Path, coords: dict[str, float] | None,
            family: str, style: str, weight: int) -> tuple[int, str]:
    """woff2 → 静态 ttf。返回 (字符数, 字体全名)。"""
    font = TTFont(str(src))                      # fontTools 会自动 brotli 解 woff2
    font.flavor = None                           # 关键：不写回 woff2 容器
    if coords and "fvar" in font:
        font = instantiateVariableFont(font, coords, inplace=False, updateFontNames=False)
        font.flavor = None
    set_names(font, family, style, weight)
    dst.parent.mkdir(parents=True, exist_ok=True)
    font.save(str(dst))
    return len(font.getBestCmap()), font["name"].getDebugName(4) or dst.stem


def main() -> int:
    ap = argparse.ArgumentParser(description="woff2 字体 → Unity 静态 TTF")
    ap.add_argument("--out-dir", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--list", action="store_true")
    args = ap.parse_args()

    plans = ((SRC_DIR, FONT_PLAN), (MISANS_SRC_DIR, MISANS_FONT_PLAN))
    for src_dir, _ in plans:
        if not src_dir.is_dir():
            print(f"找不到字体源目录: {src_dir}", file=sys.stderr)
            return 2

    missing = [f"{src_dir.name}/{n}"
               for src_dir, plan in plans for n in plan
               if not (src_dir / n).exists()]
    if missing:
        print(f"缺少源字体: {', '.join(missing)}", file=sys.stderr)
        return 2

    if args.list:
        for src_dir, plan in plans:
            for src_name, outs in plan.items():
                f = TTFont(str(src_dir / src_name))
                variable = "fvar" in f
                print(f"{src_name}  (可变={variable}, 字符={len(f.getBestCmap())})")
                for out, coords, family, style, weight in outs:
                    tag = "原样静态化" if coords is None else str(coords)
                    print(f"  → {out:<22} {family} {style} ({weight})  {tag}")
        return 0

    total = 0
    for src_dir, plan in plans:
        for src_name, outs in plan.items():
            for out_name, coords, family, style, weight in outs:
                dst = args.out_dir / out_name
                glyphs, full = convert(src_dir / src_name, dst, coords, family, style, weight)
                print(f"  {out_name:<22} {glyphs:3d} 字符  fullName='{full}'  → {dst.relative_to(ROOT)}")
                total += 1
    print(f"DONE: {total} 个静态 TTF → {args.out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())