# 使用指南：素材落地、Unity 导入、能力边界

## 素材落地约定（全项目统一）

```
MediaRaw/                    ← 生成原稿区（在 Assets/ 之外，Unity 不扫描）
├── style/STYLE.md           ← 项目风格档案：STYLE_PREFIX / 锚图 / VOICES 音色表（生成前必读）
├── style/anchor*.*          ← 风格锚图、设定图
├── images/                  ← 生图原稿与透明 PNG（语义命名：ui_icon_inktank.png）
├── voice/                   ← 台词 WAV（命名：vo_<说话人>_<场景>_<序号>.wav）
├── music/                   ← BGM（命名：bgm_<场景>_<情绪>.mp3 / .ogg）
├── sfx/                     ← 本地烘焙音效（tools/bake_sfx.py，命名 <key>_<n>.wav）
└── manifest.jsonl           ← 生成台账（脚本自动写）：ts/kind/model/prompt/refs/output
```

**为什么在项目根而不是 `Assets/` 里**：Unity 只扫描 `Assets/` 下的内容，所以根目录天然
不被导入——不需要 Godot 版 `assets_raw/.gdignore` 那种排除标记，也没有 `.meta` 噪音。
**永远不要把整个 `MediaRaw` 拷进 `Assets/`**，那会连带 `.raw.jpg`、草图、落选稿一起入库。

- **先查台账再生成**：`grep "关键词" MediaRaw/manifest.jsonl`，
  已有相似素材优先复用或用参考图编辑，不要重复烧配额。
- 精选过的成品才拷进 `Assets/_Project/{Art,Audio}/`，`MediaRaw/` 永远只当底稿库。

## ★ Unity 导入注意（与 Godot 最关键的三处差异）

### 1. 导入设置不能手改 `.meta`——由 `AssetPostprocessor` 按路径自动套

Godot 是在 Import 面板点几下；Unity 的等价物是 `.meta` 文件，而
**AGENTS.md 规则 1 与已装 `optimize-audio` 技能都禁止手改 `.meta` YAML**
（手改易出坏引用、且无法 code review）。所以本项目的做法是：

**把素材拷到下面这些路径，导入设置就自动对了**（`MediaImportPostprocessor.cs` 负责）：

| 路径 | 自动套用的设置 | 为什么 |
|---|---|---|
| `Assets/_Project/Art/Sprites/**` | Sprite(2D&UI)、alphaIsTransparency、**mips off**、Clamp、Bilinear、max 512 | UI/HUD 图标：不吃 mip、不重复采样、清晰 |
| `Assets/_Project/Art/Sprites/**/Pixel/**` | 同上但 **FilterMode.Point** | 将来若定像素风，放这个子目录即可，无需改代码 |
| `Assets/_Project/Art/Textures/**` | Default、**mips on**、**Repeat**、max 1024/2048 | 3D 世界贴图（可平铺地面等） |
| `Assets/_Project/Audio/Music/**` | **Streaming** + Vorbis + loadInBackground | BGM：流式读盘，不常驻内存 |
| `Assets/_Project/Audio/SFX/**` | **DecompressOnLoad + PCM** + preload + forceToMono | 短音效：解码零延迟。**Vorbis 对短音效有解码延迟**，别用 |
| `Assets/_Project/Audio/Voice/**` | CompressedInMemory + Vorbis q0.8 + forceToMono | 台词：省内存，允许轻微延迟 |

> **这两个目录下的导入设置以 Postprocessor 为准**：它每次导入都会重套，
> 所以在 Inspector 里手改会被下次重导入覆盖。要改默认行为就改那个 C# 文件（可 review、可提交）。

### 2. 拷进去的素材必须导入一次，否则是「哑的」

新文件在 Unity 扫描前没有 `.meta`，AudioClip/Texture 都是空的（播出来静默、图显示白）。
编辑器开着时：

```bash
"$HOME/.unity/bin/unity" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'
```

## 然后跑体检确认设置真的对上了（这是 agent 的「眼睛」）

```bash
"$HOME/.unity/bin/unity" command script.eval --code 'P1.Editor.MediaTools.ReportMedia();'
"$HOME/.unity/bin/unity" command console.get --tail 40
```

或在编辑器菜单点 `P1/素材/2. 体检：列出已导入媒体与设置`，报告会打印每个资源的
filterMode / loadType / 声道 / 时长。**报告为空或报错 = 没落地**，不要以为拷进去就完事了。
（编辑器没开时退回 batchmode：
`Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod P1.Editor.MediaTools.ReportMedia -logFile Logs/media.log`）

### 3. BGM 循环：Unity 有可听间隙，需要 OGG

Godot 导入 MP3 勾 loop 即无缝；**Unity 没有导入期 loop 开关**（循环是运行时的
`AudioSource.loop = true`），而且 **MP3 有编码器 padding，循环回接处有可听间隙**。
需要无缝循环的曲目生成时加 `--ogg`（Vorbis 在 Unity 里能样本级无缝循环）：

```bash
.agents/skills/media-gen/scripts/gen_music.sh -p "..." --ogg -o MediaRaw/music/bgm_match
```

代价是 MP3→OGG 的二次有损转码（源 MP3 会一并保留）。一次性播放的曲子（结算/开场短曲）
不必转，直接用 MP3。详见 `references/api-music.md`。

## P1 的美术面：和 Godot 项目完全不同（别照搬概念）

| | Godot 项目（T1） | **P1（本项目）** |
|---|---|---|
| 视角 | 2D 像素、俯视 | **3D + 正交俯视**（GamePlan §4.1） |
| 角色 | 需要精灵立绘/转身表/8 方向序列帧 | **不需要**——角色是胶囊体几何占位，M6 才考虑低多边形 |
| 真正要生成的 | 图标、立绘、场景插画 | **UI/HUD 图标、可平铺地面贴图、墨点溅射 alpha 遮罩、标题/关键视觉** |

所以 Godot 版的「角色转身表 / 表情差分 / 8 方向序列帧」工作流在 P1 **没有对应需求**，
不要为了「有这个流程」而去生成角色素材。

### P1 专属的两条硬约束

**① 图标一律出「中性单色」**：两队颜色每局不同（GamePlan §4.2 的 ink owner 决定配色），
所以图标出**白色/浅灰主体 + alpha、不带固有色**，颜色交给 Unity 用 `Image.color`
（UGUI）或材质属性染色。一套图标服务两个队伍，省一半配额，也避免「红队图标配蓝队墨色」的尴尬。

**② 地面贴图必须低饱和、低对比、可平铺**：墨系统要在底色上叠队色，
底面贴图如果花纹重、对比高，会跟墨色打架，覆盖率一眼看不出来（违反体验支柱 3「地图控制可读性」）。
提示词里明确写「可平铺无缝、低饱和、低对比、无强烈方向性光影」。

## Prompt 模板库（拼在 STYLE_PREFIX 之后）

| 素材 | 模板 |
|---|---|
| **中性 UI 图标** | `游戏 UI 图标：{名称}，纯白单色剪影、无固有色、简洁几何、主体居中饱满、不要文字` （配锚图 `-r`，`--keyout 256`） |
| **图标网格草稿** | `同一套 UI 图标差异表：{图标1}、{图标2}、{图标3}、{图标4}，2x2 网格排布，纯白单色剪影，风格规格完全一致、无文字`（`--grid 2x2 --keyout 256`） |
| **可平铺地面贴图** | `可无缝平铺的地面材质：{材质描述}，低饱和、低对比、无强烈方向性光影、无明显接缝、正俯视、无透视、无文字` （放 `Art/Textures/`，**不要** `--keyout`） |
| **墨点溅射遮罩** | `墨点溅射形状，单一色块剪影，边缘有飞溅小点，正俯视扁平，纯白，无文字`（`--keyout 256`，与画风无关的 alpha 遮罩） |
| **墨点形状差分表** | `四种不同的墨点溅射形状差异表：圆形大块、放射状、细长条、星形，2x2 网格排布，纯白剪影` |
| **标题/关键视觉** | `游戏标题插画：{描述}，宽幅 16:9 构图，{氛围词}` |
| **HUD 面板底图** | `游戏 HUD 面板底图：{材质}硬朗斜切边框，无文字` |

通用负面约束（写进 prompt）：`无文字、无水印、无签名、无Q版感`；
**额外禁止**：`不得使用任天堂/Splatoon 的角色、名称、美术风格或音效`（GamePlan R5）。
要透明底的加 `--keyout`（脚本自动追加品红底要求）。

## 能力边界（别绕弯路）

| 需求 | 行不行 | 替代方案 |
|---|---|---|
| 透明底 PNG | ✅ 走 `--keyout` 品红管线 | 主体含品红/粉色时换纯绿底 + `keyout.py --green` |
| 中文语音（播报/台词） | ✅ `gen_voice.sh` | — |
| BGM | ⚠️ `gen_music.sh` 流程保留，但 `lyria-3.5` **2026-10-29 起上游禁用**（400 `PROXY_GENERATION_DISABLED`） | 改用 **`python3 tools/bake_bgm.py`**（本地程序化合成，固定种子、零配额，产 OGG 进 `Assets/_Project/Audio/Music/`）；网关恢复后仍可用 `gen_music.sh --ogg` |
| **音效 SFX（枪声/溅射/UI 音）** | **❌ 网关没有 SFX 模型，且音乐模型不能当音效用**：实测提示词写「只要 0.15 秒点击音」，`lyria-3.5` 仍返回 56 秒整段素材（按「一首曲子」理解 prompt），剪开头也剪不出干脆瞬态 | **本项目已有一等公民：`python3 tools/bake_sfx.py`**（纯标准库离线合成，固定种子可复现，17 个音效族/23 个文件，产 WAV 进 `Assets/_Project/Audio/SFX/`）。其次 jsfxr、freesound.org、Kenney 音频包（需你明确授权引入第三方素材） |
| 逐帧动画序列（走路循环等） | ❌ 模型生成不了多帧一致的动画 | 静态图 + 程序化动画；P1 是 3D 占位，本来也不需要 |
| 精确时长的音乐段落 | ⚠️ 无时长参数（实测要 1 秒给 56 秒） | 生成后用 ffmpeg 裁裁/淡出对齐 |
| 像素级精确的 tilemap 素材 | ⚠️ 风格能像，网格对齐不稳 | 素材包，或生成后手修 |
| 商用合规 | ⚠️ 生成图带 SynthID/C2PA 隐形水印；各平台条款自查；别要求在世画师风格 | — |

## 故障排查

| 症状 | 原因 | 处理 |
|---|---|---|
| 脚本报 HTTP 000 | 本机 ALL_PROXY 指向失效代理 | 所有 curl 加 `--noproxy '*'`（脚本已内置）。若偶发 000 但 `selfcheck.sh` 正常，多半是网关被并发占满，稍后重试 |
| **生图报 400 `PROXY_GENERATION_DISABLED`** | 该模型通道被上游禁用（2026-10-28 实测 `gemini-3.1-flash-lite-image` 如此） | 改用 `gemini-3.1-flash-image`（脚本默认）。名单里有 ≠ 能用 |
| **透明底图标看着是全白/空白** | 这是设计（纯白主体 + alpha），白底看图器看不见 | `python3 .agents/skills/media-gen/scripts/preview_alpha.py <out.png> <icons...>` 合成到深色底再看 |
| 响应无图/无音频 | 模型偶发只回文本（约 1/10） | 脚本自动重试 2 次；仍失败换措辞重试 |
| 抠图边缘有品红晕 | JPEG 压缩溢色重 | 提高 `keyout.py` 的 hi 阈值（如 130）重跑 `raw.jpg` |
| 图完全没抠透 | 品红区域占比 < 4% 未触发裁剪 | 手动跑 `keyout.py raw.jpg out.png` |
| `--grid` 切出来的格子尺寸不一致 | 每格各自裁到内容边界，属预期 | 当独立精灵用；要严格等格就整表不 keyout，或事后重排 |
| TTS 把指令读出来 | 指令没用方括号括起来（实测「用XX的语气说：」会被朗读） | 用 `--style`（脚本自动转方括号） |
| `--ogg` 报 `Unknown encoder 'libvorbis'` | 本机 ffmpeg 未编入 libvorbis | 新版脚本已自动降级到内置 vorbis（需 `-ac 2`）；要更好音质 `brew install vorbis-tools` |
| **素材拷进 Assets 后图是白块/音是哑的** | 没让 Unity 导入（缺 `.meta`） | `unity command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'`，再跑体检 |
| **Inspector 里改了导入设置，过一会又变回去了** | `MediaImportPostprocessor.cs` 按路径重套了 | 这是设计意图；要改默认行为请改那个 C# 文件 |
| BGM 循环处有「咔哒」间隙 | MP3 encoder padding | 重生成时加 `--ogg`（Vorbis 无缝） |
| `unity command` 超时 / 报 504 | 编辑器主线程被阻塞（弹窗、导入中、编译中） | 先 `unity status` 与 `unity pipeline list`；长时间不恢复就检查编辑器窗口是否有弹窗需人工点掉。也可退回 batchmode（要求没有别的实例占用工程锁） |

## 自检

任何时候不确定环境是否可用，先跑零配额自检：

```bash
bash .agents/skills/media-gen/scripts/selfcheck.sh
```