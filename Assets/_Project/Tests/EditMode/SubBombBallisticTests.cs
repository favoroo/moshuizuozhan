using System.Collections.Generic;
using NUnit.Framework;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 副武器手雷 inkwave 弹道模型回归：
    /// 出手初速由相机俯仰物理决定（clamp 偏移映射）、0.4× 自身速度继承、
    /// 预览仿真与实飞同常数（重力 24 / 反弹 1.35）、穿透投掷者自身。
    /// 仿真场地架在 y=1000 空中平台，与编辑器当前加载场景的真实碰撞体隔离；
    /// 所有测试对象必须 TearDown 回收，否则残留挡板会拦截后续用例的弹道。
    /// </summary>
    public class SubBombBallisticTests
    {
        private const float GroundY = 1000f;
        private static readonly Vector3 Origin = new Vector3(1000f, GroundY + 1.35f, 1000f);

        private readonly List<GameObject> _created = new List<GameObject>();
        private WeaponConfigSO _config;

        [SetUp]
        public void SetUp()
        {
            // 顶面在 y=1000 的 400x400 空中平台（远离场景内一切碰撞体），供射线仿真落点判定
            // （EditMode 下碰撞查询即时可用，无需物理步进）
            var ground = new GameObject("SimGround");
            ground.transform.position = new Vector3(Origin.x, GroundY - 0.5f, Origin.z);
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(400f, 1f, 400f);
            _created.Add(ground);

            _config = ScriptableObject.CreateInstance<WeaponConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) Object.DestroyImmediate(_config);
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [Test]
        public void ComputeThrowVelocity_LevelPitch_MatchesInkwaveLob()
        {
            // 相机平视 0° + 偏移 16°：inkwave 标准平抛（出手 vy≈5.22、水平≈12.98）
            Vector3 v = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, _config);

            Assert.AreEqual(0f, v.x, 0.001f, "正前瞄准不应有侧向分量");
            Assert.AreEqual(5.22f, v.y, 0.06f, "平抛上抛分量 = sin(16°)*13.5 + 1.5");
            Assert.AreEqual(12.98f, new Vector2(v.x, v.z).magnitude, 0.06f, "水平初速 = cos(16°)*13.5");
        }

        [Test]
        public void ComputeThrowVelocity_PitchClampsAtInkwaveBounds()
        {
            Vector3 up = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 90f, Vector3.zero, _config);
            Assert.AreEqual(13.53f, up.y, 0.06f, "满仰角应钳制在 63°（sin=0.891*13.5+1.5），丢最远");

            Vector3 down = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, -90f, Vector3.zero, _config);
            Assert.AreEqual(-2.49f, down.y, 0.06f, "满俯角应钳制在 -17.2°（sin=-0.296*13.5+1.5），温柔贴脚抛");
        }

        [Test]
        public void ComputeThrowVelocity_InheritsFortyPercentOfCarryVelocity()
        {
            Vector3 baseline = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, _config);
            Vector3 v = InkBombTrajectoryUtility.ComputeThrowVelocity(
                Vector3.forward, 0f, new Vector3(5f, 3f, 2f), _config);

            Assert.AreEqual(baseline.x + 2f, v.x, 0.001f, "水平携带速度 0.4×5 = 2（跑动投掷顺势更远）");
            Assert.AreEqual(baseline.z + 0.8f, v.z, 0.001f, "水平携带速度 0.4×2 = 0.8");
            Assert.AreEqual(baseline.y, v.y, 0.001f, "携带速度的垂直分量不参与继承");
        }

        [Test]
        public void ComputeThrowVelocity_NullConfig_FallsBackToInkwaveDefaults()
        {
            Vector3 withConfig = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, _config);
            Vector3 without = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, null);

            Assert.AreEqual(withConfig.x, without.x, 0.001f);
            Assert.AreEqual(withConfig.y, without.y, 0.001f);
            Assert.AreEqual(withConfig.z, without.z, 0.001f);
        }

        [Test]
        public void SimulateThrow_LevelLobLandsAroundEightMetres()
        {
            Vector3 vel = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, _config);

            var points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, null, points, out Vector3 hit);

            Assert.IsTrue(landed, "平抛初速应能在 64 点预览窗口内落到平台");
            Assert.AreEqual(GroundY, hit.y, 0.05f, "预测落点应贴在平台顶面");
            float flat = new Vector2(hit.x - Origin.x, hit.z - Origin.z).magnitude;
            Assert.That(flat, Is.InRange(7f, 9.5f), "inkwave 平抛射程约 8m：远近由俯仰物理决定，不再有落点钳制");

            // 首点 = 出手点，尾部折叠为落点（LineRenderer 可整段提交）
            Assert.AreEqual(Origin, points[0]);
            Assert.AreEqual(hit, points[points.Length - 1]);
        }

        [Test]
        public void SimulateThrow_FullDownPitch_DropsAtFeet()
        {
            Vector3 vel = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, -90f, Vector3.zero, _config);

            var points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, null, points, out Vector3 hit);

            Assert.IsTrue(landed, "满俯角投掷也应落地");
            float flat = new Vector2(hit.x - Origin.x, hit.z - Origin.z).magnitude;
            Assert.That(flat, Is.InRange(1.5f, 4.5f), "满俯角被钳制后应温柔地丢在贴脚 2~4m，而不是砸脸");
        }

        [Test]
        public void ComputeThrowVelocityToLandAt_NearTarget_LandsWithinCoreRadius()
        {
            // 旧落点制契约回归：4m 目标（SpecialWeaponsPlayTests 场景）必须被砸中
            Vector3 target = new Vector3(Origin.x, GroundY, Origin.z + 4f);
            Vector3 vel = InkBombTrajectoryUtility.ComputeThrowVelocityToLandAt(Origin, target, _config);

            var points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, null, points, out Vector3 hit);

            Assert.IsTrue(landed, "反解弹道应落地");
            float miss = new Vector2(hit.x - target.x, hit.z - target.z).magnitude;
            Assert.Less(miss, 0.6f, "反解应落在目标点半米内（核心秒杀圈 1.2m，留积分器离散余量）");
        }

        [Test]
        public void ComputeThrowVelocityToLandAt_FarTarget_LandsWithinCoreRadius()
        {
            // 8m 接近满射程（平抛约 8m）：低弧反解仍应命中
            Vector3 target = new Vector3(Origin.x, GroundY, Origin.z + 8f);
            Vector3 vel = InkBombTrajectoryUtility.ComputeThrowVelocityToLandAt(Origin, target, _config);

            var points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, null, points, out Vector3 hit);

            Assert.IsTrue(landed, "反解弹道应落地");
            float miss = new Vector2(hit.x - target.x, hit.z - target.z).magnitude;
            Assert.Less(miss, 0.6f, "远距低弧反解仍应命中目标点");
        }

        [Test]
        public void SimulateThrow_IgnoresThrowerOwnCollider()
        {
            // 出手点正前方挡一块属于投掷者自身的碰撞体：穿透自身后仍应落到远处平台
            var self = new GameObject("Thrower");
            self.transform.position = new Vector3(Origin.x, GroundY, Origin.z);
            _created.Add(self);
            var blocker = new GameObject("SelfBlocker");
            blocker.transform.SetParent(self.transform, false);
            blocker.transform.localPosition = new Vector3(0f, 1.2f, 1f);
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 2.5f, 0.5f);

            Vector3 vel = InkBombTrajectoryUtility.ComputeThrowVelocity(Vector3.forward, 0f, Vector3.zero, _config);

            var points = new Vector3[InkBombTrajectoryUtility.TrajectoryPointCount];
            bool landed = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, self.transform, points, out Vector3 hit);

            Assert.IsTrue(landed, "穿透自身后应继续飞行并落地");
            float flat = new Vector2(hit.x - Origin.x, hit.z - Origin.z).magnitude;
            Assert.That(flat, Is.InRange(7f, 9.5f), "自身碰撞体不应截断弹道");

            // 对照组：不排除自身时弹道会被自己挡住（验证排除逻辑真的在起作用）
            bool blocked = InkBombTrajectoryUtility.SimulateThrow(Origin, vel, _config, null, points, out Vector3 selfHit);
            Assert.IsTrue(blocked);
            Assert.Less(selfHit.z - Origin.z, 1.6f, "不排除自身时出手即撞上自挡板");
        }
    }
}
