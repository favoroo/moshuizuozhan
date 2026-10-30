# fonts —— Inkwave 字体 → Unity 静态 TTF + TMP 字体资产

`inkwave-aah-research/assets/fonts/` 里给的是 Google Fonts 的 **woff2 latin 子集**，
Unity 与 TMP 都不认，所以要在这里转一道，产物落到 `Assets/_Project/Art/Fonts/`。

## 为什么必须转换（不只是换个容器）

| 坑 | 后果 | 处理 |
|---|---|---|
| Unity 的 `TrueTypeFontImporter` 不认 woff2 容器 | 拖进 Assets 也不导入 | 用 fontTools 解成 TTF（brotli 解压） |
| `Rubik-latin.woff2` 是**可变字体**（wght 300–900，默认实例是 **Light 300**） | TMP 只会取默认主实例 → HUD 文字细得看不清 | 实例化出 Regular 400 / Bold 700 / Black 900 三个静态字重 |
| 可变字体源里的 family 名被写成了默认实例的 `Rubik Light` | 实例化后产出 `Rubik Light Bold` 这种鬼名字 | 显式重写 name 表（family/subfamily/full/PostScript/OS-2 字重） |

字重取三档的依据：参考实现的 CSS 正文用 `Rubik` 400、强调用 700、
标题/标签用 `font: 900 'Rubik'`（街头涂鸦风的粗标签）。

## 产物

```
Assets/_Project/Art/Fonts/
├── TitanOne-Regular.ttf    标题/大字（Titan One 400，212 字符）
├── Rubik-Regular.ttf       正文（Rubik 400）
├── Rubik-Bold.ttf          强调（Rubik 700）
├── Rubik-Black.ttf         标题/标签（Rubik 900）
└── TMP/
    ├── TitanOne-Regular SDF.asset
    ├── Rubik-Regular SDF.asset
    ├── Rubik-Bold SDF.asset
    └── Rubik-Black SDF.asset
```

TMP 资产：SDFAA、采样 90pt、图集 1024²、**Dynamic** 人口模式，
预烘了 137 个字符（ASCII + Latin-1 + 常见排版符号）。
字体是 latin 子集，**不含 CJK**，也不含 `→←↑↓` 四个箭头（源字体就没有）；
要做中文 UI 时给 TMP 配 fallback 字体，或另找一套中文主字体。

## 生成

```bash
python3 tools/bake_fonts.py            # woff2 → 4 个静态 TTF
python3 tools/bake_fonts.py --list     # 看将产出的字重
# 再让 Unity 生成 TMP 字体资产（会顺带补齐 TMP Essential Resources）
"$HOME/.unity/bin/unity" command script.eval --code 'P1.Editor.UIAssetTools.CreateTmpFontAssets();'
```

TMP 资产是「字体图集 + 材质 + 元数据」的复合资产，只能由编辑器脚本生成，
所以放在 `Assets/_Project/Scripts/Editor/UIAssetTools.cs`（菜单 `P1/素材/4.`）。
图标与字体的体检走同目录的 `P1/素材/5.`（或 `UIAssetTools.ReportUIAssets()`）。

## 许可

Titan One 与 Rubik 均为 **SIL Open Font License 1.1**（Google Fonts 发行版），
允许随游戏分发与嵌入；OFL 要求保留版权声明、且不得单独售卖字体本身。
TMP 生成的 SDF 资产是派生物，同样按 OFL 处理。

## 并发冲突留档（2026-10-28）

导入期间有另一实例往 `Art/Fonts/` 落了 3 个**未实例化的可变字体解码件**
（`Rubik.ttf`、`Rubik-Light.ttf`、`TitanOne.ttf`）并据此生成了 3 个同名 TMP 资产。
可变字体在 Unity/TMP 里会按 Light 300 主实例渲染，是错的效果；最终收敛时这些文件已被清除，
`Art/Fonts/` 现在只剩 4 个静态 TTF + 4 个 TMP 资产。同源文件可由
`python3 tools/bake_fonts.py` 一键重建，没有备份损失。

## MiSans 中文字体（2026-10-30 起替换站酷两款）

- **来源**：小米官方 MiSans 全套（免费商用，含应用/游戏内嵌授权；协议原文见
  <https://hyperos.mi.com/font/>，官方下载包内附《MiSans 字体知识产权许可协议》）。
  本目录存放用户提供的**完整原版** `MiSans-Semibold.woff2` / `MiSans-Heavy.woff2`
  （各 29571 字符 / 20976 汉字）。**不要**用其他项目（如 Godot `t-1/fonts/`）里的
  subset 版——那是按彼项目文案裁剪的，仅 ~3783 汉字，本项目覆盖率自检会缺字。
- **转换**：`python3 tools/bake_fonts.py`（与 TitanOne/Rubik 同一条管线；
  MiSans 每档字重是独立静态文件，无需可变字体实例化）。
- **产物与分工**：`Assets/_Project/Art/Fonts/MiSans-Semibold.ttf`（650，UI 正文）/
  `MiSans-Heavy.ttf`（900，标题/大字显示），TMP 动态图集资产由菜单
  `P1/画风/漫画风/0`（`ComicFontAssetBuilder`）生成并接管 fallback 链。
- **授权留档**：`Assets/_Project/Art/Fonts/MiSans-LICENSE-NOTE.txt`（协议禁止字体文件
  单独分发或售卖；游戏内嵌使用免费。正式发版前建议把官方协议 txt 落进 Fonts 目录）。
- **前任字体**：站酷快乐体 / 站酷庆科黄油体（OFL），其 TTF 原件与 `ZCOOL-OFL.txt`
  仍在本目录留档，回滚只需还原 `ComicFontAssetBuilder` / `ComicStyleInstaller` 的指向
  并重跑菜单 0 / 1。