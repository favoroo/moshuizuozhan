#!/bin/bash
# selfcheck.sh —— 零配额自检：确认媒体网关与本地依赖可用，不产生任何生成调用
#
# 用法:
#   selfcheck.sh
#
# 检查项:
#   1. 工程根与 MediaRaw 目录可写
#   2. config/api.env 存在且含 MEDIA_API_BASE / MEDIA_API_KEY
#   3. 网关可达（GET /v1/models，只读列表，不消耗生成配额）
#   4. 期望模型在网关名单里（缺哪个就报哪个，不阻断）
#   5. python3 依赖 numpy + PIL（keyout.py 需要）
#   6. ffmpeg（仅 gen_music.sh --ogg 需要，缺了只是转码不可用）
#   7. tools/bake_sfx.py 存在（本地音效烘焙，零配额）
#
# 退出码: 0 = 关键项全过；1 = 有硬性失败（1/2/3/5 任一项不过）
# 输出末尾打印可直接复制的运行环境摘要，便于贴进问题报告。

set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib_common.sh"

TMP_RESP="$(mktemp)"; trap 'rm -f "$TMP_RESP"' EXIT
FAIL=0
ok()   { printf '  \033[32m✓\033[0m %s\n' "$1"; }
bad()  { printf '  \033[31m✗\033[0m %s\n' "$1"; FAIL=1; }
note() { printf '  \033[33m!\033[0m %s\n' "$1"; }

echo "media-gen 自检（P1 Unity 工程）"
echo "项目根: $ROOT"
echo "原稿区: $RAW"
echo

echo "[1] 目录"
if [ -f "$ROOT/ProjectSettings/ProjectVersion.txt" ]; then
  ok "Unity 工程标识存在（$(grep -m1 m_EditorVersion "$ROOT/ProjectSettings/ProjectVersion.txt" | cut -d' ' -f2)）"
else
  bad "未找到 ProjectSettings/ProjectVersion.txt —— 工程根定位错了？"
fi
if mkdir -p "$RAW/images" && touch "$RAW/.selfcheck" 2>/dev/null; then
  rm -f "$RAW/.selfcheck"; ok "MediaRaw 可写"
else
  bad "MediaRaw 不可写"
fi

echo "[2] 配置"
if [ -f "$API_ENV" ]; then
  ok "config/api.env 存在"
  [ -n "${MEDIA_API_BASE:-}" ] && ok "MEDIA_API_BASE = $MEDIA_API_BASE" || bad "缺 MEDIA_API_BASE"
  [ -n "${MEDIA_API_KEY:-}" ] && ok "MEDIA_API_KEY 已设置（长度 ${#MEDIA_API_KEY}，不回显）" || bad "缺 MEDIA_API_KEY"
else
  bad "缺 config/api.env（首次部署需从网关配置拷入；该文件已 gitignore）"
fi

echo "[3] 网关"
# /v1/models 也要带鉴权（不带会得到 401 {"error":"Missing API key"}，别误判成「网关没模型」）
CODE="$(curl -s -m 10 --noproxy '*' -o "$TMP_RESP" -w '%{http_code}' \
        -H "Authorization: Bearer $MEDIA_API_KEY" \
        "$MEDIA_API_BASE/v1/models" 2>/dev/null || echo 000)"
case "$CODE" in
  200) ok "GET $MEDIA_API_BASE/v1/models → 200" ;;
  000) bad "网关不可达（检查网关进程是否在跑；本机 ALL_PROXY 常见失效，已带 --noproxy '*'）" ;;
  401|403) bad "网关拒绝鉴权（HTTP $CODE）—— config/api.env 里的 MEDIA_API_KEY 失效或过期" ;;
  *)   bad "网关返回 HTTP $CODE" ;;
esac

echo "[4] 模型名单"
if [ "$CODE" = 200 ] && [ -s "$TMP_RESP" ]; then
  python3 - "$TMP_RESP" <<'PY'
import json, sys
try:
    ids = [m.get("id", "") for m in json.load(open(sys.argv[1])).get("data", [])]
except Exception as e:
    print("  ! 响应不是预期的 /v1/models JSON: %s" % e); sys.exit(0)
want = ["gemini-3.1-flash-image", "gemini-3.1-flash-lite-image",
        "gemini-3.8-flash-tts", "gemini-3.8-flash-lite-tts",
        "lyria-3.5", "lyria-3-pro-preview"]
# 名单里有 ≠ 真能用：下面这些是实测已知的「在名单但通道不可用」
KNOWN_BROKEN = {
    "gemini-3.1-flash-lite-image": "2026-10-28 实测 PROXY_GENERATION_DISABLED，改用品质模型",
    "lyria-3.5": "2026-10-29 实测 PROXY_GENERATION_DISABLED，BGM 改用 tools/bake_bgm.py",
    "lyria-3-pro-preview": "2026-10-29 实测同样 PROXY_GENERATION_DISABLED（gen_music.sh 的备用模型）",
}
print("  网关共 %d 个模型" % len(ids))
for w in want:
    if w not in ids:
        print("  \033[33m!\033[0m %s  不在名单里" % w)
    elif w in KNOWN_BROKEN:
        print("  \033[33m!\033[0m %s  在名单但已知不可用：%s" % (w, KNOWN_BROKEN[w]))
    else:
        print("  \033[32m✓\033[0m %s" % w)
extra = [i for i in ids if i not in want]
if extra:
    print("  ! 名单里还有其它模型（本技能未使用）: %s" % ", ".join(sorted(extra)[:12]))
PY
else
  note "跳过（网关不可达或响应为空）"
fi

echo "[5] python 依赖（keyout.py 需要）"
if python3 -c "import numpy" 2>/dev/null; then ok "numpy"; else bad "缺 numpy（pip3 install numpy）"; fi
if python3 -c "from PIL import Image" 2>/dev/null; then ok "Pillow"; else bad "缺 Pillow（pip3 install pillow）"; fi
note "python3 = $(command -v python3) ($(python3 -c 'import sys;print(".".join(map(str,sys.version_info[:3])))' 2>/dev/null))"

echo "[6] ffmpeg（仅 gen_music.sh --ogg 需要）"
FF="$(command -v ffmpeg 2>/dev/null || true)"
[ -z "$FF" ] && [ -x /opt/homebrew/bin/ffmpeg ] && FF=/opt/homebrew/bin/ffmpeg
if [ -n "$FF" ]; then ok "ffmpeg = $FF"; else note "未找到 ffmpeg —— gen_music.sh --ogg 不可用（MP3 仍可用）"; fi

echo "[7] 本地音效烘焙"
if [ -f "$ROOT/tools/bake_sfx.py" ]; then
  ok "tools/bake_sfx.py 存在（零配额，纯标准库）"
  python3 "$ROOT/tools/bake_sfx.py" --list 2>/dev/null | head -3 | sed 's/^/    /'
else
  note "未找到 tools/bake_sfx.py"
fi

echo
if [ "$FAIL" = 0 ]; then
  printf '\033[32m自检通过：可以用脚本生成素材了。\033[0m\n'
else
  printf '\033[31m自检有失败项，先修再生成（否则会白烧配额或直接报错）。\033[0m\n'
fi
echo "环境摘要: Unity $(grep -m1 m_EditorVersion "$ROOT/ProjectSettings/ProjectVersion.txt" 2>/dev/null | cut -d' ' -f2) / python3 $(python3 -c 'import sys;print(".".join(map(str,sys.version_info[:3])))' 2>/dev/null) / $(uname -sm)"
exit "$FAIL"