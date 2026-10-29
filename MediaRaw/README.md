# MediaRaw —— 素材生成原稿区

本目录是 `media-gen` 技能的**底稿库**，不对游戏运行时负责。

## 为什么放在这里（而不是 Assets/ 里）

Unity 只扫描并导入 `Assets/` 下的内容。本目录在项目根、`Assets/` 之外，
所以原稿**天然不会被 Unity 导入**——不需要 Godot 那种 `.gdignore` 排除标记，
也不会有 `.meta` 噪音。

精选过的成品才拷进 `Assets/_Project/{Art,Audio}/`（见 `docs` 一节）。
**永远不要把整个 MediaRaw 拷进 Assets**：那会连带 `.raw.jpg`、草图、落选稿一起入库。

## 目录

```
MediaRaw/
├── style/STYLE.md     ★项目风格档案：生成任何素材前必读；为空则禁止生成正式素材
├── style/anchor.*     风格锚图、角色/道具设定图（风格锁定的参考图）
├── images/            生图原稿与透明 PNG（语义命名：ui_icon_inktank.png）
├── voice/             台词 WAV（命名：vo_<说话人>_<场景>_<序号>.wav）
├── music/             BGM（命名：bgm_<场景>_<情绪>.mp3 / .ogg）
├── sfx/               本地烘焙音效（tools/bake_sfx.py 产出，命名 <key>_<n>.wav）
├── icons/             SVG 图标烘焙记录 + contact_sheet.png（tools/bake_icons.py）
├── fonts/             woff2 字体转 TTF 记录（tools/bake_fonts.py）
└── manifest.jsonl     生成台账（脚本自动追加）：ts/kind/model/prompt/refs/output
```

`icons/` 与 `fonts/` 只放**说明与自查图**，真正的 SVG/woff2 源在
`inkwave-aah-research/assets/` 里（逆向调研库，不随游戏入库）。

## 先查台账再生成

生成本来就花配额，重复生成是纯浪费。**动手前先查**：

```bash
grep 关键词 MediaRaw/manifest.jsonl          # 已有相似素材？
python3 -c "import json;[print(json.loads(l)['output']) for l in open('MediaRaw/manifest.jsonl')]" | sort | uniq
```

已有素材优先**复用**或**当参考图编辑**（`-r` 传已有图 + 改动指令），不要从零重生成。

## 入库流程（原稿 → 游戏可用）

```bash
cp MediaRaw/images/ui_icon_inktank.png Assets/_Project/Art/Sprites/
# 然后让编辑器导入；导入设置由 MediaImportPostprocessor.cs 按路径自动套，不用手工点 Inspector
"$HOME/.unity/bin/unity" command script.eval --code 'UnityEditor.AssetDatabase.Refresh();'
```

导入后跑一次素材体检（编辑器菜单 `P1/素材/体检：列出已导入媒体与设置`，
或 batchmode 走 `-executeMethod P1.Editor.MediaTools.ReportMedia`），
确认设置符合预期、控制台 0 error 才算落地。

## 版本控制

- **入库**：`style/STYLE.md`、`manifest.jsonl`、`images/`、`voice/`、`music/`。
  语音与音乐是按配额生成的，丢了要重烧，所以原稿进版本库。
  `.raw.jpg` 也进——它是重抠透明底的唯一底稿（`keyout.py` 可反复重跑调阈值）。
- **忽略**：`sfx/`（自家 `tools/bake_sfx.py` 随时可确定性重烘，进去只是噪音）。
- 体量变大后按 Godot 项目的做法外迁到 `~/AssetsLibrary/` 并改用 Git LFS/忽略，
  届时改 `.gitignore` 并在本文件更新说明。