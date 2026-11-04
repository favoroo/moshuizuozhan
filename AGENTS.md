# AGENTS.md — P1 项目开发指南

> 供 AI 开发 Agent 阅读。具体操作步骤（Unity CLI 全量命令、菜单/批处理、测试与环境坑、素材落地、并行冲突细则）见技能 **`p1-dev-workflow`**，本文不抄录。
> **红线：除非用户明确要求修改本文件，否则严禁擅自修改 AGENTS.md。**
> 改完任何代码 / 场景 / 资源后，按文末《变更日志》追加一条到 [CHANGELOG.md](CHANGELOG.md)。

## 项目简介

Unity 第三人称 3D「涂地对战」游戏（玩法参考 Splatoon 涂地模式）：用墨水涂满地面，结算时覆盖率高的一方获胜。

- **对局**：单机 4v4（1 名本地玩家 + 7 名 Bot），不联网；默认 2 分钟一局，结算比覆盖率
- **引擎**：Unity `6000.6.7f1` / URP 17 / 新版 Input System；Linear 色彩空间，1 unit = 1 米
- **平台**：桌面（macOS / Windows）+ Android（横屏 / minSdk 26 / ARM64），双端均已出包
- **美术**：程序化几何体 + 程序化 UI + 本地合成音频，不依赖第三方美术资源；三渲二 + 卡通描边
- **核心循环**：战前选枪 → 涂地 / 交火（在己方墨里潜行回墨回血，踩敌墨掉血）→ 按涂地面积攒大招 → 覆盖率结算
- **已落地**：7 类主武器（Shooter / Roller / Charger / Dualies / Slosher / Blaster / Splatling）+ 2 大招（Slam / Storm）+ 副武器、4v4 Bot、40×56m 集装箱码头竞技场、几何大胆风 HUD、移动端触屏与布局自定义、暂停 / 设置、游戏内检查更新
- **版本**：唯一来源 [GameVersion.cs](Assets/_Project/Scripts/Core/GameVersion.cs) 的 `APP_VERSION`（须与 `PlayerSettings.bundleVersion` 一致）；发版走技能 `p1-release`
- **主场景**：[PhaseA_Playbox.unity](Assets/_Project/Scenes/PhaseA_Playbox.unity)（**禁止手改 `.unity`**，用 Editor 菜单重建）

## 快速定位

先看这张表，再去对应目录；比全库搜索快。

| 要做什么 | 去哪里看 | 注意 |
|---|---|---|
| 第一次接触项目 | 本文件 + 技能 `p1-dev-workflow` | 先读项目简介、目录结构、硬性规则 |
| 查历史改动 / 踩过的坑 | [CHANGELOG.md](CHANGELOG.md) | 追加式；新条目在最上面 |
| 改 UI 外观 / 布局 / 动效 | [Docs/UI-GeometricBold.md](Docs/UI-GeometricBold.md) | 唯一 UI 风格规范，改前必读 |
| 驱动编辑器 / 跑测试 / 出包 | 技能 `p1-dev-workflow` | 菜单、CLI、环境坑都在这里 |
| 发版 / 打 Tag / Release | 技能 `p1-release` | 未经用户明确指示不得发版 |
| 涂地规则 / 覆盖率 / 网格 | `Ink/InkGridConfigSO`、`Ink/InkSurfaceController` | 每帧最多上传一次纹理 |
| 油漆观感 / 湿润 / 凹凸 | `Shaders/InkGround.shader`、`InkWall.shader` | 材质在 `Materials/`，不手改 `.mat` YAML |
| 整体三渲二 / 描边 / 光照 / 质感 | `Shaders/ToonLighting.hlsl`、`ToonOutline.shader`、`Editor/ToonStyleInstaller.cs`、`Environment/ArenaStyleSO` | 画风 `P1/画风/1`；质感增强 `P1/画风/4`；全屏描边开关 `P1/画风/2` |
| 武器数值 / 新武器 | `Weapons/WeaponConfigSO` + `*WeaponStrategy` + `WeaponStrategyFactory` | 外部只经 `WeaponController` 公共 API |
| 大招充能节奏 | `Weapons/WeaponConfigSO.specialPointsPerSquareMeter`（按武器可调） | 按涂地**面积**计价，与墨格分辨率无关；`AddSpecialPoints` 旧签名保留 |
| 移动 / 潜墨 / 踩墨 / 生命 | `Player/InkCharacterMotor`、`PlayerConfigSO`、`Player/CombatantHealth` | 60 HP 踩墨保护底线不可破 |
| 角色外观 / 程序化装配 | `Player/CharacterVisualAssembler`、`CharacterVisualController`、`Player/CharacterMetrics` | 角色由代码装配；菜单 `P1/角色与武器/…` |
| 角色步态 / 跳跃 / 待机动画 | `Player/KidGaitSolver` + `Player/CharacterAnimConfigSO`（菜单 `P1/角色动画/0` 生成配置） | 调参只动 `CharacterAnimConfig.asset` 的 Gait/Air/Lean/Idle 四组；`Sample/SampleAir` 旧签名保留 |
| 比赛流程 / 时长 / 结算 | `Core/MatchManager`、`Core/GameConfigSO` | 阶段事件 `PhaseChanged` 被 UI/音频订阅 |
| Bot 行为 | `AI/BotController`、`AI/BotConfigSO`、`AI/NavGrid`、`AI/BotTactics` | 寻路按墨水加权，撤退不得朝敌 |
| HUD / UI 样式 | `UI/HudController` + 对应 View；`UIThemeSO`、`HudGaugeStyleSO`、`UI/Core/ComicStyle` | 先读 `Docs/UI-GeometricBold.md`；运行时建 Graphic 必须补 `CanvasRenderer`，已有 Image 的物体不能 `AddComponent<UIInkShapeGraphic>`（用 `CreateChildBackground`） |
| 战前选枪 / 配装 | `UI/WeaponSelect/WeaponSelectBootstrap`、`WeaponSelectView` | 选择存 PlayerPrefs；独立包依赖 `P1RuntimeAssets` |
| 暂停 / 设置 / 震屏 | `UI/Menus/PauseMenuOverlay`、`Core/GameSettingsService` | 暂停用 `Time.timeScale=0`，与 Hitstop 协调 |
| 移动端触屏 / 布局自定义 | `UI/MobileTouchUI`、`UI/MobileLayoutCustomizer`（Presets/Storage 走 PlayerPrefs） | 预览用菜单 `P1/移动端/…`；改前读 `Docs/UI-GeometricBold.md` 触控章节 |
| 游戏内检查更新 / 版本 | `UI/UpdateManager`、`UI/UpdateDialog`、`Core/GameVersion` | Android 更新安装清单校验走菜单 `P1/构建/12` |
| 音效 / 循环音 | `Core/AudioManager`、`Weapons/WeaponAudioDriver`、`Player/MovementAudioDriver` | 新增音效后必须重建运行时资源库 |
| 图标 / 音效 / 主题打包 | `Resources/P1RuntimeAssets.asset` + 菜单 `P1/构建/11` | 生成资产，不手改 `.asset` |
| 地图 / 掩体 / 遮挡 / 场地坐标 | `Environment/ArenaLayoutV2`（40×56 单一数据源）、`ArenaGeometryBuilder`（菜单 `P1/关卡与地图/…`） | 改布局坐标只改 `ArenaLayoutV2`；重建会重写场景，先确认无人在 Play |
| 场景装配 | `Editor/ProjectScaffolder`（菜单 `P1/构建/…`） | **禁止手改 `.unity`** |
| 性能采样 / 渲染统计 | `Editor/BuildAndProfilerSuite`、`Core/PerformanceProfilerProbe` | 菜单 `P1/性能/…`；红线取证必须走 Player 包 |
| 图标 / 字体 / 音效 / BGM | `MediaRaw/` 原稿 + `tools/bake_*.py`，落地 `Art / Audio` | 落地后必须 Refresh + 体检 |
| UI 图标运行时查找 | `UI/Core/UIIconLibrary` + `P1RuntimeAssets.uiIcons` | 取不到必须隐藏 Image；新增图标后跑菜单 `P1/构建/11` |
| 中文字体 | `Art/Fonts/MiSans-*.ttf` + 菜单 `P1/画风/漫画风/0` | 项目原字体无 CJK；缺字会显示 □，菜单 0 会自检 1200+ 汉字 |

## 目录结构

```
Assets/_Project/
  Scripts/
    Core/        比赛流程 MatchManager、配置 SO、AudioManager、VfxPool、Hitstop、性能探针、运行时资源库、画质环境全局推送（RenderEnvGlobals）
    Ink/         CPU 墨格 InkGrid + GPU 墨迹图集 InkSplatAtlas；墙面涂墨 InkWallFace/InkWallSurface；覆盖率审计 PaintCoverageAudit
    Player/      人/乌贼双形态机动 InkCharacterMotor、生命 CombatantHealth、第三人称相机 TopDownCameraFollow、地面准星、输入仲裁、
                 程序化角色 CharacterVisualAssembler/CharacterVisualController、真骨骼步态 KidGaitSolver、机动音频
    Weapons/     7 武器策略（Strategy）+ 弹道与手感配置（InkBallistics、WeaponFeelProfile）+ 弹丸/光束/滚筒/炸弹/风暴云对象池；
                 WeaponController 为对外门面
    AI/          Bot 状态机 BotController、NavGrid 寻路、类人瞄准弹簧 HumanAimSpring、战术决策 BotTactics
    Environment/ 竞技场几何生成 ArenaGeometryBuilder、布局单一数据源 ArenaLayoutV2（40×56）、场地风格 ArenaStyleSO、
                 第三人称遮挡 Dither 淡出（OcclusionFader / TopDownOcclusionManager）
    UI/          HUD 视图：计时器、比分、墨水/大招条、小地图、击杀卡、战况条、墨渍特效（镜片 LensInk / 转场 ScreenInkTransition /
                 踩墨警告 InkEdgeWarning）、移动端触屏与布局自定义、检查更新弹窗
    UI/Core/     UI 基元：直角色块、硬切动效、主题 SO、纯色精灵（UIFlatSprite）、图标库 UIIconLibrary
    UI/WeaponSelect/ 战前 7 武器选择/配装抽屉（运行时装配）
    UI/Menus/    暂停、设置、无障碍震屏（运行时装配）
    Editor/      场景生成、素材导入/体检、UI/构建/性能菜单（仅编辑器）
  Shaders/       InkGround / InkWall（含 InkWetShading、InkSurfaceOverlay hlsl）、ObstacleDitherLit / PropLit / CharacterToon* /
                 SkyGradient / SpawnPad / Water / DecalUnlit / ToonOutline
  Materials/  Prefabs/(基本空置，角色/UI 均运行时装配)
  Resources/(特效 Shader 兜底 + P1RuntimeAssets + ContactShadow)
  ScriptableObjects/(调参入口，含 Weapons/、ArenaStyle、CharacterAnimConfig)
  Scenes/(PhaseA_Playbox 主场景, UIDemo, ComicGallery)  Settings/(URP)
  Art/(Fonts, Sprites/Icons, Textures/Arena|Ink|Surfaces, Meshes/Arena)  Audio/(Music, SFX, Voice)
  Tests/(EditMode, PlayMode)
Docs/            规范、评审与验收存档（唯一必读：UI-GeometricBold.md）
MediaRaw/        素材原稿区（Assets 之外，Unity 不扫描）
tools/           bake_sfx.py / bake_bgm.py / bake_icons.py / bake_fonts.py / bake_arena_textures.py / bake_prop_surfaces.py
                 （本地零配额烘焙）、run_tests.sh
.agents/skills/  p1-dev-workflow | media-gen | p1-release
inkwave-aah-research/  竞品（INKWAVE）逆向资料，只读参考
```

程序集：`P1.Game`（运行时）/ `P1.Editor`（仅编辑器）/ `P1.Tests.EditMode` / `P1.Tests.PlayMode`；
命名空间基本随目录：`P1.Core` `P1.Ink` `P1.Player` `P1.Weapons` `P1.AI` `P1.Environment` `P1.UI` `P1.Editor`。

## 硬性规则

1. **不手改 `.unity` / `.prefab` / `.asset` / `.meta` 的 YAML**：场景与预制体用 Editor 脚本
   （`Scripts/Editor/` 的 `[MenuItem]`）或 Unity CLI 生成；素材导入设置改 `MediaImportPostprocessor.cs`。
2. 不修改 `Library/`、`Temp/`、`Logs/`、`UserSettings/`；`Builds/`、`build/`、`_backup_Unity7/` 是产物/备份，不手改。
3. 新增 Package 前先说明理由并等待确认。
4. 所有可调参数走 `ScriptableObject`，不在 MonoBehaviour 里硬编码数值；默认实例已挂在 `ScriptableObjects/*.asset`。
5. 一次只动一个系统：不跨系统大重构，不顺手改无关文件。
6. 删除或覆盖已有文件前，先 `Read` 确认内容。
7. 不引入第三方美术资源或付费插件，除非明确要求。
8. **未经用户明确指示，禁止 `git commit` / `git push` / 打 Tag / 发 Release**（发版流程见 `p1-release`）。
9. **版本号唯一来源**：`Scripts/Core/GameVersion.cs` 的 `APP_VERSION`，须与 `PlayerSettings.bundleVersion` 一致。
10. **多 Agent 并行防冲突**：本项目由多个 Agent 并行/交替开发，必须遵守下节。
11. **未经用户明确要求，不修改 `AGENTS.md` 本身**：它是所有 Agent 的共同入口，改动会影响所有并行任务。
12. **每次改动必须追加 `CHANGELOG.md`**：格式与取舍见文末《变更日志》。

## 多 Agent 并行协作（防冲突）

1. **永远假设有人在并发修改**：写文件前必须重新 `Read` 最新内容，禁止凭会话记忆盲写；只动自己负责的系统。
2. **公共接口向后兼容**：改 `InkCharacterMotor` / `CombatantHealth` / `WeaponController` /
   `InkSurfaceController` / `MatchManager` / `TopDownCameraFollow` 等跨系统组件时，用**重载 + 默认参数**增量扩展，
   保留原有签名，**禁止破坏性重命名/删参**。
3. **Unity 编辑器是单例共享资源**：保存 `.cs` 会触发 Domain Reload，打断别人的 PlayMode / 测试 /
   `script.eval`。改完立即检查 `script.recompile_status` + `console.get`；编译报错先看文件名是否属于自己，
   不要盲目回滚他人代码。
4. **不碰编辑器锁**：禁止删除 `Temp/UnityLockfile`，禁止 kill 正在运行的编辑器。
5. **防止场景/配置被冲刷**：未经本轮任务明确要求，不运行
   `P1/构建/2. 一键生成并打开第一阶段试玩场景 (Phase A Playbox)`（会重写主场景）；
   `ScriptableObjects/*.asset` 只精确修改自己负责的字段，不整文件重置。
6. **领域锁**：Shader/VFX、Combat/Weapons、Player/Motor、UI/HUD、AI/Nav、Scaffolder、Audio、Tests
   各域互不越界；`ProjectScaffolder.cs` 同一时间只允许一个 Agent 修改。
7. **交付时说明影响面**：改动文件、新增/调整接口、是否保持向后兼容、验证结果。
8. **`CHANGELOG.md` 为追加式**：往上加即可，别重排/删除他人条目，冲突面小。

## 快速验证

```bash
U="$HOME/.unity/bin/unity"
"$U" status                                   # 编辑器是否 ready（端口 + 项目）
"$U" command script.recompile_status          # 编译是否失败
"$U" command console.get --tail 50 --level error
"$U" command screenshot --view game --output Logs/shot.png
```

- 代码改完必须编译干净：`compilationFailed: false` 且无 `error CS`；不以「warning 而已」交付。
- 测试统一走 **`tools/run_tests.sh`**（编译预检 + 清理残留运行 + 失败摘要，避免并行编译期误报）：
  `./tools/run_tests.sh fast`（改 UI 首选，EditMode + PlayMode UI 子集）、`ui`、`play`、`edit`、`all`。
  底层接口仍是 `"$U" command test run --mode …`；**PlayMode 会把任何运行时 log error 判为失败**
  （抓 `ParticleSystem`、UI、池等隐性问题最有效）。
  基线：约 EditMode 350+ / PlayMode 100（2026-10-31 快照，并行提交会小幅浮动，精确数以 `tools/run_tests.sh all` 为准），
  新改动不得让既有用例转红。已知假阴性：`InkGroundShader_ContainsBothAtlasWritePasses` 在 batchmode 判红
  （`Material.FindPass` 返回 -1；GUI 编辑器内为绿），判回归前先与干净基线（stash 对照）比。
- 纯逻辑测试放 `Tests/EditMode`（`XxxTests.cs`）；涉及 MonoBehaviour 生命周期放 `Tests/PlayMode`。
- **UI 截图**：`screenshot --view game` 只拍相机，不含 ScreenSpaceOverlay 的 uGUI；
  请用 `"$U" command script.eval --code 'UnityEngine.ScreenCapture.CaptureScreenshot("Logs/ui.png");'`。
- GUI 编辑器主线程可能停止 tick（`console.status` 的 `ageMs` 持续增长）：不要重试，改用批处理（见技能）。
- Agent 看不见画面：关键数值用 `Debug.Log`（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）并读回控制台；
  视觉用截图自查。调试面板 `F12` 切换，默认关闭。
- 性能红线（改动涉及战斗/涂地/UI 时自查）：桌面 60 FPS、Android 30+ FPS；战斗热路径零持续 GC；
  `InkSurfaceController` 纹理上传 < 1.5 ms 无尖刺；DrawCalls < 120（桌面）/ 150（移动）。
  采样入口：菜单 `P1/性能/1. 执行 120 秒战斗真实 Profiler 采样 (P6-C)`（真实 PlayMode 逐帧实测，需编辑器空闲；
  旧「仿真采样」及其报告数据已作废）。红线取证必须走 Player 包：`P1/性能/5` 出桌面 Development 包，或向
  `persistentDataPath` 放 `P1_AutoProfile.request` 触发真机自动采样（见 `P1.Core.AutoProfileRuntimeService`）。
- 出包：菜单 `P1/构建/10. 一键全平台自动化出包与验收 (P6-C)` → `Builds/StandaloneOSX` / `Builds/Android`；
  正式发版走 `python3 .agents/skills/p1-release/release.py`（版本递增 + 双端 Release）。
- **新增/替换 SFX、武器图标或 UI 主题后**：先运行 `P1/构建/11. 生成运行时资源库 (P1RuntimeAssets)`，
  否则独立包会静音/缺图标（编辑器内仍能靠 `AssetDatabase` 兜底，容易漏检）。
- **改 UI 外观前先跑** `P1/画风/几何风/2. 生成样式画廊场景`，在 `Scenes/ComicGallery.unity` 里确认
  面板/直角/纯色块/字体正常，再改真实界面；主题数值用菜单 `P1/画风/几何风/1` 写入，不手改 `.asset`。
- **同一物体已有 Image 时禁止 `AddComponent<UIInkShapeGraphic>()`**：Unity 的 `Graphic` 带
  `[DisallowMultipleComponent]`，会静默返回 null（表现为"换了皮但什么都没发生"）。
- **纯装饰性换肤代码要 try/catch + LogWarning**：装饰失败不该让整局游戏起不来，
  也不该触发 PlayMode 的 log-error 门禁。

## 素材生成

生成美术 / 语音 / BGM / 音效前，**先读 `.agents/skills/media-gen/SKILL.md`**。三条底线：

1. **画风档案**：生成美术前必须读 `MediaRaw/style/STYLE.md`（2026-10-29 已回填，基准 = INKWAVE 街头潮流风）；
   按其 `STYLE_PREFIX` 与锚图（`-r`）要求生成，风格冲突的素材不得落地。
2. **短音效与 BGM 不走网关**：音效用 `python3 tools/bake_sfx.py`，BGM 用 `python3 tools/bake_bgm.py`
   （固定种子、可复现、零配额）；网关音乐通道（`lyria-3.5` 与备用 `lyria-3-pro-preview`）
   2026-10-29 实测均被上游禁用（400 `PROXY_GENERATION_DISABLED`）。
3. **落地靠路径**：拷进 `Assets/_Project/Art|Audio/...` 对应目录，导入设置自动套；
   落地后必须 `AssetDatabase.Refresh()` + `MediaTools.ReportMedia()` 体检（详见技能）。

## 变更日志（每次改代码必读必写）

改完追加写入 [CHANGELOG.md](CHANGELOG.md)（已纳入版本控制，是排查知识库）。**格式 `## YYYY-MM-DD #序号`**
（如 `## 2026-11-01 #01`），序号当日从 01 起、**不记时间点**、**新的在上面**。

**每条 `#序号` 只写 1~3 行**（一个自然段，别写成长篇）：
- 一句话结论（改了什么 / 现象）+ **根因或陷阱**（Bug 用 `Fixed:` 标出根因）+ 关键文件路径。
- 发布条只留：版本号 + 包含的 `#序号` 范围 + APK 文件名，**不复述**各条内容。
- **只记对排查问题有用的根因、陷阱与架构变动**。
- **不记**：逐项 `Added`/`Changed`/`Verified` 标签流水账、测试项数与 exit 状态、selftest 细节、用户原话、
  设计推导、采样数值、临时出图路径、SHA-256 与包体积明细。

无论是否提交都要记；发版时补一条版本里程碑。

## 常见坑（高频）

- 自定义 `Graphic` 在运行时用 `new GameObject().AddComponent<T>()` 构造时必须同时补 `CanvasRenderer`，
  否则 `GraphicRaycaster` 抛 `MissingComponentException`，UI 不渲染。
- `MaterialPropertyBlock` 不能在字段初始化器里 `new`（Unity 线程限制），必须延迟到 `Awake`/首次使用。
- batchmode 被 GUI 编辑器占用工程锁时日志只有 `[ExitDontLaunchBugReporter] ... return code 1`，
  不是编译失败；先 `unity status` 确认，不要 kill 编辑器。
- 给已有方法加参数时，旧重载必须保留：否则旧调用会被静默绑定到新语义（例如跳跃永远为 false）。
- `.cs` 新增/删除后若菜单没出现，先等 Domain Reload 完成再查 `console.status`。
- 本项目只启用新版 Input System：**禁止** `UnityEngine.Input.GetKeyDown` 等旧 API，否则运行时每帧抛
  `InvalidOperationException`（PlayMode 会判失败并制造大量 GC）。统一用 `Keyboard.current` / `Mouse.current`。
- 新增/替换 SFX、武器图标或 UI 主题后，记得运行 `P1/构建/11. 生成运行时资源库 (P1RuntimeAssets)`。
- `MediaRaw/` 是原稿区，**不要把整个目录拷进 `Assets/`**。