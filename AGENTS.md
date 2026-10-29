# AGENTS.md — P1 项目约定

## 项目是什么

Unity 俯视角「涂地对战」游戏（玩法参考 Splatoon 涂地模式）：用墨水涂满地面，结算时覆盖率高的一方获胜。
当前为原型阶段：单机 + Bot，不做联网，美术用基础几何体占位。完整设计与初始里程碑见 `Docs/GamePlan.md`。
**多 Agent 优化任务看板**：基于 `inkwave-aah-research/` 深度解构的全套精致化分阶段并行开发计划详见 **`Docs/OptimizationPlan.md`**（所有 Agent 在此领任务与打勾）。

**技术栈**：Unity `6000.6.7f1` / URP / 新版 Input System；Linear 色彩空间，1 unit = 1 米；目标桌面（macOS / Windows）+ Android。

## 目录结构

```
Assets/_Project/
  Scripts/   Core 比赛流程与服务 | Ink 涂地网格/贴图 | Player 角色与相机 | Weapons 武器/对象池
             AI Bot 状态机 | UI HUD/结算 | Editor 场景生成与调试（仅编辑器）
  Prefabs/  ScriptableObjects/  Scenes/  Settings/(URP)
  Art/(Sprites, Textures)  Audio/(Music, SFX, Voice)  Tests/(EditMode, PlayMode)
Docs/        设计与路线文档
MediaRaw/    素材原稿区（Assets 之外，Unity 不导入）
tools/       本地脚本（bake_sfx.py：零配额离线音效烘焙）
.agents/skills/  agent 技能
```

> 详细操作手册（Unity CLI 全量命令、菜单/批处理、测试与环境坑、素材落地、并行冲突细则）
> 见技能 **`p1-dev-workflow`**；生成素材另见 **`media-gen`**。

## 硬性规则

1. **不手改 `.unity` / `.prefab` / `.asset` / `.meta` 的 YAML**：场景与预制体用 Editor 脚本
   （`Scripts/Editor/` 下的 `[MenuItem]`）或 Unity CLI 生成；素材导入设置改 `MediaImportPostprocessor.cs`。
2. 不修改 `Library/`、`Temp/`、`Logs/`、`UserSettings/`。
3. 新增 Package 前先说明理由并等待确认。
4. 所有可调参数走 `ScriptableObject`，不在 MonoBehaviour 里硬编码数值。
5. 一次只动一个系统：不跨系统大重构，不顺手改无关文件。
6. 删除或覆盖已有文件前，先 `Read` 确认内容。
7. 不引入第三方美术资源或付费插件，除非明确要求。
8. **多 Agent 并行防冲突**：本项目由多个 Agent 并行/交替开发，必须遵守下节。

## 多 Agent 并行协作（防冲突）

1. **永远假设有人在并发修改**：写文件前必须重新 `Read` 最新内容，禁止凭会话记忆盲写；只动自己负责的系统。
2. **公共接口向后兼容**：改 `InkCharacterMotor` / `CombatantHealth` / `WeaponController` /
   `InkSurfaceController` / `MatchManager` 等跨系统组件时，保留原有公共签名或默认重载，**禁止破坏性变更**。
3. **Unity 编辑器是单例共享资源**：保存 `.cs` 会触发 Domain Reload，打断别人的 PlayMode / 测试 /
   `script.eval`。改完立即检查 `script.recompile_status` + `console.get`；编译报错先看文件名是否属于自己，
   不要盲目回滚他人代码。
4. **不碰编辑器锁**：禁止删除 `Temp/UnityLockfile`，禁止 kill 正在运行的编辑器。
5. **防止场景/配置被冲刷**：未经本轮任务明确要求，不运行 `P1/构建/2. 一键生成并打开场景`
   （会重写主场景）；`ScriptableObjects/*.asset` 只精确修改自己负责的字段，不整文件重置。
6. **交付时说明影响面**：改动文件、新增/调整接口、是否保持向后兼容。

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
  **PlayMode 会把任何运行时 log error 判为失败**（抓 `ParticleSystem` 等隐性问题最有效）。
- 纯逻辑测试放 `Tests/EditMode`（`XxxTests.cs`）；涉及 MonoBehaviour 生命周期放 `Tests/PlayMode`。
- GUI 编辑器主线程可能停止 tick（`console.status` 的 `ageMs` 持续增长）：不要重试，改用批处理（见技能）。
- Agent 看不见画面：关键数值用 `Debug.Log`（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）并读回控制台；
  视觉用 `screenshot` 自查。调试面板 `F12` 切换，默认关闭。

## 素材生成

生成美术 / 语音 / BGM / 音效前，**先读 `.agents/skills/media-gen/SKILL.md`**。三条底线：

1. **画风档案**：生成美术前先读 `MediaRaw/style/STYLE.md`；它目前是**空白**的 = 禁止生成正式素材
   （仅允许管线冒烟测试，且必须标注为测试件）。
2. **短音效不走网关**：一律 `python3 tools/bake_sfx.py`（固定种子、可复现、零配额）。
3. **落地靠路径**：拷进 `Assets/_Project/Art|Audio/...` 对应目录，导入设置自动套；
   落地后必须 `AssetDatabase.Refresh()` + `MediaTools.ReportMedia()` 体检（详见技能）。

## 交付流程

1. **先说计划**：改哪些文件、为什么、验收标准。
2. 实现（遵守硬性规则与多 Agent 规范）。
3. 编译校验 + 测试；涉及素材再加「导入 + 体检」。
4. 更新 `Docs/OptimizationPlan.md`（或 `Docs/GamePlan.md`）的任务看板勾选与认领状态。
5. **总结**：改了什么、如何验证、已知问题/未做项。
