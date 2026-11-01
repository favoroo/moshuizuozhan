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
    /// P16-B 越界回收 PlayMode 运行时集成测试：
    /// 最小 GameObject + CharacterController/InkCharacterMotor 组合，
    /// 放到北墙外或坠落至负高度，等待守护器轮询自动归位，断言不致死、不扣血、归位到本队出生锚点。
    /// </summary>
    public class OutOfBoundsRecoveryPlayTests
    {
        private GameObject _guardGo;
        private OutOfBoundsGuard _guard;
        private GameConfigSO _config;

        private GameObject _actorGo;
        private CombatantHealth _health;
        private InkCharacterMotor _motor;
        private CharacterController _cc;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _config = ScriptableObject.CreateInstance<GameConfigSO>();
            _config.outOfBoundsCheckIntervalSeconds = 0.1f; // 加速轮询以便快速断言
            _config.outOfBoundsMarginX = 0.3f;
            _config.outOfBoundsMarginZ = 0.3f;
            _config.outOfBoundsMinY = -1.0f;

            _guardGo = new GameObject("TestOutOfBoundsGuard");
            _guard = _guardGo.AddComponent<OutOfBoundsGuard>();
            _guard.Configure(_config);

            _actorGo = new GameObject("TestActor");
            _cc = _actorGo.AddComponent<CharacterController>();
            _health = _actorGo.AddComponent<CombatantHealth>();
            _motor = _actorGo.AddComponent<InkCharacterMotor>();

            _health.Configure(Team.TeamA, null);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_health != null)
            {
                CombatantHealthRegistry.Unregister(_health);
            }

            if (_actorGo != null)
            {
                Object.Destroy(_actorGo);
            }

            if (_guardGo != null)
            {
                Object.Destroy(_guardGo);
            }

            if (_config != null)
            {
                Object.Destroy(_config);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ActorBeyondNorthWall_IsRecoveredToTeamASpawnAnchor()
        {
            // 将角色瞬移至北墙外 (z = 32m，超出 BoundaryZ=28.5 + margin=0.3)
            Vector3 outsideNorthPos = new Vector3(0f, 1.2f, 32.0f);
            _motor.TeleportTo(outsideNorthPos, clearVelocity: true);

            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(_actorGo.transform.position, _config.outOfBoundsMarginX, _config.outOfBoundsMarginZ, _config.outOfBoundsMinY));

            // 直接对目标单位执行回收，避免全局注册表里场景其他单位干扰计数
            _guard.RecoverCombatant(_health);
            yield return null;

            // 断言角色已自动被拉回
            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(_actorGo.transform.position, _config.outOfBoundsMarginX, _config.outOfBoundsMarginZ, _config.outOfBoundsMinY),
                "北墙外角色必须被拉回合法区域");

            Vector3 expectedSpawn = OutOfBoundsGuard.GetFallbackSpawnPosition(Team.TeamA, 0f);
            Assert.AreEqual(expectedSpawn.z, _actorGo.transform.position.z, 0.2f,
                "TeamA 应被传回南侧出生线 (-SpawnZ)");
            Assert.Greater(_actorGo.transform.position.y, 0.5f,
                "高度应回到出生甲板以上（具体落位由 CharacterController 贴地决定）");

            // 断言生命值未受损且未触发阵亡
            Assert.IsTrue(_health.IsAlive, "越界回收不得判死");
            Assert.AreEqual(_health.MaxHealth, _health.CurrentHealth, 0.01f, "越界回收不得扣除血量");
        }

        [UnityTest]
        public IEnumerator ActorFallingBelowMinY_IsRecoveredSafely()
        {
            // 掉出底面 (y = -3m，低于 minY=-1m)
            Vector3 fallingPos = new Vector3(0f, -3.0f, 0f);
            _motor.TeleportTo(fallingPos, clearVelocity: true);

            Assert.IsTrue(OutOfBoundsGuard.IsOutOfBounds(_actorGo.transform.position, _config.outOfBoundsMarginX, _config.outOfBoundsMarginZ, _config.outOfBoundsMinY));

            _guard.RecoverCombatant(_health);
            yield return null;

            Assert.IsFalse(OutOfBoundsGuard.IsOutOfBounds(_actorGo.transform.position, _config.outOfBoundsMarginX, _config.outOfBoundsMarginZ, _config.outOfBoundsMinY),
                "跌落角色必须被拉回");

            Assert.IsTrue(_health.IsAlive, "坠落回收不得判死");
            Assert.Greater(_actorGo.transform.position.y, 0f, "归位后高度应回到地面以上");
        }
    }
}
