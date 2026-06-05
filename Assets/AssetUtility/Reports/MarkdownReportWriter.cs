#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
    public static class MarkdownReportWriter
    {
        public static string Write(AssetUtilityOptimizationReport report, string fileName = "asset_utility_report.md")
        {
            if (report == null) return string.Empty;
            EnsureReportDirectory();

            string path = AssetUtilityProductionPaths.ReportRoot + "/" + SanitizeFileName(fileName, ".md");
            File.WriteAllText(path, ToMarkdown(report), Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[AssetUtility] Markdown report written: " + path);
            return path;
        }

        public static string ToMarkdown(AssetUtilityOptimizationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AssetUtility Optimization Report");
            sb.AppendLine();
            sb.AppendLine("- Tool: " + report.tool);
            sb.AppendLine("- Schema: " + report.schemaVersion);
            sb.AppendLine("- Generated: " + report.generatedAtLocal);
            sb.AppendLine("- Operation: " + report.operation);
            sb.AppendLine("- Duration: " + report.durationMs + " ms");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Metric | Before | After | Change |");
            sb.AppendLine("|---|---:|---:|---:|");
            AppendMetric(sb, "Objects scanned", report.before.objectsScanned, report.after.objectsScanned);
            AppendMetric(sb, "Meshes", report.before.meshObjects, report.after.meshObjects);
            AppendMetric(sb, "Textures", report.before.textureObjects, report.after.textureObjects);
            AppendMetric(sb, "Materials", report.before.materialObjects, report.after.materialObjects);
            AppendMetric(sb, "Triangles", report.before.totalTriangles, report.after.totalTriangles);
            AppendMetric(sb, "Texture VRAM MB", report.before.textureVramMB, report.after.textureVramMB);
            AppendMetric(sb, "Estimated Memory MB", report.before.estimatedMemoryMB, report.after.estimatedMemoryMB);
            sb.AppendLine();
            sb.AppendLine("## Changes");
            sb.AppendLine();
            if (report.changes.Count == 0) sb.AppendLine("No changes recorded.");
            for (int i = 0; i < report.changes.Count; i++)
            {
                var c = report.changes[i];
                sb.AppendLine("- `" + c.objectPath + "` " + c.componentType + ": `" + c.sourceAssetPath + "` -> `" + c.generatedAssetPath + "` " + c.note);
            }
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            if (report.warnings.Count == 0) sb.AppendLine("No warnings.");
            for (int i = 0; i < report.warnings.Count; i++)
            {
                var w = report.warnings[i];
                sb.AppendLine("- **" + w.code + "** `" + w.objectPath + "`: " + w.message);
            }
            sb.AppendLine();
            sb.AppendLine("## Errors");
            sb.AppendLine();
            if (report.errors.Count == 0) sb.AppendLine("No errors.");
            for (int i = 0; i < report.errors.Count; i++) sb.AppendLine("- " + report.errors[i]);
            return sb.ToString();
        }

        private static void AppendMetric(StringBuilder sb, string label, long before, long after)
        {
            sb.AppendLine("| " + label + " | " + before + " | " + after + " | " + (after - before) + " |");
        }

        private static void AppendMetric(StringBuilder sb, string label, float before, float after)
        {
            sb.AppendLine("| " + label + " | " + before.ToString("F2") + " | " + after.ToString("F2") + " | " + (after - before).ToString("F2") + " |");
        }

        private static void EnsureReportDirectory()
        {
            if (!Directory.Exists(AssetUtilityProductionPaths.GeneratedRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.GeneratedRoot);
            if (!Directory.Exists(AssetUtilityProductionPaths.ReportRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.ReportRoot);
        }

        private static string SanitizeFileName(string name, string extension)
        {
            if (string.IsNullOrEmpty(name)) name = "asset_utility_report" + extension;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (!name.EndsWith(extension)) name += extension;
            return name;
        }
    }
}
#endif
