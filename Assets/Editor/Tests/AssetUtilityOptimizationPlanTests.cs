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
}
