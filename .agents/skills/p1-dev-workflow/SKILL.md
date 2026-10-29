---
name: p1-dev-workflow
description: P1（Unity 俯视角涂地对战）工程操作手册：Unity CLI 命令与常见坑、Phase A 一键生成菜单与批处理入口、编译校验与测试流程、本机环境注意事项、多 Agent 并行防冲突细则、媒体素材落地与体检。凡需要驱动该项目的 Unity 编辑器、重建场景、跑测试、排查编译/环境问题或落地素材时使用。
---

# p1-dev-workflow：P1 工程操作手册

`AGENTS.md` 只保留必读摘要；本技能存放**具体操作步骤与排错细节**。
项目根目录：`/Users/a1/Documents/01Code/Unity/P1`。

## 1. Unity CLI（优先使用）

本机已装 Unity CLI，项目含 `com.unity.pipeline`，可直接驱动开着的编辑器。
非交互 shell 里 `unity` 可能不在 PATH，统一用绝对路径：

```bash
U="$HOME/.unity/bin/unity"
"$U" status                                   # 编辑器 ready？含端口与项目
"$U" command                                  # 列出当前暴露的所有命令
"$U" command console.status                   # 编译状态 + 控制台计数（看 ageMs）
"$U" command console.get --tail 50 --level error
"$U" command script.recompile                 # 触发重编译
"$U" command script.recompile_status          # 编译结果（failed / errors）
"$U" command screenshot --view game --output Logs/shot.png
```

- 创建/修改对象优先走命令：`gameobjects.*`、`prefabs.*`、`scene.*`、`script.*`。
- 批量修改用 `unity command batch`：默认事务性（任一步失败整体回滚）；先加 `--dry_run` 预演。
- 需要跑任意 C#：`unity command script.eval --code '...'`。
- 拿不准命令名用 `unity command --query <关键词>` 搜索，不要猜。
- **`screenshot --view game` 只拍相机画面，不含 ScreenSpaceOverlay 的 uGUI**（纯代码 HUD 会得到一张纯背景色图）。
  Canvas UI 验收请在 Play 模式下用
  `"$U" command script.eval --code 'UnityEngine.ScreenCapture.CaptureScreenshot("Logs/ui.png");'`，
  等 2~3 秒再读 `Logs/ui.png`（相对项目根目录）。
- 播放模式：`command start` / `stop` / `pause`。**播放模式下不能重建场景**
  （`EditorSceneManager.NewScene` 会抛 "cannot create scene during play mode"），先 `stop`。
- 测试走异步：`test run --mode PlayMode --async_tests`，再轮询 `test status`。
- 编辑器不聚焦时主循环可能暂停：`set_run_in_background --enable true` 临时覆盖
  （用完恢复 `--enable false`）。

## 2. Phase A 一键生成

| 菜单 | 作用 |
|---|---|
| `P1/构建/1. 生成默认配置与材质` | 生成 `ScriptableObjects/*.asset` 与 `Materials/*.mat` |
| `P1/构建/2. 一键生成并打开第一阶段试玩场景 (Phase A Playbox)` | 重建并打开 `Assets/_Project/Scenes/PhaseA_Playbox.unity` |
| `P1/构建/3. 应用项目 PlayerSettings` | 横屏 / 包名 / minSdk 26 / ARM64 |
| `P1/构建/4. 渲染场景涂地自检预览图` | 输出 `Logs/phaseA_preview.png` |
| `P1/音频/1. 绑定播报语音与双段 BGM` | 把 `Audio/Voice`、`Audio/Music` 与 `AudioMixConfig` 绑进当前场景的 `AudioManager`（会保存场景） |
| `P1/音频/2. 体检音频导入设置` | 等价 `MediaTools.ReportMedia()`，确认 Voice/Music 导入设置 |

菜单调用（注意名字必须完全一致，含后缀）：

```bash
"$U" command editor.invoke_menu --path "P1/构建/2. 一键生成并打开第一阶段试玩场景 (Phase A Playbox)"
```

批处理等价入口：

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod P1.Editor.ProjectScaffolder.BuildPhaseAPlayboxScene -logFile Logs/scaffold.log
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod P1.Editor.ProjectScaffolder.CaptureVerificationScreenshot -logFile Logs/preview.log
```

**批处理渲染局限**：手动 `cam.Render()` 下 URP/Lit 的受光/颜色不可信（深色材质会偏蓝）。
自检图只用于确认几何、涂地分布与遮蔽关系；**颜色观感必须在真实编辑器里点 Play 验收**。
纯阻挡物件（掩体、边界、出生台）已统一用 URP/Unlit，避免受该问题影响。

## 3. 编译校验（每次改完代码必须跑）

编辑器开着时（最快）：

```bash
"$U" command script.recompile_status
"$U" command console.get --tail 50 --level error
```

`compilationFailed: true` 或出现 error 条目即失败，必须修到干净。

编辑器关闭时退回 batchmode：

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" -logFile Logs/compilecheck.log
grep -E "error CS|Compilation failed" Logs/compilecheck.log || echo "COMPILE OK"
```

**排错：**

- 有任何 `error CS` 必须修干净，不能以「warning 而已」交付。
- batchmode 要求工程没被别的实例占用。若日志只有 30 来行、末尾是
  `[ExitDontLaunchBugReporter] ... return code 1`，就是 GUI 编辑器占着工程锁
  （`Temp/UnityLockfile`）。先用 `unity status` / `ps aux | grep Unity` 确认，
  不要误判成编译失败，也不要 kill 别人的编辑器。
- 编辑器主线程被弹窗或导入卡住时，pipeline 命令会一直 504 超时；
  要么等，要么让用户处理弹窗（可能涉及 License）。
- GUI 编辑器在无头/后台环境会停止 tick：`console.status` 的 `ageMs` 持续增长即为征兆。
  此时不要反复重试，直接用批处理入口（不受影响）。

## 4. 测试

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode -testResults Logs/em.xml -logFile Logs/em.log
"$UNITY" -batchmode -projectPath "$PWD" \
  -runTests -testPlatform PlayMode -testResults Logs/pm.xml -logFile Logs/pm.log
```

编辑器开着时用实时 Pipeline（避免工程锁冲突）：

```bash
"$U" command test run --mode EditMode
"$U" command test run --mode PlayMode --async_tests
"$U" command test status
```

现有测试：

- `Tests/EditMode/InkGridTests.cs`：涂色、互刷计数、不可涂格、重置。
- `Tests/PlayMode/PhaseAPlayboxSmokeTests.cs`：加载真实场景，验证开火扣墨+涂地、潜行形态与移速、
  跳跃腾空与落地、手雷扣墨、大招大范围涂地。
- `Tests/PlayMode/MatchFlowTests.cs`：Ready→Countdown→Playing→Finished→Result→Restart 全链路。

要点：

- **PlayMode 测试会把任何运行时 log error 判为失败**，是抓 `ParticleSystem` 等隐性问题最有效的手段。
- 纯逻辑（墨水网格、覆盖率、FSM、数值）必须能 EditMode 无场景测试。
- 场景测试里传送角色后，`CharacterController.isGrounded` 要到下一次 `Move` 才刷新，注意等待一帧。

## 5. 多 Agent 并行防冲突（细则）

本项目由多个 Agent 并行/交替开发。除 `AGENTS.md` 的摘要外，注意：

1. **写文件前重新 `Read`**：其他 Agent 可能已改过同一文件；禁止用会话早期内容盲写。
2. **公共 API 兼容**：给已有方法加参数时，用**重载 + 默认参数**而不是改签名。示例（已发生过的坑）：
   `SetFrameIntent(move, aim, swim, isFiring, locked)` 与新增 `(move, aim, swim, jump, isFiring, locked)`
   必须同时存在，否则旧的 5 参调用会被静默绑定到新语义上（跳跃永远为 false）。
3. **Domain Reload**：任何 `.cs` 保存都会打断其他 Agent 的 PlayMode / 测试 / eval 会话。
   编译报错时先核对文件名；不属于自己的改动不要回滚，报告即可。
4. **场景与配置**：`PhaseA_Playbox.unity` 由 `ProjectScaffolder` 全量重建，会覆盖场景内手工调整；
   `ScriptableObjects/*.asset` 由菜单 1 写入调优值。非本系统任务只改自己负责的字段。
5. **编辑器锁**：`Temp/UnityLockfile` 是 Unity 独占锁，禁止删除；禁止 kill 编辑器进程。
6. **交付说明**：列出改动文件、接口变化、验证结果，便于其他 Agent 对齐。

## 6. 素材落地（图 / 语音 / BGM / 音效）

生成前先读 `.agents/skills/media-gen/SKILL.md`（网关、脚本、台账、风格档案）。
落地与体检流程：

| 素材 | 落地路径 | 自动设置 |
|---|---|---|
| UI 图标 / 精灵 | `Assets/_Project/Art/Sprites/` | Sprite、alphaIsTransparency、mips off、Clamp |
| 世界贴图 | `Assets/_Project/Art/Textures/` | Default、mips on、Repeat |
| BGM | `Assets/_Project/Audio/Music/` | Streaming + Vorbis |
| 音效 | `Assets/_Project/Audio/SFX/` | DecompressOnLoad + PCM |
| 语音 | `Assets/_Project/Audio/Voice/` | CompressedInMemory + Vorbis |

```bash
"$U" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'
"$U" command script.eval --code 'P1.Editor.MediaTools.ReportMedia();'
"$U" command console.get --tail 40      # 读回体检报告，确认 0 异常
```

- 编辑器菜单同功能：`P1/素材/2. 体检：列出已导入媒体与设置`。
- 新文件没有 `.meta` 时是白块/哑的，必须导入 + 体检。
- **白图标在白色看图器里看不见**，检查形状先跑
  `python3 .agents/skills/media-gen/scripts/preview_alpha.py <out.png> <图...>`。
- `MediaRaw/` 是原稿区（`Assets/` 之外，Unity 不扫描），**不要把整个 `MediaRaw` 拷进 `Assets/`**。
- 音效不走网关：`python3 tools/bake_sfx.py`（`--list` 看音色族，`--only` 只烘指定族）。
- BGM：`lyria-3.5` 2026-10-29 起被上游禁用（400 `PROXY_GENERATION_DISABLED`），
  改用 `python3 tools/bake_bgm.py` 本地确定性合成两段 60s 循环（128 BPM 主阶段 / 160 BPM 决战段）；
  `--only main|final` 单段重烘、`--stats` 只看指标。生成后仍需 `Refresh` + 体检。
