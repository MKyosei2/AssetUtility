// ===== AssetUtility.cs (Reinforced: Shape-QEM++, BoneWeights, Compact, Scale-Normalize, Sliver-Guard) =====
// Place under: Assets/EasyTool/AssetUtility.cs

using EasyTool;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#region EasyTool models & data
// =================================================
namespace EasyTool
{
    [System.Serializable]
    public class MaterialInfo
    {
        public GameObject obj;
        public Texture tex;
        public Vector2Int texSize;
        public long fileSize;
        public int newMaxSize;
        public int originalSize;
        public Material mat;
        public bool modified;
    }

    [System.Serializable]
    public class MeshInfo
    {
        public GameObject obj;
        public Mesh mesh;
        public int triangleCount;
        public long fileSize;
        public int vertexCount;
        public int targetTriangleCount;
        public bool hasAnimation;
        public bool modified;
    }

    // レコード/with 不使用の互換版
    [System.Serializable]
    public class AssetInfo
    {
        public int instanceID;
        public string name;
        public string scene;
        public string scenePath;       // Sceneを跨いでも対象を特定するためのパス
        public string objectPath;      // 同名Object誤爆を避けるための階層パス
        public string objectType;
        public int memoryMB;
        public string materialType;
        public int polygonCount;
        public int targetPolygonCount;
        public string meshPath;        // Mesh参照を名前ではなくAssetPathで特定
        public bool canEditMesh;
        public string textureName;
        public string texturePath;     // Texture参照を名前ではなくAssetPathで特定
        public string textureType;
        public float vramMB;
        public Vector2Int resolution;
        public Vector2Int targetResolution;

        public AssetInfo Clone()
        {
            return new AssetInfo
            {
                instanceID = instanceID,
                name = name,
                scene = scene,
                scenePath = scenePath,
                objectPath = objectPath,
                objectType = objectType,
                memoryMB = memoryMB,
                materialType = materialType,
                polygonCount = polygonCount,
                targetPolygonCount = targetPolygonCount,
                meshPath = meshPath,
                canEditMesh = canEditMesh,
                textureName = textureName,
                texturePath = texturePath,
                textureType = textureType,
                vramMB = vramMB,
                resolution = resolution,
                targetResolution = targetResolution
            };
        }
    }

    [System.Serializable]
    public class TextureEditData
    {
        public string texturePath;
        public Vector2 originalSize;
        public Vector2 editedSize;
        public double processTimeMS;
    }

    [System.Serializable]
    public class MaterialEditData
    {
        public string assetPath;
        public int newMaxSize;
        public double processTimeMS;
    }

    [System.Serializable]
    public class MeshEditData
    {
        public string assetPath;
        public string simplifiedMeshPath;
        public string targetObjectName;
        public string targetObjectPath; // 同名Objectの誤適用を避けるための階層パス
        public string targetScenePath;  // AllScenes適用時に対象Sceneを正確に開くためのパス
        public int targetInstanceID;
        public int targetTriangleCount;
        public double processTimeMS;
    }

    // AssetUtilityData は ScriptableObject として安定させるため AssetUtilityData.cs に分離しました。

}
#endregion

#region AssetOptimizer (Shape-Preserving QEM ++ / BoneWeights / Compact / Safety)
// =================================================
public static class AssetOptimizer
{
    private static ComputeShader simplifyShader;

    // タプルを使わないエッジキー
    private struct Edge
    {
        public int a;
        public int b;
        public Edge(int i, int j)
        {
            if (i <= j) { a = i; b = j; }
            else { a = j; b = i; }
        }
        public override int GetHashCode()
        {
            unchecked { return (a * 397) ^ b; }
        }
        public override bool Equals(object obj)
        {
            if (!(obj is Edge)) return false;
            var e = (Edge)obj;
            return e.a == a && e.b == b;
        }
    }

    // ========= Utility =========
    private static Matrix4x4 MatAdd(Matrix4x4 A, Matrix4x4 B)
    {
        Matrix4x4 C = new Matrix4x4();
        C.m00 = A.m00 + B.m00; C.m01 = A.m01 + B.m01; C.m02 = A.m02 + B.m02; C.m03 = A.m03 + B.m03;
        C.m10 = A.m10 + B.m10; C.m11 = A.m11 + B.m11; C.m12 = A.m12 + B.m12; C.m13 = A.m13 + B.m13;
        C.m20 = A.m20 + B.m20; C.m21 = A.m21 + B.m21; C.m22 = A.m22 + B.m22; C.m23 = A.m23 + B.m23;
        C.m30 = A.m30 + B.m30; C.m31 = A.m31 + B.m31; C.m32 = A.m32 + B.m32; C.m33 = A.m33 + B.m33;
        return C;
    }
    private static Matrix4x4 MatScale(Matrix4x4 A, float s)
    {
        Matrix4x4 C = new Matrix4x4();
        C.m00 = A.m00 * s; C.m01 = A.m01 * s; C.m02 = A.m02 * s; C.m03 = A.m03 * s;
        C.m10 = A.m10 * s; C.m11 = A.m11 * s; C.m12 = A.m12 * s; C.m13 = A.m13 * s;
        C.m20 = A.m20 * s; C.m21 = A.m21 * s; C.m22 = A.m22 * s; C.m23 = A.m23 * s;
        C.m30 = A.m30 * s; C.m31 = A.m31 * s; C.m32 = A.m32 * s; C.m33 = A.m33 * s;
        return C;
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    public struct SimplifyOptions
    {
        public bool preserveBorders;
        public bool preserveUVSeams;
        public bool preserveHardNormals;
        public bool preventNonManifold;
        public float maxPositionError;
        public float maxNormalDeviation;
        public float minTriangleArea;
        public float uvWeight;
        public float normalWeight;
        public float edgeLengthClamp;
        public bool snapToLocalSurface;
        public int maxIterationsPerStep;

        // 強化オプション
        public float sliverAspectMin;       // 極細(スリバー)抑止のための最小アスペクト
        public float curvatureWeight;       // 曲率(エッジの曲がり)を潰しにくくする重み
        public bool compactOnFinish;        // 未使用頂点を削除しメッシュを圧縮
        public bool recomputeQuadricsLocally;// Collapse後に近傍三角形から Qa を再構築（コスト↑）

        public static SimplifyOptions Default
        {
            get
            {
                SimplifyOptions o = new SimplifyOptions();
                o.preserveBorders = true;
                o.preserveUVSeams = true;
                o.preserveHardNormals = true;
                o.preventNonManifold = true;
                o.maxPositionError = 0f;
                o.maxNormalDeviation = 45f;
                o.minTriangleArea = 1e-10f;
                o.uvWeight = 0.25f;
                o.normalWeight = 0.5f;
                o.edgeLengthClamp = 2.0f;
                o.snapToLocalSurface = true;
                o.maxIterationsPerStep = 30000; // 少し増やす

                // 追加の既定
                o.sliverAspectMin = 0.02f;     // 小さすぎる高さ/長辺比は不可
                o.curvatureWeight = 0.3f;      // 曲率ペナルティ
                o.compactOnFinish = true;
                o.recomputeQuadricsLocally = false;
                return o;
            }
        }
    }

    // ===== テクスチャ高速リサイズ =====
    public static string ForceResizeAndSaveTexture(string originalPath, int width, int height)
    {
        Texture sourceTex = AssetDatabase.LoadAssetAtPath<Texture>(originalPath);
        if (sourceTex == null)
        {
            Debug.LogError("[ResizeGPU] テクスチャが見つかりません: " + originalPath);
            return null;
        }

        Texture2D sourceTex2D = sourceTex as Texture2D;
        if (sourceTex2D == null)
        {
            Debug.LogError("[ResizeGPU] Texture2D のみ対応しています");
            return null;
        }

        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Graphics.ConvertTexture(sourceTex2D, 0, resized, 0);
        byte[] pngBytes = resized.EncodeToPNG();
        string savePath = originalPath.Replace(".png", "_gpu_" + width + "x" + height + ".png");
        File.WriteAllBytes(savePath, pngBytes);
        AssetDatabase.ImportAsset(savePath);
        Debug.Log("🖼️ 高速テクスチャ変換完了: " + savePath);
        return savePath;
    }

    // ---- 形状維持QEM：ヘルパー ----
    private static int TriCount(List<int> tris) { return tris.Count / 3; }
    private static float TriArea(Vector3 a, Vector3 b, Vector3 c) { return Vector3.Cross(b - a, c - a).magnitude * 0.5f; }

    // 3x3 正規方程式を倍精度で解く（安定性↑）
    private static bool SolveOptimalVertex(Matrix4x4 Qa, Matrix4x4 Qb, Vector3 a, Vector3 b, out Vector3 x)
    {
        double A11 = (double)Qa.m00 + (double)Qb.m00;
        double A12 = (double)Qa.m01 + (double)Qb.m01;
        double A13 = (double)Qa.m02 + (double)Qb.m02;
        double t1 = (double)Qa.m03 + (double)Qb.m03;

        double A21 = (double)Qa.m10 + (double)Qb.m10;
        double A22 = (double)Qa.m11 + (double)Qb.m11;
        double A23 = (double)Qa.m12 + (double)Qb.m12;
        double t2 = (double)Qa.m13 + (double)Qb.m13;

        double A31 = (double)Qa.m20 + (double)Qb.m20;
        double A32 = (double)Qa.m21 + (double)Qb.m21;
        double A33 = (double)Qa.m22 + (double)Qb.m22;
        double t3 = (double)Qa.m23 + (double)Qb.m23;

        double det =
            A11 * (A22 * A33 - A23 * A32) -
            A12 * (A21 * A33 - A23 * A31) +
            A13 * (A21 * A32 - A22 * A31);

        if (System.Math.Abs(det) < 1e-18)
        {
            x = 0.5f * (a + b);
            return false;
        }

        // adj(A) * (-t)
        double c11 = (A22 * A33 - A23 * A32);
        double c12 = -(A21 * A33 - A23 * A31);
        double c13 = (A21 * A32 - A22 * A31);
        double c21 = -(A12 * A33 - A13 * A32);
        double c22 = (A11 * A33 - A13 * A31);
        double c23 = -(A11 * A32 - A12 * A31);
        double c31 = (A12 * A23 - A13 * A22);
        double c32 = -(A11 * A23 - A13 * A21);
        double c33 = (A11 * A22 - A12 * A21);

        double nx = (-t1), ny = (-t2), nz = (-t3);
        double rx = (c11 * nx + c12 * ny + c13 * nz) / det;
        double ry = (c21 * nx + c22 * ny + c23 * nz) / det;
        double rz = (c31 * nx + c32 * ny + c33 * nz) / det;

        x = new Vector3((float)rx, (float)ry, (float)rz);
        if (!IsFinite(x)) { x = 0.5f * (a + b); return false; }
        return true;
    }

    private static Vector3 ClosestPointOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + v * ab;
        }

        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + w * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        float denom = 1f / (va + vb + vc);
        float v2 = vb * denom, w2 = vc * denom;
        return a + ab * v2 + ac * w2;
    }

    private static Vector3 ProjectToLocalSurface(Vector3 candidate, int keep, int removed, List<int> tris, List<Vector3> verts)
    {
        float best = float.PositiveInfinity;
        Vector3 bestP = candidate;

        for (int i = 0; i < tris.Count; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            if (i0 != keep && i1 != keep && i2 != keep && i0 != removed && i1 != removed && i2 != removed) continue;

            int a = i0, b = i1, c = i2;
            if (a == removed) a = keep;
            if (b == removed) b = keep;
            if (c == removed) c = keep;
            if (a == b || b == c || c == a) continue;

            Vector3 q = ClosestPointOnTri(candidate, verts[i0], verts[i1], verts[i2]);
            float d = (q - candidate).sqrMagnitude;
            if (d < best) { best = d; bestP = q; }
        }
        return bestP;
    }

    private static bool TriangleSliver(Vector3 pa, Vector3 pb, Vector3 pc, float aspectMin)
    {
        // 高さ/長辺比 ≈ 2*Area / maxEdgeLen
        float la = (pb - pa).sqrMagnitude;
        float lb = (pc - pb).sqrMagnitude;
        float lc = (pa - pc).sqrMagnitude;
        float maxLen = Mathf.Sqrt(Mathf.Max(la, Mathf.Max(lb, lc)));
        float area = TriArea(pa, pb, pc);
        if (maxLen < 1e-12f) return true;
        float height = (2f * area) / maxLen;
        float ratio = height / (maxLen + 1e-20f);
        return ratio < aspectMin;
    }

    private static bool WouldFlipOrDegenerate(int keep, int removed, Vector3 newPos, List<int> tris, List<Vector3> verts, SimplifyOptions opt)
    {
        for (int i = 0; i < tris.Count; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            if (i0 != keep && i1 != keep && i2 != keep && i0 != removed && i1 != removed && i2 != removed) continue;

            int a = i0, b = i1, c = i2;
            if (a == removed) a = keep;
            if (b == removed) b = keep;
            if (c == removed) c = keep;
            if (a == b || b == c || c == a) continue;

            var pa = (a == keep) ? newPos : verts[a];
            var pb = (b == keep) ? newPos : verts[b];
            var pc = (c == keep) ? newPos : verts[c];

            float area = TriArea(pa, pb, pc);
            if (area < opt.minTriangleArea) return true;
            if (TriangleSliver(pa, pb, pc, opt.sliverAspectMin)) return true;

            var n0 = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
            var n1 = Vector3.Cross(pb - pa, pc - pa).normalized;
            if (n0.sqrMagnitude > 1e-12f && n1.sqrMagnitude > 1e-12f)
            {
                float dot = Vector3.Dot(n0, n1);
                if (dot < Mathf.Cos(opt.maxNormalDeviation * Mathf.Deg2Rad)) return true;
            }
        }
        return false;
    }

    private static float EdgeCurvaturePenalty(int a, int b, List<int> tris, List<Vector3> verts)
    {
        // a-b を含む三角形の法線差分を平均（簡易曲率）
        Vector3 na = Vector3.zero, nb = Vector3.zero;
        int ca = 0, cb = 0;

        for (int i = 0; i < tris.Count; i += 3)
        {
            int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
            if (i0 == a || i1 == a || i2 == a)
            {
                Vector3 n = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
                if (n.sqrMagnitude > 1e-12f) { na += n; ca++; }
            }
            if (i0 == b || i1 == b || i2 == b)
            {
                Vector3 n = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
                if (n.sqrMagnitude > 1e-12f) { nb += n; cb++; }
            }
        }
        if (ca > 0) na /= ca;
        if (cb > 0) nb /= cb;
        if (na.sqrMagnitude < 1e-12f || nb.sqrMagnitude < 1e-12f) return 0f;
        float d = Mathf.Clamp01(1f - Vector3.Dot(na.normalized, nb.normalized));
        return d; // 0..1
    }

    private static float EdgeCost(
        Edge e,
        Matrix4x4[] Q,
        List<Vector2> uvs,
        List<Vector3> normals,
        HashSet<Edge> isBorder,
        HashSet<Edge> isUVSeam,
        HashSet<Edge> isHard,
        SimplifyOptions opt,
        List<int> tris,
        List<Vector3> verts,
        out Vector3 bestPos)
    {
        int a = e.a, b = e.b;
        Vector3 x;
        bool solved = SolveOptimalVertex(Q[a], Q[b], verts[a], verts[b], out x);
        if (!solved) x = 0.5f * (verts[a] + verts[b]);
        if (!IsFinite(x)) x = 0.5f * (verts[a] + verts[b]);
        if (opt.snapToLocalSurface) x = ProjectToLocalSurface(x, a, b, tris, verts);

        // 過伸長防止（長さ比）
        if (opt.edgeLengthClamp > 0f)
        {
            float oldLen = (verts[a] - verts[b]).magnitude + 1e-12f;
            float na = (x - verts[b]).magnitude / oldLen;
            float nb = (x - verts[a]).magnitude / oldLen;
            if (na > opt.edgeLengthClamp || nb > opt.edgeLengthClamp)
            {
                bestPos = x;
                return float.PositiveInfinity;
            }
        }

        Vector4 v = new Vector4(x.x, x.y, x.z, 1f);
        float qem = Vector4.Dot(v, Q[a] * v) + Vector4.Dot(v, Q[b] * v);

        // UV/法線コスト
        float uvCost = (uvs[a] - uvs[b]).sqrMagnitude;
        float nCost = 1f - Mathf.Clamp01(Vector3.Dot(normals[a].normalized, normals[b].normalized));

        // 曲率ペナルティ（曲がりが大きいほど潰しにくい）
        float curv = EdgeCurvaturePenalty(a, b, tris, verts);

        float penalty = 0f;
        Edge key = new Edge(a, b);
        if (opt.preserveBorders && isBorder.Contains(key)) penalty += 1e8f;
        if (opt.preserveUVSeams && isUVSeam.Contains(key)) penalty += 1e8f;
        if (opt.preserveHardNormals && isHard.Contains(key)) penalty += 1e8f;

        bestPos = x;
        return qem + opt.uvWeight * uvCost + opt.normalWeight * nCost + opt.curvatureWeight * curv + penalty;
    }

    // ===== BoneWeight 結合（上位4本へ凝縮） =====
    private static BoneWeight BlendBoneWeights(BoneWeight A, BoneWeight B)
    {
        // BoneIndex -> weight の簡易マージ
        Dictionary<int, float> map = new Dictionary<int, float>();

        System.Action<int, float> Add = (idx, w) =>
        {
            if (idx < 0) return;
            float prev;
            if (!map.TryGetValue(idx, out prev)) map[idx] = w;
            else map[idx] = prev + w;
        };

        Add(A.boneIndex0, A.weight0);
        Add(A.boneIndex1, A.weight1);
        Add(A.boneIndex2, A.weight2);
        Add(A.boneIndex3, A.weight3);
        Add(B.boneIndex0, B.weight0);
        Add(B.boneIndex1, B.weight1);
        Add(B.boneIndex2, B.weight2);
        Add(B.boneIndex3, B.weight3);

        // 上位4件
        List<KeyValuePair<int, float>> list = new List<KeyValuePair<int, float>>(map);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        float sum = 0f;
        int count = Mathf.Min(4, list.Count);
        for (int i = 0; i < count; i++) sum += list[i].Value;
        if (sum <= 1e-20f) sum = 1f;

        BoneWeight bw = new BoneWeight();
        if (count > 0) { bw.boneIndex0 = list[0].Key; bw.weight0 = list[0].Value / sum; } else { bw.boneIndex0 = 0; bw.weight0 = 0; }
        if (count > 1) { bw.boneIndex1 = list[1].Key; bw.weight1 = list[1].Value / sum; } else { bw.boneIndex1 = 0; bw.weight1 = 0; }
        if (count > 2) { bw.boneIndex2 = list[2].Key; bw.weight2 = list[2].Value / sum; } else { bw.boneIndex2 = 0; bw.weight2 = 0; }
        if (count > 3) { bw.boneIndex3 = list[3].Key; bw.weight3 = list[3].Value / sum; } else { bw.boneIndex3 = 0; bw.weight3 = 0; }
        return bw;
    }

    // ===== 頂点圧縮・未使用頂点削除（UV/法線/色/ボーンなど対応） =====
    private static Mesh BuildCompactMesh(
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        List<Vector3> normals,
        Color[] colors,
        BoneWeight[] boneWeights,
        Matrix4x4[] bindposes)
    {
        int n = verts.Count;
        bool[] used = new bool[n];
        for (int i = 0; i < tris.Count; i++) { int id = tris[i]; if (id >= 0 && id < n) used[id] = true; }

        int[] remap = new int[n];
        List<Vector3> nv = new List<Vector3>();
        List<Vector2> nuv = new List<Vector2>();
        List<Vector3> nnorm = new List<Vector3>();
        List<Color> ncol = colors != null && colors.Length == n ? new List<Color>() : null;
        List<BoneWeight> nbw = boneWeights != null && boneWeights.Length == n ? new List<BoneWeight>() : null;

        int next = 0;
        for (int i = 0; i < n; i++)
        {
            if (!used[i]) { remap[i] = -1; continue; }
            remap[i] = next++;
            nv.Add(verts[i]);
            if (uvs != null && uvs.Count == n) nuv.Add(uvs[i]);
            if (normals != null && normals.Count == n) nnorm.Add(normals[i]);
            if (ncol != null) ncol.Add(colors[i]);
            if (nbw != null) nbw.Add(boneWeights[i]);
        }

        List<int> nt = new List<int>(tris.Count);
        for (int i = 0; i < tris.Count; i++)
        {
            int old = tris[i];
            int mapped = old >= 0 && old < n ? remap[old] : -1;
            nt.Add(mapped);
        }

        // 退化面除去
        for (int i = nt.Count - 3; i >= 0; i -= 3)
        {
            int a = nt[i], b = nt[i + 1], c = nt[i + 2];
            if (a == b || b == c || c == a || a < 0 || b < 0 || c < 0)
                nt.RemoveRange(i, 3);
        }

        Mesh m = new Mesh();
#if UNITY_2017_3_OR_NEWER
        if (nv.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
        m.SetVertices(nv);
        if (nuv != null && nuv.Count == nv.Count) m.SetUVs(0, nuv);
        if (nnorm != null && nnorm.Count == nv.Count) m.SetNormals(nnorm);
        if (ncol != null && ncol.Count == nv.Count) m.SetColors(ncol);
        m.SetTriangles(nt, 0, true);

        if (nbw != null && nbw.Count == nv.Count)
        {
            m.boneWeights = nbw.ToArray();
            if (bindposes != null && bindposes.Length > 0) m.bindposes = bindposes;
        }

        m.RecalculateBounds();
        m.RecalculateNormals();
        m.RecalculateTangents();
        return m;
    }

    // ========= 形状維持QEM 本体（面積重み + スケール正規化 + スリバー/曲率対策 + BoneWeight保持 + 圧縮）=========
    public static Mesh SimplifyQEM(Mesh src, int targetTris, SimplifyOptions opt)
    {
        // 複数サブメッシュはマテリアル崩壊を避けるためスキップ
        if (src.subMeshCount > 1)
        {
            Debug.LogWarning("⚠️ 複数サブメッシュのメッシュは QEM をスキップします（マテリアル割当維持のため）。");
            return Object.Instantiate(src);
        }

        // 入力属性
        Color[] colors = src.colors != null && src.colors.Length == src.vertexCount ? src.colors : null;
        BoneWeight[] boneWeights = src.boneWeights != null && src.boneWeights.Length == src.vertexCount ? src.boneWeights : null;
        Matrix4x4[] bindposes = (src.bindposes != null && src.bindposes.Length > 0) ? src.bindposes : null;

        Mesh mesh = Object.Instantiate(src);
        mesh.RecalculateNormals();

        List<Vector3> verts = new List<Vector3>(mesh.vertices);
        List<Vector3> normals = new List<Vector3>(mesh.normals);
        List<Vector2> uvs = new List<Vector2>(mesh.uv.Length == mesh.vertexCount ? mesh.uv : new Vector2[mesh.vertexCount]);
        List<int> tris = new List<int>(mesh.triangles);
        if (tris.Count < 3) return mesh;

        if (TriCount(tris) <= targetTris) return mesh;

        // スケール正規化（極端スケールの安定性向上）
        Bounds b = mesh.bounds;
        float diag = b.size.magnitude;
        float scale = 1f;
        if (diag > 0f && (diag < 0.01f || diag > 1000f))
        {
            scale = 1f / Mathf.Clamp(diag, 1e-6f, 1e6f);
            for (int i = 0; i < verts.Count; i++) verts[i] *= scale;
            if (opt.maxPositionError > 0f) opt.maxPositionError *= scale;
            opt.minTriangleArea *= (scale * scale);
        }

        // ----- エッジ/境界/属性シーム -----
        Dictionary<Edge, int> edgeCount = new Dictionary<Edge, int>();
        System.Action<int, int> AddEdge = (ia, ib) =>
        {
            Edge k = new Edge(ia, ib);
            int v;
            if (!edgeCount.TryGetValue(k, out v)) edgeCount[k] = 1;
            else edgeCount[k] = v + 1;
        };

        for (int t = 0; t < TriCount(tris); t++)
        {
            int a = tris[t * 3], b0 = tris[t * 3 + 1], c = tris[t * 3 + 2];
            AddEdge(a, b0); AddEdge(b0, c); AddEdge(c, a);
        }

        HashSet<Edge> isBorder = new HashSet<Edge>();
        foreach (KeyValuePair<Edge, int> kv in edgeCount)
            if (kv.Value == 1) isBorder.Add(kv.Key);

        HashSet<Edge> isUVSeam = new HashSet<Edge>();
        // UV seam判定の精度修正:
        // 以前は「エッジ両端のUVが違う」だけでUV seam扱いしていました。
        // しかし通常のメッシュでは隣り合う頂点のUVは必ず違うため、ほぼ全エッジが固定され、
        // QEMが一切Collapseできず 12800 → 12800 のような未削減結果になっていました。
        // Unity Meshでは本当のUV seamは頂点分離として表れることが多いため、ここでは
        // 端点UV差による全エッジ固定をやめ、境界/HardNormal/Flip防止側で形状保護します。

        HashSet<Edge> isHard = new HashSet<Edge>();
        float hardAngle = 60f;
        foreach (Edge k in edgeCount.Keys)
        {
            int a = k.a, b0 = k.b;
            if (Vector3.Angle(normals[a], normals[b0]) > hardAngle) isHard.Add(k);
        }

        // ----- 頂点 Quadric 作成（面積重み） -----
        Matrix4x4[] Q = new Matrix4x4[verts.Count];
        for (int i = 0; i < Q.Length; i++) Q[i] = Matrix4x4.zero;

        for (int t = 0; t < TriCount(tris); t++)
        {
            int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
            Vector3 p0 = verts[i0]; Vector3 p1 = verts[i1]; Vector3 p2 = verts[i2];
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            float area2 = n.magnitude; // 2*area
            if (area2 < 1e-12f) continue;
            n = n / area2;
            float d = -Vector3.Dot(n, p0);
            float a = n.x, b0 = n.y, c = n.z;

            Matrix4x4 kp = new Matrix4x4(
                new Vector4(a * a, a * b0, a * c, a * d),
                new Vector4(b0 * a, b0 * b0, b0 * c, b0 * d),
                new Vector4(c * a, c * b0, c * c, c * d),
                new Vector4(d * a, d * b0, d * c, d * d)
            );
            kp = MatScale(kp, area2 * 0.5f);

            Q[i0] = MatAdd(Q[i0], kp);
            Q[i1] = MatAdd(Q[i1], kp);
            Q[i2] = MatAdd(Q[i2], kp);
        }

        HashSet<Edge> edges = new HashSet<Edge>(edgeCount.Keys);

        int safety = 0;
        int target = Mathf.Max(1, targetTris);

        // BoneWeight 作業配列（存在時のみ）
        List<BoneWeight> bwList = null;
        if (boneWeights != null && boneWeights.Length == verts.Count) bwList = new List<BoneWeight>(boneWeights);

        while (TriCount(tris) > target && safety++ < opt.maxIterationsPerStep)
        {
            float bestC = float.PositiveInfinity;
            Edge bestE = new Edge(-1, -1);
            Vector3 bestX = Vector3.zero;

            foreach (Edge e in edges)
            {
                int a = e.a; int b0 = e.b;
                Edge key = new Edge(a, b0);
                if (opt.preserveBorders && isBorder.Contains(key)) continue;
                if (opt.preserveUVSeams && isUVSeam.Contains(key)) continue;
                if (opt.preserveHardNormals && isHard.Contains(key)) continue;

                Vector3 x;
                float c = EdgeCost(e, Q, uvs, normals, isBorder, isUVSeam, isHard, opt, tris, verts, out x);
                if (c < bestC)
                {
                    if (WouldFlipOrDegenerate(a, b0, x, tris, verts, opt)) continue;
                    bestC = c; bestE = e; bestX = x;
                }
            }

            if (float.IsInfinity(bestC) || bestE.a < 0) break;

            int A = bestE.a, B2 = bestE.b;

            // 位置誤差制限
            if (opt.maxPositionError > 0f)
            {
                float errA = (bestX - verts[A]).magnitude;
                float errB = (bestX - verts[B2]).magnitude;
                if (Mathf.Max(errA, errB) > opt.maxPositionError)
                {
                    edges.Remove(bestE);
                    continue;
                }
            }

            // Collapse: B -> A
            verts[A] = bestX;
            uvs[A] = (uvs[A] + uvs[B2]) * 0.5f;
            normals[A] = (normals[A] + normals[B2]).normalized;

            if (bwList != null)
            {
                BoneWeight merged = BlendBoneWeights(bwList[A], bwList[B2]);
                bwList[A] = merged;
            }

            Q[A] = MatAdd(Q[A], Q[B2]);

            // トライアングルの B を A に差し替え
            for (int i = 0; i < tris.Count; i++) if (tris[i] == B2) tris[i] = A;

            // 退化面削除
            for (int i = tris.Count - 3; i >= 0; i -= 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                if (i0 == i1 || i1 == i2 || i2 == i0) tris.RemoveRange(i, 3);
            }

            // オプション：近傍から Qa を再構築（高コスト）
            if (opt.recomputeQuadricsLocally)
            {
                Q[A] = Matrix4x4.zero;
                for (int t = 0; t < TriCount(tris); t++)
                {
                    int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                    if (i0 != A && i1 != A && i2 != A) continue;

                    Vector3 p0 = verts[i0]; Vector3 p1 = verts[i1]; Vector3 p2 = verts[i2];
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                    float area2 = n.magnitude; if (area2 < 1e-12f) continue;
                    n = n / area2;
                    float d = -Vector3.Dot(n, p0);
                    float a = n.x, b0 = n.y, c = n.z;

                    Matrix4x4 kp = new Matrix4x4(
                        new Vector4(a * a, a * b0, a * c, a * d),
                        new Vector4(b0 * a, b0 * b0, b0 * c, b0 * d),
                        new Vector4(c * a, c * b0, c * c, c * d),
                        new Vector4(d * a, d * b0, d * c, d * d)
                    );
                    kp = MatScale(kp, area2 * 0.5f);
                    Q[A] = MatAdd(Q[A], kp);
                }
            }

            // 境界・エッジ再構築
            if (opt.preventNonManifold)
            {
                edgeCount.Clear();
                for (int t = 0; t < TriCount(tris); t++)
                {
                    int a = tris[t * 3], b0 = tris[t * 3 + 1], c = tris[t * 3 + 2];
                    Edge e1 = new Edge(a, b0);
                    Edge e2 = new Edge(b0, c);
                    Edge e3 = new Edge(c, a);
                    int v;
                    if (!edgeCount.TryGetValue(e1, out v)) edgeCount[e1] = 1; else edgeCount[e1] = v + 1;
                    if (!edgeCount.TryGetValue(e2, out v)) edgeCount[e2] = 1; else edgeCount[e2] = v + 1;
                    if (!edgeCount.TryGetValue(e3, out v)) edgeCount[e3] = 1; else edgeCount[e3] = v + 1;
                }

                isBorder.Clear();
                foreach (KeyValuePair<Edge, int> kv in edgeCount)
                    if (kv.Value == 1) isBorder.Add(kv.Key);

                // 非多様体（>2）を候補から除外
                List<Edge> nonManifold = new List<Edge>();
                foreach (KeyValuePair<Edge, int> kv in edgeCount)
                    if (kv.Value > 2) nonManifold.Add(kv.Key);
                for (int i = 0; i < nonManifold.Count; i++) edges.Remove(nonManifold[i]);
            }

            edges.Clear();
            for (int t = 0; t < TriCount(tris); t++)
            {
                int a = tris[t * 3], b0 = tris[t * 3 + 1], c = tris[t * 3 + 2];
                edges.Add(new Edge(a, b0));
                edges.Add(new Edge(b0, c));
                edges.Add(new Edge(c, a));
            }
        }

        // スケール戻し
        if (scale != 1f)
        {
            float inv = 1f / scale;
            for (int i = 0; i < verts.Count; i++) verts[i] *= inv;
        }

        // 圧縮 or そのまま
        Mesh outMesh;
        if (opt.compactOnFinish)
        {
            outMesh = BuildCompactMesh(
                verts,
                tris,
                uvs,
                normals,
                colors,
                bwList != null ? bwList.ToArray() : null,
                bindposes
            );
        }
        else
        {
            Mesh m = new Mesh();
#if UNITY_2017_3_OR_NEWER
            if (verts.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            if (colors != null && colors.Length == verts.Count) m.colors = colors;
            if (bwList != null && bwList.Count == verts.Count)
            {
                m.boneWeights = bwList.ToArray();
                if (bindposes != null && bindposes.Length > 0) m.bindposes = bindposes;
            }
            m.RecalculateBounds();
            m.RecalculateNormals();
            m.RecalculateTangents();
            outMesh = m;
        }

        return outMesh;
    }

    // ===== Loop subdiv + smoothing（必要なら増やす時用） =====
    public static Mesh SubdivideLoop(Mesh src, int steps = 1, bool smooth = true)
    {
        if (src.subMeshCount > 1) return Object.Instantiate(src);

        Mesh mesh = Object.Instantiate(src);
        mesh.RecalculateNormals();

        for (int step = 0; step < steps; step++)
        {
            List<Vector3> verts = new List<Vector3>(mesh.vertices);
            List<Vector2> uvs = new List<Vector2>(mesh.uv.Length == verts.Count ? mesh.uv : new Vector2[verts.Count]);
            List<int> tris = new List<int>(mesh.triangles);

            Dictionary<Edge, int> edgeNewIndex = new Dictionary<Edge, int>();
            System.Func<int, int, int> AddEdgePoint = (a, b) =>
            {
                Edge key = new Edge(a, b);
                int idx;
                if (edgeNewIndex.TryGetValue(key, out idx)) return idx;
                Vector3 p = 0.5f * (verts[a] + verts[b]);
                Vector2 uv = 0.5f * (uvs[a] + uvs[b]);
                int ni = verts.Count; verts.Add(p); uvs.Add(uv); edgeNewIndex[key] = ni; return ni;
            };

            List<int> newTris = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int v0 = tris[i], v1 = tris[i + 1], v2 = tris[i + 2];
                int e01 = AddEdgePoint(v0, v1);
                int e12 = AddEdgePoint(v1, v2);
                int e20 = AddEdgePoint(v2, v0);

                newTris.AddRange(new int[] { v0, e01, e20 });
                newTris.AddRange(new int[] { v1, e12, e01 });
                newTris.AddRange(new int[] { v2, e20, e12 });
                newTris.AddRange(new int[] { e01, e12, e20 });
            }

            Mesh outMesh = new Mesh();
            outMesh.SetVertices(verts);
            outMesh.SetUVs(0, uvs);
            outMesh.SetTriangles(newTris, 0);

            if (smooth)
            {
                outMesh.RecalculateNormals();
                LaplacianSmooth(outMesh, 0.5f);
                LaplacianSmooth(outMesh, -0.53f);
            }
            outMesh.RecalculateNormals();
            outMesh.RecalculateTangents();
            mesh = outMesh;
        }
        return mesh;
    }

    private static void LaplacianSmooth(Mesh m, float lambda)
    {
        Vector3[] verts = m.vertices;
        int[] tris = m.triangles;
        int n = verts.Length;

        List<HashSet<int>> adj = new List<HashSet<int>>(n);
        for (int i = 0; i < n; i++) adj.Add(new HashSet<int>());
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            adj[a].Add(b); adj[a].Add(c);
            adj[b].Add(a); adj[b].Add(c);
            adj[c].Add(a); adj[c].Add(b);
        }

        Vector3[] outV = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (adj[i].Count == 0) { outV[i] = verts[i]; continue; }
            Vector3 mean = Vector3.zero;
            foreach (int j in adj[i]) mean += verts[j];
            mean /= adj[i].Count;
            outV[i] = verts[i] + lambda * (mean - verts[i]);
        }
        m.vertices = outV;
    }

    // ===== 既存GPU/フォールバック =====
    public static Mesh Simplify(Mesh mesh, int targetTris)
    {
        if (mesh == null) return null;

        if (simplifyShader == null)
            simplifyShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/Shaders/SimplifyMesh.compute");

        if (simplifyShader != null)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Mesh result = SimplifyWithCompute(mesh, simplifyShader, targetTris);
                sw.Stop();
                Debug.Log("⏱️ GPU簡略化完了: " + mesh.name + " → " + targetTris + " tris （" + sw.ElapsedMilliseconds + " ms）");
                return result;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("ComputeShader 簡略化失敗（Fallback使用）: " + ex.Message);
            }
        }
        return FallbackSimplify(mesh, targetTris);
    }

    // パイプライン：保存前に UV2 を生成（ライトマップ破綻回避）
    public static void SaveOptimizedMesh(Mesh mesh, string fileName, out string savedPath)
    {
        savedPath = string.Empty;
        if (mesh == null || mesh.vertexCount == 0 || mesh.triangles == null || mesh.triangles.Length == 0)
        {
            Debug.LogWarning("❌ 最適化されたメッシュが不正なため保存を中止: " + fileName);
            return;
        }

#if UNITY_EDITOR
        // UV2 がない場合は自動生成（可能な環境のみ）
        try
        {
            List<Vector2> uv2 = new List<Vector2>();
            mesh.GetUVs(1, uv2);
            if (uv2 == null || uv2.Count != mesh.vertexCount)
            {
                Unwrapping.GenerateSecondaryUVSet(mesh);
            }
        }
        catch { /* 一部環境では未サポート */ }
#endif

        string folder = "Assets/OptimizedMeshes";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        savedPath = Path.Combine(folder, fileName + ".asset");
        if (AssetDatabase.LoadAssetAtPath<Mesh>(savedPath)) AssetDatabase.DeleteAsset(savedPath);

        AssetDatabase.CreateAsset(mesh, savedPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(savedPath);
        Debug.Log("💾 メッシュ保存: " + savedPath);
    }

    // CPU フォールバック（保守的）
    public static Mesh FallbackSimplify(Mesh mesh, int targetTris)
    {
        if (mesh.subMeshCount > 1) return Object.Instantiate(mesh);

        List<Vector3> verts = new List<Vector3>(mesh.vertices);
        List<Vector2> uvs = new List<Vector2>(mesh.uv);
        List<int> tris = new List<int>(mesh.triangles);
        int iter = 0;
        int maxIterations = 5000;

        while (tris.Count / 3 > targetTris && iter++ < maxIterations)
        {
            bool collapsed = false;

            for (int i = 0; i < tris.Count && !collapsed; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];

                if (a < 0 || a >= uvs.Count || b < 0 || b >= uvs.Count) continue;
                if (Vector2.Distance(uvs[a], uvs[b]) > 0.01f) continue;

                Vector3 mid = (verts[a] + verts[b]) * 0.5f;

                // 面が潰れない最小チェック
                bool bad = false;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    if (i0 != a && i1 != a && i2 != a && i0 != b && i1 != b && i2 != b) continue;
                    int va = (i0 == a || i0 == b) ? -1 : i0;
                    int vb = (i1 == a || i1 == b) ? -1 : i1;
                    int vc = (i2 == a || i2 == b) ? -1 : i2;
                    Vector3 pa = (va == -1) ? mid : verts[va];
                    Vector3 pb = (vb == -1) ? mid : verts[vb];
                    Vector3 pc = (vc == -1) ? mid : verts[vc];
                    if (TriArea(pa, pb, pc) < 1e-12f) { bad = true; break; }
                }
                if (bad) continue;

                Vector2 midUV = (uvs[a] + uvs[b]) * 0.5f;
                int newIdx = verts.Count;
                verts.Add(mid); uvs.Add(midUV);

                for (int j = 0; j < tris.Count; j++)
                    if (tris[j] == a || tris[j] == b)
                        tris[j] = newIdx;

                for (int j = tris.Count - 3; j >= 0; j -= 3)
                {
                    int v0 = tris[j], v1 = tris[j + 1], v2 = tris[j + 2];
                    if (v0 == v1 || v1 == v2 || v0 == v2)
                        tris.RemoveRange(j, 3);
                }

                collapsed = true;
            }

            if (!collapsed) break;
        }

        Mesh result = new Mesh();
#if UNITY_2017_3_OR_NEWER
        if (verts.Count > 65535) result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
        result.SetVertices(verts);
        result.SetUVs(0, uvs);
        result.SetTriangles(tris, 0);
        result.RecalculateNormals();
        result.RecalculateTangents();
        return result;
    }

    public static Mesh SimplifyWithCompute(Mesh mesh, ComputeShader shader, int targetTris)
    {
        if (mesh.subMeshCount > 1)
        {
            Debug.LogWarning("⚠️ GPU 簡略化は複数サブメッシュ未対応のためスキップします。");
            return Object.Instantiate(mesh);
        }

        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;

        int triCount = tris.Length / 3;
        if (triCount <= targetTris)
        {
            Debug.Log("✅ " + mesh.name + " はすでに目標以下のポリゴン数（" + triCount + "）");
            return Object.Instantiate(mesh);
        }

        int vertCount = verts.Length;

        ComputeBuffer vertBuffer = new ComputeBuffer(vertCount, sizeof(float) * 3);
        ComputeBuffer triBuffer = new ComputeBuffer(tris.Length, sizeof(int));
        ComputeBuffer outVerts = new ComputeBuffer(vertCount, sizeof(float) * 3, ComputeBufferType.Append);
        ComputeBuffer outTris = new ComputeBuffer(tris.Length, sizeof(int), ComputeBufferType.Append);
        ComputeBuffer targetCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        outVerts.SetCounterValue(0);
        outTris.SetCounterValue(0);

        vertBuffer.SetData(verts);
        triBuffer.SetData(tris);
        targetCountBuffer.SetData(new int[] { targetTris });

        if (!shader.HasKernel("CSMain"))
        {
            Debug.LogError("CSMain カーネルが存在しません");
            return FallbackSimplify(mesh, targetTris);
        }

        int kernel = shader.FindKernel("CSMain");
        shader.SetBuffer(kernel, "_Vertices", vertBuffer);
        shader.SetBuffer(kernel, "_Triangles", triBuffer);
        shader.SetBuffer(kernel, "_OutVertices", outVerts);
        shader.SetBuffer(kernel, "_OutTriangles", outTris);
        shader.SetBuffer(kernel, "_TargetTris", targetCountBuffer);

        shader.Dispatch(kernel, Mathf.CeilToInt(triCount / 64f), 1, 1);

        ComputeBuffer.CopyCount(outVerts, targetCountBuffer, 0);
        int[] resultCounts = new int[1];
        targetCountBuffer.GetData(resultCounts);
        int actualTris = resultCounts[0];

        if (actualTris == 0)
        {
            Debug.LogWarning("[SimplifyWithCompute] " + mesh.name + " のGPU簡略化結果が空でした。Fallbackへ移行します。");
            vertBuffer.Release(); triBuffer.Release(); outVerts.Release(); outTris.Release(); targetCountBuffer.Release();
            return FallbackSimplify(mesh, targetTris);
        }

        Vector3[] newVerts = new Vector3[vertCount];
        int[] newTris = new int[tris.Length];
        outVerts.GetData(newVerts);
        outTris.GetData(newTris);

        Mesh result = new Mesh();
        result.vertices = newVerts;
        result.triangles = newTris;
        result.RecalculateNormals();
        result.RecalculateTangents();

        vertBuffer.Release(); triBuffer.Release(); outVerts.Release(); outTris.Release(); targetCountBuffer.Release();

        Debug.Log("⚙️ GPU簡略化完了: " + mesh.name + " → " + actualTris + " tris");
        return result;
    }

    public static Mesh SimplifyWithCompute(Mesh mesh, ComputeShader shader)
    {
        if (mesh.subMeshCount > 1) return Object.Instantiate(mesh);

        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;

        int vertCount = verts.Length;
        int triCount = tris.Length;

        ComputeBuffer vertBuffer = new ComputeBuffer(vertCount, sizeof(float) * 3);
        ComputeBuffer triBuffer = new ComputeBuffer(triCount, sizeof(int));

        int maxOutput = triCount * 2;
        ComputeBuffer outVerts = new ComputeBuffer(maxOutput, sizeof(float) * 3, ComputeBufferType.Append);
        ComputeBuffer outTris = new ComputeBuffer(maxOutput, sizeof(int), ComputeBufferType.Append);
        ComputeBuffer outCount = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        outVerts.SetCounterValue(0);
        outTris.SetCounterValue(0);
        outCount.SetData(new int[] { 0 });

        vertBuffer.SetData(verts);
        triBuffer.SetData(tris);

        if (!shader.HasKernel("CSMain"))
        {
            Debug.LogError("CSMain カーネルが存在しません");
            return Object.Instantiate(mesh);
        }

        int kernel = shader.FindKernel("CSMain");
        shader.SetBuffer(kernel, "_Vertices", vertBuffer);
        shader.SetBuffer(kernel, "_Triangles", triBuffer);
        shader.SetBuffer(kernel, "_OutVertices", outVerts);
        shader.SetBuffer(kernel, "_OutTriangles", outTris);
        shader.SetBuffer(kernel, "_OutCount", outCount);

        shader.Dispatch(kernel, Mathf.CeilToInt(triCount / 64f), 1, 1);

        ComputeBuffer.CopyCount(outVerts, outCount, 0);
        int[] resultCounts = new int[1];
        outCount.GetData(resultCounts);

        Vector3[] newVerts = new Vector3[resultCounts[0]];
        int[] newTris = new int[resultCounts[0]];
        outVerts.GetData(newVerts);
        outTris.GetData(newTris);

        Mesh result = new Mesh();
        result.vertices = newVerts;
        result.triangles = newTris;
        result.RecalculateNormals();
        result.RecalculateTangents();

        vertBuffer.Release();
        triBuffer.Release();
        outVerts.Release();
        outTris.Release();
        outCount.Release();

        return result;
    }
}
#endregion

#region AssetUtilityWindow (主力化向け: 品質検証 / BeforeAfter / Demo / UI強化)
// =================================================
public class AssetUtilityWindow : EditorWindow
{
    private enum ScanMode { AllScenes, ActiveScene, SingleObject }
    private enum ViewMode { All, Texture, Material, Polygon }

    private ScanMode currentMode = ScanMode.ActiveScene;
    private ViewMode currentViewMode = ViewMode.All;
    private GameObject droppedObject;
    private Vector2 scrollPos;

    private List<AssetInfo> allAssets = new List<AssetInfo>();
    private List<AssetInfo> editableAssets = new List<AssetInfo>();
    private List<string> allSceneNames = new List<string>();
    private HashSet<string> objectTypeSet = new HashSet<string>();
    private HashSet<string> textureTypeSet = new HashSet<string>();
    private HashSet<string> materialTypeSet = new HashSet<string>();
    private string objectTypeFilter = "すべて";
    private string textureTypeFilter = "すべて";
    private string materialTypeFilter = "すべて";
    private bool isEditing = false;
    private bool applyScheduled = false;
    private bool suppressScanOnce = false;

    private bool useGPUOptimization = true;

    // GPU前提でスムーズに動かすため、既定ではQEMを切り、GPU簡略化を優先します。
    // QEM自体は既存機能として残します。必要な場合だけONにしてください。
    private bool useQEM = false;
    private bool preserveBorders = true, preserveUVSeams = true, preserveHardNormals = true, preventNonManifold = true;
    private bool snapToLocalSurface = true;
    private float maxPosErr = 0f;
    private float maxNormalDev = 45f;
    private float minTriArea = 1e-10f;
    private float uvWeight = 0.25f, normalWeight = 0.5f;
    private float edgeLenClamp = 2.0f;
    private int subdivideSteps = 0;
    private int maxIters = 30000;

    // 既存QEMの精度パラメータ。新しい機能ではなく、既存簡略化の品質を安定させるために使います。
    private float sliverAspectMin = 0.02f;
    private float curvatureWeight = 0.3f;
    private bool compactOnFinish = true;
    private bool recomputeQuadricsLocally = false;

    private bool foldShapeOptions = false;

    [MenuItem("Tools/Asset Utility")]
    public static void ShowWindow()
    {
        AssetUtilityWindow win = GetWindow<AssetUtilityWindow>("Asset Utility");
        win.suppressScanOnce = false;
    }

    private void OnEnable()
    {
        if (suppressScanOnce) { suppressScanOnce = false; return; }
        RefreshSceneList();
        ExecuteScan();
    }

    private void OnGUI()
    {
        DrawTopButtons();
        DrawViewMode();
        DrawFooter();
        DrawStaticUI();
    }

    private void DrawTopButtons()
    {
        GUILayout.Space(4);
        GUILayout.BeginHorizontal("box");

        if (GUILayout.Button("再読み込み (UI)", "Button", GUILayout.Height(20)))
        {
            ExecuteScan();
            Repaint();
        }
        if (GUILayout.Toggle(currentMode == ScanMode.AllScenes, "すべてのシーン", "Button"))
        {
            if (currentMode != ScanMode.AllScenes) { currentMode = ScanMode.AllScenes; ExecuteScan(); }
        }
        if (GUILayout.Toggle(currentMode == ScanMode.ActiveScene, "開いているシーン", "Button"))
        {
            if (currentMode != ScanMode.ActiveScene) { currentMode = ScanMode.ActiveScene; ExecuteScan(); }
        }
        if (GUILayout.Toggle(currentMode == ScanMode.SingleObject, "オブジェクト単体", "Button"))
        {
            if (currentMode != ScanMode.SingleObject) { currentMode = ScanMode.SingleObject; ExecuteScan(); }
        }

        GUILayout.EndHorizontal();
    }

    private void DrawViewMode()
    {
        GUILayout.Space(4);
        GUILayout.BeginHorizontal("box");

        if (GUILayout.Toggle(currentViewMode == ViewMode.All, "すべて", "Button")) { currentViewMode = ViewMode.All; Repaint(); }
        if (GUILayout.Toggle(currentViewMode == ViewMode.Texture, "テクスチャー", "Button")) { currentViewMode = ViewMode.Texture; Repaint(); }
        if (GUILayout.Toggle(currentViewMode == ViewMode.Material, "マテリアル", "Button")) { currentViewMode = ViewMode.Material; Repaint(); }
        if (GUILayout.Toggle(currentViewMode == ViewMode.Polygon, "ポリゴン", "Button")) { currentViewMode = ViewMode.Polygon; Repaint(); }

        GUILayout.EndHorizontal();
    }

    private void DrawStaticUI()
    {
        GUILayout.Space(8);
        GUILayout.BeginVertical("box");

        if (currentMode == ScanMode.SingleObject)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("対象オブジェクト", EditorStyles.boldLabel);
            GameObject next = (GameObject)EditorGUILayout.ObjectField("対象オブジェクト", droppedObject, typeof(GameObject), true);
            if (next != droppedObject)
            {
                droppedObject = next;
                ExecuteScan();
            }
            GUILayout.EndVertical();
        }

        DrawSummaryLine();

        List<AssetInfo> filteredAssets = GetFilteredAssets();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        if (currentViewMode == ViewMode.All)
        {
            DrawEditableObjectUnitTable(filteredAssets);
        }
        else if (currentViewMode == ViewMode.Texture)
        {
            DrawEditableTextureTable(filteredAssets);
        }
        else if (currentViewMode == ViewMode.Material)
        {
            DrawMaterialTable(filteredAssets);
        }
        else if (currentViewMode == ViewMode.Polygon)
        {
            DrawEditablePolygonTable(filteredAssets);
        }

        EditorGUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawSummaryLine()
    {
        long currentTris = 0;
        long targetTris = 0;
        float currentVram = 0f;
        float targetVram = 0f;

        for (int i = 0; i < allAssets.Count; i++)
        {
            currentTris += allAssets[i].polygonCount;
            currentVram += allAssets[i].vramMB;
        }
        for (int i = 0; i < editableAssets.Count; i++)
        {
            targetTris += editableAssets[i].polygonCount;
            Vector2Int r = editableAssets[i].resolution;
            targetVram += r.x > 0 && r.y > 0 ? (r.x * r.y * 4f) / (1024f * 1024f) : 0f;
        }

        GUILayout.BeginHorizontal("box");
        GUILayout.Label("Objects: " + allAssets.Count, EditorStyles.boldLabel);
        GUILayout.Label("Poly: " + currentTris + " → " + targetTris, EditorStyles.boldLabel);
        GUILayout.Label("Texture VRAM: " + currentVram.ToString("F2") + " → " + targetVram.ToString("F2") + " MB", EditorStyles.boldLabel);
        GUILayout.EndHorizontal();
    }

    private List<AssetInfo> GetFilteredAssets()
    {
        return editableAssets.Where(a =>
            (objectTypeFilter == "すべて" || a.objectType == objectTypeFilter) &&
            (textureTypeFilter == "すべて" || a.textureType == textureTypeFilter) &&
            (materialTypeFilter == "すべて" || a.materialType == materialTypeFilter)
        ).ToList();
    }

    private void DrawEditableObjectUnitTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("オブジェクト", GUILayout.Width(180));
        DrawObjectTypePopup(130);
        GUILayout.Label("現在Poly", GUILayout.Width(90));
        GUILayout.Label("目標Poly", GUILayout.Width(90));
        GUILayout.Label("削減率", GUILayout.Width(70));
        GUILayout.Label("Texture", GUILayout.Width(160));
        GUILayout.Label("現在解像度", GUILayout.Width(120));
        GUILayout.Label("目標解像度", GUILayout.Width(180));
        GUILayout.Label("Material", GUILayout.Width(170));
        GUILayout.Label("警告", GUILayout.Width(240));
        GUILayout.EndHorizontal();

        for (int i = 0; i < filteredAssets.Count; i++)
        {
            AssetInfo asset = filteredAssets[i];
            GUILayout.BeginHorizontal();

            GUILayout.Label(asset.name, GUILayout.Width(180));
            GUILayout.Label(asset.objectType, GUILayout.Width(130));
            GUILayout.Label(GetOriginalPolygon(asset).ToString(), GUILayout.Width(90));
            DrawPolygonField(asset, 90);
            GUILayout.Label(ReductionText(asset), GUILayout.Width(70));
            GUILayout.Label(asset.textureName, GUILayout.Width(160));
            GUILayout.Label(ResolutionText(GetOriginalResolution(asset)), GUILayout.Width(120));
            DrawResolutionFields(asset, 180);
            GUILayout.Label(asset.materialType, GUILayout.Width(170));
            GUILayout.Label(BuildAssetWarning(asset), GUILayout.Width(240));

            GUILayout.EndHorizontal();
        }
    }

    private void DrawEditableTextureTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("オブジェクト", GUILayout.Width(180));
        GUILayout.Label("テクスチャー名", GUILayout.Width(160));
        DrawTextureTypePopup(130);
        GUILayout.Label("現在解像度", GUILayout.Width(120));
        GUILayout.Label("目標解像度", GUILayout.Width(180));
        GUILayout.Label("現在VRAM", GUILayout.Width(90));
        GUILayout.Label("目標VRAM", GUILayout.Width(90));
        GUILayout.EndHorizontal();

        for (int i = 0; i < filteredAssets.Count; i++)
        {
            AssetInfo asset = filteredAssets[i];
            if (string.IsNullOrEmpty(asset.texturePath)) continue;

            GUILayout.BeginHorizontal();
            GUILayout.Label(asset.name, GUILayout.Width(180));
            GUILayout.Label(asset.textureName, GUILayout.Width(160));
            GUILayout.Label(asset.textureType, GUILayout.Width(130));
            GUILayout.Label(ResolutionText(GetOriginalResolution(asset)), GUILayout.Width(120));
            DrawResolutionFields(asset, 180);
            GUILayout.Label(GetOriginalVram(asset).ToString("F2") + " MB", GUILayout.Width(90));
            GUILayout.Label(EstimateVram(asset.resolution).ToString("F2") + " MB", GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawMaterialTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("オブジェクト", GUILayout.Width(180));
        GUILayout.Label("マテリアル名", GUILayout.Width(180));
        DrawMaterialTypePopup(180);
        GUILayout.Label("シーン", GUILayout.Width(160));
        GUILayout.EndHorizontal();

        for (int i = 0; i < filteredAssets.Count; i++)
        {
            AssetInfo asset = filteredAssets[i];
            GUILayout.BeginHorizontal();
            GUILayout.Label(asset.name, GUILayout.Width(180));
            GUILayout.Label(asset.materialType, GUILayout.Width(180));
            GUILayout.Label(asset.materialType, GUILayout.Width(180));
            GUILayout.Label(asset.scene, GUILayout.Width(160));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawEditablePolygonTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("オブジェクト", GUILayout.Width(200));
        DrawObjectTypePopup(130);
        GUILayout.Label("現在ポリゴン", GUILayout.Width(110));
        GUILayout.Label("目標ポリゴン", GUILayout.Width(110));
        GUILayout.Label("削減率", GUILayout.Width(70));
        GUILayout.Label("警告", GUILayout.Width(220));
        GUILayout.Label("Mesh Path", GUILayout.Width(320));
        GUILayout.EndHorizontal();

        for (int i = 0; i < filteredAssets.Count; i++)
        {
            AssetInfo asset = filteredAssets[i];
            if (asset.polygonCount <= 0 && string.IsNullOrEmpty(asset.meshPath)) continue;

            GUILayout.BeginHorizontal();
            GUILayout.Label(asset.name, GUILayout.Width(200));
            GUILayout.Label(asset.objectType, GUILayout.Width(130));
            GUILayout.Label(GetOriginalPolygon(asset).ToString(), GUILayout.Width(110));
            DrawPolygonField(asset, 110);
            GUILayout.Label(ReductionText(asset), GUILayout.Width(70));
            GUILayout.Label(BuildAssetWarning(asset), GUILayout.Width(220));
            GUILayout.Label(asset.meshPath, GUILayout.Width(320));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawPolygonField(AssetInfo asset, int width)
    {
        bool editable = asset.canEditMesh && !string.IsNullOrEmpty(asset.meshPath) && GetOriginalPolygon(asset) > 0;
        using (new EditorGUI.DisabledScope(!editable))
        {
            int current = Mathf.Max(0, asset.polygonCount);
            int max = Mathf.Max(1, GetOriginalPolygon(asset));
            int next = EditorGUILayout.IntField(current, GUILayout.Width(width));
            next = Mathf.Clamp(next, 1, max);
            if (next != current)
            {
                AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
                Undo.RecordObject(data, "Polygon編集");
                asset.polygonCount = next;
                asset.targetPolygonCount = next;
                RegisterMeshEdit(data, asset, next);
                data.SaveChangesToDisk();
                isEditing = true;
            }
        }
    }

    private void DrawResolutionFields(AssetInfo asset, int width)
    {
        if (string.IsNullOrEmpty(asset.texturePath))
        {
            GUILayout.Label("—", GUILayout.Width(width));
            return;
        }

        GUILayout.BeginHorizontal(GUILayout.Width(width));
        int w = Mathf.Max(1, asset.resolution.x);
        int h = Mathf.Max(1, asset.resolution.y);
        int newW = EditorGUILayout.IntField(w, GUILayout.Width(80));
        int newH = EditorGUILayout.IntField(h, GUILayout.Width(80));
        GUILayout.EndHorizontal();

        newW = ClampTextureDimension(newW);
        newH = ClampTextureDimension(newH);

        if (newW != w || newH != h)
        {
            AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
            Undo.RecordObject(data, "解像度編集");
            asset.resolution = new Vector2Int(newW, newH);
            RegisterTextureEdit(data, asset, new Vector2Int(newW, newH));
            data.SaveChangesToDisk();
            isEditing = true;
        }
    }

    private void DrawObjectTypePopup(int width)
    {
        string[] objectTypes = (new[] { "すべて" }).Concat(objectTypeSet.OrderBy(x => x)).ToArray();
        int idx = System.Array.IndexOf(objectTypes, objectTypeFilter);
        if (idx < 0) idx = 0;
        objectTypeFilter = objectTypes[EditorGUILayout.Popup(idx, objectTypes, EditorStyles.toolbarPopup, GUILayout.Width(width))];
    }

    private void DrawTextureTypePopup(int width)
    {
        string[] textureTypes = (new[] { "すべて" }).Concat(textureTypeSet.OrderBy(x => x)).ToArray();
        int idx = System.Array.IndexOf(textureTypes, textureTypeFilter);
        if (idx < 0) idx = 0;
        textureTypeFilter = textureTypes[EditorGUILayout.Popup(idx, textureTypes, EditorStyles.toolbarPopup, GUILayout.Width(width))];
    }

    private void DrawMaterialTypePopup(int width)
    {
        string[] materialTypes = (new[] { "すべて" }).Concat(materialTypeSet.OrderBy(x => x)).ToArray();
        int idx = System.Array.IndexOf(materialTypes, materialTypeFilter);
        if (idx < 0) idx = 0;
        materialTypeFilter = materialTypes[EditorGUILayout.Popup(idx, materialTypes, EditorStyles.toolbarPopup, GUILayout.Width(width))];
    }

    private void DrawFooter()
    {
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical("box", GUILayout.Width(420));

        foldShapeOptions = EditorGUILayout.Foldout(foldShapeOptions, "形状保護オプション（QEM）");
        if (!foldShapeOptions)
        {
            useQEM = GUILayout.Toggle(useQEM, "高精度QEM（形状保護）");
            GUILayout.Label("詳細設定を表示するには見出しをクリック", EditorStyles.miniLabel);
        }
        else
        {
            useQEM = GUILayout.Toggle(useQEM, "高精度QEM（形状保護）");
            preserveBorders = GUILayout.Toggle(preserveBorders, "境界保持");
            preserveUVSeams = GUILayout.Toggle(preserveUVSeams, "UVシーム保持");
            preserveHardNormals = GUILayout.Toggle(preserveHardNormals, "ハード法線保持");
            preventNonManifold = GUILayout.Toggle(preventNonManifold, "非多様体の防止");
            snapToLocalSurface = GUILayout.Toggle(snapToLocalSurface, "新頂点を近傍面へ再投影（崩れ抑制）");

            GUILayout.Label(string.Format("Max Normal Deviation: {0:F0}°", maxNormalDev));
            maxNormalDev = Mathf.Clamp(EditorGUILayout.Slider(maxNormalDev, 5f, 180f), 5f, 180f);

            GUILayout.Label(string.Format("Min Triangle Area: {0:E2}", minTriArea));
            minTriArea = Mathf.Clamp(EditorGUILayout.FloatField(minTriArea), 1e-12f, 1e-4f);

            GUILayout.Label(string.Format("UV Weight: {0:F2}", uvWeight));
            uvWeight = Mathf.Clamp01(EditorGUILayout.Slider(uvWeight, 0f, 1f));
            GUILayout.Label(string.Format("Normal Weight: {0:F2}", normalWeight));
            normalWeight = Mathf.Clamp01(EditorGUILayout.Slider(normalWeight, 0f, 1f));

            GUILayout.Label(string.Format("Edge Length Clamp (×): {0:F2}", edgeLenClamp));
            edgeLenClamp = Mathf.Max(0f, EditorGUILayout.FloatField(edgeLenClamp));

            GUILayout.Label(string.Format("Max Position Error (local units, 0=無制限): {0:F6}", maxPosErr));
            maxPosErr = Mathf.Max(0f, EditorGUILayout.FloatField(maxPosErr));

            GUILayout.Label("Subdivide Steps (before simplify): " + subdivideSteps);
            subdivideSteps = Mathf.Clamp(EditorGUILayout.IntField(subdivideSteps, GUILayout.Width(60)), 0, 3);

            GUILayout.Label("Max Iterations per Step: " + maxIters);
            maxIters = Mathf.Clamp(EditorGUILayout.IntField(maxIters, GUILayout.Width(80)), 1000, 50000);

            GUILayout.Space(6);
            useGPUOptimization = GUILayout.Toggle(useGPUOptimization, "GPU簡略化を優先する（推奨）");
        }

        GUILayout.EndVertical();

        GUILayout.Space(8);

        GUILayout.BeginVertical();
        GUI.enabled = isEditing;
        if (GUILayout.Button("適応する", GUILayout.Width(180), GUILayout.Height(28)))
        {
            ScheduleApplyAssetChanges();
        }
        GUI.enabled = true;

        if (GUILayout.Button("変更破棄", GUILayout.Width(180)))
        {
            AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
            data.ClearTemporaryData();
            data.SaveChangesToDisk();
            editableAssets = allAssets.Select(x => x.Clone()).ToList();
            isEditing = false;
            Debug.Log("変更を破棄");
        }

        if (GUILayout.Button("ポリゴン数 50% 削減", GUILayout.Width(180)))
        {
            AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
            Undo.RecordObject(data, "一括ポリゴン変更");

            for (int i = 0; i < editableAssets.Count; i++)
            {
                AssetInfo asset = editableAssets[i];
                if (!asset.canEditMesh || string.IsNullOrEmpty(asset.meshPath) || asset.polygonCount <= 1) continue;
                int reduced = Mathf.Max(1, asset.polygonCount / 2);
                if (asset.polygonCount != reduced)
                {
                    asset.polygonCount = reduced;
                    asset.targetPolygonCount = reduced;
                    RegisterMeshEdit(data, asset, reduced);
                    isEditing = true;
                }
            }
            data.SaveChangesToDisk();
            Repaint();
        }

        if (GUILayout.Button("Before/After保存", GUILayout.Width(180)))
        {
            WriteBeforeAfterReport(allAssets.Select(x => x.Clone()).ToList(), editableAssets.Select(x => x.Clone()).ToList(), allAssets, new List<string>(), true);
        }

        if (GUILayout.Button("検証Demo作成", GUILayout.Width(180)))
        {
            CreateValidationDemoScene();
        }

        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void ScheduleApplyAssetChanges()
    {
        // OnGUI中にSceneを開く/保存する処理を直接実行すると、GUILayoutのBegin/End状態が壊れることがあります。
        // そのため、Applyは必ずIMGUIイベント終了後に遅延実行します。
        if (applyScheduled) return;

        string preview = BuildApplyPreviewText();
        if (!EditorUtility.DisplayDialog("AssetUtility Apply Preview", preview, "適用する", "キャンセル"))
        {
            return;
        }

        applyScheduled = true;

        EditorApplication.delayCall += () =>
        {
            applyScheduled = false;
            try
            {
                ApplyAssetChanges();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[AssetUtility] 適用中に例外が発生しました。変更は途中で停止しました。\n" + ex);
            }
        };
    }

    private void ApplyAssetChanges()
    {
        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
        List<string> diffLog = new List<string>();
        List<AssetInfo> beforeAssets = allAssets.Select(x => x.Clone()).ToList();
        List<AssetInfo> plannedAssets = editableAssets.Select(x => x.Clone()).ToList();

        // Importerの再インポートやScene切り替えでAssetUtilityData参照が破棄されることがあるため、
        // 各処理内では編集リストのコピーを使います。
        ApplyTextureChanges(data, diffLog);
        ApplyMeshChanges(data, diffLog);

        AssetUtilityData freshData = AssetUtilityData.LoadOrCreateData();
        if (freshData != null)
        {
            freshData.SaveChangesToDisk();
        }

        isEditing = false;

        EditorApplication.delayCall += () =>
        {
            ExecuteScan();
            WriteBeforeAfterReport(beforeAssets, plannedAssets, allAssets, diffLog, false);
            Repaint();
            Debug.Log("✅ 変更の適用が完了しました");
            for (int i = 0; i < diffLog.Count; i++) Debug.Log(diffLog[i]);
        };
    }

    private void ApplyTextureChanges(AssetUtilityData data, List<string> diffLog)
    {
        List<TextureEditData> textureEdits = data != null ? data.textureEditDataList.ToList() : new List<TextureEditData>();
        for (int i = 0; i < textureEdits.Count; i++)
        {
            TextureEditData texEdit = textureEdits[i];
            if (texEdit == null || string.IsNullOrEmpty(texEdit.texturePath)) continue;

            TextureImporter importer = AssetImporter.GetAtPath(texEdit.texturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("⚠️ TextureImporter が取得できません: " + texEdit.texturePath);
                continue;
            }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int before = importer.maxTextureSize;
            int targetMax = ToImporterMaxSize(Mathf.Max(Mathf.RoundToInt(texEdit.editedSize.x), Mathf.RoundToInt(texEdit.editedSize.y)));
            if (targetMax <= 0) targetMax = before;

            if (before != targetMax)
            {
                importer.maxTextureSize = targetMax;
                importer.SaveAndReimport();
            }
            sw.Stop();
            texEdit.processTimeMS = sw.Elapsed.TotalMilliseconds;

            diffLog.Add(string.Format("🟢 Texture: {0}: maxSize {1} → {2}", Path.GetFileName(texEdit.texturePath), before, targetMax));
        }
    }

    private void ApplyMeshChanges(AssetUtilityData data, List<string> diffLog)
    {
        List<MeshEditData> meshEdits = data != null ? data.meshEditDataList.ToList() : new List<MeshEditData>();
        if (meshEdits.Count == 0) return;

        // 同じMesh / 同じ目標値を複数Objectが使っている場合、簡略化を1回だけ行って使い回します。
        // これにより、特にデモのように同一Meshを複数Objectが参照している場合のApplyがかなり軽くなります。
        Dictionary<string, Mesh> optimizedMeshCache = new Dictionary<string, Mesh>();
        Dictionary<string, string> optimizedPathCache = new Dictionary<string, string>();

        try
        {
            for (int i = 0; i < meshEdits.Count; i++)
            {
                MeshEditData meshEdit = meshEdits[i];
                if (meshEdit == null || string.IsNullOrEmpty(meshEdit.assetPath)) continue;

                EditorUtility.DisplayProgressBar(
                    "AssetUtility",
                    string.Format("GPU前提でメッシュを最適化中... {0}/{1}", i + 1, meshEdits.Count),
                    meshEdits.Count > 0 ? (i / (float)meshEdits.Count) : 0f);

                Mesh original = AssetDatabase.LoadAssetAtPath<Mesh>(meshEdit.assetPath);
                if (original == null) continue;

                int originalTris = SafeTriangleCount(original);
                int target = Mathf.Clamp(meshEdit.targetTriangleCount, 1, Mathf.Max(1, originalTris));
                if (target >= originalTris)
                {
                    Debug.Log(string.Format("⏩ {0}: 変更なし（{1} tris）", meshEdit.targetObjectName, originalTris));
                    continue;
                }

                string cacheKey = BuildOptimizationCacheKey(meshEdit.assetPath, target);
                Mesh savedMesh;
                string savedPath;

                if (!optimizedMeshCache.TryGetValue(cacheKey, out savedMesh) || savedMesh == null)
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

                    Mesh working = UnityEngine.Object.Instantiate(original);
                    if (subdivideSteps > 0)
                        working = AssetOptimizer.SubdivideLoop(working, subdivideSteps, true);

                    AssetOptimizer.SimplifyOptions opt = BuildSimplifyOptions();

                    // GPU前提: 既定ではGPUを最初に試します。
                    // QEMが明示的にONの場合だけQEMを使います。
                    Mesh modified = useQEM
                        ? AssetOptimizer.SimplifyQEM(working, target, opt)
                        : (useGPUOptimization
                            ? AssetOptimizer.Simplify(working, target)
                            : AssetOptimizer.FallbackSimplify(working, target));

                    modified = RecoverReductionIfNeeded(original, modified, target, meshEdit.targetObjectName);

                    if (!ValidateSimplifiedMesh(original, modified, target, meshEdit.targetObjectName))
                    {
                        continue;
                    }

                    string methodName = useQEM ? "shapeqempp" : (useGPUOptimization ? "gpu" : "cpu");
                    string filename = Path.GetFileNameWithoutExtension(meshEdit.assetPath) + "_" + methodName + "_" + target;
                    AssetOptimizer.SaveOptimizedMesh(modified, filename, out savedPath);
                    savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(savedPath);
                    if (savedMesh == null)
                    {
                        Debug.LogWarning("❌ 保存したメッシュを読み込めません: " + savedPath);
                        continue;
                    }

                    sw.Stop();
                    meshEdit.processTimeMS = sw.Elapsed.TotalMilliseconds;
                    optimizedMeshCache[cacheKey] = savedMesh;
                    optimizedPathCache[cacheKey] = savedPath;
                }
                else
                {
                    optimizedPathCache.TryGetValue(cacheKey, out savedPath);
                    if (string.IsNullOrEmpty(savedPath)) savedPath = AssetDatabase.GetAssetPath(savedMesh);
                }

                bool replaced = ReplaceTargetMeshReference(meshEdit, original, savedMesh);
                meshEdit.simplifiedMeshPath = savedPath;
                meshEdit.targetTriangleCount = target;

                if (replaced)
                {
                    string methodLabel = useQEM ? "Shape-QEM++" : (useGPUOptimization ? "GPU優先" : "CPU");
                    diffLog.Add(string.Format("🟢 {0}: {1} → {2} tris（{3}）", meshEdit.targetObjectName, originalTris, SafeTriangleCount(savedMesh), methodLabel));
                }
                else
                {
                    Debug.LogWarning("⚠️ 対象オブジェクトが見つからなかったため、メッシュ参照を更新できません: " + meshEdit.targetObjectName);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private string BuildOptimizationCacheKey(string meshPath, int target)
    {
        return string.Join("|", new string[]
        {
            meshPath ?? string.Empty,
            target.ToString(),
            useQEM ? "qem" : (useGPUOptimization ? "gpu" : "cpu"),
            preserveBorders.ToString(),
            preserveUVSeams.ToString(),
            preserveHardNormals.ToString(),
            preventNonManifold.ToString(),
            snapToLocalSurface.ToString(),
            maxNormalDev.ToString("F2"),
            uvWeight.ToString("F2"),
            normalWeight.ToString("F2"),
            edgeLenClamp.ToString("F2"),
            subdivideSteps.ToString()
        });
    }

    private AssetOptimizer.SimplifyOptions BuildSimplifyOptions()
    {
        AssetOptimizer.SimplifyOptions opt = new AssetOptimizer.SimplifyOptions();
        opt.preserveBorders = preserveBorders;
        opt.preserveUVSeams = preserveUVSeams;
        opt.preserveHardNormals = preserveHardNormals;
        opt.preventNonManifold = preventNonManifold;
        opt.maxPositionError = maxPosErr;
        opt.maxNormalDeviation = maxNormalDev;
        opt.minTriangleArea = minTriArea;
        opt.uvWeight = uvWeight;
        opt.normalWeight = normalWeight;
        opt.edgeLengthClamp = edgeLenClamp;
        opt.snapToLocalSurface = snapToLocalSurface;
        opt.maxIterationsPerStep = maxIters;
        opt.sliverAspectMin = sliverAspectMin;
        opt.curvatureWeight = curvatureWeight;
        opt.compactOnFinish = compactOnFinish;
        opt.recomputeQuadricsLocally = recomputeQuadricsLocally;
        return opt;
    }

    private Mesh RecoverReductionIfNeeded(Mesh original, Mesh modified, int target, string label)
    {
        int originalTris = SafeTriangleCount(original);
        int modifiedTris = SafeTriangleCount(modified);

        if (original == null || target <= 0 || target >= originalTris)
            return modified;

        // GPU結果が削減できていても、境界が増えて穴が開く結果は採用しません。
        if (IsReductionResultUsable(original, modified, target, label, "GPU/QEM result"))
            return modified;

        // GPU前提は維持しますが、GPU結果が穴を作る場合は安全側に倒します。
        // ここで三角形の単純間引きは使いません。単純間引きは孤立三角形を作り、穴の原因になります。
        Mesh cpu = AssetOptimizer.FallbackSimplify(original, target);
        if (IsReductionResultUsable(original, cpu, target, label, "CPU fallback"))
        {
            Debug.Log(string.Format("[AssetUtility] {0}: GPU結果を破棄し、穴を作らないfallbackを採用しました。{1} → {2}", label, originalTris, SafeTriangleCount(cpu)));
            return cpu;
        }

        Mesh safe = BuildTopologySafeEdgeCollapseMesh(original, target);
        if (IsReductionResultUsable(original, safe, target, label, "Topology-safe collapse"))
        {
            Debug.Log(string.Format("[AssetUtility] {0}: 穴防止Collapseで削減しました。{1} → {2}", label, originalTris, SafeTriangleCount(safe)));
            return safe;
        }

        return modified;
    }

    private bool IsReductionResultUsable(Mesh original, Mesh modified, int target, string label, string method)
    {
        if (original == null || modified == null || modified.vertexCount == 0 || modified.triangles == null || modified.triangles.Length == 0)
            return false;

        int originalTris = SafeTriangleCount(original);
        int modifiedTris = SafeTriangleCount(modified);
        if (modifiedTris <= 0 || modifiedTris >= originalTris)
            return false;

        if (modifiedTris > originalTris)
            return false;

        if (HasLikelyHoles(original, modified, label, method))
            return false;

        return true;
    }

    private bool HasLikelyHoles(Mesh original, Mesh modified, string label, string method)
    {
        int originalBoundary = CountBoundaryEdges(original);
        int modifiedBoundary = CountBoundaryEdges(modified);

        // 境界数が大きく増えるのは、内部に穴が開いた典型パターンです。
        // 平面グリッドの場合、正しい削減なら外周境界はほぼ維持されますが、
        // 三角形間引きでは内部エッジが大量に境界化します。
        int tolerance = Mathf.Max(16, Mathf.CeilToInt(originalBoundary * 0.25f));
        if (modifiedBoundary > originalBoundary + tolerance)
        {
            Debug.LogWarning(string.Format(
                "⚠️ {0}: {1} の結果は境界エッジが増えすぎているため破棄しました。Boundary {2} → {3}。穴あき防止のため元メッシュまたは安全fallbackを使います。",
                label, method, originalBoundary, modifiedBoundary));
            return true;
        }

        return false;
    }

    private int CountBoundaryEdges(Mesh mesh)
    {
        if (mesh == null || mesh.triangles == null) return 0;
        int[] tris = mesh.triangles;
        Dictionary<string, int> edgeCount = new Dictionary<string, int>();

        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            AddBoundaryEdge(edgeCount, tris[i], tris[i + 1]);
            AddBoundaryEdge(edgeCount, tris[i + 1], tris[i + 2]);
            AddBoundaryEdge(edgeCount, tris[i + 2], tris[i]);
        }

        int count = 0;
        foreach (KeyValuePair<string, int> kv in edgeCount)
        {
            if (kv.Value == 1) count++;
        }
        return count;
    }

    private void AddBoundaryEdge(Dictionary<string, int> edgeCount, int a, int b)
    {
        int x = Mathf.Min(a, b);
        int y = Mathf.Max(a, b);
        string key = x.ToString() + ":" + y.ToString();
        int value;
        if (!edgeCount.TryGetValue(key, out value)) edgeCount[key] = 1;
        else edgeCount[key] = value + 1;
    }

    private struct CollapseEdge
    {
        public int a;
        public int b;
        public float lengthSqr;
    }

    private Mesh BuildTopologySafeEdgeCollapseMesh(Mesh source, int targetTriangles)
    {
        if (source == null) return null;
        if (source.subMeshCount > 1)
        {
            Debug.Log(string.Format("[AssetUtility] {0}: 複数SubMeshのため穴防止Collapseはスキップしました。", source.name));
            return null;
        }

        int[] sourceTris = source.triangles;
        Vector3[] sourceVerts = source.vertices;
        if (sourceTris == null || sourceTris.Length < 3 || sourceVerts == null || sourceVerts.Length == 0)
            return null;

        int sourceTriCount = sourceTris.Length / 3;
        targetTriangles = Mathf.Clamp(targetTriangles, 1, sourceTriCount - 1);
        if (targetTriangles <= 0 || targetTriangles >= sourceTriCount) return null;

        List<Vector3> verts = new List<Vector3>(sourceVerts);
        List<int> tris = new List<int>(sourceTris);
        List<Vector2> uvs = new List<Vector2>(source.uv != null && source.uv.Length == sourceVerts.Length ? source.uv : new Vector2[sourceVerts.Length]);
        List<Vector3> normals = new List<Vector3>(source.normals != null && source.normals.Length == sourceVerts.Length ? source.normals : new Vector3[sourceVerts.Length]);
        List<Vector4> tangents = new List<Vector4>(source.tangents != null && source.tangents.Length == sourceVerts.Length ? source.tangents : new Vector4[sourceVerts.Length]);
        List<Color> colors = source.colors != null && source.colors.Length == sourceVerts.Length ? new List<Color>(source.colors) : null;
        List<BoneWeight> boneWeights = source.boneWeights != null && source.boneWeights.Length == sourceVerts.Length ? new List<BoneWeight>(source.boneWeights) : null;

        int safety = Mathf.Max(1000, sourceTriCount * 6);
        int lastTriCount = TriCount(tris);
        int stuckCount = 0;

        while (TriCount(tris) > targetTriangles && safety-- > 0)
        {
            Dictionary<string, int> edgeUse = BuildEdgeUseMap(tris);
            List<CollapseEdge> candidates = BuildCollapseCandidates(edgeUse, verts, preserveBorders: true);
            if (candidates.Count == 0) break;

            candidates.Sort((l, r) => l.lengthSqr.CompareTo(r.lengthSqr));

            bool collapsed = false;
            int candidateLimit = Mathf.Min(candidates.Count, 256);
            for (int i = 0; i < candidateLimit; i++)
            {
                CollapseEdge e = candidates[i];
                Vector3 newPos = 0.5f * (verts[e.a] + verts[e.b]);

                if (WouldCollapseCreateInvalidTriangles(e.a, e.b, newPos, tris, verts))
                    continue;

                CollapseVertexPair(e.a, e.b, newPos, verts, tris, uvs, normals, tangents, colors, boneWeights);
                RemoveDegenerateTriangles(tris, verts);
                collapsed = true;
                break;
            }

            if (!collapsed)
                break;

            int currentTriCount = TriCount(tris);
            if (currentTriCount == lastTriCount)
            {
                stuckCount++;
                if (stuckCount > 32) break;
            }
            else
            {
                stuckCount = 0;
                lastTriCount = currentTriCount;
            }
        }

        if (TriCount(tris) >= sourceTriCount || TriCount(tris) <= 0)
            return null;

        Mesh compact = BuildCompactMeshFromWorkingData(source, verts, tris, uvs, normals, tangents, colors, boneWeights);
        compact.name = source.name + "_HoleSafe_" + TriCount(tris);
        return compact;
    }

    private int TriCount(List<int> tris)
    {
        return tris != null ? tris.Count / 3 : 0;
    }

    private Dictionary<string, int> BuildEdgeUseMap(List<int> tris)
    {
        Dictionary<string, int> edgeUse = new Dictionary<string, int>();
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            AddEdgeUse(edgeUse, tris[i], tris[i + 1]);
            AddEdgeUse(edgeUse, tris[i + 1], tris[i + 2]);
            AddEdgeUse(edgeUse, tris[i + 2], tris[i]);
        }
        return edgeUse;
    }

    private void AddEdgeUse(Dictionary<string, int> edgeUse, int a, int b)
    {
        int x = Mathf.Min(a, b);
        int y = Mathf.Max(a, b);
        string key = x.ToString() + ":" + y.ToString();
        int count;
        if (!edgeUse.TryGetValue(key, out count)) edgeUse[key] = 1;
        else edgeUse[key] = count + 1;
    }

    private List<CollapseEdge> BuildCollapseCandidates(Dictionary<string, int> edgeUse, List<Vector3> verts, bool preserveBorders)
    {
        List<CollapseEdge> edges = new List<CollapseEdge>();
        foreach (KeyValuePair<string, int> kv in edgeUse)
        {
            if (preserveBorders && kv.Value == 1)
                continue;
            if (kv.Value > 2)
                continue;

            string[] parts = kv.Key.Split(':');
            if (parts.Length != 2) continue;
            int a, b;
            if (!int.TryParse(parts[0], out a) || !int.TryParse(parts[1], out b)) continue;
            if (a < 0 || b < 0 || a >= verts.Count || b >= verts.Count || a == b) continue;

            CollapseEdge e = new CollapseEdge();
            e.a = a;
            e.b = b;
            e.lengthSqr = (verts[a] - verts[b]).sqrMagnitude;
            edges.Add(e);
        }
        return edges;
    }

    private bool WouldCollapseCreateInvalidTriangles(int keep, int remove, Vector3 newPos, List<int> tris, List<Vector3> verts)
    {
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];

            bool affected = a == keep || b == keep || c == keep || a == remove || b == remove || c == remove;
            if (!affected) continue;

            int na = a == remove ? keep : a;
            int nb = b == remove ? keep : b;
            int nc = c == remove ? keep : c;

            if (na == nb || nb == nc || nc == na)
                continue; // Collapse対象エッジに接する面は消えるので正常。

            Vector3 pa = na == keep ? newPos : verts[na];
            Vector3 pb = nb == keep ? newPos : verts[nb];
            Vector3 pc = nc == keep ? newPos : verts[nc];

            float area = Vector3.Cross(pb - pa, pc - pa).magnitude * 0.5f;
            if (area < 1e-12f)
                return true;

            Vector3 oldNormal = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).normalized;
            Vector3 newNormal = Vector3.Cross(pb - pa, pc - pa).normalized;
            if (oldNormal.sqrMagnitude > 1e-12f && newNormal.sqrMagnitude > 1e-12f)
            {
                if (Vector3.Dot(oldNormal, newNormal) < 0.15f)
                    return true;
            }
        }
        return false;
    }

    private void CollapseVertexPair(
        int keep,
        int remove,
        Vector3 newPos,
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Color> colors,
        List<BoneWeight> boneWeights)
    {
        verts[keep] = newPos;
        if (uvs != null && keep < uvs.Count && remove < uvs.Count) uvs[keep] = 0.5f * (uvs[keep] + uvs[remove]);
        if (normals != null && keep < normals.Count && remove < normals.Count) normals[keep] = (normals[keep] + normals[remove]).normalized;
        if (tangents != null && keep < tangents.Count && remove < tangents.Count) tangents[keep] = 0.5f * (tangents[keep] + tangents[remove]);
        if (colors != null && keep < colors.Count && remove < colors.Count) colors[keep] = 0.5f * (colors[keep] + colors[remove]);
        if (boneWeights != null && keep < boneWeights.Count && remove < boneWeights.Count) boneWeights[keep] = BlendBoneWeightsSimple(boneWeights[keep], boneWeights[remove]);

        for (int i = 0; i < tris.Count; i++)
        {
            if (tris[i] == remove)
                tris[i] = keep;
        }
    }

    private BoneWeight BlendBoneWeightsSimple(BoneWeight a, BoneWeight b)
    {
        Dictionary<int, float> map = new Dictionary<int, float>();
        AddBoneWeight(map, a.boneIndex0, a.weight0 * 0.5f);
        AddBoneWeight(map, a.boneIndex1, a.weight1 * 0.5f);
        AddBoneWeight(map, a.boneIndex2, a.weight2 * 0.5f);
        AddBoneWeight(map, a.boneIndex3, a.weight3 * 0.5f);
        AddBoneWeight(map, b.boneIndex0, b.weight0 * 0.5f);
        AddBoneWeight(map, b.boneIndex1, b.weight1 * 0.5f);
        AddBoneWeight(map, b.boneIndex2, b.weight2 * 0.5f);
        AddBoneWeight(map, b.boneIndex3, b.weight3 * 0.5f);

        List<KeyValuePair<int, float>> list = new List<KeyValuePair<int, float>>(map);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        float sum = 0f;
        for (int i = 0; i < Mathf.Min(4, list.Count); i++) sum += list[i].Value;
        if (sum <= 1e-20f) sum = 1f;

        BoneWeight r = new BoneWeight();
        if (list.Count > 0) { r.boneIndex0 = list[0].Key; r.weight0 = list[0].Value / sum; }
        if (list.Count > 1) { r.boneIndex1 = list[1].Key; r.weight1 = list[1].Value / sum; }
        if (list.Count > 2) { r.boneIndex2 = list[2].Key; r.weight2 = list[2].Value / sum; }
        if (list.Count > 3) { r.boneIndex3 = list[3].Key; r.weight3 = list[3].Value / sum; }
        return r;
    }

    private void AddBoneWeight(Dictionary<int, float> map, int index, float weight)
    {
        if (weight <= 0f) return;
        float current;
        if (!map.TryGetValue(index, out current)) map[index] = weight;
        else map[index] = current + weight;
    }

    private void RemoveDegenerateTriangles(List<int> tris, List<Vector3> verts)
    {
        for (int i = tris.Count - 3; i >= 0; i -= 3)
        {
            int a = tris[i];
            int b = tris[i + 1];
            int c = tris[i + 2];
            if (a == b || b == c || c == a || a < 0 || b < 0 || c < 0 || a >= verts.Count || b >= verts.Count || c >= verts.Count)
            {
                tris.RemoveRange(i, 3);
                continue;
            }

            float area = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).magnitude * 0.5f;
            if (area < 1e-12f)
                tris.RemoveRange(i, 3);
        }
    }

    private Mesh BuildCompactMeshFromWorkingData(
        Mesh source,
        List<Vector3> verts,
        List<int> tris,
        List<Vector2> uvs,
        List<Vector3> normals,
        List<Vector4> tangents,
        List<Color> colors,
        List<BoneWeight> boneWeights)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> outVerts = new List<Vector3>();
        List<Vector2> outUvs = new List<Vector2>();
        List<Vector3> outNormals = new List<Vector3>();
        List<Vector4> outTangents = new List<Vector4>();
        List<Color> outColors = colors != null ? new List<Color>() : null;
        List<BoneWeight> outBoneWeights = boneWeights != null ? new List<BoneWeight>() : null;
        List<int> outTris = new List<int>(tris.Count);

        for (int i = 0; i < tris.Count; i++)
        {
            int oldIndex = tris[i];
            int newIndex;
            if (!remap.TryGetValue(oldIndex, out newIndex))
            {
                newIndex = outVerts.Count;
                remap.Add(oldIndex, newIndex);
                outVerts.Add(verts[oldIndex]);
                if (uvs != null && oldIndex < uvs.Count) outUvs.Add(uvs[oldIndex]);
                if (normals != null && oldIndex < normals.Count) outNormals.Add(normals[oldIndex]);
                if (tangents != null && oldIndex < tangents.Count) outTangents.Add(tangents[oldIndex]);
                if (outColors != null && oldIndex < colors.Count) outColors.Add(colors[oldIndex]);
                if (outBoneWeights != null && oldIndex < boneWeights.Count) outBoneWeights.Add(boneWeights[oldIndex]);
            }
            outTris.Add(newIndex);
        }

        Mesh mesh = new Mesh();
#if UNITY_2017_3_OR_NEWER
        if (outVerts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
        mesh.SetVertices(outVerts);
        mesh.SetTriangles(outTris, 0);
        if (outUvs.Count == outVerts.Count) mesh.SetUVs(0, outUvs);
        if (outColors != null && outColors.Count == outVerts.Count) mesh.SetColors(outColors);
        if (outNormals.Count == outVerts.Count) mesh.SetNormals(outNormals); else mesh.RecalculateNormals();
        if (outTangents.Count == outVerts.Count) mesh.SetTangents(outTangents); else { try { mesh.RecalculateTangents(); } catch { } }
        if (outBoneWeights != null && outBoneWeights.Count == outVerts.Count)
        {
            mesh.boneWeights = outBoneWeights.ToArray();
            if (source.bindposes != null && source.bindposes.Length > 0) mesh.bindposes = source.bindposes;
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private bool ValidateSimplifiedMesh(Mesh original, Mesh modified, int target, string label)
    {
        if (modified == null || modified.vertexCount == 0 || modified.triangles == null || modified.triangles.Length == 0)
        {
            Debug.LogWarning(string.Format("❌ {0}: メッシュの簡略化に失敗しました。元の状態を維持します。", label));
            return false;
        }
        int originalTris = SafeTriangleCount(original);
        int modifiedTris = SafeTriangleCount(modified);
        if (modifiedTris <= 0)
        {
            Debug.LogWarning(string.Format("❌ {0}: 簡略化後の三角形数が0です。", label));
            return false;
        }
        if (modifiedTris > originalTris)
        {
            Debug.LogWarning(string.Format("❌ {0}: 簡略化後の三角形数が元より増えました。{1} → {2}", label, originalTris, modifiedTris));
            return false;
        }
        if (target < originalTris && modifiedTris == originalTris)
        {
            Debug.Log(string.Format("[AssetUtility] {0}: 削減結果が元メッシュと同じだったため適用しません。{1} → {2}", label, originalTris, modifiedTris));
            return false;
        }
        if (HasLikelyHoles(original, modified, label, "final validation"))
        {
            Debug.LogWarning(string.Format("❌ {0}: 穴が発生する可能性が高いため、この削減結果は適用しません。", label));
            return false;
        }
        if (target < originalTris && modifiedTris > Mathf.CeilToInt(target * 1.25f))
        {
            Debug.LogWarning(string.Format("⚠️ {0}: 目標値から大きく外れています。target={1}, result={2}。品質優先で適用は継続します。", label, target, modifiedTris));
        }
        return true;
    }

    private bool ReplaceTargetMeshReference(MeshEditData meshEdit, Mesh original, Mesh savedMesh)
    {
        bool replaced = false;
        if (!string.IsNullOrEmpty(meshEdit.targetScenePath))
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(meshEdit.targetScenePath, OpenSceneMode.Single);
                replaced = ReplaceTargetMeshReferenceInScene(scene, meshEdit, original, savedMesh);
                if (replaced) EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }
        else
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (ReplaceTargetMeshReferenceInScene(scene, meshEdit, original, savedMesh)) replaced = true;
            }
        }
        return replaced;
    }

    private bool ReplaceTargetMeshReferenceInScene(Scene scene, MeshEditData meshEdit, Mesh original, Mesh savedMesh)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;

        GameObject target = FindObjectInScene(scene, meshEdit.targetObjectPath, meshEdit.targetObjectName, meshEdit.targetInstanceID);
        if (target == null) return false;

        bool replaced = false;
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh == original)
        {
            Undo.RecordObject(mf, "AssetUtility Mesh Replace");
            mf.sharedMesh = savedMesh;
            EditorUtility.SetDirty(mf);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
            replaced = true;
        }

        // SkinnedMeshRendererは元から対応していたため維持。ただし、編集登録は基本的に静的Meshを優先します。
        SkinnedMeshRenderer smr = target.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.sharedMesh == original)
        {
            Undo.RecordObject(smr, "AssetUtility Skinned Mesh Replace");
            smr.sharedMesh = savedMesh;
            EditorUtility.SetDirty(smr);
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
            replaced = true;
        }

        MeshCollider col = target.GetComponent<MeshCollider>();
        if (col != null && col.sharedMesh == original)
        {
            Undo.RecordObject(col, "AssetUtility MeshCollider Replace");
            col.sharedMesh = savedMesh;
            EditorUtility.SetDirty(col);
            PrefabUtility.RecordPrefabInstancePropertyModifications(col);
        }

        if (replaced)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
        return replaced;
    }


    private string BuildApplyPreviewText()
    {
        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
        int meshCount = data != null ? data.meshEditDataList.Count : 0;
        int textureCount = data != null ? data.textureEditDataList.Count : 0;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("これから以下の変更を適用します。");
        sb.AppendLine();
        sb.AppendLine("Mesh変更: " + meshCount);
        sb.AppendLine("Texture変更: " + textureCount);
        sb.AppendLine();

        if (data != null)
        {
            int shown = 0;
            for (int i = 0; i < data.meshEditDataList.Count && shown < 8; i++, shown++)
            {
                MeshEditData m = data.meshEditDataList[i];
                sb.AppendLine("- Mesh: " + m.targetObjectName + " → " + m.targetTriangleCount + " tris");
            }
            for (int i = 0; i < data.textureEditDataList.Count && shown < 14; i++, shown++)
            {
                TextureEditData t = data.textureEditDataList[i];
                sb.AppendLine("- Texture: " + Path.GetFileName(t.texturePath) + " → " + Vector2Int.RoundToInt(t.editedSize));
            }
        }

        sb.AppendLine();
        sb.AppendLine("品質検証で穴・退化面・空Meshが疑われる結果は適用しません。");
        return sb.ToString();
    }

    private string ReductionText(AssetInfo asset)
    {
        int original = Mathf.Max(0, GetOriginalPolygon(asset));
        int target = Mathf.Max(0, asset.polygonCount);
        if (original <= 0 || target <= 0) return "—";
        float rate = 100f * (1f - (target / (float)original));
        return Mathf.Clamp(rate, 0f, 100f).ToString("F0") + "%";
    }

    private string BuildAssetWarning(AssetInfo asset)
    {
        List<string> warnings = new List<string>();
        if (asset == null) return string.Empty;
        if (!asset.canEditMesh && asset.polygonCount > 0) warnings.Add("静的Mesh対象外");
        if (string.IsNullOrEmpty(asset.meshPath) && asset.polygonCount > 0) warnings.Add("MeshPathなし");
        if (asset.materialType == "None") warnings.Add("Materialなし");
        if (asset.textureName == "None") warnings.Add("Textureなし");
        int original = GetOriginalPolygon(asset);
        if (original > 0 && asset.polygonCount < Mathf.CeilToInt(original * 0.2f)) warnings.Add("削減率が高い");
        return string.Join("; ", warnings.ToArray());
    }

    private void WriteBeforeAfterReport(List<AssetInfo> beforeAssets, List<AssetInfo> plannedAssets, List<AssetInfo> afterAssets, List<string> diffLog, bool reveal)
    {
        string folder = "Assets/AssetUtilityReports";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string mdPath = folder + "/AssetUtility_BeforeAfter_" + stamp + ".md";
        string csvPath = folder + "/AssetUtility_BeforeAfter_" + stamp + ".csv";

        long beforeTris = beforeAssets.Sum(a => (long)a.polygonCount);
        long plannedTris = plannedAssets.Sum(a => (long)a.polygonCount);
        long afterTris = afterAssets.Sum(a => (long)a.polygonCount);
        float beforeVram = beforeAssets.Sum(a => a.vramMB);
        float plannedVram = plannedAssets.Sum(a => EstimateVram(a.resolution));
        float afterVram = afterAssets.Sum(a => a.vramMB);

        StringBuilder md = new StringBuilder();
        md.AppendLine("# AssetUtility Before / After Report");
        md.AppendLine();
        md.AppendLine("- Generated: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        md.AppendLine("- Objects: " + beforeAssets.Count);
        md.AppendLine("- Triangles: " + beforeTris + " → planned " + plannedTris + " → after " + afterTris);
        md.AppendLine("- Texture VRAM: " + beforeVram.ToString("F2") + " MB → planned " + plannedVram.ToString("F2") + " MB → after " + afterVram.ToString("F2") + " MB");
        md.AppendLine();
        md.AppendLine("## Changed Objects");
        md.AppendLine();
        md.AppendLine("| Object | Scene | Poly Before | Poly Planned | Poly After | Texture Before | Texture Planned | Texture After | Warning |");
        md.AppendLine("|---|---|---:|---:|---:|---|---|---|---|");

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("object,scene,polyBefore,polyPlanned,polyAfter,textureBefore,texturePlanned,textureAfter,warning");

        for (int i = 0; i < plannedAssets.Count; i++)
        {
            AssetInfo planned = plannedAssets[i];
            AssetInfo before = beforeAssets.FirstOrDefault(a => a.scenePath == planned.scenePath && a.objectPath == planned.objectPath);
            AssetInfo after = afterAssets.FirstOrDefault(a => a.scenePath == planned.scenePath && a.objectPath == planned.objectPath);
            if (before == null) before = planned;
            if (after == null) after = planned;

            bool changed = before.polygonCount != planned.polygonCount || before.resolution != planned.resolution || before.polygonCount != after.polygonCount || before.resolution != after.resolution;
            if (!changed) continue;

            string warning = BuildAssetWarning(planned);
            md.AppendLine("| " + Md(planned.name) + " | " + Md(planned.scene) + " | " + before.polygonCount + " | " + planned.polygonCount + " | " + after.polygonCount + " | " + Md(ResolutionText(before.resolution)) + " | " + Md(ResolutionText(planned.resolution)) + " | " + Md(ResolutionText(after.resolution)) + " | " + Md(warning) + " |");
            csv.AppendLine(string.Join(",", new string[] { Csv(planned.name), Csv(planned.scene), before.polygonCount.ToString(), planned.polygonCount.ToString(), after.polygonCount.ToString(), Csv(ResolutionText(before.resolution)), Csv(ResolutionText(planned.resolution)), Csv(ResolutionText(after.resolution)), Csv(warning) }));
        }

        if (diffLog != null && diffLog.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Apply Log");
            md.AppendLine();
            for (int i = 0; i < diffLog.Count; i++) md.AppendLine("- " + diffLog[i]);
        }

        File.WriteAllText(mdPath, md.ToString(), new UTF8Encoding(false));
        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log("[AssetUtility] Before/After report: " + mdPath);
        if (reveal) EditorUtility.RevealInFinder(mdPath);
    }

    private string Csv(string value)
    {
        value = value ?? string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string Md(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        return value.Replace("|", "\\|").Replace("\n", " ");
    }

    private void CreateValidationDemoScene()
    {
        string root = "Assets/Samples/AssetUtilityValidation";
        string sceneFolder = root + "/Scenes";
        string meshFolder = root + "/Meshes";
        string matFolder = root + "/Materials";
        string texFolder = root + "/Textures";
        Directory.CreateDirectory(sceneFolder);
        Directory.CreateDirectory(meshFolder);
        Directory.CreateDirectory(matFolder);
        Directory.CreateDirectory(texFolder);

        Texture2D tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
        for (int y = 0; y < tex.height; y++)
        {
            for (int x = 0; x < tex.width; x++)
            {
                bool on = ((x / 64) + (y / 64)) % 2 == 0;
                tex.SetPixel(x, y, on ? Color.white : Color.magenta);
            }
        }
        tex.Apply();
        string texPath = texFolder + "/AU_Validation_Checker.png";
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(texPath);
        Texture2D importedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        Material mat = new Material(Shader.Find("Standard"));
        mat.name = "AU_Validation_Material";
        mat.mainTexture = importedTex;
        string matPath = matFolder + "/AU_Validation_Material.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(mat, matPath);

        Mesh grid = CreateValidationGridMesh(80, 80, 6f);
        string meshPath = meshFolder + "/AU_Validation_HighPolyGrid.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(grid, meshPath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject camera = new GameObject("Main Camera");
        camera.tag = "MainCamera";
        Camera cam = camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 5f, -8f);
        camera.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;

        GameObject light = new GameObject("Directional Light");
        Light l = light.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject a = new GameObject("AU_HighPoly_StaticMesh");
        a.transform.position = new Vector3(-2f, 0f, 0f);
        a.AddComponent<MeshFilter>().sharedMesh = grid;
        a.AddComponent<MeshRenderer>().sharedMaterial = mat;

        GameObject b = new GameObject("AU_MeshCollider_SharedMesh");
        b.transform.position = new Vector3(2f, 0f, 0f);
        b.AddComponent<MeshFilter>().sharedMesh = grid;
        b.AddComponent<MeshRenderer>().sharedMaterial = mat;
        b.AddComponent<MeshCollider>().sharedMesh = grid;

        string scenePath = sceneFolder + "/AssetUtilityValidationScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("AssetUtility", "検証Demo Sceneを作成しました。\n" + scenePath, "OK");
    }

    private Mesh CreateValidationGridMesh(int xSegments, int zSegments, float size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "AU_Validation_HighPolyGrid";
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();
        for (int z = 0; z <= zSegments; z++)
        {
            for (int x = 0; x <= xSegments; x++)
            {
                float px = ((float)x / xSegments - 0.5f) * size;
                float pz = ((float)z / zSegments - 0.5f) * size;
                verts.Add(new Vector3(px, 0f, pz));
                uvs.Add(new Vector2((float)x / xSegments, (float)z / zSegments));
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
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        try { mesh.RecalculateTangents(); } catch { }
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (isEditing)
        {
            bool save = EditorUtility.DisplayDialog("保存されていない変更があります",
                "変更を保存してから閉じますか？", "保存", "破棄");
            if (save) ScheduleApplyAssetChanges();
            else Debug.Log("⚠️ 変更は保存されませんでした");
        }
    }

    private void ExecuteScan()
    {
        allAssets.Clear();
        objectTypeSet.Clear();
        textureTypeSet.Clear();
        materialTypeSet.Clear();

        if (currentMode == ScanMode.AllScenes)
        {
            ScanAllScenes();
        }
        else if (currentMode == ScanMode.ActiveScene)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.name))
            {
                Debug.LogError("[ExecuteScan] 無効なシーン名です");
                return;
            }
            ScanSceneAppend(activeScene);
        }
        else if (currentMode == ScanMode.SingleObject)
        {
            if (droppedObject != null)
            {
                TraverseAndCollect(droppedObject);
            }
        }

        editableAssets = allAssets.Select(asset => asset.Clone()).ToList();
        ApplySavedEditsToEditableAssets();

        Debug.Log("📊 スキャン完了: " + allAssets.Count + " 件");
    }

    private void ScanAllScenes()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[AssetUtility] AllScenes scan をキャンセルしました。");
            return;
        }

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");

        try
        {
            for (int gi = 0; gi < sceneGuids.Length; gi++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuids[gi]);
                if (string.IsNullOrEmpty(path)) continue;
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (scene.IsValid() && scene.isLoaded)
                {
                    ScanSceneAppend(scene);
                }
            }
        }
        finally
        {
            if (previousSetup != null && previousSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    private void ApplySavedEditsToEditableAssets()
    {
        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();

        for (int i = 0; i < data.meshEditDataList.Count; i++)
        {
            MeshEditData edit = data.meshEditDataList[i];
            AssetInfo editable = editableAssets.FirstOrDefault(a => IsSameMeshTarget(a, edit));
            if (editable != null)
            {
                editable.polygonCount = edit.targetTriangleCount;
                editable.targetPolygonCount = edit.targetTriangleCount;
            }
        }

        for (int i = 0; i < data.textureEditDataList.Count; i++)
        {
            TextureEditData tex = data.textureEditDataList[i];
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.texturePath == tex.texturePath);
            if (editable != null)
            {
                editable.resolution = Vector2Int.RoundToInt(tex.editedSize);
                editable.targetResolution = editable.resolution;
                editable.vramMB = EstimateVram(editable.resolution);
            }
        }
    }

    private void ScanSceneAppend(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
        {
            Debug.LogError("[ScanScene] 無効なシーン名");
            return;
        }
        foreach (GameObject rootObj in scene.GetRootGameObjects())
            TraverseAndCollect(rootObj);
    }

    private void TraverseAndCollect(GameObject obj)
    {
        CollectAsset(obj);
        foreach (Transform child in obj.transform)
            TraverseAndCollect(child.gameObject);
    }

    private void CollectAsset(GameObject go)
    {
        if (go == null) return;

        string objectType = GetObjectType(go);
        string textureType = GetTextureType(go);
        string materialType = GetMaterialType(go);

        objectTypeSet.Add(objectType);
        textureTypeSet.Add(textureType);
        materialTypeSet.Add(materialType);

        MeshFilter mf = go.GetComponent<MeshFilter>();
        SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
        Mesh mesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);

        Renderer renderer = (Renderer)go.GetComponent<SkinnedMeshRenderer>() ?? go.GetComponent<MeshRenderer>();
        Material mat = renderer != null ? renderer.sharedMaterial : null;
        Texture tex = mat != null ? mat.mainTexture : null;

        long mem = 0;
        if (mesh != null) mem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
        if (mat != null) mem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mat);
        if (tex != null) mem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
        int memMB = Mathf.CeilToInt(mem / (1024f * 1024f));

        int polyCount = SafeTriangleCount(mesh);
        string texturePath = tex != null ? AssetDatabase.GetAssetPath(tex) : string.Empty;
        string meshPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;

        AssetInfo info = new AssetInfo();
        info.instanceID = go.GetInstanceID();
        info.name = go.name;
        info.scene = go.scene.name;
        info.scenePath = go.scene.path;
        info.objectPath = GetHierarchyPath(go.transform);
        info.objectType = objectType;
        info.memoryMB = memMB;
        info.materialType = mat != null && mat.shader != null ? mat.shader.name : "None";
        info.polygonCount = polyCount;
        info.targetPolygonCount = polyCount;
        info.meshPath = meshPath;
        info.canEditMesh = mf != null && mesh != null && !string.IsNullOrEmpty(meshPath);
        info.textureName = tex != null ? tex.name : "None";
        info.textureType = tex != null ? tex.GetType().Name : "Unknown";
        info.texturePath = texturePath;
        info.vramMB = tex != null ? (tex.width * tex.height * 4f) / (1024f * 1024f) : 0f;
        info.resolution = tex != null ? new Vector2Int(tex.width, tex.height) : Vector2Int.zero;
        info.targetResolution = info.resolution;

        allAssets.Add(info);
    }

    private void RegisterMeshEdit(AssetUtilityData data, AssetInfo asset, int targetTriangleCount)
    {
        MeshEditData meshEdit = data.meshEditDataList.FirstOrDefault(m => IsSameMeshTarget(asset, m));
        if (meshEdit == null)
        {
            meshEdit = new MeshEditData();
            meshEdit.assetPath = asset.meshPath;
            meshEdit.targetObjectName = asset.name;
            meshEdit.targetInstanceID = asset.instanceID;
            meshEdit.targetObjectPath = asset.objectPath;
            meshEdit.targetScenePath = asset.scenePath;
            data.meshEditDataList.Add(meshEdit);
        }
        meshEdit.assetPath = asset.meshPath;
        meshEdit.targetObjectName = asset.name;
        meshEdit.targetInstanceID = asset.instanceID;
        meshEdit.targetObjectPath = asset.objectPath;
        meshEdit.targetScenePath = asset.scenePath;
        meshEdit.targetTriangleCount = targetTriangleCount;
        EditorUtility.SetDirty(data);
    }

    private void RegisterTextureEdit(AssetUtilityData data, AssetInfo asset, Vector2Int targetResolution)
    {
        TextureEditData existing = data.textureEditDataList.FirstOrDefault(t => t.texturePath == asset.texturePath);
        if (existing == null)
        {
            existing = new TextureEditData();
            existing.texturePath = asset.texturePath;
            existing.originalSize = GetOriginalResolution(asset);
            data.textureEditDataList.Add(existing);
        }
        existing.editedSize = targetResolution;
        EditorUtility.SetDirty(data);
    }

    private bool IsSameMeshTarget(AssetInfo asset, MeshEditData edit)
    {
        if (asset == null || edit == null) return false;
        if (!string.IsNullOrEmpty(edit.targetScenePath) && asset.scenePath != edit.targetScenePath) return false;
        if (!string.IsNullOrEmpty(edit.targetObjectPath) && asset.objectPath != edit.targetObjectPath) return false;
        if (!string.IsNullOrEmpty(edit.assetPath) && asset.meshPath != edit.assetPath) return false;
        if (edit.targetInstanceID != 0 && asset.instanceID == edit.targetInstanceID) return true;
        return asset.name == edit.targetObjectName && asset.meshPath == edit.assetPath;
    }

    private AssetInfo GetOriginalAsset(AssetInfo editable)
    {
        AssetInfo original = allAssets.FirstOrDefault(a => a.instanceID == editable.instanceID && a.objectPath == editable.objectPath && a.scenePath == editable.scenePath);
        if (original == null) original = allAssets.FirstOrDefault(a => a.name == editable.name && a.scene == editable.scene && a.meshPath == editable.meshPath);
        return original;
    }

    private int GetOriginalPolygon(AssetInfo editable)
    {
        AssetInfo original = GetOriginalAsset(editable);
        return original != null ? original.polygonCount : editable.polygonCount;
    }

    private Vector2Int GetOriginalResolution(AssetInfo editable)
    {
        AssetInfo original = GetOriginalAsset(editable);
        return original != null ? original.resolution : editable.resolution;
    }

    private float GetOriginalVram(AssetInfo editable)
    {
        AssetInfo original = GetOriginalAsset(editable);
        return original != null ? original.vramMB : editable.vramMB;
    }

    private void RefreshSceneList()
    {
        allSceneNames.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Scene");
        for (int gi = 0; gi < guids.Length; gi++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[gi]);
            allSceneNames.Add(Path.GetFileNameWithoutExtension(path));
        }
    }

    private string GetObjectType(GameObject go)
    {
        if (go.GetComponent<SkinnedMeshRenderer>()) return "SkinnedMesh";
        if (go.GetComponent<MeshRenderer>()) return "Mesh";
        if (go.GetComponent<CanvasRenderer>()) return "UI";
        if (go.GetComponent<Light>()) return "Light";
        if (go.GetComponent<Camera>()) return "Camera";
        return "Other";
    }

    private string GetTextureType(GameObject go)
    {
        Renderer renderer = (Renderer)go.GetComponent<SkinnedMeshRenderer>() ?? go.GetComponent<MeshRenderer>();
        if (renderer == null) return "None";
        Material mat = renderer.sharedMaterial;
        if (mat == null) return "None";
        Texture tex = mat.mainTexture;
        if (tex == null) return "None";
        if (tex is Texture2D) return "Texture2D";
        if (tex is RenderTexture) return "RenderTexture";
        if (tex is Cubemap) return "Cubemap";
        if (tex is Texture3D) return "Texture3D";
        return tex.GetType().Name;
    }

    private string GetMaterialType(GameObject go)
    {
        Renderer renderer = (Renderer)go.GetComponent<SkinnedMeshRenderer>() ?? go.GetComponent<MeshRenderer>();
        if (renderer == null || renderer.sharedMaterial == null) return "None";
        string shaderName = renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : null;
        if (shaderName == null) return "None";
        if (shaderName.Contains("Standard")) return "Standard";
        if (shaderName.Contains("Universal")) return "URP";
        if (shaderName.Contains("HDRP")) return "HDRP";
        if (shaderName.Contains("Legacy")) return "Legacy";
        return shaderName;
    }

    private int SafeTriangleCount(Mesh mesh)
    {
        try { return mesh != null && mesh.triangles != null ? mesh.triangles.Length / 3 : 0; }
        catch { return 0; }
    }

    private string ResolutionText(Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0) return "—";
        return size.x + " x " + size.y;
    }

    private float EstimateVram(Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0) return 0f;
        return (size.x * size.y * 4f) / (1024f * 1024f);
    }

    private int ClampTextureDimension(int v)
    {
        return Mathf.Clamp(v, 1, 8192);
    }

    private int ToImporterMaxSize(int v)
    {
        v = Mathf.Clamp(v, 32, 8192);
        int size = 32;
        while (size < v && size < 8192) size *= 2;
        return Mathf.Clamp(size, 32, 8192);
    }

    private GameObject FindObjectInScene(Scene scene, string objectPath, string objectName, int instanceID)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                GameObject go = children[i].gameObject;
                if (!string.IsNullOrEmpty(objectPath) && GetHierarchyPath(children[i]) == objectPath) return go;
                if (instanceID != 0 && go.GetInstanceID() == instanceID) return go;
                if (go.name == objectName) return go;
            }
        }
        return null;
    }

    private string GetHierarchyPath(Transform transform)
    {
        if (transform == null) return string.Empty;
        Stack<string> names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", names.ToArray());
    }
}
#endregion
