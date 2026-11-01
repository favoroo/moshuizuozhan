#!/usr/bin/env python3
"""把 Inkwave 调研库的 SVG 图标烘焙成 Unity 可用的「粗黑描边 + 纯白主体 + 透明」UI 精灵。

为什么需要这个脚本：
- 调研库的 SVG 是**彩色**的（深色描边 + 橙色主体 + 浅色填充），而 P1 的图标必须是
  **白主体 + alpha**，队伍颜色由 Unity 的 `Image.color` 运行时染色（一套图标服务两队）。
- 调研库有 27 个 SVG 带**重复属性**（`stroke-width` 写了两次），标准 XML 解析器直接报
  `Attribute stroke-width redefined`，必须先做去重消毒。
- 直接把彩色图平铺成白色剪影会丢掉内部结构（描边与填充融成一团），
  这里用「亮度 → alpha」的二值映射（`--cut dark`，默认）：深色描边/暗部挖成透明，
  中亮主体与高光填成实心白，边缘保留抗锯齿过渡 —— 齿轮的孔、瞳孔、枪身分件都还在。

**128px 粗描边（2026-10-31 重做）**：512px 源缩到 20~48px 显示会糊成一团（细节线只剩
亚像素宽度，Unity 双线性再把它们磨平）。现在按 HUD/选枪界面真实显示尺寸（28~85px）
出片，并在剪影外围补一圈**实心墨黑描边**（默认 128px 下 6px）：
- 描边直接用 `#15121C`（STYLE.md 的 UI 墨色）烘进 RGB：染色是 `Image.color` 乘法，
  墨色 × 任何队伍色仍然是深墨色，所以描边在任何队伍色下都保持可见。
- 描边只加在**外轮廓**外侧；图标内部的孔洞/瞳孔/分件仍然是 alpha 镂空，
  这样白主体、墨描边、镂空细节三层在浅底/深底/队伍色底上都能读出来。
- 白主体仍是纯白 RGB —— 队伍染色语义与旧版完全一致。

依赖：`brew install librsvg`（提供 rsvg-convert）+ `numpy` + `Pillow`。
（numpy 在 `bake_arena_textures.py` / `bake_prop_surfaces.py` 里早已是项目依赖。）

用法：
    python3 tools/bake_icons.py                  # 全量 66 个 @128 → Assets/_Project/Art/Sprites/Icons/
    python3 tools/bake_icons.py --list           # 看图标清单与目标路径
    python3 tools/bake_icons.py --only weapon    # 只处理某一类（类名或文件名关键字）
    python3 tools/bake_icons.py --size 256       # 换输出边长（描边按 --size/128 等比缩放）
    python3 tools/bake_icons.py --outline 0      # 关掉描边，回到旧的纯白剪影
    python3 tools/bake_icons.py --size 512       # 需要高分辨率源稿时（描边自动变 24px）
    python3 tools/bake_icons.py --out-dir /tmp/x # 换输出根目录（调试用，不碰 Assets）
    python3 tools/bake_icons.py --preview /tmp/contact_sheet.png   # 另出缩略图自查

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

import numpy as np
from PIL import Image, ImageDraw

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

# 128 是「显示尺寸的 1.5~4 倍」：HUD/选枪界面真实图标显示 28~85px，
# 128 留了余量，同时比 512 少 16 倍像素、缩小采样时不再糊。
# 想重新出 512 源稿：--size 512 --outline 24。
DEFAULT_SIZE = 128

# 描边宽度（按 128px 输出计），随 --size 等比缩放。
DEFAULT_OUTLINE = 6.0

# 描边之外再留的透明边距，防止描边贴着图片边缘被裁。
DEFAULT_PAD = 3.0

# STYLE.md 的 UI 墨色；也是 Inkwave 图标的原描边色差一档。
INK_RGB = (0x15, 0x12, 0x1C)

# 内部按超采样合成，再 LANCZOS 缩到目标尺寸 —— 描边和 alpha 边缘都拿到干净 AA。
# 上限 1024：128px 出片内部跑 512（4x），256/512px 出片自动降到 2x/1x，不至于慢成几分钟。
MAX_WORK_SIZE = 1024
SUPERSAMPLE = 4


def work_size(size: int) -> int:
    return min(size * SUPERSAMPLE, MAX_WORK_SIZE)


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


def fill_holes(mask: Image.Image) -> Image.Image:
    """L 模式 0/255 蒙版 → 填掉所有内部孔洞的实心剪影（只认「与画面四边连通」为外部）。"""
    w, h = mask.size
    # 反转后背景=255；从四角洪水填充标记外圈，剩下的 255 就是内部孔洞
    inv = mask.point(lambda v: 255 - v)
    for xy in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        if inv.getpixel(xy) == 255:
            ImageDraw.floodfill(inv, xy, 128)
    exterior = inv.point(lambda v: 255 if v == 128 else 0)
    return exterior.point(lambda v: 255 - v)


def distance_to_solid(mask: Image.Image) -> np.ndarray:
    """每个像素到最近「实心像素」的距离（3-4 chamfer 近似欧氏，误差 <2%）。

    为什么不用 PIL 的 MaxFilter：方形核在 45° 斜边上会把描边撑粗约 40%，
    三角/星形图标的描边会显得忽粗忽细。
    """
    solid = np.asarray(mask, dtype=bool)
    h, w = solid.shape
    inf = np.float32(1e6)
    d = np.where(solid, np.float32(0.0), inf).astype(np.float32)

    a, b = np.float32(3.0), np.float32(4.0)
    idx = np.arange(w, dtype=np.float32)

    def row_pass(rows, cols_from_right: bool):
        prev = None
        for i in rows:
            row = d[i]
            if cols_from_right:
                row = np.minimum(row, np.minimum.accumulate((row + a * idx)[::-1])[::-1] - a * idx)
            else:
                row = np.minimum(row, np.minimum.accumulate(row - a * idx) + a * idx)
            if prev is not None:
                shift_l = np.full(w, inf, np.float32)
                shift_r = np.full(w, inf, np.float32)
                shift_l[1:] = prev[:-1] + b
                shift_r[:-1] = prev[1:] + b
                row = np.minimum(row, np.minimum(prev + a, np.minimum(shift_l, shift_r)))
                if cols_from_right:
                    row = np.minimum(row, np.minimum.accumulate((row + a * idx)[::-1])[::-1] - a * idx)
                else:
                    row = np.minimum(row, np.minimum.accumulate(row - a * idx) + a * idx)
            d[i] = row
            prev = row

    for _ in range(2):
        row_pass(range(h), cols_from_right=False)
        row_pass(range(h - 1, -1, -1), cols_from_right=True)

    return d / a


def bake_icon(svg: Path, size: int, workdir: Path, cut: str = "dark",
              outline: float = DEFAULT_OUTLINE, pad: float = DEFAULT_PAD) -> Image.Image:
    """单个图标 → size×size RGBA（白主体 + 外轮廓墨描边 + 镂空细节）。"""
    hi = work_size(size)
    unit = hi / DEFAULT_SIZE          # 描边/边距都按 128px 标称值定义，内部等比换算
    outline_px = outline * unit
    pad_px = pad * unit

    # 内容占位扣掉描边与边距，保证描边永远在画布内
    inner = hi - 2 * int(round(outline_px + pad_px))
    if inner < 8:
        raise ValueError(f"描边/边距太大：size={size} outline={outline} pad={pad}")

    raw = render(svg, inner, workdir)
    canvas = Image.new("RGBA", (hi, hi), (255, 255, 255, 0))
    canvas.alpha_composite(raw, ((hi - raw.width) // 2, (hi - raw.height) // 2))

    body = to_white(canvas, cut)
    body_a = np.asarray(body.getchannel("A"), dtype=np.float32) / 255.0

    if outline_px < 1.0:
        ring_a = np.zeros_like(body_a)
    else:
        # 剪影取「白主体」而不是完整原作：原作自带一圈深色描边（已被挖空），
        # 若按完整轮廓加环，主体与环之间会留一条缝，形成双线。
        solid = fill_holes(body.getchannel("A").point(lambda v: 255 if v >= 96 else 0))
        dist = distance_to_solid(solid)
        ring_a = np.clip((outline_px + 0.5) - dist, 0.0, 1.0)
        ring_a[np.asarray(solid, dtype=bool)] = 0.0

    # 白主体画在墨环之上：out = body over ring
    out_a = body_a + ring_a * (1.0 - body_a)
    ink = np.array(INK_RGB, dtype=np.float32) / 255.0
    # 预乘 alpha 后再缩小：透明区域的 RGB 不会污染边缘（否则深色描边会脏边）
    pre_rgb = np.ones((hi, hi, 3), np.float32) * body_a[..., None] \
        + ink[None, None, :] * (ring_a * (1.0 - body_a))[..., None]
    if (hi, hi) != (size, size):
        pre_rgb = _resize_float(pre_rgb, size)
        out_a = _resize_float(out_a, size)

    out_a = np.clip(out_a, 0.0, 1.0)
    safe = np.maximum(out_a, 1e-6)[..., None]
    rgb = np.clip(pre_rgb / safe, 0.0, 1.0)
    rgb[out_a <= 1e-3] = 1.0   # 全透明像素先留白

    rgba = np.dstack([rgb, out_a])
    im = Image.fromarray((rgba * 255.0 + 0.5).astype(np.uint8))
    return _bleed_rgb(im)


def _bleed_rgb(im: Image.Image, passes: int = 4) -> Image.Image:
    """把不透明像素的 RGB 往透明区域外扩几圈（color bleed）。

    描边是深墨色、外侧全透明：双线性采样 / BC7-ETC2 压缩会让透明像素的 RGB 渗进
    描边边缘，白 RGB 会在深色描边外冒一圈白雾。把 RGB 外扩成「最近的不透明颜色」
    后，任何采样都只会取到墨色，边缘保持干净。
    （Unity 的 Alpha Is Transparency 也会做一次 dilation，这里是双保险。）
    """
    arr = np.asarray(im).astype(np.float32) / 255.0
    rgb, a = arr[..., :3].copy(), arr[..., 3].copy()
    known = a > 1e-3
    for _ in range(passes):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                src_rgb = np.roll(np.roll(rgb, dy, axis=0), dx, axis=1)
                src_known = np.roll(np.roll(known, dy, axis=0), dx, axis=1)
                acc += src_rgb * src_known[..., None]
                cnt += src_known
        newly = (~known) & (cnt > 0)
        rgb[newly] = acc[newly] / cnt[newly][..., None]
        known = known | newly

    out = np.dstack([rgb, a])
    return Image.fromarray((np.clip(out, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8))


def _resize_float(arr: np.ndarray, size: int) -> np.ndarray:
    """float32 单通道/多通道数组 LANCZOS 缩放到 size×size（逐通道走 Pillow F 模式）。"""
    if arr.ndim == 2:
        return np.asarray(Image.fromarray(arr.astype(np.float32)).resize((size, size), Image.LANCZOS))
    chans = [
        np.asarray(Image.fromarray(arr[..., c].astype(np.float32)).resize((size, size), Image.LANCZOS))
        for c in range(arr.shape[2])
    ]
    return np.dstack(chans)


def contact_sheet(rows: list[tuple[str, Image.Image]], out: Path, cell: int = 96) -> None:
    """缩略图拼版：中灰底，白主体与墨描边都能看清（纯白/纯黑底必有一层看不见）。"""
    cols = 11
    pad = 8
    label_h = 14
    sheet = Image.new(
        "RGBA",
        (cols * (cell + pad) + pad, ((len(rows) + cols - 1) // cols) * (cell + pad + label_h) + pad),
        (150, 150, 158, 255),
    )
    for i, (name, im) in enumerate(rows):
        col, row = i % cols, i // cols
        thumb = im.copy()
        thumb.thumbnail((cell, cell), Image.LANCZOS)
        x = pad + col * (cell + pad) + (cell - thumb.width) // 2
        y = pad + row * (cell + pad + label_h) + (cell - thumb.height) // 2
        sheet.alpha_composite(thumb, (x, y))
    sheet.convert("RGB").save(out, optimize=True)


def main() -> int:
    ap = argparse.ArgumentParser(description="SVG 图标 → Unity 白主体+墨描边透明精灵")
    ap.add_argument("--size", type=int, default=DEFAULT_SIZE, help=f"输出边长（默认 {DEFAULT_SIZE}）")
    ap.add_argument("--outline", type=float, default=DEFAULT_OUTLINE,
                    help=f"外轮廓墨色描边宽度（按 {DEFAULT_SIZE}px 计，随 --size 缩放；0 = 关闭）")
    ap.add_argument("--pad", type=float, default=DEFAULT_PAD,
                    help=f"描边外的透明边距（按 {DEFAULT_SIZE}px 计，默认 {DEFAULT_PAD}）")
    ap.add_argument("--out-dir", type=Path, default=DEFAULT_OUT, help="输出根目录")
    ap.add_argument("--only", help="只处理匹配关键字（分类目录名或文件名）")
    ap.add_argument("--list", action="store_true", help="只列清单不烘焙")
    ap.add_argument("--preview", type=Path, help="另出缩略图到该路径")
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

            icon = bake_icon(svg, args.size, workdir, args.cut, args.outline, args.pad)
            icon.save(dst, optimize=True)

            by_folder[folder] = by_folder.get(folder, 0) + 1
            preview_rows.append((f"{folder}/{stem}", icon))

    for folder in CATEGORIES:
        if folder in by_folder:
            print(f"  {folder:9s} {by_folder[folder]:2d} 个 → {args.out_dir / folder}")
    total = sum(by_folder.values())
    print(f"DONE: {total} 个图标 @{args.size}px outline={args.outline} → {args.out_dir}")

    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        contact_sheet(preview_rows, args.preview)
        print(f"PREVIEW: {args.preview}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
