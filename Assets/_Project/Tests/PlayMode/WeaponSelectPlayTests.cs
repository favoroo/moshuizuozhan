using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI.WeaponSelect;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P8] 战前武器选择与换装运行时测试（PlayMode）：
    /// 1. Ready→Countdown 时本地玩家装备被准确替换；
    /// 2. Bot 的武器配置完全不受玩家换装影响；
    /// 3. 非法索引（负数/超范围）安全回落为合法武器（默认 Shooter）。
    /// </summary>
    public class WeaponSelectPlayTests
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
                    Object.Destroy(_createdObjects[i]);
                }
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                {
                    Object.Destroy(_createdConfigs[i]);
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

        [UnityTest]
        public IEnumerator LocalPlayerWeaponReplaced_WhenCountdownStarts()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            // 1. 创建玩家角色，初始配备 Shooter
            GameObject playerGo = CreateCombatantActor("TestPlayer", Team.TeamA, shooterCfg, isLocalPlayer: true);
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "LocalPlayer 应该已被正确注册");
            Assert.AreEqual(WeaponKind.Shooter, PlayerController.LocalPlayer.Weapon.Config.weaponKind);

            // 2. 创建 Bootstrap 实例并模拟换装应用
            var bootGo = new GameObject("TestBootstrap", typeof(WeaponSelectBootstrap));
            _createdObjects.Add(bootGo);
            var boot = bootGo.GetComponent<WeaponSelectBootstrap>();

            boot.ApplyWeaponToLocalPlayer(chargerCfg);
            yield return null;

            // 3. 验证玩家武器成功被替换为 Charger
            Assert.AreEqual(WeaponKind.Charger, PlayerController.LocalPlayer.Weapon.Config.weaponKind, "玩家武器应已被替换为 Glint Charger");
        }

        [UnityTest]
        public IEnumerator BotWeaponRemainsUntouched_WhenPlayerWeaponChanges()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var rollerCfg = CreateConfig(WeaponKind.Roller, "Swell Roller");
            var dualiesCfg = CreateConfig(WeaponKind.Dualies, "Twinfin Dualies");

            // 1. 创建玩家（初始 Shooter）与 Bot（初始 Roller）
            GameObject playerGo = CreateCombatantActor("Player", Team.TeamA, shooterCfg, isLocalPlayer: true);
            GameObject botGo = CreateCombatantActor("BotB_01", Team.TeamB, rollerCfg, isLocalPlayer: false);
            yield return null;

            var botWeapon = botGo.GetComponent<WeaponController>();
            Assert.AreEqual(WeaponKind.Roller, botWeapon.Config.weaponKind);

            // 2. 玩家换装为 Dualies
            var bootGo = new GameObject("TestBootstrap", typeof(WeaponSelectBootstrap));
            _createdObjects.Add(bootGo);
            var boot = bootGo.GetComponent<WeaponSelectBootstrap>();

            boot.ApplyWeaponToLocalPlayer(dualiesCfg);
            yield return null;

            // 3. 验证玩家成功换成 Dualies，而 Bot 的武器依然是 Roller，完全不受影响
            Assert.AreEqual(WeaponKind.Dualies, PlayerController.LocalPlayer.Weapon.Config.weaponKind);
            Assert.AreEqual(WeaponKind.Roller, botWeapon.Config.weaponKind, "Bot 的武器绝对不应受玩家换装影响");
        }

        [Test]
        public void InvalidIndex_FallsBackToShooterSafely()
        {
            // 负数越界
            int clampedNegative = WeaponSelectMath.ClampIndex(-99);
            Assert.AreEqual(0, clampedNegative);
            Assert.AreEqual(WeaponKind.Shooter, WeaponSelectMath.IndexToKind(-99));

            // 正数越界
            int clampedPositive = WeaponSelectMath.ClampIndex(999);
            Assert.AreEqual(6, clampedPositive);
            Assert.AreEqual(WeaponKind.Splatling, WeaponSelectMath.IndexToKind(999));
        }
    }
}
