# INKWAVE (Turf Riot) 逆向工程与技术调研资产库

本项目为对 Web 版仿喷射战士游戏 [INKWAVE (Turf Riot)](https://inkwave-aah.pages.dev) 的完整技术解构、资源提取与对我们俯视角涂地对战游戏（P1）的参考调研库。

---

## 目录结构说明

```
inkwave-aah-research/
├── README.md                          # 本说明文档
├── reports/
│   └── RESEARCH_REPORT.md             # 深度调研与落地参考报告（核心！）
├── assets/
│   ├── icons/                         # 68 个纯矢量 SVG 图标（武器、技能、UI、键位、规则）
│   ├── stages/                        # 16 张关卡地图高清预览原画 (WebP 格式，日景/黄昏，全分辨率及缩略图)
│   ├── lightmaps/                     # 关卡烘焙光照贴图 (PNG + JSON)
│   ├── fonts/                         # 字体资源 (TitanOne, Rubik)
│   └── audio/
│       └── sfx-catalog.json           # 94 个程序化合成音效的参数元数据与配方表
├── styles/
│   ├── ui.css                         # 游戏全局 UI 样式与动画
│   └── hud.css                        # 战斗内 HUD、小地图、准星与计量槽样式
└── src/                               # 完整未混淆的前端 ES6 源代码 (74个模块)
    ├── config.js                      # 核心数值调优、武器属性、玩家物理、地图配置
    ├── main.js                        # 游戏主循环、流程控制与装配入口
    ├── core/                          # 渲染管线、输入响应、事件总线
    ├── world/                         # 关卡几何、Atlas 涂地系统、着色器、材质
    │   ├── paint.js                   # 涂地系统核心 (GPU Atlas + CPU 0.25m 网格)
    │   ├── levelMaterial.js           # 关卡 PBR 材质
    │   ├── inkShading.js              # 墨水表面着色 (三次 B 样条、高程法线、波纹)
    │   └── maps.js                    # 4 套关卡几何布局定义
    ├── game/                          # 物理、控制器、武器弹丸、Bot AI、小地图
    │   ├── weapons.js                 # 7 类主武器 + 副武器 + 大招的弹丸物理与开火逻辑
    │   ├── actor.js                   # 角色物理控制器 (潜墨、爬墙、回血回墨、位移积分)
    │   ├── bots.js                    # 人性化 Bot 状态机 (二阶瞄准弹簧、寻路、走位)
    │   ├── nav.js                     # 3D 导航图与 A* 寻路
    │   └── minimap.js                 # 2D 分层小地图渲染器
    ├── ui/                            # HUD、菜单、矢量图标
    ├── audio/                         # Web Audio 全程序化音效与音乐合成器
    └── boss/                          # 单人 Boss 战模式
```

---

## 快速导航

- **完整调研报告**：请查阅 [reports/RESEARCH_REPORT.md](reports/RESEARCH_REPORT.md)。
- **武器与技能图标**：见 [assets/icons/](assets/icons/)，可直接在浏览器或矢量编辑工具中预览。
- **音效配方清单**：见 [assets/audio/sfx-catalog.json](assets/audio/sfx-catalog.json)，可供 P1 的 `tools/bake_sfx.py` 作为波形与包络合成参考。
