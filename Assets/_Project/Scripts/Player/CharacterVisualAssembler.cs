using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// INKWAVE 原版比例的 squidkid 角色（P33 骨骼重建）与 7 类主武器装配工厂。
    /// 遵循《P1 项目约定》与 CharacterMetrics 唯一尺寸真源：
    /// 1. 人形态 = ~4 头身真骨骼：馒头头（mochi head）独立挂在 Neck→Head 骨上，
    ///    躯干链 Hips→BodySquash→Spine→Chest（前倾按 25/45/30 分配）、两骨臂（肩→肘→腕）、
    ///    两骨腿（髋→膝→踝），由 KidGaitSolver 驱动相位步态与骨盆贴地（参考 INKWAVE character-geo.js 骨位表）；
    /// 2. 总视觉包络不变（脚底 0 → 头顶 1.12m）：碰撞体 / 相机 / 遮挡采样 / 武器标定零玩法改动；
    /// 3. 服装按 INKWAVE 色彩规则：盒型 T 恤 + 灯笼短裤 + 中筒袜 + 厚底板鞋为中性色，
    ///    队伍色集中在触须发（6 根 2 段弹簧链）、发帽、腰带、墨罐液面与鞋头小饰片；
    /// 4. 蛋体时代的贴面薄壳（短裤/发带/耳机头带壳）全部废弃：真骨骼 + 真体积不再需要贴面工艺；
    /// 5. 7 套武器模型的握把按右手手位锚定（P34 两骨 IK 反解持握姿态），并绑定动态构件与枪口；
    /// 6. 武器静态构件在装配时按材质合并为少量 MergedStatic_* 网格压缩 DrawCall：
    ///    源部件 GameObject / MeshFilter / 测试锚点层级全部保留、仅禁用渲染；
    ///    动态构件（滚筒滚轴、加特林枪管簇、蓄力线圈）与枪口锚点不参与合并。
    /// </summary>
    public static class CharacterVisualAssembler
    {
        private static Mesh _mochiHeadMesh;
        private static Mesh _scalpCapMesh;
        private static Mesh _teeMesh;
        private static Mesh _shortsMesh;
        private static Mesh _waistbandMesh;
        private static Mesh _hairSeg1Mesh;
        private static Mesh _hairSeg2Mesh;
        private static Mesh _squidMesh;

        /// <summary>清空静态缓存的程序化网格与材质，供编辑器刷新与参数重载。</summary>
        public static void ClearCaches()
        {
            _mochiHeadMesh = null;
            _scalpCapMesh = null;
            _teeMesh = null;
            _shortsMesh = null;
            _waistbandMesh = null;
            _hairSeg1Mesh = null;
            _hairSeg2Mesh = null;
            _squidMesh = null;
            _mergedMeshCache.Clear();
        }

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

            // 3. 构建人形态 (Kid Root)：P33 真骨骼树（Hips→BodySquash→Spine→Chest→Neck→Head）
            var kidGo = new GameObject("KidRoot");
            kidGo.transform.SetParent(visualRoot, false);

            Transform hips, chest, head, leftHair, rightHair, leftArm, rightArm, tankFill;
            Transform bodySquash, leftEye, rightEye, face;
            Transform leftLegRoot, rightLegRoot, leftKnee, rightKnee, leftAnkle, rightAnkle;
            Transform leftElbow, rightElbow;
            Transform spine, neck;
            Transform[] hairRootPivots, hairMidPivots;
            BuildKidHierarchy(kidGo.transform, palette,
                out hips, out chest, out head,
                out leftHair, out rightHair, out leftArm, out rightArm, out tankFill,
                out bodySquash, out leftEye, out rightEye, out face,
                out leftLegRoot, out rightLegRoot, out leftKnee, out rightKnee, out leftAnkle, out rightAnkle,
                out leftElbow, out rightElbow,
                out spine, out neck, out hairRootPivots, out hairMidPivots);

            // 4. 构建乌贼形态 (Squid Root)
            var squidGo = new GameObject("SquidRoot");
            squidGo.transform.SetParent(visualRoot, false);
            squidGo.SetActive(false);

            Transform squidBody, squidTentacles;
            Transform[] tentacleArray;
            BuildSquidHierarchy(squidGo.transform, palette, out squidBody, out squidTentacles, out tentacleArray);

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

            // 5.1 武器挂到胸腔骨 (Chest) 下：抵消 Hips+BodySquash+Spine+Chest 的链上偏移，
            //     使武器在 KidRoot 空间的绝对位置等于下方握把锚点（前倾时枪随上身自然走）。
            //     挤压节点的缩放由控制器逐帧在武器根上反缩放抵消（枪不被 squash 压扁）。
            weaponGo.transform.SetParent(chest, false);
            float chainY = CharacterMetrics.HipBoneY
                + CharacterMetrics.SpineLocalY + CharacterMetrics.ChestLocalY;
            weaponGo.transform.localPosition = new Vector3(0f, -chainY, 0f);

            // 5.1.5 握把锚定 + 每把武器的手臂持握姿态（P34）：
            //      旧版挂点按 1.6m 人形缩放后落在腰前 ~30cm，而手臂总长只有 0.325m，
            //      枪其实漂在手的正前方 5~8cm 处，肩/肘角度再准也握不到。
            //      现在先定义"右手手位"，再把每把武器的握把（Grip/Handle 部件）平移到该手位，
            //      最后用两骨 IK 反解肩/肘 —— 手与握把在装配期就严格重合。
            Dictionary<WeaponKind, ArmHoldPose> holdPoses = ResolveWeaponHoldPoses(
                weaponGo.transform, leftArm, rightArm, leftElbow, rightElbow,
                shooterGo, rollerGo, chargerGo, dualiesGo, slosherGo, blasterGo, splatlingGo,
                chainY);

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

            // 6.1 注入真骨骼步态与头部稳定绑定。P33：Face 容器直接由装配出参传入（旧版靠路径 Find
            //     "Chest/BodySquash/Torso/Face"，骨骼树重建后该路径不再成立）。
            controller.BindEggRig(bodySquash, face, leftEye, rightEye);

            // 6.1.1 绑定双腿两骨链（髋→膝→踝）：步态摆腿 / 滞空收腿 / 落地屈膝吸收
            controller.BindLegRig(hips, leftLegRoot, rightLegRoot, leftKnee, rightKnee, leftAnkle, rightAnkle);

            // 6.1.2 绑定二段臂肘枢轴：步态屈肘 / 持枪基础弯 / 落地伸肘
            controller.BindArmRig(leftElbow, rightElbow);

            // 6.1.3 绑定脊柱链（Spine/Neck/Head）与 6 根触须发弹簧链（根段 + 中段枢轴）
            controller.BindKidRig(spine, neck, head, face, hairRootPivots, hairMidPivots);

            // 6.2 绑定 4 根拖尾触须（逐根行波驱动；数组为空时控制器回退根节点整体摆动）
            controller.BindSquidTentacles(tentacleArray);

            // 6.3 注入每把武器的持握姿态：切枪时左手托握/双持、右手握把与肘部角度整体切换，
            //     保证"枪在哪只手就在哪"，而不是每把武器共用一套按旧挂点写的固定角度。
            controller.BindArmHoldPoses(holdPoses);

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
            /// <summary>墨罐罐盖专用哑光金属：不用枪械金属的紧凑高光，防止阳光角度对上时罐顶闪白。</summary>
            public Material TankMetal;
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

            // T 恤主体为奶油白（唯一大面积衣物剪影）；腰带是队伍色窄环、短裤锚在骨盆（P33 真体积）。
            // 停用反向外扩描边（outlined: false），全屏 ToonOutline 已提供清晰锐利轮廓，杜绝单面开放筒体反向外扩破面与 Z-fighting。
            p.Shirt = CreateCharacterMat("Mat_CharacterShirt", shirtColor, 0.35f, 0.15f, outlined: false, shadeSoftness: 0.10f);
            p.Sleeve = CreateCharacterMat("Mat_CharacterSleeve", shirtColor, 0.35f, 0.15f, shadeSoftness: 0.08f);
            p.Shorts = CreateCharacterMat("Mat_CharacterShorts", shortsColor, 0.35f, 0.15f, shadeSoftness: 0.08f);
            p.Waistband = CreateCharacterMat("Mat_CharacterWaistband", teamCol, 0.45f, 0.20f, shadeSoftness: 0.06f);
            p.TeamCloth = p.Shirt;

            // 队伍墨色部件：墨焰/瞳孔/腕带共用；胸章使用哑光 InkDecal，墨罐液面单独带一点自发光。
            // 触须发采用柔和胶质卡通参数（smoothness 0.45、rim 0.22），杜绝极硬高光与强边缘光在曲面微晃时产生的闪烁。
            p.TeamHair = CreateCharacterMat("Mat_TeamHair", teamCol, 0.45f, 0.22f, shadeSoftness: 0.06f);
            p.InkDecal = CreateCharacterMat("Mat_TeamInkDecal", teamCol, 0.18f, 0.12f, shadeSoftness: 0.06f);
            p.TeamLiquid = CreateCharacterMat("Mat_TeamLiquid", teamCol, 0.50f, 0.35f, emission: teamCol * 0.20f);
            p.SquidBody = CreateCharacterMat("Mat_SquidBody", teamCol, 0.65f, 0.35f);

            p.Skin = CreateCharacterMat("Mat_KidSkin", new Color(0.96f, 0.73f, 0.61f), 0.25f, 0.12f);
            p.Dark = CreateCharacterMat("Mat_DarkPlastic", darkColor, 0.40f, 0.25f);
            p.White = CreateCharacterMat("Mat_AccentsWhite", new Color(0.95f, 0.96f, 0.98f), 0.40f, 0.30f);
            p.Metal = CreateCharacterMat("Mat_Gunmetal", new Color(0.55f, 0.58f, 0.65f), 0.65f, 0.40f);
            p.TankMetal = CreateCharacterMat("Mat_TankMetal", new Color(0.55f, 0.58f, 0.65f), 0.22f, 0.25f);
            p.Blush = CreateCharacterMat("Mat_Blush", new Color(0.98f, 0.62f, 0.60f), 0.20f, 0.15f);

            // 墨罐玻璃必须保留 URP Lit 透明表面材质：卡通 Shader 不支持透明通道，否则墨罐外壳会变为不透明阻挡内部液面。
            // 但不能沿用 0.95 的镜面级光滑度：近白基色 + 紧凑 GGX 高光/天空反射在阳光下会被 Bloom 烧成一团白光，
            // 角色转身时偶尔对上角度就闪一下（表现为"后背墨罐上闪白光"）。基色/alpha 保持原值以复用同名材质资产，
            // 只把光滑度压哑并关掉镜面高光与环境反射。
            p.Glass = CreateLitMat("Mat_TankGlass", new Color(0.85f, 0.92f, 1.0f, 0.45f), TankGlassSmoothness, 0f);
            DisableTankGlassGlint(p.Glass);

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

        /// <summary>墨罐玻璃粗糙度：保留玻璃质感，但不产生会被 Bloom 放大的紧凑 HDR 高光。</summary>
        private const float TankGlassSmoothness = 0.20f;

        /// <summary>
        /// 墨罐玻璃关掉镜面高光与环境反射（URP Lit 用局部关键词控制）。
        /// 只调 smoothness 不够：阳光角度对上时 GGX 高光峰值仍会远超 Bloom 阈值，在角色后背烧出一团白光。
        /// </summary>
        private static void DisableTankGlassGlint(Material mat)
        {
            if (mat == null) return;

            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
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
            // 关闭面部贴片、细饰边、背包外露附件、手部小件与武器零件阴影。
            // P33 真骨骼：主体大件 = 头/发帽/触须/T恤/短裤/大腿/小腿/球鞋壳/墨罐罐体，全部保留投影。
            if (partName.StartsWith("EyeMask") ||
                partName.StartsWith("Eye_") ||
                partName == "Iris" ||
                partName == "Pupil" ||
                partName.StartsWith("Glint") ||
                partName.StartsWith("Blush") ||
                partName == "Mouth" ||
                partName == "Nose" ||
                partName.StartsWith("Ear_") ||
                partName.StartsWith("Brow_") ||
                partName.StartsWith("ChestInk") ||
                // 腰带是 ~8mm 饰带、袜口是贴腿小环：主体 T 恤 / 小腿已投出相同轮廓，
                // 重复投影只增加阴影 draw，并在细级联下放大自阴影噪声。
                partName == "Waistband" ||
                partName == "SockCuff_L" ||
                partName == "SockCuff_R" ||
                // 球鞋分层件（白中底/鞋头包边/后跟饰片）随 Foot 鞋壳轮廓投影即可
                partName == "SoleMid" ||
                partName == "ToeCap" ||
                partName == "HeelTab_L" ||
                partName == "HeelTab_R" ||
                partName == "TankStrap_L" ||
                partName == "TankStrap_R" ||
                partName == "TopCap" ||
                partName == "ValveKnob" ||
                partName == "BottomCap" ||
                partName == "MountPlate" ||
                partName == "Cuff" ||
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

        #region Squidkid Procedural Meshes (P33)

        // 解析软最小/最大（Inigo Quilez 多项式版）：用于头部轮廓分段混合，交界 C1 圆滑无折痕。
        private static float SmoothMin(float a, float b, float h)
        {
            float t = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(0.0001f, h));
            return Mathf.Lerp(b, a, t) - h * t * (1f - t);
        }

        private static float SmoothMax(float a, float b, float h) => -SmoothMin(-a, -b, h);

        /// <summary>
        /// 馒头头（mochi head）解析面采样：椭球基面 (Rx,Ry,Rz) 上加 INKWAVE 三处雕塑特征——
        /// 颊线最宽（眼下 ~0.02 高度鼓一点）、小下巴（前下方快速收窄收小）、额后倾（顶前缘 −Z 收）。
        /// dir = 单位方向向量（+Z 为脸朝向）。返回该方向上的表面点（头心局部坐标）。
        /// </summary>
        internal static Vector3 SampleMochiHeadPoint(Vector3 dir, float outOffset)
        {
            dir = dir.normalized;
            float rx = CharacterMetrics.HeadHalfWidth;
            float ry = CharacterMetrics.HeadHalfHeight;
            float rz = CharacterMetrics.HeadHalfDepth;

            // 基础椭球表面点（非单位球：dir 投影）
            Vector3 p = new Vector3(dir.x * rx, dir.y * ry, dir.z * rz);
            float x = dir.x, y = dir.y, z = dir.z;

            // 1. 小下巴：dir 处于前下象限时，横向 + 纵深快速收窄，收成小巧圆润的下颌
            if (y < -0.12f && z > 0f)
            {
                float chin01 = Mathf.InverseLerp(-0.12f, -0.95f, y);
                float chin = chin01 * chin01 * Mathf.Clamp01(z * 1.6f);
                p.x *= 1f - 0.42f * chin;
                p.z *= 1f - 0.30f * chin;
            }

            // 2. 颊线微鼓：眼下 ±0.03 高度带最宽（"mochi" 的压扁重心），宽度过渡平滑
            float cheekBand = Mathf.Exp(-Mathf.Pow((y - 0.02f) / 0.30f, 2f)) * Mathf.Clamp01(z * 1.5f);
            p.x *= 1f + 0.055f * cheekBand;

            // 3. 额后倾 + 脸平面略平：dir 上半前侧，把额头往 −Z 收、把鼻尖前的弧压平
            if (y > 0.25f && z > 0f)
            {
                float fore = Mathf.InverseLerp(0.25f, 0.95f, y) * Mathf.Clamp01(z * 1.4f);
                p.z -= 0.30f * rz * fore;
            }
            if (y > -0.35f && y < 0.35f && z > 0.45f)
            {
                // 正脸区（眼眶所在）略平：大眼贴片不骑在球面上
                p.z *= 1f - 0.075f * Mathf.Clamp01((z - 0.45f) / 0.55f);
            }

            // 外偏：沿近似法线（椭球缩放梯度）外移，保证贴片不陷入曲面
            Vector3 normal = new Vector3(p.x / (rx * rx), p.y / (ry * ry), p.z / (rz * rz));
            if (normal.sqrMagnitude < 1e-9f) normal = Vector3.forward;
            return p + normal.normalized * outOffset;
        }

        /// <summary>
        /// 创建并静态缓存 P33 馒头头网格（48×32 环面，解析雕塑函数见 <see cref="SampleMochiHeadPoint"/>）。
        /// 法线由网格重算（RecalculateNormals）：三处特征位移后解析法线不再成立，
        /// 高环密度下重算法线足够光滑（描边轮廓由 CharacterToonOutlined 边缘检测负责）。
        /// </summary>
        public static Mesh CreateMochiHeadMesh()
        {
            if (_mochiHeadMesh != null) return _mochiHeadMesh;

            const int latRings = 32;
            const int lonSegs = 48;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int r = 0; r <= latRings; r++)
            {
                float theta = Mathf.PI * (float)r / latRings; // 0 顶 → π 底
                float y = Mathf.Cos(theta);
                float ring = Mathf.Sin(theta);
                for (int s = 0; s <= lonSegs; s++)
                {
                    float phi = 2f * Mathf.PI * (float)s / lonSegs; // 0 = 前脸 +Z
                    Vector3 dir = new Vector3(ring * Mathf.Sin(phi), y, ring * Mathf.Cos(phi));
                    verts.Add(SampleMochiHeadPoint(dir, 0f));
                    normals.Add(dir); // 近似：随后 RecalculateNormals 覆盖
                    uvs.Add(new Vector2((float)s / lonSegs, (float)r / latRings));
                }
            }

            int row = lonSegs + 1;
            for (int r = 0; r < latRings; r++)
            {
                for (int s = 0; s < lonSegs; s++)
                {
                    int i0 = r * row + s;
                    int i1 = (r + 1) * row + s;
                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            }

            var mesh = new Mesh { name = "MochiHeadMesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();

            // 极点处三角退化可能产生零模长法线，统一做保底归一化
            Vector3[] computedNormals = mesh.normals;
            for (int i = 0; i < computedNormals.Length; i++)
            {
                if (computedNormals[i].sqrMagnitude < 0.01f)
                    computedNormals[i] = verts[i].normalized;
                else
                    computedNormals[i] = computedNormals[i].normalized;
            }
            mesh.SetNormals(computedNormals);
            mesh.RecalculateBounds();
            _mochiHeadMesh = mesh;
            return mesh;
        }

        /// <summary>
        /// 头皮帽（ScalpCap）：沿头部雕塑面外扩 6mm 的壳体，后脑深（极距 ~128°）、
        /// 前发际浅（~68°，露出额头与眉上皮肤），左右按方位角平滑过渡。
        /// 触须发根全部种在帽面上，读作"从头发里长出来"而不是漂浮贴片。
        /// </summary>
        public static Mesh CreateScalpCapMesh()
        {
            if (_scalpCapMesh != null) return _scalpCapMesh;

            const int lonSegs = 48;
            const int uRings = 32; // 对齐 MochiHead 的 32 环，杜绝多边形弦凹差造成的 Z-fighting 闪烁

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int r = 0; r <= uRings; r++)
            {
                float u = (float)r / uRings;
                for (int s = 0; s <= lonSegs; s++)
                {
                    float phi = 2f * Mathf.PI * (float)s / lonSegs;
                    float frontness = Mathf.Cos(phi); // 1 = 正脸方向
                    // 帽缘极角：前脸 68°（发际在额上）→ 后脑 126°（盖到枕部）
                    float thetaMax = Mathf.Lerp(Mathf.PI * 0.38f, Mathf.PI * 0.70f,
                        Mathf.SmoothStep(0f, 1f, (1f - frontness) * 0.5f));
                    float theta = u * thetaMax;
                    Vector3 dir = new Vector3(
                        Mathf.Sin(theta) * Mathf.Sin(phi),
                        Mathf.Cos(theta),
                        Mathf.Sin(theta) * Mathf.Cos(phi));
                    // 外扩 12mm 形成头发厚度与体量感，彻底避免待机 squash 呼吸缩放时与头骨互穿
                    verts.Add(SampleMochiHeadPoint(dir, 0.012f));
                }
            }

            int row = lonSegs + 1;
            for (int r = 0; r < uRings; r++)
            {
                for (int s = 0; s < lonSegs; s++)
                {
                    int i0 = r * row + s;
                    int i1 = (r + 1) * row + s;
                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            }

            var mesh = new Mesh { name = "ScalpCapMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _scalpCapMesh = mesh;
            return mesh;
        }

        /// <summary>
        /// 盒型 T 恤（INKWAVE "boxy kid tee"）：超椭圆环堆叠（指数 2.3，方中带圆真实衣物布量感）。
        /// 网格锚在 Spine 节点（世界 y = 0.605）：下摆 y -0.015（世界 0.590，紧扣腰带上沿）→
        /// 腰腹 0.050 → 胸膛 0.105 → 落肩 0.145 → 肩线 0.170（世界 0.775，与肩关节 0.762m 协调）。
        /// 顶端圆领向下挖弧 0.038m（前领窝世界 0.737m），开领口（openCaps: 3），露出一截清爽精神的肤色小脖子。
        /// </summary>
        public static Mesh CreateTeeMesh()
        {
            if (_teeMesh != null) return _teeMesh;

            var profile = new SuperellipseProfile(
                new float[] { -0.015f, 0.050f, 0.105f, 0.145f, 0.170f },
                new float[] { 0.100f, 0.108f, 0.116f, 0.124f, 0.078f },
                new float[] { 0.080f, 0.086f, 0.088f, 0.084f, 0.060f });
            _teeMesh = BuildSuperellipseTube(profile, "TeeMesh", 0.0f, 3, 2.3f, frontCollarDip: 0.038f);
            return _teeMesh;
        }

        /// <summary>
        /// 灯笼短裤（半筒，膝上）：原点 = Hips 节点（世界 0.520）。
        /// 上端埋进 T 恤下摆（交界处同超椭圆族不描边），中段胯部最宽，裤脚微收形成灯笼感。
        /// </summary>
        public static Mesh CreateShortsMesh()
        {
            if (_shortsMesh != null) return _shortsMesh;

            var profile = new SuperellipseProfile(
                new float[] { 0.125f, 0.080f, 0.000f, -0.060f, -0.105f, -0.120f },
                new float[] { 0.102f, 0.107f, 0.110f, 0.104f, 0.082f, 0.078f },
                new float[] { 0.084f, 0.090f, 0.093f, 0.088f, 0.070f, 0.066f });
            _shortsMesh = BuildSuperellipseTube(profile, "ShortsMesh", 0.0f, 3, 2.2f);
            return _shortsMesh;
        }

        /// <summary>队伍色腰带：T 恤下摆处外扩 7mm 的窄环（与 T 恤同超椭圆族，贴合不出缝）。</summary>
        public static Mesh CreateWaistbandMesh()
        {
            if (_waistbandMesh != null) return _waistbandMesh;

            var profile = new SuperellipseProfile(
                new float[] { -0.020f, 0.012f },
                new float[] { 0.105f, 0.105f },
                new float[] { 0.085f, 0.085f });
            // 外扩 13mm（高于 T 恤与短裤的基准面），形成立体的衣带层级台阶，彻底消除下摆交界处的共面 Z-fighting
            _waistbandMesh = BuildSuperellipseTube(profile, "WaistbandMesh", 0.013f, 3, 2.3f);
            return _waistbandMesh;
        }

        /// <summary>服装纵向截面轮廓：高度序列（必须单调）+ 半宽 + 半深。</summary>
        private struct SuperellipseProfile
        {
            public readonly float[] Y;
            public readonly float[] HalfWidth;
            public readonly float[] HalfDepth;

            public SuperellipseProfile(float[] y, float[] w, float[] d)
            {
                Y = y; HalfWidth = w; HalfDepth = d;
            }

            /// <summary>在归一化弧长 u∈[0,1] 处采样（按 Y 段距线性参数化）。</summary>
            public Vector2 Sample(float u)
            {
                float total = Y[Y.Length - 1] - Y[0];
                float targetY = Y[0] + u * total;
                for (int i = 0; i < Y.Length - 1; i++)
                {
                    if (targetY <= Y[i + 1] || i == Y.Length - 2)
                    {
                        float t = Mathf.InverseLerp(Y[i], Y[i + 1], targetY);
                        return new Vector2(Mathf.Lerp(HalfWidth[i], HalfWidth[i + 1], t),
                                           Mathf.Lerp(HalfDepth[i], HalfDepth[i + 1], t));
                    }
                }
                return new Vector2(HalfWidth[HalfWidth.Length - 1], HalfDepth[HalfDepth.Length - 1]);
            }
        }

        /// <summary>
        /// 通用超椭圆管体网格（T 恤 / 短裤 / 腰带共用）：沿 Y 采样 profile，
        /// 每环用 |cos|^(2/n)、|sin|^(2/n) 生成方中带圆的截面；上下端盖面（openCaps:bit0=底 bit1=顶，1=开放）。
        /// </summary>
        private static Mesh BuildSuperellipseTube(
            SuperellipseProfile profile, string name, float offset, int openCaps, float exponent, float frontCollarDip = 0f)
        {
            const int rings = 20;
            const int segs = 32;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float u = (float)r / rings;
                Vector2 wd = profile.Sample(u);
                float w = wd.x + offset;
                float d = wd.y + offset;
                for (int s = 0; s <= segs; s++)
                {
                    float theta = 2f * Mathf.PI * (float)s / segs;
                    float c = Mathf.Cos(theta);
                    float sn = Mathf.Sin(theta);
                    float e = 2f / exponent;

                    float y = u * (profile.Y[profile.Y.Length - 1] - profile.Y[0]) + profile.Y[0];
                    if (frontCollarDip > 0.001f && u > 0.45f)
                    {
                        // 前胸圆领挖弧：sn = sin(theta) > 0（正脸 +Z 方向）平滑下沉，后背与双肩平缓过渡
                        float forwardFactor = Mathf.Clamp01(sn);
                        float dipProg = Mathf.SmoothStep(0f, 1f, (u - 0.45f) / 0.55f);
                        y -= frontCollarDip * forwardFactor * dipProg;
                    }

                    verts.Add(new Vector3(
                        Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e) * w,
                        y,
                        Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e) * d));
                }
            }

            int row = segs + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int i0 = r * row + s;
                    int i1 = (r + 1) * row + s;
                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            }

            // 端盖（默认封闭；短裤底开放让大腿穿出、T 恤领口由颈部遮断但仍封闭顶面）
            if ((openCaps & 1) == 0)
            {
                AddFanCap(verts, tris, 0, segs, false);
            }
            if ((openCaps & 2) == 0)
            {
                AddFanCap(verts, tris, rings * row, segs, true);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>给管体端部补一个中心扇形盖面。</summary>
        private static void AddFanCap(List<Vector3> verts, List<int> tris, int ringBase, int segs, bool facingUp)
        {
            int center = verts.Count;
            Vector3 avg = Vector3.zero;
            for (int s = 0; s < segs; s++) avg += verts[ringBase + s];
            avg /= segs;
            verts.Add(avg);
            for (int s = 0; s < segs; s++)
            {
                int a = ringBase + s;
                int b = ringBase + (s + 1) % segs;
                if (facingUp) { tris.Add(center); tris.Add(a); tris.Add(b); }
                else { tris.Add(center); tris.Add(b); tris.Add(a); }
            }
        }

        /// <summary>三次贝塞尔插值。</summary>
        private static Vector3 EvalBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float mt = 1f - t;
            return mt * mt * mt * a + 3f * mt * mt * t * b + 3f * mt * t * t * c + t * t * t * d;
        }

        /// <summary>三次贝塞尔一阶导（切线方向）。</summary>
        private static Vector3 EvalBezierTangent(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float mt = 1f - t;
            return 3f * mt * mt * (b - a) + 6f * mt * t * (c - b) + 3f * t * t * (d - c);
        }

        /// <summary>
        /// 触须发段网格（INKWAVE tentacle hair kernel）：沿两段三次贝塞尔脊线的扁锥形管
        /// （Z 向厚度 = 宽度的 flatten 比例，发束前后扁、左右宽），根部扇形封口（埋进帽里不露洞），
        /// 尖端由段 B 贝塞尔向脸侧 (+Z) 内卷。原点 = 发根枢轴，脊线沿 −Y 生长。
        /// </summary>
        private static Mesh BuildHairTubeMesh(
            string name,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector3 p4, Vector3 p5, Vector3 p6,
            float baseRadius, float tipRadius, float flattenZ, float taperStart)
        {
            const int spineSegs = 16;
            const int radialSegs = 10;

            var verts = new List<Vector3>(spineSegs * radialSegs + 2);
            var tris = new List<int>(spineSegs * radialSegs * 6);

            for (int i = 0; i <= spineSegs; i++)
            {
                float u = (float)i / spineSegs;
                Vector3 center, tangent;
                if (u <= 0.5f)
                {
                    float t = u / 0.5f;
                    center = EvalBezier(p0, p1, p2, p3, t);
                    tangent = EvalBezierTangent(p0, p1, p2, p3, t);
                }
                else
                {
                    float t = (u - 0.5f) / 0.5f;
                    center = EvalBezier(p3, p4, p5, p6, t);
                    tangent = EvalBezierTangent(p3, p4, p5, p6, t);
                }

                float taper = Mathf.SmoothStep(taperStart, 0.97f, u);
                float radius = Mathf.Lerp(baseRadius, tipRadius, taper);

                Vector3 tangentN = tangent.normalized;
                Vector3 side = Vector3.Cross(new Vector3(0f, 0f, 1f), tangentN);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
                side.Normalize();
                Vector3 up = Vector3.Cross(tangentN, side).normalized;

                for (int s = 0; s < radialSegs; s++)
                {
                    float theta = (float)s / radialSegs * Mathf.PI * 2f;
                    Vector3 radial = side * Mathf.Cos(theta) + up * Mathf.Sin(theta) * flattenZ;
                    verts.Add(center + radial * radius);
                }
            }

            for (int i = 0; i < spineSegs; i++)
            {
                int row0 = i * radialSegs;
                int row1 = (i + 1) * radialSegs;
                for (int s = 0; s < radialSegs; s++)
                {
                    int sNext = (s + 1) % radialSegs;
                    int i0 = row0 + s;
                    int i1 = row1 + s;
                    int i0n = row0 + sNext;
                    int i1n = row1 + sNext;
                    tris.Add(i0); tris.Add(i1); tris.Add(i0n);
                    tris.Add(i0n); tris.Add(i1); tris.Add(i1n);
                }
            }

            // 根部封口扇面（+Y 朝上，摆动时不露洞）
            int capRootCenter = verts.Count;
            verts.Add(p0 + Vector3.up * 0.004f);
            for (int s = 0; s < radialSegs; s++)
            {
                tris.Add(capRootCenter);
                tris.Add((s + 1) % radialSegs);
                tris.Add(s);
            }

            // 末端封口扇面（封闭发管尖端，杜绝管内中空漏底与背面剔除穿透）
            int tipBaseRow = spineSegs * radialSegs;
            int capTipCenter = verts.Count;
            Vector3 tipDir = (p6 - p5).sqrMagnitude > 1e-6f ? (p6 - p5).normalized : Vector3.down;
            verts.Add(p6 + tipDir * 0.003f);
            for (int s = 0; s < radialSegs; s++)
            {
                tris.Add(capTipCenter);
                tris.Add(tipBaseRow + s);
                tris.Add(tipBaseRow + (s + 1) % radialSegs);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 触须发段 A（根段，长 ~0.13m）：根粗 0.048 → 中段 0.030，微后收（−Z 0.012）。
        /// 公开给 EditMode 测试做网格健全性检查。
        /// </summary>
        public static Mesh CreateHairSegment1Mesh()
        {
            if (_hairSeg1Mesh != null) return _hairSeg1Mesh;
            _hairSeg1Mesh = BuildHairTubeMesh(
                "HairSeg1",
                Vector3.zero,
                new Vector3(0f, -0.045f, -0.002f),
                new Vector3(0f, -0.090f, -0.008f),
                new Vector3(0f, -0.130f, -0.012f),
                new Vector3(0f, -0.130f, -0.012f),
                new Vector3(0f, -0.130f, -0.012f),
                new Vector3(0f, -0.130f, -0.012f),
                0.048f, 0.030f, 0.55f, 0.15f);
            return _hairSeg1Mesh;
        }

        /// <summary>
        /// 触须发段 B（梢段，长 ~0.12m，挂在 HairMid 枢轴下）：0.030 → 0.006，
        /// 末端向脸侧（+Z）内卷 ~0.06（INKWAVE 标志性的 tentacle 卷尖）。
        /// </summary>
        public static Mesh CreateHairSegment2Mesh()
        {
            if (_hairSeg2Mesh != null) return _hairSeg2Mesh;
            _hairSeg2Mesh = BuildHairTubeMesh(
                "HairSeg2",
                Vector3.zero,
                new Vector3(0f, -0.040f, 0.004f),
                new Vector3(0f, -0.085f, 0.028f),
                new Vector3(0f, -0.118f, 0.062f),
                new Vector3(0f, -0.118f, 0.062f),
                new Vector3(0f, -0.118f, 0.062f),
                new Vector3(0f, -0.118f, 0.062f),
                0.033f, 0.008f, 0.55f, 0.20f);
            return _hairSeg2Mesh;
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
            out Transform rightEye,
            out Transform face,
            out Transform leftLegRoot,
            out Transform rightLegRoot,
            out Transform leftKnee,
            out Transform rightKnee,
            out Transform leftAnkle,
            out Transform rightAnkle,
            out Transform leftElbow,
            out Transform rightElbow,
            out Transform spine,
            out Transform neck,
            out Transform[] hairRootPivots,
            out Transform[] hairMidPivots)
        {
            // 1. 骨盆枢轴 (Hips)：P33 真骨骼树根（世界 y = HipBoneY = 0.520m）。
            //    双腿直接挂 Hips（不受躯干挤压影响），上身链经 BodySquash → Spine → Chest 逐级折叠，
            //    前倾按 KidGaitSolver 的 25/45/30 分配到髋/腰/胸（对齐 INKWAVE runPose 脊柱链）。
            var hipsGo = new GameObject("Hips");
            hipsGo.transform.SetParent(kidRoot, false);
            hipsGo.transform.localPosition = new Vector3(0f, CharacterMetrics.HipBoneY, 0f);
            hips = hipsGo.transform;

            // 2. 上身挤压容器 (BodySquash)：触地压扁 / 起跳拉伸只作用在上身链（脚保持踩地）。
            var squashGo = new GameObject("BodySquash");
            squashGo.transform.SetParent(hips, false);
            squashGo.transform.localPosition = Vector3.zero;
            bodySquash = squashGo.transform;

            // 2.05 腰椎 (Spine) 与胸腔 (Chest)：两级脊柱节点，控制器分别写 45% / 30% 前倾；
            //      头/臂/墨罐/武器都挂胸腔骨，前倾时枪随上身自然走（滚筒贴地由运行时闭环兜底）。
            var spineGo = new GameObject("Spine");
            spineGo.transform.SetParent(bodySquash, false);
            spineGo.transform.localPosition = new Vector3(0f, CharacterMetrics.SpineLocalY, 0f);
            spine = spineGo.transform;

            var chestGo = new GameObject("Chest");
            chestGo.transform.SetParent(spine, false);
            chestGo.transform.localPosition = new Vector3(0f, CharacterMetrics.ChestLocalY, 0f);
            chest = chestGo.transform;

            // 2.1 服装真体积化（P33 废弃蛋面贴壳工艺）：盒型 T 恤随胸腔折叠、灯笼短裤锚骨盆、
            //     队伍色腰带贴 T 恤下摆。外描边只给 T 恤与头（两条主剪影），交界处不出双描边黑线。
            var teeGo = new GameObject("KidTee");
            teeGo.transform.SetParent(spine, false);
            teeGo.transform.localPosition = Vector3.zero;
            teeGo.AddComponent<MeshFilter>().sharedMesh = CreateTeeMesh();
            teeGo.AddComponent<MeshRenderer>().sharedMaterial = p.Shirt;

            var shortsGo = new GameObject("KidShorts");
            shortsGo.transform.SetParent(hips, false);
            shortsGo.transform.localPosition = Vector3.zero;
            shortsGo.AddComponent<MeshFilter>().sharedMesh = CreateShortsMesh();
            shortsGo.AddComponent<MeshRenderer>().sharedMaterial = p.Shorts;

            var bandGo = new GameObject("Waistband");
            bandGo.transform.SetParent(spine, false);
            bandGo.transform.localPosition = Vector3.zero;
            bandGo.AddComponent<MeshFilter>().sharedMesh = CreateWaistbandMesh();
            bandGo.AddComponent<MeshRenderer>().sharedMaterial = p.Waistband;

            // 2.4 颈与头 (Neck → Head)：头心世界 y ≈ 0.978、头顶 1.120（恰好贴包络，零玩法影响）。
            var neckGo = new GameObject("Neck");
            neckGo.transform.SetParent(chest, false);
            neckGo.transform.localPosition = new Vector3(0f, CharacterMetrics.NeckLocalY, 0.006f);
            neck = neckGo.transform;

            // 真实可见的少年肤色小脖子：从 T 恤领口（世界 0.73m）稳稳延伸到下巴（世界 0.88m），露出一截清爽精神的颈部
            CreateCylinder(neck, "NeckSkin", new Vector3(0f, -0.055f, 0.002f), new Vector3(0.076f, 0.060f, 0.076f), p.Skin);
            // T 恤圆领螺纹领圈（Crew Collar Ribbing）：勾勒出精致时尚的圆领轮廓
            CreateCylinder(neck, "NeckCollarRib", new Vector3(0f, -0.095f, 0.002f), new Vector3(0.084f, 0.008f, 0.080f), p.Dark);

            var headGo = new GameObject("Head");
            headGo.transform.SetParent(neck, false);
            headGo.transform.localPosition = new Vector3(0f, CharacterMetrics.HeadLocalY, 0.004f);
            head = headGo.transform;

            // 2.5 馒头头（mochi head）：唯一描边头部网格 + 队伍色头皮帽（盖枕部、露额与眉上）。
            var headMeshGo = new GameObject("MochiHead");
            headMeshGo.transform.SetParent(head, false);
            headMeshGo.transform.localPosition = Vector3.zero;
            headMeshGo.AddComponent<MeshFilter>().sharedMesh = CreateMochiHeadMesh();
            headMeshGo.AddComponent<MeshRenderer>().sharedMaterial = p.Skin;

            var scalpGo = new GameObject("ScalpCap");
            scalpGo.transform.SetParent(head, false);
            scalpGo.transform.localPosition = Vector3.zero;
            scalpGo.AddComponent<MeshFilter>().sharedMesh = CreateScalpCapMesh();
            scalpGo.AddComponent<MeshRenderer>().sharedMaterial = p.TeamHair;

            // 2.6 面部容器 (Face)：五官按头部雕塑面采样贴面（SampleMochiHeadPoint），
            //     既不会陷入曲面也不悬浮；大眼构图对齐 INKWAVE（单眼宽 ≈ 头宽 25%，颊线高度）。
            var faceGo = new GameObject("Face");
            faceGo.transform.SetParent(head, false);
            faceGo.transform.localPosition = Vector3.zero;
            face = faceGo.transform;

            // 连体深色眼罩桥（鼻上一小横）+ 左右眼窝罩 + 大眼 + 队伍色虹膜 + 瞳孔 + 双高光
            var bridgeDir = new Vector3(0f, 0.10f, 1f).normalized;
            var maskBridge = CreateSphere(face, "EyeMask_Bridge",
                SampleMochiHeadPoint(bridgeDir, 0.005f), new Vector3(0.052f, 0.040f, 0.016f), p.Dark);
            maskBridge.transform.localRotation = Quaternion.identity;

            leftEye = null;
            rightEye = null;
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s < 0 ? "L" : "R";

                Vector3 eyeDir = new Vector3(s * 0.44f, 0.10f, 0.89f).normalized;
                float yaw = Mathf.Atan2(eyeDir.x, eyeDir.z) * Mathf.Rad2Deg;

                var mask = CreateSphere(face, "EyeMask_" + side,
                    SampleMochiHeadPoint(eyeDir, 0.005f), new Vector3(0.094f, 0.108f, 0.018f), p.Dark);
                mask.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                // 白眼球椭球（局部 +Z 朝脸外）：眨眼写 localScale.y，控制器缓存默认值
                var eye = CreateSphere(face, "Eye_" + side,
                    SampleMochiHeadPoint(eyeDir, 0.010f), new Vector3(0.070f, 0.082f, 0.036f), p.White);
                eye.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                CreateSphere(eye.transform, "Iris", new Vector3(0f, -0.004f, 0.47f), new Vector3(0.52f, 0.52f, 0.30f), p.TeamHair);
                CreateSphere(eye.transform, "Pupil", new Vector3(0f, -0.004f, 0.58f), new Vector3(0.26f, 0.28f, 0.20f), p.Dark);
                CreateSphere(eye.transform, "Glint", new Vector3(-0.14f, 0.16f, 0.66f), new Vector3(0.15f, 0.15f, 0.10f), p.White);
                CreateSphere(eye.transform, "Glint2", new Vector3(0.11f, -0.11f, 0.64f), new Vector3(0.08f, 0.08f, 0.08f), p.White);

                if (s < 0) leftEye = eye.transform;
                else rightEye = eye.transform;

                // 眉毛：眼罩上方的小弧，外侧微挑（+roll 抬 +X 端）
                Vector3 browDir = new Vector3(s * 0.50f, 0.44f, 0.86f).normalized;
                float browYaw = Mathf.Atan2(browDir.x, browDir.z) * Mathf.Rad2Deg;
                var brow = CreateSphere(face, "Brow_" + side,
                    SampleMochiHeadPoint(browDir, 0.007f), new Vector3(0.056f, 0.012f, 0.012f), p.Dark);
                brow.transform.localRotation = Quaternion.Euler(0f, browYaw, s * 6f);

                // 腮红（保留一点 Q 版信号）：眼外下方
                Vector3 blushDir = new Vector3(s * 0.68f, -0.16f, 0.72f).normalized;
                float blushYaw = Mathf.Atan2(blushDir.x, blushDir.z) * Mathf.Rad2Deg;
                var blush = CreateSphere(face, "Blush_" + side,
                    SampleMochiHeadPoint(blushDir, 0.006f), new Vector3(0.050f, 0.026f, 0.012f), p.Blush);
                blush.transform.localRotation = Quaternion.Euler(0f, blushYaw, 0f);

                // 耳朵（挂头骨本体）：头侧小杯
                Vector3 earDir = new Vector3(s * 0.97f, -0.05f, -0.06f).normalized;
                var ear = CreateSphere(head, "Ear_" + side,
                    SampleMochiHeadPoint(earDir, 0.004f), new Vector3(0.014f, 0.034f, 0.026f), p.Skin);
                ear.transform.localRotation = Quaternion.Euler(0f, 0f, s * 10f);
            }

            // 小嘴：中线、颊线下方（chin 收坡上，不落在下巴尖）
            var mouthDir = new Vector3(0f, -0.26f, 0.96f).normalized;
            var mouth = CreateSphere(face, "Mouth",
                SampleMochiHeadPoint(mouthDir, 0.006f), new Vector3(0.044f, 0.014f, 0.012f), p.Dark);
            mouth.transform.localRotation = Quaternion.identity;

            // 2.7 胸前队伍色墨点 ×2：T 恤前片（超椭圆脸近似平面）上的小贴点，保留队伍识别。
            var ink0 = CreateSphere(spine, "ChestInk_0", new Vector3(0.045f, 0.085f, 0.096f),
                new Vector3(0.045f, 0.038f, 0.010f), p.InkDecal);
            ink0.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            var ink1 = CreateSphere(spine, "ChestInk_1", new Vector3(0.072f, 0.058f, 0.092f),
                new Vector3(0.028f, 0.024f, 0.010f), p.InkDecal);
            ink1.transform.localRotation = Quaternion.Euler(0f, 0f, 14f);

            // 2.8 触须垂发（P33 主剪件，队伍色）：6 束（左右各 3），每束 根段→梢段 两节骨骼链。
            // 发根种在头皮帽面上（曲面外扩 14mm），默认"外撇 + 下垂 + 微后收"；
            // 根/梢两层 pivot 交给控制器弹簧链（BindKidRig → UpdateHairSway），
            // 跑跳/急转/变形时整束甩动并在鞭梢滞后 —— INKWAVE 头发读法的灵魂。
            // 节点命名避开测试断言不存在的 "InkCrest"。
            var hairRootGo = new GameObject("TentacleHair");
            hairRootGo.transform.SetParent(head, false);
            Mesh seg1Mesh = CreateHairSegment1Mesh();
            Mesh seg2Mesh = CreateHairSegment2Mesh();
            hairRootPivots = new Transform[6];
            hairMidPivots = new Transform[6];
            Vector3[] strandDirs = new Vector3[]
            {
                new Vector3(-0.88f, 0.30f, -0.10f),  // 0 左前（贴脸侧）
                new Vector3( 0.88f, 0.30f, -0.10f),  // 1 右前
                new Vector3(-0.62f, 0.42f, -0.56f),  // 2 左后侧
                new Vector3( 0.62f, 0.42f, -0.56f),  // 3 右后侧
                new Vector3(-0.28f, 0.52f, -0.80f),  // 4 左后
                new Vector3( 0.28f, 0.52f, -0.80f)   // 5 右后
            };
            for (int i = 0; i < strandDirs.Length; i++)
            {
                Vector3 dir = strandDirs[i].normalized;
                Vector3 rootPos = SampleMochiHeadPoint(dir, 0.008f);
                // 下垂方向 = 外法线 45% + 重力 95% + 微后收，用 FromToRotation 把骨骼 -Y 轴对准它
                Vector3 drop = (dir * 0.5f + Vector3.down * 0.92f + Vector3.back * 0.12f).normalized;
                Quaternion rootRot = Quaternion.FromToRotation(Vector3.down, drop);

                var rootGo = new GameObject("HairPivot_" + i);
                rootGo.transform.SetParent(hairRootGo.transform, false);
                rootGo.transform.localPosition = rootPos;
                rootGo.transform.localRotation = rootRot;
                hairRootPivots[i] = rootGo.transform;

                var seg1Go = new GameObject("Tentacle_Hair_" + i);
                seg1Go.transform.SetParent(rootGo.transform, false);
                seg1Go.AddComponent<MeshFilter>().sharedMesh = seg1Mesh;
                seg1Go.AddComponent<MeshRenderer>().sharedMaterial = p.TeamHair;

                // 梢段 pivot：接在根段末端（网格脊线 −0.13），默认续接方向 + 微内卷预弯
                var midGo = new GameObject("HairSkirt_" + i);
                midGo.transform.SetParent(rootGo.transform, false);
                midGo.transform.localPosition = new Vector3(0f, -0.128f, -0.012f);
                midGo.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                hairMidPivots[i] = midGo.transform;

                var seg2Go = new GameObject("Tentacle_Hair_Skirt" + i);
                seg2Go.transform.SetParent(midGo.transform, false);
                seg2Go.AddComponent<MeshFilter>().sharedMesh = seg2Mesh;
                seg2Go.AddComponent<MeshRenderer>().sharedMaterial = p.TeamHair;
            }
            leftHair = hairRootPivots[0];
            rightHair = hairRootPivots[1];

            // 2.9 背驮墨罐 (InkTankBackpack)：挂胸腔骨、罐顶微后倾（INKWAVE tank 骨 −0.1rad）；
            //     上身前倾时墨罐自然跟着抬压，罐内液面由控制器按墨水余量收缩。
            var backpackGo = new GameObject("InkTankBackpack");
            backpackGo.transform.SetParent(chest, false);
            backpackGo.transform.localPosition = new Vector3(0f, -0.010f, -CharacterMetrics.TankBackZ);
            backpackGo.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);

            CreateBox(backpackGo.transform, "MountPlate", new Vector3(0f, 0f, 0.048f), new Vector3(0.10f, 0.20f, 0.014f), p.Dark);
            CreateCylinder(backpackGo.transform, "GlassChamber", Vector3.zero, new Vector3(0.112f, 0.078f, 0.112f), p.Glass);

            var fillGo = CreateCylinder(backpackGo.transform, "LiquidFill", Vector3.zero, new Vector3(0.094f, 0.066f, 0.094f), p.TeamLiquid);
            tankFill = fillGo.transform;

            CreateCylinder(backpackGo.transform, "TopCap", new Vector3(0f, 0.080f, 0f), new Vector3(0.120f, 0.008f, 0.120f), p.TankMetal);
            CreateCylinder(backpackGo.transform, "ValveKnob", new Vector3(0f, 0.093f, 0f), new Vector3(0.028f, 0.010f, 0.028f), p.Dark);
            CreateCylinder(backpackGo.transform, "BottomCap", new Vector3(0f, -0.080f, 0f), new Vector3(0.120f, 0.008f, 0.120f), p.TankMetal);

            // 3. 两骨手臂 (LeftArm / RightArm)：肩枢轴挂胸腔骨（测试路径 Chest/LeftArm 保持）。
            //    这里的 authored 旋转只是"装配期占位"：真正的持握姿态在武器建好后由
            //    ResolveWeaponHoldPoses 按每把武器的真实握把位置用两骨 IK 反解并整体套用
            //    （见 CharacterVisualAssembler.ResolveWeaponHoldPoses）。
            var lArmGo = new GameObject("LeftArm");
            lArmGo.transform.SetParent(chest, false);
            lArmGo.transform.localPosition = new Vector3(-CharacterMetrics.ShoulderLocalX, CharacterMetrics.ShoulderLocalY, CharacterMetrics.ShoulderLocalZ);
            lArmGo.transform.localRotation = Quaternion.Euler(-8f, 0f, -4f);
            leftArm = lArmGo.transform;
            BuildKidArm(leftArm, p, -1, out leftElbow);

            var rArmGo = new GameObject("RightArm");
            rArmGo.transform.SetParent(chest, false);
            rArmGo.transform.localPosition = new Vector3(CharacterMetrics.ShoulderLocalX, CharacterMetrics.ShoulderLocalY, CharacterMetrics.ShoulderLocalZ);
            rArmGo.transform.localRotation = Quaternion.Euler(-20f, 0f, 5f);
            rightArm = rArmGo.transform;
            BuildKidArm(rightArm, p, 1, out rightElbow);

            // 4. 两骨双腿：髋→膝→踝三段链挂 Hips，由控制器按 KidGaitSolver 步态与滞空/落地写回
            BuildLeg(hips, p, -1, out leftLegRoot, out leftKnee, out leftAnkle);
            BuildLeg(hips, p, 1, out rightLegRoot, out rightKnee, out rightAnkle);
        }

        /// <summary>
        /// 两骨腿（P33）：短裤蓝大腿 → 膝 → 肤色小腿（白袜口）→ 踝 → INKWAVE 厚底板鞋
        /// （深色鞋壳 + 白中底 + 白鞋头包边 + 队伍色后跟片），鞋底严格落在世界 y=0
        /// （踝世界高度 0.501 − 0.222 − 0.211 = 0.068，鞋壳底 = 0.068 − 0.037 − 0.032 = −0.001 贴地）。
        /// </summary>
        private static void BuildLeg(
            Transform hips,
            Palette p,
            int side,
            out Transform legRoot,
            out Transform kneePivot,
            out Transform anklePivot)
        {
            string sideName = side < 0 ? "L" : "R";
            var legGo = new GameObject(side < 0 ? "LeftLeg" : "RightLeg");
            legGo.transform.SetParent(hips, false);
            legGo.transform.localPosition = new Vector3(side * CharacterMetrics.HipHalfX, CharacterMetrics.LegHipLocalY, 0f);
            legRoot = legGo.transform;

            float thighLen = CharacterMetrics.ThighLength;
            float shinLen = CharacterMetrics.ShinLength;

            // 大腿（短裤蓝）：从髋枢轴垂到膝
            CreateCapsule(legRoot, "Thigh",
                new Vector3(0f, -thighLen * 0.5f, 0f),
                new Vector3(0.096f, thighLen * 0.5f + 0.024f, 0.096f), p.Shorts);

            // 膝关节枢轴
            var kneeGo = new GameObject("Knee");
            kneeGo.transform.SetParent(legRoot, false);
            kneeGo.transform.localPosition = new Vector3(0f, -thighLen, 0f);
            kneePivot = kneeGo.transform;

            // 小腿（肤色）+ 白袜口（中筒袜上沿）
            CreateCapsule(kneePivot, "Calf",
                new Vector3(0f, -shinLen * 0.5f, 0f),
                new Vector3(0.078f, shinLen * 0.5f + 0.016f, 0.078f), p.Skin);
            CreateCylinder(kneePivot, "SockCuff_" + sideName,
                new Vector3(0f, -0.052f, 0f), new Vector3(0.086f, 0.011f, 0.086f), p.White);

            // 踝关节枢轴
            var ankleGo = new GameObject("Ankle");
            ankleGo.transform.SetParent(kneePivot, false);
            ankleGo.transform.localPosition = new Vector3(0f, -shinLen, 0f);
            anklePivot = ankleGo.transform;

            // 厚底板鞋：鞋壳压扁球前伸成鞋头；中底白条与鞋头包边底缘与鞋壳齐平（触地标定不变）
            CreateSphere(anklePivot, "Foot",
                new Vector3(0f, -0.037f, 0.052f),
                new Vector3(0.098f, 0.064f, CharacterMetrics.ShoeLength), p.Dark);
            CreateSphere(anklePivot, "SoleMid",
                new Vector3(0f, -0.056f, 0.048f),
                new Vector3(0.104f, 0.024f, 0.208f), p.White);
            CreateSphere(anklePivot, "ToeCap",
                new Vector3(0f, -0.044f, 0.128f),
                new Vector3(0.086f, 0.042f, 0.070f), p.White);
            // 队伍色后跟片（INKWAVE heel window：远距认队色的锚点之一）
            CreateBox(anklePivot, "HeelTab_" + sideName,
                new Vector3(0f, -0.022f, -0.046f), new Vector3(0.042f, 0.034f, 0.014f), p.TeamHair);
        }

        /// <summary>
        /// 两骨手臂（P32 二段臂的完整骨骼版）：肩球（三角肌胶囊）+ 上臂（奶油白短袖身）→ 肘枢轴 →
        /// 队伍色袖口 + 肤色前臂 + 圆手。肩球挂在肩枢轴上、随抬臂一起转，专门用来填平
        /// "衣身肩线 → 手臂"之间的断缝；上臂短袖顶部压在枢轴之下，避免旧版袖头冒出肩膀的悬浮感。
        /// 具体持握角度由 <see cref="ResolveWeaponHoldPoses"/> 按武器反解，这里只负责几何。
        /// </summary>
        private static void BuildKidArm(Transform shoulder, Palette p, int side, out Transform elbowPivot)
        {
            float upperLen = CharacterMetrics.UpperArmLength;
            float foreLen = CharacterMetrics.ForearmLength;

            // 肩球：只保留埋进 T 恤里的一小段三角肌过渡，不外凸成"护肩球"。
            // 肩枢轴已经落在衣身最宽处内侧，袖子上半段会被衣服自然盖住，形成肩线。
            CreateSphere(shoulder, "ShoulderCap",
                new Vector3(side * 0.004f, -0.006f, 0f),
                new Vector3(0.082f, 0.070f, 0.082f), p.Sleeve);

            // 短袖：覆盖上臂上段（drop-shoulder 短款，袖口在肘上方收边）
            // 顶端落在肩枢轴高度（不再冒出肩线），内侧半段埋进衣身、外侧形成圆润肩头。
            CreateCapsule(shoulder, "Sleeve",
                new Vector3(0f, -0.068f, 0f),
                new Vector3(0.090f, 0.066f, 0.090f), p.Sleeve);

            // 肘枢轴：authored 屈肘 35°（负 X = 小臂向前卷）
            var elbowGo = new GameObject("Elbow");
            elbowGo.transform.SetParent(shoulder, false);
            elbowGo.transform.localPosition = new Vector3(0f, -upperLen, 0f);
            elbowGo.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            Transform elbowT = elbowGo.transform;

            // 队伍色袖口：盖住短袖与前臂的接缝（绕肘枢轴旋转时遮住断缝）
            CreateCylinder(elbowT, "Cuff", Vector3.zero, new Vector3(0.080f, 0.010f, 0.080f), p.TeamHair);

            // 肤色前臂
            CreateCapsule(elbowT, "Forearm",
                new Vector3(0f, -foreLen * 0.5f, 0f),
                new Vector3(0.064f, foreLen * 0.5f + 0.012f, 0.064f), p.Skin);

            // 握拳（握把位）：沿骨轴略拉长，读起来是"拳"而不是一颗球
            var hand = CreateSphere(elbowT, "Hand", new Vector3(0f, -foreLen, 0f),
                new Vector3(0.078f, 0.090f, 0.078f), p.Skin);
            // 拇指：贴握把内侧的小凸起（右手在 -X 侧、左手在 +X 侧）
            CreateSphere(hand.transform, "Thumb",
                new Vector3(-side * 0.026f, 0.006f, 0.030f),
                new Vector3(0.036f, 0.040f, 0.036f), p.Skin);

            elbowPivot = elbowT;
        }

        /// <summary>
        /// 把每把武器的握把平移到右手手位，并反解出对应的手臂姿态。
        /// 具体解算已抽到 <see cref="WeaponHoldRig"/>（装配器与运行期共用同一套数学），
        /// 这里只保留装配器入口，避免两处手位表漂移。
        /// </summary>
        private static Dictionary<WeaponKind, ArmHoldPose> ResolveWeaponHoldPoses(
            Transform weaponRoot,
            Transform leftArm, Transform rightArm, Transform leftElbow, Transform rightElbow,
            GameObject shooterGo, GameObject rollerGo, GameObject chargerGo, GameObject dualiesGo,
            GameObject slosherGo, GameObject blasterGo, GameObject splatlingGo,
            float chainY)
        {
            return WeaponHoldRig.Build(
                weaponRoot, leftArm, rightArm, leftElbow, rightElbow,
                shooterGo, rollerGo, chargerGo, dualiesGo, slosherGo, blasterGo, splatlingGo,
                chainY);
        }

        #endregion

        #region Squid Form Assembly

        private static void BuildSquidHierarchy(
            Transform squidRoot,
            Palette p,
            out Transform squidBody,
            out Transform squidTentacles,
            out Transform[] tentacleArray)
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

            tentacleArray = new Transform[4];
            tentacleArray[0] = CreateCapsule(squidTentacles, "Tentacle_OutL", new Vector3(-0.14f, 0.01f, -0.16f), new Vector3(0.06f, 0.18f, 0.06f), p.SquidBody).transform;
            tentacleArray[0].localRotation = Quaternion.Euler(85f, 0f, 0f);
            tentacleArray[1] = CreateCapsule(squidTentacles, "Tentacle_MidL", new Vector3(-0.05f, 0.01f, -0.20f), new Vector3(0.055f, 0.22f, 0.055f), p.SquidBody).transform;
            tentacleArray[1].localRotation = Quaternion.Euler(88f, 0f, 0f);
            tentacleArray[2] = CreateCapsule(squidTentacles, "Tentacle_MidR", new Vector3(0.05f, 0.01f, -0.20f), new Vector3(0.055f, 0.22f, 0.055f), p.SquidBody).transform;
            tentacleArray[2].localRotation = Quaternion.Euler(88f, 0f, 0f);
            tentacleArray[3] = CreateCapsule(squidTentacles, "Tentacle_OutR", new Vector3(0.14f, 0.01f, -0.16f), new Vector3(0.06f, 0.18f, 0.06f), p.SquidBody).transform;
            tentacleArray[3].localRotation = Quaternion.Euler(85f, 0f, 0f);
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

            // 枪托缩短并前移：握把锚进手位后（枪身整体后收 ~5cm），旧的长枪托会埋进 T 恤。
            // 短托 + 前移让托底刚好停在胸前表面之外，视觉上仍是"抵肩长枪"的剪影。
            CreateBox(root, "Stock", new Vector3(0f, 0.02f, -0.090f), new Vector3(0.06f, 0.10f, 0.16f), p.Dark);
            CreateBox(root, "ButtPad", new Vector3(0f, 0.02f, -0.180f), new Vector3(0.07f, 0.14f, 0.03f), p.Dark);

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
        /// 运行时兜底合并（P30-C）：场景烘焙的角色在编辑器装配期可能因合并缓存瞬时失败而
        /// 保持逐件武器渲染（每把武器 6~16 个独立 MeshRenderer），这里在首次进入场景时补做一次。
        /// 幂等：MergeWeaponRoot 对已有合并产物的根直接跳过；静态网格缓存跨实例共享，
        /// 全场 8 个角色只有首个触发真实构建，其余复用。逐帧零开销（仅 Start 一次性）。
        /// </summary>
        public static void MergeWeaponsRuntime(
            GameObject shooter, GameObject roller, GameObject charger, GameObject dualies,
            GameObject slosher, GameObject blaster, GameObject splatling,
            Transform rollerDrum, Transform splatlingBarrels, Renderer[] chargerCoils)
        {
            if (shooter == null || roller == null || charger == null || dualies == null
                || slosher == null || blaster == null || splatling == null)
                return;

            try
            {
                MergeWeaponStaticMeshes(shooter, roller, charger, dualies, slosher, blaster, splatling,
                    rollerDrum, splatlingBarrels, chargerCoils);
            }
            catch (System.Exception e)
            {
                // 装饰性优化失败不拖垮游戏（与装配期同一降级策略）
                Debug.LogWarning($"[CharacterVisualAssembler] 运行时武器合并兜底失败，保持独立部件渲染：{e}");
            }
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

            // 0. 幂等守卫：该合并根下已有合并产物（编辑器重装配 / 运行时兜底重入）直接跳过，
            //    否则源部件虽已禁用仍会被再次收集，重复生成 MergedStatic_*。
            for (int i = 0; i < bakeRoot.childCount; i++)
            {
                if (bakeRoot.GetChild(i).name.StartsWith(MergedPartPrefix)) return;
            }

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
            bool allGroupsBuilt = true;
            for (int g = 0; g < groups.Count; g++)
            {
                if (reuseCache)
                {
                    builtMeshes[g] = cachedGroups[g];
                    if (builtMeshes[g] == null) allGroupsBuilt = false;
                    continue;
                }
                builtMeshes[g] = BuildMergedGroupMesh(kind, mergeIndex, g, bakeRoot, groups[g]);
                if (builtMeshes[g] == null) allGroupsBuilt = false;
            }
            if (!reuseCache && allGroupsBuilt)
            {
                perRoot[mergeIndex] = new List<Mesh>(builtMeshes);
            }
            // P30-C 修复：失败组不再写入缓存。网格构建受装配时机影响可能瞬时失败，
            // 缓存 null 会把瞬时失败固化成永久静默跳过（实测场景烘焙武器自 v0.1.3 起从未合并成功）；
            // 改为失败组不落缓存，下次装配自动重试，成功后幂等守卫防止重复挂载。

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
