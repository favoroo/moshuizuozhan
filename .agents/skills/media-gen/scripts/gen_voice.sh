#!/bin/bash
# gen_voice.sh —— 生成中文语音台词（TTS，输出 24kHz 16bit 单声道 WAV）
#
# 用法:
#   gen_voice.sh -t "台词" [--style "音色/语气描述"] [-o 输出前缀] [-m 模型] [--timeout 秒]
#
# 参数:
#   -t        要朗读的台词文本（必填，逐字朗读；大写/标点可控制停顿重音）
#   --style   音色与语气描述（如"低沉沙哑的中年男声，语速慢"）。
#             角色音色一致性：把每个角色的固定音色描述登记在 MediaRaw/style/STYLE.md 的 VOICES 表里
#   -o        输出前缀（默认 MediaRaw/voice/vo_时间戳），最终文件为 <前缀>.wav
#   -m        模型：gemini-3.8-flash-tts（默认）/ gemini-3.8-flash-lite-tts（便宜快速）
#   --timeout 单次请求超时秒数（默认 120）
#
# 说明: 音色/语气用【方括号舞台指令】控制，如 "[低沉沙哑的中年男声，缓慢阴森]台词..."。
#       实测（时长对照实验）：直接写"用XX的语气说："会被朗读出来（时长随指令长度增长），
#       而 [方括号] 指令不会被朗读（长指令版时长≈基线）。
#       角色音色一致性：把每个角色的固定音色描述登记在 STYLE.md 的 VOICES 表里，原样复用。
#
# Unity 落地: 24kHz 单声道 WAV 可直接进 Unity；入库到 Assets/_Project/Audio/Voice/ 后，
#             MediaImportPostprocessor.cs 会自动套成 CompressedInMemory + Vorbis + forceToMono。
#             长台词若嫌体积大，可后续统一转 OGG（本机 ffmpeg 已装）。
#
# 输出: 成功时最后一行打印 "DONE: <wav 路径>"

set -eo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib_common.sh"

MODEL="gemini-3.8-flash-tts"; TEXT=""; STYLE=""; OUT=""; TIMEOUT=120
while [ $# -gt 0 ]; do
  case "$1" in
    -t) TEXT="$2"; shift 2 ;;
    --style) STYLE="$2"; shift 2 ;;
    -o) OUT="$2"; shift 2 ;;
    -m) MODEL="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    *) echo "未知参数: $1（用法见文件头注释）" >&2; exit 2 ;;
  esac
done
[ -n "$TEXT" ] || { echo "缺少 -t 台词" >&2; exit 2; }
[ -n "$OUT" ] || OUT="$RAW/voice/vo_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$(dirname "$OUT")"

if [ -n "$STYLE" ]; then
  PROMPT="[${STYLE}]${TEXT}"
else
  PROMPT="$TEXT"
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

EXT="${GOT##*.}"
cp "$GOT" "$OUT.$EXT"
FINAL="$OUT.$EXT"

manifest_append voice "$MODEL" "style=${STYLE} | text=${TEXT}" "" "$FINAL" >/dev/null
echo "DONE: $FINAL"
