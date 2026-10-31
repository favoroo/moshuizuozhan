using P1.Player;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 生成 <c>CharacterAnimConfig.asset</c>（角色程序化动画调参入口：形态手势曲线、
    /// 游动姿态、触须行波、表演点缀）。已存在时不覆盖（保留调参），只提示。
    /// </summary>
    public static class CharacterAnimConfigTools
    {
        private const string LogTag = "[角色动画]";

        public const string AnimConfigPath = "Assets/_Project/ScriptableObjects/CharacterAnimConfig.asset";

        [MenuItem("P1/角色动画/0. 生成角色动画配置 (CharacterAnimConfig.asset)", false, 40)]
        public static void CreateAnimConfigAsset()
        {
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
            }

            CharacterAnimConfigSO existing = AssetDatabase.LoadAssetAtPath<CharacterAnimConfigSO>(AnimConfigPath);
            if (existing != null)
            {
                Debug.Log(LogTag + " 动画配置已存在，保持现有调参不覆盖：" + AnimConfigPath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            CharacterAnimConfigSO config = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
            AssetDatabase.CreateAsset(config, AnimConfigPath);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log(LogTag + " CharacterAnimConfig.asset 已生成（默认值 = INKWAVE 数值）：" + AnimConfigPath);
            EditorGUIUtility.PingObject(config);
        }
    }
}
