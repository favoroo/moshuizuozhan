#!/usr/bin/env python3
"""bake_sfx.py —— P1 涂地对战音效离线烘焙（纯标准库，零模型依赖、零配额）

为什么不用 media-gen 的网关：那个技能的能力边界表写明「音效 SFX ❌ 无 SFX 模型」——
网关只有生图、TTS、BGM(lyria)，而 lyria 是「写一首曲子」的模型，
实测索要 0.15 秒点击音仍返回 56 秒素材（见 .agents/skills/media-gen/references/api-music.md）。
所以：**BGM/语音走网关生成，短音效走本地合成**。

用数学而不是录音生成音效。构件（2026-11 全面升级，对齐仓库内 INKWAVE 参考实现
`inkwave-aah-research/src/audio/`，它的 94 个音效全是 Web Audio 程序化合成）：

    振荡器   tone / sat_tone（tanh 饱和）/ fm_tone（2 算子 FM，调制指数指数衰减）
    分音     partials（非谐比率堆叠 → 金属/铃/玻璃）
    噪声     noise(kind=white|pink|brown) —— 白只给瞬态与气声，粉给湿/厚的体，棕给低频轰鸣。
             采样自 2s 无缝噪声床（周期 = LOOP_DUR），循环音因此天然对齐
    滤波     RBJ 双二阶 lowpass/highpass/bandpass（可扫频、可 LFO 抖动截止），破坏性 in-place；
             **叠层请走 tone/noise，它们内部渲染到临时段滤波后再相加**
    墨粒     bub / bloops（下坠）/ plips（上滑）/ splat（标准 5 层墨花）
    循环床   grain_bed（bubble/squelch/rain/sizzle 环形颗粒，周期恰为 LOOP_DUR → 无缝）
    空间     space（单抽头，小音）/ room（多抽头早期反射，爆炸/雷/号角）
    其他     click（瞬态）/ air（气声）/ drive（饱和）/ _am_sine / _am_square（循环音起伏）

后处理链：DC 去除 → 循环交叉淡化（线性，对同相内容即恒等）或裁尾 → _soft_limit(1.3)
（压瞬态尖峰，同峰值下听感更厚）→ 峰值归一化。
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

变体与随机音高：关键游戏声（射击/溅射/命中/脚步/UI 点击）出多个变体，
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
# 循环音效渲染窗与无缝参数
RENDER_WINDOW_LOOP = 2.15
LOOP_DUR = 1.8
XFADE_DUR = 0.25
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


# 滤波系数每 32 个样本才重算一次：扫频照样够平滑（LFO 最高 20Hz），
# 但省掉每样本两次三角函数——层数变多后这是烘焙时间的主要开销。
_BQ_STEP = 32


def _bq(buf, i0, i1, ftype, f0, f1=None, q=0.9, wob=None, t_off=0.0):
    """对 buf[i0:i1] 就地做 RBJ 双二阶滤波；f1 给定时截止频率指数扫描。

    这是**破坏性**滤波（会覆盖该段内容），用于整体频率整形是对的；
    但「往后叠一层」绝不能直接调它——那会把该段已有内容抹掉。
    叠层请走 tone()/noise()/sat_tone()，它们内部渲染到临时段、滤波后再相加。

    wob=(rate_hz, depth_hz) 叠加正弦截止抖动——循环音床的「有机运动」就靠它。
    循环音请把 rate 取成 1/1.8s≈0.5556Hz 的整数倍（5/10/15/20Hz），否则首尾交叉淡化对不齐。
    t_off 是 i0 在整段中的绝对时间（秒），只有 wob 的相位用它。

    注意：滤波器状态在 i0 处从零开始。
    """
    i0 = max(0, i0)
    i1 = min(len(buf), i1)
    if i1 - i0 < 4:
        return
    span = float(i1 - i0 - 1)
    x1 = x2 = y1 = y2 = 0.0
    b0 = b1 = b2 = a1 = a2 = 0.0
    for i in range(i0, i1):
        if (i - i0) % _BQ_STEP == 0:
            u = (i - i0) / span if span > 0 else 0.0
            fc = f0 if (f1 is None or f1 == f0) else f0 * ((f1 / f0) ** u)
            if wob:
                fc += wob[1] * math.sin(2.0 * math.pi * wob[0] * (i - i0 + t_off * SR) / SR)
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
                # RBJ「恒定裙边增益、峰值增益 = Q」形式，与 Web Audio 的 bandpass 一致
                # （INKWAVE 就是用 Web Audio 的）。共振湿共振峰必须用这一形式才有电平：
                # 换成 0dB 峰值形式，Q=6 的带通输出只剩 3% 振幅，层与层根本配不平。
                b0, b1, b2 = sw / 2.0, 0.0, -sw / 2.0
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
         a0=1.0, a1=0.001, ramp="exp", lp=None, hp=None, phase=0.0,
         vib=None, wob=None, detune=0.0):
    """振荡器 + 指数包络，可选带通/低通。f1 给定时 f0→f1 扫描（ramp 决定指数或线性）。

    vib=(rate_hz, depth_ratio) 频率颤音（裁判哨/失真长音）；
    detune 是相对音高偏移（0.003 ≈ 5 音分），叠两只做拍频是「合成器没有 vibrato」时的替代；
    wob 透传给滤波器的截止抖动，lp/hp 用。
    """
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    if i1 <= i0:
        return
    seg = [0.0] * (i1 - i0)
    ph = phase
    for j in range(len(seg)):
        t = j / SR
        u = j / n if n > 0 else 0.0
        f = f0 if f1 is None else (f0 * ((f1 / f0) ** u) if ramp == "exp" else f0 + (f1 - f0) * u)
        if detune:
            f *= (1.0 + detune)
        if vib:
            f *= 1.0 + vib[1] * math.sin(2.0 * math.pi * vib[0] * t)
        ph += f / SR
        seg[j] = peak * _expramp(a0, a1, t, dur) * _wave_at(wtype, ph)
    if lp:
        _bq(seg, 0, len(seg), "lowpass", lp[0], lp[1] if len(lp) > 1 else None, wob=wob, t_off=t0)
    if hp:
        _bq(seg, 0, len(seg), "highpass", hp[0], hp[1] if len(hp) > 1 else None, wob=wob, t_off=t0)
    for j in range(len(seg)):  # 相加而不是覆盖：叠层不会互相抹掉
        buf[i0 + j] += seg[j]


def sat_tone(buf, *, f0=440.0, f1=None, t0=0.0, dur=0.12, peak=0.3, drive=2.5,
             a0=1.0, a1=0.001, lp=None):
    """软削波饱和音：方波感但不像 square 那样刺耳。"""
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    if i1 <= i0:
        return
    seg = [0.0] * (i1 - i0)
    span = float(n - 1) if n > 1 else 1.0
    ph = 0.0
    for j in range(len(seg)):
        u = j / span
        f = f0 if f1 is None else f0 * ((f1 / f0) ** u)
        ph += f / SR
        s = math.sin(2.0 * math.pi * ph)
        seg[j] = peak * _expramp(a0, a1, j / SR, dur) * math.tanh(drive * s) / math.tanh(drive)
    if lp:
        _bq(seg, 0, len(seg), "lowpass", lp[0], lp[1] if len(lp) > 1 else None)
    for j in range(len(seg)):
        buf[i0 + j] += seg[j]


# ---------------- 着色噪声床（白 / 粉 / 棕） ----------------

# INKWAVE 的噪声源分三色：白只留给瞬态与气声，粉给「湿/厚」的体，棕给低频轰鸣。
# P1 旧引擎只有白噪声——湿感与体积感就是在这里丢的。
# 床长取 LOOP_DUR：这样循环音里任何一层噪声都天然以 1.8s 为周期，
# 首尾交叉淡化变成恒等变换（配合线性淡化），循环接缝彻底干净。
_BEDS = {}
_BED_RMS = 0.5


def _bed(kind):
    """返回 kind 色的无缝噪声床（list[float]，周期 = LOOP_DUR），首次调用时生成。"""
    global _seed
    bed = _BEDS.get(kind)
    if bed is not None:
        return bed
    if kind not in ("white", "pink", "brown"):
        raise ValueError("未知噪声色: %s" % kind)

    # 用专属固定种子生成，并保存/还原当前随机流：床的生成不干扰变体的确定性
    saved = _seed
    _reset_seed(0xBED0000 + sum(ord(c) for c in kind))
    n = int(LOOP_DUR * SR)
    raw = [0.0] * (n * 2)
    if kind == "pink":
        # Paul Kellett 7 级单极点级联（粉噪声的标准近似）
        b = [0.0] * 7
        for i in range(n * 2):
            w = _srand()
            b[0] = 0.99886 * b[0] + w * 0.0555179
            b[1] = 0.99332 * b[1] + w * 0.0750759
            b[2] = 0.96900 * b[2] + w * 0.1538520
            b[3] = 0.86650 * b[3] + w * 0.3104856
            b[4] = 0.55000 * b[4] + w * 0.5329522
            b[5] = -0.7616 * b[5] - w * 0.0168980
            raw[i] = b[0] + b[1] + b[2] + b[3] + b[4] + b[5] + b[6] + w * 0.5362
            b[6] = w * 0.115926
    elif kind == "brown":
        last = 0.0
        for i in range(n * 2):
            last = (last + 0.02 * _srand()) / 1.02
            raw[i] = last
    else:
        for i in range(n * 2):
            raw[i] = _srand()
    _seed = saved

    # 取第二圈（滤波器已进稳态），再绕行两遍一极点 DC 阻断让首尾严格相接。
    # 两遍必须读**同一个**输入 src；读自己的输出会串成级联滤波并在接缝处爆出尖峰。
    src = raw[n:]
    y = [0.0] * n
    prev_x = src[n - 1]
    prev_y = 0.0
    for _pass in range(2):
        for i in range(n):
            prev_y = src[i] - prev_x + 0.995 * prev_y
            prev_x = src[i]
            y[i] = prev_y

    r = _rms(y)
    if r > 1e-9:
        g = _BED_RMS / r
        y = [v * g for v in y]
    p = _peak(y)
    if p > 0.99:  # 限制峰值因子：粉/棕的尾巴不能顶掉整只音效的归一化
        g = 0.99 / p
        y = [v * g for v in y]
    _BEDS[kind] = y
    return y


def noise(buf, *, t0=0.0, dur=0.1, peak=0.2, ftype="bandpass", f0=1200.0, f1=None,
          q=1.0, a0=1.0, a1=0.001, kind="white", wob=None):
    """噪声 × 着色（white/pink/brown）× 双二阶滤波 × 指数包络。

    kind="pink" 给湿、厚的体（墨、水、风），kind="brown" 给低频轰鸣（雷、爆炸尾），
    白噪声留给瞬态 click 与高频气声。采样自无缝噪声床、随机起点顺序读。
    """
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    if i1 <= i0:
        return
    src = _bed(kind)
    ln = len(src)
    idx = int((_srand() * 0.5 + 0.5) * (ln - 1))
    seg = [0.0] * (i1 - i0)
    for j in range(len(seg)):
        seg[j] = peak * _expramp(a0, a1, j / SR, dur) * src[idx]
        idx += 1
        if idx >= ln:
            idx = 0
    if ftype:
        _bq(seg, 0, len(seg), ftype, f0, f1, q, wob, t_off=t0)
    for j in range(len(seg)):  # 相加而不是覆盖：叠层不会互相抹掉
        buf[i0 + j] += seg[j]


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


# ---------------- 音色构件（P-Audio：对齐 INKWAVE 的分层与质感手段） ----------------

def drive(buf, *, i0=0, i1=None, k=2.0):
    """tanh 软饱和：给爆炸 / 冲击体加谐波与「重量」。
    只该用在低频冲击层，不要用在 UI 与脚步上（会把细节磨平）。"""
    i1 = len(buf) if i1 is None else min(len(buf), i1)
    nt = math.tanh(k)
    if nt <= 1e-9:
        return
    for i in range(max(0, i0), i1):
        buf[i] = math.tanh(k * buf[i]) / nt


def fm_tone(buf, *, fc=880.0, fm_ratio=2.0, index=4.0, index_decay=0.18, t0=0.0,
            dur=0.6, peak=0.2, a0=1.0, a1=0.001, lp=None):
    """2 算子 FM：载波 fc + 调制器 fc*fm_ratio，调制指数指数衰减到 0。

    「衰减的调制指数」是金属 / 钟 / 玻璃感的来源：起音亮而吵、尾巴变纯。
    partials() 出的是静态金属，FM 出的是会「变」的金属。
    """
    i0 = int(t0 * SR)
    n = int(dur * SR)
    i1 = min(len(buf), i0 + n)
    if i1 <= i0:
        return
    fmf = fc * fm_ratio
    ph = 0.0
    mph = 0.0
    for i in range(i0, i1):
        t = (i - i0) / SR
        idx = index * math.exp(-t / index_decay) if index_decay > 0 else index
        mph += fmf / SR
        ph += (fc + idx * fmf * math.sin(2.0 * math.pi * mph)) / SR
        buf[i] += peak * _expramp(a0, a1, t, dur) * math.sin(2.0 * math.pi * ph)
    if lp:
        _bq(buf, i0, i1, "lowpass", lp[0], lp[1] if len(lp) > 1 else None)


def partials(buf, *, f0=880.0, ratios=(1.0, 2.0, 3.01, 4.2, 5.43),
             amps=(0.5, 0.22, 0.14, 0.09, 0.05), t0=0.0, dur=0.6, peak=0.2,
             a0=1.0, a1=0.01, wtype="sine"):
    """非谐分音叠加：金属 / 铃 / 玻璃。

    比率越不整数越「金属」——1/2.76/5.4/8.93 是铝，1/2/3.01/4.2/5.43 是铃。
    高次分音衰减更快（a1 随比率上升），所以尾巴会自然变纯。
    """
    for r, a in zip(ratios, amps):
        tone(buf, wtype=wtype, f0=f0 * r, t0=t0, dur=dur, peak=peak * a,
             a0=a0, a1=min(0.95, a1 * (r ** 0.7)))


def click(buf, *, t0=0.0, amp=0.2, f0=4500.0, dur=0.012, ftype="highpass", a1=0.01):
    """瞬态点击层：极短的滤波噪声脉冲。音效在混音里「切得出来」靠的就是它。"""
    noise(buf, t0=t0, dur=dur, peak=amp, ftype=ftype, f0=f0, q=0.9, a0=1.0, a1=a1)


def air(buf, *, t0=0.0, dur=0.12, amp=0.06, f0=5000.0, a0=0.8, a1=0.10, q=0.7):
    """高频气声层：给泼溅 / 风压 / 爆炸加一层空气感尾巴。"""
    noise(buf, t0=t0, dur=dur, peak=amp, ftype="highpass", f0=f0, q=q, a0=a0, a1=a1)


def bub(buf, *, t0=0.0, f=400.0, peak=0.18, dur=0.05, rise=1.8):
    """单颗气泡：Minnaert 式上滑阻尼正弦——「墨」的最小颗粒。"""
    tone(buf, wtype="sine", f0=f, f1=f * rise, t0=t0, dur=dur, peak=peak, a0=1.0, a1=0.01)


def bloops(buf, *, t0=0.0, count=3, spread=0.03, f_lo=380.0, f_hi=650.0,
           peak=0.12, dur=0.06, rise=0.36):
    """一串下坠墨滴（INKWAVE bloops）：随机时序 + 递减幅度，墨花的核心颗粒纹理。"""
    for k in range(count):
        f = f_lo + (f_hi - f_lo) * (_srand() * 0.5 + 0.5)
        t = t0 + (_srand() * 0.5 + 0.5) * spread + k * spread * 0.6
        tone(buf, wtype="sine", f0=f, f1=f * rise, t0=t, dur=dur,
             peak=peak * (1.0 - 0.22 * k), a0=1.0, a1=0.01)


def plips(buf, *, t0=0.0, count=4, spread=0.035, f_lo=700.0, f_hi=1800.0,
          peak=0.09, dur=0.035, rise=1.9):
    """一串上滑气泡（INKWAVE plips）：墨粒散射的高频颗粒。"""
    for k in range(count):
        f = f_lo + (f_hi - f_lo) * (_srand() * 0.5 + 0.5)
        t = t0 + (_srand() * 0.5 + 0.5) * spread + k * spread * 0.5
        bub(buf, t0=t, f=f, peak=peak * (1.0 - 0.18 * k), dur=dur, rise=rise)


def splat(buf, *, t0=0.0, scale=1.0, peak=0.30, wet_q=6.0, f_body=120.0, air_amp=0.20):
    """标准墨花（INKWAVE bigSplat 同构，5 层）：低频体 + 粉噪 LP 体 + 共振湿共振峰
    + 墨粒下坠 + 高频气声。ink 系音效的签名音模板。"""
    k = scale
    tone(buf, wtype="sine", f0=f_body * k, f1=40.0 * k, t0=t0, dur=0.28 * k,
         peak=peak * 0.62, a1=0.01)
    noise(buf, t0=t0, dur=0.26 * k, peak=peak * 0.55, ftype="lowpass",
          f0=2500.0 * k, f1=300.0 * k, q=1.5, kind="pink")
    noise(buf, t0=t0, dur=0.16 * k, peak=peak * 0.45, ftype="bandpass",
          f0=1600.0 * k, f1=320.0 * k, q=wet_q)
    bloops(buf, t0=t0 + 0.012, count=3, spread=0.03, f_lo=400.0 * k, f_hi=660.0 * k,
           peak=peak * 0.30)
    air(buf, t0=t0, dur=0.14 * k, amp=peak * air_amp, f0=3500.0, a1=0.06)


def _grain_sine(bed, start, f0, f1, dur, amp):
    """环形写入一颗滑音阻尼正弦（越界绕回开头）。"""
    L = len(bed)
    n = int(dur * SR)
    ph = 0.0
    for k in range(n):
        u = k / n
        ph += (f0 * ((f1 / f0) ** u)) / SR
        bed[(start + k) % L] += amp * _expramp(1.0, 0.01, k / SR, dur) * math.sin(2.0 * math.pi * ph)


def _grain_noise(bed, start, dur, amp, ftype, f0, q):
    """环形写入一颗滤波噪声颗粒（越界绕回开头）。"""
    L = len(bed)
    n = int(dur * SR)
    if n < 1:
        return
    seg = [0.0] * n
    for k in range(n):
        seg[k] = amp * _expramp(1.0, 0.01, k / SR, dur) * _srand()
    _bq(seg, 0, n, ftype, f0, None, q)
    for k in range(n):
        bed[(start + k) % L] += seg[k]


def grain_bed(buf, *, kind="bubbles", peak=0.2, density=1.0):
    """环形颗粒床：把成百上千颗短颗粒铺成周期恰为 LOOP_DUR 的循环床。

    颗粒位置取模 LOOP_DUR、尾部绕回开头，所以循环接缝天然无缝——这是循环音
    （游动 / 滚动 / 雨 / 滋滋）听起来「活」而不是「一段循环噪声」的关键。
    kind: bubbles | bubbles_bright | squelch | rain | sizzle
    """
    L = int(LOOP_DUR * SR)
    X = int(XFADE_DUR * SR)
    bed = [0.0] * L
    r01 = lambda: _srand() * 0.5 + 0.5

    if kind in ("bubbles", "bubbles_bright"):
        bright = kind.endswith("bright")
        f_lo, f_hi = (500.0, 2200.0) if bright else (200.0, 900.0)
        for _ in range(int(150 * density)):
            f = f_lo + (f_hi - f_lo) * r01()
            _grain_sine(bed, int(r01() * (L - 1)), f, f * (1.4 + r01()),
                        0.018 + 0.040 * r01(), peak * (0.25 + 0.75 * r01() ** 2))
    elif kind == "squelch":
        for _ in range(int(260 * density)):
            _grain_noise(bed, int(r01() * (L - 1)), 0.008 + 0.022 * r01(),
                         peak * (0.3 + 0.7 * r01()), "bandpass",
                         500.0 + 1300.0 * r01(), 2.2)
    elif kind == "rain":
        for _ in range(int(900 * density)):
            _grain_noise(bed, int(r01() * (L - 1)), 0.002 + 0.005 * r01(),
                         peak * (0.2 + 0.8 * r01() ** 2), "highpass",
                         3200.0 + 2600.0 * r01(), 0.8)
    elif kind == "sizzle":
        count = int(300 * density)
        for _ in range(count):
            _grain_noise(bed, int(r01() * (L - 1)), 0.0015 + 0.004 * r01(),
                         peak * (0.25 + 0.75 * r01() ** 2), "highpass",
                         2600.0 + 3400.0 * r01(), 0.7)
        for _ in range(max(1, count // 12)):  # 偶发的爆裂颗粒
            f = 600.0 + 900.0 * r01()
            _grain_sine(bed, int(r01() * (L - 1)), f, 180.0, 0.02, peak * 0.5)
    else:
        raise ValueError("未知颗粒床: %s" % kind)

    for i in range(L):
        buf[i] += bed[i]
    for i in range(min(X, len(buf) - L)):  # 补到 [L, L+X)：线性淡化对同相内容即恒等
        buf[L + i] += bed[i]


def room(buf, *, taps=((0.031, 0.30), (0.057, 0.24), (0.089, 0.18), (0.131, 0.12)),
         mix=0.18, lp=3200.0):
    """多抽头早期反射 + 反馈尾巴：比单抽头 space() 更像「场地」，用于爆炸 / 雷 / 号角。
    延迟与循环长度不成整比，会破坏无缝性，所以只可用于一次性音效。"""
    n = len(buf)
    wet = list(buf)
    for delay, fb in taps:
        d = int(delay * SR)
        if d <= 0 or d >= n:
            continue
        w = [0.0] * n
        for i in range(n):
            w[i] = buf[i] + (fb * w[i - d] if i >= d else 0.0)
        for i in range(n):
            wet[i] += w[i] * 0.5
    if lp:
        _bq(wet, 0, n, "lowpass", lp)
    for i in range(n):
        buf[i] += mix * wet[i]


def _am_sine(buf, rate, depth, i0=0, i1=None):
    """正弦幅度调制（缓速起伏）：给循环床去掉「一段循环噪声」的死板感。

    rate 请取 1/LOOP_DUR≈0.5556Hz 的整数倍，否则循环接缝对不齐。
    """
    i1 = len(buf) if i1 is None else min(len(buf), i1)
    for i in range(max(0, i0), i1):
        buf[i] *= 1.0 - depth * (0.5 + 0.5 * math.sin(2.0 * math.pi * rate * i / SR))


def _am_square(buf, rate, depth, i0=0, i1=None):
    """方波幅度调制：机械颗粒感（枪管抖动 / 齿轮咔哒）。
    同样要求 rate 为 1/LOOP_DUR 的整数倍（如 30Hz = 54×）。"""
    i1 = len(buf) if i1 is None else min(len(buf), i1)
    for i in range(max(0, i0), i1):
        ph = rate * i / SR
        on = (ph - math.floor(ph)) < 0.5
        buf[i] *= 1.0 if on else (1.0 - depth)


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


def _make_seamless_loop(buf, loop_len_s=LOOP_DUR, xfade_s=XFADE_DUR):
    """首尾交叉淡化，输出无爆音的循环缓冲区。

    用**线性**权重（t 与 1-t）而不是等功率：本项目所有循环层都取自周期恰为 LOOP_DUR
    的噪声床 / 环形颗粒床，即首尾段本就同相，线性淡化对同相内容是恒等变换；
    等功率权重（sin/cos）反而会在接缝处把同相内容抬高到 √2，每 1.8s 出现一次音量鼓包。
    """
    L = int(loop_len_s * SR)
    X = int(xfade_s * SR)
    if len(buf) < L + X:
        buf = buf + [0.0] * (L + X - len(buf))
    out = [0.0] * L
    for i in range(X):
        t = i / float(X)
        out[i] = buf[i] * t + buf[L + i] * (1.0 - t)
    for i in range(X, L):
        out[i] = buf[i]
    return out


def _normalize(buf, target):
    p = _peak(buf)
    if p <= 1e-9:
        return 0.0
    g = target / p
    for i in range(len(buf)):
        buf[i] *= g
    return g


def _soft_limit(buf, k=1.3):
    """轻微 tanh 软限幅：把瞬态尖峰压圆，让同一峰值下的「体」浮出来。

    纯峰值归一化有个陷阱——一记很短的瞬态 click 会独占峰值，把整只音效其余各层
    一起压小，听感就是「薄」。先软限幅再归一化（相当于录音里的总线胶合压缩），
    RMS 明显上升、层与层贴得更紧。k=1.3 谐波失真很小，UI 音也不会被染色。
    """
    nt = math.tanh(k)
    if nt <= 1e-9:
        return
    for i in range(len(buf)):
        buf[i] = math.tanh(k * buf[i]) / nt


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
    """主武器射击：签名音，按 INKWAVE shoot_shooter 的 7 层结构重做。

    瞬态 snap → 音调 chirp → 气动 pshk → 低频 thump → 共振湿 splort → 墨滴 → 气声。
    旧的 4 层版里最后一层 noise() 会把前面三层在同段内整段抹掉（_bq 是破坏性滤波），
    实际只听到「一声带通噪声 + 尾巴」——这是"简陋"的主因，现在层与层真正叠加。
    """
    k = _vpitch(v, n, 0.07)
    j = 1.0 + 0.06 * (_srand() * 0.5 + 0.5)              # 每发微抖动：连发不似复读
    click(buf, amp=0.22, f0=4200 * k * j, dur=0.018)
    tone(buf, wtype="sine", f0=2600 * k * j, f1=820 * k, dur=0.020, peak=0.20, a1=0.02)
    noise(buf, dur=0.050, peak=0.18, ftype="bandpass", f0=2500 * k, f1=1550 * k, q=1.4, kind="pink")
    tone(buf, wtype="sine", f0=175 * k, f1=62 * k, dur=0.080, peak=0.22, a1=0.01)
    noise(buf, dur=0.090, peak=0.16, ftype="bandpass", f0=1500 * k, f1=560 * k, q=5.5)
    bub(buf, t0=0.018, f=900 * k * j, peak=0.09, dur=0.045, rise=1.8)
    air(buf, t0=0.004, dur=0.055, amp=0.06, f0=5200)


def _f_ink_hit_ground(buf, v, n):
    """弹丸落地溅射：标准墨花（低频体 + 粉噪体 + 共振湿 + 墨粒 + 气声），
    再补高频颗粒。墨斑越大（AudioManager 换算音高）读作越沉越厚。"""
    k = _vpitch(v, n, 0.08)
    splat(buf, scale=0.85 * k, peak=0.30, wet_q=5.5, f_body=170.0)
    noise(buf, dur=0.030, peak=0.10, ftype="highpass", f0=4200, a1=0.02)
    plips(buf, t0=0.02, count=2, spread=0.03, f_lo=800 * k, f_hi=1700 * k, peak=0.06)


def _f_splat_small(buf, v, n):
    """小墨斑（单发子弹落地）：比 ink_hit_ground 更短更干，用于高频涂地。"""
    k = _vpitch(v, n, 0.10)
    tone(buf, wtype="sine", f0=170 * k, f1=65 * k, dur=0.14, peak=0.24, a1=0.01)
    noise(buf, dur=0.10, peak=0.20, ftype="bandpass", f0=2400 * k, f1=900 * k, q=2.5)
    bloops(buf, t0=0.01, count=2, spread=0.025, f_lo=450 * k, f_hi=750 * k, peak=0.11)
    plips(buf, count=2, spread=0.03, f_lo=900 * k, f_hi=1900 * k, peak=0.07)


def _f_splat_big(buf, v, n):
    """大墨斑（空爆 / 大招落墨）：低频更沉、湿层更宽、气声更长。"""
    k = _vpitch(v, n, 0.06)
    splat(buf, scale=1.25 * k, peak=0.32, wet_q=4.5, f_body=110.0, air_amp=0.26)
    noise(buf, dur=0.34, peak=0.14, ftype="lowpass", f0=900 * k, f1=220 * k, q=1.2, kind="pink")
    plips(buf, t0=0.03, count=4, spread=0.05, f_lo=700 * k, f_hi=1600 * k, peak=0.07)


def _f_ink_hit_wall(buf, v, n):
    """墨水打墙：没有地面体积，只有共振湿润的「啪」+ 一颗墨粒 + 瞬态（INKWAVE ink_hit_wall）。"""
    k = _vpitch(v, n, 0.09)
    noise(buf, dur=0.10, peak=0.24, ftype="bandpass", f0=1300 * k, f1=490 * k, q=4.0)
    tone(buf, wtype="sine", f0=920 * k, f1=405 * k, dur=0.08, peak=0.16, a1=0.01)
    bub(buf, t0=0.006, f=760 * k, peak=0.10, dur=0.05, rise=0.44)
    click(buf, amp=0.14, f0=4000, dur=0.010)


def _f_hit_enemy(buf, v, n):
    """命中敌人：比落地更「硬」的撞击——高频共振 + 短促低频拍击，尾巴干净。"""
    k = _vpitch(v, n, 0.09)
    noise(buf, dur=0.028, peak=0.20, ftype="highpass", f0=2400, a1=0.02)
    noise(buf, dur=0.07, peak=0.18, ftype="bandpass", f0=1300 * k, f1=520 * k, q=3.0)
    tone(buf, wtype="triangle", f0=1250 * k, f1=680 * k, dur=0.05, peak=0.22, a1=0.01)
    tone(buf, wtype="sine", f0=190 * k, f1=70 * k, dur=0.09, peak=0.16, a1=0.01)


def _f_kill(buf, v, n):
    """击杀确认：两音上跳 + E 大调闪音 + 噪声爆点（INKWAVE splat_enemy 的 sparkle 语义）。

    闪音用非谐比率 2.76 的分音堆出「金属高光」，比纯正弦琶音更有确认感。
    """
    k = _vpitch(v, n, 0.05)
    arp(buf, [620 * k, 940 * k], t_step=0.05, dur=0.16, peak=0.18, wtype="triangle")
    noise(buf, dur=0.10, peak=0.15, ftype="bandpass", f0=3000 * k, f1=800 * k, q=0.9)
    for i, f in enumerate((784, 988, 1175)):
        partials(buf, f0=f * k, ratios=(1.0, 2.76), amps=(0.5, 0.22), t0=0.02 + i * 0.045,
                 dur=0.24, peak=0.055, a1=0.006)
    air(buf, dur=0.14, amp=0.05, f0=6500)


def _f_death(buf, v, n):
    """被击杀：下行厚重音 + 饱和质感 + 粉噪尾（读到「自己没了」）。"""
    k = _vpitch(v, n, 0.05)
    sat_tone(buf, f0=430 * k, f1=125 * k, dur=0.38, peak=0.28, drive=2.4, lp=(1800, 500))
    noise(buf, dur=0.26, peak=0.15, ftype="bandpass", f0=900 * k, f1=240 * k, q=0.7, kind="pink")
    tone(buf, wtype="sine", f0=110 * k, f1=48 * k, dur=0.42, peak=0.14, a1=0.004)
    room(buf, mix=0.10, lp=2600)


def _f_spawn_protect(buf, v, n):
    """出生保护生效：柔和上行的护盾「贴膜」音——亮但不刺激，别像受伤。

    用 FM 钟（衰减调制指数）+ 一层气声，读作「保护到位」而不是「被打」。
    """
    fm_tone(buf, fc=520, fm_ratio=2.0, index=2.2, index_decay=0.10, dur=0.45, peak=0.16, a1=0.02)
    tone(buf, wtype="sine", f0=390, f1=780, dur=0.40, peak=0.12, a0=0.4, a1=0.03)
    air(buf, dur=0.42, amp=0.055, f0=4200, a0=0.4, a1=0.20)
    room(buf, taps=((0.041, 0.26), (0.081, 0.16)), mix=0.12, lp=3400)


def _f_low_health(buf, v, n):
    """低血心跳：两记低频「咚-咚」——闷、无音调感、比受伤更沉，只提示「快没血了」。

    不做变调（心跳必须每次都一样才能被识别为「危险」）。
    """
    tone(buf, wtype="sine", f0=92, f1=44, dur=0.14, peak=0.30, a1=0.01)
    noise(buf, dur=0.06, peak=0.10, ftype="lowpass", f0=420, q=1.0)
    tone(buf, wtype="sine", f0=84, f1=40, t0=0.20, dur=0.18, peak=0.24, a1=0.01)
    noise(buf, t0=0.20, dur=0.07, peak=0.08, ftype="lowpass", f0=380, q=1.0)


def _f_respawn_tick(buf, v, n):
    """复活倒计时滴答：比 countdown_tick 更窄更暗（是「等待」而不是「开赛」）。"""
    tone(buf, wtype="sine", f0=760, f1=680, dur=0.045, peak=0.22, a1=0.02)
    noise(buf, dur=0.010, peak=0.06, ftype="highpass", f0=3800)


def _f_ink_empty(buf, v, n):
    """墨量不足：干打咔哒 + 一点空腔「噗」，无音调感（必须明显区别于成功射击）。"""
    k = _vpitch(v, n, 0.06)
    click(buf, amp=0.26, f0=3000, dur=0.022)
    tone(buf, wtype="square", f0=900 * k, dur=0.014, peak=0.10, a1=0.05, lp=(2400,))
    noise(buf, t0=0.020, dur=0.045, peak=0.09, ftype="bandpass", f0=1100 * k, f1=500 * k, q=1.6, kind="pink")
    bub(buf, t0=0.05, f=520 * k, peak=0.06, dur=0.04, rise=0.6)


def _f_ink_refill(buf, v, n):
    """潜墨回墨：气泡咕嘟上升 + 一层水下闷响（INKWAVE 的 bubbles 颗粒语义）。"""
    k = _vpitch(v, n, 0.07)
    noise(buf, dur=0.22, peak=0.08, ftype="bandpass", f0=520 * k, q=0.7, kind="pink", a1=0.06)
    plips(buf, count=5, spread=0.038, f_lo=260 * k, f_hi=820 * k, peak=0.10, dur=0.05)
    bub(buf, t0=0.14, f=300 * k, peak=0.08, dur=0.07, rise=1.5)


def _f_respawn(buf, v, n):
    """复活：上行闪烁 + FM 钟 + 气声（INKWAVE respawn：上升 + 气泡 + 铃）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=300 * k, f1=1300 * k, dur=0.30, peak=0.15, a1=0.10)
    noise(buf, dur=0.30, peak=0.06, ftype="highpass", f0=4000, a1=0.25)
    for i, f in enumerate((784, 1046, 1318)):
        fm_tone(buf, fc=f * k, fm_ratio=2.0, index=1.6, index_decay=0.09,
                t0=0.05 + i * 0.055, dur=0.30, peak=0.075, a1=0.006)
    plips(buf, t0=0.02, count=4, spread=0.04, f_lo=560 * k, f_hi=1500 * k, peak=0.06)


def _f_countdown_tick(buf, v, n):
    """倒计时滴答：干净、短、可辨认（正弦 + 方波经低通，INKWAVE countdown_tick 同构）。"""
    tone(buf, wtype="sine", f0=880, dur=0.05, peak=0.24, a1=0.02)
    tone(buf, wtype="square", f0=880, dur=0.045, peak=0.07, a1=0.03, lp=(3000,))
    click(buf, amp=0.09, f0=5000, dur=0.008)


def _f_final_10s(buf, v, n):
    """终局最后 10 秒：更紧迫的双音（正弦 + 12 音分失谐的锯齿做拍频），明显区别于滴答。"""
    tone(buf, wtype="sine", f0=1320, dur=0.09, peak=0.20, a1=0.02)
    tone(buf, wtype="square", f0=660, dur=0.08, peak=0.09, a1=0.03, lp=(4500,))
    tone(buf, wtype="sawtooth", f0=1326, dur=0.09, peak=0.07, a1=0.02, lp=(5000,))
    click(buf, amp=0.10, f0=5200, dur=0.008)


def _f_match_start(buf, v, n):
    """对局开场前的蓄势：墨幕吸气般的上扬扫频 + 加速脉冲，尾部留白交给 GO 号角。

    不写旋律：0.86s 的墨幕入场里连续三音上行会和 GO 抢拍，这里只做「要开始了」的张力。
    """
    tone(buf, wtype="sine", f0=55, f1=110, dur=0.50, peak=0.13, a0=0.10, a1=0.35)
    noise(buf, dur=0.38, peak=0.14, ftype="bandpass", f0=300, f1=2200, q=0.7,
          kind="pink", a0=0.02, a1=0.95)
    noise(buf, t0=0.36, dur=0.16, peak=0.12, ftype="bandpass", f0=2400, q=0.8, a0=0.9, a1=0.02)
    noise(buf, t0=0.12, dur=0.34, peak=0.05, ftype="highpass", f0=4200, a0=0.05, a1=0.40)
    for i, t in enumerate((0.04, 0.17, 0.29, 0.39)):
        tone(buf, wtype="sine", f0=880 + i * 140, f1=960 + i * 140, t0=t,
             dur=0.030, peak=0.075, a0=0.9, a1=0.04)
    tone(buf, wtype="sine", f0=140, f1=72, t0=0.47, dur=0.10, peak=0.13, a0=1.0, a1=0.01)
    room(buf, taps=((0.033, 0.26), (0.071, 0.16)), mix=0.10, lp=3000)


def _f_ready(buf, v, n):
    """READY 提示：1.25s 的上升建构，比 match_start 更克制（INKWAVE ready 同构）。

    四声锯齿和弦的 detune 从很宽收到 ±9 音分，制造「音高往上爬」的错觉；
    箝位总长留在 1.2s 以内，不侵入 countdown 的滴答拍点。
    """
    noise(buf, dur=1.10, peak=0.11, ftype="bandpass", f0=300, f1=4000, q=0.7,
          kind="pink", a0=0.05, a1=0.60)
    for det in (-0.09, -0.045, 0.0, 0.045):
        tone(buf, wtype="sawtooth", f0=131 * (1.0 + det), dur=1.05, peak=0.055,
             a0=0.10, a1=0.30, lp=(400, 5000))
    tone(buf, wtype="sine", f0=600, f1=1400, t0=1.02, dur=0.14, peak=0.14, a1=0.04)


def _f_go_horn(buf, v, n):
    """GO 冲击号角：低频下潜 + 饱和号角和弦 + 墨点爆音。

    这是「GO」的人声替代：必须有起跑的重量，所以先给一记低频落点，
    再用 220/330/440Hz 叠出号角和弦；尾巴收干净，不拖到开火节奏里。
    """
    tone(buf, wtype="sine", f0=150, f1=52, dur=0.28, peak=0.16, a0=1.0, a1=0.01)
    sat_tone(buf, f0=220, f1=214, dur=0.38, peak=0.24, drive=2.6, a0=0.85, a1=0.10, lp=(3200,))
    tone(buf, wtype="sawtooth", f0=330, f1=322, dur=0.36, peak=0.14, a0=0.75, a1=0.06, lp=(3600,))
    tone(buf, wtype="sawtooth", f0=440, f1=430, dur=0.30, peak=0.08, a0=0.65, a1=0.05, lp=(4200,))
    tone(buf, wtype="sawtooth", f0=165, f1=160, dur=0.34, peak=0.08, a0=0.7, a1=0.08, lp=(2400,))
    noise(buf, dur=0.09, peak=0.17, ftype="bandpass", f0=2600, f1=850, q=0.9, a0=1.0, a1=0.02)
    air(buf, t0=0.0, dur=0.030, amp=0.08, f0=5600, a1=0.05)
    room(buf, taps=((0.037, 0.28), (0.079, 0.18)), mix=0.11, lp=3000)


def _f_one_minute(buf, v, n):
    """剩余一分钟：四音脉冲琶音 + 八度铃（INKWAVE one_minute 语义），压得住但别抢镜。"""
    arp(buf, [392, 440, 494, 587], t_step=0.11, dur=0.24, peak=0.13, wtype="triangle")
    for i, f in enumerate((784, 988)):
        fm_tone(buf, fc=f, fm_ratio=2.0, index=1.4, index_decay=0.10,
                t0=i * 0.11, dur=0.28, peak=0.05, a1=0.008)
    air(buf, t0=0.0, dur=0.34, amp=0.045, f0=4200)


def _f_match_end(buf, v, n):
    """终场定格：定格闷响 + 裁判双音哨 + 三音下行钟 + 多抽头场地反射。

    哨音用两只轻微失谐的高音叠出拍频颤音（合成器没有 vibrato，靠 detune 做「裁判哨」）。
    """
    tone(buf, wtype="sine", f0=96, f1=42, dur=0.45, peak=0.20, a0=1.0, a1=0.01)
    noise(buf, dur=0.09, peak=0.13, ftype="lowpass", f0=900, a0=1.0, a1=0.02)
    tone(buf, wtype="sine", f0=2320, dur=0.30, peak=0.11, a0=0.9, a1=0.25)
    tone(buf, wtype="sine", f0=2378, dur=0.30, peak=0.09, a0=0.9, a1=0.25)
    noise(buf, t0=0.02, dur=0.30, peak=0.035, ftype="highpass", f0=4200, a0=0.8, a1=0.15)
    tone(buf, wtype="sine", f0=2262, t0=0.40, dur=0.16, peak=0.09, a0=0.8, a1=0.06)
    tone(buf, wtype="sine", f0=2318, t0=0.40, dur=0.16, peak=0.07, a0=0.8, a1=0.06)
    noise(buf, t0=0.40, dur=0.16, peak=0.03, ftype="highpass", f0=4200, a0=0.7, a1=0.10)
    arp(buf, [523, 392, 262], t_step=0.14, dur=0.50, peak=0.13, wtype="triangle")
    tone(buf, wtype="sine", f0=110, t0=0.10, dur=0.85, peak=0.07, a0=0.6, a1=0.01)
    room(buf, mix=0.15, lp=2800)


def _f_times_up(buf, v, n):
    """时间到：裁判哨（长颤音）+ 铜管下行 + 低音铺底（INKWAVE times_up 同构）。"""
    tone(buf, wtype="sine", f0=2600, dur=0.70, peak=0.15, a0=0.9, a1=0.06,
         vib=(28.0, 0.06))
    noise(buf, dur=0.70, peak=0.05, ftype="bandpass", f0=2600, q=3.0, a0=0.8, a1=0.08)
    arp(buf, [233, 247, 262, 294], t_step=0.10, dur=0.60, peak=0.10, wtype="sawtooth")
    arp(buf, [523, 440, 349], t_step=0.15, dur=0.45, peak=0.11, wtype="triangle")
    tone(buf, wtype="sine", f0=131, dur=0.90, peak=0.10, a0=0.4, a1=0.02)
    room(buf, mix=0.16, lp=3000)


def _f_lead_gain(buf, v, n):
    """比分反超（我方领先）：三音上行 + 亮闪音——短、克制，不打断对局节奏。"""
    arp(buf, [659, 784, 1046], t_step=0.055, dur=0.22, peak=0.14, wtype="triangle")
    partials(buf, f0=1046, ratios=(1.0, 2.76), amps=(0.4, 0.18), t0=0.10, dur=0.30,
             peak=0.05, a1=0.008)
    air(buf, t0=0.0, dur=0.20, amp=0.04, f0=6000)


def _f_lead_lose(buf, v, n):
    """比分被反超（我方落后）：两音下行 + 低通闷音——提醒但不说教。"""
    arp(buf, [494, 370], t_step=0.10, dur=0.34, peak=0.15, wtype="triangle")
    tone(buf, wtype="sine", f0=185, f1=140, dur=0.40, peak=0.10, a1=0.03)
    _bq(buf, 0, len(buf), "lowpass", 2200)


def _f_enemy_special_warn(buf, v, n):
    """敌方大招来袭警告：三声急促下行低方波（危险但不同于我方 ult_ready 的上行亮音）。"""
    for i, f in enumerate((330, 294, 262)):
        tone(buf, wtype="square", f0=f, t0=i * 0.11, dur=0.09, peak=0.15, a1=0.05, lp=(1600,))
        tone(buf, wtype="sine", f0=f * 2, t0=i * 0.11, dur=0.07, peak=0.05, a1=0.06)
    noise(buf, dur=0.02, peak=0.05, ftype="lowpass", f0=900)


def _f_result_win(buf, v, n):
    """结算胜利：四音上行小句 + 每音补非谐高光（铃感），比纯三角波琶音更「庆祝」。"""
    arp(buf, [523, 659, 784, 1046], t_step=0.13, dur=0.50, peak=0.15, wtype="triangle")
    for i, f in enumerate((523, 659, 784, 1046)):
        partials(buf, f0=f * 2, ratios=(1.0, 2.76), amps=(0.30, 0.13),
                 t0=i * 0.13, dur=0.40, peak=0.045, a1=0.01)
    tone(buf, wtype="sine", f0=131, dur=0.90, peak=0.08, a0=0.4, a1=0.02)
    room(buf, mix=0.14, lp=3600)


def _f_result_lose(buf, v, n):
    """结算失败：三音下行 + 低音铺底 + 整体低通（压低沉闷）。"""
    arp(buf, [440, 349, 262], t_step=0.15, dur=0.55, peak=0.20, wtype="sine")
    tone(buf, wtype="triangle", f0=147, dur=0.80, peak=0.09, a0=0.4, a1=0.03)
    room(buf, taps=((0.045, 0.22), (0.093, 0.13)), mix=0.10, lp=2200)
    _bq(buf, 0, len(buf), "lowpass", 2200)


# ---------------- 武器操作（7 类主武器） ----------------

def _f_roller_flick(buf, v, n):
    """滚筒挥击甩墨：三段手绘扫频的「唰」+ 甩出瞬间的湿脆点 + 低频挥动体 + 四片重叠墨帘。

    风压用**分阶段**带通（320→1700→600Hz）而不是单段扫频——单段听起来像「嘶」不像「唰」。
    """
    k = _vpitch(v, n, 0.07)
    noise(buf, dur=0.15, peak=0.22, ftype="bandpass", f0=320 * k, f1=1700 * k, q=1.8, kind="pink", a1=0.30)
    noise(buf, t0=0.15, dur=0.17, peak=0.16, ftype="bandpass", f0=1700 * k, f1=600 * k, q=1.8, kind="pink", a1=0.06)
    tone(buf, wtype="sine", f0=120 * k, f1=45 * k, t0=0.12, dur=0.20, peak=0.18, a1=0.01)
    noise(buf, dur=0.22, peak=0.10, ftype="lowpass", f0=1100 * k, f1=220 * k, q=1.2, kind="pink")
    for i in range(4):  # 四片墨帘，彼此重叠 18ms，越后越轻
        noise(buf, t0=0.10 + i * 0.018, dur=0.10, peak=0.14 - i * 0.025,
              ftype="bandpass", f0=(1300 - i * 80) * k, f1=(420 - i * 30) * k, q=3.0)
    bloops(buf, t0=0.14, count=4, spread=0.035, f_lo=420 * k, f_hi=760 * k, peak=0.09)
    air(buf, t0=0.02, dur=0.12, amp=0.06, f0=3500)


def _f_charger_charge(buf, v, n):
    """蓄力升频（循环）：三角波升频 + 正弦体 + 减半粉噪床（2026-11-08 减刺耳改版）。

    旧版（锯齿 + 7kHz 气声层 + q=5 共振带通 + ±380Hz 截止抖动）被玩家反馈「太吵」：
    高频谐波 + 嘶声在耳边持续循环极易疲劳。新版锯齿改三角波（谐波衰减快、科技感保留）、
    整层删掉气声、噪声床减半去共振，升频扫频保留（「正在蓄力」的紧张感不丢）。
    循环渲染，所以用恒定包络（a0=a1=1）+ 周期为 1.8s 的噪声床；LFO 速率取 1/1.8s 的整数倍。
    """
    tone(buf, wtype="triangle", f0=250, dur=2.15, peak=0.13, a0=1.0, a1=1.0,
         lp=(1200, 3400), wob=(5.0, 120.0))
    tone(buf, wtype="sine", f0=500, dur=2.15, peak=0.09, a0=1.0, a1=1.0)
    tone(buf, wtype="sine", f0=752.5, dur=2.15, peak=0.04, a0=1.0, a1=1.0)
    noise(buf, dur=2.15, peak=0.05, ftype="bandpass", f0=800, q=2.5, kind="pink", a0=1.0, a1=1.0,
          wob=(5.0, 100.0))


def _f_charger_full(buf, v, n):
    """蓄满清脆叮音：非谐铃分音堆（1/2/3.01/4.2/5.43）+ 高频闪光。

    这组比率是 INKWAVE charger_full 的钟声配方：整数倍叠不出「叮」，不谐才像金属。
    """
    k = _vpitch(v, n, 0.03)
    partials(buf, f0=1568 * k, ratios=(1.0, 2.0, 3.01, 4.2, 5.43),
             amps=(0.50, 0.22, 0.14, 0.09, 0.05), dur=0.45, peak=0.22, a1=0.006)
    partials(buf, f0=2349 * k, ratios=(1.0, 2.76, 5.4), amps=(0.24, 0.11, 0.05),
             dur=0.34, peak=0.14, a1=0.008)
    air(buf, dur=0.10, amp=0.05, f0=8000)
    room(buf, taps=((0.027, 0.24), (0.061, 0.14)), mix=0.13, lp=6000)


def _f_charger_shot(buf, v, n):
    """蓄力狙发射：高频爆裂 + 快速下坠的「咻」+ 长墨线尾 + 墨粒。

    满蓄时由 AudioManager 降音高（更沉）；这里守住「爆裂 + 墨线」的骨架。
    """
    k = _vpitch(v, n, 0.04)
    click(buf, amp=0.26, f0=2600, dur=0.010)
    tone(buf, wtype="sine", f0=5200 * k, dur=0.006, peak=0.14, a1=0.05)
    noise(buf, dur=0.16, peak=0.22, ftype="bandpass", f0=6000 * k, f1=900 * k, q=3.0)
    tone(buf, wtype="sawtooth", f0=2600 * k, f1=260 * k, dur=0.14, peak=0.16, a1=0.006, lp=(6000,))
    tone(buf, wtype="sine", f0=140 * k, f1=46 * k, dur=0.22, peak=0.20, a1=0.004)
    noise(buf, t0=0.02, dur=0.55, peak=0.09, ftype="bandpass", f0=1700 * k, f1=420 * k, q=2.5)
    bloops(buf, t0=0.06, count=2, spread=0.05, f_lo=350 * k, f_hi=620 * k, peak=0.07)
    air(buf, t0=0.0, dur=0.10, amp=0.06, f0=7000)
    room(buf, mix=0.12, lp=4200)


def _f_blaster_shot(buf, v, n):
    """爆破枪发射：低沉「噗通」——比小枪闷、低频体长 3 倍、还有一颗上滑的墨球（INKWAVE shoot_blaster）。"""
    k = _vpitch(v, n, 0.05)
    click(buf, amp=0.18, f0=3800, dur=0.008)
    noise(buf, dur=0.08, peak=0.16, ftype="bandpass", f0=1500 * k, f1=900 * k, q=2.2, kind="pink")
    tone(buf, wtype="sine", f0=150 * k, f1=40 * k, dur=0.30, peak=0.26, a1=0.006)
    tone(buf, wtype="triangle", f0=300 * k, f1=88 * k, dur=0.14, peak=0.14, a1=0.01)
    noise(buf, dur=0.14, peak=0.14, ftype="lowpass", f0=1600 * k, f1=280 * k, q=2.0, kind="pink")
    noise(buf, t0=0.01, dur=0.13, peak=0.10, ftype="bandpass", f0=320 * k, f1=950 * k, q=6.0)  # 上滑墨球
    tone(buf, wtype="sine", f0=190 * k, f1=430 * k, dur=0.13, peak=0.07, a0=0.5, a1=0.02)
    air(buf, dur=0.10, amp=0.05, f0=5000)


def _f_blaster_pump(buf, v, n):
    """爆破枪换气泵（连续射击的循环机构音）：两次机械卡扣 + 泄压气流（INKWAVE blaster_pump）。

    两记 clack 相隔 ~170ms，与泵动作对齐；旧的 P1 完全没有这个音。
    """
    k = _vpitch(v, n, 0.05)
    for t, amp in ((0.0, 1.0), (0.19, 0.85)):
        click(buf, t0=t, amp=0.18 * amp, f0=3100 * k, dur=0.009)
        tone(buf, wtype="sine", f0=1870 * k, t0=t, dur=0.05, peak=0.05 * amp, a1=0.05)
        tone(buf, wtype="sine", f0=3050 * k, t0=t, dur=0.04, peak=0.03 * amp, a1=0.06)
        tone(buf, wtype="sine", f0=230 * k, f1=120 * k, t0=t, dur=0.07, peak=0.12 * amp, a1=0.03)
    noise(buf, dur=0.22, peak=0.09, ftype="bandpass", f0=1200 * k, f1=2200 * k, q=0.9, kind="pink")


def _f_blaster_boom(buf, v, n):
    """爆破枪空爆重音：低频冲击 + 粉噪体经饱和 + 墨粒雨 + 长混响（INKWAVE blaster_boom）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=110 * k, f1=34 * k, dur=0.45, peak=0.26, a1=0.004)
    click(buf, amp=0.18, f0=2500, dur=0.010)
    tone(buf, wtype="sine", f0=950 * k, f1=210 * k, dur=0.16, peak=0.18, a1=0.006)
    noise(buf, dur=0.40, peak=0.24, ftype="lowpass", f0=3500 * k, f1=250 * k, q=1.0, kind="pink")
    noise(buf, dur=0.22, peak=0.13, ftype="bandpass", f0=1400 * k, f1=450 * k, q=3.5)
    bloops(buf, t0=0.02, count=3, spread=0.04, f_lo=300 * k, f_hi=580 * k, peak=0.09)
    plips(buf, t0=0.05, count=9, spread=0.10, f_lo=600 * k, f_hi=1500 * k, peak=0.035)
    air(buf, dur=0.30, amp=0.06, f0=3600, a1=0.10)
    room(buf, mix=0.18, lp=2600)


def _f_slosh_throw(buf, v, n):
    """泼桶起手：墨在水桶里上抛的湿扫 + 桶壁晃动的上滑音 + 墨粒（INKWAVE slosh_throw）。"""
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.24, peak=0.20, ftype="bandpass", f0=260 * k, f1=1400 * k, q=1.6, kind="pink")
    noise(buf, t0=0.14, dur=0.14, peak=0.12, ftype="bandpass", f0=1400 * k, f1=500 * k, q=1.6, kind="pink")
    tone(buf, wtype="sine", f0=180 * k, f1=420 * k, dur=0.18, peak=0.14, a0=0.4, a1=0.02)
    noise(buf, dur=0.18, peak=0.09, ftype="bandpass", f0=380 * k, f1=1100 * k, q=6.0)
    bloops(buf, t0=0.04, count=3, spread=0.05, f_lo=300 * k, f_hi=560 * k, peak=0.10)


def _f_slosh_land(buf, v, n):
    """泼桶落墨：整桶重泼——大墨花 + 高频啪 + 六颗逐渐稀疏的墨球（INKWAVE slosh_land）。"""
    k = _vpitch(v, n, 0.07)
    splat(buf, scale=1.15 * k, peak=0.32, wet_q=5.0, f_body=110.0, air_amp=0.24)
    noise(buf, dur=0.05, peak=0.14, ftype="highpass", f0=2800, a1=0.03)
    for i in range(6):
        plips(buf, t0=0.04 + i * 0.035, count=1, spread=0.02,
              f_lo=600 * k, f_hi=1500 * k, peak=0.055)


def _f_splatling_ready(buf, v, n):
    """加特林蓄力到位：机械「咚」+ 金属卡扣 + 两音上跳的预备钟（INKWAVE splatling_ready）。"""
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="sine", f0=170 * k, f1=90 * k, dur=0.14, peak=0.22, a1=0.02)
    click(buf, amp=0.16, f0=2600 * k, dur=0.010)
    partials(buf, f0=1568 * k, ratios=(1.0, 2.76), amps=(0.30, 0.13), t0=0.05, dur=0.30,
             peak=0.10, a1=0.008)
    partials(buf, f0=2093 * k, ratios=(1.0, 2.76), amps=(0.26, 0.11), t0=0.12, dur=0.30,
             peak=0.09, a1=0.008)


# ---------------- 移动与潜墨（P2-A 双形态机动） ----------------

def _f_squid_in(buf, v, n):
    """潜墨入水：下沉「噗通」+ 高强度共振吸水（Q6）+ 下沉墨粒（INKWAVE squid_in）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=950 * k, f1=240 * k, dur=0.09, peak=0.22, a1=0.008)
    noise(buf, dur=0.11, peak=0.18, ftype="bandpass", f0=900 * k, f1=380 * k, q=6.0)
    noise(buf, dur=0.10, peak=0.10, ftype="lowpass", f0=1600 * k, f1=380 * k, q=0.8, kind="pink")
    bloops(buf, t0=0.03, count=2, spread=0.035, f_lo=260 * k, f_hi=520 * k, peak=0.09)


def _f_squid_out(buf, v, n):
    """浮出墨面：上升「啵」+ 破水高频 + 共振上滑（入水音的镜像，INKWAVE squid_out）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=260 * k, f1=1050 * k, dur=0.11, peak=0.22, a0=0.5, a1=0.008)
    click(buf, amp=0.12, f0=3000, dur=0.010)
    noise(buf, dur=0.10, peak=0.15, ftype="bandpass", f0=600 * k, f1=1400 * k, q=4.0)
    air(buf, dur=0.07, amp=0.06, f0=5000)


def _f_swim_splash(buf, v, n):
    """高速潜行水花：极短湿噪声 + 墨粒，可高频重复触发不糊。"""
    k = _vpitch(v, n, 0.08)
    tone(buf, wtype="sine", f0=500 * k, f1=200 * k, dur=0.06, peak=0.14, a1=0.01)
    noise(buf, dur=0.07, peak=0.18, ftype="bandpass", f0=1600 * k, f1=700 * k, q=2.0)
    plips(buf, count=3, spread=0.028, f_lo=700 * k, f_hi=1600 * k, peak=0.07, dur=0.03)


def _f_step_ink_bubble(buf, v, n):
    """踩己方墨水：湿润「咕叽」（共振带通 Q3.5）+ 墨粒（INKWAVE step_ink）。"""
    k = _vpitch(v, n, 0.10)
    noise(buf, dur=0.07, peak=0.18, ftype="bandpass", f0=1300 * k, f1=520 * k, q=3.5, kind="pink")
    tone(buf, wtype="sine", f0=190 * k, f1=110 * k, dur=0.06, peak=0.13, a1=0.02)
    plips(buf, count=2, spread=0.02, f_lo=900 * k, f_hi=1900 * k, peak=0.06, dur=0.028)


def _f_step_dry(buf, v, n):
    """踩干地：干净短促的鞋底轻叩 + 一点砂砾（音高抖动大 = 走路的随机感）。"""
    k = _vpitch(v, n, 0.10)
    tone(buf, wtype="sine", f0=120 * k, f1=70 * k, dur=0.045, peak=0.14, a1=0.02)
    noise(buf, dur=0.04, peak=0.14, ftype="bandpass", f0=2600 * k, q=0.9)
    click(buf, amp=0.07, f0=5200, dur=0.012)


def _f_step_enemy(buf, v, n):
    """踩敌方墨水：粘稠「咕叽」——比己方墨水更低更慢（Q5）、尾巴带一点黏滞的滋声。"""
    k = _vpitch(v, n, 0.09)
    noise(buf, dur=0.10, peak=0.19, ftype="bandpass", f0=700 * k, f1=330 * k, q=5.0, kind="pink")
    tone(buf, wtype="sine", f0=140 * k, f1=85 * k, dur=0.08, peak=0.13, a1=0.02)
    noise(buf, t0=0.05, dur=0.06, peak=0.07, ftype="highpass", f0=3500, a1=0.06)


def _f_ink_drip(buf, v, n):
    """墨滴滴落：单颗高频下坠水滴（跑动时连续滴墨的伴音，INKWAVE ink_drip 就是一颗气泡）。"""
    k = _vpitch(v, n, 0.12)
    bub(buf, f=1400 * k, peak=0.18, dur=0.05, rise=0.32)
    noise(buf, dur=0.008, peak=0.045, ftype="highpass", f0=5200)


def _f_jump(buf, v, n):
    """起跳：短促上扬的气流（三角 + 正弦双扫 + 带通风压）。"""
    k = _vpitch(v, n, 0.07)
    tone(buf, wtype="triangle", f0=280 * k, f1=620 * k, dur=0.12, peak=0.15, a1=0.02)
    tone(buf, wtype="sine", f0=280 * k, f1=620 * k, dur=0.12, peak=0.09, a1=0.02)
    noise(buf, dur=0.10, peak=0.11, ftype="bandpass", f0=800 * k, f1=1800 * k, q=0.9)


def _f_land(buf, v, n):
    """落地：低频闷响 + 带通拍击 + 高频瞬态（INKWAVE land）。"""
    k = _vpitch(v, n, 0.07)
    tone(buf, wtype="sine", f0=140 * k, f1=55 * k, dur=0.14, peak=0.22, a1=0.008)
    noise(buf, dur=0.08, peak=0.13, ftype="bandpass", f0=750 * k, f1=420 * k, q=3.0)
    click(buf, amp=0.08, f0=3000, dur=0.010)


def _f_dodge_roll(buf, v, n):
    """双枪战术翻滚：下扫「唰」+ 橡胶摩擦 + 粉噪拖尾 + 落定的低频「哒」（INKWAVE dualies_roll）。

    0.4s 内收干净，不抢紧随其后的射击声。
    """
    k = _vpitch(v, n, 0.06)
    noise(buf, dur=0.20, peak=0.18, ftype="bandpass", f0=450 * k, f1=1900 * k, q=1.2, kind="pink")
    noise(buf, t0=0.04, dur=0.18, peak=0.12, ftype="bandpass", f0=900 * k, f1=1400 * k, q=4.0)
    noise(buf, dur=0.24, peak=0.10, ftype="lowpass", f0=1400 * k, f1=400 * k, kind="pink")
    tone(buf, wtype="sine", f0=140 * k, f1=60 * k, t0=0.24, dur=0.14, peak=0.14, a1=0.02)
    plips(buf, count=3, spread=0.03, f_lo=700 * k, f_hi=1500 * k, peak=0.05, dur=0.03)


# ---------------- 战斗反馈补充 ----------------

def _f_hit_marker(buf, v, n):
    """打击标记：极短高位「嗒」+ 共振湿 tsk + 上滑 plink（INKWAVE hit_marker 的 4 层）。

    连击升调由运行时控制 pitch，这里只留干净瞬态。
    """
    k = _vpitch(v, n, 0.04)
    click(buf, amp=0.16, f0=4500, dur=0.006)
    noise(buf, dur=0.04, peak=0.10, ftype="bandpass", f0=3400 * k, q=4.0)
    tone(buf, wtype="sine", f0=1650 * k, f1=1250 * k, dur=0.035, peak=0.16, a1=0.02)
    tone(buf, wtype="triangle", f0=3300 * k, dur=0.025, peak=0.05, a1=0.03)


def _f_hurt(buf, v, n):
    """受伤：比 death 短得多的下坠硬音 + 共振胸闷（可连续触发不糊）。"""
    k = _vpitch(v, n, 0.07)
    sat_tone(buf, f0=130 * k, f1=50 * k, dur=0.14, peak=0.24, drive=2.4, lp=(1400,))
    noise(buf, dur=0.10, peak=0.13, ftype="bandpass", f0=700 * k, f1=260 * k, q=5.0, kind="pink")
    noise(buf, dur=0.08, peak=0.07, ftype="lowpass", f0=600, q=1.0, kind="pink")


def _f_refill_full(buf, v, n):
    """墨罐回满：上滑 + 两颗 FM 铃（INKWAVE refill_full）——是小提示，不做成大招就绪。"""
    tone(buf, wtype="sine", f0=400, f1=1200, dur=0.14, peak=0.15, a1=0.03)
    for i, f in enumerate((784, 1046)):
        fm_tone(buf, fc=f, fm_ratio=2.0, index=1.2, index_decay=0.08,
                t0=0.03 + i * 0.06, dur=0.26, peak=0.07, a1=0.006)


def _f_low_ink_warning(buf, v, n):
    """墨水不足警报：两声固定音高的方波「嘀嘀」，不做变体变调（警报必须可辨识）。"""
    tone(buf, wtype="square", f0=880, dur=0.07, peak=0.16, a1=0.05, lp=(2400,))
    tone(buf, wtype="square", f0=880, t0=0.11, dur=0.09, peak=0.16, a1=0.03, lp=(2400,))
    noise(buf, dur=0.006, peak=0.04, ftype="highpass", f0=5000)


# ---------------- 副武器与大招 ----------------

def _f_bomb_throw(buf, v, n):
    """投掷炸弹：上抛的风声（三角上滑 + 80ms 起音的带通风压）。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="triangle", f0=330 * k, f1=210 * k, dur=0.12, peak=0.10, a1=0.02)
    noise(buf, dur=0.16, peak=0.17, ftype="bandpass", f0=500 * k, f1=2200 * k, q=0.9,
          kind="pink", a0=0.10, a1=0.55)
    air(buf, t0=0.02, dur=0.10, amp=0.05, f0=4800)


def _f_bomb_bounce(buf, v, n):
    """炸弹撞墙反弹：一记干硬的塑料「嗒」+ 低沉余震（提示「炸弹还在弹」）。"""
    k = _vpitch(v, n, 0.08)
    click(buf, amp=0.20, f0=2400 * k, dur=0.010)
    tone(buf, wtype="sine", f0=190 * k, f1=120 * k, dur=0.06, peak=0.13, a1=0.03)
    tone(buf, wtype="sine", f0=1450 * k, dur=0.05, peak=0.06, a1=0.06)


def _f_bomb_beep(buf, v, n):
    """引信提示音：极短高位电子「哔」，落地后按引信节奏重复。"""
    tone(buf, wtype="square", f0=1480, dur=0.045, peak=0.16, a1=0.01, lp=(4000,))
    tone(buf, wtype="sine", f0=2960, dur=0.03, peak=0.05, a1=0.01)


def _f_bomb_explode(buf, v, n):
    """炸弹爆炸：全库最重的打击音——低频下潜 + 粉噪体经饱和 + 墨粒雨 + 长场地反射。"""
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="sine", f0=90 * k, f1=30 * k, dur=0.70, peak=0.30, a1=0.002)
    click(buf, amp=0.20, f0=2000, dur=0.010)
    tone(buf, wtype="sine", f0=950 * k, f1=210 * k, dur=0.14, peak=0.18, a1=0.006)
    noise(buf, dur=0.45, peak=0.26, ftype="lowpass", f0=5000 * k, f1=180 * k, q=0.9, kind="pink")
    noise(buf, dur=0.24, peak=0.14, ftype="bandpass", f0=1500 * k, f1=350 * k, q=3.0)
    bloops(buf, t0=0.02, count=5, spread=0.05, f_lo=260 * k, f_hi=520 * k, peak=0.09)
    plips(buf, t0=0.06, count=9, spread=0.12, f_lo=600 * k, f_hi=1600 * k, peak=0.04)
    air(buf, dur=0.40, amp=0.07, f0=3000, a1=0.10)
    room(buf, mix=0.20, lp=2400)


def _f_ult_ready(buf, v, n):
    """潜能形态就绪：上升脉冲琶音 + FM 铃 + 上升噪声扫（INKWAVE special_ready 同构）。"""
    for i, f in enumerate((262, 330, 392, 523)):
        tone(buf, wtype="square", f0=f, t0=i * 0.075, dur=0.11, peak=0.09, a1=0.04, lp=(3500,))
    fm_tone(buf, fc=2093, fm_ratio=2.0, index=1.8, index_decay=0.12, t0=0.20, dur=0.42,
            peak=0.10, a1=0.006)
    noise(buf, t0=0.10, dur=0.40, peak=0.07, ftype="bandpass", f0=800, f1=6000, q=0.8,
          kind="pink", a0=0.10, a1=0.35)
    room(buf, taps=((0.041, 0.26), (0.087, 0.15)), mix=0.14, lp=4200)


def _f_ult_cast(buf, v, n):
    """潜能形态发动：先压后冲的扫频 + 噪声洗 + 低频落点（INKWAVE special_activate）。"""
    noise(buf, dur=0.35, peak=0.16, ftype="bandpass", f0=300, f1=3000, q=0.8, a0=0.05, a1=0.60)
    sat_tone(buf, f0=190, f1=1350, dur=0.42, peak=0.26, drive=1.8, lp=(900, 4200))
    tone(buf, wtype="sawtooth", f0=110, f1=440, dur=0.36, peak=0.12, a0=0.4, a1=0.10, lp=(400, 4000))
    tone(buf, wtype="sine", f0=900, f1=240, t0=0.30, dur=0.28, peak=0.15, a1=0.02)
    tone(buf, wtype="sine", f0=120, f1=40, t0=0.02, dur=0.40, peak=0.16, a1=0.004)
    room(buf, mix=0.14, lp=3600)


def _f_special_slam(buf, v, n):
    """超级降落砸地：先落体啸叫 0.3s，再低频猛砸 + 粉噪经饱和 + 震波尾（INKWAVE special_slam）。"""
    k = _vpitch(v, n, 0.04)
    tone(buf, wtype="sine", f0=1500, f1=180, dur=0.30, peak=0.14, a0=0.3, a1=0.4)
    tone(buf, wtype="sine", f0=70 * k, f1=24 * k, t0=0.26, dur=1.10, peak=0.30, a1=0.003)
    click(buf, t0=0.26, amp=0.20, f0=1600, dur=0.010)
    noise(buf, t0=0.26, dur=0.50, peak=0.26, ftype="lowpass", f0=6000 * k, f1=150 * k, q=1.0, kind="pink")
    noise(buf, t0=0.26, dur=0.30, peak=0.15, ftype="bandpass", f0=900 * k, f1=250 * k, q=2.5)
    bloops(buf, t0=0.30, count=6, spread=0.06, f_lo=260 * k, f_hi=560 * k, peak=0.09)
    air(buf, t0=0.27, dur=0.20, amp=0.08, f0=3400)
    room(buf, mix=0.20, lp=2400)


def _f_storm_thunder(buf, v, n):
    """风暴云落地雷声：4 记高频脆裂 + 棕噪雷体（双段起伏）+ 60→35Hz 潜低频 + 远雷回声。

    棕噪声是这里的质变点：旧版用白噪过低通，听起来像「噪声被捂住」；
    棕噪声天然是 -6dB/oct 的轰鸣，才是雷。
    """
    k = 0.94 + 0.10 * v
    for i in range(4):
        noise(buf, t0=0.005 + i * (0.042 + 0.018 * ((i * 7 + n) % 3)),
              dur=0.05, peak=0.20 - i * 0.02, ftype="highpass", f0=(3400 - i * 240) * k)
    noise(buf, t0=0.01, dur=1.10, peak=0.34, ftype="lowpass", f0=900 * k, f1=110 * k,
          q=0.7, kind="brown", a1=0.004)
    noise(buf, t0=0.30, dur=1.00, peak=0.20, ftype="lowpass", f0=250 * k, q=0.9, kind="brown", a1=0.02)
    tone(buf, wtype="sine", f0=60 * k, f1=35 * k, dur=0.90, peak=0.22, a1=0.004)
    room(buf, taps=((0.06, 0.30), (0.13, 0.22), (0.24, 0.14)), mix=0.20, lp=1400)


def _f_storm_wind(buf, v, n):
    """风暴云投掷的风压：低频棕噪呼啸 + 上升扫频（云在飞过来的那一段）。"""
    noise(buf, dur=0.55, peak=0.20, ftype="bandpass", f0=240, f1=900, q=0.7,
          kind="pink", a0=0.10, a1=0.40)
    noise(buf, dur=0.60, peak=0.16, ftype="lowpass", f0=180, q=0.9, kind="brown")
    tone(buf, wtype="sine", f0=70, f1=130, dur=0.55, peak=0.10, a0=0.2, a1=0.20)


# ---------------- 循环音效（环形颗粒床 + 周期噪声床） ----------------

def _f_roll(buf, v, n):
    """滚筒推地滚动：squelch 颗粒床过低通 + 棕噪低频碾压 + 5Hz 挤压感 + 周期湿气。

    旧版是「低通噪声 + 几声噼啪」，听起来像风扇；颗粒床才有「滚筒在湿地上挤」的质感。
    """
    grain_bed(buf, kind="squelch", peak=0.30, density=1.0)
    _bq(buf, 0, int(LOOP_DUR * SR) + int(XFADE_DUR * SR), "lowpass", 1400.0, wob=(5.0, 500.0))
    noise(buf, dur=2.15, peak=0.20, ftype="lowpass", f0=260, q=1.1, kind="brown", a0=1.0, a1=1.0)
    tone(buf, wtype="sine", f0=75, dur=2.15, peak=0.16, a0=1.0, a1=1.0)
    noise(buf, dur=2.15, peak=0.09, ftype="bandpass", f0=750, q=3.0, kind="pink",
          a0=1.0, a1=1.0, wob=(5.0, 180.0))


def _f_splatling_spin(buf, v, n):
    """加特林按住蓄力旋转：马达锯齿（LP 跟随）+ 齿轮啸叫 + 30Hz 方波调幅的枪管抖动 + 风噪。

    30Hz 方波做幅度调制是「枪管在抖」的关键——旧版只有静态马达嗡鸣，所以像吸尘器。
    """
    tone(buf, wtype="sawtooth", f0=180, dur=2.15, peak=0.15, a0=1.0, a1=1.0, lp=(1800,),
         wob=(5.0, 220.0))
    tone(buf, wtype="square", f0=360.4, dur=2.15, peak=0.055, a0=1.0, a1=1.0, lp=(1800,))
    tone(buf, wtype="sine", f0=90, dur=2.15, peak=0.10, a0=1.0, a1=1.0)
    _am_square(buf, 30.0, 0.45)
    noise(buf, dur=2.15, peak=0.09, ftype="bandpass", f0=2400, q=2.5, a0=1.0, a1=1.0)
    noise(buf, dur=2.15, peak=0.06, ftype="highpass", f0=3400, kind="pink", a0=1.0, a1=1.0)


def _f_swim(buf, v, n):
    """潜水游泳持续：bubbles 颗粒床 + 棕噪水下位移 + 0.7Hz 般的低频起伏（取 1.8s 整数倍）。

    旧的「低通噪声 + 定时气泡」有可听见的 0.26s 节律；颗粒床把节律打散成流动。
    """
    grain_bed(buf, kind="bubbles", peak=0.34, density=1.0)
    _bq(buf, 0, int(LOOP_DUR * SR) + int(XFADE_DUR * SR), "lowpass", 1300.0, wob=(5.0, 400.0))
    noise(buf, dur=2.15, peak=0.18, ftype="lowpass", f0=300, q=0.9, kind="brown", a0=1.0, a1=1.0)
    tone(buf, wtype="sine", f0=85, dur=2.15, peak=0.12, a0=1.0, a1=1.0)


def _f_climb(buf, v, n):
    """攀爬墨墙持续：bright bubbles 颗粒床过带通 + 7Hz 般的湿润摩擦 + 高频气声。

    速率取 1/1.8s 的整数倍（5.5556Hz = 10×），循环接缝才对齐。
    """
    grain_bed(buf, kind="bubbles_bright", peak=0.30, density=1.1)
    noise(buf, dur=2.15, peak=0.15, ftype="bandpass", f0=1600, q=1.2, kind="pink",
          a0=1.0, a1=1.0, wob=(5.5556, 600.0))
    tone(buf, wtype="triangle", f0=260, dur=2.15, peak=0.08, a0=1.0, a1=1.0)
    noise(buf, dur=2.15, peak=0.06, ftype="highpass", f0=3200, a0=1.0, a1=1.0)


def _f_enemy_ink_sizzle(buf, v, n):
    """敌方墨水灼烧持续：sizzle 颗粒床（腐蚀滋滋）+ 双速率幅度 LFO + 棕噪低层。

    双 LFO（11Hz / 17.3Hz）是 INKWAVE 的做法：两个不整比速率叠加后不再有可辨识的周期感。
    """
    grain_bed(buf, kind="sizzle", peak=0.42, density=1.0)
    noise(buf, dur=2.15, peak=0.16, ftype="highpass", f0=2800, kind="pink", a0=1.0, a1=1.0)
    _am_sine(buf, 11.1111, 0.30)
    _am_sine(buf, 16.6667, 0.22)
    noise(buf, dur=2.15, peak=0.10, ftype="lowpass", f0=400, kind="brown", a0=1.0, a1=1.0)


def _f_storm_rain(buf, v, n):
    """风暴墨雨持续：rain 颗粒床 + 粉噪雨幕 + 棕噪下沉风声 + 缓慢起伏。"""
    grain_bed(buf, kind="rain", peak=0.34, density=1.0)
    noise(buf, dur=2.15, peak=0.17, ftype="bandpass", f0=2200, f1=1800, q=0.6,
          kind="pink", a0=1.0, a1=1.0, wob=(5.5556, 500.0))
    noise(buf, dur=2.15, peak=0.16, ftype="lowpass", f0=200, q=1.0, kind="brown", a0=1.0, a1=1.0)
    tone(buf, wtype="sine", f0=65, dur=2.15, peak=0.08, a0=1.0, a1=1.0)


# ---------------- UI 补充 ----------------

def _f_ui_hover(buf, v, n):
    """悬停：25ms 极轻高音 + 细砂瞬态，只为「指到了」的触感。"""
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="sine", f0=1900 * k, f1=1450 * k, dur=0.030, peak=0.16, a1=0.02)
    click(buf, amp=0.05, f0=5000, dur=0.012)


def _f_ui_click(buf, v, n):
    """UI 按钮点击：正弦上滑 + 三角八度 + 一点带通咔哒（INKWAVE ui_click 的 3 层）。"""
    k = _vpitch(v, n, 0.08)
    tone(buf, wtype="sine", f0=380 * k, f1=1150 * k, dur=0.035, peak=0.18, a1=0.01)
    tone(buf, wtype="triangle", f0=760 * k, f1=2300 * k, dur=0.030, peak=0.08, a1=0.02)
    noise(buf, dur=0.010, peak=0.07, ftype="bandpass", f0=3000 * k, q=1.5)


def _f_ui_confirm(buf, v, n):
    """UI 确认：两音上跳，每个音带八度三角波（比纯正弦有实体感）。"""
    k = _vpitch(v, n, 0.03)
    arp(buf, [700 * k, 1050 * k], t_step=0.055, dur=0.18, peak=0.16, wtype="sine")
    arp(buf, [1400 * k, 2100 * k], t_step=0.055, dur=0.10, peak=0.05, wtype="triangle")


def _f_ui_toggle(buf, v, n):
    """开关切换：方波经低通的两声「嗒-嗒」，第二声更闷，形成明确的开关落位感。"""
    k = _vpitch(v, n, 0.05)
    tone(buf, wtype="square", f0=640 * k, f1=540 * k, dur=0.05, peak=0.14, a0=0.8, a1=0.03, lp=(2200,))
    tone(buf, wtype="sine", f0=1280 * k, dur=0.04, peak=0.07, a1=0.04)
    click(buf, amp=0.09, f0=4000, dur=0.009)


def _f_ui_back(buf, v, n):
    """返回/取消：两音下行（与 ui_confirm 的上行互为正反）。"""
    arp(buf, [880, 660], t_step=0.045, dur=0.10, peak=0.15, wtype="triangle")
    tone(buf, wtype="sine", f0=440, dur=0.06, peak=0.06, a1=0.05)


def _f_ui_slider(buf, v, n):
    """滑杆步进：比 ui_click 更窄更钝的细碎咔哒，拖动时不刺耳。"""
    k = _vpitch(v, n, 0.06)
    tone(buf, wtype="sine", f0=2600 * k, dur=0.022, peak=0.13, a1=0.04)
    tone(buf, wtype="triangle", f0=5200 * k, dur=0.010, peak=0.045, a1=0.06)
    click(buf, amp=0.045, f0=3800, dur=0.008)


def _f_ui_error(buf, v, n):
    """操作失败：155/163Hz 两只低方波做 8 音分拍频（低闷的「嗡嗡」），不是警报。"""
    for t in (0.0, 0.11):
        tone(buf, wtype="square", f0=155, t0=t, dur=0.09, peak=0.15, a1=0.04, lp=(1100,))
        tone(buf, wtype="square", f0=163, t0=t, dur=0.09, peak=0.13, a1=0.04, lp=(1100,))
    noise(buf, dur=0.02, peak=0.05, ftype="lowpass", f0=800)


def _f_pause_open(buf, v, n):
    """暂停打开：下行「收住」的两音 + 低频落点（世界被按下暂停的感觉）。"""
    arp(buf, [660, 494], t_step=0.06, dur=0.20, peak=0.14, wtype="triangle")
    tone(buf, wtype="sine", f0=165, f1=110, dur=0.22, peak=0.12, a1=0.04)


def _f_pause_close(buf, v, n):
    """暂停恢复：上行「放开」的两音（与 pause_open 互为正反）。"""
    arp(buf, [494, 660], t_step=0.06, dur=0.20, peak=0.14, wtype="triangle")
    tone(buf, wtype="sine", f0=110, f1=165, dur=0.22, peak=0.12, a0=0.5, a1=0.04)


def _f_reticle_lock(buf, v, n):
    """瞄准锁定 tick：极轻的高位双点（准星咬住目标时的确认），比 hit_marker 更弱更干。"""
    k = _vpitch(v, n, 0.04)
    tone(buf, wtype="sine", f0=2200 * k, dur=0.012, peak=0.11, a1=0.05)
    tone(buf, wtype="sine", f0=2600 * k, t0=0.030, dur=0.014, peak=0.09, a1=0.05)


def _f_weapon_switch(buf, v, n):
    """局内换枪：机械「咔嗒」+ 短促上滑，提示「换好了」（比 UI 点击更硬）。"""
    click(buf, amp=0.20, f0=2800, dur=0.012)
    noise(buf, dur=0.06, peak=0.12, ftype="bandpass", f0=1400, f1=2600, q=1.2, kind="pink")
    tone(buf, wtype="sine", f0=300, f1=620, dur=0.09, peak=0.12, a1=0.03)
    tone(buf, wtype="sine", f0=980, dur=0.05, peak=0.06, a1=0.05)


def _f_charge_cancel(buf, v, n):
    """蓄力取消：蓄起来的能量被泄掉——下行的气声 + 一句闷响（不是「叮」，明确是中止）。"""
    noise(buf, dur=0.16, peak=0.16, ftype="bandpass", f0=1800, f1=380, q=0.9, kind="pink", a1=0.10)
    tone(buf, wtype="sine", f0=420, f1=170, dur=0.14, peak=0.13, a1=0.04)
    noise(buf, dur=0.08, peak=0.07, ftype="lowpass", f0=700, q=1.0, kind="pink")


# key → (变体数, 渲染函数, 归一化目标峰值)
# 峰值目标：战斗反馈音给足（0.30~0.34），UI/提示音压低（0.20~0.26），避免 UI 抢戏。
# P1-D 起统一在 0.22~0.34 分档，全部留足 headroom，混音音量交给 AudioManager 控制。
SFX = {
    # 主武器与涂地（签名音，变体最多 —— 战斗里最容易听出「复读机」的几个）
    "shot":           (6, _f_shot,           0.34),
    "ink_hit_ground": (5, _f_ink_hit_ground, 0.30),
    "splat_small":    (3, _f_splat_small,    0.28),
    "splat_big":      (2, _f_splat_big,      0.32),
    "ink_hit_wall":   (3, _f_ink_hit_wall,   0.30),
    # 武器操作（7 类主武器）
    "roller_flick":   (4, _f_roller_flick,   0.32),
    "charger_charge": (1, _f_charger_charge, 0.26),
    "charger_full":   (1, _f_charger_full,   0.26),
    "charger_shot":   (2, _f_charger_shot,   0.34),
    "blaster_shot":   (4, _f_blaster_shot,   0.30),
    "blaster_pump":   (2, _f_blaster_pump,   0.28),
    "blaster_boom":   (2, _f_blaster_boom,   0.34),
    "slosh_throw":    (2, _f_slosh_throw,    0.28),
    "slosh_land":     (3, _f_slosh_land,     0.32),
    "splatling_ready": (1, _f_splatling_ready, 0.28),
    # 移动与潜墨
    "squid_in":       (2, _f_squid_in,       0.26),
    "squid_out":      (2, _f_squid_out,      0.26),
    "swim_splash":    (3, _f_swim_splash,    0.24),
    "step_ink_bubble": (5, _f_step_ink_bubble, 0.22),
    "step_dry":       (5, _f_step_dry,       0.22),
    "step_enemy":     (5, _f_step_enemy,     0.22),
    "ink_drip":       (3, _f_ink_drip,       0.22),
    "jump":           (2, _f_jump,           0.24),
    "land":           (2, _f_land,           0.26),
    "dodge_roll":     (3, _f_dodge_roll,     0.28),
    # 战斗反馈
    "hit_enemy":      (3, _f_hit_enemy,      0.30),
    "hit_marker":     (4, _f_hit_marker,     0.30),
    "hurt":           (2, _f_hurt,           0.32),
    "kill":           (2, _f_kill,           0.30),
    "death":          (2, _f_death,          0.32),
    "respawn":        (1, _f_respawn,        0.26),
    "spawn_protect":  (1, _f_spawn_protect,  0.26),
    "low_health":     (1, _f_low_health,     0.26),
    "respawn_tick":   (1, _f_respawn_tick,   0.22),
    # 墨水资源
    "ink_empty":      (1, _f_ink_empty,      0.26),
    "ink_refill":     (3, _f_ink_refill,     0.24),
    "refill_full":    (1, _f_refill_full,    0.24),
    "low_ink_warning": (1, _f_low_ink_warning, 0.30),
    # 副武器与大招
    "bomb_throw":     (2, _f_bomb_throw,     0.26),
    "bomb_bounce":    (2, _f_bomb_bounce,    0.22),
    "bomb_beep":      (1, _f_bomb_beep,      0.24),
    "bomb_explode":   (2, _f_bomb_explode,   0.34),
    "ult_ready":      (1, _f_ult_ready,      0.24),
    "ult_cast":       (1, _f_ult_cast,       0.30),
    "special_slam":   (2, _f_special_slam,   0.34),
    "storm_thunder":  (2, _f_storm_thunder,  0.45),
    "storm_wind":     (1, _f_storm_wind,     0.26),
    # 比赛流程
    "countdown_tick": (1, _f_countdown_tick, 0.26),
    "match_start":    (1, _f_match_start,    0.26),
    "ready":          (1, _f_ready,          0.26),
    "go_horn":        (1, _f_go_horn,        0.30),
    "one_minute":     (1, _f_one_minute,     0.28),
    "match_end":      (1, _f_match_end,      0.28),
    "times_up":       (1, _f_times_up,       0.28),
    "final_10s":      (1, _f_final_10s,      0.26),
    "lead_gain":      (1, _f_lead_gain,      0.24),
    "lead_lose":      (1, _f_lead_lose,      0.24),
    "enemy_special_warn": (1, _f_enemy_special_warn, 0.28),
    "result_win":     (1, _f_result_win,     0.26),
    "result_lose":    (1, _f_result_lose,    0.24),
    # UI
    "ui_hover":       (2, _f_ui_hover,       0.22),
    "ui_click":       (4, _f_ui_click,       0.22),
    "ui_confirm":     (2, _f_ui_confirm,     0.24),
    "ui_toggle":      (2, _f_ui_toggle,      0.24),
    "ui_back":        (2, _f_ui_back,        0.24),
    "ui_slider":      (2, _f_ui_slider,      0.22),
    "ui_error":       (1, _f_ui_error,       0.26),
    "pause_open":     (1, _f_pause_open,     0.24),
    "pause_close":    (1, _f_pause_close,    0.24),
    "reticle_lock":   (2, _f_reticle_lock,   0.22),
    "weapon_switch":  (1, _f_weapon_switch,  0.24),
    "charge_cancel":  (1, _f_charge_cancel,  0.24),
    # 循环音效（无缝循环通道）
    "roll":             (1, _f_roll,             0.24),
    "splatling_spin":   (1, _f_splatling_spin,   0.21),
    "swim":             (1, _f_swim,             0.18),
    "climb":            (1, _f_climb,            0.16),
    "enemy_ink_sizzle": (1, _f_enemy_ink_sizzle, 0.26),
    "storm_rain":       (1, _f_storm_rain,       0.22),
}

# 循环音效键集合（使用无缝循环交叉淡化，同时输出 *_1.wav 与 *_loop_1.wav）
# charger_charge 一并纳入：蓄力是持续音，旧版拿一次性 wav 当循环播，接缝会「咔」。
LOOP_KEYS = {"roll", "splatling_spin", "swim", "climb", "enemy_ink_sizzle", "storm_rain", "charger_charge"}

# 保留 key 清单（供 M5/M6 建 SfxLibrary SO 时对照；改这里要同步文档）
GROUPS = {
    "主武器与涂地": ["shot", "ink_hit_ground", "splat_small", "splat_big", "ink_hit_wall"],
    "武器操作":     ["roller_flick", "charger_charge", "charger_full", "charger_shot",
                     "blaster_shot", "blaster_pump", "blaster_boom", "slosh_throw",
                     "slosh_land", "splatling_ready"],
    "移动与潜墨":   ["squid_in", "squid_out", "swim_splash", "step_ink_bubble",
                     "step_dry", "step_enemy", "ink_drip", "jump", "land", "dodge_roll"],
    "战斗反馈":     ["hit_enemy", "hit_marker", "hurt", "kill", "death", "respawn",
                     "spawn_protect", "low_health", "respawn_tick"],
    "墨水资源":     ["ink_empty", "ink_refill", "refill_full", "low_ink_warning"],
    "副武器与大招": ["bomb_throw", "bomb_bounce", "bomb_beep", "bomb_explode", "ult_ready",
                     "ult_cast", "special_slam", "storm_thunder", "storm_wind"],
    "比赛流程":     ["countdown_tick", "match_start", "ready", "go_horn", "one_minute",
                     "match_end", "times_up", "final_10s", "lead_gain", "lead_lose",
                     "enemy_special_warn", "result_win", "result_lose"],
    "UI":           ["ui_hover", "ui_click", "ui_confirm", "ui_toggle", "ui_back",
                     "ui_slider", "ui_error", "pause_open", "pause_close",
                     "reticle_lock", "weapon_switch", "charge_cancel"],
    "循环音效":     ["roll", "splatling_spin", "swim", "climb", "enemy_ink_sizzle",
                     "storm_rain"],
}

# 允许超过 0.9s 的长音（流程/胜负提示与循环音天然带尾音或本身很长，不按「过长」告警）
LONG_OK = {
    "match_end", "result_win", "result_lose", "times_up", "one_minute", "ready",
    "bomb_explode", "special_slam", "go_horn", "storm_thunder", "storm_wind",
    "charge_cancel", "respawn",
    "roll", "splatling_spin", "swim", "climb", "enemy_ink_sizzle", "storm_rain",
    "charger_charge", "charger_charge_loop",
    "roll_loop", "splatling_spin_loop", "swim_loop", "climb_loop", "enemy_ink_sizzle_loop",
    "storm_rain_loop",
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


def bake_one(key, idx, total, fn, target_peak, is_loop=False):
    """渲染单个变体，返回 (浮点缓冲, 客观指标)。"""
    # 每个变体独立固定种子：加变体或改顺序不会影响已有变体的波形
    _reset_seed(0x2F6E2B1 ^ (_key_hash(key) & 0xFFFF) ^ (idx * 0x9E3779B1))
    dur_win = RENDER_WINDOW_LOOP if is_loop else RENDER_WINDOW
    buf = [0.0] * int(dur_win * SR)
    fn(buf, idx, total)
    _dc_remove(buf)
    if is_loop:
        buf = _make_seamless_loop(buf, loop_len_s=LOOP_DUR, xfade_s=XFADE_DUR)
    else:
        buf = _trim_tail(buf)
    _soft_limit(buf, 1.3)  # 先压瞬态尖峰，再归一化：同峰值下听感更厚
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
        is_loop = key in LOOP_KEYS
        for idx in range(total):
            buf, st = bake_one(key, idx, total, fn, target, is_loop=is_loop)
            names_to_write = ["%s_%d.wav" % (key, idx + 1)]
            if is_loop:
                names_to_write.append("%s_loop_%d.wav" % (key, idx + 1))
            for name in names_to_write:
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
    ap.add_argument("--loop", "--loops", dest="loops", action="store_true", help="只烘焙循环音效族")
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
    if a.loops:
        only = set(LOOP_KEYS)
    if a.only:
        spec_only = {s.strip() for s in a.only.split(",") if s.strip()}
        only = spec_only if only is None else (only & spec_only)
        unknown = spec_only - set(SFX)
        if unknown:
            print("未知音效族: %s\n可用: %s" % (", ".join(sorted(unknown)), ", ".join(sorted(SFX))), file=sys.stderr)
            return 2
    return bake(only=only, write_assets=not a.no_assets)


if __name__ == "__main__":
    sys.exit(main())