#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""build_site.py — 构建墨水大作战介绍页到 site/dist/

做的事：
  1. 从 Assets/_Project/Scripts/Core/GameVersion.cs 读 APP_VERSION
  2. 拷贝 Logs/ 精选截图 → WebP（og 分享图另裁 1200x630 PNG）
  3. 拷贝品牌/武器/大招/副武器/规则图标
  4. 按页面实际用到的字符集子集化 MiSans 字体 → woff2
  5. site/video/*.mp4（若存在）拷入产物
  6. 把 site/{index.html,styles.css,app.js} 的 __V__ / __APP_VERSION__ 令牌替换后写入 dist

用法：python3 tools/build_site.py
依赖：Pillow、fontTools（含 brotli，用于 woff2）。
"""

import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SITE = REPO / "site"
DIST = SITE / "dist"
IMG = DIST / "img"
FONTS = DIST / "fonts"
VIDEO = DIST / "video"
PLAY = DIST / "play"
PLAY_SRC = REPO / "Builds/WebGL"
WEBGL_TEMPLATE = REPO / "Assets/WebGLTemplates/InkTurf/index.html"

GAMEVERSION_CS = REPO / "Assets/_Project/Scripts/Core/GameVersion.cs"
LOGS = REPO / "Logs"
ART = REPO / "Assets/_Project/Art"
MEDIA_FONTS = REPO / "MediaRaw/fonts"


def fail(msg: str) -> None:
    print(f"[build_site] 错误：{msg}", file=sys.stderr)
    sys.exit(1)


def read_version() -> str:
    if not GAMEVERSION_CS.exists():
        fail(f"找不到 {GAMEVERSION_CS}")
    m = re.search(r'APP_VERSION\s*=\s*"([\d.]+)"', GAMEVERSION_CS.read_text(encoding="utf-8"))
    if not m:
        fail("GameVersion.cs 中解析不到 APP_VERSION")
    return m.group(1)


def build_version_token(app_version: str) -> str:
    return f"{app_version}.{int(time.time()) % 100000}"


# ---------- 图片 ----------

def copy_screenshots() -> int:
    try:
        from PIL import Image
    except ImportError:
        fail("缺少 Pillow，请先：python3 -m pip install Pillow")

    shots = {
        # 源（Logs/）            目标文件名            最大宽
        "p5a_weapons_arsenal.png": ("arsenal.webp", 1920),
        "p7b_overview.png": ("shot_overview.webp", 1600),
        "quality_p31_combat.png": ("shot_combat.webp", 1600),
    }
    n = 0
    for src_name, (dst_name, max_w) in shots.items():
        src = LOGS / src_name
        if not src.exists():
            fail(f"截图缺失：{src}（Logs/ 不进 git，请确认本地有截图）")
        im = Image.open(src).convert("RGB")
        if im.width > max_w:
            im = im.resize((max_w, int(im.height * max_w / im.width)), Image.LANCZOS)
        im.save(IMG / dst_name, "WEBP", quality=82, method=6)
        n += 1

    # Hero 海报：有宣传视频就从视频抽一帧（画面与视频首帧一致），否则退回实机截图
    poster_video = SITE / "video/promo.av1.mp4"
    if not poster_video.exists():
        poster_video = SITE / "video/promo.mp4"
    if poster_video.exists() and shutil.which("ffmpeg"):
        tmp_frame = IMG / "_poster_frame.png"
        subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                        "-ss", "3", "-i", str(poster_video), "-frames:v", "1", str(tmp_frame)],
                       check=True)
        frame = Image.open(tmp_frame).convert("RGB")
        frame.save(IMG / "hero_poster.webp", "WEBP", quality=88, method=6)
        tmp_frame.unlink()
    else:
        fallback = LOGS / "quality_p31_game.png"
        if fallback.exists():
            im = Image.open(fallback).convert("RGB")
            im.save(IMG / "hero_poster.webp", "WEBP", quality=82, method=6)
        else:
            fail("既没有宣传视频也没有 Hero 备用截图")
    n += 1

    # og 分享图：1200x630 居中裁切（社交爬虫不认 webp，用 PNG）
    og_src = Image.open(LOGS / "quality_p31_game.png").convert("RGB")
    tw, th = 1200, 630
    scale = max(tw / og_src.width, th / og_src.height)
    og = og_src.resize((int(og_src.width * scale), int(og_src.height * scale)), Image.LANCZOS)
    x = (og.width - tw) // 2
    y = (og.height - th) // 2
    og.crop((x, y, x + tw, y + th)).save(IMG / "og.png", "PNG", optimize=True)
    n += 1

    # 品牌图标
    icon = Image.open(ART / "Brand/app_icon_1024.png").convert("RGBA")
    icon.resize((256, 256), Image.LANCZOS).save(IMG / "app_icon.png", "PNG", optimize=True)
    icon.resize((64, 64), Image.LANCZOS).save(IMG / "favicon-64.png", "PNG", optimize=True)
    n += 2
    return n


def copy_ui_icons() -> int:
    n = 0
    for sub in ("Weapons", "Specials", "Subs", "Rules"):
        src_dir = ART / "Sprites/Icons" / sub
        if not src_dir.exists():
            fail(f"图标目录缺失：{src_dir}")
        for png in sorted(src_dir.glob("*.png")):
            shutil.copy2(png, IMG / png.name)
            n += 1
    return n


# ---------- 字体子集 ----------

def collect_text() -> str:
    raw = ""
    for f in ("index.html", "styles.css", "app.js", "privacy.html", "terms.html"):
        raw += (SITE / f).read_text(encoding="utf-8")
    # 试玩页（Unity 模板）也用 MiSans，字符集必须一起子集化，否则会显示方框
    if WEBGL_TEMPLATE.exists():
        raw += WEBGL_TEMPLATE.read_text(encoding="utf-8")
    # 页面实际字符 = 全部可见 ASCII + 文中出现的非 ASCII（汉字等）
    chars = {c for c in raw if ord(c) > 127}
    chars.update(chr(c) for c in range(0x20, 0x7F))
    chars.update("：；，。！？、（）《》「」—…·％→0123456789")
    return "".join(sorted(chars))


def subset_fonts() -> int:
    try:
        from fontTools import subset
    except ImportError:
        fail("缺少 fontTools，请先：python3 -m pip install fontTools brotli")

    text = collect_text()
    opts = subset.Options()
    opts.flavor = "woff2"
    opts.name_IDs = ["*"]
    opts.layout_features = ["*"]
    n = 0
    for name in ("MiSans-Heavy", "MiSans-Semibold"):
        src = MEDIA_FONTS / f"{name}.woff2"
        if not src.exists():
            fail(f"字体缺失：{src}")
        font = subset.load_font(str(src), opts)
        ss = subset.Subsetter(opts)
        ss.populate(text=text)
        ss.subset(font)
        font.save(str(FONTS / f"{name}.woff2"))
        n += 1
    return n


# ---------- HTML/CSS/JS 令牌替换 ----------

def stamp_sources(build_v: str, app_version: str) -> None:
    for f in ("index.html", "styles.css", "app.js", "privacy.html", "terms.html"):
        text = (SITE / f).read_text(encoding="utf-8")
        text = text.replace("__APP_VERSION__", app_version).replace("__V__", build_v)
        (DIST / f).write_text(text, encoding="utf-8")


def copy_video() -> int:
    src_dir = SITE / "video"
    VIDEO.mkdir(parents=True, exist_ok=True)
    n = 0
    if src_dir.exists():
        for v in sorted(src_dir.glob("*.mp4")) + sorted(src_dir.glob("*.webm")):
            shutil.copy2(v, VIDEO / v.name)
            n += 1
    return n


def copy_webgl_build() -> tuple[int, int]:
    entry = PLAY_SRC / "index.html"
    if not entry.exists():
        print("[build_site] 警告：找不到 Builds/WebGL/index.html，本次产物不含 /play/。"
              "（先出 WebGL 包；部署脚本会阻止覆盖线上已有的 /play/）")
        return 0, 0

    shutil.copytree(PLAY_SRC, PLAY,
                    ignore=shutil.ignore_patterns("unity-build.provenance.json"))

    newest_src = 0.0
    for root in (REPO / "Assets/_Project/Scripts", REPO / "Assets/_Project/Scenes",
                 REPO / "Assets/WebGLTemplates"):
        if not root.exists():
            continue
        for p in root.rglob("*"):
            if p.is_file() and p.suffix in (".cs", ".unity", ".html", ".css", ".js"):
                newest_src = max(newest_src, p.stat().st_mtime)
    if entry.stat().st_mtime < newest_src:
        print("[build_site] 警告：Builds/WebGL 比脚本/场景/模板旧，"
              "可能不是最新构建；建议先重新出 WebGL 包。")

    files = [p for p in PLAY.rglob("*") if p.is_file()]
    return len(files), sum(p.stat().st_size for p in files)


def main() -> None:
    app_version = read_version()
    build_v = build_version_token(app_version)

    if DIST.exists():
        shutil.rmtree(DIST)
    IMG.mkdir(parents=True)
    FONTS.mkdir(parents=True)

    n_shots = copy_screenshots()
    n_icons = copy_ui_icons()
    n_fonts = subset_fonts()
    stamp_sources(build_v, app_version)
    n_video = copy_video()
    n_play, play_bytes = copy_webgl_build()
    (DIST / ".nojekyll").write_text("", encoding="utf-8")

    total = sum(p.stat().st_size for p in DIST.rglob("*") if p.is_file())
    print(f"[build_site] 完成 → {DIST.relative_to(REPO)}/")
    print(f"  版本 v{app_version}（缓存令牌 {build_v}）")
    print(f"  图片 {n_shots + n_icons} 个 · 字体 {n_fonts} 个 · 视频 {n_video} 个 · "
          f"WebGL {n_play} 个文件（{play_bytes / 1024 / 1024:.1f} MB） · "
          f"总计 {total / 1024 / 1024:.1f} MB")
    if n_video == 0:
        print("  提示：site/video/ 还没有宣传视频，页面将显示海报占位。")


if __name__ == "__main__":
    main()
