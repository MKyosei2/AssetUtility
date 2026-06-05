#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EasyTool
{
    public static class OptimizationRiskAnalyzer
    {
        public static List<AssetUtilityWarningRecord> Analyze(IReadOnlyList<AssetInfo> assets)
        {
            var warnings = new List<AssetUtilityWarningRecord>();
            if (assets == null) return warnings;

            for (int i = 0; i < assets.Count; i++)
            {
                AssetInfo asset = assets[i];
                if (asset == null) continue;

                if (string.IsNullOrEmpty(asset.meshPath) && asset.polygonCount > 0)
                {
                    warnings.Add(Make(asset, "MESH_PATH_MISSING", "Mesh exists but AssetDatabase path is missing. Reference replacement may be unsafe."));
                }

                if (asset.targetPolygonCount > 0 && asset.polygonCount > 0 && asset.targetPolygonCount >= asset.polygonCount)
                {
                    warnings.Add(Make(asset, "INVALID_TARGET_TRIANGLES", "Target triangle count must be smaller than the original triangle count."));
                }

                if (asset.objectType != null && asset.objectType.Contains("Skinned"))
                {
                    warnings.Add(Make(asset, "SKINNED_MESH_RISK", "SkinnedMeshRenderer optimization requires visual validation for skin weights, bindposes, and blend shapes."));
                }

                if (!string.IsNullOrEmpty(asset.texturePath) && asset.resolution.x >= 4096 || asset.resolution.y >= 4096)
                {
                    warnings.Add(Make(asset, "LARGE_TEXTURE", "Large texture detected. Prefer TextureImporter max size/platform override workflow over PNG replacement."));
                }
            }

            return warnings;
        }

        public static bool HasBlockingMeshPlanError(MeshOptimizationPlan plan, out string reason)
        {
            if (plan == null)
            {
                reason = "Optimization plan is null.";
                return true;
            }

            if (!plan.IsValid(out reason)) return true;
            reason = string.Empty;
            return false;
        }

        private static AssetUtilityWarningRecord Make(AssetInfo asset, string code, string message)
        {
            return new AssetUtilityWarningRecord
            {
                objectPath = asset.objectPath,
                code = code,
                message = message
            };
        }
    }
}
#endif
