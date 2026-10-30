# 漫画风 UI 规范（Comic Style / P10）

本文件是 P1 全部 UI 的**唯一视觉与交互规范**。改任何 HUD / 菜单 / 触控界面前先读这里；
和本文冲突的实现一律视为 bug（包括"只是好看一点"的渐变、柔边阴影、弹簧回弹）。

参考来源：`https://www.stylekit.top/styles/comic-style/showcase`，落地时按 Unity uGUI 的能力做了等价映射。

---

## 1. 设计 token

| 类别 | Token | 值 | 说明 |
|---|---|---|---|
| 墨线 | `ComicStyle.Ink` | `#1A1A1A` | 所有边框、硬阴影、纸上文字 |
| 纸面 | `ComicStyle.Paper` | `#FFFEF0` | 面板主底色 |
| 次级纸面 | `ComicStyle.PaperAlt` | `#F2EFD8` | 列表行 / 内嵌区块 |
| 强调红 | `ComicStyle.Red` | `#FF3333` | 危险、失败、K.O. |
| 强调黄 | `ComicStyle.Yellow` | `#FFCC00` | 就绪、选中、NEW! |
| 强调蓝 | `ComicStyle.Blue` | `#3366FF` | 信息、链接 |
| 强调绿 | `ComicStyle.Green` | `#33CC33` | 成功 |
| 队伍色 | `theme.GetTeamColor(team)` | 橙 `#FF7314` / 蓝 `#296BFF` | **不改**，与场地墨水/小地图一致 |

**度量**

| 项 | 值 | 备注 |
|---|---|---|
| 面板圆角 | 5px（`CornerSharp`） | 近似直角；规范禁止 rounded-lg/xl |
| 气泡圆角 | 32px（`CornerBubble`） | 对话气泡是唯一允许大圆角的地方 |
| 墨线宽度 | 4 / 6 / 8px | 细:徽章 4，常规:面板按钮 6，重点:标题/弹窗 8 |
| 硬阴影 | 偏移 (6,-6)，实心墨色 | **无模糊**；悬停 (3,-3)，按下 (0,0) |
| 半调网点 | 网格 8px，点径 1.3px，墨色 14%（悬停 22%） | 大面板/背景；小徽章可关 |
| 动作线 | 24 条 3px 射线，墨色 18% | 大招就绪、结算、横幅 |
| 正文最小字号 | 16（HUD 18+，设置项 22+） | 触控目标 ≥132 UI px @1920×1080 |

## 2. 动效（硬切，无回弹）

| 状态 | 行为 | 时长 |
|---|---|---|
| 悬停 | scale 1.06 + rotate -3° + 硬阴影减半 + 网点 22% | 80ms |
| 按下 | 硬阴影归零 + scale 0.97 | 60ms |
| 入场 | scale 0.9 → 1 + 淡入，无过冲 | ≤120ms |
| 徽章弹出 | scale 0 → 1，起始 -7° → 0° | 80ms |

- 全部使用 `unscaledDeltaTime`：`Time.timeScale = 0` 的暂停菜单里动效依然有效。
- **禁止**：`bounce` / `elastic` / 过冲弹簧、`PlayWobble`、任何 > 0.1s 的交互动画。
- 主题里的弹簧已改为**临界阻尼**（damping = 1.0，频率 12~20Hz），数学上不可能过冲。

## 3. 组件与 API

### 面板 / 卡片
`UIInkShapeGraphic`（程序化网格，零贴图）：
```csharp
var panel = go.GetComponent<UIInkShapeGraphic>();
panel.ApplyComic(ComicStyle.Paper, dots: true);      // 一键套漫画观感
panel.Shape = UIInkShape.SpeechBubble;               // 对话气泡
panel.SpeechTail = true; panel.TailAngle = 270f;     // 尾角朝下
```
- `UIInkShape.Blob` 属于旧 Inkwave 语言，漫画风里只在极少数「墨滴」语义保留。
- 已有 `Image` 的物体上**不能** `AddComponent<UIInkShapeGraphic>()`：
  Unity 的 `Graphic` 带 `[DisallowMultipleComponent]`，会**静默返回 null**。
  改用 `UIInkShapeGraphic.CreateChildBackground(parent)` 在子物体铺底。

### 文本
```csharp
tmp.font = theme.ResolveFont(display: true, text);   // 含汉字自动切庆科黄油体/快乐体
```
- 字体链：`TitanOne`（拉丁数字）→ `ZCOOL QingKe HuangYou`（中文标题）；
  `Rubik-Bold`（拉丁正文）→ `ZCOOL KuaiLe`（中文正文）。
- 双层硬阴影字用 `UIChunkyText.Configure(...)`；拉丁自动大写（`fontStyle |= UpperCase`）。
- **任何 `new TextMeshProUGUI` 都必须显式设字体**，否则中文会退回默认拉丁字体显示成 □。

### 按钮
`UIJellyButton`（类名保留，行为已改漫画语义）：悬停阴影减半 + 网点浮现，按下阴影归零，不做果冻弹簧。
配合 `UISpringMotion` 驱动缩放/倾斜（两个组件都写 `localScale` 会打架，分工：`UISpringMotion` 管变换，
`UIJellyButton` 管墨线/网点/阴影）。

### 背景
- `ComicHalftoneGraphic`：全屏网点纸（`color = ComicStyle.Ink; Alpha = 0.07~0.10f`）。
- `ComicActionLinesGraphic`：放射动作线（大招就绪 / 胜利徽章）。
- `ComicBadge`：NEW! / 已选定 角标，`Play()` 弹出。

### 图标
```csharp
var sprite = UIIconLibrary.Get("mark_squid");   // RuntimeAssetLibrary → Resources → 编辑器
if (sprite == null) image.enabled = false;      // 绝不允许留白块
```
新增图标后必须跑 `P1/构建/11. 生成运行时资源库 (P1RuntimeAssets)`，否则真机取不到（编辑器有兜底，容易漏检）。

## 4. 禁止项（交付前逐条自查）

- [ ] 没有细边框（< 4px）与 `border-gray-*` 式低对比描边
- [ ] 没有渐变作为主要视觉（旧的 `glossAlpha` 必须为 0）
- [ ] 没有柔边/模糊阴影（只允许实心偏移）
- [ ] 没有大圆角面板（除对话气泡）
- [ ] 没有 bounce / elastic / 过冲弹簧 / `PlayWobble`
- [ ] 没有 > 0.1s 的交互动画
- [ ] 没有白底 Image 承载缺失 sprite（白方块）
- [ ] 没有中文文本使用纯拉丁字体
- [ ] 正文对比度 ≥ 4.5（`ComicStyle.ContrastRatio`；墨/纸 17.1，白/红 3.6 仅可用于 ≥32px 大字）
- [ ] 移动端触控目标 ≥ 132 UI px，且不与左右触控区重叠

## 5. 自检工具

| 工具 | 用途 |
|---|---|
| `P1/画风/漫画风/0. 构建中文字体与 Fallback` | 生成/修复 CJK TMP 资产 + fallback 链，并自检 1200+ 汉字覆盖 |
| `P1/画风/漫画风/1. 应用漫画主题` | 用 SerializedObject 把 token 写进 `UITheme.asset` / `HudGaugeStyle.asset`（不手改 YAML） |
| `P1/画风/漫画风/1b. 校验漫画 token 对比度` | 打印 WCAG 对比度 |
| `P1/画风/漫画风/2. 生成样式画廊场景` | 生成 `Scenes/ComicGallery.unity`：一屏看全面板/气泡/网点/动作线/按钮三态/字体样张 |
| `P1/构建/11. 生成运行时资源库` | 打包 UI 图标（含 `mark_*` / `special_*` / 触控图标） |

## 6. 关键文件

| 文件 | 职责 |
|---|---|
| `UI/Core/ComicStyle.cs` | 静态色板/度量/对比度/中文判定 |
| `UI/Core/UIThemeSO.cs` | 可调 token（旧的 Inkwave 字段保留兼容） |
| `UI/Core/UIInkShapeGraphic.cs` | 面板网格：硬阴影 + 网点 + 气泡尾 + 粗墨边 |
| `UI/Core/UIChunkyText.cs` | 双层硬阴影文字 + 中文自动换字体 |
| `UI/Core/UIJellyButton.cs` | 漫画按钮反馈（阴影/网点） |
| `UI/Core/UISpringMotion.cs` | 变换驱动（临界阻尼，无过冲） |
| `UI/Core/ComicHalftoneGraphic.cs` | 全屏网点 |
| `UI/Core/ComicActionLinesGraphic.cs` | 放射动作线 |
| `UI/Core/ComicBadge.cs` | 角标弹出 |
| `UI/Core/UIIconLibrary.cs` | 图标运行时查找（修白方块） |
| `Editor/ComicFontAssetBuilder.cs` | 中文字体资产生成 + 覆盖率自检 |
| `Editor/ComicStyleInstaller.cs` | 主题写入 |
| `Editor/ComicGalleryBuilder.cs` | 样式画廊场景 |

## 7. 主菜单构图（P11）

Ready 页（`MatchFlowUI.EnsureReadyVisuals`）是**不透明漫画封面**，点「开始比赛」才进对局：

```
┌────────────────────────────────────────┐
│              〔4v4 涂地对战〕← 墨底标签   │
│     ╔══════════════════════════╗╗      │ ← 纸面贴纸 -2° 倾斜
│     ║   墨水大作战（橙 chunky）  ║║      │    背后放射动作线
│     ╚══════════════════════════╝╝      │
│            INK TURF WAR                │
│        ┏━━━━━━━━━━━━━━━━┓              │
│        ┃   开始比赛（黄）  ┃ ← 560×140   │
│        ┗━━━━━━━━━━━━━━━━┛              │
│         也可以按 空格 / 回车 开始          │
│      [检查更新]  [自定义按键·移动端]       │
│ 〔武器整备 >〕← 左缘页签       版本号→右下 │
└────────────────────────────────────────┘
```

- 底：纸面 `#FFFEF0`（alpha=1，**不许透出 3D 场景**）+ 半调网点 + 右上橙/左下蓝队伍色墨渍。
- 战斗 HUD 画布只在 Countdown/Playing/Finished 启用（`HudController` 阶段门控）；
  武器抽屉默认收起，左缘黄色页签是唯一入口（键盘 1~7 自动展开）。
- `aspect ≥ 1.9` 或真机时字号整体放大一档；新增菜单元素必须同时给两档尺寸。

