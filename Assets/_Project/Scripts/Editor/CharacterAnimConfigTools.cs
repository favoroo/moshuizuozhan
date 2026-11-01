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

        /// <summary>
        /// P33 真骨骼步态迁移：现有资产已序列化旧值，写入 P33 骨骼步态、触须弹簧链与脊柱分配字段。
        /// </summary>
        [MenuItem("P1/角色动画/1. 迁移步态参数到 P33 真骨骼步态", false, 41)]
        public static void MigrateGaitToP33()
        {
            CharacterAnimConfigSO config = AssetDatabase.LoadAssetAtPath<CharacterAnimConfigSO>(AnimConfigPath);
            if (config == null)
            {
                Debug.LogWarning(LogTag + " 未找到 " + AnimConfigPath + "，请先运行菜单 0 生成");
                return;
            }

            config.kidCadenceMin = KidGaitSolver.CadenceMin;
            config.kidCadenceMax = KidGaitSolver.CadenceMax;
            config.kidBobSlow = KidGaitSolver.BobSlow;
            config.kidBobFast = KidGaitSolver.BobFast;
            config.kidRockSlowDeg = KidGaitSolver.RockSlow;
            config.kidRockFastDeg = KidGaitSolver.RockFast;
            config.kidSquashDepth = KidGaitSolver.SquashDepth;
            config.kidSquashBulge = KidGaitSolver.SquashBulge;
            config.kidArmSwingSlowDeg = KidGaitSolver.ArmSwingSlow;
            config.kidArmSwingFastDeg = KidGaitSolver.ArmSwingFast;
            config.kidLegSwingSlowDeg = KidGaitSolver.LegSwingSlow;
            config.kidLegSwingFastDeg = KidGaitSolver.LegSwingFast;
            config.kidKneeBendSlowDeg = KidGaitSolver.KneeBendSlow;
            config.kidKneeBendFastDeg = KidGaitSolver.KneeBendFast;
            config.kidToeUpDeg = KidGaitSolver.ToeUpMax;
            config.kidToeOffDeg = KidGaitSolver.ToeOffMax;
            config.kidLegLength = KidGaitSolver.LegLengthDefault;
            config.kidHipDropMaxMeters = 0.035f;
            config.kidElbowBendBaseDeg = KidGaitSolver.ElbowBendBase;
            config.kidElbowBendFastDeg = KidGaitSolver.ElbowBendFast;
            config.kidElbowSwingGainDeg = KidGaitSolver.ElbowSwingGain;
            config.kidLeanSlowDeg = KidGaitSolver.LeanSlow;
            config.kidLeanFastDeg = KidGaitSolver.LeanFast;
            config.kidHipLeanShare = KidGaitSolver.HipLeanShare;
            config.kidSpineLeanShare = KidGaitSolver.SpineLeanShare;
            config.kidChestLeanShare = KidGaitSolver.ChestLeanShare;
            config.kidSpineDynamicShare = 0.45f;
            config.kidChestDynamicShare = 0.35f;
            config.headStabilizePitchShare = 0.70f;
            config.hairRootOmega = 16f;
            config.hairMidOmega = 12f;
            config.hairAccelGain = 0.40f;
            config.hairIdleWaveDeg = 3.5f;
            config.hairRootZeta = 0.38f;
            config.hairMidWhip = 1.35f;
            config.hairMidZeta = 0.30f;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log(LogTag + " 步态参数已迁移到 P33 真骨骼步态：cadenceMax=9.0、长腿 0.433m、脊柱分配 25/45/30、触须弹簧已激活。");
            EditorGUIUtility.PingObject(config);
        }
    }
}
