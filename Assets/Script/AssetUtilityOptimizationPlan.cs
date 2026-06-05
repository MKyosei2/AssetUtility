#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Portfolio-quality dry-run/apply/rollback model for AssetUtility.
/// This file is intentionally independent from the main EditorWindow so validation and tests can run
/// without mutating scene assets. The main window can call these types before destructive changes.
/// </summary>
[Serializable]
public sealed class AssetOptimizationPlan
{
    public readonly List<MeshOptimizationCommand> MeshCommands = new List<MeshOptimizationCommand>();
    public readonly List<TextureOptimizationCommand> TextureCommands = new List<TextureOptimizationCommand>();

    public bool IsEmpty => MeshCommands.Count == 0 && TextureCommands.Count == 0;
}

[Serializable]
public sealed class MeshOptimizationCommand
{
    public string ObjectPath;
    public string SourceMeshPath;
    public int OriginalTriangles;
    public int TargetTriangles;
    public string Method = "Shape-Preserving QEM";
}

[Serializable]
public sealed class TextureOptimizationCommand
{
    public string TexturePath;
    public int OriginalMaxSize;
    public int TargetMaxSize;
    public string Platform = "Default";
}

[Serializable]
public sealed class ReferenceReplacementRecord
{
    public string ObjectPath;
    public string ComponentType;
    public string BeforeAssetPath;
    public string AfterAssetPath;
}

[Serializable]
public sealed class GeneratedAssetRecord
{
    public string Path;
    public string Kind;
    public bool Created;
}

[Serializable]
public sealed class AssetOptimizationResult
{
    public bool Success;
    public readonly List<string> Warnings = new List<string>();
    public readonly List<string> Errors = new List<string>();
    public readonly List<GeneratedAssetRecord> GeneratedAssets = new List<GeneratedAssetRecord>();
    public readonly List<ReferenceReplacementRecord> Replacements = new List<ReferenceReplacementRecord>();
    public readonly List<string> RollbackCommands = new List<string>();
}

[Serializable]
public sealed class MeshQualityReport
{
    public int OriginalTriangles;
    public int FinalTriangles;
    public int DegenerateTriangles;
    public int InvalidIndices;
    public int BoundaryEdges;
    public float MinTriangleArea;
    public float MaxTriangleArea;
    public bool NormalsValid;
    public bool Passed;

    public string ToMarkdown()
    {
        return "| Metric | Value |\n" +
               "|---|---:|\n" +
               "| Original triangles | " + OriginalTriangles + " |\n" +
               "| Final triangles | " + FinalTriangles + " |\n" +
               "| Degenerate triangles | " + DegenerateTriangles + " |\n" +
               "| Invalid indices | " + InvalidIndices + " |\n" +
               "| Boundary edges | " + BoundaryEdges + " |\n" +
               "| Min triangle area | " + MinTriangleArea + " |\n" +
               "| Max triangle area | " + MaxTriangleArea + " |\n" +
               "| Normals valid | " + NormalsValid + " |\n" +
               "| Passed | " + Passed + " |\n";
    }
}

public static class AssetUtilityMeshQualityGate
{
    public static MeshQualityReport Analyze(Mesh mesh, int originalTriangles = 0)
    {
        var report = new MeshQualityReport { OriginalTriangles = originalTriangles };
        if (mesh == null)
        {
            report.InvalidIndices = 1;
            report.Passed = false;
            return report;
        }

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        report.FinalTriangles = triangles != null ? triangles.Length / 3 : 0;
        report.NormalsValid = mesh.normals != null && mesh.normals.Length == vertices.Length;
        report.MinTriangleArea = float.MaxValue;
        report.MaxTriangleArea = 0f;

        var edgeUse = new Dictionary<EdgeKey, int>();
        for (int i = 0; triangles != null && i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
            {
                report.InvalidIndices++;
                continue;
            }
            if (a == b || b == c || c == a)
            {
                report.DegenerateTriangles++;
                continue;
            }

            float area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
            report.MinTriangleArea = Mathf.Min(report.MinTriangleArea, area);
            report.MaxTriangleArea = Mathf.Max(report.MaxTriangleArea, area);
            if (area <= 1.0e-10f) report.DegenerateTriangles++;

            AddEdge(edgeUse, a, b);
            AddEdge(edgeUse, b, c);
            AddEdge(edgeUse, c, a);
        }

        if (report.MinTriangleArea == float.MaxValue) report.MinTriangleArea = 0f;
        foreach (var pair in edgeUse)
            if (pair.Value == 1) report.BoundaryEdges++;

        report.Passed = report.InvalidIndices == 0 && report.DegenerateTriangles == 0 && report.FinalTriangles > 0;
        return report;
    }

    private static void AddEdge(Dictionary<EdgeKey, int> edgeUse, int a, int b)
    {
        var key = new EdgeKey(a, b);
        int count;
        edgeUse.TryGetValue(key, out count);
        edgeUse[key] = count + 1;
    }

    private struct EdgeKey : IEquatable<EdgeKey>
    {
        private readonly int a;
        private readonly int b;

        public EdgeKey(int x, int y)
        {
            if (x < y) { a = x; b = y; }
            else { a = y; b = x; }
        }

        public bool Equals(EdgeKey other) => a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => (a * 397) ^ b;
    }
}

public static class AssetUtilityDryRunPlanner
{
    public static AssetOptimizationResult ValidateDryRun(AssetOptimizationPlan plan)
    {
        var result = new AssetOptimizationResult { Success = true };
        if (plan == null || plan.IsEmpty)
        {
            result.Warnings.Add("Optimization plan is empty. Nothing will be changed.");
            result.RollbackCommands.Add("No rollback is required for an empty dry-run plan.");
            return result;
        }

        foreach (MeshOptimizationCommand cmd in plan.MeshCommands)
        {
            if (cmd.TargetTriangles <= 0)
            {
                result.Success = false;
                result.Errors.Add("Mesh target triangle count must be positive: " + cmd.ObjectPath);
            }
            if (cmd.OriginalTriangles > 0 && cmd.TargetTriangles >= cmd.OriginalTriangles)
                result.Warnings.Add("Mesh target does not reduce triangle count: " + cmd.ObjectPath);

            result.RollbackCommands.Add("Restore mesh reference for " + cmd.ObjectPath + " to " + cmd.SourceMeshPath);
        }

        foreach (TextureOptimizationCommand cmd in plan.TextureCommands)
        {
            if (cmd.TargetMaxSize <= 0)
            {
                result.Success = false;
                result.Errors.Add("Texture target max size must be positive: " + cmd.TexturePath);
            }
            result.RollbackCommands.Add("Restore TextureImporter max size for " + cmd.TexturePath + " to " + cmd.OriginalMaxSize);
        }

        return result;
    }

    public static bool TryApplyTextureImporterSetting(TextureOptimizationCommand command, AssetOptimizationResult result)
    {
        if (result == null) result = new AssetOptimizationResult();
        if (command == null || string.IsNullOrEmpty(command.TexturePath))
        {
            result.Errors.Add("Texture command is missing a path.");
            result.Success = false;
            return false;
        }

        TextureImporter importer = AssetImporter.GetAtPath(command.TexturePath) as TextureImporter;
        if (importer == null)
        {
            result.Errors.Add("TextureImporter not found: " + command.TexturePath);
            result.Success = false;
            return false;
        }

        command.OriginalMaxSize = importer.maxTextureSize;
        importer.maxTextureSize = Mathf.Max(32, command.TargetMaxSize);
        importer.SaveAndReimport();
        result.Success = true;
        result.Replacements.Add(new ReferenceReplacementRecord
        {
            ObjectPath = command.TexturePath,
            ComponentType = "TextureImporter",
            BeforeAssetPath = command.OriginalMaxSize.ToString(),
            AfterAssetPath = importer.maxTextureSize.ToString()
        });
        result.RollbackCommands.Add("Set TextureImporter.maxTextureSize for " + command.TexturePath + " back to " + command.OriginalMaxSize);
        return true;
    }
}
#endif
