#!/usr/bin/env python3
"""把 Inkwave 调研库的 SVG 图标烘焙成 Unity 可用的「纯白 + 透明」UI 精灵。

为什么需要这个脚本：
- 调研库的 SVG 是**彩色**的（深色描边 + 橙色主体 + 浅色填充），而 P1 的图标必须是
  **纯白主体 + alpha**，队伍颜色由 Unity 的 `Image.color` 运行时染色（一套图标服务两队）。
- 调研库有 27 个 SVG 带**重复属性**（`stroke-width` 写了两次），标准 XML 解析器直接报
  `Attribute stroke-width redefined`，必须先做去重消毒。
- 直接把彩色图平铺成白色剪影会丢掉内部结构（描边与填充融成一团），
  这里用「亮度 → 压缩 alpha」的 duotone 映射：描边接近全不透明、浅色填充半透明，
  既保持纯白可染色，又保住眼睛/齿轮孔/闪电等内部细节。

依赖：`brew install librsvg`（提供 rsvg-convert）；Python 侧只用 stdlib + Pillow。

用法：
    python3 tools/bake_icons.py                  # 全量 66 个 → Assets/_Project/Art/Sprites/Icons/
    python3 tools/bake_icons.py --list           # 看图标清单与目标路径
    python3 tools/bake_icons.py --only weapon    # 只处理某一类（类名或文件名关键字）
    python3 tools/bake_icons.py --size 256       # 换输出边长
    python3 tools/bake_icons.py --out-dir /tmp/x # 换输出根目录（调试用，不碰 Assets）
    python3 tools/bake_icons.py --preview /tmp/contact_sheet.png   # 另出深底缩略图自查

产物是确定性的（同一输入 → 同一字节），可以随时重烘，不进 generation 台账。
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SRC_DIR = ROOT / "inkwave-aah-research" / "assets" / "icons"
DEFAULT_OUT = ROOT / "Assets" / "_Project" / "Art" / "Sprites" / "Icons"

# 分类归档：目录 → 文件名前缀。原文件名 `<CATEGORY>_<name>.svg` 会被剥掉前缀，
# 换成语义化的 snake_case（例：WEAPON_ICONS_shooter.svg → weapon_shooter.png）。
CATEGORIES = {
    "Weapons": "weapon",    # 7 类主武器
    "Specials": "special",  # 2 个大招
    "Subs": "sub",          # 副武器（炸弹）
    "Marks": "mark",        # 对局标记：死亡 / 涂地印章 / 乌贼本体
    "Rules": "rule",        # 玩法规则插画（爬墙/潜墨/敌方/涂地）
    "Glyphs": "glyph",      # 通用 UI 字形
}

# 显式映射：调研库文件名 → (分类目录, 输出名)。顺序不影响产物，只影响 --list 的排版。
CATEGORY_RULES = [
    ("WEAPON_ICONS_", "Weapons"),
    ("SPECIAL_ICONS_", "Specials"),
    ("SUB_ICONS_", "Subs"),
    ("RULE_ART_", "Rules"),
    ("GLYPHS_", "Glyphs"),
    ("DEATH_ICON", "Marks"),
    ("SPLAT_ICON", "Marks"),
    ("SQUID", "Marks"),
]

# 少数文件整体就是一个名字（没有下划线分段），直接给短名，避免 mark_death_icon 这类啰嗦产物
NAME_ALIASES = {
    "DEATH_ICON": "death",
    "SPLAT_ICON": "splat",
    "SQUID": "squid",
}

# 图标按 512 输出：UI 里最大用到 128px 时有 4x 余量，也匹配
# MediaImportPostprocessor 给 Art/Sprites 设的 maxTextureSize=512。
DEFAULT_SIZE = 512


def classify(path: Path) -> tuple[str, str] | None:
    """返回 (分类目录, 输出文件名不含扩展名)。未知前缀返回 None。"""
    stem = path.stem
    if stem in NAME_ALIASES:
        return "Marks", f"{CATEGORIES['Marks']}_{NAME_ALIASES[stem]}"
    for prefix, folder in CATEGORY_RULES:
        if stem.startswith(prefix):
            # 剥掉 "WEAPON_ICONS_" 这类全大写前缀；DEATH_ICON/SPLAT_ICON/SQUID 整体当名字
            name = stem[len(prefix):] if stem.startswith(prefix) and prefix.endswith("_") else stem
            name = name or stem
            name = name.lower()
            name = re.sub(r"[^a-z0-9]+", "_", name).strip("_")
            return folder, f"{CATEGORIES[folder]}_{name}"
    return None


def sanitize_svg(text: str) -> str:
    """去掉同一个标签里重复的属性（保留最后一次），否则 librsvg 会拒绝解析。

    调研库里有 27 个文件把 stroke-width 写了两遍（g 上先 3 再 3.4 之类），
    浏览器容忍，标准 XML 解析器不容忍。
    """
    def fix_tag(tag: str) -> str:
        if tag.startswith("<!") or tag.startswith("<?"):
            return tag
        head = re.match(r"<\s*/?\s*([A-Za-z_:][-A-Za-z0-9_:.]*)", tag)
        if not head:
            return tag
        name = head.group(1)
        closing = tag.startswith("</")
        self_closing = tag.rstrip().endswith("/>")

        attrs = re.findall(r"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(\"[^\"]*\"|'[^']*')", tag)
        if not attrs:
            return tag

        merged: dict[str, str] = {}
        for key, value in attrs:
            merged[key] = value  # 后写的覆盖先写的，与 CSS 后者优先一致

        built = "<" + ("/" if closing else "") + name
        for key, value in merged.items():
            built += " " + key + "=" + value
        built += " />" if self_closing else ">"
        return built

    return re.sub(r"<[^>]+>", lambda m: fix_tag(m.group(0)), text)


def render(svg: Path, size: int, workdir: Path) -> Image.Image:
    """SVG → 正确宽高比的 RGBA 位图（长边 = size）。"""
    text = sanitize_svg(svg.read_text(encoding="utf-8"))
    tmp_svg = workdir / (svg.stem + ".svg")
    tmp_svg.write_text(text, encoding="utf-8")

    box = re.search(r'viewBox="([^"]+)"', text)
    if not box:
        raise ValueError(f"{svg.name}: 没有 viewBox，无法确定宽高比")
    parts = [float(v) for v in re.split(r"[\s,]+", box.group(1).strip()) if v]
    vb_w, vb_h = parts[2], parts[3]

    if vb_w >= vb_h:
        width, height = size, max(1, round(size * vb_h / vb_w))
    else:
        width, height = max(1, round(size * vb_w / vb_h)), size

    out_png = workdir / (svg.stem + ".png")
    subprocess.run(
        ["rsvg-convert", "-w", str(width), "-h", str(height), "-f", "png",
         "-o", str(out_png), str(tmp_svg)],
        check=True, capture_output=True,
    )
    return Image.open(out_png).convert("RGBA")


def to_white(im: Image.Image, cut: str = "dark",
             dark_lo: float = 0.08, dark_hi: float = 0.26,
             light_hi: float = 0.86, light_lo: float = 0.92) -> Image.Image:
    """彩色 RGBA → 纯白 RGBA（二值化 + 抗锯齿边缘）。

    为什么不是简单的「保留覆盖度 + 涂白」：描边与填充会融成一团白剪影，
    齿轮的孔、眼睛的瞳孔、枪身分件全部消失，缩到 48px 就是一坨。

    极性可选（原画是「深色描边 + 浅色主体 + 亮色高光」的三层结构，
    把哪一层挖空决定了图标读起来像什么）：

      cut="dark"  ← 默认。深色描边/暗部 → 挖空；主体与高光 → 实心白。
                     成品是「实心主体 + 镂空细节线」，暗底上细节清晰，
                     也让 P4 的描边特效只在外轮廓上生效。
      cut="light" 深色描边 → 实心白（保住外轮廓）；亮色留白 → 镂空。
                     成品更像传统「白线稿」，但细节是反相的（瞳孔变白、孔洞变实）。

    RGB 恒为纯白 —— 队伍颜色完全交给 Unity 的 Image.color。
    """
    r, g, b, a = im.split()
    lum = Image.merge("RGB", (r, g, b)).convert("L")

    if cut == "dark":
        lo, hi = dark_lo * 255.0, dark_hi * 255.0

        def tone(v: int) -> int:          # 暗 → 0（挖空），中/亮 → 255
            if v <= lo:
                return 0
            if v >= hi:
                return 255
            return int(round(255 * (v - lo) / (hi - lo)))
    elif cut == "light":
        lo, hi = light_hi * 255.0, light_lo * 255.0

        def tone(v: int) -> int:          # 亮 → 0（挖空），中/暗 → 255
            if v <= lo:
                return 255
            if v >= hi:
                return 0
            return int(round(255 * (hi - v) / (hi - lo)))
    else:
        raise ValueError("cut 只能是 'dark' 或 'light'，收到: " + repr(cut))

    scaled = lum.point(tone)
    alpha = Image.new("L", im.size)
    alpha.putdata([
        int(round(cov * tone_v / 255.0))
        for cov, tone_v in zip(a.getdata(), scaled.getdata())
    ])

    white = Image.new("RGBA", im.size, (255, 255, 255, 255))
    white.putalpha(alpha)
    return white


def pad_square(im: Image.Image, size: int) -> Image.Image:
    """居中放进 size×size 的透明画布（120×80 的横幅图标要和方形图标同尺寸）。"""
    canvas = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    x = (size - im.width) // 2
    y = (size - im.height) // 2
    canvas.alpha_composite(im, (x, y))
    return canvas


def contact_sheet(rows: list[tuple[str, Image.Image]], out: Path, cell: int = 96) -> None:
    """深底缩略图：纯白图标在白底看图器里完全看不见，必须垫深色才看得出来。"""
    cols = 11
    pad = 8
    label_h = 14
    sheet = Image.new("RGBA", (cols * (cell + pad) + pad, ((len(rows) + cols - 1) // cols) * (cell + pad + label_h) + pad),
                      (28, 28, 34, 255))
    for i, (name, im) in enumerate(rows):
        col, row = i % cols, i // cols
        thumb = im.copy()
        thumb.thumbnail((cell, cell), Image.LANCZOS)
        x = pad + col * (cell + pad) + (cell - thumb.width) // 2
        y = pad + row * (cell + pad + label_h) + (cell - thumb.height) // 2
        sheet.alpha_composite(thumb, (x, y))
    sheet.convert("RGB").save(out, optimize=True)


def main() -> int:
    ap = argparse.ArgumentParser(description="SVG 图标 → Unity 纯白透明精灵")
    ap.add_argument("--size", type=int, default=DEFAULT_SIZE, help=f"输出边长（默认 {DEFAULT_SIZE}）")
    ap.add_argument("--out-dir", type=Path, default=DEFAULT_OUT, help="输出根目录")
    ap.add_argument("--only", help="只处理匹配关键字（分类目录名或文件名）")
    ap.add_argument("--list", action="store_true", help="只列清单不烘焙")
    ap.add_argument("--preview", type=Path, help="另出深底缩略图到该路径")
    ap.add_argument("--cut", choices=("dark", "light"), default="dark",
                    help="挖空哪一层：dark=挖深色描边/暗部（默认，实心主体+镂空细节）；light=挖亮色留白")
    args = ap.parse_args()

    if not SRC_DIR.is_dir():
        print(f"找不到图标源目录: {SRC_DIR}", file=sys.stderr)
        return 2
    if not args.list and shutil.which("rsvg-convert") is None:
        print("缺少 rsvg-convert，请先 `brew install librsvg`", file=sys.stderr)
        return 2

    sources = sorted(SRC_DIR.glob("*.svg"))
    plan: list[tuple[Path, str, str]] = []
    skipped: list[str] = []
    for svg in sources:
        hit = classify(svg)
        if hit is None:
            skipped.append(svg.name)
            continue
        folder, stem = hit
        if args.only and args.only.lower() not in folder.lower() and args.only.lower() not in stem:
            continue
        plan.append((svg, folder, stem))

    if args.list:
        for svg, folder, stem in plan:
            print(f"{folder:9s} {svg.name:34s} → {stem}.png")
        if skipped:
            print(f"未分类（跳过）: {', '.join(skipped)}")
        print(f"合计 {len(plan)} 个")
        return 0

    if not plan:
        print("没有匹配的图标", file=sys.stderr)
        return 1

    by_folder: dict[str, int] = {}
    preview_rows: list[tuple[str, Image.Image]] = []
    with tempfile.TemporaryDirectory(prefix="p1-icons-") as tmp:
        workdir = Path(tmp)
        for svg, folder, stem in plan:
            dst_dir = args.out_dir / folder
            dst_dir.mkdir(parents=True, exist_ok=True)
            dst = dst_dir / f"{stem}.png"

            raw = render(svg, args.size, workdir)
            white = pad_square(to_white(raw, args.cut), args.size)
            white.save(dst, optimize=True)

            by_folder[folder] = by_folder.get(folder, 0) + 1
            preview_rows.append((f"{folder}/{stem}", white))

    for folder in CATEGORIES:
        if folder in by_folder:
            print(f"  {folder:9s} {by_folder[folder]:2d} 个 → {args.out_dir / folder}")
    total = sum(by_folder.values())
    print(f"DONE: {total} 个图标 @{args.size}px → {args.out_dir}")

    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        contact_sheet(preview_rows, args.preview)
        print(f"PREVIEW: {args.preview}")
    return 0


if __name__ == "__main__":
    sys.exit(main())