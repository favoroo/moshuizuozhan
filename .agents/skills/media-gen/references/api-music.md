# 音乐生成 API 实测笔记（2026-10-07 实测；2026-10-28 移植到 P1 时复核）

> **2026-10-29 P5-C 实测：`lyria-3.5` 已被上游禁用。**
> 请求返回 `400 {"code":400,"message":"Proxy browser error: Google API returned error:
> 400 INVALID_ARGUMENT ... PROXY_GENERATION_DISABLED: generateContent is not supported"}`。
> 重试两次、换更短提示词均失败；网关模型名单里也没有其它音乐模型。
> **需要 BGM 时改用项目本地烘焙：`python3 tools/bake_bgm.py`**
> （纯离线程序化合成，固定种子、零配额；产物 `MediaRaw/music/*.ogg` + `Assets/_Project/Audio/Music/*.ogg`）。
> 网关恢复后再用 `gen_music.sh` 重新生成即可，脚本与 `--ogg` 流程保持有效。

## 结论速查

| 模型 | 端点 | 输出 | 实测耗时 |
|---|---|---|---|
| `lyria-3.5` | `/v1/chat/completions` | MP3 192kbps 44.1kHz 立体声 | ~34s，单段约 2 分钟 |
| ~~`lyria-3.5`（2026-10-29 起）~~ | ~~同上~~ | **400 `PROXY_GENERATION_DISABLED`，暂不可用** | 改用 `python3 tools/bake_bgm.py` 本地合成 |

P1 复核（2026-10-28）：`lyria-3.5` 在网关 `/v1/models` 名单里，通道可用。

> **它是「写歌」模型，不是音效模型。** 实测（2026-10-08）提示词写
> `[0:00 - 0:01] a single 0.15 second UI button click sound only, ... then silence`，
> 仍返回 **56.1 秒 / 1.3 MB** 的连续素材，能量分布是「前段稀疏、16-32s 越演越响（RMS 0.25）」——
> 模型把请求当成一首曲子来展开，短音效要的干脆瞬态做不出来，剪开头也只是淡入片段。
> **短音效请走本地烘焙 `python3 tools/bake_sfx.py`，不要调本接口。**（17 族 23 文件，零配额）

## 请求（通过 chat completions）

```bash
curl -s --noproxy '*' http://127.0.0.1:8317/v1/chat/completions \
  -H "Authorization: Bearer $MEDIA_API_KEY" -H "Content-Type: application/json" \
  -d '{"model":"lyria-3.5","messages":[{"role":"user","content":"An energetic electronic game loop, upbeat competitive theme, instrumental only, no vocals"}]}'
```

## 响应与提取

MP3 以 `data:audio/mpeg;base64,...` 形式嵌在 `choices[0].message.content` 的
markdown 图片语法里（`![Generated Image](data:audio/mpeg;base64,...)`），
前缀可能带 `[[A0]]` 等分段标记，用 `lib_extract.py ... audio` 提取即可。

## ★ Unity 循环播放：与 Godot 的关键差异（务必读懂）

| | Godot | **Unity** |
|---|---|---|
| 导入期 loop 开关 | ✅ MP3 勾 loop 即无缝 | **❌ 没有**——循环是运行时的 `AudioSource.loop = true` |
| MP3 循环接缝 | 无缝 | **有可听间隙**（MP3 编码器 padding/解码器延迟） |
| 无缝方案 | 直接用 MP3 | **用 OGG(Vorbis)** |

所以需要无缝循环的曲目，生成时加 `--ogg`：

```bash
.agents/skills/media-gen/scripts/gen_music.sh \
  -p "intense competitive turf war battle theme, driving electronic percussion, 140 BPM, seamless loop, instrumental only, no vocals" \
  --ogg -o MediaRaw/music/bgm_match
# → MediaRaw/music/bgm_match.mp3（源，保留）+ bgm_match.ogg（入库件，无缝循环）
```

- 代价：MP3→OGG 是**二次有损转码**。源 MP3 会一并保留，需要更高保真可自行从源重转。
- **编码器探测（2026-10-28 实测本机环境，重要）**：本机 ffmpeg 8.1.2 **不含 `libvorbis`**，
  只有内置 `vorbis` 编码器，而内置版**只支持 2 声道**（单声道输入直接报错
  `Current FFmpeg Vorbis encoder only supports 2 channels`）。所以脚本按优先级探测：
  1. `oggenc`（vorbis-tools，走真 libvorbis，质量最好）—— 本机**未安装**
  2. ffmpeg `libvorbis` —— 本机没有
  3. ffmpeg 内置 `vorbis` + `-strict experimental -ac 2 -b:a 192k` —— **本机走这条**
  
  第 3 条能用但音质低于标准 Vorbis。想要更好音质：
  `brew install vorbis-tools`（提供 oggenc）后**重跑同一条命令**即可，脚本会自动优先用它。
  lyria 输出本身就是立体声，所以 `-ac 2` 不损失信息；它只是为了让内置编码器接受输入。
- **一次性播放**的曲子（结算音乐、开场短曲）不必转，直接用 MP3 省事。
- 落地：`Assets/_Project/Audio/Music/` → 导入设置由 `MediaImportPostprocessor.cs`
  自动套 **Streaming + Vorbis + loadInBackground**（BGM 不常驻内存）。
- `manifest.jsonl` 登记的是**入库目标**（带 `--ogg` 时为 ogg 路径），源 MP3 与它同名前缀并列存放。

## 提示词写法（官方规则）

- 没有独立的 BPM/时长/负面提示参数，**全部写进提示词文本**
- 曲风/情绪/乐器/BPM 直接描述；时长与结构用段落标签（`[Verse]`、`[Chorus]`、`[Bridge]`）
  或时间戳（`[0:00 - 0:10]`）控制
- 纯音乐（游戏 BGM 必须）：写 `instrumental only, no vocals`
- 无负面提示词：不想要的元素用正面表述绕开
- 安全过滤：点名歌手音色、受版权保护歌词会被拒
- 每次生成结果非确定性，同一提示词多生成几条挑选即可

## P1 使用要点

对局 3 分钟（GamePlan §4.6），配乐参考公式 `<场景用途> + <曲风> + <情绪> + <乐器> + BPM`：

| 用途 | 提示词骨架 | 是否 `--ogg` |
|---|---|---|
| 对局 BGM | `competitive turf war battle theme, driving electronic percussion and bass, high energy, 140 BPM, seamless loop, instrumental only` | ✅ 要循环 |
| 倒计时/开场 | `short punchy match intro sting, rising synth, 4 seconds feel, instrumental only` | ❌ 单次 |
| 结算胜利 | `victory jingle, bright and celebratory, instrumental only` | ❌ 单次 |
| 结算失败 | `defeat jingle, low key and consoling, instrumental only` | ❌ 单次 |
| 菜单（M6） | `main menu theme, calm and inviting, instrumental only` | ✅ 要循环 |

- 命名：`bgm_<场景>_<情绪>.mp3/.ogg`。
- 单段只有约 2 分钟，而一局 3 分钟 → 必须靠循环覆盖；**所以对局 BGM 的循环质量比曲子本身更重要**，
  一律加 `--ogg`。
- 文档里提到的「seamless loop」提示词只是加分项，**不保证**；真正的无缝靠 OGG + `AudioSource.loop`。

## 官方参考文档

- 音乐：<https://aistudio.google.com/docs/music-generation>
  （要点：lyria-3.5 官方走 interactions API，本网关映射为 chat completions，
  请求/响应字段以本笔记实测为准）