#if UNITY_EDITOR
// ============================================================================
// AssetUtilityDemoSceneCreator.cs
// 配置場所: Assets/Script/AssetUtilityDemoSceneCreator.cs
//
// 目的:
// - Asset Utility本体とは別に、確認用Demo Sceneだけを作成します。
// - メインメニューを汚さないよう、Tools > Asset Utility Demo に分けています。
// ============================================================================

using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Asset Utility確認用のDemo Sceneを作成します。
/// </summary>
public static class AssetUtilityDemoSceneCreator
{
    private const string DemoRoot = "Assets/Samples/AssetUtilityDemo";
    private const string SceneFolder = DemoRoot + "/Scenes";
    private const string MeshFolder = DemoRoot + "/Meshes";
    private const string MaterialFolder = DemoRoot + "/Materials";
    private const string TextureFolder = DemoRoot + "/Textures";

    [MenuItem("Tools/Asset Utility Demo/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        Directory.CreateDirectory(SceneFolder);
        Directory.CreateDirectory(MeshFolder);
        Directory.CreateDirectory(MaterialFolder);
        Directory.CreateDirectory(TextureFolder);

        Texture2D checkerTexture = CreateCheckerTexture(512, 512);
        string texturePath = TextureFolder + "/AU_CheckerTexture.png";
        File.WriteAllBytes(texturePath, checkerTexture.EncodeToPNG());
        AssetDatabase.ImportAsset(texturePath);

        Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        Material material = new Material(Shader.Find("Standard"));
        material.name = "AU_DemoMaterial";
        material.mainTexture = importedTexture;

        string materialPath = MaterialFolder + "/AU_DemoMaterial.mat";
        AssetDatabase.CreateAsset(material, materialPath);

        Mesh denseGrid = CreateDenseGridMesh(80, 80, 4.0f);
        string denseGridPath = MeshFolder + "/AU_DenseGridMesh.asset";
        AssetDatabase.CreateAsset(denseGrid, denseGridPath);

        Mesh skinnedMesh = CreateSimpleQuadMesh("AU_SkinnedWarningMesh");
        string skinnedPath = MeshFolder + "/AU_SkinnedWarningMesh.asset";
        AssetDatabase.CreateAsset(skinnedMesh, skinnedPath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject camera = new GameObject("Main Camera");
        Camera cam = camera.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 4f, -8f);
        camera.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;

        GameObject light = new GameObject("Directional Light");
        Light directional = light.AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.intensity = 1.0f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject highPoly = new GameObject("AU_HighPoly_StaticMesh");
        highPoly.transform.position = new Vector3(-2.2f, 0f, 0f);
        MeshFilter highPolyFilter = highPoly.AddComponent<MeshFilter>();
        MeshRenderer highPolyRenderer = highPoly.AddComponent<MeshRenderer>();
        highPolyFilter.sharedMesh = denseGrid;
        highPolyRenderer.sharedMaterial = material;

        GameObject colliderObject = new GameObject("AU_MeshCollider_SharedMesh");
        colliderObject.transform.position = new Vector3(2.2f, 0f, 0f);
        MeshFilter colliderFilter = colliderObject.AddComponent<MeshFilter>();
        MeshRenderer colliderRenderer = colliderObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = colliderObject.AddComponent<MeshCollider>();
        colliderFilter.sharedMesh = denseGrid;
        colliderRenderer.sharedMaterial = material;
        meshCollider.sharedMesh = denseGrid;

        GameObject skinnedObject = new GameObject("AU_SkinnedMesh_WarningSample");
        skinnedObject.transform.position = new Vector3(0f, 0f, 2.5f);
        SkinnedMeshRenderer skinnedRenderer = skinnedObject.AddComponent<SkinnedMeshRenderer>();
        skinnedRenderer.sharedMesh = skinnedMesh;
        skinnedRenderer.sharedMaterial = material;

        string scenePath = SceneFolder + "/AssetUtilityDemoScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Demo Scene 作成完了",
            "AssetUtilityDemoScene を作成しました。\n\n" +
            "Tools > Asset Utility を開き、ActiveScene または AllScenes で Scan してください。",
            "OK");

        Debug.Log("[AssetUtilityDemoSceneCreator] Demo Scene を作成しました: " + scenePath);
    }

    private static Mesh CreateDenseGridMesh(int xSegments, int zSegments, float size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "AU_DenseGridMesh";

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int z = 0; z <= zSegments; z++)
        {
            for (int x = 0; x <= xSegments; x++)
            {
                float px = ((float)x / xSegments - 0.5f) * size;
                float pz = ((float)z / zSegments - 0.5f) * size;
                float py = Mathf.Sin(px * 3.0f) * Mathf.Cos(pz * 3.0f) * 0.18f;

                vertices.Add(new Vector3(px, py, pz));
                uvs.Add(new Vector2((float)x / xSegments, (float)z / zSegments));
            }
        }

        int row = xSegments + 1;
        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                int a = z * row + x;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;

                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);

                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Mesh CreateSimpleQuadMesh(string meshName)
    {
        Mesh mesh = new Mesh();
        mesh.name = meshName;

        mesh.vertices = new Vector3[]
        {
            new Vector3(-1f, 0f, -1f),
            new Vector3( 1f, 0f, -1f),
            new Vector3(-1f, 0f,  1f),
            new Vector3( 1f, 0f,  1f)
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Texture2D CreateCheckerTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "AU_CheckerTexture";

        int cell = 32;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool on = ((x / cell) + (y / cell)) % 2 == 0;
                Color color = on ? new Color(0.85f, 0.85f, 0.85f, 1f) : new Color(0.25f, 0.45f, 0.75f, 1f);
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        return texture;
    }
}
#endif
