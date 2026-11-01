#!/usr/bin/env python3
"""生成 MiSans 预烘字集账本：tools/font_charset.txt + tools/font_exempt_chars.txt。

为什么需要这个脚本：
- TMP 的动态字体图集是**运行时**按需栅格化的：图集尺寸、笔画清晰度、首帧耗时都由
  「采样点大小 + 已烘进图集的字形」决定。采样点从 48pt 提到 64/96pt 后，必须把 UI 真的会
  用到的字**在构建期就烘进图集**，否则新文案首次出现时仍会现场栅格化（真机卡帧）。
- 但「把 GB2312 或整份 MiSans 全烘进去」会白烧十几 MB 显存：项目真正常用的字远少于字库。
  这里按 godot/t-1 `tools/subset_fonts.py` 的口径立一份**可增量维护的字集账本**：
  只有出现在源码字符串字面量里的字（+ ASCII + 项目符号表）才进图集。
- 账本一旦漂移就会「静默变糊/变方块」（新增文案里的生僻字没人管），所以：
  * 本脚本写账本；`Assets/_Project/Tests/EditMode/FontCoverageTests.cs` 现场重扫源码验收；
    两边的扫描口径（扩展名 / 目录 / 是否只看字面量）**必须一致，改一边要改两边**。
  * 豁免账（MiSans 渲染不了、只能靠系统回退字体的字）只许进注释，不许进上屏文案：
    真机上系统回退字体的动态加载会让整层 UI 当帧重排（画面集体闪一下）。
    新增豁免字必须人拍板，故有 `--accept-exempt` 门禁。

用法：
    python3 tools/font_charset.py                  # 扫源码 → 写两份账本
    python3 tools/font_charset.py --check          # 只比对，不写盘；漂移则退出码 1
    python3 tools/font_charset.py --accept-exempt  # 确认并接受新的豁免字
    python3 tools/font_charset.py --report         # 打印账本统计与图集预算

依赖：`fontTools`（读 TTF 的 cmap）。
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
SCAN_ROOT = ROOT / "Assets" / "_Project" / "Scripts"
SCAN_EXTS = (".cs",)
SKIP_DIRS = {"obj", "bin"}
CHARSET_FILE = ROOT / "tools" / "font_charset.txt"
BASELINE_FILE = ROOT / "tools" / "font_baseline.txt"
EXEMPT_FILE = ROOT / "tools" / "font_exempt_chars.txt"

# 两支在用字重的源 TTF（由 tools/bake_fonts.py 从 MediaRaw/fonts 的 woff2 解出）。
FONT_TTFS = (
    ROOT / "Assets" / "_Project" / "Art" / "Fonts" / "MiSans-Semibold.ttf",
    ROOT / "Assets" / "_Project" / "Art" / "Fonts" / "MiSans-Heavy.ttf",
)

# 基线：ASCII 可见字符 + 项目装饰符号（比 ComicFontAssetBuilder 旧的 requiredSymbols 更宽，
# 因为 UI 还会用到 ° ± ㎡ 与制表符号）。基线里的字同样要过 MiSans cmap，进不了就进豁免账。
#
# 为什么单独出一份基线账本：标题字重（Heavy）按 96pt 采样，字形框大、每张图集只装得下 81 字，
# 把 1200+ 字的正文账本整个预烘进去要多花 ~14MB 显存。而大字号文案几乎只有拉丁/数字/符号，
# 汉字首次出现时现场栅格化一次即可（TMP 会把字形留在图集里），所以 Heavy 只预烘基线。
BASELINE = (
    "".join(chr(c) for c in range(0x20, 0x7F))
    # Latin-1 + 排版符号：覆盖运行时拼出来的文案（更新说明、版本号、数据串），
    # 与旧 k_PresetCharacters 的覆盖面保持一致，避免"源码里没写就烘不到"。
    + "¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿"
    + "–—‘’“”…•™→←↑↓"
    + "·×÷°±—…‘’“”《》【】〈〉「」『』、。，；：！？（）～＋－％"
)

# 图集预算口径：必须与 ComicFontAssetBuilder 的采样参数一致（body 64/padding6、display 96/padding8）。
ATLAS_SIZE = 1024

WRAP = 64


def renderable(ch: str) -> bool:
    """控制字符不上屏，也不进账本（`\\n` `\\t` 之类会被扫到）。"""
    cp = ord(ch)
    return cp >= 0x20 and cp != 0x7F and not (0x80 <= cp <= 0x9F) and cp not in (0x200B, 0xFEFF)


def extract_literals(text: str) -> str:
    """取 C# 源码里的字符串/字符字面量，跳过注释。

    - `//` 与 `/* */` 注释整段跳过（项目注释里汉字最多，混进来账本会虚胖一倍）。
    - `"..."` 处理转义；`@"..."` 处理 `""` 转义；`$"..."` 的插值表达式 `{...}` 按代码跳过
      （表达式内部若再嵌字符串，本函数会把它一起当代码跳过，属于已知的保守近似）。
    - `'x'` 字符字面量同样算上屏文案。
    """
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]

        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue

        # 前缀（@ / $@ / @$ / $）统一把 i 落到开引号上，再统一跳过开引号。
        verbatim = False
        interp = False
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            verbatim, i = True, i + 1
        elif c == "$" and i + 2 < n and text[i + 1] == "@" and text[i + 2] == '"':
            verbatim, interp, i = True, True, i + 2
        elif c == "@" and i + 2 < n and text[i + 1] == "$" and text[i + 2] == '"':
            verbatim, interp, i = True, True, i + 2
        elif c == "$" and i + 1 < n and text[i + 1] == '"':
            interp, i = True, i + 1
        elif c != '"':
            if c == "'":
                j = i + 1
                buf: list[str] = []
                while j < n:
                    if text[j] == "\\":
                        j += 2
                        continue
                    if text[j] == "'":
                        break
                    buf.append(text[j])
                    j += 1
                if j < n:
                    out.append("".join(buf))
                    i = j + 1
                    continue
            i += 1
            continue

        i += 1  # 跳过开引号

        # 字符串体
        buf: list[str] = []
        depth = 0
        while i < n:
            ch = text[i]
            if depth > 0:
                if ch == '"':  # 插值表达式内的嵌套字符串：整体跳过
                    i += 1
                    while i < n:
                        if text[i] == "\\":
                            i += 2
                            continue
                        if text[i] == '"':
                            break
                        i += 1
                    i += 1
                    continue
                if ch == "{":
                    depth += 1
                elif ch == "}":
                    depth -= 1
                i += 1
                continue
            if not verbatim and ch == "\\":
                if i + 1 < n:
                    buf.append(text[i + 1])
                i += 2
                continue
            if ch == '"':
                if verbatim and i + 1 < n and text[i + 1] == '"':
                    buf.append('"')
                    i += 2
                    continue
                break
            if ch == "{" and interp:
                depth = 1
                i += 1
                continue
            buf.append(ch)
            i += 1
        out.append("".join(buf))
        i += 1

    return "".join(out)


def scan_source() -> tuple[set[str], int]:
    chars: set[str] = set()
    files = 0
    for path in sorted(SCAN_ROOT.rglob("*")):
        if not path.is_file() or path.suffix not in SCAN_EXTS:
            continue
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        files += 1
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError as exc:  # pragma: no cover - IO 失败直接暴露
            print(f"读取失败: {path} ({exc})", file=sys.stderr)
            continue
        chars.update(ch for ch in extract_literals(text) if renderable(ch))
    return chars, files


def font_coverage() -> set[str]:
    covered: set[str] = set()
    for ttf in FONT_TTFS:
        if not ttf.is_file():
            print(f"找不到字体源文件: {ttf}", file=sys.stderr)
            sys.exit(2)
        covered.update(chr(cp) for cp in TTFont(str(ttf), lazy=True).getBestCmap())
    return covered


def load_ledger(path: Path) -> str:
    if not path.is_file():
        return ""
    return "".join(ch for ch in path.read_text(encoding="utf-8") if renderable(ch))


def write_ledger(path: Path, chars: str) -> None:
    ordered = "".join(sorted(chars, key=ord))
    lines = [ordered[i:i + WRAP] for i in range(0, len(ordered), WRAP)]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def diff_summary(old: str, new: str) -> str:
    added = "".join(sorted(set(new) - set(old), key=ord))
    removed = "".join(sorted(set(old) - set(new), key=ord))
    parts = []
    if added:
        parts.append(f"新增 {len(added)}: {added[:80]}")
    if removed:
        parts.append(f"移除 {len(removed)}: {removed[:80]}")
    return "；".join(parts) if parts else "无变化"


def atlas_budget(charset_count: int, baseline_count: int) -> str:
    parts = []
    for label, size, padding, count in (
        ("正文 Semibold", 64, 6, charset_count),
        ("标题 Heavy", 96, 8, baseline_count),
    ):
        per_row = max(1, ATLAS_SIZE // (size + padding * 2))
        per_atlas = per_row * per_row
        atlases = (count + per_atlas - 1) // per_atlas
        parts.append(f"{label} {size}pt/pad{padding} 预烘 {count} 字: {per_atlas} 字/图集 → "
                     f"{atlases} 张 ≈ {atlases} MB")
    return " | ".join(parts)


def main() -> int:
    ap = argparse.ArgumentParser(description="生成 MiSans 预烘字集账本")
    ap.add_argument("--accept-exempt", action="store_true",
                    help="确认接受新出现的豁免字（MiSans 渲染不了、靠系统回退的字）")
    ap.add_argument("--check", action="store_true", help="只比对不写盘，漂移则退出码 1")
    ap.add_argument("--report", action="store_true", help="只打印统计")
    args = ap.parse_args()

    if not SCAN_ROOT.is_dir():
        print(f"找不到扫描目录: {SCAN_ROOT}", file=sys.stderr)
        return 2

    source, files = scan_source()
    covered = font_coverage()
    candidate = source | set(BASELINE)

    charset = "".join(sorted(candidate & covered, key=ord))
    baseline = "".join(sorted(set(BASELINE) & covered, key=ord))
    exempt = "".join(sorted(candidate - covered, key=ord))

    old_charset = load_ledger(CHARSET_FILE)
    old_baseline = load_ledger(BASELINE_FILE)
    old_exempt = load_ledger(EXEMPT_FILE)

    print(f"扫描 {files} 个 .cs 文件；字面量可上屏字符 {len(source)} 个")
    print(f"字集账本 {len(charset)} 字（原 {len(old_charset)}）；{diff_summary(old_charset, charset)}")
    print(f"基线账本 {len(baseline)} 字（原 {len(old_baseline)}）；{diff_summary(old_baseline, baseline)}")
    print(f"豁免账 {len(exempt)} 字（原 {len(old_exempt)}）；{diff_summary(old_exempt, exempt)}")
    if exempt:
        print("豁免字（只许出现在注释与编辑器日志里，不许上屏）: " + exempt)
    print("图集预算: " + atlas_budget(len(charset), len(baseline)))

    if args.report:
        return 0

    charset_drift = set(charset) != set(old_charset)
    baseline_drift = set(baseline) != set(old_baseline)
    exempt_added = set(exempt) - set(old_exempt)

    if exempt_added and not args.accept_exempt:
        print("\n⚠ 出现新的豁免字，需要人拍板（改文案 or 接受系统回退字体）："
              + "".join(sorted(exempt_added, key=ord)), file=sys.stderr)
        print("确认无误后重跑：python3 tools/font_charset.py --accept-exempt", file=sys.stderr)
        return 3

    if args.check:
        if charset_drift or baseline_drift or exempt_added:
            print("账本与源码不一致（--check 不写盘）", file=sys.stderr)
            return 1
        print("账本与源码一致 ✓")
        return 0

    write_ledger(CHARSET_FILE, charset)
    write_ledger(BASELINE_FILE, baseline)
    write_ledger(EXEMPT_FILE, exempt)
    print(f"已写入 {CHARSET_FILE.relative_to(ROOT)} / {BASELINE_FILE.relative_to(ROOT)} / "
          f"{EXEMPT_FILE.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
