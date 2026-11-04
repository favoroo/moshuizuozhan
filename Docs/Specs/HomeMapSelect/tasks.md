# 任务清单：首页地图选择卡片 + 真实切图

> 对应规格：[spec.md](spec.md)。已全部完成，勾选状态用于交付核对。

## 0. 前置保护

- [x] `git status --short` 快照存档，确认他人未提交改动（角色/武器/材质/配置/Logs）保持原样。
- [x] 截「改前首页」：进入 Play Mode，`ScreenCapture` 存 `Logs/verify-map-select/home_before.png`。
- [x] `git diff` 记录 `RenderEnvGlobals.cs` / `MatchFlowUI.cs` / `PauseMenuOverlay.cs` / `WeaponSelectView.cs` / `WeaponSelectBootstrap.cs` / `MenuInputRouter.cs` 当前内容，后续只做增量。

## 1. 数据与服务层

- [x] 新增 `Assets/_Project/Scripts/UI/MapCatalogService.cs`：
      MapDefinition / MapSchematicBlock / MapSchematicRole；两张图数据（phase_a / kelp_terminal）；
      `SelectedId` 默认 PhaseA 不写盘；`TrySelect` 写 `PlayerPrefs("P1.Map.SelectedId")` + Save；
      `ActiveOrSelected` / `MapForScene` / `IsMapScene`；`LoadMap` 用 `SceneUtility.GetBuildIndexByScenePath` + `LoadScene(Single)`；
      `ResetRuntimeState` / `ResetForTests`。
- [x] 新增 `Assets/_Project/Scripts/UI/MapStartupRouter.cs`：
      SubsystemRegistration 重置静态；AfterSceneLoad 首帧恢复选中图（只在地图场景内）；
      sceneLoaded 清 `_loading`；载入 PhaseA 时 `RenderEnvGlobals.ResetStyleToDefault()`。
- [x] `Scripts/Core/RenderEnvGlobals.cs` 新增 `ResetStyleToDefault()`（只增不改既有 API）。

## 2. 预览与浮层

- [x] 新增 `Assets/_Project/Scripts/UI/MapPreviewBuilder.cs`：40×56 等比俯视小图；颜色全部走 `ComicStyle` / `theme.teamAColor/teamBColor` / 地图 Accent；无光线投射。
- [x] 新增 `Assets/_Project/Scripts/UI/MapSelectLayoutMath.cs`：桌面/移动两套面板、头部、卡片、预览、字号、按钮、页脚；`ResolvePanelSize` 安全区钳制；字号 ≥18。
- [x] 新增 `Assets/_Project/Scripts/UI/MapSelectOverlay.cs`：sortingOrder 60；遮罩/面板/头部/两卡/页脚；
      当前卡「当前地图」、另一张「切换到此图」；点击 = TrySelect + LoadMap（失败告警不假装成功）；
      `IsOpen` / `MenuInputConsumedThisFrame` / `TryCloseFromMenuInput()`；安全区/Screen 变化重建。

## 3. 首页与输入

- [x] `Scripts/UI/MatchFlowUI.cs`：
      Index 2 换地图卡（Tag=MAP、Title=当前场景图名、Info=当前 + 另一张、Accent=当前图 Accent）；
      标题下右侧嵌 `MapPreviewBuilder`；`ClickAction=OpenMapSelect`；
      删除 `BuildKeysInfo()` / `_homeKeysInfo`，`RefreshHomeInfo` 改刷地图信息；
      `CanStartMatch` 增加 `MapSelectOverlay.IsOpen`；`TryStartMatch` 地图场景与选中不一致时先切图。
- [x] `Scripts/UI/Core/MenuInputRouter.cs`：`MapSelect=5`、`CloseMapSelect`、ResolveEscOwner 可选 `mapSelectOpen`、CanLayerHandle、ActionForOwner。
- [x] `Scripts/UI/Menus/PauseMenuOverlay.cs`：Esc 仲裁接入 MapSelect；设置页「画面与操作」新增第 5 行「操作说明」+「查看指南」→ `HomeInfoOverlay.Show(..., sortingOrder: 110)`。
- [x] `Scripts/UI/WeaponSelect/WeaponSelectView.cs`：MapSelect 打开时输入让路；`CloseMapSelect` 分支。
- [x] `Scripts/UI/HomeInfoOverlay.cs`：`Show` 增加可选 `sortingOrder = 50`。
- [x] `Scripts/UI/WeaponSelect/WeaponSelectBootstrap.cs`：修复跨场景重新订阅（同场景幂等、旧引用销毁可退订）。

## 4. 测试

- [x] 新增 `Assets/_Project/Tests/EditMode/MapCatalogTests.cs`：
      默认 PhaseA；非法 id 回退；TrySelect 写盘/读取；ResetForTests 清理；
      两张图路径均在 `EditorBuildSettings.scenes` 且 enabled；
      `MapSelectLayoutMath` 字号下限与安全区；
      id/路径/场景名唯一。
- [x] 如发现既有用例依赖「Index 2 = KEYS」，同步改为 MAP（先跑 `run_tests.sh fast` 找红）。

## 5. 验证

- [x] Unity CLI：`script.recompile_status` 干净；`console.get --level error` 无本任务报错。
- [x] 改后首页截图：
      `Logs/verify-map-select/home_after_phase_a.png`
      `Logs/verify-map-select/home_after_kelp.png`
      `Logs/verify-map-select/map_select_overlay.png`
- [x] 真实对局截图：
      `Logs/verify-map-select/match_phase_a.png`
      `Logs/verify-map-select/match_kelp.png`
      两张图画面差异可辨（中央方形高台 vs 中央长货台/信标）。
- [x] 持久化验证：打印 `PlayerPrefs.GetString("P1.Map.SelectedId")`；退出 Play 再进，首页仍是选中图；切回 PhaseA 复验。
- [x] `./tools/run_tests.sh fast` 全绿。
- [x] `python3 tools/font_charset.py` 更新账本；菜单 `P1/画风/漫画风/0` 重烘；字体门禁转绿。

## 6. 收尾

- [x] `CHANGELOG.md` 追加 `## 2026-11-04 #37`（#36 已被同日并行任务占用；只记根因/陷阱/架构变动）。
- [x] 输出中文 Markdown 报告：结论、改动文件清单、UI 与交互说明、验证结果与截图路径、残留风险。
- [x] 不执行 `git commit` / `git push`（除非用户另行明确要求）。
