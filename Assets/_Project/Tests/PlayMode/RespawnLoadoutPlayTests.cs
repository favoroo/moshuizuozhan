using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.UI.WeaponSelect;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// [P36] 阵亡整备（复活换枪）运行时测试（PlayMode）：
    /// 1. 换枪清空大招充能；不带 reset 的战前换装绝不能顺手清空（回归）；
    /// 2. 挂起选择的索引清洗与 PlayerPrefs 落盘；
    /// 3. 整备条运行时装配出 7 个可点槽位、点选回传配置、收起后停用对象（不挡射线）。
    /// </summary>
    [Category("UI")]
    public class RespawnLoadoutPlayTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();
        private int _originalPrefsIndex;

        [SetUp]
        public void SetUp()
        {
            _originalPrefsIndex = PlayerPrefs.GetInt(WeaponSelectMath.PrefsKeySelectedWeaponIndex, 0);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetInt(WeaponSelectMath.PrefsKeySelectedWeaponIndex, _originalPrefsIndex);
            PlayerPrefs.Save();

            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] != null)
                {
                    var health = _createdObjects[i].GetComponent<CombatantHealth>();
                    if (health != null)
                    {
                        CombatantHealthRegistry.Unregister(health);
                    }
                    UnityEngine.Object.Destroy(_createdObjects[i]);
                }
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                {
                    UnityEngine.Object.Destroy(_createdConfigs[i]);
                }
            }
            _createdConfigs.Clear();
        }

        private WeaponConfigSO CreateConfig(WeaponKind kind, string name)
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.weaponKind = kind;
            cfg.displayName = name;
            _createdConfigs.Add(cfg);
            return cfg;
        }

        private GameObject CreateCombatantActor(string name, Team team, WeaponConfigSO config, bool isLocalPlayer)
        {
            var go = new GameObject(name, typeof(CharacterController), typeof(InkCharacterMotor), typeof(WeaponController), typeof(CombatantHealth));
            _createdObjects.Add(go);

            var health = go.GetComponent<CombatantHealth>();
            health.Configure(team, null);
            CombatantHealthRegistry.Register(health);

            var weapon = go.GetComponent<WeaponController>();
            weapon.EquipWeapon(config);

            if (isLocalPlayer)
            {
                go.AddComponent<PlayerController>();
            }

            return go;
        }

        private WeaponSelectBootstrap CreateBootstrap()
        {
            var bootGo = new GameObject("TestBootstrap", typeof(WeaponSelectBootstrap));
            _createdObjects.Add(bootGo);
            return bootGo.GetComponent<WeaponSelectBootstrap>();
        }

        [UnityTest]
        public IEnumerator EquipWeapon_WithResetSpecialCharge_ClearsGauge()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            GameObject playerGo = CreateCombatantActor("TestPlayer", Team.TeamA, shooterCfg, isLocalPlayer: true);
            yield return null;

            var weapon = playerGo.GetComponent<WeaponController>();
            weapon.AddSpecialPointsRaw(20f);
            Assert.Greater(weapon.SpecialPoints, 0f, "前置：大招应已充能");

            weapon.EquipWeapon(chargerCfg, resetSpecialCharge: true);

            Assert.AreEqual(WeaponKind.Charger, weapon.Config.weaponKind, "复活换枪后应装备新武器");
            Assert.AreEqual(0f, weapon.SpecialPoints, "阵亡整备换枪必须清空大招充能");
            Assert.AreEqual(0f, weapon.SpecialCharge01, "充能比例必须同步归零");
        }

        [UnityTest]
        public IEnumerator EquipWeapon_WithoutReset_PreservesSpecialCharge()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            GameObject playerGo = CreateCombatantActor("TestPlayer", Team.TeamA, shooterCfg, isLocalPlayer: true);
            yield return null;

            var weapon = playerGo.GetComponent<WeaponController>();
            weapon.AddSpecialPointsRaw(20f);
            float before = weapon.SpecialPoints;

            weapon.EquipWeapon(chargerCfg);

            Assert.AreEqual(before, weapon.SpecialPoints, "战前换装（不带 reset）不能动大招充能");
        }

        [UnityTest]
        public IEnumerator ApplyWeaponToLocalPlayer_WithReset_ClearsSpecialCharge()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            GameObject playerGo = CreateCombatantActor("TestPlayer", Team.TeamA, shooterCfg, isLocalPlayer: true);
            yield return null;

            var weapon = playerGo.GetComponent<WeaponController>();
            weapon.AddSpecialPointsRaw(20f);

            WeaponSelectBootstrap boot = CreateBootstrap();
            boot.ApplyWeaponToLocalPlayer(chargerCfg, resetSpecialCharge: true);
            yield return null;

            Assert.AreEqual(WeaponKind.Charger, PlayerController.LocalPlayer.Weapon.Config.weaponKind);
            Assert.AreEqual(0f, PlayerController.LocalPlayer.Weapon.SpecialPoints);
        }

        [UnityTest]
        public IEnumerator SelectRespawnLoadout_ClampsAndPersistsChoice()
        {
            WeaponSelectBootstrap boot = CreateBootstrap();
            yield return null;

            boot.SelectRespawnLoadout(3);
            Assert.AreEqual(3, WeaponSelectMath.LoadSelectedWeaponIndex(), "点选应立即落盘");

            boot.SelectRespawnLoadout(-2);
            Assert.AreEqual(0, WeaponSelectMath.LoadSelectedWeaponIndex(), "负索引必须清洗为 0");

            boot.SelectRespawnLoadout(99);
            Assert.AreEqual(RespawnLoadoutMath.SlotCount - 1, WeaponSelectMath.LoadSelectedWeaponIndex(),
                "超范围索引必须清洗为最后一个槽位");

            // 测试环境没有 MatchManager / RuntimeAssetLibrary，_configs 为空：
            // 结算必须安全返回 false，而不是空引用。
            Assert.IsFalse(boot.ApplyPendingRespawnLoadout(), "资产缺失时结算应安全失败");
        }

        [UnityTest]
        public IEnumerator View_BuildsSevenSlotsAndReportsSelection()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            var configs = new WeaponConfigSO[RespawnLoadoutMath.SlotCount];
            configs[0] = shooterCfg;
            configs[1] = chargerCfg;
            var icons = new Sprite[RespawnLoadoutMath.SlotCount];

            var viewGo = new GameObject(
                "RespawnLoadout_Canvas", typeof(Canvas), typeof(RespawnLoadoutView));
            _createdObjects.Add(viewGo);
            var view = viewGo.GetComponent<RespawnLoadoutView>();

            int reportedIndex = -1;
            WeaponConfigSO reportedConfig = null;
            view.OnWeaponSelected += (index, config) =>
            {
                reportedIndex = index;
                reportedConfig = config;
            };

            view.Configure(UIThemeSO.Fallback, configs, icons);
            yield return null;

            var buttons = viewGo.GetComponentsInChildren<Button>(true);
            Assert.AreEqual(RespawnLoadoutMath.SlotCount, buttons.Length, "整备条应恰好装配 7 个可点槽位");

            view.SetVisible(true);
            view.SetCountdown(2.5f);
            yield return null;

            Assert.IsTrue(view.IsVisible);

            var texts = viewGo.GetComponentsInChildren<TextMeshProUGUI>(true);
            Assert.IsTrue(Array.Exists(texts, t => t.text.Contains("2.5")), "倒计时必须写进整备条");
            Assert.IsFalse(Array.Exists(texts, t => t.text.Contains("伤害")), "简化版整备条不再展示武器定位/读数");
            Assert.IsTrue(Array.Exists(texts, t => t.text.Contains("大招")), "必须保留一行换枪代价提示");
            Assert.IsTrue(Array.Exists(texts, t => t.text == "阵亡整备"), "标题必须是「阵亡整备」");

            view.SelectIndex(1);
            Assert.AreEqual(1, view.SelectedIndex);
            Assert.AreEqual(1, reportedIndex, "点选必须回传下标");
            Assert.AreSame(chargerCfg, reportedConfig, "点选必须回传对应武器配置");

            view.SetSelectedIndex(0);
            Assert.AreEqual(0, view.SelectedIndex, "外部同步应更新选中项");

            view.SetVisible(false);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(viewGo.activeSelf, "收起后必须停用对象，避免挡住射线/出现在截图里");
        }
    }
}
