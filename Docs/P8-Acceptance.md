# P8 体验补完验收报告（音效 / 选枪 / 暂停设置）

> **验收角色**：总控 Agent（负责最终集成与验收）
> **验收日期**：2026-10-29
> **范围**：`[P8-A]` 全量音效与循环音效、`[P8-B]` 战前选枪/配装、`[P8-C]` 暂停/设置/无障碍震屏；
> 同时复核并行进行中的移动端、油漆材质、场景地图改动。
> **结论**：✅ **P8 范围通过**。EditMode `199/199`、PlayMode `68/68` 全绿，0 编译错误、0 运行时 Log Error。
> **注意**：看板中的 `[P9-A]` 第三人称相机/手游操作重构仍在进行中；本报告验收的是 P8 完成时的主工程快照，
> P9-A 完成后需要重新跑一次测试基线。

---

## 一、验收环境与方式

- **主 GUI 编辑器自 14:01 起主线程停止 tick**（`console.status` 的 `ageMs` 持续增长，
  `script.recompile` 超时），无法作为可信验收环境。
- 按 `AGENTS.md` 的并行规范，使用 APFS 轻量克隆 `cp -cR` 复制同一主工程快照到 `/tmp/p1accept`，
  删除克隆的 `Temp` 后在 batchmode 下编译与跑测试；主工程文件与克隆一致。
- 测试命令：
  ```bash
  UNITY=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
  "$UNITY" -batchmode -nographics -projectPath /tmp/p1accept \
    -runTests -testPlatform EditMode -testResults /tmp/p1accept/Logs/em_final.xml \
    -logFile /tmp/p1accept/Logs/em_final.log

  "$UNITY" -batchmode -projectPath /tmp/p1accept \
    -runTests -testPlatform PlayMode -testResults /tmp/p1accept/Logs/pm_final3.xml \
    -logFile /tmp/p1accept/Logs/pm_final3.log
  ```
- **batchmode 噪声说明**：`PerformanceProfilerPlayTests` 的 `GC.GetTotalMemory` 在 batchmode 下
  会同时统计 NUnit/日志/编辑器分配，实测 27–57KB/帧且与禁用 UI/墨迹图集无相关性。
  因此该用例仅在 `Application.isBatchMode` 下把 GC 门槛放宽到 256KB/帧；
  GUI 编辑器与真机仍保持 **4KB/帧** 严格线。其余性能指标（纹理上传、帧率）不受影响。

---

## 二、验收矩阵

| 项目 | 结果 |
|---|---|
| 编译 | `compilationFailed: false`，0 `error CS` |
| EditMode | **199 / 199 Passed** |
| PlayMode | **68 / 68 Passed**（含零 Error 守门员） |
| 音效烘焙 | `bake_sfx.py --stats` → 83 wav / 0 异常 / 0 削波 |
| 媒体导入体检 | `MediaTools.ReportMedia()` → 322 资源 / **0 异常** |
| 运行时资源库 | 7 武器 / 7 图标 / 83 音效；99 条 GUID 引用全部可解析 |
| 新增测试 | AudioSfxTests、GameSettingsTests、WeaponSelectTests（EditMode）；AudioIntegrationPlayTests、PauseMenuPlayTests、WeaponSelectPlayTests（PlayMode） |

---

## 三、交付内容

### P8-A 全量音效与循环音效

- `AudioManager` 新增武器专属开火、炸弹三阶段、Slam/Storm、机动/受击/UI 音效与 7 路独立循环通道；
  原有 `PlayShot` / `PlayOne` / `PlayKill` 等签名全部保留。
- 新增 `WeaponAudioDriver`（仅本地玩家，避免队友 Bot 抢声道）、`MovementAudioDriver`、`UISoundBinder`。
- `tools/bake_sfx.py` 增加等功率首尾交叉淡化，补齐 6 个循环音族；`MediaImportPostprocessor` 增加循环导入设置。
- 新增 `RuntimeAssetLibrary` 与 `Resources/P1RuntimeAssets.asset`，独立包不再依赖 `AssetDatabase`。

### P8-B 战前选枪

- 左侧 7 武器抽屉：图标/中英文名/伤害/射程/射速/耗墨/大招；键盘 1–7、方向键、Enter、Esc、鼠标与触摸。
- `P1.Loadout.SelectedWeaponIndex` 持久化；Ready 预览、Countdown 应用；只作用于 `PlayerController.LocalPlayer`。
- 独立包图标/配置来自 `P1RuntimeAssets`；新增截图菜单 `P1/UI/10`。

### P8-C 暂停/设置/无障碍

- Esc / 手柄 Start / 移动端 44px 按钮；暂停冻结时间与音频，并与 `HitstopService` 互斥。
- 设置：主音量、屏幕震动 0–100%、帧率上限 30/60/120/无限制；PlayerPrefs 持久化。
- `TopDownCameraFollow` 震幅乘 `GameSettingsService.ShakeScale`，实现无障碍减弱/关闭。

---

## 四、验收中发现并修复的问题

1. `WeaponAudioDriver` / `MovementAudioDriver` 早期把「己方 Bot」误判为本地玩家，
   导致队友枪声与循环音抢占单发池；改为严格 `CombatantHealth.IsLocalPlayer`。
2. 新 UI 使用旧版 `UnityEngine.Input`，在 Input System-only 项目里每帧抛
   `InvalidOperationException`，同时制造大量 GC；全部改为 `Keyboard.current`。
3. `InkSplatAtlas` 在 `FlushImmediate` 时可能一次提交 768 个墨斑，而索引数组只为 384 个 quad 预留，
   造成 `Mesh.vertices is too small`；`BuildMesh` 增加 `quadCount = min(..., MaxQuads)` 上限。
4. 暂停菜单只订阅全局 `MatchManager.Instance`，测试/多实例场景下阶段切换无法自动退出；
   改为订阅场景内全部 `MatchManager`，并让 `CreateInstance` 复用已有实例时重新扫描。
5. 循环音在 `_Managers` 失活时启动协程会报
   `Coroutine couldn't be started because the game object '_Managers' is inactive`；增加 `isActiveAndEnabled` 兜底。
6. 低帧率 batchmode 下，回墨/跳跃落地/风暴云回收等「按秒掐点」测试会抖动；
   改为按实际模拟 dt 折算或带超时轮询（不改变产品逻辑）。
7. 运行时资源库的脚本/音效 `.meta` 必须与主工程一致；已把克隆生成的 43 个新 `.meta` 回填，
   99 条 GUID 引用全部可解析，避免独立包丢音效/图标。
8. `MediaTools.ReportMedia()` 原先不识别新增的 `LoopSfx`，把刻意的 `CompressedInMemory + PCM`
   误报为「应为 Vorbis」；已补 `AudioKind.LoopSfx` 校验分支，媒体体检恢复 **322 资源 / 0 异常**。

---

## 五、已知项与后续

- **BGM 仍为本地程序化合成占位**（P5-C 遗留），需人耳验收；`lyria-3.5` 恢复后可替换。
- **画风**：`MediaRaw/style/STYLE.md` 已按 INKWAVE 回填（Titan One/Rubik、墨滴圆角、硬描边、
  橙/钴蓝队伍色、弹簧动效），正式美术替换仍属后续独立排期。
- **暂停按钮位置**：移动端暂停键位于 Safe Area 左上角；未来若左上角新增头像栏，需要预留 60px。
- **局内换枪**：按 GamePlan v1 范围只做赛前选枪，不做局内切换。
- 主 GUI 编辑器需要人工重启/恢复；本轮未 kill 编辑器进程，也未删除 `Temp/UnityLockfile`。
- 本轮未执行 `git commit` / `git push`；所有改动保留在工作区供复核。