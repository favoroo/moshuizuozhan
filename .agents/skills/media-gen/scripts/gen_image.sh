#!/bin/bash
# gen_image.sh —— 生成游戏图片素材（支持参考图风格锁定、品红底自动抠透明）
#
# 用法:
#   gen_image.sh -p "提示词" [-o 输出前缀] [-m 模型] [-r 参考图.png]... [--keyout [最长边]] [--timeout 秒]
#
# 参数:
#   -p          提示词（必填，中文可用）
#   -o          输出文件前缀（默认 MediaRaw/images/img_时间戳）
#   -m          模型：gemini-3.1-flash-image（唯一可用，默认）
#               注意 gemini-3.1-flash-lite-image 自 2026-10-28 起被上游禁用
#               （400 PROXY_GENERATION_DISABLED），别再用它，草稿也用默认模型
#   -r          参考图路径（可多次出现，风格锁定用；相对路径按项目根解析）
#   --keyout    品红底生成 + 自动抠成透明 PNG（可选跟最长边像素，如 --keyout 256）
#               原始品红底图保留为 <前缀>.raw.jpg 便于重新抠图
#   --grid      网格切表：生成一张 列x行 宫格素材表后自动切为单张 PNG（如 2x2），
#               输出 <前缀>_r行c列.png；与 --keyout 同用时逐格抠透明
#               用于图标网格/墨点形状差分表/UI 状态差分（同表内天然同画风同规格）
#   --timeout   单次请求超时秒数（默认 180）
#
# 示例:
#   gen_image.sh -p "游戏图标：墨水罐，简洁剪影"
#   gen_image.sh -p "${PREFIX} 中性单色 UI 图标：墨水罐，纯白主体" \
#                -r MediaRaw/style/anchor.png --keyout 256 -o MediaRaw/images/ui_icon_inktank
#
# 输出: 成功时最后一行打印 "DONE: <最终文件路径>"

set -eo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib_common.sh"

MODEL="gemini-3.1-flash-image"; PROMPT=""; OUT=""; TIMEOUT=180
KEYOUT=0; SIZE=0; GRID=""; REFS=()
while [ $# -gt 0 ]; do
  case "$1" in
    -p) PROMPT="$2"; shift 2 ;;
    -o) OUT="$2"; shift 2 ;;
    -m) MODEL="$2"; shift 2 ;;
    -r) REFS+=("$2"); shift 2 ;;
    --keyout)
      KEYOUT=1; shift
      case "${1:-}" in ''|*[!0-9]*) ;; *) SIZE="$1"; shift ;; esac
      ;;
    --grid) GRID="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    *) echo "未知参数: $1（用法见文件头注释）" >&2; exit 2 ;;
  esac
done
[ -n "$PROMPT" ] || { echo "缺少 -p 提示词" >&2; exit 2; }
[ -z "$GRID" ] || echo "$GRID" | grep -Eq '^[0-9]+x[0-9]+$' || { echo "--grid 格式应为 列x行，如 3x1" >&2; exit 2; }
[ -n "$OUT" ] || OUT="$RAW/images/img_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$(dirname "$OUT")"

if [ "$KEYOUT" = 1 ]; then
  if [ -n "$GRID" ]; then
    PROMPT="$PROMPT

背景必须是纯品红色 #FF00FF 实色，各分格主体分别居中、不接触分格边缘，背景无渐变、无投影、无光晕。"
  else
    PROMPT="$PROMPT

背景必须是纯品红色 #FF00FF 实色，主体居中、不接触画面边缘，背景无渐变、无投影、无光晕。"
  fi
fi

T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT

MODEL="$MODEL" PROMPT="$PROMPT" ROOT="$ROOT" REFS="$(printf '%s\n' "${REFS[@]:-}")" \
python3 - > "$T/payload.json" <<'PY'
import json, os, sys, base64
model, prompt = os.environ["MODEL"], os.environ["PROMPT"]
content = []
for r in [r for r in os.environ.get("REFS", "").split("\n") if r.strip()]:
    if not os.path.exists(r):
        r = os.path.join(os.environ["ROOT"], r)
    ext = r.lower().rsplit(".", 1)[-1]
    mime = {"png": "image/png", "webp": "image/webp"}.get(ext, "image/jpeg")
    b64 = base64.b64encode(open(r, "rb").read()).decode()
    content.append({"type": "image_url", "image_url": {"url": "data:%s;base64,%s" % (mime, b64)}})
content.append({"type": "text", "text": prompt})
print(json.dumps({"model": model, "messages": [{"role": "user", "content": content}]}))
PY

GOT=""
for ATTEMPT in 1 2; do
  curl -s -m "$TIMEOUT" --noproxy '*' "$MEDIA_API_BASE/v1/chat/completions" \
    -H "Authorization: Bearer $MEDIA_API_KEY" -H "Content-Type: application/json" \
    -d @"$T/payload.json" -o "$T/resp.json" || true
  GOT="$(python3 "$SCRIPT_DIR/lib_extract.py" "$T/resp.json" "$T/media" image 2>/dev/null | head -1 || true)"
  [ -n "$GOT" ] && break
  echo "第 $ATTEMPT 次尝试未返回图片，重试中..." >&2
  sleep 2
done
[ -n "$GOT" ] || { echo "生成失败：两次尝试均未拿到图片" >&2; exit 1; }

if [ -n "$GRID" ]; then
  COLS="${GRID%x*}"; ROWS="${GRID#*x}"
  cp "$GOT" "$OUT.raw.jpg"
  python3 "$SCRIPT_DIR/split_grid.py" "$GOT" "$T/cell" "$COLS" "$ROWS" >&2
  FINAL=""
  for c in "$T"/cell_r*.png; do
    [ -f "$c" ] || continue
    sfx="$(basename "$c" .png)"; sfx="${sfx#cell_}"
    if [ "$KEYOUT" = 1 ]; then
      if [ "$SIZE" -gt 0 ] 2>/dev/null; then
        python3 "$SCRIPT_DIR/keyout.py" "$c" "${OUT}_${sfx}.png" "$SIZE" >&2
      else
        python3 "$SCRIPT_DIR/keyout.py" "$c" "${OUT}_${sfx}.png" >&2
      fi
    else
      cp "$c" "${OUT}_${sfx}.png"
    fi
    FINAL="${OUT}_${sfx}.png"
  done
  [ -n "$FINAL" ] || { echo "切分失败：未产出任何分格" >&2; exit 1; }
  echo "已切分为 ${COLS}x${ROWS} 共 $((COLS * ROWS)) 格（${OUT}_r1c1.png 起）" >&2
elif [ "$KEYOUT" = 1 ]; then
  cp "$GOT" "$OUT.raw.jpg"
  if [ "$SIZE" -gt 0 ] 2>/dev/null; then
    python3 "$SCRIPT_DIR/keyout.py" "$GOT" "$OUT.png" "$SIZE" >&2
  else
    python3 "$SCRIPT_DIR/keyout.py" "$GOT" "$OUT.png" >&2
  fi
  FINAL="$OUT.png"
else
  EXT="${GOT##*.}"
  cp "$GOT" "$OUT.$EXT"
  FINAL="$OUT.$EXT"
fi

REFS_CSV="$(printf '%s|' "${REFS[@]:-}")"
manifest_append image "$MODEL" "$PROMPT" "$REFS_CSV" "$FINAL" >/dev/null
echo "DONE: $FINAL"
