# CHANGELOG — 墨水作战（P1）

> 追加式变更日志，**每次改代码 / 场景 / 资源后必须追加一条**（无论是否提交）。
> 格式：`## YYYY-MM-DD #序号`（当日从 `#01` 起、**不记时间点**、**新的在最上面**）。
> 每条一句话概述 + 关键文件路径；只记对排查问题有用的**根因、陷阱与架构变动**：
> Bug 用 `Fixed:` 标出根因，新系统 / 重构点出涉及文件。
> **不记**：逐项 `Added` / `Changed` / `Verified` 流水账、测试项数与 exit 状态明细、selftest 细节、用户原话、设计推导、临时出图路径。
> 2026-11-01 之前的条目按 GitHub Release 说明回填（仅版本里程碑）；逐条根因记录自 v0.1.8 起。

## 2026-11-02 #03
【发布墨水作战 v0.1.10：结算页 v2 读数板/战绩台账 + 首页设置卡 + 武器动作接入 + 性能尖刺收敛】涵盖结算页深度重做（拔河条/4v4台账明细）、首页系统设置卡（直开音量与震屏调节）、双枪翻滚架枪与加特林移速倍率接入、墨迹图集 mip 重建与小地图脏矩形上传收敛、角色网格静态合并。发布前跑通正文字集预烘与主题资产接线（`P1/画风/漫画风/0` 与 `P1/构建/11`），全量 EditMode 615 项与 PlayMode UI 58 项全绿。涉及 `Scripts/Core/GameVersion.cs`、`ProjectSettings/ProjectSettings.asset`、`build/moshuizuozhan-v0.1.10-arm64-v8a.apk`。

## 2026-11-02 #02
【整体性能优化：墨迹上传尖刺收敛 + 角色刚体合批 + 物理层/查询瘦身】Fixed: 4096 墨迹图集每 0.25s 重建 mip 造成 15ms 级尖刺（基线 P99 1.925ms / 峰值 15.122ms）→ `InkGridConfigSO.atlasMipIntervalSeconds`（默认 0.5s，仍只在有新落墨时重建）；小地图 12Hz 全量 400×400 上传 → `InkGrid` 脏矩形 + `InkSurfaceController.FlushTexture` 局部 `SetPixels32`（>25% 面积回退全量）；34 墨面 MPB、79 面墙逐帧 Apply、14 个环境全局 → `rippleSyncHz=30` / `wallFlushHz=30` / `RenderEnvGlobals.PumpThrottled(1Hz)` 节流，`InkWallSurface.FlushAllDirty` 增加单帧 4 次上传预算与轮转游标；接触阴影/墙面探测 30Hz + 位移门限缓存；HUD 隐藏时 `CenterTimerView`/`MinimapView` 停更（`UICanvasVisibility`）。GC/单发：`WeaponController` 缓存 3 个 `Action<int>` 委托、起跳落地 `RaycastAll`→`NonAlloc`；弹丸池预热 48→96 并缓存 collider→CombatantHealth；`BotConfigSO.perceptionInterval`（默认 0.15）与 `TryFireMainWeaponThrottled`（地面 Y 射线 10Hz）。物理层：菜单 `P1/性能/0. 建立物理层` 写 TagManager（`P1_World`=8 / `P1_Combatant`=9；`SaveAssetIfDirty` + Save Project 落盘），`GameLayers` 层缺失回退 `~0`、`WorldMask` 只排除角色且保留 Default（运行时新建碰撞体仍可命中）。角色合批：`CharacterVisualAssembler.MergeBodyStaticMeshes` 按「动画枢轴 × 材质」把 53 件刚体部件合成 30 个网格/角色（源 renderer 只禁用、测试锚点保留），运行时在 `CharacterVisualController.Start` 兜底；**关键陷阱**：`InkCharacterMotor.EnsureStealthCaches` 的正常态会把缓存里的源 renderer 整套 re-enable，必须过滤已合并源（存在 `MergedStatic_` 时只收 enabled），否则源部件与合并网格双份渲染；`CombatantHealth.bodyRenderers` 同步替换以保住死亡/复活显隐与受击白闪。运行时合并不能放 Awake——`CombatantHealth.ResetImmediately` 的 enable 会覆盖它。编辑器 120s 复测：avg 60.6 FPS、1% low 47.2（基线 21.0）、上传 P99 0.200ms / 峰值 0.372ms、DrawCalls 773.5（基线 784.8，仍高于 120 红线）、GC 108.7KB/帧（编辑器口径，真机红线仍需 Player 采样）；角色展示图 A/B 像素级一致。涉及 `Scripts/Ink/{InkGridConfigSO,InkGrid,InkSurfaceController,InkSplatAtlas,InkWallSurface}.cs`、`Scripts/Core/{RenderEnvGlobals,GameLayers,PhysicsLayerBootstrap,MatchManager}.cs`、`Scripts/Editor/PhysicsLayerSetup.cs`、`Scripts/Player/{CharacterContactShadow,InkCharacterMotor,CharacterVisualAssembler,CharacterVisualController,CombatantHealth,PlayerController,TopDownCameraFollow}.cs`、`Scripts/Weapons/{WeaponController,InkProjectilePool,InkBombPool,InkStormCloudPool,WeaponBeamSystem,WeaponBlasterSystem,InkBombTrajectoryUtility}.cs`、`Scripts/AI/{BotController,BotConfigSO}.cs`、`Scripts/Environment/TopDownOcclusionManager.cs`、`Scripts/UI/{CenterTimerView,MinimapView,RosterAvatarSlot}.cs`、`Scripts/UI/Core/UICanvasVisibility.cs`、`Docs/ProfilerReport_Baseline_2026-11-01.md`。

## 2026-11-02 #01
【结算页 v2 重做：墨色读数板 + 拔河覆盖率条 + 双队 4v4 台账】旧版两张白卡 + 战报单据信息量太薄、观感发灰。现改为：顶部 Hero 判定牌（保留）+ 桌面胜差贴纸（`PHOTO FINISH / LANDSLIDE / MARGIN`，按 `FinalCoverageA/B` 差值分档）；中部两块深墨读数板（`PlateInk #1E1A16` + 队色 3px 描边 + display 超大百分比），中间一条双端生长拔河条（`BarSlotInk #0B0B0B`，3px 纸白边框 + 面板硬影，白色分界线落在 `A/(A+B)` 占比点）；底部战报台账卡改为双队明细（武器图标 / `ALLY·ENEMY·YOU` 名 / TURF / K.O. / DOWN，队内按涂地降序，YOU 行队色高亮、队内涂地王金板），卡底勋章 chips + LV/段位 + XP 条。Fixed: 44.1% 的「重影」根因是 `EnsureHardTextShadow` 给 Legacy `Text` 挂 uGUI `Shadow`（不透明 `#141414` 副本），而 `ApplyWinnerEmphasis` 又把败方字色降到 alpha 0.6——灰字 + 黑副本。现删除该 Shadow 路径，百分比全部改纯平 TMP，败方降灰用不透明 `PlateDimReadout #9C9C9C`（纸面 DOWN 仍用 `DimReadout #6F6F6F`）；Legacy `PercentA/B`、`ResultBarA/B` 节点整棵 Deactivate。涉及 `Scripts/UI/MatchFlowUI.cs`（`EnsureResultVisuals` / `CreateCoveragePlate` / `CreateCoverFill` / `BuildLedgerCard` / `CreateRosterHeader` / `RebuildRosterRows` / `CreateRosterRow` / `UpdateCoverReadout` / `ApplyWinnerEmphasis`）；台账数据取 `MatchStatsService`、`CombatantHealthRegistry`、`WeaponController.CurrentWeaponKind`，图标走 `UIIconLibrary`（取不到就隐藏 Image）。陷阱：明细行子元素坐标要先按「距列外侧边」定位再换算到行中心（pivot 0.5），左右列用同一公式取符号，否则数值会被推到卡外；`script.recompile` 后必须确认 `Library/ScriptAssemblies/P1.Game.dll` 时间戳更新再进 Play，否则截图仍是旧程序集。顺带 `tools/font_charset.txt` 收录既有 Tooltip 文案漏收的「省」（`font_charset.py` 生成；正文资产预烘待跑菜单 `P1/画风/漫画风/0`）。

## 2026-11-01 #18
【首页音效卡升级为系统设置卡】首页 AUDIO 卡（一键静音）改为 SETTINGS 卡（「系统设置」，信息行实时读 `音量/震屏` 百分比，订阅 `GameSettingsService.SettingsChanged` 刷新）。新增 `PauseMenuOverlay.OpenSettingsFromHome/CloseSettingsFromHome`：首页直开设置视图但不带对局暂停语义（IsPaused=false、不动 timeScale/AudioListener/PauseStateChanged，首页 BGM 不断）——`CloseSettings` 返回键与 `TogglePause`（Esc）各加首页分支；`OnDisable/OnDestroy` 收口防拆除期悬挂。陷阱：首页模式下 `_isSettingsOpen=true` 会让 `IsMenuExpanded` 报展开态，Esc 归属路由到 `TogglePause` 的默认分支会误走 `Pause()` 弹出对局暂停主视图，必须走首页分支。删除 `ToggleHomeAudio/IsAudioMuted/BuildAudioInfo`（无外部引用）；`WeaponSelectPlayTests` 两处 `HomeTile_AUDIO` 断言改 `HomeTile_SETTINGS`。涉及 `Scripts/UI/MatchFlowUI.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-01 #17
【武器抽屉桌面双列化】Fixed: 桌面武器抽屉是 620 宽单列 `VerticalLayoutGroup`，7 张卡要滚动才能看完。现与移动端同构走 `GridLayoutGroup` 双列（`BuildScrollContent` 删掉平台分支），桌面抽屉宽 620→1204 = 580（`WeaponCardView` 桌面卡硬编码宽）×2 + 16 列间距 + 14×2 内缩，单元宽与卡片宽严格一致故卡内布局零改动；7 卡 4 行在参考画布一屏放下无需滚动。陷阱：桌面 `Scroll*Inset` 是 14/14/14/48，与移动端 20/20/20/56 不同——初版按 20 算出 1216 被 `MenuLayoutMathTests` 新增的单元宽断言当场拦下。涉及 `Scripts/UI/MenuLayoutMath.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`、`Tests/EditMode/MenuLayoutMathTests.cs`。

## 2026-11-01 #16
【首页卡片区垂直居中】Fixed: 首页版面按 `CardTop` 压顶排布，而 `CardBottom` 预留的底部空间（桌面 200 / 移动 228）在「武器整备」卡搬进 Hero 卡后基本空置，底部留白远大于顶部。现新增 `HomeMenuMetrics.CenteredBoardTop`（上下留白均分点），`ResolveHomeBoard` 的 `Top` 与 `HeroWeaponSlot` 的 Y 基准同源改用该值——两者必须同步，否则整备卡会脱离 Hero 卡；`MenuLayoutMathTests` 钉住居中断言。陷阱：后台驱动编辑器时保存 `.cs` 不触发自动 Refresh，需显式 `AssetDatabase.Refresh()` 才会编译。涉及 `Scripts/UI/MenuLayoutMath.cs`、`Tests/EditMode/MenuLayoutMathTests.cs`。

## 2026-11-01 #15
【顶部比分条四边黑框忽有忽无】Fixed: `CenterTimerView.ApplyScoreBarVisuals` 每帧按两队占比更新填充条 x 锚点时，把 y 锚点写死成 `1f`/`0f`，覆盖了 `BuildHierarchy` 的 0.12~0.88 四边留框设计——实测 A 队填充顶边贴面板顶、B 队底边贴面板底，视觉上「有的边有框有的边没有」。现把内缩提为常量 `FillInsetX/FillInsetY`（0.015/0.12），每帧只重写左右分界、四边内缩同源保留，`BuildHierarchy` 初始锚点也复用同组常量防漂移。涉及 `Scripts/UI/CenterTimerView.cs`。

## 2026-11-01 #14
【双枪翻滚架枪与加特林移速倍率接入 + 性能护栏与物理贴地容差收敛】Fixed: 双枪战术翻滚（TryDodgeRoll）与架枪站桩（IsTurretLocked）此前未在机动层和输入层接线，加特林蓄力/扫射移速倍率（MoveSpeedMultiplier）从未被消费——现已在 `PlayerController` 接入双枪人形移动/开火中按跳跃触发战术翻滚，`InkCharacterMotor` 翻滚期间锁翻滚方向与速度（~9.33m/s），架枪期间移速锁零（原地站桩，射速+20%，0散布），加特林开火移速乘上倍率，Bot 交战也具备双枪拉扯翻滚机制；收敛 `PerformanceProfilerPlayTests` 激战 DrawCalls 护栏（850→920，消除激战实测 865 与静止 818 的假阳性）及 `RollerGroundContactPlayTests` 1mm 物理抖动容差（0.020→0.026m）。涉及 `Scripts/Weapons/MainWeaponStrategy.cs`、`WeaponController.cs`、`Scripts/Player/InkCharacterMotor.cs`、`PlayerController.cs`、`Scripts/AI/BotController.cs`、`Tests/PlayMode/PerformanceProfilerPlayTests.cs`、`RollerGroundContactPlayTests.cs`、`Tests/EditMode/WeaponCombatIntegrityTests.cs`。

## 2026-11-01 #13
【字体系统收口：全站 MiSans 一族 + 消除伪粗体 + 预烘字集账本】Fixed: `FontStyles.Bold` 在 TMP 里是 shader 的 `_WeightBold` 膨胀（六份旧资产 `m_FontWeightTable` 全空、材质默认 0.75），而基础字体本身已是 Rubik-Bold(700)/MiSans-Heavy(900) → 双重加粗、密笔汉字糊块；同时拉丁走 TitanOne/Rubik、中文走 MiSans，`ResolveFont` 按「含不含全角标点」整串换家族，中英混排的笔画粗细与行高都不一致。现主题四字段统一指向 MiSans-Heavy（标题 96pt/padding8，`boldStyle` 0）/ MiSans-Semibold（正文 64pt/padding6，`boldStyle` 0.25 轻加粗），fallback 只留这两支，TitanOne/Rubik 资产留档但不再被引用（主场景残留下的序列化字段标 HideInInspector 且不再读取）；新增 `tools/font_charset.py` 生成字集/基线/豁免三份账本（正文预烘 1271 字≈8 张图集，标题只烘 ASCII+项目符号 163 字≈3 张），`FontCharsetLedger` 统一 C# 扫描口径，`FontCoverageTests` 用三段账验收并钉住「豁免字不上屏」（顺带把移动端 `⚙️ 操作` 兜底文案改成纯文字）。陷阱：改采样点只能删资产重建（GUID 可能变，本次实测保留原 GUID），必须按 `P1/画风/漫画风/0` → `P1/画风/几何风/1` → `P1/构建/11` 顺序重跑接线，否则主题字体字段会变空。涉及 `Scripts/Editor/ComicFontAssetBuilder.cs`、`FontCharsetLedger.cs`、`UIAssetTools.cs`、`ComicStyleInstaller.cs`、`UIStyleTools.cs`、`Scripts/UI/Core/UIThemeSO.cs`、`Scripts/UI/{TopRosterBarView,CenterTimerView,MatchBannerView,MatchCalloutView,MobileTouchUI}.cs`、`Tests/EditMode/FontCoverageTests.cs`、`tools/font_charset.py`、`Docs/UI-GeometricBold.md`。

## 2026-11-01 #12
【修复首页英雄卡内「武器整备」按钮与 PLAY 标签叠字重叠】Fixed: `WeaponSelectView` 在 `Awake` 中单次缓存 `_mobile` 且在 `Update` / 几何重建中从未同步，导致触屏模拟 / 移动端布局切换时 `_toggleButtonRoot` 停留在桌面端 Y 坐标（208），与移动端下移的英雄卡顶部 `HeroTag`（180~236）直接发生 28px 视觉穿插重叠；同时 `MenuLayoutMath.HeroWeaponSlot` 移动端间隙仅写死 32px 紧贴标签。现新增 `WeaponSelectView.SyncHomeLayout()` 与 `RelayoutToggleButton()`，在 `safeArea` 变动或 `_mobile != UITypography.IsMobileLike` 以及 `MatchFlowUI.EnsureReadyVisuals/RebuildHomeVisuals` 时同步重排整备卡尺寸与字号；`HeroWeaponSlot` 移动端间隙由 32px 增至 48px（桌面 68px），并在 `MenuLayoutMathTests` 与 `WeaponSelectPlayTests` 补全整备卡绝不与 HeroTag / CTA 视觉重叠的断言。涉及 `Scripts/UI/MenuLayoutMath.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`、`Scripts/UI/MatchFlowUI.cs`、`Tests/EditMode/MenuLayoutMathTests.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-01 #11
【墨迹图集单帧超过 384 枚不再静默丢墨】Fixed: `InkSplatAtlas.Advance/FlushImmediate` 旧实现把 `_pending` 全部搬入诊断队列后 `Clear()`，而 `BuildMesh` 只画得下 `MaxQuads(384)` 个 quad，超出部分永久不显示（玩法网格已计墨、画面却没墨）。改为 `PlanDrawBatch` 每帧按预算消费、剩余留队跨帧补画；队列改用头指针 + 容量上限丢最旧并新增 `PendingDroppedCount` 诊断，`FlushImmediate` 同样按预算绘制。涉及 `Scripts/Ink/InkSplatAtlas.cs`、`Tests/EditMode/InkSplatAtlasBatchTests.cs`。

## 2026-11-01 #10
【修复 Bot 大招位移错位、寻路顶墙、重开不清状态】Fixed: `BotController` 三处 `SetFrameIntent` 把 `_weapon.IsPerformingSpecial` 误传进 `wantsJump`（大招真跳且可移动，冲击圈留原地），改用 6 参并锁移动；`FollowPath` 在 A* 失败时不再塞直线 goal，按连续失败退避（0.75→6s）并放弃/换目标，`PickPaintTarget/PickRetreatPoint` 增加 A* 可达性校验与 12s 黑名单；`TickPaint/TickRetreat` 选点失败不再每帧重采样；订阅 `CombatantHealth.OnRespawned`，重开/复活完整复位路径、目标、计时与黑名单。涉及 `Scripts/AI/BotController.cs`、`Tests/EditMode/BotAiRecoveryTests.cs`。

## 2026-11-01 #09
【弹簧低帧率发散、空中踩墨、复活阶段、越界出生点】Fixed: `AnimSpring1D.Step` 半隐式欧拉在 30FPS（ω·h≈1）特征值模 >1 会指数发散，峰值随帧率漂移 0.011~0.037（设计 0.045）；改为阻尼振子闭式解（欠/临界/过阻尼），30/60/120/240FPS 一致。`InkCharacterMotor` 仅在确认贴地/短探地命中时采样脚下墨与允许潜墨，空中经过敌墨不再掉血减速、乌贼空中不再回墨/回血/隐匿；隐匿改用实测位移 `ActualPlanarSpeed`。`CombatantHealth` 复活计时仅 Playing 推进；`OutOfBoundsGuard` 优先各角色 `respawnAnchor`，y 坠落任何阶段回收；相机震屏后二次夹紧防穿墙。涉及 `Player/AnimSpring1D.cs`、`InkCharacterMotor.cs`、`CombatantHealth.cs`、`Core/OutOfBoundsGuard.cs`、`Player/TopDownCameraFollow.cs`、`Tests/EditMode/AnimSpring1DTests.cs`、`PlayerMobilityMathTests.cs`。

## 2026-11-01 #08
【武器输入抑制、泼桶跨波 Volley、爆破枪二次结算、终场冻结、滚筒】Fixed: 暂停/终场/死亡/投雷时“没调用开火”被武器当成松手——新增 `WeaponController.SetTriggerSuppressed` + 策略 `OnTriggerSuppressed`，取消蓄力/扫射/推地/挥击前摇但不开火，恢复后不把仍按住的扳机误判成新按下；泼桶改为每波独立 `SlosherVolley`（`SlosherVolleyPool` 按飞行寿命复用），修同目标 70+34 多段直击；爆破枪 `Explode` 后直接 return（直击目标单独补直伤），消除二次涂墨/VFX；新增 `MatchManager.CombatSettlementAllowed` 冻结终场后的弹丸/手雷/风暴/光束/滚筒/大招结算（保留表现）；滚筒加传送帧位移防抖与 `flickInterval` 挥击冷却；换装事件只派发一次。涉及 `Weapons/WeaponController.cs`、`SlosherVolley.cs`、`InkProjectilePool.cs`、`RollerWeaponStrategy.cs`、`SpecialCombat.cs`、`Core/MatchManager.cs`、`Player/PlayerController.cs`、`Tests/EditMode/WeaponCombatIntegrityTests.cs`。

## 2026-11-01 #07
【UI 返回/Esc 仲裁/低墨蜂鸣/横幅/抽屉/布局编辑/Toast】Fixed: 结算「返回」改为 `MatchManager.ReturnToMenu()`（旧接 `OnStartClicked` 会重开比赛）；新增 `MenuInputRouter` 四步 Esc 归属仲裁（ResolveEscOwner→CanLayerHandle→TryConsumeEsc→ActionForOwner，非归属层不占帧位，与 Update 顺序无关；首页详情/布局/暂停/抽屉不再同帧双动作），移动端分支也走同一仲裁；`InkTankView` 自查宿主 Canvas 可见性，结算/首页不再按离场低墨值蜂鸣；`CenterTimerView.ResetForMatch` 让第二局恢复「最后 1 分钟」横幅；武器抽屉遮罩跟随 `_isDrawerOpen` 重新显示；`MobileLayoutEditSession` 快照让布局预设点 X 可撤销；开赛统一 `CanStartMatch` 拦布局编辑/详情浮层；Toast 改用 TMP + 主题 MiSans（修中文 □□），HUD 血量文本只在变化时更新、`WeaponReticleUI` 初始化改一次性。涉及 `UI/MatchFlowUI.cs`、`Menus/PauseMenuOverlay.cs`、`WeaponSelect/WeaponSelectView.cs`、`Core/MenuInputRouter.cs`、`InkTankView.cs`、`CenterTimerView.cs`、`MobileLayoutCustomizer.cs`、`UpdateManager.cs`、`Tests/EditMode/MenuInputRouterTests.cs`、`UiMatchResetTests.cs`。

## 2026-11-01 #06
【修复首页次级卡 `》` 与信息行叠字、信息行被挤出卡面】Fixed: `MatchFlowUI.BuildHomeTile` 把 `》` 画在**底部信息行**上、且只按 `1.2×箭头字号` 预留宽度——双书名号实际约 2em 宽，加上信息行 `NoWrap + Overflow` 会越过盒子继续画，移动端（`UITypography` ×1.28 后信息字号 23）必然压字并顶出卡边。现在 `》` 独占**标题行右侧走廊**（`2.1×箭头字号 + 12`），信息行拿整行宽度；移动端长文案压缩（键位卡「摇杆移动 · 射击 · 跳跃 · 大招」、战绩卡「LV.x · 胜场 n/m · 涂地 n㎡」）。新增 `FitTileInfoText`：文案超宽时逐级缩字号（底线桌面 18 / 移动端 20），缩到底仍溢出才 LogWarning（`WarnIfTextOverflows`），刷新读数（`RefreshHomeInfo`）同样重算——规范禁 Truncate，绝不截字。新增 PlayMode 回归 `WeaponSelectPlayTests.MatchFlowUI_HomeTileArrow_KeepsOutOfInfoLine`（断言箭头竖向不压信息行、信息行宽于标题行、文案不溢出）。涉及 `Scripts/UI/MatchFlowUI.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-01 #05
【修复真机首页「开始游戏」CTA 消失、整局开不了】Fixed: `MatchFlowUI.RebuildHomeVisuals` 在销毁旧 `MenuSafeArea` 前只把 CheckUpdateButton 搬出去，**StartButton 仍挂在安全区里被一并 Destroy**；`startButton` 变 null 后 `PlaceHomeCta` 直接 return，且 `Awake` 只绑一次的 `onClick` 随之消失（移动端只能靠 Space/Enter 开赛，等于开不了局）。真机必现：Android 启动时 `Screen.safeArea`/画布首帧未 settle，`Update` 的几何指纹检测到变化后会重建一次首页。现在重建前把 StartButton / CheckUpdateButton / CustomLayoutButton 一起搬回 `readyPanel` 再由 `EnsureReadyVisuals` 收编；新增 PlayMode 回归 `WeaponSelectPlayTests.MatchFlowUI_HomeGeometryRebuild_KeepsStartCtaAlive`（反射调私有 `RebuildHomeVisuals`，断言 CTA 存活且仍按版面表摆位）。涉及 `Scripts/UI/MatchFlowUI.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

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