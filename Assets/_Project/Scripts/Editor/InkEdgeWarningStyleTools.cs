using P1.UI;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 生成踩敌墨屏幕边缘墨迹的样式资产（P18-A）。
    /// 只创建缺失的资产，已存在时保留现场调参，不覆盖。
    /// 资产放在 Resources 下：运行时 <see cref="InkEdgeWarningView"/> 用 Resources.Load 读取，
    /// 独立包同样生效；缺失时视图退回 <see cref="InkEdgeWarningStyleSO.Fallback"/> 代码默认值。
    /// </summary>
    public static class InkEdgeWarningStyleTools
    {
        private const string StylePath = "Assets/_Project/Resources/InkEdgeWarningStyle.asset";

        [MenuItem("P1/UI/11. 生成踩敌墨边缘样式资产 (InkEdgeWarningStyle)", false, 11)]
        public static void CreateStyleAsset()
        {
            InkEdgeWarningStyleSO existing = AssetDatabase.LoadAssetAtPath<InkEdgeWarningStyleSO>(StylePath);
            if (existing != null)
            {
                Debug.Log("[InkEdgeWarning] 样式资产已存在，保留现有调参不覆盖：" + StylePath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            InkEdgeWarningStyleSO style = ScriptableObject.CreateInstance<InkEdgeWarningStyleSO>();
            AssetDatabase.CreateAsset(style, StylePath);
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[InkEdgeWarning] InkEdgeWarningStyle.asset 已生成：" + StylePath);
            EditorGUIUtility.PingObject(style);
        }
    }
}
