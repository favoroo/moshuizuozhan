using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 程序化装配 HUD 底部「双横条仪表」（2026-11 第四轮：放大 + 抗锯齿 + 满能状态环）。
    ///
    /// 第四轮解决「图标/横条太小、边缘发毛」：徽章 28→34、图标 24→28、轨道 14→18，整组 284×64→344×78；
    /// 徽章 / 轨道网格开 <see cref="UIInkShapeGraphic.EdgeFeather"/>（外沿 alpha 羽化），
    /// 胶囊填充贴图开 mip + 三线性，图标贴图开 mip（素材导入策略统一处理）——
    /// UI 网格没有 MSAA、128px 图标缩到 28px 硬采样，是锯齿的两个来源。
    ///
    /// 结构仍是右端拉满：**左端身份徽章 + 墨黑胶囊轨道 + 轨道内缩 3px 的彩色胶囊填充**。
    ///
    /// <code>
    ///  ◎ ⬤ ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬   ← 大招条：墨黑徽章 + 黄图标 / 墨黑轨道 + 黄填充
    ///  ⬤ ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬   ← 生命条：绿底墨十字徽章 / 墨黑轨道 + 绿填充
    /// </code>
    ///
    /// 大招满能**只做静默换状态**：徽章反色（墨底黄图标 ↔ 黄底墨图标）并露出外圈
    /// <see cref="ReadyRingName"/>（徽章外留 2px 缝的 4px 黄环，填充透明），不闪条身、不爆闪、不做循环脉冲。
    /// 生命告警 = 填充/徽章翻红 + 填充红↔纸白交替闪（2.2Hz，脱险即停），掉血整行一记 Punch。
    ///
    /// 公共 API（BuildCluster / ApplyStatusCardsLayout / GaugeCluster / Strip* 常量）保持向后兼容；
    /// 旧卡片常量与 <see cref="BuildCardChrome"/> 保留给并行工具 / 旧调用方，底部 HUD 不再读取。
    /// </summary>
    public static class HudSpecialGaugeFactory
    {
        /// <summary>装配结果。</summary>
        public struct GaugeCluster
        {
            public LiquidOrbView Orb;
            public InkTankView Tank;
            public RectTransform Root;

            /// <summary>大招条根节点（LiquidOrbView 就挂在这个物体上）。</summary>
            public RectTransform SpecialCard;

            /// <summary>墨水卡根节点（P42-E 起恒为 null，仅保留字段兼容）。</summary>
            public RectTransform InkCard;
        }

        /// <summary>旧卡片外壳（纸白底 + 墨框 + 硬影 + 顶部身份色带）；底部 HUD 已改横条，不再使用。</summary>
        public struct CardChrome
        {
            public UIInkShapeGraphic Background;
            public UIInkShapeGraphic Rail;
        }

        // ── 双横条几何（1920×1080 参考分辨率，底边居中） ─────────────────────

        /// <summary>横条组宽度（桌面）= 徽章 34 + 间隙 10 + 轨道 300（轨道拉满到行右沿）。</summary>
        public const float BarGroupWidthDesktop = 344f;

        /// <summary>横条组宽度（移动端）= 38 + 11 + 330。</summary>
        public const float BarGroupWidthMobile = 379f;

        /// <summary>横条组高度（桌面）= 行高 34 × 2 + 行距 10。</summary>
        public const float BarGroupHeightDesktop = 78f;

        /// <summary>横条组高度（移动端）= 38 × 2 + 11。</summary>
        public const float BarGroupHeightMobile = 87f;

        /// <summary>单行高度（桌面，= 身份徽章边长）。</summary>
        public const float BarRowHeightDesktop = 34f;

        /// <summary>单行高度（移动端）。</summary>
        public const float BarRowHeightMobile = 38f;

        /// <summary>两行之间的留白（桌面）。</summary>
        public const float BarRowGapDesktop = 10f;

        /// <summary>两行之间的留白（移动端）。</summary>
        public const float BarRowGapMobile = 11f;

        /// <summary>轨道（横条本体）高度：桌面 18，配合 3px 墨描边仍有 12px 实心。</summary>
        public const float BarTrackHeightDesktop = 18f;

        /// <summary>轨道高度（移动端）：手机物理尺寸小，不缩小。</summary>
        public const float BarTrackHeightMobile = 20f;

        /// <summary>轨道宽度（桌面）= 组宽 344 − 徽章 34 − 间隙 10；拉到行右沿，右端不再有槽位。</summary>
        public const float BarTrackWidthDesktop = 300f;

        /// <summary>轨道宽度（移动端）= 379 − 38 − 11。</summary>
        public const float BarTrackWidthMobile = 330f;

        /// <summary>左端身份徽章边长（桌面正圆）。</summary>
        public const float BarBadgeSizeDesktop = 34f;

        /// <summary>左端身份徽章边长（移动端）。</summary>
        public const float BarBadgeSizeMobile = 38f;

        /// <summary>徽章图标显示尺寸（桌面）：28px，正好落在 34px 徽章的 3px 墨边内圈。</summary>
        public const float BarBadgeIconSize = 28f;

        /// <summary>徽章图标显示尺寸（移动端）。</summary>
        public const float BarBadgeIconSizeMobile = 32f;

        /// <summary>徽章与轨道之间的留白（桌面）。</summary>
        public const float BarBadgeGap = 10f;

        /// <summary>徽章与轨道之间的留白（移动端）。</summary>
        public const float BarBadgeGapMobile = 11f;

        /// <summary>满能状态环相对徽章的外扩量（桌面）：徽章外留 2px 缝后，再画 4px 黄环。</summary>
        public const float ReadyRingExtraDesktop = 12f;

        /// <summary>满能状态环外扩量（移动端）。</summary>
        public const float ReadyRingExtraMobile = 14f;

        /// <summary>满能状态环的环带宽度（px）。</summary>
        public const float ReadyRingBand = 4f;

        // ── 右端槽位遗留常量（第三轮已移除 Q 键帽 / 读数芯片，横条拉满整行；
        //    常量保留只为旧调用方与测试编译通过，底部 HUD 不再读取） ─────────────

        /// <summary>遗留：右端槽位与轨道之间的留白（桌面）。</summary>
        public const float BarSlotGap = 6f;

        /// <summary>遗留：右端槽位留白（移动端）。</summary>
        public const float BarSlotGapMobile = 7f;

        /// <summary>遗留：右端槽位宽度（桌面）。</summary>
        public const float BarSlotWidthDesktop = 50f;

        /// <summary>遗留：右端槽位宽度（移动端）。</summary>
        public const float BarSlotWidthMobile = 56f;

        /// <summary>遗留：右端读数芯片 / 键帽高度（桌面）。</summary>
        public const float BarChipHeightDesktop = 26f;

        /// <summary>遗留：右端读数芯片高度（移动端）。</summary>
        public const float BarChipHeightMobile = 31f;

        /// <summary>遗留：生命读数芯片节点名（第三轮起不再创建）。</summary>
        public const string HealthChipName = "HpChip";

        /// <summary>填充相对轨道的内缩（= 横条自动墨描边，桌面 18 → 12px 实心）。</summary>
        public const float BarFillInset = 3f;

        /// <summary>徽章 / 轨道网格的外沿羽化宽度（px）：消掉 UI 网格无 MSAA 的轮廓锯齿。</summary>
        public const float BarEdgeFeather = 1.6f;

        /// <summary>横条组底边距屏幕底部（桌面）。</summary>
        public const float BarGroupBottomYDesktop = 54f;

        /// <summary>横条组底边距（移动端，给左右触控键留出下沿空间）。</summary>
        public const float BarGroupBottomYMobile = 46f;

        /// <summary>遗留：「Q」键帽宽度（键帽已下线，仅兼容）。</summary>
        public const float ReadyKeycapWidth = BarSlotWidthDesktop;

        /// <summary>遗留：「Q」键帽高度（键帽已下线，仅兼容）。</summary>
        public const float ReadyKeycapHeight = BarChipHeightDesktop;

        /// <summary>移动端徽章放大系数（徽章尺寸随端别切换，不再是键帽缩放）。</summary>
        public const float BadgeMobileScale = 1.2f;

        /// <summary>遗留：生命读数（右槽大数字）字号（读数已下线，仅兼容）。</summary>
        public const float HealthNumberFontSize = 20f;

        /// <summary>遗留：读数字号上限（读数已下线，仅兼容）。</summary>
        public const float HealthNumberMaxFontSize = 24f;

        /// <summary>身份徽章节点名（两行同名，位置由 <see cref="ApplyBarRowMetrics"/> 统一写）。</summary>
        public const string BarBadgeName = "BarBadge";

        /// <summary>横条轨道节点名（墨黑胶囊底）。</summary>
        public const string BarTrackName = "BarTrack";

        /// <summary>横条填充节点名（UIPillImage，左起横向填充）。</summary>
        public const string BarFillName = "BarFill";

        /// <summary>遗留：「Q」键帽节点名（第三轮起不再创建）。</summary>
        public const string ReadyHintName = "ReadyHint";

        /// <summary>遗留：满能爆闪节点名（爆闪已下线，仅兼容）。</summary>
        public const string ReadyBurstName = "ReadyBurst";

        /// <summary>生命徽章内的十字图标节点名。</summary>
        public const string HealthBadgeIconName = "HpCrossIcon";

        /// <summary>大招徽章内的技能图标根节点名。</summary>
        public const string SpecialIconName = "SpecialIcon";

        /// <summary>满能状态环节点名（徽章外圈，满能才显示；充能时隐藏）。</summary>
        public const string ReadyRingName = "ReadyRing";

        // ── 环组包围盒的兼容常量（旧测试 / 旧面板容器读这一组） ───────────────

        /// <summary>桌面横条组宽度（旧「环组 / 信息条」宽度）。</summary>
        public const float CardAreaWidth = BarGroupWidthDesktop;

        /// <summary>移动端横条组宽度。</summary>
        public const float CardAreaWidthMobile = BarGroupWidthMobile;

        /// <summary>桌面横条组高度。</summary>
        public const float CardHeightDesktop = BarGroupHeightDesktop;

        /// <summary>移动端横条组高度。</summary>
        public const float CardHeightMobile = BarGroupHeightMobile;

        /// <summary>旧「卡片间距」（现为两行之间的行距，桌面）。</summary>
        public const float CardGapDesktop = BarRowGapDesktop;

        /// <summary>旧「移动端卡片间距」（现为行距）。</summary>
        public const float CardGapMobile = BarRowGapMobile;

        // ── 圆环版遗留常量（横条版不再读取，保留只为旧调用方 / 并行工具编译通过） ──

        /// <summary>遗留：圆环版大招环边长（现等价于行高）。</summary>
        public const float SpecialRingSizeDesktop = BarRowHeightDesktop;

        /// <summary>遗留：圆环版大招环边长（移动端）。</summary>
        public const float SpecialRingSizeMobile = BarRowHeightMobile;

        /// <summary>遗留：圆环版生命环边长（现等价于行高）。</summary>
        public const float HealthRingSizeDesktop = BarRowHeightDesktop;

        /// <summary>遗留：圆环版生命环边长（移动端）。</summary>
        public const float HealthRingSizeMobile = BarRowHeightMobile;

        /// <summary>遗留：圆环版两环间距（现为行距）。</summary>
        public const float RingGapDesktop = BarRowGapDesktop;

        /// <summary>遗留：圆环版两环间距（移动端）。</summary>
        public const float RingGapMobile = BarRowGapMobile;

        /// <summary>遗留：圆环版环组底边距。</summary>
        public const float CardsBottomYDesktop = BarGroupBottomYDesktop;

        /// <summary>遗留：圆环版环组底边距（移动端）。</summary>
        public const float CardsBottomYMobile = BarGroupBottomYMobile;

        /// <summary>遗留：圆环版环带宽度占比（横条版不读）。</summary>
        public const float RingBandRatio = 0.12f;

        /// <summary>遗留：圆环版环带外沿墨底（横条版不读）。</summary>
        public const float RingOuterInset = 3f;

        /// <summary>遗留：圆环版满能键帽与环顶间距（横条版不读）。</summary>
        public const float ReadyKeycapGap = 6f;

        /// <summary>遗留：圆环版生命十字徽章边长（现等价于身份徽章边长）。</summary>
        public const float HealthBadgeSize = BarBadgeSizeDesktop;

        /// <summary>遗留：圆环版角标停靠点（横条版角标在行内左/右槽，不再停靠）。</summary>
        public static readonly Vector2 BadgeDockOffset = Vector2.zero;

        /// <summary>遗留：圆环版生命徽章节点名（横条版身份徽章统一叫 <see cref="BarBadgeName"/>）。</summary>
        public const string HealthBadgeName = BarBadgeName;

        /// <summary>遗留入口：圆环版角标缩放（横条版等价于 <see cref="ApplyBarRowMetrics"/>）。</summary>
        public static void ApplyRingBadgeLayout(RectTransform ringRoot, bool mobile)
        {
            ApplyBarRowMetrics(ringRoot, mobile);
        }

        /// <summary>遗留入口：圆环版环带宽度（横条版不绘制环弧，静默忽略）。</summary>
        public static void ApplyRingBand(UIArcRingGraphic arc, float ringSize)
        {
            if (arc == null) return;
            float band = Mathf.Max(3f, ringSize * RingBandRatio);
            arc.SetBand(band, RingOuterInset);
        }

        /// <summary>遗留入口：按环根节点尺寸重算环带（横条版不绘制环弧，静默忽略）。</summary>
        public static void RefreshRingBand(RectTransform ringRoot)
        {
            if (ringRoot == null || ringRoot.rect.width < 1f) return;

            float size = Mathf.Min(ringRoot.rect.width, ringRoot.rect.height);
            var arcs = ringRoot.GetComponentsInChildren<UIArcRingGraphic>(true);
            for (int i = 0; i < arcs.Length; i++)
            {
                ApplyRingBand(arcs[i], size);
            }
        }

        // ── 旧卡片内部度量（底部 HUD 不再读取，保留给并行工具 / 旧调用方） ────────

        /// <summary>旧墨水卡宽度（P42-E 起底部已无墨水卡）。</summary>
        public const float InkCardWidth = 332f;

        /// <summary>旧大招卡宽度。</summary>
        public const float SpecialCardWidth = BarGroupWidthDesktop;

        /// <summary>旧生命卡宽度。</summary>
        public const float HealthCardWidth = BarGroupWidthDesktop;

        /// <summary>旧卡片墨框宽度（规范 3px）。</summary>
        public const float CardBorderWidth = ComicStyle.BorderThin;

        /// <summary>旧卡片顶部身份色带高度（规范 5px）。</summary>
        public const float CardRailHeight = 5f;

        /// <summary>旧卡内左右内边距。</summary>
        public const float CardPadding = 12f;

        /// <summary>旧徽章尺寸。</summary>
        public const float CardBadgeSize = 44f;

        /// <summary>旧徽章边框宽度（规范 4px）。</summary>
        public const float CardBadgeBorderWidth = ComicStyle.Border;

        /// <summary>旧徽章与轨道之间的间距。</summary>
        public const float CardBadgeGap = 10f;

        /// <summary>旧墨水 / 大招轨道宽度。</summary>
        public const float MeterTrackWidth = 142f;

        /// <summary>旧生命轨道宽度。</summary>
        public const float HealthTrackWidth = 220f;

        /// <summary>旧轨道高度。</summary>
        public const float TrackHeight = 24f;

        /// <summary>旧大读数字号。</summary>
        public const float CardReadoutFontSize = 36f;

        /// <summary>旧移动端读数上限。</summary>
        public const float MobileReadoutMaxFontSize = 38f;

        /// <summary>旧读数 / 生命读数列宽。</summary>
        public const float MeterReadoutWidth = 112f;

        /// <summary>旧生命读数列宽。</summary>
        public const float HealthReadoutWidth = 86f;

        /// <summary>旧读数列右内边距。</summary>
        public const float ReadoutRightPadding = 8f;

        // ── 旧键鼠提示条常量（HUD 已不绘制，保留向后兼容测试/工具） ──────────────

        /// <summary>旧键鼠提示条宽度（HUD 已不绘制）。</summary>
        public const float KeyHintWidth = 900f;

        /// <summary>旧键鼠提示条高度（HUD 已不绘制）。</summary>
        public const float KeyHintHeight = 34f;

        /// <summary>旧键鼠提示条底边距（HUD 已不绘制）。</summary>
        public const float KeyHintBottomY = 12f;

        // ── 兼容常量（旧「信息条」命名保留：现指向横条组包围盒） ──────────────────

        /// <summary>桌面横条组宽度（旧信息条宽度）。</summary>
        public const float StripWidth = CardAreaWidth;

        /// <summary>移动端横条组宽度（旧信息条宽度）。</summary>
        public const float StripWidthMobile = CardAreaWidthMobile;

        /// <summary>桌面横条组高度（旧信息条高度）。</summary>
        public const float StripHeightDesktop = CardHeightDesktop;

        /// <summary>移动端横条组高度（旧信息条高度）。</summary>
        public const float StripHeightMobile = CardHeightMobile;

        /// <summary>桌面横条组底边距（旧信息条底边距）。</summary>
        public const float StripBottomYDesktop = BarGroupBottomYDesktop;

        /// <summary>移动端横条组底边距（旧信息条底边距）。</summary>
        public const float StripBottomYMobile = BarGroupBottomYMobile;

        private const string CardBackgroundName = "CardBg";
        private const string CardRailName = "Rail";

        // ── 装配 ───────────────────────────────────────────────────────────

        public static GaugeCluster BuildCluster(Transform canvasRoot, UIThemeSO theme, HudGaugeStyleSO styleAsset)
        {
            HudGaugeStyleSO style = HudGaugeStyleSO.Resolve(styleAsset);

            var rootGo = new GameObject("StatusCardsRoot", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvasRoot, false);
            ApplyStatusCardsLayout(root, null, null, null, false);

            // 先关掉再装配：让 LiquidOrbView 的 Awake / OnEnable 在字段写好后运行
            rootGo.SetActive(false);

            // 墨水卡已移除（P42-E）：墨水量改由准星旁 InkTankCrosshairView 显示（对标 INKWAVE .iw-tank）。
            RectTransform specialCard = NewRect("SpecialCard", root);
            LiquidOrbView orb = BuildSpecialBar(specialCard, theme, style);

            var cluster = new GaugeCluster
            {
                Root = root,
                InkCard = null,
                SpecialCard = specialCard,
                Tank = null,
                Orb = orb,
            };

            ApplyStatusCardsLayout(root, null, specialCard, null, false);
            rootGo.SetActive(true);
            return cluster;
        }

        /// <summary>
        /// 移动端适配：条内已无「Q」键帽，这里只把视图切到紧凑布局（保持旧调用兼容）。
        /// 横条几何由 <see cref="ApplyStatusCardsLayout"/> 的 mobile 分支处理。
        /// </summary>
        public static void ApplyMobileBottomLayout(InkTankView tank, LiquidOrbView orb)
        {
            if (tank != null) tank.SetCompactBottomLayout(true);
            if (orb != null) orb.SetCompactBottomLayout(true);
        }

        /// <summary>
        /// 横条组布局（唯一入口）：桌面 / 移动两套几何，幂等，可在分辨率/安全区变化时重复调用。
        /// 根节点尺寸 = 两行包围盒；大招条在上、生命条在下，整组底边居中。
        /// </summary>
        public static void ApplyStatusCardsLayout(RectTransform cardsRoot, RectTransform inkCard,
            RectTransform specialCard, RectTransform healthCard, bool mobile)
        {
            if (cardsRoot == null) return;

            float width = mobile ? BarGroupWidthMobile : BarGroupWidthDesktop;
            float height = mobile ? BarGroupHeightMobile : BarGroupHeightDesktop;
            float rowHeight = mobile ? BarRowHeightMobile : BarRowHeightDesktop;
            float rowGap = mobile ? BarRowGapMobile : BarRowGapDesktop;
            float bottomY = mobile ? BarGroupBottomYMobile : BarGroupBottomYDesktop;

            cardsRoot.anchorMin = cardsRoot.anchorMax = new Vector2(0.5f, 0f);
            cardsRoot.pivot = new Vector2(0.5f, 0f);
            cardsRoot.sizeDelta = new Vector2(width, height);
            cardsRoot.anchoredPosition = new Vector2(0f, bottomY);

            // 旧参数 inkCard 保留只为签名兼容：底部已无墨水卡，传进来也按横条排布，避免混排出错位。
            PlaceBarRow(specialCard, rowHeight + rowGap, width, rowHeight);
            PlaceBarRow(healthCard, 0f, width, rowHeight);

            // 行内度量（徽章尺寸 / 轨道宽高）随端别刷新；填充内缩是常量，不用重算
            ApplyBarRowMetrics(specialCard, mobile);
            ApplyBarRowMetrics(healthCard, mobile);
            // 旧卡片色带（若有遗留节点）仍按最终尺寸收内缩，不影响横条
            RefreshRailInset(specialCard);
            RefreshRailInset(healthCard);
        }

        /// <summary>
        /// 行内度量（幂等）：身份徽章边长 / 图标显示尺寸 / 满能环尺寸 / 轨道宽高。
        /// 桌面与移动两套几何都走这里，分辨率切换重复调用安全。
        /// 右端槽位（Q 键帽 / 读数芯片）已下线：轨道右沿 = 行右沿，条内不留任何附加小件。
        /// </summary>
        public static void ApplyBarRowMetrics(RectTransform row, bool mobile)
        {
            if (row == null) return;

            float badge = mobile ? BarBadgeSizeMobile : BarBadgeSizeDesktop;
            float badgeGap = mobile ? BarBadgeGapMobile : BarBadgeGap;
            float trackW = mobile ? BarTrackWidthMobile : BarTrackWidthDesktop;
            float trackH = mobile ? BarTrackHeightMobile : BarTrackHeightDesktop;
            float iconSize = mobile ? BarBadgeIconSizeMobile : BarBadgeIconSize;
            float ringExtra = mobile ? ReadyRingExtraMobile : ReadyRingExtraDesktop;

            // ① 左端身份徽章：贴着行左沿的垂直居中正圆
            if (row.Find(BarBadgeName) is RectTransform badgeRect)
            {
                badgeRect.sizeDelta = new Vector2(badge, badge);
                badgeRect.anchoredPosition = new Vector2(badge * 0.5f, 0f);

                // 图标随端别取 28 / 32px，徽章变大时内缩同步跟随
                float iconInset = Mathf.Max(0f, (badge - iconSize) * 0.5f);
                ApplyBadgeIcon(badgeRect, SpecialIconName, iconInset);
                ApplyBadgeIcon(badgeRect, HealthBadgeIconName, iconInset);
            }

            // ② 满能状态环：与徽章同心，尺寸 = 徽章 + 外扩量；显隐由 LiquidOrbView 按满能状态切
            if (row.Find(ReadyRingName) is RectTransform ringRect)
            {
                float ringSize = badge + ringExtra;
                ringRect.sizeDelta = new Vector2(ringSize, ringSize);
                ringRect.anchoredPosition = new Vector2(badge * 0.5f, 0f);
            }

            // ③ 墨黑轨道：紧接徽章右侧，一直拉到行右沿
            if (row.Find(BarTrackName) is RectTransform trackRect)
            {
                trackRect.sizeDelta = new Vector2(trackW, trackH);
                trackRect.anchoredPosition = new Vector2(badge + badgeGap, 0f);
            }

        }

        private static void ApplyBadgeIcon(RectTransform badge, string iconName, float inset)
        {
            if (!(badge.Find(iconName) is RectTransform icon)) return;

            icon.anchorMin = Vector2.zero;
            icon.anchorMax = Vector2.one;
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.offsetMin = new Vector2(inset, inset);
            icon.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>遗留：旧右槽读数字号（读数芯片与 Q 键帽已下线，保留给旧调用方）。</summary>
        public static float HealthNumberSize()
        {
            float boosted = UITypography.FontSnapped(HealthNumberFontSize);
            return Mathf.Min(boosted, Mathf.Max(HealthNumberFontSize, HealthNumberMaxFontSize));
        }

        /// <summary>
        /// 旧卡片外壳构建（纸白底 + 3px 墨框 + 面板硬影 + 顶部 5px 身份色带）。
        /// 底部 HUD 已改横条，本方法只为并行工具 / 旧调用方保留；幂等，同名子物体复用。
        /// </summary>
        public static CardChrome BuildCardChrome(RectTransform card, UIThemeSO themeAsset, Color railColor)
        {
            if (card == null) return default;

            UIThemeSO themeResolved = UIThemeSO.Resolve(themeAsset);

            RectTransform bgRect = card.Find(CardBackgroundName) as RectTransform;
            if (bgRect == null)
            {
                bgRect = NewRect(CardBackgroundName, card);
                bgRect.SetAsFirstSibling();
                Stretch(bgRect);
            }

            var bg = bgRect.GetComponent<UIInkShapeGraphic>();
            if (bg == null) bg = bgRect.gameObject.AddComponent<UIInkShapeGraphic>();
            bg.raycastTarget = false;
            bg.Shape = UIInkShape.RoundedRect; // 圆角化：自适应半径，硬影同形状自动跟随
            bg.SpeechTail = false;
            bg.BorderWidth = CardBorderWidth;
            bg.BorderColor = ComicStyle.Ink;
            bg.Halftone = false;
            bg.GlossAlpha = 0f;
            bg.DrawGloss = false;
            bg.InnerRimAlpha = 0f;
            bg.color = ComicStyle.Paper;
            // 面板档硬影 (4,-5)：画在 Rect 右下之外，卡间与屏边已留余量
            themeResolved.ApplyHardShadow(bg, UIHardShadowLevel.Panel);

            RectTransform railRect = card.Find(CardRailName) as RectTransform;
            if (railRect == null)
            {
                railRect = NewRect(CardRailName, card);
            }

            // 圆角化：卡片顶角现在是圆的，色带横向内缩到圆角的直线段内（胶囊端头），不再戳出卡外。
            // 陷阱：BuildCardChrome 常在 PlaceCard 设定卡片尺寸之前调用，card.rect 还是 100×100 默认值；
            // 半径先按已知卡高常量估算，ApplyStatusCardsLayout 拿到最终尺寸后再刷新（RefreshRailInset）。
            float cardHeight = card.rect.height >= 1f ? card.rect.height : CardHeightDesktop;
            float cardRadius = ComicStyle.CornerFor(Mathf.Max(card.rect.width, 1f), cardHeight);
            railRect.anchorMin = new Vector2(0f, 1f);
            railRect.anchorMax = new Vector2(1f, 1f);
            railRect.pivot = new Vector2(0.5f, 1f);
            railRect.sizeDelta = new Vector2(-(cardRadius + CardBorderWidth) * 2f, CardRailHeight);
            railRect.anchoredPosition = new Vector2(0f, -CardBorderWidth);

            var rail = railRect.GetComponent<UIInkShapeGraphic>();
            if (rail == null) rail = railRect.gameObject.AddComponent<UIInkShapeGraphic>();
            rail.raycastTarget = false;
            rail.Shape = UIInkShape.Pill; // 圆角化：5px 身份色带改胶囊短划
            rail.SpeechTail = false;
            rail.BorderWidth = 0f;
            rail.BorderColor = ComicStyle.Ink;
            rail.HardShadow = false;
            rail.ShadowOffset = Vector2.zero;
            rail.Halftone = false;
            rail.GlossAlpha = 0f;
            rail.DrawGloss = false;
            rail.InnerRimAlpha = 0f;
            rail.color = railColor;

            return new CardChrome { Background = bg, Rail = rail };
        }

        // ── 大招横条 ───────────────────────────────────────────────────────

        private static LiquidOrbView BuildSpecialBar(RectTransform card, UIThemeSO theme,
            HudGaugeStyleSO style)
        {
            // 0) 满能状态环：徽章外留 2px 缝的 4px 黄环（填充透明，只画环带），画在徽章之上，
            //    不会被徽章色签硬影吃掉。充能时整块隐藏；满能时配合徽章反色，
            //    「大招好了」不看条长也能一眼读出（静默换状态，不做动画）。
            RectTransform ringRect = NewRect(ReadyRingName, card);
            ringRect.anchorMin = ringRect.anchorMax = new Vector2(0f, 0.5f);
            ringRect.pivot = new Vector2(0.5f, 0.5f);
            float ringSize = BarBadgeSizeDesktop + ReadyRingExtraDesktop;
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            ringRect.anchoredPosition = new Vector2(BarBadgeSizeDesktop * 0.5f, 0f);

            var readyRing = ringRect.gameObject.AddComponent<UIInkShapeGraphic>();
            readyRing.raycastTarget = false;
            readyRing.Shape = UIInkShape.Pill;
            readyRing.SpeechTail = false;
            readyRing.BorderWidth = ReadyRingBand;
            readyRing.BorderColor = ComicStyle.Yellow;
            readyRing.EdgeFeather = BarEdgeFeather;
            readyRing.HardShadow = false;
            readyRing.ShadowOffset = Vector2.zero;
            readyRing.Halftone = false;
            readyRing.GlossAlpha = 0f;
            readyRing.DrawGloss = false;
            readyRing.InnerRimAlpha = 0f;
            readyRing.color = Color.clear; // 只画环带，透明填充不能挡住徽章
            ringRect.gameObject.SetActive(false);

            // 1) 左端身份徽章：黄底正圆 + 墨色技能图标（充能时墨底黄图标，满能翻黄底墨图标）。
            //    色签档硬影 (2,-3)：给横条一点贴纸厚度。
            RectTransform badgeRect = NewRect(BarBadgeName, card);
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(BarBadgeSizeDesktop, BarBadgeSizeDesktop);
            badgeRect.anchoredPosition = new Vector2(BarBadgeSizeDesktop * 0.5f, 0f);

            var badge = badgeRect.gameObject.AddComponent<UIInkShapeGraphic>();
            badge.raycastTarget = false;
            badge.Shape = UIInkShape.Pill; // 方形 rect = 正圆
            badge.SpeechTail = false;
            badge.BorderWidth = ComicStyle.BorderThin;
            badge.BorderColor = ComicStyle.Ink;
            badge.EdgeFeather = BarEdgeFeather;
            badge.HardShadow = false;
            badge.ShadowOffset = Vector2.zero;
            badge.Halftone = false;
            badge.GlossAlpha = 0f;
            badge.DrawGloss = false;
            badge.InnerRimAlpha = 0f;
            badge.color = ComicStyle.Ink;
            UIThemeSO.Resolve(theme).ApplyHardShadow(badge, UIHardShadowLevel.Tag);

            // 中心技能图标（锚定比例铺满徽章内圈，移动端放大时自动跟随）
            RectTransform iconRoot = NewRect(SpecialIconName, badgeRect);
            float iconInset = Mathf.Max(0f, (BarBadgeSizeDesktop - BarBadgeIconSize) * 0.5f);
            iconRoot.anchorMin = Vector2.zero;
            iconRoot.anchorMax = Vector2.one;
            iconRoot.offsetMin = new Vector2(iconInset, iconInset);
            iconRoot.offsetMax = new Vector2(-iconInset, -iconInset);
            Image icon = CreateIconImage("Glyph", iconRoot, ComicStyle.Yellow);

            // 2) 墨黑胶囊轨道（色签硬影）：填充的对比底，也是整条横条的墨描边
            RectTransform trackRect = NewRect(BarTrackName, card);
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 0.5f);
            trackRect.pivot = new Vector2(0f, 0.5f);
            trackRect.sizeDelta = new Vector2(BarTrackWidthDesktop, BarTrackHeightDesktop);
            trackRect.anchoredPosition = new Vector2(BarBadgeSizeDesktop + BarBadgeGap, 0f);

            var track = trackRect.gameObject.AddComponent<UIInkShapeGraphic>();
            track.raycastTarget = false;
            track.Shape = UIInkShape.Pill;
            track.SpeechTail = false;
            track.BorderWidth = 0f;
            track.BorderColor = ComicStyle.Ink;
            track.EdgeFeather = BarEdgeFeather;
            track.HardShadow = false;
            track.ShadowOffset = Vector2.zero;
            track.Halftone = false;
            track.GlossAlpha = 0f;
            track.DrawGloss = false;
            track.InnerRimAlpha = 0f;
            track.color = ComicStyle.Ink;
            UIThemeSO.Resolve(theme).ApplyHardShadow(track, UIHardShadowLevel.Tag);

            // 3) 彩色填充：UIPillImage（胶囊精灵随条宽自适应）+ 左起横向填充
            Image barFill = CreateBarFill(trackRect, ComicStyle.Yellow);

            // 满能环移到最上层：位于徽章之上，环带不会被徽章的色签硬影压暗；
            // 填充/轨道从行内 x=徽章+间隙 才开始，环与它们不重叠，置顶无副作用。
            ringRect.SetAsLastSibling();

            // 4) 满能不做任何附加特效（无爆闪 / 无 Punch / 无条身闪烁）：只让左端徽章静默换状态。
            //    右端也不再挂任何附加小件（Q 键帽 / 读数芯片均已下线），条内只有徽章 + 轨道 + 填充。
            var view = card.gameObject.AddComponent<LiquidOrbView>();
            view.Configure(theme, style, barFill, null, badge, null, null, null, null, null);
            view.ConfigureReadyRing(readyRing);
            view.ConfigureSpecialIcon(iconRoot, icon, null);

            return view;
        }

        /// <summary>
        /// 遗留：满能键帽文案（键帽已下线，保留给旧装配路径）。
        /// </summary>
        private static string ReadyKeyLabel(HudGaugeStyleSO style)
        {
            if (style == null) return "Q";

            string key = style.readyHintKey;
            return string.IsNullOrEmpty(key) ? "Q" : key;
        }

        // ── 共用构件 ────────────────────────────────────────────────────────

        /// <summary>旧卡片大读数字号（底部 HUD 已不显示百分比；保留给旧调用方）。</summary>
        public static float ReadoutFontSize()
        {
            float boosted = UITypography.FontSnapped(CardReadoutFontSize);
            return Mathf.Min(boosted, Mathf.Max(CardReadoutFontSize, MobileReadoutMaxFontSize));
        }

        /// <summary>
        /// 横条填充：UIPillImage（按条宽自动换胶囊精灵）+ Filled/Horizontal/Left。
        /// 内缩 <see cref="BarFillInset"/>，让墨黑轨道露出一圈描边。
        /// </summary>
        public static UIPillImage CreateBarFill(RectTransform track, Color fillColor)
        {
            var go = new GameObject(BarFillName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UIPillImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(track, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(BarFillInset, BarFillInset);
            rect.offsetMax = new Vector2(-BarFillInset, -BarFillInset);

            var fill = go.GetComponent<UIPillImage>();
            fill.raycastTarget = false;
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillClockwise = true;
            fill.fillAmount = 1f;
            return fill;
        }

        /// <summary>按行最终 rect 重算旧卡片顶部身份色带的横向内缩量（遗留节点无操作）。</summary>
        private static void RefreshRailInset(RectTransform card)
        {
            if (card == null || card.rect.width < 1f) return;

            if (card.Find(CardRailName) is RectTransform rail
                && Mathf.Approximately(rail.anchorMin.x, 0f) && Mathf.Approximately(rail.anchorMax.x, 1f))
            {
                float radius = ComicStyle.CornerFor(card.rect.width, card.rect.height);
                rail.sizeDelta = new Vector2(-(radius + CardBorderWidth) * 2f, rail.sizeDelta.y);
            }
        }

        /// <summary>底边左起放一行横条（整行宽 = 横条组宽），返回是否摆放成功。</summary>
        private static void PlaceBarRow(RectTransform row, float y, float width, float height)
        {
            if (row == null) return;

            row.anchorMin = row.anchorMax = new Vector2(0f, 0f);
            row.pivot = new Vector2(0f, 0f);
            row.sizeDelta = new Vector2(width, height);
            row.anchoredPosition = new Vector2(0f, y);
        }

        private static TMP_Text CreateChunkyTmp(string name, Transform parent, UIThemeSO theme,
            float fontSize, string text, Color? fill = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = text;
            tmp.fontSize = fontSize;

            var chunky = go.AddComponent<UIChunkyText>();
            chunky.Configure(theme, text, fontSize, displayFont: true, doubleLayered: false,
                fill: fill ?? ComicStyle.Paper, outlinedText: false);

            return tmp;
        }

        private static Image CreateIconImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            // 必须显式带上 CanvasRenderer：运行时 AddComponent<Graphic> 不会自动补这个组件，
            // 缺了它自绘网格永远不会被提交，只有 Text/TMP 能画出来。
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            Stretch(rect, inset, inset, inset, inset);
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
