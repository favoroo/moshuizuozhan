using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 精致多部件程序化角色（Squidkid）与 7 类主武器装配工厂。
    /// 基于纯程序化几何体组装潮流涂鸦少年形象（头雕、高光大眼、潮流耳机、触须发辫、连帽卫衣、运动板鞋、墨水储罐背包），
    /// 以及专属流线型乌贼网格，并为 7 种武器分别构建精细多部件模型。
    /// </summary>
    public static class CharacterVisualAssembler
    {
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessProp = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicProp = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

        public static CharacterVisualController Assemble(
            Transform visualRoot,
            Team team,
            Material teamBodyMat,
            Material darkVisorMat,
            WeaponConfigSO initialWeaponConfig)
        {
            // 1. 清理已有子节点（防止重复生成重叠）
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(visualRoot.GetChild(i).gameObject);
            }

            // 2. 准备材质调色盘
            Color teamColor = ResolveTeamColor(team);
            var palette = BuildMaterialPalette(team, teamColor, teamBodyMat, darkVisorMat);

            // 3. 构建人形态 (Kid Root)
            var kidGo = new GameObject("KidRoot");
            kidGo.transform.SetParent(visualRoot, false);

            Transform hips, chest, head, leftHair, rightHair, leftArm, rightArm, tankFill;
            BuildKidHierarchy(kidGo.transform, palette, out hips, out chest, out head,
                out leftHair, out rightHair, out leftArm, out rightArm, out tankFill);

            // 4. 构建乌贼形态 (Squid Root)
            var squidGo = new GameObject("SquidRoot");
            squidGo.transform.SetParent(visualRoot, false);
            squidGo.SetActive(false);

            Transform squidBody, squidTentacles;
            BuildSquidHierarchy(squidGo.transform, palette, out squidBody, out squidTentacles);

            // 5. 构建武器挂载根节点 (WeaponVisual)
            var weaponGo = new GameObject("WeaponVisual");
            weaponGo.transform.SetParent(visualRoot, false);

            GameObject shooterGo, rollerGo, chargerGo, dualiesGo, slosherGo, blasterGo, splatlingGo;
            Transform rollerDrum, splatlingBarrels;
            Renderer[] chargerCoils;
            Transform mShooter, mRoller, mCharger, mDualiesR, mDualiesL, mSlosher, mBlaster, mSplatling;

            BuildWeaponsHierarchy(weaponGo.transform, palette,
                out shooterGo, out rollerGo, out chargerGo, out dualiesGo, out slosherGo, out blasterGo, out splatlingGo,
                out rollerDrum, out splatlingBarrels, out chargerCoils,
                out mShooter, out mRoller, out mCharger, out mDualiesR, out mDualiesL, out mSlosher, out mBlaster, out mSplatling);

            // 5.1 武器挂到胸部骨骼下（世界位置保持不变）：奔跑前倾/呼吸时武器与双手同步运动，不会脱手
            weaponGo.transform.SetParent(chest, false);
            weaponGo.transform.localPosition = -chest.localPosition;

            // 6. 挂载并绑定 CharacterVisualController
            var controller = visualRoot.GetComponent<CharacterVisualController>();
            if (controller == null)
            {
                controller = visualRoot.gameObject.AddComponent<CharacterVisualController>();
            }

            controller.BindHierarchy(
                kidGo, squidGo, weaponGo,
                hips, chest, head, leftHair, rightHair, leftArm, rightArm, tankFill,
                squidBody, squidTentacles,
                shooterGo, rollerGo, chargerGo, dualiesGo, slosherGo, blasterGo, splatlingGo,
                rollerDrum, splatlingBarrels, chargerCoils,
                mShooter, mRoller, mCharger, mDualiesR, mDualiesL, mSlosher, mBlaster, mSplatling);

            WeaponKind startKind = initialWeaponConfig != null ? initialWeaponConfig.weaponKind : WeaponKind.Shooter;
            controller.EquipWeaponKind(startKind);

            return controller;
        }

        #region Material Palette

        private struct Palette
        {
            public Material TeamCloth;
            public Material TeamHair;
            public Material TeamLiquid;
            public Material SquidBody;
            public Material Skin;
            public Material Dark;
            public Material White;
            public Material Metal;
            public Material Glass;
        }

        private static Color ResolveTeamColor(Team team)
        {
            if (InkSurfaceController.Instance != null)
            {
                return InkSurfaceController.Instance.GetTeamColor(team);
            }
            return team == Team.TeamA ? new Color(1.0f, 0.45f, 0.08f) : new Color(0.16f, 0.42f, 1.0f);
        }

        private static Palette BuildMaterialPalette(Team team, Color teamCol, Material fallbackBodyMat, Material fallbackDarkMat)
        {
            Palette p = new Palette();

            p.TeamCloth = fallbackBodyMat != null ? fallbackBodyMat : CreateLitMat("Mat_TeamCloth", teamCol, 0.45f, 0.0f);
            p.TeamHair = CreateLitMat("Mat_TeamHair", teamCol, 0.88f, 0.08f);
            p.TeamLiquid = CreateLitMat("Mat_TeamLiquid", teamCol, 0.95f, 0.0f, teamCol * 0.4f);
            p.SquidBody = CreateLitMat("Mat_SquidBody", teamCol, 0.90f, 0.05f);

            p.Skin = CreateLitMat("Mat_KidSkin", new Color(0.97f, 0.80f, 0.68f), 0.20f, 0.0f);
            p.Dark = fallbackDarkMat != null ? fallbackDarkMat : CreateLitMat("Mat_DarkPlastic", new Color(0.12f, 0.14f, 0.18f), 0.40f, 0.1f);
            p.White = CreateLitMat("Mat_AccentsWhite", new Color(0.94f, 0.95f, 0.98f), 0.50f, 0.0f);
            p.Metal = CreateLitMat("Mat_Gunmetal", new Color(0.55f, 0.58f, 0.65f), 0.65f, 0.85f);
            p.Glass = CreateLitMat("Mat_TankGlass", new Color(0.85f, 0.92f, 1.0f, 0.45f), 0.95f, 0.1f);

            return p;
        }

        private static Material CreateLitMat(string name, Color baseCol, float smoothness, float metallic, Color? emission = null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");

#if UNITY_EDITOR
            // 场景装配发生在编辑器内：材质必须落成资产，否则保存场景后引用会丢失（渲染成洋红/空材质）。
            // 路径按「名称 + 颜色哈希」确定，多个角色/同队武器共享同一份资产。
            if (!Application.isPlaying)
            {
                const string folder = "Assets/_Project/Materials/Characters";
                if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                {
                    if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                        UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Materials");
                    UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Materials", "Characters");
                }

                string fileName = name + "_" + ColorUtility.ToHtmlStringRGBA(baseCol);
                string path = folder + "/" + fileName + ".mat";
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (asset != null)
                {
                    ApplyLitMatSettings(asset, baseCol, smoothness, metallic, emission);
                    UnityEditor.EditorUtility.SetDirty(asset);
                    return asset;
                }

                var created = new Material(sh) { name = fileName };
                ApplyLitMatSettings(created, baseCol, smoothness, metallic, emission);
                UnityEditor.AssetDatabase.CreateAsset(created, path);
                return created;
            }
#endif
            var mat = new Material(sh) { name = name };
            ApplyLitMatSettings(mat, baseCol, smoothness, metallic, emission);
            return mat;
        }

        private static void ApplyLitMatSettings(Material mat, Color baseCol, float smoothness, float metallic, Color? emission)
        {
            mat.SetColor(BaseColorProp, baseCol);
            mat.SetColor(ColorProp, baseCol);
            mat.SetFloat(SmoothnessProp, smoothness);
            mat.SetFloat(MetallicProp, metallic);

            if (baseCol.a < 0.999f)
            {
                // URP Lit 透明表面（墨罐玻璃）：否则不透明外壳会挡住内部液面
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            if (emission.HasValue && emission.Value != Color.black)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorProp, emission.Value);
            }
        }

        #endregion

        #region Primitives Helper

        private static GameObject CreatePart(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 localPos,
            Vector3 localScale,
            Quaternion localRot,
            Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRot;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            if (mat != null)
            {
                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = mat;
            }

            return go;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => CreatePart(parent, name, PrimitiveType.Cube, pos, scale, Quaternion.identity, mat);

        private static GameObject CreateSphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => CreatePart(parent, name, PrimitiveType.Sphere, pos, scale, Quaternion.identity, mat);

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => CreatePart(parent, name, PrimitiveType.Cylinder, pos, scale, Quaternion.identity, mat);

        private static GameObject CreateCapsule(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
            => CreatePart(parent, name, PrimitiveType.Capsule, pos, scale, Quaternion.identity, mat);

        #endregion

        #region Kid Form Assembly

        private static void BuildKidHierarchy(
            Transform kidRoot,
            Palette p,
            out Transform hips,
            out Transform chest,
            out Transform head,
            out Transform leftHair,
            out Transform rightHair,
            out Transform leftArm,
            out Transform rightArm,
            out Transform tankFill)
        {
            // 1. 臀部与双腿板鞋
            var hipsGo = new GameObject("Hips");
            hipsGo.transform.SetParent(kidRoot, false);
            hipsGo.transform.localPosition = new Vector3(0f, 0.58f, 0f);
            hips = hipsGo.transform;

            CreateCapsule(hips, "Pelvis", Vector3.zero, new Vector3(0.32f, 0.16f, 0.22f), p.Dark);

            // 左右腿与潮流板鞋
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "Left" : "Right";
                float x = s * 0.15f;

                var legGo = new GameObject(side + "Leg");
                legGo.transform.SetParent(hips, false);
                legGo.transform.localPosition = new Vector3(x, 0f, 0f);

                // 大腿与小腿
                CreateCapsule(legGo.transform, "Thigh", new Vector3(0f, -0.16f, 0f), new Vector3(0.13f, 0.16f, 0.13f), p.Dark);
                CreateCapsule(legGo.transform, "Calf", new Vector3(0f, -0.36f, 0.02f), new Vector3(0.11f, 0.18f, 0.11f), p.Skin);

                // 运动滑板鞋（厚底、深色鞋帮、白色大鞋头）
                var footGo = new GameObject(side + "Sneaker");
                footGo.transform.SetParent(legGo.transform, false);
                footGo.transform.localPosition = new Vector3(0f, -0.52f, 0.02f);

                CreateBox(footGo.transform, "Sole", new Vector3(0f, 0.025f, 0f), new Vector3(0.13f, 0.05f, 0.26f), p.White);
                CreateBox(footGo.transform, "Upper", new Vector3(0f, 0.075f, -0.01f), new Vector3(0.12f, 0.07f, 0.23f), p.Dark);
                CreateSphere(footGo.transform, "ToeCap", new Vector3(0f, 0.05f, 0.09f), new Vector3(0.12f, 0.06f, 0.09f), p.White);
            }

            // 2. 脊柱与连帽卫衣躯干 (Spine / Chest)
            var chestGo = new GameObject("Chest");
            chestGo.transform.SetParent(kidRoot, false);
            chestGo.transform.localPosition = new Vector3(0f, 0.92f, 0f);
            chest = chestGo.transform;

            // 卫衣主躯干
            CreateBox(chest, "HoodieTorso", Vector3.zero, new Vector3(0.36f, 0.36f, 0.26f), p.TeamCloth);
            CreateBox(chest, "HoodiePocket", new Vector3(0f, -0.07f, 0.135f), new Vector3(0.24f, 0.11f, 0.04f), p.Dark);
            CreateCylinder(chest, "Collar", new Vector3(0f, 0.18f, 0f), new Vector3(0.16f, 0.04f, 0.16f), p.Dark);

            // 3. 后背墨水储罐背包 (Ink Tank Backpack)
            var backpackGo = new GameObject("InkTankBackpack");
            backpackGo.transform.SetParent(chest, false);
            backpackGo.transform.localPosition = new Vector3(0f, 0.02f, -0.22f);

            CreateBox(backpackGo.transform, "MountPlate", new Vector3(0f, 0f, 0.04f), new Vector3(0.18f, 0.32f, 0.04f), p.Dark);
            CreateCylinder(backpackGo.transform, "GlassChamber", Vector3.zero, new Vector3(0.18f, 0.17f, 0.18f), p.Glass);

            var fillGo = CreateCylinder(backpackGo.transform, "LiquidFill", Vector3.zero, new Vector3(0.15f, 0.155f, 0.15f), p.TeamLiquid);
            tankFill = fillGo.transform;

            CreateCylinder(backpackGo.transform, "TopCap", new Vector3(0f, 0.18f, 0f), new Vector3(0.20f, 0.025f, 0.20f), p.Metal);
            CreateCylinder(backpackGo.transform, "ValveKnob", new Vector3(0f, 0.22f, 0f), new Vector3(0.06f, 0.03f, 0.06f), p.Dark);
            CreateCylinder(backpackGo.transform, "BottomCap", new Vector3(0f, -0.18f, 0f), new Vector3(0.20f, 0.025f, 0.20f), p.Metal);

            // 4. 头部、脸部面罩、大眼、耳机与触须发辫
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(chest, false);
            headGo.transform.localPosition = new Vector3(0f, 0.36f, 0f);
            head = headGo.transform;

            CreateSphere(head, "HeadMesh", Vector3.zero, new Vector3(0.36f, 0.36f, 0.36f), p.Skin);
            CreateBox(head, "EyeMask", new Vector3(0f, 0.02f, 0.165f), new Vector3(0.28f, 0.09f, 0.04f), p.Dark);

            // 高光大眼 (Left & Right)
            for (int s = -1; s <= 1; s += 2)
            {
                float ex = s * 0.075f;
                var eyeGo = CreateSphere(head, s < 0 ? "Eye_L" : "Eye_R", new Vector3(ex, 0.025f, 0.18f), new Vector3(0.075f, 0.075f, 0.025f), p.White);
                CreateSphere(eyeGo.transform, "Pupil", new Vector3(0f, 0f, 0.45f), new Vector3(0.6f, 0.6f, 0.5f), p.TeamHair);
                CreateSphere(eyeGo.transform, "Glint", new Vector3(0.2f, 0.25f, 0.7f), new Vector3(0.2f, 0.2f, 0.2f), p.White);

                // 精灵尖耳
                CreatePart(head, s < 0 ? "Ear_L" : "Ear_R", PrimitiveType.Cube,
                    new Vector3(s * 0.20f, -0.01f, -0.02f),
                    new Vector3(0.06f, 0.04f, 0.10f),
                    Quaternion.Euler(15f, s * -30f, s * -20f),
                    p.Skin);
            }

            // 潮流 DJ 耳机 (Headphones)
            var hpLeft = CreateCylinder(head, "EarCup_L", new Vector3(-0.20f, 0.02f, 0f), new Vector3(0.13f, 0.035f, 0.13f), p.Dark);
            hpLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            CreateCylinder(hpLeft.transform, "Ring", new Vector3(0f, 0.9f, 0f), new Vector3(0.85f, 0.2f, 0.85f), p.Metal);

            var hpRight = CreateCylinder(head, "EarCup_R", new Vector3(0.20f, 0.02f, 0f), new Vector3(0.13f, 0.035f, 0.13f), p.Dark);
            hpRight.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            CreateCylinder(hpRight.transform, "Ring", new Vector3(0f, 0.9f, 0f), new Vector3(0.85f, 0.2f, 0.85f), p.Metal);

            var headband = CreateBox(head, "Headband", new Vector3(0f, 0.19f, 0f), new Vector3(0.38f, 0.03f, 0.06f), p.Dark);

            // 触须发辫 (Tentacle Hair Braids)
            var lHairGo = new GameObject("LeftHairPivot");
            lHairGo.transform.SetParent(head, false);
            lHairGo.transform.localPosition = new Vector3(-0.16f, 0.10f, 0.06f);
            leftHair = lHairGo.transform;
            BuildTentacleHairBraid(leftHair, p, -1);

            var rHairGo = new GameObject("RightHairPivot");
            rHairGo.transform.SetParent(head, false);
            rHairGo.transform.localPosition = new Vector3(0.16f, 0.10f, 0.06f);
            rightHair = rHairGo.transform;
            BuildTentacleHairBraid(rightHair, p, 1);

            // 5. 双臂与手部 (Arms)
            var rArmGo = new GameObject("RightArm");
            rArmGo.transform.SetParent(chest, false);
            rArmGo.transform.localPosition = new Vector3(0.24f, 0.08f, 0.06f);
            rightArm = rArmGo.transform;

            CreateCapsule(rightArm, "UpperArm", new Vector3(0f, -0.10f, 0.04f), new Vector3(0.10f, 0.14f, 0.10f), p.TeamCloth);
            CreateCapsule(rightArm, "Forearm", new Vector3(0f, -0.22f, 0.14f), new Vector3(0.09f, 0.14f, 0.09f), p.Skin);
            CreateSphere(rightArm, "HandR", new Vector3(0f, -0.25f, 0.24f), new Vector3(0.11f, 0.10f, 0.12f), p.Dark);

            var lArmGo = new GameObject("LeftArm");
            lArmGo.transform.SetParent(chest, false);
            lArmGo.transform.localPosition = new Vector3(-0.24f, 0.08f, 0.06f);
            leftArm = lArmGo.transform;

            CreateCapsule(leftArm, "UpperArm", new Vector3(0f, -0.08f, 0.06f), new Vector3(0.10f, 0.14f, 0.10f), p.TeamCloth);
            CreateCapsule(leftArm, "Forearm", new Vector3(0.06f, -0.18f, 0.18f), new Vector3(0.09f, 0.14f, 0.09f), p.Skin);
            CreateSphere(leftArm, "HandL", new Vector3(0.12f, -0.20f, 0.28f), new Vector3(0.11f, 0.10f, 0.12f), p.Dark);
        }

        private static void BuildTentacleHairBraid(Transform root, Palette p, int side)
        {
            // 3 段顺滑下垂并微向前弯曲的触须发辫
            Vector3 seg1Pos = new Vector3(0f, -0.12f, 0.02f);
            var s1 = CreateCapsule(root, "HairSeg1", seg1Pos, new Vector3(0.11f, 0.15f, 0.11f), p.TeamHair);

            Vector3 seg2Pos = new Vector3(0f, -0.26f, 0.05f);
            var s2 = CreateCapsule(root, "HairSeg2", seg2Pos, new Vector3(0.10f, 0.14f, 0.10f), p.TeamHair);

            Vector3 seg3Pos = new Vector3(0f, -0.38f, 0.10f);
            var s3 = CreateCapsule(root, "HairTip", seg3Pos, new Vector3(0.08f, 0.12f, 0.08f), p.TeamHair);

            // 触须内侧白/浅色吸盘小点
            for (int i = 0; i < 3; i++)
            {
                float y = -0.10f - i * 0.12f;
                CreatePart(root, "SuctionCup" + i, PrimitiveType.Sphere,
                    new Vector3(side * -0.035f, y, 0.05f + i * 0.025f),
                    new Vector3(0.04f, 0.04f, 0.015f),
                    Quaternion.identity, p.White);
            }
        }

        #endregion

        #region Squid Form Assembly

        private static void BuildSquidHierarchy(
            Transform squidRoot,
            Palette p,
            out Transform squidBody,
            out Transform squidTentacles)
        {
            var bodyGo = new GameObject("SquidBody");
            bodyGo.transform.SetParent(squidRoot, false);
            bodyGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            squidBody = bodyGo.transform;

            // 1. 程序化流线型扁平乌贼网格 (Mantle & Wings)
            var mf = bodyGo.AddComponent<MeshFilter>();
            var mr = bodyGo.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateSquidMesh();
            mr.sharedMaterial = p.SquidBody;

            // 2. 乌贼大眼 (两侧突出的前向大眼)
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.14f;
                var eyeBase = CreateSphere(bodyGo.transform, s < 0 ? "Eye_L" : "Eye_R",
                    new Vector3(x, 0.05f, 0.06f),
                    new Vector3(0.09f, 0.07f, 0.09f),
                    p.Dark);

                CreateSphere(eyeBase.transform, "Pupil",
                    new Vector3(0f, 0.2f, 0.4f),
                    new Vector3(0.6f, 0.6f, 0.5f),
                    p.White);
            }

            // 3. 拖尾游动触须 (4 条流线游动触须)
            var tentRootGo = new GameObject("TrailingTentacles");
            tentRootGo.transform.SetParent(bodyGo.transform, false);
            tentRootGo.transform.localPosition = new Vector3(0f, 0f, -0.22f);
            squidTentacles = tentRootGo.transform;

            CreateCapsule(squidTentacles, "Tentacle_OutL", new Vector3(-0.14f, 0.01f, -0.16f), new Vector3(0.05f, 0.18f, 0.05f), p.SquidBody).transform.localRotation = Quaternion.Euler(85f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_MidL", new Vector3(-0.05f, 0.01f, -0.20f), new Vector3(0.045f, 0.22f, 0.045f), p.SquidBody).transform.localRotation = Quaternion.Euler(88f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_MidR", new Vector3(0.05f, 0.01f, -0.20f), new Vector3(0.045f, 0.22f, 0.045f), p.SquidBody).transform.localRotation = Quaternion.Euler(88f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_OutR", new Vector3(0.14f, 0.01f, -0.16f), new Vector3(0.05f, 0.18f, 0.05f), p.SquidBody).transform.localRotation = Quaternion.Euler(85f, 0f, 0f);
        }

        /// <summary>
        /// 生成平滑流线型三角箭囊与水平三角游翼专属网格
        /// </summary>
        public static Mesh CreateSquidMesh()
        {
            var mesh = new Mesh { name = "SquidMantleMesh" };

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            const int ringCount = 14;
            const int radialSegs = 16;

            for (int r = 0; r <= ringCount; r++)
            {
                float u = (float)r / ringCount; // 0 (前鼻尖) ~ 1 (尾部根)
                float z = Mathf.Lerp(0.38f, -0.24f, u);

                // 宽度轮廓：鼻尖尖锐 -> 中段横向扩张成三角游水翼 -> 尾部收束
                float baseR = Mathf.Sin(u * Mathf.PI * 0.85f) * 0.20f + 0.01f;
                float finFlare = Mathf.Clamp01(Mathf.Sin((u - 0.2f) * Mathf.PI * 1.5f)) * 0.16f;
                float rx = baseR + finFlare;
                float ry = baseR * 0.38f; // 扁平压低以紧贴水面滑行

                for (int s = 0; s < radialSegs; s++)
                {
                    float angle = (float)s / radialSegs * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    float vx = cos * rx;
                    float vy = sin * ry;
                    // 底面更加平滑压平
                    if (vy < 0f) vy *= 0.7f;

                    verts.Add(new Vector3(vx, vy, z));
                    normals.Add(new Vector3(cos, sin * 1.8f, 0f).normalized);
                    uvs.Add(new Vector2((float)s / radialSegs, u));
                }
            }

            // 缝合圆环面三角形
            for (int r = 0; r < ringCount; r++)
            {
                int r0 = r * radialSegs;
                int r1 = (r + 1) * radialSegs;
                for (int s = 0; s < radialSegs; s++)
                {
                    int next = (s + 1) % radialSegs;
                    tris.Add(r0 + s);
                    tris.Add(r1 + s);
                    tris.Add(r0 + next);

                    tris.Add(r0 + next);
                    tris.Add(r1 + s);
                    tris.Add(r1 + next);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        #endregion

        #region 7 Weapon Procedural Models

        private static void BuildWeaponsHierarchy(
            Transform weaponRoot,
            Palette p,
            out GameObject shooter,
            out GameObject roller,
            out GameObject charger,
            out GameObject dualies,
            out GameObject slosher,
            out GameObject blaster,
            out GameObject splatling,
            out Transform rollerDrum,
            out Transform splatlingBarrels,
            out Renderer[] chargerCoils,
            out Transform mShooter,
            out Transform mRoller,
            out Transform mCharger,
            out Transform mDualiesR,
            out Transform mDualiesL,
            out Transform mSlosher,
            out Transform mBlaster,
            out Transform mSplatling)
        {
            // 1. Spritzer (Shooter 喷漆步枪)
            shooter = new GameObject("Spritzer_Shooter");
            shooter.transform.SetParent(weaponRoot, false);
            BuildShooterModel(shooter.transform, p, out mShooter);

            // 2. Swell (Roller 宽屏滚筒架)
            roller = new GameObject("Swell_Roller");
            roller.transform.SetParent(weaponRoot, false);
            BuildRollerModel(roller.transform, p, out rollerDrum, out mRoller);

            // 3. Glint (Charger 长导轨蓄力狙)
            charger = new GameObject("Glint_Charger");
            charger.transform.SetParent(weaponRoot, false);
            BuildChargerModel(charger.transform, p, out chargerCoils, out mCharger);

            // 4. Twinfin (Dualies 双枪)
            dualies = new GameObject("Twinfin_Dualies");
            dualies.transform.SetParent(weaponRoot, false);
            BuildDualiesModel(dualies.transform, p, out mDualiesR, out mDualiesL);

            // 5. Tidebucket (Slosher 泼桶)
            slosher = new GameObject("Tidebucket_Slosher");
            slosher.transform.SetParent(weaponRoot, false);
            BuildSlosherModel(slosher.transform, p, out mSlosher);

            // 6. Popper (Blaster 爆破枪)
            blaster = new GameObject("Popper_Blaster");
            blaster.transform.SetParent(weaponRoot, false);
            BuildBlasterModel(blaster.transform, p, out mBlaster);

            // 7. Gyre (Splatling 加特林)
            splatling = new GameObject("Gyre_Splatling");
            splatling.transform.SetParent(weaponRoot, false);
            BuildSplatlingModel(splatling.transform, p, out splatlingBarrels, out mSplatling);
        }

        // 1. Spritzer 喷漆步枪
        private static void BuildShooterModel(Transform root, Palette p, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0.24f, 0.80f, 0.32f);
            root.localPosition = offset;

            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.02f), new Vector3(0.08f, 0.10f, 0.28f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.06f, -0.06f), new Vector3(0.06f, 0.14f, 0.08f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(root, "InkSpine", new Vector3(0f, 0.095f, 0.02f), new Vector3(0.04f, 0.02f, 0.24f), p.TeamHair);

            // 顶部加压墨水罐
            CreateCylinder(root, "InkCanister", new Vector3(0f, 0.14f, -0.02f), new Vector3(0.09f, 0.12f, 0.09f), p.TeamLiquid).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 散热套筒与喷嘴
            CreateCylinder(root, "Barrel", new Vector3(0f, 0.04f, 0.22f), new Vector3(0.055f, 0.14f, 0.055f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "NozzleTip", new Vector3(0f, 0.04f, 0.34f), new Vector3(0.075f, 0.035f, 0.075f), p.TeamHair).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.04f, 0.40f);
            muzzle = muz.transform;
        }

        // 2. Swell 滚筒
        private static void BuildRollerModel(Transform root, Palette p, out Transform drum, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0f, 0.72f, 0.30f);
            root.localPosition = offset;

            // 居中操作长杆
            CreateCylinder(root, "Shaft", new Vector3(0f, 0f, 0.20f), new Vector3(0.045f, 0.40f, 0.045f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GripRubberRear", new Vector3(0f, 0f, -0.10f), new Vector3(0.06f, 0.12f, 0.06f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GripRubberFront", new Vector3(0f, 0f, 0.18f), new Vector3(0.06f, 0.12f, 0.06f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 叉架主体与墨水槽
            CreateBox(root, "YokeHub", new Vector3(0f, 0f, 0.52f), new Vector3(0.14f, 0.10f, 0.10f), p.Dark);
            CreateBox(root, "InkReservoir", new Vector3(0f, 0.08f, 0.52f), new Vector3(0.18f, 0.07f, 0.12f), p.TeamLiquid);

            // 两侧外伸钢臂
            for (int s = -1; s <= 1; s += 2)
            {
                CreateBox(root, s < 0 ? "Arm_L" : "Arm_R",
                    new Vector3(s * 0.35f, 0f, 0.52f),
                    new Vector3(0.58f, 0.06f, 0.06f),
                    p.Metal);

                CreateCylinder(root, s < 0 ? "Bearing_L" : "Bearing_R",
                    new Vector3(s * 0.65f, 0f, 0.52f),
                    new Vector3(0.16f, 0.04f, 0.16f),
                    p.Dark).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // 旋转滚筒
            var drumGo = new GameObject("RollerDrum");
            drumGo.transform.SetParent(root, false);
            drumGo.transform.localPosition = new Vector3(0f, 0f, 0.52f);
            drum = drumGo.transform;

            var drumCyl = CreateCylinder(drum, "DrumMesh", Vector3.zero, new Vector3(0.24f, 0.60f, 0.24f), p.TeamLiquid);
            drumCyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // 滚筒表面防滑凸起墨道纹理
            for (int k = -2; k <= 2; k++)
            {
                var rib = CreateCylinder(drum, "TreadRib" + k, new Vector3(k * 0.22f, 0f, 0f), new Vector3(0.25f, 0.025f, 0.25f), p.Dark);
                rib.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0f, 0.66f);
            muzzle = muz.transform;
        }

        // 3. Glint 狙击枪
        private static void BuildChargerModel(Transform root, Palette p, out Renderer[] coils, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0.24f, 0.80f, 0.30f);
            root.localPosition = offset;

            // 枪托与镂空后托
            CreateBox(root, "Stock", new Vector3(0f, 0.02f, -0.22f), new Vector3(0.06f, 0.10f, 0.22f), p.Dark);
            CreateBox(root, "ButtPad", new Vector3(0f, 0.02f, -0.34f), new Vector3(0.07f, 0.14f, 0.03f), p.Dark);

            // 机匣与握把
            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.04f), new Vector3(0.075f, 0.10f, 0.32f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.06f, -0.04f), new Vector3(0.055f, 0.14f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            // 光学瞄准镜
            CreateCylinder(root, "Scope", new Vector3(0f, 0.13f, 0.04f), new Vector3(0.055f, 0.18f, 0.055f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "ScopeLens", new Vector3(0f, 0.13f, 0.20f), new Vector3(0.065f, 0.02f, 0.065f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 超长精密导轨管
            CreateCylinder(root, "LongBarrel", new Vector3(0f, 0.04f, 0.45f), new Vector3(0.038f, 0.42f, 0.038f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "MuzzleBrake", new Vector3(0f, 0.04f, 0.86f), new Vector3(0.052f, 0.05f, 0.052f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 4 组环形蓄力发光线圈 (Charge Coils)
            List<Renderer> coilRends = new List<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                float z = 0.30f + i * 0.14f;
                var c = CreateCylinder(root, "Coil_" + i, new Vector3(0f, 0.04f, z), new Vector3(0.065f, 0.025f, 0.065f), p.TeamLiquid);
                c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                coilRends.Add(c.GetComponent<Renderer>());
            }
            coils = coilRends.ToArray();

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.04f, 0.92f);
            muzzle = muz.transform;
        }

        // 4. Twinfin 双枪
        private static void BuildDualiesModel(Transform root, Palette p, out Transform muzzleR, out Transform muzzleL)
        {
            root.localPosition = Vector3.zero;

            // 右手手枪
            var rightPistol = new GameObject("RightPistol");
            rightPistol.transform.SetParent(root, false);
            rightPistol.transform.localPosition = new Vector3(0.24f, 0.82f, 0.30f);
            BuildSinglePistol(rightPistol.transform, p, out muzzleR);

            // 左手手枪
            var leftPistol = new GameObject("LeftPistol");
            leftPistol.transform.SetParent(root, false);
            leftPistol.transform.localPosition = new Vector3(-0.24f, 0.82f, 0.30f);
            BuildSinglePistol(leftPistol.transform, p, out muzzleL);
        }

        private static void BuildSinglePistol(Transform pistolRoot, Palette p, out Transform muzzle)
        {
            CreateBox(pistolRoot, "Frame", new Vector3(0f, 0.02f, 0.02f), new Vector3(0.065f, 0.07f, 0.22f), p.Dark);
            CreateBox(pistolRoot, "Grip", new Vector3(0f, -0.06f, -0.04f), new Vector3(0.055f, 0.12f, 0.065f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(pistolRoot, "Slide", new Vector3(0f, 0.07f, 0.02f), new Vector3(0.065f, 0.045f, 0.23f), p.White);
            CreateBox(pistolRoot, "DorsalFin", new Vector3(0f, 0.10f, 0.04f), new Vector3(0.025f, 0.02f, 0.10f), p.TeamHair);

            // 下挂圆柱墨囊
            CreateCylinder(pistolRoot, "InkCapsule", new Vector3(0f, -0.02f, 0.07f), new Vector3(0.05f, 0.07f, 0.05f), p.TeamLiquid).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(pistolRoot, "Nozzle", new Vector3(0f, 0.04f, 0.14f), new Vector3(0.045f, 0.04f, 0.045f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(pistolRoot, false);
            muz.transform.localPosition = new Vector3(0f, 0.04f, 0.18f);
            muzzle = muz.transform;
        }

        // 5. Tidebucket 泼桶
        private static void BuildSlosherModel(Transform root, Palette p, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0.12f, 0.76f, 0.32f);
            root.localPosition = offset;

            // 梯形水桶桶身
            CreateCylinder(root, "BucketPail", new Vector3(0f, 0.05f, 0.10f), new Vector3(0.34f, 0.15f, 0.34f), p.White);
            CreateCylinder(root, "ReinforceHoopTop", new Vector3(0f, 0.16f, 0.10f), new Vector3(0.36f, 0.025f, 0.36f), p.Dark);
            CreateCylinder(root, "ReinforceHoopBottom", new Vector3(0f, -0.06f, 0.10f), new Vector3(0.30f, 0.025f, 0.30f), p.Dark);

            // 桶内墨水液面
            CreateCylinder(root, "InkLiquidSurface", new Vector3(0f, 0.12f, 0.10f), new Vector3(0.31f, 0.03f, 0.31f), p.TeamLiquid);

            // 突出倾倒嘴 (Pouring Lip)
            CreateBox(root, "PourLip", new Vector3(0f, 0.16f, 0.28f), new Vector3(0.18f, 0.04f, 0.10f), p.White).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            // 后置水罐提手 (Pitcher Handle)
            CreateBox(root, "HandleRear", new Vector3(0f, 0.08f, -0.10f), new Vector3(0.06f, 0.22f, 0.06f), p.Dark);
            CreateBox(root, "HandleLever", new Vector3(0f, 0.20f, -0.06f), new Vector3(0.05f, 0.03f, 0.08f), p.Metal);

            // 底部横向副把手
            CreateBox(root, "CarryBar", new Vector3(0f, -0.12f, 0.08f), new Vector3(0.26f, 0.045f, 0.045f), p.Dark);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.16f, 0.34f);
            muzzle = muz.transform;
        }

        // 6. Popper 爆破枪
        private static void BuildBlasterModel(Transform root, Palette p, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0.24f, 0.80f, 0.32f);
            root.localPosition = offset;

            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.02f), new Vector3(0.12f, 0.12f, 0.28f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.07f, -0.06f), new Vector3(0.06f, 0.15f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            // 球形高压墨水球置于外置保护笼内
            CreateSphere(root, "PressureBulb", new Vector3(0f, 0.06f, 0.08f), new Vector3(0.18f, 0.18f, 0.18f), p.TeamLiquid);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f;
                var rib = CreateBox(root, "BulbRib_" + k, new Vector3(0f, 0.06f, 0.08f), new Vector3(0.20f, 0.025f, 0.19f), p.White);
                rib.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            }

            // 后置圆形高压气压表盘 (Pressure Gauge)
            CreateCylinder(root, "PressureGauge", new Vector3(0f, 0.08f, -0.12f), new Vector3(0.08f, 0.02f, 0.08f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GaugeDial", new Vector3(0f, 0.08f, -0.13f), new Vector3(0.065f, 0.015f, 0.065f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateBox(root, "GaugeNeedle", new Vector3(0f, 0.09f, -0.14f), new Vector3(0.01f, 0.04f, 0.005f), p.Dark);

            // 巨大喇叭口散弹枪管 (Flared Bell Muzzle)
            CreateCylinder(root, "HornBase", new Vector3(0f, 0.06f, 0.24f), new Vector3(0.11f, 0.10f, 0.11f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "BellMuzzle", new Vector3(0f, 0.06f, 0.36f), new Vector3(0.22f, 0.08f, 0.22f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "BellRim", new Vector3(0f, 0.06f, 0.42f), new Vector3(0.24f, 0.025f, 0.24f), p.TeamHair).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 下挂泵动滑块 (Pump Foregrip)
            CreateCylinder(root, "PumpSlide", new Vector3(0f, -0.04f, 0.18f), new Vector3(0.07f, 0.12f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.06f, 0.45f);
            muzzle = muz.transform;
        }

        // 7. Gyre 加特林机枪
        private static void BuildSplatlingModel(Transform root, Palette p, out Transform barrelsCluster, out Transform muzzle)
        {
            Vector3 offset = new Vector3(0.24f, 0.78f, 0.28f);
            root.localPosition = offset;

            // 机电外壳与握把
            CreateBox(root, "MotorHousing", new Vector3(0f, 0.04f, 0.06f), new Vector3(0.16f, 0.16f, 0.28f), p.White);
            CreateBox(root, "RearGrip", new Vector3(0f, -0.06f, -0.06f), new Vector3(0.06f, 0.14f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(root, "ForwardVerticalGrip", new Vector3(0f, -0.10f, 0.16f), new Vector3(0.05f, 0.14f, 0.05f), p.Dark);

            // 顶部加特林转鼓墨盒
            CreateCylinder(root, "TopInkDrum", new Vector3(0f, 0.16f, 0.04f), new Vector3(0.14f, 0.14f, 0.14f), p.TeamLiquid).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "DrumCapFront", new Vector3(0f, 0.16f, 0.17f), new Vector3(0.15f, 0.02f, 0.15f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "DrumCapRear", new Vector3(0f, 0.16f, -0.09f), new Vector3(0.15f, 0.02f, 0.15f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 6 联装旋转枪管簇 (Spinning Barrels Cluster)
            var clusterGo = new GameObject("RotatingBarrelsCluster");
            clusterGo.transform.SetParent(root, false);
            clusterGo.transform.localPosition = new Vector3(0f, 0.04f, 0.20f);
            barrelsCluster = clusterGo.transform;

            CreateCylinder(barrelsCluster, "CenterSpindle", new Vector3(0f, 0f, 0.20f), new Vector3(0.035f, 0.38f, 0.035f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 6 根圆形排列枪管
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                float bx = Mathf.Cos(angle) * 0.055f;
                float by = Mathf.Sin(angle) * 0.055f;

                var b = CreateCylinder(barrelsCluster, "Barrel_" + i,
                    new Vector3(bx, by, 0.22f),
                    new Vector3(0.024f, 0.40f, 0.024f),
                    p.Metal);
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // 3 圈固定卡盘
            for (int k = 0; k < 3; k++)
            {
                float z = 0.08f + k * 0.16f;
                var clamp = CreateCylinder(barrelsCluster, "ClampDisc_" + k,
                    new Vector3(0f, 0f, z),
                    new Vector3(0.15f, 0.015f, 0.15f),
                    p.Dark);
                clamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.04f, 0.65f);
            muzzle = muz.transform;
        }

        #endregion
    }
}
