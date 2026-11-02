#!/usr/bin/env python3
"""烘焙 P1 品牌资产：App 图标、Android 自适应图标、Unity 启动画面 logo、启动动画贴纸。

为什么单独一个脚本：
- App 图标必须走 **Default 纹理**（PlayerSettings 只吃 Texture2D，Sprite 导入会把边长
  钳到 512），而游戏内 UI 用的乌贼/墨点需要 Sprite —— 两套导入策略无法共用一张图。
- 图标构图就是「几何大胆风」的三件套：墨黑底 + 正圆 + 硬描边剪影，
  直接用调研库的 SQUID / SPLAT_ICON 矢量重上色，比生图更可控、可复现、零配额。
- 所有尺寸都从 2048 母版 Lanczos 缩小，避免 128px 源放大后描边发虚。

依赖：`brew install librsvg`（rsvg-convert）+ `numpy` + `Pillow`（与 bake_icons.py 一致）。

用法：
    python3 tools/bake_brand.py                 # 全量烘焙到 Assets/_Project/Art/{Brand,Sprites/Brand}
    python3 tools/bake_brand.py --out-dir /tmp/x # 调试：不碰 Assets
    python3 tools/bake_brand.py --preview /tmp/brand_sheet.png

产物（确定性，同一输入 → 同一字节）：
    Art/Brand/app_icon_1024.png                    主图标（墨黑底 + 橙圆 + 白乌贼）
    Art/Brand/android_adaptive_background_432.png  自适应背景层（墨黑 + 橙圆）
    Art/Brand/android_adaptive_foreground_432.png  自适应前景层（透明 + 乌贼/墨点，居中安全区）
    Art/Brand/app_icon_round_512.png               圆形图标（部分启动器/通知用）
    Art/Sprites/Icons/Brand/brand_app_icon.png           启动动画贴纸（完整方形图标，Sprite）
    Art/Sprites/Icons/Brand/brand_squid.png              透明乌贼（彩色，Sprite）
    Art/Sprites/Icons/Brand/brand_splat.png              透明墨点（白主体 + 墨描边，运行时染色）
    Art/Sprites/Icons/Brand/splash_logo.png              Unity 启动画面 logo（透明圆标，Sprite）
"""

from __future__ import annotations

import argparse
import io
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
BRAND_SRC = ROOT / "inkwave-aah-research" / "assets" / "icons"
DEFAULT_ART_BRAND = ROOT / "Assets" / "_Project" / "Art" / "Brand"
DEFAULT_SPRITE_BRAND = ROOT / "Assets" / "_Project" / "Art" / "Sprites" / "Icons" / "Brand"

# 项目色板（Docs/UI-GeometricBold.md）：只用这些值，禁止即兴调色。
INK = "#141414"
STAGE = "#0B0B0B"
TEAM_A = "#FF700F"
TEAM_B = "#266BFF"
PAPER = "#FFFFFF"
YELLOW = "#FFD400"

MASTER = 2048


def sanitize_svg(text: str) -> str:
    """去掉同一标签里重复的属性（调研库 SQUID.svg 的 stroke-width 写了两遍）。"""
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
            merged[key] = value
        built = "<" + ("/" if closing else "") + name
        for key, value in merged.items():
            built += " " + key + "=" + value
        built += " />" if self_closing else ">"
        return built

    return re.sub(r"<[^>]+>", lambda m: fix_tag(m.group(0)), text)


def render_svg(svg_text: str, size: int) -> Image.Image:
    svg_text = sanitize_svg(svg_text)
    svg_text = svg_text.replace("<svg ", f'<svg width="{size}" height="{size}" ', 1)
    proc = subprocess.run(
        ["rsvg-convert", "-f", "png", "-w", str(size), "-h", str(size)],
        input=svg_text.encode("utf-8"),
        capture_output=True,
    )
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.decode("utf-8", "replace"))
    return Image.open(io.BytesIO(proc.stdout)).convert("RGBA")


def load_source(name: str) -> str:
    path = BRAND_SRC / name
    if not path.exists():
        raise FileNotFoundError(f"缺少矢量源：{path}")
    return path.read_text(encoding="utf-8")


def squash_svg(body: str, eye: str, pupil: str, outline: str) -> str:
    """给 SQUID.svg 换色。注意替换顺序：先主体、再眼白、最后瞳孔/描边。"""
    text = load_source("SQUID.svg")
    text = text.replace("#ff8a14", body)
    text = text.replace('fill="#fff"', f'fill="{eye}"')
    text = text.replace('fill="#15121c"', f'fill="{pupil}"')
    text = text.replace('stroke="#15121c"', f'stroke="{outline}"')
    return text


def splat_svg(fill: str, outline: str) -> str:
    text = load_source("SPLAT_ICON.svg")
    text = text.replace("#ff8a14", fill)
    text = text.replace('stroke="#15121c"', f'stroke="{outline}"')
    # 源 viewBox 是 0 0 64 64，但四颗小墨滴的圆心在 x=-0.6 / 64.7，出框后被裁成直角。
    # 扩到 -6..70，让小墨滴完整落进透明画布；下面的尺寸已按 76/64 补偿。
    text = text.replace('viewBox="0 0 64 64"', 'viewBox="-6 -6 76 76"')
    return text


# SPLAT_ICON 原有 64 单位 + 6 单位四周留白 = 76；补偿系数让小墨滴不再被切。
SPLAT_PAD = 76 / 64


def place(canvas: Image.Image, sprite: Image.Image, size: int, center: tuple[float, float],
          angle: float = 0.0) -> None:
    """以画布比例坐标 (0~1) 居中贴图；旋转后保持完整画布，避免裁掉墨点滴。"""
    img = sprite.resize((size, size), Image.LANCZOS)
    if angle:
        img = img.rotate(angle, resample=Image.BICUBIC, expand=False)
    x = int(round(canvas.width * center[0] - size / 2))
    y = int(round(canvas.height * center[1] - size / 2))
    canvas.alpha_composite(img, (x, y))


def draw_circle(canvas: Image.Image, color: str, diameter: int, center: tuple[float, float]) -> None:
    draw = ImageDraw.Draw(canvas)
    x = canvas.width * center[0]
    y = canvas.height * center[1]
    r = diameter / 2
    draw.ellipse([x - r, y - r, x + r, y + r], fill=color)


def compose_icon(size: int = MASTER) -> Image.Image:
    """主图标：墨黑底 + 橙正圆 + 白乌贼 + 右上钴蓝墨点。"""
    canvas = Image.new("RGBA", (size, size), STAGE)

    # 钴蓝墨点压在右上，整枚留在画布内（自适应版会另裁，安全区见 foreground）。
    place(canvas, render_svg(splat_svg(TEAM_B, INK), size), int(size * 0.32 * SPLAT_PAD),
          (0.785, 0.205), 16)

    # 橙正圆略偏下，给乌贼的触手留呼吸位。
    draw_circle(canvas, TEAM_A, int(size * 0.755), (0.5, 0.495))

    # 白乌贼：白眼 + 墨黑瞳孔，描边即 UI 的墨线。
    place(canvas, render_svg(squash_svg(PAPER, TEAM_A, INK, INK), size), int(size * 0.59),
          (0.5, 0.49))
    return canvas


def compose_adaptive_foreground(size: int = 432) -> Image.Image:
    """自适应前景：透明底，所有图形落在中心 66.7% 安全圆内（Android 规范）。"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    # 安全区直径 = size * 2/3；乌贼取安全区的 88%，墨点贴安全区右上。
    safe = size * 2 / 3
    place(canvas, render_svg(splat_svg(TEAM_B, INK), size), int(safe * 0.30 * SPLAT_PAD),
          (0.5 + safe / size * 0.30, 0.5 - safe / size * 0.30), 16)
    place(canvas, render_svg(squash_svg(PAPER, TEAM_A, INK, INK), size), int(safe * 0.90),
          (0.5, 0.5))
    return canvas


def compose_adaptive_background(size: int = 432) -> Image.Image:
    """自适应背景：墨黑底 + 橙圆（直径略大于安全区，遮罩后仍是一整块橙）。"""
    canvas = Image.new("RGBA", (size, size), STAGE)
    draw_circle(canvas, TEAM_A, int(size * 0.78), (0.5, 0.5))
    return canvas


def compose_round_icon(size: int = 512) -> Image.Image:
    """圆形图标：主图标裁成圆，圆外透明。"""
    icon = compose_icon(MASTER).resize((size, size), Image.LANCZOS)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, size - 1, size - 1], fill=255)
    icon.putalpha(mask)
    return icon


def compose_splash_logo(size: int = 512) -> Image.Image:
    """Unity 启动画面 logo：透明底上的橙圆 + 白乌贼 + 蓝墨点（黑底上浮起一枚圆标）。"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    place(canvas, render_svg(splat_svg(TEAM_B, INK), size), int(size * 0.32 * SPLAT_PAD),
          (0.80, 0.20), 16)
    draw_circle(canvas, TEAM_A, int(size * 0.78), (0.5, 0.5))
    place(canvas, render_svg(squash_svg(PAPER, TEAM_A, INK, INK), size), int(size * 0.60),
          (0.5, 0.5))
    return canvas


def compose_squid_only(size: int = 512) -> Image.Image:
    """透明乌贼：给启动动画/宣传位单独染色用（不改图标构图）。"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    place(canvas, render_svg(squash_svg(PAPER, TEAM_A, INK, INK), size), size, (0.5, 0.5))
    return canvas


def compose_splat_only(size: int = 512) -> Image.Image:
    """透明墨点（白主体 + 墨描边）：启动动画里用 Image.color 染成队色。"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    place(canvas, render_svg(splat_svg(PAPER, INK), size), size, (0.5, 0.5))
    return canvas


def to_rgb(canvas: Image.Image, background: str = STAGE) -> Image.Image:
    """App 图标不允许透明：把半透明边缘压到实色底上，避免商店审核与黑边。"""
    bg = Image.new("RGBA", canvas.size, background)
    bg.alpha_composite(canvas)
    return bg.convert("RGB")


def save(canvas: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(path, optimize=True)
    print(f"  {path.relative_to(ROOT) if path.is_relative_to(ROOT) else path}  {canvas.size[0]}x{canvas.size[1]}")


def bake(art_dir: Path, sprite_dir: Path) -> None:
    icon = compose_icon(MASTER)

    print("烘焙品牌资产：")
    save(to_rgb(icon.resize((1024, 1024), Image.LANCZOS)), art_dir / "app_icon_1024.png")
    save(to_rgb(compose_adaptive_background(432)), art_dir / "android_adaptive_background_432.png")
    save(compose_adaptive_foreground(432), art_dir / "android_adaptive_foreground_432.png")
    save(compose_round_icon(512), art_dir / "app_icon_round_512.png")

    save(icon.resize((512, 512), Image.LANCZOS), sprite_dir / "brand_app_icon.png")
    save(compose_squid_only(512), sprite_dir / "brand_squid.png")
    save(compose_splat_only(512), sprite_dir / "brand_splat.png")
    save(compose_splash_logo(512), sprite_dir / "splash_logo.png")


def preview(path: Path) -> None:
    """自查拼图：白/黑两底 + 真实小尺寸（48/64/96）。"""
    icon = to_rgb(compose_icon(1024))
    sheet = Image.new("RGB", (4 * 1044 + 20, 1024 + 220), (32, 32, 32))
    for i, (label, im) in enumerate([
        ("icon", icon),
        ("round", to_rgb(compose_round_icon(1024), PAPER)),
        ("adaptive-fg", to_rgb(compose_adaptive_foreground(1024), TEAM_A)),
        ("splash-logo", to_rgb(compose_splash_logo(1024))),
    ]):
        sheet.paste(im, (i * 1044 + 20, 0))
        for j, s in enumerate((48, 64, 96)):
            sheet.paste(im.resize((s, s), Image.LANCZOS), (i * 1044 + 40 + j * 120, 1050))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)
    print(f"预览图: {path}")


def main() -> int:
    parser = argparse.ArgumentParser(description="烘焙 P1 品牌图标与启动画面资产")
    parser.add_argument("--out-dir", type=Path, default=None, help="覆盖 Art/Brand 输出目录（调试用）")
    parser.add_argument("--sprite-dir", type=Path, default=None, help="覆盖 Art/Sprites/Brand 输出目录")
    parser.add_argument("--preview", type=Path, default=None, help="另出一张自查拼图")
    args = parser.parse_args()

    art_dir = args.out_dir or DEFAULT_ART_BRAND
    sprite_dir = args.sprite_dir or DEFAULT_SPRITE_BRAND

    try:
        bake(art_dir, sprite_dir)
        if args.preview:
            preview(args.preview)
    except (RuntimeError, FileNotFoundError) as exc:
        print(f"FAILED: {exc}", file=sys.stderr)
        return 1
    print("DONE")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
