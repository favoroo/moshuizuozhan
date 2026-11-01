# CHANGELOG — 墨水作战（P1）

> 追加式变更日志，**每次改代码 / 场景 / 资源后必须追加一条**（无论是否提交）。
> 格式：`## YYYY-MM-DD #序号`（当日从 `#01` 起、**不记时间点**、**新的在最上面**）。
> 每条一句话概述 + 关键文件路径；只记对排查问题有用的**根因、陷阱与架构变动**：
> Bug 用 `Fixed:` 标出根因，新系统 / 重构点出涉及文件。
> **不记**：逐项 `Added` / `Changed` / `Verified` 流水账、测试项数与 exit 状态明细、selftest 细节、用户原话、设计推导、临时出图路径。
> 2026-11-01 之前的条目按 GitHub Release 说明回填（仅版本里程碑）；逐条根因记录自 v0.1.8 起。

## 2026-11-01 #04
【发布 v0.1.9：武器涂墙计大招充能 + 首页信息卡详情浮层】Fixed: `MatchFlowUI.RebuildHomeVisuals` 重建后旧 MenuSafeArea 空壳不销毁，按名查找命中空容器导致 `WeaponSelectPlayTests` 安全区断言红（随 HomeInfo 几何重建引入，旧节点改名 `_Old` + Destroy 退役）。性能收敛：墙面涂墨改为每表面 1 合并网格 + 1 条带图集 + 1 渲染器（`InkWallSurface.BuildMergedWallVisual`，UV0 映射图集页、shader 零改动，`InkWallFace` 不再自建贴图、新增 `UploadIfDirtyTo`，战斗激活渲染器 316→~80）；武器静态合并修复——`_mergedMeshCache` 把瞬时失败缓存成 null 后永不重试（自 v0.1.3 静默失效，场景从未烘焙出 MergedStatic），改为失败组不落缓存 + `MergeWeaponRoot` 幂等守卫 + `CharacterVisualController.Awake` 调 `MergeWeaponsRuntime` 运行时兜底（静态网格跨 8 角色共享）。DrawCalls 护栏 800→850：逐组关闭实验证明 ~818 为场景结构性地板（8 角色 × ~73 身体部件逐件渲染 ~584 + 竞技场 ~114 + 已合并武器 ~40，UI/墙面 ≈ 0，SceneView 可见时再 +47），旧注释「450-520」与自身场景矛盾无法复现；按骨骼分组合并身体部件留待下版。已知债：`WeaponFeelTests.SpringImpulse` 在 v0.1.8 即红（`AnimSpring1D` 半隐式欧拉子步进 ω·h 过粗，冲量峰值仅为解析值 24~64%，修复需全局重调手感）。另有 GUI 编辑器后台不自动 Refresh 的坑：`script.recompile_status` 报「成功」可能只是上次结果，验证编译必须核对 `Library/ScriptAssemblies/*.dll` 时间戳晚于源码修改时间。涉及 `Scripts/Ink/InkWallSurface.cs`、`InkWallFace.cs`、`Scripts/Player/CharacterVisualAssembler.cs`、`CharacterVisualController.cs`、`Scripts/UI/MatchFlowUI.cs`、`Tests/PlayMode/PerformanceProfilerPlayTests.cs`；版本号落在 `Scripts/Core/GameVersion.cs`（0.1.9）。

## 2026-11-01 #03
【首页「战绩 / 键位说明」信息卡改为可点并新增详情浮层】原先两张卡是 `Interactive = false` 的纯信息卡，点了没反应；现在打开独立 Overlay Canvas（sortingOrder 50，必须高于武器抽屉 15，否则左下整备卡浮在遮罩之上）的几何风浮层。新增 `Scripts/UI/HomeInfoOverlay.cs`、`HomeInfoLayoutMath.cs`、`HomeInfoContent.cs`（键位表唯一数据源，暂停菜单 `BuildControlsCard` 改用它），`MatchFlowUI` 两张卡接点击 + Space/Enter 开赛门禁 + 阶段切换关闭；`PauseMenuOverlay` / `WeaponSelectView` 用 `HomeInfoOverlay.TryCloseFromMenuInput()` 的「同帧已消费」标记保证一个 Esc 只关浮层、不会顺带打开暂停或武器抽屉。

## 2026-11-01 #02
【AGENTS.md 改为「项目导览」并建立 CHANGELOG 规范】去掉已结项的计划看板说明，把「项目简介 / 快速定位 / 目录结构 / 硬性规则 / 并行协作 / 验证 / 素材 / 变更日志 / 坑」收成一份可直接定位文件的入口；新增本文件与「每次改动必须追加」约定。涉及 `AGENTS.md`、`CHANGELOG.md`（新）。

## 2026-11-01 #01
【发布 v0.1.8：结算勋章与段位成长 + 战况大字播报 + 角色动作/武器手感重做 + 码头光影精修】同时修复手机端丢雷与 Slam 无伤害。涉及 `Scripts/UI/Core/MatchAwards.cs`、`Scripts/UI/KillFeedView.cs`、`Scripts/UI/MatchCalloutView.cs`、`Scripts/Player/CharacterVisualController.cs`、`Scripts/Weapons/WeaponFeelProfile.cs`、`Scripts/Environment/ArenaStyleSO.cs`；版本号落在 `Scripts/Core/GameVersion.cs`（0.1.8）。

## 2026-10-31 #01
【发布 v0.1.7：手雷按住瞄准松手投出 + 击杀闪光/阵亡转场/踩敌墨警告 + 角色圆润造型与双腿步态】涉及 `Scripts/Player/SubBombThrowArbiter.cs`、`Scripts/Player/SubWeaponThrowTrajectory.cs`、`Scripts/UI/InkEdgeWarningView.cs`、`Scripts/Player/KidGaitSolver.cs`、`Scripts/Player/CharacterVisualController.cs`。

## 2026-10-30 #04
【发布 v0.1.6：UI 几何大胆风重构 + 射击涂地手感重做 + 复活 3 秒无敌】涉及 `Docs/UI-GeometricBold.md`、`Scripts/UI/Core/*`、`Scripts/Weapons/*`、`Scripts/Player/CombatantHealth.cs`。

## 2026-10-30 #03
【发布 v0.1.5：移动端 2/3/4 指方案与自由布局 + 竞技场碰撞收敛】同轮修复栏杆/道具卡人、打不上墨、掩体背光死黑。涉及 `Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/MobileLayoutCustomizer.cs`、`Scripts/Player/InkCharacterMotor.cs`、`Scripts/Environment/ArenaGeometryBuilder.cs`。

## 2026-10-30 #02
【发布 v0.1.4：修复地面墨迹不显示、移动端按钮失灵、墨水罐全黑】同轮修发布脚本：Gitee 附件清理按 `attach_files` 查 id、`http_json` 支持 3xx 重定向、版本预检防旧包白跑、长任务心跳与推送/上传并行。涉及 `Scripts/Ink/*`、`Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/InkTankView.cs`、`.agents/skills/p1-release/release.py`。

## 2026-10-30 #01
【发布 v0.1.3：修复移动端触控层遮挡首屏与结算界面、战前选枪按钮无法点击】涉及 `Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/MatchFlowUI.cs`、`Scripts/UI/WeaponSelect/*`。

## 2026-10-29 #03
【发布 v0.1.2：触控层遮挡首屏/结算与选枪卡片点不动的修复版】涉及 `Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/WeaponSelect/*`。

## 2026-10-29 #02
【发布 v0.1.1：第三人称视角重构 + 战前 7 武器选择/配装 + 暂停设置 + 竞技场美术升级】角色改蛋形并补武器/潜墨音效。涉及 `Scripts/Player/TopDownCameraFollow.cs`、`Scripts/UI/WeaponSelect/*`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Scripts/Environment/ArenaGeometryBuilder.cs`、`Scripts/Core/AudioManager.cs`。

## 2026-10-29 #01
【发布 v0.1.0：首个可玩版本（4v4 涂地）+ 游戏内检查更新】建立 GitHub / Gitee 双源发布链。涉及 `Scripts/UI/UpdateManager.cs`、`Scripts/UI/UpdateDialog.cs`、`Scripts/Core/GameVersion.cs`、`.agents/skills/p1-release/`。