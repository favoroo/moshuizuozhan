#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# P1 涂地对战 一键发布脚本（两阶段）：build（Unity APK 导出+打包+SHA-256）/ publish（推送+双端 Release+验证+清理老附件）
# 独立 prune 子命令：双端各保留最近 N 个版本的 APK 附件，防止超出 Gitee 配额
# 仅用 Python 标准库，无第三方依赖。用法见 SKILL.md，或 python3 release.py --help

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
VERSION_CS = REPO_ROOT / 'Assets' / '_Project' / 'Scripts' / 'Core' / 'GameVersion.cs'
BUILD_DIR = REPO_ROOT / 'build'
META_FILE = BUILD_DIR / 'release-meta.json'

# 发布说明守则（与 SKILL.md §3 同源）：只写玩家能感知的变化
NOTES_MAX_ITEMS = 5
NOTES_MAX_CHARS = 25
NOTES_ALLOWED_SECTIONS = ('新增内容', '调整', '问题修复')
NOTES_BANNED_TERMS = (
    '池化', '对象池', '节流', '信号', '节点', 'await', '渲染器', '工厂',
    '口径', '探针', 'HUD', '合批', '分帧', '重构', '缓存', '日志', '接口',
    '测试', '站点',
)

UNITY_CLI = os.environ.get('UNITY_CLI', os.path.expanduser('~/.unity/bin/unity'))
UNITY_EDITOR_BIN = os.environ.get('UNITY_EDITOR_BIN', '/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity')
ANDROID_BUILD_MENU = 'P1/构建/9. 构建 Android APK (ARM64 + Vulkan/GLES3) (P6-C)'
MIN_APK_BYTES = 5 * 1024 * 1024

GITHUB_OWNER, GITHUB_REPO = 'favoroo', 'moshuizuozhan'
GITEE_OWNER, GITEE_REPO = 'favo9', 'moshuizuozhan'
GITHUB_API = f'https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}'
GITEE_API = f'https://gitee.com/api/v5/repos/{GITEE_OWNER}/{GITEE_REPO}'

T0 = time.time()


def log(msg: str) -> None:
    print(f'[{time.time() - T0:7.1f}s] {msg}', flush=True)


def die(msg: str, code: int = 1) -> None:
    print(f'✗ 错误: {msg}', file=sys.stderr, flush=True)
    sys.exit(code)


def run(cmd: list, **kw) -> subprocess.CompletedProcess:
    kw.setdefault('cwd', REPO_ROOT)
    kw.setdefault('capture_output', True)
    kw.setdefault('text', True)
    return subprocess.run(cmd, **kw)


# ---------------------------------------------------------------- 通用 HTTP

def http_json(url: str, method: str = 'GET', headers: dict = None,
              data=None, timeout: int = 30):
    """返回 (status, 解析后的JSON或None)。HTTPError 不抛出，返回错误码与响应体。"""
    req = urllib.request.Request(url, method=method,
                                 headers=headers or {}, data=data)
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                body = r.read()
                return r.status, (json.loads(body) if body else None)
        except urllib.error.HTTPError as e:
            body = e.read()
            try:
                return e.code, (json.loads(body) if body else None)
            except Exception:
                return e.code, None
        except Exception:
            if attempt == 2:
                raise
            time.sleep(1 + attempt)


def multipart_body(fields: dict, file_field: str, filename: str,
                   file_bytes: bytes) -> tuple:
    """构造 multipart/form-data 请求体，返回 (body, content_type)。"""
    boundary = uuid.uuid4().hex
    parts = []
    for k, v in fields.items():
        parts.append(
            f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"'
            f'\r\n\r\n{v}\r\n'.encode('utf-8'))
    parts.append(
        f'--{boundary}\r\nContent-Disposition: form-data; name="{file_field}"'
        f'; filename="{filename}"\r\n'
        f'Content-Type: application/octet-stream\r\n\r\n'.encode('utf-8'))
    parts.append(file_bytes)
    parts.append(f'\r\n--{boundary}--\r\n'.encode('utf-8'))
    return b''.join(parts), f'multipart/form-data; boundary={boundary}'


# ---------------------------------------------------------------- 令牌获取

def git_credential_fill(host: str) -> str | None:
    """从 git 凭据库取 host 的 password；取不到返回 None（禁止交互）。"""
    env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GIT_ASKPASS='echo')
    r = subprocess.run(['git', 'credential', 'fill'],
                       input=f'protocol=https\nhost={host}\n\n',
                       capture_output=True, text=True, env=env, timeout=15)
    m = re.search(r'^password=(.+)$', r.stdout, re.M)
    return m.group(1).strip() if m else None


def get_github_token() -> str:
    token = os.environ.get('GITHUB_TOKEN') or os.environ.get('GH_TOKEN')
    if token:
        log('GitHub 令牌来源: 环境变量')
        return token.strip()
    token = git_credential_fill('github.com')
    if token:
        log('GitHub 令牌来源: git 凭据库')
        return token
    die('拿不到 GitHub 令牌。请: 1) export GITHUB_TOKEN=<PAT> 或 '
        '2) git push 一次让凭据入库(osxkeychain)。')


def get_gitee_token() -> str:
    token = os.environ.get('GITEE_TOKEN')
    if token:
        log('Gitee 令牌来源: 环境变量')
        return token.strip()
    token = git_credential_fill('gitee.com')
    if token:
        log('Gitee 令牌来源: git 凭据库')
        return token
    die('拿不到 Gitee 令牌。请: 1) export GITEE_TOKEN=<私人令牌> 或 '
        '2) git push 一次让凭据入库。')


# ---------------------------------------------------------------- 版本解析与递增

def read_current_version() -> str:
    if not VERSION_CS.exists():
        die(f'未找到版本文件: {VERSION_CS}')
    content = VERSION_CS.read_text(encoding='utf-8')
    m = re.search(r'public\s+const\s+string\s+APP_VERSION\s*=\s*"([^"]+)";', content)
    if not m:
        die(f'无法从 {VERSION_CS} 中提取 APP_VERSION')
    return m.group(1).strip()


def bump_version_string(ver: str) -> str:
    parts = ver.split('.')
    if len(parts) != 3 or not all(p.isdigit() for p in parts):
        die(f'非法语义化版本号: {ver}（期望 X.Y.Z 格式）')
    x, y, z = (int(p) for p in parts)
    return f'{x}.{y}.{z + 1}'


def write_version(new_ver: str) -> None:
    content = VERSION_CS.read_text(encoding='utf-8')
    new_content = re.sub(
        r'(public\s+const\s+string\s+APP_VERSION\s*=\s*)"[^"]+";',
        f'\\g<1>"{new_ver}";',
        content
    )
    VERSION_CS.write_text(new_content, encoding='utf-8')
    log(f'已写回 {VERSION_CS.name} -> {new_ver}')


def cmd_bump(args) -> None:
    cur = read_current_version()
    if args.version:
        target = args.version.lstrip('vV')
    else:
        # 查询当前 tag 是否已存在
        r = run(['git', 'tag', '-l', f'v{cur}'])
        if r.stdout.strip() == f'v{cur}':
            target = bump_version_string(cur)
            log(f'检测到 tag v{cur} 已存在，版本自动递增: {cur} -> {target}')
        else:
            target = cur
            log(f'tag v{cur} 尚未发布，保持当前版本: {target}')

    if args.write:
        write_version(target)
    print(target)


# ---------------------------------------------------------------- SHA-256 与文件工具

def sha256_file(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        while chunk := f.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest()


def write_meta(meta: dict) -> None:
    BUILD_DIR.mkdir(exist_ok=True)
    tmp = META_FILE.with_suffix('.tmp')
    tmp.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(tmp, META_FILE)


# ---------------------------------------------------------------- APK 身份校验

def expected_version_code(ver: str) -> int:
    parts = ver.split('.')
    if len(parts) != 3 or not all(p.isdigit() for p in parts):
        die(f'非法语义化版本号: {ver}（期望 X.Y.Z 格式）')
    x, y, z = (int(p) for p in parts)
    return x * 10000 + y * 100 + z


def find_android_tool() -> str | None:
    """定位 aapt2 / aapt，用于读取 APK 包内 versionName / versionCode。"""
    roots = [os.environ.get('ANDROID_HOME'), os.environ.get('ANDROID_SDK_ROOT'),
             os.path.expanduser('~/Library/Android/sdk'),
             os.path.expanduser('~/Android/Sdk')]
    for root in roots:
        if not root:
            continue
        bt = Path(root) / 'build-tools'
        if not bt.is_dir():
            continue
        versions = sorted(bt.iterdir(), key=lambda p: _ver_key(p.name), reverse=True)
        for v in versions:
            for name in ('aapt2', 'aapt'):
                cand = v / name
                if cand.is_file() and os.access(cand, os.X_OK):
                    return str(cand)
    for name in ('aapt2', 'aapt'):
        found = shutil.which(name)
        if found:
            return found
    return None


def read_apk_version(apk_path: Path) -> tuple[str, int] | None:
    """读取 APK 包内 versionName / versionCode；无 Android 工具链时返回 None。"""
    tool = find_android_tool()
    if not tool:
        return None
    cmd = [tool, 'dump', 'badging', str(apk_path)]
    r = run(cmd)
    if r.returncode != 0:
        return None
    m_name = re.search(r"versionName='([^']*)'", r.stdout)
    m_code = re.search(r"versionCode='(\d+)'", r.stdout)
    if not m_name or not m_code:
        return None
    return m_name.group(1).strip(), int(m_code.group(1))


def newest_source_mtime() -> float:
    """仓库内受版本控制的最新源码/配置修改时间，用于无 aapt 时判断产物是否陈旧。"""
    r = run(['git', 'ls-files', '-z'])
    newest = 0.0
    for rel in r.stdout.split('\0'):
        if not rel:
            continue
        p = REPO_ROOT / rel
        try:
            newest = max(newest, p.stat().st_mtime)
        except OSError:
            continue
    return newest


def verify_apk_identity(apk_path: Path, target_ver: str) -> None:
    """确认 APK 是目标版本，防止把旧包当新版本发出去。"""
    if not apk_path.exists():
        die(f'APK 不存在: {apk_path}')
    if apk_path.stat().st_size <= MIN_APK_BYTES:
        die(f'APK 体积异常（可能构建失败）: {apk_path.stat().st_size} 字节')

    identity = read_apk_version(apk_path)
    if identity is None:
        # 没有 Android 工具链：退化为时间戳校验，并明确告知风险
        src_mtime = newest_source_mtime()
        if src_mtime and apk_path.stat().st_mtime + 1 < src_mtime:
            die(f'APK 比最新源码还旧，疑似陈旧产物: {apk_path}\n'
                f'  请重新构建，或安装 Android build-tools 以便精确校验版本号。')
        log('⚠ 未找到 aapt2/aapt，按时间戳判断 APK 新鲜度（建议安装 Android build-tools）')
        return

    ver_name, ver_code = identity
    want_code = expected_version_code(target_ver)
    if ver_name != target_ver or ver_code != want_code:
        die(f'APK 版本不符: 包内 {ver_name} ({ver_code})，期望 {target_ver} ({want_code})\n'
            f'  产物: {apk_path}\n'
            f'  这说明复用了旧包。请重新构建，或确认 GameVersion.cs / PlayerSettings 是否已同步。')
    log(f'APK 版本校验通过: {ver_name} (versionCode {ver_code})')


def editor_is_running() -> bool:
    """GUI 编辑器是否正在占用工程锁。"""
    if not (REPO_ROOT / 'Temp' / 'UnityLockfile').exists():
        return False
    try:
        r = run([UNITY_CLI, 'status'], timeout=20)
    except (subprocess.TimeoutExpired, FileNotFoundError):
        return False
    out = (r.stdout or '') + (r.stderr or '')
    return 'ready' in out and str(REPO_ROOT) in out


def wait_for_editor_build(log_path: Path, log_offset: int, timeout: int = 1200) -> bool:
    """轮询编辑器日志的新增内容，等待菜单构建结束。

    不能用 APK 的修改时间判断：Unity 增量构建在无改动时会直接复用缓存，
    只把上一次的 APK 原样保留（mtime 不变）并仍然报告构建成功。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        if log_path.exists():
            try:
                with open(log_path, 'rb') as f:
                    f.seek(log_offset)
                    tail = f.read().decode('utf-8', errors='ignore')
            except OSError:
                tail = ''
            if 'Android 构建失败' in tail or 'Android build failed' in tail:
                log('编辑器日志显示构建失败')
                return False
            if '[P6-C][Build] Android APK 构建成功' in tail:
                return True
        time.sleep(2)
    return False


# ---------------------------------------------------------------- build 阶段

def pick_verified_apk(candidates: list, target_ver: str, stage_label: str) -> Path | None:
    """从候选产物中挑出包内版本与目标一致的 APK；固定名旧包会被跳过。"""
    for cand in candidates:
        if not cand.exists() or cand.stat().st_size <= MIN_APK_BYTES:
            continue
        identity = read_apk_version(cand)
        if identity is None:
            # 无 Android 工具链时只能信任带目标版本号的文件名
            if target_ver in cand.name:
                log(f'复用候选（{stage_label}，无 aapt 校验，文件名含目标版本）: {cand}')
                return cand
            log(f'跳过无法校验版本的候选 {cand.name}（缺少 aapt2/aapt）')
            continue
        ver_name, ver_code = identity
        if ver_name == target_ver and ver_code == expected_version_code(target_ver):
            log(f'候选版本校验通过（{stage_label}）: {cand} -> {ver_name} ({ver_code})')
            return cand
        log(f'跳过版本不符的候选（{stage_label}）: {cand.name} '
            f'实际 {ver_name} ({ver_code})，目标 {target_ver} ({expected_version_code(target_ver)})')
    return None


def cmd_build(args) -> None:
    cur_ver = read_current_version()
    target_ver = args.version.lstrip('vV')
    if cur_ver != target_ver:
        die(f'GameVersion.cs ({cur_ver}) 与 --version ({args.version}) 不一致，请先运行 bump')

    apk_name = f'moshuizuozhan-v{target_ver}-arm64-v8a.apk'
    BUILD_DIR.mkdir(exist_ok=True)
    dst = BUILD_DIR / apk_name

    log(f'开始构建 Release APK ({apk_name})...')

    # 候选产物：版本命名产物优先；Unity 固定输出名只有通过包内版本校验才可复用。
    candidates = [
        dst,
        REPO_ROOT / 'Builds' / 'Android' / apk_name,
        REPO_ROOT / 'Builds' / 'Android' / 'InkTurfDemo.apk',
        REPO_ROOT / 'Builds' / 'Android' / 'moshuizuozhan.apk'
    ]

    def pick_verified(stage_label: str) -> Path | None:
        return pick_verified_apk(candidates, target_ver, stage_label)

    found_apk = None if args.force else pick_verified('已有产物')

    if found_apk is None:
        build_log = REPO_ROOT / 'Logs' / 'build_android_release.log'

        if editor_is_running() and not args.batchmode:
            # GUI 编辑器持有工程锁，batchmode 必然启动失败；改走 Pipeline 触发同一构建方法
            log('检测到 GUI 编辑器正在运行，改用 Pipeline 菜单构建（避开工程锁）...')
            log(f'菜单: {ANDROID_BUILD_MENU}')
            editor_log = REPO_ROOT / 'Logs' / 'Editor.log'
            log_offset = editor_log.stat().st_size if editor_log.exists() else 0
            try:
                r = run([UNITY_CLI, 'command', '--timeout', str(args.build_timeout),
                         'editor', 'invoke_menu', '--path', ANDROID_BUILD_MENU],
                        timeout=args.build_timeout + 60)
                out = (r.stdout or '') + (r.stderr or '')
                if 'No command named' in out or 'Command Not Found' in out:
                    die(f'Pipeline 不可用，无法在编辑器内构建:\n{out[-1000:]}')
                if r.returncode != 0 or 'Executed menu item' not in out:
                    log(f'Pipeline 调用返回异常，继续等待编辑器日志:\n{out[-1000:]}')
            except subprocess.TimeoutExpired:
                log('Pipeline 调用超时，继续等待编辑器日志...')
            if not wait_for_editor_build(editor_log, log_offset, timeout=args.build_timeout):
                die('编辑器内构建未在超时内完成。\n'
                    f'  请查看 {editor_log}，或手动执行菜单 {ANDROID_BUILD_MENU} 后重跑 build。')
        else:
            log('调用 Unity CLI 以 batchmode 构建 Android APK...')
            cmd = [
                UNITY_CLI, 'build',
                '--target', 'Android',
                '--execute-method', 'P1.Editor.BuildAndProfilerSuite.BuildAndroid',
                '--output-path', str(dst),
                '--allow-dirty-build',
                '--no-tail',
                '-l', str(build_log)
            ]
            log(f'执行命令: {" ".join(cmd)}')
            r = run(cmd, timeout=args.build_timeout + 120)
            if r.returncode != 0:
                tail = ''
                if build_log.exists():
                    tail = build_log.read_text(encoding='utf-8', errors='ignore')[-1500:]
                hint = ''
                if 'ExitDontLaunchBugReporter' in tail:
                    hint = ('\n  日志显示工程锁冲突：GUI 编辑器正在占用工程。'
                            '关闭编辑器，或让 build 自动走 Pipeline（去掉 --batchmode）。')
                die(f'unity build 失败(return {r.returncode})。{hint}\n日志尾部:\n{tail}')

        found_apk = pick_verified('本次构建')

    if found_apk is None:
        die('未找到与目标版本一致的 APK 产物。\n'
            f'  期望: {target_ver} (versionCode {expected_version_code(target_ver)})\n'
            '  请检查构建日志，或确认 GameVersion.cs / ApplyAndroidPlayerSettings 是否同步。')

    if found_apk != dst:
        log(f'拷贝 APK 产物: {found_apk} -> {dst}')
        shutil.copy2(found_apk, dst)

    verify_apk_identity(dst, target_ver)

    digest = sha256_file(dst)
    head = run(['git', 'rev-parse', 'HEAD']).stdout.strip()
    dirty = bool(run(['git', 'status', '--porcelain']).stdout.strip())
    meta = {
        'status': 'success',
        'version': target_ver,
        'version_code': expected_version_code(target_ver),
        'tag': f'v{target_ver}',
        'apk_name': apk_name,
        'apk_path': str(dst),
        'sha256': digest,
        'size': dst.stat().st_size,
        'timestamp': int(time.time()),
        'git_head': head,
        'git_dirty': dirty,
    }
    write_meta(meta)
    log(f'构建完成: {apk_name} ({dst.stat().st_size} 字节, {dst.stat().st_size / (1024 * 1024):.1f} MB)')
    log(f'SHA-256: {digest}')
    log(f'构建来源: {head[:8]}{" (工作区有未提交改动)" if dirty else ""}')


# ---------------------------------------------------------------- publish 阶段

def validate_notes(notes: str) -> None:
    raw_lines = [ln.strip() for ln in notes.splitlines() if ln.strip()]

    # 脚本会自动追加 **SHA-256** 行；它和 markdown 水平分割线都不算发布条目
    lines = [
        ln for ln in raw_lines
        if not re.match(r'^\*\*SHA-256', ln, re.I) and not re.fullmatch(r'-{3,}', ln)
    ]

    for ln in lines:
        if ln.startswith('### '):
            section = ln[4:].strip()
            if section not in NOTES_ALLOWED_SECTIONS:
                die(f'Release 说明出现不允许的分段「{section}」，'
                    f'只允许: {" / ".join(NOTES_ALLOWED_SECTIONS)}')

    # 条目必须形如 "- 文本" / "* 文本"（排除 **加粗** 行）
    items = [ln for ln in lines if re.match(r'^[-*]\s+\S', ln)]

    if not (1 <= len(items) <= NOTES_MAX_ITEMS):
        die(f'Release 说明条数不合规: 当前 {len(items)} 条（要求 1~{NOTES_MAX_ITEMS} 条）')

    for it in items:
        clean = re.sub(r'^[-*]\s+', '', it)
        if len(clean) > NOTES_MAX_CHARS:
            die(f'Release 说明单条超长（>{NOTES_MAX_CHARS}字）: "{clean}"')
        for banned in NOTES_BANNED_TERMS:
            if banned in clean:
                die(f'Release 说明包含开发者视角禁用词 "{banned}": "{clean}"')


def wait_for_build(target_ver: str, timeout: int = 300) -> dict:
    log(f'等待构建元数据 {META_FILE.name}（最长 {timeout}s）...')
    deadline = time.time() + timeout
    while time.time() < deadline:
        if META_FILE.exists():
            try:
                meta = json.loads(META_FILE.read_text(encoding='utf-8'))
                if meta.get('status') == 'failed':
                    die(f"构建阶段已失败: {meta.get('error', '')[:1500]}")
                if meta.get('status') == 'success':
                    if meta.get('version') != target_ver:
                        die(f"元数据版本 {meta.get('version')} 与目标版本 {target_ver} 不一致")
                    log(f"构建产物就绪: {meta['apk_path']}")
                    return meta
            except json.JSONDecodeError:
                pass
        time.sleep(2)
    die(f'等待构建超时（{timeout}s）。检查 build 进程是否完成。')


def verify_project_settings(target_ver: str) -> None:
    """AGENTS.md 规则 9：GameVersion.APP_VERSION 必须与 PlayerSettings.bundleVersion 一致。"""
    ps_path = REPO_ROOT / 'ProjectSettings' / 'ProjectSettings.asset'
    if not ps_path.exists():
        die(f'未找到 {ps_path}')
    text = ps_path.read_text(encoding='utf-8')
    m_ver = re.search(r'^\s*bundleVersion:\s*(\S+)\s*$', text, re.M)
    m_code = re.search(r'^\s*AndroidBundleVersionCode:\s*(\d+)\s*$', text, re.M)
    want_code = expected_version_code(target_ver)
    if not m_ver or m_ver.group(1) != target_ver:
        die(f'PlayerSettings.bundleVersion ({m_ver.group(1) if m_ver else "缺失"}) '
            f'与目标版本 {target_ver} 不一致，请先执行 ApplyAndroidPlayerSettings')
    if not m_code or int(m_code.group(1)) != want_code:
        die(f'PlayerSettings.AndroidBundleVersionCode ({m_code.group(1) if m_code else "缺失"}) '
            f'与目标 {want_code} 不一致，请先执行 ApplyAndroidPlayerSettings')
    log(f'PlayerSettings 版本校验通过: {m_ver.group(1)} ({m_code.group(1)})')


def preflight(tag: str, tokens: dict) -> None:
    r = run(['git', 'status', '--porcelain'])
    if r.stdout.strip():
        lines = [ln for ln in r.stdout.strip().splitlines() if 'release-meta.json' not in ln and '.tmp' not in ln]
        if lines:
            die('工作树不干净，先提交全部改动再 publish:\n' + '\n'.join(lines[:10]))

    head = run(['git', 'rev-parse', 'HEAD']).stdout.strip()
    r = run(['git', 'rev-parse', '--verify', f'refs/tags/{tag}'])
    if r.returncode == 0:
        tag_commit = run(['git', 'rev-parse', f'{tag}^{{commit}}']).stdout.strip()
        if tag_commit != head:
            die(f'tag {tag} 已存在但指向 {tag_commit[:8]}，与当前 HEAD {head[:8]} 不一致。\n'
                f'  禁止把新代码的包发到旧 tag 上；请递增版本号后重新发布。')
        st_gh, d_gh = http_json(f'{GITHUB_API}/releases/tags/{tag}', headers=gh_headers(tokens['gh']))
        st_gt, d_gt = http_json(f'{GITEE_API}/releases/tags/{tag}?access_token={tokens["gitee"]}')
        gh_apks = [a for a in (d_gh.get('assets', []) if st_gh == 200 and d_gh else []) if str(a.get('name', '')).endswith('.apk')]
        gt_apks = [a for a in (d_gt.get('assets', []) if st_gt == 200 and d_gt else []) if str(a.get('name', '')).endswith('.apk')]
        if gh_apks and gt_apks:
            die(f'tag {tag} 已在双端完整发布过 Release 与 APK，禁止同版本覆盖发布。请先运行 bump 递增版本号。')
        log(f'本地 tag {tag} 已存在且与 HEAD 一致，远端 Release/APK 尚未全部就绪，将复用该 tag（重试场景）')
    else:
        log(f'创建本地 annotated tag: {tag}')
        r = run(['git', 'tag', '-a', tag, '-m', f'Moshuizuozhan {tag}'])
        if r.returncode != 0:
            die(f'创建 tag 失败: {r.stderr}')


def current_branch() -> str:
    r = run(['git', 'rev-parse', '--abbrev-ref', 'HEAD'])
    return r.stdout.strip() or 'main'


def git_push(remote: str, branch: str, tag: str) -> None:
    r = run(['git', 'push', remote, branch, tag])
    if r.returncode != 0:
        if 'already exists' in r.stderr or 'up-to-date' in r.stderr:
            log(f'git push {remote}: {branch} / {tag} 已同步，视为成功')
            return
        raise RuntimeError(f'git push {remote} 失败: {r.stderr[-500:]}')


def push_both(branch: str, tag: str) -> None:
    log('并行推送分支+标签到 origin(GitHub) / gitee(Gitee)...')
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
        futs = {ex.submit(git_push, rm, branch, tag): rm for rm in ('origin', 'gitee')}
        for f in concurrent.futures.as_completed(futs):
            f.result()
            log(f'git push {futs[f]} 完成 ({branch} + {tag})')


def gh_headers(token: str) -> dict:
    return {
        'Authorization': f'token {token}',
        'User-Agent': 'moshuizuozhan-release-script',
        'Accept': 'application/vnd.github+json'
    }


def gh_release_id_by_tag(tag: str, token: str) -> int | None:
    st, d = http_json(f'{GITHUB_API}/releases/tags/{tag}', headers=gh_headers(token))
    return d['id'] if st == 200 and d else None


def gitee_release_id_by_tag(tag: str, token: str) -> int | None:
    url = f'{GITEE_API}/releases/tags/{tag}?{urllib.parse.urlencode({"access_token": token})}'
    st, d = http_json(url)
    return d['id'] if st == 200 and d else None


def create_release(tag: str, name: str, notes: str, tokens: dict) -> dict:
    gh_id = gh_release_id_by_tag(tag, tokens['gh'])
    gitee_id = gitee_release_id_by_tag(tag, tokens['gitee'])
    results = {}

    def create_gh():
        if gh_id:
            results['gh'] = gh_id
            log('GitHub Release 已存在，跳过创建（幂等复用）')
            return
        payload = json.dumps({
            'tag_name': tag,
            'name': name,
            'body': notes,
            'draft': False,
            'prerelease': False
        }).encode('utf-8')
        st, d = http_json(f'{GITHUB_API}/releases', 'POST',
                          headers={**gh_headers(tokens['gh']), 'Content-Type': 'application/json'},
                          data=payload)
        if st != 201 or not d:
            raise RuntimeError(f'GitHub 创建 Release 失败({st}): {d}')
        results['gh'] = d['id']
        log(f'GitHub Release 创建成功 (id={d["id"]})')

    def create_gitee():
        if gitee_id:
            results['gitee'] = gitee_id
            log('Gitee Release 已存在，跳过创建（幂等复用）')
            return
        data = urllib.parse.urlencode({
            'tag_name': tag,
            'name': name,
            'body': notes,
            'prerelease': 'false',
            'target_commitish': 'main'
        }).encode('utf-8')
        st, d = http_json(f'{GITEE_API}/releases', 'POST',
                          headers={'Authorization': f"token {tokens['gitee']}",
                                   'Content-Type': 'application/x-www-form-urlencoded'},
                          data=data)
        if st not in (200, 201) or not d:
            raise RuntimeError(f'Gitee 创建 Release 失败({st}): {d}')
        results['gitee'] = d['id']
        log(f'Gitee Release 创建成功 (id={d["id"]})')

    log('并行创建双端 Release...')
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
        f1, f2 = ex.submit(create_gh), ex.submit(create_gitee)
        f1.result()
        f2.result()
    return results


def upload_apks(release_ids: dict, apk_path: Path, apk_name: str, tokens: dict) -> None:
    file_bytes = apk_path.read_bytes()

    def upload_gh():
        # 预先检查是否有旧同名附件
        st_chk, d_rel = http_json(f'{GITHUB_API}/releases/{release_ids["gh"]}', headers=gh_headers(tokens['gh']))
        if st_chk == 200 and d_rel:
            for item in d_rel.get('assets', []):
                if item.get('name') == apk_name:
                    if item.get('size') == len(file_bytes):
                        log(f'GitHub 附件已存在且体积一致 ({len(file_bytes)} 字节)，跳过上传')
                        return
                    else:
                        log(f'GitHub 存在旧附件 ({item.get("size")} 字节)，正在删除替换...')
                        http_json(f'{GITHUB_API}/releases/assets/{item["id"]}', method='DELETE', headers=gh_headers(tokens['gh']))
                        break

        st, d = http_json(
            f'https://uploads.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}'
            f'/releases/{release_ids["gh"]}/assets?'
            f'{urllib.parse.urlencode({"name": apk_name})}',
            'POST',
            headers={**gh_headers(tokens['gh']), 'Content-Type': 'application/octet-stream'},
            data=file_bytes, timeout=600)
        if st != 201 or not d:
            raise RuntimeError(f'GitHub 上传 APK 失败({st}): {d}')
        log(f"GitHub 上传完成: {d['name']} ({d['size']} 字节)")

    def upload_gitee():
        # 先查已挂附件：同名同体积跳过；同名不同体积删除后重传
        st_list, d_list = http_json(
            f'{GITEE_API}/releases/{release_ids["gitee"]}/attach_files'
            f'?access_token={tokens["gitee"]}')
        if st_list == 200 and isinstance(d_list, list):
            for item in d_list:
                if item.get('name') != apk_name:
                    continue
                if item.get('size') == len(file_bytes):
                    log(f'Gitee 附件已存在且体积一致 ({len(file_bytes)} 字节)，跳过上传')
                    return
                log(f'Gitee 存在同名旧附件 ({item.get("size")} 字节)，删除后重传...')
                http_json(f'{GITEE_API}/releases/{release_ids["gitee"]}/attach_files/{item["id"]}'
                          f'?access_token={tokens["gitee"]}', method='DELETE')
                break

        body, ctype = multipart_body({'name': apk_name}, 'file', apk_name, file_bytes)
        st, d = http_json(
            f'{GITEE_API}/releases/{release_ids["gitee"]}/attach_files',
            'POST', headers={'Authorization': f"token {tokens['gitee']}",
                             'Content-Type': ctype},
            data=body, timeout=600)
        if st not in (200, 201) or not d:
            if st == 400 and 'already exists' in str(d):
                log('Gitee 附件已存在（接口返回），跳过重复上传')
                return
            raise RuntimeError(f'Gitee 上传 APK 失败({st}): {d}')
        log(f"Gitee 上传完成: {apk_name}")

    log(f'并行上传 APK ({apk_name}, {len(file_bytes)} 字节)...')
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
        f1, f2 = ex.submit(upload_gh), ex.submit(upload_gitee)
        f1.result()
        f2.result()


def verify_releases(tag: str, apk_name: str, tokens: dict, expected_size: int | None = None) -> None:
    log('开始发布后双端验证...')
    st_gh, d_gh = http_json(f'{GITHUB_API}/releases/tags/{tag}', headers=gh_headers(tokens['gh']))
    if st_gh != 200 or not d_gh:
        die(f'GitHub 验证失败({st_gh})')
    gh_asset = None
    for a in d_gh.get('assets', []):
        if a.get('name') == apk_name:
            gh_asset = a
            break
    if gh_asset is None:
        die(f'GitHub Release 缺少附件 {apk_name}，现有: {[a.get("name") for a in d_gh.get("assets", [])]}')
    if gh_asset.get('state') != 'uploaded':
        die(f'GitHub 附件状态异常: {gh_asset.get("state")}')
    if expected_size and gh_asset.get('size') != expected_size:
        die(f'GitHub 附件体积不符: {gh_asset.get("size")} != {expected_size}')
    log(f'✓ GitHub Release 校验通过: {d_gh.get("html_url")}')

    st_gt, d_gt = http_json(f'{GITEE_API}/releases/tags/{tag}?access_token={tokens["gitee"]}')
    if st_gt != 200 or not d_gt:
        die(f'Gitee 验证失败({st_gt})')
    # Gitee Release 对象不含附件体积，需查 attach_files
    st_list, d_list = http_json(
        f'{GITEE_API}/releases/{d_gt["id"]}/attach_files?access_token={tokens["gitee"]}')
    gt_asset = None
    if st_list == 200 and isinstance(d_list, list):
        for a in d_list:
            if a.get('name') == apk_name:
                gt_asset = a
                break
    if gt_asset is None:
        die(f'Gitee Release 缺少附件 {apk_name}，现有: '
            f'{[a.get("name") for a in (d_list or [])]}')
    if expected_size and gt_asset.get('size') != expected_size:
        die(f'Gitee 附件体积不符: {gt_asset.get("size")} != {expected_size}')
    log(f'✓ Gitee Release 校验通过: '
        f'https://gitee.com/{GITEE_OWNER}/{GITEE_REPO}/releases/tag/{tag}')


def _ver_key(tag: str):
    nums = re.findall(r'\d+', tag or '')
    return tuple(int(n) for n in nums) or (0,)


def prune_old_assets(tokens: dict, keep_gh: int = 10, keep_gitee: int = 3) -> None:
    log(f'开始清理老版本 APK 附件（GitHub 保留最近 {keep_gh} 版，Gitee 保留最近 {keep_gitee} 版）...')
    # GitHub prune
    try:
        st, rels = http_json(f'{GITHUB_API}/releases?per_page=100', headers=gh_headers(tokens['gh']))
        if st == 200 and isinstance(rels, list):
            apk_releases = [r for r in rels if any(str(a.get('name', '')).endswith('.apk') for a in r.get('assets', []))]
            apk_releases.sort(key=lambda r: _ver_key(r.get('tag_name', '')), reverse=True)
            for old_rel in apk_releases[keep_gh:]:
                for asset in old_rel.get('assets', []):
                    if str(asset.get('name', '')).endswith('.apk'):
                        log(f'GitHub 删除老版本 {old_rel.get("tag_name")} 附件: {asset.get("name")}')
                        http_json(f'{GITHUB_API}/releases/assets/{asset["id"]}', method='DELETE', headers=gh_headers(tokens['gh']))
    except Exception as ex:
        log(f'GitHub 老版本附件清理跳过: {ex}')

    # Gitee prune
    try:
        st, rels = http_json(f'{GITEE_API}/releases?access_token={tokens["gitee"]}&per_page=100')
        if st == 200 and isinstance(rels, list):
            apk_releases = [r for r in rels if any(str(a.get('name', '')).endswith('.apk') for a in r.get('assets', []))]
            apk_releases.sort(key=lambda r: _ver_key(r.get('tag_name', '')), reverse=True)
            for old_rel in apk_releases[keep_gitee:]:
                for asset in old_rel.get('assets', []):
                    if str(asset.get('name', '')).endswith('.apk'):
                        log(f'Gitee 删除老版本 {old_rel.get("tag_name")} 附件: {asset.get("name")}')
                        http_json(f'{GITEE_API}/releases/{old_rel["id"]}/attach_files/{asset["id"]}?access_token={tokens["gitee"]}', method='DELETE')
    except Exception as ex:
        log(f'Gitee 老版本附件清理跳过: {ex}')


def cmd_publish(args) -> None:
    target_ver = args.version.lstrip('vV')
    tag = f'v{target_ver}'

    tokens = {
        'gh': get_github_token(),
        'gitee': get_gitee_token(),
    }

    meta = wait_for_build(target_ver, timeout=args.wait_build_timeout)
    apk_path = Path(meta['apk_path'])
    if not apk_path.exists():
        die(f'APK 文件不存在: {apk_path}')

    # 产物一致性：文件内容必须与构建元数据一致，且包内版本必须是目标版本
    actual_sha = sha256_file(apk_path)
    if actual_sha != meta['sha256']:
        die(f'APK 与构建元数据不一致（文件被替换或 meta 过期）:\n'
            f'  文件: {actual_sha}\n  元数据: {meta["sha256"]}\n'
            f'  请重新运行 build。')
    verify_apk_identity(apk_path, target_ver)
    verify_project_settings(target_ver)

    notes = ''
    if args.notes_file:
        p = Path(args.notes_file)
        if not p.exists():
            die(f'说明文件不存在: {p}')
        notes = p.read_text(encoding='utf-8').strip()
    elif args.notes:
        notes = args.notes.strip()
    else:
        die('必须通过 --notes-file 或 --notes 提供 Release 说明')

    if not args.skip_notes_check:
        validate_notes(notes)

    # 附加上 SHA-256
    digest = meta['sha256']
    if 'SHA-256' not in notes:
        notes += f'\n\n**SHA-256**: `{digest}`'

    preflight(tag, tokens)

    branch = current_branch()
    push_both(branch, tag)

    rel_name = f'墨水作战 {tag}'
    rel_ids = create_release(tag, rel_name, notes, tokens)

    upload_apks(rel_ids, apk_path, meta['apk_name'], tokens)
    verify_releases(tag, meta['apk_name'], tokens, expected_size=meta['size'])

    prune_old_assets(tokens, keep_gh=args.keep_gh, keep_gitee=args.keep_gitee)

    log('================================================================')
    log(f'🎉 墨水作战 {tag} 双端正式发布成功！')
    log(f'  GitHub: https://github.com/{GITHUB_OWNER}/{GITHUB_REPO}/releases/tag/{tag}')
    log(f'  Gitee:  https://gitee.com/{GITEE_OWNER}/{GITEE_REPO}/releases/tag/{tag}')
    log(f'  APK:    {meta["apk_name"]} ({meta["size"]} 字节)')
    log(f'  SHA256: {digest}')
    log('================================================================')


def cmd_prune(args) -> None:
    tokens = {
        'gh': get_github_token(),
        'gitee': get_gitee_token(),
    }
    prune_old_assets(tokens, keep_gh=args.keep_gh, keep_gitee=args.keep_gitee)


# ---------------------------------------------------------------- CLI 入口

def main() -> None:
    parser = argparse.ArgumentParser(description='P1 涂地对战双端发布工具')
    subparsers = parser.add_subparsers(dest='subcmd', required=True)

    # bump
    p_bump = subparsers.add_parser('bump', help='版本号查询或递增')
    p_bump.add_argument('--version', help='指定目标版本号')
    p_bump.add_argument('--write', action='store_true', help='写回 GameVersion.cs')

    # build
    p_build = subparsers.add_parser('build', help='构建 Release APK')
    p_build.add_argument('--version', required=True, help='目标版本号')
    p_build.add_argument('--force', action='store_true', help='忽略已有产物，强制重新构建')
    p_build.add_argument('--batchmode', action='store_true',
                         help='强制使用 batchmode 构建（默认检测到 GUI 编辑器时自动改走 Pipeline）')
    p_build.add_argument('--build-timeout', type=int, default=1800,
                         help='构建等待超时秒数（默认 1800）')

    # publish
    p_pub = subparsers.add_parser('publish', help='双端推送与发布')
    p_pub.add_argument('--version', required=True, help='目标版本号')
    p_pub.add_argument('--notes-file', help='Release 说明文件路径')
    p_pub.add_argument('--notes', help='Release 说明内容')
    p_pub.add_argument('--skip-notes-check', action='store_true', help='跳过说明校验')
    p_pub.add_argument('--wait-build-timeout', type=int, default=300, help='等待构建产物超时秒数')
    p_pub.add_argument('--keep-gh', type=int, default=10, help='GitHub 保留最近 N 版 APK')
    p_pub.add_argument('--keep-gitee', type=int, default=3, help='Gitee 保留最近 N 版 APK')

    # prune
    p_prune = subparsers.add_parser('prune', help='清理老版本 APK 附件')
    p_prune.add_argument('--keep-gh', type=int, default=10, help='GitHub 保留最近 N 版 APK')
    p_prune.add_argument('--keep-gitee', type=int, default=3, help='Gitee 保留最近 N 版 APK')

    args = parser.parse_args()

    if args.subcmd == 'bump':
        cmd_bump(args)
    elif args.subcmd == 'build':
        cmd_build(args)
    elif args.subcmd == 'publish':
        cmd_publish(args)
    elif args.subcmd == 'prune':
        cmd_prune(args)


if __name__ == '__main__':
    main()
