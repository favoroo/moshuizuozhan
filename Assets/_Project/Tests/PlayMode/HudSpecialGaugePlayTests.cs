using System.Collections;
using NUnit.Framework;
using P1.UI;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// P4-C 运行时验收：HUD 自举装配、大招球满能爆发（光环 + 爆发计数 + 按键提示）、
    /// 墨水管低墨告警与迟滞解除，全部在真实 MonoBehaviour 生命周期下运行。
    /// </summary>
    [Category("UI")]
    public class HudSpecialGaugePlayTests
    {
        private static GameObject CreateCanvas()
        {
            GameObject root = new GameObject("P4C_TestCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return root;
        }

        [UnityTest]
        public IEnumerator HudController_AutoBuildsOrbAndTank()
        {
            GameObject root = CreateCanvas();
            var hud = root.AddComponent<HudController>();

            yield return null;
            yield return null;

            Assert.IsNotNull(hud.LiquidOrb, "HudController 应自动装配大招条");
            Assert.IsNotNull(hud.InkTank, "HudController 应自动装配墨水条");
            Assert.AreEqual(1, root.GetComponentsInChildren<LiquidOrbView>(true).Length,
                "只应生成一个大招条，不允许重复装配");
            Assert.AreEqual(1, root.GetComponentsInChildren<InkTankView>(true).Length,
                "只应生成一个墨水条，不允许重复装配");

            AssertFilledBar(hud.LiquidOrb.transform, "大招条内部应有横向填充块");
            AssertFilledBar(hud.InkTank.transform, "墨水条内部应有横向填充块");

            Object.Destroy(root);
            yield return null;
        }

        /// <summary>横向仪表用 Image.Filled（水平方向）做纯色填充，取代旧的波浪液体网格。</summary>
        private static void AssertFilledBar(Transform root, string message)
        {
            Image[] images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image.type == Image.Type.Filled && image.fillMethod == Image.FillMethod.Horizontal)
                {
                    return;
                }
            }

            Assert.Fail(message);
        }

        [UnityTest]
        public IEnumerator LiquidOrb_TurnsYellowAndShowsKeyHintWhenReady()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            LiquidOrbView orb = cluster.Orb;
            Assert.IsNotNull(orb, "工厂应返回大招条视图");

            orb.SetCharge(0.35f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.AreEqual(0, orb.ReadyBurstCount, "未满能不应触发就绪");
            Assert.AreEqual(0.35f, orb.BarFill.fillAmount, 0.01f, "充能条应跟随充能比例");
            // t-1 三卡：徽章墨底常驻，身份色由 4px 黄边表达（旧版是纸白/黄整块翻色）
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.color, "未满能徽章应为墨底");
            Assert.AreEqual(ComicStyle.Yellow, orb.IconPlate.BorderColor, "未满能徽章边框应为行动黄");
            var cardBg = orb.transform.Find("CardBg") != null
                ? orb.transform.Find("CardBg").GetComponent<UIInkShapeGraphic>()
                : null;
            Assert.IsNotNull(cardBg, "大招卡应有纸白卡底");
            Assert.AreEqual(ComicStyle.Paper, cardBg.color, "未满能时卡片应为纸白");

            orb.SetCharge(1f, true);
            yield return null;
            Assert.IsTrue(orb.IsReady, "满能后 IsReady 应为 true");
            Assert.AreEqual(1, orb.ReadyBurstCount, "进入满能应计数一次");
            Assert.AreEqual(1f, orb.BarFill.fillAmount, 0.01f, "满能条应铺满");
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.color, "满能时徽章仍是墨底（翻黄由整卡承担）");
            Assert.AreEqual(ComicStyle.Yellow, cardBg.color, "满能应整卡翻黄");

            Transform readyHint = orb.transform.Find("ReadyHint");
            Assert.IsNotNull(readyHint, "大招条应包含满能按键提示节点");
            Assert.IsTrue(readyHint.gameObject.activeSelf, "桌面端满能应显示「Q 释放」提示");
            Assert.IsFalse(orb.transform.Find("Percent").gameObject.activeSelf, "满能时百分比让位给按键提示");

            // 掉回未满能：提示收起、百分比回来、整卡恢复纸白
            orb.SetCharge(0f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.IsFalse(readyHint.gameObject.activeSelf, "释放后提示应隐藏");
            Assert.IsTrue(orb.transform.Find("Percent").gameObject.activeSelf, "释放后百分比应回来");
            Assert.AreEqual(ComicStyle.Paper, cardBg.color, "释放后卡片恢复纸白");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiquidOrb_CenterIconSwitchesWithSpecialKind()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            LiquidOrbView orb = cluster.Orb;

            orb.SetSpecialKind(SpecialKind.Slam);
            yield return null;
            Sprite slam = orb.SpecialIconSprite;
            Assert.IsNotNull(slam, "球心应显示 Tidal Slam 矢量图标");
            Assert.AreEqual(SpecialKind.Slam, orb.DisplayedSpecialKind);

            orb.SetSpecialKind(SpecialKind.Storm);
            yield return null;
            Sprite storm = orb.SpecialIconSprite;
            Assert.IsNotNull(storm, "球心应显示 Ink Tempest 矢量图标");
            Assert.AreEqual(SpecialKind.Storm, orb.DisplayedSpecialKind);
            Assert.AreNotSame(slam, storm, "Slam / Storm 应使用不同图标");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InkTank_WarnsBelowThresholdAndClearsWithHysteresis()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            InkTankView tank = cluster.Tank;
            Assert.IsNotNull(tank, "工厂应返回墨水管视图");

            tank.SetInk(80f, 100f);
            yield return null;
            Assert.IsFalse(tank.IsLowInk, "高墨量不应告警");
            Assert.AreEqual(0, tank.WarningPlayCount);

            tank.SetInk(12f, 100f);
            yield return null;
            Assert.IsTrue(tank.IsLowInk, "低于 20% 应进入低墨告警");
            Assert.GreaterOrEqual(tank.WarningPlayCount, 1, "进入低墨应触发预警音");

            // 迟滞带内（20%~23%）仍应保持告警
            tank.SetInk(21f, 100f);
            yield return null;
            Assert.IsTrue(tank.IsLowInk, "迟滞带内不应抖动解除");

            tank.SetInk(40f, 100f);
            yield return null;
            Assert.IsFalse(tank.IsLowInk, "回到安全墨量应解除告警");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InkTank_ThresholdMarkerFollowsBombCost()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            InkTankView tank = cluster.Tank;

            RectTransform marker = null;
            foreach (RectTransform child in tank.GetComponentsInChildren<RectTransform>(true))
            {
                if (child.name == "BombThreshold") marker = child;
            }

            Assert.IsNotNull(marker, "墨水条应有副武器刻度线节点");

            tank.SetThreshold01(0.55f);
            yield return null;
            float at55 = marker.anchoredPosition.x;

            tank.SetThreshold01(0.7f);
            yield return null;
            float at70 = marker.anchoredPosition.x;

            Assert.Greater(at70, at55, "横向条的竖刻度线应随阈值右移");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BottomStatus_ThreeIndependentCards_GeometryAndStyle()
        {
            GameObject root = CreateCanvas();
            // 真实场景里有烘焙的 BottomStatusPanel（旧黑块节点）：三卡改造后它只当不可见容器，
            // 提示条与迁移后的血条都挂在它下面，所以测试也要先放一个同名节点。
            var panelGo = new GameObject("BottomStatusPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(root.transform, false);
            var hud = root.AddComponent<HudController>();
            yield return null;
            yield return null;

            RectTransform cards = hud.GaugeRoot;
            Assert.IsNotNull(cards, "底部状态区应有三卡根节点");

            var ink = cards.Find("InkCard") as RectTransform;
            var spec = cards.Find("SpecialCard") as RectTransform;
            var health = cards.Find("HealthCard") as RectTransform;
            Assert.IsNotNull(ink, "应有独立的墨水卡");
            Assert.IsNotNull(spec, "应有独立的大招卡");
            Assert.IsNotNull(health, "应有独立的生命卡");

            // 桌面几何：88 高、底边 56、卡间 16、三卡不重叠
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightDesktop, cards.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.CardsBottomYDesktop, cards.anchoredPosition.y, 0.01f);
            Assert.AreEqual(0f, ink.anchoredPosition.y, 0.01f, "卡片底边应与卡区底边对齐");
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightDesktop, ink.rect.height, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightDesktop, spec.rect.height, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightDesktop, health.rect.height, 0.01f);

            Assert.AreEqual(ink.anchoredPosition.x + ink.rect.width + HudSpecialGaugeFactory.CardGapDesktop,
                spec.anchoredPosition.x, 0.01f, "墨水卡与大招卡之间应留 16px");
            Assert.AreEqual(spec.anchoredPosition.x + spec.rect.width + HudSpecialGaugeFactory.CardGapDesktop,
                health.anchoredPosition.x, 0.01f, "大招卡与生命卡之间应留 16px");

            // 三卡整体居中：根节点锚定底边中央，卡片从根矩形左边起算、两端余量相等
            Assert.AreEqual(0.5f, cards.anchorMin.x, 1e-4f, "三卡区根节点应锚定屏幕水平中心");
            Assert.AreEqual(0.5f, cards.pivot.x, 1e-4f);
            float sideInset = (HudSpecialGaugeFactory.CardAreaWidth -
                (ink.rect.width + spec.rect.width + health.rect.width
                 + HudSpecialGaugeFactory.CardGapDesktop * 2f)) * 0.5f;
            Assert.AreEqual(sideInset, ink.anchoredPosition.x, 0.01f, "第一张卡左边距应等于右边余量");
            Assert.AreEqual(cards.rect.width, health.anchoredPosition.x + health.rect.width + sideInset,
                0.01f, "最后一张卡右边距应等于左边余量");

            // 不伸进左右触控区（1920 宽屏左右各留 200px）
            Assert.LessOrEqual(cards.rect.width * 0.5f + 200f, 960f,
                "三卡区不得伸进左右下角的触控按键区");

            // 卡片外壳：纸白 + 3px 墨框 + 面板档硬影 (4,-5)
            foreach (var card in new[] { ink, spec, health })
            {
                var bg = card.Find("CardBg").GetComponent<UIInkShapeGraphic>();
                Assert.IsNotNull(bg, card.name + " 应有纸白卡底");
                Assert.AreEqual(ComicStyle.Paper, bg.color);
                Assert.AreEqual(ComicStyle.BorderThin, bg.BorderWidth, 0.01f);
                Assert.AreEqual(ComicStyle.Ink, bg.BorderColor);
                Assert.IsTrue(bg.HasHardShadow, card.name + " 必须开面板硬影");
                Assert.AreEqual(UIThemeSO.Resolve(null).GetHardShadowOffset(UIHardShadowLevel.Panel),
                    bg.ShadowOffset, "硬影必须是面板档 (4,-5)");

                // 顶部 5px 身份色带：内缩 3px 墨框、不叠硬影
                var rail = card.Find("Rail").GetComponent<UIInkShapeGraphic>();
                Assert.IsNotNull(rail, card.name + " 应有身份色带");
                var railRect = (RectTransform)rail.transform;
                Assert.AreEqual(5f, railRect.rect.height, 0.01f, "身份色带高 5px");
                Assert.AreEqual(card.rect.width - ComicStyle.BorderThin * 2f, railRect.rect.width, 0.01f,
                    "色带应内缩边框宽度");
                Assert.IsFalse(rail.HasHardShadow, "色带不叠硬影");
            }

            // 墨水卡：44 墨底徽章 + 4px 队伍色边 + 24 高轨道 + display 36 墨色读数
            var inkBadge = ink.Find("InkBadge").GetComponent<UIInkShapeGraphic>();
            Assert.AreEqual(44f, ((RectTransform)inkBadge.transform).rect.width, 0.01f);
            Assert.AreEqual(4f, inkBadge.BorderWidth, 0.01f);
            Assert.AreEqual(ComicStyle.Ink, inkBadge.color);
            var inkTrack = (RectTransform)ink.Find("InkBar");
            Assert.AreEqual(24f, inkTrack.rect.height, 0.01f);
            var inkPct = ink.Find("Percent").GetComponent<TMPro.TMP_Text>();
            // 桌面 36 / 移动端 clamp 到 38（UITypography 通路），三卡读数同档
            Assert.AreEqual(HudSpecialGaugeFactory.ReadoutFontSize(), inkPct.fontSize, 0.01f);
            Assert.GreaterOrEqual(inkPct.fontSize, HudSpecialGaugeFactory.CardReadoutFontSize,
                "大百分比不得小于桌面基准 36");
            Assert.AreEqual(ComicStyle.Ink, inkPct.color, "纸白卡上的读数是墨色");

            // 大招卡：同构墨底徽章 + 行动黄读数 / 轨道
            var specBadge = spec.Find("IconPlate").GetComponent<UIInkShapeGraphic>();
            Assert.AreEqual(ComicStyle.Ink, specBadge.color);
            Assert.AreEqual(ComicStyle.Yellow, specBadge.BorderColor);
            Assert.AreEqual(ComicStyle.Yellow, spec.Find("SpecialBar/Fill").GetComponent<Image>().color);

            // 生命卡：220 高轨道、绿色填充、右侧 display 36
            var hpTrack = (RectTransform)health.Find("HealthBarBg");
            Assert.AreEqual(HudSpecialGaugeFactory.HealthTrackWidth, hpTrack.rect.width, 0.01f);
            Assert.AreEqual(24f, hpTrack.rect.height, 0.01f);
            var hpFill = hpTrack.Find("HealthBarFill").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Filled, hpFill.type, "生命条必须是横向 Filled 纯色块");
            Assert.AreEqual(ComicStyle.Green, hpFill.color);
            Assert.AreEqual(HudSpecialGaugeFactory.ReadoutFontSize(),
                health.Find("HpText").GetComponent<TMPro.TMP_Text>().fontSize, 0.01f);

            // 生命卡单层模式断言：HpText 与 HpBadge 都不应有活跃的 ChunkyFill 重影层
            Transform hpTextTr = health.Find("HpText");
            Transform hpFillTr = hpTextTr != null ? hpTextTr.Find("ChunkyFill") : null;
            Assert.IsTrue(hpFillTr == null || !hpFillTr.gameObject.activeSelf, "HpText 单层模式下不允许存在活跃的 ChunkyFill 重影层");

            Transform hpBadgeTr = health.Find("HpBadge/Label");
            Transform badgeFillTr = hpBadgeTr != null ? hpBadgeTr.Find("ChunkyFill") : null;
            Assert.IsTrue(badgeFillTr == null || !badgeFillTr.gameObject.activeSelf, "HpBadge 单层模式下不允许存在活跃的 ChunkyFill 重影层");

            // 键鼠提示已迁到暂停菜单：HUD 底部不再出现提示条/小字
            Transform panel = hud.transform.Find("HudSafeArea/BottomStatusPanel");
            if (panel == null) panel = hud.transform.Find("BottomStatusPanel");
            Assert.IsNotNull(panel, "旧面板节点必须保留（承载不可见节点）");
            Assert.IsNull(panel.Find("KeyHintStrip"), "HUD 底部不再生成键鼠提示条");
            Transform hint = panel.Find("ControlsHintText");
            Assert.IsTrue(hint == null || !hint.gameObject.activeSelf, "HUD 底部不再显示键鼠提示文字");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator KillCard_LongNamesUseBodyFont_AndDoNotSpamEllipsisWarning()
        {
            GameObject root = CreateCanvas();
            var cardGo = new GameObject("KillCard", typeof(RectTransform), typeof(KillCardView));
            cardGo.transform.SetParent(root.transform, false);
            var card = cardGo.GetComponent<KillCardView>();

            int ellipsisWarnings = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if ((type == LogType.Warning || type == LogType.Error) && condition.Contains("Ellipsis"))
                {
                    ellipsisWarnings++;
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                var ev = new KillFeedEvent
                {
                    KillerName = "OCTOLING_SUPER_LONG_NAME_1234567890",
                    KillerTeam = P1.Core.Team.TeamB,
                    VictimName = "非常非常长的中文受害者名字用来验证截断",
                    VictimTeam = P1.Core.Team.TeamA,
                    WeaponKind = WeaponKind.Shooter,
                };
                card.Bind(ev, null, null, null);
                UnityEngine.Canvas.ForceUpdateCanvases();
                yield return null;
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // 名字必须走正文字重 + Truncate：长名字截断不依赖省略号字形，
            // 否则 TMP 每次重建都会刷一条「Ellipsis not available」警告（旧 bug）
            var killer = card.transform.Find("KillerText").GetComponent<TMPro.TMP_Text>();
            var victim = card.transform.Find("VictimText").GetComponent<TMPro.TMP_Text>();
            Assert.IsNotNull(killer);
            Assert.AreEqual(TMPro.TextOverflowModes.Truncate, killer.overflowMode);
            Assert.AreEqual(TMPro.TextOverflowModes.Truncate, victim.overflowMode);
            Assert.IsNotNull(killer.font);
            StringAssert.Contains("MiSans-Semibold", killer.font.name, "击杀者名字应用正文字重（MiSans-Semibold）");
            StringAssert.Contains("MiSans-Semibold", victim.font.name, "受害者名字应用正文字重（MiSans-Semibold）");
            Assert.AreEqual(0, ellipsisWarnings, "长名字截断不得再刷 TMP Ellipsis 警告");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MobileLayout_KeepsGaugesCenteredAboveHealthBar_AndHidesKeyboardHint()
        {
            GameObject root = CreateCanvas();
            var hud = root.AddComponent<HudController>();

            yield return null;
            yield return null;

            Assert.IsNotNull(hud.LiquidOrb, "大招条应自动装配");
            Assert.IsNotNull(hud.InkTank, "墨水条应自动装配");
            Assert.IsNotNull(hud.GaugeRoot, "仪表条应有独立根节点，才能与底部面板同步移动");

            hud.ApplyMobileLayout();
            yield return null;

            RectTransform gaugeRoot = hud.GaugeRoot;
            Assert.AreEqual(0.5f, gaugeRoot.anchorMin.x, 1e-4f, "移动端仪表条应锚定屏幕水平中心");
            Assert.AreEqual(0f, gaugeRoot.anchorMin.y, 1e-4f, "移动端仪表条应锚定屏幕底部");
            Assert.AreEqual(HudSpecialGaugeFactory.StripWidth, gaugeRoot.sizeDelta.x, 0.01f,
                "三卡区根节点应与旧信息条矩形同宽（300+300+380+16×2）");
            Assert.AreEqual(HudSpecialGaugeFactory.StripHeightMobile, gaugeRoot.sizeDelta.y, 0.01f,
                "移动端卡片应收矮（88 → 72）");
            Assert.AreEqual(HudSpecialGaugeFactory.StripBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);
            Assert.Less(gaugeRoot.sizeDelta.x, 1120f,
                "三卡区必须留在屏幕中央，不能伸进左右触控按键区");

            // t-1 三卡：墨水条 / 大招条各自挂在独立卡片上（旧版是同一行 MeterRow）
            Assert.AreEqual(gaugeRoot.Find("InkCard"), hud.InkTank.transform,
                "墨水条应挂在独立的墨水卡上");
            Assert.AreEqual(gaugeRoot.Find("SpecialCard"), hud.LiquidOrb.transform,
                "大招条应挂在独立的大招卡上");
            Assert.IsNotNull(gaugeRoot.Find("HealthCard"), "底部状态区应有独立的生命卡");

            // 移动端几何：卡高 72、间距 12、底边 46（横向排布与桌面一致）
            var mobileInk = gaugeRoot.Find("InkCard") as RectTransform;
            var mobileSpec = gaugeRoot.Find("SpecialCard") as RectTransform;
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightMobile, mobileInk.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.CardHeightMobile, mobileSpec.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.CardGapMobile,
                mobileSpec.anchoredPosition.x - (mobileInk.anchoredPosition.x + mobileInk.sizeDelta.x),
                0.01f, "移动端卡间距应为 12");
            Assert.AreEqual(HudSpecialGaugeFactory.CardsBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);

            // 满能时移动端不应弹出「按 Q 释放」，大招键本身已有就绪提示
            hud.LiquidOrb.SetCharge(1f, true);
            yield return null;
            Transform readyHint = hud.LiquidOrb.transform.Find("ReadyHint");
            Assert.IsNotNull(readyHint, "大招条应包含满能提示节点");
            Assert.IsFalse(readyHint.gameObject.activeSelf, "移动端必须隐藏键盘 Q 提示");

            // 重复应用布局必须幂等（分辨率/安全区变化会重复调用）
            hud.ApplyMobileLayout();
            Assert.AreEqual(HudSpecialGaugeFactory.StripHeightMobile, gaugeRoot.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.StripBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);

            Object.Destroy(root);
            yield return null;
        }
    }
}
