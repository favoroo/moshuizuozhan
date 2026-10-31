using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

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

        // ==================== P22-A 圆头蛋形躯干 + 短粗腿 ====================

        [Test]
        public void EggBodyMesh_HasEggSilhouetteAndPlantedBase()
        {
            Mesh mesh = CharacterVisualAssembler.CreateEggBodyMesh();
            Assert.IsNotNull(mesh, "蛋体网格必须生成");
            Assert.Greater(mesh.vertexCount, 100, "蛋体网格顶点数应足够支撑圆润轮廓");
            Assert.Greater(mesh.triangles.Length, 0, "蛋体网格面数必须大于 0");

            Bounds bounds = mesh.bounds;
            float torsoH = CharacterMetrics.TorsoHeight;
            Assert.AreEqual(0f, bounds.min.y, 0.002f, "躯干底面必须在躯干局部 y=0（Torso 节点在此）");
            Assert.AreEqual(torsoH, bounds.max.y, 0.005f, "躯干高度必须等于 CharacterMetrics.TorsoHeight");
            Assert.AreEqual(CharacterMetrics.BodyMaxRadius * 2f, bounds.size.x, 0.02f, "横向直径必须等于最大半径的两倍");
            Assert.AreEqual(CharacterMetrics.BodyMaxRadius * 2f, bounds.size.z, 0.02f, "前后厚度必须等于最大半径的两倍");

            // 下段饱满：下半身（y≈35%）应当比上段（y≈88%）明显更宽
            Assert.Greater(RingRadius(mesh, torsoH * 0.35f),
                           RingRadius(mesh, torsoH * 0.88f) * 1.2f,
                           "蛋形轮廓必须下半身饱满、上段收窄");

            // P22-A 圆钝头顶：顶端有效曲率半径须与体宽同级（旧版 ~0.18m 读成"洋葱尖"）。
            // 圆穹版在 95% / 97.5% 高度仍保有 0.40x / 0.28x 最大半径的宽度。
            float maxRing = RingRadius(mesh, torsoH * 0.38f);
            Assert.Greater(maxRing, CharacterMetrics.BodyMaxRadius * 0.97f, "最大体宽应出现在下段 ~38% 高度");
            Assert.Greater(RingRadius(mesh, torsoH * 0.95f), CharacterMetrics.BodyMaxRadius * 0.38f,
                           "顶部 95% 高度必须保持圆穹宽度（不得收成尖顶）");
            Assert.Greater(RingRadius(mesh, torsoH * 0.975f), CharacterMetrics.BodyMaxRadius * 0.27f,
                           "顶部 97.5% 高度必须保持圆穹宽度（顶端曲率半径 ≈ 体宽量级）");

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
        public void KidHierarchy_HasStubbyLegs_AndKeepsControllerPaths()
        {
            Transform kid = _controller.KidRoot.transform;

            // 双腿双脚：Hips → LeftLeg/RightLeg → Knee → Ankle → Foot（FK 三段链，P22-A）
            Transform hips = kid.Find("Chest/Hips");
            Assert.IsNotNull(hips, "蛋仔风角色必须有 Hips 髋部节点");
            Assert.AreEqual(CharacterMetrics.HipPivotY - CharacterMetrics.ChestPivotY, hips.localPosition.y, 0.001f,
                "髋枢轴必须按 CharacterMetrics 藏在躯干下段内");
            Transform leftLeg = kid.Find("Chest/Hips/LeftLeg");
            Transform rightLeg = kid.Find("Chest/Hips/RightLeg");
            Assert.IsNotNull(leftLeg, "左腿必须存在");
            Assert.IsNotNull(rightLeg, "右腿必须存在");
            Assert.IsNotNull(leftLeg.Find("Knee"), "左膝枢轴必须存在");
            Assert.IsNotNull(leftLeg.Find("Knee/Ankle"), "左踝枢轴必须存在");
            Assert.IsNotNull(leftLeg.Find("Knee/Ankle/Foot"), "左脚圆头靴必须存在");
            Assert.IsNotNull(rightLeg.Find("Knee/Ankle/Foot"), "右脚圆头靴必须存在");

            // 腿挂在 Hips（Chest 子树）而不是 BodySquash：触地挤压不得连带腿部变形、把脚压进地里
            Assert.IsNull(kid.Find("Chest/BodySquash/LeftLeg"), "腿不允许挂在 BodySquash 下");
            Assert.IsNull(kid.Find("Chest/BodySquash/RightLeg"), "腿不允许挂在 BodySquash 下");

            // 鞋底贴地：踝世界高度 = HipPivotY - Thigh - Calf = 0.05，靴底（球心 - 半径）应落在 y≈0
            Transform foot = leftLeg.Find("Knee/Ankle/Foot");
            float footBottom = foot.position.y - foot.lossyScale.y * 0.5f;
            Assert.Less(footBottom, 0.03f, "鞋底必须接近地面 y=0");
            Assert.Greater(footBottom, -0.06f, "鞋底不能明显插入地面");

            // 控制器与测试依赖的路径必须保留（躯干件现挂在新 Torso 抬升节点下）
            Assert.IsNotNull(kid.Find("Chest"), "Chest 必须是 KidRoot 的直接子节点");
            Assert.IsNotNull(kid.Find("Chest/BodySquash"), "BodySquash 必须是 Chest 的直接子节点");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Torso"), "躯干抬升节点 Torso 必须存在");
            Assert.AreEqual(CharacterMetrics.TorsoBaseY, kid.Find("Chest/BodySquash/Torso").localPosition.y, 0.001f,
                "躯干底面必须按 CharacterMetrics.TorsoBaseY 抬升");
            Assert.IsNotNull(kid.Find("Chest/LeftArm"), "Chest/LeftArm 路径必须保留（双枪姿态测试依赖）");
            Assert.IsNotNull(kid.Find("Chest/RightArm"), "Chest/RightArm 必须保留");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Torso/EggBody"), "蛋体网格节点必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Torso/Face/Eye_L"), "左眼必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Torso/Face/Eye_R"), "右眼必须存在");
            Assert.IsNotNull(kid.Find("Chest/BodySquash/Torso/InkTankBackpack/LiquidFill"), "墨罐液面必须存在（墨量联动）");
            Assert.IsNull(kid.Find("Chest/BodySquash/Torso/InkCrest"), "P22-A 已移除头顶墨焰触须（'头尖'观感来源）");
            Assert.IsNotNull(_controller.BodySquashTransform, "控制器必须绑定 BodySquash 挤压节点");
        }

        [Test]
        public void EggShells_DoNotCastRedundantShadows()
        {
            Transform squash = _controller.KidRoot.transform.Find("Chest/BodySquash");
            Assert.IsNotNull(squash, "BodySquash 节点必须存在");

            var egg = squash.Find("Torso/EggBody").GetComponent<Renderer>();
            var shorts = squash.Find("Torso/EggShorts").GetComponent<Renderer>();
            var band = squash.Find("Torso/EggWaistband").GetComponent<Renderer>();

            Assert.IsNotNull(egg, "EggBody 渲染器必须存在");
            Assert.IsNotNull(shorts, "EggShorts 渲染器必须存在");
            Assert.IsNotNull(band, "EggWaistband 渲染器必须存在");

            Assert.AreEqual(ShadowCastingMode.On, egg.shadowCastingMode,
                "蛋体主躯干必须投影");
            Assert.AreEqual(ShadowCastingMode.Off, shorts.shadowCastingMode,
                "短裤薄壳与蛋体轮廓重合，不应重复投影");
            Assert.AreEqual(ShadowCastingMode.Off, band.shadowCastingMode,
                "腰带薄壳与蛋体轮廓重合，不应重复投影");
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

        [Test]
        public void GaitSolver_LegsCoordinateWithBody_PhaseLocked()
        {
            // 触地相位（φ=0）：双腿竖直穿过躯干下方 → 屈膝抬脚 + 脚尖随动下垂，与躯干触地挤压同帧
            EggGaitPose contact = EggGaitSolver.Sample(1f, 0f);
            Assert.AreEqual(0f, contact.LegSwingDeg, 0.001f, "收步相位双腿应竖直穿过躯干下方");
            Assert.Greater(contact.KneeBendDeg, 30f, "收步相位必须明显屈膝（快跑 ≥ 30°）");
            Assert.Greater(contact.FootPitchDeg, 0f, "屈膝时脚尖随动下垂（极简 toe-roll）");

            // 分开相位（φ=π/2）：双腿前后张开踩地 → 伸直、脚掌回正，身体抬升舒展
            EggGaitPose apex = EggGaitSolver.Sample(1f, Mathf.PI * 0.5f);
            Assert.Greater(Mathf.Abs(apex.LegSwingDeg), 0f, "分开相位双腿应前后张开");
            Assert.AreEqual(0f, apex.KneeBendDeg, 0.001f, "分开相位双腿伸直");
            Assert.AreEqual(0f, apex.FootPitchDeg, 0.001f, "分开相位脚掌回正");

            // 站立（speed01=0）保留一半形变量但腿幅照常存在——慢走收着走
            EggGaitPose idle = EggGaitSolver.Sample(0f, 0f);
            Assert.Greater(idle.KneeBendDeg, 0f, "慢速触地仍需保留一半膝弯，避免小碎步僵硬");
        }

        [Test]
        public void GaitSolver_LegAmplitudeScalesWithSpeed()
        {
            EggGaitPose slow = EggGaitSolver.Sample(0.2f, Mathf.PI * 0.5f);
            EggGaitPose fast = EggGaitSolver.Sample(1f, Mathf.PI * 0.5f);
            Assert.Less(Mathf.Abs(slow.LegSwingDeg), Mathf.Abs(fast.LegSwingDeg), "速度越快大腿摆幅越大");

            EggGaitPose slowContact = EggGaitSolver.Sample(0.2f, 0f);
            EggGaitPose fastContact = EggGaitSolver.Sample(1f, 0f);
            Assert.Less(slowContact.KneeBendDeg, fastContact.KneeBendDeg, "速度越快膝弯越深");

            // 符号约定：φ=π/2 时摆幅为正（左腿向后基准），控制器对左右腿取反相实现交叉协同
            Assert.Greater(fast.LegSwingDeg, 0f, "摆幅符号约定：正 = 左腿向后");

            // 收腿常量供控制器滞空/攀墙使用，必须为正（屈膝方向）
            Assert.Greater(EggGaitSolver.AirTuckKneeDeg, 0f);
            Assert.Greater(EggGaitSolver.WallClimbTuckKneeDeg, 0f);
        }
    }
}
