# 检查清单：首页地图选择卡片 + 真实切图

- [x] 首页第 3 张卡是地图卡，显示当前图名 + 程序化俯视预览 + 另一张可选项，文案不截断
- [x] 地图浮层默认选中当前图（红边框 /「当前」角签 /「当前地图」按钮），另一张显示「切换到此图」
- [x] 选择写入 `PlayerPrefs("P1.Map.SelectedId")`，退出 Play 重进仍恢复选中图
- [x] 首次进入（无存档）默认 PhaseA，且不会自动写盘改变老玩家体验
- [x] 切换真实加载对应场景（`SceneUtility.GetBuildIndexByScenePath` + `LoadScene(Single)`，无硬编码场景名）
- [x] 两张图各自进入 Playing 阶段，画面可辨（PhaseA 中央方形高台 / Kelp 中央长货台 + 信标）
- [x] 墨格、边界、出生点、NavGrid 随场景切换（均随场景内组件重建，实测两图可正常对局）
- [x] 「操作指南」仍可达：暂停设置页「画面与操作」→「操作说明 / 查看指南」，浮层压在设置页之上
- [x] UI 字号全部 ≥18px，颜色只走 `ComicStyle` / `UIThemeSO` / 地图 Accent，移动端安全区钳制
- [x] 浮层支持 Esc / 点击空白 / 关闭键退出，且不会让首页开赛快捷键误触发
- [x] `tools/run_tests.sh fast` 全绿（EditMode 737、PlayMode UI 71，失败 0）
- [x] 字库账本更新 + 菜单 `P1/画风/漫画风/0` 预烘，字体门禁转绿
- [x] `CHANGELOG.md` 追加 `## 2026-11-04 #37`
- [x] 未执行 `git commit` / `git push`，工作区他人改动保持原样
