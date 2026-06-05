#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyTool
{
    /// <summary>
    /// Production-oriented scan service that is independent from the EditorWindow.
    /// It intentionally performs a conservative active-scene scan first, so tests and reports can call it without UI state.
    /// </summary>
    public static class SceneScanService
    {
        public static List<AssetInfo> ScanActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            var results = new List<AssetInfo>();
            if (!scene.IsValid()) return results;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                ObjectAssetCollector.CollectRecursive(roots[i], scene, results);
            }

            return results;
        }

        public static List<AssetInfo> ScanSingleObject(GameObject root)
        {
            var results = new List<AssetInfo>();
            if (root == null) return results;
            ObjectAssetCollector.CollectRecursive(root, root.scene, results);
            return results;
        }
    }

    public static class ObjectAssetCollector
    {
        public static void CollectRecursive(GameObject go, Scene scene, List<AssetInfo> output)
        {
            if (go == null || output == null) return;

            CollectMeshFilter(go, scene, output);
            CollectSkinnedMeshRenderer(go, scene, output);

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                CollectRecursive(t.GetChild(i).gameObject, scene, output);
            }
        }

        private static void CollectMeshFilter(GameObject go, Scene scene, List<AssetInfo> output)
        {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null) return;
            output.Add(BuildMeshAssetInfo(go, scene, mf.sharedMesh, renderer, "MeshFilter"));
        }

        private static void CollectSkinnedMeshRenderer(GameObject go, Scene scene, List<AssetInfo> output)
        {
            SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return;
            output.Add(BuildMeshAssetInfo(go, scene, smr.sharedMesh, smr, "SkinnedMeshRenderer"));
        }

        private static AssetInfo BuildMeshAssetInfo(GameObject go, Scene scene, Mesh mesh, Renderer renderer, string objectType)
        {
            Material material = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial : null;
            Texture mainTexture = material != null ? material.mainTexture : null;
            Texture2D texture2D = mainTexture as Texture2D;
            string texturePath = mainTexture != null ? AssetDatabase.GetAssetPath(mainTexture) : string.Empty;
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            Vector2Int resolution = texture2D != null ? new Vector2Int(texture2D.width, texture2D.height) : Vector2Int.zero;

            var info = new AssetInfo
            {
                instanceID = go.GetInstanceID(),
                name = go.name,
                scene = scene.IsValid() ? scene.name : string.Empty,
                scenePath = scene.IsValid() ? scene.path : string.Empty,
                objectPath = BuildHierarchyPath(go.transform),
                objectType = objectType,
                materialType = material != null && material.shader != null ? material.shader.name : string.Empty,
                materialName = material != null ? material.name : string.Empty,
                polygonCount = mesh.triangles != null ? mesh.triangles.Length / 3 : 0,
                targetPolygonCount = mesh.triangles != null ? mesh.triangles.Length / 3 : 0,
                meshPath = meshPath,
                canEditMesh = !string.IsNullOrEmpty(meshPath),
                textureName = mainTexture != null ? mainTexture.name : string.Empty,
                texturePath = texturePath,
                textureType = texture2D != null ? "Texture2D" : string.Empty,
                resolution = resolution,
                targetResolution = resolution,
                vramMB = AssetCostEstimator.EstimateTextureVramMB(resolution),
                memoryMB = AssetCostEstimator.EstimateMeshMemoryMB(mesh)
            };

            return info;
        }

        public static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            Transform p = transform.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }
    }
}
#endif
