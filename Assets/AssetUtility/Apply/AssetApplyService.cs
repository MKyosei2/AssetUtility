#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace EasyTool
{
    public static class AssetApplyService
    {
        public static void ApplyMeshPlan(MeshOptimizationPlan plan, bool dryRun = true)
        {
            if (plan == null) return;

            if (!plan.IsValid(out string reason))
            {
                Debug.LogWarning("Cannot apply mesh plan: " + reason);
                return;
            }

            if (dryRun)
            {
                Debug.Log("Dry-run: Skipping actual mesh apply for " + plan.targetObjectPath);
                return;
            }

            GameObject target = GameObject.Find(plan.targetObjectPath);
            if (target == null)
            {
                Debug.LogWarning("Target object not found: " + plan.targetObjectPath);
                return;
            }

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null && plan.updateMeshFilter)
            {
                Mesh newMesh = AssetDatabase.LoadAssetAtPath<Mesh>(plan.sourceMeshPath);
                if (newMesh != null) mf.sharedMesh = newMesh;
            }

            SkinnedMeshRenderer smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && plan.updateSkinnedMeshRenderer)
            {
                Mesh newMesh = AssetDatabase.LoadAssetAtPath<Mesh>(plan.sourceMeshPath);
                if (newMesh != null) smr.sharedMesh = newMesh;
            }

            MeshCollider mc = target.GetComponent<MeshCollider>();
            if (mc != null && plan.updateMeshCollider)
            {
                Mesh newMesh = AssetDatabase.LoadAssetAtPath<Mesh>(plan.sourceMeshPath);
                if (newMesh != null) mc.sharedMesh = newMesh;
            }

            Debug.Log("Applied mesh plan to " + plan.targetObjectPath);
        }
    }
}
#endif