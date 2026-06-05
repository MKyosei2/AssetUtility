#if UNITY_EDITOR
using UnityEngine;

namespace EasyTool
{
    public static class MeshValidationService
    {
        public static bool IsValidMesh(Mesh mesh, out string reason)
        {
            if (mesh == null)
            {
                reason = "Mesh is null.";
                return false;
            }

            if (mesh.vertexCount <= 0)
            {
                reason = "Mesh has no vertices.";
                return false;
            }

            int[] triangles = mesh.triangles;
            if (triangles == null || triangles.Length < 3 || triangles.Length % 3 != 0)
            {
                reason = "Mesh triangle index buffer is invalid.";
                return false;
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] >= mesh.vertexCount)
                {
                    reason = "Mesh has out-of-range triangle index at " + i + ".";
                    return false;
                }
            }

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                {
                    reason = "Mesh contains non-finite vertex at " + i + ".";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
#endif
