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


# ---------------------------------------------------------------- build 阶段

def cmd_build(args) -> None:
    cur_ver = read_current_version()
    target_ver = args.version.lstrip('vV')
    if cur_ver != target_ver:
        die(f'GameVersion.cs ({cur_ver}) 与 --version ({args.version}) 不一致，请先运行 bump')

    apk_name = f'moshuizuozhan-v{target_ver}-arm64-v8a.apk'
    BUILD_DIR.mkdir(exist_ok=True)
    dst = BUILD_DIR / apk_name

    log(f'开始构建 Release APK ({apk_name})...')

    # 优先检查是否已有构建产物（避免重复耗时构建）
    candidates = [
        dst,
        REPO_ROOT / 'Builds' / 'Android' / apk_name,
        REPO_ROOT / 'Builds' / 'Android' / 'InkTurfDemo.apk',
        REPO_ROOT / 'Builds' / 'Android' / 'moshuizuozhan.apk'
    ]

    found_apk = None
    for cand in candidates:
        if cand.exists() and cand.stat().st_size > 5 * 1024 * 1024:
            found_apk = cand
            break

    if not found_apk or args.force:
        log('正在调用 Unity 构建 Android APK...')
        # 尝试调用 Unity CLI 或 batchmode
        build_output_arg = str(dst)
        build_log = REPO_ROOT / 'Logs' / 'build_android_release.log'

        # 方式 1: unity build 命令
        cmd = [
            UNITY_CLI, 'build',
            '--target', 'Android',
            '--execute-method', 'P1.Editor.BuildAndProfilerSuite.BuildAndroid',
            '--output-path', build_output_arg,
            '--allow-dirty-build',
            '--no-tail',
            '-l', str(build_log)
        ]
        log(f'执行命令: {" ".join(cmd)}')
        r = run(cmd)
        if r.returncode != 0:
            log(f'unity build 返回非 0 ({r.returncode})，尝试检查是否有输出或备用方案...')

        for cand in candidates:
            if cand.exists() and cand.stat().st_size > 5 * 1024 * 1024:
                found_apk = cand
                break

    if not found_apk:
        die(f'未找到构建出的 APK 文件。请检查构建日志: {REPO_ROOT}/Logs/build_android_release.log')

    if found_apk != dst:
        log(f'拷贝 APK 产物: {found_apk} -> {dst}')
        shutil.copy2(found_apk, dst)

    digest = sha256_file(dst)
    meta = {
        'status': 'success',
        'version': target_ver,
        'tag': f'v{target_ver}',
        'apk_name': apk_name,
        'apk_path': str(dst),
        'sha256': digest,
        'size': dst.stat().st_size,
        'timestamp': int(time.time()),
    }
    write_meta(meta)
    log(f'构建完成: {apk_name} ({dst.stat().st_size} 字节, {dst.stat().st_size / (1024 * 1024):.1f} MB)')
    log(f'SHA-256: {digest}')


# ---------------------------------------------------------------- publish 阶段

def validate_notes(notes: str) -> None:
    lines = [ln.strip() for ln in notes.splitlines() if ln.strip()]
    items = [ln for ln in lines if ln.startswith(('-', '*'))]

    if not (1 <= len(items) <= NOTES_MAX_ITEMS):
        die(f'Release 说明条数不合规: 当前 {len(items)} 条（要求 1~{NOTES_MAX_ITEMS} 条）')

    for it in items:
        clean = re.sub(r'^[-*]\s*', '', it)
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


def preflight(tag: str, tokens: dict) -> None:
    r = run(['git', 'status', '--porcelain'])
    if r.stdout.strip():
        lines = [ln for ln in r.stdout.strip().splitlines() if 'release-meta.json' not in ln and '.tmp' not in ln]
        if lines:
            die('工作树不干净，先提交全部改动再 publish:\n' + '\n'.join(lines[:10]))

    r = run(['git', 'rev-parse', '--verify', f'refs/tags/{tag}'])
    if r.returncode == 0:
        st_gh, d_gh = http_json(f'{GITHUB_API}/releases/tags/{tag}', headers=gh_headers(tokens['gh']))
        st_gt, d_gt = http_json(f'{GITEE_API}/releases/tags/{tag}?access_token={tokens["gitee"]}')
        gh_apks = [a for a in (d_gh.get('assets', []) if st_gh == 200 and d_gh else []) if str(a.get('name', '')).endswith('.apk')]
        gt_apks = [a for a in (d_gt.get('assets', []) if st_gt == 200 and d_gt else []) if str(a.get('name', '')).endswith('.apk')]
        if gh_apks and gt_apks:
            die(f'tag {tag} 已在双端完整发布过 Release 与 APK，禁止同版本覆盖发布。请先运行 bump 递增版本号。')
        log(f'本地 tag {tag} 已存在，远端 Release/APK 尚未全部就绪，将复用该 tag（重试场景）')
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
        body, ctype = multipart_body({'name': apk_name}, 'file', apk_name, file_bytes)
        st, d = http_json(
            f'{GITEE_API}/releases/{release_ids["gitee"]}/attach_files',
            'POST', headers={'Authorization': f"token {tokens['gitee']}",
                             'Content-Type': ctype},
            data=body, timeout=600)
        if st not in (200, 201) or not d:
            if st == 400 and 'already exists' in str(d):
                log('Gitee 附件已存在，跳过重复上传')
                return
            raise RuntimeError(f'Gitee 上传 APK 失败({st}): {d}')
        log(f"Gitee 上传完成: {apk_name}")

    log(f'并行上传 APK ({apk_name}, {len(file_bytes)} 字节)...')
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
        f1, f2 = ex.submit(upload_gh), ex.submit(upload_gitee)
        f1.result()
        f2.result()


def verify_releases(tag: str, apk_name: str, tokens: dict) -> None:
    log('开始发布后双端验证...')
    st_gh, d_gh = http_json(f'{GITHUB_API}/releases/tags/{tag}', headers=gh_headers(tokens['gh']))
    if st_gh != 200 or not d_gh:
        die(f'GitHub 验证失败({st_gh})')
    gh_assets = [a['name'] for a in d_gh.get('assets', [])]
    if apk_name not in gh_assets:
        die(f'GitHub Release 缺少附件 {apk_name}，现有: {gh_assets}')
    log(f'✓ GitHub Release 校验通过: {d_gh.get("html_url")}')

    st_gt, d_gt = http_json(f'{GITEE_API}/releases/tags/{tag}?access_token={tokens["gitee"]}')
    if st_gt != 200 or not d_gt:
        die(f'Gitee 验证失败({st_gt})')
    gt_assets = [a['name'] for a in d_gt.get('assets', [])]
    if apk_name not in gt_assets:
        die(f'Gitee Release 缺少附件 {apk_name}，现有: {gt_assets}')
    log(f'✓ Gitee Release 校验通过: {d_gt.get("html_url")}')


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
    verify_releases(tag, meta['apk_name'], tokens)

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
    p_build.add_argument('--force', action='store_true', help='强制重新构建')

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
