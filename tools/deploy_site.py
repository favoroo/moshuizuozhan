#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""deploy_site.py — 把介绍页部署到 GitHub Pages（gh-pages 孤儿分支）

流程（与 t-1 的 deploy_web.py 同模式，但只发宣传站）：
  1. 先运行 tools/build_site.py 生成 site/dist/
  2. 校验主工作树没有未提交的「已跟踪」改动（未跟踪文件不拦）
  3. 用独立 worktree 建孤儿分支 gh-pages，把 dist/ 拷到分支根目录
  4. 提交并 git push origin gh-pages --force-with-lease（该分支始终单提交）
  5. 尝试用 gh CLI 开启 GitHub Pages（gh-pages 分支 / 根目录 / legacy）

用法：python3 tools/deploy_site.py
注意：只推 gh-pages 分支，不会改动/提交 main。
"""

import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DIST = REPO / "site/dist"
WORKTREE = REPO / ".gh-pages-work"

GITHUB_OWNER = "favoroo"
GITHUB_REPO = "moshuizuozhan"
PAGES_URL = f"https://{GITHUB_OWNER}.github.io/{GITHUB_REPO}/"


def run(cmd, **kw):
    print(f"$ {' '.join(str(c) for c in cmd)}")
    return subprocess.run(cmd, cwd=REPO, check=True, **kw)


def try_run(cmd, **kw):
    print(f"$ {' '.join(str(c) for c in cmd)}")
    return subprocess.run(cmd, cwd=REPO, check=False, **kw)


def fail(msg: str) -> None:
    print(f"[deploy_site] 错误：{msg}", file=sys.stderr)
    sys.exit(1)


def warn_if_dirty() -> None:
    """主工作树有已跟踪改动时只警告不拦截：部署用独立 worktree，
    不会动主分支（正常流程下 CHANGELOG 往往就是未提交状态）。"""
    dirty = subprocess.run(["git", "diff", "--quiet"], cwd=REPO).returncode != 0
    staged = subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=REPO).returncode != 0
    if dirty or staged:
        print("[deploy_site] 提示：主工作树有未提交的已跟踪改动（部署只发 gh-pages，不影响 main）。")


def deploy() -> None:
    if not DIST.exists():
        fail("site/dist 不存在，先跑 python3 tools/build_site.py")
    warn_if_dirty()

    # 清理可能残留的旧 worktree / 本地 gh-pages 分支
    if WORKTREE.exists():
        try_run(["git", "worktree", "remove", "--force", str(WORKTREE)])
        if WORKTREE.exists():
            shutil.rmtree(WORKTREE)
    if subprocess.run(["git", "show-ref", "--verify", "--quiet", "refs/heads/gh-pages"],
                      cwd=REPO).returncode == 0:
        run(["git", "branch", "-D", "gh-pages"])

    run(["git", "worktree", "add", "--orphan", "-b", "gh-pages", str(WORKTREE)])
    try:
        # dist → worktree 根目录
        for item in sorted(DIST.iterdir()):
            dst = WORKTREE / item.name
            if item.is_dir():
                shutil.copytree(item, dst)
            else:
                shutil.copy2(item, dst)

        msg = f"site: 介绍页部署 ({subprocess.check_output(['date', '+%Y-%m-%d %H:%M'], text=True).strip()})"
        run(["git", "-C", str(WORKTREE), "add", "-A"])
        run(["git", "-C", str(WORKTREE), "commit", "-m", msg])
        run(["git", "-C", str(WORKTREE), "push", "origin", "gh-pages", "--force-with-lease"])
    finally:
        try_run(["git", "worktree", "remove", "--force", str(WORKTREE)])
        if WORKTREE.exists():
            shutil.rmtree(WORKTREE)


def enable_pages() -> None:
    """首次部署需要把仓库 Pages 源设为 gh-pages 分支根目录（legacy 模式）。"""
    if shutil.which("gh") is None:
        print("[deploy_site] 未安装 gh CLI，请到仓库 Settings → Pages 手动选择 gh-pages 分支 /(root)。")
        return
    r = subprocess.run(
        ["gh", "api", f"repos/{GITHUB_OWNER}/{GITHUB_REPO}/pages",
         "-X", "POST",
         "-f", "source[branch]=gh-pages",
         "-f", "source[path]=/",
         "-f", "build_type=legacy"],
        cwd=REPO, capture_output=True, text=True)
    if r.returncode == 0:
        print(f"[deploy_site] Pages 已开启：{PAGES_URL}")
    elif "409" in r.stderr or "already exists" in r.stderr.lower():
        print(f"[deploy_site] Pages 已存在，直接访问：{PAGES_URL}")
    else:
        print(f"[deploy_site] 自动开启 Pages 失败：{r.stderr.strip()}")
        print(f"  请手动到 https://github.com/{GITHUB_OWNER}/{GITHUB_REPO}/settings/pages 选择 gh-pages 分支 /(root) Save。")


def main() -> None:
    build = subprocess.run([sys.executable, str(REPO / "tools/build_site.py")], cwd=REPO)
    if build.returncode != 0:
        fail("build_site.py 失败")
    deploy()
    enable_pages()
    print(f"\n[deploy_site] 完成 → {PAGES_URL} （GitHub 构建约需 1 分钟）")


if __name__ == "__main__":
    main()
