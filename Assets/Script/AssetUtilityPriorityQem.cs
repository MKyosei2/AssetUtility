#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Standalone priority-queue QEM simplifier prototype for portfolio validation.
/// This implementation is intentionally separate from the legacy AssetUtility.cs monolith so it can be
/// tested and iterated without breaking the existing editor window. It demonstrates the intended
/// production direction: min-heap candidates, adjacency cache, triangle valid flags, and delayed compaction.
/// </summary>
public static class AssetUtilityPriorityQem
{
    public sealed class Options
    {
        public int TargetTriangles = 1000;
        public int MaxIterations = 100000;
        public bool PreserveBorders = true;
    }

    private struct Edge : IEquatable<Edge>
    {
        public readonly int A;
        public readonly int B;

        public Edge(int a, int b)
        {
            if (a < b) { A = a; B = b; }
            else { A = b; B = a; }
        }

        public bool Equals(Edge other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is Edge other && Equals(other);
        public override int GetHashCode() => (A * 397) ^ B;
    }

    private struct Candidate
    {
        public Edge Edge;
        public float Cost;
        public int VersionA;
        public int VersionB;
    }

    private sealed class MinHeap
    {
        private readonly List<Candidate> data = new List<Candidate>();
        public int Count => data.Count;

        public void Push(Candidate candidate)
        {
            data.Add(candidate);
            int i = data.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (data[p].Cost <= candidate.Cost) break;
                data[i] = data[p];
                i = p;
            }
            data[i] = candidate;
        }

        public Candidate Pop()
        {
            Candidate root = data[0];
            Candidate last = data[data.Count - 1];
            data.RemoveAt(data.Count - 1);
            if (data.Count == 0) return root;
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                if (l >= data.Count) break;
                int c = r < data.Count && data[r].Cost < data[l].Cost ? r : l;
                if (data[c].Cost >= last.Cost) break;
                data[i] = data[c];
                i = c;
            }
            data[i] = last;
            return root;
        }
    }

    private sealed class Adjacency
    {
        public readonly List<HashSet<int>> VertexTriangles;
        public readonly List<HashSet<int>> VertexNeighbors;
        public readonly bool[] TriangleValid;
        public readonly int[] VertexVersion;
        public int ActiveTriangles;

        public Adjacency(int vertexCount, int triangleCount)
        {
            VertexTriangles = new List<HashSet<int>>(vertexCount);
            VertexNeighbors = new List<HashSet<int>>(vertexCount);
            for (int i = 0; i < vertexCount; i++)
            {
                VertexTriangles.Add(new HashSet<int>());
                VertexNeighbors.Add(new HashSet<int>());
            }
            TriangleValid = new bool[triangleCount];
            VertexVersion = new int[vertexCount];
        }
    }

    public static Mesh Simplify(Mesh source, Options options, out MeshQualityReport qualityReport)
    {
        if (options == null) options = new Options();
        if (source == null)
        {
            qualityReport = new MeshQualityReport { Passed = false, InvalidIndices = 1 };
            return null;
        }

        var vertices = new List<Vector3>(source.vertices);
        var triangles = new List<int>(source.triangles);
        int originalTriangles = triangles.Count / 3;
        int targetTriangles = Mathf.Clamp(options.TargetTriangles, 1, originalTriangles);
        var adjacency = BuildAdjacency(vertices.Count, triangles);
        var heap = new MinHeap();
        var edges = CollectEdges(triangles, adjacency.TriangleValid);
        foreach (Edge edge in edges) Enqueue(edge, vertices, adjacency, heap);

        int safety = 0;
        while (adjacency.ActiveTriangles > targetTriangles && heap.Count > 0 && safety++ < options.MaxIterations)
        {
            Candidate candidate = heap.Pop();
            int a = candidate.Edge.A;
            int b = candidate.Edge.B;
            if (a < 0 || b < 0 || a >= vertices.Count || b >= vertices.Count) continue;
            if (candidate.VersionA != adjacency.VertexVersion[a] || candidate.VersionB != adjacency.VertexVersion[b]) continue;
            if (options.PreserveBorders && IsBorderEdge(candidate.Edge, triangles, adjacency)) continue;
            if (!CanCollapse(a, b, vertices, triangles, adjacency)) continue;

            Collapse(a, b, vertices, triangles, adjacency);
            foreach (int n in adjacency.VertexNeighbors[a])
                if (n != a) Enqueue(new Edge(a, n), vertices, adjacency, heap);
        }

        Mesh result = BuildCompactedMesh(source, vertices, triangles, adjacency.TriangleValid);
        result.RecalculateBounds();
        result.RecalculateNormals();
        qualityReport = AssetUtilityMeshQualityGate.Analyze(result, originalTriangles);
        return result;
    }

    private static Adjacency BuildAdjacency(int vertexCount, List<int> triangles)
    {
        int triCount = triangles.Count / 3;
        var adjacency = new Adjacency(vertexCount, triCount);
        for (int t = 0; t < triCount; t++)
        {
            int i = t * 3;
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
            adjacency.TriangleValid[t] = true;
            adjacency.ActiveTriangles++;
            adjacency.VertexTriangles[a].Add(t);
            adjacency.VertexTriangles[b].Add(t);
            adjacency.VertexTriangles[c].Add(t);
            Link(adjacency, a, b);
            Link(adjacency, b, c);
            Link(adjacency, c, a);
        }
        return adjacency;
    }

    private static void Link(Adjacency adjacency, int a, int b)
    {
        adjacency.VertexNeighbors[a].Add(b);
        adjacency.VertexNeighbors[b].Add(a);
    }

    private static HashSet<Edge> CollectEdges(List<int> triangles, bool[] valid)
    {
        var edges = new HashSet<Edge>();
        for (int t = 0; t < valid.Length; t++)
        {
            if (!valid[t]) continue;
            int i = t * 3;
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            edges.Add(new Edge(a, b));
            edges.Add(new Edge(b, c));
            edges.Add(new Edge(c, a));
        }
        return edges;
    }

    private static void Enqueue(Edge edge, List<Vector3> vertices, Adjacency adjacency, MinHeap heap)
    {
        if (edge.A < 0 || edge.B < 0 || edge.A >= vertices.Count || edge.B >= vertices.Count) return;
        heap.Push(new Candidate
        {
            Edge = edge,
            Cost = (vertices[edge.A] - vertices[edge.B]).sqrMagnitude,
            VersionA = adjacency.VertexVersion[edge.A],
            VersionB = adjacency.VertexVersion[edge.B]
        });
    }

    private static bool IsBorderEdge(Edge edge, List<int> triangles, Adjacency adjacency)
    {
        int count = 0;
        foreach (int t in adjacency.VertexTriangles[edge.A])
        {
            if (!adjacency.TriangleValid[t]) continue;
            int i = t * 3;
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (ContainsEdge(a, b, c, edge)) count++;
            if (count > 1) return false;
        }
        return count <= 1;
    }

    private static bool ContainsEdge(int a, int b, int c, Edge edge)
    {
        return new Edge(a, b).Equals(edge) || new Edge(b, c).Equals(edge) || new Edge(c, a).Equals(edge);
    }

    private static bool CanCollapse(int keep, int remove, List<Vector3> vertices, List<int> triangles, Adjacency adjacency)
    {
        Vector3 newPos = (vertices[keep] + vertices[remove]) * 0.5f;
        var affected = new HashSet<int>(adjacency.VertexTriangles[keep]);
        affected.UnionWith(adjacency.VertexTriangles[remove]);
        foreach (int t in affected)
        {
            if (!adjacency.TriangleValid[t]) continue;
            int i = t * 3;
            int a = triangles[i] == remove ? keep : triangles[i];
            int b = triangles[i + 1] == remove ? keep : triangles[i + 1];
            int c = triangles[i + 2] == remove ? keep : triangles[i + 2];
            if (a == b || b == c || c == a) continue;
            Vector3 va = a == keep ? newPos : vertices[a];
            Vector3 vb = b == keep ? newPos : vertices[b];
            Vector3 vc = c == keep ? newPos : vertices[c];
            float area = Vector3.Cross(vb - va, vc - va).sqrMagnitude;
            if (area <= 1.0e-16f) return false;
        }
        return true;
    }

    private static void Collapse(int keep, int remove, List<Vector3> vertices, List<int> triangles, Adjacency adjacency)
    {
        vertices[keep] = (vertices[keep] + vertices[remove]) * 0.5f;
        adjacency.VertexVersion[keep]++;
        adjacency.VertexVersion[remove]++;

        var affected = new HashSet<int>(adjacency.VertexTriangles[remove]);
        affected.UnionWith(adjacency.VertexTriangles[keep]);
        foreach (int t in affected)
        {
            if (!adjacency.TriangleValid[t]) continue;
            int i = t * 3;
            if (triangles[i] == remove) triangles[i] = keep;
            if (triangles[i + 1] == remove) triangles[i + 1] = keep;
            if (triangles[i + 2] == remove) triangles[i + 2] = keep;

            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (a == b || b == c || c == a)
            {
                adjacency.TriangleValid[t] = false;
                adjacency.ActiveTriangles--;
            }
        }

        adjacency.VertexTriangles[keep].UnionWith(adjacency.VertexTriangles[remove]);
        adjacency.VertexTriangles[remove].Clear();
        adjacency.VertexNeighbors[keep].UnionWith(adjacency.VertexNeighbors[remove]);
        adjacency.VertexNeighbors[keep].Remove(keep);
        adjacency.VertexNeighbors[keep].Remove(remove);
        foreach (int n in adjacency.VertexNeighbors[remove])
        {
            adjacency.VertexNeighbors[n].Remove(remove);
            if (n != keep) adjacency.VertexNeighbors[n].Add(keep);
        }
        adjacency.VertexNeighbors[remove].Clear();
    }

    private static Mesh BuildCompactedMesh(Mesh source, List<Vector3> vertices, List<int> triangles, bool[] valid)
    {
        var map = new Dictionary<int, int>();
        var compactVerts = new List<Vector3>();
        var compactTriangles = new List<int>();
        for (int t = 0; t < valid.Length; t++)
        {
            if (!valid[t]) continue;
            int i = t * 3;
            int a = Remap(triangles[i], vertices, map, compactVerts);
            int b = Remap(triangles[i + 1], vertices, map, compactVerts);
            int c = Remap(triangles[i + 2], vertices, map, compactVerts);
            if (a == b || b == c || c == a) continue;
            compactTriangles.Add(a);
            compactTriangles.Add(b);
            compactTriangles.Add(c);
        }
        var mesh = new Mesh { name = source.name + "_PriorityQEM" };
        mesh.SetVertices(compactVerts);
        mesh.SetTriangles(compactTriangles, 0);
        return mesh;
    }

    private static int Remap(int oldIndex, List<Vector3> vertices, Dictionary<int, int> map, List<Vector3> compact)
    {
        int next;
        if (map.TryGetValue(oldIndex, out next)) return next;
        next = compact.Count;
        map.Add(oldIndex, next);
        compact.Add(vertices[oldIndex]);
        return next;
    }
}
#endif
