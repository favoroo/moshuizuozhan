# P6-B 全量自动化测试与规则回归 — 质检报告

> Agent: QA-Automation　|　项目：P1 俯视角涂地对战　|　日期：2026-10-29
> 看板：`Docs/OptimizationPlan.md` → `[P6-B]` 已勾选 `[x]`

## 1. 验收结论

| 套件 | 结果 | 说明 |
|---|---|---|
| 编译 | ✅ `compilationFailed:false` | 0 error CS |
| EditMode | ✅ **165 / 165 Passed** | 含本任务新增 30 项 |
| PlayMode | ✅ **53 / 53 Passed** | 含本任务新增 5 项；任何运行时 log error 都会判失败 |
| 运行时 Log Error | ✅ **0** | 零 Error 守门员实跑完整比赛 / 7 武器 / 手雷 / 双大招 / 风暴云 / VFX 池 |

真实工程命令（GUI 编辑器）：

```bash
U="$HOME/.unity/bin/unity"
"$U" command script.recompile_status
"$U" command test run --mode EditMode
"$U" command test run --mode PlayMode --async_tests   # + test status 轮询
```

## 2. 新增用例（35 项）

### 2.1 EditMode — 武器库契约 `WeaponArsenalContractTests.cs`（14 项）

- 7 类主武器工厂分派与策略 `Kind` 一致性（含显式枚举重载）。
- 全部射程 / 伤害 / 耗墨档案：小枪 7.2m/36/0.95、滚筒 125→30/1.9m 墨带/1.1 墨每米、蓄力狙 11→27m/40→160/18 墨、双枪 11m/30/0.85+翻滚 7、泼桶 9.5m/70+34/7.5、爆破枪 10.5m/125+70/30/9、加特林 15m/28/0.6。
- 真实开火耗墨比例：小枪 10 发恰好 9.5 墨；蓄力狙满/半蓄 18/9；双枪单发 + 翻滚；泼桶单次 7.5；爆破枪 9；加特林满蓄扫射约 15 墨。
- 伤害衰减：滚筒 125→77.5（5m）→30（8.5m）；`WeaponShot.ComputeDamage` 无衰减恒伤；蓄力狙贯穿光束恒伤。
- 特殊状态：双枪翻滚 → 0.5s 架枪（移速 0 / 散布 0 / 射速 +20% / 每发 0.85 墨）；泼桶 Volley 同目标只判 1 次直击、第二桶击杀；加特林半蓄力扫射约 1.0s / 15 发。
- 空墨罐时 7 类武器全部拒绝开火、不产生幽灵弹丸或光束。

### 2.2 EditMode — 机动与生命週期规则 `InkLifecycleRulesTests.cs`（9 项）

- 敌墨 DOT 20/s：满血 2.0s 压到 60 HP 底线；65 → 60 不穿透；4s 连续灼烧仍存活。
- 无敌帧 / 阵亡状态下敌墨不结算；治疗封顶 100；友军伤害拒绝、中立来源生效。
- 阵亡进入 4.0s 复活倒计时，`ResetImmediately` 满血复位。
- `PlayerConfigSO` 契约：潜墨 11.8 / 敌墨 1.9 / 回墨 42/s / 回血 60/s / 底线 60 / 受击槽 2/s（0.5s 退出）/ 隐匿阈值 1.5 / 出水 0.07s / 开火缓冲 0.16s / 复活 4s。

### 2.3 EditMode — 双大招与副武器 `SpecialRuleRegressionTests.cs`（7 项）

- Ink Tempest：6.5s → 13 跳 × 17 = 221 总伤；5 跳后剩 15 HP，第 6 跳（3.0s）击杀满血目标；6.5s 生命周期漂移 7.15m。
- Tidal Slam：核心 3.2m 满伤 180；核心外台阶 92.5 → 4.2m 处 73.75 → 5.2m 外缘 55 → 圈外 0；外圈不秒杀、同队与圈外免伤。
- Splat Bomb：平方衰减，半程 71.25、四分之一程 44.0625、3.1m 外 0。
- 双大招配置契约与 5m 落地墨斑 / 3.4m 降雨 / 34 DPS / 1.1m/s 漂移。

### 2.4 PlayMode — 机动生命週期 `MotorLifecyclePlayTests.cs`（3 项）

- 己方墨水潜行 11.8 m/s；1s 回墨 ≈42；约 2.4s 回满 100。
- 静止潜墨进入隐匿；≤1.5 m/s 蠕动保持；11.8 m/s 立即解除；`OnStealthChanged` 退出/进入双向事件。
- 踩敌墨 1.9 m/s、DOT 20/s 停在 60 HP 且存活；离开 0.5s 受击槽清零并恢复 6.0 m/s。
- 阵亡 4s 后复活：满血满墨、退出潜行/隐匿、回到出生点。

### 2.5 PlayMode — 4v4 战况条 `RosterStatePlayTests.cs`（1 项）

- 场景 8 名战斗人员绑定到左右各 4 槽。
- 队员阵亡 → 死亡图标 / 复活进度环 / 秒数角标（1~4）同时出现；4s 后复活全部收起。
- 存活且大招就绪 → 金色旋转光环强度上升。

### 2.6 PlayMode — 零 Error 守门员 `ZeroLogErrorGatePlayTests.cs`（1 项）

- 完整比赛 Ready→Countdown→Playing；Bot 自由行动 2s。
- 玩家依次实战 7 类主武器（弹丸 / 光束 / 推地 / Volley / 空爆 / 蓄力扫射）+ 手雷 + 超级降落 + 风暴云。
- 断言行星云 6.5s 后回收、VFX 池环/墨珠/延迟环归零。
- `Application.logMessageReceived` 捕获全流程 Error / Exception / Assert，逐条汇报。

## 3. 质检中发现并修复的真实缺陷

| 缺陷 | 影响 | 修复 |
|---|---|---|
| 13 处自定义 `Graphic` 用 `new GameObject(...)` 构造时缺 `CanvasRenderer`（`UIInkCard` 之外的 UI） | `GraphicRaycaster` 首次射线检测抛 `MissingComponentException`，相关 UI 不渲染；PlayMode 随机红 | 13 处构造显式加 `typeof(CanvasRenderer)`；`MatchBannerView` 对旧场景已保存子物体在 Awake 统一补齐 |
| `GroundAimReticle` 字段初始化器 `new MaterialPropertyBlock()` | Unity 禁止在 MonoBehaviour 构造函数创建原生对象，每次实例化报 `UnityException` | 移除字段初始化器，保留 `EnsureInitialized()` 兜底（与并发 Agent 同步修复） |
| `NavGrid.TryGetNearestWalkable` 对越界坐标从 (-1,-1) 起搜 | 任何场外点被吸附到地图对角角落；Bot 残血撤退可能选到穿过威胁的目标，反而靠近敌人 | 越界坐标夹到最近边界格再搜 |
| Bot 撤退候选只按「离威胁距离」评分 | 可能选中需先穿过威胁的远点；无路点时沿用 `transform.forward` 直冲 | 撤退候选增加背离威胁方向过滤（点积 ≤0.35），吸附后二次校验；无路点时朝威胁反方向撤离 |
| 伤害衰减公式只存在于弹丸池私有代码 | 无法被 EditMode 直接断言，易与策略参数漂移 | 新增 `WeaponShot.ComputeDamage` 纯函数，`InkProjectilePool` 复用 |
| VFX 池无观测入口 | 无法自动断言粒子/墨珠泄露 | `VfxPool` 新增 `ActiveRingCount` / `ActiveDropletCount` / `PendingRingCount` / `IsEffectPoolSettled` |

### 测试稳健性修正（不改变产品行为）

- `BotAiPlayboxTests` 残血撤退：1.2s 内 Bot 可能已回血到 75 并退出 Retreat，改用「撤退窗口内最大拉开距离」断言。
- `ReticlePlayModeTests` 弹道预测：移至 50m 高空，摆脱新地图掩体对水平落点的干扰。
- `ArenaOcclusionPlayTests`（P5-B 在研）：固定 30 帧改为 2.5s 超时收敛轮询，匹配相机跟随与 7/s 淡出速度。

## 4. 兼容性与影响面

- 公共接口只增不改：`WeaponShot.ComputeDamage(static)`、`VfxPool.ActiveRingCount/ActiveDropletCount/PendingRingCount/IsEffectPoolSettled` 均为新增只读 API。
- `NavGrid.TryGetNearestWalkable` 仅修正越界输入行为，合法输入结果不变。
- UI 仅补 `CanvasRenderer`，不改变层级、布局、配色、交互。
- 未新增 Package；未手改 `.unity/.prefab/.asset/.meta`；未触碰 `Library/Temp/Logs` 以外的编辑器锁。

## 5. 备注与遗留

- 验收期间另一 Agent 触发的 165 项全量测试超时使 GUI 编辑器主线程暂停约 30 分钟；期间在 `/tmp` 的 APFS 写时复制克隆上用 batchmode 并行跑通同版本代码（EditMode 165/165、PlayMode 54/54），恢复后在真实工程重跑得到本报告结论（PlayMode 53/53，少一项 P6-A 的截图留底用例，属于其显式/留底测试）。
- 视觉类内容（打击标记、受击弧、遮挡淡出观感、角色/武器造型）不在自动化验收范围，最终仍需人眼复核。
- 若后续再调整武器数值/大招时长/复活时间，应同步更新对应契约断言；测试中的关键数值均带中文失败说明，便于定位。