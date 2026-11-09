using UnityEngine;

namespace P1.Core
{
    /// <summary>外观槽位（衣柜的页签顺序 = 枚举顺序）。</summary>
    public enum CosmeticSlot
    {
        Outfit = 0,      // 服装配色：上衣 / 短袖 / 短裤 / 鞋
        Hair = 1,        // 触手发型（骨形不变，换样条网格）
        Headgear = 2,    // 头饰：鸭舌帽 / 毛线帽 / 护目镜
        WeaponSkin = 3,  // 武器涂装：枪身 + 金属件双色
        Title = 4,       // 称号 + 徽章阶
    }

    /// <summary>
    /// 服装配色。<b>只</b>管中性衣物四色：队伍的腰带 / 触手发色 / 瞳色 / 墨罐液面 / 乌贼体
    /// 永远由队伍色驱动，绝不参与外观（敌我一眼可读是第一红线）。
    /// </summary>
    public readonly struct OutfitPaletteDef
    {
        public readonly Color Shirt;   // 上衣主体
        public readonly Color Sleeve;  // 短袖/肩盖
        public readonly Color Shorts;  // 短裤 + 大腿
        public readonly Color Shoe;    // 鞋面

        public OutfitPaletteDef(Color shirt, Color sleeve, Color shorts, Color shoe)
        {
            Shirt = shirt;
            Sleeve = sleeve;
            Shorts = shorts;
            Shoe = shoe;
        }
    }

    /// <summary>
    /// 一段触手（7 点样条管）的几何参数。坐标系是发束枢轴局部空间：<b>-Y = 下垂方向</b>，
    /// 原点在头皮采样点。与 <c>CharacterVisualAssembler.BuildHairTubeMesh</c> 逐参数对应。
    /// </summary>
    public readonly struct HairSegmentSpec
    {
        public readonly Vector3[] Points;   // 恒 7 点；P0 为原点，P3..P6 相同 = 末端停顿
        public readonly float BaseRadius;
        public readonly float TipRadius;
        public readonly float FlattenZ;     // Z 向压扁（0.55 ≈ 扁带感）
        public readonly float TaperStart;   // 起收细的位置（0..1）

        public HairSegmentSpec(Vector3[] points, float baseRadius, float tipRadius, float flattenZ, float taperStart)
        {
            Points = points;
            BaseRadius = baseRadius;
            TipRadius = tipRadius;
            FlattenZ = flattenZ;
            TaperStart = taperStart;
        }

        /// <summary>构造 7 点：原点 + 三个行进点 + 末端重复三次。</summary>
        public static HairSegmentSpec Make(Vector3 p1, Vector3 p2, Vector3 p3,
            float baseRadius, float tipRadius, float flattenZ, float taperStart)
        {
            return new HairSegmentSpec(
                new[] { Vector3.zero, p1, p2, p3, p3, p3, p3 },
                baseRadius, tipRadius, flattenZ, taperStart);
        }
    }

    /// <summary>
    /// 触手发型：6 束同拓扑，只换网格与每束的粗细/长度倍率。
    /// 不增删 GameObject —— 12 个 MeshRenderer 恒定存在，换发只是重设 sharedMesh，
    /// 因此和合并后的角色、弹簧摆动链（BindKidRig）完全兼容。
    /// </summary>
    public readonly struct HairStyleDef
    {
        public readonly Vector3[] StrandDirs;      // 恒 6：头皮采样方向，决定轮廓布局
        public readonly float[] RadiusScale;       // 恒 6：每束粗细倍率
        public readonly float[] LengthScale;       // 恒 6：每束长度倍率（缩放梢段枢轴位置）
        public readonly HairSegmentSpec Root;      // 根段
        public readonly HairSegmentSpec Tip;       // 梢段
        public readonly Vector3 MidPivotPosition;  // 梢段枢轴（= 根段末端）
        public readonly Vector3 MidPivotEuler;

        public HairStyleDef(Vector3[] strandDirs, float[] radiusScale, float[] lengthScale,
            HairSegmentSpec root, HairSegmentSpec tip, Vector3 midPivotPosition, Vector3 midPivotEuler)
        {
            StrandDirs = strandDirs;
            RadiusScale = radiusScale;
            LengthScale = lengthScale;
            Root = root;
            Tip = tip;
            MidPivotPosition = midPivotPosition;
            MidPivotEuler = midPivotEuler;
        }
    }

    /// <summary>头饰形态。None 也占一个条目，玩家可以把他摘下来。</summary>
    public enum HeadgearKind
    {
        None = 0,
        Cap = 1,      // 鸭舌帽：圆顶 + 前檐
        Beanie = 2,   // 毛线帽：高圆顶 + 折边
        Goggles = 3,  // 护目镜：双镜片 + 环带（架在额前，压在发根上）
    }

    public readonly struct HeadgearDef
    {
        public readonly HeadgearKind Kind;
        public readonly Color Main;    // 帽体 / 镜带
        public readonly Color Accent;  // 帽檐 / 折边 / 镜片

        public HeadgearDef(HeadgearKind kind, Color main, Color accent)
        {
            Kind = kind;
            Main = main;
            Accent = accent;
        }
    }

    /// <summary>武器涂装：枪身（深色塑料件）+ 金属件双色。墨罐/液面永远队伍色（墨量反馈刚需）。</summary>
    public readonly struct WeaponSkinDef
    {
        public readonly Color Body;
        public readonly Color Metal;

        public WeaponSkinDef(Color body, Color metal)
        {
            Body = body;
            Metal = metal;
        }
    }

    /// <summary>称号。Tier 0..4 = 段位色阶，5 = 成就专属（金）。</summary>
    public readonly struct TitleDef
    {
        public readonly string Text;
        public readonly int Tier;

        public TitleDef(string text, int tier)
        {
            Text = text;
            Tier = tier;
        }
    }

    /// <summary>
    /// 一件外观。载荷按槽位分（未用到的字段为 default），静态表里一次性构造，运行时只读。
    /// </summary>
    public readonly struct CosmeticDef
    {
        public readonly string Id;
        public readonly CosmeticSlot Slot;
        public readonly string DisplayName;
        public readonly string Subtitle;
        public readonly string IconName;          // UIIconLibrary 键；取不到时 UI 隐藏 Image
        public readonly int UnlockLevel;          // 0 = 开局即有
        public readonly string UnlockAchievementId; // 非空 = 成就解锁（优先于 UnlockLevel）

        public readonly OutfitPaletteDef Outfit;
        public readonly HairStyleDef Hair;
        public readonly HeadgearDef Headgear;
        public readonly WeaponSkinDef WeaponSkin;
        public readonly TitleDef Title;

        private CosmeticDef(string id, CosmeticSlot slot, string displayName, string subtitle, string iconName,
            int unlockLevel, string unlockAchievementId,
            OutfitPaletteDef outfit, HairStyleDef hair, HeadgearDef headgear, WeaponSkinDef weaponSkin, TitleDef title)
        {
            Id = id;
            Slot = slot;
            DisplayName = displayName;
            Subtitle = subtitle;
            IconName = iconName;
            UnlockLevel = unlockLevel;
            UnlockAchievementId = unlockAchievementId;
            Outfit = outfit;
            Hair = hair;
            Headgear = headgear;
            WeaponSkin = weaponSkin;
            Title = title;
        }

        /// <summary>是否成就解锁（衣柜卡片要显示成就名而不是等级）。</summary>
        public bool IsAchievementReward => !string.IsNullOrEmpty(UnlockAchievementId);

        public static CosmeticDef MakeOutfit(string id, string name, string subtitle, int level, string achievement,
            Color shirt, Color sleeve, Color shorts, Color shoe)
        {
            return new CosmeticDef(id, CosmeticSlot.Outfit, name, subtitle, "glyph_shirt", level, achievement,
                new OutfitPaletteDef(shirt, sleeve, shorts, shoe), default, default, default, default);
        }

        public static CosmeticDef MakeHair(string id, string name, string subtitle, int level, string achievement,
            HairStyleDef style)
        {
            return new CosmeticDef(id, CosmeticSlot.Hair, name, subtitle, "glyph_hair", level, achievement,
                default, style, default, default, default);
        }

        public static CosmeticDef MakeHeadgear(string id, string name, string subtitle, int level, string achievement,
            HeadgearDef def)
        {
            return new CosmeticDef(id, CosmeticSlot.Headgear, name, subtitle, "glyph_hanger", level, achievement,
                default, default, def, default, default);
        }

        public static CosmeticDef MakeWeaponSkin(string id, string name, string subtitle, int level, string achievement,
            Color body, Color metal)
        {
            return new CosmeticDef(id, CosmeticSlot.WeaponSkin, name, subtitle, "weapon_shooter", level, achievement,
                default, default, default, new WeaponSkinDef(body, metal), default);
        }

        public static CosmeticDef MakeTitle(string id, string name, string subtitle, int level, string achievement,
            string text, int tier)
        {
            return new CosmeticDef(id, CosmeticSlot.Title, name, subtitle, "glyph_crown", level, achievement,
                default, default, default, default, new TitleDef(text, tier));
        }
    }

    /// <summary>
    /// 外观目录（唯一数据源，34 件）。与 <see cref="AchievementCatalog"/> / <see cref="P1.UI.MatchAwards"/>
    /// 同思路：**内容不走 ScriptableObject**（外观是内容不是调参旋钮）。解锁门槛就是下面表里的
    /// <c>level</c> / <c>achievement</c> 两个参数，想改节奏直接改这一处。
    ///
    /// 设计红线：
    /// 1. 队伍色部件（腰带 / 触手发色 / 瞳色 / 墨罐液面 / 乌贼体）永不出现在这张表里；
    /// 2. 每个槽位必须有一件 <c>level = 0</c> 的默认件，存档里装备 id 非法时回退到它。
    /// </summary>
    public static class CosmeticCatalog
    {
        // ---- 调色板（街头潮流 + 几何大胆；与队伍橙 #FF7314 / 队伍蓝 #296BFF 相邻不脏）----
        private static readonly Color Cream = Hex("#F0EDE3");
        private static readonly Color CreamShade = Hex("#DCDAD0");
        private static readonly Color Navy = Hex("#27304A");
        private static readonly Color OffWhite = Hex("#F2F2F0");

        public static readonly CosmeticDef[] All =
        {
            // ================= 服装配色 =================
            CosmeticDef.MakeOutfit("outfit_cream", "奶油原色", "出厂配置，怎么穿都不出错", 0, null,
                Cream, CreamShade, Navy, OffWhite),

            CosmeticDef.MakeOutfit("outfit_mint", "薄荷汽水", "苏打水里泡过的新鲜感", 2, null,
                Hex("#BFE8D8"), Hex("#A9D9C7"), Hex("#14453F"), Hex("#EEF7F3")),

            CosmeticDef.MakeOutfit("outfit_sunset", "落日冲锋", "把黄昏穿在身上去涂地", 5, null,
                Hex("#FF9F73"), Hex("#F58A5E"), Hex("#3A2140"), Hex("#FFF2E8")),

            CosmeticDef.MakeOutfit("outfit_neon", "霓虹夜行", "夜班涂地员的荧光工装", 10, null,
                Hex("#E8F94A"), Hex("#CFE03C"), Hex("#1B1B21"), Hex("#23232B")),

            CosmeticDef.MakeOutfit("outfit_sakura", "樱吹雪", "一整套都是花瓣的错", 16, null,
                Hex("#F7C9D9"), Hex("#EFB6C9"), Hex("#16161C"), Hex("#FBF5F7")),

            CosmeticDef.MakeOutfit("outfit_midnight", "午夜蓝调", "上身沉进夜色，下身留一盏灯", 20, null,
                Hex("#2B3560"), Hex("#232B50"), Hex("#DCDCD2"), Hex("#1A1A22")),

            CosmeticDef.MakeOutfit("outfit_camo", "橄榄迷彩", "在货柜码头，这叫保护色", 26, null,
                Hex("#6E7A4F"), Hex("#5E6A43"), Hex("#4A4536"), Hex("#2A2A26")),

            CosmeticDef.MakeOutfit("outfit_mono", "纯白通勤", "五十场的老兵，穿衣开始做减法", 0,
                "ach_career_match_50",
                Hex("#FFFFFF"), Hex("#ECECEC"), Hex("#101014"), Hex("#101014")),

            // ================= 触手发型 =================
            // 默认 = 现有造型，参数不要再动（场景里已烘焙的角色就长这样）
            CosmeticDef.MakeHair("hair_tide", "潮汐垂发", "标准触手，六束外撇下垂", 0, null,
                new HairStyleDef(
                    new[] { Dir(-0.88f, 0.30f, -0.10f), Dir(0.88f, 0.30f, -0.10f), Dir(-0.62f, 0.42f, -0.56f),
                            Dir(0.62f, 0.42f, -0.56f), Dir(-0.28f, 0.52f, -0.80f), Dir(0.28f, 0.52f, -0.80f) },
                    Six(1f), Six(1f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.045f, -0.002f), new Vector3(0f, -0.090f, -0.008f),
                        new Vector3(0f, -0.130f, -0.012f), 0.048f, 0.030f, 0.55f, 0.15f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.040f, 0.004f), new Vector3(0f, -0.085f, 0.028f),
                        new Vector3(0f, -0.118f, 0.062f), 0.033f, 0.008f, 0.55f, 0.20f),
                    new Vector3(0f, -0.128f, -0.012f), new Vector3(-6f, 0f, 0f))),

            CosmeticDef.MakeHair("hair_bob", "圆盖短发", "贴头一圈，干净利落", 4, null,
                new HairStyleDef(
                    new[] { Dir(-0.90f, 0.26f, -0.12f), Dir(0.90f, 0.26f, -0.12f), Dir(-0.64f, 0.40f, -0.58f),
                            Dir(0.64f, 0.40f, -0.58f), Dir(-0.30f, 0.50f, -0.82f), Dir(0.30f, 0.50f, -0.82f) },
                    Six(1f), Six(1f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.028f, -0.002f), new Vector3(0f, -0.052f, -0.006f),
                        new Vector3(0f, -0.068f, -0.008f), 0.058f, 0.044f, 0.62f, 0.10f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.018f, 0.006f), new Vector3(0f, -0.032f, 0.020f),
                        new Vector3(0f, -0.040f, 0.034f), 0.042f, 0.016f, 0.55f, 0.30f),
                    new Vector3(0f, -0.066f, -0.008f), new Vector3(-14f, 0f, 0f))),

            CosmeticDef.MakeHair("hair_spike", "冲天短刺", "早上起晚了，但气势是足的", 7, null,
                new HairStyleDef(
                    // 发根压到帽檐高度（y 0.38~0.52）：帽子（帽冠覆盖 y≥0.02）会盖住冠部头发，
                    // 所以发型必须把剪影做在帽檐以下的侧后方 —— 刺从耳侧斜上方支出去，
                    // 没帽子时是一头短刺，戴帽子时帽檐下露出一圈刺。发根太低会变"耳侧横刺"，
                    // 发根太高会被帽子整头吞掉，0.38~0.52 是两者的中间带。
                    new[] { Dir(-0.74f, 0.38f, -0.06f), Dir(0.74f, 0.38f, -0.06f), Dir(-0.52f, 0.46f, -0.46f),
                            Dir(0.52f, 0.46f, -0.46f), Dir(-0.22f, 0.52f, -0.66f), Dir(0.22f, 0.52f, -0.66f) },
                    Six(1f), Six(1f),
                    // 根段短而粗、几乎不下垂；梢段朝斜上方收尖（局部 +Y = 逆重力）
                    HairSegmentSpec.Make(new Vector3(0f, -0.026f, -0.004f), new Vector3(0f, -0.044f, -0.008f),
                        new Vector3(0f, -0.056f, -0.010f), 0.052f, 0.040f, 0.58f, 0.10f),
                    HairSegmentSpec.Make(new Vector3(0f, 0.022f, -0.006f), new Vector3(0f, 0.060f, -0.014f),
                        new Vector3(0f, 0.090f, -0.022f), 0.040f, 0.004f, 0.52f, 0.50f),
                    new Vector3(0f, -0.054f, -0.006f), new Vector3(14f, 0f, 0f))),

            CosmeticDef.MakeHair("hair_twin", "双侧尾", "左右各甩一束，转身很有戏", 14, null,
                new HairStyleDef(
                    new[] { Dir(-0.96f, 0.22f, -0.06f), Dir(0.96f, 0.22f, -0.06f), Dir(-0.58f, 0.44f, -0.52f),
                            Dir(0.58f, 0.44f, -0.52f), Dir(-0.24f, 0.54f, -0.78f), Dir(0.24f, 0.54f, -0.78f) },
                    new[] { 1.35f, 1.35f, 0.40f, 0.40f, 0.45f, 0.45f },
                    new[] { 1.10f, 1.10f, 0.45f, 0.45f, 0.50f, 0.50f },
                    HairSegmentSpec.Make(new Vector3(0f, -0.046f, -0.004f), new Vector3(0f, -0.092f, -0.010f),
                        new Vector3(0f, -0.126f, -0.014f), 0.050f, 0.034f, 0.58f, 0.14f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.048f, 0.006f), new Vector3(0f, -0.100f, 0.020f),
                        new Vector3(0f, -0.140f, 0.040f), 0.032f, 0.007f, 0.55f, 0.22f),
                    new Vector3(0f, -0.124f, -0.010f), new Vector3(-4f, 0f, 0f))),

            CosmeticDef.MakeHair("hair_pony", "高马尾", "后脑一束扎紧，跑起来拖尾最长", 22, null,
                new HairStyleDef(
                    new[] { Dir(-0.85f, 0.34f, -0.12f), Dir(0.85f, 0.34f, -0.12f), Dir(-0.46f, 0.46f, -0.52f),
                            Dir(0.46f, 0.46f, -0.52f), Dir(-0.14f, 0.50f, -0.86f), Dir(0.14f, 0.50f, -0.86f) },
                    new[] { 0.42f, 0.42f, 0.55f, 0.55f, 1.30f, 1.30f },
                    new[] { 0.45f, 0.45f, 0.60f, 0.60f, 1.15f, 1.15f },
                    HairSegmentSpec.Make(new Vector3(0f, -0.050f, -0.002f), new Vector3(0f, -0.100f, -0.006f),
                        new Vector3(0f, -0.145f, -0.010f), 0.046f, 0.030f, 0.55f, 0.15f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.055f, 0.008f), new Vector3(0f, -0.115f, 0.030f),
                        new Vector3(0f, -0.160f, 0.055f), 0.032f, 0.008f, 0.55f, 0.20f),
                    new Vector3(0f, -0.143f, -0.010f), new Vector3(-6f, 0f, 0f))),

            CosmeticDef.MakeHair("hair_crest", "后掠冠羽", "一条命杀八个的人，头发都往后飘", 0, "ach_life_8",
                new HairStyleDef(
                    new[] { Dir(-0.70f, 0.40f, -0.30f), Dir(0.70f, 0.40f, -0.30f), Dir(-0.50f, 0.50f, -0.62f),
                            Dir(0.50f, 0.50f, -0.62f), Dir(-0.22f, 0.56f, -0.84f), Dir(0.22f, 0.56f, -0.84f) },
                    Six(1f), Six(1f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.042f, -0.012f), new Vector3(0f, -0.086f, -0.030f),
                        new Vector3(0f, -0.124f, -0.048f), 0.050f, 0.032f, 0.55f, 0.15f),
                    HairSegmentSpec.Make(new Vector3(0f, -0.030f, 0.010f), new Vector3(0f, -0.062f, 0.042f),
                        new Vector3(0f, -0.086f, 0.078f), 0.032f, 0.007f, 0.55f, 0.18f),
                    new Vector3(0f, -0.122f, -0.048f), new Vector3(-10f, 0f, 0f))),

            // ================= 头饰 =================
            CosmeticDef.MakeHeadgear("head_none", "不戴", "让触手自己说话", 0, null,
                new HeadgearDef(HeadgearKind.None, default, default)),

            CosmeticDef.MakeHeadgear("head_cap", "街头鸭舌帽", "帽檐压低两度，命中率提升百分之零", 3, null,
                new HeadgearDef(HeadgearKind.Cap, Hex("#1B1B22"), Hex("#FFD400"))),

            CosmeticDef.MakeHeadgear("head_beanie", "毛线帽", "码头风大，脑子不能着凉", 8, null,
                new HeadgearDef(HeadgearKind.Beanie, Hex("#E84A5F"), Hex("#F7F3EA"))),

            CosmeticDef.MakeHeadgear("head_goggles", "护目镜", "架在额前不挡视线，但很专业", 18, null,
                new HeadgearDef(HeadgearKind.Goggles, Hex("#2B2B33"), Hex("#7FD8C4"))),

            // ================= 武器涂装 =================
            CosmeticDef.MakeWeaponSkin("gun_default", "出厂灰", "没有涂装的涂装，也是一种涂装", 0, null,
                Hex("#1A1D27"), Hex("#8C94A6")),

            CosmeticDef.MakeWeaponSkin("gun_cherry", "樱桃弹药", "红色不只是好看，还很快", 6, null,
                Hex("#8E1F2B"), Hex("#D9D2C8")),

            CosmeticDef.MakeWeaponSkin("gun_mint", "薄荷合金", "冷色调的枪，热得发烫的手感", 12, null,
                Hex("#5FC3AE"), Hex("#E8F4F0")),

            CosmeticDef.MakeWeaponSkin("gun_void", "虚空霓虹", "枪身沉进黑里，金属泛紫", 24, null,
                Hex("#0B0B12"), Hex("#6C5CE0")),

            CosmeticDef.MakeWeaponSkin("gun_sakura", "樱花糖霜", "开一枪掉一片花瓣（并不会）", 32, null,
                Hex("#E88DAE"), Hex("#FBF3F0")),

            CosmeticDef.MakeWeaponSkin("gun_gold", "冠军金", "三十杀的奖杯，直接焊在枪上", 0, "ach_kill_30",
                Hex("#3A2E14"), Hex("#E0B34A")),

            // ================= 称号 =================
            CosmeticDef.MakeTitle("title_recruit", "新兵", "Fresh Recruit", 0, null, "FRESH RECRUIT", 0),
            CosmeticDef.MakeTitle("title_scrapper", "涂地打手", "Turf Scrapper", 5, null, "TURF SCRAPPER", 1),
            CosmeticDef.MakeTitle("title_slinger", "墨水枪手", "Ink Slinger", 10, null, "INK SLINGER", 2),
            CosmeticDef.MakeTitle("title_veteran", "喷涂老兵", "Splat Veteran", 20, null, "SPLAT VETERAN", 3),
            CosmeticDef.MakeTitle("title_legend", "潮汐传奇", "Tide Legend", 30, null, "TIDE LEGEND", 4),

            CosmeticDef.MakeTitle("title_untouched", "无痕杀手", "整局对面没闻到你", 0, "ach_untouched",
                "UNTOUCHABLE", 5),
            CosmeticDef.MakeTitle("title_clutch", "压哨先生", "最后十秒才是你的主场", 0, "ach_clutch",
                "BUZZER BEATER", 5),
            CosmeticDef.MakeTitle("title_pacifist", "佛系粉刷匠", "不杀一人，照样赢", 0, "ach_pacifist",
                "PACIFIST", 5),
            CosmeticDef.MakeTitle("title_specialist", "大招永动机", "能量条刚满就空", 0, "ach_special_5",
                "PERPETUAL", 5),
            CosmeticDef.MakeTitle("title_lazy", "甩手掌柜", "全场不到十平米，躺赢", 0, "ach_lazy_win",
                "IDLE WINNER", 5),
        };

        public static int Count => All.Length;

        // 按槽位切好的缓存（衣柜页签要按槽位一次性取全，避免每次进页面重新扫表）
        private static CosmeticDef[][] _bySlot;
        private static string[] _defaultIds;

        /// <summary>槽位页签的中文名。</summary>
        public static string SlotLabel(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Outfit: return "服装配色";
                case CosmeticSlot.Hair: return "触手发型";
                case CosmeticSlot.Headgear: return "头饰";
                case CosmeticSlot.WeaponSkin: return "武器涂装";
                case CosmeticSlot.Title: return "称号";
                default: return "外观";
            }
        }

        /// <summary>槽位页签图标（UIIconLibrary 键）。</summary>
        public static string SlotIcon(CosmeticSlot slot)
        {
            switch (slot)
            {
                case CosmeticSlot.Outfit: return "glyph_shirt";
                case CosmeticSlot.Hair: return "glyph_hair";
                case CosmeticSlot.Headgear: return "glyph_hanger";
                case CosmeticSlot.WeaponSkin: return "weapon_shooter";
                case CosmeticSlot.Title: return "glyph_crown";
                default: return "glyph_star";
            }
        }

        public static CosmeticSlot[] Slots { get; } = new[]
        {
            CosmeticSlot.Outfit, CosmeticSlot.Hair, CosmeticSlot.Headgear, CosmeticSlot.WeaponSkin, CosmeticSlot.Title
        };

        /// <summary>某槽位的全部条目（按目录顺序 = 解锁顺序）。</summary>
        public static CosmeticDef[] ForSlot(CosmeticSlot slot)
        {
            if (_bySlot == null)
            {
                _bySlot = new CosmeticDef[Slots.Length][];
                for (int s = 0; s < Slots.Length; s++)
                {
                    int n = 0;
                    for (int i = 0; i < All.Length; i++) if (All[i].Slot == Slots[s]) n++;
                    var list = new CosmeticDef[n];
                    int k = 0;
                    for (int i = 0; i < All.Length; i++) if (All[i].Slot == Slots[s]) list[k++] = All[i];
                    _bySlot[s] = list;
                }
            }
            return _bySlot[(int)slot];
        }

        public static bool TryGet(string id, out CosmeticDef def)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i].Id, id, System.StringComparison.Ordinal))
                    {
                        def = All[i];
                        return true;
                    }
                }
            }
            def = default;
            return false;
        }

        /// <summary>槽位默认件 id（level = 0 且无成就门槛的第一件）。</summary>
        public static string DefaultId(CosmeticSlot slot)
        {
            if (_defaultIds == null)
            {
                _defaultIds = new string[Slots.Length];
                for (int s = 0; s < Slots.Length; s++)
                {
                    CosmeticDef[] list = ForSlot(Slots[s]);
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i].UnlockLevel <= 0 && !list[i].IsAchievementReward)
                        {
                            _defaultIds[s] = list[i].Id;
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(_defaultIds[s]) && list.Length > 0) _defaultIds[s] = list[0].Id;
                }
            }
            return _defaultIds[(int)slot];
        }

        /// <summary>清掉静态缓存（仅 EditMode 测试用）。</summary>
        public static void ResetCachesForTests()
        {
            _bySlot = null;
            _defaultIds = null;
        }

        // ---- 构造辅助 ----
        private static Vector3 Dir(float x, float y, float z) => new Vector3(x, y, z).normalized;

        private static float[] Six(float v) => new[] { v, v, v, v, v, v };

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
        }
    }
}
