using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Editor
{
    /// <summary>
    /// P12 渲染性能优化工具：静态合批开关与场景标记、微部件阴影裁剪、PC 阴影级联调优、GPU Resident Drawer 开关。
    /// 全部走 Unity API（PlayerSettings / SerializedObject / StaticEditorFlags），不手改 .asset / .unity YAML。
    /// </summary>
    public static class PerformanceOptimizeTools
    {
        private const string PcRpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string PcRpBackupPath = "Logs/PC_RPAsset.backup.asset";
        private const string PcRpGrdBackupPath = "Logs/PC_RPAsset.grd_backup.asset";

        /// <summary>渲染器最小包围盒边小于该值（米）视为微部件，关闭阴影投射（蛋体/枪身等大件远超此值）。</summary>
        private const float MicroPartShadowCullMeters = 0.12f;

        [MenuItem("P1/性能/2. 场景渲染优化：静态合批 + 微部件阴影裁剪 (P12)", false, 91)]
        public static void OptimizeSceneRendering()
        {
            bool staticWasEnabled = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.StandaloneOSX);
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.StandaloneOSX, true);
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.StandaloneWindows64, true);
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.Android, true);

            int markedStatic = 0;
            int shadowPruned = 0;
            int shadowCastersBefore = 0;

            var combatantType = typeof(P1.Player.CombatantHealth);
            var visualType = typeof(P1.Player.CharacterVisualController);
            var weaponType = typeof(P1.Weapons.WeaponController);
            var botType = typeof(P1.AI.BotController);
            var vfxType = typeof(P1.Core.VfxPool);

            // 只扫激活渲染器：非激活对象（未装备武器等）的 bounds 无效，会被误裁
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var report = new StringBuilder();
            report.AppendLine("[P12] 渲染优化开始");

            foreach (var rend in renderers)
            {
                if (rend == null) continue;

                var go = rend.gameObject;
                // 角色本体 / 武器 / Bot 逻辑体 / VFX 池 / 动态刚体不参与静态合批
                bool dynamicObject = go.GetComponentInParent(combatantType) != null
                                     || go.GetComponentInParent(visualType) != null
                                     || go.GetComponentInParent(weaponType) != null
                                     || go.GetComponentInParent(botType) != null
                                     || go.GetComponentInParent(vfxType) != null
                                     || go.GetComponentInParent<Rigidbody>() != null
                                     || go.GetComponentInParent<Canvas>() != null;

                if (!dynamicObject && (GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.BatchingStatic) == 0)
                {
                    GameObjectUtility.SetStaticEditorFlags(go,
                        GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.BatchingStatic);
                    markedStatic++;
                }

                // 微部件阴影裁剪： egg 体/枪身等大件保留投影，2~5cm 的瞳孔/高光/螺栓不再画进 shadowmap
                if (rend.shadowCastingMode != ShadowCastingMode.Off)
                {
                    shadowCastersBefore++;
                    Bounds b = rend.bounds;
                    float minAxis = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
                    if (minAxis < MicroPartShadowCullMeters)
                    {
                        rend.shadowCastingMode = ShadowCastingMode.Off;
                        EditorUtility.SetDirty(rend);
                        shadowPruned++;
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            report.AppendLine($"  静态合批开关：{(staticWasEnabled ? "原本已启用" : "本次启用")}（Standalone/Android）");
            report.AppendLine($"  标记 BatchingStatic：{markedStatic} 个对象");
            report.AppendLine($"  微部件阴影裁剪：{shadowPruned} / {shadowCastersBefore} 个投影体关闭");
            report.AppendLine("  已保存场景。重跑一次对局采样可对比 DrawCalls。");
            Debug.Log(report.ToString());
        }

        [MenuItem("P1/性能/3. PC 阴影级联 4→2 与距离 50→40 调优 (P12)", false, 92)]
        public static void TunePcShadowSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(PcRpAssetPath);
            if (asset == null)
            {
                Debug.LogError($"[P12] 找不到 {PcRpAssetPath}");
                return;
            }

            Directory.CreateDirectory("Logs");
            if (!File.Exists(PcRpBackupPath))
            {
                File.Copy(PcRpAssetPath, PcRpBackupPath, false);
            }

            var so = new SerializedObject(asset);
            SerializedProperty cascades = so.FindProperty("m_ShadowCascadeCount");
            SerializedProperty distance = so.FindProperty("m_ShadowDistance");
            if (cascades == null || distance == null)
            {
                Debug.LogError("[P12] PC_RPAsset 缺少 m_ShadowCascadeCount / m_ShadowDistance 字段，URP 版本可能不同。");
                return;
            }

            cascades.intValue = 2;
            distance.floatValue = 40f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[P12] PC 阴影调优完成：级联 {cascades.intValue}、距离 {distance.floatValue}m（原值备份在 {PcRpBackupPath}）。" +
                      "如需回退，把备份复制回 Assets/Settings/PC_RPAsset.asset。");
        }

        /// <summary>
        /// GPU Resident Drawer 开关（P12-C）：只动 PC_RPAsset（Forward+ 桌面）。
        /// Mobile_RPAsset 明确不动：移动端首版不启用 GRD——GLES3 走 UBO 路径开销更高、收益依赖 SRP 兼容网格占比，
        /// DOTS 变体已随 shader 进包，后续可在真机采样验证后再单独开启。
        /// 字段名已对照 URP 源码核实：Runtime/Data/UniversalRenderPipelineAsset.cs 的
        /// m_GPUResidentDrawerMode（枚举 0=Disabled / 1=InstancedDrawing，见 core RP IGPUResidentRenderPipeline.cs）。
        /// </summary>
        [MenuItem("P1/性能/4. GPU Resident Drawer：PC 启用/停用切换 (P12-C)", false, 93)]
        public static void TogglePcGpuResidentDrawer()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(PcRpAssetPath);
            if (asset == null)
            {
                Debug.LogError($"[P12-C] 找不到 {PcRpAssetPath}");
                return;
            }

            var so = new SerializedObject(asset);

            // 防御 + 核实：枚举顶层属性，打印含 Drawer / GPUResident / SmallMesh 的真实字段名与当前值
            var grdProps = new StringBuilder();
            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = false; // 只列顶层，不递归
                if (it.name.Contains("Drawer") || it.name.Contains("GPUResident") || it.name.Contains("SmallMesh"))
                {
                    string value = it.propertyType switch
                    {
                        SerializedPropertyType.Integer => it.intValue.ToString(),
                        SerializedPropertyType.Float => it.floatValue.ToString("0.##"),
                        SerializedPropertyType.Boolean => it.boolValue.ToString(),
                        _ => it.propertyType.ToString(),
                    };
                    grdProps.AppendLine($"  {it.propertyPath} = {value}");
                }
            }

            SerializedProperty mode = so.FindProperty("m_GPUResidentDrawerMode");
            if (mode == null)
            {
                // 字段名对不上时列出全部顶层属性，方便排查 URP 版本差异
                var allProps = new StringBuilder();
                var it2 = so.GetIterator();
                bool enter = true;
                while (it2.Next(enter))
                {
                    enter = false;
                    allProps.AppendLine("  " + it2.propertyPath);
                }
                Debug.LogError($"[P12-C] PC_RPAsset 找不到 m_GPUResidentDrawerMode 字段（URP 版本可能不同）。\n顶层属性清单：\n{allProps}");
                return;
            }

            // 0=Disabled（默认），1=InstancedDrawing（GRD 用 BatchRendererGroup 提交 draw）
            bool willEnable = mode.intValue != 1;

            // 切换前备份（覆盖旧备份没关系）
            Directory.CreateDirectory("Logs");
            File.Copy(PcRpAssetPath, PcRpGrdBackupPath, true);

            mode.intValue = willEnable ? 1 : 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            if (willEnable)
            {
                EnsureBrgVariantsKeepAll();
            }

            Debug.Log($"[P12-C] GPU Resident Drawer（PC）已切换为 {(willEnable ? "启用（1=InstancedDrawing）" : "停用（0=Disabled）")}。\n" +
                      $"  当前 GRD 相关字段：\n{grdProps}" +
                      $"  切换前的资产已备份到 {PcRpGrdBackupPath}。\n" +
                      "  生效时机：渲染管线资产重新加载后（重进 Play / 重新聚焦场景视图）。\n" +
                      "  前提：renderer 的 shader 需带 DOTS_INSTANCING_ON 变体（P12-C 已给 5 个自研 shader 共 12 个 pass 补齐）；" +
                      "无变体或带 MPB 的 renderer 自动回退普通路径渲染，正确性不受影响。");
        }

        /// <summary>
        /// GRD 依赖 DOTS instancing 变体进包：构建期剥离（BatchRendererGroup Variants ≠ Keep All）会把它们全部剪掉，
        /// 玩家包里 GRD 静默失效。启用 GRD 时强制 Keep All（Graphics > BatchRendererGroup Variants，m_BrgStripping=2）。
        /// </summary>
        private static void EnsureBrgVariantsKeepAll()
        {
            var graphicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsAssets == null || graphicsAssets.Length == 0)
            {
                Debug.LogError("[P12-C] 无法加载 ProjectSettings/GraphicsSettings.asset，请手动把 Graphics > BatchRendererGroup Variants 设为 Keep All。");
                return;
            }

            var so = new SerializedObject(graphicsAssets[0]);
            SerializedProperty brg = so.FindProperty("m_BrgStripping");
            if (brg == null)
            {
                Debug.LogError("[P12-C] GraphicsSettings 缺少 m_BrgStripping 字段（Unity 版本差异），请手动把 BatchRendererGroup Variants 设为 Keep All。");
                return;
            }

            if (brg.intValue == 2)
            {
                return; // 已是 Keep All
            }

            brg.intValue = 2; // 0=Strip if Entities Graphics 未安装 / 1=Strip All / 2=Keep All
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[P12-C] Graphics > BatchRendererGroup Variants 已设为 Keep All，防止玩家包构建时剥离 DOTS instancing 变体导致 GRD 失效。");
        }

        [MenuItem("P1/性能/9. 渲染统计报告 (P12)", false, 99)]
        public static void ReportRenderingStats()
        {
            int renderers = 0;
            int casters = 0;
            int staticFlagged = 0;

            var allRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var rend in allRenderers)
            {
                if (rend == null) continue;
                renderers++;
                if (rend.shadowCastingMode != ShadowCastingMode.Off) casters++;
                if ((GameObjectUtility.GetStaticEditorFlags(rend.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                    staticFlagged++;
            }

            bool sb = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.StandaloneOSX);
            Debug.Log($"[P12] 场景 MeshRenderer={renderers}（含未激活），投影体={casters}，BatchingStatic={staticFlagged}；" +
                      $"平台静态合批 StandaloneOSX={(sb ? "开" : "关")}。");
        }
    }
}
