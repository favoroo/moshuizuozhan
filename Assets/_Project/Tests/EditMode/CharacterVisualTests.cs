using System.Collections.Generic;
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
            Transform leftArm = _controller.ChestTransform != null
                ? _controller.ChestTransform.Find("LeftArm")
                : _controller.KidRoot.transform.Find("Hips/BodySquash/Spine/Chest/LeftArm");
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

        // ==================== P33 INKWAVE squidkid 真骨骼与网格 ====================

        [Test]
        public void MochiHeadMesh_HasMochiSilhouetteAndValidGeometry()
        {
            Mesh mesh = CharacterVisualAssembler.CreateMochiHeadMesh();
            Assert.IsNotNull(mesh, "馒头头网格必须生成");
            Assert.Greater(mesh.vertexCount, 100, "头部网格顶点数应足够支撑圆润雕塑轮廓");
            Assert.Greater(mesh.triangles.Length, 0, "头部网格面数必须大于 0");

            Bounds bounds = mesh.bounds;
            Assert.AreEqual(CharacterMetrics.HeadHalfWidth * 2f, bounds.size.x, 0.04f, "头宽必须符合 CharacterMetrics");
            Assert.AreEqual(CharacterMetrics.HeadHalfHeight * 2f, bounds.size.y, 0.04f, "头高必须符合 CharacterMetrics");

            Vector3[] normals = mesh.normals;
            Assert.AreEqual(mesh.vertexCount, normals.Length);
            float maxError = 0f;
            for (int i = 0; i < normals.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(normals[i].x), "法线不能为 NaN");
                maxError = Mathf.Max(maxError, Mathf.Abs(normals[i].magnitude - 1f));
            }
            Assert.Less(maxError, 0.02f, "法线必须已归一化");
        }

        [Test]
        public void KidHierarchy_HasSquidkidBones_AndKeepsControllerPaths()
        {
            Transform kid = _controller.KidRoot.transform;

            // 骨骼树：Hips → BodySquash → Spine → Chest → Neck → Head
            Transform hips = kid.Find("Hips");
            Assert.IsNotNull(hips, "squidkid 角色必须有 Hips 骨盆节点作为树根");
            Assert.AreEqual(CharacterMetrics.HipBoneY, hips.localPosition.y, 0.001f,
                "骨盆高度必须等于 CharacterMetrics.HipBoneY");

            Transform leftLeg = hips.Find("LeftLeg");
            Transform rightLeg = hips.Find("RightLeg");
            Assert.IsNotNull(leftLeg, "左腿必须挂在 Hips 下");
            Assert.IsNotNull(rightLeg, "右腿必须挂在 Hips 下");
            Assert.IsNotNull(leftLeg.Find("Knee"), "左膝枢轴必须存在");
            Assert.IsNotNull(leftLeg.Find("Knee/Ankle"), "左踝枢轴必须存在");
            Assert.IsNotNull(leftLeg.Find("Knee/Ankle/Foot"), "左脚鞋壳必须存在");
            Assert.IsNotNull(rightLeg.Find("Knee/Ankle/Foot"), "右脚鞋壳必须存在");

            // 腿不挂在 BodySquash 下（挤压只压缩上身）
            Assert.IsNull(hips.Find("BodySquash/LeftLeg"), "腿不允许挂在 BodySquash 下");
            Assert.IsNull(hips.Find("BodySquash/RightLeg"), "腿不允许挂在 BodySquash 下");

            // 鞋底贴地：踝世界高度 0.068m，鞋底应落在 y≈0
            Transform foot = leftLeg.Find("Knee/Ankle/Foot");
            float footBottom = foot.position.y - foot.lossyScale.y * 0.5f;
            Assert.Less(footBottom, 0.03f, "鞋底必须接近地面 y=0");
            Assert.Greater(footBottom, -0.05f, "鞋底不能明显插入地面");

            // 上身脊柱链层级
            Transform squash = hips.Find("BodySquash");
            Assert.IsNotNull(squash, "BodySquash 必须是 Hips 的直接子节点");
            Transform spine = squash.Find("Spine");
            Assert.IsNotNull(spine, "Spine 必须是 BodySquash 的子节点");
            Assert.AreEqual(CharacterMetrics.SpineLocalY, spine.localPosition.y, 0.001f);

            Transform chest = spine.Find("Chest");
            Assert.IsNotNull(chest, "Chest 必须是 Spine 的子节点");
            Assert.AreEqual(CharacterMetrics.ChestLocalY, chest.localPosition.y, 0.001f);

            Assert.IsNotNull(chest.Find("LeftArm"), "LeftArm 必须挂在 Chest 下");
            Assert.IsNotNull(chest.Find("RightArm"), "RightArm 必须挂在 Chest 下");

            Transform neck = chest.Find("Neck");
            Assert.IsNotNull(neck, "Neck 必须挂在 Chest 下");
            Assert.AreEqual(CharacterMetrics.NeckLocalY, neck.localPosition.y, 0.001f);

            Transform head = neck.Find("Head");
            Assert.IsNotNull(head, "Head 必须挂在 Neck 下");
            Assert.AreEqual(CharacterMetrics.HeadLocalY, head.localPosition.y, 0.001f);

            Assert.IsNotNull(head.Find("MochiHead"), "馒头头网格节点必须存在");
            Assert.IsNotNull(head.Find("Face/Eye_L"), "左眼必须存在");
            Assert.IsNotNull(head.Find("Face/Eye_R"), "右眼必须存在");
            Assert.IsNotNull(chest.Find("InkTankBackpack/LiquidFill"), "墨罐液面必须存在");
            Assert.IsNull(head.Find("InkCrest"), "InkCrest 不允许存在");
            Assert.IsNotNull(_controller.BodySquashTransform, "控制器必须绑定 BodySquash 节点");
        }

        [Test]
        public void InkTank_FillTicksBombLineGauge_ExistAndFillMeshHasMeniscus()
        {
            Transform chest = _controller.KidRoot.transform.Find("Hips/BodySquash/Spine/Chest");
            Assert.IsNotNull(chest, "Chest 必须存在");
            Transform backpack = chest.Find("InkTankBackpack");
            Assert.IsNotNull(backpack, "墨罐背包必须存在");

            // 墨量读数三件套：25/50/75% 刻度块、副武器墨量线、罐顶压力表
            Assert.IsNotNull(backpack.Find("FillTicks"), "液位刻度块容器必须存在");
            Assert.IsNotNull(backpack.Find("FillTicks/FillTick_25_L"), "25% 刻度块必须存在");
            Assert.IsNotNull(backpack.Find("FillTicks/FillTick_75_R"), "75% 刻度块必须存在");
            Assert.IsNotNull(backpack.Find("BombLine"), "副武器墨量线必须存在");
            Assert.IsNotNull(backpack.Find("TankGauge/GaugeFace"), "罐顶压力表盘必须存在");
            Assert.IsNotNull(backpack.Find("TankGauge/GaugeNeedle"), "压力表指针必须存在");

            // 液面网格：实尺弯月面旋转体（INKWAVE tankGlass fill 移植）——底部锚定原点、顶缘外翻高于满位线
            Transform fill = backpack.Find("LiquidFill");
            var filter = fill != null ? fill.GetComponent<MeshFilter>() : null;
            Assert.IsNotNull(filter, "液面必须挂程序化弯月面网格");
            Assert.AreSame(CharacterVisualAssembler.CreateTankFillMesh(), filter.sharedMesh, "液面网格必须共享静态缓存实例");
            Assert.AreEqual(0f, filter.sharedMesh.bounds.min.y, 0.002f, "液面网格底部必须锚定在原点");
            Assert.Greater(filter.sharedMesh.bounds.max.y, 0.131f, "液面网格必须带弯月面外翻（高于满位线）");
            Assert.Less(filter.sharedMesh.bounds.max.y, 0.134f, "弯月面外翻量必须受控");
            Assert.AreEqual(-0.066f, fill.localPosition.y, 0.001f, "液面底部必须与满位底（−0.066）对齐");
        }

        [Test]
        public void KidCostume_ShadowCastingRules()
        {
            Transform kid = _controller.KidRoot.transform;
            Transform hips = kid.Find("Hips");
            Transform chest = hips.Find("BodySquash/Spine/Chest");
            Transform head = chest.Find("Neck/Head");

            var headRend = head.Find("MochiHead").GetComponent<Renderer>();
            var teeRend = hips.Find("BodySquash/Spine/KidTee").GetComponent<Renderer>();
            var shortsRend = hips.Find("KidShorts").GetComponent<Renderer>();
            var bandRend = hips.Find("BodySquash/Spine/Waistband").GetComponent<Renderer>();
            var footRend = hips.Find("LeftLeg/Knee/Ankle/Foot").GetComponent<Renderer>();
            var soleRend = hips.Find("LeftLeg/Knee/Ankle/SoleMid").GetComponent<Renderer>();
            var toeRend = hips.Find("LeftLeg/Knee/Ankle/ToeCap").GetComponent<Renderer>();

            Assert.IsNotNull(headRend);
            Assert.IsNotNull(teeRend);
            Assert.IsNotNull(shortsRend);
            Assert.IsNotNull(bandRend);
            Assert.IsNotNull(footRend);
            Assert.IsNotNull(soleRend);
            Assert.IsNotNull(toeRend);

            Assert.AreEqual(ShadowCastingMode.On, headRend.shadowCastingMode, "头本体必须投影");
            Assert.AreEqual(ShadowCastingMode.On, teeRend.shadowCastingMode, "T恤大件必须投影");
            Assert.AreEqual(ShadowCastingMode.On, shortsRend.shadowCastingMode, "短裤大件必须投影");
            Assert.AreEqual(ShadowCastingMode.Off, bandRend.shadowCastingMode, "腰带窄环与T恤重合，不应重复投影");
            Assert.AreEqual(ShadowCastingMode.On, footRend.shadowCastingMode, "鞋壳必须投影");
            Assert.AreEqual(ShadowCastingMode.Off, soleRend.shadowCastingMode, "鞋底贴条不应重复投影");
            Assert.AreEqual(ShadowCastingMode.Off, toeRend.shadowCastingMode, "鞋头包边不应重复投影");
        }

        [Test]
        public void TeeNeckline_IsClosedAndWiderThanNeck()
        {
            Mesh tee = CharacterVisualAssembler.CreateTeeMesh();
            Assert.IsNotNull(tee, "T 恤网格必须生成");
            Assert.Greater(tee.bounds.max.y, CharacterMetrics.TeeNecklineY - 0.005f,
                "T 恤必须做到领口上沿高度（肩线以上要有锥形领口）");
            Assert.Greater(CharacterMetrics.TeeNecklineHalfWidth, CharacterMetrics.NeckRadius,
                "领口环必须比颈筒宽，封口环才能始终罩住脖子");

            // 开放边（只被一个三角形使用的边）：领口高度附近若存在开放边，说明顶面没封口，
            // 俯视会直接看穿空心 T 恤 —— 这正是"脖子下面是空的"的根因。
            // 按【顶点位置】而不是顶点索引统计：管体在 θ=0 处有一列重复顶点（接缝），按索引会把
            // 接缝与封口扇的环绕边误判成开放边。
            Vector3[] verts = tee.vertices;
            int[] tris = tee.triangles;
            var edgeUse = new Dictionary<EdgeKey, int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                RegisterEdge(edgeUse, verts[tris[i]], verts[tris[i + 1]]);
                RegisterEdge(edgeUse, verts[tris[i + 1]], verts[tris[i + 2]]);
                RegisterEdge(edgeUse, verts[tris[i + 2]], verts[tris[i]]);
            }

            float rimMinY = CharacterMetrics.TeeNecklineY - 0.005f;
            int openRimEdges = 0;
            foreach (var kv in edgeUse)
            {
                if (kv.Value != 1) continue;
                if (kv.Key.YA > rimMinY && kv.Key.YB > rimMinY) openRimEdges++;
            }

            Assert.AreEqual(0, openRimEdges, "T 恤领口顶面必须封闭：领口环上不允许有开放边");
        }

        [Test]
        public void NeckJunction_CollarRibRidesSpine_AndNeckIsSocketed()
        {
            Transform kid = _controller.KidRoot.transform;
            Transform spine = kid.Find("Hips/BodySquash/Spine");
            Assert.IsNotNull(spine, "Spine 必须存在");

            // 领圈必须与 T 恤同枢轴（Spine）：挂在 Neck 骨上会随头部转动漂出领口
            Transform rib = spine.Find("CollarRib");
            Assert.IsNotNull(rib, "领口螺纹条必须挂在 Spine 下");
            var ribFilter = rib.GetComponent<MeshFilter>();
            Assert.IsNotNull(ribFilter, "领口螺纹条必须有 MeshFilter");
            Assert.IsNotNull(ribFilter.sharedMesh, "领口螺纹条网格必须生成");
            Assert.Greater(ribFilter.sharedMesh.bounds.size.y, 0.005f, "领口螺纹条必须有可见高度");

            // 颈筒加粗并埋进上下两端：可见段只从领口到下巴
            Transform neckSkin = kid.Find("Hips/BodySquash/Spine/Chest/Neck/NeckSkin");
            Assert.IsNotNull(neckSkin, "颈筒必须挂在 Neck 骨下");
            Assert.AreEqual(CharacterMetrics.NeckRadius * 2f, neckSkin.localScale.x, 0.001f, "颈筒直径必须符合 NeckRadius");
            Assert.AreEqual(CharacterMetrics.NeckLength * 0.5f, neckSkin.localScale.y, 0.001f,
                "颈筒半高必须符合 NeckLength（圆柱基元高 = 2×scale.y）");
            Assert.AreEqual(CharacterMetrics.NeckSkinCenterY, neckSkin.localPosition.y, 0.001f,
                "颈筒中心必须下沉到 NeckSkinCenterY");
        }

        private static void RegisterEdge(Dictionary<EdgeKey, int> edges, Vector3 a, Vector3 b)
        {
            var key = new EdgeKey(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        /// <summary>按位置量化的无向边（0.1mm 精度），把接缝重复顶点归并成同一条边。</summary>
        private readonly struct EdgeKey : System.IEquatable<EdgeKey>
        {
            private readonly int _x0, _y0, _z0, _x1, _y1, _z1;

            public EdgeKey(Vector3 a, Vector3 b)
            {
                const float scale = 10000f;
                int ax = Mathf.RoundToInt(a.x * scale), ay = Mathf.RoundToInt(a.y * scale), az = Mathf.RoundToInt(a.z * scale);
                int bx = Mathf.RoundToInt(b.x * scale), by = Mathf.RoundToInt(b.y * scale), bz = Mathf.RoundToInt(b.z * scale);
                bool swap = ax > bx || (ax == bx && (ay > by || (ay == by && az > bz)));
                _x0 = swap ? bx : ax; _y0 = swap ? by : ay; _z0 = swap ? bz : az;
                _x1 = swap ? ax : bx; _y1 = swap ? ay : by; _z1 = swap ? az : bz;
            }

            public float YA => _y0 / 10000f;
            public float YB => _y1 / 10000f;

            public bool Equals(EdgeKey other)
                => _x0 == other._x0 && _y0 == other._y0 && _z0 == other._z0
                && _x1 == other._x1 && _y1 == other._y1 && _z1 == other._z1;

            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + _x0; h = h * 31 + _y0; h = h * 31 + _z0;
                    h = h * 31 + _x1; h = h * 31 + _y1; h = h * 31 + _z1;
                    return h;
                }
            }
        }

        [Test]
        public void KidHierarchy_HasElbowJoints_AndKeepsWeaponMountPaths()
        {
            Transform chest = _controller.ChestTransform;
            Assert.IsNotNull(chest, "Chest 必须有效");

            Transform leftArm = chest.Find("LeftArm");
            Assert.IsNotNull(leftArm, "LeftArm 必须存在");
            Transform leftElbow = leftArm.Find("Elbow");
            Assert.IsNotNull(leftElbow, "左肘枢轴必须存在");
            Assert.IsNotNull(leftElbow.Find("Cuff"), "袖口必须挂在肘枢轴下");
            Assert.IsNotNull(leftElbow.Find("Forearm"), "小臂必须挂在肘枢轴下");
            Assert.IsNotNull(leftElbow.Find("Hand"), "圆手必须挂在肘枢轴下");
            Assert.IsNotNull(leftArm.Find("Sleeve"), "上臂短袖挂在肩枢轴下");
            Assert.IsNotNull(chest.Find("RightArm/Elbow"), "右肘枢轴必须存在");

            Assert.IsNotNull(_controller.LeftElbowPivot, "控制器必须绑定左肘枢轴");
            Assert.IsNotNull(_controller.RightElbowPivot, "控制器必须绑定右肘枢轴");

            Vector3 elbowLocal = leftArm.Find("Elbow").localPosition;
            Assert.AreEqual(-CharacterMetrics.UpperArmLength, elbowLocal.y, 0.002f, "肘部纵向位置必须等于上臂长");
        }

        [Test]
        public void KidHierarchy_HasTentacleHair_BoundToHairSwayPivots()
        {
            Transform kid = _controller.KidRoot.transform;
            Transform hairRoot = kid.Find("Hips/BodySquash/Spine/Chest/Neck/Head/TentacleHair");
            Assert.IsNotNull(hairRoot, "触须垂发根节点必须存在");

            Transform pivot0 = hairRoot.Find("HairPivot_0");
            Transform pivot1 = hairRoot.Find("HairPivot_1");
            Assert.IsNotNull(pivot0, "左前发根枢轴必须存在");
            Assert.IsNotNull(pivot1, "右前发根枢轴必须存在");
            Assert.IsNotNull(pivot0.Find("Tentacle_Hair_0"), "左侧触须网格必须存在");
            Assert.IsNotNull(pivot1.Find("Tentacle_Hair_1"), "右侧触须网格必须存在");

            // 梢段枢轴与网格
            Assert.IsNotNull(pivot0.Find("HairSkirt_0"), "梢段枢轴必须存在");
            Assert.IsNotNull(pivot0.Find("HairSkirt_0/Tentacle_Hair_Skirt0"), "梢段网格必须存在");

            // 左右发根对称
            Assert.AreEqual(pivot0.localPosition.x, -pivot1.localPosition.x, 0.002f, "左右发根必须对称");
            Assert.AreEqual(pivot0.localPosition.y, pivot1.localPosition.y, 0.002f, "左右发根必须等高");

            // 控制器绑定
            Assert.IsNotNull(_controller.LeftHairPivot, "控制器必须绑定左发根枢轴");
            Assert.IsNotNull(_controller.RightHairPivot, "控制器必须绑定右发根枢轴");
            Assert.AreEqual(pivot0, _controller.LeftHairPivot);
            Assert.AreEqual(pivot1, _controller.RightHairPivot);

            Assert.IsNotNull(pivot0.Find("Tentacle_Hair_0").GetComponent<MeshRenderer>(),
                "触须发束必须有渲染器（队伍色识别信号）");
        }

        [Test]
        public void TentacleHairMesh_HasValidGeometry()
        {
            Mesh mesh1 = CharacterVisualAssembler.CreateHairSegment1Mesh();
            Assert.IsNotNull(mesh1, "触须根段网格必须生成");
            Assert.Greater(mesh1.vertexCount, 50);
            Assert.Greater(mesh1.triangles.Length, 0);

            Mesh mesh2 = CharacterVisualAssembler.CreateHairSegment2Mesh();
            Assert.IsNotNull(mesh2, "触须梢段网格必须生成");
            Assert.Greater(mesh2.vertexCount, 50);
            Assert.Greater(mesh2.triangles.Length, 0);

            Bounds b2 = mesh2.bounds;
            Assert.Greater(b2.max.z, 0.04f, "梢段尖端必须向脸侧（+Z）卷出");
        }

        [Test]
        public void KidHierarchy_HasInkwaveCostumeAndSneakers()
        {
            Transform kid = _controller.KidRoot.transform;
            Transform hips = kid.Find("Hips");
            Transform spine = hips.Find("BodySquash/Spine");
            Transform chest = spine.Find("Chest");
            Transform head = chest.Find("Neck/Head");

            Assert.IsNotNull(spine.Find("KidTee"), "盒型T恤必须存在");
            Assert.IsNotNull(hips.Find("KidShorts"), "灯笼短裤必须存在");
            Assert.IsNotNull(spine.Find("Waistband"), "队伍色腰带必须存在");
            Assert.IsNotNull(head.Find("ScalpCap"), "发帽必须存在");
            Assert.IsNotNull(head.Find("Face/Brow_L"), "左眉必须存在");
            Assert.IsNotNull(head.Find("Face/Brow_R"), "右眉必须存在");
            Assert.IsNotNull(head.Find("Ear_L"), "左耳必须存在");
            Assert.IsNotNull(head.Find("Ear_R"), "右耳必须存在");

            Transform ankle = hips.Find("LeftLeg/Knee/Ankle");
            Assert.IsNotNull(ankle, "左踝必须存在");
            Assert.IsNotNull(ankle.Find("SoleMid"), "白色鞋底中底必须存在");
            Assert.IsNotNull(ankle.Find("ToeCap"), "鞋头包边必须存在");
            Assert.IsNotNull(ankle.Find("HeelTab_L"), "队伍色后跟饰片必须存在");

            Transform foot = ankle.Find("Foot");
            Transform sole = ankle.Find("SoleMid");
            float footBottom = foot.position.y - foot.lossyScale.y * 0.5f;
            float soleBottom = sole.position.y - sole.lossyScale.y * 0.5f;
            Assert.AreEqual(footBottom, soleBottom, 0.025f,
                "鞋底与鞋壳底缘必须平齐（步态触地标定一致）");
        }

        // ==================== P33 KidGaitSolver 步态求解器 ====================

        [Test]
        public void GaitSolver_ElbowBendsForwardSwing_AndDeepensWithSpeed()
        {
            KidGaitPose leftForward = KidGaitSolver.SampleGait(1f, Mathf.PI * 1.5f);
            Assert.Greater(leftForward.LeftElbowBendDeg, leftForward.RightElbowBendDeg,
                "前摆臂的肘部必须比后摆臂更弯（划水节奏）");
            Assert.GreaterOrEqual(leftForward.LeftElbowBendDeg, KidGaitSolver.ElbowBendBase,
                "屈肘不得低于基础弯（站立持枪不读成直棍）");

            KidGaitPose rightForward = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.Greater(rightForward.RightElbowBendDeg, rightForward.LeftElbowBendDeg,
                "摆臂相位对调后肘部主从必须对调");

            KidGaitPose slow = KidGaitSolver.SampleGait(0.1f, Mathf.PI);
            KidGaitPose fast = KidGaitSolver.SampleGait(1f, Mathf.PI);
            Assert.Greater(fast.LeftElbowBendDeg, slow.LeftElbowBendDeg,
                "快跑屈肘必须比慢走更深");
        }

        [Test]
        public void GaitSolver_ContactSquashesAndApexStretches()
        {
            KidGaitPose contact = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            KidGaitPose apex = KidGaitSolver.SampleGait(1f, 0f);

            Assert.Less(contact.SquashY, 0.98f, "触地分腿帧必须压扁");
            Assert.Greater(contact.SquashXZ, 1.01f, "触地分腿帧必须横向膨胀");
            Assert.AreEqual(1f, apex.SquashY, 0.001f, "最高点应恢复原状");
            Assert.AreEqual(1f, apex.SquashXZ, 0.001f);
        }

        [Test]
        public void GaitSolver_ScalesWithSpeed()
        {
            Assert.AreEqual(0f, KidGaitSolver.Speed01(0f), 0.0001f);
            Assert.AreEqual(1f, KidGaitSolver.Speed01(11.8f), 0.0001f, "潜墨全速应归一化为 1");
            Assert.AreEqual(1f, KidGaitSolver.Speed01Run(6f), 0.0001f, "人形跑速 6m/s 归一化为 1");

            KidGaitPose slow = KidGaitSolver.SampleGait(0.2f, Mathf.PI * 0.5f);
            KidGaitPose fast = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.Less(fast.SquashY, slow.SquashY, "速度越快触地压扁越深");
            Assert.Greater(fast.SquashXZ, slow.SquashXZ, "速度越快触地膨胀越大");
        }

        [Test]
        public void GaitSolver_LegsCoordinateWithBody_PhaseLocked()
        {
            // 摆动中点（φ=0）：左腿屈膝抬脚、右腿伸直踩地
            KidGaitPose mid = KidGaitSolver.SampleGait(1f, 0f);
            Assert.Greater(mid.LeftKneeDeg, 30f, "摆动腿必须明显屈膝");
            Assert.AreEqual(0f, mid.RightKneeDeg, 0.001f, "支撑腿必须伸直踩地");

            // 落地分腿（φ=π/2）：双腿前后张开踩地、两膝伸直
            KidGaitPose plant = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.Less(plant.LeftThighDeg, -20f, "前腿在前方");
            Assert.Greater(plant.RightThighDeg, 20f, "后腿在后方");
            Assert.AreEqual(0f, plant.LeftKneeDeg, 0.001f, "落地支撑腿伸直");
            Assert.AreEqual(0f, plant.RightKneeDeg, 0.001f, "蹬地腿伸直");
        }

        [Test]
        public void GaitSolver_LegAmplitudeScalesWithSpeed()
        {
            KidGaitPose slow = KidGaitSolver.SampleGait(0.2f, Mathf.PI * 0.5f);
            KidGaitPose fast = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.Less(Mathf.Abs(slow.LeftThighDeg), Mathf.Abs(fast.LeftThighDeg), "速度越快大腿摆幅越大");

            KidGaitPose slowMid = KidGaitSolver.SampleGait(0.2f, 0f);
            KidGaitPose fastMid = KidGaitSolver.SampleGait(1f, 0f);
            Assert.Less(slowMid.LeftKneeDeg, fastMid.LeftKneeDeg, "速度越快膝弯越深");

            Assert.Greater(fast.RightThighDeg, 0f, "正值 = 腿向后");
            Assert.Greater(KidGaitSolver.AirTuckKneeDeg, 0f);
            Assert.Greater(KidGaitSolver.WallClimbTuckKneeDeg, 0f);
        }

        [Test]
        public void GaitSolver_CadenceTracksSpeedWithinStride()
        {
            var p = KidGaitParams.Default;
            for (float speed = 0.5f; speed <= 6.0f; speed += 0.5f)
            {
                float speed01 = KidGaitSolver.Speed01Run(speed);
                float ampRad = Mathf.Lerp(p.LegSwingSlowDeg, p.LegSwingFastDeg, speed01) * Mathf.Deg2Rad;
                float cadence = KidGaitSolver.CadenceFromSpeed(speed, speed01);
                Assert.GreaterOrEqual(cadence, KidGaitSolver.CadenceMin, $"{speed}m/s 步频不得低于下限");
                Assert.LessOrEqual(cadence, KidGaitSolver.CadenceMax, $"{speed}m/s 步频不得高于上限");
                if (cadence > KidGaitSolver.CadenceMin + 0.01f && cadence < KidGaitSolver.CadenceMax - 0.01f)
                {
                    Assert.AreEqual(speed, Mathf.PI * p.LegLength * ampRad * cadence, speed * 0.02f,
                        $"{speed}m/s 未触底/触顶时必须满足峰值匹配律（支撑中点脚不滑）");
                }
            }

            Assert.LessOrEqual(KidGaitSolver.CadenceFromSpeed(1f, KidGaitSolver.Speed01Run(1f)),
                KidGaitSolver.CadenceFromSpeed(6f, KidGaitSolver.Speed01Run(6f)));
            Assert.AreEqual(KidGaitSolver.CadenceMax, KidGaitSolver.CadenceFromSpeed(10f, 1f),
                "超速奔跑触顶上限");
            Assert.AreEqual(KidGaitSolver.CadenceMin, KidGaitSolver.CadenceFromSpeed(0.1f, 0f),
                "蠕步取步频下限");
        }

        [Test]
        public void GaitSolver_MidstanceFootPinnedAcrossSpeeds()
        {
            var p = KidGaitParams.Default;
            foreach (float speed in new[] { 1f, 2f, 3f, 4f, 5f })
            {
                float speed01 = KidGaitSolver.Speed01Run(speed);
                float ampRad = Mathf.Lerp(p.LegSwingSlowDeg, p.LegSwingFastDeg, speed01) * Mathf.Deg2Rad;
                float cadence = KidGaitSolver.CadenceFromGait(speed, ampRad, p.LegLength);
                Assert.Less(cadence, KidGaitSolver.CadenceMax);
                Assert.AreEqual(speed, Mathf.PI * p.LegLength * ampRad * cadence, speed * 0.02f,
                    $"{speed}m/s 支撑中点脚必须钉地（峰值匹配律）");
            }
        }

        [Test]
        public void GaitSolver_SlipRateBounded()
        {
            var p = KidGaitParams.Default;
            foreach (float speed in new[] { 0.5f, 2f, 4f, 6f })
            {
                float speed01 = KidGaitSolver.Speed01Run(speed);
                float ampRad = Mathf.Lerp(p.LegSwingSlowDeg, p.LegSwingFastDeg, speed01) * Mathf.Deg2Rad;
                float cadence = KidGaitSolver.CadenceFromSpeed(speed, speed01);
                float slipPerContact = Mathf.Max(0f, speed / cadence - 2f * p.LegLength * Mathf.Sin(ampRad));
                float slipRate = slipPerContact * cadence / speed;
                Assert.LessOrEqual(slipRate, 0.5f, $"{speed}m/s 滑移率 {slipRate:P0} 超过 50%");
            }
        }

        [Test]
        public void GaitSolver_StrafeYawAndSwingScale()
        {
            Vector3 rightStrafe = new Vector3(6f, 0f, 0f);
            Assert.AreEqual(KidGaitSolver.StrafeYawMaxDeg,
                KidGaitSolver.StrafeYawTargetDeg(rightStrafe, 6f, 1f), 0.01f, "右侧横移应半侧转至上限");
            Assert.AreEqual(-KidGaitSolver.StrafeYawMaxDeg,
                KidGaitSolver.StrafeYawTargetDeg(-rightStrafe, 6f, 1f), 0.01f, "左侧横移应为负向偏转");
            Assert.AreEqual(0f, KidGaitSolver.StrafeYawTargetDeg(new Vector3(0f, 0f, -6f), 6f, 1f), 0.01f, "后退不偏转");

            Vector3 forward = new Vector3(0f, 0f, 6f);
            Assert.AreEqual(0f, KidGaitSolver.StrafeYawTargetDeg(forward, 6f, 1f), 0.01f, "前进不偏转");
            Assert.AreEqual(0f, KidGaitSolver.StrafeYawTargetDeg(rightStrafe, 6f, 0f), 0.01f, "离地不做半侧转");
            Assert.AreEqual(0f, KidGaitSolver.StrafeYawTargetDeg(rightStrafe, 0.5f, 1f), 0.01f, "静止不做半侧转");

            Assert.AreEqual(1f, KidGaitSolver.DirectionalSwingScale(1f), 0.001f);
            Assert.AreEqual(KidGaitSolver.StrafeSwingFalloff, KidGaitSolver.DirectionalSwingScale(0f), 0.001f);
            float strafe = KidGaitSolver.DirectionalSwingScale(0f);
            float backward = KidGaitSolver.DirectionalSwingScale(-1f);
            Assert.Less(backward, strafe, "后退摆幅必须小于横移");
            Assert.Less(strafe, 1f, "横移摆幅必须小于前进");
        }

        [Test]
        public void GaitSolver_AirWeightsFollowPoseAirDecomposition()
        {
            KidAirPose launch = KidGaitSolver.SampleAir(7f, 0f, 5f);
            Assert.AreEqual(1f, launch.UpW, 0.01f, "刚起跳应为满上升权重");
            Assert.Greater(launch.LaunchW, 0.9f, "起跳瞬间腿还伸着（蹬伸）");
            Assert.AreEqual(0f, launch.ReachW, 0.01f);
            Assert.Greater(launch.RunSplitDeg, 0f, "跑步起跳应劈腿");
            Assert.AreEqual(0f, KidGaitSolver.SampleAir(7f, 0f, 1f).RunSplitDeg, "慢速起跳不做劈腿");

            Assert.Less(KidGaitSolver.SampleAir(3f, 0.25f, 5f).LaunchW, 0.05f, "蹬伸应在 0.2s 内衰减");

            KidAirPose apex = KidGaitSolver.SampleAir(0f, 0.4f, 0f);
            Assert.Greater(apex.ApexW, 0.9f, "顶点应悬停");
            Assert.Less(apex.SquashYMul, 1f, "顶点微团身压缩");

            KidAirPose fall = KidGaitSolver.SampleAir(-8f, 0.6f, 0f);
            Assert.AreEqual(0f, fall.UpW, 0.01f, "快速下落无上升权重");
            Assert.Greater(fall.ReachW, 0.5f, "下落后期应伸腿够地");
            Assert.Greater(fall.SquashYMul, 1f, "快速下落应再拉伸");

            Assert.Greater(KidGaitSolver.AirApexSoften, 0f);
            Assert.Greater(KidGaitSolver.AirFallStretch, 0f);
        }

        [Test]
        public void GaitSolver_AnimConfigDefaultsMatchSolverConstants()
        {
            var cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            try
            {
                Assert.AreEqual(KidGaitSolver.BobSlow, cfg.kidBobSlow, 0.0001f);
                Assert.AreEqual(KidGaitSolver.BobFast, cfg.kidBobFast, 0.0001f);
                Assert.AreEqual(KidGaitSolver.SquashDepth, cfg.kidSquashDepth, 0.0001f);
                Assert.AreEqual(KidGaitSolver.RockSlow, cfg.kidRockSlowDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.RockFast, cfg.kidRockFastDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.ArmSwingSlow, cfg.kidArmSwingSlowDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.LegSwingFast, cfg.kidLegSwingFastDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.KneeBendFast, cfg.kidKneeBendFastDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.CadenceMax, cfg.kidCadenceMax, 0.01f);
                Assert.AreEqual(KidGaitSolver.ToeUpMax, cfg.kidToeUpDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.ToeOffMax, cfg.kidToeOffDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.LegLengthDefault, cfg.kidLegLength, 0.0001f);
                Assert.AreEqual(KidGaitSolver.StrafeYawMaxDeg, cfg.strafeYawMaxDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.StrafeSwingFalloff, cfg.strafeSwingFalloff, 0.001f);

                Assert.AreEqual(KidGaitSolver.ElbowBendBase, cfg.kidElbowBendBaseDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.ElbowBendFast, cfg.kidElbowBendFastDeg, 0.01f);
                Assert.AreEqual(KidGaitSolver.ElbowSwingGain, cfg.kidElbowSwingGainDeg, 0.01f);
                Assert.AreEqual(0.70f, cfg.headStabilizePitchShare, 0.01f, "头部稳定补偿");

                Assert.Greater(cfg.hipsBobShare, 0f);
                Assert.Greater(cfg.airLaunchStretchImpulse, 3.2f);
                Assert.Greater(cfg.landImpulseMax, 6.0f);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void GaitSolver_P33AmplitudesScamperButGrounded()
        {
            KidGaitPose plant = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.GreaterOrEqual(plant.RightThighDeg, 38f, "快跑腿摆幅度充分");
            Assert.Less(plant.HipDropY, -0.03f, "快跑骨盆贴地下沉 ≥ 3cm");
            Assert.GreaterOrEqual(plant.HipDropY, -0.13f, "快跑骨盆贴地下沉在正常几何范围内");
        }

        [Test]
        public void GaitSolver_SampleGait_LegsAlternateSupportAndSwing()
        {
            KidGaitPose mid = KidGaitSolver.SampleGait(1f, 0f);
            Assert.Greater(mid.LeftKneeDeg, 40f, "左腿摆动中点屈膝抬脚");
            Assert.AreEqual(0f, mid.RightKneeDeg, 0.001f, "右腿支撑伸直");

            KidGaitPose plant = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f);
            Assert.Less(plant.LeftThighDeg, -25f, "落地前腿在前方");
            Assert.Greater(plant.RightThighDeg, 25f, "蹬地后腿在后方");
            Assert.AreEqual(0f, plant.LeftKneeDeg, 0.001f);
            Assert.AreEqual(0f, plant.RightKneeDeg, 0.001f);

            KidGaitPose half = KidGaitSolver.SampleGait(1f, Mathf.PI);
            Assert.AreEqual(0f, half.LeftKneeDeg, 0.001f);
            Assert.Greater(half.RightKneeDeg, 40f);
        }

        [Test]
        public void GaitSolver_SampleGait_HipDropKeepsStanceFootGrounded()
        {
            var p = KidGaitParams.Default;
            float[] phases = { 0f, 0.4f, Mathf.PI * 0.5f, 2f, Mathf.PI };
            foreach (float phase in phases)
            {
                KidGaitPose pose = KidGaitSolver.SampleGait(1f, phase, p);
                Assert.LessOrEqual(pose.HipDropY, 0.0001f);

                float stanceThighAbs = Mathf.Max(Mathf.Abs(pose.LeftThighDeg), Mathf.Abs(pose.RightThighDeg));
                float ankleY = p.LegLength + pose.HipDropY - p.LegLength * Mathf.Cos(stanceThighAbs * Mathf.Deg2Rad);
                Assert.AreEqual(0f, ankleY, 0.0005f, $"φ={phase:F2} 支撑脚踝必须保持在站立高度（贴地）");
            }
        }

        [Test]
        public void GaitSolver_Speed01RunUsesHumanRunSpeed()
        {
            Assert.AreEqual(1f, KidGaitSolver.Speed01Run(6f), 0.0001f);
            Assert.AreEqual(1f, KidGaitSolver.Speed01Run(11.8f), 0.0001f);
            Assert.Less(KidGaitSolver.Speed01(6f), 0.6f);
        }

        [Test]
        public void GaitSolver_CadenceUsesPeakMatchLaw()
        {
            float speed = 3f;
            float speed01 = KidGaitSolver.Speed01Run(speed);
            float ampRad = Mathf.Lerp(KidGaitSolver.LegSwingSlow, KidGaitSolver.LegSwingFast, speed01) * Mathf.Deg2Rad;
            Assert.AreEqual(speed / (Mathf.PI * KidGaitSolver.LegLengthDefault * ampRad),
                KidGaitSolver.CadenceFromSpeed(speed, speed01), 0.01f);

            Assert.AreEqual(KidGaitSolver.CadenceMax,
                KidGaitSolver.CadenceFromSpeed(10f, 1f), 0.01f);
            Assert.AreEqual(KidGaitSolver.CadenceMin, KidGaitSolver.CadenceFromSpeed(0.1f, 0f), 0.01f);
        }

        [Test]
        public void GaitSolver_SampleGaitUsesConfigParamsNotDeadFields()
        {
            var slow = new KidGaitParams
            {
                BobSlow = 0f, BobFast = 0f, SquashDepth = 0.1f, SquashBulge = 0.06f,
                RockSlowDeg = 3.5f, RockFastDeg = 6.5f, ArmSwingSlowDeg = 6f, ArmSwingFastDeg = 12f,
                LegSwingSlowDeg = 8f, LegSwingFastDeg = 20f, KneeBendSlowDeg = 10f, KneeBendFastDeg = 30f,
                ToeUpDeg = 5f, ToeOffDeg = 7f, LegLength = 0.433f,
                LeanSlowDeg = 2f, LeanFastDeg = 8f, HipLeanShare = 0.25f, SpineLeanShare = 0.45f, ChestLeanShare = 0.30f
            };
            KidGaitPose custom = KidGaitSolver.SampleGait(1f, Mathf.PI * 0.5f, slow);
            Assert.AreEqual(-20f, custom.LeftThighDeg, 0.01f, "摆幅必须来自参数包");
            Assert.AreEqual(-0.433f * (1f - Mathf.Cos(20f * Mathf.Deg2Rad)), custom.HipDropY, 0.001f,
                "骨盆下沉必须用参数包腿长/摆角求解");
        }

        // ------------------------------------------------------------------ P27 滚筒持握/攻击

        [Test]
        public void RollerSolver_WindupLiftsWeaponMonotonically()
        {
            // 前摇期滚筒从持握角单调抡起过肩（INKWAVE _poseFlick 蓄力段），胸口同步后仰
            var p = RollerWieldParams.Default;
            float prev = RollerWieldSolver.SampleFlick(0f, 0.22f, p).PitchDeg;
            Assert.AreEqual(p.CarryPitchDeg, prev, 0.001f, "时间线起点 = 持握角");

            for (float t = 0.02f; t <= 0.2201f; t += 0.02f)
            {
                float pitch = RollerWieldSolver.SampleFlick(t, 0.22f, p).PitchDeg;
                Assert.Less(pitch, prev - 0.5f, $"t={t:F3} 应持续抬起（俯仰更负）");
                prev = pitch;
            }

            Assert.AreEqual(p.FlickLiftPitchDeg, prev, 1.0f, "前摇结束应抡到过肩角");
            Assert.Less(RollerWieldSolver.SampleFlick(0.22f, 0.22f, p).BodyPitchDeg, -5f, "蓄力期胸口后仰");
        }

        [Test]
        public void RollerSolver_SlamPeaksThenRecoversToCarry()
        {
            var p = RollerWieldParams.Default;
            const float windup = 0.22f;

            // 砸下段末：砸到砸地角 + 挂点下压到位（与墨滴释放同步）
            RollerWieldPose slam = RollerWieldSolver.SampleFlick(windup + p.FlickWhipDuration, windup, p);
            Assert.AreEqual(p.FlickSlamPitchDeg, slam.PitchDeg, 0.5f);
            Assert.AreEqual(p.FlickSlamDropMeters, slam.DropMeters, 0.005f);

            // 随挥段保持砸地姿态
            RollerWieldPose follow = RollerWieldSolver.SampleFlick(
                windup + p.FlickWhipDuration + p.FlickFollowDuration * 0.5f, windup, p);
            Assert.AreEqual(p.FlickSlamPitchDeg, follow.PitchDeg, 0.01f, "随挥应保持砸地姿态");

            // 收势结束回持握（时间线越界钳制到持握，供状态机安全关闭无跳变）
            float total = windup + p.FlickWhipDuration + p.FlickFollowDuration + p.FlickRecoverDuration;
            RollerWieldPose tail = RollerWieldSolver.SampleFlick(total + 1f, windup, p);
            Assert.AreEqual(p.CarryPitchDeg, tail.PitchDeg, 0.001f);
            Assert.AreEqual(0f, tail.DropMeters, 0.001f);
            Assert.AreEqual(0f, tail.BodyPitchDeg, 0.001f);

            // 砸下应显著快于收势（easeIn 加速砸地，区别于匀速机械摆动）
            Assert.Less(p.FlickWhipDuration, p.FlickRecoverDuration * 0.5f);
        }

        [Test]
        public void RollerSolver_RollPoseCalibratedOnRig()
        {
            // 拖地/砸地姿态为真实骨架上的**默认视角造型**标定值（武器挂 Chest 下，胸口前倾会耦合进滚筒高度）。
            // P39 对齐 INKWAVE wRoll（手位前推 + 脊柱链前倾 ~20°）：拖地基准角抬到 40°、身体 18°、前推 14cm，
            // 闭环只剩 -8° 左右的小修正，鼓轴落在脚前 0.6m+，不再是"竖着吊在手心的拐杖"。
            // P31 起运行时按真实地面做俯仰闭环（GroundContact01），视角/坡度不再影响贴地；
            // 改动装配器（WeaponMountOffset / WeaponModelScale / Chest 层级）后仍需重新标定本组造型常量。
            var p = RollerWieldParams.Default;
            Assert.AreEqual(40f, p.RollPitchDeg, 0.01f, "拖行俯角标定值");
            Assert.AreEqual(0.02f, p.RollDropMeters, 0.001f, "拖行下压标定值");
            Assert.AreEqual(0.14f, p.RollForwardMeters, 0.001f, "拖行前推标定值");
            Assert.AreEqual(18f, p.RollBodyPitchDeg, 0.01f, "拖行身体前倾标定值");
            Assert.AreEqual(5f, p.FlickSlamPitchDeg, 0.01f, "砸地俯角标定值");
            Assert.AreEqual(0.03f, p.FlickSlamDropMeters, 0.001f, "砸地下压标定值");

            // 贴地半径取主鼓外皮（0.24×0.92/2 = 0.1104）：TreadRib 花纹略高，按花纹贴地会让主鼓体悬空。
            // 同一半径用于 ω=地速/半径 的无滑动自转。
            Assert.AreEqual(0.1104f, RollerWieldSolver.DrumRadiusMeters, 0.0005f);
            // P40 0.004 → 0.026：切线接触（4mm）在第三人称俯视下仍读作悬浮，压入 1/8 鼓径才贴地。
            Assert.AreEqual(0.026f, RollerWieldSolver.GroundContactPenetration, 0.0001f);

            RollerWieldPose roll = RollerWieldSolver.SampleRoll(p);
            Assert.Greater(roll.ForwardMeters, 0f, "拖地应把滚筒前推");
            Assert.Greater(roll.BodyPitchDeg, 0f, "拖地身体应前倾");
            Assert.Greater(roll.Sink01, 0f, "拖地重心应下沉");
            Assert.AreEqual(1f, roll.GroundContact01, 0.0001f, "拖地必须声明贴地闭环");
        }

        [Test]
        public void RollerSolver_RollBlendOverridesFlickRecover()
        {
            // 按住扳机进入推地：即使挥击还在随挥保持，wRoll=1 时拖地姿态完全接管（砸地→拖行过渡）
            var p = RollerWieldParams.Default;
            const float windup = 0.22f;
            float followT = windup + p.FlickWhipDuration + p.FlickFollowDuration * 0.5f;

            RollerWieldPose flick = RollerWieldSolver.SampleFlick(followT, windup, p);
            Assert.AreEqual(p.FlickSlamPitchDeg, flick.PitchDeg, 0.01f, "随挥段保持砸地角");
            RollerWieldPose roll = RollerWieldSolver.SampleRoll(p);

            RollerWieldPose blended = RollerWieldSolver.Blend(flick, roll, 1f);
            Assert.AreEqual(roll.PitchDeg, blended.PitchDeg, 0.001f);
            Assert.AreEqual(roll.DropMeters, blended.DropMeters, 0.001f);
            Assert.AreEqual(roll.Sink01, blended.Sink01, 0.001f);

            // 半程混合应已向拖地姿态收敛（拖地俯角 > 砸地角，两常量均被锁定）
            RollerWieldPose mid = RollerWieldSolver.Blend(flick, roll, 0.5f);
            Assert.AreEqual((flick.PitchDeg + roll.PitchDeg) * 0.5f, mid.PitchDeg, 0.001f);
            Assert.Greater(mid.PitchDeg, flick.PitchDeg, "半程混合俯仰应向拖地角收敛");
            Assert.Greater(mid.Sink01, flick.Sink01, "半程混合应已出现重心下沉");
        }

        [Test]
        public void RollerSolver_DrumSpinRollsWithGroundAndCoasts()
        {
            // 滚轴自转（INKWAVE character-weapons.js L691-699）：拖地时 ω = 地速/半径（无滑动），
            // 松开扳机停转，惯性按 exp(-decay·t) 滑行衰减
            const float speed = 4.4f;
            float spin = RollerWieldSolver.DrumSpinRadPerSec(speed, true);
            Assert.AreEqual(speed / RollerWieldSolver.DrumRadiusMeters, spin, 0.01f);

            Assert.AreEqual(0f, RollerWieldSolver.DrumSpinRadPerSec(speed, false), 0.0001f, "非拖地状态不再驱动");

            float coasted = RollerWieldSolver.DrumCoastStep(spin, 2.2f, 0.45f);
            Assert.AreEqual(spin * Mathf.Exp(-2.2f * 0.45f), coasted, 0.01f, "0.45s 后应衰减到约 37%");
        }

        [Test]
        public void RollerSolver_GroundContactWeightsFollowTimeline()
        {
            // P31 贴地闭环权重：持握 0、拖地 1；挥击只在砸下末段并入、随挥保持 1、收势线性释放。
            // 权重决定控制器何时用真实地面反解俯仰，不能整段挥击都拽向地面（会削掉抡砸弧线）。
            var p = RollerWieldParams.Default;
            const float windup = 0.22f;

            Assert.AreEqual(0f, RollerWieldSolver.SampleCarry(p).GroundContact01, 0.0001f);
            Assert.AreEqual(1f, RollerWieldSolver.SampleRoll(p).GroundContact01, 0.0001f);

            Assert.AreEqual(0f,
                RollerWieldSolver.SampleFlick(windup * 0.5f, windup, p).GroundContact01, 0.0001f,
                "前摇抡起阶段不应贴地");
            Assert.AreEqual(0f,
                RollerWieldSolver.SampleFlick(windup + p.FlickWhipDuration * 0.5f, windup, p).GroundContact01, 0.0001f,
                "砸下前半段不应提前拽向地面");
            Assert.AreEqual(1f,
                RollerWieldSolver.SampleFlick(windup + p.FlickWhipDuration, windup, p).GroundContact01, 0.0001f,
                "砸地瞬间应满权重贴地");
            Assert.AreEqual(1f,
                RollerWieldSolver.SampleFlick(
                    windup + p.FlickWhipDuration + p.FlickFollowDuration * 0.5f, windup, p).GroundContact01,
                0.0001f, "随挥保持应贴地");

            float recoverMidTime = windup + p.FlickWhipDuration + p.FlickFollowDuration
                + p.FlickRecoverDuration * 0.5f;
            float recoverMid = RollerWieldSolver.SampleFlick(recoverMidTime, windup, p).GroundContact01;
            Assert.Greater(recoverMid, 0f, "收势前半程仍应部分贴地");
            Assert.Less(recoverMid, 1f, "收势应释放贴地权重");

            RollerWieldPose blended = RollerWieldSolver.Blend(
                RollerWieldSolver.SampleCarry(p), RollerWieldSolver.SampleRoll(p), 0.5f);
            Assert.AreEqual(0.5f, blended.GroundContact01, 0.0001f, "姿态混合应同步混合贴地权重");
        }

        [Test]
        public void RollerSolver_GroundContactTargetMath()
        {
            // 目标轴心 = 地面 + 主鼓半径 - 压入量；目标 forward.y = (目标轴心 - 握把高度) / 杠杆长。
            // 控制器用数值斜率把该分量反解成俯仰增量——这里的纯函数保证目标值可单测、零分配。
            float targetAxisY = RollerWieldSolver.GroundContactTargetAxisY(0f);
            Assert.AreEqual(RollerWieldSolver.DrumRadiusMeters - RollerWieldSolver.GroundContactPenetration,
                targetAxisY, 0.0001f);
            Assert.Less(targetAxisY, RollerWieldSolver.DrumRadiusMeters,
                "正压入量必须让轴心低于主鼓半径，鼓体才真正陷进地面");

            const float gripY = 0.42f;
            const float lever = 0.4784f;
            float targetForwardY = RollerWieldSolver.GroundContactTargetForwardY(gripY, targetAxisY, lever);
            Assert.AreEqual((targetAxisY - gripY) / lever, targetForwardY, 0.0001f);
            Assert.Less(targetForwardY, 0f, "握把高于滚轴目标时前向分量应为负（滚筒压向前下方）");

            Assert.AreEqual(-1f, RollerWieldSolver.GroundContactTargetForwardY(1f, targetAxisY, lever), 0.0001f,
                "杠杆够不到地面时钳到 -1");
            Assert.AreEqual(0f, RollerWieldSolver.GroundContactTargetForwardY(gripY, targetAxisY, 0f), 0.0001f,
                "零杠杆应安全回退");
        }

        [Test]
        public void RollerSolver_AnimConfigDefaultsMatchSolverConstants()
        {
            // P27 约定：SO 新字段默认值 = Solver 常量（旧资产反序列化补默认值后行为一致），锁定两者防漂移
            var cfg = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            try
            {
                Assert.AreEqual(RollerWieldSolver.CarryPitchDeg, cfg.rollerCarryPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.CarryYawDeg, cfg.rollerCarryYawDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.CarryRollDeg, cfg.rollerCarryRollDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.FlickLiftPitchDeg, cfg.rollerFlickLiftPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.FlickSlamPitchDeg, cfg.rollerFlickSlamPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.FlickSlamDropMeters, cfg.rollerFlickSlamDrop, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.FlickWhipDuration, cfg.rollerFlickWhipDuration, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.FlickFollowDuration, cfg.rollerFlickFollowDuration, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.FlickRecoverDuration, cfg.rollerFlickRecoverDuration, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.FlickCoilBodyPitchDeg, cfg.rollerFlickCoilBodyPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.FlickWhipBodyPitchDeg, cfg.rollerFlickWhipBodyPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.SlamSquashImpulse, cfg.rollerSlamSquashImpulse, 0.001f);
                Assert.AreEqual(RollerWieldSolver.RollPitchDeg, cfg.rollerRollPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.RollDropMeters, cfg.rollerRollDrop, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.RollForwardMeters, cfg.rollerRollForward, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.RollBodyPitchDeg, cfg.rollerRollBodyPitchDeg, 0.001f);
                Assert.AreEqual(RollerWieldSolver.RollSink, cfg.rollerRollSink, 0.0001f);
                Assert.AreEqual(RollerWieldSolver.RollWeightInRate, cfg.rollerRollWeightInRate, 0.001f);
                Assert.AreEqual(RollerWieldSolver.RollWeightOutRate, cfg.rollerRollWeightOutRate, 0.001f);
                Assert.AreEqual(RollerWieldSolver.DrumFlickSpinRad, cfg.rollerDrumFlickSpin, 0.001f);
                Assert.AreEqual(RollerWieldSolver.DrumCoastDecay, cfg.rollerDrumCoastDecay, 0.001f);
                Assert.AreEqual(RollerWieldSolver.GroundContactPenetration, cfg.rollerGroundPenetration, 0.0001f);
                Assert.Greater(cfg.rollerGroundContactInRate, 0f, "贴地闭环进入速率必须为正");
                Assert.Greater(cfg.rollerGroundContactOutRate, 0f, "贴地闭环退出速率必须为正");
                Assert.Greater(cfg.rollerRollPlantSquashImpulse, 0f, "拖地入地重量感冲量必须为正（0 才能关闭）");

                Assert.Greater(cfg.rollerSlamSquashImpulse, 0f, "砸地必须有果冻压扁冲量");
                Assert.Greater(RollerWieldSolver.TotalDuration(0.22f, RollerWieldParams.Default), 0.5f,
                    "挥击全程（含收势）应超过半秒可感时长");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(cfg);
            }
        }

        // ==================== P34 每把武器持握姿态（手在握把上 + 肘部弯曲） ====================

        private static readonly WeaponKind[] AllHoldKinds =
        {
            WeaponKind.Shooter, WeaponKind.Roller, WeaponKind.Charger, WeaponKind.Dualies,
            WeaponKind.Slosher, WeaponKind.Blaster, WeaponKind.Splatling
        };

        private static string ModelNameOf(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller: return "Swell_Roller";
                case WeaponKind.Charger: return "Glint_Charger";
                case WeaponKind.Dualies: return "Twinfin_Dualies";
                case WeaponKind.Slosher: return "Tidebucket_Slosher";
                case WeaponKind.Blaster: return "Popper_Blaster";
                case WeaponKind.Splatling: return "Gyre_Splatling";
                default: return "Spritzer_Shooter";
            }
        }

        private static Transform RightGripOf(CharacterVisualController controller, WeaponKind kind)
        {
            Transform model = controller.WeaponRoot.transform.Find(ModelNameOf(kind));
            Assert.IsNotNull(model, $"武器 {kind} 模型必须存在");
            if (kind == WeaponKind.Dualies)
            {
                Transform rightPistol = model.Find("RightPistol");
                Assert.IsNotNull(rightPistol, "双枪右手枪必须存在");
                return WeaponHoldRig.FindChildByName(rightPistol, "Grip");
            }
            return WeaponHoldRig.FindChildByName(model, WeaponHoldTargets.RightGripName(kind));
        }

        [Test]
        public void WeaponHoldTargets_EveryKind_ReachableAndKeepsElbowBent()
        {
            // 手位必须在两骨长度内，同时不能贴到最大伸展：旧滚筒手位 0.322m 已接近 0.325m 极限，
            // 视觉上就是一条直棍"手竖着放"。这里锁 0.20~0.31m，保证肘部至少弯 30° 以上。
            Vector3 shoulderKid = new Vector3(
                CharacterMetrics.ShoulderLocalX,
                WeaponHoldRig.ChestChainY + CharacterMetrics.ShoulderLocalY,
                CharacterMetrics.ShoulderLocalZ);
            float maxReach = CharacterMetrics.UpperArmLength + CharacterMetrics.ForearmLength;

            for (int i = 0; i < AllHoldKinds.Length; i++)
            {
                WeaponKind kind = AllHoldKinds[i];
                Vector3 target = WeaponHoldTargets.RightHand(kind);
                float dist = Vector3.Distance(shoulderKid, target);
                Assert.Greater(dist, 0.20f, $"{kind} 手位离肩 {dist:F3}m 太近，手臂会缩成一团");
                Assert.Less(dist, maxReach - 0.015f, $"{kind} 手位离肩 {dist:F3}m 超过可弯肘范围（上限 {maxReach:F3}m）");
                Assert.Greater(target.z, 0.09f, $"{kind} 手位必须在身体前方，不能埋在躯干里");
            }
        }

        [Test]
        public void EquipWeapon_HandsLandOnGrips_ForEveryKind()
        {
            for (int i = 0; i < AllHoldKinds.Length; i++)
            {
                WeaponKind kind = AllHoldKinds[i];
                _controller.EquipWeaponKind(kind);

                Transform hand = _controller.RightElbowPivot.Find("Hand");
                Transform grip = RightGripOf(_controller, kind);
                Assert.IsNotNull(hand, "右手必须挂在右肘枢轴下");
                Assert.IsNotNull(grip, $"{kind} 必须能找到真实握把部件");
                Assert.Less(Vector3.Distance(hand.position, grip.position), 0.02f,
                    $"{kind} 右手没有落在握把上（手与握把距离 {Vector3.Distance(hand.position, grip.position):F3}m）");
            }
        }

        [Test]
        public void Roller_Equip_LeftHandGripsShaft_AndDualies_BothHandsOnPistols()
        {
            _controller.EquipWeaponKind(WeaponKind.Roller);
            Transform roller = _controller.WeaponRoot.transform.Find("Swell_Roller");
            Transform rollerLeftHand = _controller.LeftElbowPivot.Find("Hand");
            Vector3 support = roller.TransformPoint(WeaponHoldTargets.LeftSupportLocal(WeaponKind.Roller));
            Assert.Less(Vector3.Distance(rollerLeftHand.position, support), 0.02f,
                "滚筒左手必须托在杆上（双手握杆），不能垂在身侧");

            _controller.EquipWeaponKind(WeaponKind.Dualies);
            Transform twinfin = _controller.WeaponRoot.transform.Find("Twinfin_Dualies");
            Transform leftPistolGrip = WeaponHoldRig.FindChildByName(
                WeaponHoldRig.FindChildByName(twinfin, "LeftPistol"), "Grip");
            Transform dualLeftHand = _controller.LeftElbowPivot.Find("Hand");
            Assert.IsNotNull(leftPistolGrip, "双枪左手枪握把必须存在");
            Assert.Less(Vector3.Distance(dualLeftHand.position, leftPistolGrip.position), 0.02f,
                "双枪左手必须落在左手枪握把上");
        }
    }
}
