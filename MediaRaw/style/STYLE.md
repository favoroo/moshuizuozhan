# P1 项目风格档案 —— 生成任何美术/语音素材前必读并遵循

> **当前状态：已回填（2026-10-29）。画风基准 = INKWAVE（Turf Riot）街头潮流风。**
> 参考对象是 `inkwave-aah-research/` 里的 INKWAVE 逆向资料，**不是任天堂 Splatoon 的角色/名称/美术**。
> 生成正式素材时必须带本文件的 `STYLE_PREFIX` 并传锚图 `-r`；
> 本档案所有字段一旦填写，后续生成必须**原样遵循**，不得即兴改写。
>
> **2026-10-31 用户决策（P18 画质增强）**：3D 实机基调**保留既有明亮浅底**，只做质感增强
> （湿墨环境反射 / 微表面颗粒 / 接触遮蔽 / 分离调色 / 屏幕液体特效）。本文件「深炭紫底」
> 仅作为 **UI 深底与可选项**保留（烘焙备选贴图 `ArenaGround_Albedo_V2`），不作为 3D 场景基调；
> 3D 基调以菜单「P1/画风/4 画质增强」写入的明亮档为准，「P1/画风/1」为旧扁平哑光档一键回退。

---

## 风格前缀 STYLE_PREFIX

```
Inkwave-inspired street ink graffiti, chunky rounded silhouettes with hard 2-3px charcoal outlines,
saturated team-color inks (orange vs cobalt) over deep charcoal-violet surfaces,
glossy wet highlights with subtle ink drips and splat shapes,
soft contact shadows and high-contrast lighting,
UI built from pill/blob shapes, offset hard shadows and springy overshoot
```

## 风格锚图 anchor

- 路径：`inkwave-aah-research/assets/stages/tidewater-day.webp`
- 说明：INKWAVE 的 Tidewater 日景关卡预览，作为**风格参考锚图**（构图、体块、墨色对比、光照方向）；
  仅用于锁画风，不直接作为 P1 素材发布。UI 细节另参考
  `inkwave-aah-research/assets/icons/` 与 `inkwave-aah-research/styles/ui.css`、`hud.css`。
- 登记日期：2026-10-29

## 全局画面约束

- **光照方向**：主光从左上方 45° 打入，暖白偏中性；右侧冷色补光；地面/物体接触处保留柔和接触阴影，
  不追求写实全局光照，优先保证俯视角下高低差与掩体轮廓清晰。
- **色板倾向**：
  - 深底：`#15121C`（Inkwave 的 `--k`）、`#1F1A2C`（`--k2`）、`#2C2540`（`--k3`）
  - 浅色文字/高光：`#FFFFFF`、`#EBE8F3`、`#C3BDD6`（muted）
  - 队伍色：见下节；强调色只用队伍色 + 少量金黄 `#FFC48A` / 警报红 `#FF3A47`
  - 地面/场景基础色调保持低饱和、低对比，避免与队伍墨色抢视觉焦点。
- **剪影要求**：体块圆润、厚实、外轮廓完整；禁止细线、碎线、毛刺；俯视角缩小到 1/4 屏时仍能一眼读出
  “人 / 乌贼 / 武器 / 掩体”。角色与武器必须有 2–3px 深色硬描边（Unity 内用几何体+描边材质/后处理实现，
  不靠贴图烘焙）。
- **UI 风格**：
  - 字体：标题 `Titan One`，正文/数值 `Rubik`（Black/Bold）；中文走 TMP 回退。
  - 形状：胶囊 `border-radius: 999px`、墨滴圆角（`43% 57% 51% 49% / 47% 42% 58% 53%` 一类不规则圆角）、
    2–3px 纯黑硬描边 + 向下偏移的硬阴影（不是高斯柔影）。
  - 动效：弹性过冲 `cubic-bezier(.34, 1.56, .64, 1)`；按下缩放 0.96、悬停上浮 2–4px、
    卡片入场 0.35–0.55s；数值变化用弹簧插值，不用线性 lerp。
  - 装饰：墨滴、飞溅、斜切高光、网点/半调（dither/halftone），但**不能遮挡可读信息**。
  - 参考令牌：`inkwave-aah-research/styles/ui.css`（`--spring`、`--spring2`、`--out`）与
    `hud.css`（`.iw-timer__blob`、`.iw-kcard`、`.iw-ret`、`.iw-hit`）。

### 队伍配色（P1 专属，Splatoon 类玩法的关键约束）

墨色即阵营标识，必须满足：

- **队 A 墨色**：`#FF700F`（当前运行时 `teamAColor`，橙）
- **队 B 墨色**：`#266BFF`（当前运行时 `teamBColor`，钴蓝）
- **中性墨色**（空场地/未涂/无阵营）：`#383D47`（当前 `neutralGroundColor`，深灰蓝）
- **UI 深底/描边**：`#15121C`；**UI 浅字/高光**：`#FFFFFF` / `#C3BDD6`

约束：两队墨色需**色相互补或明度拉开**，且都能在底面贴图上清晰可读；
红绿色盲友好性需单独确认（不要只靠红/绿对立区分阵营）。
**素材不要硬编码队伍颜色**——UI 图标一律出中性单色（白主体 + alpha），
颜色交给 Unity 按队伍 `Image.color` 染色，一套图标服务两队（详见 usage-guide.md）。

> INKWAVE 参考值：`--a: #FF8A14` / `--b: #2F5BFF`。P1 当前运行时略深一档，
> 二者视觉语义一致；以 P1 运行时值为准，生成素材时不要写成 Inkwave 的十六进制。

## 音乐风格

（写清曲风/情绪/主要乐器；BGM 必须 instrumental only、可循环）

> 2026-10-29 回填。
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

### 角色 / 阵营（按需填写；v1 为程序化角色，通常不需要）

| id | 角色 | 音色/语气描述 |
|---|---|---|
| | | |

## 负面约束

（写进提示词末尾的通用否定项）

- 无文字、无水印、无签名
- **不得使用任天堂（Splatoon 等）的角色、名称、美术风格或音效**（GamePlan R5）；
  INKWAVE 仅作为街头潮流 UI/材质语言的参考，不复制其角色、Logo、关卡原画
- 图标类：主体居中饱满、不接触画面边缘、背景纯色无投影
- 地面贴图：低饱和、低对比、可平铺（见 usage-guide.md，否则与墨色打架）
- 不要写实照片感、不要细线描边、不要大面积纯白/纯黑无层次、不要高斯柔影代替硬偏移阴影
- 不要给武器/角色加发光霓虹轮廓（会破坏俯视角阵营色识别）

---

## 维护规则

- 用户说明画风后**立即回填本文件**，不要只在对话里记着。
- 本文件一旦有值，后续所有生成都必须带上 STYLE_PREFIX 并传锚图 `-r`。
- 改动本文件属于「换画风」级别的事件：需同步 `CHANGELOG.md` 记一行，
  并知会用户（已产出的素材不会自动跟随新画风）。

## 参考来源（只读）

- `inkwave-aah-research/styles/ui.css`：UI 弹簧曲线、按钮/卡片/描边/阴影令牌
- `inkwave-aah-research/styles/hud.css`：计时器墨滴、击杀卡、准星、小地图、血条
- `inkwave-aah-research/assets/stages/`：Tidewater / Kelpline 等关卡预览
- `inkwave-aah-research/assets/icons/`：68 个单色矢量图标（UI/武器/规则/技能）
- `inkwave-aah-research/reports/RESEARCH_REPORT.md`：材质、武器、HUD、角色控制的技术细节