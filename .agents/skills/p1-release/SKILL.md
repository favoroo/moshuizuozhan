---
name: p1-release
description: P1 涂地对战游戏的 Git 提交存档、双平台推送与版本发布流程。当用户要求提交/commit/存档/git提交、推送/push/推送到平台/推送到云端/双端推送、发布/release/发版/打tag/创建release、版本号加一/apk版本号加一/版本号+1/升级版本/版本递增、构建APK/打apk包时使用。涵盖 Unity 6000.6.7f1 Android APK 导出构建、一键发布脚本 release.py、双端 Tag 与 Release API 创建、令牌配置与发布后检查清单。位于 .agents/skills/p1-release/SKILL.md。
---

# P1 涂地对战 Git 提交与双端版本发布

## Git 提交与双平台同步

- **提交与存档原则（绝对禁止未授权自动操作）**：
  - **严禁自动执行 `git commit`（存档）**！日常代码修改和验证完成后，留在工作区，方便用户自行查看 Diff、测试与确认。
  - **严禁自动执行 `git push`（推送到云端）**！
  - 只有当用户明确指示「提交/存档/commit」时，才执行本地 `git commit`；
  - 只有当用户明确指示「推送/push/推送到云端/发布」时，才执行向远端推送。
- **提交格式**：`<type>(<scope>): <description>`，Type：`feat` / `fix` / `refactor` / `style` / `docs` / `test` / `chore`
- **双平台同步要求**：代码、Git Tag 及 Release 产物必须**同时同步至 GitHub 与 Gitee 两端**：
  - **GitHub 远端**：`origin` (`https://github.com/favoroo/moshuizuozhan.git`)
  - **Gitee 远端**：`gitee` (`https://gitee.com/favo9/moshuizuozhan.git`)
  - 代码与标签一次连接同时推：
    ```bash
    git push origin main vX.Y.Z &
    git push gitee main vX.Y.Z &
    wait
    ```

---

## 版本发布流程

当用户明确要求「发布版本 / release / 发版 / 版本号+1 / 构建APK」时执行。

### 0. 前置约定

| 项 | 规范 |
|----|------|
| 构建命令 | 调用 `python3 .agents/skills/p1-release/release.py build --version X.Y.Z`，内部通过 Unity CLI / Batchmode 导出 Release APK |
| APK 命名 | `moshuizuozhan-v<version>-arm64-v8a.apk`（如 `moshuizuozhan-v0.1.0-arm64-v8a.apk`），**两端文件名完全一致** |
| 产物目录 | `build/` |
| Tag 命名 | `v<version>`（如 `v0.1.0`），使用 annotated tag |
| 版本号递增 | **每次发布/打包 APK，版本号必须递增 +1**，禁止同版本覆盖发布。由 `release.py bump` 自动判断：读 `Assets/_Project/Scripts/Core/GameVersion.cs` 当前版本 X → 查 tag `vX` → 不存在直接用 X；已存在则 +1 到 X+1 并写回。 |
| 版本来源 | `Assets/_Project/Scripts/Core/GameVersion.cs` 的 `APP_VERSION` 与 Unity `PlayerSettings.bundleVersion` 保持严格一致 |
| 两端一致性 | GitHub 与 Gitee 必须使用相同 Tag、相同 APK 文件名、同为正式 Release（非 draft / prerelease） |
| 更新服务 | `Assets/_Project/Scripts/UI/UpdateManager.cs` 优先读 Gitee `/releases/latest`，备选 GitHub，并配合国内加速代理 |
| 附件保留策略 | **发布后自动清理双端老 APK 附件**：GitHub 保留最近 10 个（`--keep-gh`）、Gitee 保留最近 3 个（`--keep-gitee`），控制仓库体积与 Gitee 1GB 附件配额 |

### 1. 一键脚本编排

脚本：`.agents/skills/p1-release/release.py`（纯 Python 标准库）。

```text
⓪ 发布前门禁:
   U="$HOME/.unity/bin/unity"
   "$U" command script.recompile_status    # 编译是否成功（compilationFailed: false）
   "$U" command console.get --tail 50 --level error
① 自动算版本号并写回:  python3 .agents/skills/p1-release/release.py bump --write
   —— 读 GameVersion.cs → 查 tag → 不存在直接用当前版本(预升未发布);已存在则 +1 写回 GameVersion.cs
② 执行构建:           python3 .agents/skills/p1-release/release.py build --version X.Y.Z
③ 提交改动:           git add -A && git commit -m "feat(release): 发布墨水作战 vX.Y.Z"
④ 撰写 Release 说明存为临时文件，执行发布:
   python3 .agents/skills/p1-release/release.py publish --version X.Y.Z --notes-file /tmp/notes.md
   —— 写法见 §3；publish 会校验条数/字数/禁用词，不合规直接拒绝。
⑤ 向用户汇报发布验证清单（含两端 URL、APK 附件状态与 SHA-256）
```

- `build` 阶段：校验版本一致性 → Unity 导出 Release APK → SHA-256 计算 → 原子写 `build/release-meta.json`。
- `publish` 阶段：等待构建元数据就绪 → 预检（工作树干净、tag 不存在）→ 创建 annotated tag → **并行**推送双端 → **并行**创建双端 Release → **并行**上传 APK → 幂等可重试 → 双端验证 → 自动清理双端老版本附件（GitHub 保留最近 10 个 / Gitee 保留最近 3 个）。

### 2. 令牌获取（严禁写入任何仓库文件）

脚本按以下顺序自动获取令牌，绝不落盘：
1. 环境变量：`GITHUB_TOKEN`（或 `GH_TOKEN`）、`GITEE_TOKEN`；
2. macOS Keychain（git 凭据库）：`printf 'protocol=https\nhost=github.com\n\n' | git credential fill`（Gitee 同理取 `password`）；
3. 都取不到时提示用户配置。

### 3. Release 说明规范（玩家视角）

**唯一判据：只写玩家进游戏能看见、听见或手感到的变化。** 代码结构、性能余量、内部工具、测试、日志一律不写——玩家不关心代码怎么改，只关心玩起来有什么不同。

硬约束：

- 总条目 ≤5 条，每条 ≤25 字（中文计，含标点）。
- 只用三段：`### 新增内容` / `### 调整` / `### 问题修复`。数值增强削弱进「调整」，不要塞进「问题修复」。
- 每条写「玩家看到什么变了」，不写「代码怎么改的」；第一条放最值得玩家知道的变化。
- 末尾 `**SHA-256**` 行保留（游戏内弹窗会自动隐藏）。
- `publish` 调用 `validate_notes()` 校验条数、字数与禁用词，不合规直接拒绝发布；确需跳过加 `--skip-notes-check`。

禁用词（出现即说明写成了开发者视角）：

`池化` `对象池` `节流` `信号` `节点` `await` `渲染器` `工厂` `口径` `探针` `HUD` `合批` `分帧` `重构` `缓存` `日志` `接口` `测试` `站点`

模板：

```markdown
## 墨水作战 vX.Y.Z

### 新增内容
- 首页新增检查更新与游戏内下载安装
- 全新 4v4 潮流涂地对决与大招爆发

### 调整
- 优化操作按键与移动端触控手感

### 问题修复
- 修复偶发界面显示异常

**SHA-256**: `<shasum -a 256 输出>`
```

### 4. 发布检查清单

脚本自动完成：

- [ ] Release 说明通过 `validate_notes()`（≤5 条 / ≤25 字 / 无禁用词）
- [ ] GitHub Release 页面可见，APK 附件已上传（state=uploaded）
- [ ] Gitee Release 页面可见，APK 附件已上传
- [ ] 两端 Tag 一致（`v<version>`）、两端 APK 文件名一致
- [ ] 两端 `/releases/latest` 均指向新 Tag（游戏内更新检测依赖）
- [ ] 双端历史附件已按配额修剪（GitHub 最近 10 版 / Gitee 最近 3 版）
