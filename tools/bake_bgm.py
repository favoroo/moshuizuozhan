#!/usr/bin/env python3
"""bake_bgm.py —— P1 对局双段动态 BGM 离线烘焙（确定性、可复现、零网关配额）

为什么不用网关：`lyria-3.5` 于 2026-10-29 实测被上游禁用
（400 `PROXY_GENERATION_DISABLED: generateContent is not supported`），
网关也没有其它音乐模型。调研库 Inkwave 的配乐本身就是 Web Audio 程序化合成
（`inkwave-aah-research/src/audio/music.js`），所以本项目用同样的思路离线渲染：
和声进行、鼓组节奏与主旋律直接沿用调研库 battle / battle_final 的编排，
音色用 Python 加性合成 + 滤波 + 软削波重建。

产物：
    MediaRaw/music/bgm_match_main.wav/.ogg    主阶段（128 BPM，32 小节 = 60.0s 循环）
    MediaRaw/music/bgm_match_final.wav/.ogg   决战段（160 BPM，40 小节 = 60.0s 循环）
    Assets/_Project/Audio/Music/*.ogg         入库件（ImportPostprocessor 自动套 Streaming+Vorbis）
    MediaRaw/music/manifest.jsonl             客观指标台账（时长/峰值/RMS/频段/循环接缝）

用法：
    python3 tools/bake_bgm.py                 # 两段全烘 + 转 OGG + 拷进 Assets
    python3 tools/bake_bgm.py --only main     # 只烘主阶段（调音时用）
    python3 tools/bake_bgm.py --list
    python3 tools/bake_bgm.py --stats         # 不烘焙，只体检已有产物
    python3 tools/bake_bgm.py --no-assets     # 只烘到 MediaRaw，不拷进 Assets

依赖：numpy + scipy（本机已装；Unity 素材管线也已依赖 numpy，Pillow 用于 keyout）。

⚠ 烘焙是确定性的（固定种子），同输入永远同波形；改编排/音色后重跑即可。
⚠ 客观指标只保证「不削波、响度合理、循环无爆点」，**好不好听必须由用户耳机验收**。
"""

import argparse
import json
import math
import os
import subprocess
import sys
import wave
from datetime import datetime

import numpy as np
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW_DIR = os.path.join(ROOT, "MediaRaw", "music")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "Music")
MANIFEST = os.path.join(RAW_DIR, "manifest.jsonl")

# ---------------------------------------------------------------- 基础工具

def midi_hz(m):
    return 440.0 * (2.0 ** ((m - 69) / 12.0))


def _t(n):
    return np.arange(n, dtype=np.float64) / SR


def _env_adsr(gate_s, release_s, a=0.004, d=0.18, s=0.72):
    """线性 ADSR；gate 之后指数释放，避免尾音硬切。"""
    gate_n = max(1, int(gate_s * SR))
    rel_n = max(1, int(release_s * SR))
    n = gate_n + rel_n
    t = _t(n)
    env = np.ones(n)
    ai = max(1, int(a * SR))
    env[:ai] = np.linspace(0.0, 1.0, ai)
    di = max(1, int(d * SR))
    d_end = min(n, ai + di)
    if d_end > ai:
        env[ai:d_end] = 1.0 + (s - 1.0) * np.linspace(0.0, 1.0, d_end - ai)
    if n > d_end:
        env[d_end:gate_n] = s
    if n > gate_n:
        env[gate_n:] = s * np.exp(-np.linspace(0.0, 4.0, n - gate_n))
    return env


def _perc(n, attack=0.002, tau=0.2):
    t = _t(n)
    env = np.exp(-t / max(1e-4, tau))
    ai = max(1, int(attack * SR))
    env[:ai] *= np.linspace(0.0, 1.0, ai)
    return env


def _fit(env, n):
    """把包络对齐到音符缓冲长度（不同参数取整时长度会差 1~几十个采样）。"""
    if len(env) >= n:
        return env[:n]
    return np.pad(env, (0, n - len(env)))


def _band(sig, lo, hi, order=2):
    sos = butter(order, [lo / (SR / 2), hi / (SR / 2)], btype="bandpass", output="sos")
    return sosfilt(sos, sig)


def _hp(sig, f, order=2):
    sos = butter(order, f / (SR / 2), btype="highpass", output="sos")
    return sosfilt(sos, sig)


def _lp(sig, f, order=2):
    sos = butter(order, f / (SR / 2), btype="lowpass", output="sos")
    return sosfilt(sos, sig)


def _tanh_drive(sig, drive):
    return np.tanh(sig * drive) / np.tanh(drive)


def _add(bus, start_s, sig, gain=1.0, pan=0.0):
    """等功率声像叠加到立体声总线（bus = [L, R]）。"""
    if sig is None or len(sig) == 0:
        return
    i0 = int(start_s * SR)
    if i0 >= len(bus[0]):
        return
    n = min(len(sig), len(bus[0]) - i0)
    if n <= 0:
        return
    p = max(-1.0, min(1.0, pan))
    gl = math.cos((p + 1.0) * math.pi / 4.0)
    gr = math.sin((p + 1.0) * math.pi / 4.0)
    bus[0][i0:i0 + n] += sig[:n] * gain * gl
    bus[1][i0:i0 + n] += sig[:n] * gain * gr


# ---------------------------------------------------------------- 音高 / 和声

_PC = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4,
       "F": 5, "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8, "A": 9,
       "A#": 10, "Bb": 10, "B": 11}

_CHORDS = {
    "E5": (4, [0, 7, 12]), "C5": (0, [0, 7, 12]), "D5": (2, [0, 7, 12]),
    "B5": (11, [0, 7, 12]),
    "Em": (4, [0, 3, 7]), "Em7": (4, [0, 3, 7, 10]), "Em7b5": (4, [0, 3, 6, 10]),
    "C": (0, [0, 4, 7]), "Cmaj7": (0, [0, 4, 7, 11]), "Cmaj9": (0, [0, 4, 7, 11, 14]),
    "Cmaj7#11": (0, [0, 4, 7, 11, 18]),
    "D": (2, [0, 4, 7]), "D7": (2, [0, 4, 7, 10]),
    "Bm": (11, [0, 3, 7]), "Bm7": (11, [0, 3, 7, 10]), "B7": (11, [0, 4, 7, 10]),
    "B7sus4": (11, [0, 5, 7, 10]),
    "Am7": (9, [0, 3, 7, 10]), "Am9": (9, [0, 3, 7, 10, 14]),
    "A7#9": (9, [0, 4, 7, 10, 15]),
    "G7": (7, [0, 4, 7, 10]), "G13": (7, [0, 4, 7, 10, 14]),
}


def _chord(sym):
    sym = sym.strip()
    if sym in _CHORDS:
        return _CHORDS[sym]
    # 兜底：只取根音 + 大三和弦
    root = sym[:2] if len(sym) > 1 and sym[1] in "#b" else sym[:1]
    pc = _PC.get(root, 0)
    if "m" in sym[1:]:
        return pc, [0, 3, 7]
    return pc, [0, 4, 7]


def _root_midi(sym, lo=40, hi=52):
    pc, _ = _chord(sym)
    m = lo + ((pc - lo) % 12)
    while m < hi:
        return m
    return m


def _voicing(sym, lo=55, hi=76, max_notes=4):
    pc, ivs = _chord(sym)
    out = []
    for iv in ivs[:max_notes]:
        target = (pc + iv) % 12
        m = lo + ((target - lo) % 12)
        while m <= hi:
            if m not in out:
                out.append(m)
                break
            m += 12
    return sorted(set(out))


def _power_chord(sym, lo=40):
    root = _root_midi(sym, lo, lo + 12)
    return [root, root + 7, root + 12]


_NOTE_RE = None


def parse_phrase(text):
    """与 music.js parsePhrase 等价：note/len（len 以 16 分音符为单位），r=休止，_=延长。"""
    import re
    global _NOTE_RE
    if _NOTE_RE is None:
        _NOTE_RE = re.compile(r"^(r|_|[A-G][#b]?-?\d)/(\d+)(!?)$")
    ev, pos, last = [], 0, None
    for tok in text.split():
        if tok == "|":
            continue
        m = _NOTE_RE.match(tok)
        if not m:
            raise ValueError(f"bad phrase token: {tok}")
        ln = int(m.group(2))
        if m.group(1) == "r":
            last = None
        elif m.group(1) == "_":
            if last is not None:
                last["len"] += ln
        else:
            last = {"s": pos, "len": ln, "m": _note_midi(m.group(1)), "acc": bool(m.group(3))}
            ev.append(last)
        pos += ln
    if pos % 16:
        raise ValueError(f"phrase length {pos} is not whole bars")
    return ev, pos


def _note_midi(s):
    import re
    m = re.match(r"^([A-G][#b]?)(-?\d)$", s)
    if not m:
        raise ValueError(f"bad note: {s}")
    return 12 * (int(m.group(2)) + 1) + _PC[m.group(1)]


# ---------------------------------------------------------------- 旋律素材（调研库原曲编排）

BATTLE_A_LEAD = (
    "B4/2 E5/2 r/1 E5/1 G5/2 F#5/2 E5/2 D5/2 B4/2 | E5/3 G5/3 B5/2 A5/2 G5/2 E5/4 | "
    "r/2 A4/1 C5/1 E5/2 A5/2 G5/2 E5/2 C5/2 D5/2 | D#5/3 F#5/3 A5/2 B5/6 r/2 | "
    "B4/2 E5/2 r/1 E5/1 G5/2 F#5/2 E5/2 D5/2 B4/2 | E5/3 G5/3 B5/2 A5/2 G5/2 E5/4 | "
    "r/2 A4/1 C5/1 E5/2 A5/2 G5/2 E5/2 C5/2 D5/2 | F#5/2 E5/2 D#5/2 B4/2 D#5/4 F#5/4"
)
BATTLE_B_LEAD = (
    "G5/4 E5/2 G5/2 A5/4 G5/4 | F#5/4 D5/2 F#5/2 A5/6 r/2 | B5/3 A5/3 F#5/2 D5/4 F#5/4 | G5/6 F#5/2 E5/8 | "
    "G5/4 E5/2 G5/2 C6/4 B5/4 | A5/4 F#5/2 A5/2 D6/6 r/2 | E6/4 D6/4 B5/4 A5/4 | A5/2 G5/2 F#5/2 D#5/2 F#5/8"
)
LEAD_A, _ = parse_phrase(BATTLE_A_LEAD)
LEAD_B, _ = parse_phrase(BATTLE_B_LEAD)

# ---------------------------------------------------------------- 乐器合成

def synth_bass(freq, dur, vel, style="punk"):
    gate = int(dur * SR)
    n = gate + int(0.09 * SR)
    t = _t(n)
    cutoff = 2400.0 if style == "punk" else 1400.0
    sig = np.zeros(n)
    for h in range(1, 15):
        amp = (1.0 / h ** 1.05) * math.exp(-((freq * h / cutoff) ** 2))
        if amp < 1e-4:
            continue
        sig += amp * np.sin(2 * math.pi * freq * h * t + 0.37 * h)
    sig += 0.55 * np.sin(2 * math.pi * freq * t)
    drive = 2.6 if style == "punk" else 1.7
    sig = _tanh_drive(sig, drive)
    return sig * _fit(_env_adsr(dur, 0.09, a=0.003, d=0.16, s=0.68), n) * vel


def synth_brass(freq, dur, vel):
    n = int((dur + 0.24) * SR)
    t = _t(n)
    sig = np.zeros(n)
    for det in (-0.006, 0.0, 0.006):
        f = freq * (1.0 + det)
        for h in range(1, 17):
            fh = f * h
            if fh > 9000:
                break
            formant = 1.0 + 1.60 * math.exp(-(((fh - 1600.0) / 900.0) ** 2))
            amp = (1.0 / h ** 0.72) * formant * math.exp(-((fh / 7200.0) ** 2))
            sig += amp * np.sin(2 * math.pi * fh * t + 0.13 * h + det * 60)
    sig = _tanh_drive(sig * 0.55, 1.45)
    return sig * _fit(_env_adsr(dur, 0.2, a=0.02, d=0.26, s=0.66), n) * vel


def synth_keys(freq, dur, vel):
    n = int((dur + 0.35) * SR)
    t = _t(n)
    idx = 1.5 * np.exp(-t / 0.55) + 0.18
    sig = np.sin(2 * math.pi * freq * t + idx * np.sin(2 * math.pi * freq * 2.01 * t))
    sig += 0.11 * np.sin(2 * math.pi * freq * 7.0 * t) * np.exp(-t / 0.1)
    env = _env_adsr(dur, 0.32, a=0.003, d=1.1, s=0.28)
    return sig * _fit(env, n) * vel


def synth_pluck(freq, dur, vel):
    n = int((dur + 0.22) * SR)
    t = _t(n)
    sig = np.zeros(n)
    for h in range(1, 11):
        if freq * h > 8000:
            break
        sig += (1.0 / h) * np.sin(2 * math.pi * freq * h * t) * np.exp(-t * (7.0 + 1.8 * h))
    return sig * _perc(n, 0.002, 0.12) * vel


def synth_guitar(freqs, dur, vel, mute=False):
    n = int((dur + (0.07 if mute else 0.14)) * SR)
    t = _t(n)
    sig = np.zeros(n)
    for f0 in freqs:
        for det in (-0.004, 0.004):
            f = f0 * (1.0 + det)
            for h in range(1, 15):
                if f * h > 9000:
                    break
                sig += (1.0 / h ** 0.9) * np.sin(2 * math.pi * f * h * t + 0.21 * h)
    sig = _hp(_tanh_drive(sig, 5.0), 90.0)
    return sig * _fit(_env_adsr(dur, 0.08 if mute else 0.15, a=0.003, d=0.12, s=0.55 if mute else 0.7), n) * vel


def synth_pad(freq, dur, vel):
    n = int((dur + 0.7) * SR)
    t = _t(n)
    sig = np.zeros(n)
    for det in (-0.008, 0.008):
        f = freq * (1.0 + det)
        for h in range(1, 11):
            sig += (1.0 / h ** 1.15) * np.sin(2 * math.pi * f * h * t + 0.3 * h)
    sig = _lp(sig, 2400.0)
    return sig * _fit(_env_adsr(dur, 0.55, a=0.25, d=0.4, s=0.8), n) * vel


def synth_lead(freq, dur, vel, style="pulse"):
    n = int((dur + 0.16) * SR)
    t = _t(n)
    vib = 1.0 + 0.006 * np.sin(2 * math.pi * 5.4 * t) * np.clip(t / 0.25, 0, 1)
    f = freq * vib
    if style == "saw":
        sig = np.zeros(n)
        for h in range(1, 15):
            sig += (1.0 / h) * np.sin(2 * math.pi * f * h * t)
        sig = _lp(sig, 3600.0)
    elif style == "soft":
        sig = np.sin(2 * math.pi * f * t) + 0.35 * np.sin(2 * math.pi * f * 2 * t)
        sig = _lp(sig, 2800.0)
    else:
        sig = np.zeros(n)
        for h in range(1, 16, 2):
            sig += (1.0 / h) * np.sin(2 * math.pi * f * h * t)
        sig = _lp(sig, 4200.0)
    return sig * _fit(_env_adsr(dur, 0.12, a=0.006, d=0.18, s=0.74), n) * vel


# ---------------------------------------------------------------- 鼓组

def drum_kick(vel=1.0):
    n = int(0.42 * SR)
    t = _t(n)
    f = 46.0 + 105.0 * np.exp(-t / 0.032)
    phase = 2 * math.pi * np.cumsum(f) / SR
    body = np.sin(phase) * np.exp(-t / 0.26)
    click = np.random.default_rng(11).standard_normal(n) * np.exp(-t / 0.004) * 0.16
    return _tanh_drive(body + click, 1.6) * vel


def drum_snare(vel=1.0, rng=None):
    rng = rng or np.random.default_rng(12)
    n = int(0.26 * SR)
    t = _t(n)
    noise = _band(rng.standard_normal(n), 1200.0, 7000.0)
    body = np.sin(2 * math.pi * 185.0 * t) * np.exp(-t / 0.1) * 0.55
    return (noise * np.exp(-t / 0.15) * 0.9 + body) * _perc(n, 0.001, 0.18) * vel


def drum_clap(vel=1.0, rng=None):
    rng = rng or np.random.default_rng(13)
    n = int(0.3 * SR)
    t = _t(n)
    sig = np.zeros(n)
    for off in (0.0, 0.011, 0.023, 0.036):
        i = int(off * SR)
        burst = _band(rng.standard_normal(n - i), 900.0, 5200.0) * np.exp(-_t(n - i) / 0.055)
        sig[i:] += burst
    return sig * 0.55 * vel


def drum_hat(vel=1.0, open_=False, rng=None):
    rng = rng or np.random.default_rng(14)
    n = int((0.3 if open_ else 0.06) * SR)
    noise = _hp(rng.standard_normal(n), 6800.0)
    return noise * np.exp(-_t(n) / (0.22 if open_ else 0.035)) * 0.5 * vel


def drum_ride(vel=1.0, rng=None):
    rng = rng or np.random.default_rng(15)
    n = int(0.55 * SR)
    t = _t(n)
    noise = _hp(rng.standard_normal(n), 5200.0) * np.exp(-t / 0.42)
    metal = (np.sin(2 * math.pi * 2370 * t + 0.3) + 0.7 * np.sin(2 * math.pi * 3180 * t + 1.4)
             + 0.5 * np.sin(2 * math.pi * 4710 * t + 2.2)) * np.exp(-t / 0.3) * 0.08
    return (noise * 0.35 + metal) * vel


def drum_crash(vel=1.0, rng=None):
    rng = rng or np.random.default_rng(16)
    n = int(1.4 * SR)
    t = _t(n)
    noise = _hp(rng.standard_normal(n), 3800.0) * np.exp(-t / 0.9)
    metal = np.zeros(n)
    for f, ph, gain in ((2110, 0.2, 0.06), (2870, 1.1, 0.05), (4090, 2.4, 0.04), (5630, 0.7, 0.03)):
        metal += gain * np.sin(2 * math.pi * f * t + ph) * np.exp(-t / 0.65)
    return (noise * 0.55 + metal) * vel


def drum_tom(freq, vel=1.0):
    n = int(0.4 * SR)
    t = _t(n)
    f = freq * (0.6 + 0.4 * np.exp(-t / 0.05))
    return np.sin(2 * math.pi * np.cumsum(f) / SR) * np.exp(-t / 0.28) * 0.7 * vel


def fx_riser(dur, vel=1.0):
    n = int(dur * SR)
    t = _t(n)
    rng = np.random.default_rng(17)
    raw = rng.standard_normal(n)
    # 三段带通交叉淡化，模拟上升的带宽中心
    sig = np.zeros(n)
    for lo, hi, w in ((200, 900, 0.5), (700, 3000, 0.7), (2200, 9000, 0.9)):
        sig += _band(raw, lo, hi) * w
    sweep = np.sin(2 * math.pi * np.cumsum(np.linspace(180.0, 1400.0, n)) / SR) * 0.12
    env = np.linspace(0.0, 1.0, n) ** 1.6
    return (sig * 0.18 + sweep) * env * vel


# ---------------------------------------------------------------- 编曲调度

VEL = {"X": 1.0, "O": 0.95, "x": 0.82, "o": 0.82, "c": 0.55, "g": 0.4, "m": 0.6, "r": 0.5}


def _pat(pat, bar, nbars, fill):
    if fill is not None and bar == nbars - 1:
        return fill
    if pat is None:
        return None
    return pat[bar % len(pat)] if isinstance(pat, list) else pat


def _next_chord(sections, si, bar_in_sec, nbars, chords):
    if bar_in_sec + 1 < nbars:
        return chords[(bar_in_sec + 1) % len(chords)].split()[0]
    nxt = sections[(si + 1) % len(sections)]
    return nxt["chords"][0].split()[0]


def _chord_at(chords, bar_in_sec):
    syms = chords[bar_in_sec % len(chords)].split()
    return syms


def render_song(spec):
    bpm = spec["bpm"]
    swing = spec.get("swing", 0.0)
    step_dur = 60.0 / bpm / 4.0
    bars_total = sum(s["bars"] for s in spec["sections"])
    loop_s = bars_total * 16 * step_dur
    n_loop = int(round(loop_s * SR))
    tail_s = 3.0
    n_render = n_loop + int(tail_s * SR)

    drums = [np.zeros(n_render), np.zeros(n_render)]
    bass = [np.zeros(n_render), np.zeros(n_render)]
    harm = [np.zeros(n_render), np.zeros(n_render)]
    lead = [np.zeros(n_render), np.zeros(n_render)]
    fx = [np.zeros(n_render), np.zeros(n_render)]
    kick_times = []

    rng = np.random.default_rng(20261029)
    sections = spec["sections"]
    bar_abs = 0
    for si, sec in enumerate(sections):
        nbars = sec["bars"]
        chords = sec["chords"]
        sg = sec.get("gain", 1.0)
        for b in range(nbars):
            t_bar = (bar_abs + b) * 16 * step_dur
            syms = _chord_at(chords, b)
            seg = [(0, 8, syms[0])] if len(syms) == 1 else [(0, 8, syms[0]), (8, 16, syms[1])]

            def chord_at(step):
                for s0, s1, sym in seg:
                    if s0 <= step < s1:
                        return sym
                return seg[0][2]

            # ---- 鼓
            for key, maker in (("k", drum_kick), ("s", drum_snare), ("c", drum_clap),
                               ("h", drum_hat), ("ride", drum_ride), ("x", drum_crash)):
                pat = _pat(sec["drums"].get(key), b, nbars, sec.get("fills", {}).get(key))
                if not pat:
                    continue
                for s in range(16):
                    ch = pat[s]
                    if ch in ". -":
                        continue
                    t = t_bar + s * step_dur + (swing * 2 * step_dur if swing and s % 4 == 2 else 0.0)
                    vel = VEL.get(ch, 0.8)
                    if key == "k":
                        _add(drums, t, maker(vel), 0.75 * sg)
                        kick_times.append(t)
                    elif key == "s":
                        _add(drums, t, maker(vel, rng), 0.66 * sg, pan=0.04)
                    elif key == "c":
                        _add(drums, t, maker(vel, rng), 0.5 * sg, pan=-0.05)
                    elif key == "h":
                        open_ = ch in "oO"
                        _add(drums, t, maker(vel, open_, rng), 0.44 * sg, pan=0.22)
                    elif key == "ride":
                        _add(drums, t, maker(vel, rng), 0.55 * sg, pan=-0.28)
                    elif key == "x":
                        _add(drums, t, maker(vel, rng), 0.70 * sg, pan=0.12)

            # 段落级鼓填充/风险音
            if sec.get("toms_last") and b == nbars - 1:
                for k in range(4):
                    t = t_bar + (12 + k) * step_dur
                    _add(drums, t, drum_tom(100.0 + 22.0 * k, 0.9), 0.5 * sg, pan=-0.3 + 0.2 * k)

            # ---- 贝斯
            bass_pat = _pat(sec.get("bass"), b, nbars, sec.get("fills", {}).get("bass"))
            if bass_pat:
                root = _root_midi(chord_at(0))
                nxt_root = _root_midi(_next_chord(sections, si, b, nbars, chords))
                for s in range(16):
                    c = bass_pat[s]
                    if c in ". -":
                        continue
                    len_steps = 1
                    while s + len_steps < 16 and bass_pat[s + len_steps] == "-":
                        len_steps += 1
                    sym = chord_at(s)
                    r = _root_midi(sym)
                    pc, ivs = _chord(sym)
                    third = 3 if 3 in ivs else (4 if 4 in ivs else 7)
                    sev = 11 if 11 in ivs else 10
                    if c in "Rr":
                        m = r
                    elif c in "Oo":
                        m = r + 12
                    elif c == "5":
                        m = r + 7
                    elif c == "3":
                        m = r + third
                    elif c == "7":
                        m = r + sev
                    elif c == "n":
                        m = nxt_root - 1
                        if m - r > 7:
                            m -= 12
                    elif c == "L":
                        m = r - 5
                    else:
                        m = r
                    t = t_bar + s * step_dur + (swing * 2 * step_dur if swing and s % 4 == 2 else 0.0)
                    vel = 0.55 if c in "ro" else (0.95 if s % 4 == 0 else 0.82)
                    _add(bass, t, synth_bass(midi_hz(m), len_steps * step_dur * 0.92, vel, spec["bass_style"]),
                         0.26 * sg)

            # ---- 和弦 stab / 铜管 / 吉他
            stab_pat = _pat(sec.get("stabs"), b, nbars, sec.get("fills", {}).get("stabs"))
            if stab_pat:
                for s in range(16):
                    c = stab_pat[s]
                    if c in ". -":
                        continue
                    len_steps = 1
                    while s + len_steps < 16 and stab_pat[s + len_steps] == "-":
                        len_steps += 1
                    sym = chord_at(s)
                    if sec["chords_inst"] == "guitar":
                        notes = _power_chord(sym, 40)
                    else:
                        notes = _voicing(sym, 55, 76)
                    vel = VEL.get(c, 0.82)
                    t = t_bar + s * step_dur + (swing * 2 * step_dur if swing and s % 4 == 2 else 0.0)
                    dur = len_steps * step_dur * 0.9
                    for i, m in enumerate(notes):
                        pan = (-0.45, 0.45, -0.15, 0.15)[i % 4]
                        if sec["chords_inst"] == "guitar":
                            _add(harm, t, synth_guitar([midi_hz(m)], dur, vel, mute=(c == "m")),
                                 0.20 * sg, pan=pan * 0.7)
                        else:
                            _add(harm, t, synth_brass(midi_hz(m), dur, vel), 0.26 * sg, pan=pan)

            # ---- Pad
            if sec.get("pad"):
                for s0, s1, sym in seg:
                    notes = _voicing(sym, 52, 72, max_notes=4)
                    dur = (s1 - s0) * step_dur * 0.98
                    for i, m in enumerate(notes):
                        _add(harm, t_bar + s0 * step_dur, synth_pad(midi_hz(m), dur, 0.8), 0.06 * sg,
                             pan=(-0.55, 0.55, -0.25, 0.25)[i % 4])

            # ---- Arp / 电钢
            arp = sec.get("arp")
            if arp:
                rate = arp.get("rate", 2)
                low, high = arp.get("lo", 64), arp.get("lo", 64) + 11
                for s in range(0, 16, rate):
                    sym = chord_at(s)
                    tones = _voicing(sym, low, high)
                    seq = []
                    for oct_ in range(arp.get("oct", 1)):
                        seq += [m + 12 * oct_ for m in tones]
                    if arp.get("pattern") == "down":
                        seq = list(reversed(seq))
                    elif arp.get("pattern") == "updown":
                        seq = seq + list(reversed(seq[1:-1]))
                    if not seq:
                        continue
                    idx = ((b * 16 + s) // rate) % len(seq)
                    t = t_bar + s * step_dur + (swing * 2 * step_dur if swing and s % 4 == 2 else 0.0)
                    vel = 0.85 if s % 4 == 0 else 0.62
                    _add(harm, t, synth_keys(midi_hz(seq[idx]), rate * step_dur * 0.9, vel), 0.14 * sg,
                         pan=0.35 if (b + s) % 2 else -0.35)

            # ---- 主旋律
            if sec.get("lead"):
                lead_ev, lead_len = sec["lead"]
                phrase_steps = lead_len
                base = (b * 16 // phrase_steps) * phrase_steps
                for ev in lead_ev:
                    at = base + ev["s"]
                    if b * 16 <= at < (b + 1) * 16:
                        t = t_bar + (at - b * 16) * step_dur
                        if swing and (at % 4) == 2:
                            t += swing * 2 * step_dur
                        vel = 1.0 if ev.get("acc") else 0.86
                        _add(lead, t, synth_lead(midi_hz(ev["m"]), ev["len"] * step_dur * 0.95, vel,
                                                 spec.get("lead_style", "pulse")), 0.32 * sg, pan=0.1)
                        if sec.get("harmony"):
                            tones = _voicing(chord_at(at - b * 16), 52, 72)
                            below = [m for m in tones if 3 <= ev["m"] - m <= 9]
                            if below:
                                _add(lead, t, synth_lead(midi_hz(max(below)), ev["len"] * step_dur * 0.95,
                                                         vel * 0.75, "saw"), 0.12 * sg, pan=-0.35)

            # ---- Riser / 转场
            if sec.get("riser_last") and b == nbars - 1:
                _add(fx, t_bar, fx_riser(16 * step_dur, 0.9), 0.4 * sg, pan=0.0)
            if sec.get("riser_all") and b == 0:
                _add(fx, t_bar, fx_riser(nbars * 16 * step_dur, 0.8), 0.35 * sg, pan=0.0)

        bar_abs += nbars

    # ---- 侧链 pump（只压贝斯/和声，保留鼓的冲击）
    pump = np.ones(n_render)
    for kt in kick_times:
        i0 = int(kt * SR)
        n = min(int(0.26 * SR), n_render - i0)
        if n <= 0:
            continue
        curve = 1.0 - spec.get("pump", 0.28) * np.exp(-_t(n) / 0.09)
        pump[i0:i0 + n] = np.minimum(pump[i0:i0 + n], curve)
    for b in (bass, harm):
        b[0] *= pump
        b[1] *= pump

    # ---- 立体声延时（附点八分，乒乓）
    d = int(0.75 * 60.0 / bpm * SR)
    for src, dst, mix in ((lead, harm, 0.10),):
        for ch in (0, 1):
            s = src[ch]
            acc = np.zeros_like(s)
            for k in range(1, 4):
                shift = k * d
                if shift >= len(s):
                    break
                acc[shift:] += (0.38 ** k) * s[:-shift]
            dst[1 - ch] += acc * mix

    # ---- 混响（合成 IR，轻量 send）
    ir_n = int(1.3 * SR)
    ir_t = _t(ir_n)
    ir_rng = np.random.default_rng(99)
    ir = ir_rng.standard_normal(ir_n) * np.exp(-ir_t / 0.42)
    ir = _lp(ir, 5200.0)
    ir[: int(0.02 * SR)] = 0.0
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    send = [harm[0] + lead[0] + fx[0] * 0.8, harm[1] + lead[1] + fx[1] * 0.8]
    for ch in (0, 1):
        wet = fftconvolve(send[ch], ir, mode="full")[:n_render]
        if ch == 0:
            harm[0] += wet * 0.16
        else:
            harm[1] += wet * 0.16

    # ---- 总线混合
    mixL = drums[0] * 1.0 + bass[0] * 0.95 + harm[0] * 0.95 + lead[0] * 0.9 + fx[0] * 0.8
    mixR = drums[1] * 1.0 + bass[1] * 0.95 + harm[1] * 0.95 + lead[1] * 0.9 + fx[1] * 0.8

    # ---- 循环折叠：把尾部混响/音符尾音折回开头，循环回接无断尾
    fade = n_render - n_loop
    mixL[:fade] += mixL[n_loop:n_loop + fade]
    mixR[:fade] += mixR[n_loop:n_loop + fade]
    mixL = mixL[:n_loop]
    mixR = mixR[:n_loop]

    # ---- 去直流 + 高通 + 软限幅 + 归一化
    for ch in (0, 1):
        sig = mixL if ch == 0 else mixR
        sig -= np.mean(sig)
        sig[:] = _hp(sig, 28.0)
    peak = max(np.max(np.abs(mixL)), np.max(np.abs(mixR))) + 1e-9
    if peak > 0.95:
        mixL = np.tanh(mixL / 0.95 * 1.15) / 1.15
        mixR = np.tanh(mixR / 0.95 * 1.15) / 1.15
    peak = max(np.max(np.abs(mixL)), np.max(np.abs(mixR))) + 1e-9
    gain = 0.9 / peak
    mixL *= gain
    mixR *= gain
    rms = math.sqrt(float(np.mean(np.concatenate([mixL, mixR]) ** 2)) + 1e-12)
    target_rms = spec.get("target_rms", 0.24)
    if rms > target_rms:
        k = target_rms / rms
        mixL *= k
        mixR *= k

    stereo = np.stack([mixL, mixR], axis=1)
    return stereo, loop_s


# ---------------------------------------------------------------- 歌曲定义

def _bar_chords(section_chords, bars):
    return [section_chords[i % len(section_chords)] for i in range(bars)]


def build_main():
    sections = [
        dict(name="intro", bars=4, chords=["E5", "C5 D5", "E5", "B7"], gain=0.72,
             chords_inst="brass", bass_style="funk",
             drums=dict(k=["X...............", "X.......X.......", "X...X...X...X...", "X...X...X...X..."],
                        s=["................", "x.x.x.x.xxxxXXXX", "....X.......X...", "....X.......X.xx"],
                        h="x.c.x.c.x.c.x.oc"),
             bass=["R---------------", "R-------R-------", "R.R.R.R.R.R.R.R.", "R.R.R.R.R.R.R.R."],
             stabs=["X---------------", "X-------X-------", "X---X---X---X---", "X---X---X---X---"],
             riser_last=True, toms_last=True),
        dict(name="A", bars=8, chords=["Em7", "Cmaj7", "Am7", "B7"], chords_inst="brass",
             bass_style="funk", lead=(LEAD_A, 128), arp=dict(rate=2, pattern="up", lo=64, oct=1),
             drums=dict(k="X..x..x.X.....x.", s="....X.....g.X..g", h="x.c.x.c.x.c.x.oc",
                        ride="x...x...x...x..."),
             fills=dict(s="....X.....g.X.xx"),
             bass=["R.RO.RR.5.RO.R7.", "R.RO.RR.5.RO.5On"],
             stabs="X-.x..x-.x..x-.x"),
        dict(name="B", bars=8, chords=["C", "D", "Bm", "Em", "C", "D", "B7sus4", "B7"],
             chords_inst="brass", bass_style="funk", lead=(LEAD_B, 128), pad=True,
             arp=dict(rate=1, pattern="updown", lo=64, oct=2),
             drums=dict(k="X...x...X...x...", s="....X.......X...", c="....x.......x...",
                        h="x.o.x.o.x.o.x.o.", ride="x...x...x...x..."),
             fills=dict(s="....X.......X.xx", k="X...x...X...x.x."),
             bass="R.R.O.R.R.R.O.Rn",
             stabs="X-x-x-x-X-x-x-x-"),
        dict(name="break", bars=4, chords=["Am7", "Bm7", "Cmaj7", "D"], gain=0.6, chords_inst="brass",
             bass_style="funk", pad=True, arp=dict(rate=1, pattern="updown", lo=60, oct=2),
             drums=dict(k="X.........x.....", s="........X.......", h="x.c.x.c.x.c.x.c.", ride="x.......x......."),
             fills=dict(s="x.x.x.x.xxxxXXXX", k="X...X...X...X..."),
             bass="R-----R-5-----O-", stabs="X-------X-------",
             riser_last=True, toms_last=True),
        dict(name="A2", bars=8, chords=["Em7", "Cmaj7", "Am7", "B7"], gain=1.06, chords_inst="brass",
             bass_style="punk", lead=(LEAD_A, 128), harmony=True,
             arp=dict(rate=2, pattern="up", lo=64, oct=1),
             drums=dict(k="X..x..x.X.....x.", s="....X.....g.X..g", h="x.c.x.c.x.c.x.oc",
                        ride="x...x...x...x..."),
             fills=dict(s="....X.....g.X.xx"),
             bass=["R.RO.RR.5.RO.R7.", "R.RO.RR.5.RO.5On"],
             stabs="X-x-x-x-X-x-x-x-"),
    ]
    return dict(name="bgm_match_main", bpm=128, swing=0.20, key="E minor", pump=0.26,
                bass_style="funk", lead_style="soft", sections=sections)


def build_final():
    sections = [
        dict(name="lift", bars=4, chords=["B5", "B5", "C5", "D5"], gain=0.8, chords_inst="guitar",
             bass_style="punk", riser_all=True,
             drums=dict(k=["X...X...X...X...", "X...X...X...X...", "X...X...X...X...", "X...X...X...X..."],
                        s=["x...x...x...x...", "x.x.x.x.x.x.x.x.", "x.x.x.x.xxxxXXXX", "xxxxxxxxXXXXXXXX"],
                        h="xcxcxcxcxcxcxcxc"),
             bass="R-----R-----R-R-", stabs="X-----X-----X-X-"),
        dict(name="F1", bars=12,
             chords=["C", "D", "Bm", "Em", "C", "D", "B7sus4", "B7"],
             chords_inst="guitar", bass_style="punk", lead=(LEAD_B, 128), harmony=True,
             arp=dict(rate=1, pattern="up", lo=64, oct=2),
             drums=dict(k="X...x...X...x...", s="....X.......X...", c="....x.......x...",
                        h="xcxcxcxcxcxcxcxc"),
             fills=dict(s="....X.....x.X.xx"),
             bass="RrOrRrOrRrOrRrOn", stabs="X-x-x-x-X-x-x-xx"),
        dict(name="F2", bars=12, chords=["Em7", "Cmaj7", "Am7", "B7"], chords_inst="guitar",
             bass_style="punk", lead=(LEAD_A, 128), harmony=True,
             arp=dict(rate=1, pattern="updown", lo=64, oct=2),
             drums=dict(k="X..x..x.X..x..x.", s="....X.......X...", c="....x.......x...",
                        h="xcxcxcxoxcxcxcxo"),
             fills=dict(s="....X.....x.X.xx"),
             bass="R.ROR.RO5.5OR.On", stabs="X-.x..x-.x..x-.x"),
        dict(name="build", bars=4, chords=["C", "D", "Em", "B7"], gain=0.9, chords_inst="guitar",
             bass_style="punk", riser_all=True,
             drums=dict(k="X...X...X...X...", s=["....X.......X...", "x...x...x...x...",
                                                 "x.x.x.x.x.x.x.x.", "xxxxxxxxXXXXXXXX"],
                        h="xcxcxcxcxcxcxcxc"),
             bass="R.R.R.R.R.R.R.R.", stabs="X-x-x-x-X-x-x-x-",
             toms_last=True),
        dict(name="climax", bars=8, gain=1.08,
             chords=["C", "D", "Bm", "Em", "C", "D", "B7sus4", "B7"],
             chords_inst="guitar", bass_style="punk", lead=(LEAD_B, 128), harmony=True,
             drums=dict(k="X...x...X...x...", s="....X.......X...", c="....x.......x...",
                        x="X...............", h="xcxcxcxcxcxcxcxc"),
             fills=dict(s="....X.....x.X.xx"),
             bass="RrOrRrOrRrOrRrOn", stabs="X-x-x-x-X-x-x-xx"),
    ]
    return dict(name="bgm_match_final", bpm=160, swing=0.0, key="E minor", pump=0.32, target_rms=0.28,
                bass_style="punk", lead_style="saw", sections=sections)


SONGS = {"main": build_main(), "final": build_final()}


# ---------------------------------------------------------------- 写盘 / 统计 / 编码

def write_wav(path, stereo):
    pcm = np.clip(stereo, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype("<i2")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        n, ch = w.getnframes(), w.getnchannels()
        data = np.frombuffer(w.readframes(n), dtype="<i2").astype(np.float64) / 32768.0
    return data.reshape(-1, ch), w.getframerate()


def stats_of(stereo):
    mono = stereo.mean(axis=1)
    peak = float(np.max(np.abs(stereo)))
    rms = float(np.sqrt(np.mean(mono ** 2)))
    clip = int(np.sum(np.abs(stereo) >= 0.999))
    spec = np.abs(np.fft.rfft(mono[: min(len(mono), SR * 20)]))
    freqs = np.fft.rfftfreq(min(len(mono), SR * 20), 1.0 / SR)
    centroid = float(np.sum(freqs * spec) / (np.sum(spec) + 1e-12))
    bands = {}
    for name, lo, hi in (("low", 20, 200), ("mid", 200, 2000), ("high", 2000, 12000)):
        m = (freqs >= lo) & (freqs < hi)
        bands[name] = float(np.sqrt(np.mean(spec[m] ** 2))) if np.any(m) else 0.0
    seam = float(abs(stereo[-1, 0] - stereo[0, 0]))
    return dict(duration=round(len(stereo) / SR, 3), peak=round(peak, 4), rms=round(rms, 4),
                clipped=clip, centroid_hz=round(centroid, 1),
                bands={k: round(v, 5) for k, v in bands.items()},
                seam_delta=round(seam, 4))


def encode_ogg(wav_path, ogg_path):
    ff = "/opt/homebrew/bin/ffmpeg"
    if not os.path.exists(ff):
        ff = "ffmpeg"
    enc = subprocess.run([ff, "-hide_banner", "-encoders"], capture_output=True, text=True)
    if "libvorbis" in enc.stdout:
        cmd = [ff, "-y", "-v", "error", "-i", wav_path, "-c:a", "libvorbis", "-q:a", "6", ogg_path]
    else:
        cmd = [ff, "-y", "-v", "error", "-i", wav_path, "-c:a", "vorbis",
               "-strict", "experimental", "-b:a", "192k", ogg_path]
    subprocess.run(cmd, check=True)


def manifest_append(entry):
    os.makedirs(os.path.dirname(MANIFEST), exist_ok=True)
    with open(MANIFEST, "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def bake(key, copy_assets=True):
    spec = SONGS[key]
    print(f"[bake_bgm] 渲染 {spec['name']} ({spec['bpm']} BPM)...", flush=True)
    stereo, loop_s = render_song(spec)
    wav_path = os.path.join(RAW_DIR, spec["name"] + ".wav")
    ogg_path = os.path.join(RAW_DIR, spec["name"] + ".ogg")
    write_wav(wav_path, stereo)
    encode_ogg(wav_path, ogg_path)
    st = stats_of(stereo)
    st.update(bpm=spec["bpm"], loop_seconds=round(loop_s, 3), key=spec["key"])
    print(f"[bake_bgm] {spec['name']}: {st}", flush=True)
    if copy_assets:
        os.makedirs(OUT_DIR, exist_ok=True)
        import shutil
        shutil.copy2(ogg_path, os.path.join(OUT_DIR, spec["name"] + ".ogg"))
        print(f"[bake_bgm] 入库: Assets/_Project/Audio/Music/{spec['name']}.ogg", flush=True)
    manifest_append(dict(ts=datetime.now().isoformat(timespec="seconds"), kind="music",
                         model="local:bake_bgm.py", prompt=f"procedural {spec['key']} {spec['bpm']} BPM",
                         refs=[], output=wav_path, ogg=ogg_path, stats=st))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", choices=list(SONGS), help="只烘一段")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--stats", action="store_true")
    ap.add_argument("--no-assets", action="store_true")
    args = ap.parse_args()

    if args.list:
        for k, s in SONGS.items():
            step = 60.0 / s["bpm"] / 4.0
            bars = sum(x["bars"] for x in s["sections"])
            print(f"{k}\t{s['name']}\t{s['bpm']} BPM\t{bars} bars\t{bars * 16 * step:.2f}s")
        return

    if args.stats:
        for k, s in SONGS.items():
            p = os.path.join(RAW_DIR, s["name"] + ".wav")
            if not os.path.exists(p):
                print(f"{k}: 未烘焙 ({p})")
                continue
            stereo, _ = read_wav(p)
            print(f"{k}: {stats_of(stereo)}")
        return

    keys = [args.only] if args.only else list(SONGS)
    for k in keys:
        bake(k, copy_assets=not args.no_assets)


if __name__ == "__main__":
    sys.exit(main() or 0)