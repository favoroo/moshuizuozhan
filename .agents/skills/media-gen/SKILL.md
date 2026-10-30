---
name: media-gen
description: 为 P1（Unity 俯视角涂地对战游戏）生成与落地分媒体素材：游戏美术图（UI/HUD 图标、墨点溅射遮罩、可平铺地面贴图、标题关键视觉，支持参考图锁画风与抠透明底）、中文播报/台词语音（TTS）、BGM 配乐，以及本地烘焙音效 SFX。凡需要生成、编辑、批量产出、风格化一致的游戏图/语音/音乐/音效素材，或需要把素材落地进 Unity（导入设置、体检）时，必须使用本技能。
---

# media-gen：P1 Unity 素材生成与落地

本机媒体网关（`http://127.0.0.1:8317/v1`）的封装：生图（Gemini）、中文语音（Gemini TTS）、
BGM（Lyria）；外加**本地烘焙音效**（网关没有 SFX 模型，见铁律 4）。
所有调用走脚本，脚本自动处理鉴权、重试、媒体提取、抠透明底和台账登记；
Unity 侧的导入设置由项目内 `MediaImportPostprocessor.cs` 按路径自动套（**禁止手改 `.meta`**）。

## 铁律（违反会浪费配额或产出废料）

1. **生成美术前先读 `MediaRaw/style/STYLE.md`**（项目风格档案）：
   拿 `STYLE_PREFIX` 风格前缀拼进提示词、拿锚图路径传 `-r`。
   **本项目的 STYLE.md 目前是空白的**——画风按 `Docs/GamePlan.md` 推迟到 M6。
   空档案 = 禁止生成正式素材；先走 [工作流 1 风格锚定](references/workflow-style-lock.md)
   并与用户确认画风。唯一例外是冒烟测试（中性主体、产物标注为测试件）。
2. **不要手搓 curl**，一律用脚本（返回形状不固定、代理陷阱、重试逻辑都已封装）。
3. **生成前先查台账**：`grep 关键词 MediaRaw/manifest.jsonl`，能复用就不重新生成。
4. **音效不要走网关**：网关无 SFX 模型，且 `lyria-3.5` 实测把「0.15 秒点击音」
   返回成 56 秒整段素材。短音效一律走 `python3 tools/bake_sfx.py`。
5. **素材落地后必须让 Unity 导入一次并跑体检**，否则是哑的/白块（见「落地」一节）。

## 快速开始

```bash
S=.agents/skills/media-gen/scripts

# 环境自检（零配额，任何时候不确定就先跑这个）
bash $S/selfcheck.sh

# 生图（PREFIX/ANCHOR 从 STYLE.md 取；图标一律中性单色 + 透明底）
$S/gen_image.sh -p "${PREFIX} 游戏 UI 图标：墨水罐，纯白单色剪影、无固有色、主体居中饱满、不要文字" \
  -r "$ANCHOR" --keyout 256 -o MediaRaw/images/ui_icon_inktank

# 成组图标：一条命令出一表再切分（同图天然同规格，见工作流 2）
$S/gen_image.sh -p "${PREFIX} 同一套 UI 图标差异表：墨水罐、秒表、地图标记、能力按钮，2x2 网格排布，纯白单色剪影、规格一致、无文字" \
  -r "$ANCHOR" --grid 2x2 --keyout 256 -o MediaRaw/images/ui_icons_set_a

# 中文播报（音色描述登记在 STYLE.md 的 VOICES 表，同说话人固定复用）
$S/gen_voice.sh -t "三、二、一，开始！" --style "<从 STYLE.md 的 VOICES 表取 announcer 音色>" \
  -o MediaRaw/voice/vo_announcer_countdown_01

# BGM（对局曲要循环 → 加 --ogg，Unity 的 MP3 循环有可听间隙）
$S/gen_music.sh -p "<STYLE.md 音乐风格> competitive battle theme, 140 BPM, seamless loop, instrumental only, no vocals" \
  --ogg -o MediaRaw/music/bgm_match

# 音效（零配额，本地合成；改完参数重新烘即可）
python3 tools/bake_sfx.py                 # 全量：17 族 23 文件
python3 tools/bake_sfx.py --list           # 看有哪些族
python3 tools/bake_sfx.py --only shot      # 只调射击音时用（快）
python3 tools/bake_sfx.py --stats          # 客观体检：时长/峰值/RMS/削波

# 透明底素材看不见形状？合成到深色底出预览图再自己看（P1 图标是纯白+alpha，白底看图器里全白）
python3 $S/preview_alpha.py /tmp/preview.png MediaRaw/images/ui_icon_inktank.png
```

成功输出最后一行 `DONE: <文件路径>`；失败看 stderr 提示，处理后重跑。

## 任务路由（只读你需要的那个文件）

| 任务 | 去处 |
|---|---|
| 风格锚定 / **中性单色图标** / 可平铺地面贴图 / 墨点遮罩 / 图标网格 / 改图 / 透明底管线 | `references/workflow-style-lock.md` ★先读这个 |
| **验证透明底素材的形状**（白图标在白底看不见） | `scripts/preview_alpha.py`（合成到深灰/棋盘格出预览图） |
| 生图参数、返回格式、故障 | `references/api-image.md` |
| 语音音色控制、逐字技巧、播报员台词 | `references/api-voice.md` |
| BGM 提示词公式、**Unity 循环方案 / OGG 编码器探测** | `references/api-music.md` |
| **素材落地路径、Unity 导入设置、能力边界** | `references/usage-guide.md` ★落地必读 |
| 本技能的历史与迭代规则 | `CHANGELOG.md` |

## 落地（P1 专属，和 Godot 项目不一样）

**Unity 只扫描 `Assets/` 下内容，且导入设置存在 `.meta` 里——而 AGENTS.md 规则 1 与
`optimize-audio` 技能都禁止手改 `.meta`。** 所以本项目的做法是「按路径自动套」：

| 素材 | 拷到这里 | 自动获得的导入设置 |
|---|---|---|
| UI 图标 / 精灵 / 墨点遮罩 | `Assets/_Project/Art/Sprites/` | Sprite、alphaIsTransparency、mips off、Clamp |
| 世界贴图（地面） | `Assets/_Project/Art/Textures/` | Default、mips on、Repeat |
| BGM | `Assets/_Project/Audio/Music/` | Streaming + Vorbis + loadInBackground |
| 音效 | `Assets/_Project/Audio/SFX/` | DecompressOnLoad + **PCM** + preload |
| 语音 | `Assets/_Project/Audio/Voice/` | CompressedInMemory + Vorbis q0.8 + forceToMono |

```bash
cp MediaRaw/images/ui_icon_inktank.png Assets/_Project/Art/Sprites/
# 让编辑器导入（不导入 = 图白块、音哑的）
"$HOME/.unity/bin/unity" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'
# 体检：确认设置真对上了（agent 读 console 拿结果，不靠用户截图）
"$HOME/.unity/bin/unity" command script.eval --code 'P1.Editor.MediaTools.ReportMedia();'
"$HOME/.unity/bin/unity" command console.get --tail 40
```

- **不要把整个 `MediaRaw` 拷进 `Assets/`**：会连带 `.raw.jpg`、草图、落选稿一起入库。
- 那两张目录下的导入设置**以 `MediaImportPostprocessor.cs` 为准**，
  在 Inspector 手改会被下次重导入覆盖——要改行为就改那个 C# 文件。

## 模型选择

| 用途 | 模型 | 备注 |
|---|---|---|
| 生图（默认，**当前唯一可用**） | `gemini-3.1-flash-image` | 支持参考图锁定风格，11~45s |
| ~~生图（草稿/批量）~~ | ~~`gemini-3.1-flash-lite-image`~~ | **2026-10-28 实测通道被上游禁用**（400 `PROXY_GENERATION_DISABLED`），别再用；草稿也用品质模型 |
| 语音（默认） | `gemini-3.8-flash-tts` | WAV 24kHz |
| 语音（批量草稿） | `gemini-3.8-flash-lite-tts` | 更便宜，实测可用 |
| 音乐（主用） | ~~`lyria-3.5`~~ **2026-10-29 起上游禁用** | 请求返回 400 `PROXY_GENERATION_DISABLED`。BGM 改用 **`python3 tools/bake_bgm.py`**（本地确定性合成，零配额） |
| 音乐（备用，自动回退） | `lyria-3-pro-preview` | 已接入 `gen_music.sh` 的模型链：主用失败后自动尝试。**2026-10-29 实测同样被禁用**（400），两个都不可用时脚本会打印上游原文并提示改用 `bake_bgm.py` |
| 音效 SFX | **不走网关** | `python3 tools/bake_sfx.py`（纯标准库离线合成，零配额） |
| ~~sensenova-u1.5-lite~~ | 网关上游配置错误，**暂不可用** |
| ~~gpt-image-*~~ | 通道无鉴权，不可用 |

> **名单里有 ≠ 真能用**：`/v1/models` 仍会列出已禁用的模型（如 flash-lite-image）。
> `selfcheck.sh` 会专门标注这类「在名单但已知不可用」的模型。

## 已知陷阱

- 本机 shell 的 `ALL_PROXY` 指向失效代理：手动 curl 必须加 `--noproxy '*'`（脚本已内置）
- 网关 `/v1/models` **需要鉴权**，不带 header 返回 `401 Missing API key`，
  别误判成「网关没有模型」（`selfcheck.sh` 已区分 401 与不可达）
- 生图**没有透明通道**：一律走 `--keyout` 品红管线
- 响应里媒体位置不固定（`message.images` 或 content 内嵌 markdown），提取交给脚本。
  **TTS 的 wav 也在 `images` 字段里**，这是实测结论不是笔误
- 模型偶发只回文本不出图/音频：脚本已自动重试 2 次
- TTS 风格指令直接写进台词**会被朗读出来**——必须走 `--style`（脚本自动转方括号舞台指令）
- `bake_sfx.py` 不能用内置 `hash()` 做种子（Python 3 逐进程随机化）——
  已改用 FNV-1a，改脚本时别退回去，否则每次烘焙的 wav 都不一样
- **纯白 + alpha 的图标在白色背景的看图器里完全看不见**（这是 P1 的既定约定，不是 bug）：
  检查形状一律先跑 `scripts/preview_alpha.py` 合成到深灰/棋盘格上再看，别对着白图困惑
- `--grid` 配合 `--keyout` 时，**每个分格会被各自裁到内容边界，所以尺寸不一致**
  （实测 2x2 出 256×252 / 256×213 / 256×58 / 256×256）。当独立精灵用没问题；
  要拼成严格等格图集就别逐格 keyout，或事后重排

## 维护规则（后续 agent 必须遵守）

- 发现 API 行为变化（端点/参数/新模型/修复的通道）→ **更新对应 `references/api-*.md`
  的实测内容，并在 `CHANGELOG.md` 追加一行**（日期 + 变更 + 你的发现）
- 新增脚本参数时同步更新脚本头注释与本文件示例
- `bake_sfx.py` 加音效族后，同步更新本文件与 `usage-guide.md` 里的族数
- 不要改动 `config/api.env` 格式；密钥已进 `.gitignore`，不要把密钥写进任何其他文件
- 改 `MediaImportPostprocessor.cs` 的路径约定时，必须同步 `usage-guide.md` 与
  `MediaRaw/README.md` 里的路径表（三处别漂）