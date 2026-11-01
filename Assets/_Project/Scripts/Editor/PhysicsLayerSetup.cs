using P1.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 物理层建立菜单：把 TagManager 的第 8/9 槽位命名为 P1_World / P1_Combatant。
    /// 走 SerializedObject 修改 ProjectSettings，不手改 YAML；幂等，重复执行不会重复建层。
    /// 层不存在时运行时 GameLayers 会回退 ~0，所以本菜单不是运行的必要条件。
    /// </summary>
    public static class PhysicsLayerSetup
    {
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";

        [MenuItem("P1/性能/0. 建立物理层", false, 0)]
        public static void EnsureLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError($"[PhysicsLayerSetup] 找不到 {TagManagerPath}，无法建立物理层。");
                return;
            }

            var serialized = new SerializedObject(assets[0]);
            SerializedProperty layers = serialized.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogError("[PhysicsLayerSetup] TagManager 缺少 layers 数组。");
                return;
            }

            bool changed = false;
            changed |= EnsureLayer(layers, GameLayers.WorldLayerIndex, GameLayers.WorldLayerName);
            changed |= EnsureLayer(layers, GameLayers.CombatantLayerIndex, GameLayers.CombatantLayerName);

            if (!changed)
            {
                // 层名可能已在内存里（上一条命令建过）但还没落盘，这里也补一次保存
                PersistProjectSettings(assets[0]);
                Debug.Log("[PhysicsLayerSetup] 物理层已存在，已确认写入 ProjectSettings。");
                return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            // ProjectSettings 资产不在常规 AssetDatabase 脏标记链路里，必须显式按资产保存，
            // 否则只在编辑器内存生效、重启后层名消失（实测 SaveAssets 不够）。
            PersistProjectSettings(assets[0]);
            GameLayers.InvalidateCache();

            Debug.Log($"[PhysicsLayerSetup] 已建立物理层：{GameLayers.WorldLayerName} = {GameLayers.WorldLayerIndex}，" +
                      $"{GameLayers.CombatantLayerName} = {GameLayers.CombatantLayerIndex}。");
        }

        private static void PersistProjectSettings(Object tagManagerAsset)
        {
            AssetDatabase.SaveAssetIfDirty(tagManagerAsset);
            AssetDatabase.SaveAssets();

            // 「Save Project」是让 ProjectSettings 立即落盘的可靠途径；
            // 但当前场景有未保存改动时绝不能调用（会把并行 Agent 的场景改动一起写盘）。
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorSceneManager.GetActiveScene().isDirty)
            {
                EditorApplication.ExecuteMenuItem("File/Save Project");
            }
        }

        private static bool EnsureLayer(SerializedProperty layers, int index, string layerName)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return false; // 已存在（可能被手动放在别的槽位）
            }

            if (index < 0 || index >= layers.arraySize)
            {
                Debug.LogError($"[PhysicsLayerSetup] 槽位 {index} 超出 layers 数组范围。");
                return false;
            }

            SerializedProperty slot = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(slot.stringValue) && slot.stringValue != layerName)
            {
                Debug.LogError($"[PhysicsLayerSetup] 槽位 {index} 已被 '{slot.stringValue}' 占用，未覆盖。");
                return false;
            }

            slot.stringValue = layerName;
            return true;
        }
    }
}