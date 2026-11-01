using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Environment;
using P1.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// P16-A 出生禁入圈 PlayMode 运行时集成测试：
    /// 敌队角色进入禁入圈内被径向推出、本队角色不受影响保持不动。
    /// </summary>
    public class SpawnZonePlayTests
    {
        private GameObject _zoneGo;
        private SpawnZone _zone;

        private GameObject _allyGo;
        private CombatantHealth _allyHealth;
        private InkCharacterMotor _allyMotor;

        private GameObject _enemyGo;
        private CombatantHealth _enemyHealth;
        private InkCharacterMotor _enemyMotor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _zoneGo = new GameObject("TestSpawnZone");
            _zone = _zoneGo.AddComponent<SpawnZone>();
            _zone.Configure(Team.TeamA, null);
            // 放在空中隔离区，避免场景中央高台/掩体的 CharacterController 碰撞把测试单位横向挤开
            _zone.SetCenter(new Vector3(0f, 50f, 0f));
            _zone.SetRadiusAndSpeed(6.0f, 10.0f); // 半径 6m，外推速度 10 m/s

            // 本队角色 (TeamA)
            _allyGo = new GameObject("AllyActor");
            _allyGo.AddComponent<CharacterController>();
            _allyHealth = _allyGo.AddComponent<CombatantHealth>();
            _allyMotor = _allyGo.AddComponent<InkCharacterMotor>();
            _allyHealth.Configure(Team.TeamA, null);

            // 敌队角色 (TeamB)
            _enemyGo = new GameObject("EnemyActor");
            _enemyGo.AddComponent<CharacterController>();
            _enemyHealth = _enemyGo.AddComponent<CombatantHealth>();
            _enemyMotor = _enemyGo.AddComponent<InkCharacterMotor>();
            _enemyHealth.Configure(Team.TeamB, null);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_allyHealth != null) CombatantHealthRegistry.Unregister(_allyHealth);
            if (_enemyHealth != null) CombatantHealthRegistry.Unregister(_enemyHealth);

            if (_allyGo != null) Object.Destroy(_allyGo);
            if (_enemyGo != null) Object.Destroy(_enemyGo);
            if (_zoneGo != null) Object.Destroy(_zoneGo);

            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyInsideZone_IsPushedOutward_AllyRemainsStill()
        {
            // 本队角色放在圈内另一侧，敌队角色放在 +X 侧，避免两者互相干涉
            Vector3 allyStart = new Vector3(-2.0f, 50.0f, 0f);
            Vector3 enemyStart = new Vector3(1.0f, 50.0f, 0f);
            _allyMotor.TeleportTo(allyStart, clearVelocity: true);
            _enemyMotor.TeleportTo(enemyStart, clearVelocity: true);

            yield return null;

            // 直接对单个单位施加一次 0.3s 推力，避免全局注册表与场景 MatchManager 阶段门禁干扰
            bool enemyPushed = _zone.TryPush(_enemyHealth, 0.3f);
            bool allyPushed = _zone.TryPush(_allyHealth, 0.3f);
            yield return null;

            Assert.IsTrue(enemyPushed, "敌方角色在圈内应被推");
            Assert.IsFalse(allyPushed, "本队角色不应被推");

            // 1. 本队友军不应被推出（只比较水平位移，排除无地面时的重力下落）
            Vector3 allyXZ = new Vector3(_allyGo.transform.position.x, 0f, _allyGo.transform.position.z);
            Vector3 startXZ = new Vector3(allyStart.x, 0f, allyStart.z);
            float allyDrift = Vector3.Distance(allyXZ, startXZ);
            Assert.Less(allyDrift, 0.2f, "本队角色在禁入圈内不应被推出");

            // 2. 敌队角色应被明显向外推移（沿 +X 径向）
            Assert.Greater(_enemyGo.transform.position.x, 2.0f, "敌方角色进入圈内应被径向外推");

            // 3. 双方均存活且生命值未受损
            Assert.IsTrue(_allyHealth.IsAlive && _enemyHealth.IsAlive, "禁入圈推力不致死");
            Assert.AreEqual(_allyHealth.MaxHealth, _allyHealth.CurrentHealth, 0.01f);
            Assert.AreEqual(_enemyHealth.MaxHealth, _enemyHealth.CurrentHealth, 0.01f);
        }
    }
}
