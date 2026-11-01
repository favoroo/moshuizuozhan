# icons —— Inkwave 矢量图标 → Unity 粗描边扁平精灵

`inkwave-aah-research/assets/icons/` 的 66 个 SVG 到这里之后就变成
`Assets/_Project/Art/Sprites/Icons/` 的 66 个 **128×128** PNG 精灵。
512 源稿不常驻（重烘 66 张约 2 分钟、产物确定性），需要时 `--size 512` 重新出。

## 为什么需要烘焙层

调研库的 SVG 是**彩色**的（深色描边 `#15121c` + 橙色主体 `#ff8a14` + 浅色高光），
而 P1 的图标必须是**白主体 + alpha**：两队颜色每局不同，颜色一律交给
Unity 的 `Image.color` 运行时染色（一套图标服务两队）。

顺带解决两个坑：

1. 27 个 SVG 把 `stroke-width` 写了两遍，标准 XML 解析器直接报错，
   脚本会先做属性去重（浏览器能忍，librsvg 不能）。
2. 直接把彩色图「保留覆盖度 + 涂白」会得到一坨白剪影：齿轮的孔、
   眼睛的瞳孔、枪身的分件全糊在一起。所以映射成**二值化 alpha**：
   深色描边/暗部 → 挖空，主体与高光 → 不透明纯白，边缘保留抗锯齿过渡
   （全库只有约 1.2% 的像素是半透明，就是 AA 边）。

## 128px 与粗黑描边（2026-10-31）

512 源缩到 20~48px 显示时会糊：内部细节线只剩亚像素宽，双线性把它们磨平，
图标看起来"灰、软、廉价"。现在按真实显示尺寸出片：

| 项 | 值 | 说明 |
|---|---|---|
| 输出边长 | **128** | 对视口里最大 85px 的图标有 1.5x 余量；要 512 源稿用 `--size 512` 重出 |
| 墨色描边 | **6px @128** | 只加在**外轮廓**外侧；`#15121C`，染色是 `Image.color` 乘法，墨色 × 任何队伍色仍是深墨色 |
| 透明边距 | 3px @128 | 描边不贴画布边，避免被裁 |
| 内部细节 | alpha 镂空 | 瞳孔、齿轮孔、分件仍然是真镂空，不画成墨块 |

读出来的层次是：**墨描边（外轮廓）/ 白主体（可染色）/ 镂空细节（透出底板色）**。
白主体仍是纯白 RGB，染色语义与旧版完全一致；关掉描边用 `--outline 0` 即可回到旧观感。

## 生成

```bash
brew install librsvg                 # 只需一次，提供 rsvg-convert
python3 tools/bake_icons.py          # 全量 66 个 @128 → Assets/_Project/Art/Sprites/Icons/
python3 tools/bake_icons.py --list   # 看映射表
python3 tools/bake_icons.py --preview MediaRaw/icons/contact_sheet.png   # 缩略图自查（中灰底）
python3 tools/bake_icons.py --size 512 --out-dir MediaRaw/icons/source_512   # 重建 512 源稿
```

产物是确定性的（同输入 → 同字节），随时可重烘，不消耗任何生成配额。
`contact_sheet.png` 用中灰底：纯白主体与墨描边在纯白/纯黑底上总有一层看不见。

## 分类归档

| 目录 | 前缀 | 数量 | 内容 |
|---|---|---|---|
| `Weapons/` | `weapon_` | 7 | 主武器：shooter / roller / charger / blaster / dualies / slosher / splatling |
| `Specials/` | `special_` | 2 | 大招：slam / storm |
| `Subs/` | `sub_` | 1 | 副武器：bomb |
| `Marks/` | `mark_` | 3 | 对局标记：death / splat / squid |
| `Rules/` | `rule_` | 4 | 玩法规则插画：climb / swim / enemy / turf（源图 120×80，居中补成方形） |
| `Glyphs/` | `glyph_` | 49 | 通用 UI 字形（返回、齿轮、时钟、准星等） |

合计 66。**原任务看板写的 68 是约数，调研库实际只有 66 个 SVG。**

## 命名对照

原始文件名 → 产物名的规则在 `tools/bake_icons.py` 的 `CATEGORY_RULES` / `NAME_ALIASES`：

```
WEAPON_ICONS_shooter.svg → Weapons/weapon_shooter.png
GLYPHS_gear.svg          → Glyphs/glyph_gear.png
DEATH_ICON.svg           → Marks/mark_death.png
RULE_ART_swim.svg        → Rules/rule_swim.png
```

## 并发冲突留档（2026-10-28）

导入期间检测到有另一进程同时往 `Icons/` 写了一套**不同分类命名**的产物
（`Status/`、`Special/`、`Sub/` 三个目录，6 个文件）。为避免两套 scheme 并存，
本任务保留 `Weapons/Specials/Subs/Marks/Rules/Glyphs` 为准，
把那一套原样移到 `MediaRaw/icons/_conflict_dup_scheme/`（没有删除，可追溯）。

## 使用约定

```csharp
// 图标是「白主体 + 墨描边 + alpha」，染色只改 RGB，alpha 原样保留：
//   - 描边像素是 #15121C，乘以任何队伍色仍是深墨色，描边永远可见；
//   - 主体像素是纯白，乘以队伍色得到队色实体。
icon.color = teamColor;            // Image.color
icon.preserveAspect = true;
```

不要给图标做「按队伍色预烘焙」的第二套图——那会把 66 张变成 132 张，
而且队伍配色一改就要全部重出。