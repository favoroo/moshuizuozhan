# 规格：首页地图选择卡片 + 真实切图

> 状态：待用户确认（Spec Mode）。确认前不落地任何代码/场景改动。
> 项目：`/Users/a1/Documents/01Code/Unity/P1`（Unity 6000.6.7f1 / URP / 新版 Input System）
> 日期：2026-11-04

## 1. 目标

把首页 2×2 卡阵里原「操作指南 / 键位说明」卡（Index 2）替换为**地图选择卡**：

1. 卡片显示**当前所在竞技场**（名称 + 程序化小预览 + 当前态），并直接写出还能选哪张。
2. 点击卡片打开**地图选择浮层**，可看两张图的预览/差异说明并切换。
3. 切换**真实生效**：保存选择 → 按 `EditorBuildSettings` 的 Build Index 加载对应场景 → 新场景回到 Ready 首页，再开赛即在该图对局。
4. 选择写入 `PlayerPrefs`，下次启动自动加载；无存档时仍是 PhaseA（老玩家首进不变）。
5. 操作指南不丢：保留暂停菜单「操作说明」卡，并在**系统设置 → 画面与操作**新增「操作说明」行，打开同一份 `HomeInfoContent` 指南。

## 2. 已确认的现状（决策依据）

- 启动链路**当前没有动态选场景**：全项目无 `SceneManager.LoadScene`；构建包固定从 Build Index 0 = `Assets/_Project/Scenes/PhaseA_Playbox.unity` 启动。
- `EditorBuildSettings.asset` 已是：0 `PhaseA_Playbox`、1 `GullHarbor`（潮汐货站 KelpTerminal 已被海鸥港湾替换并删除）。
- 两张图都是完整可玩场景（`ProjectScaffolder.BuildArenaScene` 生成 `MatchManager` / 玩家 / Bot / HUD / 触控 / 墨面），玩法常量统一来自 `ArenaLayoutV2`（40×56），场景内各自持有出生点、掩体、NavGrid 构建源与 `InkGridConfigSO`。
- `GullHarbor` 场景内有 `ArenaStyleSceneAnchor`，运行时 `RenderEnvGlobals.SetStyle` 推新图雾/天空；PhaseA 无锚点，切回时必须清掉上一张图的静态样式（否则会沿用港湾天空）。
- 首页牌面与浮层已有成熟范式：`HomeInfoOverlay` / `HomeInfoLayoutMath` / `ComicStyle` / `UITypography` / `MenuItem` 版面表 / `MenuInputRouter`。

## 3. 架构决策

### 3.1 地图目录与持久化（新增 `Scripts/UI/MapCatalogService.cs`）

```csharp
namespace P1.UI
{
    public enum MapSchematicRole { Terrain, Cover, Spawn, Landmark }

    public readonly struct MapSchematicBlock   // 世界坐标 x/z + 宽/深，仅用于小预览
    public sealed class MapDefinition          // Id / ScenePath / 显示名 / 英文名 / 一句话差异 / Accent / 图元表
    public static class MapCatalogService
    {
        public const string PrefsKey = "P1.Map.SelectedId";
        public const string PhaseAScenePath = "Assets/_Project/Scenes/PhaseA_Playbox.unity";
        public static IReadOnlyList<MapDefinition> Maps;       // [0]=PhaseA 默认，[1]=GullHarbor
        public static MapDefinition DefaultMap;
        public static string SelectedId;                        // 读 Prefs；非法/缺失 → PhaseA（不写盘）
        public static MapDefinition Selected;
        public static MapDefinition ActiveOrSelected;           // 当前场景对应图；非地图场景回落 Selected
        public static MapDefinition MapForScene(string scenePath);
        public static bool IsMapScene(string scenePath);
        public static bool TrySelect(string id);                // 只写 Prefs + Save
        public static bool LoadMap(MapDefinition map);          // BuildIndex 校验 → LoadScene(Single)
        public static void ResetForTests();                     // 删 PrefsKey + Save
    }
}
```

地图数据（全部走注册表，不硬编码 UI 颜色）：

| Id | ScenePath | 显示名 | 英文 | 一句话差异 | Accent |
|---|---|---|---|---|---|
| `phase_a` | `PhaseA_Playbox.unity` | 货柜码头 | CONTAINER DOCKS | 中央高台 · 四向坡道 · 集装箱掩体 | `ComicStyle.Red` |
| `gull_harbor` | `GullHarbor.unity` | 海鸥港湾 | GULL HARBOR | 中央渡轮 · 浅湾水线 · 木栈道绕后 | `ComicStyle.Blue` |

- `SelectedId` 只在 Prefs 有合法值时返回它，否则返回默认 id，**不自动写盘**（保证老玩家首进仍是 PhaseA）。
- `LoadMap`：`SceneUtility.GetBuildIndexByScenePath(map.ScenePath)`，`< 0` 直接 `Debug.LogError` 返回 false；否则 `SceneManager.LoadScene(index, LoadSceneMode.Single)`。静态 `_loading` 防连点，`sceneLoaded` 时清零。
- 只改 UI 状态不算切换：`TrySelect` 与 `LoadMap` 必须由浮层在同一次点击里连续调用。

### 3.2 启动恢复与画风复位（新增 `Scripts/UI/MapStartupRouter.cs`）

- `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 重置静态标记（Domain Reload 关闭时仍能跨 Play 会话复位）。
- `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`：
  - 若当前场景是地图场景且与 `Selected` 不同 → `MapCatalogService.LoadMap(Selected)`。首次帧内完成，不产生可见闪屏；`SampleScene` / 空场景 / 测试 `InitTestScene` 一律跳过。
  - 挂钩 `SceneManager.sceneLoaded`：每次载入地图场景时 `MapCatalogService.ResetRuntimeState()`；若载入的是 `PhaseAScenePath`，调用 `RenderEnvGlobals.ResetStyleToDefault()`，避免港湾图的天空/雾/湿润参数残留到主图。
- `RenderEnvGlobals` 只新增 `ResetStyleToDefault()`：`_style = null; _styleLookupDone = true; Pump();` 走既有回退默认（注释已声明与 `ArenaStyleSO.asset` 一致），不修改既有 `SetStyle` / `Pump` 行为。

### 3.3 场景切换的连带修复：武器选择订阅

`WeaponSelectBootstrap.SubscribeMatch` 现有守卫 `if (match == null || _subscribedMatch) return;` 在切场景后永远不会重新订阅新 `MatchManager`（视图是 DDOL，旧 MatchManager 已销毁）。改为：

```csharp
if (match == null) return;
if (_subscribedMatch && _currentMatch == match) return;
if (_subscribedMatch) UnsubscribeMatch();   // 允许旧引用为已销毁对象
_currentMatch = match;
_currentMatch.PhaseChanged += HandlePhaseChanged;
_subscribedMatch = true;
```

`UnsubscribeMatch` 无论旧引用是否已销毁，都把 `_currentMatch = null`。这是切图后战前选枪仍能跟随 Ready/Playing 的必要条件。

## 4. UI 规格

### 4.1 首页地图卡（`MatchFlowUI.BuildHomeTiles` Index 2）

- 牌面：`Tag="MAP"`、`Title=当前场景地图显示名`（4 字，移动端不顶箭头走廊）、`Info="当前 · 还可选「<另一张>」"`、`Accent=当前地图 Accent`。
- 预览：在标题与信息行之间的空白区右侧放 `MapPreviewBuilder` 小程序化俯视图（40×56 等比、右对齐、不压信息行）；高度自适应 `infoTop - titleBottom`，桌面约 190px、移动端约 129px。
- 交互：整卡可点（沿用 `AttachTileInteraction` / `UIJellyButton`），打开 `MapSelectOverlay`。
- 文案保护：沿用 `WarnIfTextOverflows` + `FitTileInfoText`（下限 18/20px，禁止截断）。
- 删除死代码：`BuildKeysInfo()`、`_homeKeysInfo` 字段与 `RefreshHomeInfo` 中对应分支；`ShowHomeInfo(Controls)` 仍保留给设置页指南按钮使用。

### 4.2 地图选择浮层（新增 `Scripts/UI/MapSelectOverlay.cs` + `MapSelectLayoutMath.cs`）

- 独立 Overlay Canvas，`sortingOrder = 60`；`CanvasScaler` 与 `HomeInfoOverlay` 同参考分辨率 / match 0.5；全屏墨黑遮罩（`ComicStyle.Ink` α0.74）+ 居中纸白面板（4px 墨框 + Dialog 硬影 + 顶部 Accent 色带）。
- 头部：`Tag="MAP"`、标题「选择地图」、副标题 `SELECT MAP`、关闭键；Esc / 手柄 Start / 点空白关闭。
- 内容：两张大选项卡并排。
  - 卡面：`PaperAlt` 底、选中态 4px Accent 边框 + 右上「当前」角签；未选中 2px Ink 边框。
  - 内容自上而下：地图小预览（`MapPreviewBuilder`，等比居中）→ 显示名（36/46px 粗体）→ 英文名（18/24px）→ 一句话差异（20/26px）→ 底部大按钮（桌面 64px / 移动 84px）。
  - 选中卡按钮文案「当前地图」；另一张「切换到此图」。整卡与按钮都可点。
- 点当前卡 = 关闭；点另一张 = `TrySelect` → `LoadMap`；`LoadMap` 失败则 `Debug.LogError` 并关闭（UI 不假装切换成功）。
- 页脚：左「选择即保存 · 切换会重新载入场地」，右「ESC / 点击空白处关闭」。
- 版面表 `MapSelectLayoutMath.Metrics(bool mobile)` + `ResolvePanelSize(...)`，字段覆盖面板/头部/卡片/预览/字号/按钮/页脚，EditMode 可直接断言安全区与字号下限。
- 安全区：面板中心跟随 `Screen.safeArea` 中心，尺寸按安全区比例钳制（复用 HomeInfoOverlay 的算法）。
- 文案不截断：`TextWrappingModes.NoWrap` + `Overflow`，开发期对标题/差异行做 `WarnIfOverflows`；字号与盒宽由版面表兜住。

### 4.3 小程序化地图预览（新增 `Scripts/UI/MapPreviewBuilder.cs`）

- 输入 `RectTransform parent`、`MapDefinition`、`Vector2 size`、`UIThemeSO`；返回预览根。
- 底：`PaperAlt` 填充 + `Ink` 细框；按 40×56 世界范围等比映射（留 6px 内边距）。
- 图元颜色全部来自注册表：地形 α0.10 Ink、掩体 `InkSoft`、出生盘 `theme.teamAColor` / `theme.teamBColor`（南北对半）、地标 = 地图 Accent。
- 绘制顺序 Terrain → Cover → Landmark → Spawn；所有子节点 `raycastTarget=false`、无硬影。
- 两张图的图元表在 `MapCatalogService` 中手写（PhaseA：中心 10×10 台 + 四向坡道 + 两侧掩体/侧翼；GullHarbor：中央渡轮 + 南北浅湾水线 + 东西木栈道 + 船坞拖船），只服务辨识，不参与玩法判定。

### 4.4 操作指南的收纳

- 暂停菜单主视图已有「操作说明」卡（键位表 + 规则速记，数据来自 `HomeInfoContent`），保留不动。
- `PauseMenuOverlay.BuildDisplaySettingsPanel` 末尾新增第 5 行：
  - 左标签「操作说明」，灰注解「移动 / 战斗 / 规则速记」；右侧按钮「查看指南」。
  - 点击 → `HomeInfoOverlay.Show(host, theme, HomeInfoTab.Controls, sortingOrder: 110)`。
  - 首页移动端没有常驻暂停键，因此这是首页 → 系统设置 → 操作说明的可用路径；桌面端 Esc 暂停同样可达。
- `HomeInfoOverlay.Show` 增加可选末参 `int sortingOrder = 50`（默认值保持既有调用行为），设置页传 110（高于暂停 Canvas 的 100）。

## 5. 输入仲裁

- `MenuEscLayer` 追加 `MapSelect = 5`；`MenuEscAction` 追加 `CloseMapSelect`。
- `MenuInputRouter.ResolveEscOwner` / `ResolveEscOwnerThisFrame` 末尾追加可选参数 `bool mapSelectOpen = false`：
  `mapSelectOpen` 最高优先 → HomeInfo → LayoutEditor → PauseMenu → Drawer → 默认 PauseMenu。
- `CanLayerHandle` 把 `MapSelect` 与 HomeInfo / LayoutEditor 同等视为「任何消费者可代劳关闭」。
- `ActionForOwner` 映射到 `CloseMapSelect`。
- `PauseMenuOverlay.HandleInput`、`WeaponSelectView.HandleMenuEsc` 传入 `MapSelectOverlay.IsOpen` 并新增 `CloseMapSelect` 分支；`WeaponSelectView` 在浮层打开时数字键/方向键/Enter 全部让路。
- `MatchFlowUI.CanStartMatch` 增加 `MapSelectOverlay.IsOpen` 守卫；`TryStartMatch` 若当前地图场景与 `Selected` 不一致，先 `LoadMap(Selected)` 再返回（防陈旧存档直接开错图）。

## 6. 改动文件清单

新增：
- `Assets/_Project/Scripts/UI/MapCatalogService.cs`
- `Assets/_Project/Scripts/UI/MapSelectLayoutMath.cs`
- `Assets/_Project/Scripts/UI/MapSelectOverlay.cs`
- `Assets/_Project/Scripts/UI/MapPreviewBuilder.cs`
- `Assets/_Project/Scripts/UI/MapStartupRouter.cs`
- `Assets/_Project/Tests/EditMode/MapCatalogTests.cs`
- `Docs/Specs/HomeMapSelect/spec.md`、`tasks.md`

修改：
- `Assets/_Project/Scripts/UI/MatchFlowUI.cs`：Index 2 卡换成地图卡；删 `BuildKeysInfo` / `_homeKeysInfo`；`RefreshHomeInfo`、`CanStartMatch`、`TryStartMatch`。
- `Assets/_Project/Scripts/UI/Menus/PauseMenuOverlay.cs`：设置页第 5 行「操作说明」；Esc 仲裁接入 MapSelect。
- `Assets/_Project/Scripts/UI/WeaponSelect/WeaponSelectView.cs`：浮层输入让路 + Esc 分支。
- `Assets/_Project/Scripts/UI/WeaponSelect/WeaponSelectBootstrap.cs`：跨场景重新订阅 MatchManager。
- `Assets/_Project/Scripts/UI/HomeInfoOverlay.cs`：`Show` 可选 `sortingOrder`。
- `Assets/_Project/Scripts/UI/Core/MenuInputRouter.cs`：MapSelect 层/动作/可选参数。
- `Assets/_Project/Scripts/Core/RenderEnvGlobals.cs`：新增 `ResetStyleToDefault()`（只增不改）。
- `CHANGELOG.md`：追加 `## 2026-11-04 #36`。
- `tools/font_charset.txt`：`python3 tools/font_charset.py` 自动重算后提交。

不动：
- 任何 `.unity` / `.prefab` / `.asset` / `.meta` YAML；`ProjectSettings/EditorBuildSettings.asset`（已含两张图）；`ArenaLayoutV2` / `ArenaLayoutHarbor` / 玩法判定；他人未提交的战斗、角色、音频、材质文件。

## 7. 验证方案

1. **改前截图**（动手前）：Play Mode 截当前首页 → `Logs/verify-map-select/home_before.png`；记录 `git status` 快照，确认不覆盖他人改动。
2. 编译：`"$U" command script.recompile_status` → `compilationFailed: false`，`console.get --level error` 无本任务报错。
3. **改后首页**：
   - 默认（PhaseA）：`Logs/verify-map-select/home_after_phase_a.png`
   - 选中海鸥港湾后：`Logs/verify-map-select/home_after_gull.png`
   - 浮层：`Logs/verify-map-select/map_select_overlay.png`
4. **真实切图对局**：两张图各进一局（倒计时结束进入 Playing）截图：
   - `Logs/verify-map-select/match_phase_a.png`
   - `Logs/verify-map-select/match_gull.png`
   - 画面必须能看出港湾图的中央渡轮/水线与 PhaseA 的中央方形高台不同。
5. **持久化**：`PlayerPrefs.GetString("P1.Map.SelectedId")` 打印为 `gull_harbor`；退出 Play 后重新进入（不改选择）首页直接是海鸥港湾（截图同上）；再切回 PhaseA 复验。`MapCatalogTests` 用 `PlayerPrefs` 断言默认值、非法值回退、写盘与读取。
6. 测试：`./tools/run_tests.sh fast` 全绿（EditMode + PlayMode UI 子集）；新增 EditMode：
   - 默认选择 PhaseA、非法 id 回退、`TrySelect` 持久化；
   - 两张图路径均在 `EditorBuildSettings.scenes` 且 enabled；
   - `MapSelectLayoutMath` 移动端面板不出安全区、字号 ≥18；
   - `MapCatalogService.Maps` 数量/唯一 id/路径与场景名一致。
7. 字体：`python3 tools/font_charset.py` 重算账本 → 菜单 `P1/画风/漫画风/0` 预烘 → `FontCoverageTests` 转绿。
8. 报告：中文 Markdown（结论、改动清单、UI/交互、验证结果与截图路径、残留风险）。

## 8. 风险与取舍

- **切场景会触发一次完整重建**（DDOL 的选枪画布/启动页/更新管理器保留）；已识别并修复 `WeaponSelectBootstrap` 订阅 bug，其余管理器按场景重建属预期。若实测发现其他悬挂订阅，追加最小修复并在报告列出。
- **启动双加载**：玩家记住的是海鸥港湾时，构建包仍先载入 Build Index 0 再在首帧内切到港湾图；`AfterSceneLoad` 在首帧渲染前完成，预期无可见闪屏，但会多一次场景初始化开销（两图规模相近，可接受）。不做 Boot 场景，避免改构建顺序与其他人冲突。
- **PhaseA 切回后的环境样式**：走 `RenderEnvGlobals` 回退默认（代码注释声明与 `ArenaStyleSO.asset` 一致）；若截图发现天空/雾与基线有肉眼差异，再评估给 PhaseA 场景补 `ArenaStyleSceneAnchor`（需改主场景，另行确认）。
- **`Logs/` 与他人共享**：截图只写 `Logs/verify-map-select/`，不改他人的日志/截图。
- **字体门禁**：新增中文字面量必然触发，已纳入验证；不改字体资产本身，只用既有烘焙菜单。
- **不纳入本次**：地图缩略图 PNG 资产化、选图联网同步、更多地图、对局中热切换。

## 9. 需要用户确认

- 地图卡采用「程序化俯视小预览 + 浮层选择」而非引入截图 PNG 资产。
- 操作指南收纳到「系统设置 → 画面与操作 → 操作说明」，同时保留暂停菜单「操作说明」卡。
- 默认值保持 PhaseA；启动恢复采用首帧内场景切换，不新增 Boot 场景。
