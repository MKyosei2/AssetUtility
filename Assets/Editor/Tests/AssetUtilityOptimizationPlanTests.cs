using NUnit.Framework;
using UnityEngine;

public sealed class AssetUtilityOptimizationPlanTests
{
    [Test]
    public void DryRunRejectsInvalidMeshTarget()
    {
        var plan = new AssetOptimizationPlan();
        plan.MeshCommands.Add(new MeshOptimizationCommand
        {
            ObjectPath = "Root/BadMesh",
            SourceMeshPath = "Assets/BadMesh.asset",
            OriginalTriangles = 100,
            TargetTriangles = 0
        });

        AssetOptimizationResult result = AssetUtilityDryRunPlanner.ValidateDryRun(plan);
        Assert.IsFalse(result.Success);
        Assert.Greater(result.Errors.Count, 0);
        Assert.Greater(result.RollbackCommands.Count, 0);
    }

    [Test]
    public void MeshQualityGatePassesSimpleTriangle()
    {
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateNormals();

        MeshQualityReport report = AssetUtilityMeshQualityGate.Analyze(mesh, 1);
        Assert.IsTrue(report.Passed);
        Assert.AreEqual(1, report.FinalTriangles);
        Assert.AreEqual(0, report.DegenerateTriangles);
        Assert.AreEqual(0, report.InvalidIndices);
    }

    [Test]
    public void PriorityQemReducesGridMeshWithoutInvalidIndices()
    {
        Mesh grid = BuildGridMesh(5, 5);
        var options = new AssetUtilityPriorityQem.Options
        {
            TargetTriangles = 8,
            MaxIterations = 1000,
            PreserveBorders = false
        };

        MeshQualityReport report;
        Mesh simplified = AssetUtilityPriorityQem.Simplify(grid, options, out report);

        Assert.IsNotNull(simplified);
        Assert.LessOrEqual(simplified.triangles.Length / 3, grid.triangles.Length / 3);
        Assert.AreEqual(0, report.InvalidIndices);
    }

    private static Mesh BuildGridMesh(int width, int height)
    {
        var mesh = new Mesh();
        Vector3[] vertices = new Vector3[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                vertices[y * width + x] = new Vector3(x, y, 0f);

        int quadCount = (width - 1) * (height - 1);
        int[] triangles = new int[quadCount * 6];
        int p = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int a = y * width + x;
                int b = a + 1;
                int c = a + width;
                int d = c + 1;
                triangles[p++] = a; triangles[p++] = c; triangles[p++] = b;
                triangles[p++] = b; triangles[p++] = c; triangles[p++] = d;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }
}
