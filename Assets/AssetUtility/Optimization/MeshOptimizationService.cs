#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
    public static class MeshOptimizationService
    {
        public static MeshOptimizationPlan CreatePlan(AssetInfo asset, int targetTriangleCount, bool dryRun)
        {
            if (asset == null) return null;
            return new MeshOptimizationPlan
            {
                sourceMeshPath = asset.meshPath,
                targetObjectPath = asset.objectPath,
                targetScenePath = asset.scenePath,
                originalTriangleCount = asset.polygonCount,
                targetTriangleCount = targetTriangleCount,
                dryRun = dryRun
            };
        }

        public static bool TryGenerateOptimizedMesh(MeshOptimizationPlan plan, out string generatedPath, out string error)
        {
            generatedPath = string.Empty;
            error = string.Empty;

            if (OptimizationRiskAnalyzer.HasBlockingMeshPlanError(plan, out error)) return false;

            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(plan.sourceMeshPath);
            if (source == null)
            {
                error = "Source mesh not found: " + plan.sourceMeshPath;
                return false;
            }

            Mesh optimized = null;
            try
            {
                // Reuse the existing optimizer as the implementation backend while the new service owns orchestration and validation.
                optimized = AssetOptimizer.Simplify(source, plan.targetTriangleCount);
            }
            catch (System.Exception ex)
            {
                error = "Mesh optimization failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            if (!MeshValidationService.IsValidMesh(optimized, out error)) return false;

            string safeName = MakeSafeFileName(source.name) + "_tris" + plan.targetTriangleCount;
            AssetOptimizer.SaveOptimizedMesh(optimized, safeName, out generatedPath);
            if (string.IsNullOrEmpty(generatedPath))
            {
                error = "Optimized mesh save failed.";
                return false;
            }

            return true;
        }

        public static List<MeshOptimizationPlan> BuildPlansFromAssets(IReadOnlyList<AssetInfo> assets, bool dryRun)
        {
            var plans = new List<MeshOptimizationPlan>();
            if (assets == null) return plans;

            for (int i = 0; i < assets.Count; i++)
            {
                AssetInfo asset = assets[i];
                if (asset == null || string.IsNullOrEmpty(asset.meshPath)) continue;
                if (asset.targetPolygonCount <= 0 || asset.targetPolygonCount >= asset.polygonCount) continue;
                plans.Add(CreatePlan(asset, asset.targetPolygonCount, dryRun));
            }

            return plans;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "OptimizedMesh";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
#endif
