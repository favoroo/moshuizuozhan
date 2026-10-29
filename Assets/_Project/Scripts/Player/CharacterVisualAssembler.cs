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
    /// 5. 7 套武器模型按 CharacterMetrics 缩放换算挂载并绑定动态构件与枪口。
    /// </summary>
    public static class CharacterVisualAssembler
    {
        private static Mesh _eggMesh;

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

            return controller;
        }

        #endregion

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

            // 从可能传入的旧材质中读取基础颜色语义，不直接复用旧的 URP Lit 实例，确保角色全套材质卡通风格统一
            Color clothColor = teamCol;
            if (fallbackBodyMat != null)
            {
                if (fallbackBodyMat.HasProperty("_BaseColor"))
                    clothColor = fallbackBodyMat.GetColor("_BaseColor");
                else if (fallbackBodyMat.HasProperty("_Color"))
                    clothColor = fallbackBodyMat.GetColor("_Color");
            }

            Color darkColor = new Color(0.12f, 0.14f, 0.18f);
            if (fallbackDarkMat != null)
            {
                if (fallbackDarkMat.HasProperty("_BaseColor"))
                    darkColor = fallbackDarkMat.GetColor("_BaseColor");
                else if (fallbackDarkMat.HasProperty("_Color"))
                    darkColor = fallbackDarkMat.GetColor("_Color");
            }

            // 蛋体作为视觉核心且轮廓最大，唯一启用黑色外描边；其他微小部件不启用描边，避免密集杂线
            p.TeamCloth = CreateCharacterMat("Mat_TeamCloth", clothColor, 0.55f, 0.55f, outlined: true);
            p.TeamHair = CreateCharacterMat("Mat_TeamHair", teamCol, 0.90f, 0.70f);
            p.TeamLiquid = CreateCharacterMat("Mat_TeamLiquid", teamCol, 0.95f, 0.60f, emission: teamCol * 0.35f);
            p.SquidBody = CreateCharacterMat("Mat_SquidBody", teamCol, 0.90f, 0.70f);

            p.Skin = CreateCharacterMat("Mat_KidSkin", new Color(0.97f, 0.80f, 0.68f), 0.30f, 0.20f);
            p.Dark = CreateCharacterMat("Mat_DarkPlastic", darkColor, 0.35f, 0.25f);
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
            Color? emission = null)
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
                    ApplyCharacterMatSettings(asset, baseCol, smoothness, rimStrength, emission);
                    UnityEditor.EditorUtility.SetDirty(asset);
                    return asset;
                }

                var created = new Material(sh) { name = fileName };
                ApplyCharacterMatSettings(created, baseCol, smoothness, rimStrength, emission);
                UnityEditor.AssetDatabase.CreateAsset(created, path);
                return created;
            }
#endif
            var mat = new Material(sh) { name = name };
            ApplyCharacterMatSettings(mat, baseCol, smoothness, rimStrength, emission);
            return mat;
        }

        private static void ApplyCharacterMatSettings(
            Material mat,
            Color baseCol,
            float smoothness,
            float rimStrength,
            Color? emission)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", rimStrength);
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", Color.white);

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

        #endregion

        #region Egg Body Procedural Mesh

        private static readonly float[] ProfileT = new float[]
        {
            0.00f, 0.08f, 0.20f, 0.35f, 0.50f, 0.65f, 0.78f, 0.88f, 0.95f, 1.00f
        };

        private static readonly float[] ProfileR01 = new float[]
        {
            0.62f, 0.86f, 0.96f, 1.00f, 0.99f, 0.94f, 0.84f, 0.68f, 0.46f, 0.10f
        };

        /// <summary>
        /// 采样蛋体归一化轮廓半径比例（0~1）。分段采用 SmoothStep 平滑插值，保证轮廓二阶光滑无折角。
        /// </summary>
        private static float SampleEggRadius01(float t)
        {
            t = Mathf.Clamp01(t);
            for (int i = 0; i < ProfileT.Length - 1; i++)
            {
                if (t <= ProfileT[i + 1])
                {
                    float segLength = ProfileT[i + 1] - ProfileT[i];
                    float u = (t - ProfileT[i]) / Mathf.Max(0.0001f, segLength);
                    float smoothU = Mathf.SmoothStep(0f, 1f, u);
                    return Mathf.Lerp(ProfileR01[i], ProfileR01[i + 1], smoothU);
                }
            }
            return ProfileR01[ProfileR01.Length - 1];
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
        /// 24 环 × 24 径向段旋转体，原点位于脚底（y=0），底部封口（半径 0.248m 做三角扇）。
        /// 法线完全使用切空间解析法推导，确保顶点法线精确指向外侧且圆环处平滑无硬边。
        /// </summary>
        public static Mesh CreateEggBodyMesh()
        {
            if (_eggMesh != null) return _eggMesh;

            var mesh = new Mesh { name = "EggBodyMesh" };

            const int ringCount = 24;
            const int radialSegs = 24;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // 1. 生成旋转体侧面顶点与解析法线
            // 为保证 UV 纹理在 0~1 闭合时无拉伸接缝，每环包含 (radialSegs + 1) 列顶点
            for (int r = 0; r <= ringCount; r++)
            {
                float t = (float)r / ringCount;
                float y = t * CharacterMetrics.BodyHeight;
                float r01 = SampleEggRadius01(t);
                float radius = r01 * CharacterMetrics.BodyMaxRadius;

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

                    // 解析法线推导：
                    // aroundTangent = (-sin, 0, cos)，profileTangent = (dr_dy * cos, 1, dr_dy * sin)
                    // cross(profileTangent, aroundTangent) = (cos, -dr_dy, sin)
                    // 与 radialDir (cos, 0, sin) 点乘结果为 1 > 0，严格指向外部，杜绝法线反向导致的黑脸
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

            // 2. 缝合侧面圆环三角形（绕序朝外）
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

            // 3. 底部平底封口（y=0 处半径 0.248m 做向下三角扇，保证站立触地接触面严密）
            int bottomCenterIdx = verts.Count;
            verts.Add(Vector3.zero);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0f));

            for (int s = 0; s < radialSegs; s++)
            {
                int p_s = s;
                int p_next = s + 1;
                tris.Add(bottomCenterIdx);
                tris.Add(p_s);
                tris.Add(p_next);
            }

            // 4. 顶部极点封顶三角扇（闭合水密网格）
            int topCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, CharacterMetrics.BodyHeight, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));

            int topRow = ringCount * (radialSegs + 1);
            for (int s = 0; s < radialSegs; s++)
            {
                int p_s = topRow + s;
                int p_next = topRow + s + 1;
                tris.Add(topCenterIdx);
                tris.Add(p_next);
                tris.Add(p_s);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            _eggMesh = mesh;
            return _eggMesh;
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

            // 2.1 蛋形主躯干网格（唯一使用带外描边 Cartoon Shader 的部件）
            var eggGo = new GameObject("EggBody");
            eggGo.transform.SetParent(bodySquash, false);
            eggGo.transform.localPosition = Vector3.zero;
            var mf = eggGo.AddComponent<MeshFilter>();
            var mr = eggGo.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateEggBodyMesh();
            mr.sharedMaterial = p.TeamCloth;

            // 2.2 面部分区靠大眼与墨焰承担，蛋体本身保持整块干净曲面：
            // 早期试过在腰部加一圈深色墨环，但圆柱与蛋形曲面的相交处会切出锯齿状碎边，观感很差，故不做。

            // 2.3 面部容器 (Face)：容器本身放在原点，所有部件直接用「蛋体身体空间」坐标贴面
            var faceGo = new GameObject("Face");
            faceGo.transform.SetParent(bodySquash, false);
            faceGo.transform.localPosition = Vector3.zero;
            var face = faceGo.transform;

            // 大眼与深色眼罩：眼睛贴在上半前脸（y≈0.72），由曲面采样保证既不陷入也不悬浮
            const float eyeY = 0.72f;
            leftEye = null;
            rightEye = null;
            for (int s = -1; s <= 1; s += 2)
            {
                float eyeX = s * 0.135f;
                string side = s < 0 ? "L" : "R";
                Quaternion faceRot = Quaternion.Euler(0f, EggFaceYaw(eyeX, eyeY), 0f);

                // 深色底托略大于白眼球，合成连体面罩的视觉轮廓
                var mask = CreateSphere(face, "EyeMask_" + side, EggFrontPoint(eyeX, eyeY, 0.004f), new Vector3(0.225f, 0.245f, 0.055f), p.Dark);
                mask.transform.localRotation = faceRot;

                var eye = CreateSphere(face, "Eye_" + side, EggFrontPoint(eyeX, eyeY, 0.012f), new Vector3(0.17f, 0.19f, 0.09f), p.White);
                eye.transform.localRotation = faceRot;
                // 瞳孔必须是「贴在眼球表面的一枚凸片」：眼球的局部空间半径是 0.5（Unity 球体直径为 1），
                // 放在 z=0.5 的极点处、厚度 0.35，才能从白眼球表面凸出来被看见；埋进眼球内部会完全不可见。
                CreateSphere(eye.transform, "Pupil", new Vector3(0f, -0.01f, 0.50f), new Vector3(0.55f, 0.55f, 0.35f), p.TeamHair);
                CreateSphere(eye.transform, "Glint", new Vector3(-0.16f, 0.16f, 0.62f), new Vector3(0.18f, 0.18f, 0.18f), p.White);
                CreateSphere(eye.transform, "Glint2", new Vector3(0.13f, -0.13f, 0.60f), new Vector3(0.10f, 0.10f, 0.10f), p.White);

                if (s < 0) leftEye = eye.transform;
                else rightEye = eye.transform;

                // 腮红贴在眼睛外下方
                float blushX = s * 0.255f;
                var blush = CreateSphere(face, "Blush_" + side, EggFrontPoint(blushX, 0.585f, 0.004f), new Vector3(0.10f, 0.055f, 0.035f), p.Blush);
                blush.transform.localRotation = Quaternion.Euler(0f, EggFaceYaw(blushX, 0.585f), 0f);
            }

            // 小 ω 嘴：极扁的深色椭圆，贴在两眼下方的中线上
            var mouth = CreateSphere(face, "Mouth", EggFrontPoint(0f, 0.60f, 0.004f), new Vector3(0.09f, 0.032f, 0.03f), p.Dark);
            mouth.transform.localRotation = Quaternion.identity;

            // 2.4 头顶墨焰触须束 (InkCrest)：根部 y = 0.98m，复用发辫惯性物理摆荡
            var crestGo = new GameObject("InkCrest");
            crestGo.transform.SetParent(bodySquash, false);
            crestGo.transform.localPosition = new Vector3(0f, 0.98f, 0f);

            var lHairGo = new GameObject("LeftHairPivot");
            lHairGo.transform.SetParent(crestGo.transform, false);
            lHairGo.transform.localPosition = new Vector3(-0.10f, 0f, 0f);
            leftHair = lHairGo.transform;
            BuildInkFlameCrest(leftHair, p, -1);

            var rHairGo = new GameObject("RightHairPivot");
            rHairGo.transform.SetParent(crestGo.transform, false);
            rHairGo.transform.localPosition = new Vector3(0.10f, 0f, 0f);
            rightHair = rHairGo.transform;
            BuildInkFlameCrest(rightHair, p, 1);

            // 2.5 后背紧凑型墨罐背包 (InkTankBackpack)：外推到 z=-0.40，让壳体真正凸出蛋体背面成为可见背包
            var backpackGo = new GameObject("InkTankBackpack");
            backpackGo.transform.SetParent(bodySquash, false);
            backpackGo.transform.localPosition = new Vector3(0f, 0.50f, -0.40f);

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
        /// 短圆手：袖管从肩部沿「肩 → 握把」方向伸到身前，末端是白色圆手套。
        /// inward 为横向收拢方向（左臂 +1 向身体中线，右臂 -1 同理）。
        /// </summary>
        private static void BuildMittenArm(Transform root, Palette p, int inward)
        {
            Vector3 toHand = new Vector3(inward * 0.16f, 0.06f, 0.30f);
            Vector3 mid = toHand * 0.5f;

            var sleeve = CreateCapsule(root, "Sleeve", mid,
                new Vector3(0.085f, toHand.magnitude * 0.5f, 0.085f), p.TeamCloth);
            sleeve.transform.localRotation = Quaternion.FromToRotation(Vector3.up, toHand.normalized);

            CreateSphere(root, "Hand", toHand, new Vector3(0.12f, 0.12f, 0.12f), p.White);
        }

        private static void BuildInkFlameCrest(Transform root, Palette p, int side)
        {
            // 3 段由粗到细、向后上方微弯的队伍色墨焰束
            var s1 = CreateCapsule(root, "FlameSeg1", new Vector3(0f, 0.05f, -0.015f), new Vector3(0.09f, 0.09f, 0.09f), p.TeamHair);
            s1.transform.localRotation = Quaternion.Euler(-12f, side * 10f, side * -5f);

            var s2 = CreateCapsule(root, "FlameSeg2", new Vector3(0f, 0.12f, -0.045f), new Vector3(0.075f, 0.08f, 0.075f), p.TeamHair);
            s2.transform.localRotation = Quaternion.Euler(-24f, side * 15f, side * -8f);

            var s3 = CreateCapsule(root, "FlameTip", new Vector3(0f, 0.18f, -0.085f), new Vector3(0.055f, 0.07f, 0.055f), p.TeamHair);
            s3.transform.localRotation = Quaternion.Euler(-36f, side * 20f, side * -12f);
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
        /// 长度由 0.62m 缩减至约 0.52m，横向 rx 拓宽至约 0.23m，竖向 ry 提升至 0.50*baseR。
        /// </summary>
        public static Mesh CreateSquidMesh()
        {
            var mesh = new Mesh { name = "SquidMantleMesh" };

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            const int ringCount = 15;
            const int radialSegs = 16;

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
    }
}
