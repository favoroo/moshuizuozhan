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
    /// P4-C 运行时验收：HUD 自举装配、大招横条满能（填充铺满 + 徽章反色 + 一次性爆闪）、
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
            Assert.AreEqual(1, root.GetComponentsInChildren<LiquidOrbView>(true).Length,
                "只应生成一个大招条，不允许重复装配");
            AssertBarTrack(hud.LiquidOrb.transform, "大招条内部应有墨黑胶囊轨道 + 胶囊填充");

            // P42-E：底部墨水卡移除，墨水量改由准星旁竖罐承担
            Assert.IsNull(hud.InkTank, "底部墨水卡应已移除（墨水量看准星旁墨水罐）");
            Assert.IsNotNull(hud.InkTankCrosshair, "HudController 应自动装配准星旁墨水罐");
            Assert.AreEqual(1, root.GetComponentsInChildren<InkTankCrosshairView>(true).Length,
                "只应生成一个准星墨水罐，不允许重复装配");

            Object.Destroy(root);
            yield return null;
        }

        /// <summary>横条仪表 = 墨黑胶囊轨道（UIInkShapeGraphic）+ 胶囊填充（UIPillImage）。</summary>
        private static void AssertBarTrack(Transform root, string message)
        {
            var track = root.Find(HudSpecialGaugeFactory.BarTrackName)?.GetComponent<UIInkShapeGraphic>();
            var fill = root.Find(HudSpecialGaugeFactory.BarTrackName + "/" + HudSpecialGaugeFactory.BarFillName)
                ?.GetComponent<UIPillImage>();
            Assert.IsNotNull(track, message);
            Assert.AreEqual(UIInkShape.Pill, track.Shape, "轨道必须是胶囊（圆角语言的进度条）");
            Assert.IsNotNull(fill, "横条必须有胶囊填充");
            Assert.AreEqual(Image.Type.Filled, fill.type, "填充必须是 Filled 才能驱动 fillAmount");
        }

        [UnityTest]
        public IEnumerator LiquidOrb_FillsBarAndInvertsBadgeWhenReady()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            LiquidOrbView orb = cluster.Orb;
            Assert.IsNotNull(orb, "工厂应返回大招环视图");

            orb.SetCharge(0.35f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.AreEqual(0, orb.ReadyBurstCount, "未满能不应触发就绪");
            Assert.IsNotNull(orb.BarFill, "大招条应装配胶囊填充");
            Assert.AreEqual(0.35f, orb.BarFill.fillAmount, 0.01f, "胶囊填充应跟随充能比例");

            // 横条化（三轮）：充能中 = 墨底徽章 + 黄图标；右端不挂任何小件（Q 键帽已下线）
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.color, "充能中徽章底色应为墨黑");
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.BorderColor, "徽章描边恒为墨色");
            var glyph = orb.transform.Find("BarBadge/SpecialIcon/Glyph").GetComponent<Image>();
            Assert.IsNotNull(glyph, "大招条左端徽章应有技能图标");
            Assert.AreEqual(ComicStyle.Yellow, glyph.color, "充能中图标应为行动黄");
            Assert.IsNull(orb.transform.Find("Percent"), "横条化后不再显示百分比读数");
            Assert.IsNull(orb.transform.Find("CardBg"), "横条化后不再有纸白卡片");
            Assert.IsNull(orb.transform.Find("ReadyHint"), "条内不再有「Q」键帽（三轮已删）");
            Assert.IsNull(orb.transform.Find("HpChip"), "大招条不应有读数芯片");

            // 第四轮：满能状态环 = 徽章外圈的 4px 黄环（填充透明），充能中隐藏
            var readyRing = orb.transform.Find(HudSpecialGaugeFactory.ReadyRingName) as RectTransform;
            Assert.IsNotNull(readyRing, "大招条应有满能状态环");
            Assert.IsFalse(readyRing.gameObject.activeSelf, "充能中状态环必须隐藏");
            var readyRingShape = readyRing.GetComponent<UIInkShapeGraphic>();
            Assert.AreEqual(ComicStyle.Yellow, readyRingShape.BorderColor, "状态环环带应为行动黄");
            Assert.AreEqual(Color.clear, readyRingShape.color, "状态环填充必须透明（只画环带，不挡徽章）");

            orb.SetCharge(1f, true);
            // 满能只换徽章状态：条身颜色恒定，不闪、不爆闪（用户明确要求）
            Assert.AreEqual(ComicStyle.Yellow, orb.BarFill.color, "满能时条身保持常驻行动黄，不做闪烁");
            yield return null;
            Assert.IsTrue(orb.IsReady, "满能后 IsReady 应为 true");
            Assert.AreEqual(1, orb.ReadyBurstCount, "进入满能应计数一次");
            Assert.AreEqual(1f, orb.BarFill.fillAmount, 0.01f, "满能时填充应铺满整条");
            // 满能 = 徽章整体反色（黄底墨图标），条内唯一的状态承载点
            Assert.AreEqual(ComicStyle.Yellow, orb.IconPlate.color, "满能时徽章翻黄底");
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.BorderColor, "徽章 3px 墨边常驻");
            Assert.AreEqual(ComicStyle.Ink, glyph.color, "满能时图标翻墨（黄底墨图标才读得出）");

            Assert.IsNull(orb.transform.Find("ReadyBurst"), "满能不再生成爆闪节点（改由徽章换状态）");
            Assert.IsTrue(readyRing.gameObject.activeSelf, "满能时状态环应亮起");

            // 掉回未满能：徽章恢复墨底黄图标
            orb.SetCharge(0f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.IsFalse(readyRing.gameObject.activeSelf, "释放后状态环应收起");
            Assert.AreEqual(ComicStyle.Ink, orb.IconPlate.color, "释放后徽章恢复墨底");
            Assert.AreEqual(ComicStyle.Yellow, glyph.color, "释放后图标恢复行动黄");
            Assert.AreEqual(ComicStyle.Yellow, orb.BarFill.color, "释放后条身回到常驻行动黄（闪烁停止）");

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
            var tankGo = new GameObject("InkTankCrosshair", typeof(RectTransform));
            tankGo.transform.SetParent(root.transform, false);
            var tank = tankGo.AddComponent<InkTankCrosshairView>();
            tank.EnsureBuilt();
            yield return null;

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
            var tankGo = new GameObject("InkTankCrosshair", typeof(RectTransform));
            tankGo.transform.SetParent(root.transform, false);
            var tank = tankGo.AddComponent<InkTankCrosshairView>();
            tank.EnsureBuilt();
            yield return null;

            // 罐体挂在视图根下：直接按名字全量搜，避免依赖层级拼写
            RectTransform marker = null;
            foreach (Transform child in tank.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Threshold") marker = (RectTransform)child;
            }

            Assert.IsNotNull(marker, "准星墨水罐应有副武器刻度线节点");

            tank.SetThreshold01(0.55f);
            yield return null;
            float at55 = marker.anchoredPosition.y;

            tank.SetThreshold01(0.7f);
            yield return null;
            float at70 = marker.anchoredPosition.y;

            Assert.Greater(at70, at55, "竖罐的横向刻度线应随阈值上移");

            tank.SetThreshold01(0f);
            yield return null;
            var line = marker.GetComponent<Image>();
            Assert.AreEqual(Color.clear, line.color, "手雷不耗墨时刻度线应隐藏（透明）");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BottomStatus_TwoBars_GeometryAndStyle()
        {
            GameObject root = CreateCanvas();
            // 真实场景里有烘焙的 BottomStatusPanel（旧黑块节点）：横条化后它只当不可见容器，
            // 迁移后的血条节点挂在它下面，所以测试也要先放一个同名节点。
            var panelGo = new GameObject("BottomStatusPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(root.transform, false);
            var hud = root.AddComponent<HudController>();
            yield return null;
            yield return null;

            RectTransform cards = hud.GaugeRoot;
            Assert.IsNotNull(cards, "底部状态区应有横条组根节点");

            // P42-E：墨水卡移除；横条化后底部只留大招条 + 生命条
            var ink = cards.Find("InkCard") as RectTransform;
            var spec = cards.Find("SpecialCard") as RectTransform;
            var health = cards.Find("HealthCard") as RectTransform;
            Assert.IsNull(ink, "墨水卡应已移除（墨水量在准星旁墨水罐上）");
            Assert.IsNotNull(spec, "应有独立的大招条");
            Assert.IsNotNull(health, "应有独立的生命条");

            // 桌面几何：整组 344×78、底边 54；两行各 344×34、行距 10
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupHeightDesktop, cards.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupWidthDesktop, cards.sizeDelta.x, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupBottomYDesktop, cards.anchoredPosition.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupWidthDesktop, spec.rect.width, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarRowHeightDesktop, spec.rect.height, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupWidthDesktop, health.rect.width, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarRowHeightDesktop, health.rect.height, 0.01f);

            // 大招在上、生命在下，中间行距 8
            Assert.AreEqual(HudSpecialGaugeFactory.BarRowHeightDesktop + HudSpecialGaugeFactory.BarRowGapDesktop,
                spec.anchoredPosition.y, 0.01f, "大招条应在上一行");
            Assert.AreEqual(0f, health.anchoredPosition.y, 0.01f, "生命条应在下一行（贴组底）");

            // 整组居中：根节点锚定底边中央
            Assert.AreEqual(0.5f, cards.anchorMin.x, 1e-4f, "横条组应锚定屏幕水平中心");
            Assert.AreEqual(0.5f, cards.pivot.x, 1e-4f);

            // 窄横条：整组远小于旧 712px 卡片区，且不伸进左右触控区
            Assert.Less(cards.rect.width, 400f, "横条组应保持窄条形态");
            Assert.LessOrEqual(cards.rect.width * 0.5f + 200f, 960f,
                "横条组不得伸进左右下角的触控按键区");

            // 大招条：左端墨底徽章 + 墨黑胶囊轨道 + 胶囊填充（右端无任何附加小件）
            Assert.IsNull(spec.Find("CardBg"), "横条化后不再有纸白卡片");
            Assert.IsNull(spec.Find("Rail"), "横条化后不再有身份色带");
            Assert.IsNull(spec.Find("Disc"), "横条化后不再有墨底正圆");
            Assert.IsNull(spec.Find("Arc"), "横条化后不再有环形弧");

            var specBadge = spec.Find(HudSpecialGaugeFactory.BarBadgeName)?.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(specBadge, "大招条左端应有身份徽章");
            Assert.AreEqual(UIInkShape.Pill, specBadge.Shape, "身份徽章应为正圆");
            Assert.AreEqual(ComicStyle.Ink, specBadge.color, "未满能徽章应为墨底");
            Assert.IsNotNull(spec.Find(HudSpecialGaugeFactory.BarBadgeName + "/SpecialIcon/Glyph"),
                "大招徽章应有技能图标");

            var specTrack = spec.Find(HudSpecialGaugeFactory.BarTrackName)?.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(specTrack, "大招条应有胶囊轨道");
            Assert.AreEqual(UIInkShape.Pill, specTrack.Shape);
            Assert.AreEqual(ComicStyle.Ink, specTrack.color, "轨道应为墨黑（填充的对比底 + 自动描边）");

            var specFill = spec.Find(HudSpecialGaugeFactory.BarTrackName + "/" + HudSpecialGaugeFactory.BarFillName)
                ?.GetComponent<UIPillImage>();
            Assert.IsNotNull(specFill, "大招条应有胶囊填充");
            Assert.AreEqual(ComicStyle.Yellow, specFill.color, "大招填充应为行动黄");
            Assert.AreEqual(Image.Type.Filled, specFill.type);
            Assert.IsNull(spec.Find(HudSpecialGaugeFactory.ReadyHintName), "三轮起条内不再有「Q」键帽");
            var specTrackRect = (RectTransform)specTrack.transform;
            Assert.AreEqual(HudSpecialGaugeFactory.BarTrackWidthDesktop, specTrackRect.rect.width, 0.01f,
                "轨道应拉满到行右沿（300）");
            Assert.AreEqual(spec.rect.width, specTrackRect.anchoredPosition.x + specTrackRect.rect.width, 0.01f,
                "轨道右沿必须与行右沿重合，条内不留右槽");

            // 生命条：左端绿底十字徽章 + 胶囊轨道 + 绿色填充（右端无读数芯片）
            Assert.IsNull(health.Find("CardBg"), "横条化后不再有纸白卡片");
            Assert.IsNull(health.Find("HpDisc"), "横条化后不再有墨底正圆");
            Assert.IsNull(health.Find("HealthBarArc"), "横条化后不再有环形弧");

            var hpBadge = health.Find(HudSpecialGaugeFactory.BarBadgeName)?.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(hpBadge, "生命条左端应有身份徽章");
            Assert.AreEqual(UIInkShape.Pill, hpBadge.Shape, "身份徽章应为正圆");
            Assert.AreEqual(ComicStyle.Green, hpBadge.color, "常态徽章应为语义绿（生命身份色）");

            var hpCrossIcon = health
                .Find(HudSpecialGaugeFactory.BarBadgeName + "/" + HudSpecialGaugeFactory.HealthBadgeIconName)
                ?.GetComponent<Image>();
            Assert.IsNotNull(hpCrossIcon, "十字徽章应有 glyph_plus 图标");
            Assert.IsTrue(hpCrossIcon.enabled && hpCrossIcon.sprite != null,
                "十字图标必须加载成功（取不到要隐藏，不允许白块）");

            var hpTrack = health.Find(HudSpecialGaugeFactory.BarTrackName)?.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(hpTrack, "生命条应有胶囊轨道");
            Assert.AreEqual(UIInkShape.Pill, hpTrack.Shape);
            Assert.AreEqual(ComicStyle.Ink, hpTrack.color);

            var hpFill = health.Find(HudSpecialGaugeFactory.BarTrackName + "/" + HudSpecialGaugeFactory.BarFillName)
                ?.GetComponent<UIPillImage>();
            Assert.IsNotNull(hpFill, "生命条应有胶囊填充");
            Assert.AreEqual(ComicStyle.Green, hpFill.color, "生命填充应为语义绿");
            Assert.AreEqual(1f, hpFill.fillAmount, 0.01f, "满血时生命填充应铺满整条");

            // 旧横条 Image 只当数据载体：节点保留但禁绘制
            var legacyFill = health.Find("HealthBarBg/HealthBarFill")?.GetComponent<Image>();
            Assert.IsNotNull(legacyFill, "旧 HealthBarFill 绑定节点必须保留");
            Assert.IsFalse(legacyFill.enabled, "旧横条填充不得再绘制");

            // 右端读数芯片与数字全部下线：条内只有徽章 + 轨道 + 填充
            Assert.IsNull(health.Find(HudSpecialGaugeFactory.HealthChipName),
                "三轮起生命条不再有读数芯片");
            var hpTrackRect = (RectTransform)hpTrack.transform;
            Assert.AreEqual(HudSpecialGaugeFactory.BarTrackWidthDesktop, hpTrackRect.rect.width, 0.01f,
                "生命轨道也应拉满到行右沿（300）");
            Assert.AreEqual(health.rect.width, hpTrackRect.anchoredPosition.x + hpTrackRect.rect.width, 0.01f,
                "生命轨道右沿必须与行右沿重合");

            Transform hpBadgeLegacy = health.Find("HpBadge");
            Assert.IsTrue(hpBadgeLegacy == null || !hpBadgeLegacy.gameObject.activeSelf,
                "旧 [HP] 徽章已并入横条，必须隐藏");

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
            Assert.IsNull(hud.InkTank, "底部墨水卡应已移除（P42-E）");
            Assert.IsNotNull(hud.GaugeRoot, "横条组应有独立根节点，才能与底部面板同步移动");

            hud.ApplyMobileLayout();
            yield return null;

            RectTransform gaugeRoot = hud.GaugeRoot;
            Assert.AreEqual(0.5f, gaugeRoot.anchorMin.x, 1e-4f, "移动端横条组应锚定屏幕水平中心");
            Assert.AreEqual(0f, gaugeRoot.anchorMin.y, 1e-4f, "移动端横条组应锚定屏幕底部");
            Assert.AreEqual(HudSpecialGaugeFactory.StripWidthMobile, gaugeRoot.sizeDelta.x, 0.01f,
                "移动端横条组宽度应按移动端常量（318），不允许比桌面窄");
            Assert.AreEqual(HudSpecialGaugeFactory.StripHeightMobile, gaugeRoot.sizeDelta.y, 0.01f,
                "移动端横条组应放大（64 → 73），不允许比桌面小");
            Assert.AreEqual(HudSpecialGaugeFactory.StripBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);
            Assert.Less(gaugeRoot.sizeDelta.x, 1120f,
                "横条组必须留在屏幕中央，不能伸进左右触控按键区");

            // P42-E 两行：墨水卡移除，大招条 / 生命条各自挂在独立节点上
            Assert.IsNull(gaugeRoot.Find("InkCard"), "墨水卡应已移除");
            Assert.AreEqual(gaugeRoot.Find("SpecialCard"), hud.LiquidOrb.transform,
                "大招条应挂在独立的 SpecialCard 上");
            Assert.IsNotNull(gaugeRoot.Find("HealthCard"), "底部状态区应有独立的生命条");

            // 移动端几何：行高 38、整组 379×87（与桌面同构，只换尺寸）
            var mobileSpec = gaugeRoot.Find("SpecialCard") as RectTransform;
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupWidthMobile, mobileSpec.sizeDelta.x, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarRowHeightMobile, mobileSpec.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.BarGroupBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);

            // 三轮起两端都没有「Q」键帽节点；移动端只能靠徽章反色 + 触控大招键表达就绪
            hud.LiquidOrb.SetCharge(1f, true);
            yield return null;
            Assert.IsNull(hud.LiquidOrb.transform.Find("ReadyHint"), "条内不再生成「Q」键帽（含移动端）");
            Assert.AreEqual(ComicStyle.Yellow, hud.LiquidOrb.IconPlate.color,
                "移动端满能同样靠徽章翻黄表达");
            var mobileTrack = hud.LiquidOrb.transform.Find(HudSpecialGaugeFactory.BarTrackName) as RectTransform;
            Assert.AreEqual(HudSpecialGaugeFactory.BarTrackWidthMobile, mobileTrack.sizeDelta.x, 0.01f,
                "移动端轨道同样拉满（330）");

            // 重复应用布局必须幂等（分辨率/安全区变化会重复调用）
            hud.ApplyMobileLayout();
            Assert.AreEqual(HudSpecialGaugeFactory.StripHeightMobile, gaugeRoot.sizeDelta.y, 0.01f);
            Assert.AreEqual(HudSpecialGaugeFactory.StripBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);

            Object.Destroy(root);
            yield return null;
        }
    }
}
