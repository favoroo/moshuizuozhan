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
3. 复核包体（**必做**，见下节）
4. 上传并提审（见下节）

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

提审前还要确认：`platform-status` 的 `region_flag_android` 仍是 `4`（开放试玩）。

## 已知坑

- **CLI 默认 scope 会串应用**：`taptap-cli app +select` 存的是本地 profile，之前残留为 1006720「嘟嘟羽毛球」，导致 0.1.17 曾误传进那个游戏的包体库（未绑定槽位、未提审）。**所有上传/写入命令都显式带 `--app-id 1020602`**。
- **`release.py build --force` 会优先复用 `build/` 里版本匹配的旧 APK**：TapTap 包必须走菜单 13，不要用 `release.py build`。
- **宏必须还原**：菜单 13 已用 `try/finally` 保证还原。若中途强制中断了编辑器，先跑一次 `script.recompile_status` 并检查 Android 平台的 `P1_OFFLINE` 是否还在；残留会让「构建/9」也打成无联网包。
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
