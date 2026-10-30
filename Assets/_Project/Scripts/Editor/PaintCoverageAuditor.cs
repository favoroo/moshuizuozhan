using System.Collections.Generic;
using System.IO;
using System.Text;
using P1.Ink;
using UnityEditor;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// "P1/关卡与地图/9. 涂地覆盖率审计" 菜单包装：
    /// 真正的判定逻辑在运行时的 <see cref="PaintCoverageAudit"/>（EditMode 测试共用同一份规则），
    /// 这里只负责跑一遍、把结果写进 Logs/ 并打印到 Console。
    /// </summary>
    public static class PaintCoverageAuditor
    {
        public const string ReportPath = "Logs/paint_coverage_audit.txt";

        [MenuItem("P1/关卡与地图/9. 涂地覆盖率审计", false, 9)]
        public static void AuditMenuItem()
        {
            var violations = new List<string>();
            var warnings = new List<string>();
            int checkedCount = PaintCoverageAudit.RunScene(violations, warnings);

            var sb = new StringBuilder();
            sb.AppendLine("=== 涂地覆盖率审计报告 ===");
            sb.AppendLine($"检查对象: {checkedCount}，违规: {violations.Count}，提示: {warnings.Count}");
            sb.AppendLine();

            if (violations.Count > 0)
            {
                sb.AppendLine("[违规] 以下对象有可视表面，却既不能顶面受墨、也没有墨面立面：");
                for (int i = 0; i < violations.Count; i++)
                {
                    sb.AppendLine("  - " + violations[i]);
                }
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine("[提示] 以下对象虽有墨面，但顶面渲染器未注册进 inkableSurfaceRenderers（只丢波纹，不影响落墨）：");
                for (int i = 0; i < warnings.Count; i++)
                {
                    sb.AppendLine("  - " + warnings[i]);
                }
                sb.AppendLine();
            }

            if (violations.Count == 0 && warnings.Count == 0)
            {
                sb.AppendLine("全部通过：场上每个可视表面都能正常喷涂。");
            }

            string text = sb.ToString();
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(ReportPath, text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PaintCoverageAuditor] 写报告失败: {e.Message}");
            }

            if (violations.Count > 0)
            {
                Debug.LogError($"[PaintCoverageAuditor] 审计未通过：{violations.Count} 个表面无法喷涂。详见 {ReportPath}\n{text}");
            }
            else
            {
                Debug.Log($"[PaintCoverageAuditor] 审计通过（{checkedCount} 个对象，0 违规、{warnings.Count} 条提示）。报告：{ReportPath}");
            }
        }
    }
}