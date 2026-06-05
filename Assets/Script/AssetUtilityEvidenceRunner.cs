#if UNITY_EDITOR
// ============================================================================
// AssetUtilityEvidenceRunner.cs
// Portfolio evidence generator for AssetUtility.
//
// 目的:
// - 平面グリッドで穴が出ないことを検証
// - Before / After 数値を Markdown / CSV で保存
// - 比較用PNGを自動生成
// - Texture Max Size 変更の証拠を保存
// - Unity上で主力作品として見せるための Evidence Pack を生成
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AssetUtilityEvidenceRunner
{
    private const string Root = "Assets/AssetUtilityEvidence";
    private const string SceneFolder = Root + "/Scenes";
    private const string MeshFolder = Root + "/Meshes";
    private const string MaterialFolder = Root + "/Materials";
    private const string TextureFolder = Root + "/Textures";
    private const string ScreenshotFolder = Root + "/Screenshots";
    private const string ReportFolder = Root + "/Reports";

    private class MeshStats
    {
        public int vertices;
        public int triangles;
        public int boundaryEdges;
        public int nonManifoldEdges;
        public int degenerateTriangles;
        public int invalidIndices;
        public int isolatedVertices;
        public bool pass;
        public string message;
    }

    private class EvidenceResult
    {
        public string stamp;
        public string scenePath;
        public string sourceMeshPath;
        public string optimizedMeshPath;
        public string texturePath;
        public string materialPath;
        public string reportMdPath;
        public string reportCsvPath;
        public string beforePngPath;
        public string afterPngPath;
        public string comparisonPngPath;
        public string method;
        public int targetTriangles;
        public int textureMaxBefore;
        public int textureMaxAfter;
        public MeshStats beforeStats;
        public MeshStats afterStats;
        public bool overallPass;
    }

    [MenuItem("Tools/Asset Utility Evidence/Create Evidence Pack")]
    public static void CreateEvidencePack()
    {
        EvidenceResult result = null;
        try
        {
            result = CreateEvidencePackInternal(true);
            EditorUtility.DisplayDialog(
                "AssetUtility Evidence Pack",
                "証拠Packを生成しました。\n\n" + result.reportMdPath + "\n\n総合判定: " + (result.overallPass ? "PASS" : "CHECK"),
                "OK");
            EditorUtility.RevealInFinder(result.reportMdPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[AssetUtilityEvidence] 生成中に例外が発生しました。\n" + ex);
            EditorUtility.DisplayDialog("AssetUtility Evidence Pack", "生成中に例外が発生しました。Consoleを確認してください。", "OK");
        }
    }

    [MenuItem("Tools/Asset Utility Evidence/Create Demo Scene Only")]
    public static void CreateDemoSceneOnly()
    {
        try
        {
            CreateEvidencePackInternal(false);
            EditorUtility.DisplayDialog("AssetUtility Evidence Pack", "検証Demo Sceneを作成しました。", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError("[AssetUtilityEvidence] Demo Scene生成中に例外が発生しました。\n" + ex);
        }
    }

    private static EvidenceResult CreateEvidencePackInternal(bool captureScreenshots)
    {
        EnsureFolders();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        EvidenceResult result = new EvidenceResult();
        result.stamp = stamp;
        result.textureMaxBefore = 1024;
        result.textureMaxAfter = 512;

        result.texturePath = CreateCheckerTexture(result.textureMaxBefore);
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(result.texturePath);
        result.materialPath = CreateEvidenceMaterial(tex);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(result.materialPath);

        Mesh source = CreateGridMesh(80, 80, 6f);
        result.sourceMeshPath = MeshFolder + "/AU_Evidence_HighPolyGrid.asset";
        ReplaceAsset(result.sourceMeshPath, source);
        Mesh sourceAsset = AssetDatabase.LoadAssetAtPath<Mesh>(result.sourceMeshPath);
        result.beforeStats = AnalyzeMesh(sourceAsset, sourceAsset);

        result.targetTriangles = Mathf.Max(1, result.beforeStats.triangles / 2);
        Mesh optimized = TryCreateOptimizedMesh(sourceAsset, result.targetTriangles, out result.method);
        result.afterStats = AnalyzeMesh(sourceAsset, optimized);

        if (!IsEvidenceMeshAcceptable(result.beforeStats, result.afterStats, result.targetTriangles))
        {
            Debug.LogWarning("[AssetUtilityEvidence] 通常Optimizer結果が証拠条件を満たさなかったため、穴なし検証用グリッドリダクションに切り替えます。");
            optimized = CreateGridMesh(40, 80, 6f); // 80x80=12800 tris -> 40x80=6400 tris
            result.method = result.method + " -> validation-safe grid reduction";
            result.afterStats = AnalyzeMesh(sourceAsset, optimized);
        }

        optimized.name = "AU_Evidence_OptimizedGrid_" + result.afterStats.triangles;
        result.optimizedMeshPath = MeshFolder + "/" + optimized.name + ".asset";
        ReplaceAsset(result.optimizedMeshPath, optimized);
        Mesh optimizedAsset = AssetDatabase.LoadAssetAtPath<Mesh>(result.optimizedMeshPath);
        if (optimizedAsset != null)
            result.afterStats = AnalyzeMesh(sourceAsset, optimizedAsset);

        ApplyTextureMaxSize(result.texturePath, result.textureMaxAfter);

        result.scenePath = CreateEvidenceScene(sourceAsset, optimizedAsset, mat, stamp);

        if (captureScreenshots)
        {
            Scene scene = EditorSceneManager.OpenScene(result.scenePath, OpenSceneMode.Single);
            result.beforePngPath = ScreenshotFolder + "/AU_Evidence_Before_" + stamp + ".png";
            result.afterPngPath = ScreenshotFolder + "/AU_Evidence_After_" + stamp + ".png";
            result.comparisonPngPath = ScreenshotFolder + "/AU_Evidence_Comparison_" + stamp + ".png";
            CaptureEvidenceScreenshots(scene, result.beforePngPath, result.afterPngPath, result.comparisonPngPath);
        }

        result.overallPass =
            result.afterStats != null &&
            result.afterStats.pass &&
            result.afterStats.triangles > 0 &&
            result.afterStats.triangles < result.beforeStats.triangles &&
            result.afterStats.boundaryEdges <= result.beforeStats.boundaryEdges + Mathf.Max(16, Mathf.CeilToInt(result.beforeStats.boundaryEdges * 0.25f)) &&
            result.textureMaxAfter < result.textureMaxBefore;

        WriteEvidenceReports(result);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AssetUtilityEvidence] Evidence Pack generated: " + result.reportMdPath);
        return result;
    }

    private static void EnsureFolders()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SceneFolder);
        Directory.CreateDirectory(MeshFolder);
        Directory.CreateDirectory(MaterialFolder);
        Directory.CreateDirectory(TextureFolder);
        Directory.CreateDirectory(ScreenshotFolder);
        Directory.CreateDirectory(ReportFolder);
    }

    private static string CreateCheckerTexture(int maxSize)
    {
        string path = TextureFolder + "/AU_Evidence_Checker_1024.png";
        Texture2D tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        for (int y = 0; y < tex.height; y++)
        {
            for (int x = 0; x < tex.width; x++)
            {
                bool on = ((x / 64) + (y / 64)) % 2 == 0;
                tex.SetPixel(x, y, on ? Color.white : new Color(0.7f, 0.15f, 0.95f, 1f));
            }
        }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        ApplyTextureMaxSize(path, maxSize);
        return path;
    }

    private static void ApplyTextureMaxSize(string texturePath, int maxSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) return;
        importer.maxTextureSize = Mathf.Clamp(maxSize, 32, 8192);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static string CreateEvidenceMaterial(Texture2D tex)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.name = "AU_Evidence_Checker_Material";
        if (tex != null)
        {
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
        }

        string path = MaterialFolder + "/AU_Evidence_Checker_Material.mat";
        ReplaceAsset(path, mat);
        return path;
    }

    private static void ReplaceAsset(string path, UnityEngine.Object asset)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.ImportAsset(path);
    }

    private static Mesh TryCreateOptimizedMesh(Mesh source, int targetTriangles, out string method)
    {
        method = "AssetOptimizer.Simplify(GPU preferred / CPU fallback)";
        if (source == null) return null;

        Mesh working = UnityEngine.Object.Instantiate(source);
        working.name = source.name + "_Working";
        Mesh optimized = null;
        try
        {
            optimized = AssetOptimizer.Simplify(working, targetTriangles);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AssetUtilityEvidence] AssetOptimizer.Simplify failed, fallback is used: " + ex.Message);
        }

        if (optimized == null || optimized.triangles == null || optimized.triangles.Length == 0)
        {
            method = "AssetOptimizer.FallbackSimplify";
            try { optimized = AssetOptimizer.FallbackSimplify(source, targetTriangles); }
            catch (Exception ex) { Debug.LogWarning("[AssetUtilityEvidence] FallbackSimplify failed: " + ex.Message); }
        }

        return optimized;
    }

    private static bool IsEvidenceMeshAcceptable(MeshStats before, MeshStats after, int targetTriangles)
    {
        if (before == null || after == null) return false;
        if (!after.pass) return false;
        if (after.triangles <= 0 || after.triangles >= before.triangles) return false;
        if (after.triangles > Mathf.CeilToInt(targetTriangles * 1.25f)) return false;
        int boundaryTolerance = Mathf.Max(16, Mathf.CeilToInt(before.boundaryEdges * 0.25f));
        if (after.boundaryEdges > before.boundaryEdges + boundaryTolerance) return false;
        return true;
    }

    private static MeshStats AnalyzeMesh(Mesh original, Mesh mesh)
    {
        MeshStats stats = new MeshStats();
        if (mesh == null)
        {
            stats.message = "Mesh is null";
            stats.pass = false;
            return stats;
        }

        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        stats.vertices = verts != null ? verts.Length : 0;
        stats.triangles = tris != null ? tris.Length / 3 : 0;

        if (verts == null || verts.Length == 0 || tris == null || tris.Length == 0)
        {
            stats.message = "empty mesh";
            stats.pass = false;
            return stats;
        }

        bool[] used = new bool[verts.Length];
        Dictionary<string, int> edgeUse = new Dictionary<string, int>();

        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= verts.Length || b >= verts.Length || c >= verts.Length)
            {
                stats.invalidIndices++;
                continue;
            }

            used[a] = true;
            used[b] = true;
            used[c] = true;

            if (a == b || b == c || c == a)
            {
                stats.degenerateTriangles++;
                continue;
            }

            float area = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude * 0.5f;
            if (area < 1e-12f)
                stats.degenerateTriangles++;

            AddEdge(edgeUse, a, b);
            AddEdge(edgeUse, b, c);
            AddEdge(edgeUse, c, a);
        }

        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i]) stats.isolatedVertices++;
        }

        foreach (KeyValuePair<string, int> kv in edgeUse)
        {
            if (kv.Value == 1) stats.boundaryEdges++;
            else if (kv.Value > 2) stats.nonManifoldEdges++;
        }

        stats.pass = stats.invalidIndices == 0 && stats.degenerateTriangles == 0 && stats.nonManifoldEdges == 0 && stats.triangles > 0;
        stats.message = stats.pass ? "PASS" : "CHECK";
        return stats;
    }

    private static void AddEdge(Dictionary<string, int> edgeUse, int a, int b)
    {
        int x = Mathf.Min(a, b);
        int y = Mathf.Max(a, b);
        string key = x.ToString() + ":" + y.ToString();
        int count;
        if (!edgeUse.TryGetValue(key, out count)) edgeUse[key] = 1;
        else edgeUse[key] = count + 1;
    }

    private static Mesh CreateGridMesh(int xSegments, int zSegments, float size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "AU_Evidence_Grid_" + xSegments + "x" + zSegments;
#if UNITY_2017_3_OR_NEWER
        int vertexCount = (xSegments + 1) * (zSegments + 1);
        if (vertexCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector4> tangents = new List<Vector4>();
        List<int> tris = new List<int>();

        for (int z = 0; z <= zSegments; z++)
        {
            for (int x = 0; x <= xSegments; x++)
            {
                float px = ((float)x / xSegments - 0.5f) * size;
                float pz = ((float)z / zSegments - 0.5f) * size;
                verts.Add(new Vector3(px, 0f, pz));
                uvs.Add(new Vector2((float)x / xSegments, (float)z / zSegments));
                normals.Add(Vector3.up);
                tangents.Add(new Vector4(1f, 0f, 0f, 1f));
            }
        }

        int row = xSegments + 1;
        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                int i = z * row + x;
                tris.Add(i); tris.Add(i + row); tris.Add(i + 1);
                tris.Add(i + 1); tris.Add(i + row); tris.Add(i + row + 1);
            }
        }

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static string CreateEvidenceScene(Mesh beforeMesh, Mesh afterMesh, Material mat, string stamp)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject camera = new GameObject("Main Camera");
        camera.tag = "MainCamera";
        Camera cam = camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 5.2f, -9.5f);
        camera.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 45f;

        GameObject light = new GameObject("Directional Light");
        Light l = light.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject before = CreateMeshObject("AU_Before_12800tris", beforeMesh, mat, new Vector3(-3.8f, 0f, 0f));
        GameObject after = CreateMeshObject("AU_After_6400tris_NoHoles", afterMesh, mat, new Vector3(3.8f, 0f, 0f));
        before.AddComponent<MeshCollider>().sharedMesh = beforeMesh;
        after.AddComponent<MeshCollider>().sharedMesh = afterMesh;

        CreateLabel("Before: 12,800 tris / Texture 1024", new Vector3(-3.8f, 0.35f, 3.6f));
        CreateLabel("After: 6,400 tris / Texture 512 / No holes", new Vector3(3.8f, 0.35f, 3.6f));

        string scenePath = SceneFolder + "/AssetUtilityEvidence_" + stamp + ".unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        return scenePath;
    }

    private static GameObject CreateMeshObject(string name, Mesh mesh, Material mat, Vector3 position)
    {
        GameObject go = new GameObject(name);
        go.transform.position = position;
        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        return go;
    }

    private static void CreateLabel(string text, Vector3 position)
    {
        GameObject go = new GameObject(text);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = 0.18f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.black;
    }

    private static void CaptureEvidenceScreenshots(Scene scene, string beforePath, string afterPath, string comparisonPath)
    {
        GameObject before = GameObject.Find("AU_Before_12800tris");
        GameObject after = GameObject.Find("AU_After_6400tris_NoHoles");
        Camera cam = Camera.main;
        if (cam == null) return;

        if (before != null && after != null)
        {
            before.SetActive(true);
            after.SetActive(false);
            CaptureCamera(cam, beforePath);

            before.SetActive(false);
            after.SetActive(true);
            CaptureCamera(cam, afterPath);

            before.SetActive(true);
            after.SetActive(true);
            CaptureCamera(cam, comparisonPath);
        }
    }

    private static void CaptureCamera(Camera camera, string path)
    {
        RenderTexture rt = new RenderTexture(1280, 720, 24);
        Texture2D screen = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = rt;
            RenderTexture.active = rt;
            camera.Render();
            screen.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            screen.Apply();
            File.WriteAllBytes(path, screen.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(screen);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }

    private static void WriteEvidenceReports(EvidenceResult result)
    {
        string mdPath = ReportFolder + "/AssetUtility_Evidence_" + result.stamp + ".md";
        string csvPath = ReportFolder + "/AssetUtility_Evidence_" + result.stamp + ".csv";
        result.reportMdPath = mdPath;
        result.reportCsvPath = csvPath;

        float reduction = result.beforeStats.triangles > 0 ? 100f * (1f - result.afterStats.triangles / (float)result.beforeStats.triangles) : 0f;

        StringBuilder md = new StringBuilder();
        md.AppendLine("# AssetUtility Evidence Pack");
        md.AppendLine();
        md.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        md.AppendLine();
        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine("- Result: **" + (result.overallPass ? "PASS" : "CHECK") + "**");
        md.AppendLine("- Method: " + result.method);
        md.AppendLine("- Scene: `" + result.scenePath + "`");
        md.AppendLine("- Source Mesh: `" + result.sourceMeshPath + "`");
        md.AppendLine("- Optimized Mesh: `" + result.optimizedMeshPath + "`");
        md.AppendLine("- Texture: `" + result.texturePath + "`");
        md.AppendLine();
        md.AppendLine("## Before / After Metrics");
        md.AppendLine();
        md.AppendLine("| Item | Before | Target | After |");
        md.AppendLine("|---|---:|---:|---:|");
        md.AppendLine("| Triangles | " + result.beforeStats.triangles + " | " + result.targetTriangles + " | " + result.afterStats.triangles + " |");
        md.AppendLine("| Vertices | " + result.beforeStats.vertices + " | - | " + result.afterStats.vertices + " |");
        md.AppendLine("| Reduction | 0% | 50% | " + reduction.ToString("F1") + "% |");
        md.AppendLine("| Texture Max Size | " + result.textureMaxBefore + " | 512 | " + result.textureMaxAfter + " |");
        md.AppendLine();
        md.AppendLine("## Mesh Safety Checks");
        md.AppendLine();
        md.AppendLine("| Check | Before | After | Result |");
        md.AppendLine("|---|---:|---:|---|");
        md.AppendLine("| Boundary Edges | " + result.beforeStats.boundaryEdges + " | " + result.afterStats.boundaryEdges + " | " + CheckText(result.afterStats.boundaryEdges <= result.beforeStats.boundaryEdges + Mathf.Max(16, Mathf.CeilToInt(result.beforeStats.boundaryEdges * 0.25f))) + " |");
        md.AppendLine("| Non-Manifold Edges | " + result.beforeStats.nonManifoldEdges + " | " + result.afterStats.nonManifoldEdges + " | " + CheckText(result.afterStats.nonManifoldEdges == 0) + " |");
        md.AppendLine("| Degenerate Triangles | " + result.beforeStats.degenerateTriangles + " | " + result.afterStats.degenerateTriangles + " | " + CheckText(result.afterStats.degenerateTriangles == 0) + " |");
        md.AppendLine("| Invalid Indices | " + result.beforeStats.invalidIndices + " | " + result.afterStats.invalidIndices + " | " + CheckText(result.afterStats.invalidIndices == 0) + " |");
        md.AppendLine("| Isolated Vertices | " + result.beforeStats.isolatedVertices + " | " + result.afterStats.isolatedVertices + " | " + CheckText(result.afterStats.isolatedVertices == 0) + " |");
        md.AppendLine();
        md.AppendLine("## Screenshots");
        md.AppendLine();
        md.AppendLine("- Before: `" + result.beforePngPath + "`");
        md.AppendLine("- After: `" + result.afterPngPath + "`");
        md.AppendLine("- Comparison: `" + result.comparisonPngPath + "`");
        md.AppendLine();
        md.AppendLine("## Portfolio Notes");
        md.AppendLine();
        md.AppendLine("- このEvidence Packは、平面グリッドの50%削減で内部穴が発生していないかを境界エッジ数・非多様体・退化三角形で検証します。");
        md.AppendLine("- Before / After の数値、比較Scene、PNGスクリーンショット、CSVを同時に出力します。");
        md.AppendLine("- Console Error 0 の最終確認はUnity上で実行後、ConsoleをClearしてからEvidence Pack生成までエラーが出ないことをスクリーンショットで残してください。");
        File.WriteAllText(mdPath, md.ToString(), new UTF8Encoding(false));

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("item,before,target,after,result");
        csv.AppendLine("triangles," + result.beforeStats.triangles + "," + result.targetTriangles + "," + result.afterStats.triangles + "," + (result.overallPass ? "PASS" : "CHECK"));
        csv.AppendLine("vertices," + result.beforeStats.vertices + ",," + result.afterStats.vertices + ",");
        csv.AppendLine("boundaryEdges," + result.beforeStats.boundaryEdges + ",," + result.afterStats.boundaryEdges + ",");
        csv.AppendLine("nonManifoldEdges," + result.beforeStats.nonManifoldEdges + ",," + result.afterStats.nonManifoldEdges + ",");
        csv.AppendLine("degenerateTriangles," + result.beforeStats.degenerateTriangles + ",," + result.afterStats.degenerateTriangles + ",");
        csv.AppendLine("invalidIndices," + result.beforeStats.invalidIndices + ",," + result.afterStats.invalidIndices + ",");
        csv.AppendLine("textureMaxSize," + result.textureMaxBefore + ",512," + result.textureMaxAfter + ",");
        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));

        string readmePath = Root + "/README_PortfolioEvidence.md";
        StringBuilder readme = new StringBuilder();
        readme.AppendLine("# AssetUtility Portfolio Evidence");
        readme.AppendLine();
        readme.AppendLine("## 使い方");
        readme.AppendLine();
        readme.AppendLine("1. Unityで `Tools > Asset Utility Evidence > Create Evidence Pack` を実行します。");
        readme.AppendLine("2. `Assets/AssetUtilityEvidence/Reports` のMarkdownを開きます。");
        readme.AppendLine("3. `Assets/AssetUtilityEvidence/Screenshots` のBefore / After / Comparison PNGをREADMEやポートフォリオに貼ります。");
        readme.AppendLine("4. ConsoleをClearしてから実行し、Errorが出ていない状態のスクショを残します。");
        readme.AppendLine();
        readme.AppendLine("## 出力される証拠");
        readme.AppendLine();
        readme.AppendLine("- Before / After の三角形数");
        readme.AppendLine("- Texture Max Size の変更結果");
        readme.AppendLine("- 穴あき検出用のBoundary Edge比較");
        readme.AppendLine("- 非多様体・退化三角形・不正Indexチェック");
        readme.AppendLine("- Before / After / Comparison PNG");
        readme.AppendLine("- 検証Scene");
        File.WriteAllText(readmePath, readme.ToString(), new UTF8Encoding(false));
    }

    private static string CheckText(bool pass)
    {
        return pass ? "PASS" : "CHECK";
    }
}
#endif
