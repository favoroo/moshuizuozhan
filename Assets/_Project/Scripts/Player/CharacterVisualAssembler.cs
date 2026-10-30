using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// Q 版无腿蛋形角色（Squidkid Egg Rig）与 7 类主武器装配工厂。
    /// 遵循《P1 项目约定》与 CharacterMetrics 唯一尺寸真源：
    /// 1. 人形态采用低重心、无腿蛋形圆润躯干，由 EggGaitSolver 驱动接触挤压与左右摇晃；
    /// 2. 贴地枢轴（ChestPivotY = 0.05m）让倾斜与弹跳表现为整蛋摇摆而非上半身折叠；
    /// 3. 蛋体网格采用解析法线分段光滑旋转体，原点置于脚底，保证着色连续与无接缝；
    /// 4. 材质全面切换为 P1/CharacterToon 卡通着色器（蛋体唯一启用外描边），玻璃保持 URP Lit 透明；
    /// 5. 7 套武器模型按 CharacterMetrics 缩放换算挂载并绑定动态构件与枪口；
    /// 6. 武器静态构件在装配时按材质合并为少量 MergedStatic_* 网格压缩 DrawCall：
    ///    源部件 GameObject / MeshFilter / 测试锚点层级全部保留、仅禁用渲染；
    ///    动态构件（滚筒滚轴、加特林枪管簇、蓄力线圈）与枪口锚点不参与合并。
    /// </summary>
    public static class CharacterVisualAssembler
    {
        private static Mesh _eggMesh;
        private static Mesh _eggShortsShellMesh;
        private static Mesh _eggWaistbandShellMesh;
        private static Mesh _sideTentacleMesh;
        private static Mesh _centerTentacleMesh;
        private static Mesh _squidMesh;

        #region Public Assembly Entry

        public static CharacterVisualController Assemble(
            Transform visualRoot,
            Team team,
            Material teamBodyMat,
            Material darkVisorMat,
            WeaponConfigSO initialWeaponConfig)
        {
            // 1. 清理已有子节点（防止重复生成导致层级重叠或泄漏）
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(visualRoot.GetChild(i).gameObject);
            }

            // 2. 准备材质调色盘（队伍色卡通材质 + URP Lit 透明玻璃）
            Color teamColor = ResolveTeamColor(team);
            var palette = BuildMaterialPalette(team, teamColor, teamBodyMat, darkVisorMat);

            // 3. 构建人形态 (Kid Root)
            var kidGo = new GameObject("KidRoot");
            kidGo.transform.SetParent(visualRoot, false);

            Transform hips, chest, head, leftHair, rightHair, leftArm, rightArm, tankFill;
            Transform bodySquash, leftEye, rightEye;
            BuildKidHierarchy(kidGo.transform, palette,
                out hips, out chest, out head,
                out leftHair, out rightHair, out leftArm, out rightArm, out tankFill,
                out bodySquash, out leftEye, out rightEye);

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

            // 5.1 武器挂到胸部贴地枢轴下（世界位置保持不变）：
            // 保留原有 -chest.localPosition 偏移写法，抵消 Chest 局部高度，使武器在视觉上相对于根空间绝对位置正确
            weaponGo.transform.SetParent(chest, false);
            weaponGo.transform.localPosition = -chest.localPosition;

            // 5.2 武器静态构件网格合并（装配期一次性完成，战斗热路径零开销）：
            // 每把程序化武器由十几个独立 MeshRenderer 组成，按材质分组合并成少量 MergedStatic_* 网格压缩 DrawCall。
            // 源部件 GameObject 与测试锚点全部保留、仅禁用渲染；合并失败自动降级回独立渲染，不影响装配主流程。
            try
            {
                MergeWeaponStaticMeshes(shooterGo, rollerGo, chargerGo, dualiesGo, slosherGo, blasterGo, splatlingGo,
                    rollerDrum, splatlingBarrels, chargerCoils);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CharacterVisualAssembler] 武器静态网格合并失败，已降级为独立部件渲染：{e}");
            }

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

            // 6.1 注入蛋形角色步态与面部动效骨架绑定
            controller.BindEggRig(bodySquash, head, leftEye, rightEye);

            WeaponKind startKind = initialWeaponConfig != null ? initialWeaponConfig.weaponKind : WeaponKind.Shooter;
            controller.EquipWeaponKind(startKind);

            // 裁剪面部贴片、墨渍小滴、手部与细碎构件等微小部件的阴影投射
            ApplyShadowCastingRules(visualRoot);

            return controller;
        }

        #endregion

        #region Material Palette

        private struct Palette
        {
            /// <summary>中性上衣（奶油白），蛋体主体与短袖内层。</summary>
            public Material Shirt;
            /// <summary>浅灰短袖：和奶油白上衣拉开层次，让手臂不再糊成身体上的圆点。</summary>
            public Material Sleeve;
            /// <summary>深色短裤，蛋体下段。</summary>
            public Material Shorts;
            /// <summary>队伍色腰带，唯一横贯身体的大面积队伍色点缀。</summary>
            public Material Waistband;
            /// <summary>兼容旧字段：等价于 Shirt。</summary>
            public Material TeamCloth;
            public Material TeamHair;
            /// <summary>胸口墨渍专用：哑光队伍色，避免小圆片被高光打成"气泡"。</summary>
            public Material InkDecal;
            public Material TeamLiquid;
            public Material SquidBody;
            public Material Skin;
            public Material Dark;
            public Material White;
            public Material Metal;
            public Material Glass;
            public Material Blush;
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

            // 设计参考 INKWAVE：服装/皮肤保持中性色，队伍墨色只作为局部点缀。
            // 旧版整颗蛋都刷队伍色，站在同色墨水上会完全糊进地面；现在上衣为奶油白、下摆为深色短裤，
            // 队伍色只出现在腰带、墨焰、瞳孔、墨罐、胸章与手腕，任何底色上都能一眼分辨。
            Color shirtColor = new Color(0.94f, 0.93f, 0.89f);
            Color shortsColor = new Color(0.153f, 0.188f, 0.290f); // 对齐 INKWAVE 的深海军蓝短裤
            Color darkColor = new Color(0.10f, 0.115f, 0.155f);

            // 整颗蛋体只用这一件奶油白描边材质；短裤与腰带是另外的贴面薄壳（GetEggShortsShellMesh / GetEggWaistbandShellMesh）。
            p.Shirt = CreateCharacterMat("Mat_CharacterShirt", shirtColor, 0.62f, 0.35f, outlined: true, shadeSoftness: 0.10f);
            p.Sleeve = CreateCharacterMat("Mat_CharacterSleeve", new Color(0.78f, 0.81f, 0.85f), 0.50f, 0.30f, shadeSoftness: 0.08f);
            // 短裤与腰带是不带描边的贴面薄壳：整颗蛋体只保留一条连续外描边，避免多材质接缝处出现黑线
            p.Shorts = CreateCharacterMat("Mat_CharacterShorts", shortsColor, 0.45f, 0.30f, shadeSoftness: 0.08f);
            p.Waistband = CreateCharacterMat("Mat_CharacterWaistband", teamCol, 0.85f, 0.55f, shadeSoftness: 0.06f);
            p.TeamCloth = p.Shirt;

            // 队伍墨色部件：墨焰/瞳孔/腕带共用；胸章使用哑光 InkDecal，墨罐液面单独带一点自发光。
            p.TeamHair = CreateCharacterMat("Mat_TeamHair", teamCol, 0.90f, 0.70f, shadeSoftness: 0.05f);
            p.InkDecal = CreateCharacterMat("Mat_TeamInkDecal", teamCol, 0.18f, 0.12f, shadeSoftness: 0.06f);
            p.TeamLiquid = CreateCharacterMat("Mat_TeamLiquid", teamCol, 0.95f, 0.60f, emission: teamCol * 0.35f);
            p.SquidBody = CreateCharacterMat("Mat_SquidBody", teamCol, 0.90f, 0.70f);

            p.Skin = CreateCharacterMat("Mat_KidSkin", new Color(0.96f, 0.73f, 0.61f), 0.38f, 0.18f);
            p.Dark = CreateCharacterMat("Mat_DarkPlastic", darkColor, 0.40f, 0.25f);
            p.White = CreateCharacterMat("Mat_AccentsWhite", new Color(0.95f, 0.96f, 0.98f), 0.40f, 0.30f);
            p.Metal = CreateCharacterMat("Mat_Gunmetal", new Color(0.55f, 0.58f, 0.65f), 0.65f, 0.40f);
            p.Blush = CreateCharacterMat("Mat_Blush", new Color(0.98f, 0.62f, 0.60f), 0.20f, 0.15f);

            // 墨罐玻璃必须保留 URP Lit 透明表面材质：卡通 Shader 不支持透明通道，否则墨罐外壳会变为不透明阻挡内部液面
            p.Glass = CreateLitMat("Mat_TankGlass", new Color(0.85f, 0.92f, 1.0f, 0.45f), 0.95f, 0.1f);

            return p;
        }

        private static Material CreateCharacterMat(
            string name,
            Color baseCol,
            float smoothness,
            float rimStrength,
            bool outlined = false,
            Color? emission = null,
            float shadeSoftness = 0.03f)
        {
            string targetShaderName = outlined ? "P1/CharacterToonOutlined" : "P1/CharacterToon";
            Shader sh = Shader.Find(targetShaderName);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");

#if UNITY_EDITOR
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
                    if (asset.shader != sh)
                    {
                        asset.shader = sh;
                    }
                    ApplyCharacterMatSettings(asset, baseCol, smoothness, rimStrength, emission, shadeSoftness);
                    UnityEditor.EditorUtility.SetDirty(asset);
                    return asset;
                }

                var created = new Material(sh) { name = fileName };
                ApplyCharacterMatSettings(created, baseCol, smoothness, rimStrength, emission, shadeSoftness);
                UnityEditor.AssetDatabase.CreateAsset(created, path);
                return created;
            }
#endif
            var mat = new Material(sh) { name = name };
            ApplyCharacterMatSettings(mat, baseCol, smoothness, rimStrength, emission, shadeSoftness);
            return mat;
        }

        private static void ApplyCharacterMatSettings(
            Material mat,
            Color baseCol,
            float smoothness,
            float rimStrength,
            Color? emission,
            float shadeSoftness)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", rimStrength);
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", Color.white);
            // 明暗交界柔化：曲面蛋体上硬二分色阶会切出明显的块状边界，看起来"不平整"
            if (mat.HasProperty("_ShadeSoftness")) mat.SetFloat("_ShadeSoftness", shadeSoftness);

            if (emission.HasValue && emission.Value != Color.black)
            {
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
                if (mat.HasProperty("_EmissionStrength")) mat.SetFloat("_EmissionStrength", 1f);
                mat.EnableKeyword("_EMISSION");
            }
            else
            {
                if (mat.HasProperty("_EmissionStrength")) mat.SetFloat("_EmissionStrength", 0f);
            }
        }

        private static Material CreateLitMat(string name, Color baseCol, float smoothness, float metallic, Color? emission = null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");

#if UNITY_EDITOR
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
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            if (baseCol.a < 0.999f)
            {
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
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
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

        private static GameObject CreateMeshPart(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// 优化角色的阴影投射：裁剪微小部件（面部贴片、墨渍小滴、袖口/小臂/手、旋钮与武器细碎零件）
        /// 的阴影以压缩阴影 Pass DrawCall，保留蛋体主躯干、发束与武器主体件的投影。
        /// 武器合并网格（MergedStatic_*）不在此逐件裁剪：其阴影行为已在合并时按组内部件多数决定。
        /// </summary>
        private static void ApplyShadowCastingRules(Transform visualRoot)
        {
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                // 合并网格按多数规则已带阴影开关，跳过名称规则避免覆盖（组内微小件随大网格投影属预期增益/损耗）
                if (r.gameObject.name.StartsWith(MergedPartPrefix)) continue;
                r.shadowCastingMode = ShouldCastShadow(r.gameObject.name)
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private static bool ShouldCastShadow(string partName)
        {
            // 关闭面部小件、墨渍小滴、背包外露附件、手部小件与武器零件阴影
            if (partName.StartsWith("EyeMask") ||
                partName.StartsWith("Eye_") ||
                partName == "Iris" ||
                partName == "Pupil" ||
                partName.StartsWith("Glint") ||
                partName.StartsWith("Blush") ||
                partName == "Mouth" ||
                partName.StartsWith("ChestInk") ||
                // 短裤/腰带是 5mm/9mm 贴面薄壳，蛋体本身已投出相同轮廓；
                // 重复投影只增加阴影 draw，并在细级联下放大自阴影噪声。
                partName == "EggShorts" ||
                partName == "EggWaistband" ||
                partName == "TopCap" ||
                partName == "ValveKnob" ||
                partName == "BottomCap" ||
                partName == "Cuff" ||
                partName == "Forearm" ||
                partName == "Hand" ||
                partName.Contains("Grip") ||
                partName == "InkSpine" ||
                partName.StartsWith("Nozzle") ||
                partName.StartsWith("Bearing") ||
                partName.StartsWith("TreadRib") ||
                partName == "ButtPad" ||
                partName == "ScopeLens" ||
                partName == "MuzzleBrake" ||
                partName.StartsWith("Coil_") ||
                partName == "DorsalFin" ||
                partName.StartsWith("ReinforceHoop") ||
                partName == "PourLip" ||
                partName == "HandleLever" ||
                partName == "CarryBar" ||
                partName.StartsWith("BulbRib") ||
                partName == "PressureGauge" ||
                partName == "GaugeDial" ||
                partName == "GaugeNeedle" ||
                partName == "BellRim" ||
                partName == "PumpSlide" ||
                partName.StartsWith("DrumCap") ||
                partName.StartsWith("ClampDisc"))
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Egg Body Procedural Mesh

        // 解析蛋形轮廓：max(半径) 出现高度约 36%，下段饱满、上段收窄。
        // 旧版用 10 个控制点 + SmoothStep 分段插值，每个控制点都会把曲率压平，
        // 在蛋体表面形成一圈圈"微鼓包"；这里改为单一解析函数，半径与切向连续可导。
        private const float EggProfileAlpha = 0.68f;
        private const float EggProfileBeta = 0.50f;

        /// <summary>
        /// 采样蛋体归一化轮廓半径比例（0~1）。解析函数在两端自然收敛到 0，
        /// 底部圆润触地、顶部收成圆顶，不再需要分段焊接。
        /// </summary>
        private static float SampleEggRadius01(float t)
        {
            t = Mathf.Clamp01(t);
            float s = Mathf.Sin(Mathf.PI * Mathf.Pow(t, EggProfileAlpha));
            return Mathf.Pow(Mathf.Max(0f, s), EggProfileBeta);
        }

        /// <summary>蛋体在指定高度处的实际半径（米）。</summary>
        private static float EggRadiusAt(float y)
            => SampleEggRadius01(y / CharacterMetrics.BodyHeight) * CharacterMetrics.BodyMaxRadius;

        /// <summary>
        /// 求蛋体前半球表面（面向 +Z）上的一点，并沿表面法线向外推出 outOffset。
        /// 贴面部件（眼睛/面具/腮红/嘴）必须用它定位：蛋体是曲面，直接写死 z 会让椭球要么埋进肚子要么浮在空中。
        /// </summary>
        private static Vector3 EggFrontPoint(float x, float y, float outOffset)
        {
            float r = EggRadiusAt(y);
            float xx = Mathf.Clamp(x, -r * 0.92f, r * 0.92f);
            float z = Mathf.Sqrt(Mathf.Max(0.0001f, r * r - xx * xx));
            Vector3 normal = new Vector3(xx, 0f, z).normalized;
            return new Vector3(xx, y, z) + normal * outOffset;
        }

        /// <summary>贴面部件的朝向：让局部 +Z 对准该处的水平表面法线。</summary>
        private static float EggFaceYaw(float x, float y)
        {
            float r = EggRadiusAt(y);
            float xx = Mathf.Clamp(x, -r * 0.92f, r * 0.92f);
            float z = Mathf.Sqrt(Mathf.Max(0.0001f, r * r - xx * xx));
            return Mathf.Atan2(xx, z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 创建并静态缓存 Q 版无腿蛋形角色网格。
        /// 40 环 × 40 径向段解析旋转体，原点位于脚底（y=0）；上下极点各用一个中心点封口。
        /// 整颗蛋体使用单一描边材质；短裤与腰带是另外的贴面薄壳（见 CreateEggShellMesh）。
        /// 法线全部由解析轮廓切线推导，避免四边形拼接处的硬边与明暗断裂。
        /// </summary>
        public static Mesh CreateEggBodyMesh()
        {
            if (_eggMesh != null) return _eggMesh;

            var mesh = new Mesh { name = "EggBodyMesh" };

            const int ringCount = 40;
            const int radialSegs = 40;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // 1. 底部极点（y=0，贴地）
            int bottomCenterIdx = 0;
            verts.Add(Vector3.zero);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0f));

            // 2. 侧面圆环：跳过两端极点，避免退化三角形
            for (int r = 1; r < ringCount; r++)
            {
                float t = (float)r / ringCount;
                float y = t * CharacterMetrics.BodyHeight;
                float radius = SampleEggRadius01(t) * CharacterMetrics.BodyMaxRadius;

                // 差分计算轮廓沿高度切线斜率 dr/dy
                const float dt = 0.0005f;
                float tPrev = Mathf.Max(0f, t - dt);
                float tNext = Mathf.Min(1f, t + dt);
                float rPrev = SampleEggRadius01(tPrev) * CharacterMetrics.BodyMaxRadius;
                float rNext = SampleEggRadius01(tNext) * CharacterMetrics.BodyMaxRadius;
                float dr_dy = (rNext - rPrev) / Mathf.Max(0.00001f, (tNext - tPrev) * CharacterMetrics.BodyHeight);

                for (int s = 0; s <= radialSegs; s++)
                {
                    float uCoord = (float)s / radialSegs;
                    float theta = uCoord * Mathf.PI * 2f;
                    float cos = Mathf.Cos(theta);
                    float sin = Mathf.Sin(theta);

                    verts.Add(new Vector3(cos * radius, y, sin * radius));

                    // 解析法线：profileTangent × aroundTangent = (cos, -dr_dy, sin)，严格朝外
                    Vector3 radialDir = new Vector3(cos, 0f, sin);
                    Vector3 normal = new Vector3(cos, -dr_dy, sin);
                    if (Vector3.Dot(normal, radialDir) < 0f)
                    {
                        normal = -normal;
                    }
                    normals.Add(normal.normalized);

                    uvs.Add(new Vector2(uCoord, t));
                }
            }

            int lastRing = ringCount - 1;

            // 3. 底部封口三角扇
            int firstRingBase = 1;
            for (int s = 0; s < radialSegs; s++)
            {
                tris.Add(bottomCenterIdx);
                tris.Add(firstRingBase + s);
                tris.Add(firstRingBase + s + 1);
            }

            // 4. 侧面四边形拆三角
            for (int r = 1; r < ringCount - 1; r++)
            {
                int row0 = 1 + (r - 1) * (radialSegs + 1);
                int row1 = 1 + r * (radialSegs + 1);

                for (int s = 0; s < radialSegs; s++)
                {
                    int i0 = row0 + s;
                    int i1 = row1 + s;
                    int i0_next = row0 + s + 1;
                    int i1_next = row1 + s + 1;

                    tris.Add(i0);
                    tris.Add(i1);
                    tris.Add(i0_next);

                    tris.Add(i0_next);
                    tris.Add(i1);
                    tris.Add(i1_next);
                }
            }

            // 5. 顶部极点封顶
            int topCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, CharacterMetrics.BodyHeight, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));

            int lastRingBase = 1 + (lastRing - 1) * (radialSegs + 1);
            for (int s = 0; s < radialSegs; s++)
            {
                tris.Add(topCenterIdx);
                tris.Add(lastRingBase + s + 1);
                tris.Add(lastRingBase + s);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            _eggMesh = mesh;
            return _eggMesh;
        }

        /// <summary>短裤薄壳（贴面、无描边）。</summary>
        private static Mesh GetEggShortsShellMesh()
        {
            if (_eggShortsShellMesh == null)
            {
                _eggShortsShellMesh = BuildEggShellMesh(0.006f, 0.325f, 0.005f, "EggShortsShell");
            }
            return _eggShortsShellMesh;
        }

        /// <summary>队伍色腰带薄壳（贴面、无描边，略高于短裤上沿以盖住接缝）。</summary>
        private static Mesh GetEggWaistbandShellMesh()
        {
            if (_eggWaistbandShellMesh == null)
            {
                _eggWaistbandShellMesh = BuildEggShellMesh(0.295f, 0.365f, 0.009f, "EggWaistbandShell", true, true, 0.005f, 0f);
            }
            return _eggWaistbandShellMesh;
        }

        /// <summary>
        /// 生成沿蛋体表面偏移 offset 的开放薄壳（上下不封口，由内层蛋体兜底）。
        /// 用薄壳替代同一网格的 submesh 分材质：整颗蛋体只保留一次描边，
        /// 短裤/腰带交界处不会因为多份描边壳叠加而出现细黑线。
        /// </summary>
        private static Mesh BuildEggShellMesh(
            float tStart,
            float tEnd,
            float offset,
            string name,
            bool capBottom = false,
            bool capTop = false,
            float innerOffsetBottom = 0f,
            float innerOffsetTop = 0f)
        {
            var mesh = new Mesh { name = name };

            const int ringCount = 12;
            const int radialSegs = 40;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int r = 0; r <= ringCount; r++)
            {
                float t = Mathf.Lerp(tStart, tEnd, (float)r / ringCount);
                float y = t * CharacterMetrics.BodyHeight;
                float radius = SampleEggRadius01(t) * CharacterMetrics.BodyMaxRadius + offset;

                const float dt = 0.0005f;
                float tPrev = Mathf.Max(0f, t - dt);
                float tNext = Mathf.Min(1f, t + dt);
                float rPrev = SampleEggRadius01(tPrev) * CharacterMetrics.BodyMaxRadius;
                float rNext = SampleEggRadius01(tNext) * CharacterMetrics.BodyMaxRadius;
                float dr_dy = (rNext - rPrev) / Mathf.Max(0.00001f, (tNext - tPrev) * CharacterMetrics.BodyHeight);

                for (int s = 0; s <= radialSegs; s++)
                {
                    float uCoord = (float)s / radialSegs;
                    float theta = uCoord * Mathf.PI * 2f;
                    float cos = Mathf.Cos(theta);
                    float sin = Mathf.Sin(theta);

                    verts.Add(new Vector3(cos * radius, y, sin * radius));

                    Vector3 radialDir = new Vector3(cos, 0f, sin);
                    Vector3 normal = new Vector3(cos, -dr_dy, sin);
                    if (Vector3.Dot(normal, radialDir) < 0f) normal = -normal;
                    normals.Add(normal.normalized);

                    uvs.Add(new Vector2(uCoord, t));
                }
            }

            for (int r = 0; r < ringCount; r++)
            {
                int row0 = r * (radialSegs + 1);
                int row1 = (r + 1) * (radialSegs + 1);
                for (int s = 0; s < radialSegs; s++)
                {
                    int i0 = row0 + s;
                    int i1 = row1 + s;
                    int i0_next = row0 + s + 1;
                    int i1_next = row1 + s + 1;

                    tris.Add(i0);
                    tris.Add(i1);
                    tris.Add(i0_next);

                    tris.Add(i0_next);
                    tris.Add(i1);
                    tris.Add(i1_next);
                }
            }

            // 6. 腰带上下沿做一圈内收盖面，接到底下的短裤/蛋体表面：
            // 薄壳的开放边缘会在接缝处投出一条黑线，加盖后变成干净的队伍色皮带边。
            if (capBottom || capTop)
            {
                int outerBottomRow = 0;
                int outerTopRow = ringCount * (radialSegs + 1);

                if (capBottom)
                {
                    AddShellCap(verts, normals, uvs, tris,
                        tStart, outerBottomRow, offset, innerOffsetBottom, radialSegs, facingUp: false);
                }
                if (capTop)
                {
                    AddShellCap(verts, normals, uvs, tris,
                        tEnd, outerTopRow, offset, innerOffsetTop, radialSegs, facingUp: true);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>给薄壳端面补一圈水平盖面（从外圈收到内圈），避免开放边缘露缝/露黑。</summary>
        private static void AddShellCap(
            List<Vector3> verts,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> tris,
            float t,
            int outerRowBase,
            float outerOffset,
            float innerOffset,
            int radialSegs,
            bool facingUp)
        {
            float y = t * CharacterMetrics.BodyHeight;
            float bodyRadius = SampleEggRadius01(t) * CharacterMetrics.BodyMaxRadius;
            int innerRowBase = verts.Count;

            for (int s = 0; s <= radialSegs; s++)
            {
                float uCoord = (float)s / radialSegs;
                float theta = uCoord * Mathf.PI * 2f;
                float cos = Mathf.Cos(theta);
                float sin = Mathf.Sin(theta);
                float innerRadius = bodyRadius + innerOffset;

                verts.Add(new Vector3(cos * innerRadius, y, sin * innerRadius));
                normals.Add(facingUp ? Vector3.up : Vector3.down);
                uvs.Add(new Vector2(uCoord, t));
            }

            for (int s = 0; s < radialSegs; s++)
            {
                int outerA = outerRowBase + s;
                int outerB = outerRowBase + s + 1;
                int innerA = innerRowBase + s;
                int innerB = innerRowBase + s + 1;

                if (facingUp)
                {
                    tris.Add(outerA);
                    tris.Add(innerA);
                    tris.Add(outerB);

                    tris.Add(outerB);
                    tris.Add(innerA);
                    tris.Add(innerB);
                }
                else
                {
                    tris.Add(outerA);
                    tris.Add(outerB);
                    tris.Add(innerA);

                    tris.Add(outerB);
                    tris.Add(innerB);
                    tris.Add(innerA);
                }
            }
        }

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
            out Transform tankFill,
            out Transform bodySquash,
            out Transform leftEye,
            out Transform rightEye)
        {
            // 蛋形角色取消腿部骨骼结构，hips 传 null 以保持旧接口兼容
            hips = null;

            // 1. 躯干枢轴 (Chest)：位于贴地低位 (y = ChestPivotY = 0.05m)。
            // 使得奔跑前倾与左右摇摆像"蛋在地面晃荡"，而非传统人形的上半身在腰部折断
            var chestGo = new GameObject("Chest");
            chestGo.transform.SetParent(kidRoot, false);
            chestGo.transform.localPosition = new Vector3(0f, CharacterMetrics.ChestPivotY, 0f);
            chest = chestGo.transform;

            // 2. 弹性形变容器 (BodySquash)：挂在 Chest 下，抵消枢轴高度置于世界 y=0。
            // 步态弹跳（BobY）与触地挤压拉伸（SquashY / SquashXZ）全部作用在它身上
            var squashGo = new GameObject("BodySquash");
            squashGo.transform.SetParent(chest, false);
            squashGo.transform.localPosition = new Vector3(0f, -CharacterMetrics.ChestPivotY, 0f);
            bodySquash = squashGo.transform;

            // 2.1 蛋形主躯干：整颗蛋体只用一件带描边的奶油白材质。
            // 短裤与腰带是另外两层贴面薄壳（沿表面法线外扩 5mm / 9mm），像真实衣物一样叠在蛋体上；
            // 这样外轮廓只有一条完整黑边，不会出现 submesh 各画一次描边造成的接缝黑线。
            var eggGo = new GameObject("EggBody");
            eggGo.transform.SetParent(bodySquash, false);
            eggGo.transform.localPosition = Vector3.zero;
            var mf = eggGo.AddComponent<MeshFilter>();
            var mr = eggGo.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateEggBodyMesh();
            mr.sharedMaterial = p.Shirt;

            var shortsGo = new GameObject("EggShorts");
            shortsGo.transform.SetParent(bodySquash, false);
            shortsGo.transform.localPosition = Vector3.zero;
            shortsGo.AddComponent<MeshFilter>().sharedMesh = GetEggShortsShellMesh();
            shortsGo.AddComponent<MeshRenderer>().sharedMaterial = p.Shorts;

            var bandGo = new GameObject("EggWaistband");
            bandGo.transform.SetParent(bodySquash, false);
            bandGo.transform.localPosition = Vector3.zero;
            bandGo.AddComponent<MeshFilter>().sharedMesh = GetEggWaistbandShellMesh();
            bandGo.AddComponent<MeshRenderer>().sharedMaterial = p.Waistband;

            // 2.2 面部分区靠大眼、连体面罩与胸前墨点承担，蛋体本身保持一整块干净曲面。

            // 2.3 面部容器 (Face)：容器本身放在原点，所有部件直接用「蛋体身体空间」坐标贴面
            var faceGo = new GameObject("Face");
            faceGo.transform.SetParent(bodySquash, false);
            faceGo.transform.localPosition = Vector3.zero;
            var face = faceGo.transform;

            // 2.3.1 连体深色眼罩：两片眼窝 + 中间鼻梁桥，读起来像 inkling 的面罩，而不是两个孤立的黑斑。
            const float eyeY = 0.72f;
            var maskBridge = CreateSphere(face, "EyeMask_Bridge",
                EggFrontPoint(0f, eyeY, 0.002f), new Vector3(0.20f, 0.15f, 0.028f), p.Dark);
            maskBridge.transform.localRotation = Quaternion.identity;

            // 大眼：眼睛贴在上半前脸（y≈0.72），由曲面采样保证既不陷入也不悬浮。
            leftEye = null;
            rightEye = null;
            for (int s = -1; s <= 1; s += 2)
            {
                float eyeX = s * 0.135f;
                string side = s < 0 ? "L" : "R";
                Quaternion faceRot = Quaternion.Euler(0f, EggFaceYaw(eyeX, eyeY), 0f);

                // 面罩压扁成贴面片，减少"脸上贴了几个球"的凸起感
                var mask = CreateSphere(face, "EyeMask_" + side, EggFrontPoint(eyeX, eyeY, 0.003f), new Vector3(0.24f, 0.25f, 0.030f), p.Dark);
                mask.transform.localRotation = faceRot;

                var eye = CreateSphere(face, "Eye_" + side, EggFrontPoint(eyeX, eyeY, 0.010f), new Vector3(0.175f, 0.195f, 0.062f), p.White);
                eye.transform.localRotation = faceRot;

                // 白眼球 + 队伍色虹膜 + 深色瞳孔 + 双高光：彩色只占眼球中央一小块，远看仍是大白眼。
                // 眼球的局部空间半径是 0.5（Unity 球体直径为 1），贴片必须放在 z≈0.5 的极点附近才能露出表面。
                CreateSphere(eye.transform, "Iris", new Vector3(0f, -0.005f, 0.50f), new Vector3(0.58f, 0.58f, 0.26f), p.TeamHair);
                CreateSphere(eye.transform, "Pupil", new Vector3(0f, -0.005f, 0.62f), new Vector3(0.30f, 0.32f, 0.18f), p.Dark);
                CreateSphere(eye.transform, "Glint", new Vector3(-0.15f, 0.17f, 0.70f), new Vector3(0.16f, 0.16f, 0.10f), p.White);
                CreateSphere(eye.transform, "Glint2", new Vector3(0.12f, -0.12f, 0.68f), new Vector3(0.09f, 0.09f, 0.08f), p.White);

                if (s < 0) leftEye = eye.transform;
                else rightEye = eye.transform;

                // 腮红贴在眼睛外下方
                float blushX = s * 0.255f;
                var blush = CreateSphere(face, "Blush_" + side, EggFrontPoint(blushX, 0.585f, 0.004f), new Vector3(0.095f, 0.05f, 0.022f), p.Blush);
                blush.transform.localRotation = Quaternion.Euler(0f, EggFaceYaw(blushX, 0.585f), 0f);
            }

            // 小 ω 嘴：极扁的深色椭圆，贴在两眼下方的中线上
            var mouth = CreateSphere(face, "Mouth", EggFrontPoint(0f, 0.60f, 0.004f), new Vector3(0.085f, 0.028f, 0.020f), p.Dark);
            mouth.transform.localRotation = Quaternion.identity;

            // 2.3.2 胸前队伍色墨点：把"全身刷队伍色"改成小面积墨渍点缀，既保留队伍识别，
            // 又不会让角色和地面/墨迹糊成一片。位置全部由蛋体曲面采样，不会浮空。
            Vector3[] chestInkPos = new Vector3[]
            {
                new Vector3(0.12f, 0.500f, 0f),
                new Vector3(0.19f, 0.455f, 0f),
                new Vector3(0.045f, 0.545f, 0f),
                new Vector3(0.155f, 0.415f, 0f)
            };
            float[] chestInkSize = new float[] { 0.085f, 0.050f, 0.042f, 0.034f };
            for (int i = 0; i < chestInkPos.Length; i++)
            {
                Vector3 cp = chestInkPos[i];
                float size = chestInkSize[i];
                var spot = CreateSphere(face, "ChestInk_" + i, EggFrontPoint(cp.x, cp.y, 0.003f),
                    new Vector3(size, size * 0.85f, size * 0.14f), p.InkDecal);
                spot.transform.localRotation = Quaternion.Euler(0f, EggFaceYaw(cp.x, cp.y), 0f);
            }
            var drip = CreateSphere(face, "ChestInk_Drip", EggFrontPoint(0.105f, 0.430f, 0.003f),
                new Vector3(0.022f, 0.050f, 0.012f), p.InkDecal);
            drip.transform.localRotation = Quaternion.Euler(0f, EggFaceYaw(0.105f, 0.430f), 0f);

            // 2.4 头顶墨焰触须束 (InkCrest)：三束墨焰从圆顶后方起笔、向后下方扫出弧线，
            // 像真正垂在脑后的触须头发，而不是竖在头顶的两根天线。
            var crestGo = new GameObject("InkCrest");
            crestGo.transform.SetParent(bodySquash, false);
            crestGo.transform.localPosition = new Vector3(0f, 0.99f, 0f);

            var lHairGo = new GameObject("LeftHairPivot");
            lHairGo.transform.SetParent(crestGo.transform, false);
            lHairGo.transform.localPosition = new Vector3(-0.09f, 0f, 0f);
            lHairGo.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);   // 向左外扩，形成扇形墨发
            leftHair = lHairGo.transform;
            CreateMeshPart(leftHair, "LeftTentacle", GetSideTentacleMesh(), p.TeamHair);

            var rHairGo = new GameObject("RightHairPivot");
            rHairGo.transform.SetParent(crestGo.transform, false);
            rHairGo.transform.localPosition = new Vector3(0.09f, 0f, 0f);
            rHairGo.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);  // 向右外扩
            rightHair = rHairGo.transform;
            CreateMeshPart(rightHair, "RightTentacle", GetSideTentacleMesh(), p.TeamHair);

            // 中央发束：比两侧短一截，填满头冠又不遮挡脸
            var centerHairGo = new GameObject("CenterHairPivot");
            centerHairGo.transform.SetParent(crestGo.transform, false);
            centerHairGo.transform.localPosition = new Vector3(0f, 0.012f, -0.020f);
            CreateMeshPart(centerHairGo.transform, "CenterTentacle", GetCenterTentacleMesh(), p.TeamHair);

            // 2.5 后背紧凑型墨罐背包 (InkTankBackpack)：外推到 z=-0.40，让壳体真正凸出蛋体背面成为可见背包
            var backpackGo = new GameObject("InkTankBackpack");
            backpackGo.transform.SetParent(bodySquash, false);
            backpackGo.transform.localPosition = new Vector3(0f, 0.50f, -0.43f);

            CreateBox(backpackGo.transform, "MountPlate", new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.26f, 0.035f), p.Dark);
            CreateCylinder(backpackGo.transform, "GlassChamber", Vector3.zero, new Vector3(0.26f, 0.15f, 0.26f), p.Glass);

            var fillGo = CreateCylinder(backpackGo.transform, "LiquidFill", Vector3.zero, new Vector3(0.22f, 0.135f, 0.22f), p.TeamLiquid);
            tankFill = fillGo.transform;

            CreateCylinder(backpackGo.transform, "TopCap", new Vector3(0f, 0.15f, 0f), new Vector3(0.28f, 0.02f, 0.28f), p.Metal);
            CreateCylinder(backpackGo.transform, "ValveKnob", new Vector3(0f, 0.18f, 0f), new Vector3(0.06f, 0.025f, 0.06f), p.Dark);
            CreateCylinder(backpackGo.transform, "BottomCap", new Vector3(0f, -0.15f, 0f), new Vector3(0.28f, 0.02f, 0.28f), p.Metal);

            // 2.6 头顶空锚点 (Head)：供待机微表现进行微妙呼吸浮动
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(bodySquash, false);
            headGo.transform.localPosition = new Vector3(0f, 0.74f, 0f);
            head = headGo.transform;

            // 3. 左右手臂 (LeftArm / RightArm)：必须是 Chest 的直接子节点
            //    （测试用例强依赖 KidRoot.transform.Find("Chest/LeftArm") 路径）。
            //    蛋体很胖，枢轴只能埋进侧面、让前臂从体表前方伸出，圆手才落在武器握把附近。
            var lArmGo = new GameObject("LeftArm");
            lArmGo.transform.SetParent(chest, false);
            lArmGo.transform.localPosition = new Vector3(-0.34f, 0.41f, 0.04f);
            leftArm = lArmGo.transform;
            BuildMittenArm(leftArm, p, 1);

            var rArmGo = new GameObject("RightArm");
            rArmGo.transform.SetParent(chest, false);
            rArmGo.transform.localPosition = new Vector3(0.34f, 0.41f, 0.04f);
            rightArm = rArmGo.transform;
            BuildMittenArm(rightArm, p, -1);
        }

        /// <summary>
        /// 短袖手臂：上臂（奶油白短袖）→ 队伍色袖口 → 肤色小臂 → 肤色圆手。
        /// 旧版整条手臂都是奶油白袖子，叠在奶油白蛋体上远看只剩几个圆点；
        /// 露出一段肤色小臂后，正面/侧面都能读出"手在握枪"。
        /// </summary>
        private static void BuildMittenArm(Transform root, Palette p, int inward)
        {
            Vector3 toHand = new Vector3(inward * 0.11f, 0.06f, 0.46f);
            // 肘部放到 72% 处：刚好越过蛋体表面，短袖、袖口和小臂都能被看见；
            // 旧版 52% 时整段上臂都埋在肚子里，正面只剩一颗肤色圆点。
            Vector3 elbow = toHand * 0.72f;
            Vector3 wrist = toHand * 0.93f;

            // 上臂短袖
            Vector3 sleeveVec = elbow;
            var sleeve = CreateCapsule(root, "Sleeve", sleeveVec * 0.5f,
                new Vector3(0.095f, sleeveVec.magnitude * 0.5f, 0.095f), p.Sleeve);
            sleeve.transform.localRotation = Quaternion.FromToRotation(Vector3.up, sleeveVec.normalized);

            // 队伍色袖口：盖住短袖与肤色小臂的接缝
            var cuff = CreateCylinder(root, "Cuff", elbow,
                new Vector3(0.105f, 0.018f, 0.105f), p.TeamHair);
            cuff.transform.localRotation = Quaternion.FromToRotation(Vector3.up, sleeveVec.normalized);

            // 肤色小臂
            Vector3 forearmVec = wrist - elbow;
            var forearm = CreateCapsule(root, "Forearm", (elbow + wrist) * 0.5f,
                new Vector3(0.075f, forearmVec.magnitude * 0.5f + 0.01f, 0.075f), p.Skin);
            forearm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, forearmVec.normalized);

            // 圆手
            CreateSphere(root, "Hand", toHand, new Vector3(0.105f, 0.105f, 0.105f), p.Skin);
        }

        private static Mesh GetSideTentacleMesh()
        {
            if (_sideTentacleMesh == null)
            {
                _sideTentacleMesh = CreateTentacleMesh(
                    0.070f, 0.012f,
                    new Vector3(0f, -0.020f, 0.020f),
                    new Vector3(0f, 0.140f, 0.040f),
                    new Vector3(0f, 0.180f, -0.180f),
                    new Vector3(0f, 0.080f, -0.380f),
                    "SideTentacleMesh");
            }
            return _sideTentacleMesh;
        }

        private static Mesh GetCenterTentacleMesh()
        {
            if (_centerTentacleMesh == null)
            {
                _centerTentacleMesh = CreateTentacleMesh(
                    0.065f, 0.011f,
                    new Vector3(0f, -0.020f, 0.020f),
                    new Vector3(0f, 0.130f, 0.030f),
                    new Vector3(0f, 0.160f, -0.150f),
                    new Vector3(0f, 0.050f, -0.290f),
                    "CenterTentacleMesh");
            }
            return _centerTentacleMesh;
        }

        /// <summary>
        /// 单条触须：三次贝塞尔中心线 + 变半径圆截面管状网格，两端自然收细。
        /// 用一整条光滑曲面替代"4 段胶囊叠起来"的做法，背面不会再出现一串肉瘤状凸起。
        /// </summary>
        private static Mesh CreateTentacleMesh(
            float baseRadius,
            float tipRadius,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            string name)
        {
            const int ringCount = 20;
            const int radialSegs = 14;

            var mesh = new Mesh { name = name };
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            Vector3 lastCenter = p3;
            Vector3 lastTangent = (p3 - p2).normalized;

            for (int r = 0; r <= ringCount; r++)
            {
                float u = (float)r / ringCount;
                Vector3 center = Bezier(p0, p1, p2, p3, u);
                Vector3 tangent = (Bezier(p0, p1, p2, p3, Mathf.Min(1f, u + 0.002f))
                                   - Bezier(p0, p1, p2, p3, Mathf.Max(0f, u - 0.002f))).normalized;
                if (tangent.sqrMagnitude < 0.0001f) tangent = lastTangent;

                // 根部粗、梢部细，中段略鼓；曲线在 YZ 平面内，right 始终与切线垂直
                float taper = Mathf.Lerp(baseRadius, tipRadius, Mathf.Pow(u, 0.9f));
                float radius = taper * (1f + 0.08f * Mathf.Sin(u * Mathf.PI));

                Vector3 side = Vector3.right;
                Vector3 up = Vector3.Cross(tangent, side).normalized;

                for (int s = 0; s < radialSegs; s++)
                {
                    float theta = (float)s / radialSegs * Mathf.PI * 2f;
                    Vector3 dir = side * Mathf.Cos(theta) + up * Mathf.Sin(theta);
                    verts.Add(center + dir * radius);
                    uvs.Add(new Vector2((float)s / radialSegs, u));
                }

                lastCenter = center;
                lastTangent = tangent;
            }

            for (int r = 0; r < ringCount; r++)
            {
                int row0 = r * radialSegs;
                int row1 = (r + 1) * radialSegs;
                for (int s = 0; s < radialSegs; s++)
                {
                    int next = (s + 1) % radialSegs;
                    int i0 = row0 + s;
                    int i0n = row0 + next;
                    int i1 = row1 + s;
                    int i1n = row1 + next;

                    tris.Add(i0);
                    tris.Add(i0n);
                    tris.Add(i1);

                    tris.Add(i0n);
                    tris.Add(i1n);
                    tris.Add(i1);
                }
            }

            // 圆润尖端封口
            int tipIndex = verts.Count;
            verts.Add(lastCenter + lastTangent * tipRadius * 0.8f);
            uvs.Add(new Vector2(0.5f, 1f));
            int lastRow = ringCount * radialSegs;
            for (int s = 0; s < radialSegs; s++)
            {
                int next = (s + 1) % radialSegs;
                tris.Add(lastRow + s);
                tris.Add(lastRow + next);
                tris.Add(tipIndex);
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float v = 1f - u;
            return a * (v * v * v) + b * (3f * v * v * u) + c * (3f * v * u * u) + d * (u * u * u);
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

            // 1. 更饱满圆润的乌贼流线型墨团网格
            var mf = bodyGo.AddComponent<MeshFilter>();
            var mr = bodyGo.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateSquidMesh();
            mr.sharedMaterial = p.SquidBody;

            // 2. 乌贼大眼（白眼球 + 深色瞳孔，和人形态的大眼保持同一套读法）
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.14f;
                var eyeBase = CreateSphere(bodyGo.transform, s < 0 ? "Eye_L" : "Eye_R",
                    new Vector3(x, 0.05f, 0.06f),
                    new Vector3(0.115f, 0.095f, 0.115f),
                    p.White);

                CreateSphere(eyeBase.transform, "Pupil",
                    new Vector3(0f, 0.15f, 0.42f),
                    new Vector3(0.55f, 0.55f, 0.45f),
                    p.Dark);
            }

            // 3. 拖尾游动触须（保留节点名称与位置，略微加粗）
            var tentRootGo = new GameObject("TrailingTentacles");
            tentRootGo.transform.SetParent(bodyGo.transform, false);
            tentRootGo.transform.localPosition = new Vector3(0f, 0f, -0.22f);
            squidTentacles = tentRootGo.transform;

            CreateCapsule(squidTentacles, "Tentacle_OutL", new Vector3(-0.14f, 0.01f, -0.16f), new Vector3(0.06f, 0.18f, 0.06f), p.SquidBody).transform.localRotation = Quaternion.Euler(85f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_MidL", new Vector3(-0.05f, 0.01f, -0.20f), new Vector3(0.055f, 0.22f, 0.055f), p.SquidBody).transform.localRotation = Quaternion.Euler(88f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_MidR", new Vector3(0.05f, 0.01f, -0.20f), new Vector3(0.055f, 0.22f, 0.055f), p.SquidBody).transform.localRotation = Quaternion.Euler(88f, 0f, 0f);
            CreateCapsule(squidTentacles, "Tentacle_OutR", new Vector3(0.14f, 0.01f, -0.16f), new Vector3(0.06f, 0.18f, 0.06f), p.SquidBody).transform.localRotation = Quaternion.Euler(85f, 0f, 0f);
        }

        /// <summary>
        /// 生成更胖更圆润的程序化流线型乌贼墨团网格。
        /// 长度约 0.52m，横向 rx 约 0.23m，竖向 ry 为 0.50*baseR。
        /// 18 环 × 24 径向段 + 平滑法线，避免低分段在墨团表面切出可见棱面。
        /// </summary>
        public static Mesh CreateSquidMesh()
        {
            if (_squidMesh != null) return _squidMesh;

            var mesh = new Mesh { name = "SquidMantleMesh" };

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            const int ringCount = 18;
            const int radialSegs = 24;

            for (int r = 0; r <= ringCount; r++)
            {
                float u = (float)r / ringCount; // 0 (前鼻尖) ~ 1 (尾部根)
                float z = Mathf.Lerp(0.30f, -0.22f, u); // 长度约 0.52m

                float baseR = Mathf.Sin(u * Mathf.PI * 0.85f) * 0.22f + 0.02f;
                float finFlare = Mathf.Clamp01(Mathf.Sin((u - 0.18f) * Mathf.PI * 1.4f)) * 0.08f;
                float rx = baseR + finFlare;
                float ry = baseR * 0.50f;

                for (int s = 0; s < radialSegs; s++)
                {
                    float angle = (float)s / radialSegs * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    float vx = cos * rx;
                    float vy = sin * ry;
                    if (vy < 0f) vy *= 0.75f;

                    verts.Add(new Vector3(vx, vy, z));
                    normals.Add(new Vector3(cos, sin * 1.6f, 0f).normalized);
                    uvs.Add(new Vector2((float)s / radialSegs, u));
                }
            }

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

            _squidMesh = mesh;
            return _squidMesh;
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
            shooter.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildShooterModel(shooter.transform, p, out mShooter);

            // 2. Swell (Roller 宽屏滚筒架)
            roller = new GameObject("Swell_Roller");
            roller.transform.SetParent(weaponRoot, false);
            roller.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildRollerModel(roller.transform, p, out rollerDrum, out mRoller);

            // 3. Glint (Charger 长导轨蓄力狙)
            charger = new GameObject("Glint_Charger");
            charger.transform.SetParent(weaponRoot, false);
            charger.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildChargerModel(charger.transform, p, out chargerCoils, out mCharger);

            // 4. Twinfin (Dualies 双枪)
            dualies = new GameObject("Twinfin_Dualies");
            dualies.transform.SetParent(weaponRoot, false);
            dualies.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildDualiesModel(dualies.transform, p, out mDualiesR, out mDualiesL);

            // 5. Tidebucket (Slosher 泼桶)
            slosher = new GameObject("Tidebucket_Slosher");
            slosher.transform.SetParent(weaponRoot, false);
            slosher.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildSlosherModel(slosher.transform, p, out mSlosher);

            // 6. Popper (Blaster 爆破枪)
            blaster = new GameObject("Popper_Blaster");
            blaster.transform.SetParent(weaponRoot, false);
            blaster.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildBlasterModel(blaster.transform, p, out mBlaster);

            // 7. Gyre (Splatling 加特林)
            splatling = new GameObject("Gyre_Splatling");
            splatling.transform.SetParent(weaponRoot, false);
            splatling.transform.localScale = Vector3.one * CharacterMetrics.WeaponModelScale;
            BuildSplatlingModel(splatling.transform, p, out splatlingBarrels, out mSplatling);
        }

        // 1. Spritzer 喷漆步枪
        private static void BuildShooterModel(Transform root, Palette p, out Transform muzzle)
        {
            root.localPosition = CharacterMetrics.WeaponMountOffset(0.24f, 0.80f, 0.32f);

            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.02f), new Vector3(0.08f, 0.10f, 0.28f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.06f, -0.06f), new Vector3(0.06f, 0.14f, 0.08f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(root, "InkSpine", new Vector3(0f, 0.095f, 0.02f), new Vector3(0.04f, 0.02f, 0.24f), p.TeamHair);

            CreateCylinder(root, "InkCanister", new Vector3(0f, 0.14f, -0.02f), new Vector3(0.09f, 0.12f, 0.09f), p.TeamLiquid).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
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
            root.localPosition = CharacterMetrics.WeaponMountOffset(0f, 0.72f, 0.30f);

            CreateCylinder(root, "Shaft", new Vector3(0f, 0f, 0.20f), new Vector3(0.045f, 0.40f, 0.045f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GripRubberRear", new Vector3(0f, 0f, -0.10f), new Vector3(0.06f, 0.12f, 0.06f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GripRubberFront", new Vector3(0f, 0f, 0.18f), new Vector3(0.06f, 0.12f, 0.06f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreateBox(root, "YokeHub", new Vector3(0f, 0f, 0.52f), new Vector3(0.14f, 0.10f, 0.10f), p.Dark);
            CreateBox(root, "InkReservoir", new Vector3(0f, 0.08f, 0.52f), new Vector3(0.18f, 0.07f, 0.12f), p.TeamLiquid);

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

            var drumGo = new GameObject("RollerDrum");
            drumGo.transform.SetParent(root, false);
            drumGo.transform.localPosition = new Vector3(0f, 0f, 0.52f);
            drum = drumGo.transform;

            var drumCyl = CreateCylinder(drum, "DrumMesh", Vector3.zero, new Vector3(0.24f, 0.60f, 0.24f), p.TeamLiquid);
            drumCyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

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
            root.localPosition = CharacterMetrics.WeaponMountOffset(0.24f, 0.80f, 0.30f);

            CreateBox(root, "Stock", new Vector3(0f, 0.02f, -0.22f), new Vector3(0.06f, 0.10f, 0.22f), p.Dark);
            CreateBox(root, "ButtPad", new Vector3(0f, 0.02f, -0.34f), new Vector3(0.07f, 0.14f, 0.03f), p.Dark);

            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.04f), new Vector3(0.075f, 0.10f, 0.32f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.06f, -0.04f), new Vector3(0.055f, 0.14f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            CreateCylinder(root, "Scope", new Vector3(0f, 0.13f, 0.04f), new Vector3(0.055f, 0.18f, 0.055f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "ScopeLens", new Vector3(0f, 0.13f, 0.20f), new Vector3(0.065f, 0.02f, 0.065f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreateCylinder(root, "LongBarrel", new Vector3(0f, 0.04f, 0.45f), new Vector3(0.038f, 0.42f, 0.038f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "MuzzleBrake", new Vector3(0f, 0.04f, 0.86f), new Vector3(0.052f, 0.05f, 0.052f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

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

            var rightPistol = new GameObject("RightPistol");
            rightPistol.transform.SetParent(root, false);
            rightPistol.transform.localPosition = CharacterMetrics.WeaponMountOffset(0.24f, 0.82f, 0.30f);
            BuildSinglePistol(rightPistol.transform, p, out muzzleR);

            var leftPistol = new GameObject("LeftPistol");
            leftPistol.transform.SetParent(root, false);
            leftPistol.transform.localPosition = CharacterMetrics.WeaponMountOffset(-0.24f, 0.82f, 0.30f);
            BuildSinglePistol(leftPistol.transform, p, out muzzleL);
        }

        private static void BuildSinglePistol(Transform pistolRoot, Palette p, out Transform muzzle)
        {
            CreateBox(pistolRoot, "Frame", new Vector3(0f, 0.02f, 0.02f), new Vector3(0.065f, 0.07f, 0.22f), p.Dark);
            CreateBox(pistolRoot, "Grip", new Vector3(0f, -0.06f, -0.04f), new Vector3(0.055f, 0.12f, 0.065f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(pistolRoot, "Slide", new Vector3(0f, 0.07f, 0.02f), new Vector3(0.065f, 0.045f, 0.23f), p.White);
            CreateBox(pistolRoot, "DorsalFin", new Vector3(0f, 0.10f, 0.04f), new Vector3(0.025f, 0.02f, 0.10f), p.TeamHair);

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
            root.localPosition = CharacterMetrics.WeaponMountOffset(0.12f, 0.76f, 0.32f);

            CreateCylinder(root, "BucketPail", new Vector3(0f, 0.05f, 0.10f), new Vector3(0.34f, 0.15f, 0.34f), p.White);
            CreateCylinder(root, "ReinforceHoopTop", new Vector3(0f, 0.16f, 0.10f), new Vector3(0.36f, 0.025f, 0.36f), p.Dark);
            CreateCylinder(root, "ReinforceHoopBottom", new Vector3(0f, -0.06f, 0.10f), new Vector3(0.30f, 0.025f, 0.30f), p.Dark);

            CreateCylinder(root, "InkLiquidSurface", new Vector3(0f, 0.12f, 0.10f), new Vector3(0.31f, 0.03f, 0.31f), p.TeamLiquid);
            CreateBox(root, "PourLip", new Vector3(0f, 0.16f, 0.28f), new Vector3(0.18f, 0.04f, 0.10f), p.White).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            CreateBox(root, "HandleRear", new Vector3(0f, 0.08f, -0.10f), new Vector3(0.06f, 0.22f, 0.06f), p.Dark);
            CreateBox(root, "HandleLever", new Vector3(0f, 0.20f, -0.06f), new Vector3(0.05f, 0.03f, 0.08f), p.Metal);
            CreateBox(root, "CarryBar", new Vector3(0f, -0.12f, 0.08f), new Vector3(0.26f, 0.045f, 0.045f), p.Dark);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.16f, 0.34f);
            muzzle = muz.transform;
        }

        // 6. Popper 爆破枪
        private static void BuildBlasterModel(Transform root, Palette p, out Transform muzzle)
        {
            root.localPosition = CharacterMetrics.WeaponMountOffset(0.24f, 0.80f, 0.32f);

            CreateBox(root, "Receiver", new Vector3(0f, 0.04f, 0.02f), new Vector3(0.12f, 0.12f, 0.28f), p.White);
            CreateBox(root, "Grip", new Vector3(0f, -0.07f, -0.06f), new Vector3(0.06f, 0.15f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            CreateSphere(root, "PressureBulb", new Vector3(0f, 0.06f, 0.08f), new Vector3(0.18f, 0.18f, 0.18f), p.TeamLiquid);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f;
                var rib = CreateBox(root, "BulbRib_" + k, new Vector3(0f, 0.06f, 0.08f), new Vector3(0.20f, 0.025f, 0.19f), p.White);
                rib.transform.localRotation = Quaternion.Euler(0f, 0f, a);
            }

            CreateCylinder(root, "PressureGauge", new Vector3(0f, 0.08f, -0.12f), new Vector3(0.08f, 0.02f, 0.08f), p.Metal).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "GaugeDial", new Vector3(0f, 0.08f, -0.13f), new Vector3(0.065f, 0.015f, 0.065f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateBox(root, "GaugeNeedle", new Vector3(0f, 0.09f, -0.14f), new Vector3(0.01f, 0.04f, 0.005f), p.Dark);

            CreateCylinder(root, "HornBase", new Vector3(0f, 0.06f, 0.24f), new Vector3(0.11f, 0.10f, 0.11f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "BellMuzzle", new Vector3(0f, 0.06f, 0.36f), new Vector3(0.22f, 0.08f, 0.22f), p.White).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "BellRim", new Vector3(0f, 0.06f, 0.42f), new Vector3(0.24f, 0.025f, 0.24f), p.TeamHair).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreateCylinder(root, "PumpSlide", new Vector3(0f, -0.04f, 0.18f), new Vector3(0.07f, 0.12f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var muz = new GameObject("Muzzle");
            muz.transform.SetParent(root, false);
            muz.transform.localPosition = new Vector3(0f, 0.06f, 0.45f);
            muzzle = muz.transform;
        }

        // 7. Gyre 加特林机枪
        private static void BuildSplatlingModel(Transform root, Palette p, out Transform barrelsCluster, out Transform muzzle)
        {
            root.localPosition = CharacterMetrics.WeaponMountOffset(0.24f, 0.78f, 0.28f);

            CreateBox(root, "MotorHousing", new Vector3(0f, 0.04f, 0.06f), new Vector3(0.16f, 0.16f, 0.28f), p.White);
            CreateBox(root, "RearGrip", new Vector3(0f, -0.06f, -0.06f), new Vector3(0.06f, 0.14f, 0.07f), p.Dark).transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            CreateBox(root, "ForwardVerticalGrip", new Vector3(0f, -0.10f, 0.16f), new Vector3(0.05f, 0.14f, 0.05f), p.Dark);

            CreateCylinder(root, "TopInkDrum", new Vector3(0f, 0.16f, 0.04f), new Vector3(0.14f, 0.14f, 0.14f), p.TeamLiquid).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "DrumCapFront", new Vector3(0f, 0.16f, 0.17f), new Vector3(0.15f, 0.02f, 0.15f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            CreateCylinder(root, "DrumCapRear", new Vector3(0f, 0.16f, -0.09f), new Vector3(0.15f, 0.02f, 0.15f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var clusterGo = new GameObject("RotatingBarrelsCluster");
            clusterGo.transform.SetParent(root, false);
            clusterGo.transform.localPosition = new Vector3(0f, 0.04f, 0.20f);
            barrelsCluster = clusterGo.transform;

            CreateCylinder(barrelsCluster, "CenterSpindle", new Vector3(0f, 0f, 0.20f), new Vector3(0.035f, 0.38f, 0.035f), p.Dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

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

        #region Weapon Static Mesh Merge

        /// <summary>合并网格物体命名前缀：ApplyShadowCastingRules 遇到该前缀直接跳过（阴影行为已在合并时按组内部件多数决定）。</summary>
        private const string MergedPartPrefix = "MergedStatic_";

        /// <summary>每类武器的合并根数量上限：0 = 武器主根，1 = 动态构件簇（滚筒滚轴 / 加特林枪管簇）。</summary>
        private const int MaxMergeRootsPerWeapon = 2;

        /// <summary>
        /// 合并网格静态缓存：7 类武器几何完全确定性（同一构建代码 + 固定 Primitive 组合），
        /// 合并网格只含几何不含材质，可跨实例 / 跨队伍复用。
        /// 结构：武器种类 → 合并根序号 → 材质组网格列表（null 项表示该组校验失败、维持原部件独立渲染）。
        /// 缓存网格装配时一次性生成、永不销毁（随会话存续，Domain Reload 自动清空），
        /// 避免逐实例重建网格在换枪/重装配时泄漏。
        /// </summary>
        private static readonly Dictionary<WeaponKind, List<Mesh>[]> _mergedMeshCache =
            new Dictionary<WeaponKind, List<Mesh>[]>();

        /// <summary>
        /// 对 7 类武器执行「静态构件网格合并」：
        /// 1. 只合并相对合并根刚体静止、不参与逐帧动画 / MPB 特殊属性的 MeshRenderer；
        /// 2. 按 sharedMaterial 引用分组，用 CombineMeshes 把部件按「相对合并根的局部矩阵」烘焙成组网格；
        /// 3. 源部件仅禁用渲染（GameObject / MeshFilter / 测试锚点层级全部保留）；
        /// 4. 滚筒滚轴与加特林枪管簇是动态枢轴：整棵子树跳过后作为二级合并根单独烘焙，
        ///    簇内构件相对枢轴刚体静止，合并网格挂在枢轴下随其整体旋转，动画表现完全不变。
        /// </summary>
        private static void MergeWeaponStaticMeshes(
            GameObject shooter, GameObject roller, GameObject charger, GameObject dualies,
            GameObject slosher, GameObject blaster, GameObject splatling,
            Transform rollerDrum, Transform splatlingBarrels, Renderer[] chargerCoils)
        {
            // 1. Spritzer：全部构件相对枪根静止（枪口锚点无渲染器，自然保留）
            MergeWeaponRoot(AsTransform(shooter), WeaponKind.Shooter, 0, null, null);

            // 2. Swell：滚筒滚轴是测试锚点 + 旋转动画，整棵子树跳过后作为二级合并根单独烘焙
            MergeWeaponRoot(AsTransform(roller), WeaponKind.Roller, 0, ToArray(rollerDrum), null);
            MergeWeaponRoot(rollerDrum, WeaponKind.Roller, 1, null, null);

            // 3. Glint：充能线圈逐帧写自发光 MaterialPropertyBlock，必须保留独立渲染器；其余部件全部静止
            MergeWeaponRoot(AsTransform(charger), WeaponKind.Charger, 0, null, chargerCoils);

            // 4. Twinfin：两把手枪整体静止，跨子根合并（烘焙矩阵含手枪挂点偏移；枪口锚点自然保留）
            MergeWeaponRoot(AsTransform(dualies), WeaponKind.Dualies, 0, null, null);

            // 5. Tidebucket / Popper：全部构件静止
            MergeWeaponRoot(AsTransform(slosher), WeaponKind.Slosher, 0, null, null);
            MergeWeaponRoot(AsTransform(blaster), WeaponKind.Blaster, 0, null, null);

            // 6. Gyre：加特林枪管簇同滚筒处理
            MergeWeaponRoot(AsTransform(splatling), WeaponKind.Splatling, 0, ToArray(splatlingBarrels), null);
            MergeWeaponRoot(splatlingBarrels, WeaponKind.Splatling, 1, null, null);
        }

        /// <summary>
        /// 合并单个合并根下的静态部件：
        /// 收集 → 按 sharedMaterial 分组 → 生成/复用缓存网格 → 挂载 MergedStatic_* 物体 → 禁用组内源部件渲染。
        /// 只有组网格构建成功才禁用该组源部件，任何失败都让该组维持原状（降级而非缺件）。
        /// </summary>
        private static void MergeWeaponRoot(
            Transform bakeRoot,
            WeaponKind kind,
            int mergeIndex,
            Transform[] skipSubtrees,
            Renderer[] skipRenderers)
        {
            if (bakeRoot == null || mergeIndex < 0 || mergeIndex >= MaxMergeRootsPerWeapon) return;

            // 1. 深度优先收集可合并部件（遍历顺序确定 → 材质组顺序确定 → 缓存组序跨队伍/实例一致）
            var parts = new List<MeshRenderer>();
            CollectMergeableParts(bakeRoot, skipSubtrees, skipRenderers, parts);
            if (parts.Count == 0) return;

            // 2. 按 sharedMaterial 引用分组（保持首次出现顺序）
            var groupMats = new List<Material>();
            var groups = new List<List<MeshRenderer>>();
            for (int i = 0; i < parts.Count; i++)
            {
                Material mat = parts[i].sharedMaterial;
                if (mat == null) continue;
                int gi = -1;
                for (int m = 0; m < groupMats.Count; m++)
                {
                    if (ReferenceEquals(groupMats[m], mat)) { gi = m; break; }
                }
                if (gi < 0)
                {
                    gi = groupMats.Count;
                    groupMats.Add(mat);
                    groups.Add(new List<MeshRenderer>());
                }
                groups[gi].Add(parts[i]);
            }
            if (groups.Count == 0) return;

            // 3. 生成 / 复用缓存网格（网格几何只取决于部件集合与矩阵，与材质实例无关 → 缓存 key 无需含队伍色）
            if (!_mergedMeshCache.TryGetValue(kind, out var perRoot) || perRoot == null
                || perRoot.Length != MaxMergeRootsPerWeapon)
            {
                perRoot = new List<Mesh>[MaxMergeRootsPerWeapon];
                _mergedMeshCache[kind] = perRoot;
            }
            List<Mesh> cachedGroups = perRoot[mergeIndex];
            bool reuseCache = cachedGroups != null && cachedGroups.Count == groups.Count;

            var builtMeshes = new Mesh[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                if (reuseCache)
                {
                    builtMeshes[g] = cachedGroups[g]; // 可能含 null（该组校验失败保持不合并）
                    continue;
                }
                builtMeshes[g] = BuildMergedGroupMesh(kind, mergeIndex, g, bakeRoot, groups[g]);
            }
            if (!reuseCache)
            {
                perRoot[mergeIndex] = new List<Mesh>(builtMeshes);
            }

            // 4. 挂载合并网格物体并禁用组内源部件渲染（GameObject / 锚点层级全部保留）
            for (int g = 0; g < groups.Count; g++)
            {
                Mesh mergedMesh = builtMeshes[g];
                if (mergedMesh == null) continue;
                var members = groups[g];

                var mergedGo = new GameObject(MergedPartPrefix + groupMats[g].name);
                mergedGo.transform.SetParent(bakeRoot, false);
                mergedGo.AddComponent<MeshFilter>().sharedMesh = mergedMesh;
                var mergedRend = mergedGo.AddComponent<MeshRenderer>();
                mergedRend.sharedMaterial = groupMats[g];

                // 阴影行为按组内「多数部件」决定（ShouldCastShadow 是装配期唯一真源，此时逐件裁剪尚未应用）；
                // 平票时保守保留投影。微件（握把/轴承等）随大网格获得投影属预期内的轻微增益。
                int castCount = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    if (ShouldCastShadow(members[i].gameObject.name)) castCount++;
                }
                mergedRend.shadowCastingMode = castCount * 2 >= members.Count
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;

                for (int i = 0; i < members.Count; i++)
                {
                    members[i].enabled = false;
                }
            }
        }

        /// <summary>
        /// 深度优先收集可合并部件：MeshFilter+MeshRenderer 齐全且网格/材质非空；
        /// 跳过动态子树（滚筒/枪管簇等）、MPB 动画渲染器（蓄力线圈）、枪口锚点子树与已合并对象。
        /// </summary>
        private static void CollectMergeableParts(
            Transform current,
            Transform[] skipSubtrees,
            Renderer[] skipRenderers,
            List<MeshRenderer> results)
        {
            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                if (skipSubtrees != null && ContainsReference(skipSubtrees, child)) continue;
                if (child.name.StartsWith(MergedPartPrefix)) continue; // 防御：避免重复收集已合并对象
                if (child.name == "Muzzle") continue;                  // 枪口锚点子树（当前无渲染器，防御性跳过）

                var rend = child.GetComponent<MeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();
                if (rend != null && filter != null && filter.sharedMesh != null && rend.sharedMaterial != null)
                {
                    if (skipRenderers == null || !ContainsReference(skipRenderers, rend))
                    {
                        results.Add(rend);
                    }
                }

                // 当前部件均为叶子，防御性支持未来嵌套部件结构
                CollectMergeableParts(child, skipSubtrees, skipRenderers, results);
            }
        }

        /// <summary>
        /// 把同材质组内的部件按「相对合并根的局部矩阵」烘焙合并成一个网格。
        /// 所有部件均为 Unity Primitive（Cube/Sphere/Cylinder），顶点属性布局天然统一；
        /// 若组内出现属性不一致 / 多 submesh 的网格则放弃合并（返回 null），该组维持原部件独立渲染。
        /// </summary>
        private static Mesh BuildMergedGroupMesh(
            WeaponKind kind,
            int mergeIndex,
            int groupIndex,
            Transform bakeRoot,
            List<MeshRenderer> members)
        {
            // 1. 顶点属性布局一致性校验（防御：未来若混入非 Primitive 网格则整组放弃合并）
            Mesh firstMesh = members[0].GetComponent<MeshFilter>().sharedMesh;
            if (firstMesh == null || firstMesh.subMeshCount != 1)
            {
                Debug.LogWarning($"[CharacterVisualAssembler] 武器 {kind} 材质组 {groupIndex} 首个网格无效或多 submesh，放弃合并该组");
                return null;
            }
            bool needNormals = firstMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal);
            bool needUv = firstMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
            bool needTangents = firstMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
            for (int i = 1; i < members.Count; i++)
            {
                Mesh mesh = members[i].GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.subMeshCount != 1 ||
                    mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal) != needNormals ||
                    mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0) != needUv ||
                    mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) != needTangents)
                {
                    Debug.LogWarning($"[CharacterVisualAssembler] 武器 {kind} 材质组 {groupIndex} 顶点属性不一致，放弃合并该组（保持独立渲染）");
                    return null;
                }
            }

            // 2. 烘焙合并：矩阵 = bakeRoot.worldToLocal × part.localToWorld（部件在合并根局部空间中的位姿），
            //    合并物体随后以单位变换挂在合并根下，最终世界变换与原部件完全一致。
            var combine = new CombineInstance[members.Count];
            Matrix4x4 rootWorldToLocal = bakeRoot.worldToLocalMatrix;
            for (int i = 0; i < members.Count; i++)
            {
                combine[i].mesh = members[i].GetComponent<MeshFilter>().sharedMesh;
                combine[i].transform = rootWorldToLocal * members[i].transform.localToWorldMatrix;
            }

            var merged = new Mesh { name = "MergedWeapon_" + kind + "_R" + mergeIndex + "_G" + groupIndex };
            try
            {
                merged.CombineMeshes(combine, true, true);
            }
            catch (System.Exception e)
            {
                Object.DestroyImmediate(merged);
                Debug.LogWarning($"[CharacterVisualAssembler] CombineMeshes 失败（{kind} 根 {mergeIndex} 组 {groupIndex}）：{e.Message}，该组保持独立渲染");
                return null;
            }

            // 3. 非等比缩放烘焙后法线/切线模长会漂移（部件均为轴向缩放，方向不受影响），
            //    统一归一化，避免卡通着色出现明暗斑块。
            NormalizeMergedVectors(merged, needNormals, needTangents);
            merged.RecalculateBounds();
            return merged;
        }

        /// <summary>烘焙合并后统一归一化法线与切线（方向由 CombineMeshes 变换，模长受非等比缩放漂移）。</summary>
        private static void NormalizeMergedVectors(Mesh mesh, bool hasNormals, bool hasTangents)
        {
            if (hasNormals)
            {
                var normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 n = normals[i];
                    float mag = n.magnitude;
                    normals[i] = mag > 1e-5f ? n / mag : Vector3.up;
                }
                mesh.normals = normals;
            }

            if (hasTangents)
            {
                var tangents = mesh.tangents;
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector4 t = tangents[i];
                    Vector3 xyz = new Vector3(t.x, t.y, t.z);
                    float mag = xyz.magnitude;
                    if (mag > 1e-5f) xyz /= mag;
                    tangents[i] = new Vector4(xyz.x, xyz.y, xyz.z, t.w);
                }
                mesh.tangents = tangents;
            }
        }

        private static Transform AsTransform(GameObject go) => go != null ? go.transform : null;

        private static Transform[] ToArray(Transform t) => t != null ? new[] { t } : null;

        private static bool ContainsReference(Transform[] array, Transform t)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (ReferenceEquals(array[i], t)) return true;
            }
            return false;
        }

        private static bool ContainsReference(Renderer[] array, Renderer r)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (ReferenceEquals(array[i], r)) return true;
            }
            return false;
        }

        #endregion
    }
}
