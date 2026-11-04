# TapTap 上架发版清单

> **每次往 TapTap 上传新版本前必须过一遍这份清单。**
> 本文件是 TapTap 渠道的长期记录；发版流程见技能 `p1-release`。

## 为什么 TapTap 要单独出包

TapTap 审核会做安装包静态检测。历史上依次卡过两条：

| 时间 | 拒审原因 | 修复方式 |
|---|---|---|
| v0.1.15 | 安装包隐私合规检测未通过（含计费/统计 SDK、高危安装权限、无隐私同意） | 移除 Purchasing/Analytics 依赖、移除 `REQUEST_INSTALL_PACKAGES`、加隐私同意 |
| v0.1.17 | 同意前读取传感器列表（Unity3d 框架在 UnityPlayer 初始化阶段读取） | 原生 `PrivacyActivity` 做启动页，同意前不创建 `UnityPlayer` |
| v0.1.17（复审） | **安装包检测到联网功能（检查更新需联网）→ 要求补充 ICP 核准信息** | 出**无联网变体**：去掉 INTERNET 权限、隐藏检查更新 |

第三条决定了现在必须打两个不同的包：

- **TapTap 上架包**：`P1_OFFLINE` 宏，无 INTERNET 权限，无检查更新入口。
- **其它渠道包**（GitHub / Gitee Release、网页版）：完整联网更新链路。

## 出包步骤

1. 递增版本：`python3 -u .agents/skills/p1-release/release.py bump --write`
2. 打 TapTap 包：菜单 **`P1/构建/13. 构建 TapTap 上架包（无联网变体）`**
   - 产物：`Builds/Android/TapTap/InkTurf-TapTap.apk`
   - 该菜单会临时写入 `P1_OFFLINE` 宏，构建结束自动还原，不会污染后续其它渠道出包。
3. **先建草稿版本**（上一版已是「已上线」时必做）：
   `taptap-cli app create-draft --app-id 1020602 --dev-id 504338 --idempotency-key p1-draft-<版本> --yes`
   - 已上线版本字段**全锁**，不建草稿就没法绑新包。
   - 草稿基于最新线上版本创建，资料自动继承；建完后 `analyze-app-status` 的 blockers 应为空。
   - **假警报**：建草稿**之前**调 `analyze-app-status` 会以「无未发布草稿」的空视图报出「9 个必填项未填写 + 尚未选择主包体」，那不是资料丢失，不要去补资料。
4. 复核包体（**必做**，见下节）
5. 上传并提审（见下节）

> 批处理出包时调用 `P1.Editor.TapTapBuildSuite.BuildTapTapApk`，同样自动切宏与还原。

## 包体复核（上传前必做）

```bash
AAPT=/Applications/Unity/Hub/Editor/6000.6.7f1/PlaybackEngines/AndroidPlayer/SDK/build-tools/36.0.0/aapt2
APK=Builds/Android/TapTap/InkTurf-TapTap.apk

# 1. 版本号正确
"$AAPT" dump badging "$APK" | grep -E "^package:|launchable-activity"
# 期望：versionCode/versionName 与 GameVersion 一致；launchable-activity = com.p1studio.inkturfdemo.PrivacyActivity

# 2. 零联网权限（关键）
"$AAPT" dump permissions "$APK"
# 期望：只有 com.p1studio.inkturfdemo.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION
# 绝不能出现 android.permission.INTERNET

# 3. Unity 页不可被外部拉起
"$AAPT" dump xmltree --file AndroidManifest.xml "$APK" | grep -A3 UnityPlayerActivity | grep exported
# 期望：exported=false
```

## 上传与提审

```bash
# 注意：CLI 的默认 app scope 可能残留为其它游戏，一律显式带 --app-id 1020602
taptap-cli upload-apk Builds/Android/TapTap/InkTurf-TapTap.apk \
  --app-id 1020602 --dev-id 504338 --dry-run
taptap-cli upload-apk Builds/Android/TapTap/InkTurf-TapTap.apk \
  --app-id 1020602 --dev-id 504338 --idempotency-key p1-apk-<版本> --yes

taptap-cli app list-packages --app-id 1020602 --dev-id 504338   # 取新 package_id 与 main expected
taptap-cli app select-package --app-id 1020602 --dev-id 504338 --data @select.json --yes
taptap-cli app save-changes --app-id 1020602 --dev-id 504338 --data @whatsnew.json --yes
taptap-cli app prepare-review-snapshot --app-id 1020602 --dev-id 504338
taptap-cli app precheck-app-review --app-id 1020602 --dev-id 504338 \
  --data '{"release_schedule":{"kind":"immediate"}}'
taptap-cli app submit-app-review --app-id 1020602 --dev-id 504338 \
  --data '{"release_schedule":{"kind":"immediate"}}' --idempotency-key p1-submit-<版本> --yes
```

两处 payload 口径：

- `select.json` 的 `expected` **原样**回传同一次 `list-packages` 里的 `package_slots.main.expected`（形如 `{"kind":"apk","apk_id":"<旧包体 id>"}`），`package.id` 用本次上传返回的 `apk_id`：
  `{"slot":"main","package":{"id":"<新 apk_id>","type":"apk"},"expected":{"kind":"apk","apk_id":"<旧 apk_id>"}}`。
  写完必须重读 `list-packages` 核对 `package_slots.main.expected.apk_id` 已变新值。
- `whatsnew.json` 的 `expected` 必须回填 `get-app-module("package")` 里 `apk_whatsnew` 的当次 `current_value`，`value` 是新更新日志（≤5000 字）：
  `{"changes":[{"field_id":"apk_whatsnew","value":"<新文案>","expected":"<旧文案>"}]}`。
  写后读回确认。

提审意图是独立确认门：跑完预检必须先向用户确认，再执行 `submit-app-review`。

提审前还要确认：`platform-status` 的 `region_flag_android` 仍是 `4`（开放试玩）。

## 已知坑

- **CLI 默认 scope 会串应用**：`taptap-cli app +select` 存的是本地 profile，之前残留为 1006720「嘟嘟羽毛球」，导致 0.1.17 曾误传进那个游戏的包体库（未绑定槽位、未提审）。**所有上传/写入命令都显式带 `--app-id 1020602`**。
- **`release.py build --force` 会优先复用 `build/` 里版本匹配的旧 APK**：TapTap 包必须走菜单 13，不要用 `release.py build`。
- **宏必须还原（比想象的更容易残留）**：菜单 13 的 `try/finally` 只还原**编辑器内存**里的宏。实测 v0.1.23 出包后 `script.eval` 读到的 Android defines 已是空数组，但 `ProjectSettings.asset` 落盘停在构建中途写入的
  ```yaml
  scriptingDefineSymbols:
    Android: P1_OFFLINE
  ```
  ——**内存对、文件错**，一眼看不出问题。**每次跑完菜单 13 必须 `git diff ProjectSettings/ProjectSettings.asset`**：若出现 `Android: P1_OFFLINE`，用 `script.eval` 写空 defines + `File/Save Project` 清掉，再 `git checkout` 该文件；否则下一次「构建/9」会静默打出无联网包。残留时用 `script.eval` 单独查 Android defines 是查不出来的，只有 diff 文件才可靠。
- **无联网包不能自更新**：玩家从 TapTap 拿到新版本靠商店更新，游戏内不再有更新入口（右下角角签退回只读版本号）。

## 相关代码

| 文件 | 作用 |
|---|---|
| `Scripts/Core/BuildVariants.cs` | 宏常量 `P1_OFFLINE` 的唯一来源 |
| `Scripts/Editor/TapTapBuildSuite.cs` | 菜单 `P1/构建/13`，切宏 → 出包 → 还原 |
| `Scripts/Editor/AndroidPrivacyManifestPostprocessor.cs` | Gradle 生成后压 `UnityPlayerActivity.exported=false`；`P1_OFFLINE` 下删除 INTERNET 权限 |
| `Scripts/UI/UpdateManager.cs` | `NetworkFeatureEnabled` 与 `CheckForUpdate` 兜底 |
| `Scripts/UI/MatchFlowUI.cs` | `P1_OFFLINE` 下右下角角签退回只读版本号 |
| `tools/android-privacy/` + `tools/build_privacy_activity.sh` | 原生隐私同意启动页及其编译脚本 |
