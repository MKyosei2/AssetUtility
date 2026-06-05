#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
    public static class JsonReportWriter
    {
        public static string Write(AssetUtilityOptimizationReport report, string fileName = "asset_utility_report.json")
        {
            if (report == null) return string.Empty;
            EnsureReportDirectory();

            string path = AssetUtilityProductionPaths.ReportRoot + "/" + SanitizeFileName(fileName);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[AssetUtility] JSON report written: " + path);
            return path;
        }

        private static void EnsureReportDirectory()
        {
            if (!Directory.Exists(AssetUtilityProductionPaths.GeneratedRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.GeneratedRoot);
            if (!Directory.Exists(AssetUtilityProductionPaths.ReportRoot)) Directory.CreateDirectory(AssetUtilityProductionPaths.ReportRoot);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "asset_utility_report.json";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (!name.EndsWith(".json")) name += ".json";
            return name;
        }
    }
}
#endif
