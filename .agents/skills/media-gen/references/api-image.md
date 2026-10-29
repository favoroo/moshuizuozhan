# 生图 API 实测笔记（2026-10-07 实测于本机网关；2026-10-28 移植到 P1 时复核）

## 结论速查

| 模型 | 端点 | 默认输出 | 实测耗时 | 用途 |
|---|---|---|---|---|
| `gemini-3.1-flash-image` | `/v1/chat/completions` | JPEG 1408×768 | 11~45s | **当前唯一可用**：质量/关键视觉/参考图锁定/图标（默认） |
| ~~`gemini-3.1-flash-lite-image`~~ | `/v1/chat/completions` | — | — | **2026-10-28 实测已不可用**，见下方「已知问题 0」 |

## ★ 已知问题 0（2026-10-28 新发现，最要紧）

`gemini-3.1-flash-lite-image` 现在返回 **HTTP 400**（约 20s 后）：

```json
{"error":{"code":400,"message":"Proxy browser error: Google API returned error:
 400 INVALID_ARGUMENT {\"error\":{\"code\":400,
 \"message\":\"PROXY_GENERATION_DISABLED: generateContent is not supported\", ...}}"}}
```

即**网关上游把该模型的 generateContent 关掉了**。同一时刻
`gemini-3.1-flash-image`（质量模型）返回 200 且正常出图，
纯文本模型 `gemini-3-flash` 也正常 —— 所以不是鉴权或网络问题，
是**该模型通道被单独禁用**。

**因此：批量草稿也请用 `gemini-3.1-flash-image`（脚本默认值），别再传 `-m ...lite-image`。**
脚本对这种情况会浪费两次重试（约 40s）才报错，别误判成"脚本坏了"。
通道修好后请实测并更新本节、CHANGELOG 与 SKILL.md 的模型表。

## 请求（通过 chat completions）

纯文生图：

```bash
curl -s --noproxy '*' http://127.0.0.1:8317/v1/chat/completions \
  -H "Authorization: Bearer $MEDIA_API_KEY" -H "Content-Type: application/json" \
  -d '{"model":"gemini-3.1-flash-image","messages":[{"role":"user","content":"生成一个游戏 UI 图标：墨水罐，纯白单色剪影，白色背景"}]}'
```

带参考图（图生图/风格锁定，`image_url` 可放多块，官方上限 14 张）：

```json
{"model":"gemini-3.1-flash-image","messages":[{"role":"user","content":[
  {"type":"image_url","image_url":{"url":"data:image/jpeg;base64,<B64>"}},
  {"type":"text","text":"以参考图完全一致的艺术风格，生成：<内容描述>"}
]}]}
```

中文提示词实测可用。宽高比靠提示词文字控制（实测 9:16 → 768×1376，
横版默认 1408×768，方形（lite 模型）1024×1024）。

## 响应（形状不固定，必须用 lib_extract.py 提取）

图片 base64 出现在**两种位置之一**：
- `choices[0].message.images[0].image_url.url` → `data:image/jpeg;base64,...`
- `choices[0].message.content` 内嵌 markdown：`![Generated Image](data:image/jpeg;base64,...)`

偶发只回文本不出图（约 1/10），重试即可（脚本已内置 2 次重试）。

## 关键实测事实

- **无 alpha 通道**：明确要求透明底也返回 JPEG。透明底走 `--keyout` 品红管线，
  见 workflow-style-lock.md。
- 图内含 C2PA 内容凭证元数据（jumd/c2pa 段），属正常，Unity 导入无影响。
- 参考图风格延续效果很好（实测苹果→药水瓶，线条/光泽/阴影/母题全继承）。
- 参考图的**构图母题也会被继承**：只想要画风时，prompt 里明写
  「仅保持参考图的画风，构图按我的描述来」。

## P1 使用要点

- **UI 图标出中性单色**（白主体 + alpha），颜色交给 Unity 染色 —— 见 workflow-style-lock.md 工作流 2。
- **地面贴图不加 `--keyout`**（要满铺），且必须低饱和低对比可平铺 —— 工作流 3。
- 图标 `--keyout 256`；关键视觉/大图按需 `--keyout 1024` 或不抠。
- 落地路径必须选对，导入设置才自动对：
  - 图标/精灵 → `Assets/_Project/Art/Sprites/`
  - 世界贴图 → `Assets/_Project/Art/Textures/`
  - 尺寸上限：Sprites 自动压到 512，Textures 到 1024/2048（见 `MediaImportPostprocessor.cs`）

## 已知问题（2026-10-07，P1 复核未变）

1. `sensenova-u1.5-lite`：网关 `/v1/models` 有名单，但转发上游报
   `5: model is not found`（provider=openai-compatible-sensenova）。
   → 需网关管理员修正上游模型名；修好后请实测并更新本文件与 CHANGELOG。
2. `gpt-image-1.5/2/2.5`（含原生透明底能力）：报
   `auth_not_found (providers=codex)`，通道未配鉴权。若日后配好，
   透明底可改走 `/v1/images/generations` 的 `background:"transparent"`，优先级高于品红管线。
3. `/v1beta/interactions` 原生透传存在，但其 `speech_config`/voice 的 proto 结构与
   官方文档示例不符（报 `Cannot find field`），未继续攻坚——chat 路径已覆盖全部需求。
4. `/v1/images/generations`、`/v1/audio/speech`、`/v1/interactions` 对本技能模型均 404/不支持。
5. `/v1/models` **需要鉴权**：不带 `Authorization` 返回 `401 {"error":"Missing API key"}`。
   别把它误判成「网关没有模型」——`selfcheck.sh` 已带鉴权并会区分 401 与不可达。

## 官方参考文档

- 生图：<https://aistudio.google.com/docs/image-generation>
  （要点：参考图上限 14 张、多轮编辑 previous_interaction_id、
  response_format 的 aspect_ratio/image_size 参数——chat 通道不透传这些参数，
  一律用提示词控制）