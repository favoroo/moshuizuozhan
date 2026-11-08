# 音效系统排查与优化（2026-11-08）

> 范围：全部 SFX（武器 / 涂地 / 移动 / 战斗反馈 / UI / 比赛流程 / 循环音），
> 素材侧 `tools/bake_sfx.py` + 播放侧 `AudioManager` / `AudioMixConfigSO` + 触发点接线。
> 参照实现：仓库内 `inkwave-aah-research/`（INKWAVE，94 个音效全程序化合成）。

## 1. 结论摘要

| 指标 | 改前 | 改后 |
|---|---|---|
| 音效族 | 59 | **78**（+19 新事件音） |
| wav 文件 | 87 | **157** |
| 每只音效的合成层数 | 1~3 层 | **5~9 层** |
| 噪声源 | 只有白噪声 | 白 / **粉** / **棕**（+共振带通做湿共振峰） |
| 逐族 RMS（响度密度） | — | 平均 **+14%**，炸弹/爆炸类 +30% |
| 高频重复音的变体 | shot 3 / step 3 / UI 2 | shot **6** / step **5** / UI click **4** |
| 烘出但从未播放的音效 | 4 个 | **0** |

**最重要的发现（根因级）**：`_bq()` 是破坏性 in-place 滤波，而 `noise()/tone()/sat_tone()`
在写入后直接调用它——**后写的噪声层会把前面所有层在同一时间段内整段抹掉**。
例如 `shot` 的最后一层带通噪声覆盖前 55ms，把「低频体 + 音调 chirp + 颤音」三层全部擦掉，
实际只剩一声带通噪声 + 一小截尾巴。这是音效听感「简陋、发薄」的第一主因，
不是配方写得不够花。修复方式：三个层写入函数改为「渲染到临时段 → 滤波 → 相加」。
仅修这一处，旧配方在相同峰值下 RMS 就 +59%（shot）/ +77%（ink_hit_ground）。

## 2. 素材层改造（`tools/bake_sfx.py`）

### 2.1 新增构件（对齐 INKWAVE）

| 构件 | 作用 | INKWAVE 对应 |
|---|---|---|
| `noise(kind=white/pink/brown)` | 粉噪声给湿/厚的体，棕噪声给低频轰鸣；2s 无缝噪声床随机位置取样 | `noiseBuffer()` |
| `fm_tone()` | 2 算子 FM + **指数衰减调制指数** → 钟/玻璃/金属高光 | `bell()` / `keys()` |
| `partials()` | 非谐分音堆叠（1/2.76/5.4/8.93、1/2/3.01/4.2/5.43） | `halyard_clink` / `charger_full` |
| `bloops()` / `plips()` | 下坠 / 上滑墨粒散射，随机时序 + 递减幅度 | `bloops()` / `plips()` |
| `splat()` | 标准 5 层墨花（低频体 + 粉噪体 + 共振湿 + 墨粒 + 气声） | `bigSplat()` |
| `grain_bed()` | bubble/squelch/rain/sizzle 环形颗粒床，周期 = 循环长度 → 天然无缝 | `texture()` |
| `click()` / `air()` | 瞬态点击层 + 高频气声层（让音效在混音里切得出来） | 各音效的首尾层 |
| `room()` | 多抽头早期反射（爆炸/雷/号角） | 卷积混响的低成本替代 |
| `_am_sine()` / `_am_square()` | 循环音的缓速起伏 / 机械颗粒 | `lfo()` |
| `_soft_limit(k=1.3)` | 归一化前压瞬态尖峰：同一峰值下「体」更饱满 | 总线胶合压缩 |

另外把 `_bq()` 的带通改成 **RBJ「峰值增益 = Q」** 形式（与 Web Audio 的 `bandpass` 一致）。
0dB 峰值形式下 Q=6 的带通只剩 3% 振幅，层与层根本配不平——这是「加了很多层却听不出来」的第二个坑。

### 2.2 循环音

- 噪声床长度 = `LOOP_DUR`，因此循环里任何噪声层天然以 1.8s 为周期；
- `_make_seamless_loop()` 从等功率（sin/cos）换成**线性**淡化：对同相内容线性淡化是恒等变换，
  等功率反而会在接缝处把同相内容抬高到 √2，每 1.8s 出现一次音量鼓包；
- 7 条循环音全部通过客观接缝检查（接缝跳变 ≤ 正常相邻样点差的 p99.9）；
- `charger_charge` 加入循环族：旧版拿一次性 wav 当循环播，接缝会「咔」。

### 2.3 新增音效族（19）

| 分类 | 族 |
|---|---|
| 状态提示 | `spawn_protect` `low_health` `respawn_tick` |
| 战局提示 | `lead_gain` `lead_lose` `final_10s` `enemy_special_warn` |
| 操作反馈 | `weapon_switch` `charge_cancel` `bomb_bounce` `reticle_lock` `pause_open` `pause_close` |
| 涂地/武器补全 | `splat_small` `splat_big` `ink_hit_wall` `blaster_pump` `splatling_ready` `storm_wind` |

## 3. 播放层修复（`AudioManager` / `AudioMixConfigSO`）

### 3.1 烘了但从未播放的 4 个音效（现已接线）

| 音效 | 新触发点 |
|---|---|
| `blaster_boom` | 爆破枪空爆（`WeaponBlasterSystem`），叠在落墨音之上 |
| `slosh_land` | 大墨斑（`PlaySplat` 的 ≥1.8m 档） |
| `refill_full` | 墨罐「未满 → 满」边沿（`InkCharacterMotor`） |
| `charger_charge` | 蓄力循环改用真正的 `charger_charge_loop_1` |

### 3.2 远场衰减

新增 `TryEventGain(worldPos, maxDistance)`：超距不播，否则 `0.22 + 0.78·a²`。
旧版这些事件是**全局 2D 满音量**，敌人在地图对角和自己贴脸一样响。
已接入：炸弹投掷/引信/爆炸、大招施放、风暴雷声、跳跃、翻滚、墨花、打墙、敌方大招警告。
本地玩家自身的事件（击杀确认 / 阵亡 / 复活 / 空墨）保持满音量，不做衰减。

### 3.3 重复播放与误播

- `PlayKill(streak)` 在 `uiConfirmClip` 为空时回退成 `killClip`，同一帧把击杀音播两遍（相位叠成怪音）→ 已修；
- 炸弹投掷音在 `WeaponController` 和 `InkBombPool` 各播一次 → 删掉前者（后者拿得到投出位置）；
- 大招 `special_slam` 在释放与落地各播一遍完整 clip → 落地改播墨花分档；
- 武器卡片点击绕过节流直接 `PlayOne`（连点叠爆音）→ 统一走 `PlayWeaponCardSelect()`；
- `Team == TeamA` 判定（含 AI 队友）→ 改 `IsLocalPlayer`：大招就绪、复活、空墨、回墨 tick、跳跃、开火兜底。

### 3.4 对象池

12 → 16 条 voice：8 人混战时炸弹 + 大招 + 落墨 + 命中会同帧叠十几发，12 条会开始截断仍在发声的旧 clip。

### 3.5 变体数组

`RuntimeAssetLibrary.GetSfxVariants(key)` 按族自动取全变体；`EnsureEditorClips` 同步按
`<key>_1.._64` 探测。旧实现把变体名逐个写死（只填前 2~3 个），加变体后独立包里根本取不到。
`EnsureEditorClips` 另补齐了旧版漏掉的 11 个字段（`shotClips` / `inkHitGroundClips` /
`hitEnemyClips` / `inkEmptyClip` / `inkRefillClip` / `killClip` / `deathClip` / `respawnClip` /
`ultReadyClip` / `ultCastClip` / `uiClickClip`），场景没序列化时编辑器里也能出声。

## 4. 验收方式

- 客观体检：`python3 tools/bake_sfx.py --stats` → 157 文件 / 0 异常 / 0 削波。
- 试听：`python3 tools/make_sfx_audition.py --before <旧快照> --out <html>`，
  逐族新旧 A/B + 时长/峰值/RMS 与变化百分比。
- 单族回炉：`python3 tools/bake_sfx.py --only <族名>` 后重新生成试听页。

## 5. 已知取舍

- 所有音效仍是**单声道**：导入链路 `forceToMono = true`，立体声会被折叠，做 stereo 收益为零。
- 爆炸/大招的「房间感」用多抽头早期反射近似，不是真卷积混响（离线烘焙无 IR 资源）。
- 低血心跳的音量/阈值（`lowHealthVolume`、0.35 血线）与比分反超的滞回（0.4%）是首版估值，
  需要实机试听后调 `AudioMixConfig.asset`。
