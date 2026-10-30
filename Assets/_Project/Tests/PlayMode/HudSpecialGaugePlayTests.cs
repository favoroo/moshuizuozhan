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

            Assert.IsNotNull(hud.LiquidOrb, "HudController 应自动装配大招液体球");
            Assert.IsNotNull(hud.InkTank, "HudController 应自动装配墨水计量管");
            Assert.AreEqual(1, root.GetComponentsInChildren<LiquidOrbView>(true).Length,
                "只应生成一个大招球，不允许重复装配");
            Assert.AreEqual(1, root.GetComponentsInChildren<InkTankView>(true).Length,
                "只应生成一个墨水管，不允许重复装配");

            Assert.IsNotNull(hud.LiquidOrb.GetComponentInChildren<UILiquidFillGraphic>(true),
                "大招球内部应有波浪液体网格");
            Assert.IsNotNull(hud.InkTank.GetComponentInChildren<UILiquidFillGraphic>(true),
                "墨水管内部应有波浪液体网格");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiquidOrb_BurstsHaloAndHintWhenReady()
        {
            GameObject root = CreateCanvas();
            var cluster = HudSpecialGaugeFactory.BuildCluster(root.transform, null, null);
            LiquidOrbView orb = cluster.Orb;
            Assert.IsNotNull(orb, "工厂应返回大招球视图");

            var halo = orb.GetComponentInChildren<UIRotatingRingGraphic>(true);
            var burst = orb.GetComponentInChildren<UIBurstGraphic>(true);
            Assert.IsNotNull(halo, "满能光环应存在于球体外围");
            Assert.IsNotNull(burst, "满能爆闪应存在");

            orb.SetCharge(0.35f, false);
            yield return null;
            Assert.IsFalse(orb.IsReady);
            Assert.AreEqual(0, orb.ReadyBurstCount, "未满能不应爆发");

            orb.SetCharge(1f, true);
            Assert.IsTrue(orb.IsReady, "满能后 IsReady 应为 true");
            Assert.AreEqual(1, orb.ReadyBurstCount, "满能瞬间应爆发一次");
            Assert.IsTrue(burst.IsPlaying, "满能瞬间冲击波应在播放");

            // 等光环强度弹簧升起
            float elapsed = 0f;
            while (elapsed < 0.4f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(halo.gameObject.activeSelf, "满能后金色光环应显示");
            Assert.Greater(halo.Intensity, 0.2f, "光环强度应升起来");

            // 掉回未满能：提示与光环应回落
            orb.SetCharge(0f, false);
            Assert.IsFalse(orb.IsReady);

            elapsed = 0f;
            while (elapsed < 1.2f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.Less(halo.Intensity, 0.1f, "释放/清零后光环应熄灭");

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

            Assert.IsNotNull(marker, "墨水管应有副武器刻度线节点");

            tank.SetThreshold01(0.55f);
            yield return null;
            float at55 = marker.anchoredPosition.y;

            tank.SetThreshold01(0.7f);
            yield return null;
            float at70 = marker.anchoredPosition.y;

            Assert.Greater(at70, at55, "刻度线高度应随阈值上升");

            Object.Destroy(root);
            yield return null;
        }
    }
}
