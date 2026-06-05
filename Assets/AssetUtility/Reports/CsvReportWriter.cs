#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
    public static class CsvReportWriter
    {
        public static string Write(AssetUtilityOptimizationReport report, string fileName = "asset_utility_changes.csv")
        {
            if (report == null) return string.Empty;
            EnsureReportDirectory();

            string path = AssetUtilityProductionPaths.ReportRoot + "/" + SanitizeFileName(fileName);
            var sb = new StringBuilder();
            sb.AppendLine("objectPath,scenePath,componentType,sourceAssetPath,generatedAssetPath,note");
            for (int i = 0; i < report.changes.Count; i++)
            {
                var c = report.changes[i];
                sb.AppendLine(Escape(c.objectPath) + "," + Escape(c.scenePath) + "," + Escape(c.componentType) + "," + Escape(c.sourceAssetPath) + "," + Escape(c.generatedAssetPath) + "," + Escape(c.note));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[AssetUtility] CSV report written: " + path);
            return path;
        }

        private static string Escape(string value)
        {
            if (value == null) value = string.Empty;
            value = value.Replace("\"", "\"\"");
            return "\"" + value + "\"";
        }

        private static void EnsureReportDirectory()
        {
            if (!Directory.Exists(AssetUtilityProductionPaths.GeneratedRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.GeneratedRoot);
            if (!Directory.Exists(AssetUtilityProductionPaths.ReportRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.ReportRoot);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "asset_utility_changes.csv";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (!name.EndsWith(".csv")) name += ".csv";
            return name;
        }
    }
}
#endif
