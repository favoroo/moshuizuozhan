# CHANGELOG — 墨水大作战（P1）

> 排查知识库：**每条 1~3 行** = 一句话结论 + 根因 / 陷阱（Bug 用 `Fixed:`）+ 关键文件路径。
> 格式 `## YYYY-MM-DD #序号`（当日从 `#01` 起、**不记时间点**、**新的在最上面**）。
> **不记**：逐项 `Added`/`Changed`/`Verified` 流水账、测试项数与 exit 状态、selftest 细节、用户原话、设计推导、临时出图路径、采样数值、SHA/体积明细。
> 2026-11-01 之前的条目按 GitHub Release 说明回填，只有版本里程碑。
> **反复踩的坑**：① 外部工具改完 `.cs` 编辑器可能不触发重编译——先 `AssetDatabase.Refresh(ForceUpdate)` 再核对 `Library/ScriptAssemblies/*.dll` 时间戳，否则测试/出包跑的是旧程序集；② 提交前 `git diff Materials/Characters/Mat_TankGlass_*.mat`（`_SrcBlend` 只要窗口里碰过玻璃材质就可能被写回 5，与 `_ALPHAPREMULTIPLY_ON` 配对会双重乘 alpha）；③ 新增上屏中文先跑 `python3 tools/font_charset.py` 并重跑菜单 `P1/画风/漫画风/0`，否则 `FontCoverageTests` 判红。

## 2026-11-09 #08
【移动端地面细节 + 全距离落地感】移动端原先被 `P1_INK_HIGH` 整档关掉，`ArenaGround_N/RMO`（板缝凹槽、气泡坑、油污 AO、粗糙度）三张材质里明明挂着却从不采样——拆出 `P1_INK_GROUNDMAPS` 与 `P1_INK_HIGH` 同组互斥（`#pragma multi_compile _ P1_INK_HIGH P1_INK_GROUNDMAPS`），`RenderEnvGlobals.EnsureTier` 在移动端只开这一档；亚纹素墨缘、碎墨、B 样条仍只留给桌面。
新增「掩体落地接触遮蔽」：`InkSurfaceController.RebuildGroundContactMask` 在 `SealNarrowSlits` **之前**取不可涂格（否则死缝会在地面留下莫名暗线），两遍可分离盒式模糊（半径 `groundContactAORadiusMeters`）烘成 R8 遮蔽场，InkGround 按**计分场地矩形**乘到地面基底色——不能用 `inkUV`（图集比计分场四周多 1.5m 边距，会整体外扩失准）；纹理四周留 1 texel 白边，否则场外 74×84m 码头基座会被 Clamp 拉出一条暗带。
补的是实时阴影（shadowDistance 48m 外）与 SSAO（屏幕空间 0.6m）都覆盖不到的远景箱底/墙脚；冻结 `timeScale` A/B 实测遮蔽场开/关 3.93% 像素变化、平均压暗 6.7 灰阶，`P1_INK_GROUNDMAPS` 对无贴图档 56.4% 像素变化。文件：`Shaders/InkGround.shader`、`Scripts/Ink/{InkSurfaceController,InkGridConfigSO}.cs`、`Scripts/Core/RenderEnvGlobals.cs`。

## 2026-11-09 #07
【移动端画质同步】把 #05 的质感调优补齐到 Android（其余项都落在共用资产上，只有 SSAO 缺）：`Mobile_Renderer` 原本完全没挂 SSAO，新增 feature 用 `Source=Depth` + `Downsample=1` + 4 采样 + 高斯模糊——刻意不用 PC 的 DepthNormals 源：那要每帧多跑一遍几何 prepass，移动端 DrawCalls 预算 150 下比 AO 本身更贵，Depth 源只多一次深度拷贝（法线由片元从深度重建）。`Mobile_RPAsset` 调色模式 LDR→HDR，否则同一份 Neutral tonemap + -0.10 EV 在移动端先裁高光再调色，两端对比不一致。
Fixed: 两个 RPAsset 的 SSAO *变体剔除* 标记与实际档位对不上（PC 用 `Samples=Low` 却把 `_SAMPLE_COUNT_LOW` 剔了）——这些是 `multi_compile_local`，编辑器永远编满，**只有出包才暴露**；`SSAO.hlsl` 的 `#else` 恰好等于 Low、`_SOURCE_DEPTH_NORMALS` 被剔只会静默退化成深度重建，两种症状都不报错。剔除标记改为按各端实际档位保留（`VisualPolishTools.ApplySsaoPrefilters`）。
切 Mobile 画质冻结 `timeScale` 做 A/B 验证 AO 确实落地（AO 开/关像素有差、两帧都关则逐像素一致）。文件：`Scripts/Editor/VisualPolishTools.cs`、`Settings/{Mobile_Renderer,Mobile_RPAsset,PC_RPAsset}.asset`。

## 2026-11-09 #06
【更新说明去校验码】Release 正文不再出现 `**SHA-256**: …` 行——`release.py publish` 删掉自动追加逻辑（校验值只留在 `build/release-meta.json` 与终端汇报），玩家看不懂哈希。配套 `UpdateNotesMarkdown` 增加 `ChecksumRx` 整行丢弃，已发布的老 Release 在游戏内弹窗里也不再露出校验码（GitHub/Gitee 网页正文仍是历史原文）。文件：`.agents/skills/p1-release/{release.py,SKILL.md}`、`Scripts/UI/UpdateNotesMarkdown.cs`、`Tests/EditMode/UpdateSystemTests.cs`。

## 2026-11-09 #05
【画面质感调优】对标 INKWAVE 的「留 headroom + 找体积」：曝光 +0.12→-0.10 EV、ColorCurves 补 satVsSat vibrance（中性提彩/队色墨保护）、阴影下限地面 0.36 / 道具 0.40 / 角色 0.46（发平主因是 shadowFloor 把暗部抬到 0.46~0.55）；墨面加保色相软肩 `P1InkHeadroom`（max 通道越 0.9 整体回收，不让队色 R 先撞白）+ 墨缘背光侧压暗 + 单斑色调差 0.93~1.06→0.88~1.12，三图 `_InkGlow` 0.2/0.1→0.04、场景 `inkGlow` 0.2→0.06；图集开 mipmap（0.5→0.2s）消远景摩尔纹；新增菜单 `P1/画风/8. 应用质感调优档` 幂等落地（78 个共享 Toon 材质 + 三张地面材质）。
Fixed: SSAO 一直是白渲染——所有场景走自定义 Toon shader，无人采样 `_ScreenSpaceOcclusionTexture`，`AfterOpaque=0` 时 SSAO 只作用于 URP Lit；PC_Renderer 改 `AfterOpaque=1`（0.85/0.6）并给 `CharacterToon(Outlined)` 补 DepthNormals pass（原先角色像素读到的是身后地面法线）。陷阱：`EditorUtility.SetDirty(组件)` 不会把场景标脏，改 `InkSurfaceController.inkGlow` 后必须 `MarkSceneDirty` 再保存，否则退出 Play 静默回到旧值。文件：`Scripts/Editor/{VisualPolishTools(新),ArenaAtmosphereBuilder,ToonStyleInstaller}.cs`、`Environment/ArenaStyleSO.cs`、`Ink/InkGridConfigSO.cs`、`Shaders/{InkWetShading.hlsl,InkGround,InkSurfaceOverlay.hlsl,CharacterToon*}`、`Materials/*.mat`、`tools/font_charset.txt`。

## 2026-11-09 #04
【UI 手感整体优化】点击音收口到 `UIJellyButton.PlayClickFeedback`（根因：`UISoundBinder.AutoBindScene` 只在 `AfterSceneLoad` 跑一次，本项目 UI 几乎全是运行时装配，后建按钮永远挂不上 → 9 个界面里 7 个静音）；涂地飘字补 `Toggle` 音（仅 ≥10m² 且 0.26s 自节流）、倒计时 30s 红切与最后 10 秒滴答补音；准星墨边加粗到与白芯持平（`MakeRingSprite` 各环 outline=core、`BarOutline` 1.2→2.4），亮地面「白芯只占 33% 笔画、对比度 2.62:1」改为 50% 深色轮廓；菜单补 `PlayOut()` 退出转场（原先一律 `SetActive(false)` 硬切），`UpdateDialog` 补上「建了 motion 从未 `PlayIn()`」的入场。
Fixed: 暂停面板的响应式缩放与 `UISpringMotion` 抢同一 `localScale`——矮屏（20:9 横屏）入场结束会把缩放覆盖回 1、面板底部按钮出屏；改为新增 `PausePanelScale` 父容器承载缩放（与 `UpdateDialog`/`PrivacyConsentDialog` 同方案），`PauseMenuPlayTests` 相应改为按名字递归查找、不写死层级。陷阱：Play 期间改 `.cs` 不会重编译（`Resume(bool)` 一度报「无此重载」、dll 比源码旧两小时），必须先 `AssetDatabase.Refresh(ForceUpdate)` 再核对 dll 时间戳才验得了新代码。文件：`Scripts/UI/Core/UIJellyButton.cs`、`Scripts/UI/{WeaponReticleUI,TurfScoreView,CenterTimerView,UpdateDialog,PrivacyConsentDialog,MobileLayoutCustomizer,MatchFlowUI,MapSelectOverlay,HomeInfoOverlay}.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Tests/PlayMode/PauseMenuPlayTests.cs`。

## 2026-11-09 #03
Fixed: 俯视投影涂地「涂了不计分」——道具/集装箱顶面与脚下地面共用同一个 XZ 格，`InkGrid` 把道具占位格判为不可涂（不计分），但顶面墨迹仍由图集直接渲染，玩家看到箱顶有墨、比分不涨。新增计分掩码 `_GlobalInkScoreMaskTex`（R8，`InkSurfaceController.RebuildScoreMask` 在窄缝补偿后烘焙），由 `InkSurfaceOverlay.hlsl` 的 `P1EvaluateTopInk` 裁掉不可涂格，保证「看得到的顶面墨迹 = 会计分的墨迹」；`WeaponFeelProfile` 的 `TrailVisualScale` 全部归一到 1.0（视觉半径=计分半径），飞溅墨珠 `PaintSpeck` 改为同时写网格与图集。陷阱：掩码必须在 `SealNarrowSlits` 之后重建，否则死缝里仍显墨；`PropLit/ObstacleDitherLit/SpawnPad` 任一消费者编译失败都会让顶面墨迹静默消失，新增 `InkGroundShaderTests.TopInkShaders_HaveNoCompileErrors` 门禁。文件：`Scripts/Ink/{InkGrid,InkSurfaceController}.cs`、`Shaders/InkSurfaceOverlay.hlsl`、`Scripts/Weapons/{WeaponFeelProfile,WeaponConfigSO}.cs`、`Scripts/Core/VfxPool.cs`、`Tests/{EditMode/InkGridTests,EditMode/InkGroundShaderTests,PlayMode/InkAtlasRenderPlayTests}.cs`。

## 2026-11-09 #02
Fixed: 移动端底部生命值条全黑——`MobileTouchUI.Awake` 抢在 `HudController.Awake` 之前调 `ApplyMobileLayout()`，先建了一套兜底 `StatusCardsRoot` 并缓存 `_hpFill/_hpBadgePill`；正式工厂横条组随后复用该缓存，跳过填充与徽章底圆创建，只剩黑轨道叠在完整血条上。修法：`_hudInitialized` 守卫兜底组建在 Awake 之后、正式组就位时 `DiscardFallbackCardsRoot()` 先清引用再销毁、生命条填充/徽章按当前卡校验；新增 PlayMode 回归用例 `HudController_EarlyMobileLayoutBeforeAwake_KeepsHealthBarComplete`。文件：`Scripts/UI/HudController.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`。

## 2026-11-09 #01
Fixed: Android 发布包构建报 `CS0103: _debugPreviewUntil does not exist`——`SubmergedInkView` / `LowHealthVignetteView` 的字段在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 内、`DebugPreview()` 方法却留在外面，编辑器（含测试）能编译、只有 Player 构建才炸；按 `KillFlashView` / `LensInkView` 的既有写法给方法补同一层编译守卫。陷阱：`run_tests.sh fast` 全绿不等于能出包，改完 `#if` 一定要跑一次 Release 构建。文件：`Scripts/UI/{SubmergedInkView,LowHealthVignetteView}.cs`。

【发布 v0.1.31】含 2026-11-09 `#02`~`#08`；APK `moshuizuozhan-v0.1.31-arm64-v8a.apk`。

## 2026-11-08 #36
Fixed: 喷墙不再留墨、子弹穿过箱体/地面——`InkProjectilePool.TryCheckImpact` 的「贴脸重叠补判」改造把球扫的世界命中 `found` 丢掉了（只返回 `OverlapCheckDue && TryResolveOverlappedCombatant`），非爆破弹丸因此只认角色命中，墙/地一律穿透，只剩沿途滴墨的 XZ 投影涂到箱顶与地面。恢复「`found` 即按世界命中结算」，重叠补判只作为球扫落空时的补充；`WallInkPlayboxTests` 7/7 通过（含此前判红的 `ProjectileHit_PaintsWallFace`）。文件：`Scripts/Weapons/InkProjectilePool.cs`。

【发布 v0.1.30】含 2026-11-08 `#01`~`#36` 与 2026-11-09 `#01`；APK `moshuizuozhan-v0.1.30-arm64-v8a.apk`。

## 2026-11-08 #35
【底部双横条放大 + 抗锯齿 + 满能状态环】徽章 28→34 / 图标 24→28 / 轨道 248×14→300×18（移动端 379×87，行高 38、轨道 330×20）；"锯齿/不清晰"的根因是 UI 网格无 MSAA + 128px 图标与 64px 胶囊贴图被硬缩 4~6 倍——新增 `UIInkShapeGraphic.EdgeFeather` 外沿 alpha 羽化（默认 0，只给徽章/轨道开 1.6px）、`UIFlatSprite.Pill` 生成 mip + 三线性、`MediaImportPostprocessor` 新增 `UiIconSprite`（mip + 三线性，`GetVersion=1` 触发既有图标重导入）。满能除徽章反色外新增 `ReadyRing`（徽章外留 2px 缝的 4px 黄环，填充透明，充能隐藏、满能亮起），仍不做闪条/爆闪/脉冲。文件：`Scripts/UI/{HudSpecialGaugeFactory,LiquidOrbView,HudController}.cs`、`Scripts/UI/Core/{UIInkShapeGraphic,UIFlatSprite}.cs`、`Scripts/Editor/MediaImportPostprocessor.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #34
【撤回满能闪烁】#33 的条身黄↔纸白 1.5Hz 闪 + 条尾爆闪被判定"乱七八糟"：全部撤掉（`UpdateReadyFlash`、`ReadyBurst` 节点、满能 Punch 一并删除），大招满能**只换左端徽章状态**（墨底黄图标 ↔ 黄底墨图标，3px 墨边常驻），条身颜色/长度恒定。约定：HUD 除低墨 / 低血功能性告警外不做任何循环闪或一次性爆点，状态切换只允许"某一构件静默换外观"。文件：`Scripts/UI/{LiquidOrbView,HudSpecialGaugeFactory}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #33
【满能状态加强】反馈"大招满了看不出来"：满能待放时**条身整体黄 ↔ 纸白 1.5Hz 硬切闪烁**（进满能同帧先亮一次纸白，释放/失效立即停），徽章继续保持反色（黄底墨图标），条尾一次性爆闪 96→128 并改纸白；只翻 28px 徽章不足以传达"满了"，条身才是主信号。闪烁只在相位翻转时写顶点色（不重建、零 GC），属于低墨/低血同族的功能性状态提示，禁止挪作装饰循环。文件：`Scripts/UI/{LiquidOrbView,HudSpecialGaugeFactory}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #32
【底部 HUD 三轮：条内只留徽章 + 横条】按反馈删掉每行右端的「Q」键帽与生命读数芯片（50×26 胶囊把 28 高的细条切成两段，被判"太丑"），轨道拉满整行（桌面 192→248、移动端 214→277，组宽 284/318 不变，右沿 = 行右沿）；满能改由徽章**整体反色**（黄底 + 墨图标 + 墨边）表达，低血告警改为**填充色**红↔纸白 2.2Hz 闪（条长 = 剩余血量，只换色不换长度），条内不再有任何数字/键帽。陷阱：反色必须底色与图标同时翻——只翻底色会让黄图标消失在黄底上（二轮曾因此只敢翻描边）；`ReadyHint` 的硬影补齐逻辑（`EnsureReadyHintHardShadow`）随键帽一起删，留着会对着空节点每帧 `transform.Find`。文件：`Scripts/UI/{HudSpecialGaugeFactory,LiquidOrbView,HudController}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #31
【底部 HUD 二轮：双窄横条】两个小圆环改回**两条 284×64 的窄横条**（行高 28 / 轨道 14、行距 8）：左端身份徽章（大招=墨底黄技能图标、生命=绿底墨十字，均 24px 图标 + 色签硬影）+ 墨黑胶囊轨道 + 内缩 2px 的胶囊填充（`UIPillImage`，移动端 318×73 放大）+ 右端槽位（大招「Q」键帽 50×26、生命读数芯片 50×26 墨底纸白边 + 20 号数字）；满能 = 填充铺满 + 徽章 3px 描边翻黄 + 键帽翻黄弹入 + 一次 0.45s 爆闪 + 整行 Punch，生命告警 = 填充/徽章翻红 + 芯片墨底↔红底 2.2Hz 闪 + 掉血 Punch。陷阱：**徽章底色不要翻黄**——技能图标是密集剪影，底色翻黄会被图标吃光导致状态读不出，满能只翻描边；填充不要再用 `UIArcRingGraphic`。另记：Play 期间改 `.cs` 时 Unity 会推迟到退出 Play 才重编译，截图/测试都可能跑旧程序集，改完先 `stop` + `AssetDatabase.Refresh(ForceUpdate)` 再验。文件：`Scripts/UI/{HudSpecialGaugeFactory,LiquidOrbView,HudController}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #30
Fixed: 集装箱喷印在另外两个长面上**整片左右镜像**（字反着读、CSC 牌跑到另一端）——`BuildContainerLivery` 把 `right` 硬写成 `(0,0,1)` / `(1,0,0)`，只对一侧成立；改由面法线推导 `right = cross(normal, up)`。Fixed: 木托盘/木箱/电缆盘渲染成惨白"纸箱"——`BuildStructuralDecorations` 用 `new Palette(null)` 兜底色（ArenaStyle 的深木色没传下来），且顶点色按 sRGB 原值直写进 Linear 色彩空间（0.47 当线性用屏幕上变 0.72），两处叠加整堆冲白；散件改自带 `.linear` 转换的中间调木色，并新增 `Mat_Prop_WoodDetail`（PropLit + Wood 法线/RMO，原先复用 `Mat_Prop_Matte` 吃到的是混凝土骨料法线）。文件：`Scripts/Environment/{ArenaClutterBuilder,ArenaGeometryBuilder,ArenaMaterialSet}.cs`、`Scripts/Editor/ProjectScaffolder.cs`。

## 2026-11-08 #29
Fixed: 武器卡顶部身份色带横向戳出圆角卡片轮廓（选中态更明显，看着像乱画的横线）——`WeaponCardView.RefreshAccentBand` 只按 `-border*2` 重算宽度，把构建期「圆角半径 + 墨框」的内缩量冲掉，色带两端正好压在圆角外缘；改为构建 / 选中 / 取消选中统一走 `ApplyAccentBandGeometry`，内缩量随当前边框重算（半径口径同 RoundedRect 的 `ComicStyle.CornerFor`）。新增 `WeaponSelectPlayTests.WeaponCardView_AccentBand_NeverOverflowsRoundedCorner` 锁住三态几何。文件：`Scripts/UI/WeaponSelect/WeaponCardView.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-08 #28
【底部双环仪表重设计】悬浮「Q」键签改为**常驻停靠在大招环右上角的键帽角标**（充能=墨底纸白字隐入墨底圆，满能=翻黄底墨字+Back-Out 弹入+一次 0.45s `UIBurstGraphic` 爆闪播完自停）；生命环新增右上角**绿底墨十字角标**（`glyph_plus`，告警翻红）——两环各一枚右上角标，身份一眼可辨；两环墨底圆补色签硬影 (2,-3) 离地，低血/踩墨数字红白交替闪（2.2Hz，同低墨告警口径）+ 掉血整环一次 Punch（0.25s 节流），复活倒计时上移 12→34px 给角标让位。陷阱：直接写 `TMP_Text.color` 会被 `UIChunkyText` 重建时的 `fillColor` 冲掉，换色必须走 `SetFillColor`；角标缩放统一走 `HudSpecialGaugeFactory.ApplyRingBadgeLayout`（移动端 ×1.2，字号 Font() 已自动放大）。测试 `HudSpecialGaugePlayTests` 的「释放后键签隐藏」断言改为「键帽常驻翻色」。文件：`Scripts/UI/{HudSpecialGaugeFactory,LiquidOrbView,HudController}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Docs/UI-GeometricBold.md`、`tools/font_charset.txt`（重跑账本+菜单漫画风/0 补「帽逆」字形）。

## 2026-11-08 #27
【第一张图（货柜码头）细节精修】集装箱从纯立方体换成真实瓦楞壳：新 `Environment/ArenaContainerMeshFactory.cs` 在 unit 空间生成"棱脊贴标称面、凹槽向内收"的侧壁瓦楞 + 上下横梁台阶 + 顶面纵向压型肋，因此 BoxCollider / InkWallSurface 的墨面 Quad（外扩 1cm）/ OcclusionFader / 静态合批包围盒与旧立方体逐位一致，碰撞、涂墨、遮挡和既有测试断言全不受影响。陷阱：凹槽不能外凸，否则墨面 Quad 会被瓦楞穿透成破面；顶盖必须铺满 ±0.5，只在"肋的起止"留边会留出能看到箱内的缝。配套 `ArenaClutterBuilder.cs`（托盘/油桶/锥桶/缆桩/电缆盘/轮胎堆/护栏/墙面配电箱与卷帘门 + 箱体喷印编号与 CSC 标牌，全部无碰撞无墨面、合并进木/金属/发光三张网格），远景近排集装箱补瓦楞与角件。贴图由新 `tools/bake_arena_surface_details.py` 程序化烘焙 `Surfaces_Container_{N,RMO,Albedo}.png` 与 `Surfaces_Concrete_Albedo.png`；`ObstacleDitherLit` 新增 `_BaseMapWorldSpace/_BaseMapWorldScale`（瓦楞壳 uv0 是整面归一化坐标，直接采样会把长面压成一个贴图周期）。修掉两个既有穿帮：端门加强筋颜色是 `Lerp(boxColor, steelDark, 0.35)`，在芥末黄箱上插值成土黄被读作木条（改深钢灰 + 横条改竖向门板瓦楞）；四个集装箱材质里只有 Slate 的 `_EdgeLineStrength` 落在 shader 默认 0.85、其余三个是 0，同排箱子混着"有描边/无描边"。文件：`Scripts/Environment/{ArenaContainerMeshFactory,ArenaClutterBuilder,ArenaGeometryBuilder}.cs`、`Shaders/ObstacleDitherLit.shader`、`Scripts/Editor/{ProjectScaffolder,ToonStyleInstaller,ArenaDecorationBuilder}.cs`、`tools/bake_arena_surface_details.py`。

## 2026-11-08 #26
Fixed: 结算页「本局成就」节标压在战报卡下边框上——成就条挂载点（-332 / -330）只按卡行高度算，没给节标自己占的题头带（胶囊 34 高 + 与卡行 6px 缝）留位置。整条下移到 -368 / -370，CTA 按钮跟着下沉并同步 `ApplyResultColumnFit` 的底缘包络（-508 / -504 → -520 / -530）；移动端节标字号封顶 20（×1.28 放大到 28 会把胶囊撑成 47.6，20:9 手机上顶不住）；桌面溢出读数行从卡行下方改挂节标同行右对齐。文件：`Scripts/UI/{MatchFlowUI,ResultAchievementsView}.cs`、`Tests/PlayMode/ResultLayoutPlayTests.cs`。

## 2026-11-08 #25
【整体流畅度优化】战斗期 GC 尖刺与每帧重建收敛：删掉 `InkSplatAtlas._active` 诊断 List（每帧 O(n²) `RemoveAt` + 持续扩容）与网格每帧 `Clear()`（改 `Clear(false)` 保留顶点布局），弹丸池 `_active` 容量 96→512（旧版 96→384 翻倍即 40~80KB 单帧分配），贴脸 `OverlapSphere` 补判改 4 帧轮转（出生首帧/自由落体仍逐帧），小地图上传 scratch 改为整图一次性预分配，音频淡出不再每帧 Stop+Start 协程；HUD 占领百分比文本 10Hz、比分条 15Hz + 0.2% 量化（避免 `UIPillImage` 胶囊贴图随条宽反复重建）、复活倒计时先比 0.1s 档位再拼字符串、`ReadyHint`/`SpecialReadyPip` 的 `transform.Find` 只解析一次；AI 的 A* 按帧奇偶错峰（7 个 Bot 同相重算会叠加尖刺），遮挡视线球扫 20Hz（Fader 仍逐帧插值）。DrawCall：发裙/墨罐液面不再投影，运行时清掉 P39 遗留的 `MergedStatic_Mat_TankGlass` 合并组（每角色 base+shadow 各 1），并给馒头头补 `RecalculateTangents` 修复 `Kid/Head` 合并校验（下次重建场景生效）。文件：`Scripts/Ink/{InkSplatAtlas,InkSurfaceController}.cs`、`Scripts/Weapons/InkProjectilePool.cs`、`Scripts/UI/{CenterTimerView,HudController,RosterAvatarSlot,WeaponSelect/RespawnLoadoutView}.cs`、`Scripts/AI/BotController.cs`、`Scripts/Environment/TopDownOcclusionManager.cs`、`Scripts/Core/AudioManager.cs`、`Scripts/Player/{CharacterVisualAssembler,CharacterVisualController}.cs`。

## 2026-11-08 #24
Fixed: `tools/run_tests.sh` 预检只认 `script.recompile_status` 的 `completed` / `idle`，而工程已编译且无改动时 Unity 6 报 `up_to_date` → 预检空转 60s 后误报「编译状态未就绪」。判断里补上 `up_to_date`。

## 2026-11-08 #23
底部 HUD 由两张纸白大卡（大招 332×88 + 生命 380×88，合计 712 宽）改成两个小圆环：大招环 66px（墨底正圆 + 黄弧 + 技能图标，满能时弧铺满 / 中心圆翻黄 / 图标翻墨 / 环上方弹「Q」键签）、生命环 68px（墨底正圆 + 绿弧 + 环心数字，低血或踩敌墨翻红），整组 150×68 居中、底边 y=56，移动端 72/76、底边 48。陷阱：环状弧**不能**用 `Image.Filled + Radial360` 配运行时生成的圆环精灵——即时 Canvas 重建会把它整块画成不透明矩形（`Simple` 正常、径向失效），改用网格基元 `UIArcRingGraphic`（`Fill01` 顶点起顺时针，带宽 = 直径 × 0.12、外沿留 3px 墨底即自动描边）。顺带下线存活时的「墨水 / 手雷 / HP」长串（只留阵亡复活倒计时，锚在环组上方 12px）；`HealthBarFill` 节点保留但禁绘制（数据载体），实际绘制走同卡 `HealthBarArc`。文件：`Scripts/UI/{HudSpecialGaugeFactory,LiquidOrbView,HudController}.cs`、`Scripts/UI/Core/UIArcRingGraphic.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-08 #22
双枪翻滚后不再被锁在原地：删掉翻滚后 0.5s 架枪阶段的移速锁零（`DualiesWeaponStrategy.MoveSpeedMultiplier` 恒 1，`InkCharacterMotor` 删掉 `IsTurretLocked` 的 `CurrentMoveSpeed = 0` 分支），并把架枪态从 `WeaponController.WantsAimFacing` 摘掉——移速一解放，朝向若还钉在准星上，移动输入带不动身体，手感依旧是被钉住（开枪时另有 `_isFiringOrThrowing` 兜朝向）；射速 +20% / 散布清零 / 准星菱形锁定框 / 躯干前压下沉等效果全部保留，`BotController` 同步摘掉该再翻滚闸门。陷阱：`IsTurretLocked` 是历史命名，语义已是「架枪强化」，别按字面当移速锁用。文件：`Scripts/Weapons/{DualiesWeaponStrategy,WeaponController,MainWeaponStrategy,WeaponConfigSO}.cs`、`Scripts/Player/{InkCharacterMotor,DodgeRollPoseSolver}.cs`、`Tests/{EditMode/Weapon*Tests,PlayMode/MotorFeelPlayTests}.cs`。

## 2026-11-08 #21
蓄力狙蓄力音「太吵」三处整改：烘焙侧锯齿改三角波、整层删 7kHz 气声、粉噪床减半去共振（`tools/bake_sfx.py` `_f_charger_charge`，已重烘并重建 P1RuntimeAssets）；混音侧 `chargerChargeLoopVolume` 0.50→0.38（asset 已同步）；新增 `PlayLoop(kind, multiplier)` 重载（旧签名保留），循环音随 `Charge01` 从 0.40 渐强到 1.0——轻蓄安静、满蓄全量（`WeaponAudioDriver.cs`）。陷阱：`bake_sfx.py` 会做峰值归一化，调音只降层峰值没用，必须改频谱构成。

## 2026-11-08 #20
Fixed: 结算页「再来一局」按钮在宽屏下溢出屏幕底——根因是整列按 1080 高设计稿从中心绝对定位，而 HUD 画布 `ScaleWithScreenSize(match 0.5)` 在宽屏下纵向单位收缩（~2.1:1 时只剩 ~982），按钮底缘（-508）出界。新增 `MatchFlowUI.ApplyResultColumnFit`：按 resultPanel 实际高度把整列（判定牌/读数板/战报卡/成就条/按钮组）等量上移，间距不动，16:9 位移为 0，Update 里跟随窗口比例重排。文件：`Scripts/UI/MatchFlowUI.cs`。

## 2026-11-08 #19
【准星 HUD 打磨（用户实测反馈两条）】① `+N m²` 飘字会被墨水罐挡住 → 出生点从中心 +34,-30 下移到 +24,-52（INKWAVE 同位的准星正下偏右），并把 `EnsureInkTankCrosshair` 挪到 `EnsureTurfScore` 之前建——后建者渲染在上，飘字上飘掠过罐体下缘时不再被玻璃底盖住；② 手雷刻度线实色红杠太突兀 → 内收进玻璃宽度（20+4 探出 → 16×2 贴内）、厚度 3→2、配色改常态半透明白 55% / 缺墨时柔红 80%（`s_thresholdIdle/Warn`），不再整条探出罐体。文件：`Scripts/UI/{TurfScoreView,InkTankCrosshairView,HudController}.cs`。

## 2026-11-08 #18
【开赛扫场镜头（对标 INKWAVE `CameraRig.cinematic`）】新增 `MatchIntroCameraDirector`（运行时自装配，LateUpdate 顺序 200 晚于跟随相机）：Countdown 期间把机位架到敌方半场高空绕场地中心横扫 34°，再二次贝塞尔俯冲到玩家身后，t=1 姿态直接取「跟随相机本帧写出的位姿」作为路径终点 → 放手瞬间零跳变。Countdown 时长改为 `max(introSeconds, introCameraSeconds=3.4s)`（0 = 关闭）；扫场期间 `TopDownOcclusionManager` 停摆并复位全部 fader（高空视线会把整片掩体判成遮挡、网点狂闪），HUD 内容根与移动端触控层让位、Playing 首帧恢复（墨幕转场挂画布根，只关内容根不关画布）。陷阱：`GameObject.Find("HudSafeArea")` 必须在开赛前取（首帧就被 SetActive(false) 后找不到）；Countdown 变长会让"等 Playing"的旧用例多等 3.4 秒，`PlayModeTestKit.ShortenMatchTimers` 必须同步把 `introCameraSeconds` 置 0。文件：`Scripts/Player/{MatchIntroCameraMath,MatchIntroCameraDirector}.cs`、`Scripts/Core/{MatchManager,GameConfigSO}.cs`、`Scripts/UI/{HudController,MobileTouchUI}.cs`、`Tests/{EditMode/MatchIntroCameraMathTests,PlayMode/MatchIntroCameraPlayTests,EditMode/MobileTouchUITests,PlayMode/PlayModeTestKit}.cs`。

## 2026-11-08 #17
【油漆观感对标 INKWAVE 第二批：墨斑"活"起来 + 图集三次 B 样条重建】写侧生长编排：decorated 墨斑进 `InkSplatAtlas._growing` 每帧重绘（A=Max/R,B=Over 混合幂等；主体 40%→100% ease-out 泛出、射线先行、卫星墨滴错峰落地**越远越晚**、细雾收尾，时长随半径缩放），替换旧片元均匀侵蚀（`_InkGrowActive` 恒 0，`PushWetClockGlobals` 一行可回滚）；旧"死板"根因还有 `MakeSeed` 按 0.125m 位置量化——同点位连喷形状完全相同，加逐墨斑盐解决。着色侧新增 `P1SampleInkAtlasCubic`（16-tap 三次 B 样条 + 解析梯度 + 导数钳 ±8 纹元）接入地面 ForwardLit 与顶面 `P1EvaluateTopInk`，消墨缘法线/接触阴影/两队交界的纹元级马赛克小面片；阴影中保饱和（INK_SHADE 对齐，亮度守恒提色度）。新参数 `ArenaStyleSO.inkBicubicStrength`（0=旧行为回退开关）/`inkShadowChromaBoost`。陷阱：①生长重绘靠 Over 混合"最新获胜"，CommandBuffer 提交顺序必须 [敌队强收尾→新墨→常规重绘]（新落墨时把范围内敌队生长墨斑 ForceFinish，INKWAVE 同款）；②回归底线是 `splatGrowSeconds=0`（测试路径）与 `tn=3` 时 SDF 与旧实现**逐位一致**；③B 样条 tap 会钳到边界纹素，场外屏蔽必须在混合前清零。文件：`Shaders/{InkGround.shader,InkSurfaceOverlay.hlsl,InkWetShading.hlsl}`、`Scripts/Ink/{InkSplatAtlas,InkSurfaceController}.cs`、`Scripts/Environment/ArenaStyleSO.cs`、`Scripts/Core/RenderEnvGlobals.cs`、`Tests/PlayMode/InkAtlasRenderPlayTests.cs`（新增写侧生长回归用例）。

## 2026-11-08 #16
【墨水量上准星：删底部墨水卡，新增准星旁竖罐（对标 INKWAVE `.iw-tank`，用户点名要这个布局）】新 `InkTankCrosshairView`（根锚画布中心，罐体 20×78 挂准星右 +56,0）：玻璃墨底 + 队伍色液柱（14/s 指数追值）+ 液面亮条随侧移速度倾斜 + 回墨冒气泡 + 手雷耗墨刻度线（不够丢雷时红线）；低墨（沿用 `HudGaugeMath` 迟滞 + 原警报音节奏）= 罐下弹「墨水不足」红签 + 罐框翻红（进入瞬间硬切闪 3 下后稳定红，不循环）；墨水打到 0 边沿整罐 0.35s 衰减抖动；满罐无告警 1.4s 整罐淡出（is-idle），一有消耗立即回来。底部三卡改两卡（大招 + 生命）居中重排——陷阱：`PlaceCard` 对 null 也推进 x（旧语义），墨水卡缺位必须跳过调用而不是传 null，否则两卡整体右偏 348px；`InkTankView` 类保留（旧场景认领路径 + 直接构造的 EditMode 测试不变），工厂 `GaugeCluster.Tank/InkCard` 置 null。文件：`Scripts/UI/{InkTankCrosshairView,InkTankCrosshairMath,HudSpecialGaugeFactory,HudController}.cs`、`Tests/PlayMode/HudSpecialGaugePlayTests.cs`、`Tests/EditMode/InkTankCrosshairMathTests.cs`。

## 2026-11-08 #15
【角色被敌方墨覆盖（对标 INKWAVE `character-mats.js` HURT_FRAG）】命中与踩敌墨会把**攻击方队色**糊上身体：`CharacterToon`/`CharacterToonOutlined` 加 `_InkColor/_InkAmount/_InkSeed/_InkThresholdMax/Min/_InkCurvePower` 程序化噪声墨斑（湿边压暗 + 墨面浓淡 + 压掉该处高光），阈值曲线按本噪声实测分布标定（满覆盖≈52% 表面积，刻意不糊满：整身换敌队色会让 4v4 分不清敌我）；`CombatantHealth` 逐单位共用一块 MPB 驱动：命中 `+伤害/60`、踩敌墨 `+0.5/s`（不受敌墨 DOT 60 血底线影响，2s 就能糊满）、离开 `0.6/s` 淡出、覆盖量 `= max(累积, 1-血量比)`（残血本身读作"被糊住"，回血一起淡掉）、复活清零、出生保护/大招无敌期不累积；新增 `PlayerConfigSO.inkCoat{PerDamage,InEnemyInkRatePerSec,DecayPerSec}`。
两个坑：① 旧白闪是 MPB 直接改 `_BaseColor`、结束时 `SetPropertyBlock(null)`，会把墨覆盖一起抹掉 → 白闪改由 `_FlashAmount` 在着色器里 lerp 到白，同一块 MPB 才能同时承载两件事；② 角色是 ~70 件刚体部件、各 mesh 的 object space 互不相同，直接用 `positionOS` 求噪声会让左右腿/手臂出现**镜像重复的墨斑**，必须用 MPB 推 `_InkPartOffset/_InkPartScale` 把顶点还原到"角色空间静止世界坐标"（按米制一致、跨部件连续）。文件：`Shaders/{CharacterToon,CharacterToonOutlined}.shader`、`Shaders/CharacterToonCommon.hlsl`、`Scripts/Player/{CombatantHealth,PlayerConfigSO}.cs`、`Tests/PlayMode/{InkCoatPlayTests,InkCoatVisualCapturePlayTests}.cs`。

## 2026-11-08 #14
【音效播放层：死音效接线 + 远场衰减 + 去重 + 变体自动取全】`blaster_boom`·`slosh_land`·`refill_full`·`charger_charge` 烘了却从没被播放；炸弹/大招/雷/跳跃/翻滚原为全局 2D 满音量（敌人隔整张图和贴脸一样响）→ 新增 `TryEventGain` 距离衰减并给这些方法加 `(worldPos)` 重载；`PlayKill` 在 `uiConfirmClip` 为空时把保底设成 `killClip` 同帧播两遍、炸弹投掷音在 `WeaponController` 与 `InkBombPool` 各播一次、`special_slam` 释放+落地各播一整段，均已修。
`Team == TeamA`（含 AI 队友）的大招就绪/复活/空墨/回墨/跳跃改 `IsLocalPlayer`；变体数组改按族自动探测（旧版写死前 2~3 个名字，加变体后独立包取不到），`EnsureEditorClips` 补齐 11 个漏填字段；voice 池 12→16。新增 19 个事件音（出生保护/低血心跳/复活滴答/比分反超/终局 10 秒/敌方大招警告/换枪/蓄力取消/撞墙/锁定/暂停/墨花分档/打墙/泵/加特林就绪/风暴风压）。文件：`Scripts/Core/{AudioManager,AudioMixConfigSO,RuntimeAssetLibrary}.cs`、`Docs/Audio-Audit.md`。

## 2026-11-08 #13
【音效素材全量重做：59→78 族、87→157 文件，每只 5~9 层】根因是 `bake_sfx.py` 的 `_bq()` 为破坏性 in-place 滤波，而 `noise()`/`tone()` 写完直接调它——同一时间段内**后写的层会把之前所有层整段抹掉**（`shot` 末层带通噪声擦掉了前 55ms 的低频体与 chirp），所以「层数越加越薄」；改成「渲染临时段 → 滤波 → 相加」后，同配方同峰值 RMS +59%（ink_hit_ground +77%）。
同批补上 INKWAVE 的粉/棕噪声、FM 衰减调制指数、非谐分音、共振带通（改成 RBJ 峰值增益 = Q；0dB 形式下 Q6 带通只剩 3% 振幅，层与层配不平）、bloops/plips 墨粒、环形颗粒床、线性循环淡化（等功率会把同相内容在接缝抬到 √2，每 1.8s 一次鼓包）与 `_soft_limit(1.3)`。文件：`tools/bake_sfx.py`、`tools/make_sfx_audition.py`（新增 A/B 试听页生成）。

## 2026-11-08 #12
【阵亡观战镜头（对标 INKWAVE death cam）：死亡后不再定格在空地】`TopDownCameraFollow` 新增观战模式：跟随目标 `OnKilled` 时若 `LastAttacker` 存活则接管镜头——前 0.8s 停在阵亡点看击杀爆墨，随后转向并在击杀者周围环绕（半径 4.2m→5.5m、高 2.4m、0.07 rad/s 漂移封顶 6s、FOV 收窄 6°），复活 `OnRespawned` 硬切回第三人称（68m 对角线地图上做长距离混合会扫穿掩体，故不做 INKWAVE 的 0.7s 回镜）；曲线抽成纯函数 `Player/DeathCamMath`，`TopDownOcclusionManager` 阵亡期间改投射击杀者，否则观战画面被集装箱糊死。陷阱：①环境死亡（踩敌墨/越界，`LastAttacker == null`）不进观战、保持原机位；②凶手自己中途阵亡要冻结最后已知位置，不能把镜头拉回自己尸体或跟去它的复活点；③`BindTargetHealth` 换目标时不能走 `EndDeathCam`（会 `SnapImmediately → ResolveActorRefs →` 递归），直接清标志位。文件：`Scripts/Player/{TopDownCameraFollow,DeathCamMath}.cs`、`Scripts/Environment/TopDownOcclusionManager.cs`、`Tests/EditMode/DeathCamMathTests.cs`、`Tests/PlayMode/ThirdPersonCameraPlayTests.cs`。

## 2026-11-08 #11
【涂地飘字改为准星位、删常驻读数卡（用户反馈：INKWAVE 的 +Np 是打在准星旁，左上角卡没有及时反馈感）】`TurfScoreView` 从左上角读数卡改为纯飘字层：根节点锚画布中心，飘字出生在准星右下（中心 +34,-30，同 INKWAVE `.iw-tpops`），大额（≥10m²）染队伍墨色、小额白字，均走 UIChunkyText 果冻字 + `iw-tpop` 关键帧曲线；删掉卡片/墨滴图标/追值滚动/图标 punch 及 `SetMobileLayout`、`ApplyTurfScoreLayout` 全套布局管线与对应 Math 函数，`EnsureTurfScore` 根物体不再挂 `UIInkShapeGraphic`。陷阱：Play 验收时飘字 1s 寿命 ≈ CLI `script.eval` 单条往返延迟，常规抓帧必空——把 `Time.timeScale` 临时压到 0.3 再 flush/capture、完事恢复，才能定格飘字；编辑器被并行 Agent 的测试循环反复抢走 playMode，验收要轮询 `test status` 空窗再动手。文件：`Scripts/UI/{TurfScoreView,TurfScoreMath,HudController}.cs`、`Tests/EditMode/TurfAndLowHealthUiMathTests.cs`。

## 2026-11-08 #10
【潜入墨水屏幕浸染（对标 INKWAVE swim 段）】潜入己方墨水时屏幕边缘向己方墨色轻微"沉下去" + 缓慢流动焦散光斑：`SubmergedInkView/Math` + `Resources/SubmergedInk.shader` 双 Pass（乘法 `Blend DstColor Zero` 复刻 INKWAVE 的 mix 保场景细节 + 加法焦散），强度 damp(7/s) 平滑、受「屏幕震动」滑杆门控、空闲关 Graphic；`EnsureSubmergedInk` 须排在所有 `SetAsFirstSibling` 视图之前建（最终位于低血暗角之上、不抢 InkEdge=0/LensInk=1 断言位）。陷阱：ShaderLab 共享代码块闭 token 是 `ENDCG` 不是 `CGEND`——解析报「CGINCLUDE/CGPROGRAM and ENDCG tags mismatch」且行号指向 CGINCLUDE 行，极易误判成他人 shader；新代码字符串含新汉字须重跑 `tools/font_charset.py` + 菜单 `P1/画风/漫画风/0`（本次账本 +浸）。文件：`Scripts/UI/{SubmergedInkView,SubmergedInkMath,HudController}.cs`、`Resources/SubmergedInk.shader`、`Tests/EditMode/SubmergedInkTests.cs`、`Tests/PlayMode/SubmergedInkPlayTests.cs`。

【发布 v0.1.29】含 2026-11-07 `#09`~`#24`；APK `moshuizuozhan-v0.1.29-arm64-v8a.apk`。

## 2026-11-08 #09
【涂地读数卡对标 INKWAVE 重设计（用户反馈「简陋、无及时反馈感」）】`TurfScoreView` 从「标签+数字」卡改为 `.iw-turf` 胶囊语义：墨滴图标（`glyph_drop` 染队色）+ 英雄数字（40 Heavy 果冻字 UIChunkyText）+ 小号 m² 单位；读数改为追值平滑滚动（`TurfScoreMath.StepShown` = `max(6, 差值*7)`，同 INKWAVE `_updTurf`），结算时图标做 scale1.5/rotate-12° punch，飘字换 `iw-tpop` 关键帧（缩放 .5→1.15→1 弹入 + ±8px 抖动 + ≥10m² 染队色 is-big），曲线全部纯函数可 EditMode 断言。陷阱：`UIChunkyText` 默认 `uppercaseLatin` 会把单位 "m²" 强转 "M²"（新增 `SetUppercaseLatin` 开关）；CLI `script.eval` 单条往返 ~1s，与飘字 1s 寿命同量级，Play 截图验收必须后台连续抓帧再挑帧。文件：`Scripts/UI/{TurfScoreView,TurfScoreMath,Core/UIChunkyText}.cs`、`Tests/EditMode/TurfAndLowHealthUiMathTests.cs`。

## 2026-11-08 #08
【视角描述大扫除 + 命名统一】全库排查「俯视角」陈旧描述：活文档 4 处（media-gen 技能链，含「STYLE.md 空白」过期说明）与代码注释 21 处（4 shader + 11 cs，纯注释/测试文案）统一改第三人称/斜俯视角表述；AGENTS.md 事实修正（玩法场景补 GullHarbor/NeonSkatepark、地图改 3 张、ArenaLayoutV2 降为共享常量源 + Harbor/Skatepark 各自 Layout/Builder）；命名统一为「墨水大作战」（release.py 发布标题、p1-release 模板、本文件标题），仓库/APK 前缀 `moshuizuozhan` 为仓库名保持不动。陷阱：usage-guide 引 GamePlan §4.1 却写成「正交俯视」——引用与被引矛盾时以被引文件为准；判断视角看实现别看类名（TopDownCameraFollow 已是第三人称轨道相机）。文件：`.agents/skills/media-gen/*`、`.agents/skills/p1-release/*`、`MediaRaw/style/STYLE.md`、`Shaders/CharacterToonCommon.hlsl`、`Shaders/InkGround.shader`、`Shaders/InkWetShading.hlsl`、`Scripts/UI/HitFeedbackMath.cs`、`Scripts/Weapons/{WeaponShot,WeaponRollSystem}.cs`、`Scripts/Editor/{ArenaDecorationBuilder,HarborGroundTextureBaker,SkateparkGroundTextureBaker,ProjectScaffolder}.cs`、`Scripts/Environment/{HarborGeometryBuilder,SkateparkGeometryBuilder}.cs`、`Tests/EditMode/{WeaponStrategyTests,ArenaAndOcclusionTests}.cs`、`AGENTS.md`。

## 2026-11-08 #07
【战斗 HUD 补三条信息闭环（对标 INKWAVE）】①个人涂地读数 + `+N m²` 飘字（左上角 `TurfScoreView`，走新事件 `MatchStatsService.TurfRegistered` 复用既有 `RegisterTurf` 埋点，0.4s 聚合窗口防连发刷屏）；②低血屏幕边缘暗角（`LowHealthVignetteView` + `Resources/LowHealthVignette.shader`：35% 起压暗，20% 以下有限 3 次硬切告警后停在稳定暗角、24% 迟滞去抖，受「屏幕震动」滑杆门控）；③击杀卡补受害者武器小圆标（`KillFeedEvent.VictimWeaponKind/HasVictimWeapon` + `KillCardView.Bind` 重载，旧 `Bind` 签名保留）。陷阱：`EnsureLowHealthVignette` 必须排在 `EnsureLensInk` 之前建，否则 `SetAsFirstSibling` 会把 LensInk / InkEdgeWarning 顶离 0/1 位、两个既有 PlayMode 层级用例判红；涂地事件回调同步跑在涂地热路径上，必须 try/catch。文件：`Scripts/UI/{TurfScoreView,TurfScoreMath,LowHealthVignetteView,LowHealthVignetteMath,KillCardView,KillFeedView,KillFeedEvent,HudController}.cs`、`Scripts/Core/MatchStatsService.cs`、`Resources/LowHealthVignette.shader`、`Tests/EditMode/TurfAndLowHealthUiMathTests.cs`。

## 2026-11-08 #06
【移除出生禁入圈】玩家反馈敌我出生台前被「空气墙」卡住（滑板场尤甚）。Fixed: 根因是 P16-A 的 `SpawnZone` 在出生线中心 (0,±23.5) 布了半径 6m 圆形推力场，只对敌方角色生效且每帧径向外推，圈前沿伸到 |z|=17.5——比出生甲板前沿（|z|=22.0）还靠外 4.5m，手感即空气墙。整套移除：`SpawnZone.cs` 及两测试文件删除，`MatchManager` 不再创建/配置，`BotController` 涂地目标不再避敌区，`GameConfigSO.spawnZoneRadius/PushSpeed`、`BotConfigSO.spawnZoneAvoidBufferMeters` 字段随功能删除；注意 `GullHarbor/NeonSkatepark.unity` 里序列化过 `SpawnZone_TeamA/B` 对象，需逐个删除保存（已清）。

## 2026-11-08 #05
【Agent 入口描述去残留】`p1-dev-workflow` SKILL.md 的 description 仍写「俯视角涂地对战」，新 Agent 会据此误判视角；已改为「第三人称」。陷阱：P9 改第三人称轨道相机时**类名 `TopDownCameraFollow`/`TopDownOcclusionManager` 为兼容旧调用方故意保留**，判断视角要看实现（`Scripts/Player/TopDownCameraFollow.cs` 头注释）而非类名。

## 2026-11-08 #04
【霓虹滑板场远景岸桥去黄重设计】4 座 22-28m 岸桥从整片警示黄改「钢青自动化码头」配色：主结构派生自 `Lerp(SteelDark, ContainerSlate, .5)`（相邻岸桥交替亮一档、最远一座压雾色）、保留白机房+双暗横条、主梁上缘加港青色带（呼应港青集装箱/场内涂鸦）、黄黑警示只留支脚台车；立柱 0.95→0.78m、主梁 2.5×2.1→2.2×1.7 更轻盈；极远 2 座岸桥剪影同步去黄（旧版会在真岸桥正后方读成「重影黄塔」）、HorizonCity 提亮半档雾化。根因：场内已有 2 座黄色英雄塔吊（`SkateparkDetailBuilder.BuildCranes` 刻意统一 mustard），远景岸桥再同色会让北天际线糊成一团黄、与冷色海天相冲。新增菜单 `P1/关卡与地图/重建霓虹滑板场远景 (Skate Backdrop)`：只销毁重建 `_SkateBackdrop` 并覆盖 `skate_backdrop_*.asset`，不整场重建不重烘地面。陷阱：该文件同时 `using System` + `UnityEngine`，裸写 `Object.DestroyImmediate` 报 CS0104 歧义，须写 `UnityEngine.Object`。文件：`Scripts/Editor/SkateparkBackdropBuilder.cs`、`Scenes/NeonSkatepark.unity`、`Art/Meshes/Arena/skate_backdrop_*.asset`。

## 2026-11-08 #03
【成就目录扩至 37 条 + 成就页签改 t-1 式方块网格】用户反馈两点：①「连杀」口径澄清——原 三连威慑/连杀狂魔 是 4 秒窗口多杀（`KillFeedbackState`），不是「一条命」；新增单条命击杀统计 `MaxLifeKills`（`MatchStatsService.RegisterKill` 内维护、victim 阵亡清零），成就 一命五连/这条命超神/命硬杀穿 三档，另加 复仇（Grudges 账本：被击杀记仇、反杀销账一次）/压哨（剩余 ≤10s，`IsClutchWindow` 纯函数）/杀意泛滥(30杀)/大招永动机/首座奖杯(MVP)/佛系粉刷匠/甩手掌柜/花式阵亡图鉴(生涯阵亡100) 等 13 条，`AchievementCatalog.TotalCount` 改为 `All.Length` 动态。②成就页签从一行一行列表改为分类分节+方块网格（桌面3列/移动2列）：已解锁=分类色实底大色块+硬阴影（击杀红/涂地绿/生存蓝/大招黄/彩蛋墨底/生涯荣誉金 `ComicStyle.Gold` 新增）+反色图标盒与状态签，未解锁=纸灰+描边淡+底部进度条；`HomeInfoOverlay.CreateText` 加 `wrapping` 默认参数（描述两行内换行）。陷阱：①`SettleMatch(in ctx, mvpAwards)` 的 MVP 有两条入口（`ctx.MvpAwards` 新/参数旧），只填其一会「生涯累计了、成就没解锁」——服务内取大者统一；②EditMode 测试 SetUp 会 `PlayerProfile.Reset()` 清掉开发档案成就串，截图前要重写 PlayerPrefs 再 `InvalidateCache`；③跑测试前先 `AssetDatabase.Refresh()`，否则改完代码首跑仍是旧程序集结果。文件：`Scripts/Core/{Achievements,MatchStatsService,PlayerProfile}.cs`、`Scripts/UI/{HomeInfoOverlay,MatchFlowUI}.cs`、`Scripts/UI/Core/ComicStyle.cs`、`Tests/EditMode/{AchievementEvaluatorTests,PlayerProfileTests}.cs`。

## 2026-11-08 #02
【墨水风暴云（Ink Tempest）整体重做对标 INKWAVE】数值面（6.5s/3.4m/34DPS/1.1m/s）本就对齐不动，表现与判定全面补齐：新增 `Player/StormPoseSolver.cs` 三段投掷姿势（引臂→鞭甩→随挥，0.55s 窗口长于 0.35s 动作锁，`CharacterVisualController` 复用 slam 俯仰/侧倾/武器通道接管）+ 云体重排为 15-puff 双色调积云（深墨腹/浅云顶、castShadow 云影即落点提示、顶部 puff 周期雷光闪烁）+ 双层雨（密雨滴 80/s + 细长亮雨丝 25/s）+ 云底地面圈（出手即落点预警、随云漂移/消散淡出）+ 小地图风暴盘升级为队色盘+寿命倒计时弧（`MinimapView`，走新 `TryGetActiveClouds`，旧 `TryGetActiveCloudPosition` 保留）+ 落地雷声（`tools/bake_sfx.py` 新增 storm_thunder 配方 ×2 变体，AudioManager.PlayStormThunder，已跑菜单 11 入资源库）+ 雨声循环音量随云消散淡出（`AudioManager.SetLoopVolume` 乘法器，StopLoop 复位）+ 判定：`ApplyStormTick` 加向后兼容重载做胸口→云腹 LOS 掩体检查（集装箱顶下躲雨有效、雨穿人群不穿屋顶）并跳过无敌目标、涂地半径改恒定不再随 grow/fade 缩放（与伤害圈一致）。顺手修：`RebuildStrategy` 未刷 `_activeSpecialKind` 导致蓄力狙/爆破/加特林开局 HUD 显示 Slam 图标；Bot 放 Storm 永远朝正前方 9m（改为朝最近敌人地面点）。陷阱：①`Physics.LinecastNonAlloc` 不存在（Unity 从未提供），改用 `RaycastNonAlloc` 沿「胸口→云腹」线段方向查询（起点在受害者胶囊内，queriesHitBackfaces=false 天然不命中自身）；②`VfxPool` 墨滴池只预热 24 颗、雨滴命中还要留 6 颗 reserve，风暴双层雨 ~47 颗并发会被饿成稀稀拉拉——池扩到 72；③共享编辑器里 script.eval 往返延迟 1.5~2s，轮询 `ActiveStormCount` 会误判云「秒消失」，取证须用 `#if UNITY_EDITOR||DEVELOPMENT_BUILD` 的 `[Storm]` Debug.Log 读回（lifespan 实测 6.5s 正常）。文件：`Scripts/Weapons/{InkStormCloudPool,SpecialCombat,WeaponConfigSO,WeaponController}.cs`、`Scripts/Player/{StormPoseSolver,CharacterAnimConfigSO,CharacterVisualController}.cs`、`Scripts/Core/{VfxPool,AudioManager}.cs`、`Scripts/UI/MinimapView.cs`、`Scripts/AI/BotController.cs`、`tools/bake_sfx.py`、`Tests/EditMode/{StormPoseSolverTests,SpecialSystemsTests}.cs`、`Tests/PlayMode/SpecialWeaponsPlayTests.cs`。

## 2026-11-08 #01
【战绩板块重设计 + 成就系统（参考 Godot/t-1 幽默文案风）】结算页战报卡升级为四列（+最高连杀）+ 回接已退场的 `MatchAwards` 勋章条（中文化幽默描述，金银铜 chips）+「本局成就」解锁卡（`ResultAchievementsView`，最多 3 张+溢出提示）；新增 24 条成就（单局 14 + 生涯 10，`Core/Achievements.cs` 纯静态目录+判定器）与生涯统计落档（`PlayerProfile` 新键：总击杀/大招/单局最佳/已解锁集合，旧档缺键默认 0 天然兼容）；首页战绩浮层加「成就」页签（页签胶囊互换、滚动列表、分类分节+进度条）。陷阱：①成就判定吃的 3 个新数据源要自己搭——`CombatantMatchStats` 加 `MaxStreak`/`SpecialCasts` 字段（`CombatantHealth.Die` 本地击杀分支喂连杀峰值、`WeaponController.TryActivateSpecial` 成功分支喂大招数）+ `MatchStatsService` 记全场首杀（环境死亡不算）；②新增的每局结算状态必须在 `Ready` 与 `Countdown` 两个分支都复位，只清一处第二局成就条就不播（s_SettledThisMatch 同款坑）；③新增 58 个上屏汉字，已跑 `tools/font_charset.py` + 菜单 `P1/画风/漫画风/0` 重烘图集。文件：`Scripts/Core/{Achievements,MatchStatsService,PlayerProfile}.cs`、`Scripts/UI/{MatchFlowUI,ResultAchievementsView,HomeInfoOverlay,HomeInfoContent}.cs`、`Scripts/UI/Core/MatchAwards.cs`、`Scripts/Player/CombatantHealth.cs`、`Scripts/Weapons/WeaponController.cs`、`Tests/EditMode/{AchievementEvaluatorTests,PlayerProfileTests,HomeInfoContentTests}.cs`。

## 2026-11-07 #26
【Fixed: 滚筒挥击贴脸打不中敌人】弹丸命中走 `Physics.SphereCast`，它不报告「球体起始时已相交」的碰撞体——挥击出射点（身前 0.55m/高 1.15m/命中半径 0.85m）贴脸时已嵌进敌人胶囊（半径 0.45m），球扫整段静默失败，墨滴飞过头顶、隔一段距离才命中；INKWAVE 用「线段-胶囊距离」纯数学判定（重叠即命中，weapons.js `_step`）故无此盲区。修复：球扫未找到角色时用只含角色层的 `OverlapSphere` 补判已重叠敌人（取碰撞体最近点为命中点，更近的世界命中不覆盖），爆破枪等所有弹丸的同类贴脸盲区一并修复。文件：`Scripts/Weapons/InkProjectilePool.cs`。

## 2026-11-07 #25
【按键布局编辑器：大小/透明度滑条重做为细轨道 + 圆钮】旧版 110×44 通体胶囊轨道配 20×44 竖长条把手，观感笨重；现拖动热区仍为 110×64（透明 Image 吃射线），可见部分改为 26 高 PaperAlt 胶囊轨道（3px 墨框）+ 黄色胶囊填充 + 26 正圆纸白把手（3px 墨边），与设置页音量条同一语言。陷阱：圆钮圆心必须在 [半径, 宽-半径] 内移动，旧公式 `norm*width` 会让把手半截越出轨道两端。文件：`Scripts/UI/MobileLayoutCustomizer.cs`。

## 2026-11-07 #24
【准星 P40：7 把武器各自轮廓 + 整体克制化】5 把弹丸武器（小枪/双枪/泼桶/爆破/加特林）此前共用一个轮廓，现按身份各配外壳（双枪双小环+架枪菱形锁定框、泼桶拱形+落点斗、爆破内外双环、加特林 8 段蓄力环），共享中心点与散布刻度；主环 60→44 且不再随散布缩放（同一信息只画一遍）、满蓄 PingPong 循环脉冲改 0.35s 一次性闪光（几何风禁止循环脉冲）、移动端整体 ×1.28 与字号同源、滚筒「像进度条的胶囊」改括号+落点弧（宽度随 rollWidth）、命中标记收细并收进主环内（旧版 44px 冲击环+长条糊成菱形靶心）。陷阱：`OutlinedBar` 长轴沿本地 +Y，绕 Z 转 θ 后方向是 (-sinθ,cosθ)，手算角度拼拱形会转成放射线——新增 `CreateSegment(from,to)` 按端点连段（`WeaponReticleUI`）。文件：`Scripts/UI/{WeaponReticleUI,ReticleStateMath}.cs`、`Tests/EditMode/{ReticleStateMathTests,ReticleAndTrajectoryTests}.cs`。

## 2026-11-07 #23
【全站圆角加大一档】`ComicStyle` 圆角三 token 调大（Ratio 0.18→0.30、上限 20→32、下限 8→12），面板/卡片/按钮观感整体更圆；陷阱：MatchCalloutView 左侧竖条内缩量原按全局上限 `CornerPanel×2+14` 现编，上限提到 32 后会超过 80 高紧凑横幅、把竖条压成负高度——Fixed: 改为 ApplyLayout 里按 `CornerFor(横幅实际宽高)` 推导并随四档横幅尺寸刷新。文件：`Scripts/UI/Core/ComicStyle.cs`、`Scripts/UI/MatchCalloutView.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-07 #22
【手雷冷却徽标从屏幕正中挪到底部三卡正上方】原居中落位（准星下 68px）悬在瞄准视野正中；改为复用空墨 toast 的「按画布高度换算、随帧重算」落位（桌面 178 / 移动 156 参考像素，紧贴三卡顶边 +14），空墨 toast 相应上移到 218 避让徽标带（两者同显不叠压）。文件：`Scripts/UI/WeaponReticleUI.cs`。

## 2026-11-07 #21
【Fixed: 设置页每行底部的 2px 分隔线两端探出圆角】设置行改成圆角卡片后，行内 `Separator` 仍按整行宽拉伸，线端从左右圆角外探出成两条「小尾巴」（用户截图箭头所指的线条溢出）；内缩量改为 `ComicStyle.CornerFor` 的行圆角半径（左右各缩一个半径），线端正好落在圆角直线段端点。文件：`Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-07 #20
【双枪翻滚手感重做（对标 INKWAVE）】旧实现只是「匀速滑行+侧倾」，观感是平移冲刺；现改为快出缓停速度曲线（`burst×dist/dur×(1-u²)`，积分恰为翻滚距离，翻滚期 Motor 绕过 `ComputePlanarVelocity` 整形防加速限幅吃掉爆发）+ KidRoot 绕 `up×滚向` 轴整圈前滚翻（pivot 补偿绕髋轴防穿地 + 中段跳弧）+ 蜷身团抱（步态 keep 抑制/收腿/脊柱前卷/squash 压缩，直接衔接架枪）+ `dodge_roll` 音效（玩家/Bot 共用）。转体/蜷身经 `DodgeRollPoseSolver` 纯函数求解（仿 SlamPoseSolver 房规），角度量级入 `CharacterAnimConfig.asset`，burst 倍率入 `WeaponConfigSO.dualiesRollBurstSpeedMultiplier`。陷阱：slam 每帧无条件重写 `KidRoot.localRotation`，翻滚旋转必须在其后绝对覆盖；KidRoot 位移与 `ApplyVisualGroundOffset` 每帧重写协同走增量记账字段。文件：`Scripts/Player/{InkCharacterMotor,CharacterVisualController,DodgeRollPoseSolver,WeaponWieldSolver}.cs`、`Scripts/Weapons/{WeaponConfigSO,WeaponController}.cs`、`Scripts/Core/{AudioManager,AudioMixConfigSO}.cs`、`Scripts/Editor/ProjectScaffolder.cs`、`tools/bake_sfx.py`、`Tests/EditMode/DodgeRollPoseSolverTests.cs`。

## 2026-11-07 #19
【开战/结束横幅缩小并缩短】「开战!」720×300/1.35s → 520×210/0.9s，「结束!」760×300/2.4s → 560×220/1.8s，主标题字号 140→104（TMP 自动缩字兜底，场景无需重建，每次 Show 重写 sizeDelta）。文件：Scripts/UI/MatchBannerView.cs。顺带：并行改动给 `WeaponConfigSO` Tooltip 引入 `蜷卷恰` 三字导致 `FontCoverageTests` 判红，按提示重跑 `tools/font_charset.py` + 菜单 `P1/画风/漫画风/0` 修复。

## 2026-11-07 #18
【Fixed: 拖尾偶发断层——撞击帧吞滴墨】弹丸主循环撞击判定在位置推进之前短路 return，撞击帧内越过的滴墨阈值被整体跳过：加载顿帧让一帧从 ~2.6m 直接跨到撞击点时，滴 2 的补间墨道整段缺失，拖尾在滴 1 圆斑边缘与终点爆斑边缘之间出现 ~0.4m 断层（弹道 PlayMode 偶发翻红，采样图定位）。修复：撞击分支先 `FlushTrailDripsOnImpact` 按同套插值补画本步越过的阈值（`InkProjectilePool.cs`）。顺带把弹道拖尾测试改为显式装零散布小枪——PlayerPrefs 换枪（试玩选了蓄力狙）会把蓄力狙带进测试污染断言。

## 2026-11-07 #17
【Fixed: 加特林蓄力/扫射移速只有设计值六成——比率乘错基数】策略层把 `splatlingMoveSpeed/6.0` 当归一化交给电机，但电机开火分支的基数是 `firingMoveSpeed` 3.8 而非跑速 6.0，实际蓄力 1.52 / 扫射 2.15 m/s。改为 `MainWeaponStrategy.FiringMoveSpeedOverride`（**绝对移速**，0=旧全局兜底），7 把武器开火移速对齐 INKWAVE 逐武器 moveSpeed（小枪4.6/双枪5.0/爆破4.0/泼桶4.2 前摇×0.7/滚筒鼓速4.4/蓄力狙4.2→1.8/加特林4.5→2.4→3.4）；`WeaponConfigSO` 新增 `firingMoveSpeed`/`chargeMoveSpeedStart`/`splatlingMoveSpeedChargeStart`（菜单 6 重生成，契约测试补断言）。文件：Scripts/Weapons/{MainWeaponStrategy,ChargerWeaponStrategy,SplatlingWeaponStrategy,SlosherWeaponStrategy,WeaponController,WeaponConfigSO}.cs、Scripts/Player/InkCharacterMotor.cs。

## 2026-11-07 #16
【Fixed: 滚筒推地仍读作悬空——第三处根因在涂地位置而非高度】贴地闭环实测 gap 恒 -0.026（静止/移动一致，无物理抬高），真正的根因是墨带止于脚下（`RollSweep.From→To`）：滚筒在身前 0.6m 处滚过未涂色地面，墨永远出现在滚筒后方，俯视角读作"悬在干净地面上方"、一动"越滚越悬"。对齐 INKWAVE（涂点=玩家前方 0.75m=滚筒正下）：`RollSweep` 新增 `PaintLead`（缺省 0=旧行为，向后兼容）+ `WeaponConfigSO.rollPaintLeadMeters=0.7`，墨带远端前伸到滚筒触地处（实测带缘+1.2m > 鼓前缘+0.73m）；碾压判定与墙面涂墨锚点不变（墙面探针若随前伸会罩空贴墙接触格）。文件：Scripts/Weapons/{WeaponShot,WeaponRollSystem,WeaponConfigSO,RollerWeaponStrategy}.cs、ScriptableObjects/Weapons/Weapon_SwellRoller.asset、Tests/EditMode/WeaponStrategyTests.cs。

## 2026-11-07 #15
【Fixed: 爆破枪在直击目标前的地面/坡面提前爆裂】根因：弹丸池对爆破弹的几何体检测用 0.26m 球扫（本意是避免蹭到墙前地面），下压弹道上球体下沿会提前「半径 / tan(俯角)」米贴地，爆点停在目标前半米级、直击判定也收不到角色。改为两段球扫：角色用 `blasterHitRadius=0.38` 大球（直击 125 才好接），几何体用 `BlasterWorldHitRadius=0.02` 细射线，两段取更近者；非爆破弹丸走单段 `ImpactFilter.Any`，与原实现逐字一致（改两段时漏掉 `actorsOnly=false` 分支会把地面命中整个过滤掉，PlayMode 立刻判红）。同批：爆破枪耗墨 10.4→9（对齐 INKWAVE）。文件：Scripts/Weapons/{InkProjectilePool,BlasterWeaponStrategy,WeaponConfigSO}.cs、ScriptableObjects/{WeaponConfig,Weapons/Weapon_PopperBlaster}.asset、Tests/PlayMode/InkBallisticsPlayboxTests.cs（新增坡前爆裂回归；`LookingUp` 改为显式装备连发枪，不再依赖上次选中的武器）。

## 2026-11-07 #14
【Fixed: 滚筒按住推地悬空——两处叠加根因】① `CharacterVisualController` 的 `_weaponMap`/`_armHoldPoses` 是非序列化缓存，运行中域重载（并行改码触发脚本重编译）后清空且 Awake 不再跑，贴地闭环与姿态写入取不到模型整段早退，滚筒冻结在装配姿态悬浮（实测 gap 0.06~0.22m、contact=0、weaponMap=0）→ OnEnable/LateUpdate 按需重建自愈 + 贴地路径回退序列化引用；② 高度保险把目标 gap 钉在 0，等于每帧把俯仰解算压入的 4mm 又抬回去，且 4mm 切线接触俯视下仍读作悬浮 → 压入量 0.004→0.026（≈1/8 鼓径，PlayMode 下陷下限 -0.03 内）并让保险钉在压入后高度。文件：Scripts/Player/{CharacterVisualController,RollerWieldSolver,CharacterAnimConfigSO}.cs、ScriptableObjects/CharacterAnimConfig.asset、Tests/EditMode/CharacterVisualTests.cs。

## 2026-11-07 #13
【选枪卡片属性数字行下线】卡片上的「伤X 距X 频X 耗X%」玩家不读，整行删除，只留 伤害/涂地/机动 三条对比横条 + 大招行（口径仍是 `WeaponSelectMath.ComputeStatBars`）；卡片底部布局从「大招→横条→数字行」三段改两段自底向上。陷阱：`WeaponSelectMath.Format*` 四个格式化方法被阵亡整备（RespawnLoadoutMath）与 EditMode 测试共用，只删 UI 调用不删方法。文件：Scripts/UI/WeaponSelect/WeaponCardView.cs。

## 2026-11-07 #12
【结算页 UI 改为开场一次到位】旧顺序是「按钮 0s 就在 → 双读数板 + 1.4s 掷点 → 判定牌/战报卡最后一帧同弹」，读成自上而下倒着长；现判定牌/双板/战报卡在 Result 入场同批 pop-in（0.05s/张），掷点只负责点亮「胜」签与败方转灰。陷阱：判定牌内容（胜利/失败 + 语义色）必须入场前写好，否则输局会先亮 1.4s「胜利」；`UpdatePostRollVisuals` 只保留首帧直进 Result 的兜底激活。文件：Scripts/UI/MatchFlowUI.cs。

## 2026-11-07 #11
【开局 VS 阵容卡整层下线】开战演出两侧「昵称 + 武器」队伍卡与 VS 徽章对单机局没有信息量（击杀播报/结算已有昵称渠道），整层删除 `MatchIntroLineupOverlay.cs`（运行时自装配、零外部引用，`RuntimeInitializeOnLoadMethod` 随文件消失即不再建层，无场景/预制残留要清）；`MatchBannerView` 的「开战!」横幅保留。陷阱：文件同时带着当日 #09 圆角化批次的未提交改动一并删除，要找回走 git；`BotLoadoutService` 昵称池与结算/播报共用不受影响，仅两处注释去掉「VS 阵容卡」字样。文件：Scripts/UI/Menus/MatchIntroLineupOverlay.cs（已删除）、Scripts/AI/BotLoadoutService.cs（注释）。

## 2026-11-07 #10
【Fixed: 桌面端阵亡整备条无法点选——阵亡期间鼠标仍被锁死】根因：光标门禁 `ApplyCursorState(GameplayInputEnabled && InputAllowed)` 只看对局阶段，Playing 中阵亡时门禁恒真，光标整局 Locked+隐藏，整备条 7 槽 Button 挂了 GraphicRaycaster 也点不到（移动端无光标概念不受影响）；改为 `CursorShouldLock` 追加 `Health.IsAlive` 条件并同步 `OnApplicationFocus`/`Update` 两处调用点，阵亡即释放光标、复活下一帧自动重锁。陷阱：暂停菜单靠 `GameplayInputEnabled=false` 释放光标，而死亡没有任何开关翻转，属同类门禁的漏网分支；改后 `ApplyCursorState` 的旧 `gameplayActive` 形参语义不变（输入抑制逻辑仍用原门禁，只动光标）。文件：Scripts/Player/PlayerController.cs。

## 2026-11-07 #09
【UI 全站圆角化：直角 → 圆角矩形 / 胶囊 / 正圆】形状基元 `UIInkShapeGraphic` 把 `RoundedRect` 半径 0 语义改为「构建期按短边自适应（`ComicStyle.CornerFor` = clamp(短边×0.18, 8, 20)）」，`ApplyComic` 不再把 RoundedRect 降级成 SharpRect，硬影复用同一 effectiveRadius 自动跟随圆角；25 个 UI 文件约 120 处 `SharpRect+CornerSharp` 按语义换成 RoundedRect（面板/按钮/卡片）或 Pill（小徽章改正圆、条签/加载块/进度条改胶囊），顶部身份色带全部内缩进圆角直线段改胶囊短划（直角色带会戳出圆角轮廓）；条形轨道/填充换 `UIPillImage`（Image 子类，按纵横比自动取 `UIFlatSprite.Pill` 胶囊精灵，fillAmount 逻辑不变）。陷阱：胶囊条的本质约束是「圆头半径恒等于高度一半」，静态拉伸的方图做不到——必须按纵横比生成精灵或用 mesh；`CornerRadius=0` 现在意味着自适应而非直角，旧调用方显式写 0 反而安全。文件：`Scripts/UI/Core/{ComicStyle,UIInkShapeGraphic,UIFlatSprite,UIPillImage,UIThemeSO}.cs`、`Scripts/Editor/ComicStyleInstaller.cs`、`Scripts/UI/*`（HUD/菜单/选枪/移动端 25 文件）、`Docs/UI-GeometricBold.md`（圆角语言规范）。

## 2026-11-07 #08
【手雷冷却徽标去掉「手雷就绪」弹出提示】冷却一结束徽标立即收起，只保留冷却中的倒计时 + 充能条（旧的整条翻黄「手雷就绪 · E」提示 1.8s 被玩家视为冗余打扰）；`WeaponReticleUI.UpdateSubAim` 删掉 `_bombReadyFlashTimer`/`_bombWasCooling` 与 `SetBombBadgeReadyState` 两态切换，`BuildSubAimBadge` 不再建 `ReadyText` 节点。陷阱：字段 `_subAimBg`/`_subAimIcon`/`_subAimLabelText` 原先只在就绪态被读取，删掉调用方后留着会触发 CS0414「assigned but never used」，改回构建期局部变量；EditMode `SubAimBadge_TextFitsItsBoxes` 原本断言 `ReadyText` 存在，同步改为断言其不存在（否则删节点立刻判红）。文件：Scripts/UI/WeaponReticleUI.cs、Tests/EditMode/ReticleAndTrajectoryTests.cs。

## 2026-11-07 #07
【击杀接进主循环 + 选枪三条对比横条】击杀敌方 → 击杀者 +70 能量（≈1/3 大招）+ 受害者损失能量全额转移（旧版白扣）+ 回墨 25；玩家/Bot 一视同仁。陷阱：转移量必须在 `OnKilled`（内部减半）**之前**捕获，否则少一半——受害者减半比例与转移共用 `GameConfigSO.killRewardEnergyTransferFraction` 单一口径（`KillRewardMath` 纯函数 + `KillRewardService` 静态服务，`CombatantHealth.Die` 挂接，环境死亡无攻击者不奖励）；全链路用例放 PlayMode——EditMode 下 `AddComponent` 不跑 `Awake`，`_health` 为 null 会 NRE。选枪卡把无解释的 7 段总评块换成三条 0~1 横条（伤害/涂地/机动，口径 `WeaponSelectMath.ComputeStatBars`：DPS 430 锚 / 每墨平方米 3.1 锚 / 射程 6~27m + 机动定档表），null 配置分支按各武器规范值兜底（漏了加特林 28/0.066 会把伤害王判给小枪）；卡高 164/236→200/288（桌面 4 行 870 ≤ 视口 922 仍免滚动）。文件：`Scripts/Core/{KillRewardMath,KillRewardService,GameConfigSO}.cs`、`Scripts/Player/{CombatantHealth,InkCharacterMotor}.cs`、`Scripts/Weapons/WeaponController.cs`、`Scripts/UI/WeaponSelect/{WeaponSelectMath,WeaponCardView}.cs`、`Scripts/UI/MenuLayoutMath.cs`、`Tests/{EditMode/KillRewardTests,PlayMode/KillRewardPlayTests,EditMode/WeaponSelectTests}.cs`。

## 2026-11-07 #06
【首页左下角签「游戏共创 · 单机 4V4」下线】这颗角签只读、不可点、不承载信息（模式/时长/难度都在 Hero 卡里），却在一屏最显眼的位置占掉 320×36 黑底条，还会让人误以为能点；现在 `MenuLayoutMath.Home()` 的 `FooterTag` 槽两平台都置空（桌面原 320×36、移动端本来就是空盒），`MatchFlowUI.BuildHomeCornerTags` 不再建该节点，右下「检查更新」小标签不动。陷阱：重建清理分支里的 `childName == "HomeFooterTag"` **必须保留**——旧场景 / 旧运行时缓存里可能还留着这颗节点，不清掉会变成挂在安全区上的孤儿；`FooterTagFont` 也不能删，右下版本 / 检查更新角签还在用它。文件：Scripts/UI/{MatchFlowUI,MenuLayoutMath}.cs、Tests/{EditMode/MenuLayoutMathTests,PlayMode/MatchFlowTests}.cs、Docs/UI-GeometricBold.md。

## 2026-11-07 #05
【阵亡整备：复活前换枪】本地玩家阵亡时底部弹出整备条（7 槽武器卡 + 复活倒计时 + 「换枪清空大招充能」代价提示），点选立刻写 PlayerPrefs、**复活那一帧**才真正 `EquipWeapon(config, resetSpecialCharge: true)`（`RespawnLoadoutMath` 纯函数门禁 + `RespawnLoadoutView` 独立 Overlay Canvas sortingOrder 24 + `WeaponSelectBootstrap` 轮询编排）。陷阱：① `EquipWeapon(config)` 旧的单参语义必须保持「不清充能」（Ready 阶段每次预览都调它，默认清空会让玩家在战前反复掉大），故新增可选参数重载而不是改默认值；② 编排走 `Update` 轮询 `PlayerController.LocalPlayer.Health` 而不是订阅 `OnKilled/OnRespawned`——LocalPlayer 随场景重建换实例，订阅旧身体会跨场景泄漏且拿不到新实例；③ 离开 Playing 必须同时收起整备条并作废挂起选择，否则上一局死亡时点的枪会跨局带进新局；④ 首次弹出以「死亡瞬间手里的枪」为初始选中项，不能用存档（玩家可能是用上一局存档枪进场）。文件：Scripts/Weapons/WeaponController.cs、Scripts/UI/WeaponSelect/{RespawnLoadoutMath,RespawnLoadoutView,WeaponSelectBootstrap}.cs、Tests/{EditMode/RespawnLoadoutMathTests,PlayMode/RespawnLoadoutPlayTests}.cs。

## 2026-11-07 #04
【储液罐墨量可视化与液面动画对标 INKWAVE】液面换程序化弯月面旋转体网格（tankGlass fill 移植：实尺烘焙/底部锚定，运行时只写 `scale=(挤压,液位,挤压)` 免位置补偿）+ 25/50/75% 侧刻度块（并入背包静态合批零 DC）+ 副武器墨量线（白环标 BombInkCost 高度，轮询重定位、无武器隐藏）+ 罐顶压力表装饰 + 三弹簧液面（显示墨量先 ExpDamp rate8 → bob 弹簧吃跳跃/落地/急停/扣墨冲量 → 局部加速度驱动前后/左右晃 ±0.06rad + 径向挤压；参数全进 CharacterAnimConfigSO 新「储液罐」组）。陷阱：① 液面禁碰材质——CombatantHealth 白闪占 MPB 且任何非空 MPB 踢出 GPU instancing，反馈全走 Transform；② 扣墨冲量 gain 取 100（bob 峰值≈7% 液位肉眼可辨），照搬 Inkwave 事件冲量 2.0 的量级换算只有 ~1% 读不出来；③ 场景烘焙角色须跑 `P1/角色与武器/升级场景中所有角色模型` 才有新罐件（PlayMode 测试按 Scaffolder 管线运行时重装配绕开）；④ 新增上屏/注释字照例跑 font_charset.py + 菜单 `P1/画风/漫画风/0`。反射实测：急刹 rot.x=+3.4°、左转 rot.z=−3.4°（方向与物理一致）、扣墨 20% 下沉至 78.8% 后回稳 80.0%、空罐自动隐藏。文件：Player/{CharacterVisualAssembler,CharacterVisualController,CharacterAnimConfigSO,TankFillMath}.cs、Tests/{EditMode/TankFillTests,EditMode/CharacterVisualTests,PlayMode/CharacterVisualPlayTests}.cs、tools/font_charset.txt。

## 2026-11-07 #03
【Bot 局内差异化对标 INKWAVE】每局 Countdown 统一抽卡（`BotLoadoutService`，仿 WeaponSelectBootstrap 自建主机）：同队武器类型互不重复、队友池排除玩家武器（`BotLoadoutMath`，照 match.js pickTeam）+ 每 Bot 风格个性倍率（`BotPersonalityMath`，叠在难度克隆之上）+ 20 昵称洗牌（击杀播报/VS 阵容卡 `MatchIntroLineupOverlay` 共用）。复活不换枪；`WeaponController.EquipWeapon` 是通用入口，BotTactics 按 WeaponKind 自适应战术。陷阱：① 普通难度 `BotDifficultyMath.CreateRuntimeConfig` 返回**共享资产本身**，个性乘算前必须先 `Instantiate` 克隆，否则污染全部 Bot 与后续对局；② 项目在 `P1.Environment` 命名空间下，裸写 `Environment.TickCount` 会被解析成 P1.Environment（CS0234）；③ 自动抽卡设门槛（在场 Bot ≥ `MinBotsForAutoDraft`=4），合成小测试场不被越界换枪；旧用例 `BotWeaponRemainsUntouched_WhenPlayerWeaponChanges` 语义改为「Playing 中玩家换枪不影响 Bot」。新昵称汉字需跑 `tools/font_charset.py` 更新账本 + 菜单 `P1/画风/漫画风/0` 重烘（FontCoverageTests 判红过一轮）；`BotAiPlayboxTests` 撤退用例改为先把受害者回满血（抽卡后开局交火会先打掉一截，70 伤害便直接击杀）。`Fixed:` 合成小场用例「一个 GameObject 两个 CombatantHealth」——`new GameObject(name, typeof(InkCharacterMotor), typeof(WeaponController), typeof(CombatantHealth))` 一次性传组件列表时，`InkCharacterMotor` 的 `[RequireComponent(CombatantHealth)]` 先自动补一个、显式那个又加一个，两次 OnEnable 各注册一次，Bot 计数凭空翻倍（靠 `GetEntityId` 比对 go/rid 才定位）；只传前置组件、让 RequireComponent 自己补即可。文件：Scripts/AI/Bot{LoadoutMath,LoadoutService,PersonalityMath}.cs、BotController.cs(AssignMatchLoadout/DisplayName)、Scripts/UI/{KillFeedEvent.cs,Menus/MatchIntroLineupOverlay.cs}、Tests/{EditMode/Bot{Loadout,PersonalityMath}Tests,PlayMode/BotLoadoutPlayTests}.cs。

## 2026-11-07 #02
【涂地/喷射观感对标 INKWAVE】墨斑方向性拉伸端到端：`PaintCircle` 尾部可选 `directionXZ/stretch01` → Splat.Stretch → 图集 UV2 → SDF 各向异性（stretch=0 与旧观感逐字节一致，玩法网格仍正圆）；图集 G 通道 8bit 量化不动，渐干改为着色读数「边缘先干」(inkEdgeDryBias)；墙面补齐软边/色调微差/垂流/非均匀湿润（新 InkWallStyle.hlsl，卫星盘只写视觉层 alpha≤0.46 不写 _owners，攀爬判定零扩大）；潜墨尾迹 V 形水花（CharacterSwimWake 自建粒子，不占 VfxPool）。`Fixed:` maxSplatsPerFrame 死字段接入（容量构造时定死）、命中反馈被 0.05s 节流+40m 门控吞掉（放宽 60m+最小墨花）、命中加法白盘过曝（×0.55）。陷阱：新 shader 不放 `Resources/` 出包会被剔除，故弹丸菲涅尔复用 Resources 下 `P1/InkVfx` 而非新建；飞溅水珠落地补涂必须走 `PaintSpeckVisualOnly`（不进覆盖率），防装饰污染充能/统计。新 VFX 全池化零 GC（墨雾≤4/帧、speck≤3/帧、枪口点光仅桌面本地玩家 1 盏）。文件：Ink/Ink{SplatAtlas,SurfaceController,WallFace,WallSurface,GridConfigSO}.cs、Shaders/{InkGround,InkWall}.shader+InkSurfaceOverlay.hlsl、Weapons/{InkProjectilePool,WeaponController,WeaponFeelProfile}.cs、Player/{InkCharacterMotor,CharacterSwimWake,PlayerConfigSO,CombatantHealth}.cs、Core/VfxPool.cs。

## 2026-11-07 #01
【角色观感 P39】实机审角色后四项调整：前臂/短袖/拳头加粗一档（旧 0.064 与灯笼裤量级脱节，读成火柴棍）、嘴稍放大、头发弹簧摆幅钳制收紧（欠阻尼+鞭梢增益在急停节点"美杜莎"炸开）、墨罐可读性重做。`Fixed:` 墨罐液面满罐也读成"奶粉罐"是两层根因：白 rim(0.35) 铺满细圆柱整个侧面 + 透明 GlassChamber 叠加后比任何单一组分都亮（alpha 0.25/0.45 均无效）→ 删玻璃筒直接露液面（空罐=深色背板），TeamLiquid rim→0.08、emission→0.15。陷阱①重踩：后台编辑器改 .cs 不自动 Refresh，必须先 `script.recompile` 再验证，否则测试/截图全跑旧 DLL（本轮前几次"改了没效果"皆因此）；重装配走 `P1/角色与武器/升级场景中所有角色模型`（保存当前活动场景 NeonSkatepark）。文件：CharacterVisualAssembler.cs、CharacterVisualController.cs(UpdateHairSway 钳制)。

## 2026-11-05 #15
【发布 v0.1.28】含 `#12`~`#14`；APK `moshuizuozhan-v0.1.28-arm64-v8a.apk`。陷阱：本轮 GUI 编辑器是用 `-logFile Logs/editor_ink.log` 启动的，而 `release.py build` 的 Pipeline 通道只盯 `Logs/Editor.log`（该文件停在 06:31），构建其实 54s 就成功了脚本却看不见完成标记、一路空等——编辑器日志名不是 `Logs/Editor.log` 时，构建成功后直接重跑一次 `build --version X` 复用已通过 aapt2 校验的 `Builds/Android/InkTurf.apk` 即可收尾。

## 2026-11-05 #14
【Fixed: 霓虹滑板场「看着能过、走过去卡住」的集装箱/塔吊基座夹缝】根因：基座在 `SkateparkGeometryBuilder` 里**硬编码 3.6m**（布局常量 `CraneBaseSize` 实为 3.2m），45° 斜置后南角与双层堆东北角只剩 **0.84m**——小于角色胶囊直径 0.9m，视觉像条缝、实际进不去；地面贴花与通行审计都按常量 3.2m 算，口径本身已漂移。现基座统一取 `ArenaLayoutSkatepark.CraneBaseSize`，`CranePos` 沿双层堆北面法线外移 0.4m（(17.4,16.2)→(17.54,16.58)），缝宽提到 **1.50m**（`Physics.OverlapCapsule` 实测胶囊可通，SW 镜像位对称同样通过），基座与 ExtraA 高层堆仍咬合、不新开第二条缝。涉及 `Scripts/Environment/{ArenaLayoutSkatepark,SkateparkGeometryBuilder}.cs`、`Scenes/NeonSkatepark.unity`、`Art/Textures/Arena/SkateparkGround_Albedo|RMO.png`（基座黄黑警示框随位置重烘）。

## 2026-11-05 #13
【Fixed: 霓虹滑板场下到碗池底部被直接传回出生点】根因：`OutOfBoundsGuard` 的坠落阈值 `GameConfig.outOfBoundsMinY` 仍是为全平地地图设的 `-1m`，而碗池底是 `-2.1m`（`ArenaLayoutSkatepark.BowlFloorY`），角色下到半坡就被当成「坠出世界」直接 Teleport 回出生锚点（不判死、不清血）。现将默认阈值降到 `-6m`（`GameConfigSO.DefaultOutOfBoundsMinY`，池底下方留 3.9m 余量），并补 EditMode 断言「碗池底不回收 / 真正掉出世界仍回收」；`ShouldRecover` / `IsOutOfBounds` 的 `minY` 默认参数也改指该常量（原来硬编码 `-1f`，新调用点会静默踩回同一个坑，旧口径用例改为显式传值）。涉及 `Scripts/Core/{GameConfigSO,OutOfBoundsGuard}.cs`、`ScriptableObjects/GameConfig.asset`、`Tests/EditMode/{OutOfBoundsMathTests,PlayerMobilityMathTests}.cs`。

## 2026-11-05 #12
【Fixed: 「最后 1 分钟」决战横幅同时弹两条】根因：1:00 有两套独立触发——`CenterTimerView`（计时球下方红卡）与 `MatchFlowUI` 调 `MatchBannerView.ShowOneMinute`（屏幕中央大横幅）；`PhaseA_Playbox` 的 MatchBannerView 是停用状态（`m_IsActive: 0`）所以只看到一条，海鸥港湾 / 霓虹滑板场两张地图里它是激活的，于是同帧弹两条。现删除 `MatchFlowUI` 的 1:00 触发（`_oneMinuteTriggered` 一并移除），1:00 只归 `CenterTimerView`；`MatchBannerView.ShowOneMinute` 保留给单测并注明已退役，另加源码断言防回接。涉及 `Scripts/UI/{MatchFlowUI,MatchBannerView}.cs`、`Tests/EditMode/UiMatchResetTests.cs`。

## 2026-11-05 #11
【发布 v0.1.27】含 `#01`~`#10`；APK `moshuizuozhan-v0.1.27-arm64-v8a.apk`。

## 2026-11-05 #10
【新地图「霓虹滑板场 / NEON SKATEPARK」+ 地图选择卡横向滚动】新图复用 `ArenaLayoutV2` 的 40×56 玩法常量，场内节奏重写：中央 22×14m 下沉碗池（3 圈 35° 环带 + 对角 8 级台阶，最深 2.1m）+ 24m 对角天桥（两端 6m 坡道），全部按原点 180° 点对称；入口菜单 `P1/关卡与地图/生成新地图：霓虹滑板场`。地图从 2 张加到 3 张后桌面会横向溢出，故 `MapSelectLayoutMath` 改为纯函数布局：`ResolvePanelWidth` 按「地图数 × 期望卡宽」长面板、`ResolveCardLayout` 算卡宽/是否需要滚动（卡宽夹到 `CardMinWidth` 就转横向滚动，宁可滚也不压没文字列），箭头/方向键/拖动/滚轮走 `StepScrollOffset`。涉及 `Scripts/Environment/{ArenaLayoutSkatepark,SkateparkGeometryBuilder,SkateparkDetailBuilder}.cs`、`Scripts/Editor/{ProjectScaffolder,SkateparkBackdropBuilder,SkateparkGroundTextureBaker}.cs`、`Scripts/UI/{MapCatalogService,MapSelectLayoutMath,MapSelectOverlay}.cs`、`Scripts/UI/Core/UIHorizontalScrollRect.cs`。

## 2026-11-05 #09
【Fixed: 霓虹滑板场重建在 `BuildGraffitiCluster` 抛 IndexOutOfRange 中断】根因：`Hash01` 最大值可达 1.0，`(int)(Hash01 * palette.Length)` 会取到 `Length` 越界；旧种子未触发，调低容器涂鸦密度后暴露。索引改为 `Mathf.Min(Length - 1, …)`。涉及 `Scripts/Environment/SkateparkDetailBuilder.cs`。

## 2026-11-05 #08
【霓虹滑板场整体优化：干碗池 + 出生台落地 + 假掩体清理】碗池被 240m 远景水面从 y=-1.35 灌满（池底 -2.1），俯视读作「蓝水池」；现水面在碗池开口挖 24×16m 洞并外扩 900m，池底压暗 + 贴壁 AO + 32 段环带；对角台阶改为按 35° 坡面「挖」出的 8 级踏步（0.2625m/级；旧版整组埋在环带里），补池底中央脊与对称方块；出生台后加 1.2m 岸台承托背景集装箱，南侧补低矮库仓/高杆灯/拖船，甲板背沿加不可见防越界；删除场内无碰撞的托盘/油桶/新泽西护栏/木箱/铁丝网，QP 改 4 段贴墙 bank，坡道/防滑条改橙色 accent，集装箱锈红压深。涉及 `Scripts/Environment/{ArenaLayoutSkatepark,SkateparkGeometryBuilder,SkateparkDetailBuilder}.cs`、`Scripts/Editor/{SkateparkBackdropBuilder,SkateparkGroundTextureBaker,ProjectScaffolder}.cs`、`Resources/MapPreview/neon_skatepark.png`。

## 2026-11-05 #07
【Fixed: 霓虹滑板场地面上残留「SKATE 涂鸦/油漆溅斑/墙根标签」】根因：贴图烘焙器把涂鸦、溅斑、墙根标签全画进了 2048 反照率，粗黑描边+橙色在俯视/远景里读作巨大箭头、橙色椭圆与白色光标点，与「油漆只能游玩时喷上去」的规则冲突。现删除 `BakePaintSplashes` / `BakeGraffiti` 等全部地面涂鸦生成，`ProjectScaffolder` 同步移除 `PaintSkateparkInk` 调用，碗池内衬回到中性浅灰；地面只保留混凝土/沥青/标线/警示斜纹/油污/箅子。涉及 `Scripts/Editor/{SkateparkGroundTextureBaker,ProjectScaffolder}.cs`。

## 2026-11-05 #06
【Fixed: `WeaponReticleUI_OutOfInk_ShowsWarningAndEmptyToast` 单条跑绿、全量跑红】根因是**测试顺序污染**而非空墨逻辑：该用例不加载场景，但准星 `LateUpdate` 受 `MatchManager.InputAllowed` 门控，前序加载场景的用例（`MobileBombTouchPlayTests` 等）会留下停在 `Ready` 的 `MatchManager` → `InputAllowed=false` → 准星整帧早退、`_isOutOfInk` 永不更新。现用例开头调 `EnsureMatchInputAllowed()` 把残留实例推进到 `Playing`（无实例时直接返回）。涉及 `Tests/PlayMode/ReticlePlayModeTests.cs`。

## 2026-11-05 #05
【Fixed: `FontCoverageTests.SourceLiterals_AreCovered_ByLedgers` 报「若抑」未收录】根因：`OcclusionFader` 新增迟滞参数的 `[Tooltip]` 是字符串字面量，扫描器会把它算上屏文案，而字集账本未同步。Tooltip 汉字在本项目一律进账本（该 Tooltip 其余 69 字均已在册），故跑 `python3 tools/font_charset.py` 后重跑菜单 `P1/画风/漫画风/0` 重烘正文图集（1330 字）。涉及 `tools/font_charset.txt`、`Art/Fonts/TMP/MiSans-*-SDF.asset`。

## 2026-11-05 #04
【Fixed: 海鸥港湾渡轮甲板涂地整片发暗/发黑、掩体边界线全丢】根因：掩体全部标记了 BatchingStatic，静态合批把顶点烘成世界坐标后 `positionOS` 就是世界坐标、`UNITY_MATRIX_M` 退化为单位阵，`(0.5 - abs(positionOS)) * 列长` 的盒体距离恒为负 → `edgeLine=1` 把整张表面涂成 `_EdgeLineColor`（橙墨被压成暗砖色，甲板读作"巨大黑墨块"）。
现由 `ArenaGeometryBuilder.CreateBox` 把每个盒子的世界 AABB 烘进 uv2/uv3/uv4（UV 通道合批后保留），`ObstacleDitherLit` 用 `positionWS` 与该 AABB 求"到最近棱边的米距离"，AABB 全零时跳过（防退化盒体整面涂黑）；场景已用菜单 `P1/关卡与地图/生成新地图：海鸥港湾` 重建。涉及 `Shaders/ObstacleDitherLit.shader`、`Scripts/Environment/ArenaGeometryBuilder.cs`、`Tests/EditMode/ObstacleEdgeLineRegressionTests.cs`。

## 2026-11-05 #03
【Fixed: 场地地面横向白色条带闪烁（z-fighting）】根因：`ArenaDecorationBuilder.BuildMarinaScenery` 的码头大平台顶板（47.6×63.6，顶面 y=0）与竞技场地面（40×56，顶面 y=0）**整片完全共面**，相机移动时两块面片交替抢占深度，在场内渲染出整片横向白色条带（不是掩体网点的逐帧闪变，`OcclusionFader` 那条是另一处独立问题）。现把顶板拆成**四条只覆盖场外 3.8m 的环带**（顶面仍保持 y=0 与场地齐平）；当前海鸥港湾场景中的 `Backdrop_Matte` 同步下沉 3cm 并已保存。涉及 `Scripts/Editor/ArenaDecorationBuilder.cs`。

## 2026-11-05 #02
【Fixed: 掩体边缘网点 dither 逐帧闪变】根因：`OcclusionFader._targetAlpha` 直接跟随球探针当帧命中结果，第三人称相机下探针在掩体边缘逐帧命中/未命中，目标透明度在 0.35↔1.0 间以帧率翻转，Screen-Door 网点随之闪变（60% 帧翻转）。现加 0.25s 遮挡保持窗口（迟滞）：最后一次命中后继续淡化，边缘抖动不再传到网点；仿真验证目标翻转 23 次→0 次、alpha 标准差 0.25→0.049。涉及 `Scripts/Environment/OcclusionFader.cs`。

## 2026-11-05 #01
【Fixed: 海鸥港湾旋转视角时甲板贴图闪烁 + 渡轮/水面配色精修】根因有三：① 渡轮甲板/船壳挂了 `OcclusionFader`，第三人称相机旋转时遮挡球探针擦到甲板，Bayer 4×4 屏幕空间 dither 网点在转动中动态跳变；② 甲板与坡道上挂了 2mm 浮动贴地盒（车道虚线/踏步/警示条），在斜视远视角下与底面产生 Z-Fighting 深度冲突；③ 木栈道法线图在板缝中心存在 ±0.85 的不连续阶跃跳变，镜头转动时光影走样。
现彻底移除渡轮上的 `OcclusionFader` 与全部浮动贴片，`TopDownOcclusionManager` 过滤脚底平台；木栈道法线改平滑连续凹槽斜率（槽心归零）；贴图开启 Trilinear 与 8× 各向异性过滤；渡轮与水面配色对齐 INKWAVE Halyard（象牙白客舱、海军蓝船身、蓝灰钢甲板、薄荷青浅水与柔和天光）。
涉及 `Scripts/Environment/{HarborGeometryBuilder,ArenaLayoutHarbor,TopDownOcclusionManager}.cs`、`Scripts/Editor/{ProjectScaffolder,HarborGroundTextureBaker,ToonStyleInstaller}.cs`、`Shaders/InkGround.shader`。

## 2026-11-04 #49
【Fixed: 地图切回海鸥港湾后，选枪抽屉在屏幕左缘残留一条白纸黑边竖条】根因：场景里烘焙的开发期开关 `MobileTouchUI.forceShowInEditor`（GullHarbor=1、货柜码头=0）让 `UITypography.IsMobileLike` 在桌面编辑器里为真，首页整屏按移动端度量排版；切图时平台观感在运行中翻转，而 `WeaponSelectView.SyncHomeLayout` 只重算字段——DrawerPanel 仍是最初构建的宽度（移动端 1574），收起位移却按桌面 1204 算，位移小于宽度 → 左缘露出约 330 宽的一截抽屉。
现删除该开关（需要预览触屏直接勾 `showTouchUiInEditor`），并让 `SyncHomeLayout` 在度量变化时整棵重建并保留选中武器/抽屉开合（重建后直接落到目标 X，不插值滑入）；顺带修 `ApplyPlatformMetrics` 首次调用不初始化度量会建出 0 宽抽屉（被既有 `WeaponSelectView_LoadoutCard_*` 断言抓住）。
涉及 `Scripts/UI/{MobileTouchUI,WeaponSelect/WeaponSelectView}.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-04 #48
【新地图「海鸥港湾 / GULL HARBOR」替换潮汐货站】删除 `KelpTerminal` 场景与 `ArenaLayoutKelp`/`KelpTerminalGeometryBuilder`/`ArenaStyle_KelpTerminal`/`Mat_KT_*`；新图复刻 INKWAVE `halyard`：中央渡轮（甲板 1.5m，只有南北两条登船坡道）+ 南北浅湾水线（可行走、无碰撞，用矩形禁区从计分剔除）+ 东西木栈道 + 船坞拖船，40×56 玩法常量仍复用 `ArenaLayoutV2`。画风档 `ArenaStyle_GullHarbor` + `Mat_GH_*` + `HarborGroundTextureBaker`（2048 木板/码头 albedo + 1024 N/RMO）+ 港湾远景（`ArenaStyleSO.backdropTheme`）。
两个坑：① 新建 `ObstacleDitherLit` 材质必须逐材质调 `ToonStyleInstaller.ApplyStyleToMaterial` **并挂 `_SurfaceRMO`**——RMO 为空时 AO 被压到 0.35，整块只剩三成亮度（暖木色×冷阴影读作「酒红塑料」）；② 整块盖板式强调色会把客舱/加油亭屋顶刷成深红，压顶改四边压条。
涉及 `Scripts/Environment/{ArenaLayoutHarbor,HarborGeometryBuilder}.cs`、`Scripts/Editor/{ProjectScaffolder,HarborGroundTextureBaker,ArenaDecorationBuilder,ToonStyleInstaller}.cs`、`Scripts/Ink/InkSurfaceController.cs`、`Scripts/UI/MapCatalogService.cs`。

## 2026-11-04 #47
【空墨按手雷键改为「按下即拒 + 空墨提示」，不再进入瞄准】原实现空墨也能按住 E 进入瞄准并显示变灰的弹道预览，松手才被拒；现在 `SubBombThrowArbiter.Advance` 新增 `hasInkForThrow`（默认 true 保持旧调用兼容），墨水 < `bombInkCost` 时按下沿返回 `DeniedByInk` 且不置 `IsAiming`，`PlayerController` 复用 `WeaponController.ReportEmptyFeedback()`（空打音效 + 准星红叉/震颤 + 「弹药耗尽」toast）并把拒绝原因记为 `NotEnoughInk`。
冷却中若墨水已回够（≥ 55）仍按旧行为进入瞄准（灰线 + 松手冷却反馈），墨水不足时空墨门槛优先；`SubBombDenyStamp`/按钮闪红语义不变。空墨被拒后按住不放不会因回墨自动补瞄准，必须重新按。涉及 `Scripts/Player/{SubBombThrowArbiter,PlayerController}.cs`、`Scripts/Weapons/WeaponController.cs`、`Tests/{EditMode/SubBombThrowArbiterTests,PlayMode/MobileBombTouchPlayTests}.cs`。

## 2026-11-04 #46
【墨水经济收紧：单发 +15% / 潜墨 34/s / 手雷恢复 55 墨】原数值几乎逐项照抄 INKWAVE（小枪 0.95@10 发/秒、潜墨 42/s、手雷 0 墨），满罐输出 10.5s : 补给 2.4s = 4.4:1，墨水几乎不构成资源；现小枪 1.1、双枪 1.0、加特林 0.7、泼桶 8.6、爆破 10.4、蓄力狙 21、滚筒 1.25/米、挥击 10.5，潜墨回墨 42→34/s、开枪后回墨延迟 0.25→0.5s → 满罐约 8.5~9.5s 输出 / 2.9s 回满（约 3:1）。手雷改 55 墨 + 6s 冷却双重限制，空墨投不出并补空打音效/准星戳记（旧版 0 墨的静默失败是刻意改的，恢复耗墨必须同时补反馈）。
数值落在 `PlayerConfig.asset` 与 7 份武器 `.asset`：重跑菜单 6/7 会整份重刷其它调参，日常改墨水走新菜单 `P1/构建/15. 同步墨水资源数值 (Ink Economy)`（只写耗墨/回墨字段，PlayMode 需同步改硬编码断言）。涉及 `Scripts/{Player/PlayerConfigSO,Player/InkCharacterMotor,Weapons/WeaponConfigSO,Weapons/WeaponController}.cs`、`Tests/{EditMode,PlayMode}/*Ink*|*Weapon*|*Bomb*|*Motor*`。

## 2026-11-04 #45
【Fixed: 场外码头/边界墙顶被涂成「一条一条」墨色】根因：场外 `Backdrop_Matte`（74x84m 码头基座）等几何同样走 PropLit/ObstacleDitherLit 的顶面墨迹采样，世界 XZ 落在图集外时被 `saturate` 钳到边界纹素，整片场外沿边界拉伸成墨色条纹。现由 `InkSurfaceController` 推送 `_GlobalInkPlayableRect`（40x56 计分场），`InkSurfaceOverlay.hlsl` / `InkGround.shader` 对场外片元直接判无墨（地面侧须在亚纹素修正后再清一次 `amtEff`，否则邻域采样会把边界覆盖率带回来）；副作用：边界墙顶不再受墨。
涉及 `Scripts/Ink/{InkSurfaceController,InkGridConfigSO}.cs`、`Shaders/{InkSurfaceOverlay.hlsl,InkGround.shader}`、`Tests/EditMode/InkMapBoundsPaintTests.cs`。

## 2026-11-04 #44
【去掉全部播报员语音，开赛/终场改用音效】删除 `Audio/Voice` 的 6 条语音与 `AudioManager` 语音通道，新增独立 `MatchCue` 通道（优先级 16，不会被战斗 SFX 抢占）：墨幕入场播 0.58s 蓄势扫频、Playing 首帧播 GO 冲击号角、终场播定格闷响 + 裁判双哨、一分钟提示改 `one_minute_1`、旧 3-2-1 改 `countdown_tick_1`；`tools/bake_sfx.py` 重写了 `match_start` / `go_horn` / `match_end` 三条音色。
主场景 `AudioManager` 的 6 个语音字段引用已随 `P1/音频/1` 重新绑定清除，`goCueClip` / `oneMinuteCueClip` 已写入场景；运行时仍有 `EnsureRuntimeClips` / `EnsureEditorClips` 按文件名兜底，重建场景时 `ProjectScaffolder.WireAudioClips` 也会写入。
涉及 `Scripts/Core/{AudioManager,AudioMixConfigSO,MatchManager}.cs`、`Scripts/Editor/{AudioSetupTools,ProjectScaffolder}.cs`、`tools/bake_sfx.py`、`Tests/EditMode/AudioSfxTests.cs`。

## 2026-11-04 #43
【Fixed: 手机端切图不生效 / 开始游戏无反应（真根因：APK 里没有第二张场景）】`BuildAndProfilerSuite` 与 `TapTapBuildSuite` 的 `BuildPlayerOptions.scenes` 只传了 `MainScenePath`，出包只有 `level0`；编辑器两图都在 Build Settings 所以测不出来，真机上 `LoadMap` 必然拿不到 Build Index → 浮层走失败分支关闭、选中态不变、开赛静默失败。现新增 `MapCatalogService.AllScenePaths()` + `BuildAndProfilerSuite.MapScenePaths()` 供所有出包入口使用，`EnsureSceneInBuildSettings` 覆盖全部地图场景；重出 APK 已确认含 `level0`(PhaseA)+`level1`(Kelp)。
浮层改为切换只写存档、原地刷新「当前」角标与文案（不再自动关闭），关窗后首页地图卡同步换图；「开始游戏」发现图不一致时先切场景，再由 `MapStartupRouter.RequestAutoStart()` 自动开赛；安装包缺场景时在首页地图卡显示可见错误而非静默失败。切图守卫改按 `AsyncOperation.isDone` 判定（8s 超时才视为过期），手机慢加载不会在途中反复重发 `LoadSceneAsync(Single)` 把自己打断。涉及 `Scripts/Editor/{BuildAndProfilerSuite,TapTapBuildSuite}.cs`、`Scripts/UI/{MapCatalogService,MapSelectOverlay,MapStartupRouter,MatchFlowUI}.cs`、`Tests/EditMode/MapCatalogTests.cs`。

## 2026-11-04 #42
【发布 v0.1.25（双端 Release）】含 #39–#41；版本三方一致 0.1.25 / versionCode 125；APK `build/moshuizuozhan-v0.1.25-arm64-v8a.apk`（65.4 MB）。

## 2026-11-04 #41
【地图选择卡预览改实景缩略图（3/4 立体沙盘）】新增编辑器工具 `MapPreviewCaptureTool`（菜单「关卡与地图/8」）：逐张打开地图场景，用**正交相机 50° 俯角 / 6° 斜角**把整块场地按 40×56 渲到 `Resources/MapPreview/<mapId>.png`（文件名取 `map.Id` 而非场景名），围边留 3%；`MapPreviewBuilder` 优先读图、缺图回落原程序化示意图，卡片预览框同为 40:56 所以 `preserveAspect=false` 不裁切。四个坑：① 场景存档里是「整片己方墨」的涂地状态，直接拍会得到整片橙地板 → 出图前 `ResetSurface`；② 相机架 200m 高空，场景距离雾会把图糊成灰白 → 出图期间关雾、结束还原；③ 出生台上站着玩家与 Bot → 临时关渲染器；④ **`cam.aspect` 必须先设成输出图的 40:56**，否则 `FrameArena` 按 Game 视图宽高比构图，竖幅渲染后左右被切、上方空出一片海。用正交而非透视：透视把近边放大、场地缩成画面中央一小块；正交按包围盒八个角在相机右/上轴的投影反解 `orthographicSize`，场地精确吃满画幅。

## 2026-11-04 #40
【CHANGELOG 改「排查知识库」口径：全量条目压到 1~3 行】原文 147k 字符、单条常上千字的流水账 → 77k 字符；只留「结论 + 根因/陷阱 + 关键文件」，删掉测试项数、用户原话、采样数值、设计推导、SHA/体积与发布条对各条内容的复述（发布条只留版本 + `#范围` + APK 名）。文件头补 3 条「反复踩的坑」总表（外部改 `.cs` 不重编译 / `Mat_TankGlass_*` 的 `_SrcBlend` 被写回 5 / 新增上屏中文要走字集账本），原先散在 5 处的重复记录已收拢。`AGENTS.md`《变更日志》小节同步写入该约束。

## 2026-11-04 #39
【Fixed: 手机端「地图切换点不动 / 点开始游戏没反应」+ 地图选择卡文字压边】根因是 `MapCatalogService.LoadMap` 的 `_loading` 静默守卫：玩家包在 `AfterSceneLoad` 阶段发起的 LoadScene 可能被引擎吞掉、`sceneLoaded` 不回调，标记永久卡死，此后所有切图与开赛补切全部 `return false`（存档已写、画面不动）。现改 `LoadSceneAsync` + 只拦同帧连点 + 按场景名扫 Build Settings 兜底 + 全部失败路径补日志，启动恢复交给 `MapStartupRouter.PersistedSelectionDriver` 逐帧重试，`MapForActiveScene()` 补场景名兜底（玩家包 `Scene.path` 可能为空），`TryStartMatch` 同步改用并在放弃时告警。
版面：地图卡改横版（预览在左 / 标题英文描述在右 / 按钮通栏），`PreviewSize` 按卡片可用高度撑满左列并删除误导性的 `CardPreviewMaxHeight`，描述两行换行（`CreateText(..., wrap:true)`），非当前图按钮由纯黑改纸白+墨色描边。涉及 `Scripts/UI/{MapCatalogService,MapStartupRouter,MapSelectOverlay,MapSelectLayoutMath,MatchFlowUI}.cs`、`Tests/EditMode/MapCatalogTests.cs`。另：`Mat_TankGlass` 的 `_SrcBlend` 又被编辑器写回 5，已还原。

## 2026-11-04 #38
【发布 v0.1.24（双端 Release）】含 #33–#37；版本三方一致 0.1.24 / versionCode 124；APK `build/moshuizuozhan-v0.1.24-arm64-v8a.apk`（64.6 MB）。

## 2026-11-04 #37
【首页地图选择卡 + 真实切图】首页第 3 张卡改 `MAP`（程序化 40×56 俯视预览 `MapPreviewBuilder`），点击打开 `MapSelectOverlay`，选另一张即 `MapCatalogService.TrySelect` 写 `PlayerPrefs("P1.Map.SelectedId")` 并 `LoadScene(Single)`；启动链路固定从 Build Index 0 起步 + `MapStartupRouter` 恢复选中图（不插 Boot 场景，避免改 Build Settings 顺序）。五个坑：① 切场景后 `WeaponSelectView` 是 DDOL 常驻，`SubscribeMatch` 的「已订阅」守卫让新 `MatchManager` 永远绑不上 → 检测实例变化后换绑；② PhaseA 没有 `ArenaStyleSceneAnchor`，从 Kelp 切回会残留雾/日光 → `sceneLoaded` 里调 `RenderEnvGlobals.ResetStyleToDefault()`；③ `ScreenFxController.OnDestroy` 对共享 `Resources/Mat_ScreenFx` 调 `Destroy` 每局必报错 → 只回收自己 `new` 的拷贝；④ `HomeInfoOverlay.Show` 固定 sortingOrder 50 会被暂停设置页（Canvas 100）盖住 → 加可选参数；⑤ Esc 仲裁新增 `MenuEscLayer.MapSelect`，`PauseMenuOverlay` 与 `WeaponSelectView` 都要接。涉及 `Scripts/UI/{MapCatalogService,MapStartupRouter,MapPreviewBuilder,MapSelectLayoutMath,MapSelectOverlay,MatchFlowUI,HomeInfoOverlay,ScreenFxController}.cs`、`Scripts/UI/Core/MenuInputRouter.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Scripts/Core/RenderEnvGlobals.cs`。

## 2026-11-04 #36
【Fixed: 滚筒按住推地仍读作「悬浮」】根因不在贴地闭环（实测 gap=0、contact=1），而是拖地基准 25°/body10° 离地太远（闭环要再拧 +25°，滚轴几乎竖直吊在手心），且滚轴大圆柱的鼓体底缘被侧面挡住，数学贴地仍像悬空。按 INKWAVE `character.js` 的 `wRoll` 重标（40°/drop0.02/fwd0.14/body18°/sink0.06），并补鼓下接地阴影与推地墨雾压到真实接触线。涉及 `Scripts/Player/{RollerWieldSolver,CharacterVisualController,CharacterContactShadow}.cs`、`ScriptableObjects/CharacterAnimConfig.asset`。坑：`CharacterContactShadow.Create()` 按固定名返回脚底那份，滚筒阴影必须自建 `RollerContactShadow`。

## 2026-11-04 #35
【空墨准星反馈 + 「弹药耗尽」toast】墨水低于下一发门槛时准星染红 + 方波硬闪，扣扳机被拒时红叉弹跳 + 震颤 + 刻度反向内收；数据链 `ReticleStateMath.ComputeInk`（纯逻辑）→ `WeaponController.InkRequiredToFire`。坑：① 门槛必须由策略给出（`MainWeaponStrategy.InkRequiredToFire`，狙/加特林/滚筒各自重写），UI 里写死单发墨耗会出现「准星说没墨、扣扳机还能打」；② 拒射走轮询戳记 `EmptyFeedbackStamp`（0.18s 节流）而非事件，避免换枪/复活产生悬挂引用；③ 空打震颤只能写在各形态容器上（根节点 `localScale` 被击杀弹跳占用）；toast 要按画布高度换算相对中心的 Y。涉及 `Scripts/UI/{WeaponReticleUI,ReticleStateMath}.cs`、`Scripts/Weapons/{MainWeaponStrategy,WeaponController,ChargerWeaponStrategy,SplatlingWeaponStrategy,RollerWeaponStrategy}.cs`。

## 2026-11-04 #34
【Fixed: 开赛墨幕 / 阵亡敌墨 / 复活虹膜二次色彩空间转换】`ScreenInkTransitionView` 在 `Material.SetColor` 前又手动做了一次 `ToLinear`，而 ShaderLab `Color` 在 Linear 工程下本就由 Unity 自动 sRGB→Linear，队伍橙被压成暗血红。改为直接传 `InkSurfaceController.GetTeamColor` 的 sRGB 原值。**`KillFlashView`、`LensInkView` 里仍是「`.linear` + `SetColor`」的同类写法，未改。** 涉及 `Scripts/UI/ScreenInkTransitionView.cs`。

## 2026-11-04 #33
【新地图「潮汐货站 / Kelp Terminal」】新增 `Environment/ArenaLayoutKelp.cs`（只定义本图道具表，玩法常量复用 `ArenaLayoutV2`）+ `KelpTerminalGeometryBuilder.cs`，入口菜单 `P1/关卡与地图/生成新地图：潮汐货站`；画风档 `ScriptableObjects/ArenaStyle_KelpTerminal.asset` + `Materials/Mat_KT_*`。三个坑：① 运行时 `RenderEnvGlobals` 按固定路径拾取 `ArenaStyle.asset`，多场景共用会读错雾/日光 → 新增 `ArenaStyleSceneAnchor`（`[DefaultExecutionOrder(-1000)]`）由场景 push；② `ArenaDecorationBuilder`/`ArenaAtmosphereBuilder` 的网格与天空/水面写到固定资产名 → 新图必须传 `meshPrefix="kelp"` 与 `ArenaMaterialSet` 覆盖；③ `RegisterSceneInBuildSettings` 原逻辑会把新图插到 Index 0 顶掉默认启动图，已改 PhaseA 固定 0。新图复用已烘地面图集，不重烘。涉及 `Scripts/Editor/{ProjectScaffolder,ArenaDecorationBuilder,ArenaAtmosphereBuilder}.cs`、`Scripts/Core/RenderEnvGlobals.cs`。

## 2026-11-04 #32
【发布 v0.1.23（双端 Release + TapTap 提审）】含 #27–#31；版本三方一致 0.1.23 / 123；APK `build/moshuizuozhan-v0.1.23-arm64-v8a.apk`（64.4 MB）。TapTap（app 1020602）走「create-draft → 绑包 → 写更新日志 → 提审」。两个坑（已同步 `Docs/TapTapRelease.md`）：① 上一版「已上线」后字段被锁，`analyze-app-status` 在空草稿视图下报的必填缺失 + 未选主包体是假警报，先 `app create-draft`（继承线上资料）再按同次 `list-packages` 的 `package_slots.main.expected` 原样回传 `select-package`；② 菜单 13 的 `try/finally` 只还原编辑器内存宏，`ProjectSettings.asset` 可能停在构建中途写入的 `P1_OFFLINE`（此时 `script.eval` 读到的 Android defines 是空的：内存对、文件错），提交前必须 `git diff ProjectSettings/ProjectSettings.asset`。

## 2026-11-04 #31
【v0.1.23 出包前两处门禁修复】① Fixed: #27 新增的 EditMode 用例断言「运行时构建出的界面层级」不可能通过——`AddComponent` 不触发 `Awake`、`BuildUI()` 不执行，`transform.Find` 恒 null 且不报错，静默变成空断言（FPS 那条因为有 `if (badge != null)` 兜底反而显绿）。层级断言整体迁 PlayMode。**结论：被断言的节点由 `Awake`/运行时装配产生，就必须放 PlayMode。** ② `Mat_TankGlass_D9EBFF73.mat` 的 `_SrcBlend` 又被写回 5（见文件头反复坑）。涉及 `Tests/EditMode/MobileTouchUITests.cs`、`Tests/PlayMode/{PauseMenuPlayTests,UIFoundationPlayTests}.cs`。

## 2026-11-04 #30
【播报层视觉验收用例 `MatchCalloutVisualCapturePlayTests`】把四种真实形态铺到全屏墨底 Canvas 上自动截图（只截图 + 打几何日志，像素断言仍在 `MatchCalloutViewLayoutTests`）。两个坑：① `ScreenCapture.CaptureScreenshot` 异步落盘，连续几帧连着截会把上一帧写进下一个文件 → `WaitForEndOfFrame` + `CaptureScreenshotAsTexture` 同步读回再自己写盘；② 用例跑在主菜单场景时 `MatchManager` 的阶段切换会让 `MatchCalloutView.HandlePhaseChanged` 对非 Playing 阶段 `DismissImmediately` 关掉正在展示的播报 → 截图前必须确认仍在 Holding。涉及 `Tests/PlayMode/MatchCalloutVisualCapturePlayTests.cs`。

## 2026-11-04 #29
【首页去掉与「按键位置」卡重复的顶部自定义按键小按钮】坑：`HomeMenuMetrics.ShowTopButton` 一直被当「是否移动端」的替身传给 `LoadoutCard()`/`ResolveHeroFlow()`，直接置 false 会让移动端 Hero 卡与 CTA 退回桌面尺寸 → 新增 `HomeMenuMetrics.Mobile` 承载平台判断（`ShowTopButton` 保留但恒 false）。环境坑见文件头①。涉及 `Scripts/UI/{MatchFlowUI,MenuLayoutMath,HomeInfoContent}.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-04 #28
【Fixed: 连杀/战斗大字播报底板过长、中间大片空洞】尺寸沿用英文长句时代（620~700px），中文化为 2~4 字后左右留白近 400px。改三档紧凑尺寸：连杀/多杀 320/330、首杀/反杀 260×80、带副标题 410×108；`StreakPips` 由左下偏置改水平居中、单颗 9×9。涉及 `Scripts/UI/MatchCalloutView.cs`、`Tests/EditMode/MatchCalloutMathTests.cs`。

## 2026-11-04 #27
【暂停弹窗整合「操作按钮调整」】主面板「系统设置」下方新增入口：移动端先退出暂停再唤起触控布局拖拽编辑器，桌面端唤起按键设置（Esc / 点空白精确退回暂停主视图）；主面板按钮高度统一 76。Fixed: 左上角常驻长方形阴影块＝`HudController.EnsureFpsBadge` 的墨色底板在 `ApplyFpsBadgeVisibility` 里只隐藏了文字子节点 → 引入 `_fpsBadgeRoot` 整块显隐，开启 FPS 时按 `IsMobileLike` 避让到暂停键右侧。涉及 `Scripts/UI/HudController.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Scripts/UI/MobileTouchUI.cs`。

## 2026-11-04 #26
【发布 v0.1.22】含 #22–#25；版本三方一致 0.1.22 / 122；APK `build/moshuizuozhan-v0.1.22-arm64-v8a.apk`（64.4 MB）。坑：提交前 `Mat_TankGlass_D9EBFF73.mat` 的 `_SrcBlend` 又被写回 5（见文件头反复坑）。

## 2026-11-04 #25
【出生台中心黑盘提亮】根因是两个材质生成入口写了不同 `_BaseColor`（`ProjectScaffolder.CreateOrUpdateSpawnPadMaterial` 0.50 钢灰 vs `ArenaAtmosphereBuilder.ConfigureSpawnPadMaterial` 0.22 深钢色），哪条流程后跑就刷成哪一种。现两处统一 0.50，注释标明「必须同值」。坑：改这两处任何一处的调色都必须同步另一处，否则只在部分重建路径生效。涉及 `Scripts/Editor/{ArenaAtmosphereBuilder,ProjectScaffolder}.cs`、`Materials/Mat_SpawnPad_Team{A,B}.mat`。

## 2026-11-04 #24
【顶部倒计时再压一档】`CenterTimerView` 桌面 136×60 / 字号 38、移动端 152×68（`FontSnapped` ×1.28 → 48），READY 26。坑：① 用户看到的可能是旧包或旧截图——改源码不影响已装 APK，验收必须重新出包；② 截图工具里 `cullingMask=0` 会把 `Canvas` 自己也剔掉（整帧纯色底、五档尺寸 MD5 全同，误判「尺寸没生效」），要按 `Canvas.gameObject.layer` 生成遮罩；③ `FindAnyObjectByType<HudController>()` 可能命中另一个 `CenterTimer` 为 null 的实例后静默 return，取视图时要筛 `activeInHierarchy`。涉及 `Scripts/UI/CenterTimerView.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-04 #23
【出生台队色降饱和】鲜红/纯蓝大面积铺在浅暖灰基台上过刺眼。`SpawnPad.shader` 新增 `_TeamTintBlend`（0.80，队色先向基台混凝土色 lerp）；两处材质生成同步写 0.80。坑：后处理 `colorSaturation +14 / colorContrast +16` 会二次放大饱和，必须按屏上取样定值。另修 `Mat_TankGlass_*` 的 `_SrcBlend`（见文件头反复坑）。涉及 `Shaders/SpawnPad.shader`、`Scripts/Editor/{ArenaAtmosphereBuilder,ProjectScaffolder}.cs`。

## 2026-11-04 #22
【顶部倒计时再缩一档】`CenterTimerView` 桌面 170×74 / 字号 50、移动端 190×82（字号 64），READY 36；四个尺寸/字号常量是唯一数据源，`ApplyLayoutMetrics`/`SetTime`/`BuildHierarchy` 共用。坑：最后 1 分钟横幅落位公式 `-(70 + 球高 + 16)` 给的是**锚点中心坐标**（pivot 0.5）而不是注释里写的「顶边」，改球体尺寸后务必用 `GetWorldCorners` 实测。涉及 `Scripts/UI/CenterTimerView.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-04 #21
【发布 v0.1.21】含 #09–#20；版本三方一致 0.1.21 / 121；APK `build/moshuizuozhan-v0.1.21-arm64-v8a.apk`（64.4 MB）。坑：见文件头③（新增上屏中文的字集账本）。

## 2026-11-04 #20
【Fixed: 手雷徽标换图标后回归测试 NRE】#10 把徽标左侧从 "BOMB" 文字换成图标（`UIIconLibrary.Get("sub_bomb")`），有图标时不再建名为 "Text" 的子节点，而旧用例无条件 `badge.Find("Text")` → null NRE。改为「Icon / Text 二选一」断言。涉及 `Tests/EditMode/ReticleAndTrajectoryTests.cs`。

## 2026-11-04 #19
【首页 Hero 卡改流式排版】卡片从上到下 = Header → 武器整备卡 →「难度」4 档 →「时长」3 档 → CTA，`MenuLayoutMath.HeroFlowLayout`/`ResolveHeroFlow` 为设计盒单一数据源，运行时用 `HomeHeroCard.sizeDelta.y` 重算。尺寸连带改动（CTA/整备卡/设置行高/`CardBottom` 208→188）见 `HomeMenuMetrics`。涉及 `Scripts/UI/{MatchFlowUI,MenuLayoutMath}.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`、`Tests/EditMode/MenuLayoutMathTests.cs`。

## 2026-11-04 #18
【武器逐个对齐 INKWAVE `firingPose`：身体朝向与枪口不再脱节】① `WeaponController.WantsAimFacing` 从 `IsCharging || IsStreaming` 扩到完整开火姿态（含 `IsRollingDodge`/`IsTurretLocked`——双枪翻滚后架枪站桩时朝向仍在跟移动输入，枪钉原地而身体原地打转）；② 副武器按住瞄准此前不属于主武器状态，`InkCharacterMotor.SetFrameIntent` 新增末位可选参 `aimFacingRequested`（默认 false，旧调用语义不变）+ 只读 `IsAimFacingRequested`；③ 泼桶出手方向改读甩出瞬间的准星（对齐 INKWAVE `fireSlosh`），消除前摇内甩枪的「枪口朝新目标、墨浪飞旧方向」。涉及 `Scripts/Weapons/{WeaponController,SlosherWeaponStrategy}.cs`、`Scripts/Player/{InkCharacterMotor,PlayerController}.cs`。

## 2026-11-04 #17
【启动流程改「启动 logo → 黑屏 → 直接首页」】`LaunchIntroView` 新增 `coverOnly` 模式（只建全屏纯黑遮罩，等 `MatchFlowUI.HasStableHomeGeometry` 且黑够 0.12s 后硬切自毁，上限 2.5s 兜底，继续承担「脏几何首帧不许露出来」的职责）；`MatchFlowUI.Awake` 首次构建改 `EnsureReadyVisuals(false)`，黑屏揭开即完整首页。坑：`Create` 里必须先 `SetActive(false)` 再 `AddComponent` 再赋值 `_coverOnly` 再激活——`AddComponent` 会立即触发 `Awake`，那时 `Build()` 必须已经知道是黑屏模式。品牌动画代码与 `LaunchIntroMath` 保留（`Create(force:true)` 仍走完整演出）。涉及 `Scripts/UI/{LaunchIntroView,MatchFlowUI}.cs`。

## 2026-11-04 #16
【Fixed: 加特林松手扫射时身体朝向脱节】松手进入扫射后 `PlayerController` 不再调 `TryFireMainWeapon`，`_aimFacingTimer` 失效 → 朝向仲裁掉回「面向移动方向」，身体转向身后而弹幕仍按准星倾泻。新增 `WeaponController.WantsAimFacing => IsCharging || IsStreaming`，`InkCharacterMotor` 把该状态并入朝向仲裁并每帧刷新 `_aimFacingTimer`（松手保留 0.4s）；玩家与 Bot 共用。后续已由 #18 扩到完整开火姿态。涉及 `Scripts/Weapons/WeaponController.cs`、`Scripts/Player/InkCharacterMotor.cs`。

## 2026-11-04 #15
【首页 Hero 卡删品牌大标题 + 整备卡垂直居中】新增 `MenuLayoutMath.ResolveHeroWeaponSlotTop(m, heroHeight)`：整备卡在「头部设置行下沿 +12」与「CTA 上沿 −12」之间居中。**关键：Hero 卡高度随窗口/安全区变化，`WeaponSelectView.ResolveHeroWeaponSlot` 必须用 `HomeHeroCard.sizeDelta.y` 重算。** 移动端头部到 CTA 只有十几 px 空档，且 TMP 的 Overflow 会从 0 高盒子里继续画字（历史曾从 CTA 上沿露出残字）——**别把标题加回来**。涉及 `Scripts/UI/{MatchFlowUI,MenuLayoutMath}.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`。

## 2026-11-04 #14
【Fixed: 按键布局编辑器背景透出首页 UI】旧背板是编辑器根节点上的 65% 半透明黑，既挡不住首页卡片又压暗一层按键。改为独立全屏**不透明**深墨舞台底，挂在触屏画布根并 `SetAsFirstSibling`（渲染在首页/战场之上、按键与编辑面板之下），`raycastTarget=true` 吃掉空白点击；根节点旧 Image 改 `Color.clear`（测试语义保留）。涉及 `Scripts/UI/MobileLayoutCustomizer.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-04 #13
【首页可选比赛时长 2/3/5 分钟】生效路径收口：`MatchManager` 四处 `_remaining` 初始化全走 `ResolveMatchDurationSeconds()`（读 `GameSettingsService`，≤0 才回落 `GameConfigSO`，故 `.asset` 未动）；`GameSettingsMath` 新增 key/三档常量/`NormalizeMatchDurationMinutes`（平局取较长档）+ 六件套属性与事件。坑：① 移动端要腾第二行高度（`CardBottom` 228→208、整备卡 200、间距 48→72），`BuildHomeHero` 手算的 `slotTopLocal` 必须与 `HomeMenuMetrics.HeroWeaponSlot` 同源；② TMP Overflow 会从近 0 高盒子里向下画字，加 `freeH >= titleFont * 1.1f` 门槛。涉及 `Scripts/Core/{GameSettingsMath,GameSettingsService,MatchManager}.cs`、`Scripts/UI/{MenuLayoutMath,MatchFlowUI}.cs`。

## 2026-11-04 #12
【BGM 默认音量 1.0→0.2】满格 BGM 压过枪声与播报；只改默认值，老玩家 PlayerPrefs 已存 1.0 不受影响。涉及 `Scripts/Core/GameSettingsMath.cs`。

## 2026-11-04 #11
【Fixed: 设置页在 20:9 手机上返回不了】根因：设置项堆到 11 行、面板临时扩到 1124 高，而 `ApplyResponsivePanelScale` 的可用高度按硬编码 1080 算（2000×900 屏画布实际约 966 单位，缩放算大）→ 面板溢出、页脚「恢复默认/返回」被裁到屏外，触屏没有返回途径。修复：设置项拆「声音/视角/画面与操作」三板块 Tab + 面板恢复固定 820 + 缩放系数改取 CanvasScaler 生效后的实际根节点高度（<100 回退 1080）+ 点面板外空白返回。涉及 `Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-04 #10
【战斗播报中文化 + 击杀成就 UI 重做】击杀播报改「首杀/双杀/三杀/四杀/反杀/终结连杀/全队击倒/连杀」+ 图标圆章 + ×N 徽章，击杀卡改「击倒」、队伍名改中文，横幅补时钟图标，手雷徽标改图标。陷阱：中文必须走 `UIThemeSO.ResolveFont(..., text)` 的 CJK 分支（直接用 `GetFont(true)` 会把只预烘 ASCII 的标题字重用于中文）；新增字必须先在字集账本里（见文件头③）。涉及 `Scripts/UI/{MatchCalloutView,MatchCalloutMath,KillCardView,KillFeedView,CenterTimerView,MatchBannerView,WeaponReticleUI}.cs`。

## 2026-11-04 #09
【顶部倒计时缩小一档】几何/字号提为 `TimerFontBaseSize`/`TimerReadyFontBaseSize`/`TimerBlobDesktopSize`/`TimerBlobMobileSize` 常量供 `ApplyLayoutMetrics`/`SetTime`/`BuildHierarchy` 三处共用，避免字号再次三处漂移；最后 1 分钟横幅仍按球高推导落位。涉及 `Scripts/UI/CenterTimerView.cs`、`Docs/UI-GeometricBold.md`。

## 2026-11-04 #08
【发布 v0.1.20】含 #02–#07；版本三方一致 0.1.20 / 120；APK `build/moshuizuozhan-v0.1.20-arm64-v8a.apk`（64.3 MB）。陷阱：`release.py bump --write` 经编辑器同步 `PlayerSettings` 时内存里已是新版本但 `ProjectSettings.asset` 未落盘（GUI 编辑器不自动 flush），`publish` 的版本一致性校验会直接拒绝——需显式 `File/Save Project`。

## 2026-11-04 #07
【设置页补分轨音量 + FPS 角标 + 小地图开关】音乐/音效为 `AudioListener.volume` 下的分轨：音乐轨在 `AudioManager.ApplyMusicVolume` 乘；音效轨必须乘在 `PlayOne`/`PlayAnnouncement`/`PlayLoop` 三处收口（循环音是长驻音源，只在 `PlayLoop` 定价的话拖滑条不生效，故订阅 `SfxVolumeChanged` 重算在播通道）。坑：① `PauseMenuOverlay` 设置视图与主/按键视图共享 820 固定面板，加行就溢出 → 打开设置页临时扩高、所有退出路径恢复，且 `ApplyResponsivePanelScale` 目标高度必须跟 `sizeDelta.y`；② `OpenSettings` 回读清单漏项导致灵敏度/反转 Y 二次打开显示陈旧值；③ 编辑器后台 Play 下 `ScreenCapture` 会反复返回同一缓存帧（多次截图 MD5 相同），含 uGUI 的 HUD 只能靠反射断言验收；④ `git stash` 对照后 pop，若编辑器还在 Play 不会重编译，判据要用反射读域内类型。涉及 `Scripts/Core/{GameSettingsMath,GameSettingsService,AudioManager}.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`、`Scripts/UI/HudController.cs`。

## 2026-11-04 #06
【布局自定义器顶栏收窄】顶栏原为全屏宽横条（anchor 0→1），把顶部两角的潜行/左开火键盖住没法拖。顶栏/属性舱/辅助卡统一 `PanelWidth=960` 居中，滑轨 150×64→110×44、把手 24×64→20×44，文案缩短并按移动端 ×1.28 字宽重排全部内部 X 坐标。坑：画布参考 1920×1080 + match 0.5，GameView 非整 16:9 时面板实际渲染宽 ≠ 设计值，核对坐标要按 `GetWorldCorners`。涉及 `Scripts/UI/MobileLayoutCustomizer.cs`。

## 2026-11-04 #05
【Fixed: Android 真机首页「先是放大的错版 → 闪一下 → 变正常」】两段叠加：① 卡片树在 `Awake` 第 1 帧按未 settle 的 `Screen.safeArea`/画布尺寸构建；② settle 后几何指纹守护触发 `RebuildHomeVisuals()`，走 `EnsureReadyVisuals()` 的错峰入场（先压成缩小+全透明再逐张弹回），重建瞬间整树消失一帧。修复：重建改走 `EnsureReadyVisuals(false)` → `SnapHomeEntrance()` 无缝吸附；新增静态 `MatchFlowUI.HasStableHomeGeometry`（指纹连续 ≥2 帧不变，`Awake` 显式归零防 DisableDomainReload 残留），`LaunchIntroView` 在擦除处等它（上限 2.5s 兜底，**门槛必须 fail-open**，否则 `LaunchIntroPlayTests` 转红）。编辑器/桌面 safeArea 恒等于全屏，只在真机可见。涉及 `Scripts/UI/{MatchFlowUI,LaunchIntroView}.cs`。

## 2026-11-04 #04
【Fixed: Android 自适应图标圆形被切】根因是自适应图层画得超出遮罩可见区——背景橙圆占 78% 边长、前景墨点贴 2/3 安全区外沿，而启动器圆形遮罩只保证中心约 61% 可见（鸿蒙/小米还会先 1.5× 放大再套遮罩），实测内容最远伸到 0.394R。`tools/bake_brand.py` 重排：背景橙圆缩到 0.52 边长、前景 0.40/墨点 0.16 并整体收进 0.30R；桌面主图标与启动画面未动。需重新出 Android 包生效。涉及 `tools/bake_brand.py`、`Assets/_Project/Art/Brand/`。

## 2026-11-04 #03
【Fixed: Android 无法反向横屏】根因是 `Assets/Plugins/Android/AndroidManifest.xml` 两个 activity 都声明 `screenOrientation="landscape"`——Android 语义里这是锁死单一横屏方向、无视传感器，直接压过 Player Settings 的自动旋转（那里本就是 true）。改 `sensorLandscape`。纯清单变更，下次出包生效。

## 2026-11-04 #02
【Fixed: 检查更新「前往下载」改回应用内「立即更新」】根因：`UpdateManager.InAppInstallSupported` 自 TapTap 隐私整改起被硬编码 `=> false`，而 `P1_OFFLINE` 变体已专门承载 TapTap 上架包，全量包不必陪绑。改 `#if P1_OFFLINE false #else true`；manifest 加回 `REQUEST_INSTALL_PACKAGES` 与 `androidx.core.content.FileProvider`（**陷阱：provider 必须是 `<application>` 子节点，放顶层会 Gradle merge 失败**）；离线变体剥离扩为同删安装权限 + provider。涉及 `Scripts/UI/UpdateManager.cs`、`Scripts/Editor/AndroidPrivacyManifestPostprocessor.cs`、`Assets/Plugins/Android/AndroidManifest.xml`。

## 2026-11-04 #01
【发布 v0.1.19】含 #20–#26（Slam 空翻演出 + 混音重配平 + 涂地实时显色 + 角色领口封口 + 网页跑动减颠）；版本三方一致 0.1.19 / 119；APK `build/moshuizuozhan-v0.1.19-arm64-v8a.apk`（64.3 MB）。发版前 `FontCoverageTests` 红（Tooltip 新增字），跑 `tools/font_charset.py` + 字体三连菜单后恢复。

## 2026-11-03 #26
【弹着涂地二次定位：地面图集 mip 链是「命中后才出现墨」的真根因】覆盖 #24 的「mip 不回退」结论。实测：`SplatAtlas.Atlas.mipMapBias` 压到 −4（等价只采 mip0）后，原本看不见的落墨立刻整片显色。机理：图集 2985×4096 带 mip，第三人称机位只有 3.2m 高、斜视地面，10m 处像素足迹约 5cm（LOD≈1~2）、20m 处约 13cm（LOD≈3）；新墨只画进 mip0，而 `GenerateMips` 受 `atlasMipIntervalSeconds=0.5` 节流 → 「网格和图集都是新墨，画面要等下次 mip 重建才实心」。墙面图集是 `mipChain:false` 所以只有地面/顶面有此延迟。修复：`InkGridConfigSO.atlasMipmaps` 默认改 false（顺带消掉 15ms 级 GenerateMips 尖刺）。取舍：远景墨迹失去 mip 过滤可能轻微闪烁；回退路径 `atlasMipmaps=true` + `atlasMipIntervalSeconds≈0.15`（有尖刺）。涉及 `Scripts/Ink/InkGridConfigSO.cs`、`ScriptableObjects/InkGridConfig.asset`。

## 2026-11-03 #25
【Fixed: Slam 起跳「直直升上去好怪」——角色本体是贴地跑待机姿态，不是缺动画】根因：`WeaponController.UpdateSpecialSlam` 只抬升 `visualBodyRoot`，角色 Transform 与 `CharacterController` 全程贴地 → `CharacterVisualController._groundedNow` 恒真，贴地步态/待机姿态整段接管。按 INKWAVE `character.js` 的 `_poseLeap`/`_poseSlam` 补独立表演管线：新增 `Player/SlamPoseSolver.cs`（纯逻辑梯形窗，四段权重 + FlipRadians）+ `CharacterVisualController.SampleSlamPerformance`（腿/躯干/挤压/`KidRoot` 前空翻/武器过顶→砸地）。坑：① 落地收势段必须由视觉层自己计时（`WeaponController` 在落地帧就归零 `_slamPhase`）；② 验收前空翻要用 `Vector3.Dot(kidRoot.up, Vector3.up)` 走到 −1（`Quaternion.Angle` 只到 180°）；③ 落地触发 `HitstopService.Pulse`，PlayMode TearDown 必须 `Cancel()` + `Time.timeScale=1`，否则污染后续用例。调参在 `CharacterAnimConfigSO` 的「大招 Slam 起跳表演」段（`slamFlipDegrees` 设 0 可关空翻）。涉及 `Scripts/Player/{SlamPoseSolver,CharacterVisualController,CharacterAnimConfigSO}.cs`、`Scripts/Weapons/WeaponController.cs`。

## 2026-11-03 #24
【弹着涂地「感觉有延迟」：延迟主体是弹丸飞行时间，可修的是落地可见尺寸】逐帧实测（`PendingSplatCount/CommandExecuteCount` 探针）：命中当帧 `AddSplat`，同一 LateUpdate 的 `InkSplatAtlas.Advance` 就提交绘制（`pending` 恒 0），**图集没有排队/节流延迟**；扣扳机到落点爆斑 ≈330ms 等于 34m/s、12m 弹道的飞行时间。真正可修：`InkGrowShrinkMeters`（生长动画的片元侧形态学侵蚀）是**绝对米数** 0.4m，而沿途滴墨半径只有 0.44m → 落地瞬间可见半径只剩 0.04m；落点飞溅小点也被整段抹掉。改 `splatGrowSeconds` 0.12→0.08、`atlasGrowShrinkMeters` 0.4→0.18。弹速/射程属平衡值，未动。涉及 `Scripts/Ink/{InkGridConfigSO,InkSurfaceController}.cs`、`ScriptableObjects/InkGridConfig.asset`。

## 2026-11-03 #23
【Fixed: 角色颈部空心 / 脖子像悬空细棍】根因：T 恤网格 `CreateTeeMesh` 顶环半径 0.078m 却两端敞口（`openCaps: 3`），而颈筒半径只有 0.038m → 俯视时领口与脖子之间留 3~4cm 环状开口，直接看穿空心筒内壁。按 INKWAVE `addTeeCollar` 重做：加三圈锥形领口（顶环 0.050×0.048）+ `openCaps` 改 1（只开下摆）封死顶面 + 前领窝 0.038→0.014 + `BuildSuperellipseTube` 新增可选 `topZShift`；新增 `CreateCollarRibMesh` 挂 Spine（替代 Neck 骨上的圆片）；颈筒半径 0.040、长度 0.15 并整体下沉（两端埋进胸腔/头部），可见段只剩领口到下巴 2~4cm。**坑：场景角色是编辑器装配期烘焙的，改装配器后必须再跑菜单 `P1/角色与武器/升级场景中所有角色模型 (P5-A)`，只改 C# 画面不变。** 涉及 `Scripts/Player/{CharacterVisualAssembler,CharacterMetrics}.cs`。

## 2026-11-03 #22
【音频混音重配平 + 补齐 Bot 远场开火】根因（ffmpeg 实测）：BGM 原曲 −10.5 LUFS 且 crest factor 仅 ~9.6dB（高压缩持续能量），掩蔽了峰值仅 −9.4dBFS 的开火瞬态；且全项目**无 AudioMixer 总线**、无战斗 ducking，AudioSource 优先级还是反的（BGM=96 高于 SFX 池默认 128，发声数超限时 SFX 先被抢占）。全部落在 `AudioMixConfigSO`（新增音量阶梯/duck/远场参数）+ `AudioManager` + `WeaponAudioDriver`：bgmVolume 0.34→0.26 + 开火 duck（0.18/0.45s）、单发阶梯对齐 INKWAVE sfx-catalog、优先级改 SFX 96 > 循环 128 > BGM 180；**Bot/队友此前完全静默**（`HandleFired` 非本地直接 return）→ 新增 `AudioManager.PlayWeaponShotAt`（24m 平方衰减 + 50ms 节流 + 音量上限 0.30）。坑：旧 `.asset` 已序列化 bgmVolume=0.34，改 C# 默认值对它无效，必须在**非 Play 模式**跑菜单 `P1/音频/3. 写入推荐混音数值`。涉及 `Scripts/Core/{AudioMixConfigSO,AudioManager}.cs`、`Scripts/Weapons/WeaponAudioDriver.cs`、`Scripts/Editor/AudioSetupTools.cs`。

## 2026-11-03 #21
【Fixed: 网页试玩人物头部持续上下跳 + 整体卡顿】① 不是头部骨骼在动，而是**骨盆竖向起伏**：`KidGaitSolver` 的贴地几何下沉在 4.5 m/s 以上被 `kidHipDropMaxMeters` 削顶成 6 步/秒、峰峰 35mm 的方波，低帧率采样不足时混叠成乱跳 → `CharacterVisualController` 新增骨盆竖向量一阶低通（时间常数按帧率自适应，`VisualMotionMath.BobSmoothTime`）。② 视觉层补 dt 钳制 `VisualMotionMath.SanitizeFrameDt`（上限 0.1s）：读盘/GC/切后台的假帧会让步态相位一次推进大半圈（观感「人抽一下」）；**上限刻意只截真卡顿，更紧的钳制会让视觉层与移动层（0.05s）时间基准解耦，脚与位移脱节、`Running_GaitCadenceTracksDistance` 转红**。③ WebGL 画布此前按 `window.devicePixelRatio` 创建（Retina 4 倍像素）→ 模板 `createUnityInstance` 加 `devicePixelRatio: 1`。涉及 `Scripts/Player/{CharacterVisualController,VisualMotionMath,CharacterAnimConfigSO}.cs`、`Assets/WebGLTemplates/InkTurf/index.html`。

## 2026-11-03 #20
【Tidal Slam 镜头演出】根因：`UpdateSpecialSlam` 只抬升视觉体 4.2m，Transform 与相机枢轴留在地面，顶点角色纵向屏幕投影 1.17~1.50（整个人在画面上方之外），5.2m 预警圈被低机位透视压扁、近侧半圈落在相机身后。`TopDownCameraFollow` 新增三拍演出（起跳枢轴 +2m/吊臂 +3m/FOV +4° → 下砸收回高机位 → 落地弹簧推近 + FOV 收紧后回基线），全是沿视轴的位置/FOV 偏移、不旋转照射轴，且只接受被跟随的本地玩家；落地加 45ms 微定格（相机用真实时间推进）。调参在 `GameConfigSO` 的「大招：Tidal Slam 镜头演出」段。涉及 `Scripts/Player/TopDownCameraFollow.cs`、`Scripts/Weapons/WeaponController.cs`。

## 2026-11-03 #19
【发布 v0.1.18】含 #17–#18（地面墨色回归队色 + TapTap 无联网变体落地）；版本三方一致 0.1.18 / 118；APK `build/moshuizuozhan-v0.1.18-arm64-v8a.apk`（63.1 MB）。另：构建时 Unity 把 `Mat_TankGlass_D9EBFF73.mat` 的 `_SrcBlend` 归一化为 1（与 `_ALPHAPREMULTIPLY_ON` 配对，旧的 5 会双重乘 alpha）——见文件头反复坑。

## 2026-11-03 #18
【TapTap 上架包改无联网变体（拒审：安装包含联网功能需 ICP）】新增宏 `P1_OFFLINE`（`Scripts/Core/BuildVariants.cs`）：开启后 `UpdateManager.NetworkFeatureEnabled` 编译为常量 false、`CheckForUpdate` 直接返回、首页角签退回只读版本号；新增菜单 `P1/构建/13`（临时写宏 → 出包 → `try/finally` 还原）。**关键陷阱：`#if P1_OFFLINE` 在 `P1.Editor` 程序集里永远为假**（宏只写进 Android 平台 defines，而 Editor 程序集按 Editor 平台编译，首次构建的包因此仍带 INTERNET）→ `AndroidPrivacyManifestPostprocessor` 改为运行时读 `PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)`；`-executeMethod` 只接受无参方法，批处理入口必须另给 `BuildTapTapOfflineBatch()`。发版清单在 `Docs/TapTapRelease.md`。

## 2026-11-03 #17
【Fixed: 地面墨色发粉 / 变浅】根因：P31 把 `InkGround.shader` 清漆环境反射的墨色染色从 80% 降到 45%，天空反射的冷白直接叠到队色上，橙色墨蓝通道被抬高读作「粉橘」（低机位掠射角最明显；墙面因 `InkWall` 走 0.25→1.0 的 `specTint` 未受影响）。改回 90% 强染色，并把干墨环境反射基乘 1.15 降回 1.0。仅 `Shaders/InkGround.shader`。

## 2026-11-03 #16
【发布 v0.1.17】含 #12–#15（原生隐私启动页 + 画质去廉价感 + 首页检查更新角签）；版本三方一致 0.1.17 / 117；APK `build/moshuizuozhan-v0.1.17-arm64-v8a.apk`（63.1 MB）。陷阱：`RenderEnvGlobals.EnsureStyle` 走 `Resources.Load("ArenaStyle")` 在编辑器 Play 下必失败（资产在 `ScriptableObjects/`），已补 `#if UNITY_EDITOR` 的 AssetDatabase 回退，否则整局走硬编码回退值、风格资产质感参数全部静默不生效。

## 2026-11-03 #15
【TapTap 0.1.17 提审】`app 1020602` 主包体切换 + `apk_whatsnew` 更新，预检 blockers 为空，提交审核。陷阱：① CLI 本地 profile 的 app scope 会残留为其它游戏，`upload-apk` 必须显式 `--app-id 1020602`（曾误传到别的游戏的包体库，CLI 无删除接口）；② `release.py build --force` 仍会优先复用 `build/` 里版本匹配的旧 APK，改过画面后必须用 `Builds/Android/InkTurf.apk` 覆盖再校验。

## 2026-11-03 #14
【画质去廉价感（P31）】① 墨面读作「平涂粉漆」：物理正确的清漆 F0（0.035~0.065）在 40°~50° 俯视角下镜面贡献≈0 → `InkWetShading.hlsl` F0 抬到 0.09 并把 wet 权重并入 sheen；② 远景奶白 → 雾距 45/135 改 65/210；③ 阴影发飘 → sun shadowStrength 0.94、地面 `_ShadowFloor`/`_InkShadowFloor`/`_ContactShadowStrength` 加深、SSAO 升档；④ 后处理对比 16/饱和 14/曝光 0.12。Fixed: `RenderEnvGlobals.EnsureStyle` 编辑器 Play 下必失败（见 #16）。移动端画质分支未动。涉及 `Shaders/{InkGround.shader,InkWetShading.hlsl}`、`ScriptableObjects/ArenaStyleSO.asset`、`Settings/URP/PC_Renderer.asset`、`ArenaPostProfile.asset`。

## 2026-11-03 #13
【TapTap 隐私复检：同意前读取传感器列表】Fixed: C# 同意弹窗要等 Unity 引擎启动后才能画，而 Unity 框架在 `UnityPlayer` 初始化阶段就读了传感器列表 → 新增原生 `PrivacyActivity`（唯一 LAUNCHER，勾选同意后才显式拉起 `UnityPlayerActivity`），主清单用 `tools:node="replace"` 覆盖 `unityLibrary` 默认声明并去掉 MAIN/LAUNCHER；Unity 的清单处理器会把 `exported` 强制写回 true，由 `AndroidPrivacyManifestPostprocessor`（`IPostGenerateGradleAndroidProject`）压回 false。`PrivacyConsentDialog.HasValidConsent` 读原生 SharedPreferences 并镜像到 PlayerPrefs，避免二次弹窗。坑：`script.eval --code` 必须带结尾分号；GUI 编辑器 PlayMode 时 `File/Save Project` 不落盘，版本同步要在 batchmode 构建里做。涉及 `tools/android-privacy/`、`Assets/Plugins/Android/privacy-consent.jar`、`Scripts/Editor/AndroidPrivacyManifestPostprocessor.cs`。

## 2026-11-03 #12
【首页「检查更新」由大卡改成右下角小标签】第 4 张卡改「按键位置」（`MobileTouchUI.OpenLayoutCustomizer`）；检查更新复用 `CheckUpdateButton` 节点，文案随状态切换（无结果类 2.5s 复位，「新版 vX!」不复位）。坑：① 节点上已有 Image，直接 `AddComponent<UIInkShapeGraphic>` 会被 `DisallowMultipleComponent` 静默拒掉，背景必须走 `UIInkShapeGraphic.CreateChildBackground`；② 卡片标题只放 4 个字（移动端字号 ×1.28 后 6 个字会顶进 `》` 走廊）。涉及 `Scripts/UI/MatchFlowUI.cs`、`Docs/UI-GeometricBold.md`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-03 #11
【恢复应用内更新下载与安装】`UpdateManager.InAppInstallSupported` 由 false 改回 true，弹窗主按钮回到「立即更新 → 应用内下载 → 自动调起系统安装器」；manifest 重新声明 `REQUEST_INSTALL_PACKAGES` 与 `${applicationId}.fileprovider`。注意：TapTap 渠道会把该权限判为与游戏功能无关的高危权限（v0.1.14 曾因此被拒），`P1/构建/12` 自检必须保持通过。

## 2026-11-03 #10
【发布 v0.1.16】含 #07–#09（结算页极简化 + 战斗 HUD 通知重排 + 触控图标重做）；版本三方一致 0.1.16 / 116；APK `build/moshuizuozhan-v0.1.16-arm64-v8a.apk`（63.1 MB）。

## 2026-11-03 #09
【Fixed: 战斗 HUD 通知类 UI 文字溢出 / 重叠】① 最后 1 分钟横幅原 380×60 面板塞 407×74 文字 → 按「球顶 −70 + 球高 + 16」推导位置；② 手雷冷却徽标 220×36 三等分框装不下 → 胶囊 292×40/340×48，进度条吃剩余宽度；③ 击杀卡第二行按 `preferredWidth` 动态排布（移动端该行收 24 号），`KillFeedView` 移动端最多 3 张卡；④ 播报/横幅改 TMP 自动缩字。陷阱：`preferredWidth` 在写完 `font`/`text` 后即可读，但比较不同锚点（左锚 vs 右锚）的矩形必须先换算到同一局部坐标，否则出现「−80 vs 202」的假失败。涉及 `Scripts/UI/{CenterTimerView,WeaponReticleUI,KillCardView,KillFeedView,MatchCalloutView,MatchBannerView}.cs`。

## 2026-11-03 #08
【移动端触控键图标重做】射击/潜行/跳跃/手雷/大招与摇杆把手改用 `MobileControlGlyphFactory` 程序化几何字形，替换实心圆点、规则说明插画与手柄图标；图标仍走 `Image.color` 染色（按下翻墨黑不变）。陷阱：动作键图标不能复用 `UIIconLibrary` 的 Rules/Weapons 插画——那套是深描边 + 内部细节线的说明图，缩到 60~80px 会糊成一坨，动作键需要单一粗剪影。Inspector 显式赋值的 Sprite 仍优先。涉及 `Scripts/UI/Core/MobileControlGlyphFactory.cs`、`Scripts/UI/MobileTouchUI.cs`。

## 2026-11-03 #07
【结算页改极简中文版：比分 + 个人战绩】移除 4v4 明细台账、勋章 chips、胜差贴纸、拔河覆盖率条与背景装饰；判定大牌改「胜利 / 失败」，战报卡只留「涂地 / 击杀 / 阵亡 + 等级经验」。`MatchAwards` 纯逻辑保留但结算页不再消费。涉及 `Scripts/UI/MatchFlowUI.cs`。

## 2026-11-03 #06
【发布 v0.1.15】含 #02–#05（手雷准星落点投掷 + 结算大字重影/隐私弹窗修复）；版本三方一致 0.1.15 / 115。陷阱：`tools/run_tests.sh all` 的 PlayMode 卡死阈值是 90s，而本项目 PlayMode 实测约 140s，脚本会误判「运行器卡死」并取消重跑——判定回归前直接跑 `"$U" command test run --mode PlayMode --async_tests` 再轮询 `test status`。

## 2026-11-03 #05
【手雷改为「按住瞄准 → 松手按准星落点投出」】Fixed: 旧实现把相机 pitch 直接映射成出手俯仰，准星指 15m 外时手雷只落 6~8m。改为按屏幕中心射线命中的世界点做弹道反解：`ResolveThrowTarget` 把准星点钳制到 `bombMaxThrowDistance`（默认 22m），`ComputeThrowVelocityToLandAt` 用近 0.5s/远 1.2s 插值飞行时间反推初速；预览与实飞共用同一初速；`SubBombThrowArbiter.Advance` 新增按住瞄准/松手投状态机。陷阱：菜单同步配置时若刚保存过 `.cs`，`script.recompile_status` 可能报旧程序集的 completed——先 `script.eval` 读一个新常量确认程序集已更新。涉及 `Scripts/Weapons/{InkBombTrajectoryUtility,WeaponController}.cs`、`Scripts/Player/{SubBombThrowArbiter,SubWeaponThrowTrajectory,PlayerController}.cs`。

## 2026-11-03 #04
【Fixed: 结算页 VICTORY / DEFEAT 大字「重影」】根因：`MatchFlowUI.TriggerVictoryBadge` 里 `victoryBadgeText.color = fill` 把 `UIChunkyText` 的底层描边/硬影层也染成填充色，两层同色错开 4px 就读成双字。填充色只走 `UIChunkyText.SetFillColor`，并在其内加护栏（双层模式下无论外部怎么染底层，一律拉回 `outlineColor`）。涉及 `Scripts/UI/{MatchFlowUI,Core/UIChunkyText}.cs`。

## 2026-11-03 #03
【移动端射击键图标改实心圆点】由具象武器小图（`weapon_shooter`）改为功能色实心圆点（复用 `_circleFillSprite`）；`fireIcon` 字段与加载逻辑保留以便回退。涉及 `Scripts/UI/MobileTouchUI.cs`。

## 2026-11-03 #02
【Fixed: 隐私同意弹窗正文与链接/勾选/按钮重叠】正文框写死 880×260，而 TMP 实测渲染约 292（设备端字体回退换行更多），TopLeft 溢出绘制压住下方控件。改为运行时先按 `preferredHeight` 实测高度，标题/链接/勾选/按钮自上而下链式排布并回填面板高度。陷阱：`script.recompile_status` 报 completed 可能只是自动刷新在编辑中途编译的旧程序集，进 PlayMode 前务必 `unity recompile` 并核对 `Library/ScriptAssemblies/P1.Game.dll` 时间戳。涉及 `Scripts/UI/PrivacyConsentDialog.cs`。

## 2026-11-03 #01
【发布 v0.1.14】含 #26–#36（TapTap 隐私合规 + WebGL 网页试玩 + 手感与观感修复）；版本三方一致 0.1.14 / 114。陷阱：上一个会话已 bump 到 0.1.14 并出包但未提交，本次直接复用该 APK（`release.py build` 经 aapt2 包内校验通过）。

## 2026-11-02 #36
【首页 Hero 卡补品牌标题群 + 键帽文案紧凑化】标题群在武器整备卡与 CTA 之间偏上排布（陷阱：CTA 上方 36.8px 是 `HeroKeyHint` 常驻区，标题底限必须扣掉它）；`HeroKeyHint` 16px 违反规范下限改 18px。Fixed: 键帽文案超宽截断 → `DesktopBindingSlot` 新增 `GetCompactDisplayName()`（「鼠标右键→右键」），完整名 `GetDisplayName()` 保留给改键面板（`DesktopKeyBindingsTests` 断言不可改）。涉及 `Scripts/UI/{MatchFlowUI,HomeInfoContent}.cs`、`Scripts/Core/DesktopKeyBindings.cs`。

## 2026-11-02 #35
【场景观感精修档 P32（菜单 `P1/画风/7`）】在 P31 基线上叠加：云色灰紫→近白；`skyGround` 由 slate 灰绿提至水天线色系（根因：240m 水面之外的露底区渲染天空盒 `_GroundColor`，在水天交界形成脏带）；水面波幅 1.0→0.8 / strength 0.35→0.22 / glitter 0.72→0.45·阈值 0.988 / foam 0.65→0.35（根因：240m 大水面密铺细碎亮斑 + 掠射角反射，远看像满屏白色乱码）；`waterReflectionStrength` 0.8→0.5。陷阱：并行会话 PlayMode 中调用该菜单会在末尾 `SaveScene` 抛错、已 SetDirty 的 SO/材质不落盘。回退跑 `P1/画风/5` 回 P31 基线。涉及 `Scripts/Editor/{ToonStyleInstaller,ArenaAtmosphereBuilder}.cs`、`ScriptableObjects/ArenaStyle.asset`。

## 2026-11-02 #34
【角色生动性：受击顿挫 + 视线灵动（P36）】均为纯增量叠加：① 受击顿挫订阅 `CombatantHealth.DamageApplied`（该事件是 static，OnEnable/OnDisable 必须配对退订），按「攻击者→我」方向给俯仰/滚转注冲量；② 视线灵动——眼球是装配期按眼合并的单网格，只能在深色眼罩内整体微移（偏移钳 ≤4mm），只写 `localPosition` 且基于装配期缓存默认位置，与眨眼的 `localScale` 互不干扰。陷阱：新增 SO 字段靠反序列化保留字段初始化器默认值（YAML 缺失字段不回填 0），无需手改 `.asset`；敌墨 DOT 走 `ApplyEnemyInkBurn` 不触发 `DamageApplied`，不会被刷屏。涉及 `Scripts/Player/{CharacterVisualController,CharacterAnimConfigSO}.cs`。

## 2026-11-02 #33
【WebGL 全黑 / 粉色方块综合修复】① 角色接触阴影缺材质属性块回退成粉块 → `ContactShadow.shader` 把 `_Color` 纳入 `UnityPerMaterial` 并统一 target 3.0；② 所有着色器含 `#pragma target 4.5` 在 WebGL2（只支持 SM 3.0/GLES3）被 Unity 判不支持 → 整套材质回退 URP Lit，而各材质 `_BaseMap` 为空 → 采样未绑定纹理返回纯黑、物体变全黑剪影（统一 target 3.0，WebGL 端直接取 `_BaseColor`）；③ `ObstacleDitherLit` 盒体边界描线因法线分量坐标恒 ±0.5 导致到棱边距离恒 0，把表面全涂黑 → 排除法线轴；④ 非编辑器独立/WebGL 包里拿不到 `ArenaStyleSO` 导致环境光回退深炭黑 → 改回日间海港天色。修复严格限定在 WebGL 条件内。涉及 `Shaders/*.shader`、`Resources/{ContactShadow,InkBody,InkVfx}.shader`、`Scripts/Core/{WebGLRuntimeCompat,RenderEnvGlobals}.cs`。

## 2026-11-02 #32
【Fixed: 顶部战况条 YOU 角标顶出屏幕】移动端 24pt 粗体「YOU」实测约 62px，比 `TopRosterBarView` 写死的 52px 角标更宽，TMP 默认换行把单词拆两行（高约 64px）悬在 84px 槽上方，被安全区裁掉。修复：`textWrappingMode = NoWrap`，角标宽高按 `GetPreferredValues` 实测 + 边框/内边距推导，顶部下沉量改为「12px 内边距 + 4px 间隙 + 实测角标高」。涉及 `Scripts/UI/TopRosterBarView.cs`。

## 2026-11-02 #31
【Fixed: 生命值与 HP 徽章双层重影】`UIChunkyText.doubleLayer` 序列化默认 true，`AddComponent` 触发 `Awake` 即生成子节点 `ChunkyFill`；后续 `Configure(doubleLayered: false)` 虽置标志但未停用该子物体，`Update` 仍每帧同步文本并施加像素偏移。修复：`ApplyStyle`/`Update`/`Build` 在 `!doubleLayer` 时显式 `SetActive(false)` 并跳过偏移同步。涉及 `Scripts/UI/Core/UIChunkyText.cs`、`Scripts/UI/HudController.cs`。

## 2026-11-02 #30
【Fixed: 手雷投掷俯仰符号修正】`PlayerController.ComputeBombThrowVelocity` 把 `TopDownCameraFollow.PitchDegrees`（正数 = 低头）直接喂给 INKWAVE 约定（正数 = 抬头）的工具，两套符号相反 → 朝天瞄被解成 −17.2° 下砸、落点约 3m。新增 `CameraPitchToAimPitchDegrees` 显式翻符号。涉及 `Scripts/Player/PlayerController.cs`、`Scripts/Weapons/InkBombTrajectoryUtility.cs`。

## 2026-11-02 #29
【敌墨减速手感 1.9 → 2.4 m/s + INKWAVE 独立陷入曲线】4v4 实战踩敌墨像被钉住。`ComputePlanarVelocity` 新增可选 `inEnemyInk` 参数（保留旧签名）在敌墨中改走起步/衰减 30，超速滑行读 `enemyInkDecel`；DOT 20/s 与 60 HP 保护底线不变。陷阱：`PlayerConfigSO` 新增字段后必须同步 `PlayerConfig.asset`，否则运行时仍用旧序列化值。涉及 `Scripts/Player/{PlayerConfigSO,InkCharacterMotor}.cs`、`ScriptableObjects/PlayerConfig.asset`。

## 2026-11-02 #28
【TapTap 隐私合规修复】根因：APK 携带未使用的 Unity Purchasing（`com.android.vending.BILLING`）与 Analytics 依赖，工程无隐私政策/首启同意链路，还声明了高危 `REQUEST_INSTALL_PACKAGES`。修复：`Packages/manifest.json` 移除三者（无代码引用，APK 66→60MB）；manifest 删安装权限与 FileProvider；新增 `PrivacyConsentDialog`（非编辑器构建首启弹窗，未同意前 `CheckForUpdate` 直接返回）+ `site/{privacy,terms}.html`。

## 2026-11-02 #27
【介绍页接入网页试玩 `/play/`（Unity WebGL）】WebGL 包放 `/play/`，入口用新标签打开（不用 iframe）；手机打开 `/play/` 显示下载 Android 版提示（不做 WebGL 触屏适配）。陷阱：Pages 不能设 `Content-Encoding`，必须开 Brotli 解压兜底；构建用哈希文件名避免 CDN 缓存错配；`deploy_site.py` 加保护（线上已有 `/play/` 而本轮产物没有时拒绝部署，避免只改文案把试玩删掉）。涉及 `tools/{build_site,deploy_site}.py`、`Assets/WebGLTemplates/InkTurf/index.html`。

## 2026-11-02 #26
【WebGL 构建入口 + 兼容修复】新增 `BuildAndProfilerSuite.BuildWebGL`（菜单 `P1/构建/14`）。Fixed: ① WebGL 玩家包会剥掉自定义 Pass 名，`Material.FindPass("InkAtlasPaint"/"InkAtlasStamp")` 恒 −1 → `InkSplatAtlas` 按固定索引 3/4 回退（否则地面不显示墨迹）；② `GameObject.CreatePrimitive` 隐式创建的 `SphereCollider` 被 code stripping 剥掉（炸弹/风暴云/角色球体报 class doesn't exist）→ 新增 `Assets/link.xml` 保留 `UnityEngine.PhysicsModule`。WebGL 用 FXAA Low 替代 SMAA High。涉及 `Scripts/Editor/BuildAndProfilerSuite.cs`、`Scripts/Ink/InkSplatAtlas.cs`、`Assets/link.xml`。

## 2026-11-02 #25
【发布 v0.1.13】含 #16–#24（「新手」难度 + 设置页重排 + 更新弹窗修复 + 桌面图标/窗口名统一）；版本三方一致 0.1.13 / 113。发版前按 `P1/画风/漫画风/0` → `P1/画风/几何风/1` → `P1/构建/11` 重跑接线。

## 2026-11-02 #24
【低难度给本地玩家加最大生命】新增 `Player/PlayerDifficultyMath`：新手档玩家最大生命 ×1.5、轻松 ×1.25、普通/硬核 ×1.0；`CombatantHealth` 增加实例级 `SetMaxHealthScale`（按当前血量比例缩放，切难度血条百分比不跳变；复活/重开以新 MaxHealth 满血）。**只改本地玩家**，Bot 与队友保持基准血量。涉及 `Scripts/Player/{PlayerDifficultyMath,CombatantHealth,PlayerController}.cs`。

## 2026-11-02 #23
【系统层应用名去掉 Demo】`PlayerSettings.productName` 仍是 `InkTurfDemo`，导致安卓图标名、桌面窗口标题与 PlayerPrefs/persistentDataPath 目录都带 Demo。出包与场景构建前置改为写入 `GameVersion.APP_NAME`，固定产物名改 `InkTurf.app/.apk/_Dev.app`（`release.py` 候选表保留旧名兼容历史产物）。注意：`productName` 变更会换掉 macOS/Windows 的 PlayerPrefs 与 persistentDataPath 目录（安卓按包名不受影响）；包名不变避免更新断链。涉及 `ProjectSettings/ProjectSettings.asset`、`Scripts/Editor/{ProjectScaffolder,BuildAndProfilerSuite}.cs`。

## 2026-11-02 #22
【介绍页接入宣传视频】AV1 1080p 主源 + H.264 720p 兜底（源片 HEVC，H.264 CRF20 反而涨到 43MB，最终 `libsvtav1 -preset 6 -crf 38` 得 18.7MB）；Hero 视频框改 20:9，海报由 `build_site.py` 从视频第 3 秒抽帧生成。陷阱：`play()` 在无用户手势的自动化环境会被自动播放策略拒绝，验收需 CDP 真实鼠标事件 + `--autoplay-policy=no-user-gesture-required`。涉及 `site/`、`tools/build_site.py`。

## 2026-11-02 #21
【新增「新手」难度】四档重排 0 新手 / 1 轻松 / 2 普通 / 3 硬核（默认仍普通）。Fixed: 旧实现只缩放 `BotConfigSO.sightRange`，而 `BotTactics.SightRange` 用「武器射程 + 余量」兜底 → 倍率对大多数枪完全无效。新增 `BotConfigSO.sightRangeScale`，`BotController.FindNearestEnemy` 把最终索敌半径再乘它。存档迁移：`P1_Settings_BotDifficultyVersion`（旧三档整体 +1，避免老玩家的「轻松」被解释成「新手」）。涉及 `Scripts/AI/{BotDifficultyMath,BotConfigSO,BotController}.cs`、`Scripts/Core/{GameSettingsMath,GameSettingsService}.cs`。

## 2026-11-02 #20
【系统设置页重排：分组栅格 + 小圆形滑条手柄】Fixed: ① 滑条 Fill 的 RectTransform 用点锚 + offset 把高度压成 0，黄色进度从未渲染 → 改成 26 高轨道内缩 3px 的 fillArea；② Slider 会把 handleRect 锚点拉满整条轨道（44×44 方块实际渲染成竖条）→ 改零宽 HandleSlot 接管锚点 + 26×26 Pill 圆手柄作为居中子物体（整条 64 高滑条仍是拖动热区）。涉及 `Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-02 #19
【Fixed: 更新弹窗进度条恒满格】Fill 是裸 `Image`（`sprite == null`）时 `fillAmount` 被 Unity 忽略——这是「一直全绿且像没有动画」的根因。改用 `UIFlatSprite.MakeFlat(filled:true)` 补 1×1 白图，并按帧指数逼近目标值（unscaled）。更新说明新增 `UpdateNotesMarkdown`（原文尖括号转 `noparse` 防伪标签改色）。涉及 `Scripts/UI/{UpdateDialog,UpdateNotesMarkdown}.cs`。

## 2026-11-02 #18
【介绍页战场截图精简为两张】移除「4v4 对局 HUD」「湿墨质感」，`build_site.py` 同步不再生成对应 webp。涉及 `site/index.html`、`tools/build_site.py`。

## 2026-11-02 #17
【介绍页去掉版本号展示与 FAQ】`deploy_site.py` 的脏工作树检查从「拒绝部署」改为「仅警告」——部署走独立 worktree 不动 main，而正常流程下 CHANGELOG 往往就是未提交状态，原逻辑会把部署拦死。涉及 `site/`、`tools/deploy_site.py`。

## 2026-11-02 #16
【游戏介绍页上线 GitHub Pages】新增 `site/` 纯静态介绍站（视觉沿用游戏内几何大胆风），`tools/build_site.py` 拷截图转 WebP、注入版本号、子集化 MiSans、注水令牌进 `site/dist/`；`tools/deploy_site.py` 用独立 worktree 建孤儿 `gh-pages` 分支 force-with-lease 推送（主分支不提交）。陷阱：本机 git 的 `worktree add --orphan` 是旧式无参形式，必须写 `--orphan -b gh-pages <path>`；`Logs/` 被 gitignore，换机器构建会缺图。涉及 `site/`、`tools/{build_site,deploy_site}.py`。

## 2026-11-02 #15
【发布 v0.1.12】含 #09–#14（App 图标与启动动画 + Bot 三档难度 + 墨幕开场 + 更新安装旧包/宽屏空场/角签溢出修复）；版本三方一致 0.1.12 / 112。

## 2026-11-02 #14
【Bot 难度三档（轻松 / 普通 / 硬核）】参考 INKWAVE `config.js` 的 `DIFFICULTY`：只缩放 Bot 的反应延迟/瞄准误差/弹簧刚度/甩枪转速/感知半径/撤退血线/炸弹率，状态机三档共用；「普通」是恒等档——`BotDifficultyMath.CreateRuntimeConfig` 直接返回 `BotConfig.asset`（不克隆、不产生任何行为变化）。坑：编辑期 `Configure` 不克隆，否则 ProjectScaffolder 会把运行时 SO 引用写进场景；PlayMode 断言改用布局坐标（入场 `UISpringMotion` 只改 `localScale`，按世界角点测量会把入场中途的卡误算进间距）。涉及 `Scripts/AI/{BotDifficultyMath,BotConfigSO,BotController}.cs`、`Scripts/UI/{MenuLayoutMath,MatchFlowUI}.cs`。

## 2026-11-02 #13
【开赛取消 3-2-1 倒数，改单次墨幕入场】`MatchManager` 新增 `introSeconds`（默认 0.86s，负值回退旧倒计时）驱动 Countdown 阶段，且不再触发滴答音；`ScreenInkTransitionView` 新增 `MatchIntro` 模式（己方墨压满 → 停留 → 虹膜撕开，破口全开与 Playing 同帧）。陷阱：菜单/结算面板在 MatchFlow_Canvas（sortingOrder 10）、墨幕在 HUD_Canvas（0），必须在 PhaseChanged 里立刻隐藏面板，否则会闪一帧菜单盖在墨幕上。涉及 `Scripts/Core/MatchManager.cs`、`Scripts/UI/{ScreenInkTransitionView,ScreenInkTransitionMath,MatchFlowUI}.cs`。

## 2026-11-02 #12
【App 图标与启动动画】新增 `tools/bake_brand.py`（确定性烘 1024 主图标 + Android 自适应两层 + 启动画面 logo/贴纸）+ `BrandAssetInstaller`（菜单 `P1/构建/13`）写 PlayerSettings，接进出包链路；新增 `UI/LaunchIntroView`/`LaunchIntroMath` 运行时启动动画。陷阱：① `Art/Brand` 必须走 Default 纹理（Sprite 导入会把 maxTextureSize 钳到 512，1024 母版会废）；② Unity 6 的 `AndroidPlatformIconKind.Legacy/Round` 是 CS0619 硬错误，Android 只能写 Adaptive 两层；③ `SetIconsForTargetGroup(Standalone)` 必须给满 8 个槽位否则报错；④ 启动动画只在首场景存在 `MatchManager` 时创建。涉及 `tools/bake_brand.py`、`Scripts/UI/{LaunchIntroView,LaunchIntroMath}.cs`、`Scripts/Editor/{BrandAssetInstaller,MediaImportPostprocessor,BuildAndProfilerSuite}.cs`。

## 2026-11-02 #11
【Fixed: 首页宽屏中部空场与角签文字溢出】① `ResolveHomeBoard` 把 2×2 卡阵固定贴右、Hero 宽度钳在 940，20:9 真机多出的宽度全灌进列间（设计 20 → 实际约 146）→ 新增 `HomeRuntimeBoard.HeroX`，安全区比设计宽时整块水平居中、`ColumnGap` 恒定；整备卡 X 跟随 `HomeHeroCard` 实际左缘。② 角签黑底固定 140×44，移动端 26 号的 `SETTINGS` 实测约 193px 直接画出黑底 → 按 TMP 自然宽度自适应（只放大、上限为内容行宽）。涉及 `Scripts/UI/{MenuLayoutMath,MatchFlowUI}.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`。

## 2026-11-02 #10
【Fixed: 检查更新复用旧版本安装包】下载缓存固定写 `moshuizuozhan_update.apk`，`PopupUpdate` 只校验文件存在与 ZIP 魔数，上个版本留下的包会被当成本次更新直接调起安装器（Android 对降级/签名不符只回「应用未安装」）。改为按版本名缓存 + 同名 `.json` 清单（版本/字节数），`HasReadyApk` 同时校验版本、清单、文件大小与 APK 魔数；发现新版本时清理其它版本缓存。涉及 `Scripts/UI/{UpdateManager,UpdateDialog}.cs`。

## 2026-11-02 #09
【构建发布管线健壮性】Fixed: ① 改版本号后 Domain Reload 导致 `ensure_editor_version` 误判跳过预检、菜单触发失败后无重试死等（实测空耗 18 分钟）→ 先验 `script.recompile_status` + 3 次退避重试 + 持续不可达立即中断；② Python `urllib` 上传 68MB 内存开销大且代理下偶发 BrokenPipe → 优先系统 `curl` 流式上传（实测 28.5s）；③ `bump --write` 仅改 `GameVersion.cs` 导致 `ProjectSettings.asset` 漏步 → 自动联动 `bundleVersion` 与 `AndroidBundleVersionCode`。涉及 `.agents/skills/p1-release/release.py`、`Scripts/Editor/BuildAndProfilerSuite.cs`。

## 2026-11-02 #08
【发布 v0.1.11】含 #05–#07（桌面改键 + 7 类武器持枪动作 + DrawCalls 专项收敛）；版本三方一致 0.1.11 / 111。发版前按 `P1/画风/漫画风/0` → `P1/画风/几何风/1` → `P1/构建/11` 顺序重跑接线（新增改键文案的缺字会让 `FontCoverageTests` 两条预烘断言转红）。

## 2026-11-02 #07
【7 类主武器持枪动作 + 滚筒贴地闭环】引入纯函数求解层 `WeaponWieldSolver` 接管 6 类枪械动作（小枪前压回拉/蓄力狙后拉后仰/双枪交替后坐与翻滚侧倾/泼桶前摇后仰与甩出前倾等）。Fixed: ① 滚筒极端视角下滚轴底缘微悬空 → `ApplyRollerGroundContact` 增加装配期基准复位 + 闭环最终高度补偿（限幅 ±5cm）；② 双枪战术翻滚因 Motor 在 Update 中已面向位移方向导致 LateUpdate 侧向投影为零 → 触发瞬间锁定相对身体右方/前方的投影。另：`WeaponController` 设 `[DefaultExecutionOrder(50)]`、`CharacterVisualController` 设 100。涉及 `Scripts/Player/{WeaponWieldSolver,CharacterVisualController,ArmHoldPose}.cs`、`Scripts/Weapons/DualiesWeaponStrategy.cs`。

## 2026-11-02 #06
【渲染 DrawCalls 专项收敛】Fixed: ① 34 个墨面因逐帧写 MPB、且角色受击重置清空为「空 MPB」导致 GPU Resident Drawer 与 GPU Instancing 判定失效（均值 773.5）→ 波纹改全局 uniform 推送（`PushRippleGlobals`）、湿润度直接写共享材质、`CombatantHealth.ClearHitFlash` 改 `SetPropertyBlock(null)`；② 自研着色器缺 `UNITY_VERTEX_INPUT_INSTANCE_ID`/`UNITY_SETUP_INSTANCE_ID` 导致批处理阶段顶点矩阵丢失与地面洋红 → 7 款着色器补齐并按需把颜色抽为 `UNITY_DEFINE_INSTANCED_PROP`；③ DisableDomainReload 下静态合并网格缓存残留已销毁 Mesh → `ResetStaticCachesForPlayMode` 重置与有效性校验。实测均值 773.5→310.7、峰值 2282→836。涉及 `Shaders/*.shader`、`Resources/*.shader`、`Scripts/Ink/InkSurfaceController.cs`、`Scripts/Player/{CombatantHealth,CharacterVisualAssembler}.cs`。

## 2026-11-02 #05
【桌面端操作按键自定义】系统设置新增「按键设置」模态子面板，支持 9 项核心操作主键/备用键修改；`DesktopKeyBindingService` 接管键盘/鼠标按键检测与 PlayerPrefs JSON 持久化，支持冲突自动互换、备用键清空、Esc 取消监听与多级返回；`PlayerController` 输入读取解耦到服务层并保留触控与手柄分支；`HomeInfoContent.DynamicControls` 联动更新操作说明卡。涉及 `Scripts/Core/DesktopKeyBindings.cs`、`Scripts/Player/PlayerController.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-02 #04
【Fixed: 移动端大招就绪文案超宽】「大招就绪!」超出 148px 按钮宽度 → 就绪态沿用默认文案「大招」，颜色/实底高亮仍保留就绪强调。涉及 `Scripts/UI/MobileTouchUI.cs`。

## 2026-11-02 #03
【发布 v0.1.10】含结算页 v2 读数板/战绩台账、首页设置卡、双枪翻滚架枪与加特林移速倍率接入、墨迹图集 mip 重建与小地图脏矩形上传收敛、角色网格静态合并；版本三方一致 0.1.10 / 110。

## 2026-11-02 #02
【整体性能优化：墨迹上传尖刺 + 角色刚体合批 + 物理层瘦身】Fixed: 4096 墨迹图集每 0.25s 重建 mip 造成 15ms 级尖刺 → `atlasMipIntervalSeconds` 默认 0.5s（仍只在有新落墨时重建）；小地图 12Hz 全量 400×400 上传 → `InkGrid` 脏矩形 + 局部 `SetPixels32`（>25% 面积回退全量）；墨面/墙面 MPB 与环境全局推送节流；角色部件按「动画枢轴 × 材质」把 53 件刚体部件合成 30 个网格/角色。**关键陷阱：`InkCharacterMotor.EnsureStealthCaches` 的正常态会把缓存里的源 renderer 整套 re-enable，必须过滤已合并源（存在 `MergedStatic_` 时只收 enabled），否则双份渲染；运行时合并不能放 `Awake`——`CombatantHealth.ResetImmediately` 的 enable 会覆盖它。** 实测 avg 60.6 FPS（1% low 47.2）、DrawCalls 773.5（仍高于 120 红线）。涉及 `Scripts/Ink/{InkGrid,InkSurfaceController,InkSplatAtlas,InkWallSurface}.cs`、`Scripts/Player/{CharacterVisualAssembler,CharacterVisualController,InkCharacterMotor}.cs`、`Scripts/Core/{RenderEnvGlobals,GameLayers}.cs`。

## 2026-11-02 #01
【结算页 v2：墨色读数板 + 拔河覆盖率条 + 双队 4v4 台账】顶部 Hero 判定牌 + 胜差贴纸；中部两块深墨读数板 + 双端生长拔河条（白色分界线落在 A/(A+B) 占比点）；底部双队明细台账（武器图标 / 涂地 / K.O. / DOWN，YOU 行队色高亮）。Fixed: 44.1% 的「重影」根因是 `EnsureHardTextShadow` 给 Legacy `Text` 挂 uGUI `Shadow`（不透明黑副本），而 `ApplyWinnerEmphasis` 又把败方字色降到 alpha 0.6（灰字 + 黑副本）→ 删除该 Shadow 路径，百分比全部改纯平 TMP，败方降灰用不透明色。陷阱：明细行子元素坐标要先按「距列外侧边」定位再换算到行中心（pivot 0.5），左右列用同一公式取符号。涉及 `Scripts/UI/MatchFlowUI.cs`。

## 2026-11-01 #18
【首页音效卡升级为系统设置卡】AUDIO 卡（一键静音）改 SETTINGS 卡，信息行实时读「音量/震屏」并订阅 `GameSettingsService.SettingsChanged`。新增 `PauseMenuOverlay.OpenSettingsFromHome/CloseSettingsFromHome`（首页直开设置视图但不带对局暂停语义：`IsPaused=false`、不动 timeScale/AudioListener/BGM 不断）。陷阱：首页模式下 `_isSettingsOpen=true` 会让 `IsMenuExpanded` 报展开态，Esc 归属路由到 `TogglePause` 默认分支会误走 `Pause()` 弹出对局暂停主视图，必须走首页分支。涉及 `Scripts/UI/MatchFlowUI.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-01 #17
【武器抽屉桌面双列化】桌面抽屉原为 620 宽单列要滚动才能看完，改为与移动端同构的 `GridLayoutGroup` 双列（宽 620→1204 = 580×2 + 16 列间距 + 14×2 内缩）。陷阱：桌面 `Scroll*Inset` 是 14/14/14/48，与移动端 20/20/20/56 不同，按 20 算出的 1216 会被断言拦下。涉及 `Scripts/UI/MenuLayoutMath.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`。

## 2026-11-01 #16
【首页卡片区垂直居中】新增 `HomeMenuMetrics.CenteredBoardTop`（上下留白均分点），`ResolveHomeBoard` 的 `Top` 与 `HeroWeaponSlot` 的 Y 基准同源改用该值——两者必须同步，否则整备卡会脱离 Hero 卡。陷阱：后台驱动编辑器时保存 `.cs` 不触发自动 Refresh，需显式 `AssetDatabase.Refresh()`。涉及 `Scripts/UI/MenuLayoutMath.cs`。

## 2026-11-01 #15
【Fixed: 顶部比分条四边黑框忽有忽无】`CenterTimerView.ApplyScoreBarVisuals` 每帧按占比更新填充条 x 锚点时把 y 锚点写死成 1f/0f，覆盖了 `BuildHierarchy` 的 0.12~0.88 四边留框设计。把内缩提为常量 `FillInsetX/FillInsetY`（0.015/0.12），每帧只重写左右分界。涉及 `Scripts/UI/CenterTimerView.cs`。

## 2026-11-01 #14
【双枪翻滚/架枪与加特林移速倍率接入】`PlayerController` 接入双枪人形移动/开火中按跳跃触发战术翻滚，`InkCharacterMotor` 翻滚期间锁方向与速度（~9.33m/s）、架枪期间移速锁零（射速 +20%、0 散布），加特林开火移速乘倍率，Bot 交战也具备双枪拉扯；顺带收敛性能护栏（DrawCalls 850→920，消除激战 865 与静止 818 的假阳性）与贴地抖动容差（0.020→0.026m）。涉及 `Scripts/Player/{InkCharacterMotor,PlayerController}.cs`、`Scripts/Weapons/{MainWeaponStrategy,WeaponController}.cs`、`Scripts/AI/BotController.cs`。

## 2026-11-01 #13
【字体系统收口：全站 MiSans + 消除伪粗体 + 预烘字集账本】Fixed: `FontStyles.Bold` 在 TMP 里是 shader `_WeightBold` 膨胀，而基础字体本身已是 Rubik-Bold/MiSans-Heavy → 双重加粗、密笔汉字糊块；同时拉丁走 TitanOne/Rubik、中文走 MiSans，`ResolveFont` 按「含不含全角标点」整串换家族。主题四字段统一指向 MiSans-Heavy（标题）/ MiSans-Semibold（正文）。新增 `tools/font_charset.py` 生成字集/基线/豁免三份账本 + `FontCoverageTests` 三段账验收（并钉住「豁免字不上屏」）。陷阱：改采样点只能删资产重建（GUID 可能变），必须按 `P1/画风/漫画风/0` → `P1/画风/几何风/1` → `P1/构建/11` 顺序重跑接线，否则主题字体字段会变空。涉及 `Scripts/Editor/{ComicFontAssetBuilder,UIAssetTools}.cs`、`Scripts/UI/Core/UIThemeSO.cs`、`tests/EditMode/FontCoverageTests.cs`。

## 2026-11-01 #12
【Fixed: 首页 Hero 卡内「武器整备」按钮与 PLAY 标签叠字】根因：`WeaponSelectView` 在 `Awake` 中单次缓存 `_mobile` 且从未同步，触屏模拟/移动端布局切换时停在桌面端 Y 坐标（208），与移动端下移的 HeroTag（180~236）穿插重叠 28px。新增 `SyncHomeLayout()`/`RelayoutToggleButton()`，在 safeArea 变动、`_mobile != UITypography.IsMobileLike`、以及首页重建时同步重排。涉及 `Scripts/UI/MenuLayoutMath.cs`、`Scripts/UI/WeaponSelect/WeaponSelectView.cs`、`Scripts/UI/MatchFlowUI.cs`。

## 2026-11-01 #11
【Fixed: 墨迹图集单帧超 384 枚静默丢墨】`InkSplatAtlas.Advance/FlushImmediate` 旧实现把 `_pending` 全部搬入诊断队列后 `Clear()`，而 `BuildMesh` 只画得下 `MaxQuads(384)` 个 quad，超出部分永久不显示（玩法网格已计墨、画面却没墨）。改为 `PlanDrawBatch` 每帧按预算消费、剩余留队跨帧补画；队列改头指针 + 容量上限丢最旧并加 `PendingDroppedCount` 诊断。涉及 `Scripts/Ink/InkSplatAtlas.cs`。

## 2026-11-01 #10
【Fixed: Bot 大招位移错位 / 寻路顶墙 / 重开不清状态】① 三处 `SetFrameIntent` 把 `_weapon.IsPerformingSpecial` 误传进 `wantsJump`（大招真跳且可移动，冲击圈留原地）→ 改用 6 参并锁移动；② `FollowPath` 在 A* 失败时不再塞直线 goal，改连续失败退避（0.75→6s）+ `PickPaintTarget/PickRetreatPoint` 可达性校验与 12s 黑名单；③ 订阅 `CombatantHealth.OnRespawned` 完整复位路径、目标、计时与黑名单。涉及 `Scripts/AI/BotController.cs`。

## 2026-11-01 #09
【Fixed: 弹簧低帧率发散 / 空中踩墨 / 复活阶段 / 越界出生点】① `AnimSpring1D.Step` 半隐式欧拉在 30FPS（ω·h≈1）特征值模 >1 会指数发散、峰值随帧率漂移 → 改阻尼振子闭式解（欠/临界/过阻尼），30~240FPS 一致；② `InkCharacterMotor` 仅在确认贴地/短探地命中时采样脚下墨与允许潜墨（空中经过敌墨不再掉血减速、空中不再回墨/回血/隐匿）；③ `CombatantHealth` 复活计时仅 Playing 推进；④ `OutOfBoundsGuard` 优先各角色 `respawnAnchor`。涉及 `Scripts/Player/{AnimSpring1D,InkCharacterMotor}.cs`、`Scripts/Player/CombatantHealth.cs`、`Scripts/Core/OutOfBoundsGuard.cs`。

## 2026-11-01 #08
【Fixed: 武器输入抑制 / 泼桶跨波 / 爆破枪二次结算 / 终场冻结】① 暂停/终场/死亡/投雷时「没调用开火」被武器当成松手 → 新增 `WeaponController.SetTriggerSuppressed` + 策略 `OnTriggerSuppressed`（取消蓄力/扫射/推地/挥击前摇但不开火，恢复后不把仍按住的扳机误判成新按下）；② 泼桶改每波独立 `SlosherVolley`（修同目标 70+34 多段直击）；③ 爆破枪 `Explode` 后直接 return（消除二次涂墨/VFX）；④ 新增 `MatchManager.CombatSettlementAllowed` 冻结终场后的弹丸/手雷/风暴/光束/滚筒/大招结算（保留表现）；⑤ 滚筒加传送帧位移防抖与 `flickInterval` 挥击冷却。涉及 `Scripts/Weapons/{WeaponController,SlosherVolley,RollerWeaponStrategy}.cs`、`Scripts/Core/MatchManager.cs`。

## 2026-11-01 #07
【Fixed: UI 返回 / Esc 仲裁 / 低墨蜂鸣 / 横幅 / 抽屉 / Toast】① 结算「返回」旧接 `OnStartClicked` 会重开比赛 → 改 `MatchManager.ReturnToMenu()`；② 新增 `MenuInputRouter` 四步 Esc 归属仲裁（非归属层不占帧位，与 Update 顺序无关，首页详情/布局/暂停/抽屉不再同帧双动作），移动端走同一仲裁；③ `InkTankView` 自查宿主 Canvas 可见性（结算/首页不再按离场低墨值蜂鸣）；④ `CenterTimerView.ResetForMatch` 让第二局恢复「最后 1 分钟」横幅；⑤ 开赛统一 `CanStartMatch` 拦布局编辑/详情浮层；⑥ Toast 改用 TMP + 主题 MiSans（修中文 □□）。涉及 `Scripts/UI/{MatchFlowUI,Core/MenuInputRouter,InkTankView,CenterTimerView,UpdateManager}.cs`。

## 2026-11-01 #06
【Fixed: 首页次级卡 `》` 与信息行叠字 / 信息行被挤出卡面】根因：`BuildHomeTile` 把 `》` 画在底部信息行上、且只按 `1.2×箭头字号` 预留宽度——双书名号实际约 2em 宽，加上信息行 `NoWrap + Overflow` 会越过盒子继续画。现 `》` 独占标题行右侧走廊（`2.1×箭头字号 + 12`），信息行拿整行宽度；新增 `FitTileInfoText` 超宽时逐级缩字号（底线桌面 18 / 移动端 20），缩到底仍溢出才警告（**规范禁 Truncate，绝不截字**）。涉及 `Scripts/UI/MatchFlowUI.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-01 #05
【Fixed: 真机首页「开始游戏」CTA 消失、整局开不了】根因：`RebuildHomeVisuals` 在销毁旧 `MenuSafeArea` 前只把 CheckUpdateButton 搬出去，**StartButton 仍挂在安全区里被一并 Destroy**（`startButton` 变 null 后 `PlaceHomeCta` 直接 return，且 `Awake` 只绑一次的 `onClick` 随之消失）。真机必现：Android 启动时 safeArea/画布首帧未 settle，几何指纹检测到变化会重建一次首页。现重建前把 StartButton / CheckUpdateButton / CustomLayoutButton 一起搬回 `readyPanel`。涉及 `Scripts/UI/MatchFlowUI.cs`、`Tests/PlayMode/WeaponSelectPlayTests.cs`。

## 2026-11-01 #04
【发布 v0.1.9】Fixed: 首页重建后旧 MenuSafeArea 空壳不销毁，按名查找命中空容器导致安全区断言红（旧节点改名 `_Old` + Destroy 退役）。性能收敛：墙面涂墨改为每表面 1 合并网格 + 1 条带图集（战斗激活渲染器 316→~80）；Fixed: 武器静态合并 `_mergedMeshCache` 把瞬时失败缓存成 null 后永不重试（自 v0.1.3 静默失效）→ 失败组不落缓存 + 幂等守卫 + `CharacterVisualController.Awake` 运行时兜底。已知债：DrawCalls ~818 是场景结构性地板（8 角色 × ~73 部件逐件渲染 + 竞技场 + 已合并武器，UI/墙面≈0）；`WeaponFeelTests.SpringImpulse` 自 v0.1.8 即红（弹簧子步进过粗，见 #09）。涉及 `Scripts/Ink/{InkWallSurface,InkWallFace}.cs`、`Scripts/Player/CharacterVisualAssembler.cs`、`Scripts/UI/MatchFlowUI.cs`。

## 2026-11-01 #03
【首页「战绩 / 键位说明」信息卡改为可点 + 详情浮层】新增 `Scripts/UI/{HomeInfoOverlay,HomeInfoLayoutMath,HomeInfoContent}.cs`（`HomeInfoContent` 是键位表唯一数据源，暂停菜单 `BuildControlsCard` 改用它）。陷阱：浮层用独立 Overlay Canvas sortingOrder 50，必须高于武器抽屉 15，否则左下整备卡浮在遮罩之上；Esc 用「同帧已消费」标记保证一个 Esc 只关浮层。涉及 `Scripts/UI/MatchFlowUI.cs`、`Scripts/UI/Menus/PauseMenuOverlay.cs`。

## 2026-11-01 #02
【AGENTS.md 改为「项目导览」并建立 CHANGELOG 规范】去掉已结项的计划看板说明，把「项目简介 / 快速定位 / 目录结构 / 硬性规则 / 并行协作 / 验证 / 素材 / 变更日志 / 坑」收成一份可直接定位文件的入口。涉及 `AGENTS.md`、`CHANGELOG.md`（新）。

## 2026-11-01 #01
【发布 v0.1.8】结算勋章与段位成长 + 战况大字播报 + 角色动作/武器手感重做 + 码头光影精修；同轮修手机端丢雷与 Slam 无伤害。涉及 `Scripts/UI/Core/MatchAwards.cs`、`Scripts/Player/CharacterVisualController.cs`、`Scripts/Environment/ArenaStyleSO.cs`。

## 2026-10-31 #01
【发布 v0.1.7】手雷按住瞄准松手投出 + 击杀闪光/阵亡转场/踩敌墨警告 + 角色圆润造型与双腿步态。涉及 `Scripts/Player/{SubBombThrowArbiter,KidGaitSolver}.cs`、`Scripts/UI/InkEdgeWarningView.cs`。

## 2026-10-30 #04
【发布 v0.1.6】UI 几何大胆风重构 + 射击涂地手感重做 + 复活 3 秒无敌。涉及 `Docs/UI-GeometricBold.md`、`Scripts/UI/Core/*`、`Scripts/Weapons/*`。

## 2026-10-30 #03
【发布 v0.1.5】移动端 2/3/4 指方案与自由布局 + 竞技场碰撞收敛；同轮修栏杆/道具卡人、打不上墨、掩体背光死黑。涉及 `Scripts/UI/{MobileTouchUI,MobileLayoutCustomizer}.cs`、`Scripts/Player/InkCharacterMotor.cs`、`Scripts/Environment/ArenaGeometryBuilder.cs`。

## 2026-10-30 #02
【发布 v0.1.4】修地面墨迹不显示、移动端按钮失灵、墨水罐全黑；同轮修发布脚本（Gitee 附件清理按 `attach_files` 查 id、`http_json` 支持 3xx 重定向、版本预检防旧包白跑、长任务心跳与推送/上传并行）。涉及 `Scripts/Ink/*`、`.agents/skills/p1-release/release.py`。

## 2026-10-30 #01
【发布 v0.1.3】修移动端触控层遮挡首屏与结算界面、战前选枪按钮无法点击。涉及 `Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/MatchFlowUI.cs`、`Scripts/UI/WeaponSelect/*`。

## 2026-10-29 #03
【发布 v0.1.2】触控层遮挡首屏/结算与选枪卡片点不动的修复版。涉及 `Scripts/UI/MobileTouchUI.cs`、`Scripts/UI/WeaponSelect/*`。

## 2026-10-29 #02
【发布 v0.1.1】第三人称视角重构 + 战前 7 武器选择/配装 + 暂停设置 + 竞技场美术升级；角色改蛋形并补武器/潜墨音效。涉及 `Scripts/Player/TopDownCameraFollow.cs`、`Scripts/UI/WeaponSelect/*`、`Scripts/Environment/ArenaGeometryBuilder.cs`。

## 2026-10-29 #01
【发布 v0.1.0】首个可玩版本（4v4 涂地）+ 游戏内检查更新；建立 GitHub / Gitee 双源发布链。涉及 `Scripts/UI/{UpdateManager,UpdateDialog}.cs`、`Scripts/Core/GameVersion.cs`、`.agents/skills/p1-release/`。
