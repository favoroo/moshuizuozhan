#!/usr/bin/env python3
"""make_sfx_audition.py —— 生成音效 A/B 试听页（新旧对照，纯标准库）

为什么需要它：agent 听不见声音，音色「好不好听」只能靠人耳验收。
本脚本把 `Assets/_Project/Audio/SFX/` 的新版产物与一份「改动前」旧版快照
逐族并排铺成一个可点击的 HTML，并附客观指标（时长/峰值/RMS 与 RMS 变化），
让用户先整体过一遍，再决定哪些族要回炉。

用法：
    # 1) 先把「改动前」的 wav 快照存一份（改引擎前做，之后才有对照）
    mkdir -p /tmp/p1_sfx_before && cp Assets/_Project/Audio/SFX/*.wav /tmp/p1_sfx_before/

    # 2) 生成试听页（旧版快照会拷到输出目录旁的 sfx_before/，页面因此自包含旧音）
    python3 tools/make_sfx_audition.py --before /tmp/p1_sfx_before --out /path/to/sfx_audition.html

    # 只列新版（不做 A/B）
    python3 tools/make_sfx_audition.py --out /path/to/sfx_audition.html

产物：
    <out>                  试听页
    <out 同级>/sfx_before/ 旧版 wav 快照（--before 未给时忽略）
新版音效用相对路径指向工程内的 Assets/_Project/Audio/SFX，不重复拷贝。
"""

import argparse
import html
import json
import os
import shutil
import struct
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bake_sfx  # noqa: E402  取 GROUPS / SFX / LOOP_KEYS，保持与配方同步

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NEW_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "SFX")


def metrics(path):
    """读 wav 算 (时长, 峰值, RMS)。"""
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        ch = w.getnchannels()
        raw = w.readframes(w.getnframes())
        n = w.getnframes()
    vals = [struct.unpack("<h", raw[i:i + 2])[0] / 32768.0 for i in range(0, len(raw), 2)]
    if not vals:
        return 0.0, 0.0, 0.0
    peak = max(abs(v) for v in vals)
    rms = (sum(v * v for v in vals) / len(vals)) ** 0.5
    return n / float(sr), peak, rms


def variants(directory, key):
    """列出某族的全部变体文件名，按变体序号排序（key_1, key_2, ...）。"""
    out = []
    n = 1
    while True:
        name = "%s_%d.wav" % (key, n)
        if os.path.isfile(os.path.join(directory, name)):
            out.append(name)
            n += 1
        else:
            break
    return out


def fmt(v):
    return "%.3f" % v


def main():
    ap = argparse.ArgumentParser(description="生成 P1 音效 A/B 试听页")
    ap.add_argument("--out", required=True, help="输出 HTML 路径")
    ap.add_argument("--before", default=None, help="改动前的 wav 快照目录（给了就做 A/B）")
    ap.add_argument("--new-dir", default=NEW_DIR, help="新版 wav 目录")
    ap.add_argument("--no-search", action="store_true", help="不生成搜索框")
    a = ap.parse_args()

    out_dir = os.path.dirname(os.path.abspath(a.out))
    os.makedirs(out_dir, exist_ok=True)
    new_dir = os.path.abspath(a.new_dir)
    new_prefix = os.path.relpath(new_dir, out_dir).replace(os.sep, "/")

    before_dir = os.path.abspath(a.before) if a.before else None
    before_prefix = None
    if before_dir and os.path.isdir(before_dir):
        # 旧版快照拷到输出目录旁，页面因此不依赖 /tmp
        dst = os.path.join(out_dir, "sfx_before")
        if os.path.abspath(before_dir) != os.path.abspath(dst):
            os.makedirs(dst, exist_ok=True)
            for nm in os.listdir(before_dir):
                if nm.endswith(".wav"):
                    shutil.copy2(os.path.join(before_dir, nm), os.path.join(dst, nm))
        before_prefix = "sfx_before"
    else:
        print("⚠ 未提供 --before（或目录不存在）：只列新版，不做 A/B")

    families = []      # (category, key, is_loop, [new clips], [old clips])
    warn_missing = []
    for category, keys in bake_sfx.GROUPS.items():
        for key in keys:
            if key not in bake_sfx.SFX:
                continue
            total = bake_sfx.SFX[key][0]
            is_loop = key in bake_sfx.LOOP_KEYS

            news = []
            for nm in variants(new_dir, key):
                d, p, r = metrics(os.path.join(new_dir, nm))
                news.append({"file": nm, "dur": d, "peak": p, "rms": r})
            if len(news) < total:
                warn_missing.append("%s 期望 %d 个变体，只找到 %d 个" % (key, total, len(news)))

            olds = []
            if before_prefix:
                for nm in variants(before_dir, key):
                    d, p, r = metrics(os.path.join(before_dir, nm))
                    olds.append({"file": nm, "dur": d, "peak": p, "rms": r})

            families.append({"cat": category, "key": key, "loop": is_loop,
                             "new": news, "old": olds})

    total_new = sum(len(f["new"]) for f in families)
    total_old = sum(len(f["old"]) for f in families)

    # 逐族 RMS 变化（取各自 _1 变体，最稳定的对照）
    def rms1(clips):
        for c in clips:
            if c["file"].endswith("_1.wav"):
                return c["rms"], c["dur"]
        return (clips[0]["rms"], clips[0]["dur"]) if clips else (0.0, 0.0)

    deltas = []
    for f in families:
        if not f["old"] or not f["new"]:
            continue
        ro, _ = rms1(f["old"])
        rn, _ = rms1(f["new"])
        if ro > 1e-9:
            deltas.append((f["key"], (rn / ro - 1.0) * 100.0))
    avg_delta = sum(d for _, d in deltas) / len(deltas) if deltas else 0.0

    # ---------------- HTML ----------------
    esc = html.escape
    parts = []
    parts.append("""<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>P1 音效 A/B 试听</title>
<style>
 :root{--bg:#12141a;--card:#1b1f27;--card2:#22262f;--fg:#e8ecf3;--dim:#8b95a5;
       --accent:#5ad1ff;--good:#4ade80;--bad:#f87171;--line:#2c323d;--loop:#fbbf24}
 *{box-sizing:border-box}
 body{margin:0;background:var(--bg);color:var(--fg);
      font:14px/1.5 -apple-system,"PingFang SC","Segoe UI",sans-serif}
 header{position:sticky;top:0;z-index:9;background:rgba(18,20,26,.96);
        border-bottom:1px solid var(--line);padding:14px 20px;backdrop-filter:blur(8px)}
 h1{margin:0 0 6px;font-size:17px;letter-spacing:.3px}
 .sub{color:var(--dim);font-size:12.5px}
 .sub b{color:var(--accent)}
 .tools{margin-top:10px;display:flex;gap:10px;align-items:center;flex-wrap:wrap}
 input[type=search]{background:var(--card2);border:1px solid var(--line);color:var(--fg);
      border-radius:7px;padding:6px 10px;min-width:220px;font-size:13px}
 button{background:var(--card2);border:1px solid var(--line);color:var(--fg);cursor:pointer;
      border-radius:7px;padding:5px 10px;font-size:12.5px}
 button:hover{border-color:var(--accent);color:var(--accent)}
 button.playing{border-color:var(--accent);color:var(--accent)}
 main{padding:16px 20px 60px}
 section{margin:0 0 22px}
 h2{font-size:13px;text-transform:uppercase;letter-spacing:1.2px;color:var(--dim);
    margin:0 0 10px;border-bottom:1px solid var(--line);padding-bottom:6px}
 .card{background:var(--card);border:1px solid var(--line);border-radius:10px;
       padding:11px 13px;margin-bottom:9px}
 .head{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin-bottom:8px}
 .name{font-weight:600;font-size:14px}
 .badge{font-size:11px;color:var(--dim);border:1px solid var(--line);border-radius:999px;
        padding:1px 8px}
 .badge.loop{color:var(--loop);border-color:#5a4a1a}
 .badge.up{color:var(--good);border-color:#1e4d33}
 .badge.down{color:var(--bad);border-color:#5a2626}
 .rows{display:grid;grid-template-columns:52px 1fr;gap:6px 10px;align-items:start}
 .lane{color:var(--dim);font-size:12px;padding-top:4px;white-space:nowrap}
 .clips{display:flex;flex-wrap:wrap;gap:6px}
 .clip{display:flex;align-items:center;gap:6px;background:var(--card2);
       border:1px solid var(--line);border-radius:8px;padding:4px 8px 4px 4px;font-size:12px}
 .clip .meta{color:var(--dim);font-variant-numeric:tabular-nums;font-size:11px}
 .clip button{padding:2px 8px;font-size:11.5px}
 .empty{color:var(--dim);font-size:12px;padding-top:4px}
</style></head><body>
<header>
 <h1>P1 音效 A/B 试听 <span class="sub">(新旧对照)</span></h1>
 <div class="sub">新版 <b>%d</b> 个文件 / 旧版 <b>%d</b> 个 · 共 <b>%d</b> 族 ·
    逐族 RMS（响度密度）平均变化 <b style="color:%s">%+.0f%%</b></div>
 <div class="tools">
  <input id="q" type="search" placeholder="筛选：族名 / 分类，如 shot / UI / step">
  <button id="stop">■ 停止</button>
  <button id="onlychanged">只显示 RMS 变化 &gt; 15%%</button>
  <span class="sub" id="count"></span>
 </div>
</header>
<main id="main">
""" % (total_new, total_old, len(families),
       "#4ade80" if avg_delta >= 0 else "#f87171", avg_delta))

    by_cat = {}
    for f in families:
        by_cat.setdefault(f["cat"], []).append(f)

    for cat, flist in by_cat.items():
        parts.append('<section data-cat="%s">\n<h2>%s</h2>\n' % (esc(cat), esc(cat)))
        for f in flist:
            ro = rn = 0.0
            if f["old"]:
                ro, _ = rms1(f["old"])
            if f["new"]:
                rn, _ = rms1(f["new"])
            d = (rn / ro - 1.0) * 100.0 if ro > 1e-9 else 0.0
            badge_cls = "up" if d >= 0 else "down"
            parts.append('<div class="card" data-key="%s" data-delta="%.1f">\n<div class="head">'
                         % (esc(f["key"]), d))
            parts.append('<span class="name">%s</span>' % esc(f["key"]))
            if f["loop"]:
                parts.append('<span class="badge loop">循环</span>')
            parts.append('<span class="badge">%d 变体</span>' % len(f["new"]))
            if f["old"]:
                parts.append('<span class="badge %s">RMS %+.0f%%</span>' % (badge_cls, d))
            else:
                parts.append('<span class="badge">新增</span>')
            parts.append("</div>\n<div class=\"rows\">\n")

            def lane(label, clips, prefix):
                if not clips:
                    return '<div class="lane">%s</div><div class="empty">（无）</div>' % label
                out = ['<div class="lane">%s</div><div class="clips">' % label]
                for c in clips:
                    url = "%s/%s" % (prefix, os.path.basename(c["file"]))
                    out.append(
                        '<span class="clip">'
                        '<button data-src="%s" data-loop="%d">▶</button>'
                        '<span>%s</span><span class="meta">%ss %.3f/%.3f</span></span>'
                        % (esc(url), 1 if f["loop"] else 0,
                           esc(c["file"].replace(".wav", "")),
                           fmt(c["dur"]), c["peak"], c["rms"]))
                out.append("</div>")
                return "".join(out)

            parts.append(lane("新版", f["new"], new_prefix))
            if f["old"]:
                parts.append(lane("旧版", f["old"], before_prefix))
            parts.append("</div></div>\n")
        parts.append("</section>\n")

    parts.append("""</main>
<audio id="au"></audio>
<script>
 const au = document.getElementById('au');
 let cur = null;
 document.addEventListener('click', e => {
   const b = e.target.closest('button[data-src]');
   if (!b) return;
   if (cur === b) { au.pause(); b.classList.remove('playing'); cur = null; return; }
   if (cur) cur.classList.remove('playing');
   au.loop = b.dataset.loop === '1';
   au.src = b.dataset.src;
   au.play();
   b.classList.add('playing');
   cur = b;
 });
 au.addEventListener('ended', () => { if (cur) cur.classList.remove('playing'); cur = null; });
 document.getElementById('stop').onclick = () => {
   au.pause(); if (cur) cur.classList.remove('playing'); cur = null;
 };
 const q = document.getElementById('q');
 const only = document.getElementById('onlychanged');
 const cards = [...document.querySelectorAll('.card')];
 const count = document.getElementById('count');
 let changedOnly = false;
 function apply() {
   const s = (q.value || '').trim().toLowerCase();
   let shown = 0;
   document.querySelectorAll('section').forEach(sec => {
     let any = false;
     sec.querySelectorAll('.card').forEach(c => {
       const hit = !s || c.dataset.key.includes(s) ||
                   sec.dataset.cat.toLowerCase().includes(s);
       const big = !changedOnly || Math.abs(parseFloat(c.dataset.delta)) > 15;
       const vis = hit && big;
       c.style.display = vis ? '' : 'none';
       if (vis) any = true;
     });
     sec.style.display = any ? '' : 'none';
     if (any) shown += sec.querySelectorAll('.card:not([style*="display: none"])').length;
   });
   count.textContent = shown + ' / ' + cards.length + ' 族';
 }
 q.oninput = apply;
 only.onclick = () => {
   changedOnly = !changedOnly;
   only.style.color = changedOnly ? 'var(--accent)' : '';
   apply();
 };
 apply();
</script></body></html>
""")

    with open(a.out, "w", encoding="utf-8") as fh:
        fh.write("".join(parts))

    print("试听页: %s" % a.out)
    print("  新版 %d 个文件 · 旧版 %d 个 · %d 族 · 逐族 RMS 平均 %+.0f%%"
          % (total_new, total_old, len(families), avg_delta))
    if warn_missing:
        print("⚠ 变体数不足：")
        for w in warn_missing:
            print("   " + w)
    if before_prefix:
        print("  旧版快照已拷到: %s" % os.path.join(out_dir, before_prefix))
    return 0


if __name__ == "__main__":
    sys.exit(main())
