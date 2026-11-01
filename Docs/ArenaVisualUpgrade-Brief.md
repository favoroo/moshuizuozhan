# 场地视觉精修统一简报（P30 场地精修轮）

> **目标**：参考 INKWAVE 街头潮流风，逐件精修 40×56 集装箱码头竞技场的**程序化外观**，让场地更细节、更精致、更有美感。
> 本文件是本轮全部子 Agent 的**统一风格契约**，开工前必读；与 `AGENTS.md` 冲突处以更严格者为准。
> 基线留底：`Logs/p7b_overview.png` / `p7b_spawn_teamA.png` / `p7b_midfield.png` / `p7b_north_vista.png`（2026-10-31，精修前）。

---

## 1. 参考图（先看图再动手）

- `inkwave-aah-research/assets/stages/cargo-day.webp` —— 集装箱码头日景，**最直接参考**
- `inkwave-aah-research/assets/stages/tidewater-day.webp` —— 风格锚图（明亮海港）
- 已转 PNG 可直接 Read：`/tmp/p1_refs/cargo-day.png`、`/tmp/p1_refs/tidewater-day.png`

参考图要点（提炼，作为评审标准）：

1. **主地标** = 大型黄色龙门岸桥：门架 + 斜撑桁架 + 走台梁 + 大字样（"CARGO"）+ 支脚黄黑警示条；体量巨大、剪影一眼可读。
2. **集装箱** = 饱和彩色（青绿/铁锈红/钴蓝/芥末黄/米白）+ 竖向波纹 + 端门框 + 角柱 + 顶部边轨，成排与堆叠交错。
3. **地面** = 浅灰混凝土大板（分缝清晰）+ 黄色标线/斑马线/区域涂装 + 集装箱脚印污渍。
4. **机械** = 蓝色跨运车、黄色移动式吊机等，远景剪影即可。
5. **背景纵深三层**：近景彩箱 → 中景岸桥/仓库/货船 → 远景悬索桥/绿丘/白云/帆船。
6. **整体气质**：明亮、干净、高饱和点缀压在低饱和底上；**没有灰蒙蒙的雾**；物件剪影圆润厚实，禁细碎毛刺。

## 2. 统一色板（唯一来源 `ArenaStyleSO`，禁止引入新色相）

| 类别 | 字段 |
|---|---|
| 混凝土底 | `concreteLight / concreteMid / concreteDark`、`boundaryConcrete / boundaryTrim` |
| 钢构 | `steel / steelDark` |
| 箱色 | `containerTeal / containerRust / containerSlate / containerMustard` |
| 点缀 | `hazardYellow`（警示黄）、`hazardDark`；警报红仅限小型标识 |
| 队伍色 | `teamAPad`（橙）/ `teamBPad`（蓝）——**只允许出现在出生区专属物件** |
| 自然 | `wood / woodDark / foliage / lampGlass` |
| 天/水 | `skyTop / skyHorizon / sunDisc / cloudColor`、`waterShallow / waterDeep / waterHorizon` |

- **派生规则**：变体色一律 `Color.Lerp(基准色, 黑/白/steelDark, 0~0.4f)`，保持同色相，不得自造新色相。
- 需要新 SO 字段时**不要自己加**：写进最终报告，由协调者统一添加。

## 3. 文件所有权（本轮硬边界，越界即事故）

| 子 Agent | 独占文件（可改） |
|---|---|
| A 道具网格 | `Assets/_Project/Scripts/Environment/ArenaPropMeshFactory.cs` |
| B 装饰与远景 | `Assets/_Project/Scripts/Editor/ArenaDecorationBuilder.cs` |
| C 核心几何外观 | `Assets/_Project/Scripts/Environment/ArenaGeometryBuilder.cs` |
| D 天空/雾/水/光 | `Assets/_Project/Scripts/Editor/ArenaAtmosphereBuilder.cs`；`Assets/_Project/Shaders/{SkyGradient,Water,SpawnPad,PropLit}.shader` |
| E 地面贴图 | `Assets/_Project/Scripts/Editor/ArenaGroundTextureBaker.cs` |
| **协调者** | `ArenaStyleSO.cs`、`ArenaLayoutV2.cs`、`ProjectScaffolder.cs`、`Tests/`、`Ink*.shader`、`ObstacleDitherLit.shader`、场景重建与验收 |

- 上表之外的**一切文件只读**（包括 `ObstacleDitherLit.shader`、`InkGround/InkWall.shader`、`ToonLighting.hlsl`、`ArenaStyleSO.cs`、`ArenaLayoutV2.cs`、`ProjectScaffolder.cs`）。
- 原则上**不新建文件**；确需新建时在报告里说明理由。
- 写文件前必须重新 `Read` 最新内容（多 Agent 并行，禁止凭会话记忆盲写）。
- 建议每完成一个独立小节就保存，缩短别人的编译等待窗口。

## 4. 玩法不变量（违反 = 返工）

1. **不改任何碰撞体**尺寸/位置/数量；不改掩体（cover）坐标与占地；不改 `ArenaLayoutV2`。
2. 不改被 EditMode 测试断言的对象名与 Transform（中央高台/四坡道/侧翼台/侧翼集装箱/边界墙/出生台/出生楼梯/SpawnGate 等；测试见 `Tests/EditMode/ArenaAndOcclusionTests.cs`、`SpawnDeckLayoutTests.cs`）。
3. 不改 `InkWallSurface` 的面数、朝向与攀爬规则；**新增装饰子网格一律不带 Collider、不带 InkWallSurface**。
4. 装饰件不得形成"看起来能站上去"的凸台（高度 < 4cm 或贴附在既有面上），不得封堵既有通道与坡道口。
5. `ArenaPropMeshFactory`：`BuildProp` 签名不变；`ArenaPropSurface` 四分类不变；**Solid=true 部件不得超出该道具种类既有的占地包围盒**（碰撞代理与涂墨立面由 Solid 包围盒决定），伸出主体的装饰件标 `Solid=false`。
6. 道具变化优先用现有 `variant` int 参数，不改 `ArenaLayoutV2.Props` 数据结构。

## 5. 性能预算

- 场内道具必须继续走 **MeshCombiner 合并管线**（factory 产出 → surface bucket 合并 → 6 个合并网格），禁止为小装饰单独开 GameObject/Renderer。
- 新增几何预算：场内道具合计 ≤ 60k 三角形；远景（backdrop）合计 ≤ 40k；单个道具 ≤ 3k；单块装饰条带 ≤ 200。
- 场景 Renderer 总数不得翻倍（当前 496）；能合并就不开新 Renderer。
- `decorationDensity < 1`（移动端）时新增装饰也要能被现有密度过滤逻辑合理裁剪。

## 6. 编译与自检（每人交付前必做）

1. `"$HOME/.unity/bin/unity" command script.recompile_status` → 必须 `compilationFailed: false`。
2. `"$HOME/.unity/bin/unity" command console.get --tail 40 --level error` → 不得有指向**自己文件**的 `error CS`。
3. 并行开发中可能看到别人文件的编译错误：**只修自己的**，别人的报给协调者，不要回滚他人代码。
4. **禁止运行任何重建场景的菜单**（`P1/关卡与地图/场景视觉重构…`、`P1/构建/2`、烘焙贴图菜单等）——协调者统一重建验收。
5. 禁止手改 `.unity / .prefab / .asset / .meta` YAML。
6. 禁止 `git commit` / `git push`。

## 7. 视觉一致性细则（全部子 Agent 共同遵守）

- **剪影优先**：先大块后细节；俯视缩到 1/4 屏仍能一眼认出每个物件是什么。
- **圆润厚实**：多倒角/圆角/加粗；杆件直径 ≥ 6cm；禁止细线、碎线、毛刺。
- **描边外包**：2-3px 深炭描边由全屏 `ToonOutline` 后处理负责，几何不要自己画黑边。
- **明亮基调**：P18 已定明亮浅底（非深炭紫），物件亮面保持明亮；阴影靠冷紫 tint，不要把物件整体做暗。
- **少即是多**：每类物件 2~3 个记忆点即可（如集装箱 = 波纹 + 端门框 + 角柱），不要堆满细节。
- **大字样**：几何字母只允许大写、无衬线、笔画简单的词（P1 / CARGO / TURF / WAVE），顶点色即可，禁止贴图字。
- **代码风格**：中文注释，与文件内现有注释密度/命名一致；不加 Package；遵守各程序集边界。

## 8. 交付格式（最终报告 ≤ 40 行）

1. 改动文件清单（仅自己的）。
2. 每个改进点一句话说明（对应参考图第几条）。
3. 不变量自查结果（§4 逐条确认）。
4. 编译验证结果（compilationFailed / error CS 指向）。
5. 需要协调者代办的事项（新 SO 字段 / ArenaLayoutV2 点位 / 跨文件契约）。
