using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 程序化装配 HUD 底部中央仪表条（P14-H 重做，取代旧的「右上角竖墨罐 + 大招球」）。
    ///
    /// 为什么改成横向：本作同时出桌面与横屏移动端，屏幕左右下角在移动端是摇杆 / 开火 / 跳跃 / 大招
    /// 五个触控键的地盘；把仪表挂在两侧等于让手指盖住读数。因此墨水量与大招充能统一收进
    /// 底部中央的黑底信息条，**永远落在血条正上方**，桌面与移动端同一套位置：
    ///
    /// <code>
    ///  ┌──────────────────────────────────────────────┐
    ///  │ 💧 ██████████████░░ 78%   ◆ ██████░░░ 42%    │  ← 横向仪表行（本文件生成）
    ///  │ ███████████████████████████████████████████ │  ← 生命条（HudController）
    ///  │ WASD 移动 · 鼠标 视角 · …                    │  ← 键盘提示（桌面）
    ///  └──────────────────────────────────────────────┘
    /// </code>
    ///
    /// 风格约束（Docs/UI-GeometricBold.md）：纯色直角块、无渐变、无阴影、无网点，
    /// 数字用主题显示字体，满能只用「黄块 + 黄条」硬切表达。
    /// </summary>
    public static class HudSpecialGaugeFactory
    {
        /// <summary>装配结果。</summary>
        public struct GaugeCluster
        {
            public LiquidOrbView Orb;
            public InkTankView Tank;
            public RectTransform Root;
        }

        /// <summary>底部信息条宽度（1920×1080 参考分辨率）。</summary>
        public const float StripWidth = 940f;

        /// <summary>桌面信息条高度（含键盘提示行）。</summary>
        public const float StripHeightDesktop = 104f;

        /// <summary>移动端信息条高度（隐藏键盘提示行后收紧）。</summary>
        public const float StripHeightMobile = 76f;

        /// <summary>桌面板底边距屏幕底部的距离（面板与仪表条共用，轴心都在底边）。</summary>
        public const float StripBottomYDesktop = 12f;

        /// <summary>移动端板底边距；与桌面一致，靠隐藏提示行把面板收矮。</summary>
        public const float StripBottomYMobile = 12f;

        /// <summary>仪表行高度。</summary>
        public const float MeterRowHeight = 28f;

        /// <summary>仪表行距信息条顶边的内边距。</summary>
        public const float MeterRowTopInset = 8f;

        private const float SideMargin = 24f;
        private const float MeterGap = 32f;
        private const float GroupWidth = (StripWidth - SideMargin * 2f - MeterGap) * 0.5f;
        private const float BarHeight = 18f;
        private const float PercentColumn = 92f;
        private const float PercentGap = 10f;
        private const float InkIconColumn = 34f;
        private const float SpecialIconColumn = 36f;
        private const float SpecialPlateSize = 28f;
        private const float InkBarWidth = GroupWidth - InkIconColumn - PercentGap - PercentColumn;
        private const float SpecialBarWidth = GroupWidth - SpecialIconColumn - PercentGap - PercentColumn;

        private struct BarParts
        {
            public RectTransform Track;
            public Image Fill;
            public UIInkShapeGraphic Frame;
        }

        public static GaugeCluster BuildCluster(Transform canvasRoot, UIThemeSO theme, HudGaugeStyleSO styleAsset)
        {
            HudGaugeStyleSO style = HudGaugeStyleSO.Resolve(styleAsset);

            var rootGo = new GameObject("SpecialGaugeCluster", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvasRoot, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(StripWidth, StripHeightDesktop);
            root.anchoredPosition = new Vector2(0f, StripBottomYDesktop);

            // 先关掉再装配：让两个 View 的 Awake / OnEnable 在字段写好后运行
            rootGo.SetActive(false);

            RectTransform row = NewRect("MeterRow", root);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-SideMargin * 2f, MeterRowHeight);
            row.anchoredPosition = new Vector2(0f, -MeterRowTopInset);

            var cluster = new GaugeCluster
            {
                Root = root,
                Tank = BuildInkMeter(row, theme, style),
                Orb = BuildSpecialMeter(row, theme, style),
            };

            rootGo.SetActive(true);
            return cluster;
        }

        /// <summary>
        /// 移动端适配：键盘「Q 释放」提示交给触控大招键，仪表本身位置不变
        /// （两条横向仪表本来就在屏幕中央、血条上方，不会压到左右触控区）。
        /// </summary>
        public static void ApplyMobileBottomLayout(InkTankView tank, LiquidOrbView orb)
        {
            if (tank != null) tank.SetCompactBottomLayout(true);
            if (orb != null) orb.SetCompactBottomLayout(true);
        }

        // ── 墨水条 ─────────────────────────────────────────────────────────

        private static InkTankView BuildInkMeter(RectTransform row, UIThemeSO theme, HudGaugeStyleSO style)
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            RectTransform group = NewRect("InkTank", row);
            group.anchorMin = group.anchorMax = new Vector2(0f, 0.5f);
            group.pivot = new Vector2(0f, 0.5f);
            group.sizeDelta = new Vector2(GroupWidth, MeterRowHeight);
            group.anchoredPosition = Vector2.zero;

            // 1) 低墨告警红光：垫在轨道后方，平时完全透明
            RectTransform glowRect = NewRect("WarningGlow", group);
            PlaceLeft(glowRect, InkIconColumn - 3f, InkBarWidth + 6f, BarHeight + 6f);
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

            // 2) 图标（墨滴）：白色矢量，读作「墨水」，比中文字省一行高度
            Image icon = CreateIcon("InkIcon", group, "glyph_drop");
            var iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            iconRect.anchoredPosition = new Vector2(InkIconColumn * 0.5f - 2f, 0f);
            icon.color = ComicStyle.Paper;

            // 3) 轨道 + 纯色填充 + 描边
            BarParts bar = BuildBar(group, "InkBar", InkIconColumn, InkBarWidth,
                InkTeamColorFilter.ResolveTeamColor(Team.Neutral, themeResolved));

            // 4) 副武器耗墨刻度：竖红线（横向条的刻度是竖线，不再是横线）
            RectTransform marker = NewRect("BombThreshold", bar.Track);
            marker.anchorMin = marker.anchorMax = new Vector2(0f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(3f, BarHeight);
            marker.anchoredPosition = new Vector2(2f, 0f);

            CreateFlatImage("ThresholdBacking", marker, ComicStyle.Ink, new Vector2(5f, BarHeight));
            CreateFlatImage("ThresholdLine", marker, ComicStyle.Red, new Vector2(3f, BarHeight));

            // 5) 百分比读数：右对齐固定列，数字变化不抖动
            TMP_Text percent = CreateMeterPercent("Percent", group, theme, style);

            var view = group.gameObject.AddComponent<InkTankView>();
            view.Configure(theme, style, bar.Fill, marker, bar.Frame, warningGlow, percent, 2f);
            // 首帧 HudController 才会按 WeaponConfig 刷新；先用样式默认值落位，避免红线跳一下
            view.SetThreshold01(style.bombThreshold01);

            return view;
        }

        // ── 大招条 ─────────────────────────────────────────────────────────

        private static LiquidOrbView BuildSpecialMeter(RectTransform row, UIThemeSO theme, HudGaugeStyleSO style)
        {
            RectTransform group = NewRect("LiquidOrb", row);
            group.anchorMin = group.anchorMax = new Vector2(1f, 0.5f);
            group.pivot = new Vector2(1f, 0.5f);
            group.sizeDelta = new Vector2(GroupWidth, MeterRowHeight);
            group.anchoredPosition = Vector2.zero;

            // 1) 技能图标徽章：白方块 + 墨黑矢量图标；满能整块翻黄（几何风的「就绪」硬切）
            RectTransform plateRect = NewRect("IconPlate", group);
            PlaceLeft(plateRect, 0f, SpecialPlateSize, SpecialPlateSize);
            var plate = plateRect.gameObject.AddComponent<UIInkShapeGraphic>();
            plate.raycastTarget = false;
            plate.Shape = UIInkShape.SharpRect;
            plate.CornerRadius = ComicStyle.CornerSharp;
            plate.BorderWidth = 0f;
            plate.HardShadow = false;
            plate.Halftone = false;
            plate.GlossAlpha = 0f;
            plate.InnerRimAlpha = 0f;
            plate.color = ComicStyle.Paper;

            // 2) 技能图标：白方块（满能翻黄）+ 墨黑矢量图标。
            //    几何风不做旋转虚线环 / 放射爆闪——满能信号由「黄块 + 黄条 + Q 释放」三处硬切表达。
            RectTransform iconRoot = NewRect("SpecialIcon", plateRect);
            Stretch(iconRoot, 4f);
            Image icon = CreateIconImage("Glyph", iconRoot, ComicStyle.Ink);

            // 4) 轨道 + 纯色填充 + 描边
            BarParts bar = BuildBar(group, "SpecialBar", SpecialIconColumn, SpecialBarWidth, ComicStyle.Yellow);

            // 5) 百分比读数 / 满能按键提示共用右侧一列
            TMP_Text percent = CreateMeterPercent("Percent", group, theme, style);

            RectTransform hintRect = NewRect("ReadyHint", group);
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(1f, 0.5f);
            hintRect.pivot = new Vector2(1f, 0.5f);
            hintRect.sizeDelta = new Vector2(
                Mathf.Clamp(style.readyHintSize.x, 60f, PercentColumn),
                Mathf.Clamp(style.readyHintSize.y, 22f, MeterRowHeight));
            hintRect.anchoredPosition = Vector2.zero;

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

            float hintFontSize = UITypography.FontSnapped(16f);
            TMP_Text hintLabel = CreateChunkyTmp("HintLabel", hintRect, theme, hintFontSize,
                ReadyHintText(style), ComicStyle.Ink);
            hintLabel.alignment = TextAlignmentOptions.Center;

            var hintMotion = hintRect.gameObject.AddComponent<UISpringMotion>();
            hintMotion.SetTheme(theme);

            var view = group.gameObject.AddComponent<LiquidOrbView>();
            view.Configure(theme, style, bar.Fill, percent, plate,
                hintRect, hintPill, hintLabel, hintMotion, ReadyHintText(style));
            view.ConfigureSpecialIcon(iconRoot, icon, null);

            return view;
        }

        /// <summary>
        /// 按键提示文案：右侧读数列只有 92px，SO 里的「按 Q 释放」会溢出，
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

        private static BarParts BuildBar(Transform parent, string name, float left, float width, Color fillColor)
        {
            RectTransform track = NewRect(name, parent);
            PlaceLeft(track, left, width, BarHeight);

            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.raycastTarget = false;
            trackImage.color = new Color(ComicStyle.Paper.r, ComicStyle.Paper.g, ComicStyle.Paper.b, 0.16f);
            UIFlatSprite.MakeFlat(trackImage);

            RectTransform fillRect = NewRect("Fill", track);
            Stretch(fillRect);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            fill.color = fillColor;
            UIFlatSprite.MakeFlat(fill, filled: true);
            fill.fillAmount = 1f;

            RectTransform frameRect = NewRect("Frame", parent);
            PlaceLeft(frameRect, left, width, BarHeight);
            var frame = frameRect.gameObject.AddComponent<UIInkShapeGraphic>();
            frame.raycastTarget = false;
            frame.Shape = UIInkShape.SharpRect;
            frame.CornerRadius = ComicStyle.CornerSharp;
            frame.BorderWidth = ComicStyle.BorderThin;
            frame.BorderColor = ComicStyle.Paper;
            frame.HardShadow = false;
            frame.ShadowOffset = Vector2.zero;
            frame.Halftone = false;
            frame.GlossAlpha = 0f;
            frame.DrawGloss = false;
            frame.InnerRimAlpha = 0f;
            frame.color = new Color(1f, 1f, 1f, 0f);

            return new BarParts { Track = track, Fill = fill, Frame = frame };
        }

        private static TMP_Text CreateMeterPercent(string name, Transform parent, UIThemeSO theme,
            HudGaugeStyleSO style)
        {
            float fontSize = UITypography.FontSnapped(Mathf.Clamp(style.percentFontSize, 20f, 26f));
            TMP_Text label = CreateChunkyTmp(name, parent, theme, fontSize, "100%", ComicStyle.Paper);

            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(PercentColumn, MeterRowHeight);
            rect.anchoredPosition = Vector2.zero;
            label.alignment = TextAlignmentOptions.Right;
            return label;
        }

        /// <summary>左中锚定放置（横向仪表条的图标 / 轨道 / 描边共用一套坐标）。</summary>
        private static void PlaceLeft(RectTransform rect, float left, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, 0f);
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
            tmp.alignment = TextAlignmentOptions.Bottom;
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
