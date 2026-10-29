#!/bin/bash
# gen_music.sh —— 生成 BGM 配乐（lyria-3.5，输出 44.1kHz 立体声 MP3，单段约 2 分钟）
#
# 用法:
#   gen_music.sh -p "音乐描述" [-o 输出前缀] [-m 模型] [--ogg] [--timeout 秒]
#
# 参数:
#   -p        音乐描述（必填）。写清：曲风/情绪/节奏BPM/主要乐器；
#             BGM 加 "instrumental only, no vocals"；结构可用 [Verse]/[Chorus] 等段落标签
#   -o        输出前缀（默认 MediaRaw/music/bgm_时间戳）
#   -m        模型（默认 lyria-3.5，网关当前唯一音乐模型）
#   --ogg     额外用 ffmpeg 转一份 OGG(Vorbis)，供 Unity 无缝循环用（见下）
#             编码器按优先级自动探测：oggenc > ffmpeg libvorbis > ffmpeg 内置 vorbis。
#             实测本机 ffmpeg 8.1.2 无 libvorbis，只能走内置 vorbis（需 -ac 2，音质略低）；
#             想要更好音质：brew install vorbis-tools 后重跑，脚本会自动改用 oggenc。
#   --timeout 单次请求超时秒数（默认 300，实测一首约 35~60s）
#
# Unity 循环播放（与 Godot 的关键差异，务必读懂再用）:
#   Godot 导入 MP3 勾 loop 即无缝；**Unity 不行**——Unity 没有导入期 loop 开关
#   （循环是运行时的 AudioSource.loop = true），而且 MP3 有编码器 padding，
#   循环回接处会有可听间隙。所以需要无缝循环的曲目请加 --ogg：
#   Vorbis 在 Unity 里能样本级无缝循环。代价是 MP3→OGG 的二次有损转码，
#   所以源 MP3 一并保留（<前缀>.mp3），需要无损可自行从源重新转。
#   一次性播放的曲子（结算/开场短曲）不必转，直接用 MP3 省事。
#
# 输出: 成功时最后一行打印 "DONE: <文件路径>"（带 --ogg 时为 ogg 路径）
#       注意 manifest 登记的是这个「入库目标」，源 mp3 与它同名前缀并列存放。

set -eo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib_common.sh"

MODEL="lyria-3.5"; PROMPT=""; OUT=""; TIMEOUT=300; WANT_OGG=0
while [ $# -gt 0 ]; do
  case "$1" in
    -p) PROMPT="$2"; shift 2 ;;
    -o) OUT="$2"; shift 2 ;;
    -m) MODEL="$2"; shift 2 ;;
    --ogg) WANT_OGG=1; shift ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    *) echo "未知参数: $1（用法见文件头注释）" >&2; exit 2 ;;
  esac
done
[ -n "$PROMPT" ] || { echo "缺少 -p 音乐描述" >&2; exit 2; }
[ -n "$OUT" ] || OUT="$RAW/music/bgm_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$(dirname "$OUT")"

FFMPEG=""
OGGENC=""
VENC=""
if [ "$WANT_OGG" = 1 ]; then
  FFMPEG="$(command -v ffmpeg 2>/dev/null || true)"
  # 非交互 shell 的 PATH 常缺 /opt/homebrew/bin（AGENTS.md 记过同类坑）
  if [ -z "$FFMPEG" ] && [ -x /opt/homebrew/bin/ffmpeg ]; then
    FFMPEG=/opt/homebrew/bin/ffmpeg
  fi
  # Vorbis 编码器探测。优先级 oggenc(libvorbis) > ffmpeg libvorbis > ffmpeg 内置 vorbis。
  # 实测本机 ffmpeg 8.1.2 **不含 libvorbis**，只有内置 vorbis：
  # 内置版仅支持 2 声道且需 -strict experimental，质量不如 libvorbis，
  # 想更好音质可 `brew install vorbis-tools`（提供 oggenc）后重跑。
  OGGENC="$(command -v oggenc 2>/dev/null || true)"
  if [ -z "$OGGENC" ] && [ -n "$FFMPEG" ]; then
    if "$FFMPEG" -hide_banner -encoders 2>/dev/null | grep -qE '[[:space:]]libvorbis[[:space:]]'; then
      VENC="libvorbis"
    elif "$FFMPEG" -hide_banner -encoders 2>/dev/null | grep -qE '[[:space:]]vorbis[[:space:]]'; then
      VENC="vorbis"
    fi
  fi
  if [ -z "$OGGENC" ] && [ -z "$VENC" ]; then
    echo "--ogg 需要 Vorbis 编码器，但本机既无 oggenc 也无 ffmpeg libvorbis/vorbis。" >&2
    echo "  可选：brew install vorbis-tools  或  brew install ffmpeg（含 libvorbis）" >&2
    exit 1
  fi
fi

T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT

MODEL="$MODEL" PROMPT="$PROMPT" python3 - > "$T/payload.json" <<'PY'
import json, os
print(json.dumps({
    "model": os.environ["MODEL"],
    "messages": [{"role": "user", "content": os.environ["PROMPT"]}],
}))
PY

GOT=""
for ATTEMPT in 1 2; do
  curl -s -m "$TIMEOUT" --noproxy '*' "$MEDIA_API_BASE/v1/chat/completions" \
    -H "Authorization: Bearer $MEDIA_API_KEY" -H "Content-Type: application/json" \
    -d @"$T/payload.json" -o "$T/resp.json" || true
  GOT="$(python3 "$SCRIPT_DIR/lib_extract.py" "$T/resp.json" "$T/media" audio 2>/dev/null | head -1 || true)"
  [ -n "$GOT" ] && break
  echo "第 $ATTEMPT 次尝试未返回音频，重试中..." >&2
  sleep 2
done
[ -n "$GOT" ] || { echo "生成失败：两次尝试均未拿到音频" >&2; exit 1; }

SRC="$OUT.${GOT##*.}"
cp "$GOT" "$SRC"
FINAL="$SRC"

if [ "$WANT_OGG" = 1 ]; then
  if [ -n "$OGGENC" ]; then
    # oggenc 走 libvorbis，质量最好
    "$OGGENC" -q 7 -o "$OUT.ogg" "$SRC" \
      || { echo "oggenc 转码失败；源文件已保留: $SRC" >&2; exit 1; }
  else
    EXTRA=()
    [ "$VENC" = "vorbis" ] && EXTRA=(-strict experimental)
    # -ac 2：内置 vorbis 编码器只支持 2 声道（实测单声道会直接报错）
    "$FFMPEG" -hide_banner -loglevel error -y -i "$SRC" -ac 2 \
      -c:a "$VENC" "${EXTRA[@]}" -b:a 192k "$OUT.ogg" \
      || { echo "ffmpeg($VENC) 转码失败；源文件已保留: $SRC" >&2; exit 1; }
    [ "$VENC" = "vorbis" ] && echo "注意: 用的是 ffmpeg 内置 vorbis（本机无 libvorbis），音质低于标准 Vorbis" >&2
  fi
  FINAL="$OUT.ogg"
  echo "已转 OGG(Vorbis) 供 Unity 无缝循环；源 MP3 保留在 $SRC" >&2
fi

manifest_append music "$MODEL" "$PROMPT" "" "$FINAL" >/dev/null
echo "DONE: $FINAL"