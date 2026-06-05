#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
    public class AssetUtilityProductionWindow : EditorWindow
    {
        private Vector2 scrollPos;
        private List<AssetInfo> scannedAssets;
        private List<MeshOptimizationPlan> plans;
        private AssetUtilityOptimizationReport report;

        [MenuItem("Window/AssetUtility Production")] 
        public static void ShowWindow() => GetWindow<AssetUtilityProductionWindow>("AssetUtility Production");

        private void OnGUI()
        {
            GUILayout.Label("AssetUtility Production", EditorStyles.boldLabel);

            if (GUILayout.Button("Scan Active Scene"))
            {
                scannedAssets = SceneScanService.ScanActiveScene();
                report = new AssetUtilityOptimizationReport();
                report.before = AssetCostEstimator.BuildSnapshot("before", scannedAssets);
                report.StampNow("Scan");
            }

            if (scannedAssets != null && scannedAssets.Count > 0)
            {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
                foreach (var asset in scannedAssets)
                {
                    EditorGUILayout.LabelField(asset.name + " [" + asset.objectType + "]");
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Generate Mesh Plans"))
                {
                    plans = MeshOptimizationService.BuildPlansFromAssets(scannedAssets, true);
                    // Generate optimized mesh paths immediately
                    foreach (var p in plans)
                    {
                        if (MeshOptimizationService.TryGenerateOptimizedMesh(p, out string path, out string err))
                        {
                            p.generatedMeshPath = path;
                        }
                        else
                        {
                            Debug.LogWarning("Mesh plan failed: " + err);
                        }
                    }
                }

                if (plans != null && plans.Count > 0)
                {
                    if (GUILayout.Button("Apply Plans (Dry Run)"))
                    {
                        foreach (var p in plans) AssetApplyService.ApplyMeshPlan(p, true);
                        report.after = AssetCostEstimator.BuildSnapshot("after", scannedAssets);
                        report.warnings = OptimizationRiskAnalyzer.Analyze(scannedAssets);
                        JsonReportWriter.Write(report);
                        MarkdownReportWriter.Write(report);
                        CsvReportWriter.Write(report);
                    }

                    if (GUILayout.Button("Apply Plans (Actual)"))
                    {
                        foreach (var p in plans) AssetApplyService.ApplyMeshPlan(p, false);
                        report.after = AssetCostEstimator.BuildSnapshot("after", scannedAssets);
                        report.warnings = OptimizationRiskAnalyzer.Analyze(scannedAssets);
                        JsonReportWriter.Write(report);
                        MarkdownReportWriter.Write(report);
                        CsvReportWriter.Write(report);
                    }
                }
            }
        }
    }
}
#endif