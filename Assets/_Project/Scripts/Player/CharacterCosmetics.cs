using System.Collections.Generic;
using P1.Core;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 把一套 <see cref="CosmeticSet"/> 刷到已装配好的角色上（运行时，可重复调用）。
    ///
    /// 三条设计约束决定了这里的做法：
    /// 1. <b>不重装配</b>：角色是编辑器烘焙进场景的，运行时不重建层级，只换材质引用 / 换头发网格 /
    ///    增删头饰；换装因此是"即时且零卡顿"的，衣柜里点一下就能看到。
    /// 2. <b>按「材质名前缀 + 作用域」定位</b>，而不是靠节点名逐个找：
    ///    - 编辑器烘焙出来的材质资产名带颜色后缀（<c>Mat_CharacterShirt_F0EDE3</c>），
    ///      运行时新建的则是不带后缀的 <c>Mat_CharacterShirt</c> —— 所以统一用 <c>StartsWith</c>；
    ///    - 武器件与眼睛/袜子共用 <c>Mat_AccentsWhite</c>、鞋壳与衣领/眼睛共用 <c>Mat_DarkPlastic</c>，
    ///      光看材质名会误伤 —— 于是加上作用域：武器件必须在 WeaponRoot 之下、鞋壳必须在 Ankle 之下。
    /// 3. <b>队伍色永不参与</b>：腰带 / 触手发色 / 瞳色 / 墨罐液面 / 乌贼体用的是 Mat_TeamHair、
    ///    Mat_TeamLiquid、Mat_SquidBody，这里一个都不碰（敌我可读性红线）。
    ///
    /// 材质是**共享缓存**的：同一 (源材质, 目标色) 只克隆一次，本地玩家与衣柜预览克隆共用同一份，
    /// 既不会每换一次装就漏一批材质，也不会增加 DrawCall 断批。
    /// </summary>
    public static class CharacterCosmetics
    {
        // 材质名常量（前缀匹配，见类注释第 2 条）
        private const string MatShirt = "Mat_CharacterShirt";
        private const string MatSleeve = "Mat_CharacterSleeve";
        private const string MatShorts = "Mat_CharacterShorts";
        private const string MatShoe = "Mat_CharacterShoe";
        private const string MatDarkPlastic = "Mat_DarkPlastic";
        private const string MatWeaponBody = "Mat_WeaponBody";
        private const string MatAccentsWhite = "Mat_AccentsWhite";
        private const string MatWeaponMetal = "Mat_WeaponMetal";
        private const string MatGunmetal = "Mat_Gunmetal";

        private const string HeadgearRootName = "CosmeticHeadgear";

        /// <summary>(源材质实例, 目标色) → 已克隆材质。有界：颜色组合数量恒定。</summary>
        private static readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>(32);

        /// <summary>6 束出厂方向（样式数据缺失时的兜底，与 BuildKidHierarchy 里的表一致）。</summary>
        private static readonly Vector3[] DefaultStrandDirs =
        {
            new Vector3(-0.88f, 0.30f, -0.10f).normalized,
            new Vector3(0.88f, 0.30f, -0.10f).normalized,
            new Vector3(-0.62f, 0.42f, -0.56f).normalized,
            new Vector3(0.62f, 0.42f, -0.56f).normalized,
            new Vector3(-0.28f, 0.52f, -0.80f).normalized,
            new Vector3(0.28f, 0.52f, -0.80f).normalized,
        };

        /// <summary>
        /// 应用整套外观。<paramref name="weaponRoot"/> 可传 null（预览台只关心人物时）；
        /// <paramref name="team"/> 只用于兜底（队伍色部件不在这里改）。
        /// </summary>
        public static void Apply(Transform kidRoot, Transform squidRoot, Transform weaponRoot,
            in CosmeticSet set, Team team)
        {
            if (kidRoot == null) return;

            // 每个槽位拿的是 CosmeticDef，真正要用的外观载荷在它对应的字段里（Outfit/Hair/...）
            OutfitPaletteDef outfit = set.Outfit.Outfit;
            HairStyleDef hair = set.Hair.Hair;
            WeaponSkinDef weaponSkin = set.WeaponSkin.WeaponSkin;
            HeadgearDef headgear = set.Headgear.Headgear;

            ApplyOutfit(kidRoot, outfit);
            ApplyHair(kidRoot, hair, headgear);
            ApplyWeaponSkin(weaponRoot, weaponSkin);
            ApplyHeadgear(kidRoot, headgear);
        }

        /// <summary>把当前档案里装备的一整套外观刷到角色上（控制器/预览台的便捷入口）。</summary>
        public static void ApplyFromProfile(Transform kidRoot, Transform squidRoot, Transform weaponRoot, Team team)
        {
            CosmeticSet set = CosmeticUnlockMath.ResolveSet(PlayerProfile.Current);
            Apply(kidRoot, squidRoot, weaponRoot, set, team);
        }

        // ==================== 服装配色 ====================

        private static void ApplyOutfit(Transform kidRoot, in OutfitPaletteDef outfit)
        {
            var renderers = kidRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                string n = MaterialName(r);
                if (n == null) continue;

                if (n.StartsWith(MatShirt, System.StringComparison.Ordinal))
                {
                    Recolor(r, outfit.Shirt);
                }
                else if (n.StartsWith(MatSleeve, System.StringComparison.Ordinal))
                {
                    Recolor(r, outfit.Sleeve);
                }
                else if (n.StartsWith(MatShorts, System.StringComparison.Ordinal))
                {
                    Recolor(r, outfit.Shorts);
                }
            }

            // 鞋壳：只在踝关节作用域内改 Mat_DarkPlastic（同名材质还用在衣领/眼睛/背包上，不能全局改）
            var ankles = new List<Transform>(2);
            CollectByName(kidRoot, "Ankle", ankles);
            for (int a = 0; a < ankles.Count; a++)
            {
                var ankleRenderers = ankles[a].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < ankleRenderers.Length; i++)
                {
                    Renderer r = ankleRenderers[i];
                    string n = MaterialName(r);
                    if (n == null) continue;
                    if (n.StartsWith(MatShoe, System.StringComparison.Ordinal)
                        || n.StartsWith(MatDarkPlastic, System.StringComparison.Ordinal))
                    {
                        Recolor(r, outfit.Shoe);
                    }
                }
            }
        }

        // ==================== 武器涂装 ====================

        private static void ApplyWeaponSkin(Transform weaponRoot, in WeaponSkinDef skin)
        {
            if (weaponRoot == null) return;

            var renderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                string n = MaterialName(r);
                if (n == null) continue;

                // 枪身面板（默认就是枪械白）与枪管金属件；墨罐 Mat_TeamLiquid 不动。
                if (n.StartsWith(MatWeaponBody, System.StringComparison.Ordinal)
                    || n.StartsWith(MatAccentsWhite, System.StringComparison.Ordinal))
                {
                    Recolor(r, skin.Body);
                }
                else if (n.StartsWith(MatWeaponMetal, System.StringComparison.Ordinal)
                         || n.StartsWith(MatGunmetal, System.StringComparison.Ordinal))
                {
                    Recolor(r, skin.Metal);
                }
            }
        }

        // ==================== 触手发型 ====================

        private static void ApplyHair(Transform kidRoot, in HairStyleDef style, in HeadgearDef headgear)
        {
            Transform hairRoot = FindDeep(kidRoot, "TentacleHair");
            if (hairRoot == null) return;

            // 戴帽子时把头发整体压细压短：发根种在头皮面上（离头心 ~0.14），而帽壳只比头大 5%，
            // 不压的话发束会直接从帽壳里穿出来（远看就是一个"橙色脑袋"，帽子形同虚设）。
            // 0.62 / 0.78 是"仍能看出发型、但发尖够不到帽壳内壁"的折中值。
            bool hatted = headgear.Kind != HeadgearKind.None;
            float radiusDamp = hatted ? 0.62f : 1f;
            float lengthDamp = hatted ? 0.78f : 1f;

            for (int i = 0; i < 6; i++)
            {
                Transform pivot = hairRoot.Find("HairPivot_" + i);
                if (pivot == null) continue;

                float radiusScale = At(style.RadiusScale, i, 1f) * radiusDamp;
                float lengthScale = At(style.LengthScale, i, 1f) * lengthDamp;

                // 发根布局：与装配时同一套公式（头皮采样 + 重力混合方向），保证运行时换发型不跑偏
                Vector3 dir = At(style.StrandDirs, i, DefaultStrandDirs[i]);
                if (dir.sqrMagnitude < 1e-6f) dir = DefaultStrandDirs[i];
                dir = dir.normalized;
                pivot.localPosition = CharacterVisualAssembler.SampleMochiHeadPoint(dir, 0.008f);
                Vector3 drop = (dir * 0.5f + Vector3.down * 0.92f + Vector3.back * 0.12f).normalized;
                pivot.localRotation = Quaternion.FromToRotation(Vector3.down, drop);

                ApplyHairSegment(pivot, "Tentacle_Hair_" + i, style, 0, radiusScale);

                Transform mid = pivot.Find("HairSkirt_" + i);
                if (mid != null)
                {
                    // 梢段枢轴跟着"这束有多长"缩放：马尾只有后脑两束伸长，其余缩到发根处
                    mid.localPosition = new Vector3(
                        style.MidPivotPosition.x,
                        style.MidPivotPosition.y * lengthScale,
                        style.MidPivotPosition.z * lengthScale);
                    mid.localRotation = Quaternion.Euler(style.MidPivotEuler);
                    ApplyHairSegment(mid, "Tentacle_Hair_Skirt" + i, style, 1, radiusScale);
                }
            }
        }

        private static void ApplyHairSegment(Transform parent, string childName, in HairStyleDef style,
            int segment, float radiusScale)
        {
            Transform child = parent.Find(childName);
            if (child == null) return;
            var filter = child.GetComponent<MeshFilter>();
            if (filter == null) return;
            filter.sharedMesh = CharacterVisualAssembler.BuildHairStyleMesh(style, segment, radiusScale);
        }

        // ==================== 头饰 ====================

        private static void ApplyHeadgear(Transform kidRoot, in HeadgearDef def)
        {
            Transform head = FindDeep(kidRoot, "Head");
            if (head == null) return;

            Transform existing = head.Find(HeadgearRootName);
            if (existing != null)
            {
                // 运行时用 Destroy；编辑器/测试里用 DestroyImmediate，否则旧帽子会留到帧尾
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
            }

            if (def.Kind == HeadgearKind.None) return;
            CharacterVisualAssembler.BuildHeadgear(head, def);
        }

        // ==================== 材质工具 ====================

        /// <summary>
        /// 给单个 Renderer 换上目标色的材质。颜色与源材质一致时<b>不克隆</b>，直接保留共享资产，
        /// 这样默认外观零额外材质、零额外 DrawCall 断批。
        /// </summary>
        private static void Recolor(Renderer renderer, Color color)
        {
            Material src = renderer.sharedMaterial;
            if (src == null) return;

            if (src.HasProperty("_BaseColor"))
            {
                Color cur = src.GetColor("_BaseColor");
                if (Approximately(cur, color)) return;
            }

            Material tinted = GetOrCreateTinted(src, color);
            if (tinted != null) renderer.sharedMaterial = tinted;
        }

        private static Material GetOrCreateTinted(Material src, Color color)
        {
            // 用「源材质名 + 目标色」做键，而不是 GetInstanceID —— 后者在 Unity 6.6 已标记过时
            // （改用 GetEntityId）。材质名本身就带角色语义（Mat_CharacterShirt 等），
            // 而烘焙出来的资产名还额外带颜色后缀，因此不会撞键。
            string hex = ColorUtility.ToHtmlStringRGB(color);
            string key = src.name + "|" + hex;
            if (_materialCache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            // 源材质被销毁（场景重载）时旧缓存会变 null，这里覆盖重建
            Material tinted = new Material(src) { name = src.name + "_Tint" + hex };
            // 双写 _BaseColor 与 _Color 别名：CharacterToon 两个属性都存在，谁生效都不会漏
            if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", color);
            if (tinted.HasProperty("_Color")) tinted.SetColor("_Color", color);
            tinted.enableInstancing = true;

            _materialCache[key] = tinted;
            return tinted;
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.004f
                   && Mathf.Abs(a.g - b.g) < 0.004f
                   && Mathf.Abs(a.b - b.b) < 0.004f
                   && Mathf.Abs(a.a - b.a) < 0.004f;
        }

        private static string MaterialName(Renderer renderer)
        {
            Material m = renderer != null ? renderer.sharedMaterial : null;
            return m != null ? m.name : null;
        }

        // ==================== 层级查找 ====================

        private static T At<T>(T[] array, int index, T fallback)
        {
            return array != null && index >= 0 && index < array.Length ? array[index] : fallback;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void CollectByName(Transform root, string name, List<Transform> into)
        {
            if (root == null || into == null) return;
            if (root.name == name)
            {
                into.Add(root);
                return;
            }
            for (int i = 0; i < root.childCount; i++)
            {
                CollectByName(root.GetChild(i), name, into);
            }
        }

        /// <summary>清掉材质缓存（仅测试/场景切换用）。</summary>
        public static void ClearCacheForTests()
        {
            _materialCache.Clear();
        }
    }
}
