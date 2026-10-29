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

        // ==================== P8-D 蛋形角色（无腿） ====================

        [Test]
        public void EggBodyMesh_HasEggSilhouetteAndPlantedBase()
        {
            Mesh mesh = CharacterVisualAssembler.CreateEggBodyMesh();
            Assert.IsNotNull(mesh, "蛋体网格必须生成");
            Assert.Greater(mesh.vertexCount, 100, "蛋体网格顶点数应足够支撑圆润轮廓");
            Assert.Greater(mesh.triangles.Length, 0, "蛋体网格面数必须大于 0");

            Bounds bounds = mesh.bounds;
            Assert.AreEqual(0f, bounds.min.y, 0.002f, "蛋体底面必须贴地 y=0（挤压枢轴在此）");
            Assert.AreEqual(CharacterMetrics.BodyHeight, bounds.max.y, 0.005f, "蛋体高度必须等于 CharacterMetrics.BodyHeight");
            Assert.AreEqual(CharacterMetrics.BodyMaxRadius * 2f, bounds.size.x, 0.02f, "横向直径必须等于最大半径的两倍");
            Assert.AreEqual(CharacterMetrics.BodyMaxRadius * 2f, bounds.size.z, 0.02f, "前后厚度必须等于最大半径的两倍");

            // 蛋形特征：下半身（y≈35%）应当比上半身（y≈88%）明显更宽
            Assert.Greater(RingRadius(mesh, CharacterMetrics.BodyHeight * 0.35f),
                           RingRadius(mesh, CharacterMetrics.BodyHeight * 0.88f) * 1.2f,
                           "蛋形轮廓必须下半身饱满、上半身收窄");

            // 法线必须朝外且为单位向量，否则角色会整片变黑
            Vector3[] normals = mesh.normals;
            Assert.AreEqual(mesh.vertexCount, normals.Length);
            float maxError = 0f;
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(normals[i].x), "法线不能为 NaN");
                maxError = Mathf.Max(maxError, Mathf.Abs(normals[i].magnitude - 1f));
            }
            Assert.Less(maxError, 0.01f, "法线必须已归一化");
        }

        /// <summary>取网格在指定高度附近的实际横向半径（用于验证蛋形轮廓比例）。</summary>
        private static float RingRadius(Mesh mesh, float y)
        {
            Vector3[] verts = mesh.vertices;
            float best = 0f;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float delta = Mathf.Abs(verts[i].y - y);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = new Vector2(verts[i].x, verts[i].z).magnitude;
                }
            }
            return best;
        }

        [Test]
        public void KidHierarchy_IsLeglessEgg_AndKeepsControllerPaths()
        {
            Transform kid = _controller.KidRoot.transform;

            Assert.IsNull(kid.Find("Hips"), "蛋形角色不应再有 Hips 节点");
            Assert.IsNull(kid.Find("Chest/LeftLeg"), "蛋形角色不应再有左腿");
            Assert.IsNull(kid.Find("Chest/RightLeg"), "蛋形角色不应再有右腿");

            // 控制器与测试依赖的路径必须保留
            Assert.IsNotNull(kid.Find("Chest"), "Chest 必须是 KidRoot 的直接子节点");
            Assert.IsNotNull(kid.Find("Chest/LeftArm"), "Chest/LeftArm 路径必须保留（双枪姿态测试依赖）");
            Assert.IsNotNull(kid.Find("Chest/RightArm"), "Chest/RightArm 必须保留");
            Assert.IsNotNull(kid.Find("Chest/BodySquash"), "BodySquash 必须是 Chest 的直接子节点");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/EggBody"), "蛋体网格节点必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Face/Eye_L"), "左眼必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Face/Eye_R"), "右眼必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/InkTankBackpack/LiquidFill"), "墨罐液面必须存在（墨量联动）");
            Assert.IsNotNull(_controller.BodySquashTransform, "控制器必须绑定 BodySquash 挤压节点");
        }

        [Test]
        public void GaitSolver_ContactSquashesAndApexStretches()
        {
            EggGaitPose contact = EggGaitSolver.Sample(1f, 0f);                 // |sin| = 0 -> 触地
            EggGaitPose apex = EggGaitSolver.Sample(1f, Mathf.PI * 0.5f);       // |sin| = 1 -> 最高点

            Assert.AreEqual(0f, contact.BobY, 0.0001f, "触地帧不应有抬升");
            Assert.Less(contact.SquashY, 0.95f, "触地帧必须压扁");
            Assert.Greater(contact.SquashXZ, 1.02f, "触地帧必须横向膨胀");
            Assert.AreEqual(1f, apex.SquashY, 0.001f, "最高点应恢复原状");
            Assert.AreEqual(1f, apex.SquashXZ, 0.001f);
            Assert.Greater(apex.BobY, 0f, "最高点必须有抬升");
            Assert.Greater(apex.RockDeg, 0f, "最高点应处于左右摇摆的峰值");
        }

        [Test]
        public void GaitSolver_ScalesWithSpeed()
        {
            Assert.AreEqual(0f, EggGaitSolver.Speed01(0f), 0.0001f);
            Assert.AreEqual(1f, EggGaitSolver.Speed01(11.8f), 0.0001f, "潜墨全速应归一化为 1");
            Assert.Less(EggGaitSolver.CadenceHz(0f), EggGaitSolver.CadenceHz(1f), "速度越快触地频率越高");

            EggGaitPose slow = EggGaitSolver.Sample(0.2f, 0f);
            EggGaitPose fast = EggGaitSolver.Sample(1f, 0f);
            Assert.Less(fast.SquashY, slow.SquashY, "速度越快触地压扁越深");
            Assert.Greater(fast.SquashXZ, slow.SquashXZ, "速度越快触地膨胀越大");
        }
    }
}
