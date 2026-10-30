# 漫画风 UI 规范（已废弃）

> **本规范已于 2026-10-30 被 `Docs/UI-GeometricBold.md`（几何大胆风 Geometric Bold）取代。**
> 改任何 HUD / 菜单 / 触控界面前请读新规范；本文只保留作历史对照。

## 为什么废弃

漫画风的网点和"硬边偏移阴影"属于印刷语言，与本次改版目标（纯色大色块、扁平、无阴影、直角/全圆、
超大粗体字、≤100ms 线性硬切）冲突。代码层已整体切换：

| 旧 token / 实现 | 新状态 |
|---|---|
| `Ink #1A1A1A` / `Paper #FFFEF0` | `#141414` / `#FFFFFF`（见 `ComicStyle.cs`） |
| 硬阴影 `ShadowOffset=6` | **0（全局禁用阴影）** |
| 半调网点 `HalftoneAlpha=0.14` | **0（全局禁用网点）** |
| 圆角 `CornerSharp=5` / 气泡 32 | **直角 0 / 全圆胶囊（`CornerBubble=999`）** |
| 弹簧回弹（`UISpringMotion` 物理阻尼） | **timed lerp ≤100ms linear，无过冲** |
| `PlayWobble` 有机边缘 | **no-op** |
| 对话气泡尾巴 / 放射动作线 | 不再用于新界面（字段保留兼容） |
| 移动端字号"放大一档"靠各 View 手写 | **统一 `UITypography.Font()`（移动端 ×1.28，正文下限 18/20）** |
| `UIInkShape.RoundedRect` | 新增 `SharpRect`（直角）与 `Pill`（胶囊），新界面用新形状 |

旧 token 名称全部保留（26 个调用方文件零改动），只是值换成了几何风。
旧菜单 `P1/画风/漫画风/1|1b|2` 仍可用，等价于 `P1/画风/几何风/1|1b|2`。

## 历史内容

完整的漫画风 token、组件 API、禁止项与主菜单构图保留在上一条 git 提交的
`Docs/UI-ComicStyle.md` 中（`git show HEAD:Docs/UI-ComicStyle.md`）。当前实现的唯一事实来源是：

- 规范：`Docs/UI-GeometricBold.md`
- Token：`Assets/_Project/Scripts/UI/Core/ComicStyle.cs`
- 主题资产：`Assets/_Project/ScriptableObjects/UITheme.asset` / `HudGaugeStyle.asset`
- 画廊：`Assets/_Project/Scenes/ComicGallery.unity`（菜单 `P1/画风/几何风/2`）