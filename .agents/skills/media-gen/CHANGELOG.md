# media-gen 变更记录

> 规则：任何 agent 修改本技能（脚本/参考文档/配置格式/C# 导入工具），必须在此追加一行：
> `## 日期 摘要 (agent名)` + 要点列表。

## 2026-10-29 lyria-3.5 被上游禁用；新增本地双段 BGM 烘焙 (Sound-Director)

- **实测**：`lyria-3.5` 请求返回 400 `PROXY_GENERATION_DISABLED: generateContent is not supported`
  （重试 2 次、换短提示词均失败）；网关 `/v1/models` 无其它音乐模型。
  已更新 `references/api-music.md`、`references/usage-guide.md`、`SKILL.md` 的模型表。
- **新增项目工具 `tools/bake_bgm.py`**（不在本技能 scripts 下，属 P1 本地工具）：
  移植 Inkwave `music.js` 的 battle / battle_final 编排（和声、鼓组、旋律），
  用 Python 加性合成离线渲染 128 BPM 主阶段与 160 BPM 决战段各 60.0s 循环，
  含侧链 pump、乒乓延时、合成 IR 混响与循环尾音折叠；固定种子、零配额、可复现。
  产物 WAV 留 `MediaRaw/music/`，OGG 入库 `Assets/_Project/Audio/Music/`。
- 播报员英文音色已登记 `MediaRaw/style/STYLE.md`（announcer / announcer_urgent），
  6 条台词入库 `Assets/_Project/Audio/Voice/`。

## 2026-10-28 P1 初版：从 Godot 项目移植并做 Unity 适配 (ZCode)

**背景**：参照 `/Users/a1/Documents/01Code/Godot/t-1/.agents/skills/media-gen` 建立 P1 版。
判断结论是「工具链就位，不开始产美术」——P1 的画风按 GamePlan 推迟到 M6，
现阶段美术用几何体占位，所以本次**不生成任何正式素材**（STYLE.md 保持空白）。

### 直接复用（引擎无关，原样或仅改注释与路径）

- `scripts/lib_extract.py`、`gen_image.sh`、`gen_voice.sh`、`keyout.py`、`split_grid.py`
  原样复用——返回形状解析、品红抠透明管线、宫格切表逻辑都经过 Godot 项目 574 条台账的实战检验。
- 三份 API 实测笔记（`api-image.md` / `api-voice.md` / `api-music.md`）保留全部实测数据
  （方括号 TTS 指令、模型只回文本的 1/10 概率、媒体嵌在 `images` 字段等），
  新增 P1 使用要点段落。

### Unity 适配（相对 Godot 版的实质改动）

1. **`lib_common.sh` 工程根判据**：`project.godot` → `ProjectSettings/ProjectVersion.txt`；
   原稿目录 `assets_raw/` → `MediaRaw/`。
   **不再需要 `.gdignore`**：Unity 只扫描 `Assets/` 下的内容，原稿放项目根天然不被导入。
2. **`gen_music.sh` 新增 `--ogg`**：Godot 的 MP3 勾 loop 即无缝，**Unity 不是**——
   Unity 无导入期 loop 开关（循环在 `AudioSource.loop`），且 MP3 有编码器 padding，
   循环回接处有可听间隙。改为可选 ffmpeg 转 OGG(Vorbis) 作入库件，源 MP3 保留。
   ffmpeg 探测兼容非交互 shell PATH 缺 `/opt/homebrew/bin` 的情况。
3. **`selfcheck.sh` 新增**：零配额自检（工程根/配置/网关/模型名单/python 依赖/ffmpeg/烘焙脚本）。
   实测中发现并修正一个真实 bug：**网关 `/v1/models` 也需要鉴权**，
   不带 header 返回 `401 {"error":"Missing API key"}`——最初版本没带 header，
   把 401 误报成「网关共 0 个模型」。现在会明确区分 401（密钥失效）与 000（不可达）。
   复核结果：网关 38 个模型，五个期望模型全部在名单里。
4. **素材工作流重写为 P1 的（原版是 2D 像素游戏，概念不可照搬）**：
   P1 是 3D + 正交俯视、角色为胶囊体占位，**不需要角色转身表/表情差分/8 方向序列帧**；
   真正需要的是 UI/HUD 图标、可平铺地面贴图、墨点溅射 alpha 遮罩、标题关键视觉。
   新增两条 P1 专属硬约束：
   - **图标一律中性单色**（白主体 + alpha），颜色交给 Unity 按队伍 `Image.color` 染色
     —— 两队墨色每局不同，一套图标服务两队，省一半配额；
   - **地面贴图必须低饱和低对比可平铺**，否则与墨色打架、违反 GamePlan 体验支柱 3
     「地图控制可读性」。
5. **落地与校验改为 Unity 回路**：Godot 靠 `--headless --editor --quit` 触发导入；
   Unity 靠 `AssetDatabase.Refresh()` + 体检报告（`P1.Editor.MediaTools.ReportMedia`）
   + `unity command console.get` 读回。

### 新增：本地音效烘焙 `tools/bake_sfx.py`（零配额）

网关无 SFX 模型，且 Godot 项目实测证明 `lyria-3.5` 把「0.15 秒点击音」理解成一首曲子
（返回 56.1 秒）。故按 Godot 项目 `bake_sfx.py` 的设计哲学新写一份 P1 版：
纯标准库、xorshift 固定种子可复现、振荡器 + 指数包络 + RBJ 双二阶 + 白噪声 + 反馈延迟。

**音色定义整套重写为涂地对战需求**（不搬修仙剑招那 1000 行）：17 个音效族 / 23 个文件，
覆盖主武器射击（3 变体，对应 GamePlan M6「random-pitch SFX」）、落地溅射、命中/击杀/被击杀、
复活、墨量不足/回墨、潜能形态就绪/发动、倒计时/开场/时间到/结算胜败、UI 点击确认。
峰值按用途分档（战斗反馈 0.30~0.34、UI 0.22~0.26）避免 UI 抢戏。

- 修正一个自造 bug：最初用内置 `hash(key)` 做变体种子，而 **Python 3 对 str 的 hash
  逐进程随机化**（PYTHONHASHSEED），会让「同输入同波形」的承诺失效；改 FNV-1a。
  已实测验证：连跑两次全部 wav 的 SHA-256 一致。
- 提供 `--stats` 客观体检（时长/峰值/RMS/削波/声道/采样率）——agent 听不见，
  这是音效侧唯一能自动化的验收手段；**音色好不好听仍需用户实际播放**
  `MediaRaw/sfx/` 确认。
- 首轮烘焙 23 文件 0 异常（无削波、时长合理、单声道 44.1kHz）。
  `shot` 初版 65ms/RMS 0.042 判定过薄（像咔哒不像啵），补低频身体层并拉长到 85ms。

### 新增：Unity 导入适配 C#（`Assets/_Project/Scripts/Editor/`）

- `MediaImportPostprocessor.cs`：`AssetPostprocessor` 按路径自动套导入设置。
  这是**唯一合规路径**——AGENTS.md 规则 1 与 `optimize-audio` 技能都禁止手改 `.meta`。
  Sprites / Textures / Music / SFX / Voice 五类路径各有对应设置
  （SFX 用 PCM + DecompressOnLoad 而非 Vorbis，因为 Vorbis 对短音效有解码延迟）。
  仅对 `Assets/_Project/` 下路径生效。
- `MediaTools.cs`：`[MenuItem]` ① 对选中资源重新套导入设置（修历史文件）；
  ② 体检报告（filterMode / loadType / 声道 / 时长），可 `script.eval` 或 batchmode `-executeMethod` 调用。

### 冒烟测试实测记录（2026-10-28，逐项留证）

**通过的**

- `selfcheck.sh`：五个期望模型全在网关名单里（网关共 38 个模型）。
- **生图 + 抠透明底**：`gemini-3.1-flash-image` 出「墨点溅射」单图，`--keyout 256` 得
  256×232 透明 PNG；alpha 覆盖率 25.6%（不透明 21.2% / 半透明 4.5%），棋盘格上边缘干净。
- **宫格切表**：`--grid 2x2 --keyout 256` 一次出 4 张形状（圆形大块/放射状/细长条/星形）。
  目视验收（用新建的 `preview_alpha.py`）4 张里 3 张可用；「细长条」那格被模型画成
  细长划痕（裁完仅 256×58），当墨痕可用、当墨团不行。
- **中文 TTS**：`gemini-3.8-flash-lite-tts` 出 2.96s / 24kHz / 单声道 16bit WAV。
  时长落在合理区间，说明方括号音色指令**没有被朗读**（若被朗读会明显变长）。
- **本地音效烘焙**：17 族 23 文件，连跑两次全部 wav 的 SHA-256 一致（确定性成立），
  `--stats` 报 0 异常（无削波、单声道 44.1kHz、时长合理）。
- **C# 编译**：改用 Unity 自带 Roslyn 独立编译验证通过（见下「环境阻塞」），
  0 error。

**实测踩到的坑（已修，别再踩）**

1. `generation-lite` 通道被上游禁用：`gemini-3.1-flash-lite-image` 返回
   `400 PROXY_GENERATION_DISABLED: generateContent is not supported`，
   而同刻 `gemini-3.1-flash-image` 正常。已把该模型标注为不可用。
   **教训：`/v1/models` 里有名字 ≠ 能生成**，所以 `selfcheck.sh` 现在会专门标注
   「在名单但已知不可用」的模型。
2. `/v1/models` 需要鉴权：不带 header 返回 `401 {"error":"Missing API key"}`（已在初版修过）。
3. **本机 ffmpeg 8.1.2 不含 `libvorbis`**，只有内置 `vorbis`，而内置版**只支持 2 声道**
   （单声道输入直接报错）。原 `gen_music.sh --ogg` 硬编码 `-c:a libvorbis` 在本机会直接失败
   —— 已改为按 `oggenc > ffmpeg libvorbis > ffmpeg 内置 vorbis` 顺序探测，
   内置路径补 `-strict experimental -ac 2 -b:a 192k`。已用立体声与单声道上混两种输入实测通过。
   （更好音质可 `brew install vorbis-tools`，脚本会自动优先 oggenc。）
4. Unity 6 里 `AudioClipLoadState` 已移除，`clip.loadState` 的枚举类型是 **`AudioDataLoadState`**。
   初版写错导致 `CS0103`，已修。写这类「凭记忆的 API」时先到
   `Contents/Resources/Scripting/Managed/UnityEngine/*.dll` 里查符号，别猜。
5. `--grid` + `--keyout` 的各格尺寸不一致（每格各自裁到内容边界，实测
   256×252 / 256×213 / 256×58 / 256×256）。当独立精灵没问题，拼等格图集要注意。

### 新增 `scripts/preview_alpha.py`（因一个真实盲区）

P1 图标按约定是「纯白 + alpha」，**在白色背景的看图器里完全看不见**——
第一次验收墨点图时只看到一片白，无法判断形状。所以加了预览工具：
把素材合成到深灰底与棋盘格上、拼成接触表，同时打印 alpha 覆盖率/半透明占比，
覆盖率 ≈0%（没抠出来）或 >92%（整张没透）会告警。这是素材侧的「截图自检」。

### 环境阻塞（未能在编辑器内完成的验证）

本轮尝试在编辑器内跑 `MediaTools.ReportMedia` 时，Unity 编辑器主线程反复停摆：
两个实例的 `Editor.log` 都停在同一批 `Licensing::Module` 行之后，pipeline 命令
一律 504 超时；同时另一个会话正在同一工程上做 M0 的 Android SDK/NDK 配置
（新增了 `Assets/_Project/Editor/AndroidToolsSetup.cs`），占着 `Temp/UnityLockfile`，
导致 batchmode 也起不来（`ExitDontLaunchBugReporter ... return code 1`）。

因此：

- **已完成**：C# 用 Unity 自带 Roslyn 独立编译验证 0 error（引用
  `Resources/Scripting/Managed/UnityEngine/*.dll` 模块化程序集，不要同时引用
  `UnityEngine.dll`/`UnityEditor.dll` 外观程序集，否则同名类型二义 CS0433）。
- **已完成（由产物反证）**：`Assets/_Project/Audio/SFX/*.wav.meta` 在 14:21:44 落盘
  （我修 CS0103 是 14:16:11），内容是 `loadType: 0`(DecompressOnLoad)、
  `compressionFormat: 0`(PCM)、`quality: 1`、`preloadAudioData: 1`、`forceToMono: 1`、
  `loadInBackground: 0` —— 与 SFX 策略逐项吻合，且都不是 Unity 默认值（默认是
  Vorbis + quality 0.7 + 非 mono）。**这同时证明修好后的 C# 在真实编辑器里编译通过、
  Postprocessor 确实按路径生效了。**
- **未完成**：精灵（`Art/Sprites/`）的导入未落地——那 4 张图是 14:31 才拷进去的，
  当时编辑器主线程已停摆，所以至今没有 `.meta`。这不是代码问题；
  编辑器恢复后 `AssetDatabase.Refresh()` 一次即可，随后
  `unity command script.eval --code 'P1.Editor.MediaTools.ReportMedia();'`
  应看到 `type=Sprite filter=Bilinear mips=Off alpha=T max=512`。
- **未做**：`lyria-3.5` BGM 通道未实测（本轮冒烟批准范围是「1 张图 + 1 句语音」）。

### 未做（明确留给后续）

- **不建运行时音频系统**：AudioMixer（`audio-setup-mixers` 技能指明必须用户在窗口手动建）、
  AudioManager、SfxLibrary SO、音频播放代码——属 M5/M6，按 AGENTS.md 规则 5 不跨系统。
- 不做风格锚定、不生成正式美术素材。