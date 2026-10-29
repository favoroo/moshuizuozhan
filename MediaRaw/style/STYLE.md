# P1 项目风格档案 —— 生成任何美术/语音素材前必读并遵循

> **当前状态：空白（项目画风未定）。**
> 按 `Docs/GamePlan.md`，P1 现阶段美术一律用**基础几何体占位**，画风决定推迟到 M6。
> 在下方字段回填之前，**禁止生成任何「正式」素材**——唯一例外是管线冒烟测试
> （用无风格的中性主体，产物标注为测试件、不当作项目美术）。
>
> 回填方式见 `.agents/skills/media-gen/references/workflow-style-lock.md` 工作流 1。
> 本档案所有字段一旦填写，后续生成必须**原样遵循**，不得即兴改写。

---

## 风格前缀 STYLE_PREFIX

（一句短语，建议格式：`<风格名>、<线条/体块特征>、<配色特征>、<光影特征>`）

```

```

## 风格锚图 anchor

（先有锚图才能锁画风。锚图挑选标准：剪影清晰、颜色对比强、光影简单。
不满意就重生成，锚图值得花时间。）

- 路径：
- 说明：
- 登记日期：

## 全局画面约束

- 光照方向：
- 色板倾向：
- 剪影要求：
- UI 风格：

### 队伍配色（P1 专属，Splatoon 类玩法的关键约束）

墨色即阵营标识，必须满足：

- **队 A 墨色**：`#`
- **队 B 墨色**：`#`
- **中性墨色**（空场地/未涂/无阵营）：`#`

约束：两队墨色需**色相互补或明度拉开**，且都能在底面贴图上清晰可读；
红绿色盲友好性需单独确认（不要只靠红/绿对立区分阵营）。
**素材不要硬编码队伍颜色**——UI 图标一律出中性单色（白主体 + alpha），
颜色交给 Unity 按队伍 `Image.color` 染色，一套图标服务两队（详见 usage-guide.md）。

## 音乐风格

（写清曲风/情绪/主要乐器；BGM 必须 instrumental only、可循环）

> 2026-10-29 回填（仅音频区；画风仍空白，禁止生成正式美术）。
> P5-C 对局双段 BGM：街头朋克爵士 / 电子爵士。

- 主阶段（120s 对局前 60s）：`street punk jazz / electro-jazz, bright brass stabs, funky walking bass, breakbeat drums, 132 BPM, nimble and confident`
- 决战阶段（最后 60s）：`high-intensity punk jazz finale, distorted punk bass, wailing brass, four-on-the-floor electronic drums, 150 BPM, urgent and explosive`
- 共同约束：`seamless loop, instrumental only, no vocals`；不点名现实乐队/艺人。

## 说话人 → 语音音色 VOICES

（同一说话人的音色描述**固定下来、原样复用**，不要每次即兴改写，
否则同一角色在不同台词里音色会漂移。）

### 播报员（Splatoon 类玩法固定需要：开场倒数、时间到、领先/落后、结算）

| id | 用途 | 音色/语气描述 |
|---|---|---|
| announcer | 对局播报、倒计时、结算宣告 | `energetic male stadium announcer, bright punchy voice, crisp articulation, hyped but precise` |
| announcer_urgent | 最后 30 秒/加时的紧迫播报 | `same energetic male stadium announcer, urgent driving delivery, faster pace, emphatic stress` |

### 角色 / 阵营（按需填写；v1 为几何体占位，通常不需要）

| id | 角色 | 音色/语气描述 |
|---|---|---|
| | | |

## 负面约束

（写进提示词末尾的通用否定项）

- 无文字、无水印、无签名
- **不得使用任天堂（Splatoon 等）的角色、名称、美术风格或音效**（GamePlan R5）
- 图标类：主体居中饱满、不接触画面边缘、背景纯色无投影
- 地面贴图：低饱和、低对比、可平铺（见 usage-guide.md，否则与墨色打架）

---

## 维护规则

- 用户说明画风后**立即回填本文件**，不要只在对话里记着。
- 本文件一旦有值，后续所有生成都必须带上 STYLE_PREFIX 并传锚图 `-r`。
- 改动本文件属于「换画风」级别的事件：需同步 `CHANGELOG.md` 记一行，
  并知会用户（已产出的素材不会自动跟随新画风）。