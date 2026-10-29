using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 角色与武器模型程序化装配与动态表现的运行时集成测试 (P5-A)。
    /// </summary>
    public class CharacterVisualPlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "场景里应存在本地玩家 PlayerController");
            Assert.IsNotNull(InkSurfaceController.Instance, "场景里应存在 InkSurfaceController");
        }

        [UnityTest]
        public IEnumerator LocalPlayer_HasAssembledVisualController_WithKidAndSquidRoots()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var visual = player.GetComponentInChildren<CharacterVisualController>();

            Assert.IsNotNull(visual, "玩家应挂载 CharacterVisualController");
            Assert.IsNotNull(visual.KidRoot, "人形态根节点 KidRoot 应存在");
            Assert.IsNotNull(visual.SquidRoot, "乌贼形态根节点 SquidRoot 应存在");
            Assert.IsNotNull(visual.WeaponRoot, "武器根节点 WeaponRoot 应存在");
            Assert.IsTrue(visual.KidRoot.activeSelf, "人形态初始应可见");
        }

        [UnityTest]
        public IEnumerator WeaponEquip_SwitchesModelAndMuzzle_AtRuntime()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            var visual = player.GetComponentInChildren<CharacterVisualController>();
            var weaponCtrl = player.Weapon;

            Assert.IsNotNull(visual);
            Assert.IsNotNull(weaponCtrl);

            // 切换为蓄力狙 (Charger)
            visual.EquipWeaponKind(WeaponKind.Charger);
            yield return null;

            Assert.AreEqual(WeaponKind.Charger, visual.CurrentWeaponKind);
            Transform chargerGo = visual.WeaponRoot.transform.Find("Glint_Charger");
            Assert.IsNotNull(chargerGo);
            Assert.IsTrue(chargerGo.gameObject.activeSelf, "狙击枪模型应处于激活状态");

            // 切换为双枪 (Dualies)
            visual.EquipWeaponKind(WeaponKind.Dualies);
            yield return null;

            Assert.AreEqual(WeaponKind.Dualies, visual.CurrentWeaponKind);
            Transform dualiesGo = visual.WeaponRoot.transform.Find("Twinfin_Dualies");
            Assert.IsNotNull(dualiesGo);
            Assert.IsTrue(dualiesGo.gameObject.activeSelf, "双枪模型应处于激活状态");
            Assert.IsFalse(chargerGo.gameObject.activeSelf, "非当前武器模型应隐藏");
        }

        [UnityTest]
        public IEnumerator Swimming_TransformsBetweenKidAndSquidForms()
        {
            yield return LoadPlaybox();

            var player = PlayerController.LocalPlayer;
            player.enabled = false; // 禁用外部输入覆盖，直接测试运动与视觉系统

            var motor = player.Motor;
            var visual = player.GetComponentInChildren<CharacterVisualController>();

            Assert.IsNotNull(motor);
            Assert.IsNotNull(visual);

            // 出生台按竞技规则不可涂色，将角色移到可涂地主地面 (0, 0.1, -11)
            var paintSpot = new Vector3(0f, 0.1f, -11f);
            InkSurfaceController.Instance.PaintCircle(paintSpot, 3.5f, player.Health.Team);

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = paintSpot;
            if (cc != null) cc.enabled = true;
            yield return null;

            Assert.AreEqual(player.Health.Team, motor.FootInkTeam, "角色应站在己方墨水上");

            // 按住潜行键
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
            for (int i = 0; i < 6; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, true, false);
                yield return null;
            }

            Assert.IsTrue(motor.IsSwimming, "站在己方墨水上按潜行应进入潜行形态");
            Assert.IsTrue(visual.SquidRoot.activeSelf, "潜墨状态下乌贼形态应展开可见");

            // 松开潜行键
            motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
            for (int i = 0; i < 8; i++)
            {
                motor.SetFrameIntent(Vector3.zero, Vector3.forward, false, false);
                yield return null;
            }

            Assert.IsFalse(motor.IsSwimming, "松开潜行后角色应脱离潜行");
            Assert.IsTrue(visual.KidRoot.activeSelf, "脱离潜行后人形态应重新可见");
        }
    }
}
