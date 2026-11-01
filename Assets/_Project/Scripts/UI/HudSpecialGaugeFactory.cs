using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 程序化装配 HUD 底部「三张状态卡」（P14-H 信息条 → UI 对标 t-1 新野兽派波次收尾）。
    ///
    /// 为什么不再用「一个墨黑大块」：旧版把墨水条 / 大招条 / 生命条 / 键鼠提示塞进 960×120 的黑板，
    /// 用户反馈「一个黑块里面搞这些太丑了」。新语言与首页 2×2 卡一致：
    /// **纸白底 + 3px 墨框 + 面板档硬影 (4,-5) + 顶部 5px 身份色带**，
    /// 三张独立卡并排居中（墨水 332 / 大招 332 / 生命 380，间距 16，底边 y≈56，合计 1076）；
    /// 卡宽比设计稿的「约 300」略宽：display 36 的「100%」约 104px，300 宽塞不下「44 徽章 + 150 轨道 + 大百分比」。
    ///
    /// 键鼠提示条已迁到暂停菜单（右栏「操作说明」卡），HUD 底部不再绘制任何按键提示。
    ///
    /// <code>
    ///  ┌────────────┐ ┌────────────┐ ┌──────────────────┐
    ///  │████ 队伍色 │ │████ 行动黄 │ │████ 语义绿/红    │   ← 5px 身份色带
    ///  │ ▣ █████ 78%│ │ ◆ █████ 42%│ │ HP ███████ 100   │
    ///  └────────────┘ └────────────┘ └──────────────────┘
    /// </code>
    ///
    /// 风格约束（Docs/UI-GeometricBold.md）：纯色直角块、无渐变、无网点；满能只用「整卡翻黄 + 墨字 + Q 释放」
    /// 硬切表达；硬影只走主题档位（卡片 Panel (4,-5)、提示条 Tag (2,-3)）。
    /// 公共 API（BuildCluster / ApplyMobileBottomLayout / GaugeCluster / 常量）保持向后兼容。
    /// </summary>
    public static class HudSpecialGaugeFactory
    {
        /// <summary>装配结果。</summary>
        public struct GaugeCluster
        {
            public LiquidOrbView Orb;
            public InkTankView Tank;
            public RectTransform Root;

            /// <summary>墨水卡根节点（InkTankView 就挂在这个物体上）。</summary>
            public RectTransform InkCard;

            /// <summary>大招卡根节点（LiquidOrbView 就挂在这个物体上）。</summary>
            public RectTransform SpecialCard;
        }

        /// <summary>卡片外壳（纸白底 + 墨框 + 面板硬影 + 顶部身份色带）的两块图形。</summary>
        public struct CardChrome
        {
            public UIInkShapeGraphic Background;
            public UIInkShapeGraphic Rail;
        }

        // ── 三卡区几何（1920×1080 参考分辨率，底边居中） ─────────────────────

        /// <summary>墨水卡宽度。比「约 300」宽 32px：display 36 的「100%」实测约 104px，
        /// 300 宽塞不下「44 徽章 + 150 轨道 + 大百分比」，硬塞会让数字压到轨道上。</summary>
        public const float InkCardWidth = 332f;

        /// <summary>大招卡宽度（与墨水卡同构，理由同上）。</summary>
        public const float SpecialCardWidth = 332f;

        /// <summary>生命卡宽度。</summary>
        public const float HealthCardWidth = 380f;

        /// <summary>桌面卡间距。</summary>
        public const float CardGapDesktop = 16f;

        /// <summary>移动端卡间距。</summary>
        public const float CardGapMobile = 12f;

        /// <summary>桌面卡高。</summary>
        public const float CardHeightDesktop = 88f;

        /// <summary>移动端卡高。</summary>
        public const float CardHeightMobile = 72f;

        /// <summary>桌面卡区底边距屏幕底部（硬影 (4,-5) 向下 5px，仍留 51px）。</summary>
        public const float CardsBottomYDesktop = 56f;

        /// <summary>移动端卡区底边距。</summary>
        public const float CardsBottomYMobile = 46f;

        /// <summary>三卡区根节点宽度 = 300 + 300 + 380 + 16×2；根节点本身不绘制，只是布局参考。</summary>
        public const float CardAreaWidth = InkCardWidth + SpecialCardWidth + HealthCardWidth + CardGapDesktop * 2f;

        // ── 卡片内部（两种布局共用：移动端只收矮卡片，横向排布不变） ─────────

        /// <summary>卡片墨框宽度（规范 3px）。</summary>
        public const float CardBorderWidth = ComicStyle.BorderThin;

        /// <summary>顶部身份色带高度（规范 5px）。</summary>
        public const float CardRailHeight = 5f;

        /// <summary>卡内左右内边距。</summary>
        public const float CardPadding = 12f;

        /// <summary>徽章尺寸（墨底方块 + 4px 身份色边框）。</summary>
        public const float CardBadgeSize = 44f;

        /// <summary>徽章边框宽度（规范 4px）。</summary>
        public const float CardBadgeBorderWidth = ComicStyle.Border;

        /// <summary>徽章与轨道之间的间距。</summary>
        public const float CardBadgeGap = 10f;

        /// <summary>墨水 / 大招轨道宽度（约 150；受 300 级卡宽与 display 36 读数列约束取 142）。</summary>
        public const float MeterTrackWidth = 142f;

        /// <summary>生命轨道宽度。</summary>
        public const float HealthTrackWidth = 220f;

        /// <summary>轨道高度。</summary>
        public const float TrackHeight = 24f;

        /// <summary>右侧大读数基础字号（规范 HUD 英雄数字 40 档；36 适配 88 高卡片）。</summary>
        public const float CardReadoutFontSize = 36f;

        /// <summary>
        /// 移动端读数上限：标题字重（MiSans-Heavy）的「100%」≈ 2.6em，46px 会压住轨道，
        /// 因此移动端按 clamp 收到 38（仍大于桌面 36，不会出现「手机字更小」）。
        /// </summary>
        public const float MobileReadoutMaxFontSize = 38f;

        /// <summary>墨水 / 大招右侧读数列宽（display 36 的「100%」≈104px，留 8px 余量）。</summary>
        public const float MeterReadoutWidth = 112f;

        /// <summary>生命右侧读数列宽。</summary>
        public const float HealthReadoutWidth = 86f;

        /// <summary>读数列右内边距。</summary>
        public const float ReadoutRightPadding = 8f;

        // ── 旧键鼠提示条常量（HUD 已不再显示，保留常量向后兼容测试/工具） ──────
        // 键鼠提示已迁到暂停菜单（PauseMenuOverlay.BuildControlsCard），HUD 底部只留三张状态卡。

        /// <summary>旧键鼠提示条宽度（HUD 已不绘制）。</summary>
        public const float KeyHintWidth = 900f;

        /// <summary>旧键鼠提示条高度（HUD 已不绘制）。</summary>
        public const float KeyHintHeight = 34f;

        /// <summary>旧键鼠提示条底边距（HUD 已不绘制）。</summary>
        public const float KeyHintBottomY = 12f;

        // ── 兼容常量（旧「信息条」命名保留：现指向三卡区总矩形） ─────────────
        // 旧调用方（测试 / 编辑器工具）仍按这两个名字读取底部矩形，语义换成三卡包围盒。

        /// <summary>三卡区宽度（旧信息条宽度）。</summary>
        public const float StripWidth = CardAreaWidth;

        /// <summary>桌面板高等价于卡高（旧信息条高度）。</summary>
        public const float StripHeightDesktop = CardHeightDesktop;

        /// <summary>移动端板高等价于卡高（旧信息条高度）。</summary>
        public const float StripHeightMobile = CardHeightMobile;

        /// <summary>桌面板底边距（旧信息条底边距）。</summary>
        public const float StripBottomYDesktop = CardsBottomYDesktop;

        /// <summary>移动端板底边距（旧信息条底边距）。</summary>
        public const float StripBottomYMobile = CardsBottomYMobile;

        private const string CardBackgroundName = "CardBg";
        private const string CardRailName = "Rail";

        private struct BarParts
        {
            public RectTransform Track;
            public Image Fill;
            public UIInkShapeGraphic Frame;
        }

        public static GaugeCluster BuildCluster(Transform canvasRoot, UIThemeSO theme, HudGaugeStyleSO styleAsset)
        {
            HudGaugeStyleSO style = HudGaugeStyleSO.Resolve(styleAsset);
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            var rootGo = new GameObject("StatusCardsRoot", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvasRoot, false);
            ApplyStatusCardsLayout(root, null, null, null, false);

            // 先关掉再装配：让两个 View 的 Awake / OnEnable 在字段写好后运行
            rootGo.SetActive(false);

            // 1) 墨水卡：身份色带 = 队伍色（本地玩家队伍由 InkTankView 的 LateUpdate 追进来）
            Color inkAccent = InkTeamColorFilter.ResolveTeamColor(Team.Neutral, themeResolved);
            RectTransform inkCard = NewRect("InkCard", root);
            CardChrome inkChrome = BuildCardChrome(inkCard, theme, inkAccent);
            InkTankView tank = BuildInkMeter(inkCard, inkChrome, theme, style);

            // 2) 大招卡：身份色带 = 主行动黄
            RectTransform specialCard = NewRect("SpecialCard", root);
            CardChrome specialChrome = BuildCardChrome(specialCard, theme, ComicStyle.Yellow);
            LiquidOrbView orb = BuildSpecialMeter(specialCard, specialChrome, theme, style);

            var cluster = new GaugeCluster
            {
                Root = root,
                InkCard = inkCard,
                SpecialCard = specialCard,
                Tank = tank,
                Orb = orb,
            };

            ApplyStatusCardsLayout(root, inkCard, specialCard, null, false);
            rootGo.SetActive(true);
            return cluster;
        }

        /// <summary>
        /// 移动端适配：键盘「Q 释放」提示交给触控大招键，卡片横向位置不变
        /// （三卡区宽 1076 居中，左右各留 422px 给摇杆与操作键）。
        /// </summary>
        public static void ApplyMobileBottomLayout(InkTankView tank, LiquidOrbView orb)
        {
            if (tank != null) tank.SetCompactBottomLayout(true);
            if (orb != null) orb.SetCompactBottomLayout(true);
        }

        /// <summary>
        /// 三卡区布局（唯一入口）：桌面 / 移动两套几何，幂等，可在分辨率变化时重复调用。
        /// cardsRoot 尺寸 = 三卡包围盒；每张卡按底边左起累计排布并整体居中。
        /// </summary>
        public static void ApplyStatusCardsLayout(RectTransform cardsRoot, RectTransform inkCard,
            RectTransform specialCard, RectTransform healthCard, bool mobile)
        {
            if (cardsRoot == null) return;

            float height = mobile ? CardHeightMobile : CardHeightDesktop;
            float bottomY = mobile ? CardsBottomYMobile : CardsBottomYDesktop;
            float gap = mobile ? CardGapMobile : CardGapDesktop;

            cardsRoot.anchorMin = cardsRoot.anchorMax = new Vector2(0.5f, 0f);
            cardsRoot.pivot = new Vector2(0.5f, 0f);
            cardsRoot.sizeDelta = new Vector2(CardAreaWidth, height);
            cardsRoot.anchoredPosition = new Vector2(0f, bottomY);

            float total = InkCardWidth + SpecialCardWidth + HealthCardWidth + gap * 2f;
            // 卡片本身锚定根节点左下角（anchor 0,0），x 从根矩形左边起算；
            // 根节点已居中，因此这里要把「根宽 − 卡区总宽」的一半作为余量，桌面两端各 0、移动端各 4px。
            float x = (CardAreaWidth - total) * 0.5f;
            x = PlaceCard(inkCard, x, InkCardWidth, height, gap);
            x = PlaceCard(specialCard, x, SpecialCardWidth, height, gap);
            PlaceCard(healthCard, x, HealthCardWidth, height, gap);
        }

        /// <summary>
        /// 卡片外壳（纸白底 + 3px 墨框 + 面板档硬影 + 顶部 5px 身份色带）。
        /// 幂等：同名子物体已存在时复用，只重刷外观（供运行时反复布局 / 迁移旧节点）。
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
            bg.Shape = UIInkShape.SharpRect;
            bg.CornerRadius = ComicStyle.CornerSharp;
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

            // 色带内缩边框宽度，只占顶边内侧 5px：不让它盖住 3px 墨框
            railRect.anchorMin = new Vector2(0f, 1f);
            railRect.anchorMax = new Vector2(1f, 1f);
            railRect.pivot = new Vector2(0.5f, 1f);
            railRect.sizeDelta = new Vector2(-CardBorderWidth * 2f, CardRailHeight);
            railRect.anchoredPosition = new Vector2(0f, -CardBorderWidth);

            var rail = railRect.GetComponent<UIInkShapeGraphic>();
            if (rail == null) rail = railRect.gameObject.AddComponent<UIInkShapeGraphic>();
            rail.raycastTarget = false;
            rail.Shape = UIInkShape.SharpRect;
            rail.CornerRadius = ComicStyle.CornerSharp;
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

        // ── 墨水卡 ─────────────────────────────────────────────────────────

        private static InkTankView BuildInkMeter(RectTransform card, CardChrome chrome, UIThemeSO theme,
            HudGaugeStyleSO style)
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);
            Color accent = chrome.Rail != null
                ? chrome.Rail.color
                : InkTeamColorFilter.ResolveTeamColor(Team.Neutral, themeResolved);

            float trackLeft = CardPadding + CardBadgeSize + CardBadgeGap;

            // 1) 低墨告警红光：垫在轨道后方，平时完全透明
            RectTransform glowRect = NewRect("WarningGlow", card);
            PlaceLeft(glowRect, trackLeft - 3f, MeterTrackWidth + 6f, TrackHeight + 6f);
            var warningGlow = glowRect.gameObject.AddComponent<UIInkShapeGraphic>();
            warningGlow.raycastTarget = false;
            warningGlow.Shape = UIInkShape.SharpRect;
            warningGlow.CornerRadius = ComicStyle.CornerSharp;
            warningGlow.BorderWidth = 0f;
            warningGlow.HardShadow = false;
            warningGlow.Halftone = false;
            warningGlow.GlossAlpha = 0f;
            warningGlow.InnerRimAlpha = 0f;
            warningGlow.color = new Color(ComicStyle.Red.r, ComicStyle.Red.g, ComicStyle.Red.b, 0f);

            // 2) 墨底方形徽章 + 4px 队伍色边框 + 队伍色水滴图标
            RectTransform plateRect = NewRect("InkBadge", card);
            PlaceLeft(plateRect, CardPadding, CardBadgeSize, CardBadgeSize);
            var plate = plateRect.gameObject.AddComponent<UIInkShapeGraphic>();
            plate.raycastTarget = false;
            plate.Shape = UIInkShape.SharpRect;
            plate.CornerRadius = ComicStyle.CornerSharp;
            plate.BorderWidth = CardBadgeBorderWidth;
            plate.BorderColor = accent;
            plate.HardShadow = false;
            plate.Halftone = false;
            plate.GlossAlpha = 0f;
            plate.InnerRimAlpha = 0f;
            plate.color = ComicStyle.Ink;

            Image icon = CreateIcon("InkIcon", plateRect, "glyph_drop");
            var iconRect = (RectTransform)icon.transform;
            Stretch(iconRect, 6f);
            icon.color = accent;

            // 3) 轨道（纸白 ALT 底 + 3px 墨边 + 队伍色填充）
            BarParts bar = BuildBar(card, "InkBar", trackLeft, MeterTrackWidth, accent);

            // 4) 副武器耗墨刻度：竖红线（横向条上的战术刻度线，标记手雷阈值）
            RectTransform marker = NewRect("BombThreshold", bar.Track);
            marker.anchorMin = marker.anchorMax = new Vector2(0f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(4f, TrackHeight + 4f);
            marker.anchoredPosition = new Vector2(2f, 0f);

            CreateFlatImage("ThresholdBacking", marker, ComicStyle.Ink, new Vector2(6f, TrackHeight + 4f));
            CreateFlatImage("ThresholdLine", marker, ComicStyle.Red, new Vector2(3f, TrackHeight + 2f));
            var cap = CreateFlatImage("ThresholdCap", marker, ComicStyle.Red, new Vector2(7f, 4f));
            var capRect = (RectTransform)cap.transform;
            capRect.anchoredPosition = new Vector2(0f, (TrackHeight + 4f) * 0.5f - 2f);

            // 5) 右侧大百分比：display 字体 36，墨色
            TMP_Text percent = CreateReadout("Percent", card, theme, MeterReadoutWidth);

            var view = card.gameObject.AddComponent<InkTankView>();
            view.Configure(theme, style, bar.Fill, marker, bar.Frame, warningGlow, percent, 2f);
            view.SetCardChrome(chrome.Rail, plate, icon);
            // 首帧 HudController 才会按 WeaponConfig 刷新；先用样式默认值落位，避免红线跳一下
            view.SetThreshold01(style.bombThreshold01);

            return view;
        }

        // ── 大招卡 ─────────────────────────────────────────────────────────

        private static LiquidOrbView BuildSpecialMeter(RectTransform card, CardChrome chrome, UIThemeSO theme,
            HudGaugeStyleSO style)
        {
            float trackLeft = CardPadding + CardBadgeSize + CardBadgeGap;

            // 1) 徽章：与墨水卡同构的墨底方块 + 4px 行动黄边框 + 大招图标；
            //    满能时整卡翻黄（几何风的「就绪」硬切），徽章仍是墨底的重心
            RectTransform plateRect = NewRect("IconPlate", card);
            PlaceLeft(plateRect, CardPadding, CardBadgeSize, CardBadgeSize);
            var plate = plateRect.gameObject.AddComponent<UIInkShapeGraphic>();
            plate.raycastTarget = false;
            plate.Shape = UIInkShape.SharpRect;
            plate.CornerRadius = ComicStyle.CornerSharp;
            plate.BorderWidth = CardBadgeBorderWidth;
            plate.BorderColor = ComicStyle.Yellow;
            plate.HardShadow = false;
            plate.Halftone = false;
            plate.GlossAlpha = 0f;
            plate.InnerRimAlpha = 0f;
            plate.color = ComicStyle.Ink;

            RectTransform iconRoot = NewRect("SpecialIcon", plateRect);
            Stretch(iconRoot, 6f);
            Image icon = CreateIconImage("Glyph", iconRoot, ComicStyle.Yellow);

            // 2) 轨道（纸白 ALT 底 + 3px 墨边 + 行动黄填充）
            BarParts bar = BuildBar(card, "SpecialBar", trackLeft, MeterTrackWidth, ComicStyle.Yellow);

            // 3) 右侧大百分比（满能时让位给「Q 释放」小签）
            TMP_Text percent = CreateReadout("Percent", card, theme, MeterReadoutWidth);

            RectTransform hintRect = NewRect("ReadyHint", card);
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(1f, 0.5f);
            hintRect.pivot = new Vector2(1f, 0.5f);
            hintRect.sizeDelta = new Vector2(MeterReadoutWidth, 36f);
            hintRect.anchoredPosition = new Vector2(-ReadoutRightPadding, 0f);

            var hintPill = hintRect.gameObject.AddComponent<UIInkShapeGraphic>();
            hintPill.raycastTarget = false;
            hintPill.Shape = UIInkShape.SharpRect;
            hintPill.CornerRadius = ComicStyle.CornerSharp;
            hintPill.SpeechTail = false;
            hintPill.BorderWidth = ComicStyle.BorderThin;
            hintPill.BorderColor = ComicStyle.Ink;
            hintPill.HardShadow = false;
            hintPill.ShadowOffset = Vector2.zero;
            hintPill.Halftone = false;
            hintPill.color = ComicStyle.Yellow;
            // 满能提示 = 色签档硬影（(2,-3)，画在 Rect 右下之外）
            UIThemeSO.Resolve(theme).ApplyHardShadow(hintPill, UIHardShadowLevel.Tag);

            float hintFontSize = UITypography.FontSnapped(20f);
            TMP_Text hintLabel = CreateChunkyTmp("HintLabel", hintRect, theme, hintFontSize,
                ReadyHintText(style), ComicStyle.Ink);
            hintLabel.alignment = TextAlignmentOptions.Center;

            var hintMotion = hintRect.gameObject.AddComponent<UISpringMotion>();
            hintMotion.SetTheme(theme);

            var view = card.gameObject.AddComponent<LiquidOrbView>();
            view.Configure(theme, style, bar.Fill, percent, plate,
                hintRect, hintPill, hintLabel, hintMotion, ReadyHintText(style));
            view.ConfigureSpecialIcon(iconRoot, icon, null);
            view.SetCardChrome(chrome.Background, chrome.Rail);

            return view;
        }

        /// <summary>
        /// 按键提示文案：右侧读数列只有 78px，SO 里的「按 Q 释放」会溢出，
        /// 这里压成「Q 释放」；没有按键名时退回原文。
        /// </summary>
        private static string ReadyHintText(HudGaugeStyleSO style)
        {
            if (style == null) return "Q 释放";

            string key = style.readyHintKey;
            if (string.IsNullOrEmpty(key)) return style.readyHintText;

            string text = style.readyHintText;
            if (string.IsNullOrEmpty(text)) text = "释放";

            int keyPosition = text.IndexOf(key, System.StringComparison.Ordinal);
            if (keyPosition > 0) text = text.Substring(keyPosition);

            return text;
        }

        // ── 共用构件 ────────────────────────────────────────────────────────

        /// <summary>轨道 + 纯色填充 + 3px 墨框；三者都以卡片左边为基准横向排布。</summary>
        private static BarParts BuildBar(Transform parent, string name, float left, float width, Color fillColor)
        {
            RectTransform track = NewRect(name, parent);
            PlaceLeft(track, left, width, TrackHeight);

            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.raycastTarget = false;
            // 纸白 ALT 底：卡片是纸白，轨道要低一级才看得见
            trackImage.color = ComicStyle.PaperAlt;
            UIFlatSprite.MakeFlat(trackImage);

            RectTransform fillRect = NewRect("Fill", track);
            Stretch(fillRect);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            fill.color = fillColor;
            UIFlatSprite.MakeFlat(fill, filled: true);
            fill.fillAmount = 1f;

            RectTransform frameRect = NewRect("Frame", parent);
            PlaceLeft(frameRect, left, width, TrackHeight);
            var frame = frameRect.gameObject.AddComponent<UIInkShapeGraphic>();
            frame.raycastTarget = false;
            frame.Shape = UIInkShape.SharpRect;
            frame.CornerRadius = ComicStyle.CornerSharp;
            frame.BorderWidth = ComicStyle.BorderThin;
            frame.BorderColor = ComicStyle.Ink;
            frame.HardShadow = false;
            frame.ShadowOffset = Vector2.zero;
            frame.Halftone = false;
            frame.GlossAlpha = 0f;
            frame.DrawGloss = false;
            frame.InnerRimAlpha = 0f;
            frame.color = new Color(1f, 1f, 1f, 0f);

            return new BarParts { Track = track, Fill = fill, Frame = frame };
        }

        /// <summary>卡片右侧大读数（display 字体 36、墨色、右对齐固定列，数字变化不抖动）。</summary>
        private static TMP_Text CreateReadout(string name, Transform parent, UIThemeSO theme, float width)
        {
            float fontSize = ReadoutFontSize();
            TMP_Text label = CreateChunkyTmp(name, parent, theme, fontSize, "100%", ComicStyle.Ink);

            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, 48f);
            rect.anchoredPosition = new Vector2(-ReadoutRightPadding, 0f);
            label.alignment = TextAlignmentOptions.MidlineRight;
            return label;
        }

        /// <summary>
        /// 卡片大读数字号：桌面 36；移动端走 UITypography 放大但 clamp 到
        /// <see cref="MobileReadoutMaxFontSize"/>（38），保证「100%」不压到轨道。
        /// HudController 的生命数字也走这里，三张卡读数保持同档。
        /// </summary>
        public static float ReadoutFontSize()
        {
            float boosted = UITypography.FontSnapped(CardReadoutFontSize);
            return Mathf.Min(boosted, Mathf.Max(CardReadoutFontSize, MobileReadoutMaxFontSize));
        }

        /// <summary>左中锚定放置（卡内徽章 / 轨道 / 描边共用一套坐标）。</summary>
        private static void PlaceLeft(RectTransform rect, float left, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, 0f);
        }

        /// <summary>底边左起放置卡片，返回下一张卡的起始 x。</summary>
        private static float PlaceCard(RectTransform card, float x, float width, float height, float gap)
        {
            if (card != null)
            {
                card.anchorMin = card.anchorMax = new Vector2(0f, 0f);
                card.pivot = new Vector2(0f, 0f);
                card.sizeDelta = new Vector2(width, height);
                card.anchoredPosition = new Vector2(x, 0f);
            }

            return x + width + gap;
        }

        private static Image CreateFlatImage(string name, Transform parent, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            UIFlatSprite.MakeFlat(image);
            return image;
        }

        private static Image CreateIcon(string name, Transform parent, string iconName)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;

            Sprite sprite = UIIconLibrary.Get(iconName);
            image.sprite = sprite;
            image.enabled = sprite != null;
            return image;
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
            tmp.alignment = TextAlignmentOptions.MidlineRight;
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