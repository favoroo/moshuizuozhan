# 核心工作流：风格锚定与一致性（生成任何美术素材前必读）

> 目标：让几十上百张生成图保持同一画风。原理 = **固定风格前缀 + 参考图** 双保险，
> 两者都登记在项目风格档案 `MediaRaw/style/STYLE.md` 里。

生成前固定三步：

1. 读 `MediaRaw/style/STYLE.md`，拿到 `STYLE_PREFIX`（风格前缀短语）和锚图路径。
2. 提示词 = `STYLE_PREFIX + 本次内容描述`。
3. 调脚本时加 `-r <锚图路径>`。

> ⚠️ **P1 现状：STYLE.md 是空白的，禁止生成正式素材。** 先走工作流 1；
> 唯一的例外是管线冒烟测试（中性主体、产物标注为测试件）。

---

## 工作流 1：风格锚定（用户定画风时执行）

STYLE.md 中没有锚图时，先补锚再干活：

1. **和用户确认画风方向**（给 2~3 个具体选项），或让用户丢一张喜欢的图进来。
   P1 的画风决定推迟在 M6（GamePlan §0），所以这一步是**用户发起的**，
   agent 不要自作主张先定一个画风开始铺量。
2. 生成锚图（白底、单主体、最能代表画风的图）：
   ```bash
   .agents/skills/media-gen/scripts/gen_image.sh \
     -p "<用户钦定的风格描述> 代表性主体全身/单品，白色背景，居中" \
     -o MediaRaw/style/anchor
   ```
3. **填写 STYLE.md**：把画风浓缩成一句 `STYLE_PREFIX`
   （建议格式：`<风格名>、<线条/体块特征>、<配色特征>、<光影特征>`），
   登记锚图路径与日期，并回填「光照方向 / 色板倾向 / 剪影要求 / UI 风格 / 队伍配色」字段。
4. 之后所有生成都带 `-r MediaRaw/style/anchor.<ext>` + 前缀。

锚图挑选标准：剪影清晰、颜色对比强、光影简单。不满意就重生成，锚图值得花时间。

---

## 工作流 2：★ 中性单色图标（P1 最常用的一个，务必读懂）

**为什么必须中性**：两队墨色由 `InkGrid` 的 owner 决定、每局可换（GamePlan §4.2），
图标如果自带固有色，就会出现「红队图标配蓝队墨色」。所以：

**图标出「白色/浅灰主体 + alpha，无固有色」，颜色交给 Unity 染色。**

```bash
PREFIX="<STYLE_PREFIX>"
ANCHOR="MediaRaw/style/anchor.<ext>"

.agents/skills/media-gen/scripts/gen_image.sh \
  -p "${PREFIX} 游戏 UI 图标：墨水罐，纯白单色剪影、无固有色、简洁几何、主体居中饱满、不要文字" \
  -r "$ANCHOR" --keyout 256 -o MediaRaw/images/ui_icon_inktank
```

Unity 侧染色（UGUI）：`Image.color = teamColor`；3D 侧用材质属性。

**一套图标要成套一致**——不要一个个单独生成（画风会漂、粗细会不统一）。

### 图标成套：用 `--grid` 一次出一表再切分

同图内天然同画风、同线条粗细、同留白比例，切分后即为独立素材：

```bash
.agents/skills/media-gen/scripts/gen_image.sh \
  -p "${PREFIX} 同一套 UI 图标差异表：墨水罐、倒计时秒表、地图标记、能力按钮，2x2 网格排布，纯白单色剪影、规格完全一致、无文字" \
  -r "$ANCHOR" --grid 2x2 --keyout 256 -o MediaRaw/images/ui_icons_set_a
# → ui_icons_set_a_r1c1.png ... r2c2.png（4 张）
```

- 格子数越多单格越糊：**2x2 是质量与效率的甜点**，需要更多图标就分多次生成。
- 先试产 1 张给用户过目，风格 OK 再铺量（省配额也省返工）。
- 分格错位（模型分格不等宽）时手动裁原表重切：
  `split_grid.py <原表> <前缀> <列> <行>`；原表自动保留为 `<前缀>.raw.jpg`。
- **`--keyout` 时各格尺寸不一致**（实测 2x2 出 256×252 / 256×213 / 256×58 / 256×256，
  因为每格各自裁到内容边界）。当独立精灵用没问题；要拼严格等格图集就别逐格 keyout。

### 生成后必须自己看一眼（白图标看不见的坑）

图标是「纯白 + alpha」，在白色背景的看图器里**完全空白**。所以要验证形状必须合成到深色底：

```bash
python3 .agents/skills/media-gen/scripts/preview_alpha.py /tmp/preview.png \
  MediaRaw/images/ui_icons_set_a_r1c1.png MediaRaw/images/ui_icons_set_a_r1c2.png
```

它会拼成接触表（左深灰、右棋盘格）并打印每张的 alpha 覆盖率：
覆盖率 ≈0%（没抠出来）或 >92%（整张没透）都会告警。**这相当于素材侧的「截图自检」**，
agent 不看这一眼就交付等于蒙眼验收。

---

## 工作流 3：可平铺的地面 / 材质贴图（P1 专属约束）

墨系统要在底色上叠队色，所以地面贴图**不能抢戏**：

- **必须可平铺**（`wrapMode Repeat`，由 `MediaImportPostprocessor.cs` 按
  `Art/Textures/` 路径自动套），提示词写「无缝平铺、无明显接缝、正俯视、无透视」。
- **必须低饱和、低对比**，否则覆盖率一眼看不出来（违反 GamePlan 体验支柱 3「地图控制可读性」）。
- 不要 `--keyout`（这是要满铺的贴图，不是透明图标）。

```bash
.agents/skills/media-gen/scripts/gen_image.sh \
  -p "${PREFIX} 可无缝平铺的地面材质：磨砂混凝土，低饱和灰、低对比、无强烈方向性光影、无明显接缝、正俯视、无透视、无文字" \
  -o MediaRaw/images/tex_ground_concrete
# 拷进 Assets/_Project/Art/Textures/ 后导入设置自动是 Repeat + mips on
```

> 注意：P1 的**地面底色**如果是由 Shader 程序化生成的，就不需要这张贴图。
> 先确认需求再生成，别凭空造素材。

---

## 工作流 4：墨点溅射遮罩（与画风无关，可先备着）

墨点形状是玩法可读性的一部分，用 alpha 遮罩（单色形状）而不是带画风的插画——
这样它**不受 STYLE.md 影响**，可以提前备好：

```bash
# 单个形状
gen_image.sh -p "墨点溅射形状，单一色块剪影，边缘有飞溅小点，正俯视扁平，纯白，无文字" \
  --keyout 256 -o MediaRaw/images/ink_splat_a

# 四种形状差分表（切分后随机取一条用，避免每次涂地都是同一个形状）
gen_image.sh -p "四种不同的墨点溅射形状差异表：圆形大块、放射状、细长条、星形，2x2 网格排布，纯白剪影，无文字" \
  --grid 2x2 --keyout 256 -o MediaRaw/images/ink_splats
```

落地：`Art/Sprites/`（UGUI/精灵）或 `Art/Textures/`（程序化贴花），按实际用法选。

---

## 工作流 5：图像编辑（改色 / 加细节 / 去元素 / 换背景）

把**原图**当参考图传入，prompt 写修改指令：

```bash
gen_image.sh -p "把这个图标的外轮廓改得更圆润，其余一切保持不变" \
  -r MediaRaw/images/ui_icon_inktank.png -o MediaRaw/images/ui_icon_inktank_v2
```

- 适合：局部改动、风格微调。不适合：大改构图（不如重生成）。
- 改完的图可以继续当参考图（链式迭代），但建议链不超过 2~3 跳，细节会漂移。
- 迭代版本**不要覆盖已入库的文件**：文件名带 `_v2/_v3`，否则 Unity 会缓存旧 `.meta` 造成混乱。

---

## 透明底管线（`--keyout` 内部做了什么）

模型**不支持** alpha 通道（实测要求透明底仍返回 JPEG）。管线：

1. 脚本自动在 prompt 追加「纯品红 #FF00FF 背景、主体不触边、无投影」要求
2. 生成 JPEG（模型有时把品红画成内嵌矩形，脚本会自动定位并裁剪）
3. `keyout.py`：品红度量 `min(R,B)-G` 软抠 + 半透明区去品红溢色 + 腐蚀 1px 去晕边
   + 按内容裁剪 + 可选缩放，输出带 alpha 的 PNG
4. 原始图保留为 `<前缀>.raw.jpg`，抠得不干净可调 `keyout.py` 阈值重跑
   （**这也是为什么 `.raw.jpg` 要进版本库**——它是重抠的唯一底稿）

注意：主体本身含大量品红/粉紫色时品红底会误伤 —— 此时在 prompt 里要求换成
纯绿色 #00FF00 背景，并用 `keyout.py --green` 处理（脚本已支持该开关）。

**Unity 侧**：透明 PNG 拷进 `Art/Sprites/` 后，
`MediaImportPostprocessor.cs` 会自动设 `alphaIsTransparency`，
所以不需要手工点 Inspector（手改也会被重导入覆盖）。