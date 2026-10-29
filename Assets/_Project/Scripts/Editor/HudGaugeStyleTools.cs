using P1.UI;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// P4-C HUD 仪表工具：生成 <c>HudGaugeStyle.asset</c>，并可把主题 / 仪表样式
    /// 精确绑定到当前场景已有的 <see cref="HudController"/>（只写两个序列化引用，不重建场景、不重刷 HUD）。
    /// </summary>
    public static class HudGaugeStyleTools
    {
        private const string LogTag = "[HUD仪表]";

        public const string GaugeStylePath = "Assets/_Project/ScriptableObjects/HudGaugeStyle.asset";

        private const string SlamIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_slam.png";
        private const string StormIconPath = "Assets/_Project/Art/Sprites/Icons/Specials/special_storm.png";

        [MenuItem("P1/UI/5. 生成 HUD 仪表样式资产 (HudGaugeStyle.asset)", false, 5)]
        public static void CreateGaugeStyleAsset()
        {
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder("Assets/_Project/ScriptableObjects"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "ScriptableObjects");
            }

            HudGaugeStyleSO existing = AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(GaugeStylePath);
            if (existing != null)
            {
                // 只补空的图标引用，不覆盖已调好的几何/节拍参数
                if (AssignSpecialIcons(existing))
                {
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log(LogTag + " 仪表样式已存在，保持现有调参不覆盖：" + GaugeStylePath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            HudGaugeStyleSO style = ScriptableObject.CreateInstance<HudGaugeStyleSO>();
            AssignSpecialIcons(style);
            AssetDatabase.CreateAsset(style, GaugeStylePath);
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();

            Debug.Log(LogTag + " HudGaugeStyle.asset 已生成：" + GaugeStylePath);
            EditorGUIUtility.PingObject(style);
        }

        [MenuItem("P1/UI/7. 同步大招技能图标引用 (Specials)", false, 7)]
        public static void SyncSpecialIcons()
        {
            AssetDatabase.Refresh();

            HudGaugeStyleSO style = AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(GaugeStylePath);
            if (style == null)
            {
                Debug.LogWarning(LogTag + " 缺少 HudGaugeStyle.asset，先跑 P1/UI/5。");
                return;
            }

            if (!AssignSpecialIcons(style))
            {
                Debug.Log(LogTag + " 大招技能图标引用已是最新，无需修改。");
                return;
            }

            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
            Debug.Log(LogTag + " 已同步大招技能图标引用（Slam/Storm）。");
        }

        /// <summary>只在引用为空时赋值，返回是否发生改动。</summary>
        private static bool AssignSpecialIcons(HudGaugeStyleSO style)
        {
            bool changed = false;

            if (style.slamIcon == null)
            {
                style.slamIcon = AssetDatabase.LoadAssetAtPath<Sprite>(SlamIconPath);
                changed |= style.slamIcon != null;
            }

            if (style.stormIcon == null)
            {
                style.stormIcon = AssetDatabase.LoadAssetAtPath<Sprite>(StormIconPath);
                changed |= style.stormIcon != null;
            }

            return changed;
        }

        [MenuItem("P1/UI/6. 绑定 UITheme/HudGaugeStyle 到当前场景 HUD", false, 6)]
        public static void BindCurrentSceneHud()
        {
            HudController hud = Object.FindAnyObjectByType<HudController>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning(LogTag + " 当前场景没有 HudController，跳过绑定。");
                return;
            }

            UIThemeSO theme = AssetDatabase.LoadAssetAtPath<UIThemeSO>(UIStyleTools.ThemePath);
            HudGaugeStyleSO style = AssetDatabase.LoadAssetAtPath<HudGaugeStyleSO>(GaugeStylePath);

            if (theme == null) Debug.LogWarning(LogTag + " 缺少 UITheme.asset，先跑 P1/UI/1。");
            if (style == null) Debug.LogWarning(LogTag + " 缺少 HudGaugeStyle.asset，先跑 P1/UI/5。");

            var serialized = new SerializedObject(hud);
            SerializedProperty themeProperty = serialized.FindProperty("uiTheme");
            SerializedProperty styleProperty = serialized.FindProperty("gaugeStyle");

            if (themeProperty != null && theme != null) themeProperty.objectReferenceValue = theme;
            if (styleProperty != null && style != null) styleProperty.objectReferenceValue = style;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);

            Debug.Log(LogTag + " 已绑定到 " + hud.name + "（场景需手动保存）。");
        }
    }
}
