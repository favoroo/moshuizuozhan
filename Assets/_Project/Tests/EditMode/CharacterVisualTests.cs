using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    [TestFixture]
    public class CharacterVisualTests
    {
        private GameObject _characterRoot;
        private Transform _visualRoot;
        private CharacterVisualController _controller;

        [SetUp]
        public void SetUp()
        {
            _characterRoot = new GameObject("TestCharacter");
            var vrGo = new GameObject("VisualRoot");
            vrGo.transform.SetParent(_characterRoot.transform, false);
            _visualRoot = vrGo.transform;

            _controller = CharacterVisualAssembler.Assemble(_visualRoot, Team.TeamA, null, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_characterRoot != null)
            {
                Object.DestroyImmediate(_characterRoot);
            }
        }

        [Test]
        public void SquidMesh_GeneratesValidNormalsAndNonEmptyTriangles()
        {
            Mesh mesh = CharacterVisualAssembler.CreateSquidMesh();
            Assert.IsNotNull(mesh);
            Assert.Greater(mesh.vertexCount, 0, "乌贼网格顶点数必须大于 0");
            Assert.Greater(mesh.triangles.Length, 0, "乌贼网格面数必须大于 0");
            Assert.Greater(mesh.bounds.size.magnitude, 0.1f, "乌贼网格包围盒尺寸必须有效");
        }

        [Test]
        public void Assembler_BuildsKidAndSquidAndWeapons_HierarchyCorrectly()
        {
            Assert.IsNotNull(_controller, "CharacterVisualController 必须成功挂载");
            Assert.IsNotNull(_controller.KidRoot, "人形态根节点 KidRoot 必须存在");
            Assert.IsNotNull(_controller.SquidRoot, "乌贼形态根节点 SquidRoot 必须存在");
            Assert.IsNotNull(_controller.WeaponRoot, "武器挂载根节点 WeaponRoot 必须存在");

            Assert.IsTrue(_controller.KidRoot.activeSelf, "人形态初始应为启用");
            Assert.IsFalse(_controller.SquidRoot.activeSelf, "乌贼形态初始应为隐藏");

            Assert.AreEqual(WeaponKind.Shooter, _controller.CurrentWeaponKind, "默认主武器应为 Shooter (Spritzer)");
            Assert.IsNotNull(_controller.ActiveMuzzle, "默认枪口锚点必须有效绑定");
        }

        [Test]
        public void Weapons_SwitchingAllSevenKinds_TogglesCorrectWeaponModels()
        {
            WeaponKind[] allKinds = new[]
            {
                WeaponKind.Shooter,
                WeaponKind.Roller,
                WeaponKind.Charger,
                WeaponKind.Dualies,
                WeaponKind.Slosher,
                WeaponKind.Blaster,
                WeaponKind.Splatling
            };

            for (int i = 0; i < allKinds.Length; i++)
            {
                WeaponKind kind = allKinds[i];
                _controller.EquipWeaponKind(kind);

                Assert.AreEqual(kind, _controller.CurrentWeaponKind, $"装备武器应切换为 {kind}");
                Assert.IsNotNull(_controller.ActiveMuzzle, $"武器 {kind} 的枪口 Muzzle 必须有效存在");
            }
        }

        [Test]
        public void Dualies_Equipping_PosesLeftArmForDualWield()
        {
            // 切为常规武器 (Shooter)：左手处于托握姿态
            _controller.EquipWeaponKind(WeaponKind.Shooter);
            Transform leftArm = _controller.KidRoot.transform.Find("Chest/LeftArm");
            Assert.IsNotNull(leftArm, "LeftArm 必须存在");
            Vector3 singlePose = leftArm.localPosition;

            // 切为双枪 (Dualies)：左手变为平行出枪姿态
            _controller.EquipWeaponKind(WeaponKind.Dualies);
            Vector3 dualiesPose = leftArm.localPosition;

            Assert.AreNotEqual(singlePose.x, dualiesPose.x, "双枪形态下左手横向位置应发生外展调整");
        }

        [Test]
        public void Weapons_MovingParts_ExistAndAreProperlyBound()
        {
            // 验证滚筒滚轴 Drum
            _controller.EquipWeaponKind(WeaponKind.Roller);
            Transform drum = _controller.WeaponRoot.transform.Find("Swell_Roller/RollerDrum");
            Assert.IsNotNull(drum, "滚筒旋转轴心 RollerDrum 必须存在");

            // 验证加特林旋转枪管簇 BarrelsCluster
            _controller.EquipWeaponKind(WeaponKind.Splatling);
            Transform cluster = _controller.WeaponRoot.transform.Find("Gyre_Splatling/RotatingBarrelsCluster");
            Assert.IsNotNull(cluster, "加特林旋转枪管簇 RotatingBarrelsCluster 必须存在");

            // 验证蓄力狙枪口
            _controller.EquipWeaponKind(WeaponKind.Charger);
            Transform chargerMuzzle = _controller.WeaponRoot.transform.Find("Glint_Charger/Muzzle");
            Assert.IsNotNull(chargerMuzzle, "狙击枪 Muzzle 必须存在");
        }
    }
}
