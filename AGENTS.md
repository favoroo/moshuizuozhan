# AGENTS.md — P1 项目约定

## 项目是什么

Unity 第三人称 3D「涂地对战」游戏（玩法参考 Splatoon 涂地模式）：用墨水涂满地面，结算时覆盖率高的一方获胜。
单机 4v4（1 名本地玩家 + 7 名 Bot），不做联网；美术为**程序化几何体 + 程序化 UI + 本地合成音频**，不依赖第三方美术资源。
当前版本 `v0.1.0`：核心对局闭环、7 类主武器 + 2 大招 + 副武器、4v4 Bot、40×40m 对称竞技场、Inkwave 风 HUD、桌面/Android 出包均已落地。

**技术栈**：Unity `6000.6.7f1` / URP 17 / 新版 Input System；Linear 色彩空间，1 unit = 1 米；目标桌面（macOS / Windows）+ Android（横屏 / minSdk 26 / ARM64）。

**文档与看板**：

| 文件 | 用途 |
|---|---|
| `Docs/GamePlan.md` | 设计定位、核心循环、技术决策（为什么这样做） |
| `Docs/OptimizationPlan.md` | **多 Agent 任务看板**：领任务、标 `[-]`、完工打 `[x]`，含依赖与领域锁 |
| `Docs/P6B-QA-Report.md` / `Docs/ProfilerReport_P6C.md` | 测试与性能验收留底 |

> 详细操作手册（Unity CLI 全量命令、菜单/批处理、测试与环境坑、素材落地、并行冲突细则）
> 见技能 **`p1-dev-workflow`**；生成素材见 **`media-gen`**；发版见 **`p1-release`**。

## 目录结构

```
Assets/_Project/
  Scripts/
    Core/        比赛流程 MatchManager、配置 SO、AudioManager、VfxPool、Hitstop、性能探针、运行时资源库
    Ink/         CPU 墨格 InkGrid + GPU 墨迹图集 InkSplatAtlas；墙面涂墨 InkWall*
    Player/      人/乌贼双形态机动、生命、相机、地面准星、输入仲裁、程序化角色、机动音频
    Weapons/     7 武器策略（Strategy）+ 弹丸/光束/滚筒/炸弹/风暴云对象池；WeaponController 为对外门面
    AI/          Bot 状态机、NavGrid 寻路、类人瞄准弹簧、战术决策
    Environment/ 竞技场几何生成、第三人称遮挡 Dither 淡出（OcclusionFader / TopDownOcclusionManager）
    UI/          HUD 视图：计时器、比分、墨水罐、大招球、小地图、击杀卡、战况条、触屏
    UI/Core/     潮流 UI 基元：墨滴形状、弹簧动效、粗描边、主题 SO
    UI/WeaponSelect/ 战前 7 武器选择/配装抽屉（运行时装配）
    UI/Menus/    暂停、设置、无障碍震屏（运行时装配）
    Editor/      场景生成、素材导入/体检、UI/构建/性能菜单（仅编辑器）
  Shaders/       InkGround / InkWall / InkSplat / ObstacleDitherLit
  Materials/  Prefabs/  Scenes/(PhaseA_Playbox 主场景, UIDemo)  ScriptableObjects/(调参入口)
  Settings/(URP)  Art/(Fonts, Sprites/Icons, Textures)  Audio/(Music, SFX, Voice)
  Tests/(EditMode, PlayMode)
Docs/            设计、优化看板、验收报告
MediaRaw/        素材原稿区（Assets 之外，Unity 不扫描）
tools/           bake_sfx.py / bake_bgm.py / bake_icons.py / bake_fonts.py（本地零配额烘焙）
.agents/skills/  p1-dev-workflow | media-gen | p1-release
inkwave-aah-research/  竞品（INKWAVE）逆向资料，只读参考
```

程序集：`P1.Game`（运行时）/ `P1.Editor`（仅编辑器）/ `P1.Tests.EditMode` / `P1.Tests.PlayMode`；
命名空间基本随目录：`P1.Core` `P1.Ink` `P1.Player` `P1.Weapons` `P1.AI` `P1.Environment` `P1.UI` `P1.Editor`。

## 改哪里（模块地图）

| 想改什么 | 首选入口 | 注意 |
|---|---|---|
| 涂地规则/覆盖率/网格 | `Ink/InkGridConfigSO`、`Ink/InkSurfaceController` | 每帧最多上传一次纹理 |
| 油漆观感/湿润/凹凸 | `Shaders/InkGround.shader`、`InkWall.shader` | 材质在 `Materials/`，不手改 `.mat` YAML |
| 武器数值/新武器 | `Weapons/WeaponConfigSO` + `*WeaponStrategy` + `WeaponStrategyFactory` | 外部只经 `WeaponController` 公共 API |
| 移动/潜墨/踩墨/生命 | `Player/InkCharacterMotor`、`PlayerConfigSO`、`CombatantHealth` | 60 HP 踩墨保护底线不可破 |
| 比赛流程/时长/结算 | `Core/MatchManager`、`GameConfigSO` | 阶段事件 `PhaseChanged` 被 UI/音频订阅 |
| Bot 行为 | `AI/BotController`、`BotConfigSO`、`NavGrid`、`BotTactics` | 寻路按墨水加权，撤退不得朝敌 |
| HUD/UI 样式 | `UI/HudController` + 对应 View；`UIThemeSO`、`HudGaugeStyleSO` | 运行时建 Graphic 必须补 `CanvasRenderer` |
| 战前选枪/配装 | `UI/WeaponSelect/WeaponSelectBootstrap`、`WeaponSelectView` | 选择存 PlayerPrefs；独立包依赖 `P1RuntimeAssets` |
| 暂停/设置/震屏 | `UI/Menus/PauseMenuOverlay`、`Core/GameSettingsService` | 暂停用 `Time.timeScale=0`，与 Hitstop 协调 |
| 音效/循环音 | `Core/AudioManager`、`Weapons/WeaponAudioDriver`、`Player/MovementAudioDriver` | 新增音效后必须重建运行时资源库 |
| 图标/音效/主题打包 | `Resources/P1RuntimeAssets.asset` + 菜单 `P1/构建/11` | 生成资产，不手改 `.asset` |
| 地图/掩体/遮挡 | `Environment/ArenaGeometryBuilder`（菜单 `P1/关卡与地图/…`） | 重建会重写场景，先确认无人在 Play |
| 场景装配 | `Editor/ProjectScaffolder`（菜单 `P1/构建/…`） | **禁止手改 `.unity`** |
| 图标/字体/音效/BGM | `MediaRaw/` 原稿 + `tools/bake_*.py`，落地 `Art|Audio` | 落地后必须 Refresh + 体检 |

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

## 快速验证

```bash
U="$HOME/.unity/bin/unity"
"$U" status                                   # 编辑器是否 ready（端口 + 项目）
"$U" command script.recompile_status          # 编译是否失败
"$U" command console.get --tail 50 --level error
"$U" command screenshot --view game --output Logs/shot.png
```

- 代码改完必须编译干净：`compilationFailed: false` 且无 `error CS`；不以「warning 而已」交付。
- 测试：`"$U" command test run --mode EditMode`；PlayMode 用 `--async_tests` + `test status` 轮询。
  **PlayMode 会把任何运行时 log error 判为失败**（抓 `ParticleSystem`、UI、池等隐性问题最有效）。
  基线：EditMode 209 / PlayMode 70（P9 快照），新改动不得让既有用例转红。
- 纯逻辑测试放 `Tests/EditMode`（`XxxTests.cs`）；涉及 MonoBehaviour 生命周期放 `Tests/PlayMode`。
- **UI 截图**：`screenshot --view game` 只拍相机，不含 ScreenSpaceOverlay 的 uGUI；
  请用 `"$U" command script.eval --code 'UnityEngine.ScreenCapture.CaptureScreenshot("Logs/ui.png");'`。
- GUI 编辑器主线程可能停止 tick（`console.status` 的 `ageMs` 持续增长）：不要重试，改用批处理（见技能）。
- Agent 看不见画面：关键数值用 `Debug.Log`（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）并读回控制台；
  视觉用截图自查。调试面板 `F12` 切换，默认关闭。
- 性能红线（改动涉及战斗/涂地/UI 时自查）：桌面 60 FPS、Android 30+ FPS；战斗热路径零持续 GC；
  `InkSurfaceController` 纹理上传 < 1.5 ms 无尖刺；DrawCalls < 120（桌面）/ 150（移动）。
  采样入口：菜单 `P1/性能/1. 执行 120 秒战斗 Profiler 性能仿真采样 (P6-C)`。
- 出包：菜单 `P1/构建/10. 一键全平台自动化出包与验收 (P6-C)` → `Builds/StandaloneOSX` / `Builds/Android`；
  正式发版走 `python3 .agents/skills/p1-release/release.py`（版本递增 + 双端 Release）。
- **新增/替换 SFX、武器图标或 UI 主题后**：先运行 `P1/构建/11. 生成运行时资源库 (P1RuntimeAssets)`，
  否则独立包会静音/缺图标（编辑器内仍能靠 `AssetDatabase` 兜底，容易漏检）。

## 素材生成

生成美术 / 语音 / BGM / 音效前，**先读 `.agents/skills/media-gen/SKILL.md`**。三条底线：

1. **画风档案**：生成美术前先读 `MediaRaw/style/STYLE.md`；它目前是**空白**的 = 禁止生成正式素材
   （仅允许管线冒烟测试，且必须标注为测试件）。
2. **短音效与 BGM 不走网关**：音效用 `python3 tools/bake_sfx.py`，BGM 用 `python3 tools/bake_bgm.py`
   （固定种子、可复现、零配额）；`lyria-3.5` 已于 2026-10-29 被上游禁用。
3. **落地靠路径**：拷进 `Assets/_Project/Art|Audio/...` 对应目录，导入设置自动套；
   落地后必须 `AssetDatabase.Refresh()` + `MediaTools.ReportMedia()` 体检（详见技能）。

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

## 交付流程

1. **先说计划**：改哪些文件、为什么、验收标准。
2. 实现（遵守硬性规则与多 Agent 规范）。
3. 编译校验 + 测试；涉及素材再加「导入 + 体检」；涉及性能再加 Profiler 采样。
4. 更新 `Docs/OptimizationPlan.md`（或 `Docs/GamePlan.md`）的任务看板勾选与认领状态。
5. **总结**：改了什么、如何验证、已知问题/未做项。