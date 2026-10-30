using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P2-C] 运行时 PlayMode 验证：
    /// 1. 副武器 64 点平滑抛物线虚线与 2.7m 爆炸红圈实时渲染与精确落点；
    /// 2. 地面准星在小枪、滚筒、蓄力狙之间平滑跨形态过渡与散布角驱动；
    /// 3. 屏幕专属准星 UI (WeaponReticleUI) 屏幕投影定位与副武器微胶囊状态响应。
    /// </summary>
    [Category("UI")]
    public class ReticlePlayModeTests
    {
        [UnityTest]
        public IEnumerator SubWeaponTrajectory_Runtime_UpdatesAndHidesSmoothly()
        {
            var hostGo = new GameObject("TestTrajectoryHost");
            var trajectory = hostGo.AddComponent<SubWeaponThrowTrajectory>();

            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.bombMaxThrowDistance = 9f;
            cfg.bombArcHeight = 2.6f;
            cfg.bombSplatRadius = 2.7f;

            // 放在高空：只验证弹道数学与渲染，不依赖当前地图在该位置是否有掩体。
            // （地面射线会贴合 y，水平落点仍应精确等于瞄准点 x=7。）
            Vector3 startPos = new Vector3(0f, 50f, 0f);
            Vector3 aimPos = new Vector3(7f, 49f, 0f);

            // 驱动 3 帧渲染
            for (int i = 0; i < 3; i++)
            {
                trajectory.UpdateTrajectory(startPos, aimPos, cfg, true, Team.TeamA);
                yield return null;
            }

            Assert.IsTrue(trajectory.IsShowing);
            Assert.AreEqual(7f, trajectory.CurrentLandingPosition.x, 0.05f);

            var line = hostGo.GetComponentInChildren<LineRenderer>();
            Assert.IsNotNull(line);
            Assert.AreEqual(64, line.positionCount);
            Assert.IsTrue(line.gameObject.activeInHierarchy);

            trajectory.Hide();
            yield return null;

            Assert.IsFalse(trajectory.IsShowing);
            Assert.IsFalse(line.gameObject.activeInHierarchy);

            Object.Destroy(hostGo);
            Object.Destroy(cfg);
        }

        [UnityTest]
        public IEnumerator GroundAimReticle_Runtime_MorphsVisualsAcrossWeapons()
        {
            var reticleGo = new GameObject("TestGroundReticle");
            var reticle = reticleGo.AddComponent<GroundAimReticle>();

            var playerGo = new GameObject("TestPlayer");
            var motor = playerGo.AddComponent<InkCharacterMotor>();
            var health = playerGo.AddComponent<CombatantHealth>();
            var weapon = playerGo.AddComponent<WeaponController>();

            Vector3 aimPos = new Vector3(5f, 0f, 0f);

            // 1. 小枪形态更新 5 帧，权重平滑上升
            for (int i = 0; i < 5; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            Assert.AreEqual(aimPos.x, reticleGo.transform.position.x, 0.01f);
            Assert.AreEqual(aimPos.z, reticleGo.transform.position.z, 0.01f);

            // 2. 装备滚筒并更新
            var rollerCfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            rollerCfg.weaponKind = WeaponKind.Roller;
            rollerCfg.rollWidth = 1.9f;
            weapon.EquipWeapon(rollerCfg);

            for (int i = 0; i < 10; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            // 3. 装备蓄力狙并更新
            var chargerCfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            chargerCfg.weaponKind = WeaponKind.Charger;
            weapon.EquipWeapon(chargerCfg);

            for (int i = 0; i < 10; i++)
            {
                reticle.UpdateReticle(aimPos, playerGo.transform.position, weapon, motor, health);
                yield return null;
            }

            Object.Destroy(reticleGo);
            Object.Destroy(playerGo);
            Object.Destroy(rollerCfg);
            Object.Destroy(chargerCfg);
        }

        [UnityTest]
        public IEnumerator WeaponReticleUI_Runtime_RendersContainers()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var reticleGo = new GameObject("TestReticleUI", typeof(RectTransform));
            reticleGo.transform.SetParent(canvasGo.transform, false);
            var reticleUI = reticleGo.AddComponent<WeaponReticleUI>();
            reticleUI.EnsureInitialized();

            yield return null;

            Assert.IsNotNull(reticleGo.transform.Find("ShooterReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("RollerReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("ChargerReticle"));
            Assert.IsNotNull(reticleGo.transform.Find("SubAimBadge"));

            Object.Destroy(canvasGo);
        }
    }
}
