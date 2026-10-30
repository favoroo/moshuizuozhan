#!/usr/bin/env bash
# run_tests.sh —— P1 测试一键运行器（编译预检 + 残留运行清理 + 预设 + 简洁失败摘要）
#
# 为什么需要它：项目由多个 Agent 并行开发，直接调 `unity command test run` 有三类高发误报：
#   1. 别的 Agent 正在保存 .cs，工程处于编译失败/中间态 → 测试全红但代码其实没问题；
#   2. 上一轮运行因编译失败中断，test status 卡在 running → 新一轮永远等不到结果；
#   3. 测试结果是一大坨 JSON，失败信息埋在几万字里，人眼很难定位。
# 本脚本把这三件事包成：先验编译 → 清残留 → 轮询 → 只打印失败用例与首行原因。
#
# 用法：
#   tools/run_tests.sh              # 默认 fast：EditMode 全量 + PlayMode UI 子集
#   tools/run_tests.sh edit         # 仅 EditMode（219 条）
#   tools/run_tests.sh play         # 仅 PlayMode 全量
#   tools/run_tests.sh ui           # 仅 PlayMode UI 相关子集（HUD/触控/菜单/选枪/计时器…）
#   tools/run_tests.sh all          # EditMode + PlayMode 全量
#   UNITY_CLI=/path/to/unity tools/run_tests.sh play   # 指定 CLI
#
# 退出码：0 全过 / 1 有用例失败 / 2 编译失败 / 3 超时
set -uo pipefail

U="${UNITY_CLI:-$HOME/.unity/bin/unity}"
MODE="${1:-fast}"
RESULT_JSON="/tmp/p1_test_last.json"

# UI 相关 PlayMode 子集：改 HUD/触控/菜单/选枪/结算后先跑这个，30 秒内能拿到反馈。
# 通过测试类上的 [Category("UI")] 标记筛选（Unity 的 filter 只支持 testName/assembly/category）。
UI_CATEGORY="UI"

say() { printf '%s\n' "$*"; }

# 从 `command\tok\t{json}\tparams` 信封里取第 3 段 JSON 字段
envelope_field() {
    python3 - "$1" "$2" <<'PY'
import json, sys
raw, expr = sys.argv[1], sys.argv[2]
try:
    payload = raw.split("\t")[2]
    d = json.loads(payload)
    print(eval(expr, {"d": d}))
except Exception:
    print("unknown")
PY
}

preflight() {
    local out state failed
    for _ in $(seq 1 24); do
        out="$("$U" command script.recompile_status 2>&1 | tail -1)"
        state="$(envelope_field "$out" 'd.get("status")')"
        failed="$(envelope_field "$out" 'd.get("compilationFailed")')"
        # 等编译/域重载真正结束：期间启动 test run 会被管线直接拒绝
        if [ "$state" = "completed" ] || [ "$state" = "idle" ]; then
            if [ "$failed" = "True" ]; then
                say "✗ 编译失败：此时跑测试只会得到误报，请先修复编译错误。"
                "$U" command console.get --tail 40 --level error 2>&1 \
                    | grep -o "error CS[0-9]*[^\"]*" | sort -u | head -10
                exit 2
            fi
            say "✓ 编译干净"
            return 0
        fi
        say "… 编辑器正在重载/编译（${state}），等待 5s"
        sleep 5
    done
    say "✗ 编译状态 60s 内未就绪（先跑 unity status 确认编辑器活着）"
    exit 2
}

cancel_stale() {
    local st
    st="$("$U" command test status 2>&1 | tail -1)"
    if printf '%s' "$st" | grep -q '"status":"running"'; then
        say "! 发现上一轮残留的测试运行（多半是编译中断导致），先取消"
        "$U" command test cancel >/dev/null 2>&1
        sleep 2
    fi
}

summarize() {
    python3 - "$RESULT_JSON" <<'PY'
import json, sys
raw = open(sys.argv[1]).read().strip()
try:
    d = json.loads(raw.split("\t")[2])
except Exception:
    print("✗ 无法解析测试结果（运行可能被中断）")
    sys.exit(1)

s = d.get("summary") or {}
print(f"结果 {d.get('status')} · 耗时 {d.get('duration', 0):.1f}s · "
      f"用例 {s.get('total', 0)} · 通过 {s.get('passed', 0)} · 失败 {s.get('failed', 0)}")

failed = [r for r in (d.get("results") or []) if r.get("Status") == "Failed"]
for r in failed:
    msg = (r.get("Message") or "").strip().replace("\n", " ")
    print(f"  ✗ {r.get('FullName')}\n    {msg[:300]}")
sys.exit(1 if failed or d.get("status") != "completed" else 0)
PY
}

run_suite() { # $1=mode $2=filter(可空) $3=超时秒 $4=filter_type(可空)
    local mode="$1" filter="${2:-}" timeout_s="${3:-420}" filter_type="${4:-}"
    cancel_stale
    local args=(command test run --mode "$mode" --async_tests)
    [ -n "$filter" ] && args+=(--filter "$filter")
    [ -n "$filter_type" ] && args+=(--filter_type "$filter_type")

    say "▶ ${mode}${filter:+（过滤：${filter}）}"
    local start=$SECONDS
    local out; out="$("$U" "${args[@]}" 2>&1 | tail -1)"
    if ! printf '%s' "$out" | grep -q '"result":"running"'; then
        say "… 测试未能启动（多半是域重载边界），5s 后重试"
        sleep 5
        out="$("$U" "${args[@]}" 2>&1 | tail -1)"
        if ! printf '%s' "$out" | grep -q '"result":"running"'; then
            say "✗ 测试未能启动：$(printf '%s' "$out" | cut -c1-200)"
            return 3
        fi
    fi

    local deadline=$((SECONDS + timeout_s))
    while [ $SECONDS -lt $deadline ]; do
        sleep 5
        local st; st="$("$U" command test status 2>&1 | tail -1)"
        if ! printf '%s' "$st" | grep -q '"status":"running"'; then
            printf '%s' "$st" > "$RESULT_JSON"
            say "（真实等待 $((SECONDS - start))s）"
            summarize
            return $?
        fi
        # 卡死检测：状态 running 但编辑器早已退出 Play 模式，且持续 60s 没有结果
        if [ $((SECONDS - start)) -gt 90 ]; then
            local pm; pm="$("$U" command editor.status 2>&1 | tail -1)"
            if printf '%s' "$pm" | grep -q '"playMode":"stopped"'; then
                say "! 运行器疑似卡死（playMode=stopped 但 status=running），取消并重试一次"
                "$U" command test cancel >/dev/null 2>&1
                sleep 2
                return 4
            fi
        fi
    done

    say "✗ 超时 ${timeout_s}s，取消本次运行"
    "$U" command test cancel >/dev/null 2>&1
    return 3
}

run_with_retry() { # 卡死(4)时重试一次
    local mode="$1" filter="$2" timeout_s="$3" filter_type="${4:-}" code
    run_suite "$mode" "$filter" "$timeout_s" "$filter_type"; code=$?
    if [ $code -eq 4 ]; then
        run_suite "$mode" "$filter" "$timeout_s" "$filter_type"; code=$?
    fi
    return $code
}

preflight
rc=0

case "$MODE" in
    edit) run_with_retry EditMode "" 300; rc=$? ;;
    play) run_with_retry PlayMode "" 480; rc=$? ;;
    ui)   run_with_retry PlayMode "$UI_CATEGORY" 240 category; rc=$? ;;
    all)
        run_with_retry EditMode "" 300; rc=$?
        [ $rc -eq 0 ] && { run_with_retry PlayMode "" 480; rc=$?; }
        ;;
    fast)
        run_with_retry EditMode "" 300; rc=$?
        [ $rc -eq 0 ] && { run_with_retry PlayMode "$UI_CATEGORY" 240 category; rc=$?; }
        ;;
    *)
        say "未知预设：${MODE}（可用：fast | edit | play | ui | all）"
        exit 64
        ;;
esac

[ $rc -eq 0 ] && say "✓ 全部通过" || say "✗ 存在失败用例（详见上方 ✗ 行）"
exit $rc
