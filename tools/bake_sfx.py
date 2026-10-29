#!/usr/bin/env python3
"""bake_sfx.py —— P1 涂地对战音效离线烘焙（纯标准库，零模型依赖、零配额）

为什么不用 media-gen 的网关：那个技能的能力边界表写明「音效 SFX ❌ 无 SFX 模型」——
网关只有生图、TTS、BGM(lyria)，而 lyria 是「写一首曲子」的模型，
实测索要 0.15 秒点击音仍返回 56 秒素材（见 .agents/skills/media-gen/references/api-music.md）。
所以：**BGM/语音走网关生成，短音效走本地合成**。

用数学而不是录音生成音效：
    tone  = 振荡器(sine/square/sawtooth/triangle + 频率指数/线性斜坡) × 指数包络 [可选滤波]
    noise = 白噪声 × RBJ 双二阶(lowpass/highpass/bandpass，截止频率可扫) × 指数包络
固定种子 xorshift → **同一次输入永远产出同样的波形**，diff 友好、可复现。

用法：
    python3 tools/bake_sfx.py                     # 全量烘焙 → MediaRaw/sfx/ 且拷进 Assets
    python3 tools/bake_sfx.py --list               # 列出音色族与变体数
    python3 tools/bake_sfx.py --only shot,ui_click  # 只烘指定族（调音时用，快）
    python3 tools/bake_sfx.py --no-assets          # 只烘到 MediaRaw，不拷进 Assets
    python3 tools/bake_sfx.py --stats              # 不烘焙，只对已有产物做客观体检

产物约定：
    MediaRaw/sfx/<key>_<n>.wav          源（可重复烘，已 gitignore）
    MediaRaw/sfx/manifest.json          客观指标台账（时长/峰值/RMS/削波数）
    Assets/_Project/Audio/SFX/<key>_<n>.wav   入库件（导入设置由
                                        MediaImportPostprocessor.cs 自动套 PCM+DecompressOnLoad）

⚠ 烘完必须让 Unity 导入一次，否则新 wav 没有 .meta、播出来是哑的：
    "$HOME/.unity/bin/unity" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'

⚠ 客观指标（时长/峰值/RMS/削波）由 --stats 保证；**音色好不好听只能靠人耳**。
   agent 听不见，所以调音必须由用户实际播放 MediaRaw/sfx/ 下的文件来定。

变体与随机音高：关键游戏声（射击/溅射/命中/UI 点击）出多个变体，
播放时随机取一条 —— 这是 GamePlan M6「random-pitch SFX」验收项的素材侧基础。
"""

import argparse
import json
import math
import os
import struct
import sys
import wave

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW_DIR = os.path.join(ROOT, "MediaRaw", "sfx")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "SFX")
# 统一渲染窗：够长以容纳最长的尾音，尾部静音会被裁掉
RENDER_WINDOW = 1.6
# 低于此幅度视为静音，用于裁尾（避免留下长尾静音浪费体积）
SILENCE_FLOOR = 0.0015


# ---------------- 确定性随机（烘焙可复现，diff 友好） ----------------

_seed = 0x2F6E2B1


def _reset_seed(seed: int) -> None:
    global _seed
    _seed = seed & 0xFFFFFFFF


def _srand() -> float:
    """xorshift32 → [-1, 1)。"""
    global _seed
    s = _seed & 0xFFFFFFFF
    s ^= (s << 13) & 0xFFFFFFFF
    s ^= s >> 17
    s ^= (s << 5) & 0xFFFFFFFF
    _seed = s
    return ((s % 0x7FFFFF) / 0x7FFFFF) * 2.0 - 1.0


def _urand(a: float, b: float) -> float:
    return a + (_srand() * 0.5 + 0.5) * (b - a)


# ---------------- 基本构件 ----------------

def _wave_at(wtype: str, p: float) -> float:
    p -= math.floor(p)
    if wtype == "sine":
        return math.sin(2.0 * math.pi * p)
    if wtype == "square":
        return 1.0 if p < 0.5 else -1.0
    if wtype == "sawtooth":
        return 2.0 * p - 1.0
    if wtype == "triangle":
        if p < 0.25:
            return 4.0 * p
        if p < 0.75:
            return 2.0 - 4.0 * p
        return 4.0 * p - 4.0
    raise ValueError("未知波形: %s" % wtype)


def _expramp(v0: float, v1: float, t: float, dur: float) -> float:
    """包络：v0→v1 指数过渡（v1 是 t=dur 时的值）。"""
    if dur <= 0.0:
        return v1
    u = min(1.0, max(0.0, t / dur))
    if v0 <= 1e-6:
        v0 = 1e-6
    if v1 <= 1e-6:
        v1 = 1e-6
    return v0 * ((v1 / v0) ** u)


def _bq(buf, i0, i1, ftype, f0, f1=None, q=0.9):
    """对 buf[i0:i1] 就地做 RBJ 双二阶滤波；f1 给定时截止频率指数扫描。

    注意：滤波器状态在 i0 处从零开始，所以「只滤新写入的段」是安全用法；
    对整个缓冲区调用会把已有内容一并滤掉。
    """
    i0 = max(0, i0)
    i1 = min(len(buf), i1)
    if i1 - i0 < 4:
        return
    span = float(i1 - i0 - 1)
    x1 = x2 = y1 = y2 = 0.0
    for i in range(i0, i1):
        u = (i - i0) / span if span > 0 else 0.0
        fc = f0 if (f1 is None or f1 == f0) else f0 * ((f1 / f0) ** u)
        fc = min(max(fc, 20.0), SR * 0.45)
        w0 = 2.0 * math.pi * fc / SR
        cw = math.cos(w0)
        sw = math.sin(w0)
        alpha = sw / (2.0 * q)
        if ftype == "lowpass":
            b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
        elif ftype == "highpass":
            b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
        elif ftype == "bandpass":
            b0, b1, b2 = alpha, 0.0, -alpha
        else:
            raise ValueError("未知滤波器: %s" % ftype)
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
        b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
        x = buf[i]
        y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        buf[i] = y
        x2, x1 = x1, x
        y2, y1 = y1, y


def tone(buf, *, wtype="sine", f0=440.0, f1=None, t0=0.0, dur=0.12, peak=0.3,
         a0=1.0, a1=0.001, ramp="exp", lp=None, hp=None, phase=0.0):
    """振荡器 + 指数包络，可选带通/低通。f1 给定时 f0→f1 扫描（ramp 决定指数或线性）。"""
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    ph = phase
    for i in range(i0, i1):
        t = (i - i0) / SR
        u = (i - i0) / n if n > 0 else 0.0
        f = f0 if f1 is None else (f0 * ((f1 / f0) ** u) if ramp == "exp" else f0 + (f1 - f0) * u)
        ph += f / SR
        buf[i] += peak * _expramp(a0, a1, t, dur) * _wave_at(wtype, ph)
    if lp:
        _bq(buf, i0, i1, "lowpass", lp[0], lp[1] if len(lp) > 1 else None)
    if hp:
        _bq(buf, i0, i1, "highpass", hp[0], hp[1] if len(hp) > 1 else None)


def sat_tone(buf, *, f0=440.0, f1=None, t0=0.0, dur=0.12, peak=0.3, drive=2.5,
             a0=1.0, a1=0.001, lp=None):
    """软削波饱和音：方波感但不像 square 那样刺耳。"""
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    ph = 0.0
    span = float(n - 1) if n > 1 else 1.0
    for i in range(i0, i1):
        u = (i - i0) / span
        f = f0 if f1 is None else f0 * ((f1 / f0) ** u)
        ph += f / SR
        s = math.sin(2.0 * math.pi * ph)
        buf[i] += peak * _expramp(a0, a1, (i - i0) / SR, dur) * math.tanh(drive * s) / math.tanh(drive)
    if lp:
        _bq(buf, i0, i1, "lowpass", lp[0], lp[1] if len(lp) > 1 else None)


def noise(buf, *, t0=0.0, dur=0.1, peak=0.2, ftype="bandpass", f0=1200.0, f1=None,
          q=1.0, a0=1.0, a1=0.001):
    """白噪声 × 双二阶滤波 × 指数包络。"""
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    for i in range(i0, i1):
        buf[i] += peak * _expramp(a0, a1, (i - i0) / SR, dur) * _srand()
    if ftype:
        _bq(buf, i0, i1, ftype, f0, f1, q)


def space(buf, *, delay=0.09, fb=0.35, mix=0.20):
    """单抽头反馈延迟（简易空间感）。fb<1 保证稳定。"""
    d = int(delay * SR)
    n = len(buf)
    if d <= 0 or d >= n:
        return
    wet = [0.0] * n
    for i in range(n):
        wet[i] = buf[i] + (fb * wet[i - d] if i >= d else 0.0)
    for i in range(n):
        buf[i] += mix * wet[i]


def arp(buf, freqs, *, t_step=0.09, dur=0.26, peak=0.15, wtype="triangle", echo=None):
    """把一串音高按时序铺成短句（UI/开场/结算提示音用）。"""
    for k, f in enumerate(freqs):
        tone(buf, wtype=wtype, f0=f, t0=k * t_step, dur=dur, peak=peak)
    if echo:
        space(buf, delay=echo[0], fb=echo[1], mix=echo[2])


def bubble(buf, *, t0=0.0, count=4, f_start=280.0, f_end=760.0, spread=0.05,
           dur=0.05, peak=0.14):
    """气泡咕嘟声：一串短促上升正弦（潜墨回墨用）。"""
    for k in range(count):
        u = k / max(1, count - 1)
        f = f_start * ((f_end / f_start) ** u)
        tone(buf, wtype="sine", f0=f, f1=f * 1.25, t0=t0 + k * spread,
             dur=dur, peak=peak * (1.0 - 0.15 * u))


# ---------------- 后处理 ----------------

def _peak(buf):
    return max((abs(v) for v in buf), default=0.0)


def _rms(buf):
    if not buf:
        return 0.0
    return math.sqrt(sum(v * v for v in buf) / len(buf))


def _dc_remove(buf):
    if not buf:
        return
    mean = sum(buf) / len(buf)
    for i in range(len(buf)):
        buf[i] -= mean


def _trim_tail(buf):
    """裁掉尾部静音，并加 6ms 淡出以免结尾爆音。"""
    last = -1
    for i in range(len(buf) - 1, -1, -1):
        if abs(buf[i]) > SILENCE_FLOOR:
            last = i
            break
    if last < 0:
        return buf
    last = min(len(buf), last + int(0.01 * SR))
    out = buf[:last]
    fade = min(int(0.006 * SR), len(out))
    for i in range(fade):
        out[len(out) - 1 - i] *= i / fade
    return out


def _normalize(buf, target):
    p = _peak(buf)
    if p <= 1e-9:
        return 0.0
    g = target / p
    for i in range(len(buf)):
        buf[i] *= g
    return g


def _clip_count(buf, limit=0.999):
    return sum(1 for v in buf if abs(v) >= limit)


def _write_wav(path, buf):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    frames = bytearray()
    for v in buf:
        s = int(max(-1.0, min(1.0, v)) * 32767.0)
        frames += struct.pack("<h", s)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))


def _stats_of(buf):
    return {
        "dur_s": round(len(buf) / SR, 4),
        "peak": round(_peak(buf), 4),
        "rms": round(_rms(buf), 4),
        "clipped": _clip_count(buf),
    }


# ---------------- 音效定义 ----------------
# 每个族 = (变体数, 渲染函数)。渲染函数签名 fn(buf, v, n)，v 为变体序号、n 为变体总数。
# 变体通过音高因子与轻微参数抖动区分，避免连发时听起来像机器人。

def _vpitch(v, n, spread=0.06):
    """变体音高因子：n=1 时恒为 1；n>1 时围绕 1 均匀且小幅分布。"""
    if n <= 1:
        return 1.0
    return 1.0 + spread * ((v / (n - 1)) * 2.0 - 1.0)


def _f_shot(buf, v, n):
    """主武器射击：短促下沉哨音 + 低频身体 + 高频噪声爆点。这是签名音，最重要。

    只留扫频会听起来像「咔哒」而不是「啵/咻」——65ms 时 RMS 只有 0.042（太薄），
    所以补一层短低频身体把体积撑起来，并把扫频拉长到 75ms。
    """
    k = _vpitch(v, n, 0.07)
    tone(buf, wtype="sine", f0=820 * k, f1=240 * k, dur=0.075, peak=0.32, a1=0.01)
    sat_tone(buf, f0=420 * k, f1=190 * k, dur=0.055, peak=0.11, drive=1.8)
    tone(buf, wtype="triangle", f0=1760 * k, f1=620 * k, dur=0.022, peak=0.11, a1=0.02)
    noise(buf, t0=0.0, dur=0.055, peak=0.20, ftype="bandpass", f0=2600 * k, f1=900 * k, q=1.1)


def _f_ink_hit_ground(buf, v, n):
    """弹丸落地溅射：湿润的「啪沙」——带宽噪声下沉 + 低频闷响。"""
    k = _vpitch(v, n, 0.08)
    noise(buf, dur=0.13, peak=0.24, ftype="bandpass", f0=1900 * k, f1=380 * k, q=0.8)
    tone(buf, wtype="sine", f0=210 * k, f1=88 * k, dur=0.11, peak=0.17, a1=0.01)
    noise(buf, t0=0.006, dur=0.03, peak=0.10, ftype="highpass", f0=4200)


def _f_hit_enemy(buf, v, n):
    """命中敌人：比溅射更「硬」的撞击，音高更高、尾巴短。"""
    k = _vpitch(v, n, 0.09)
    tone(buf, wtype="triangle", f0=1250 * k, f1=680 * k, dur=0.05, peak=0.26, a1=0.01)
    noise(buf, dur=0.028, peak=0.16, ftype="highpass", f0=2400)


def _f_kill(buf, v, n):
    """击杀：两音上跳 + 噪声爆点，确认感强。"""
    k = _vpitch(v, n, 0.05)
    arp(buf, [620 * k, 940 * k], t_step=0.05, dur=0.16, peak=0.20, wtype="triangle")
    noise(buf, dur=0.10, peak=0.16, ftype="bandpass", f0=3000 * k, f1=800 * k, q=0.9)


def _f_death(buf, v, n):
    """被击杀：下行厚重音，带饱和质感。"""
    k = _vpitch(v, n, 0.05)
    sat_tone(buf, f0=430 * k, f1=125 * k, dur=0.38, peak=0.30, drive=2.2, lp=(1800, 500))
    noise(buf, dur=0.26, peak=0.14, ftype="bandpass", f0=900 * k, f1=240 * k, q=0.7)


def _f_ink_empty(buf, v, n):
    """墨量不足：干打咔哒，无音调感（要明显区别于成功射击）。"""
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.026, peak=0.24, ftype="highpass", f0=3000)
    tone(buf, wtype="square", f0=900 * k, dur=0.012, peak=0.09, a1=0.05)


def _f_ink_refill(buf, v, n):
    """潜墨回墨：气泡咕嘟上升。"""
    k = _vpitch(v, n, 0.07)
    bubble(buf, count=5, f_start=250 * k, f_end=780 * k, spread=0.048, dur=0.05, peak=0.15)
    noise(buf, dur=0.22, peak=0.07, ftype="bandpass", f0=520 * k, q=0.7)


def _f_respawn(buf, v, n):
    """复活：上升闪烁 + 气声。"""
    k = _vpitch(v, n, 0.06)
    arp(buf, [520 * k, 660 * k, 880 * k], t_step=0.055, dur=0.20, peak=0.17, wtype="sine")
    noise(buf, dur=0.30, peak=0.06, ftype="highpass", f0=4000)


def _f_countdown_tick(buf, v, n):
    """倒计时滴答：干净、短、可辨认。"""
    tone(buf, wtype="sine", f0=1150, f1=980, dur=0.05, peak=0.26, a1=0.01)
    noise(buf, dur=0.012, peak=0.10, ftype="highpass", f0=5000)


def _f_match_start(buf, v, n):
    """对局开场：三音上行，明快不拖沓。"""
    arp(buf, [440, 660, 880], t_step=0.085, dur=0.26, peak=0.19, wtype="triangle",
        echo=(0.075, 0.22, 0.14))


def _f_match_end(buf, v, n):
    """时间到：两记下行钟感，明确「结束了」。"""
    tone(buf, wtype="sine", f0=680, f1=640, dur=0.55, peak=0.26, a1=0.02)
    tone(buf, wtype="sine", f0=452, f1=430, t0=0.13, dur=0.70, peak=0.24, a1=0.02)
    space(buf, delay=0.11, fb=0.30, mix=0.16)


def _f_result_win(buf, v, n):
    """结算胜利：四音上行小句。"""
    arp(buf, [523, 659, 784, 1046], t_step=0.13, dur=0.50, peak=0.17, wtype="triangle",
        echo=(0.10, 0.28, 0.18))


def _f_result_lose(buf, v, n):
    """结算失败：三音下行，压低沉闷。"""
    arp(buf, [440, 349, 262], t_step=0.15, dur=0.55, peak=0.20, wtype="sine",
        echo=(0.12, 0.20, 0.12))
    _bq(buf, 0, len(buf), "lowpass", 2200)


def _f_ui_click(buf, v, n):
    """UI 按钮点击：极短轻点，频繁触发也不烦。"""
    k = _vpitch(v, n, 0.08)
    tone(buf, wtype="sine", f0=1800 * k, f1=1350 * k, dur=0.035, peak=0.20, a1=0.01)
    noise(buf, dur=0.014, peak=0.08, ftype="highpass", f0=5200)


def _f_ui_confirm(buf, v, n):
    """UI 确认：两音上跳。"""
    arp(buf, [700, 1050], t_step=0.055, dur=0.18, peak=0.19, wtype="sine")


def _f_ult_ready(buf, v, n):
    """潜能形态就绪：蓄力上升 + 高频闪烁，提示「可以用大招了」。"""
    tone(buf, wtype="sine", f0=300, f1=1180, dur=0.45, peak=0.20, a1=0.25)
    noise(buf, dur=0.40, peak=0.09, ftype="bandpass", f0=900, f1=3600, q=0.8)
    arp(buf, [880, 1170, 1560], t_step=0.075, dur=0.30, peak=0.09,
        wtype="sine", echo=(0.08, 0.25, 0.15))


def _f_ult_cast(buf, v, n):
    """潜能形态发动：先压后冲的扫频 + 噪声洗，气势足。"""
    sat_tone(buf, f0=190, f1=1350, dur=0.42, peak=0.30, drive=1.8, lp=(900, 4200))
    noise(buf, dur=0.42, peak=0.16, ftype="bandpass", f0=380, f1=3200, q=0.7)
    tone(buf, wtype="sine", f0=900, f1=240, t0=0.30, dur=0.28, peak=0.16, a1=0.02)


# ---------------- 武器操作（P1-D：对齐 sfx-catalog 的 7 类主武器） ----------------

def _f_roller_flick(buf, v, n):
    """滚筒挥击甩墨：上扬「唰」的风压 + 甩出瞬间的湿脆点 + 低频挥动体。

    风压用先扬后落的带通噪声叠成两段，单段扫频会听起来像「嘶」而不是「唰」。
    """
    k = _vpitch(v, n, 0.07)
    noise(buf, dur=0.16, peak=0.24, ftype="bandpass", f0=520 * k, f1=2800 * k, q=0.7)
    noise(buf, t0=0.10, dur=0.10, peak=0.16, ftype="bandpass", f0=2600 * k, f1=700 * k, q=0.8)
    noise(buf, t0=0.045, dur=0.05, peak=0.14, ftype="highpass", f0=4200)
    tone(buf, wtype="sine", f0=300 * k, f1=120 * k, dur=0.14, peak=0.16, a1=0.02)


def _f_charger_charge(buf, v, n):
    """蓄力升频蜂鸣：0.55s 内频率与振幅同步上升（a0 极小 → a1=1 即指数渐强）。

    尾端满幅由 _trim_tail 的 6ms 淡出收口，不会留爆音。
    """
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="sawtooth", f0=170 * k, f1=760 * k, dur=0.55, peak=0.20,
         a0=0.03, a1=1.0, lp=(1500, 4200))
    tone(buf, wtype="sine", f0=340 * k, f1=1520 * k, dur=0.55, peak=0.10, a0=0.02, a1=1.0)
    noise(buf, dur=0.55, peak=0.07, ftype="bandpass", f0=1200, f1=3600, q=1.2,
          a0=0.05, a1=1.0)


def _f_charger_full(buf, v, n):
    """蓄满清脆叮音：非整数倍泛音堆出金属感，短混响拉出「叮——」。"""
    k = _vpitch(v, n, 0.03)
    tone(buf, wtype="sine", f0=1760 * k, dur=0.30, peak=0.22, a1=0.001)
    tone(buf, wtype="sine", f0=2640 * k, dur=0.18, peak=0.10, a1=0.001)
    tone(buf, wtype="sine", f0=3520 * k, dur=0.10, peak=0.05, a1=0.002)
    noise(buf, dur=0.012, peak=0.08, ftype="highpass", f0=6000)
    space(buf, delay=0.055, fb=0.25, mix=0.14)


def _f_charger_shot(buf, v, n):
    """蓄力狙发射：高频爆裂 + 快速下坠的「咻」+ 墨线长尾。"""
    k = _vpitch(v, n, 0.04)
    noise(buf, dur=0.035, peak=0.28, ftype="highpass", f0=2600)
    tone(buf, wtype="sine", f0=2400 * k, f1=320 * k, dur=0.16, peak=0.26, a1=0.004)
    sat_tone(buf, f0=520 * k, f1=150 * k, dur=0.12, peak=0.14, drive=2.0, lp=(2600, 700))
    noise(buf, t0=0.02, dur=0.26, peak=0.09, ftype="bandpass", f0=3200 * k, f1=900 * k, q=0.9)
    space(buf, delay=0.075, fb=0.22, mix=0.12)


def _f_blaster_shot(buf, v, n):
    """爆破枪发射：低沉「噗通」，比小枪更闷更重（直击秒杀要有重量）。"""
    k = _vpitch(v, n, 0.05)
    sat_tone(buf, f0=300 * k, f1=85 * k, dur=0.14, peak=0.28, drive=2.6, lp=(1400, 400))
    noise(buf, dur=0.10, peak=0.16, ftype="bandpass", f0=1400 * k, f1=320 * k, q=0.7)
    tone(buf, wtype="sine", f0=180 * k, f1=70 * k, dur=0.16, peak=0.14, a1=0.01)


def _f_blaster_boom(buf, v, n):
    """爆破枪空爆重音：低频冲击 + 宽噪声爆 + 尾音混响（对齐 catalog reverb 0.22）。"""
    k = _vpitch(v, n, 0.06)
    sat_tone(buf, f0=190 * k, f1=42 * k, dur=0.45, peak=0.30, drive=2.8, lp=(1600, 220))
    noise(buf, dur=0.34, peak=0.22, ftype="lowpass", f0=2200 * k, f1=300 * k, q=0.7)
    noise(buf, t0=0.008, dur=0.07, peak=0.16, ftype="highpass", f0=3800)
    tone(buf, wtype="sine", f0=120 * k, f1=38 * k, dur=0.55, peak=0.16, a1=0.002)
    space(buf, delay=0.11, fb=0.30, mix=0.16)


def _f_slosh_throw(buf, v, n):
    """泼桶起手：墨在水桶里上抛的湿扫 + 桶壁晃动的低音。"""
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.22, peak=0.20, ftype="bandpass", f0=420 * k, f1=1900 * k, q=0.6)
    tone(buf, wtype="triangle", f0=220 * k, f1=520 * k, dur=0.18, peak=0.12, a0=0.4, a1=0.02)
    bubble(buf, t0=0.02, count=3, f_start=300 * k, f_end=520 * k,
           spread=0.05, dur=0.04, peak=0.08)


def _f_slosh_land(buf, v, n):
    """泼桶落墨：墨浪重泼——宽噪声铺开 + 低频肚音 + 气泡尾巴。"""
    k = _vpitch(v, n, 0.07)
    noise(buf, dur=0.22, peak=0.26, ftype="bandpass", f0=1500 * k, f1=260 * k, q=0.6)
    noise(buf, t0=0.015, dur=0.16, peak=0.18, ftype="lowpass", f0=900 * k, q=0.8)
    tone(buf, wtype="sine", f0=170 * k, f1=62 * k, dur=0.24, peak=0.18, a1=0.01)
    bubble(buf, t0=0.09, count=4, f_start=220 * k, f_end=620 * k,
           spread=0.045, dur=0.045, peak=0.09)
    space(buf, delay=0.06, fb=0.20, mix=0.10)


# ---------------- 移动与潜墨（P2-A 双形态机动） ----------------

def _f_squid_in(buf, v, n):
    """潜墨入水：下沉的「噗通」+ 被墨闷住的噪声 + 气泡。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=560 * k, f1=140 * k, dur=0.13, peak=0.20, a1=0.01)
    noise(buf, dur=0.12, peak=0.16, ftype="lowpass", f0=1600 * k, f1=380 * k, q=0.8)
    bubble(buf, t0=0.05, count=3, f_start=260 * k, f_end=520 * k,
           spread=0.04, dur=0.035, peak=0.07)


def _f_squid_out(buf, v, n):
    """浮出墨面：上扬的「啵」+ 破水高频点（入水音的镜像）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=160 * k, f1=640 * k, dur=0.12, peak=0.22, a0=0.5, a1=0.01)
    noise(buf, dur=0.10, peak=0.16, ftype="bandpass", f0=700 * k, f1=2200 * k, q=0.8)
    noise(buf, t0=0.02, dur=0.05, peak=0.10, ftype="highpass", f0=3600)


def _f_swim_splash(buf, v, n):
    """高速潜行的水花：极短湿噪声，可高频重复触发不糊。"""
    k = _vpitch(v, n, 0.08)
    noise(buf, dur=0.09, peak=0.20, ftype="bandpass", f0=1700 * k, f1=600 * k, q=0.8)
    tone(buf, wtype="sine", f0=360 * k, f1=200 * k, dur=0.07, peak=0.10, a1=0.01)


def _f_step_ink_bubble(buf, v, n):
    """踩己方墨水：咕嘟气泡 + 轻轻一拍水花（比 ink_refill 更短更闷）。"""
    k = _vpitch(v, n, 0.10)
    noise(buf, dur=0.05, peak=0.14, ftype="bandpass", f0=1400 * k, f1=700 * k, q=1.0)
    bubble(buf, count=3, f_start=240 * k, f_end=680 * k, spread=0.038, dur=0.045, peak=0.11)


def _f_step_dry(buf, v, n):
    """踩干地：干净短促的鞋底轻叩，音高抖动大（走路的随机感）。"""
    k = _vpitch(v, n, 0.10)
    noise(buf, dur=0.035, peak=0.16, ftype="bandpass", f0=1800 * k, q=1.2)
    tone(buf, wtype="sine", f0=150 * k, f1=95 * k, dur=0.05, peak=0.12, a1=0.02)


def _f_step_enemy(buf, v, n):
    """踩敌方墨水：粘稠「咕叽」+ 轻微嘶声，明显区别于己方墨水。"""
    k = _vpitch(v, n, 0.09)
    noise(buf, dur=0.09, peak=0.18, ftype="bandpass", f0=900 * k, f1=380 * k, q=1.4)
    tone(buf, wtype="triangle", f0=210 * k, f1=130 * k, dur=0.08, peak=0.12, a1=0.02)
    noise(buf, t0=0.01, dur=0.06, peak=0.07, ftype="highpass", f0=4600)


def _f_ink_drip(buf, v, n):
    """墨滴滴落：极短的高频下坠水滴（P1-B 连续滴墨的伴音）。"""
    k = _vpitch(v, n, 0.12)
    tone(buf, wtype="sine", f0=1250 * k, f1=420 * k, dur=0.06, peak=0.18, a1=0.005)
    noise(buf, dur=0.012, peak=0.05, ftype="highpass", f0=5200)


def _f_jump(buf, v, n):
    """起跳：短促上扬的气流。"""
    k = _vpitch(v, n, 0.07)
    tone(buf, wtype="triangle", f0=320 * k, f1=680 * k, dur=0.12, peak=0.16, a1=0.02)
    noise(buf, dur=0.10, peak=0.10, ftype="bandpass", f0=900 * k, f1=2200 * k, q=0.9)


def _f_land(buf, v, n):
    """落地：低频闷响 + 一小片干噪声。"""
    k = _vpitch(v, n, 0.07)
    tone(buf, wtype="sine", f0=150 * k, f1=60 * k, dur=0.12, peak=0.20, a1=0.01)
    noise(buf, dur=0.07, peak=0.12, ftype="lowpass", f0=900 * k, q=0.9)


# ---------------- 战斗反馈补充（P6-A 打击反馈） ----------------

def _f_hit_marker(buf, v, n):
    """打击标记：极短高位「嗒」，连击升调由运行时控制 pitch，这里只留干净瞬态。"""
    k = _vpitch(v, n, 0.04)
    tone(buf, wtype="sine", f0=2600 * k, f1=2200 * k, dur=0.028, peak=0.24, a1=0.02)
    noise(buf, dur=0.010, peak=0.10, ftype="highpass", f0=6000)


def _f_hurt(buf, v, n):
    """受伤：比 death 短得多的下坠硬音，可连续触发。"""
    k = _vpitch(v, n, 0.07)
    sat_tone(buf, f0=520 * k, f1=190 * k, dur=0.12, peak=0.24, drive=2.2, lp=(2600, 700))
    noise(buf, dur=0.07, peak=0.14, ftype="bandpass", f0=1800 * k, q=0.8)


def _f_refill_full(buf, v, n):
    """墨罐回满：两音上跳的小提示（不是大招就绪，别做太亮）。"""
    arp(buf, [880, 1320], t_step=0.05, dur=0.12, peak=0.14, wtype="sine")
    tone(buf, wtype="sine", f0=1760, t0=0.05, dur=0.10, peak=0.06, a1=0.01)


def _f_low_ink_warning(buf, v, n):
    """墨水不足警报：两声固定音高的方波「嘀嘀」，不做变体变调（警报必须可辨识）。"""
    tone(buf, wtype="square", f0=880, dur=0.07, peak=0.16, a1=0.05, lp=(2400,))
    tone(buf, wtype="square", f0=880, t0=0.11, dur=0.09, peak=0.16, a1=0.03, lp=(2400,))
    noise(buf, dur=0.006, peak=0.04, ftype="highpass", f0=5000)


# ---------------- 副武器与大招（P3-B） ----------------

def _f_bomb_throw(buf, v, n):
    """投掷炸弹：短促上抛的风声。"""
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.14, peak=0.18, ftype="bandpass", f0=700 * k, f1=2400 * k, q=0.9)
    tone(buf, wtype="sine", f0=260 * k, f1=520 * k, dur=0.10, peak=0.08, a0=0.5, a1=0.02)


def _f_bomb_beep(buf, v, n):
    """引信提示音：极短高位电子「哔」，落地后按引信节奏重复。"""
    tone(buf, wtype="square", f0=1480, dur=0.045, peak=0.16, a1=0.01, lp=(4000,))
    tone(buf, wtype="sine", f0=2960, dur=0.03, peak=0.05, a1=0.01)


def _f_bomb_explode(buf, v, n):
    """炸弹爆炸：低频冲击 + 宽噪声 + 长尾混响（全库最重的打击音）。"""
    k = _vpitch(v, n, 0.05)
    sat_tone(buf, f0=160 * k, f1=36 * k, dur=0.55, peak=0.30, drive=3.0, lp=(1800, 200))
    noise(buf, dur=0.45, peak=0.24, ftype="lowpass", f0=2600 * k, f1=260 * k, q=0.7)
    noise(buf, t0=0.01, dur=0.09, peak=0.18, ftype="highpass", f0=3600)
    tone(buf, wtype="sine", f0=90 * k, f1=32 * k, dur=0.70, peak=0.16, a1=0.002)
    space(buf, delay=0.13, fb=0.32, mix=0.18)


def _f_special_slam(buf, v, n):
    """超级降落砸地：先落体啸叫 0.3s，再低频猛砸 + 震波尾。"""
    tone(buf, wtype="sine", f0=1500, f1=180, dur=0.30, peak=0.14, a0=0.3, a1=0.4)
    sat_tone(buf, f0=140, f1=32, t0=0.26, dur=0.55, peak=0.32, drive=3.0, lp=(1600, 180))
    noise(buf, t0=0.26, dur=0.40, peak=0.22, ftype="lowpass", f0=2400, f1=240, q=0.7)
    noise(buf, t0=0.27, dur=0.08, peak=0.16, ftype="highpass", f0=3400)
    space(buf, delay=0.12, fb=0.30, mix=0.18)


# ---------------- 比赛流程补充（P4-D / P5-C 横幅音） ----------------

def _f_ready(buf, v, n):
    """READY 提示：两音上跳，比 match_start 更短更克制。"""
    arp(buf, [523, 784], t_step=0.10, dur=0.20, peak=0.16, wtype="sine",
        echo=(0.07, 0.20, 0.12))


def _f_go_horn(buf, v, n):
    """GO 号角：饱和基音 + 五度泛音，短促有冲劲。"""
    sat_tone(buf, f0=330, dur=0.42, peak=0.22, drive=2.2, a0=0.6, a1=0.35, lp=(2600,))
    tone(buf, wtype="sawtooth", f0=495, dur=0.42, peak=0.12, a0=0.5, a1=0.30, lp=(3000,))
    tone(buf, wtype="sine", f0=165, dur=0.42, peak=0.10, a0=0.6, a1=0.30)
    space(buf, delay=0.095, fb=0.25, mix=0.14)


def _f_one_minute(buf, v, n):
    """剩余一分钟：两记下行提示 + 一层空气感，压得住但别抢镜。"""
    arp(buf, [392, 294], t_step=0.14, dur=0.32, peak=0.16, wtype="triangle")
    noise(buf, t0=0.02, dur=0.30, peak=0.05, ftype="highpass", f0=4200)
    space(buf, delay=0.10, fb=0.24, mix=0.14)


def _f_times_up(buf, v, n):
    """时间到：三音下行 + 低音铺底，比 match_end 的双钟更有「终局」感。"""
    arp(buf, [523, 392, 262], t_step=0.13, dur=0.45, peak=0.18, wtype="triangle",
        echo=(0.10, 0.22, 0.14))
    tone(buf, wtype="sine", f0=131, dur=0.80, peak=0.10, a0=0.4, a1=0.02)


# ---------------- UI 补充（P4-A 弹性 UI） ----------------

def _f_ui_hover(buf, v, n):
    """悬停：25ms 极轻高音，只为「指到了」的触感。"""
    tone(buf, wtype="sine", f0=2100, f1=2400, dur=0.022, peak=0.16, a1=0.02)


def _f_ui_toggle(buf, v, n):
    """开关切换：两声快速「嗒-嗒」，第二声更闷，形成明确的开关落位感。"""
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="sine", f0=1180 * k, f1=1560 * k, dur=0.030, peak=0.18, a0=0.8, a1=0.02)
    tone(buf, wtype="sine", f0=1760 * k, f1=1400 * k, t0=0.035, dur=0.035, peak=0.14, a1=0.01)
    noise(buf, dur=0.010, peak=0.07, ftype="highpass", f0=6000)


def _f_ui_back(buf, v, n):
    """返回/取消：两音下行（与 ui_confirm 的上行互为正反）。"""
    arp(buf, [880, 660], t_step=0.045, dur=0.10, peak=0.16, wtype="triangle")


def _f_ui_slider(buf, v, n):
    """滑杆步进：比 ui_click 更窄更钝的细碎咔哒，拖动时不刺耳。"""
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.012, peak=0.14, ftype="bandpass", f0=2600 * k, q=2.0)
    tone(buf, wtype="sine", f0=1500 * k, dur=0.015, peak=0.06, a1=0.05)


def _f_ui_error(buf, v, n):
    """操作失败：两声低方波「嗡嗡」，低沉不尖锐（不是警报，别和 low_ink 混淆）。"""
    tone(buf, wtype="square", f0=196, dur=0.10, peak=0.16, a1=0.05, lp=(1200,))
    tone(buf, wtype="square", f0=175, t0=0.125, dur=0.13, peak=0.16, a1=0.04, lp=(1200,))
    noise(buf, dur=0.02, peak=0.05, ftype="lowpass", f0=800)


# key → (变体数, 渲染函数, 归一化目标峰值)
# 峰值目标：战斗反馈音给足（0.30~0.34），UI/提示音压低（0.20~0.26），避免 UI 抢戏。
# P1-D 起统一在 0.22~0.34 分档，全部留足 headroom，混音音量交给 AudioManager 控制。
SFX = {
    # 主武器与涂地（签名音，变体多）
    "shot":           (3, _f_shot,           0.34),
    "ink_hit_ground": (3, _f_ink_hit_ground, 0.30),
    # 武器操作（P1-D：7 类主武器）
    "roller_flick":   (2, _f_roller_flick,   0.32),
    "charger_charge": (1, _f_charger_charge, 0.26),
    "charger_full":   (1, _f_charger_full,   0.26),
    "charger_shot":   (1, _f_charger_shot,   0.34),
    "blaster_shot":   (2, _f_blaster_shot,   0.30),
    "blaster_boom":   (2, _f_blaster_boom,   0.34),
    "slosh_throw":    (1, _f_slosh_throw,    0.28),
    "slosh_land":     (2, _f_slosh_land,     0.32),
    # 移动与潜墨（P2-A）
    "squid_in":       (1, _f_squid_in,       0.26),
    "squid_out":      (1, _f_squid_out,      0.26),
    "swim_splash":    (2, _f_swim_splash,    0.24),
    "step_ink_bubble": (3, _f_step_ink_bubble, 0.22),
    "step_dry":       (3, _f_step_dry,       0.22),
    "step_enemy":     (3, _f_step_enemy,     0.22),
    "ink_drip":       (3, _f_ink_drip,       0.22),
    "jump":           (1, _f_jump,           0.24),
    "land":           (1, _f_land,           0.26),
    # 战斗反馈
    "hit_enemy":      (2, _f_hit_enemy,      0.30),
    "hit_marker":     (2, _f_hit_marker,     0.30),
    "hurt":           (1, _f_hurt,           0.32),
    "kill":           (1, _f_kill,           0.30),
    "death":          (1, _f_death,          0.32),
    "respawn":        (1, _f_respawn,        0.26),
    # 墨水资源
    "ink_empty":      (1, _f_ink_empty,      0.26),
    "ink_refill":     (1, _f_ink_refill,     0.24),
    "refill_full":    (1, _f_refill_full,    0.24),
    "low_ink_warning": (1, _f_low_ink_warning, 0.30),
    # 副武器与大招（P3-B）
    "bomb_throw":     (1, _f_bomb_throw,     0.26),
    "bomb_beep":      (1, _f_bomb_beep,      0.24),
    "bomb_explode":   (1, _f_bomb_explode,   0.34),
    "ult_ready":      (1, _f_ult_ready,      0.24),
    "ult_cast":       (1, _f_ult_cast,       0.30),
    "special_slam":   (1, _f_special_slam,   0.34),
    # 比赛流程（M5 / P4-D）
    "countdown_tick": (1, _f_countdown_tick, 0.26),
    "match_start":    (1, _f_match_start,    0.26),
    "ready":          (1, _f_ready,          0.26),
    "go_horn":        (1, _f_go_horn,        0.30),
    "one_minute":     (1, _f_one_minute,     0.28),
    "match_end":      (1, _f_match_end,      0.28),
    "times_up":       (1, _f_times_up,       0.28),
    "result_win":     (1, _f_result_win,     0.26),
    "result_lose":    (1, _f_result_lose,    0.24),
    # UI
    "ui_hover":       (1, _f_ui_hover,       0.22),
    "ui_click":       (2, _f_ui_click,       0.22),
    "ui_confirm":     (1, _f_ui_confirm,     0.24),
    "ui_toggle":      (1, _f_ui_toggle,      0.24),
    "ui_back":        (1, _f_ui_back,        0.24),
    "ui_slider":      (1, _f_ui_slider,      0.22),
    "ui_error":       (1, _f_ui_error,       0.26),
}

# 保留 key 清单（供 M5/M6 建 SfxLibrary SO 时对照；改这里要同步文档）
GROUPS = {
    "主武器与涂地": ["shot", "ink_hit_ground"],
    "武器操作":     ["roller_flick", "charger_charge", "charger_full", "charger_shot",
                     "blaster_shot", "blaster_boom", "slosh_throw", "slosh_land"],
    "移动与潜墨":   ["squid_in", "squid_out", "swim_splash", "step_ink_bubble",
                     "step_dry", "step_enemy", "ink_drip", "jump", "land"],
    "战斗反馈":     ["hit_enemy", "hit_marker", "hurt", "kill", "death", "respawn"],
    "墨水资源":     ["ink_empty", "ink_refill", "refill_full", "low_ink_warning"],
    "副武器与大招": ["bomb_throw", "bomb_beep", "bomb_explode", "ult_ready", "ult_cast",
                     "special_slam"],
    "比赛流程":     ["countdown_tick", "match_start", "ready", "go_horn", "one_minute",
                     "match_end", "times_up", "result_win", "result_lose"],
    "UI":           ["ui_hover", "ui_click", "ui_confirm", "ui_toggle", "ui_back",
                     "ui_slider", "ui_error"],
}

# 允许超过 0.9s 的长音（流程/胜负提示天然带尾音，不按「过长」告警）
LONG_OK = {
    "match_end", "result_win", "result_lose", "times_up", "one_minute",
    "bomb_explode", "special_slam", "go_horn",
}


# ---------------- 烘焙 ----------------

def _key_hash(key: str) -> int:
    """稳定字符串散列（FNV-1a）。

    不能用内置 hash()：Python 3 对 str 的 hash 逐进程随机化（PYTHONHASHSEED），
    会让「同一次输入产出同样波形」的承诺失效——每次跑出的 wav 都不一样，diff 全是噪音。
    """
    h = 0x811C9DC5
    for ch in key.encode("utf-8"):
        h ^= ch
        h = (h * 0x01000193) & 0xFFFFFFFF
    return h


def bake_one(key, idx, total, fn, target_peak):
    """渲染单个变体，返回 (浮点缓冲, 客观指标)。"""
    # 每个变体独立固定种子：加变体或改顺序不会影响已有变体的波形
    _reset_seed(0x2F6E2B1 ^ (_key_hash(key) & 0xFFFF) ^ (idx * 0x9E3779B1))
    buf = [0.0] * int(RENDER_WINDOW * SR)
    fn(buf, idx, total)
    _dc_remove(buf)
    buf = _trim_tail(buf)
    _normalize(buf, target_peak)
    return buf, _stats_of(buf)


def bake(only=None, write_assets=True):
    keys = [k for k in SFX if (only is None or k in only)]
    if not keys:
        print("没有匹配的音效族。可用: %s" % ", ".join(sorted(SFX)))
        return 1

    os.makedirs(RAW_DIR, exist_ok=True)
    if write_assets:
        os.makedirs(OUT_DIR, exist_ok=True)

    manifest = []
    total_files = 0
    print("烘焙音效 → %s" % RAW_DIR)
    for key in keys:
        total, fn, target = SFX[key]
        for idx in range(total):
            buf, st = bake_one(key, idx, total, fn, target)
            name = "%s_%d.wav" % (key, idx + 1)
            raw_path = os.path.join(RAW_DIR, name)
            _write_wav(raw_path, buf)
            rel = os.path.relpath(raw_path, ROOT)
            if write_assets:
                _write_wav(os.path.join(OUT_DIR, name), buf)
            total_files += 1
            warn = ""
            if st["clipped"] > 0:
                warn += " ⚠削波%d" % st["clipped"]
            if st["dur_s"] < 0.01:
                warn += " ⚠过短"
            if st["dur_s"] > 0.9 and key not in LONG_OK:
                warn += " ⚠过长"
            if st["peak"] < 0.15:
                warn += " ⚠偏轻"
            print("  %-22s %5.3fs peak=%.3f rms=%.3f%s" %
                  (name, st["dur_s"], st["peak"], st["rms"], warn))
            manifest.append({"key": key, "variant": idx + 1, "file": rel,
                             "out": os.path.relpath(os.path.join(OUT_DIR, name), ROOT) if write_assets else "",
                             **st})

    with open(os.path.join(RAW_DIR, "manifest.json"), "w", encoding="utf-8") as fh:
        json.dump({"sr": SR, "files": manifest}, fh, ensure_ascii=False, indent=1)
    print("\n共 %d 个文件；台账: %s" % (total_files, os.path.relpath(os.path.join(RAW_DIR, "manifest.json"), ROOT)))
    if write_assets:
        print("入库件: %s" % os.path.relpath(OUT_DIR, ROOT))
        print("\n⚠ 接下一步：让 Unity 导入一次（否则新 wav 没有 .meta、播出来是哑的）：")
        print("    \"$HOME/.unity/bin/unity\" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'")
    print("\n⚠ 音色是否好听只能靠人耳：请实际播放 MediaRaw/sfx/ 下的文件确认。")
    return 0


def print_registry():
    print("音效族清单（%d 族 / %d 个文件）\n" % (len(SFX), sum(v[0] for v in SFX.values())))
    for group, keys in GROUPS.items():
        print("[%s]" % group)
        for k in keys:
            n = SFX[k][0]
            print("  %-18s %d 个变体%s" % (k, n, "（随机取一条播放）" if n > 1 else ""))
        print()


def stats_only():
    """不烘焙，只对 MediaRaw/sfx 下已有 wav 做客观体检。"""
    if not os.path.isdir(RAW_DIR):
        print("还没有产物目录: %s" % RAW_DIR)
        return 1
    names = sorted(f for f in os.listdir(RAW_DIR) if f.endswith(".wav"))
    if not names:
        print("没有产物。先跑: python3 tools/bake_sfx.py")
        return 1
    print("%-22s %8s %7s %7s %8s" % ("文件", "时长s", "峰值", "RMS", "削波"))
    bad = 0
    for nm in names:
        with wave.open(os.path.join(RAW_DIR, nm), "rb") as w:
            raw = w.readframes(w.getnframes())
            n = w.getnframes()
            ch = w.getnchannels()
            sr = w.getframerate()
        vals = [struct.unpack("<h", raw[i:i + 2])[0] / 32768.0 for i in range(0, len(raw), 2)]
        pk = _peak(vals)
        cl = _clip_count(vals, 0.9995)
        flag = ""
        if ch != 1 or sr != SR:
            flag = " ⚠%dch/%dHz" % (ch, sr)
            bad += 1
        if cl > 0:
            flag += " ⚠削波"
            bad += 1
        print("%-22s %8.3f %7.3f %7.3f %8d%s" % (nm, n / sr, pk, _rms(vals), cl, flag))
    print("\n共 %d 个文件，%d 项异常。" % (len(names), bad))
    return 0


def main():
    ap = argparse.ArgumentParser(description="P1 涂地对战音效离线烘焙（纯标准库）")
    ap.add_argument("--only", default=None, help="只烘指定族，逗号分隔，如 shot,ui_click")
    ap.add_argument("--list", action="store_true", help="列出音色族与变体数后退出")
    ap.add_argument("--stats", action="store_true", help="不烘焙，只对已有产物做客观体检")
    ap.add_argument("--no-assets", action="store_true", help="只烘到 MediaRaw，不拷进 Assets")
    a = ap.parse_args()

    if a.list:
        print_registry()
        return 0
    if a.stats:
        return stats_only()

    only = None
    if a.only:
        only = {s.strip() for s in a.only.split(",") if s.strip()}
        unknown = only - set(SFX)
        if unknown:
            print("未知音效族: %s\n可用: %s" % (", ".join(sorted(unknown)), ", ".join(sorted(SFX))), file=sys.stderr)
            return 2
    return bake(only=only, write_assets=not a.no_assets)


if __name__ == "__main__":
    sys.exit(main())