# P1 画质与油漆质感升级（P29）

> 范围：材质 / 光照 / 后处理 / 墨水 / 水面 / 天空 / VFX。不改玩法、碰撞、场地布局，不重建主场景。
> 调参唯一入口：`Assets/_Project/ScriptableObjects/ArenaStyle.asset`（`ArenaStyleSO`）。

## 1. 本轮解决的根因

| 问题 | 事实 | 处理 |
|---|---|---|
| 后处理全是死配置 | 主相机没有 `UniversalAdditionalCameraData`，`renderPostProcessing=false`，`ArenaPostProfile` 的 Bloom/Tonemap/调色/暗角从未生效 | `TopDownCameraFollow` 与 `ArenaAtmosphereBuilder` 幂等补相机数据并开启后处理 |
| 零抗锯齿 | MSAA 关、无 FXAA/SMAA | 桌面 SMAA High，移动 FXAA Low |
| SSAO 几乎不可见 | `Intensity 0.28`、`Radius 0.30` | PC 调到 `0.9 / 0.55 / Direct 0.35` |
| 没有真实环境反射 | 无 ReflectionProbe/cubemap/SSR，只有解析三色天空 | 新增一次性 256² HDR 环境 Cubemap（`ArenaReflectionCapture`），墨面/金属/水面共享 |
| 墨面只有单层高光 | 反射在典型俯视角下被 F0=0.04 压得很弱 | 双层清漆高光 + 湿墨反射增强 + 薄缘高光 |
| 墙面/顶面像贴纸 | 墙面只有整面湿润 float + 静态假垂滴；顶面只有单次图集采样 | 墙面动态垂滴/法线/太阳高光；顶面 lip 法线 + 接触阴影 + 清漆 |
| 溅射粒子哑光 | `Universal Render Pipeline/Unlit` 色球 | 新增 `P1/InkVfx`，与飞行墨滴同一套湿润语言 |

## 2. 视觉链路

```
ArenaReflectionCapture (桌面, 进场 0.35s 一次)
  └─ 256² RGBAHalf Cubemap (6 面, mips) → RenderEnvGlobals
       ├─ _P1EnvCube / _P1EnvCubeReady / _P1EnvCubeStrength
       └─ _P1InkCoatStrength / _P1InkCoatRoughness / _P1InkEdgeRimStrength
          _P1InkEdgeSmoothStrength / _P1InkTopDetailStrength
          _P1WallDripSpeed / _P1WaterReflectionStrength / _P1EmissiveStrength
                │
                ├─ InkWetShading.hlsl: P1InkEnvSpecularFull / P1InkCoatSpec / P1InkWetRim
                ├─ InkGround: 梯度修正边缘 + 双层清漆 + 湿墨反射增强
                ├─ InkSurfaceOverlay: 顶面 lip 法线 + 接触阴影
                ├─ InkWall / InkBody / InkVfx: 墙面与 VFX 统一湿润语言
                ├─ Water / SkyGradient: 水面反射 + HDR 太阳
                └─ PropLit / ObstacleDitherLit / SpawnPad: 金属/道具环境反射 + 发光泛光
```

## 3. 主要文件

**运行时**
- `Scripts/Core/ArenaReflectionCapture.cs`（新增）
- `Scripts/Core/RenderEnvGlobals.cs`
- `Scripts/Player/TopDownCameraFollow.cs`
- `Scripts/Environment/ArenaStyleSO.cs`
- `Scripts/Core/VfxPool.cs`

**Shader**
- `Shaders/InkWetShading.hlsl`（共享 API）
- `Shaders/InkGround.shader`
- `Shaders/InkSurfaceOverlay.hlsl`
- `Shaders/InkWall.shader`
- `Resources/InkBody.shader`
- `Resources/InkVfx.shader`（新增）
- `Shaders/Water.shader` / `SkyGradient.shader`
- `Shaders/PropLit.shader` / `ObstacleDitherLit.shader` / `SpawnPad.shader`

**编辑器 / 素材**
- `Scripts/Editor/ArenaAtmosphereBuilder.cs`
- `Scripts/Editor/ToonStyleInstaller.cs`
- `Scripts/Editor/ArenaSurfaceDetailInstaller.cs`（新增，菜单 `P1/画质/1. 应用道具材质细节`）
- `tools/bake_prop_surfaces.py`（新增）
- `Art/Textures/Surfaces/Surfaces_Wood_N/RMO.png`、`Surfaces_Cloth_N/RMO.png`（新增 512²）

**场景 / 资产（由 Editor 菜单写入，非手改 YAML）**
- `Scenes/PhaseA_Playbox.unity`：相机 URP 数据、Volume
- `Settings/PC_RPAsset.asset`：HDR 调色（`m_ColorGradingMode=1`）
- `Settings/PC_Renderer.asset`：SSAO 强度/半径/直接光
- `Settings/ArenaPostProfile.asset` / `Materials/Mat_Skybox.mat` / `ArenaStyle.asset`：菜单写入

## 4. 关键调参（ArenaStyleSO）

| 字段 | 默认 | 作用 / 建议 |
|---|---|---|
| `postProcessingEnabled` | true | 后处理总开关；false 回到旧扁平观感 |
| `envSpecStrength` | 1.2 | 环境镜面总强度；0 关闭，1~1.5 建议 |
| `inkProbeStrength` | 1.0 | Cubemap 反射强度；0~2 |
| `inkCoatStrength` | 1.2 | 清漆高光强度；0~3（>2 容易过亮） |
| `inkCoatRoughness` | 0.06 | 清漆粗糙度；0.03 更锐、0.12 更柔 |
| `inkEdgeRimStrength` | 0.55 | 墨缘薄缘高光；0 关闭 |
| `inkEdgeSmoothStrength` | 0.7 | 图集边缘梯度修正；0~1 |
| `inkTopDetailStrength` | 1.0 | 顶面 lip 法线/接触阴影；0 关闭 |
| `wallDripSpeed` | 1.3 | 墙面垂滴动画速度；0 冻结 |
| `waterReflectionStrength` | 0.8 | 水面环境反射；0~2 |
| `emissiveBloomStrength` | 2.0 | 发光材质泛光；0 关闭 |
| `skySunIntensity` | 2.2 | 天空太阳 HDR 强度（写入 Mat_Skybox `_SunIntensity`） |
| `ssaoIntensity` | 0.9 | PC SSAO 强度（菜单 4 写入 PC_Renderer） |
| `bloomIntensity/threshold` | 0.45 / 0.98 | 后处理 Bloom |

移动端：`P1_INK_HIGH` 关闭时，墨面走单层高光（1 次 pow）、无 Cubemap 采样、顶面/墙面无额外邻域采样；桌面独享全量。

## 5. 验证

- **编译**：`script.recompile_status` → `compilationFailed:false`；9 个改动 Shader `ShaderUtil.GetShaderMessages` 全 OK。
- **EditMode 全量**：491 条，489 通过；唯一失败为工作区既有 `WeaponFeelTests.SpringImpulse_PeakMatchesRequestedOffset_AndSettles`（P28 弹簧改动，本轮未触碰）。
- **PlayMode 定向**：
  - `InkAtlasRenderPlayTests` 3/3
  - `WallInkPlayboxTests` 7/7
  - `ThirdPersonCameraPlayTests` 1/1
  - `ArenaOcclusionPlayTests` 2/2
- **真实 Profiler（Editor PlayMode）**：
  - 30s 纯战斗段：平均 **60.8 FPS**、**1% Low 41.0**、上传 P99 **0.26 ms**、Peak **0.42 ms**。
  - 120s 全量（含开局/结算/UI 切换）：平均 **61.6 FPS**、1% Low **37.2**、上传 P99 **0.375 ms**、DrawCalls 均值 **378**。
  - 1% Low 的两次差异来自 120s 采样覆盖了结算/等级 UI 的一次性分配尖峰（单帧最高 GC 8.6 MB），纯战斗段稳定在 40+；本轮新增渲染代码无逐帧分配（反射捕获为一次性，已提前到进场预热期）。
  - DrawCalls / GC 均为 Editor PlayMode 口径（包含 GameView/SceneView/阴影/后处理 Pass 与既有 UI/战斗系统）；真机红线仍需 Player 包采样。
- **视觉对照**：`Logs/visual_before_game.png`（改动前）→ `Logs/visual_final_game.png` / `Logs/visual_final_wet.png`（改动后）；`Logs/ab_spec_on/off.png` 与 `Logs/ab_spec_diff_x4.png` 证明环境反射在整片墨面产生可见贡献。

## 6. 回退与已知限制

- 想回到旧观感：`ArenaStyleSO.postProcessingEnabled=false` + `envSpecStrength=0` + `inkCoatStrength=0`；或关闭 `P1_INK_HIGH` 验证移动降级路径。
- 当前没有 SSR / 平面反射 / 体积雾；环境 Cubemap 是进场一次性捕获，反射内容是静态场景（角色/新墨不会实时出现在反射里），对粗糙墨面足够，对镜面金属会略有"定格"感。
- 水面反射来自同一张 Cubemap + 解析天空，不含实时波纹扭曲的场景倒影。
- 场景几何仍是硬边盒子；倒角/灯带需要重建主场景，另开任务。