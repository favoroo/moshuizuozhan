using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 程序化装配 HUD 仪表簇（P4-C）：桌面在右上角，移动端由
/// <see cref="ApplyMobileBottomLayout"/> 搬到中间底部，避开摇杆与操作按键。
    ///
    /// 全部用代码生成，不写场景、不依赖 Prefab，因此：
    /// - 现有主场景无需重建即可获得新 HUD（HudController 在 Awake 里自举）；
    /// - 后续若想持久化摆放，编辑器工具或 P4-B/D 可以直接调用本工厂。
    ///
    /// 颜色 / 字体 / 弹簧来自 <see cref="UIThemeSO"/>，几何 / 阈值 / 节拍来自 <see cref="HudGaugeStyleSO"/>；
    /// 唯一的外部素材是球心大招图标（`Art/Sprites/Icons/Specials`），其余全部自绘。
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

        private static Font _legacyFont;

        public static GaugeCluster BuildCluster(Transform canvasRoot, UIThemeSO theme, HudGaugeStyleSO styleAsset)
        {
            HudGaugeStyleSO style = HudGaugeStyleSO.Resolve(styleAsset);

            var rootGo = new GameObject("SpecialGaugeCluster", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(canvasRoot, false);
            Stretch(root);

            // 先关掉再装配：让 UIChunkyText / UISpringMotion 等组件的 Awake 在字段写好后运行
            rootGo.SetActive(false);

            var cluster = new GaugeCluster
            {
                Root = root,
                Orb = BuildOrb(root, theme, style),
                Tank = BuildTank(root, theme, style),
            };

            rootGo.SetActive(true);
            return cluster;
        }

        /// <summary>
        /// 移动端：把右上角「墨水管 + 大招球」搬到中间底部，做成紧凑横排。
        /// 位置经过验算：落在血条上方、左侧摇杆（中心 200,200，半径 170）右侧边界之外，
        /// 且远离右下角操作按键，HUD 本身不拦截触控（sortingOrder 低于触控层）。
        /// </summary>
        public static void ApplyMobileBottomLayout(InkTankView tank, LiquidOrbView orb)
        {
            if (orb != null)
            {
                var orbRect = (RectTransform)orb.transform;
                orbRect.anchorMin = orbRect.anchorMax = new Vector2(0.5f, 0f);
                orbRect.pivot = new Vector2(0.5f, 0.5f);
                orbRect.sizeDelta = new Vector2(84f, 84f);
                orbRect.anchoredPosition = new Vector2(-75f, 108f);
                orb.SetCompactBottomLayout(true);
            }

            if (tank != null)
            {
                var tankRect = (RectTransform)tank.transform;
                tankRect.anchorMin = tankRect.anchorMax = new Vector2(0.5f, 0f);
                tankRect.pivot = new Vector2(0.5f, 0.5f);
                tankRect.sizeDelta = new Vector2(36f, 116f);
                tankRect.anchoredPosition = new Vector2(-4f, 116f);
                tank.SetCompactBottomLayout(true);
            }
        }

        private static LiquidOrbView BuildOrb(RectTransform parent, UIThemeSO theme, HudGaugeStyleSO style)
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            var orbGo = new GameObject("LiquidOrb", typeof(RectTransform));
            var orbRect = (RectTransform)orbGo.transform;
            orbRect.SetParent(parent, false);
            orbRect.anchorMin = orbRect.anchorMax = new Vector2(1f, 1f);
            orbRect.pivot = new Vector2(0.5f, 0.5f);
            orbRect.sizeDelta = Vector2.one * style.orbSize;
            orbRect.anchoredPosition = style.orbAnchoredPosition;

            // Pulse 层：满能时整簇缩放震颤，不影响外层锚点
            RectTransform pulse = NewRect("Pulse", orbRect);
            Stretch(pulse);

            // 0) 满能放射动作线（漫画 POW! 能量感）
            RectTransform linesRect = NewRect("ActionLines", pulse);
            Stretch(linesRect, -32f);
            var actionLines = linesRect.gameObject.AddComponent<ComicActionLinesGraphic>();
            actionLines.raycastTarget = false;
            actionLines.RayCount = 20;
            actionLines.Alpha = 0f;
            actionLines.color = ComicStyle.Yellow;

            // 1) 金色旋转光环（球体外围）
            RectTransform haloRect = NewRect("ReadyHalo", pulse);
            Stretch(haloRect);
            var halo = haloRect.gameObject.AddComponent<UIRotatingRingGraphic>();
            halo.raycastTarget = false;
            halo.Configure(1.18f, 5f, style.haloDashCount, style.haloSpinDegreesPerSecond, themeResolved.readyGold);

            // 2) 满能爆闪（冲击波 + 射线）
            RectTransform burstRect = NewRect("ReadyBurst", pulse);
            Stretch(burstRect);
            var burst = burstRect.gameObject.AddComponent<UIBurstGraphic>();
            burst.raycastTarget = false;
            burst.Configure(style.burstDuration, style.burstRayCount, themeResolved.readyGold);

            // 3) 球体底：漫画气泡，粗墨边 6px + 硬阴影 (6,-6) + 半调网点
            RectTransform bodyRect = NewRect("Body", pulse);
            Stretch(bodyRect);
            UIInkShapeGraphic bodyShape = bodyRect.gameObject.AddComponent<UIInkShapeGraphic>();
            bodyShape.raycastTarget = false;
            bodyShape.Shape = UIInkShape.Blob;
            bodyShape.BorderWidth = ComicStyle.Border;
            bodyShape.BorderColor = ComicStyle.Ink;
            bodyShape.HardShadow = true;
            bodyShape.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            bodyShape.ShadowColor = ComicStyle.Ink;
            bodyShape.Halftone = true;
            bodyShape.HalftoneAlpha = 0.14f;
            bodyShape.GlossAlpha = 0f;
            bodyShape.DrawGloss = false;
            bodyShape.InnerRimAlpha = 0f;
            bodyShape.color = themeResolved.panelColor;

            // 4) 球内液体
            RectTransform liquidRect = NewRect("Liquid", pulse);
            Stretch(liquidRect, 10f);
            var liquid = liquidRect.gameObject.AddComponent<UILiquidFillGraphic>();
            liquid.raycastTarget = false;
            liquid.Configure(UILiquidClipShape.Circle, 0f, 0f,
                new Color(1f, 1f, 1f, 0.6f),
                style.orbWaveAmplitude, style.orbWaveSpeed, style.orbWaveCycles,
                style.orbColumns, style.orbBubbleCount, 0.030f,
                style.orbFillSpring, 0f);

            // 4.5) 球心大招技能图标：深墨硬阴影 + 白色矢量主图标
            RectTransform iconRoot = NewRect("SpecialIcon", pulse);
            iconRoot.anchorMin = iconRoot.anchorMax = new Vector2(0.5f, 0.5f);
            iconRoot.pivot = new Vector2(0.5f, 0.5f);
            iconRoot.sizeDelta = Vector2.one * style.specialIconSize;
            iconRoot.anchoredPosition = new Vector2(0f, style.specialIconOffsetY);

            Image iconShadow = CreateIconImage("Shadow", iconRoot, ComicStyle.Ink,
                new Vector2(2.5f, -2.5f));
            Image icon = CreateIconImage("Glyph", iconRoot, Color.white, Vector2.zero);
            Sprite initIcon = UIIconLibrary.Get("special_slam") ?? style.GetSpecialIcon(SpecialKind.Slam);
            icon.sprite = initIcon;
            iconShadow.sprite = initIcon;
            icon.enabled = initIcon != null;
            iconShadow.enabled = initIcon != null;

            // 5) 漫画外环
            RectTransform rimRect = NewRect("Rim", pulse);
            Stretch(rimRect, 6f);
            UIInkShapeGraphic rimShape = rimRect.gameObject.AddComponent<UIInkShapeGraphic>();
            rimShape.raycastTarget = false;
            rimShape.Shape = UIInkShape.Blob;
            rimShape.BorderWidth = 2f;
            rimShape.BorderColor = ComicStyle.Ink;
            rimShape.GlossAlpha = 0f;
            rimShape.DrawGloss = false;
            rimShape.InnerRimAlpha = 0f;
            rimShape.color = new Color(1f, 1f, 1f, 0f);

            // 6) 充能百分比
            TMP_Text percent = CreateChunkyTmp("Percent", orbRect, themeResolved, style.percentFontSize, "0%");
            var percentRect = (RectTransform)percent.transform;
            percentRect.anchorMin = new Vector2(0.5f, 0f);
            percentRect.anchorMax = new Vector2(0.5f, 0f);
            percentRect.pivot = new Vector2(0.5f, 0f);
            percentRect.sizeDelta = new Vector2(84f, 26f);
            percentRect.anchoredPosition = new Vector2(0f, 7f);

            // 7) 满能按键提示（漫画纸面气泡，朝上指尾）
            RectTransform hintRect = NewRect("ReadyHint", orbRect);
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = style.readyHintSize;
            hintRect.anchoredPosition = new Vector2(0f, style.readyHintOffsetY);

            UIInkShapeGraphic hintPill = hintRect.gameObject.AddComponent<UIInkShapeGraphic>();
            hintPill.raycastTarget = false;
            hintPill.Shape = UIInkShape.SpeechBubble;
            hintPill.CornerRadius = ComicStyle.CornerBubble;
            hintPill.SpeechTail = true;
            hintPill.TailSize = new Vector2(16f, 14f);
            hintPill.TailAngle = 90f; // 尖角朝向上方大招球
            hintPill.BorderWidth = ComicStyle.BorderThin;
            hintPill.BorderColor = ComicStyle.Ink;
            hintPill.HardShadow = true;
            hintPill.ShadowOffset = new Vector2(3f, -3f);
            hintPill.ShadowColor = ComicStyle.Ink;
            hintPill.color = ComicStyle.Paper;

            Text hintLabel = CreateLegacyText("HintLabel", hintRect, Mathf.Max(16f, style.hintFontSize),
                style.readyHintText, TextAnchor.MiddleCenter, ComicStyle.Ink);
            Stretch((RectTransform)hintLabel.transform, 4f, 8f, 2f, 2f);

            var hintMotion = hintRect.gameObject.AddComponent<UISpringMotion>();
            hintMotion.SetTheme(theme);

            var view = orbGo.AddComponent<LiquidOrbView>();
            view.Configure(theme, style, liquid, bodyShape, rimShape, pulse, percent,
                halo, burst, hintRect, hintPill, hintLabel, hintMotion,
                style.readyHintText, style.readyHintKey);
            view.ConfigureSpecialIcon(iconRoot, icon, iconShadow);

            return view;
        }

        private static InkTankView BuildTank(RectTransform parent, UIThemeSO theme, HudGaugeStyleSO style)
        {
            UIThemeSO themeResolved = UIThemeSO.Resolve(theme);

            var tankGo = new GameObject("InkTank", typeof(RectTransform));
            var tankRect = (RectTransform)tankGo.transform;
            tankRect.SetParent(parent, false);
            tankRect.anchorMin = tankRect.anchorMax = new Vector2(1f, 1f);
            tankRect.pivot = new Vector2(0.5f, 0.5f);
            tankRect.sizeDelta = style.tankSize;
            tankRect.anchoredPosition = style.tankAnchoredPosition;

            float halfW = style.tankSize.x * 0.5f;
            float halfH = style.tankSize.y * 0.5f;
            float corner = Mathf.Min(18f, halfW - 2f);

            // 1) 低墨告警外发光（画在最底层，比罐体略大）
            RectTransform glowRect = NewRect("WarningGlow", tankRect);
            Stretch(glowRect, -7f);
            UIInkShapeGraphic warningGlow = glowRect.gameObject.AddComponent<UIInkShapeGraphic>();
            warningGlow.raycastTarget = false;
            ApplyRectStyle(warningGlow, corner + 5f, borderWidth: 0f,
                fill: new Color(themeResolved.dangerRed.r, themeResolved.dangerRed.g, themeResolved.dangerRed.b, 0f),
                border: themeResolved.dangerRed, glossAlpha: 0f, innerRimAlpha: 0f);

            // 2) 罐体底（实心纸面：投影只在这一层开，阴影被自己的填充盖住，只露外侧一圈）
            RectTransform bodyRect = NewRect("Body", tankRect);
            Stretch(bodyRect);
            UIInkShapeGraphic bodyShape = bodyRect.gameObject.AddComponent<UIInkShapeGraphic>();
            bodyShape.raycastTarget = false;
            ApplyRectStyle(bodyShape, corner, borderWidth: 0f, fill: themeResolved.panelColor,
                border: themeResolved.outlineColor, glossAlpha: 0f, innerRimAlpha: 0f, hardShadow: true);

            // 3) 液面
            const float liquidInset = 4f;
            RectTransform liquidRect = NewRect("Liquid", tankRect);
            Stretch(liquidRect, liquidInset);
            var liquid = liquidRect.gameObject.AddComponent<UILiquidFillGraphic>();
            liquid.raycastTarget = false;
            liquid.Configure(UILiquidClipShape.RoundedRect, Mathf.Max(2f, corner - liquidInset), 0f,
                new Color(1f, 1f, 1f, 0.55f),
                style.tankWaveAmplitude, style.tankWaveSpeed, 1.1f,
                style.tankColumns, style.tankBubbleCount, style.tankBubbleSize,
                style.tankFillSpring, 1f);

            // 4) 副武器耗墨刻度红线（细罐体上要够厚才看得清：深色衬底 + 3.4px 红线）
            RectTransform marker = NewRect("BombThreshold", tankRect);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(style.tankSize.x - 2f, 8f);

            CreateImage("ThresholdBacking", marker,
                new Color(themeResolved.outlineColor.r, themeResolved.outlineColor.g, themeResolved.outlineColor.b, 0.95f),
                new Vector2(style.tankSize.x - 2f, 7f));
            CreateImage("ThresholdLine", marker, themeResolved.dangerRed,
                new Vector2(style.tankSize.x - 1f, 3.4f));

            // 5) 外框（漫画粗墨边 6px，压住液体与刻度线）
            //    这里坚决不能开 HardShadow：外框填充是透明的，而硬阴影是整块实心多边形，
            //    会把罐内液面整片糊成墨黑（P13 实机截图里的"黑薯条"就是它）。
            RectTransform frameRect = NewRect("Frame", tankRect);
            Stretch(frameRect);
            UIInkShapeGraphic frameShape = frameRect.gameObject.AddComponent<UIInkShapeGraphic>();
            frameShape.raycastTarget = false;
            ApplyRectStyle(frameShape, corner, borderWidth: ComicStyle.Border, fill: new Color(1f, 1f, 1f, 0f),
                border: ComicStyle.Ink, glossAlpha: 0f, innerRimAlpha: 0f);

            // 6) 百分比读数（纸面标牌：纸白底 + 墨线 + 硬阴影）
            RectTransform plateRect = NewRect("PercentPlate", tankRect);
            plateRect.anchorMin = new Vector2(0.5f, 0f);
            plateRect.anchorMax = new Vector2(0.5f, 0f);
            plateRect.pivot = new Vector2(0.5f, 1f);
            plateRect.sizeDelta = new Vector2(74f, 26f);
            plateRect.anchoredPosition = new Vector2(0f, -4f);

            var plateShape = plateRect.gameObject.AddComponent<UIInkShapeGraphic>();
            plateShape.raycastTarget = false;
            plateShape.Shape = UIInkShape.RoundedRect;
            plateShape.CornerRadius = ComicStyle.CornerSharp;
            plateShape.BorderWidth = 2.5f;
            plateShape.BorderColor = ComicStyle.Ink;
            plateShape.HardShadow = true;
            plateShape.ShadowOffset = new Vector2(2f, -2f);
            plateShape.ShadowColor = ComicStyle.Ink;
            plateShape.color = ComicStyle.Paper;

            Text percent = CreateLegacyText("Percent", plateRect, 16f,
                "100%", TextAnchor.MiddleCenter, ComicStyle.Ink);
            percent.fontStyle = FontStyle.Bold;
            Stretch((RectTransform)percent.transform);

            var view = tankGo.AddComponent<InkTankView>();
            view.Configure(theme, style, liquid, marker, frameShape, bodyShape, warningGlow, percent,
                liquidInset);
            // 首帧 HudController 才会按 WeaponConfig 刷新；先用样式默认值落位，避免红线从 55% 跳一下
            view.SetThreshold01(style.bombThreshold01);

            return view;
        }

        private static void ApplyBlobStyle(UIInkShapeGraphic graphic, int seed, float borderWidth,
            Color fill, Color border, float wobble, float glossAlpha, float innerRimAlpha)
        {
            var card = new UIInkCardStyle
            {
                shape = UIInkShape.Blob,
                fillColor = fill,
                borderColor = border,
                borderWidth = borderWidth,
                wobble = wobble,
                glossAlpha = glossAlpha,
                glossSlope = 0.35f,
                glossCenter = 0.72f,
                glossThickness = 0.18f,
                innerRimAlpha = innerRimAlpha,
            };

            graphic.Seed = seed;
            graphic.ApplyStyle(card, fill);
        }

        private static void ApplyRectStyle(UIInkShapeGraphic graphic, float cornerRadius, float borderWidth,
            Color fill, Color border, float glossAlpha, float innerRimAlpha, bool hardShadow = false)
        {
            var card = new UIInkCardStyle
            {
                shape = UIInkShape.RoundedRect,
                fillColor = fill,
                borderColor = border,
                borderWidth = borderWidth,
                cornerRadius = cornerRadius,
                wobble = 0f,
                glossAlpha = glossAlpha,
                glossSlope = 0.3f,
                glossCenter = 0.74f,
                glossThickness = 0.14f,
                innerRimAlpha = innerRimAlpha,
                // UIInkCardStyle.comicPanel 默认 true（整个矩形画一块实心墨色阴影）。
                // 透明填充的描边层（外框、告警辉光）如果带这块阴影，内部会被整片盖成墨色，
                // 所以这里默认关掉，只有实心面板才显式 opt-in。
                comicPanel = false,
            };

            graphic.ApplyStyle(card, fill);

            if (!hardShadow) return;

            // 阴影画在本图元自己的填充之下，只有偏移出去的一圈露在面板外。
            graphic.HardShadow = true;
            graphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            graphic.ShadowColor = ComicStyle.Ink;
        }

        private static void ApplyPillStyle(UIInkShapeGraphic pill, UIThemeSO theme)
        {
            var card = new UIInkCardStyle
            {
                shape = UIInkShape.RoundedRect,
                fillColor = theme.teamAColor,
                borderColor = theme.outlineColor,
                borderWidth = 3f,
                cornerRadius = 16f,
                wobble = 0f,
                glossAlpha = 0.16f,
                glossSlope = 0.3f,
                glossCenter = 0.72f,
                glossThickness = 0.2f,
                innerRimAlpha = 0f,
            };

            pill.ApplyStyle(card, theme.teamAColor);
        }

        private static TMP_Text CreateChunkyTmp(string name, Transform parent, UIThemeSO theme,
            float fontSize, string text)
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
                fill: theme.textPrimary, outlinedText: true);

            return tmp;
        }

        private static Text CreateLegacyText(string name, Transform parent, float fontSize, string text,
            TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            var label = go.AddComponent<Text>();
            label.font = LegacyFont();
            label.fontSize = Mathf.RoundToInt(fontSize);
            label.text = text;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            return label;
        }

        private static Font LegacyFont()
        {
            if (_legacyFont == null)
            {
                _legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return _legacyFont;
        }

        private static Image CreateIconImage(string name, Transform parent, Color color, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            rect.anchoredPosition = offset;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static Image CreateImage(string name, Transform parent, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
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
