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
            Assert.AreEqual(ComicStyle.Paper, orb.IconPlate.color, "未满能徽章应为纸白");

            orb.SetCharge(1f, true);
            yield return null;
            Assert.IsTrue(orb.IsReady, "满能后 IsReady 应为 true");
            Assert.AreEqual(1, orb.ReadyBurstCount, "进入满能应计数一次");
            Assert.AreEqual(1f, orb.BarFill.fillAmount, 0.01f, "满能条应铺满");
            Assert.AreEqual(ComicStyle.Yellow, orb.IconPlate.color, "满能徽章应翻黄");

            Transform readyHint = orb.transform.Find("ReadyHint");
            Assert.IsNotNull(readyHint, "大招条应包含满能按键提示节点");
            Assert.IsTrue(readyHint.gameObject.activeSelf, "桌面端满能应显示「Q 释放」提示");
            Assert.IsFalse(orb.transform.Find("Percent").gameObject.activeSelf, "满能时百分比让位给按键提示");

            // 掉回未满能：提示收起、百分比回来、徽章恢复纸白
            orb.SetCharge(0f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.IsFalse(readyHint.gameObject.activeSelf, "释放后提示应隐藏");
            Assert.IsTrue(orb.transform.Find("Percent").gameObject.activeSelf, "释放后百分比应回来");
            Assert.AreEqual(ComicStyle.Paper, orb.IconPlate.color, "释放后徽章恢复纸白");

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
                "仪表条应与底部信息条同宽");
            Assert.AreEqual(HudSpecialGaugeFactory.StripHeightMobile, gaugeRoot.sizeDelta.y, 0.01f,
                "移动端底部信息条应收矮（隐藏键盘提示行）");
            Assert.AreEqual(HudSpecialGaugeFactory.StripBottomYMobile, gaugeRoot.anchoredPosition.y, 0.01f);
            Assert.Less(gaugeRoot.sizeDelta.x, 1120f,
                "仪表条必须留在屏幕中央，不能伸进左右触控按键区");

            // 墨水条 / 大招条必须在同一行、血条正上方
            Assert.AreEqual(gaugeRoot.Find("MeterRow"), hud.InkTank.transform.parent,
                "墨水条应挂在仪表行里");
            Assert.AreEqual(gaugeRoot.Find("MeterRow"), hud.LiquidOrb.transform.parent,
                "大招条应与墨水条同一行");

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
