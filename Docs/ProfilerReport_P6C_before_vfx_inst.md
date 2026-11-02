# P1 性能采样报告 [P6-C-R]

> **生成时间**: 2026-11-02 08:40:32
> **运行环境**: Unity Editor PlayMode · 平台 OSXEditor · GPU Metal (Apple M1) · 设备 MacBookPro17,1 · CPU 8 核 (Apple M1) · 内存 16.0 GB · 分辨率 1845x1121 · targetFrameRate 60 · Unity 6000.6.7f1
> **数据来源**: 真实 PlayMode 战斗逐帧实测（Time.unscaledDeltaTime + ProfilerRecorder + InkSurfaceController 上报），无任何仿真或写死数值

## 采样结果
### Profiler 性能测试报告：Editor PlayMode 实测 · MacBookPro17,1
- **运行环境**: Unity Editor PlayMode · 平台 OSXEditor · GPU Metal (Apple M1) · 设备 MacBookPro17,1 · CPU 8 核 (Apple M1) · 内存 16.0 GB · 分辨率 1845x1121 · targetFrameRate 60 · Unity 6000.6.7f1
- **总采样帧数**: 1198 帧（运行时长：20.01 秒）
- **帧率表现 (FPS)**:
  - 平均 FPS: **60.4**
  - 1% Low FPS: **45.1**
  - 极值范围: 24.9 ~ 134.8 FPS
  - 说明: 编辑器 PlayMode 数值受帧率上限设置与编辑器自身开销影响，真机红线以 Player 采样为准
- **GC 内存分配 (GC.Alloc)**:
  - **[Recorder 实测] 平均每帧分配: 116404 Bytes/frame** (113.68 KB)
  - **[Recorder 实测] 单帧最高分配: 715523 Bytes** (698.75 KB)
  - [近似口径·托管堆差值] 平均每帧: 77567.6 Bytes/frame，单帧最高: 253952 Bytes，总计: 90748 KB
- **涂地上传 (GPU 图集提交 InkSplatAtlas.Execute + 小地图 CPU FlushTexture)**:
  - 上传次数: 1012 次
  - 平均上传耗时: **0.149 ms**
  - P95 耗时: **0.209 ms**
  - P99 耗时: **0.322 ms**
  - 单帧最高耗时 (Peak): **0.989 ms**
- **Draw Calls (ProfilerRecorder)**:
  - 平均每帧: **275.4**，峰值: **566**（红线：桌面 < 120 / 移动端 < 150）
- **指标综合达标判定**:
  - 帧率达标 (Desktop >= 60 / Mobile >= 30): PASS
  - GC.Alloc 趋零 (Recorder 实测 < 2KB/帧): FAIL
  - 纹理上传稳定 (平均 < 1.5ms 且 P99 < 1.8ms): PASS
  - DrawCalls (均值 < 120 桌面红线): FAIL

## 真机数据状态与红线口径
- 本表为 **编辑器 PlayMode** 数据：帧率受编辑器自身开销与「帧率上限」设置影响，
  桌面 60 FPS / Android 30+ FPS 红线 **必须以 Player 包（真机/本机运行 Development Build）实测为准**。
- Player 自动采样流程（`P1.Core.AutoProfileRuntimeService`）：
  1. 构建 Development Build（菜单 `P1/构建/8` 或 9，改传 `BuildOptions.Development`）；
  2. 放置请求文件（内容为采样秒数）：Android 例 `adb push profile_request.txt /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfile.request`；
  3. 启动游戏（预热 5s 后自动实测并结束），报告写入同目录 `P1_AutoProfiles/`；
  4. 回收：`adb pull /sdcard/Android/data/com.p1studio.inkturfdemo/files/P1_AutoProfiles/ Docs/`。
- 在获得 Player 实测数据前，本项目的移动端性能红线状态应记为 **未验证**。

## 历史数据作废说明
旧版 `Docs/ProfilerReport_P6C.md`（≤ 2026-10-30）由 `BuildAndProfilerSuite.SimulateBattleSampling` 的离线仿真生成（FPS 来自 `Random(42)`、平均帧率硬编码 60.1、DrawCalls 45~75 为写死文案、GC 0 B/帧测的是仿真循环），全部作废；其「验收圆满通过」结论不成立。旧版纹理上传判定使用 `Peak < 1.5 || P99 < 1.2` 的 OR 条件会掩盖单帧尖刺，已改为均值+P99 双条件。
