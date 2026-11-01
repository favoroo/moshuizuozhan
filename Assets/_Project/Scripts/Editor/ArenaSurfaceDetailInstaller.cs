using P1.Environment;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 道具材质细节安装器：将程序化生成的木材与布料法线 (_N) 及材质掩码 (_RMO)
    /// 贴图与 PBR 细节参数接入木箱、木板与布料材质中，提升近景道具质感。
    /// 绝不手改 .mat YAML，完全通过 Unity API 幂等写入。
    /// </summary>
    public static class ArenaSurfaceDetailInstaller
    {
        private const string StylePath = "Assets/_Project/ScriptableObjects/ArenaStyle.asset";

        private const string WoodNormalPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Wood_N.png";
        private const string WoodRmoPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Wood_RMO.png";
        private const string ClothNormalPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Cloth_N.png";
        private const string ClothRmoPath = "Assets/_Project/Art/Textures/Surfaces/Surfaces_Cloth_RMO.png";

        private const string MatWoodPath = "Assets/_Project/Materials/Mat_Prop_Wood.mat";
        private const string MatWoodDarkPath = "Assets/_Project/Materials/Mat_Prop_WoodDark.mat";
        private const string MatClothPath = "Assets/_Project/Materials/Mat_Prop_Cloth.mat";

        private static readonly int SurfaceNId = Shader.PropertyToID("_SurfaceN");
        private static readonly int SurfaceRmoId = Shader.PropertyToID("_SurfaceRMO");
        private static readonly int SurfaceTexelScaleId = Shader.PropertyToID("_SurfaceTexelScale");
        private static readonly int SurfaceNormalStrengthId = Shader.PropertyToID("_SurfaceNormalStrength");
        private static readonly int SurfaceAoStrengthId = Shader.PropertyToID("_SurfaceAOStrength");
        private static readonly int SurfaceSmoothnessScaleId = Shader.PropertyToID("_SurfaceSmoothnessScale");

        [MenuItem("P1/画质/1. 应用道具材质细节", false, 10)]
        public static void ApplyAll()
        {
            // 确保新生成的 PNG 贴图已被 AssetDatabase 索引
            AssetDatabase.Refresh();

            var style = AssetDatabase.LoadAssetAtPath<ArenaStyleSO>(StylePath);
            float baseNormalStrength = 0.6f;
            float baseAoStrength = 0.7f;

            if (style != null)
            {
                baseNormalStrength = style.surfaceNormalStrength;
                baseAoStrength = style.surfaceAOStrength;
            }
            else
            {
                Debug.LogWarning($"[ArenaSurfaceDetailInstaller] 未找到风格配置: {StylePath}，使用保守回退值 (normal: {baseNormalStrength}, AO: {baseAoStrength})。");
            }

            int modifiedCount = 0;

            // 1. 木材材质 (Mat_Prop_Wood 与 Mat_Prop_WoodDark)
            float woodTexelScale = 0.75f;
            float woodNormalStrength = baseNormalStrength * 1.15f;
            float woodAoStrength = baseAoStrength;
            float woodSmoothnessScale = 0.75f;

            if (ApplyMaterialProperties(MatWoodPath, WoodNormalPath, WoodRmoPath, woodTexelScale, woodNormalStrength, woodAoStrength, woodSmoothnessScale))
            {
                modifiedCount++;
            }

            if (ApplyMaterialProperties(MatWoodDarkPath, WoodNormalPath, WoodRmoPath, woodTexelScale, woodNormalStrength, woodAoStrength, woodSmoothnessScale))
            {
                modifiedCount++;
            }

            // 2. 布料材质 (Mat_Prop_Cloth)
            float clothTexelScale = 1.1f;
            float clothNormalStrength = baseNormalStrength * 0.8f;
            float clothAoStrength = baseAoStrength;
            float clothSmoothnessScale = 0.55f;

            if (ApplyMaterialProperties(MatClothPath, ClothNormalPath, ClothRmoPath, clothTexelScale, clothNormalStrength, clothAoStrength, clothSmoothnessScale))
            {
                modifiedCount++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[ArenaSurfaceDetailInstaller] 道具材质细节应用完成：成功更新 {modifiedCount} 个材质 (Mat_Prop_Wood, Mat_Prop_WoodDark, Mat_Prop_Cloth)。");
        }

        private static bool ApplyMaterialProperties(
            string matPath,
            string normalPath,
            string rmoPath,
            float texelScale,
            float normalStrength,
            float aoStrength,
            float smoothnessScale)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Debug.LogWarning($"[ArenaSurfaceDetailInstaller] 未找到目标材质: {matPath}");
                return false;
            }

            bool changed = false;

            if (mat.HasProperty(SurfaceNId))
            {
                var normTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normTex != null)
                {
                    mat.SetTexture(SurfaceNId, normTex);
                    changed = true;
                }
                else
                {
                    Debug.LogWarning($"[ArenaSurfaceDetailInstaller] 未找到法线贴图: {normalPath}");
                }
            }

            if (mat.HasProperty(SurfaceRmoId))
            {
                var rmoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rmoPath);
                if (rmoTex != null)
                {
                    mat.SetTexture(SurfaceRmoId, rmoTex);
                    changed = true;
                }
                else
                {
                    Debug.LogWarning($"[ArenaSurfaceDetailInstaller] 未找到 RMO 贴图: {rmoPath}");
                }
            }

            if (mat.HasProperty(SurfaceTexelScaleId))
            {
                mat.SetFloat(SurfaceTexelScaleId, texelScale);
                changed = true;
            }

            if (mat.HasProperty(SurfaceNormalStrengthId))
            {
                mat.SetFloat(SurfaceNormalStrengthId, normalStrength);
                changed = true;
            }

            if (mat.HasProperty(SurfaceAoStrengthId))
            {
                mat.SetFloat(SurfaceAoStrengthId, aoStrength);
                changed = true;
            }

            if (mat.HasProperty(SurfaceSmoothnessScaleId))
            {
                mat.SetFloat(SurfaceSmoothnessScaleId, smoothnessScale);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(mat);
            }

            return true;
        }
    }
}
