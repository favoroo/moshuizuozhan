# P1 软化卡通画质精修（P31）

> 范围：Toon 光影软化 / 描边减淡 / 环境光与阴影 / SSAO / 反射 / 材质 albedo 细节层 / 后处理精调 / 受击镜头反馈。
> 不改玩法、碰撞、场地布局，不重建主场景，不烘焙 lightmap。
> 调参唯一入口：`ArenaStyle.asset`（经菜单 `P1/画风/5. 应用软化卡通参数` 幂等写入）。
> 基线留底：`Logs/visual_final_game.png`（P29 后）→ 本轮 `Logs/quality_p31_game.png` / `quality_p31_combat.png`。

## 1. 本轮解决的根因

用户反馈"优化了几版还是简陋"。对照 INKWAVE（`inkwave-aah-research/`）源码与截图诊断，根因是**风格执行档位偏硬**，不是缺某个特效：

| 问题 | 事实 | 处理 |
|---|---|---|
| 光影亮暗硬切换 | `shadeSoftness=0.03`，二值色阶 | 新增 `P1ToonTriBand` 三阶柔和过渡；`shadeSoftness 0.03→0.10`、高光/阴影同步软化 |
| 粗黑描边"剪纸感" | `outlineColor≈纯黑`、`outlineThickness=1.6` | 深炭紫 (0.14,0.12,0.18)、1.0px、远处 18m 起淡化；掩体边界线同色减淡 |
| 暗部死黑 | `shadowFloor=0.42`、环境光 0.75、地面反弹色近黑 | `shadowFloor=0.50`、环境光 0.88、`ambientGround` 暖褐（冷阴影+暖底） |
| 移动端没有描边 | `Mobile_Renderer` Feature 列表为空 | 新增 mini 版描边（0.7px、强度 65%、仅深度边缘省 4 次法线采样） |
| 阴影糊 | 2048 / 40m / 2 级联 | 4096 / 48m / 3 级联（仅 PC） |
| SSAO 几乎不可见 | NormalSamples=Medium(1)、Direct 0.35 | High(2)、Direct 0.30（BlurQuality 保持 0=Bilateral，枚举 0 即最高，勿改 1） |
| 反射粗糙 | 256² Cubemap 一次性 | 512²；结算阶段二次捕获（战斗中不重捕获，避免 6 面 spike） |
| 角色/道具纯色块 | CharacterToon 无贴图槽 | 新增可选 `_BaseMap`/`_DetailNormalMap`（默认 white/bump 零差异）+ 程序化织物 albedo（±3.5% 低对比）接到 22 个角色材质 + Mat_Prop_Cloth |
| 调色推爆队伍墨 | `saturation+18` 无差别提饱和 | 回落到 +10、对比 +12、暗角 0.18、bloom 阈值 0.98→0.90 / 强度 0.55（发光体真正起辉） |
| 无镜头受击反馈 | 只有 UI 层 KillFlash（击杀） | 新增 `P1/ScreenFx` FullScreenPass：本地玩家受击色差 kick（峰值 0.045uv 按伤害缩放、7.5/s 指数衰减），桌面端启用 |

## 2. 视觉链路

```
菜单 P1/画风/5. 应用软化卡通参数
  └─ WriteStylePresetSoftCartoon（继承 P18 质感档 + 软化覆盖）
       ├─ ToonLighting.hlsl: P1ToonTriBand 三阶（暗 0 / 中 0.5 / 亮 1.0 连续）
       │    ├─ CharacterToonCommon（角色 22 材质）
       │    ├─ PropLit / ObstacleDitherLit（P1ToonDiffuse 内部切换）
       │    └─ InkGround / InkWall / InkSurfaceOverlay（墨面同语言）
       ├─ Mat_ToonOutline(1.0px 深炭紫) + Mat_ToonOutlineMobile(0.7px 65%)
       ├─ 环境: ambientIntensity 0.88 / ambientGround 暖褐 / 4096·48m·3 级联阴影
       ├─ SSAO: High 采样 / Direct 0.30
       ├─ 反射: ArenaReflectionCapture 512² ×2（进场 + 结算 Result 阶段）
       ├─ 材质: Surfaces_Cloth_Albedo.png（bake_prop_surfaces 新增 bake_cloth_albedo）
       ├─ 后处理: ArenaPostProfile（sat 10 / con 12 / vig 0.18 / bloom 0.90·0.55）
       └─ ScreenFx: PC_Renderer 挂 "P1 ScreenFx" FullScreenPass（无深度输入）
            └─ ScreenFxController 订阅 CombatantHealth.DamageApplied（本地受击）
```

## 3. 主要文件

**Shader / HLSL**
- `Shaders/ToonLighting.hlsl` — `P1ToonTriBand` 新增；`P1ToonDiffuse` 内部切换（旧 `P1ToonBand` 签名保留）
- `Shaders/CharacterToon.shader` / `CharacterToonOutlined.shader` — 新增 `_BaseMap`/`_DetailNormalMap`/`_DetailNormalStrength` 属性
- `Shaders/CharacterToonCommon.hlsl` — CBUFFER + 纹理声明 + frag 采样/细节法线（强度 0 零扰动）
- `Shaders/PropLit.shader` / `ObstacleDitherLit.shader` — 新增 `_BaseMap`（三 Pass CBUFFER 同步，SRP Batcher 兼容）
- `Resources/ScreenFx.shader` — 受击色差 kick FullScreenPass

**运行时**
- `Scripts/UI/ScreenFxController.cs` — 本地受击 → 色差峰值 → 指数衰减；移动端跳过
- `Scripts/Core/ArenaReflectionCapture.cs` — 512² + 订阅 MatchManager.PhaseChanged 结算二次捕获

**编辑器**
- `Scripts/Editor/ToonStyleInstaller.cs` — 菜单 5 / `WriteStylePresetSoftCartoon` / `ApplyAlbedoDetailMaps` / `InstallMobileOutlineFeature` / `InstallScreenFxFeature`（`InstallFullScreenFeature` 泛化）
- `tools/bake_prop_surfaces.py` — 新增 `bake_cloth_albedo()`（产物 `Surfaces_Cloth_Albedo.png`）

**资产（全部经菜单/API 写入，未手改 YAML）**
- `ArenaStyle.asset` / `PC_RPAsset.asset` / `PC_Renderer.asset` / `Mobile_RPAsset.asset` / `Mobile_Renderer.asset` / `ArenaPostProfile.asset`
- `Materials/Mat_ToonOutline*.mat` / `Resources/Mat_ScreenFx.mat` / `Characters/*.mat`（22 个接 albedo）

## 4. 关键调参（P31 新增/变更，均为 ArenaStyleSO 字段）

| 字段 | 旧 | 新 | 作用 |
|---|---|---|---|
| `shadeSoftness` | 0.03 | 0.10 | 三阶过渡带宽度 |
| `specSoftness` | 0.02 | 0.04 | 高光边缘柔和 |
| `shadowTint` | (0.40,0.45,0.62) | (0.48,0.50,0.64) | 阴影染色提亮 |
| `shadowFloor` | 0.42 | 0.50 | 暗部直射保底 |
| `outlineColor` | (0.09,0.10,0.13) | (0.14,0.12,0.18) | 近黑 → 深炭紫 |
| `outlineThickness` | 1.6 | 1.0 | 粗线 → 细线 |
| `outlineFadeStart` | 25 | 18 | 远处更早变细 |
| `ambientIntensity` | 0.75 | 0.88 | 环境光增强 |
| `ambientGround` | (0.18,0.17,0.17) | (0.28,0.22,0.18) | 地面暖反弹 |
| `colorSaturation` | 18 | 10 | 保护队伍墨色 |
| `colorContrast` | 10 | 12 | 微增对比 |
| `vignetteIntensity` | 0.12 | 0.18 | 收拢视线 |
| `bloomThreshold` | 0.98 | 0.90 | 发光体起辉 |
| `bloomIntensity` | 0.45 | 0.55 | 泛光强度 |

## 5. 验证

- **编译**：`script.recompile_status` → `compilationFailed:false`（含 4 次 Domain Reload 时序验证）。
- **EditMode**：500 总 / 498 过。唯一失败 `WeaponFeelTests.SpringImpulse_...` 为 P28 工作区既有失败（P29 文档已记录，本轮未触碰武器域）；1 条 `ObstacleMaterial_KeepsEdgeLineVisible` 为"会话未加载掩体材质"环境跳过。无新增回归。
- **PlayMode UI 子集**：55/55 全过。
- **运行时零错误**：Play 模式开局 + 60s 留底期间 `console.get --level error` = 0（ScreenFxController / 反射二次捕获 / 移动描边材质均无异样）。
- **截图**：`Logs/quality_p31_game.png`（出生区俯瞰）——三阶光影过渡、描边减淡、背光暖底、角色织物细节、湿墨高光全部可见。
- **回退**：运行 `P1/画风/4. 应用画质增强` 回到 P18 档；或 `P1/画风/1` 回到 P10 旧档（描边/色阶全回旧值，ScreenFx Feature 保留但强度恒 0 直通）。

## 6. 已知限制与后续

- **ScreenFx 空闲常开直通**：强度 0 时仍有一次全屏 blit（桌面 <0.5ms）。禁用/启用 feature 需运行时 RendererData 引用，成本高于收益。
- **移动端 SSAO 未开**：mini 描边 + 软阴影已补，SSAO 留给移动端性能余量验证后再加（`Mobile_Renderer` 挂 ScreenSpaceAmbientOcclusion 即可）。
- **道具/墙面 albedo**：本轮只生成织物 albedo（角色 + Mat_Prop_Cloth）。混凝土/金属/木材的专属 albedo 可扩展 `bake_prop_surfaces.py` 后接到 `Mat_Prop_*`（shader 已支持，SetTexture 即可）。
- **发光点缀**：bloom 阈值已降到 0.90，既有 emissive（出生台/警示条）会起辉；新增灯带/霓虹属装饰域（`ArenaDecorationBuilder.cs`），本轮未越界。
- **烘焙 lightmap / GI**：未做（计划内留作可选扩展）；当前用增强 Gradient 环境光 + 高采样 SSAO + 512² Cubemap 近似。
- **天空盒方向**：`ApplySkybox` 的太阳方向与方向光本就同源于 `style.sunEuler`（rotation×back = 光来向），不存在脱钩，本轮未改。
