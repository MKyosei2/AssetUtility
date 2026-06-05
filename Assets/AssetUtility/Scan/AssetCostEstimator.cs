#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EasyTool
{
    public static class AssetCostEstimator
    {
        public static AssetUtilityCostSnapshot BuildSnapshot(string label, IReadOnlyList<AssetInfo> assets)
        {
            var snapshot = AssetUtilityCostSnapshot.Empty(label);
            if (assets == null) return snapshot;

            snapshot.objectsScanned = assets.Count;

            var uniqueMeshes = new HashSet<string>();
            var uniqueTextures = new HashSet<string>();
            var uniqueMaterials = new HashSet<string>();

            for (int i = 0; i < assets.Count; i++)
            {
                AssetInfo a = assets[i];
                if (a == null) continue;

                if (string.IsNullOrEmpty(snapshot.sceneName) && !string.IsNullOrEmpty(a.scene)) snapshot.sceneName = a.scene;
                if (string.IsNullOrEmpty(snapshot.scenePath) && !string.IsNullOrEmpty(a.scenePath)) snapshot.scenePath = a.scenePath;

                snapshot.totalTriangles += Mathf.Max(0, a.polygonCount);
                snapshot.textureVramMB += Mathf.Max(0f, a.vramMB);
                snapshot.estimatedMemoryMB += Mathf.Max(0, a.memoryMB);

                if (!string.IsNullOrEmpty(a.meshPath)) uniqueMeshes.Add(a.meshPath);
                if (!string.IsNullOrEmpty(a.texturePath)) uniqueTextures.Add(a.texturePath);
                if (!string.IsNullOrEmpty(a.materialName)) uniqueMaterials.Add(a.materialName);
            }

            snapshot.meshObjects = uniqueMeshes.Count;
            snapshot.textureObjects = uniqueTextures.Count;
            snapshot.materialObjects = uniqueMaterials.Count;
            return snapshot;
        }

        public static float EstimateTextureVramMB(Vector2Int resolution, int bytesPerPixel = 4)
        {
            if (resolution.x <= 0 || resolution.y <= 0) return 0f;
            return resolution.x * resolution.y * bytesPerPixel / (1024f * 1024f);
        }

        public static int EstimateMeshMemoryMB(Mesh mesh)
        {
            if (mesh == null) return 0;
            long bytes = 0;
            bytes += (long)mesh.vertexCount * sizeof(float) * 3; // positions
            bytes += (long)mesh.vertexCount * sizeof(float) * 3; // normals
            bytes += (long)mesh.vertexCount * sizeof(float) * 4; // tangents
            bytes += (long)mesh.vertexCount * sizeof(float) * 2; // uv0
            bytes += (long)(mesh.triangles != null ? mesh.triangles.Length : 0) * sizeof(int);
            return Mathf.CeilToInt(bytes / (1024f * 1024f));
        }
    }
}
#endif
