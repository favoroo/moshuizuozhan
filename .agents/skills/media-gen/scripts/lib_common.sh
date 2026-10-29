# lib_common.sh —— gen_*.sh 公共库：路径解析、配置加载、manifest 登记（Unity 版）
# 由 gen_*.sh source，勿直接执行。
# 注意：本机 shell 常见 ALL_PROXY/HTTP_PROXY 环境变量指向失效代理，
#       所有 curl 必须带 --noproxy '*'，否则请求会走代理而失败（HTTP 000）。

LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_DIR="$LIB_DIR"
# scripts -> media-gen -> skills -> .agents -> 项目根（共 4 级）
ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"

# Unity 工程判定：ProjectSettings/ProjectVersion.txt 是每个 Unity 工程必然存在的文件。
# （Godot 版判据是 project.godot；换成 Unity 判据是移植时唯一必须改的一处。）
if [ ! -f "$ROOT/ProjectSettings/ProjectVersion.txt" ]; then
  echo "警告: 未在 $ROOT 找到 ProjectSettings/ProjectVersion.txt，manifest/素材目录定位可能不准" >&2
fi

# 生成原稿区：放在 Assets/ 之外，Unity 只扫描 Assets/ 下的内容，
# 所以根目录天然不被导入，无需 Godot 版 assets_raw/.gdignore 那种排除标记。
RAW="$ROOT/MediaRaw"
mkdir -p "$RAW/images" "$RAW/voice" "$RAW/music" "$RAW/style"

# 成品入库区（Assets/_Project 下由 MediaImportPostprocessor.cs 按路径自动套导入设置）
ASSETS="$ROOT/Assets/_Project"

API_ENV="$SCRIPT_DIR/../config/api.env"
[ -f "$API_ENV" ] || { echo "缺少配置文件: $API_ENV" >&2; exit 1; }
# shellcheck disable=SC1090
source "$API_ENV"
[ -n "${MEDIA_API_BASE:-}" ] && [ -n "${MEDIA_API_KEY:-}" ] || {
  echo "config/api.env 中缺少 MEDIA_API_BASE / MEDIA_API_KEY" >&2; exit 1
}

# manifest_append <kind> <model> <prompt> <refs用|分隔> <最终文件路径>
# 逐行追加 JSON 到 MediaRaw/manifest.jsonl，供后续 agent 检索复用、避免重复生成
manifest_append() {
  python3 - "$RAW/manifest.jsonl" "$1" "$2" "$3" "$4" "$5" "$ROOT" <<'PY'
import json, sys, datetime, os
p, kind, model, prompt, refs, f, root = sys.argv[1:8]
rec = {
    "ts": datetime.datetime.now().isoformat(timespec="seconds"),
    "kind": kind, "model": model, "prompt": prompt[:800],
    "refs": [r for r in refs.split("|") if r],
    "output": os.path.relpath(os.path.abspath(f), root) if f else "",
}
os.makedirs(os.path.dirname(p), exist_ok=True)
with open(p, "a", encoding="utf-8") as fh:
    fh.write(json.dumps(rec, ensure_ascii=False) + "\n")
PY
}