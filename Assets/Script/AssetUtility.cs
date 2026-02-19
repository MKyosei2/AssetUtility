// ===== AssetUtility.cs (Reinforced: Shape-QEM++, BoneWeights, Compact, Scale-Normalize, Sliver-Guard) =====
// Place under: Assets/EasyTool/AssetUtility.cs

using EasyTool;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public string objectType;
        public int memoryMB;
        public string materialType;
        public int polygonCount;
        public string textureName;
        public string textureType;
        public float vramMB;
        public Vector2Int resolution;

        public AssetInfo Clone()
        {
            return new AssetInfo
            {
                instanceID = instanceID,
                name = name,
                scene = scene,
                objectType = objectType,
                memoryMB = memoryMB,
                materialType = materialType,
                polygonCount = polygonCount,
                textureName = textureName,
                textureType = textureType,
                vramMB = vramMB,
                resolution = resolution
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
        public int targetInstanceID;
        public int targetTriangleCount;
        public double processTimeMS;
    }

    public class AssetUtilityData : ScriptableObject
    {
        public List<MaterialEditData> materialEdits = new List<MaterialEditData>();
        public List<TextureEditData> textureEditDataList = new List<TextureEditData>();
        public List<MeshEditData> meshEditDataList = new List<MeshEditData>();

        public void ClearTemporaryData()
        {
            textureEditDataList.Clear();
            meshEditDataList.Clear();
            Debug.Log("[AssetUtilityData] 一時編集データをクリアしました");
        }

        public void RestoreTemporaryData()
        {
            Debug.Log("[AssetUtilityData] 一時編集データを復元しました（現状では何もしません）");
        }

        public static AssetUtilityData LoadOrCreateData()
        {
            string path = "Assets/Editor/Resources/AssetUtilityData.asset";
            var data = AssetDatabase.LoadAssetAtPath<AssetUtilityData>(path);
            if (data == null)
            {
                data = CreateInstance<AssetUtilityData>();
                if (!Directory.Exists("Assets/Editor/Resources"))
                    Directory.CreateDirectory("Assets/Editor/Resources");
                AssetDatabase.CreateAsset(data, path);
                AssetDatabase.SaveAssets();
            }
            return data;
        }

        public void SaveChangesToDisk()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log("📦 AssetUtilityData を保存しました");
        }
    }
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
        foreach (Edge k in edgeCount.Keys)
        {
            int a = k.a, b0 = k.b;
            if ((uvs[a] - uvs[b0]).sqrMagnitude > 1e-8f) isUVSeam.Add(k);
        }

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

#region AssetUtilityWindow (UIは既存踏襲 / 内部強化版を呼び出し)
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
    private bool suppressScanOnce = false;

    private bool useGPUOptimization = false;

    private bool useQEM = true;
    private bool preserveBorders = true, preserveUVSeams = true, preserveHardNormals = true, preventNonManifold = true;
    private bool snapToLocalSurface = true;
    private float maxPosErr = 0f;
    private float maxNormalDev = 45f;
    private float minTriArea = 1e-10f;
    private float uvWeight = 0.25f, normalWeight = 0.5f;
    private float edgeLenClamp = 2.0f;
    private int subdivideSteps = 0;
    private int maxIters = 30000;

    // 追加パラメータ（UIには出さず内部で使用/既定値は SimplifyOptions.Default）
    private float sliverAspectMin = 0.02f;
    private float curvatureWeight = 0.3f;
    private bool compactOnFinish = true;
    private bool recomputeQuadricsLocally = false;

    // 折りたたみ（既定閉）
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
            currentMode = ScanMode.AllScenes; Repaint();
        }
        if (GUILayout.Toggle(currentMode == ScanMode.ActiveScene, "開いているシーン", "Button"))
        {
            currentMode = ScanMode.ActiveScene; Repaint();
        }
        if (GUILayout.Toggle(currentMode == ScanMode.SingleObject, "オブジェクト単体", "Button"))
        {
            currentMode = ScanMode.SingleObject; Repaint();
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

        List<AssetInfo> filteredAssets = allAssets.Where(a =>
            (objectTypeFilter == "すべて" || a.objectType == objectTypeFilter) &&
            (textureTypeFilter == "すべて" || a.textureType == textureTypeFilter) &&
            (materialTypeFilter == "すべて" || a.materialType == materialTypeFilter)
        ).ToList();

        if (currentMode == ScanMode.ActiveScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.name))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("シーン名: " + scene.name, EditorStyles.boldLabel);
                GUILayout.Label("シーン合計ストレージ: " + filteredAssets.Sum(a => a.memoryMB) + " MB", EditorStyles.boldLabel);
                GUILayout.EndHorizontal();

                if (currentViewMode == ViewMode.All)
                {
                    DrawTableWithHeaderFilters(new[] { "オブジェクト名", "オブジェクトタイプ", "メモリ容量" },
                        filteredAssets.Select(a => new[] { a.name, a.objectType, a.memoryMB + " MB" }).ToList(), objectTypeCol: 1);
                }
                else if (currentViewMode == ViewMode.Texture)
                {
                    DrawEditableTextureTable(filteredAssets);
                }
                else if (currentViewMode == ViewMode.Material)
                {
                    DrawTableWithHeaderFilters(new[] { "マテリアル名", "マテリアルタイプ" },
                        filteredAssets.Select(a => new[] { a.materialType, a.materialType }).ToList(), materialTypeCol: 1);
                }
                else if (currentViewMode == ViewMode.Polygon)
                {
                    DrawEditablePolygonTable(filteredAssets);
                }
            }
            else GUILayout.Label("開いているシーンのデータが取得できません", EditorStyles.helpBox);
        }

        if (currentMode == ScanMode.AllScenes)
        {
            if (currentViewMode == ViewMode.All)
            {
                DrawTableWithHeaderFilters(new[] { "オブジェクト名", "オブジェクトタイプ", "メモリ容量" },
                    filteredAssets.Select(a => new[] { a.name, a.objectType, a.memoryMB + " MB" }).ToList(), objectTypeCol: 1);
            }
            else if (currentViewMode == ViewMode.Texture) DrawEditableTextureTable(filteredAssets);
            else if (currentViewMode == ViewMode.Material)
            {
                DrawTableWithHeaderFilters(new[] { "マテリアル名", "マテリアルタイプ" },
                    filteredAssets.Select(a => new[] { a.materialType, a.materialType }).ToList(), materialTypeCol: 1);
            }
            else if (currentViewMode == ViewMode.Polygon) DrawEditablePolygonTable(filteredAssets);
        }

        if (currentMode == ScanMode.SingleObject)
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical("box");

            GUILayout.Label("対象オブジェクト", EditorStyles.boldLabel);
            droppedObject = (GameObject)EditorGUILayout.ObjectField("対象オブジェクト", droppedObject, typeof(GameObject), true);

            if (droppedObject != null)
            {
                MeshFilter mf = droppedObject.GetComponent<MeshFilter>();
                SkinnedMeshRenderer smr = droppedObject.GetComponent<SkinnedMeshRenderer>();
                Mesh mesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);

                if (mesh == null || mesh.triangles == null || mesh.triangles.Length == 0)
                {
                    EditorGUILayout.HelpBox("このオブジェクトには有効なメッシュが存在しないため、最適化できません。", MessageType.Warning);
                }
                else if (currentViewMode == ViewMode.Polygon)
                {
                    int currentCount = mesh.triangles.Length / 3;
                    GUILayout.Label("現在のポリゴン数: " + currentCount);
                    int targetCount = EditorGUILayout.IntField("目標ポリゴン数", currentCount);

                    useQEM = GUILayout.Toggle(useQEM, "高精度QEM（形状保護）");
                    useGPUOptimization = GUILayout.Toggle(useGPUOptimization, "QEMを使わない時はGPU最適化");

                    if (GUILayout.Button("このメッシュを最適化適用"))
                    {
                        Mesh working = Object.Instantiate(mesh);
                        if (subdivideSteps > 0)
                            working = AssetOptimizer.SubdivideLoop(working, subdivideSteps, true);

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

                        // 強化項目
                        opt.sliverAspectMin = sliverAspectMin;
                        opt.curvatureWeight = curvatureWeight;
                        opt.compactOnFinish = compactOnFinish;
                        opt.recomputeQuadricsLocally = recomputeQuadricsLocally;

                        Mesh optimized = useQEM
                            ? AssetOptimizer.SimplifyQEM(working, targetCount, opt)
                            : (useGPUOptimization
                                ? AssetOptimizer.Simplify(working, targetCount)
                                : AssetOptimizer.FallbackSimplify(working, targetCount));

                        AssetOptimizer.SaveOptimizedMesh(optimized, droppedObject.name + "_Optimized", out string path);

                        if (mf != null) { mf.sharedMesh = optimized; EditorUtility.SetDirty(mf); PrefabUtility.RecordPrefabInstancePropertyModifications(mf); }
                        if (smr != null) { smr.sharedMesh = optimized; EditorUtility.SetDirty(smr); PrefabUtility.RecordPrefabInstancePropertyModifications(smr); }

                        MeshCollider col = droppedObject.GetComponent<MeshCollider>();
                        if (col != null) { col.sharedMesh = optimized; EditorUtility.SetDirty(col); }

                        EditorSceneManager.MarkSceneDirty(droppedObject.scene);

                        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
                        data.meshEditDataList.RemoveAll(e => e.targetInstanceID == droppedObject.GetInstanceID());
                        Mesh srcMesh = mesh;
                        string srcPath = AssetDatabase.GetAssetPath(srcMesh);

                        MeshEditData m = new MeshEditData();
                        m.assetPath = srcPath;
                        m.simplifiedMeshPath = path;
                        m.targetObjectName = droppedObject.name;
                        m.targetInstanceID = droppedObject.GetInstanceID();
                        m.targetTriangleCount = targetCount;
                        data.meshEditDataList.Add(m);

                        EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssets();
                        Debug.Log("✅ " + droppedObject.name + " に最適化メッシュを適用（" + (useQEM ? "Shape-QEM++" : (useGPUOptimization ? "GPU" : "CPU")) + "）");
                    }
                }
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndVertical();
    }

    private void DrawTableWithHeaderFilters(string[] headers, List<string[]> rows, int? objectTypeCol = null, int? textureTypeCol = null, int? materialTypeCol = null)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < headers.Length; i++)
        {
            if (objectTypeCol.HasValue && i == objectTypeCol.Value)
            {
                string[] objectTypes = (new[] { "すべて" }).Concat(objectTypeSet.OrderBy(x => x)).ToArray();
                int idx = System.Array.IndexOf(objectTypes, objectTypeFilter);
                if (idx < 0) idx = 0;
                objectTypeFilter = objectTypes[EditorGUILayout.Popup(idx, objectTypes, GUILayout.Width(150))];
            }
            else if (textureTypeCol.HasValue && i == textureTypeCol.Value)
            {
                string[] textureTypes = (new[] { "すべて" }).Concat(textureTypeSet.OrderBy(x => x)).ToArray();
                int idx = System.Array.IndexOf(textureTypes, textureTypeFilter);
                if (idx < 0) idx = 0;
                textureTypeFilter = textureTypes[EditorGUILayout.Popup(idx, textureTypes, GUILayout.Width(150))];
            }
            else if (materialTypeCol.HasValue && i == materialTypeCol.Value)
            {
                string[] materialTypes = (new[] { "すべて" }).Concat(materialTypeSet.OrderBy(x => x)).ToArray();
                int idx = System.Array.IndexOf(materialTypes, materialTypeFilter);
                if (idx < 0) idx = 0;
                materialTypeFilter = materialTypes[EditorGUILayout.Popup(idx, materialTypes, GUILayout.Width(150))];
            }
            else
            {
                GUILayout.Label(headers[i], EditorStyles.boldLabel, GUILayout.Width(150));
            }
        }
        GUILayout.EndHorizontal();

        for (int r = 0; r < rows.Count; r++)
        {
            GUILayout.BeginHorizontal();
            string[] row = rows[r];
            for (int c = 0; c < row.Length; c++) GUILayout.Label(row[c], GUILayout.Width(150));
            GUILayout.EndHorizontal();
        }
    }

    private void DrawEditableTextureTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("テクスチャー名", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.Label("テクスチャータイプ", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.Label("VRAM容量", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.Label("解像度", EditorStyles.boldLabel, GUILayout.Width(200));
        GUILayout.EndHorizontal();

        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();

        for (int i = 0; i < filteredAssets.Count; i++)
        {
            AssetInfo asset = filteredAssets[i];
            GUILayout.BeginHorizontal();

            GUILayout.Label(asset.textureName, GUILayout.Width(150));
            GUILayout.Label(asset.textureType, GUILayout.Width(150));
            GUILayout.Label(string.Format("{0:F2} MB", asset.vramMB), GUILayout.Width(150));

            Vector2Int originalRes = asset.resolution;
            Vector2Int editedRes = originalRes;

            TextureEditData existing = data.textureEditDataList.FirstOrDefault(t =>
                Path.GetFileNameWithoutExtension(t.texturePath) == asset.textureName);

            if (existing != null) editedRes = Vector2Int.RoundToInt(existing.editedSize);

            int newWidth = EditorGUILayout.IntField(editedRes.x, GUILayout.Width(90));
            int newHeight = EditorGUILayout.IntField(editedRes.y, GUILayout.Width(90));

            if (newWidth != editedRes.x || newHeight != editedRes.y)
            {
                Undo.RecordObject(data, "解像度編集");
                isEditing = true;

                Texture foundTex = Resources.FindObjectsOfTypeAll<Texture>()
                    .FirstOrDefault(t => t.name == asset.textureName);

                string texPath = foundTex != null ? AssetDatabase.GetAssetPath(foundTex) : string.Empty;

                if (existing != null)
                {
                    existing.editedSize = new Vector2(newWidth, newHeight);
                }
                else
                {
                    TextureEditData ted = new TextureEditData();
                    ted.texturePath = texPath;
                    ted.originalSize = originalRes;
                    ted.editedSize = new Vector2(newWidth, newHeight);
                    data.textureEditDataList.Add(ted);
                }

                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }

            GUILayout.EndHorizontal();
        }
    }

    private void DrawEditablePolygonTable(List<AssetInfo> filteredAssets)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("オブジェクト名", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.Label("ポリゴン数", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.EndHorizontal();

        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();

        foreach (AssetInfo asset in filteredAssets)
        {
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.name == asset.name && a.scene == asset.scene);
            GUILayout.BeginHorizontal();
            GUILayout.Label(asset.name, GUILayout.Width(150));

            if (editable != null && asset.polygonCount > 0)
            {
                int poly = EditorGUILayout.IntField(editable.polygonCount, GUILayout.Width(150));
                if (poly != editable.polygonCount)
                {
                    Undo.RecordObject(data, "Polygon編集");
                    editable.polygonCount = poly;
                    isEditing = true;

                    string meshPath = AssetDatabase.GetAssetPath(
                        GameObject.Find(asset.name)?.GetComponent<MeshFilter>()?.sharedMesh
                    );

                    if (!string.IsNullOrEmpty(meshPath))
                    {
                        MeshEditData meshEdit = data.meshEditDataList.FirstOrDefault(m => m.assetPath == meshPath);
                        if (meshEdit == null)
                        {
                            MeshEditData med = new MeshEditData();
                            med.assetPath = meshPath;
                            med.targetObjectName = asset.name;
                            med.targetTriangleCount = poly;
                            data.meshEditDataList.Add(med);
                        }
                        else
                        {
                            meshEdit.targetTriangleCount = poly;
                        }

                        EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssets();
                    }
                }
            }
            else
            {
                GUILayout.Label("—（編集不可）", GUILayout.Width(150));
            }

            GUILayout.EndHorizontal();
        }
    }

    private void DrawFooter()
    {
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();

        // 左：形状保護オプション（折りたたみ）
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
            useGPUOptimization = GUILayout.Toggle(useGPUOptimization, "QEMを使わない場合のみGPU簡略化を利用");
        }

        GUILayout.EndVertical();

        GUILayout.Space(8);

        // 右：適用ボタン群
        GUILayout.BeginVertical();
        GUI.enabled = isEditing;
        if (GUILayout.Button("適応する", GUILayout.Width(180), GUILayout.Height(28)))
        {
            ApplyAssetChanges();
        }
        GUI.enabled = true;

        if (GUILayout.Button("変更破棄", GUILayout.Width(180)))
        {
            editableAssets = allAssets.Select(x => x.Clone()).ToList();
            isEditing = false;
            Debug.Log("変更を破棄");
        }

        if (GUILayout.Button("ポリゴン数 50% 削減", GUILayout.Width(180)))
        {
            AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
            foreach (AssetInfo asset in editableAssets)
            {
                int reduced = Mathf.Max(10, asset.polygonCount / 2);
                if (asset.polygonCount != reduced)
                {
                    asset.polygonCount = reduced;
                    isEditing = true;

                    GameObject go = GameObject.Find(asset.name);
                    string meshPath = go ? AssetDatabase.GetAssetPath(go.GetComponent<MeshFilter>()?.sharedMesh) : string.Empty;
                    if (!string.IsNullOrEmpty(meshPath))
                    {
                        Undo.RecordObject(data, "一括ポリゴン変更");
                        MeshEditData existing = data.meshEditDataList.FirstOrDefault(m => m.assetPath == meshPath);
                        if (existing != null) existing.targetTriangleCount = reduced;
                        else
                        {
                            MeshEditData med = new MeshEditData();
                            med.assetPath = meshPath;
                            med.targetObjectName = asset.name;
                            med.targetTriangleCount = reduced;
                            data.meshEditDataList.Add(med);
                        }
                        EditorUtility.SetDirty(data);
                    }
                }
            }
            AssetDatabase.SaveAssets();
            Repaint();
        }

        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void ApplyAssetChanges()
    {
        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();
        List<string> diffLog = new List<string>();

        foreach (MeshEditData meshEdit in data.meshEditDataList.ToList())
        {
            Mesh original = AssetDatabase.LoadAssetAtPath<Mesh>(meshEdit.assetPath);
            if (original == null) continue;

            int originalTris = original.triangles.Length / 3;
            if (meshEdit.targetTriangleCount == originalTris)
            {
                Debug.Log(string.Format("⏩ {0}: 変更なし（{1} tris）", meshEdit.targetObjectName, originalTris));
                continue;
            }

            Mesh working = Object.Instantiate(original);
            if (subdivideSteps > 0)
                working = AssetOptimizer.SubdivideLoop(working, subdivideSteps, true);

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

            // 強化項目
            opt.sliverAspectMin = sliverAspectMin;
            opt.curvatureWeight = curvatureWeight;
            opt.compactOnFinish = compactOnFinish;
            opt.recomputeQuadricsLocally = recomputeQuadricsLocally;

            Mesh modified = useQEM
                ? AssetOptimizer.SimplifyQEM(working, meshEdit.targetTriangleCount, opt)
                : (useGPUOptimization
                    ? AssetOptimizer.Simplify(working, meshEdit.targetTriangleCount)
                    : AssetOptimizer.FallbackSimplify(working, meshEdit.targetTriangleCount));

            if (modified == null || modified.vertexCount == 0 || modified.triangles.Length == 0)
            {
                Debug.LogWarning(string.Format("❌ {0}: メッシュの簡略化に失敗しました。元の状態を維持します。", meshEdit.targetObjectName));
                continue;
            }

            string filename = Path.GetFileNameWithoutExtension(meshEdit.assetPath) + "_" + (useQEM ? "shapeqempp" : (useGPUOptimization ? "gpu" : "cpu")) + "_" + meshEdit.targetTriangleCount;
            AssetOptimizer.SaveOptimizedMesh(modified, filename, out string savedPath);

            foreach (GameObject go in GameObject.FindObjectsOfType<GameObject>())
            {
                MeshFilter mf = go.GetComponent<MeshFilter>();
                SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                Mesh current = mf ? mf.sharedMesh : (smr ? smr.sharedMesh : null);
                if (current == original)
                {
                    if (mf) { mf.sharedMesh = modified; EditorUtility.SetDirty(mf); PrefabUtility.RecordPrefabInstancePropertyModifications(mf); }
                    if (smr) { smr.sharedMesh = modified; EditorUtility.SetDirty(smr); PrefabUtility.RecordPrefabInstancePropertyModifications(smr); }

                    MeshCollider col = go.GetComponent<MeshCollider>();
                    if (col) { col.sharedMesh = modified; EditorUtility.SetDirty(col); }

                    EditorSceneManager.MarkSceneDirty(go.scene);

                    if (PrefabUtility.IsPartOfPrefabInstance(go))
                    {
                        GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                        if (root != null) PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                    }

                    data.meshEditDataList.RemoveAll(m => m.targetInstanceID == go.GetInstanceID());

                    MeshEditData med = new MeshEditData();
                    med.assetPath = meshEdit.assetPath;
                    med.simplifiedMeshPath = savedPath;
                    med.targetObjectName = go.name;
                    med.targetInstanceID = go.GetInstanceID();
                    med.targetTriangleCount = meshEdit.targetTriangleCount;
                    data.meshEditDataList.Add(med);

                    diffLog.Add(string.Format("🟢 {0}: {1} → {2} tris（{3}）",
                        go.name, originalTris, meshEdit.targetTriangleCount, (useQEM ? "Shape-QEM++" : (useGPUOptimization ? "GPU" : "CPU"))));
                }
            }
        }

        data.SaveChangesToDisk();
        isEditing = false;

        EditorApplication.delayCall += () =>
        {
            ExecuteScan();
            Repaint();
            Debug.Log("✅ 変更の適用が完了しました");
            for (int i = 0; i < diffLog.Count; i++) Debug.Log(diffLog[i]);
        };
    }

    private void OnDestroy()
    {
        if (isEditing)
        {
            bool save = EditorUtility.DisplayDialog("保存されていない変更があります",
                "変更を保存してから閉じますか？", "保存", "破棄");
            if (save) ApplyAssetChanges();
            else Debug.Log("⚠️ 変更は保存されませんでした");
        }
    }

    private void ExecuteScan()
    {
        allAssets.Clear();

        if (currentMode == ScanMode.AllScenes)
        {
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            for (int gi = 0; gi < sceneGuids.Length; gi++)
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuids[gi]);
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (scene.IsValid())
                {
                    ScanScene(scene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
        else if (currentMode == ScanMode.ActiveScene)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.name))
            {
                Debug.LogError("[ExecuteScan] 無効なシーン名です");
                return;
            }
            ScanScene(activeScene);
        }

        editableAssets = allAssets.Select(asset => asset.Clone()).ToList();
        AssetUtilityData data = AssetUtilityData.LoadOrCreateData();

        foreach (MeshEditData edit in data.meshEditDataList)
        {
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.name == edit.targetObjectName);
            if (editable != null) editable.polygonCount = edit.targetTriangleCount;
        }
        foreach (TextureEditData tex in data.textureEditDataList)
        {
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.textureName == Path.GetFileNameWithoutExtension(tex.texturePath));
            if (editable != null)
            {
                editable.resolution = Vector2Int.RoundToInt(tex.editedSize);
                editable.vramMB = (tex.editedSize.x * tex.editedSize.y * 4f) / (1024f * 1024f);
            }
        }

        editableAssets = allAssets.Select(asset => asset.Clone()).ToList();

        foreach (MeshEditData edit in data.meshEditDataList)
        {
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.name == edit.targetObjectName && a.instanceID == edit.targetInstanceID);
            if (editable != null) editable.polygonCount = edit.targetTriangleCount;
        }
        foreach (TextureEditData tex in data.textureEditDataList)
        {
            AssetInfo editable = editableAssets.FirstOrDefault(a => a.textureName == Path.GetFileNameWithoutExtension(tex.texturePath));
            if (editable != null)
            {
                editable.resolution = Vector2Int.RoundToInt(tex.editedSize);
                editable.vramMB = (tex.editedSize.x * tex.editedSize.y * 4f) / (1024f * 1024f);
            }
        }

        Debug.Log("📊 スキャン完了: " + allAssets.Count + " 件");
    }

    private void ScanScene(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name))
        {
            Debug.LogError("[ScanScene] 無効なシーン名");
            return;
        }
        allAssets.Clear();
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

        int polyCount = mesh != null ? mesh.triangles.Length / 3 : 0;

        if (mesh != null && (mf != null || smr != null))
        {
            AssetUtilityData savedData = AssetUtilityData.LoadOrCreateData();
            MeshEditData edit = savedData.meshEditDataList
                .FirstOrDefault(m => m.targetObjectName == go.name && !string.IsNullOrEmpty(m.simplifiedMeshPath));
            if (edit != null)
            {
                Mesh simplified = AssetDatabase.LoadAssetAtPath<Mesh>(edit.simplifiedMeshPath);
                if (simplified != null)
                {
                    if (mf && mf.sharedMesh != simplified)
                    {
                        mf.sharedMesh = simplified;
                        EditorUtility.SetDirty(mf);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                    if (smr && smr.sharedMesh != simplified)
                    {
                        smr.sharedMesh = simplified;
                        EditorUtility.SetDirty(smr);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                    MeshCollider col = go.GetComponent<MeshCollider>();
                    if (col && col.sharedMesh != simplified)
                    {
                        col.sharedMesh = simplified;
                        EditorUtility.SetDirty(col);
                    }

                    GameObject prefab = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                    if (prefab != null) PrefabUtility.ApplyPrefabInstance(prefab, InteractionMode.AutomatedAction);

                    polyCount = simplified.triangles.Length / 3;
                }
            }
        }

        AssetInfo info = new AssetInfo();
        info.name = go.name;
        info.scene = go.scene.name;
        info.objectType = objectType;
        info.memoryMB = memMB;
        info.materialType = mat != null ? mat.shader.name : "None";
        info.polygonCount = polyCount;
        info.textureName = tex != null ? tex.name : "None";
        info.textureType = tex != null ? tex.GetType().Name : "Unknown";
        info.vramMB = tex != null ? (tex.width * tex.height * 4f) / (1024f * 1024f) : 0f;
        info.resolution = tex != null ? new Vector2Int(tex.width, tex.height) : Vector2Int.zero;
        info.instanceID = go.GetInstanceID();
        allAssets.Add(info);
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
        return "Custom";
    }
}
#endregion
