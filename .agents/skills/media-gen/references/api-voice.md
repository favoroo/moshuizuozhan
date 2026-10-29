# 语音合成 API 实测笔记（2026-10-07 实测；2026-10-28 移植到 P1 时复核）

## 结论速查

| 模型 | 端点 | 输出 | 实测耗时 | 用途 |
|---|---|---|---|---|
| `gemini-3.8-flash-tts` | `/v1/chat/completions` | WAV 24kHz 16bit 单声道 | 4s 台词 ≈ 3.5s | 播报/台词（默认） |
| `gemini-3.8-flash-lite-tts` | `/v1/chat/completions` | 同上 | 更快更便宜 | 草稿批量试听 |

P1 复核（2026-10-28）：两个 TTS 模型都在网关 `/v1/models` 名单里，通道可用。

`/v1/audio/speech`（OpenAI TTS 端点）对本模型 **404**，不要用。

## 请求（通过 chat completions，音色/语气靠提示词指令控制）

```bash
curl -s --noproxy '*' http://127.0.0.1:8317/v1/chat/completions \
  -H "Authorization: Bearer $MEDIA_API_KEY" -H "Content-Type: application/json" \
  -d '{"model":"gemini-3.8-flash-tts","messages":[{"role":"user","content":"[语气轻快的男声]三、二、一，开始！"}]}'
```

## 响应与提取

音频 base64 出现在 `choices[0].message.images[0].image_url.url`（data:audio/wav;base64,...）
——**注意 TTS 的音频也在 `images` 字段里**，用 `lib_extract.py ... audio` 提取即可。

## 音色与语气控制（重要，实测定论）

- 网关不透传 Google 原生的 `voice`/`speech_metadata` 参数（官方 interactions API 的
  用法在本网关走不通，见 api-image.md 已知问题 3）。
- **风格指令直接写进提示词会被朗读出来**（时长对照实验，台词固定为"你好"）：

  | 写法 | 总时长 | 结论 |
  |---|---|---|
  | 无指令 | 1.0s | 基线 |
  | `用欢快的语气说：`（3 字指令） | 3.1s | 被朗读 |
  | 22 字指令 | 4.9s | 被朗读（时长随指令长度增长） |
  | `[22 字方括号指令]` | 0.9s | **不朗读** ✓ |
  | `[轻声耳语]` | 2.3s | 不朗读，"耳语"本身慢 |

- **正解：方括号舞台指令**——`[低沉沙哑的中年男声，缓慢阴森]欢迎来到地牢……`，
  模型把方括号内容当表演指示而非台词（`gen_voice.sh` 的 `--style` 已自动加方括号）。
- **说话人音色一致性**：每个说话人的方括号音色描述固定下来，登记在
  `MediaRaw/style/STYLE.md` 的 VOICES 表，每次合成原样复用，不要即兴改写。
- 原生 `/v1beta/interactions` + `speech_config.voice_name`（如 Kore）实测**时好时坏**
  （同请求约半数返回 400 "Unknown name voiceName"），带 `annotations` 的请求全部 400
  → 原生路径仅作实验性备选，不要依赖。

## 逐字控制技巧（来自官方文档，chat 通道同样适用）

- 文本按逐字稿处理：标点控制停顿，需要明显停顿可加破折号或省略号
- 强调某个词可以换行或加引号
- 官方还支持内联标签（`<laugh>`、`<sigh>`、`<short pause>` 等），chat 通道未验证，可尝试

## P1 使用要点

Splatoon 类玩法固定需要**播报员**（GamePlan M5「3 分钟倒计时/结算」）：

| 场景 | 台词示例 |
|---|---|
| 开场倒数 | `[语气明快的播报员男声]三、二、一，开始！` |
| 最后 30 秒 | `[语速加快、略带紧迫]最后三十秒！` |
| 时间到 | `[干脆利落的播报员男声]时间到！` |
| 领先 | `[语气上扬]我方领先！` |
| 结算胜利 | `[语气热烈]我方获胜！` |
| 结算失败 | `[语气低沉但不失礼]惜败，再战一场！` |

- 播报员的音色描述要**在 STYLE.md 的 VOICES 表里固定**（id: `announcer`），
  否则每次生成的音色会漂移。
- 命名：`vo_announcer_<场景>_<序号>.wav`，放 `MediaRaw/voice/`。
- 生成后放 `Assets/_Project/Audio/Voice/`，导入设置自动套
  CompressedInMemory + Vorbis q0.8 + forceToMono。
- **上线前用多模态 chat 模型对音频做转写自检**，确认没把方括号指令读进去
  （方括号机制是实测结论，但模型行为偶有波动，成批生成后值得抽查）。

## 官方参考文档

- 语音：<https://aistudio.google.com/docs/speech-generation>
  （要点：30 个预置音色名如 Kore/Puck/Zephyr、style 标注、内联标签、
  response_format.mime_type 可选 audio/wav 或 audio/l16——chat 通道不透传这些参数）