#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyTool
{
    /// <summary>
    /// Shared paths used by the production-oriented AssetUtility modules.
    /// Keeping paths in one place avoids scattering generated-output locations across UI code.
    /// </summary>
    public static class AssetUtilityProductionPaths
    {
        public const string GeneratedRoot = "Assets/AssetUtilityGenerated";
        public const string ReportRoot = GeneratedRoot + "/Reports";
        public const string OptimizedMeshRoot = "Assets/OptimizedMeshes";
    }

    [Serializable]
    public class AssetUtilityCostSnapshot
    {
        public string label;
        public string sceneName;
        public string scenePath;
        public int objectsScanned;
        public int meshObjects;
        public int textureObjects;
        public int materialObjects;
        public long totalTriangles;
        public float textureVramMB;
        public float estimatedMemoryMB;

        public static AssetUtilityCostSnapshot Empty(string label)
        {
            return new AssetUtilityCostSnapshot { label = label };
        }
    }

    [Serializable]
    public class AssetUtilityChangeRecord
    {
        public string objectPath;
        public string scenePath;
        public string componentType;
        public string sourceAssetPath;
        public string generatedAssetPath;
        public string note;
    }

    [Serializable]
    public class AssetUtilityWarningRecord
    {
        public string objectPath;
        public string code;
        public string message;
    }

    [Serializable]
    public class AssetUtilityOptimizationReport
    {
        public string tool = "AssetUtility";
        public string schemaVersion = "1.0";
        public string generatedAtLocal;
        public string operation;
        public AssetUtilityCostSnapshot before = AssetUtilityCostSnapshot.Empty("before");
        public AssetUtilityCostSnapshot after = AssetUtilityCostSnapshot.Empty("after");
        public List<AssetUtilityChangeRecord> changes = new List<AssetUtilityChangeRecord>();
        public List<AssetUtilityWarningRecord> warnings = new List<AssetUtilityWarningRecord>();
        public List<string> errors = new List<string>();
        public long durationMs;

        public void StampNow(string operationName)
        {
            operation = operationName;
            generatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    [Serializable]
    public class MeshOptimizationPlan
    {
        public string sourceMeshPath;
        public string targetObjectPath;
        public string targetScenePath;
        public int originalTriangleCount;
        public int targetTriangleCount;
        public bool dryRun = true;
        public bool updateMeshFilter = true;
        public bool updateSkinnedMeshRenderer = true;
        public bool updateMeshCollider = true;

        public bool IsValid(out string reason)
        {
            if (string.IsNullOrEmpty(sourceMeshPath))
            {
                reason = "sourceMeshPath is empty.";
                return false;
            }

            if (originalTriangleCount <= 0)
            {
                reason = "originalTriangleCount must be greater than zero.";
                return false;
            }

            if (targetTriangleCount <= 0 || targetTriangleCount >= originalTriangleCount)
            {
                reason = "targetTriangleCount must be greater than zero and smaller than originalTriangleCount.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
#endif
