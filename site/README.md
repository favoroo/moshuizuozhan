# 墨水大作战 InkTurf — 介绍页

线上地址：<https://favoroo.github.io/moshuizuozhan/>（GitHub Pages，`gh-pages` 孤儿分支）

## 目录

```
site/
  index.html / styles.css / app.js   # 页面源码（含 __V__ / __APP_VERSION__ 构建令牌）
  video/                             # 宣传视频放这里（mp4/webm，不进 git）
  dist/                              # 构建产物（不进 git）
tools/build_site.py                  # 素材构建：截图转 webp、图标拷贝、字体子集、令牌注水
tools/deploy_site.py                 # 部署：构建 + gh-pages 孤儿分支推送 + 开启 Pages
```

## 日常操作

```bash
# 本地预览（改完页面后）
python3 tools/build_site.py            # 没有 Builds/WebGL 时会提示跳过 /play/
python3 -m http.server 8080 -d site/dist
# 打开 http://localhost:8080

# 部署上线（会重新 build_site；线上已有 /play/ 而本轮产物没有时会拒绝部署）
python3 tools/deploy_site.py
```

## 网页试玩（WebGL）

线上地址：<https://favoroo.github.io/moshuizuozhan/play/>（介绍页仍在根目录）。

- 需要 Unity 6000.6.7f1 的 **Web Build Support** 模块；没装时先：
  `~/.unity/bin/unity install-modules -e 6000.6.7f1 -m webgl --accept-eula`
- 出包（产物 `Builds/WebGL/`，不进 git）：

  ```bash
  ~/.unity/bin/unity build /Users/a1/Documents/01Code/Unity/P1 \
    --target WebGL \
    --execute-method P1.Editor.BuildAndProfilerSuite.BuildWebGL \
    --output-path Builds/WebGL \
    --allow-dirty-build \
    --log-file Logs/build-webgl.log
  ```

  也可以在 Unity 菜单点 `P1/构建/14. 构建 WebGL 网页试玩包`。
- `tools/build_site.py` 会把 `Builds/WebGL/` 整体拷进 `site/dist/play/`；缺少
  `Builds/WebGL/index.html` 时会跳过 `/play/` 并警告。`deploy_site.py` 会检查线上是否已有
  `/play/`，已有但本轮产物没有时**拒绝部署**，避免只改文案的发布把网页试玩删掉。
  脚本比源码旧时会打印“可能不是最新构建”的警告（不会自动重建）。
- 构建配置：Brotli + **Decompression Fallback**（GitHub Pages 不能设 `Content-Encoding`）、
  哈希文件名（避免 CDN 缓存错配）、关闭多线程；模板在
  `Assets/WebGLTemplates/InkTurf/index.html`。
- **仅桌面浏览器**：手机打开 `/play/` 会显示“请下载 Android 版”提示，不会加载游戏。
  游戏内「检查更新」在 WebGL 下改为打开 GitHub Releases。

## 更新宣传视频

宣传视频双源已就位：

- `video/promo.av1.mp4` — AV1 1080p 主源（18.7 MB），现代浏览器优先播放
- `video/promo.mp4` — H.264 720p 兜底（15 MB），老浏览器 / 不支持 AV1 的设备

换视频时按同样规格重编码两条，同名覆盖后重新跑 `deploy_site.py`。Hero 海报会
自动从视频第 3 秒抽帧生成（无视频时才退回实机截图）。视频不进 main 的 git
（`site/.gitignore` 忽略），但会随部署进入 `gh-pages` 分支。

## 版本号

页面不展示游戏版本号。`tools/build_site.py` 仍会读取 `GameVersion.cs` 的 `APP_VERSION`
用于生成静态资源的缓存令牌（`?v=`），发版后重新部署即可刷新缓存。

## 注意

- `deploy_site.py` 只推 `gh-pages` 分支，不会提交/改动 `main`；主工作树有未提交的
  **已跟踪**改动时只打印警告，不拦截部署（部署走独立 worktree）。
- 截图来自 `Logs/`（不进 git），换机器后若缺图，构建脚本会报缺失文件名。
- `Builds/WebGL/` 是本地构建产物；换机器或清理 `Builds/` 后，部署前必须重新出 WebGL 包。
