#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes dry-run/apply/rollback and mesh-quality reports for portfolio and production review.
/// The writer is deliberately small and dependency-free so it can be called from Editor tests,
/// the AssetUtility window, or future batch CI runners.
/// </summary>
public static class AssetUtilityReportWriter
{
    public static void WriteOptimizationReport(
        string directory,
        AssetOptimizationPlan plan,
        AssetOptimizationResult result,
        MeshQualityReport meshQualityReport)
    {
        if (string.IsNullOrEmpty(directory)) directory = "Docs/Reports";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "assetutility_optimization_report.md"), ToMarkdown(plan, result, meshQualityReport));
        File.WriteAllText(Path.Combine(directory, "assetutility_optimization_report.json"), ToJson(plan, result, meshQualityReport));
    }

    public static string ToMarkdown(AssetOptimizationPlan plan, AssetOptimizationResult result, MeshQualityReport meshQualityReport)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AssetUtility Optimization Report");
        sb.AppendLine();
        sb.AppendLine("Generated UTC: `" + DateTime.UtcNow.ToString("o") + "`");
        sb.AppendLine();
        sb.AppendLine("## Plan");
        sb.AppendLine();
        sb.AppendLine("| Kind | Target | Before | After | Method |");
        sb.AppendLine("|---|---|---:|---:|---|");
        if (plan != null)
        {
            foreach (MeshOptimizationCommand cmd in plan.MeshCommands)
                sb.AppendLine("| Mesh | " + EscapeTable(cmd.ObjectPath) + " | " + cmd.OriginalTriangles + " | " + cmd.TargetTriangles + " | " + EscapeTable(cmd.Method) + " |");
            foreach (TextureOptimizationCommand cmd in plan.TextureCommands)
                sb.AppendLine("| Texture | " + EscapeTable(cmd.TexturePath) + " | " + cmd.OriginalMaxSize + " | " + cmd.TargetMaxSize + " | TextureImporter |");
        }
        sb.AppendLine();
        sb.AppendLine("## Result");
        sb.AppendLine();
        sb.AppendLine("Success: `" + (result != null && result.Success) + "`");
        sb.AppendLine();
        if (meshQualityReport != null)
        {
            sb.AppendLine("## Mesh quality gate");
            sb.AppendLine();
            sb.AppendLine(meshQualityReport.ToMarkdown());
        }
        sb.AppendLine();
        sb.AppendLine("## Rollback commands");
        sb.AppendLine();
        if (result != null && result.RollbackCommands.Count > 0)
        {
            foreach (string command in result.RollbackCommands) sb.AppendLine("- " + command);
        }
        else
        {
            sb.AppendLine("- none");
        }
        sb.AppendLine();
        sb.AppendLine("## Warnings / Errors");
        sb.AppendLine();
        if (result != null)
        {
            foreach (string warning in result.Warnings) sb.AppendLine("- WARNING: " + warning);
            foreach (string error in result.Errors) sb.AppendLine("- ERROR: " + error);
        }
        return sb.ToString();
    }

    public static string ToJson(AssetOptimizationPlan plan, AssetOptimizationResult result, MeshQualityReport meshQualityReport)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"generatedUtc\": \"" + EscapeJson(DateTime.UtcNow.ToString("o")) + "\",");
        sb.AppendLine("  \"success\": " + ((result != null && result.Success) ? "true" : "false") + ",");
        sb.AppendLine("  \"meshCommands\": [");
        if (plan != null)
        {
            for (int i = 0; i < plan.MeshCommands.Count; i++)
            {
                MeshOptimizationCommand cmd = plan.MeshCommands[i];
                sb.Append("    { \"objectPath\": \"").Append(EscapeJson(cmd.ObjectPath)).Append("\", \"sourceMeshPath\": \"").Append(EscapeJson(cmd.SourceMeshPath))
                  .Append("\", \"originalTriangles\": ").Append(cmd.OriginalTriangles).Append(", \"targetTriangles\": ").Append(cmd.TargetTriangles)
                  .Append(", \"method\": \"").Append(EscapeJson(cmd.Method)).Append("\" }");
                if (i + 1 < plan.MeshCommands.Count) sb.Append(',');
                sb.AppendLine();
            }
        }
        sb.AppendLine("  ],");
        sb.AppendLine("  \"meshQuality\": " + MeshQualityToJson(meshQualityReport) + ",");
        sb.AppendLine("  \"rollbackCommands\": [");
        if (result != null)
        {
            for (int i = 0; i < result.RollbackCommands.Count; i++)
            {
                sb.Append("    \"").Append(EscapeJson(result.RollbackCommands[i])).Append("\"");
                if (i + 1 < result.RollbackCommands.Count) sb.Append(',');
                sb.AppendLine();
            }
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string MeshQualityToJson(MeshQualityReport report)
    {
        if (report == null) return "null";
        return "{ \"originalTriangles\": " + report.OriginalTriangles +
               ", \"finalTriangles\": " + report.FinalTriangles +
               ", \"degenerateTriangles\": " + report.DegenerateTriangles +
               ", \"invalidIndices\": " + report.InvalidIndices +
               ", \"boundaryEdges\": " + report.BoundaryEdges +
               ", \"minTriangleArea\": " + report.MinTriangleArea.ToString("0.########", CultureInfo.InvariantCulture) +
               ", \"maxTriangleArea\": " + report.MaxTriangleArea.ToString("0.########", CultureInfo.InvariantCulture) +
               ", \"normalsValid\": " + (report.NormalsValid ? "true" : "false") +
               ", \"passed\": " + (report.Passed ? "true" : "false") + " }";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static string EscapeTable(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
    }
}
#endif
