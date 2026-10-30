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
    /// [P6-A] 运行时打击反馈验收（走真实 CombatantHealth 伤害链路，与武器命中完全同一条路径）：
    /// 1. 本地玩家命中敌人 → 准星 X 型打击标记 + 连击计数；
    /// 2. 本地玩家击杀敌人 → 全局时间微缩（Hitstop）并在真实时间 0.2s 内弹簧恢复到 1.0；
    /// 3. 本地玩家自身受击 → 屏幕中央定向受击弧（伤害来源方向）。
    /// </summary>
    [Category("UI")]
    public class HitFeedbackPlayTests
    {
        private GameObject _playerGo;
        private GameObject _enemyGo;
        private GameObject _canvasGo;
        private GameObject _reticleGo;
        private WeaponReticleUI _reticle;
        private CombatantHealth _enemy;
        private PlayerController _player;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;

            _canvasGo = new GameObject("TestFeedbackCanvas",
                typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            _canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            _reticleGo = new GameObject("TestFeedbackReticle", typeof(RectTransform));
            _reticleGo.transform.SetParent(_canvasGo.transform, false);
            _reticle = _reticleGo.AddComponent<WeaponReticleUI>();
            yield return null;

            _playerGo = new GameObject("TestLocalPlayer",
                typeof(CharacterController),
                typeof(CombatantHealth),
                typeof(InkCharacterMotor),
                typeof(WeaponController),
                typeof(PlayerController));
            _player = _playerGo.GetComponent<PlayerController>();
            _player.Health.Configure(Team.TeamA, null);
            yield return null;

            _enemyGo = new GameObject("TestEnemy", typeof(CombatantHealth));
            _enemyGo.transform.position = new Vector3(3f, 0f, 0f);
            _enemy = _enemyGo.GetComponent<CombatantHealth>();
            _enemy.Configure(Team.TeamB, null);

            yield return null;

            Assert.IsNotNull(_player.Health, "本地玩家必须存在");
            Assert.AreSame(_player, PlayerController.LocalPlayer, "LocalPlayer 单例应指向测试玩家");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            HitstopService.Cancel();
            Time.timeScale = 1f;

            if (_enemyGo != null) Object.Destroy(_enemyGo);
            if (_playerGo != null) Object.Destroy(_playerGo);
            if (_reticleGo != null) Object.Destroy(_reticleGo);
            if (_canvasGo != null) Object.Destroy(_canvasGo);

            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalHit_ShowsHitMarker_AndComboEscalates()
        {
            bool killed = _enemy.TakeDamage(72f, Team.TeamA, _enemy.transform.position, _player.Health, "Spritzer");
            Assert.IsFalse(killed, "72 伤害不应击杀满血敌人");

            yield return null;

            Assert.IsTrue(_reticle.IsHitMarkerPlaying, "命中敌人后准星应弹出打击标记");
            Assert.AreEqual(0, _reticle.HitCombo, "第一次命中应为 0 连击");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "非致命命中不应触发 Hitstop");

            _enemy.TakeDamage(10f, Team.TeamA, _enemy.transform.position, _player.Health, "Spritzer");
            yield return null;

            Assert.AreEqual(1, _reticle.HitCombo, "连击窗口内的第二次命中应累计连击");
        }

        [UnityTest]
        public IEnumerator LocalKill_TriggersHitstop_ThenRecoversToNormal()
        {
            bool killed = _enemy.TakeDamage(200f, Team.TeamA, _enemy.transform.position, _player.Health, "Spritzer");
            Assert.IsTrue(killed, "200 伤害应击杀敌人");

            Assert.AreEqual(0.2f, Time.timeScale, 0.01f, "击杀瞬间应冻结到 0.2 倍时间尺度");
            Assert.IsTrue(_reticle.IsHitMarkerPlaying, "击杀同样要弹出打击标记（更大更亮）");

            yield return new WaitForSecondsRealtime(0.4f);

            Assert.AreEqual(1f, Time.timeScale, 0.001f, "0.15s 内必须用弹簧恢复到正常时间尺度");
            Assert.IsFalse(HitstopService.IsActive, "恢复后 Hitstop 必须完全归位，不能永久卡住时间");
        }

        [UnityTest]
        public IEnumerator RemoteKill_DoesNotTriggerHitstop()
        {
            // 攻击者不是本地玩家（Bot 击杀）时，玩家屏幕不应被时间微缩打断
            bool killed = _enemy.TakeDamage(200f, Team.TeamB, _enemy.transform.position, null, "Spritzer");
            Assert.IsFalse(killed, "同队伤害不应生效");

            var otherEnemyGo = new GameObject("TestEnemyB", typeof(CombatantHealth));
            var otherEnemy = otherEnemyGo.GetComponent<CombatantHealth>();
            otherEnemy.Configure(Team.TeamB, null);

            var botGo = new GameObject("TestBotA", typeof(CombatantHealth));
            var bot = botGo.GetComponent<CombatantHealth>();
            bot.Configure(Team.TeamA, null);

            killed = otherEnemy.TakeDamage(200f, Team.TeamA, otherEnemy.transform.position, bot, "Spritzer");
            Assert.IsTrue(killed);
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Bot 击杀不应触发玩家侧 Hitstop");

            yield return null;

            Object.Destroy(otherEnemyGo);
            Object.Destroy(botGo);
        }

        [UnityTest]
        public IEnumerator LocalPlayerDamaged_SpawnsDamageDirectionArc()
        {
            bool killed = _player.Health.TakeDamage(30f, Team.TeamB, _player.transform.position, _enemy, "Spritzer");
            Assert.IsFalse(killed, "30 伤害不应击杀满血玩家");

            yield return null;

            Assert.AreEqual(1, _reticle.ActiveDamageArcCount, "自身受击应立即生成一条指向伤害来源的指示弧");

            // 同一攻击者连续命中只刷新同一条弧，不刷屏
            _player.Health.TakeDamage(20f, Team.TeamB, _player.transform.position, _enemy, "Spritzer");
            yield return null;
            Assert.AreEqual(1, _reticle.ActiveDamageArcCount, "同一攻击者不应叠加多条弧");
        }
    }
}
